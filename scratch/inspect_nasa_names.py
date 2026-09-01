import os
import glob
import sys
import re

target_dir = r"C:\Users\43701\Documents\Downloads\GMAT-NASA-main"
for root, dirs, files in os.walk(target_dir):
    for f in files:
        if f.endswith(".script"):
            fp = os.path.join(root, f)
            with open(fp, "r", encoding="utf-8", errors="ignore") as file:
                content = file.read()
            sc_matches = re.findall(r"Create\s+Spacecraft\s+([^;]+);?", content, re.I)
            tanks_matches = re.findall(r"Create\s+ChemicalTank\s+([^;]+);?", content, re.I)
            thr_matches = re.findall(r"Create\s+ChemicalThruster\s+([^;]+);?", content, re.I)
            dc_matches = re.findall(r"Create\s+(?:DifferentialCorrector|SQP|VF13ad|fminconOptimizer)\s+([^;]+);?", content, re.I)
            print(f"File: {f}")
            print(f"   Spacecraft: {sc_matches}")
            print(f"   Tanks: {tanks_matches}")
            print(f"   Thrusters: {thr_matches}")
            print(f"   Solvers: {dc_matches}")
            print("-" * 50)
