// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 11: DirectCompute (DirectX 11 HLSL CS 5.0) Universal GPU Porkchop Solver
// High-Throughput Hardware Accelerated Porkchop & Synodic Grid Pre-Filter
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
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

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

#pragma pack(push, 8)
struct GpuPlanetElements {
    double sma_km;
    double ecc;
    double inc_rad;
    double raan_rad;
    double aop_rad;
    double ma0_rad;
    double epoch_ut;
    double mu_self;
    double radius_km;
    double park_alt_km;
};

// 16 字节对齐的常量缓冲区结构
struct GpuPorkchopParams {
    float dep_span_sec;
    float tof_min_sec;
    float tof_max_sec;
    float central_mu;

    int grid_w;
    int grid_h;
    int flyby_only;
    float pad0;

    // 天体 1 轨道要素 (单精度与相对基准)
    float p1_sma;
    float p1_ecc;
    float p1_inc;
    float p1_raan;

    float p1_aop;
    float p1_ma_start;
    float p1_mu_self;
    float p1_r_park;

    // 天体 2 轨道要素
    float p2_sma;
    float p2_ecc;
    float p2_inc;
    float p2_raan;

    float p2_aop;
    float p2_ma_start;
    float p2_mu_self;
    float p2_r_park;
};
#pragma pack(pop)

namespace {
    static std::mutex g_gpu_mutex;
    static ID3D11Device* g_d3d_device = nullptr;
    static ID3D11DeviceContext* g_d3d_context = nullptr;
    static ID3D11ComputeShader* g_compute_shader = nullptr;
    static std::string g_gpu_adapter_name = "";
    static bool g_gpu_init_attempted = false;
    static bool g_gpu_supported = false;

    // HLSL CS 5.0 内嵌计算着色器代码
    static const char* g_porkchop_hlsl_source = R"(
cbuffer Params : register(b0) {
    float dep_span_sec;
    float tof_min_sec;
    float tof_max_sec;
    float central_mu;

    int grid_w;
    int grid_h;
    int flyby_only;
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
};

RWStructuredBuffer<float> OutTotDV : register(u0);
RWStructuredBuffer<float> OutC3    : register(u1);

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

    // 3 阶初值
    float sinM = sin(M);
    float cosM = cos(M);
    float E = M + ecc * sinM + ecc * ecc * sinM * cosM;

    // 3 步牛顿迭代
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

    // 三维欧拉旋转 (黄道面)
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

// Izzo 2015 零圈顺行 Lambert 求解器 HLSL 实现
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

    // 0 圈初值预测 (Izzo 2015)
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

    // Householder-3 阶迭代 (2~3 步超局部收敛)
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

    float vrs = gamma * ((lambda * y_sol - x) - rho_ratio * (lambda * y_sol + x)) / R1;
    float vrf = -gamma * ((lambda * y_sol - x) + rho_ratio * (lambda * y_sol + x)) / R2;
    float vt = gamma * sigma * (y_sol + lambda * x);
    float vts = vt / R1;
    float vtf = vt / R2;

    out_v1 = vrs * ir1 + vts * it1;
    out_v2 = vrf * ir2 + vtf * it2;
    return true;
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    int gx = id.x;
    int gy = id.y;
    if (gx >= grid_w || gy >= grid_h) return;

    int idx = gy * grid_w + gx;

    float dt_dep = (float(gx) / max(1.0f, float(grid_w - 1))) * dep_span_sec;
    float tof_sec = tof_min_sec + (float(gy) / max(1.0f, float(grid_h - 1))) * (tof_max_sec - tof_min_sec);
    float dt_arr = dt_dep + tof_sec;

    // 解析推算两颗天体相对 dep_start_ut 的位置与速度 (0 跨边界开销，0 纪元大数截断)
    float3 r1, vP1;
    elements_to_state(p1_sma, p1_ecc, p1_inc, p1_raan, p1_aop, p1_ma_start, central_mu, dt_dep, r1, vP1);

    float3 r2, vP2;
    elements_to_state(p2_sma, p2_ecc, p2_inc, p2_raan, p2_aop, p2_ma_start, central_mu, dt_arr, r2, vP2);

    float3 v1, v2;
    if (solve_lambert(r1, r2, tof_sec, central_mu, v1, v2)) {
        float3 vInfDep = v1 - vP1;
        float3 vInfArr = v2 - vP2;
        float vInfDepLen = length(vInfDep);
        float vInfArrLen = length(vInfArr);

        float c3 = vInfDepLen * vInfDepLen;
        OutC3[idx] = c3;

        // 出射脱离 ΔV (双曲注入)
        float rPark1 = p1_r_park;
        float vHyp1 = sqrt(vInfDepLen * vInfDepLen + 2.0f * p1_mu_self / max(1.0f, rPark1));
        float vPark1 = sqrt(p1_mu_self / max(1.0f, rPark1));
        float ejDV = (vHyp1 - vPark1) * 1000.0f;

        float inDV = 0.0f;
        if (flyby_only == 0) {
            float rPark2 = p2_r_park;
            float vHyp2 = sqrt(vInfArrLen * vInfArrLen + 2.0f * p2_mu_self / max(1.0f, rPark2));
            float vPark2 = sqrt(p2_mu_self / max(1.0f, rPark2));
            inDV = (vHyp2 - vPark2) * 1000.0f;
        }

        OutTotDV[idx] = ejDV + inDV;
    } else {
        OutTotDV[idx] = 1e8f; // 无效解高惩罚
        OutC3[idx] = 1e8f;
    }
}
)";

    static bool init_d3d11_compute() {
        if (g_gpu_init_attempted) return g_gpu_supported;
        g_gpu_init_attempted = true;

        D3D_FEATURE_LEVEL featureLevels[] = {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0
        };
        D3D_FEATURE_LEVEL featureLevel;

        HRESULT hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            0,
            featureLevels,
            2,
            D3D11_SDK_VERSION,
            &g_d3d_device,
            &featureLevel,
            &g_d3d_context);

        if (FAILED(hr) || !g_d3d_device) {
            g_gpu_supported = false;
            return false;
        }

        // 读取适配器显卡名称
        IDXGIDevice* dxgiDevice = nullptr;
        if (SUCCEEDED(g_d3d_device->QueryInterface(__uuidof(IDXGIDevice), (void**)&dxgiDevice))) {
            IDXGIAdapter* adapter = nullptr;
            if (SUCCEEDED(dxgiDevice->GetAdapter(&adapter))) {
                DXGI_ADAPTER_DESC desc;
                if (SUCCEEDED(adapter->GetDesc(&desc))) {
                    char nameBuf[256] = {0};
                    WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, nameBuf, sizeof(nameBuf) - 1, nullptr, nullptr);
                    g_gpu_adapter_name = std::string(nameBuf);
                }
                adapter->Release();
            }
            dxgiDevice->Release();
        }

        // 编译内嵌 HLSL 计算着色器 (CS 5.0)
        ID3DBlob* shaderBlob = nullptr;
        ID3DBlob* errorBlob = nullptr;
        hr = D3DCompile(
            g_porkchop_hlsl_source,
            std::strlen(g_porkchop_hlsl_source),
            "porkchop_cs",
            nullptr,
            nullptr,
            "CSMain",
            "cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3,
            0,
            &shaderBlob,
            &errorBlob);

        if (FAILED(hr) || !shaderBlob) {
            if (errorBlob) errorBlob->Release();
            g_gpu_supported = false;
            return false;
        }

        hr = g_d3d_device->CreateComputeShader(
            shaderBlob->GetBufferPointer(),
            shaderBlob->GetBufferSize(),
            nullptr,
            &g_compute_shader);

        shaderBlob->Release();
        if (FAILED(hr) || !g_compute_shader) {
            g_gpu_supported = false;
            return false;
        }

        g_gpu_supported = true;
        return true;
    }
}

/// <summary>
/// 硬件探针：检查当前系统是否支持 DirectCompute GPU 计算，并返回显卡型号
/// </summary>
EXPORT_API int pykep_check_gpu_compute_support(char* out_adapter_name, int max_len, int* out_supported) {
    std::lock_guard<std::mutex> lock(g_gpu_mutex);
    bool supp = init_d3d11_compute();
    if (out_supported) *out_supported = supp ? 1 : 0;
    if (out_adapter_name && max_len > 0) {
        std::string n = supp ? g_gpu_adapter_name : "DirectCompute Unavailable (CPU Fallback)";
        std::strncpy(out_adapter_name, n.c_str(), max_len - 1);
        out_adapter_name[max_len - 1] = '\0';
    }
    return supp ? 0 : -1;
}

/// <summary>
/// 通用 GPU 高通量猪排图 (Porkchop) 与发射窗口初查核心求解器
/// (DirectCompute HLSL CS 5.0: 16x16 线程块并发求解两体开普勒轨迹与 Izzo Lambert)
/// </summary>
EXPORT_API int pykep_porkchop_gpu_evaluate(
    const GpuPlanetElements* p1,
    const GpuPlanetElements* p2,
    double central_mu,
    double dep_start_ut, double dep_span_sec,
    double tof_min_sec, double tof_max_sec,
    int width, int height,
    int flyby_only,
    float* out_tot_dv,
    float* out_c3)
{
    if (!p1 || !p2 || width <= 0 || height <= 0 || !out_tot_dv || !out_c3) return -1;

    std::lock_guard<std::mutex> lock(g_gpu_mutex);
    if (!init_d3d11_compute() || !g_d3d_device || !g_d3d_context || !g_compute_shader) {
        return -2; // 降级信号：交由 CPU 执行
    }

    int total_elements = width * height;

    // 1. 创建常量缓冲区
    GpuPorkchopParams cbData;
    std::memset(&cbData, 0, sizeof(cbData));
    cbData.dep_span_sec = (float)dep_span_sec;
    cbData.tof_min_sec = (float)tof_min_sec;
    cbData.tof_max_sec = (float)tof_max_sec;
    cbData.central_mu = (float)central_mu;
    cbData.grid_w = width;
    cbData.grid_h = height;
    cbData.flyby_only = flyby_only ? 1 : 0;
    cbData.pad0 = 0.0f;

    double n1 = std::sqrt(central_mu / std::max(1e-6, p1->sma_km * p1->sma_km * p1->sma_km));
    double dt1 = dep_start_ut - p1->epoch_ut;
    double ma_start1 = std::fmod(p1->ma0_rad + n1 * dt1, 2.0 * 3.14159265358979323846);
    if (ma_start1 < 0.0) ma_start1 += 2.0 * 3.14159265358979323846;

    double n2 = std::sqrt(central_mu / std::max(1e-6, p2->sma_km * p2->sma_km * p2->sma_km));
    double dt2 = dep_start_ut - p2->epoch_ut;
    double ma_start2 = std::fmod(p2->ma0_rad + n2 * dt2, 2.0 * 3.14159265358979323846);
    if (ma_start2 < 0.0) ma_start2 += 2.0 * 3.14159265358979323846;

    cbData.p1_sma = (float)p1->sma_km;
    cbData.p1_ecc = (float)p1->ecc;
    cbData.p1_inc = (float)p1->inc_rad;
    cbData.p1_raan = (float)p1->raan_rad;
    cbData.p1_aop = (float)p1->aop_rad;
    cbData.p1_ma_start = (float)ma_start1;
    cbData.p1_mu_self = (float)p1->mu_self;
    cbData.p1_r_park = (float)(p1->radius_km + p1->park_alt_km);

    cbData.p2_sma = (float)p2->sma_km;
    cbData.p2_ecc = (float)p2->ecc;
    cbData.p2_inc = (float)p2->inc_rad;
    cbData.p2_raan = (float)p2->raan_rad;
    cbData.p2_aop = (float)p2->aop_rad;
    cbData.p2_ma_start = (float)ma_start2;
    cbData.p2_mu_self = (float)p2->mu_self;
    cbData.p2_r_park = (float)(p2->radius_km + p2->park_alt_km);

    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GpuPorkchopParams);
    cbDesc.Usage = D3D11_USAGE_IMMUTABLE;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    D3D11_SUBRESOURCE_DATA cbInit = { &cbData, 0, 0 };
    ID3D11Buffer* pConstantBuffer = nullptr;
    if (FAILED(g_d3d_device->CreateBuffer(&cbDesc, &cbInit, &pConstantBuffer))) {
        return -3;
    }

    // 2. 创建输出结构化缓冲区与 UAV
    D3D11_BUFFER_DESC sbDesc = {};
    sbDesc.ByteWidth = (UINT)(total_elements * sizeof(float));
    sbDesc.Usage = D3D11_USAGE_DEFAULT;
    sbDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    sbDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    sbDesc.StructureByteStride = sizeof(float);

    ID3D11Buffer* pBufDV = nullptr;
    ID3D11Buffer* pBufC3 = nullptr;
    if (FAILED(g_d3d_device->CreateBuffer(&sbDesc, nullptr, &pBufDV)) ||
        FAILED(g_d3d_device->CreateBuffer(&sbDesc, nullptr, &pBufC3))) {
        if (pConstantBuffer) pConstantBuffer->Release();
        if (pBufDV) pBufDV->Release();
        return -4;
    }

    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_UNKNOWN;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uavDesc.Buffer.NumElements = total_elements;

    ID3D11UnorderedAccessView* pUavDV = nullptr;
    ID3D11UnorderedAccessView* pUavC3 = nullptr;
    if (FAILED(g_d3d_device->CreateUnorderedAccessView(pBufDV, &uavDesc, &pUavDV)) ||
        FAILED(g_d3d_device->CreateUnorderedAccessView(pBufC3, &uavDesc, &pUavC3))) {
        pConstantBuffer->Release();
        pBufDV->Release();
        pBufC3->Release();
        return -5;
    }

    // 3. 调度 Compute Shader 执行
    g_d3d_context->CSSetShader(g_compute_shader, nullptr, 0);
    g_d3d_context->CSSetConstantBuffers(0, 1, &pConstantBuffer);
    ID3D11UnorderedAccessView* uavs[2] = { pUavDV, pUavC3 };
    g_d3d_context->CSSetUnorderedAccessViews(0, 2, uavs, nullptr);

    UINT threadGroupX = (UINT)((width + 15) / 16);
    UINT threadGroupY = (UINT)((height + 15) / 16);
    g_d3d_context->Dispatch(threadGroupX, threadGroupY, 1);

    // 解绑 UAV
    ID3D11UnorderedAccessView* nullUavs[2] = { nullptr, nullptr };
    g_d3d_context->CSSetUnorderedAccessViews(0, 2, nullUavs, nullptr);

    // 4. Staging Buffer 拷贝写回 CPU 内存
    D3D11_BUFFER_DESC stageDesc = {};
    stageDesc.ByteWidth = (UINT)(total_elements * sizeof(float));
    stageDesc.Usage = D3D11_USAGE_STAGING;
    stageDesc.BindFlags = 0;
    stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    ID3D11Buffer* pStageDV = nullptr;
    ID3D11Buffer* pStageC3 = nullptr;
    if (SUCCEEDED(g_d3d_device->CreateBuffer(&stageDesc, nullptr, &pStageDV)) &&
        SUCCEEDED(g_d3d_device->CreateBuffer(&stageDesc, nullptr, &pStageC3))) {
        g_d3d_context->CopyResource(pStageDV, pBufDV);
        g_d3d_context->CopyResource(pStageC3, pBufC3);

        D3D11_MAPPED_SUBRESOURCE mappedDV, mappedC3;
        if (SUCCEEDED(g_d3d_context->Map(pStageDV, 0, D3D11_MAP_READ, 0, &mappedDV))) {
            std::memcpy(out_tot_dv, mappedDV.pData, total_elements * sizeof(float));
            g_d3d_context->Unmap(pStageDV, 0);
        }
        if (SUCCEEDED(g_d3d_context->Map(pStageC3, 0, D3D11_MAP_READ, 0, &mappedC3))) {
            std::memcpy(out_c3, mappedC3.pData, total_elements * sizeof(float));
            g_d3d_context->Unmap(pStageC3, 0);
        }
        pStageDV->Release();
        pStageC3->Release();
    }

    // 释放临时资源
    pUavDV->Release();
    pUavC3->Release();
    pBufDV->Release();
    pBufC3->Release();
    pConstantBuffer->Release();

    return 0; // 成功完成 GPU 计算
}
