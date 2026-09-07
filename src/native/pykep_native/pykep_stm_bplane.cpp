// ============================================================================
// ESA PyKEP v3 (kep3) Official Native C-API Bridge
// Module 3: Keplerian STM & B-Plane TCM Midcourse Targeting Solver
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

EXPORT_API int kep3_calc_reynolds_stm(
    const double* r0_km, const double* v0_kms, double dt_sec, double mu,
    double* out_stm_6x6)
{
    if (!r0_km || !v0_kms || !out_stm_6x6 || mu <= 0.0) return -1;

    // 1. 优先使用 kep3 官方解析拉格朗日微商 STM (Analytical Lagrangian STM, JGCD 2022)
    try {
        std::array<std::array<double, 3>, 2> pos_vel0 = {{
            {r0_km[0], r0_km[1], r0_km[2]},
            {v0_kms[0], v0_kms[1], v0_kms[2]}
        }};

        auto [rf_vf, stm_opt] = kep3::propagate_lagrangian(pos_vel0, dt_sec, mu, true);
        if (stm_opt.has_value()) {
            const auto& stm = stm_opt.value();
            for (int i = 0; i < 36; ++i) {
                out_stm_6x6[i] = stm[i];
            }
            return 0;
        }
    } catch (...) {
    }

    // 2. 备选 kep3 Reynolds 解析 STM
    try {
        std::array<std::array<double, 3>, 2> pos_vel0 = {{
            {r0_km[0], r0_km[1], r0_km[2]},
            {v0_kms[0], v0_kms[1], v0_kms[2]}
        }};

        auto [rf_vf, stm_opt] = kep3::propagate_stm_reynolds(pos_vel0, dt_sec, mu, true);
        if (stm_opt.has_value()) {
            const auto& stm = stm_opt.value();
            for (int i = 0; i < 36; ++i) {
                out_stm_6x6[i] = stm[i];
            }
            return 0;
        }
    } catch (...) {
    }

    return -2;
}

EXPORT_API int pykep_calc_kepler_stm(
    const double* r0_km, const double* v0_kms, double dt_sec, double mu,
    double* out_stm_6x6)
{
    if (!r0_km || !v0_kms || !out_stm_6x6 || mu <= 0.0) return -1;

    // 优先使用 kep3 官方解析 STM (机器精度)
    if (kep3_calc_reynolds_stm(r0_km, v0_kms, dt_sec, mu, out_stm_6x6) == 0) {
        return 0;
    }

    // 后备：高精拉格朗日有限中心差分
    try {
        double eps_r = 1e-4; // 10 cm
        double eps_v = 1e-7; // 0.1 mm/s

        for (int j = 0; j < 6; ++j) {
            std::array<double, 3> r_p = {r0_km[0], r0_km[1], r0_km[2]};
            std::array<double, 3> v_p = {v0_kms[0], v0_kms[1], v0_kms[2]};
            std::array<double, 3> r_m = {r0_km[0], r0_km[1], r0_km[2]};
            std::array<double, 3> v_m = {v0_kms[0], v0_kms[1], v0_kms[2]};

            double eps = (j < 3) ? eps_r : eps_v;
            if (j < 3) {
                r_p[j] += eps;
                r_m[j] -= eps;
            } else {
                v_p[j - 3] += eps;
                v_m[j - 3] -= eps;
            }

            pykep_propagate_lagrangian(r_p, v_p, dt_sec, mu);
            pykep_propagate_lagrangian(r_m, v_m, dt_sec, mu);

            double denom = 2.0 * eps;
            out_stm_6x6[0 * 6 + j] = (r_p[0] - r_m[0]) / denom;
            out_stm_6x6[1 * 6 + j] = (r_p[1] - r_m[1]) / denom;
            out_stm_6x6[2 * 6 + j] = (r_p[2] - r_m[2]) / denom;
            out_stm_6x6[3 * 6 + j] = (v_p[0] - v_m[0]) / denom;
            out_stm_6x6[4 * 6 + j] = (v_p[1] - v_m[1]) / denom;
            out_stm_6x6[5 * 6 + j] = (v_p[2] - v_m[2]) / denom;
        }
        return 0;
    } catch (...) {
        return -99;
    }
}

EXPORT_API int pykep_calc_bplane(
    const double* r_rel_km, const double* v_rel_kms, double mu,
    NativeBPlaneResult* out_bp)
{
    if (!r_rel_km || !v_rel_kms || !out_bp || mu <= 0.0) return -1;

    try {
        std::memset(out_bp, 0, sizeof(NativeBPlaneResult));

        double v2 = v_rel_kms[0]*v_rel_kms[0] + v_rel_kms[1]*v_rel_kms[1] + v_rel_kms[2]*v_rel_kms[2];
        double v_mag = std::sqrt(v2);
        if (v_mag < 1e-6) return -2;

        out_bp->v_inf_mag_kms = v_mag;

        double s[3] = {v_rel_kms[0]/v_mag, v_rel_kms[1]/v_mag, v_rel_kms[2]/v_mag};
        out_bp->s_hat[0] = s[0]; out_bp->s_hat[1] = s[1]; out_bp->s_hat[2] = s[2];

        double k[3] = {0, 0, 1};
        double t[3] = {s[1]*k[2] - s[2]*k[1], s[2]*k[0] - s[0]*k[2], s[0]*k[1] - s[1]*k[0]};
        double t_len = std::sqrt(t[0]*t[0] + t[1]*t[1] + t[2]*t[2]);
        if (t_len < 1e-6) { t[0] = 1; t[1] = 0; t[2] = 0; t_len = 1; }
        t[0] /= t_len; t[1] /= t_len; t[2] /= t_len;
        out_bp->t_hat[0] = t[0]; out_bp->t_hat[1] = t[1]; out_bp->t_hat[2] = t[2];

        double r[3] = {s[1]*t[2] - s[2]*t[1], s[2]*t[0] - s[0]*t[2], s[0]*t[1] - s[1]*t[0]};
        out_bp->r_hat[0] = r[0]; out_bp->r_hat[1] = r[1]; out_bp->r_hat[2] = r[2];

        double r_dot_s = r_rel_km[0]*s[0] + r_rel_km[1]*s[1] + r_rel_km[2]*s[2];
        double b_vec[3] = {r_rel_km[0] - r_dot_s*s[0], r_rel_km[1] - r_dot_s*s[1], r_rel_km[2] - r_dot_s*s[2]};

        out_bp->b_dot_t_km = b_vec[0]*t[0] + b_vec[1]*t[1] + b_vec[2]*t[2];
        out_bp->b_dot_r_km = b_vec[0]*r[0] + b_vec[1]*r[1] + b_vec[2]*r[2];
        out_bp->b_mag_km = std::sqrt(out_bp->b_dot_t_km * out_bp->b_dot_t_km + out_bp->b_dot_r_km * out_bp->b_dot_r_km);
        out_bp->theta_deg = std::atan2(out_bp->b_dot_r_km, out_bp->b_dot_t_km) * RAD2DEG;
        return 0;
    } catch (...) {
        return -99;
    }
}

EXPORT_API int pykep_calc_bplane_tcm(
    const double* r_tcm_km, const double* v_tcm_kms, double dt_to_enc_sec,
    const double* r_enc_curr_km, const double* v_enc_curr_kms,
    double target_b_dot_t_km, double target_b_dot_r_km, double body_mu,
    NativeTCMResult* out_tcm)
{
    if (!r_tcm_km || !v_tcm_kms || !r_enc_curr_km || !v_enc_curr_kms || !out_tcm || body_mu <= 0.0) return -1;

    try {
        std::memset(out_tcm, 0, sizeof(NativeTCMResult));

        NativeBPlaneResult bp_curr;
        pykep_calc_bplane(r_enc_curr_km, v_enc_curr_kms, body_mu, &bp_curr);

        double d_bt = target_b_dot_t_km - bp_curr.b_dot_t_km;
        double d_br = target_b_dot_r_km - bp_curr.b_dot_r_km;

        double stm[36];
        pykep_calc_kepler_stm(r_tcm_km, v_tcm_kms, dt_to_enc_sec, SUN_MU, stm);

        double M[2][3] = {{0}};
        for (int col = 0; col < 3; ++col) {
            double phi_rv[3] = {stm[0 * 6 + (3 + col)], stm[1 * 6 + (3 + col)], stm[2 * 6 + (3 + col)]};
            M[0][col] = bp_curr.t_hat[0]*phi_rv[0] + bp_curr.t_hat[1]*phi_rv[1] + bp_curr.t_hat[2]*phi_rv[2];
            M[1][col] = bp_curr.r_hat[0]*phi_rv[0] + bp_curr.r_hat[1]*phi_rv[1] + bp_curr.r_hat[2]*phi_rv[2];
        }

        double A00 = M[0][0]*M[0][0] + M[0][1]*M[0][1] + M[0][2]*M[0][2];
        double A01 = M[0][0]*M[1][0] + M[0][1]*M[1][1] + M[0][2]*M[1][2];
        double A10 = A01;
        double A11 = M[1][0]*M[1][0] + M[1][1]*M[1][1] + M[1][2]*M[1][2];

        double det = A00*A11 - A01*A10;
        if (std::abs(det) < 1e-12) return -3;

        double invA00 = A11 / det;
        double invA01 = -A01 / det;
        double invA10 = -A10 / det;
        double invA11 = A00 / det;

        double lam0 = invA00 * d_bt + invA01 * d_br;
        double lam1 = invA10 * d_bt + invA11 * d_br;

        double dv_x = M[0][0]*lam0 + M[1][0]*lam1;
        double dv_y = M[0][1]*lam0 + M[1][1]*lam1;
        double dv_z = M[0][2]*lam0 + M[1][2]*lam1;

        out_tcm->dv_x = dv_x; out_tcm->dv_y = dv_y; out_tcm->dv_z = dv_z;
        double dv_kms = std::sqrt(dv_x*dv_x + dv_y*dv_y + dv_z*dv_z);
        out_tcm->dv_mag_ms = dv_kms * 1000.0;

        double v_len = std::sqrt(v_tcm_kms[0]*v_tcm_kms[0] + v_tcm_kms[1]*v_tcm_kms[1] + v_tcm_kms[2]*v_tcm_kms[2]);
        if (v_len > 1e-6) {
            double vhat[3] = {v_tcm_kms[0]/v_len, v_tcm_kms[1]/v_len, v_tcm_kms[2]/v_len};
            double nx = r_tcm_km[1]*v_tcm_kms[2] - r_tcm_km[2]*v_tcm_kms[1];
            double ny = r_tcm_km[2]*v_tcm_kms[0] - r_tcm_km[0]*v_tcm_kms[2];
            double nz = r_tcm_km[0]*v_tcm_kms[1] - r_tcm_km[1]*v_tcm_kms[0];
            double n_len = std::sqrt(nx*nx + ny*ny + nz*nz);
            if (n_len < 1e-6) { nx=0; ny=0; nz=1; n_len=1; }
            double nhat[3] = {nx/n_len, ny/n_len, nz/n_len};
            double chat[3] = {vhat[1]*nhat[2] - vhat[2]*nhat[1], vhat[2]*nhat[0] - vhat[0]*nhat[2], vhat[0]*nhat[1] - vhat[1]*nhat[0]};

            out_tcm->prograde_ms = (dv_x*vhat[0] + dv_y*vhat[1] + dv_z*vhat[2]) * 1000.0;
            out_tcm->normal_ms = (dv_x*nhat[0] + dv_y*nhat[1] + dv_z*nhat[2]) * 1000.0;
            out_tcm->radial_ms = (dv_x*chat[0] + dv_y*chat[1] + dv_z*chat[2]) * 1000.0;
        }
        return 0;
    } catch (...) {
        return -99;
    }
}
