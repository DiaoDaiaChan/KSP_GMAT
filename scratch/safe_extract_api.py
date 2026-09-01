import os
import sys
import json

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

types = [
    "Spacecraft", "Formation", "ChemicalTank", "ElectricTank", "ChemicalThruster", "ElectricThruster",
    "NuclearPowerSystem", "SolarPowerSystem", "Antenna", "Transmitter", "Receiver", "Transponder",
    "Propagator", "ForceModel", "PointMassForce", "DragForce", "SRP", "RelativisticCorrection",
    "ODEModel", "PrinceDormand78", "RungeKutta89", "RungeKutta68", "RungeKutta56", "BulirschStoer",
    "AdamsBashforthMoulton", "SPK",
    "ImpulsiveBurn", "FiniteBurn",
    "DifferentialCorrector",
    "CelestialBody", "Planet", "Moon", "Star", "Barycenter", "LibrationPoint",
    "CoordinateSystem", "SolarSystem",
    "ReportFile", "EphemerisFile", "OrbitView", "GroundTrackPlot", "XYPlot", "DataFile",
    "EclipseLocator", "ContactLocator",
    "Variable", "Array", "String",
    "Propagate", "Target", "Vary", "Achieve", "EndTarget",
    "Optimize", "Minimize", "NonlinearConstraint", "EndOptimize",
    "Maneuver", "BeginFiniteBurn", "EndFiniteBurn", "BeginFileThrust", "EndFileThrust",
    "Report", "Write", "Toggle", "ClearPlot", "PenUp", "PenDown", "UpdateDynamicData",
    "FindEvents", "MarkPoint", "Stop", "Breakpoint", "CommandEcho", "Global",
    "If", "Else", "EndIf", "While", "EndWhile", "For", "EndFor", "ScriptEvent",
    "CallGmatFunction", "CallMatlabFunction", "CallPythonFunction"
]

results = {"resources": {}, "commands": {}}

for t in types:
    try:
        obj = gmat.Construct(t, f"probe_{t}")
        if obj is None:
            print(f"Skipping {t}: returned None")
            continue
        try:
            p_cnt = obj.GetParameterCount()
        except:
            p_cnt = 0
        p_dict = {}
        for i in range(p_cnt):
            try:
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
                except:
                    try: val = obj.GetStringParameter(i)
                    except: pass
                p_dict[p_name] = {"type": p_type, "default": val}
            except Exception as pe:
                pass
        
        # Classify
        is_cmd = t in ("Propagate", "Target", "Vary", "Achieve", "EndTarget", "Optimize", "Minimize", "NonlinearConstraint", "EndOptimize", "Maneuver", "BeginFiniteBurn", "EndFiniteBurn", "BeginFileThrust", "EndFileThrust", "Report", "Write", "Toggle", "ClearPlot", "PenUp", "PenDown", "UpdateDynamicData", "FindEvents", "MarkPoint", "Stop", "Breakpoint", "CommandEcho", "Global", "If", "Else", "EndIf", "While", "EndWhile", "For", "EndFor", "ScriptEvent", "CallGmatFunction", "CallMatlabFunction", "CallPythonFunction")
        if is_cmd:
            results["commands"][t] = {"param_count": len(p_dict), "parameters": p_dict}
        else:
            results["resources"][t] = {"param_count": len(p_dict), "parameters": p_dict}
        print(f"[OK] {t}: {len(p_dict)} params")
    except Exception as ex:
        print(f"[ERR] {t}: {ex}")

with open(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service\gmat_complete_api_catalog.json", "w", encoding="utf-8") as f:
    json.dump(results, f, indent=2, ensure_ascii=False)

print(f"\nSuccessfully wrote gmat_complete_api_catalog.json with {len(results['resources'])} resources and {len(results['commands'])} commands!")
