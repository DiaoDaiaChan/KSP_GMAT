// ============================================================================
// MGAMasterOptimizer.cs
// Tier 1: 最顶层 MGA 多重引力助推综合优化器与高价值工程参数体系
// 接收面向任务工程的高价值决策参数，自适应转译并调度中层拓扑推演与底层多岛连续进化
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA.Solver {
    /// <summary>
    /// 多目标任务优化导向优先级
    /// </summary>
    public enum ObjectivePriority {
        FuelOptimal,      // 极低能耗导向 (优先将工质 Delta-V 压至绝对极小，允许 ToF 适当放宽)
        TimeOptimal,      // 快速到达导向 (优先缩短总飞行时间，工质在预算内可较为充裕)
        BalancedPareto    // 帕累托综合平衡导向 (依据边际换算比率在燃料与航时之间进行权衡)
    }

    /// <summary>
    /// 算力投资与演化深度预设档位
    /// </summary>
    public enum ComputeBudgetPreset {
        QuickReconnaissance,       // 快速侦察 (低代数、小种群，用于交互式前端低延迟预览出图)
        StandardEngineering,       // 标准工程级寻优 (均衡的种群与代数配置)
        ExtremeDeepSpaceChallenge  // 极限深空突破 (多拓扑深度挖掘、大种群精细演化、严苛物理门禁)
    }

    /// <summary>
    /// 一键工程任务预设枚举
    /// </summary>
    public enum MGAPreset {
        ExtremeDeepSpaceChallenge,       // 极限深空突破 (如 Scenario 9: 3800m/s 地球出射 + 500m/s DSM 水星-海王星极限巡回)
        GrandTour,                       // 外行星连环弹弓巡航 (如 Voyager 2 巨行星引力跳板)
        InnerSystemResonantDeceleration, // 深入内太阳系减速入轨 (如 BepiColombo / 帕克号 多重金星/水星减速共振)
        QuickReconnaissance,             // 快速低开销勘测
        StandardEngineering              // 标准综合工程设计
    }

    /// <summary>
    /// 高价值可调参数体系 (MGA High-Value Parameters)
    /// 聚焦任务工程决策、物理边界、安全走廊与算力配置，屏蔽底层数十个实现细节旋钮
    /// </summary>
    public sealed class MGAHighValueParameters {
        // =========================================================================
        // 维度 1: 推进系统与机动能力预算 (Propulsion Capabilities & Delta-V Budget)
        // =========================================================================

        /// <summary>
        /// 飞船总工质机动能力上限 (m/s)。涵盖发射出射额外机动、中途 DSM 及到达机动。
        /// </summary>
        public double MaxVesselDeltaVMs { get; set; } = 4000.0;

        /// <summary>
        /// 航路中途允许施加的最大深空机动 (DSM) 上限 (m/s)。若为 0.0 则表示自动联动飞船总工质预算 (不设人为单段上限，只要在总预算内均可接受)。
        /// </summary>
        public double MaxDsmDeltaVMs { get; set; } = 0.0;

        /// <summary>
        /// 始发天体停泊轨道 (如地球 LEO) 的最大允许切向注入出射速度增量 (m/s) (C3 门禁)。
        /// </summary>
        public double LaunchDeltaVCeilingMs { get; set; } = 3800.0;

        /// <summary>
        /// 目标天体到达双曲过剩速度上限 (km/s)。若为 0 表示不加限制 (高速飞掠模式)。
        /// </summary>
        public double MaxArrivalVInfKms { get; set; } = 0.0;

        /// <summary>
        /// 强制严格无动力双曲弹弓 (Strict Unpowered Flyby, DeltaV_fb 恒为 0.00 m/s)。
        /// </summary>
        public bool StrictUnpoweredFlyby { get; set; } = true;


        // =========================================================================
        // 维度 2: 引力辅助策略与爬升网络 (Gravity Assist Hierarchy & Adsorption Network)
        // =========================================================================

        /// <summary>
        /// 天体“引力必吃榜”吸附阈值 (0.0 ~ 1.0，以系统引力最强者木星=1.0为基准)。
        /// </summary>
        public double MinGravityPowerRatio { get; set; } = 0.025;

        /// <summary>
        /// 单个天体允许的最大连续共振借力次数 (1 ~ 5)。
        /// </summary>
        public int MaxResonancesPerBody { get; set; } = 5;

        /// <summary>
        /// 是否允许交错双星共振泵升 (Interleaved Resonant Pumping, 如 Earth -> Venus -> Earth -> Venus)。
        /// </summary>
        public bool AllowInterleavedResonances { get; set; } = true;

        /// <summary>
        /// 飞往外太阳系深空 (目标半长轴 > 4 AU) 时，是否强制吸附巨行星超级弹弓。
        /// </summary>
        public bool RequireOuterGasGiantAssist { get; set; } = true;

        /// <summary>
        /// 全航程允许经过的最大借力天体总次数。
        /// </summary>
        public int MaxIntermediateFlybys { get; set; } = 8;


        // =========================================================================
        // 维度 3: 航天器物理走廊与空间环境安全 (Thermal, Solar & Environmental Corridor)
        // =========================================================================

        /// <summary>
        /// 航天器全航程允许的最小近日距 (AU) (防热罩热控安全底线)。
        /// </summary>
        public double MinSolarDistanceAu { get; set; } = 0.28;

        /// <summary>
        /// 航天器全航程允许的最大远日距 (AU) (太阳能电池板工作上限，核动力可放宽至 50)。
        /// </summary>
        public double MaxSolarDistanceAu { get; set; } = 35.0;

        /// <summary>
        /// 飞越借力天体时的大气层/地表最小安全净空高度 (km)。
        /// </summary>
        public double FlybySafeAltitudeMarginKm { get; set; } = 50.0;


        // =========================================================================
        // 维度 4: 任务寿命、航时与时序边界 (Mission Lifetime & Temporal Constraints)
        // =========================================================================

        /// <summary>
        /// 任务全寿命最大飞行时间上限 (年)。
        /// </summary>
        public double MaxTotalMissionDurationYears { get; set; } = 25.0;

        /// <summary>
        /// 任务全寿命最大飞行时间上限 (天)。若 > 0 则优先以此为准，否则由 MaxTotalMissionDurationYears * 365.25 换算。
        /// </summary>
        public double MaxMissionDurationDays { get; set; } = 0.0;

        /// <summary>
        /// 任务允许的最短总飞行时间 (年)。
        /// </summary>
        public double MinTotalMissionDurationYears { get; set; } = 0.5;

        /// <summary>
        /// 任务允许的最短总飞行时间 (天)。若 > 0 则优先以此为准，否则由 MinTotalMissionDurationYears * 365.25 换算。
        /// </summary>
        public double MinMissionDurationDays { get; set; } = 0.0;

        /// <summary>
        /// 发射窗口全局搜索跨度 (天)。
        /// </summary>
        public double LaunchWindowSpanDays { get; set; } = 800.0;

        /// <summary>
        /// 连续同星共振单次最大允许等待时间 (年)。
        /// </summary>
        public double MaxResonanceWaitYears { get; set; } = 3.0;


        // =========================================================================
        // 维度 5: 多目标帕累托权衡与优化偏好 (Multi-Objective Trade-Off & Priority)
        // =========================================================================

        /// <summary>
        /// 任务优化导向优先级。
        /// </summary>
        public ObjectivePriority Priority { get; set; } = ObjectivePriority.FuelOptimal;

        /// <summary>
        /// 帕累托边际换算比率 (m/s 速度增量 / 年 飞行时间)。
        /// </summary>
        public double ParetoTradeoffRatioMsPerYear { get; set; } = 150.0;

        /// <summary>
        /// 发射能级 C3 相对中途深空机动 (DSM) 的惩罚权重。
        /// </summary>
        public double LaunchC3Weight { get; set; } = 1.0;


        // =========================================================================
        // 维度 6: 算力预算、演化深度与双向联动调度 (Compute Budget & Bi-directional Dispatch)
        // =========================================================================

        /// <summary>
        /// 一键算力预设档位。
        /// </summary>
        public ComputeBudgetPreset ComputePreset { get; set; } = ComputeBudgetPreset.StandardEngineering;

        /// <summary>
        /// 竞争拓扑候选池规模 (4 ~ 16)。
        /// </summary>
        public int CandidateTopologyPoolSize { get; set; } = 8;

        /// <summary>
        /// 单拓扑会合窗口上限 (0=不限制; 用于快速验证/预算控制, 截断多窗口以缩短求解时长)。
        /// </summary>
        public int MaxWindows { get; set; } = 0;

        /// <summary>
        /// 单岛进化种群规模 (50 ~ 150)。
        /// </summary>
        public int PopulationPerIsland { get; set; } = 100;

        /// <summary>
        /// 差分进化最大迭代代数 (80 ~ 300)。
        /// </summary>
        public int MaxGenerations { get; set; } = 150;

        /// <summary>
        /// 是否启用拓扑-优化器双向联动：优势拓扑发射历元动态对齐。
        /// </summary>
        public bool EnableBiDirectionalEpochCentering { get; set; } = true;

        /// <summary>
        /// 是否启用早期不可达物理赤字熔断与代数自适应再分配。
        /// </summary>
        public bool EnableEarlyDeficitPruning { get; set; } = true;

        /// <summary>
        /// 深拷贝当前参数副本
        /// </summary>
        public MGAHighValueParameters Clone() {
            return (MGAHighValueParameters)this.MemberwiseClone();
        }

        /// <summary>
        /// 校验参数物理与逻辑一致性
        /// </summary>
        public bool Validate(out List<string> issues) {
            issues = new List<string>();
            if (MaxVesselDeltaVMs < 0) issues.Add("MaxVesselDeltaVMs 必须 >= 0 m/s");
            if (MaxDsmDeltaVMs < 0) issues.Add("MaxDsmDeltaVMs 必须 >= 0 m/s");
            if (LaunchDeltaVCeilingMs < 0) issues.Add("LaunchDeltaVCeilingMs 必须 >= 0 m/s");
            if (MinSolarDistanceAu <= 0.05) issues.Add("MinSolarDistanceAu 过小，可能跌入太阳日冕层 (< 0.05 AU)");
            if (MaxSolarDistanceAu <= MinSolarDistanceAu) issues.Add("MaxSolarDistanceAu 必须大于 MinSolarDistanceAu");
            if (MaxTotalMissionDurationYears < MinTotalMissionDurationYears) issues.Add("MaxTotalMissionDurationYears 必须大于 MinTotalMissionDurationYears");
            if (CandidateTopologyPoolSize < 1) issues.Add("CandidateTopologyPoolSize 必须 >= 1");
            if (PopulationPerIsland < 20) issues.Add("PopulationPerIsland 必须 >= 20");
            if (MaxGenerations < 10) issues.Add("MaxGenerations 必须 >= 10");
            return issues.Count == 0;
        }

        /// <summary>
        /// 快速工程预设工厂
        /// </summary>
        public static MGAHighValueParameters CreatePreset(MGAPreset preset) {
            var p = new MGAHighValueParameters();
            switch (preset) {
                case MGAPreset.ExtremeDeepSpaceChallenge:
                    p.MaxVesselDeltaVMs = 4000.0;
                    p.MaxDsmDeltaVMs = 500.0;
                    p.LaunchDeltaVCeilingMs = 3800.0;
                    p.MaxArrivalVInfKms = 0.0;
                    p.StrictUnpoweredFlyby = true;
                    p.MinGravityPowerRatio = 0.025;
                    p.MaxResonancesPerBody = 5;
                    p.AllowInterleavedResonances = true;
                    p.RequireOuterGasGiantAssist = true;
                    p.MaxIntermediateFlybys = 8;
                    p.MinSolarDistanceAu = 0.28;
                    p.MaxSolarDistanceAu = 35.0;
                    p.ComputePreset = ComputeBudgetPreset.ExtremeDeepSpaceChallenge;
                    p.CandidateTopologyPoolSize = 8;
                    p.PopulationPerIsland = 120;
                    p.MaxGenerations = 200;
                    p.EnableBiDirectionalEpochCentering = true;
                    p.EnableEarlyDeficitPruning = true;
                    p.Priority = ObjectivePriority.FuelOptimal;
                    break;

                case MGAPreset.GrandTour:
                    p.MaxVesselDeltaVMs = 5000.0;
                    p.MaxDsmDeltaVMs = 300.0;
                    p.LaunchDeltaVCeilingMs = 4200.0;
                    p.MaxArrivalVInfKms = 0.0;
                    p.StrictUnpoweredFlyby = true;
                    p.MinGravityPowerRatio = 0.05;
                    p.RequireOuterGasGiantAssist = true;
                    p.MaxTotalMissionDurationYears = 30.0;
                    p.MaxSolarDistanceAu = 45.0;
                    p.ComputePreset = ComputeBudgetPreset.StandardEngineering;
                    p.CandidateTopologyPoolSize = 6;
                    p.PopulationPerIsland = 80;
                    p.MaxGenerations = 120;
                    p.Priority = ObjectivePriority.FuelOptimal;
                    break;

                case MGAPreset.InnerSystemResonantDeceleration:
                    p.MaxVesselDeltaVMs = 4500.0;
                    p.MaxDsmDeltaVMs = 600.0;
                    p.LaunchDeltaVCeilingMs = 3800.0;
                    p.MinSolarDistanceAu = 0.25;
                    p.MaxResonancesPerBody = 6;
                    p.AllowInterleavedResonances = true;
                    p.RequireOuterGasGiantAssist = false;
                    p.ComputePreset = ComputeBudgetPreset.ExtremeDeepSpaceChallenge;
                    p.CandidateTopologyPoolSize = 8;
                    p.PopulationPerIsland = 100;
                    p.MaxGenerations = 150;
                    p.Priority = ObjectivePriority.FuelOptimal;
                    break;

                case MGAPreset.QuickReconnaissance:
                    p.ComputePreset = ComputeBudgetPreset.QuickReconnaissance;
                    p.CandidateTopologyPoolSize = 4;
                    p.PopulationPerIsland = 50;
                    p.MaxGenerations = 60;
                    p.EnableEarlyDeficitPruning = true;
                    break;

                case MGAPreset.StandardEngineering:
                default:
                    p.ComputePreset = ComputeBudgetPreset.StandardEngineering;
                    p.CandidateTopologyPoolSize = 8;
                    p.PopulationPerIsland = 100;
                    p.MaxGenerations = 150;
                    break;
            }
            return p;
        }
    }

    /// <summary>
    /// 高价值参数转译器 (MGA Configuration Translator)
    /// 负责将任务设计师的 MGAHighValueParameters 自适应转译并分发给中底层求解组件
    /// </summary>
    public static class MGAConfigurationTranslator {
        /// <summary>
        /// 转译为底层自适应连续求解器配置 MGAAdaptiveSolverConfig
        /// </summary>
        public static MGAAdaptiveSolverConfig TranslateToAdaptiveSolverConfig(
            string departureBody,
            string arrivalBody,
            List<string> waypointIntentions,
            double searchStartUT,
            double searchEndUT,
            MGAHighValueParameters p)
        {
            if (p == null) p = new MGAHighValueParameters();

            // 1. 自动推导始发与到达停泊轨道
            double depPe = MGADefaults.GetSafeDepartureAltitudeKm(departureBody);
            MGADefaults.GetBodyCaptureAltitudeBounds(arrivalBody, out double arrMinPe, out double arrRecPe, out _);

            bool isFlybyOnly = (p.MaxArrivalVInfKms == 0.0);
            string resolvedCentral = SolarSystemCatalog.ResolveTransferCentralBody(departureBody, arrivalBody);

            var cfg = new MGAAdaptiveSolverConfig {
                CentralBody = !string.IsNullOrEmpty(resolvedCentral) ? resolvedCentral : SolarSystemCatalog.RootStarName,
                DeparturePlanet = departureBody,
                ArrivalPlanet = arrivalBody,
                FlybySequence = waypointIntentions != null ? new List<string>(waypointIntentions) : new List<string>(),
                SearchStartUT = searchStartUT,
                SearchEndUT = searchEndUT > searchStartUT ? searchEndUT : (searchStartUT + p.LaunchWindowSpanDays * MGADefaults.SecondsPerDay),
                DepPeKm = depPe,
                DepApKm = depPe,
                ArrPeKm = arrRecPe > 0 ? arrRecPe : 200.0,
                ArrApKm = arrRecPe > 0 ? arrRecPe : 200.0,
                ArrivalOrbitType = isFlybyOnly ? MGAArrivalOrbitType.Flyby : MGAArrivalOrbitType.HighlyElliptic,
                AutoAdaptGiantPlanetEllipse = true,
                PropulsionType = MGAPropulsionType.ChemicalImpulsive,
                DeparturePolicy = DepartureModePolicy.FreeLaunch,
                NoInsertionBurn = isFlybyOnly,
                EngineMode = (p.MaxDsmDeltaVMs > 0) ? MGAEngineMode.DeepSpaceManeuver1DSM : MGAEngineMode.BallisticImpulsive,
                ConstrainByVesselDeltaV = (p.MaxVesselDeltaVMs > 0),
                VesselMaxDeltaVMs = p.MaxVesselDeltaVMs,
                MaxDsmDeltaVMs = p.MaxDsmDeltaVMs,
                LaunchDeltaVCeilingMs = p.LaunchDeltaVCeilingMs,
                VinfFreeKmS = p.MaxArrivalVInfKms,
                AutoTuneToFBounds = true,
                EnablePermutationsAndInsertions = true,
                EnableAlternatingPumping = p.AllowInterleavedResonances,
                EnableEphemerisReachabilityFilter = true,
                MaxResonances = p.MaxResonancesPerBody,
                MaxCandidateTopologies = p.CandidateTopologyPoolSize,
                MaxWindows = p.MaxWindows,
                PopulationSize = p.PopulationPerIsland,
                MaxGenerations = p.MaxGenerations,
                FlybySafeAtmosphereMarginKm = p.FlybySafeAltitudeMarginKm,
                EnforceSafeFlybyMargin = true,
                EnableSelfHealing = true
            };

            return cfg;
        }
    }

    /// <summary>
    /// 顶层优化器求解结果容器
    /// </summary>
    public sealed class MGAMasterOptimizationResult {
        public bool Success { get; set; } = false;
        public string Message { get; set; } = "";
        public MGASolution BestSolution { get; set; }
        public List<MGASolution> AllSolutions { get; set; } = new List<MGASolution>();
        public List<string> BestTopology { get; set; } = new List<string>();
        public List<List<string>> EvaluatedTopologies { get; set; } = new List<List<string>>();
        public double ComputeTimeSec { get; set; } = 0.0;
        public string DiagnosticReportPath { get; set; } = "";
        public MGAConstraintProfile ConstraintProfile { get; set; }
        public MGAConstraintAuditReport ConstraintReport { get; set; }
    }

    /// <summary>
    /// 最顶层多重引力助推综合优化器 (MGA Master Optimizer)
    /// </summary>
    public sealed class MGAMasterOptimizer {
        /// <summary>
        /// 顶层统一任务求解主入口
        /// </summary>
        public MGAMasterOptimizationResult OptimizeMission(
            string departureBody,
            string arrivalBody,
            List<string> waypointIntentions,
            double searchStartUT,
            double searchEndUT,
            MGAHighValueParameters parameters = null,
            CancellationToken cancellationToken = default,
            Action<float, string> onProgress = null)
        {
            var sw = Stopwatch.StartNew();
            var res = new MGAMasterOptimizationResult();
            var hp = parameters != null ? parameters.Clone() : new MGAHighValueParameters();

            if (!hp.Validate(out var issues)) {
                res.Success = false;
                res.Message = "参数校验不通过: " + string.Join("; ", issues);
                return res;
            }

            // 1. 通过统一约束管理器构建任务约束画像 (SSOT)
            var constraintProfile = MGAConstraintManager.CreateProfile(hp, departureBody, arrivalBody);
            res.ConstraintProfile = constraintProfile;

            // 2. 转译底层求解器配置并注入全量约束
            var solverConfig = MGAConfigurationTranslator.TranslateToAdaptiveSolverConfig(
                departureBody, arrivalBody, waypointIntentions, searchStartUT, searchEndUT, hp);
            MGAConstraintManager.ApplyToSolverConfig(constraintProfile, solverConfig);

            onProgress?.Invoke(0.05f, "已完成高价值参数校验与统一约束模型分发，进入拓扑推演与多岛连续优化...");

            // 3. 调度底层 MGAAdaptiveSolver 进行自适应推导与多岛进化
            var solutions = MGAAdaptiveSolver.Solve(solverConfig, cancellationToken, onProgress);

            sw.Stop();
            res.ComputeTimeSec = sw.Elapsed.TotalSeconds;

            if (solutions != null && solutions.Count > 0) {
                var best = solutions[0];
                res.BestSolution = best;
                res.AllSolutions = solutions;

                // 4. 委托 MGAConstraintManager 开展集中式全景物理与工程合规审计
                var auditReport = MGAConstraintManager.AuditSolution(best, constraintProfile);
                res.ConstraintReport = auditReport;
                res.Success = auditReport.IsFullyFeasible;
                res.Message = auditReport.SummaryText;

                if (best.Legs != null && best.Legs.Count > 0) {
                    res.BestTopology = new List<string> { best.Legs[0].FromBody };
                    foreach (var l in best.Legs) res.BestTopology.Add(l.ToBody);
                }
            } else {
                res.Success = false;
                res.Message = "求解器未能找到符合动力学与物理边界约束的可行解。";
            }

            return res;
        }

        /// <summary>
        /// 一键预设工厂
        /// </summary>
        public static MGAHighValueParameters CreatePreset(MGAPreset preset) {
            return MGAHighValueParameters.CreatePreset(preset);
        }
    }
}
