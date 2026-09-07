using System;
using System.Collections.Generic;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.UI {
    /// <summary>
    /// GMAT 官方原生 API 与全量动态参数全景速查字典弹窗
    /// 提供 310 个物理参数、323 个官方类、6,355 个原生源码方法及 222 个扩展函数的毫秒级多维检索与源码查看
    /// 严格遵循 baseline_agent.md 规范 (深空暗黑主题、零白屏闪烁、全量 i18n、响应式双栏布局)
    /// </summary>
    public partial class GMATMainWindow {
        // Modal State
        private bool showApiInspectorModal = false;
        private Rect apiInspectorWindowRect;
        private string inspectorSearchQuery = "";
        private string inspectorSelectedTypeFilter = "ALL"; // ALL, PARAMS, CLASSES, FUNCTIONS
        private string inspectorSelectedFamily = "ALL";
        private GMATSchemaRegistry.CapabilitySearchResult inspectorSelectedItem = null;
        private Vector2 inspectorListScroll = Vector2.zero;
        private Vector2 inspectorDetailScroll = Vector2.zero;

        // Search cache
        private List<GMATSchemaRegistry.CapabilitySearchResult> cachedSearchResults = null;
        private string lastSearchQuery = null;
        private string lastTypeFilter = null;
        private string lastFamilyFilter = null;

        // Toast feedback
        private string inspectorToastMessage = "";
        private float inspectorToastTimer = 0f;
        private GUIStyle cachedInspectorBadgeStyle = null;

        /// <summary>
        /// 打开 API & 全量参数全景字典
        /// </summary>
        public void OpenApiInspector(string initialQuery = "", string initialType = "ALL") {
            inspectorSearchQuery = initialQuery ?? "";
            inspectorSelectedTypeFilter = initialType ?? "ALL";
            inspectorSelectedFamily = "ALL";

            float screenW = Screen.width > 0 ? Screen.width : 1920f;
            float screenH = Screen.height > 0 ? Screen.height : 1080f;
            float initW = Mathf.Clamp(screenW * 0.76f, 960f, 1280f);
            float initH = Mathf.Clamp(screenH * 0.82f, 620f, 880f);
            apiInspectorWindowRect = new Rect((screenW - initW) * 0.5f, (screenH - initH) * 0.5f, initW, initH);

            cachedSearchResults = null;
            showApiInspectorModal = true;
        }

        /// <summary>
        /// 绘制 API & 全量参数全景速查字典弹窗
        /// </summary>
        private void DrawApiInspectorWindow(int windowID) {
            // Update Toast Timer
            if (inspectorToastTimer > 0f) {
                inspectorToastTimer -= Time.unscaledDeltaTime;
                if (inspectorToastTimer <= 0f) {
                    inspectorToastMessage = "";
                }
            }

            var reg = GMATSchemaRegistry.Instance;

            // 1. Title Bar & Overview Stats
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>{I18n.T("modal_api_inspector_title")}</b></color>", headerStyle);
            GUILayout.Space(12);

            int totalExtFuncs = reg.TopFunctionCount + reg.UserFunctionCount + reg.UtilityFunctionCount;
            string statText = I18n.Format("inspector_stat_badge",
                reg.ParameterCount,
                reg.ApiClassCount,
                reg.ApiMethodCount,
                totalExtFuncs);
            GUILayout.Label($"<color=#94a3b8>{statText}</color>", labelValueStyle);

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(inspectorToastMessage)) {
                GUILayout.Label($"<color=#34d399><b>{inspectorToastMessage}</b></color>", labelValueStyle);
                GUILayout.Space(10);
            }

            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(26), GUILayout.Height(22))) {
                showApiInspectorModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 2. Search Box & Quick Type Filter Bar
            GUILayout.BeginHorizontal(cardBoxStyle);
            
            GUILayout.Label("🔍", labelValueStyle, GUILayout.Width(20));
            GUI.SetNextControlName("InspectorSearchField");
            string newQuery = GUILayout.TextField(inspectorSearchQuery, GUI.skin.textField, GUILayout.MinWidth(240), GUILayout.Height(22));
            if (newQuery != inspectorSearchQuery) {
                inspectorSearchQuery = newQuery;
            }

            if (GUILayout.Button(I18n.T("btn_clear"), secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(22))) {
                inspectorSearchQuery = "";
            }

            GUILayout.Space(12);

            // Category tabs
            string tabAllLabel = I18n.Format("inspector_tab_all", reg.ParameterCount + reg.ApiClassCount + reg.ApiMethodCount + totalExtFuncs);
            string tabParamsLabel = I18n.Format("inspector_tab_params", reg.ParameterCount);
            string tabClassesLabel = I18n.Format("inspector_tab_classes", reg.ApiClassCount);
            string tabFuncsLabel = I18n.Format("inspector_tab_functions", reg.ApiMethodCount + totalExtFuncs);

            DrawFilterTab("ALL", tabAllLabel);
            DrawFilterTab("PARAMS", tabParamsLabel);
            DrawFilterTab("CLASSES", tabClassesLabel);
            DrawFilterTab("FUNCTIONS", tabFuncsLabel);

            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 3. Class Family Sub-Filter (shown when CLASSES or ALL is active)
            if (inspectorSelectedTypeFilter == "CLASSES" || inspectorSelectedTypeFilter == "ALL") {
                var families = reg.GetClassFamilies();
                if (families != null && families.Count > 0) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("inspector_label_category", ""), labelValueStyle, GUILayout.Width(75));

                    bool isAllFam = (inspectorSelectedFamily == "ALL");
                    if (GUILayout.Button(I18n.T("inspector_filter_all_families"), isAllFam ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                        inspectorSelectedFamily = "ALL";
                    }

                    for (int f = 0; f < families.Count && f < 8; f++) {
                        string fam = families[f];
                        bool isCur = (inspectorSelectedFamily == fam);
                        if (GUILayout.Button(fam, isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            inspectorSelectedFamily = fam;
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
            }

            // Cache check
            if (cachedSearchResults == null ||
                inspectorSearchQuery != lastSearchQuery ||
                inspectorSelectedTypeFilter != lastTypeFilter ||
                inspectorSelectedFamily != lastFamilyFilter) {
                
                string filterParam = "all";
                if (inspectorSelectedTypeFilter == "PARAMS") filterParam = "params";
                else if (inspectorSelectedTypeFilter == "FUNCTIONS") filterParam = "funcs";
                else if (inspectorSelectedTypeFilter == "CLASSES") {
                    filterParam = (inspectorSelectedFamily != "ALL" && !string.IsNullOrEmpty(inspectorSelectedFamily))
                        ? "family_" + inspectorSelectedFamily
                        : "classes";
                }

                cachedSearchResults = reg.SearchCapabilities(
                    inspectorSearchQuery,
                    filterParam,
                    250
                );
                lastSearchQuery = inspectorSearchQuery;
                lastTypeFilter = inspectorSelectedTypeFilter;
                lastFamilyFilter = inspectorSelectedFamily;

                if (inspectorSelectedItem != null && !cachedSearchResults.Contains(inspectorSelectedItem)) {
                    inspectorSelectedItem = cachedSearchResults.Find(x => x.title == inspectorSelectedItem.title);
                }
                if (inspectorSelectedItem == null && cachedSearchResults.Count > 0) {
                    inspectorSelectedItem = cachedSearchResults[0];
                }
            }

            // 4. Main Two-Column View: Left List (380px) | Right Details
            GUILayout.BeginHorizontal();

            // -------------------------------------------------------------
            // LEFT COLUMN: Search Results & Card List (Fixed Width: 380px)
            // -------------------------------------------------------------
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(380));
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#7dd3fc><b>{I18n.Format("inspector_results_count", cachedSearchResults.Count)}</b></color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            inspectorListScroll = GUILayout.BeginScrollView(inspectorListScroll);

            if (cachedSearchResults.Count == 0) {
                GUILayout.Space(30);
                GUILayout.Label($"<color=#94a3b8>{I18n.T("inspector_no_results")}</color>", gridLabelStyle);
            } else {
                for (int i = 0; i < cachedSearchResults.Count; i++) {
                    var item = cachedSearchResults[i];
                    bool isSelected = (inspectorSelectedItem == item);

                    GUIStyle cardStyle = isSelected ? tabButtonActiveStyle : secondaryButtonStyle;

                    GUILayout.BeginVertical(cardStyle);
                    GUILayout.BeginHorizontal();

                    // Type Badge
                    DrawTypeBadge(item.itemType);
                    GUILayout.Space(4);

                    // Name
                    string nameColor = isSelected ? "#ffffff" : "#e2e8f0";
                    GUILayout.Label($"<color={nameColor}><b>{item.title}</b></color>", labelValueStyle);

                    GUILayout.FlexibleSpace();

                    // Signature / Unit
                    if (!string.IsNullOrEmpty(item.signature)) {
                        GUILayout.Label($"<color=#38bdf8>{item.signature}</color>", labelValueStyle);
                    }
                    GUILayout.EndHorizontal();

                    // Sub-line: Category / Subtitle
                    if (!string.IsNullOrEmpty(item.subtitle)) {
                        GUILayout.Label($"<color=#64748b>{item.subtitle}</color>", gridLabelStyle);
                    }

                    GUILayout.EndVertical();

                    // Handle Click Selection
                    if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) {
                        inspectorSelectedItem = item;
                        inspectorDetailScroll = Vector2.zero;
                        Event.current.Use();
                    }

                    GUILayout.Space(2);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // -------------------------------------------------------------
            // RIGHT COLUMN: Detail View & Native Code Block (Flexible Width)
            // -------------------------------------------------------------
            GUILayout.BeginVertical(nestedCardBoxStyle);

            if (inspectorSelectedItem == null) {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=#64748b><b>{I18n.T("inspector_select_hint")}</b></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            } else {
                var cur = inspectorSelectedItem;

                // Header Card
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                
                DrawTypeBadge(cur.itemType);
                GUILayout.Space(6);
                GUILayout.Label($"<color=#f8fafc><size=15><b>{cur.title}</b></size></color>", labelValueStyle);
                GUILayout.FlexibleSpace();

                // Quick Action Buttons
                if (GUILayout.Button(I18n.T("inspector_btn_copy_name"), secondaryButtonStyle, GUILayout.Height(22))) {
                    GUIUtility.systemCopyBuffer = cur.insertSnippet ?? cur.title;
                    ShowInspectorToast(I18n.T("inspector_copied_toast"));
                }

                if (!string.IsNullOrEmpty(cur.codeImpl)) {
                    if (GUILayout.Button(I18n.T("inspector_btn_copy_impl"), actionButtonStyle, GUILayout.Height(22))) {
                        GUIUtility.systemCopyBuffer = cur.codeImpl;
                        ShowInspectorToast(I18n.T("inspector_copied_toast"));
                    }
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // Meta metadata row
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#94a3b8>{I18n.Format("inspector_label_category", cur.categoryOrFamily ?? "-")}</color>", labelValueStyle);
                if (!string.IsNullOrEmpty(cur.signature)) {
                    GUILayout.Space(12);
                    GUILayout.Label($"<color=#38bdf8>{cur.signature}</color>", labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Scrollable Body
                inspectorDetailScroll = GUILayout.BeginScrollView(inspectorDetailScroll);

                // 1. Parameter Capabilities
                if (cur.itemType == GMATSchemaRegistry.CapabilityItemType.PhysicalParam) {
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label($"<color=#38bdf8><b>{I18n.T("inspector_label_capabilities")}</b></color>", sectionHeaderStyle);
                    GUILayout.Space(4);
                    GUILayout.BeginHorizontal();

                    // GMAT官方规则：极值仅限 Propagate 停止条件，不得用于 Achieve
                    bool isExtremum = cur.subtitle != null && cur.subtitle.Contains("极值");
                    bool canAchieve = !isExtremum;
                    bool canVary = false; // 物理输出量不可作为决策变量
                    bool canReport = true;
                    bool canStop = true;

                    DrawCapabilityPill(I18n.T("inspector_cap_achieve"), canAchieve, "#34d399");
                    DrawCapabilityPill(I18n.T("inspector_cap_vary"), canVary, "#38bdf8");
                    DrawCapabilityPill(I18n.T("inspector_cap_report"), canReport, "#c084fc");
                    DrawCapabilityPill(I18n.T("inspector_cap_stop"), canStop, "#f59e0b");

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    GUILayout.Space(6);
                }

                // 2. Functional Description / Signature
                if (!string.IsNullOrEmpty(cur.doc)) {
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label($"<color=#38bdf8><b>{I18n.T("inspector_label_desc")}</b></color>", sectionHeaderStyle);
                    GUILayout.Space(4);
                    GUILayout.Label(cur.doc, gridLabelStyle);
                    GUILayout.EndVertical();
                    GUILayout.Space(6);
                }

                // 3. Official Native Implementation Code
                if (!string.IsNullOrEmpty(cur.codeImpl)) {
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#a78bfa><b>{I18n.T("inspector_label_impl")}</b></color>", sectionHeaderStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);

                    // Render implementation inside dark theme code block
                    float textHeight = Mathf.Max(100f, cur.codeImpl.Split('\n').Length * 16f + 20f);
                    GUILayout.TextArea(cur.codeImpl, codeBlockStyle, GUILayout.MinHeight(100f), GUILayout.Height(textHeight));
                    GUILayout.EndVertical();
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // Bottom Resize Grip
            DrawResizeHandle(ref apiInspectorWindowRect, 860f, 520f);
            GUI.DragWindow(new Rect(0, 0, apiInspectorWindowRect.width - 30, 24));
        }

        private void DrawFilterTab(string filterKey, string label) {
            bool isSelected = (inspectorSelectedTypeFilter == filterKey);
            if (GUILayout.Button(label, isSelected ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                inspectorSelectedTypeFilter = filterKey;
            }
        }

        private void DrawTypeBadge(GMATSchemaRegistry.CapabilityItemType type) {
            string text = "";
            Color bgColor = Color.gray;
            Color textColor = Color.white;

            switch (type) {
                case GMATSchemaRegistry.CapabilityItemType.PhysicalParam:
                    text = I18n.T("inspector_badge_param");
                    bgColor = new Color(0.08f, 0.28f, 0.45f, 0.95f);
                    textColor = new Color(0.38f, 0.82f, 1.0f);
                    break;
                case GMATSchemaRegistry.CapabilityItemType.ApiClass:
                    text = I18n.T("inspector_badge_class");
                    bgColor = new Color(0.24f, 0.12f, 0.35f, 0.95f);
                    textColor = new Color(0.78f, 0.48f, 0.98f);
                    break;
                case GMATSchemaRegistry.CapabilityItemType.ApiMethod:
                    text = I18n.T("inspector_badge_method");
                    bgColor = new Color(0.30f, 0.18f, 0.05f, 0.95f);
                    textColor = new Color(0.96f, 0.62f, 0.04f);
                    break;
                case GMATSchemaRegistry.CapabilityItemType.TopFunction:
                    text = I18n.T("inspector_badge_function");
                    bgColor = new Color(0.06f, 0.26f, 0.18f, 0.95f);
                    textColor = new Color(0.20f, 0.83f, 0.60f);
                    break;
                case GMATSchemaRegistry.CapabilityItemType.UserFunction:
                    text = I18n.T("inspector_badge_user_function");
                    bgColor = new Color(0.06f, 0.26f, 0.18f, 0.95f);
                    textColor = new Color(0.20f, 0.83f, 0.60f);
                    break;
                case GMATSchemaRegistry.CapabilityItemType.UtilityFunction:
                    text = I18n.T("inspector_badge_tool_function");
                    bgColor = new Color(0.12f, 0.16f, 0.25f, 0.95f);
                    textColor = new Color(0.58f, 0.66f, 0.78f);
                    break;
            }

            if (cachedInspectorBadgeStyle == null) {
                cachedInspectorBadgeStyle = new GUIStyle(badgeDarkStyle) {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;
            GUILayout.Box($"<color=#{ColorUtility.ToHtmlStringRGB(textColor)}><b>{text}</b></color>", cachedInspectorBadgeStyle, GUILayout.Height(18));
            GUI.backgroundColor = prevBg;
        }

        private void DrawCapabilityPill(string label, bool enabled, string activeColorHex) {
            string color = enabled ? activeColorHex : "#475569";
            string prefix = enabled ? "<b>" : "";
            string suffix = enabled ? "</b>" : "";
            GUILayout.Label($"<color={color}>{prefix}{label}{suffix}</color>", labelValueStyle, GUILayout.Width(150));
        }

        private void ShowInspectorToast(string msg) {
            inspectorToastMessage = msg;
            inspectorToastTimer = 2.0f;
        }
    }
}
