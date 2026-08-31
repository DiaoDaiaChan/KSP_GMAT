import os
import sys
import json
import subprocess

def test_roundtrip():
    service_dir = os.path.dirname(os.path.abspath(__file__))
    bin_dir = os.path.abspath(os.path.join(service_dir, "..", ".."))
    cli_path = os.path.join(service_dir, "gmat_cli.py")
    output_dir = os.path.join(bin_dir, "output")
    os.makedirs(output_dir, exist_ok=True)

    export_target_script = os.path.join(output_dir, "roundtrip_test.script")
    export_input_json = os.path.join(output_dir, "export_input.json")
    export_output_json = os.path.join(output_dir, "export_output.json")
    import_input_json = os.path.join(output_dir, "import_input.json")
    import_output_json = os.path.join(output_dir, "import_output.json")

    # Step 1: Prepare export payload from GUI
    payload = {
        "export_path": export_target_script,
        "craft": {
            "vessel_name": "MoonSat",
            "central_body": "Earth",
            "ut": 100000.0,
            "x": 6578.14, "y": 0.0, "z": 0.0,
            "vx": 0.0, "vy": 7.784, "vz": 0.0,
            "dry_mass": 1200.0,
            "fuel_mass": 2800.0,
            "user_variables": [
                {"name": "TLIBurnDuration", "value": 638.2, "description": "TLI burn"},
                {"name": "ParkingCoastSecs", "value": 1800.0, "description": "Coast time"},
                {"name": "TCMBurnDuration", "value": 45.0, "description": "TCM burn"},
                {"name": "LOIBurnDuration", "value": 258.4, "description": "LOI burn"}
            ],
            "user_arrays": [
                {"name": "TransMatrix", "rows": 3, "cols": 3, "description": "3x3 Matrix"},
                {"name": "TargetVector", "rows": 3, "cols": 1, "description": "3x1 Vector"}
            ],
            "user_strings": [
                {"name": "MissionPhase", "value": "TLI", "description": "Phase"},
                {"name": "TargetBody", "value": "Luna", "description": "Target body"}
            ]
        },
        "sequence": [
            {"type": "Equation", "variable": "ParkingCoastSecs", "expression": "1800.0", "label": "Set coast"},
            {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 1800.0, "label": "LEO Coast"},
            {"type": "BeginFiniteBurn", "burn_name": "TLIFiniteBurn", "label": "Start TLI Burn"},
            {"type": "Propagate", "propagator": "LEOProp", "stop_type": "Time", "duration": 638.2, "label": "TLI Burn Duration"},
            {"type": "EndFiniteBurn", "burn_name": "TLIFiniteBurn", "label": "Cutoff TLI Burn"},
            {"type": "Target", "dc_name": "DefaultDC", "solve_mode": "Solve", "label": "Targeter loop"},
            {"type": "Vary", "vary_variable": "TLIBurnDuration", "vary_initial": 638.0, "vary_lower": 500.0, "vary_upper": 800.0, "vary_max_step": 5.0, "label": "Vary TLI"},
            {"type": "Achieve", "achieve_param": "Sat.Luna.RMAG", "achieve_val": 1837.4, "achieve_tolerance": 0.5, "label": "Achieve Perilune"},
            {"type": "EndTarget", "label": "End targeter"}
        ]
    }

    with open(export_input_json, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)

    # Step 2: Run CLI Export
    print(f"[*] Step 1: Exporting GUI state to GMAT script via gmat_cli.py --action export...")
    subprocess.run([sys.executable, cli_path, "--action", "export", "--input", export_input_json, "--output", export_output_json], check=True)

    assert os.path.exists(export_target_script), "Exported script file does not exist!"
    with open(export_target_script, "r", encoding="utf-8") as f:
        script_text = f.read()

    print(f"[+] Script successfully generated ({len(script_text.splitlines())} lines).")
    assert "Create Spacecraft Sat;" in script_text or "Create Spacecraft MoonSat;" in script_text
    assert "Create Variable" in script_text
    assert "Create Array" in script_text
    assert "Create String" in script_text

    # Step 3: Simulate User Editing in GMAT Desktop (Modifying values)
    print("\n[*] Step 2: Simulating User modifying parameters in GMAT Desktop...")
    modified_text = script_text.replace("ParkingCoastSecs = 1800.0", "ParkingCoastSecs = 2400.0")
    modified_text = modified_text.replace("TLIBurnDuration = 638.0", "TLIBurnDuration = 645.5")
    modified_text = modified_text.replace("Sat.Luna.RMAG = 1837.4", "Sat.Luna.RMAG = 1937.4")
    
    with open(export_target_script, "w", encoding="utf-8") as f:
        f.write(modified_text)
    print("[+] Saved modified script.")

    # Step 4: Run CLI Import
    print("\n[*] Step 3: Importing modified script back to GUI via gmat_cli.py --action import...")
    import_payload = {
        "script_path": export_target_script,
        "vessel_name": "MoonSat",
        "ut": 100000.0
    }
    with open(import_input_json, "w", encoding="utf-8") as f:
        json.dump(import_payload, f, indent=2)

    subprocess.run([sys.executable, cli_path, "--action", "import", "--input", import_input_json, "--output", import_output_json], check=True)

    assert os.path.exists(import_output_json), "Import output JSON does not exist!"
    with open(import_output_json, "r", encoding="utf-8") as f:
        import_res = json.load(f)

    print(f"[+] Import Success: {import_res.get('success')}")
    print(f"[+] Imported Variables: {len(import_res.get('user_variables', []))}")
    for v in import_res.get('user_variables', []):
        print(f"    - {v['name']} = {v['value']}")
    print(f"[+] Imported Arrays: {len(import_res.get('user_arrays', []))}")
    for a in import_res.get('user_arrays', []):
        print(f"    - {a['name']} [{a['rows']}x{a['cols']}]")
    print(f"[+] Imported Strings: {len(import_res.get('user_strings', []))}")
    for s in import_res.get('user_strings', []):
        print(f"    - {s['name']} = '{s['value']}'")
    print(f"[+] Imported Steps count: {len(import_res.get('sequence', []))}")
    
    # Verify modified values are correctly parsed!
    vary_step = next((st for st in import_res.get('sequence', []) if st.get('type') == 'Vary'), None)
    achieve_step = next((st for st in import_res.get('sequence', []) if st.get('type') == 'Achieve'), None)
    
    assert vary_step is not None, "Vary step missing"
    assert achieve_step is not None, "Achieve step missing"
    
    print(f"[+] Verified Vary initial value: {vary_step.get('vary_initial')} (Expected: 645.5)")
    assert vary_step.get('vary_initial') == 645.5, f"Vary value mismatch: {vary_step.get('vary_initial')}"

    print(f"[+] Verified Achieve goal value: {achieve_step.get('achieve_val')} (Expected: 1937.4)")
    assert achieve_step.get('achieve_val') == 1937.4, f"Achieve value mismatch: {achieve_step.get('achieve_val')}"

    print("\n[OK] FULL BIDIRECTIONAL ROUNDTRIP (EXPORT -> EDIT IN GMAT -> IMPORT TO GUI) TEST PASSED 100%!")

if __name__ == "__main__":
    test_roundtrip()
