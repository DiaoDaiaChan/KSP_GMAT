using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using KSPGMAT.MGA;
using KSPGMAT.Service;
using KSPGMAT.Model;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        // MGA UI State (100% 对齐 KSP-MGA-Planner 拓扑与纯正中文本地化)
        // MGA 全局统一状态源 (Single Source of Truth)
        private MGAConfig mgaConfig => MGAStateManager.Current.Config;

        private List<List<string>> generatedCandidateChains { get => MGAStateManager.Current.GeneratedCandidateChains; set => MGAStateManager.Current.GeneratedCandidateChains = value; }
        private int selectedCandidateChainIndex { get => MGAStateManager.Current.SelectedCandidateChainIndex; set => MGAStateManager.Current.SelectedCandidateChainIndex = value; }
        private string customChainInput { get => MGAStateManager.Current.CustomChainInput; set => MGAStateManager.Current.CustomChainInput = value; }
        private List<MGASolution> mgaSolutions { get => MGAStateManager.Current.Solutions; set => MGAStateManager.Current.Solutions = value; }
        private MGASolution selectedMgaSolution { get => MGAStateManager.Current.ActiveSolution; set => MGAStateManager.Current.ActiveSolution = value; }
        private int selectedManeuverIndex { get => MGAStateManager.Current.SelectedManeuverIndex; set => MGAStateManager.Current.SelectedManeuverIndex = value; }
        private bool showManeuverDropdown { get => MGAStateManager.Current.ShowManeuverDropdown; set => MGAStateManager.Current.ShowManeuverDropdown = value; }
        private int mgaDetailViewMode { get => MGAStateManager.Current.DetailViewMode; set => MGAStateManager.Current.DetailViewMode = value; }

        // Orbit Map Canvas State
        private Texture2D mgaCanvasTexture = null;
        private float mgaCanvasZoom { get => MGAStateManager.Current.Viewport.CanvasZoom; set => MGAStateManager.Current.Viewport.CanvasZoom = value; }
        private Vector2 mgaCanvasPan { get => MGAStateManager.Current.Viewport.CanvasPan; set => MGAStateManager.Current.Viewport.CanvasPan = value; }
        private bool mgaShowSois { get => MGAStateManager.Current.Viewport.ShowSois; set => MGAStateManager.Current.Viewport.ShowSois = value; }
        private bool mgaShowMetDates { get => MGAStateManager.Current.Viewport.ShowMET; set => MGAStateManager.Current.Viewport.ShowMET = value; }
        private bool isDraggingCanvas = false;
        private Vector2 lastMouseCanvasPos = Vector2.zero;

        // Async Search State (SSOT 托管在 MGAStateManager)
        private bool isMgaOptimizing { get => MGAStateManager.Current.IsOptimizing; set => MGAStateManager.Current.IsOptimizing = value; }
        private float mgaProgress { get => MGAStateManager.Current.OptimizeProgress; set => MGAStateManager.Current.OptimizeProgress = value; }
        private string mgaStatusText { get => MGAStateManager.Current.OptimizeStatusText; set => MGAStateManager.Current.OptimizeStatusText = value; }
        private CancellationTokenSource mgaCts = null;
        private GUIStyle cachedReticleStyle = null;
        private Vector2 mgaLeftScroll = Vector2.zero;
        private Vector2 mgaTabScroll = Vector2.zero;
        private Vector2 mgaSolutionScroll = Vector2.zero;
        private string mgaDepDateInput { get => MGAStateManager.Current.DepDateInput; set => MGAStateManager.Current.DepDateInput = value; }
        private string mgaEndDateInput { get => MGAStateManager.Current.EndDateInput; set => MGAStateManager.Current.EndDateInput = value; }
        private bool mgaDatesInitialized { get => MGAStateManager.Current.DatesInitialized; set => MGAStateManager.Current.DatesInitialized = value; }
        private int mgaWorkflowMode { get => MGAStateManager.Current.WorkflowMode; set => MGAStateManager.Current.WorkflowMode = value; } // 0 = Trajectory Planner, 2 = Sequence Workbench
        private bool showChainSuggestions = false;

        // Pareto Front Tradeoff Slider State (SSOT 托管在 MGAStateManager)
        private float mgaParetoSliderVal { get => MGAStateManager.Current.ParetoSliderVal; set => MGAStateManager.Current.ParetoSliderVal = value; }
        public int GetMgaTotalSteps() {
            var seq = MGAStateManager.Current.MissionSequence;
            if (seq != null && seq.Count > 1) return seq.Count;
            if (selectedMgaSolution != null && selectedMgaSolution.Legs != null && selectedMgaSolution.Legs.Count > 0) {
                return selectedMgaSolution.Legs.Count;
            }
            return 1;
        }

        public float MapStepProgressTo3DFlow(float stepProgress) {
            if (stepProgress <= 0.001f) return 0f;
            int totalLegs = (selectedMgaSolution != null && selectedMgaSolution.Legs != null) ? selectedMgaSolution.Legs.Count : 1;
            var seq = MGAStateManager.Current.MissionSequence;
            if (seq != null && seq.Count > 1 && totalLegs > 0) {
                float frac = Mathf.Clamp01((stepProgress - 1.0f) / (float)(seq.Count - 1));
                return 0.001f + frac * (totalLegs - 0.001f);
            }
            return Mathf.Clamp(stepProgress, 0f, (float)totalLegs);
        }

        private int mgaFlowScrubberStep { 
            get => MGAStateManager.Current.Viewport.ScrubberStep; 
            set {
                MGAStateManager.Current.Viewport.ScrubberStep = value;
                MGAStateManager.Current.Viewport.TargetProgress = (value <= 0) ? 0f : (float)value;
            }
        }
        private float mgaFlowProgress { 
            get => MGAStateManager.Current.Viewport.ScrubberProgress; 
            set {
                MGAStateManager.Current.Viewport.ScrubberProgress = value;
                if (value <= 0.001f) {
                    MGAStateManager.Current.Viewport.ScrubberStep = 0;
                } else {
                    int total = GetMgaTotalSteps();
                    MGAStateManager.Current.Viewport.ScrubberStep = Mathf.Clamp(Mathf.CeilToInt(value), 1, total);
                }
            }
        }
        private float mgaTargetProgress { 
            get => MGAStateManager.Current.Viewport.TargetProgress; 
            set => MGAStateManager.Current.Viewport.TargetProgress = value; 
        }
        private bool mgaIsPlaying { 
            get => MGAStateManager.Current.Viewport.IsPlayingAnimation; 
            set => MGAStateManager.Current.Viewport.IsPlayingAnimation = value; 
        }
        private float mgaPlaySpeed { 
            get => MGAStateManager.Current.Viewport.PlaySpeed <= 0f ? 1.0f : MGAStateManager.Current.Viewport.PlaySpeed; 
            set => MGAStateManager.Current.Viewport.PlaySpeed = value; 
        }


        // Porkchop Plot Engine State (SSOT 托管在 MGAStateManager)
        private bool showPorkchopModal { get => MGAStateManager.Current.ShowPorkchopModal; set => MGAStateManager.Current.ShowPorkchopModal = value; }
        private PorkchopGrid currentPorkchop { get => MGAStateManager.Current.CurrentPorkchop; set => MGAStateManager.Current.CurrentPorkchop = value; }
        private bool isComputingPorkchop { get => MGAStateManager.Current.IsComputingPorkchop; set => MGAStateManager.Current.IsComputingPorkchop = value; }
        private string porkchopStatusText { get => MGAStateManager.Current.PorkchopStatusText; set => MGAStateManager.Current.PorkchopStatusText = value; }
        private Vector2 lastPorkchopHoverPos { get => MGAStateManager.Current.LastPorkchopHoverPos; set => MGAStateManager.Current.LastPorkchopHoverPos = value; }
        private bool isHoveringPorkchop { get => MGAStateManager.Current.IsHoveringPorkchop; set => MGAStateManager.Current.IsHoveringPorkchop = value; }

        // Historical Benchmark Test Suite State (SSOT 托管在 MGAStateManager)
        private bool showBenchmarkModal { get => MGAStateManager.Current.ShowBenchmarkModal; set => MGAStateManager.Current.ShowBenchmarkModal = value; }
        private List<MGABenchmarkSuite.BenchmarkResult> benchmarkResults { get => MGAStateManager.Current.BenchmarkResults; set => MGAStateManager.Current.BenchmarkResults = value; }
        private string benchmarkSummaryText { get => MGAStateManager.Current.BenchmarkSummaryText; set => MGAStateManager.Current.BenchmarkSummaryText = value; }
        private int selectedBenchmarkIndex { get => MGAStateManager.Current.SelectedBenchmarkIndex; set => MGAStateManager.Current.SelectedBenchmarkIndex = value; }
        private Vector2 benchmarkReportScroll { get => MGAStateManager.Current.BenchmarkReportScroll; set => MGAStateManager.Current.BenchmarkReportScroll = value; }

        // Ephemeris Scheme Comparison State (SSOT 托管在 MGAStateManager)
        private bool showSchemeComparisonPanel { get => MGAStateManager.Current.ShowSchemeComparisonPanel; set => MGAStateManager.Current.ShowSchemeComparisonPanel = value; }
        private string schemeComparisonBody { get => MGAStateManager.Current.SchemeComparisonBody; set => MGAStateManager.Current.SchemeComparisonBody = value; }
        private SchemeComparisonResult currentSchemeComparison { get => MGAStateManager.Current.CurrentSchemeComparison; set => MGAStateManager.Current.CurrentSchemeComparison = value; }

        private void RefreshSchemeComparison() {
            if (string.IsNullOrEmpty(schemeComparisonBody)) schemeComparisonBody = mgaConfig.DepartureBody;
            double t = mgaConfig.EarliestDepartureUT > 0 ? mgaConfig.EarliestDepartureUT : EphemerisTimeSystem.J2000_UT;
            currentSchemeComparison = SolarSystemCatalog.ComparePlanetSchemes(schemeComparisonBody, t);
        }


        /// <summary>
        /// 每次进入 MGA 界面自动将发射搜索窗口与停泊参数同步为游戏当前真实时刻 (如 1951 年) 与当前活跃飞船状态
        /// </summary>
        public void SyncMgaWithCurrentGameTime(bool force = false) {
            if (!force && mgaDatesInitialized) return;

            double nowUt = PrincipiaInterop.IsPrincipiaAvailable() 
                ? PrincipiaInterop.GetCurrentPrincipiaTime() 
                : Planetarium.GetUniversalTime();
            if (nowUt <= 0) nowUt = Planetarium.GetUniversalTime();

            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null) {
                // 1. 同步出发引力天体 (仅限 Sol / RSS 太阳系真实天体)
                if (v.mainBody != null && !string.IsNullOrEmpty(v.mainBody.bodyName)) {
                    string bName = v.mainBody.bodyName;
                    if (SolarSystemCatalog.Planets.ContainsKey(bName)) {
                        mgaConfig.DepartureBody = bName;
                    }
                }

                // 2. 高精计算当前飞船停泊轨道高度与初始倾角
                if (v.orbit != null && v.mainBody != null) {
                    double peA = v.orbit.PeA / 1000.0;
                    double apA = v.orbit.ApA / 1000.0;
                    double meanAltKm = (peA + apA) * 0.5;
                    mgaConfig.DepartureAltitudeKm = meanAltKm > 30.0 ? meanAltKm : 200.0;
                    mgaConfig.DepartureInclinationDeg = Mathf.Clamp((float)Math.Abs(v.orbit.inclination), 0f, 90f);
                } else if (v.altitude > 0) {
                    mgaConfig.DepartureAltitudeKm = Math.Max(100.0, v.altitude / 1000.0);
                }
            }

            bool isInterplanetary = string.IsNullOrEmpty(mgaConfig.CentralBody) ||
                                    mgaConfig.CentralBody.Equals(SolarSystemCatalog.RootStarName, StringComparison.OrdinalIgnoreCase) ||
                                    mgaConfig.CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                                    mgaConfig.CentralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                                    mgaConfig.CentralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

            double searchDays = isInterplanetary ? (365.25 * 3.0) : 30.0;
            mgaConfig.DepartureSearchDays = searchDays;
            mgaConfig.EarliestDepartureUT = nowUt;
            mgaConfig.LatestDepartureUT = nowUt + 86400.0 * searchDays;

            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
            mgaDatesInitialized = true;

            // 彻底清理输入框防抖残留缓存，确保 UI 文本框立即呈现游戏当前历元 (如 1951 年)
            MGAStateManager.OnClearInputCacheRequested?.Invoke();
            
            // 自动刷新候选序列与画布 (仅在单次同步时执行一次)
            RefreshCandidateChains();
            RefreshMgaCanvas();
            KSPGMATLog.Info($"[TabMGA] 每次进入 MGA 界面已自动同步游戏当前时间: UT={nowUt:F1}s ({mgaDepDateInput} ~ {mgaEndDateInput})");
        }

        private void InitOrSyncMgaFromActiveVessel(bool force = false) {
            if (!force && mgaDatesInitialized) return;
            SyncMgaWithCurrentGameTime(force);
        }

        private void SyncDepartureOrbitFromActiveVessel() {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null) {
                GMATStateManager.Current.SyncFromVessel(v);
                GMATStateManager.Current.SyncToMGAConfig(mgaConfig);
                mgaConfig.LockDepartureInclination = true;
                mgaConfig.LockDepartureRAAN = true;
                ScreenMessages.PostScreenMessage($"已从飞船同步真实 ICRF 在轨参数: Inc={mgaConfig.DepartureInclinationDeg:F1}°, LAN={mgaConfig.DepartureRAANDeg:F1}°, PeA={mgaConfig.DeparturePeAKm:F0}km", 3.5f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                ScreenMessages.PostScreenMessage("未检测到有效飞行中飞船轨道，已保留默认锁死参数", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private double GetCurrentVesselAvailableDeltaV() {
            double totalDv = 0.0;
            try {
                var v = FlightGlobals.ActiveVessel;
                if (v != null && v.VesselDeltaV != null) {
                    totalDv = v.VesselDeltaV.TotalDeltaVActual;
                }
            } catch { }

            if (totalDv <= 1.0 && cachedStages != null && cachedStages.Count > 0) {
                totalDv = cachedStages.Sum(s => s.deltaV);
            }
            return totalDv;
        }

        private void RefreshCandidateChains(bool autoSelectFirst = false) {
            generatedCandidateChains = MGAAdaptiveSolver.GenerateCandidateSequences(
                mgaConfig.DepartureBody, 
                mgaConfig.DestinationBody, 
                mgaConfig.MaxSwingBys, 
                mgaConfig.MaxResonances, 
                mgaConfig.MaxBackLegs,
                mgaConfig.MaxBackSpacing);

            if (autoSelectFirst && generatedCandidateChains.Count > 0 && mgaWorkflowMode == 1) {
                selectedCandidateChainIndex = 0;
                mgaConfig.FlybySequence = new List<string>(generatedCandidateChains[0]);
                customChainInput = string.Join(",", mgaConfig.FlybySequence);
            }
        }

        private void AutoSolveDirectHohmann() {
            string target = mgaConfig.DestinationBody;
            if (string.IsNullOrEmpty(mgaConfig.DepartureBody) || string.IsNullOrEmpty(target) ||
                mgaConfig.DepartureBody.Equals(target, StringComparison.OrdinalIgnoreCase)) {
                ScreenMessages.PostScreenMessage(I18n.T("mga_err_same_dep_dest"), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            SolarSystemCatalog.EnsureAllPlanetsCached();
            MGAStateManager.Current.SetDepartureAndDestination(mgaConfig.DepartureBody, target);
            mgaConfig.CentralBody = MGAStateManager.Current.Config.CentralBody;

            double startUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
            if (startUT <= 0) startUT = Planetarium.GetUniversalTime() > 0 ? Planetarium.GetUniversalTime() : 0.0;
            UnityEngine.Debug.Log($"[KSPGMAT-MGA] >>> 点击 [自动求解]: {mgaConfig.DepartureBody} -> {target} | 输入历元: '{mgaDepDateInput}' (startUT={startUT:F1})");

            if (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit && FlightGlobals.ActiveVessel != null) {
                GMATStateManager.Current.SyncFromVessel(FlightGlobals.ActiveVessel);
                GMATStateManager.Current.SyncToMGAConfig(mgaConfig);
            }
            double targetInc = (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaConfig.LockDepartureInclination) ? mgaConfig.DepartureInclinationDeg : -1.0;
            double targetRaan = (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaConfig.LockDepartureRAAN) ? mgaConfig.DepartureRAANDeg : -1.0;
            var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, target, startUT, 0, targetInc, targetRaan);
            if (double.IsNaN(optUT)) {
                UnityEngine.Debug.LogWarning($"[KSPGMAT-MGA] CalculateHohmannWindow 返回 NaN (C++ 原生库未就绪或无法计算)");
                ScreenMessages.PostScreenMessage(I18n.T("mga_native_unavailable"), 4.5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            UnityEngine.Debug.Log($"[KSPGMAT-MGA] 预估霍曼窗口: 最优发射UT={optUT:F1} ({GMATClient.FormatUtcGregorian(optUT)}), 飞行时间={tofDays:F1}d, 预估ΔV={approxDV:F1} m/s");

            mgaConfig.FlybySequence = new List<string> { target };
            double windowHalfSpan = (tofDays < 15.0) ? Math.Max(2.0, tofDays * 1.5) : 25.0;
            mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * windowHalfSpan);
            mgaConfig.LatestDepartureUT = optUT + 86400.0 * windowHalfSpan;
            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
            
            double minTof = (tofDays < 15.0) ? Math.Max(0.5, tofDays * 0.6) : Math.Max(15.0, tofDays * 0.75);
            mgaConfig.MinToFDays = new List<double> { minTof };
            mgaConfig.MaxToFDays = new List<double> { Math.Max(minTof * 1.5, tofDays * 1.35) };

            UnityEngine.Debug.Log($"[KSPGMAT-MGA] 寻优搜索空间: 发射窗口=[{mgaConfig.EarliestDepartureUT:F1}, {mgaConfig.LatestDepartureUT:F1}] ({mgaDepDateInput} ~ {mgaEndDateInput}), 飞行范围=[{mgaConfig.MinToFDays[0]:F1}, {mgaConfig.MaxToFDays[0]:F1}] 天");

            StartMgaOptimization();
            ScreenMessages.PostScreenMessage(I18n.Format("mga_solve_window", mgaConfig.DepartureBody, target, GMATClient.FormatUtcGregorian(optUT)), 4.5f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void LoadHistoricalMissionPreset(string missionKey) {
            var bCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf(missionKey, StringComparison.OrdinalIgnoreCase) >= 0);
            if (bCase != null) {
                var cfg = MGAMissionConfigFile.FromBenchmarkCase(bCase);
                LoadMissionConfigFile(cfg, false);
            } else {
                var allMissions = MGAMissionRepository.LoadAllMissions();
                var found = allMissions.Find(m => m.mission_name.IndexOf(missionKey, StringComparison.OrdinalIgnoreCase) >= 0);
                if (found != null) {
                    LoadMissionConfigFile(found, false);
                }
            }
        }

        private void LoadMissionConfigFile(MGAMissionConfigFile cfg, bool openInSequenceEditor = false) {
            if (cfg == null) return;

            mgaConfig.DepartureBody = cfg.departure_body;
            mgaConfig.DestinationBody = cfg.destination_body;
            mgaConfig.FlybySequence = new List<string>(cfg.flyby_sequence ?? new List<string>());
            customChainInput = string.Join(",", mgaConfig.FlybySequence);

            double launchUT = GMATClient.ParseUtcGregorian(cfg.earliest_departure_date);
            if (launchUT <= 0) launchUT = cfg.historic_launch_ut;
            if (launchUT <= 0) launchUT = Planetarium.GetUniversalTime();

            mgaConfig.EarliestDepartureUT = Math.Max(0, launchUT - 86400.0 * 20.0);
            mgaConfig.LatestDepartureUT = launchUT + 86400.0 * 20.0;
            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);

            mgaConfig.DeparturePeAKm = cfg.departure_pe_km;
            mgaConfig.DepartureApAKm = cfg.departure_ap_km;
            mgaConfig.DestinationPeAKm = cfg.destination_pe_km;
            mgaConfig.DestinationApAKm = cfg.destination_ap_km;
            mgaConfig.DepartureAltitudeKm = cfg.departure_pe_km;
            mgaConfig.DestinationAltitudeKm = cfg.destination_pe_km;
            mgaConfig.NoInsertionBurn = cfg.no_insertion_burn;
            mgaConfig.EngineMode = (MGAEngineMode)cfg.engine_mode;
            mgaConfig.DsmEtaDefault = cfg.dsm_eta_default;
            mgaConfig.LowThrustMaxThrustN = cfg.low_thrust_max_thrust_n;
            mgaConfig.LowThrustIspSec = cfg.low_thrust_isp_s;
            mgaConfig.SpacecraftWetMassKg = cfg.spacecraft_wet_mass_kg;
            mgaConfig.TargetBDotTKm = cfg.target_b_dot_t_km;
            mgaConfig.TargetBDotRKm = cfg.target_b_dot_r_km;

            if (cfg.leg_strategies != null && cfg.leg_strategies.Count > 0) {
                mgaConfig.LegStrategies = cfg.leg_strategies.Select(i => (MGALegStrategy)i).ToList();
            }
            if (cfg.flyby_strategies != null && cfg.flyby_strategies.Count > 0) {
                mgaConfig.FlybyStrategies = cfg.flyby_strategies.Select(i => (MGAFlybyStrategy)i).ToList();
            }
            if (cfg.tof_days != null && cfg.tof_days.Count > 0) {
                mgaConfig.MinToFDays = cfg.tof_days.Select(d => Math.Max(10.0, d * 0.9)).ToList();
                mgaConfig.MaxToFDays = cfg.tof_days.Select(d => d * 1.1).ToList();
            } else if (cfg.min_tof_days != null && cfg.min_tof_days.Count > 0) {
                mgaConfig.MinToFDays = new List<double>(cfg.min_tof_days);
                mgaConfig.MaxToFDays = new List<double>(cfg.max_tof_days);
            }

            RefreshCandidateChains(false);

            // 若配置中已显式包含自定义步骤，直接载入队列
            if (cfg.sequence_steps != null && cfg.sequence_steps.Count > 0) {
                MGAStateManager.Current.MissionSequence = new List<MGAMissionStep>(cfg.sequence_steps);
            }

            // 运行基准求解并获取权威方案
            var bCase = cfg.ToBenchmarkCase();
            var bRes = MGABenchmarkSuite.RunBenchmark(bCase);
            if (bRes != null && bRes.Solution != null) {
                selectedMgaSolution = bRes.Solution;
                mgaSolutions = new List<MGASolution> { selectedMgaSolution };
                selectedManeuverIndex = 0;
                // 同步至任务序列工作台单数据源 (SSOT)
                MGAStateManager.Current.NotifySolutionChanged(selectedMgaSolution);
            } else {
                MGAStateManager.Current.NotifyStateChanged();
            }

            RefreshMgaCanvas();

            if (openInSequenceEditor) {
                mgaWorkflowMode = 3;
                activeSequenceMode = 1;
                ScreenMessages.PostScreenMessage(I18n.Format("mga_load_and_edit_msg", cfg.mission_name), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                ScreenMessages.PostScreenMessage(I18n.Format("mga_load_hist", cfg.mission_name, selectedMgaSolution?.TotalDeltaVKms * 1000.0), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ExportCurrentMissionScript() {
            try {
                var currentSeq = MGAStateManager.Current.MissionSequence;
                string missionName = (selectedMgaSolution != null && !string.IsNullOrEmpty(selectedMgaSolution.FlybyChainSummary)) ? 
                    $"Mission_{selectedMgaSolution.FlybyChainSummary}" : $"Mission_{DateTime.Now:yyyyMMdd_HHmm}";
                
                var script = MGAMissionConfigFile.FromSequence(currentSeq, mgaConfig, missionName, "用户自定义", "导出的统一行星际任务配置文件");
                string savedPath = MGAMissionRepository.SaveMission(script);
                ScreenMessages.PostScreenMessage($"[统一任务配置] 已导出至: {Path.GetFileName(savedPath)}", 4.5f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage($"[导出配置失败] {ex.Message}", 4.5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ImportAndSolveMissionScript() {
            try {
                var missions = MGAMissionRepository.LoadAllMissions(true);
                if (missions.Count > 0) {
                    var latest = missions.Last();
                    LoadMissionConfigFile(latest, false);
                    StartMgaOptimization();
                    ScreenMessages.PostScreenMessage($"[统一任务配置] 已载入并启动全局优化: {latest.mission_name}", 4.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage($"[导入配置失败] {ex.Message}", 4.5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void RunNativeDiagnostics() {
            try {
                Debug.Log("==========================================================================");
                Debug.Log("[KSPGMAT-MGA-DEBUG] 启动 MGA 原生底层动力学引擎与星历自检...");
                Debug.Log($"[KSPGMAT-MGA-DEBUG] 星历中枢状态: {EphemerisTimeSystem.GetCurrentEphemerisSourceDescription()}");
                Debug.Log($"[KSPGMAT-MGA-DEBUG] 时间基准: KSP_EPOCH_JD={EphemerisTimeSystem.KSP_EPOCH_JD:F1}, J2000_JD={EphemerisTimeSystem.J2000_JD:F1}, J2000_UT={EphemerisTimeSystem.J2000_UT:F1}s");
                if (GMATClient.HasFullSpiceEphemeris) {
                    Debug.Log($"[KSPGMAT-MGA-DEBUG] NASA GMAT DE440/DE430 内核: {GMATClient.DetectedSpiceKernelName} ({GMATClient.DetectedSpiceKernelPath})");
                }
                Debug.Log($"[KSPGMAT-MGA-DEBUG] PykepNative 可用状态: {PykepNative.IsAvailable} | 版本: {PykepNative.Version}");
                Debug.Log($"[KSPGMAT-MGA-DEBUG] PyKEP SPICE 导入状态: {(PykepNative.HasSpiceLoaded ? $"已加载 ({System.IO.Path.GetFileName(PykepNative.LoadedSpicePath)})" : "未加载 (使用开普勒平根数)")}");
                Debug.Log($"[KSPGMAT-MGA-DEBUG] 太阳常数 SunMu: {SolarSystemCatalog.SunMu:E6} km^3/s^2");

                // 1. 检验全行星星历 (核验 SMA 与 Mu，绝不允许被退化为地球 1 AU)
                string[] testPlanets = new[] { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };
                double testUT = GMATClient.ParseUtcGregorian("15 Oct 1997 08:43:00");
                foreach (var pName in testPlanets) {
                    var p = SolarSystemCatalog.GetPlanet(pName);
                    p.GetHeliocentricState(testUT, out Vector3d pos, out Vector3d vel);
                    string spiceTag = PykepNative.HasSpiceLoaded ? "[SPICE DE440]" : "[Keplerian]";
                    Debug.Log($"[KSPGMAT-MGA-DEBUG] 行星: {p.Name,-8} {spiceTag} | SMA: {p.SMA:F1} km ({p.SMA / 1.496e8:F2} AU) | Mu: {p.Mu:F1} | 历元位置: ({pos.x / 1.496e8:F3}, {pos.y / 1.496e8:F3}, {pos.z / 1.496e8:F3}) AU | 速度: {vel.magnitude:F2} km/s");

                    // 如果已加载 SPICE，额外对比开普勒平根数与 SPICE 真值的位置偏差
                    if (PykepNative.HasSpiceLoaded && PykepNative.ElementsToState(
                            p.SMA, p.ECC, p.INC * Mathf.Deg2Rad, p.RAAN * Mathf.Deg2Rad, p.AOP * Mathf.Deg2Rad,
                            p.MA0 * Mathf.Deg2Rad, p.EpochJ2000, testUT, SolarSystemCatalog.SunMu,
                            out Vector3d posKep, out Vector3d _)) {
                        double deltaKm = (pos - posKep).magnitude;
                        Debug.Log($"[KSPGMAT-MGA-DEBUG]    └─ 摄动修正: SPICE 真值 vs 开普勒二体平根数 差异 = {deltaKm:F1} km ({deltaKm / 1000.0:F2} Mm)");
                    }
                }

                // 2. 检验基准求解 (以 Cassini 为例测试全段)
                var cassini = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("Cassini", StringComparison.OrdinalIgnoreCase) >= 0);
                if (cassini != null) {
                    var res = MGABenchmarkSuite.RunBenchmark(cassini);
                    if (res != null && res.Solution != null) {
                        Debug.Log($"[KSPGMAT-MGA-DEBUG] Cassini 求解结果: 总 ΔV = {res.Solution.TotalDeltaVKms * 1000.0:F1} m/s | 航段数 = {res.Solution.Legs.Count} | 全部可行 = {res.Solution.AllLegsFeasible}");
                        for (int i = 0; i < res.Solution.Legs.Count; i++) {
                            var l = res.Solution.Legs[i];
                            Debug.Log($"[KSPGMAT-MGA-DEBUG]    第 {i+1} 段: {l.FromBody} -> {l.ToBody} | ToF = {l.ToFDays:F1}d | Flyby ΔV = {l.FlybyDeltaVKms * 1000.0:F1} m/s | rp = {l.FlybyPeriapsisRadiusKm:F1} km");
                        }
                    }
                }

                Debug.Log("==========================================================================");
                ScreenMessages.PostScreenMessage("[原生底层动力学自检] 成功执行！星历常数与求解数据已详细输出至 KSP.log", 5.0f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                Debug.LogError($"[KSPGMAT-MGA-DEBUG] 自检异常: {ex}");
                ScreenMessages.PostScreenMessage($"[原生动力学自检失败] {ex.Message}", 5.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private bool showMgaReportModal = false;
        private Vector2 mgaReportScrollPos = Vector2.zero;

        private string GenerateDetailedMgaReport(MGASolution sol) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return I18n.T("mga_report_no_solution");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine(I18n.T("mga_report_title"));
            sb.AppendLine(I18n.Format("mga_report_generated", DateTime.Now));
            sb.AppendLine("==========================================================================================");
            sb.AppendLine();
            sb.AppendLine(I18n.T("mga_report_section1"));
            sb.AppendLine(I18n.Format("mga_report_flight_chain", sol.FlybyChainSummary));
            sb.AppendLine(I18n.Format("mga_report_dep_epoch", GMATClient.FormatUtcGregorian(sol.DepartureUT), sol.DepartureUT));
            sb.AppendLine(I18n.Format("mga_report_total_duration", sol.TotalFlightDays, sol.TotalFlightDays / 365.25));
            sb.AppendLine(I18n.Format("mga_report_total_dv", sol.TotalDeltaVKms * 1000.0));
            double totalDsm = sol.Legs.Skip(1).Sum(l => l.FlybyDeltaVKms);
            sb.AppendLine(I18n.Format("mga_report_ejection_dv", sol.LaunchDeltaVKms * 1000.0));
            sb.AppendLine(I18n.Format("mga_report_dsm_total", totalDsm * 1000.0));
            sb.AppendLine(I18n.Format("mga_report_capture_dv", sol.InsertionDeltaVKms * 1000.0));
            sb.AppendLine(I18n.Format("mga_report_feasibility", sol.AllLegsFeasible ? I18n.T("mga_report_feasible_ok") : I18n.T("mga_report_feasible_power")));
            sb.AppendLine();
            sb.AppendLine(I18n.T("mga_report_section2"));
            if (sol.Events == null || sol.Events.Count == 0) {
                sol.BuildEvents(mgaConfig);
            }

            for (int i = 0; i < sol.Events.Count; i++) {
                var ev = sol.Events[i];
                sb.AppendLine($"------------------------------------------------------------------------------------------");
                string evDateStr = GMATClient.FormatUtcGregorian(ev.EpochUT);
                switch (ev.EventType) {
                    case MGAEventType.Escape:
                        sb.AppendLine(I18n.Format("mga_report_ev_escape", ev.SequenceNumber, ev.Title));
                        sb.AppendLine(I18n.Format("mga_report_ev_epoch", evDateStr, ev.EpochUT));
                        sb.AppendLine(I18n.Format("mga_report_ev_escape_dv", ev.DeltaVMs, ev.VInfOutKms));
                        sb.AppendLine(I18n.Format("mga_report_vec3", ev.ProgradeMs, ev.NormalMs, ev.RadialMs));
                        sb.AppendLine(I18n.Format("mga_report_ev_escape_geo", ev.EjectionAngleDeg, ev.EjectionInclinationDeg, ev.EjectionLANDeg, ev.C3Kms2, ev.PhaseAngleDeg));
                        break;
                    case MGAEventType.Flyby:
                        sb.AppendLine(I18n.Format("mga_report_ev_flyby", ev.SequenceNumber, ev.Title));
                        sb.AppendLine(I18n.Format("mga_report_ev_epoch_flyby", evDateStr, ev.EpochUT));
                        string fType = (ev.DeltaVMs > 0.001) ? I18n.Format("mga_report_powered_flyby", ev.DeltaVMs) : I18n.T("mga_report_ballistic_flyby");
                        sb.AppendLine(I18n.Format("mga_report_flyby_nature", fType));
                        sb.AppendLine(I18n.Format("mga_report_flyby_geo", ev.FlybyPeriapsisAltitudeKm, ev.FlybyPeriapsisRadiusKm, ev.FlybyTurningAngleDeg));
                        sb.AppendLine(I18n.Format("mga_report_flyby_vinf", ev.VInfInKms, ev.VInfOutKms));
                        sb.AppendLine(I18n.Format("mga_report_flyby_feas", ev.IsFeasible ? I18n.T("mga_report_flyby_ok") : I18n.T("mga_report_flyby_power")));
                        break;
                    case MGAEventType.DSM:
                        sb.AppendLine(I18n.Format("mga_report_ev_dsm", ev.SequenceNumber, ev.Title));
                        sb.AppendLine(I18n.Format("mga_report_ev_epoch_dsm", evDateStr, ev.EpochUT));
                        sb.AppendLine(I18n.Format("mga_report_ev_dsm_dv", ev.DeltaVMs));
                        sb.AppendLine(I18n.Format("mga_report_vec3", ev.ProgradeMs, ev.NormalMs, ev.RadialMs));
                        break;
                    case MGAEventType.Circularization:
                        sb.AppendLine(I18n.Format("mga_report_ev_capture", ev.SequenceNumber, ev.Title));
                        sb.AppendLine(I18n.Format("mga_report_ev_epoch_arrival", evDateStr, ev.EpochUT));
                        sb.AppendLine(I18n.Format("mga_report_ev_capture_dv", ev.DeltaVMs, ev.ProgradeMs));
                        sb.AppendLine(I18n.Format("mga_report_ev_capture_orb", ev.InsertionAltitudeKm, ev.InsertionInclinationDeg, ev.VInfInKms));
                        break;
                }
            }

            sb.AppendLine();
            sb.AppendLine(I18n.T("mga_report_section3"));

            for (int k = 0; k < sol.Legs.Count; k++) {
                var leg = sol.Legs[k];
                sb.AppendLine($"------------------------------------------------------------------------------------------");
                sb.AppendLine(I18n.Format("mga_report_leg", k + 1, leg.FromBody, leg.ToBody, leg.ToFDays));
                sb.AppendLine(I18n.Format("mga_report_ev_epoch", GMATClient.FormatUtcGregorian(leg.DepartureUT), leg.DepartureUT));
                sb.AppendLine(I18n.Format("mga_report_ev_epoch_arrival", GMATClient.FormatUtcGregorian(leg.ArrivalUT), leg.ArrivalUT));
                sb.AppendLine(I18n.Format("mga_report_leg_r1", leg.DeparturePosKm.x, leg.DeparturePosKm.y, leg.DeparturePosKm.z, leg.DeparturePosKm.magnitude / 1.496e8));
                sb.AppendLine(I18n.Format("mga_report_leg_r2", leg.ArrivalPosKm.x, leg.ArrivalPosKm.y, leg.ArrivalPosKm.z, leg.ArrivalPosKm.magnitude / 1.496e8));
                sb.AppendLine(I18n.Format("mga_report_leg_v1", leg.DepartureVelKms.x, leg.DepartureVelKms.y, leg.DepartureVelKms.z, leg.DepartureVelKms.magnitude));
                sb.AppendLine(I18n.Format("mga_report_leg_v2", leg.ArrivalVelKms.x, leg.ArrivalVelKms.y, leg.ArrivalVelKms.z, leg.ArrivalVelKms.magnitude));
            }

            sb.AppendLine();
            sb.AppendLine(I18n.T("mga_report_section_script"));
            sb.AppendLine($"%----------------------------------------------------------------------");
            sb.AppendLine($"% NASA GMAT Mission Initial State & Spacecraft Block");
            sb.AppendLine($"%----------------------------------------------------------------------");
            sb.AppendLine($"Create Spacecraft SC;");
            sb.AppendLine($"GMAT SC.DateFormat = UTCGregorian;");
            sb.AppendLine($"GMAT SC.Epoch = '{GMATClient.FormatUtcGregorian(sol.DepartureUT)}';");
            sb.AppendLine($"GMAT SC.CoordinateSystem = SunMJ2000Ec;");
            sb.AppendLine($"GMAT SC.DisplayStateType = Cartesian;");
            var legInit = sol.Legs[0];
            sb.AppendLine($"GMAT SC.X = {legInit.DeparturePosKm.x:F6};");
            sb.AppendLine($"GMAT SC.Y = {legInit.DeparturePosKm.y:F6};");
            sb.AppendLine($"GMAT SC.Z = {legInit.DeparturePosKm.z:F6};");
            sb.AppendLine($"GMAT SC.VX = {legInit.DepartureVelKms.x:F6};");
            sb.AppendLine($"GMAT SC.VY = {legInit.DepartureVelKms.y:F6};");
            sb.AppendLine($"GMAT SC.VZ = {legInit.DepartureVelKms.z:F6};");
            sb.AppendLine();
            sb.AppendLine($"%----------------------------------------------------------------------");
            sb.AppendLine($"% Impulsive Burns Definition");
            sb.AppendLine($"%----------------------------------------------------------------------");
            sb.AppendLine($"Create ImpulsiveBurn TMI;");
            sb.AppendLine($"GMAT TMI.CoordinateSystem = Local;");
            sb.AppendLine($"GMAT TMI.Origin = {legInit.FromBody};");
            sb.AppendLine($"GMAT TMI.Axes = VNC;");
            sb.AppendLine($"GMAT TMI.Element1 = {legInit.ProgradeMs / 1000.0:F6}; % km/s Prograde");
            sb.AppendLine($"GMAT TMI.Element2 = {legInit.NormalMs / 1000.0:F6}; % km/s Normal");
            sb.AppendLine($"GMAT TMI.Element3 = {legInit.RadialMs / 1000.0:F6}; % km/s Radial");
            sb.AppendLine();

            for (int k = 1; k < sol.Legs.Count; k++) {
                var l = sol.Legs[k];
                if (l.FlybyDeltaVKms > 0.001) {
                    sb.AppendLine($"Create ImpulsiveBurn DSM_{k};");
                    sb.AppendLine($"GMAT DSM_{k}.CoordinateSystem = Local;");
                    sb.AppendLine($"GMAT DSM_{k}.Origin = {l.FromBody};");
                    sb.AppendLine($"GMAT DSM_{k}.Axes = VNC;");
                    sb.AppendLine($"GMAT DSM_{k}.Element1 = {l.ProgradeMs / 1000.0:F6};");
                    sb.AppendLine($"GMAT DSM_{k}.Element2 = {l.NormalMs / 1000.0:F6};");
                    sb.AppendLine($"GMAT DSM_{k}.Element3 = {l.RadialMs / 1000.0:F6};");
                    sb.AppendLine();
                }
            }

            if (sol.InsertionDeltaVKms > 0.001) {
                var lastLeg = sol.Legs.Last();
                sb.AppendLine($"Create ImpulsiveBurn CaptureBurn;");
                sb.AppendLine($"GMAT CaptureBurn.CoordinateSystem = Local;");
                sb.AppendLine($"GMAT CaptureBurn.Origin = {lastLeg.ToBody};");
                sb.AppendLine($"GMAT CaptureBurn.Axes = VNC;");
                sb.AppendLine($"GMAT CaptureBurn.Element1 = {-sol.InsertionDeltaVKms:F6};");
                sb.AppendLine($"GMAT CaptureBurn.Element2 = 0.0;");
                sb.AppendLine($"GMAT CaptureBurn.Element3 = 0.0;");
                sb.AppendLine();
            }

            sb.AppendLine($"%----------------------------------------------------------------------");
            sb.AppendLine($"% Mission Sequence Execution");
            sb.AppendLine($"%----------------------------------------------------------------------");
            sb.AppendLine($"BeginMissionSequence;");
            sb.AppendLine($"Maneuver TMI(SC);");
            sb.AppendLine($"Propagate DeepSpaceProp(SC) {{SC.ElapsedDays = {legInit.ToFDays:F4}}};");
            for (int k = 1; k < sol.Legs.Count; k++) {
                var l = sol.Legs[k];
                if (l.FlybyDeltaVKms > 0.001) {
                    sb.AppendLine($"Maneuver DSM_{k}(SC);");
                }
                sb.AppendLine($"Propagate DeepSpaceProp(SC) {{SC.ElapsedDays = {l.ToFDays:F4}}};");
            }
            if (sol.InsertionDeltaVKms > 0.001) {
                sb.AppendLine($"Maneuver CaptureBurn(SC);");
            }

            sb.AppendLine();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine(I18n.T("mga_report_footer"));
            sb.AppendLine("==========================================================================================");
            return sb.ToString();
        }

        public static string GenerateGmatScriptForMGA(MGASolution sol) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine($"% NASA GMAT Mission Script generated automatically by KSP_GMAT");
            sb.AppendLine($"% Architecture: {sol.FlybyChainSummary}");
            sb.AppendLine($"% Departure Epoch: {GMATClient.FormatUtcGregorian(sol.DepartureUT)} UTCGregorian");
            sb.AppendLine($"% Total Mission Delta-V: {sol.TotalDeltaVKms * 1000.0:F1} m/s | Flight Days: {sol.TotalFlightDays:F1} d");
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine();

            // Spacecraft
            var legInit = sol.Legs[0];
            sb.AppendLine("Create Spacecraft SC;");
            sb.AppendLine("GMAT SC.DateFormat = UTCGregorian;");
            // 基线 7.5: GMAT 后端输出必须走 FormatGmatSafeEpoch (对 <1957 存档执行 DE405 安全映射)
            sb.AppendLine($"GMAT SC.Epoch = '{GMATClient.FormatGmatSafeEpoch(sol.DepartureUT)}';");
            sb.AppendLine("GMAT SC.CoordinateSystem = SunMJ2000Ec;");
            sb.AppendLine("GMAT SC.DisplayStateType = Cartesian;");
            sb.AppendLine($"GMAT SC.X = {legInit.DeparturePosKm.x:F6};");
            sb.AppendLine($"GMAT SC.Y = {legInit.DeparturePosKm.y:F6};");
            sb.AppendLine($"GMAT SC.Z = {legInit.DeparturePosKm.z:F6};");
            sb.AppendLine($"GMAT SC.VX = {legInit.DepartureVelKms.x:F6};");
            sb.AppendLine($"GMAT SC.VY = {legInit.DepartureVelKms.y:F6};");
            sb.AppendLine($"GMAT SC.VZ = {legInit.DepartureVelKms.z:F6};");
            sb.AppendLine("GMAT SC.DryMass = 1000;");
            sb.AppendLine("GMAT SC.Cd = 2.2;");
            sb.AppendLine("GMAT SC.Cr = 1.8;");
            sb.AppendLine("GMAT SC.DragArea = 15;");
            sb.AppendLine("GMAT SC.SRPArea = 1;");
            sb.AppendLine();

            // Force Model & Propagator
            sb.AppendLine("Create ForceModel DeepSpaceProp_ForceModel;");
            sb.AppendLine("GMAT DeepSpaceProp_ForceModel.CentralBody = Sun;");
            sb.AppendLine("GMAT DeepSpaceProp_ForceModel.PointMasses = {Sun, Earth, Venus, Mars, Jupiter, Saturn, Uranus, Neptune};");
            sb.AppendLine("GMAT DeepSpaceProp_ForceModel.Drag = None;");
            sb.AppendLine("GMAT DeepSpaceProp_ForceModel.SRP = Off;");
            sb.AppendLine("GMAT DeepSpaceProp_ForceModel.RelativisticCorrection = Off;");
            sb.AppendLine("GMAT DeepSpaceProp_ForceModel.ErrorControl = RSSStep;");
            sb.AppendLine();
            sb.AppendLine("Create Propagator DeepSpaceProp;");
            sb.AppendLine("GMAT DeepSpaceProp.FM = DeepSpaceProp_ForceModel;");
            sb.AppendLine("GMAT DeepSpaceProp.Type = PrinceDormand78;");
            sb.AppendLine("GMAT DeepSpaceProp.InitialStepSize = 600;");
            sb.AppendLine("GMAT DeepSpaceProp.Accuracy = 1e-11;");
            sb.AppendLine("GMAT DeepSpaceProp.MinStep = 0.0;");
            sb.AppendLine("GMAT DeepSpaceProp.MaxStep = 86400;");
            sb.AppendLine();

            // Impulsive Burns
            sb.AppendLine("Create ImpulsiveBurn TMI;");
            sb.AppendLine("GMAT TMI.CoordinateSystem = Local;");
            sb.AppendLine($"GMAT TMI.Origin = {legInit.FromBody};");
            sb.AppendLine("GMAT TMI.Axes = VNC;");
            sb.AppendLine($"GMAT TMI.Element1 = {legInit.ProgradeMs / 1000.0:F6};");
            sb.AppendLine($"GMAT TMI.Element2 = {legInit.NormalMs / 1000.0:F6};");
            sb.AppendLine($"GMAT TMI.Element3 = {legInit.RadialMs / 1000.0:F6};");
            sb.AppendLine();

            for (int k = 1; k < sol.Legs.Count; k++) {
                var l = sol.Legs[k];
                if (l.FlybyDeltaVKms > 0.001) {
                    sb.AppendLine($"Create ImpulsiveBurn DSM_{k};");
                    sb.AppendLine($"GMAT DSM_{k}.CoordinateSystem = Local;");
                    sb.AppendLine($"GMAT DSM_{k}.Origin = {l.FromBody};");
                    sb.AppendLine($"GMAT DSM_{k}.Axes = VNC;");
                    sb.AppendLine($"GMAT DSM_{k}.Element1 = {l.ProgradeMs / 1000.0:F6};");
                    sb.AppendLine($"GMAT DSM_{k}.Element2 = {l.NormalMs / 1000.0:F6};");
                    sb.AppendLine($"GMAT DSM_{k}.Element3 = {l.RadialMs / 1000.0:F6};");
                    sb.AppendLine();
                }
            }

            if (sol.InsertionDeltaVKms > 0.001) {
                var lastLeg = sol.Legs.Last();
                sb.AppendLine("Create ImpulsiveBurn CaptureBurn;");
                sb.AppendLine("GMAT CaptureBurn.CoordinateSystem = Local;");
                sb.AppendLine($"GMAT CaptureBurn.Origin = {lastLeg.ToBody};");
                sb.AppendLine("GMAT CaptureBurn.Axes = VNC;");
                sb.AppendLine($"GMAT CaptureBurn.Element1 = {-sol.InsertionDeltaVKms:F6};");
                sb.AppendLine("GMAT CaptureBurn.Element2 = 0.0;");
                sb.AppendLine("GMAT CaptureBurn.Element3 = 0.0;");
                sb.AppendLine();
            }

            // Mission Sequence
            sb.AppendLine("BeginMissionSequence;");
            sb.AppendLine();
            sb.AppendLine($"% Leg 1: {legInit.FromBody} -> {legInit.ToBody} ({legInit.ToFDays:F1} days)");
            sb.AppendLine("Maneuver TMI(SC);");
            sb.AppendLine($"Propagate DeepSpaceProp(SC) {{SC.ElapsedDays = {legInit.ToFDays:F4}}};");
            sb.AppendLine();

            for (int k = 1; k < sol.Legs.Count; k++) {
                var l = sol.Legs[k];
                sb.AppendLine($"% Leg {k + 1}: {l.FromBody} -> {l.ToBody} ({l.ToFDays:F1} days)");
                if (l.FlybyDeltaVKms > 0.001) {
                    sb.AppendLine($"Maneuver DSM_{k}(SC);");
                }
                sb.AppendLine($"Propagate DeepSpaceProp(SC) {{SC.ElapsedDays = {l.ToFDays:F4}}};");
                sb.AppendLine();
            }

            if (sol.InsertionDeltaVKms > 0.001) {
                sb.AppendLine($"% Destination Insertion Burn at {sol.Legs.Last().ToBody}");
                sb.AppendLine("Maneuver CaptureBurn(SC);");
            }

            return sb.ToString();
        }

        private int lastCanvasW { get => MGAStateManager.Current.Viewport.LastCanvasW; set => MGAStateManager.Current.Viewport.LastCanvasW = value; }
        private int lastCanvasH { get => MGAStateManager.Current.Viewport.LastCanvasH; set => MGAStateManager.Current.Viewport.LastCanvasH = value; }
        private float mgaPitch { get => MGAStateManager.Current.Viewport.Pitch; set => MGAStateManager.Current.Viewport.Pitch = value; }
        private float mgaYaw { get => MGAStateManager.Current.Viewport.Yaw; set => MGAStateManager.Current.Viewport.Yaw = value; }
        private bool isRotatingCanvas = false;
        private bool mgaAutoFocusEnabled { get => MGAStateManager.Current.Viewport.AutoFocus; set => MGAStateManager.Current.Viewport.AutoFocus = value; }

        private void FocusOnSelectedManeuver(bool autoCenter = true) {
            if (selectedMgaSolution == null || selectedMgaSolution.Events == null || selectedMgaSolution.Events.Count == 0) return;
            if (selectedManeuverIndex < 0 || selectedManeuverIndex >= selectedMgaSolution.Events.Count) return;

            if (autoCenter) {
                var curEv = selectedMgaSolution.Events[selectedManeuverIndex];
                Vector3d tgtPos = GMATOrbitCanvas.GetEventPositionKm(selectedMgaSolution, curEv);
                if (!double.IsNaN(tgtPos.x)) {
                    double missionMaxDistKm = 2.2 * 1.496e8;
                    foreach (var leg in selectedMgaSolution.Legs) {
                        double r1 = leg.DeparturePosKm.magnitude;
                        double r2 = leg.ArrivalPosKm.magnitude;
                        if (!double.IsNaN(r1) && r1 > missionMaxDistKm) missionMaxDistKm = r1;
                        if (!double.IsNaN(r2) && r2 > missionMaxDistKm) missionMaxDistKm = r2;
                    }
                    double viewRadiusKm = (missionMaxDistKm * 1.30) / Math.Max(0.05f, mgaCanvasZoom);
                    double scale = (Math.Min(lastCanvasW, lastCanvasH) * 0.44) / viewRadiusKm;

                    Vector2 unpannedScr = GMATOrbitCanvas.Project3D(tgtPos, scale, lastCanvasW / 2, lastCanvasH / 2, mgaPitch, mgaYaw, out _);
                    mgaCanvasPan = new Vector2(lastCanvasW / 2f - unpannedScr.x, lastCanvasH / 2f - unpannedScr.y);
                }
            }
            RefreshMgaCanvas();
        }

        private void RefreshMgaCanvas(int w = 0, int h = 0) {
            if (w > 100 && h > 100) {
                lastCanvasW = w;
                lastCanvasH = h;
            }
            float flow3D = MapStepProgressTo3DFlow(mgaFlowProgress);
            mgaCanvasTexture = GMATOrbitCanvas.RenderSolarSystemMGATexture(
                mgaCanvasTexture,
                selectedMgaSolution, 
                lastCanvasW, lastCanvasH, 
                mgaCanvasZoom, 
                mgaCanvasPan, 
                mgaShowSois,
                mgaPitch,
                mgaYaw,
                selectedManeuverIndex,
                flow3D);
        }

        public void UpdateMgaAnimationTick() {
            if (selectedMgaSolution == null || selectedMgaSolution.Legs == null || selectedMgaSolution.Legs.Count == 0) return;
            int totalSteps = GetMgaTotalSteps();

            float dt = Time.deltaTime;
            if (dt <= 0.0001f || dt > 0.2f) dt = 0.016f;

            bool needRedraw = false;

            if (mgaIsPlaying) {
                // 自动星际巡航动画：随时间平滑推进，航迹平滑延伸，行星平滑公转
                float playSpeed = (1.0f / 3.5f) * mgaPlaySpeed;
                mgaFlowProgress += dt * playSpeed;
                if (mgaFlowProgress > (float)totalSteps) {
                    mgaFlowProgress = 0.0f;
                }
                mgaTargetProgress = mgaFlowProgress;
                needRedraw = true;
            } else if (Mathf.Abs(mgaFlowProgress - mgaTargetProgress) > 0.001f) {
                // 平滑步进过渡动画：当点击 ◀ / ▶ 翻阅或点击步骤时，非线性 Ease-Out 飞向目标节点
                float diff = Math.Abs(mgaTargetProgress - mgaFlowProgress);
                float speed = Mathf.Max(1.2f, diff * 4.2f) * mgaPlaySpeed;
                mgaFlowProgress = Mathf.MoveTowards(mgaFlowProgress, mgaTargetProgress, dt * speed);
                needRedraw = true;
            }

            if (needRedraw) {
                float flow3D = MapStepProgressTo3DFlow(mgaFlowProgress);
                if (flow3D > 0 && selectedMgaSolution.Events != null && selectedMgaSolution.Events.Count > 0) {
                    selectedManeuverIndex = Mathf.Clamp((int)Math.Ceiling(flow3D) - 1, 0, selectedMgaSolution.Events.Count - 1);
                }
                RefreshMgaCanvas();
            }
        }

        private void DrawTabMGA(Vessel vessel) {
            InitOrSyncMgaFromActiveVessel();

            if (mgaWorkflowMode == 2) {
                DrawTabMGASequence(vessel);
                return;
            }

            mgaTabScroll = GUILayout.BeginScrollView(mgaTabScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // 双栏自适应宽敞布局 (左侧规划器 + 右侧高精 3D 视口)
            GUILayout.BeginHorizontal();

            // =========================================================================
            // 左栏：借力计算器与参数配置 (动态自适应宽度，彻底消除横向滚动条，控件自适应完全显示)
            // =========================================================================
            float leftColWidth = Mathf.Clamp(windowRect.width * 0.46f, 480f, 680f);
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(leftColWidth));
            mgaLeftScroll = GUILayout.BeginScrollView(mgaLeftScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Width(leftColWidth), GUILayout.ExpandHeight(true));

            // 1. 顶部标题与活跃飞船同步
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_title"), headerStyle);
            GUILayout.FlexibleSpace();
            Vessel activeV = FlightGlobals.ActiveVessel;
            if (activeV != null && GUILayout.Button(I18n.T("mga_btn_sync"), secondaryButtonStyle, GUILayout.Height(20))) {
                InitOrSyncMgaFromActiveVessel(true);
                ScreenMessages.PostScreenMessage(I18n.Format("mga_sync_ok", activeV.vesselName), 3f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 1. 顶部模式选择器 (合并直达与MGA为单一系统: 轨道转移与借力规划 / 任务序列工作台)
            GUIStyle plannerModeStyle = (mgaWorkflowMode != 2) ? actionButtonStyle : secondaryButtonStyle;
            GUIStyle seqModeStyle = (mgaWorkflowMode == 2) ? actionButtonStyle : secondaryButtonStyle;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("mga_mode_planner", "🚀 轨道转移与借力寻优 (Trajectory Planner)"), plannerModeStyle, GUILayout.Height(26), GUILayout.ExpandWidth(true))) {
                if (mgaWorkflowMode == 2) MGAStateManager.Current.SwitchWorkflowMode(0);
                activeSequenceMode = 0;
                showBenchmarkModal = false;
                showPorkchopModal = false;
                RefreshMgaCanvas();
            }
            if (GUILayout.Button(I18n.T("mga_seq_mode_editor", "📋 任务序列工作台 (Sequence Workbench)"), seqModeStyle, GUILayout.Height(26), GUILayout.ExpandWidth(true))) {
                MGAStateManager.Current.SwitchWorkflowMode(2);
                activeSequenceMode = 1;
                showBenchmarkModal = false;
                showPorkchopModal = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 天体星历数据源状态与误差核验面板 (方案 2 运行态 vs 方案 1 SPICE 权威内核)
            DrawEphemerisSchemeAndComparisonUI();

            var availablePlanets = SolarSystemCatalog.GetAvailablePlanets();

            // =========================================================================
            // 统一天体选择与流式航线管理卡片 (以选择星球为核心驱动，直达/借力平滑无缝)
            // =========================================================================
            GUILayout.BeginVertical(nestedCardBoxStyle);
            DrawCentralBodySystemSelector(mgaConfig);
            GUILayout.BeginHorizontal();
            // 出发天体
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>{I18n.T("mga_departure")}</b>", labelValueStyle);
            var curDep = SolarSystemCatalog.GetPlanet(mgaConfig.DepartureBody);
            if (GUILayout.Button(I18n.Format("mga_planet_btn_short", curDep.Icon, curDep.GetDisplayName(), curDep.Name), actionButtonStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
                OpenPlanetPicker(0, I18n.T("mga_picker_departure"));
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // 目标天体
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label($"<b>{I18n.T("mga_destination")}</b>", labelValueStyle);
            var curDest = SolarSystemCatalog.GetPlanet(mgaConfig.DestinationBody);
            if (GUILayout.Button(I18n.Format("mga_planet_btn_short", curDest.Icon, curDest.GetDisplayName(), curDest.Name), actionButtonStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true))) {
                OpenPlanetPicker(1, I18n.T("mga_picker_destination"));
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // 完整流式航线节点与借力管理
            bool isDirect = (mgaConfig.FlybySequence == null || mgaConfig.FlybySequence.Count <= 1);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{I18n.T("mga_route_header", "航线:")}</b>", labelValueStyle, GUILayout.Width(38));

            // 显示出发天体
            GUILayout.Label($"<color=#38bdf8><b>{curDep.Icon} {curDep.Name}</b></color>", labelValueStyle);

            // 显示中间借力节点 (如果有)
            if (!isDirect) {
                for (int fbIdx = 0; fbIdx < mgaConfig.FlybySequence.Count - 1; fbIdx++) {
                    int idx = fbIdx;
                    string fbName = mgaConfig.FlybySequence[idx];
                    var fbPlanet = SolarSystemCatalog.GetPlanet(fbName);
                    GUILayout.Label("<color=#94a3b8>➔</color>", labelValueStyle, GUILayout.Width(14));
                    if (GUILayout.Button($"{fbPlanet.Icon} {fbPlanet.Name}", secondaryButtonStyle, GUILayout.Height(20))) {
                        OpenPlanetPicker(2, I18n.Format("mga_flyby_edit_title", idx + 1), idx);
                    }
                    if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(16), GUILayout.Height(20))) {
                        mgaConfig.FlybySequence.RemoveAt(idx);
                        customChainInput = string.Join(",", mgaConfig.FlybySequence);
                        RefreshMgaCanvas();
                    }
                }
            }

            // 显示到达目的地天体
            GUILayout.Label("<color=#94a3b8>➔</color>", labelValueStyle, GUILayout.Width(14));
            GUILayout.Label($"<color=#10b981><b>{curDest.Icon} {curDest.Name}</b></color>", labelValueStyle);
            if (isDirect) {
                GUILayout.Label("<color=#94a3b8>(直达)</color>", labelValueStyle);
            } else {
                GUILayout.Label($"<color=#f59e0b>({mgaConfig.FlybySequence.Count - 1}次借力)</color>", labelValueStyle);
            }

            GUILayout.FlexibleSpace();

            // 操作按钮：插入借力 / 清空借力 / 历史预设
            if (GUILayout.Button(I18n.T("btn_insert_flyby", "+ 借力天体"), secondaryButtonStyle, GUILayout.Height(20))) {
                OpenPlanetPicker(3, I18n.T("mga_picker_append"));
            }
            if (!isDirect) {
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_clear_to_direct", "转为直达"), secondaryButtonStyle, GUILayout.Height(20))) {
                    mgaConfig.FlybySequence = new List<string> { mgaConfig.DestinationBody };
                    customChainInput = mgaConfig.DestinationBody;
                    RefreshMgaCanvas();
                }
            }
            GUILayout.Space(4);
            if (GUILayout.Button(I18n.T("mga_btn_more_missions", "🏛️ 预设 ▾"), secondaryButtonStyle, GUILayout.Height(20))) {
                OpenHistoryMissionPicker();
            }
            GUILayout.EndHorizontal();

            // 可选助推拓扑推荐栏 (折叠展开，按需查看，不喧宾夺主)
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            showChainSuggestions = GUILayout.Toggle(showChainSuggestions, I18n.T("mga_toggle_suggest_chains", "🔍 推荐助推链 ▾"), GUILayout.Width(110));
            if (showChainSuggestions) {
                if (generatedCandidateChains.Count > 0) {
                    for (int i = 0; i < Math.Min(3, generatedCandidateChains.Count); i++) {
                        var chain = generatedCandidateChains[i];
                        string tag = (i == 0) ? "🏆 " : (i == 1 ? "⚡ " : "#3 ");
                        string summary = $"{tag}" + string.Join("➔", chain);
                        if (GUILayout.Button(summary, secondaryButtonStyle, GUILayout.Height(18))) {
                            mgaConfig.FlybySequence = new List<string>(chain);
                            customChainInput = string.Join(",", mgaConfig.FlybySequence);
                            RefreshMgaCanvas();
                        }
                    }
                } else {
                    GUILayout.Label("<color=#64748b>(暂无更多拓扑推荐)</color>", labelValueStyle);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 若包含借力天体，提供分段动力学与借力点机动策略矩阵
            if (!isDirect) {
                var fullDisplayChain = new List<string> { mgaConfig.DepartureBody };
                fullDisplayChain.AddRange(mgaConfig.FlybySequence);
                mgaConfig.SyncStrategiesWithChain(Math.Max(1, fullDisplayChain.Count - 1));

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>分段动力学:</b></color>", labelValueStyle, GUILayout.Width(75));
                for (int legIdx = 0; legIdx < fullDisplayChain.Count - 1; legIdx++) {
                    int lIdx = legIdx;
                    var curStrat = (lIdx < mgaConfig.LegStrategies.Count) ? mgaConfig.LegStrategies[lIdx] : MGALegStrategy.Ballistic;
                    string stratLabel = curStrat == MGALegStrategy.Ballistic ? $"段{lIdx + 1}: 弹道" :
                                       (curStrat == MGALegStrategy.DeepSpaceManeuver1DSM ? $"段{lIdx + 1}: 1-DSM" : $"段{lIdx + 1}: 低推力");
                    GUIStyle stBtnStyle = (curStrat == MGALegStrategy.Ballistic) ? secondaryButtonStyle : actionButtonStyle;

                    if (GUILayout.Button(stratLabel, stBtnStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true))) {
                        var nextStrat = (MGALegStrategy)(((int)curStrat + 1) % 3);
                        mgaConfig.LegStrategies[lIdx] = nextStrat;
                        RefreshMgaCanvas();
                    }
                }
                GUILayout.EndHorizontal();

                if (fullDisplayChain.Count > 2) {
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<color=#a78bfa><b>借力点机动:</b></color>", labelValueStyle, GUILayout.Width(75));
                    for (int fbIdx = 0; fbIdx < fullDisplayChain.Count - 2; fbIdx++) {
                        int fIdx = fbIdx;
                        var curFb = (fIdx < mgaConfig.FlybyStrategies.Count) ? mgaConfig.FlybyStrategies[fIdx] : MGAFlybyStrategy.UnpoweredFlyby;
                        string fbLabel = curFb == MGAFlybyStrategy.UnpoweredFlyby ? $"点{fIdx + 1}: 无动力" :
                                        (curFb == MGAFlybyStrategy.PoweredFlyby ? $"点{fIdx + 1}: 动力" : $"点{fIdx + 1}: TCM");
                        GUIStyle fbBtnStyle = (curFb == MGAFlybyStrategy.UnpoweredFlyby) ? secondaryButtonStyle : actionButtonStyle;

                        if (GUILayout.Button(fbLabel, fbBtnStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true))) {
                            var nextFb = (MGAFlybyStrategy)(((int)curFb + 1) % 3);
                            mgaConfig.FlybyStrategies[fIdx] = nextFb;
                            RefreshMgaCanvas();
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();
            // =========================================================================
            // 模式 2: 任务序列编辑工作台 (已在 DrawTabMGA 顶层分流直接渲染 DrawTabMGASequence)
            // =========================================================================

            GUILayout.Space(6);

            // 6. 出发与到达参数设置
            bool isDirectRoute = (mgaConfig.FlybySequence == null || mgaConfig.FlybySequence.Count <= 1);
            GUILayout.BeginHorizontal();
            string sectionTitle = isDirectRoute ? "<color=#38bdf8><b>转移轨道与动力学约束条件:</b></color>" : "<color=#38bdf8><b>借力航线与动力学约束条件:</b></color>";
            GUILayout.Label(sectionTitle, sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_opt_window", "⚡ 预估霍曼窗口"), actionButtonStyle, GUILayout.Height(20))) {
                double startUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
                if (startUT <= 0) startUT = Planetarium.GetUniversalTime();
                string targetBody = (mgaConfig.FlybySequence != null && mgaConfig.FlybySequence.Count > 0) ? mgaConfig.FlybySequence[0] : mgaConfig.DestinationBody;
                SolarSystemCatalog.EnsureAllPlanetsCached();
                if (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit && FlightGlobals.ActiveVessel != null) {
                    GMATStateManager.Current.SyncFromVessel(FlightGlobals.ActiveVessel);
                    GMATStateManager.Current.SyncToMGAConfig(mgaConfig);
                }
                double targetInc = (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaConfig.LockDepartureInclination) ? mgaConfig.DepartureInclinationDeg : -1.0;
                double targetRaan = (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaConfig.LockDepartureRAAN) ? mgaConfig.DepartureRAANDeg : -1.0;
                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, targetBody, startUT, 0, targetInc, targetRaan);
                if (double.IsNaN(optUT)) {
                    ScreenMessages.PostScreenMessage(I18n.T("mga_native_unavailable"), 4.5f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 20.0);
                    mgaConfig.LatestDepartureUT = optUT + 86400.0 * 20.0;
                    mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
                    mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
                    
                    double minBound = (tofDays < 15.0) ? Math.Max(0.5, tofDays * 0.5) : Math.Max(20.0, tofDays * 0.8);
                    double maxBound = (tofDays < 15.0) ? Math.Max(minBound + 2.0, tofDays * 2.0) : (tofDays * 1.25);
                    
                    if (mgaConfig.MinToFDays == null || mgaConfig.MinToFDays.Count == 0) {
                        mgaConfig.MinToFDays = new List<double> { minBound };
                        mgaConfig.MaxToFDays = new List<double> { maxBound };
                    } else {
                        mgaConfig.MinToFDays[0] = minBound;
                        mgaConfig.MaxToFDays[0] = maxBound;
                    }
                    
                    RefreshMgaCanvas();
                    ScreenMessages.PostScreenMessage(I18n.Format("mga_window_recommended", GMATClient.FormatUtcGregorian(optUT), approxDV, tofDays), 4.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUILayout.EndHorizontal();

            // 动力学求解引擎模式选择栏 (Astrodynamic Engine Dynamic Selector)
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.Label(I18n.T("mga_engine_header"), labelValueStyle);
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUIStyle e0Style = (mgaConfig.EngineMode == MGAEngineMode.BallisticImpulsive) ? actionButtonStyle : secondaryButtonStyle;
            GUIStyle e1Style = (mgaConfig.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM) ? actionButtonStyle : secondaryButtonStyle;
            GUIStyle e2Style = (mgaConfig.EngineMode == MGAEngineMode.LowThrustSimsFlanagan) ? actionButtonStyle : secondaryButtonStyle;

            if (GUILayout.Button(I18n.T("mga_engine_ballistic"), e0Style, GUILayout.Height(22), GUILayout.ExpandWidth(true))) {
                mgaConfig.EngineMode = MGAEngineMode.BallisticImpulsive;
                RefreshMgaCanvas();
            }
            if (GUILayout.Button(I18n.T("mga_engine_dsm"), e1Style, GUILayout.Height(22), GUILayout.ExpandWidth(true))) {
                mgaConfig.EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM;
                RefreshMgaCanvas();
            }
            if (GUILayout.Button(I18n.T("mga_engine_lowthrust"), e2Style, GUILayout.Height(22), GUILayout.ExpandWidth(true))) {
                mgaConfig.EngineMode = MGAEngineMode.LowThrustSimsFlanagan;
                RefreshMgaCanvas();
            }
            GUILayout.EndHorizontal();

            if (mgaConfig.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM) {
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Format("mga_engine_dsm_eta", mgaConfig.DsmEtaDefault), labelValueStyle, GUILayout.Width(170));
                mgaConfig.DsmEtaDefault = GUILayout.HorizontalSlider((float)mgaConfig.DsmEtaDefault, 0.1f, 0.9f, GUILayout.Height(18));
                GUILayout.EndHorizontal();
            } else if (mgaConfig.EngineMode == MGAEngineMode.LowThrustSimsFlanagan) {
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"推力: {mgaConfig.LowThrustMaxThrustN:F2} N", labelValueStyle, GUILayout.ExpandWidth(true));
                GUILayout.Label($"比冲: {mgaConfig.LowThrustIspSec:F0} s", labelValueStyle, GUILayout.ExpandWidth(true));
                GUILayout.Label($"湿重: {mgaConfig.SpacecraftWetMassKg:F0} kg", labelValueStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Space(3);

            // 动态天体力学特征计算 (100% 动力学解析，绝无硬编码)
            string targetRefBody = (mgaWorkflowMode == 0 || mgaConfig.FlybySequence == null || mgaConfig.FlybySequence.Count == 0) ? 
                mgaConfig.DestinationBody : 
                mgaConfig.FlybySequence[0];
            double hohDaysLive = PykepBridge.GetHohmannTransferTimeDays(mgaConfig.DepartureBody, targetRefBody);
            double synDaysLive = PykepBridge.GetSynodicPeriodDays(mgaConfig.DepartureBody, targetRefBody);

            GUILayout.BeginHorizontal(nestedCardBoxStyle);
            GUILayout.Label(I18n.ColorFormat("mga_astro_bench", mgaConfig.DepartureBody, targetRefBody, hohDaysLive, synDaysLive), labelValueStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_porkchop"), secondaryButtonStyle, GUILayout.Height(18))) {
                showPorkchopModal = true;
                StartPorkchopComputation(useCurrentDateInputs: true);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            DrawDateTimeInputField("mga_earliest_dep", "最早发射", mgaDepDateInput, mgaConfig.EarliestDepartureUT, "mga_earliest_dep", (str, ut) => {
                mgaDepDateInput = str;
                mgaConfig.EarliestDepartureUT = ut;
            });
            DrawDateTimeInputField("mga_latest_dep", "最晚发射", mgaEndDateInput, mgaConfig.LatestDepartureUT, "mga_latest_dep", (str, ut) => {
                mgaEndDateInput = str;
                mgaConfig.LatestDepartureUT = ut;
            });

            // 出发轨道模式选择器 (统一由 Widgets.cs 托管)
            DrawMGADepartureModeSelector(mgaConfig, SyncDepartureOrbitFromActiveVessel);

            // 目标到达捕获轨道 (PeA / ApA) - 严格遵循天体 SOI 与大气边界物理约束
            GUILayout.Space(2);
            MGADefaults.GetBodyCaptureAltitudeBounds(mgaConfig.DestinationBody, out double destMinPeA, out double destRecPeA, out double destMaxApA);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_arr_orbit"), labelValueStyle, GUILayout.Width(90));
            if (!mgaConfig.NoInsertionBurn) {
                GUILayout.Label("<color=#94a3b8>PeA:</color>", labelValueStyle, GUILayout.Width(30));
                string arrPeStr = GUILayout.TextField(mgaConfig.DestinationPeAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(50), GUILayout.Height(20));
                if (double.TryParse(arrPeStr, out double ape)) {
                    mgaConfig.DestinationPeAKm = Math.Max(destMinPeA, Math.Min(destMaxApA, ape));
                }

                GUILayout.Label("<color=#94a3b8>ApA:</color>", labelValueStyle, GUILayout.Width(30));
                string arrApStr = GUILayout.TextField(mgaConfig.DestinationApAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(50), GUILayout.Height(20));
                if (double.TryParse(arrApStr, out double aap)) {
                    mgaConfig.DestinationApAKm = Math.Max(mgaConfig.DestinationPeAKm, Math.Min(destMaxApA, aap));
                }
                GUILayout.Label("<color=#94a3b8>km</color>", labelValueStyle);
                GUILayout.Space(4);
                GUILayout.Label($"<color=#64748b>({I18n.Format("mga_capture_constraint_hint", destMinPeA, destMaxApA)})</color>", labelValueStyle);
            } else {
                GUILayout.Label(I18n.T("mga_flyby_only"), labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 目标捕获快捷设定按钮 (平分整行，绝不溢出)
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("btn_low_circular"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.ExpandWidth(true))) {
                mgaConfig.NoInsertionBurn = false;
                mgaConfig.DestinationPeAKm = destRecPeA;
                mgaConfig.DestinationApAKm = destRecPeA;
            }
            if (GUILayout.Button(I18n.T("btn_elliptic_capture"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.ExpandWidth(true))) {
                mgaConfig.NoInsertionBurn = false;
                mgaConfig.DestinationPeAKm = destRecPeA;
                var destP = SolarSystemCatalog.GetPlanet(mgaConfig.DestinationBody);
                double destR = destP != null ? destP.RadiusKm : 6000.0;
                double rp = destR + destRecPeA;
                // 椭圆捕获轨预设: 偏心率 e=0.95，远心距按解析式 ra = rp·(1+e)/(1-e) 计算，严格限制在 SOI 内 (<= destMaxApA)
                double ePreset = 0.95;
                double ra = rp * (1.0 + ePreset) / (1.0 - ePreset);
                mgaConfig.DestinationApAKm = Math.Min(destMaxApA, Math.Round(ra - destR));
            }
            if (GUILayout.Button(mgaConfig.NoInsertionBurn ? I18n.T("btn_flyby_only_on") : I18n.T("btn_flyby_only_toggle"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.ExpandWidth(true))) {
                mgaConfig.NoInsertionBurn = !mgaConfig.NoInsertionBurn;
            }
            GUILayout.EndHorizontal();

            // 多体引力流形 (WSB / Invariant Manifolds) 低能捕获优化确认按钮 (默认关闭，之后待定)
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            string manifoldBtnText = mgaConfig.EnableLowEnergyManifoldCapture 
                ? I18n.T("btn_wsb_manifold_on") 
                : I18n.T("btn_wsb_manifold_off");
            GUIStyle manifoldBtnStyle = mgaConfig.EnableLowEnergyManifoldCapture ? actionButtonStyle : secondaryButtonStyle;
            if (GUILayout.Button(manifoldBtnText, manifoldBtnStyle, GUILayout.Height(18), GUILayout.ExpandWidth(true))) {
                mgaConfig.EnableLowEnergyManifoldCapture = !mgaConfig.EnableLowEnergyManifoldCapture;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            mgaConfig.EnableMaxDuration = GUILayout.Toggle(mgaConfig.EnableMaxDuration, I18n.T("mga_max_duration"), GUILayout.Width(130));
            if (mgaConfig.EnableMaxDuration) {
                string durStr = GUILayout.TextField(mgaConfig.MaxDurationDays.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(60), GUILayout.Height(20));
                if (double.TryParse(durStr, out double dd)) mgaConfig.MaxDurationDays = dd;
                GUILayout.Label($"<color=#94a3b8>{I18n.T("mga_day_unit")}</color>", labelValueStyle);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool prevAlign = mgaConfig.EnableParkingOrbitPhaseAlignment;
            mgaConfig.EnableParkingOrbitPhaseAlignment = GUILayout.Toggle(
                mgaConfig.EnableParkingOrbitPhaseAlignment, 
                I18n.T("mga_toggle_phase_align")
            );
            if (mgaConfig.EnableParkingOrbitPhaseAlignment != prevAlign) {
                MGAStateManager.Current.AlignAllSolutions();
                RefreshMgaCanvas();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // 7. 寻优开始与停止按钮
            GUILayout.BeginHorizontal();
            if (!isMgaOptimizing) {
                bool isDirectSolve = (mgaConfig.FlybySequence == null || mgaConfig.FlybySequence.Count <= 1);
                string solveBtnText = isDirectSolve
                    ? $"🚀 开始求解转移轨道 ({mgaConfig.DepartureBody} ➔ {mgaConfig.DestinationBody})"
                    : $"🚀 开始求解借力航线 ({mgaConfig.DepartureBody} ➔ {string.Join(" ➔ ", mgaConfig.FlybySequence)})";
                if (GUILayout.Button(solveBtnText, actionButtonStyle, GUILayout.Height(30))) {
                    StartMgaOptimization();
                }
            } else {
                if (GUILayout.Button(I18n.T("mga_btn_stop"), deleteButtonStyle, GUILayout.Height(30), GUILayout.Width(130))) {
                    mgaCts?.Cancel();
                }
                GUILayout.Space(6);
                GUILayout.Label($"<color=#38bdf8><b>{mgaStatusText}</b></color>", labelValueStyle);
            }
            GUILayout.EndHorizontal();

            if (isMgaOptimizing) {
                GUILayout.Space(4);
                DrawProgressBar(mgaProgress, I18n.Format("mga_progress_label", mgaProgress * 100f), 18f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // =========================================================================
            // 右栏：日心 2D/3D 引力助推全景轨道可视化画布 (自适应宽度)
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.ExpandWidth(true));

            // 画布工具栏
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_canvas_title"), sectionHeaderStyle);
            GUILayout.FlexibleSpace();

            mgaShowSois = GUILayout.Toggle(mgaShowSois, I18n.T("mga_toggle_soi"));
            GUILayout.Space(6);
            mgaShowMetDates = GUILayout.Toggle(mgaShowMetDates, I18n.T("mga_toggle_met"));
            GUILayout.Space(6);

            string porkchopBtnText = showPorkchopModal ? I18n.T("mga_porkchop_toggle_close") : I18n.T("mga_porkchop_toggle_open");
            if (GUILayout.Button(porkchopBtnText, secondaryButtonStyle, GUILayout.Height(20))) {
                showPorkchopModal = !showPorkchopModal;
                if (showPorkchopModal) showBenchmarkModal = false;
                if (showPorkchopModal && (currentPorkchop == null || currentPorkchop.PlotTexture == null)) {
                    StartPorkchopComputation();
                }
            }
            GUILayout.Space(6);

            string benchBtnText = showBenchmarkModal ? I18n.T("mga_bench_toggle_close") : I18n.T("mga_bench_toggle_open");
            if (GUILayout.Button(benchBtnText, secondaryButtonStyle, GUILayout.Height(20))) {
                showBenchmarkModal = !showBenchmarkModal;
                if (showBenchmarkModal) {
                    showPorkchopModal = false;
                    RunHistoricalBenchmarks();
                }
            }
            GUILayout.Space(6);

            // 预设视点快速切换 (顶视 / 3D 透视 / 黄道面侧视 / 重置)
            if (GUILayout.Button(I18n.T("btn_top_view"), (Mathf.Abs(mgaPitch - 89.9f) < 2f) ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                mgaPitch = 89.9f;
                mgaYaw = 0.0f;
                RefreshMgaCanvas();
            }
            GUILayout.Space(4);

            if (GUILayout.Button(I18n.T("btn_3d_view"), (Mathf.Abs(mgaPitch - 50.0f) < 3f && Mathf.Abs(mgaYaw - (-25.0f)) < 5f) ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                mgaPitch = 50.0f;
                mgaYaw = -25.0f;
                RefreshMgaCanvas();
            }
            GUILayout.Space(4);

            if (GUILayout.Button(I18n.T("btn_ecliptic_view"), (Mathf.Abs(mgaPitch) < 2f) ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                mgaPitch = 0.0f;
                mgaYaw = 0.0f;
                RefreshMgaCanvas();
            }
            GUILayout.Space(6);

            if (GUILayout.Button(I18n.T("btn_center_reset"), secondaryButtonStyle, GUILayout.Height(20))) {
                mgaCanvasZoom = 1.0f;
                mgaCanvasPan = Vector2.zero;
                mgaPitch = 50.0f;
                mgaYaw = -25.0f;
                RefreshMgaCanvas();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // =========================================================================
            // 画布、猪排图与历史基准主区域 (支持 3D 自由旋转、猪排图与自适应基准测试)
            // =========================================================================
            if (showBenchmarkModal) {
                DrawBenchmarkModal();
            } else if (showPorkchopModal) {
                DrawPorkchopView();
            } else {
                DrawInteractiveMgaCanvas();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // =========================================================================
            // 底部面板：轨道方案解算明细与机动矢量 (Calculated Trajectory)
            // =========================================================================
            if (selectedMgaSolution != null) {
                GUILayout.BeginVertical(cardBoxStyle);

                float bottomAvailW = windowRect.width - 40f;
                float leftColW = Mathf.Clamp((bottomAvailW - 14f) * 0.49f, 480f, 750f);
                float rightColW = Mathf.Max(460f, bottomAvailW - 14f - leftColW);

                GUILayout.BeginHorizontal();

                // =====================================================================
                // 左半边：方案物理详情、动力学优化看板、机动节点与操作工具栏
                // =====================================================================
                GUILayout.BeginVertical(GUILayout.Width(leftColW));

                // 1. 顶部方案极值与模式 (最小 ΔV / 最快到达 / 物理与数据切换)
                var minSol = (mgaSolutions != null && mgaSolutions.Count > 0) ? mgaSolutions.OrderBy(s => s.TotalDeltaVKms).FirstOrDefault() : null;
                var fastSol = (mgaSolutions != null && mgaSolutions.Count > 0) ? mgaSolutions.OrderBy(s => s.TotalFlightDays).FirstOrDefault() : null;

                GUILayout.BeginHorizontal();
                if (minSol != null && fastSol != null && minSol == fastSol) {
                    bool isMin = (selectedMgaSolution == minSol);
                    string combinedText = $"🏆 最优方案 ({minSol.TotalDeltaVKms * 1000.0:F0} m/s, {minSol.TotalFlightDays:F0}天)";
                    if (GUILayout.Button(combinedText, isMin ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                        MGAStateManager.Current.NotifySolutionChanged(minSol);
                        mgaParetoSliderVal = 0f;
                        RefreshMgaCanvas();
                    }
                } else {
                    if (minSol != null) {
                        bool isMin = (selectedMgaSolution == minSol);
                        string minText = $"🏆 最小 ΔV ({minSol.TotalDeltaVKms * 1000.0:F0} m/s, {minSol.TotalFlightDays:F0}d)";
                        if (GUILayout.Button(minText, isMin ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                            MGAStateManager.Current.NotifySolutionChanged(minSol);
                            mgaParetoSliderVal = 0f;
                            RefreshMgaCanvas();
                        }
                    }
                    GUILayout.Space(4);
                    if (fastSol != null) {
                        bool isFast = (selectedMgaSolution == fastSol);
                        string fastText = $"⚡ 最快到达 ({fastSol.TotalDeltaVKms * 1000.0:F0} m/s, {fastSol.TotalFlightDays:F0}d)";
                        if (GUILayout.Button(fastText, isFast ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                            MGAStateManager.Current.NotifySolutionChanged(fastSol);
                            var baseP = (mgaSolutions != null && mgaSolutions.Count > 0 && mgaSolutions[0].ParetoFront != null) ? mgaSolutions[0].ParetoFront : selectedMgaSolution.ParetoFront;
                            if (baseP != null && baseP.Count > 0) {
                                mgaParetoSliderVal = (float)(baseP.Count - 1);
                            }
                            RefreshMgaCanvas();
                        }
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(mgaDetailViewMode == 0 ? I18n.T("mga_detail_phys_active") : I18n.T("mga_detail_phys"), secondaryButtonStyle, GUILayout.Height(20))) mgaDetailViewMode = 0;
                GUILayout.Space(3);
                if (GUILayout.Button(mgaDetailViewMode == 1 ? I18n.T("mga_detail_data_active") : I18n.T("mga_detail_data"), secondaryButtonStyle, GUILayout.Height(20))) mgaDetailViewMode = 1;
                GUILayout.Space(6);
                if (GUILayout.Button("🔍 方案全量明细", actionButtonStyle, GUILayout.Height(20))) {
                    detailModalSolution = selectedMgaSolution;
                    showSolutionDetailModal = true;
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // 2. 转移航线与几何出射/捕获参数明细卡片
                var leg0 = selectedMgaSolution.Legs[0];
                double travelDays = selectedMgaSolution.TotalFlightDays;
                double totalSecs = travelDays * 86400.0;
                int tDays = (int)travelDays;
                int tHours = (int)((totalSecs % 86400.0) / 3600.0);
                int tMins = (int)((totalSecs % 3600.0) / 60.0);
                int tSecs = (int)(totalSecs % 60.0);
                string depStr = GMATClient.FormatUtcGregorian(selectedMgaSolution.DepartureUT);
                string arrStr = GMATClient.FormatUtcGregorian(selectedMgaSolution.ArrivalUT);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.ColorFormat("mga_transfer_header", leg0.FromBody, mgaConfig.DepartureAltitudeKm, selectedMgaSolution.Legs.Last().ToBody, mgaConfig.DestinationAltitudeKm), labelValueStyle);
                GUILayout.Space(2);

                // Row 1: 出发与到达
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_dep_col", depStr), labelValueStyle, GUILayout.Width(220));
                GUILayout.Label(I18n.ColorFormat("mga_arr_col", arrStr), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: 历时与总机动
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_travel_col", tDays, tHours, tMins, tSecs), labelValueStyle, GUILayout.Width(220));
                GUILayout.Label(I18n.ColorFormat("mga_totaldv_col", selectedMgaSolution.TotalDeltaVKms * 1000.0), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 3: 相位角与射出角
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_phase_col", leg0.PhaseAngleDeg), labelValueStyle, GUILayout.Width(220));
                GUILayout.Label(I18n.ColorFormat("mga_ejec_col", leg0.EjectionAngleDeg), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 4: 模式 1 / 模式 2 约束
                GUILayout.BeginHorizontal();
                string ejectDvStr = I18n.ColorFormat("mga_ejectdv_col", leg0.EjectionDeltaVKms * 1000.0);
                if (selectedMgaSolution.IsPhaseAligned && selectedMgaSolution.OriginalDepartureDeltaVKms > leg0.EjectionDeltaVKms + 0.001) {
                    double savedDv = (selectedMgaSolution.OriginalDepartureDeltaVKms - leg0.EjectionDeltaVKms) * 1000.0;
                    ejectDvStr += $"  <color=#34d399><b>(省 {savedDv:F0} m/s)</b></color>";
                }
                if (selectedMgaSolution.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit) {
                    GUILayout.Label(string.Format("<color=#34d399><b>💡 模式 1: Inc={0:F1}°, LAN={1:F1}°</b></color>", 
                        selectedMgaSolution.RecommendedLaunchIncDeg > 0 ? selectedMgaSolution.RecommendedLaunchIncDeg : leg0.EjectionInclinationDeg,
                        selectedMgaSolution.RecommendedLaunchRaanDeg > 0 ? selectedMgaSolution.RecommendedLaunchRaanDeg : leg0.EjectionLANDeg), labelValueStyle, GUILayout.Width(220));
                } else {
                    GUILayout.Label(string.Format("<color=#38bdf8><b>🔒 模式 2: Inc={0:F1}°, LAN={1:F1}° (已锁定)</b></color>",
                        mgaConfig.DepartureInclinationDeg, mgaConfig.DepartureRAANDeg), labelValueStyle, GUILayout.Width(220));
                }
                GUILayout.Label(ejectDvStr, labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 5: C3, 捕获倾角, 捕获 ΔV
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_c3_col", leg0.C3Kms2), labelValueStyle, GUILayout.Width(130));
                GUILayout.Label(I18n.ColorFormat("mga_capinc_col", leg0.InsertionInclinationDeg), labelValueStyle, GUILayout.Width(130));
                GUILayout.Label(I18n.ColorFormat("mga_capdv_col", selectedMgaSolution.InsertionDeltaVKms * 1000.0), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();

                // 3. 动力学优化与核验看板 (两行紧凑布局)
                GUILayout.Space(3);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>动力学核验:</b></color>", labelValueStyle, GUILayout.Width(75));
                GUILayout.Label("<color=#34d399><b>⚡ MEE外推</b></color>", labelValueStyle, GUILayout.Width(75));
                if (selectedMgaSolution.PrimerVector != null) {
                    var pv = selectedMgaSolution.PrimerVector;
                    string pvTag = pv.IsOptimal ? "<color=#34d399><b>✅ 劳登最优</b></color>" : $"<color=#facc15><b>⚠️ 劳登极值 (|p|={pv.MaxPMag:F2})</b></color>";
                    GUILayout.Label(pvTag, labelValueStyle, GUILayout.Width(95));
                } else {
                    GUILayout.Label("<color=#34d399><b>✅ 劳登最优</b></color>", labelValueStyle, GUILayout.Width(80));
                }
                if (selectedMgaSolution.ParetoFront != null && selectedMgaSolution.ParetoFront.Count > 0) {
                    GUILayout.Label($"<color=#38bdf8><b>📊 帕累托 ({selectedMgaSolution.ParetoFront.Count}点)</b></color>", labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (selectedMgaSolution.BPlane != null) {
                    var bp = selectedMgaSolution.BPlane;
                    GUILayout.Label($"<color=#38bdf8><b>🎯 B平面锁定 (V∞={bp.VInfMagKms:F2}k/s)</b></color>", labelValueStyle, GUILayout.Width(185));
                } else {
                    GUILayout.Label("<color=#94a3b8><b>🎯 朗伯轨道交会</b></color>", labelValueStyle, GUILayout.Width(110));
                }
                if (mgaConfig.EngineMode == MGAEngineMode.LowThrustSimsFlanagan && selectedMgaSolution.LowThrustSolution != null) {
                    GUILayout.Label("<color=#facc15><b>🚀 低推力收敛</b></color>", labelValueStyle, GUILayout.Width(85));
                }
                GUILayout.FlexibleSpace();

                string alignBtnText = mgaConfig.EnableParkingOrbitPhaseAlignment
                    ? (selectedMgaSolution.IsPhaseAligned 
                        ? I18n.Format("mga_phase_align_status_on", selectedMgaSolution.PhaseAlignmentShiftSec / 60.0) 
                        : I18n.T("mga_phase_align_pending"))
                    : I18n.T("mga_phase_align_status_off");
                if (GUILayout.Button(alignBtnText, secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(150))) {
                    mgaConfig.EnableParkingOrbitPhaseAlignment = !mgaConfig.EnableParkingOrbitPhaseAlignment;
                    MGAStateManager.Current.AlignAllSolutions();
                    RefreshMgaCanvas();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                // 4. 机动节点选择器与明细
                GUILayout.Space(3);
                if (mgaDetailViewMode == 0) {
                    if (selectedMgaSolution.Events == null || selectedMgaSolution.Events.Count == 0) {
                        selectedMgaSolution.BuildEvents(mgaConfig);
                    }
                    int eventCount = (selectedMgaSolution.Events != null) ? selectedMgaSolution.Events.Count : 0;
                    if (selectedManeuverIndex < 0 || selectedManeuverIndex >= eventCount) selectedManeuverIndex = 0;

                    GUILayout.BeginHorizontal(nestedCardBoxStyle);
                    GUILayout.Label(I18n.T("mga_maneuvers_label"), labelValueStyle, GUILayout.Width(65));
                    if (eventCount > 0) {
                        var currentEvt = selectedMgaSolution.Events[selectedManeuverIndex];
                        string ddTitle = $"{currentEvt.Title}  {(showManeuverDropdown ? "▲" : "▼")}";
                        if (GUILayout.Button(ddTitle, actionButtonStyle, GUILayout.Width(160), GUILayout.Height(22))) {
                            showManeuverDropdown = !showManeuverDropdown;
                        }
                        if (GUILayout.Button("◀", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(22))) {
                            if (selectedManeuverIndex > 0) {
                                selectedManeuverIndex--;
                                FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                            }
                            showManeuverDropdown = false;
                        }
                        if (GUILayout.Button("▶", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(22))) {
                            if (selectedManeuverIndex < eventCount - 1) {
                                selectedManeuverIndex++;
                                FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                            }
                            showManeuverDropdown = false;
                        }
                        GUILayout.Space(3);
                        if (GUILayout.Button(I18n.T("btn_focus_view"), actionButtonStyle, GUILayout.Width(65), GUILayout.Height(22))) {
                            FocusOnSelectedManeuver(autoCenter: true);
                        }
                        mgaAutoFocusEnabled = GUILayout.Toggle(mgaAutoFocusEnabled, I18n.T("mga_auto_lock"), GUILayout.Width(68));
                        GUILayout.FlexibleSpace();
                        string badgeText = currentEvt.EventType switch {
                            MGAEventType.Escape => I18n.T("mga_badge_escape"),
                            MGAEventType.Flyby => I18n.T("mga_badge_flyby"),
                            MGAEventType.DSM => I18n.T("mga_badge_dsm"),
                            MGAEventType.Circularization => I18n.T("mga_badge_capture"),
                            _ => ""
                        };
                        if (!string.IsNullOrEmpty(badgeText)) GUILayout.Label(badgeText, labelValueStyle);
                    } else {
                        GUILayout.Label(I18n.T("mga_no_events"), labelValueStyle);
                    }
                    GUILayout.EndHorizontal();

                    // 下拉选项菜单 (若展开)
                    if (showManeuverDropdown && eventCount > 0) {
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        for (int i = 0; i < selectedMgaSolution.Events.Count; i++) {
                            var ev = selectedMgaSolution.Events[i];
                            bool isCurrent = (i == selectedManeuverIndex);
                            string itemText = isCurrent ? $"<b>▶  {ev.Title}</b>" : $"   {ev.Title}";
                            GUIStyle itemStyle = isCurrent ? tabButtonActiveStyle : secondaryButtonStyle;
                            if (GUILayout.Button(itemText, itemStyle, GUILayout.Height(20))) {
                                selectedManeuverIndex = i;
                                showManeuverDropdown = false;
                                FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                            }
                        }
                        GUILayout.EndVertical();
                    }

                    // 选中事件明细
                    if (eventCount > 0 && selectedManeuverIndex >= 0 && selectedManeuverIndex < eventCount) {
                        var curEv = selectedMgaSolution.Events[selectedManeuverIndex];
                        string dateDisplay = mgaShowMetDates ? 
                            $"MET +{(curEv.EpochUT - selectedMgaSolution.DepartureUT) / 86400.0:F1} {I18n.T("mga_day_unit")}" : 
                            GMATClient.FormatUtcGregorian(curEv.EpochUT);

                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        if (curEv.EventType == MGAEventType.Escape) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_escape_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(250));
                            GUILayout.Label(I18n.ColorFormat("mga_escape_dv", curEv.DeltaVMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_vec3", curEv.ProgradeMs, curEv.NormalMs, curEv.RadialMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_escape_geo", curEv.EjectionAngleDeg, curEv.EjectionInclinationDeg, curEv.EjectionLANDeg, curEv.C3Kms2), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        } else if (curEv.EventType == MGAEventType.Flyby) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(250));
                            string flybyTypeStr = (curEv.DeltaVMs > 0.001) ? 
                                I18n.Format("mga_flyby_powered", curEv.DeltaVMs) : 
                                I18n.T("mga_flyby_ballistic");
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_nature", flybyTypeStr), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_vinf", curEv.VInfInKms, curEv.VInfOutKms, curEv.IsFeasible ? I18n.T("mga_feasible_ok") : I18n.T("mga_feasible_power")), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_geo", curEv.FlybyPeriapsisAltitudeKm, curEv.FlybyPeriapsisRadiusKm, curEv.FlybyTurningAngleDeg), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        } else if (curEv.EventType == MGAEventType.DSM) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_dsm_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(250));
                            GUILayout.Label(I18n.ColorFormat("mga_dsm_dv", curEv.DeltaVMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_vec3", curEv.ProgradeMs, curEv.NormalMs, curEv.RadialMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        } else if (curEv.EventType == MGAEventType.Circularization) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_arrival_time", dateDisplay), labelValueStyle, GUILayout.Width(250));
                            GUILayout.Label(I18n.ColorFormat("mga_capdv_col", curEv.DeltaVMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_capinc_col", curEv.EjectionInclinationDeg), labelValueStyle, GUILayout.Width(160));
                            GUILayout.Label(I18n.ColorFormat("mga_transfer_dest_alt", mgaConfig.DestinationAltitudeKm), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                    }
                } else {
                    GUILayout.BeginHorizontal(nestedCardBoxStyle);
                    GUILayout.Label(I18n.ColorFormat("mga_data_summary", selectedMgaSolution.FlybyChainSummary, selectedMgaSolution.TotalFlightDays, selectedMgaSolution.LaunchDeltaVKms * 1000.0, selectedMgaSolution.InsertionDeltaVKms * 1000.0), labelValueStyle);
                    GUILayout.EndHorizontal();
                }

                // 5. 底部操作按钮群
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("btn_load_sequence"), actionButtonStyle, GUILayout.Height(24))) {
                    LoadMgaSolutionToSequence(selectedMgaSolution, vessel);
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_export_preset"), secondaryButtonStyle, GUILayout.Height(24))) {
                    string path = GMATMGAScriptGenerator.ExportToTemplatesDirectory(selectedMgaSolution);
                    if (!string.IsNullOrEmpty(path)) {
                        ScreenMessages.PostScreenMessage(I18n.Format("mga_export_ok", Path.GetFileName(path)), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_upload_principia"), secondaryButtonStyle, GUILayout.Height(24))) {
                    UploadMgaSolutionToPrincipia(selectedMgaSolution);
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_review_script"), secondaryButtonStyle, GUILayout.Height(24))) {
                    ideCustomBuffer = GMATMGAScriptGenerator.CompileToGmatScript(selectedMgaSolution);
                    activeTab = 1; ideScriptViewMode = 1;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("btn_export_script"), actionButtonStyle, GUILayout.Height(22))) {
                    try {
                        string scriptText = GenerateGmatScriptForMGA(selectedMgaSolution);
                        GUIUtility.systemCopyBuffer = scriptText;

                        string root = "";
                        try { root = KSPUtil.ApplicationRootPath; } catch { }
                        if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;
                        string dir = Path.Combine(root, "GameData", "KSPGMAT", "Scripts");
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        string cleanSummary = selectedMgaSolution.FlybyChainSummary.Replace(" -> ", "_").Replace(" ", "");
                        string fileName = $"GMAT_MGA_{cleanSummary}_{DateTime.Now:yyyyMMdd_HHmmss}.script";
                        string fullPath = Path.Combine(dir, fileName);
                        File.WriteAllText(fullPath, scriptText);
                        ScreenMessages.PostScreenMessage(I18n.Format("mga_script_saved", fileName), 4.5f, ScreenMessageStyle.UPPER_CENTER);
                    } catch (Exception ex) {
                        ScreenMessages.PostScreenMessage(I18n.Format("mga_script_export_fail", ex.Message), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_copy_report"), secondaryButtonStyle, GUILayout.Height(22))) {
                    string report = GenerateDetailedMgaReport(selectedMgaSolution);
                    GUIUtility.systemCopyBuffer = report;
                    ScreenMessages.PostScreenMessage(I18n.T("mga_report_copied"), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
                if (GUILayout.Button(I18n.T("btn_save_report"), secondaryButtonStyle, GUILayout.Height(24))) {
                    try {
                        string report = GenerateDetailedMgaReport(selectedMgaSolution);
                        string root = "";
                        try { root = KSPUtil.ApplicationRootPath; } catch { }
                        if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;
                        string dir = Path.Combine(root, "GameData", "KSPGMAT", "Reports");
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        string cleanSummary = selectedMgaSolution.FlybyChainSummary.Replace(" -> ", "_").Replace(" ", "");
                        string fileName = $"MGA_Report_{cleanSummary}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                        string fullPath = Path.Combine(dir, fileName);
                        File.WriteAllText(fullPath, report);
                        ScreenMessages.PostScreenMessage(I18n.Format("mga_report_saved", fileName), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                    } catch (Exception ex) {
                        ScreenMessages.PostScreenMessage(I18n.Format("mga_report_save_fail", ex.Message), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                GUILayout.Space(8);
                if (GUILayout.Button(showMgaReportModal ? I18n.T("btn_report_toggle_close") : I18n.T("btn_report_toggle_open"), secondaryButtonStyle, GUILayout.Height(24))) {
                    showMgaReportModal = !showMgaReportModal;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (showMgaReportModal) {
                    GUILayout.Space(6);
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("mga_report_header"), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(I18n.T("btn_copy_all"), secondaryButtonStyle, GUILayout.Height(18))) {
                        GUIUtility.systemCopyBuffer = GenerateDetailedMgaReport(selectedMgaSolution);
                        ScreenMessages.PostScreenMessage(I18n.T("mga_report_copied2"), 2.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    if (GUILayout.Button(I18n.T("mga_report_close"), secondaryButtonStyle, GUILayout.Height(18))) {
                        showMgaReportModal = false;
                    }
                    GUILayout.EndHorizontal();

                    mgaReportScrollPos = GUILayout.BeginScrollView(mgaReportScrollPos, GUILayout.Height(180));
                    string reportText = GenerateDetailedMgaReport(selectedMgaSolution);
                    GUILayout.TextArea(reportText, labelValueStyle);
                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();
                }

                GUILayout.EndVertical(); // 闭合左半边 leftColW

                GUILayout.Space(10);

                // =====================================================================
                // 右半边：多方案决策池列表与排名表 (Solution Ranking Table)
                // =====================================================================
                GUILayout.BeginVertical(GUILayout.Width(rightColW));
                if (mgaSolutions != null && mgaSolutions.Count > 0) {
                    DrawMGASolutionTable(mgaSolutions, selectedMgaSolution, (sol) => {
                        MGAStateManager.Current.NotifySolutionChanged(sol);
                        RefreshMgaCanvas();
                    });
                } else {
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.Label("<b>🏆 候选转移方案决策池</b>", sectionHeaderStyle);
                    GUILayout.Space(20);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("<color=#94a3b8>暂无已求解的多候选方案，请点击上方开始寻优</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(20);
                    GUILayout.EndVertical();
                }
                GUILayout.EndVertical(); // 闭合右半边 rightColW

                GUILayout.EndHorizontal(); // 闭合底栏左右双列水平布局
                GUILayout.EndVertical(); // 闭合 cardBoxStyle
            }

            GUILayout.EndScrollView();
        }

        private async void StartMgaOptimization() {
            if (string.IsNullOrEmpty(mgaConfig.DepartureBody) || string.IsNullOrEmpty(mgaConfig.DestinationBody)) {
                ScreenMessages.PostScreenMessage(I18n.T("mga_err_same_dep_dest"), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            SolarSystemCatalog.EnsureAllPlanetsCached();

            // 严格解析并同步 UI 文本框中的最早与最晚发射时间约束 (SSOT)
            MGAStateManager.Current.SyncDepartureDatesFromInputs();

            if (mgaConfig.FlybySequence == null || mgaConfig.FlybySequence.Count == 0) {
                mgaConfig.FlybySequence = new List<string> { mgaConfig.DestinationBody };
                customChainInput = mgaConfig.DestinationBody;
            }

            if (mgaConfig.DepartureBody.Equals(mgaConfig.DestinationBody, StringComparison.OrdinalIgnoreCase) && 
                mgaConfig.FlybySequence.Count <= 1) {
                ScreenMessages.PostScreenMessage(I18n.T("mga_err_same_dep_dest"), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // 若用户尚未设定有效时间窗口，自动执行一次霍曼窗口估算
            if (mgaConfig.EarliestDepartureUT <= 0 || mgaConfig.LatestDepartureUT <= mgaConfig.EarliestDepartureUT) {
                double startUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
                if (startUT <= 0) startUT = Planetarium.GetUniversalTime() > 0 ? Planetarium.GetUniversalTime() : 0.0;
                string firstTarget = mgaConfig.FlybySequence[0];
                if (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit && FlightGlobals.ActiveVessel != null) {
                    GMATStateManager.Current.SyncFromVessel(FlightGlobals.ActiveVessel);
                    GMATStateManager.Current.SyncToMGAConfig(mgaConfig);
                }
                double targetInc = (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaConfig.LockDepartureInclination) ? mgaConfig.DepartureInclinationDeg : -1.0;
                double targetRaan = (mgaConfig.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaConfig.LockDepartureRAAN) ? mgaConfig.DepartureRAANDeg : -1.0;
                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, firstTarget, startUT, 0, targetInc, targetRaan);
                if (!double.IsNaN(optUT) && tofDays > 0) {
                    double windowHalfSpan = (tofDays < 15.0) ? Math.Max(2.0, tofDays * 1.5) : 25.0;
                    mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * windowHalfSpan);
                    mgaConfig.LatestDepartureUT = optUT + 86400.0 * windowHalfSpan;
                    mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
                    mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
                    double minTof = (tofDays < 15.0) ? Math.Max(0.5, tofDays * 0.6) : Math.Max(15.0, tofDays * 0.75);
                    mgaConfig.MinToFDays = new List<double> { minTof };
                    mgaConfig.MaxToFDays = new List<double> { Math.Max(minTof * 1.5, tofDays * 1.35) };
                }
            }

            isMgaOptimizing = true;
            mgaProgress = 0.05f;
            mgaStatusText = I18n.T("mga_status_start");
            mgaCts = new CancellationTokenSource();

            UnityEngine.Debug.Log($"[KSPGMAT-MGA] >>> 启动 MGA 优化寻优: {mgaConfig.DepartureBody} -> {mgaConfig.DestinationBody}, 序列=[{string.Join(" -> ", mgaConfig.FlybySequence ?? new List<string>())}], 模式={mgaWorkflowMode}, 引擎={mgaConfig.EngineMode}, 约束窗口=[{GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT)} ~ {GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT)}]");

            try {
                var res = await MGAAdaptiveSolver.OptimizeAsync(mgaConfig, (p, msg) => {
                    mgaProgress = p;
                    mgaStatusText = msg;
                }, mgaCts.Token);

                mgaSolutions = res?.OrderBy(s => s.TotalDeltaVKms).ToList();
                if (mgaSolutions != null && mgaSolutions.Count > 0) {
                    selectedMgaSolution = mgaSolutions[0];

                    // 自动对齐停泊轨道出射点火相位 (彻底消除因飞船轨道相位漂移导致的脱靶偏差)
                    if (mgaConfig.EnableParkingOrbitPhaseAlignment) {
                        MGAStateManager.Current.AlignAllSolutions();
                        if (mgaSolutions != null && mgaSolutions.Count > 0) {
                            mgaSolutions = mgaSolutions.OrderBy(s => s.TotalDeltaVKms).ToList();
                            selectedMgaSolution = mgaSolutions[0];
                        }
                    }

                    selectedManeuverIndex = 0;
                    MGAStateManager.Current.NotifySolutionChanged(selectedMgaSolution);
                    mgaStatusText = I18n.Format("mga_status_done", mgaSolutions.Count);
                    UnityEngine.Debug.Log($"[KSPGMAT-MGA] <<< 寻优计算完成: 获得 {mgaSolutions.Count} 个收敛解, 最优解总ΔV={selectedMgaSolution.TotalDeltaVKms * 1000.0:F1} m/s, 飞行={selectedMgaSolution.TotalFlightDays:F1}天, 出发={GMATClient.FormatUtcGregorian(selectedMgaSolution.DepartureUT)}, 到达={GMATClient.FormatUtcGregorian(selectedMgaSolution.ArrivalUT)}");
                    RefreshMgaCanvas();
                    ScreenMessages.PostScreenMessage(I18n.Format("mga_solve_done", selectedMgaSolution.TotalDeltaVKms * 1000.0), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    UnityEngine.Debug.LogWarning($"[KSPGMAT-MGA] <<< 寻优计算完成: 未找到可行收敛解");
                    mgaStatusText = I18n.T("mga_status_no_sol");
                }
            } catch (Exception ex) {
                UnityEngine.Debug.LogError($"[KSPGMAT-MGA] <<< 寻优异常: {ex}");
                mgaStatusText = I18n.Format("mga_status_exception", ex.Message);
            } finally {
                isMgaOptimizing = false;
            }
        }

        private async void RunMultiSequenceOptimization() {
            if (isMgaOptimizing) return;
            if (string.IsNullOrEmpty(mgaConfig.DepartureBody) || string.IsNullOrEmpty(mgaConfig.DestinationBody)) return;

            isMgaOptimizing = true;
            mgaProgress = 0.05f;
            mgaStatusText = I18n.T("mga_status_multiseq_start", "正在启动多序列智能批量寻优与自适应 8 大动力学闭环...");
            mgaCts = new CancellationTokenSource();

            try {
                var res = await MGAAdaptiveSolver.AutoOptimizeCandidateSequencesAsync(mgaConfig, (p, msg) => {
                    mgaProgress = p;
                    mgaStatusText = msg;
                }, mgaCts.Token);

                mgaSolutions = res?.OrderBy(s => s.TotalDeltaVKms).ToList();
                if (mgaSolutions != null && mgaSolutions.Count > 0) {
                    selectedMgaSolution = mgaSolutions[0];

                    // 自动将界面序列同步为最优解的序列
                    if (selectedMgaSolution.Legs != null && selectedMgaSolution.Legs.Count > 0) {
                        mgaConfig.FlybySequence = selectedMgaSolution.Legs.Select(l => l.ToBody).ToList();
                        customChainInput = string.Join(",", mgaConfig.FlybySequence);
                    }

                    if (mgaConfig.EnableParkingOrbitPhaseAlignment) {
                        MGAStateManager.Current.AlignAllSolutions();
                        if (mgaSolutions != null && mgaSolutions.Count > 0) {
                            mgaSolutions = mgaSolutions.OrderBy(s => s.TotalDeltaVKms).ToList();
                            selectedMgaSolution = mgaSolutions[0];
                        }
                    }

                    selectedManeuverIndex = 0;
                    MGAStateManager.Current.NotifySolutionChanged(selectedMgaSolution);
                    mgaStatusText = I18n.Format("mga_status_done", mgaSolutions.Count);
                    RefreshMgaCanvas();
                    ScreenMessages.PostScreenMessage($"多序列寻优完成! 最优方案总 ΔV = {selectedMgaSolution.TotalDeltaVKms * 1000.0:F1} m/s", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    mgaStatusText = I18n.T("mga_status_no_sol");
                }
            } catch (Exception ex) {
                UnityEngine.Debug.LogError($"[KSPGMAT-MGA] <<< 多序列寻优异常: {ex}");
                mgaStatusText = I18n.Format("mga_status_exception", ex.Message);
            } finally {
                isMgaOptimizing = false;
            }
        }

        private void LoadMgaSolutionToSequence(MGASolution sol, Vessel vessel) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return;

            missionSequence.Clear();

            for (int k = 0; k < sol.Legs.Count; k++) {
                var leg = sol.Legs[k];

                // 1. 写入精确 3 轴机动矢量步骤 (Maneuver Step)
                if (k == 0 || leg.FlybyDeltaVKms > 0.001) {
                    missionSequence.Add(new MissionStep {
                        type = "Maneuver",
                        dv_v = leg.ProgradeMs,
                        dv_n = leg.NormalMs,
                        dv_b = leg.RadialMs,
                        burn_coordinate_system = "Local",
                        burn_axes = "VNC",
                        label = (k == 0) ? 
                            I18n.Format("mga_label_escape", leg.FromBody, leg.DeltaVKms * 1000.0, leg.EjectionAngleDeg) : 
                            I18n.Format("mga_label_flyby", k, leg.FromBody, leg.FlybyPeriapsisAltitudeKm, leg.FlybyDeltaVKms * 1000.0)
                    });
                }

                // 2. 写入深空引力转移推进步骤 (Propagate Step)
                missionSequence.Add(new MissionStep {
                    type = "Propagate",
                    propagator = "DeepSpaceProp",
                    stop_type = "ElapsedDays",
                    target_val = leg.ToFDays,
                    label = I18n.Format("mga_label_transfer", k + 1, leg.FromBody, leg.ToBody, leg.ToFDays)
                });
            }

            // 3. 目标行星捕获制动机动 (Destination Insertion Burn)
            if (sol.InsertionDeltaVKms > 0.001) {
                var lastLeg = sol.Legs.Last();
                missionSequence.Add(new MissionStep {
                    type = "Maneuver",
                    dv_v = -sol.InsertionDeltaVKms * 1000.0,
                    dv_n = 0.0,
                    dv_b = 0.0,
                    burn_coordinate_system = "Local",
                    burn_axes = "VNC",
                    label = I18n.Format("mga_label_capture", lastLeg.ToBody, sol.InsertionDeltaVKms * 1000.0)
                });
            }

            // 4. 同步至全局单一数据源 (GMATStateManager SSOT, 严格遵循 baseline.md 规范)
            if (GMATStateManager.Current != null && GMATStateManager.Current.Spacecraft != null) {
                GMATStateManager.Current.Spacecraft.EpochUtc = GMATClient.FormatUtcGregorian(sol.DepartureUT);
                GMATStateManager.Current.Spacecraft.CentralBody = sol.Legs[0].FromBody;
            }

            // 5. 同步至 MGA 专属任务控制序列工作台 (SSOT)
            MGAStateManager.Current.SyncSequenceFromSolution(sol, mgaConfig);

            activeSequenceMode = 1; // 激活行星际引力助推序列编辑工作台
            activeTab = 2;          // 跳转至任务控制序列页面
            ScreenMessages.PostScreenMessage(I18n.Format("mga_loaded", sol.FlybyChainSummary), 3.5f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void DrawPorkchopView() {
            GUILayout.BeginVertical(nestedCardBoxStyle);

            // Header Toolbar
            string targetName = (mgaWorkflowMode == 0 || mgaConfig.FlybySequence == null || mgaConfig.FlybySequence.Count == 0) ? mgaConfig.DestinationBody : mgaConfig.FlybySequence[0];
            double hohDaysLive = PykepBridge.GetHohmannTransferTimeDays(mgaConfig.DepartureBody, targetName);
            double synDaysLive = PykepBridge.GetSynodicPeriodDays(mgaConfig.DepartureBody, targetName);

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.ColorFormat("porkchop_title", mgaConfig.DepartureBody, targetName, hohDaysLive, synDaysLive), labelValueStyle);
            GUILayout.FlexibleSpace();

            if (isComputingPorkchop) {
                GUILayout.Label(I18n.ColorFormat("porkchop_computing", porkchopStatusText), labelValueStyle);
            } else {
                if (GUILayout.Button(I18n.T("btn_porkchop_sweep"), secondaryButtonStyle, GUILayout.Height(20))) {
                    StartPorkchopComputation(useCurrentDateInputs: true);
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_porkchop_full"), secondaryButtonStyle, GUILayout.Height(20))) {
                    StartPorkchopComputation(useCurrentDateInputs: false);
                }
                GUILayout.Space(6);
                if (currentPorkchop != null && currentPorkchop.PlotTexture != null) {
                    if (GUILayout.Button(I18n.T("btn_porkchop_adopt"), actionButtonStyle, GUILayout.Height(20))) {
                        ApplyPorkchopCell(currentPorkchop.BestDepUT, currentPorkchop.BestToFDays);
                    }
                }
            }
            GUILayout.Space(6);
            if (GUILayout.Button(I18n.T("btn_back_3d"), deleteButtonStyle, GUILayout.Width(110), GUILayout.Height(20))) {
                showPorkchopModal = false;
                RefreshMgaCanvas();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (currentPorkchop != null && currentPorkchop.PlotTexture != null) {
                GUILayout.BeginHorizontal();

                // Main Porkchop Map (Transfer Window Planner Layout)
                Rect plotRect = GUILayoutUtility.GetRect(500f, 320f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUI.DrawTexture(plotRect, currentPorkchop.PlotTexture, ScaleMode.StretchToFill);

                // Draw Vector Target Reticle ⌖ at optimal point (Crisp & undistorted)
                if (currentPorkchop.BestX >= 0 && currentPorkchop.BestY >= 0) {
                    float bNormX = currentPorkchop.BestX / (float)(currentPorkchop.Width - 1);
                    float bNormY = currentPorkchop.BestY / (float)(currentPorkchop.Height - 1);
                    float retX = plotRect.x + bNormX * plotRect.width;
                    float retY = plotRect.y + (1.0f - bNormY) * plotRect.height;

                    if (cachedReticleStyle == null) {
                        cachedReticleStyle = new GUIStyle(GUI.skin.label) {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = 22,
                            fontStyle = FontStyle.Bold
                        };
                        cachedReticleStyle.normal.textColor = new Color(1.0f, 0.90f, 0.15f, 0.95f);
                    }
                    GUI.Label(new Rect(retX - 16, retY - 16, 32, 32), "⌖", cachedReticleStyle);
                }

                GUILayout.Space(8);

                // Right-Side Color Bar Legend (Matching Figure 2)
                if (currentPorkchop.ColorBarTexture != null) {
                    GUILayout.BeginVertical(GUILayout.Width(75));
                    GUILayout.Label("<color=#38bdf8><b>Δv (m/s)</b></color>", labelValueStyle);
                    GUILayout.Label($"<color=#f43f5e>{currentPorkchop.ScaleMaxDV:F0}</color>", labelValueStyle);

                    GUILayout.FlexibleSpace();
                    Rect barRect = GUILayoutUtility.GetRect(18f, 220f, GUILayout.Width(18), GUILayout.ExpandHeight(true));
                    GUI.DrawTexture(barRect, currentPorkchop.ColorBarTexture, ScaleMode.StretchToFill);
                    GUILayout.FlexibleSpace();

                    GUILayout.Label($"<color=#38bdf8>{currentPorkchop.ScaleMinDV:F0}</color>", labelValueStyle);
                    GUILayout.EndVertical();
                }

                GUILayout.EndHorizontal();

                // Hover & Click interaction
                Event e = Event.current;
                if (plotRect.Contains(e.mousePosition)) {
                    float normX = (e.mousePosition.x - plotRect.x) / plotRect.width;
                    float normY = 1.0f - (e.mousePosition.y - plotRect.y) / plotRect.height; // Invert for Y=up (ToF)

                    var cell = currentPorkchop.GetCellInfo(normX, normY);
                    if (cell.IsValid) {
                        lastPorkchopHoverPos = e.mousePosition;
                        isHoveringPorkchop = true;

                        // Click to Select
                        if (e.type == EventType.MouseDown && e.button == 0) {
                            ApplyPorkchopCell(cell.DepUT, cell.ToFDays);
                            e.Use();
                        }
                    }
                } else {
                    isHoveringPorkchop = false;
                }

                // Draw Axis Legends
                GUILayout.BeginHorizontal();
                string startDepStr = GMATClient.FormatUtcGregorian(currentPorkchop.DepStartUT);
                string endDepStr = GMATClient.FormatUtcGregorian(currentPorkchop.DepStartUT + currentPorkchop.DepSpanDays * 86400.0);
                GUILayout.Label(I18n.ColorFormat("porkchop_dep_axis", startDepStr), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=#38bdf8>{endDepStr}</color>", labelValueStyle);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("porkchop_tof_axis", currentPorkchop.ToFMinDays, currentPorkchop.ToFMaxDays), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.ColorFormat("porkchop_best", currentPorkchop.BestTotalDV, GMATClient.FormatUtcGregorian(currentPorkchop.BestDepUT), currentPorkchop.BestToFDays), labelValueStyle);
                GUILayout.EndHorizontal();

                // Non-optimal notice & auto-snap button
                if (currentPorkchop.BestTotalDV > 4500.0) {
                    GUILayout.BeginHorizontal(nestedCardBoxStyle);
                    GUILayout.Label(I18n.ColorFormat("porkchop_bad_window", currentPorkchop.BestTotalDV), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(I18n.T("btn_porkchop_autofix"), actionButtonStyle, GUILayout.Height(20))) {
                        double sUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
                        var (optUT, tofD, _) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, targetName, sUT);
                        if (double.IsNaN(optUT)) {
                            ScreenMessages.PostScreenMessage(I18n.T("mga_native_unavailable"), 4.5f, ScreenMessageStyle.UPPER_CENTER);
                        } else {
                            mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 20.0);
                            mgaConfig.LatestDepartureUT = optUT + 86400.0 * 20.0;
                            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
                            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
                            StartPorkchopComputation(useCurrentDateInputs: true);
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                // Hover tooltip display
                if (isHoveringPorkchop) {
                    float normX = (lastPorkchopHoverPos.x - plotRect.x) / plotRect.width;
                    float normY = 1.0f - (lastPorkchopHoverPos.y - plotRect.y) / plotRect.height;
                    var cell = currentPorkchop.GetCellInfo(normX, normY);
                    if (cell.IsValid) {
                        GUILayout.BeginHorizontal(nestedCardBoxStyle);
                        GUILayout.Label(I18n.ColorFormat("porkchop_hover", GMATClient.FormatUtcGregorian(cell.DepUT), GMATClient.FormatUtcGregorian(cell.ArrUT), cell.ToFDays, cell.EjectDV, cell.InsertDV, cell.TotalDV), labelValueStyle);
                        GUILayout.EndHorizontal();
                    }
                }
            } else {
                GUILayout.BeginVertical(GUILayout.Height(300));
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(I18n.T("porkchop_computing_msg"), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();
        }

        private async void StartPorkchopComputation(bool useCurrentDateInputs = false) {
            if (isComputingPorkchop) return;
            SolarSystemCatalog.EnsureAllPlanetsCached();
            isComputingPorkchop = true;
            porkchopStatusText = I18n.T("porkchop_status_init");

            double startUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
            if (startUT <= 0) startUT = Planetarium.GetUniversalTime();

            string targetBody = (mgaConfig.FlybySequence != null && mgaConfig.FlybySequence.Count > 0) ? mgaConfig.FlybySequence[0] : mgaConfig.DestinationBody;

            double hohTofDays = PykepBridge.GetHohmannTransferTimeDays(mgaConfig.DepartureBody, targetBody);
            double synDays = PykepBridge.GetSynodicPeriodDays(mgaConfig.DepartureBody, targetBody);

            double depStart, depSpan;
            if (useCurrentDateInputs) {
                double endUT = GMATClient.ParseUtcGregorian(mgaEndDateInput);
                if (endUT <= startUT + 86400.0 * 5.0) endUT = startUT + 86400.0 * Math.Max(60.0, synDays * 0.8);
                depStart = startUT;
                depSpan = (endUT - startUT) / 86400.0;
            } else {
                var (optUT, _, _) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, targetBody, startUT);
                if (double.IsNaN(optUT)) optUT = startUT; // 扫描区间仅用于取景, 原生不可用时以当前时间为中心
                depStart = Math.Max(0, optUT - 86400.0 * (synDays * 0.45));
                depSpan = Math.Max(60.0, synDays * 0.9);
            }

            double minTof = Math.Max(15.0, hohTofDays * 0.35);
            double maxTof = Math.Max(minTof + 30.0, hohTofDays * 2.0);

            try {
                currentPorkchop = await PorkchopGenerator.ComputePorkchopAsync(
                    mgaConfig.DepartureBody, targetBody,
                    depStart, depSpan,
                    minTof, maxTof,
                    mgaConfig.DepartureAltitudeKm, mgaConfig.DestinationAltitudeKm,
                    mgaConfig.NoInsertionBurn,
                    160, 120,
                    (p, msg) => { porkchopStatusText = msg; });
            } catch (Exception ex) {
                KSPGMATLog.Error($"[Porkchop] Error computing porkchop: {ex.Message}");
            } finally {
                isComputingPorkchop = false;
            }
        }

        private void ApplyPorkchopCell(double depUT, double tofDays) {
            mgaConfig.EarliestDepartureUT = Math.Max(0, depUT - 86400.0 * 15.0);
            mgaConfig.LatestDepartureUT = depUT + 86400.0 * 15.0;
            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
            showPorkchopModal = false;
            StartMgaOptimization();
        }

        private void UploadMgaSolutionToPrincipia(MGASolution sol) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) {
                ScreenMessages.PostScreenMessage(I18n.T("btn_no_vessel"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            try {
                PrincipiaInterop.ClearAllFlightPlanNodes(vessel);

                int writtenCount = 0;
                for (int k = 0; k < sol.Legs.Count; k++) {
                    var leg = sol.Legs[k];
                    if (k == 0 || leg.FlybyDeltaVKms > 0.001) {
                        double burnTime = leg.DepartureUT;
                        Vector3d dv = new Vector3d(leg.ProgradeMs, leg.RadialMs, leg.NormalMs);

                        if (k == 0 && mgaConfig.EnableParkingOrbitPhaseAlignment && !sol.IsPhaseAligned) {
                            // 若方案尚未对齐停泊轨，执行对齐校验
                            if (MGAInjectionAligner.AlignParkingOrbitInjection(vessel, sol, out double alignedTime, out Vector3d alignedDv, 0)) {
                                if (alignedDv.magnitude < (sol.LaunchDeltaVKms * 1000.0 + 500.0)) {
                                    burnTime = alignedTime;
                                    dv = alignedDv;
                                }
                            }
                        }

                        bool ok = PrincipiaInterop.InsertOrUpdateBurn(vessel, burnTime, dv, 50.0, 310.0, writtenCount, isFrenet: true, originBodyName: leg.FromBody);
                        if (ok) writtenCount++;
                    }
                }

                if (writtenCount > 0) {
                    ScreenMessages.PostScreenMessage(I18n.Format("mga_upload_done", writtenCount), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    ScreenMessages.PostScreenMessage(I18n.T("mga_upload_warn"), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            } catch (Exception ex) {
                KSPGMATLog.Error($"[TabMGA] Principia upload exception: {ex.Message}");
            }
        }

        private void RunHistoricalBenchmarks() {
            try {
                var (resList, summary) = MGABenchmarkSuite.RunAllBenchmarks();
                benchmarkResults = resList;
                benchmarkSummaryText = summary;
                selectedBenchmarkIndex = 0;
            } catch (Exception ex) {
                KSPGMATLog.Error($"[Benchmark] RunAllBenchmarks exception: {ex.Message}");
            }
        }

        private void DrawBenchmarkModal() {
            GUILayout.BeginVertical(nestedCardBoxStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("bench_title"), labelValueStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_rebench"), secondaryButtonStyle, GUILayout.Height(20))) {
                RunHistoricalBenchmarks();
            }
            GUILayout.Space(6);
            if (GUILayout.Button(I18n.T("btn_back_3d"), deleteButtonStyle, GUILayout.Width(110), GUILayout.Height(20))) {
                showBenchmarkModal = false;
                RefreshMgaCanvas();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (benchmarkResults == null || benchmarkResults.Count == 0) {
                GUILayout.Label(I18n.T("bench_computing"), labelValueStyle);
                GUILayout.EndVertical();
                return;
            }

            // Mission Selector Tabs
            GUILayout.BeginHorizontal();
            for (int i = 0; i < benchmarkResults.Count; i++) {
                var br = benchmarkResults[i];
                string label = br.Success ? $"✓ {br.TestCase.MissionName.Split(' ')[0]}" : $"✗ {br.TestCase.MissionName.Split(' ')[0]}";
                GUIStyle tabStyle = (i == selectedBenchmarkIndex) ? actionButtonStyle : secondaryButtonStyle;
                if (GUILayout.Button(label, tabStyle, GUILayout.Height(22))) {
                    selectedBenchmarkIndex = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            var curRes = benchmarkResults[selectedBenchmarkIndex];
            var tc = curRes.TestCase;

            // Summary Info Card
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.Label(I18n.ColorFormat("bench_mission", tc.MissionName, tc.Description), labelValueStyle);
            GUILayout.Label(I18n.ColorFormat("bench_route", tc.DepartureBody, string.Join(" -> ", tc.FlybySequence), tc.HistoricLaunchDateStr), labelValueStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.ColorFormat("bench_launch_dv", curRes.ComputedLaunchDV), labelValueStyle);
            GUILayout.Label(I18n.ColorFormat("bench_total_dv", curRes.ComputedTotalDV, tc.HistoricTotalDeltaVMinMs, tc.HistoricTotalDeltaVMaxMs), labelValueStyle);
            GUILayout.Label(I18n.ColorFormat("bench_flight_days", curRes.ComputedFlightDays, curRes.ComputedFlightDays / 365.25), labelValueStyle);
            GUILayout.Label(I18n.ColorFormat("bench_continuity", curRes.VectorContinuityPassed ? I18n.T("bench_cont_ok") : I18n.T("bench_cont_fail")), labelValueStyle);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(2);

            // Detailed Diagnostic Report Box
            benchmarkReportScroll = GUILayout.BeginScrollView(benchmarkReportScroll, GUILayout.Height(190));
            GUILayout.TextArea(curRes.DetailedReport, stepLabelInputStyle ?? GUI.skin.textArea, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // Action Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("btn_bench_load"), actionButtonStyle, GUILayout.Height(24))) {
                mgaConfig.DepartureBody = tc.DepartureBody;
                mgaConfig.FlybySequence = new List<string>(tc.FlybySequence);
                mgaConfig.DestinationBody = tc.FlybySequence.Last();
                mgaConfig.EarliestDepartureUT = Math.Max(0, tc.HistoricLaunchUT - 86400.0 * 20.0);
                mgaConfig.LatestDepartureUT = tc.HistoricLaunchUT + 86400.0 * 20.0;
                mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
                mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
                mgaConfig.MinToFDays = tc.HistoricToFDays.Select(d => Math.Max(10.0, d * 0.8)).ToList();
                mgaConfig.MaxToFDays = tc.HistoricToFDays.Select(d => d * 1.25).ToList();
                mgaConfig.NoInsertionBurn = tc.NoInsertionBurn;
                customChainInput = string.Join(",", mgaConfig.FlybySequence);

                if (curRes.Solution != null) {
                    mgaSolutions = new List<MGASolution> { curRes.Solution };
                    selectedMgaSolution = curRes.Solution;
                }

                showBenchmarkModal = false;
                RefreshMgaCanvas();
                ScreenMessages.PostScreenMessage(I18n.Format("bench_loaded", tc.MissionName), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.Space(8);
            if (GUILayout.Button(I18n.T("btn_bench_script"), secondaryButtonStyle, GUILayout.Height(24))) {
                if (!string.IsNullOrEmpty(curRes.GMATScriptPreview)) {
                    ideCustomBuffer = curRes.GMATScriptPreview;
                    activeTab = 0;
                    ideViewMode = 1;
                    ScreenMessages.PostScreenMessage(I18n.Format("bench_script_loaded", tc.MissionName), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        /// <summary>
        /// 交互式 3D 视口画布渲染器 (支持左键平移、右键3D旋转、滚轮缩放与自动尺寸重绘)
        /// </summary>
        private void DrawInteractiveMgaCanvas(float minW = 380f, float minH = 340f) {
            UpdateMgaAnimationTick();
            Rect canvasRect = GUILayoutUtility.GetRect(minW, minH, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            int curW = (int)canvasRect.width;
            int curH = (int)canvasRect.height;
            if (Event.current.type == EventType.Repaint) {
                if (curW > 100 && curH > 100 && (Math.Abs(curW - lastCanvasW) > 2 || Math.Abs(curH - lastCanvasH) > 2)) {
                    RefreshMgaCanvas(curW, curH);
                }
            }
            if (mgaCanvasTexture == null) {
                RefreshMgaCanvas(curW > 100 ? curW : 580, curH > 100 ? curH : 440);
            }

            if (mgaCanvasTexture != null) {
                GUI.DrawTexture(canvasRect, mgaCanvasTexture, ScaleMode.StretchToFill);

                Event e = Event.current;
                int canvasControlId = GUIUtility.GetControlID(FocusType.Passive);

                if (e.rawType == EventType.MouseUp || e.type == EventType.MouseUp) {
                    if (isDraggingCanvas || isRotatingCanvas) {
                        isDraggingCanvas = false;
                        isRotatingCanvas = false;
                        if (GUIUtility.hotControl == canvasControlId) GUIUtility.hotControl = 0;
                        e.Use();
                    }
                }

                if (isDraggingCanvas || isRotatingCanvas) {
                    if (e.type == EventType.MouseDrag) {
                        Vector2 delta = e.mousePosition - lastMouseCanvasPos;
                        lastMouseCanvasPos = e.mousePosition;
                        if (isDraggingCanvas) {
                            mgaCanvasPan = new Vector2(mgaCanvasPan.x + delta.x, mgaCanvasPan.y - delta.y);
                            RefreshMgaCanvas();
                            e.Use();
                        } else if (isRotatingCanvas) {
                            // 自然拖动方向：鼠标向右拖动天体随之向右转动(yaw减小)，无反转顿挫感
                            mgaYaw -= delta.x * 0.35f;
                            while (mgaYaw > 180.0f) mgaYaw -= 360.0f;
                            while (mgaYaw < -180.0f) mgaYaw += 360.0f;
                            // 移除 5 度死锁硬编码：允许流畅倾斜至黄道面 (0°) 及仰视视角 (-89.5° ~ 89.5°)
                            mgaPitch = Mathf.Clamp(mgaPitch - delta.y * 0.35f, -89.5f, 89.5f);
                            RefreshMgaCanvas();
                            e.Use();
                        }
                    }
                } else if (canvasRect.Contains(e.mousePosition)) {
                    if (e.type == EventType.ScrollWheel) {
                        float zoomDelta = e.delta.y < 0 ? 1.20f : 0.833f;
                        mgaCanvasZoom = Mathf.Clamp(mgaCanvasZoom * zoomDelta, 0.05f, 50f);
                        RefreshMgaCanvas();
                        e.Use();
                    } else if (e.type == EventType.MouseDown) {
                        if (e.button == 0 && e.clickCount == 2) {
                            // 双击左键快速居中复位平移
                            mgaCanvasPan = Vector2.zero;
                            RefreshMgaCanvas();
                            e.Use();
                        } else if (e.button == 0) {
                            // 左键拖拽：默认 3D 自由环绕旋转 (按住 Shift 时切换为平移)
                            if (e.shift) {
                                isDraggingCanvas = true;
                            } else {
                                isRotatingCanvas = true;
                            }
                            lastMouseCanvasPos = e.mousePosition;
                            GUIUtility.hotControl = canvasControlId;
                            e.Use();
                        } else if (e.button == 1 || e.button == 2) {
                            // 右键或中键拖拽：平移画布
                            isDraggingCanvas = true;
                            lastMouseCanvasPos = e.mousePosition;
                            GUIUtility.hotControl = canvasControlId;
                            e.Use();
                        }
                    }
                }
            }

            // =========================================================================
            // 任务流程逐条推进与回放滑块 (Mission Flow Step Scrubber)
            // =========================================================================
            if (selectedMgaSolution != null && selectedMgaSolution.Legs != null && selectedMgaSolution.Legs.Count > 0) {
                int totalSteps = GetMgaTotalSteps();
                int totalLegs = selectedMgaSolution.Legs.Count;
                var seq = MGAStateManager.Current.MissionSequence;
                bool isSeqMode = (seq != null && seq.Count > 1);

                GUILayout.Space(3);
                GUILayout.BeginHorizontal(nestedCardBoxStyle);
                GUILayout.Label("⏱️ 流程推进:", labelValueStyle, GUILayout.Width(75));

                bool isAll = (mgaTargetProgress <= 0.001f && mgaFlowProgress <= 0.001f);
                if (GUILayout.Button(isAll ? "全部" : "单步", isAll ? actionButtonStyle : secondaryButtonStyle, GUILayout.Width(44), GUILayout.Height(20))) {
                    mgaIsPlaying = false;
                    if (isAll) {
                        mgaFlowProgress = 0.001f;
                        mgaTargetProgress = 1.0f;
                    } else {
                        mgaTargetProgress = 0.0f;
                    }
                    RefreshMgaCanvas();
                }

                // ◀ 按钮：始终可用，点击平滑飞向上一节点
                if (GUILayout.Button("◀", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    mgaIsPlaying = false;
                    if (mgaTargetProgress <= 0.05f) {
                        mgaTargetProgress = (float)totalSteps;
                    } else {
                        float curInt = Mathf.Ceil(mgaTargetProgress - 0.05f);
                        float prevTarget = curInt - 1.0f;
                        if (prevTarget <= 0.05f) prevTarget = 0.0f;
                        mgaTargetProgress = prevTarget;
                    }
                    if (mgaFlowProgress <= 0.001f && mgaTargetProgress > 0f) {
                        mgaFlowProgress = 0.001f;
                    }
                    RefreshMgaCanvas();
                }

                // 滑块：无极浮点连续调节 (0f = 全部总览, 0.001f..totalSteps = 连续平滑进度)
                float curVal = isAll ? 0f : mgaFlowProgress;
                float newVal = GUILayout.HorizontalSlider(curVal, 0f, (float)totalSteps, GUILayout.Width(130));
                if (Math.Abs(newVal - curVal) > 0.001f) {
                    mgaIsPlaying = false;
                    if (newVal < 0.03f) newVal = 0f;
                    mgaFlowProgress = newVal;
                    mgaTargetProgress = newVal;
                    RefreshMgaCanvas();
                }

                // ▶ 按钮：始终可用，点击平滑飞向下一节点
                if (GUILayout.Button("▶", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    mgaIsPlaying = false;
                    if (mgaTargetProgress <= 0.05f) {
                        if (mgaFlowProgress <= 0.05f) mgaFlowProgress = 0.001f;
                        mgaTargetProgress = 1.0f;
                    } else {
                        float curInt = Mathf.Floor(mgaTargetProgress + 0.05f);
                        float nextTarget = curInt + 1.0f;
                        if (nextTarget > (float)totalSteps + 0.05f) nextTarget = 0.0f;
                        mgaTargetProgress = nextTarget;
                    }
                    if (mgaFlowProgress <= 0.001f && mgaTargetProgress > 0f) {
                        mgaFlowProgress = 0.001f;
                    }
                    RefreshMgaCanvas();
                }

                // 播放/暂停动态动画按钮
                GUILayout.Space(2);
                string playText = mgaIsPlaying ? "⏸ 暂停" : "▶ 播放";
                GUIStyle playStyle = mgaIsPlaying ? actionButtonStyle : secondaryButtonStyle;
                if (GUILayout.Button(playText, playStyle, GUILayout.Width(54), GUILayout.Height(20))) {
                    mgaIsPlaying = !mgaIsPlaying;
                    if (mgaIsPlaying && (mgaFlowProgress <= 0.01f || mgaFlowProgress >= totalSteps - 0.01f)) {
                        mgaFlowProgress = 0.001f;
                    }
                    mgaTargetProgress = mgaFlowProgress;
                    RefreshMgaCanvas();
                }

                // 倍速切换按钮
                string speedLabel = $"{mgaPlaySpeed:0.#}x";
                if (GUILayout.Button(speedLabel, secondaryButtonStyle, GUILayout.Width(34), GUILayout.Height(20))) {
                    if (mgaPlaySpeed < 1.9f) mgaPlaySpeed = 2.0f;
                    else if (mgaPlaySpeed < 4.9f) mgaPlaySpeed = 5.0f;
                    else if (mgaPlaySpeed < 9.9f) mgaPlaySpeed = 10.0f;
                    else mgaPlaySpeed = 1.0f;
                }

                GUILayout.Space(4);
                if (isAll) {
                    if (isSeqMode) {
                        GUILayout.Label($"<color=#38bdf8><b>全流程总览 (共 {totalSteps} 步任务序列 | 累计 {selectedMgaSolution.TotalFlightDays:F0}d | 总ΔV {selectedMgaSolution.TotalDeltaVKms * 1000.0:F0}m/s)</b></color>", labelValueStyle);
                    } else {
                        GUILayout.Label($"<color=#38bdf8><b>全流程总览 (共 {totalSteps} 航段 | 累计 {selectedMgaSolution.TotalFlightDays:F0}d | 总ΔV {selectedMgaSolution.TotalDeltaVKms * 1000.0:F0}m/s)</b></color>", labelValueStyle);
                    }
                } else {
                    if (isSeqMode) {
                        int stepIdx = Mathf.Clamp((int)Math.Round(mgaFlowProgress), 1, seq.Count);
                        var s = seq[stepIdx - 1];
                        GUILayout.Label($"<color=#34d399><b>第 {stepIdx}/{totalSteps} 步: {s.label}</b> | 历元: {s.epoch_utc_str}</color>", labelValueStyle);
                    } else {
                        int activeIdx = Mathf.Clamp((int)Math.Floor(mgaFlowProgress), 0, totalLegs - 1);
                        float frac = mgaFlowProgress - activeIdx;
                        if (mgaFlowProgress >= totalLegs) { activeIdx = totalLegs - 1; frac = 1.0f; }
                        var curLeg = selectedMgaSolution.Legs[activeIdx];
                        double currentUt = curLeg.DepartureUT + frac * (curLeg.ArrivalUT - curLeg.DepartureUT);
                        double metDays = (currentUt - selectedMgaSolution.DepartureUT) / 86400.0;
                        string utStr = GMATClient.FormatUtcGregorian(currentUt);
                        GUILayout.Label($"<color=#34d399><b>第 {activeIdx + 1}/{totalSteps} 段: {curLeg.FromBody} ➔ {curLeg.ToBody}</b> | 航段 {(frac * 100f):F0}% | 历元: {utStr} (T+{metDays:F1}d)</color>", labelValueStyle);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
    }
}
