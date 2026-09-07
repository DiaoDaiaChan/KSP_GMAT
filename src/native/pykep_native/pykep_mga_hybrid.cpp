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

            int strat = (leg_strategies != nullptr) ? leg_strategies[k] : 1;
            double eta = (dsm_etas != nullptr && dsm_etas[k] > 0.0) ? std::max(0.05, std::min(0.95, dsm_etas[k])) : 0.5;

            // STRATEGY 0: PURE BALLISTIC LAMBERT TRANSFER (NO DSM)
            if (strat == 0) {
                std::array<double, 3> r1_arr = {p1.rx, p1.ry, p1.rz};
                std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
                std::array<double, 3> v1_lam = {0, 0, 0};
                std::array<double, 3> v2_lam = {0, 0, 0};
                bool lam_ok = false;
                try {
                    int nmax = (tof > 86400.0 * 250.0) ? 2 : 0;
                    kep3::lambert_problem lp(r1_arr, r2_arr, tof, effective_mu, false, nmax);
                    double best_cost = 1e18;
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

                if (!lam_ok) {
                    out_sol->all_feasible = 0;
                    return -2;
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
                    if (fb_strat == 1 || fb_ok != 0) {
                        leg.flyby_dv_kms = pykep_flyby_dv(prev_leg.v_inf_arr_x, prev_leg.v_inf_arr_y, prev_leg.v_inf_arr_z,
                                                          leg.v_inf_dep_x, leg.v_inf_dep_y, leg.v_inf_dep_z,
                                                          p1.mu, p1.safe_radius);
                        total_flyby_dv += leg.flyby_dv_kms;
                        total_dv += leg.flyby_dv_kms;
                    }
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
                    std::array<double, 3> r1_arr = {p1.rx, p1.ry, p1.rz};
                    std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
                    int nmax_init = (tof > 86400.0 * 250.0) ? 2 : 0;
                    kep3::lambert_problem lp_init(r1_arr, r2_arr, tof, effective_mu, false, nmax_init);
                    double best_vinf = 1e18;
                    const auto& v0_init_list = lp_init.get_v0();
                    for (size_t b = 0; b < v0_init_list.size(); ++b) {
                        double vx = v0_init_list[b][0] - p1.vx;
                        double vy = v0_init_list[b][1] - p1.vy;
                        double vz = v0_init_list[b][2] - p1.vz;
                        double vinf = std::sqrt(vx*vx + vy*vy + vz*vz);
                        if (vinf < best_vinf) {
                            best_vinf = vinf;
                            cur_v_dep = v0_init_list[b];
                        }
                    }
                    if (best_vinf > 1e17) {
                        cur_v_dep = {p1.vx * 1.1, p1.vy * 1.1, p1.vz * 1.1};
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
                    int nmax_full = (tof > 86400.0 * 250.0) ? 2 : 0;
                    kep3::lambert_problem lp_full(r1_arr, r2_arr, tof, effective_mu, false, nmax_full);
                    
                    std::array<double, 3> v_tgt_dep = {p1.vx, p1.vy, p1.vz};
                    double best_tgt_cost = 1e18;
                    const auto& v0_full_list = lp_full.get_v0();
                    for (size_t b = 0; b < v0_full_list.size(); ++b) {
                        double vx = v0_full_list[b][0] - p1.vx;
                        double vy = v0_full_list[b][1] - p1.vy;
                        double vz = v0_full_list[b][2] - p1.vz;
                        double dv_diff = std::abs(std::sqrt(vx*vx + vy*vy + vz*vz) - v_in_mag);
                        if (dv_diff < best_tgt_cost) {
                            best_tgt_cost = dv_diff;
                            v_tgt_dep = v0_full_list[b];
                        }
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

                        // 优先尝试官方原生 fb_vout
                        std::array<double, 3> vin_abs = {prev_leg.v_inf_arr_x + p1.vx, prev_leg.v_inf_arr_y + p1.vy, prev_leg.v_inf_arr_z + p1.vz};
                        std::array<double, 3> vpla_abs = {p1.vx, p1.vy, p1.vz};
                        try {
                            out_v_dep = kep3::fb_vout(vin_abs, vpla_abs, actual_rp, 0.0, p1.mu);
                        } catch (...) {
                            double nx = u_in[1]*u_tgt[2] - u_in[2]*u_tgt[1];
                            double ny = u_in[2]*u_tgt[0] - u_in[0]*u_tgt[2];
                            double nz = u_in[0]*u_tgt[1] - u_in[1]*u_tgt[0];
                            double n_len = std::sqrt(nx*nx + ny*ny + nz*nz);
                            if (n_len < 1e-6) { nx = 0; ny = 0; nz = 1.0; n_len = 1.0; }
                            else { nx /= n_len; ny /= n_len; nz /= n_len; }

                            double cos_t = std::cos(turn_angle);
                            double sin_t = std::sin(turn_angle);
                            double cross_u[3] = { ny*u_in[2] - nz*u_in[1], nz*u_in[0] - nx*u_in[2], nx*u_in[1] - ny*u_in[0] };
                            double u_out[3] = { u_in[0]*cos_t + cross_u[0]*sin_t, u_in[1]*cos_t + cross_u[1]*sin_t, u_in[2]*cos_t + cross_u[2]*sin_t };
                            out_v_dep = { p1.vx + v_in_mag * u_out[0], p1.vy + v_in_mag * u_out[1], p1.vz + v_in_mag * u_out[2] };
                        }
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
                    out_sol->all_feasible = 0;
                    return -3;
                }

                std::array<double, 3> r2_arr = {p2.rx, p2.ry, p2.rz};
                std::array<double, 3> v_dsm_p = {0, 0, 0};
                std::array<double, 3> v2_arr = {0, 0, 0};

                bool lam_ok = false;
                try {
                    int nmax = (dt2 > 86400.0 * 250.0) ? 2 : 0;
                    kep3::lambert_problem lp_dsm(r_dsm, r2_arr, dt2, effective_mu, false, nmax);
                    double best_dsm = 1e18;
                    const auto& v0_dsm_list = lp_dsm.get_v0();
                    const auto& v1_dsm_list = lp_dsm.get_v1();
                    for (size_t b = 0; b < v0_dsm_list.size(); ++b) {
                        double dv_x = v0_dsm_list[b][0] - v_dsm_m[0];
                        double dv_y = v0_dsm_list[b][1] - v_dsm_m[1];
                        double dv_z = v0_dsm_list[b][2] - v_dsm_m[2];
                        double dsm_mag = std::sqrt(dv_x*dv_x + dv_y*dv_y + dv_z*dv_z);
                        if (dsm_mag < best_dsm) {
                            best_dsm = dsm_mag;
                            v_dsm_p = v0_dsm_list[b];
                            v2_arr = v1_dsm_list[b];
                            lam_ok = true;
                        }
                    }
                } catch (...) {}

                if (!lam_ok) {
                    out_sol->all_feasible = 0;
                    return -4;
                }

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

                leg.v2x = v2_arr[0]; leg.v2y = v2_arr[1]; leg.v2z = v2_arr[2];
                leg.v_inf_arr_x = v2_arr[0] - p2.vx;
                leg.v_inf_arr_y = v2_arr[1] - p2.vy;
                leg.v_inf_arr_z = v2_arr[2] - p2.vz;

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
