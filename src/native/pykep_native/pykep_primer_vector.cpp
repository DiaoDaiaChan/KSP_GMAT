// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 4: Lawden's Primer Vector Theory (Impulsive Optimality Evaluator)
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

// Forward declaration from Module 3 (pykep_stm_bplane.cpp)
EXPORT_API int pykep_calc_kepler_stm(
    const double* r0_km, const double* v0_kms, double dt_sec, double mu,
    double* out_stm_6x6);

EXPORT_API int pykep_calc_primer_vector(
    const double* r1_km, const double* v1_kms,
    const double* dv1_kms, const double* dv2_kms,
    double tof_sec, double mu,
    NativePrimerVectorResult* out_pv)
{
    if (!r1_km || !v1_kms || !dv1_kms || !dv2_kms || !out_pv || tof_sec <= 0 || mu <= 0) return -1;

    try {
        std::memset(out_pv, 0, sizeof(NativePrimerVectorResult));

        double dv1_len = std::sqrt(dv1_kms[0]*dv1_kms[0] + dv1_kms[1]*dv1_kms[1] + dv1_kms[2]*dv1_kms[2]);
        double dv2_len = std::sqrt(dv2_kms[0]*dv2_kms[0] + dv2_kms[1]*dv2_kms[1] + dv2_kms[2]*dv2_kms[2]);
        if (dv1_len < 1e-6 || dv2_len < 1e-6) return -2;

        double u1[3] = {dv1_kms[0]/dv1_len, dv1_kms[1]/dv1_len, dv1_kms[2]/dv1_len};
        double u2[3] = {dv2_kms[0]/dv2_len, dv2_kms[1]/dv2_len, dv2_kms[2]/dv2_len};

        double M_ji[36];
        pykep_calc_kepler_stm(r1_km, v1_kms, tof_sec, mu, M_ji);

        double B[3][3] = {
            {M_ji[0*6 + 3], M_ji[0*6 + 4], M_ji[0*6 + 5]},
            {M_ji[1*6 + 3], M_ji[1*6 + 4], M_ji[1*6 + 5]},
            {M_ji[2*6 + 3], M_ji[2*6 + 4], M_ji[2*6 + 5]}
        };

        double detB = B[0][0]*(B[1][1]*B[2][2] - B[1][2]*B[2][1])
                    - B[0][1]*(B[1][0]*B[2][2] - B[1][2]*B[2][0])
                    + B[0][2]*(B[1][0]*B[2][1] - B[1][1]*B[2][0]);
        if (std::abs(detB) < 1e-12) return -3;

        double invB[3][3];
        invB[0][0] = (B[1][1]*B[2][2] - B[1][2]*B[2][1]) / detB;
        invB[0][1] = (B[0][2]*B[2][1] - B[0][1]*B[2][2]) / detB;
        invB[0][2] = (B[0][1]*B[1][2] - B[0][2]*B[1][1]) / detB;
        invB[1][0] = (B[1][2]*B[2][0] - B[1][0]*B[2][2]) / detB;
        invB[1][1] = (B[0][0]*B[2][2] - B[0][2]*B[2][0]) / detB;
        invB[1][2] = (B[0][2]*B[1][0] - B[0][0]*B[1][2]) / detB;
        invB[2][0] = (B[1][0]*B[2][1] - B[1][1]*B[2][0]) / detB;
        invB[2][1] = (B[0][1]*B[2][0] - B[0][0]*B[2][1]) / detB;
        invB[2][2] = (B[0][0]*B[1][1] - B[0][1]*B[1][0]) / detB;

        int n_samples = 21;
        out_pv->sample_count = n_samples;
        double max_p = 0.0;
        double max_t = 0.0;

        for (int s = 0; s < n_samples; ++s) {
            double tau = (double)s / (double)(n_samples - 1);
            double t_k = tau * tof_sec;
            out_pv->times_sec[s] = t_k;

            if (s == 0 || s == n_samples - 1) {
                out_pv->p_mags[s] = 1.0;
                continue;
            }

            kep_toolbox::array3D r_k = {r1_km[0], r1_km[1], r1_km[2]};
            kep_toolbox::array3D v_k = {v1_kms[0], v1_kms[1], v1_kms[2]};
            kep_toolbox::propagate_lagrangian(r_k, v_k, t_k, mu);

            double dt_rem = tof_sec - t_k;
            double M_jk[36];
            double r_k_arr[3] = {r_k[0], r_k[1], r_k[2]};
            double v_k_arr[3] = {v_k[0], v_k[1], v_k[2]};
            pykep_calc_kepler_stm(r_k_arr, v_k_arr, dt_rem, mu, M_jk);

            double M_jk_rv[3][3] = {
                {M_jk[0*6 + 3], M_jk[0*6 + 4], M_jk[0*6 + 5]},
                {M_jk[1*6 + 3], M_jk[1*6 + 4], M_jk[1*6 + 5]},
                {M_jk[2*6 + 3], M_jk[2*6 + 4], M_jk[2*6 + 5]}
            };
            double Aik[3][3] = {{0}};
            for (int i = 0; i < 3; ++i) {
                for (int j = 0; j < 3; ++j) {
                    double sum = 0;
                    for (int m = 0; m < 3; ++m) sum += invB[i][m] * M_jk_rv[m][j];
                    Aik[i][j] = -sum;
                }
            }

            double M_ji_vv[3][3] = {
                {M_ji[3*6 + 3], M_ji[3*6 + 4], M_ji[3*6 + 5]},
                {M_ji[4*6 + 3], M_ji[4*6 + 4], M_ji[4*6 + 5]},
                {M_ji[5*6 + 3], M_ji[5*6 + 4], M_ji[5*6 + 5]}
            };
            double M_jk_vv[3][3] = {
                {M_jk[3*6 + 3], M_jk[3*6 + 4], M_jk[3*6 + 5]},
                {M_jk[4*6 + 3], M_jk[4*6 + 4], M_jk[4*6 + 5]},
                {M_jk[5*6 + 3], M_jk[5*6 + 4], M_jk[5*6 + 5]}
            };
            double Ajk[3][3] = {{0}};
            for (int i = 0; i < 3; ++i) {
                for (int j = 0; j < 3; ++j) {
                    double sum = 0;
                    for (int m = 0; m < 3; ++m) sum += M_ji_vv[i][m] * Aik[m][j];
                    Ajk[i][j] = -(sum + M_jk_vv[i][j]);
                }
            }

            double p[3] = {0, 0, 0};
            for (int i = 0; i < 3; ++i) {
                double p1 = 0, p2 = 0;
                for (int m = 0; m < 3; ++m) {
                    p1 += Aik[m][i] * u1[m];
                    p2 += Ajk[m][i] * u2[m];
                }
                p[i] = -p1 - p2;
            }

            double p_mag = std::sqrt(p[0]*p[0] + p[1]*p[1] + p[2]*p[2]);
            out_pv->p_mags[s] = p_mag;
            if (p_mag > max_p) {
                max_p = p_mag;
                max_t = t_k;
            }
        }

        out_pv->max_p_mag = max_p;
        out_pv->max_p_time_sec = max_t;
        out_pv->is_optimal = (max_p <= 1.0001) ? 1 : 0;
        return 0;
    } catch (...) {
        return -99;
    }
}
