import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

cmds_to_inspect = [
    "Vary",
    "Achieve",
    "Propagate",
    "Target",
    "BeginFiniteBurn",
    "EndFiniteBurn",
    "Maneuver",
    "Report"
]

print("=== INSPECTING GMAT COMMAND FACTORY DEFAULTS ===")

for c in cmds_to_inspect:
    try:
        cmd_obj = gmat.Construct(c, f"Default_{c}")
        p_count = cmd_obj.GetParameterCount()
        print(f"\n[Command: {c}] Parameters: {p_count}")
        for p_id in range(p_count):
            p_name = cmd_obj.GetParameterText(p_id)
            p_type = cmd_obj.GetParameterTypeString(p_id)
            try:
                if p_type in ("Real", "Distance", "Time", "Velocity", "Angle"):
                    val = cmd_obj.GetRealParameter(p_id)
                elif p_type in ("Integer", "UnsignedInteger"):
                    val = cmd_obj.GetIntegerParameter(p_id)
                elif p_type in ("String", "Enumeration"):
                    val = cmd_obj.GetStringParameter(p_id)
                elif p_type in ("Boolean", "OnOff"):
                    val = cmd_obj.GetBooleanParameter(p_id)
                else:
                    val = "<other>"
            except Exception as e:
                val = f"<err: {e}>"
            print(f"   -> {p_name} ({p_type}): {val}")
    except Exception as ex:
        print(f"Error inspecting {c}: {ex}")
