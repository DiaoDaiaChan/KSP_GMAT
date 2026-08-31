"""Complete, robust AST-like parser for GMAT script files.
Extracts all GMAT Resources (Spacecraft, Hardware, Burns, Propagators, Coordinate Systems,
Solvers, Outputs, Variables, Arrays, Strings, Event Locators) and all 27 Mission Sequence commands.
"""
import os
import re
import json

_CREATE_MULTI = re.compile(r"^Create\s+([A-Za-z_]\w*)\s+([^;]+);?$", re.I | re.MULTILINE)
_ASSIGN = re.compile(r"^([A-Za-z_][\w.]*(?:\[\s*\d+\s*(?:,\s*\d+\s*)?\])?)\s*=\s*(.*?)\s*;?$", re.S)
_CALL = re.compile(r"^(\w+)\s*(?:'([^']*)'\s*)?([\w.]+)?(?:\s*\(([^)]*)\))?\s*(?:\{(.*)\})?$", re.S)

def _value(raw):
    raw = raw.strip()
    if raw.startswith("{") and raw.endswith("}"):
        items = [x.strip() for x in raw[1:-1].split(",") if x.strip()]
        return [_value(x) for x in items]
    if len(raw) >= 2 and raw[0] in "'\"" and raw[-1] == raw[0]:
        return raw[1:-1]
    low = raw.lower()
    if low in ("true", "on"): return True
    if low in ("false", "off"): return False
    if low == "none": return "None"
    try:
        if "." in raw or "e" in raw.lower():
            return float(raw)
        return int(raw)
    except ValueError:
        return raw

def _split_statements(text):
    result = []
    buf = []
    depth = 0
    quote = None
    in_comment = False
    
    lines = text.splitlines()
    clean_lines = []
    for l in lines:
        stripped = l.strip()
        if stripped.startswith("%") or stripped.startswith("#"):
            continue
        c_idx = l.find("%")
        if c_idx >= 0:
            l = l[:c_idx]
        clean_lines.append(l)
    
    clean_text = "\n".join(clean_lines)
    
    for ch in clean_text:
        if quote:
            buf.append(ch)
            if ch == quote: quote = None
        elif ch in "'\"":
            quote = ch
            buf.append(ch)
        elif ch == "{":
            depth += 1
            buf.append(ch)
        elif ch == "}":
            depth -= 1
            buf.append(ch)
        elif ch == ";" and depth == 0:
            stmt = "".join(buf).strip()
            if stmt: result.append(stmt + ";")
            buf = []
        elif ch == "\n" and depth == 0:
            # Handle block commands without semicolon like If, Else, EndIf, For, EndFor, While, EndWhile
            curr_str = "".join(buf).strip()
            if curr_str:
                first_w = curr_str.split()[0].lower() if curr_str.split() else ""
                if first_w in ("if", "else", "endif", "for", "endfor", "while", "endwhile", "beginscript", "endscript"):
                    result.append(curr_str)
                    buf = []
                else:
                    buf.append(" ")
            else:
                buf = []
        else:
            buf.append(ch)
    
    final_s = "".join(buf).strip()
    if final_s:
        result.append(final_s)
    return result

def parse_gmat_script(filepath, override_epoch_utc=None, override_craft_name=None):
    if not os.path.exists(filepath):
        raise FileNotFoundError(f"Script file not found: {filepath}")
    
    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
        raw_content = f.read()

    # Split into Preamble (Resources) and MissionSequence
    seq_match = re.search(r"BeginMissionSequence\s*;?", raw_content, re.I)
    if seq_match:
        preamble_text = raw_content[:seq_match.start()]
        mission_text = raw_content[seq_match.end():]
    else:
        preamble_text = raw_content
        mission_text = ""

    # Parse Preamble Objects & Resources
    objects = {}
    order = []
    spacecraft_list = []
    tanks_list = []
    thrusters_list = []
    impulsive_burns = []
    finite_burns = []
    propagators = []
    solvers = []
    outputs = []
    coord_systems = []
    variables_map = {}
    arrays_map = {}
    strings_map = {}
    assignments = {}

    for stmt in _split_statements(preamble_text):
        s = stmt.strip()
        if not s: continue
        
        # Check Create statements
        if s.lower().startswith("create "):
            # Create Type Name1 Name2 ... or Create Array Arr[3,3]
            parts = s.split(None, 2)
            if len(parts) >= 3:
                kind = parts[1]
                decls_raw = parts[2].rstrip(";").strip()
                
                # Split declarations by whitespace or comma (respecting [r,c] brackets)
                decl_tokens = []
                b_buf = []; in_b = False
                for ch in decls_raw:
                    if ch == '[': in_b = True; b_buf.append(ch)
                    elif ch == ']': in_b = False; b_buf.append(ch)
                    elif (ch == ',' or ch.isspace()) and not in_b:
                        t_str = "".join(b_buf).strip()
                        if t_str: decl_tokens.append(t_str)
                        b_buf = []
                    else:
                        b_buf.append(ch)
                if "".join(b_buf).strip():
                    decl_tokens.append("".join(b_buf).strip())

                for token in decl_tokens:
                    if kind.lower() == "array" and "[" in token:
                        m_arr = re.match(r"(\w+)\[\s*(\d+)\s*(?:,\s*(\d+)\s*)?\]", token)
                        if m_arr:
                            aname = m_arr.group(1)
                            r_cnt = int(m_arr.group(2))
                            c_cnt = int(m_arr.group(3)) if m_arr.group(3) else 1
                            arrays_map[aname] = {"name": aname, "rows": r_cnt, "cols": c_cnt, "description": f"{r_cnt}x{c_cnt} 数组"}
                            continue
                    elif kind.lower() == "variable":
                        variables_map[token] = {"name": token, "value": 0.0, "description": "标量变量"}
                        continue
                    elif kind.lower() == "string":
                        strings_map[token] = {"name": token, "value": "", "description": "字符串变量"}
                        continue

                    # Generic object creation
                    name = token
                    obj_dict = {"name": name, "type": kind, "properties": {}, "source": s}
                    objects[name] = obj_dict
                    order.append(name)

                    k_low = kind.lower()
                    if k_low == "spacecraft": spacecraft_list.append(name)
                    elif "tank" in k_low: tanks_list.append(name)
                    elif "thruster" in k_low: thrusters_list.append(name)
                    elif k_low == "impulsiveburn": impulsive_burns.append(name)
                    elif k_low == "finiteburn": finite_burns.append(name)
                    elif k_low == "propagator": propagators.append(name)
                    elif k_low in ("differentialcorrector", "sqp", "fminconoptimizer", "vf13ad"): solvers.append(name)
                    elif k_low in ("reportfile", "orbitview", "ephemerisfile"): outputs.append(name)
                    elif k_low == "coordinatesystem": coord_systems.append(name)
            continue

        # Check Assignments (GMAT Object.Property = Value or GMAT Var = Val)
        assignment_s = re.sub(r"^GMAT\s+", "", s, flags=re.I).rstrip(";")
        m_ass = _ASSIGN.match(assignment_s)
        if m_ass:
            lhs = m_ass.group(1).strip()
            rhs_raw = m_ass.group(2).strip()
            rhs_val = _value(rhs_raw)
            
            if "." in lhs:
                owner, prop = lhs.split(".", 1)
                if owner in objects:
                    objects[owner]["properties"][prop] = rhs_val
                elif owner in spacecraft_list:
                    objects.setdefault(owner, {"name": owner, "type": "Spacecraft", "properties": {}})["properties"][prop] = rhs_val
            else:
                # Variable assignment in preamble
                if lhs in variables_map:
                    try: variables_map[lhs]["value"] = float(rhs_val)
                    except: pass
                elif lhs in strings_map:
                    strings_map[lhs]["value"] = str(rhs_val)
                else:
                    assignments[lhs] = rhs_val

    # Resolve Primary Spacecraft
    primary_craft_name = None
    if override_craft_name and override_craft_name in spacecraft_list:
        primary_craft_name = override_craft_name
    elif "Sat" in spacecraft_list:
        primary_craft_name = "Sat"
    elif "MoonSat" in spacecraft_list:
        primary_craft_name = "MoonSat"
    elif spacecraft_list:
        primary_craft_name = spacecraft_list[0]
    else:
        primary_craft_name = "Sat"

    primary_craft = objects.get(primary_craft_name, {}).get("properties", {})
    
    # -------------------------------------------------------------
    # Parse Mission Sequence Statements (All 27 Commands)
    # -------------------------------------------------------------
    sequence = []
    
    for idx, stmt in enumerate(_split_statements(mission_text)):
        s = stmt.strip()
        if not s: continue
        s_clean = s.rstrip(";")

        # 1. ScriptEvent (BeginScript ... EndScript)
        if s.lower().startswith("beginscript"):
            m_sc = re.search(r"beginscript\s*(?:'([^']*)')?(.*?)endscript", s, re.I | re.S)
            label = m_sc.group(1) if (m_sc and m_sc.group(1)) else "脚本事件块"
            content = m_sc.group(2).strip() if m_sc else s
            sequence.append({
                "index": idx,
                "type": "ScriptEvent",
                "label": label,
                "script_content": content,
                "source": s
            })
            continue

        # 2. Control Logic: If, Else, EndIf, While, EndWhile, For, EndFor
        first_w = s_clean.split()[0].lower() if s_clean.split() else ""
        if first_w == "if":
            cond = s_clean[2:].strip()
            sequence.append({"index": idx, "type": "If", "condition_expr": cond, "label": f"If ({cond})", "source": s})
            continue
        elif first_w == "else":
            sequence.append({"index": idx, "type": "Else", "label": "Else 可选分支", "source": s})
            continue
        elif first_w == "endif":
            sequence.append({"index": idx, "type": "EndIf", "label": "EndIf 闭合条件", "source": s})
            continue
        elif first_w == "while":
            cond = s_clean[5:].strip()
            sequence.append({"index": idx, "type": "While", "condition_expr": cond, "label": f"While ({cond})", "source": s})
            continue
        elif first_w == "endwhile":
            sequence.append({"index": idx, "type": "EndWhile", "label": "EndWhile 闭合循环", "source": s})
            continue
        elif first_w == "for":
            m_for = re.search(r"for\s+(\w+)\s*=\s*(.+)", s_clean, re.I)
            f_var = m_for.group(1) if m_for else "i"
            f_range = m_for.group(2) if m_for else "1:1:5"
            sequence.append({"index": idx, "type": "For", "for_var": f_var, "for_range": f_range, "label": f"For {f_var} = {f_range}", "source": s})
            continue
        elif first_w == "endfor":
            sequence.append({"index": idx, "type": "EndFor", "label": "EndFor 闭合循环", "source": s})
            continue

        # 3. Simple Control Commands: Stop, Breakpoint, ClearPlot, PenUp, PenDown, UpdateDynamicData
        if first_w == "stop":
            sequence.append({"index": idx, "type": "Stop", "label": "终止任务序列", "source": s})
            continue
        elif first_w == "breakpoint":
            sequence.append({"index": idx, "type": "Breakpoint", "label": "调试断点", "source": s})
            continue
        elif first_w == "endtarget":
            sequence.append({"index": idx, "type": "EndTarget", "label": "闭合打靶循环", "source": s})
            continue
        elif first_w == "endoptimize":
            sequence.append({"index": idx, "type": "EndOptimize", "label": "闭合优化规划", "source": s})
            continue
        elif first_w in ("clearplot", "penup", "pendown", "updatedynamicdata"):
            sub_name = s_clean.split(None, 1)[1].strip() if len(s_clean.split()) > 1 else "EarthInertialView"
            c_type = {"clearplot": "ClearPlot", "penup": "PenUp", "pendown": "PenDown", "updatedynamicdata": "UpdateDynamicData"}[first_w]
            sequence.append({"index": idx, "type": c_type, "subscriber_name": sub_name, "label": f"{c_type} ({sub_name})", "source": s})
            continue
        elif first_w == "commandecho":
            e_state = s_clean.split(None, 1)[1].strip() if len(s_clean.split()) > 1 else "On"
            sequence.append({"index": idx, "type": "CommandEcho", "echo_state": e_state, "label": f"CommandEcho {e_state}", "source": s})
            continue
        elif first_w == "toggle":
            # Toggle ViewName On/Off
            parts = s_clean.split()
            v_name = parts[1] if len(parts) > 1 else "EarthInertialView"
            t_state = parts[2] if len(parts) > 2 else "Off"
            sequence.append({"index": idx, "type": "Toggle", "subscriber_name": v_name, "action_state": t_state, "label": f"Toggle {v_name} {t_state}", "source": s})
            continue
        elif first_w == "global":
            objs_s = s_clean[6:].strip()
            sequence.append({"index": idx, "type": "Global", "global_objects": objs_s, "label": f"Global {objs_s}", "source": s})
            continue
        elif first_w == "set":
            m_set = re.search(r"set\s+([\w.]+)\s*=\s*(.+)", s_clean, re.I)
            if m_set:
                sequence.append({"index": idx, "type": "Set", "set_target": m_set.group(1), "set_expr": m_set.group(2), "label": f"Set {m_set.group(1)}", "source": s})
                continue

        # 4. Standard Function / Command Calls (Propagate, Maneuver, BeginFiniteBurn, Target, Optimize, Achieve, Vary, Minimize, NonlinearConstraint, Report, Write, FindEvents, MarkPoint)
        call = _CALL.match(s_clean)
        if call:
            cmd, label, obj, args, options = call.groups()
            cmd_low = cmd.lower()
            label = label or ""
            obj = obj or ""
            args = (args or "").strip()
            options = (options or "").strip()

            if cmd_low == "propagate":
                prop_step = {
                    "index": idx,
                    "type": "Propagate",
                    "label": label or f"轨道积分 ({obj})",
                    "propagator": obj,
                    "spacecraft": args or primary_craft_name,
                    "stop": options,
                    "source": s
                }
                # Parse stop conditions
                if "ElapsedSecs" in options:
                    m_dur = re.search(r"ElapsedSecs\s*=\s*([-+0-9.eE]+)", options)
                    if m_dur: prop_step.update({"stop_type": "Time", "duration": float(m_dur.group(1))})
                elif "ElapsedDays" in options:
                    m_dur = re.search(r"ElapsedDays\s*=\s*([-+0-9.eE]+)", options)
                    if m_dur: prop_step.update({"stop_type": "Days", "duration": float(m_dur.group(1)) * 86400.0})
                elif "Apoapsis" in options:
                    prop_step["stop_type"] = options if "." in options else f"Sat.Earth.Apoapsis"
                elif "Periapsis" in options:
                    prop_step["stop_type"] = options if "." in options else f"Sat.Earth.Periapsis"
                else:
                    prop_step["stop_type"] = options or "Time"
                    m_val = re.search(r"=\s*([-+0-9.eE]+)", options)
                    if m_val: prop_step["target_val"] = float(m_val.group(1))
                sequence.append(prop_step)
                continue

            elif cmd_low == "maneuver":
                m_props = objects.get(obj, {}).get("properties", {})
                sequence.append({
                    "index": idx,
                    "type": "ImpulsiveBurn",
                    "label": label or f"脉冲机动 ({obj})",
                    "burn_name": obj,
                    "burn_origin": m_props.get("Origin", "Earth"),
                    "burn_axes": m_props.get("Axes", "VNB"),
                    "burn_coordinate_system": m_props.get("CoordinateSystem", "Local"),
                    "burn_isp": float(m_props.get("Isp", 300.0) or 300.0),
                    "burn_decrement_mass": bool(m_props.get("DecrementMass", False)),
                    "dv_v": float(m_props.get("Element1", 0.0) or 0.0) * 1000.0,
                    "dv_n": float(m_props.get("Element2", 0.0) or 0.0) * 1000.0,
                    "dv_b": float(m_props.get("Element3", 0.0) or 0.0) * 1000.0,
                    "source": s
                })
                continue

            elif cmd_low == "beginfiniteburn":
                sequence.append({"index": idx, "type": "BeginFiniteBurn", "burn_name": obj, "label": label or f"开启有限点火 ({obj})", "source": s})
                continue
            elif cmd_low == "endfiniteburn":
                sequence.append({"index": idx, "type": "EndFiniteBurn", "burn_name": obj, "label": label or f"关闭有限点火 ({obj})", "source": s})
                continue
            elif cmd_low == "beginfilethrust":
                sequence.append({"index": idx, "type": "BeginFileThrust", "file_thrust_name": obj, "label": label or f"开启文件推力 ({obj})", "source": s})
                continue
            elif cmd_low == "endfilethrust":
                sequence.append({"index": idx, "type": "EndFileThrust", "file_thrust_name": obj, "label": label or f"结束文件推力 ({obj})", "source": s})
                continue

            elif cmd_low == "target":
                s_mode = "Solve"
                if re.search(r"SolveMode\s*=\s*RunInitialGuess", options, re.I): s_mode = "RunInitialGuess"
                sequence.append({"index": idx, "type": "Target", "dc_name": obj, "solve_mode": s_mode, "label": label or f"Target 打靶组 ({obj})", "source": s})
                continue

            elif cmd_low == "vary":
                m_var = re.search(r"([\w.]+)\s*=\s*([-+0-9.eE]+)", args)
                v_name = m_var.group(1) if m_var else "burntime"
                v_init = float(m_var.group(2)) if m_var else 100.0
                v_step = {
                    "index": idx, "type": "Vary", "label": label or f"Vary ({v_name})",
                    "vary_variable": v_name, "vary_initial": v_init, "dc_name": obj, "source": s
                }
                for key in ("Perturbation", "Lower", "Upper", "MaxStep"):
                    m_k = re.search(r"%s\s*=\s*([-+0-9.eE]+)" % key, args, re.I)
                    if m_k:
                        v_step[{"Perturbation": "vary_perturbation", "Lower": "vary_lower", "Upper": "vary_upper", "MaxStep": "vary_max_step"}[key]] = float(m_k.group(1))
                sequence.append(v_step)
                continue

            elif cmd_low == "achieve":
                m_ach = re.search(r"([\w.]+)\s*=\s*([-+0-9.eE]+)", args)
                a_param = m_ach.group(1) if m_ach else "Sat.RMAG"
                a_val = float(m_ach.group(2)) if m_ach else 42164.0
                a_step = {
                    "index": idx, "type": "Achieve", "label": label or f"Achieve ({a_param})",
                    "achieve_param": a_param, "achieve_val": a_val, "dc_name": obj, "source": s
                }
                m_tol = re.search(r"Tolerance\s*=\s*([-+0-9.eE]+)", args, re.I)
                if m_tol: a_step["achieve_tolerance"] = float(m_tol.group(1))
                sequence.append(a_step)
                continue

            elif cmd_low == "optimize":
                sequence.append({"index": idx, "type": "Optimize", "optimizer_name": obj, "label": label or f"Optimize 优化器 ({obj})", "source": s})
                continue
            elif cmd_low == "minimize":
                # Minimize SQP1(Sat.FuelMassBurned)
                min_var = args if args else "Sat.FuelMassBurned"
                sequence.append({"index": idx, "type": "Minimize", "optimizer_name": obj, "minimize_variable": min_var, "label": f"Minimize ({min_var})", "source": s})
                continue
            elif cmd_low == "nonlinearconstraint":
                # NonlinearConstraint SQP1(Sat.Luna.RMAG >= 1937.4)
                m_con = re.search(r"([\w.]+)\s*(<=|>=|==|<|>)\s*([-+0-9.eE]+)", args)
                lhs = m_con.group(1) if m_con else "Sat.Luna.RMAG"
                op = m_con.group(2) if m_con else ">="
                val = float(m_con.group(3)) if m_con else 1937.4
                sequence.append({"index": idx, "type": "NonlinearConstraint", "optimizer_name": obj, "constraint_lhs": lhs, "constraint_op": op, "constraint_val": val, "label": f"约束 ({lhs} {op} {val})", "source": s})
                continue

            elif cmd_low == "report":
                sequence.append({"index": idx, "type": "Report", "subscriber_name": obj, "write_fields": args, "label": label or f"Report ({obj})", "source": s})
                continue
            elif cmd_low == "write":
                sequence.append({"index": idx, "type": "Write", "write_fields": f"{obj} {args}".strip(), "label": "Write 消息流写入", "source": s})
                continue
            elif cmd_low == "findevents":
                sequence.append({"index": idx, "type": "FindEvents", "event_locator": obj, "label": label or f"FindEvents ({obj})", "source": s})
                continue
            elif cmd_low == "markpoint":
                sequence.append({"index": idx, "type": "MarkPoint", "mark_label": label or "Mark", "subscriber_name": obj, "label": f"MarkPoint ({label})", "source": s})
                continue

        # 5. Equation / Assignment in sequence (GMAT TLIBurnDuration = 638.2;)
        m_seq_ass = _ASSIGN.match(re.sub(r"^GMAT\s+", "", s_clean, flags=re.I))
        if m_seq_ass:
            sequence.append({
                "index": idx,
                "type": "Equation",
                "variable": m_seq_ass.group(1),
                "expression": m_seq_ass.group(2),
                "label": f"赋值 {m_seq_ass.group(1)} = {m_seq_ass.group(2)}",
                "source": s
            })
            continue

        # 6. Fallback: Generic Command / ScriptEvent
        sequence.append({
            "index": idx,
            "type": "ScriptEvent",
            "script_content": s,
            "label": s_clean.split()[0] if s_clean.split() else "原生代码块",
            "source": s
        })

    # Convert User Resources to output list
    user_vars_list = list(variables_map.values())
    user_arrays_list = list(arrays_map.values())
    user_strings_list = list(strings_map.values())

    return {
        "success": True,
        "script_path": os.path.abspath(filepath),
        "script_name": os.path.basename(filepath),
        "primary_craft_name": primary_craft_name,
        "all_spacecraft_names": spacecraft_list,
        "craft": primary_craft,
        "objects": [objects[n] for n in order],
        "user_variables": user_vars_list,
        "user_arrays": user_arrays_list,
        "user_strings": user_strings_list,
        "tanks": tanks_list,
        "thrusters": thrusters_list,
        "propagators": propagators,
        "solvers": solvers,
        "outputs": outputs,
        "coord_systems": coord_systems,
        "sequence": sequence,
        "source": raw_content
    }

