import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

confirmed_commands = [
    "Propagate", "Target", "Vary", "Achieve", "EndTarget",
    "Optimize", "Minimize", "NonlinearConstraint", "EndOptimize",
    "Maneuver", "BeginFiniteBurn", "EndFiniteBurn",
    "Report", "Write", "Toggle", "ClearPlot", "PenUp", "PenDown", "UpdateDynamicData",
    "FindEvents", "MarkPoint", "Stop",
    "If", "Else", "EndIf", "While", "EndWhile", "For", "EndFor"
]

for c in confirmed_commands:
    print(f"Constructing command: {c} ... ", end="")
    cmd = gmat.Construct(c, f"Cmd_{c}")
    print(f"Created. Count: {cmd.GetParameterCount()}")
    for i in range(cmd.GetParameterCount()):
        try:
            name = cmd.GetParameterText(i)
        except Exception as e:
            print(f"   Param {i} error: {e}")
            break
    print("Done.")

print("All commands done.")
