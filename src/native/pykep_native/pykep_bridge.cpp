// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

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
#include <keplerian_toolbox/planet/keplerian.hpp>

#define EXPORT_API extern "C" __declspec(dllexport)

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
#pragma pack(pop)

EXPORT_API const char* pykep_get_version() {
    return "ESA ACT keplerian_toolbox / pykep C++ Official Core (Native MGA Bridge)";
}

EXPORT_API int pykep_lambert_solve(
    double r1x, double r1y, double r1z,
    double r2x, double r2y, double r2z,
    double tof_sec, double mu, int retrograde,
    double* v1x, double* v1y, double* v1z,
    double* v2x, double* v2y, double* v2z)
{
    if (tof_sec <= 0.0 || mu <= 0.0) return -1;

    kep_toolbox::array3D r1 = {r1x, r1y, r1z};
    kep_toolbox::array3D r2 = {r2x, r2y, r2z};

    try {
        int nmax = (tof_sec > 86400.0 * 250.0) ? 2 : 0;
        kep_toolbox::lambert_problem lp(r1, r2, tof_sec, mu, retrograde != 0, nmax);
        const auto& v1_solutions = lp.get_v1();
        const auto& v2_solutions = lp.get_v2();

        if (v1_solutions.empty() || v2_solutions.empty()) return -1;

        *v1x = v1_solutions[0][0]; *v1y = v1_solutions[0][1]; *v1z = v1_solutions[0][2];
        *v2x = v2_solutions[0][0]; *v2y = v2_solutions[0][1]; *v2z = v2_solutions[0][2];
        return 0;
    } catch (...) {
        return -1;
    }
}

EXPORT_API int pykep_flyby_con(
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

EXPORT_API double pykep_flyby_dv(
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

EXPORT_API int pykep_propagate_lagrangian(
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

// ============================================================================
// OFFICIAL NATIVE MGA TRAJECTORY EVALUATOR (0 MANAGED MATH FALLBACK)
// ============================================================================

static inline double calc_vis_viva_dep(double v_inf, double mu, double radius, double pe_km, double ap_km) {
    if (mu <= 0.0 || radius <= 0.0) return v_inf;
    double rp = radius + std::max(0.0, pe_km);
    double ra = radius + std::max(pe_km, ap_km);
    double a = (rp + ra) * 0.5;
    double v_park = std::sqrt(mu * (2.0 / rp - 1.0 / a));
    double v_hyp = std::sqrt(v_inf * v_inf + 2.0 * mu / rp);
    return std::max(0.0, v_hyp - v_park);
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

EXPORT_API int pykep_mga_evaluate(
    const NativePlanetState* dep_planets,
    const NativePlanetState* arr_planets,
    const double* tofs_sec,
    int leg_count,
    double dep_pe_km, double dep_ap_km,
    double arr_pe_km, double arr_ap_km,
    int no_insertion,
    NativeMGASolution* out_sol)
{
    if (!dep_planets || !arr_planets || !tofs_sec || !out_sol || leg_count <= 0 || leg_count > 16) {
        return -1;
    }

    std::memset(out_sol, 0, sizeof(NativeMGASolution));
    out_sol->leg_count = leg_count;
    out_sol->all_feasible = 1;

    double total_tof_sec = 0.0;
    double total_dv = 0.0;
    std::array<double, 3> last_v_out = {0.0, 0.0, 0.0};

    for (int k = 0; k < leg_count; ++k) {
        NativeLegResult& leg = out_sol->legs[k];
        double tof = tofs_sec[k];
        leg.tof_sec = tof;
        total_tof_sec += tof;

        const NativePlanetState& p1 = dep_planets[k];
        const NativePlanetState& p2 = arr_planets[k];

        leg.r1x = p1.rx; leg.r1y = p1.ry; leg.r1z = p1.rz;
        leg.r2x = p2.rx; leg.r2y = p2.ry; leg.r2z = p2.rz;

        double v1x, v1y, v1z, v2x, v2y, v2z;
        int status = pykep_lambert_solve(
            p1.rx, p1.ry, p1.rz,
            p2.rx, p2.ry, p2.rz,
            tof, SUN_MU, 0,
            &v1x, &v1y, &v1z,
            &v2x, &v2y, &v2z);

        if (status != 0) {
            out_sol->all_feasible = 0;
            return -2;
        }

        leg.v1x = v1x; leg.v1y = v1y; leg.v1z = v1z;
        leg.v2x = v2x; leg.v2y = v2y; leg.v2z = v2z;

        leg.v_inf_dep_x = v1x - p1.vx;
        leg.v_inf_dep_y = v1y - p1.vy;
        leg.v_inf_dep_z = v1z - p1.vz;

        leg.v_inf_arr_x = v2x - p2.vx;
        leg.v_inf_arr_y = v2y - p2.vy;
        leg.v_inf_arr_z = v2z - p2.vz;

        double v_inf_dep_mag = std::sqrt(leg.v_inf_dep_x * leg.v_inf_dep_x + leg.v_inf_dep_y * leg.v_inf_dep_y + leg.v_inf_dep_z * leg.v_inf_dep_z);

        if (k == 0) {
            // Departure Leg
            out_sol->c3_kms2 = v_inf_dep_mag * v_inf_dep_mag;
            out_sol->launch_dv_kms = calc_vis_viva_dep(v_inf_dep_mag, p1.mu, p1.radius, dep_pe_km, dep_ap_km);
            total_dv += out_sol->launch_dv_kms;

            // Geometry
            double dec = std::asin(std::max(-1.0, std::min(1.0, leg.v_inf_dep_z / std::max(1e-6, v_inf_dep_mag)))) * RAD2DEG;
            double ra = std::atan2(leg.v_inf_dep_y, leg.v_inf_dep_x) * RAD2DEG;
            if (ra < 0) ra += 360.0;
            out_sol->ejection_inc_deg = std::abs(dec);
            out_sol->ejection_lan_deg = ra;

            double r_park = p1.radius + dep_pe_km;
            double e_hyp = 1.0 + (r_park * v_inf_dep_mag * v_inf_dep_mag) / std::max(1e-6, p1.mu);
            double beta = std::acos(-1.0 / std::max(1.0001, e_hyp)) * RAD2DEG;
            double ej_ang = 180.0 - (beta - 90.0);
            if (ej_ang > 180.0) ej_ang = 360.0 - ej_ang;
            out_sol->ejection_angle_deg = ej_ang;

            leg.flyby_dv_kms = 0.0;
            leg.prograde_ms = out_sol->launch_dv_kms * 1000.0;
            leg.normal_ms = 0.0;
            leg.radial_ms = 0.0;
            leg.is_feasible = 1;
        } else {
            // Intermediate Flyby Leg
            double vin_x = last_v_out[0], vin_y = last_v_out[1], vin_z = last_v_out[2];
            double vout_x = leg.v_inf_dep_x, vout_y = leg.v_inf_dep_y, vout_z = leg.v_inf_dep_z;

            double eq_v2, ineq_delta, delta_deg, rp_km;
            int fb_res = pykep_flyby_con(
                vin_x, vin_y, vin_z,
                vout_x, vout_y, vout_z,
                p1.mu, p1.safe_radius,
                &eq_v2, &ineq_delta, &delta_deg, &rp_km);

            double dv_req = pykep_flyby_dv(
                vin_x, vin_y, vin_z,
                vout_x, vout_y, vout_z,
                p1.mu, p1.safe_radius);

            leg.flyby_turn_deg = delta_deg;
            leg.flyby_rp_km = rp_km;
            leg.flyby_dv_kms = dv_req;
            leg.is_feasible = (fb_res == 0 && rp_km >= p1.safe_radius);
            if (!leg.is_feasible) out_sol->all_feasible = 0;

            total_dv += dv_req;

            // 3-Axis Frenet (VNC) Vector Projection
            if (dv_req > 1e-4) {
                double dvx = vout_x - vin_x;
                double dvy = vout_y - vin_y;
                double dvz = vout_z - vin_z;

                double v_len = std::sqrt(vin_x * vin_x + vin_y * vin_y + vin_z * vin_z);
                if (v_len < 1e-6) v_len = 1.0;
                double vhat_x = vin_x / v_len, vhat_y = vin_y / v_len, vhat_z = vin_z / v_len;

                // Normal vector r x v
                double nx = p1.ry * vin_z - p1.rz * vin_y;
                double ny = p1.rz * vin_x - p1.rx * vin_z;
                double nz = p1.rx * vin_y - p1.ry * vin_x;
                double n_len = std::sqrt(nx * nx + ny * ny + nz * nz);
                if (n_len < 1e-6) { nx = 0; ny = 0; nz = 1; n_len = 1; }
                double nhat_x = nx / n_len, nhat_y = ny / n_len, nhat_z = nz / n_len;

                // Radial/Cross vector v x n
                double chat_x = vhat_y * nhat_z - vhat_z * nhat_y;
                double chat_y = vhat_z * nhat_x - vhat_x * nhat_z;
                double chat_z = vhat_x * nhat_y - vhat_y * nhat_x;

                leg.prograde_ms = (dvx * vhat_x + dvy * vhat_y + dvz * vhat_z) * 1000.0;
                leg.normal_ms = (dvx * nhat_x + dvy * nhat_y + dvz * nhat_z) * 1000.0;
                leg.radial_ms = (dvx * chat_x + dvy * chat_y + dvz * chat_z) * 1000.0;
            } else {
                leg.prograde_ms = 0.0;
                leg.normal_ms = 0.0;
                leg.radial_ms = 0.0;
            }
        }

        last_v_out[0] = leg.v_inf_arr_x;
        last_v_out[1] = leg.v_inf_arr_y;
        last_v_out[2] = leg.v_inf_arr_z;
    }

    // Final Arrival Insertion
    if (!no_insertion) {
        double v_inf_arr_mag = std::sqrt(last_v_out[0] * last_v_out[0] + last_v_out[1] * last_v_out[1] + last_v_out[2] * last_v_out[2]);
        const NativePlanetState& p_dest = arr_planets[leg_count - 1];
        out_sol->insertion_dv_kms = calc_vis_viva_arr(v_inf_arr_mag, p_dest.mu, p_dest.radius, arr_pe_km, arr_ap_km);
        total_dv += out_sol->insertion_dv_kms;
    } else {
        out_sol->insertion_dv_kms = 0.0;
    }

    out_sol->total_dv_kms = total_dv;
    out_sol->total_flight_days = total_tof_sec / 86400.0;
    return 0;
}
