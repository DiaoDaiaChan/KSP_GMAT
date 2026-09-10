// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 15: DirectCompute (HLSL CS 5.0) Monte Carlo B-Plane Dispersion Solver
// High-Throughput Hardware Accelerated Orbit Uncertainty & Dispersion Analysis
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>

#include <string>
#include <vector>
#include <cmath>
#include <algorithm>
#include <mutex>

#pragma pack(push, 8)
struct GpuMonteCarloParams {
    float r0_x, r0_y, r0_z;
    float v0_x, v0_y, v0_z;
    float target_r_x, target_r_y, target_r_z;
    float target_v_x, target_v_y, target_v_z;

    float target_mu;
    float target_radius_km;
    float tof_sec;
    float central_mu;

    float sigma_pos_km;
    float sigma_vel_kms;
    int sample_count;
    float pad0;
};
#pragma pack(pop)

namespace {
    static std::mutex g_mc_gpu_mutex;
    static ID3D11Device* g_mc_d3d_device = nullptr;
    static ID3D11DeviceContext* g_mc_d3d_context = nullptr;
    static ID3D11ComputeShader* g_mc_compute_shader = nullptr;
    static bool g_mc_gpu_init_attempted = false;
    static bool g_mc_gpu_supported = false;

    // HLSL CS 5.0 蒙特卡洛 B 平面分散度模拟器
    static const char* g_bplane_mc_hlsl_source = R"(
cbuffer Params : register(b0) {
    float3 r0;
    float  pad_r0;
    float3 v0;
    float  pad_v0;
    float3 target_r;
    float  pad_tr;
    float3 target_v;
    float  pad_tv;

    float target_mu;
    float target_radius_km;
    float tof_sec;
    float central_mu;

    float sigma_pos_km;
    float sigma_vel_kms;
    int sample_count;
    float pad0;
};

struct BPlaneSample {
    float b_dot_r;
    float b_dot_t;
    float periapsis_alt_km;
    float pad;
};

RWStructuredBuffer<BPlaneSample> OutSamples : register(u0);

static const float TWO_PI = 6.28318530717958647692f;

// 快速轻量伪随机哈希
uint hash(uint s) {
    s ^= 2747636419u;
    s *= 2654435769u;
    s ^= s >> 16;
    s *= 2654435769u;
    s ^= s >> 16;
    s *= 2654435769u;
    return s;
}

float random_float(inout uint state) {
    state = hash(state);
    return float(state) / 4294967295.0f;
}

// Box-Muller 正态分布变换
float2 box_muller(inout uint state) {
    float u1 = max(1e-7f, random_float(state));
    float u2 = random_float(state);
    float r = sqrt(-2.0f * log(u1));
    float theta = TWO_PI * u2;
    return float2(r * cos(theta), r * sin(theta));
}

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    uint idx = id.x;
    if (idx >= (uint)sample_count) return;

    uint rngState = idx + 1013904223u;

    float2 g1 = box_muller(rngState);
    float2 g2 = box_muller(rngState);
    float2 g3 = box_muller(rngState);

    // 施加发控与速度噪声
    float3 pertR = float3(g1.x, g1.y, g2.x) * sigma_pos_km;
    float3 pertV = float3(g2.y, g3.x, g3.y) * sigma_vel_kms;

    float3 r_pert = r0 + pertR;
    float3 v_pert = v0 + pertV;

    // 两体到达线性状态预测 (双曲切面映射)
    float3 relR = (r_pert + v_pert * tof_sec) - target_r;
    float3 vInf = v_pert - target_v;
    float vInfMag = length(vInf);

    float3 S = vInf / max(1e-4f, vInfMag);
    float3 K = float3(0.0f, 0.0f, 1.0f); // 黄道极轴
    float3 T = normalize(cross(S, K));
    float3 R = cross(S, T);

    // 偏心率矢量与 B 平面脱靶距离
    float3 hVec = cross(relR, vInf);
    float hMag = length(hVec);
    float bMag = (hMag > 1e-4f && vInfMag > 1e-4f) ? (hMag / vInfMag) : 0.0f;

    float bdotR = dot(relR, R);
    float bdotT = dot(relR, T);

    // 双曲近拱距计算
    float muM = target_mu * 1e9f;
    float vInfM = vInfMag * 1000.0f;
    float cParam = muM / max(1e-3f, vInfM * vInfM);
    float bM = bMag * 1000.0f;
    float rpM = -cParam + sqrt(cParam * cParam + bM * bM);
    float rpKm = rpM / 1000.0f;
    float altKm = rpKm - target_radius_km;

    BPlaneSample outSample;
    outSample.b_dot_r = bdotR;
    outSample.b_dot_t = bdotT;
    outSample.periapsis_alt_km = altKm;
    outSample.pad = 0.0f;

    OutSamples[idx] = outSample;
}
)";

    static bool init_bplane_mc_d3d11_compute() {
        if (g_mc_gpu_init_attempted) return g_mc_gpu_supported;
        g_mc_gpu_init_attempted = true;

        D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        D3D_FEATURE_LEVEL featureLevel;

        HRESULT hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
            featureLevels, 2, D3D11_SDK_VERSION,
            &g_mc_d3d_device, &featureLevel, &g_mc_d3d_context);

        if (FAILED(hr) || !g_mc_d3d_device) {
            g_mc_gpu_supported = false;
            return false;
        }

        ID3DBlob* csBlob = nullptr;
        ID3DBlob* errorBlob = nullptr;
        hr = D3DCompile(
            g_bplane_mc_hlsl_source, strlen(g_bplane_mc_hlsl_source),
            "BPlaneMcCS", nullptr, nullptr,
            "CSMain", "cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
            &csBlob, &errorBlob);

        if (FAILED(hr)) {
            if (errorBlob) errorBlob->Release();
            if (g_mc_d3d_device) { g_mc_d3d_device->Release(); g_mc_d3d_device = nullptr; }
            if (g_mc_d3d_context) { g_mc_d3d_context->Release(); g_mc_d3d_context = nullptr; }
            g_mc_gpu_supported = false;
            return false;
        }

        hr = g_mc_d3d_device->CreateComputeShader(
            csBlob->GetBufferPointer(), csBlob->GetBufferSize(),
            nullptr, &g_mc_compute_shader);
        csBlob->Release();

        if (FAILED(hr) || !g_mc_compute_shader) {
            g_mc_gpu_supported = false;
            return false;
        }

        g_mc_gpu_supported = true;
        return true;
    }
}

/// <summary>
/// 蒙特卡洛轨道分散度与 B 平面协方差分析 GPU 导出接口
/// </summary>
EXPORT_API int pykep_bplane_monte_carlo_gpu_evaluate(
    const double* r0_km,
    const double* v0_kms,
    const double* target_r_km,
    const double* target_v_kms,
    double target_mu,
    double target_radius_km,
    double tof_sec,
    double central_mu,
    double sigma_pos_km,
    double sigma_vel_kms,
    int sample_count,
    float* out_mean_bdot_r,
    float* out_mean_bdot_t,
    float* out_sigma_rr,
    float* out_sigma_tt,
    float* out_sigma_rt,
    float* out_collision_prob)
{
    if (!r0_km || !v0_kms || !target_r_km || !target_v_kms || sample_count <= 0) return -1;

    std::lock_guard<std::mutex> lock(g_mc_gpu_mutex);
    if (!init_bplane_mc_d3d11_compute()) return -10;

    GpuMonteCarloParams cbData;
    cbData.r0_x = (float)r0_km[0]; cbData.r0_y = (float)r0_km[1]; cbData.r0_z = (float)r0_km[2];
    cbData.v0_x = (float)v0_kms[0]; cbData.v0_y = (float)v0_kms[1]; cbData.v0_z = (float)v0_kms[2];
    cbData.target_r_x = (float)target_r_km[0]; cbData.target_r_y = (float)target_r_km[1]; cbData.target_r_z = (float)target_r_km[2];
    cbData.target_v_x = (float)target_v_kms[0]; cbData.target_v_y = (float)target_v_kms[1]; cbData.target_v_z = (float)target_v_kms[2];

    cbData.target_mu = (float)target_mu;
    cbData.target_radius_km = (float)target_radius_km;
    cbData.tof_sec = (float)tof_sec;
    cbData.central_mu = (float)central_mu;

    cbData.sigma_pos_km = (float)sigma_pos_km;
    cbData.sigma_vel_kms = (float)sigma_vel_kms;
    cbData.sample_count = sample_count;
    cbData.pad0 = 0.0f;

    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GpuMonteCarloParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

    D3D11_SUBRESOURCE_DATA cbInit = { &cbData, 0, 0 };
    ID3D11Buffer* constBuffer = nullptr;
    if (FAILED(g_mc_d3d_device->CreateBuffer(&cbDesc, &cbInit, &constBuffer))) return -11;

    // 输出结构体大小: 4 * sizeof(float)
    int structSize = 4 * sizeof(float);
    D3D11_BUFFER_DESC outDesc = {};
    outDesc.ByteWidth = sample_count * structSize;
    outDesc.Usage = D3D11_USAGE_DEFAULT;
    outDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    outDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    outDesc.StructureByteStride = structSize;

    ID3D11Buffer* outBuffer = nullptr;
    if (FAILED(g_mc_d3d_device->CreateBuffer(&outDesc, nullptr, &outBuffer))) {
        constBuffer->Release();
        return -12;
    }

    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_UNKNOWN;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uavDesc.Buffer.NumElements = sample_count;

    ID3D11UnorderedAccessView* uav = nullptr;
    if (FAILED(g_mc_d3d_device->CreateUnorderedAccessView(outBuffer, &uavDesc, &uav))) {
        outBuffer->Release();
        constBuffer->Release();
        return -13;
    }

    g_mc_d3d_context->CSSetShader(g_mc_compute_shader, nullptr, 0);
    g_mc_d3d_context->CSSetConstantBuffers(0, 1, &constBuffer);
    g_mc_d3d_context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);

    UINT groupsX = (sample_count + 63) / 64;
    g_mc_d3d_context->Dispatch(groupsX, 1, 1);

    ID3D11UnorderedAccessView* nullUav = nullptr;
    g_mc_d3d_context->CSSetUnorderedAccessViews(0, 1, &nullUav, nullptr);

    // 回读
    D3D11_BUFFER_DESC stageDesc = {};
    stageDesc.ByteWidth = sample_count * structSize;
    stageDesc.Usage = D3D11_USAGE_STAGING;
    stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    ID3D11Buffer* stageBuffer = nullptr;
    if (FAILED(g_mc_d3d_device->CreateBuffer(&stageDesc, nullptr, &stageBuffer))) {
        uav->Release();
        outBuffer->Release();
        constBuffer->Release();
        return -14;
    }

    g_mc_d3d_context->CopyResource(stageBuffer, outBuffer);

    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(g_mc_d3d_context->Map(stageBuffer, 0, D3D11_MAP_READ, 0, &mapped))) {
        const float* raw = static_cast<const float*>(mapped.pData);

        double sumR = 0.0, sumT = 0.0;
        int collisionCount = 0;

        for (int i = 0; i < sample_count; ++i) {
            float br = raw[i * 4 + 0];
            float bt = raw[i * 4 + 1];
            float alt = raw[i * 4 + 2];
            sumR += br;
            sumT += bt;
            if (alt <= 0.0f) collisionCount++;
        }

        double meanR = sumR / sample_count;
        double meanT = sumT / sample_count;

        double sumSqR = 0.0, sumSqT = 0.0, sumRT = 0.0;
        for (int i = 0; i < sample_count; ++i) {
            double dR = raw[i * 4 + 0] - meanR;
            double dT = raw[i * 4 + 1] - meanT;
            sumSqR += dR * dR;
            sumSqT += dT * dT;
            sumRT  += dR * dT;
        }

        if (out_mean_bdot_r) *out_mean_bdot_r = (float)meanR;
        if (out_mean_bdot_t) *out_mean_bdot_t = (float)meanT;
        if (out_sigma_rr)    *out_sigma_rr = (float)std::sqrt(sumSqR / std::max(1, sample_count - 1));
        if (out_sigma_tt)    *out_sigma_tt = (float)std::sqrt(sumSqT / std::max(1, sample_count - 1));
        if (out_sigma_rt)    *out_sigma_rt = (float)(sumRT / std::max(1, sample_count - 1));
        if (out_collision_prob) *out_collision_prob = (float)collisionCount / sample_count;

        g_mc_d3d_context->Unmap(stageBuffer, 0);
    }

    stageBuffer->Release();
    uav->Release();
    outBuffer->Release();
    constBuffer->Release();

    return 0; // 成功完成 GPU 蒙特卡洛分散度评估
}
