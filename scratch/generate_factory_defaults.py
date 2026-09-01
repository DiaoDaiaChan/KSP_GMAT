import os
import sys
import json

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

def extract_all_gmat_factory_defaults():
    catalog = {
        "resources": {},
        "commands": {}
    }
    
    resource_types = [
        "Spacecraft", "ChemicalTank", "ElectricTank", "ChemicalThruster", "ElectricThruster",
        "DifferentialCorrector", "SQP", "VF13ad", "fminconOptimizer",
        "ImpulsiveBurn", "FiniteBurn", "Propagator", "ForceModel",
        "CoordinateSystem", "ReportFile", "EphemerisFile"
    ]
    
    for r in resource_types:
        try:
            obj = gmat.Construct(r, f"Default_{r}")
            p_cnt = obj.GetParameterCount()
            catalog["resources"][r] = {}
            for i in range(p_cnt):
                p_name = obj.GetParameterText(i)
                p_type = obj.GetParameterTypeString(i)
                if not p_name: continue
                val = None
                try:
                    if p_type in ("Real", "Distance", "Time", "Velocity", "Angle"):
                        val = obj.GetRealParameter(i)
                    elif p_type in ("Integer", "UnsignedInteger"):
                        val = obj.GetIntegerParameter(i)
                    elif p_type in ("Boolean", "OnOff"):
                        val = obj.GetBooleanParameter(i)
                    elif p_type in ("String", "Enumeration", "Filename"):
                        val = obj.GetStringParameter(i)
                    elif "Array" in p_type or "List" in p_type:
                        val = obj.GetStringArrayParameter(i)
                except Exception:
                    pass
                catalog["resources"][r][p_name] = {
                    "type": p_type,
                    "default": val
                }
        except Exception as e:
            print(f"Skipping resource {r}: {e}")
            
    command_types = [
        "Vary", "Achieve", "Propagate", "Target", "Optimize", "BeginFiniteBurn",
        "EndFiniteBurn", "Maneuver", "Report", "Minimize", "NonlinearConstraint"
    ]
    for c in command_types:
        try:
            cmd = gmat.Construct(c, f"Default_{c}")
            p_cnt = cmd.GetParameterCount()
            catalog["commands"][c] = {}
            for i in range(p_cnt):
                p_name = cmd.GetParameterText(i)
                p_type = cmd.GetParameterTypeString(i)
                if not p_name: continue
                val = None
                try:
                    val = cmd.GetStringParameter(i)
                except Exception:
                    try: val = cmd.GetRealParameter(i)
                    except Exception: pass
                catalog["commands"][c][p_name] = {
                    "type": p_type,
                    "default": val
                }
        except Exception as e:
            print(f"Skipping command {c}: {e}")
            
    return catalog

cat = extract_all_gmat_factory_defaults()
out_file = os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service\gmat_factory_defaults.json")
with open(out_file, "w", encoding="utf-8") as f:
    json.dump(cat, f, indent=2, ensure_ascii=False)

print(f"Generated {out_file} with {len(cat['resources'])} resources and {len(cat['commands'])} commands.")
