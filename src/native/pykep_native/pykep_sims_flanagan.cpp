// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 6: Sims-Flanagan Continuous Low-Thrust Evaluator
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

EXPORT_API int pykep_sims_flanagan_evaluate(
    const double* r1_km, const double* v1_kms, double m0_kg,
    const double* r2_km, const double* v2_kms, double mf_guess_kg,
    double tof_sec, double max_thrust_N, double isp_sec, double mu,
    int n_seg, const double* throttles_3n,
    NativeSimsFlanaganResult* out_sf)
{
    if (!r1_km || !v1_kms || !r2_km || !v2_kms || !out_sf || tof_sec <= 0 || mu <= 0 || n_seg <= 0 || n_seg > 32) return -1;

    try {
        std::memset(out_sf, 0, sizeof(NativeSimsFlanaganResult));
        out_sf->n_segments = n_seg;

        int n_fwd = n_seg / 2;
        int n_bck = n_seg - n_fwd;
        double dt = tof_sec / (double)n_seg;
        double veff_kms = (isp_sec * 9.80665) / 1000.0;
        double c = (max_thrust_N / 1000.0) * dt;

        std::array<double, 3> r_fwd = {r1_km[0], r1_km[1], r1_km[2]};
        std::array<double, 3> v_fwd = {v1_kms[0], v1_kms[1], v1_kms[2]};
        double m_fwd = m0_kg;
        double tot_dv = 0.0;

        for (int i = 0; i < n_fwd; ++i) {
            pykep_propagate_lagrangian(r_fwd, v_fwd, dt * 0.5, mu);

            out_sf->segment_pos[i][0] = r_fwd[0];
            out_sf->segment_pos[i][1] = r_fwd[1];
            out_sf->segment_pos[i][2] = r_fwd[2];

            double u[3] = {0, 0, 0};
            if (throttles_3n != nullptr) {
                u[0] = std::max(-1.0, std::min(1.0, throttles_3n[3*i + 0]));
                u[1] = std::max(-1.0, std::min(1.0, throttles_3n[3*i + 1]));
                u[2] = std::max(-1.0, std::min(1.0, throttles_3n[3*i + 2]));
            }
            out_sf->throttle[i][0] = u[0]; out_sf->throttle[i][1] = u[1]; out_sf->throttle[i][2] = u[2];

            double dv[3] = {c / m_fwd * u[0], c / m_fwd * u[1], c / m_fwd * u[2]};
            double dv_mag = std::sqrt(dv[0]*dv[0] + dv[1]*dv[1] + dv[2]*dv[2]);

            v_fwd[0] += dv[0]; v_fwd[1] += dv[1]; v_fwd[2] += dv[2];
            m_fwd *= std::exp(-dv_mag / std::max(1e-6, veff_kms));
            tot_dv += dv_mag * 1000.0;

            pykep_propagate_lagrangian(r_fwd, v_fwd, dt * 0.5, mu);
        }

        std::array<double, 3> r_bck = {r2_km[0], r2_km[1], r2_km[2]};
        std::array<double, 3> v_bck = {v2_kms[0], v2_kms[1], v2_kms[2]};
        double m_bck = (mf_guess_kg > 0) ? mf_guess_kg : m0_kg * 0.8;

        for (int i = n_seg - 1; i >= n_fwd; --i) {
            pykep_propagate_lagrangian(r_bck, v_bck, -dt * 0.5, mu);

            out_sf->segment_pos[i][0] = r_bck[0];
            out_sf->segment_pos[i][1] = r_bck[1];
            out_sf->segment_pos[i][2] = r_bck[2];

            double u[3] = {0, 0, 0};
            if (throttles_3n != nullptr) {
                u[0] = std::max(-1.0, std::min(1.0, throttles_3n[3*i + 0]));
                u[1] = std::max(-1.0, std::min(1.0, throttles_3n[3*i + 1]));
                u[2] = std::max(-1.0, std::min(1.0, throttles_3n[3*i + 2]));
            }
            out_sf->throttle[i][0] = u[0]; out_sf->throttle[i][1] = u[1]; out_sf->throttle[i][2] = u[2];

            double dv[3] = {c / m_bck * u[0], c / m_bck * u[1], c / m_bck * u[2]};
            double dv_mag = std::sqrt(dv[0]*dv[0] + dv[1]*dv[1] + dv[2]*dv[2]);

            v_bck[0] -= dv[0]; v_bck[1] -= dv[1]; v_bck[2] -= dv[2];
            m_bck *= std::exp(dv_mag / std::max(1e-6, veff_kms));
            tot_dv += dv_mag * 1000.0;

            pykep_propagate_lagrangian(r_bck, v_bck, -dt * 0.5, mu);
        }

        out_sf->delta_r_km = std::sqrt((r_fwd[0]-r_bck[0])*(r_fwd[0]-r_bck[0]) + (r_fwd[1]-r_bck[1])*(r_fwd[1]-r_bck[1]) + (r_fwd[2]-r_bck[2])*(r_fwd[2]-r_bck[2]));
        out_sf->delta_v_kms = std::sqrt((v_fwd[0]-v_bck[0])*(v_fwd[0]-v_bck[0]) + (v_fwd[1]-v_bck[1])*(v_fwd[1]-v_bck[1]) + (v_fwd[2]-v_bck[2])*(v_fwd[2]-v_bck[2]));
        out_sf->delta_m_kg = std::abs(m_fwd - m_bck);
        out_sf->total_dv_ms = tot_dv;
        out_sf->final_mass_kg = m_fwd;
        return 0;
    } catch (...) {
        return -99;
    }
}
