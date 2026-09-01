import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

# Test various GMAT core factory types
all_possible_types = [
    # Spacecraft & Hardware
    "Spacecraft", "Formation", "ChemicalTank", "ElectricTank", "ChemicalThruster", "ElectricThruster",
    "NuclearPowerSystem", "SolarPowerSystem", "Antenna", "Transmitter", "Receiver", "Transponder",
    
    # Propagators & Force Models
    "Propagator", "ForceModel", "PointMassForce", "DragForce", "SRP", "RelativisticCorrection",
    "ODEModel", "PrinceDormand78", "RungeKutta89", "RungeKutta68", "RungeKutta56", "BulirschStoer",
    "AdamsBashforthMoulton", "SPK",
    
    # Burns & Maneuvers
    "ImpulsiveBurn", "FiniteBurn",
    
    # Solvers & Optimizers
    "DifferentialCorrector", "SQP", "VF13ad", "fminconOptimizer", "Snopt",
    
    # Celestial & Coordinate Systems
    "CelestialBody", "Planet", "Moon", "Star", "Barycenter", "LibrationPoint",
    "CoordinateSystem", "SolarSystem",
    
    # Outputs & Subscribers
    "ReportFile", "EphemerisFile", "OrbitView", "GroundTrackPlot", "XYPlot", "DataFile",
    
    # Event Locators & Measurements
    "EclipseLocator", "ContactLocator", "MeasurementModel", "TrackingSystem",
    
    # Variables, Arrays, Strings
    "Variable", "Array", "String",
    
    # Commands
    "Propagate", "Target", "Vary", "Achieve", "EndTarget",
    "Optimize", "Minimize", "NonlinearConstraint", "EndOptimize",
    "Maneuver", "BeginFiniteBurn", "EndFiniteBurn", "BeginFileThrust", "EndFileThrust",
    "Report", "Write", "Toggle", "ClearPlot", "PenUp", "PenDown", "UpdateDynamicData",
    "FindEvents", "MarkPoint", "Stop", "Breakpoint", "CommandEcho", "Global",
    "If", "Else", "EndIf", "While", "EndWhile", "For", "EndFor", "ScriptEvent",
    "BeginScript", "EndScript", "CallGmatFunction", "CallMatlabFunction", "CallPythonFunction"
]

print("=== TESTING CREATABILITY OF ALL GMAT TYPES ===")
valid_resources = {}
valid_commands = {}

for t in all_possible_types:
    try:
        obj = gmat.Construct(t, f"probe_{t}")
        if obj is not None:
            p_cnt = obj.GetParameterCount()
            params = {}
            for i in range(p_cnt):
                p_name = obj.GetParameterText(i)
                p_type = obj.GetParameterTypeString(i)
                if not p_name: continue
                # check if read/write
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
                params[p_name] = {
                    "id": i,
                    "type": p_type,
                    "default": val
                }
            
            # Categorize
            is_cmd = hasattr(obj, "GetTypeName") and ("Command" in obj.GetTypeName() or t in ("Propagate", "Target", "Vary", "Achieve", "Optimize", "Maneuver", "Report", "Write", "Toggle", "Stop", "If", "While", "For"))
            if is_cmd:
                valid_commands[t] = {"total_params": p_cnt, "parameters": params}
            else:
                valid_resources[t] = {"total_params": p_cnt, "parameters": params}
            print(f"  [OK] {t:25s} (Params: {p_cnt})")
        else:
            print(f"  [NULL] {t}")
    except Exception as e:
        print(f"  [FAIL] {t:25s}: {e}")

print(f"\nDiscovered: {len(valid_resources)} Valid Resources, {len(valid_commands)} Valid Commands")
