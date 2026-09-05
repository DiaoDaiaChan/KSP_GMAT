using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;

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
        /// <param name="stepIndex">当前时序节点索引 (用于补全回填模型)</param>
        /// <returns>当前文本框中的最新字符串内容</returns>
        private string DrawDelayedSmartField(string controlId, string rawValue, double fallbackNumericValue, GUIStyle style, GUILayoutOption widthOpt, string intelliSenseContextMode = null, int stepIndex = -1) {
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
                if (!string.IsNullOrEmpty(intelliSenseContextMode) && stepIndex >= 0) {
                    TriggerIntelliSense(newStr, intelliSenseContextMode, stepIndex);
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
        /// </summary>
        /// <param name="controlId">输入框唯一标识符</param>
        /// <param name="currentText">当前绑定的字符串</param>
        /// <param name="style">GUIStyle 样式</param>
        /// <param name="widthOpt">GUILayout 宽度布局选项</param>
        /// <returns>最新编辑的字符串</returns>
        private static string DrawDelayedTextField(string controlId, string currentText, GUIStyle style, GUILayoutOption widthOpt) {
            GUI.SetNextControlName(controlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == controlId);

            string displayStr;
            if (!inputStringCache.TryGetValue(controlId, out displayStr)) {
                displayStr = currentText ?? "";
                inputStringCache[controlId] = displayStr;
            }

            if (!isFocused) {
                displayStr = currentText ?? "";
                inputStringCache[controlId] = displayStr;
            }

            string newStr = GUILayout.TextField(displayStr, style ?? GUI.skin.textField, widthOpt, GUILayout.Height(20));
            if (newStr != displayStr) {
                inputStringCache[controlId] = newStr;
                return newStr;
            }
            return displayStr;
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

            GUIStyle centerStyle = new GUIStyle(labelValueStyle) {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            string displayPercent = $"{(pClamped * 100f):F0}%";
            string fullBarLabel = string.IsNullOrEmpty(labelText) ? displayPercent : $"{labelText}  ({displayPercent})";
            GUI.Label(barRect, $"<b>{fullBarLabel}</b>", centerStyle);
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
        /// 唤起 GMAT IntelliSense 智能补全查询
        /// </summary>
        /// <param name="query">用户当前输入的文本前缀</param>
        /// <param name="fieldName">目标字段类型 ("achieve", "vary", "prop_stop", "prop_val", "fburn_var")</param>
        /// <param name="stepIndex">目标时序步骤索引</param>
        private void TriggerIntelliSense(string query, string fieldName, int stepIndex) {
            if (intelliSenseDismissed && intelliSenseLastQuery == query) return;
            intelliSenseTargetStepIndex = stepIndex;
            intelliSenseFieldName = fieldName;
            intelliSenseLastQuery = query;
            intelliSenseItems = GMATParameterRegistry.QueryIntelliSense(query, FlightGlobals.ActiveVessel, fieldName);
            intelliSenseSelectedIndex = 0;
            intelliSenseOpen = (intelliSenseItems != null && intelliSenseItems.Count > 0);
        }

        /// <summary>
        /// 将选中的 IntelliSense 补全项应用并回填到目标时序节点与数据模型中
        /// </summary>
        /// <param name="item">被选中的补全数据项</param>
        private void ApplyIntelliSense(IntelliSenseItem item) {
            if (item == null) return;
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
        /// 带左侧文本标签的延迟浮点数值输入表单行
        /// </summary>
        private double DrawDoubleField(string label, double val, float labelW = 110, float fieldW = 90, string format = "auto", string customId = null) {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label)) GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            
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
        /// 带左侧文本标签的字符串文本输入表单行
        /// </summary>
        private string DrawTextField(string label, string val, float labelW = 110, float fieldW = 120) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            string txt = GUILayout.TextField(val ?? "", stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW));
            GUILayout.EndHorizontal();
            return txt;
        }

        /// <summary>
        /// 带左侧文本标签的整型数值输入表单行
        /// </summary>
        private int DrawIntField(string label, int val, float labelW = 110, float fieldW = 60) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            string txt = GUILayout.TextField(val.ToString(), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW));
            int res;
            if (int.TryParse(txt, out res)) {
                val = res;
            }
            GUILayout.EndHorizontal();
            return val;
        }

        // =========================================================================
        // 5. 脚本与任务时序复用组件 (Script & Mission Sequence Reusable Widgets)
        // =========================================================================

        /// <summary>
        /// 单步 GMAT 原生脚本透视展示组件：就地渲染暗黑高亮语法代码块
        /// </summary>
        /// <param name="step">任务步骤</param>
        private void DrawStepCodePeekBox(MissionStep step) {
            if (step == null || !step.show_code_peek) return;
            GUILayout.Space(2);
            GUILayout.BeginVertical(nestedCardBoxStyle);
            string peekCode = GMATScriptGenerator.GenerateStepScript(step);
            string coloredPeek = ColorizeGmatSyntax(peekCode);
            GUILayout.Label(coloredPeek, codeBlockStyle);
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
    }
}
