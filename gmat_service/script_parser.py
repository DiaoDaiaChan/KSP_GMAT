"""Loss-minimising parser for the GMAT script language used by the bridge."""
import os
import re

_CREATE = re.compile(r"^Create\s+(\w+)\s+(\w+)\s*;?$", re.I)
_ASSIGN = re.compile(r"^([A-Za-z_][\w.]*)\s*=\s*(.*?)\s*;?$", re.S)
_CALL = re.compile(r"^(\w+)\s*(?:'([^']*)'\s*)?([\w]+)(?:\s*\(([^)]*)\))?\s*(?:\{(.*)\})?$", re.S)

def _value(raw):
    raw = raw.strip()
    if raw.startswith("{") and raw.endswith("}"): return [_value(x) for x in raw[1:-1].split(",") if x.strip()]
    if len(raw) >= 2 and raw[0] in "'\"" and raw[-1] == raw[0]: return raw[1:-1]
    low = raw.lower()
    if low in ("true", "on"): return True
    if low in ("false", "off"): return False
    if low == "none": return "None"
    try: return float(raw)
    except ValueError: return raw

def _statements(text):
    result=[]; buf=[]; depth=0; quote=None
    for ch in text:
        if quote:
            buf.append(ch)
            if ch == quote: quote=None
        elif ch in "'\"": quote=ch; buf.append(ch)
        elif ch == "{": depth += 1; buf.append(ch)
        elif ch == "}": depth -= 1; buf.append(ch)
        elif ch == ";" and depth == 0:
            if "".join(buf).strip(): result.append("".join(buf).strip()+";")
            buf=[]
        else: buf.append(ch)
    if "".join(buf).strip(): result.append("".join(buf).strip())
    return result

def parse_gmat_script(filepath, override_epoch_utc=None, override_craft_name=None):
    if not os.path.exists(filepath): raise FileNotFoundError(filepath)
    with open(filepath, encoding="utf-8", errors="ignore") as stream:
        raw=stream.read()
    clean="\n".join(line[:line.find("%")] if "%" in line else line for line in raw.splitlines())
    objects={}; order=[]; spacecraft_name=None
    for statement in _statements(clean):
        m=_CREATE.match(statement.strip())
        if m:
            kind,name=m.groups(); objects[name]={"name":name,"type":kind,"properties":{},"source":statement}; order.append(name)
            if kind.lower()=="spacecraft" and spacecraft_name is None: spacecraft_name=name
            continue
        assignment = re.sub(r"^GMAT\s+", "", statement.strip(), flags=re.I)
        m=_ASSIGN.match(assignment)
        if m:
            path,value=m.groups(); owner,prop=path.split(".",1) if "." in path else (None,path)
            if owner in objects: objects[owner]["properties"][prop]=_value(value)
            else: objects.setdefault("__global__",{"name":"__global__","type":"Global","properties":{},"source":""})["properties"][path]=_value(value)
    craft_obj=objects.get(spacecraft_name or "",{}); craft=dict(craft_obj.get("properties",{})); craft["name"]=override_craft_name or spacecraft_name or ""
    if override_epoch_utc is not None: craft["Epoch"]=override_epoch_utc
    craft["properties"]=craft_obj.get("properties",{})
    sequence=[]; start=clean.find("BeginMissionSequence"); mission=clean[start:] if start>=0 else ""
    for index,statement in enumerate(_statements(mission)):
        s=statement.strip()
        if not s or s.lower().startswith("beginmissionsequence"): continue
        if s.lower().rstrip(";").strip() == "endtarget":
            sequence.append({"index":index,"type":"EndTarget","source":s})
            continue
        call=_CALL.match(s[:-1] if s.endswith(";") else s)
        if call:
            command,label,obj,args,stop=call.groups(); kind={"propagate":"Propagate","maneuver":"ImpulsiveBurn"}.get(command.lower(),command)
            item={"index":index,"type":kind,"label":label or "","object":obj,"spacecraft":(args or "").strip(),"stop":stop.strip() if stop else "","source":s}
            props=objects.get(obj,{}).get("properties",{})
            if kind == "ImpulsiveBurn":
                item.update({"name":obj,"origin":props.get("Origin"),"axes":props.get("Axes"),"coordinate_system":props.get("CoordinateSystem"),"decrement_mass":props.get("DecrementMass",False),"isp":props.get("Isp",0.0),"dv_v":float(props.get("Element1",0))*1000.0,"dv_n":float(props.get("Element2",0))*1000.0,"dv_b":float(props.get("Element3",0))*1000.0})
            elif kind == "Propagate":
                item["propagator"]=obj; condition=item["stop"]
                for field, stop_type in (("ElapsedSecs","Time"),("ElapsedDays","Days")):
                    m=re.search(r"%s\s*=\s*([-+0-9.eE]+)" % field, condition)
                    if m: item.update({"stop_type":stop_type,"duration":float(m.group(1))}); break
                else:
                    item["stop_type"]="Apoapsis" if "Apoapsis" in condition else ("Periapsis" if "Periapsis" in condition else "Event")
                    m=re.search(r"(?:Altitude|RMAG|TA)\s*=\s*([-+0-9.eE]+)", condition)
                    if m: item["target_val"]=float(m.group(1))
            elif kind == "Target":
                item["solve_mode"] = "RunInitialGuess" if re.search(r"SolveMode\s*=\s*RunInitialGuess", item["stop"], re.I) else "Solve"
                item["exit_mode"] = "DiscardAndContinue" if re.search(r"ExitMode\s*=\s*DiscardAndContinue", item["stop"], re.I) else "SaveAndContinue"
            elif kind == "Vary":
                m=re.search(r"([\w.]+)\s*=\s*([-+0-9.eE]+)", item["spacecraft"])
                if m:
                    item["vary_variable"] = m.group(1)
                    item["vary_initial"] = float(m.group(2))
                for key in ("Perturbation","Lower","Upper","MaxStep"):
                    m=re.search(r"%s\s*=\s*([-+0-9.eE]+)" % key, item["spacecraft"], re.I)
                    if m: item[{"Perturbation":"vary_perturbation","Lower":"vary_lower","Upper":"vary_upper","MaxStep":"vary_max_step"}[key]]=float(m.group(1))
            elif kind == "Achieve":
                m=re.search(r"([\w.]+)\s*=\s*([-+0-9.eE]+)", item["spacecraft"])
                if m:
                    item["achieve_param"] = m.group(1)
                    item["achieve_val"] = float(m.group(2))
                m=re.search(r"Tolerance\s*=\s*([-+0-9.eE]+)", item["spacecraft"], re.I)
                if m: item["achieve_tolerance"] = float(m.group(1))
            sequence.append(item)
        elif "=" in s:
            m=_ASSIGN.match(s); sequence.append({"index":index,"type":"Assignment","variable":m.group(1) if m else "","value":_value(m.group(2)) if m else "","source":s})
        else: sequence.append({"index":index,"type":s.split(None,1)[0].rstrip(";"),"source":s})
    return {"success":True,"script_path":os.path.abspath(filepath),"script_name":os.path.basename(filepath),"craft":craft,"objects":[objects[n] for n in order],"sequence":sequence,"source":raw}
