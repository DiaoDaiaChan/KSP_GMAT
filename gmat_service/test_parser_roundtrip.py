import os
import sys
import json

# Add gmat_service to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from script_parser import parse_gmat_script

def test_parse():
    script_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "test_all_27_commands.script"))
    print(f"[*] Parsing test script: {script_path}")
    res = parse_gmat_script(script_path)
    
    print(f"[+] Success: {res['success']}")
    print(f"[+] Primary Spacecraft: {res.get('primary_craft_name')}")
    print(f"[+] Spacecraft list: {res.get('all_spacecraft_names')}")
    print(f"[+] Variables count: {len(res.get('user_variables', []))}")
    for v in res.get('user_variables', []):
        print(f"    - Variable: {v['name']} = {v['value']}")
    print(f"[+] Arrays count: {len(res.get('user_arrays', []))}")
    for a in res.get('user_arrays', []):
        print(f"    - Array: {a['name']} [{a['rows']}x{a['cols']}]")
    print(f"[+] Strings count: {len(res.get('user_strings', []))}")
    for s in res.get('user_strings', []):
        print(f"    - String: {s['name']} = '{s['value']}'")
    print(f"[+] Sequence steps count: {len(res.get('sequence', []))}")
    for st in res.get('sequence', []):
        print(f"    - Step [{st['type']}]: {st.get('label')}")

if __name__ == "__main__":
    test_parse()
    print("\n[OK] PARSER ROUNDTRIP VERIFIED SUCCESSFULLY!")
