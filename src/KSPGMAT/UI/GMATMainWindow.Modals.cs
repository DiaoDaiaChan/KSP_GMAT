using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void OpenParamPicker(int stepIndex, string mode) {
            paramPickerTargetStepIndex = stepIndex;
            paramPickerMode = mode;
            paramPickerSearchQuery = "";
            paramPickerCategoryIndex = 0;
            showParamPickerModal = true;
        }

        // =========================================================================
        // GMAT OFFICIAL PARAMETER SELECT DIALOG (100% REPLICATING GMAT GUI - IMAGE 3)
        // 3-Column Architecture: Object Type | Object Properties | Selected Value(s)
        // =========================================================================
        private int psdSelectedObjectTypeIndex = 0;
        private string psdSelectedHardwareName = "";
        private string psdPendingSelectedValue = "";
        private Vector2 psdScrollObjType = Vector2.zero;
        private Vector2 psdScrollObjProps = Vector2.zero;

        private void OpenGMATParameterSelectDialog(int stepIndex, string mode, string currentVal) {
            paramPickerTargetStepIndex = stepIndex;
            paramPickerMode = mode;
            psdPendingSelectedValue = currentVal ?? "";
            paramPickerSearchQuery = "";
            showParamPickerModal = true;
            float pw = Mathf.Clamp(Screen.width * 0.65f, 760f, 1000f);
            float ph = Mathf.Clamp(Screen.height * 0.65f, 500f, 750f);
            paramPickerWindowRect = new Rect((Screen.width - pw) / 2, (Screen.height - ph) / 2, pw, ph);
        }

        private void DrawParamPickerWindow(int windowID) {
            Vessel v = FlightGlobals.ActiveVessel;
            string cb = (v != null) ? v.mainBody.bodyName : "Earth";
            if (cb == "Kerbin") cb = "Earth"; else if (cb == "Mun") cb = "Luna";

            // Title Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🌐 ParameterSelectDialog (GMAT 官方参数与硬件对象选择器)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showParamPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Search Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍 快速搜索过滤:", labelValueStyle, GUILayout.Width(100));
            paramPickerSearchQuery = GUILayout.TextField(paramPickerSearchQuery, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("清空", secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(20))) {
                paramPickerSearchQuery = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 3-COLUMN MAIN LAYOUT (Image 3)
            GUILayout.BeginHorizontal();

            // -----------------------------------------------------------------
            // COLUMN 1: Object Type & Attached Hardware List (Width: 200px)
            // -----------------------------------------------------------------
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(210));
            GUILayout.Label("<b>Object Type (对象类型)</b>", subHeaderStyle);
            psdScrollObjType = GUILayout.BeginScrollView(psdScrollObjType);

            string[] objTypes = new string[] { "Spacecraft", "Variable", "ChemicalThruster", "ChemicalTank", "ImpulsiveBurn", "CoordinateSystem", "Array" };
            string[] objTypeLabels = new string[] { "🚀 Spacecraft (航天器)", "🔢 Variable (自定义变量)", "🔥 ChemicalThruster (推力器)", "⛽ ChemicalTank (储罐)", "💥 ImpulsiveBurn (脉冲机动)", "🌐 CoordinateSystem (坐标系)", "📊 Array (数组矩阵)" };

            for (int t = 0; t < objTypes.Length; t++) {
                bool isCurType = (psdSelectedObjectTypeIndex == t && string.IsNullOrEmpty(psdSelectedHardwareName));
                if (GUILayout.Button(objTypeLabels[t], isCurType ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    psdSelectedObjectTypeIndex = t;
                    psdSelectedHardwareName = "";
                }
            }

            // Attached Hardware Section (Scan actual craft + sequence)
            GUILayout.Space(6);
            GUILayout.Label("<color=#94a3b8><b>Attached Hardware:</b></color>", labelValueStyle);

            // Thrusters
            var allEngines = ROEngineInterop.ScanVesselEngines(v);
            List<string> thList = new List<string>();
            if (allEngines != null) {
                for (int e = 0; e < allEngines.Count; e++) thList.Add(GMATParameterRegistry.SanitizeGmatIdentifier(allEngines[e].title, "Engine"));
            }
            for (int s = 0; s < missionSequence.Count; s++) {
                if (missionSequence[s].type == "FiniteBurn") {
                    string bn = string.IsNullOrEmpty(missionSequence[s].burn_name) ? $"Finite_{s + 1}" : missionSequence[s].burn_name;
                    string thId = $"{GMATParameterRegistry.SanitizeGmatIdentifier(bn, "Finite")}_Thruster";
                    if (!thList.Contains(thId)) thList.Add(thId);
                }
            }
            for (int th = 0; th < thList.Count; th++) {
                string tName = thList[th];
                bool isCurHw = (psdSelectedHardwareName == tName);
                if (GUILayout.Button($"🔥 {tName}", isCurHw ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    psdSelectedHardwareName = tName;
                    psdSelectedObjectTypeIndex = 2; // Thruster
                }
            }

            // Tanks
            var allTanks = ROEngineInterop.ScanVesselTanks(v);
            List<string> tkList = new List<string>();
            if (allTanks != null) {
                for (int tk = 0; tk < allTanks.Count; tk++) tkList.Add(GMATParameterRegistry.SanitizeGmatIdentifier(allTanks[tk].id, "Tank"));
            }
            if (tkList.Count == 0) tkList.Add("MainTank");
            for (int tk = 0; tk < tkList.Count; tk++) {
                string tName = tkList[tk];
                bool isCurHw = (psdSelectedHardwareName == tName);
                if (GUILayout.Button($"⛽ {tName}", isCurHw ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    psdSelectedHardwareName = tName;
                    psdSelectedObjectTypeIndex = 3; // Tank
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // -----------------------------------------------------------------
            // COLUMN 2: Object Properties List (Width: 320px)
            // -----------------------------------------------------------------
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.MinWidth(280), GUILayout.ExpandWidth(true));
            string curObjTypeName = objTypes[psdSelectedObjectTypeIndex];
            string headerTitle = !string.IsNullOrEmpty(psdSelectedHardwareName) ? $"Properties of [{psdSelectedHardwareName}]" : $"Properties of [{curObjTypeName}]";
            GUILayout.Label($"<b>{headerTitle}</b>", subHeaderStyle);

            psdScrollObjProps = GUILayout.BeginScrollView(psdScrollObjProps);

            // Populate properties dynamically based on selected Object Type
            List<KeyValuePair<string, string>> propList = new List<KeyValuePair<string, string>>();

            if (curObjTypeName == "Spacecraft" && string.IsNullOrEmpty(psdSelectedHardwareName)) {
                // Spacecraft Orbital & Cartesian Properties
                string[] scProps = new string[] {
                    "SMA", "ECC", "INC", "RAAN", "AOP", "TA", "Altitude", "Apoapsis", "Periapsis",
                    "RadApo", "RadPer", "RMAG", "VMAG", "Energy", "OrbitPeriod", "Latitude", "Longitude",
                    "X", "Y", "Z", "VX", "VY", "VZ", "DryMass", "TotalMass", "FuelMass",
                    "ElapsedSecs", "ElapsedDays", "A1ModJulian", "Epoch"
                };
                for (int sp = 0; sp < scProps.Length; sp++) {
                    string pKey = scProps[sp];
                    string pDesc = GMATParameterRegistry.GetParamBadgeI18n(pKey);
                    propList.Add(new KeyValuePair<string, string>(pKey, pDesc));
                }
            } else if (curObjTypeName == "ChemicalThruster" || (!string.IsNullOrEmpty(psdSelectedHardwareName) && (psdSelectedHardwareName.Contains("Thruster") || psdSelectedHardwareName.Contains("Engine") || psdSelectedHardwareName.Contains("RL10")))) {
                // Thruster Properties
                propList.Add(new KeyValuePair<string, string>("ThrustDirection1", "切向推力偏转分量 (0~1)"));
                propList.Add(new KeyValuePair<string, string>("ThrustDirection2", "法向推力偏转分量 (-1~1)"));
                propList.Add(new KeyValuePair<string, string>("ThrustDirection3", "径向推力偏转分量 (-1~1)"));
                propList.Add(new KeyValuePair<string, string>("C1", "推力大小 (Newtons N)"));
                propList.Add(new KeyValuePair<string, string>("K1", "真空比冲 (Isp s)"));
                propList.Add(new KeyValuePair<string, string>("DutyCycle", "占空比开度 (0.0~1.0)"));
                propList.Add(new KeyValuePair<string, string>("ThrustScaleFactor", "推力缩放因子"));
                propList.Add(new KeyValuePair<string, string>("GravitationalAccel", "标准重力加速度 (9.81 m/s²)"));
            } else if (curObjTypeName == "ChemicalTank" || (!string.IsNullOrEmpty(psdSelectedHardwareName) && psdSelectedHardwareName.Contains("Tank"))) {
                // Tank Properties
                propList.Add(new KeyValuePair<string, string>("FuelMass", "储罐当前推进剂质量 (kg)"));
                propList.Add(new KeyValuePair<string, string>("FuelDensity", "燃料混合密度 (kg/m³)"));
                propList.Add(new KeyValuePair<string, string>("Pressure", "储罐工作压力 (kPa)"));
                propList.Add(new KeyValuePair<string, string>("Volume", "储罐物理容积 (m³)"));
                propList.Add(new KeyValuePair<string, string>("Temperature", "推进剂温度 (°C)"));
            } else if (curObjTypeName == "Variable") {
                // User declared sequence variables
                List<string> declaredVars = new List<string>() { "burntime", "GTO_Duration", "GEO_Duration", "LOI_Duration1", "LOI_Duration2", "LOI_Duration3", "TLI_Duration" };
                for (int st = 0; st < missionSequence.Count; st++) {
                    var ms = missionSequence[st];
                    if (!string.IsNullOrEmpty(ms.vary_variable) && !declaredVars.Contains(ms.vary_variable) && !ms.vary_variable.Contains(".")) {
                        declaredVars.Add(ms.vary_variable);
                    }
                    if (!string.IsNullOrEmpty(ms.variable) && !declaredVars.Contains(ms.variable)) {
                        declaredVars.Add(ms.variable);
                    }
                }
                for (int dv = 0; dv < declaredVars.Count; dv++) {
                    propList.Add(new KeyValuePair<string, string>(declaredVars[dv], "用户标量时间/寻优变量 (s)"));
                }
            } else if (curObjTypeName == "ImpulsiveBurn") {
                propList.Add(new KeyValuePair<string, string>("Element1", "切向速度增量 ΔV_V (km/s)"));
                propList.Add(new KeyValuePair<string, string>("Element2", "法向速度增量 ΔV_N (km/s)"));
                propList.Add(new KeyValuePair<string, string>("Element3", "径向速度增量 ΔV_B (km/s)"));
            } else if (curObjTypeName == "CoordinateSystem") {
                propList.Add(new KeyValuePair<string, string>("EarthMJ2000Eq", "地球标准赤道惯性坐标系"));
                propList.Add(new KeyValuePair<string, string>("Local", "局部轨道面 VNB 坐标系"));
                propList.Add(new KeyValuePair<string, string>("MoonInertial", "月心惯性坐标系"));
            }

            // Apply search filter if present
            if (!string.IsNullOrEmpty(paramPickerSearchQuery)) {
                string q = paramPickerSearchQuery.Trim().ToLowerInvariant();
                propList = propList.FindAll(x => x.Key.ToLowerInvariant().Contains(q) || (x.Value != null && x.Value.ToLowerInvariant().Contains(q)));
            }

            for (int p = 0; p < propList.Count; p++) {
                var kv = propList[p];
                string propKey = kv.Key;
                string propDesc = kv.Value;

                // Formulate fully-qualified GMAT path
                string fullGmatPath = propKey;
                if (!string.IsNullOrEmpty(psdSelectedHardwareName)) {
                    fullGmatPath = $"Sat.{psdSelectedHardwareName}.{propKey}";
                } else if (curObjTypeName == "Spacecraft") {
                    if (propKey == "X" || propKey == "Y" || propKey == "Z" || propKey == "VX" || propKey == "VY" || propKey == "VZ") {
                        fullGmatPath = $"Sat.EarthMJ2000Eq.{propKey}";
                    } else if (propKey == "TotalMass" || propKey == "DryMass" || propKey == "FuelMass" || propKey == "ElapsedSecs" || propKey == "ElapsedDays") {
                        fullGmatPath = $"Sat.{propKey}";
                    } else {
                        fullGmatPath = $"Sat.{cb}.{propKey}";
                    }
                } else if (curObjTypeName == "ImpulsiveBurn") {
                    fullGmatPath = $"GEO_Burn.{propKey}";
                }

                bool isSelected = (psdPendingSelectedValue == fullGmatPath || psdPendingSelectedValue == propKey);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"➔ {propKey}", isSelected ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(170), GUILayout.Height(20))) {
                    psdPendingSelectedValue = fullGmatPath;
                }
                GUILayout.Label($"<color=#facc15>{propDesc}</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // -----------------------------------------------------------------
            // COLUMN 3: Selected Value(s) & Confirmation Actions (Width: 220px)
            // -----------------------------------------------------------------
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(220));
            GUILayout.Label("<b>Selected Value(s)</b>", subHeaderStyle);
            GUILayout.Space(4);

            GUILayout.Label("<color=#94a3b8>完整 GMAT 变量表达式:</color>", labelValueStyle);
            psdPendingSelectedValue = GUILayout.TextField(psdPendingSelectedValue, GUILayout.Height(26));

            GUILayout.Space(8);
            string i18nSummary = GMATParameterRegistry.GetParamBadgeI18n(psdPendingSelectedValue);
            if (!string.IsNullOrEmpty(i18nSummary)) {
                GUILayout.Label($"<color=#34d399><b>[{i18nSummary}]</b></color>", labelValueStyle);
            }

            GUILayout.FlexibleSpace();

            // [OK] & [Cancel] Buttons (Image 3)
            if (GUILayout.Button("✔ OK / 确定应用", successButtonStyle, GUILayout.Height(28))) {
                if (paramPickerTargetStepIndex >= 0 && paramPickerTargetStepIndex < missionSequence.Count) {
                    var s = missionSequence[paramPickerTargetStepIndex];
                    if (paramPickerMode == "vary") {
                        s.vary_variable = psdPendingSelectedValue;
                        pendingFocusControl = $"Field_Vary_{paramPickerTargetStepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    } else if (paramPickerMode == "achieve") {
                        s.achieve_param = psdPendingSelectedValue;
                        pendingFocusControl = $"Field_Achieve_{paramPickerTargetStepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    } else if (paramPickerMode == "stop" || paramPickerMode == "prop_stop") {
                        s.stop_type = psdPendingSelectedValue;
                        pendingFocusControl = $"Field_PropStop_{paramPickerTargetStepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    }
                }
                showParamPickerModal = false;
            }

            GUILayout.Space(4);
            if (GUILayout.Button("✕ Cancel / 取消", secondaryButtonStyle, GUILayout.Height(24))) {
                showParamPickerModal = false;
            }

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawOldParamPickerWindow_Deprecated(int windowID) {
            bool isPropStop = (paramPickerMode == "prop_stop" || paramPickerMode == "stop");
            string cb = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.mainBody.bodyName : "Earth";
            if (cb == "Kerbin") cb = "Earth"; else if (cb == "Mun") cb = "Luna";

            GUILayout.BeginHorizontal();
            string pickerTitle = isPropStop ? "⏱️ GMAT 动力学终止条件字典 (Propagate Stop Conditions)" :
                                 (paramPickerMode == "achieve") ? "🎯 GMAT 官方约束目标字典 (Achieve Parameters)" :
                                 "📋 GMAT 动力学与开普勒参数字典 (GMAT Parameter Library)";
            GUILayout.Label($"<b>{pickerTitle}</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showParamPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Search Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍 快速搜索过滤:", labelValueStyle, GUILayout.Width(95));
            paramPickerSearchQuery = GUILayout.TextField(paramPickerSearchQuery);
            if (GUILayout.Button("清空", secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(20))) {
                paramPickerSearchQuery = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            string[] categoryOptions = isPropStop ? GMATParameterRegistry.PropStopCategories : GMATParameterRegistry.Categories;
            if (paramPickerCategoryIndex >= categoryOptions.Length) paramPickerCategoryIndex = 0;

            // Category Filter Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label("参数类别:", labelValueStyle, GUILayout.Width(65));
            for (int c = 0; c < categoryOptions.Length; c++) {
                bool isCur = (paramPickerCategoryIndex == c);
                if (GUILayout.Button(categoryOptions[c], isCur ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(22))) {
                    paramPickerCategoryIndex = c;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Scrollable List
            float pickerScrollH = Mathf.Max(260f, paramPickerWindowRect.height - 195f);
            paramPickerScrollPos = GUILayout.BeginScrollView(paramPickerScrollPos, cardBoxStyle, GUILayout.Height(pickerScrollH));

            string curCategory = categoryOptions[paramPickerCategoryIndex];

            if (isPropStop) {
                List<PropStopDef> stopItems = GMATParameterRegistry.GetStopDefsByCategory(curCategory);
                if (!string.IsNullOrEmpty(paramPickerSearchQuery)) {
                    string q = paramPickerSearchQuery.Trim().ToLowerInvariant();
                    stopItems = stopItems.FindAll(x => x.Key.ToLowerInvariant().Contains(q) ||
                                                       x.DisplayName.ToLowerInvariant().Contains(q) ||
                                                       x.Description.ToLowerInvariant().Contains(q));
                }
                if (stopItems.Count == 0) {
                    GUILayout.Space(20);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("<color=#64748b>未找到匹配终止条件</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                } else {
                    for (int p = 0; p < stopItems.Count; p++) {
                        var item = stopItems[p];
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button($"➕ {item.Key}", secondaryButtonStyle, GUILayout.Width(180), GUILayout.Height(22))) {
                            if (paramPickerTargetStepIndex >= 0 && paramPickerTargetStepIndex < missionSequence.Count) {
                                var s = missionSequence[paramPickerTargetStepIndex];
                                
                                // Auto-fill with fully-qualified GMAT parameter name
                                string k = item.Key;
                                if (k == "ElapsedSecs" || k == "ElapsedDays" || k == "A1ModJulian" || k == "Epoch") {
                                    s.stop_type = $"Sat.{k}";
                                } else if (k == "X" || k == "Y" || k == "Z" || k == "VX" || k == "VY" || k == "VZ") {
                                    s.stop_type = $"Sat.EarthMJ2000Eq.{k}";
                                } else {
                                    s.stop_type = $"Sat.{cb}.{k}";
                                }

                                if (!item.IsExtremum && item.DefaultValue != 0) {
                                    s.target_val = item.DefaultValue;
                                }
                                if (item.DefaultTolerance > 0) {
                                    s.stop_tolerance = item.DefaultTolerance;
                                }
                            }
                            showParamPickerModal = false;
                        }
                        string cleanName = item.DisplayName;
                        if (cleanName.Contains("[")) cleanName = cleanName.Substring(0, cleanName.IndexOf('[')).Trim();
                        string i18nDesc = GMATParameterRegistry.GetParamBadgeI18n(item.Key);
                        string unitBadge = (!string.IsNullOrEmpty(item.Unit) && item.Unit != "?") ? $" <color=#38bdf8>[{item.Unit}]</color>" : "";
                        string descText = !string.IsNullOrEmpty(i18nDesc) ? $"<color=#facc15><b>{i18nDesc}</b></color> <color=#94a3b8>({item.GetLocalizedDescription()})</color>" : $"<color=#94a3b8>{item.GetLocalizedDescription()}</color>";
                        GUILayout.Label($"<b>{cleanName}</b>{unitBadge} - {descText}", labelValueStyle);
                        GUILayout.EndHorizontal();
                    }
                }
            } else {
                List<GMATParam> paramItems = GMATParameterRegistry.GetByCategory(curCategory);
                if (!string.IsNullOrEmpty(paramPickerSearchQuery)) {
                    string q = paramPickerSearchQuery.Trim().ToLowerInvariant();
                    paramItems = paramItems.FindAll(x => x.Name.ToLowerInvariant().Contains(q) ||
                                                         (x.DescriptionZh != null && x.DescriptionZh.ToLowerInvariant().Contains(q)) ||
                                                         (x.DescriptionEn != null && x.DescriptionEn.ToLowerInvariant().Contains(q)) ||
                                                         x.Description.ToLowerInvariant().Contains(q) ||
                                                         x.Unit.ToLowerInvariant().Contains(q));
                }
                if (paramItems.Count == 0) {
                    GUILayout.Space(20);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("<color=#64748b>未找到匹配参数</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                } else {
                    for (int p = 0; p < paramItems.Count; p++) {
                        var item = paramItems[p];
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button($"➕ {item.Name}", secondaryButtonStyle, GUILayout.Width(180), GUILayout.Height(22))) {
                            if (paramPickerTargetStepIndex >= 0 && paramPickerTargetStepIndex < missionSequence.Count) {
                                var s = missionSequence[paramPickerTargetStepIndex];
                                if (paramPickerMode == "achieve") {
                                    string nm = item.Name;
                                    if (nm == "TotalMass" || nm == "DryMass" || nm == "FuelMass") {
                                        s.achieve_param = $"Sat.{nm}";
                                    } else if (nm == "X" || nm == "Y" || nm == "Z" || nm == "VX" || nm == "VY" || nm == "VZ") {
                                        s.achieve_param = $"Sat.EarthMJ2000Eq.{nm}";
                                    } else {
                                        s.achieve_param = $"Sat.{cb}.{nm}";
                                    }

                                    double val;
                                    if (double.TryParse(item.Example, out val)) {
                                        s.achieve_val = val;
                                    }
                                } else {
                                    s.vary_variable = item.Name;
                                    s.variable = item.Name;
                                    double val;
                                    if (double.TryParse(item.Example, out val)) {
                                        s.vary_initial = val;
                                        s.expression = item.Example;
                                    }
                                }
                            }
                            showParamPickerModal = false;
                        }
                        string i18nDesc = GMATParameterRegistry.GetParamBadgeI18n(item.Name);
                        string unitBadge = (!string.IsNullOrEmpty(item.Unit) && item.Unit != "?") ? $" <color=#38bdf8>[{item.Unit}]</color>" : "";
                        string descText = !string.IsNullOrEmpty(i18nDesc) ? $"<color=#facc15><b>{i18nDesc}</b></color> <color=#94a3b8>({item.GetLocalizedDescription()})</color>" : $"<color=#94a3b8>{item.GetLocalizedDescription()}</color>";
                        GUILayout.Label($"<b>{item.Name}</b>{unitBadge} - {descText} <color=#64748b>(例: {item.Example})</color>", labelValueStyle);
                        GUILayout.EndHorizontal();
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ 完成并关闭", secondaryButtonStyle, GUILayout.Width(120), GUILayout.Height(24))) {
                showParamPickerModal = false;
            }
            GUILayout.EndHorizontal();
        }

        private void OpenTargeterSetupModal(int stepIndex = -1) {
            editingTargetStepIndex = stepIndex;
            if (stepIndex >= 0 && stepIndex < missionSequence.Count) {
                var s = missionSequence[stepIndex];
                wizardDcName = string.IsNullOrEmpty(s.dc_name) ? "DefaultDC" : s.dc_name;
                wizardAlgorithmIndex = Array.IndexOf(WizardAlgorithmNames, s.dc_algorithm);
                if (wizardAlgorithmIndex < 0) wizardAlgorithmIndex = 0;
                wizardMaxIterations = s.dc_max_iter > 0 ? s.dc_max_iter : 50;
                wizardDerivativeIndex = Array.IndexOf(WizardDerivativeNames, s.dc_derivative_method);
                if (wizardDerivativeIndex < 0) wizardDerivativeIndex = 0;
                wizardSolveModeIndex = Array.IndexOf(WizardSolveModeNames, s.solve_mode);
                if (wizardSolveModeIndex < 0) wizardSolveModeIndex = 0;
                wizardExitModeIndex = Array.IndexOf(WizardExitModeNames, s.exit_mode);
                if (wizardExitModeIndex < 0) wizardExitModeIndex = 0;
            } else {
                wizardDcName = "DefaultDC";
                wizardAlgorithmIndex = 0;
                wizardMaxIterations = 50;
                wizardDerivativeIndex = 0;
                wizardSolveModeIndex = 0;
                wizardExitModeIndex = 0;
                wizardTemplateIndex = 0;
            }
            showTargeterSetupModal = true;
        }

        private void DrawTargeterSetupWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🎯 GMAT 官方微分修正求解器管理与配置 (Solvers & Targeters)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showTargeterSetupModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Solver Instance Switcher Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label("当前求解器:", labelValueStyle, GUILayout.Width(75));

            var solverConfigs = GMATParameterRegistry.GetAllSolverConfigs();
            List<string> solverKeys = new List<string>(solverConfigs.Keys);
            if (!solverConfigs.ContainsKey(wizardDcName) && solverKeys.Count > 0) {
                wizardDcName = solverKeys[0];
            }

            for (int s = 0; s < solverKeys.Count; s++) {
                bool isSelected = (wizardDcName == solverKeys[s]);
                if (GUILayout.Button(solverKeys[s], isSelected ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(24))) {
                    wizardDcName = solverKeys[s];
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("➕ 新建求解器...", actionButtonStyle, GUILayout.Height(24))) {
                string newName = "CustomDC_" + (solverConfigs.Count + 1);
                var newSolver = new SolverConfig {
                    name = newName,
                    algorithm = "NewtonRaphson",
                    derivativeMethod = "ForwardDifference",
                    maxIterations = 50,
                    solveMode = "Solve",
                    exitMode = "SaveAndContinue",
                    reportStyle = "Normal",
                    showProgress = true,
                    isDefault = false
                };
                GMATParameterRegistry.AddOrUpdateSolver(newSolver);
                wizardDcName = newName;
                ScreenMessages.PostScreenMessage($"✅ 已新建求解器: {newName}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            SolverConfig activeSolver = solverConfigs.ContainsKey(wizardDcName) ? solverConfigs[wizardDcName] : new SolverConfig();

            if (!activeSolver.isDefault) {
                GUILayout.Space(6);
                if (GUILayout.Button("🗑️ 删除", deleteButtonStyle, GUILayout.Height(24))) {
                    GMATParameterRegistry.DeleteSolver(wizardDcName);
                    wizardDcName = "DefaultDC";
                    ScreenMessages.PostScreenMessage("🗑️ 已删除自定义求解器", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginVertical(cardBoxStyle);

            // Section 1: 求解器命名与标识
            GUILayout.Label("1. 求解器标识与命名", subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("求解器名称 (DC Name):", labelValueStyle, GUILayout.Width(140));
            activeSolver.name = GUILayout.TextField(activeSolver.name ?? wizardDcName, GUILayout.Width(160));
            GUILayout.Label("<color=#64748b>(GMAT 原生 DifferentialCorrector 实例名)</color>", labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Section 2: 数学优化算法 (Algorithm)
            GUILayout.Label("2. 数学优化算法 (Algorithm)", subHeaderStyle);
            GUILayout.BeginHorizontal();
            for (int a = 0; a < WizardAlgorithmNames.Length; a++) {
                bool isSel = (activeSolver.algorithm == WizardAlgorithmNames[a]);
                if (GUILayout.Button(WizardAlgorithmLabels[a], isSel ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(24))) {
                    activeSolver.algorithm = WizardAlgorithmNames[a];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Section 3: 导数计算差分方式 (DerivativeMethod)
            GUILayout.Label("3. 导数计算差分方式 (DerivativeMethod)", subHeaderStyle);
            GUILayout.BeginHorizontal();
            for (int d = 0; d < WizardDerivativeNames.Length; d++) {
                bool isSel = (activeSolver.derivativeMethod == WizardDerivativeNames[d]);
                if (GUILayout.Button(WizardDerivativeLabels[d], isSel ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(24))) {
                    activeSolver.derivativeMethod = WizardDerivativeNames[d];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Section 4: 控制参数与模式 (Modes & Parameters)
            GUILayout.Label("4. 求解模式、退出策略与最大迭代", subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("求解模式:", labelValueStyle, GUILayout.Width(75));
            for (int sm = 0; sm < WizardSolveModeNames.Length; sm++) {
                bool isSel = (activeSolver.solveMode == WizardSolveModeNames[sm]);
                if (GUILayout.Button(WizardSolveModeLabels[sm], isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    activeSolver.solveMode = WizardSolveModeNames[sm];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("退出模式:", labelValueStyle, GUILayout.Width(75));
            for (int em = 0; em < WizardExitModeNames.Length; em++) {
                bool isSel = (activeSolver.exitMode == WizardExitModeNames[em]);
                if (GUILayout.Button(WizardExitModeLabels[em], isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    activeSolver.exitMode = WizardExitModeNames[em];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("最大迭代次数 (MaxIterations):", labelValueStyle, GUILayout.Width(190));
            string sMaxIter = GUILayout.TextField(activeSolver.maxIterations.ToString(), GUILayout.Width(70));
            int.TryParse(sMaxIter, out activeSolver.maxIterations);

            GUILayout.Space(15);
            activeSolver.showProgress = GUILayout.Toggle(activeSolver.showProgress, "控制台显示收敛进度", GUILayout.Width(160));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            // Bottom Buttons
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("💾 保存并应用此求解器", actionButtonStyle, GUILayout.Width(180), GUILayout.Height(28))) {
                GMATParameterRegistry.AddOrUpdateSolver(activeSolver);
                if (editingTargetStepIndex >= 0 && editingTargetStepIndex < missionSequence.Count) {
                    var step = missionSequence[editingTargetStepIndex];
                    step.dc_name = activeSolver.name;
                    step.dc_algorithm = activeSolver.algorithm;
                    step.dc_max_iter = activeSolver.maxIterations;
                }
                showTargeterSetupModal = false;
                ScreenMessages.PostScreenMessage($"✅ 求解器配置已成功同步: {activeSolver.name}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            GUILayout.Space(10);
            if (GUILayout.Button("关闭", secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(28))) {
                showTargeterSetupModal = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, targeterSetupWindowRect.width, 30));
        }

        private void ApplyTargeterSetup() {
            string dcName = string.IsNullOrEmpty(wizardDcName) ? "DefaultDC" : wizardDcName.Trim();
            string dcAlg = WizardAlgorithmNames[wizardAlgorithmIndex];
            string dcDm = WizardDerivativeNames[wizardDerivativeIndex];
            string solveMode = WizardSolveModeNames[wizardSolveModeIndex];
            string exitMode = WizardExitModeNames[wizardExitModeIndex];
            int maxIter = Mathf.Max(5, wizardMaxIterations);

            if (editingTargetStepIndex >= 0 && editingTargetStepIndex < missionSequence.Count) {
                var s = missionSequence[editingTargetStepIndex];
                s.dc_name = dcName;
                s.dc_algorithm = dcAlg;
                s.dc_derivative_method = dcDm;
                s.solve_mode = solveMode;
                s.exit_mode = exitMode;
                s.dc_max_iter = maxIter;
                ScreenMessages.PostScreenMessage($"✅ 求解器 {dcName} 参数已更新！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                if (wizardTemplateIndex == 0) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = "自定义打靶组",
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = "闭合自定义打靶" });
                } else if (wizardTemplateIndex == 1) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = "远地点高度打靶",
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "burntime", vary_initial = 150.0, vary_lower = 0.0, vary_upper = 1000.0, vary_max_step = 2.0, label = "Vary burntime" });
                    missionSequence.Add(new MissionStep { type = "FiniteBurn", propagator = "LEOProp", burn_use_vary_var = true, burn_vary_var_name = "burntime", label = "有限推力顺行点火" });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis", label = "积分至远地点" });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Earth.Altitude", achieve_val = 35786.0, achieve_tolerance = 0.1, label = "Achieve Ap=35786km" });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = "闭合远地点打靶" });
                } else if (wizardTemplateIndex == 2) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = "GEO 周期同步打靶",
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "burntime", vary_initial = 200.0, vary_lower = 0.0, vary_upper = 2000.0, vary_max_step = 2.0, label = "Vary burntime" });
                    missionSequence.Add(new MissionStep { type = "FiniteBurn", propagator = "LEOProp", burn_use_vary_var = true, burn_vary_var_name = "burntime", label = "GEO 圆化点火" });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis", label = "积分至远地点" });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Earth.OrbitPeriod", achieve_val = 86164.0, achieve_tolerance = 1.0, label = "Achieve Period=86164s" });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = "闭合 GEO 打靶" });
                } else if (wizardTemplateIndex == 3) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = "地月转移注入 TLI",
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "burntime", vary_initial = 350.0, vary_lower = 0.0, vary_upper = 3000.0, vary_max_step = 2.0, label = "Vary TLI burntime" });
                    missionSequence.Add(new MissionStep { type = "FiniteBurn", propagator = "LEOProp", burn_use_vary_var = true, burn_vary_var_name = "burntime", label = "TLI 点火" });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "CislunarProp", stop_type = "Days", duration = 4.5, label = "巡航至月球" });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Luna.RMAG", achieve_val = 3229.0, achieve_tolerance = 5.0, label = "Achieve Perilune=3229km" });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = "闭合地月打靶" });
                } else if (wizardTemplateIndex == 4) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = "轨道面倾角变换",
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "Maneuver1.Element2", vary_initial = 100.0, vary_lower = -1000.0, vary_upper = 1000.0, vary_max_step = 5.0, label = "Vary Normal dV" });
                    missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", dv_v = 0.0, dv_n = 100.0, dv_b = 0.0, label = "交点法向脉冲机动" });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Node", label = "积分至赤道交点" });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Earth.INC", achieve_val = 0.0, achieve_tolerance = 0.01, label = "Achieve INC=0°" });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = "闭合倾角打靶" });
                }
                ScreenMessages.PostScreenMessage($"✅ 成功创建打靶求解组 [{dcName}]！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void DrawSimulationImageWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🖼️ GMAT 轨道仿真图像渲染器 (Trajectory 2D/3D View)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showSimulationImageWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // View Mode Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label("切换视图:", labelValueStyle, GUILayout.Width(65));
            string[] modes = new string[] { "dual", "planar", "target" };
            string[] modeLabels = new string[] { "🌐 双视角 (平面+3D)", "🪐 轨道全景 (2D)", "🎯 目标端点特写" };

            for (int i = 0; i < modes.Length; i++) {
                bool isCur = (currentViewMode == modes[i]);
                if (GUILayout.Button(modeLabels[i], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(24))) {
                    currentViewMode = modes[i];
                    RerenderSimulationImage();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Responsive Image Container
            float imgW = simulationImageWindowRect.width - 32f;
            float imgH = Mathf.Max(260f, simulationImageWindowRect.height - 135f);

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(imgW), GUILayout.Height(imgH));
            if (simulationTexture != null) {
                Rect imgRect = GUILayoutUtility.GetRect(imgW - 10f, imgH - 10f);
                GUI.DrawTexture(imgRect, simulationTexture, ScaleMode.ScaleToFit);
            } else {
                GUILayout.Space(imgH * 0.4f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=#94a3b8>{imageStatusText}</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 重新渲染此视角", actionButtonStyle, GUILayout.Height(26))) {
                RerenderSimulationImage();
            }
            GUILayout.Label($"状态: {imageStatusText}", labelValueStyle);
            GUILayout.EndHorizontal();

            DrawResizeHandle(ref simulationImageWindowRect, 680f, 480f);
            GUI.DragWindow(new Rect(0, 0, simulationImageWindowRect.width - 40, 26));
        }

        private void RerenderSimulationImage() {
            imageStatusText = "正在请求 Python 重新渲染图像...";
            string bodyName = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.mainBody.name : "Earth";
            double bodyRadiusKm = FlightGlobals.ActiveVessel != null ? (FlightGlobals.ActiveVessel.mainBody.Radius / 1000.0) : 6378.14;
            GMATClient.RenderTrajectory(currentViewMode, bodyName, bodyRadiusKm, (success, path) => {
                if (success) {
                    LoadSimulationTexture(path);
                } else {
                    imageStatusText = "重新渲染失败: " + path;
                }
            });
        }

        private void DrawPropagatorConfigWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>⚙️ GMAT 官方传播子与高精度受力模型深度配置 (Propagator & Force Models)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showPropagatorConfigWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Propagator Selector & Management Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label("当前传播子:", labelValueStyle, GUILayout.Width(75));

            var propConfigs = GMATParameterRegistry.GetAllPropagatorConfigs();
            List<string> propKeys = new List<string>(propConfigs.Keys);
            if (!propConfigs.ContainsKey(selectedPropagatorKey)) {
                selectedPropagatorKey = propKeys.Count > 0 ? propKeys[0] : "LEOProp";
            }

            for (int i = 0; i < propKeys.Count; i++) {
                bool isSelected = (selectedPropagatorKey == propKeys[i]);
                if (GUILayout.Button(propKeys[i], isSelected ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(24))) {
                    selectedPropagatorKey = propKeys[i];
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("➕ 新建传播子...", actionButtonStyle, GUILayout.Height(24))) {
                string newName = "CustomProp_" + (propConfigs.Count + 1);
                var newCfg = new PropagatorConfig {
                    name = newName,
                    integratorType = "RungeKutta89",
                    accuracy = 1e-11,
                    initialStepSize = 60.0,
                    minStep = 0.001,
                    maxStep = 2700.0,
                    centralBody = "Earth",
                    gravityEarth = true,
                    gravityLuna = true,
                    gravitySun = true,
                    gravityJupiter = true,
                    isDefault = false
                };
                GMATParameterRegistry.AddOrUpdatePropagator(newCfg);
                selectedPropagatorKey = newName;
                ScreenMessages.PostScreenMessage($"✅ 已新建传播子: {newName}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            PropagatorConfig cfg = propConfigs.ContainsKey(selectedPropagatorKey) ? propConfigs[selectedPropagatorKey] : new PropagatorConfig();

            if (!cfg.isDefault) {
                GUILayout.Space(6);
                if (GUILayout.Button("🗑️ 删除", deleteButtonStyle, GUILayout.Height(24))) {
                    GMATParameterRegistry.DeletePropagator(selectedPropagatorKey);
                    selectedPropagatorKey = "LEOProp";
                    ScreenMessages.PostScreenMessage("🗑️ 已删除自定义传播子", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            float propScrollH = Mathf.Max(280f, propagatorConfigWindowRect.height - 150f);
            scrollPosPropConfig = GUILayout.BeginScrollView(scrollPosPropConfig, cardBoxStyle, GUILayout.Height(propScrollH));

            // Section 1: 积分器动力学内核 (Integrator Core - Official GMAT Integrators)
            GUILayout.Label("1. 数值积分器算法 (Numerical Integrator Engine)", subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("积分器内核:", labelValueStyle, GUILayout.Width(80));

            string[] integrators = new string[] { "RungeKutta89", "PrinceDormand78", "RungeKutta56", "RungeKutta68", "AdamsBashforthMoulton" };
            string[] integLabels = new string[] { "RK89 (8阶高精)", "PD78 (7/8阶自适应)", "RK56 (5/6阶经典)", "RK68 (6/8阶平衡)", "ABM (4阶预报校正)" };

            for (int it = 0; it < integrators.Length; it++) {
                bool isCur = (cfg.integratorType == integrators[it]);
                if (GUILayout.Button(integLabels[it], isCur ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(22))) {
                    cfg.integratorType = integrators[it];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("初始步长 (InitialStepSize):", labelValueStyle, GUILayout.Width(170));
            string sInitStep = GUILayout.TextField(cfg.initialStepSize.ToString("F1"), GUILayout.Width(70));
            double.TryParse(sInitStep, out cfg.initialStepSize);

            GUILayout.Label("数值精度 (Accuracy):", labelValueStyle, GUILayout.Width(140));
            string sAcc = GUILayout.TextField((cfg.accuracy > 1e-20 ? cfg.accuracy : 1e-11).ToString("E2", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(80));
            if (double.TryParse(sAcc, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAcc)) {
                if (parsedAcc > 0.0) cfg.accuracy = parsedAcc;
            }

            GUILayout.Label("最小步长 (MinStep):", labelValueStyle, GUILayout.Width(130));
            string sMinStep = GUILayout.TextField(cfg.minStep.ToString("F3"), GUILayout.Width(60));
            double.TryParse(sMinStep, out cfg.minStep);

            GUILayout.Label("最大步长 (MaxStep):", labelValueStyle, GUILayout.Width(130));
            string sMaxStep = GUILayout.TextField(cfg.maxStep.ToString("F1"), GUILayout.Width(70));
            double.TryParse(sMaxStep, out cfg.maxStep);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Section 2: 引力场与多体引力摄动 (Gravity Field & Point Masses)
            GUILayout.Label("2. 引力场与多体摄动模型 (Gravity Field & Point Masses)", subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("中心天体 (CentralBody):", labelValueStyle, GUILayout.Width(160));
            Vessel v = FlightGlobals.ActiveVessel;
            List<string> bodies = GMATParameterRegistry.GetAllBodies(v);
            for (int b = 0; b < bodies.Count && b < 6; b++) {
                string bName = bodies[b];
                bool isCenter = (cfg.centralBody == bName);
                if (GUILayout.Button(bName, isCenter ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.centralBody = bName;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("点质量引力摄动源:", labelValueStyle, GUILayout.Width(130));
            cfg.gravityEarth = GUILayout.Toggle(cfg.gravityEarth, "地球 (Earth)", GUILayout.Width(100));
            cfg.gravityLuna = GUILayout.Toggle(cfg.gravityLuna, "月球 (Luna)", GUILayout.Width(95));
            cfg.gravitySun = GUILayout.Toggle(cfg.gravitySun, "太阳 (Sun)", GUILayout.Width(95));
            cfg.gravityJupiter = GUILayout.Toggle(cfg.gravityJupiter, "木星 (Jupiter)", GUILayout.Width(105));
            cfg.gravityMars = GUILayout.Toggle(cfg.gravityMars, "火星 (Mars)", GUILayout.Width(95));
            cfg.gravityVenus = GUILayout.Toggle(cfg.gravityVenus, "金星 (Venus)", GUILayout.Width(95));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Section 3: 大气与环境扰动 (Atmosphere & SRP)
            GUILayout.Label("3. 大气阻力与太阳光压扰动 (Atmosphere & SRP)", subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("大气模型 (Drag):", labelValueStyle, GUILayout.Width(110));
            string[] atmos = new string[] { "None", "MSISE90", "JacchiaRoberts", "Exponential" };
            for (int a = 0; a < atmos.Length; a++) {
                bool isAtmo = (cfg.dragModel == atmos[a]);
                if (GUILayout.Button(atmos[a], isAtmo ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.dragModel = atmos[a];
                }
            }

            GUILayout.Space(15);
            cfg.enableSRP = GUILayout.Toggle(cfg.enableSRP, "太阳辐射光压 (SRP)", GUILayout.Width(140));
            cfg.enableRelativity = GUILayout.Toggle(cfg.enableRelativity, "相对论修正", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            // Bottom Action Toolbar
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("💾 保存并应用此传播子", actionButtonStyle, GUILayout.Width(180), GUILayout.Height(28))) {
                GMATParameterRegistry.AddOrUpdatePropagator(cfg);
                ScreenMessages.PostScreenMessage($"✅ 传播子已保存并同步至全局: {cfg.name}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            GUILayout.Space(10);
            if (GUILayout.Button("关闭", secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(28))) {
                showPropagatorConfigWindow = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, propagatorConfigWindowRect.width, 30));
        }

        private void DrawCraftDetailsWindow(int windowID) {
            Vessel vessel = FlightGlobals.ActiveVessel;
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🛰️ GMAT 同步航天器完整参数快照 (Spacecraft Parameters)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showCraftDetailsWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            scrollPosCraftDetails = GUILayout.BeginScrollView(scrollPosCraftDetails);

            if (vessel != null) {
                // Section 1: Basic & Orbit
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("🌍 航天器身份与轨道引力环境", subHeaderStyle);
                GUILayout.Label($"飞船名称: <b>{vessel.vesselName}</b> | 活跃中心天体 (SOI): <b>{vessel.mainBody.name}</b>", labelValueStyle);
                double ut = Planetarium.GetUniversalTime();
                GUILayout.Label($"当前游戏世界时间 UT: <b>{ut:F2} s</b>", labelValueStyle);

                Vector3d pos_km, vel_kms;
                PrincipiaInterop.GetVesselStateICRF(vessel, out pos_km, out vel_kms);
                GUILayout.Label($"空间惯性系位置 (ICRF): X=<b>{pos_km.x:F2}</b> km, Y=<b>{pos_km.y:F2}</b> km, Z=<b>{pos_km.z:F2}</b> km (r=<b>{pos_km.magnitude:F1}</b> km)", labelValueStyle);
                GUILayout.Label($"空间惯性系速度 (ICRF): VX=<b>{vel_kms.x:F3}</b> km/s, VY=<b>{vel_kms.y:F3}</b> km/s, VZ=<b>{vel_kms.z:F3}</b> km/s (v=<b>{vel_kms.magnitude:F3}</b> km/s)", labelValueStyle);
                if (vessel.orbit != null) {
                    GUILayout.Label($"开普勒根数: 半长轴 SMA=<b>{vessel.orbit.semiMajorAxis / 1000.0:F1} km</b>, 离心率 ECC=<b>{vessel.orbit.eccentricity:F4}</b>, 倾角 INC=<b>{vessel.orbit.inclination:F2}°</b>", labelValueStyle);
                    GUILayout.Label($"近拱点高度: <b>{vessel.orbit.PeA / 1000.0:F1} km</b>, 远拱点高度: <b>{vessel.orbit.ApA / 1000.0:F1} km</b>", labelValueStyle);
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 2: Staging Stack
                GUILayout.BeginVertical(cardBoxStyle);
                var selStages = GetSelectedStagesList();
                GUILayout.Label($"🚀 多级火箭质量与 ΔV 组合 (已选 {selStages.Count} 个分级)", subHeaderStyle);
                double sumDv = 0, sumFuel = 0, maxWet = 0, minDry = double.MaxValue;
                foreach (var st in selStages) {
                    sumDv += st.deltaV;
                    sumFuel += st.fuelMassTonnes;
                    if (st.wetMassTonnes > maxWet) maxWet = st.wetMassTonnes;
                    if (st.dryMassTonnes < minDry) minDry = st.dryMassTonnes;
                    GUILayout.Label($"  - <b>Stage {st.stageNumber}</b>: ΔV=<b>{st.deltaV:F0} m/s</b> | 湿重=<b>{st.wetMassTonnes:F2}t</b> | 燃料=<b>{st.fuelMassTonnes:F2}t</b> | 干重=<b>{st.dryMassTonnes:F2}t</b>", labelValueStyle);
                }
                if (minDry == double.MaxValue) minDry = vessel.totalMass * 0.4;
                if (maxWet <= 0) maxWet = vessel.totalMass;
                GUILayout.Space(2);
                GUILayout.Label($"<color=#55ffff>组合初始湿重: <b>{maxWet:F2} 吨</b> | 最终干重: <b>{minDry:F2} 吨</b> | 总推进剂: <b>{sumFuel:F2} 吨</b> | 总设计ΔV: <b>{sumDv:F0} m/s</b></color>", labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 3: RO Engine & Tank
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("🔥 RealismOverhaul 推进系统动力学", subHeaderStyle);
                if (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) {
                    var eng = cachedEngines[selectedEngineIndex];
                    GUILayout.Label($"主发动机: <b>{eng.title}</b> | 真空推力: <b>{eng.thrustKN:F1} kN</b> | 真空比冲: <b>{eng.ispVac:F0} s</b>", labelValueStyle);
                    GUILayout.Label($"推进剂组元: <color=#55ffff>{eng.propellantDesc}</color>", labelValueStyle);
                    GUILayout.Label($"推进剂混合密度: <b>{eng.fuelDensityKgM3:F1} kg/m³</b> | 储罐容积: <b>{eng.tankVolumeM3:F3} m³</b> | 调节压力: <b>{eng.tankPressureKPa:F0} kPa</b>", labelValueStyle);
                } else {
                    GUILayout.Label("使用通用动力学推力模型 (50.0 kN, Isp 310 s)", labelValueStyle);
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 4: GMAT Force Models
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("🌌 GMAT 动力学模型与传播子配置", subHeaderStyle);
                GUILayout.Label("• <b>CislunarForceModel</b>: 中心天体 Earth, 质点引力 {Earth, Luna, Sun, Jupiter}, 太阳光压 SRP 开启", labelValueStyle);
                GUILayout.Label("• <b>LunarNRHOForceModel</b>: 中心天体 Luna, 质点引力 {Luna, Earth, Sun, Jupiter}, 太阳光压 SRP 开启", labelValueStyle);
                GUILayout.Label("• <b>LEOProp</b>: 地球近地轨道RungeKutta89积分器 (最大步长 2700s)", labelValueStyle);
                GUILayout.Label("• <b>CislunarProp</b>: 地月转移高效率RungeKutta89积分器 (最大步长 21600s / 6小时)", labelValueStyle);
                GUILayout.Label("• <b>LunarNRHOProp</b>: 月球NRHO高精度Halo晕轮轨道积分器 (最大步长 10800s / 3小时)", labelValueStyle);
                GUILayout.EndVertical();
            } else {
                GUILayout.Label("无活跃飞船数据。", labelValueStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button("✕ 关闭快照", secondaryButtonStyle, GUILayout.Height(28))) {
                showCraftDetailsWindow = false;
            }
            DrawResizeHandle(ref craftDetailsWindowRect, 600f, 450f);
            GUI.DragWindow(new Rect(0, 0, craftDetailsWindowRect.width - 40, 26));
        }


        // =========================================================================
        // PURE ASTRODYNAMICS & MULTI-BODY MANEUVER TIMING ENGINE (ZERO HARDCODING)
        // 1. Primary Authority: GMAT N-body numerical integration (valid across all celestial bodies & orbits).
        // 2. Exact Tsiolkovsky Equation: dynamically integrates vehicle mass flow, thrust, and Isp.
        // 3. Universal Centroid Law: t_start = T_event - (t_duration / 2) aligns the burn energy center.
        // 4. Absolute Solve Epoch Anchor: eliminates 100% of UI delay/time drift across all mission phases.
        // =========================================================================

        private void DrawCoordSystemWindow(int windowID) {
            Vessel v = FlightGlobals.ActiveVessel;
            List<string> allBodies = GMATParameterRegistry.GetAllBodies(v);

            // Title Bar
            GUILayout.BeginHorizontal();
            string winTitle = editingCoordSystem.isBuiltIn ? "<b>📐 官方坐标系统属性 (Coordinate System)</b>" : "<b>📐 New Coordinate System (新建/修改空间坐标系)</b>";
            GUILayout.Label(winTitle, headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                showCoordSystemWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 1. Coordinate System Name
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.Label("<color=#e2e8f0>Coordinate System Name</color>", labelValueStyle);
            GUI.enabled = !editingCoordSystem.isBuiltIn;
            editingCoordSystem.name = GUILayout.TextField(editingCoordSystem.name ?? "", GUILayout.Height(22));
            GUI.enabled = true;
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 2. Origin Dropdown
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#e2e8f0>Origin</color>", labelValueStyle, GUILayout.Width(60));
            if (GUILayout.Button($"{editingCoordSystem.origin} ▾", secondaryButtonStyle, GUILayout.Width(200), GUILayout.Height(22))) {
                showCsOriginPicker = !showCsOriginPicker;
                showCsAxesPicker = false;
                showCsPrimaryPicker = false;
                showCsSecondaryPicker = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (showCsOriginPicker) {
                GUILayout.Space(2);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#94a3b8>选择参考原点 (Origin):</color>", labelValueStyle);
                GUILayout.BeginHorizontal();
                int oCount = 0;
                for (int b = 0; b < allBodies.Count && oCount < 6; b++) {
                    string bName = allBodies[b];
                    if (GUILayout.Button(bName, (editingCoordSystem.origin == bName) ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                        editingCoordSystem.origin = bName;
                        showCsOriginPicker = false;
                    }
                    oCount++;
                }
                if (GUILayout.Button("Sat", (editingCoordSystem.origin == "Sat") ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    editingCoordSystem.origin = "Sat";
                    showCsOriginPicker = false;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(8);

            // 3. Axes Group Box (1:1 with NASA GMAT)
            GUILayout.BeginVertical(targetGroupBoxStyle);
            GUILayout.Label("<color=#38bdf8><b>Axes (轴向定义)</b></color>", subHeaderStyle);
            GUILayout.Space(4);

            // Axes Type
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Type", labelValueStyle, GUILayout.Width(50));
            if (GUILayout.Button($"{editingCoordSystem.axes} ▾", secondaryButtonStyle, GUILayout.Width(220), GUILayout.Height(22))) {
                showCsAxesPicker = !showCsAxesPicker;
                showCsOriginPicker = false;
                showCsPrimaryPicker = false;
                showCsSecondaryPicker = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (showCsAxesPicker) {
                GUILayout.Space(2);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#94a3b8>选择官方轴向定义 (Axes Type):</color>", labelValueStyle);
                for (int a = 0; a < GMATParameterRegistry.AllowedAxisTypes.Count; a++) {
                    string aType = GMATParameterRegistry.AllowedAxisTypes[a];
                    bool isSel = (editingCoordSystem.axes == aType);
                    string desc = (aType == "MJ2000Eq") ? "J2000 赤道惯性系" :
                                 (aType == "MJ2000Ec") ? "J2000 黄道惯性系" :
                                 (aType == "ICRF") ? "国际天球参考系" :
                                 (aType == "ObjectReferenced") ? "双天体旋转随动系 (R, V, N)" :
                                 (aType == "BodyInertial") ? "中心天体极轴惯性系" :
                                 (aType == "BodyFixed") ? "中心天体固连自转系" :
                                 (aType == "BodySpinSun") ? "天体自转-太阳对齐系" :
                                 (aType == "Topocentric") ? "站心坐标系" : aType;
                    if (GUILayout.Button($"<b>{aType}</b> - {desc}", isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                        editingCoordSystem.axes = aType;
                        showCsAxesPicker = false;
                    }
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);

            // Primary & Secondary (Active only when ObjectReferenced or BodySpinSun)
            bool reqObjects = (editingCoordSystem.axes == "ObjectReferenced" || editingCoordSystem.axes == "BodySpinSun");
            GUI.enabled = reqObjects;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Primary", labelValueStyle, GUILayout.Width(65));
            if (GUILayout.Button(reqObjects ? $"{editingCoordSystem.primary} ▾" : "--", secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                showCsPrimaryPicker = !showCsPrimaryPicker;
                showCsSecondaryPicker = false;
            }

            GUILayout.Space(10);
            GUILayout.Label("Secondary", labelValueStyle, GUILayout.Width(75));
            if (GUILayout.Button(reqObjects ? $"{editingCoordSystem.secondary} ▾" : "--", secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                showCsSecondaryPicker = !showCsSecondaryPicker;
                showCsPrimaryPicker = false;
            }
            GUILayout.EndHorizontal();

            if (showCsPrimaryPicker && reqObjects) {
                GUILayout.BeginHorizontal();
                GUILayout.Space(65);
                for (int b = 0; b < allBodies.Count && b < 4; b++) {
                    string pName = allBodies[b];
                    if (GUILayout.Button(pName, (editingCoordSystem.primary == pName) ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                        editingCoordSystem.primary = pName;
                        showCsPrimaryPicker = false;
                    }
                }
                GUILayout.EndHorizontal();
            }

            if (showCsSecondaryPicker && reqObjects) {
                GUILayout.BeginHorizontal();
                GUILayout.Space(280);
                for (int b = 0; b < allBodies.Count && b < 4; b++) {
                    string sName = allBodies[b];
                    if (GUILayout.Button(sName, (editingCoordSystem.secondary == sName) ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                        editingCoordSystem.secondary = sName;
                        showCsSecondaryPicker = false;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUI.enabled = true;

            GUILayout.Space(6);

            // A1MJD Epoch
            GUILayout.BeginHorizontal();
            GUILayout.Label("A1MJD Epoch", labelValueStyle, GUILayout.Width(90));
            editingCoordSystem.epoch = GUILayout.TextField(editingCoordSystem.epoch ?? "", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Triad: X, Y, Z (Active when ObjectReferenced)
            bool reqTriad = (editingCoordSystem.axes == "ObjectReferenced");
            GUI.enabled = reqTriad;

            GUILayout.BeginHorizontal();
            GUILayout.Label("X:", labelValueStyle, GUILayout.Width(20));
            if (GUILayout.Button(editingCoordSystem.xAxis ?? "R", secondaryButtonStyle, GUILayout.Width(60), GUILayout.Height(22))) {
                CycleAxisVector(ref editingCoordSystem.xAxis);
            }

            GUILayout.Space(10);
            GUILayout.Label("Y:", labelValueStyle, GUILayout.Width(20));
            if (GUILayout.Button(editingCoordSystem.yAxis ?? "None", secondaryButtonStyle, GUILayout.Width(60), GUILayout.Height(22))) {
                CycleAxisVector(ref editingCoordSystem.yAxis);
            }

            GUILayout.Space(10);
            GUILayout.Label("Z:", labelValueStyle, GUILayout.Width(20));
            if (GUILayout.Button(editingCoordSystem.zAxis ?? "N", secondaryButtonStyle, GUILayout.Width(60), GUILayout.Height(22))) {
                CycleAxisVector(ref editingCoordSystem.zAxis);
            }
            GUILayout.EndHorizontal();

            GUI.enabled = true;

            GUILayout.EndVertical();

            // Help Drawer (if toggled)
            if (showCsHelpTooltip) {
                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#38bdf8><b>NASA GMAT 坐标系统官方规范:</b></color>", labelValueStyle);
                GUILayout.Label("• <b>MJ2000Eq / MJ2000Ec</b>: IAU-1976/FK5 标准赤道/黄道惯性系，用于常规轨道积分；", labelValueStyle);
                GUILayout.Label("• <b>ObjectReferenced</b>: 双天体轨道随动系。X轴沿连线 R，Z轴沿轨道法向 N，Y轴正交补全。地月拉格朗日点与 NRHO 轨道设计必备；", labelValueStyle);
                GUILayout.Label("• <b>BodyInertial / BodyFixed</b>: 天体极轴惯性或固连系，用于近月、近火着陆与近心点打靶。", labelValueStyle);
                GUILayout.EndVertical();
            }

            GUILayout.FlexibleSpace();

            // Bottom Buttons (OK, Cancel, Help)
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("OK", actionButtonStyle, GUILayout.Width(90), GUILayout.Height(26))) {
                if (string.IsNullOrEmpty(editingCoordSystem.name)) {
                    ScreenMessages.PostScreenMessage("❌ 坐标系名称不能为空！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    GMATParameterRegistry.AddOrUpdateCoordinateSystem(editingCoordSystem);
                    showCoordSystemWindow = false;
                    ScreenMessages.PostScreenMessage($"✅ 成功注册空间参考系: {editingCoordSystem.name}！补全提示已实时刷新！", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Cancel", secondaryButtonStyle, GUILayout.Width(90), GUILayout.Height(26))) {
                showCoordSystemWindow = false;
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Help", secondaryButtonStyle, GUILayout.Width(70), GUILayout.Height(26))) {
                showCsHelpTooltip = !showCsHelpTooltip;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUI.DragWindow(new Rect(0, 0, coordSystemWindowRect.width, 30));
        }

        private void CycleAxisVector(ref string cur) {
            string[] vecOptions = new string[] { "R", "-R", "V", "-V", "N", "-N", "None" };
            int idx = Array.IndexOf(vecOptions, cur);
            if (idx < 0) idx = 0;
            idx = (idx + 1) % vecOptions.Length;
            cur = vecOptions[idx];
        }
    }
}
