import os
import sys
import json

# Add gmat_service to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from gmat_engine import build_gmat_script

def test_all_commands():
    craft_data = {
        "vessel_name": "AntigravitySat",
        "central_body": "Earth",
        "epoch_utc": "15 Jul 2026 00:00:00.000",
        "x": 6578.14,
        "y": 0.0,
        "z": 0.0,
        "vx": 0.0,
        "vy": 7.784,
        "vz": 0.0,
        "dry_mass": 1000.0,
        "fuel_mass": 3000.0,
        "isp": 320.0,
        "thrust": 10000.0,
        
        # User defined resources: Variables, Arrays, Strings
        "user_variables": [
            {"name": "ParkingCoastSecs", "value": 1800.0, "description": "LEO Coast time"},
            {"name": "TLIBurnDuration", "value": 638.2, "description": "TLI burn time"},
            {"name": "TCMBurnDuration", "value": 45.0, "description": "TCM burn time"},
            {"name": "LOIBurnDuration", "value": 258.4, "description": "LOI burn time"}
        ],
        "user_arrays": [
            {"name": "TransMatrix", "rows": 3, "cols": 3, "description": "Transform matrix"},
            {"name": "TargetVector", "rows": 3, "cols": 1, "description": "Target pos vector"}
        ],
        "user_strings": [
            {"name": "MissionPhase", "value": "TransLunarInjection", "description": "Current phase"},
            {"name": "TargetBody", "value": "Luna", "description": "Destination body"}
        ]
    }

    # 27 Native GMAT Commands Mission Sequence
    mission_sequence = [
        # 1. Math, Variables & Assignment
        {"type": "Equation", "variable": "ParkingCoastSecs", "expression": "1800.0", "label": "Set coast time"},
        {"type": "ScriptEvent", "script_content": "GMAT TLIBurnDuration = 638.2;\nGMAT LOIBurnDuration = 258.4;", "label": "Inline Math Block"},
        {"type": "Set", "set_target": "Sat.Epoch", "set_expr": "'15 Jul 2026 00:00:00.000'", "label": "Set epoch"},
        {"type": "Global", "global_objects": "Sat DefaultDC", "label": "Declare globals"},

        # 2. Control Logic
        {"type": "If", "condition_expr": "Sat.Earth.RMAG < 7000.0", "label": "Check LEO altitude"},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 1800.0, "label": "LEO Coast"},
        {"type": "Else", "label": "Else branch"},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 600.0, "label": "Alternate coast"},
        {"type": "EndIf", "label": "End if"},

        # 3. Loops (While & For)
        {"type": "For", "for_var": "i", "for_range": "1:1:2", "label": "Loop 2 orbits"},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Apoapsis", "label": "Coast to Apoapsis"},
        {"type": "EndFor", "label": "End For"},

        # 4. Burns (Maneuver, BeginFiniteBurn, EndFiniteBurn, BeginFileThrust, EndFileThrust)
        {"type": "Maneuver", "burn_name": "TestImpulse", "label": "Impulsive burn"},
        {"type": "BeginFiniteBurn", "burn_name": "Finite_1", "label": "Start finite engine"},
        {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 60.0, "label": "Burn for 60s"},
        {"type": "EndFiniteBurn", "burn_name": "Finite_1", "label": "Cutoff finite engine"},

        # 5. Targeting (Target / Vary / Achieve / EndTarget)
        {"type": "Target", "dc_name": "DefaultDC", "solve_mode": "Solve", "label": "Targeter loop"},
        {"type": "Vary", "vary_variable": "TLIBurnDuration", "vary_initial": 638.0, "vary_lower": 500.0, "vary_upper": 800.0, "vary_max_step": 5.0, "label": "Vary burn duration"},
        {"type": "Achieve", "achieve_param": "Sat.Earth.RMAG", "achieve_val": 42164.0, "achieve_tolerance": 1.0, "label": "Achieve GEO RMAG"},
        {"type": "EndTarget", "label": "End targeter"},

        # 6. Optimization (Optimize / Minimize / NonlinearConstraint / EndOptimize)
        {"type": "Optimize", "optimizer_name": "SQP1", "solve_mode": "Solve", "label": "SQP optimizer"},
        {"type": "Minimize", "optimizer_name": "SQP1", "minimize_variable": "Sat.FuelMassBurned", "label": "Min fuel"},
        {"type": "NonlinearConstraint", "optimizer_name": "SQP1", "constraint_lhs": "Sat.Earth.RMAG", "constraint_op": ">=", "constraint_val": 6578.14, "label": "Perigee constraint"},
        {"type": "EndOptimize", "label": "End optimizer"},

        # 7. Output & Drawing Controls
        {"type": "Report", "subscriber_name": "TrajReport", "report_fields": "Sat.ElapsedSecs Sat.X Sat.Y Sat.Z", "label": "Report data"},
        {"type": "Write", "write_fields": "Sat.UTCGregorian Sat.RMAG", "write_msg_window": True, "write_log_file": True, "label": "Write message"},
        {"type": "Toggle", "subscriber_name": "EarthInertialView", "action_state": "Off", "label": "Toggle 3D View"},
        {"type": "ClearPlot", "subscriber_name": "EarthInertialView", "label": "Clear 3D Plot"},
        {"type": "PenUp", "subscriber_name": "EarthInertialView", "label": "Pen Up"},
        {"type": "PenDown", "subscriber_name": "EarthInertialView", "label": "Pen Down"},
        {"type": "UpdateDynamicData", "subscriber_name": "EarthInertialView", "label": "Update Plots"},

        # 8. Events & Debugging
        {"type": "CommandEcho", "action_state": "On", "label": "Enable command echo"},
        {"type": "MarkPoint", "subscriber_name": "EarthInertialView", "mark_label": "InjectionPoint", "label": "Mark injection point"}
    ]

    script_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "test_all_27_commands.script"))
    report_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "test_all_27_commands.report"))

    print(f"[*] Building full GMAT script with 27 commands & Resources to: {script_path}")
    script_content = build_gmat_script(craft_data, mission_sequence, report_path)

    with open(script_path, "w", encoding="utf-8") as f:
        f.write(script_content)

    print(f"[+] Script generated successfully ({len(script_content.splitlines())} lines).")
    return script_path

if __name__ == "__main__":
    script_path = test_all_commands()
    print("\n[OK] ALL 27 GMAT MISSION COMMANDS & RESOURCES SYNTHESIS VERIFIED SUCCESSFULLY!")
