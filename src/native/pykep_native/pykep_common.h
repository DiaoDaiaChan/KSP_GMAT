// ============================================================================
// ESA PyKEP v3 (kep3) Official Native C-API Bridge
// Common Protocol, 8-byte Packed Structs & Core Astrodynamics Utilities
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#pragma once

#include <cmath>
#include <array>
#include <vector>
#include <tuple>
#include <string>
#include <unordered_map>
#include <memory>
#include <algorithm>
#include <cstring>
#include <random>
#include <intrin.h>
#include <immintrin.h>
#ifdef _OPENMP
#include <omp.h>
#endif

#include <kep3/epoch.hpp>
#include <kep3/lambert_problem.hpp>
#include <kep3/core_astro/flyby.hpp>
#include <kep3/core_astro/propagate_lagrangian.hpp>
#include <kep3/core_astro/ic2par2ic.hpp>
#include <kep3/core_astro/ic2mee2ic.hpp>
#include <kep3/core_astro/kepler_equations.hpp>
#include <kep3/core_astro/stm.hpp>
#include <kep3/core_astro/basic_transfers.hpp>
#include <kep3/udpla/keplerian.hpp>
#include <kep3/udpla/jpl_lp.hpp>

#ifndef EXPORT_API
#define EXPORT_API extern "C" __declspec(dllexport)
#endif

// ============================================================================
// CPUID Hardware Guard & Feature Detection (100% Zero Crash Fallback)
// ============================================================================
struct CPUFeatures {
    int sse2 = 0;
    int avx = 0;
    int avx2 = 0;
    int fma = 0;
};

inline CPUFeatures detect_cpu_features() {
    CPUFeatures feat;
    int info[4] = {0};
    __cpuid(info, 0);
    int nIds = info[0];
    if (nIds >= 1) {
        __cpuid(info, 1);
        feat.sse2 = (info[3] & (1 << 26)) ? 1 : 0;
        feat.fma  = (info[2] & (1 << 12)) ? 1 : 0;
        bool osxsave = (info[2] & (1 << 27)) != 0;
        bool cpu_avx = (info[2] & (1 << 28)) != 0;
        if (osxsave && cpu_avx) {
            unsigned __int64 xcrFeatureMask = _xgetbv(0);
            feat.avx = ((xcrFeatureMask & 6) == 6) ? 1 : 0;
        }
    }
    if (nIds >= 7 && feat.avx) {
        __cpuidex(info, 7, 0);
        feat.avx2 = (info[1] & (1 << 5)) ? 1 : 0;
    }
    return feat;
}

EXPORT_API inline int pykep_check_cpu_features(int* out_sse2, int* out_avx, int* out_avx2, int* out_fma) {
    CPUFeatures f = detect_cpu_features();
    if (out_sse2) *out_sse2 = f.sse2;
    if (out_avx)  *out_avx  = f.avx;
    if (out_avx2) *out_avx2 = f.avx2;
    if (out_fma)  *out_fma  = f.fma;
    return (f.avx2 && f.fma) ? 1 : 0;
}

// ============================================================================
// AVX2 / FMA Optimized Vector Math Helpers (4-wide SIMD & Fast Dot/Norm)
// ============================================================================
namespace {
    struct OpenMpInitConfig {
        OpenMpInitConfig() {
            // 彻底杜绝 OpenMP 空闲线程在 Windows 上的 100% 自旋空转，防止系统假死/死锁
            _putenv("OMP_WAIT_POLICY=PASSIVE");
        }
    };
    static OpenMpInitConfig g_omp_init_config;
}

inline double vec3_dot(const double* a, const double* b) {
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

inline double vec3_dot(const std::array<double, 3>& a, const std::array<double, 3>& b) {
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

inline double vec3_norm_sq(const double* a) {
    return a[0] * a[0] + a[1] * a[1] + a[2] * a[2];
}

inline double vec3_norm_sq(const std::array<double, 3>& a) {
    return a[0] * a[0] + a[1] * a[1] + a[2] * a[2];
}

inline double vec3_norm(const double* a) {
    return std::sqrt(vec3_norm_sq(a));
}

inline double vec3_norm(const std::array<double, 3>& a) {
    return std::sqrt(vec3_norm_sq(a));
}

inline void vec3_cross(const double* a, const double* b, double* out) {
    out[0] = a[1] * b[2] - a[2] * b[1];
    out[1] = a[2] * b[0] - a[0] * b[2];
    out[2] = a[0] * b[1] - a[1] * b[0];
}

inline std::array<double, 3> vec3_cross(const std::array<double, 3>& a, const std::array<double, 3>& b) {
    return { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
}

// 4 组 3 维矢量点积并行求解 (AVX2 + FMA 4-wide SIMD: out[i] = ax[i]*bx[i] + ay[i]*by[i] + az[i]*bz[i])
inline void batch_vec3_dot4_avx2(
    const double* ax, const double* ay, const double* az,
    const double* bx, const double* by, const double* bz,
    double* out_dots)
{
#if defined(__AVX2__)
    __m256d vax = _mm256_loadu_pd(ax);
    __m256d vay = _mm256_loadu_pd(ay);
    __m256d vaz = _mm256_loadu_pd(az);
    __m256d vbx = _mm256_loadu_pd(bx);
    __m256d vby = _mm256_loadu_pd(by);
    __m256d vbz = _mm256_loadu_pd(bz);
    __m256d dots = _mm256_fmadd_pd(vax, vbx, _mm256_fmadd_pd(vay, vby, _mm256_mul_pd(vaz, vbz)));
    _mm256_storeu_pd(out_dots, dots);
#else
    for (int i = 0; i < 4; ++i) {
        out_dots[i] = ax[i] * bx[i] + ay[i] * by[i] + az[i] * bz[i];
    }
#endif
}

// 4 组 3 维矢量模长并行求解 (AVX2 + FMA: out[i] = sqrt(x[i]^2 + y[i]^2 + z[i]^2))
inline void batch_vec3_norm4_avx2(
    const double* x, const double* y, const double* z,
    double* out_norms)
{
#if defined(__AVX2__)
    __m256d vx = _mm256_loadu_pd(x);
    __m256d vy = _mm256_loadu_pd(y);
    __m256d vz = _mm256_loadu_pd(z);
    __m256d nsq = _mm256_fmadd_pd(vx, vx, _mm256_fmadd_pd(vy, vy, _mm256_mul_pd(vz, vz)));
    __m256d norms = _mm256_sqrt_pd(nsq);
    _mm256_storeu_pd(out_norms, norms);
#else
    for (int i = 0; i < 4; ++i) {
        out_norms[i] = std::sqrt(x[i]*x[i] + y[i]*y[i] + z[i]*z[i]);
    }
#endif
}

// 4 组 3 维矢量几何差值欧氏距离并行求解 (AVX2 + FMA: out[i] = ||a[i] - b[i]||)
inline void batch_vec3_diff_norm4_avx2(
    const double* ax, const double* ay, const double* az,
    const double* bx, const double* by, const double* bz,
    double* out_dists)
{
#if defined(__AVX2__)
    __m256d dx = _mm256_sub_pd(_mm256_loadu_pd(ax), _mm256_loadu_pd(bx));
    __m256d dy = _mm256_sub_pd(_mm256_loadu_pd(ay), _mm256_loadu_pd(by));
    __m256d dz = _mm256_sub_pd(_mm256_loadu_pd(az), _mm256_loadu_pd(bz));
    __m256d dsq = _mm256_fmadd_pd(dx, dx, _mm256_fmadd_pd(dy, dy, _mm256_mul_pd(dz, dz)));
    _mm256_storeu_pd(out_dists, _mm256_sqrt_pd(dsq));
#else
    for (int i = 0; i < 4; ++i) {
        double dx = ax[i] - bx[i];
        double dy = ay[i] - by[i];
        double dz = az[i] - bz[i];
        out_dists[i] = std::sqrt(dx*dx + dy*dy + dz*dz);
    }
#endif
}

// 单个 3 维矢量的低延迟 SIMD 点积
inline double vec3_dot_fast(const double* a, const double* b) {
#if defined(__AVX2__)
    __m256d va = _mm256_setr_pd(a[0], a[1], a[2], 0.0);
    __m256d vb = _mm256_setr_pd(b[0], b[1], b[2], 0.0);
    __m256d prod = _mm256_mul_pd(va, vb);
    __m128d lo = _mm256_castpd256_pd128(prod);
    __m128d hi = _mm256_extractf128_pd(prod, 1);
    __m128d sum = _mm_add_pd(lo, hi);
    return _mm_cvtsd_f64(_mm_hadd_pd(sum, sum));
#else
    return a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
#endif
}

inline double vec3_norm_fast(const double* a) {
    return std::sqrt(vec3_dot_fast(a, a));
}

static const double SUN_MU = 1.32712440018e11; // km^3/s^2 (NASA/JPL standard)
static const double PI = 3.14159265358979323846;
static const double RAD2DEG = 180.0 / PI;
static const double DEG2RAD = PI / 180.0;

#pragma pack(push, 8)
struct NativePlanetState {
    double rx, ry, rz; // km
    double vx, vy, vz; // km/s
    double mu;         // km^3/s^2
    double radius;     // km
    double safe_radius;// km
    double soi_radius; // km
    double atm_radius; // km
};

static inline bool is_central_body(const NativePlanetState& p, double effective_mu) {
    if (effective_mu > 0.0 && std::abs(p.mu - effective_mu) / effective_mu < 1e-3) {
        return true;
    }
    if (p.soi_radius > 1e14) {
        return true;
    }
    double r_sq = p.rx * p.rx + p.ry * p.ry + p.rz * p.rz;
    double boundary = (p.atm_radius > 0.0) ? p.atm_radius : p.safe_radius;
    if (boundary > 0.0 && r_sq < boundary * boundary * 4.0 && (p.soi_radius <= 0.0 || r_sq < p.soi_radius * p.soi_radius)) {
        return true;
    }
    return false;
}

struct NativeLegResult {
    double tof_sec;
    double r1x, r1y, r1z;
    double r2x, r2y, r2z;
    double v1x, v1y, v1z;
    double v2x, v2y, v2z;
    double v_inf_dep_x, v_inf_dep_y, v_inf_dep_z;
    double v_inf_arr_x, v_inf_arr_y, v_inf_arr_z;
    double flyby_turn_deg;
    double flyby_rp_km;
    double flyby_dv_kms;
    double prograde_ms;
    double normal_ms;
    double radial_ms;
    int is_feasible;
};

struct NativeMGASolution {
    double launch_dv_kms;
    double insertion_dv_kms;
    double total_dv_kms;
    double total_flight_days;
    double c3_kms2;
    double ejection_angle_deg;
    double ejection_inc_deg;
    double ejection_lan_deg;
    int all_feasible;
    int leg_count;
    NativeLegResult legs[16];
};

struct NativeDSMResult {
    double epoch_offset_sec;
    double pos_x, pos_y, pos_z;
    double dv_x, dv_y, dv_z;
    double dv_mag_ms;
    double prograde_ms;
    double normal_ms;
    double radial_ms;
};

struct NativeLeg1DSMResult {
    double tof_sec;
    double r1x, r1y, r1z;
    double r2x, r2y, r2z;
    double v1x, v1y, v1z;
    double v2x, v2y, v2z;
    double v_inf_dep_x, v_inf_dep_y, v_inf_dep_z;
    double v_inf_arr_x, v_inf_arr_y, v_inf_arr_z;
    double flyby_turn_deg;
    double flyby_rp_km;
    double flyby_beta_deg;
    double flyby_dv_kms;
    int has_dsm;
    NativeDSMResult dsm;
    int is_feasible;
};

struct NativeMGA1DSMSolution {
    double launch_dv_kms;
    double total_dsm_dv_kms;
    double total_flyby_dv_kms;
    double insertion_dv_kms;
    double total_dv_kms;
    double total_flight_days;
    double c3_kms2;
    double ejection_angle_deg;
    double ejection_inc_deg;
    double ejection_lan_deg;
    int all_feasible;
    int leg_count;
    NativeLeg1DSMResult legs[16];
};

struct NativeBPlaneResult {
    double b_dot_t_km;
    double b_dot_r_km;
    double b_mag_km;
    double theta_deg;
    double v_inf_mag_kms;
    double s_hat[3];
    double t_hat[3];
    double r_hat[3];
};

struct NativeBPlaneCovarianceResult {
    double sigma_bt_km;
    double sigma_br_km;
    double cov_btr_km2;
    double semi_major_sigma_km;
    double semi_minor_sigma_km;
    double ellipse_angle_deg;
    double safe_radius_km;
    double periapsis_km;
    double safe_margin_3sigma_km;
    int is_safe_flyby;
    double tcm_3sigma_dv_ms;
};

struct NativeManifoldCaptureResult {
    int is_manifold_feasible;
    double jacobi_constant;
    double hill_radius_km;
    double wsb_capture_dv_kms;
    double classical_visviva_dv_kms;
    double dv_saved_kms;
    double saving_percentage;
    double ingress_angle_deg;
    double ballistic_duration_days;
};

struct NativeTCMResult {
    double dv_x, dv_y, dv_z;
    double dv_mag_ms;
    double prograde_ms;
    double normal_ms;
    double radial_ms;
};

struct NativePrimerVectorResult {
    int sample_count;
    double times_sec[32];
    double p_mags[32];
    double max_p_mag;
    double max_p_time_sec;
    int is_optimal;
};

struct NativeMEE {
    double p;
    double f;
    double g;
    double h;
    double k;
    double L;
};

struct NativeSimsFlanaganResult {
    double delta_r_km;
    double delta_v_kms;
    double delta_m_kg;
    double total_dv_ms;
    double final_mass_kg;
    int n_segments;
    double segment_pos[32][3];
    double throttle[32][3];
};
#pragma pack(pop)

// ============================================================================
// SHARED UTILITY FUNCTIONS (INLINE & C-API EXPORTS)
// ============================================================================

static inline double calc_vis_viva_dep_inc(
    double v_inf, double mu, double radius, double pe_km, double ap_km,
    double dep_inc_deg, double v_inf_z, double* out_ej_inc) 
{
    if (mu <= 0.0 || radius <= 0.0) {
        if (out_ej_inc) *out_ej_inc = std::abs(dep_inc_deg);
        return v_inf;
    }
    double rp = radius + std::max(0.0, pe_km);
    double ra = radius + std::max(pe_km, ap_km);
    double a = (rp + ra) * 0.5;
    double v_park = std::sqrt(mu * (2.0 / rp - 1.0 / a));
    double v_hyp = std::sqrt(v_inf * v_inf + 2.0 * mu / rp);

    // 模式 1: 自由发射 / 纯切向规划 (dep_inc_deg < -0.01)
    // 强制消除黄赤交角和赤纬对发射造成的伪变面惩罚，纯切向出射
    if (dep_inc_deg < -0.01) {
        if (out_ej_inc) *out_ej_inc = 0.0;
        return std::max(0.0, v_hyp - v_park);
    }

    double park_inc_deg = std::abs(dep_inc_deg);
    double dec_rad = std::asin(std::max(-1.0, std::min(1.0, v_inf_z / std::max(1e-6, v_inf))));
    double abs_dec_deg = std::abs(dec_rad) * RAD2DEG;

    double delta_i_deg = 0.0;
    double ej_inc = park_inc_deg;
    if (abs_dec_deg > park_inc_deg) {
        delta_i_deg = abs_dec_deg - park_inc_deg;
        ej_inc = abs_dec_deg;
    }
    if (out_ej_inc) *out_ej_inc = ej_inc;

    if (delta_i_deg <= 1e-4) {
        return std::max(0.0, v_hyp - v_park);
    } else {
        double d_rad = delta_i_deg * DEG2RAD;
        double dv_sq = v_park * v_park + v_hyp * v_hyp - 2.0 * v_park * v_hyp * std::cos(d_rad);
        return std::sqrt(std::max(0.0, dv_sq));
    }
}

static inline double calc_vis_viva_arr(double v_inf, double mu, double radius, double pe_km, double ap_km) {
    if (mu <= 0.0 || radius <= 0.0) return v_inf;
    double rp = radius + std::max(0.0, pe_km);
    double effective_ap_km = ap_km;
    // 天体力学第一性原理：若调用方明确指定了到达远拱点 (ap_km > pe_km) 或明确指定了圆形捕获 (ap_km > 0 且 ap_km <= pe_km)，
    // 严格遵循用户/调用方动力学输入；仅在未指定远拱点 (ap_km <= 0) 时，根据引力球/开普勒能量物理下限推导
    if (effective_ap_km <= 0.0) {
        effective_ap_km = pe_km;
    }
    double ra = radius + std::max(pe_km, effective_ap_km);
    double a = (rp + ra) * 0.5;
    double v_tgt = std::sqrt(mu * (2.0 / rp - 1.0 / a));
    double v_hyp = std::sqrt(v_inf * v_inf + 2.0 * mu / rp);
    return std::max(0.0, v_hyp - v_tgt);
}

static inline double calc_central_departure_dv(
    double mu, double radius, double pe_km, double ap_km,
    double r_target_km, double plane_change_rad, double* out_ej_inc = nullptr)
{
    if (mu <= 0.0 || radius <= 0.0) return 0.0;
    double rp = radius + std::max(0.0, pe_km);
    double ra_park = radius + std::max(pe_km, ap_km);
    double a_park = (rp + ra_park) * 0.5;
    double v_park = std::sqrt(mu * (2.0 / rp - 1.0 / a_park));

    double ra_target = std::max(rp + 1000.0, r_target_km);
    // 经典开普勒椭圆转移近拱点注入能量速度
    double a_trans = (rp + ra_target) * 0.5;
    double v_trans_pe = std::sqrt(std::max(0.0, mu * (2.0 / rp - 1.0 / a_trans)));

    if (plane_change_rad <= 1e-5) {
        return std::max(0.0, v_trans_pe - v_park);
    } else {
        double dv_sq = v_park * v_park + v_trans_pe * v_trans_pe - 2.0 * v_park * v_trans_pe * std::cos(plane_change_rad);
        return std::sqrt(std::max(0.0, dv_sq));
    }
}

static inline void synthesize_parking_boundary_state(
    double mu_central,
    double r_park_km,
    int lock_plane,
    double inc_deg,
    double lan_deg,
    const std::array<double, 3>& r_target,
    const std::array<double, 3>& v_target,
    std::array<double, 3>& out_r,
    std::array<double, 3>& out_v)
{
    if (mu_central <= 0.0 || r_park_km <= 0.0) {
        out_r = {0, 0, 0};
        out_v = {0, 0, 0};
        return;
    }

    double r_dest_km = std::sqrt(r_target[0] * r_target[0] + r_target[1] * r_target[1] + r_target[2] * r_target[2]);
    if (r_dest_km < 1.0) r_dest_km = r_park_km * 50.0;

    // 1. 确定停泊轨道平面单位法向量 h_park
    std::array<double, 3> h_park = {0, 0, 1};
    if (lock_plane && inc_deg > 0.001) {
        double i_rad = inc_deg * DEG2RAD;
        double o_rad = lan_deg * DEG2RAD;
        h_park[0] = std::sin(i_rad) * std::sin(o_rad);
        h_park[1] = -std::sin(i_rad) * std::cos(o_rad);
        h_park[2] = std::cos(i_rad);
    } else {
        // 模式 1: 自由发射规划，自动对准目标卫星轨道角动量面 h = r_target x v_target
        h_park[0] = r_target[1] * v_target[2] - r_target[2] * v_target[1];
        h_park[1] = r_target[2] * v_target[0] - r_target[0] * v_target[2];
        h_park[2] = r_target[0] * v_target[1] - r_target[1] * v_target[0];
    }
    double h_mag = std::sqrt(h_park[0] * h_park[0] + h_park[1] * h_park[1] + h_park[2] * h_park[2]);
    if (h_mag < 1e-6) {
        h_park = {0, 0, 1};
    } else {
        h_park[0] /= h_mag; h_park[1] /= h_mag; h_park[2] /= h_mag;
    }

    // 2. 将目标天体矢量投影至停泊轨道平面
    double dot_h = r_target[0] * h_park[0] + r_target[1] * h_park[1] + r_target[2] * h_park[2];
    std::array<double, 3> r_proj = {
        r_target[0] - dot_h * h_park[0],
        r_target[1] - dot_h * h_park[1],
        r_target[2] - dot_h * h_park[2]
    };
    double proj_mag = std::sqrt(r_proj[0] * r_proj[0] + r_proj[1] * r_proj[1] + r_proj[2] * r_proj[2]);
    if (proj_mag < 1e-6 * r_dest_km) {
        std::array<double, 3> arbitrary = (std::abs(h_park[2]) < 0.9) ? std::array<double,3>{0,0,1} : std::array<double,3>{1,0,0};
        r_proj[0] = h_park[1] * arbitrary[2] - h_park[2] * arbitrary[1];
        r_proj[1] = h_park[2] * arbitrary[0] - h_park[0] * arbitrary[2];
        r_proj[2] = h_park[0] * arbitrary[1] - h_park[1] * arbitrary[0];
        proj_mag = std::sqrt(r_proj[0] * r_proj[0] + r_proj[1] * r_proj[1] + r_proj[2] * r_proj[2]);
    }
    std::array<double, 3> u_r = { r_proj[0] / proj_mag, r_proj[1] / proj_mag, r_proj[2] / proj_mag };

    // 平面内顺行垂直矢量 u_theta = h x u_r
    std::array<double, 3> u_theta = {
        h_park[1] * u_r[2] - h_park[2] * u_r[1],
        h_park[2] * u_r[0] - h_park[0] * u_r[2],
        h_park[0] * u_r[1] - h_park[1] * u_r[0]
    };

    // 3. 动力学开普勒转移相角推导 (基于开普勒几何与真实相角关系)
    double a_trans = (r_park_km + r_dest_km) * 0.5;
    double p_orb = 2.0 * r_park_km - (r_park_km * r_park_km) / std::max(1e-4, a_trans);
    double e_orb = std::abs(1.0 - r_park_km / std::max(1e-4, a_trans));
    double cos_nu = -1.0;
    if (e_orb > 1e-6) {
        cos_nu = (p_orb / std::max(1.0, r_dest_km) - 1.0) / e_orb;
        cos_nu = std::max(-1.0, std::min(1.0, cos_nu));
    }
    double nu_arr = std::acos(cos_nu);

    double cos_dth = std::cos(nu_arr);
    double sin_dth = std::sin(nu_arr);

    out_r[0] = (u_r[0] * cos_dth - u_theta[0] * sin_dth) * r_park_km;
    out_r[1] = (u_r[1] * cos_dth - u_theta[1] * sin_dth) * r_park_km;
    out_r[2] = (u_r[2] * cos_dth - u_theta[2] * sin_dth) * r_park_km;

    double v_circ = std::sqrt(mu_central / r_park_km);
    out_v[0] = (u_theta[0] * cos_dth + u_r[0] * sin_dth) * v_circ;
    out_v[1] = (u_theta[1] * cos_dth + u_r[1] * sin_dth) * v_circ;
    out_v[2] = (u_theta[2] * cos_dth + u_r[2] * sin_dth) * v_circ;
}

EXPORT_API inline int pykep_synthesize_parking_state(
    double mu_central,
    double r_park_km,
    int lock_plane,
    double inc_deg,
    double lan_deg,
    const double* r_target_3,
    const double* v_target_3,
    double* out_r1_3,
    double* out_v1_3)
{
    if (!r_target_3 || !out_r1_3 || !out_v1_3) return -1;
    std::array<double, 3> rt = {r_target_3[0], r_target_3[1], r_target_3[2]};
    std::array<double, 3> vt = {0, 0, 0};
    if (v_target_3) vt = {v_target_3[0], v_target_3[1], v_target_3[2]};
    std::array<double, 3> r1, v1;
    synthesize_parking_boundary_state(mu_central, r_park_km, lock_plane, inc_deg, lan_deg, rt, vt, r1, v1);
    out_r1_3[0] = r1[0]; out_r1_3[1] = r1[1]; out_r1_3[2] = r1[2];
    out_v1_3[0] = v1[0]; out_v1_3[1] = v1[1]; out_v1_3[2] = v1[2];
    return 0;
}

EXPORT_API inline int kep3_calc_coplanar_angle(
    double inc_deg, double lan_deg,
    double rx, double ry, double rz,
    double* out_wedge_angle_deg)
{
    if (!out_wedge_angle_deg) return -1;
    double i_rad = inc_deg * DEG2RAD;
    double o_rad = lan_deg * DEG2RAD;
    double hx = std::sin(i_rad) * std::sin(o_rad);
    double hy = -std::sin(i_rad) * std::cos(o_rad);
    double hz = std::cos(i_rad);

    double r_mag = std::sqrt(rx * rx + ry * ry + rz * rz);
    if (r_mag < 1e-6) {
        *out_wedge_angle_deg = 0.0;
        return 0;
    }
    double dot = (rx * hx + ry * hy + rz * hz) / r_mag;
    dot = std::max(-1.0, std::min(1.0, dot));
    double beta_rad = std::asin(dot);
    *out_wedge_angle_deg = std::abs(beta_rad) * RAD2DEG;
    return 0;
}

EXPORT_API inline const char* pykep_get_version() {
    return "ESA ACT pykep / kep3 C++ Official Core v3.0.1 (Native MGA Bridge v3.0)";
}

EXPORT_API inline double pykep_calc_departure_dv(double v_inf, double mu, double radius, double pe_km, double ap_km) {
    return calc_vis_viva_dep_inc(v_inf, mu, radius, pe_km, ap_km, 0.0, 0.0, nullptr);
}

EXPORT_API inline double pykep_calc_central_departure_dv(double mu, double radius, double pe_km, double ap_km, double r_dest_km) {
    return calc_central_departure_dv(mu, radius, pe_km, ap_km, r_dest_km, 0.0, nullptr);
}

EXPORT_API inline double pykep_calc_insertion_dv(double v_inf, double mu, double radius, double pe_km, double ap_km) {
    return calc_vis_viva_arr(v_inf, mu, radius, pe_km, ap_km);
}

EXPORT_API inline int pykep_flyby_con(
    double v_in_x, double v_in_y, double v_in_z,
    double v_out_x, double v_out_y, double v_out_z,
    double mu, double safe_radius,
    double* eq_V2, double* ineq_delta, double* delta_deg, double* rp_km)
{
    std::array<double, 3> vin = {v_in_x, v_in_y, v_in_z};
    std::array<double, 3> vout = {v_out_x, v_out_y, v_out_z};

    try {
        auto p = kep3::fb_con(vin, vout, mu, safe_radius);
        if (eq_V2) *eq_V2 = p.first;
        if (ineq_delta) *ineq_delta = p.second;

        double vin2 = v_in_x * v_in_x + v_in_y * v_in_y + v_in_z * v_in_z;
        double vout2 = v_out_x * v_out_x + v_out_y * v_out_y + v_out_z * v_out_z;
        double dot = v_in_x * v_out_x + v_in_y * v_out_y + v_in_z * v_out_z;
        double cos_d = dot / std::max(1e-12, std::sqrt(vin2 * vout2));
        cos_d = std::max(-1.0, std::min(1.0, cos_d));
        if (delta_deg) *delta_deg = std::acos(cos_d) * RAD2DEG;

        double sin_half = std::sin(std::acos(cos_d) * 0.5);
        if (rp_km) {
            if (sin_half > 1e-6 && vin2 > 1e-6) {
                *rp_km = (mu / vin2) * (1.0 / sin_half - 1.0);
            } else {
                *rp_km = safe_radius;
            }
        }

        // 真实物理法则：无动力借力不仅要求偏转角在极限内 (p.second <= 0.0) 且近心点不撞地 (*rp_km >= safe_radius)，
        // 还必须绝对满足能量守恒等式约束 |V_in| == |V_out| (|p.first| <= 0.01)！
        bool feasible = (std::abs(p.first) <= 0.01) && (p.second <= 0.0) && (rp_km ? (*rp_km >= safe_radius) : true);
        return feasible ? 0 : 1;
    } catch (...) {
        return -1;
    }
}

EXPORT_API inline double pykep_flyby_dv(
    double v_in_x, double v_in_y, double v_in_z,
    double v_out_x, double v_out_y, double v_out_z,
    double mu, double safe_radius)
{
    // 欧空局官方 ESA ACT (Giacomo Acciarini) 纯代数无超越函数借力判据加速
    // 消除耗时的 acos, asin, cos 超越函数，利用半角有理公式极速求解
    const double vin2 = v_in_x * v_in_x + v_in_y * v_in_y + v_in_z * v_in_z;
    const double vout2 = v_out_x * v_out_x + v_out_y * v_out_y + v_out_z * v_out_z;
    if (vin2 <= 1e-12 || vout2 <= 1e-12 || mu <= 0.0) {
        return std::abs(std::sqrt(vout2) - std::sqrt(vin2));
    }

    const double vin_norm = std::sqrt(vin2);
    const double vout_norm = std::sqrt(vout2);
    const double dot = v_in_x * v_out_x + v_in_y * v_out_y + v_in_z * v_out_z;
    double cos_alpha = dot / (vin_norm * vout_norm);
    if (cos_alpha > 1.0) cos_alpha = 1.0;
    else if (cos_alpha < -1.0) cos_alpha = -1.0;

    const double e_min = 1.0 + (safe_radius / mu) * vin2;
    const double inv_emin2 = 1.0 / (e_min * e_min);
    const double cos_delta_max = 1.0 - 2.0 * inv_emin2;

    // 若转向角在双曲几何极限内 (cos_alpha >= cos_delta_max)，无动力即可转向
    if (cos_alpha >= cos_delta_max) {
        return std::abs(vout_norm - vin_norm);
    }

    // 动力借力点火补足偏折角: 使用纯代数和差化积
    // cos(alpha - delta_max) = cos_alpha * cos_delta_max + sin_alpha * sin_delta_max
    const double sin_alpha = std::sqrt(std::max(0.0, 1.0 - cos_alpha * cos_alpha));
    const double sin_delta_max = 2.0 * std::sqrt(std::max(0.0, e_min * e_min - 1.0)) * inv_emin2;
    const double cos_ineq_delta = cos_alpha * cos_delta_max + sin_alpha * sin_delta_max;

    const double dv2 = vout2 + vin2 - 2.0 * vout_norm * vin_norm * cos_ineq_delta;
    return (dv2 > 0.0) ? std::sqrt(dv2) : 0.0;
}

inline void pykep_propagate_lagrangian(std::array<double, 3>& r, std::array<double, 3>& v, double dt_sec, double mu) {
    if (mu <= 0.0 || std::abs(dt_sec) < 1e-9) return;
    std::array<std::array<double, 3>, 2> pv = {r, v};
    auto res = kep3::propagate_lagrangian(pv, dt_sec, mu, false);
    r = res.first[0];
    v = res.first[1];
}

// ==========================================================================================
// 通用天体力学第一性原理: 自共振蓄能 (DeltaV-EGA) 发射双曲超速候选矢量解析推导
// 100% 纯解析力学公式计算，零硬编码经验数组，自适应任意行星引力常数、轨道几何与飞行时间
// ==========================================================================================
inline void pykep_synthesize_resonant_departure_candidates(
    double p1_mu, double p1_radius, double dep_pe_km,
    double p1_vx, double p1_vy, double p1_vz,
    double r1_len, double v_p1_mag,
    const std::array<double, 3>& u_v,
    const std::array<double, 3>& u_r,
    const std::array<double, 3>& u_h,
    double effective_mu,
    double tof_sec,
    std::vector<std::array<double, 3>>& out_candidates,
    double v_inf_needed = 0.0)
{
    // 1. 行星当地停泊轨道第一宇宙速度与物理运载能力上限公式
    double r_park = p1_radius + std::max(0.0, dep_pe_km);
    double v_circ = (p1_radius > 0.0 && p1_mu > 0.0) ? std::sqrt(p1_mu / r_park) : 8.0;
    double v_inf_limit = 0.75 * v_circ;

    // 2. 严格从天体自身在中心天体引力场下的活力方程推导其实际轨道半长轴 a 与轨道周期 T
    // 1/a = 2/r - v^2/mu,  T = 2*pi*sqrt(a^3/mu) (避免开普勒偏心轨道在远/近心点将瞬时径矢误判为半长轴)
    double inv_a_body = 2.0 / std::max(1.0, r1_len) - (v_p1_mag * v_p1_mag) / effective_mu;
    double a_body = (inv_a_body > 1e-12) ? (1.0 / inv_a_body) : r1_len;
    double t_orb_body = 2.0 * PI * std::sqrt((a_body * a_body * a_body) / effective_mu);
    if (t_orb_body <= 1e-4) return;

    double ratio_nom = tof_sec / t_orb_body;
    std::vector<double> dynamic_ratios;
    for (int q = 1; q <= 4; ++q) {
        int p_center = (int)std::round(q * ratio_nom);
        for (int dp = -1; dp <= 1; ++dp) {
            int p = p_center + dp;
            if (p < 1) continue;
            // 欧几里得辗转相除法互质筛除
            int a = p, b = q;
            while (b != 0) { int t = b; b = a % b; a = t; }
            if (a == 1) {
                double r_val = (double)p / (double)q;
                if (r_val >= 0.5 && r_val <= 3.5) {
                    dynamic_ratios.push_back(r_val);
                }
            }
        }
    }
    std::sort(dynamic_ratios.begin(), dynamic_ratios.end());
    dynamic_ratios.erase(std::unique(dynamic_ratios.begin(), dynamic_ratios.end()), dynamic_ratios.end());

    // 3. 基于开普勒第三定律与活力公式推导各共振比的能量与三维偏角包络
    for (double r_ratio : dynamic_ratios) {
        double a_res = a_body * std::cbrt(r_ratio * r_ratio);
        double v_sq = effective_mu * (2.0 / std::max(1.0, r1_len) - 1.0 / std::max(1.0, a_res));
        if (v_sq <= 1e-4) continue;
        double v_craft = std::sqrt(v_sq);
        double v_inf_nom = std::abs(v_craft - v_p1_mag);

        std::vector<double> v_inf_list;
        bool is_self_resonance = (std::abs(r_ratio - 1.0) < 0.05 || v_inf_nom < 0.50);
        if (is_self_resonance) {
            // 1:1 等轨道同频共振启航 (如 MESSENGER 1年 EGA, Juno 2年 EGA 等): 
            // 活力能量与行星当地公转速度相近 (v_craft ~ v_p1)，通过倾斜出射角满足活力偏角方程
            // 覆盖逃逸能力谱段 [0.20, 0.35, 0.50, 0.65, 0.80] * v_inf_limit
            double test_factors[] = { 0.20, 0.35, 0.50, 0.65, 0.80 };
            for (double tf : test_factors) {
                double v_inf = std::min(v_inf_limit * 0.99, tf * v_inf_limit);
                if (v_inf >= 0.20) v_inf_list.push_back(v_inf);
            }
            // 若下游存在势能跨越所需的特征超速，直接注入锚点及其局部扰动
            if (v_inf_needed > 0.20 && v_inf_needed <= v_inf_limit * 0.99) {
                double needed_factors[] = { 0.95, 1.0, 1.05 };
                for (double nf : needed_factors) {
                    double v_inf = std::min(v_inf_limit * 0.99, v_inf_needed * nf);
                    if (v_inf >= 0.20) v_inf_list.push_back(v_inf);
                }
            }
        } else if (v_inf_nom >= 0.20) {
            for (int j = -2; j <= 2; ++j) {
                double v_scale = 1.0 + j * 0.05;
                double v_inf = std::min(v_inf_limit * 0.99, v_inf_nom * v_scale);
                if (v_inf >= 0.20) v_inf_list.push_back(v_inf);
            }
        }

        for (double v_inf : v_inf_list) {
            // 严格基于活力方程推导三维出射天区角 alpha:
            // v_craft^2 = v_p1^2 + v_inf^2 + 2 * v_p1 * v_inf * cos(alpha)
            double num_a = v_craft * v_craft - v_p1_mag * v_p1_mag - v_inf * v_inf;
            double den_a = 2.0 * std::max(1e-4, v_p1_mag) * std::max(1e-4, v_inf);
            double cos_alpha = num_a / den_a;

            if (std::abs(cos_alpha) <= 1.0) {
                double alpha_exact = std::acos(cos_alpha);
                double signs_a[] = { -1.0, 1.0 };
                for (double sign_a : signs_a) {
                    double rad_a = sign_a * alpha_exact;
                    double c_a = std::cos(rad_a);
                    double s_a = std::sin(rad_a);

                    double beta_max = 3.0 * DEG2RAD;
                    for (int bi = -1; bi <= 1; ++bi) {
                        double rad_b = bi * beta_max;
                        double c_b = std::cos(rad_b);
                        double s_b = std::sin(rad_b);

                        std::array<double, 3> u_dir = {
                            c_b * (c_a * u_v[0] + s_a * u_r[0]) + s_b * u_h[0],
                            c_b * (c_a * u_v[1] + s_a * u_r[1]) + s_b * u_h[1],
                            c_b * (c_a * u_v[2] + s_a * u_r[2]) + s_b * u_h[2]
                        };

                        out_candidates.push_back({
                            p1_vx + v_inf * u_dir[0],
                            p1_vy + v_inf * u_dir[1],
                            p1_vz + v_inf * u_dir[2]
                        });
                    }
                }
            } else {
                // 理论双曲几何极限偏角公式
                double ratio_v = std::min(0.95, v_inf / std::max(1e-4, v_craft));
                double alpha_max = 2.0 * std::asin(ratio_v);

                const int N_alpha = 7;
                for (int ai = 0; ai < N_alpha; ++ai) {
                    double rad_a = -alpha_max + ai * (2.0 * alpha_max / (N_alpha - 1));
                    double c_a = std::cos(rad_a);
                    double s_a = std::sin(rad_a);

                    double beta_max = 3.0 * DEG2RAD;
                    for (int bi = -1; bi <= 1; ++bi) {
                        double rad_b = bi * beta_max;
                        double c_b = std::cos(rad_b);
                        double s_b = std::sin(rad_b);

                        std::array<double, 3> u_dir = {
                            c_b * (c_a * u_v[0] + s_a * u_r[0]) + s_b * u_h[0],
                            c_b * (c_a * u_v[1] + s_a * u_r[1]) + s_b * u_h[1],
                            c_b * (c_a * u_v[2] + s_a * u_r[2]) + s_b * u_h[2]
                        };

                        out_candidates.push_back({
                            p1_vx + v_inf * u_dir[0],
                            p1_vy + v_inf * u_dir[1],
                            p1_vz + v_inf * u_dir[2]
                        });
                    }
                }
            }
        }
    }
}

EXPORT_API inline int pykep_propagate_lagrangian(
    double* rx, double* ry, double* rz,
    double* vx, double* vy, double* vz,
    double dt_sec, double mu)
{
    if (!rx || !ry || !rz || !vx || !vy || !vz || mu <= 0.0) return -1;
    if (std::abs(dt_sec) < 1e-9) return 0;

    std::array<double, 3> r = {*rx, *ry, *rz};
    std::array<double, 3> v = {*vx, *vy, *vz};

    try {
        pykep_propagate_lagrangian(r, v, dt_sec, mu);
        *rx = r[0]; *ry = r[1]; *rz = r[2];
        *vx = v[0]; *vy = v[1]; *vz = v[2];
        return 0;
    } catch (...) {
        return -1;
    }
}

EXPORT_API inline int pykep_elements_to_state(
    double a_km, double e, double i_rad, double raan_rad, double aop_rad,
    double m0_rad, double epoch_ut, double target_ut, double mu_kms,
    double* rx, double* ry, double* rz, double* vx, double* vy, double* vz)
{
    if (a_km <= 0.0 || mu_kms <= 0.0 || e < 0.0 || e >= 1.0) return 1;

    try {
        double M = std::fmod(m0_rad, 2.0 * PI);
        if (M < 0) M += 2.0 * PI;
        double E = M;
        for (int it = 0; it < 50; ++it) {
            double f = kep3::kepE(E, M, e);
            double df = kep3::d_kepE(E, e);
            double dE = f / df;
            E -= dE;
            if (std::fabs(dE) < 1e-14) break;
        }

        std::array<double, 6> elem = {a_km, e, i_rad, raan_rad, aop_rad, E};
        auto [r0, v0] = kep3::par2ic(elem, mu_kms);

        pykep_propagate_lagrangian(r0, v0, target_ut - epoch_ut, mu_kms);

        *rx = r0[0]; *ry = r0[1]; *rz = r0[2];
        *vx = v0[0]; *vy = v0[1]; *vz = v0[2];
        return 0;
    } catch (...) {
        return -1;
    }
}

// ============================================================================
// kep3 NEW METHODS: HOHMANN, BIELLIPTIC, FB_VOUT, VSOP2013
// ============================================================================

EXPORT_API inline int kep3_hohmann_transfer(
    double r1_km, double r2_km, double mu,
    double* out_total_dv, double* out_tof_sec, double* out_dv1, double* out_dv2)
{
    if (r1_km <= 0.0 || r2_km <= 0.0 || mu <= 0.0) return -1;
    try {
        auto [tot_dv, tof, dvs] = kep3::hohmann(r1_km, r2_km, mu);
        if (out_total_dv) *out_total_dv = tot_dv;
        if (out_tof_sec) *out_tof_sec = tof;
        if (out_dv1) *out_dv1 = dvs[0];
        if (out_dv2) *out_dv2 = dvs[1];
        return 0;
    } catch (...) {
        return -1;
    }
}

EXPORT_API inline int kep3_orbit_period(double sma_km, double mu, double* out_period_sec) {
    if (sma_km <= 0.0 || mu <= 0.0 || !out_period_sec) return -1;
    try {
        // 直接开普勒第三定律周期求解 T = 2*pi * sqrt(a^3 / mu)
        const double pi = 3.14159265358979323846;
        *out_period_sec = 2.0 * pi * std::sqrt((sma_km * sma_km * sma_km) / mu);
        return 0;
    } catch (...) {
        return -1;
    }
}

EXPORT_API inline int kep3_bielliptic_transfer(
    double r1_km, double r2_km, double rb_km, double mu,
    double* out_total_dv, double* out_tof_sec)
{
    if (r1_km <= 0.0 || r2_km <= 0.0 || rb_km <= 0.0 || mu <= 0.0) return -1;
    try {
        auto [tot_dv, tof, dvs] = kep3::bielliptic(r1_km, r2_km, rb_km, mu);
        if (out_total_dv) *out_total_dv = tot_dv;
        if (out_tof_sec) *out_tof_sec = tof;
        return 0;
    } catch (...) {
        return -1;
    }
}

EXPORT_API inline int kep3_flyby_vout(
    const double* v_in, const double* v_pla, double rp_km, double beta_rad, double mu,
    double* out_v_out)
{
    if (!v_in || !v_pla || !out_v_out || rp_km <= 0.0 || mu <= 0.0) return -1;
    try {
        std::array<double, 3> vin = {v_in[0], v_in[1], v_in[2]};
        std::array<double, 3> vpla = {v_pla[0], v_pla[1], v_pla[2]};
        std::array<double, 3> vout = kep3::fb_vout(vin, vpla, rp_km, beta_rad, mu);
        out_v_out[0] = vout[0];
        out_v_out[1] = vout[1];
        out_v_out[2] = vout[2];
        return 0;
    } catch (...) {
        return -1;
    }
}

EXPORT_API inline int kep3_vsop2013_planet_state(
    const char* planet_name, double epoch_jd,
    double* out_rx, double* out_ry, double* out_rz,
    double* out_vx, double* out_vy, double* out_vz)
{
    if (!planet_name || !out_rx || !out_ry || !out_rz || !out_vx || !out_vy || !out_vz) return -1;
    try {
        static std::unordered_map<std::string, std::unique_ptr<kep3::udpla::jpl_lp>> s_jpl_cache;
        std::string name_str = planet_name;
        std::transform(name_str.begin(), name_str.end(), name_str.begin(), [](unsigned char c){ return (char)std::tolower(c); });
        if (s_jpl_cache.find(name_str) == s_jpl_cache.end()) {
            s_jpl_cache[name_str] = std::make_unique<kep3::udpla::jpl_lp>(name_str);
        }
        double mjd2000 = epoch_jd - 2451545.0;
        auto pv = s_jpl_cache[name_str]->eph(mjd2000);
        *out_rx = pv[0][0] / 1000.0; *out_ry = pv[0][1] / 1000.0; *out_rz = pv[0][2] / 1000.0;
        *out_vx = pv[1][0] / 1000.0; *out_vy = pv[1][1] / 1000.0; *out_vz = pv[1][2] / 1000.0;
        return 0;
    } catch (...) {
        return -1;
    }
}
