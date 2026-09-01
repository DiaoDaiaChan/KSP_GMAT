import sys, os
gmat_bin = r'c:\Users\43701\gmat-win-R2026a\bin'
os.chdir(gmat_bin)
os.add_dll_directory(gmat_bin)
sys.path.insert(0, os.path.join(gmat_bin, r'gmatpy\_py310'))

import gmat_py as gmat
gmat.Setup()

test_script = r'C:\Users\43701\Documents\test.script'
print(f"Loading script: {test_script}")
gmat.LoadScript(test_script)

print("Running script...")
gmat.RunScript()
print("RunScript completed successfully!")

print("\n--- Testing native GMAT API object extraction ---")
sat = gmat.GetObject("Sat")
print(f"Sat: type={sat.GetTypeName()}, name={sat.GetName()}")
for param_name in ["DryMass", "TotalMass", "X", "Y", "Z", "VX", "VY", "VZ", "SMA", "ECC", "INC", "RAAN", "AOP", "TA"]:
    try:
        val = sat.GetRealParameter(param_name)
        print(f"  Sat.{param_name} = {val}")
    except Exception as e:
        print(f"  Sat.{param_name} -> error: {e}")

print("\n--- Testing Thrusters ---")
for t_name in ["Engine11178kNThruster", "RL10SeriesNonExtendableNozzle"]:
    thr = gmat.GetObject(t_name)
    if thr:
        print(f"Thruster: {thr.GetName()} (Type: {thr.GetTypeName()})")
        c1_id = thr.GetParameterID("C1")
        k1_id = thr.GetParameterID("K1")
        d1_id = thr.GetParameterID("ThrustDirection1")
        d2_id = thr.GetParameterID("ThrustDirection2")
        d3_id = thr.GetParameterID("ThrustDirection3")
        print(f"  C1 (Thrust): {thr.GetRealParameter(c1_id)} N")
        print(f"  K1 (Isp): {thr.GetRealParameter(k1_id)} s")
        print(f"  ThrustDirection1: {thr.GetRealParameter(d1_id)}")
        print(f"  ThrustDirection2: {thr.GetRealParameter(d2_id)}")
        print(f"  ThrustDirection3: {thr.GetRealParameter(d3_id)}")

print("\n--- Testing Solvers ---")
for s_name in ["DC_GTO", "DC_GEO"]:
    dc = gmat.GetObject(s_name)
    if dc:
        print(f"Solver: {dc.GetName()} (Type: {dc.GetTypeName()})")

print("\n--- Testing Variables ---")
for v_name in ["GTO_Duration", "GEO_Duration"]:
    var = gmat.GetObject(v_name)
    if var:
        val_id = var.GetParameterID("Value")
        print(f"Variable {v_name} = {var.GetRealParameter(val_id)}")
