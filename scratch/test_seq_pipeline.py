import os
import sys
import json
import traceback

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import script_parser

print("==================================================")
print("  GMAT RUN_MISSION_SEQUENCE PIPELINE ROBUSTNESS  ")
print("==================================================")

craft = {
    "name": "Sat",
    "epoch_utc": "09 Mar 1960 00:56:51.031",
    "x": 5405.33, "y": -2646.04, "z": -2616.99,
    "vx": 2.47, "vy": 7.10, "vz": -2.05,
    "dry_mass": 1477.96,
    "fuel_mass": 6282.91,
    "tanks": [{"id": "Tank0", "fuel_mass": 6282.91, "volume": 62.83, "pressure_model": "PressureRegulated"}],
    "thrusters": [{"id": "Engine1", "thrust": 108100.0, "isp": 464.8, "tank": "Tank0"}]
}

sequences = [
    # 1. 2-Step Finite Burn LEO->GTO
    ("LEO to GTO Finite Burn Targeter", [
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Sat.Earth.Latitude", "target_val": 0.0},
        {"type": "Target", "dc_name": "DC_GTO"},
        {"type": "Vary", "vary_variable": "GTO_Duration", "vary_initial": 120.0, "vary_lower": 10.0, "vary_upper": 400.0, "vary_max_step": 10.0, "dc_name": "DC_GTO"},
        {"type": "BeginFiniteBurn", "burn_name": "GTOBurn", "bound_thruster": "Engine1"},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Sat.ElapsedSecs", "duration_str": "GTO_Duration"},
        {"type": "EndFiniteBurn", "burn_name": "GTOBurn"},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Sat.Earth.Apoapsis"},
        {"type": "Achieve", "achieve_param": "Sat.Earth.Altitude", "achieve_val": 35786.0, "achieve_tolerance": 5.0, "dc_name": "DC_GTO"},
        {"type": "EndTarget"}
    ]),

    # 2. Pure Propagate without targeting
    ("Pure Propagate Single Step", [
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 3600.0}
    ]),

    # 3. Impulsive Maneuver
    ("Impulsive Maneuver", [
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 600.0},
        {"type": "ImpulsiveBurn", "burn_name": "Burn1", "dv_v": 200.0, "dv_n": 0.0, "dv_b": 0.0},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 1800.0}
    ])
]

passed_count = 0
for idx, (title, seq) in enumerate(sequences, 1):
    print(f"\n--- Test #{idx}: {title} ---")
    try:
        res = gmat_engine.run_mission_sequence(craft, seq)
        if res.get("success"):
            print(f"  [PASS] Clean Execution.")
            print(f"         SMA: {res.get('final_sma')} km, Ecc: {res.get('final_ecc')}, Solvers: {res.get('all_solvers_converged')}")
            print(f"         Solved Burns: {len(res.get('solved_burns', []))}")
            passed_count += 1
        else:
            print(f"  [FAIL] Result success=False, error: {res.get('error')}, warning: {res.get('warning')}")
    except Exception as e:
        print(f"  [EXCEPTION] Crashed: {e}")
        traceback.print_exc()

print(f"\n==================================================")
print(f"  SUMMARY: {passed_count}/{len(sequences)} Passed ({passed_count/len(sequences)*100:.0f}%)")
print(f"==================================================")
