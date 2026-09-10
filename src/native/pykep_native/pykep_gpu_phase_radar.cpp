// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 13: DirectCompute (HLSL CS 5.0) All-Planets Century Phase Radar Solver
// High-Throughput Hardware Accelerated Multi-Body Century Synodic Alignment
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
struct GpuRadarPairData {
    // 天体 A 轨道根数
    float a_sma;
    float a_ecc;
    float a_inc;
    float a_raan;
    float a_aop;
    float a_ma_start;

    // 天体 B 轨道根数
    float b_sma;
    float b_ecc;
    float b_inc;
    float b_raan;
    float b_aop;
    float b_ma_start;
};

struct GpuRadarParams {
    float start_ut;
    float step_days;
    float central_mu;
    int pair_count;
    int step_count;
    float pad0;
    float pad1;
    float pad2;
};
#pragma pack(pop)

namespace {
    static std::mutex g_radar_gpu_mutex;
    static ID3D11Device* g_radar_d3d_device = nullptr;
    static ID3D11DeviceContext* g_radar_d3d_context = nullptr;
    static ID3D11ComputeShader* g_radar_compute_shader = nullptr;
    static bool g_radar_gpu_init_attempted = false;
    static bool g_radar_gpu_supported = false;

    // HLSL CS 5.0 全天体百年相位雷达着色器
    static const char* g_radar_hlsl_source = R"(
cbuffer Params : register(b0) {
    float start_ut;
    float step_days;
    float central_mu;
    int pair_count;
    int step_count;
    float pad0;
    float pad1;
    float pad2;
};

struct PlanetPair {
    float a_sma;
    float a_ecc;
    float a_inc;
    float a_raan;
    float a_aop;
    float a_ma_start;

    float b_sma;
    float b_ecc;
    float b_inc;
    float b_raan;
    float b_aop;
    float b_ma_start;
};

StructuredBuffer<PlanetPair> InPairs : register(t0);
// 输出缓冲区: [pair_count * step_count], 存储两星日心相对相位角 (度数, [-180, 180])
RWStructuredBuffer<float> OutPhaseAngles : register(u0);

static const float PI = 3.14159265358979323846f;
static const float TWO_PI = 6.28318530717958647692f;
static const float RAD2DEG = 57.29577951308232f;

float3 get_body_pos(float sma, float ecc, float inc, float raan, float aop, float ma0, float mu, float dt_sec) {
    float n = sqrt(max(1e-12f, mu) / max(1e-6f, sma * sma * sma));
    float M = ma0 + n * dt_sec;
    M = M - TWO_PI * floor(M / TWO_PI);

    float sinM = sin(M);
    float cosM = cos(M);
    float E = M + ecc * sinM + ecc * ecc * sinM * cosM;

    [unroll]
    for (int it = 0; it < 3; ++it) {
        float f = E - ecc * sin(E) - M;
        float f_dot = 1.0f - ecc * cos(E);
        E = E - f / max(1e-6f, f_dot);
    }

    float cosE = cos(E);
    float sinE = sin(E);
    float xp = sma * (cosE - ecc);
    float yp = sma * sqrt(max(0.0f, 1.0f - ecc * ecc)) * sinE;

    float cO = cos(raan), sO = sin(raan);
    float ci = cos(inc),  si = sin(inc);
    float co = cos(aop),  so = sin(aop);

    float P11 = cO * co - sO * so * ci;
    float P12 = -cO * so - sO * co * ci;
    float P21 = sO * co + cO * so * ci;
    float P22 = -sO * so + cO * co * ci;
    float P31 = so * si;
    float P32 = co * si;

    return float3(P11 * xp + P12 * yp, P21 * xp + P22 * yp, P31 * xp + P32 * yp);
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    uint pair_idx = id.x;
    uint step_idx = id.y;

    if (pair_idx >= (uint)pair_count || step_idx >= (uint)step_count) return;

    PlanetPair p = InPairs[pair_idx];
    float dt_sec = step_idx * step_days * 86400.0f;

    float3 rA = get_body_pos(p.a_sma, p.a_ecc, p.a_inc, p.a_raan, p.a_aop, p.a_ma_start, central_mu, dt_sec);
    float3 rB = get_body_pos(p.b_sma, p.b_ecc, p.b_inc, p.b_raan, p.b_aop, p.b_ma_start, central_mu, dt_sec);

    // 计算黄道面相对相位角
    float thetaA = atan2(rA.y, rA.x);
    float thetaB = atan2(rB.y, rB.x);
    float dTheta = thetaB - thetaA;

    // 归一化到 [-PI, PI]
    dTheta = dTheta - TWO_PI * floor((dTheta + PI) / TWO_PI);

    int out_idx = step_idx * pair_count + pair_idx;
    OutPhaseAngles[out_idx] = dTheta * RAD2DEG;
}
)";

    static bool init_radar_d3d11_compute() {
        if (g_radar_gpu_init_attempted) return g_radar_gpu_supported;
        g_radar_gpu_init_attempted = true;

        D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        D3D_FEATURE_LEVEL featureLevel;

        HRESULT hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
            featureLevels, 2, D3D11_SDK_VERSION,
            &g_radar_d3d_device, &featureLevel, &g_radar_d3d_context);

        if (FAILED(hr) || !g_radar_d3d_device) {
            g_radar_gpu_supported = false;
            return false;
        }

        ID3DBlob* csBlob = nullptr;
        ID3DBlob* errorBlob = nullptr;
        hr = D3DCompile(
            g_radar_hlsl_source, strlen(g_radar_hlsl_source),
            "RadarCS", nullptr, nullptr,
            "CSMain", "cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
            &csBlob, &errorBlob);

        if (FAILED(hr)) {
            if (errorBlob) errorBlob->Release();
            if (g_radar_d3d_device) { g_radar_d3d_device->Release(); g_radar_d3d_device = nullptr; }
            if (g_radar_d3d_context) { g_radar_d3d_context->Release(); g_radar_d3d_context = nullptr; }
            g_radar_gpu_supported = false;
            return false;
        }

        hr = g_radar_d3d_device->CreateComputeShader(
            csBlob->GetBufferPointer(), csBlob->GetBufferSize(),
            nullptr, &g_radar_compute_shader);
        csBlob->Release();

        if (FAILED(hr) || !g_radar_compute_shader) {
            g_radar_gpu_supported = false;
            return false;
        }

        g_radar_gpu_supported = true;
        return true;
    }
}

/// <summary>
/// 全太阳系多天体百年历表与共振相位雷达图 GPU 计算导出接口
/// </summary>
EXPORT_API int pykep_ephemeris_radar_gpu_evaluate(
    const GpuPlanetElements* planets_a,
    const GpuPlanetElements* planets_b,
    int pair_count,
    double central_mu,
    double start_ut,
    double step_days,
    int step_count,
    float* out_phase_angles_deg)
{
    if (!planets_a || !planets_b || !out_phase_angles_deg || pair_count <= 0 || step_count <= 0) return -1;

    std::lock_guard<std::mutex> lock(g_radar_gpu_mutex);
    if (!init_radar_d3d11_compute()) return -10;

    int total_elements = pair_count * step_count;

    // 1. 组装输入行星对结构化数据
    std::vector<GpuRadarPairData> pairs(pair_count);
    for (int i = 0; i < pair_count; i++) {
        const auto& a = planets_a[i];
        const auto& b = planets_b[i];

        pairs[i].a_sma = (float)a.sma_km;
        pairs[i].a_ecc = (float)a.ecc;
        pairs[i].a_inc = (float)a.inc_rad;
        pairs[i].a_raan = (float)a.raan_rad;
        pairs[i].a_aop = (float)a.aop_rad;
        double na = std::sqrt(std::max(1e-12, central_mu) / std::max(1e-6, a.sma_km * a.sma_km * a.sma_km));
        pairs[i].a_ma_start = (float)(a.ma0_rad + na * (start_ut - a.epoch_ut));

        pairs[i].b_sma = (float)b.sma_km;
        pairs[i].b_ecc = (float)b.ecc;
        pairs[i].b_inc = (float)b.inc_rad;
        pairs[i].b_raan = (float)b.raan_rad;
        pairs[i].b_aop = (float)b.aop_rad;
        double nb = std::sqrt(std::max(1e-12, central_mu) / std::max(1e-6, b.sma_km * b.sma_km * b.sma_km));
        pairs[i].b_ma_start = (float)(b.ma0_rad + nb * (start_ut - b.epoch_ut));
    }

    // 2. 常量缓冲区
    GpuRadarParams cbData;
    cbData.start_ut = (float)start_ut;
    cbData.step_days = (float)step_days;
    cbData.central_mu = (float)central_mu;
    cbData.pair_count = pair_count;
    cbData.step_count = step_count;
    cbData.pad0 = cbData.pad1 = cbData.pad2 = 0.0f;

    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GpuRadarParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

    D3D11_SUBRESOURCE_DATA cbInit = { &cbData, 0, 0 };
    ID3D11Buffer* constBuffer = nullptr;
    if (FAILED(g_radar_d3d_device->CreateBuffer(&cbDesc, &cbInit, &constBuffer))) return -11;

    // 3. 输入 StructuredBuffer
    D3D11_BUFFER_DESC inSbDesc = {};
    inSbDesc.ByteWidth = pair_count * sizeof(GpuRadarPairData);
    inSbDesc.Usage = D3D11_USAGE_IMMUTABLE;
    inSbDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    inSbDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    inSbDesc.StructureByteStride = sizeof(GpuRadarPairData);

    D3D11_SUBRESOURCE_DATA inSbInit = { pairs.data(), 0, 0 };
    ID3D11Buffer* inBuffer = nullptr;
    if (FAILED(g_radar_d3d_device->CreateBuffer(&inSbDesc, &inSbInit, &inBuffer))) {
        constBuffer->Release();
        return -12;
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_UNKNOWN;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
    srvDesc.Buffer.FirstElement = 0;
    srvDesc.Buffer.NumElements = pair_count;

    ID3D11ShaderResourceView* inSrv = nullptr;
    if (FAILED(g_radar_d3d_device->CreateShaderResourceView(inBuffer, &srvDesc, &inSrv))) {
        inBuffer->Release();
        constBuffer->Release();
        return -13;
    }

    // 4. 输出 StructuredBuffer
    D3D11_BUFFER_DESC outSbDesc = {};
    outSbDesc.ByteWidth = total_elements * sizeof(float);
    outSbDesc.Usage = D3D11_USAGE_DEFAULT;
    outSbDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    outSbDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    outSbDesc.StructureByteStride = sizeof(float);

    ID3D11Buffer* outBuffer = nullptr;
    if (FAILED(g_radar_d3d_device->CreateBuffer(&outSbDesc, nullptr, &outBuffer))) {
        inSrv->Release();
        inBuffer->Release();
        constBuffer->Release();
        return -14;
    }

    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_UNKNOWN;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uavDesc.Buffer.FirstElement = 0;
    uavDesc.Buffer.NumElements = total_elements;

    ID3D11UnorderedAccessView* outUav = nullptr;
    if (FAILED(g_radar_d3d_device->CreateUnorderedAccessView(outBuffer, &uavDesc, &outUav))) {
        outBuffer->Release();
        inSrv->Release();
        inBuffer->Release();
        constBuffer->Release();
        return -15;
    }

    // 5. 调度计算
    g_radar_d3d_context->CSSetShader(g_radar_compute_shader, nullptr, 0);
    g_radar_d3d_context->CSSetConstantBuffers(0, 1, &constBuffer);
    g_radar_d3d_context->CSSetShaderResources(0, 1, &inSrv);
    g_radar_d3d_context->CSSetUnorderedAccessViews(0, 1, &outUav, nullptr);

    UINT groupsX = (pair_count + 15) / 16;
    UINT groupsY = (step_count + 15) / 16;
    g_radar_d3d_context->Dispatch(groupsX, groupsY, 1);

    // 解绑
    ID3D11UnorderedAccessView* nullUav = nullptr;
    ID3D11ShaderResourceView* nullSrv = nullptr;
    g_radar_d3d_context->CSSetUnorderedAccessViews(0, 1, &nullUav, nullptr);
    g_radar_d3d_context->CSSetShaderResources(0, 1, &nullSrv);

    // 6. 回读
    D3D11_BUFFER_DESC stageDesc = {};
    stageDesc.ByteWidth = total_elements * sizeof(float);
    stageDesc.Usage = D3D11_USAGE_STAGING;
    stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    ID3D11Buffer* stageBuffer = nullptr;
    if (FAILED(g_radar_d3d_device->CreateBuffer(&stageDesc, nullptr, &stageBuffer))) {
        outUav->Release();
        outBuffer->Release();
        inSrv->Release();
        inBuffer->Release();
        constBuffer->Release();
        return -16;
    }

    g_radar_d3d_context->CopyResource(stageBuffer, outBuffer);

    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(g_radar_d3d_context->Map(stageBuffer, 0, D3D11_MAP_READ, 0, &mapped))) {
        memcpy(out_phase_angles_deg, mapped.pData, total_elements * sizeof(float));
        g_radar_d3d_context->Unmap(stageBuffer, 0);
    }

    stageBuffer->Release();
    outUav->Release();
    outBuffer->Release();
    inSrv->Release();
    inBuffer->Release();
    constBuffer->Release();

    return 0; // 成功完成 GPU 百年相位雷达运算
}
