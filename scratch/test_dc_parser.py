import os
import sys
import glob
import re

output_dir = r"c:\Users\43701\gmat-win-R2026a\output"
dc_files = glob.glob(os.path.join(output_dir, "DifferentialCorrector*.data"))
print(f"Found {len(dc_files)} DC data files:")

solvers = {}

for f in dc_files:
    bname = os.path.basename(f)
    # extract solver name from DifferentialCorrector<Name>.data
    m_name = re.match(r"DifferentialCorrector(.*)\.data", bname)
    sname = m_name.group(1) if m_name else bname
    with open(f, "r", encoding="utf-8", errors="ignore") as fl:
        text = fl.read()
    
    # Parse variables
    var_dict = {}
    m_vars = re.findall(r"(\w+(?:\.\w+)*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)", text)
    for k, v in m_vars:
        if k not in ("Tolerance", "Desired", "Achieved", "Variance"):
            try: var_dict[k] = float(v)
            except: pass
            
    # Parse goals and achieved
    goals = []
    goal_blocks = re.findall(r"([\w\.]+)\s+Desired:\s*([\d\.\-eE]+)\s+Achieved:\s*([\d\.\-eE]+)(?:\s+Tolerance:\s*([\d\.\-eE]+))?", text)
    for g_param, g_des, g_ach, g_tol in goal_blocks:
        des = float(g_des)
        ach = float(g_ach)
        tol = float(g_tol) if g_tol else 0.1
        var = ach - des
        passed = abs(var) <= (tol * 1.05)
        goals.append({
            "param": g_param,
            "desired": des,
            "achieved": ach,
            "variance": round(var, 5),
            "tolerance": tol,
            "passed": passed
        })
        
    m_iter = re.search(r"Targeting Completed in (\d+) iterations", text)
    iterations = int(m_iter.group(1)) if m_iter else 50
    converged = ("Targeting Completed" in text) and all(g["passed"] for g in goals)
    
    solvers[sname] = {
        "name": sname,
        "converged": converged,
        "iterations": iterations,
        "variables": var_dict,
        "goals": goals
    }
    print(f"Solver: {sname} -> Converged: {converged} (Iter: {iterations})")
    print(f"  Variables: {var_dict}")
    print(f"  Goals: {goals}")
