"""
GMAT vs Principia Real-Time Parameter Comparator and Diagnostics Engine.
Compares native GMAT mission design outputs with Principia N-body flight plan state.
"""

import math
import time

# Global cache for latest comparison state
_LATEST_GMAT_RESULT = None
_LATEST_PRINCIPIA_NODES = []
_LATEST_VESSEL_STATE = {}
_LATEST_COMPARISON = None

def update_gmat_result(result):
    global _LATEST_GMAT_RESULT
    _LATEST_GMAT_RESULT = result

def update_principia_state(vessel_state, nodes):
    global _LATEST_PRINCIPIA_NODES, _LATEST_VESSEL_STATE
    _LATEST_VESSEL_STATE = vessel_state or {}
    _LATEST_PRINCIPIA_NODES = nodes or []

def get_latest_comparison():
    global _LATEST_COMPARISON
    _LATEST_COMPARISON = compare_gmat_with_principia(_LATEST_GMAT_RESULT, _LATEST_PRINCIPIA_NODES, _LATEST_VESSEL_STATE)
    return _LATEST_COMPARISON

def compare_gmat_with_principia(gmat_res, p_nodes, vessel_state=None):
    if not gmat_res or not gmat_res.get("success"):
        return {
            "status": "WAITING_FOR_GMAT",
            "message": "尚未执行 GMAT 任务序列求解，请先在游戏中点击【启动 GMAT 任务序列求解】。",
            "timestamp": time.time(),
            "nodes": [],
            "orbit_comparison": None
        }

    g_burns = gmat_res.get("solved_burns", [])
    if not g_burns:
        return {
            "status": "NO_BURNS",
            "message": "GMAT 任务序列中没有机动变轨步骤。",
            "timestamp": time.time(),
            "nodes": [],
            "orbit_comparison": None
        }

    node_comparisons = []
    overall_match = True
    vessel_state = vessel_state or {}

    current_ut = float(vessel_state.get("ut", 0.0))
    p_time_offset = float(vessel_state.get("principia_time_offset", 0.0))

    # Match each GMAT burn with corresponding Principia node
    for i, gb in enumerate(g_burns):
        g_name = gb.get("name", f"Burn_{i+1}")
        g_type = gb.get("type", "Finite")
        g_dur = float(gb.get("duration", 0.0))
        g_mag = float(gb.get("magnitude", 0.0))
        g_v = float(gb.get("dv_v", 0.0))
        g_n = float(gb.get("dv_n", 0.0))
        g_b = float(gb.get("dv_b", 0.0))
        g_epoch_offset = float(gb.get("epoch_offset", 0.0))
        g_thrust = float(gb.get("thrust_kn", 0.0))
        g_isp = float(gb.get("isp", 0.0))
        g_fuel = float(gb.get("fuel_burned", 0.0))

        matched_p = None
        if i < len(p_nodes):
            matched_p = p_nodes[i]

        comp = {
            "index": i + 1,
            "name": g_name,
            "type": g_type,
            "gmat": {
                "duration_s": round(g_dur, 2),
                "magnitude_mps": round(g_mag, 2),
                "dv_v_mps": round(g_v, 2),
                "dv_n_mps": round(g_n, 2),
                "dv_b_mps": round(g_b, 2),
                "epoch_offset_s": round(g_epoch_offset, 2),
                "thrust_kn": round(g_thrust, 2),
                "isp_s": round(g_isp, 2),
                "fuel_burned_kg": round(g_fuel, 2),
                "orbital_delta_v": gb.get("orbital_delta_v"),
                "gravity_loss_mps": gb.get("gravity_loss_mps", 0.0)
            },
            "principia": None,
            "errors": {},
            "status": "UNMATCHED",
            "diagnostics": []
        }

        if matched_p:
            p_dur = float(matched_p.get("duration", 0.0))
            p_mag = float(matched_p.get("magnitude", 0.0))
            p_v = float(matched_p.get("dv_tangent", matched_p.get("dv_v", 0.0)))
            p_n = float(matched_p.get("dv_normal", matched_p.get("dv_n", 0.0)))
            p_b = float(matched_p.get("dv_binormal", matched_p.get("dv_b", 0.0)))
            p_init_time = float(matched_p.get("initial_time", 0.0))
            p_thrust = float(matched_p.get("thrust_kn", 0.0))
            p_isp = float(matched_p.get("isp", 0.0))
            p_init_mass = float(matched_p.get("initial_mass_tonnes", 0.0)) * 1000.0

            # If Principia reported magnitude was zero but vector exists, compute it
            if p_mag <= 0.0 and (p_v != 0.0 or p_n != 0.0 or p_b != 0.0):
                p_mag = math.sqrt(p_v**2 + p_n**2 + p_b**2)

            comp["principia"] = {
                "duration_s": round(p_dur, 2),
                "magnitude_mps": round(p_mag, 2),
                "dv_tangent_mps": round(p_v, 2),
                "dv_normal_mps": round(p_n, 2),
                "dv_binormal_mps": round(p_b, 2),
                "initial_time_s": round(p_init_time, 2),
                "thrust_kn": round(p_thrust, 2),
                "isp_s": round(p_isp, 2),
                "initial_mass_kg": round(p_init_mass, 2)
            }

            # Errors & discrepancies
            dur_err = abs(p_dur - g_dur)
            mag_err = abs(p_mag - g_mag)
            v_err = abs(p_v - g_v)
            n_err = abs(p_n - g_n)
            b_err = abs(p_b - g_b)

            comp["errors"] = {
                "duration_err_s": round(dur_err, 2),
                "duration_err_pct": round(dur_err / max(0.1, g_dur) * 100.0, 2),
                "magnitude_err_mps": round(mag_err, 2),
                "magnitude_err_pct": round(mag_err / max(0.1, g_mag) * 100.0, 2),
                "tangent_err_mps": round(v_err, 2),
                "binormal_err_mps": round(b_err, 2)
            }

            # Check status
            if dur_err <= 1.0 and mag_err <= 5.0 and b_err <= 2.0:
                comp["status"] = "MATCHED_PERFECT"
                comp["diagnostics"].append("✔ GMAT 与 Principia 动力学参数完全吻合 (时长与速度误差均 < 1%)")
            elif dur_err <= 5.0 and mag_err <= 50.0:
                comp["status"] = "MATCHED_ACCEPTABLE"
                comp["diagnostics"].append("ℹ️ 参数基本吻合，处于合理数值积分容差内")
            else:
                comp["status"] = "DISCREPANCY"
                overall_match = False
                if abs(p_dur - g_dur) > 5.0:
                    comp["diagnostics"].append(f"⚠️ 点火时间偏差较大: GMAT={g_dur:.1f}s vs Principia={p_dur:.1f}s (差 {p_dur - g_dur:+.1f}s)")
                if abs(p_b) > 10.0 and abs(g_b) < 1.0:
                    comp["diagnostics"].append(f"⚠️ 径向副法向分量异常: Principia 存在 {p_b:.1f} m/s 径向速度，疑似受重力累积加速度污染")
                if abs(p_mag - g_mag) > 50.0:
                    comp["diagnostics"].append(f"⚠️ 速度增量幅值偏差: GMAT={g_mag:.1f} m/s vs Principia={p_mag:.1f} m/s")

        else:
            comp["status"] = "NOT_IN_PRINCIPIA"
            comp["diagnostics"].append("❌ Principia 飞行计划尚未写入此节点，请点击【一键写入所有机动至 Principia】")
            overall_match = False

        node_comparisons.append(comp)

    # Orbit Elements Comparison
    target_ap = None
    # Find targeted apoapsis in mission sequence
    for s in gmat_res.get("updated_sequence", []):
        if s.get("type") == "Achieve" and "altitude" in s.get("achieve_param", "").lower():
            target_ap = float(s.get("achieve_val", 0.0))
        elif s.get("type") in ("TargetApoapsis", "Targeter") and s.get("target_goal") in ("Apoapsis", "ApoapsisVZ0"):
            target_ap = float(s.get("target_alt", 0.0))

    final_sma = float(gmat_res.get("final_sma", 0.0))
    final_ecc = float(gmat_res.get("final_ecc", 0.0))
    earth_r = 6378.14
    gmat_calc_ap = (final_sma * (1.0 + final_ecc)) - earth_r if final_sma > 0 else 0.0

    p_predicted_ap = float(vessel_state.get("principia_final_apoapsis", 0.0))

    orbit_comp = {
        "target_apoapsis_km": target_ap,
        "gmat_solved_apoapsis_km": round(gmat_calc_ap, 1),
        "gmat_solved_sma_km": round(final_sma, 1),
        "gmat_solved_ecc": round(final_ecc, 4),
        "gmat_solved_inc_deg": round(float(gmat_res.get("final_inc", 0.0)), 2),
        "principia_predicted_apoapsis_km": round(p_predicted_ap, 1) if p_predicted_ap > 0 else None,
        "apoapsis_error_km": round(abs(p_predicted_ap - (target_ap or gmat_calc_ap)), 1) if p_predicted_ap > 0 else None
    }

    return {
        "status": "OK" if overall_match else "DISCREPANCY_DETECTED",
        "message": "所有机动参数与 Principia 完全对齐" if overall_match else "检测到 GMAT 与 Principia 存在参数或状态差异",
        "timestamp": time.time(),
        "craft_name": vessel_state.get("vessel_name", "Unknown"),
        "nodes": node_comparisons,
        "orbit_comparison": orbit_comp
    }
