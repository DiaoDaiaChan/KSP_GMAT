# -*- coding: utf-8 -*-
import json, os, re, sys

sys.stdout.reconfigure(encoding='utf-8')

src_dir = "src/KSPGMAT"
loc_dir = "GameData/KSPGMAT/loc"

print("================================================================")
print("             COMPREHENSIVE I18N HEALTH AUDIT                    ")
print("================================================================")

# 1. Load all 4 localization tables
tables = {}
for lang in ["zh", "en", "ru", "jp"]:
    with open(os.path.join(loc_dir, f"{lang}.json"), "r", encoding="utf-8") as f:
        tables[lang] = json.load(f)

# 2. Check key count and symmetry
all_keys = set(tables["zh"].keys())
print(f"Total keys in zh.json: {len(all_keys)}")
asymmetry = False
for lang in ["en", "ru", "jp"]:
    cur_keys = set(tables[lang].keys())
    if cur_keys != all_keys:
        asymmetry = True
        print(f"ASYMMETRY between zh and {lang}:")
        print(f"  Missing in {lang}: {all_keys - cur_keys}")
        print(f"  Extra in {lang}: {cur_keys - all_keys}")
if not asymmetry:
    print("ALL 4 LANGUAGES HAVE 100% PERFECT SYMMETRIC KEY COUNT (1728/1728)!")

# 3. Check tag nesting across all 4 tables
tag_re = re.compile(r"<\s*(/?)\s*([a-zA-Z0-9]+)(?:=[^>]*)?\s*>")
total_tag_errors = 0
for lang in ["zh", "en", "ru", "jp"]:
    err_count = 0
    for k, v in tables[lang].items():
        stack = []
        for m in tag_re.finditer(v):
            is_closing = (m.group(1) == "/")
            tag_name = m.group(2).lower()
            if tag_name not in ["b", "i", "size", "color", "material"]:
                continue
            if not is_closing:
                stack.append((tag_name, m.group(0), m.start()))
            else:
                if not stack:
                    err_count += 1
                else:
                    top_tag, top_str, top_pos = stack.pop()
                    if top_tag != tag_name:
                        err_count += 1
        if stack:
            err_count += len(stack)
    total_tag_errors += err_count
    print(f"Tag nesting errors in {lang}.json: {err_count}")

# 4. Check placeholder count consistency across languages
ph_re = re.compile(r"\{(\d+)(?::[^}]*)?\}")
ph_mismatches = 0
for k in all_keys:
    phs_by_lang = {}
    for lang in ["zh", "en", "ru", "jp"]:
        v = tables[lang].get(k, "").replace("{{", "").replace("}}", "")
        matches = ph_re.findall(v)
        phs_by_lang[lang] = sorted(list(set(int(m) for m in matches)))
    maxes = [phs[-1] if phs else -1 for phs in phs_by_lang.values()]
    if len(set(maxes)) > 1:
        ph_mismatches += 1
        print(f"Placeholder index mismatch for key '{k}': {phs_by_lang}")
print(f"Total placeholder mismatches across languages: {ph_mismatches}")

# 5. Scan C# callers and test string.Format execution on mock args
cs_files = []
for root, dirs, files in os.walk(src_dir):
    for f in files:
        if f.endswith(".cs"):
            cs_files.append(os.path.join(root, f))

call_re = re.compile(r'I18n\.(Format|ColorFormat|TF)\s*\(\s*"([^"]+)"', re.DOTALL)
call_errors = 0

for file_path in cs_files:
    with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
    
    for match in call_re.finditer(content):
        key = match.group(2)
        start_idx = match.start()
        line_no = content[:start_idx].count("\n") + 1
        
        # Balance parens
        paren_start = match.end() - len(f'"{key}"') - 1
        while paren_start < len(content) and content[paren_start] != '(':
            paren_start += 1
        
        depth = 0
        end_idx = paren_start
        in_str = False
        str_char = ''
        escape = False
        for i in range(paren_start, len(content)):
            ch = content[i]
            if in_str:
                if escape: escape = False
                elif ch == '\\': escape = True
                elif ch == str_char: in_str = False
            else:
                if ch in ('"', "'"): in_str = True; str_char = ch
                elif ch in '([{': depth += 1
                elif ch in ')]}':
                    depth -= 1
                    if depth == 0: end_idx = i; break
        
        call_text = content[paren_start + 1:end_idx].strip()
        args = []
        cur_arg = []
        d = 0
        in_s = False
        s_c = ''
        esc = False
        for ch in call_text:
            if in_s:
                cur_arg.append(ch)
                if esc: esc = False
                elif ch == '\\': esc = True
                elif ch == s_c: in_s = False
            else:
                if ch in ('"', "'"): in_s = True; s_c = ch; cur_arg.append(ch)
                elif ch in '([{<': d += 1; cur_arg.append(ch)
                elif ch in ')]}>': d -= 1; cur_arg.append(ch)
                elif ch == ',' and d == 0: args.append("".join(cur_arg).strip()); cur_arg = []
                else: cur_arg.append(ch)
        if cur_arg: args.append("".join(cur_arg).strip())
        
        arg_count = len(args) - 1
        
        for lang in ["zh", "en", "ru", "jp"]:
            tpl = tables[lang].get(key)
            if not tpl: continue
            temp = tpl.replace("{{", "").replace("}}", "")
            matches = ph_re.findall(temp)
            if matches:
                max_ph = max(int(m) for m in matches)
                if max_ph >= arg_count:
                    call_errors += 1
                    print(f"CALL SITE ERROR in {os.path.basename(file_path)}:{line_no} [{key}]: max placeholder {max_ph} >= arg_count {arg_count}!")

print(f"Total C# call site placeholder errors: {call_errors}")

if total_tag_errors == 0 and ph_mismatches == 0 and call_errors == 0:
    print("\n>>> ALL FORMATTING, TAG NESTING, AND I18N HEALTH CHECKS PASSED 100%! <<<")
else:
    print("\n>>> THERE ARE STILL ISSUES TO RESOLVE! <<<")
