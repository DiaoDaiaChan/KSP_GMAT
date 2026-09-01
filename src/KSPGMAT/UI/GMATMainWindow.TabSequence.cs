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
            string dragBtnText = isBeingDragged ? "✊ 移动中" : (draggedStepIndex >= 0 ? "⬇ 放置于此" : "≡ 排序");
            GUIStyle dragBtnStyle = isBeingDragged ? successButtonStyle : (draggedStepIndex >= 0 ? tabButtonActiveStyle : secondaryButtonStyle);
            float dragBtnW = (draggedStepIndex >= 0 && !isBeingDragged) ? 82f : 50f;

            if (GUILayout.Button(dragBtnText, dragBtnStyle, GUILayout.Width(dragBtnW), GUILayout.Height(20))) {
                if (draggedStepIndex == i) {
                    draggedStepIndex = -1; // Cancel
                } else if (draggedStepIndex >= 0) {
                    var moveItem = missionSequence[draggedStepIndex];
                    missionSequence.RemoveAt(draggedStepIndex);
                    missionSequence.Insert(i, moveItem);
                    ScreenMessages.PostScreenMessage($"✅ 步骤已成功移动至 #{i + 1}！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    draggedStepIndex = -1;
                } else {
                    draggedStepIndex = i;
                    ScreenMessages.PostScreenMessage($"✊ 已选定步骤 #{i + 1}，请点击目标位置卡片的 [ ⬇ 放置于此 ] 完成移动", 3.0f, ScreenMessageStyle.UPPER_CENTER);
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
            if (GUILayout.Button($"<b>{foldoutArrow} 📚 任务预设模板库</b> <color=#94a3b8>({allTemplates.Count} 个可用脚本)</color>", headerStyle, GUILayout.ExpandWidth(false))) {
                showTemplatesFoldout = !showTemplatesFoldout;
            }

            if (!showTemplatesFoldout && !string.IsNullOrEmpty(currentLoadedTemplateName)) {
                GUILayout.Space(8);
                GUILayout.Label($"<color=#38bdf8>当前已载入:</color> <color=#94a3b8>{currentLoadedTemplateName}</color>", labelValueStyle);
            }

            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("🔄 刷新", secondaryButtonStyle, GUILayout.Height(20))) {
                GMATTemplateManager.RefreshTemplates();
                allTemplates = GMATTemplateManager.GetTemplates(true);
            }
            GUILayout.Space(3);
            if (GUILayout.Button("📁 打开目录...", secondaryButtonStyle, GUILayout.Height(20))) {
                GMATTemplateManager.OpenTemplatesFolder();
            }
            GUILayout.Space(3);
            if (GUILayout.Button("📝 IDE...", secondaryButtonStyle, GUILayout.Height(20))) {
                activeTab = 1; ideScriptViewMode = 0;
            }
            GUILayout.Space(3);
            if (GUILayout.Button(showCustomScriptInput ? "▲ 脚本入/出" : "📜 脚本入/出...", secondaryButtonStyle, GUILayout.Height(20))) {
                showCustomScriptInput = !showCustomScriptInput;
            }
            GUILayout.EndHorizontal();

            // EXPANDED TEMPLATE BROWSER
            if (showTemplatesFoldout) {
                // Filter & Search bar (if templates > 3)
                if (allTemplates.Count > 3) {
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<color=#94a3b8>分类:</color>", labelValueStyle, GUILayout.Width(35));
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
                    GUILayout.Label("<color=#94a3b8>🔍</color>", labelValueStyle, GUILayout.Width(15));
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
                                string btnText = $"{tpl.Icon} {tpl.DisplayTitle}";

                                if (GUILayout.Button(new GUIContent(btnText, $"{tpl.FileName}\n{tpl.Description}\n路径: {tpl.FullPath}"), btnStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
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
                    GUILayout.Label("<color=#64748b>没有找到匹配的模板脚本。可点击上方「📁 打开目录」放入自定义 .script 脚本文件后点击刷新。</color>", labelValueStyle);
                    GUILayout.EndHorizontal();
                }
            }

            if (showCustomScriptInput) {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("📂 导入第三方脚本:", labelValueStyle, GUILayout.Width(110));
                customScriptPath = GUILayout.TextField(customScriptPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
                if (GUILayout.Button("解析导入", secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
                    if (System.IO.File.Exists(customScriptPath)) {
                        GMATClient.ImportScript(customScriptPath, vessel, (ok, result, msg) => {
                            if (ok && result != null) {
                                ApplyImportResultToState(result);

                                // Clear old solver diagnostics from previous run
                                solveStageText = "就绪";
                                latestResult = null;
                                solveProgress = 0f;

                                try {
                                    ideCustomBuffer = File.ReadAllText(customScriptPath, System.Text.Encoding.UTF8);
                                } catch {}

                                ScreenMessages.PostScreenMessage($"✅ 成功导入第三方脚本 ({result.sequence?.Count ?? 0} 个任务步骤)！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                            } else {
                                ScreenMessages.PostScreenMessage("❌ 解析第三方脚本失败: " + msg, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                            }
                        });
                    } else {
                        ScreenMessages.PostScreenMessage("❌ 文件不存在: " + customScriptPath, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("💾 导出为预设模板:", labelValueStyle, GUILayout.Width(110));
                exportTemplateName = GUILayout.TextField(exportTemplateName, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
                if (GUILayout.Button("导出模板", secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
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
                    GUILayout.Label("<color=#34d399><b>✅ 求解器状态：全部打靶任务 100% 收敛</b></color>", labelValueStyle);
                } else {
                    string unconvStr = (latestResult.unconverged_solvers != null && latestResult.unconverged_solvers.Count > 0)
                        ? string.Join(", ", latestResult.unconverged_solvers.ToArray())
                        : "部分求解器未收敛";
                    GUILayout.Label($"<color=#f87171><b>⚠️ 求解器状态警告：打靶未收敛 ({unconvStr})</b></color>", labelValueStyle);
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
                GUILayout.Label("<color=#64748b>任务序列为空。点击上方模板或下方按钮添加步骤。</color>", labelValueStyle);
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
                    if (GUILayout.Button($"⬇ 插入步骤 #{draggedStepIndex + 1} 到步骤 #{i + 1} 之前 ⬇", tabButtonActiveStyle, GUILayout.Height(22))) {
                        var moveItem = missionSequence[draggedStepIndex];
                        missionSequence.RemoveAt(draggedStepIndex);
                        int targetIdx = (draggedStepIndex < i) ? i - 1 : i;
                        missionSequence.Insert(targetIdx, moveItem);
                        ScreenMessages.PostScreenMessage($"✅ 步骤 #{draggedStepIndex + 1} 已成功移动至 #{i + 1} 之前！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
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

                    string foldIcon = step.is_collapsed ? "▸ 展开求解组" : "▾ 收起求解组";
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

                    if (GUILayout.Button(isBeingDragged ? "✊ 拖动中 (点此取消)" : "☰ 拖动", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 120 : 54), GUILayout.Height(20))) {
                        draggedStepIndex = isBeingDragged ? -1 : i;
                    }

                    GUILayout.Label($"<b>#{i + 1} 🎯 Target 求解组:</b>", headerStyle, GUILayout.Width(130));
                    step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? "打靶求解组" : step.label, stepLabelInputStyle, GUILayout.MinWidth(140), GUILayout.MaxWidth(280), GUILayout.Height(20));
                    GUILayout.FlexibleSpace();

                    string solverBadge = $"<color=#38bdf8><b>[DC: {step.dc_name} | {step.dc_algorithm} | 迭代:{step.dc_max_iter}]</b></color>";
                    GUILayout.Label(solverBadge, labelValueStyle);

                    bool showCfg = expandedSolverSettings.Contains(i);
                    if (GUILayout.Button(showCfg ? "⚙️ 求解器配置 ▴" : "⚙️ 求解器配置 ▾", secondaryButtonStyle, GUILayout.Height(20))) {
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
                        GUILayout.Label($"<color=#f43f5e><b>⚠️ 自由度不足:</b> 包含 {achieveCount} 个目标，但仅有 {varyCount} 个寻优变量！牛顿迭代需要 变量数 ≥ 目标数。</color>", labelValueStyle);
                        if (GUILayout.Button("➕ 自动补齐推力方向 Vary 变量", tabButtonActiveStyle, GUILayout.Height(20))) {
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
                                label = "Vary 法向推力矢量偏转"
                            });
                            ScreenMessages.PostScreenMessage($"✅ 已自动为推力器 '{activeThruster}' 补齐推力方向 Vary 寻优变量！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        GUILayout.EndHorizontal();
                    }

                    if (isDuplicateDcName) {
                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal(nestedCardBoxStyle);
                        GUILayout.Label($"<color=#facc15><b>⚠️ 求解器重名:</b> 当前存在多个共享 '{step.dc_name}' 的求解组，可能导致导数矩阵冲突。</color>", labelValueStyle);
                        if (GUILayout.Button("🔄 自动重命名为独立求解器", secondaryButtonStyle, GUILayout.Height(20))) {
                            string newDc = $"DC_{i + 1}";
                            step.dc_name = newDc;
                            for (int ch = i + 1; ch < missionSequence.Count; ch++) {
                                if (missionSequence[ch].type == "EndTarget") { missionSequence[ch].dc_name = newDc; break; }
                                if (missionSequence[ch].type == "Target") break;
                                if (missionSequence[ch].type == "Vary" || missionSequence[ch].type == "Achieve") {
                                    missionSequence[ch].dc_name = newDc;
                                }
                            }
                            ScreenMessages.PostScreenMessage($"✅ 已重命名为独立求解器: {newDc}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
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
                        GUILayout.Label($"<color=#94a3b8><i>↳ [已折叠求解组: 包含 {childTotal} 个子步骤 ({childVary} Vary, {childBurn} Burn, {childAchieve} Achieve) - 点击上方「▶ 展开求解组」即可展开编辑]</i></color>", labelValueStyle);
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
                            GUILayout.Label("<color=#38bdf8><b>绑定求解器 (Solver):</b></color>", labelValueStyle, GUILayout.Width(130));
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

                            if (GUILayout.Button("➕ 新增求解器", secondaryButtonStyle, GUILayout.Height(20))) {
                                string newName = $"DC_{GMATStateManager.Current.Solvers.Count + 1}";
                                GMATStateManager.Current.Solvers.Add(new GMATSolverModel {
                                    Name = newName,
                                    Algorithm = "NewtonRaphson",
                                    DerivativeMethod = "ForwardDifference",
                                    MaxIterations = 50
                                });
                                SetTargetGroupSolver(i, newName);
                                ScreenMessages.PostScreenMessage($"✅ 已创建并绑定新求解器: {newName}", 2.5f, ScreenMessageStyle.UPPER_CENTER);
                            }
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            // Row 2: Algorithm, Derivative Method, Maximum Iterations (Bidirectional sync with GMATStateManager)
                            GUILayout.Space(3);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("<color=#94a3b8>寻优算法:</color>", gridLabelStyle, GUILayout.Width(65));
                            string[] dcAlgs = new string[] { "NewtonRaphson", "Broyden" };
                            int aIdx = Array.IndexOf(dcAlgs, step.dc_algorithm);
                            if (aIdx < 0) aIdx = 0;
                            if (GUILayout.Button(dcAlgs[aIdx], secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(20))) {
                                step.dc_algorithm = dcAlgs[(aIdx + 1) % dcAlgs.Length];
                                var sol = GMATStateManager.Current.Solvers.Find(s => s.Name == step.dc_name);
                                if (sol != null) sol.Algorithm = step.dc_algorithm;
                            }

                            GUILayout.Space(8);
                            GUILayout.Label("<color=#94a3b8>导数算法:</color>", gridLabelStyle, GUILayout.Width(65));
                            string[] dcDms = new string[] { "ForwardDifference", "CentralDifference" };
                            int dmIdx = Array.IndexOf(dcDms, step.dc_derivative_method);
                            if (dmIdx < 0) dmIdx = 0;
                            if (GUILayout.Button(dcDms[dmIdx], secondaryButtonStyle, GUILayout.Width(125), GUILayout.Height(20))) {
                                step.dc_derivative_method = dcDms[(dmIdx + 1) % dcDms.Length];
                                var sol = GMATStateManager.Current.Solvers.Find(s => s.Name == step.dc_name);
                                if (sol != null) sol.DerivativeMethod = step.dc_derivative_method;
                            }

                            GUILayout.Space(8);
                            GUILayout.Label("<color=#94a3b8>最大迭代:</color>", gridLabelStyle, GUILayout.Width(65));
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

                        GUILayout.Label("<color=#64748b>向此打靶组内快速添加 (插入至组末尾):</color>", labelValueStyle);
                        if (GUILayout.Button("➕ Vary 寻优变量", secondaryButtonStyle, GUILayout.Height(18))) {
                            stepToInsertVary = groupEndIdx;
                        }
                        if (GUILayout.Button("➕ Achieve 约束目标", secondaryButtonStyle, GUILayout.Height(18))) {
                            stepToInsertAchieve = groupEndIdx;
                        }
                        if (GUILayout.Button("➕ Burn 点火", secondaryButtonStyle, GUILayout.Height(18))) {
                            stepToInsertBurn = groupEndIdx;
                        }
                        if (GUILayout.Button("➕ Prop 推进", secondaryButtonStyle, GUILayout.Height(18))) {
                            stepToInsertProp = groupEndIdx;
                        }
                        if (GUILayout.Button("🧭 更改推力矢量 (Vary 矢量)", actionButtonStyle, GUILayout.Height(18))) {
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

                        if (GUILayout.Button(isBeingDragged ? "✊ 拖动中" : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 65 : 26), GUILayout.Height(20))) {
                            draggedStepIndex = isBeingDragged ? -1 : i;
                        }

                        GUILayout.Box("↵ EndTarget", pillEndTargetStyle, GUILayout.Width(80), GUILayout.Height(20));
                        GUILayout.Label($"<b>闭合求解循环: '{(string.IsNullOrEmpty(step.label) ? "结束打靶" : step.label)}'</b>", labelValueStyle);
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

                    if (GUILayout.Button(isBeingDragged ? "✊ 拖动中" : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 65 : 26), GUILayout.Height(20))) {
                        draggedStepIndex = isBeingDragged ? -1 : i;
                    }

                    // Modern Pill Badge by Type with Auto Width & Overflow Prevention
                    string badgeTextChild = GetStepTypeBadgeText(step.type);
                    GUIStyle pillStyleChild = GetStepPillStyle(step.type);
                    GUILayout.Box(badgeTextChild, pillStyleChild, GUILayout.MinWidth(75), GUILayout.ExpandWidth(false), GUILayout.Height(20));

                    if (step.is_collapsed) {
                        string summaryBadge = "";
                        if (step.type == "Propagate") summaryBadge = $"<color=#f87171><b>{step.propagator}</b></color> ➔ 停止: <b>{step.stop_type}</b> {(!string.IsNullOrEmpty(step.duration_str) ? $"({step.duration_str})" : (step.stop_type == "Time" ? $"({step.duration:F0}s)" : ""))}";
                        else if (step.type == "Vary") summaryBadge = $"<color=#38bdf8><b>{step.vary_variable}</b></color> (初猜: {(!string.IsNullOrEmpty(step.vary_initial_str) ? step.vary_initial_str : step.vary_initial.ToString("F1"))}, 范围: [{step.vary_lower:F1}, {step.vary_upper:F1}])";
                        else if (step.type == "Achieve") summaryBadge = $"<color=#38bdf8><b>{step.achieve_param}</b></color> = <b>{(!string.IsNullOrEmpty(step.achieve_val_str) ? step.achieve_val_str : step.achieve_val.ToString("F2"))}</b> (容差: ±{step.achieve_tolerance})";
                        else if (step.type == "FiniteBurn") summaryBadge = $"时长: <b>{(step.burn_use_vary_var ? step.burn_vary_var_name : (!string.IsNullOrEmpty(step.duration_str) ? step.duration_str : step.duration.ToString("F1") + "s"))}</b>";
                        else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") summaryBadge = $"🧭 VNB 矢量 = ({step.thrust_dir1:F2}, {step.thrust_dir2:F2}, {step.thrust_dir3:F2})";
                        else if (step.type == "ImpulsiveBurn") summaryBadge = $"ΔV = ({step.dv_v:F1}, {step.dv_n:F1}, {step.dv_b:F1}) m/s";
                        else if (step.type == "Equation") summaryBadge = $"{step.variable} = {step.expression}";
                        else if (step.type == "StageSeparation") summaryBadge = $"抛离死重: {step.separated_mass:F0} kg (剩余干重: {step.remaining_dry_mass:F0} kg)";
                        else summaryBadge = string.IsNullOrEmpty(step.label) ? "" : step.label;
                        GUILayout.Label(summaryBadge, labelValueStyle);
                        DrawStepCollapsedResultBadge(step);
                    } else {
                        GUILayout.Space(2);
                        step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? "" : step.label, stepLabelInputStyle, GUILayout.MinWidth(130), GUILayout.MaxWidth(280), GUILayout.Height(20));
                    }

                    GUILayout.FlexibleSpace();

                    if (step.type == "Propagate" && !step.is_collapsed) {
                        if (GUILayout.Button("⚙ 力模型", secondaryButtonStyle, GUILayout.Height(18))) {
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
                        float stripW = isHoveredNow ? 6f : 3.5f;
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

                if (GUILayout.Button(isBeingDragged ? "✊ 拖动中" : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 65 : 26), GUILayout.Height(20))) {
                    draggedStepIndex = isBeingDragged ? -1 : i;
                }

                // Modern Pill Badge by Type with Auto Width & Overflow Prevention
                string badgeText = GetStepTypeBadgeText(step.type);
                GUIStyle pillStyle = GetStepPillStyle(step.type);
                GUILayout.Box(badgeText, pillStyle, GUILayout.MinWidth(75), GUILayout.ExpandWidth(false), GUILayout.Height(20));

                if (step.is_collapsed) {
                    string summaryBadge = "";
                    if (step.type == "Propagate") summaryBadge = $"<color=#f87171><b>{step.propagator}</b></color> ➔ 停止: <b>{step.stop_type}</b> {(step.stop_type == "Time" ? $"({step.duration:F0}s)" : "")}";
                    else if (step.type == "Report") summaryBadge = $"🧾 <b>状态报告</b>";
                    else if (step.type == "Equation") summaryBadge = $"{step.variable} = {step.expression}";
                    else if (step.type == "FiniteBurn") summaryBadge = $"时长: <b>{(step.burn_use_vary_var ? step.burn_vary_var_name : step.duration.ToString("F1") + "s")}</b>";
                    else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") summaryBadge = $"🧭 VNB 矢量 = ({step.thrust_dir1:F2}, {step.thrust_dir2:F2}, {step.thrust_dir3:F2})";
                    else if (step.type == "ImpulsiveBurn") summaryBadge = $"ΔV = ({step.dv_v:F1}, {step.dv_n:F1}, {step.dv_b:F1}) m/s";
                    else summaryBadge = string.IsNullOrEmpty(step.label) ? "" : step.label;
                    GUILayout.Label(summaryBadge, labelValueStyle);
                    DrawStepCollapsedResultBadge(step);
                } else {
                    GUILayout.Space(2);
                    step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? "" : step.label, stepLabelInputStyle, GUILayout.MinWidth(130), GUILayout.MaxWidth(280), GUILayout.Height(20));
                }

                GUILayout.FlexibleSpace();

                if (step.type == "Propagate" && !step.is_collapsed) {
                    if (GUILayout.Button("⚙ 力模型", secondaryButtonStyle, GUILayout.Height(18))) {
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
                    float stripW = isHoveredNow ? 6f : 3.5f;
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
                GUILayout.Label($"<color=#facc15><b>✊ 正在重排步骤 #{draggedStepIndex + 1} ({draggedItem.type} - {draggedItem.label})</b></color>", labelValueStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"⬇ 移动至任务序列末尾 (步骤 #{missionSequence.Count})", tabButtonActiveStyle, GUILayout.Height(24))) {
                    missionSequence.RemoveAt(draggedStepIndex);
                    missionSequence.Add(draggedItem);
                    ScreenMessages.PostScreenMessage($"↕️ 步骤已成功移动至序列末尾", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    draggedStepIndex = -1;
                }
                if (GUILayout.Button("✕ 取消拖拽重排", secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(24))) {
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
                    label = $"Vary 寻优 ({suggestedVar})"
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
                    label = $"Achieve 目标 ({cb}.SMA)"
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
                    label = $"脉冲矢量机动 ({burnName})"
                });
            }
            if (stepToInsertBurn >= 0) {
                string parentDc = GetParentTargetDcName(stepToInsertBurn);
                var fbStep = CreateFiniteBurnStep("变轨有限推力点火");
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
                    label = $"轨道积分 ({cb}.Apoapsis)"
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
                    label = $"推力矢量寻优 ({engName})"
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
                missionSequence.Add(new MissionStep { type = "Propagate", propagator = propName, stop_type = "Time", duration = 3600.0, label = $"轨道积分推进 ({cb})" });
            }
            if (GUILayout.Button(I18n.T("add_target"), actionButtonStyle, GUILayout.Height(24))) {
                string cb = GetContextCentralBody();
                string newDc = $"DC_{missionSequence.Count + 1}";
                double defaultVal = (cb == "Earth") ? 42164.14 : ((cb == "Luna" || cb == "Moon") ? 1837.4 : 7000.0);
                missionSequence.Add(new MissionStep { type = "Target", label = $"打靶求解组 ({newDc})", dc_name = newDc, dc_algorithm = "NewtonRaphson", dc_max_iter = 50 });
                missionSequence.Add(new MissionStep { type = "Vary", vary_variable = $"{newDc}_Duration", vary_initial = 120.0, vary_lower = 10.0, vary_upper = 1000.0, vary_max_step = 10.0, dc_name = newDc, label = $"Vary 点火寻优" });
                missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = $"Sat.{cb}.SMA", achieve_val = defaultVal, achieve_tolerance = 1.0, dc_name = newDc, label = $"Achieve 目标约束 ({cb}.SMA)" });
                missionSequence.Add(new MissionStep { type = "EndTarget", label = $"闭合打靶 ({newDc})", dc_name = newDc });
            }
            if (GUILayout.Button(I18n.T("add_finiteburn"), secondaryButtonStyle, GUILayout.Height(24))) {
                missionSequence.Add(CreateFiniteBurnStep("有限推力点火"));
            }
            if (GUILayout.Button(I18n.T("add_impulsive"), secondaryButtonStyle, GUILayout.Height(24))) {
                string burnName = $"Burn_{missionSequence.Count + 1}";
                missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", burn_name = burnName, dv_v = 500.0, dv_n = 0.0, dv_b = 0.0, label = $"脉冲矢量机动 ({burnName})" });
            }
            if (GUILayout.Button(I18n.T("add_stagesep"), secondaryButtonStyle, GUILayout.Height(24))) {
                double curDry = vessel != null ? vessel.totalMass * 1000.0 * 0.4 : 1500.0;
                missionSequence.Add(new MissionStep { type = "StageSeparation", separated_mass = 2500.0, remaining_dry_mass = curDry, label = "级段分离 / 抛死重" });
            }
            if (GUILayout.Button(I18n.T("add_report"), secondaryButtonStyle, GUILayout.Height(24))) {
                missionSequence.Add(new MissionStep { type = "Report", label = "轨道瞬时状态报告" });
            }
            GUILayout.EndHorizontal();

            // Expanded 27 GMAT Command Library Rows
            if (showCommandPalette) {
                GUILayout.Space(4);

                // Row 1: Targeting & Maneuvers
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#facc15><b>打靶与控制:</b></color>", labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("Target", secondaryButtonStyle, GUILayout.Height(20))) {
                    string dcName = $"DC_{missionSequence.Count + 1}";
                    missionSequence.Add(new MissionStep { type = "Target", label = $"打靶控制块 ({dcName})", dc_name = dcName });
                }
                if (GUILayout.Button("Vary", secondaryButtonStyle, GUILayout.Height(20))) {
                    string activeDc = GetAvailableSolverList()[0];
                    missionSequence.Add(new MissionStep { type = "Vary", vary_variable = $"{activeDc}_Duration", vary_initial = 120.0, vary_lower = 10.0, vary_upper = 1000.0, vary_max_step = 10.0, dc_name = activeDc, label = "Vary 寻优" });
                }
                if (GUILayout.Button("Achieve", secondaryButtonStyle, GUILayout.Height(20))) {
                    string cb = GetContextCentralBody();
                    string activeDc = GetAvailableSolverList()[0];
                    double defaultVal = (cb == "Earth") ? 42164.14 : 7000.0;
                    missionSequence.Add(new MissionStep { type = "Achieve", achieve_param = $"Sat.{cb}.SMA", achieve_val = defaultVal, achieve_tolerance = 1.0, dc_name = activeDc, label = $"Achieve 约束 ({cb})" });
                }
                if (GUILayout.Button("EndTarget", secondaryButtonStyle, GUILayout.Height(20))) {
                    missionSequence.Add(new MissionStep { type = "EndTarget", label = "闭合打靶" });
                }
                if (GUILayout.Button("BeginFiniteBurn", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "BeginFiniteBurn", burn_name = "Finite_1", label = "开启有限推力" });
                if (GUILayout.Button("EndFiniteBurn", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndFiniteBurn", burn_name = "Finite_1", label = "关闭有限推力" });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: Optimization
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#34d399><b>优化与极值:</b></color>", labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("Optimize", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Optimize", label = "SQP 全局优化", optimizer_name = "SQP1" });
                if (GUILayout.Button("Minimize", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Minimize", minimize_variable = "Sat.FuelMassBurned", optimizer_name = "SQP1", label = "极小化消耗" });
                if (GUILayout.Button("NonlinearConstraint", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "NonlinearConstraint", constraint_lhs = "Sat.Earth.RMAG", constraint_op = ">=", constraint_val = 6578.14, label = "非线性不等式约束" });
                if (GUILayout.Button("EndOptimize", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndOptimize", label = "闭合优化循环" });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 3: Control Flow & Logic
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#a78bfa><b>分支与循环:</b></color>", labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("If", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "If", condition_expr = "Sat.Earth.RMAG < 7000.0", label = "条件分支" });
                if (GUILayout.Button("Else", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Else", label = "否则分支" });
                if (GUILayout.Button("EndIf", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndIf", label = "闭合条件" });
                if (GUILayout.Button("For", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "For", for_var = "i", for_range = "1:1:2", label = "For 循环" });
                if (GUILayout.Button("EndFor", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndFor", label = "闭合 For" });
                if (GUILayout.Button("While", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "While", condition_expr = "Sat.ElapsedDays < 3.0", label = "While 循环" });
                if (GUILayout.Button("EndWhile", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "EndWhile", label = "闭合 While" });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 4: Math, Variables & IO
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>数学与指令:</b></color>", labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("Equation (变量赋值)", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Equation", variable = "burntime", expression = "120.0", label = "变量赋值" });
                if (GUILayout.Button("ScriptEvent (内联脚本)", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "ScriptEvent", script_content = "GMAT burntime = 150.0;", label = "内联数学语句" });
                if (GUILayout.Button("Write (控制台输出)", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Write", write_fields = "Sat.UTCGregorian Sat.RMAG", label = "输出日志" });
                if (GUILayout.Button("Toggle (视图显隐)", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Toggle", subscriber_name = "EarthInertialView", action_state = "Off", label = "切换绘图" });
                if (GUILayout.Button("Stop (终止任务)", secondaryButtonStyle, GUILayout.Height(20))) missionSequence.Add(new MissionStep { type = "Stop", label = "终止执行" });
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
            GUILayout.Label("⚡ 任务执行与求解中枢 (Solver Center)", headerStyle);
            GUILayout.FlexibleSpace();
            if (isPropagating) {
                GUILayout.Label($"<color=#facc15>⏱️ 耗时: {solveTotalTime:F1}s</color>", labelValueStyle);
            } else if (solveTotalTime > 0) {
                GUILayout.Label($"<color=#94a3b8>上回耗时: {solveTotalTime:F2}s</color>", labelValueStyle);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (isPropagating) {
                GUILayout.BeginHorizontal();
                GUILayout.Button("⏳ GMAT 多体引力打靶求解中...", actionButtonStyle, GUILayout.Height(38), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("🛑 中断求解", deleteButtonStyle, GUILayout.Width(110), GUILayout.Height(38))) {
                    GMATClient.AbortCurrentExecution();
                    isPropagating = false;
                    solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                    solveStageText = $"⚠️ 用户已手动中断 GMAT 求解 (耗时 {solveTotalTime:F1}s)";
                    solveProgress = 0f;
                    ScreenMessages.PostScreenMessage("⚠️ 已手动中断 GMAT 物理求解进程。", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();
            } else {
                GUI.enabled = (vessel != null);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🚀 启动 GMAT 全序列高保真求解", actionButtonStyle, GUILayout.Height(38), GUILayout.ExpandWidth(true))) {
                    isPropagating = true;
                    solveStartTime = Time.realtimeSinceStartup;
                    solveProgress = 0.05f;
                    solveStageText = "1/4 🛰️ 抓取飞船与引力场真实初始状态...";
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
                        solveStageText = success ? $"✅ GMAT 求解收敛完成 (耗时 {solveTotalTime:F2}s)！" : "❌ GMAT 求解失败";
                        latestResult = result;

                        SyncSolvedValuesToSequence(result);
                        if (success) {
                            if (result != null && result.trajectory != null && result.trajectory.Count > 0) {
                                simulationTexture = GMATOrbitCanvas.RenderTrajectoryTexture(result.trajectory, cBody);
                            }
                            ScreenMessages.PostScreenMessage($"✅ GMAT 求解成功 (耗时 {solveTotalTime:F2}s)！机动数值与发射窗口已更新。", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                        } else {
                            string errTxt = (result != null && !string.IsNullOrEmpty(result.error)) ? result.error : "打靶未完全收敛";
                            ScreenMessages.PostScreenMessage("⚠️ GMAT 求解提示: " + errTxt, 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    });
                }
                if (GUILayout.Button("🧹 清除残差", secondaryButtonStyle, GUILayout.Width(100), GUILayout.Height(38))) {
                    ClearSolutionResults();
                }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }

            // =========================================================================
            // LIVE SOLVER PROGRESS BAR & STEP FLOW HUD
            // =========================================================================
            GUILayout.Space(6);
            DrawProgressBar(solveProgress, isPropagating ? solveStageText : (latestResult != null && latestResult.success ? $"✅ 求解收敛完成 ({latestResult.solved_burns.Count} 个机动)" : solveStageText), 24f);

            // Step Pipeline Flow Tracker Ribbon
            if (missionSequence != null && missionSequence.Count > 0) {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#94a3b8>序列进度:</color>", labelValueStyle, GUILayout.Width(60));

                int maxShow = Math.Min(5, missionSequence.Count);
                for (int s = 0; s < maxShow; s++) {
                    var st = missionSequence[s];
                    string sIcon = isPropagating ? (s < solveCurrentStepIndex ? "<color=#34d399>✔</color>" : (s == solveCurrentStepIndex ? "<color=#facc15>⏳</color>" : "<color=#64748b>⚪</color>")) : (latestResult != null && latestResult.success ? "<color=#34d399>✔</color>" : "<color=#64748b>⚪</color>");
                    string shortLabel = (st.type == "Propagate") ? "积分" : (st.type == "Target") ? "打靶" : (st.type == "Vary") ? "Vary" : (st.type == "Achieve") ? "目标" : (st.type == "FiniteBurn" || st.type == "ImpulsiveBurn" || st.type == "Maneuver") ? "点火" : st.type;
                    GUILayout.Label($"{sIcon} #{s + 1} {shortLabel}", labelValueStyle);
                }
                if (missionSequence.Count > maxShow) {
                    GUILayout.Label($"<color=#94a3b8>+{missionSequence.Count - maxShow}步</color>", labelValueStyle);
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
            GUILayout.Label("<b>📊 GMAT 官方原生打靶求解看板 (Results & Targeters)</b>", headerStyle);
            GUILayout.Space(3);

            if (latestResult != null && latestResult.success) {
                // 1. Top Orbital Elements Snapshot Frame
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"最终半长轴: <color=#38bdf8><b>{latestResult.final_sma:F1} km</b></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"离心率: <color=#38bdf8><b>{latestResult.final_ecc:F4}</b></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"倾角: <color=#38bdf8><b>{latestResult.final_inc:F2}°</b></color>", labelValueStyle);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // 2. 打靶器收敛状态矩阵 (Per-DC Matrix)
                if (latestResult.dc_summaries != null && latestResult.dc_summaries.Count > 0) {
                    GUILayout.Space(3);
                    GUILayout.Label("<color=#38bdf8><b>🎯 微分修正器打靶矩阵 (Differential Corrector Status):</b></color>", labelValueStyle);
                    
                    foreach (var dcp in latestResult.dc_summaries) {
                        string dcName = dcp.Key;
                        var dcs = dcp.Value;
                        string statusTag = dcs.converged ? $"<color=#34d399>✅ 已收敛 ({dcs.iterations} 次迭代)</color>" : "<color=#f87171>❌ 未收敛</color>";

                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<b>打靶器: {dcName}</b>", labelValueStyle);
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
                                GUILayout.Label($" • <b>变量解:</b> {string.Join(" | ", varStrs.ToArray())}", labelValueStyle);
                            }
                        }

                        // 显示达成目标
                        if (dcs.goals_summary != null && dcs.goals_summary.Count > 0) {
                            GUILayout.Space(2);
                            foreach (var g in dcs.goals_summary) {
                                double err = g.Achieved - g.Desired;
                                string col = Math.Abs(err) < 1.0 ? "#34d399" : "#facc15";
                                GUILayout.Label($" • <b>目标达成:</b> {g.GoalName} ➔ 达成 <color={col}><b>{g.Achieved:F2}</b></color> (目标 {g.Desired:F2})", labelValueStyle);
                            }
                        }

                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                // 3. 解算机动列表 (Solved Burns Frames)
                if (latestResult.solved_burns != null && latestResult.solved_burns.Count > 0) {
                    GUILayout.Space(3);
                    GUILayout.Label($"<color=#34d399><b>🚀 解算机动点火 ({latestResult.solved_burns.Count} 个):</b></color>", labelValueStyle);
                    for (int bIdx = 0; bIdx < latestResult.solved_burns.Count; bIdx++) {
                        var sb = latestResult.solved_burns[bIdx];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<b>#{bIdx + 1} {sb.name}</b> <color=#94a3b8>({sb.type})</color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"<color=#38bdf8><b>ΔV = {sb.magnitude:F2} m/s</b></color>", labelValueStyle);
                        GUILayout.EndHorizontal();
                        GUILayout.Space(2);
                        GUILayout.Label($" • <b>推力分解:</b> 切向 <b>{sb.dv_v:F1}</b> | 变面 <b>{sb.dv_n:F1}</b> | 径向 <b>{sb.dv_b:F1}</b> m/s", labelValueStyle);
                        GUILayout.Label($" • <b>点火时相:</b> 时长 <b><color=#facc15>{sb.duration:F1}s</color></b> | 推力 <b>{sb.thrust_kn:F1} kN</b> (Isp {sb.isp:F0}s)", labelValueStyle);
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                if (latestResult.has_fuel_deficit) {
                    GUILayout.Space(2);
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.Label($"<color=#f87171>⚠️ <b>燃料赤字:</b> {latestResult.fuel_deficit:F1} kg</color>", labelValueStyle);
                    GUILayout.EndVertical();
                }
            } else if (latestResult != null && !latestResult.success) {
                string shortError = (latestResult.error ?? "求解未收敛").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                GUILayout.Label($"<color=#f87171><b>❌ GMAT 求解失败:</b> {shortError}</color>", labelValueStyle);
                
                // Detailed Error Diagnostic Box (Constrained & Scrollable)
                string errDetails = !string.IsNullOrEmpty(latestResult.error_details) ? latestResult.error_details : latestResult.error;
                if (!string.IsNullOrEmpty(errDetails)) {
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<color=#94a3b8><b>📋 详细错误诊断与引擎日志:</b></color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    string foldTxt = showFullErrorLog ? "▾ 收起日志" : "▸ 展开日志";
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

                        if (GUILayout.Button("📋 复制完整报错日志", secondaryButtonStyle, GUILayout.Height(22))) {
                            GUIUtility.systemCopyBuffer = $"[GMAT Error]\nSummary: {shortError}\nDetails:\n{errDetails}\nScript: {latestResult.script_path}";
                            ScreenMessages.PostScreenMessage("📋 完整报错日志已复制至系统剪贴板！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
            } else {
                GUILayout.Label("<color=#64748b>就绪。点击上方按钮启动高保真多体引力打靶计算。</color>", labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // =========================================================================
            // CARD 3: VEHICLE & PROPULSION SNAPSHOT (STAGING & ENGINES)
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            var b = GMATStateManager.Current.GetActiveStageBudget();
            GUILayout.Label("<b>🚀 多级火箭推进与质量预算</b>", headerStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#34d399><size=11>● LIVE 同步</size></color>", labelValueStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(3);

            if (!string.IsNullOrEmpty(b.EngineTitle)) {
                // Frame 1: Stage Mass & Delta-V Summary Frame
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>Stage {b.StageNumber} 推进构型</b>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=#34d399><b>设计能力 ΔV: {b.DeltaV:F0} m/s</b></color>", labelValueStyle);
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
                GUILayout.Label($" • <b>质量分配:</b> 湿重 <b>{b.WetMassTonnes:F2}t</b> | 燃料 <b>{b.FuelMassTonnes:F2}t</b> | 干重 <b>{b.DryMassTonnes:F2}t</b>", labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.Space(3);

                // Frame 2: Hardware & Propellant Specs Frame
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label($" • <b>主推力器:</b> <color=#38bdf8><b>{b.EngineTitle}</b></color> (额定推力 <b>{b.ThrustKN:F1} kN</b> | 比冲 <b>{b.IspVac:F0} s</b>)", labelValueStyle);
                GUILayout.Label($" • <b>推进剂组元:</b> <color=#facc15><b>{b.PropellantDesc}</b></color> (密度 <b>{b.FuelDensityKgM3:F0} kg/m³</b>)", labelValueStyle);
                GUILayout.EndVertical();
            } else if (vessel != null) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label($"飞船总质量: <b>{vessel.GetTotalMass():F2} 吨</b>", labelValueStyle);
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // =========================================================================
            // CARD 4: TOOLS & EXTERNAL INTEROP
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("<b>🔗 分析工具与外部联动 (Tools & Interop)</b>", headerStyle);
            GUILayout.Space(2);

            if (GUILayout.Button("🖼️ 查看 GMAT 仿真轨迹图像", actionButtonStyle, GUILayout.Height(26))) {
                LoadSimulationTexture();
                showSimulationImageWindow = true;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⚙️ 传播子配置", secondaryButtonStyle, GUILayout.Height(24))) {
                showPropagatorConfigWindow = true;
            }
            if (GUILayout.Button("🛰️ 航天器参数", secondaryButtonStyle, GUILayout.Height(24))) {
                showCraftDetailsWindow = true;
            }
            GUILayout.EndHorizontal();

            GUI.enabled = (latestResult != null && latestResult.success && latestResult.solved_burns.Count > 0);
            if (GUILayout.Button("🔥 一键写入所有机动至 Principia", successButtonStyle, GUILayout.Height(30))) {
                WriteBurnsToPrincipia(vessel);
            }
            GUI.enabled = (latestResult != null && latestResult.success);

            if (GUILayout.Button("🖥️ 在 GMAT 桌面端打开 3D 渲染", secondaryButtonStyle, GUILayout.Height(24))) {
                if (!string.IsNullOrEmpty(latestResult.script_path)) {
                    GMATClient.OpenGmatDesktop(latestResult.script_path);
                    ScreenMessages.PostScreenMessage("正在启动 GMAT 桌面端 GUI...", 3.0f, ScreenMessageStyle.UPPER_CENTER);
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
                GUILayout.Label("动力学传播子:", labelValueStyle, GUILayout.Width(85));

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
                GUILayout.Label("停止条件:", labelValueStyle, GUILayout.Width(65));

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

                if (GUILayout.Button("📋 条件库...", secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(20))) {
                    OpenParamPicker(i, "prop_stop");
                }

                string propBadge = GMATParameterRegistry.GetParamBadgeI18n(step.stop_type);
                if (!string.IsNullOrEmpty(propBadge)) {
                    GUILayout.Label($"<color=#38bdf8><b>[{propBadge}]</b></color>", labelValueStyle);
                }

                if (IsPureOrbitalEvent(step.stop_type)) {
                    GUILayout.Label("<color=#34d399><b>● 极值事件 (自动精确积分至最高/低点，无需设值)</b></color>", labelValueStyle);
                } else {
                    GUILayout.Space(8);
                    GUILayout.Label("<color=#94a3b8>目标阈值 / 变量:</color>", gridLabelStyle, GUILayout.Width(95));

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
                            GUILayout.Label($"<color=#facc15><b>[变量: {matchedVar.Name} = {matchedVar.InitialValue}]</b></color>", labelValueStyle);
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
                GUILayout.Label("<color=#38bdf8><b>寻优变量 (Variable):</b></color>", labelValueStyle, GUILayout.Width(130));

                GUI.SetNextControlName($"Field_Vary_{i}");
                string prevVary = step.vary_variable ?? "burntime";
                string newVary = GUILayout.TextField(prevVary, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true), GUILayout.Height(22));
                if (newVary != prevVary) step.vary_variable = newVary;

                if (GUILayout.Button("📝 Edit / 选择变量...", tabButtonActiveStyle, GUILayout.Width(130), GUILayout.Height(22))) {
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
                GUILayout.Label("<color=#94a3b8>初猜:</color>", gridLabelStyle, GUILayout.Width(35));
                step.vary_initial_str = DrawDelayedSmartField($"Field_VaryInit_{i}", step.vary_initial_str, step.vary_initial, numericInputStyle, GUILayout.Width(75), "vary_init", i);
                if (double.TryParse(step.vary_initial_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryInit)) {
                    step.vary_initial = parsedVaryInit;
                }

                GUILayout.Space(6);
                GUILayout.Label("<color=#94a3b8>下界:</color>", gridLabelStyle, GUILayout.Width(35));
                step.vary_lower_str = DrawDelayedSmartField($"vary_low_{i}", step.vary_lower_str, step.vary_lower, numericInputStyle, GUILayout.Width(60), null, i);
                if (double.TryParse(step.vary_lower_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryLow)) {
                    step.vary_lower = parsedVaryLow;
                }

                GUILayout.Space(6);
                GUILayout.Label("<color=#94a3b8>上界:</color>", gridLabelStyle, GUILayout.Width(35));
                step.vary_upper_str = DrawDelayedSmartField($"vary_up_{i}", step.vary_upper_str, step.vary_upper, numericInputStyle, GUILayout.Width(60), null, i);
                if (double.TryParse(step.vary_upper_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryUp)) {
                    step.vary_upper = parsedVaryUp;
                }

                GUILayout.Space(6);
                GUILayout.Label("<color=#94a3b8>步长:</color>", gridLabelStyle, GUILayout.Width(35));
                step.vary_max_step_str = DrawDelayedSmartField($"vary_step_{i}", step.vary_max_step_str, (step.vary_max_step <= 0 ? 0.1 : step.vary_max_step), numericInputStyle, GUILayout.Width(55), null, i);
                if (double.TryParse(step.vary_max_step_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVaryStep)) {
                    step.vary_max_step = parsedVaryStep;
                }

                // Inline Solved Value
                if (step.has_solved_value) {
                    string unitStr = (step.vary_variable != null && (step.vary_variable.Contains("Direction") || step.vary_variable.Contains("Thrust"))) ? "" : ((step.vary_variable != null && step.vary_variable.Contains("Element")) ? "km/s" : "s");
                    GUILayout.Space(12);
                    GUILayout.Label($"<color=#34d399><b>➔ 🎯 求解收敛解: {step.solved_value_str} {unitStr}</b></color>", labelValueStyle);
                    if (GUILayout.Button("📌 应用为初猜", secondaryButtonStyle, GUILayout.Height(20))) {
                        step.vary_initial = step.solved_value;
                        step.vary_initial_str = step.solved_value_str;
                        inputStringCache[$"Field_VaryInit_{i}"] = step.solved_value_str;
                        ScreenMessages.PostScreenMessage($"📌 已将收敛解 {step.solved_value_str} 填入初猜！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
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
                        GUILayout.Label($"<color=#34d399><b>➔ 🎯 求解收敛解: {solvedVal:F4} {unitStr}</b></color>", labelValueStyle);
                        if (GUILayout.Button("📌 应用为初猜", secondaryButtonStyle, GUILayout.Height(20))) {
                            step.vary_initial = solvedVal;
                            step.vary_initial_str = solvedVal.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                            inputStringCache[$"Field_VaryInit_{i}"] = step.vary_initial_str;
                            ScreenMessages.PostScreenMessage($"📌 已将收敛解 {solvedVal:F4} 填入初猜！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
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
                GUILayout.Label("<color=#38bdf8><b>约束目标 (Goal):</b></color>", labelValueStyle, GUILayout.Width(110));

                GUI.SetNextControlName($"Field_Achieve_{i}");
                string prevAchieve = step.achieve_param ?? "Sat.Earth.Altitude";
                string newAchieve = GUILayout.TextField(prevAchieve, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true), GUILayout.Height(22));

                if (GUILayout.Button("📝 Edit / 选择目标...", tabButtonActiveStyle, GUILayout.Width(130), GUILayout.Height(22))) {
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
                GUILayout.Label("<color=#94a3b8>期望目标值:</color>", gridLabelStyle, GUILayout.Width(75));
                step.achieve_val_str = DrawDelayedSmartField($"Field_AchVal_{i}", step.achieve_val_str, step.achieve_val, numericInputStyle, GUILayout.Width(95), "achieve_val", i);
                if (double.TryParse(step.achieve_val_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedAchVal)) {
                    step.achieve_val = parsedAchVal;
                }

                GUILayout.Space(8);
                GUILayout.Label("<color=#94a3b8>容差: ±</color>", gridLabelStyle, GUILayout.Width(50));
                step.achieve_tolerance_str = DrawDelayedSmartField($"Field_AchTol_{i}", step.achieve_tolerance_str, step.achieve_tolerance, numericInputStyle, GUILayout.Width(65), null, i);
                if (double.TryParse(step.achieve_tolerance_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedAchTol)) {
                    step.achieve_tolerance = parsedAchTol;
                }

                // Inline Solved Result
                if (step.has_achieve_result) {
                    string col = step.achieve_passed ? "#34d399" : "#f87171";
                    string icon = step.achieve_passed ? "🎯" : "⚠️";
                    string convStatus = step.solver_converged ? "收敛" : "未收敛";
                    GUILayout.Space(12);
                    GUILayout.Label($"<color={col}><b>➔ {icon} 打靶达成值: {step.achieved_value:F3}</b></color> <color=#94a3b8>(残差: {step.achieved_variance:F4} | {step.solver_iterations} 步 {convStatus})</color>", labelValueStyle);
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
                GUILayout.Label("<color=#38bdf8><b>脉冲机动配置 (Impulsive Maneuver):</b></color>", labelValueStyle, GUILayout.Width(170));
                step.burn_name = GUILayout.TextField(string.IsNullOrEmpty(step.burn_name) ? "ImpulsiveBurn" : step.burn_name, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110), GUILayout.Height(20));

                GUILayout.Label("<color=#94a3b8>坐标轴:</color>", labelValueStyle, GUILayout.Width(45));
                step.burn_axes = GUILayout.TextField(string.IsNullOrEmpty(step.burn_axes) ? "VNB" : step.burn_axes, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(45), GUILayout.Height(20));

                GUILayout.Label("<color=#94a3b8>参考天体:</color>", labelValueStyle, GUILayout.Width(60));
                step.burn_origin = GUILayout.TextField(string.IsNullOrEmpty(step.burn_origin) ? "Earth" : step.burn_origin, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(65), GUILayout.Height(20));

                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#34d399><size=11>● LIVE 硬件实时同步</size></color>", labelValueStyle);
                GUILayout.EndHorizontal();

                // Delta-V Components Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#94a3b8>切向 ΔV_V (m/s):</color>", gridLabelStyle, GUILayout.Width(105));
                step.dv_v = DrawDelayedNumericField($"burn_v_{i}", step.dv_v, numericInputStyle, GUILayout.Width(70), "F2");

                GUILayout.Space(8);
                GUILayout.Label("<color=#94a3b8>法向 ΔV_N:</color>", gridLabelStyle, GUILayout.Width(70));
                step.dv_n = DrawDelayedNumericField($"burn_n_{i}", step.dv_n, numericInputStyle, GUILayout.Width(65), "F2");

                GUILayout.Space(8);
                GUILayout.Label("<color=#94a3b8>径向 ΔV_B:</color>", gridLabelStyle, GUILayout.Width(70));
                step.dv_b = DrawDelayedNumericField($"burn_b_{i}", step.dv_b, numericInputStyle, GUILayout.Width(65), "F2");

                double totalDv = Math.Sqrt(step.dv_v * step.dv_v + step.dv_n * step.dv_n + step.dv_b * step.dv_b);
                GUILayout.Space(12);
                GUILayout.Label($"<b>总 ΔV: <color=#34d399>{totalDv:F2} m/s</color></b>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Hardware Binding Section
                GUILayout.Space(3);
                GUILayout.BeginVertical(nestedCardBoxStyle);

                // Engine Selector Row
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>🚀 点火发动机:</b>", labelValueStyle, GUILayout.Width(90));
                if (allEngines.Count > 0) {
                    for (int eg = 0; eg < allEngines.Count; eg++) {
                        var eng = allEngines[eg];
                        string engId = GMATParameterRegistry.SanitizeGmatIdentifier(eng.title, "Engine");
                        bool isCurEng = (step.bound_thruster == engId) || (string.IsNullOrEmpty(step.bound_thruster) && eg == 0);
                        if (GUILayout.Button($"🚀 {eng.title} ({eng.thrustKN:F0}kN / {eng.ispVac:F0}s)", isCurEng ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
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
                            ScreenMessages.PostScreenMessage($"🚀 已同步机动发动机: {eng.title}", 2.0f, ScreenMessageStyle.UPPER_CENTER);
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
                GUILayout.Label("<b>⛽ 供油储罐:</b>", labelValueStyle, GUILayout.Width(90));
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
                        if (GUILayout.Button($"⛽ Stage {tnk.stageNumber} ({tnk.fuelMassKg:F0}kg 燃料)", isCurTank ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            step.bound_tank = tkId;
                            if (cachedStages != null) {
                                for (int s = 0; s < cachedStages.Count; s++) {
                                    if (cachedStages[s].stageNumber == tnk.stageNumber && !selectedStageIndices.Contains(s)) {
                                        selectedStageIndices.Add(s);
                                    }
                                }
                            }
                            ScreenMessages.PostScreenMessage($"⛽ 已同步供油储罐: Stage {tnk.stageNumber} ({tnk.fuelMassKg:F0}kg)", 2.0f, ScreenMessageStyle.UPPER_CENTER);
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
                step.burn_decrement_mass = GUILayout.Toggle(step.burn_decrement_mass, " 扣减消耗燃料质量", GUILayout.Width(140));
                GUILayout.Label("比冲 Isp (s):", labelValueStyle, GUILayout.Width(80));
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
                GUILayout.Label("<color=#f97316><b>🔥 有限推力点火配置:</b></color>", labelValueStyle, GUILayout.Width(135));

                GUILayout.Label("<color=#94a3b8>对象名:</color>", gridLabelStyle, GUILayout.Width(45));
                step.burn_name = GUILayout.TextField(string.IsNullOrEmpty(step.burn_name) ? $"Burn_{i + 1}" : step.burn_name, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(85), GUILayout.Height(20));

                GUILayout.Space(6);
                GUILayout.Label("<color=#94a3b8>参考系:</color>", gridLabelStyle, GUILayout.Width(45));
                step.burn_coordinate_system = GUILayout.TextField(string.IsNullOrEmpty(step.burn_coordinate_system) ? "Local" : step.burn_coordinate_system, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(55), GUILayout.Height(20));
                step.burn_axes = GUILayout.TextField(string.IsNullOrEmpty(step.burn_axes) ? "VNB" : step.burn_axes, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(42), GUILayout.Height(20));

                GUILayout.Space(8);
                step.burn_use_vary_var = GUILayout.Toggle(step.burn_use_vary_var, " 使用 Vary 变量控制时长", GUILayout.Width(160));
                if (step.burn_use_vary_var) {
                    GUILayout.Label("<color=#94a3b8>变量名:</color>", gridLabelStyle, GUILayout.Width(45));
                    string curVName = !string.IsNullOrEmpty(step.burn_vary_var_name) ? step.burn_vary_var_name : (!string.IsNullOrEmpty(step.duration_str) ? step.duration_str : "burntime");
                    string newVName = DrawDelayedSmartField($"fburn_var_{i}", curVName, 0.0, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110), "fburn_var", i);
                    if (newVName != curVName) {
                        step.burn_vary_var_name = newVName;
                        step.duration_str = newVName;
                    }

                    if (GMATStateManager.Current != null && GMATStateManager.Current.Variables != null) {
                        var matchedVar = GMATStateManager.Current.Variables.Find(v => v.Name.Equals(step.burn_vary_var_name, StringComparison.OrdinalIgnoreCase));
                        if (matchedVar != null) {
                            GUILayout.Label($"<color=#facc15><b>[变量: {matchedVar.Name} = {matchedVar.InitialValue}]</b></color>", labelValueStyle);
                        }
                    }
                } else {
                    GUILayout.Label("<color=#94a3b8>固定时长 (s):</color>", gridLabelStyle, GUILayout.Width(75));
                    step.duration = DrawDelayedNumericField($"fburn_dur_{i}", step.duration, numericInputStyle, GUILayout.Width(80), "F1");
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("🔄 重新同步飞船硬件", secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(20))) {
                    allEngines = ROEngineInterop.ScanVesselEngines(v);
                    allTanks = ROEngineInterop.ScanVesselTanks(v);
                    ScreenMessages.PostScreenMessage("✅ 已实时同步当前飞船发动机与储罐！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();

                // Thruster & Tank Selection Box
                GUILayout.Space(3);
                GUILayout.BeginVertical(nestedCardBoxStyle);

                // Engine / Cluster Selector Row
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>🚀 点火发动机 (支持多发集群):</b>", labelValueStyle, GUILayout.Width(190));
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
                            ScreenMessages.PostScreenMessage($"🚀 点火绑定: {eng.title} (推力: {eng.thrustKN:F0}kN | 比冲: {eng.ispVac:F0}s)", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                    if (step.bound_thrusters.Count > 1) {
                        GUILayout.Label($"<color=#38bdf8><b>[已选 {step.bound_thrusters.Count} 台集群联合点火]</b></color>", labelValueStyle);
                    }
                } else {
                    step.bound_thruster = GUILayout.TextField(string.IsNullOrEmpty(step.bound_thruster) ? "MainEngine" : step.bound_thruster, GUILayout.Width(120));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Tank Selector Row
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>⛽ 供油储罐 (自动精准匹配):</b>", labelValueStyle, GUILayout.Width(190));
                var stateTanks = GMATStateManager.Current.Tanks;
                if (stateTanks.Count > 0) {
                    for (int tk = 0; tk < stateTanks.Count; tk++) {
                        var tnk = stateTanks[tk];
                        string tkId = tnk.Id;
                        bool isCurTank = (step.bound_tank == tkId) || (string.IsNullOrEmpty(step.bound_tank) && tk == 0);
                        if (GUILayout.Button($"⛽ {tkId} (Stage {tnk.StageNumber} - {tnk.FuelMassKg:F0}kg 燃料)", isCurTank ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
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
                GUILayout.Label($"<b>🟢 {engTag}.ThrustDirection1 (切向 / V):</b>", labelValueStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label($"<color=#38bdf8><i>{I18n.T("vector_hint_v")}</i></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                step.vary_dir1_enabled = GUILayout.Toggle(step.vary_dir1_enabled, step.vary_dir1_enabled ? "☑ 参与 Vary 寻优" : "☐ 固定方向", GUILayout.Width(110));
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
                    GUILayout.Label("<color=#64748b><i>[固定值设定，不作为求解变量]</i></color>", labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // N-axis (ThrustDirection2)
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>🟡 {engTag}.ThrustDirection2 (副法向 / N):</b>", labelValueStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label($"<color=#facc15><i>{I18n.T("vector_hint_n")}</i></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                step.vary_dir2_enabled = GUILayout.Toggle(step.vary_dir2_enabled, step.vary_dir2_enabled ? "☑ 参与 Vary 寻优" : "☐ 固定方向", GUILayout.Width(110));
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
                    GUILayout.Label("<color=#64748b><i>[固定值设定，不作为求解变量]</i></color>", labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // B-axis (ThrustDirection3)
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>🟣 {engTag}.ThrustDirection3 (径向 / B):</b>", labelValueStyle, GUILayout.ExpandWidth(false));
                GUILayout.Label($"<color=#f472b6><i>{I18n.T("vector_hint_b")}</i></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                step.vary_dir3_enabled = GUILayout.Toggle(step.vary_dir3_enabled, step.vary_dir3_enabled ? "☑ 参与 Vary 寻优" : "☐ 固定方向", GUILayout.Width(110));
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
                    GUILayout.Label("<color=#64748b><i>[固定值设定，不作为求解变量]</i></color>", labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // GMAT Solved Vector Banner
                if (step.has_solved_value) {
                    GUILayout.Space(3);
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#34d399><b>🎯 GMAT 求解收敛推力矢量: ({step.solved_dir1:F4}, {step.solved_dir2:F4}, {step.solved_dir3:F4})</b></color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("📌 应用收敛解为初猜", actionButtonStyle, GUILayout.Width(130), GUILayout.Height(20))) {
                        step.thrust_dir1 = step.solved_dir1;
                        step.thrust_dir2 = step.solved_dir2;
                        step.thrust_dir3 = step.solved_dir3;
                        inputStringCache[$"tvc_dir1_{i}"] = step.thrust_dir1.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        inputStringCache[$"tvc_dir2_{i}"] = step.thrust_dir2.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        inputStringCache[$"tvc_dir3_{i}"] = step.thrust_dir3.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        ScreenMessages.PostScreenMessage("✅ 已将 GMAT 收敛推力矢量应用为输入初猜！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
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
                            if (GUILayout.Button($"📥 从 Principia #{fbIdx + 1} 实机节点同步推力方向 (切向: {pNode.deltaV.x:F1}, 副法向: {pNode.deltaV.z:F1}, 径向: {pNode.deltaV.y:F1} m/s)", secondaryButtonStyle, GUILayout.Height(20))) {
                                double pMag = Math.Sqrt(pNode.deltaV.x * pNode.deltaV.x + pNode.deltaV.y * pNode.deltaV.y + pNode.deltaV.z * pNode.deltaV.z);
                                if (pMag > 1e-5) {
                                    step.thrust_dir1 = pNode.deltaV.x / pMag;
                                    step.thrust_dir2 = pNode.deltaV.z / pMag;
                                    step.thrust_dir3 = pNode.deltaV.y / pMag;
                                }
                                ScreenMessages.PostScreenMessage($"✅ 成功从 Principia #{fbIdx + 1} 节点同步推力矢量！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
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
                GUILayout.Label("<color=#f59e0b><b>🚀 级段分离 / 抛离死重配置:</b></color>", labelValueStyle, GUILayout.Width(170));

                if (allStages.Count > 0) {
                    for (int s = 0; s < allStages.Count; s++) {
                        var st = allStages[s];
                        bool isCurStage = (step.separated_stage_index == st.stageNumber);
                        GUI.backgroundColor = isCurStage ? new Color(0.9f, 0.6f, 0.2f) : Color.white;
                        if (GUILayout.Button($"Stage {st.stageNumber} (干重: {st.dryMassTonnes * 1000.0:F0}kg)", secondaryButtonStyle, GUILayout.Height(18))) {
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
                GUILayout.Label("抛离级段干重 (kg):", labelValueStyle, GUILayout.Width(120));
                step.separated_mass = DrawDelayedNumericField($"stg_drop_{i}", step.separated_mass, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(75), "F0");

                GUILayout.Label("分离后剩余飞船干重 (kg):", labelValueStyle, GUILayout.Width(160));
                step.remaining_dry_mass = DrawDelayedNumericField($"stg_rem_{i}", step.remaining_dry_mass, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(75), "F0");

                GUILayout.Label("<color=#94a3b8>(将在 GMAT 序列中输出 Sat.DryMass 动态扣减)</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            // =========================================================================
            // 6. EQUATION STEP CARD
            // =========================================================================
            else if (step.type == "Equation") {
                GUILayout.BeginHorizontal();
                GUILayout.Label("赋值变量: Sat.", labelValueStyle, GUILayout.Width(95));
                step.variable = DrawDelayedSmartField($"eq_var_{i}", step.variable, 0.0, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(130), "vary", i);
                GUILayout.Label("=", labelValueStyle, GUILayout.Width(15));
                step.expression = DrawDelayedSmartField($"eq_exp_{i}", step.expression, 0.0, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(110), "prop_val", i);

                if (GUILayout.Button("📋 参数库...", secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(20))) {
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
                GUILayout.Label("<color=#38bdf8><b>🧾 瞬时状态报告 (Report File):</b></color>", labelValueStyle, GUILayout.Width(185));

                GUILayout.Label("<color=#94a3b8>对象名:</color>", gridLabelStyle, GUILayout.Width(45));
                step.subscriber_name = DrawDelayedTextField($"rep_sub_{i}", string.IsNullOrEmpty(step.subscriber_name) ? $"StepReport_{i + 1}" : step.subscriber_name, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110));

                GUILayout.Space(6);
                step.is_expanded = GUILayout.Toggle(step.is_expanded, step.is_expanded ? " ▾ 收起数据明细" : " ▸ 展开详细数据报表", secondaryButtonStyle, GUILayout.Height(20));

                GUILayout.Space(10);
                if (rep != null) {
                    GUILayout.Label($"<color=#34d399><b>● 已就绪</b></color> <color=#94a3b8>T+{rep.elapsed_secs:F1}s | SMA: {rep.sma_km:F1}km | 高度: {rep.altitude_km:F1}km | 质量: {rep.total_mass_kg:F1}kg</color>", labelValueStyle);
                } else {
                    GUILayout.Label("<color=#94a3b8><i>(求解后自动捕获该瞬时所有动力学物理参量)</i></color>", labelValueStyle);
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
                        GUILayout.Label("<color=#facc15><b>🪐 瞬时开普勒轨道六根数 (Keplerian Elements):</b></color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.Space(2);
                        // Row A: SMA, ECC, INC, RAAN
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"半长轴 (SMA): <color=#38bdf8><b>{rep.sma_km:F2} km</b></color>", labelValueStyle, GUILayout.Width(170));
                        GUILayout.Label($"偏心率 (ECC): <color=#38bdf8><b>{rep.ecc:F5}</b></color>", labelValueStyle, GUILayout.Width(150));
                        GUILayout.Label($"倾角 (INC): <color=#38bdf8><b>{rep.inc_deg:F3}°</b></color>", labelValueStyle, GUILayout.Width(140));
                        GUILayout.Label($"升交点赤经 (RAAN): <color=#38bdf8><b>{rep.raan_deg:F3}°</b></color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        // Row B: AOP, TA, Periapsis Alt, Apoapsis Alt
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"近心点幅角 (AOP): <color=#38bdf8><b>{rep.aop_deg:F3}°</b></color>", labelValueStyle, GUILayout.Width(170));
                        GUILayout.Label($"真近点角 (TA): <color=#38bdf8><b>{rep.ta_deg:F3}°</b></color>", labelValueStyle, GUILayout.Width(150));
                        string periStr = rep.periapsis_km > 0 ? $"{rep.periapsis_km:F1} km" : $"{rep.altitude_km:F1} km";
                        string apoStr = rep.apoapsis_km > 0 ? $"{rep.apoapsis_km:F1} km" : $"{rep.altitude_km:F1} km";
                        GUILayout.Label($"近地点高: <color=#34d399><b>{periStr}</b></color>", labelValueStyle, GUILayout.Width(140));
                        GUILayout.Label($"远地点高: <color=#34d399><b>{apoStr}</b></color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();

                        GUILayout.Space(2);

                        // Section 2: Cartesian 3D Vectors & Mass
                        GUILayout.BeginHorizontal();

                        // Col 1: Position & Velocity
                        GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(380));
                        GUILayout.Label("<color=#38bdf8><b>📍 空间三维笛卡尔状态 (MJ2000Eq):</b></color>", labelValueStyle);
                        GUILayout.Label($"位置向量: <b>X={rep.x:F1}, Y={rep.y:F1}, Z={rep.z:F1} km</b>", labelValueStyle);
                        GUILayout.Label($"轨道矢径模长: <color=#34d399><b>|R| = {rep.rmag_km:F2} km</b></color> (距地心)", labelValueStyle);
                        GUILayout.Label($"速度向量: <b>Vx={rep.vx:F4}, Vy={rep.vy:F4}, Vz={rep.vz:F4} km/s</b>", labelValueStyle);
                        double vmag = rep.vmag_km_s > 0 ? rep.vmag_km_s : Math.Sqrt(rep.vx * rep.vx + rep.vy * rep.vy + rep.vz * rep.vz);
                        GUILayout.Label($"空间合速度: <color=#34d399><b>|V| = {vmag:F4} km/s ({vmag * 1000.0:F1} m/s)</b></color>", labelValueStyle);
                        GUILayout.EndVertical();

                        GUILayout.Space(3);

                        // Col 2: Mass, Fuel & Epoch Timing
                        GUILayout.BeginVertical(cardBoxStyle);
                        GUILayout.Label("<color=#a78bfa><b>🚀 航天器质量与任务时间:</b></color>", labelValueStyle);
                        GUILayout.Label($"总质量 (TotalMass): <color=#34d399><b>{rep.total_mass_kg:F1} kg</b></color>", labelValueStyle);
                        if (rep.fuel_mass_kg > 0) {
                            GUILayout.Label($"剩余推进剂 (Fuel): <b>{rep.fuel_mass_kg:F1} kg</b>", labelValueStyle);
                        }
                        GUILayout.Label($"飞行任务耗时: <color=#facc15><b>T+{rep.elapsed_secs:F1} s</b></color> ({rep.elapsed_secs / 60.0:F1} min / {rep.elapsed_secs / 86400.0:F2} d)", labelValueStyle);
                        if (!string.IsNullOrEmpty(rep.epoch_utc)) {
                            GUILayout.Label($"仿真历元: <color=#94a3b8>{rep.epoch_utc}</color>", labelValueStyle);
                        }
                        GUILayout.EndVertical();

                        GUILayout.EndHorizontal();
                    } else {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<color=#facc15>ℹ️ 当前尚未执行求解计算。点击主面板顶部的 <b>[ 🚀 运行求解并生成轨道 ]</b> 即可实时捕获并查看该节点的瞬时全部动力学数据与六根数报表。</color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    // Section 3: Output Variables Customization & Quick Presets
                    GUILayout.Space(3);
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<color=#64748b><b>⚙️ 导出数据项配置 (GMAT Write Fields):</b></color>", labelValueStyle, GUILayout.Width(220));
                    
                    if (GUILayout.Button("🌐 默认全部数据", secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.Earth.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.Earth.RMAG Sat.TotalMass Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ";
                    }
                    if (GUILayout.Button("🪐 仅轨道六根数", secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.Earth.Altitude";
                    }
                    if (GUILayout.Button("📍 仅位置与速度", secondaryButtonStyle, GUILayout.Height(18))) {
                        step.write_fields = "Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ";
                    }
                    if (GUILayout.Button("⛽ 仅质量与耗油", secondaryButtonStyle, GUILayout.Height(18))) {
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
                        GUILayout.Label($"<color=#94a3b8>原始 GMAT 导出文本预览: {rep.raw_report_text.Trim().Split(new char[] { '\n' }).Length} 行数据已同步</color>", labelValueStyle);
                        if (GUILayout.Button("📋 复制原始报表文本", secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(18))) {
                            GUIUtility.systemCopyBuffer = rep.raw_report_text;
                            ScreenMessages.PostScreenMessage("✅ 已复制该节点原始 GMAT 状态报告至剪贴板！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
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
                case "Propagate": return "🌐 轨道积分";
                case "Vary": return "✏️ Vary 寻优";
                case "Achieve": return "🎯 Achieve 目标";
                case "FiniteBurn": return "🔥 有限点火";
                case "BeginFiniteBurn": return "🔥 开启点火";
                case "EndFiniteBurn": return "🛑 结束点火";
                case "ThrustVectorControl":
                case "VaryThrustDirection": return "🧭 推力矢量";
                case "ImpulsiveBurn":
                case "Maneuver": return "💥 脉冲机动";
                case "StageSeparation": return "🚀 级段分离";
                case "Report": return "🧾 状态报告";
                case "Equation": return "📐 变量赋值";
                case "If": return "🔀 If 分支";
                case "Else": return "🔀 Else 分支";
                case "EndIf": return "🔀 EndIf";
                case "While": return "🔁 While 循环";
                case "EndWhile": return "🔁 EndWhile";
                case "For": return "🔁 For 循环";
                case "EndFor": return "🔁 EndFor";
                case "Optimize": return "🧬 优化器";
                case "Minimize": return "📉 极小化";
                case "NonlinearConstraint": return "🚧 约束条件";
                case "EndOptimize": return "🧬 闭合优化";
                case "ScriptEvent": return "📜 脚本事件";
                case "BeginFileThrust": return "📁 开启文件推力";
                case "EndFileThrust": return "🛑 结束文件推力";
                case "Stop": return "⛔ 终止任务";
                case "Breakpoint": return "🔴 调试断点";
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
                        solveStageText = "就绪";
                        solveProgress = 0f;

                        // Instantly synchronize IDE buffer with the loaded script template
                        try {
                            string rawScript = File.ReadAllText(foundPath, System.Text.Encoding.UTF8);
                            ideCustomBuffer = rawScript;
                        } catch {
                            ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                        }

                        ScreenMessages.PostScreenMessage($"✅ 成功从标准模板库导入: {Path.GetFileName(foundPath)} ({res.sequence?.Count ?? 0} 个任务步骤)", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    } else {
                        ScreenMessages.PostScreenMessage($"❌ 解析脚本模板失败: {msg}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                });
            } else {
                ScreenMessages.PostScreenMessage($"❌ 未在模板库中找到文件: {templatePathOrFileName}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
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
                ScreenMessages.PostScreenMessage($"✅ 成功导出标准合规预设模板: {cleanName}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage($"❌ 导出模板失败: {ex.Message}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
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
            ScreenMessages.PostScreenMessage("🧹 已彻底清除历史求解残差与状态缓存！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
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
                GUILayout.Label($"<color=#34d399><b>➔ 🎯 求解值: {sVal} {unitStr}</b></color>", labelValueStyle);
            } else if (step.type == "Achieve" && step.has_achieve_result) {
                GUILayout.Space(8);
                string col = step.achieve_passed ? "#34d399" : "#f87171";
                string icon = step.achieve_passed ? "🎯" : "⚠️";
                GUILayout.Label($"<color={col}><b>➔ {icon} 达成值: {step.achieved_value:F3} (残差: {step.achieved_variance:F4})</b></color>", labelValueStyle);
            } else if ((step.type == "FiniteBurn" || step.type == "ImpulsiveBurn" || step.type == "Maneuver") && (step.dv_v != 0 || step.dv_n != 0 || step.dv_b != 0 || step.has_solved_value)) {
                GUILayout.Space(8);
                double tot = Math.Sqrt(step.dv_v * step.dv_v + step.dv_n * step.dv_n + step.dv_b * step.dv_b);
                if (tot > 0) {
                    GUILayout.Label($"<color=#34d399><b>➔ 🎯 ΔV: {tot:F1} m/s (切向: {step.dv_v:F1}, 法向: {step.dv_n:F1}, 变面: {step.dv_b:F1})</b></color>", labelValueStyle);
                }
            } else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                GUILayout.Space(8);
                if (step.has_solved_value) {
                    GUILayout.Label($"<color=#34d399><b>➔ 🧭 求解矢量: ({step.solved_dir1:F2}, {step.solved_dir2:F2}, {step.solved_dir3:F2})</b></color>", labelValueStyle);
                } else {
                    GUILayout.Label($"<color=#38bdf8><b>➔ 🧭 初始矢量: ({step.thrust_dir1:F2}, {step.thrust_dir2:F2}, {step.thrust_dir3:F2})</b></color>", labelValueStyle);
                }
            } else if (step.type == "StageSeparation") {
                GUILayout.Space(8);
                GUILayout.Label($"<color=#34d399><b>➔ 🚀 抛离: {step.separated_mass:F0} kg</b></color>", labelValueStyle);
            }
        }
    }
}
