import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

commands = [
    "Propagate", "Target", "Vary", "Achieve", "EndTarget",
    "Optimize", "Minimize", "NonlinearConstraint", "EndOptimize",
    "Maneuver", "BeginFiniteBurn", "EndFiniteBurn", "BeginFileThrust", "EndFileThrust",
    "Report", "Write", "Toggle", "ClearPlot", "PenUp", "PenDown", "UpdateDynamicData",
    "FindEvents", "MarkPoint", "Stop", "Breakpoint", "CommandEcho", "Global",
    "If", "Else", "EndIf", "While", "EndWhile", "For", "EndFor", "ScriptEvent",
    "CallGmatFunction", "CallMatlabFunction", "CallPythonFunction"
]

for c in commands:
    sys.stdout.write(f"Testing Command {c}... ")
    sys.stdout.flush()
    try:
        obj = gmat.Construct(c, f"cmd_{c}")
        if obj:
            print(f"OK ({obj.GetParameterCount()} params)")
        else:
            print("None")
    except Exception as e:
        print(f"Error: {e}")
