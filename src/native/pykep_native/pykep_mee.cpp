// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 5: Modified Equinoctial Elements (MEE) Non-Singular Transformations
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

EXPORT_API int pykep_cartesian_to_mee(const double* r_km, const double* v_kms, double mu, NativeMEE* out_mee) {
    if (!r_km || !v_kms || !out_mee || mu <= 0) return -1;

    try {
        double r_len = std::sqrt(r_km[0]*r_km[0] + r_km[1]*r_km[1] + r_km[2]*r_km[2]);
        double v_len = std::sqrt(v_kms[0]*v_kms[0] + v_kms[1]*v_kms[1] + v_kms[2]*v_kms[2]);
        if (r_len < 1e-6) return -2;

        double hx = r_km[1]*v_kms[2] - r_km[2]*v_kms[1];
        double hy = r_km[2]*v_kms[0] - r_km[0]*v_kms[2];
        double hz = r_km[0]*v_kms[1] - r_km[1]*v_kms[0];
        double h_len = std::sqrt(hx*hx + hy*hy + hz*hz);
        if (h_len < 1e-9) return -3;

        double p = (h_len * h_len) / mu;

        double vxh_x = v_kms[1]*hz - v_kms[2]*hy;
        double vxh_y = v_kms[2]*hx - v_kms[0]*hz;
        double vxh_z = v_kms[0]*hy - v_kms[1]*hx;
        double ex = vxh_x / mu - r_km[0]/r_len;
        double ey = vxh_y / mu - r_km[1]/r_len;
        double ez = vxh_z / mu - r_km[2]/r_len;

        double hz_u = hz / h_len;
        double tan_i_half = std::sqrt(std::max(0.0, (1.0 - hz_u) / (1.0 + hz_u)));
        double node_len = std::sqrt(hx*hx + hy*hy);
        double Omega = (node_len > 1e-9) ? std::atan2(hx, -hy) : 0.0;
        if (Omega < 0) Omega += 2*PI;

        out_mee->p = p;
        out_mee->h = tan_i_half * std::cos(Omega);
        out_mee->k = tan_i_half * std::sin(Omega);

        double L = std::atan2(r_km[1], r_km[0]);
        if (L < 0) L += 2*PI;
        out_mee->L = L;

        double den = 1.0 + out_mee->h*out_mee->h + out_mee->k*out_mee->k;
        double f_hat[3] = {(1.0 - out_mee->h*out_mee->h + out_mee->k*out_mee->k)/den, (2.0*out_mee->h*out_mee->k)/den, (-2.0*out_mee->h)/den};
        double g_hat[3] = {(2.0*out_mee->h*out_mee->k)/den, (1.0 + out_mee->h*out_mee->h - out_mee->k*out_mee->k)/den, (2.0*out_mee->k)/den};

        out_mee->f = ex*f_hat[0] + ey*f_hat[1] + ez*f_hat[2];
        out_mee->g = ex*g_hat[0] + ey*g_hat[1] + ez*g_hat[2];
        return 0;
    } catch (...) {
        return -99;
    }
}

EXPORT_API int pykep_mee_to_cartesian(const NativeMEE* mee, double mu, double* out_r_km, double* out_v_kms) {
    if (!mee || !out_r_km || !out_v_kms || mu <= 0 || mee->p <= 0) return -1;

    try {
        double den = 1.0 + mee->h*mee->h + mee->k*mee->k;
        double f_hat[3] = {(1.0 - mee->h*mee->h + mee->k*mee->k)/den, (2.0*mee->h*mee->k)/den, (-2.0*mee->h)/den};
        double g_hat[3] = {(2.0*mee->h*mee->k)/den, (1.0 + mee->h*mee->h - mee->k*mee->k)/den, (2.0*mee->k)/den};

        double cosL = std::cos(mee->L);
        double sinL = std::sin(mee->L);
        double q = 1.0 + mee->f*cosL + mee->g*sinL;
        double r = mee->p / q;

        out_r_km[0] = r * (cosL * f_hat[0] + sinL * g_hat[0]);
        out_r_km[1] = r * (cosL * f_hat[1] + sinL * g_hat[1]);
        out_r_km[2] = r * (cosL * f_hat[2] + sinL * g_hat[2]);

        double sqrt_mu_p = std::sqrt(mu / mee->p);
        for (int i = 0; i < 3; ++i) {
            out_v_kms[i] = -sqrt_mu_p * (sinL + mee->g) * f_hat[i] + sqrt_mu_p * (cosL + mee->f) * g_hat[i];
        }
        return 0;
    } catch (...) {
        return -99;
    }
}
