import sys, os
gmat_bin = r'c:\Users\43701\gmat-win-R2026a\bin'
os.chdir(gmat_bin)
os.add_dll_directory(gmat_bin)
sys.path.insert(0, os.path.join(gmat_bin, r'gmatpy\_py310'))

import gmat_py as gmat
gmat.Setup()
test_script = r'C:\Users\43701\Documents\test.script'
gmat.LoadScript(test_script)
gmat.RunScript()

print("\n=== 1. Testing GetRunSummary() ===")
try:
    summary = gmat.GetRunSummary()
    print("RunSummary:", summary)
except Exception as e:
    print("GetRunSummary error:", e)

print("\n=== 2. Testing GetRuntimeObject() ===")
for obj_name in ["Sat", "GTO_Duration", "GEO_Duration", "DC_GTO", "DC_GEO", "RL10SeriesNonExtendableNozzle"]:
    try:
        ro = gmat.GetRuntimeObject(obj_name)
        if ro:
            print(f"RuntimeObject {obj_name}: Type={ro.GetTypeName()}, Name={ro.GetName()}")
            if obj_name.endswith("Duration"):
                print(f"   Value = {ro.GetRealParameter('Value')}")
            elif obj_name == "Sat":
                print(f"   SMA = {ro.GetRealParameter('SMA')}, ECC = {ro.GetRealParameter('ECC')}, INC = {ro.GetRealParameter('INC')}")
            elif obj_name.startswith("RL10"):
                d1 = ro.GetRealParameter("ThrustDirection1")
                d2 = ro.GetRealParameter("ThrustDirection2")
                d3 = ro.GetRealParameter("ThrustDirection3")
                print(f"   ThrustDirections = ({d1}, {d2}, {d3})")
    except Exception as e:
        print(f"RuntimeObject {obj_name} error: {e}")
