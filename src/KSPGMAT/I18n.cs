using System.Collections.Generic;

namespace KSPGMAT {
    /// <summary>
    /// Lightweight i18n system for KSP GMAT mod.
    /// Supports Chinese (zh) and English (en) with runtime switching.
    /// Usage: I18n.T("key") returns the localized string.
    /// </summary>
    public static class I18n {
        public enum Lang { ZH, EN }
        public static Lang Current = Lang.ZH;

        public static void Toggle() {
            SetLang((Current == Lang.ZH) ? Lang.EN : Lang.ZH);
        }

        public static void SetLang(Lang lang) {
            Current = lang;
            try {
                GMATParameterRegistry.ReloadCategories();
            } catch {}
        }

        public static void AutoDetect() {
            try {
                string kspLang = KSP.Localization.Localizer.CurrentLanguage;
                if (!string.IsNullOrEmpty(kspLang)) {
                    kspLang = kspLang.ToLowerInvariant();
                    if (kspLang.Contains("zh") || kspLang.Contains("chinese")) {
                        SetLang(Lang.ZH);
                        return;
                    } else if (kspLang.Contains("en") || kspLang.Contains("english")) {
                        SetLang(Lang.EN);
                        return;
                    }
                }
            } catch {}

            try {
                if (System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant().Contains("zh")) {
                    SetLang(Lang.ZH);
                    return;
                }
            } catch {}

            SetLang(Lang.EN);
        }

        private static readonly Dictionary<string, string[]> Dict = new Dictionary<string, string[]> {
            // [0] = Chinese, [1] = English
            // ===== Window & Tabs =====
            {"window_title",               new[]{"⚡ GMAT-Principia 任务规划器", "⚡ GMAT-Principia Mission Planner"}},
            {"tab_craft",                   new[]{"🛰️ 飞船与动力学", "🛰️ Spacecraft"}},
            {"tab_mission",                 new[]{"📋 任务序列与求解", "📋 Mission Sequence"}},
            {"tab_results",                 new[]{"📈 变轨与轨迹明细", "📈 Results & Trajectory"}},
            {"tab_settings",                new[]{"⚙️ 设置", "⚙️ Settings"}},
            {"no_vessel",                   new[]{"无活跃飞船", "No active vessel"}},
            {"target_vessel",               new[]{"🛰️ 目标", "🛰️ Target"}},
            {"close",                       new[]{"关闭", "Close"}},

            // ===== Presets =====
            {"preset_header",               new[]{"任务队列预设模板 (官方开源工程结构)", "Mission Presets (Official Templates)"}},
            {"preset_gto",                  new[]{"🛰️ GTO 转移", "🛰️ GTO Transfer"}},
            {"preset_geo",                  new[]{"🌍 GEO 圆化", "🌍 GEO Circularize"}},
            {"preset_ce6",                  new[]{"🌙 嫦娥六号地月转移 (对齐截图)", "🌙 Chang'e-6 Lunar Transfer"}},
            {"preset_nrho",                 new[]{"🌙 地月 NRHO", "🌙 Lunar NRHO"}},
            {"preset_basic",                new[]{"🔄 基础积分", "🔄 Basic Propagation"}},

            // ===== Step Types (Badge Labels) =====
            {"type_target",                 new[]{"🎯 Target 求解组", "🎯 Target Group"}},
            {"type_vary",                   new[]{"✏️ Vary 寻优", "✏️ Vary"}},
            {"type_achieve",                new[]{"🎯 Achieve 目标", "🎯 Achieve Goal"}},
            {"type_endtarget",              new[]{"↵ End", "↵ End"}},
            {"type_var",                    new[]{"📐 Var 变量", "📐 Variable"}},
            {"type_propagate",              new[]{"🌐 轨道积分", "🌐 Propagate"}},
            {"type_finiteburn",             new[]{"🔥 有限推力点火", "🔥 Finite Burn"}},
            {"type_impulsiveburn",          new[]{"💥 脉冲机动", "💥 Impulsive Burn"}},
            {"type_report",                 new[]{"🧾 状态报告", "🧾 Report"}},
            {"type_equation",               new[]{"📐 状态赋值", "📐 Equation"}},

            // ===== Add Step Buttons =====
            {"btn_add_propagate",           new[]{"➕ 轨道积分", "➕ Propagate"}},
            {"btn_add_target_group",        new[]{"🎯 求解器组", "🎯 Solver Group"}},
            {"btn_add_vary",                new[]{"✏️ 变量寻优", "✏️ Vary"}},
            {"btn_add_achieve",             new[]{"🎯 达成目标", "🎯 Achieve"}},
            {"btn_add_finiteburn",          new[]{"🔥 有限推力", "🔥 Finite Burn"}},
            {"btn_add_impulsiveburn",       new[]{"💥 脉冲机动", "💥 Impulsive Burn"}},
            {"btn_add_var",                 new[]{"📐 变量定义", "📐 Variable"}},
            {"btn_add_endtarget",           new[]{"↵ 结束求解", "↵ End Target"}},
            {"btn_add_report",              new[]{"🧾 状态报告", "🧾 Report"}},
            {"btn_clear",                   new[]{"🗑️ 清空", "🗑️ Clear"}},

            // ===== Step Card Labels =====
            {"label_propagator",            new[]{"函数传播子", "Propagator"}},
            {"label_stop_cond",             new[]{"终止动力学条件", "Stop Condition"}},
            {"label_stop_picker",           new[]{"📋 选择参数 (GMAT 动力学库)...", "📋 Pick Parameter (GMAT Library)..."}},
            {"label_stop_picker_close",     new[]{"▲ 收起选择器", "▲ Close Picker"}},
            {"label_stop_picker_header",    new[]{"选择终止条件参数 (分类库)", "Select Stop Condition Parameter"}},
            {"label_duration_s",            new[]{"秒数 (s)", "Seconds (s)"}},
            {"label_duration_days",         new[]{"天数 (Days)", "Days"}},
            {"label_epoch_utc",             new[]{"历元 (UTC)", "Epoch (UTC)"}},
            {"label_target_val",            new[]{"目标数值", "Target Value"}},
            {"label_tolerance",             new[]{"容差 (Tol)", "Tolerance"}},
            {"label_event_trigger",         new[]{"几何触发事件", "Geometric Event Trigger"}},
            {"label_no_value_needed",       new[]{"无需数值", "No value needed"}},

            // ===== Vary Card =====
            {"vary_variable",               new[]{"待解变量", "Variable"}},
            {"vary_initial",                new[]{"初始猜值", "Initial Guess"}},
            {"vary_perturbation",           new[]{"扰动 (Pert)", "Perturbation"}},
            {"vary_lower",                  new[]{"下界 (Lower)", "Lower Bound"}},
            {"vary_upper",                  new[]{"上界 (Upper)", "Upper Bound"}},
            {"vary_max_step",               new[]{"最大步长", "Max Step"}},

            // ===== Achieve Card =====
            {"achieve_param",               new[]{"目标参数", "Goal Parameter"}},
            {"achieve_val",                 new[]{"目标数值", "Goal Value"}},
            {"achieve_tol",                 new[]{"容差 (Tol)", "Tolerance"}},
            {"achieve_picker_open",         new[]{"📋 目标参数库...", "📋 Goal Library..."}},
            {"achieve_picker_close",        new[]{"▲ 收起库", "▲ Close"}},
            {"achieve_picker_header",       new[]{"选择 Achieve 目标约束参数 (GMAT 官方动力学库)", "Select Achieve Goal (GMAT Parameter Library)"}},

            // ===== Target Card =====
            {"target_label",                new[]{"组名 (Label)", "Group Name"}},
            {"target_dc_algo",              new[]{"DC 算法", "DC Algorithm"}},
            {"target_solve_mode",           new[]{"求解模式", "Solve Mode"}},
            {"target_max_iter",             new[]{"最大迭代", "Max Iterations"}},
            {"target_collapsed_summary",    new[]{"已折叠: 包含 {0} 个子步骤 ({1} Vary, {2} Achieve)", "Collapsed: {0} sub-steps ({1} Vary, {2} Achieve)"}},
            {"target_expand",               new[]{"[+] 展开组", "[+] Expand"}},
            {"target_collapse",             new[]{"[-] 折叠组", "[-] Collapse"}},
            {"target_endtarget_desc",       new[]{"闭合微分修正器循环", "Close DC loop"}},

            // ===== FiniteBurn Card =====
            {"burn_bind_vary",              new[]{"绑定 Vary 变量", "Bind Vary Variable"}},
            {"burn_duration",               new[]{"点火时长 (s)", "Burn Duration (s)"}},
            {"burn_direction",              new[]{"推力方向", "Thrust Direction"}},
            {"burn_prograde",               new[]{"+V 顺行", "+V Prograde"}},
            {"burn_retrograde",             new[]{"-V 逆行", "-V Retrograde"}},
            {"burn_normal",                 new[]{"+N 法向", "+N Normal"}},
            {"burn_antinormal",             new[]{"-N 反法", "-N Anti-Normal"}},
            {"burn_decrement_mass",         new[]{"扣减质量", "Decrement Mass"}},

            // ===== ImpulsiveBurn Card =====
            {"impulse_dv_v",                new[]{"ΔV 顺行 V (m/s)", "ΔV Prograde V (m/s)"}},
            {"impulse_dv_n",                new[]{"法向 N", "Normal N"}},
            {"impulse_dv_b",                new[]{"径向 B", "Radial B"}},
            {"impulse_total",               new[]{"瞬间脉冲机动: 合成 ΔV", "Impulsive Maneuver: Total ΔV"}},

            // ===== Var Card =====
            {"var_name",                    new[]{"用户变量", "User Variable"}},
            {"var_desc",                    new[]{"(GMAT 原生变量定义/赋值语句: Create Variable ...)", "(GMAT native variable definition)"}},

            // ===== Execution Hub =====
            {"exec_title",                  new[]{"⚡ 任务执行中枢 (Pinned)", "⚡ Execution Hub (Pinned)"}},
            {"exec_run",                    new[]{"▶ 启动 GMAT 任务序列求解", "▶ Run GMAT Mission Sequence"}},
            {"exec_running",                new[]{"⏳ GMAT 任务序列求解中...", "⏳ GMAT Solving..."}},
            {"exec_status_computing",       new[]{"GMAT 引擎计算中...", "GMAT engine computing..."}},
            {"exec_status_done",            new[]{"● 求解完成", "● Solve Complete"}},
            {"exec_status_ready",           new[]{"● 就绪", "● Ready"}},

            // ===== Results =====
            {"results_title",               new[]{"📊 动力学求解与机动结果", "📊 Dynamics & Maneuver Results"}},
            {"results_none",                new[]{"尚未运行。点击上方按钮执行 GMAT 求解。", "No results yet. Run GMAT to solve."}},
            {"results_fail",                new[]{"❌ 求解失败", "❌ Solve Failed"}},
            {"results_success",             new[]{"✔ 任务序列求解成功", "✔ Mission Sequence Solved"}},
            {"results_traj_pts",            new[]{"轨迹点数", "Trajectory Points"}},
            {"results_burns_title",         new[]{"已解析机动列表", "Solved Maneuvers"}},
            {"results_no_burns",            new[]{"此序列未产生变轨机动（纯轨道积分巡航）。", "No maneuvers (pure coast propagation)."}},
            {"results_write_principia",     new[]{"🔥 一键写入所有机动至 Principia", "🔥 Write All Burns to Principia"}},
            {"results_open_gmat",           new[]{"🖥️ 在 GMAT 桌面端打开 3D 渲染", "🖥️ Open GMAT Desktop 3D View"}},

            // ===== Actions Hub =====
            {"actions_title",               new[]{"🔗 一键联动快捷操作", "🔗 Quick Actions"}},
            {"action_view_sim",             new[]{"🖼️ 查看 GMAT 仿真轨迹图像", "🖼️ View GMAT Trajectory Image"}},
            {"action_prop_config",          new[]{"⚙️ 传播子与受力模型深度配置", "⚙️ Propagator & Force Model Config"}},
            {"action_craft_details",        new[]{"🛰️ 查看航天器已同步参数快照", "🛰️ View Spacecraft Sync Snapshot"}},
            {"action_configure_forces",     new[]{"⚙️ 配置力模型", "⚙️ Configure Forces"}},

            // ===== Propagator Config =====
            {"propconfig_title",            new[]{"⚙️ GMAT 传播子与高精度受力模型深度配置", "⚙️ GMAT Propagator & Force Model Config"}},
            {"propconfig_add",              new[]{"➕ 新建传播子", "➕ Add Propagator"}},
            {"propconfig_delete",           new[]{"🗑️ 删除", "🗑️ Delete"}},
            {"propconfig_rename",           new[]{"✏️ 重命名", "✏️ Rename"}},
            {"propconfig_integrator",       new[]{"积分器类型", "Integrator Type"}},
            {"propconfig_init_step",        new[]{"初始步长 (s)", "Initial Step (s)"}},
            {"propconfig_accuracy",         new[]{"精度 (Accuracy)", "Accuracy"}},
            {"propconfig_min_step",         new[]{"最小步长 (s)", "Min Step (s)"}},
            {"propconfig_max_step",         new[]{"最大步长 (s)", "Max Step (s)"}},
            {"propconfig_central_body",     new[]{"中心天体", "Central Body"}},
            {"propconfig_gravity",          new[]{"引力源", "Gravity Sources"}},
            {"propconfig_drag",             new[]{"阻力模型", "Drag Model"}},
            {"propconfig_srp",              new[]{"太阳光压 (SRP)", "Solar Radiation Pressure"}},
            {"propconfig_srp_flux",         new[]{"太阳通量 (W/m²)", "Solar Flux (W/m²)"}},
            {"propconfig_relativity",       new[]{"广义相对论修正", "General Relativity Correction"}},

            // ===== Settings =====
            {"settings_title",              new[]{"🔧 后台服务设置与星历校准", "🔧 Backend & Ephemeris Settings"}},
            {"settings_server_url",         new[]{"服务地址", "Server URL"}},
            {"settings_test_conn",          new[]{"测试连接", "Test Connection"}},
            {"settings_conn_status",        new[]{"连接状态", "Connection Status"}},
            {"settings_language",           new[]{"🌐 界面语言", "🌐 Interface Language"}},
            {"settings_lang_zh",            new[]{"中文", "Chinese"}},
            {"settings_lang_en",            new[]{"English", "English"}},

            // ===== Craft & Orbit =====
            {"craft_orbit_title",           new[]{"🌍 轨道根数与引力环境 (Principia ICRF)", "🌍 Orbital Elements & Gravity (Principia ICRF)"}},
            {"craft_central_body",          new[]{"中心天体", "Central Body"}},
            {"craft_no_vessel",             new[]{"请在飞行场景中选择一艘飞船。", "Please select a vessel in flight scene."}},
            {"craft_sync_btn",              new[]{"🚀 同步航天器动力学至 GMAT (前往任务序列)", "🚀 Sync Spacecraft to GMAT"}},

            // ===== Tooltips =====
            {"tip_vary_variable",           new[]{"GMAT Vary 自变量。支持用户变量名 (burntime) 或航天器属性 (Sat.TA, Sat.RAAN)。微分修正器将自动搜索此变量使 Achieve 约束收敛。", "GMAT Vary independent variable. Supports user variables (burntime) or spacecraft properties (Sat.TA, Sat.RAAN). The DC solver iterates this to satisfy Achieve constraints."}},
            {"tip_vary_perturbation",       new[]{"DC 求解器在每次迭代中对自变量施加的微小扰动量。值过小会导致雅可比矩阵不精确,值过大会导致非线性效应。默认 0.0001 适用于大多数场景。", "Small perturbation applied to the variable each iteration for Jacobian computation. Too small = inaccurate Jacobian; too large = nonlinear effects. Default 0.0001 works for most cases."}},
            {"tip_vary_bounds",             new[]{"变量搜索的上下界限。求解器不会在此范围外搜索，防止非物理解。如 burntime 一般为 [0, 5000]s。", "Upper/lower bounds for variable search. Prevents solver from finding non-physical solutions. E.g. burntime typically [0, 5000]s."}},
            {"tip_vary_max_step",           new[]{"每次迭代中变量变化的最大幅度。控制求解器收敛速度与稳定性的平衡。步长过大容易发散，过小收敛慢。", "Maximum change per iteration. Balances convergence speed vs stability. Too large may diverge; too small converges slowly."}},
            {"tip_achieve_param",           new[]{"目标约束的动力学参数。如 Earth.Altitude=35786km (GEO), Earth.OrbitPeriod=86400s, Luna.RMAG=4000km 等。支持 GMAT 全参数库。", "Target constraint dynamic parameter. E.g. Earth.Altitude=35786km (GEO), Earth.OrbitPeriod=86400s. Full GMAT parameter library supported."}},
            {"tip_achieve_tolerance",       new[]{"求解器允许的目标参数偏差量。值越小精度越高但可能不收敛。高度约束通常用 0.1km，角度用 0.001°。", "Allowed deviation for the goal parameter. Smaller = higher precision but may not converge. Typical: 0.1km for altitude, 0.001° for angles."}},
            {"tip_target_dc_algo",          new[]{"微分修正器算法。NewtonRaphson (默认) 收敛快但需要好的初始猜测；Broyden 对初值不敏感但收敛慢。", "DC algorithm. NewtonRaphson (default) converges fast with good initial guess; Broyden is more robust but slower."}},
            {"tip_target_solve_mode",       new[]{"Solve = 正常求解迭代直到收敛；RunInitialGuess = 仅用初始猜测值运行一次，不迭代求解（用于调试脚本流程）。", "Solve = iterate until convergence; RunInitialGuess = run once with initial values, no iteration (for debugging script flow)."}},
            {"tip_propagator",              new[]{"GMAT 传播子（积分器+力模型组合）。LEOProp 适用于近地轨道，CislunarProp 适用于地月转移巡航，LunarNRHOProp 适用于月球 NRHO 轨道。", "GMAT propagator (integrator + force model). LEOProp for LEO, CislunarProp for cislunar cruise, LunarNRHOProp for lunar NRHO orbits."}},
            {"tip_stop_condition",          new[]{"传播终止条件。Apoapsis/Periapsis = 极值点事件触发；Time = 指定秒数；Altitude/RMAG = 数值条件触发。", "Propagation stop condition. Apoapsis/Periapsis = event trigger; Time = elapsed seconds; Altitude/RMAG = value trigger."}},
            {"tip_burn_direction",          new[]{"有限推力方向。Prograde = +V 顺行加速（升轨）；Retrograde = -V 逆行制动（降轨）；Normal = +N 法向（变倾角）。", "Finite burn direction. Prograde = +V (raise orbit); Retrograde = -V (lower orbit); Normal = +N (change inclination)."}},
            {"tip_burn_vary_bind",          new[]{"将此有限点火的时长绑定至 Vary 自变量。求解器自动调整点火时长使 Achieve 约束收敛。这是原版 GMAT Target-Vary-Achieve 架构核心。", "Bind this burn's duration to a Vary variable. The solver auto-adjusts burn duration to satisfy Achieve constraints. Core of GMAT Target-Vary-Achieve architecture."}},
            {"tip_stop_tolerance",          new[]{"GMAT 传播终止条件的数值容差。默认 1e-5，一般无需修改。仅在精度不满足或计算过慢时调整。", "Numerical tolerance for propagation stop condition. Default 1e-5, rarely needs adjustment. Only change for precision/speed tradeoff."}},
            {"tip_srp",                     new[]{"太阳光压 (Solar Radiation Pressure)。主要影响 GEO 和深空探测器长期轨道演化。近地 LEO 可忽略。", "Solar Radiation Pressure. Mainly affects GEO and deep-space long-term orbit evolution. Negligible for LEO."}},
            {"tip_relativity",              new[]{"广义相对论修正。对 GPS/GEO 精密定轨有微小影响。对转移轨道无显著作用,可关闭以加速计算。", "General relativity correction. Minor impact on GPS/GEO precision orbits. Negligible for transfer orbits; disable to speed computation."}},
            {"tip_integrator",              new[]{"数值积分器类型。RungeKutta89 = 8(9)阶 Runge-Kutta 变步长（默认，高精度推荐）；PrinceDormand78 = 7(8)阶变步长（稍快但精度略低）。", "Numerical integrator type. RungeKutta89 = 8(9) order variable-step (default, high precision); PrinceDormand78 = 7(8) order (slightly faster, less precise)."}},
            {"tip_accuracy",                new[]{"积分器精度控制。值越小精度越高但计算越慢。1e-11 是 GMAT 精密轨道计算标准值。1e-9 适用于快速粗估。", "Integrator accuracy control. Smaller = higher precision but slower. 1e-11 is GMAT standard. 1e-9 for quick estimates."}},
        };

        /// <summary>
        /// Get the localized string for the given key.
        /// Falls back to key itself if not found.
        /// </summary>
        public static string T(string key) {
            if (Dict.TryGetValue(key, out var arr)) {
                int idx = (Current == Lang.ZH) ? 0 : 1;
                if (idx < arr.Length) return arr[idx];
            }
            return key; // fallback
        }

        /// <summary>
        /// Get localized string with format arguments (for string.Format patterns).
        /// </summary>
        public static string TF(string key, params object[] args) {
            string fmt = T(key);
            try {
                return string.Format(fmt, args);
            } catch {
                return fmt;
            }
        }
    }
}
