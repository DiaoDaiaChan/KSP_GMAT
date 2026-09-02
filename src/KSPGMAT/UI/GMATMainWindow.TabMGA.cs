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
        private MGAConfig mgaConfig = new MGAConfig {
            DepartureBody = "Earth",
            DestinationBody = "Saturn",
            FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter", "Saturn" },
            MaxSwingBys = 4,
            MaxResonances = 1,
            MaxBackLegs = 1,
            MaxBackSpacing = 2,
            EarliestDepartureUT = 0.0,
            LatestDepartureUT = 86400.0 * 365.25 * 3.0,
            DepartureAltitudeKm = 200.0,
            DestinationAltitudeKm = 200.0,
            MaxDurationDays = 4000.0,
            EnableMaxDuration = false,
            NoInsertionBurn = false,
            PopulationSize = 60,
            MaxGenerations = 60,
            AllowPoweredFlyby = true
        };

        private List<List<string>> generatedCandidateChains = new List<List<string>>();
        private int selectedCandidateChainIndex = 0;
        private string customChainInput = "";
        private List<MGASolution> mgaSolutions = new List<MGASolution>();
        private MGASolution selectedMgaSolution = null;
        private int selectedManeuverIndex = 0;
        private bool showManeuverDropdown = false;
        private int mgaDetailViewMode = 0; // 0 = 物理明细, 1 = 原始数据

        // Orbit Map Canvas State
        private Texture2D mgaCanvasTexture = null;
        private float mgaCanvasZoom = 1.0f;
        private Vector2 mgaCanvasPan = Vector2.zero;
        private bool mgaShowSois = true;
        private bool mgaShowMetDates = false;
        private bool isDraggingCanvas = false;
        private Vector2 lastMouseCanvasPos = Vector2.zero;

        // Async Search State
        private bool isMgaOptimizing = false;
        private float mgaProgress = 0f;
        private string mgaStatusText = "";
        private CancellationTokenSource mgaCts = null;
        private Vector2 mgaLeftScroll = Vector2.zero;
        private string mgaDepDateInput = "";
        private string mgaEndDateInput = "";
        private bool mgaDatesInitialized = false;
        private int mgaWorkflowMode = 0; // 0 = Direct Hohmann, 1 = MGA Differential Evolution, 2 = Historical Benchmark

        // Porkchop Plot Engine State
        private bool showPorkchopModal = false;
        private PorkchopGrid currentPorkchop = null;
        private bool isComputingPorkchop = false;
        private string porkchopStatusText = "";
        private Vector2 lastPorkchopHoverPos = Vector2.zero;
        private bool isHoveringPorkchop = false;

        // Historical Benchmark Test Suite State
        private bool showBenchmarkModal = false;
        private List<MGABenchmarkSuite.BenchmarkResult> benchmarkResults = null;
        private string benchmarkSummaryText = "";
        private int selectedBenchmarkIndex = 0;
        private Vector2 benchmarkReportScroll = Vector2.zero;

        private void InitOrSyncMgaFromActiveVessel(bool force = false) {
            if (mgaDatesInitialized && !force) return;

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

                // 3. 当前游戏时刻历元 (绝对秒数 UT)
                double nowUt = Planetarium.GetUniversalTime();
                mgaConfig.EarliestDepartureUT = nowUt;
                mgaConfig.LatestDepartureUT = nowUt + 86400.0 * 365.25 * 3.0; // 默认向后搜索 3 年窗口
            } else {
                double nowUt = Planetarium.GetUniversalTime() > 0 ? Planetarium.GetUniversalTime() : 0.0;
                mgaConfig.EarliestDepartureUT = nowUt;
                mgaConfig.LatestDepartureUT = nowUt + 86400.0 * 365.25 * 3.0;
            }

            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
            
            // 自动刷新候选序列与画布
            RefreshCandidateChains();
            mgaDatesInitialized = true;
            RefreshMgaCanvas();
        }

        private void RefreshCandidateChains(bool autoSelectFirst = false) {
            generatedCandidateChains = PykepBridge.GenerateCandidateSequences(
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
            double startUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
            if (startUT <= 0) startUT = Planetarium.GetUniversalTime() > 0 ? Planetarium.GetUniversalTime() : 0.0;
            string target = mgaConfig.DestinationBody;
            var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, target, startUT);
            mgaConfig.FlybySequence = new List<string> { target };
            mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 25.0);
            mgaConfig.LatestDepartureUT = optUT + 86400.0 * 25.0;
            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
            
            mgaConfig.MinToFDays = new List<double> { Math.Max(15.0, tofDays * 0.75) };
            mgaConfig.MaxToFDays = new List<double> { tofDays * 1.35 };

            StartMgaOptimization();
            ScreenMessages.PostScreenMessage(I18n.Format("mga_solve_window", mgaConfig.DepartureBody, target, GMATClient.FormatUtcGregorian(optUT)), 4.5f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void LoadHistoricalMissionPreset(string missionKey) {
            var bCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf(missionKey, StringComparison.OrdinalIgnoreCase) >= 0);
            if (bCase == null) return;

            mgaConfig.DepartureBody = bCase.DepartureBody;
            mgaConfig.DestinationBody = bCase.FlybySequence.Last();
            mgaConfig.FlybySequence = new List<string>(bCase.FlybySequence);
            customChainInput = string.Join(",", mgaConfig.FlybySequence);

            double launchUT = GMATClient.ParseUtcGregorian(bCase.HistoricLaunchDateStr);
            if (launchUT <= 0) launchUT = bCase.HistoricLaunchUT;

            mgaConfig.EarliestDepartureUT = Math.Max(0, launchUT - 86400.0 * 20.0);
            mgaConfig.LatestDepartureUT = launchUT + 86400.0 * 20.0;
            mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
            mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);

            mgaConfig.DepartureAltitudeKm = bCase.DepAltKm;
            mgaConfig.DestinationAltitudeKm = bCase.ArrAltKm;
            mgaConfig.NoInsertionBurn = bCase.NoInsertionBurn;

            if (bCase.HistoricToFDays != null && bCase.HistoricToFDays.Count > 0) {
                mgaConfig.MinToFDays = bCase.HistoricToFDays.Select(d => Math.Max(10.0, d * 0.9)).ToList();
                mgaConfig.MaxToFDays = bCase.HistoricToFDays.Select(d => d * 1.1).ToList();
            }

            RefreshCandidateChains(false);

            // 直接构建并加载历史权威基准解，确保 3D 轨迹与 3 轴机动矢量物理绝对真值
            var bRes = MGABenchmarkSuite.RunBenchmark(bCase);
            if (bRes != null && bRes.Solution != null) {
                selectedMgaSolution = bRes.Solution;
                mgaSolutions = new List<MGASolution> { selectedMgaSolution };
                selectedManeuverIndex = 0;
            }

            RefreshMgaCanvas();
            ScreenMessages.PostScreenMessage(I18n.Format("mga_load_hist", bCase.MissionName, selectedMgaSolution?.TotalDeltaVKms * 1000.0), 5f, ScreenMessageStyle.UPPER_CENTER);
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

        private int lastCanvasW = 600;
        private int lastCanvasH = 400;
        private float mgaPitch = 35.0f; // 3D Camera Elevation Angle (35° default, 90° for 2D top-down)
        private float mgaYaw = 0.0f;   // 3D Camera Azimuth Angle
        private bool isRotatingCanvas = false;
        private bool mgaAutoFocusEnabled = true;

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
                    mgaCanvasPan.x = lastCanvasW / 2 - unpannedScr.x;
                    mgaCanvasPan.y = unpannedScr.y - lastCanvasH / 2;
                }
            }
            RefreshMgaCanvas();
        }

        private void RefreshMgaCanvas(int w = 0, int h = 0) {
            if (w > 100 && h > 100) {
                lastCanvasW = w;
                lastCanvasH = h;
            }
            mgaCanvasTexture = GMATOrbitCanvas.RenderSolarSystemMGATexture(
                selectedMgaSolution, 
                lastCanvasW, lastCanvasH, 
                mgaCanvasZoom, 
                mgaCanvasPan, 
                mgaShowSois,
                mgaPitch,
                mgaYaw,
                selectedManeuverIndex);
        }

        private void DrawTabMGA(Vessel vessel) {
            InitOrSyncMgaFromActiveVessel();

            // 双栏自适应宽敞布局 (左侧规划器 + 右侧高精 3D 视口)
            GUILayout.BeginHorizontal();

            // =========================================================================
            // 左栏：借力计算器与参数配置 (动态自适应宽度，确保全部控件宽敞不拥挤)
            // =========================================================================
            float leftColWidth = Mathf.Clamp(windowRect.width * 0.44f, 520f, 650f);
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(leftColWidth));
            mgaLeftScroll = GUILayout.BeginScrollView(mgaLeftScroll);

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

            // 1. 顶部模式选择器 (3 种明确的目标模式，消除操作困惑)
            GUILayout.BeginHorizontal();
            GUIStyle mode0Style = (mgaWorkflowMode == 0) ? actionButtonStyle : secondaryButtonStyle;
            GUIStyle mode1Style = (mgaWorkflowMode == 1) ? actionButtonStyle : secondaryButtonStyle;
            GUIStyle mode2Style = (mgaWorkflowMode == 2) ? actionButtonStyle : secondaryButtonStyle;

            if (GUILayout.Button(I18n.T("mga_mode_direct"), mode0Style, GUILayout.Height(24))) {
                mgaWorkflowMode = 0;
                mgaConfig.FlybySequence = new List<string> { mgaConfig.DestinationBody };
                customChainInput = mgaConfig.DestinationBody;
                selectedMgaSolution = null;
                mgaSolutions.Clear();
                showBenchmarkModal = false;
                showPorkchopModal = false;
                RefreshMgaCanvas();
            }
            if (GUILayout.Button(I18n.T("mga_mode_mga"), mode1Style, GUILayout.Height(24))) {
                mgaWorkflowMode = 1;
                showBenchmarkModal = false;
                showPorkchopModal = false;
                RefreshCandidateChains();
                RefreshMgaCanvas();
            }
            if (GUILayout.Button(I18n.T("mga_mode_history"), mode2Style, GUILayout.Height(24))) {
                mgaWorkflowMode = 2;
                showBenchmarkModal = false;
                showPorkchopModal = false;
                LoadHistoricalMissionPreset("Cassini");
                RefreshMgaCanvas();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            var availablePlanets = SolarSystemCatalog.GetAvailablePlanets();

            // =========================================================================
            // 模式 0: 极速直接转移 (Direct Hohmann Transfer)
            // =========================================================================
            if (mgaWorkflowMode == 0) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_departure"), labelValueStyle, GUILayout.Width(70));
                var curDep = SolarSystemCatalog.GetPlanet(mgaConfig.DepartureBody);
                if (GUILayout.Button(I18n.Format("mga_planet_btn_short", curDep.Icon, curDep.GetDisplayName(), curDep.Name), actionButtonStyle, GUILayout.Height(22))) {
                    OpenPlanetPicker(0, I18n.T("mga_picker_departure"));
                }
                GUILayout.Space(8);
                GUILayout.Label(I18n.T("mga_destination"), labelValueStyle, GUILayout.Width(70));
                var curDest = SolarSystemCatalog.GetPlanet(mgaConfig.DestinationBody);
                if (GUILayout.Button(I18n.Format("mga_planet_btn_short", curDest.Icon, curDest.GetDisplayName(), curDest.Name), actionButtonStyle, GUILayout.Height(22))) {
                    OpenPlanetPicker(1, I18n.T("mga_picker_destination"));
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                // 一键全自动智能求解按钮
                if (GUILayout.Button(I18n.Format("mga_auto_solve", mgaConfig.DepartureBody, mgaConfig.DestinationBody), actionButtonStyle, GUILayout.Height(30))) {
                    AutoSolveDirectHohmann();
                }
                GUILayout.EndVertical();
            }
            // =========================================================================
            // 模式 1: 多重引力助推全局寻优 (MGA Differential Evolution)
            // =========================================================================
            else if (mgaWorkflowMode == 1) {
                // 出发与目标天体选择
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_departure"), labelValueStyle, GUILayout.Width(70));
                var curDep = SolarSystemCatalog.GetPlanet(mgaConfig.DepartureBody);
                if (GUILayout.Button(I18n.Format("mga_planet_btn_short", curDep.Icon, curDep.GetDisplayName(), curDep.Name), actionButtonStyle, GUILayout.Height(22))) {
                    OpenPlanetPicker(0, I18n.T("mga_picker_departure"));
                }
                GUILayout.Space(8);
                GUILayout.Label(I18n.T("mga_destination"), labelValueStyle, GUILayout.Width(70));
                var curDest = SolarSystemCatalog.GetPlanet(mgaConfig.DestinationBody);
                if (GUILayout.Button(I18n.Format("mga_planet_btn_short", curDest.Icon, curDest.GetDisplayName(), curDest.Name), actionButtonStyle, GUILayout.Height(22))) {
                    OpenPlanetPicker(1, I18n.T("mga_picker_destination"));
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label(I18n.T("mga_hist_header"), labelValueStyle);
                
                // 2 行 x 3 列整齐排布，杜绝水平挤压与换行混乱
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("mga_mission_cassini"), secondaryButtonStyle, GUILayout.Height(20))) {
                    LoadHistoricalMissionPreset("Cassini");
                }
                if (GUILayout.Button(I18n.T("mga_mission_galileo"), secondaryButtonStyle, GUILayout.Height(20))) {
                    LoadHistoricalMissionPreset("Galileo");
                }
                if (GUILayout.Button(I18n.T("mga_mission_bepi"), secondaryButtonStyle, GUILayout.Height(20))) {
                    LoadHistoricalMissionPreset("BepiColombo");
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("mga_mission_rosetta"), secondaryButtonStyle, GUILayout.Height(20))) {
                    LoadHistoricalMissionPreset("Rosetta");
                }
                if (GUILayout.Button(I18n.T("mga_mission_juno"), secondaryButtonStyle, GUILayout.Height(20))) {
                    LoadHistoricalMissionPreset("Juno");
                }
                if (GUILayout.Button(I18n.T("mga_mission_mariner"), secondaryButtonStyle, GUILayout.Height(20))) {
                    LoadHistoricalMissionPreset("Mariner");
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                // 候选序列推荐列表 (带直观推荐标签)
                if (generatedCandidateChains.Count > 0) {
                    GUILayout.Label(I18n.T("mga_suggest_header"), labelValueStyle);
                    for (int i = 0; i < Math.Min(3, generatedCandidateChains.Count); i++) {
                        var chain = generatedCandidateChains[i];
                        string tag = (i == 0) ? I18n.T("mga_chain_lowest") : (i == 1 ? I18n.T("mga_chain_fast") : I18n.T("mga_chain_single"));
                        string summary = $"{tag}{mgaConfig.DepartureBody} -> " + string.Join(" -> ", chain);
                        bool isSelected = (selectedCandidateChainIndex == i);
                        GUIStyle chainStyle = isSelected ? actionButtonStyle : secondaryButtonStyle;
                        if (GUILayout.Button(summary, chainStyle, GUILayout.Height(20))) {
                            selectedCandidateChainIndex = i;
                            mgaConfig.FlybySequence = new List<string>(chain);
                            customChainInput = string.Join(",", mgaConfig.FlybySequence);
                            RefreshMgaCanvas();
                        }
                    }
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // 5. 当前激活借力序列可视化装配与自定义 (仅在引力助推模式下显示)
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_chain_header"), labelValueStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_append_flyby"), actionButtonStyle, GUILayout.Width(110), GUILayout.Height(18))) {
                    OpenPlanetPicker(3, I18n.T("mga_picker_append"));
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_clear_flyby"), secondaryButtonStyle, GUILayout.Width(75), GUILayout.Height(18))) {
                    mgaConfig.FlybySequence = new List<string> { mgaConfig.DestinationBody };
                    customChainInput = mgaConfig.DestinationBody;
                    RefreshMgaCanvas();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // 流式展示当前完整链路
                var fullDisplayChain = new List<string> { mgaConfig.DepartureBody };
                if (mgaConfig.FlybySequence != null) fullDisplayChain.AddRange(mgaConfig.FlybySequence);

                GUILayout.BeginHorizontal();
                for (int k = 0; k < fullDisplayChain.Count; k++) {
                    int nodeIdx = k;
                    string bName = fullDisplayChain[nodeIdx];
                    var planet = SolarSystemCatalog.GetPlanet(bName);

                    if (nodeIdx == 0) {
                        if (GUILayout.Button($"{planet.Name} ▼", actionButtonStyle, GUILayout.Height(20))) {
                            OpenPlanetPicker(0, I18n.T("mga_picker_change_dep"));
                        }
                    } else if (nodeIdx == fullDisplayChain.Count - 1) {
                        GUILayout.Label("<color=#94a3b8>-></color>", labelValueStyle, GUILayout.Width(12));
                        if (GUILayout.Button($"{planet.Name} ▼", actionButtonStyle, GUILayout.Height(20))) {
                            OpenPlanetPicker(1, I18n.T("mga_picker_change_dest"));
                        }
                    } else {
                        int flybyIdx = nodeIdx - 1;
                        GUILayout.Label("<color=#94a3b8>-></color>", labelValueStyle, GUILayout.Width(12));
                        if (GUILayout.Button($"{planet.Name} ▼", secondaryButtonStyle, GUILayout.Height(20))) {
                            OpenPlanetPicker(2, I18n.Format("mga_flyby_edit_title", flybyIdx + 1), flybyIdx);
                        }
                        if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(16), GUILayout.Height(20))) {
                            mgaConfig.FlybySequence.RemoveAt(flybyIdx);
                            customChainInput = string.Join(",", mgaConfig.FlybySequence);
                            RefreshMgaCanvas();
                        }
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // 自定义序列文本输入框
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_custom_seq"), labelValueStyle, GUILayout.Width(80));
                customChainInput = GUILayout.TextField(customChainInput, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
                if (GUILayout.Button(I18n.T("btn_apply"), secondaryButtonStyle, GUILayout.Width(45), GUILayout.Height(20))) {
                    if (!string.IsNullOrEmpty(customChainInput)) {
                        var parts = customChainInput.Split(new char[] { ',', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) {
                            mgaConfig.FlybySequence = parts.ToList();
                            RefreshMgaCanvas();
                        }
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }
            // =========================================================================
            // 模式 2: 经典历史任务库 (Missions Library & 3D Review)
            // =========================================================================
            else if (mgaWorkflowMode == 2) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("mga_library_header"), labelValueStyle);
                GUILayout.Space(4);

                var missions = MGABenchmarkSuite.StandardHistoricalMissions;
                foreach (var bCase in missions) {
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{bCase.MissionName}</b> <color=#94a3b8>({bCase.Description})</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(I18n.T("mga_lib_load"), actionButtonStyle, GUILayout.Width(150), GUILayout.Height(22))) {
                        LoadHistoricalMissionPreset(bCase.MissionName);
                        showBenchmarkModal = false;
                        showPorkchopModal = false;
                    }
                    GUILayout.EndHorizontal();

                    string seqStr = $"{bCase.DepartureBody} -> " + string.Join(" -> ", bCase.FlybySequence);
                    GUILayout.Label(I18n.ColorFormat("mga_lib_route", seqStr, bCase.HistoricLaunchDateStr), labelValueStyle);
                    GUILayout.Label(I18n.ColorFormat("mga_lib_dv", bCase.HistoricTotalDeltaVMinMs, bCase.HistoricTotalDeltaVMaxMs, bCase.HistoricFlightDaysMin, bCase.HistoricFlightDaysMax, bCase.HistoricFlightDaysMax / 365.25), labelValueStyle);
                    GUILayout.EndVertical();
                    GUILayout.Space(3);
                }

                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_run_benchmarks"), secondaryButtonStyle, GUILayout.Height(24))) {
                    showBenchmarkModal = true;
                    showPorkchopModal = false;
                    RunHistoricalBenchmarks();
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);

            // 6. 出发与到达参数设置
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_params_header"), sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_opt_window"), actionButtonStyle, GUILayout.Height(20))) {
                double startUT = GMATClient.ParseUtcGregorian(mgaDepDateInput);
                if (startUT <= 0) startUT = Planetarium.GetUniversalTime();
                string targetBody = (mgaConfig.FlybySequence != null && mgaConfig.FlybySequence.Count > 0) ? mgaConfig.FlybySequence[0] : mgaConfig.DestinationBody;
                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(mgaConfig.DepartureBody, targetBody, startUT);
                mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 20.0);
                mgaConfig.LatestDepartureUT = optUT + 86400.0 * 20.0;
                mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
                mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
                
                if (mgaConfig.MinToFDays == null || mgaConfig.MinToFDays.Count == 0) {
                    mgaConfig.MinToFDays = new List<double> { Math.Max(20.0, tofDays * 0.8) };
                    mgaConfig.MaxToFDays = new List<double> { tofDays * 1.25 };
                } else {
                    mgaConfig.MinToFDays[0] = Math.Max(20.0, tofDays * 0.8);
                    mgaConfig.MaxToFDays[0] = tofDays * 1.25;
                }
                
                RefreshMgaCanvas();
                ScreenMessages.PostScreenMessage(I18n.Format("mga_window_recommended", GMATClient.FormatUtcGregorian(optUT), approxDV, tofDays), 4.5f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();

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

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_earliest_dep"), labelValueStyle, GUILayout.Width(110));
            mgaDepDateInput = GUILayout.TextField(mgaDepDateInput, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_latest_dep"), labelValueStyle, GUILayout.Width(110));
            mgaEndDateInput = GUILayout.TextField(mgaEndDateInput, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Height(20));
            GUILayout.EndHorizontal();

            // 出发停泊轨道 (PeA / ApA / Inc)
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_dep_park"), labelValueStyle, GUILayout.Width(100));
            GUILayout.Label("<color=#94a3b8>PeA:</color>", labelValueStyle, GUILayout.Width(30));
            string depPeStr = GUILayout.TextField(mgaConfig.DeparturePeAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(50), GUILayout.Height(20));
            if (double.TryParse(depPeStr, out double dpe)) mgaConfig.DeparturePeAKm = dpe;

            GUILayout.Label("<color=#94a3b8>ApA:</color>", labelValueStyle, GUILayout.Width(30));
            string depApStr = GUILayout.TextField(mgaConfig.DepartureApAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(50), GUILayout.Height(20));
            if (double.TryParse(depApStr, out double dap)) mgaConfig.DepartureApAKm = dap;

            GUILayout.Label("<color=#94a3b8>Inc:</color>", labelValueStyle, GUILayout.Width(25));
            string depIncStr = GUILayout.TextField(mgaConfig.DepartureInclinationDeg.ToString("F1"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(40), GUILayout.Height(20));
            if (double.TryParse(depIncStr, out double di)) mgaConfig.DepartureInclinationDeg = Mathf.Clamp((float)di, 0f, 90f);
            GUILayout.Label("<color=#94a3b8>°</color>", labelValueStyle);
            GUILayout.EndHorizontal();

            // 目标到达捕获轨道 (PeA / ApA)
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("mga_arr_orbit"), labelValueStyle, GUILayout.Width(100));
            if (!mgaConfig.NoInsertionBurn) {
                GUILayout.Label("<color=#94a3b8>PeA:</color>", labelValueStyle, GUILayout.Width(30));
                string arrPeStr = GUILayout.TextField(mgaConfig.DestinationPeAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(50), GUILayout.Height(20));
                if (double.TryParse(arrPeStr, out double ape)) mgaConfig.DestinationPeAKm = ape;

                GUILayout.Label("<color=#94a3b8>ApA:</color>", labelValueStyle, GUILayout.Width(30));
                string arrApStr = GUILayout.TextField(mgaConfig.DestinationApAKm.ToString("F0"), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), GUILayout.Height(20));
                if (double.TryParse(arrApStr, out double aap)) mgaConfig.DestinationApAKm = aap;
                GUILayout.Label("<color=#94a3b8>km</color>", labelValueStyle);
            } else {
                GUILayout.Label(I18n.T("mga_flyby_only"), labelValueStyle);
            }
            GUILayout.EndHorizontal();

            // 目标捕获快捷设定按钮 (纯数值填充，零隐式算法)
            GUILayout.BeginHorizontal();
            GUILayout.Space(105);
            if (GUILayout.Button(I18n.T("btn_low_circular"), secondaryButtonStyle, GUILayout.Height(18))) {
                mgaConfig.NoInsertionBurn = false;
                mgaConfig.DestinationApAKm = mgaConfig.DestinationPeAKm;
            }
            if (GUILayout.Button(I18n.T("btn_elliptic_capture"), secondaryButtonStyle, GUILayout.Height(18))) {
                mgaConfig.NoInsertionBurn = false;
                var destP = SolarSystemCatalog.GetPlanet(mgaConfig.DestinationBody);
                double destR = destP != null ? destP.RadiusKm : 6000.0;
                double rp = destR + mgaConfig.DestinationPeAKm;
                // 椭圆捕获轨预设: 偏心率 e=0.95，远心距按解析式 ra = rp·(1+e)/(1-e) 计算 (基线7: 解析式而非经验倍数)
                double ePreset = 0.95;
                double ra = rp * (1.0 + ePreset) / (1.0 - ePreset);
                mgaConfig.DestinationApAKm = Math.Round(ra - destR);
            }
            if (GUILayout.Button(mgaConfig.NoInsertionBurn ? I18n.T("btn_flyby_only_on") : I18n.T("btn_flyby_only_toggle"), secondaryButtonStyle, GUILayout.Height(18))) {
                mgaConfig.NoInsertionBurn = !mgaConfig.NoInsertionBurn;
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

            GUILayout.Space(8);

            // 7. 寻优开始与停止按钮
            GUILayout.BeginHorizontal();
            if (!isMgaOptimizing) {
                if (GUILayout.Button(I18n.T("mga_btn_start"), actionButtonStyle, GUILayout.Height(30))) {
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

            if (GUILayout.Button(I18n.T("btn_3d_view"), (Mathf.Abs(mgaPitch - 35.0f) < 2f) ? actionButtonStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                mgaPitch = 35.0f;
                mgaYaw = 0.0f;
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
                mgaPitch = 35.0f;
                mgaYaw = 0.0f;
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
                // 交互式绘制 Texture 画布 (左键拖拽平移 / 右键拖拽 3D 自由旋转 / 滚轮缩放)
                Rect canvasRect = GUILayoutUtility.GetRect(580f, 340f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                int curW = (int)canvasRect.width;
                int curH = (int)canvasRect.height;
                if (curW > 100 && curH > 100 && (curW != lastCanvasW || curH != lastCanvasH)) {
                    RefreshMgaCanvas(curW, curH);
                }
                if (mgaCanvasTexture == null) RefreshMgaCanvas();

                if (mgaCanvasTexture != null) {
                    GUI.DrawTexture(canvasRect, mgaCanvasTexture, ScaleMode.StretchToFill);

                    Event e = Event.current;
                    if (canvasRect.Contains(e.mousePosition)) {
                        if (e.type == EventType.ScrollWheel) {
                            float zoomDelta = e.delta.y < 0 ? 1.20f : 0.833f;
                            mgaCanvasZoom = Mathf.Clamp(mgaCanvasZoom * zoomDelta, 0.05f, 50f);
                            RefreshMgaCanvas(curW, curH);
                            e.Use();
                        } else if (e.type == EventType.MouseDown) {
                            if (e.button == 0) {
                                isDraggingCanvas = true;
                                lastMouseCanvasPos = e.mousePosition;
                                e.Use();
                            } else if (e.button == 1) {
                                isRotatingCanvas = true;
                                lastMouseCanvasPos = e.mousePosition;
                                e.Use();
                            }
                        } else if (e.type == EventType.MouseDrag) {
                            Vector2 delta = e.mousePosition - lastMouseCanvasPos;
                            if (isDraggingCanvas) {
                                mgaCanvasPan.x += delta.x;
                                mgaCanvasPan.y -= delta.y; // UP drag moves canvas UP
                                lastMouseCanvasPos = e.mousePosition;
                                RefreshMgaCanvas(curW, curH);
                                e.Use();
                            } else if (isRotatingCanvas) {
                                mgaYaw = (mgaYaw + delta.x * 0.45f) % 360.0f;
                                mgaPitch = Mathf.Clamp(mgaPitch - delta.y * 0.45f, 5f, 89.9f); // 基线7.8: 俯仰 5°~89.9°
                                lastMouseCanvasPos = e.mousePosition;
                                RefreshMgaCanvas(curW, curH);
                                e.Use();
                            }
                        } else if (e.type == EventType.MouseUp) {
                            if (e.button == 0) isDraggingCanvas = false;
                            if (e.button == 1) isRotatingCanvas = false;
                            e.Use();
                        }
                    }
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // =========================================================================
            // 底部面板：轨道方案解算明细与机动矢量 (Calculated Trajectory)
            // =========================================================================
            if (selectedMgaSolution != null) {
                GUILayout.BeginVertical(cardBoxStyle);

                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("mga_details_header"), sectionHeaderStyle);
                GUILayout.Space(10);
                if (GUILayout.Button(mgaDetailViewMode == 0 ? I18n.T("mga_detail_phys_active") : I18n.T("mga_detail_phys"), secondaryButtonStyle, GUILayout.Height(20))) mgaDetailViewMode = 0;
                if (GUILayout.Button(mgaDetailViewMode == 1 ? I18n.T("mga_detail_data_active") : I18n.T("mga_detail_data"), secondaryButtonStyle, GUILayout.Height(20))) mgaDetailViewMode = 1;

                GUILayout.FlexibleSpace();
                string depStr = GMATClient.FormatUtcGregorian(selectedMgaSolution.DepartureUT);
                string arrStr = GMATClient.FormatUtcGregorian(selectedMgaSolution.ArrivalUT);
                GUILayout.Label(I18n.ColorFormat("mga_departure_time", depStr), labelValueStyle);
                GUILayout.Space(12);
                GUILayout.Label(I18n.ColorFormat("mga_arrival_time", arrStr), labelValueStyle);
                GUILayout.Space(12);
                GUILayout.Label(I18n.ColorFormat("mga_total_dv", selectedMgaSolution.TotalDeltaVKms * 1000.0), labelValueStyle);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // =========================================================================
                // 行星际转移几何与出射/捕获明细卡片 (Selected Transfer Details)
                // =========================================================================
                var leg0 = selectedMgaSolution.Legs[0];
                double travelDays = selectedMgaSolution.TotalFlightDays;
                double totalSecs = travelDays * 86400.0;
                int tDays = (int)travelDays;
                int tHours = (int)((totalSecs % 86400.0) / 3600.0);
                int tMins = (int)((totalSecs % 3600.0) / 60.0);
                int tSecs = (int)(totalSecs % 60.0);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.ColorFormat("mga_transfer_header", leg0.FromBody, mgaConfig.DepartureAltitudeKm, selectedMgaSolution.Legs.Last().ToBody, mgaConfig.DestinationAltitudeKm), labelValueStyle);
                GUILayout.Space(2);

                // Row 1: Departure, Arrival, Travel Time
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_dep_col", depStr), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_arr_col", arrStr), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_travel_col", tDays, tHours, tMins, tSecs), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: Phase Angle, Ejection Angle, Total Δv
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_phase_col", leg0.PhaseAngleDeg), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_ejec_col", leg0.EjectionAngleDeg), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_totaldv_col", selectedMgaSolution.TotalDeltaVKms * 1000.0), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 3: Ejection LAN, Ejection Inclination, Ejection Δv
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_lan_col", leg0.EjectionLANDeg), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_inc_col", leg0.EjectionInclinationDeg), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_ejectdv_col", leg0.EjectionDeltaVKms * 1000.0), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 4: C3, Insertion Inclination, Insertion Δv
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.ColorFormat("mga_c3_col", leg0.C3Kms2), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_capinc_col", leg0.InsertionInclinationDeg), labelValueStyle, GUILayout.Width(250));
                GUILayout.Label(I18n.ColorFormat("mga_capdv_col", selectedMgaSolution.InsertionDeltaVKms * 1000.0), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();

                GUILayout.Space(4);

                if (mgaDetailViewMode == 0) {
                    if (selectedMgaSolution.Events == null || selectedMgaSolution.Events.Count == 0) {
                        selectedMgaSolution.BuildEvents(mgaConfig);
                    }

                    int eventCount = (selectedMgaSolution.Events != null) ? selectedMgaSolution.Events.Count : 0;
                    if (selectedManeuverIndex < 0 || selectedManeuverIndex >= eventCount) selectedManeuverIndex = 0;

                    // 1. Maneuvers 下拉选择器与快速步进器 (100% 对齐 KSP-MGA-Planner 界面)
                    GUILayout.BeginHorizontal(nestedCardBoxStyle);
                    GUILayout.Label(I18n.T("mga_maneuvers_label"), labelValueStyle, GUILayout.Width(85));

                    if (eventCount > 0) {
                        var currentEvt = selectedMgaSolution.Events[selectedManeuverIndex];
                        string ddTitle = $"{currentEvt.Title}  {(showManeuverDropdown ? "▲" : "▼")}";
                        if (GUILayout.Button(ddTitle, actionButtonStyle, GUILayout.Width(250), GUILayout.Height(22))) {
                            showManeuverDropdown = !showManeuverDropdown;
                        }

                        if (GUILayout.Button("◀", secondaryButtonStyle, GUILayout.Width(26), GUILayout.Height(22))) {
                            if (selectedManeuverIndex > 0) {
                                selectedManeuverIndex--;
                                FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                            }
                            showManeuverDropdown = false;
                        }
                        if (GUILayout.Button("▶", secondaryButtonStyle, GUILayout.Width(26), GUILayout.Height(22))) {
                            if (selectedManeuverIndex < eventCount - 1) {
                                selectedManeuverIndex++;
                                FocusOnSelectedManeuver(mgaAutoFocusEnabled);
                            }
                            showManeuverDropdown = false;
                        }

                        GUILayout.Space(6);
                        if (GUILayout.Button(I18n.T("btn_focus_view"), actionButtonStyle, GUILayout.Width(80), GUILayout.Height(22))) {
                            FocusOnSelectedManeuver(autoCenter: true);
                        }
                        mgaAutoFocusEnabled = GUILayout.Toggle(mgaAutoFocusEnabled, I18n.T("mga_auto_lock"), GUILayout.Width(75));

                        GUILayout.Space(8);
                        string badgeText = currentEvt.EventType switch {
                            MGAEventType.Escape => I18n.T("mga_badge_escape"),
                            MGAEventType.Flyby => I18n.T("mga_badge_flyby"),
                            MGAEventType.DSM => I18n.T("mga_badge_dsm"),
                            MGAEventType.Circularization => I18n.T("mga_badge_capture"),
                            _ => ""
                        };
                        GUILayout.Label(badgeText, labelValueStyle);
                    } else {
                        GUILayout.Label(I18n.T("mga_no_events"), labelValueStyle);
                    }

                    GUILayout.FlexibleSpace();
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

                    // 2. 选中事件的物理量精确明细展示 (区分 Escape / Flyby / DSM / Circularization)
                    if (eventCount > 0 && selectedManeuverIndex >= 0 && selectedManeuverIndex < eventCount) {
                        var curEv = selectedMgaSolution.Events[selectedManeuverIndex];
                        string dateDisplay = mgaShowMetDates ? 
                            $"MET +{(curEv.EpochUT - selectedMgaSolution.DepartureUT) / 86400.0:F1} {I18n.T("mga_day_unit")}" : 
                            GMATClient.FormatUtcGregorian(curEv.EpochUT);

                        GUILayout.BeginVertical(nestedCardBoxStyle);

                        if (curEv.EventType == MGAEventType.Escape) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_escape_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(360));
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
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(360));
                            string flybyTypeStr = (curEv.DeltaVMs > 0.001) ? 
                                I18n.Format("mga_flyby_powered", curEv.DeltaVMs) : 
                                I18n.T("mga_flyby_ballistic");
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_nature", flybyTypeStr), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_geo", curEv.FlybyPeriapsisAltitudeKm, curEv.FlybyPeriapsisRadiusKm, curEv.FlybyTurningAngleDeg), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_flyby_vinf", curEv.VInfInKms, curEv.VInfOutKms, curEv.IsFeasible ? I18n.T("mga_feasible_ok") : I18n.T("mga_feasible_power")), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        } else if (curEv.EventType == MGAEventType.DSM) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_dsm_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(360));
                            GUILayout.Label(I18n.ColorFormat("mga_dsm_dv", curEv.DeltaVMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_vec3", curEv.ProgradeMs, curEv.NormalMs, curEv.RadialMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        } else if (curEv.EventType == MGAEventType.Circularization) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_cap_time", dateDisplay, curEv.EpochUT), labelValueStyle, GUILayout.Width(360));
                            GUILayout.Label(I18n.ColorFormat("mga_cap_dv", curEv.DeltaVMs), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label(I18n.ColorFormat("mga_cap_orb", curEv.InsertionAltitudeKm, curEv.InsertionInclinationDeg, curEv.VInfInKms), labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.EndVertical();
                    }
                } else {
                    // 数据视图
                    GUILayout.BeginHorizontal(nestedCardBoxStyle);
                    GUILayout.Label(I18n.ColorFormat("mga_data_summary", selectedMgaSolution.FlybyChainSummary, selectedMgaSolution.TotalFlightDays, selectedMgaSolution.LaunchDeltaVKms * 1000.0, selectedMgaSolution.InsertionDeltaVKms * 1000.0), labelValueStyle);
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(8);

                // 执行工具栏
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("btn_load_sequence"), actionButtonStyle, GUILayout.Height(26))) {
                    LoadMgaSolutionToSequence(selectedMgaSolution, vessel);
                }
                GUILayout.Space(8);
                if (GUILayout.Button(I18n.T("btn_export_preset"), secondaryButtonStyle, GUILayout.Height(26))) {
                    string path = GMATMGAScriptGenerator.ExportToTemplatesDirectory(selectedMgaSolution);
                    if (!string.IsNullOrEmpty(path)) {
                        ScreenMessages.PostScreenMessage(I18n.Format("mga_export_ok", Path.GetFileName(path)), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                GUILayout.Space(8);
                if (GUILayout.Button(I18n.T("btn_upload_principia"), secondaryButtonStyle, GUILayout.Height(26))) {
                    UploadMgaSolutionToPrincipia(selectedMgaSolution);
                }
                GUILayout.Space(8);
                if (GUILayout.Button(I18n.T("btn_review_script"), secondaryButtonStyle, GUILayout.Height(26))) {
                    ideCustomBuffer = GMATMGAScriptGenerator.CompileToGmatScript(selectedMgaSolution);
                    activeTab = 1; ideScriptViewMode = 1;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Row 2: 报表与 GMAT 脚本导出工具栏
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(I18n.T("btn_export_script"), actionButtonStyle, GUILayout.Height(24))) {
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
                GUILayout.Space(8);
                if (GUILayout.Button(I18n.T("btn_copy_report"), secondaryButtonStyle, GUILayout.Height(24))) {
                    string report = GenerateDetailedMgaReport(selectedMgaSolution);
                    GUIUtility.systemCopyBuffer = report;
                    ScreenMessages.PostScreenMessage(I18n.T("mga_report_copied"), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.Space(8);
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

                GUILayout.EndVertical();
            }
        }

        private async void StartMgaOptimization() {
            if (mgaWorkflowMode == 0) {
                mgaConfig.FlybySequence = new List<string> { mgaConfig.DestinationBody };
                customChainInput = mgaConfig.DestinationBody;
            }

            isMgaOptimizing = true;
            mgaProgress = 0.05f;
            mgaStatusText = I18n.T("mga_status_start");
            mgaCts = new CancellationTokenSource();

            try {
                var res = await PykepBridge.OptimizeAsync(mgaConfig, (p, msg) => {
                    mgaProgress = p;
                    mgaStatusText = msg;
                }, mgaCts.Token);

                mgaSolutions = res;
                if (mgaSolutions != null && mgaSolutions.Count > 0) {
                    selectedMgaSolution = mgaSolutions[0];
                    selectedManeuverIndex = 0;
                    mgaStatusText = I18n.Format("mga_status_done", mgaSolutions.Count);
                    RefreshMgaCanvas();
                    ScreenMessages.PostScreenMessage(I18n.Format("mga_solve_done", selectedMgaSolution.TotalDeltaVKms * 1000.0), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    mgaStatusText = I18n.T("mga_status_no_sol");
                }
            } catch (Exception ex) {
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

            activeTab = 2;
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

                    GUIStyle retStyle = new GUIStyle(GUI.skin.label) {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 22,
                        fontStyle = FontStyle.Bold
                    };
                    retStyle.normal.textColor = new Color(1.0f, 0.90f, 0.15f, 0.95f);
                    GUI.Label(new Rect(retX - 16, retY - 16, 32, 32), "⌖", retStyle);
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
                        mgaConfig.EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 20.0);
                        mgaConfig.LatestDepartureUT = optUT + 86400.0 * 20.0;
                        mgaDepDateInput = GMATClient.FormatUtcGregorian(mgaConfig.EarliestDepartureUT);
                        mgaEndDateInput = GMATClient.FormatUtcGregorian(mgaConfig.LatestDepartureUT);
                        StartPorkchopComputation(useCurrentDateInputs: true);
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
                    mgaConfig.DepartureInclinationDeg,
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
                        Vector3d dv = new Vector3d(leg.ProgradeMs, leg.RadialMs, leg.NormalMs);
                        bool ok = PrincipiaInterop.InsertOrUpdateBurn(vessel, leg.DepartureUT, dv, 50.0, 310.0, writtenCount, isFrenet: true, originBodyName: leg.FromBody);
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
    }
}
