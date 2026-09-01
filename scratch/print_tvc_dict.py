import sys
import os

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
from script_parser import parse_gmat_script
fn = r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\test_vary_bounds.script"
res = parse_gmat_script(fn)
for s in res["sequence"]:
    if s.get("type") == "ThrustVectorControl":
        print(s)
