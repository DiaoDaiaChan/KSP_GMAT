// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 1: Ballistic Lambert Solver & Multiple Gravity Assist (MGA) Evaluator
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

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

EXPORT_API int pykep_lambert_multirev(
    double r1x, double r1y, double r1z,
    double r2x, double r2y, double r2z,
    double tof_sec, double mu, int retrograde, int nmax,
    int max_solutions,
    int* out_solution_count,
    double* out_v1_xyz,
    double* out_v2_xyz)
{
    if (tof_sec <= 0.0 || mu <= 0.0 || max_solutions <= 0 || !out_solution_count || !out_v1_xyz || !out_v2_xyz) {
        return -1;
    }

    kep_toolbox::array3D r1 = {r1x, r1y, r1z};
    kep_toolbox::array3D r2 = {r2x, r2y, r2z};

    try {
        kep_toolbox::lambert_problem lp(r1, r2, tof_sec, mu, retrograde != 0, std::max(0, std::min(4, nmax)));
        const auto& v1_list = lp.get_v1();
        const auto& v2_list = lp.get_v2();

        int n_sol = (int)std::min((size_t)max_solutions, v1_list.size());
        *out_solution_count = n_sol;

        for (int i = 0; i < n_sol; ++i) {
            out_v1_xyz[i * 3 + 0] = v1_list[i][0];
            out_v1_xyz[i * 3 + 1] = v1_list[i][1];
            out_v1_xyz[i * 3 + 2] = v1_list[i][2];

            out_v2_xyz[i * 3 + 0] = v2_list[i][0];
            out_v2_xyz[i * 3 + 1] = v2_list[i][1];
            out_v2_xyz[i * 3 + 2] = v2_list[i][2];
        }

        return (n_sol > 0) ? 0 : -2;
    } catch (...) {
        *out_solution_count = 0;
        return -1;
    }
}

EXPORT_API int pykep_mga_evaluate(
    const NativePlanetState* dep_planets,
    const NativePlanetState* arr_planets,
    const double* tofs_sec,
    int leg_count,
    double dep_pe_km, double dep_ap_km,
    double dep_inc_deg,
    double arr_pe_km, double arr_ap_km,
    int no_insertion,
    NativeMGASolution* out_sol)
{
    if (!dep_planets || !arr_planets || !tofs_sec || !out_sol || leg_count <= 0 || leg_count > 16) {
        return -1;
    }

    try {
        std::memset(out_sol, 0, sizeof(NativeMGASolution));
        out_sol->leg_count = leg_count;
        out_sol->all_feasible = 1;

        double total_tof_sec = 0.0;

        struct LegCandidateBranches {
            std::vector<kep_toolbox::array3D> v1;
            std::vector<kep_toolbox::array3D> v2;
        };
        std::vector<LegCandidateBranches> leg_branches(leg_count);

        for (int k = 0; k < leg_count; ++k) {
            double tof = tofs_sec[k];
            total_tof_sec += tof;
            out_sol->legs[k].tof_sec = tof;

            const NativePlanetState& p1 = dep_planets[k];
            const NativePlanetState& p2 = arr_planets[k];

            out_sol->legs[k].r1x = p1.rx; out_sol->legs[k].r1y = p1.ry; out_sol->legs[k].r1z = p1.rz;
            out_sol->legs[k].r2x = p2.rx; out_sol->legs[k].r2y = p2.ry; out_sol->legs[k].r2z = p2.rz;

            double r1_len = std::sqrt(p1.rx * p1.rx + p1.ry * p1.ry + p1.rz * p1.rz);
            double r2_len = std::sqrt(p2.rx * p2.rx + p2.ry * p2.ry + p2.rz * p2.rz);
            double r_avg = (r1_len + r2_len) * 0.5;
            double t_planet = 2.0 * PI * std::sqrt(r_avg * r_avg * r_avg / SUN_MU);

            int nmax = 0;
            if (t_planet > 1.0 && tof > 0.85 * t_planet) {
                nmax = (int)std::min(4.0, std::floor(tof / t_planet));
            }

            kep_toolbox::array3D r1 = {p1.rx, p1.ry, p1.rz};
            kep_toolbox::array3D r2 = {p2.rx, p2.ry, p2.rz};

            try {
                kep_toolbox::lambert_problem lp(r1, r2, tof, SUN_MU, false, nmax);
                leg_branches[k].v1 = lp.get_v1();
                leg_branches[k].v2 = lp.get_v2();
            } catch (...) {}

            if (leg_branches[k].v1.empty()) {
                try {
                    kep_toolbox::lambert_problem lp(r1, r2, tof, SUN_MU, true, nmax);
                    leg_branches[k].v1 = lp.get_v1();
                    leg_branches[k].v2 = lp.get_v2();
                } catch (...) {}
            }

            if (leg_branches[k].v1.empty()) {
                out_sol->all_feasible = 0;
                return -2;
            }
        }

        std::vector<int> best_branch(leg_count, 0);
        double min_total_cost = 1e18;

        std::vector<int> current_branch(leg_count, 0);
        auto search_branches = [&](auto& self, int k, double cur_cost, std::array<double, 3> v_out_prev) -> void {
            if (k == leg_count) {
                if (!no_insertion && leg_count > 0) {
                    int last_idx = current_branch[leg_count - 1];
                    const auto& v2 = leg_branches[leg_count - 1].v2[last_idx];
                    const auto& p_dest = arr_planets[leg_count - 1];
                    double v_arr_x = v2[0] - p_dest.vx;
                    double v_arr_y = v2[1] - p_dest.vy;
                    double v_arr_z = v2[2] - p_dest.vz;
                    double v_arr_mag = std::sqrt(v_arr_x * v_arr_x + v_arr_y * v_arr_y + v_arr_z * v_arr_z);
                    cur_cost += calc_vis_viva_arr(v_arr_mag, p_dest.mu, p_dest.radius, arr_pe_km, arr_ap_km);
                }
                if (cur_cost < min_total_cost) {
                    min_total_cost = cur_cost;
                    best_branch = current_branch;
                }
                return;
            }

            int branch_count = (int)leg_branches[k].v1.size();
            for (int b = 0; b < branch_count; ++b) {
                current_branch[k] = b;
                const auto& v1 = leg_branches[k].v1[b];
                const auto& v2 = leg_branches[k].v2[b];
                const auto& p_dep = dep_planets[k];

                double added_cost = 0.0;
                if (k == 0) {
                    double v_inf_x = v1[0] - p_dep.vx;
                    double v_inf_y = v1[1] - p_dep.vy;
                    double v_inf_z = v1[2] - p_dep.vz;
                    double v_inf_mag = std::sqrt(v_inf_x * v_inf_x + v_inf_y * v_inf_y + v_inf_z * v_inf_z);
                    added_cost = calc_vis_viva_dep_inc(v_inf_mag, p_dep.mu, p_dep.radius, dep_pe_km, dep_ap_km, dep_inc_deg, v_inf_z, nullptr);
                } else {
                    double dv = pykep_flyby_dv(v_out_prev[0], v_out_prev[1], v_out_prev[2],
                                               v1[0] - p_dep.vx, v1[1] - p_dep.vy, v1[2] - p_dep.vz,
                                               p_dep.mu, p_dep.safe_radius);
                    added_cost = dv;
                }

                if (cur_cost + added_cost < min_total_cost) {
                    const auto& p_arr = arr_planets[k];
                    std::array<double, 3> v_out_next = {v2[0] - p_arr.vx, v2[1] - p_arr.vy, v2[2] - p_arr.vz};
                    self(self, k + 1, cur_cost + added_cost, v_out_next);
                }
            }
        };

        std::array<double, 3> init_v_out = {0, 0, 0};
        search_branches(search_branches, 0, 0.0, init_v_out);

        double total_dv = 0.0;
        for (int k = 0; k < leg_count; ++k) {
            int b_opt = best_branch[k];
            const auto& v1 = leg_branches[k].v1[b_opt];
            const auto& v2 = leg_branches[k].v2[b_opt];
            const auto& p1 = dep_planets[k];
            const auto& p2 = arr_planets[k];

            NativeLegResult& leg = out_sol->legs[k];
            leg.v1x = v1[0]; leg.v1y = v1[1]; leg.v1z = v1[2];
            leg.v2x = v2[0]; leg.v2y = v2[1]; leg.v2z = v2[2];
            leg.v_inf_dep_x = v1[0] - p1.vx;
            leg.v_inf_dep_y = v1[1] - p1.vy;
            leg.v_inf_dep_z = v1[2] - p1.vz;
            leg.v_inf_arr_x = v2[0] - p2.vx;
            leg.v_inf_arr_y = v2[1] - p2.vy;
            leg.v_inf_arr_z = v2[2] - p2.vz;

            if (k == 0) {
                double v_inf_mag = std::sqrt(leg.v_inf_dep_x * leg.v_inf_dep_x + leg.v_inf_dep_y * leg.v_inf_dep_y + leg.v_inf_dep_z * leg.v_inf_dep_z);
                out_sol->c3_kms2 = v_inf_mag * v_inf_mag;
                out_sol->launch_dv_kms = calc_vis_viva_dep_inc(v_inf_mag, p1.mu, p1.radius, dep_pe_km, dep_ap_km, dep_inc_deg, leg.v_inf_dep_z, &out_sol->ejection_inc_deg);
                total_dv += out_sol->launch_dv_kms;

                double ra = std::atan2(leg.v_inf_dep_y, leg.v_inf_dep_x) * RAD2DEG;
                if (ra < 0) ra += 360.0;
                out_sol->ejection_lan_deg = ra;

                double r_park = p1.radius + dep_pe_km;
                double e_hyp = 1.0 + (r_park * v_inf_mag * v_inf_mag) / std::max(1e-6, p1.mu);
                double beta = std::acos(-1.0 / std::max(1.0001, e_hyp)) * RAD2DEG;
                double ej_ang = 180.0 - (beta - 90.0);
                if (ej_ang > 180.0) ej_ang = 360.0 - ej_ang;
                out_sol->ejection_angle_deg = ej_ang;

                double v_body_mag = std::sqrt(p1.vx * p1.vx + p1.vy * p1.vy + p1.vz * p1.vz);
                if (v_body_mag > 1e-6) {
                    double v_hat[3] = {p1.vx / v_body_mag, p1.vy / v_body_mag, p1.vz / v_body_mag};
                    double h_vec[3] = {p1.ry * p1.vz - p1.rz * p1.vy, p1.rz * p1.vx - p1.rx * p1.vz, p1.rx * p1.vy - p1.ry * p1.vx};
                    double h_mag = std::sqrt(h_vec[0] * h_vec[0] + h_vec[1] * h_vec[1] + h_vec[2] * h_vec[2]);
                    double n_hat[3] = {0, 0, 1};
                    if (h_mag > 1e-6) { n_hat[0] = h_vec[0] / h_mag; n_hat[1] = h_vec[1] / h_mag; n_hat[2] = h_vec[2] / h_mag; }
                    double r_hat[3] = {v_hat[1] * n_hat[2] - v_hat[2] * n_hat[1], v_hat[2] * n_hat[0] - v_hat[0] * n_hat[2], v_hat[0] * n_hat[1] - v_hat[1] * n_hat[0]};

                    leg.prograde_ms = (leg.v_inf_dep_x * v_hat[0] + leg.v_inf_dep_y * v_hat[1] + leg.v_inf_dep_z * v_hat[2]) * 1000.0;
                    leg.normal_ms = (leg.v_inf_dep_x * n_hat[0] + leg.v_inf_dep_y * n_hat[1] + leg.v_inf_dep_z * n_hat[2]) * 1000.0;
                    leg.radial_ms = (leg.v_inf_dep_x * r_hat[0] + leg.v_inf_dep_y * r_hat[1] + leg.v_inf_dep_z * r_hat[2]) * 1000.0;
                }
            } else {
                NativeLegResult& prev_leg = out_sol->legs[k - 1];
                double eq_v2, ineq_d, delta_deg, rp_km;
                int fb_ok = pykep_flyby_con(prev_leg.v_inf_arr_x, prev_leg.v_inf_arr_y, prev_leg.v_inf_arr_z,
                                            leg.v_inf_dep_x, leg.v_inf_dep_y, leg.v_inf_dep_z,
                                            p1.mu, p1.safe_radius, &eq_v2, &ineq_d, &delta_deg, &rp_km);

                leg.flyby_turn_deg = delta_deg;
                leg.flyby_rp_km = rp_km;
                leg.is_feasible = (fb_ok == 0) ? 1 : 0;
                if (fb_ok != 0) out_sol->all_feasible = 0;

                double dv = pykep_flyby_dv(prev_leg.v_inf_arr_x, prev_leg.v_inf_arr_y, prev_leg.v_inf_arr_z,
                                           leg.v_inf_dep_x, leg.v_inf_dep_y, leg.v_inf_dep_z,
                                           p1.mu, p1.safe_radius);
                leg.flyby_dv_kms = dv;
                total_dv += dv;

                double d_vx = leg.v_inf_dep_x - prev_leg.v_inf_arr_x;
                double d_vy = leg.v_inf_dep_y - prev_leg.v_inf_arr_y;
                double d_vz = leg.v_inf_dep_z - prev_leg.v_inf_arr_z;
                double v_body_mag = std::sqrt(p1.vx * p1.vx + p1.vy * p1.vy + p1.vz * p1.vz);
                if (v_body_mag > 1e-6) {
                    double v_hat[3] = {p1.vx / v_body_mag, p1.vy / v_body_mag, p1.vz / v_body_mag};
                    double h_vec[3] = {p1.ry * p1.vz - p1.rz * p1.vy, p1.rz * p1.vx - p1.rx * p1.vz, p1.rx * p1.vy - p1.ry * p1.vx};
                    double h_mag = std::sqrt(h_vec[0] * h_vec[0] + h_vec[1] * h_vec[1] + h_vec[2] * h_vec[2]);
                    double n_hat[3] = {0, 0, 1};
                    if (h_mag > 1e-6) { n_hat[0] = h_vec[0] / h_mag; n_hat[1] = h_vec[1] / h_mag; n_hat[2] = h_vec[2] / h_mag; }
                    double r_hat[3] = {v_hat[1] * n_hat[2] - v_hat[2] * n_hat[1], v_hat[2] * n_hat[0] - v_hat[0] * n_hat[2], v_hat[0] * n_hat[1] - v_hat[1] * n_hat[0]};

                    leg.prograde_ms = (d_vx * v_hat[0] + d_vy * v_hat[1] + d_vz * v_hat[2]) * 1000.0;
                    leg.normal_ms = (d_vx * n_hat[0] + d_vy * n_hat[1] + d_vz * n_hat[2]) * 1000.0;
                    leg.radial_ms = (d_vx * r_hat[0] + d_vy * r_hat[1] + d_vz * r_hat[2]) * 1000.0;
                }
            }
        }

        if (!no_insertion && leg_count > 0) {
            const NativePlanetState& p_dest = arr_planets[leg_count - 1];
            double v_inf_arr_mag = std::sqrt(
                out_sol->legs[leg_count - 1].v_inf_arr_x * out_sol->legs[leg_count - 1].v_inf_arr_x +
                out_sol->legs[leg_count - 1].v_inf_arr_y * out_sol->legs[leg_count - 1].v_inf_arr_y +
                out_sol->legs[leg_count - 1].v_inf_arr_z * out_sol->legs[leg_count - 1].v_inf_arr_z);

            out_sol->insertion_dv_kms = calc_vis_viva_arr(v_inf_arr_mag, p_dest.mu, p_dest.radius, arr_pe_km, arr_ap_km);
            total_dv += out_sol->insertion_dv_kms;
        }

        out_sol->total_dv_kms = total_dv;
        out_sol->total_flight_days = total_tof_sec / 86400.0;
        return 0;
    } catch (...) {
        if (out_sol) out_sol->all_feasible = 0;
        return -99;
    }
}
