using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;
using KSPGMAT.MGA;

namespace KSPGMAT.UI {
    /// <summary>
    /// KSP_GMAT 统一标准化 UI 控件库与交互引擎
    /// 包含：延迟输入框家族 (Numeric/Smart/Int/Text)、行内选择器、状态徽章、悬浮智能补全 (IntelliSense)
    /// </summary>
    public partial class GMATMainWindow {
        /// <summary>
        /// 全局延迟输入框防抖与未完成输入缓存池 (以 controlId 为 Key，保持编辑态字符串)
        /// </summary>
        private static readonly Dictionary<string, string> inputStringCache = new Dictionary<string, string>();
        private GUIStyle cachedCenterLabelStyle = null;

        static GMATMainWindow() {
            MGAStateManager.OnClearInputCacheRequested += ClearInputCache;
        }

        /// <summary>
        /// 清空所有延迟输入框缓存，确保多子窗口切换时各视图立即强制回填最新底层数值
        /// </summary>
        public static void ClearInputCache() {
            inputStringCache.Clear();
        }

        /// <summary>
        /// 清空指定控件的延迟输入框缓存
        /// </summary>
        public static void ClearInputCache(string controlId) {
            if (!string.IsNullOrEmpty(controlId) && inputStringCache.ContainsKey(controlId)) {
                inputStringCache.Remove(controlId);
            }
        }

        // =========================================================================
        // 1. 延迟输入控件家族 (Delayed Input Field Suite)
        // =========================================================================

        /// <summary>
        /// 延迟格式化浮点数值输入框：当输入框聚焦编辑时保持用户原始输入，不重置光标或提前格式化；失焦后才安全格式化同步模型。
        /// </summary>
        /// <param name="controlId">输入框唯一标识符 (用于防抖缓存与聚焦状态跟踪)</param>
        /// <param name="currentValue">当前绑定的 double 数值</param>
        /// <param name="style">GUIStyle 样式 (若为 null 则使用 GUI.skin.textField)</param>
        /// <param name="widthOpt">GUILayout 宽度布局选项</param>
        /// <param name="format">格式化字符串 (例如 "G", "F2", "F4")</param>
        /// <returns>用户输入解析后的 double 数值</returns>
        private static double DrawDelayedNumericField(string controlId, double currentValue, GUIStyle style, GUILayoutOption widthOpt, string format = "G") {
            GUI.SetNextControlName(controlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);

            string displayStr;
            if (!inputStringCache.TryGetValue(controlId, out displayStr)) {
                displayStr = currentValue.ToString(format, CultureInfo.InvariantCulture);
                inputStringCache[controlId] = displayStr;
            }

            if (!isFocused) {
                displayStr = currentValue.ToString(format, CultureInfo.InvariantCulture);
                inputStringCache[controlId] = displayStr;
            }

            string newStr = GUILayout.TextField(displayStr, style ?? GUI.skin.textField, widthOpt, GUILayout.Height(20));
            if (newStr != displayStr) {
                inputStringCache[controlId] = newStr;
                double parsed;
                if (double.TryParse(newStr, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) {
                    currentValue = parsed;
                }
            }
            return currentValue;
        }

        /// <summary>
        /// 统一智能双模态延迟输入框：支持变量名 (如 Coast_Time)/表达式/数值双模态输入，编辑时防焦点跳变，失焦保持原始内容，深度集成智能补全与锚点定位。
        /// </summary>
        /// <param name="controlId">输入框唯一标识符</param>
        /// <param name="rawValue">当前存储的原始文本内容 (若为变量名则传入变量名字符串)</param>
        /// <param name="fallbackNumericValue">兜底数值 (当 rawValue 为空时的默认显示数值)</param>
        /// <param name="style">GUIStyle 样式</param>
        /// <param name="widthOpt">GUILayout 宽度布局选项</param>
        /// <param name="intelliSenseContextMode">智能补全上下文模式 (如 "prop_val", "fburn_var", "variable_or_value")，为空则不激活补全</param>
        /// <param name="stepIndex">当前时序节点索引 (用于补全回填模型，若传入 customCallback 可为 -1)</param>
        /// <param name="customCallback">可选自定义补全项选择回调 (优先于内置 step 回填)</param>
        /// <returns>当前文本框中的最新字符串内容</returns>
        private string DrawDelayedSmartField(string controlId, string rawValue, double fallbackNumericValue, GUIStyle style, GUILayoutOption widthOpt, string intelliSenseContextMode = null, int stepIndex = -1, Action<IntelliSenseItem> customCallback = null) {
            GUI.SetNextControlName(controlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);

            string displayStr;
            if (!inputStringCache.TryGetValue(controlId, out displayStr)) {
                displayStr = !string.IsNullOrEmpty(rawValue) 
                    ? rawValue 
                    : (fallbackNumericValue != 0.0 ? fallbackNumericValue.ToString("G", CultureInfo.InvariantCulture) : "0");
                inputStringCache[controlId] = displayStr;
            }

            if (!isFocused && !string.IsNullOrEmpty(rawValue)) {
                displayStr = rawValue;
                inputStringCache[controlId] = displayStr;
            }

            string newStr = GUILayout.TextField(displayStr, style ?? GUI.skin.textField, widthOpt, GUILayout.Height(20));

            if (Event.current.type == EventType.Repaint && isFocused) {
                Rect r = GUILayoutUtility.GetLastRect();
                Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                intelliSenseAnchorRect = new Rect(sp.x, sp.y, r.width, r.height);
            }

            if (newStr != displayStr || (isFocused && !intelliSenseOpen && !intelliSenseDismissed && !string.IsNullOrEmpty(intelliSenseContextMode))) {
                inputStringCache[controlId] = newStr;
                if (!string.IsNullOrEmpty(intelliSenseContextMode) && (stepIndex >= 0 || customCallback != null)) {
                    TriggerIntelliSense(newStr, intelliSenseContextMode, stepIndex, customCallback);
                }
                return newStr;
            }

            return displayStr;
        }

        /// <summary>
        /// 延迟格式化整型输入框：用于迭代次数、步数等整数类型输入。
        /// </summary>
        /// <param name="controlId">输入框唯一标识符</param>
        /// <param name="currentValue">当前绑定的 int 数值</param>
        /// <param name="style">GUIStyle 样式</param>
        /// <param name="widthOpt">GUILayout 宽度布局选项</param>
        /// <returns>用户输入解析后的 int 数值</returns>
        private static int DrawDelayedIntField(string controlId, int currentValue, GUIStyle style, GUILayoutOption widthOpt) {
            GUI.SetNextControlName(controlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);

            string displayStr;
            if (!inputStringCache.TryGetValue(controlId, out displayStr)) {
                displayStr = currentValue.ToString();
                inputStringCache[controlId] = displayStr;
            }

            if (!isFocused) {
                displayStr = currentValue.ToString();
                inputStringCache[controlId] = displayStr;
            }

            string newStr = GUILayout.TextField(displayStr, style ?? GUI.skin.textField, widthOpt, GUILayout.Height(20));
            if (newStr != displayStr) {
                inputStringCache[controlId] = newStr;
                int parsed;
                if (int.TryParse(newStr, out parsed)) {
                    currentValue = parsed;
                }
            }
            return currentValue;
        }

        /// <summary>
        /// 延迟字符串文本输入框：编辑期间保留中间未完成输入，失焦后安全提交，防止高频每帧 String 分配。
        /// 全面统一接入可选智能补全与自定义回填机制。
        /// </summary>
        /// <param name="controlId">输入框唯一标识符</param>
        /// <param name="currentText">当前绑定的字符串</param>
        /// <param name="style">GUIStyle 样式</param>
        /// <param name="widthOpt">GUILayout 宽度布局选项</param>
        /// <param name="intelliSenseContextMode">可选补全上下文模式 (如 "script_code", "vary", "achieve")</param>
        /// <param name="customCallback">可选自定义补全项选择回调</param>
        /// <returns>最新编辑的字符串</returns>
        private string DrawDelayedTextField(string controlId, string currentText, GUIStyle style, GUILayoutOption widthOpt, string intelliSenseContextMode = null, Action<IntelliSenseItem> customCallback = null) {
            return DrawDelayedSmartField(controlId, currentText, 0.0, style, widthOpt, intelliSenseContextMode, -1, customCallback);
        }

        // =========================================================================
        // 2. 选择器与胶囊组 (Pickers, Dropdowns & Chips)
        // =========================================================================

        /// <summary>
        /// 行内微分修正求解器选择器：以胶囊按钮组的形式快速切换节点绑定的求解器 (如 DC_1, DC_2)。
        /// </summary>
        /// <param name="step">当前任务节点对象</param>
        /// <param name="stepIndex">节点索引</param>
        private void DrawInlineSolverPicker(MissionStep step, int stepIndex) {
            GUILayout.Label(I18n.T("widget_bind_solver"), gridLabelStyle, GUILayout.Width(75));
            List<string> availSolvers = GetAvailableSolverList();
            if (availSolvers == null || availSolvers.Count == 0) availSolvers = new List<string> { "DefaultDC" };

            string currentDc = !string.IsNullOrEmpty(step.dc_name) ? step.dc_name : GetParentTargetDcName(stepIndex);

            for (int s = 0; s < availSolvers.Count; s++) {
                string sName = availSolvers[s];
                bool isSel = currentDc.Equals(sName, StringComparison.OrdinalIgnoreCase);
                string label = isSel ? $"<b>[✓ {sName}]</b>" : sName;
                if (GUILayout.Button(label, isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    step.dc_name = sName;
                    step.optimizer_name = sName;
                }
            }
        }

        private List<string> GetAvailableOptimizerList() {
            List<string> opts = new List<string>();
            var all = GMATParameterRegistry.GetAllSolverConfigs();
            if (all != null) {
                foreach (var kvp in all) {
                    if (kvp.Value.type == "Yukon" || kvp.Value.type == "Optimizer" || kvp.Value.algorithm == "SQP" || kvp.Key.StartsWith("Yukon", StringComparison.OrdinalIgnoreCase)) {
                        if (!opts.Contains(kvp.Key)) opts.Add(kvp.Key);
                    }
                }
            }
            if (missionSequence != null) {
                for (int j = 0; j < missionSequence.Count; j++) {
                    if (missionSequence[j].type == "Optimize" && !string.IsNullOrEmpty(missionSequence[j].optimizer_name)) {
                        if (!opts.Contains(missionSequence[j].optimizer_name)) opts.Add(missionSequence[j].optimizer_name);
                    }
                }
            }
            if (opts.Count == 0) opts.Add("Yukon1");
            return opts;
        }

        private void DrawInlineOptimizerPicker(MissionStep step, int stepIndex) {
            GUILayout.Label(I18n.T("seq_opt_name_label"), gridLabelStyle, GUILayout.Width(75));
            List<string> availOpts = GetAvailableOptimizerList();
            string curOpt = !string.IsNullOrEmpty(step.optimizer_name) ? step.optimizer_name : "Yukon1";

            GUI.SetNextControlName($"Field_OptName_{stepIndex}");
            string newOpt = GUILayout.TextField(curOpt, stepLabelInputStyle, GUILayout.Width(100), GUILayout.Height(20));
            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() == $"Field_OptName_{stepIndex}") {
                Rect r = GUILayoutUtility.GetLastRect();
                Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                intelliSenseAnchorRect = new Rect(sp.x, sp.y, r.width, r.height);
            }
            if (newOpt != curOpt || (GUI.GetNameOfFocusedControl() == $"Field_OptName_{stepIndex}" && !intelliSenseOpen && !intelliSenseDismissed)) {
                step.optimizer_name = newOpt;
                TriggerIntelliSense(newOpt, "optimizer", stepIndex);
            }

            for (int s = 0; s < availOpts.Count; s++) {
                string oName = availOpts[s];
                bool isSel = curOpt.Equals(oName, StringComparison.OrdinalIgnoreCase);
                string label = isSel ? $"<b>[✓ {oName}]</b>" : oName;
                if (GUILayout.Button(label, isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    step.optimizer_name = oName;
                }
            }
        }

        /// <summary>
        /// 行内传播子选择器：以胶囊按钮组的形式快速切换积分传播子 (如 LEOProp, CislunarProp)。
        /// </summary>
        /// <param name="step">当前任务节点对象</param>
        /// <param name="stepIndex">节点索引</param>
        private void DrawInlinePropagatorPicker(MissionStep step, int stepIndex) {
            GUILayout.Label(I18n.T("widget_dyn_propagator"), gridLabelStyle, GUILayout.Width(80));
            // 基线 7: 传播子清单以 GMAT 注册表 (JSON 动态加载) 为唯一真值源，杜绝 C# 层写死清单；
            // 仅当注册表为空时回退引导默认项。
            List<string> props = new List<string>();
            var regProps = GMATParameterRegistry.GetAllPropagatorConfigs();
            if (regProps != null && regProps.Count > 0) {
                foreach (var kvp in regProps) {
                    if (!props.Contains(kvp.Key)) props.Add(kvp.Key);
                }
            } else {
                props.Add("DefaultProp");
            }

            string curProp = !string.IsNullOrEmpty(step.propagator) ? step.propagator : "LEOProp";
            for (int p = 0; p < props.Count; p++) {
                string pName = props[p];
                bool isSel = curProp.Equals(pName, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(pName, isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    step.propagator = pName;
                }
            }
        }

        // =========================================================================
        // 2.1 高级天文与历元日期选择器 (Astronomical DateTime Picker)
        // =========================================================================
        private string _activeDatePickerId = null;
        private int _dpYear = 1951;
        private int _dpMonth = 5;
        private int _dpDay = 12;
        private int _dpHour = 8;
        private int _dpMinute = 0;
        private double _dpSecond = 0.0;

        private void InitDatePickerState(double ut) {
            if (ut <= 0) ut = GMATClient.ParseUtcGregorian("01 Jan 2000 12:00:00");
            GMATClient.UtToDateComponents(ut, out _dpYear, out _dpMonth, out _dpDay, out _dpHour, out _dpMinute, out _dpSecond);
        }

        private void DrawDateTimeInputField(string labelKey, string defaultLabel, string dateStr, double utVal, string pickerId, Action<string, double> onUpdated) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T(labelKey, defaultLabel), labelValueStyle, GUILayout.Width(90));
            string newStr = GUILayout.TextField(dateStr, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
            if (newStr != dateStr) {
                double parsedUT = GMATClient.ParseUtcGregorian(newStr);
                onUpdated?.Invoke(newStr, parsedUT > 0 ? parsedUT : utVal);
            }

            bool isOpen = (_activeDatePickerId == pickerId);
            string icon = isOpen ? "▲" : "📅";
            if (GUILayout.Button(icon, isOpen ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(26), GUILayout.Height(20))) {
                if (isOpen) {
                    _activeDatePickerId = null;
                } else {
                    _activeDatePickerId = pickerId;
                    double currentUt = utVal > 0 ? utVal : GMATClient.ParseUtcGregorian(dateStr);
                    InitDatePickerState(currentUt);
                }
            }
            GUILayout.EndHorizontal();

            // 若当前选择器展开，紧随其后绘制日期时间选择器交互抽屉
            if (_activeDatePickerId == pickerId) {
                DrawDatePickerDrawer(dateStr, utVal, pickerId, onUpdated);
            }
        }

        private void DrawDatePickerDrawer(string dateStr, double utVal, string pickerId, Action<string, double> onUpdated) {
            GUILayout.Space(2);
            GUILayout.BeginVertical(nestedCardBoxStyle);

            // 标题栏
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>📅 {I18n.T("mga_date_picker_title", "历元日期选择器")}</b></color> <color=#94a3b8>({_dpYear:D4}-{_dpMonth:D2}-{_dpDay:D2} {_dpHour:D2}:{_dpMinute:D2})</color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(20), GUILayout.Height(18))) {
                _activeDatePickerId = null;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 快捷跳转工具条
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("mga_date_picker_current_game", "🚀 游戏时间"), secondaryButtonStyle, GUILayout.Height(18))) {
                double gameUt = 0;
                try { gameUt = Planetarium.GetUniversalTime(); } catch {}
                if (gameUt > 0) {
                    InitDatePickerState(gameUt);
                    onUpdated?.Invoke(GMATClient.FormatUtcGregorian(gameUt), gameUt);
                }
            }
            if (GUILayout.Button("-30d", secondaryButtonStyle, GUILayout.Width(42), GUILayout.Height(18))) { ShiftDatePickerDays(-30, dateStr, utVal, onUpdated); }
            if (GUILayout.Button("-7d", secondaryButtonStyle, GUILayout.Width(36), GUILayout.Height(18))) { ShiftDatePickerDays(-7, dateStr, utVal, onUpdated); }
            if (GUILayout.Button("-1d", secondaryButtonStyle, GUILayout.Width(36), GUILayout.Height(18))) { ShiftDatePickerDays(-1, dateStr, utVal, onUpdated); }
            if (GUILayout.Button("+1d", secondaryButtonStyle, GUILayout.Width(36), GUILayout.Height(18))) { ShiftDatePickerDays(1, dateStr, utVal, onUpdated); }
            if (GUILayout.Button("+7d", secondaryButtonStyle, GUILayout.Width(36), GUILayout.Height(18))) { ShiftDatePickerDays(7, dateStr, utVal, onUpdated); }
            if (GUILayout.Button("+30d", secondaryButtonStyle, GUILayout.Width(42), GUILayout.Height(18))) { ShiftDatePickerDays(30, dateStr, utVal, onUpdated); }
            if (GUILayout.Button("+90d", secondaryButtonStyle, GUILayout.Width(42), GUILayout.Height(18))) { ShiftDatePickerDays(90, dateStr, utVal, onUpdated); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 年份步进
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#94a3b8>年份:</color>", labelValueStyle, GUILayout.Width(42));
            if (GUILayout.Button("<< -10", secondaryButtonStyle, GUILayout.Width(48), GUILayout.Height(18))) { _dpYear -= 10; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("< -1", secondaryButtonStyle, GUILayout.Width(38), GUILayout.Height(18))) { _dpYear -= 1; UpdateFromDatePickerComponents(onUpdated); }
            GUILayout.Label($"<color=#facc15><b>{_dpYear:D4}</b></color>", labelValueStyle, GUILayout.Width(45));
            if (GUILayout.Button("+1 >", secondaryButtonStyle, GUILayout.Width(38), GUILayout.Height(18))) { _dpYear += 1; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("+10 >>", secondaryButtonStyle, GUILayout.Width(48), GUILayout.Height(18))) { _dpYear += 10; UpdateFromDatePickerComponents(onUpdated); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 月份选择胶囊 (12 个紧凑月份按钮)
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#94a3b8>月份:</color>", labelValueStyle, GUILayout.Width(42));
            string[] monthNames = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            for (int m = 1; m <= 12; m++) {
                bool isCurMonth = (_dpMonth == m);
                if (GUILayout.Button(monthNames[m - 1], isCurMonth ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(27), GUILayout.Height(18))) {
                    _dpMonth = m;
                    UpdateFromDatePickerComponents(onUpdated);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 日期步进与快捷跳转
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#94a3b8>日期:</color>", labelValueStyle, GUILayout.Width(42));
            if (GUILayout.Button("< -1", secondaryButtonStyle, GUILayout.Width(38), GUILayout.Height(18))) {
                _dpDay = Math.Max(1, _dpDay - 1);
                UpdateFromDatePickerComponents(onUpdated);
            }
            GUILayout.Label($"<color=#38bdf8><b>{_dpDay:D2}日</b></color>", labelValueStyle, GUILayout.Width(40));
            if (GUILayout.Button("+1 >", secondaryButtonStyle, GUILayout.Width(38), GUILayout.Height(18))) {
                _dpDay = Math.Min(31, _dpDay + 1);
                UpdateFromDatePickerComponents(onUpdated);
            }
            GUILayout.Space(4);
            int[] dayShortcuts = new int[] { 1, 5, 10, 15, 20, 25, 28 };
            for (int d = 0; d < dayShortcuts.Length; d++) {
                int targetD = dayShortcuts[d];
                if (GUILayout.Button($"{targetD}", (_dpDay == targetD) ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(24), GUILayout.Height(18))) {
                    _dpDay = targetD;
                    UpdateFromDatePickerComponents(onUpdated);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 时间步进 (时:分)
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#94a3b8>时间:</color>", labelValueStyle, GUILayout.Width(42));
            GUILayout.Label($"<color=#34d399><b>{_dpHour:D2}:{_dpMinute:D2}</b></color>", labelValueStyle, GUILayout.Width(46));
            if (GUILayout.Button("-6h", secondaryButtonStyle, GUILayout.Width(32), GUILayout.Height(18))) { _dpHour = (_dpHour - 6 + 24) % 24; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("-1h", secondaryButtonStyle, GUILayout.Width(32), GUILayout.Height(18))) { _dpHour = (_dpHour - 1 + 24) % 24; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("+1h", secondaryButtonStyle, GUILayout.Width(32), GUILayout.Height(18))) { _dpHour = (_dpHour + 1) % 24; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("+6h", secondaryButtonStyle, GUILayout.Width(32), GUILayout.Height(18))) { _dpHour = (_dpHour + 6) % 24; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("00:00", secondaryButtonStyle, GUILayout.Width(44), GUILayout.Height(18))) { _dpHour = 0; _dpMinute = 0; UpdateFromDatePickerComponents(onUpdated); }
            if (GUILayout.Button("12:00", secondaryButtonStyle, GUILayout.Width(44), GUILayout.Height(18))) { _dpHour = 12; _dpMinute = 0; UpdateFromDatePickerComponents(onUpdated); }
            
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("mga_date_picker_apply", "✓ 完成"), actionButtonStyle, GUILayout.Width(58), GUILayout.Height(18))) {
                _activeDatePickerId = null;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private void ShiftDatePickerDays(double days, string dateStr, double utVal, Action<string, double> onUpdated) {
            double currentUt = utVal > 0 ? utVal : GMATClient.ParseUtcGregorian(dateStr);
            currentUt += days * 86400.0;
            InitDatePickerState(currentUt);
            onUpdated?.Invoke(GMATClient.FormatUtcGregorian(currentUt), currentUt);
        }

        private void UpdateFromDatePickerComponents(Action<string, double> onUpdated) {
            double ut = GMATClient.DateComponentsToUt(_dpYear, _dpMonth, _dpDay, _dpHour, _dpMinute, _dpSecond);
            onUpdated?.Invoke(GMATClient.FormatUtcGregorian(ut), ut);
        }

        /// <summary>
        /// 行内行星系/引力域选择器：动态自动识别有卫星的母星系统，零硬编码自适应 RSS/Stock/OPM/JNSQ。
        /// 保持极致紧凑，在尽量少改动 GUI 的情况下支持行星际与行星系内转移切换与双向智能联动。
        /// </summary>
        private bool _isSystemPickerOpen = false;
        private bool _autoLinkSystem = true;

        /// <summary>
        /// 行内行星系/引力域选择器：动态自动识别有卫星的母星系统，零硬编码自适应 RSS/Stock/OPM/JNSQ。
        /// 采用单行紧凑控制条 + 下拉折叠网格，彻底消除横向撑爆与滚动截断。
        /// </summary>
        private void DrawCentralBodySystemSelector(MGAConfig cfg) {
            if (cfg == null) return;

            string rootStar = SolarSystemCatalog.RootStarName;

            // 1. 逆向智能推导：根据当前出发和目标天体，动态识别当前转移所处的引力中心
            if (_autoLinkSystem) {
                string inferredCb = SolarSystemCatalog.ResolveTransferCentralBody(cfg.DepartureBody, cfg.DestinationBody);
                if (!string.IsNullOrEmpty(inferredCb) && !inferredCb.Equals(cfg.CentralBody, StringComparison.OrdinalIgnoreCase)) {
                    cfg.CentralBody = inferredCb;
                    MGAStateManager.Current.SetCentralBody(inferredCb, resetCamera: false);
                }
            }

            bool isInterplanetary = string.IsNullOrEmpty(cfg.CentralBody) || 
                                    cfg.CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                                    cfg.CentralBody.Equals(rootStar, StringComparison.OrdinalIgnoreCase);

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_system_domain", "<b>🪐 系统:</b>"), labelValueStyle, GUILayout.Width(46));

            // 2. 恒星/日心行星际系统 (主恒星，默认为 Sun / Kerbol)
            string sunLabel = isInterplanetary ? "<b>[✓ ☀️ 行星际]</b>" : "☀️ 行星际";
            if (GUILayout.Button(sunLabel, isInterplanetary ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20), GUILayout.Width(78))) {
                cfg.CentralBody = rootStar;
                _isSystemPickerOpen = false;
                // 若当前出发天体是卫星，切回主星
                var curDep = SolarSystemCatalog.GetPlanet(cfg.DepartureBody);
                if (curDep != null && curDep.IsMoon && !string.IsNullOrEmpty(curDep.ParentName)) {
                    cfg.DepartureBody = curDep.ParentName;
                }
                // 目标若也是卫星或与出发相同，动态选取首选主行星 (0 静态硬编码)
                var curDest = SolarSystemCatalog.GetPlanet(cfg.DestinationBody);
                if (curDest != null && (curDest.IsMoon || curDest.Name.Equals(cfg.DepartureBody, StringComparison.OrdinalIgnoreCase))) {
                    var avail = SolarSystemCatalog.GetAvailablePlanets();
                    var alt = avail.Find(p => !p.IsMoon && 
                                              !p.Name.Equals(cfg.DepartureBody, StringComparison.OrdinalIgnoreCase) && 
                                              !p.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase));
                    if (alt != null) cfg.DestinationBody = alt.Name;
                }
                cfg.DepartureSearchDays = 365;
                MGAStateManager.Current.SetCentralBody(rootStar, resetCamera: true);
                if (mgaWorkflowMode == 1) RefreshCandidateChains();
                RefreshMgaCanvas();
                MGAStateManager.Current.NotifyStateChanged();
            }

            // 3. 当前选中的母星系统下拉触发器 (例如：🌍 地月系 ▾ / ♃ 木卫系 ▾)
            string currentSystemDisplayName;
            if (!isInterplanetary) {
                var curParent = SolarSystemCatalog.GetPlanet(cfg.CentralBody);
                string pIcon = curParent != null ? curParent.Icon : "🪐";
                string pName = curParent != null ? curParent.GetDisplayName() : cfg.CentralBody;
                currentSystemDisplayName = $"<b>[✓ {pIcon}{pName}系 ▾]</b>";
            } else {
                currentSystemDisplayName = "行星系 ▾";
            }

            if (GUILayout.Button(currentSystemDisplayName, (!isInterplanetary || _isSystemPickerOpen) ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20), GUILayout.MinWidth(82))) {
                _isSystemPickerOpen = !_isSystemPickerOpen;
            }

            // 4. 智能自动联动开关 (根据起讫天体自动锁定引力域)
            string linkLabel = _autoLinkSystem ? "<color=#34d399>⚡ 联动开</color>" : "<color=#94a3b8>⚡ 联动关</color>";
            if (GUILayout.Button(linkLabel, secondaryButtonStyle, GUILayout.Height(20), GUILayout.Width(62))) {
                _autoLinkSystem = !_autoLinkSystem;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 5. 下拉折叠网格卡片 (3 列紧凑网格，横向不溢出)
            if (_isSystemPickerOpen) {
                GUILayout.Space(2);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#7dd3fc><b>选择母星引力域与卫星系统:</b></color>", labelValueStyle);
                
                var parentBodies = SolarSystemCatalog.GetBodiesWithMoons();
                int columns = 3;
                for (int i = 0; i < parentBodies.Count; i += columns) {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < columns; c++) {
                        int idx = i + c;
                        if (idx < parentBodies.Count) {
                            var p = parentBodies[idx];
                            bool isCur = !isInterplanetary && cfg.CentralBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase);
                            string itemText = isCur ? $"<b>✓ {p.Icon}{p.GetDisplayName()}</b>" : $"{p.Icon}{p.GetDisplayName()}";
                            if (GUILayout.Button(itemText, isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true))) {
                                cfg.CentralBody = p.Name;
                                _isSystemPickerOpen = false;
                                var moons = SolarSystemCatalog.GetMoonsOf(p.Name);
                                if (moons.Count >= 2) {
                                    cfg.DepartureBody = moons[0].Name;
                                    cfg.DestinationBody = moons[1].Name;
                                    if (cfg.FlybySequence == null) cfg.FlybySequence = new List<string>();
                                    cfg.FlybySequence.Clear();
                                    if (moons.Count > 2) {
                                        cfg.FlybySequence.Add(moons[2].Name);
                                    }
                                } else if (moons.Count == 1) {
                                    cfg.DepartureBody = p.Name;
                                    cfg.DestinationBody = moons[0].Name;
                                    if (cfg.FlybySequence != null) cfg.FlybySequence.Clear();
                                } else {
                                    cfg.DepartureBody = p.Name;
                                }
                                cfg.DepartureSearchDays = 30;
                                MGAStateManager.Current.SetCentralBody(p.Name, resetCamera: true);
                                if (mgaWorkflowMode == 1) RefreshCandidateChains();
                                RefreshMgaCanvas();
                                MGAStateManager.Current.NotifyStateChanged();
                            }
                        } else {
                            GUILayout.Space(60); // 对齐占位
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
        }

        // =========================================================================
        // 3. 视觉反馈与状态徽章 (Badges & Visual Indicators)
        // =========================================================================

        /// <summary>
        /// 绘制高质感平滑任务/状态进度条
        /// </summary>
        /// <param name="progress">进度值 [0.0 ~ 1.0]</param>
        /// <param name="labelText">进度条中央文本</param>
        /// <param name="height">高度 (像素)</param>
        private void DrawProgressBar(float progress, string labelText, float height = 22f) {
            Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUI.Box(barRect, GUIContent.none, nestedCardBoxStyle);

            float pClamped = Mathf.Clamp01(progress);
            if (pClamped > 0f) {
                Rect fillRect = new Rect(barRect.x + 2, barRect.y + 2, (barRect.width - 4) * pClamped, barRect.height - 4);
                Color oldColor = GUI.color;
                GUI.color = isPropagating ? new Color(0.2f, 0.8f, 0.9f) : (latestResult != null && latestResult.success ? new Color(0.2f, 0.85f, 0.45f) : new Color(0.9f, 0.35f, 0.35f));
                GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
                GUI.color = oldColor;
            }

            if (cachedCenterLabelStyle == null) {
                cachedCenterLabelStyle = new GUIStyle(labelValueStyle) {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
            }
            string displayPercent = $"{(pClamped * 100f):F0}%";
            string fullBarLabel = string.IsNullOrEmpty(labelText) ? displayPercent : $"{labelText}  ({displayPercent})";
            GUI.Label(barRect, $"<b>{fullBarLabel}</b>", cachedCenterLabelStyle);
        }

        /// <summary>
        /// 绘制 GMAT 官方参数类型中英文提示徽章
        /// </summary>
        /// <param name="paramName">参数全名 (例如 Sat.Earth.Altitude, Sat.Luna.RadPer)</param>
        private void DrawParamBadge(string paramName) {
            if (string.IsNullOrEmpty(paramName)) return;
            string propBadge = GMATParameterRegistry.GetParamBadgeI18n(paramName);
            if (!string.IsNullOrEmpty(propBadge)) {
                GUILayout.Label($"<color=#38bdf8><b>[{propBadge}]</b></color>", labelValueStyle);
            }
        }

        /// <summary>
        /// 绘制全局变量绑定感知徽章 (如果传入的名字与 GMAT 变量库匹配，自动显示其当前值)
        /// </summary>
        /// <param name="varName">待检测的变量名</param>
        private void DrawVariableBadge(string varName) {
            if (string.IsNullOrEmpty(varName)) return;
            if (GMATStateManager.Current != null && GMATStateManager.Current.Variables != null) {
                var matchedVar = GMATStateManager.Current.Variables.Find(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
                if (matchedVar != null) {
                    GUILayout.Label(I18n.ColorFormat("widget_var_badge", matchedVar.Name, matchedVar.InitialValue), labelValueStyle);
                }
            }
        }

        // =========================================================================
        // 4. 悬浮智能补全引擎 (Floating IntelliSense Engine)
        // =========================================================================

        /// <summary>
        /// 从代码行或文本中提取光标处正在键入的最后一个有效标识符或点语法命名空间 (例如 "Sat.Earth.S" 或 "GEO_")
        /// </summary>
        /// <param name="text">当前完整文本</param>
        /// <param name="tokenStart">返回当前 Token 在文本中的起始偏移</param>
        /// <returns>提取出的 Token 字符串</returns>
        private static string ExtractIntelliSenseToken(string text, out int tokenStart) {
            tokenStart = -1;
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.Length;
            while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
            if (end == 0) return "";
            int start = end - 1;
            while (start >= 0) {
                char c = text[start];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_') {
                    start--;
                } else {
                    break;
                }
            }
            tokenStart = start + 1;
            return text.Substring(tokenStart, end - tokenStart);
        }

        /// <summary>
        /// 唤起 GMAT IntelliSense 智能补全查询 (支持自定义回调回填，全面打通单步代码透视与通用文本组件)
        /// </summary>
        /// <param name="query">用户当前输入的文本前缀</param>
        /// <param name="fieldName">目标字段类型 ("achieve", "vary", "prop_stop", "prop_val", "fburn_var", "script_code")</param>
        /// <param name="stepIndex">目标时序步骤索引 (通用回填可传 -1)</param>
        /// <param name="customCallback">自定义应用回填委托 (若不为 null，选中项时将优先执行该委托)</param>
        private void TriggerIntelliSense(string query, string fieldName, int stepIndex, Action<IntelliSenseItem> customCallback = null) {
            if (intelliSenseDismissed && intelliSenseLastQuery == query && intelliSenseCustomCallback == customCallback) return;
            intelliSenseTargetStepIndex = stepIndex;
            intelliSenseFieldName = fieldName;
            intelliSenseLastQuery = query;
            intelliSenseCustomCallback = customCallback;
            intelliSenseItems = GMATParameterRegistry.QueryIntelliSense(query, FlightGlobals.ActiveVessel, fieldName);
            intelliSenseSelectedIndex = 0;
            intelliSenseOpen = (intelliSenseItems != null && intelliSenseItems.Count > 0);
        }

        /// <summary>
        /// 将选中的 IntelliSense 补全项应用并回填到目标时序节点或自定义代码区域中
        /// </summary>
        /// <param name="item">被选中的补全数据项</param>
        private void ApplyIntelliSense(IntelliSenseItem item) {
            if (item == null) return;

            // 优先处理通用回调 (单步代码透视框、通用延迟文本框等)
            if (intelliSenseCustomCallback != null) {
                var cb = intelliSenseCustomCallback;
                intelliSenseCustomCallback = null;
                cb(item);
                intelliSenseOpen = false;
                intelliSenseDismissed = true;
                intelliSenseLastQuery = item.InsertText;
                return;
            }

            if (intelliSenseTargetStepIndex >= 0 && intelliSenseTargetStepIndex < missionSequence.Count) {
                var step = missionSequence[intelliSenseTargetStepIndex];
                if (intelliSenseFieldName == "achieve") {
                    step.achieve_param = item.InsertText;
                    if (item.RecommendedValue != 0 || item.RecommendedTolerance != 0) {
                        step.achieve_val = item.RecommendedValue;
                        step.achieve_val_str = item.RecommendedValue.ToString("F3");
                        step.achieve_tolerance = item.RecommendedTolerance;
                    }
                    pendingFocusControl = $"Field_Achieve_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName.StartsWith("prop_stop")) {
                    int cIdx = 0;
                    if (intelliSenseFieldName.StartsWith("prop_stop_")) {
                        int.TryParse(intelliSenseFieldName.Substring("prop_stop_".Length), out cIdx);
                    }
                    if (step.stop_conditions != null && cIdx < step.stop_conditions.Count) {
                        step.stop_conditions[cIdx].parameter = item.InsertText;
                        if (item.RecommendedValue != 0) {
                            step.stop_conditions[cIdx].target_val = item.RecommendedValue;
                            step.stop_conditions[cIdx].target_val_str = item.RecommendedValue.ToString("F3");
                        }
                    }
                    step.stop_type = item.InsertText;
                    if (item.RecommendedValue != 0) {
                        step.target_val = item.RecommendedValue;
                        step.target_val_str = item.RecommendedValue.ToString("F3");
                    }
                    pendingFocusControl = $"Field_PropStop_{intelliSenseTargetStepIndex}_{cIdx}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "vary") {
                    step.vary_variable = item.InsertText;
                    if (item.RecommendedValue != 0) {
                        step.vary_initial = item.RecommendedValue;
                        step.vary_initial_str = item.RecommendedValue.ToString("F2");
                        step.vary_lower = 0.0;
                        step.vary_upper = item.RecommendedValue * 5.0;
                    }
                    pendingFocusControl = $"Field_Vary_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "prop_val") {
                    step.target_val_str = item.InsertText;
                    step.duration_str = item.InsertText;
                    if (item.Kind == IntelliSenseKind.Variable) {
                        step.burn_use_vary_var = true;
                        step.burn_vary_var_name = item.InsertText;
                        if (item.RecommendedValue != 0) {
                            step.target_val = item.RecommendedValue;
                            step.duration = item.RecommendedValue;
                        }
                    } else if (item.RecommendedValue != 0) {
                        step.target_val = item.RecommendedValue;
                        step.duration = item.RecommendedValue;
                        step.burn_use_vary_var = false;
                    }
                    pendingFocusControl = $"Field_PropVal_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "fburn_var") {
                    step.burn_vary_var_name = item.InsertText;
                    step.duration_str = item.InsertText;
                    step.burn_use_vary_var = true;
                    pendingFocusControl = $"Field_FBurnVar_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "achieve_val") {
                    step.achieve_val_str = item.InsertText;
                    if (item.RecommendedValue != 0) {
                        step.achieve_val = item.RecommendedValue;
                    }
                    pendingFocusControl = $"Field_AchVal_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "vary_init") {
                    step.vary_initial_str = item.InsertText;
                    if (item.RecommendedValue != 0) {
                        step.vary_initial = item.RecommendedValue;
                    }
                    pendingFocusControl = $"Field_VaryInit_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "minimize") {
                    step.minimize_variable = item.InsertText;
                    pendingFocusControl = $"Field_MinVar_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "nlc") {
                    step.constraint_lhs = item.InsertText;
                    if (item.RecommendedValue != 0) {
                        step.constraint_val = item.RecommendedValue;
                    }
                    pendingFocusControl = $"Field_NlcLhs_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "optimizer") {
                    step.optimizer_name = item.InsertText;
                    pendingFocusControl = $"Field_OptName_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "python_module") {
                    step.python_module = item.InsertText;
                    pendingFocusControl = $"Field_PyMod_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "python_func") {
                    step.python_function = item.InsertText;
                    pendingFocusControl = $"Field_PyFunc_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "python_inputs") {
                    if (string.IsNullOrEmpty(step.python_inputs)) {
                        step.python_inputs = item.InsertText;
                    } else if (!step.python_inputs.Contains(item.InsertText)) {
                        step.python_inputs = $"{step.python_inputs.TrimEnd(',', ' ')}, {item.InsertText}";
                    }
                    pendingFocusControl = $"Field_PyInputs_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                } else if (intelliSenseFieldName == "python_outputs") {
                    if (string.IsNullOrEmpty(step.python_outputs)) {
                        step.python_outputs = item.InsertText;
                    } else if (!step.python_outputs.Contains(item.InsertText)) {
                        step.python_outputs = $"{step.python_outputs.TrimEnd(',', ' ')}, {item.InsertText}";
                    }
                    pendingFocusControl = $"Field_PyOutputs_{intelliSenseTargetStepIndex}";
                    GUI.FocusControl(pendingFocusControl);
                }
            }
            intelliSenseOpen = false;
            intelliSenseDismissed = true;
            intelliSenseLastQuery = item.InsertText;
        }

        /// <summary>
        /// 绘制全局顶层浮动 IntelliSense 智能补全窗口 (支持键盘上下导航、翻页、分类图标及文档提示)
        /// </summary>
        private void DrawFloatingIntelliSenseWindow() {
            if (!intelliSenseOpen || intelliSenseItems == null || intelliSenseItems.Count == 0) return;
            if (intelliSenseAnchorRect.width <= 0) return;

            float popW = 420f;
            float itemH = 26f;
            float listH = Mathf.Min(182f, intelliSenseItems.Count * itemH + 6f);
            float totalH = listH + 60f; // header + list + footer doc

            float px = intelliSenseAnchorRect.x;
            float py = intelliSenseAnchorRect.y + intelliSenseAnchorRect.height + 4f;

            if (py + totalH > Screen.height - 30f) {
                py = intelliSenseAnchorRect.y - totalH - 4f;
            }

            Rect popRect = new Rect(px, py, popW, totalH);

            GUI.depth = -200;
            GUI.color = Color.white;

            GUI.Box(popRect, GUIContent.none, cardBoxStyle ?? GUI.skin.box);
            GUILayout.BeginArea(popRect, GUI.skin.box);

            // Header with Pagination Status & Quick Page Buttons
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("intellisense_title"), labelValueStyle, GUILayout.Width(130));
            GUILayout.Label($"<color=#38bdf8>[ {intelliSenseSelectedIndex + 1} / {intelliSenseItems.Count} ]</color> <color=#64748b>↑↓/PgUp/PgDn/Tab</color>", labelValueStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("▲", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                intelliSenseSelectedIndex = Mathf.Max(0, intelliSenseSelectedIndex - 5);
                intelliSenseScroll.y = Mathf.Max(0, (intelliSenseSelectedIndex - 2) * itemH);
            }
            if (GUILayout.Button("▼", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                intelliSenseSelectedIndex = Mathf.Min(intelliSenseItems.Count - 1, intelliSenseSelectedIndex + 5);
                intelliSenseScroll.y = Mathf.Max(0, (intelliSenseSelectedIndex - 2) * itemH);
            }
            if (GUILayout.Button("✕", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(18))) {
                intelliSenseOpen = false;
                intelliSenseDismissed = true;
            }
            GUILayout.EndHorizontal();

            // Scrollable Items List
            intelliSenseScroll = GUILayout.BeginScrollView(intelliSenseScroll, GUILayout.Height(listH));
            for (int i = 0; i < intelliSenseItems.Count; i++) {
                var item = intelliSenseItems[i];
                bool isSelected = (i == intelliSenseSelectedIndex);

                if (isSelected) {
                    GUI.backgroundColor = new Color(0.02f, 0.52f, 0.78f, 1f);
                } else {
                    GUI.backgroundColor = new Color(0.12f, 0.16f, 0.24f, 0.85f);
                }

                GUILayout.BeginHorizontal(GUI.skin.button, GUILayout.Height(itemH));
                GUILayout.Label(item.GetIcon(), GUILayout.Width(20));
                GUILayout.Label($"<b>{item.Label}</b>", isSelected ? headerStyle : labelValueStyle, GUILayout.Width(150));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=#38bdf8>{item.TypeTag}</color>", labelValueStyle);
                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) {
                    intelliSenseSelectedIndex = i;
                    ApplyIntelliSense(item);
                    Event.current.Use();
                }

                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();

            // Selected Item Documentation Pane
            if (intelliSenseSelectedIndex >= 0 && intelliSenseSelectedIndex < intelliSenseItems.Count) {
                var cur = intelliSenseItems[intelliSenseSelectedIndex];
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#94a3b8>{cur.Doc}</color>", labelValueStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
            GUI.depth = 0;
        }

        /// <summary>
        /// 绘制停止条件快捷补全分类栏 (天体、坐标系、航天器前缀胶囊)
        /// </summary>
        private void DrawPropStopAutocompleteBar(int stepIndex, MissionStep step) {
            GUILayout.Space(2);
            Vessel v = FlightGlobals.ActiveVessel;
            List<string> allBodies = GMATParameterRegistry.GetAllBodies(v);
            List<string> allCoords = GMATParameterRegistry.GetAllCoordinateSystems();
            List<string> allCraft = GMATParameterRegistry.GetAllSpacecraft(v);

            // Row 1: Entity Category Tabs
            GUILayout.BeginHorizontal();
            GUILayout.Space(75);
            GUILayout.Label(I18n.T("widget_obj_type"), labelValueStyle, GUILayout.Width(60));

            string[] catTabs = new string[] { I18n.T("cat_body"), I18n.T("cat_coord"), I18n.T("cat_craft") };
            for (int t = 0; t < catTabs.Length; t++) {
                bool isSel = (propCategoryTab == t);
                GUI.backgroundColor = isSel ? new Color(0.2f, 0.7f, 0.9f) : Color.white;
                if (GUILayout.Button(catTabs[t], secondaryButtonStyle, GUILayout.Height(18))) {
                    propCategoryTab = t;
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Row 2: Selected Category's Chips
            GUILayout.BeginHorizontal();
            GUILayout.Space(75);
            GUILayout.Label(I18n.T("widget_completion_obj"), labelValueStyle, GUILayout.Width(60));

            if (propCategoryTab == 0) { // 天体
                int chipLimit = Math.Min(6, allBodies.Count);
                for (int b = 0; b < chipLimit; b++) {
                    string bName = allBodies[b];
                    bool hasPrefix = !string.IsNullOrEmpty(step.stop_type) && step.stop_type.StartsWith(bName + ".");
                    GUI.backgroundColor = hasPrefix ? new Color(0.2f, 0.7f, 0.9f) : Color.white;
                    if (GUILayout.Button(bName + ".", secondaryButtonStyle, GUILayout.Height(18))) {
                        ApplyPropStopPrefix(step, bName);
                        pendingFocusControl = $"Field_PropStop_{stepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    }
                    GUI.backgroundColor = Color.white;
                }
            } else if (propCategoryTab == 1) { // 坐标系
                for (int c = 0; c < Math.Min(5, allCoords.Count); c++) {
                    string cName = allCoords[c];
                    bool hasPrefix = !string.IsNullOrEmpty(step.stop_type) && step.stop_type.StartsWith(cName + ".");
                    GUI.backgroundColor = hasPrefix ? new Color(0.2f, 0.7f, 0.9f) : Color.white;
                    if (GUILayout.Button(cName + ".", secondaryButtonStyle, GUILayout.Height(18))) {
                        ApplyPropStopPrefix(step, cName);
                        pendingFocusControl = $"Field_PropStop_{stepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    }
                    GUI.backgroundColor = Color.white;
                }
            } else if (propCategoryTab == 2) { // 航天器
                for (int sc = 0; sc < allCraft.Count; sc++) {
                    string scName = allCraft[sc];
                    bool hasPrefix = !string.IsNullOrEmpty(step.stop_type) && step.stop_type.StartsWith(scName + ".");
                    GUI.backgroundColor = hasPrefix ? new Color(0.2f, 0.7f, 0.9f) : Color.white;
                    if (GUILayout.Button(scName + ".", secondaryButtonStyle, GUILayout.Height(18))) {
                        ApplyPropStopPrefix(step, scName);
                        pendingFocusControl = $"Field_PropStop_{stepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Row 3: Dynamic Smart Suggestions
            string curStop = (step.stop_type ?? "").Trim();
            List<IntelliSenseItem> suggestions = GMATParameterRegistry.QueryIntelliSense(curStop, v);
            if (suggestions.Count > 0) {
                GUILayout.Space(1);
                GUILayout.BeginHorizontal();
                GUILayout.Space(75);
                GUILayout.Label(I18n.T("widget_smart_suggest"), labelValueStyle, GUILayout.Width(60));
                for (int s = 0; s < Math.Min(6, suggestions.Count); s++) {
                    var sug = suggestions[s];
                    if (GUILayout.Button(sug.InsertText, secondaryButtonStyle, GUILayout.Height(18))) {
                        step.stop_type = sug.InsertText;
                        if (sug.RecommendedValue != 0) {
                            step.target_val = sug.RecommendedValue;
                            step.target_val_str = sug.RecommendedValue.ToString("F3");
                            step.duration = sug.RecommendedValue;
                        }
                        pendingFocusControl = $"Field_PropStop_{stepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 格式化应用对象前缀至停止条件
        /// </summary>
        private void ApplyPropStopPrefix(MissionStep step, string objectPrefix) {
            string cur = (step.stop_type ?? "").Trim();
            if (string.IsNullOrEmpty(cur)) {
                step.stop_type = objectPrefix + ".";
                return;
            }

            if (objectPrefix.Equals("Sat", StringComparison.OrdinalIgnoreCase) || objectPrefix.StartsWith("Sat", StringComparison.OrdinalIgnoreCase)) {
                if (cur.StartsWith("Sat.", StringComparison.OrdinalIgnoreCase)) return;
                step.stop_type = "Sat." + cur;
                return;
            }

            bool isBody = objectPrefix.Equals("Luna", StringComparison.OrdinalIgnoreCase) || objectPrefix.Equals("Earth", StringComparison.OrdinalIgnoreCase) || objectPrefix.Equals("Sun", StringComparison.OrdinalIgnoreCase) || objectPrefix.Equals("Mars", StringComparison.OrdinalIgnoreCase) || objectPrefix.Equals("Jupiter", StringComparison.OrdinalIgnoreCase);
            if (isBody) {
                if (cur.StartsWith("Sat.", StringComparison.OrdinalIgnoreCase)) {
                    string rest = cur.Substring(4);
                    if (rest.Contains(".")) {
                        string paramOnly = rest.Substring(rest.LastIndexOf('.') + 1);
                        step.stop_type = $"Sat.{objectPrefix}.{paramOnly}";
                    } else {
                        step.stop_type = $"Sat.{objectPrefix}.{rest}";
                    }
                } else if (cur.Contains(".")) {
                    string paramOnly = cur.Substring(cur.LastIndexOf('.') + 1);
                    step.stop_type = $"{objectPrefix}.{paramOnly}";
                } else {
                    step.stop_type = $"{objectPrefix}.{cur}";
                }
                return;
            }

            if (cur.StartsWith("Sat.", StringComparison.OrdinalIgnoreCase)) {
                string paramOnly = cur.Contains(".") ? cur.Substring(cur.LastIndexOf('.') + 1) : "Z";
                if (paramOnly == "Periapsis" || paramOnly == "Apoapsis" || paramOnly == "Time") paramOnly = "Z";
                step.stop_type = $"Sat.{objectPrefix}.{paramOnly}";
            } else {
                string paramOnly = cur.Contains(".") ? cur.Substring(cur.LastIndexOf('.') + 1) : "Z";
                step.stop_type = $"{objectPrefix}.{paramOnly}";
            }
        }

        // =========================================================================
        // 5. 标签式表单字段控件 (Labeled Form Input Fields)
        // =========================================================================

        private static Dictionary<string, string> smartFieldBuffers = new Dictionary<string, string>();
        private static string activeSmartControlId = null;

        /// <summary>
        /// 智能度量衡与物理单位推断器：根据字段标签文本与上下文自动补齐物理单位，提升工程排版规整度
        /// </summary>
        private static string InferPhysicalUnit(string label) {
            if (string.IsNullOrEmpty(label)) return null;
            string lower = label.ToLowerInvariant();
            if (lower.Contains("fuel") || lower.Contains("燃料") || lower.Contains("干重") || lower.Contains("dry") || lower.Contains("质量") || lower.Contains("mass")) return "kg";
            if (lower.Contains("容积") || lower.Contains("vol") || lower.Contains("volume") || lower.Contains("m3")) return "m³";
            if (lower.Contains("thrust") || lower.Contains("推力")) return "kN";
            if (lower.Contains("isp") || lower.Contains("比冲")) return "s";
            if (lower.Contains("delta-v") || lower.Contains("δv") || lower.Contains("dv") || lower.Contains("速度")) return "m/s";
            if (lower.Contains("drag") || lower.Contains("迎风") || lower.Contains("srp") || lower.Contains("光压")) return "m²";
            if (lower.Contains("density") || lower.Contains("密度")) return "kg/m³";
            if (lower.Contains("pres") || lower.Contains("压强") || lower.Contains("压力")) return "kPa";
            return null;
        }

        /// <summary>
        /// 智能测量标签自适应紧凑宽度，杜绝写死大宽（如 90px/110px 导致单字或双字后留出 50px 荒唐空白）
        /// </summary>
        private static float GetSmartLabelWidth(string label, float requestedW, GUIStyle style) {
            if (string.IsNullOrEmpty(label)) return 0f;
            float measuredW = style != null ? style.CalcSize(new GUIContent(label)).x + 6f : (label.Length * 12f + 8f);
            if (requestedW <= 0f) return measuredW;
            // 若请求宽度超出测量宽度 14px 以上，则自动平滑收敛，紧密贴合内容，消除怪诞留白
            return (requestedW > measuredW + 14f) ? measuredW : requestedW;
        }

        /// <summary>
        /// 带左侧文本标签的延迟浮点数值输入表单行 (自适应紧密排版 + 自动单位标注)
        /// </summary>
        private double DrawDoubleField(string label, double val, float labelW = 110, float fieldW = 80, string format = "auto", string customId = null) {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label)) {
                float actualLabelW = GetSmartLabelWidth(label, labelW, labelValueStyle);
                GUILayout.Label(label, labelValueStyle, GUILayout.Width(actualLabelW));
                GUILayout.Space(3);
            }
            
            string ctrlId = !string.IsNullOrEmpty(customId) ? customId : ("res_field_" + (label ?? "num").GetHashCode());
            GUI.SetNextControlName(ctrlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == ctrlId);

            string currentBuf;
            if (!smartFieldBuffers.TryGetValue(ctrlId, out currentBuf)) {
                currentBuf = FormatSmartDoubleForDisplay(val, format);
                smartFieldBuffers[ctrlId] = currentBuf;
            }

            if (!isFocused) {
                if (activeSmartControlId == ctrlId) {
                    activeSmartControlId = null;
                }
                currentBuf = FormatSmartDoubleForDisplay(val, format);
                smartFieldBuffers[ctrlId] = currentBuf;
            } else {
                if (activeSmartControlId != ctrlId) {
                    activeSmartControlId = ctrlId;
                    currentBuf = (val == 0.0) ? "0.0" : val.ToString("G", CultureInfo.InvariantCulture);
                    smartFieldBuffers[ctrlId] = currentBuf;
                }
            }

            string edited = GUILayout.TextField(currentBuf, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW), GUILayout.Height(20));
            if (edited != currentBuf) {
                smartFieldBuffers[ctrlId] = edited;
                double res;
                if (double.TryParse(edited, NumberStyles.Any, CultureInfo.InvariantCulture, out res)) {
                    val = res;
                }
            }

            // 智能物理单位标注，增强比例饱满度
            string unit = InferPhysicalUnit(label);
            if (!string.IsNullOrEmpty(unit)) {
                GUILayout.Space(2);
                GUILayout.Label($"<color=#7dd3fc>{unit}</color>", labelValueStyle, GUILayout.Width(24));
            }

            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            return val;
        }

        /// <summary>
        /// 智能浮点显示格式化 (根据数量级自动选用科学计数法、高精度小数或定点数)
        /// </summary>
        private static string FormatSmartDoubleForDisplay(double val, string format = "auto") {
            if (val == 0.0) return "0.0";
            if (format != "auto") {
                return val.ToString(format, CultureInfo.InvariantCulture);
            }
            double abs = Math.Abs(val);
            if (abs < 1e-4) {
                return val.ToString("E2", CultureInfo.InvariantCulture); // e.g. 1.00E-11
            }
            if (abs >= 10000.0) {
                return val.ToString("F1", CultureInfo.InvariantCulture); // e.g. 35786.0
            }
            if (abs >= 100.0) {
                return val.ToString("F2", CultureInfo.InvariantCulture); // e.g. 6282.84
            }
            if (abs >= 1.0) {
                return val.ToString("F2", CultureInfo.InvariantCulture); // e.g. 2.16
            }
            return val.ToString("0.####", CultureInfo.InvariantCulture); // e.g. 0.0001
        }

        /// <summary>
        /// 带左侧文本标签的字符串文本输入表单行 (自适应紧密排版)
        /// </summary>
        private string DrawTextField(string label, string val, float labelW = 110, float fieldW = 120) {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label)) {
                float actualLabelW = GetSmartLabelWidth(label, labelW, labelValueStyle);
                GUILayout.Label(label, labelValueStyle, GUILayout.Width(actualLabelW));
                GUILayout.Space(3);
            }
            string txt = GUILayout.TextField(val ?? "", stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW), GUILayout.Height(20));
            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            return txt;
        }

        /// <summary>
        /// 带左侧文本标签的整型数值输入表单行 (自适应紧密排版 + 居中数字)
        /// </summary>
        private int DrawIntField(string label, int val, float labelW = 110, float fieldW = 50) {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label)) {
                float actualLabelW = GetSmartLabelWidth(label, labelW, labelValueStyle);
                GUILayout.Label(label, labelValueStyle, GUILayout.Width(actualLabelW));
                GUILayout.Space(3);
            }
            string txt = GUILayout.TextField(val.ToString(), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW), GUILayout.Height(20));
            int res;
            if (int.TryParse(txt, out res)) {
                val = res;
            }
            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            return val;
        }

        /// <summary>
        /// 统一紧凑标签表单输入控件：用于时序节点中的多参数行排版（如 Vary 步的初/下/上/步/扰），保持定宽与呼吸间距
        /// </summary>
        /// <param name="label">参数简写或标签 (如 "初", "下", "上")</param>
        /// <param name="controlId">输入框唯一标识符</param>
        /// <param name="rawStr">当前文本内容</param>
        /// <param name="fallbackVal">默认兜底数值</param>
        /// <param name="fieldW">输入框定宽 (默认 68px)</param>
        /// <param name="intelliMode">可选自动补全模式</param>
        /// <param name="stepIdx">时序步骤索引</param>
        /// <returns>输入框最新的文本值</returns>
        private string DrawCompactLabeledSmartField(string label, string controlId, string rawStr, double fallbackVal, float fieldW = 68f, string intelliMode = null, int stepIdx = -1) {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label)) {
                float measuredW = gridLabelStyle != null ? gridLabelStyle.CalcSize(new GUIContent(label)).x + 2f : 16f;
                GUILayout.Label(label, gridLabelStyle ?? GUI.skin.label, GUILayout.Width(measuredW));
                GUILayout.Space(3);
            }
            string result = DrawDelayedSmartField(controlId, rawStr, fallbackVal, numericInputStyle, GUILayout.Width(fieldW), intelliMode, stepIdx);
            GUILayout.Space(6);
            GUILayout.EndHorizontal();
            return result;
        }

        // =========================================================================
        // 5. 脚本与任务时序复用组件 (Script & Mission Sequence Reusable Widgets)
        // =========================================================================

        /// <summary>
        /// 单步代码透视就地编辑文本与状态缓存池
        /// </summary>
        private static readonly Dictionary<MissionStep, string> stepCodeEditCache = new Dictionary<MissionStep, string>();
        private static readonly Dictionary<MissionStep, bool> stepCodeSyncStatus = new Dictionary<MissionStep, bool>();

        /// <summary>
        /// 单步 GMAT 原生脚本透视展示组件：支持暗黑高亮展示与实时就地直接编辑数值与参数 (Live Two-Way Binding)
        /// </summary>
        /// <param name="step">任务步骤</param>
        private void DrawStepCodePeekBox(MissionStep step) {
            if (step == null || !step.show_code_peek) return;
            GUILayout.Space(2);
            GUILayout.BeginVertical(nestedCardBoxStyle);

            string controlId = $"StepCodePeek_{step.GetHashCode()}";
            GUI.SetNextControlName(controlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);

            string editBuffer;
            if (!stepCodeEditCache.TryGetValue(step, out editBuffer)) {
                editBuffer = GMATScriptGenerator.GenerateStepScript(step);
                stepCodeEditCache[step] = editBuffer;
                stepCodeSyncStatus[step] = true;
            }

            // 当未聚焦且处于同步状态时，自动跟随上层表单的变化生成最新代码
            if (!isFocused && stepCodeSyncStatus.TryGetValue(step, out bool synced) && synced) {
                editBuffer = GMATScriptGenerator.GenerateStepScript(step);
                stepCodeEditCache[step] = editBuffer;
            }

            // 顶部工具状态条
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("seq_step_code_title"), labelValueStyle);
            GUILayout.FlexibleSpace();

            bool isCurrentlySynced;
            if (!stepCodeSyncStatus.TryGetValue(step, out isCurrentlySynced)) isCurrentlySynced = true;

            string syncBadge = isCurrentlySynced 
                ? $"<color=#10b981><b>✔ {I18n.T("seq_step_synced")}</b></color>"
                : $"<color=#f59e0b><b>✎ {I18n.T("seq_step_editing")}</b></color>";
            GUILayout.Label(syncBadge, labelValueStyle);
            GUILayout.Space(6);

            if (GUILayout.Button(I18n.T("btn_step_code_reset"), secondaryButtonStyle, GUILayout.Height(18))) {
                editBuffer = GMATScriptGenerator.GenerateStepScript(step);
                stepCodeEditCache[step] = editBuffer;
                stepCodeSyncStatus[step] = true;
                GUI.FocusControl(null);
            }
            GUILayout.Space(3);
            if (GUILayout.Button(I18n.T("btn_step_code_copy"), secondaryButtonStyle, GUILayout.Height(18))) {
                GUIUtility.systemCopyBuffer = editBuffer;
                ScreenMessages.PostScreenMessage(I18n.T("btn_step_code_copy"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 锚点定位与全屏智能补全窗对齐
            if (Event.current.type == EventType.Repaint && isFocused) {
                Rect r = GUILayoutUtility.GetLastRect();
                Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                intelliSenseAnchorRect = new Rect(sp.x, sp.y, r.width, r.height);
            }

            // 主动快捷键支持：聚焦时按 Ctrl + Space 主动唤起智能补全
            if (isFocused && Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.Space) {
                int tStart;
                string tStr = ExtractIntelliSenseToken(editBuffer, out tStart);
                int capStart = tStart;
                string capTok = tStr;
                TriggerIntelliSense(tStr, "script_code", -1, (item) => {
                    string cur = stepCodeEditCache.ContainsKey(step) ? stepCodeEditCache[step] : editBuffer;
                    if (capStart >= 0 && capStart <= cur.Length) {
                        string before = cur.Substring(0, capStart);
                        int afterStart = Math.Min(cur.Length, capStart + capTok.Length);
                        string after = cur.Substring(afterStart);
                        string replaced = before + item.InsertText + after;
                        stepCodeEditCache[step] = replaced;
                        bool parseOk = GMATScriptParser.ParseSingleStepScript(step, replaced);
                        stepCodeSyncStatus[step] = parseOk;
                        if (parseOk) {
                            inputStringCache.Clear();
                            ideHasUnsavedEdits = true;
                        }
                    }
                });
                Event.current.Use();
            }

            // 可直接编辑的单步原生脚本输入框 (使用 ideEditorStyle，暗黑背景等宽字体)
            string newText = GUILayout.TextArea(editBuffer, ideEditorStyle, GUILayout.MinHeight(24), GUILayout.ExpandWidth(true));
            if (newText != editBuffer) {
                stepCodeEditCache[step] = newText;
                bool ok = GMATScriptParser.ParseSingleStepScript(step, newText);
                stepCodeSyncStatus[step] = ok;
                if (ok) {
                    // 清理 inputStringCache 关联控件，强制上层各表单控件在下一帧重取最新模型值
                    inputStringCache.Clear();
                    ideHasUnsavedEdits = true;
                }

                // 统一智能自动补全接入：提取当前光标处正在输入的 Token (如 Sat.Earth.S 或变量名)
                int tokenStart;
                string token = ExtractIntelliSenseToken(newText, out tokenStart);
                if (!string.IsNullOrEmpty(token) && (token.Contains(".") || token.Length >= 2)) {
                    int capturedStart = tokenStart;
                    string capturedToken = token;
                    TriggerIntelliSense(token, "script_code", -1, (item) => {
                        string cur = stepCodeEditCache.ContainsKey(step) ? stepCodeEditCache[step] : newText;
                        if (capturedStart >= 0 && capturedStart <= cur.Length) {
                            string before = cur.Substring(0, capturedStart);
                            int afterStart = Math.Min(cur.Length, capturedStart + capturedToken.Length);
                            string after = cur.Substring(afterStart);
                            string replaced = before + item.InsertText + after;
                            stepCodeEditCache[step] = replaced;
                            bool parseOk = GMATScriptParser.ParseSingleStepScript(step, replaced);
                            stepCodeSyncStatus[step] = parseOk;
                            if (parseOk) {
                                inputStringCache.Clear();
                                ideHasUnsavedEdits = true;
                            }
                        }
                    });
                }
            }

            GUILayout.EndVertical();
        }

        /// <summary>
        /// 脚本静态语法分析与自愈门禁状态条组件 (包含健康徽章、一键自愈与问题诊断折叠列表)
        /// </summary>
        /// <param name="currentScript">当前待分析的脚本文本</param>
        /// <param name="vessel">当前航天器</param>
        private void DrawScriptLinterStatusBar(string currentScript, Vessel vessel) {
            GMATLintReport activeLint = GMATScriptLinter.Lint(currentScript);
            currentLintReport = activeLint;

            GUILayout.BeginHorizontal(nestedCardBoxStyle);
            string statusBadge;
            if (activeLint.IsClean) {
                statusBadge = $"<color=#10b981><b>{I18n.T("ide_lint_status_clean")}</b></color>";
            } else if (activeLint.HasErrors) {
                statusBadge = $"<color=#ef4444><b>{I18n.Format("ide_lint_status_issues", activeLint.ErrorCount, activeLint.WarningCount)}</b></color>";
            } else {
                statusBadge = $"<color=#f59e0b><b>{I18n.Format("ide_lint_status_issues", activeLint.ErrorCount, activeLint.WarningCount)}</b></color>";
            }
            GUILayout.Label(statusBadge, labelValueStyle);
            GUILayout.FlexibleSpace();

            if (!activeLint.IsClean) {
                if (GUILayout.Button(I18n.Format("ide_lint_toggle_issues", activeLint.Issues.Count), secondaryButtonStyle, GUILayout.Height(20))) {
                    showLintIssuesDrawer = !showLintIssuesDrawer;
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("ide_lint_autofix"), successButtonStyle, GUILayout.Height(20))) {
                    string fixedScript = GMATScriptLinter.FixScript(currentScript);
                    if (ideViewMode != 0) {
                        ideCustomBuffer = fixedScript;
                        ideHasUnsavedEdits = true;
                    } else {
                        SaveAndSyncIdeScriptToSequence(fixedScript, vessel);
                    }
                    ScreenMessages.PostScreenMessage(I18n.T("ide_lint_autofix"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUILayout.EndHorizontal();

            if (showLintIssuesDrawer && !activeLint.IsClean) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                lintIssuesScroll = GUILayout.BeginScrollView(lintIssuesScroll, GUILayout.MaxHeight(90));
                for (int iss = 0; iss < activeLint.Issues.Count; iss++) {
                    var issue = activeLint.Issues[iss];
                    string sevTag = (issue.Severity == GMATLintSeverity.Error) ? "<color=#ef4444>[ERR]</color>" : ((issue.Severity == GMATLintSeverity.Warning) ? "<color=#f59e0b>[WARN]</color>" : "<color=#38bdf8>[INFO]</color>");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{sevTag} <color=#64748b>L{issue.LineNumber}:</color> {issue.DefaultMessage}", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button($"➔ L{issue.LineNumber}", secondaryButtonStyle, GUILayout.Width(50), GUILayout.Height(18))) {
                        targetJumpLine = issue.LineNumber;
                        ideScriptScroll.y = Mathf.Max(0, (issue.LineNumber - 3) * 18);
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }
        }

        /// <summary>
        /// 官方 7 大标准脚本范式库抽屉组件 (Canonical Blueprints Drawer)
        /// </summary>
        private void DrawScriptBlueprintDrawer() {
            if (!showBlueprintDrawer) return;

            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>{I18n.T("ide_bp_drawer_title")}</b></color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✖", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(18))) {
                showBlueprintDrawer = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            blueprintScrollPos = GUILayout.BeginScrollView(blueprintScrollPos, GUILayout.MaxHeight(150));
            var allBlueprints = GMATBlueprintLibrary.GetAllBlueprints();
            for (int b = 0; b < allBlueprints.Count; b++) {
                var bp = allBlueprints[b];
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{I18n.T(bp.TitleKey, bp.TitleDefault)}</b> <color=#64748b>({bp.Category})</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("ide_bp_replace"), actionButtonStyle, GUILayout.Height(18))) {
                    string formatted = bp.GetFormattedScript();
                    ideCustomBuffer = formatted;
                    ideHasUnsavedEdits = true;
                    ideViewMode = 1; // Switch to Edit mode
                    showBlueprintDrawer = false;
                    ScreenMessages.PostScreenMessage(I18n.Format("ide_bp_loaded", I18n.T(bp.TitleKey, bp.TitleDefault)), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.Space(3);
                if (GUILayout.Button(I18n.T("ide_bp_insert"), secondaryButtonStyle, GUILayout.Height(18))) {
                    string formatted = bp.GetFormattedScript();
                    InsertIntoIdeBuffer(formatted);
                    ideHasUnsavedEdits = true;
                    ideViewMode = 1; // Switch to Edit mode
                    showBlueprintDrawer = false;
                    ScreenMessages.PostScreenMessage(I18n.Format("ide_bp_loaded", I18n.T(bp.TitleKey, bp.TitleDefault)), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();
                GUILayout.Label($"<color=#94a3b8>{I18n.T(bp.DescriptionKey, bp.DescriptionDefault)}</color>", labelValueStyle);
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        /// <summary>
        /// 双栏视觉差异比对与合并视图组件 (Myers Diff Visual Split-View)
        /// </summary>
        /// <param name="baselineText">原生纯净基准脚本</param>
        /// <param name="userText">用户工作区脚本缓冲区</param>
        /// <param name="vessel">当前活跃飞船</param>
        /// <param name="rightW">右侧面板总宽度</param>
        private void DrawScriptDiffViewer(string baselineText, string userText, Vessel vessel, float rightW) {
            var diff = GMATDiffEngine.ComputeDiff(baselineText, userText);

            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            // Diff Control & Stats Bar
            GUILayout.BeginHorizontal(nestedCardBoxStyle);
            string statsText = diff.HasDifferences 
                ? I18n.Format("ide_diff_stats", diff.InsertCount, diff.DeleteCount, diff.ModifyCount)
                : I18n.T("ide_diff_no_changes");
            string statColor = diff.HasDifferences ? "#fbbf24" : "#10b981";
            GUILayout.Label($"<color={statColor}><b>{statsText}</b></color>", labelValueStyle);
            GUILayout.FlexibleSpace();

            GUI.enabled = diff.HasDifferences;
            if (GUILayout.Button(I18n.T("ide_diff_revert_all"), secondaryButtonStyle, GUILayout.Height(20))) {
                ideCustomBuffer = baselineText;
                ideLastLiveScriptSnapshot = baselineText;
                ideHasUnsavedEdits = false;
                ScreenMessages.PostScreenMessage(I18n.T("res_restored"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.Space(4);
            if (GUILayout.Button(I18n.T("ide_diff_apply_all"), successButtonStyle, GUILayout.Height(20))) {
                SaveAndSyncIdeScriptToSequence(userText, vessel);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Column Headers
            float paneW = (rightW - 30f) * 0.49f;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>{I18n.T("ide_diff_baseline")}</b></color>", labelValueStyle, GUILayout.Width(paneW));
            GUILayout.Space(16);
            GUILayout.Label($"<color=#f59e0b><b>{I18n.T("ide_diff_workspace")}</b></color>", labelValueStyle, GUILayout.Width(paneW));
            GUILayout.EndHorizontal();

            // Synchronized Scrollable Diff Body
            ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
            for (int d = 0; d < diff.Lines.Count; d++) {
                var dl = diff.Lines[d];
                GUILayout.BeginHorizontal();

                // Left Line (Baseline)
                string leftNum = (dl.LineNumberA > 0) ? $"<color=#64748b>{dl.LineNumberA,3}:</color> " : "<color=#334155>   :</color> ";
                string leftContent = dl.ContentA ?? "";
                string leftDisplay;
                if (dl.Type == GMATDiffType.Equal) {
                    leftDisplay = leftNum + ColorizeGmatSyntax(leftContent);
                } else if (dl.Type == GMATDiffType.Delete) {
                    leftDisplay = leftNum + $"<color=#f87171><b>- {leftContent}</b></color>";
                } else if (dl.Type == GMATDiffType.Modify) {
                    leftDisplay = leftNum + $"<color=#fbbf24><b>~ {leftContent}</b></color>";
                } else {
                    leftDisplay = leftNum + "<color=#334155><i>----------------</i></color>";
                }
                GUILayout.Label(leftDisplay, codeBlockStyle, GUILayout.Width(paneW));

                // Center Divider Indicator
                string midTag = (dl.Type == GMATDiffType.Equal) ? "<color=#334155>|</color>" : 
                                (dl.Type == GMATDiffType.Insert) ? "<color=#34d399>+</color>" : 
                                (dl.Type == GMATDiffType.Delete) ? "<color=#f87171>-</color>" : "<color=#fbbf24>~</color>";
                GUILayout.Label(midTag, labelValueStyle, GUILayout.Width(14));

                // Right Line (User Workspace)
                string rightNum = (dl.LineNumberB > 0) ? $"<color=#64748b>{dl.LineNumberB,3}:</color> " : "<color=#334155>   :</color> ";
                string rightContent = dl.ContentB ?? "";
                string rightDisplay;
                if (dl.Type == GMATDiffType.Equal) {
                    rightDisplay = rightNum + ColorizeGmatSyntax(rightContent);
                } else if (dl.Type == GMATDiffType.Insert) {
                    rightDisplay = rightNum + $"<color=#34d399><b>+ {rightContent}</b></color>";
                } else if (dl.Type == GMATDiffType.Modify) {
                    rightDisplay = rightNum + $"<color=#fbbf24><b>~ {rightContent}</b></color>";
                } else {
                    rightDisplay = rightNum + "<color=#334155><i>----------------</i></color>";
                }
                GUILayout.Label(rightDisplay, codeBlockStyle, GUILayout.Width(paneW));

                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        // =========================================================================
        // 9. 通用求解组与优化组层次容器控件 (Nested Solver Group Framework)
        // =========================================================================

        public class SolverGroupFrame {
            public int StepIndex;
            public string GroupType; // "Target" or "Optimize"
            public string SolverName;
            public bool IsCollapsed;
        }

        private void SetOptimizeGroupSolver(int optStepIndex, string newOptName) {
            if (optStepIndex < 0 || optStepIndex >= missionSequence.Count) return;
            var optStep = missionSequence[optStepIndex];
            optStep.optimizer_name = newOptName;

            // Cascade update child steps in this Optimize group
            for (int ch = optStepIndex + 1; ch < missionSequence.Count; ch++) {
                if (missionSequence[ch].type == "EndOptimize") {
                    missionSequence[ch].optimizer_name = newOptName;
                    break;
                }
                if (missionSequence[ch].type == "Optimize") break;
                if (missionSequence[ch].type == "Vary" || missionSequence[ch].type == "Minimize" || missionSequence[ch].type == "NonlinearConstraint") {
                    missionSequence[ch].optimizer_name = newOptName;
                    missionSequence[ch].dc_name = newOptName;
                }
            }
        }

        /// <summary>
        /// 绘制 Optimize 优化求解组头部栏、统计摘要、高级配置面板与组内快捷操作栏
        /// </summary>
        private void DrawOptimizeGroupHeader(
            int i, 
            MissionStep step, 
            ref int stepToMoveUp, 
            ref int stepToMoveDown, 
            ref int draggedStepIndex, 
            ref int stepToDelete, 
            ref int stepToInsertVary, 
            ref int stepToInsertMinimize, 
            ref int stepToInsertNlc, 
            ref int stepToInsertBurn, 
            ref int stepToInsertProp, 
            ref int stepToInsertThrustVector) 
        {
            bool isBeingDragged = (draggedStepIndex == i);

            // 统计该优化组内子步骤
            int varyCount = 0;
            int minCount = 0;
            int nlcCount = 0;
            int burnCount = 0;
            int totalCount = 0;
            int groupEndIdx = missionSequence.Count;

            int optDepth = 0;
            for (int ch = i + 1; ch < missionSequence.Count; ch++) {
                string ct = missionSequence[ch].type;
                if (ct == "Optimize") {
                    optDepth++;
                } else if (ct == "EndOptimize") {
                    if (optDepth > 0) {
                        optDepth--;
                    } else {
                        groupEndIdx = ch;
                        break;
                    }
                }
                totalCount++;
                if (ct == "Vary") varyCount++;
                if (ct == "Minimize") minCount++;
                if (ct == "NonlinearConstraint") nlcCount++;
                if (ct == "FiniteBurn" || ct == "ImpulsiveBurn") burnCount++;
            }

            // Top Header Bar
            GUILayout.BeginHorizontal();

            string foldIcon = step.is_collapsed ? I18n.T("seq_opt_expand") : I18n.T("seq_opt_collapse");
            if (GUILayout.Button(foldIcon, secondaryButtonStyle, GUILayout.Width(100), GUILayout.Height(20))) {
                step.is_collapsed = !step.is_collapsed;
            }

            GUI.enabled = (i > 0);
            if (GUILayout.Button("↑", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(20))) {
                stepToMoveUp = i;
            }
            GUI.enabled = (i < missionSequence.Count - 1);
            if (GUILayout.Button("↓", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(20))) {
                stepToMoveDown = i;
            }
            GUI.enabled = true;

            if (GUILayout.Button(isBeingDragged ? I18n.T("seq_dragging_cancel") : I18n.T("btn_drag"), isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 120 : 54), GUILayout.Height(20))) {
                draggedStepIndex = isBeingDragged ? -1 : i;
            }

            GUILayout.Label(I18n.Format("seq_opt_title_label", i + 1), headerStyle, GUILayout.Width(130));
            step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? I18n.T("seq_opt_default_label") : step.label, stepLabelInputStyle, GUILayout.MinWidth(140), GUILayout.MaxWidth(280), GUILayout.Height(20));
            GUILayout.FlexibleSpace();

            string optBadge = I18n.Format("seq_opt_badge", string.IsNullOrEmpty(step.optimizer_name) ? "Yukon1" : step.optimizer_name, step.solve_mode ?? "Solve");
            GUILayout.Label(optBadge, labelValueStyle);

            bool showCfg = expandedSolverSettings.Contains(i);
            if (GUILayout.Button(showCfg ? I18n.T("btn_opt_cfg_close") : I18n.T("btn_opt_cfg_open"), secondaryButtonStyle, GUILayout.Height(20))) {
                if (showCfg) expandedSolverSettings.Remove(i);
                else expandedSolverSettings.Add(i);
            }

            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                stepToDelete = i;
            }
            GUILayout.EndHorizontal();

            // Folded Summary
            if (step.is_collapsed) {
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("seq_opt_collapsed_summary", totalCount, varyCount, nlcCount, minCount, burnCount), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Expanded Config & Quick Add
            if (!step.is_collapsed) {
                if (expandedSolverSettings.Contains(i)) {
                    GUILayout.Space(4);
                    GUILayout.BeginVertical(nestedCardBoxStyle);

                    // Row 1: Optimizer Object selector chips
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("seq_opt_name_label"), labelValueStyle, GUILayout.Width(100));
                    string curOpt = string.IsNullOrEmpty(step.optimizer_name) ? "Yukon1" : step.optimizer_name;
                    string editedOpt = GUILayout.TextField(curOpt, GUILayout.Width(110), GUILayout.Height(20));
                    if (editedOpt != curOpt) {
                        SetOptimizeGroupSolver(i, editedOpt);
                    }

                    List<string> availSolvers = GetAvailableSolverList();
                    for (int c = 0; c < availSolvers.Count; c++) {
                        string cs = availSolvers[c];
                        bool isSel = (step.optimizer_name == cs);
                        if (GUILayout.Button(cs, isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            SetOptimizeGroupSolver(i, cs);
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // Row 2: Solve Mode & Exit Mode
                    GUILayout.Space(3);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("seq_solve_mode"), gridLabelStyle, GUILayout.Width(80));
                    string[] solveModes = new string[] { "Solve", "RunInitialGuess" };
                    for (int m = 0; m < solveModes.Length; m++) {
                        bool isMode = (step.solve_mode == solveModes[m]);
                        if (GUILayout.Button(solveModes[m], isMode ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(115), GUILayout.Height(20))) {
                            step.solve_mode = solveModes[m];
                        }
                    }

                    GUILayout.Space(12);
                    GUILayout.Label("Exit Mode:", gridLabelStyle, GUILayout.Width(75));
                    string[] exitModes = new string[] { "SaveAndContinue", "DiscardAndContinue" };
                    for (int e = 0; e < exitModes.Length; e++) {
                        bool isExit = (step.exit_mode == exitModes[e]);
                        if (GUILayout.Button(exitModes[e], isExit ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(20))) {
                            step.exit_mode = exitModes[e];
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                // Quick Add Sub-steps for Optimize Group
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Space(8);
                GUILayout.Label(I18n.T("seq_quick_add"), labelValueStyle);

                if (GUILayout.Button(I18n.T("btn_add_vary2"), secondaryButtonStyle, GUILayout.Height(18))) {
                    stepToInsertVary = groupEndIdx;
                }
                if (GUILayout.Button(I18n.T("btn_add_minimize"), secondaryButtonStyle, GUILayout.Height(18))) {
                    stepToInsertMinimize = groupEndIdx;
                }
                if (GUILayout.Button(I18n.T("btn_add_nlc"), secondaryButtonStyle, GUILayout.Height(18))) {
                    stepToInsertNlc = groupEndIdx;
                }
                if (GUILayout.Button(I18n.T("btn_add_burn2"), secondaryButtonStyle, GUILayout.Height(18))) {
                    stepToInsertBurn = groupEndIdx;
                }
                if (GUILayout.Button(I18n.T("btn_add_prop2"), secondaryButtonStyle, GUILayout.Height(18))) {
                    stepToInsertProp = groupEndIdx;
                }
                if (GUILayout.Button(I18n.T("btn_add_thrustvec"), actionButtonStyle, GUILayout.Height(18))) {
                    stepToInsertThrustVector = groupEndIdx;
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
        }

        // =========================================================================
        // 现代化原子求解组块管理与防崩溃结构自愈引擎 (Atomic Group & Robust AST)
        // =========================================================================

        /// <summary>
        /// 获取指定步骤所属的完整原子块范围 [startIdx, count]
        /// 若该步骤为 Target 或 Optimize，则包含其所有子步骤直至匹配闭合的 EndTarget / EndOptimize；
        /// 若为闭合步骤 EndTarget / EndOptimize，则反向追溯至根起点；若为普通步骤，则 count 为 1。
        /// </summary>
        public static void GetAtomicStepBlockRange(List<MissionStep> seq, int index, out int startIdx, out int count) {
            startIdx = index;
            count = 1;
            if (seq == null || index < 0 || index >= seq.Count) return;

            string t = seq[index].type;
            if (t == "Target" || t == "Optimize") {
                string endType = (t == "Target") ? "EndTarget" : "EndOptimize";
                int depth = 0;
                for (int k = index; k < seq.Count; k++) {
                    if (seq[k].type == t) depth++;
                    else if (seq[k].type == endType) {
                        depth--;
                        if (depth == 0) {
                            count = (k - index) + 1;
                            return;
                        }
                    }
                }
                count = seq.Count - index; // 兜底：未闭合时包含至末尾
            } else if (t == "EndTarget" || t == "EndOptimize") {
                string rootType = (t == "EndTarget") ? "Target" : "Optimize";
                int depth = 0;
                for (int k = index; k >= 0; k--) {
                    if (seq[k].type == t) depth++;
                    else if (seq[k].type == rootType) {
                        depth--;
                        if (depth == 0) {
                            startIdx = k;
                            count = (index - k) + 1;
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 将指定索引处的原子块（单步骤或完整求解组）整体向上移动一位
        /// </summary>
        public static int MoveAtomicBlockUp(List<MissionStep> seq, int index) {
            if (seq == null || index <= 0 || index >= seq.Count) return index;
            GetAtomicStepBlockRange(seq, index, out int curStart, out int curCount);
            if (curStart <= 0) return curStart;

            // 获取紧随其前的前置原子块
            GetAtomicStepBlockRange(seq, curStart - 1, out int prevStart, out int prevCount);
            var curItems = seq.GetRange(curStart, curCount);
            seq.RemoveRange(curStart, curCount);
            seq.InsertRange(prevStart, curItems);
            return prevStart;
        }

        /// <summary>
        /// 将指定索引处的原子块（单步骤或完整求解组）整体向下移动一位
        /// </summary>
        public static int MoveAtomicBlockDown(List<MissionStep> seq, int index) {
            if (seq == null || index < 0 || index >= seq.Count) return index;
            GetAtomicStepBlockRange(seq, index, out int curStart, out int curCount);
            if (curStart + curCount >= seq.Count) return curStart;

            // 获取紧随其后的后置原子块
            GetAtomicStepBlockRange(seq, curStart + curCount, out int nextStart, out int nextCount);
            var curItems = seq.GetRange(curStart, curCount);
            seq.RemoveRange(curStart, curCount);
            int targetInsertIdx = curStart + nextCount;
            seq.InsertRange(targetInsertIdx, curItems);
            return targetInsertIdx;
        }

        /// <summary>
        /// 将指定原子块完整移动至目标插槽位置
        /// </summary>
        public static int MoveAtomicBlockTo(List<MissionStep> seq, int fromIndex, int toIndex) {
            if (seq == null || fromIndex < 0 || fromIndex >= seq.Count) return fromIndex;
            GetAtomicStepBlockRange(seq, fromIndex, out int curStart, out int curCount);
            if (toIndex >= curStart && toIndex <= curStart + curCount) return curStart; // 在自身范围内无需移动

            var curItems = seq.GetRange(curStart, curCount);
            seq.RemoveRange(curStart, curCount);
            if (toIndex > curStart) {
                toIndex -= curCount;
            }
            toIndex = Mathf.Clamp(toIndex, 0, seq.Count);
            seq.InsertRange(toIndex, curItems);
            return toIndex;
        }

        /// <summary>
        /// 完整删除指定步骤所属的原子块（若为求解组则级联删除全部子步骤）
        /// </summary>
        public static void DeleteAtomicBlock(List<MissionStep> seq, int index) {
            if (seq == null || index < 0 || index >= seq.Count) return;
            GetAtomicStepBlockRange(seq, index, out int curStart, out int curCount);
            seq.RemoveRange(curStart, curCount);
        }

        /// <summary>
        /// 自动校验并修复求解组与优化组的开闭合完整性，杜绝未配对标签导致 IMGUI 布局崩溃
        /// </summary>
        public static bool RepairSequenceGroupIntegrity(List<MissionStep> seq) {
            if (seq == null) return false;
            bool modified = false;

            Stack<string> openGroups = new Stack<string>();
            List<MissionStep> repaired = new List<MissionStep>();

            for (int i = 0; i < seq.Count; i++) {
                var step = seq[i];
                if (step.type == "Target") {
                    openGroups.Push("Target");
                    repaired.Add(step);
                } else if (step.type == "Optimize") {
                    openGroups.Push("Optimize");
                    repaired.Add(step);
                } else if (step.type == "EndTarget") {
                    if (openGroups.Count > 0 && openGroups.Peek() == "Target") {
                        openGroups.Pop();
                        repaired.Add(step);
                    } else if (openGroups.Contains("Target")) {
                        // 遇到跨层闭合，先闭合顶层
                        while (openGroups.Count > 0 && openGroups.Peek() != "Target") {
                            string top = openGroups.Pop();
                            repaired.Add(new MissionStep { type = (top == "Optimize" ? "EndOptimize" : "EndTarget"), label = "Auto-Closed" });
                            modified = true;
                        }
                        if (openGroups.Count > 0 && openGroups.Peek() == "Target") {
                            openGroups.Pop();
                            repaired.Add(step);
                        }
                    } else {
                        // 孤立的 EndTarget，忽略移除
                        modified = true;
                    }
                } else if (step.type == "EndOptimize") {
                    if (openGroups.Count > 0 && openGroups.Peek() == "Optimize") {
                        openGroups.Pop();
                        repaired.Add(step);
                    } else if (openGroups.Contains("Optimize")) {
                        while (openGroups.Count > 0 && openGroups.Peek() != "Optimize") {
                            string top = openGroups.Pop();
                            repaired.Add(new MissionStep { type = (top == "Target" ? "EndTarget" : "EndOptimize"), label = "Auto-Closed" });
                            modified = true;
                        }
                        if (openGroups.Count > 0 && openGroups.Peek() == "Optimize") {
                            openGroups.Pop();
                            repaired.Add(step);
                        }
                    } else {
                        // 孤立的 EndOptimize，忽略移除
                        modified = true;
                    }
                } else {
                    repaired.Add(step);
                }
            }

            // 序列结束时仍未闭合的组，自动追加闭合标签
            while (openGroups.Count > 0) {
                string unclosed = openGroups.Pop();
                repaired.Add(new MissionStep {
                    type = (unclosed == "Target") ? "EndTarget" : "EndOptimize",
                    label = (unclosed == "Target") ? "闭合打靶" : "闭合优化"
                });
                modified = true;
            }

            if (modified) {
                seq.Clear();
                seq.AddRange(repaired);
            }
            return modified;
        }

        /// <summary>
        /// 绘制现代化前端交互式发光放置插槽指示器 (Dynamic Drop Slot Indicator)
        /// </summary>
        private void DrawModernDropIndicator(int insertIndex, string accentHex = "#38bdf8", bool isGroup = false) {
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            string dropText = I18n.Format("seq_drag_drop_here", insertIndex + 1);
            if (isGroup) {
                dropText += $" <color=#c084fc>{I18n.T("seq_drag_group_items")}</color>";
            }

            GUILayout.Label($"<color={accentHex}><b>─── ✦ {dropText} ───</b></color>", labelValueStyle, GUILayout.Height(18));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.Repaint) {
                Rect slotRect = GUILayoutUtility.GetLastRect();
                Color glowCol;
                if (!ColorUtility.TryParseHtmlString(accentHex, out glowCol)) {
                    glowCol = new Color(0.22f, 0.74f, 0.97f, 1f);
                }
                Color bgCol = new Color(glowCol.r, glowCol.g, glowCol.b, 0.18f);
                GUI.DrawTexture(new Rect(slotRect.x + 4, slotRect.y + 1, slotRect.width - 8, slotRect.height - 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, bgCol, 0, 4);
                GUI.DrawTexture(new Rect(slotRect.x + 4, slotRect.y, slotRect.width - 8, 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowCol, 0, 0);
            }
            GUILayout.Space(2);
        }

        /// <summary>
        /// 绘制随鼠标光标悬浮的现代化拖拽胶囊徽章 (Floating Drag Capsule)
        /// </summary>
        private void DrawFloatingDragPill(Vector2 mousePos, string itemTitle, int groupCount, int targetDropIdx) {
            float pillW = 280f;
            float pillH = 34f;
            float px = Mathf.Clamp(mousePos.x + 14f, 10f, Screen.width - pillW - 20f);
            float py = Mathf.Clamp(mousePos.y + 12f, 10f, Screen.height - pillH - 20f);
            Rect pillRect = new Rect(px, py, pillW, pillH);

            Color bgDark = new Color(0.05f, 0.07f, 0.12f, 0.96f);
            Color borderGlow = (groupCount > 1) ? new Color(0.78f, 0.48f, 0.98f, 0.95f) : new Color(0.22f, 0.74f, 0.97f, 0.95f);

            GUI.DrawTexture(pillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, bgDark, 0, 6);
            GUI.DrawTexture(new Rect(pillRect.x, pillRect.y, pillRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, borderGlow, 0, 0);
            GUI.DrawTexture(new Rect(pillRect.x, pillRect.y + pillRect.height - 1, pillRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, borderGlow, 0, 0);
            GUI.DrawTexture(new Rect(pillRect.x, pillRect.y, 1, pillRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, borderGlow, 0, 0);
            GUI.DrawTexture(new Rect(pillRect.x + pillRect.width - 1, pillRect.y, 1, pillRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, borderGlow, 0, 0);

            string titleStr = string.IsNullOrEmpty(itemTitle) ? "Module" : (itemTitle.Length > 20 ? itemTitle.Substring(0, 18) + ".." : itemTitle);
            string countNote = (groupCount > 1) ? $" <color=#e879f9>({groupCount}步整组)</color>" : "";
            string targetStr = (targetDropIdx >= 0) ? $" <color=#34d399>➜ #{targetDropIdx + 1}</color>" : "";

            GUI.Label(new Rect(pillRect.x + 8, pillRect.y + 7, pillRect.width - 16, 20), 
                      $"<color=#38bdf8><b>⠿</b></color> <b>{titleStr}</b>{countNote}{targetStr}", labelValueStyle);
        }

        // =========================================================================
        // 8. 统一航天 HUD 富文本显示组件家族 (Rich Text HUD Suite)
        // =========================================================================

        /// <summary>
        /// 绘制标准化航天 HUD 富文本信息行 (强制保障 richText = true)
        /// </summary>
        private void DrawRichInfoRow(string richContent, GUIStyle customStyle = null) {
            GUIStyle s = customStyle ?? labelValueStyle;
            if (!s.richText) s.richText = true;
            GUILayout.Label(richContent, s);
        }

        /// <summary>
        /// 绘制标准化分栏式航天 HUD 键值对行 (Label + RichValue)
        /// </summary>
        private void DrawRichKeyValueRow(string labelText, string richValueText, float labelWidth = 110f) {
            GUILayout.BeginHorizontal();
            if (!labelValueStyle.richText) labelValueStyle.richText = true;
            GUILayout.Label(labelText, labelValueStyle, GUILayout.Width(labelWidth));
            GUILayout.Label(richValueText, labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // =========================================================================
        // 9. MGA 统一交互 GUI 控件托管族 (MGA Unified GUI Widgets Suite)
        // =========================================================================

        /// <summary>
        /// MGA 出发轨道模式选择器 (模式 1: 发射前规划 vs 模式 2: 已在轨转移)
        /// 统一由 Widgets.cs 托管，提供标准化样式、数值约束与飞船同步交互
        /// </summary>
        private void DrawMGADepartureModeSelector(MGAConfig cfg, Action onSyncDepartureOrbit) {
            if (cfg == null) return;

            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            bool isMode1 = (cfg.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit);
            bool isMode2 = (cfg.DepartureMode == MGADepartureMode.InOrbitLockedOrbit);

            if (GUILayout.Button(isMode1 ? "<color=#38bdf8><b>🚀 模式 1: 发射前规划 (未发射/自由倾角LAN)</b></color>" : "🚀 模式 1: 发射前规划", isMode1 ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
                cfg.DepartureMode = MGADepartureMode.PreLaunchFreeOrbit;
                cfg.LockDepartureInclination = false;
                cfg.LockDepartureRAAN = false;
            }
            if (GUILayout.Button(isMode2 ? "<color=#38bdf8><b>🛰️ 模式 2: 已在轨转移 (已入轨/当前轨道锁死)</b></color>" : "🛰️ 模式 2: 已在轨转移", isMode2 ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
                cfg.DepartureMode = MGADepartureMode.InOrbitLockedOrbit;
                cfg.LockDepartureInclination = true;
                cfg.LockDepartureRAAN = true;
                onSyncDepartureOrbit?.Invoke();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            if (isMode1) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#34d399>💡 模式 1 [发射前规划]：火箭尚未发射，倾角与升交点经度(LAN)不锁死，算法将自由寻找最佳转移窗口，并推荐理论最优发射方位与轨道面。</color>", labelValueStyle);
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_dep_park"), labelValueStyle, GUILayout.Width(70));
                GUILayout.Label("<color=#94a3b8>PeA:</color>", labelValueStyle, GUILayout.Width(30));
                string depPeStr = GUILayout.TextField(cfg.DeparturePeAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));
                if (double.TryParse(depPeStr, out double dpe)) cfg.DeparturePeAKm = dpe;

                GUILayout.Label("<color=#94a3b8>ApA:</color>", labelValueStyle, GUILayout.Width(30));
                string depApStr = GUILayout.TextField(cfg.DepartureApAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));
                if (double.TryParse(depApStr, out double dap)) cfg.DepartureApAKm = dap;
                GUILayout.Label("<color=#94a3b8>km</color>", labelValueStyle, GUILayout.Width(25));

                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#94a3b8>倾角与LAN:</color> <color=#34d399>【自由推导】无需锁死</color>", labelValueStyle);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            } else {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8>🔒 模式 2 [已在轨转移]：根据飞船当前实际轨道参数 (高度/倾角/升交点LAN/近地点幅角AOP) 严格锁死寻找转移。</color>", labelValueStyle, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("↻ 读飞船锁死", actionButtonStyle, GUILayout.Height(18), GUILayout.Width(90))) {
                    onSyncDepartureOrbit?.Invoke();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_dep_park"), labelValueStyle, GUILayout.Width(70));
                GUILayout.Label("<color=#94a3b8>PeA:</color>", labelValueStyle, GUILayout.Width(30));
                string depPeStr2 = GUILayout.TextField(cfg.DeparturePeAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));
                if (double.TryParse(depPeStr2, out double dpe2)) cfg.DeparturePeAKm = dpe2;

                GUILayout.Label("<color=#94a3b8>ApA:</color>", labelValueStyle, GUILayout.Width(30));
                string depApStr2 = GUILayout.TextField(cfg.DepartureApAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));
                if (double.TryParse(depApStr2, out double dap2)) cfg.DepartureApAKm = dap2;
                GUILayout.Label("<color=#94a3b8>km</color>", labelValueStyle, GUILayout.Width(25));

                GUILayout.Label("<color=#94a3b8>Inc:</color>", labelValueStyle, GUILayout.Width(25));
                string depIncStr = GUILayout.TextField(cfg.DepartureInclinationDeg.ToString("F1"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(40), GUILayout.Height(20));
                if (double.TryParse(depIncStr, out double di)) cfg.DepartureInclinationDeg = Mathf.Clamp((float)di, 0f, 90f);
                GUILayout.Label("<color=#38bdf8>°🔒</color>", labelValueStyle, GUILayout.Width(25));

                GUILayout.Label("<color=#94a3b8>LAN:</color>", labelValueStyle, GUILayout.Width(30));
                string depRaanStr = GUILayout.TextField(cfg.DepartureRAANDeg.ToString("F1"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));
                if (double.TryParse(depRaanStr, out double dr)) cfg.DepartureRAANDeg = (dr % 360.0 + 360.0) % 360.0;
                GUILayout.Label("<color=#38bdf8>°🔒</color>", labelValueStyle, GUILayout.Width(25));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#94a3b8>AOP:</color> {cfg.DepartureAOPDeg:F1}°", labelValueStyle, GUILayout.Width(95));
                GUILayout.Label($"<color=#94a3b8>Ecc:</color> {cfg.DepartureEccentricity:F4}", labelValueStyle, GUILayout.Width(95));
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#94a3b8>快捷预设:</color>", labelValueStyle, GUILayout.Width(60));
                if (GUILayout.Button("28.6°(KSC)", secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(75))) {
                    cfg.DepartureInclinationDeg = 28.6;
                }
                if (GUILayout.Button("0°(赤道)", secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(65))) {
                    cfg.DepartureInclinationDeg = 0.0;
                }
                if (GUILayout.Button("51.6°(拜科努尔)", secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(95))) {
                    cfg.DepartureInclinationDeg = 51.6;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }

        /// <summary>
        /// 星历数据源方案与 JPL SPICE DE440 精度对比卡片组件 (统一由 Widgets.cs 托管)
        /// </summary>
        private void DrawEphemerisSchemeAndComparisonUI() {
            var curScheme = SolarSystemCatalog.CurrentScheme;
            bool isScheme2 = (curScheme == EphemerisScheme.Scheme2_FlightGlobals);

            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.BeginHorizontal();

            string statusIcon = isScheme2 ? "🟢" : "🔵";
            string statusTitle = isScheme2 
                ? I18n.T("ephem_short_s2", "[方案 2] 游戏运行态直读") 
                : I18n.T("ephem_short_s1", "[方案 1] SPICE DE440 内核");
            GUILayout.Label($"{statusIcon} {statusTitle}", labelValueStyle);
            GUILayout.FlexibleSpace();

            string toggleBtnText = showSchemeComparisonPanel 
                ? I18n.T("mga_hide_ephem_comp", "▲ 收起核验") 
                : I18n.T("mga_show_ephem_comp", "▼ 精度核验");

            if (GUILayout.Button(toggleBtnText, secondaryButtonStyle, GUILayout.Width(100), GUILayout.Height(20))) {
                showSchemeComparisonPanel = !showSchemeComparisonPanel;
                if (showSchemeComparisonPanel) {
                    RefreshSchemeComparison();
                }
            }
            GUILayout.EndHorizontal();

            if (showSchemeComparisonPanel) {
                GUILayout.Space(2);
                string subInfo = isScheme2
                    ? I18n.T("ephem_scheme2_info", "首选模式: 直读游戏天体物理与轨道 (支持小行星/系外天体包)。缺失时平滑回退方案 1。")
                    : I18n.T("ephem_scheme1_info", "回退模式: 游戏天体未载入，通过底层 NASA JPL SPICE DE440 切比雪夫反算。");
                GUILayout.Label(subInfo, subHeaderStyle);
                GUILayout.Space(2);

                if (string.IsNullOrEmpty(schemeComparisonBody)) {
                    schemeComparisonBody = mgaConfig.DepartureBody;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_comp_target", "核验目标:"), labelValueStyle, GUILayout.Width(65));
                var compPlanet = SolarSystemCatalog.GetPlanet(schemeComparisonBody);
                if (GUILayout.Button(I18n.Format("mga_planet_btn_short", compPlanet.Icon, compPlanet.GetDisplayName(), compPlanet.Name), secondaryButtonStyle, GUILayout.Height(20))) {
                    schemeComparisonBody = (schemeComparisonBody.Equals(mgaConfig.DepartureBody, StringComparison.OrdinalIgnoreCase)) 
                        ? mgaConfig.DestinationBody 
                        : mgaConfig.DepartureBody;
                    RefreshSchemeComparison();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("mga_btn_refresh", "刷新对比"), secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
                    RefreshSchemeComparison();
                }
                GUILayout.EndHorizontal();

                if (currentSchemeComparison != null) {
                    GUILayout.Space(2);
                    string posText = currentSchemeComparison.PosDiffKm < 1000.0 ? $"{currentSchemeComparison.PosDiffKm:F2} km" : $"{currentSchemeComparison.PosDiffKm:N0} km";
                    string velText = currentSchemeComparison.VelDiffMs < 1000.0 ? $"{currentSchemeComparison.VelDiffMs:F2} m/s" : $"{currentSchemeComparison.VelDiffMs:N0} m/s";

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"位置误差 Δr: {posText}", labelValueStyle);
                    GUILayout.Label($"速度误差 Δv: {velText}", labelValueStyle);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    string daText = currentSchemeComparison.DaKm < 1000.0 ? $"{currentSchemeComparison.DaKm:F1} km" : $"{currentSchemeComparison.DaKm:N0} km";
                    GUILayout.Label($"Δa: {daText}", subHeaderStyle);
                    GUILayout.Label($"Δe: {currentSchemeComparison.De:F5}", subHeaderStyle);
                    GUILayout.Label($"Δi: {currentSchemeComparison.DincDeg:F3}°", subHeaderStyle);
                    GUILayout.EndHorizontal();

                    if (currentSchemeComparison.Scheme1Available && currentSchemeComparison.Scheme2Available) {
                        GUILayout.Label("✅ 方案 1 与 方案 2 坐标系与星历高精锁相对齐 (100% C++ 原生计算)", subHeaderStyle);
                    } else if (!currentSchemeComparison.Scheme1Available) {
                        GUILayout.Label("ℹ️ 方案 1 (SPICE DE440) 未载入，显示单向参数", subHeaderStyle);
                    } else if (!currentSchemeComparison.Scheme2Available) {
                        GUILayout.Label("ℹ️ 方案 2 暂无该天体实例 (如在非飞行场景)", subHeaderStyle);
                    }
                }
            }

            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        /// <summary>
        /// 统一标准化 MGA 候选求解方案卡片列表组件：
        /// 将所有收敛解按总 ΔV 升序排列，最优解置顶显示，助推序列完整排布，支持一键切换并与 SSOT 全局状态同步
        /// </summary>
        public void DrawMGASolutionTable(List<MGASolution> solutions, MGASolution activeSol, Action<MGASolution> onSelect) {
            if (solutions == null || solutions.Count == 0) return;

            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.T("mga_sol_table_title", "🏆 候选转移方案决策池")}</b> <color=#94a3b8>(共 {solutions.Count} 解，总 ΔV 最优置顶)</color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            // 表头栏
            GUILayout.BeginHorizontal(nestedCardBoxStyle);
            GUILayout.Label("标号 / 助推链", subHeaderStyle, GUILayout.Width(175));
            GUILayout.Label("发射窗口 (UTC)", subHeaderStyle, GUILayout.Width(110));
            GUILayout.Label("历时", subHeaderStyle, GUILayout.Width(45));
            GUILayout.Label("出射 ΔV", subHeaderStyle, GUILayout.Width(58));
            GUILayout.Label("捕获 ΔV", subHeaderStyle, GUILayout.Width(58));
            GUILayout.Label("总机动需求", subHeaderStyle, GUILayout.Width(68));
            GUILayout.FlexibleSpace();
            GUILayout.Label("详情", subHeaderStyle, GUILayout.Width(28));
            GUILayout.Label("操作", subHeaderStyle, GUILayout.Width(50));
            GUILayout.EndHorizontal();

            // 方案数据行 (按总 ΔV 严格升序排列，最优解置顶，最多展示 10 项，支持滚动)
            var sortedSolutions = solutions.OrderBy(s => s.TotalDeltaVKms).ToList();
            int displayCount = Math.Min(10, sortedSolutions.Count);
            var minDvSol = sortedSolutions.FirstOrDefault();
            var fastSol = sortedSolutions.OrderBy(s => s.TotalFlightDays).FirstOrDefault();

            mgaSolutionScroll = GUILayout.BeginScrollView(mgaSolutionScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.MaxHeight(255));

            for (int i = 0; i < displayCount; i++) {
                var sol = sortedSolutions[i];
                bool isSelected = (sol == activeSol);
                bool isMin = (sol == minDvSol);
                bool isFast = (sol == fastSol && !isMin);

                GUILayout.BeginHorizontal(isSelected ? nestedCardBoxStyle : GUI.skin.box);

                // 列 1: 徽章与完整/紧凑序列
                string badge = isMin ? "<color=#f59e0b><b>🏆</b></color> " : (isFast ? "<color=#38bdf8><b>⚡</b></color> " : $"<color=#94a3b8>#{i + 1}</color> ");
                string rawChain = !string.IsNullOrEmpty(sol.FlybyChainSummary) ? sol.FlybyChainSummary : $"{mgaConfig?.DepartureBody} ➔ {mgaConfig?.DestinationBody}";
                string displayChain = FormatSequenceDisplay(rawChain);
                GUIContent chainContent = new GUIContent($"{badge}{displayChain}", rawChain);
                GUILayout.Label(chainContent, labelValueStyle, GUILayout.Width(175));

                // 列 2: 发射历元
                string dateStr = GMATClient.FormatUtcGregorian(sol.DepartureUT);
                GUILayout.Label(dateStr, labelValueStyle, GUILayout.Width(110));

                // 列 3: 飞行天数
                string daysStr = sol.TotalFlightDays >= 365.25 ? $"{sol.TotalFlightDays / 365.25:F1}y" : $"{sol.TotalFlightDays:F0}d";
                GUILayout.Label(daysStr, labelValueStyle, GUILayout.Width(45));

                // 列 4: 出射 ΔV
                GUILayout.Label($"{sol.LaunchDeltaVKms * 1000.0:F0}m/s", labelValueStyle, GUILayout.Width(58));

                // 列 5: 捕获 ΔV
                GUILayout.Label($"{sol.InsertionDeltaVKms * 1000.0:F0}m/s", labelValueStyle, GUILayout.Width(58));

                // 列 6: 总 ΔV
                string totalColor = isMin ? "#10b981" : "#f59e0b";
                GUILayout.Label($"<color={totalColor}><b>{sol.TotalDeltaVKms * 1000.0:F0}</b></color>", labelValueStyle, GUILayout.Width(68));

                GUILayout.FlexibleSpace();

                // 列 7: 行内详情查询按钮 [🔍]
                if (GUILayout.Button(new GUIContent("🔍", "查看航段全量动力学明细与目标捕获参数"), secondaryButtonStyle, GUILayout.Width(28), GUILayout.Height(20))) {
                    detailModalSolution = sol;
                    showSolutionDetailModal = true;
                }

                GUILayout.Space(2);

                // 列 8: 选用按钮
                if (isSelected) {
                    GUILayout.Label("<color=#38bdf8>✓已选</color>", subHeaderStyle, GUILayout.Width(50));
                } else {
                    if (GUILayout.Button(I18n.T("btn_select_sol", "选用"), actionButtonStyle, GUILayout.Width(50), GUILayout.Height(20))) {
                        onSelect?.Invoke(sol);
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static string FormatSequenceDisplay(string rawChain) {
            if (string.IsNullOrEmpty(rawChain)) return "";
            var parts = rawChain.Split(new[] { "->", "➔", "  ->  " }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (parts.Count == 0) return rawChain;
            if (parts.Count <= 3) {
                return string.Join(" ➔ ", parts);
            }
            // 4 个及以上节点时动态读取天体标准物理代号 (Icon) 进行紧凑排版，零硬编码
            var tokens = parts.Select(p => {
                var planet = SolarSystemCatalog.GetPlanet(p);
                if (planet != null && !string.IsNullOrEmpty(planet.Icon)) {
                    return planet.Icon;
                }
                return p.Length > 3 ? p.Substring(0, 3) : p;
            });
            return string.Join(" ➔ ", tokens);
        }

        // =========================================================================
        // 7. 高级航段动力学遥测与全量物理真值富文本展示 (Rich-Text Astrodynamic Telemetry)
        // =========================================================================
        public void DrawRichTextTelemetryCard(MGAMissionStep step, int stepIndex, MGASolution solution) {
            if (step == null) return;

            GUILayout.BeginVertical(nestedCardBoxStyle);

            // 头部标题与徽章
            GUILayout.BeginHorizontal();
            string typeBadge = step.type switch {
                MGAStepType.Departure => "<color=#38bdf8><b>[始发脱离]</b></color>",
                MGAStepType.Transfer => "<color=#6366f1><b>[转移巡航]</b></color>",
                MGAStepType.Maneuver => "<color=#f59e0b><b>[深空机动]</b></color>",
                MGAStepType.Flyby => "<color=#c084fc><b>[行星助推]</b></color>",
                MGAStepType.Arrival => "<color=#34d399><b>[目标交会]</b></color>",
                _ => "<color=#94a3b8><b>[任务步骤]</b></color>"
            };
            GUILayout.Label($"{typeBadge} <color=#f8fafc><b>#{stepIndex + 1}: {step.label}</b></color>", sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            if (step.is_solved) {
                GUILayout.Label("<color=#34d399><b>✓ 动力学已收敛</b></color>", labelValueStyle);
            } else {
                GUILayout.Label("<color=#94a3b8>未单独求解</color>", labelValueStyle);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 1. 天体引力域与物理空间几何
            var planet = SolarSystemCatalog.GetPlanet(step.body_name);
            double rKm = planet != null ? planet.RadiusKm : 6000.0;
            double atmKm = planet != null ? planet.AtmosphereDepthKm : 0.0;
            double soiKm = planet != null ? planet.SoiKm : 1e6;
            MGADefaults.GetBodyCaptureAltitudeBounds(step.body_name, out double minPeA, out double recPeA, out double maxApA);

            GUILayout.Label($"<b><color=#38bdf8>🪐 {I18n.T("mga_telemetry_geometry", "天体引力与环境物理参数")}</color></b>", labelValueStyle);
            GUILayout.BeginHorizontal(cardBoxStyle);
            GUILayout.BeginVertical();
            GUILayout.Label($"天体名称: <color=#f8fafc><b>{planet?.GetDisplayName() ?? step.body_name}</b></color>", labelValueStyle);
            GUILayout.Label($"母星系统: <color=#94a3b8>{planet?.ParentName ?? "Sun"}</color>", labelValueStyle);
            GUILayout.Label($"引力常数 μ: <color=#facc15><b>{(planet?.Mu ?? 398600.4):F1} km³/s²</b></color>", labelValueStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label($"天体物理半径: <color=#94a3b8>{rKm:F1} km</color>", labelValueStyle);
            string atmText = atmKm > 0 ? $"<color=#f87171><b>{atmKm:F1} km</b></color>" : "<color=#34d399>无大气真空</color>";
            GUILayout.Label($"大气层顶界: {atmText}", labelValueStyle);
            string soiText = (soiKm > 0 && !double.IsInfinity(soiKm)) ? $"<color=#38bdf8><b>{soiKm:F0} km</b></color>" : "<color=#94a3b8>无限域</color>";
            GUILayout.Label($"引力影响球 (SOI): {soiText}", labelValueStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label($"避气安全下限: <color=#facc15>≥ {minPeA:F0} km</color>", labelValueStyle);
            GUILayout.Label($"推荐科学停泊: <color=#34d399>~ {recPeA:F0} km</color>", labelValueStyle);
            GUILayout.Label($"逃逸引力上限: <color=#f87171>≤ {maxApA:F0} km</color>", labelValueStyle);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(3);

            // 2. 历元与时间线
            GUILayout.Label($"<b><color=#38bdf8>⏱️ {I18n.T("mga_telemetry_timing", "历元时刻与时序规划")}</color></b>", labelValueStyle);
            GUILayout.BeginHorizontal(cardBoxStyle);
            string utcStr = string.IsNullOrEmpty(step.epoch_utc_str) ? GMATClient.FormatUtcGregorian(step.epoch_ut) : step.epoch_utc_str;
            GUILayout.Label($"历元 UTC: <color=#34d399><b>{utcStr}</b></color>", labelValueStyle, GUILayout.Width(230));
            GUILayout.Label($"起算 UT: <color=#94a3b8>{step.epoch_ut:F3} s</color>", labelValueStyle, GUILayout.Width(170));
            if (step.type == MGAStepType.Transfer) {
                GUILayout.Label($"飞行耗时 (ToF): <color=#facc15><b>{step.tof_days:F2} 天</b></color> <color=#94a3b8>({step.tof_days * 86400.0:F0} s)</color>", labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(3);

            // 3. 推进机动与冲量矢量
            GUILayout.Label($"<b><color=#38bdf8>🚀 {I18n.T("mga_telemetry_maneuver", "推进机动与冲量矢量 (VNC / Frenet)")}</color></b>", labelValueStyle);
            GUILayout.BeginHorizontal(cardBoxStyle);
            GUILayout.BeginVertical();
            GUILayout.Label($"净冲量需求 ΔV: <color=#34d399><b>{step.solved_dv_ms:F1} m/s</b></color>", labelValueStyle);
            if (step.solved_c3_kms2 > 0.001) {
                GUILayout.Label($"发射特征能量 C3: <color=#38bdf8><b>{step.solved_c3_kms2:F2} km²/s²</b></color>", labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label($"顺向切向 (Prograde Δv_V): <color={(step.dv_v >= 0 ? "#34d399" : "#f87171")}><b>{step.dv_v:F2} m/s</b></color>", labelValueStyle);
            GUILayout.Label($"轨道法向 (Normal Δv_N): <color={(Math.Abs(step.dv_n) < 0.1 ? "#94a3b8" : "#38bdf8")}><b>{step.dv_n:F2} m/s</b></color>", labelValueStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label($"径向垂向 (Radial Δv_R): <color={(Math.Abs(step.dv_b) < 0.1 ? "#94a3b8" : "#facc15")}><b>{step.dv_b:F2} m/s</b></color>", labelValueStyle);
            if (step.dsm_eta > 0) {
                GUILayout.Label($"深空机动时段比 η: <color=#38bdf8><b>{step.dsm_eta:F3}</b></color>", labelValueStyle);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // 4. 针对特定步骤展示 B 平面穿靶或借力参数
            if (step.type == MGAStepType.Flyby || Math.Abs(step.target_b_dot_t_km) > 0.1 || Math.Abs(step.target_b_dot_r_km) > 0.1) {
                GUILayout.Space(3);
                GUILayout.Label($"<b><color=#38bdf8>🎯 {I18n.T("mga_telemetry_flyby", "借力几何与 B 平面穿靶遥测")}</color></b>", labelValueStyle);
                GUILayout.BeginHorizontal(cardBoxStyle);
                GUILayout.Label($"穿靶矢量 B·T: <color=#38bdf8><b>{step.target_b_dot_t_km:F1} km</b></color>", labelValueStyle, GUILayout.Width(170));
                GUILayout.Label($"穿靶矢量 B·R: <color=#38bdf8><b>{step.target_b_dot_r_km:F1} km</b></color>", labelValueStyle, GUILayout.Width(170));
                double bMag = Math.Sqrt(step.target_b_dot_t_km * step.target_b_dot_t_km + step.target_b_dot_r_km * step.target_b_dot_r_km);
                GUILayout.Label($"脱靶模值 |B|: <color={(bMag < 5000 ? "#34d399" : "#facc15")}><b>{bMag:F1} km</b></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }
    }
}
