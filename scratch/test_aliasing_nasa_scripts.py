import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
from script_parser import parse_gmat_script

target_dir = r"C:\Users\43701\Documents\Downloads\GMAT-NASA-main"
script_files = []
for root, dirs, files in os.walk(target_dir):
    for f in files:
        if f.endswith(".script"):
            script_files.append(os.path.join(root, f))

print("=== VERIFYING SPACECRAFT ALIASING TO SAT ACROSS ALL 25 SCRIPTS ===")

for idx, sf in enumerate(script_files):
    f_name = os.path.basename(sf)
    res = parse_gmat_script(sf)
    orig_sc = res.get("primary_craft_name")
    seq = res.get("sequence", [])
    
    # Check if any non-Sat spacecraft names leaked into stop_types, vary_vars, or achieve_params
    leaks = []
    for s in seq:
        st = s.get("stop_type", "")
        vv = s.get("vary_variable", "")
        ap = s.get("achieve_param", "")
        if orig_sc and orig_sc != "Sat":
            if orig_sc + "." in st: leaks.append(f"stop_type leak: {st}")
            if orig_sc + "." in vv: leaks.append(f"vary_var leak: {vv}")
            if orig_sc + "." in ap: leaks.append(f"achieve_param leak: {ap}")
            
    status = "OK (Aliased to Sat)" if not leaks else f"LEAKED: {leaks}"
    print(f"[{idx+1:2d}/25] {f_name:35s} | Orig Craft: {orig_sc:15s} | Steps: {len(seq):2d} | Status: {status}")

print("=== ALL 25 SCRIPTS TEST COMPLETE ===")
