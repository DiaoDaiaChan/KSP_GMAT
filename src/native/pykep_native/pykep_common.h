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
    double park_inc_deg = std::abs(dep_inc_deg);
    if (mu <= 0.0 || radius <= 0.0) {
        if (out_ej_inc) *out_ej_inc = park_inc_deg;
        return v_inf;
    }
    double rp = radius + std::max(0.0, pe_km);
    double ra = radius + std::max(pe_km, ap_km);
    double a = (rp + ra) * 0.5;
    double v_park = std::sqrt(mu * (2.0 / rp - 1.0 / a));
    double v_hyp = std::sqrt(v_inf * v_inf + 2.0 * mu / rp);

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
    double ra = radius + std::max(pe_km, ap_km);
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

        bool feasible = (p.second <= 0.0) && (rp_km ? (*rp_km >= safe_radius) : true);
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
    std::array<double, 3> vin = {v_in_x, v_in_y, v_in_z};
    std::array<double, 3> vout = {v_out_x, v_out_y, v_out_z};

    try {
        return kep3::fb_dv(vin, vout, mu, safe_radius);
    } catch (...) {
        return 0.0;
    }
}

inline void pykep_propagate_lagrangian(std::array<double, 3>& r, std::array<double, 3>& v, double dt_sec, double mu) {
    if (mu <= 0.0 || std::abs(dt_sec) < 1e-9) return;
    std::array<std::array<double, 3>, 2> pv = {r, v};
    auto res = kep3::propagate_lagrangian(pv, dt_sec, mu, false);
    r = res.first[0];
    v = res.first[1];
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
