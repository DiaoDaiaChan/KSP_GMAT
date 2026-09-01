import os
import re

dcf = r"c:\Users\43701\gmat-win-R2026a\output\DifferentialCorrectorDC_GEO.data"
if os.path.exists(dcf):
    with open(dcf, "r", encoding="utf-8", errors="ignore") as fl:
        text = fl.read()
    blocks = re.split(r"(?:Iteration\s+\d+|Final Variable values:)", text)
    last_block = blocks[-1] if blocks else text
    print("Last Block:")
    print(last_block)
    goal_matches = re.findall(r"([A-Za-z_][\w.]*)\s+Desired:\s*([\d\.\-eE]+)\s+Achieved:\s*([\d\.\-eE]+)(?:\s+Tolerance:\s*([\d\.\-eE]+))?", last_block)
    print("Goal Matches:")
    print(goal_matches)
