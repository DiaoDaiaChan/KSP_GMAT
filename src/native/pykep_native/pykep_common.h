// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Common Protocol, 8-byte Packed Structs & Core Astrodynamics Utilities
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#pragma once

#include <cmath>
#include <array>
#include <vector>
#include <algorithm>
#include <cstring>
#include <random>

#include <keplerian_toolbox/epoch.hpp>
#include <keplerian_toolbox/lambert_problem.hpp>
#include <keplerian_toolbox/core_functions/fb_con.hpp>
#include <keplerian_toolbox/core_functions/fb_vel.hpp>
#include <keplerian_toolbox/core_functions/propagate_lagrangian.hpp>
#include <keplerian_toolbox/core_functions/par2ic.hpp>
#include <keplerian_toolbox/core_functions/ic2par.hpp>
#include <keplerian_toolbox/core_functions/kepler_equations.hpp>
#include <keplerian_toolbox/planet/keplerian.hpp>

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
};

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

EXPORT_API inline const char* pykep_get_version() {
    return "ESA ACT keplerian_toolbox / pykep C++ Official Core (Native MGA Bridge v2.5 Modular)";
}

EXPORT_API inline double pykep_calc_departure_dv(double v_inf, double mu, double radius, double pe_km, double ap_km) {
    return calc_vis_viva_dep_inc(v_inf, mu, radius, pe_km, ap_km, 0.0, 0.0, nullptr);
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
    kep_toolbox::array3D vin = {v_in_x, v_in_y, v_in_z};
    kep_toolbox::array3D vout = {v_out_x, v_out_y, v_out_z};

    try {
        kep_toolbox::array6D elem = {1.496e8, 0.0, 0.0, 0.0, 0.0, 0.0};
        kep_toolbox::planet::keplerian pl(kep_toolbox::epoch(0), elem, SUN_MU, mu, safe_radius, safe_radius, "Body");
        kep_toolbox::fb_con(*eq_V2, *ineq_delta, vin, vout, pl);

        double vin2 = v_in_x * v_in_x + v_in_y * v_in_y + v_in_z * v_in_z;
        double vout2 = v_out_x * v_out_x + v_out_y * v_out_y + v_out_z * v_out_z;
        double dot = v_in_x * v_out_x + v_in_y * v_out_y + v_in_z * v_out_z;
        double cos_d = dot / std::max(1e-12, std::sqrt(vin2 * vout2));
        cos_d = std::max(-1.0, std::min(1.0, cos_d));
        *delta_deg = std::acos(cos_d) * RAD2DEG;

        double sin_half = std::sin(std::acos(cos_d) * 0.5);
        if (sin_half > 1e-6 && vin2 > 1e-6) {
            *rp_km = (mu / vin2) * (1.0 / sin_half - 1.0);
        } else {
            *rp_km = safe_radius;
        }

        bool feasible = (*ineq_delta <= 0.0) && (*rp_km >= safe_radius);
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
    kep_toolbox::array3D vin = {v_in_x, v_in_y, v_in_z};
    kep_toolbox::array3D vout = {v_out_x, v_out_y, v_out_z};

    try {
        kep_toolbox::array6D elem = {1.496e8, 0.0, 0.0, 0.0, 0.0, 0.0};
        kep_toolbox::planet::keplerian pl(kep_toolbox::epoch(0), elem, SUN_MU, mu, safe_radius, safe_radius, "Body");
        double dv = 0.0;
        kep_toolbox::fb_vel(dv, vin, vout, pl);
        return dv;
    } catch (...) {
        return 0.0;
    }
}

EXPORT_API inline int pykep_propagate_lagrangian(
    double* rx, double* ry, double* rz,
    double* vx, double* vy, double* vz,
    double dt_sec, double mu)
{
    if (mu <= 0.0 || std::abs(dt_sec) < 1e-9) return 0;

    kep_toolbox::array3D r = {*rx, *ry, *rz};
    kep_toolbox::array3D v = {*vx, *vy, *vz};

    try {
        kep_toolbox::propagate_lagrangian(r, v, dt_sec, mu);
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
            double f = kep_toolbox::kepE(E, M, e);
            double df = kep_toolbox::d_kepE(E, e);
            double dE = f / df;
            E -= dE;
            if (std::fabs(dE) < 1e-14) break;
        }

        kep_toolbox::array6D elem = {a_km, e, i_rad, raan_rad, aop_rad, E};
        kep_toolbox::array3D r0, v0;
        kep_toolbox::par2ic(elem, mu_kms, r0, v0);

        kep_toolbox::propagate_lagrangian(r0, v0, target_ut - epoch_ut, mu_kms);

        *rx = r0[0]; *ry = r0[1]; *rz = r0[2];
        *vx = v0[0]; *vy = v0[1]; *vz = v0[2];
        return 0;
    } catch (...) {
        return -1;
    }
}
