import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

types_to_inspect = [
    "Spacecraft",
    "ChemicalTank",
    "ChemicalThruster",
    "DifferentialCorrector",
    "ImpulsiveBurn",
    "FiniteBurn",
    "Propagator",
    "ForceModel"
]

print("=== INSPECTING OFFICIAL GMAT OBJECT FACTORY DEFAULTS ===")

defaults_catalog = {}

for t in types_to_inspect:
    try:
        obj = gmat.Construct(t, f"Default_{t}")
        p_count = obj.GetParameterCount()
        defaults_catalog[t] = {}
        print(f"\n[{t}] Total Parameters: {p_count}")
        for p_id in range(p_count):
            p_name = obj.GetParameterText(p_id)
            p_type_name = obj.GetParameterTypeString(p_id)
            
            # Retrieve default value according to type
            val = None
            try:
                if p_type_name in ("Real", "Distance", "Time", "Velocity", "Angle"):
                    val = obj.GetRealParameter(p_id)
                elif p_type_name in ("Integer", "UnsignedInteger"):
                    val = obj.GetIntegerParameter(p_id)
                elif p_type_name in ("Boolean", "OnOff"):
                    val = obj.GetBooleanParameter(p_id)
                elif p_type_name in ("String", "Enumeration", "Filename"):
                    val = obj.GetStringParameter(p_id)
                elif "Array" in p_type_name or "List" in p_type_name:
                    val = obj.GetStringArrayParameter(p_id)
            except Exception as e:
                val = f"<err: {e}>"
                
            defaults_catalog[t][p_name] = {
                "id": p_id,
                "type": p_type_name,
                "default": val
            }
            # Print sample key parameters
            if p_name in ("DryMass", "DragArea", "Cd", "Cr", "SRPArea", "FuelMass", "Volume", "PressureModel", 
                          "C1", "K1", "ThrustDirection1", "ThrustDirection2", "ThrustDirection3", 
                          "MaximumIterations", "DerivativeMethod", "Algorithm", "ShowProgress",
                          "Element1", "Element2", "Element3", "Isp", "CentralBody", "Type"):
                print(f"   -> {p_name} ({p_type_name}): {val}")
    except Exception as ex:
        print(f"Error constructing {t}: {ex}")

print("\n=== INSPECTION COMPLETED SUCCESSFULLY ===")
