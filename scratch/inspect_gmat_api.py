import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

print("=== GMATPY MODULE INSPECTION ===")
gmat_attrs = dir(gmat)
print(f"Total attributes in gmat: {len(gmat_attrs)}")

# Check factory / list functions
list_funcs = [a for a in gmat_attrs if "list" in a.lower() or "type" in a.lower() or "factory" in a.lower() or "get" in a.lower()]
print(f"List/Get/Type functions: {len(list_funcs)}")
for f in sorted(list_funcs):
    print(f"  {f}")
