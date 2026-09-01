import os
import sys
import glob
import re

def parse_dc_file(filepath):
    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        text = f.read()
    
    blocks = re.split(r"(?:Iteration\s+\d+|Final Variable values:)", text)
    last_block = blocks[-1] if blocks else text
    
    converged = "Targeting Completed in" in text
    m_iter = re.search(r"Targeting Completed in (\d+) iterations", text)
    if not m_iter:
        m_iter = re.search(r"Iteration\s+(\d+)", text)
    iterations = int(m_iter.group(1)) if m_iter else 50
    
    var_dict = {}
    m_vars = re.findall(r"([A-Za-z_][\w.]*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)", last_block)
    for k, v in m_vars:
        if k not in ("Tolerance", "Desired", "Achieved", "Variance", "ScaleFactor"):
            try: var_dict[k] = float(v)
            except: pass
            
    goals = {}
    goal_matches = re.findall(r"([A-Za-z_][\w.]*)\s+Desired:\s*([\d\.\-eE]+)\s+Achieved:\s*([\d\.\-eE]+)(?:\s+Tolerance:\s*([\d\.\-eE]+))?", last_block)
    for g_param, g_des, g_ach, g_tol in goal_matches:
        des = float(g_des)
        ach = float(g_ach)
        tol = float(g_tol) if g_tol else 0.1
        var = ach - des
        passed = abs(var) <= (tol * 1.05)
        goals[g_param] = {
            "param": g_param,
            "desired": des,
            "achieved": ach,
            "variance": round(var, 5),
            "tolerance": tol,
            "passed": passed
        }
    return {
        "converged": converged,
        "iterations": iterations,
        "variables": var_dict,
        "goals": list(goals.values())
    }

for name in ["DifferentialCorrectorDC_GTO.data", "DifferentialCorrectorDC_GEO.data"]:
    p = os.path.join(r"c:\Users\43701\gmat-win-R2026a\output", name)
    if os.path.exists(p):
        print(f"=== {name} ===")
        res = parse_dc_file(p)
        print("Converged:", res["converged"], "Iterations:", res["iterations"])
        print("Variables:", res["variables"])
        print("Goals:", res["goals"])
