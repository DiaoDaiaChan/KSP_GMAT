using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.MGA.Telemetry;

namespace KSPGMAT.Model {
    /// <summary>
    /// 行星际多引力助推全局底层状态管理核心引擎 (Single Source of Truth)
    /// 遵循基线0/7/9规范，实现 MGA 全局参数的统一生命周期管理、自动合法性校验与事件驱动刷新
    /// </summary>
    public class MGAStateManager {
        private static MGAStateManager _instance;
        public static MGAStateManager Current => _instance ?? (_instance = new MGAStateManager());

        // 1. 核心动力学与任务链路约束 (SSOT)
        public MGAConfig Config { get; set; } = new MGAConfig();

        // 2. 任务序列步骤列表 (与 Tab 2 序列对齐设计语言)
        public List<MGAMissionStep> MissionSequence { get; set; } = new List<MGAMissionStep>();

        // 3. 当前有效最优方案与候选解池
        public MGASolution ActiveSolution { get; set; }
        public List<MGASolution> Solutions { get; set; } = new List<MGASolution>();
        public int SelectedManeuverIndex { get; set; } = 0;

        // 4. 3D 视口相机与渲染状态
        public MGAViewportModel Viewport { get; set; } = new MGAViewportModel();
        public CraftTelemetry Telemetry { get; set; } = new CraftTelemetry();

        // 5. GUI 交互工作流状态 (0: 极速直达, 1: MGA 行星借力规划, 2: 任务序列工作台)
        public MGAUIInteractionState UI { get; set; } = new MGAUIInteractionState();
        private int _workflowMode = 0;
        public int WorkflowMode { 
            get => _workflowMode; 
            set => _workflowMode = (value == 3) ? 2 : value; 
        }
        public string DepDateInput { get; set; } = "";
        public string EndDateInput { get; set; } = "";
        public List<List<string>> GeneratedCandidateChains { get; set; } = new List<List<string>>();
        public int SelectedCandidateChainIndex { get; set; } = 0;
        public string CustomChainInput { get; set; } = "";
        public bool DatesInitialized { get; set; } = false;

        public CancellationTokenSource CurrentCts { get; set; }

        /// <summary>
        /// 统一协同取消当前正在运行的后台差分演化或序列求解任务
        /// </summary>
        public void CancelOptimization() {
            try {
                if (CurrentCts != null && !CurrentCts.IsCancellationRequested) {
                    CurrentCts.Cancel();
                }
            } catch { }
            IsOptimizing = false;
            IsSeqSolving = false;
            NotifyStateChanged();
        }

        /// <summary>
        /// 从当前 MissionSequence 统一提取动力学约束并全量同步到当前 Config 中 (SSOT 闭环)
        /// 消除 UI 表现层中手工逐行赋值 12 行属性的散落代码
        /// </summary>
        public void ApplySequenceToConfig() {
            var extracted = ExtractConfigFromSequence();
            Config.DepartureBody = extracted.DepartureBody;
            Config.DestinationBody = extracted.DestinationBody;
            Config.DeparturePeAKm = extracted.DeparturePeAKm;
            Config.DepartureApAKm = extracted.DepartureApAKm;
            Config.DestinationPeAKm = extracted.DestinationPeAKm;
            Config.DestinationApAKm = extracted.DestinationApAKm;
            Config.EarliestDepartureUT = extracted.EarliestDepartureUT;
            Config.LatestDepartureUT = extracted.LatestDepartureUT;
            Config.DepartureSearchDays = Math.Max(1.0, (Config.LatestDepartureUT - Config.EarliestDepartureUT) / MGADefaults.SecondsPerDay);
            Config.FlybySequence = new List<string>(extracted.FlybySequence);
            Config.MinToFDays = new List<double>(extracted.MinToFDays);
            Config.MaxToFDays = new List<double>(extracted.MaxToFDays);
            Config.LegStrategies = new List<MGALegStrategy>(extracted.LegStrategies);
            Config.FlybyStrategies = new List<MGAFlybyStrategy>(extracted.FlybyStrategies);
            NotifyStateChanged();
        }

        /// <summary>
        /// 统一更新飞掠天体序列，并自动执行策略维度同步与全量状态变更通知
        /// </summary>
        public void UpdateFlybySequence(List<string> newSequence) {
            Config.FlybySequence = newSequence != null ? new List<string>(newSequence) : new List<string>();
            CustomChainInput = string.Join(",", Config.FlybySequence);
            int legCount = Math.Max(1, Config.FlybySequence.Count);
            Config.SyncStrategiesWithChain(legCount);
            NotifyStateChanged();
        }

        // 6. 全面接入的高级分析与模态框交互状态 (SSOT)
        public float ParetoSliderVal { get; set; } = 0f;
        public int DetailViewMode { get; set; } = 0; // 0 = 物理明细, 1 = 原始数据
        public bool ShowManeuverDropdown { get; set; } = false;
        public bool ShowPorkchopModal { get; set; } = false;
        public bool ShowBenchmarkModal { get; set; } = false;
        public bool ShowSchemeComparisonPanel { get; set; } = false;
        public int SelectedBenchmarkIndex { get; set; } = 0;
        public string SchemeComparisonBody { get; set; } = "";

        // 7. 猪排图计算与交互状态 (SSOT)
        public PorkchopGrid CurrentPorkchop { get; set; } = null;
        public bool IsComputingPorkchop { get; set; } = false;
        public string PorkchopStatusText { get; set; } = "";
        public Vector2 LastPorkchopHoverPos { get; set; } = Vector2.zero;
        public bool IsHoveringPorkchop { get; set; } = false;

        // 8. 历史基准测试套件状态 (SSOT)
        public List<MGABenchmarkSuite.BenchmarkResult> BenchmarkResults { get; set; } = null;
        public string BenchmarkSummaryText { get; set; } = "";

        // 9. 星历方案比对状态 (SSOT)
        public SchemeComparisonResult CurrentSchemeComparison { get; set; } = null;

        // 10. 异步寻优搜索状态 (SSOT)
        public bool IsOptimizing { get; set; } = false;
        public float OptimizeProgress { get; set; } = 0f;
        public string OptimizeStatusText { get; set; } = "";
        public Vector2 BenchmarkReportScroll { get; set; } = Vector2.zero;

        // 11. 任务序列工作台交互状态 (SSOT)
        public bool IsSeqSolving { get; set; } = false;
        public string SeqSolveStatus { get; set; } = "";
        public int ActiveSequenceMode { get; set; } = 0;
        public Vector2 SeqScrollPos { get; set; } = Vector2.zero;
        public Vector2 SeqRightScrollPos { get; set; } = Vector2.zero;
        public bool SeqTemplatesFoldout { get; set; } = false;
        public string SeqSearchQuery { get; set; } = "";
        public string SeqSelectedCategory { get; set; } = "全部";

        // 12. 事件驱动响应委托 (SSOT 状态同步)
        public event Action OnStateChanged;
        public event Action OnSolutionChanged;
        public event Action OnViewportChanged;
        public static Action OnClearInputCacheRequested;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static double TryGetFlightGlobalsUniversalTime() {
            try {
                if (FlightGlobals.ready) return Planetarium.GetUniversalTime();
            } catch { }
            return 0.0;
        }

        /// <summary>
        /// 全面统一计算当前 3D 视口航天器物理遥测参数 (SSOT)
        /// 严格按引力中心 (Sun 或母行星) 动态选取引力常数 muCentral，彻底消除行星系内速度与距离假性畸变
        /// </summary>
        public CraftTelemetry UpdateTelemetry(float flowProgress, int selectedEventIndex) {
            var sol = ActiveSolution;
            var cfg = Config;
            var telem = new CraftTelemetry();

            string centralBody = sol?.CentralBody ?? cfg?.CentralBody;
            if (string.IsNullOrEmpty(centralBody)) {
                centralBody = SolarSystemCatalog.ResolveTransferCentralBody(cfg?.DepartureBody, cfg?.DestinationBody);
            }
            if (string.IsNullOrEmpty(centralBody)) centralBody = SolarSystemCatalog.RootStarName;
            telem.CentralBody = centralBody;

            bool isInterplanetary = centralBody.Equals(SolarSystemCatalog.RootStarName, StringComparison.OrdinalIgnoreCase) ||
                                    centralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                                    centralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                                    centralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);
            telem.IsInterplanetary = isInterplanetary;

            double muCentral = isInterplanetary ? SolarSystemCatalog.SunMu : SolarSystemCatalog.GetCentralMu(centralBody);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;

            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) {
                telem.HasCraft = false;
                telem.IsScrubbing = false;
                double defaultUt = (cfg != null && cfg.EarliestDepartureUT > 0) ? cfg.EarliestDepartureUT : 0;
                if (defaultUt <= 0) {
                    try {
                        defaultUt = TryGetFlightGlobalsUniversalTime();
                    } catch { }
                }
                telem.MissionUt = defaultUt;
                Telemetry = telem;
                return telem;
            }

            int totalLegs = sol.Legs.Count;
            telem.TotalLegs = totalLegs;
            telem.TotalFlightDays = sol.TotalFlightDays;
            double depUt0 = sol.DepartureUT;

            bool isScrubbing = (flowProgress > 0.0001f);
            telem.IsScrubbing = isScrubbing;

            int activeLegIdx = 0;
            float activeFrac = 0f;

            if (isScrubbing) {
                activeLegIdx = Mathf.Clamp((int)Math.Floor(flowProgress), 0, totalLegs - 1);
                activeFrac = flowProgress - activeLegIdx;
                if (flowProgress >= totalLegs) {
                    activeLegIdx = totalLegs - 1;
                    activeFrac = 1.0f;
                }
                var curLeg = sol.Legs[activeLegIdx];
                telem.MissionUt = curLeg.DepartureUT + activeFrac * (curLeg.ArrivalUT - curLeg.DepartureUT);
                telem.HasCraft = true;
            } else if (sol.Events != null && selectedEventIndex >= 0 && selectedEventIndex < sol.Events.Count) {
                var curEv = sol.Events[selectedEventIndex];
                telem.MissionUt = (curEv.EpochUT > 0) ? curEv.EpochUT : depUt0;
                for (int i = 0; i < totalLegs; i++) {
                    if (telem.MissionUt >= sol.Legs[i].DepartureUT && telem.MissionUt <= sol.Legs[i].ArrivalUT) {
                        activeLegIdx = i;
                        double legDt = sol.Legs[i].ArrivalUT - sol.Legs[i].DepartureUT;
                        activeFrac = (legDt > 1) ? (float)((telem.MissionUt - sol.Legs[i].DepartureUT) / legDt) : 0f;
                        break;
                    }
                }
                telem.HasCraft = true;
            } else {
                telem.MissionUt = depUt0;
                activeLegIdx = 0;
                activeFrac = 0f;
                telem.HasCraft = false;
            }

            telem.ActiveLegIndex = activeLegIdx;
            var leg = sol.Legs[activeLegIdx];
            telem.FromBody = leg.FromBody;
            telem.ToBody = leg.ToBody;
            telem.MetDays = Math.Max(0, (telem.MissionUt - depUt0) / MGADefaults.SecondsPerDay);

            // 开普勒/双曲线高精位置与瞬时速度推算 (严格使用引力中心真实常数 muCentral)
            Vector3d r0 = leg.DeparturePosKm;
            Vector3d v0 = leg.DepartureVelKms;
            double totalDt = Math.Max(1.0, leg.ArrivalUT - leg.DepartureUT);

            Vector3d hVec = Vector3d.Cross(r0, v0);
            double hMag = hVec.magnitude;
            double r0Mag = r0.magnitude;
            double v0Mag = v0.magnitude;

            if (hMag > 1e-5 && r0Mag > 1.0) {
                Vector3d eVec = (Vector3d.Cross(v0, hVec) / muCentral) - (r0 / r0Mag);
                double ecc = eVec.magnitude;
                double energy = (v0Mag * v0Mag * 0.5) - (muCentral / r0Mag);
                double a = -muCentral / (2.0 * energy);

                Vector3d P = (ecc > 1e-6) ? (eVec / ecc) : r0.normalized;
                Vector3d W = hVec / hMag;
                Vector3d Q = Vector3d.Cross(W, P).normalized;

                double cosNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, P), -1f, 1f);
                double sinNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, Q), -1f, 1f);
                double nu0 = Math.Atan2(sinNu0, cosNu0);
                if (nu0 < 0) nu0 += 2.0 * Math.PI;

                double dtActive = activeFrac * totalDt;
                if (ecc < 1.0 && a > 0) {
                    double E0 = 2.0 * Math.Atan2(Math.Sqrt(1.0 - ecc) * Math.Sin(nu0 / 2.0), Math.Sqrt(1.0 + ecc) * Math.Cos(nu0 / 2.0));
                    double M0 = E0 - ecc * Math.Sin(E0);
                    double n = Math.Sqrt(muCentral / (a * a * a));
                    double Mc = M0 + n * dtActive;
                    double Ec = Mc;
                    for (int it = 0; it < 14; it++) {
                        double f = Ec - ecc * Math.Sin(Ec) - Mc;
                        double fp = 1.0 - ecc * Math.Cos(Ec);
                        double dE = -f / fp;
                        Ec += dE;
                        if (Math.Abs(dE) < 1e-9) break;
                    }
                    double nuc = 2.0 * Math.Atan2(Math.Sqrt(1.0 + ecc) * Math.Sin(Ec / 2.0), Math.Sqrt(1.0 - ecc) * Math.Cos(Ec / 2.0));
                    double rMagC = a * (1.0 - ecc * Math.Cos(Ec));
                    telem.PositionKm = (activeFrac >= 0.999f) ? leg.ArrivalPosKm : ((rMagC * Math.Cos(nuc)) * P + (rMagC * Math.Sin(nuc)) * Q);
                    double vMagFactor = Math.Sqrt(muCentral * a) / Math.Max(1.0, rMagC);
                    telem.VelocityKmS = vMagFactor * (-Math.Sin(Ec) * P + Math.Sqrt(Math.Max(0, 1.0 - ecc * ecc)) * Math.Cos(Ec) * Q);
                } else if (ecc > 1.0001 && energy > 0) {
                    double a_h = muCentral / (2.0 * energy);
                    double n_h = Math.Sqrt(muCentral / (a_h * a_h * a_h));
                    double sinH0 = Math.Sqrt(ecc * ecc - 1.0) * Math.Sin(nu0) / (1.0 + ecc * Math.Cos(nu0));
                    double H0 = Math.Log(sinH0 + Math.Sqrt(sinH0 * sinH0 + 1.0));
                    double Mh0 = ecc * Math.Sinh(H0) - H0;
                    double Mhc = Mh0 + n_h * dtActive;
                    double Hc = H0;
                    for (int it = 0; it < 16; it++) {
                        double sH = Math.Sinh(Hc);
                        double cH = Math.Cosh(Hc);
                        double f = ecc * sH - Hc - Mhc;
                        double fp = ecc * cH - 1.0;
                        double dH = -f / fp;
                        Hc += dH;
                        if (Math.Abs(dH) < 1e-9) break;
                    }
                    double xP = a_h * (ecc - Math.Cosh(Hc));
                    double yQ = a_h * Math.Sqrt(ecc * ecc - 1.0) * Math.Sinh(Hc);
                    telem.PositionKm = (activeFrac >= 0.999f) ? leg.ArrivalPosKm : (xP * P + yQ * Q);
                    double rMagC = telem.PositionKm.magnitude;
                    double vMagSq = muCentral * (2.0 / Math.Max(1.0, rMagC) + 1.0 / a_h);
                    telem.VelocityKmS = (P * (-Math.Sinh(Hc)) + Q * Math.Cosh(Hc)).normalized * Math.Sqrt(Math.Max(0, vMagSq));
                } else {
                    telem.PositionKm = Vector3d.Lerp(r0, leg.ArrivalPosKm, activeFrac);
                    telem.VelocityKmS = (leg.ArrivalPosKm - r0).normalized * v0.magnitude;
                }
            } else {
                telem.PositionKm = Vector3d.Lerp(r0, leg.ArrivalPosKm, activeFrac);
                telem.VelocityKmS = v0;
            }

            telem.SpeedKmS = telem.VelocityKmS.magnitude;
            telem.DistanceKm = telem.PositionKm.magnitude;
            telem.DistanceAu = telem.DistanceKm / MGADefaults.AU_KM;

            Telemetry = telem;
            return telem;
        }

        /// <summary>
        /// 显式切换中心引力天体系统，自动平滑重置相机缩放与偏航，自适应搜索窗口
        /// </summary>
        public void SetCentralBody(string centralBody, bool resetCamera = true) {
            if (string.IsNullOrEmpty(centralBody)) centralBody = SolarSystemCatalog.RootStarName;
            Config.CentralBody = centralBody;

            bool isInterplanetary = centralBody.Equals(SolarSystemCatalog.RootStarName, StringComparison.OrdinalIgnoreCase) ||
                                    centralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                                    centralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                                    centralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

            Config.DepartureSearchDays = isInterplanetary ? (365.25 * MGADefaults.MinSearchYears) : MGADefaults.SystemSearchDays;
            Config.LatestDepartureUT = Config.EarliestDepartureUT + MGADefaults.SecondsPerDay * Config.DepartureSearchDays;
            EndDateInput = GMATClient.FormatUtcGregorian(Config.LatestDepartureUT);

            // 清除旧引力系统的 ToF 边界缓存，防止行星际与行星系间 ToF 残留产生冲突
            Config.MinToFDays?.Clear();
            Config.MaxToFDays?.Clear();

            if (resetCamera) {
                Viewport.CanvasZoom = 1.0f;
                Viewport.CanvasPan = UnityEngine.Vector2.zero;
            }

            NotifyStateChanged();
            if (resetCamera) NotifyViewportChanged();
        }

        /// <summary>
        /// 设定出发与目标天体，智能无感自动识别引力中心，支持行星际与行星系间秒级无缝自动切换
        /// </summary>
        public void SetDepartureAndDestination(string dep, string dest, bool autoInferSystem = true) {
            if (!string.IsNullOrEmpty(dep)) Config.DepartureBody = dep;
            if (!string.IsNullOrEmpty(dest)) Config.DestinationBody = dest;

            if (autoInferSystem && !string.IsNullOrEmpty(Config.DepartureBody) && !string.IsNullOrEmpty(Config.DestinationBody)) {
                string inferredCb = SolarSystemCatalog.ResolveTransferCentralBody(Config.DepartureBody, Config.DestinationBody);
                if (!string.IsNullOrEmpty(inferredCb) && !inferredCb.Equals(Config.CentralBody, StringComparison.OrdinalIgnoreCase)) {
                    SetCentralBody(inferredCb, resetCamera: true);
                    return;
                }
            }

            NotifyStateChanged();
        }

        /// <summary>
        /// 同步并解析当前 UI 文本框中的出发和结束时间至 Config (SSOT)
        /// </summary>
        public void SyncDepartureDatesFromInputs() {
            double parsedDepUT = GMATClient.ParseUtcGregorian(DepDateInput);
            if (parsedDepUT > 0) {
                Config.EarliestDepartureUT = parsedDepUT;
            }
            double parsedEndUT = GMATClient.ParseUtcGregorian(EndDateInput);
            if (parsedEndUT > 0) {
                Config.LatestDepartureUT = parsedEndUT;
            }
            if (Config.LatestDepartureUT <= Config.EarliestDepartureUT) {
                double searchDays = Config.DepartureSearchDays > 0 ? Config.DepartureSearchDays : MGADefaults.DefaultSearchWindowDays;
                Config.LatestDepartureUT = Config.EarliestDepartureUT + MGADefaults.SecondsPerDay * searchDays;
                EndDateInput = GMATClient.FormatUtcGregorian(Config.LatestDepartureUT);
            } else {
                Config.DepartureSearchDays = (Config.LatestDepartureUT - Config.EarliestDepartureUT) / MGADefaults.SecondsPerDay;
            }
            NotifyStateChanged();
        }

        /// <summary>
        /// 重置视口相机缩放与平移
        /// </summary>
        public void ResetViewport() {
            Viewport.CanvasZoom = 1.0f;
            Viewport.CanvasPan = UnityEngine.Vector2.zero;
            NotifyViewportChanged();
        }

        /// <summary>
        /// 统一安全切换 MGA 工作流子窗口，并触发全量状态自愈、序列准备与输入缓存刷新
        /// </summary>
        public void SwitchWorkflowMode(int newMode) {
            WorkflowMode = newMode;

            if (WorkflowMode == 0) {
                // 直达模式：确保链条单向对准目标天体
                if (Config != null && !string.IsNullOrEmpty(Config.DestinationBody)) {
                    Config.FlybySequence = new List<string> { Config.DestinationBody };
                    CustomChainInput = Config.DestinationBody;
                }
            } else if (WorkflowMode == 1) {
                // MGA 借力规划：确保链路配置与候选链有效
                if (Config != null && (Config.FlybySequence == null || Config.FlybySequence.Count == 0)) {
                    Config.FlybySequence = new List<string> { Config.DestinationBody ?? "Mars" };
                    CustomChainInput = string.Join(",", Config.FlybySequence);
                }
            } else if (WorkflowMode == 2) {
                // 任务序列工作台：若当前已有求解方案，立即同步映射为步骤卡片；若无解且无卡片，自动生成初始骨架
                if (ActiveSolution != null && (MissionSequence == null || MissionSequence.Count == 0)) {
                    SyncSequenceFromSolution(ActiveSolution, Config);
                } else if (MissionSequence == null || MissionSequence.Count == 0) {
                    GenerateDefaultSequenceFromConfig();
                }
            }

            NotifyStateChanged();
        }

        public void NotifyStateChanged() {
            ValidateAndSanitize();
            OnClearInputCacheRequested?.Invoke();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// 方案变更全局通知 (SSOT 闭环)：保持候选解池完整性，并自动联动停泊轨锁相优化与事件同步
        /// </summary>
        public void NotifySolutionChanged(MGASolution newSol) {
            ActiveSolution = newSol;
            if (newSol != null) {
                if (Solutions == null || Solutions.Count == 0) {
                    Solutions = new List<MGASolution> { newSol };
                } else if (!Solutions.Contains(newSol)) {
                    Solutions.Insert(0, newSol);
                }
                SelectedManeuverIndex = 0;

                // 若全局启用了停泊轨锁相，自动对齐当前方案并刷新事件流
                if (Config != null && Config.EnableParkingOrbitPhaseAlignment) {
                    MGAInjectionAligner.AutoAlignSolution(FlightGlobals.ActiveVessel, newSol, Config);
                } else if (newSol.IsPhaseAligned) {
                    MGAInjectionAligner.RestoreUnalignedSolution(newSol, Config);
                } else {
                    newSol.BuildEvents(Config);
                }

                SyncSequenceFromSolution(newSol, Config);
            }
            OnClearInputCacheRequested?.Invoke();
            OnSolutionChanged?.Invoke();
        }

        /// <summary>
        /// 对当前或指定方案执行停泊轨锁相优化 (确保 Events、Legs 及顶层数据完整同步)
        /// </summary>
        public bool AlignSolution(MGASolution sol, Vessel vessel = null) {
            if (sol == null) return false;
            Vessel v = vessel ?? FlightGlobals.ActiveVessel;
            bool ok = MGAInjectionAligner.AutoAlignSolution(v, sol, Config);
            if (ok) {
                SyncSequenceFromSolution(sol, Config);
                NotifyStateChanged();
            }
            return ok;
        }

        /// <summary>
        /// 恢复指定方案为标称未锁相状态
        /// </summary>
        public void RestoreSolution(MGASolution sol) {
            if (sol == null) return;
            MGAInjectionAligner.RestoreUnalignedSolution(sol, Config);
            SyncSequenceFromSolution(sol, Config);
            NotifyStateChanged();
        }

        /// <summary>
        /// 当开启或切换停泊轨锁相时，对候选方案池中的所有方案执行一致性锁相，彻底消除列表与切换割裂
        /// </summary>
        public void AlignAllSolutions(Vessel vessel = null) {
            if (Solutions == null || Solutions.Count == 0) return;
            Vessel v = vessel ?? FlightGlobals.ActiveVessel;
            foreach (var sol in Solutions) {
                if (Config != null && Config.EnableParkingOrbitPhaseAlignment) {
                    MGAInjectionAligner.AutoAlignSolution(v, sol, Config);
                } else {
                    MGAInjectionAligner.RestoreUnalignedSolution(sol, Config);
                }
            }
            Solutions = Solutions.OrderBy(s => s.TotalDeltaVKms).ToList();
            if (Solutions.Count > 0) {
                ActiveSolution = Solutions[0];
            }
            if (ActiveSolution != null) {
                SyncSequenceFromSolution(ActiveSolution, Config);
            }
            NotifyStateChanged();
        }

        public void NotifyViewportChanged() {
            OnViewportChanged?.Invoke();
        }

        /// <summary>
        /// 当用户尚未求解但从直达或借力切换至工作台时，从当前 Config 自动构建默认骨架序列，消除空白
        /// </summary>
        public void GenerateDefaultSequenceFromConfig() {
            if (Config == null) return;
            var seq = new List<MGAMissionStep>();
            double curUt = Config.EarliestDepartureUT > 0 ? Config.EarliestDepartureUT : Planetarium.GetUniversalTime();

            // 1. 始发脱离
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Departure,
                body_name = !string.IsNullOrEmpty(Config.DepartureBody) ? Config.DepartureBody : "Earth",
                epoch_ut = curUt,
                epoch_utc_str = GMATClient.FormatUtcGregorian(curUt),
                periapsis_alt_km = Config.DeparturePeAKm > 0 ? Config.DeparturePeAKm : MGADefaults.DepartureAltitudeKm,
                apoapsis_alt_km = Config.DepartureApAKm > 0 ? Config.DepartureApAKm : MGADefaults.DepartureAltitudeKm,
                label = $"{Config.DepartureBody ?? "Earth"} Departure",
                is_solved = false
            });

            // 2. 中间航段与飞越
            string prevBody = Config.DepartureBody ?? "Earth";
            var chain = Config.FlybySequence != null && Config.FlybySequence.Count > 0 
                ? Config.FlybySequence 
                : new List<string> { Config.DestinationBody ?? "Mars" };

            for (int i = 0; i < chain.Count; i++) {
                string nextBody = chain[i];
                double minT = (Config.MinToFDays != null && i < Config.MinToFDays.Count) ? Config.MinToFDays[i] : MGADefaults.DefaultMinToFDays;
                double maxT = (Config.MaxToFDays != null && i < Config.MaxToFDays.Count) ? Config.MaxToFDays[i] : MGADefaults.DefaultMaxToFDays;
                double nominalTof = (minT + maxT) * 0.5;
                var legStrat = (Config.LegStrategies != null && i < Config.LegStrategies.Count) ? Config.LegStrategies[i] : MGALegStrategy.Ballistic;

                seq.Add(new MGAMissionStep {
                    type = MGAStepType.Transfer,
                    body_name = $"{prevBody} ➔ {nextBody}",
                    epoch_ut = curUt,
                    epoch_utc_str = GMATClient.FormatUtcGregorian(curUt),
                    tof_days = nominalTof,
                    min_tof_days = minT,
                    max_tof_days = maxT,
                    strategy = legStrat,
                    label = $"Cruise #{i + 1}: {prevBody} ➔ {nextBody}",
                    is_solved = false
                });
                curUt += nominalTof * MGADefaults.SecondsPerDay;

                if (i < chain.Count - 1) {
                    var fbStrat = (Config.FlybyStrategies != null && i < Config.FlybyStrategies.Count) ? Config.FlybyStrategies[i] : MGAFlybyStrategy.UnpoweredFlyby;
                    var fbPlanet = SolarSystemCatalog.GetPlanet(nextBody);
                    double safeFbAlt = (fbPlanet != null && fbPlanet.SafeRadiusKm > fbPlanet.RadiusKm)
                        ? Math.Max(MGADefaults.DefaultFlybyAltitudeKm, fbPlanet.SafeRadiusKm - fbPlanet.RadiusKm)
                        : MGADefaults.DefaultFlybyAltitudeKm;

                    seq.Add(new MGAMissionStep {
                        type = MGAStepType.Flyby,
                        body_name = nextBody,
                        epoch_ut = curUt,
                        epoch_utc_str = GMATClient.FormatUtcGregorian(curUt),
                        periapsis_alt_km = safeFbAlt,
                        flyby_strategy = fbStrat,
                        label = $"{nextBody} Flyby",
                        is_solved = false
                    });
                }
                prevBody = nextBody;
            }

            // 3. 目标到达捕获
            string finalDest = !string.IsNullOrEmpty(Config.DestinationBody) ? Config.DestinationBody : chain.Last();
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Arrival,
                body_name = finalDest,
                epoch_ut = curUt,
                epoch_utc_str = GMATClient.FormatUtcGregorian(curUt),
                periapsis_alt_km = Config.DestinationPeAKm > 0 ? Config.DestinationPeAKm : MGADefaults.DestinationAltitudeKm,
                apoapsis_alt_km = Config.DestinationApAKm > 0 ? Config.DestinationApAKm : MGADefaults.DestinationAltitudeKm,
                label = $"{finalDest} Capture",
                is_solved = false
            });

            MissionSequence = seq;
        }

        /// <summary>
        /// 将当前求解出的 MGASolution 完整同步映射到 MGA 任务步骤卡片列表中
        /// </summary>
        public void SyncSequenceFromSolution(MGASolution sol, MGAConfig cfg = null) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return;
            MGAFlybyDynamicsHelper.EnrichSolutionFlybyDynamics(sol);
            var c = cfg ?? Config;
            var seq = new List<MGAMissionStep>();

            // 1. 始发脱离阶段
            var leg0 = sol.Legs[0];
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Departure,
                body_name = leg0.FromBody,
                epoch_ut = sol.DepartureUT,
                epoch_utc_str = GMATClient.FormatUtcGregorian(sol.DepartureUT),
                periapsis_alt_km = c.DeparturePeAKm > 0 ? c.DeparturePeAKm : MGADefaults.DepartureAltitudeKm,
                apoapsis_alt_km = c.DepartureApAKm > 0 ? c.DepartureApAKm : MGADefaults.DepartureAltitudeKm,
                dv_v = leg0.ProgradeMs,
                dv_n = leg0.NormalMs,
                dv_b = leg0.RadialMs,
                solved_dv_ms = sol.LaunchDeltaVKms * 1000.0,
                solved_c3_kms2 = leg0.C3Kms2,
                label = $"{leg0.FromBody} Escape (C3={leg0.C3Kms2:F1} km²/s²)",
                is_solved = true
            });

            // 2. 中间航段与飞越
            for (int k = 0; k < sol.Legs.Count; k++) {
                var leg = sol.Legs[k];

                // 2.1 若包含 DSM 或 TCM，插入深空机动步骤
                if (leg.HasDSM && leg.DSM != null) {
                    double dsmEpoch = leg.DepartureUT + leg.DSM.EpochOffsetSec;
                    double dsmEta = (leg.ToFDays > 0) ? (leg.DSM.EpochOffsetSec / (leg.ToFDays * MGADefaults.SecondsPerDay)) : MGADefaults.DsmEtaDefault;
                    seq.Add(new MGAMissionStep {
                        type = MGAStepType.Maneuver,
                        body_name = leg.FromBody,
                        epoch_ut = dsmEpoch,
                        epoch_utc_str = GMATClient.FormatUtcGregorian(dsmEpoch),
                        dsm_eta = dsmEta,
                        dv_v = leg.DSM.DeltaVMs,
                        solved_dv_ms = leg.DSM.DeltaVMs,
                        label = $"DSM #{k + 1}: {leg.DSM.DeltaVMs:F0} m/s",
                        is_solved = true
                    });
                }

                // 2.2 转移巡航航段
                seq.Add(new MGAMissionStep {
                    type = MGAStepType.Transfer,
                    body_name = $"{leg.FromBody} ➔ {leg.ToBody}",
                    epoch_ut = leg.DepartureUT,
                    epoch_utc_str = GMATClient.FormatUtcGregorian(leg.DepartureUT),
                    tof_days = leg.ToFDays,
                    min_tof_days = (c.MinToFDays != null && k < c.MinToFDays.Count) ? c.MinToFDays[k] : Math.Max(10.0, leg.ToFDays * MGADefaults.ToFMarginMinFactor),
                    max_tof_days = (c.MaxToFDays != null && k < c.MaxToFDays.Count) ? c.MaxToFDays[k] : leg.ToFDays * MGADefaults.ToFMarginMaxFactor,
                    strategy = leg.HasDSM ? MGALegStrategy.DeepSpaceManeuver1DSM : (c.EngineMode == MGAEngineMode.LowThrustSimsFlanagan ? MGALegStrategy.ContinuousLowThrust : MGALegStrategy.Ballistic),
                    solved_dv_ms = leg.DeltaVKms * 1000.0,
                    solved_phase_deg = leg.PhaseAngleDeg,
                    label = $"Cruise #{k + 1}: {leg.FromBody} ➔ {leg.ToBody} ({leg.ToFDays:F1}d)",
                    is_solved = true
                });

                // 2.3 中间飞越天体 (非最后到达)
                if (k < sol.Legs.Count - 1) {
                    var fbPlanet = SolarSystemCatalog.GetPlanet(leg.ToBody);
                    double safeFbAlt = (fbPlanet != null && fbPlanet.SafeRadiusKm > fbPlanet.RadiusKm)
                        ? Math.Max(MGADefaults.DefaultFlybyAltitudeKm, fbPlanet.SafeRadiusKm - fbPlanet.RadiusKm)
                        : MGADefaults.DefaultFlybyAltitudeKm;
                    double actualFbAlt = leg.FlybyPeriapsisAltitudeKm > 0 ? leg.FlybyPeriapsisAltitudeKm : safeFbAlt;

                    seq.Add(new MGAMissionStep {
                        type = MGAStepType.Flyby,
                        body_name = leg.ToBody,
                        epoch_ut = leg.ArrivalUT,
                        epoch_utc_str = GMATClient.FormatUtcGregorian(leg.ArrivalUT),
                        periapsis_alt_km = actualFbAlt,
                        target_b_dot_t_km = leg.BPlane != null ? leg.BPlane.BDotTKm : 0.0,
                        target_b_dot_r_km = leg.BPlane != null ? leg.BPlane.BDotRKm : 0.0,
                        incoming_dha_deg = leg.IncomingDhaDeg,
                        incoming_dec_deg = leg.IncomingDecDeg,
                        flyby_rp_km = leg.FlybyPeriapsisRadiusKm,
                        flyby_turn_deg = leg.FlybyTurningAngleDeg,
                        v_inf_kms = leg.VInfArrivalKms.magnitude,
                        flyby_strategy = (c.FlybyStrategies != null && k < c.FlybyStrategies.Count) ? c.FlybyStrategies[k] : MGAFlybyStrategy.UnpoweredFlyby,
                        solved_dv_ms = leg.FlybyDeltaVKms * 1000.0,
                        label = $"{leg.ToBody} Flyby (hp={actualFbAlt:F0} km, δ={leg.FlybyTurningAngleDeg:F1}°)",
                        is_solved = true
                    });
                }
            }

            // 3. 目标行星捕获交会
            var lastLeg = sol.Legs.Last();
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Arrival,
                body_name = lastLeg.ToBody,
                epoch_ut = lastLeg.ArrivalUT,
                epoch_utc_str = GMATClient.FormatUtcGregorian(lastLeg.ArrivalUT),
                periapsis_alt_km = c.DestinationPeAKm > 0 ? c.DestinationPeAKm : MGADefaults.DestinationAltitudeKm,
                apoapsis_alt_km = c.DestinationApAKm > 0 ? c.DestinationApAKm : MGADefaults.DestinationAltitudeKm,
                solved_dv_ms = sol.InsertionDeltaVKms * 1000.0,
                label = $"{lastLeg.ToBody} Capture (ΔV={sol.InsertionDeltaVKms * 1000.0:F0} m/s)",
                is_solved = true
            });

            MissionSequence = seq;
            NotifyStateChanged();
        }

        /// <summary>
        /// 从 MGA 任务步骤卡片列表反向提取构建 MGAConfig 动力学约束
        /// </summary>
        public MGAConfig ExtractConfigFromSequence() {
            var cfg = new MGAConfig {
                DepartureAltitudeKm = Config.DepartureAltitudeKm,
                DestinationAltitudeKm = Config.DestinationAltitudeKm,
                DeparturePeAKm = Config.DeparturePeAKm,
                DepartureApAKm = Config.DepartureApAKm,
                DestinationPeAKm = Config.DestinationPeAKm,
                DestinationApAKm = Config.DestinationApAKm,
                NoInsertionBurn = Config.NoInsertionBurn,
                EngineMode = Config.EngineMode,
                DsmEtaDefault = Config.DsmEtaDefault,
                LowThrustMaxThrustN = Config.LowThrustMaxThrustN,
                LowThrustIspSec = Config.LowThrustIspSec,
                SpacecraftWetMassKg = Config.SpacecraftWetMassKg,
                TargetBDotTKm = Config.TargetBDotTKm,
                TargetBDotRKm = Config.TargetBDotRKm,
                FlybySequence = new List<string>(),
                MinToFDays = new List<double>(),
                MaxToFDays = new List<double>(),
                LegStrategies = new List<MGALegStrategy>(),
                FlybyStrategies = new List<MGAFlybyStrategy>()
            };

            var dep = MissionSequence.Find(s => s.type == MGAStepType.Departure);
            if (dep != null) {
                cfg.DepartureBody = dep.body_name;
                cfg.DeparturePeAKm = dep.periapsis_alt_km;
                cfg.DepartureApAKm = dep.apoapsis_alt_km;
                cfg.EarliestDepartureUT = Math.Max(0, dep.epoch_ut - MGADefaults.SecondsPerDay * MGADefaults.DefaultSequenceSearchMarginDays);
                cfg.LatestDepartureUT = dep.epoch_ut + MGADefaults.SecondsPerDay * MGADefaults.DefaultSequenceSearchMarginDays;
            }

            var arr = MissionSequence.Find(s => s.type == MGAStepType.Arrival);
            if (arr != null) {
                cfg.DestinationBody = arr.body_name;
                cfg.DestinationPeAKm = arr.periapsis_alt_km;
                cfg.DestinationApAKm = arr.apoapsis_alt_km;
            }

            var flybys = MissionSequence.Where(s => s.type == MGAStepType.Flyby).ToList();
            foreach (var fb in flybys) {
                cfg.FlybySequence.Add(fb.body_name);
                cfg.FlybyStrategies.Add(fb.flyby_strategy);
            }
            if (arr != null) cfg.FlybySequence.Add(arr.body_name);

            var transfers = MissionSequence.Where(s => s.type == MGAStepType.Transfer).ToList();
            foreach (var tr in transfers) {
                cfg.MinToFDays.Add(Math.Max(5.0, tr.min_tof_days));
                cfg.MaxToFDays.Add(Math.Max(tr.min_tof_days + 1.0, tr.max_tof_days));
                cfg.LegStrategies.Add(tr.strategy);
            }

            return cfg;
        }

        /// <summary>
        /// 针对整个 MGA 约束执行物理合法性检查与自愈修正 (Self-Healing Validation)
        /// </summary>
        public bool ValidateAndSanitize() {
            bool autoFixed = false;
            if (Config == null) {
                Config = new MGAConfig();
                autoFixed = true;
            }

            // 停泊轨道近拱点高度自愈下限
            if (Config.DeparturePeAKm < MGADefaults.MinSafeAltitudeKm && Config.DeparturePeAKm > 0.0) {
                Config.DeparturePeAKm = MGADefaults.DepartureAltitudeKm;
                autoFixed = true;
            }
            if (Config.DestinationPeAKm < MGADefaults.MinSafeAltitudeKm && Config.DestinationPeAKm > 0.0) {
                Config.DestinationPeAKm = MGADefaults.DestinationAltitudeKm;
                autoFixed = true;
            }

            // 低推力推进剂参数校验
            if (Config.LowThrustIspSec <= 0.0) {
                Config.LowThrustIspSec = MGADefaults.LowThrustIspSec;
                autoFixed = true;
            }
            if (Config.LowThrustMaxThrustN <= 0.0) {
                Config.LowThrustMaxThrustN = MGADefaults.LowThrustMaxThrustN;
                autoFixed = true;
            }
            if (Config.SpacecraftWetMassKg <= 0.0) {
                Config.SpacecraftWetMassKg = MGADefaults.SpacecraftWetMassKg;
                autoFixed = true;
            }

            // 自动同步异构分段与交会节点策略列表长度
            int legCount = Math.Max(1, (Config.FlybySequence != null && Config.FlybySequence.Count > 0) ? Config.FlybySequence.Count : 1);
            Config.SyncStrategiesWithChain(legCount);

            return autoFixed;
        }

        /// <summary>
        /// 从当前操控飞船轨道状态自动同步起算天体与停泊轨道
        /// </summary>
        public void SyncFromActiveVessel(Vessel v) {
            if (v == null || v.mainBody == null) return;

            Config.DepartureBody = v.mainBody.name;
            if (v.orbit != null && v.orbit.PeA > MGADefaults.KspOrbitPeAMinSafeM) {
                double peA = v.orbit.PeA / 1000.0;
                double apA = v.orbit.ApA / 1000.0;
                double meanAltKm = (peA + apA) * 0.5;
                Config.DepartureAltitudeKm = meanAltKm > MGADefaults.MinSafeAltitudeKm ? meanAltKm : MGADefaults.DepartureAltitudeKm;
                Config.DeparturePeAKm = peA;
                Config.DepartureApAKm = apA;
                Config.DepartureInclinationDeg = Mathf.Clamp((float)Math.Abs(v.orbit.inclination), 0f, 90f);
                Config.DepartureRAANDeg = (float)v.orbit.LAN;
                Config.DepartureAOPDeg = (float)v.orbit.argumentOfPeriapsis;
                Config.DepartureEccentricity = v.orbit.eccentricity;
            } else {
                bool isKerbin = v.mainBody.name.Equals("Kerbin", StringComparison.OrdinalIgnoreCase);
                Config.DepartureInclinationDeg = isKerbin ? MGADefaults.DefaultKerbinInclinationDeg : MGADefaults.DefaultEarthInclinationDeg;
            }

            double nowUt = (Planetarium.GetUniversalTime() > 0) ? Planetarium.GetUniversalTime() : 0.0;
            if (PrincipiaInterop.IsPrincipiaAvailable()) {
                double pTime = PrincipiaInterop.GetCurrentPrincipiaTime();
                if (pTime > 0) nowUt = pTime;
            }
            bool isInterplanetary = string.IsNullOrEmpty(Config.CentralBody) ||
                                    Config.CentralBody.Equals(SolarSystemCatalog.RootStarName, StringComparison.OrdinalIgnoreCase) ||
                                    Config.CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                                    Config.CentralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                                    Config.CentralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);
            double searchDays = isInterplanetary ? (365.25 * MGADefaults.MinSearchYears) : MGADefaults.SystemSearchDays;
            Config.DepartureSearchDays = searchDays;
            Config.EarliestDepartureUT = nowUt;
            Config.LatestDepartureUT = nowUt + MGADefaults.SecondsPerDay * searchDays;

            DepDateInput = GMATClient.FormatUtcGregorian(Config.EarliestDepartureUT);
            EndDateInput = GMATClient.FormatUtcGregorian(Config.LatestDepartureUT);
            DatesInitialized = true;
            NotifyStateChanged();
        }

        /// <summary>
        /// 加载历史基准任务预设并自动重置全套状态
        /// </summary>
        public void LoadPreset(MGABenchmarkSuite.BenchmarkCase bCase) {
            if (bCase == null) return;

            Config.DepartureBody = bCase.DepartureBody;
            Config.DestinationBody = bCase.FlybySequence.Last();
            Config.FlybySequence = new List<string>(bCase.FlybySequence);
            CustomChainInput = string.Join(",", Config.FlybySequence);

            double launchUT = GMATClient.ParseUtcGregorian(bCase.HistoricLaunchDateStr);
            if (launchUT <= 0) launchUT = bCase.HistoricLaunchUT;

            Config.EarliestDepartureUT = Math.Max(0, launchUT - MGADefaults.SecondsPerDay * MGADefaults.PresetSearchMarginDays);
            Config.LatestDepartureUT = launchUT + MGADefaults.SecondsPerDay * MGADefaults.PresetSearchMarginDays;
            DepDateInput = GMATClient.FormatUtcGregorian(Config.EarliestDepartureUT);
            EndDateInput = GMATClient.FormatUtcGregorian(Config.LatestDepartureUT);

            Config.DepartureAltitudeKm = bCase.DepAltKm;
            Config.DestinationAltitudeKm = bCase.ArrAltKm;
            Config.NoInsertionBurn = bCase.NoInsertionBurn;
            Config.EngineMode = bCase.EngineMode;
            Config.DsmEtaDefault = bCase.DsmEtaDefault;
            Config.LowThrustMaxThrustN = bCase.LowThrustMaxThrustN;
            Config.LowThrustIspSec = bCase.LowThrustIspSec;
            Config.TargetBDotTKm = bCase.TargetBDotTKm;
            Config.TargetBDotRKm = bCase.TargetBDotRKm;
            Config.LegStrategies = (bCase.LegStrategies != null) ? new List<MGALegStrategy>(bCase.LegStrategies) : null;
            Config.FlybyStrategies = (bCase.FlybyStrategies != null) ? new List<MGAFlybyStrategy>(bCase.FlybyStrategies) : null;

            if (bCase.HistoricToFDays != null && bCase.HistoricToFDays.Count > 0) {
                Config.MinToFDays = bCase.HistoricToFDays.Select(d => Math.Max(10.0, d * 0.9)).ToList();
                Config.MaxToFDays = bCase.HistoricToFDays.Select(d => d * 1.1).ToList();
            }

            var bRes = MGABenchmarkSuite.RunBenchmark(bCase);
            if (bRes != null && bRes.Solution != null) {
                NotifySolutionChanged(bRes.Solution);
            } else {
                NotifyStateChanged();
            }
        }
    }
}
