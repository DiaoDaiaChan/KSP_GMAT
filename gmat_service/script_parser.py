"""
GMAT Script Parser & Template Importer.
Parses any GMAT .script into structured JSON for KSP-GMAT Bridge,
including Spacecraft state, Tanks, Thrusters, Burns, Propagators, Equations, and Mission Sequences.
Synchronizes spacecraft epoch with in-game real-time epoch.
"""

import os
import re

def parse_gmat_script(filepath, override_epoch_utc=None, override_craft_name=None):
    """
    Parses a GMAT .script file and extracts:
    - craft: initial state, mass, hardware, epoch (synchronized to current game UT if provided)
    - burns: impulsive and finite burns
    - sequence: list of mission sequence steps including Propagators (with specific names) and Equations
    """
    if not os.path.exists(filepath):
        raise FileNotFoundError(f"Script file not found: {filepath}")

    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()

    # Strip line comments (GMAT % comments)
    clean_lines = []
    for line in content.splitlines():
        idx = line.find("%")
        if idx >= 0:
            line = line[:idx]
        clean_lines.append(line.strip())
    full_clean = "\n".join(clean_lines)

    # 1. Parse Spacecraft
    craft = {}
    sc_match = re.search(r"Create\s+Spacecraft\s+([A-Za-z0-9_]+);", full_clean)
    orig_sat_name = sc_match.group(1) if sc_match else "NRHOSat"
    craft["name"] = override_craft_name if override_craft_name else orig_sat_name

    epoch = re.search(rf"{orig_sat_name}\.Epoch\s*=\s*['\"]([^'\"]+)['\"];", full_clean)
    if override_epoch_utc:
        craft["epoch_utc"] = override_epoch_utc
    elif epoch:
        craft["epoch_utc"] = epoch.group(1)
    else:
        craft["epoch_utc"] = "20 Jul 1988 12:00:00.000"

    coords = re.search(rf"{orig_sat_name}\.CoordinateSystem\s*=\s*([A-Za-z0-9_]+);", full_clean)
    if coords: craft["coord_sys"] = coords.group(1)

    for param in ["X", "Y", "Z", "VX", "VY", "VZ", "DryMass"]:
        val = re.search(rf"{orig_sat_name}\.{param}\s*=\s*([-0-9.]+);", full_clean)
        if val:
            craft[param.lower()] = float(val.group(1))

    # 2. Parse Impulsive Burns
    burns = {}
    burn_matches = re.finditer(r"Create\s+ImpulsiveBurn\s+([A-Za-z0-9_]+);", full_clean)
    for bm in burn_matches:
        bname = bm.group(1)
        b_dict = {
            "name": bname,
            "origin": "Earth",
            "axes": "VNB",
            "dv_v": 0.0,
            "dv_n": 0.0,
            "dv_b": 0.0,
            "isp": 320.0
        }
        orig = re.search(rf"{bname}\.Origin\s*=\s*([A-Za-z0-9_]+);", full_clean)
        if orig: b_dict["origin"] = orig.group(1)

        axes = re.search(rf"{bname}\.Axes\s*=\s*([A-Za-z0-9_]+);", full_clean)
        if axes: b_dict["axes"] = axes.group(1)

        e1 = re.search(rf"{bname}\.Element1\s*=\s*([-0-9.]+);", full_clean)
        e2 = re.search(rf"{bname}\.Element2\s*=\s*([-0-9.]+);", full_clean)
        e3 = re.search(rf"{bname}\.Element3\s*=\s*([-0-9.]+);", full_clean)
        if e1: b_dict["dv_v"] = float(e1.group(1)) * 1000.0 # GMAT km/s -> m/s
        if e2: b_dict["dv_n"] = float(e2.group(1)) * 1000.0
        if e3: b_dict["dv_b"] = float(e3.group(1)) * 1000.0

        isp = re.search(rf"{bname}\.Isp\s*=\s*([-0-9.]+);", full_clean)
        if isp: b_dict["isp"] = float(isp.group(1))

        burns[bname] = b_dict

    # 3. Parse Mission Sequence Line by Line
    sequence = []
    seq_idx = full_clean.find("BeginMissionSequence;")
    if seq_idx >= 0:
        seq_text = full_clean[seq_idx + len("BeginMissionSequence;"):]

        for raw_line in seq_text.splitlines():
            line = raw_line.strip()
            if not line:
                continue

            # Command 1: Propagate 'label' PropName(Sat) {condition};
            if line.startswith("Propagate"):
                label_m = re.search(r"['\"]([^'\"]+)['\"]", line)
                label = label_m.group(1) if label_m else ""

                # Extract propagator function name: LEOProp, CislunarProp, LunarNRHOProp
                prop_m = re.search(r"Propagate\s+(?:['\"][^'\"]+['\"]\s+)?([A-Za-z0-9_]+)\s*\(", line)
                propagator_name = prop_m.group(1) if prop_m else "LEOProp"

                stop_match = re.search(r"\{([^}]+)\}", line)
                stop_cond = stop_match.group(1).strip() if stop_match else ""

                step = {
                    "type": "Propagate",
                    "propagator": propagator_name,
                    "stop_type": "Time",
                    "duration": 3600.0,
                    "target_val": 200.0,
                    "label": label or f"Propagate ({propagator_name})"
                }

                if "ElapsedSecs" in stop_cond:
                    val = re.search(r"ElapsedSecs\s*=\s*([-0-9.]+)", stop_cond)
                    step["stop_type"] = "Time"
                    step["duration"] = float(val.group(1)) if val else 3600.0
                elif "ElapsedDays" in stop_cond:
                    val = re.search(r"ElapsedDays\s*=\s*([-0-9.]+)", stop_cond)
                    step["stop_type"] = "Days"
                    step["duration"] = float(val.group(1)) if val else 1.0
                elif "Apoapsis" in stop_cond:
                    step["stop_type"] = "Apoapsis"
                elif "Periapsis" in stop_cond:
                    step["stop_type"] = "Periapsis"
                elif "Altitude" in stop_cond:
                    val = re.search(r"Altitude\s*=\s*([-0-9.]+)", stop_cond)
                    step["stop_type"] = "Altitude"
                    step["target_val"] = float(val.group(1)) if val else 200.0
                elif "RMAG" in stop_cond:
                    val = re.search(r"RMAG\s*=\s*([-0-9.]+)", stop_cond)
                    step["stop_type"] = "RMAG"
                    step["target_val"] = float(val.group(1)) if val else 42164.0
                elif "TA" in stop_cond:
                    val = re.search(r"TA\s*=\s*([-0-9.]+)", stop_cond)
                    step["stop_type"] = "TA"
                    step["target_val"] = float(val.group(1)) if val else 180.0
                elif "Latitude" in stop_cond:
                    step["stop_type"] = "Node"

                sequence.append(step)

            # Command 2: Maneuver 'label' BurnName(Sat);
            elif line.startswith("Maneuver"):
                label_m = re.search(r"['\"]([^'\"]+)['\"]", line)
                label = label_m.group(1) if label_m else ""

                m = re.search(r"([A-Za-z0-9_]+)\s*\(", line)
                if m:
                    bname = m.group(1)
                    burn_info = burns.get(bname, {"dv_v": 0.0, "dv_n": 0.0, "dv_b": 0.0, "origin": "Earth"})
                    sequence.append({
                        "type": "ImpulsiveBurn",
                        "name": bname,
                        "label": label or bname,
                        "origin": burn_info.get("origin", "Earth"),
                        "dv_v": burn_info["dv_v"],
                        "dv_n": burn_info["dv_n"],
                        "dv_b": burn_info["dv_b"]
                    })

            # Command 3: Equation / State Assignment (Sat.EarthMoonRot.X = -173.0;)
            elif "=" in line and line.endswith(";"):
                m = re.search(r"([A-Za-z0-9_.]+)\s*=\s*([-0-9.]+);", line)
                if m:
                    var_full = m.group(1)
                    val = m.group(2)
                    # Strip spacecraft name prefix if present (NRHOSat.EarthMoonRot.X -> EarthMoonRot.X)
                    var_name = var_full.split(".", 1)[1] if "." in var_full else var_full
                    sequence.append({
                        "type": "Equation",
                        "variable": var_name,
                        "expression": val,
                        "label": f"{var_name} = {val}"
                    })

            elif line.startswith("BeginFiniteBurn"):
                sequence.append({
                    "type": "FiniteBurn",
                    "duration": 60.0,
                    "label": "Finite Burn"
                })

            elif line.startswith("Target"):
                sequence.append({
                    "type": "TargetApoapsis",
                    "target_alt": 35786.0,
                    "label": "Targeter"
                })

    return {
        "success": True,
        "script_path": filepath,
        "script_name": os.path.basename(filepath),
        "craft": craft,
        "burns": burns,
        "sequence": sequence
    }
