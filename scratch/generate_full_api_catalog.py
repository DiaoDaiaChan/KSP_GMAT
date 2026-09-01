import os
import sys
import json

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

confirmed_resources = [
    "Spacecraft", "ChemicalTank", "ElectricTank", "ChemicalThruster", "ElectricThruster",
    "SolarPowerSystem", "NuclearPowerSystem",
    "Propagator", "ForceModel", "PointMassForce", "DragForce", "RelativisticCorrection",
    "ODEModel", "PrinceDormand78", "RungeKutta89", "RungeKutta68", "RungeKutta56", "AdamsBashforthMoulton",
    "ImpulsiveBurn", "FiniteBurn",
    "DifferentialCorrector",
    "Planet", "Moon", "Barycenter", "LibrationPoint", "CoordinateSystem",
    "ReportFile", "EphemerisFile", "OrbitView", "GroundTrackPlot", "XYPlot",
    "Variable", "Array", "String"
]

confirmed_commands = [
    "Propagate", "Target", "Vary", "Achieve", "EndTarget",
    "Optimize", "Minimize", "NonlinearConstraint", "EndOptimize",
    "Maneuver", "BeginFiniteBurn", "EndFiniteBurn",
    "Report", "Write", "Toggle", "ClearPlot", "PenUp", "PenDown", "UpdateDynamicData",
    "FindEvents", "MarkPoint", "Stop",
    "If", "Else", "EndIf", "While", "EndWhile", "For", "EndFor"
]

complete_catalog = {
    "version": "GMAT-R2026a-Official",
    "resources": {},
    "commands": {}
}

for r in confirmed_resources:
    try:
        obj = gmat.Construct(r, f"Probe_{r}")
        p_cnt = obj.GetParameterCount()
        p_map = {}
        for i in range(p_cnt):
            try:
                p_name = obj.GetParameterText(i)
                p_type = obj.GetParameterTypeString(i)
                if not p_name: continue
                val = None
                # Safe getter: try string first
                try:
                    val = obj.GetStringParameter(i)
                except BaseException:
                    pass
                p_map[p_name] = {
                    "id": i,
                    "type": p_type,
                    "default": val
                }
            except BaseException:
                pass
        complete_catalog["resources"][r] = {
            "total_parameters": len(p_map),
            "parameters": p_map
        }
        print(f"Resource {r}: {len(p_map)} params")
    except BaseException as e:
        print(f"Error on resource {r}: {e}")

for c in confirmed_commands:
    try:
        cmd = gmat.Construct(c, f"Cmd_{c}")
        p_cnt = cmd.GetParameterCount()
        p_map = {}
        for i in range(p_cnt):
            try:
                p_name = cmd.GetParameterText(i)
                p_type = cmd.GetParameterTypeString(i)
                if not p_name: continue
                val = None
                try:
                    val = cmd.GetStringParameter(i)
                except BaseException:
                    pass
                p_map[p_name] = {
                    "id": i,
                    "type": p_type,
                    "default": val
                }
            except BaseException:
                pass
        complete_catalog["commands"][c] = {
            "total_parameters": len(p_map),
            "parameters": p_map
        }
        print(f"Command {c}: {len(p_map)} params")
    except BaseException as e:
        print(f"Error on command {c}: {e}")

out_path = os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service\gmat_complete_api_catalog.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(complete_catalog, f, indent=2, ensure_ascii=False)

print(f"\nSuccessfully generated {out_path} with {len(complete_catalog['resources'])} Resources and {len(complete_catalog['commands'])} Commands!")
