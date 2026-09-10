// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 12: DirectCompute (HLSL CS 5.0) Multi-Leg MGA 3D/4D Corridor Tensor Solver
// High-Throughput Hardware Accelerated 3-Body Gravity Assist Corridor Screening
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
// 16 字节对齐的三天体 MGA 张量常量缓冲区
struct GpuMgaTensorParams {
    float dep_span_sec;
    float tof1_min_sec;
    float tof1_max_sec;
    float tof2_min_sec;

    float tof2_max_sec;
    float central_mu;
    int grid_dep;
    int grid_tof1;

    int grid_tof2;
    int flyby_p2_mu;
    float p2_rp_min_km;
    float pad0;

    // 天体 1 (出发星)
    float p1_sma;
    float p1_ecc;
    float p1_inc;
    float p1_raan;
    float p1_aop;
    float p1_ma_start;
    float p1_mu_self;
    float p1_r_park;

    // 天体 2 (借力星)
    float p2_sma;
    float p2_ecc;
    float p2_inc;
    float p2_raan;
    float p2_aop;
    float p2_ma_start;
    float p2_mu_self;
    float p2_r_park;

    // 天体 3 (到达星)
    float p3_sma;
    float p3_ecc;
    float p3_inc;
    float p3_raan;
    float p3_aop;
    float p3_ma_start;
    float p3_mu_self;
    float p3_r_park;
};
#pragma pack(pop)

namespace {
    static std::mutex g_mga_gpu_mutex;
    static ID3D11Device* g_mga_d3d_device = nullptr;
    static ID3D11DeviceContext* g_mga_d3d_context = nullptr;
    static ID3D11ComputeShader* g_mga_compute_shader = nullptr;
    static bool g_mga_gpu_init_attempted = false;
    static bool g_mga_gpu_supported = false;

    // HLSL CS 5.0 三天体多腿张量扫描着色器
    static const char* g_mga_tensor_hlsl_source = R"(
cbuffer Params : register(b0) {
    float dep_span_sec;
    float tof1_min_sec;
    float tof1_max_sec;
    float tof2_min_sec;

    float tof2_max_sec;
    float central_mu;
    int grid_dep;
    int grid_tof1;

    int grid_tof2;
    int flyby_p2_mu;
    float p2_rp_min_km;
    float pad0;

    float p1_sma;
    float p1_ecc;
    float p1_inc;
    float p1_raan;
    float p1_aop;
    float p1_ma_start;
    float p1_mu_self;
    float p1_r_park;

    float p2_sma;
    float p2_ecc;
    float p2_inc;
    float p2_raan;
    float p2_aop;
    float p2_ma_start;
    float p2_mu_self;
    float p2_r_park;

    float p3_sma;
    float p3_ecc;
    float p3_inc;
    float p3_raan;
    float p3_aop;
    float p3_ma_start;
    float p3_mu_self;
    float p3_r_park;
};

// 输出缓冲区: 展平的 3D 张量 [grid_dep * grid_tof1 * grid_tof2]
RWStructuredBuffer<float> OutTensorDV : register(u0);

static const float PI = 3.14159265358979323846f;
static const float TWO_PI = 6.28318530717958647692f;

void elements_to_state(
    float sma, float ecc, float inc, float raan, float aop, float ma0,
    float mu_parent, float dt_sec,
    out float3 out_r, out float3 out_v)
{
    float n = sqrt(max(1e-12f, mu_parent) / max(1e-6f, sma * sma * sma));
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
    float r_orb = sma * (1.0f - ecc * cosE);

    float xp = sma * (cosE - ecc);
    float yp = sma * sqrt(max(0.0f, 1.0f - ecc * ecc)) * sinE;

    float v_fac = sqrt(max(1e-12f, mu_parent * sma)) / max(1e-3f, r_orb);
    float vxp = -v_fac * sinE;
    float vyp = v_fac * sqrt(max(0.0f, 1.0f - ecc * ecc)) * cosE;

    float cO = cos(raan), sO = sin(raan);
    float ci = cos(inc),  si = sin(inc);
    float co = cos(aop),  so = sin(aop);

    float P11 = cO * co - sO * so * ci;
    float P12 = -cO * so - sO * co * ci;
    float P21 = sO * co + cO * so * ci;
    float P22 = -sO * so + cO * co * ci;
    float P31 = so * si;
    float P32 = co * si;

    out_r = float3(P11 * xp + P12 * yp, P21 * xp + P22 * yp, P31 * xp + P32 * yp);
    out_v = float3(P11 * vxp + P12 * vyp, P21 * vxp + P22 * vyp, P31 * vxp + P32 * vyp);
}

bool solve_lambert(float3 r1, float3 r2, float tof, float mu, out float3 out_v1, out float3 out_v2) {
    out_v1 = float3(0, 0, 0);
    out_v2 = float3(0, 0, 0);
    if (tof <= 0.0f || mu <= 0.0f) return false;

    float3 d_r = r2 - r1;
    float c = length(d_r);
    float R1 = length(r1);
    float R2 = length(r2);
    if (R1 <= 1e-3f || R2 <= 1e-3f || c <= 1e-3f) return false;

    float s = (R1 + R2 + c) * 0.5f;
    float lambda2 = 1.0f - c / s;
    float lambda = sqrt(max(0.0f, lambda2));

    float3 ir1 = r1 / R1;
    float3 ir2 = r2 / R2;
    float3 ih = cross(ir1, ir2);
    float ih_len = length(ih);
    if (ih_len <= 1e-6f) return false;
    ih = ih / ih_len;

    float3 it1 = cross(ih, ir1);
    float3 it2 = cross(ih, ir2);

    if (ih.z < 0.0f) {
        lambda = -lambda;
        it1 = -it1;
        it2 = -it2;
    }

    float T = sqrt(2.0f * mu / max(1e-6f, s * s * s)) * tof;
    float T00 = acos(clamp(lambda, -1.0f, 1.0f)) + lambda * sqrt(max(0.0f, 1.0f - lambda2));
    float T1 = (2.0f / 3.0f) * (1.0f - lambda * lambda2);

    float x0 = 0.0f;
    if (T >= T00) {
        x0 = -(T - T00) / (T - T00 + 4.0f);
    } else if (T <= T1) {
        x0 = T1 * (T1 - T) / max(1e-6f, 0.4f * (1.0f - lambda2 * lambda * lambda2) * T) + 1.0f;
    } else {
        float p_idx = 0.693147f / log(max(1e-6f, T1 / T00));
        x0 = pow(max(1e-6f, T / T00), p_idx) - 1.0f;
    }

    float x = x0;
    [unroll]
    for (int iter = 0; iter < 3; ++iter) {
        float E = x * x - 1.0f;
        float rho = abs(E);
        float z = sqrt(max(0.0f, 1.0f + lambda2 * E));
        float y = sqrt(max(1e-12f, rho));
        float g = x * z - lambda * E;
        float d = (E < 0.0f) ? acos(clamp(g, -1.0f, 1.0f)) : log(max(1e-6f, y * (z - lambda * x) + g));
        float E_denom = (abs(E) > 1e-6f) ? E : (E >= 0.0f ? 1e-6f : -1e-6f);
        float tof_calc = (x - lambda * z - d / max(1e-6f, y)) / E_denom;

        float delta = tof_calc - T;
        float umx2 = max(1e-6f, 1.0f - x * x);
        float y_denom = sqrt(max(1e-6f, 1.0f - lambda2 * umx2));
        float DT = (3.0f * tof_calc * x - 2.0f + 2.0f * lambda * lambda2 * x / y_denom) / umx2;
        float DDT = (3.0f * tof_calc + 5.0f * x * DT + 2.0f * (1.0f - lambda2) * lambda * lambda2 / max(1e-6f, y_denom * y_denom * y_denom)) / umx2;

        float DT2 = DT * DT;
        float denom = DT * (DT2 - delta * DDT) + 1e-6f;
        float step = delta * (DT2 - 0.5f * delta * DDT) / denom;
        x = x - clamp(step, -0.3f, 0.3f);
    }

    float gamma = sqrt(max(0.0f, mu * s * 0.5f));
    float rho_ratio = (R1 - R2) / c;
    float sigma = sqrt(max(0.0f, 1.0f - rho_ratio * rho_ratio));
    float y_sol = sqrt(max(0.0f, 1.0f - lambda2 + lambda2 * x * x));

    float Vr1 = gamma * (y_sol * (sigma - lambda * rho_ratio) - x * (rho_ratio - lambda * sigma)) / R1;
    float Vr2 = -gamma * (y_sol * (sigma + lambda * rho_ratio) - x * (rho_ratio + lambda * sigma)) / R2;
    float Vt1 = gamma * (sigma + y_sol) * (1.0f + lambda) / R1;
    float Vt2 = gamma * (sigma + y_sol) * (1.0f + lambda) / R2;

    out_v1 = Vr1 * ir1 + Vt1 * it1;
    out_v2 = Vr2 * ir2 + Vt2 * it2;
    return true;
}

[numthreads(8, 8, 4)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    if (id.x >= (uint)grid_dep || id.y >= (uint)grid_tof1 || id.z >= (uint)grid_tof2) return;

    int flat_idx = (id.z * grid_tof1 + id.y) * grid_dep + id.x;

    float dt_dep = (grid_dep > 1) ? ((float)id.x / (grid_dep - 1) * dep_span_sec) : 0.0f;
    float tof1 = (grid_tof1 > 1) ? (tof1_min_sec + (float)id.y / (grid_tof1 - 1) * (tof1_max_sec - tof1_min_sec)) : tof1_min_sec;
    float tof2 = (grid_tof2 > 1) ? (tof2_min_sec + (float)id.z / (grid_tof2 - 1) * (tof2_max_sec - tof2_min_sec)) : tof2_min_sec;

    // 1. 三体状态展开
    float3 r1, v1;
    elements_to_state(p1_sma, p1_ecc, p1_inc, p1_raan, p1_aop, p1_ma_start, central_mu, dt_dep, r1, v1);

    float3 r2, v2;
    elements_to_state(p2_sma, p2_ecc, p2_inc, p2_raan, p2_aop, p2_ma_start, central_mu, dt_dep + tof1, r2, v2);

    float3 r3, v3;
    elements_to_state(p3_sma, p3_ecc, p3_inc, p3_raan, p3_aop, p3_ma_start, central_mu, dt_dep + tof1 + tof2, r3, v3);

    // 2. 双段 Lambert 求解
    float3 vDep1, vArr1;
    if (!solve_lambert(r1, r2, tof1, central_mu, vDep1, vArr1)) {
        OutTensorDV[flat_idx] = 1e8f;
        return;
    }

    float3 vDep2, vArr2;
    if (!solve_lambert(r2, r3, tof2, central_mu, vDep2, vArr2)) {
        OutTensorDV[flat_idx] = 1e8f;
        return;
    }

    // 3. 中间天体 P2 飞越双曲偏折几何判定
    float3 vInfIn = vArr1 - v2;
    float3 vInfOut = vDep2 - v2;
    float vInMag = length(vInfIn);
    float vOutMag = length(vInfOut);

    if (vInMag < 1e-4f || vOutMag < 1e-4f) {
        OutTensorDV[flat_idx] = 1e8f;
        return;
    }

    float dotVal = clamp(dot(vInfIn, vInfOut) / (vInMag * vOutMag), -1.0f, 1.0f);
    float reqDeflect = acos(dotVal);

    float vRef = min(vInMag, vOutMag);
    float p2_mu_m = p2_mu_self * 1e9f;
    float p2_rp_m = p2_rp_min_km * 1000.0f;
    float denom = 1.0f + (p2_rp_m * (vRef * 1000.0f) * (vRef * 1000.0f)) / max(1.0f, p2_mu_m);
    float maxDeflect = (denom <= 1.0f) ? PI : (2.0f * asin(clamp(1.0f / denom, 0.0f, 1.0f)));

    // 飞掠偏转角赤字动力学惩罚
    float flybyDeltaV = 0.0f;
    if (reqDeflect > maxDeflect + 1e-4f) {
        float excess = reqDeflect - maxDeflect;
        flybyDeltaV = (2.0f * vRef * sin(excess * 0.5f) + abs(vInMag - vOutMag)) * 1000.0f;
    } else {
        flybyDeltaV = abs(vInMag - vOutMag) * 1000.0f; // 仅需抹平入流/离流速度标量差
    }

    // 始发出射 Delta V
    float3 vInfDep1 = vDep1 - v1;
    float vInfDepLen = length(vInfDep1);
    float rPark1 = p1_r_park;
    float vHyp1 = sqrt(vInfDepLen * vInfDepLen + 2.0f * p1_mu_self / max(1.0f, rPark1));
    float vPark1 = sqrt(p1_mu_self / max(1.0f, rPark1));
    float launchDV = (vHyp1 - vPark1) * 1000.0f;

    // 目标捕获 Delta V
    float3 vInfArr2 = vArr2 - v3;
    float vInfArrLen = length(vInfArr2);
    float rPark3 = p3_r_park;
    float vHyp3 = sqrt(vInfArrLen * vInfArrLen + 2.0f * p3_mu_self / max(1.0f, rPark3));
    float vPark3 = sqrt(p3_mu_self / max(1.0f, rPark3));
    float captureDV = (vHyp3 - vPark3) * 1000.0f;

    float totalDV = launchDV + flybyDeltaV + captureDV;
    OutTensorDV[flat_idx] = totalDV;
}
)";

    static bool init_mga_d3d11_compute() {
        if (g_mga_gpu_init_attempted) return g_mga_gpu_supported;
        g_mga_gpu_init_attempted = true;

        D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        D3D_FEATURE_LEVEL featureLevel;

        HRESULT hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
            featureLevels, 2, D3D11_SDK_VERSION,
            &g_mga_d3d_device, &featureLevel, &g_mga_d3d_context);

        if (FAILED(hr) || !g_mga_d3d_device) {
            g_mga_gpu_supported = false;
            return false;
        }

        ID3DBlob* csBlob = nullptr;
        ID3DBlob* errorBlob = nullptr;
        hr = D3DCompile(
            g_mga_tensor_hlsl_source, strlen(g_mga_tensor_hlsl_source),
            "MgaTensorCS", nullptr, nullptr,
            "CSMain", "cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
            &csBlob, &errorBlob);

        if (FAILED(hr)) {
            if (errorBlob) errorBlob->Release();
            if (g_mga_d3d_device) { g_mga_d3d_device->Release(); g_mga_d3d_device = nullptr; }
            if (g_mga_d3d_context) { g_mga_d3d_context->Release(); g_mga_d3d_context = nullptr; }
            g_mga_gpu_supported = false;
            return false;
        }

        hr = g_mga_d3d_device->CreateComputeShader(
            csBlob->GetBufferPointer(), csBlob->GetBufferSize(),
            nullptr, &g_mga_compute_shader);
        csBlob->Release();

        if (FAILED(hr) || !g_mga_compute_shader) {
            g_mga_gpu_supported = false;
            return false;
        }

        g_mga_gpu_supported = true;
        return true;
    }
}

/// <summary>
/// 多腿 MGA 3D 张量极速走廊初筛导出接口
/// </summary>
EXPORT_API int pykep_mga_corridor_gpu_evaluate(
    const GpuPlanetElements* p1,
    const GpuPlanetElements* p2,
    const GpuPlanetElements* p3,
    double central_mu,
    double dep_start_ut,
    double dep_span_sec,
    double tof1_min_sec,
    double tof1_max_sec,
    double tof2_min_sec,
    double tof2_max_sec,
    int grid_dep,
    int grid_tof1,
    int grid_tof2,
    double p2_rp_min_km,
    float* out_best_tot_dv,
    float* out_best_tof1_days,
    float* out_best_tof2_days)
{
    if (!p1 || !p2 || !p3 || !out_best_tot_dv || !out_best_tof1_days || !out_best_tof2_days) return -1;
    if (grid_dep <= 0 || grid_tof1 <= 0 || grid_tof2 <= 0) return -2;

    std::lock_guard<std::mutex> lock(g_mga_gpu_mutex);
    if (!init_mga_d3d11_compute()) return -10;

    int total_threads = grid_dep * grid_tof1 * grid_tof2;

    GpuMgaTensorParams cbData;
    memset(&cbData, 0, sizeof(cbData));
    cbData.dep_span_sec = (float)dep_span_sec;
    cbData.tof1_min_sec = (float)tof1_min_sec;
    cbData.tof1_max_sec = (float)tof1_max_sec;
    cbData.tof2_min_sec = (float)tof2_min_sec;
    cbData.tof2_max_sec = (float)tof2_max_sec;
    cbData.central_mu = (float)central_mu;
    cbData.grid_dep = grid_dep;
    cbData.grid_tof1 = grid_tof1;
    cbData.grid_tof2 = grid_tof2;
    cbData.p2_rp_min_km = (float)p2_rp_min_km;

    // 天体 1
    cbData.p1_sma = (float)p1->sma_km;
    cbData.p1_ecc = (float)p1->ecc;
    cbData.p1_inc = (float)p1->inc_rad;
    cbData.p1_raan = (float)p1->raan_rad;
    cbData.p1_aop = (float)p1->aop_rad;
    double n1 = std::sqrt(std::max(1e-12, central_mu) / std::max(1e-6, p1->sma_km * p1->sma_km * p1->sma_km));
    cbData.p1_ma_start = (float)(p1->ma0_rad + n1 * (dep_start_ut - p1->epoch_ut));
    cbData.p1_mu_self = (float)p1->mu_self;
    cbData.p1_r_park = (float)(p1->radius_km + p1->park_alt_km);

    // 天体 2
    cbData.p2_sma = (float)p2->sma_km;
    cbData.p2_ecc = (float)p2->ecc;
    cbData.p2_inc = (float)p2->inc_rad;
    cbData.p2_raan = (float)p2->raan_rad;
    cbData.p2_aop = (float)p2->aop_rad;
    double n2 = std::sqrt(std::max(1e-12, central_mu) / std::max(1e-6, p2->sma_km * p2->sma_km * p2->sma_km));
    cbData.p2_ma_start = (float)(p2->ma0_rad + n2 * (dep_start_ut - p2->epoch_ut));
    cbData.p2_mu_self = (float)p2->mu_self;
    cbData.p2_r_park = (float)(p2->radius_km + p2->park_alt_km);

    // 天体 3
    cbData.p3_sma = (float)p3->sma_km;
    cbData.p3_ecc = (float)p3->ecc;
    cbData.p3_inc = (float)p3->inc_rad;
    cbData.p3_raan = (float)p3->raan_rad;
    cbData.p3_aop = (float)p3->aop_rad;
    double n3 = std::sqrt(std::max(1e-12, central_mu) / std::max(1e-6, p3->sma_km * p3->sma_km * p3->sma_km));
    cbData.p3_ma_start = (float)(p3->ma0_rad + n3 * (dep_start_ut - p3->epoch_ut));
    cbData.p3_mu_self = (float)p3->mu_self;
    cbData.p3_r_park = (float)(p3->radius_km + p3->park_alt_km);

    // 1. 常量缓冲区创建与填充
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GpuMgaTensorParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

    D3D11_SUBRESOURCE_DATA cbInit = { &cbData, 0, 0 };
    ID3D11Buffer* constBuffer = nullptr;
    if (FAILED(g_mga_d3d_device->CreateBuffer(&cbDesc, &cbInit, &constBuffer))) return -11;

    // 2. 结构化输出缓冲区
    D3D11_BUFFER_DESC sbDesc = {};
    sbDesc.ByteWidth = total_threads * sizeof(float);
    sbDesc.Usage = D3D11_USAGE_DEFAULT;
    sbDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    sbDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    sbDesc.StructureByteStride = sizeof(float);

    ID3D11Buffer* gpuBuffer = nullptr;
    if (FAILED(g_mga_d3d_device->CreateBuffer(&sbDesc, nullptr, &gpuBuffer))) {
        constBuffer->Release();
        return -12;
    }

    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_UNKNOWN;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uavDesc.Buffer.FirstElement = 0;
    uavDesc.Buffer.NumElements = total_threads;

    ID3D11UnorderedAccessView* uav = nullptr;
    if (FAILED(g_mga_d3d_device->CreateUnorderedAccessView(gpuBuffer, &uavDesc, &uav))) {
        gpuBuffer->Release();
        constBuffer->Release();
        return -13;
    }

    // 3. 调度计算着色器
    g_mga_d3d_context->CSSetShader(g_mga_compute_shader, nullptr, 0);
    g_mga_d3d_context->CSSetConstantBuffers(0, 1, &constBuffer);
    g_mga_d3d_context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);

    UINT groupsX = (grid_dep + 7) / 8;
    UINT groupsY = (grid_tof1 + 7) / 8;
    UINT groupsZ = (grid_tof2 + 3) / 4;
    g_mga_d3d_context->Dispatch(groupsX, groupsY, groupsZ);

    // 解绑 UAV 准备回读
    ID3D11UnorderedAccessView* nullUav = nullptr;
    g_mga_d3d_context->CSSetUnorderedAccessViews(0, 1, &nullUav, nullptr);

    // 4. Staging Buffer 回读
    D3D11_BUFFER_DESC stageDesc = {};
    stageDesc.ByteWidth = total_threads * sizeof(float);
    stageDesc.Usage = D3D11_USAGE_STAGING;
    stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    ID3D11Buffer* stageBuffer = nullptr;
    if (FAILED(g_mga_d3d_device->CreateBuffer(&stageDesc, nullptr, &stageBuffer))) {
        uav->Release();
        gpuBuffer->Release();
        constBuffer->Release();
        return -14;
    }

    g_mga_d3d_context->CopyResource(stageBuffer, gpuBuffer);

    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(g_mga_d3d_context->Map(stageBuffer, 0, D3D11_MAP_READ, 0, &mapped))) {
        const float* rawDv = static_cast<const float*>(mapped.pData);

        // 提取每个出发日的最优波谷 (ToF1, ToF2) 坐标
        for (int x = 0; x < grid_dep; x++) {
            float minDv = 1e9f;
            float bestTof1D = (float)(tof1_min_sec / 86400.0);
            float bestTof2D = (float)(tof2_min_sec / 86400.0);

            for (int z = 0; z < grid_tof2; z++) {
                float t2Sec = (grid_tof2 > 1) ? (float)(tof2_min_sec + (double)z / (grid_tof2 - 1) * (tof2_max_sec - tof2_min_sec)) : (float)tof2_min_sec;
                for (int y = 0; y < grid_tof1; y++) {
                    int idx = (z * grid_tof1 + y) * grid_dep + x;
                    float dv = rawDv[idx];
                    if (dv < minDv) {
                        minDv = dv;
                        float t1Sec = (grid_tof1 > 1) ? (float)(tof1_min_sec + (double)y / (grid_tof1 - 1) * (tof1_max_sec - tof1_min_sec)) : (float)tof1_min_sec;
                        bestTof1D = t1Sec / 86400.0f;
                        bestTof2D = t2Sec / 86400.0f;
                    }
                }
            }

            out_best_tot_dv[x] = minDv;
            out_best_tof1_days[x] = bestTof1D;
            out_best_tof2_days[x] = bestTof2D;
        }

        g_mga_d3d_context->Unmap(stageBuffer, 0);
    }

    stageBuffer->Release();
    uav->Release();
    gpuBuffer->Release();
    constBuffer->Release();

    return 0; // 成功完成 GPU 多腿张量计算
}
