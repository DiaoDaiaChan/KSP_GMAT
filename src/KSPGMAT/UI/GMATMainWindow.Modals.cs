using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;

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
            GUILayout.Label(I18n.T("modal_param_title"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showParamPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Search Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("modal_search"), labelValueStyle, GUILayout.Width(100));
            paramPickerSearchQuery = GUILayout.TextField(paramPickerSearchQuery, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(I18n.T("btn_clear"), secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(20))) {
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
            GUILayout.Label(I18n.T("modal_obj_type_header"), subHeaderStyle);
            psdScrollObjType = GUILayout.BeginScrollView(psdScrollObjType);

            string[] objTypes = new string[] { "Spacecraft", "Variable", "ChemicalThruster", "ChemicalTank", "ImpulsiveBurn", "CoordinateSystem", "Array" };
            string[] objTypeLabels = new string[] { I18n.T("objtype_spacecraft"), I18n.T("objtype_variable"), I18n.T("objtype_thruster"), I18n.T("objtype_tank"), I18n.T("objtype_impulsive"), I18n.T("objtype_coord"), I18n.T("objtype_array") };

            for (int t = 0; t < objTypes.Length; t++) {
                bool isCurType = (psdSelectedObjectTypeIndex == t && string.IsNullOrEmpty(psdSelectedHardwareName));
                if (GUILayout.Button(objTypeLabels[t], isCurType ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    psdSelectedObjectTypeIndex = t;
                    psdSelectedHardwareName = "";
                }
            }

            // Attached Hardware Section (Scan actual craft + sequence)
            GUILayout.Space(6);
            GUILayout.Label(I18n.T("modal_attached_hw"), labelValueStyle);

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
                if (GUILayout.Button(tName, isCurHw ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
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
                if (GUILayout.Button(tName, isCurHw ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
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
                propList.Add(new KeyValuePair<string, string>("ThrustDirection1", I18n.T("prop_ThrustDirection1")));
                propList.Add(new KeyValuePair<string, string>("ThrustDirection2", I18n.T("prop_ThrustDirection2")));
                propList.Add(new KeyValuePair<string, string>("ThrustDirection3", I18n.T("prop_ThrustDirection3")));
                propList.Add(new KeyValuePair<string, string>("C1", I18n.T("prop_C1")));
                propList.Add(new KeyValuePair<string, string>("K1", I18n.T("prop_K1")));
                propList.Add(new KeyValuePair<string, string>("DutyCycle", I18n.T("prop_DutyCycle")));
                propList.Add(new KeyValuePair<string, string>("ThrustScaleFactor", I18n.T("prop_ThrustScaleFactor")));
                propList.Add(new KeyValuePair<string, string>("GravitationalAccel", I18n.T("prop_GravitationalAccel")));
            } else if (curObjTypeName == "ChemicalTank" || (!string.IsNullOrEmpty(psdSelectedHardwareName) && psdSelectedHardwareName.Contains("Tank"))) {
                // Tank Properties
                propList.Add(new KeyValuePair<string, string>("FuelMass", I18n.T("prop_FuelMass")));
                propList.Add(new KeyValuePair<string, string>("FuelDensity", I18n.T("prop_FuelDensity")));
                propList.Add(new KeyValuePair<string, string>("Pressure", I18n.T("prop_Pressure")));
                propList.Add(new KeyValuePair<string, string>("Volume", I18n.T("prop_Volume")));
                propList.Add(new KeyValuePair<string, string>("Temperature", I18n.T("prop_Temperature")));
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
                    propList.Add(new KeyValuePair<string, string>(declaredVars[dv], I18n.T("prop_user_var_desc")));
                }
            } else if (curObjTypeName == "ImpulsiveBurn") {
                propList.Add(new KeyValuePair<string, string>("Element1", I18n.T("prop_Element1")));
                propList.Add(new KeyValuePair<string, string>("Element2", I18n.T("prop_Element2")));
                propList.Add(new KeyValuePair<string, string>("Element3", I18n.T("prop_Element3")));
            } else if (curObjTypeName == "CoordinateSystem") {
                propList.Add(new KeyValuePair<string, string>("EarthMJ2000Eq", I18n.T("prop_EarthMJ2000Eq")));
                propList.Add(new KeyValuePair<string, string>("Local", I18n.T("prop_Local")));
                propList.Add(new KeyValuePair<string, string>("MoonInertial", I18n.T("prop_MoonInertial")));
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
                if (GUILayout.Button($"-> {propKey}", isSelected ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(170), GUILayout.Height(20))) {
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
            GUILayout.Label(I18n.T("modal_selected_values"), subHeaderStyle);
            GUILayout.Space(4);

            GUILayout.Label(I18n.T("modal_full_expr"), labelValueStyle);
            psdPendingSelectedValue = GUILayout.TextField(psdPendingSelectedValue, GUILayout.Height(26));

            GUILayout.Space(8);
            string i18nSummary = GMATParameterRegistry.GetParamBadgeI18n(psdPendingSelectedValue);
            if (!string.IsNullOrEmpty(i18nSummary)) {
                GUILayout.Label($"<color=#34d399><b>[{i18nSummary}]</b></color>", labelValueStyle);
            }

            GUILayout.FlexibleSpace();

            // [OK] & [Cancel] Buttons (Image 3)
            if (GUILayout.Button(I18n.T("btn_ok_apply"), successButtonStyle, GUILayout.Height(28))) {
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
                    } else if (paramPickerMode == "stop" || paramPickerMode.StartsWith("prop_stop")) {
                        int cIdx = 0;
                        if (paramPickerMode.StartsWith("prop_stop_")) {
                            int.TryParse(paramPickerMode.Substring("prop_stop_".Length), out cIdx);
                        }
                        if (s.stop_conditions != null && cIdx < s.stop_conditions.Count) {
                            s.stop_conditions[cIdx].parameter = psdPendingSelectedValue;
                        }
                        pendingFocusControl = $"Field_PropStop_{paramPickerTargetStepIndex}_{cIdx}";
                        GUI.FocusControl(pendingFocusControl);
                    } else if (paramPickerMode == "minimize") {
                        s.minimize_variable = psdPendingSelectedValue;
                        pendingFocusControl = $"Field_MinVar_{paramPickerTargetStepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    } else if (paramPickerMode == "nlc") {
                        s.constraint_lhs = psdPendingSelectedValue;
                        pendingFocusControl = $"Field_NlcLhs_{paramPickerTargetStepIndex}";
                        GUI.FocusControl(pendingFocusControl);
                    }
                }
                showParamPickerModal = false;
            }

            GUILayout.Space(4);
            if (GUILayout.Button(I18n.T("btn_cancel"), secondaryButtonStyle, GUILayout.Height(24))) {
                showParamPickerModal = false;
            }

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawOldParamPickerWindow_Deprecated(int windowID) {
            bool isPropStop = (paramPickerMode == "prop_stop" || paramPickerMode == "stop");
            string cb = GMATStateManager.Current?.Spacecraft?.CentralBody ?? "Earth";
            if (cb == "Kerbin") cb = "Earth"; else if (cb == "Mun") cb = "Luna";

            GUILayout.BeginHorizontal();
            string pickerTitle = isPropStop ? I18n.T("param_title_stop") :
                                 (paramPickerMode == "achieve") ? I18n.T("param_title_achieve") :
                                 I18n.T("param_title_general");
            GUILayout.Label($"<b>{pickerTitle}</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showParamPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Search Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("modal_search"), labelValueStyle, GUILayout.Width(95));
            paramPickerSearchQuery = GUILayout.TextField(paramPickerSearchQuery);
            if (GUILayout.Button(I18n.T("btn_clear"), secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(20))) {
                paramPickerSearchQuery = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            string[] categoryOptions = isPropStop ? GMATParameterRegistry.PropStopCategories : GMATParameterRegistry.Categories;
            if (paramPickerCategoryIndex >= categoryOptions.Length) paramPickerCategoryIndex = 0;

            // Category Filter Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("param_category_label"), labelValueStyle, GUILayout.Width(65));
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
                    GUILayout.Label(I18n.T("param_no_match_stop"), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                } else {
                    for (int p = 0; p < stopItems.Count; p++) {
                        var item = stopItems[p];
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button($"+ {item.Key}", secondaryButtonStyle, GUILayout.Width(180), GUILayout.Height(22))) {
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
                    GUILayout.Label(I18n.T("param_no_match_param"), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                } else {
                    for (int p = 0; p < paramItems.Count; p++) {
                        var item = paramItems[p];
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button($"+ {item.Name}", secondaryButtonStyle, GUILayout.Width(180), GUILayout.Height(22))) {
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
                        GUILayout.Label($"<b>{item.Name}</b>{unitBadge} - {descText} {I18n.Format("param_example", item.Example)}", labelValueStyle);
                        GUILayout.EndHorizontal();
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_done_close"), secondaryButtonStyle, GUILayout.Width(120), GUILayout.Height(24))) {
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
            GUILayout.Label(I18n.T("targeter_title"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showTargeterSetupModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Solver Instance Switcher Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("targeter_current"), labelValueStyle, GUILayout.Width(75));

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
            if (GUILayout.Button(I18n.T("btn_new_solver"), actionButtonStyle, GUILayout.Height(24))) {
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
                ScreenMessages.PostScreenMessage(I18n.Format("targeter_created", newName), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            SolverConfig activeSolver = solverConfigs.ContainsKey(wizardDcName) ? solverConfigs[wizardDcName] : new SolverConfig();

            if (!activeSolver.isDefault) {
                GUILayout.Space(6);
                if (GUILayout.Button(I18n.T("btn_delete"), deleteButtonStyle, GUILayout.Height(24))) {
                    GMATParameterRegistry.DeleteSolver(wizardDcName);
                    wizardDcName = "DefaultDC";
                    ScreenMessages.PostScreenMessage(I18n.T("targeter_deleted"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginVertical(cardBoxStyle);

            // Section 1: 求解器命名与标识
            GUILayout.Label(I18n.T("targeter_s1"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("targeter_dc_name"), labelValueStyle, GUILayout.Width(140));
            activeSolver.name = GUILayout.TextField(activeSolver.name ?? wizardDcName, GUILayout.Width(160));
            GUILayout.Label(I18n.T("targeter_dc_hint"), labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Section 2: 数学优化算法 (Algorithm)
            GUILayout.Label(I18n.T("targeter_s2"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            for (int a = 0; a < WizardAlgorithmNames.Length; a++) {
                bool isSel = (activeSolver.algorithm == WizardAlgorithmNames[a]);
                if (GUILayout.Button(GetWizardAlgorithmLabels()[a], isSel ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(24))) {
                    activeSolver.algorithm = WizardAlgorithmNames[a];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Section 3: 导数计算差分方式 (DerivativeMethod)
            GUILayout.Label(I18n.T("targeter_s3"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            for (int d = 0; d < WizardDerivativeNames.Length; d++) {
                bool isSel = (activeSolver.derivativeMethod == WizardDerivativeNames[d]);
                if (GUILayout.Button(GetWizardDerivativeLabels()[d], isSel ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(24))) {
                    activeSolver.derivativeMethod = WizardDerivativeNames[d];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Section 4: 控制参数与模式 (Modes & Parameters)
            GUILayout.Label(I18n.T("targeter_s4"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("targeter_solve_mode"), labelValueStyle, GUILayout.Width(75));
            for (int sm = 0; sm < WizardSolveModeNames.Length; sm++) {
                bool isSel = (activeSolver.solveMode == WizardSolveModeNames[sm]);
                if (GUILayout.Button(GetWizardSolveModeLabels()[sm], isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    activeSolver.solveMode = WizardSolveModeNames[sm];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("targeter_exit_mode"), labelValueStyle, GUILayout.Width(75));
            for (int em = 0; em < WizardExitModeNames.Length; em++) {
                bool isSel = (activeSolver.exitMode == WizardExitModeNames[em]);
                if (GUILayout.Button(GetWizardExitModeLabels()[em], isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    activeSolver.exitMode = WizardExitModeNames[em];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("targeter_max_iter"), labelValueStyle, GUILayout.Width(190));
            activeSolver.maxIterations = DrawDelayedIntField("modal_solver_max_iter", activeSolver.maxIterations, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70));

            GUILayout.Space(15);
            activeSolver.showProgress = GUILayout.Toggle(activeSolver.showProgress, I18n.T("targeter_show_progress"), GUILayout.Width(160));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            // Bottom Buttons
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.T("btn_save_solver"), actionButtonStyle, GUILayout.Width(180), GUILayout.Height(28))) {
                GMATParameterRegistry.AddOrUpdateSolver(activeSolver);
                if (editingTargetStepIndex >= 0 && editingTargetStepIndex < missionSequence.Count) {
                    var step = missionSequence[editingTargetStepIndex];
                    step.dc_name = activeSolver.name;
                    step.dc_algorithm = activeSolver.algorithm;
                    step.dc_max_iter = activeSolver.maxIterations;
                }
                showTargeterSetupModal = false;
                ScreenMessages.PostScreenMessage(I18n.Format("targeter_saved", activeSolver.name), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            GUILayout.Space(10);
            if (GUILayout.Button(I18n.T("btn_close"), secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(28))) {
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
                ScreenMessages.PostScreenMessage(I18n.Format("targeter_updated", dcName), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                if (wizardTemplateIndex == 0) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = I18n.T("label_target_custom"),
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.T("label_end_custom") });
                } else if (wizardTemplateIndex == 1) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = I18n.T("label_target_apogee"),
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "burntime", vary_initial = 150.0, vary_lower = 0.0, vary_upper = 1000.0, vary_max_step = 2.0, label = I18n.T("label_vary_burntime") });
                    missionSequence.Add(new MissionStep { type = "FiniteBurn", propagator = "LEOProp", burn_use_vary_var = true, burn_vary_var_name = "burntime", label = I18n.T("label_burn_prograde") });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis", label = I18n.T("misc_default_prop_ap") });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Earth.Altitude", achieve_val = 35786.0, achieve_tolerance = 0.1, label = I18n.T("label_achieve_ap") });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.T("label_end_apogee") });
                } else if (wizardTemplateIndex == 2) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = I18n.T("label_target_geo"),
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "burntime", vary_initial = 200.0, vary_lower = 0.0, vary_upper = 2000.0, vary_max_step = 2.0, label = I18n.T("label_vary_burntime") });
                    missionSequence.Add(new MissionStep { type = "FiniteBurn", propagator = "LEOProp", burn_use_vary_var = true, burn_vary_var_name = "burntime", label = I18n.T("label_burn_geo") });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis", label = I18n.T("misc_default_prop_ap") });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Earth.OrbitPeriod", achieve_val = 86164.0, achieve_tolerance = 1.0, label = I18n.T("label_achieve_period") });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.T("label_end_geo") });
                } else if (wizardTemplateIndex == 3) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = I18n.T("label_target_tli"),
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "burntime", vary_initial = 350.0, vary_lower = 0.0, vary_upper = 3000.0, vary_max_step = 2.0, label = I18n.T("label_vary_tli") });
                    missionSequence.Add(new MissionStep { type = "FiniteBurn", propagator = "LEOProp", burn_use_vary_var = true, burn_vary_var_name = "burntime", label = I18n.T("label_burn_tli") });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "CislunarProp", stop_type = "Days", duration = 4.5, label = I18n.T("label_prop_luna") });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Luna.RMAG", achieve_val = 3229.0, achieve_tolerance = 5.0, label = I18n.T("label_achieve_perilune") });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.T("label_end_tli") });
                } else if (wizardTemplateIndex == 4) {
                    missionSequence.Add(new MissionStep {
                        type = "Target", label = I18n.T("label_target_inc"),
                        dc_name = dcName, dc_algorithm = dcAlg, dc_derivative_method = dcDm,
                        solve_mode = solveMode, exit_mode = exitMode, dc_max_iter = maxIter
                    });
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = "Maneuver1.Element2", vary_initial = 100.0, vary_lower = -1000.0, vary_upper = 1000.0, vary_max_step = 5.0, label = I18n.T("label_vary_normal") });
                    missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", dv_v = 0.0, dv_n = 100.0, dv_b = 0.0, label = I18n.T("label_burn_normal") });
                    missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Node", label = I18n.T("label_prop_node") });
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = "Earth.INC", achieve_val = 0.0, achieve_tolerance = 0.01, label = I18n.T("label_achieve_inc") });
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.T("label_end_inc") });
                }
                ScreenMessages.PostScreenMessage(I18n.Format("targeter_group_created", dcName), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void DrawSimulationImageWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("simimg_title"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showSimulationImageWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // View Mode Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("simimg_switch_view"), labelValueStyle, GUILayout.Width(65));
            string[] modes = new string[] { "dual", "planar", "target" };
            string[] modeLabels = new string[] { I18n.T("view_dual"), I18n.T("view_planar"), I18n.T("view_target") };

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
            if (GUILayout.Button(I18n.T("btn_render"), actionButtonStyle, GUILayout.Height(26))) {
                RerenderSimulationImage();
            }
            GUILayout.Label(I18n.ColorFormat("simimg_status", imageStatusText), labelValueStyle);
            GUILayout.EndHorizontal();

            DrawResizeHandle(ref simulationImageWindowRect, 680f, 480f);
            GUI.DragWindow(new Rect(0, 0, simulationImageWindowRect.width - 40, 26));
        }

        private void RerenderSimulationImage() {
            imageStatusText = I18n.T("simimg_rerender_req");
            string bodyName = GMATStateManager.Current?.Spacecraft?.CentralBody ?? "Earth";
            if (latestResult != null && latestResult.trajectory != null && latestResult.trajectory.Count > 0) {
                simulationTexture = Service.GMATOrbitCanvas.RenderTrajectoryTexture(latestResult.trajectory, bodyName);
                imageStatusText = I18n.T("simimg_status_ready");
            } else {
                imageStatusText = I18n.T("img_not_ready");
            }
        }

        private void DrawPropagatorConfigWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("propcfg_title"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showPropagatorConfigWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Propagator Selector & Management Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("propcfg_current"), labelValueStyle, GUILayout.Width(75));

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
            if (GUILayout.Button(I18n.T("btn_new_prop"), actionButtonStyle, GUILayout.Height(24))) {
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
                ScreenMessages.PostScreenMessage(I18n.Format("propcfg_created", newName), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            PropagatorConfig cfg = propConfigs.ContainsKey(selectedPropagatorKey) ? propConfigs[selectedPropagatorKey] : new PropagatorConfig();

            if (!cfg.isDefault) {
                GUILayout.Space(6);
                if (GUILayout.Button(I18n.T("btn_delete"), deleteButtonStyle, GUILayout.Height(24))) {
                    GMATParameterRegistry.DeletePropagator(selectedPropagatorKey);
                    selectedPropagatorKey = "LEOProp";
                    ScreenMessages.PostScreenMessage(I18n.T("propcfg_deleted"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            float propScrollH = Mathf.Max(280f, propagatorConfigWindowRect.height - 150f);
            scrollPosPropConfig = GUILayout.BeginScrollView(scrollPosPropConfig, cardBoxStyle, GUILayout.Height(propScrollH));

            // Section 1: 积分器动力学内核 (Integrator Core - Official GMAT Integrators)
            GUILayout.Label(I18n.T("propcfg_s1"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("propcfg_integrator"), labelValueStyle, GUILayout.Width(80));

            string[] integrators = new string[] { "RungeKutta89", "PrinceDormand78", "RungeKutta56", "RungeKutta68", "AdamsBashforthMoulton" };
            string[] integLabels = new string[] { I18n.T("integ_rk89"), I18n.T("integ_pd78"), I18n.T("integ_rk56"), I18n.T("integ_rk68"), I18n.T("integ_abm") };

            for (int it = 0; it < integrators.Length; it++) {
                bool isCur = (cfg.integratorType == integrators[it]);
                if (GUILayout.Button(integLabels[it], isCur ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(22))) {
                    cfg.integratorType = integrators[it];
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("propcfg_init_step"), labelValueStyle, GUILayout.Width(170));
            cfg.initialStepSize = DrawDelayedNumericField("modal_prop_init_step", cfg.initialStepSize, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), "F1");

            GUILayout.Label(I18n.T("propcfg_accuracy"), labelValueStyle, GUILayout.Width(140));
            cfg.accuracy = DrawDelayedNumericField("modal_prop_acc", (cfg.accuracy > 1e-20 ? cfg.accuracy : 1e-11), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(80), "E2");

            GUILayout.Label(I18n.T("propcfg_min_step"), labelValueStyle, GUILayout.Width(130));
            cfg.minStep = DrawDelayedNumericField("modal_prop_min_step", cfg.minStep, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(60), "F3");

            GUILayout.Label(I18n.T("propcfg_max_step"), labelValueStyle, GUILayout.Width(130));
            cfg.maxStep = DrawDelayedNumericField("modal_prop_max_step", cfg.maxStep, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), "F1");
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Section 2: 引力场与多体引力摄动 (Gravity Field & Dynamic Point Masses)
            GUILayout.Label(I18n.T("propcfg_s2"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("propcfg_central_body"), labelValueStyle, GUILayout.Width(130));
            Vessel v = FlightGlobals.ActiveVessel;
            List<string> bodies = GMATParameterRegistry.GetAllBodies(v);
            for (int b = 0; b < bodies.Count && b < 8; b++) {
                string bName = bodies[b];
                bool isCenter = (cfg.centralBody == bName);
                if (GUILayout.Button(bName, isCenter ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.centralBody = bName;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            // 高阶重力场谐波势模型 (Degree x Order)
            GUILayout.BeginHorizontal();
            GUILayout.Label("引力场谐波势:", labelValueStyle, GUILayout.Width(130));
            var gravModels = GMATSchemaRegistry.Instance.GetGravityModelsForBody(cfg.centralBody);
            string curGm = !string.IsNullOrEmpty(cfg.gravityModel) ? cfg.gravityModel : "PointMass";
            for (int gm = 0; gm < gravModels.Count; gm++) {
                string gName = gravModels[gm];
                bool isCurG = string.Equals(curGm, gName, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(gName, isCurG ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.gravityModel = gName;
                    if (gName == "PointMass") {
                        cfg.gravityDegree = 0;
                        cfg.gravityOrder = 0;
                    } else if (cfg.gravityDegree == 0) {
                        cfg.gravityDegree = 4;
                        cfg.gravityOrder = 4;
                    }
                }
            }
            if (!string.Equals(cfg.gravityModel, "PointMass", StringComparison.OrdinalIgnoreCase)) {
                GUILayout.Space(8);
                GUILayout.Label("阶数:", labelValueStyle, GUILayout.Width(35));
                cfg.gravityDegree = DrawIntField("Deg", cfg.gravityDegree, 10, 35);
                cfg.gravityOrder = DrawIntField("Ord", cfg.gravityOrder, 10, 35);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            // 多体引力点质量 (PointMasses) 动态天体解耦多选
            GUILayout.BeginHorizontal();
            GUILayout.Label("多体摄动天体:", labelValueStyle, GUILayout.Width(130));
            for (int b = 0; b < bodies.Count; b++) {
                string bName = bodies[b];
                bool hasPm = cfg.HasPointMass(bName);
                if (GUILayout.Button(hasPm ? $"✓ {bName}" : bName, hasPm ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.SetPointMass(bName, !hasPm);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Section 3: 大气与环境扰动 (Atmosphere & SRP & Relativistic Corrections)
            GUILayout.Label(I18n.T("propcfg_s3"), subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("propcfg_drag_model"), labelValueStyle, GUILayout.Width(110));
            var atmos = GMATSchemaRegistry.Instance.GetAtmosphereModelsForBody(cfg.centralBody);
            string curAtm = !string.IsNullOrEmpty(cfg.dragAtmosphereModel) ? cfg.dragAtmosphereModel : "None";
            for (int a = 0; a < atmos.Count; a++) {
                bool isAtmo = string.Equals(curAtm, atmos[a], StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(atmos[a], isAtmo ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.dragModel = atmos[a];
                    cfg.dragAtmosphereModel = atmos[a];
                }
            }

            GUILayout.Space(15);
            cfg.enableSRP = GUILayout.Toggle(cfg.enableSRP, I18n.T("propcfg_srp"), GUILayout.Width(120));
            cfg.enableRelativity = GUILayout.Toggle(cfg.enableRelativity, I18n.T("propcfg_relativity"), GUILayout.Width(120));

            // 误差控制模式
            GUILayout.Space(10);
            GUILayout.Label("误差控制:", labelValueStyle, GUILayout.Width(65));
            var errModes = GMATSchemaRegistry.Instance.GetErrorControlModes();
            string curErr = !string.IsNullOrEmpty(cfg.errorControl) ? cfg.errorControl : "RSSStep";
            for (int em = 0; em < errModes.Count; em++) {
                string emName = errModes[em];
                bool isCurEm = string.Equals(curErr, emName, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(emName, isCurEm ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    cfg.errorControl = emName;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            // Bottom Action Toolbar
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.T("btn_save_prop"), actionButtonStyle, GUILayout.Width(180), GUILayout.Height(28))) {
                GMATParameterRegistry.AddOrUpdatePropagator(cfg);
                propagatorConfigs[cfg.name] = cfg;
                GMATStateManager.Current.Propagators[cfg.name] = cfg;
                ScreenMessages.PostScreenMessage(I18n.Format("propcfg_saved", cfg.name), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            GUILayout.Space(10);
            if (GUILayout.Button(I18n.T("btn_close"), secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(28))) {
                showPropagatorConfigWindow = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, propagatorConfigWindowRect.width, 30));
        }

        private void DrawCraftDetailsWindow(int windowID) {
            Vessel vessel = FlightGlobals.ActiveVessel;
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craftdetails_title"), headerStyle);
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
                GUILayout.Label(I18n.T("craftdetails_s1"), subHeaderStyle);
                GUILayout.Label(I18n.ColorFormat("craftdetails_vessel", vessel.vesselName, vessel.mainBody.name), labelValueStyle);
                double ut = Planetarium.GetUniversalTime();
                GUILayout.Label(I18n.ColorFormat("craftdetails_ut", ut), labelValueStyle);

                Vector3d pos_km, vel_kms;
                PrincipiaInterop.GetVesselStateICRF(vessel, out pos_km, out vel_kms);
                GUILayout.Label(I18n.ColorFormat("craftdetails_icrf_pos", pos_km.x, pos_km.y, pos_km.z, pos_km.magnitude), labelValueStyle);
                GUILayout.Label(I18n.ColorFormat("craftdetails_icrf_vel", vel_kms.x, vel_kms.y, vel_kms.z, vel_kms.magnitude), labelValueStyle);
                if (vessel.orbit != null) {
                    GUILayout.Label(I18n.ColorFormat("craftdetails_kepler", vessel.orbit.semiMajorAxis / 1000.0, vessel.orbit.eccentricity, vessel.orbit.inclination), labelValueStyle);
                    GUILayout.Label(I18n.ColorFormat("craftdetails_apsides", vessel.orbit.PeA / 1000.0, vessel.orbit.ApA / 1000.0), labelValueStyle);
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 2: Staging Stack
                GUILayout.BeginVertical(cardBoxStyle);
                var selStages = GetSelectedStagesList();
                GUILayout.Label(I18n.Format("craftdetails_s2", selStages.Count), subHeaderStyle);
                double sumDv = 0, sumFuel = 0, maxWet = 0, minDry = double.MaxValue;
                foreach (var st in selStages) {
                    sumDv += st.deltaV;
                    sumFuel += st.fuelMassTonnes;
                    if (st.wetMassTonnes > maxWet) maxWet = st.wetMassTonnes;
                    if (st.dryMassTonnes < minDry) minDry = st.dryMassTonnes;
                    GUILayout.Label(I18n.ColorFormat("craftdetails_stage", st.stageNumber, st.deltaV, st.wetMassTonnes, st.fuelMassTonnes, st.dryMassTonnes), labelValueStyle);
                }
                if (minDry == double.MaxValue) minDry = vessel.totalMass * 0.4;
                if (maxWet <= 0) maxWet = vessel.totalMass;
                GUILayout.Space(2);
                GUILayout.Label(I18n.ColorFormat("craftdetails_combined", maxWet, minDry, sumFuel, sumDv), labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 3: RO Engine & Tank
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label(I18n.T("craftdetails_s3"), subHeaderStyle);
                if (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) {
                    var eng = cachedEngines[selectedEngineIndex];
                    GUILayout.Label(I18n.ColorFormat("craftdetails_engine", eng.title, eng.thrustKN, eng.ispVac), labelValueStyle);
                    GUILayout.Label(I18n.ColorFormat("craftdetails_propellant", eng.propellantDesc), labelValueStyle);
                    GUILayout.Label(I18n.ColorFormat("craftdetails_mix", eng.fuelDensityKgM3, eng.tankVolumeM3, eng.tankPressureKPa), labelValueStyle);
                } else {
                    GUILayout.Label(I18n.T("craftdetails_fallback"), labelValueStyle);
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 4: GMAT Force Models
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label(I18n.T("craftdetails_s4"), subHeaderStyle);
                GUILayout.Label(I18n.ColorFormat("craftdetails_fm1"), labelValueStyle);
                GUILayout.Label(I18n.ColorFormat("craftdetails_fm2"), labelValueStyle);
                GUILayout.Label(I18n.T("craftdetails_fm3"), labelValueStyle);
                GUILayout.Label(I18n.T("craftdetails_fm4"), labelValueStyle);
                GUILayout.Label(I18n.T("craftdetails_fm5"), labelValueStyle);
                GUILayout.EndVertical();
            } else {
                GUILayout.Label(I18n.T("no_active_vessel"), labelValueStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button(I18n.T("btn_close_snapshot"), secondaryButtonStyle, GUILayout.Height(28))) {
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
            string winTitle = editingCoordSystem.isBuiltIn ? I18n.T("cs_title_builtin") : I18n.T("cs_title_custom");
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
            GUILayout.Label(I18n.T("cs_name_label"), labelValueStyle);
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
            GUILayout.Label(I18n.T("cs_origin_label"), labelValueStyle, GUILayout.Width(60));
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
                GUILayout.Label(I18n.T("cs_origin_hint"), labelValueStyle);
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
            GUILayout.Label(I18n.T("cs_axes_header"), subHeaderStyle);
            GUILayout.Space(4);

            // Axes Type
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(I18n.T("cs_type_label"), labelValueStyle, GUILayout.Width(50));
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
                GUILayout.Label(I18n.T("cs_axes_hint"), labelValueStyle);
                for (int a = 0; a < GMATParameterRegistry.AllowedAxisTypes.Count; a++) {
                    string aType = GMATParameterRegistry.AllowedAxisTypes[a];
                    bool isSel = (editingCoordSystem.axes == aType);
                    string desc = (aType == "MJ2000Eq") ? I18n.T("axis_MJ2000Eq") :
                                 (aType == "MJ2000Ec") ? I18n.T("axis_MJ2000Ec") :
                                 (aType == "ICRF") ? I18n.T("axis_ICRF") :
                                 (aType == "ObjectReferenced") ? I18n.T("axis_ObjectReferenced") :
                                 (aType == "BodyInertial") ? I18n.T("axis_BodyInertial") :
                                 (aType == "BodyFixed") ? I18n.T("axis_BodyFixed") :
                                 (aType == "BodySpinSun") ? I18n.T("axis_BodySpinSun") :
                                 (aType == "Topocentric") ? I18n.T("axis_Topocentric") : aType;
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
            GUILayout.Label(I18n.T("cs_primary"), labelValueStyle, GUILayout.Width(65));
            if (GUILayout.Button(reqObjects ? $"{editingCoordSystem.primary} ▾" : "--", secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                showCsPrimaryPicker = !showCsPrimaryPicker;
                showCsSecondaryPicker = false;
            }

            GUILayout.Space(10);
            GUILayout.Label(I18n.T("cs_secondary"), labelValueStyle, GUILayout.Width(75));
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
            GUILayout.Label(I18n.T("cs_epoch"), labelValueStyle, GUILayout.Width(90));
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
                GUILayout.Label(I18n.T("cs_help_header"), labelValueStyle);
                GUILayout.Label(I18n.T("cs_help_1"), labelValueStyle);
                GUILayout.Label(I18n.T("cs_help_2"), labelValueStyle);
                GUILayout.Label(I18n.T("cs_help_3"), labelValueStyle);
                GUILayout.EndVertical();
            }

            GUILayout.FlexibleSpace();

            // Bottom Buttons (OK, Cancel, Help)
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.T("btn_ok"), actionButtonStyle, GUILayout.Width(90), GUILayout.Height(26))) {
                if (string.IsNullOrEmpty(editingCoordSystem.name)) {
                    ScreenMessages.PostScreenMessage(I18n.T("cs_name_empty"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    GMATParameterRegistry.AddOrUpdateCoordinateSystem(editingCoordSystem);
                    showCoordSystemWindow = false;
                    ScreenMessages.PostScreenMessage(I18n.Format("cs_registered", editingCoordSystem.name), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUILayout.Space(10);
            if (GUILayout.Button(I18n.T("btn_cancel_word"), secondaryButtonStyle, GUILayout.Width(90), GUILayout.Height(26))) {
                showCoordSystemWindow = false;
            }

            GUILayout.Space(10);
            if (GUILayout.Button(I18n.T("btn_help"), secondaryButtonStyle, GUILayout.Width(70), GUILayout.Height(26))) {
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

        // =========================================================================
        // MGA 行星际天体弹出选择器 (Celestial Body / Planet Picker Modal)
        // =========================================================================
        public void OpenPlanetPicker(int targetType, string title, int flybyIndex = -1) {
            planetPickerTargetType = targetType;
            planetPickerTitle = title;
            planetPickerFlybyIndex = flybyIndex;
            planetPickerSearch = "";
            float pw = Mathf.Clamp(Screen.width * 0.45f, 500f, 680f);
            float ph = Mathf.Clamp(Screen.height * 0.55f, 460f, 620f);
            planetPickerWindowRect = new Rect((Screen.width - pw) / 2, (Screen.height - ph) / 2, pw, ph);
            showPlanetPickerModal = true;
            GUI.BringWindowToFront(982308);
        }

        private void DrawPlanetPickerWindow(int windowID) {
            GUILayout.BeginVertical();

            // Header Title Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Format("mga_picker_title_prefix", planetPickerTitle), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showPlanetPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Search Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("modal_search"), labelValueStyle, GUILayout.Width(100));
            planetPickerSearch = GUILayout.TextField(planetPickerSearch, GUILayout.MinWidth(180), GUILayout.ExpandWidth(true));
            if (!string.IsNullOrEmpty(planetPickerSearch)) {
                if (GUILayout.Button(I18n.T("btn_clear"), secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(20))) {
                    planetPickerSearch = "";
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            var allPlanets = SolarSystemCatalog.GetAvailablePlanets();
            var filtered = string.IsNullOrEmpty(planetPickerSearch) 
                ? allPlanets 
                : allPlanets.FindAll(p => p.Name.IndexOf(planetPickerSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         p.DisplayName.IndexOf(planetPickerSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            planetPickerScrollPos = GUILayout.BeginScrollView(planetPickerScrollPos);

            // 动态分组展示：主要行星 (公转中心为恒星) vs 各母星的卫星系统 vs 次级天体/小行星 (0 硬编码，自适应所有模组)
            string rootStar = SolarSystemCatalog.RootStarName;
            var primaryPlanets = filtered.FindAll(p => !p.IsMoon && !p.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase));
            var parentBodies = SolarSystemCatalog.GetBodiesWithMoons();

            if (primaryPlanets.Count > 0) {
                GUILayout.Label($"<b>🪐 {I18n.T("mga_primary_planets_header", "主要行星 (Primary Planets)")} ({primaryPlanets.Count})</b>", labelValueStyle);
                DrawPlanetGrid(primaryPlanets);
                GUILayout.Space(8);
            }

            for (int pb = 0; pb < parentBodies.Count; pb++) {
                var parent = parentBodies[pb];
                var moons = filtered.FindAll(p => p.IsMoon && p.ParentName.Equals(parent.Name, StringComparison.OrdinalIgnoreCase));
                if (moons.Count > 0) {
                    GUILayout.Label($"<b>🌙 {parent.Icon}{parent.GetDisplayName()} 卫星系统 ({moons.Count})</b>", labelValueStyle);
                    DrawPlanetGrid(moons);
                    GUILayout.Space(8);
                }
            }

            var otherBodies = filtered.FindAll(p => p.IsMoon && !parentBodies.Any(pb => pb.Name.Equals(p.ParentName, StringComparison.OrdinalIgnoreCase)));
            if (otherBodies.Count > 0) {
                GUILayout.Label($"<b>☄️ {I18n.T("mga_other_bodies_header", "小行星与次级天体")} ({otherBodies.Count})</b>", labelValueStyle);
                DrawPlanetGrid(otherBodies);
                GUILayout.Space(8);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("mga_btn_close_cancel"), secondaryButtonStyle, GUILayout.Width(100), GUILayout.Height(24))) {
                showPlanetPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, planetPickerWindowRect.width, 30));
        }

        private void DrawPlanetGrid(List<SolarPlanet> planets) {
            int cols = 2;
            for (int r = 0; r < (planets.Count + cols - 1) / cols; r++) {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < cols; c++) {
                    int idx = r * cols + c;
                    if (idx < planets.Count) {
                        var p = planets[idx];
                        string currentTarget = "";
                        string badge = "";

                        if (planetPickerTargetType == 0) {
                            currentTarget = mgaConfig.DepartureBody;
                            if (p.Name.Equals(mgaConfig.DestinationBody, StringComparison.OrdinalIgnoreCase)) {
                                badge = " <color=#f87171>" + I18n.T("mga_picker_current_dest") + "</color>";
                            }
                        } else if (planetPickerTargetType == 1) {
                            currentTarget = mgaConfig.DestinationBody;
                            if (p.Name.Equals(mgaConfig.DepartureBody, StringComparison.OrdinalIgnoreCase)) {
                                badge = " <color=#f87171>" + I18n.T("mga_picker_current_dep") + "</color>";
                            }
                        } else if (planetPickerTargetType == 2 && planetPickerFlybyIndex >= 0 && planetPickerFlybyIndex < mgaConfig.FlybySequence.Count) {
                            currentTarget = mgaConfig.FlybySequence[planetPickerFlybyIndex];
                        }

                        bool isSelected = p.Name.Equals(currentTarget, StringComparison.OrdinalIgnoreCase);
                        GUIStyle cardStyle = isSelected ? actionButtonStyle : secondaryButtonStyle;

                        double smaAU = p.SMA / 1.495978707e8;
                        string btnText = I18n.Format("mga_planet_btn", p.Icon, p.GetDisplayName() + badge, p.Name, smaAU, p.RadiusKm);

                        if (GUILayout.Button(btnText, cardStyle, GUILayout.Height(42), GUILayout.ExpandWidth(true))) {
                            SelectPlanetFromPicker(p.Name);
                        }
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
        }

        private void SelectPlanetFromPicker(string planetName) {
            if (planetPickerTargetType == 0) {
                // 出发天体
                if (planetName.Equals(mgaConfig.DestinationBody, StringComparison.OrdinalIgnoreCase)) {
                    ScreenMessages.PostScreenMessage(I18n.T("mga_err_same_dep_dest"), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
                mgaConfig.DepartureBody = planetName;
                MGAStateManager.Current.SetDepartureAndDestination(mgaConfig.DepartureBody, mgaConfig.DestinationBody);
                mgaConfig.CentralBody = MGAStateManager.Current.Config.CentralBody;
                if (mgaWorkflowMode == 0) {
                    mgaConfig.FlybySequence = new List<string> { mgaConfig.DestinationBody };
                }
                selectedMgaSolution = null;
                mgaSolutions.Clear();
                RefreshCandidateChains(mgaWorkflowMode == 1);
                RefreshMgaCanvas();
                showPlanetPickerModal = false;
                ScreenMessages.PostScreenMessage(I18n.Format("mga_departure_set", planetName), 2.5f, ScreenMessageStyle.UPPER_CENTER);
            } else if (planetPickerTargetType == 1) {
                // 目标天体
                if (planetName.Equals(mgaConfig.DepartureBody, StringComparison.OrdinalIgnoreCase)) {
                    ScreenMessages.PostScreenMessage(I18n.T("mga_err_same_dep_dest"), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
                mgaConfig.DestinationBody = planetName;
                MGAStateManager.Current.SetDepartureAndDestination(mgaConfig.DepartureBody, mgaConfig.DestinationBody);
                mgaConfig.CentralBody = MGAStateManager.Current.Config.CentralBody;
                if (mgaWorkflowMode == 0) {
                    mgaConfig.FlybySequence = new List<string> { planetName };
                } else if (mgaConfig.FlybySequence != null && mgaConfig.FlybySequence.Count > 0) {
                    mgaConfig.FlybySequence[mgaConfig.FlybySequence.Count - 1] = planetName;
                } else {
                    mgaConfig.FlybySequence = new List<string> { planetName };
                }
                customChainInput = string.Join(",", mgaConfig.FlybySequence);
                selectedMgaSolution = null;
                mgaSolutions.Clear();
                RefreshCandidateChains(mgaWorkflowMode == 1);
                RefreshMgaCanvas();
                showPlanetPickerModal = false;
                ScreenMessages.PostScreenMessage(I18n.Format("mga_destination_set", planetName), 2.5f, ScreenMessageStyle.UPPER_CENTER);
            } else if (planetPickerTargetType == 2) {
                // 替换中间借力节点
                if (planetPickerFlybyIndex >= 0 && mgaConfig.FlybySequence != null && planetPickerFlybyIndex < mgaConfig.FlybySequence.Count) {
                    mgaConfig.FlybySequence[planetPickerFlybyIndex] = planetName;
                    customChainInput = string.Join(",", mgaConfig.FlybySequence);
                    RefreshMgaCanvas();
                    ScreenMessages.PostScreenMessage(I18n.Format("mga_flyby_changed", planetPickerFlybyIndex + 1, planetName), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            } else if (planetPickerTargetType == 3) {
                // 追加中间借力节点
                if (mgaConfig.FlybySequence == null) mgaConfig.FlybySequence = new List<string>();
                if (mgaConfig.FlybySequence.Count == 0) {
                    mgaConfig.FlybySequence.Add(planetName);
                    mgaConfig.FlybySequence.Add(mgaConfig.DestinationBody);
                } else {
                    mgaConfig.FlybySequence.Insert(mgaConfig.FlybySequence.Count - 1, planetName);
                }
                customChainInput = string.Join(",", mgaConfig.FlybySequence);
                RefreshMgaCanvas();
                ScreenMessages.PostScreenMessage(I18n.Format("mga_flyby_appended", planetName), 2.5f, ScreenMessageStyle.UPPER_CENTER);
            } else if (planetPickerTargetType == 4) {
                // 设置 MGA 任务序列步骤天体
                if (planetPickerFlybyIndex >= 0 && planetPickerFlybyIndex < MGAStateManager.Current.MissionSequence.Count) {
                    var step = MGAStateManager.Current.MissionSequence[planetPickerFlybyIndex];
                    step.body_name = planetName;
                    step.label = $"{planetName} {step.type}";
                    ScreenMessages.PostScreenMessage(I18n.Format("mga_step_body_set", planetPickerFlybyIndex + 1, planetName), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            // 无论修改何种类型的天体，全量通知 MGAStateManager 保证 3 个子窗口实时同步
            MGAStateManager.Current.NotifyStateChanged();
            showPlanetPickerModal = false;
        }

        // =========================================================================
        // MGA 经典历史任务与统一配置库弹出选择器 (Historical Mission Picker Modal)
        // =========================================================================
        public void OpenHistoryMissionPicker() {
            historyMissionSearch = "";
            historyMissionCategory = "全部";
            float pw = Mathf.Clamp(Screen.width * 0.55f, 600f, 820f);
            float ph = Mathf.Clamp(Screen.height * 0.68f, 520f, 750f);
            historyMissionPickerWindowRect = new Rect((Screen.width - pw) / 2, (Screen.height - ph) / 2, pw, ph);
            showHistoryMissionPickerModal = true;
            GUI.BringWindowToFront(982309);
        }

        private void DrawHistoryMissionPickerWindow(int windowID) {
            GUILayout.BeginVertical();

            // 1. 顶部标题栏
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_hist_modal_title", "🏛️ 行星际历史任务预设与基准配置库 (MGA Mission Library)"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showHistoryMissionPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 2. 搜索过滤与类别筛选栏
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("modal_search", "搜索:"), labelValueStyle, GUILayout.Width(40));
            historyMissionSearch = GUILayout.TextField(historyMissionSearch, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.MinWidth(140), GUILayout.ExpandWidth(true), GUILayout.Height(20));
            if (!string.IsNullOrEmpty(historyMissionSearch) && GUILayout.Button(I18n.T("btn_clear", "✖"), secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                historyMissionSearch = "";
            }
            GUILayout.Space(6);
            GUILayout.Label("分类:", labelValueStyle, GUILayout.Width(35));
            string[] cats = new string[] { "全部", "经典历史", "直达/内行星", "深空/外行星", "深空机动/低推力", "坎巴拉/原版", "用户自定义" };
            for (int c = 0; c < cats.Length; c++) {
                string cat = cats[c];
                bool isSel = (historyMissionCategory == cat);
                if (GUILayout.Button(cat, isSel ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    historyMissionCategory = cat;
                }
                GUILayout.Space(2);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 3. 历史任务卡片列表滚动视图
            historyMissionScrollPos = GUILayout.BeginScrollView(historyMissionScrollPos, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            var allMissions = MGAMissionRepository.LoadAllMissions();
            var filtered = allMissions.FindAll(m => {
                if (!string.IsNullOrEmpty(historyMissionSearch)) {
                    string q = historyMissionSearch.Trim().ToLowerInvariant();
                    bool matchName = m.mission_name != null && m.mission_name.ToLowerInvariant().Contains(q);
                    bool matchDesc = m.description != null && m.description.ToLowerInvariant().Contains(q);
                    bool matchBody = (m.departure_body != null && m.departure_body.ToLowerInvariant().Contains(q)) ||
                                     (m.destination_body != null && m.destination_body.ToLowerInvariant().Contains(q)) ||
                                     (m.flyby_sequence != null && m.flyby_sequence.Any(b => b.ToLowerInvariant().Contains(q)));
                    if (!matchName && !matchDesc && !matchBody) return false;
                }

                if (historyMissionCategory != "全部") {
                    if (!string.Equals(m.category, historyMissionCategory, StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            });

            if (filtered.Count == 0) {
                GUILayout.Space(20);
                GUILayout.Label("<color=#94a3b8>没有匹配的任务预设，可尝试清空搜索条件或重新扫描配置文件库。</color>", labelValueStyle);
            } else {
                for (int mIdx = 0; mIdx < filtered.Count; mIdx++) {
                    var mCfg = filtered[mIdx];
                    GUILayout.BeginVertical(nestedCardBoxStyle);

                    // 任务名称、分类与操作按钮
                    GUILayout.BeginHorizontal();
                    string icon = string.IsNullOrEmpty(mCfg.icon) ? "🪐" : mCfg.icon;
                    GUILayout.Label($"<b>{icon} {mCfg.mission_name}</b> <color=#94a3b8>({mCfg.description})</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();

                    // 一键载入到借力规划
                    if (GUILayout.Button("🚀 载入到 MGA 借力", secondaryButtonStyle, GUILayout.Width(135), GUILayout.Height(22))) {
                        LoadMissionConfigFile(mCfg, false);
                        showHistoryMissionPickerModal = false;
                        showBenchmarkModal = false;
                        showPorkchopModal = false;
                    }
                    GUILayout.Space(4);
                    // 载入并在工作台编辑
                    if (GUILayout.Button("📋 在工作台精细编辑", actionButtonStyle, GUILayout.Width(145), GUILayout.Height(22))) {
                        LoadMissionConfigFile(mCfg, true);
                        showHistoryMissionPickerModal = false;
                        showBenchmarkModal = false;
                        showPorkchopModal = false;
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);

                    // 航线链路
                    string seqStr = $"{mCfg.departure_body} ➔ " + string.Join(" ➔ ", mCfg.flyby_sequence ?? new List<string>());
                    GUILayout.Label(I18n.ColorFormat("mga_lib_route", seqStr, mCfg.earliest_departure_date), labelValueStyle);

                    // 历史指标性能
                    if (mCfg.historic_total_dv_min_ms > 0 && mCfg.historic_flight_days_max > 0) {
                        GUILayout.Label(I18n.ColorFormat("mga_lib_dv", mCfg.historic_total_dv_min_ms, mCfg.historic_total_dv_max_ms, mCfg.historic_flight_days_min, mCfg.historic_flight_days_max, mCfg.historic_flight_days_max / 365.25), labelValueStyle);
                    }

                    GUILayout.EndVertical();
                    GUILayout.Space(4);
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6);

            // 4. 底部通用工具箱
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 重新扫描 Missions 库", secondaryButtonStyle, GUILayout.Height(22))) {
                MGAMissionRepository.LoadAllMissions(true);
                ScreenMessages.PostScreenMessage("已重新扫描 Missions 配置文件目录", 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button(I18n.T("btn_run_benchmarks", "⚡ 历史基准算法全局跑分"), secondaryButtonStyle, GUILayout.Height(22))) {
                showBenchmarkModal = true;
                showHistoryMissionPickerModal = false;
                showPorkchopModal = false;
                RunHistoricalBenchmarks();
            }
            if (GUILayout.Button("🛠️ 原生底层动力学自检", secondaryButtonStyle, GUILayout.Height(22))) {
                RunNativeDiagnostics();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("导出当前配置", secondaryButtonStyle, GUILayout.Height(22))) {
                ExportCurrentMissionScript();
            }
            if (GUILayout.Button("导入统一配置", actionButtonStyle, GUILayout.Height(22))) {
                ImportAndSolveMissionScript();
                showHistoryMissionPickerModal = false;
            }
            if (GUILayout.Button("✕ 关闭", secondaryButtonStyle, GUILayout.Width(70), GUILayout.Height(22))) {
                showHistoryMissionPickerModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, historyMissionPickerWindowRect.width, 30));
        }

        // =========================================================================
        // 航段动力学遥测明细弹窗 (Flight Telemetry Details Modal)
        // =========================================================================
        private void DrawTelemetryModalWindow(int windowID) {
            var seq = MGAStateManager.Current.MissionSequence;
            var sol = MGAStateManager.Current.ActiveSolution;

            GUILayout.BeginVertical();

            // 1. 标题栏与关闭
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_telemetry_modal_title", "<b><color=#38bdf8>🪐 行星际航段动力学遥测看板 (Flight Telemetry Details)</color></b>"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showTelemetryModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (seq == null || seq.Count == 0) {
                GUILayout.Space(50);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#94a3b8>当前任务序列为空，请先添加任务步骤或进行 MGA 求解。</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(50);
            } else {
                // 2. 航段切换选择器 (Tabs)
                GUILayout.BeginHorizontal();
                for (int s = 0; s < seq.Count; s++) {
                    var step = seq[s];
                    bool isSelected = (telemetrySelectedStepIndex == s);
                    string tabTitle = $"#{s + 1}: {step.label}";
                    if (tabTitle.Length > 16) tabTitle = tabTitle.Substring(0, 14) + "..";
                    if (GUILayout.Button(tabTitle, isSelected ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                        telemetrySelectedStepIndex = s;
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // 3. 可滚动遥测富文本展示区
                telemetryScrollPos = GUILayout.BeginScrollView(telemetryScrollPos, cardBoxStyle, GUILayout.ExpandHeight(true));
                if (telemetrySelectedStepIndex >= 0 && telemetrySelectedStepIndex < seq.Count) {
                    var targetStep = seq[telemetrySelectedStepIndex];
                    DrawRichTextTelemetryCard(targetStep, telemetrySelectedStepIndex, sol);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(4);

            // 4. 底部关闭按钮
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("mga_telemetry_close", "关闭"), secondaryButtonStyle, GUILayout.Width(90), GUILayout.Height(24))) {
                showTelemetryModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, telemetryWindowRect.width, 30));
        }

        // =========================================================================
        // Sub-Window 9: 转移方案全量动力学详情独立弹窗 (Solution Astrodynamics Details Modal)
        // =========================================================================
        private void DrawSolutionDetailWindow(int windowID) {
            var sol = detailModalSolution ?? selectedMgaSolution;
            if (sol == null) {
                showSolutionDetailModal = false;
                return;
            }

            GUILayout.BeginVertical();

            // 1. 标题栏
            GUILayout.BeginHorizontal();
            string chainSummary = !string.IsNullOrEmpty(sol.FlybyChainSummary) ? sol.FlybyChainSummary : $"{mgaConfig?.DepartureBody} ➔ {mgaConfig?.DestinationBody}";
            GUILayout.Label($"<b><color=#38bdf8>🔍 转移航线全量动力学全景明细</color></b> <color=#94a3b8>({chainSummary})</color>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showSolutionDetailModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 2. 方案综合指标概览卡片
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>助推链条:</b> <color=#38bdf8><b>{chainSummary}</b></color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<b>方案总机动 (Total ΔV):</b> <color=#10b981><b>{sol.TotalDeltaVKms * 1000.0:F1} m/s</b></color>", sectionHeaderStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            string depDate = GMATClient.FormatUtcGregorian(sol.DepartureUT);
            string arrDate = GMATClient.FormatUtcGregorian(sol.ArrivalUT);
            GUILayout.Label($"<b>发射窗口 (UTC):</b> <color=#f8fafc>{depDate}</color>", labelValueStyle, GUILayout.Width(260));
            GUILayout.Label($"<b>到达历元 (UTC):</b> <color=#f8fafc>{arrDate}</color>", labelValueStyle, GUILayout.Width(260));
            string flightStr = sol.TotalFlightDays >= 365.25 ? 
                $"{sol.TotalFlightDays / 365.25:F2} 年 ({sol.TotalFlightDays:F1} 天)" : 
                $"{sol.TotalFlightDays:F1} 天";
            GUILayout.Label($"<b>总飞行时间:</b> <color=#f8fafc>{flightStr}</color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>出射脱离 ΔV:</b> <color=#38bdf8><b>{sol.LaunchDeltaVKms * 1000.0:F1} m/s</b></color>", labelValueStyle, GUILayout.Width(260));
            GUILayout.Label($"<b>目标捕获 ΔV:</b> <color=#f59e0b><b>{sol.InsertionDeltaVKms * 1000.0:F1} m/s</b></color>", labelValueStyle, GUILayout.Width(260));
            GUILayout.Label(sol.AllLegsFeasible ? "<color=#34d399><b>✅ 全部航段动力学物理闭环收敛</b></color>" : "<color=#facc15><b>⚠️ 存在边界约束超限</b></color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 3. 逐航段全景遥测明细 (可滚动表格)
            solutionDetailScrollPos = GUILayout.BeginScrollView(solutionDetailScrollPos, cardBoxStyle, GUILayout.ExpandHeight(true));

            if (sol.Legs != null && sol.Legs.Count > 0) {
                for (int k = 0; k < sol.Legs.Count; k++) {
                    var leg = sol.Legs[k];
                    GUILayout.BeginVertical(nestedCardBoxStyle);

                    // 航段标题与类型
                    GUILayout.BeginHorizontal();
                    string legBadge = (k == 0) ? "<color=#38bdf8>[始发段]</color>" : ((k == sol.Legs.Count - 1) ? "<color=#34d399>[末端交会段]</color>" : "<color=#c084fc>[借力过渡段]</color>");
                    string stratStr = leg.Strategy switch {
                        MGALegStrategy.DeepSpaceManeuver1DSM => "MGA-1DSM (深空脉冲机动)",
                        MGALegStrategy.ContinuousLowThrust => "连续低推力 (Sims-Flanagan)",
                        _ => "纯弹道无动力引力助推"
                    };
                    GUILayout.Label($"<b>{legBadge} 第 {k + 1}/{sol.Legs.Count} 段: <color=#f8fafc>{leg.FromBody} ➔ {leg.ToBody}</color></b> <color=#94a3b8>({stratStr})</color>", sectionHeaderStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"<b>航段历时:</b> <color=#38bdf8>{leg.ToFDays:F1} 天</color>", labelValueStyle);
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);

                    // 行 1: 始发与到达历元
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  出发历元: <color=#cbd5e1>{GMATClient.FormatUtcGregorian(leg.DepartureUT)}</color>", labelValueStyle, GUILayout.Width(270));
                    GUILayout.Label($"  到达历元: <color=#cbd5e1>{GMATClient.FormatUtcGregorian(leg.ArrivalUT)}</color>", labelValueStyle, GUILayout.Width(270));
                    GUILayout.Label($"  始发相对双曲速度 V∞: <color=#f8fafc>{leg.VInfDepartureKms.magnitude:F3} km/s</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 行 2: 到达双曲速度与飞掠几何
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  到达相对双曲速度 V∞: <color=#f8fafc>{leg.VInfArrivalKms.magnitude:F3} km/s</color>", labelValueStyle, GUILayout.Width(270));
                    if (leg.FlybyPeriapsisRadiusKm > 0) {
                        GUILayout.Label($"  飞掠近拱点高度 hp: <color=#38bdf8><b>{leg.FlybyPeriapsisAltitudeKm:F1} km</b></color>", labelValueStyle, GUILayout.Width(270));
                        GUILayout.Label($"  飞掠转角 δ: <color=#f8fafc>{leg.FlybyTurningAngleDeg:F2}°</color>", labelValueStyle);
                    } else {
                        GUILayout.Label($"  终点天体状态: <color=#34d399>进入目标捕获/泊入阶段</color>", labelValueStyle);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 行 3: 若存在 DSM 深空机动
                    if (leg.HasDSM && leg.DSM != null) {
                        GUILayout.BeginHorizontal();
                        double dsmUT = leg.DepartureUT + leg.DSM.EpochOffsetSec;
                        GUILayout.Label($"  <color=#f59e0b><b>⚡ 深空机动 (DSM):</b></color> 历元 {GMATClient.FormatUtcGregorian(dsmUT)} (+{leg.DSM.EpochOffsetSec / 86400.0:F1}d)", labelValueStyle, GUILayout.Width(350));
                        GUILayout.Label($"ΔV: <color=#f59e0b><b>{leg.DSM.DeltaVMs:F1} m/s</b></color> (顺向 {leg.DSM.ProgradeMs:F1}, 法向 {leg.DSM.NormalMs:F1}, 径向 {leg.DSM.RadialMs:F1} m/s)", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    // 行 4: 动力学可行性
                    if (leg.FlybyDeltaVKms > 0.001) {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"  <color=#facc15>⚠️ 有动力飞掠机动需求: {leg.FlybyDeltaVKms * 1000.0:F1} m/s</color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
            } else {
                GUILayout.Label("<color=#94a3b8>暂无详细航段动力学数据</color>", labelValueStyle);
            }

            // 4. 目标天体减速捕获卡片
            GUILayout.BeginVertical(nestedCardBoxStyle);
            var lastLeg = (sol.Legs != null && sol.Legs.Count > 0) ? sol.Legs.Last() : null;
            string destName = lastLeg != null ? lastLeg.ToBody : (mgaConfig?.DestinationBody ?? "Target");
            GUILayout.Label($"<b>🎯 目标天体泊入与制动机动明细 ({destName})</b>", sectionHeaderStyle);
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"  目标停泊轨道: 近拱 <color=#38bdf8>{mgaConfig?.DestinationPeAKm:F0} km</color> × 远拱 <color=#38bdf8>{mgaConfig?.DestinationApAKm:F0} km</color>", labelValueStyle, GUILayout.Width(320));
            double vArrMag = lastLeg != null ? lastLeg.VInfArrivalKms.magnitude : 0.0;
            GUILayout.Label($"到达相对渐近速度 V∞: <color=#f8fafc><b>{vArrMag:F3} km/s</b></color>", labelValueStyle, GUILayout.Width(270));
            GUILayout.Label($"入轨减速制动需求: <color=#f59e0b><b>{sol.InsertionDeltaVKms * 1000.0:F1} m/s</b></color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // 5. 底部操作栏
            GUILayout.BeginHorizontal();
            bool isCurrentActive = (sol == selectedMgaSolution);
            if (isCurrentActive) {
                GUILayout.Label("<color=#38bdf8><b>✓ 此方案当前正在主视图与 3D 画布中显示</b></color>", labelValueStyle);
            } else {
                if (GUILayout.Button("✓ 选用此方案为活动轨道", actionButtonStyle, GUILayout.Width(180), GUILayout.Height(24))) {
                    selectedMgaSolution = sol;
                    MGAStateManager.Current.NotifySolutionChanged(sol);
                    RefreshMgaCanvas();
                    showSolutionDetailModal = false;
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("📋 复制此方案完整报告", secondaryButtonStyle, GUILayout.Width(160), GUILayout.Height(24))) {
                string report = GenerateDetailedMgaReport(sol);
                GUIUtility.systemCopyBuffer = report;
                ScreenMessages.PostScreenMessage(I18n.T("mga_report_copied", "方案报告已复制到剪贴板"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_close", "关闭"), secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(24))) {
                showSolutionDetailModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, solutionDetailWindowRect.width, 30));
        }
    }
}
