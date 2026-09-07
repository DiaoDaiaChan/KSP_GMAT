import sys, os, json, ast, time

def parse_py_file(fpath, module_name):
    with open(fpath, 'r', encoding='utf-8', errors='ignore') as f:
        src = f.read()
    lines = src.splitlines()
    tree = ast.parse(src)
    
    top_funcs = []
    classes = {}
    
    for node in tree.body:
        if isinstance(node, ast.FunctionDef):
            if not node.name.startswith('__') and not node.name.startswith('_swig_'):
                code = '\n'.join(lines[node.lineno-1:node.end_lineno])
                top_funcs.append({
                    'name': node.name,
                    'module': module_name,
                    'args': [a.arg for a in node.args.args],
                    'doc': ast.get_docstring(node) or '',
                    'impl': code.strip()
                })
        elif isinstance(node, ast.ClassDef):
            if not node.name.startswith('_Swig'):
                methods = []
                for sub in node.body:
                    if isinstance(sub, ast.FunctionDef):
                        code = '\n'.join(lines[sub.lineno-1:sub.end_lineno])
                        methods.append({
                            'name': sub.name,
                            'args': [a.arg for a in sub.args.args],
                            'doc': ast.get_docstring(sub) or '',
                            'impl': code.strip()
                        })
                classes[node.name] = {
                    'class_name': node.name,
                    'module': module_name,
                    'bases': [b.id if isinstance(b, ast.Name) else (b.attr if isinstance(b, ast.Attribute) else str(b)) for b in node.bases],
                    'method_count': len(methods),
                    'methods': methods
                }
    return top_funcs, classes

def main():
    t0 = time.time()
    base_dir = r'C:\Users\43701\gmat-win-R2026a'
    py_dir = os.path.join(base_dir, r'bin\gmatpy\_py310')
    files_to_parse = ['gmat_py.py', 'navigation_py.py', 'station_py.py']
    
    all_top_functions = []
    all_classes = {}
    
    for fname in files_to_parse:
        fpath = os.path.join(py_dir, fname)
        if os.path.exists(fpath):
            funcs, cls_map = parse_py_file(fpath, fname[:-3])
            all_top_functions.extend(funcs)
            for cname, cinfo in cls_map.items():
                if cname not in all_classes:
                    all_classes[cname] = cinfo
                else:
                    all_classes[cname]["methods"].extend(cinfo["methods"])
                    all_classes[cname]["method_count"] = len(all_classes[cname]["methods"])

    # 提取 userfunctions/python
    uf_dir = os.path.join(base_dir, r'userfunctions\python')
    user_funcs = []
    user_classes = {}
    if os.path.exists(uf_dir):
        for fname in sorted(os.listdir(uf_dir)):
            if fname.endswith('.py'):
                fpath = os.path.join(uf_dir, fname)
                funcs, cls_map = parse_py_file(fpath, f'userfunctions.{fname[:-3]}')
                user_funcs.extend(funcs)
                user_classes.update(cls_map)

    # 提取 utilities/python
    ut_dir = os.path.join(base_dir, r'utilities\python')
    util_funcs = []
    util_classes = {}
    if os.path.exists(ut_dir):
        for root, dirs, files in os.walk(ut_dir):
            for fname in sorted(files):
                if fname.endswith('.py'):
                    fpath = os.path.join(root, fname)
                    rel_mod = os.path.relpath(fpath, base_dir).replace('\\', '.').replace('/', '.')[:-3]
                    funcs, cls_map = parse_py_file(fpath, rel_mod)
                    util_funcs.extend(funcs)
                    util_classes.update(cls_map)

    def get_class_family(name, bases):
        n = name.lower()
        b_str = ' '.join(bases).lower()
        if any(x in n or x in b_str for x in ['thruster', 'fueltank', 'hardware', 'power', 'battery', 'antenna', 'sensor', 'spacecraft', 'spaceobject']):
            return '🚀 航天器与硬件载荷 (Spacecraft & Hardware)'
        if any(x in n or x in b_str for x in ['force', 'gravity', 'drag', 'srp', 'atmosphere', 'potential', 'tide', 'radiation', 'odemodel']):
            return '🌌 动力学与力模型 (Force Models & Physics)'
        if any(x in n or x in b_str for x in ['prop', 'integrator', 'rungekutta', 'rk', 'predictor', 'corrector', 'step']):
            return '⏱️ 积分器与轨道推演 (Propagators & Integrators)'
        if any(x in n or x in b_str for x in ['solver', 'optimizer', 'differentialcorrector', 'sqp', 'yukon', 'fmincon', 'target', 'vary', 'nlc']):
            return '🎯 求解器与轨迹寻优 (Solvers & Optimizers)'
        if any(x in n or x in b_str for x in ['coordinate', 'axis', 'body', 'celestial', 'planet', 'moon', 'star', 'barycenter', 'solarsystem', 'frame']):
            return '🌐 坐标系统与参考天体 (Coordinate Systems & Bodies)'
        if any(x in n or x in b_str for x in ['tracking', 'measurement', 'station', 'estimator', 'filter', 'nav', 'obs', 'residual', 'od']):
            return '📡 测控与定轨导航 (Navigation & Orbit Determination)'
        if any(x in n or x in b_str for x in ['command', 'branch', 'gmatcommand', 'mission']):
            return '📜 任务控制与指令流 (Mission Commands & Flow)'
        if any(x in n or x in b_str for x in ['subscriber', 'report', 'plot', 'view', 'file', 'interface', 'ephem', 'data']):
            return '📊 输出订阅与可视化 (I/O & Visualization)'
        return '⚙️ 底层内核与数学构件 (Core Engine & Math)'

    for name, cinfo in all_classes.items():
        cinfo['family'] = get_class_family(name, cinfo.get('bases', []))

    total_api_methods = sum(c.get('method_count', 0) for c in all_classes.values())
    print(f"Parsed GMAT API in {time.time()-t0:.2f}s:")
    print(f"  - Top-level API functions (with full implementation): {len(all_top_functions)}")
    print(f"  - API Classes: {len(all_classes)} (total {total_api_methods} methods with full implementation)")
    print(f"  - User Functions: {len(user_funcs)} (classes: {len(user_classes)})")
    print(f"  - Utility Functions: {len(util_funcs)} (classes: {len(util_classes)})")

    # 读取并更新 gmat_capabilities.json
    json_path = r'C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\GameData\KSPGMAT\gmat_capabilities.json'
    with open(json_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    # 注入 API 元数据与完整实现
    data["api_top_functions_count"] = len(all_top_functions)
    data["api_classes_count"] = len(all_classes)
    data["api_total_methods_count"] = total_api_methods
    data["api_top_functions"] = all_top_functions
    data["api_classes"] = all_classes
    data["api_classes_list"] = list(all_classes.values())
    data["user_functions_count"] = len(user_funcs)
    data["user_functions"] = user_funcs
    data["user_classes"] = user_classes
    data["utility_functions_count"] = len(util_funcs)
    data["utility_functions"] = util_funcs
    data["utility_classes"] = util_classes


    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

    size_mb = os.path.getsize(json_path) / (1024 * 1024)
    print(f"Successfully updated {json_path} ({size_mb:.2f} MB) with full method implementations!")

if __name__ == '__main__':
    main()
