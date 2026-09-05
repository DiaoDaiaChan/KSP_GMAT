using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using KSPGMAT.MGA;

namespace KSPGMAT.Model {
    /// <summary>
    /// 3D 视口渲染与相机状态数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public class MGAViewportModel {
        public float CanvasZoom { get; set; } = 1.0f;
        public Vector2 CanvasPan { get; set; } = Vector2.zero;
        public float Pitch { get; set; } = 50.0f;
        public float Yaw { get; set; } = -25.0f;
        public bool ShowSois { get; set; } = true;
        public bool ShowMET { get; set; } = false;
        public bool AutoFocus { get; set; } = true;
        public int LastCanvasW { get; set; } = 600;
        public int LastCanvasH { get; set; } = 400;
        public int ScrubberStep { get; set; } = 0; // 0 = 全流程总览, 1..N = 逐步推进/逐条查看
    }

    /// <summary>
    /// MGA 官方标准缺省值注册表 (Single Source of Default Values)
    /// </summary>
    public static class MGADefaults {
        public const double DepartureAltitudeKm = 200.0;
        public const double DestinationAltitudeKm = 200.0;
        public const double LowThrustMaxThrustN = 0.5;
        public const double LowThrustIspSec = 3000.0;
        public const double SpacecraftWetMassKg = 1500.0;
        public const double DsmEtaDefault = 0.5;
        public const double MinSearchYears = 3.0;
    }

    /// <summary>
    /// MGA 任务控制步骤类型 (与 Tab 2 序列设计语言统一)
    /// </summary>
    public enum MGAStepType {
        Departure,   // 始发脱离 (出发天体、停泊轨道、发射历元、C3、逃逸脉冲)
        Transfer,    // 转移巡航 (飞行时间、动力学策略: 弹道/1-DSM/低推力/B平面TCM)
        Maneuver,    // 深空机动 (DSM 历元、脉冲矢量 VNC、时间占比 eta)
        Flyby,       // 行星助推 (飞越天体、近心点高度、转向角、动力助推)
        Arrival      // 目标交会 (目标天体、捕获制动/探测飞越、捕获轨道)
    }

    /// <summary>
    /// MGA 任务步骤条目模型 (SSOT 数据源)
    /// </summary>
    [Serializable]
    public class MGAMissionStep {
        public string id = Guid.NewGuid().ToString();
        public MGAStepType type = MGAStepType.Transfer;
        public string label = "";
        public bool is_collapsed = false;

        // 天体与轨道几何
        public string body_name = "Earth";
        public double periapsis_alt_km = 200.0;
        public double apoapsis_alt_km = 200.0;

        // 历元与时间 (双历元系统)
        public double epoch_ut = 0.0;
        public string epoch_utc_str = "";
        public double tof_days = 150.0;
        public double min_tof_days = 30.0;
        public double max_tof_days = 300.0;

        // 机动与策略
        public MGALegStrategy strategy = MGALegStrategy.Ballistic;
        public MGAFlybyStrategy flyby_strategy = MGAFlybyStrategy.UnpoweredFlyby;
        public double dv_v = 0.0; // VNC V-axis (Prograde)
        public double dv_n = 0.0; // VNC N-axis (Normal)
        public double dv_b = 0.0; // VNC C-axis (Radial)
        public double dsm_eta = 0.5;
        public double target_b_dot_t_km = 0.0;
        public double target_b_dot_r_km = 0.0;

        // 求解结果与回填状态
        public bool is_solved = false;
        public double solved_dv_ms = 0.0;
        public double solved_c3_kms2 = 0.0;
        public double solved_phase_deg = 0.0;
        public bool is_feasible = true;
    }

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

        // 3. 3D 视口相机与渲染状态
        public MGAViewportModel Viewport { get; set; } = new MGAViewportModel();

        // 4. GUI 交互工作流状态
        public int WorkflowMode { get; set; } = 0; // 0: 直达, 1: MGA借力, 2: 历史任务库
        public string DepDateInput { get; set; } = "";
        public string EndDateInput { get; set; } = "";
        public List<List<string>> GeneratedCandidateChains { get; set; } = new List<List<string>>();
        public int SelectedCandidateChainIndex { get; set; } = 0;
        public string CustomChainInput { get; set; } = "";
        public bool DatesInitialized { get; set; } = false;

        // 5. 事件驱动响应委托
        public event Action OnStateChanged;
        public event Action OnSolutionChanged;
        public event Action OnViewportChanged;

        public void NotifyStateChanged() {
            ValidateAndSanitize();
            OnStateChanged?.Invoke();
        }

        public void NotifySolutionChanged(MGASolution newSol) {
            ActiveSolution = newSol;
            if (newSol != null) {
                Solutions = new List<MGASolution> { newSol };
                SelectedManeuverIndex = 0;
                SyncSequenceFromSolution(newSol, Config);
            }
            OnSolutionChanged?.Invoke();
        }

        public void NotifyViewportChanged() {
            OnViewportChanged?.Invoke();
        }

        /// <summary>
        /// 将当前求解出的 MGASolution 完整同步映射到 MGA 任务步骤卡片列表中
        /// </summary>
        public void SyncSequenceFromSolution(MGASolution sol, MGAConfig cfg = null) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return;
            var c = cfg ?? Config;
            var seq = new List<MGAMissionStep>();

            // 1. 始发脱离阶段
            var leg0 = sol.Legs[0];
            seq.Add(new MGAMissionStep {
                type = MGAStepType.Departure,
                body_name = leg0.FromBody,
                epoch_ut = sol.DepartureUT,
                epoch_utc_str = GMATClient.FormatUtcGregorian(sol.DepartureUT),
                periapsis_alt_km = c.DeparturePeAKm > 0 ? c.DeparturePeAKm : 200.0,
                apoapsis_alt_km = c.DepartureApAKm > 0 ? c.DepartureApAKm : 200.0,
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
                    double dsmEta = (leg.ToFDays > 0) ? (leg.DSM.EpochOffsetSec / (leg.ToFDays * 86400.0)) : 0.5;
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
                    min_tof_days = (c.MinToFDays != null && k < c.MinToFDays.Count) ? c.MinToFDays[k] : Math.Max(10.0, leg.ToFDays * 0.7),
                    max_tof_days = (c.MaxToFDays != null && k < c.MaxToFDays.Count) ? c.MaxToFDays[k] : leg.ToFDays * 1.3,
                    strategy = leg.HasDSM ? MGALegStrategy.DeepSpaceManeuver1DSM : (c.EngineMode == MGAEngineMode.LowThrustSimsFlanagan ? MGALegStrategy.ContinuousLowThrust : MGALegStrategy.Ballistic),
                    solved_dv_ms = leg.DeltaVKms * 1000.0,
                    solved_phase_deg = leg.PhaseAngleDeg,
                    label = $"Cruise #{k + 1}: {leg.FromBody} ➔ {leg.ToBody} ({leg.ToFDays:F1}d)",
                    is_solved = true
                });

                // 2.3 中间飞越天体 (非最后到达)
                if (k < sol.Legs.Count - 1) {
                    seq.Add(new MGAMissionStep {
                        type = MGAStepType.Flyby,
                        body_name = leg.ToBody,
                        epoch_ut = leg.ArrivalUT,
                        epoch_utc_str = GMATClient.FormatUtcGregorian(leg.ArrivalUT),
                        periapsis_alt_km = leg.FlybyPeriapsisAltitudeKm > 0 ? leg.FlybyPeriapsisAltitudeKm : 300.0,
                        flyby_strategy = (c.FlybyStrategies != null && k < c.FlybyStrategies.Count) ? c.FlybyStrategies[k] : MGAFlybyStrategy.UnpoweredFlyby,
                        solved_dv_ms = leg.FlybyDeltaVKms * 1000.0,
                        label = $"{leg.ToBody} Flyby (hp={leg.FlybyPeriapsisAltitudeKm:F0} km, δ={leg.FlybyTurningAngleDeg:F1}°)",
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
                periapsis_alt_km = c.DestinationPeAKm > 0 ? c.DestinationPeAKm : 200.0,
                apoapsis_alt_km = c.DestinationApAKm > 0 ? c.DestinationApAKm : 200.0,
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
                cfg.EarliestDepartureUT = Math.Max(0, dep.epoch_ut - 86400.0 * 15.0);
                cfg.LatestDepartureUT = dep.epoch_ut + 86400.0 * 15.0;
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
            if (Config.DeparturePeAKm < 30.0 && Config.DeparturePeAKm > 0.0) {
                Config.DeparturePeAKm = MGADefaults.DepartureAltitudeKm;
                autoFixed = true;
            }
            if (Config.DestinationPeAKm < 30.0 && Config.DestinationPeAKm > 0.0) {
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
            if (v.orbit != null && v.orbit.PeA > 30000.0) {
                double peA = v.orbit.PeA / 1000.0;
                double apA = v.orbit.ApA / 1000.0;
                double meanAltKm = (peA + apA) * 0.5;
                Config.DepartureAltitudeKm = meanAltKm > 30.0 ? meanAltKm : MGADefaults.DepartureAltitudeKm;
                Config.DeparturePeAKm = peA;
                Config.DepartureApAKm = apA;
                Config.DepartureInclinationDeg = Mathf.Clamp((float)Math.Abs(v.orbit.inclination), 0f, 90f);
            }

            double nowUt = (Planetarium.GetUniversalTime() > 0) ? Planetarium.GetUniversalTime() : 0.0;
            Config.EarliestDepartureUT = nowUt;
            Config.LatestDepartureUT = nowUt + 86400.0 * 365.25 * MGADefaults.MinSearchYears;

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

            Config.EarliestDepartureUT = Math.Max(0, launchUT - 86400.0 * 20.0);
            Config.LatestDepartureUT = launchUT + 86400.0 * 20.0;
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
