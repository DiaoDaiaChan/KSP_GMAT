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
                double best_cost = 1e18;
                double r1_len_b = std::sqrt(r1_arr[0]*r1_arr[0] + r1_arr[1]*r1_arr[1] + r1_arr[2]*r1_arr[2]);
                double r2_len_b = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                double r_avg_b = (r1_len_b + r2_len_b) * 0.5;
                double t_orb_b = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_b * r_avg_b * r_avg_b) / effective_mu);
                int nmax = (t_orb_b > 10.0) ? (int)std::floor(tof / t_orb_b + 0.25) : 0;
                bool is_res_b = (std::abs(p1.mu - p2.mu) < 1e-4 && std::abs(p1.radius - p2.radius) < 1e-4 && !is_central_body(p1, effective_mu));
                if (is_res_b && nmax < 1) nmax = 1;
                nmax = std::max(0, std::min(4, nmax));
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

                        double hx = p1.ry * cur_v_dep[2] - p1.rz * cur_v_dep[1];
                        double hy = p1.rz * cur_v_dep[0] - p1.rx * cur_v_dep[2];
                        double hz = p1.rx * cur_v_dep[1] - p1.ry * cur_v_dep[0];
                        double h_mag = std::sqrt(hx * hx + hy * hy + hz * hz);
                        if (h_mag < 1e-6) {
                            hx = p1.ry * p2.rz - p1.rz * p2.ry;
                            hy = p1.rz * p2.rx - p1.rx * p2.rz;
                            hz = p1.rx * p2.ry - p1.ry * p2.rx;
                            h_mag = std::sqrt(hx * hx + hy * hy + hz * hz);
                        }
                        if (h_mag > 1e-6) {
                            out_sol->ejection_inc_deg = std::acos(std::max(-1.0, std::min(1.0, hz / h_mag))) * RAD2DEG;
                            double nx = -hy;
                            double ny = hx;
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
                    leg.is_feasible = (fb_ok == 0) ? 1 : 0;
                    if (fb_ok != 0) out_sol->all_feasible = 0;

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

                if (k == 0) {
                    double r1_len_i = std::sqrt(r1_arr[0]*r1_arr[0] + r1_arr[1]*r1_arr[1] + r1_arr[2]*r1_arr[2]);
                    double r2_len_i = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                    double r_avg_i = (r1_len_i + r2_len_i) * 0.5;
                    double t_orb_i = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_i * r_avg_i * r_avg_i) / effective_mu);
                    int nmax_init = (t_orb_i > 10.0) ? (int)std::floor(tof / t_orb_i + 0.25) : 0;
                    bool is_res_init = (std::abs(p1.mu - p2.mu) < 1e-4 && std::abs(p1.radius - p2.radius) < 1e-4 && !is_central_body(p1, effective_mu));
                    if (is_res_init && nmax_init < 1) nmax_init = 1;
                    nmax_init = std::max(0, std::min(4, nmax_init));
                    bool init_ok = false;
                    double best_vinf = 1e18;
                    try {
                        kep3::lambert_problem lp_init(r1_arr, r2_arr, tof, effective_mu, false, nmax_init);
                        const auto& v0_init_list = lp_init.get_v0();
                        for (size_t b = 0; b < v0_init_list.size(); ++b) {
                            double vx = v0_init_list[b][0] - p1.vx;
                            double vy = v0_init_list[b][1] - p1.vy;
                            double vz = v0_init_list[b][2] - p1.vz;
                            double vinf = std::sqrt(vx*vx + vy*vy + vz*vz);
                            double v_tot = std::sqrt(v0_init_list[b][0]*v0_init_list[b][0] + v0_init_list[b][1]*v0_init_list[b][1] + v0_init_list[b][2]*v0_init_list[b][2]);
                            if (!std::isnan(v_tot) && !std::isinf(v_tot) && vinf < 45.0 && vinf < best_vinf) {
                                best_vinf = vinf;
                                cur_v_dep = v0_init_list[b];
                                init_ok = true;
                            }
                        }
                    } catch (...) {}

                    if (is_res_init) {
                        double v_p1_mag = std::sqrt(p1.vx*p1.vx + p1.vy*p1.vy + p1.vz*p1.vz);
                        if (v_p1_mag > 1e-4 && r1_len_i > 1e-4) {
                            double u_p1[3] = { p1.vx / v_p1_mag, p1.vy / v_p1_mag, p1.vz / v_p1_mag };
                            int max_m = std::max(1, std::min(4, nmax_init + 1));
                            for (int m = 1; m <= max_m; ++m) {
                                double t_sc = tof / (double)m;
                                double a_res = std::cbrt(effective_mu * (t_sc / (2.0 * PI)) * (t_sc / (2.0 * PI)));
                                double v_sq = effective_mu * (2.0 / r1_len_i - 1.0 / a_res);
                                if (v_sq <= 1e-4) continue;
                                double v_req = std::sqrt(v_sq);
                                std::array<double, 3> v_cand = { v_req * u_p1[0], v_req * u_p1[1], v_req * u_p1[2] };
                                double vx = v_cand[0] - p1.vx;
                                double vy = v_cand[1] - p1.vy;
                                double vz = v_cand[2] - p1.vz;
                                double vinf = std::sqrt(vx*vx + vy*vy + vz*vz);
                                if (vinf < best_vinf) {
                                    best_vinf = vinf;
                                    cur_v_dep = v_cand;
                                    init_ok = true;
                                }
                            }
                        }
                    }

                    if (!init_ok) {
                        try {
                            kep3::lambert_problem lp_init_retro(r1_arr, r2_arr, tof, effective_mu, true, nmax_init);
                            const auto& v0_init_list = lp_init_retro.get_v0();
                            for (size_t b = 0; b < v0_init_list.size(); ++b) {
                                double vx = v0_init_list[b][0] - p1.vx;
                                double vy = v0_init_list[b][1] - p1.vy;
                                double vz = v0_init_list[b][2] - p1.vz;
                                double vinf = std::sqrt(vx*vx + vy*vy + vz*vz);
                                double v_tot = std::sqrt(v0_init_list[b][0]*v0_init_list[b][0] + v0_init_list[b][1]*v0_init_list[b][1] + v0_init_list[b][2]*v0_init_list[b][2]);
                                if (!std::isnan(v_tot) && !std::isinf(v_tot) && vinf < 45.0 && vinf < best_vinf) {
                                    best_vinf = vinf;
                                    cur_v_dep = v0_init_list[b];
                                    init_ok = true;
                                }
                            }
                        } catch (...) {}
                    }

                    if (!init_ok) {
                        double v_p1_mag = std::sqrt(p1.vx*p1.vx + p1.vy*p1.vy + p1.vz*p1.vz);
                        double u_p1[3] = { 1, 0, 0 };
                        if (v_p1_mag > 1e-4) {
                            u_p1[0] = p1.vx / v_p1_mag; u_p1[1] = p1.vy / v_p1_mag; u_p1[2] = p1.vz / v_p1_mag;
                        }
                        double sign_dir = (r2_len_i >= r1_len_i) ? 1.0 : -1.0;
                        cur_v_dep = { p1.vx + sign_dir * 5.0 * u_p1[0], p1.vy + sign_dir * 5.0 * u_p1[1], p1.vz + sign_dir * 5.0 * u_p1[2] };
                        leg.is_feasible = 0;
                        out_sol->all_feasible = 0;
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

                        double hx = p1.ry * cur_v_dep[2] - p1.rz * cur_v_dep[1];
                        double hy = p1.rz * cur_v_dep[0] - p1.rx * cur_v_dep[2];
                        double hz = p1.rx * cur_v_dep[1] - p1.ry * cur_v_dep[0];
                        double h_mag = std::sqrt(hx * hx + hy * hy + hz * hz);
                        if (h_mag < 1e-6) {
                            hx = p1.ry * p2.rz - p1.rz * p2.ry;
                            hy = p1.rz * p2.rx - p1.rx * p2.rz;
                            hz = p1.rx * p2.ry - p1.ry * p2.rx;
                            h_mag = std::sqrt(hx * hx + hy * hy + hz * hz);
                        }
                        if (h_mag > 1e-6) {
                            out_sol->ejection_inc_deg = std::acos(std::max(-1.0, std::min(1.0, hz / h_mag))) * RAD2DEG;
                            double nx = -hy;
                            double ny = hx;
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
                } else {
                    NativeLeg1DSMResult& prev_leg = out_sol->legs[k - 1];
                    double v_in_x = prev_leg.v_inf_arr_x;
                    double v_in_y = prev_leg.v_inf_arr_y;
                    double v_in_z = prev_leg.v_inf_arr_z;
                    double v_in_mag = std::sqrt(v_in_x*v_in_x + v_in_y*v_in_y + v_in_z*v_in_z);

                    // 1. Solve full-leg Lambert problem to find nominal target transfer direction
                    std::array<double, 3> r1_arr = {p1.rx, p1.ry, p1.rz};
                    std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
                    double r1_len_f = std::sqrt(r1_arr[0]*r1_arr[0] + r1_arr[1]*r1_arr[1] + r1_arr[2]*r1_arr[2]);
                    double r2_len_f = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                    double r_avg_f = (r1_len_f + r2_len_f) * 0.5;
                    double t_orb_f = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_f * r_avg_f * r_avg_f) / effective_mu);
                    int nmax_full = (t_orb_f > 10.0) ? (int)std::floor(tof / t_orb_f + 0.25) : 0;
                    bool is_res_full = (std::abs(p1.mu - p2.mu) < 1e-4 && std::abs(p1.radius - p2.radius) < 1e-4 && !is_central_body(p1, effective_mu));
                    if (is_res_full && nmax_full < 1) nmax_full = 1;
                    nmax_full = std::max(0, std::min(4, nmax_full));
                    
                    std::array<double, 3> v_tgt_dep = {p1.vx, p1.vy, p1.vz};
                    double best_tgt_cost = 1e18;
                    bool lp_ok = false;
                    std::vector<std::array<double, 3>> candidate_v0_list;

                    // A. Attempt kep3 Lambert solver
                    try {
                        kep3::lambert_problem lp_full(r1_arr, r2_arr, tof, effective_mu, false, nmax_full);
                        const auto& v0_raw = lp_full.get_v0();
                        for (size_t b = 0; b < v0_raw.size(); ++b) {
                            double vx = v0_raw[b][0] - p1.vx;
                            double vy = v0_raw[b][1] - p1.vy;
                            double vz = v0_raw[b][2] - p1.vz;
                            double v_rel_mag = std::sqrt(vx*vx + vy*vy + vz*vz);
                            double v_tot_mag = std::sqrt(v0_raw[b][0]*v0_raw[b][0] + v0_raw[b][1]*v0_raw[b][1] + v0_raw[b][2]*v0_raw[b][2]);
                            // Filter out unphysical Lambert collinear singularities
                            if (!std::isnan(v_tot_mag) && !std::isinf(v_tot_mag) && v_rel_mag < 45.0 && v_tot_mag < 85.0) {
                                candidate_v0_list.push_back(v0_raw[b]);
                            }
                        }
                    } catch (...) {}

                    if (candidate_v0_list.empty()) {
                        try {
                            kep3::lambert_problem lp_full_retro(r1_arr, r2_arr, tof, effective_mu, true, nmax_full);
                            const auto& v0_raw = lp_full_retro.get_v0();
                            for (size_t b = 0; b < v0_raw.size(); ++b) {
                                double vx = v0_raw[b][0] - p1.vx;
                                double vy = v0_raw[b][1] - p1.vy;
                                double vz = v0_raw[b][2] - p1.vz;
                                double v_rel_mag = std::sqrt(vx*vx + vy*vy + vz*vz);
                                double v_tot_mag = std::sqrt(v0_raw[b][0]*v0_raw[b][0] + v0_raw[b][1]*v0_raw[b][1] + v0_raw[b][2]*v0_raw[b][2]);
                                if (!std::isnan(v_tot_mag) && !std::isinf(v_tot_mag) && v_rel_mag < 45.0 && v_tot_mag < 85.0) {
                                    candidate_v0_list.push_back(v0_raw[b]);
                                }
                            }
                        } catch (...) {}
                    }

                    // B. For resonant legs (same body flyby-to-flyby, e.g. V-V, M-M, E-E):
                    // Direct Keplerian Vis-Viva resonant synthesis to eradicate collinear singularity (2k*pi).
                    bool has_exit_target = false;
                    double u_exit[3] = { 0, 0, 0 };

                    if (is_res_full) {
                        // 识别同星共振集群的最终下游出口航段及出口天体，求解标称出口兰伯特矢量
                        int exit_k = k;
                        while (exit_k < leg_count &&
                               std::abs(dep_planets[exit_k].mu - p1.mu) < 1e-4 &&
                               std::abs(dep_planets[exit_k].radius - p1.radius) < 1e-4 &&
                               std::abs(arr_planets[exit_k].mu - p1.mu) < 1e-4 &&
                               std::abs(arr_planets[exit_k].radius - p1.radius) < 1e-4) {
                            exit_k++;
                        }
                        if (exit_k < leg_count) {
                            const NativePlanetState& dep_ex = dep_planets[exit_k];
                            const NativePlanetState& arr_ex = arr_planets[exit_k];
                            double tof_ex = tofs_sec[exit_k];
                            std::array<double, 3> r1_ex = { dep_ex.rx, dep_ex.ry, dep_ex.rz };
                            std::array<double, 3> r2_ex = { arr_ex.rx, arr_ex.ry, arr_ex.rz };
                            double r1_ex_len = std::sqrt(r1_ex[0]*r1_ex[0] + r1_ex[1]*r1_ex[1] + r1_ex[2]*r1_ex[2]);
                            double r2_ex_len = std::sqrt(r2_ex[0]*r2_ex[0] + r2_ex[1]*r2_ex[1] + r2_ex[2]*r2_ex[2]);
                            double r_avg_ex = (r1_ex_len + r2_ex_len) * 0.5;
                            double t_orb_ex = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_ex * r_avg_ex * r_avg_ex) / effective_mu);
                            int nmax_ex = (t_orb_ex > 10.0) ? (int)std::floor(tof_ex / t_orb_ex + 0.25) : 0;
                            nmax_ex = std::max(0, std::min(2, nmax_ex));
                            try {
                                kep3::lambert_problem lp_ex(r1_ex, r2_ex, tof_ex, effective_mu, false, nmax_ex);
                                const auto& v0_ex_list = lp_ex.get_v0();
                                if (!v0_ex_list.empty()) {
                                    double vx = v0_ex_list[0][0] - dep_ex.vx;
                                    double vy = v0_ex_list[0][1] - dep_ex.vy;
                                    double vz = v0_ex_list[0][2] - dep_ex.vz;
                                    double v_len = std::sqrt(vx*vx + vy*vy + vz*vz);
                                    if (v_len > 1e-4) {
                                        u_exit[0] = vx / v_len;
                                        u_exit[1] = vy / v_len;
                                        u_exit[2] = vz / v_len;
                                        has_exit_target = true;
                                    }
                                }
                            } catch (...) {}
                        }

                        double v_p1_mag = std::sqrt(p1.vx*p1.vx + p1.vy*p1.vy + p1.vz*p1.vz);
                        if (v_p1_mag > 1e-4 && r1_len_f > 1e-4) {
                            double u_p1[3] = { p1.vx / v_p1_mag, p1.vy / v_p1_mag, p1.vz / v_p1_mag };
                            double u_r1[3] = { r1_arr[0] / r1_len_f, r1_arr[1] / r1_len_f, r1_arr[2] / r1_len_f };
                            // Angular momentum unit vector of planet
                            double hx = r1_arr[1]*p1.vz - r1_arr[2]*p1.vy;
                            double hy = r1_arr[2]*p1.vx - r1_arr[0]*p1.vz;
                            double hz = r1_arr[0]*p1.vy - r1_arr[1]*p1.vx;
                            double h_len = std::sqrt(hx*hx + hy*hy + hz*hz);
                            double u_h[3] = { 0, 0, 1 };
                            if (h_len > 1e-6) {
                                u_h[0] = hx / h_len; u_h[1] = hy / h_len; u_h[2] = hz / h_len;
                            }
                            // In-plane transverse unit vector (u_theta = u_h x u_r)
                            double u_theta[3] = {
                                u_h[1]*u_r1[2] - u_h[2]*u_r1[1],
                                u_h[2]*u_r1[0] - u_h[0]*u_r1[2],
                                u_h[0]*u_r1[1] - u_h[1]*u_r1[0]
                            };

                            // Incoming direction unit vector
                            double u_in[3] = { 1, 0, 0 };
                            if (v_in_mag > 1e-5) {
                                u_in[0] = v_in_x / v_in_mag;
                                u_in[1] = v_in_y / v_in_mag;
                                u_in[2] = v_in_z / v_in_mag;
                            }

                            int max_m = std::max(1, std::min(4, nmax_full + 1));
                            for (int m = 1; m <= max_m; ++m) {
                                double t_sc = tof / (double)m;
                                double a_res = std::cbrt(effective_mu * (t_sc / (2.0 * PI)) * (t_sc / (2.0 * PI)));
                                double v_sq = effective_mu * (2.0 / r1_len_f - 1.0 / a_res);
                                if (v_sq <= 1e-4) continue;
                                double v_req = std::sqrt(v_sq);

                                double cos_alpha = 1.0;
                                if (v_in_mag > 1e-5) {
                                    cos_alpha = (v_req * v_req - v_p1_mag * v_p1_mag - v_in_mag * v_in_mag) / (2.0 * v_p1_mag * v_in_mag);
                                    cos_alpha = std::max(-1.0, std::min(1.0, cos_alpha));
                                }
                                double sin_alpha = std::sqrt(std::max(0.0, 1.0 - cos_alpha * cos_alpha));

                                // Candidate 1: Minimum-turn 3D cone projection towards incoming direction
                                double c_in = u_in[0]*u_p1[0] + u_in[1]*u_p1[1] + u_in[2]*u_p1[2];
                                double w_in[3] = { u_in[0] - c_in*u_p1[0], u_in[1] - c_in*u_p1[1], u_in[2] - c_in*u_p1[2] };
                                double w_len = std::sqrt(w_in[0]*w_in[0] + w_in[1]*w_in[1] + w_in[2]*w_in[2]);
                                double u_w[3] = { u_r1[0], u_r1[1], u_r1[2] };
                                if (w_len > 1e-6) {
                                    u_w[0] = w_in[0] / w_len; u_w[1] = w_in[1] / w_len; u_w[2] = w_in[2] / w_len;
                                }

                                double u_cand1[3] = {
                                    cos_alpha * u_p1[0] + sin_alpha * u_w[0],
                                    cos_alpha * u_p1[1] + sin_alpha * u_w[1],
                                    cos_alpha * u_p1[2] + sin_alpha * u_w[2]
                                };
                                candidate_v0_list.push_back({
                                    p1.vx + v_in_mag * u_cand1[0],
                                    p1.vy + v_in_mag * u_cand1[1],
                                    p1.vz + v_in_mag * u_cand1[2]
                                });

                                // Candidate Steer: Lookahead projection steered towards downstream exit target
                                if (has_exit_target && v_in_mag > 1e-5) {
                                    double e2[3] = {
                                        u_p1[1]*u_w[2] - u_p1[2]*u_w[1],
                                        u_p1[2]*u_w[0] - u_p1[0]*u_w[2],
                                        u_p1[0]*u_w[1] - u_p1[1]*u_w[0]
                                    };
                                    double c_ex = u_exit[0]*u_p1[0] + u_exit[1]*u_p1[1] + u_exit[2]*u_p1[2];
                                    double w_ex[3] = { u_exit[0] - c_ex*u_p1[0], u_exit[1] - c_ex*u_p1[1], u_exit[2] - c_ex*u_p1[2] };
                                    double w_ex_len = std::sqrt(w_ex[0]*w_ex[0] + w_ex[1]*w_ex[1] + w_ex[2]*w_ex[2]);
                                    if (w_ex_len > 1e-6) {
                                        double u_w_ex[3] = { w_ex[0] / w_ex_len, w_ex[1] / w_ex_len, w_ex[2] / w_ex_len };
                                        double cos_psi_ex = u_w_ex[0]*u_w[0] + u_w_ex[1]*u_w[1] + u_w_ex[2]*u_w[2];
                                        double sin_psi_ex = u_w_ex[0]*e2[0] + u_w_ex[1]*e2[1] + u_w_ex[2]*e2[2];
                                        double psi_ex = std::atan2(sin_psi_ex, cos_psi_ex);

                                        double e_min = 1.0 + (p1.safe_radius * v_in_mag * v_in_mag) / std::max(1e-6, p1.mu);
                                        double delta_max = 2.0 * std::asin(1.0 / std::max(1.0001, e_min));
                                        double safe_delta_max = delta_max * 0.995;
                                        double sin_in = std::sqrt(std::max(0.0, 1.0 - c_in * c_in));
                                        double psi_max = 0.0;
                                        if (sin_in * sin_alpha > 1e-5) {
                                            double r_denom = sin_in * sin_alpha;
                                            double r_nom = std::cos(safe_delta_max) - c_in * cos_alpha;
                                            double ratio = r_nom / r_denom;
                                            if (ratio <= -1.0) {
                                                psi_max = 3.14159265358979323846;
                                            } else if (ratio < 1.0) {
                                                psi_max = std::acos(ratio);
                                            } else {
                                                psi_max = 0.0;
                                            }
                                        }

                                        double steer_fracs[] = { 1.0, 0.75, 0.5 };
                                        for (double s_frac : steer_fracs) {
                                            double psi_steer = std::max(-psi_max, std::min(psi_max, psi_ex)) * s_frac;
                                            double cos_psi = std::cos(psi_steer);
                                            double sin_psi = std::sin(psi_steer);
                                            double u_w_s[3] = {
                                                cos_psi * u_w[0] + sin_psi * e2[0],
                                                cos_psi * u_w[1] + sin_psi * e2[1],
                                                cos_psi * u_w[2] + sin_psi * e2[2]
                                            };
                                            double u_c_s[3] = {
                                                cos_alpha * u_p1[0] + sin_alpha * u_w_s[0],
                                                cos_alpha * u_p1[1] + sin_alpha * u_w_s[1],
                                                cos_alpha * u_p1[2] + sin_alpha * u_w_s[2]
                                            };
                                            double u_c_s_len = std::sqrt(u_c_s[0]*u_c_s[0] + u_c_s[1]*u_c_s[1] + u_c_s[2]*u_c_s[2]);
                                            if (u_c_s_len > 1e-6) {
                                                u_c_s[0] /= u_c_s_len;
                                                u_c_s[1] /= u_c_s_len;
                                                u_c_s[2] /= u_c_s_len;
                                            }
                                            candidate_v0_list.push_back({
                                                p1.vx + v_in_mag * u_c_s[0],
                                                p1.vy + v_in_mag * u_c_s[1],
                                                p1.vz + v_in_mag * u_c_s[2]
                                            });
                                        }
                                    }
                                }

                                // Candidate 2: In-plane outward radial component
                                double u_cand2[3] = {
                                    cos_alpha * u_theta[0] + sin_alpha * u_r1[0],
                                    cos_alpha * u_theta[1] + sin_alpha * u_r1[1],
                                    cos_alpha * u_theta[2] + sin_alpha * u_r1[2]
                                };
                                candidate_v0_list.push_back({
                                    p1.vx + v_in_mag * u_cand2[0],
                                    p1.vy + v_in_mag * u_cand2[1],
                                    p1.vz + v_in_mag * u_cand2[2]
                                });

                                // Candidate 3: In-plane inward radial component
                                double u_cand3[3] = {
                                    cos_alpha * u_theta[0] - sin_alpha * u_r1[0],
                                    cos_alpha * u_theta[1] - sin_alpha * u_r1[1],
                                    cos_alpha * u_theta[2] - sin_alpha * u_r1[2]
                                };
                                candidate_v0_list.push_back({
                                    p1.vx + v_in_mag * u_cand3[0],
                                    p1.vy + v_in_mag * u_cand3[1],
                                    p1.vz + v_in_mag * u_cand3[2]
                                });
                            }
                        }
                    }

                    if (candidate_v0_list.empty()) {
                        // 物理兜底方向候选 (Physical Fallback Candidates)
                        double v_p1_mag = std::sqrt(p1.vx*p1.vx + p1.vy*p1.vy + p1.vz*p1.vz);
                        if (v_p1_mag > 1e-4) {
                            double u_p1[3] = { p1.vx / v_p1_mag, p1.vy / v_p1_mag, p1.vz / v_p1_mag };
                            double s_dir = (r2_len_f >= r1_len_f) ? 1.0 : -1.0;
                            candidate_v0_list.push_back({
                                p1.vx + s_dir * v_in_mag * u_p1[0],
                                p1.vy + s_dir * v_in_mag * u_p1[1],
                                p1.vz + s_dir * v_in_mag * u_p1[2]
                            });
                        }
                        double d12_x = p2.rx - p1.rx, d12_y = p2.ry - p1.ry, d12_z = p2.rz - p1.rz;
                        double d12 = std::sqrt(d12_x*d12_x + d12_y*d12_y + d12_z*d12_z);
                        if (d12 > 1e-4) {
                            candidate_v0_list.push_back({
                                p1.vx + v_in_mag * (d12_x / d12),
                                p1.vy + v_in_mag * (d12_y / d12),
                                p1.vz + v_in_mag * (d12_z / d12)
                            });
                        }
                        candidate_v0_list.push_back({
                            p1.vx + v_in_x,
                            p1.vy + v_in_y,
                            p1.vz + v_in_z
                        });
                    }

                    // Evaluate all candidates to find optimal target transfer direction
                    for (size_t b = 0; b < candidate_v0_list.size(); ++b) {
                        const auto& cand = candidate_v0_list[b];
                        double vx = cand[0] - p1.vx;
                        double vy = cand[1] - p1.vy;
                        double vz = cand[2] - p1.vz;
                        double v_b_mag = std::sqrt(vx*vx + vy*vy + vz*vz);
                        double dv_diff = std::abs(v_b_mag - v_in_mag);

                        double dot = (v_in_mag > 1e-4 && v_b_mag > 1e-4)
                            ? (v_in_x*vx + v_in_y*vy + v_in_z*vz) / (v_in_mag * v_b_mag)
                            : 1.0;
                        dot = std::max(-1.0, std::min(1.0, dot));
                        double req_theta = std::acos(dot);

                        double e_min = 1.0 + (p1.safe_radius * v_in_mag * v_in_mag) / std::max(1e-6, p1.mu);
                        double delta_max = 2.0 * std::asin(1.0 / std::max(1.0001, e_min));

                        double turn_excess = std::max(0.0, req_theta - delta_max);
                        double turn_penalty = 2.0 * v_in_mag * std::sin(turn_excess * 0.5);

                        double branch_cost = dv_diff + turn_penalty;
                        if (has_exit_target && is_res_full) {
                            double dot_ex = (v_b_mag > 1e-4) ? (vx*u_exit[0] + vy*u_exit[1] + vz*u_exit[2]) / v_b_mag : 0.0;
                            dot_ex = std::max(-1.0, std::min(1.0, dot_ex));
                            branch_cost += 0.50 * v_in_mag * (1.0 - dot_ex);
                        }
                        if (branch_cost < best_tgt_cost) {
                            best_tgt_cost = branch_cost;
                            v_tgt_dep = cand;
                            lp_ok = true;
                        }
                    }

                    if (!lp_ok) {
                        v_tgt_dep = { p1.vx + v_in_x, p1.vy + v_in_y, p1.vz + v_in_z };
                        leg.is_feasible = 0;
                        out_sol->all_feasible = 0;
                    }

                    double v_inf_tgt_x = v_tgt_dep[0] - p1.vx;
                    double v_inf_tgt_y = v_tgt_dep[1] - p1.vy;
                    double v_inf_tgt_z = v_tgt_dep[2] - p1.vz;
                    double v_tgt_mag = std::sqrt(v_inf_tgt_x*v_inf_tgt_x + v_inf_tgt_y*v_inf_tgt_y + v_inf_tgt_z*v_inf_tgt_z);

                    // 2. Perform optimal hyperbolic gravity assist deflection towards target direction
                    double actual_rp = p1.safe_radius * 1.15;
                    double actual_turn = 0.0;
                    std::array<double, 3> out_v_dep = v_tgt_dep;

                    if (v_in_mag > 1e-5 && v_tgt_mag > 1e-5) {
                        double u_in[3] = { v_in_x / v_in_mag, v_in_y / v_in_mag, v_in_z / v_in_mag };
                        double u_tgt[3] = { v_inf_tgt_x / v_tgt_mag, v_inf_tgt_y / v_tgt_mag, v_inf_tgt_z / v_tgt_mag };
                        double dot = std::max(-1.0, std::min(1.0, u_in[0]*u_tgt[0] + u_in[1]*u_tgt[1] + u_in[2]*u_tgt[2]));
                        double req_theta = std::acos(dot);

                        double e_min = 1.0 + (p1.safe_radius * v_in_mag * v_in_mag) / std::max(1e-6, p1.mu);
                        double delta_max = 2.0 * std::asin(1.0 / std::max(1.0001, e_min));

                        double turn_angle = std::min(req_theta, delta_max);
                        actual_turn = turn_angle * RAD2DEG;

                        double e_req = 1.0 / std::max(1e-4, std::sin(turn_angle * 0.5));
                        actual_rp = std::max(p1.safe_radius, (e_req - 1.0) * p1.mu / (v_in_mag * v_in_mag));

                        // 采用精确的三维罗德里格斯偏转 (Rodriguez Rotation)，沿 u_in 与 u_tgt 构成的最佳双曲借力轨道面偏转至目标方向
                        double nx = u_in[1]*u_tgt[2] - u_in[2]*u_tgt[1];
                        double ny = u_in[2]*u_tgt[0] - u_in[0]*u_tgt[2];
                        double nz = u_in[0]*u_tgt[1] - u_in[1]*u_tgt[0];
                        double n_len = std::sqrt(nx*nx + ny*ny + nz*nz);
                        if (n_len < 1e-6) {
                            if (std::abs(u_in[0]) < 0.9) { nx = 1.0; ny = 0; nz = 0; }
                            else { nx = 0; ny = 1.0; nz = 0; }
                            double dot_u = nx*u_in[0] + ny*u_in[1] + nz*u_in[2];
                            nx -= dot_u * u_in[0]; ny -= dot_u * u_in[1]; nz -= dot_u * u_in[2];
                            n_len = std::sqrt(nx*nx + ny*ny + nz*nz);
                        }
                        nx /= n_len; ny /= n_len; nz /= n_len;

                        double cos_t = std::cos(turn_angle);
                        double sin_t = std::sin(turn_angle);
                        double cross_u[3] = { ny*u_in[2] - nz*u_in[1], nz*u_in[0] - nx*u_in[2], nx*u_in[1] - ny*u_in[0] };
                        double u_out[3] = { u_in[0]*cos_t + cross_u[0]*sin_t, u_in[1]*cos_t + cross_u[1]*sin_t, u_in[2]*cos_t + cross_u[2]*sin_t };
                        out_v_dep = { p1.vx + v_in_mag * u_out[0], p1.vy + v_in_mag * u_out[1], p1.vz + v_in_mag * u_out[2] };
                    }

                    cur_v_dep = out_v_dep;
                    leg.v_inf_dep_x = cur_v_dep[0] - p1.vx;
                    leg.v_inf_dep_y = cur_v_dep[1] - p1.vy;
                    leg.v_inf_dep_z = cur_v_dep[2] - p1.vz;
                    leg.flyby_turn_deg = actual_turn;
                    leg.flyby_rp_km = actual_rp;
                    leg.is_feasible = 1;
                    leg.flyby_dv_kms = 0.0;
                }

                leg.v1x = cur_v_dep[0]; leg.v1y = cur_v_dep[1]; leg.v1z = cur_v_dep[2];
                v_dsm_m = cur_v_dep;
                try {
                    pykep_propagate_lagrangian(r_dsm, v_dsm_m, dt1, effective_mu);
                } catch (...) {
                    r_dsm = { p1.rx + cur_v_dep[0] * dt1, p1.ry + cur_v_dep[1] * dt1, p1.rz + cur_v_dep[2] * dt1 };
                    v_dsm_m = cur_v_dep;
                    leg.is_feasible = 0;
                    out_sol->all_feasible = 0;
                }

                std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
                std::array<double, 3> v_dsm_p = {0, 0, 0};
                std::array<double, 3> v2_arr = {0, 0, 0};

                bool lam_ok = false;
                double best_dsm = 1e18;
                try {
                    double r_dsm_len = std::sqrt(r_dsm[0]*r_dsm[0] + r_dsm[1]*r_dsm[1] + r_dsm[2]*r_dsm[2]);
                    double r2_len_dsm = std::sqrt(r2_arr[0]*r2_arr[0] + r2_arr[1]*r2_arr[1] + r2_arr[2]*r2_arr[2]);
                    double r_avg_dsm = (r_dsm_len + r2_len_dsm) * 0.5;
                    double t_orb_dsm = 2.0 * 3.14159265358979323846 * std::sqrt((r_avg_dsm * r_avg_dsm * r_avg_dsm) / effective_mu);
                    int nmax = (t_orb_dsm > 10.0) ? (int)std::floor(dt2 / t_orb_dsm + 0.25) : 0;
                    nmax = std::max(0, std::min(4, nmax));
                    kep3::lambert_problem lp_dsm(r_dsm, r2_arr, dt2, effective_mu, false, nmax);
                    const auto& v0_dsm_list = lp_dsm.get_v0();
                    const auto& v1_dsm_list = lp_dsm.get_v1();
                    for (size_t b = 0; b < v0_dsm_list.size(); ++b) {
                        double dv_x = v0_dsm_list[b][0] - v_dsm_m[0];
                        double dv_y = v0_dsm_list[b][1] - v_dsm_m[1];
                        double dv_z = v0_dsm_list[b][2] - v_dsm_m[2];
                        double dsm_mag = std::sqrt(dv_x*dv_x + dv_y*dv_y + dv_z*dv_z);
                        double v0_mag = std::sqrt(v0_dsm_list[b][0]*v0_dsm_list[b][0] + v0_dsm_list[b][1]*v0_dsm_list[b][1] + v0_dsm_list[b][2]*v0_dsm_list[b][2]);
                        if (!std::isnan(dsm_mag) && !std::isinf(dsm_mag) && v0_mag < 90.0 && dsm_mag < best_dsm) {
                            best_dsm = dsm_mag;
                            v_dsm_p = v0_dsm_list[b];
                            v2_arr = v1_dsm_list[b];
                            lam_ok = true;
                        }
                    }
                } catch (...) {}

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
                    leg.has_dsm = 1;
                    leg.dsm.epoch_offset_sec = dt1;
                    leg.dsm.pos_x = r_dsm[0]; leg.dsm.pos_y = r_dsm[1]; leg.dsm.pos_z = r_dsm[2];

                    double dsm_dv_x = v_dsm_p[0] - v_dsm_m[0];
                    double dsm_dv_y = v_dsm_p[1] - v_dsm_m[1];
                    double dsm_dv_z = v_dsm_p[2] - v_dsm_m[2];
                    leg.dsm.dv_x = dsm_dv_x; leg.dsm.dv_y = dsm_dv_y; leg.dsm.dv_z = dsm_dv_z;
                    double dsm_mag_kms = std::sqrt(dsm_dv_x*dsm_dv_x + dsm_dv_y*dsm_dv_y + dsm_dv_z*dsm_dv_z);
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

                leg.v2x = v2_arr[0]; leg.v2y = v2_arr[1]; leg.v2z = v2_arr[2];
                leg.v_inf_arr_x = v2_arr[0] - p2.vx;
                leg.v_inf_arr_y = v2_arr[1] - p2.vy;
                leg.v_inf_arr_z = v2_arr[2] - p2.vz;

                // 极速物理门禁提前截断 (Early Termination for Unreasonable / Impossible Candidates):
                // 一旦中间航段累计 DSM 超过 10 km/s 或总工质 ΔV 超过 16 km/s，该候选解已彻底脱离物理可行域。
                // 立即抛弃剩余航段的全部昂贵兰伯特与开普勒积分计算，赋予平滑线性惩罚并提前返回！
                if (k < leg_count - 1 && (total_dsm_dv > 10.0 || total_dv > 16.0)) {
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

                if (k < leg_count - 1) {
                    const NativePlanetState& p_fb = arr_planets[k];
                    double rp_input = (fb_rps_km != nullptr && fb_rps_km[k] > 0.0) ? fb_rps_km[k] : 0.0;
                    double beta_input = (fb_betas_rad != nullptr) ? fb_betas_rad[k] : 0.0;

                    double v_inf_in_mag = std::sqrt(leg.v_inf_arr_x * leg.v_inf_arr_x + leg.v_inf_arr_y * leg.v_inf_arr_y + leg.v_inf_arr_z * leg.v_inf_arr_z);
                    double safe_rp = p_fb.safe_radius;
                    double actual_rp = (rp_input > safe_rp) ? rp_input : safe_rp * 1.15;
                    double e_fb = 1.0 + (actual_rp * v_inf_in_mag * v_inf_in_mag) / std::max(1e-6, p_fb.mu);
                    double delta = 2.0 * std::asin(1.0 / std::max(1.0001, e_fb));

                    leg.flyby_rp_km = actual_rp;
                    leg.flyby_turn_deg = delta * RAD2DEG;
                    leg.flyby_beta_deg = beta_input * RAD2DEG;

                    // 优先调用 kep3 官方 fb_vout
                    std::array<double, 3> vin_abs = {leg.v_inf_arr_x + p_fb.vx, leg.v_inf_arr_y + p_fb.vy, leg.v_inf_arr_z + p_fb.vz};
                    std::array<double, 3> vpla_abs = {p_fb.vx, p_fb.vy, p_fb.vz};
                    try {
                        cur_v_dep = kep3::fb_vout(vin_abs, vpla_abs, actual_rp, beta_input, p_fb.mu);
                    } catch (...) {
                        double v_rel_in[3] = {leg.v_inf_arr_x, leg.v_inf_arr_y, leg.v_inf_arr_z};
                        double v_rel_in_norm = std::max(1e-6, v_inf_in_mag);
                        double i_hat[3] = {v_rel_in[0] / v_rel_in_norm, v_rel_in[1] / v_rel_in_norm, v_rel_in[2] / v_rel_in_norm};

                        double j_cross[3] = {
                            i_hat[1] * p_fb.vz - i_hat[2] * p_fb.vy,
                            i_hat[2] * p_fb.vx - i_hat[0] * p_fb.vz,
                            i_hat[0] * p_fb.vy - i_hat[1] * p_fb.vx
                        };
                        double j_len = std::sqrt(j_cross[0]*j_cross[0] + j_cross[1]*j_cross[1] + j_cross[2]*j_cross[2]);
                        if (j_len < 1e-6) { j_cross[0]=0; j_cross[1]=0; j_cross[2]=1; j_len=1; }
                        double j_hat[3] = {j_cross[0]/j_len, j_cross[1]/j_len, j_cross[2]/j_len};

                        double k_hat[3] = {
                            i_hat[1] * j_hat[2] - i_hat[2] * j_hat[1],
                            i_hat[2] * j_hat[0] - i_hat[0] * j_hat[2],
                            i_hat[0] * j_hat[1] - i_hat[1] * j_hat[0]
                        };

                        double cos_d = std::cos(delta);
                        double sin_d = std::sin(delta);
                        double cos_b = std::cos(beta_input);
                        double sin_b = std::sin(beta_input);

                        double v_out_rel[3] = {
                            v_rel_in_norm * (cos_d * i_hat[0] + sin_d * cos_b * j_hat[0] + sin_d * sin_b * k_hat[0]),
                            v_rel_in_norm * (cos_d * i_hat[1] + sin_d * cos_b * j_hat[1] + sin_d * sin_b * k_hat[1]),
                            v_rel_in_norm * (cos_d * i_hat[2] + sin_d * cos_b * j_hat[2] + sin_d * sin_b * k_hat[2])
                        };

                        cur_v_dep[0] = p_fb.vx + v_out_rel[0];
                        cur_v_dep[1] = p_fb.vy + v_out_rel[1];
                        cur_v_dep[2] = p_fb.vz + v_out_rel[2];
                    }
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

    #pragma omp parallel for schedule(dynamic)
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

