import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

nps = gmat.Construct("NuclearPowerSystem", "NPS")
for i in range(nps.GetParameterCount()):
    print(f"P {i}...", flush=True)
    t = nps.GetParameterText(i)
    typ = nps.GetParameterTypeString(i)
    print(f"  {t} ({typ})", flush=True)
    if typ in ("Real", "Distance", "Time"):
        v = nps.GetRealParameter(i)
        print("  val:", v)
