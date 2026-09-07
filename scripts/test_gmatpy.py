import sys, os, traceback

try:
    bin_path = r'C:\Users\43701\gmat-win-R2026a\bin'
    if bin_path not in sys.path:
        sys.path.append(bin_path)
    if hasattr(os, 'add_dll_directory'):
        os.add_dll_directory(bin_path)
    
    import gmatpy as gmat
    print("gmatpy imported successfully")
    
    # Check classes
    mod = gmat.Moderator.Instance()
    print("Moderator initialized:", mod is not None)
    
    # Get available factory classes
    classes = []
    # Test creating Spacecraft
    sc = gmat.Construct("Spacecraft", "MySat")
    print("Spacecraft constructed:", sc is not None)
    if sc:
        prop_count = sc.GetParameterCount()
        print("Spacecraft parameter count:", prop_count)
        for i in range(min(15, prop_count)):
            pname = sc.GetParameterText(i)
            ptype = sc.GetParameterTypeString(i)
            print(f"  [{i}] {pname} ({ptype})")
            
except Exception as e:
    traceback.print_exc()
