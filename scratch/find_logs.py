import glob
logs = glob.glob(r"c:\Users\43701\gmat-win-R2026a\**\*.log", recursive=True) + glob.glob(r"c:\Users\43701\gmat-win-R2026a\**\*GmatLog*.txt", recursive=True) + glob.glob(r"c:\Users\43701\gmat-win-R2026a\output\*.log") + glob.glob(r"c:\Users\43701\gmat-win-R2026a\output\*.txt")
for lg in logs:
    print(lg)
