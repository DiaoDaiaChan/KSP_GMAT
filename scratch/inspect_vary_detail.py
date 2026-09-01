import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

v = gmat.Construct("Vary", "TestVary")
for i in range(v.GetParameterCount()):
    p_name = v.GetParameterText(i)
    p_type = v.GetParameterTypeString(i)
    try:
        val = v.GetStringParameter(i)
    except:
        try: val = v.GetRealParameter(i)
        except: val = "<na>"
    print(f"{i:2d}: {p_name} ({p_type}) = {val}")
