import glob
import os

files = glob.glob(r"c:\Users\43701\gmat-win-R2026a\output\DifferentialCorrector*.data")
for f in files:
    print(f"=== {os.path.basename(f)} ===")
    with open(f, "r") as fl:
        lines = fl.readlines()
        for l in lines[-25:]:
            print(l, end="")
