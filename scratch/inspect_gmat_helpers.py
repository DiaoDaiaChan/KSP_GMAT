import sys, os
gmat_bin = r'c:\Users\43701\gmat-win-R2026a\bin'
os.chdir(gmat_bin)
os.add_dll_directory(gmat_bin)
sys.path.insert(0, os.path.join(gmat_bin, r'gmatpy\_py310'))

import gmat_py as gmat
gmat.Setup()

all_funcs = [x for x in dir(gmat) if not x.startswith('_')]
print("Get* functions:", [x for x in all_funcs if x.startswith('Get')][:40])
print("Run* functions:", [x for x in all_funcs if x.startswith('Run')])
print("Solver/Report functions:", [x for x in all_funcs if 'Solver' in x or 'Report' in x or 'Summary' in x or 'Output' in x][:40])
print("Show/Write functions:", [x for x in all_funcs if x.startswith('Show') or x.startswith('Write')][:40])
