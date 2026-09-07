// ============================================================================
// ESA PyKEP v3 (kep3) Official Native C-API Bridge
// Module 8: Parking Orbit Injection Phase & Ejection Angle Native Aligner
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

EXPORT_API int pykep_align_injection(
    const double* r_park0,      // [3] km: spacecraft position at t_epoch
    const double* v_park0,      // [3] km/s: spacecraft velocity at t_epoch
    double t_epoch,             // epoch of (r_park0, v_park0)
    double mu_central,          // km^3/s^2
    int align_mode,             // 0 = target position (Lambert/inter-satellite), 1 = v_inf ejection (interplanetary)
    const double* target_arg,   // mode 0: target pos [3] km; mode 1: v_inf vector [3] km/s
    double target_scalar,       // mode 0: arrival UT (s); mode 1: v_inf magnitude (km/s)
    double t_start,             // search start UT (s)
    double t_span,              // search time window (s), typically 1 orbit period
    int sample_count,           // coarse grid count (e.g. 72)
    int golden_iterations,      // golden section search iterations (e.g. 15)
    double* out_aligned_ut,
    double* out_aligned_dv_frenet, // [3] prograde, radial, normal (m/s)
    double* out_aligned_dv_cart    // [3] cartesian dv (m/s), can be null
)
{
    if (!r_park0 || !v_park0 || !target_arg || !out_aligned_ut || !out_aligned_dv_frenet) {
        return -1;
    }
    if (mu_central <= 0.0 || t_span <= 0.0) {
        return -2;
    }

    if (sample_count < 12) sample_count = 72;
    if (golden_iterations < 5) golden_iterations = 15;

    const double gr = 0.6180339887498948482; // (sqrt(5) - 1) / 2

    // Helper: propagate parking orbit to arbitrary time t via Lagrangian universal variables
    auto get_state_at = [&](double t, std::array<double, 3>& r_out, std::array<double, 3>& v_out) -> bool {
        r_out = { r_park0[0], r_park0[1], r_park0[2] };
        v_out = { v_park0[0], v_park0[1], v_park0[2] };
        double dt = t - t_epoch;
        if (std::abs(dt) > 1e-7) {
            try {
                pykep_propagate_lagrangian(r_out, v_out, dt, mu_central);
            } catch (...) {
                return false;
            }
        }
        return true;
    };

    if (align_mode == 0) {
        // =========================================================================
        // Mode 0: Target Position Transfer (e.g. Earth parking orbit -> Moon)
        // Objective: Minimize |v1_trans - v_craft| to match tangential prograde burn
        // =========================================================================
        std::array<double, 3> r_target = { target_arg[0], target_arg[1], target_arg[2] };
        double arr_ut = target_scalar;

        auto eval_cost = [&](double t, double& out_cost, std::array<double, 3>& out_v1, 
                             std::array<double, 3>& out_rc, std::array<double, 3>& out_vc) -> bool {
            out_cost = 1e12;
            double tof = arr_ut - t;
            if (tof <= 10.0) return false;

            if (!get_state_at(t, out_rc, out_vc)) return false;

            try {
                kep3::lambert_problem lp(out_rc, r_target, tof, mu_central, false, 0);
                const auto& v1_list = lp.get_v0();
                if (v1_list.empty()) return false;

                double min_branch_cost = 1e12;
                std::array<double, 3> min_branch_v1 = v1_list[0];
                for (size_t b = 0; b < v1_list.size(); ++b) {
                    double dvx = (v1_list[b][0] - out_vc[0]) * 1000.0;
                    double dvy = (v1_list[b][1] - out_vc[1]) * 1000.0;
                    double dvz = (v1_list[b][2] - out_vc[2]) * 1000.0;
                    double b_cost = std::sqrt(dvx * dvx + dvy * dvy + dvz * dvz);
                    if (b_cost < min_branch_cost) {
                        min_branch_cost = b_cost;
                        min_branch_v1 = v1_list[b];
                    }
                }
                out_v1 = min_branch_v1;
                out_cost = min_branch_cost;
                return true;
            } catch (...) {
                return false;
            }
        };

        // Step 1: Coarse Grid Search
        double best_t = t_start;
        double best_cost = 1e12;
        std::array<double, 3> best_v1 = {0, 0, 0};
        std::array<double, 3> best_rc = {0, 0, 0};
        std::array<double, 3> best_vc = {0, 0, 0};

        double dt_step = t_span / (double)(sample_count - 1);
        int best_idx = 0;

        for (int i = 0; i < sample_count; ++i) {
            double t_cand = t_start + i * dt_step;
            double cost = 1e12;
            std::array<double, 3> v1_cand, rc_cand, vc_cand;
            if (eval_cost(t_cand, cost, v1_cand, rc_cand, vc_cand)) {
                if (cost < best_cost) {
                    best_cost = cost;
                    best_t = t_cand;
                    best_idx = i;
                    best_v1 = v1_cand;
                    best_rc = rc_cand;
                    best_vc = vc_cand;
                }
            }
        }

        // Step 2: Golden Section Search around best_t
        double a = std::max(t_start, best_t - dt_step);
        double b = std::min(t_start + t_span, best_t + dt_step);
        double c = b - gr * (b - a);
        double d = a + gr * (b - a);

        double fc = 1e12, fd = 1e12;
        std::array<double, 3> v1c, rcc, vcc, v1d, rcd, vcd;
        eval_cost(c, fc, v1c, rcc, vcc);
        eval_cost(d, fd, v1d, rcd, vcd);

        for (int iter = 0; iter < golden_iterations; ++iter) {
            if (fc < fd) {
                b = d;
                d = c;
                fd = fc;
                v1d = v1c; rcd = rcc; vcd = vcc;
                c = b - gr * (b - a);
                eval_cost(c, fc, v1c, rcc, vcc);
            } else {
                a = c;
                c = d;
                fc = fd;
                v1c = v1d; rcc = rcd; vcc = vcd;
                d = a + gr * (b - a);
                eval_cost(d, fd, v1d, rcd, vcd);
            }
        }

        double opt_t = (a + b) * 0.5;
        double opt_cost = 1e12;
        std::array<double, 3> opt_v1, opt_rc, opt_vc;
        if (!eval_cost(opt_t, opt_cost, opt_v1, opt_rc, opt_vc)) {
            opt_t = best_t;
            opt_v1 = best_v1;
            opt_rc = best_rc;
            opt_vc = best_vc;
        }

        *out_aligned_ut = opt_t;

        // Cartesian delta V (m/s)
        double dv_cart[3] = {
            (opt_v1[0] - opt_vc[0]) * 1000.0,
            (opt_v1[1] - opt_vc[1]) * 1000.0,
            (opt_v1[2] - opt_vc[2]) * 1000.0
        };

        if (out_aligned_dv_cart) {
            out_aligned_dv_cart[0] = dv_cart[0];
            out_aligned_dv_cart[1] = dv_cart[1];
            out_aligned_dv_cart[2] = dv_cart[2];
        }

        // Project Cartesian delta V onto Spacecraft Local Orbital Frame (Prograde, Radial, Normal)
        double vc_mag = std::sqrt(opt_vc[0] * opt_vc[0] + opt_vc[1] * opt_vc[1] + opt_vc[2] * opt_vc[2]);
        if (vc_mag > 1e-7) {
            double v_hat[3] = { opt_vc[0] / vc_mag, opt_vc[1] / vc_mag, opt_vc[2] / vc_mag };
            double h_vec[3] = {
                opt_rc[1] * opt_vc[2] - opt_rc[2] * opt_vc[1],
                opt_rc[2] * opt_vc[0] - opt_rc[0] * opt_vc[2],
                opt_rc[0] * opt_vc[1] - opt_rc[1] * opt_vc[0]
            };
            double h_mag = std::sqrt(h_vec[0] * h_vec[0] + h_vec[1] * h_vec[1] + h_vec[2] * h_vec[2]);
            double n_hat[3] = { 0, 0, 1 };
            if (h_mag > 1e-7) {
                n_hat[0] = h_vec[0] / h_mag;
                n_hat[1] = h_vec[1] / h_mag;
                n_hat[2] = h_vec[2] / h_mag;
            }
            double r_hat[3] = {
                v_hat[1] * n_hat[2] - v_hat[2] * n_hat[1],
                v_hat[2] * n_hat[0] - v_hat[0] * n_hat[2],
                v_hat[0] * n_hat[1] - v_hat[1] * n_hat[0]
            };

            out_aligned_dv_frenet[0] = dv_cart[0] * v_hat[0] + dv_cart[1] * v_hat[1] + dv_cart[2] * v_hat[2]; // Prograde
            out_aligned_dv_frenet[1] = dv_cart[0] * r_hat[0] + dv_cart[1] * r_hat[1] + dv_cart[2] * r_hat[2]; // Radial
            out_aligned_dv_frenet[2] = dv_cart[0] * n_hat[0] + dv_cart[1] * n_hat[1] + dv_cart[2] * n_hat[2]; // Normal
        } else {
            out_aligned_dv_frenet[0] = std::sqrt(dv_cart[0]*dv_cart[0] + dv_cart[1]*dv_cart[1] + dv_cart[2]*dv_cart[2]);
            out_aligned_dv_frenet[1] = 0;
            out_aligned_dv_frenet[2] = 0;
        }

        return 0;
    }
    else {
        // =========================================================================
        // Mode 1: Interplanetary Hyperbolic Escape (v_inf asymptotic direction alignment)
        // Objective: Find burn true anomaly / UT such that the outgoing asymptote S_hat
        // aligns with target v_inf vector (maximizing (v_craft . v_inf) + turning angle geometry)
        // =========================================================================
        double v_inf_vec[3] = { target_arg[0], target_arg[1], target_arg[2] };
        double v_inf_mag = target_scalar;
        if (v_inf_mag <= 1e-6) {
            v_inf_mag = std::sqrt(v_inf_vec[0]*v_inf_vec[0] + v_inf_vec[1]*v_inf_vec[1] + v_inf_vec[2]*v_inf_vec[2]);
        }
        if (v_inf_mag <= 1e-6) return -3;

        double s_hat[3] = { v_inf_vec[0] / v_inf_mag, v_inf_vec[1] / v_inf_mag, v_inf_vec[2] / v_inf_mag };

        auto eval_escape_cost = [&](double t, double& out_cost, std::array<double, 3>& out_rc, std::array<double, 3>& out_vc) -> bool {
            out_cost = 1e12;
            if (!get_state_at(t, out_rc, out_vc)) return false;

            double r_len = std::sqrt(out_rc[0]*out_rc[0] + out_rc[1]*out_rc[1] + out_rc[2]*out_rc[2]);
            double vc_len = std::sqrt(out_vc[0]*out_vc[0] + out_vc[1]*out_vc[1] + out_vc[2]*out_vc[2]);
            if (r_len < 1e-6 || vc_len < 1e-6) return false;

            // Hyperbolic turn angle beta: cos(beta) = -1 / e
            double e_hyp = 1.0 + (r_len * v_inf_mag * v_inf_mag) / mu_central;
            double cos_beta = -1.0 / std::max(1.0001, e_hyp);
            double beta_rad = std::acos(std::max(-1.0, std::min(1.0, cos_beta)));
            double phi_turn = beta_rad - PI * 0.5; // Lead angle between periapsis and asymptote

            // Tangential burn direction at periapsis: v_hat
            double v_hat[3] = { out_vc[0] / vc_len, out_vc[1] / vc_len, out_vc[2] / vc_len };
            double r_hat[3] = { out_rc[0] / r_len, out_rc[1] / r_len, out_rc[2] / r_len };

            // Asymptotic direction predicted from this burn point: S_pred = cos(phi)*v_hat + sin(phi)*r_hat
            double s_pred[3] = {
                std::cos(phi_turn) * v_hat[0] + std::sin(phi_turn) * r_hat[0],
                std::cos(phi_turn) * v_hat[1] + std::sin(phi_turn) * r_hat[1],
                std::cos(phi_turn) * v_hat[2] + std::sin(phi_turn) * r_hat[2]
            };
            double s_pred_len = std::sqrt(s_pred[0]*s_pred[0] + s_pred[1]*s_pred[1] + s_pred[2]*s_pred[2]);
            if (s_pred_len > 1e-6) {
                s_pred[0] /= s_pred_len; s_pred[1] /= s_pred_len; s_pred[2] /= s_pred_len;
            }

            // Angular mismatch between predicted asymptote and target v_inf
            double dot = s_pred[0]*s_hat[0] + s_pred[1]*s_hat[1] + s_pred[2]*s_hat[2];
            dot = std::max(-1.0, std::min(1.0, dot));
            out_cost = std::acos(dot); // Angular error in radians
            return true;
        };

        // Step 1: Coarse Grid Search for Minimum Angular Misalignment
        double best_t = t_start;
        double best_cost = 1e12;
        std::array<double, 3> best_rc = {0, 0, 0};
        std::array<double, 3> best_vc = {0, 0, 0};

        double dt_step = t_span / (double)(sample_count - 1);
        for (int i = 0; i < sample_count; ++i) {
            double t_cand = t_start + i * dt_step;
            double cost = 1e12;
            std::array<double, 3> rc_cand, vc_cand;
            if (eval_escape_cost(t_cand, cost, rc_cand, vc_cand)) {
                if (cost < best_cost) {
                    best_cost = cost;
                    best_t = t_cand;
                    best_rc = rc_cand;
                    best_vc = vc_cand;
                }
            }
        }

        // Step 2: Golden Section Search for Sub-Milliradian Precision
        double a = std::max(t_start, best_t - dt_step);
        double b = std::min(t_start + t_span, best_t + dt_step);
        double c = b - gr * (b - a);
        double d = a + gr * (b - a);

        double fc = 1e12, fd = 1e12;
        std::array<double, 3> rcc, vcc, rcd, vcd;
        eval_escape_cost(c, fc, rcc, vcc);
        eval_escape_cost(d, fd, rcd, vcd);

        for (int iter = 0; iter < golden_iterations; ++iter) {
            if (fc < fd) {
                b = d; d = c; fd = fc;
                rcd = rcc; vcd = vcc;
                c = b - gr * (b - a);
                eval_escape_cost(c, fc, rcc, vcc);
            } else {
                a = c; c = d; fc = fd;
                rcc = rcd; vcc = vcd;
                d = a + gr * (b - a);
                eval_escape_cost(d, fd, rcd, vcd);
            }
        }

        double opt_t = (a + b) * 0.5;
        double opt_cost = 1e12;
        std::array<double, 3> opt_rc, opt_vc;
        if (!eval_escape_cost(opt_t, opt_cost, opt_rc, opt_vc)) {
            opt_t = best_t;
            opt_rc = best_rc;
            opt_vc = best_vc;
        }

        *out_aligned_ut = opt_t;

        // Calculate theoretical escape burn delta V magnitude via Vis-Viva
        double r_len = std::sqrt(opt_rc[0]*opt_rc[0] + opt_rc[1]*opt_rc[1] + opt_rc[2]*opt_rc[2]);
        double vc_len = std::sqrt(opt_vc[0]*opt_vc[0] + opt_vc[1]*opt_vc[1] + opt_vc[2]*opt_vc[2]);
        double v_hyp = std::sqrt(v_inf_mag * v_inf_mag + 2.0 * mu_central / r_len);
        double dv_escape_kms = std::max(0.0, v_hyp - vc_len);
        double dv_escape_ms = dv_escape_kms * 1000.0;

        // Mostly prograde burn aligned with velocity vector
        out_aligned_dv_frenet[0] = dv_escape_ms; // Prograde
        out_aligned_dv_frenet[1] = 0.0;          // Radial
        out_aligned_dv_frenet[2] = 0.0;          // Normal

        if (out_aligned_dv_cart && vc_len > 1e-7) {
            out_aligned_dv_cart[0] = (opt_vc[0] / vc_len) * dv_escape_ms;
            out_aligned_dv_cart[1] = (opt_vc[1] / vc_len) * dv_escape_ms;
            out_aligned_dv_cart[2] = (opt_vc[2] / vc_len) * dv_escape_ms;
        }

        return 0;
    }
}
