// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 14: DirectCompute (HLSL CS 5.0) Mega-Population DE Batch Evaluator
// High-Throughput Hardware Accelerated Multi-Leg Population Dynamics Loss Solver
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
struct GpuMegaPopParams {
    float central_mu;
    int pop_size;
    int leg_count;
    int gene_dim;

    float max_total_dv_kms;
    float penalty_base;
    float flyby_only;
    float launch_ceiling_kms;

    float vessel_budget_kms;
    float dsm_budget_kms;
    float quadratic_barrier_scale;
    float l1_dsm_scale;

    float earliest_dep_ut;
    float latest_dep_ut;
    float pad1;
    float pad2;
};

struct GpuBodyItem {
    float sma;
    float ecc;
    float inc;
    float raan;
    float aop;
    float ma0;
    float epoch_ut;
    float mu_self;
    float radius_km;
    float r_park_km;
    float rp_min_km;
    float pad;
};
#pragma pack(pop)

namespace {
    static std::mutex g_pop_gpu_mutex;
    static ID3D11Device* g_pop_d3d_device = nullptr;
    static ID3D11DeviceContext* g_pop_d3d_context = nullptr;
    static ID3D11ComputeShader* g_pop_compute_shader = nullptr;
    static bool g_pop_gpu_init_attempted = false;
    static bool g_pop_gpu_supported = false;

    // HLSL CS 5.0 超大规模种群批处理评估器
    static const char* g_mega_pop_hlsl_source = R"(
cbuffer Params : register(b0) {
    float central_mu;
    int pop_size;
    int leg_count;
    int gene_dim;

    float max_total_dv_kms;
    float penalty_base;
    float flyby_only;
    float launch_ceiling_kms;

    float vessel_budget_kms;
    float dsm_budget_kms;
    float quadratic_barrier_scale;
    float l1_dsm_scale;

    float earliest_dep_ut;
    float latest_dep_ut;
    float pad1;
    float pad2;
};

struct BodyItem {
    float sma;
    float ecc;
    float inc;
    float raan;
    float aop;
    float ma0;
    float epoch_ut;
    float mu_self;
    float radius_km;
    float r_park_km;
    float rp_min_km;
    float pad;
};

StructuredBuffer<BodyItem> InBodies : register(t0);
StructuredBuffer<float> InGenes : register(t1); // [pop_size * gene_dim]
RWStructuredBuffer<float> OutLoss : register(u0); // [pop_size]

static const float PI = 3.14159265358979323846f;
static const float TWO_PI = 6.28318530717958647692f;

void get_state(BodyItem b, float t_ut, float centralMu, out float3 out_r, out float3 out_v) {
    float n = sqrt(max(1e-12f, centralMu) / max(1e-6f, b.sma * b.sma * b.sma));
    float dt = t_ut - b.epoch_ut;
    float M = b.ma0 + n * dt;
    M = M - TWO_PI * floor(M / TWO_PI);

    float sinM = sin(M);
    float cosM = cos(M);
    float E = M + b.ecc * sinM + b.ecc * b.ecc * sinM * cosM;

    [unroll]
    for (int it = 0; it < 3; ++it) {
        float f = E - b.ecc * sin(E) - M;
        float f_dot = 1.0f - b.ecc * cos(E);
        E = E - f / max(1e-6f, f_dot);
    }

    float cosE = cos(E);
    float sinE = sin(E);
    float xp = b.sma * (cosE - b.ecc);
    float yp = b.sma * sqrt(max(0.0f, 1.0f - b.ecc * b.ecc)) * sinE;

    float r_orb = b.sma * (1.0f - b.ecc * cosE);
    float v_fac = sqrt(max(1e-12f, centralMu * b.sma)) / max(1e-3f, r_orb);
    float vxp = -v_fac * sinE;
    float vyp = v_fac * sqrt(max(0.0f, 1.0f - b.ecc * b.ecc)) * cosE;

    float cO = cos(b.raan), sO = sin(b.raan);
    float ci = cos(b.inc),  si = sin(b.inc);
    float co = cos(b.aop),  so = sin(b.aop);

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

    float x0 = (T >= T00) ? (-(T - T00) / (T - T00 + 4.0f)) : 0.0f;
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
        float step = delta * (DT * DT - 0.5f * delta * DDT) / (DT * (DT * DT - delta * DDT) + 1e-6f);
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

void stumpff(float z, out float c, out float s) {
    if (z > 1e-5f) {
        float sz = sqrt(z);
        c = (1.0f - cos(sz)) / z;
        s = (sz - sin(sz)) / (sz * z);
    } else if (z < -1e-5f) {
        float sz = sqrt(-z);
        c = (cosh(sz) - 1.0f) / (-z);
        s = (sinh(sz) - sz) / (sz * (-z));
    } else {
        c = 0.5f;
        s = 1.0f / 6.0f;
    }
}

bool propagate_kepler(float3 r0, float3 v0, float dt, float mu, out float3 out_r, out float3 out_v) {
    float r0_mag = length(r0);
    float v0_sq = dot(v0, v0);
    if (r0_mag < 1e-3f || mu <= 0.0f || dt <= 0.0f) {
        out_r = r0;
        out_v = v0;
        return false;
    }

    float alpha = 2.0f / r0_mag - v0_sq / mu;
    float sqrt_mu = sqrt(mu);
    float r0_v0 = dot(r0, v0);

    float chi = (abs(alpha) > 1e-4f) ? (sqrt_mu * dt * abs(alpha)) : (sqrt_mu * dt / r0_mag);

    [unroll]
    for (int it = 0; it < 5; ++it) {
        float chi2 = chi * chi;
        float z = alpha * chi2;
        float c, s;
        stumpff(z, c, s);

        float f_chi = (r0_v0 / sqrt_mu) * chi2 * c + (1.0f - alpha * r0_mag) * chi2 * chi * s + r0_mag * chi - sqrt_mu * dt;
        float f_dot = (r0_v0 / sqrt_mu) * chi * (1.0f - z * s) + (1.0f - alpha * r0_mag) * chi2 * c + r0_mag;

        float d_chi = f_chi / max(1e-6f, f_dot);
        chi = chi - clamp(d_chi, -0.5f * abs(chi) - 1.0f, 0.5f * abs(chi) + 1.0f);
        if (abs(d_chi) < 1e-4f) break;
    }

    float chi2 = chi * chi;
    float z = alpha * chi2;
    float c, s;
    stumpff(z, c, s);

    float f = 1.0f - (chi2 / r0_mag) * c;
    float g = dt - (chi2 * chi / sqrt_mu) * s;

    out_r = f * r0 + g * v0;
    float r_mag = length(out_r);
    if (r_mag < 1e-3f) {
        out_v = v0;
        return false;
    }

    float f_dot_val = (sqrt_mu / (r_mag * r0_mag)) * chi * (z * s - 1.0f);
    float g_dot_val = 1.0f - (chi2 / r_mag) * c;

    out_v = f_dot_val * r0 + g_dot_val * v0;
    return true;
}

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    uint indiv_idx = id.x;
    if (indiv_idx >= (uint)pop_size) return;

    int g_offset = indiv_idx * gene_dim;
    float t0 = InGenes[g_offset + 0];

    float cur_t = t0;
    float total_dv = 0.0f;
    float launch_dv = 0.0f;
    float total_dsm = 0.0f;
    float penalty = 0.0f;

    float3 prev_v_arr = float3(0, 0, 0);
    float3 prev_body_v = float3(0, 0, 0);

    bool is_1dsm = (gene_dim >= 1 + 2 * leg_count);

    for (int leg = 0; leg < leg_count; ++leg) {
        float tof_sec = InGenes[g_offset + 1 + leg];
        BodyItem bFrom = InBodies[leg];
        BodyItem bTo   = InBodies[leg + 1];

        float3 r1, v1;
        get_state(bFrom, cur_t, central_mu, r1, v1);

        float3 r2, v2;
        get_state(bTo, cur_t + tof_sec, central_mu, r2, v2);

        float3 vDep, vArr;

        if (!is_1dsm) {
            if (!solve_lambert(r1, r2, tof_sec, central_mu, vDep, vArr)) {
                penalty += 100000.0f;
                break;
            }
        } else {
            float eta = InGenes[g_offset + 1 + leg_count + leg];
            eta = clamp(eta, 0.10f, 0.90f);
            float t1 = eta * tof_sec;
            float t2 = (1.0f - eta) * tof_sec;

            float3 vDepInit;
            if (leg == 0) {
                float3 vL0, vL1;
                if (!solve_lambert(r1, r2, tof_sec, central_mu, vL0, vL1)) {
                    penalty += 100000.0f;
                    break;
                }
                vDepInit = vL0;
            } else {
                float3 vInfIn = prev_v_arr - prev_body_v;
                float vInMag = length(vInfIn);
                float3 vL0, vL1;
                solve_lambert(r1, r2, tof_sec, central_mu, vL0, vL1);
                float3 vInfOut = vL0 - v1;
                float vOutMag = length(vInfOut);

                if (vInMag > 1e-4f && vOutMag > 1e-4f) {
                    float dotP = clamp(dot(vInfIn, vInfOut) / (vInMag * vOutMag), -1.0f, 1.0f);
                    float reqTurn = acos(dotP);
                    float vRef = min(vInMag, vOutMag);
                    float denom = 1.0f + (bFrom.rp_min_km * 1000.0f * (vRef * 1000.0f) * (vRef * 1000.0f)) / max(1.0f, bFrom.mu_self * 1e9f);
                    float maxTurn = (denom <= 1.0f) ? PI : (2.0f * asin(clamp(1.0f / denom, 0.0f, 1.0f)));

                    if (reqTurn > maxTurn + 1e-4f) {
                        float excess = reqTurn - maxTurn;
                        total_dv += 2.0f * vRef * sin(excess * 0.5f) + abs(vInMag - vOutMag);
                        penalty += 2500.0f * excess * excess;
                    } else {
                        total_dv += abs(vInMag - vOutMag);
                    }
                }
                vDepInit = vL0;
            }

            float3 r_dsm, v_dsm_minus;
            if (!propagate_kepler(r1, vDepInit, t1, central_mu, r_dsm, v_dsm_minus)) {
                penalty += 100000.0f;
                break;
            }

            float3 v_dsm_plus, vArrLeg;
            if (!solve_lambert(r_dsm, r2, t2, central_mu, v_dsm_plus, vArrLeg)) {
                penalty += 100000.0f;
                break;
            }

            float3 dsm_vec = v_dsm_plus - v_dsm_minus;
            float dsm_mag = length(dsm_vec);
            total_dsm += dsm_mag;
            total_dv += dsm_mag;

            vDep = vDepInit;
            vArr = vArrLeg;
        }

        if (leg == 0) {
            float3 vInfDep = vDep - v1;
            float vHyp = sqrt(dot(vInfDep, vInfDep) + 2.0f * bFrom.mu_self / max(1.0f, bFrom.r_park_km));
            float vPark = sqrt(bFrom.mu_self / max(1.0f, bFrom.r_park_km));
            launch_dv = (vHyp - vPark);
            total_dv += launch_dv;
        } else if (!is_1dsm) {
            float3 vInfIn = prev_v_arr - prev_body_v;
            float3 vInfOut = vDep - v1;
            float vInMag = length(vInfIn);
            float vOutMag = length(vInfOut);

            if (vInMag > 1e-4f && vOutMag > 1e-4f) {
                float dotP = clamp(dot(vInfIn, vInfOut) / (vInMag * vOutMag), -1.0f, 1.0f);
                float reqTurn = acos(dotP);

                float vRef = min(vInMag, vOutMag);
                float denom = 1.0f + (bFrom.rp_min_km * 1000.0f * (vRef * 1000.0f) * (vRef * 1000.0f)) / max(1.0f, bFrom.mu_self * 1e9f);
                float maxTurn = (denom <= 1.0f) ? PI : (2.0f * asin(clamp(1.0f / denom, 0.0f, 1.0f)));

                if (reqTurn > maxTurn + 1e-4f) {
                    float excess = reqTurn - maxTurn;
                    total_dv += 2.0f * vRef * sin(excess * 0.5f) + abs(vInMag - vOutMag);
                    penalty += 2500.0f * excess * excess;
                } else {
                    total_dv += abs(vInMag - vOutMag);
                }
            }
        }

        if (leg == leg_count - 1 && flyby_only < 0.5f) {
            float3 vInfArr = vArr - v2;
            float vHyp = sqrt(dot(vInfArr, vInfArr) + 2.0f * bTo.mu_self / max(1.0f, bTo.r_park_km));
            float vPark = sqrt(bTo.mu_self / max(1.0f, bTo.r_park_km));
            total_dv += (vHyp - vPark);
        }

        prev_v_arr = vArr;
        prev_body_v = v2;
        cur_t += tof_sec;
    }

    // 连续二次平滑势垒惩罚与 L1 正则化 (与 CPU MGAPhysicsLossEngine.ComputeLoss 严格对齐)
    float barrier_scale = (quadratic_barrier_scale > 0.0f) ? quadratic_barrier_scale : 25000.0f;

    // 1. 发射时间窗边界平滑二次惩罚
    if (earliest_dep_ut > 0.0f && t0 < earliest_dep_ut) {
        float violation_days = (earliest_dep_ut - t0) / 86400.0f;
        penalty += violation_days * 100.0f + 0.5f * barrier_scale * (violation_days / 30.0f) * (violation_days / 30.0f);
    }
    if (latest_dep_ut > earliest_dep_ut && t0 > latest_dep_ut) {
        float violation_days = (t0 - latest_dep_ut) / 86400.0f;
        penalty += violation_days * 100.0f + 0.5f * barrier_scale * (violation_days / 30.0f) * (violation_days / 30.0f);
    }

    // 2. 发射上限平滑二次势垒
    if (launch_ceiling_kms > 0.0f && launch_dv > launch_ceiling_kms) {
        float over_launch = launch_dv - launch_ceiling_kms;
        penalty += barrier_scale * (over_launch + over_launch * over_launch);
    }

    // 3. 飞船总工质上限平滑二次势垒 (场景 1 或场景 2)
    float subject_dv = (flyby_only > 0.5f) ? (launch_dv + total_dsm) : total_dv;
    if (vessel_budget_kms > 0.0f && subject_dv > vessel_budget_kms) {
        float over_vessel = subject_dv - vessel_budget_kms;
        penalty += barrier_scale * (over_vessel + over_vessel * over_vessel);
    }

    // 4. DSM 预算上限平滑二次势垒与 L1 范数正则化
    if (dsm_budget_kms > 0.0f && total_dsm > dsm_budget_kms) {
        float over_dsm = total_dsm - dsm_budget_kms;
        penalty += barrier_scale * (over_dsm + 0.5f * over_dsm * over_dsm);
    }
    if (l1_dsm_scale > 0.0f) {
        penalty += l1_dsm_scale * total_dsm;
    }

    OutLoss[indiv_idx] = total_dv + penalty;
}
)";

    static bool init_mega_pop_d3d11_compute() {
        if (g_pop_gpu_init_attempted) return g_pop_gpu_supported;
        g_pop_gpu_init_attempted = true;

        D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        D3D_FEATURE_LEVEL featureLevel;

        HRESULT hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
            featureLevels, 2, D3D11_SDK_VERSION,
            &g_pop_d3d_device, &featureLevel, &g_pop_d3d_context);

        if (FAILED(hr) || !g_pop_d3d_device) {
            g_pop_gpu_supported = false;
            return false;
        }

        ID3DBlob* csBlob = nullptr;
        ID3DBlob* errorBlob = nullptr;
        hr = D3DCompile(
            g_mega_pop_hlsl_source, strlen(g_mega_pop_hlsl_source),
            "MegaPopCS", nullptr, nullptr,
            "CSMain", "cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0,
            &csBlob, &errorBlob);

        if (FAILED(hr)) {
            if (errorBlob) {
                fprintf(stderr, "[GPU MegaPop HLSL Error] %s\n", (const char*)errorBlob->GetBufferPointer());
                OutputDebugStringA((const char*)errorBlob->GetBufferPointer());
                errorBlob->Release();
            }
            if (g_pop_d3d_device) { g_pop_d3d_device->Release(); g_pop_d3d_device = nullptr; }
            if (g_pop_d3d_context) { g_pop_d3d_context->Release(); g_pop_d3d_context = nullptr; }
            g_pop_gpu_supported = false;
            return false;
        }

        hr = g_pop_d3d_device->CreateComputeShader(
            csBlob->GetBufferPointer(), csBlob->GetBufferSize(),
            nullptr, &g_pop_compute_shader);
        csBlob->Release();

        if (FAILED(hr) || !g_pop_compute_shader) {
            g_pop_gpu_supported = false;
            return false;
        }

        g_pop_gpu_supported = true;
        return true;
    }
}

/// <summary>
/// 超大规模 DE 种群纯 GPU 批处理动力学评估导出接口
/// </summary>
EXPORT_API int pykep_evaluate_population_gpu(
    const GpuPlanetElements* bodies,
    int body_count,
    const float* population_genes,
    int pop_size,
    int gene_dim,
    double central_mu,
    int flyby_only,
    float launch_ceiling_kms,
    float vessel_budget_kms,
    float dsm_budget_kms,
    float earliest_dep_ut,
    float latest_dep_ut,
    float* out_population_loss)
{
    if (!bodies || !population_genes || !out_population_loss || pop_size <= 0 || gene_dim <= 0 || body_count < 2) return -1;

    std::lock_guard<std::mutex> lock(g_pop_gpu_mutex);
    if (!init_mega_pop_d3d11_compute()) return -10;

    int leg_count = body_count - 1;

    // 1. 转换天体结构化数据
    std::vector<GpuBodyItem> bodyItems(body_count);
    for (int i = 0; i < body_count; ++i) {
        bodyItems[i].sma = (float)bodies[i].sma_km;
        bodyItems[i].ecc = (float)bodies[i].ecc;
        bodyItems[i].inc = (float)bodies[i].inc_rad;
        bodyItems[i].raan = (float)bodies[i].raan_rad;
        bodyItems[i].aop = (float)bodies[i].aop_rad;
        bodyItems[i].ma0 = (float)bodies[i].ma0_rad;
        bodyItems[i].epoch_ut = (float)bodies[i].epoch_ut;
        bodyItems[i].mu_self = (float)bodies[i].mu_self;
        bodyItems[i].radius_km = (float)bodies[i].radius_km;
        bodyItems[i].r_park_km = (float)(bodies[i].radius_km + bodies[i].park_alt_km);
        bodyItems[i].rp_min_km = (float)(bodies[i].radius_km + 50.0);
        bodyItems[i].pad = 0.0f;
    }

    // 2. 常量缓冲区
    GpuMegaPopParams cbData;
    cbData.central_mu = (float)central_mu;
    cbData.pop_size = pop_size;
    cbData.leg_count = leg_count;
    cbData.gene_dim = gene_dim;
    cbData.max_total_dv_kms = 10.0f;
    cbData.penalty_base = 500.0f;
    cbData.flyby_only = (flyby_only != 0) ? 1.0f : 0.0f;
    cbData.launch_ceiling_kms = launch_ceiling_kms;
    cbData.vessel_budget_kms = vessel_budget_kms;
    cbData.dsm_budget_kms = dsm_budget_kms;
    cbData.quadratic_barrier_scale = 25000.0f;
    cbData.l1_dsm_scale = 0.85f;
    cbData.earliest_dep_ut = earliest_dep_ut;
    cbData.latest_dep_ut = latest_dep_ut;
    cbData.pad1 = 0.0f;
    cbData.pad2 = 0.0f;

    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GpuMegaPopParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

    D3D11_SUBRESOURCE_DATA cbInit = { &cbData, 0, 0 };
    ID3D11Buffer* constBuffer = nullptr;
    if (FAILED(g_pop_d3d_device->CreateBuffer(&cbDesc, &cbInit, &constBuffer))) return -11;

    // 3. InBodies StructuredBuffer
    D3D11_BUFFER_DESC bDesc = {};
    bDesc.ByteWidth = body_count * sizeof(GpuBodyItem);
    bDesc.Usage = D3D11_USAGE_IMMUTABLE;
    bDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    bDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    bDesc.StructureByteStride = sizeof(GpuBodyItem);

    D3D11_SUBRESOURCE_DATA bInit = { bodyItems.data(), 0, 0 };
    ID3D11Buffer* bBuffer = nullptr;
    if (FAILED(g_pop_d3d_device->CreateBuffer(&bDesc, &bInit, &bBuffer))) {
        constBuffer->Release();
        return -12;
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC srvDescB = {};
    srvDescB.Format = DXGI_FORMAT_UNKNOWN;
    srvDescB.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
    srvDescB.Buffer.NumElements = body_count;

    ID3D11ShaderResourceView* srvBodies = nullptr;
    if (FAILED(g_pop_d3d_device->CreateShaderResourceView(bBuffer, &srvDescB, &srvBodies))) {
        bBuffer->Release();
        constBuffer->Release();
        return -13;
    }

    // 4. InGenes StructuredBuffer
    int geneTotalFloats = pop_size * gene_dim;
    D3D11_BUFFER_DESC gDesc = {};
    gDesc.ByteWidth = geneTotalFloats * sizeof(float);
    gDesc.Usage = D3D11_USAGE_IMMUTABLE;
    gDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    gDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    gDesc.StructureByteStride = sizeof(float);

    D3D11_SUBRESOURCE_DATA gInit = { population_genes, 0, 0 };
    ID3D11Buffer* gBuffer = nullptr;
    if (FAILED(g_pop_d3d_device->CreateBuffer(&gDesc, &gInit, &gBuffer))) {
        srvBodies->Release();
        bBuffer->Release();
        constBuffer->Release();
        return -14;
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC srvDescG = {};
    srvDescG.Format = DXGI_FORMAT_UNKNOWN;
    srvDescG.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
    srvDescG.Buffer.NumElements = geneTotalFloats;

    ID3D11ShaderResourceView* srvGenes = nullptr;
    if (FAILED(g_pop_d3d_device->CreateShaderResourceView(gBuffer, &srvDescG, &srvGenes))) {
        gBuffer->Release();
        srvBodies->Release();
        bBuffer->Release();
        constBuffer->Release();
        return -15;
    }

    // 5. OutLoss RWStructuredBuffer
    D3D11_BUFFER_DESC outDesc = {};
    outDesc.ByteWidth = pop_size * sizeof(float);
    outDesc.Usage = D3D11_USAGE_DEFAULT;
    outDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    outDesc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    outDesc.StructureByteStride = sizeof(float);

    ID3D11Buffer* outBuffer = nullptr;
    if (FAILED(g_pop_d3d_device->CreateBuffer(&outDesc, nullptr, &outBuffer))) {
        srvGenes->Release();
        gBuffer->Release();
        srvBodies->Release();
        bBuffer->Release();
        constBuffer->Release();
        return -16;
    }

    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_UNKNOWN;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uavDesc.Buffer.NumElements = pop_size;

    ID3D11UnorderedAccessView* uavLoss = nullptr;
    if (FAILED(g_pop_d3d_device->CreateUnorderedAccessView(outBuffer, &uavDesc, &uavLoss))) {
        outBuffer->Release();
        srvGenes->Release();
        gBuffer->Release();
        srvBodies->Release();
        bBuffer->Release();
        constBuffer->Release();
        return -17;
    }

    // 6. 调度执行
    ID3D11ShaderResourceView* srvs[2] = { srvBodies, srvGenes };
    g_pop_d3d_context->CSSetShader(g_pop_compute_shader, nullptr, 0);
    g_pop_d3d_context->CSSetConstantBuffers(0, 1, &constBuffer);
    g_pop_d3d_context->CSSetShaderResources(0, 2, srvs);
    g_pop_d3d_context->CSSetUnorderedAccessViews(0, 1, &uavLoss, nullptr);

    UINT groupsX = (pop_size + 63) / 64;
    g_pop_d3d_context->Dispatch(groupsX, 1, 1);

    // 解绑
    ID3D11UnorderedAccessView* nullUav = nullptr;
    ID3D11ShaderResourceView* nullSrvs[2] = { nullptr, nullptr };
    g_pop_d3d_context->CSSetUnorderedAccessViews(0, 1, &nullUav, nullptr);
    g_pop_d3d_context->CSSetShaderResources(0, 2, nullSrvs);

    // 7. 回读
    D3D11_BUFFER_DESC stageDesc = {};
    stageDesc.ByteWidth = pop_size * sizeof(float);
    stageDesc.Usage = D3D11_USAGE_STAGING;
    stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    ID3D11Buffer* stageBuffer = nullptr;
    if (FAILED(g_pop_d3d_device->CreateBuffer(&stageDesc, nullptr, &stageBuffer))) {
        uavLoss->Release();
        outBuffer->Release();
        srvGenes->Release();
        gBuffer->Release();
        srvBodies->Release();
        bBuffer->Release();
        constBuffer->Release();
        return -18;
    }

    g_pop_d3d_context->CopyResource(stageBuffer, outBuffer);

    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(g_pop_d3d_context->Map(stageBuffer, 0, D3D11_MAP_READ, 0, &mapped))) {
        memcpy(out_population_loss, mapped.pData, pop_size * sizeof(float));
        g_pop_d3d_context->Unmap(stageBuffer, 0);
    }

    stageBuffer->Release();
    uavLoss->Release();
    outBuffer->Release();
    srvGenes->Release();
    gBuffer->Release();
    srvBodies->Release();
    bBuffer->Release();
    constBuffer->Release();

    return 0; // 成功完成 GPU Mega-Population 批处理评估
}
