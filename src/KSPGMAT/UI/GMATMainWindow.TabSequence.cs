using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;
using KSPGMAT;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private string GetParentTargetDcName(int stepIndex) {
            for (int j = stepIndex - 1; j >= 0; j--) {
                if (missionSequence[j].type == "Target") {
                    return string.IsNullOrEmpty(missionSequence[j].dc_name) ? "DefaultDC" : missionSequence[j].dc_name;
                }
                if (missionSequence[j].type == "EndTarget") break;
            }
            return "DefaultDC";
        }

        private List<string> GetAvailableSolverList() {
            List<string> list = new List<string>();
            
            // 1. From GMATStateManager (Primary Single Source of Truth)
            if (GMATStateManager.Current != null && GMATStateManager.Current.Solvers != null) {
                for (int s = 0; s < GMATStateManager.Current.Solvers.Count; s++) {
                    string sName = GMATStateManager.Current.Solvers[s].Name;
                    if (!string.IsNullOrEmpty(sName) && !list.Contains(sName)) list.Add(sName);
                }
            }

            // 2. From active sequence
            if (missionSequence != null) {
                for (int j = 0; j < missionSequence.Count; j++) {
                    if (missionSequence[j].type == "Target" && !string.IsNullOrEmpty(missionSequence[j].dc_name)) {
                        string sName = missionSequence[j].dc_name;
                        if (!list.Contains(sName)) list.Add(sName);
                    }
                }
            }

            // Fallback defaults
            if (list.Count == 0) {
                list.Add("DC_GTO");
                list.Add("DC_GEO");
            }
            if (!list.Contains("DefaultDC")) list.Insert(0, "DefaultDC");
            return list;
        }

        private List<string> GetActiveSolverNames() {
            return GetAvailableSolverList();
        }

        private string GetContextCentralBody() {
            if (GMATStateManager.Current != null && GMATStateManager.Current.Spacecraft != null && !string.IsNullOrEmpty(GMATStateManager.Current.Spacecraft.CentralBody)) {
                return GMATStateManager.Current.Spacecraft.CentralBody;
            }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null && v.mainBody != null) {
                return GMATParameterRegistry.MapBodyName(v.mainBody.bodyName);
            }
            return "Earth";
        }

        private string GetContextPropagator(string centralBody = null) {
            string cb = string.IsNullOrEmpty(centralBody) ? GetContextCentralBody() : centralBody;
            if (propagatorConfigs != null && propagatorConfigs.Count > 0) {
                foreach (var k in propagatorConfigs.Keys) {
                    return k;
                }
            }
            if (cb == "Luna" || cb == "Moon") return "MoonProp";
            if (cb == "Mars") return "MarsProp";
            if (cb == "Sun") return "DeepSpaceProp";
            return "LEOProp";
        }

        private void SetTargetGroupSolver(int targetStepIndex, string newSolverName) {
            if (targetStepIndex < 0 || targetStepIndex >= missionSequence.Count) return;
            var targetStep = missionSequence[targetStepIndex];
            targetStep.dc_name = newSolverName;

            // Sync solver settings from GMATStateManager if exists
            if (GMATStateManager.Current != null && GMATStateManager.Current.Solvers != null) {
                var found = GMATStateManager.Current.Solvers.Find(s => s.Name == newSolverName);
                if (found != null) {
                    targetStep.dc_algorithm = found.Algorithm;
                    targetStep.dc_derivative_method = found.DerivativeMethod;
                    targetStep.dc_max_iter = found.MaxIterations;
                } else {
                    // Register into state manager
                    GMATStateManager.Current.Solvers.Add(new GMATSolverModel {
                        Name = newSolverName,
                        Algorithm = targetStep.dc_algorithm ?? "NewtonRaphson",
                        DerivativeMethod = targetStep.dc_derivative_method ?? "ForwardDifference",
                        MaxIterations = targetStep.dc_max_iter > 0 ? targetStep.dc_max_iter : 50
                    });
                }
            }

            // Cascade update all child steps within this Target group
            for (int ch = targetStepIndex + 1; ch < missionSequence.Count; ch++) {
                if (missionSequence[ch].type == "EndTarget") {
                    missionSequence[ch].dc_name = newSolverName;
                    break;
                }
                if (missionSequence[ch].type == "Target") break;
                if (missionSequence[ch].type == "Vary" || missionSequence[ch].type == "Achieve") {
                    missionSequence[ch].dc_name = newSolverName;
                }
            }
        }

        private void DrawStepReorderControls(int i) {
            bool isBeingDragged = (draggedStepIndex == i);

            // Up / Down arrows
            GUI.enabled = (i > 0);
            if (GUILayout.Button("↑", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(20))) {
                stepToMoveUp = i;
            }
            GUI.enabled = (i < missionSequence.Count - 1);
            if (GUILayout.Button("↓", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(20))) {
                stepToMoveDown = i;
            }
            GUI.enabled = true;

            // Reorder / Drag Action Button
            string dragBtnText = isBeingDragged ? I18n.T("seq_reorder_dragging") : (draggedStepIndex >= 0 ? I18n.T("seq_reorder_drop") : I18n.T("seq_reorder_sort"));
            GUIStyle dragBtnStyle = isBeingDragged ? successButtonStyle : (draggedStepIndex >= 0 ? tabButtonActiveStyle : secondaryButtonStyle);
            float dragBtnW = (draggedStepIndex >= 0 && !isBeingDragged) ? 82f : 50f;

            if (GUILayout.Button(dragBtnText, dragBtnStyle, GUILayout.Width(dragBtnW), GUILayout.Height(20))) {
                if (draggedStepIndex == i) {
                    draggedStepIndex = -1; // Cancel
                } else if (draggedStepIndex >= 0) {
                    var moveItem = missionSequence[draggedStepIndex];
                    missionSequence.RemoveAt(draggedStepIndex);
                    missionSequence.Insert(i, moveItem);
                    ScreenMessages.PostScreenMessage(I18n.Format("seq_moved_to", i + 1), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    draggedStepIndex = -1;
                } else {
                    draggedStepIndex = i;
                    ScreenMessages.PostScreenMessage(I18n.Format("seq_selected_move", i + 1), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        void DrawTabMissionSequence(Vessel vessel) {
            hoveredStepIndex = nextHoveredStepIndex;
            nextHoveredStepIndex = -1;

            float totalW = windowRect.width - 36f;
            float rightColW = Mathf.Clamp(totalW * 0.32f, 310f, 420f);
            float leftColW = totalW - rightColW - 12f;

            GUILayout.BeginHorizontal();

            // ==========================================
            // LEFT COLUMN: Sequence Editor
            // ==========================================
            GUILayout.BeginVertical(GUILayout.Width(leftColW));

            // Presets & Import Header (Collapsible & Elastic)
            GUILayout.BeginVertical(cardBoxStyle);
            
            var allTemplates = GMATTemplateManager.GetTemplates();
            
            GUILayout.BeginHorizontal();
            string foldoutArrow = showTemplatesFoldout ? "▼" : "▶";
            if (GUILayout.Button(I18n.Format("seq_preset_header", foldoutArrow, allTemplates.Count), headerStyle, GUILayout.ExpandWidth(false))) {
                showTemplatesFoldout = !showTemplatesFoldout;
            }

            if (!showTemplatesFoldout && !string.IsNullOrEmpty(currentLoadedTemplateName)) {
                GUILayout.Space(8);
                GUILayout.Label(I18n.ColorFormat("seq_current_loaded", currentLoadedTemplateName), labelValueStyle);
            }

            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button(I18n.T("btn_refresh"), secondaryButtonStyle, GUILayout.Height(20))) {
                GMATTemplateManager.RefreshTemplates();
                allTemplates = GMATTemplateManager.GetTemplates(true);
            }
            GUILayout.Space(3);
            if (GUILayout.Button(I18n.T("btn_open_folder"), secondaryButtonStyle, GUILayout.Height(20))) {
                GMATTemplateManager.OpenTemplatesFolder();
            }
            GUILayout.Space(3);
            if (GUILayout.Button(I18n.T("btn_ide"), secondaryButtonStyle, GUILayout.Height(20))) {
                activeTab = 1; ideScriptViewMode = 0;
            }
            GUILayout.Space(3);
            if (GUILayout.Button(showCustomScriptInput ? I18n.T("btn_script_io_close") : I18n.T("btn_script_io_open"), secondaryButtonStyle, GUILayout.Height(20))) {
                showCustomScriptInput = !showCustomScriptInput;
            }
            GUILayout.EndHorizontal();

            // EXPANDED TEMPLATE BROWSER
            if (showTemplatesFoldout) {
                // Filter & Search bar (if templates > 3)
                if (allTemplates.Count > 3) {
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("seq_category_label"), labelValueStyle, GUILayout.Width(35));
                    string[] categories = new string[] { "全部", "近地/地球", "地月系", "深空/行星际", "测试/验证" };
                    for (int c = 0; c < categories.Length; c++) {
                        string cat = categories[c];
                        bool isSelectedCat = (templateSelectedCategory == cat);
                        GUIStyle catStyle = isSelectedCat ? actionButtonStyle : secondaryButtonStyle;
                        if (GUILayout.Button(cat, catStyle, GUILayout.Height(18))) {
                            templateSelectedCategory = cat;
                        }
                        GUILayout.Space(2);
                    }
                    GUILayout.FlexibleSpace();
                    templateSearchQuery = GUILayout.TextField(templateSearchQuery, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(130), GUILayout.Height(18));
                    if (!string.IsNullOrEmpty(templateSearchQuery) && GUILayout.Button("✖", secondaryButtonStyle, GUILayout.Width(18), GUILayout.Height(18))) {
                        templateSearchQuery = "";
                    }
                    GUILayout.EndHorizontal();
                }

                // Filtered Templates List
                var filtered = allTemplates.Where(t => {
                    if (templateSelectedCategory != "全部" && t.Category != templateSelectedCategory) return false;
                    if (!string.IsNullOrEmpty(templateSearchQuery)) {
                        string q = templateSearchQuery.Trim().ToLowerInvariant();
                        if (!t.DisplayTitle.ToLowerInvariant().Contains(q) && !t.FileName.ToLowerInvariant().Contains(q) && !t.Description.ToLowerInvariant().Contains(q)) {
                            return false;
                        }
                    }
                    return true;
                }).ToList();

                GUILayout.Space(4);

                // ELASTIC DYNAMIC FLOW/GRID LAYOUT (With adaptive capped height scroll if many templates)
                if (filtered.Count > 0) {
                    int columns = 3; // 3 elastic buttons per row
                    int rowCount = (filtered.Count + columns - 1) / columns;

                    // If more than 2 rows (6 templates), wrap in a capped scroll view to save vertical space
                    bool needsScroll = rowCount > 2;
                    if (needsScroll) {
                        templateScrollPos = GUILayout.BeginScrollView(templateScrollPos, GUILayout.MaxHeight(130));
                    }

                    for (int r = 0; r < rowCount; r++) {
                        GUILayout.BeginHorizontal();
                        for (int col = 0; col < columns; col++) {
                            int index = r * columns + col;
                            if (index < filtered.Count) {
                                var tpl = filtered[index];
                                bool isCurrent = !string.IsNullOrEmpty(currentLoadedTemplateName) && 
                                    (currentLoadedTemplateName.Equals(tpl.FileName, StringComparison.OrdinalIgnoreCase) || currentLoadedTemplateName.Equals(tpl.FullPath, StringComparison.OrdinalIgnoreCase));
                                
                                GUIStyle btnStyle = isCurrent ? actionButtonStyle : secondaryButtonStyle;
                                string btnText = string.IsNullOrEmpty(tpl.Icon) ? tpl.DisplayTitle : $"{tpl.Icon} {tpl.DisplayTitle}";

                                if (GUILayout.Button(new GUIContent(btnText, I18n.Format("seq_template_tooltip", tpl.FileName, tpl.Description, tpl.FullPath)), btnStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
                                    currentLoadedTemplateName = tpl.FileName;
                                    LoadScriptTemplate(tpl.FullPath, vessel);
                                }
                            } else {
                                GUILayout.FlexibleSpace();
                            }
                        }
                        GUILayout.EndHorizontal();
                        if (r < rowCount - 1) GUILayout.Space(3);
                    }

                    if (needsScroll) {
                        GUILayout.EndScrollView();
                    }
                } else {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("seq_no_template"), labelValueStyle);
                    GUILayout.EndHorizontal();
                }
            }

            if (showCustomScriptInput) {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_import_label"), labelValueStyle, GUILayout.Width(110));
                customScriptPath = GUILayout.TextField(customScriptPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
                if (GUILayout.Button(I18n.T("btn_parse_import"), secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
                    if (System.IO.File.Exists(customScriptPath)) {
                        GMATClient.ImportScript(customScriptPath, vessel, (ok, result, msg) => {
                            if (ok && result != null) {
                                ApplyImportResultToState(result);

                                // Clear old solver diagnostics from previous run
                                solveStageText = I18n.T("solve_stage_ready");
                                latestResult = null;
                                solveProgress = 0f;

                                try {
                                    ideCustomBuffer = File.ReadAllText(customScriptPath, System.Text.Encoding.UTF8);
                                } catch {}

                                ScreenMessages.PostScreenMessage(I18n.Format("seq_imported", result.sequence?.Count ?? 0), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                            } else {
                                ScreenMessages.PostScreenMessage(I18n.Format("seq_import_fail", msg), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                            }
                        });
                    } else {
                        ScreenMessages.PostScreenMessage(I18n.Format("seq_file_not_found", customScriptPath), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_export_label"), labelValueStyle, GUILayout.Width(110));
                exportTemplateName = GUILayout.TextField(exportTemplateName, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
                if (GUILayout.Button(I18n.T("btn_export_template"), secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
                    ExportScriptTemplate(exportTemplateName, vessel);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // Global Sequence Toolbar: Quick Bulk Expand / Collapse
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>{I18n.T("seq_queue_header")}</b></color> <color=#64748b>({missionSequence.Count} {I18n.T("seq_step_unit")})</color>", labelValueStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(I18n.T("seq_expand_all"), secondaryButtonStyle, GUILayout.Height(20))) {
                for (int s = 0; s < missionSequence.Count; s++) missionSequence[s].is_collapsed = false;
            }
            if (GUILayout.Button(I18n.T("seq_collapse_all"), secondaryButtonStyle, GUILayout.Height(20))) {
                for (int s = 0; s < missionSequence.Count; s++) missionSequence[s].is_collapsed = true;
            }
            if (GUILayout.Button(I18n.T("seq_expand_targets"), secondaryButtonStyle, GUILayout.Height(20))) {
                bool inTarget = false;
                for (int s = 0; s < missionSequence.Count; s++) {
                    if (missionSequence[s].type == "Target") {
                        inTarget = true;
                        missionSequence[s].is_collapsed = false;
                    } else if (missionSequence[s].type == "EndTarget") {
                        inTarget = false;
                        missionSequence[s].is_collapsed = true;
                    } else {
                        missionSequence[s].is_collapsed = !inTarget;
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (latestResult != null) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                if (latestResult.all_solvers_converged && latestResult.success) {
                    GUILayout.Label(I18n.T("seq_solver_all_converged"), labelValueStyle);
                } else {
                    string unconvStr = (latestResult.unconverged_solvers != null && latestResult.unconverged_solvers.Count > 0)
                        ? string.Join(", ", latestResult.unconverged_solvers.ToArray())
                        : I18n.T("seq_solver_unconv");
                    GUILayout.Label(I18n.ColorFormat("seq_solver_warn", unconvStr), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            scrollPosSequence = GUILayout.BeginScrollView(scrollPosSequence, cardBoxStyle, GUILayout.ExpandHeight(true));

            if (missionSequence.Count == 0) {
                GUILayout.Space(40);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.T("seq_empty"), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(40);
            }

            // stepToMoveUp field used
            // stepToMoveDown field used
            int stepToDelete = -1;
            int stepToInsertVary = -1;
            int stepToInsertAchieve = -1;
            int stepToInsertBurn = -1;
            int stepToInsertImpulsive = -1;
            int stepToInsertProp = -1;
            int stepToInsertThrustVector = -1;

            bool insideTargetGroup = false;
            bool targetGroupCollapsed = false;

            for (int i = 0; i < missionSequence.Count; i++) {
                var step = missionSequence[i];
                bool isBeingDragged = (draggedStepIndex == i);

                // Animated insertion divider bar when in Reorder Mode
                if (draggedStepIndex >= 0 && draggedStepIndex != i) {
                    if (GUILayout.Button(I18n.Format("seq_insert_before", draggedStepIndex + 1, i + 1), tabButtonActiveStyle, GUILayout.Height(22))) {
                        var moveItem = missionSequence[draggedStepIndex];
                        missionSequence.RemoveAt(draggedStepIndex);
                        int targetIdx = (draggedStepIndex < i) ? i - 1 : i;
                        missionSequence.Insert(targetIdx, moveItem);
                        ScreenMessages.PostScreenMessage(I18n.Format("seq_moved_before", draggedStepIndex + 1, i + 1), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        draggedStepIndex = -1;
                    }
                }

                // ==========================================
                // CASE 1: TARGET GROUP ROOT CONTAINER
                // ==========================================
                if (step.type == "Target") {
                    // If a previous group was open, close it safely
                    if (insideTargetGroup) {
                        GUILayout.EndVertical();
                        GUILayout.Space(6);
                    }

                    insideTargetGroup = true;
                    targetGroupCollapsed = step.is_collapsed;
                    bool isTargetHovered = (hoveredStepIndex == i);

                    // BEGIN PROMINENT OUTER GROUP BOUNDING BOX (Enclosing entire Target group with dynamic hover glow)
                    GUILayout.BeginVertical(GetTargetGroupBoxStyle(i, isTargetHovered));

                    int varyCount = 0;
                    int achieveCount = 0;
                    for (int ch = i + 1; ch < missionSequence.Count; ch++) {
                        if (missionSequence[ch].type == "EndTarget") break;
                        if (missionSequence[ch].type == "Target") break;
                        if (missionSequence[ch].type == "Vary") varyCount++;
                        if (missionSequence[ch].type == "ThrustVectorControl" || missionSequence[ch].type == "VaryThrustDirection") {
                            if (missionSequence[ch].is_vary_vector) {
                                int enabledCount = (missionSequence[ch].vary_dir1_enabled ? 1 : 0) + 
                                                   (missionSequence[ch].vary_dir2_enabled ? 1 : 0) + 
                                                   (missionSequence[ch].vary_dir3_enabled ? 1 : 0);
                                varyCount += enabledCount;
                            }
                        }
                        if (missionSequence[ch].type == "Achieve") achieveCount++;
                    }

                    // Check duplicate targeter name
                    bool isDuplicateDcName = false;
                    for (int ot = 0; ot < missionSequence.Count; ot++) {
                        if (ot != i && missionSequence[ot].type == "Target" && missionSequence[ot].dc_name == step.dc_name) {
                            isDuplicateDcName = true;
                            break;
                        }
                    }

                    // Target Header Bar
                    GUILayout.BeginHorizontal();

                    string foldIcon = step.is_collapsed ? I18n.T("seq_target_expand") : I18n.T("seq_target_collapse");
                    if (GUILayout.Button(foldIcon, secondaryButtonStyle, GUILayout.Width(92), GUILayout.Height(20))) {
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

                    GUILayout.Label(I18n.Format("seq_target_label", i + 1), headerStyle, GUILayout.Width(130));
                    step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? I18n.T("seq_target_default_label") : step.label, stepLabelInputStyle, GUILayout.MinWidth(140), GUILayout.MaxWidth(280), GUILayout.Height(20));
                    GUILayout.FlexibleSpace();

                    string solverBadge = I18n.Format("seq_solver_badge", step.dc_name, step.dc_algorithm, step.dc_max_iter);
                    GUILayout.Label(solverBadge, labelValueStyle);

                    bool showCfg = expandedSolverSettings.Contains(i);
                    if (GUILayout.Button(showCfg ? I18n.T("btn_solver_cfg_close") : I18n.T("btn_solver_cfg_open"), secondaryButtonStyle, GUILayout.Height(20))) {
                        if (showCfg) expandedSolverSettings.Remove(i);
                        else expandedSolverSettings.Add(i);
                    }

                    if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                        stepToDelete = i;
                    }
                    GUILayout.EndHorizontal();

                    // Smart Targeter Diagnostics & 1-Click Auto-Fix Banners
                    if (achieveCount > varyCount) {
                        GUILayout.Space(3);
                        GUILayout.BeginHorizontal(nestedCardBoxStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_dof_warning", achieveCount, varyCount), labelValueStyle);
                        if (GUILayout.Button(I18n.T("btn_autofix_vary"), tabButtonActiveStyle, GUILayout.Height(20))) {
                            string dc = string.IsNullOrEmpty(step.dc_name) ? $"DC_{i + 1}" : step.dc_name;
                            // Dynamically find active thruster name from vessel or sequence
                            string activeThruster = "MainEngine";
                            for (int ch = i + 1; ch < missionSequence.Count; ch++) {
                                if (missionSequence[ch].type == "EndTarget") break;
                                if (!string.IsNullOrEmpty(missionSequence[ch].bound_thruster)) {
                                    activeThruster = missionSequence[ch].bound_thruster;
                                    break;
                                }
                            }
                            missionSequence.Insert(i + 1, new MissionStep {
                                type = "Vary",
                                vary_variable = $"Sat.{activeThruster}.ThrustDirection2",
                                vary_initial = 0.0,
                                vary_lower = -1.0,
                                vary_upper = 1.0,
                                vary_max_step = 0.05,
                                dc_name = dc,
                                label = I18n.T("seq_vary_label")
                            });
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_vary_autofixed", activeThruster), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        GUILayout.EndHorizontal();
                    }

                    if (isDuplicateDcName) {
                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal(nestedCardBoxStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_dup_dc", step.dc_name), labelValueStyle);
                        if (GUILayout.Button(I18n.T("btn_rename_solver"), secondaryButtonStyle, GUILayout.Height(20))) {
                            string newDc = $"DC_{i + 1}";
                            step.dc_name = newDc;
                            for (int ch = i + 1; ch < missionSequence.Count; ch++) {
                                if (missionSequence[ch].type == "EndTarget") { missionSequence[ch].dc_name = newDc; break; }
                                if (missionSequence[ch].type == "Target") break;
                                if (missionSequence[ch].type == "Vary" || missionSequence[ch].type == "Achieve") {
                                    missionSequence[ch].dc_name = newDc;
                                }
                            }
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_renamed", newDc), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        GUILayout.EndHorizontal();
                    }

                    // Target Folded Summary Banner
                    if (step.is_collapsed) {
                        int childVary = 0, childAchieve = 0, childBurn = 0, childTotal = 0;
                        for (int j = i + 1; j < missionSequence.Count; j++) {
                            if (missionSequence[j].type == "EndTarget") break;
                            childTotal++;
                            if (missionSequence[j].type == "Vary") childVary++;
                            if (missionSequence[j].type == "Achieve") childAchieve++;
                            if (missionSequence[j].type == "FiniteBurn" || missionSequence[j].type == "ImpulsiveBurn") childBurn++;
                        }
                        GUILayout.Space(3);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.ColorFormat("seq_collapsed_summary", childTotal, childVary, childBurn, childAchieve), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    // Target Expanded Solver Config & Quick Add
                    if (!step.is_collapsed) {
                        if (expandedSolverSettings.Contains(i)) {
                            GUILayout.Space(4);
                            GUILayout.BeginVertical(nestedCardBoxStyle);
                            
                            // Row 1: Solver selector chips & dynamic add button
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.T("seq_solver_bind"), labelValueStyle, GUILayout.Width(130));
                            string curDc = string.IsNullOrEmpty(step.dc_name) ? "DefaultDC" : step.dc_name;
                            string editedDc = GUILayout.TextField(curDc, GUILayout.Width(110), GUILayout.Height(20));
                            if (editedDc != curDc) {
                                SetTargetGroupSolver(i, editedDc);
                            }

                            List<string> availSolvers = GetAvailableSolverList();
                            for (int c = 0; c < availSolvers.Count; c++) {
                                string cs = availSolvers[c];
                                bool isSel = (step.dc_name == cs);
                                if (GUILayout.Button(cs, isSel ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                                    SetTargetGroupSolver(i, cs);
                                }
                            }

                            if (GUILayout.Button(I18n.T("btn_new_solver3"), secondaryButtonStyle, GUILayout.Height(20))) {
                                string newName = $"DC_{GMATStateManager.Current.Solvers.Count + 1}";
                                GMATStateManager.Current.Solvers.Add(new GMATSolverModel {
                                    Name = newName,
                                    Algorithm = "NewtonRaphson",
                                    DerivativeMethod = "ForwardDifference",
                                    MaxIterations = 50
                                });
                                SetTargetGroupSolver(i, newName);
                                ScreenMessages.PostScreenMessage(I18n.Format("seq_solver_created", newName), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                            }
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            // Row 2: Algorithm, Derivative Method, Maximum Iterations (Bidirectional sync with GMATStateManager)
                            GUILayout.Space(3);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.T("seq_algo_label"), gridLabelStyle, GUILayout.Width(65));
                            string[] dcAlgs = new string[] { "NewtonRaphson", "Broyden" };
                            int aIdx = Array.IndexOf(dcAlgs, step.dc_algorithm);
                            if (aIdx < 0) aIdx = 0;
                            if (GUILayout.Button(dcAlgs[aIdx], secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(20))) {
                                step.dc_algorithm = dcAlgs[(aIdx + 1) % dcAlgs.Length];
                                var sol = GMATStateManager.Current.Solvers.Find(s => s.Name == step.dc_name);
                                if (sol != null) sol.Algorithm = step.dc_algorithm;
                            }

                            GUILayout.Space(8);
                            GUILayout.Label(I18n.T("seq_deriv_label"), gridLabelStyle, GUILayout.Width(65));
                            string[] dcDms = new string[] { "ForwardDifference", "CentralDifference" };
                            int dmIdx = Array.IndexOf(dcDms, step.dc_derivative_method);
                            if (dmIdx < 0) dmIdx = 0;
                            if (GUILayout.Button(dcDms[dmIdx], secondaryButtonStyle, GUILayout.Width(125), GUILayout.Height(20))) {
                                step.dc_derivative_method = dcDms[(dmIdx + 1) % dcDms.Length];
                                var sol = GMATStateManager.Current.Solvers.Find(s => s.Name == step.dc_name);
                                if (sol != null) sol.DerivativeMethod = step.dc_derivative_method;
                            }

                            GUILayout.Space(8);
                            GUILayout.Label(I18n.T("seq_maxiter_label"), gridLabelStyle, GUILayout.Width(65));
                            int newMaxIter = DrawDelayedIntField($"dc_iter_{i}", step.dc_max_iter, numericInputStyle, GUILayout.Width(50));
                            if (newMaxIter != step.dc_max_iter) {
                                step.dc_max_iter = newMaxIter;
                                var sol = GMATStateManager.Current.Solvers.Find(s => s.Name == step.dc_name);
                                if (sol != null) sol.MaxIterations = newMaxIter;
                            }

                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            GUILayout.Space(2);
                        }

                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(8);
                        int groupEndIdx = missionSequence.Count;
                        for (int k = i + 1; k < missionSequence.Count; k++) {
                            if (missionSequence[k].type == "EndTarget") {
                                groupEndIdx = k;
                                break;
                            }
                        }

                        GUILayout.Label(I18n.T("seq_quick_add"), labelValueStyle);
                        if (GUILayout.Button(I18n.T("btn_add_vary2"), secondaryButtonStyle, GUILayout.Height(18))) {
                            stepToInsertVary = groupEndIdx;
                        }
                        if (GUILayout.Button(I18n.T("btn_add_achieve2"), secondaryButtonStyle, GUILayout.Height(18))) {
                            stepToInsertAchieve = groupEndIdx;
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



                    // DO NOT call GUILayout.EndVertical() here: keep the prominent group container OPEN for child steps!
                    continue;
                }

                // ==========================================
                // CASE 2: END TARGET CLOSURE
                // ==========================================
                if (step.type == "EndTarget") {
                    if (!targetGroupCollapsed) {
                        bool isEndHovered = (hoveredStepIndex == i);
                        // Render EndTarget footer inside the group container with visual hierarchy guide
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(6);
                        string endTreeColor = isEndHovered ? "#38bdf8" : "#64748b";
                        GUILayout.Label($"<color={endTreeColor}><b>│ └─</b></color>", labelValueStyle, GUILayout.Width(24));
                        GUILayout.BeginVertical(GetStepBoxStyle("EndTarget", isEndHovered, false, isBeingDragged));
                        GUILayout.BeginHorizontal();

                        string foldIcon = step.is_collapsed ? "⊞" : "⊟";
                        if (GUILayout.Button(foldIcon, secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
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

                        if (GUILayout.Button(isBeingDragged ? I18n.T("seq_dragging") : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 65 : 26), GUILayout.Height(20))) {
                            draggedStepIndex = isBeingDragged ? -1 : i;
                        }

                        GUILayout.Box(I18n.T("seq_end_badge"), pillEndTargetStyle, GUILayout.Width(80), GUILayout.Height(20));
                        GUILayout.Label(I18n.ColorFormat("seq_end_label", string.IsNullOrEmpty(step.label) ? I18n.T("seq_end_default") : step.label), labelValueStyle);
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                            stepToDelete = i;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();

                        if (Event.current.type == EventType.Repaint) {
                            Rect cardRect = GUILayoutUtility.GetLastRect();
                            bool isHoveredNow = cardRect.Contains(Event.current.mousePosition);
                            if (isHoveredNow) {
                                nextHoveredStepIndex = i;
                            }
                            Color themeColor = GetStepThemeColor("EndTarget");
                            float stripW = isHoveredNow ? 3.5f : 2.0f;
                            Color stripCol = isHoveredNow ? themeColor : new Color(themeColor.r, themeColor.g, themeColor.b, 0.70f);
                            GUI.DrawTexture(new Rect(cardRect.x + 1, cardRect.y + 2, stripW, cardRect.height - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, stripCol, 0, 0);

                            if (isHoveredNow) {
                                Color glowBorder = new Color(themeColor.r, themeColor.g, themeColor.b, 0.85f);
                                GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                                GUI.DrawTexture(new Rect(cardRect.x, cardRect.y + cardRect.height - 1, cardRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                                GUI.DrawTexture(new Rect(cardRect.x + cardRect.width - 1, cardRect.y, 1, cardRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                            }
                        }
                        GUILayout.EndHorizontal();
                    }

                    // CLOSE THE PROMINENT OUTER GROUP BOUNDING BOX
                    if (insideTargetGroup) {
                        GUILayout.EndVertical();
                        if (Event.current.type == EventType.Repaint) {
                            Rect groupRect = GUILayoutUtility.GetLastRect();
                            if (groupRect.Contains(Event.current.mousePosition) && nextHoveredStepIndex == -1) {
                                nextHoveredStepIndex = i;
                            }
                        }
                        GUILayout.Space(8);
                        insideTargetGroup = false;
                        targetGroupCollapsed = false;
                    }
                    continue;
                }

                // ==========================================
                // CASE 3: INNER CHILD STEPS INSIDE TARGET GROUP
                // ==========================================
                if (insideTargetGroup) {
                    if (targetGroupCollapsed) {
                        continue; // Skip rendering children when Target group is folded
                    }

                    bool isChildHovered = (hoveredStepIndex == i);

                    // Render inner step nested neatly inside the prominent group box with typed colored border
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(6); // Inner margin from outer group
                    string treeBranchColor = isChildHovered ? "#38bdf8" : "#64748b";
                    GUILayout.Label($"<color={treeBranchColor}><b>│ ├─</b></color>", labelValueStyle, GUILayout.Width(24));
                    GUILayout.BeginVertical(GetStepBoxStyle(step.type, isChildHovered, false, isBeingDragged));

                    // Step Header Bar
                    GUILayout.BeginHorizontal();
                    string foldIcon = step.is_collapsed ? "⊞" : "⊟";
                    if (GUILayout.Button(foldIcon, secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
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

                    if (GUILayout.Button(isBeingDragged ? I18n.T("seq_dragging") : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 65 : 26), GUILayout.Height(20))) {
                        draggedStepIndex = isBeingDragged ? -1 : i;
                    }

                    // Modern Pill Badge by Type with Auto Width & Overflow Prevention
                    string badgeTextChild = GetStepTypeBadgeText(step.type);
                    GUIStyle pillStyleChild = GetStepPillStyle(step.type);
                    GUILayout.Box(badgeTextChild, pillStyleChild, GUILayout.MinWidth(75), GUILayout.ExpandWidth(false), GUILayout.Height(20));

                    if (step.is_collapsed) {
                        string summaryBadge = "";
                        if (step.type == "Propagate") { string durPart = !string.IsNullOrEmpty(step.duration_str) ? $"({step.duration_str})" : (step.stop_type == "Time" ? $"({step.duration:F0}s)" : ""); summaryBadge = I18n.Format("seq_sum_prop", step.propagator, step.stop_type, durPart); }
                        else if (step.type == "Vary") summaryBadge = I18n.Format("seq_sum_vary", step.vary_variable, (!string.IsNullOrEmpty(step.vary_initial_str) ? step.vary_initial_str : step.vary_initial.ToString("F1")), step.vary_lower, step.vary_upper);
                        else if (step.type == "Achieve") summaryBadge = I18n.Format("seq_sum_achieve", step.achieve_param, (!string.IsNullOrEmpty(step.achieve_val_str) ? step.achieve_val_str : step.achieve_val.ToString("F2")), step.achieve_tolerance);
                        else if (step.type == "FiniteBurn") summaryBadge = I18n.Format("seq_sum_burn", (step.burn_use_vary_var ? step.burn_vary_var_name : (!string.IsNullOrEmpty(step.duration_str) ? step.duration_str : step.duration.ToString("F1") + "s")));
                        else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") summaryBadge = I18n.Format("seq_sum_tvc", step.thrust_dir1, step.thrust_dir2, step.thrust_dir3);
                        else if (step.type == "ImpulsiveBurn") summaryBadge = I18n.Format("seq_sum_impulse", step.dv_v, step.dv_n, step.dv_b);
                        else if (step.type == "Equation") summaryBadge = I18n.Format("seq_sum_equation", step.variable, step.expression);
                        else if (step.type == "StageSeparation") summaryBadge = I18n.Format("seq_sum_stage", step.separated_mass, step.remaining_dry_mass);
                        else summaryBadge = string.IsNullOrEmpty(step.label) ? "" : step.label;
                        GUILayout.Label(summaryBadge, labelValueStyle);
                        DrawStepCollapsedResultBadge(step);
                    } else {
                        GUILayout.Space(2);
                        step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? "" : step.label, stepLabelInputStyle, GUILayout.MinWidth(130), GUILayout.MaxWidth(280), GUILayout.Height(20));
                    }

                    GUILayout.FlexibleSpace();

                    if (step.type == "Propagate" && !step.is_collapsed) {
                        if (GUILayout.Button(I18n.T("btn_force_model2"), secondaryButtonStyle, GUILayout.Height(18))) {
                            selectedPropagatorKey = step.propagator;
                            showPropagatorConfigWindow = true;
                        }
                    }

                    if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                        stepToDelete = i;
                    }
                    GUILayout.EndHorizontal();

                    if (!step.is_collapsed) {
                        DrawStepCardDetails(i, step);
                    }

                    GUILayout.EndVertical();

                    if (Event.current.type == EventType.Repaint) {
                        Rect cardRect = GUILayoutUtility.GetLastRect();
                        bool isHoveredNow = cardRect.Contains(Event.current.mousePosition);
                        if (isHoveredNow) {
                            nextHoveredStepIndex = i;
                        }
                        Color themeColor = GetStepThemeColor(step.type);
                        float stripW = isHoveredNow ? 3.5f : 3.0f; // 基线4.2: 左侧指示条 2.0~3.5px
                        Color stripCol = isHoveredNow ? themeColor : new Color(themeColor.r, themeColor.g, themeColor.b, 0.75f);
                        GUI.DrawTexture(new Rect(cardRect.x + 1, cardRect.y + 2, stripW, cardRect.height - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, stripCol, 0, 0);

                        if (isHoveredNow) {
                            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);
                            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y + cardRect.height - 2, cardRect.width, 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);
                            GUI.DrawTexture(new Rect(cardRect.x + cardRect.width - 2, cardRect.y, 2, cardRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);
                        }
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    continue;
                }

                // ==========================================
                // CASE 4: REGULAR STANDALONE STEPS OUTSIDE GROUPS
                // ==========================================
                bool isRegularHovered = (hoveredStepIndex == i);
                GUILayout.BeginVertical(GetStepBoxStyle(step.type, isRegularHovered, false, isBeingDragged));
                GUILayout.BeginHorizontal();

                string regularFoldIcon = step.is_collapsed ? "⊞" : "⊟";
                if (GUILayout.Button(regularFoldIcon, secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
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

                if (GUILayout.Button(isBeingDragged ? I18n.T("seq_dragging") : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 65 : 26), GUILayout.Height(20))) {
                    draggedStepIndex = isBeingDragged ? -1 : i;
                }

                // Modern Pill Badge by Type with Auto Width & Overflow Prevention
                string badgeText = GetStepTypeBadgeText(step.type);
                GUIStyle pillStyle = GetStepPillStyle(step.type);
                GUILayout.Box(badgeText, pillStyle, GUILayout.MinWidth(75), GUILayout.ExpandWidth(false), GUILayout.Height(20));

                if (step.is_collapsed) {
                    string summaryBadge = "";
                    if (step.type == "Propagate") summaryBadge = I18n.Format("seq_sum_prop", step.propagator, step.stop_type, (step.stop_type == "Time" ? $"({step.duration:F0}s)" : ""));
                    else if (step.type == "Report") summaryBadge = I18n.T("seq_sum_report");
                    else if (step.type == "Equation") summaryBadge = I18n.Format("seq_sum_equation", step.variable, step.expression);
                    else if (step.type == "FiniteBurn") summaryBadge = I18n.Format("seq_sum_burn", (step.burn_use_vary_var ? step.burn_vary_var_name : step.duration.ToString("F1") + "s"));
                    else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") summaryBadge = I18n.Format("seq_sum_tvc", step.thrust_dir1, step.thrust_dir2, step.thrust_dir3);
                    else if (step.type == "ImpulsiveBurn") summaryBadge = I18n.Format("seq_sum_impulse", step.dv_v, step.dv_n, step.dv_b);
                    else summaryBadge = string.IsNullOrEmpty(step.label) ? "" : step.label;
                    GUILayout.Label(summaryBadge, labelValueStyle);
                    DrawStepCollapsedResultBadge(step);
                } else {
                    GUILayout.Space(2);
                    step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? "" : step.label, stepLabelInputStyle, GUILayout.MinWidth(130), GUILayout.MaxWidth(280), GUILayout.Height(20));
                }

                GUILayout.FlexibleSpace();

                if (step.type == "Propagate" && !step.is_collapsed) {
                    if (GUILayout.Button(I18n.T("btn_force_model2"), secondaryButtonStyle, GUILayout.Height(18))) {
                        selectedPropagatorKey = step.propagator;
                        showPropagatorConfigWindow = true;
                    }
                }

                if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                    stepToDelete = i;
                }
                GUILayout.EndHorizontal();

                if (!step.is_collapsed) {
                    DrawStepCardDetails(i, step);
                }

                GUILayout.EndVertical();

                if (Event.current.type == EventType.Repaint) {
                    Rect cardRect = GUILayoutUtility.GetLastRect();
                    bool isHoveredNow = cardRect.Contains(Event.current.mousePosition);
                    if (isHoveredNow) {
                        nextHoveredStepIndex = i;
                    }
                    Color themeColor = GetStepThemeColor(step.type);
                    float stripW = isHoveredNow ? 3.5f : 3.0f; // 基线4.2: 左侧指示条 2.0~3.5px
                    Color stripCol = isHoveredNow ? themeColor : new Color(themeColor.r, themeColor.g, themeColor.b, 0.75f);
                    GUI.DrawTexture(new Rect(cardRect.x + 1, cardRect.y + 2, stripW, cardRect.height - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, stripCol, 0, 0);

                    if (isHoveredNow) {
                        GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);
                        GUI.DrawTexture(new Rect(cardRect.x, cardRect.y + cardRect.height - 2, cardRect.width, 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);
                        GUI.DrawTexture(new Rect(cardRect.x + cardRect.width - 2, cardRect.y, 2, cardRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);
                    }
                }

                GUILayout.Space(4);
            }

            // Safety check: close unclosed Target group if sequence ends inside group
            if (insideTargetGroup) {
                GUILayout.EndVertical();
                insideTargetGroup = false;
            }

            // Drag mode bottom drop bar
            if (draggedStepIndex >= 0 && draggedStepIndex < missionSequence.Count) {
                var draggedItem = missionSequence[draggedStepIndex];
                GUILayout.Space(6);
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label(I18n.ColorFormat("seq_drag_bar", draggedStepIndex + 1, draggedItem.type, draggedItem.label), labelValueStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.Format("btn_move_end", missionSequence.Count), tabButtonActiveStyle, GUILayout.Height(24))) {
                    missionSequence.RemoveAt(draggedStepIndex);
                    missionSequence.Add(draggedItem);
                    ScreenMessages.PostScreenMessage(I18n.T("seq_moved_end"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    draggedStepIndex = -1;
                }
                if (GUILayout.Button(I18n.T("btn_cancel_drag"), secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(24))) {
                    draggedStepIndex = -1;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            // Execute insertions from Target quick buttons
            if (stepToInsertVary >= 0) {
                string parentDc = GetParentTargetDcName(stepToInsertVary);
                string suggestedVar = "burntime";
                for (int k = stepToInsertVary - 1; k >= 0; k--) {
                    if (missionSequence[k].type == "Target") break;
                    if (!string.IsNullOrEmpty(missionSequence[k].burn_name)) {
                        suggestedVar = $"{missionSequence[k].burn_name}_Duration";
                        break;
                    }
                    if (missionSequence[k].type == "BeginFiniteBurn" && !string.IsNullOrEmpty(missionSequence[k].burn_name)) {
                        suggestedVar = $"{missionSequence[k].burn_name}_Duration";
                        break;
                    }
                }
                if (suggestedVar == "burntime" && !string.IsNullOrEmpty(parentDc) && parentDc != "DefaultDC") {
                    suggestedVar = $"{parentDc}_Duration";
                }
                missionSequence.Insert(stepToInsertVary, new MissionStep {
                    type = "Vary",
                    vary_variable = suggestedVar,
                    vary_initial = 120.0,
                    vary_lower = 10.0,
                    vary_upper = 1000.0,
                    vary_max_step = 10.0,
                    dc_name = parentDc,
                    label = I18n.Format("seq_vary_label2", suggestedVar)
                });
            }
            if (stepToInsertAchieve >= 0) {
                string parentDc = GetParentTargetDcName(stepToInsertAchieve);
                string cb = GetContextCentralBody();
                double defaultVal = (cb == "Earth") ? 42164.14 : ((cb == "Luna" || cb == "Moon") ? 1837.4 : 7000.0);
                missionSequence.Insert(stepToInsertAchieve, new MissionStep {
                    type = "Achieve",
                    achieve_param = $"Sat.{cb}.SMA",
                    achieve_val = defaultVal,
                    achieve_tolerance = 1.0,
                    dc_name = parentDc,
                    label = I18n.Format("seq_achieve_label2", cb + ".SMA")
                });
            }
            if (stepToInsertImpulsive >= 0) {
                string burnName = $"Burn_{missionSequence.Count + 1}";
                missionSequence.Insert(stepToInsertImpulsive, new MissionStep {
                    type = "ImpulsiveBurn",
                    burn_name = burnName,
                    dv_v = 500.0,
                    dv_n = 0.0,
                    dv_b = 0.0,
                    burn_axes = "VNB",
                    label = I18n.Format("seq_impulse_label2", burnName)
                });
            }
            if (stepToInsertBurn >= 0) {
                string parentDc = GetParentTargetDcName(stepToInsertBurn);
                var fbStep = CreateFiniteBurnStep(I18n.T("misc_finite_burn_default"));
                if (!string.IsNullOrEmpty(parentDc) && parentDc != "DefaultDC") {
                    fbStep.burn_vary_var_name = $"{parentDc}_Duration";
                }
                missionSequence.Insert(stepToInsertBurn, fbStep);
            }
            if (stepToInsertProp >= 0) {
                string cb = GetContextCentralBody();
                string propName = GetContextPropagator(cb);
                missionSequence.Insert(stepToInsertProp, new MissionStep {
                    type = "Propagate",
                    propagator = propName,
                    stop_type = "Apoapsis",
                    label = I18n.Format("seq_prop_label2", cb + ".Apoapsis")
                });
            }
            if (stepToInsertThrustVector >= 0) {
                var curThruster = GMATStateManager.Current.GetSelectedThruster();
                string engName = curThruster != null ? curThruster.Id : "MainEngine";
                string parentDc = GetParentTargetDcName(stepToInsertThrustVector);
                missionSequence.Insert(stepToInsertThrustVector, new MissionStep {
                    type = "ThrustVectorControl",
                    dc_name = parentDc,
                    bound_thruster = engName,
                    thrust_dir1 = 1.0,
                    thrust_dir2 = 0.0,
                    thrust_dir3 = 0.0,
                    vary_dir1_lower = -1.0,
                    vary_dir1_upper = 1.0,
                    vary_dir1_max_step = 0.05,
                    vary_dir2_lower = -1.0,
                    vary_dir2_upper = 1.0,
                    vary_dir2_max_step = 0.05,
                    vary_dir3_lower = -1.0,
                    vary_dir3_upper = 1.0,
                    vary_dir3_max_step = 0.05,
                    is_vary_vector = true,
                    label = I18n.Format("seq_tvc_label2", engName)
                });
            }

            if (stepToMoveUp > 0 && stepToMoveUp < missionSequence.Count) {
                var temp = missionSequence[stepToMoveUp];
                missionSequence[stepToMoveUp] = missionSequence[stepToMoveUp - 1];
                missionSequence[stepToMoveUp - 1] = temp;
                stepToMoveUp = -1;
            }
            if (stepToMoveDown >= 0 && stepToMoveDown < missionSequence.Count - 1) {
                var temp = missionSequence[stepToMoveDown];
                missionSequence[stepToMoveDown] = missionSequence[stepToMoveDown + 1];
                missionSequence[stepToMoveDown + 1] = temp;
                stepToMoveDown = -1;
            } else if (stepToDelete >= 0 && stepToDelete < missionSequence.Count) {
                missionSequence.RemoveAt(stepToDelete);
                if (draggedStepIndex == stepToDelete) draggedStepIndex = -1;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // ==========================================
            // GMAT COMMANDS PALETTE (EXPANDABLE)
            // ==========================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>{I18n.T("seq_command_palette")}</b></color>", subHeaderStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(showCommandPalette ? I18n.T("seq_toggle_palette_hide") : I18n.T("seq_toggle_palette_show"), secondaryButtonStyle, GUILayout.Height(20))) {
                showCommandPalette = !showCommandPalette;
            }
            if (GUILayout.Button(I18n.T("seq_clear"), deleteButtonStyle, GUILayout.Width(80), GUILayout.Height(20))) {
                missionSequence.Clear();
                ClearSolutionResults();
            }
            GUILayout.EndHorizontal();

            // Always Visible Primary Quick-Add Bar
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("add_propagate"), actionButtonStyle, GUILayout.Height(24))) {
                string cb = GetContextCentralBody();
                string propName = GetContextPropagator(cb);
                missionSequence.Add(new MissionStep { type = "Propagate", propagator = propName, stop_type = "Time", duration = 3600.0, label = I18n.Format("seq_prop_label3", cb) });
            }
            if (GUILayout.Button(I18n.T("add_target"), actionButtonStyle, GUILayout.Height(24))) {
                string cb = GetContextCentralBody();
                string newDc = $"DC_{missionSequence.Count + 1}";
                double defaultVal = (cb == "Earth") ? 42164.14 : ((cb == "Luna" || cb == "Moon") ? 1837.4 : 7000.0);
                missionSequence.Add(new MissionStep { type = "Target", label = I18n.Format("seq_target_label2", newDc), dc_name = newDc, dc_algorithm = "NewtonRaphson", dc_max_iter = 50 });
                missionSequence.Add(new MissionStep { type = "Vary", vary_variable = $"{newDc}_Duration", vary_initial = 120.0, vary_lower = 10.0, vary_upper = 1000.0, vary_max_step = 10.0, dc_name = newDc, label = I18n.Format("seq_vary_label3", newDc) });
                missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = $"Sat.{cb}.SMA", achieve_val = defaultVal, achieve_tolerance = 1.0, dc_name = newDc, label = I18n.Format("seq_achieve_label3", cb + ".SMA") });
                missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.Format("seq_end_label2", newDc), dc_name = newDc });
            }
            if (GUILayout.Button(I18n.T("add_finiteburn"), secondaryButtonStyle, GUILayout.Height(24))) {
                missionSequence.Add(CreateFiniteBurnStep(I18n.T("seq_finite_burn_label")));
            }
            if (GUILayout.Button(I18n.T("add_impulsive"), secondaryButtonStyle, GUILayout.Height(24))) {
                string burnName = $"Burn_{missionSequence.Count + 1}";
                missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", burn_name = burnName, dv_v = 500.0, dv_n = 0.0, dv_b = 0.0, label = I18n.Format("seq_impulse_label2", burnName) });
            }
            if (GUILayout.Button(I18n.T("add_stagesep"), secondaryButtonStyle, GUILayout.Height(24))) {
                double curDry = vessel != null ? vessel.totalMass * 1000.0 * 0.4 : 1500.0;
                missionSequence.Add(new MissionStep { type = "StageSeparation", separated_mass = 2500.0, remaining_dry_mass = curDry, label = I18n.T("seq_stage_label") });
            }
            if (GUILayout.Button(I18n.T("add_report"), secondaryButtonStyle, GUILayout.Height(24))) {
                missionSequence.Add(new MissionStep { type = "Report", label = I18n.T("seq_report_label") });
            }
            GUILayout.EndHorizontal();

            // Expanded 27 GMAT Command Library Rows
            if (showCommandPalette) {
                GUILayout.Space(4);

                // Row 1: Targeting & Maneuvers
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_palette_targeting"), labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("Target", secondaryButtonStyle, GUILayout.Height(20))) {
                    string dcName = $"DC_{missionSequence.Count + 1}";
                    missionSequence.Add(new MissionStep { type = "Target", label = I18n.Format("seq_target_label3", dcName), dc_name = dcName });
                }
                if (GUILayout.Button("Vary", secondaryButtonStyle, GUILayout.Height(20))) {
                    string activeDc = GetAvailableSolverList()[0];
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = $"{activeDc}_Duration", vary_initial = 120.0, vary_lower = 10.0, vary_upper = 1000.0, vary_max_step = 10.0, dc_name = activeDc, label = I18n.Format("seq_vary_label4", activeDc) });
                }
                if (GUILayout.Button("Achieve", secondaryButtonStyle, GUILayout.Height(20))) {
                    string cb = GetContextCentralBody();
                    string activeDc = GetAvailableSolverList()[0];
                    double defaultVal = (cb == "Earth") ? 42164.14 : 7000.0;
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = $"Sat.{cb}.SMA", achieve_val = defaultVal, achieve_tolerance = 1.0, dc_name = activeDc, label = I18n.Format("seq_achieve_label4", cb) });
                }
                if (GUILayout.Button("EndTarget", secondaryButtonStyle, GUILayout.Height(20))) {
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = I18n.T("seq_end_label3") });
                }
                if (GUILayout.Button("BeginFiniteBurn", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "BeginFiniteBurn", burn_name = "Finite_1", label = I18n.T("seq_burnbegin_label") });
                if (GUILayout.Button("EndFiniteBurn", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndFiniteBurn", burn_name = "Finite_1", label = I18n.T("seq_burnend_label") });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: Optimization
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_palette_optimization"), labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("Optimize", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Optimize", label = I18n.T("seq_opt_label"), optimizer_name = "SQP1" });
                if (GUILayout.Button("Minimize", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Minimize", minimize_variable = "Sat.FuelMassBurned", optimizer_name = "SQP1", label = I18n.T("seq_min_label") });
                if (GUILayout.Button("NonlinearConstraint", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "NonlinearConstraint", constraint_lhs = "Sat.Earth.RMAG", constraint_op = ">=", constraint_val = 6578.14, label = I18n.T("seq_nlc_label") });
                if (GUILayout.Button("EndOptimize", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndOptimize", label = I18n.T("seq_endopt_label") });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 3: Control Flow & Logic
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_palette_flow"), labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("If", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "If", condition_expr = "Sat.Earth.RMAG < 7000.0", label = I18n.T("seq_if_label") });
                if (GUILayout.Button("Else", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Else", label = I18n.T("seq_else_label") });
                if (GUILayout.Button("EndIf", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndIf", label = I18n.T("seq_endif_label") });
                if (GUILayout.Button("For", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "For", for_var = "i", for_range = "1:1:2", label = I18n.T("seq_for_label") });
                if (GUILayout.Button("EndFor", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndFor", label = I18n.T("seq_endfor_label") });
                if (GUILayout.Button("While", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "While", condition_expr = "Sat.ElapsedDays < 3.0", label = I18n.T("seq_while_label") });
                if (GUILayout.Button("EndWhile", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndWhile", label = I18n.T("seq_endwhile_label") });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 4: Math, Variables & IO
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_palette_math"), labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button(I18n.T("btn_equation"), secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Equation", variable = "burntime", expression = "120.0", label = I18n.T("seq_equation_label") });
                if (GUILayout.Button(I18n.T("btn_scriptevent"), secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "ScriptEvent", script_content = "GMAT burntime = 150.0;", label = I18n.T("seq_scriptevent_label") });
                if (GUILayout.Button(I18n.T("btn_write"), secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Write", write_fields = "Sat.UTCGregorian Sat.RMAG", label = I18n.T("seq_write_label") });
                if (GUILayout.Button(I18n.T("btn_toggle"), secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Toggle", subscriber_name = "EarthInertialView", action_state = "Off", label = I18n.T("seq_toggle_label") });
                if (GUILayout.Button(I18n.T("btn_stop_palette"), secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Stop", label = I18n.T("seq_stop_label") });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUILayout.Space(12);

            // =======================================================
            // RIGHT COLUMN: Pinned Execution & Live Results (Responsive Scrollable)
            // =======================================================
            GUILayout.BeginVertical(GUILayout.Width(rightColW));
            rightColScrollPos = GUILayout.BeginScrollView(rightColScrollPos, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // Pinned Execution Box (with Live Progress Bar & Step Flow HUD)
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("seq_solver_center"), headerStyle);
            GUILayout.FlexibleSpace();
            if (isPropagating) {
                GUILayout.Label(I18n.ColorFormat("seq_elapsed", solveTotalTime), labelValueStyle);
            } else if (solveTotalTime > 0) {
                GUILayout.Label(I18n.ColorFormat("seq_last_elapsed", solveTotalTime), labelValueStyle);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (isPropagating) {
                GUILayout.BeginHorizontal();
                GUILayout.Button(I18n.T("btn_solving"), actionButtonStyle, GUILayout.Height(38), GUILayout.ExpandWidth(true));
                if (GUILayout.Button(I18n.T("btn_abort"), deleteButtonStyle, GUILayout.Width(110), GUILayout.Height(38))) {
                    GMATClient.AbortCurrentExecution();
                    isPropagating = false;
                    solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                    solveStageText = I18n.Format("solve_user_abort", solveTotalTime);
                    solveProgress = 0f;
                    ScreenMessages.PostScreenMessage(I18n.T("solve_user_abort_msg"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();
            } else {
                GUI.enabled = (vessel != null);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("btn_start_solve"), actionButtonStyle, GUILayout.Height(38), GUILayout.ExpandWidth(true))) {
                    isPropagating = true;
                    solveStartTime = Time.realtimeSinceStartup;
                    solveProgress = 0.05f;
                    solveStageText = I18n.T("solve_stage_1");
                    solveCurrentStepIndex = 0;

                    // Ensure telemetry is live
                    if (cachedEngines == null || cachedEngines.Count == 0) cachedEngines = ROEngineInterop.ScanVesselEngines(vessel);
                    if (cachedTanks == null || cachedTanks.Count == 0) cachedTanks = ROEngineInterop.ScanVesselTanks(vessel);
                    if (cachedStages == null || cachedStages.Count == 0) cachedStages = MechJebInterop.GetStages(vessel);

                    // Auto-resolve active engine from sequence steps or highest thrust engine
                    ROEngineInfo selectedEngine = null;
                    string boundEngineId = "";
                    foreach (var st in missionSequence) {
                        if (st.type == "FiniteBurn" && !string.IsNullOrEmpty(st.bound_thruster)) {
                            boundEngineId = st.bound_thruster;
                            break;
                        }
                    }
                    if (cachedEngines != null && cachedEngines.Count > 0) {
                        if (!string.IsNullOrEmpty(boundEngineId)) {
                            selectedEngine = cachedEngines.Find(e => GMATParameterRegistry.SanitizeGmatIdentifier(e.title) == boundEngineId || e.title.Contains(boundEngineId));
                        }
                        if (selectedEngine == null && selectedEngineIndex < cachedEngines.Count) {
                            selectedEngine = cachedEngines[selectedEngineIndex];
                        }
                        if (selectedEngine == null) {
                            selectedEngine = cachedEngines[0];
                        }
                    }

                    // Auto-resolve all stages with propellant or default to full vessel staging
                    List<StageData> selectedStages = GetSelectedStagesList();
                    if (selectedStages == null || selectedStages.Count == 0 || (selectedEngine != null && !selectedStages.Exists(s => s.stageNumber == selectedEngine.stageNumber))) {
                        selectedStages = (cachedStages != null && cachedStages.Count > 0) ? cachedStages : new List<StageData>();
                    }

                    string cBody = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth";
                    string scriptToSend = (ideScriptViewMode == 1 && !string.IsNullOrEmpty(ideCustomBuffer)) 
                        ? ideCustomBuffer 
                        : GMATScriptGenerator.GenerateScript(vessel, missionSequence, cBody, propagatorConfigs);

                    GMATClient.PropagateMissionWithRawScript(vessel, selectedStages, selectedEngine, missionSequence, propagatorConfigs, scriptToSend, (success, result) => {
                        isPropagating = false;
                        solveProgress = success ? 1.0f : 0.0f;
                        solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                        solveStageText = success ? I18n.Format("seq_solved_stage", solveTotalTime) : I18n.T("seq_solve_failed");
                        latestResult = result;

                        SyncSolvedValuesToSequence(result);
                        if (success) {
                            if (result != null && result.trajectory != null && result.trajectory.Count > 0) {
                                simulationTexture = GMATOrbitCanvas.RenderTrajectoryTexture(result.trajectory, cBody);
                            }
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_solve_success", solveTotalTime), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                        } else {
                            string errTxt = (result != null && !string.IsNullOrEmpty(result.error)) ? result.error : I18n.T("seq_guess_unconverged");
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_solve_hint", errTxt), 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    });
                }
                if (GUILayout.Button(I18n.T("btn_clear_residual"), secondaryButtonStyle, GUILayout.Width(100), GUILayout.Height(38))) {
                    ClearSolutionResults();
                }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }

            // =========================================================================
            // LIVE SOLVER PROGRESS BAR & STEP FLOW HUD
            // =========================================================================
            GUILayout.Space(6);
            DrawProgressBar(solveProgress, isPropagating ? solveStageText : (latestResult != null && latestResult.success ? I18n.Format("seq_progress_done", latestResult.solved_burns.Count) : solveStageText), 24f);

            // Step Pipeline Flow Tracker Ribbon
            if (missionSequence != null && missionSequence.Count > 0) {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_flow_label"), labelValueStyle, GUILayout.Width(60));

                int maxShow = Math.Min(5, missionSequence.Count);
                for (int s = 0; s < maxShow; s++) {
                    var st = missionSequence[s];
                    string sIcon = isPropagating ? (s < solveCurrentStepIndex ? "<color=#34d399>✔</color>" : (s == solveCurrentStepIndex ? "<color=#facc15>●</color>" : "<color=#64748b>○</color>")) : (latestResult != null && latestResult.success ? "<color=#34d399>✔</color>" : "<color=#64748b>○</color>");
                    string shortLabel = (st.type == "Propagate") ? I18n.T("flow_propagate") : (st.type == "Target") ? I18n.T("flow_target") : (st.type == "Vary") ? I18n.T("flow_vary") : (st.type == "Achieve") ? I18n.T("flow_achieve") : (st.type == "FiniteBurn" || st.type == "ImpulsiveBurn" || st.type == "Maneuver") ? I18n.T("flow_burn") : st.type;
                    GUILayout.Label($"{sIcon} #{s + 1} {shortLabel}", labelValueStyle);
                }
                if (missionSequence.Count > maxShow) {
                    GUILayout.Label(I18n.ColorFormat("seq_more_steps", missionSequence.Count - maxShow), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.Space(6);

            // =========================================================================
            // CARD 2: Pinned Live Solution Summary Box & Differential Corrector Matrix
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label(I18n.T("seq_board_header"), headerStyle);
            GUILayout.Space(3);

            if (latestResult != null && latestResult.success) {
                // 1. Top Orbital Elements Snapshot Frame
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("seq_final_sma", latestResult.final_sma), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.ColorFormat("seq_final_ecc", latestResult.final_ecc), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.ColorFormat("seq_final_inc", latestResult.final_inc), labelValueStyle);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // 2. 打靶器收敛状态矩阵 (Per-DC Matrix)
                if (latestResult.dc_summaries != null && latestResult.dc_summaries.Count > 0) {
                    GUILayout.Space(3);
                    GUILayout.Label(I18n.T("seq_dc_matrix_header"), labelValueStyle);
                    
                    foreach (var dcp in latestResult.dc_summaries) {
                        string dcName = dcp.Key;
                        var dcs = dcp.Value;
                        string statusTag = dcs.converged ? I18n.Format("seq_dc_converged", dcs.iterations) : I18n.T("seq_dc_notconv");

                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.ColorFormat("seq_dc_name", dcName), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(statusTag, labelValueStyle);
                        GUILayout.EndHorizontal();

                        // 显示解算变量
                        if (dcs.final_variables != null && dcs.final_variables.Count > 0) {
                            GUILayout.Space(2);
                            var varStrs = new List<string>();
                            foreach (var v in dcs.final_variables) {
                                if (!v.Key.Contains(".")) { // 只显示主变量名
                                    varStrs.Add($"<b>{v.Key}</b> = <color=#38bdf8><b>{v.Value:F3}</b></color>");
                                }
                            }
                            if (varStrs.Count > 0) {
                                GUILayout.Label(I18n.ColorFormat("seq_var_solution", string.Join(" | ", varStrs.ToArray())), labelValueStyle);
                            }
                        }

                        // 显示达成目标
                        if (dcs.goals_summary != null && dcs.goals_summary.Count > 0) {
                            GUILayout.Space(2);
                            foreach (var g in dcs.goals_summary) {
                                double err = g.Achieved - g.Desired;
                                string col = Math.Abs(err) < 1.0 ? "#34d399" : "#facc15";
                                GUILayout.Label(I18n.ColorFormat("seq_goal_achieved", g.GoalName, col, g.Achieved, g.Desired), labelValueStyle);
                            }
                        }

                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                // 3. 解算机动列表 (Solved Burns Frames)
                if (latestResult.solved_burns != null && latestResult.solved_burns.Count > 0) {
                    GUILayout.Space(3);
                    GUILayout.Label(I18n.ColorFormat("seq_burns_header", latestResult.solved_burns.Count), labelValueStyle);
                    for (int bIdx = 0; bIdx < latestResult.solved_burns.Count; bIdx++) {
                        var sb = latestResult.solved_burns[bIdx];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.ColorFormat("seq_burn_item", bIdx + 1, sb.name, sb.type), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(I18n.ColorFormat("seq_burn_dv", sb.magnitude), labelValueStyle);
                        GUILayout.EndHorizontal();
                        GUILayout.Space(2);
                        GUILayout.Label(I18n.ColorFormat("seq_burn_thrust", sb.dv_v, sb.dv_n, sb.dv_b), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_burn_timing", sb.duration, sb.thrust_kn, sb.isp), labelValueStyle);
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                if (latestResult.has_fuel_deficit) {
                    GUILayout.Space(2);
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.Label(I18n.ColorFormat("seq_fuel_deficit", latestResult.fuel_deficit), labelValueStyle);
                    GUILayout.EndVertical();
                }
            } else if (latestResult != null && !latestResult.success) {
                string shortError = (latestResult.error ?? I18n.T("seq_solve_fail_short")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                GUILayout.Label(I18n.ColorFormat("seq_solve_fail_label", shortError), labelValueStyle);
                
                // Detailed Error Diagnostic Box (Constrained & Scrollable)
                string errDetails = !string.IsNullOrEmpty(latestResult.error_details) ? latestResult.error_details : latestResult.error;
                if (!string.IsNullOrEmpty(errDetails)) {
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("seq_err_diag_label"), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    string foldTxt = showFullErrorLog ? I18n.T("btn_fold_log_close") : I18n.T("btn_fold_log_open");
                    if (GUILayout.Button(foldTxt, secondaryButtonStyle, GUILayout.Height(18))) {
                        showFullErrorLog = !showFullErrorLog;
                    }
                    GUILayout.EndHorizontal();

                    if (showFullErrorLog) {
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        rightErrorScrollPos = GUILayout.BeginScrollView(rightErrorScrollPos, GUILayout.Height(110));
                        GUILayout.Label($"<color=#fca5a5><size=11>{errDetails}</size></color>", labelValueStyle);
                        GUILayout.EndScrollView();
                        GUILayout.EndVertical();

                        if (GUILayout.Button(I18n.T("btn_copy_err_log"), secondaryButtonStyle, GUILayout.Height(22))) {
                            GUIUtility.systemCopyBuffer = $"[GMAT Error]\nSummary: {shortError}\nDetails:\n{errDetails}\nScript: {latestResult.script_path}";
                            ScreenMessages.PostScreenMessage(I18n.T("seq_err_log_copied"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
            } else {
                GUILayout.Label(I18n.T("seq_ready_hint"), labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // =========================================================================
            // CARD 3: VEHICLE & PROPULSION SNAPSHOT (STAGING & ENGINES)
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            var b = GMATStateManager.Current.GetActiveStageBudget();
            GUILayout.Label(I18n.T("seq_stage_budget_header"), headerStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(I18n.T("seq_live_sync"), labelValueStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(3);

            if (!string.IsNullOrEmpty(b.EngineTitle)) {
                // Frame 1: Stage Mass & Delta-V Summary Frame
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("seq_stage_config", b.StageNumber), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.ColorFormat("seq_stage_dv", b.DeltaV), labelValueStyle);
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
                GUILayout.Label(I18n.ColorFormat("seq_mass_alloc", b.WetMassTonnes, b.FuelMassTonnes, b.DryMassTonnes), labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.Space(3);

                // Frame 2: Hardware & Propellant Specs Frame
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.ColorFormat("seq_engine_spec", b.EngineTitle, b.ThrustKN, b.IspVac), labelValueStyle);
                GUILayout.Label(I18n.ColorFormat("seq_propellant_spec", b.PropellantDesc, b.FuelDensityKgM3), labelValueStyle);
                GUILayout.EndVertical();
            } else if (vessel != null) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.ColorFormat("seq_vessel_mass", vessel.GetTotalMass()), labelValueStyle);
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // =========================================================================
            // CARD 4: TOOLS & EXTERNAL INTEROP
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label(I18n.T("seq_tools_header"), headerStyle);
            GUILayout.Space(2);

            if (GUILayout.Button(I18n.T("btn_view_traj_image"), actionButtonStyle, GUILayout.Height(26))) {
                LoadSimulationTexture();
                showSimulationImageWindow = true;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("btn_prop_cfg"), secondaryButtonStyle, GUILayout.Height(24))) {
                showPropagatorConfigWindow = true;
            }
            if (GUILayout.Button(I18n.T("btn_craft_params"), secondaryButtonStyle, GUILayout.Height(24))) {
                showCraftDetailsWindow = true;
            }
            GUILayout.EndHorizontal();

            GUI.enabled = (latestResult != null && latestResult.success && latestResult.solved_burns.Count > 0);
            if (GUILayout.Button(I18n.T("res_write_principia"), successButtonStyle, GUILayout.Height(30))) {
                WriteBurnsToPrincipia(vessel);
            }
            GUI.enabled = (latestResult != null && latestResult.success);

            if (GUILayout.Button(I18n.T("btn_open_gmat_desktop"), secondaryButtonStyle, GUILayout.Height(24))) {
                if (!string.IsNullOrEmpty(latestResult.script_path)) {
                    GMATClient.OpenGmatDesktop(latestResult.script_path);
                    ScreenMessages.PostScreenMessage(I18n.T("seq_starting_gmat_desktop"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUI.enabled = true;

            GUILayout.EndVertical();

            GUILayout.EndScrollView(); // Closes rightColScrollPos
            GUILayout.EndVertical();   // Closes right column

            GUILayout.EndHorizontal();
        }

        private static bool IsPureOrbitalEvent(string stopType) {
            if (string.IsNullOrEmpty(stopType)) return false;
            string s = stopType.ToLower().Trim();
            return s == "apoapsis" || s == "periapsis" || s.EndsWith(".apoapsis") || s.EndsWith(".periapsis")
                || s == "node" || s == "ascendingnode" || s == "descendingnode" || s.EndsWith(".node")
                || s.EndsWith(".ascendingnode") || s.EndsWith(".descendingnode");
        }

        private void DrawStepCardDetails(int i, MissionStep step) {
            // =========================================================================
            // 1. PROPAGATE STEP CARD (CLEAN IDE INTELLISENSE)
            // =========================================================================
            if (step.type == "Propagate") {
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_prop_label"), labelValueStyle, GUILayout.Width(85));

                List<string> props = GMATParameterRegistry.GetAllPropagators();
                for (int p = 0; p < props.Count; p++) {
                    string pr = props[p];
                    bool isCur = (step.propagator == pr);
                    if (GUILayout.Button(pr, isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(18))) {
                        step.propagator = pr;
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Stop Condition Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_stop_cond_label"), labelValueStyle, GUILayout.Width(65));

                GUI.SetNextControlName($"Field_PropStop_{i}");
                string prevStop = step.stop_type ?? "Sat.Luna.Periapsis";
                string newStop = GUILayout.TextField(prevStop, GUILayout.MinWidth(180), GUILayout.Width(220), GUILayout.Height(20));

                if (Event.current.type == EventType.Repaint) {
                    if (GUI.GetNameOfFocusedControl() == $"Field_PropStop_{i}") {
                        Rect r = GUILayoutUtility.GetLastRect();
                        Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                        intelliSenseAnchorRect = new Rect(sp.x, sp.y, r.width, r.height);
                    }
                }
                if (newStop != prevStop || (GUI.GetNameOfFocusedControl() == $"Field_PropStop_{i}" && !intelliSenseOpen && !intelliSenseDismissed)) {
                    step.stop_type = newStop;
                    TriggerIntelliSense(newStop, "prop_stop", i);
                }

                if (GUILayout.Button(I18n.T("btn_cond_library"), secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
                    OpenParamPicker(i, "prop_stop");
                }

                string propBadge = GMATParameterRegistry.GetParamBadgeI18n(step.stop_type);
                if (!string.IsNullOrEmpty(propBadge)) {
                    GUILayout.Label($"<color=#38bdf8><b>[{propBadge}]</b></color>", labelValueStyle);
                }

                if (IsPureOrbitalEvent(step.stop_type)) {
                    GUILayout.Label(I18n.T("seq_orbital_event"), labelValueStyle);
                } else {
                    GUILayout.Space(8);
                    GUILayout.Label(I18n.T("seq_target_threshold"), gridLabelStyle, GUILayout.Width(95));

                    string curRaw = !string.IsNullOrEmpty(step.target_val_str) ? step.target_val_str : (!string.IsNullOrEmpty(step.duration_str) ? step.duration_str : null);
                    double defNum = step.target_val != 0.0 ? step.target_val : (step.duration > 0.0 ? step.duration : 3600.0);

                    string newRaw = DrawDelayedSmartField($"prop_tval_{i}", curRaw, defNum, numericInputStyle, GUILayout.Width(130), "prop_val", i);
                    if (newRaw != curRaw) {
                        step.target_val_str = newRaw;
                        step.duration_str = newRaw;
                        if (double.TryParse(newRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) {
                            step.target_val = dVal;
                            step.duration = dVal;
                            step.burn_use_vary_var = false;
                        } else {
                            step.burn_use_vary_var = true;
                            step.burn_vary_var_name = newRaw;
                        }
                    }

                    // Variable Name Reference Badge
                    if (GMATStateManager.Current != null && GMATStateManager.Current.Variables != null) {
                        var matchedVar = GMATStateManager.Current.Variables.Find(v => v.Name.Equals(step.target_val_str, StringComparison.OrdinalIgnoreCase));
                        if (matchedVar != null) {
                            GUILayout.Label(I18n.ColorFormat("widget_var_badge", matchedVar.Name, matchedVar.InitialValue), labelValueStyle);
                        }
                    }
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();


            }

            // =========================================================================
            // 2. VARY STEP CARD (STREAMLINED COMPACT 2-ROW WITH MULTI-VECTOR CHIPS)
            // =========================================================================
            else if (step.type == "Vary") {
                // Row 1: Fully Expanding Variable Input + Official GMAT [Edit] Dialog Button (Image 3)
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_vary_var_label"), labelValueStyle, GUILayout.Width(130));

                GUI.SetNextControlName($"Field_Vary_{i}");
                string prevVary = step.vary_variable ?? "burntime";
                string newVary = GUILayout.TextField(prevVary, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true), GUILayout.Height(22));
                if (newVary != prevVary) step.vary_variable = newVary;

                if (GUILayout.Button(I18n.T("btn_edit_var"), tabButtonActiveStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                    OpenGMATParameterSelectDialog(i, "vary", step.vary_variable);
                }

                string varyBadge = GMATParameterRegistry.GetParamBadgeI18n(step.vary_variable);
                if (!string.IsNullOrEmpty(varyBadge)) {
                    GUILayout.Label($"<color=#facc15><b>[{varyBadge}]</b></color>", labelValueStyle);
                }

                if (Event.current.type == EventType.Repaint) {
                    if (GUI.GetNameOfFocusedControl() == $"Field_Vary_{i}") {
                        Rect r = GUILayoutUtility.GetLastRect();
                        Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                        intelliSenseAnchorRect = new Rect(sp.x, sp.y, r.width, r.height);
                    }
                }
                if (newVary != prevVary || (GUI.GetNameOfFocusedControl() == $"Field_Vary_{i}" && !intelliSenseOpen && !intelliSenseDismissed)) {
                    step.vary_variable = newVary;
                    TriggerIntelliSense(newVary, "vary", i);
                }

                GUILayout.Space(10);
                DrawInlineSolverPicker(step, i);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: Search Bounds & Inline Solved Value Result
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_init_label"), gridLabelStyle, GUILayout.Width(35));
                step.vary_initial_str = DrawDelayedSmartField($"Field_VaryInit_{i}", step.vary_initial_str, step.vary_initial, numericInputStyle, GUILayout.Width(75), "vary_init", i);
                if (double.TryParse(step.vary_initial_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryInit)) {
                    step.vary_initial = parsedVaryInit;
                }

                GUILayout.Space(6);
                GUILayout.Label(I18n.T("seq_lower_label"), gridLabelStyle, GUILayout.Width(35));
                step.vary_lower_str = DrawDelayedSmartField($"vary_low_{i}", step.vary_lower_str, step.vary_lower, numericInputStyle, GUILayout.Width(60), null, i);
                if (double.TryParse(step.vary_lower_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryLow)) {
                    step.vary_lower = parsedVaryLow;
                }

                GUILayout.Space(6);
                GUILayout.Label(I18n.T("seq_upper_label"), gridLabelStyle, GUILayout.Width(35));
                step.vary_upper_str = DrawDelayedSmartField($"vary_up_{i}", step.vary_upper_str, step.vary_upper, numericInputStyle, GUILayout.Width(60), null, i);
                if (double.TryParse(step.vary_upper_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryUp)) {
                    step.vary_upper = parsedVaryUp;
                }

                GUILayout.Space(6);
                GUILayout.Label(I18n.T("seq_step_label"), gridLabelStyle, GUILayout.Width(35));
                step.vary_max_step_str = DrawDelayedSmartField($"vary_step_{i}", step.vary_max_step_str, (step.vary_max_step <= 0 ? 0.1 : step.vary_max_step), numericInputStyle, GUILayout.Width(55), null, i);
                if (double.TryParse(step.vary_max_step_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryStep)) {
                    step.vary_max_step = parsedVaryStep;
                }

                // Inline Solved Value
                if (step.has_solved_value) {
                    string unitStr = (step.vary_variable != null && (step.vary_variable.Contains("Direction") || step.vary_variable.Contains("Thrust"))) ? "" : ((step.vary_variable != null && step.vary_variable.Contains("Element")) ? "km/s" : "s");
                    GUILayout.Space(12);
                    GUILayout.Label(I18n.ColorFormat("seq_solved_value", step.solved_value_str, unitStr), labelValueStyle);
                    if (GUILayout.Button(I18n.T("btn_apply_guess"), secondaryButtonStyle, GUILayout.Height(20))) {
                        step.vary_initial = step.solved_value;
                        step.vary_initial_str = step.solved_value_str;
                        inputStringCache[$"Field_VaryInit_{i}"] = step.solved_value_str;
                        ScreenMessages.PostScreenMessage(I18n.Format("seq_guess_applied", step.solved_value_str), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                } else if (latestResult != null && latestResult.success) {
                    double solvedVal = -1.0;
                    if (latestResult.dc_summary != null && latestResult.dc_summary.final_variables != null) {
                        string vKey = step.vary_variable ?? "burntime";
                        if (latestResult.dc_summary.final_variables.ContainsKey(vKey)) {
                            solvedVal = latestResult.dc_summary.final_variables[vKey];
                        }
                    }
                    if (solvedVal > 0) {
                        string unitStr = (step.vary_variable != null && step.vary_variable.Contains("Element")) ? "km/s" : "s";
                        GUILayout.Space(12);
                        GUILayout.Label(I18n.ColorFormat("seq_solved_value", solvedVal.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), unitStr), labelValueStyle);
                        if (GUILayout.Button(I18n.T("btn_apply_guess"), secondaryButtonStyle, GUILayout.Height(20))) {
                            step.vary_initial = solvedVal;
                            step.vary_initial_str = solvedVal.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                            inputStringCache[$"Field_VaryInit_{i}"] = step.vary_initial_str;
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_guess_applied", solvedVal.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // =========================================================================
            // 3. ACHIEVE STEP CARD (STREAMLINED COMPACT 2-ROW LAYOUT)
            // =========================================================================
            else if (step.type == "Achieve") {
                // Row 1: Target Goal Input + i18n Badge + [Edit] Dialog + Solver Selector
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_goal_label"), labelValueStyle, GUILayout.Width(110));

                GUI.SetNextControlName($"Field_Achieve_{i}");
                string prevAchieve = step.achieve_param ?? "Sat.Earth.Altitude";
                string newAchieve = GUILayout.TextField(prevAchieve, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true), GUILayout.Height(22));

                if (GUILayout.Button(I18n.T("btn_edit_goal"), tabButtonActiveStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                    OpenGMATParameterSelectDialog(i, "achieve", step.achieve_param);
                }

                string achBadge = GMATParameterRegistry.GetParamBadgeI18n(step.achieve_param);
                if (!string.IsNullOrEmpty(achBadge)) {
                    GUILayout.Label($"<color=#facc15><b>[{achBadge}]</b></color>", labelValueStyle);
                }

                if (Event.current.type == EventType.Repaint) {
                    if (GUI.GetNameOfFocusedControl() == $"Field_Achieve_{i}") {
                        Rect r = GUILayoutUtility.GetLastRect();
                        Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                        intelliSenseAnchorRect = new Rect(sp.x, sp.y, r.width, r.height);
                    }
                }
                if (newAchieve != prevAchieve || (GUI.GetNameOfFocusedControl() == $"Field_Achieve_{i}" && !intelliSenseOpen && !intelliSenseDismissed)) {
                    step.achieve_param = newAchieve;
                    TriggerIntelliSense(newAchieve, "achieve", i);
                }

                GUILayout.Space(10);
                DrawInlineSolverPicker(step, i);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: Desired Value + Tolerance + Inline Solved Result
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_target_val_label"), gridLabelStyle, GUILayout.Width(75));
                step.achieve_val_str = DrawDelayedSmartField($"Field_AchVal_{i}", step.achieve_val_str, step.achieve_val, numericInputStyle, GUILayout.Width(95), "achieve_val", i);
                if (double.TryParse(step.achieve_val_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedAchVal)) {
                    step.achieve_val = parsedAchVal;
                }

                GUILayout.Space(8);
                GUILayout.Label(I18n.T("seq_tolerance_label"), gridLabelStyle, GUILayout.Width(50));
                step.achieve_tolerance_str = DrawDelayedSmartField($"Field_AchTol_{i}", step.achieve_tolerance_str, step.achieve_tolerance, numericInputStyle, GUILayout.Width(65), null, i);
                if (double.TryParse(step.achieve_tolerance_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedAchTol)) {
                    step.achieve_tolerance = parsedAchTol;
                }

                // Inline Solved Result
                if (step.has_achieve_result) {
                    string col = step.achieve_passed ? "#34d399" : "#f87171";
                    string icon = step.achieve_passed ? "✓" : "✗";
                    string convStatus = step.solver_converged ? I18n.T("seq_converged") : I18n.T("seq_notconverged");
                    GUILayout.Space(12);
                    GUILayout.Label(I18n.ColorFormat("seq_achieve_result", col, icon, step.achieved_value, step.achieved_variance, step.solver_iterations, convStatus), labelValueStyle);
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // =========================================================================
            // 4. IMPULSIVE BURN / MANEUVER (WITH VISUAL TANK & ENGINE SELECTOR & SYNC)
            // =========================================================================
            else if (step.type == "ImpulsiveBurn" || step.type == "Maneuver") {
                Vessel v = FlightGlobals.ActiveVessel;
                var allTanks = ROEngineInterop.ScanVesselTanks(v);
                var allEngines = ROEngineInterop.ScanVesselEngines(v);

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_impulse_header"), labelValueStyle, GUILayout.Width(170));
                step.burn_name = GUILayout.TextField(string.IsNullOrEmpty(step.burn_name) ? "ImpulsiveBurn" : step.burn_name, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110), GUILayout.Height(20));

                GUILayout.Label(I18n.T("seq_axes_label"), labelValueStyle, GUILayout.Width(45));
                step.burn_axes = GUILayout.TextField(string.IsNullOrEmpty(step.burn_axes) ? "VNB" : step.burn_axes, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));

                GUILayout.Label(I18n.T("seq_origin_label"), labelValueStyle, GUILayout.Width(60));
                step.burn_origin = GUILayout.TextField(string.IsNullOrEmpty(step.burn_origin) ? "Earth" : step.burn_origin, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(65), GUILayout.Height(20));

                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.T("seq_live_hw"), labelValueStyle);
                GUILayout.EndHorizontal();

                // Delta-V Components Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_dv_v"), gridLabelStyle, GUILayout.Width(105));
                step.dv_v = DrawDelayedNumericField($"burn_v_{i}", step.dv_v, numericInputStyle, GUILayout.Width(70), "F2");

                GUILayout.Space(8);
                GUILayout.Label(I18n.T("seq_dv_n"), gridLabelStyle, GUILayout.Width(70));
                step.dv_n = DrawDelayedNumericField($"burn_n_{i}", step.dv_n, numericInputStyle, GUILayout.Width(65), "F2");

                GUILayout.Space(8);
                GUILayout.Label(I18n.T("seq_dv_b"), gridLabelStyle, GUILayout.Width(70));
                step.dv_b = DrawDelayedNumericField($"burn_b_{i}", step.dv_b, numericInputStyle, GUILayout.Width(65), "F2");

                double totalDv = Math.Sqrt(step.dv_v * step.dv_v + step.dv_n * step.dv_n + step.dv_b * step.dv_b);
                GUILayout.Space(12);
                GUILayout.Label(I18n.ColorFormat("seq_total_dv", totalDv), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Hardware Binding Section
                GUILayout.Space(3);
                GUILayout.BeginVertical(nestedCardBoxStyle);

                // Engine Selector Row
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_burn_engine"), labelValueStyle, GUILayout.Width(90));
                if (allEngines.Count > 0) {
                    for (int eg = 0; eg < allEngines.Count; eg++) {
                        var eng = allEngines[eg];
                        string engId = GMATParameterRegistry.SanitizeGmatIdentifier(eng.title, "Engine");
                        bool isCurEng = (step.bound_thruster == engId) || (string.IsNullOrEmpty(step.bound_thruster) && eg == 0);
                        if (GUILayout.Button(I18n.Format("seq_engine_btn2", eng.title, eng.thrustKN, eng.ispVac), isCurEng ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            step.bound_thruster = engId;
                            step.burn_isp = eng.ispVac;
                            step.burn_decrement_mass = true;
                            selectedEngineIndex = eg;
                            selectedStageIndices.Clear();
                            if (cachedStages != null) {
                                for (int s = 0; s < cachedStages.Count; s++) {
                                    if (cachedStages[s].stageNumber == eng.stageNumber || cachedStages[s].fuelMassTonnes > 0.05) {
                                        selectedStageIndices.Add(s);
                                    }
                                }
                            }
                            if (selectedStageIndices.Count == 0) selectedStageIndices.Add(0);
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_engine_synced", eng.title), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                } else {
                    step.bound_thruster = GUILayout.TextField(string.IsNullOrEmpty(step.bound_thruster) ? "MainEngine" : step.bound_thruster, GUILayout.Width(120));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Tank Selector Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_burn_tank"), labelValueStyle, GUILayout.Width(90));
                if (allTanks.Count > 0) {
                    int bestTkIdx = 0;
                    double maxTkFuel = -1.0;
                    for (int t = 0; t < allTanks.Count; t++) {
                        if (allTanks[t].fuelMassKg > maxTkFuel) {
                            maxTkFuel = allTanks[t].fuelMassKg;
                            bestTkIdx = t;
                        }
                    }
                    for (int tk = 0; tk < allTanks.Count; tk++) {
                        var tnk = allTanks[tk];
                        string tkId = $"Tank_Stage{tnk.stageNumber}";
                        bool isCurTank = (step.bound_tank == tkId) || (string.IsNullOrEmpty(step.bound_tank) && tk == bestTkIdx);
                        if (GUILayout.Button(I18n.Format("seq_tank_btn2", tnk.stageNumber, tnk.fuelMassKg), isCurTank ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            step.bound_tank = tkId;
                            if (cachedStages != null) {
                                for (int s = 0; s < cachedStages.Count; s++) {
                                    if (cachedStages[s].stageNumber == tnk.stageNumber && !selectedStageIndices.Contains(s)) {
                                        selectedStageIndices.Add(s);
                                    }
                                }
                            }
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_tank_synced", tnk.stageNumber, tnk.fuelMassKg), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                } else {
                    step.bound_tank = GUILayout.TextField(string.IsNullOrEmpty(step.bound_tank) ? "MainTank" : step.bound_tank, GUILayout.Width(120));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Isp & Mass Decrement Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                step.burn_decrement_mass = GUILayout.Toggle(step.burn_decrement_mass, I18n.T("seq_decrement_mass"), GUILayout.Width(140));
                GUILayout.Label(I18n.T("seq_isp_label"), labelValueStyle, GUILayout.Width(80));
                double curIsp = (step.burn_isp <= 0) ? 316.0 : step.burn_isp;
                step.burn_isp = DrawDelayedNumericField($"burn_isp_{i}", curIsp, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(60), "F1");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }

            // =========================================================================
            // 5. FINITE BURN STEP CARD (WITH CLUSTER SELECTION & ACCURATE TANK BINDING)
            // =========================================================================
            else if (step.type == "FiniteBurn") {
                Vessel v = FlightGlobals.ActiveVessel;
                var allEngines = ROEngineInterop.ScanVesselEngines(v);
                var allTanks = ROEngineInterop.ScanVesselTanks(v);

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_fb_header"), labelValueStyle, GUILayout.Width(135));

                GUILayout.Label(I18n.T("seq_objname_label"), gridLabelStyle, GUILayout.Width(45));
                step.burn_name = GUILayout.TextField(string.IsNullOrEmpty(step.burn_name) ? $"Burn_{i + 1}" : step.burn_name, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(85), GUILayout.Height(20));

                GUILayout.Space(6);
                GUILayout.Label(I18n.T("seq_cs_label"), gridLabelStyle, GUILayout.Width(45));
                step.burn_coordinate_system = GUILayout.TextField(string.IsNullOrEmpty(step.burn_coordinate_system) ? "Local" : step.burn_coordinate_system, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(55), GUILayout.Height(20));
                step.burn_axes = GUILayout.TextField(string.IsNullOrEmpty(step.burn_axes) ? "VNB" : step.burn_axes, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(42), GUILayout.Height(20));

                GUILayout.Space(8);
                step.burn_use_vary_var = GUILayout.Toggle(step.burn_use_vary_var, I18n.T("seq_use_vary"), GUILayout.Width(160));
                if (step.burn_use_vary_var) {
                    GUILayout.Label(I18n.T("seq_var_name_label"), gridLabelStyle, GUILayout.Width(45));
                    string curVName = !string.IsNullOrEmpty(step.burn_vary_var_name) ? step.burn_vary_var_name : (!string.IsNullOrEmpty(step.duration_str) ? step.duration_str : "burntime");
                    string newVName = DrawDelayedSmartField($"fburn_var_{i}", curVName, 0.0, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110), "fburn_var", i);
                    if (newVName != curVName) {
                        step.burn_vary_var_name = newVName;
                        step.duration_str = newVName;
                    }

                    if (GMATStateManager.Current != null && GMATStateManager.Current.Variables != null) {
                        var matchedVar = GMATStateManager.Current.Variables.Find(v => v.Name.Equals(step.burn_vary_var_name, StringComparison.OrdinalIgnoreCase));
                        if (matchedVar != null) {
                            GUILayout.Label(I18n.ColorFormat("widget_var_badge", matchedVar.Name, matchedVar.InitialValue), labelValueStyle);
                        }
                    }
                } else {
                    GUILayout.Label(I18n.T("seq_fixed_dur_label"), gridLabelStyle, GUILayout.Width(75));
                    step.duration = DrawDelayedNumericField($"fburn_dur_{i}", step.duration, numericInputStyle, GUILayout.Width(80), "F1");
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_resync_hw"), secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(20))) {
                    allEngines = ROEngineInterop.ScanVesselEngines(v);
                    allTanks = ROEngineInterop.ScanVesselTanks(v);
                    ScreenMessages.PostScreenMessage(I18n.T("seq_hw_synced"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();

                // Thruster & Tank Selection Box
                GUILayout.Space(3);
                GUILayout.BeginVertical(nestedCardBoxStyle);

                // Engine / Cluster Selector Row
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("burn_engine_select"), labelValueStyle, GUILayout.Width(190));
                if (allEngines.Count > 0) {
                    if (step.bound_thrusters == null) step.bound_thrusters = new List<string>();
                    for (int eg = 0; eg < allEngines.Count; eg++) {
                        var eng = allEngines[eg];
                        string engId = GMATParameterRegistry.SanitizeGmatIdentifier(eng.title, "Engine");
                        bool isSelectedInCluster = step.bound_thrusters.Contains(engId) || (step.bound_thruster == engId && step.bound_thrusters.Count == 0) || (string.IsNullOrEmpty(step.bound_thruster) && step.bound_thrusters.Count == 0 && eg == selectedEngineIndex);
                        
                        string btnPrefix = isSelectedInCluster ? "☑ " : "☐ ";
                        if (GUILayout.Button($"{btnPrefix}{eng.title} ({eng.thrustKN:F0}kN / {eng.ispVac:F0}s)", isSelectedInCluster ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            if (Event.current.control || Event.current.shift) {
                                if (step.bound_thrusters.Contains(engId)) {
                                    step.bound_thrusters.Remove(engId);
                                } else {
                                    step.bound_thrusters.Add(engId);
                                }
                            } else {
                                SelectEngineAndSync(eg);
                                step.bound_thrusters.Clear();
                                step.bound_thrusters.Add(engId);
                                step.bound_thruster = engId;
                                var curThruster = GMATStateManager.Current.GetSelectedThruster();
                                if (curThruster != null) {
                                    step.bound_tank = curThruster.BoundTankId;
                                }
                            }
                            step.burn_isp = eng.ispVac;
                            step.burn_decrement_mass = true;
                            ScreenMessages.PostScreenMessage(I18n.Format("seq_burn_bound", eng.title, eng.thrustKN, eng.ispVac), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                    if (step.bound_thrusters.Count > 1) {
                        GUILayout.Label(I18n.ColorFormat("seq_cluster_active", step.bound_thrusters.Count), labelValueStyle);
                    }
                } else {
                    step.bound_thruster = GUILayout.TextField(string.IsNullOrEmpty(step.bound_thruster) ? "MainEngine" : step.bound_thruster, GUILayout.Width(120));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Tank Selector Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_fb_tank"), labelValueStyle, GUILayout.Width(190));
                var stateTanks = GMATStateManager.Current.Tanks;
                if (stateTanks.Count > 0) {
                    for (int tk = 0; tk < stateTanks.Count; tk++) {
                        var tnk = stateTanks[tk];
                        string tkId = tnk.Id;
                        bool isCurTank = (step.bound_tank == tkId) || (string.IsNullOrEmpty(step.bound_tank) && tk == 0);
                        if (GUILayout.Button(I18n.Format("seq_tank_btn3", tkId, tnk.StageNumber, tnk.FuelMassKg), isCurTank ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            step.bound_tank = tkId;
                        }
                    }
                } else {
                    step.bound_tank = GUILayout.TextField(string.IsNullOrEmpty(step.bound_tank) ? "Tank0" : step.bound_tank, GUILayout.Width(120));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.EndVertical(); // Closes Thruster & Tank Selection Box
            }

            // =========================================================================
            // 5B. THRUST VECTOR CONTROL SUBGROUP CARD (VNB VARY & STEERING)
            // =========================================================================
            else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                Vessel v = FlightGlobals.ActiveVessel;
                var allEngines = ROEngineInterop.ScanVesselEngines(v);

                GUILayout.BeginVertical(nestedCardBoxStyle);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#38bdf8><b>{I18n.T("vector_card_title")}</b></color>", labelValueStyle, GUILayout.Width(270));
                
                step.is_vary_vector = GUILayout.Toggle(step.is_vary_vector, $" {I18n.T("vector_vary_toggle")}", GUILayout.Width(200));

                GUILayout.Label($"<color=#94a3b8>{I18n.T("vector_active_engine")}</color>", gridLabelStyle, GUILayout.Width(75));
                if (allEngines.Count > 0) {
                    for (int eg = 0; eg < allEngines.Count; eg++) {
                        var eng = allEngines[eg];
                        string engId = GMATParameterRegistry.SanitizeGmatIdentifier(eng.title, "Engine");
                        bool isCurEng = (step.bound_thruster == engId) || (string.IsNullOrEmpty(step.bound_thruster) && eg == selectedEngineIndex);
                        if (GUILayout.Button(eng.title, isCurEng ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(18))) {
                            SelectEngineAndSync(eg);
                            step.bound_thruster = engId;
                        }
                    }
                } else {
                    step.bound_thruster = GUILayout.TextField(string.IsNullOrEmpty(step.bound_thruster) ? "MainEngine" : step.bound_thruster, GUILayout.Width(100), GUILayout.Height(18));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(3);

                // Quick Presets Row
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#facc15><b>{I18n.T("vector_presets_label")}</b></color>", labelValueStyle, GUILayout.Width(95));
                if (GUILayout.Button(I18n.T("vector_preset_prograde"), secondaryButtonStyle, GUILayout.Height(18))) {
                    step.thrust_dir1 = 1.0; step.thrust_dir2 = 0.0; step.thrust_dir3 = 0.0;
                    step.vary_dir1_enabled = true; step.vary_dir2_enabled = false; step.vary_dir3_enabled = false;
                }
                if (GUILayout.Button(I18n.T("vector_preset_retrograde"), secondaryButtonStyle, GUILayout.Height(18))) {
                    step.thrust_dir1 = -1.0; step.thrust_dir2 = 0.0; step.thrust_dir3 = 0.0;
                    step.vary_dir1_enabled = true; step.vary_dir2_enabled = false; step.vary_dir3_enabled = false;
                }
                if (GUILayout.Button(I18n.T("vector_preset_normal"), secondaryButtonStyle, GUILayout.Height(18))) {
                    step.thrust_dir1 = 0.0; step.thrust_dir2 = 1.0; step.thrust_dir3 = 0.0;
                    step.vary_dir1_enabled = false; step.vary_dir2_enabled = true; step.vary_dir3_enabled = false;
                }
                if (GUILayout.Button(I18n.T("vector_preset_antinormal"), secondaryButtonStyle, GUILayout.Height(18))) {
                    step.thrust_dir1 = 0.0; step.thrust_dir2 = -1.0; step.thrust_dir3 = 0.0;
                    step.vary_dir1_enabled = false; step.vary_dir2_enabled = true; step.vary_dir3_enabled = false;
                }
                if (GUILayout.Button(I18n.T("vector_preset_geo"), secondaryButtonStyle, GUILayout.Height(18))) {
                    step.thrust_dir1 = 1.0; step.thrust_dir2 = 0.0; step.thrust_dir3 = 0.0;
                    step.vary_dir1_enabled = false; step.vary_dir2_enabled = true; step.vary_dir3_enabled = false;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(3);

                // 3-Axis Direction Table with Clear Orbital Mechanics Hints
                string engTag = string.IsNullOrEmpty(step.bound_thruster) ? "Sat.Engine" : $"Sat.{step.bound_thruster}";

                // V-axis (ThrustDirection1)
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("seq_vec_v", engTag), labelValueStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label($"<color=#38bdf8><i>{I18n.T("vector_hint_v")}</i></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                step.vary_dir1_enabled = GUILayout.Toggle(step.vary_dir1_enabled, step.vary_dir1_enabled ? I18n.T("seq_vary_participate") : I18n.T("seq_fixed_dir"), GUILayout.Width(110));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("vector_initial"), gridLabelStyle, GUILayout.Width(50));
                step.thrust_dir1 = DrawDelayedNumericField($"tvc_dir1_{i}", step.thrust_dir1, numericInputStyle, GUILayout.Width(65), "F3");
                if (step.is_vary_vector && step.vary_dir1_enabled) {
                    GUILayout.Label(I18n.T("vector_lower"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir1_lower = DrawDelayedNumericField($"tvc_dir1_low_{i}", step.vary_dir1_lower, numericInputStyle, GUILayout.Width(55), "F2");
                    GUILayout.Label(I18n.T("vector_upper"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir1_upper = DrawDelayedNumericField($"tvc_dir1_up_{i}", step.vary_dir1_upper, numericInputStyle, GUILayout.Width(55), "F2");
                    GUILayout.Label(I18n.T("vector_step"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir1_max_step = DrawDelayedNumericField($"tvc_dir1_step_{i}", step.vary_dir1_max_step, numericInputStyle, GUILayout.Width(55), "F2");
                } else {
                    GUILayout.Label(I18n.T("seq_fixed_note"), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // N-axis (ThrustDirection2)
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("seq_vec_n", engTag), labelValueStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label($"<color=#facc15><i>{I18n.T("vector_hint_n")}</i></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                step.vary_dir2_enabled = GUILayout.Toggle(step.vary_dir2_enabled, step.vary_dir2_enabled ? I18n.T("seq_vary_participate") : I18n.T("seq_fixed_dir"), GUILayout.Width(110));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("vector_initial"), gridLabelStyle, GUILayout.Width(50));
                step.thrust_dir2 = DrawDelayedNumericField($"tvc_dir2_{i}", step.thrust_dir2, numericInputStyle, GUILayout.Width(65), "F3");
                if (step.is_vary_vector && step.vary_dir2_enabled) {
                    GUILayout.Label(I18n.T("vector_lower"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir2_lower = DrawDelayedNumericField($"tvc_dir2_low_{i}", step.vary_dir2_lower, numericInputStyle, GUILayout.Width(55), "F2");
                    GUILayout.Label(I18n.T("vector_upper"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir2_upper = DrawDelayedNumericField($"tvc_dir2_up_{i}", step.vary_dir2_upper, numericInputStyle, GUILayout.Width(55), "F2");
                    GUILayout.Label(I18n.T("vector_step"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir2_max_step = DrawDelayedNumericField($"tvc_dir2_step_{i}", step.vary_dir2_max_step, numericInputStyle, GUILayout.Width(55), "F2");
                } else {
                    GUILayout.Label(I18n.T("seq_fixed_note"), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // B-axis (ThrustDirection3)
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("seq_vec_b", engTag), labelValueStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label($"<color=#f472b6><i>{I18n.T("vector_hint_b")}</i></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                step.vary_dir3_enabled = GUILayout.Toggle(step.vary_dir3_enabled, step.vary_dir3_enabled ? I18n.T("seq_vary_participate") : I18n.T("seq_fixed_dir"), GUILayout.Width(110));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("vector_initial"), gridLabelStyle, GUILayout.Width(50));
                step.thrust_dir3 = DrawDelayedNumericField($"tvc_dir3_{i}", step.thrust_dir3, numericInputStyle, GUILayout.Width(65), "F3");
                if (step.is_vary_vector && step.vary_dir3_enabled) {
                    GUILayout.Label(I18n.T("vector_lower"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir3_lower = DrawDelayedNumericField($"tvc_dir3_low_{i}", step.vary_dir3_lower, numericInputStyle, GUILayout.Width(55), "F2");
                    GUILayout.Label(I18n.T("vector_upper"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir3_upper = DrawDelayedNumericField($"tvc_dir3_up_{i}", step.vary_dir3_upper, numericInputStyle, GUILayout.Width(55), "F2");
                    GUILayout.Label(I18n.T("vector_step"), gridLabelStyle, GUILayout.Width(50));
                    step.vary_dir3_max_step = DrawDelayedNumericField($"tvc_dir3_step_{i}", step.vary_dir3_max_step, numericInputStyle, GUILayout.Width(55), "F2");
                } else {
                    GUILayout.Label(I18n.T("seq_fixed_note"), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // GMAT Solved Vector Banner
                if (step.has_solved_value) {
                    GUILayout.Space(3);
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.ColorFormat("seq_solved_vec", step.solved_dir1, step.solved_dir2, step.solved_dir3), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(I18n.T("btn_apply_solved"), actionButtonStyle, GUILayout.Width(130), GUILayout.Height(20))) {
                        step.thrust_dir1 = step.solved_dir1;
                        step.thrust_dir2 = step.solved_dir2;
                        step.thrust_dir3 = step.solved_dir3;
                        inputStringCache[$"tvc_dir1_{i}"] = step.thrust_dir1.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        inputStringCache[$"tvc_dir2_{i}"] = step.thrust_dir2.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        inputStringCache[$"tvc_dir3_{i}"] = step.thrust_dir3.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        ScreenMessages.PostScreenMessage(I18n.T("seq_vec_applied"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                // Principia Maneuver Sync
                if (cachedFlightPlanNodes != null && cachedFlightPlanNodes.Count > 0) {
                    int fbIdx = 0;
                    for (int k = 0; k < i && k < missionSequence.Count; k++) {
                        if (missionSequence[k].type == "FiniteBurn" || missionSequence[k].type == "ThrustVectorControl") fbIdx++;
                    }
                    if (fbIdx < cachedFlightPlanNodes.Count) {
                        var pNode = cachedFlightPlanNodes[fbIdx];
                        if (pNode != null && pNode.hasDeltaV) {
                            GUILayout.Space(2);
                            GUILayout.BeginHorizontal();
                            if (GUILayout.Button(I18n.Format("btn_sync_from_principia", fbIdx + 1, pNode.deltaV.x, pNode.deltaV.z, pNode.deltaV.y), secondaryButtonStyle, GUILayout.Height(20))) {
                                double pMag = Math.Sqrt(pNode.deltaV.x * pNode.deltaV.x + pNode.deltaV.y * pNode.deltaV.y + pNode.deltaV.z * pNode.deltaV.z);
                                if (pMag > 1e-5) {
                                    step.thrust_dir1 = pNode.deltaV.x / pMag;
                                    step.thrust_dir2 = pNode.deltaV.z / pMag;
                                    step.thrust_dir3 = pNode.deltaV.y / pMag;
                                }
                                ScreenMessages.PostScreenMessage(I18n.Format("seq_vec_synced", fbIdx + 1), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                            }
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }
                    }
                }

                GUILayout.EndVertical();
            }

            // =========================================================================
            // 5B. STAGE SEPARATION (DISCARD DEAD MASS & STAGE TANKS)
            // =========================================================================
            else if (step.type == "StageSeparation") {
                Vessel v = FlightGlobals.ActiveVessel;
                var allStages = GetSelectedStagesList();

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_stage_header"), labelValueStyle, GUILayout.Width(170));

                if (allStages.Count > 0) {
                    for (int s = 0; s < allStages.Count; s++) {
                        var st = allStages[s];
                        bool isCurStage = (step.separated_stage_index == st.stageNumber);
                        GUI.backgroundColor = isCurStage ? new Color(0.9f, 0.6f, 0.2f) : Color.white;
                        if (GUILayout.Button(I18n.Format("seq_stage_btn", st.stageNumber, st.dryMassTonnes * 1000.0), secondaryButtonStyle, GUILayout.Height(18))) {
                            step.separated_stage_index = st.stageNumber;
                            step.separated_mass = st.dryMassTonnes * 1000.0;
                            double curVesselDry = (v != null) ? v.totalMass * 1000.0 * 0.5 : 2000.0;
                            step.remaining_dry_mass = Math.Max(500.0, curVesselDry - step.separated_mass);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_stage_mass_label"), labelValueStyle, GUILayout.Width(120));
                step.separated_mass = DrawDelayedNumericField($"stg_drop_{i}", step.separated_mass, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(75), "F0");

                GUILayout.Label(I18n.T("seq_remaining_mass_label"), labelValueStyle, GUILayout.Width(160));
                step.remaining_dry_mass = DrawDelayedNumericField($"stg_rem_{i}", step.remaining_dry_mass, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(75), "F0");

                GUILayout.Label(I18n.T("seq_stage_note"), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            // =========================================================================
            // 6. EQUATION STEP CARD
            // =========================================================================
            else if (step.type == "Equation") {
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_eq_var_label"), labelValueStyle, GUILayout.Width(95));
                step.variable = DrawDelayedSmartField($"eq_var_{i}", step.variable, 0.0, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(130), "vary", i);
                GUILayout.Label("=", labelValueStyle, GUILayout.Width(15));
                step.expression = DrawDelayedSmartField($"eq_exp_{i}", step.expression, 0.0, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(110), "prop_val", i);

                if (GUILayout.Button(I18n.T("btn_param_library"), secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(20))) {
                    OpenParamPicker(i, "variable");
                }
                
                DrawVariableBadge(step.variable);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // =========================================================================
            // 7. REPORT STEP CARD (EXPANDABLE COMPREHENSIVE FLIGHT REPORT & METRICS)
            // =========================================================================
            else if (step.type == "Report") {
                StepReportData rep = null;
                if (latestResult != null && latestResult.step_reports != null) {
                    rep = latestResult.step_reports.Find(r => r.step_index == i);
                }

                // Row 1: Header + Target File Identifier + Expand Toggle + Quick Inline Status
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("seq_report_header"), labelValueStyle, GUILayout.Width(185));

                GUILayout.Label(I18n.T("seq_objname_label"), gridLabelStyle, GUILayout.Width(45));
                step.subscriber_name = DrawDelayedTextField($"rep_sub_{i}", string.IsNullOrEmpty(step.subscriber_name) ? $"StepReport_{i + 1}" : step.subscriber_name, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110));

                GUILayout.Space(6);
                step.is_expanded = GUILayout.Toggle(step.is_expanded, step.is_expanded ? I18n.T("seq_report_collapse") : I18n.T("seq_report_expand"), secondaryButtonStyle, GUILayout.Height(20));

                GUILayout.Space(10);
                if (rep != null) {
                    GUILayout.Label(I18n.ColorFormat("seq_report_ready", rep.elapsed_secs, rep.sma_km, rep.altitude_km, rep.total_mass_kg), labelValueStyle);
                } else {
                    GUILayout.Label(I18n.T("seq_report_pending"), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Detailed Expandable Multi-Column Dashboard
                if (step.is_expanded) {
                    GUILayout.Space(3);
                    GUILayout.BeginVertical(nestedCardBoxStyle);

                    if (rep != null) {
                        // Section 1: Keplerian Orbital Elements
                        GUILayout.BeginVertical(cardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.T("seq_report_kepler_header"), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.Space(2);
                        // Row A: SMA, ECC, INC, RAAN
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.ColorFormat("seq_sma", rep.sma_km), labelValueStyle, GUILayout.Width(170));
                        GUILayout.Label(I18n.ColorFormat("seq_ecc", rep.ecc), labelValueStyle, GUILayout.Width(150));
                        GUILayout.Label(I18n.ColorFormat("seq_inc", rep.inc_deg), labelValueStyle, GUILayout.Width(140));
                        GUILayout.Label(I18n.ColorFormat("seq_raan", rep.raan_deg), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        // Row B: AOP, TA, Periapsis Alt, Apoapsis Alt
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.ColorFormat("seq_aop", rep.aop_deg), labelValueStyle, GUILayout.Width(170));
                        GUILayout.Label(I18n.ColorFormat("seq_ta", rep.ta_deg), labelValueStyle, GUILayout.Width(150));
                        string periStr = rep.periapsis_km > 0 ? $"{rep.periapsis_km:F1} km" : $"{rep.altitude_km:F1} km";
                        string apoStr = rep.apoapsis_km > 0 ? $"{rep.apoapsis_km:F1} km" : $"{rep.altitude_km:F1} km";
                        GUILayout.Label(I18n.ColorFormat("seq_peri", periStr), labelValueStyle, GUILayout.Width(140));
                        GUILayout.Label(I18n.ColorFormat("seq_apo", apoStr), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();

                        GUILayout.Space(2);

                        // Section 2: Cartesian 3D Vectors & Mass
                        GUILayout.BeginHorizontal();

                        // Col 1: Position & Velocity
                        GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(380));
                        GUILayout.Label(I18n.T("seq_cart_header"), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_pos_vec", rep.x, rep.y, rep.z), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_rmag", rep.rmag_km), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_vel_vec", rep.vx, rep.vy, rep.vz), labelValueStyle);
                        double vmag = rep.vmag_km_s > 0 ? rep.vmag_km_s : Math.Sqrt(rep.vx * rep.vx + rep.vy * rep.vy + rep.vz * rep.vz);
                        GUILayout.Label(I18n.ColorFormat("seq_vmag", vmag, vmag * 1000.0), labelValueStyle);
                        GUILayout.EndVertical();

                        GUILayout.Space(3);

                        // Col 2: Mass, Fuel & Epoch Timing
                        GUILayout.BeginVertical(cardBoxStyle);
                        GUILayout.Label(I18n.T("seq_mass_time_header"), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("seq_total_mass", rep.total_mass_kg), labelValueStyle);
                        if (rep.fuel_mass_kg > 0) {
                            GUILayout.Label(I18n.ColorFormat("seq_fuel_remaining", rep.fuel_mass_kg), labelValueStyle);
                        }
                        GUILayout.Label(I18n.ColorFormat("seq_mission_time", rep.elapsed_secs, rep.elapsed_secs / 60.0, rep.elapsed_secs / 86400.0), labelValueStyle);
                        if (!string.IsNullOrEmpty(rep.epoch_utc)) {
                            GUILayout.Label(I18n.ColorFormat("seq_epoch", rep.epoch_utc), labelValueStyle);
                        }
                        GUILayout.EndVertical();

                        GUILayout.EndHorizontal();
                    } else {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.T("seq_report_no_solve"), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    // Section 3: Output Variables Customization & Quick Presets
                    GUILayout.Space(3);
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("seq_export_fields_header"), labelValueStyle, GUILayout.Width(220));
                    
                    if (GUILayout.Button(I18n.T("btn_fields_all"), secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.Earth.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.Earth.RMAG Sat.TotalMass Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ";
                    }
                    if (GUILayout.Button(I18n.T("btn_fields_kepler"), secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.Earth.Altitude";
                    }
                    if (GUILayout.Button(I18n.T("btn_fields_posvel"), secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ";
                    }
                    if (GUILayout.Button(I18n.T("btn_fields_mass"), secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.TotalMass Sat.FuelMassBurned Sat.DryMass";
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    string curFields = !string.IsNullOrEmpty(step.write_fields) ? step.write_fields : "Sat.ElapsedSecs Sat.Earth.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.Earth.RMAG Sat.TotalMass Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ";
                    string newFields = GUILayout.TextField(curFields, GUILayout.MinWidth(350), GUILayout.ExpandWidth(true), GUILayout.Height(20));
                    if (newFields != curFields) step.write_fields = newFields;
                    GUILayout.EndHorizontal();

                    // Section 4: Raw Report Stream Preview
                    if (rep != null && !string.IsNullOrEmpty(rep.raw_report_text)) {
                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.ColorFormat("seq_raw_preview", rep.raw_report_text.Trim().Split(new char[] { '\n' }).Length), labelValueStyle);
                        if (GUILayout.Button(I18n.T("btn_copy_raw"), secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(18))) {
                            GUIUtility.systemCopyBuffer = rep.raw_report_text;
                            ScreenMessages.PostScreenMessage(I18n.T("seq_raw_copied"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.EndVertical();

                    GUILayout.EndVertical(); // Closes nestedCardBoxStyle
                }
            }
        }

        private string GetStepTypeBadgeText(string type) {
            switch (type) {
                case "Propagate": return I18n.T("type_propagate");
                case "Vary": return I18n.T("type_vary");
                case "Achieve": return I18n.T("type_achieve");
                case "FiniteBurn": return I18n.T("type_finiteburn");
                case "BeginFiniteBurn": return I18n.T("type_beginfiniteburn");
                case "EndFiniteBurn": return I18n.T("type_endfiniteburn2");
                case "ThrustVectorControl":
                case "VaryThrustDirection": return I18n.T("type_thrustvector");
                case "ImpulsiveBurn":
                case "Maneuver": return I18n.T("type_impulsiveburn");
                case "StageSeparation": return I18n.T("type_stagesep");
                case "Report": return I18n.T("type_report2");
                case "Equation": return I18n.T("type_equation");
                case "If": return I18n.T("type_if2");
                case "Else": return I18n.T("type_else2");
                case "EndIf": return I18n.T("type_endif2");
                case "While": return I18n.T("type_while2");
                case "EndWhile": return I18n.T("type_endwhile2");
                case "For": return I18n.T("type_for2");
                case "EndFor": return I18n.T("type_endfor2");
                case "Optimize": return I18n.T("type_optimize");
                case "Minimize": return I18n.T("type_minimize");
                case "NonlinearConstraint": return I18n.T("type_nlc");
                case "EndOptimize": return I18n.T("type_endoptimize");
                case "ScriptEvent": return I18n.T("type_scriptevent");
                case "BeginFileThrust": return I18n.T("type_beginfilethrust");
                case "EndFileThrust": return I18n.T("type_endfilethrust");
                case "Stop": return I18n.T("type_stop");
                case "Breakpoint": return I18n.T("type_breakpoint");
                default: return type ?? "Command";
            }
        }

        private GUIStyle GetStepPillStyle(string type) {
            switch (type) {
                case "Propagate": return pillPropStyle ?? GUI.skin.box;
                case "Vary": return pillVaryStyle ?? GUI.skin.box;
                case "Achieve": return pillAchieveStyle ?? GUI.skin.box;
                case "FiniteBurn":
                case "BeginFiniteBurn":
                case "EndFiniteBurn":
                case "ThrustVectorControl":
                case "VaryThrustDirection": return pillBurnStyle ?? GUI.skin.box;
                case "ImpulsiveBurn":
                case "Maneuver": return pillImpulseStyle ?? GUI.skin.box;
                case "StageSeparation": return pillStageStyle ?? GUI.skin.box;
                default: return pillGenericStyle ?? GUI.skin.box;
            }
        }

        private void LoadScriptTemplate(string templatePathOrFileName, Vessel vessel) {
            string foundPath = null;
            if (File.Exists(templatePathOrFileName)) {
                foundPath = templatePathOrFileName;
            } else {
                string fileName = Path.GetFileName(templatePathOrFileName);
                string[] searchPaths = new string[] {
                    Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "templates", fileName),
                    Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "GameData", "KSPGMAT", "templates", fileName),
                    Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "scripts", "templates", fileName),
                    Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "scripts", fileName),
                    Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "scripts", fileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KSPGMAT", "templates", fileName)
                };

                for (int i = 0; i < searchPaths.Length; i++) {
                    if (File.Exists(searchPaths[i])) { foundPath = searchPaths[i]; break; }
                }
            }

            if (!string.IsNullOrEmpty(foundPath)) {
                currentLoadedTemplateName = Path.GetFileName(foundPath);
                GMATClient.ImportScript(foundPath, vessel, (ok, res, msg) => {
                    if (ok && res != null) {
                        ApplyImportResultToState(res);

                        // Clear old solution state
                        latestResult = null;
                        solveStageText = I18n.T("solve_stage_ready");
                        solveProgress = 0f;

                        // Instantly synchronize IDE buffer with the loaded script template
                        try {
                            string rawScript = File.ReadAllText(foundPath, System.Text.Encoding.UTF8);
                            ideCustomBuffer = rawScript;
                        } catch {
                            ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                        }

                        ScreenMessages.PostScreenMessage(I18n.Format("seq_template_loaded", Path.GetFileName(foundPath), res.sequence?.Count ?? 0), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    } else {
                        ScreenMessages.PostScreenMessage(I18n.Format("seq_template_parse_fail", msg), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                });
            } else {
                ScreenMessages.PostScreenMessage(I18n.Format("seq_template_not_found", templatePathOrFileName), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ExportScriptTemplate(string templateName, Vessel vessel) {
            try {
                if (string.IsNullOrEmpty(templateName)) templateName = "custom_mission_preset.script";
                string cleanName = templateName.EndsWith(".script") ? templateName : (templateName + ".script");
                string templateDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "templates");
                if (!Directory.Exists(templateDir)) Directory.CreateDirectory(templateDir);
                string outPath = Path.Combine(templateDir, cleanName);
                string scriptContent = GMATClient.SanitizeGmatScript(ideCustomBuffer ?? GenerateLiveGmatScript(0));
                File.WriteAllText(outPath, scriptContent, new System.Text.UTF8Encoding(false));
                ScreenMessages.PostScreenMessage(I18n.Format("seq_template_exported", cleanName), 3.5f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage(I18n.Format("seq_template_export_fail", ex.Message), 3.5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public void ClearSolutionResults() {
            latestResult = null;
            inputStringCache.Clear();
            if (missionSequence != null) {
                foreach (var step in missionSequence) {
                    step.has_solved_value = false;
                    step.solved_value = 0.0;
                    step.solved_value_str = "";
                    step.has_achieve_result = false;
                    step.achieved_value = 0.0;
                    step.achieved_variance = 0.0;
                    step.achieve_passed = false;
                    step.solver_iterations = 0;
                    step.solver_converged = false;
                }
            }
            ScreenMessages.PostScreenMessage(I18n.T("seq_cleared"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void SyncSolvedValuesToSequence(MissionResult result) {
            if (result == null || result.updated_sequence == null || result.updated_sequence.Count == 0 || missionSequence == null) return;
            for (int k = 0; k < Math.Min(missionSequence.Count, result.updated_sequence.Count); k++) {
                var newStep = result.updated_sequence[k];
                var curStep = missionSequence[k];
                curStep.has_solved_value = newStep.has_solved_value;
                curStep.solved_value = newStep.solved_value;
                curStep.solved_value_str = newStep.solved_value_str;
                curStep.has_achieve_result = newStep.has_achieve_result;
                curStep.achieved_value = newStep.achieved_value;
                curStep.achieved_variance = newStep.achieved_variance;
                curStep.achieve_passed = newStep.achieve_passed;
                curStep.solver_iterations = newStep.solver_iterations;
                curStep.solver_converged = newStep.solver_converged;
            }
        }

        private void DrawStepCollapsedResultBadge(MissionStep step) {
            if (step == null) return;
            if (step.type == "Vary" && (step.has_solved_value || step.solved_value != 0)) {
                GUILayout.Space(8);
                string unitStr = (step.vary_variable != null && (step.vary_variable.Contains("Direction") || step.vary_variable.Contains("Thrust"))) ? "" : ((step.vary_variable != null && step.vary_variable.Contains("Element")) ? "km/s" : "s");
                string sVal = !string.IsNullOrEmpty(step.solved_value_str) ? step.solved_value_str : step.solved_value.ToString("F4");
                GUILayout.Label(I18n.ColorFormat("seq_collapsed_vary", sVal, unitStr), labelValueStyle);
            } else if (step.type == "Achieve" && step.has_achieve_result) {
                GUILayout.Space(8);
                string col = step.achieve_passed ? "#34d399" : "#f87171";
                string icon = step.achieve_passed ? "✓" : "✗";
                GUILayout.Label(I18n.ColorFormat("seq_collapsed_achieve", col, icon, step.achieved_value, step.achieved_variance), labelValueStyle);
            } else if ((step.type == "FiniteBurn" || step.type == "ImpulsiveBurn" || step.type == "Maneuver") && (step.dv_v != 0 || step.dv_n != 0 || step.dv_b != 0 || step.has_solved_value)) {
                GUILayout.Space(8);
                double tot = Math.Sqrt(step.dv_v * step.dv_v + step.dv_n * step.dv_n + step.dv_b * step.dv_b);
                if (tot > 0) {
                    GUILayout.Label(I18n.ColorFormat("seq_collapsed_dv", tot, step.dv_v, step.dv_n, step.dv_b), labelValueStyle);
                }
            } else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                GUILayout.Space(8);
                if (step.has_solved_value) {
                    GUILayout.Label(I18n.ColorFormat("seq_collapsed_tvc_solved", step.solved_dir1, step.solved_dir2, step.solved_dir3), labelValueStyle);
                } else {
                    GUILayout.Label(I18n.ColorFormat("seq_collapsed_tvc_initial", step.thrust_dir1, step.thrust_dir2, step.thrust_dir3), labelValueStyle);
                }
            } else if (step.type == "StageSeparation") {
                GUILayout.Space(8);
                GUILayout.Label(I18n.ColorFormat("seq_collapsed_stage", step.separated_mass), labelValueStyle);
            }
        }
    }
}
