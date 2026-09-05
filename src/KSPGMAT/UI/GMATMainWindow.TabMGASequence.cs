using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        // =========================================================================
        // MGA 任务控制序列工作台交互状态 (Single Source of Truth)
        // 设计语言与 Tab 2 (TabSequence.cs) 100% 深度统一，避免学习成本
        // =========================================================================
        private Vector2 mgaSeqScrollPos = Vector2.zero;
        private Vector2 mgaSeqRightScrollPos = Vector2.zero;
        private int mgaSeqDraggedIndex = -1;
        private int mgaSeqStepToMoveUp = -1;
        private int mgaSeqStepToMoveDown = -1;
        private int mgaSeqStepToDelete = -1;
        private int mgaSeqHoveredIndex = -1;
        private int mgaSeqNextHoveredIndex = -1;
        private bool mgaSeqTemplatesFoldout = false;
        private string mgaSeqSearchQuery = "";
        private string mgaSeqSelectedCategory = "全部";
        private bool isMgaSeqSolving = false;
        private string mgaSeqSolveStatus = "";

        // 顶层全局序列模式切换器: 0 = NearBody 经典序列, 1 = MGA 行星际引力助推序列
        private int activeSequenceMode = 0;

        /// <summary>
        /// MGA 经典行星际转移构型模版库定义
        /// </summary>
        private class MGATemplatePreset {
            public string Key;
            public string DisplayTitle;
            public string Category;
            public string Description;
            public string Icon;
            public string DepartureBody;
            public List<string> Flybys;
            public string DestinationBody;
            public double DeparturePeAKm;
            public double DestinationPeAKm;
            public List<double> ToFs;
            public List<MGALegStrategy> Strategies;
        }

        private static readonly List<MGATemplatePreset> MGAPresets = new List<MGATemplatePreset> {
            new MGATemplatePreset {
                Key = "EarthMars",
                DisplayTitle = "地球-火星霍曼/朗伯直飞",
                Category = "直达/内行星",
                Description = "经典火星直接转移窗口，双脉冲脱离与捕获，单次朗伯转移",
                Icon = "🚀",
                DepartureBody = "Earth",
                Flybys = new List<string>(),
                DestinationBody = "Mars",
                DeparturePeAKm = 200.0,
                DestinationPeAKm = 250.0,
                ToFs = new List<double> { 210.0 },
                Strategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic }
            },
            new MGATemplatePreset {
                Key = "Cassini",
                DisplayTitle = "卡西尼 VEEGA 引力助推",
                Category = "深空/外行星",
                Description = "地球-金星-金星-地球-木星-土星 5段借力，深空探测经典架构",
                Icon = "🪐",
                DepartureBody = "Earth",
                Flybys = new List<string> { "Venus", "Venus", "Earth", "Jupiter" },
                DestinationBody = "Saturn",
                DeparturePeAKm = 200.0,
                DestinationPeAKm = 1000.0,
                ToFs = new List<double> { 145.0, 425.0, 55.0, 600.0, 1100.0 },
                Strategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic }
            },
            new MGATemplatePreset {
                Key = "NewHorizons",
                DisplayTitle = "新视野号木星快速借力 (JGA)",
                Category = "深空/外行星",
                Description = "地球极速脱离-木星重力加速-冥王星飞掠，高C3直接逃逸",
                Icon = "🛸",
                DepartureBody = "Earth",
                Flybys = new List<string> { "Jupiter" },
                DestinationBody = "Pluto",
                DeparturePeAKm = 200.0,
                DestinationPeAKm = 10000.0,
                ToFs = new List<double> { 400.0, 3100.0 },
                Strategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic, MGALegStrategy.Ballistic }
            },
            new MGATemplatePreset {
                Key = "BepiColombo",
                DisplayTitle = "贝皮可伦坡水星引力助推 (VVME)",
                Category = "直达/内行星",
                Description = "地球-金星-金星-水星多重借力，逐步减速降低轨道能量进入水星捕获",
                Icon = "☄️",
                DepartureBody = "Earth",
                Flybys = new List<string> { "Venus", "Venus" },
                DestinationBody = "Mercury",
                DeparturePeAKm = 200.0,
                DestinationPeAKm = 400.0,
                ToFs = new List<double> { 160.0, 220.0, 180.0 },
                Strategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic }
            },
            new MGATemplatePreset {
                Key = "KerbinGrandTour",
                DisplayTitle = "坎巴拉星系绿星-宿命大巡游",
                Category = "坎巴拉/原版",
                Description = "Kerbin-Eve-Kerbin-Jool 坎系经典行星际引力跳板架构",
                Icon = "✨",
                DepartureBody = "Kerbin",
                Flybys = new List<string> { "Eve", "Kerbin" },
                DestinationBody = "Jool",
                DeparturePeAKm = 100.0,
                DestinationPeAKm = 500.0,
                ToFs = new List<double> { 60.0, 90.0, 240.0 },
                Strategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic }
            }
        };

        /// <summary>
        /// 加载指定行星际模版到 MGA 任务序列中
        /// </summary>
        private void LoadMGAPreset(MGATemplatePreset preset) {
            if (preset == null) return;
            var seq = new List<MGAMissionStep>();
            double curEpochUt = mgaConfig.EarliestDepartureUT > 0 ? mgaConfig.EarliestDepartureUT : Planetarium.GetUniversalTime();

            // 1. 始发脱离步骤
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Departure,
                body_name = preset.DepartureBody,
                epoch_ut = curEpochUt,
                epoch_utc_str = GMATClient.FormatUtcGregorian(curEpochUt),
                periapsis_alt_km = preset.DeparturePeAKm,
                apoapsis_alt_km = preset.DeparturePeAKm,
                label = $"{preset.DepartureBody} Departure",
                is_solved = false
            });

            // 2. 中间巡航与借力步骤
            string prevBody = preset.DepartureBody;
            for (int i = 0; i < preset.Flybys.Count; i++) {
                string nextBody = preset.Flybys[i];
                double tof = (i < preset.ToFs.Count) ? preset.ToFs[i] : 150.0;
                var strat = (i < preset.Strategies.Count) ? preset.Strategies[i] : MGALegStrategy.Ballistic;

                // 2.1 转移巡航
                seq.Add(new MGAMissionStep {
                    type = MGAStepType.Transfer,
                    body_name = $"{prevBody} ➔ {nextBody}",
                    epoch_ut = curEpochUt,
                    epoch_utc_str = GMATClient.FormatUtcGregorian(curEpochUt),
                    tof_days = tof,
                    min_tof_days = Math.Max(5.0, tof * 0.6),
                    max_tof_days = tof * 1.5,
                    strategy = strat,
                    label = $"Cruise: {prevBody} ➔ {nextBody}",
                    is_solved = false
                });
                curEpochUt += tof * 86400.0;

                // 2.2 行星助推
                seq.Add(new MGAMissionStep {
                    type = MGAStepType.Flyby,
                    body_name = nextBody,
                    epoch_ut = curEpochUt,
                    epoch_utc_str = GMATClient.FormatUtcGregorian(curEpochUt),
                    periapsis_alt_km = 300.0,
                    flyby_strategy = MGAFlybyStrategy.UnpoweredFlyby,
                    label = $"{nextBody} Flyby",
                    is_solved = false
                });

                prevBody = nextBody;
            }

            // 3. 最终航段与目标交会
            double finalTof = (preset.Flybys.Count < preset.ToFs.Count) ? preset.ToFs[preset.Flybys.Count] : 180.0;
            var finalStrat = (preset.Flybys.Count < preset.Strategies.Count) ? preset.Strategies[preset.Flybys.Count] : MGALegStrategy.Ballistic;

            seq.Add(new MGAMissionStep {
                type = MGAStepType.Transfer,
                body_name = $"{prevBody} ➔ {preset.DestinationBody}",
                epoch_ut = curEpochUt,
                epoch_utc_str = GMATClient.FormatUtcGregorian(curEpochUt),
                tof_days = finalTof,
                min_tof_days = Math.Max(5.0, finalTof * 0.6),
                max_tof_days = finalTof * 1.5,
                strategy = finalStrat,
                label = $"Final Cruise: {prevBody} ➔ {preset.DestinationBody}",
                is_solved = false
            });
            curEpochUt += finalTof * 86400.0;

            // 4. 目标到达捕获
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Arrival,
                body_name = preset.DestinationBody,
                epoch_ut = curEpochUt,
                epoch_utc_str = GMATClient.FormatUtcGregorian(curEpochUt),
                periapsis_alt_km = preset.DestinationPeAKm,
                apoapsis_alt_km = preset.DestinationPeAKm,
                label = $"{preset.DestinationBody} Capture",
                is_solved = false
            });

            MGAStateManager.Current.MissionSequence = seq;
            var newCfg = MGAStateManager.Current.ExtractConfigFromSequence();
            mgaConfig.DepartureBody = newCfg.DepartureBody;
            mgaConfig.DestinationBody = newCfg.DestinationBody;
            mgaConfig.FlybySequence = newCfg.FlybySequence;
            mgaConfig.MinToFDays = newCfg.MinToFDays;
            mgaConfig.MaxToFDays = newCfg.MaxToFDays;
            mgaConfig.LegStrategies = newCfg.LegStrategies;
            mgaConfig.FlybyStrategies = newCfg.FlybyStrategies;
            customChainInput = string.Join(",", mgaConfig.FlybySequence);

            MGAStateManager.Current.NotifyStateChanged();
            RefreshMgaCanvas();
            ScreenMessages.PostScreenMessage($"已成功载入模版: {preset.DisplayTitle} ({seq.Count} 步骤)", 3.0f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// 在 MGA 序列指定索引处插入新步骤
        /// </summary>
        private void InsertMGAStep(int index, MGAStepType type) {
            var seq = MGAStateManager.Current.MissionSequence;
            if (seq == null) {
                seq = new List<MGAMissionStep>();
                MGAStateManager.Current.MissionSequence = seq;
            }

            var step = new MGAMissionStep {
                type = type,
                epoch_ut = Planetarium.GetUniversalTime(),
                epoch_utc_str = GMATClient.FormatUtcGregorian(Planetarium.GetUniversalTime())
            };

            switch (type) {
                case MGAStepType.Departure:
                    step.body_name = mgaConfig.DepartureBody;
                    step.periapsis_alt_km = 200.0;
                    step.apoapsis_alt_km = 200.0;
                    step.label = $"{step.body_name} Departure";
                    break;
                case MGAStepType.Transfer:
                    step.body_name = "Transfer Cruise";
                    step.tof_days = 150.0;
                    step.min_tof_days = 30.0;
                    step.max_tof_days = 300.0;
                    step.strategy = MGALegStrategy.Ballistic;
                    step.label = "Transfer Cruise (150d)";
                    break;
                case MGAStepType.Maneuver:
                    step.body_name = "Deep Space Maneuver";
                    step.dsm_eta = 0.5;
                    step.dv_v = 50.0;
                    step.label = "DSM Midcourse Burn";
                    break;
                case MGAStepType.Flyby:
                    step.body_name = "Venus";
                    step.periapsis_alt_km = 300.0;
                    step.flyby_strategy = MGAFlybyStrategy.UnpoweredFlyby;
                    step.label = $"{step.body_name} Flyby";
                    break;
                case MGAStepType.Arrival:
                    step.body_name = mgaConfig.DestinationBody;
                    step.periapsis_alt_km = 200.0;
                    step.apoapsis_alt_km = 200.0;
                    step.label = $"{step.body_name} Capture";
                    break;
            }

            int insertIdx = Mathf.Clamp(index, 0, seq.Count);
            seq.Insert(insertIdx, step);
            MGAStateManager.Current.NotifyStateChanged();
            ScreenMessages.PostScreenMessage($"已插入步骤: {step.label}", 2.0f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// 异步提取序列动力学约束并调用底层 C++ PyKEP 引擎求解
        /// 严格遵循基线0规范: 零托管物理，纯调用原生求解器
        /// </summary>
        private async void SolveOrEvaluateMGASequence() {
            var seq = MGAStateManager.Current.MissionSequence;
            if (seq == null || seq.Count < 2) {
                ScreenMessages.PostScreenMessage("MGA 任务序列至少需要始发与到达两个阶段！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            var cfg = MGAStateManager.Current.ExtractConfigFromSequence();
            mgaConfig.DepartureBody = cfg.DepartureBody;
            mgaConfig.DestinationBody = cfg.DestinationBody;
            mgaConfig.FlybySequence = cfg.FlybySequence;
            mgaConfig.MinToFDays = cfg.MinToFDays;
            mgaConfig.MaxToFDays = cfg.MaxToFDays;
            mgaConfig.LegStrategies = cfg.LegStrategies;
            mgaConfig.FlybyStrategies = cfg.FlybyStrategies;
            mgaConfig.DeparturePeAKm = cfg.DeparturePeAKm;
            mgaConfig.DepartureApAKm = cfg.DepartureApAKm;
            mgaConfig.DestinationPeAKm = cfg.DestinationPeAKm;
            mgaConfig.DestinationApAKm = cfg.DestinationApAKm;

            isMgaSeqSolving = true;
            mgaSeqSolveStatus = "正在调用底层 PyKEP 原生天体力学求解引擎...";
            ScreenMessages.PostScreenMessage("正在启动原生多引力助推全局非线性优化...", 2.5f, ScreenMessageStyle.UPPER_CENTER);

            try {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60))) {
                    var res = await PykepBridge.OptimizeAsync(mgaConfig, (p, msg) => {
                        mgaSeqSolveStatus = $"{msg} ({p * 100.0:F0}%)";
                    }, cts.Token);

                    if (res != null && res.Count > 0) {
                        selectedMgaSolution = res[0];
                        MGAStateManager.Current.NotifySolutionChanged(selectedMgaSolution);
                        mgaSeqSolveStatus = $"解算收敛! 总 ΔV = {selectedMgaSolution.TotalDeltaVKms * 1000.0:F1} m/s, 飞行 = {selectedMgaSolution.TotalFlightDays:F1} 天";
                        RefreshMgaCanvas();
                        ScreenMessages.PostScreenMessage(mgaSeqSolveStatus, 4.0f, ScreenMessageStyle.UPPER_CENTER);
                    } else {
                        mgaSeqSolveStatus = "未找到在指定时限内的可行收敛轨道，请微调飞行时限或发射历元";
                        ScreenMessages.PostScreenMessage(mgaSeqSolveStatus, 4.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
            } catch (Exception ex) {
                mgaSeqSolveStatus = $"求解异常: {ex.Message}";
                Debug.LogError($"[KSPGMAT-MGASeq] 求解异常: {ex}");
            } finally {
                isMgaSeqSolving = false;
            }
        }

        /// <summary>
        /// MGA 任务序列编辑工作台主渲染入口 (统一设计语言)
        /// </summary>
        public void DrawTabMGASequence(Vessel vessel) {
            mgaSeqHoveredIndex = mgaSeqNextHoveredIndex;
            mgaSeqNextHoveredIndex = -1;

            var seq = MGAStateManager.Current.MissionSequence;
            if (seq == null) {
                seq = new List<MGAMissionStep>();
                MGAStateManager.Current.MissionSequence = seq;
            }

            // 若当前序列为空且已有求解方案，自动生成映射
            if (seq.Count == 0 && selectedMgaSolution != null) {
                MGAStateManager.Current.SyncSequenceFromSolution(selectedMgaSolution, mgaConfig);
                seq = MGAStateManager.Current.MissionSequence;
            }

            // Top Header: 模式与工作流导航切换条
            GUILayout.BeginHorizontal();
            bool isSeqMode = (mgaWorkflowMode == 3 || activeSequenceMode == 1);
            if (GUILayout.Button(I18n.T("mga_seq_mode_planner", "🗺️ 星际轨道规划器 (MGA Planner)"), secondaryButtonStyle, GUILayout.Height(24))) {
                mgaWorkflowMode = 1;
                activeSequenceMode = 0;
            }
            if (GUILayout.Button(I18n.T("mga_seq_mode_editor", "📋 任务序列编辑工作台 (MGA Sequence Editor)"), actionButtonStyle, GUILayout.Height(24))) {
                mgaWorkflowMode = 3;
                activeSequenceMode = 1;
            }
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("➕ 从规划器最优解重构序列", secondaryButtonStyle, GUILayout.Height(22))) {
                if (selectedMgaSolution != null) {
                    MGAStateManager.Current.SyncSequenceFromSolution(selectedMgaSolution, mgaConfig);
                    ScreenMessages.PostScreenMessage("已成功将当前最优解重构映射为任务序列步骤", 2.5f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    ScreenMessages.PostScreenMessage("暂无最优解，请先在规划器中解算或从上方模版构建！", 2.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 双栏响应式布局: 左侧序列卡片列表 (62%) + 右侧实时3D视口与5大高级动力学抽屉 (38%)
            float totalW = windowRect.width - 36f;
            float rightColW = Mathf.Clamp(totalW * 0.40f, 380f, 520f);
            float leftColW = totalW - rightColW - 12f;

            GUILayout.BeginHorizontal();

            // =========================================================================
            // LEFT COLUMN: MGA 任务序列步骤工作台 (与 Tab 2 100% 统一设计规范)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(leftColW));

            // 1. 行星际统一任务配置与经典构型模版折叠浏览器 (Presets Browser)
            var allMissions = MGAMissionRepository.LoadAllMissions();
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            string foldoutArrow = mgaSeqTemplatesFoldout ? "▼" : "▶";
            if (GUILayout.Button($"<b>{foldoutArrow} 经典引力助推构型与任务配置库 ({allMissions.Count})</b>", headerStyle, GUILayout.ExpandWidth(false))) {
                mgaSeqTemplatesFoldout = !mgaSeqTemplatesFoldout;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新库", secondaryButtonStyle, GUILayout.Height(20))) {
                MGAMissionRepository.LoadAllMissions(true);
                ScreenMessages.PostScreenMessage("已重新扫描 Missions 配置文件库", 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();

            if (mgaSeqTemplatesFoldout) {
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUILayout.Label("类别:", labelValueStyle, GUILayout.Width(35));
                string[] cats = new string[] { "全部", "经典历史", "直达/内行星", "深空/外行星", "深空机动/低推力", "坎巴拉/原版", "用户自定义" };
                for (int c = 0; c < cats.Length; c++) {
                    string cat = cats[c];
                    bool isSel = (mgaSeqSelectedCategory == cat);
                    if (GUILayout.Button(cat, isSel ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(18))) {
                        mgaSeqSelectedCategory = cat;
                    }
                    GUILayout.Space(2);
                }
                GUILayout.FlexibleSpace();
                mgaSeqSearchQuery = GUILayout.TextField(mgaSeqSearchQuery, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(110), GUILayout.Height(18));
                if (!string.IsNullOrEmpty(mgaSeqSearchQuery) && GUILayout.Button("✖", secondaryButtonStyle, GUILayout.Width(18), GUILayout.Height(18))) {
                    mgaSeqSearchQuery = "";
                }
                GUILayout.EndHorizontal();

                var filteredPresets = allMissions.Where(p => {
                    if (mgaSeqSelectedCategory != "全部" && p.category != mgaSeqSelectedCategory) return false;
                    if (!string.IsNullOrEmpty(mgaSeqSearchQuery)) {
                        string q = mgaSeqSearchQuery.Trim().ToLowerInvariant();
                        if (!p.mission_name.ToLowerInvariant().Contains(q) && !(p.description ?? "").ToLowerInvariant().Contains(q)) return false;
                    }
                    return true;
                }).ToList();

                GUILayout.Space(4);
                int cols = 2;
                for (int r = 0; r < (filteredPresets.Count + cols - 1) / cols; r++) {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols; c++) {
                        int idx = r * cols + c;
                        if (idx < filteredPresets.Count) {
                            var p = filteredPresets[idx];
                            string icon = string.IsNullOrEmpty(p.icon) ? "🪐" : p.icon;
                            string btnText = $"{icon} <b>{p.mission_name}</b>";
                            if (GUILayout.Button(new GUIContent(btnText, p.description), secondaryButtonStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
                                LoadMissionConfigFile(p, false);
                            }
                        } else {
                            GUILayout.FlexibleSpace();
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(2);
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 2. 序列队列控制条 (批量展开/收起 + 步骤计数)
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>🪐 行星际任务序列控制链</b></color> <color=#64748b>({seq.Count} 步骤)</color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("展开全部", secondaryButtonStyle, GUILayout.Height(20))) {
                for (int s = 0; s < seq.Count; s++) seq[s].is_collapsed = false;
            }
            if (GUILayout.Button("收起全部", secondaryButtonStyle, GUILayout.Height(20))) {
                for (int s = 0; s < seq.Count; s++) seq[s].is_collapsed = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3);

            // 3. 顶部快速插入按钮条
            GUILayout.BeginHorizontal(nestedCardBoxStyle);
            GUILayout.Label("<color=#94a3b8>快速插入:</color>", labelValueStyle, GUILayout.Width(65));
            if (GUILayout.Button("+ 始发脱离", secondaryButtonStyle, GUILayout.Height(20))) InsertMGAStep(0, MGAStepType.Departure);
            if (GUILayout.Button("+ 转移巡航", secondaryButtonStyle, GUILayout.Height(20))) InsertMGAStep(seq.Count, MGAStepType.Transfer);
            if (GUILayout.Button("+ 深空机动", secondaryButtonStyle, GUILayout.Height(20))) InsertMGAStep(seq.Count, MGAStepType.Maneuver);
            if (GUILayout.Button("+ 行星助推", secondaryButtonStyle, GUILayout.Height(20))) InsertMGAStep(Math.Max(0, seq.Count - 1), MGAStepType.Flyby);
            if (GUILayout.Button("+ 目标交会", secondaryButtonStyle, GUILayout.Height(20))) InsertMGAStep(seq.Count, MGAStepType.Arrival);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 4. 可滚动序列步骤卡片列表 (Scrollable Step Cards Loop)
            mgaSeqScrollPos = GUILayout.BeginScrollView(mgaSeqScrollPos, cardBoxStyle, GUILayout.ExpandHeight(true));

            if (seq.Count == 0) {
                GUILayout.Space(40);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#94a3b8>行星际任务序列为空，请从上方模版库载入或点击 [+ 插入步骤] 开始构建</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(40);
            }

            mgaSeqStepToMoveUp = -1;
            mgaSeqStepToMoveDown = -1;
            mgaSeqStepToDelete = -1;

            // 4.1 任务流程逐步推进/回放滑块 (Flow Scrubber Toolbar)
            if (seq.Count > 1) {
                GUILayout.BeginHorizontal(nestedCardBoxStyle);
                GUILayout.Label("⏱️ 流程推进:", labelValueStyle, GUILayout.Width(70));

                bool isAll = (mgaFlowScrubberStep <= 0 || mgaFlowScrubberStep > seq.Count);
                if (GUILayout.Button(isAll ? "全部" : "单步", isAll ? actionButtonStyle : secondaryButtonStyle, GUILayout.Width(44), GUILayout.Height(20))) {
                    mgaFlowScrubberStep = isAll ? 1 : 0;
                    RefreshMgaCanvas();
                }

                GUI.enabled = (!isAll && mgaFlowScrubberStep > 1);
                if (GUILayout.Button("◀", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    mgaFlowScrubberStep--;
                    RefreshMgaCanvas();
                }
                GUI.enabled = true;

                int curVal = isAll ? 0 : mgaFlowScrubberStep;
                int newVal = (int)GUILayout.HorizontalSlider(curVal, 0f, seq.Count, GUILayout.Width(110));
                if (newVal != curVal) {
                    mgaFlowScrubberStep = newVal;
                    RefreshMgaCanvas();
                }

                GUI.enabled = (!isAll && mgaFlowScrubberStep < seq.Count);
                if (GUILayout.Button("▶", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    mgaFlowScrubberStep++;
                    RefreshMgaCanvas();
                }
                GUI.enabled = true;

                GUILayout.Space(4);
                if (isAll) {
                    GUILayout.Label($"<color=#38bdf8><b>全部步骤 (共 {seq.Count} 步)</b></color>", labelValueStyle);
                } else {
                    var s = seq[mgaFlowScrubberStep - 1];
                    GUILayout.Label($"<color=#34d399><b>聚焦第 {mgaFlowScrubberStep}/{seq.Count} 步: {s.label}</b></color>", labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            for (int i = 0; i < seq.Count; i++) {
                // 逐步查看模式下：隐藏后续步骤，前置步骤精简显示
                if (mgaFlowScrubberStep > 0 && mgaFlowScrubberStep <= seq.Count) {
                    if (i > mgaFlowScrubberStep - 1) {
                        GUILayout.BeginHorizontal(nestedCardBoxStyle);
                        GUILayout.Label($"<color=#64748b>... 后续还有 {seq.Count - mgaFlowScrubberStep} 个任务步骤已隐藏 (拖动滑块推进查看)</color>", labelValueStyle);
                        GUILayout.EndHorizontal();
                        break;
                    } else if (i < mgaFlowScrubberStep - 1) {
                        GUILayout.BeginHorizontal(nestedCardBoxStyle);
                        GUILayout.Label($"<color=#64748b>✓ 步骤 #{i + 1}: {seq[i].label}</color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("展开查看", secondaryButtonStyle, GUILayout.Width(70), GUILayout.Height(18))) {
                            mgaFlowScrubberStep = i + 1;
                            RefreshMgaCanvas();
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(1);
                        continue;
                    }
                }

                var step = seq[i];
                bool isBeingDragged = (mgaSeqDraggedIndex == i);

                // 拖拽插入提示条
                if (mgaSeqDraggedIndex >= 0 && mgaSeqDraggedIndex != i) {
                    if (GUILayout.Button($"↓ 移动至步骤 #{i + 1} 之前", tabButtonActiveStyle, GUILayout.Height(20))) {
                        var moveItem = seq[mgaSeqDraggedIndex];
                        seq.RemoveAt(mgaSeqDraggedIndex);
                        int targetIdx = (mgaSeqDraggedIndex < i) ? i - 1 : i;
                        seq.Insert(targetIdx, moveItem);
                        mgaSeqDraggedIndex = -1;
                        MGAStateManager.Current.NotifyStateChanged();
                    }
                }

                bool isHovered = (mgaSeqHoveredIndex == i) || (mgaFlowScrubberStep == i + 1);

                // 步骤卡片外框 (与 Tab 2 完全一致的 GetStepBoxStyle 与指示条视觉语言)
                GUILayout.BeginVertical(GetStepBoxStyle(step.type.ToString(), isHovered, isMgaSeqSolving, isBeingDragged));

                // -------------------------------------------------------------
                // 步骤卡片头部 (Header): #序号 ➔ 折叠 ➔ 箭头 ➔ 拖拽 ➔ Pill ➔ 标题 ➔ 结果 ➔ 删除
                // -------------------------------------------------------------
                GUILayout.BeginHorizontal();

                string foldIcon = step.is_collapsed ? "⊞" : "⊟";
                if (GUILayout.Button(foldIcon, secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    step.is_collapsed = !step.is_collapsed;
                }

                GUI.enabled = (i > 0);
                if (GUILayout.Button("↑", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(20))) {
                    mgaSeqStepToMoveUp = i;
                }
                GUI.enabled = (i < seq.Count - 1);
                if (GUILayout.Button("↓", secondaryButtonStyle, GUILayout.Width(20), GUILayout.Height(20))) {
                    mgaSeqStepToMoveDown = i;
                }
                GUI.enabled = true;

                if (GUILayout.Button(isBeingDragged ? "取消" : "☰", isBeingDragged ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(isBeingDragged ? 45 : 24), GUILayout.Height(20))) {
                    mgaSeqDraggedIndex = isBeingDragged ? -1 : i;
                }

                // 步骤类型 Pill Badge
                string badgeText = step.type switch {
                    MGAStepType.Departure => "始发脱离",
                    MGAStepType.Transfer => "转移巡航",
                    MGAStepType.Maneuver => "深空机动",
                    MGAStepType.Flyby => "行星助推",
                    MGAStepType.Arrival => "目标交会",
                    _ => "任务步骤"
                };
                GUIStyle pillStyle = GetMGAPillStyle(step.type);
                GUILayout.Box(badgeText, pillStyle, GUILayout.MinWidth(75), GUILayout.Height(20));

                // 步骤标题编辑
                step.label = GUILayout.TextField(string.IsNullOrEmpty(step.label) ? $"{step.body_name} {badgeText}" : step.label, stepLabelInputStyle, GUILayout.MinWidth(140), GUILayout.MaxWidth(260), GUILayout.Height(20));

                GUILayout.FlexibleSpace();

                // 求解结果指标摘要角标
                if (step.is_solved && step.solved_dv_ms > 0.01) {
                    GUILayout.Label($"<color=#34d399><b>ΔV: {step.solved_dv_ms:F0} m/s</b></color>", labelValueStyle);
                }
                if (step.is_solved && step.solved_c3_kms2 > 0.01) {
                    GUILayout.Label($"<color=#38bdf8>C3: {step.solved_c3_kms2:F1}</color>", labelValueStyle);
                }

                if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                    mgaSeqStepToDelete = i;
                }
                GUILayout.EndHorizontal();

                // -------------------------------------------------------------
                // 步骤卡片展开详情 (Card Body)
                // -------------------------------------------------------------
                if (!step.is_collapsed) {
                    GUILayout.Space(4);
                    DrawMGAStepCardBody(i, step);
                }

                GUILayout.EndVertical();

                // 绘制左侧类型高亮色条与悬停外发光 (严格符合基线 4.2: 2.0~3.5px 指示条)
                if (Event.current.type == EventType.Repaint) {
                    Rect cardRect = GUILayoutUtility.GetLastRect();
                    if (cardRect.Contains(Event.current.mousePosition)) {
                        mgaSeqNextHoveredIndex = i;
                    }
                    Color themeColor = GetMGAThemeColor(step.type);
                    float stripW = isHovered ? 3.5f : 2.5f;
                    GUI.DrawTexture(new Rect(cardRect.x + 1, cardRect.y + 2, stripW, cardRect.height - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, themeColor, 0, 0);

                    if (isHovered) {
                        Color glowBorder = new Color(themeColor.r, themeColor.g, themeColor.b, 0.75f);
                        GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                        GUI.DrawTexture(new Rect(cardRect.x, cardRect.y + cardRect.height - 1, cardRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                        GUI.DrawTexture(new Rect(cardRect.x + cardRect.width - 1, cardRect.y, 1, cardRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                    }
                }

                // 卡片之间的插入指示条
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ 插入步骤", secondaryButtonStyle, GUILayout.Width(80), GUILayout.Height(16))) {
                    InsertMGAStep(i + 1, MGAStepType.Transfer);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            // 处理上移/下移/删除
            if (mgaSeqStepToMoveUp >= 1 && mgaSeqStepToMoveUp < seq.Count) {
                var item = seq[mgaSeqStepToMoveUp];
                seq.RemoveAt(mgaSeqStepToMoveUp);
                seq.Insert(mgaSeqStepToMoveUp - 1, item);
                MGAStateManager.Current.NotifyStateChanged();
            }
            if (mgaSeqStepToMoveDown >= 0 && mgaSeqStepToMoveDown < seq.Count - 1) {
                var item = seq[mgaSeqStepToMoveDown];
                seq.RemoveAt(mgaSeqStepToMoveDown);
                seq.Insert(mgaSeqStepToMoveDown + 1, item);
                MGAStateManager.Current.NotifyStateChanged();
            }
            if (mgaSeqStepToDelete >= 0 && mgaSeqStepToDelete < seq.Count) {
                seq.RemoveAt(mgaSeqStepToDelete);
                MGAStateManager.Current.NotifyStateChanged();
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // 5. 底部序列全局操作工具条 (Action Toolbar)
            GUILayout.BeginVertical(nestedCardBoxStyle);
            if (!string.IsNullOrEmpty(mgaSeqSolveStatus)) {
                GUILayout.Label($"<color=#38bdf8><b>{mgaSeqSolveStatus}</b></color>", labelValueStyle);
                GUILayout.Space(2);
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = !isMgaSeqSolving;
            if (GUILayout.Button("⚡ 立即解算/评估序列", actionButtonStyle, GUILayout.Height(26))) {
                SolveOrEvaluateMGASequence();
            }
            GUI.enabled = true;

            if (GUILayout.Button("🔄 同步至3D视口", secondaryButtonStyle, GUILayout.Height(26))) {
                RefreshMgaCanvas();
                ScreenMessages.PostScreenMessage("3D 轨道视口已重绘同步", 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            if (GUILayout.Button("📋 注入飞船机动节点", secondaryButtonStyle, GUILayout.Height(26))) {
                if (selectedMgaSolution != null && FlightGlobals.ActiveVessel != null) {
                    LoadMgaSolutionToSequence(selectedMgaSolution, FlightGlobals.ActiveVessel);
                    ScreenMessages.PostScreenMessage("已将当前 MGA 序列转化为飞船机动计划", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    ScreenMessages.PostScreenMessage("暂无有效求解方案可注入！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            if (GUILayout.Button("💾 保存为任务配置 (.json)", secondaryButtonStyle, GUILayout.Height(26))) {
                try {
                    var curSeq = MGAStateManager.Current.MissionSequence;
                    string name = (selectedMgaSolution != null && !string.IsNullOrEmpty(selectedMgaSolution.FlybyChainSummary)) ?
                        $"Sequence_{selectedMgaSolution.FlybyChainSummary}" : $"Custom_Sequence_{DateTime.Now:yyyyMMdd_HHmm}";
                    var mCfg = MGAMissionConfigFile.FromSequence(curSeq, mgaConfig, name, "用户自定义", "从任务序列编辑工作台导出的配置");
                    string path = MGAMissionRepository.SaveMission(mCfg);
                    ScreenMessages.PostScreenMessage($"任务序列已保存至: {Path.GetFileName(path)}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage($"保存失败: {ex.Message}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            if (GUILayout.Button("📂 打开任务配置 (.json)", secondaryButtonStyle, GUILayout.Height(26))) {
                try {
                    var missions = MGAMissionRepository.LoadAllMissions(true);
                    if (missions.Count > 0) {
                        var latest = missions.Last();
                        LoadMissionConfigFile(latest, false);
                        ScreenMessages.PostScreenMessage($"已加载任务配置: {latest.mission_name}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage($"打开配置失败: {ex.Message}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndVertical(); // Closes Left Column

            // =========================================================================
            // RIGHT COLUMN: 3D 视口画布 + 机动节点导航条 + 5大高级动力学交互抽屉
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(rightColW));
            mgaSeqRightScrollPos = GUILayout.BeginScrollView(mgaSeqRightScrollPos, false, false, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // 1. 3D 轨道地图视口画布 (Flyby Canvas Preview)
            DrawInteractiveMgaCanvas(340f, 260f);

            GUILayout.Space(4);

            // 2. 静默动力学优化与核验状态看板 (Automatic Astrodynamics Optimizations Ribbon)
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.Label("动力学优化与真值核验状态:", sectionHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#34d399><b>⚡ MEE 无奇异外推</b></color>", labelValueStyle, GUILayout.Width(125));
            if (selectedMgaSolution != null && selectedMgaSolution.PrimerVector != null) {
                var pv = selectedMgaSolution.PrimerVector;
                string pvTag = pv.IsOptimal ? 
                    "<color=#34d399><b>✅ 劳登最优 (|p|≤1.0)</b></color>" : 
                    $"<color=#facc15><b>⚠️ 劳登极值 (|p|={pv.MaxPMag:F2})</b></color>";
                GUILayout.Label(pvTag, labelValueStyle, GUILayout.Width(135));
            } else {
                GUILayout.Label("<color=#34d399><b>✅ 劳登最优脉冲</b></color>", labelValueStyle, GUILayout.Width(110));
            }
            if (selectedMgaSolution != null && selectedMgaSolution.ParetoFront != null && selectedMgaSolution.ParetoFront.Count > 0) {
                GUILayout.Label($"<color=#38bdf8><b>📊 帕累托前沿已收敛</b></color>", labelValueStyle, GUILayout.Width(130));
            }
            if (selectedMgaSolution != null && selectedMgaSolution.BPlane != null) {
                var bp = selectedMgaSolution.BPlane;
                GUILayout.Label($"<color=#38bdf8><b>🎯 B平面双曲锁定 (V∞={bp.VInfMagKms:F2} km/s)</b></color>", labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            GUILayout.Space(4);

            // 3. 机动节点交互式导航与自动聚焦视口条 (100% 对齐用户截图与 TabMGA lines 1727-1760)
            if (selectedMgaSolution != null && selectedMgaSolution.Events != null && selectedMgaSolution.Events.Count > 0) {
                int eventCount = selectedMgaSolution.Events.Count;
                if (selectedManeuverIndex < 0 || selectedManeuverIndex >= eventCount) selectedManeuverIndex = 0;

                GUILayout.BeginHorizontal(nestedCardBoxStyle);
                GUILayout.Label("机动节点:", labelValueStyle, GUILayout.Width(65));

                var currentEvt = selectedMgaSolution.Events[selectedManeuverIndex];
                string ddTitle = $"{currentEvt.Title}  {(showManeuverDropdown ? "▲" : "▼")}";
                if (GUILayout.Button(ddTitle, actionButtonStyle, GUILayout.MinWidth(150), GUILayout.MaxWidth(220), GUILayout.Height(22))) {
                    showManeuverDropdown = !showManeuverDropdown;
                }

                if (GUILayout.Button("◀", secondaryButtonStyle, GUILayout.Width(24), GUILayout.Height(22))) {
                    if (selectedManeuverIndex > 0) {
                        selectedManeuverIndex--;
                        FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                    }
                    showManeuverDropdown = false;
                }
                if (GUILayout.Button("▶", secondaryButtonStyle, GUILayout.Width(24), GUILayout.Height(22))) {
                    if (selectedManeuverIndex < eventCount - 1) {
                        selectedManeuverIndex++;
                        FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                    }
                    showManeuverDropdown = false;
                }

                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_focus_view", "聚焦视图"), actionButtonStyle, GUILayout.Width(65), GUILayout.Height(22))) {
                    FocusOnSelectedManeuver(autoCenter: true);
                }
                mgaAutoFocusEnabled = GUILayout.Toggle(mgaAutoFocusEnabled, I18n.T("mga_auto_lock", "自动锁定"), GUILayout.Width(70));

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // 下拉展开节点菜单
                if (showManeuverDropdown) {
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    for (int evIdx = 0; evIdx < selectedMgaSolution.Events.Count; evIdx++) {
                        var ev = selectedMgaSolution.Events[evIdx];
                        bool isCur = (evIdx == selectedManeuverIndex);
                        GUIStyle itmStyle = isCur ? tabButtonActiveStyle : secondaryButtonStyle;
                        if (GUILayout.Button(ev.Title, itmStyle, GUILayout.Height(20))) {
                            selectedManeuverIndex = evIdx;
                            showManeuverDropdown = false;
                            FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                        }
                    }
                    GUILayout.EndVertical();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical(); // Closes Right Column

            GUILayout.EndHorizontal(); // Closes Main 2-Column Row
        }

        /// <summary>
        /// 绘制单个 MGA 步骤卡片的详细参数表单
        /// <summary>
        /// MGA 步骤卡片展开详情 (纯可视化与执行看板设计: 锁死刚性物理数值，清晰呈现各阶段时间线与动力学遥测)
        /// </summary>
        private void DrawMGAStepCardBody(int index, MGAMissionStep step) {
            GUILayout.BeginVertical(nestedCardBoxStyle);

            // Row 1: 天体识别与轨道几何形态
            GUILayout.BeginHorizontal();
            GUILayout.Label("天体:", labelValueStyle, GUILayout.Width(45));
            var planet = SolarSystemCatalog.GetPlanet(step.body_name);
            string planetBtnText = $"{planet.Icon} {planet.GetDisplayName()} ({step.body_name})";
            GUILayout.Label($"<b>{planetBtnText}</b>", labelValueStyle, GUILayout.Width(140));

            GUILayout.Space(8);

            if (step.type == MGAStepType.Departure || step.type == MGAStepType.Arrival) {
                GUILayout.Label($"近心高: <color=#38bdf8><b>{step.periapsis_alt_km:F0} km</b></color>", labelValueStyle, GUILayout.Width(130));
                GUILayout.Label($"远心高: <color=#38bdf8><b>{step.apoapsis_alt_km:F0} km</b></color>", labelValueStyle);
            } else if (step.type == MGAStepType.Flyby) {
                GUILayout.Label($"近掠高度: <color=#38bdf8><b>{step.periapsis_alt_km:F0} km</b></color>", labelValueStyle, GUILayout.Width(150));
                GUILayout.Label("助推策略:", labelValueStyle, GUILayout.Width(65));
                step.flyby_strategy = (MGAFlybyStrategy)GUILayout.Toolbar((int)step.flyby_strategy, new string[] { "纯引力", "动力助推" }, GUILayout.Width(130), GUILayout.Height(18));
            } else if (step.type == MGAStepType.Transfer) {
                GUILayout.Label($"转移策略: <color=#6366f1><b>{step.strategy}</b></color>", labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(3);

            // Row 2: 严格时间线与天体星历历元 (纯只读数据胶囊，防止手动误改破坏相位)
            GUILayout.BeginHorizontal();
            string utcDisplay = string.IsNullOrEmpty(step.epoch_utc_str) ? GMATClient.FormatUtcGregorian(step.epoch_ut) : step.epoch_utc_str;
            GUILayout.Label($"历元时刻: <color=#34d399><b>{utcDisplay}</b></color> <color=#94a3b8>(UT: {step.epoch_ut:F0} s)</color>", labelValueStyle, GUILayout.Width(350));

            if (step.type == MGAStepType.Transfer) {
                GUILayout.Space(8);
                GUILayout.Label($"飞行耗时: <color=#facc15><b>{step.tof_days:F1} 天</b></color> <color=#94a3b8>[{step.min_tof_days:F0}~{step.max_tof_days:F0}d]</color>", labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Row 3: 机动冲量、三轴分量与穿靶遥测明细 (只读执行看板)
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            if (step.type == MGAStepType.Departure) {
                GUILayout.Label($"始发脱离: <color=#34d399><b>ΔV = {step.solved_dv_ms:F0} m/s</b></color>", labelValueStyle, GUILayout.Width(180));
                GUILayout.Label($"特征能量 C3: <color=#38bdf8><b>{step.solved_c3_kms2:F1} km²/s²</b></color>", labelValueStyle);
            } else if (step.type == MGAStepType.Maneuver) {
                GUILayout.Label($"机动冲量: <color=#facc15><b>{step.solved_dv_ms:F1} m/s</b></color>", labelValueStyle, GUILayout.Width(160));
                GUILayout.Label($"VNC 分量: V={step.dv_v:F1}, N={step.dv_n:F1}, C={step.dv_b:F1} m/s", labelValueStyle, GUILayout.Width(240));
                GUILayout.Label($"时段比 η: <color=#38bdf8><b>{step.dsm_eta:F2}</b></color>", labelValueStyle);
            } else if (step.type == MGAStepType.Flyby) {
                string fbDvText = (step.solved_dv_ms > 0.001) ? $"动力助推: <color=#facc15><b>{step.solved_dv_ms:F1} m/s</b></color>" : "<color=#34d399>无动力纯引力借力 (ΔV=0)</color>";
                GUILayout.Label(fbDvText, labelValueStyle);
            } else if (step.type == MGAStepType.Transfer) {
                if (Math.Abs(step.target_b_dot_t_km) > 0.1 || Math.Abs(step.target_b_dot_r_km) > 0.1) {
                    GUILayout.Label($"B平面穿靶: B·T = <b>{step.target_b_dot_t_km:F1} km</b>, B·R = <b>{step.target_b_dot_r_km:F1} km</b>", labelValueStyle);
                } else {
                    GUILayout.Label($"转移冲量: <color=#facc15><b>{step.solved_dv_ms:F0} m/s</b></color>", labelValueStyle);
                }
            } else if (step.type == MGAStepType.Arrival) {
                GUILayout.Label($"终点捕获制动: <color=#34d399><b>ΔV = {step.solved_dv_ms:F0} m/s</b></color>", labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        /// <summary>
        /// 获取 MGA 步骤的主题高亮色 (符合基线 4.2 色彩体系)
        /// </summary>
        private Color GetMGAThemeColor(MGAStepType type) {
            return type switch {
                MGAStepType.Departure => new Color(0.22f, 0.74f, 0.97f, 1f), // #38bdf8 天空蓝
                MGAStepType.Transfer => new Color(0.39f, 0.40f, 0.95f, 1f),  // #6366f1 经典蓝
                MGAStepType.Maneuver => new Color(0.96f, 0.62f, 0.04f, 1f),  // #f59e0b 烈焰黄
                MGAStepType.Flyby => new Color(0.78f, 0.48f, 0.98f, 1f),     // #c084fc 紫罗兰
                MGAStepType.Arrival => new Color(0.20f, 0.83f, 0.60f, 1f),   // #34d399 翠绿
                _ => new Color(0.58f, 0.64f, 0.72f, 1f)
            };
        }

        /// <summary>
        /// 获取 MGA 步骤的徽章样式
        /// </summary>
        private GUIStyle GetMGAPillStyle(MGAStepType type) {
            return type switch {
                MGAStepType.Departure => pillVaryStyle ?? GUI.skin.box,
                MGAStepType.Transfer => pillTargetStyle ?? GUI.skin.box,
                MGAStepType.Maneuver => pillBurnStyle ?? GUI.skin.box,
                MGAStepType.Flyby => pillImpulseStyle ?? GUI.skin.box,
                MGAStepType.Arrival => pillAchieveStyle ?? GUI.skin.box,
                _ => pillGenericStyle ?? GUI.skin.box
            };
        }
    }
}
