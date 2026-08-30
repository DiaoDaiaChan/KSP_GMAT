"""
GMAT Engine execution module.
Interacts with GMAT R2026a via gmatpy to assemble mission scripts, execute propagation and maneuver solvers,
and extract results into structured data.
"""

import os
import math
import re
import sys
import tempfile
import traceback
from gmat_paths import resolve_gmat_root

# Ensure GMAT bin directory is in sys.path
BIN_DIR = resolve_gmat_root(os.path.dirname(os.path.abspath(__file__)))
if BIN_DIR not in sys.path:
    sys.path.insert(0, BIN_DIR)

try:
    import gmatpy as gmat
except ImportError:
    # The service can still expose configuration and parsing endpoints without GMAT.
    gmat = None
from ephemeris_calibrator import map_body_name, ksp_ut_to_jd, jd_to_utc_gregorian

_gmat_initialized = False

def init_gmat():
    """Initializes GMAT API singleton if not already initialized."""
    global _gmat_initialized
    if gmat is None:
        raise RuntimeError("gmatpy is unavailable; install GMAT Python bindings for execution")
    if not _gmat_initialized:
        startup_file = os.path.join(BIN_DIR, "gmat_startup_file.txt")
        gmat.Setup(startup_file)
        _gmat_initialized = True
        print("[GmatEngine] GMAT API initialized successfully.")

def get_coord_system_for_body(body_name):
    """Returns appropriate GMAT coordinate system for central body."""
    body = map_body_name(body_name)
    if body == "Earth":
        return "EarthMJ2000Eq"
    elif body == "Luna":
        return "LunaMJ2000Eq"
    elif body == "Mars":
        return "MarsMJ2000Eq"
    elif body == "Sun":
        return "SolarSystemBarycenter"
    else:
        return f"{body}MJ2000Eq"

def sanitize_gmat_identifier(raw_name, prefix="Sat"):
    """Sanitizes any vessel/engine name into a strict, valid GMAT ASCII identifier [A-Za-z][A-Za-z0-9_]*."""
    import re
    clean = re.sub(r'[^a-zA-Z0-9_]', '_', str(raw_name or ""))
    clean = re.sub(r'_+', '_', clean).strip('_')
    if not clean or not clean[0].isalpha():
        return f"{prefix}_{clean or '1'}"
    return clean

def sanitize_gmat_label(raw_label, fallback="Step"):
    """Keep UI labels readable while ensuring GMAT scripts remain ASCII-only."""
    ascii_label = str(raw_label or "").encode("ascii", "ignore").decode("ascii")
    ascii_label = re.sub(r"[^A-Za-z0-9 _-]+", " ", ascii_label).strip()
    return ascii_label or fallback

def required_number(data, key, context="craft"):
    if key not in data or data[key] is None or str(data[key]).strip() == "":
        raise ValueError("missing required %s parameter: %s" % (context, key))
    return float(data[key])

def dc_vary_line(step, burn_name, element, guess, lower=None, upper=None, max_step=None, prefix=""):
    perturbation = float(step.get("vary_perturbation") or 0.0001)
    def option(name, value):
        return f", {name} = {float(value):.8g}" if value is not None and float(value) != 0.0 else ""
    raw_lower = step.get(f"{prefix}vary_lower")
    raw_upper = step.get(f"{prefix}vary_upper")
    raw_max_step = step.get(f"{prefix}vary_max_step")
    lower = raw_lower if raw_lower not in (None, 0, 0.0) else lower
    upper = raw_upper if raw_upper not in (None, 0, 0.0) else upper
    max_step = raw_max_step if raw_max_step not in (None, 0, 0.0) else max_step
    return f"  Vary DC({burn_name}.{element} = {guess:.4f}, {{Perturbation = {perturbation:.8g}{option('Lower', lower)}{option('Upper', upper)}{option('MaxStep', max_step)}}});"

def dc_vary_variable_line(step, variable_name, guess, lower, upper, max_step, hard_lower=None, hard_upper=None):
    perturbation = float(step.get("vary_perturbation") or 0.01)
    if float(step.get("vary_lower") or 0.0) != 0.0: lower = float(step["vary_lower"])
    if float(step.get("vary_upper") or 0.0) != 0.0: upper = float(step["vary_upper"])
    if float(step.get("vary_max_step") or 0.0) != 0.0: max_step = float(step["vary_max_step"])
    if hard_lower is not None: lower = max(lower, float(hard_lower))
    if hard_upper is not None: upper = min(upper, float(hard_upper))
    return f"  Vary DC({variable_name} = {guess:.8g}, {{Perturbation = {perturbation:.8g}, Lower = {lower:.8g}, Upper = {upper:.8g}, MaxStep = {max_step:.8g}}});"

def dc_tolerance(step, fallback):
    return float(step.get("target_tolerance")) if float(step.get("target_tolerance") or 0.0) > 0.0 else fallback

def dc_vz_tolerance_option(step):
    value = float(step.get("vz_tolerance") or 0.0)
    return f", {{Tolerance = {value:.8g}}}" if value > 0.0 else ""

def check_propellant_feasibility(craft_data, mission_sequence):
    """
    Checks if active vessel has sufficient propellant to reach targeted apogee/orbit.
    Returns a human-readable Chinese warning/error string if physically infeasible, or None if feasible.
    """
    dry_mass = float(craft_data.get("dry_mass", 0.0) or 0.0)
    fuel_mass = float(craft_data.get("fuel_mass", 0.0) or 0.0)
    thrust = float(craft_data.get("thrust", 0.0) or 0.0)
    isp = float(craft_data.get("isp", 0.0) or 0.0)
    if dry_mass <= 0.0 or fuel_mass <= 0.0 or thrust <= 0.0 or isp <= 0.0:
        return None

    g0 = 9.81
    mdot = thrust / (isp * g0)
    max_burn_sec = fuel_mass / mdot
    max_dv = isp * g0 * math.log((dry_mass + fuel_mass) / dry_mass)

    for step in mission_sequence:
        if step.get("type") in ("TargetApoapsis", "Targeter") and step.get("target_goal") in ("Apoapsis", "ApoapsisVZ0"):
            target_alt = float(step.get("target_alt") or step.get("target_val") or 0.0)
            try:
                x, y, z = float(craft_data["x"]), float(craft_data["y"]), float(craft_data["z"])
                vx, vy, vz = float(craft_data["vx"]), float(craft_data["vy"]), float(craft_data["vz"])
            except (KeyError, ValueError, TypeError):
                continue
            r0 = math.sqrt(x * x + y * y + z * z)
            v0 = math.sqrt(vx * vx + vy * vy + vz * vz)
            mu = 398600.4418 # km^3/s^2 for Earth
            r_body = 6378.1363
            r_target = r_body + target_alt
            if r_target > r0:
                a_req = (r0 + r_target) / 2.0
                v_peri_req = math.sqrt(mu * (2.0 / r0 - 1.0 / a_req))
                dv_req = (v_peri_req - v0) * 1000.0
                if dv_req > max_dv * 1.08:
                    v_peri_max = v0 + (max_dv / 1000.0)
                    inv_a = 2.0 / r0 - (v_peri_max * v_peri_max) / mu
                    if inv_a > 0:
                        a_max = 1.0 / inv_a
                        r_apo_max = 2.0 * a_max - r0
                        alt_max = max(0.0, r_apo_max - r_body)
                    else:
                        alt_max = 999999.0
                    return (
                        f"飞行器推进能力不足以进入目标轨道：目标远拱点 {target_alt:.0f} km 需要约 {dv_req:.0f} m/s ΔV，"
                        f"当前推进剂仅支持最多 {max_dv:.0f} m/s（最大燃烧 {max_burn_sec:.1f} 秒，"
                        f"最高远拱点约为 {alt_max:.0f} km）。请降低打靶远拱点高度，或为飞船配备具有足够燃料的运载级。"
                    )
    return None

def resolve_gmat_target_param(sat_name: str, central_body: str, goal_param: str) -> str:
    """
    Standardize and resolve GMAT Spacecraft parameter references for Achieve, Vary, and constraints.
    Prevents invalid parameter generation such as 'Sat.Earth.Z' or 'Sat.Earth.INC',
    while correctly preserving body-dependent coordinates like 'Sat.Earth.Altitude' and 'Sat.Luna.RMAG'.
    """
    p = (goal_param or "RMAG").strip()

    # Map Apoapsis/Periapsis events to numeric Altitude parameter in Achieve
    if p.endswith(".Apoapsis") or p == "Apoapsis":
        body = p.split(".")[0] if ("." in p and not p.startswith("Apoapsis") and p.split(".")[0] not in (sat_name, "Sat", "Vessel")) else central_body
        return f"{sat_name}.{body}.Altitude"
    if p.endswith(".Periapsis") or p == "Periapsis":
        body = p.split(".")[0] if ("." in p and not p.startswith("Periapsis") and p.split(".")[0] not in (sat_name, "Sat", "Vessel")) else central_body
        return f"{sat_name}.{body}.Altitude"
    # Strip any existing spacecraft prefix (e.g. Sat., Vessel., Sat_1.)
    for sp in [sat_name + ".", "Sat.", "Vessel."]:
        if p.startswith(sp):
            p = p[len(sp):]
            break

    # Parameters that CANNOT take a CelestialBody qualifier in GMAT
    # (they are Cartesian coordinates, CoordinateSystem angles, or intrinsic satellite properties)
    no_body_params = {
        "X", "Y", "Z", "VX", "VY", "VZ", "AX", "AY", "AZ",
        "INC", "AOP", "RAAN", "VMAG", "Energy", "C3Energy",
        "TotalMass", "DryMass", "ElapsedSecs", "ElapsedDays"
    }

    # Parameters that explicitly require a CelestialBody qualifier in GMAT (e.g. Sat.Earth.TA)
    require_body_params = {
        "TA", "MA", "EA", "HA", "TrueAnomaly", "MeanAnomaly", "EccentricAnomaly"
    }

    # Celestial bodies known in GMAT solar system
    known_bodies = {
        "Earth", "Luna", "Moon", "Sun", "Jupiter", "Mars", "Venus",
        "Saturn", "Mercury", "Uranus", "Neptune", "Pluto"
    }

    if "." in p:
        prefix, base = p.split(".", 1)
        # If user wrote e.g. "Earth.Z", "Earth.INC", strip the invalid body prefix!
        if prefix in known_bodies and base.upper() in {x.upper() for x in no_body_params}:
            return f"{sat_name}.{base}"
        else:
            # Valid coordinate system or body reference (e.g. "Earth.Altitude", "Luna.RMAG", "EarthMJ2000Eq.Z")
            return f"{sat_name}.{p}"
    else:
        # No dot specified (e.g. user typed "Z", "INC", "Altitude", "RMAG")
        upper_p = p.upper()
        if any(upper_p == nb.upper() for nb in no_body_params):
            return f"{sat_name}.{p}"
        elif any(upper_p == rb.upper() for rb in require_body_params):
            return f"{sat_name}.{central_body}.{p}"
        else:
            # Orbital parameters like Altitude, RMAG, SMA, ECC, RadApo, RadPer
            return f"{sat_name}.{central_body}.{p}"

def default_propagator(step=None, central_body="Earth"):
    if step and step.get("propagator"):
        return step.get("propagator")
    if step:
        stop_t = str(step.get("stop_type", "")).lower()
        b_orig = str(step.get("burn_origin", "")).lower()
        if "luna" in stop_t or "moon" in stop_t or "luna" in b_orig or "moon" in b_orig:
            return "LunarNRHOProp"
        if "cislunar" in stop_t or "sun" in stop_t:
            return "CislunarProp"
    if central_body and ("luna" in central_body.lower() or "moon" in central_body.lower()):
        return "LunarNRHOProp"
    return "LEOProp"

def build_gmat_script(craft_data, mission_sequence, report_path, propagator_configs=None, custom_coordinate_systems=None, solver_configs=None):
    """
    Builds a complete, valid GMAT script (.script).
    craft_data: dict with spacecraft parameters, state vector, staging, etc.
    mission_sequence: list of steps (Propagate, FiniteBurn, Targeter).
    report_path: output path for trajectory data.
    propagator_configs: optional dict of custom integrator and force model configs.
    """
    sat_name = "Sat"
    central_body = map_body_name(craft_data.get("central_body", "Earth"))
    coord_sys = get_coord_system_for_body(central_body)
    epoch_str = craft_data.get("epoch_utc")
    if not epoch_str and "ut" in craft_data:
        try:
            from ephemeris_calibrator import ksp_ut_to_jd, jd_to_utc_gregorian
            ut = float(craft_data["ut"])
            jd = ksp_ut_to_jd(ut)
            epoch_str = jd_to_utc_gregorian(jd)
            craft_data["epoch_utc"] = epoch_str
        except Exception as ex:
            print(f"[gmat_engine] UT to epoch_utc conversion warning: {ex}")
    if not epoch_str: raise ValueError("missing required craft parameter: epoch_utc or ut")

    # State vectors in km and km/s
    x = required_number(craft_data, "x"); y = required_number(craft_data, "y"); z = required_number(craft_data, "z")
    vx = required_number(craft_data, "vx"); vy = required_number(craft_data, "vy"); vz = required_number(craft_data, "vz")

    dry_mass = required_number(craft_data, "dry_mass")
    fuel_mass = float(craft_data.get("fuel_mass", 0.0))
    fuel_density = float(craft_data.get("fuel_density", 0.0))
    tank_pressure = float(craft_data.get("tank_pressure", 0.0))
    tank_volume = float(craft_data.get("tank_volume", 0.0))
    thrust = float(craft_data.get("thrust", 0.0))
    isp = float(craft_data.get("isp", 0.0))
    # KSP telemetry can round tank volume more aggressively than mass and
    # density.  Keep the live values authoritative while ensuring GMAT does
    # not reject a mathematically equivalent rounded input.
    if fuel_mass > 0.0 and fuel_density > 0.0:
        required_volume = fuel_mass / fuel_density
        if tank_volume < required_volume:
            tank_volume = math.nextafter(required_volume, math.inf)
    raw_engine_name = craft_data.get("engine_name")
    ascii_eng = "".join(c for c in raw_engine_name if c.isascii() and (c.isalnum() or c == '_')) if raw_engine_name else ""
    if not ascii_eng:
        engine_name = ""
    elif not ascii_eng[0].isalpha():
        engine_name = "Engine_" + ascii_eng
    else:
        engine_name = ascii_eng

    lines = []
    lines.append("% -------------------------------------------------------------")
    lines.append("% Auto-generated by KSP-GMAT Bridge (RO / RealFuels Integrated)")
    lines.append("% -------------------------------------------------------------")
    lines.append("")

    # Spacecraft definition
    lines.append(f"Create Spacecraft {sat_name};")
    lines.append(f"{sat_name}.DateFormat = UTCGregorian;")
    lines.append(f"{sat_name}.Epoch = '{epoch_str}';")
    lines.append(f"{sat_name}.CoordinateSystem = {coord_sys};")
    lines.append(f"{sat_name}.DisplayStateType = Cartesian;")
    lines.append(f"{sat_name}.X = {x};")
    lines.append(f"{sat_name}.Y = {y};")
    lines.append(f"{sat_name}.Z = {z};")
    lines.append(f"{sat_name}.VX = {vx};")
    lines.append(f"{sat_name}.VY = {vy};")
    lines.append(f"{sat_name}.VZ = {vz};")
    lines.append(f"{sat_name}.DryMass = {dry_mass};")

    # Hardware: Multi-Tank and Multi-Thruster Propulsion Architecture (RealismOverhaul Staging)
    tanks_data = craft_data.get("tanks", [])
    thrusters_data = craft_data.get("thrusters", [])

    created_tank_names = []
    created_thruster_names = []

    # 1. Chemical Tanks
    if tanks_data:
        for t in tanks_data:
            tname = sanitize_gmat_identifier(t.get("name"), "MainTank")
            fmass = float(t.get("fuel_mass", 0.0))
            fdens = float(t.get("fuel_density") or fuel_density or 1000.0)
            calc_v = (fmass / fdens) if (fmass > 0 and fdens > 0) else 1.0
            raw_v = float(t.get("volume") or calc_v)
            # Ensure tank volume always has a safe 10% ullage headroom so GMAT never throws capacity overflow
            fvol = max(raw_v, calc_v * 1.10 + 0.1)
            if tname not in created_tank_names:
                created_tank_names.append(tname)
                lines.append(f"Create ChemicalTank {tname};")
                lines.append(f"{tname}.AllowNegativeFuelMass = true;")
                lines.append(f"{tname}.FuelMass = {fmass:.2f};")
                lines.append(f"{tname}.FuelDensity = {fdens:.2f};")
                lines.append(f"{tname}.Temperature = 20;")
                lines.append(f"{tname}.RefTemperature = 20;")
                lines.append(f"{tname}.Pressure = {max(1500.0, tank_pressure):.1f};")
                lines.append(f"{tname}.Volume = {fvol:.6g};")
                lines.append(f"{tname}.PressureModel = 'PressureRegulated';")
    else:
        # Fallback single tank
        has_hardware = bool(engine_name) and fuel_density > 0.0 and tank_volume > 0.0 and thrust > 0.0 and isp > 0.0
        if has_hardware:
            created_tank_names.append("MainTank")
            lines.append("Create ChemicalTank MainTank;")
            lines.append("MainTank.AllowNegativeFuelMass = true;")
            lines.append(f"MainTank.FuelMass = {fuel_mass:.2f};")
            lines.append(f"MainTank.FuelDensity = {fuel_density:.2f};")
            lines.append("MainTank.Temperature = 20;")
            lines.append("MainTank.RefTemperature = 20;")
            lines.append(f"MainTank.Pressure = {max(1500.0, tank_pressure):.1f};")
            calc_fb_v = (fuel_mass / fuel_density) if (fuel_mass > 0 and fuel_density > 0) else 1.0
            safe_fb_v = max(float(tank_volume or calc_fb_v), calc_fb_v * 1.10 + 0.1)
            lines.append(f"MainTank.Volume = {safe_fb_v:.4f};")
            lines.append("MainTank.PressureModel = 'PressureRegulated';")

    # 2. Chemical Thrusters
    if thrusters_data:
        for th in thrusters_data:
            thname = sanitize_gmat_identifier(th.get("name"), "MainEngine")
            th_thrust = float(th.get("thrust") or thrust or 3000.0)
            th_isp = float(th.get("isp") or isp or 316.0)
            th_bound_tank = th.get("bound_tank")
            if th_bound_tank and th_bound_tank not in created_tank_names:
                # Try matching by sanitized id or substring
                san_th_tank = sanitize_gmat_identifier(th_bound_tank, "Tank")
                matched = None
                for ctk in created_tank_names:
                    if san_th_tank in ctk or ctk in san_th_tank:
                        matched = ctk
                        break
                if matched:
                    th_bound_tank = matched
                else:
                    th_bound_tank = None

            if not th_bound_tank:
                # Auto-select the tank with the largest fuel mass
                best_tk = created_tank_names[0] if created_tank_names else "MainTank"
                best_fuel = -1.0
                for tk_info, tk_nm in zip(craft_data.get("tanks", []), created_tank_names):
                    fm = float(tk_info.get("fuel_mass", 0.0) or 0.0)
                    if fm > best_fuel:
                        best_fuel = fm
                        best_tk = tk_nm
                th_bound_tank = best_tk
            
            if thname not in created_thruster_names:
                created_thruster_names.append(thname)
                lines.append(f"Create ChemicalThruster {thname};")
                lines.append(f"{thname}.CoordinateSystem = Local;")
                lines.append(f"{thname}.Origin = {central_body};")
                lines.append(f"{thname}.Axes = VNB;")
                lines.append(f"{thname}.ThrustDirection1 = 1;")
                lines.append(f"{thname}.ThrustDirection2 = 0;")
                lines.append(f"{thname}.ThrustDirection3 = 0;")
                lines.append(f"{thname}.DutyCycle = 1.0;")
                lines.append(f"{thname}.ThrustScaleFactor = 1.0;")
                lines.append(f"{thname}.DecrementMass = true;")
                lines.append(f"{thname}.Tank = {{{th_bound_tank}}};")
                lines.append(f"{thname}.GravitationalAccel = 9.81;")
                # Thrust in craft_data["thrusters"] is in Newtons (N)
                th_c1_newtons = float(th_thrust)
                lines.append(f"{thname}.C1 = {th_c1_newtons:.2f};")
                lines.append(f"{thname}.K1 = {th_isp:.2f};")
    elif created_tank_names and bool(engine_name):
        eng_id = sanitize_gmat_identifier(engine_name, "MainEngine")
        created_thruster_names.append(eng_id)
        lines.append(f"Create ChemicalThruster {eng_id};")
        lines.append(f"{eng_id}.CoordinateSystem = Local;")
        lines.append(f"{eng_id}.Origin = {central_body};")
        lines.append(f"{eng_id}.Axes = VNB;")
        lines.append(f"{eng_id}.ThrustDirection1 = 1;")
        lines.append(f"{eng_id}.ThrustDirection2 = 0;")
        lines.append(f"{eng_id}.ThrustDirection3 = 0;")
        lines.append(f"{eng_id}.DutyCycle = 1.0;")
        lines.append(f"{eng_id}.ThrustScaleFactor = 1.0;")
        lines.append(f"{eng_id}.DecrementMass = true;")
        lines.append(f"{eng_id}.Tank = {{{created_tank_names[0]}}};")
        lines.append(f"{eng_id}.GravitationalAccel = 9.81;")
        eng_c1_newtons = thrust * 1000.0 if thrust < 50000.0 else thrust
        lines.append(f"{eng_id}.C1 = {eng_c1_newtons:.2f};")
        lines.append(f"{eng_id}.K1 = {isp:.2f};")

    # Attach all hardware to Spacecraft
    if created_tank_names:
        lines.append(f"{sat_name}.Tanks = {{{', '.join(created_tank_names)}}};")
    if created_thruster_names:
        lines.append(f"{sat_name}.Thrusters = {{{', '.join(created_thruster_names)}}};")
    lines.append("")

    # Coordinate Systems (Custom User-Defined + Built-in Systems)
    custom_coords = custom_coordinate_systems or []
    created_cs_names = set()

    for cs in custom_coords:
        cs_name = str(cs.get("name") or "").strip()
        if not cs_name or cs_name in created_cs_names:
            continue
        origin = cs.get("origin") or "Earth"
        axes = cs.get("axes") or "MJ2000Eq"
        lines.append(f"Create CoordinateSystem {cs_name};")
        lines.append(f"{cs_name}.Origin = {origin};")
        lines.append(f"{cs_name}.Axes   = {axes};")

        if axes in ("ObjectReferenced", "BodySpinSun"):
            primary = cs.get("primary") or "Earth"
            secondary = cs.get("secondary") or "Luna"
            lines.append(f"{cs_name}.Primary   = {primary};")
            lines.append(f"{cs_name}.Secondary = {secondary};")
            x_axis = cs.get("xAxis") or cs.get("x_axis") or "R"
            y_axis = cs.get("yAxis") or cs.get("y_axis")
            z_axis = cs.get("zAxis") or cs.get("z_axis") or "N"
            if x_axis and x_axis != "None":
                lines.append(f"{cs_name}.XAxis     = {x_axis};")
            if y_axis and y_axis != "None":
                lines.append(f"{cs_name}.YAxis     = {y_axis};")
            if z_axis and z_axis != "None":
                lines.append(f"{cs_name}.ZAxis     = {z_axis};")

        epoch = cs.get("epoch")
        if epoch:
            lines.append(f"{cs_name}.Epoch = '{epoch}';")
        lines.append("")
        created_cs_names.add(cs_name)

    # Built-in fallbacks if not overridden
    if "MoonInertial" not in created_cs_names:
        lines.append("Create CoordinateSystem MoonInertial;")
        lines.append("MoonInertial.Origin = Luna;")
        lines.append("MoonInertial.Axes   = BodyInertial;\n")
        created_cs_names.add("MoonInertial")

    if "EarthMoonRot" not in created_cs_names:
        lines.append("Create CoordinateSystem EarthMoonRot;")
        lines.append("EarthMoonRot.Origin    = Luna;")
        lines.append("EarthMoonRot.Axes      = ObjectReferenced;")
        lines.append("EarthMoonRot.XAxis     = R;")
        lines.append("EarthMoonRot.ZAxis     = N;")
        lines.append("EarthMoonRot.Primary   = Earth;")
        lines.append("EarthMoonRot.Secondary = Luna;\n")
        created_cs_names.add("EarthMoonRot")

    # Each GMAT Propagator receives its own ForceModel so GUI settings stay
    # independent instead of silently sharing one force model.
    pcfgs = propagator_configs or {}

    # Define standard or custom force models
    def build_pm_list(pm_cfg, default_list, excluded_body=None):
        if pm_cfg is None:
            bodies = [body for body in default_list if body != excluded_body]
            return "{" + ", ".join(bodies) + "}"
        bodies = []
        if pm_cfg.get("Earth", False) and excluded_body != "Earth": bodies.append("Earth")
        if pm_cfg.get("Luna", False) and excluded_body != "Luna": bodies.append("Luna")
        if pm_cfg.get("Sun", False) and excluded_body != "Sun": bodies.append("Sun")
        if pm_cfg.get("Jupiter", False) and excluded_body != "Jupiter": bodies.append("Jupiter")
        if pm_cfg.get("Venus", False) and excluded_body != "Venus": bodies.append("Venus")
        if pm_cfg.get("Mars", False) and excluded_body != "Mars": bodies.append("Mars")
        if not bodies: bodies = [body for body in default_list if body != excluded_body]
        return "{" + ", ".join(bodies) + "}"

    def add_optional_assignment(object_name, property_name, value):
        if value is not None and value != "":
            lines.append(f"{object_name}.{property_name} = {value};")

    def add_optional_numeric_assignment(object_name, property_name, value):
        if value is not None and float(value or 0.0) != 0.0:
            lines.append(f"{object_name}.{property_name} = {value};")

    def build_force_model(model_name, cfg, default_body, default_masses):
        body = cfg.get("central_body", default_body)
        masses = [mass for mass in default_masses if mass != body]
        configured_masses = cfg.get("point_masses")
        if configured_masses:
            configured_masses = dict(configured_masses)
            configured_masses.pop(body, None)
        lines.append(f"Create ForceModel {model_name};")
        lines.append(f"{model_name}.CentralBody = {body};")
        lines.append(f"{model_name}.PrimaryBodies = {{{body}}};")
        lines.append(f"{model_name}.PointMasses = {build_pm_list(configured_masses, masses, body)};")
        lines.append(f"{model_name}.ErrorControl = {cfg.get('error_control', 'RSSStep')};")
        atmosphere_model = cfg.get("drag_atmosphere_model") or cfg.get("drag_model", "None")
        lines.append(f"{model_name}.Drag.AtmosphereModel = {atmosphere_model};")
        if atmosphere_model != "None":
            add_optional_numeric_assignment(model_name, "Drag.F107", cfg.get("drag_f107"))
            add_optional_numeric_assignment(model_name, "Drag.F107A", cfg.get("drag_f107a"))
            add_optional_numeric_assignment(model_name, "Drag.MagneticIndex", cfg.get("drag_magnetic_index"))
        gravity_model_files = {
            "JGM-2": "JGM2.cof", "JGM-3": "JGM3.cof", "EGM-96": "EGM96.cof",
            "Mars-50C": "Mars50c.cof", "MGNP-180U": "MGNP180U.cof", "LP-165P": "LP165P.cof"
        }
        gravity_file = cfg.get("gravity_potential_file")
        if not gravity_file and cfg.get("gravity_model") in gravity_model_files:
            gravity_file = gravity_model_files[cfg["gravity_model"]]
        if int(cfg.get("gravity_degree", 0) or 0) > 0:
            add_optional_assignment(model_name, "GravityField.%s.Degree" % body, int(cfg["gravity_degree"]))
        if int(cfg.get("gravity_order", 0) or 0) > 0:
            add_optional_assignment(model_name, "GravityField.%s.Order" % body, int(cfg["gravity_order"]))
        if int(cfg.get("gravity_stm_limit", 0) or 0) > 0:
            add_optional_assignment(model_name, "GravityField.%s.StmLimit" % body, int(cfg["gravity_stm_limit"]))
        if gravity_file:
            path = str(gravity_file).replace("'", "")
            add_optional_assignment(model_name, "GravityField.%s.PotentialFile" % body, "'%s'" % path)
        if cfg.get("tide_model"):
            add_optional_assignment(model_name, "GravityField.%s.TideModel" % body, cfg["tide_model"])
        if cfg.get("tide_potential_file"):
            path = str(cfg["tide_potential_file"]).replace("'", "")
            add_optional_assignment(model_name, "GravityField.%s.TidePotentialFile" % body, "'%s'" % path)
        lines.append("")

    # Base default propagators
    all_pcfgs = {
        "LEOProp": {"integrator_type": "RungeKutta89", "initial_step": 60, "accuracy": 1e-11, "min_step": 0.001, "max_step": 2700, "central_body": "Earth"},
        "CislunarProp": {"integrator_type": "RungeKutta89", "initial_step": 60, "accuracy": 1e-11, "min_step": 0.001, "max_step": 21600, "central_body": "Earth"},
        "LunarNRHOProp": {"integrator_type": "RungeKutta89", "initial_step": 60, "accuracy": 1e-11, "min_step": 0.001, "max_step": 10800, "central_body": "Luna"},
        "DefaultProp": {"integrator_type": "RungeKutta89", "initial_step": 60, "accuracy": 1e-11, "min_step": 0.001, "max_step": 2700, "central_body": "Earth"}
    }
    # Merge custom user propagators
    for pk, pv in pcfgs.items():
        all_pcfgs[pk] = pv

    # Dynamically declare all ForceModels and Propagators
    for p_name, p_cfg in all_pcfgs.items():
        fm_name = f"{p_name}ForceModel"
        def_body = p_cfg.get("central_body", "Earth")
        build_force_model(fm_name, p_cfg, def_body, ["Earth", "Luna", "Sun", "Jupiter"])

        lines.append(f"Create Propagator {p_name};")
        lines.append(f"{p_name}.FM              = {fm_name};")
        lines.append(f"{p_name}.Type            = {p_cfg.get('integrator_type', 'RungeKutta89')};")
        lines.append(f"{p_name}.InitialStepSize = {p_cfg.get('initial_step', 60)};")
        lines.append(f"{p_name}.Accuracy        = {p_cfg.get('accuracy', 1e-11)};")
        lines.append(f"{p_name}.MinStep         = {p_cfg.get('min_step', 0.001)};")
        lines.append(f"{p_name}.MaxStep         = {p_cfg.get('max_step', 2700)};\n")
        lines.append(f"{p_name}.MaxStepAttempts = {p_cfg.get('max_step_attempts', 50)};")
        lines.append(f"{p_name}.StopIfAccuracyIsViolated = {'true' if p_cfg.get('stop_if_accuracy_is_violated', False) else 'false'};\n")
        for prop_name, config_name in (("ErrorThreshold", "error_threshold"), ("SmallestInterval", "smallest_interval"), ("LowerError", "lower_error"), ("TargetError", "target_error"), ("StepSize", "step_size"), ("MinimumReduction", "minimum_reduction"), ("MaximumReduction", "maximum_reduction"), ("MinimumTolerance", "minimum_tolerance")):
            add_optional_numeric_assignment(p_name, prop_name, p_cfg.get(config_name))
        lines.append("")

    # Report files are the sole source of post-run numerical results.  Python
    # never propagates the state or estimates a burn from engine equations.
    lines.append(f"Create ReportFile TrajReport;")
    gmat_report_path = os.path.abspath(report_path).replace("\\", "/")
    lines.append(f"TrajReport.Filename = '{gmat_report_path}';")
    lines.append(f"TrajReport.WriteHeaders = false;\n")

    finite_report_path = os.path.abspath(report_path + ".finite.txt").replace("\\", "/")
    lines.append("Create ReportFile FiniteBurnReport;")
    lines.append(f"FiniteBurnReport.Filename = '{finite_report_path}';")
    lines.append("FiniteBurnReport.WriteHeaders = false;\n")



    # -------------------------------------------------------------------------
    # Auto-Fix Sequence: If Vary / Achieve exist without Target / EndTarget,
    # automatically auto-wrap them into a Target DC ... EndTarget block.
    # -------------------------------------------------------------------------
    has_explicit_target = any(s.get("type") in ("Target", "Targeter", "TargetApoapsis") for s in mission_sequence)
    has_vary_or_achieve = any(s.get("type") in ("Vary", "Achieve") for s in mission_sequence)

    if has_vary_or_achieve and not has_explicit_target:
        first_vary_idx = -1
        last_achieve_idx = -1
        for idx, s in enumerate(mission_sequence):
            stype = s.get("type")
            if stype in ("Vary", "Achieve", "FiniteBurn"):
                if first_vary_idx == -1:
                    first_vary_idx = idx
                last_achieve_idx = idx
        
        if first_vary_idx != -1 and last_achieve_idx != -1:
            wrapped_seq = []
            for idx, s in enumerate(mission_sequence):
                if idx == first_vary_idx:
                    wrapped_seq.append({"type": "Target", "label": "AutoTarget", "dc_name": "DC"})
                wrapped_seq.append(s)
                if idx == last_achieve_idx:
                    wrapped_seq.append({"type": "EndTarget", "label": "EndAutoTarget"})
            mission_sequence = wrapped_seq

    # Pre-create any burns specified in sequence
    burn_defs = []
    burn_idx = 1
    finite_idx = 1
    dc_needed = False

    # Check which steps are targeters and link to the preceding burn.
    for i, step in enumerate(mission_sequence):
        stype = step.get("type")
        if stype in ("TargetApoapsis", "Targeter"):
            dc_needed = True
            if i > 0 and mission_sequence[i - 1].get("type") in ("FiniteBurn", "ImpulsiveBurn"):
                step["_targets_burn_idx"] = i - 1
                mission_sequence[i - 1]["_targeted_by_idx"] = i
            else:
                raise ValueError("Targeter must immediately follow a FiniteBurn or ImpulsiveBurn")

    created_burn_names = set()
    created_finite_names = set()

    # Pre-scan Vary steps to automatically instantiate referenced ImpulsiveBurns (e.g. GEO_Burn.Element1)
    for st in mission_sequence:
        if st.get("type") == "Vary":
            v_var = (st.get("vary_variable") or st.get("variable") or "").strip()
            if "." in v_var:
                p_obj, p_elem = v_var.split(".", 1)
                if p_elem.lower() in ("element1", "element2", "element3") and p_obj.lower() not in ("sat", "vessel"):
                    clean_bname = sanitize_gmat_identifier(p_obj, "GEO_Burn")
                    if clean_bname not in created_burn_names:
                        created_burn_names.add(clean_bname)
                        burn_defs.append(f"Create ImpulsiveBurn {clean_bname};")
                        burn_defs.append(f"{clean_bname}.CoordinateSystem = Local;")
                        burn_defs.append(f"{clean_bname}.Origin = {central_body};")
                        burn_defs.append(f"{clean_bname}.Axes = VNB;")
                        burn_defs.append(f"{clean_bname}.Element1 = 1.10;")
                        burn_defs.append(f"{clean_bname}.Element2 = 1.50;")
                        burn_defs.append(f"{clean_bname}.Element3 = 0.0;")
                        burn_defs.append(f"{clean_bname}.DecrementMass = false;")
                        burn_defs.append(f"{clean_bname}.Isp = 320.0;")

    for step_index, step in enumerate(mission_sequence):
        step_type = step.get("type")
        if step_type in ("ImpulsiveBurn", "Maneuver"):
            requested_name = sanitize_gmat_identifier(step.get("burn_name") or step.get("name"), "")
            bname = requested_name if requested_name and requested_name not in ("Maneuver", "ImpulsiveBurn") else f"IB_{burn_idx}"
            step["_obj_name"] = bname
            if bname not in created_burn_names:
                created_burn_names.add(bname)
                burn_defs.append(f"Create ImpulsiveBurn {bname};")
                burn_defs.append(f"{bname}.CoordinateSystem = {step.get('burn_coordinate_system', 'Local')};")
                burn_defs.append(f"{bname}.Origin = {step.get('burn_origin') or central_body};")
                burn_defs.append(f"{bname}.Axes = {step.get('burn_axes', 'VNB')};")
                burn_defs.append(f"{bname}.Element1 = {float(step.get('dv_v', 0.0)) / 1000.0:.8g};")
                burn_defs.append(f"{bname}.Element2 = {float(step.get('dv_n', 0.0)) / 1000.0:.8g};")
                burn_defs.append(f"{bname}.Element3 = {float(step.get('dv_b', 0.0)) / 1000.0:.8g};")
                dec_mass = "true" if step.get("burn_decrement_mass", False) else "false"
                burn_defs.append(f"{bname}.DecrementMass = {dec_mass};")
                if dec_mass == "true":
                    bound_tank = step.get("bound_tank") or (created_tank_names[0] if created_tank_names else "MainTank")
                    burn_isp_val = float(step.get("burn_isp") or isp or 316.0)
                    burn_defs.append(f"{bname}.Tank = {{{bound_tank}}};")
                    burn_defs.append(f"{bname}.Isp = {burn_isp_val:.2f};")
            burn_idx += 1
        elif step_type == "FiniteBurn":
            raw_th = step.get("bound_thruster")
            bound_thruster = None
            if raw_th:
                clean_th = sanitize_gmat_identifier(raw_th, "MainEngine")
                norm_raw = clean_th.lower().replace("_", "").replace("-", "")
                if clean_th in created_thruster_names:
                    bound_thruster = clean_th
                else:
                    # Case-insensitive or fuzzy match with normalized underscores
                    for c_th in created_thruster_names:
                        norm_c = c_th.lower().replace("_", "").replace("-", "")
                        if norm_c == norm_raw or norm_raw in norm_c or norm_c in norm_raw:
                            bound_thruster = c_th
                            break
            if not bound_thruster:
                # Pick the thruster with highest thrust (main engine)
                best_th = created_thruster_names[0] if created_thruster_names else "MainEngine"
                best_t = -1.0
                for th_info, th_nm in zip(craft_data.get("thrusters", []), created_thruster_names):
                    tv = float(th_info.get("thrust", 0.0) or 0.0)
                    if tv > best_t:
                        best_t = tv
                        best_th = th_nm
                bound_thruster = best_th
            fbname = sanitize_gmat_identifier(step.get("burn_name") or f"Finite_{finite_idx}", "FiniteBurn")
            step["_obj_name"] = fbname
            if fbname not in created_finite_names:
                created_finite_names.add(fbname)
                burn_defs.append(f"Create FiniteBurn {fbname};")
                burn_defs.append(f"{fbname}.Thrusters = {{{bound_thruster}}};")
            finite_idx += 1
        elif step_type in ("TargetApoapsis", "Targeter", "Target", "Vary", "Achieve", "DifferentialCorrector"):
            dc_needed = True

    # Collect custom user variables (e.g. burntime)
    user_vars = set()
    for step in mission_sequence:
        stype = step.get("type")
        if stype == "Var":
            v = step.get("var_name") or step.get("variable")
            if v and "." not in v:
                user_vars.add(v)
        elif stype == "Vary":
            v = step.get("vary_variable") or step.get("variable")
            if v and "." not in v:
                user_vars.add(v)
        elif stype == "FiniteBurn":
            if step.get("burn_use_vary_var") and step.get("burn_vary_var_name"):
                v = step.get("burn_vary_var_name")
                if v and "." not in v:
                    user_vars.add(v)

    # Register time tracking variables for all maneuvers to capture exact GMAT
    # physics epochs. Finite burns also expose a targetable duration variable.
    time_vars = []
    seen_time_vars = set()
    for step in mission_sequence:
        bname = step.get("_obj_name")
        if bname and f"{bname}_Time" not in seen_time_vars:
            seen_time_vars.add(f"{bname}_Time")
            time_vars.append(f"{bname}_Time")
            if step.get("type") == "FiniteBurn":
                time_vars.extend([
                    f"{bname}_Duration",
                    f"{bname}_EndTime",
                    f"{bname}_DeltaV",
                    f"{bname}_DeltaV_V",
                    f"{bname}_DeltaV_N",
                    f"{bname}_DeltaV_B",
                    f"{bname}_StartMass",
                    f"{bname}_EndMass",
                    f"{bname}_FuelBurned",
                    f"{bname}_StartVX",
                    f"{bname}_StartVY",
                    f"{bname}_StartVZ",
                    f"{bname}_EndVX",
                    f"{bname}_EndVY",
                    f"{bname}_EndVZ",
                    f"{bname}_OrbitalDeltaV",
                    f"{bname}_GravityLoss"
                ])

    all_declared_vars = list(time_vars)
    for uv in sorted(list(user_vars)):
        if uv not in all_declared_vars:
            all_declared_vars.append(uv)

    if all_declared_vars:
        lines.append(f"Create Variable {' '.join(all_declared_vars)};\n")

    if dc_needed:
        active_solvers = {}
        for s in mission_sequence:
            stype = s.get("type")
            s_name = s.get("dc_name") or s.get("vary_solver") or s.get("achieve_solver")
            if s_name:
                clean_sname = sanitize_gmat_identifier(s_name, "DC")
                if clean_sname not in active_solvers:
                    active_solvers[clean_sname] = {
                        "algorithm": s.get("dc_algorithm") or "NewtonRaphson",
                        "derivative_method": s.get("dc_derivative_method") or "ForwardDifference",
                        "max_iter": int(s.get("dc_max_iter") or 50)
                    }

        if solver_configs:
            for s_name, s_cfg in solver_configs.items():
                clean_cfg_name = sanitize_gmat_identifier(s_name, "DC")
                if clean_cfg_name not in active_solvers:
                    active_solvers[clean_cfg_name] = {
                        "algorithm": s_cfg.get("algorithm", "NewtonRaphson"),
                        "derivative_method": s_cfg.get("derivative_method", "ForwardDifference"),
                        "max_iter": int(s_cfg.get("max_iterations", 50))
                    }

        if not active_solvers:
            active_solvers["DefaultDC"] = {"algorithm": "NewtonRaphson", "derivative_method": "ForwardDifference", "max_iter": 50}

        for s_name, s_info in active_solvers.items():
            lines.append(f"Create DifferentialCorrector {s_name};")
            lines.append(f"{s_name}.Algorithm = '{s_info.get('algorithm', 'NewtonRaphson')}';")
            lines.append(f"{s_name}.DerivativeMethod = '{s_info.get('derivative_method', 'ForwardDifference')}';")
            lines.append(f"{s_name}.ShowProgress = true;")
            lines.append(f"{s_name}.ReportStyle = 'Normal';")
            dc_data_file = os.path.abspath(os.path.join(os.path.dirname(report_path), f"DifferentialCorrector{s_name}.data")).replace("\\", "/")
            lines.append(f"{s_name}.ReportFile = '{dc_data_file}';")
            max_i = s_info.get("max_iter", 50)
            if max_i > 0:
                lines.append(f"{s_name}.MaximumIterations = {max_i};")
            lines.append("")

    if burn_defs:
        lines.extend(burn_defs)
        lines.append("")

    def target_propagator(target_step, burn_step):
        prop = target_step.get("propagator") or burn_step.get("propagator")
        if not prop:
            raise ValueError("target step requires an explicit GMAT propagator")
        if prop not in ("LEOProp", "CislunarProp", "LunarNRHOProp"):
            raise ValueError("unknown GMAT propagator: %s" % prop)
        return prop

    # Dedicated ReportFile objects for sequence 'Report' steps
    for s_idx, st in enumerate(mission_sequence):
        if st.get("type") == "Report":
            rf_name = f"StepReport_{s_idx}"
            rf_file = os.path.abspath(os.path.join(os.path.dirname(report_path), f"step_report_{s_idx}.txt")).replace("\\", "/")
            lines.append(f"Create ReportFile {rf_name};")
            lines.append(f"{rf_name}.Filename = '{rf_file}';")
            lines.append(f"{rf_name}.WriteHeaders = true;")

    # Mission Sequence Commands
    lines.append("BeginMissionSequence;\n")
    state_report = f"Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;"
    current_active_dc_name = "DefaultDC"
    lines.append(state_report)

    for step in mission_sequence:
        step_type = step.get("type")
        if step_type == "Propagate":
            prop_func = step.get("propagator")
            if not prop_func:
                prop_func = default_propagator(step, central_body) or "LEOProp"
            if prop_func not in all_pcfgs:
                prop_func = "LEOProp"
            label = sanitize_gmat_label(step.get("label"), "Propagate")
            label_str = f"'{label}' " if label else ""

            raw_stop = (step.get("stop_type") or "Time").strip()
            tol = float(step.get("stop_tolerance") or 1e-5)
            tol_str = f", StopTolerance = {tol:.8g}" if tol > 0 else ""
            target_val = float(step.get("target_val", 0.0) if step.get("target_val") is not None else step.get("duration", 0.0))

            # 1. Clean any leading Spacecraft name prefix (e.g. "Sat.", "Sat_3.", "Vessel.", "Spacecraft.")
            clean_stop = raw_stop
            # Case insensitive prefix check
            for pfx in ("sat.", "sat_3.", "spacecraft.", "sc.", "vessel.", f"{sat_name.lower()}."):
                if clean_stop.lower().startswith(pfx):
                    clean_stop = clean_stop[len(pfx):].strip()

            # 2. Check for explicit equals expression (e.g. "Luna.RMAG = 6000")
            if "=" in clean_stop:
                parts = clean_stop.split("=", 1)
                clean_stop = parts[0].strip()
                try:
                    target_val = float(parts[1].strip())
                except:
                    pass

            # 3. Universal Stop Condition Matching
            if clean_stop in ("Time", "ElapsedSecs"):
                duration = float(step.get("duration", target_val))
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.ElapsedSecs = {duration}}};")
            elif clean_stop in ("Days", "ElapsedDays"):
                days = float(step.get("duration", target_val))
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.ElapsedDays = {days}}};")
            elif clean_stop.endswith(".Periapsis") or clean_stop == "Periapsis":
                body = clean_stop.split(".")[0] if "." in clean_stop else central_body
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{body}.Periapsis}};")
            elif clean_stop.endswith(".Apoapsis") or clean_stop == "Apoapsis":
                body = clean_stop.split(".")[0] if "." in clean_stop else central_body
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{body}.Apoapsis}};")
            elif clean_stop in ("Node", "Latitude_Zero", "EquatorialNode") or clean_stop.endswith(".Latitude_Zero") or clean_stop.endswith(".Node"):
                body = clean_stop.split(".")[0] if ("." in clean_stop and not clean_stop.startswith("Node")) else central_body
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{body}.Latitude = 0.0{tol_str}}};")
            elif "." in clean_stop:
                # Multi-dot parameter (e.g. "Luna.RMAG", "Earth.Altitude", "EarthMoonRot.Z", "Earth.Z")
                parts = clean_stop.split(".")
                prefix = parts[0]
                prop_field = parts[1] if len(parts) > 1 else ""

                # Auto-heal Cartesian coordinate on body (e.g. Earth.Z -> EarthMJ2000Eq.Z, Luna.Z -> MoonInertial.Z)
                if prop_field in ("X", "Y", "Z", "VX", "VY", "VZ", "AX", "AY", "AZ"):
                    if prefix.lower() in ("earth", "kerbin"):
                        clean_stop = f"EarthMJ2000Eq.{prop_field}"
                    elif prefix.lower() in ("luna", "mun", "moon"):
                        clean_stop = f"MoonInertial.{prop_field}"
                    elif prefix.lower() in ("sun", "sol"):
                        clean_stop = f"EarthMJ2000Eq.{prop_field}"

                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{clean_stop} = {target_val}{tol_str}}};")
            elif clean_stop in ("Altitude", "Alt"):
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{central_body}.Altitude = {target_val}{tol_str}}};")
            elif clean_stop in ("RMAG", "Radius"):
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{central_body}.RMAG = {target_val}{tol_str}}};")
            elif clean_stop in ("TA", "TrueAnomaly", "EA", "EccentricAnomaly", "MA", "MeanAnomaly", "SMA", "SemiMajorAxis", "ECC", "Eccentricity", "INC", "Inclination", "RAAN", "AOP", "ArgOfPeriapsis", "OrbitPeriod", "RadApo", "RadPer", "VelApoapsis", "VelPeriapsis", "Latitude", "Longitude", "BetaAngle", "BdotT", "BdotR"):
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{central_body}.{clean_stop} = {target_val}{tol_str}}};")
            elif clean_stop in ("X", "Y", "Z", "VX", "VY", "VZ", "AX", "AY", "AZ", "RA", "DEC", "C3Energy", "Energy"):
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{clean_stop} = {target_val}{tol_str}}};")
            elif clean_stop in ("UTCGregorian", "Epoch"):
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{clean_stop} = {target_val}{tol_str}}};")
            else:
                lines.append(f"Propagate {label_str}{prop_func}({sat_name}) {{{sat_name}.{clean_stop} = {target_val}{tol_str}}};")
        elif step_type == "ImpulsiveBurn":
            if step.get("_targeted_by_idx") is not None:
                continue
            bname = step.get("_obj_name")
            label = sanitize_gmat_label(step.get("label"), "Maneuver")
            label_str = f"'{label}' " if label else ""
            lines.append(f"{bname}_Time = {sat_name}.ElapsedSecs;")
            lines.append(state_report)
            lines.append(f"Maneuver {label_str}{bname}({sat_name});")
            lines.append(state_report)

        elif step_type == "Target":
            label = sanitize_gmat_label(step.get("label"), "Target")
            label_str = f"'{label}' " if label else ""
            current_active_dc_name = sanitize_gmat_identifier(step.get("dc_name") or "DefaultDC", "DC")
            solve_mode = step.get("solve_mode") or "Solve"
            exit_mode = step.get("exit_mode") or "SaveAndContinue"
            lines.append(f"Target {label_str}{current_active_dc_name} {{SolveMode = {solve_mode}, ExitMode = {exit_mode}}};")

        elif step_type == "Vary":
            label = sanitize_gmat_label(step.get("label"), "Vary")
            label_str = f"'{label}' " if label else ""
            vary_dc_name = sanitize_gmat_identifier(step.get("dc_name") or step.get("vary_solver") or current_active_dc_name, "DC")
            var = (step.get("vary_variable") or step.get("variable") or "burntime").strip()

            # Clean craft prefix if present or handle Burn.Element1/2/3
            if var.startswith(f"{sat_name}."):
                var_ref = var
            elif "." in var:
                # e.g. GEO_Burn.Element1, LOI.Element2, Change6.AOP, EarthMoonRot.Z
                parts = var.split(".", 1)
                if parts[0].lower() in ("sat", "vessel", "spacecraft", "sc"):
                    var_ref = f"{sat_name}.{parts[1]}"
                elif parts[0] in ("AOP", "RAAN", "SMA", "ECC", "INC", "TA", "Altitude", "RMAG"):
                    var_ref = f"{sat_name}.{var}"
                elif parts[1].lower() in ("element1", "element2", "element3"):
                    # Burn vector element e.g. GEO_Burn.Element1
                    clean_bname = sanitize_gmat_identifier(parts[0], "IB")
                    var_ref = f"{clean_bname}.{parts[1]}"
                else:
                    var_ref = var
            elif var.lower() in ("element1", "element2", "element3"):
                # Default to nearest or active impulsive burn
                active_ib = "GEO_Burn"
                for st in mission_sequence:
                    if st.get("type") in ("ImpulsiveBurn", "Maneuver") and st.get("burn_name"):
                        active_ib = sanitize_gmat_identifier(st["burn_name"], "IB")
                        break
                var_ref = f"{active_ib}.{var}"
            else:
                var_ref = var

            init_val = float(step.get("vary_initial", 150.0))
            pert = float(step.get("vary_perturbation") or 0.0001)
            lower = float(step.get("vary_lower") or 0.0)
            upper = float(step.get("vary_upper") or 1000.0)
            max_step = float(step.get("vary_max_step") or 2.0)
            add_scale = float(step.get("vary_additive_scale", 0.0))
            mul_scale = float(step.get("vary_multiplicative_scale", 1.0))

            scale_opts = ""
            if add_scale != 0.0 or mul_scale != 1.0:
                scale_opts = f", AdditiveScaleFactor = {add_scale:.8g}, MultiplicativeScaleFactor = {mul_scale:.8g}"

            lines.append(f"   Vary {label_str}{vary_dc_name}({var_ref} = {init_val}, {{Perturbation = {pert}, Lower = {lower}, Upper = {upper}, MaxStep = {max_step}{scale_opts}}});")

        elif step_type == "Achieve":
            label = sanitize_gmat_label(step.get("label"), "Achieve")
            label_str = f"'{label}' " if label else ""
            achieve_dc_name = sanitize_gmat_identifier(step.get("dc_name") or step.get("achieve_solver") or current_active_dc_name, "DC")
            goal_param = step.get("achieve_param") or step.get("target_goal") or "RMAG"
            goal_ref = resolve_gmat_target_param(sat_name, central_body, goal_param)

            target_val = float(step.get("achieve_val", step.get("target_val", step.get("target_alt", 40000.0))))
            tol = float(step.get("achieve_tolerance", step.get("target_tolerance", 0.1)) or 0.1)
            lines.append(f"   Achieve {label_str}{achieve_dc_name}({goal_ref} = {target_val}, {{Tolerance = {tol}}});")

        elif step_type == "EndTarget":
            lines.append(f"EndTarget;  % For targeter {current_active_dc_name}")

        elif step_type in ("StageSeparation", "Staging", "Decouple"):
            label = sanitize_gmat_label(step.get("label"), "StageSep")
            label_str = f"'{label}' " if label else ""
            rem_dry = float(step.get("remaining_dry_mass", 0.0))
            drop_mass = float(step.get("separated_mass", 0.0))
            if rem_dry > 0:
                lines.append(f"% Stage Separation: {label_str}")
                lines.append(f"{sat_name}.DryMass = {rem_dry:.2f};")
            elif drop_mass > 0:
                lines.append(f"% Stage Separation: {label_str}")
                lines.append(f"{sat_name}.DryMass = {sat_name}.DryMass - {drop_mass:.2f};")

        elif step_type in ("Var", "Equation"):
            vname = step.get("var_name") or step.get("variable", "burntime")
            vexpr = step.get("var_expr") or step.get("expression", "0.0")
            if "." in vname and not vname.startswith(sat_name):
                vname = f"{sat_name}.{vname}"
            lines.append(f"{vname} = {vexpr};")

        elif step_type == "FiniteBurn":
            if step.get("_targeted_by_idx") is not None:
                continue
            fbname = step.get("_obj_name") or "Finite_1"
            duration = float(step.get("duration", 0.0))
            b_dir = (step.get("burn_direction") or "Prograde").strip().lower()
            if b_dir in ("retrograde", "-v"):
                dir_v, dir_n, dir_b = -1.0, 0.0, 0.0
            elif b_dir in ("normal", "+n"):
                dir_v, dir_n, dir_b = 0.0, 1.0, 0.0
            elif b_dir in ("antinormal", "-n"):
                dir_v, dir_n, dir_b = 0.0, -1.0, 0.0
            elif b_dir in ("radialout", "+b"):
                dir_v, dir_n, dir_b = 0.0, 0.0, 1.0
            elif b_dir in ("radialin", "-b"):
                dir_v, dir_n, dir_b = 0.0, 0.0, -1.0
            else:
                dir_v, dir_n, dir_b = 1.0, 0.0, 0.0

            lines.append(f"{fbname}_Time = {sat_name}.ElapsedSecs;")
            use_vary = step.get("burn_use_vary_var") and step.get("burn_vary_var_name")
            dur_var = step.get("burn_vary_var_name") if use_vary else f"{fbname}_Duration"
            if use_vary:
                lines.append(f"{fbname}_Duration = {dur_var};")
            else:
                lines.append(f"{fbname}_Duration = {duration};")
            lines.append(f"{fbname}_StartMass = {sat_name}.TotalMass;")
            lines.append(f"{fbname}_StartVX = {sat_name}.VX;")
            lines.append(f"{fbname}_StartVY = {sat_name}.VY;")
            lines.append(f"{fbname}_StartVZ = {sat_name}.VZ;")
            lines.append(f"BeginFiniteBurn {fbname}({sat_name});")
            prop_func = step.get("propagator")
            if not prop_func:
                prop_func = default_propagator(step, central_body)
            lines.append(f"Propagate {prop_func}({sat_name}) {{{sat_name}.ElapsedSecs = {dur_var}}};")
            lines.append(f"EndFiniteBurn {fbname}({sat_name});")
            lines.append(f"{fbname}_EndMass = {sat_name}.TotalMass;")
            lines.append(f"{fbname}_EndVX = {sat_name}.VX;")
            lines.append(f"{fbname}_EndVY = {sat_name}.VY;")
            lines.append(f"{fbname}_EndVZ = {sat_name}.VZ;")
            lines.append(f"{fbname}_FuelBurned = {fbname}_StartMass - {fbname}_EndMass;")
            lines.append(f"{fbname}_DeltaV = {isp:.6f} * 9.80665 * log({fbname}_StartMass / {fbname}_EndMass);")
            lines.append(f"{fbname}_DeltaV_V = {fbname}_DeltaV * {dir_v:.6f};")
            lines.append(f"{fbname}_DeltaV_N = {fbname}_DeltaV * {dir_n:.6f};")
            lines.append(f"{fbname}_DeltaV_B = {fbname}_DeltaV * {dir_b:.6f};")
            lines.append(f"{fbname}_OrbitalDeltaV = sqrt(({fbname}_EndVX - {fbname}_StartVX)*({fbname}_EndVX - {fbname}_StartVX) + ({fbname}_EndVY - {fbname}_StartVY)*({fbname}_EndVY - {fbname}_StartVY) + ({fbname}_EndVZ - {fbname}_StartVZ)*({fbname}_EndVZ - {fbname}_StartVZ)) * 1000.0;")
            lines.append(f"{fbname}_GravityLoss = {fbname}_DeltaV - {fbname}_OrbitalDeltaV;")
            lines.append(f"Report FiniteBurnReport {fbname}_Time {fbname}_Duration {fbname}_DeltaV {fbname}_DeltaV_V {fbname}_DeltaV_N {fbname}_DeltaV_B {fbname}_FuelBurned {fbname}_OrbitalDeltaV {fbname}_GravityLoss {fbname}_StartMass {fbname}_EndMass {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")

        elif step_type in ("TargetApoapsis", "Targeter"):
            targeted_step = mission_sequence[step["_targets_burn_idx"]] if "_targets_burn_idx" in step else step
            bname = targeted_step.get("_obj_name")
            target_is_finite = targeted_step.get("type") == "FiniteBurn"
            target_goal = step.get("target_goal", "Apoapsis")
            if "target_alt" not in step and "target_val" not in step:
                raise ValueError("target step requires target_alt or target_val")
            target_alt = float(step.get("target_alt", step.get("target_val")))
            target_val = float(step.get("target_val", target_alt))
            target_prop = target_propagator(step, targeted_step)

            lines.append(f"{bname}_Time = {sat_name}.ElapsedSecs;")
            lines.append("Target DC {SolveMode = Solve, ExitMode = SaveAndContinue};")

            if target_is_finite:
                duration = float(targeted_step.get("duration", 60.0))
                duration_var = f"{bname}_Duration"
                lower = float(step.get("vary_lower") or 1.0)
                upper = float(step.get("vary_upper") or max(duration * 3.0, duration + 10.0))
                if duration >= upper:
                    duration = upper * 0.5
                    targeted_step["duration"] = duration
                lower = min(lower, upper * 0.5)
                if lower <= 0.0: lower = 0.5
                max_step = float(step.get("vary_max_step") or max(duration * 0.25, 5.0))
                max_step = min(max_step, upper - lower)

                b_dir = (targeted_step.get("burn_direction") or "Prograde").strip().lower()
                if b_dir in ("retrograde", "-v"):
                    dir_v, dir_n, dir_b = -1.0, 0.0, 0.0
                elif b_dir in ("normal", "+n"):
                    dir_v, dir_n, dir_b = 0.0, 1.0, 0.0
                elif b_dir in ("antinormal", "-n"):
                    dir_v, dir_n, dir_b = 0.0, -1.0, 0.0
                elif b_dir in ("radialout", "+b"):
                    dir_v, dir_n, dir_b = 0.0, 0.0, 1.0
                elif b_dir in ("radialin", "-b"):
                    dir_v, dir_n, dir_b = 0.0, 0.0, -1.0
                else:
                    dir_v, dir_n, dir_b = 1.0, 0.0, 0.0

                lines.append(dc_vary_variable_line(step, duration_var, duration, lower, upper, max_step))
                lines.append(f"  {bname}_Time = {sat_name}.ElapsedSecs;")
                lines.append(f"  {bname}_StartMass = {sat_name}.TotalMass;")
                lines.append(f"  {bname}_StartVX = {sat_name}.VX;")
                lines.append(f"  {bname}_StartVY = {sat_name}.VY;")
                lines.append(f"  {bname}_StartVZ = {sat_name}.VZ;")
                lines.append(f"  BeginFiniteBurn {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedSecs = {duration_var}}};")
                lines.append(f"  EndFiniteBurn {bname}({sat_name});")
                lines.append(f"  {bname}_EndMass = {sat_name}.TotalMass;")
                lines.append(f"  {bname}_EndVX = {sat_name}.VX;")
                lines.append(f"  {bname}_EndVY = {sat_name}.VY;")
                lines.append(f"  {bname}_EndVZ = {sat_name}.VZ;")
                lines.append(f"  {bname}_FuelBurned = {bname}_StartMass - {bname}_EndMass;")
                lines.append(f"  {bname}_DeltaV = {isp:.6f} * 9.80665 * log({bname}_StartMass / {bname}_EndMass);")
                lines.append(f"  {bname}_DeltaV_V = {bname}_DeltaV * {dir_v:.6f};")
                lines.append(f"  {bname}_DeltaV_N = {bname}_DeltaV * {dir_n:.6f};")
                lines.append(f"  {bname}_DeltaV_B = {bname}_DeltaV * {dir_b:.6f};")
                lines.append(f"  {bname}_OrbitalDeltaV = sqrt(({bname}_EndVX - {bname}_StartVX)*({bname}_EndVX - {bname}_StartVX) + ({bname}_EndVY - {bname}_StartVY)*({bname}_EndVY - {bname}_StartVY) + ({bname}_EndVZ - {bname}_StartVZ)*({bname}_EndVZ - {bname}_StartVZ)) * 1000.0;")
                lines.append(f"  {bname}_GravityLoss = {bname}_DeltaV - {bname}_OrbitalDeltaV;")
                lines.append(f"  Report FiniteBurnReport {bname}_Time {duration_var} {bname}_DeltaV {bname}_DeltaV_V {bname}_DeltaV_N {bname}_DeltaV_B {bname}_FuelBurned {bname}_OrbitalDeltaV {bname}_GravityLoss {bname}_StartMass {bname}_EndMass {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")

                if target_goal in ("Apoapsis", "ApoapsisVZ0"):
                    lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Apoapsis, {sat_name}.ElapsedDays = 3.0}};")
                    lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_alt}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
                elif target_goal == "Periapsis":
                    lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Periapsis, {sat_name}.ElapsedDays = 3.0}};")
                    lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_val}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
                elif target_goal in ("GEO_CAM", "SMA"):
                    lines.append(f"  Achieve DC({sat_name}.SMA = {target_val}, {{Tolerance = {dc_tolerance(step, 1.0):.8g}}});")
                elif target_goal == "ECC":
                    lines.append(f"  Achieve DC({sat_name}.ECC = {target_val}, {{Tolerance = {dc_tolerance(step, 0.001):.8g}}});")
                elif target_goal == "INC":
                    lines.append(f"  Achieve DC({sat_name}.INC = {target_val}, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
                elif target_goal == "RAAN":
                    lines.append(f"  Achieve DC({sat_name}.RAAN = {target_val}, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
                elif target_goal == "AOP":
                    lines.append(f"  Achieve DC({sat_name}.AOP = {target_val}, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
                elif target_goal in ("C3", "C3Energy"):
                    lines.append(f"  Achieve DC({sat_name}.C3Energy = {target_val}, {{Tolerance = {dc_tolerance(step, 0.1):.8g}}});")
                elif target_goal == "Altitude":
                    lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_val}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
                elif target_goal == "RMAG":
                    lines.append(f"  Achieve DC({sat_name}.{central_body}.RMAG = {target_val}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
                elif target_goal == "VMAG":
                    lines.append(f"  Achieve DC({sat_name}.VMAG = {target_val}, {{Tolerance = {dc_tolerance(step, 0.01):.8g}}});")
                elif target_goal == "TA":
                    lines.append(f"  Achieve DC({sat_name}.TA = {target_val}, {{Tolerance = {dc_tolerance(step, 0.1):.8g}}});")
                elif target_goal == "LunarPerilune":
                    lines.append(f"  Achieve DC({sat_name}.Luna.RMAG = {target_val}, {{Tolerance = {dc_tolerance(step, 1.0):.8g}}});")
                elif target_goal in ("BdotR", "BdotT"):
                    lines.append(f"  Achieve DC({sat_name}.{target_goal} = {target_val}, {{Tolerance = {dc_tolerance(step, 1.0):.8g}}});")
                else:
                    lines.append(f"  Achieve DC({sat_name}.{target_goal} = {target_val}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
                lines.append("EndTarget;")
                continue

            if "dv_v" not in targeted_step: raise ValueError("targeted burn requires dv_v initial guess")
            init_guess = float(targeted_step["dv_v"]) / 1000.0

            if target_goal == "Periapsis":
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Periapsis, {sat_name}.ElapsedDays = 3.0}};")
                lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_val}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
            elif target_goal == "GEO_CAM":
                lines.append(dc_vary_line(step, bname, "Element1", float(targeted_step.get("dv_v", 0.0)) / 1000.0, 0.0, 3.5, 0.2))
                lines.append(dc_vary_line(step, bname, "Element2", float(targeted_step.get("dv_n", 0.0)) / 1000.0, -3.5, 3.5, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Periapsis, {sat_name}.ElapsedDays = 3.0}};")
                lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_val}, {{Tolerance = {dc_tolerance(step, 5.0):.8g}}});")
                lines.append(f"  Achieve DC({sat_name}.INC = 0.05, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
            elif target_goal == "SMA":
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedSecs = 60}};")
                lines.append(f"  Achieve DC({sat_name}.SMA = {target_val}, {{Tolerance = {dc_tolerance(step, 1.0):.8g}}});")
            elif target_goal == "ECC":
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedSecs = 60}};")
                lines.append(f"  Achieve DC({sat_name}.ECC = {target_val}, {{Tolerance = {dc_tolerance(step, 0.001):.8g}}});")
            elif target_goal == "INC":
                lines.append(dc_vary_line(step, bname, "Element2", float(targeted_step.get("dv_n", 0.0)) / 1000.0, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedSecs = 60}};")
                lines.append(f"  Achieve DC({sat_name}.INC = {target_val}, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
            elif target_goal == "RAAN":
                lines.append(dc_vary_line(step, bname, "Element2", float(targeted_step.get("dv_n", 0.0)) / 1000.0, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedSecs = 60}};")
                lines.append(f"  Achieve DC({sat_name}.RAAN = {target_val}, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
            elif target_goal == "AOP":
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedSecs = 60}};")
                lines.append(f"  Achieve DC({sat_name}.AOP = {target_val}, {{Tolerance = {dc_tolerance(step, 0.05):.8g}}});")
            elif target_goal in ("C3", "C3Energy"):
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.ElapsedDays = 1.0}};")
                lines.append(f"  Achieve DC({sat_name}.C3Energy = {target_val}, {{Tolerance = {dc_tolerance(step, 0.1):.8g}}});")
            elif target_goal == "RMAG":
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, -5.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Apoapsis, {sat_name}.ElapsedDays = 3.0}};")
                lines.append(f"  Achieve DC({sat_name}.{central_body}.RMAG = {target_val}, {{Tolerance = {dc_tolerance(step, 1.0):.8g}}});")
            elif target_goal in ("TargetBodyPeriapsis", "LunarPerilune"):
                target_body = map_body_name(step.get("target_body") or ("Luna" if central_body == "Earth" else "Mars"))
                coast_days = float(step.get("duration", 6.0)) if float(step.get("duration", 0.0)) > 0.0 else 6.0
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, max(0.1, init_guess - 1.0), init_guess + 1.5, 0.05))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate CislunarProp({sat_name}) {{{sat_name}.{target_body}.Periapsis, {sat_name}.ElapsedDays = {coast_days}}};")
                lines.append(f"  Achieve DC({sat_name}.{target_body}.RMAG = {target_val}, {{Tolerance = {dc_tolerance(step, 100.0):.8g}}});")
            elif target_goal == "ApoapsisVZ0":
                lines.append(dc_vary_line(step, bname, "Element1", init_guess))
                normal_guess = float(targeted_step.get("dv_n", 0.0)) / 1000.0
                lines.append(dc_vary_line(step, bname, "Element2", normal_guess, prefix="normal_"))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Apoapsis, {sat_name}.ElapsedDays = 3.0}};")
                lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_alt}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")
                lines.append(f"  Achieve DC({sat_name}.VZ = 0.0{dc_vz_tolerance_option(step)});")
            else: # Apoapsis (default)
                lines.append(dc_vary_line(step, bname, "Element1", init_guess, 0.0, 5.0, 0.2))
                lines.append(f"  Report TrajReport {sat_name}.ElapsedSecs {sat_name}.X {sat_name}.Y {sat_name}.Z {sat_name}.VX {sat_name}.VY {sat_name}.VZ;")
                lines.append(f"  Maneuver {bname}({sat_name});")
                lines.append(f"  Propagate {target_prop}({sat_name}) {{{sat_name}.{central_body}.Apoapsis, {sat_name}.ElapsedDays = 3.0}};")
                lines.append(f"  Achieve DC({sat_name}.{central_body}.Altitude = {target_alt}, {{Tolerance = {dc_tolerance(step, 0.5):.8g}}});")

            lines.append("EndTarget;")

    # NASA canonical summary report via Write command
    lines.append("")
    lines.append(f"Write {sat_name}.ElapsedSecs {sat_name}.TotalMass {sat_name}.SMA {sat_name}.ECC {sat_name}.INC {sat_name}.RAAN {sat_name}.AOP {sat_name}.TA {{ Style = Concise, LogFile = true, MessageWindow = true }};\n")

    return "\n".join(lines)

def validate_mission_sequence_structure(mission_sequence):
    """
    Validates mission sequence semantic structure before script generation.
    Returns (is_valid: bool, error_msg: str)
    """
    inside_target = False
    target_idx = -1
    target_has_burn = False
    target_has_vary = False
    target_has_achieve = False
    target_vary_var = ""

    for idx, step in enumerate(mission_sequence):
        stype = step.get("type")
        if stype == "Target":
            inside_target = True
            target_idx = idx
            target_has_burn = False
            target_has_vary = False
            target_has_achieve = False
            target_vary_var = ""
        elif stype in ("FiniteBurn", "ImpulsiveBurn", "Maneuver") and inside_target:
            target_has_burn = True
        elif stype == "Vary" and inside_target:
            target_has_vary = True
            target_vary_var = step.get("vary_variable", "burntime")
        elif stype == "Achieve" and inside_target:
            target_has_achieve = True
        elif stype == "EndTarget":
            if inside_target:
                if target_has_vary and not target_has_burn:
                    # Check if there is a burn outside the target group
                    outside_burn_idx = -1
                    for b_idx, b_step in enumerate(mission_sequence):
                        if b_step.get("type") in ("FiniteBurn", "ImpulsiveBurn", "Maneuver") and (b_idx < target_idx or b_idx > idx):
                            outside_burn_idx = b_idx
                            break
                    if outside_burn_idx >= 0:
                        return False, (
                            f"❌ 任务序列结构错误: 打靶求解组 #{target_idx + 1} ('{mission_sequence[target_idx].get('label', 'Target')}') 内部缺少点火动作！\n"
                            f"变量 '{target_vary_var}' 在打靶组内未被任何机动消耗，而点火步骤 (#{outside_burn_idx + 1}) 被错误地放在了打靶组外部 (EndTarget 之后)。\n"
                            f"👉 请点击步骤 #{outside_burn_idx + 1} 上的 [ ≡ 排序 ]，将其移动到打靶组内部 (Target 与 EndTarget 之间)！"
                        )
                    else:
                        return False, (
                            f"❌ 任务序列结构错误: 打靶求解组 #{target_idx + 1} 内部包含了寻优变量 Vary，但没有包含任何点火步骤 (FiniteBurn/ImpulsiveBurn)！\n"
                            f"👉 请在打靶组内添加点火步骤，或点击打靶组卡片头部的 [ Burn 点火 ] 按钮。"
                        )
            inside_target = False

    return True, None

def run_mission_sequence(craft_data, mission_sequence, propagator_configs=None, custom_coordinate_systems=None, solver_configs=None):
    """
    Assembles, saves, and executes a GMAT mission sequence script, then parses and returns results.
    """
    init_gmat()

    has_hardware = all(
        float(craft_data.get(key, 0.0) or 0.0) > 0.0
        for key in ("fuel_mass", "fuel_density", "tank_volume", "thrust", "isp")
    ) and bool(craft_data.get("engine_name"))

    gmat_root = os.path.dirname(BIN_DIR)
    output_dir = os.path.join(gmat_root, "output")
    os.makedirs(output_dir, exist_ok=True)
    report_file = os.path.join(output_dir, "mission_traj.txt")
    script_file = os.path.join(output_dir, "mission_run.script")

    # 1. Pre-flight Mission Sequence Semantic Validation
    is_valid_seq, seq_err = validate_mission_sequence_structure(mission_sequence)
    if not is_valid_seq:
        print(f"[GmatEngine] Pre-flight Validation Failed: {seq_err.encode('ascii', 'replace').decode('ascii')}")
        return {
            "success": False,
            "error": seq_err,
            "error_details": seq_err,
            "gmat_log": seq_err,
            "trajectory": [],
            "solved_burns": [],
            "reports": []
        }

    # Propellant feasibility check: record advisory warning instead of hard blocking
    feasibility_warning = check_propellant_feasibility(craft_data, mission_sequence)
    if feasibility_warning:
        print(f"[GmatEngine] Propellant notice: {feasibility_warning}")

    # Clear previous GMAT report files.  Stale rows would make a node invalid.
    for stale_report in (report_file, report_file + ".finite.txt"):
        if os.path.exists(stale_report):
            try:
                os.remove(stale_report)
            except Exception:
                pass

    # Build script
    custom_coords = custom_coordinate_systems or []
    script_content = build_gmat_script(craft_data, mission_sequence, report_file, propagator_configs=propagator_configs, custom_coordinate_systems=custom_coords, solver_configs=solver_configs)
    with open(script_file, "w", encoding="utf-8") as f:
        f.write(script_content)

    print(f"[GmatEngine] Script written to {script_file}")

    # gmatpy keeps the Sandbox alive across HTTP requests. Clear both the
    # object map and command sequence so report wrappers, burns, and solvers
    # from an earlier request cannot contaminate this native run.
    try:
        gmat.ClearCommandSeq()
    except Exception:
        pass
    try:
        gmat.Clear()
    except Exception:
        pass

    # Direct GMAT logging to dedicated session log file for precise diagnostic capture
    run_log_file = os.path.join(output_dir, "GmatRunLog.txt")
    if os.path.exists(run_log_file):
        try:
            os.remove(run_log_file)
        except OSError:
            pass
    try:
        gmat.UseLogFile(run_log_file)
        gmat.EchoLogFile(True)
    except Exception:
        pass

    # Load and execute script
    gmat.LoadScript(script_file)
    run_ok = gmat.RunScript()
    if run_ok is False:
        diagnostic = "GMAT 求解未收敛或执行中止"
        # Prioritize searching dedicated run log file for exact parser/solver errors
        for log_candidate in [
            run_log_file,
            os.path.join(output_dir, "DifferentialCorrectorDC.data"),
            os.path.join(output_dir, "GmatLog.txt"),
            os.path.join(BIN_DIR, "GmatLog.txt")
        ]:
            if os.path.exists(log_candidate):
                try:
                    with open(log_candidate, "r", encoding="utf-8", errors="replace") as log:
                        log_lines = [line.strip() for line in log.readlines() if line.strip()]
                    errs = [l for l in log_lines if any(kw in l for kw in ("Exception", "ERROR", "WARNING", "did not converge", "exceeded", "failed"))]
                    # Filter out benign GUI plugin warnings
                    errs = [l for l in errs if not any(b in l for b in ("GroundTrack", "libOVtoOFI", "libPythonInterface", "libMatlabInterface", "libFminconOptimizer"))]
                    if errs:
                        diagnostic = " | ".join(errs[-2:])
                        break
                    elif log_lines and diagnostic == "GMAT 求解未收敛或执行中止":
                        meaningful = [l for l in log_lines if not any(b in l for b in ("GroundTrack", "libOVtoOFI", "Successfully interpreted"))]
                        if meaningful:
                            diagnostic = " | ".join(meaningful[-2:])
                            break
                except OSError:
                    pass
        return {
            "success": False,
            "error": diagnostic,
            "script_path": script_file,
            "solved_burns": [],
            "updated_sequence": mission_sequence,
            "calculation_authority": "GMAT_NATIVE_ONLY"
        }

    sat_name = "Sat"
    central_body = map_body_name(craft_data.get("central_body", "Earth"))
    sat = gmat.GetRuntimeObject(sat_name)

    final_state = {}
    if sat:
        for p in ["X", "Y", "Z", "VX", "VY", "VZ", "TotalMass", "SMA", "ECC", "INC", "RAAN", "AOP", "TA"]:
            try:
                final_state[p.lower()] = sat.GetRealParameter(p)
            except Exception:
                pass
        try:
            final_state["epoch"] = sat.GetStringParameter("Epoch")
        except Exception:
            final_state["epoch"] = ""

    if has_hardware and "totalmass" in final_state:
        dry_mass = float(craft_data.get("dry_mass", 0.0) or 0.0)
        final_fuel_mass = final_state["totalmass"] - dry_mass
        final_state["fuel_mass"] = final_fuel_mass
        if final_fuel_mass < -1.0e-8:
            fuel_deficit = -final_fuel_mass
            fuel_needed = float(craft_data.get("fuel_mass", 0.0) or 0.0) + fuel_deficit
            final_state["has_fuel_deficit"] = True
            final_state["fuel_deficit"] = round(fuel_deficit, 2)
            final_state["warning"] = (
                f"推进剂短缺提醒：当前飞船仅有 {float(craft_data.get('fuel_mass', 0.0)):.1f} kg 推进剂，"
                f"该机动理论需消耗 {fuel_needed:.1f} kg（短缺 {fuel_deficit:.1f} kg）。"
                "GMAT 已按理论需求计算完成点火参数。"
            )

    # 1. Update burns from GMAT runtime objects (differential corrector solved values)
    finite_number = 0
    for step_index, step in enumerate(mission_sequence):
        bname = step.get("_obj_name")
        if step.get("type") == "FiniteBurn" and bname:
            try:
                duration_obj = gmat.GetRuntimeObject(f"{bname}_Duration")
                if duration_obj:
                    step["duration"] = float(duration_obj.GetRealParameter("Value"))
            except Exception as ex:
                print(f"[gmat_engine] Error reading solved finite-burn duration {bname}: {ex}")
        if step.get("type") == "ImpulsiveBurn" and bname:
            try:
                b_obj = gmat.GetRuntimeObject(bname)
                if b_obj:
                    v1 = b_obj.GetRealParameter("Element1")
                    v2 = b_obj.GetRealParameter("Element2")
                    v3 = b_obj.GetRealParameter("Element3")
                    step["dv_v"] = round(v1 * 1000.0, 1)
                    step["dv_n"] = round(v2 * 1000.0, 1)
                    step["dv_b"] = round(v3 * 1000.0, 1)
                    if "_targeted_by_idx" in step:
                        t_idx = step["_targeted_by_idx"]
                        mission_sequence[t_idx]["dv_v"] = step["dv_v"]
                        mission_sequence[t_idx]["dv_n"] = step["dv_n"]
                        mission_sequence[t_idx]["dv_b"] = step["dv_b"]
                    if "_targets_burn_idx" in step:
                        b_idx = step["_targets_burn_idx"]
                        mission_sequence[b_idx]["dv_v"] = step["dv_v"]
                        mission_sequence[b_idx]["dv_n"] = step["dv_n"]
                        mission_sequence[b_idx]["dv_b"] = step["dv_b"]
            except Exception as ex:
                print(f"[gmat_engine] Error reading runtime burn {bname}: {ex}")

    # Read trajectory points
    traj_points = []
    if os.path.exists(report_file):
        with open(report_file, "r", encoding="utf-8") as f:
            for line in f:
                parts = line.strip().split()
                if len(parts) >= 7:
                    try:
                        traj_points.append({
                            "t": float(parts[0]),
                            "x": float(parts[1]),
                            "y": float(parts[2]),
                            "z": float(parts[3]),
                            "vx": float(parts[4]),
                            "vy": float(parts[5]),
                            "vz": float(parts[6]),
                            "mass": float(parts[7]) if len(parts) > 7 else 0.0
                        })
                    except ValueError:
                        continue

    # Extract solved burns using GMAT event variables and report rows only.
    solved_burns = []
    def finite_burn_vnb_delta(start, inertial_delta):
        """Project GMAT FiniteBurn.GetDeltaVInertial() into the start-state VNB frame."""
        rx, ry, rz = start[2], start[3], start[4]
        vx0, vy0, vz0 = start[5], start[6], start[7]
        dvx, dvy, dvz = inertial_delta

        def norm(x, y, z):
            length = math.sqrt(x * x + y * y + z * z)
            if length <= 0.0:
                raise RuntimeError("GMAT finite-burn report contains a zero-length reference vector")
            return x / length, y / length, z / length

        vhat = norm(vx0, vy0, vz0)
        nhat = norm(ry * vz0 - rz * vy0, rz * vx0 - rx * vz0, rx * vy0 - ry * vx0)
        bhat = norm(vhat[1] * nhat[2] - vhat[2] * nhat[1],
                    vhat[2] * nhat[0] - vhat[0] * nhat[2],
                    vhat[0] * nhat[1] - vhat[1] * nhat[0])
        dv_v = (dvx * vhat[0] + dvy * vhat[1] + dvz * vhat[2]) * 1000.0
        dv_n = (dvx * nhat[0] + dvy * nhat[1] + dvz * nhat[2]) * 1000.0
        dv_b = (dvx * bhat[0] + dvy * bhat[1] + dvz * bhat[2]) * 1000.0
        return dv_v, dv_n, dv_b

    def state_at_or_before(offset):
        candidates = [point for point in traj_points if point["t"] <= offset + 1e-6]
        if not candidates: return None
        point = candidates[-1]
        return {"burn_x": point["x"], "burn_y": point["y"], "burn_z": point["z"], "burn_vx": point["vx"], "burn_vy": point["vy"], "burn_vz": point["vz"]}
    cum_time = 0.0
    for step in mission_sequence:
        stype = step.get("type")
        if stype == "Propagate":
            stop_type = step.get("stop_type", "Time")
            if stop_type in ("Time", "ElapsedSecs"):
                cum_time += float(step.get("duration", 3600.0))
            elif stop_type in ("Days", "ElapsedDays"):
                cum_time += float(step.get("duration", 1.0)) * 86400.0
            else:
                # Event time is resolved by GMAT.  Do not invent a duration
                # for an event stop; the next burn will use its GMAT variable.
                if traj_points:
                    cum_time = traj_points[-1]["t"]
        elif stype in ("ImpulsiveBurn", "TargetApoapsis", "Targeter"):
            if stype in ("TargetApoapsis", "Targeter") and "_targets_burn_idx" in step:
                continue
            bname = step.get("_obj_name")
            burn_epoch_offset = cum_time
            if bname:
                try:
                    t_var = gmat.GetRuntimeObject(f"{bname}_Time")
                    if t_var:
                        burn_epoch_offset = float(t_var.GetRealParameter("Value"))
                except Exception as ex:
                    print(f"[gmat_engine] Could not get {bname}_Time: {ex}")

            dv_v = float(step.get("dv_v", 0.0))
            dv_n = float(step.get("dv_n", 0.0))
            dv_b = float(step.get("dv_b", 0.0))
            mag = (dv_v**2 + dv_n**2 + dv_b**2) ** 0.5
            origin = map_body_name(step.get("origin") or central_body)
            solved = {
                "type": "Impulsive",
                "name": step.get("label") or step.get("name") or "Burn",
                "origin": origin,
                "dv_v": dv_v,
                "dv_n": dv_n,
                "dv_b": dv_b,
                "magnitude": round(mag, 1),
                "epoch_offset": burn_epoch_offset,
                "has_native_delta_v": True,
                "principia_writeable": True,
                "native_result_note": "ImpulsiveBurn Element1/2/3 read from the GMAT runtime object."
            }
            state = state_at_or_before(burn_epoch_offset)
            if state: solved.update(state)
            solved_burns.append(solved)
            cum_time = burn_epoch_offset

        elif stype == "FiniteBurn":
            fbname = step.get("_obj_name")
            burn_epoch_offset = cum_time
            if fbname:
                try:
                    t_var = gmat.GetRuntimeObject(f"{fbname}_Time")
                    if t_var:
                        burn_epoch_offset = float(t_var.GetRealParameter("Value"))
                except Exception as ex:
                    print(f"[gmat_engine] Could not get {fbname}_Time: {ex}")

            finite_rows = []
            if os.path.exists(report_file + ".finite.txt"):
                with open(report_file + ".finite.txt", "r", encoding="utf-8") as f:
                    for line in f:
                        parts = line.strip().split()
                        if len(parts) >= 11:
                            try:
                                finite_rows.append([float(value) for value in parts])
                            except ValueError:
                                continue
            if not finite_rows:
                raise RuntimeError("GMAT did not report finite-burn states")
            last_burn_row = finite_rows[-1]
            gm_start_time = last_burn_row[0]
            dur = last_burn_row[1]
            dv_thrust = last_burn_row[2]
            dv_v = last_burn_row[3]
            dv_n = last_burn_row[4]
            dv_b = last_burn_row[5]
            fuel_burned = last_burn_row[6]
            orb_dv_mag = last_burn_row[7]
            gravity_loss = last_burn_row[8]
            gm_start_mass = last_burn_row[9]
            gm_finish_mass = last_burn_row[10]
            burn_x = last_burn_row[11] if len(last_burn_row) > 11 else 0.0
            burn_y = last_burn_row[12] if len(last_burn_row) > 12 else 0.0
            burn_z = last_burn_row[13] if len(last_burn_row) > 13 else 0.0
            burn_vx = last_burn_row[14] if len(last_burn_row) > 14 else 0.0
            burn_vy = last_burn_row[15] if len(last_burn_row) > 15 else 0.0
            burn_vz = last_burn_row[16] if len(last_burn_row) > 16 else 0.0

            thrust_kn = required_number(craft_data, "thrust") / 1000.0
            isp = required_number(craft_data, "isp")
            origin = map_body_name(step.get("origin") or central_body)
            solved = {
                "type": "Finite",
                "name": step.get("label") or "FiniteBurn",
                "origin": origin,
                "duration": round(dur, 2),
                "thrust_kn": thrust_kn,
                "isp": isp,
                "fuel_burned": round(fuel_burned, 2),
                "dv_v": round(dv_v, 2),
                "dv_n": round(dv_n, 2),
                "dv_b": round(dv_b, 2),
                "magnitude": round(dv_thrust, 2),
                "epoch_offset": round(gm_start_time, 2),
                "end_epoch_offset": round(gm_start_time + dur, 2),
                "has_native_delta_v": True,
                "principia_writeable": True,
                "orbital_delta_v": {
                    "magnitude": round(orb_dv_mag, 2)
                },
                "gravity_loss_mps": round(gravity_loss, 2),
                "native_result_note": "100% GMAT native C++ evaluated physics variables."
            }
            solved.update({
                "burn_x": burn_x, "burn_y": burn_y, "burn_z": burn_z,
                "burn_vx": burn_vx, "burn_vy": burn_vy, "burn_vz": burn_vz
            })
            solved_burns.append(solved)
            cum_time = gm_start_time + dur
            finite_number += 1

    # Auto-render trajectory plot image for KSP sub-window
    render_image_path = os.path.join(output_dir, "gmat_orbit_render.png")
    try:
        from orbit_renderer import render_gmat_trajectory
        render_gmat_trajectory(report_file, render_image_path, view_mode="dual", central_body=central_body)
    except Exception as ex:
        print(f"[gmat_engine] Render trajectory error: {ex}")

    # Parse DifferentialCorrector data across all potential solver data files
    dc_info = {}
    import glob
    dc_files = glob.glob(os.path.join(output_dir, "DifferentialCorrector*.data"))
    if dc_files:
        # Sort by modification time, newest first
        dc_files.sort(key=lambda x: os.path.getmtime(x), reverse=True)
        dc_file = dc_files[0]
        try:
            with open(dc_file, "r", encoding="utf-8", errors="ignore") as f:
                dc_text = f.read()
            m_goal = re.findall(r"Desired:\s*([\d\.\-]+)\s*Achieved:\s*([\d\.\-]+)", dc_text)
            m_tol = re.findall(r"Tolerance:\s*([\d\.\-eE]+)", dc_text)
            desired, achieved = m_goal[-1] if m_goal else (None, None)
            dc_tol = float(m_tol[-1]) if m_tol else 0.1
            is_within_tol = False
            if desired is not None and achieved is not None:
                is_within_tol = abs(float(achieved) - float(desired)) <= (dc_tol * 1.05)
            dc_converged = ("Targeting Completed" in dc_text) and is_within_tol
            m_iter = re.search(r"Targeting Completed in (\d+) iterations", dc_text)
            iterations = int(m_iter.group(1)) if m_iter else None
            dc_info = {
                "converged": dc_converged,
                "iterations": iterations,
                "desired": float(desired) if desired else None,
                "achieved": float(achieved) if achieved else None,
                "variance": round(float(achieved) - float(desired), 4) if achieved and desired else None
            }
            if not dc_converged and desired is not None and achieved is not None:
                target_warn = f"打靶未达到指定容差 (目标: {desired}, 实际: {achieved}, 容差: {dc_tol})"
                feasibility_warning = f"{feasibility_warning} | {target_warn}" if feasibility_warning else target_warn
        except Exception as ex:
            print(f"[gmat_engine] DC parse error: {ex}")

    # Parse individual step reports
    step_reports = []
    for s_idx, st in enumerate(mission_sequence):
        if st.get("type") == "Report":
            rf_file = os.path.join(output_dir, f"step_report_{s_idx}.txt")
            label = st.get("label") or f"步骤 #{s_idx + 1} 状态报告"
            rep_data = {
                "step_index": s_idx,
                "step_label": label,
                "elapsed_secs": 0.0,
                "epoch_utc": final_state.get("epoch", ""),
                "altitude_km": 0.0,
                "sma_km": 0.0,
                "ecc": 0.0,
                "inc_deg": 0.0,
                "raan_deg": 0.0,
                "aop_deg": 0.0,
                "ta_deg": 0.0,
                "rmag_km": 0.0,
                "total_mass_kg": 0.0,
                "fuel_mass_kg": 0.0,
                "x": 0.0, "y": 0.0, "z": 0.0,
                "vx": 0.0, "vy": 0.0, "vz": 0.0,
                "raw_report_text": ""
            }
            if os.path.exists(rf_file):
                try:
                    with open(rf_file, "r", encoding="utf-8", errors="ignore") as rf:
                        raw = rf.read()
                    rep_data["raw_report_text"] = raw
                    rows = [line.strip().split() for line in raw.splitlines() if line.strip() and not line.strip().startswith("%") and not "ElapsedSecs" in line]
                    if rows:
                        last_row = rows[-1]
                        vals = [float(v) for v in last_row if re.match(r"^[\d\.\-+eE]+$", v)]
                        if len(vals) >= 10:
                            rep_data["elapsed_secs"] = round(vals[0], 2)
                            rep_data["altitude_km"] = round(vals[1], 2)
                            rep_data["sma_km"] = round(vals[2], 2)
                            rep_data["ecc"] = round(vals[3], 4)
                            rep_data["inc_deg"] = round(vals[4], 2)
                            rep_data["raan_deg"] = round(vals[5], 2)
                            rep_data["aop_deg"] = round(vals[6], 2)
                            rep_data["ta_deg"] = round(vals[7], 2)
                            rep_data["rmag_km"] = round(vals[8], 2)
                            rep_data["total_mass_kg"] = round(vals[9], 1)
                        if len(vals) >= 16:
                            rep_data["x"] = round(vals[10], 2)
                            rep_data["y"] = round(vals[11], 2)
                            rep_data["z"] = round(vals[12], 2)
                            rep_data["vx"] = round(vals[13], 4)
                            rep_data["vy"] = round(vals[14], 4)
                            rep_data["vz"] = round(vals[15], 4)
                except Exception as ex:
                    print(f"[gmat_engine] Error reading {rf_file}: {ex}")

            # Fallback to final state / trajectory if report file parsing had zeros
            if rep_data["sma_km"] == 0.0 and final_state:
                rep_data["sma_km"] = round(final_state.get("sma", 0.0), 2)
                rep_data["ecc"] = round(final_state.get("ecc", 0.0), 4)
                rep_data["inc_deg"] = round(final_state.get("inc", 0.0), 2)
                rep_data["raan_deg"] = round(final_state.get("raan", 0.0), 2)
                rep_data["aop_deg"] = round(final_state.get("aop", 0.0), 2)
                rep_data["ta_deg"] = round(final_state.get("ta", 0.0), 2)
                rep_data["total_mass_kg"] = round(final_state.get("totalmass", 0.0), 1)
                rep_data["fuel_mass_kg"] = round(final_state.get("fuel_mass", 0.0), 1)
                rep_data["x"] = round(final_state.get("x", 0.0), 2)
                rep_data["y"] = round(final_state.get("y", 0.0), 2)
                rep_data["z"] = round(final_state.get("z", 0.0), 2)
                rep_data["vx"] = round(final_state.get("vx", 0.0), 4)
                rep_data["vy"] = round(final_state.get("vy", 0.0), 4)
                rep_data["vz"] = round(final_state.get("vz", 0.0), 4)
                if traj_points:
                    rep_data["elapsed_secs"] = round(traj_points[-1].get("t", 0.0), 2)

            step_reports.append(rep_data)

    # Check if targeters converged
    has_target = any(s.get("type") in ("Target", "Targeter", "TargetApoapsis") for s in mission_sequence)
    if has_target and dc_info and dc_info.get("converged") is False:
        n_vars = sum(1 for s in mission_sequence if s.get("type") == "Vary")
        n_goals = sum(1 for s in mission_sequence if s.get("type") == "Achieve")
        v_str = f"{dc_info.get('variance')}" if dc_info.get('variance') is not None else "超差"
        d_str = f"{dc_info.get('desired')}" if dc_info.get('desired') is not None else "未知"
        diag_msg = f"打靶未收敛 (目标期望: {d_str}, 最终残差: {v_str})"
        if n_goals > n_vars:
            diag_msg += f"\n⚠️ 物理自由度不足: 当前打靶包含 {n_goals} 个 Achieve 目标约束 (如 Z=0 和 SMA=42164)，但仅配置了 {n_vars} 个 Vary 变量 (burntime)。\n单一标量点火时长无法同时满足轨道面内提升与面外倾角消除。请添加法向变轨变量 (如 GEO_Burn.Element2)！"
        return {
            "success": False,
            "error": "GMAT 差分修正器未收敛 (达到最大迭代上限)",
            "error_details": diag_msg,
            "script_path": script_file,
            "dc_summary": dc_info,
            "final_state": final_state,
            "solved_burns": solved_burns,
            "updated_sequence": mission_sequence,
            "calculation_authority": "GMAT_NATIVE_ONLY"
        }

    return {
        "success": True,
        "step_reports": step_reports,
        "error": None,
        "warning": final_state.get("warning") or feasibility_warning,
        "has_fuel_deficit": final_state.get("has_fuel_deficit", False),
        "fuel_deficit": final_state.get("fuel_deficit", 0.0),
        "script_path": script_file,
        "final_state": final_state,
        "final_sma": final_state.get("sma", 0.0),
        "final_ecc": final_state.get("ecc", 0.0),
        "final_inc": final_state.get("inc", 0.0),
        "final_raan": final_state.get("raan", 0.0),
        "final_aop": final_state.get("aop", 0.0),
        "final_ta": final_state.get("ta", 0.0),
        "final_mass": final_state.get("totalmass", 0.0),
        "final_epoch": final_state.get("epoch", ""),
        "trajectory_count": len(traj_points),
        "trajectory_sample": traj_points[::max(1, len(traj_points)//50)], # sample up to 50 pts for GUI
        "solved_burns": solved_burns,
        "updated_sequence": mission_sequence,
        "render_image_path": render_image_path,
        "dc_summary": dc_info,
        "calculation_authority": "GMAT_NATIVE_ONLY",
        "finite_burn_report_path": report_file + ".finite.txt"
    }


def get_propagator_api_metadata():
    """
    Dynamically introspects official GMAT C++ API to return all supported
    integrators, force model components, and tunable parameter specs.
    """
    init_gmat()
    global gmat
    
    supported_integrators = []
    for it in ["RungeKutta89", "PrinceDormand78", "RungeKutta56", "RungeKutta68", "AdamsBashforthMoulton"]:
        try:
            integ = gmat.Construct(it, f"Probe_{it}")
            if integ:
                supported_integrators.append({
                    "name": it,
                    "label": it,
                    "order": 8 if "89" in it else 7 if "78" in it else 5 if "56" in it else 6 if "68" in it else 4,
                    "adaptive": True if "SPK" not in it else False
                })
        except:
            pass

    # Inspect Propagator properties via GMAT API
    prop_params = []
    try:
        sample_prop = gmat.Construct("Propagator", "SampleProp")
        if sample_prop and hasattr(sample_prop, "GetParameterCount"):
            for i in range(sample_prop.GetParameterCount()):
                pname = sample_prop.GetParameterText(i)
                ptype = sample_prop.GetParameterTypeString(i)
                if pname not in ("FM", "Covariance", "Type"):
                    prop_params.append({
                        "name": pname,
                        "type": ptype,
                        "id": i
                    })
    except Exception as ex:
        print(f"[gmat_engine] Prop metadata probe: {ex}")

    # Inspect ForceModel properties via GMAT API
    fm_params = []
    try:
        sample_fm = gmat.Construct("ForceModel", "SampleFM")
        if sample_fm and hasattr(sample_fm, "GetParameterCount"):
            for i in range(sample_fm.GetParameterCount()):
                pname = sample_fm.GetParameterText(i)
                ptype = sample_fm.GetParameterTypeString(i)
                fm_params.append({
                    "name": pname,
                    "type": ptype,
                    "id": i
                })
    except Exception as ex:
        print(f"[gmat_engine] ForceModel metadata probe: {ex}")

    return {
        "success": True,
        "integrators": supported_integrators,
        "propagator_parameters": prop_params,
        "force_model_parameters": fm_params,
        "default_primary_bodies": ["Earth", "Luna", "Sun", "Mars", "Jupiter"],
        "default_atmosphere_models": ["None", "Exponential", "MSISE90", "JacchiaRoberts"]
    }


def get_solver_api_metadata():
    """
    Dynamically introspects official GMAT C++ API to return all supported
    differential correctors, optimization algorithms, and tunable solver specs.
    """
    init_gmat()
    global gmat

    algorithms = ["NewtonRaphson", "Broyden", "ModifiedBroyden"]
    derivative_methods = ["ForwardDifference", "CentralDifference", "BackwardDifference"]
    report_styles = ["Normal", "Concise", "Verbose"]
    solve_modes = ["Solve", "Calculate", "RunInitialGuess"]
    exit_modes = ["SaveAndContinue", "DiscardAndContinue", "Stop"]

    solver_params = []
    try:
        sample_dc = gmat.Construct("DifferentialCorrector", "SampleDC")
        if sample_dc and hasattr(sample_dc, "GetParameterCount"):
            for i in range(sample_dc.GetParameterCount()):
                pname = sample_dc.GetParameterText(i)
                ptype = sample_dc.GetParameterTypeString(i)
                enums = []
                if hasattr(sample_dc, "GetPropertyEnumStrings"):
                    try:
                        enums = list(sample_dc.GetPropertyEnumStrings(i))
                    except:
                        pass
                solver_params.append({
                    "name": pname,
                    "type": ptype,
                    "enums": enums,
                    "id": i
                })
    except Exception as ex:
        print(f"[gmat_engine] Solver metadata probe: {ex}")

    return {
        "success": True,
        "solver_type": "DifferentialCorrector",
        "algorithms": algorithms,
        "derivative_methods": derivative_methods,
        "report_styles": report_styles,
        "solve_modes": solve_modes,
        "exit_modes": exit_modes,
        "parameters": solver_params
    }
