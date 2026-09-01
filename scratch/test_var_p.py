import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

v = gmat.Construct("Variable", "V")
for i in range(v.GetParameterCount()):
    try:
        t = v.GetParameterText(i)
        print(i, t)
    except Exception as e:
        print(i, "error:", type(e), e)
