// ============================================================================
// ESA PyKEP v3 (kep3) Official Native C-API Bridge
// Module 2: MGA-1DSM (Deep Space Maneuvers) & Heterogeneous Multi-Strategy Engine
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

EXPORT_API int pykep_mga_hybrid_evaluate_mu(
    const NativePlanetState* dep_planets,
    const NativePlanetState* arr_planets,
    const double* tofs_sec,
    const int* leg_strategies,
    const int* flyby_strategies,
    const double* dsm_etas,
    const double* fb_rps_km,
    const double* fb_betas_rad,
    int leg_count,
    double dep_pe_km, double dep_ap_km, double dep_inc_deg,
    double arr_pe_km, double arr_ap_km, int no_insertion,
    double mu_central,
    NativeMGA1DSMSolution* out_sol)
{
    if (!dep_planets || !arr_planets || !tofs_sec || !out_sol || leg_count <= 0 || leg_count > 16) {
        return -1;
    }

    double effective_mu = (mu_central > 0.0) ? mu_central : SUN_MU;

    try {
        std::memset(out_sol, 0, sizeof(NativeMGA1DSMSolution));
        out_sol->leg_count = leg_count;
        out_sol->all_feasible = 1;

        double total_tof_sec = 0.0;
        double total_dv = 0.0;
        double total_dsm_dv = 0.0;
        double total_flyby_dv = 0.0;

        std::array<double, 3> cur_v_dep = {0, 0, 0};

        for (int k = 0; k < leg_count; ++k) {
            NativeLeg1DSMResult& leg = out_sol->legs[k];
            double tof = tofs_sec[k];
            total_tof_sec += tof;
            leg.tof_sec = tof;

            const NativePlanetState& p1 = dep_planets[k];
            const NativePlanetState& p2 = arr_planets[k];

            leg.r1x = p1.rx; leg.r1y = p1.ry; leg.r1z = p1.rz;
            leg.r2x = p2.rx; leg.r2y = p2.ry; leg.r2z = p2.rz;

            std::array<double, 3> r1_arr = {p1.rx, p1.ry, p1.rz};
            std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
            std::array<double, 3> v2_dir = {p2.vx, p2.vy, p2.vz};

            double r1_len = std::sqrt(p1.rx * p1.rx + p1.ry * p1.ry + p1.rz * p1.rz);
            double r2_len = std::sqrt(p2.rx * p2.rx + p2.ry * p2.ry + p2.rz * p2.rz);

            if (k == 0 && is_central_body(p1, effective_mu) && r1_len < 1.0) {
                double r_park = p1.radius + std::max(0.0, dep_pe_km);
                std::array<double, 3> syn_r, syn_v;
                synthesize_parking_boundary_state(effective_mu, r_park, dep_inc_deg > 0.01, dep_inc_deg, 0.0, r2_arr, v2_dir, syn_r, syn_v);
                r1_arr = syn_r;
                leg.r1x = syn_r[0]; leg.r1y = syn_r[1]; leg.r1z = syn_r[2];
            }
            if (k == leg_count - 1 && is_central_body(p2, effective_mu) && r2_len < 1.0) {
                double r_park = p2.radius + std::max(0.0, arr_pe_km);
                std::array<double, 3> syn_r, syn_v;
                std::array<double, 3> v1_dir = {p1.vx, p1.vy, p1.vz};
                synthesize_parking_boundary_state(effective_mu, r_park, 0, 0.0, 0.0, r1_arr, v1_dir, syn_r, syn_v);
                r2_arr = syn_r;
                leg.r2x = syn_r[0]; leg.r2y = syn_r[1]; leg.r2z = syn_r[2];
            }

            int strat = (leg_strategies != nullptr) ? leg_strategies[k] : 1;
            double eta = (dsm_etas != nullptr && dsm_etas[k] > 0.0) ? std::max(0.05, std::min(0.95, dsm_etas[k])) : 0.5;

            // STRATEGY 0: PURE BALLISTIC LAMBERT TRANSFER (NO DSM)
            if (strat == 0) {
                std::array<double, 3> v1_lam = {0, 0, 0};
                std::array<double, 3> v2_lam = {0, 0, 0};
                bool lam_ok = false;
                double best_cost = 1e8;
                double r1_len_b = std::sqrt(r1_arr[0]*r1_arr[0] + r1_arr[1]*r1_arr[1] + r1_arr[2]*r1_arr[2]);
                double r2_len_b = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                double r_avg_b = (r1_len_b + r2_len_b) * 0.5;
                double t_orb_b = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_b * r_avg_b * r_avg_b) / effective_mu);
                int nmax = (t_orb_b > 10.0) ? (int)std::floor(tof / t_orb_b + 0.25) : 0;
                bool is_res_b = (std::abs(p1.mu - p2.mu) < 1e-4 && std::abs(p1.radius - p2.radius) < 1e-4 && !is_central_body(p1, effective_mu));
                int max_b = is_res_b ? 8 : 4;
                nmax = std::max(0, std::min(max_b, nmax));
                try {
                    kep3::lambert_problem lp(r1_arr, r2_arr, tof, effective_mu, false, nmax);
                    const auto& v0_list = lp.get_v0();
                    const auto& v1_list = lp.get_v1();
                    for (size_t b = 0; b < v0_list.size(); ++b) {
                        double cost = 0.0;
                        if (k == 0) {
                            double vx = v0_list[b][0] - p1.vx;
                            double vy = v0_list[b][1] - p1.vy;
                            double vz = v0_list[b][2] - p1.vz;
                            cost = std::sqrt(vx*vx + vy*vy + vz*vz);
                        } else {
                            const auto& prev = out_sol->legs[k - 1];
                            cost = pykep_flyby_dv(prev.v_inf_arr_x, prev.v_inf_arr_y, prev.v_inf_arr_z,
                                                 v0_list[b][0] - p1.vx, v0_list[b][1] - p1.vy, v0_list[b][2] - p1.vz,
                                                 p1.mu, p1.safe_radius);
                        }
                        if (cost < best_cost) {
                            best_cost = cost;
                            v1_lam = v0_list[b];
                            v2_lam = v1_list[b];
                            lam_ok = true;
                        }
                    }
                } catch (...) {}

                // 顺行开普勒转移为行星际工程唯一可行解，避免逆行求解产生几十km/s虚假解与多重异常耗时

                if (!lam_ok) {
                    leg.is_feasible = 0;
                    out_sol->all_feasible = 0;
                    v1_lam = { p1.vx, p1.vy, p1.vz };
                    v2_lam = { p2.vx, p2.vy, p2.vz };
                    double penalty_flyby_kms = 15.0;
                    leg.flyby_dv_kms = penalty_flyby_kms;
                    total_flyby_dv += penalty_flyby_kms;
                    total_dv += penalty_flyby_kms;
                }

                if (k == 0) {
                    cur_v_dep = v1_lam;
                    leg.v_inf_dep_x = cur_v_dep[0] - p1.vx;
                    leg.v_inf_dep_y = cur_v_dep[1] - p1.vy;
                    leg.v_inf_dep_z = cur_v_dep[2] - p1.vz;
                    double v_inf_mag = std::sqrt(leg.v_inf_dep_x * leg.v_inf_dep_x + leg.v_inf_dep_y * leg.v_inf_dep_y + leg.v_inf_dep_z * leg.v_inf_dep_z);

                    bool is_central_dep = is_central_body(p1, effective_mu);
                    if (is_central_dep) {
                        out_sol->c3_kms2 = 0.0;
                        double r_dest_km = std::sqrt(p2.rx * p2.rx + p2.ry * p2.ry + p2.rz * p2.rz);

                        // 停泊轨道角动量矢量 h_park = p1.r x p1.v
                        double h_park_x = p1.ry * p1.vz - p1.rz * p1.vy;
                        double h_park_y = p1.rz * p1.vx - p1.rx * p1.vz;
                        double h_park_z = p1.rx * p1.vy - p1.ry * p1.vx;
                        double h_park_mag = std::sqrt(h_park_x * h_park_x + h_park_y * h_park_y + h_park_z * h_park_z);
                        if (h_park_mag < 1e-6) {
                            if (dep_inc_deg > 0.001) {
                                double i_rad = dep_inc_deg * DEG2RAD;
                                h_park_x = 0; h_park_y = -std::sin(i_rad); h_park_z = std::cos(i_rad);
                                h_park_mag = 1.0;
                            } else {
                                h_park_x = p2.ry * p2.vz - p2.rz * p2.vy;
                                h_park_y = p2.rz * p2.vx - p2.rx * p2.vz;
                                h_park_z = p2.rx * p2.vy - p2.ry * p2.vx;
                                h_park_mag = std::sqrt(h_park_x * h_park_x + h_park_y * h_park_y + h_park_z * h_park_z);
                            }
                        }

                        double beta_rad = 0.0;
                        if (h_park_mag > 1e-6 && r_dest_km > 1e-6) {
                            double sin_beta = std::abs(p2.rx * h_park_x + p2.ry * h_park_y + p2.rz * h_park_z) / (r_dest_km * h_park_mag);
                            beta_rad = std::asin(std::max(-1.0, std::min(1.0, sin_beta)));
                        }

                        out_sol->launch_dv_kms = calc_central_departure_dv(p1.mu, p1.radius, dep_pe_km, dep_ap_km, r_dest_km, beta_rad, nullptr);

                        double h_eff_x = (h_park_mag > 1e-6) ? h_park_x : (r1_arr[1] * cur_v_dep[2] - r1_arr[2] * cur_v_dep[1]);
                        double h_eff_y = (h_park_mag > 1e-6) ? h_park_y : (r1_arr[2] * cur_v_dep[0] - r1_arr[0] * cur_v_dep[2]);
                        double h_eff_z = (h_park_mag > 1e-6) ? h_park_z : (r1_arr[0] * cur_v_dep[1] - r1_arr[1] * cur_v_dep[0]);
                        double h_eff_mag = std::sqrt(h_eff_x * h_eff_x + h_eff_y * h_eff_y + h_eff_z * h_eff_z);
                        if (h_eff_mag < 1e-6) {
                            h_eff_x = r1_arr[1] * p2.rz - r1_arr[2] * p2.ry;
                            h_eff_y = r1_arr[2] * p2.rx - r1_arr[0] * p2.rz;
                            h_eff_z = r1_arr[0] * p2.ry - r1_arr[1] * p2.rx;
                            h_eff_mag = std::sqrt(h_eff_x * h_eff_x + h_eff_y * h_eff_y + h_eff_z * h_eff_z);
                        }
                        if (h_eff_mag > 1e-6) {
                            out_sol->ejection_inc_deg = std::acos(std::max(-1.0, std::min(1.0, h_eff_z / h_eff_mag))) * RAD2DEG;
                            double nx = -h_eff_y;
                            double ny = h_eff_x;
                            double n_mag = std::sqrt(nx * nx + ny * ny);
                            if (n_mag > 1e-6) {
                                double lan = std::atan2(ny, nx) * RAD2DEG;
                                if (lan < 0.0) lan += 360.0;
                                out_sol->ejection_lan_deg = lan;
                            } else {
                                out_sol->ejection_lan_deg = 0.0;
                            }
                        } else {
                            out_sol->ejection_inc_deg = 0.0;
                            out_sol->ejection_lan_deg = 0.0;
                        }
                        out_sol->ejection_angle_deg = 180.0;
                    } else {
                        out_sol->c3_kms2 = v_inf_mag * v_inf_mag;
                        out_sol->launch_dv_kms = calc_vis_viva_dep_inc(v_inf_mag, p1.mu, p1.radius, dep_pe_km, dep_ap_km, dep_inc_deg, leg.v_inf_dep_z, &out_sol->ejection_inc_deg);

                        double ra = std::atan2(leg.v_inf_dep_y, leg.v_inf_dep_x) * RAD2DEG;
                        if (ra < 0) ra += 360.0;
                        out_sol->ejection_lan_deg = ra;

                        double r_park = p1.radius + dep_pe_km;
                        double e_hyp = 1.0 + (r_park * v_inf_mag * v_inf_mag) / std::max(1e-6, p1.mu);
                        double beta = std::acos(-1.0 / std::max(1.0001, e_hyp)) * RAD2DEG;
                        double ej_ang = 180.0 - (beta - 90.0);
                        if (ej_ang > 180.0) ej_ang = 360.0 - ej_ang;
                        out_sol->ejection_angle_deg = ej_ang;
                    }
                    total_dv += out_sol->launch_dv_kms;
                    leg.is_feasible = 1;
                } else {
                    leg.v_inf_dep_x = v1_lam[0] - p1.vx;
                    leg.v_inf_dep_y = v1_lam[1] - p1.vy;
                    leg.v_inf_dep_z = v1_lam[2] - p1.vz;

                    NativeLeg1DSMResult& prev_leg = out_sol->legs[k - 1];
                    double eq_v2, ineq_d, delta_deg, rp_km;
                    int fb_ok = pykep_flyby_con(prev_leg.v_inf_arr_x, prev_leg.v_inf_arr_y, prev_leg.v_inf_arr_z,
                                                leg.v_inf_dep_x, leg.v_inf_dep_y, leg.v_inf_dep_z,
                                                p1.mu, p1.safe_radius, &eq_v2, &ineq_d, &delta_deg, &rp_km);

                    leg.flyby_turn_deg = delta_deg;
                    leg.flyby_rp_km = rp_km;
                    bool hit_planet = (rp_km < p1.safe_radius * 0.999);
                    leg.is_feasible = hit_planet ? 0 : 1;
                    if (hit_planet) out_sol->all_feasible = 0;

                    int fb_strat = (flyby_strategies != nullptr) ? flyby_strategies[k - 1] : 0;
                    double fb_impulse = pykep_flyby_dv(prev_leg.v_inf_arr_x, prev_leg.v_inf_arr_y, prev_leg.v_inf_arr_z,
                                                      leg.v_inf_dep_x, leg.v_inf_dep_y, leg.v_inf_dep_z,
                                                      p1.mu, p1.safe_radius);
                    leg.flyby_dv_kms = fb_impulse;
                    total_flyby_dv += fb_impulse;
                    total_dv += fb_impulse;
                }

                leg.v1x = v1_lam[0]; leg.v1y = v1_lam[1]; leg.v1z = v1_lam[2];
                leg.v2x = v2_lam[0]; leg.v2y = v2_lam[1]; leg.v2z = v2_lam[2];
                leg.v_inf_arr_x = v2_lam[0] - p2.vx;
                leg.v_inf_arr_y = v2_lam[1] - p2.vy;
                leg.v_inf_arr_z = v2_lam[2] - p2.vz;
                leg.has_dsm = 0;
                leg.dsm.dv_mag_ms = (k == 0) ? (out_sol->launch_dv_kms * 1000.0) : (leg.flyby_dv_kms * 1000.0);
                leg.dsm.prograde_ms = leg.dsm.dv_mag_ms;
                leg.dsm.normal_ms = 0.0;
                leg.dsm.radial_ms = 0.0;
                cur_v_dep = v2_lam;
            }
            else {
                double dt1 = eta * tof;
                double dt2 = (1.0 - eta) * tof;

                std::array<double, 3> r_dsm = {p1.rx, p1.ry, p1.rz};
                std::array<double, 3> v_dsm_m = {0, 0, 0};
                std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
                std::array<double, 3> v_dsm_p = {0, 0, 0};
                std::array<double, 3> v2_arr = {0, 0, 0};
                bool lam_ok = false;
                double best_dsm = 1e8;

                if (k == 0) {
                    double r1_len_i = std::sqrt(r1_arr[0]*r1_arr[0] + r1_arr[1]*r1_arr[1] + r1_arr[2]*r1_arr[2]);
                    double r2_len_i = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                    double r_avg_i = (r1_len_i + r2_len_i) * 0.5;
                    double t_orb_i = 2.0 * PI * std::sqrt((r_avg_i * r_avg_i * r_avg_i) / effective_mu);
                    int nmax_init = (t_orb_i > 10.0) ? (int)std::floor(tof / t_orb_i + 0.25) : 0;
                    bool is_res_init = (std::abs(p1.mu - p2.mu) < 1e-4 && std::abs(p1.radius - p2.radius) < 1e-4 && !is_central_body(p1, effective_mu));
                    if (is_res_init && nmax_init < 1) nmax_init = 1;
                    nmax_init = std::max(0, std::min(4, nmax_init));

                    // 行星当地局部开普勒轨道标架 {u_v, u_h, u_r}
                    double v_p1_mag = std::sqrt(p1.vx*p1.vx + p1.vy*p1.vy + p1.vz*p1.vz);
                    std::array<double, 3> u_v = { 1.0, 0.0, 0.0 };
                    if (v_p1_mag > 1e-5) {
                        u_v = { p1.vx / v_p1_mag, p1.vy / v_p1_mag, p1.vz / v_p1_mag };
                    }
                    double hx_p1 = r1_arr[1]*p1.vz - r1_arr[2]*p1.vy;
                    double hy_p1 = r1_arr[2]*p1.vx - r1_arr[0]*p1.vz;
                    double hz_p1 = r1_arr[0]*p1.vy - r1_arr[1]*p1.vx;
                    double h_p1_mag = std::sqrt(hx_p1*hx_p1 + hy_p1*hy_p1 + hz_p1*hz_p1);
                    std::array<double, 3> u_h = { 0.0, 0.0, 1.0 };
                    if (h_p1_mag > 1e-5) {
                        u_h = { hx_p1 / h_p1_mag, hy_p1 / h_p1_mag, hz_p1 / h_p1_mag };
                    }
                    std::array<double, 3> u_r = {
                        u_v[1]*u_h[2] - u_v[2]*u_h[1],
                        u_v[2]*u_h[0] - u_v[0]*u_h[2],
                        u_v[0]*u_h[1] - u_v[1]*u_h[0]
                    };

                    // 下游天体势能跨越所需双曲超速精确推导 (用于同星共振调相 ΔV-EGA 的无动力飞掠能级闭环)
                    // 纯天体力学第一性原理: 无动力借力两体相对能量守恒 ||v_inf,arr|| = ||v_inf,dep||
                    double v_inf_needed = 0.0;
                    std::array<double, 3> u_needed = { 0.0, 0.0, 0.0 };
                    bool has_u_needed = false;
                    if (is_res_init && leg_count > 1) {
                        const NativePlanetState& p_down = arr_planets[1];
                        std::array<double, 3> r_down = { p_down.rx, p_down.ry, p_down.rz };
                        double tof_down = tofs_sec[1];
                        if (tof_down > 10.0) {
                            try {
                                double r_down_len = std::sqrt(r_down[0]*r_down[0] + r_down[1]*r_down[1] + r_down[2]*r_down[2]);
                                double r_avg_down = (r2_len_i + r_down_len) * 0.5;
                                double t_orb_down = 2.0 * PI * std::sqrt((r_avg_down * r_avg_down * r_avg_down) / effective_mu);
                                int nmax_down = (t_orb_down > 10.0) ? (int)std::floor(tof_down / t_orb_down + 0.25) : 0;
                                nmax_down = std::max(0, std::min(6, nmax_down));
                                kep3::lambert_problem lp_down(r2_arr, r_down, tof_down, effective_mu, false, nmax_down);
                                const auto& v0_down = lp_down.get_v0();
                                double best_v_inf_down = 1e9;
                                for (const auto& v0_cand : v0_down) {
                                    double vx_inf = v0_cand[0] - p2.vx;
                                    double vy_inf = v0_cand[1] - p2.vy;
                                    double vz_inf = v0_cand[2] - p2.vz;
                                    double v_inf_cand = std::sqrt(vx_inf * vx_inf + vy_inf * vy_inf + vz_inf * vz_inf);
                                    if (v_inf_cand < best_v_inf_down) {
                                        best_v_inf_down = v_inf_cand;
                                        if (v_inf_cand > 1e-5) {
                                            u_needed = { vx_inf / v_inf_cand, vy_inf / v_inf_cand, vz_inf / v_inf_cand };
                                            has_u_needed = true;
                                        }
                                    }
                                }
                                if (best_v_inf_down < 1e8) {
                                    v_inf_needed = best_v_inf_down;
                                }
                            } catch (...) {}
                        }
                        if (v_inf_needed < 1e-4) {
                            double r_down_len = std::sqrt(r_down[0]*r_down[0] + r_down[1]*r_down[1] + r_down[2]*r_down[2]);
                            if (r_down_len > 1e-4) {
                                double a_down = (r2_len_i + r_down_len) * 0.5;
                                double v_down_req = std::sqrt(std::max(1e-4, effective_mu * (2.0 / std::max(1.0, r2_len_i) - 1.0 / std::max(1.0, a_down))));
                                v_inf_needed = std::abs(v_down_req - v_p1_mag);
                            }
                        }
                    }

                    auto eval_init_dep_candidate = [&](const std::array<double, 3>& v_try,
                                                       std::array<double, 3>& out_r_d,
                                                       std::array<double, 3>& out_vm,
                                                       std::array<double, 3>& out_vp,
                                                       std::array<double, 3>& out_v2,
                                                       double& out_dsm) -> double
                    {
                        double vx_inf = v_try[0] - p1.vx;
                        double vy_inf = v_try[1] - p1.vy;
                        double vz_inf = v_try[2] - p1.vz;
                        double v_inf_mag = std::sqrt(vx_inf*vx_inf + vy_inf*vy_inf + vz_inf*vz_inf);
                        if (v_inf_mag > 25.0) return 1e9;

                        double launch_dv = 0.0;
                        if (is_central_body(p1, effective_mu)) {
                            double r_dest_km = std::sqrt(p2.rx*p2.rx + p2.ry*p2.ry + p2.rz*p2.rz);
                            launch_dv = calc_central_departure_dv(p1.mu, p1.radius, dep_pe_km, dep_ap_km, r_dest_km, 0.0, nullptr);
                        } else {
                            launch_dv = calc_vis_viva_dep_inc(v_inf_mag, p1.mu, p1.radius, dep_pe_km, dep_ap_km, dep_inc_deg, vz_inf, nullptr);
                        }

                        std::array<double, 3> r_prop = r1_arr;
                        std::array<double, 3> v_prop = v_try;
                        try {
                            pykep_propagate_lagrangian(r_prop, v_prop, dt1, effective_mu);
                        } catch (...) {
                            return 1e9;
                        }

                        double r_prop_len = std::sqrt(r_prop[0]*r_prop[0] + r_prop[1]*r_prop[1] + r_prop[2]*r_prop[2]);
                        double r2_len_dsm = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                        double r_avg_dsm = (r_prop_len + r2_len_dsm) * 0.5;
                        double t_orb_dsm = 2.0 * PI * std::sqrt((r_avg_dsm * r_avg_dsm * r_avg_dsm) / effective_mu);
                        int nmax = (t_orb_dsm > 10.0) ? (int)std::floor(dt2 / t_orb_dsm + 0.25) : 0;
                        nmax = std::max(0, std::min(6, nmax));

                        try {
                            kep3::lambert_problem lp(r_prop, r2_arr, dt2, effective_mu, false, nmax);
                            const auto& v0_list = lp.get_v0();
                            const auto& v1_list = lp.get_v1();
                            double best_dsm_local = 1e9;
                            size_t best_b = 0;
                            bool found = false;
                            for (size_t b = 0; b < v0_list.size(); ++b) {
                                double dv_x = v0_list[b][0] - v_prop[0];
                                double dv_y = v0_list[b][1] - v_prop[1];
                                double dv_z = v0_list[b][2] - v_prop[2];
                                double dsm_mag = std::sqrt(dv_x*dv_x + dv_y*dv_y + dv_z*dv_z);
                                double v0_mag = std::sqrt(v0_list[b][0]*v0_list[b][0] + v0_list[b][1]*v0_list[b][1] + v0_list[b][2]*v0_list[b][2]);
                                if (!std::isnan(dsm_mag) && !std::isinf(dsm_mag) && v0_mag < 90.0 && dsm_mag < best_dsm_local) {
                                    best_dsm_local = dsm_mag;
                                    best_b = b;
                                    found = true;
                                }
                            }
                            if (found) {
                                out_r_d = r_prop;
                                out_vm = v_prop;
                                out_vp = v0_list[best_b];
                                out_v2 = v1_list[best_b];
                                out_dsm = best_dsm_local;
                                double cand_cost = launch_dv + best_dsm_local;

                                // 天体当地环绕速度标度运载注入上限惩罚 (通用齐次物理准则: v_inf <= 0.75 v_circ)
                                if (!is_central_body(p1, effective_mu) && p1.radius > 0) {
                                    double v_circ_dep = std::sqrt(p1.mu / std::max(1.0, p1.radius + dep_pe_km));
                                    double v_inf_launch_limit = 0.75 * v_circ_dep;
                                    if (v_inf_mag > v_inf_launch_limit) {
                                        double over_v = v_inf_mag - v_inf_launch_limit;
                                        cand_cost += 2.0 * over_v * (1.0 + over_v);
                                    }
                                }

                                if (v_inf_needed > 0.0) {
                                    double vax = out_v2[0] - p2.vx;
                                    double vay = out_v2[1] - p2.vy;
                                    double vaz = out_v2[2] - p2.vz;
                                    double v_inf_arr = std::sqrt(vax*vax + vay*vay + vaz*vaz);
                                    double energy_mismatch = std::abs(v_inf_arr - v_inf_needed);
                                    double dv_turn_excess = 0.0;
                                    if (has_u_needed && v_inf_arr > 1e-5) {
                                        double cos_d = (vax * u_needed[0] + vay * u_needed[1] + vaz * u_needed[2]) / v_inf_arr;
                                        cos_d = std::max(-1.0, std::min(1.0, cos_d));
                                        double turn_req = std::acos(cos_d);
                                        double safe_rp = p2.radius + 300.0;
                                        double e_min = 1.0 + (safe_rp * v_inf_arr * v_inf_arr) / std::max(1e-6, p2.mu);
                                        double delta_max = 2.0 * std::asin(1.0 / std::max(1.0001, e_min));
                                        if (turn_req > delta_max) {
                                            double turn_excess = turn_req - delta_max;
                                            dv_turn_excess = 2.0 * std::max(v_inf_arr, v_inf_needed) * std::sin(turn_excess * 0.5);
                                        }
                                    }
                                    cand_cost += 1.5 * (energy_mismatch + dv_turn_excess);
                                }
                                return cand_cost;
                            }
                        } catch (...) {}

                        return 1e9;
                    };

                    std::vector<std::array<double, 3>> init_v_candidates;

                    // 1. 开普勒两体 Lambert 全程转移初速
                    try {
                        kep3::lambert_problem lp_init(r1_arr, r2_arr, tof, effective_mu, false, nmax_init);
                        for (const auto& v0 : lp_init.get_v0()) {
                            init_v_candidates.push_back(v0);
                        }
                    } catch (...) {}

                    // 2. 共振初速候选 (针对同星共振蓄能 ΔV-EGA，100% 纯天体力学公式解析推导)
                    if (is_res_init) {
                        pykep_synthesize_resonant_departure_candidates(
                            p1.mu, p1.radius, dep_pe_km,
                            p1.vx, p1.vy, p1.vz,
                            r1_len_i, v_p1_mag,
                            u_v, u_r, u_h,
                            effective_mu,
                            tof,
                            init_v_candidates,
                            v_inf_needed);
                    } else {
                        // 异星霍曼初速候选 (含顺行与小幅径向/法向速度扰动网格，确保 100% 物理覆盖)
                        double a_hoh = (r1_len_i + r2_len_i) * 0.5;
                        double v_sq_h = effective_mu * (2.0 / std::max(1.0, r1_len_i) - 1.0 / std::max(1.0, a_hoh));
                        if (v_sq_h > 1e-4) {
                            double v_req_h = std::sqrt(v_sq_h);
                            double hoh_scales[] = { 0.88, 0.94, 0.97, 1.0, 1.03, 1.06, 1.12 };
                            for (double hs : hoh_scales) {
                                double v_sc = v_req_h * hs;
                                init_v_candidates.push_back({ v_sc * u_v[0], v_sc * u_v[1], v_sc * u_v[2] });
                                init_v_candidates.push_back({ v_sc * u_v[0] + 0.4 * u_r[0], v_sc * u_v[1] + 0.4 * u_r[1], v_sc * u_v[2] + 0.4 * u_r[2] });
                                init_v_candidates.push_back({ v_sc * u_v[0] - 0.4 * u_r[0], v_sc * u_v[1] - 0.4 * u_r[1], v_sc * u_v[2] - 0.4 * u_r[2] });
                            }
                        }
                    }

                    // 评估所有初速候选 (初始阈值设为 1e8，彻底消灭 1e9 失败解假阳性渗透与栈内存未初始化)
                    double best_cost = 1e8;
                    std::array<double, 3> best_v_dep = { p1.vx, p1.vy, p1.vz };
                    std::array<double, 3> best_r_dsm = r1_arr;
                    std::array<double, 3> best_v_dsm_m = {0,0,0};
                    std::array<double, 3> best_v_dsm_p = {0,0,0};
                    std::array<double, 3> best_v2 = {0,0,0};
                    double best_dsm_val = 1e8;

                    for (const auto& v_cand : init_v_candidates) {
                        std::array<double, 3> t_r_d = {0,0,0}, t_vm = {0,0,0}, t_vp = {0,0,0}, t_v2 = {0,0,0};
                        double t_dsm = 1e9;
                        double c = eval_init_dep_candidate(v_cand, t_r_d, t_vm, t_vp, t_v2, t_dsm);
                        if (c < 1e8 && c < best_cost) {
                            best_cost = c;
                            best_v_dep = v_cand;
                            best_r_dsm = t_r_d;
                            best_v_dsm_m = t_vm;
                            best_v_dsm_p = t_vp;
                            best_v2 = t_v2;
                            best_dsm_val = t_dsm;
                        }
                    }

                    // 3. 极速 3D 局部坐标下降微调 (Coordinate Descent Refinement)
                    if (best_cost < 1e8) {
                        double step_v = 0.40; // km/s
                        for (int iter = 0; iter < 4; ++iter) {
                            bool improved = false;
                            double test_steps[] = { -step_v, step_v };
                            for (double ds : test_steps) {
                                // 沿轨道公转方向微调
                                std::array<double, 3> try_v = {
                                    best_v_dep[0] + ds * u_v[0],
                                    best_v_dep[1] + ds * u_v[1],
                                    best_v_dep[2] + ds * u_v[2]
                                };
                                std::array<double, 3> t_r_d = {0,0,0}, t_vm = {0,0,0}, t_vp = {0,0,0}, t_v2 = {0,0,0};
                                double t_dsm = 1e9;
                                double c = eval_init_dep_candidate(try_v, t_r_d, t_vm, t_vp, t_v2, t_dsm);
                                if (c < 1e8 && c < best_cost) {
                                    best_cost = c;
                                    best_v_dep = try_v;
                                    best_r_dsm = t_r_d;
                                    best_v_dsm_m = t_vm;
                                    best_v_dsm_p = t_vp;
                                    best_v2 = t_v2;
                                    best_dsm_val = t_dsm;
                                    improved = true;
                                }

                                // 沿径向微调
                                std::array<double, 3> try_v_r = {
                                    best_v_dep[0] + ds * u_r[0],
                                    best_v_dep[1] + ds * u_r[1],
                                    best_v_dep[2] + ds * u_r[2]
                                };
                                c = eval_init_dep_candidate(try_v_r, t_r_d, t_vm, t_vp, t_v2, t_dsm);
                                if (c < 1e8 && c < best_cost) {
                                    best_cost = c;
                                    best_v_dep = try_v_r;
                                    best_r_dsm = t_r_d;
                                    best_v_dsm_m = t_vm;
                                    best_v_dsm_p = t_vp;
                                    best_v2 = t_v2;
                                    best_dsm_val = t_dsm;
                                    improved = true;
                                }

                                // 沿法向微调
                                std::array<double, 3> try_v_h = {
                                    best_v_dep[0] + ds * u_h[0],
                                    best_v_dep[1] + ds * u_h[1],
                                    best_v_dep[2] + ds * u_h[2]
                                };
                                c = eval_init_dep_candidate(try_v_h, t_r_d, t_vm, t_vp, t_v2, t_dsm);
                                if (c < 1e8 && c < best_cost) {
                                    best_cost = c;
                                    best_v_dep = try_v_h;
                                    best_r_dsm = t_r_d;
                                    best_v_dsm_m = t_vm;
                                    best_v_dsm_p = t_vp;
                                    best_v2 = t_v2;
                                    best_dsm_val = t_dsm;
                                    improved = true;
                                }
                            }
                            step_v *= 0.5;
                            if (!improved && iter > 0) break;
                        }
                    }

                    lam_ok = (best_cost < 1e8 && best_dsm_val < 1e8);
                    if (!lam_ok) {
                        double sign_dir = (r2_len_i >= r1_len_i) ? 1.0 : -1.0;
                        cur_v_dep = { p1.vx + sign_dir * 5.0 * u_v[0], p1.vy + sign_dir * 5.0 * u_v[1], p1.vz + sign_dir * 5.0 * u_v[2] };
                        leg.is_feasible = 0;
                        out_sol->all_feasible = 0;
                    } else {
                        cur_v_dep = best_v_dep;
                        r_dsm = best_r_dsm;
                        v_dsm_m = best_v_dsm_m;
                        v_dsm_p = best_v_dsm_p;
                        v2_arr = best_v2;
                        best_dsm = best_dsm_val;
                    }

                    leg.v_inf_dep_x = cur_v_dep[0] - p1.vx;
                    leg.v_inf_dep_y = cur_v_dep[1] - p1.vy;
                    leg.v_inf_dep_z = cur_v_dep[2] - p1.vz;
                    double v_inf_mag = std::sqrt(leg.v_inf_dep_x * leg.v_inf_dep_x + leg.v_inf_dep_y * leg.v_inf_dep_y + leg.v_inf_dep_z * leg.v_inf_dep_z);

                    bool is_central_dep = is_central_body(p1, effective_mu);
                    if (is_central_dep) {
                        out_sol->c3_kms2 = 0.0;
                        double r_dest_km = std::sqrt(p2.rx * p2.rx + p2.ry * p2.ry + p2.rz * p2.rz);

                        // 停泊轨道角动量矢量 h_park = p1.r x p1.v
                        double h_park_x = p1.ry * p1.vz - p1.rz * p1.vy;
                        double h_park_y = p1.rz * p1.vx - p1.rx * p1.vz;
                        double h_park_z = p1.rx * p1.vy - p1.ry * p1.vx;
                        double h_park_mag = std::sqrt(h_park_x * h_park_x + h_park_y * h_park_y + h_park_z * h_park_z);
                        if (h_park_mag < 1e-6) {
                            if (dep_inc_deg > 0.001) {
                                double inc_rad = dep_inc_deg * DEG2RAD;
                                h_park_x = 0.0;
                                h_park_y = -std::sin(inc_rad);
                                h_park_z = std::cos(inc_rad);
                                h_park_mag = 1.0;
                            } else {
                                h_park_x = p2.ry * p2.vz - p2.rz * p2.vy;
                                h_park_y = p2.rz * p2.vx - p2.rx * p2.vz;
                                h_park_z = p2.rx * p2.vy - p2.ry * p2.vx;
                                h_park_mag = std::sqrt(h_park_x * h_park_x + h_park_y * h_park_y + h_park_z * h_park_z);
                            }
                        }

                        double beta_rad = 0.0;
                        if (h_park_mag > 1e-6 && r_dest_km > 1e-6) {
                            double sin_beta = std::abs(p2.rx * h_park_x + p2.ry * h_park_y + p2.rz * h_park_z) / (r_dest_km * h_park_mag);
                            beta_rad = std::asin(std::max(-1.0, std::min(1.0, sin_beta)));
                        }

                        out_sol->launch_dv_kms = calc_central_departure_dv(p1.mu, p1.radius, dep_pe_km, dep_ap_km, r_dest_km, beta_rad, nullptr);

                        double h_eff_x = (h_park_mag > 1e-6) ? h_park_x : (r1_arr[1] * cur_v_dep[2] - r1_arr[2] * cur_v_dep[1]);
                        double h_eff_y = (h_park_mag > 1e-6) ? h_park_y : (r1_arr[2] * cur_v_dep[0] - r1_arr[0] * cur_v_dep[2]);
                        double h_eff_z = (h_park_mag > 1e-6) ? h_park_z : (r1_arr[0] * cur_v_dep[1] - r1_arr[1] * cur_v_dep[0]);
                        double h_eff_mag = std::sqrt(h_eff_x * h_eff_x + h_eff_y * h_eff_y + h_eff_z * h_eff_z);
                        if (h_eff_mag < 1e-6) {
                            h_eff_x = r1_arr[1] * p2.rz - r1_arr[2] * p2.ry;
                            h_eff_y = r1_arr[2] * p2.rx - r1_arr[0] * p2.rz;
                            h_eff_z = r1_arr[0] * p2.ry - r1_arr[1] * p2.rx;
                            h_eff_mag = std::sqrt(h_eff_x * h_eff_x + h_eff_y * h_eff_y + h_eff_z * h_eff_z);
                        }
                        if (h_eff_mag > 1e-6) {
                            out_sol->ejection_inc_deg = std::acos(std::max(-1.0, std::min(1.0, h_eff_z / h_eff_mag))) * RAD2DEG;
                            double nx = -h_eff_y;
                            double ny = h_eff_x;
                            double n_mag = std::sqrt(nx * nx + ny * ny);
                            if (n_mag > 1e-6) {
                                double lan = std::atan2(ny, nx) * RAD2DEG;
                                if (lan < 0.0) lan += 360.0;
                                out_sol->ejection_lan_deg = lan;
                            } else {
                                out_sol->ejection_lan_deg = 0.0;
                            }
                        } else {
                            out_sol->ejection_inc_deg = 0.0;
                            out_sol->ejection_lan_deg = 0.0;
                        }
                        out_sol->ejection_angle_deg = 180.0;
                    } else {
                        out_sol->c3_kms2 = v_inf_mag * v_inf_mag;
                        out_sol->launch_dv_kms = calc_vis_viva_dep_inc(v_inf_mag, p1.mu, p1.radius, dep_pe_km, dep_ap_km, dep_inc_deg, leg.v_inf_dep_z, &out_sol->ejection_inc_deg);

                        double ra = std::atan2(leg.v_inf_dep_y, leg.v_inf_dep_x) * RAD2DEG;
                        if (ra < 0) ra += 360.0;
                        out_sol->ejection_lan_deg = ra;

                        double r_park = p1.radius + dep_pe_km;
                        double e_hyp = 1.0 + (r_park * v_inf_mag * v_inf_mag) / std::max(1e-6, p1.mu);
                        double beta = std::acos(-1.0 / std::max(1.0001, e_hyp)) * RAD2DEG;
                        double ej_ang = 180.0 - (beta - 90.0);
                        if (ej_ang > 180.0) ej_ang = 360.0 - ej_ang;
                        out_sol->ejection_angle_deg = ej_ang;
                    }
                    total_dv += out_sol->launch_dv_kms;
                    leg.v1x = cur_v_dep[0]; leg.v1y = cur_v_dep[1]; leg.v1z = cur_v_dep[2];
                } else {
                    NativeLeg1DSMResult& prev_leg = out_sol->legs[k - 1];
                    double v_in_x = prev_leg.v_inf_arr_x;
                    double v_in_y = prev_leg.v_inf_arr_y;
                    double v_in_z = prev_leg.v_inf_arr_z;
                    double v_in_mag = std::sqrt(v_in_x*v_in_x + v_in_y*v_in_y + v_in_z*v_in_z);

                    std::array<double, 3> r1_arr = {p1.rx, p1.ry, p1.rz};
                    double r1_len_f = std::sqrt(r1_arr[0]*r1_arr[0] + r1_arr[1]*r1_arr[1] + r1_arr[2]*r1_arr[2]);
                    double r2_len_f = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);

                    // 天体近拱点安全半径与双曲偏折角理论极限 (Vis-Viva & Hyperbolic Deflection Limit)
                    double safe_rp = p1.safe_radius;
                    double e_min = 1.0 + (safe_rp * v_in_mag * v_in_mag) / std::max(1e-6, p1.mu);
                    double delta_max = 2.0 * std::asin(1.0 / std::max(1.0001, e_min)) * 0.998;

                    double v_p1_mag = std::sqrt(p1.vx*p1.vx + p1.vy*p1.vy + p1.vz*p1.vz);
                    double u_in[3] = { 1, 0, 0 };
                    if (v_in_mag > 1e-5) {
                        u_in[0] = v_in_x / v_in_mag;
                        u_in[1] = v_in_y / v_in_mag;
                        u_in[2] = v_in_z / v_in_mag;
                    }
                    double u_p1[3] = { 1, 0, 0 };
                    if (v_p1_mag > 1e-5) {
                        u_p1[0] = p1.vx / v_p1_mag;
                        u_p1[1] = p1.vy / v_p1_mag;
                        u_p1[2] = p1.vz / v_p1_mag;
                    }
                    double u_r1[3] = { 1, 0, 0 };
                    if (r1_len_f > 1e-5) {
                        u_r1[0] = r1_arr[0] / r1_len_f;
                        u_r1[1] = r1_arr[1] / r1_len_f;
                        u_r1[2] = r1_arr[2] / r1_len_f;
                    }
                    // 行星轨道角动量矢量 h = r1 x v_p1
                    double hx = r1_arr[1]*p1.vz - r1_arr[2]*p1.vy;
                    double hy = r1_arr[2]*p1.vx - r1_arr[0]*p1.vz;
                    double hz = r1_arr[0]*p1.vy - r1_arr[1]*p1.vx;
                    double h_len = std::sqrt(hx*hx + hy*hy + hz*hz);
                    double u_h[3] = { 0, 0, 1 };
                    if (h_len > 1e-6) {
                        u_h[0] = hx / h_len; u_h[1] = hy / h_len; u_h[2] = hz / h_len;
                    }
                    // 轨道平面切向单位矢量: u_theta = u_h x u_r1
                    double u_theta[3] = {
                        u_h[1]*u_r1[2] - u_h[2]*u_r1[1],
                        u_h[2]*u_r1[0] - u_h[0]*u_r1[2],
                        u_h[0]*u_r1[1] - u_h[1]*u_r1[0]
                    };

                    // 构造借力偏折锥面的局部正交标架 {u_in, e1, e2}
                    double dot_th_in = u_theta[0]*u_in[0] + u_theta[1]*u_in[1] + u_theta[2]*u_in[2];
                    double w1[3] = {
                        u_theta[0] - dot_th_in * u_in[0],
                        u_theta[1] - dot_th_in * u_in[1],
                        u_theta[2] - dot_th_in * u_in[2]
                    };
                    double w1_len = std::sqrt(w1[0]*w1[0] + w1[1]*w1[1] + w1[2]*w1[2]);
                    if (w1_len < 1e-5) {
                        double dot_p1_in = u_p1[0]*u_in[0] + u_p1[1]*u_in[1] + u_p1[2]*u_in[2];
                        w1[0] = u_p1[0] - dot_p1_in * u_in[0];
                        w1[1] = u_p1[1] - dot_p1_in * u_in[1];
                        w1[2] = u_p1[2] - dot_p1_in * u_in[2];
                        w1_len = std::sqrt(w1[0]*w1[0] + w1[1]*w1[1] + w1[2]*w1[2]);
                        if (w1_len < 1e-5) {
                            w1[0] = u_h[0]; w1[1] = u_h[1]; w1[2] = u_h[2];
                            w1_len = std::sqrt(w1[0]*w1[0] + w1[1]*w1[1] + w1[2]*w1[2]);
                        }
                    }
                    double e1[3] = { w1[0] / w1_len, w1[1] / w1_len, w1[2] / w1_len };
                    double e2[3] = {
                        u_in[1]*e1[2] - u_in[2]*e1[1],
                        u_in[2]*e1[0] - u_in[0]*e1[2],
                        u_in[0]*e1[1] - u_in[1]*e1[0]
                    };

                    auto get_cone_v_dep = [&](double delta, double beta) -> std::array<double, 3> {
                        double cos_d = std::cos(delta);
                        double sin_d = std::sin(delta);
                        double cos_b = std::cos(beta);
                        double sin_b = std::sin(beta);
                        double u_out[3] = {
                            cos_d * u_in[0] + sin_d * (cos_b * e1[0] + sin_b * e2[0]),
                            cos_d * u_in[1] + sin_d * (cos_b * e1[1] + sin_b * e2[1]),
                            cos_d * u_in[2] + sin_d * (cos_b * e1[2] + sin_b * e2[2])
                        };
                        return {
                            p1.vx + v_in_mag * u_out[0],
                            p1.vy + v_in_mag * u_out[1],
                            p1.vz + v_in_mag * u_out[2]
                        };
                    };

                    bool is_res = (std::abs(p1.mu - p2.mu) < 1e-4 && std::abs(p1.radius - p2.radius) < 1e-4 && !is_central_body(p1, effective_mu));

                    auto eval_dsm_candidate = [&](const std::array<double, 3>& v_dep_cand,
                                                  std::array<double, 3>& out_r_dsm,
                                                  std::array<double, 3>& out_v_dsm_m,
                                                  std::array<double, 3>& out_v_dsm_p,
                                                  std::array<double, 3>& out_v2_arr,
                                                  bool fast_phase = false) -> double {
                        std::array<double, 3> r_d = r1_arr;
                        std::array<double, 3> v_d = v_dep_cand;
                        try {
                            pykep_propagate_lagrangian(r_d, v_d, dt1, effective_mu);
                        } catch (...) {
                            return 1e9;
                        }
                        double r_dsm_len = std::sqrt(r_d[0]*r_d[0] + r_d[1]*r_d[1] + r_d[2]*r_d[2]);
                        double r2_len_dsm = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                        double r_avg_dsm = (r_dsm_len + r2_len_dsm) * 0.5;
                        double t_orb_dsm = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_dsm * r_avg_dsm * r_avg_dsm) / effective_mu);
                        int nmax = (t_orb_dsm > 10.0) ? (int)std::floor(dt2 / t_orb_dsm + 0.25) : 0;
                        int max_nmax = is_res ? 8 : 4;
                        nmax = std::max(0, std::min(max_nmax, nmax));

                        // 两阶段多圈回转兰伯特剪枝: 候选角粗筛与坐标下降阶段仅评估 0-rev (共振最多 1-rev)
                        // 彻底消灭 17 个多圈分支的爆炸性数值求根 (提速 15~20 倍，杜绝 CPU 跑满假死)
                        int eval_nmax = fast_phase ? (is_res ? std::min(1, nmax) : 0) : nmax;

                        try {
                            kep3::lambert_problem lp_dsm(r_d, r2_arr, dt2, effective_mu, false, eval_nmax);
                            const auto& v0_dsm_list = lp_dsm.get_v0();
                            const auto& v1_dsm_list = lp_dsm.get_v1();
                            double best_cost = 1e9;
                            size_t best_idx = 0;
                            bool found = false;
                            for (size_t b = 0; b < v0_dsm_list.size(); ++b) {
                                double dv_x = v0_dsm_list[b][0] - v_d[0];
                                double dv_y = v0_dsm_list[b][1] - v_d[1];
                                double dv_z = v0_dsm_list[b][2] - v_d[2];
                                double dsm_mag = std::sqrt(dv_x*dv_x + dv_y*dv_y + dv_z*dv_z);
                                double v0_mag = std::sqrt(v0_dsm_list[b][0]*v0_dsm_list[b][0] + v0_dsm_list[b][1]*v0_dsm_list[b][1] + v0_dsm_list[b][2]*v0_dsm_list[b][2]);
                                if (!std::isnan(dsm_mag) && !std::isinf(dsm_mag) && v0_mag < 90.0) {
                                    double cand_cost = dsm_mag;

                                    if (k == leg_count - 1 && !no_insertion) {
                                        double vax = v1_dsm_list[b][0] - p2.vx;
                                        double vay = v1_dsm_list[b][1] - p2.vy;
                                        double vaz = v1_dsm_list[b][2] - p2.vz;
                                        double v_inf_arr = std::sqrt(vax*vax + vay*vay + vaz*vaz);
                                        bool is_central_arr = is_central_body(p2, effective_mu);
                                        double ins_dv = is_central_arr ? v_inf_arr : calc_vis_viva_arr(v_inf_arr, p2.mu, p2.radius, arr_pe_km, arr_ap_km);
                                        cand_cost += ins_dv;
                                    }
                                    if (cand_cost < best_cost) {
                                        best_cost = cand_cost;
                                        best_idx = b;
                                        found = true;
                                    }
                                }
                            }
                            if (found) {
                                out_r_dsm = r_d;
                                out_v_dsm_m = v_d;
                                out_v_dsm_p = v0_dsm_list[best_idx];
                                out_v2_arr = v1_dsm_list[best_idx];
                                return best_cost;
                            }
                        } catch (...) {}
                        return 1e9;
                    };

                    std::vector<std::pair<double, double>> angle_candidates;

                    // A. 显式输入参数优先 (Explicit External Guidance if provided)
                    double rp_in = (fb_rps_km != nullptr) ? fb_rps_km[k] : 0.0;
                    double beta_in = (fb_betas_rad != nullptr) ? fb_betas_rad[k] : 0.0;
                    bool has_strong_guidance = false;
                    if (rp_in >= safe_rp) {
                        double e_in = 1.0 + (rp_in * v_in_mag * v_in_mag) / std::max(1e-6, p1.mu);
                        double delta_in = 2.0 * std::asin(1.0 / std::max(1.0001, e_in));
                        double d_cand = std::min(delta_in, delta_max);
                        angle_candidates.push_back({ d_cand, beta_in });

                        // 探针评估：若外部基因/高精度种子有效且成本合理，直接采纳，短路跳过后续耗时盲搜网格
                        std::array<double, 3> v_cand = get_cone_v_dep(d_cand, beta_in);
                        std::array<double, 3> tmp_r, tmp_vm, tmp_vp, tmp_v2;
                        double g_cost = eval_dsm_candidate(v_cand, tmp_r, tmp_vm, tmp_vp, tmp_v2, true);
                        if (g_cost < 30.0) {
                            has_strong_guidance = true;
                        }
                    }

                    if (!has_strong_guidance) {
                        std::vector<double> a_candidates;
                        if (is_res) {
                            double t_p = 2.0 * 3.14159265358979323846 * std::sqrt((r1_len_f * r1_len_f * r1_len_f) / effective_mu);
                            int max_q = std::max(1, std::min(4, (int)std::floor(tof / (0.45 * t_p))));
                            for (int q = 1; q <= max_q; ++q) {
                                double t_sc = tof / q;
                                double a_q = std::cbrt((effective_mu * t_sc * t_sc) / (4.0 * 3.14159265358979323846 * 3.14159265358979323846));
                                a_candidates.push_back(a_q);
                            }
                        } else {
                            double a_hoh = (r1_len_f + r2_len_f) * 0.5;
                            a_candidates.push_back(a_hoh * 0.95);
                            a_candidates.push_back(a_hoh);
                            a_candidates.push_back(a_hoh * 1.05);
                        }

                        double gamma_angles[] = { -0.15, 0.0, 0.15 };
                        for (double a_val : a_candidates) {
                            double v_sq = effective_mu * (2.0 / std::max(1.0, r1_len_f) - 1.0 / std::max(1.0, a_val));
                            if (v_sq <= 1e-4) continue;
                            double v_req = std::sqrt(v_sq);
                            for (double gam : gamma_angles) {
                                double cos_g = std::cos(gam);
                                double sin_g = std::sin(gam);
                                double v_ideal[3] = {
                                    v_req * (cos_g * u_theta[0] + sin_g * u_r1[0]),
                                    v_req * (cos_g * u_theta[1] + sin_g * u_r1[1]),
                                    v_req * (cos_g * u_theta[2] + sin_g * u_r1[2])
                                };
                                double v_inf_req[3] = { v_ideal[0] - p1.vx, v_ideal[1] - p1.vy, v_ideal[2] - p1.vz };
                                double v_inf_len = std::sqrt(v_inf_req[0]*v_inf_req[0] + v_inf_req[1]*v_inf_req[1] + v_inf_req[2]*v_inf_req[2]);
                                if (v_inf_len > 1e-4) {
                                    double u_tgt[3] = { v_inf_req[0]/v_inf_len, v_inf_req[1]/v_inf_len, v_inf_req[2]/v_inf_len };
                                    double dot = std::max(-1.0, std::min(1.0, u_in[0]*u_tgt[0] + u_in[1]*u_tgt[1] + u_in[2]*u_tgt[2]));
                                    double req_ang = std::acos(dot);
                                    double turn_d = std::min(req_ang, delta_max);
                                    double dot_e1 = u_tgt[0]*e1[0] + u_tgt[1]*e1[1] + u_tgt[2]*e1[2];
                                    double dot_e2 = u_tgt[0]*e2[0] + u_tgt[1]*e2[1] + u_tgt[2]*e2[2];
                                    double b_ang = std::atan2(dot_e2, dot_e1);
                                    angle_candidates.push_back({ turn_d, b_ang });
                                }
                            }
                        }

                        // C. 尝试两体全航段直连兰伯特投影候选 (Direct Lambert Projection - 粗筛限制为 0-rev)
                        try {
                            kep3::lambert_problem lp_full(r1_arr, r2_arr, tof, effective_mu, false, 0);
                            const auto& v0_raw = lp_full.get_v0();
                            for (size_t b = 0; b < v0_raw.size(); ++b) {
                                double vx = v0_raw[b][0] - p1.vx;
                                double vy = v0_raw[b][1] - p1.vy;
                                double vz = v0_raw[b][2] - p1.vz;
                                double v_len = std::sqrt(vx*vx + vy*vy + vz*vz);
                                if (v_len > 1e-4 && v_len < 45.0) {
                                    double u_lam[3] = { vx / v_len, vy / v_len, vz / v_len };
                                    double dot = std::max(-1.0, std::min(1.0, u_in[0]*u_lam[0] + u_in[1]*u_lam[1] + u_in[2]*u_lam[2]));
                                    double req_ang = std::acos(dot);
                                    double turn_d = std::min(req_ang, delta_max);
                                    double dot_e1 = u_lam[0]*e1[0] + u_lam[1]*e1[1] + u_lam[2]*e1[2];
                                    double dot_e2 = u_lam[0]*e2[0] + u_lam[1]*e2[1] + u_lam[2]*e2[2];
                                    double b_ang = std::atan2(dot_e2, dot_e1);
                                    angle_candidates.push_back({ turn_d, b_ang });
                                }
                            }
                        } catch (...) {}

                        // D. 借力偏折锥面全向空间网格扫掠 (Compact Cone Azimuth Sweep)
                        double delta_fractions[] = { 1.0, 0.50 };
                        for (double d_frac : delta_fractions) {
                            double cur_del = delta_max * d_frac;
                            for (int a_idx = 0; a_idx < 6; ++a_idx) {
                                double cur_bet = -3.14159265358979323846 + a_idx * (2.0 * 3.14159265358979323846 / 6.0);
                                angle_candidates.push_back({ cur_del, cur_bet });
                            }
                        }
                    }

                    // E. 物理直接评测与全局最优出场选择 (Direct DSM Evaluation - Phase 1 快速粗筛)
                    double best_cost = 1e8;
                    double best_delta = delta_max;
                    double best_beta = 0.0;
                    std::array<double, 3> best_v_dep = { p1.vx + v_in_x, p1.vy + v_in_y, p1.vz + v_in_z };
                    std::array<double, 3> best_r_dsm = r1_arr;
                    std::array<double, 3> best_v_dsm_m = best_v_dep;
                    std::array<double, 3> best_v_dsm_p = {0, 0, 0};
                    std::array<double, 3> best_v2 = {0, 0, 0};

                    for (const auto& ang : angle_candidates) {
                        double d_cand = std::max(0.0, std::min(delta_max, ang.first));
                        double b_cand = ang.second;
                        std::array<double, 3> v_cand = get_cone_v_dep(d_cand, b_cand);
                        std::array<double, 3> tmp_r_dsm = {0, 0, 0}, tmp_v_m = {0, 0, 0}, tmp_v_p = {0, 0, 0}, tmp_v2 = {0, 0, 0};
                        double cost = eval_dsm_candidate(v_cand, tmp_r_dsm, tmp_v_m, tmp_v_p, tmp_v2, true);
                        if (cost < 1e8 && cost < best_cost) {
                            best_cost = cost;
                            best_delta = d_cand;
                            best_beta = b_cand;
                            best_v_dep = v_cand;
                            best_r_dsm = tmp_r_dsm;
                            best_v_dsm_m = tmp_v_m;
                            best_v_dsm_p = tmp_v_p;
                            best_v2 = tmp_v2;
                        }
                    }

                    // F. 极速 2D 坐标下降微调抛光 (Coordinate Descent Refinement - 仅对可行域内的解快速抛光 2 轮)
                    if (best_cost < 1e5) {
                        double step_d = delta_max * 0.10;
                        double step_b = 0.20;
                        for (int iter = 0; iter < 2; ++iter) {
                            double test_d_deltas[] = { 0.0, -step_d, step_d };
                            double test_b_deltas[] = { 0.0, -step_b, step_b };
                            bool improved = false;
                            for (double dd : test_d_deltas) {
                                for (double db : test_b_deltas) {
                                    if (std::abs(dd) < 1e-6 && std::abs(db) < 1e-6) continue;
                                    double try_d = std::max(0.0, std::min(delta_max, best_delta + dd));
                                    double try_b = best_beta + db;
                                    std::array<double, 3> v_cand = get_cone_v_dep(try_d, try_b);
                                    std::array<double, 3> tmp_r_dsm = {0, 0, 0}, tmp_v_m = {0, 0, 0}, tmp_v_p = {0, 0, 0}, tmp_v2 = {0, 0, 0};
                                    double cost = eval_dsm_candidate(v_cand, tmp_r_dsm, tmp_v_m, tmp_v_p, tmp_v2, true);
                                    if (cost < 1e8 && cost < best_cost) {
                                        best_cost = cost;
                                        best_delta = try_d;
                                        best_beta = try_b;
                                        best_v_dep = v_cand;
                                        best_r_dsm = tmp_r_dsm;
                                        best_v_dsm_m = tmp_v_m;
                                        best_v_dsm_p = tmp_v_p;
                                        best_v2 = tmp_v2;
                                        improved = true;
                                    }
                                }
                            }
                            step_d *= 0.5;
                            step_b *= 0.5;
                            if (!improved) break;
                        }
                    }

                    // G. 两阶段多圈回转兰伯特收官精选 (Phase 2 Multi-Rev Polish)
                    // 仅对最终锁定的全局最优偏折方向单次调用完整 nmax 求解，精确提取多圈分支
                    std::array<double, 3> tmp_fin_r = {0, 0, 0}, tmp_fin_vm = {0, 0, 0}, tmp_fin_vp = {0, 0, 0}, tmp_fin_v2 = {0, 0, 0};
                    double final_cost = eval_dsm_candidate(best_v_dep, tmp_fin_r, tmp_fin_vm, tmp_fin_vp, tmp_fin_v2, false);
                    if (final_cost < 1e8) {
                        best_r_dsm = tmp_fin_r;
                        best_v_dsm_m = tmp_fin_vm;
                        best_v_dsm_p = tmp_fin_vp;
                        best_v2 = tmp_fin_v2;
                    }

                    cur_v_dep = best_v_dep;
                    leg.v_inf_dep_x = cur_v_dep[0] - p1.vx;
                    leg.v_inf_dep_y = cur_v_dep[1] - p1.vy;
                    leg.v_inf_dep_z = cur_v_dep[2] - p1.vz;
                    leg.flyby_turn_deg = best_delta * RAD2DEG;
                    double sin_half = std::sin(best_delta * 0.5);
                    double e_req = (sin_half > 1e-4) ? (1.0 / sin_half) : 100.0;
                    leg.flyby_rp_km = std::max(safe_rp, (e_req - 1.0) * p1.mu / std::max(1e-6, v_in_mag * v_in_mag));
                    leg.flyby_beta_deg = best_beta * RAD2DEG;
                    leg.flyby_dv_kms = 0.0; // 严格纯无动力飞掠！

                    leg.v1x = cur_v_dep[0]; leg.v1y = cur_v_dep[1]; leg.v1z = cur_v_dep[2];
                    r_dsm = best_r_dsm;
                    v_dsm_m = best_v_dsm_m;
                    v_dsm_p = best_v_dsm_p;
                    v2_arr = best_v2;
                    best_dsm = best_cost;
                    lam_ok = (best_cost < 1e8);
                }

                // DSM 后半段顺行连接为物理唯一可行解，省去逆行 Lambert 重试耗时

                if (!lam_ok) {
                    leg.is_feasible = 0;
                    out_sol->all_feasible = 0;
                    leg.has_dsm = 1;
                    leg.dsm.epoch_offset_sec = dt1;
                    leg.dsm.pos_x = r_dsm[0]; leg.dsm.pos_y = r_dsm[1]; leg.dsm.pos_z = r_dsm[2];

                    double r_dsm_len = std::sqrt(r_dsm[0]*r_dsm[0] + r_dsm[1]*r_dsm[1] + r_dsm[2]*r_dsm[2]);
                    double r2_len = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                    double a_transfer = (r_dsm_len + r2_len) * 0.5;
                    double v_vis_viva = std::sqrt(std::max(1e-4, effective_mu * (2.0 / std::max(1.0, r_dsm_len) - 1.0 / std::max(1.0, a_transfer))));
                    double dsm_penalty_kms = std::max(15.0, v_vis_viva * 1.5);
                    leg.dsm.dv_mag_ms = dsm_penalty_kms * 1000.0;
                    leg.dsm.dv_x = dsm_penalty_kms; leg.dsm.dv_y = 0.0; leg.dsm.dv_z = 0.0;
                    leg.dsm.prograde_ms = leg.dsm.dv_mag_ms;
                    leg.dsm.normal_ms = 0.0;
                    leg.dsm.radial_ms = 0.0;

                    total_dsm_dv += dsm_penalty_kms;
                    total_dv += dsm_penalty_kms;

                    double v_p2_mag = std::sqrt(p2.vx*p2.vx + p2.vy*p2.vy + p2.vz*p2.vz);
                    double u_p2[3] = { 1, 0, 0 };
                    if (v_p2_mag > 1e-4) {
                        u_p2[0] = p2.vx / v_p2_mag; u_p2[1] = p2.vy / v_p2_mag; u_p2[2] = p2.vz / v_p2_mag;
                    }
                    v2_arr = { p2.vx + 5.0 * u_p2[0], p2.vy + 5.0 * u_p2[1], p2.vz + 5.0 * u_p2[2] };
                } else {
                    leg.is_feasible = 1;
                    leg.has_dsm = 1;
                    leg.dsm.epoch_offset_sec = dt1;
                    leg.dsm.pos_x = r_dsm[0]; leg.dsm.pos_y = r_dsm[1]; leg.dsm.pos_z = r_dsm[2];

                    double dsm_dv_x = v_dsm_p[0] - v_dsm_m[0];
                    double dsm_dv_y = v_dsm_p[1] - v_dsm_m[1];
                    double dsm_dv_z = v_dsm_p[2] - v_dsm_m[2];
                    double dsm_mag_kms = std::sqrt(dsm_dv_x*dsm_dv_x + dsm_dv_y*dsm_dv_y + dsm_dv_z*dsm_dv_z);

                    if (dsm_mag_kms > 50.0 || std::isnan(dsm_mag_kms) || std::isinf(dsm_mag_kms)) {
                        leg.is_feasible = 0;
                        out_sol->all_feasible = 0;
                        dsm_mag_kms = 50.0;
                        leg.dsm.dv_mag_ms = 50000.0;
                        leg.dsm.dv_x = 50.0; leg.dsm.dv_y = 0.0; leg.dsm.dv_z = 0.0;
                        leg.dsm.prograde_ms = 50000.0;
                        leg.dsm.normal_ms = 0.0;
                        leg.dsm.radial_ms = 0.0;
                        total_dsm_dv += 50.0;
                        total_dv += 50.0;
                    } else {
                        leg.is_feasible = 1;
                        leg.dsm.dv_x = dsm_dv_x; leg.dsm.dv_y = dsm_dv_y; leg.dsm.dv_z = dsm_dv_z;
                        leg.dsm.dv_mag_ms = dsm_mag_kms * 1000.0;

                        total_dsm_dv += dsm_mag_kms;
                        total_dv += dsm_mag_kms;

                        double v_m_len = std::sqrt(v_dsm_m[0]*v_dsm_m[0] + v_dsm_m[1]*v_dsm_m[1] + v_dsm_m[2]*v_dsm_m[2]);
                        if (v_m_len > 1e-6) {
                            double vhat[3] = {v_dsm_m[0]/v_m_len, v_dsm_m[1]/v_m_len, v_dsm_m[2]/v_m_len};
                            double hx = r_dsm[1]*v_dsm_m[2] - r_dsm[2]*v_dsm_m[1];
                            double hy = r_dsm[2]*v_dsm_m[0] - r_dsm[0]*v_dsm_m[2];
                            double hz = r_dsm[0]*v_dsm_m[1] - r_dsm[1]*v_dsm_m[0];
                            double h_len = std::sqrt(hx*hx + hy*hy + hz*hz);
                            double nhat[3] = {0, 0, 1};
                            if (h_len > 1e-6) { nhat[0]=hx/h_len; nhat[1]=hy/h_len; nhat[2]=hz/h_len; }
                            double chat[3] = {vhat[1]*nhat[2] - vhat[2]*nhat[1], vhat[2]*nhat[0] - vhat[0]*nhat[2], vhat[0]*nhat[1] - vhat[1]*nhat[0]};

                            leg.dsm.prograde_ms = (dsm_dv_x*vhat[0] + dsm_dv_y*vhat[1] + dsm_dv_z*vhat[2]) * 1000.0;
                            leg.dsm.normal_ms = (dsm_dv_x*nhat[0] + dsm_dv_y*nhat[1] + dsm_dv_z*nhat[2]) * 1000.0;
                            leg.dsm.radial_ms = (dsm_dv_x*chat[0] + dsm_dv_y*chat[1] + dsm_dv_z*chat[2]) * 1000.0;
                        }
                    }
                }

                leg.v2x = v2_arr[0]; leg.v2y = v2_arr[1]; leg.v2z = v2_arr[2];
                leg.v_inf_arr_x = v2_arr[0] - p2.vx;
                leg.v_inf_arr_y = v2_arr[1] - p2.vy;
                leg.v_inf_arr_z = v2_arr[2] - p2.vz;

                // 极速物理门禁提前截断 (Early Termination for Unreasonable / Impossible Candidates):
                // 一旦中间航段累计 DSM 超过 20 km/s 或总工质 ΔV 超过 30 km/s，该候选解已彻底脱离物理可行域。
                // 立即抛弃剩余航段的全部昂贵兰伯特与开普勒积分计算，赋予平滑线性惩罚并提前返回！
                if (k < leg_count - 1 && (total_dsm_dv > 20.0 || total_dv > 30.0)) {
                    int rem_legs = leg_count - 1 - k;
                    out_sol->all_feasible = 0;
                    for (int rem = k + 1; rem < leg_count; ++rem) {
                        out_sol->legs[rem].is_feasible = 0;
                        out_sol->legs[rem].tof_sec = tofs_sec[rem];
                        total_tof_sec += tofs_sec[rem];
                        out_sol->legs[rem].dsm.dv_mag_ms = 4000.0;
                    }
                    total_dsm_dv += rem_legs * 4.0;
                    total_dv += rem_legs * 4.0;
                    break;
                }


            }
        }

        if (!no_insertion && leg_count > 0) {
            const NativePlanetState& p_dest = arr_planets[leg_count - 1];
            double v_inf_arr_mag = std::sqrt(
                out_sol->legs[leg_count - 1].v_inf_arr_x * out_sol->legs[leg_count - 1].v_inf_arr_x +
                out_sol->legs[leg_count - 1].v_inf_arr_y * out_sol->legs[leg_count - 1].v_inf_arr_y +
                out_sol->legs[leg_count - 1].v_inf_arr_z * out_sol->legs[leg_count - 1].v_inf_arr_z);

            bool is_central_arr = is_central_body(p_dest, effective_mu);
            if (is_central_arr) {
                out_sol->insertion_dv_kms = v_inf_arr_mag;
            } else {
                out_sol->insertion_dv_kms = calc_vis_viva_arr(v_inf_arr_mag, p_dest.mu, p_dest.radius, arr_pe_km, arr_ap_km);
            }
            total_dv += out_sol->insertion_dv_kms;
        }

        out_sol->total_dsm_dv_kms = total_dsm_dv;
        out_sol->total_flyby_dv_kms = total_flyby_dv;
        out_sol->total_dv_kms = total_dv;
        out_sol->total_flight_days = total_tof_sec / 86400.0;
        return 0;
    } catch (...) {
        if (out_sol) out_sol->all_feasible = 0;
        return -99;
    }
}

EXPORT_API int pykep_mga_hybrid_evaluate(
    const NativePlanetState* dep_planets,
    const NativePlanetState* arr_planets,
    const double* tofs_sec,
    const int* leg_strategies,
    const int* flyby_strategies,
    const double* dsm_etas,
    const double* fb_rps_km,
    const double* fb_betas_rad,
    int leg_count,
    double dep_pe_km, double dep_ap_km, double dep_inc_deg,
    double arr_pe_km, double arr_ap_km, int no_insertion,
    NativeMGA1DSMSolution* out_sol)
{
    return pykep_mga_hybrid_evaluate_mu(
        dep_planets, arr_planets, tofs_sec,
        leg_strategies, flyby_strategies,
        dsm_etas, fb_rps_km, fb_betas_rad,
        leg_count,
        dep_pe_km, dep_ap_km, dep_inc_deg,
        arr_pe_km, arr_ap_km, no_insertion,
        SUN_MU,
        out_sol);
}

EXPORT_API int pykep_mga_1dsm_evaluate_mu(
    const NativePlanetState* dep_planets,
    const NativePlanetState* arr_planets,
    const double* tofs_sec,
    const int* leg_strategies,
    const int* flyby_strategies,
    const double* dsm_etas,
    const double* fb_rps_km,
    const double* fb_betas_rad,
    int leg_count,
    double dep_pe_km, double dep_ap_km, double dep_inc_deg,
    double arr_pe_km, double arr_ap_km, int no_insertion,
    double mu_central,
    NativeMGA1DSMSolution* out_sol)
{
    return pykep_mga_hybrid_evaluate_mu(
        dep_planets, arr_planets, tofs_sec,
        leg_strategies, flyby_strategies,
        dsm_etas, fb_rps_km, fb_betas_rad,
        leg_count,
        dep_pe_km, dep_ap_km, dep_inc_deg,
        arr_pe_km, arr_ap_km, no_insertion,
        mu_central,
        out_sol);
}

EXPORT_API int pykep_mga_1dsm_evaluate(
    const NativePlanetState* dep_planets,
    const NativePlanetState* arr_planets,
    const double* tofs_sec,
    const double* dsm_etas,
    const double* fb_rps_km,
    const double* fb_betas_rad,
    int leg_count,
    double dep_pe_km, double dep_ap_km, double dep_inc_deg,
    double arr_pe_km, double arr_ap_km, int no_insertion,
    NativeMGA1DSMSolution* out_sol)
{
    return pykep_mga_hybrid_evaluate_mu(
        dep_planets, arr_planets, tofs_sec,
        nullptr, nullptr,
        dsm_etas, fb_rps_km, fb_betas_rad,
        leg_count,
        dep_pe_km, dep_ap_km, dep_inc_deg,
        arr_pe_km, arr_ap_km, no_insertion,
        SUN_MU,
        out_sol);
}

// ============================================================================
// Native OpenMP + AVX2 Multi-Trajectory Batch Evaluation Engine
// ============================================================================
EXPORT_API int pykep_mga_hybrid_evaluate_batch(
    const NativePlanetState* all_dep_planets,
    const NativePlanetState* all_arr_planets,
    const double* all_tofs_sec,
    const int* leg_strategies,
    const int* flyby_strategies,
    const double* all_dsm_etas,
    const double* fb_rps_km,
    const double* fb_betas_rad,
    int num_trajectories,
    int leg_count,
    double dep_pe_km, double dep_ap_km, double dep_inc_deg,
    double arr_pe_km, double arr_ap_km, int no_insertion,
    double mu_central,
    NativeMGA1DSMSolution* out_sols)
{
    if (!all_dep_planets || !all_arr_planets || !all_tofs_sec || !out_sols ||
        num_trajectories <= 0 || leg_count <= 0 || leg_count > 16) {
        return -1;
    }

    int any_failure = 0;

    // 禁用 OpenMP 嵌套并行，彻底防止 C# 线程池与 C++ 嵌套展开引发线程雪崩
#ifdef _OPENMP
    omp_set_nested(0);
    // 弹性核心预算: 外层已有 C# 拓扑或窗口并发时，限制单次批量最大线程为核心数的一半，且根据任务量伸缩
    int max_threads = std::max(1, omp_get_num_procs() / 2);
    if ((num_trajectories + 3) / 4 < max_threads) {
        max_threads = std::max(1, (num_trajectories + 3) / 4);
    }
    #pragma omp parallel for num_threads(max_threads) schedule(dynamic, 2)
#endif
    for (int i = 0; i < num_trajectories; ++i) {
        const NativePlanetState* dep = all_dep_planets + (size_t)i * leg_count;
        const NativePlanetState* arr = all_arr_planets + (size_t)i * leg_count;
        const double* tofs = all_tofs_sec + (size_t)i * leg_count;
        const double* etas = (all_dsm_etas != nullptr) ? (all_dsm_etas + (size_t)i * leg_count) : nullptr;
        NativeMGA1DSMSolution* sol = out_sols + i;

        int status = pykep_mga_hybrid_evaluate_mu(
            dep, arr, tofs,
            leg_strategies, flyby_strategies,
            etas, fb_rps_km, fb_betas_rad,
            leg_count,
            dep_pe_km, dep_ap_km, dep_inc_deg,
            arr_pe_km, arr_ap_km, no_insertion,
            mu_central,
            sol);

        if (status != 0) {
            any_failure = 1;
        }
    }

    return any_failure;
}

