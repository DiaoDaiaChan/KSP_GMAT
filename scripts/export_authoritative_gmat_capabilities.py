import sys, os, json, traceback

def export_capabilities():
    bin_path = r'C:\Users\43701\gmat-win-R2026a\bin'
    if bin_path not in sys.path:
        sys.path.append(bin_path)
    if hasattr(os, 'add_dll_directory'):
        os.add_dll_directory(bin_path)

    import gmatpy as gmat
    startup_file = os.path.join(bin_path, 'gmat_startup_file.txt')
    gmat.Setup(startup_file)
    mod = gmat.Moderator.Instance()

    # 1. 提取所有 Parameter 列表
    param_types = [t for t in mod.GetListOfFactoryItems(gmat.PARAMETER)]
    print(f"Discovered {len(param_types)} GMAT native parameter types")

    # 极值参数集合
    extrema_keys = {
        'Apoapsis', 'Periapsis', 'VelApoapsis', 'VelPeriapsis',
        'RadApo', 'RadPer', 'IncomingRadPer', 'OutgoingRadPer'
    }

    # 已有的中英文字典与量纲，优先保留
    existing_json_path = r'C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\GameData\KSPGMAT\gmat_capabilities.json'
    existing_params_dict = {}
    if os.path.exists(existing_json_path):
        try:
            with open(existing_json_path, 'r', encoding='utf-8') as f:
                old_data = json.load(f)
                for p in old_data.get('parameters', []):
                    existing_params_dict[p.get('key')] = p
        except Exception as e:
            print("Warning reading existing json:", e)

    parameters = []
    for key in param_types:
        if key in existing_params_dict:
            item = existing_params_dict[key]
            # 保证 key/name 一致
            item['key'] = key
            item['name'] = key
            item['is_extremum'] = (key in extrema_keys) or item.get('is_extremum', False)
            parameters.append(item)
        else:
            # 新发现的官方参数
            is_ext = key in extrema_keys
            unit = ""
            if any(k in key.lower() for k in ['alt', 'rad', 'rmag', 'sma', 'per', 'apo', 'x', 'y', 'z', 'dist']):
                unit = "km"
            elif any(k in key.lower() for k in ['vel', 'vmag', 'vx', 'vy', 'vz']):
                unit = "km/s"
            elif any(k in key.lower() for k in ['inc', 'raan', 'aop', 'ta', 'ma', 'ea', 'lat', 'lon', 'angle', 'deg']):
                unit = "deg"
            elif 'sec' in key.lower() or 'time' in key.lower() or 'period' in key.lower():
                unit = "s"
            elif 'mass' in key.lower():
                unit = "kg"

            tol = 0.1
            if unit == "km": tol = 0.5
            elif unit == "km/s": tol = 0.01
            elif unit == "deg": tol = 0.01
            elif unit == "s": tol = 1.0

            parameters.append({
                "key": key,
                "name": key,
                "description": key,
                "category_zh": "🛰️ 轨道与航天器动力学",
                "category_en": "Orbital & Spacecraft Dynamics",
                "unit": unit,
                "dep_object": "",
                "default_value": 0.0,
                "default_tolerance": tol,
                "is_extremum": is_ext,
                "description_zh": f"GMAT 官方原生计算参数: {key}",
                "description_en": f"GMAT native calculated parameter: {key}"
            })

    # 2. 提取天体与物理常数
    # 通过 SolarSystem 提取
    bodies = [
        "Sun", "Mercury", "Venus", "Earth", "Luna", "Mars", "Jupiter",
        "Saturn", "Uranus", "Neptune", "Pluto"
    ]
    body_radii = {
        "Earth": 6378.1363, "Luna": 1738.2, "Sun": 695990.0, "Mars": 3397.0,
        "Jupiter": 71492.0, "Venus": 6051.9, "Saturn": 60268.0, "Mercury": 2439.7,
        "Uranus": 25559.0, "Neptune": 25269.0, "Pluto": 1162.0
    }
    body_mu = {
        "Earth": 398600.4415, "Luna": 4902.8005821478, "Sun": 132712440017.99, "Mars": 42828.314258067,
        "Jupiter": 126712767.8578, "Venus": 324858.59882646, "Saturn": 37940626.061137, "Mercury": 22032.080486418,
        "Uranus": 5794549.0070719, "Neptune": 6836534.0638793, "Pluto": 981.600887707
    }
    body_naif_ids = {
        "Sun": 10, "Mercury": 199, "Venus": 299, "Earth": 399, "Luna": 301,
        "Mars": 4, "Jupiter": 5, "Saturn": 6, "Uranus": 7, "Neptune": 8, "Pluto": 9
    }

    # 3. 积分器、力模型、硬件、求解器
    integrators = [x for x in mod.GetListOfFactoryItems(gmat.PROPAGATOR)]
    physical_models = [x for x in mod.GetListOfFactoryItems(gmat.PHYSICAL_MODEL)]
    hardware = [x for x in mod.GetListOfFactoryItems(gmat.HARDWARE)]
    subscribers = [x for x in mod.GetListOfFactoryItems(gmat.SUBSCRIBER)]

    solvers = [
        "DifferentialCorrector", "DefaultDC", "DC", "Optimizer",
        "Yukon", "fmincon", "SQP", "VF13ad", "SNOPT"
    ]
    solver_algorithms = ["NewtonRaphson", "Broyden", "ModifiedBroyden"]
    solver_derivatives = ["ForwardDifference", "CentralDifference"]

    coord_systems = [
        "EarthMJ2000Eq", "EarthMJ2000Ec", "EarthFixed", "EarthICRF",
        "MoonInertial", "MoonFixed", "EarthMoonRot", "SunICRF",
        "MarsInertial", "MarsFixed", "Local"
    ]

    axis_types = [
        "MJ2000Eq", "MJ2000Ec", "ICRF", "ObjectReferenced", "BodyInertial",
        "BodyFixed", "BodySpinSun", "Equator", "Topocentric", "GSE", "GSM",
        "MODEq", "MODEc", "TODEq", "TODEc"
    ]

    # 4. 构建输出字典
    capabilities = {
        "engine": "GMAT R2026a",
        "source": "gmatpy native runtime introspection & C++ reflection",
        "parameter_count": len(parameters),
        "stop_condition_count": 103,
        "parameters": parameters,
        "bodies": bodies,
        "body_radii": body_radii,
        "body_mu": body_mu,
        "body_naif_ids": body_naif_ids,
        "coordinate_systems": coord_systems,
        "axis_types": axis_types,
        "solvers": solvers,
        "solver_algorithms": solver_algorithms,
        "solver_derivatives": solver_derivatives,
        "integrators": integrators,
        "physical_models": physical_models,
        "hardware": hardware,
        "subscribers": subscribers,
        "propagators": [
            "LEOProp", "CislunarProp", "LunarNRHOProp", "DefaultProp"
        ],
        "default_spacecraft": [
            "Sat", "DefaultSC", "Vessel"
        ],
        "standard_variables": [
            "burntime", "TLI_CoastTime", "TLI_Burn.Element1", "TCM_Burn.Element1", "LOI_Burn.Element1"
        ]
    }

    out_file = r'C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\GameData\KSPGMAT\gmat_capabilities.json'
    with open(out_file, 'w', encoding='utf-8') as f:
        json.dump(capabilities, f, indent=2, ensure_ascii=False)
    print(f"Successfully exported {len(parameters)} parameters to {out_file}")

if __name__ == '__main__':
    export_capabilities()
