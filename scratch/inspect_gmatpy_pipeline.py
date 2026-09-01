import sys, os

# Setup GMAT bin and gmat_py
gmat_bin = r'c:\Users\43701\gmat-win-R2026a\bin'
os.add_dll_directory(gmat_bin)
sys.path.insert(0, os.path.join(gmat_bin, r'gmatpy\_py310'))

import gmat_py as gmat

print("=== 1. Initializing NASA GMAT Engine ===")
gmat.Setup(r'c:\Users\43701\gmat-win-R2026a\bin\gmat_startup_file.txt')

print("=== 2. Loading script ===")
test_script = r'C:\Users\43701\Documents\test.script'
gmat.LoadScript(test_script)

print("=== 3. Running Mission ===")
gmat.RunScript()

print("=== 4. Inspecting Spacecraft & Solver Objects ===")
# List all configured objects in GMAT
sat = gmat.GetObject("Sat")
if sat:
    print(f"Spacecraft Sat found:")
    print(f"  DryMass: {sat.GetRealParameter('DryMass')} kg")
    print(f"  TotalMass: {sat.GetRealParameter('TotalMass')} kg")
    print(f"  X: {sat.GetRealParameter('X')} km, Y: {sat.GetRealParameter('Y')} km, Z: {sat.GetRealParameter('Z')} km")
    print(f"  VX: {sat.GetRealParameter('VX')} km/s, VY: {sat.GetRealParameter('VY')} km/s, VZ: {sat.GetRealParameter('VZ')} km/s")
    print(f"  SMA: {sat.GetRealParameter('SMA')} km")
    print(f"  ECC: {sat.GetRealParameter('ECC')}")
    print(f"  INC: {sat.GetRealParameter('INC')} deg")

# Inspect Differential Correctors
for dc_name in ["DC_GTO", "DC_GEO"]:
    dc = gmat.GetObject(dc_name)
    if dc:
        print(f"\nDifferentialCorrector {dc_name}:")
        print(f"  Type: {dc.GetTypeName()}")
        # Inspect parameters on DC
        count = dc.GetParameterCount()
        for p in range(count):
            p_name = dc.GetParameterText(p)
            try:
                p_val = dc.GetStringParameter(p_name)
                print(f"    {p_name}: {p_val}")
            except:
                pass

# Inspect Variables
for v_name in ["GTO_Duration", "GEO_Duration"]:
    var = gmat.GetObject(v_name)
    if var:
        print(f"\nVariable {v_name}: value = {var.GetRealParameter('Value')}")

# Inspect Thrusters
for t_name in ["Engine11178kNThruster", "RL10SeriesNonExtendableNozzle"]:
    thr = gmat.GetObject(t_name)
    if thr:
        print(f"\nThruster {t_name}:")
        print(f"  C1 (Thrust): {thr.GetRealParameter('C1')} N")
        print(f"  K1 (Isp): {thr.GetRealParameter('K1')} s")
        print(f"  ThrustDirection1: {thr.GetRealParameter('ThrustDirection1')}")
        print(f"  ThrustDirection2: {thr.GetRealParameter('ThrustDirection2')}")
        print(f"  ThrustDirection3: {thr.GetRealParameter('ThrustDirection3')}")

# Inspect FiniteBurns
for b_name in ["GTO_Burn", "GEO_Burn"]:
    b = gmat.GetObject(b_name)
    if b:
        print(f"\nFiniteBurn {b_name}:")
        print(f"  TypeName: {b.GetTypeName()}")
        count = b.GetParameterCount()
        for p in range(count):
            p_name = b.GetParameterText(p)
            try:
                p_val = b.GetStringParameter(p_name)
                print(f"    {p_name}: {p_val}")
            except:
                pass
