// ============================================================================
// ESA PyKEP v3 (kep3) Official Native C-API Bridge
// Module 5: Modified Equinoctial Elements (MEE) Non-Singular Transformations
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

EXPORT_API int kep3_cartesian_to_mee(const double* r_km, const double* v_kms, double mu, NativeMEE* out_mee) {
    if (!r_km || !v_kms || !out_mee || mu <= 0) return -1;

    try {
        std::array<std::array<double, 3>, 2> pos_vel = {{
            {r_km[0], r_km[1], r_km[2]},
            {v_kms[0], v_kms[1], v_kms[2]}
        }};

        // 官方原生 kep3::ic2mee 无奇点转换
        std::array<double, 6> mee = kep3::ic2mee(pos_vel, mu, false);
        out_mee->p = mee[0];
        out_mee->f = mee[1];
        out_mee->g = mee[2];
        out_mee->h = mee[3];
        out_mee->k = mee[4];
        out_mee->L = mee[5];
        return 0;
    } catch (...) {
        return -99;
    }
}

EXPORT_API int kep3_mee_to_cartesian(const NativeMEE* mee, double mu, double* out_r_km, double* out_v_kms) {
    if (!mee || !out_r_km || !out_v_kms || mu <= 0 || mee->p <= 0) return -1;

    try {
        std::array<double, 6> mee_arr = {mee->p, mee->f, mee->g, mee->h, mee->k, mee->L};

        // 官方原生 kep3::mee2ic 无奇点反算
        auto pos_vel = kep3::mee2ic(mee_arr, mu, false);
        out_r_km[0] = pos_vel[0][0];
        out_r_km[1] = pos_vel[0][1];
        out_r_km[2] = pos_vel[0][2];

        out_v_kms[0] = pos_vel[1][0];
        out_v_kms[1] = pos_vel[1][1];
        out_v_kms[2] = pos_vel[1][2];
        return 0;
    } catch (...) {
        return -99;
    }
}

// 保持向下兼容 pykep_ 前缀接口
EXPORT_API int pykep_cartesian_to_mee(const double* r_km, const double* v_kms, double mu, NativeMEE* out_mee) {
    return kep3_cartesian_to_mee(r_km, v_kms, mu, out_mee);
}

EXPORT_API int pykep_mee_to_cartesian(const NativeMEE* mee, double mu, double* out_r_km, double* out_v_kms) {
    return kep3_mee_to_cartesian(mee, mu, out_r_km, out_v_kms);
}
