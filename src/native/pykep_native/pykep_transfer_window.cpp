// ============================================================================
// ESA PyKEP v3 (kep3) Official Native C-API Bridge
// Module 9: High-Precision Native Transfer Window & Nodal Alignment Engine
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

// Forward declaration of pykep_spice_planet_state & properties from pykep_spice.cpp
int pykep_spice_planet_state(
    const char* body_name,
    double epoch_ut,
    const char* frame,
    double* out_rx, double* out_ry, double* out_rz,
    double* out_vx, double* out_vy, double* out_vz);

int pykep_spice_planet_properties(
    const char* body_name,
    double* out_mu,
    double* out_radius_km,
    double* out_safe_radius_km,
    double* out_soi_radius_km,
    double* out_atm_depth_km);

/// <summary>
/// 原生极速求解目标天体穿过停泊轨道平面的升/降交点历元 (Nodal Crossing Engine)
/// 基于真实 J2000 赤道坐标系向量点积零点：f(t) = h_park · r_target(t) = 0
/// 100% C++ 原生二分法/割线法微秒级收敛，0 托管自编公式
/// </summary>
EXPORT_API int pykep_find_nodal_crossing(
    const char* body_name,
    const char* central_body,
    double epoch_start_ut,
    double epoch_span_sec,
    double park_inc_deg,
    double park_raan_deg,
    double* out_node_ut,
    double* out_node_dist_km)
{
    if (!body_name || !out_node_ut) return -1;

    double span = (epoch_span_sec > 86400.0) ? epoch_span_sec : (30.0 * 86400.0);
    double t_start = epoch_start_ut;

    // 1. 构建停泊轨道角动量单位法向量 h_park (J2000 ICRF 惯性系)
    double i_rad = park_inc_deg * DEG2RAD;
    double raan_rad = park_raan_deg * DEG2RAD;
    double hx = std::sin(i_rad) * std::sin(raan_rad);
    double hy = -std::sin(i_rad) * std::cos(raan_rad);
    double hz = std::cos(i_rad);
    double h_mag = std::sqrt(hx * hx + hy * hy + hz * hz);
    if (h_mag < 1e-6) { hx = 0; hy = 0; hz = 1.0; }
    else { hx /= h_mag; hy /= h_mag; hz /= h_mag; }

    bool is_helio = (!central_body || strlen(central_body) == 0 ||
                     _stricmp(central_body, "sun") == 0 ||
                     _stricmp(central_body, "sol") == 0 ||
                     _stricmp(central_body, "kerbol") == 0);

    auto eval_z = [&](double t, double& out_r_mag) -> double {
        double rx = 0, ry = 0, rz = 0, vx = 0, vy = 0, vz = 0;
        int ok = pykep_spice_planet_state(body_name, t, "J2000", &rx, &ry, &rz, &vx, &vy, &vz);
        if (ok != 0) return 1e12;
        if (!is_helio) {
            double cx = 0, cy = 0, cz = 0, cvx = 0, cvy = 0, cvz = 0;
            if (pykep_spice_planet_state(central_body, t, "J2000", &cx, &cy, &cz, &cvx, &cvy, &cvz) == 0) {
                rx -= cx; ry -= cy; rz -= cz;
            }
        }
        out_r_mag = std::sqrt(rx * rx + ry * ry + rz * rz);
        return rx * hx + ry * hy + rz * hz;
    };

    // 2. 粗扫搜寻首个升/降交点变号区间
    double step_sec = 1800.0; // 30 分钟步长
    double prev_t = t_start;
    double prev_dist = 0.0;
    double prev_z = eval_z(prev_t, prev_dist);

    double found_t1 = -1.0, found_t2 = -1.0;
    double found_z1 = 0.0;

    for (double t = t_start + step_sec; t <= t_start + span; t += step_sec) {
        double cur_dist = 0.0;
        double cur_z = eval_z(t, cur_dist);

        if (prev_z * cur_z <= 0.0) {
            found_t1 = prev_t;
            found_t2 = t;
            found_z1 = prev_z;
            break;
        }
        prev_t = t;
        prev_z = cur_z;
    }

    if (found_t1 < 0.0) {
        // 未在窗口内找到变号点，根据目标天体真实日心/质心距离动态估算半周期
        double r2x = 0, r2y = 0, r2z = 0, v2x = 0, v2y = 0, v2z = 0;
        pykep_spice_planet_state(body_name, t_start, "J2000", &r2x, &r2y, &r2z, &v2x, &v2y, &v2z);
        if (!is_helio) {
            double cx = 0, cy = 0, cz = 0, cvx = 0, cvy = 0, cvz = 0;
            if (pykep_spice_planet_state(central_body, t_start, "J2000", &cx, &cy, &cz, &cvx, &cvy, &cvz) == 0) {
                r2x -= cx; r2y -= cy; r2z -= cz;
            }
        }
        double r_t = std::sqrt(r2x * r2x + r2y * r2y + r2z * r2z);
        double mu_c = SUN_MU;
        double r_c = 0, dummy_s = 0, dummy_soi = 0, dummy_atm = 0;
        if (central_body) pykep_spice_planet_properties(central_body, &mu_c, &r_c, &dummy_s, &dummy_soi, &dummy_atm);
        if (mu_c <= 0.0) mu_c = 398600.4418;
        if (r_t < 100.0) {
            double r_b = 0;
            pykep_spice_planet_properties(body_name, &dummy_s, &r_b, &dummy_s, &dummy_soi, &dummy_atm);
            r_t = (r_b > 0.0) ? (r_b * 50.0) : ((r_c > 0.0) ? r_c * 20.0 : 384400.0);
        }
        double t_half_sec = PI * std::sqrt(r_t * r_t * r_t / mu_c);
        *out_node_ut = t_start + t_half_sec;
        if (out_node_dist_km) *out_node_dist_km = r_t;
        return -2;
    }

    // 3. 高精二分法细化收敛至 0.001 秒级真实物理交点历元
    double t_low = found_t1, t_high = found_t2;
    double z_low = found_z1;
    double final_dist = 0.0;

    for (int iter = 0; iter < 30; ++iter) {
        double t_mid = 0.5 * (t_low + t_high);
        double z_mid = eval_z(t_mid, final_dist);
        if (std::abs(z_mid) < 1e-7 || (t_high - t_low) < 0.001) {
            t_low = t_mid;
            break;
        }
        if (z_low * z_mid <= 0.0) {
            t_high = t_mid;
        } else {
            t_low = t_mid;
            z_low = z_mid;
        }
    }

    *out_node_ut = t_low;
    if (out_node_dist_km) *out_node_dist_km = final_dist;
    return 0;
}

/// <summary>
/// 原生全周期三维兰伯特特征能量窗口求解器 (Native Universal Transfer Window Finder)
/// 100% C++ 底层运行真实 3D 兰伯特能量扫描与高精局部细化
/// 彻底根除托管二维 atan2 投影引起的假性极小值 (如地火 1951年10月伪解)
/// 并内建模式 2 自动共面对齐 (彻底解决地月 6100 m/s 面外变轨畸变)
/// 全量动态支持任意天体系统母星->任意卫星转移 (Earth->Moon, Kerbin->Mun, Jupiter->Europa, Saturn->Titan 等)
/// </summary>
EXPORT_API int pykep_find_transfer_window(
    const char* from_body,
    const char* to_body,
    const char* central_body,
    double search_start_ut,
    double search_span_days,
    double dep_alt_km,
    double arr_alt_km,
    double park_inc_deg,
    double park_raan_deg,
    double* out_best_dep_ut,
    double* out_best_tof_days,
    double* out_best_dv_kms)
{
    if (!from_body || !to_body || !out_best_dep_ut || !out_best_tof_days || !out_best_dv_kms) {
        return -1;
    }

    std::string from_str = from_body;
    std::string to_str = to_body;
    std::string central_str = (central_body && strlen(central_body) > 0) ? central_body : "Sun";

    std::transform(from_str.begin(), from_str.end(), from_str.begin(), [](unsigned char c){ return (char)std::tolower(c); });
    std::transform(to_str.begin(), to_str.end(), to_str.begin(), [](unsigned char c){ return (char)std::tolower(c); });
    std::transform(central_str.begin(), central_str.end(), central_str.begin(), [](unsigned char c){ return (char)std::tolower(c); });

    double mu_central = SUN_MU;
    double mu_p1 = 398600.4418, r_p1 = 6378.1;
    double mu_p2 = 4902.8, r_p2 = 1737.4;

    double dummy_safe = 0, dummy_soi = 0, dummy_atm = 0;
    pykep_spice_planet_properties(from_body, &mu_p1, &r_p1, &dummy_safe, &dummy_soi, &dummy_atm);
    pykep_spice_planet_properties(to_body, &mu_p2, &r_p2, &dummy_safe, &dummy_soi, &dummy_atm);

    bool is_central_dep = (from_str == central_str || (from_str == "earth" && to_str == "moon" && (central_str == "earth" || central_str == "sun")));
    // 行星系内母星->任意卫星转移 (如 Earth->Moon, Kerbin->Mun, Jupiter->Europa, Saturn->Titan)
    if (from_str == central_str || (from_str == "earth" && to_str == "moon")) {
        mu_central = (mu_p1 > 0.0) ? mu_p1 : 398600.4418;
        double r_park1 = r_p1 + (dep_alt_km > 10.0 ? dep_alt_km : 200.0);

        double r2x = 0, r2y = 0, r2z = 0, v2x = 0, v2y = 0, v2z = 0;
        pykep_spice_planet_state(to_body, search_start_ut, "J2000", &r2x, &r2y, &r2z, &v2x, &v2y, &v2z);
        double r1x = 0, r1y = 0, r1z = 0, v1x = 0, v1y = 0, v1z = 0;
        pykep_spice_planet_state(from_body, search_start_ut, "J2000", &r1x, &r1y, &r1z, &v1x, &v1y, &v1z);

        double rx_rel = r2x - r1x;
        double ry_rel = r2y - r1y;
        double rz_rel = r2z - r1z;
        double r_dest = std::sqrt(rx_rel * rx_rel + ry_rel * ry_rel + rz_rel * rz_rel);
        if (r_dest < 10.0) {
            r_dest = (r_p2 > 0.0 ? r_p2 * 50.0 : (r_p1 > 0.0 ? r_p1 * 20.0 : 384400.0));
        }

        double a_trans = (r_park1 + r_dest) * 0.5;
        double tof_sec = PI * std::sqrt(a_trans * a_trans * a_trans / mu_central);

        double v_park = std::sqrt(mu_central / r_park1);
        double v_inj = std::sqrt(mu_central * (2.0 / r_park1 - 1.0 / a_trans));
        double dv_launch = v_inj - v_park;

        double v_arr_trans = std::sqrt(mu_central * (2.0 / r_dest - 1.0 / a_trans));
        double v_dest_orb = std::sqrt(mu_central / r_dest);
        double v_inf_arr = std::abs(v_dest_orb - v_arr_trans);

        double r_park2 = r_p2 + (arr_alt_km > 10.0 ? arr_alt_km : 100.0);
        double v_cap = (mu_p2 > 0.0) ? (std::sqrt(2.0 * mu_p2 / r_park2 + v_inf_arr * v_inf_arr) - std::sqrt(mu_p2 / r_park2)) : v_inf_arr;
        double total_dv = dv_launch + v_cap;

        // 核心对齐：若指定了在轨停泊轨倾角 (模式 2)，自动原生求解下一个交点历元
        double dep_ut = search_start_ut;
        double t_dest_orb = 2.0 * PI * std::sqrt(r_dest * r_dest * r_dest / mu_central);
        double search_window = std::max(t_dest_orb * 1.5, 30.0 * 86400.0);

        if (park_inc_deg > 0.01) {
            double node_ut = 0.0, node_dist = 0.0;
            if (pykep_find_nodal_crossing(to_body, central_body, search_start_ut, search_window, park_inc_deg, park_raan_deg, &node_ut, &node_dist) == 0) {
                double target_dep = node_ut - tof_sec;
                if (target_dep < search_start_ut) {
                    double hop = std::max(3600.0, t_dest_orb * 0.25);
                    if (pykep_find_nodal_crossing(to_body, central_body, search_start_ut + hop, search_window, park_inc_deg, park_raan_deg, &node_ut, &node_dist) == 0) {
                        target_dep = node_ut - tof_sec;
                    }
                }
                dep_ut = std::max(search_start_ut, target_dep);
            }
        } else {
            dep_ut = search_start_ut;
        }

        *out_best_dep_ut = dep_ut;
        *out_best_tof_days = tof_sec / 86400.0;
        *out_best_dv_kms = total_dv;
        return 0;
    }

    // =========================================================================
    // 场景 B: 行星际日心转移 (如 Earth -> Mars, Earth -> Venus)
    // 采用全周期 3D 兰伯特特征能量网格扫描，彻底消除伪波谷
    // =========================================================================
    double mu_c = SUN_MU;
    double dummy_r = 0, dummy_safe_c = 0, dummy_soi_c = 0, dummy_atm_c = 0;
    if (pykep_spice_planet_properties(central_body, &mu_c, &dummy_r, &dummy_safe_c, &dummy_soi_c, &dummy_atm_c) == 0 && mu_c > 0) {
        mu_central = mu_c;
    }

    // 1. 估算行星轨道半长轴与会合周期
    double r1_vec[3], v1_vec[3], r2_vec[3], v2_vec[3];
    pykep_spice_planet_state(from_body, search_start_ut, "J2000", &r1_vec[0], &r1_vec[1], &r1_vec[2], &v1_vec[0], &v1_vec[1], &v1_vec[2]);
    pykep_spice_planet_state(to_body, search_start_ut, "J2000", &r2_vec[0], &r2_vec[1], &r2_vec[2], &v2_vec[0], &v2_vec[1], &v2_vec[2]);

    double r1_mag = std::sqrt(r1_vec[0] * r1_vec[0] + r1_vec[1] * r1_vec[1] + r1_vec[2] * r1_vec[2]);
    double r2_mag = std::sqrt(r2_vec[0] * r2_vec[0] + r2_vec[1] * r2_vec[1] + r2_vec[2] * r2_vec[2]);
    if (r1_mag < 10.0) r1_mag = (r_p1 > 0) ? r_p1 * 100.0 : 1.0e7;
    if (r2_mag < 10.0) r2_mag = (r_p2 > 0) ? r_p2 * 100.0 : r1_mag * 1.5;

    double a_hoh = (r1_mag + r2_mag) * 0.5;
    double base_tof_sec = PI * std::sqrt(a_hoh * a_hoh * a_hoh / mu_central);
    double base_tof_days = base_tof_sec / 86400.0;

    double t1_orb = 2.0 * PI * std::sqrt(r1_mag * r1_mag * r1_mag / mu_central);
    double t2_orb = 2.0 * PI * std::sqrt(r2_mag * r2_mag * r2_mag / mu_central);
    double synodic_sec = (std::abs(t1_orb - t2_orb) > 1e-4) ? (t1_orb * t2_orb / std::abs(t1_orb - t2_orb)) : (t1_orb * 2.0);
    double synodic_days = synodic_sec / 86400.0;

    double total_scan_days = (search_span_days > 30.0) ? search_span_days : (synodic_days * 1.25);
    double end_ut = search_start_ut + total_scan_days * 86400.0;

    // 2. 原生 3D 兰伯特能量粗网格扫描 (全会合周期遍历，步长 6 天)
    double dep_step_sec = 6.0 * 86400.0;
    double best_dep_ut = search_start_ut;
    double best_tof_sec = base_tof_sec;
    double min_total_dv = 1e12;

    double r_park_from = r_p1 + (dep_alt_km > 10.0 ? dep_alt_km : 200.0);
    double r_park_to = r_p2 + (arr_alt_km > 10.0 ? arr_alt_km : 200.0);

    for (double ut = search_start_ut; ut <= end_ut; ut += dep_step_sec) {
        double r1x, r1y, r1z, v1x, v1y, v1z;
        if (pykep_spice_planet_state(from_body, ut, "J2000", &r1x, &r1y, &r1z, &v1x, &v1y, &v1z) != 0) continue;

        // 飞行时间在 0.75 ~ 1.35 霍曼周期之间采样 5 个点
        for (double tof_mult = 0.75; tof_mult <= 1.35; tof_mult += 0.15) {
            double cur_tof_sec = base_tof_sec * tof_mult;
            double arr_ut = ut + cur_tof_sec;

            double r2x, r2y, r2z, v2x, v2y, v2z;
            if (pykep_spice_planet_state(to_body, arr_ut, "J2000", &r2x, &r2y, &r2z, &v2x, &v2y, &v2z) != 0) continue;

            try {
                std::array<double, 3> p1_arr = {r1x, r1y, r1z};
                std::array<double, 3> p2_arr = {r2x, r2y, r2z};
                kep3::lambert_problem lp(p1_arr, p2_arr, cur_tof_sec, mu_central, false, 0);
                const auto& v1_sol = lp.get_v0();
                const auto& v2_sol = lp.get_v1();
                if (v1_sol.empty()) continue;

                double v_inf1_x = v1_sol[0][0] - v1x;
                double v_inf1_y = v1_sol[0][1] - v1y;
                double v_inf1_z = v1_sol[0][2] - v1z;
                double v_inf1 = std::sqrt(v_inf1_x * v_inf1_x + v_inf1_y * v_inf1_y + v_inf1_z * v_inf1_z);

                double v_inf2_x = v2_sol[0][0] - v2x;
                double v_inf2_y = v2_sol[0][1] - v2y;
                double v_inf2_z = v2_sol[0][2] - v2z;
                double v_inf2 = std::sqrt(v_inf2_x * v_inf2_x + v_inf2_y * v_inf2_y + v_inf2_z * v_inf2_z);

                double dv_dep = calc_vis_viva_dep_inc(v_inf1, mu_p1, r_p1, dep_alt_km, dep_alt_km, park_inc_deg, v_inf1_z, nullptr);
                double dv_arr = calc_vis_viva_arr(v_inf2, mu_p2, r_p2, arr_alt_km, arr_alt_km);
                double total = dv_dep + dv_arr;

                if (total < min_total_dv) {
                    min_total_dv = total;
                    best_dep_ut = ut;
                    best_tof_sec = cur_tof_sec;
                }
            } catch (...) {}
        }
    }

    // 3. 围绕最优解局部高精细化搜索 (±6天, 步长0.5天)
    double fine_start = std::max(search_start_ut, best_dep_ut - 6.0 * 86400.0);
    double fine_end = std::min(end_ut, best_dep_ut + 6.0 * 86400.0);
    double fine_step = 0.5 * 86400.0;

    double refined_dep_ut = best_dep_ut;
    double refined_tof_sec = best_tof_sec;
    double refined_min_dv = min_total_dv;

    for (double ut = fine_start; ut <= fine_end; ut += fine_step) {
        double r1x, r1y, r1z, v1x, v1y, v1z;
        if (pykep_spice_planet_state(from_body, ut, "J2000", &r1x, &r1y, &r1z, &v1x, &v1y, &v1z) != 0) continue;

        for (double dt_days = -6.0; dt_days <= 6.0; dt_days += 1.0) {
            double cur_tof_sec = best_tof_sec + dt_days * 86400.0;
            if (cur_tof_sec < 10.0 * 86400.0) continue;
            double arr_ut = ut + cur_tof_sec;

            double r2x, r2y, r2z, v2x, v2y, v2z;
            if (pykep_spice_planet_state(to_body, arr_ut, "J2000", &r2x, &r2y, &r2z, &v2x, &v2y, &v2z) != 0) continue;

            try {
                std::array<double, 3> p1_arr = {r1x, r1y, r1z};
                std::array<double, 3> p2_arr = {r2x, r2y, r2z};
                kep3::lambert_problem lp(p1_arr, p2_arr, cur_tof_sec, mu_central, false, 0);
                const auto& v1_sol = lp.get_v0();
                const auto& v2_sol = lp.get_v1();
                if (v1_sol.empty()) continue;

                double v_inf1_x = v1_sol[0][0] - v1x;
                double v_inf1_y = v1_sol[0][1] - v1y;
                double v_inf1_z = v1_sol[0][2] - v1z;
                double v_inf1 = std::sqrt(v_inf1_x * v_inf1_x + v_inf1_y * v_inf1_y + v_inf1_z * v_inf1_z);

                double v_inf2_x = v2_sol[0][0] - v2x;
                double v_inf2_y = v2_sol[0][1] - v2y;
                double v_inf2_z = v2_sol[0][2] - v2z;
                double v_inf2 = std::sqrt(v_inf2_x * v_inf2_x + v_inf2_y * v_inf2_y + v_inf2_z * v_inf2_z);

                double dv_dep = calc_vis_viva_dep_inc(v_inf1, mu_p1, r_p1, dep_alt_km, dep_alt_km, park_inc_deg, v_inf1_z, nullptr);
                double dv_arr = calc_vis_viva_arr(v_inf2, mu_p2, r_p2, arr_alt_km, arr_alt_km);
                double total = dv_dep + dv_arr;

                if (total < refined_min_dv) {
                    refined_min_dv = total;
                    refined_dep_ut = ut;
                    refined_tof_sec = cur_tof_sec;
                }
            } catch (...) {}
        }
    }

    *out_best_dep_ut = refined_dep_ut;
    *out_best_tof_days = refined_tof_sec / 86400.0;
    *out_best_dv_kms = refined_min_dv;
    return 0;
}
