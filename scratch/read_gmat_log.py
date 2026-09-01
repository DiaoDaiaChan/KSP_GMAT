with open(r"c:\Users\43701\gmat-win-R2026a\output\GmatLog.txt", "r", encoding="utf-8", errors="ignore") as f:
    lines = f.readlines()
    print("Last 50 lines of GmatLog.txt:")
    for l in lines[-50:]:
        print(l, end="")
