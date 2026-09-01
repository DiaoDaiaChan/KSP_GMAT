import os
import glob
import sys
import json
import traceback

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
from script_parser import parse_gmat_script

target_dir = r"C:\Users\43701\Documents\Downloads\GMAT-NASA-main"
script_files = []
for root, dirs, files in os.walk(target_dir):
    for f in files:
        if f.endswith(".script"):
            script_files.append(os.path.join(root, f))

print(f"Total scripts found: {len(script_files)}\n")

passed = 0
failed = 0

for idx, sf in enumerate(script_files):
    rel = os.path.relpath(sf, target_dir)
    print(f"[{idx+1}/{len(script_files)}] Testing: {rel}")
    try:
        res = parse_gmat_script(sf)
        crafts = res.get("crafts", [])
        sc_names = [c.get("name", "Unknown") for c in crafts] if isinstance(crafts, list) else []
        tanks = res.get("tanks", [])
        thrusters = res.get("thrusters", [])
        solvers = res.get("solvers", [])
        seq = res.get("sequence", [])
        user_vars = res.get("user_variables", [])
        
        print(f"   -> Spacecrafts: {sc_names}")
        print(f"   -> Tanks: {len(tanks)}, Thrusters: {len(thrusters)}, Solvers: {solvers}")
        print(f"   -> Sequence Steps: {len(seq)}, User Variables: {len(user_vars)}")
        
        # Check first 3 steps types
        if seq:
            step_types = [s.get("type", "Unknown") for s in seq[:5]]
            print(f"   -> First steps: {step_types}")
        
        passed += 1
    except Exception as ex:
        print(f"   [FAILED]: {ex}")
        traceback.print_exc()
        failed += 1
    print("-" * 60)

print(f"\nRESULTS: Passed {passed}/{len(script_files)}, Failed {failed}/{len(script_files)}")
