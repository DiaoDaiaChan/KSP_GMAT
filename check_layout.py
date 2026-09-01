with open(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\src\KSPGMAT\UI\GMATMainWindow.cs", "r", encoding="utf-8") as f:
    lines = f.readlines()

method_name = None
brace_depth = 0
method_start = 0

methods = []
current_lines = []

for idx, line in enumerate(lines):
    # Method header detection
    clean = line.strip()
    if ("void " in clean or "bool " in clean or "int " in clean or "string " in clean or "GUIStyle " in clean) and "(" in clean and not clean.startswith("//"):
        if brace_depth == 2: # inside class
            method_name = clean
            method_start = idx + 1
            current_lines = []

    if method_name:
        current_lines.append((idx + 1, line))

    for ch in line:
        if ch == '{':
            brace_depth += 1
        elif ch == '}':
            brace_depth -= 1
            if brace_depth == 2 and method_name:
                methods.append((method_name, method_start, current_lines))
                method_name = None
                current_lines = []

print(f"Parsed {len(methods)} methods in GMATMainWindow.cs")

for m_name, m_start, m_lines in methods:
    h_stack = []
    v_stack = []
    s_stack = []
    errors = []
    
    for lno, l in m_lines:
        # Ignore comments
        clean_l = l.split("//")[0]
        
        # Count BeginHorizontal
        for _ in range(clean_l.count("GUILayout.BeginHorizontal")):
            h_stack.append(lno)
        for _ in range(clean_l.count("GUILayout.EndHorizontal")):
            if h_stack:
                h_stack.pop()
            else:
                errors.append(f"Line {lno}: Extra GUILayout.EndHorizontal()")
                
        # Count BeginVertical
        for _ in range(clean_l.count("GUILayout.BeginVertical")):
            v_stack.append(lno)
        for _ in range(clean_l.count("GUILayout.EndVertical")):
            if v_stack:
                v_stack.pop()
            else:
                errors.append(f"Line {lno}: Extra GUILayout.EndVertical()")
                
        # Count BeginScrollView
        for _ in range(clean_l.count("GUILayout.BeginScrollView")):
            s_stack.append(lno)
        for _ in range(clean_l.count("GUILayout.EndScrollView")):
            if s_stack:
                s_stack.pop()
            else:
                errors.append(f"Line {lno}: Extra GUILayout.EndScrollView()")
                
    if h_stack:
        for lno in h_stack:
            errors.append(f"Line {lno}: Unclosed GUILayout.BeginHorizontal()")
    if v_stack:
        for lno in v_stack:
            errors.append(f"Line {lno}: Unclosed GUILayout.BeginVertical()")
    if s_stack:
        for lno in s_stack:
            errors.append(f"Line {lno}: Unclosed GUILayout.BeginScrollView()")
            
    if errors:
        print(f"\n[LAYOUT MISMATCH] {m_name} (start line {m_start}):")
        for err in errors:
            print(f"   - {err}")
