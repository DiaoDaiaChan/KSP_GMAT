// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 16: DirectCompute (HLSL CS 5.0) Low-Thrust Sims-Flanagan Match-Point Tensor Solver
// High-Throughput Hardware Accelerated Low-Thrust Segment Evaluation
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
struct GpuSimsFlanaganParams {
    float r1_x, r1_y, r1_z;
    float v1_x, v1_y, v1_z;
    float r2_x, r2_y, r2_z;
    float v2_x, v2_y, v2_z;

    float tof_sec;
    float central_mu;
    int segment_count;
    int candidate_count;

    float max_thrust_accel;
    float pad0;
    float pad1;
    float pad2;
};
#pragma pack(pop)

namespace {
    static std::mutex g_sf_gpu_mutex;
    static ID3D11Device* g_sf_d3d_device = nullptr;
    static ID3D11DeviceContext* g_sf_d3d_context = nullptr;
    static ID3D11ComputeShader* g_sf_compute_shader = nullptr;
    static bool g_sf_gpu_init_attempted = false;
    static bool g_sf_gpu_supported = false;

    // HLSL CS 5.0 低推力 Sims-Flanagan 求解器
    static const char* g_sims_flanagan_hlsl_source = R"(
cbuffer Params : register(b0) {
    float3 r1;
    float  pad_r1;
    float3 v1;
    float  pad_v1;
    float3 r2;
    float  pad_r2;
    float3 v2;
    float  pad_v2;

    float tof_sec;
    float central_mu;
    int segment_count;
    int candidate_count;

    float max_thrust_accel;
    float pad0;
    float pad1;
    float pad2;
};

// 候选推力冲量输入缓冲区: [candidate_count * segment_count * 3]
StructuredBuffer<float3> InImpulses : register(t0);
// 候选解残差匹配损失输出: [candidate_count]
RWStructuredBuffer<float> OutMismatchLoss : register(u0);

// 两体引力步进 (开普勒两体显式积分近似)
void kepler_step(inout float3 r, inout float3 v, float dt, float mu) {
    float r_len = length(r);
    float3 acc = -mu * r / max(1e-3f, r_len * r_len * r_len);
    r = r + v * dt + 0.5f * acc * dt * dt;
    float3 r_next_len = length(r);
    float3 acc_next = -mu * r / max(1e-3f, r_next_len * r_next_len * r_next_len);
    v = v + 0.5f * (acc + acc_next) * dt;
}

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    uint cand_idx = id.x;
    if (cand_idx >= (uint)candidate_count) return;

    int half_segs = segment_count / 2;
    float dt_seg = tof_sec / max(1, segment_count);

    // 1. 正向推演 (Forward Propagation from r1, v1)
    float3 cur_r_fwd = r1;
    float3 cur_v_fwd = v1;
    float total_dv = 0.0f;

    int base_impulse_idx = cand_idx * segment_count;

    for (int i = 0; i < half_segs; ++i) {
        float3 dv = InImpulses[base_impulse_idx + i];
        total_dv += length(dv);
        cur_v_fwd += dv;
        kepler_step(cur_r_fwd, cur_v_fwd, dt_seg, central_mu);
    }

    // 2. 反向推演 (Backward Propagation from r2, v2)
    float3 cur_r_bwd = r2;
    float3 cur_v_bwd = v2;

    for (int j = segment_count - 1; j >= half_segs; --j) {
        kepler_step(cur_r_bwd, cur_v_bwd, -dt_seg, central_mu);
        float3 dv = InImpulses[base_impulse_idx + j];
        total_dv += length(dv);
        cur_v_bwd -= dv;
    }

    // 3. Match Point 差异评估
    float3 dr = cur_r_fwd - cur_r_bwd;
    float3 dv = cur_v_fwd - cur_v_bwd;

    float rMismatchKm = length(dr);
    float vMismatchKms = length(dv);

    // 综合残差损失 (位置误差千米 * 0.001 + 速度残差 km/s * 100 + 总消耗)
    float loss = rMismatchKm * 0.001f + vMismatchKms * 100.0f + total_dv;
    OutMismatchLoss[cand_idx] = loss;
}
)";

    static bool init_sf_d3d11_compute() {
        if (g_sf_gpu_init_attempted) return g_sf_gpu_supported;
        g_sf_gpu_init_attempted = true;

        D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        D3D_FEATURE_LEVEL featureLevel;

        HRESULT hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
            featureLevels, 2, D3D11_SDK_VERSION,
            &g_sf_d3d_device, &featureLevel, &g_sf_d3d_context);

        if (FAILED(hr) || !g_sf_d3d_device) {
            g_sf_gpu_supported = false;
            return false;
        }

        ID3DBlob* csBlob = nullptr;
        ID3DBlob* errorBlob = nullptr;
        hr = D3DCompile(
            g_sims_flanagan_hlsl_source, strlen(g_sims_flanagan_hlsl_source),
            "SimsFlanaganCS", nullptr, nullptr,
            "CSMain", "cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
            &csBlob, &errorBlob);

        if (FAILED(hr)) {
            if (errorBlob) errorBlob->Release();
            if (g_sf_d3d_device) { g_sf_d3d_device->Release(); g_sf_d3d_device = nullptr; }
            if (g_sf_d3d_context) { g_sf_d3d_context->Release(); g_sf_d3d_context = nullptr; }
            g_sf_gpu_supported = false;
            return false;
        }

        hr = g_sf_d3d_device->CreateComputeShader(
            csBlob->GetBufferPointer(), csBlob->GetBufferSize(),
            nullptr, &g_sf_compute_shader);
        csBlob->Release();

        if (FAILED(hr) || !g_sf_compute_shader) {
            g_sf_gpu_supported = false;
            return false;
        }

        g_sf_gpu_supported = true;
        return true;
    }
}

/// <summary>
/// 低推力 Sims-Flanagan 离散脉冲段张量推演导出接口
/// </summary>
EXPORT_API int pykep_sims_flanagan_gpu_evaluate(
    const double* r1_km,
    const double* v1_kms,
    const double* r2_km,
    const double* v2_kms,
    double tof_sec,
    double central_mu,
    int segment_count,
    int candidate_count,
    const float* impulses_xyz,
    float* out_mismatch_loss)
{
    if (!r1_km || !v1_kms || !r2_km || !v2_kms || !impulses_xyz || !out_mismatch_loss || segment_count < 2 || candidate_count <= 0) return -1;

    std::lock_guard<std::mutex> lock(g_sf_gpu_mutex);
    if (!init_sf_d3d11_compute()) return -10;

    GpuSimsFlanaganParams cbData;
    cbData.r1_x = (float)r1_km[0]; cbData.r1_y = (float)r1_km[1]; cbData.r1_z = (float)r1_km[2];
    cbData.v1_x = (float)v1_kms[0]; cbData.v1_y = (float)v1_kms[1]; cbData.v1_z = (float)v1_kms[2];
    cbData.r2_x = (float)r2_km[0]; cbData.r2_y = (float)r2_km[1]; cbData.r2_z = (float)r2_km[2];
    cbData.v2_x = (float)v2_kms[0]; cbData.v2_y = (float)v2_kms[1]; cbData.v2_z = (float)v2_kms[2];

    cbData.tof_sec = (float)tof_sec;
    cbData.central_mu = (float)central_mu;
    cbData.segment_count = segment_count;
    cbData.candidate_count = candidate_count;
    cbData.max_thrust_accel = 1e-3f;
    cbData.pad0 = cbData.pad1 = cbData.pad2 = 0.0f;

    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GpuSimsFlanaganParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

    D3D11_SUBRESOURCE_DATA cbInit = { &cbData, 0, 0 };
    ID3D11Buffer* constBuffer = nullptr;
    if (FAILED(g_sf_d3d_device->CreateBuffer(&cbDesc, &cbInit, &constBuffer))) return -11;

    // 输入脉冲 StructuredBuffer: candidate_count * segment_count * sizeof(float3)
    int totalImpulses = candidate_count * segment_count;
    int float3Size = 3 * sizeof(float);
    D3D11_BUFFER_DESC inDesc = {};
    inDesc.ByteWidth = totalImpulses * float3Size;
    inDesc.Usage = D3D11_USAGE_IMMUTABLE;
    inDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    inDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    inDesc.StructureByteStride = float3Size;

    D3D11_SUBRESOURCE_DATA inInit = { impulses_xyz, 0, 0 };
    ID3D11Buffer* inBuffer = nullptr;
    if (FAILED(g_sf_d3d_device->CreateBuffer(&inDesc, &inInit, &inBuffer))) {
        constBuffer->Release();
        return -12;
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_UNKNOWN;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
    srvDesc.Buffer.NumElements = totalImpulses;

    ID3D11ShaderResourceView* inSrv = nullptr;
    if (FAILED(g_sf_d3d_device->CreateShaderResourceView(inBuffer, &srvDesc, &inSrv))) {
        inBuffer->Release();
        constBuffer->Release();
        return -13;
    }

    // 输出 StructuredBuffer
    D3D11_BUFFER_DESC outDesc = {};
    outDesc.ByteWidth = candidate_count * sizeof(float);
    outDesc.Usage = D3D11_USAGE_DEFAULT;
    outDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    outDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    outDesc.StructureByteStride = sizeof(float);

    ID3D11Buffer* outBuffer = nullptr;
    if (FAILED(g_sf_d3d_device->CreateBuffer(&outDesc, nullptr, &outBuffer))) {
        inSrv->Release();
        inBuffer->Release();
        constBuffer->Release();
        return -14;
    }

    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_UNKNOWN;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uavDesc.Buffer.NumElements = candidate_count;

    ID3D11UnorderedAccessView* uav = nullptr;
    if (FAILED(g_sf_d3d_device->CreateUnorderedAccessView(outBuffer, &uavDesc, &uav))) {
        outBuffer->Release();
        inSrv->Release();
        inBuffer->Release();
        constBuffer->Release();
        return -15;
    }

    g_sf_d3d_context->CSSetShader(g_sf_compute_shader, nullptr, 0);
    g_sf_d3d_context->CSSetConstantBuffers(0, 1, &constBuffer);
    g_sf_d3d_context->CSSetShaderResources(0, 1, &inSrv);
    g_sf_d3d_context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);

    UINT groupsX = (candidate_count + 63) / 64;
    g_sf_d3d_context->Dispatch(groupsX, 1, 1);

    ID3D11UnorderedAccessView* nullUav = nullptr;
    ID3D11ShaderResourceView* nullSrv = nullptr;
    g_sf_d3d_context->CSSetUnorderedAccessViews(0, 1, &nullUav, nullptr);
    g_sf_d3d_context->CSSetShaderResources(0, 1, &nullSrv);

    // 回读
    D3D11_BUFFER_DESC stageDesc = {};
    stageDesc.ByteWidth = candidate_count * sizeof(float);
    stageDesc.Usage = D3D11_USAGE_STAGING;
    stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    ID3D11Buffer* stageBuffer = nullptr;
    if (FAILED(g_sf_d3d_device->CreateBuffer(&stageDesc, nullptr, &stageBuffer))) {
        uav->Release();
        outBuffer->Release();
        inSrv->Release();
        inBuffer->Release();
        constBuffer->Release();
        return -16;
    }

    g_sf_d3d_context->CopyResource(stageBuffer, outBuffer);

    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(g_sf_d3d_context->Map(stageBuffer, 0, D3D11_MAP_READ, 0, &mapped))) {
        memcpy(out_mismatch_loss, mapped.pData, candidate_count * sizeof(float));
        g_sf_d3d_context->Unmap(stageBuffer, 0);
    }

    stageBuffer->Release();
    uav->Release();
    outBuffer->Release();
    inSrv->Release();
    inBuffer->Release();
    constBuffer->Release();

    return 0; // 成功完成 GPU Sims-Flanagan 评估
}
