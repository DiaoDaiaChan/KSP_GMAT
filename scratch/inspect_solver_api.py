import sys, os
gmat_bin = r'c:\Users\43701\gmat-win-R2026a\bin'
os.chdir(gmat_bin)
os.add_dll_directory(gmat_bin)
sys.path.insert(0, os.path.join(gmat_bin, r'gmatpy\_py310'))

import gmat_py as gmat
gmat.Setup()
test_script = r'C:\Users\43701\Documents\test.script'
gmat.LoadScript(test_script)
gmat.RunScript()

dc_gto = gmat.GetObject("DC_GTO")
print("DC_GTO type:", dc_gto.GetTypeName())
print("DC methods:", [m for m in dir(dc_gto) if not m.startswith('__')])

# Check parameter count and names
count = dc_gto.GetParameterCount()
print(f"DC_GTO Parameter Count: {count}")
for i in range(count):
    pname = dc_gto.GetParameterText(i)
    ptype = dc_gto.GetParameterTypeString(i)
    try:
        if ptype == "Real":
            val = dc_gto.GetRealParameter(i)
        elif ptype == "String":
            val = dc_gto.GetStringParameter(i)
        elif ptype == "Integer":
            val = dc_gto.GetIntegerParameter(i)
        elif ptype == "Boolean":
            val = dc_gto.GetBooleanParameter(i)
        else:
            val = f"<{ptype}>"
        print(f"  [{i}] {pname} ({ptype}) = {val}")
    except Exception as e:
        print(f"  [{i}] {pname} ({ptype}) -> Error: {e}")

# Check Moderator / Sandbox / Internal states
mod = gmat.Moderator.Instance()
print("\nModerator methods:", [m for m in dir(mod) if not m.startswith('__')])
