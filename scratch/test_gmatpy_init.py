import sys, os
gmat_bin = r'c:\Users\43701\gmat-win-R2026a\bin'
os.add_dll_directory(gmat_bin)
sys.path.insert(0, os.path.join(gmat_bin, r'gmatpy\_py310'))

try:
    import gmat_py as gmat
    print("gmat_py imported successfully")
    print("gmat functions:", [x for x in dir(gmat) if not x.startswith('_')][:30])
    
    # Try Setup
    res = gmat.Setup()
    print("gmat.Setup() result:", res)
except Exception as e:
    print("Exception:", e)
    import traceback
    traceback.print_exc()
