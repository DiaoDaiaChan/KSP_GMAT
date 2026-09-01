with open(r"c:\Users\43701\gmat-win-R2026a\output\DifferentialCorrectorDC_GEO.data", "r") as fl:
    lines = fl.readlines()
    for l in lines[-25:]:
        print(l, end="")
