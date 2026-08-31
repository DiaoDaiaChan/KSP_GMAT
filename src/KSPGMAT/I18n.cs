using System;
using System.Collections.Generic;

namespace KSPGMAT {
    /// <summary>
    /// Decoupled Internationalization (i18n) Engine for KSP-GMAT.
    /// Provides clean, pure UI text without bilingual clutter or obscure internal remarks.
    /// Easily extensible for new languages (ZH, EN, RU, JA, etc.).
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

        public static string Get(string key) => T(key);

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

        private static readonly Dictionary<string, string[]> Dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            // [0] = Chinese (Clean, Professional), [1] = English

            // ===== Main Window & Tabs =====
            {"window_title",               new[]{"NASA GMAT 轨道任务设计与高精度仿真系统", "NASA GMAT Orbit Simulation System"}},
            {"tab_craft",                   new[]{"🛰️ 航天器参数", "🛰️ Spacecraft"}},
            {"tab_resources",               new[]{"📊 资源与变量", "📊 Resources"}},
            {"tab_mission",                 new[]{"📋 任务控制序列", "📋 Mission Sequence"}},
            {"tab_results",                 new[]{"📈 仿真求解结果", "📈 Simulation Results"}},
            {"tab_settings",                new[]{"⚙️ 设置", "⚙️ Settings"}},
            {"no_vessel",                   new[]{"无活跃飞船", "No Active Vessel"}},
            {"target_vessel",               new[]{"🛰️ 目标航天器", "🛰️ Target Spacecraft"}},
            {"close",                       new[]{"关闭", "Close"}},

            // ===== Status & Top Actions =====
            {"status_ready",                new[]{"● 就绪", "● Ready"}},
            {"status_computing",            new[]{"⏳ GMAT 引擎求解中...", "⏳ Solving Trajectory..."}},
            {"status_done",                 new[]{"✔ 求解完成", "✔ Solved"}},
            {"status_fail",                 new[]{"❌ 求解失败", "❌ Failed"}},
            {"btn_solve",                   new[]{"⚡ 开始仿真求解", "⚡ Run Simulation"}},
            {"btn_stop",                    new[]{"⏹ 终止求解", "⏹ Stop"}},
            {"btn_reset",                   new[]{"🔄 重置", "🔄 Reset"}},
            {"btn_sync_vessel",             new[]{"📥 从当前飞船同步", "📥 Sync from Vessel"}},

            // ===== Craft Tab =====
            {"craft_state_header",          new[]{"航天器轨道状态与物理参数", "Spacecraft State & Parameters"}},
            {"craft_epoch_utc",             new[]{"参考历元 (UTC)", "Epoch (UTC)"}},
            {"craft_coordsys",              new[]{"坐标参考系", "Coordinate System"}},
            {"craft_state_type",            new[]{"状态表达格式", "State Type"}},
            {"craft_total_mass",            new[]{"飞船总质量", "Total Mass"}},
            {"craft_dry_mass",              new[]{"航天器干重", "Dry Mass"}},
            {"craft_fuel_mass",             new[]{"推进剂总质量", "Fuel Mass"}},
            {"craft_drag_area",             new[]{"迎风阻力截面积", "Drag Area"}},
            {"craft_cd",                    new[]{"阻力系数", "Drag Coeff (Cd)"}},
            {"craft_srp_area",              new[]{"光压截面积", "SRP Area"}},
            {"craft_cr",                    new[]{"光压辐射系数", "SRP Coeff (Cr)"}},
            {"craft_engines_header",        new[]{"动力系统与发动机配置", "Propulsion & Engines"}},
            {"craft_select_engine",         new[]{"选择主推进发动机:", "Select Propulsion Engine:"}},
            {"craft_tanks_header",          new[]{"推进剂储罐配置", "Propellant Tanks"}},
            {"craft_stage_budget",          new[]{"分级质量预算", "Staging Mass Budget"}},

            // ===== Resources Tab =====
            {"res_title",                   new[]{"GMAT 资源库", "GMAT Resources"}},
            {"res_var_header",              new[]{"标量变量", "Scalar Variables"}},
            {"res_arr_header",              new[]{"数值数组", "Arrays"}},
            {"res_str_header",              new[]{"字符串常量", "Strings"}},
            {"btn_add_res_var",             new[]{"➕ 新建变量", "➕ New Variable"}},
            {"btn_add_res_arr",             new[]{"➕ 新建数组", "➕ New Array"}},
            {"btn_add_res_str",             new[]{"➕ 新建字符串", "➕ New String"}},
            {"res_name",                    new[]{"标识符名称", "Identifier Name"}},
            {"res_val",                     new[]{"初始数值", "Initial Value"}},
            {"res_rows",                    new[]{"行数", "Rows"}},
            {"res_cols",                    new[]{"列数", "Cols"}},
            {"res_desc",                    new[]{"物理说明", "Description"}},

            // ===== Presets =====
            {"preset_header",               new[]{"任务队列预设模板", "Mission Templates"}},
            {"preset_geo",                  new[]{"🚀 28.6° LEO ➔ GTO ➔ 0° GEO (全流程有限推力)", "🚀 LEO to GEO (Finite Burn)"}},
            {"preset_tli",                  new[]{"🌙 阿波罗地月转移轨道 (TLI ➔ 近月制动 LOI)", "🌙 Apollo Cislunar TLI to LOI"}},
            {"preset_ce6",                  new[]{"🌕 嫦娥六号环月捕获与三次制动降轨", "🌕 Chang'e 6 Lunar Capture & Orbit Insertion"}},
            {"preset_nrho",                 new[]{"🛰️ 地月晕轨道 / NRHO 空间站轨道维持", "🛰️ Earth-Moon Halo / NRHO Orbit"}},
            {"preset_mars",                 new[]{"🔴 地火霍曼转移轨道与环火捕获", "🔴 Earth-Mars Hohmann Transfer"}},

            // ===== Sequence Tab Toolbars =====
            {"seq_queue_header",            new[]{"📋 任务步队列", "📋 Mission Step Queue"}},
            {"seq_step_unit",               new[]{"个步骤", "steps"}},
            {"seq_expand_all",              new[]{"⊞ 全部展开", "⊞ Expand All"}},
            {"seq_collapse_all",            new[]{"⊟ 全部收起", "⊟ Collapse All"}},
            {"seq_expand_targets",          new[]{"🎯 仅展开求解组", "🎯 Expand Target Groups"}},
            {"seq_clear",                   new[]{"🗑️ 清空序列", "🗑️ Clear Sequence"}},
            {"seq_command_palette",         new[]{"📚 GMAT 原生命令与步骤库:", "📚 GMAT Command Palette:"}},
            {"seq_toggle_palette_show",     new[]{"🔽 展开命令库", "🔽 Open Palette"}},
            {"seq_toggle_palette_hide",     new[]{"🔼 收起命令库", "🔼 Close Palette"}},

            // ===== Step Types (Card Pill Badges) =====
            {"type_propagate",              new[]{"🌐 轨道积分", "🌐 Propagate"}},
            {"type_maneuver",               new[]{"💥 脉冲机动", "💥 Maneuver"}},
            {"type_impulsiveburn",          new[]{"💥 脉冲机动", "💥 Impulsive Burn"}},
            {"type_beginfiniteburn",        new[]{"🔥 开启点火", "🔥 Begin Finite Burn"}},
            {"type_endfiniteburn",          new[]{"🛑 关闭点火", "🛑 End Finite Burn"}},
            {"type_finiteburn",             new[]{"🔥 有限点火", "🔥 Finite Burn"}},
            {"type_thrustvector",           new[]{"🧭 推力矢量", "🧭 Thrust Vector"}},
            {"type_target",                 new[]{"🎯 打靶求解组", "🎯 Target Group"}},
            {"type_vary",                   new[]{"✏️ Vary 寻优", "✏️ Vary"}},
            {"type_achieve",                new[]{"🎯 Achieve 目标", "🎯 Achieve"}},
            {"type_endtarget",              new[]{"↵ 闭合打靶", "↵ End Target"}},
            {"type_stagesep",               new[]{"🚀 级段分离", "🚀 Stage Separation"}},
            {"type_report",                 new[]{"📊 状态报告", "📊 Report"}},
            {"type_equation",               new[]{"📐 变量赋值", "📐 Equation"}},
            {"type_if",                     new[]{"🔀 条件分支", "🔀 If Branch"}},
            {"type_else",                   new[]{"🔀 否则分支", "🔀 Else"}},
            {"type_endif",                  new[]{"↵ 闭合条件", "↵ End If"}},
            {"type_while",                  new[]{"🔁 循环执行", "🔁 While Loop"}},
            {"type_endwhile",               new[]{"↵ 闭合循环", "↵ End While"}},
            {"type_for",                    new[]{"🔢 计数循环", "🔢 For Loop"}},
            {"type_endfor",                 new[]{"↵ 闭合循环", "↵ End For"}},

            // ===== Primary Quick Add Buttons =====
            {"add_propagate",               new[]{"🌐 + 轨道积分", "🌐 + Propagate"}},
            {"add_target",                  new[]{"🎯 + 打靶求解组", "🎯 + Target Group"}},
            {"add_finiteburn",              new[]{"🔥 + 有限推力点火", "🔥 + Finite Burn"}},
            {"add_impulsive",               new[]{"💥 + 脉冲机动", "💥 + Impulsive Burn"}},
            {"add_stagesep",                new[]{"🚀 + 级段分离", "🚀 + Stage Separation"}},
            {"add_report",                  new[]{"📊 + 轨道报告", "📊 + Report"}},
            {"add_thrustvector",            new[]{"🧭 更改推力矢量", "🧭 Thrust Vector"}},
            {"target_quick_add_label",      new[]{"向打靶组内添加:", "Add to Target Group:"}},

            // ===== Step Card Details: Finite Burn =====
            {"burn_engine_select",          new[]{"🚀 点火发动机 (支持多发集群):", "🚀 Firing Engine (Cluster Supported):"}},
            {"burn_tank_select",            new[]{"⛽ 供油储罐:", "⛽ Fuel Tank:"}},
            {"burn_cluster_active",         new[]{"[已选 {0} 台集群联合点火]", "[Cluster of {0} Engines Active]"}},
            {"burn_duration_label",         new[]{"点火时长:", "Duration:"}},
            {"burn_bind_vary_toggle",       new[]{"使用寻优变量控制点火时长", "Use Vary variable for duration"}},

            // ===== Step Card Details: Thrust Vector Control =====
            {"vector_card_title",           new[]{"🧭 更改推力矢量 (三轴方向控制与寻优):", "🧭 Thrust Vector Control:"}},
            {"vector_vary_toggle",          new[]{"在打靶组内作为寻优变量 (差分修正)", "Vary in Target Block (Differential Correction)"}},
            {"vector_active_engine",        new[]{"作用发动机:", "Engine:"}},
            {"vector_presets_label",        new[]{"常用朝向预设:", "Direction Presets:"}},
            {"vector_preset_prograde",      new[]{"顺行 (+V 切向)", "Prograde (+V)"}},
            {"vector_preset_retrograde",    new[]{"逆行 (-V 切向)", "Retrograde (-V)"}},
            {"vector_preset_normal",        new[]{"副法向 (+N 变倾角)", "Normal (+N)"}},
            {"vector_preset_antinormal",    new[]{"反副法向 (-N)", "Anti-Normal (-N)"}},
            {"vector_preset_geo",           new[]{"🌐 GTO/GEO 合成", "🌐 GTO/GEO Synthesis"}},
            {"vector_initial",              new[]{"初猜:", "Initial:"}},
            {"vector_lower",                new[]{"下界:", "Lower:"}},
            {"vector_upper",                new[]{"上界:", "Upper:"}},
            {"vector_step",                 new[]{"步长:", "MaxStep:"}},
            {"vector_hint_v",               new[]{"💡 改变远近地点高度 / 升降轨与圆化", "💡 Orbit Altitude / Raising & Circularization"}},
            {"vector_hint_n",               new[]{"💡 改变轨道面倾角 INC / 变倾角转向", "💡 Plane Inclination INC / Plane Change"}},
            {"vector_hint_b",               new[]{"💡 改变近地点幅角 AOP / 拱线旋转进动", "💡 Argument of Periapsis AOP / Apsidal Precession"}},

            // ===== Target & Solver Group =====
            {"target_dof_warning",          new[]{"自由度不足: 包含 {0} 个目标，但仅有 {1} 个寻优变量！牛顿迭代需要 变量数 ≥ 目标数。", "Under-determined system: {0} goals with only {1} vary variables! Newton-Raphson requires Variables ≥ Goals."}},
            {"target_dof_autofix",          new[]{"➕ 自动补齐推力方向寻优变量", "➕ Auto-add Thrust Vector Vary Variables"}},
            {"target_algo_label",           new[]{"求解算法:", "Algorithm:"}},
            {"target_max_iter_label",       new[]{"最大迭代次数:", "Max Iterations:"}},

            // ===== IDE Script Inspector & Editor =====
            {"ide_title",                   new[]{"GMAT 原生脚本审查与自由编辑", "GMAT Script Review & Free Editor"}},
            {"ide_edit_mode_toggle",        new[]{"切换自由编辑模式", "Free Edit Mode"}},
            {"ide_view_mode_toggle",        new[]{"只读高亮模式", "Highlighted View"}},
            {"ide_solve_edited",            new[]{"⚡ 求解编辑脚本", "⚡ Solve Edited Script"}},
            {"ide_intellisense_header",     new[]{"GMAT 智能参数与命令补全库:", "GMAT Parameter IntelliSense:"}},
            {"ide_search_label",            new[]{"🔍 搜索参数/命令:", "🔍 Search:"}},
            {"ide_clear_search",            new[]{"✖ 清空", "✖ Clear"}},
            {"ide_btn_insert",              new[]{"➕ 插入代码", "➕ Insert"}},
            {"ide_btn_copy",                new[]{"📋 复制", "📋 Copy"}},
            {"ide_snippets_header",         new[]{"常用代码片段:", "Quick Snippets:"}},

            // ===== Results Tab =====
            {"res_summary_header",          new[]{"动力学求解与机动结果", "Dynamics & Maneuver Results"}},
            {"res_no_burns",                new[]{"此序列未产生变轨机动（纯轨道积分巡航）。", "No maneuvers (pure coast propagation)."}},
            {"res_write_principia",         new[]{"🔥 一键写入所有机动至 Principia", "🔥 Write All Maneuvers to Principia"}},
            {"res_open_desktop",            new[]{"🖥️ 在 GMAT 桌面端打开", "🖥️ Open in GMAT Desktop"}},

            // ===== Settings Tab =====
            {"settings_lang_header",        new[]{"🌐 界面语言设置", "🌐 Interface Language"}},
            {"settings_gmat_path",          new[]{"GMAT 引擎路径设置", "GMAT Engine Paths"}},
            {"settings_bin_path",           new[]{"GMAT Bin 路径:", "GMAT Bin Path:"}},
            {"settings_py_path",            new[]{"Python 解释器:", "Python Executable:"}},
            {"settings_check_engine",       new[]{"🔄 检查 GMAT 引擎状态", "🔄 Test GMAT Engine Status"}}
        };

        /// <summary>
        /// Get the localized string for the given key.
        /// Falls back to key itself if not found.
        /// </summary>
        public static string T(string key, string fallback = null) {
            if (string.IsNullOrEmpty(key)) return "";

            if (Dict.TryGetValue(key, out var arr)) {
                int idx = (Current == Lang.ZH) ? 0 : 1;
                if (idx < arr.Length && !string.IsNullOrEmpty(arr[idx])) {
                    return arr[idx];
                }
            }
            return fallback ?? key;
        }

        /// <summary>
        /// Get localized string with format arguments (for string.Format patterns).
        /// </summary>
        public static string Format(string key, params object[] args) {
            string fmt = T(key);
            try {
                return string.Format(fmt, args);
            } catch {
                return fmt;
            }
        }

        /// <summary>
        /// Alias for Format
        /// </summary>
        public static string TF(string key, params object[] args) => Format(key, args);
    }
}
