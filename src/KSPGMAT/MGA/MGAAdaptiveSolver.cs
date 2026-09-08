// ============================================================================
// MGAAdaptiveSolver.cs
// 权威自适应双向 MGA 求解器与智能数值自愈调度引擎
// 
// 核心理论与文献落地:
// 1. 基石: Dario Izzo (ESA ACT, 2015) "Revisiting Lambert's Problem" (Householder-4)
// 2. 模块 A: Dario Izzo et al. (Springer 2012) "The Generalized Island Model" (Ring Topology)
// 3. 模块 B: Dario Izzo et al. (2018) "Machine Learning in Trajectory Design" (Surrogate Pre-Filter)
// 4. 模块 C: Daniel Hennes & Dario Izzo (IJCAI 2015) "Interplanetary Trajectory Planning with MCTS"
// 5. 模块 D: Dario Izzo et al. (AIAA JGCD 2020) "Real-Time Guidance via DNN (G&CNets) & BGOE"
//
// 基线红线: 底层 100% 官方 ESA kep3 Native C++ 闭环动力学 (零托管数学公式自编)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    #region Configuration Contract

    public enum ToFEncodingMode
    {
        Direct,
        Alpha,
        Eta
    }

    public enum DepartureModePolicy
    {
        FreeLaunch,   // 模式 1: 自由发射规划 (park_inc_deg = -1, 纯切向无伪变面)
        OrbitLocked   // 模式 2: 在轨停泊锁定 (严格计算 3D 变面法向夹角)
    }

    public enum LambertDirectionPreference
    {
        Prograde,
        Retrograde
    }

    public enum ArchipelagoTopology
    {
        Ring,
        FullyConnected,
        Star
    }

    /// <summary>
    /// MGA 自适应求解器高内聚输入配置契约
    /// </summary>
    public sealed class MGAAdaptiveSolverConfig
    {
        // 1. 轨道动力学与几何
        public string CentralBody { get; set; } = "Sun";
        public string DeparturePlanet { get; set; } = "Earth";
        public string ArrivalPlanet { get; set; } = "Mars";
        public List<string> FlybySequence { get; set; } = new List<string>();
        public DepartureModePolicy DeparturePolicy { get; set; } = DepartureModePolicy.FreeLaunch;
        public double DepPeKm { get; set; } = 200.0;
        public double DepApKm { get; set; } = 200.0;
        public double DepIncDeg { get; set; } = -1.0; // 默认 -1 触发纯切向
        public double ArrPeKm { get; set; } = 200.0;
        public double ArrApKm { get; set; } = 200.0;
        public bool NoInsertionBurn { get; set; } = false; // 高速飞掠模式 (Flyby Only / 终点刹车为0)
        public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive; // 纯弹道 vs 1-DSM深空机动
        public List<MGALegStrategy> LegStrategies { get; set; } = null;
        public List<MGAFlybyStrategy> FlybyStrategies { get; set; } = null;
        public List<double> DsmEtas { get; set; } = null;
        public double DsmEtaDefault { get; set; } = 0.5;
        public bool ConstrainByVesselDeltaV { get; set; } = false;
        public double VesselMaxDeltaVMs { get; set; } = 0.0;
        public double MaxDsmDeltaVMs { get; set; } = 0.0; // 权威 DSM 机动上限约束 (m/s)，不可超过总任务 ΔV 或用户指定预算
        public double VinfFreeKmS { get; set; } = 0.0;
        public bool AddVinfDep { get; set; } = true;
        public bool AddVinfArr { get; set; } = true;
        public bool EnablePermutationsAndInsertions { get; set; } = true; // 自动乱序排列与中继天体插序尝试
        public bool EnableAlternatingPumping { get; set; } = true;         // 自动衍生交替回折提升PE能量拓扑 (如 E-M-E-M / E-V-E)
        public bool AutoTuneToFBounds { get; set; } = true;                // ToF 上下限全量纳入优化求解器范围 (根据天体公转与共振自适应弹性延展)
        public int MaxResonances { get; set; } = int.MaxValue;             // 单星多次共振阶数上限 (用户明确要求：不设人为上限，动态自适应求解)
        public bool EnableEphemerisReachabilityFilter { get; set; } = true; // 启用基于当前星历与天体引力势阱的物理可达性前置剪枝

        // 2. 搜索历元与时间编码
        public double SearchStartUT { get; set; }
        public double SearchEndUT { get; set; }
        public ToFEncodingMode ToFEncoding { get; set; } = ToFEncodingMode.Alpha;
        public double TotalToFMinDays { get; set; } = 150.0;
        public double TotalToFMaxDays { get; set; } = 400.0;
        public List<(double MinDays, double MaxDays)> LegToFBounds { get; set; } = new List<(double, double)>();

        // 3. 兰伯特底层参数
        public int LambertMaxRevs { get; set; } = 0;
        public LambertDirectionPreference LambertDirection { get; set; } = LambertDirectionPreference.Prograde;

        // 4. 进化算法超参数 (DE / SaDE)
        public int PopulationSize { get; set; } = 80;
        public int MaxGenerations { get; set; } = 150;
        public double DifferentialWeightF { get; set; } = 0.7;
        public double CrossoverProbabilityCR { get; set; } = 0.85;
        public int StagnationLimit { get; set; } = 15;
        public double CostTolerance { get; set; } = 0.5;

        // 5. 多岛拓扑 (Archipelago)
        public int IslandCount { get; set; } = 4;
        public int MigrationPeriod { get; set; } = 10;
        public int MigrationEliteCount { get; set; } = 2;
        public ArchipelagoTopology Topology { get; set; } = ArchipelagoTopology.Ring;

        // 6. MCTS 离散借力链超参数 (IJCAI 2015)
        public double MctsEpsilon { get; set; } = 0.02;       // 0.011 ~ 0.0406
        public double MctsMaxDeltaVKmS { get; set; } = 10.0; // 10 km/s 可行性硬门禁
        public double MctsGridStepDeg { get; set; } = 22.5;  // 历表网格 11.25° ~ 90°
        public int MctsMaxRollouts { get; set; } = 50000;

        // 7. 物理健康度与自愈门禁 (JGCD 2020 / 工程自愈)
        public bool EnableSelfHealing { get; set; } = true;
        public double HohmannEnergyCeilingFactor { get; set; } = 1.35;
        public double SelfHealingREdThreshold { get; set; } = 0.01;
        public double HealingGridResolutionDays { get; set; } = 0.5;
        public double HealingWindowSpanDays { get; set; } = 30.0;

        // 8. 多体引力流形低能捕获与弱稳定性边界 (WSB / Invariant Manifolds)
        public bool EnableLowEnergyManifoldCapture { get; set; } = false; // 水星/月球/深势阱低能捕获模式

        // 9. B 平面打靶与协方差摄动分析导航闭环 (B-Plane Covariance & Navigational Robustness)
        public bool EnableBPlaneCovarianceAnalysis { get; set; } = true;
        public double InitialPosSigmaKm { get; set; } = 10.0;
        public double InitialVelSigmaMs { get; set; } = 0.05;
        public double FlybySafeAtmosphereMarginKm { get; set; } = 50.0;
        public bool EnforceSafeFlybyMargin { get; set; } = true; // 强制 3-sigma 安全防撞门禁

        public MGAAdaptiveSolverConfig Clone()
        {
            var c = (MGAAdaptiveSolverConfig)this.MemberwiseClone();
            c.FlybySequence = new List<string>(this.FlybySequence);
            c.NoInsertionBurn = this.NoInsertionBurn;
            c.EngineMode = this.EngineMode;
            c.LegStrategies = this.LegStrategies != null ? new List<MGALegStrategy>(this.LegStrategies) : null;
            c.FlybyStrategies = this.FlybyStrategies != null ? new List<MGAFlybyStrategy>(this.FlybyStrategies) : null;
            c.DsmEtas = this.DsmEtas != null ? new List<double>(this.DsmEtas) : null;
            c.DsmEtaDefault = this.DsmEtaDefault;
            c.ConstrainByVesselDeltaV = this.ConstrainByVesselDeltaV;
            c.VesselMaxDeltaVMs = this.VesselMaxDeltaVMs;
            c.MaxDsmDeltaVMs = this.MaxDsmDeltaVMs;
            c.EnablePermutationsAndInsertions = this.EnablePermutationsAndInsertions;
            c.EnableAlternatingPumping = this.EnableAlternatingPumping;
            c.AutoTuneToFBounds = this.AutoTuneToFBounds;
            c.MaxResonances = this.MaxResonances;
            c.EnableEphemerisReachabilityFilter = this.EnableEphemerisReachabilityFilter;
            c.LegToFBounds = new List<(double, double)>(this.LegToFBounds);
            c.EnableLowEnergyManifoldCapture = this.EnableLowEnergyManifoldCapture;
            c.EnableBPlaneCovarianceAnalysis = this.EnableBPlaneCovarianceAnalysis;
            c.InitialPosSigmaKm = this.InitialPosSigmaKm;
            c.InitialVelSigmaMs = this.InitialVelSigmaMs;
            c.FlybySafeAtmosphereMarginKm = this.FlybySafeAtmosphereMarginKm;
            c.EnforceSafeFlybyMargin = this.EnforceSafeFlybyMargin;
            return c;
        }

        public static MGAAdaptiveSolverConfig FromMGAConfig(MGAConfig cfg)
        {
            double maxTof = (cfg.MaxToFDays != null && cfg.MaxToFDays.Count > 0) ? cfg.MaxToFDays[0] : 400.0;
            double minTof = (cfg.MinToFDays != null && cfg.MinToFDays.Count > 0) ? cfg.MinToFDays[0] : 150.0;

            var c = new MGAAdaptiveSolverConfig
            {
                CentralBody = cfg.CentralBody,
                DeparturePlanet = cfg.DepartureBody,
                ArrivalPlanet = cfg.DestinationBody,
                FlybySequence = cfg.FlybySequence != null ? new List<string>(cfg.FlybySequence) : new List<string>(),
                SearchStartUT = cfg.EarliestDepartureUT,
                SearchEndUT = cfg.LatestDepartureUT,
                DepPeKm = cfg.DeparturePeAKm,
                DepApKm = cfg.DepartureApAKm,
                DepIncDeg = (cfg.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit) ? -1.0 : cfg.DepartureInclinationDeg,
                DeparturePolicy = (cfg.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit) 
                    ? DepartureModePolicy.FreeLaunch 
                    : DepartureModePolicy.OrbitLocked,
                ArrPeKm = cfg.DestinationPeAKm,
                ArrApKm = cfg.DestinationApAKm,
                NoInsertionBurn = cfg.NoInsertionBurn,
                EnableLowEnergyManifoldCapture = cfg.EnableLowEnergyManifoldCapture,
                EngineMode = cfg.EngineMode,
                LegStrategies = cfg.LegStrategies != null ? new List<MGALegStrategy>(cfg.LegStrategies) : null,
                FlybyStrategies = cfg.FlybyStrategies != null ? new List<MGAFlybyStrategy>(cfg.FlybyStrategies) : null,
                DsmEtas = cfg.DsmEtas != null ? new List<double>(cfg.DsmEtas) : null,
                DsmEtaDefault = cfg.DsmEtaDefault,
                ConstrainByVesselDeltaV = cfg.ConstrainByVesselDeltaV,
                VesselMaxDeltaVMs = cfg.VesselMaxDeltaVMs,
                MaxDsmDeltaVMs = cfg.MaxDsmDeltaVMs > 0 ? cfg.MaxDsmDeltaVMs : (cfg.ConstrainByVesselDeltaV ? cfg.VesselMaxDeltaVMs : 0.0),
                PopulationSize = cfg.PopulationSize,
                MaxGenerations = cfg.MaxGenerations,
                TotalToFMinDays = minTof,
                TotalToFMaxDays = maxTof,
                EnableAlternatingPumping = true,
                AutoTuneToFBounds = true,
                LambertMaxRevs = (maxTof > 400.0) ? 1 : 0
            };

            if (cfg.MinToFDays != null && cfg.MaxToFDays != null && cfg.MinToFDays.Count > 0)
            {
                int count = Math.Min(cfg.MinToFDays.Count, cfg.MaxToFDays.Count);
                for (int i = 0; i < count; i++)
                {
                    c.LegToFBounds.Add((cfg.MinToFDays[i], cfg.MaxToFDays[i]));
                }
            }
            else if (c.FlybySequence != null && c.FlybySequence.Count > 0)
            {
                c.LegToFBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(c.DeparturePlanet, c.ArrivalPlanet, c.FlybySequence, c.CentralBody);
            }

            return c;
        }
    }

    #endregion
    #region Master Solver Facade

    /// <summary>
    /// MGA 自适应求解主门面类 (协调 Stage 1 ~ Stage 4)
    /// </summary>
    public static class MGAAdaptiveSolver
    {
        public static List<MGASolution> Solve(
            MGAAdaptiveSolverConfig config,
            CancellationToken ct,
            Action<float, string> onProgress = null)
        {
            if (config == null) return new List<MGASolution>();

            if (config.AutoTuneToFBounds)
            {
                var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds(config.DeparturePlanet, config.ArrivalPlanet, config.FlybySequence);
                config.TotalToFMinDays = dynMin;
                config.TotalToFMaxDays = dynMax;
            }

            onProgress?.Invoke(0.05f, "Stage 1: 启动 MCTS 离散借力链与动力学拓扑分析...");
            var sequences = MGAMCTSTopologySelector.GenerateOptimalFlybySequences(config);

            var candidateChains = new List<List<string>>();
            if (sequences != null && sequences.Count > 0)
            {
                // 选取前 8 条经能量赤字审计与天体引力井分析评估的高价值多样性拓扑进行多拓扑全局演化
                int maxChainsToEval = Math.Min(8, sequences.Count);
                for (int c = 0; c < maxChainsToEval; c++)
                {
                    var chain = sequences[c];
                    var newFlybys = chain.Where(b => !b.Equals(config.ArrivalPlanet, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (!candidateChains.Any(cand => cand.SequenceEqual(newFlybys)))
                    {
                        candidateChains.Add(newFlybys);
                    }
                }
            }
            else
            {
                candidateChains.Add(config.FlybySequence ?? new List<string>());
            }

            var allRawSolutions = new List<MGASolution>();
            int totalTopos = candidateChains.Count;

            for (int tIdx = 0; tIdx < totalTopos; tIdx++)
            {
                if (ct.IsCancellationRequested) break;

                var topoFlybys = candidateChains[tIdx];
                var subCfg = config.Clone();
                subCfg.FlybySequence = topoFlybys;

                if (subCfg.AutoTuneToFBounds)
                {
                    var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds(subCfg.DeparturePlanet, subCfg.ArrivalPlanet, subCfg.FlybySequence);
                    subCfg.TotalToFMinDays = dynMin;
                    subCfg.TotalToFMaxDays = dynMax;
                    subCfg.LegToFBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(subCfg.DeparturePlanet, subCfg.ArrivalPlanet, subCfg.FlybySequence, subCfg.CentralBody);
                }

                string firstTarget = (subCfg.FlybySequence != null && subCfg.FlybySequence.Count > 0)
                    ? subCfg.FlybySequence[0]
                    : subCfg.ArrivalPlanet;

                double synodicDays = PykepBridge.GetSynodicPeriodDays(subCfg.DeparturePlanet, firstTarget);
                if (synodicDays <= 0 || double.IsNaN(synodicDays)) synodicDays = 365.25;

                float stageP0 = 0.10f + 0.70f * ((float)tIdx / totalTopos);
                float stageP1 = 0.10f + 0.70f * ((float)(tIdx + 1) / totalTopos);
                string topoSummary = $"{subCfg.DeparturePlanet} ➔ {string.Join(" ➔ ", subCfg.FlybySequence)} ➔ {subCfg.ArrivalPlanet}";

                onProgress?.Invoke(stageP0, $"Stage 2/3 [{tIdx + 1}/{totalTopos}]: 拓扑 [{topoSummary}] 会合时区初筛与多岛微分进化...");

                var windowCandidates = MGASurrogatePreFilter.FilterPromisingWindows(subCfg, synodicDays);
                var rawSols = MGAArchipelagoOptimizer.EvolveWindows(
                    subCfg, 
                    windowCandidates, 
                    ct, 
                    (p, msg) => onProgress?.Invoke(stageP0 + (stageP1 - stageP0) * p, msg));

                if (rawSols != null && rawSols.Count > 0)
                {
                    foreach (var s in rawSols)
                    {
                        if (s != null && !double.IsNaN(s.TotalDeltaVKms))
                        {
                            allRawSolutions.Add(s);
                        }
                    }
                }
            }

            if (allRawSolutions.Count == 0)
            {
                return allRawSolutions;
            }

            var rawSolutions = allRawSolutions.OrderBy(s => s.TotalDeltaVKms).ToList();

            onProgress?.Invoke(0.85f, "Stage 4: 物理基线校验与智能数值自愈门禁...");

            // 纯解析开普勒 Vis-Viva 霍曼物理基准用于健康度门禁 (0 源码硬编码，对任意恒星系通用)
            var (depHohmann, totHohmann) = MGAMCTSTopologySelector.CalculateUniversalHohmannBenchmark(
                config.DeparturePlanet, 
                config.ArrivalPlanet, 
                config.DepPeKm, 
                config.ArrPeKm);

            var auditedSolutions = MGASelfHealingEngine.AuditAndHeal(
                config, 
                rawSolutions, 
                depHohmann, 
                totHohmann, 
                ct);

            // 方案去重与候选池精选 (Top 10)
            var finalSolutions = new List<MGASolution>();
            var seenKeys = new HashSet<string>();

            double finalBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
            double finalDsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : finalBudgetMs;

            foreach (var sol in auditedSolutions)
            {
                if (finalBudgetMs < 1e8 && sol.TotalDeltaVKms * 1000.0 > finalBudgetMs * 1.05) continue;
                if (finalDsmBudgetMs < 1e8 && (sol.TotalDsmDeltaVKms * 1000.0 > finalDsmBudgetMs * 1.05 || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > finalDsmBudgetMs * 1.05)))) continue;
                // 1. 挂载 B 平面协方差分析与导航鲁棒性
                if (config.EnableBPlaneCovarianceAnalysis && sol.Legs != null && sol.Legs.Count > 1 && sol.BPlane == null)
                {
                    for (int k = 0; k < sol.Legs.Count - 1; k++)
                    {
                        var leg = sol.Legs[k];
                        var fbP = SolarSystemCatalog.GetPlanet(leg.ToBody);
                        if (fbP != null && fbP.Mu > 0)
                        {
                            double dt = leg.ToFDays * 86400.0;
                            var bpCov = PykepBridge.CalculateBPlaneCovariance(
                                leg.DeparturePosKm, leg.DepartureVelKms, dt, SolarSystemCatalog.SunMu,
                                config.InitialPosSigmaKm, config.InitialVelSigmaMs / 1000.0,
                                leg.ArrivalPosKm, leg.ArrivalVelKms,
                                fbP.Mu, fbP.RadiusKm, config.FlybySafeAtmosphereMarginKm);
                            if (bpCov != null)
                            {
                                sol.BPlane = bpCov;
                            }
                        }
                    }
                }

                // 2. 评估多体引力流形低能捕获 (WSB Invariant Manifolds) - 遵循用户显式开启确认开关 (默认关闭待定)
                if (config.EnableLowEnergyManifoldCapture && !config.NoInsertionBurn && sol.Legs != null && sol.Legs.Count > 0 && sol.ManifoldCapture == null)
                {
                    var lastLeg = sol.Legs[sol.Legs.Count - 1];
                    var pArr = SolarSystemCatalog.GetPlanet(config.ArrivalPlanet);
                    if (pArr != null && pArr.Mu > 0)
                    {
                        double cMu = SolarSystemCatalog.GetCentralMu(config.CentralBody ?? "Sun");
                        if (cMu <= 0) cMu = SolarSystemCatalog.SunMu;
                        var vPlanet = lastLeg.ArrivalVelKms - lastLeg.VInfArrivalKms;
                        var manifold = PykepBridge.CalculateManifoldCapture(
                            lastLeg.ArrivalPosKm, lastLeg.ArrivalVelKms,
                            lastLeg.ArrivalPosKm, vPlanet,
                            pArr.Mu, cMu, pArr.RadiusKm,
                            config.ArrPeKm, config.ArrApKm);
                        if (manifold != null)
                        {
                            sol.ManifoldCapture = manifold;
                            if (manifold.IsManifoldCaptureFeasible)
                            {
                                double dvDiff = sol.InsertionDeltaVKms - manifold.WsbCaptureDeltaVKms;
                                if (dvDiff > 0.001)
                                {
                                    sol.InsertionDeltaVKms = manifold.WsbCaptureDeltaVKms;
                                    sol.TotalDeltaVKms = Math.Max(0.0, sol.TotalDeltaVKms - dvDiff);
                                }
                            }
                        }
                    }
                }

                string chainKey = sol.FlybyChainSummary ?? "";
                int utBucket = (int)(sol.DepartureUT / 43200.0);
                int tofBucket = (int)(sol.TotalFlightDays / 1.0);
                string compositeKey = $"{chainKey}@{utBucket}_{tofBucket}";

                if (!seenKeys.Contains(compositeKey))
                {
                    seenKeys.Add(compositeKey);
                    finalSolutions.Add(sol);
                }
                if (finalSolutions.Count >= 12) break;
            }

            if (finalSolutions.Count == 0 && auditedSolutions.Count > 0)
            {
                var bestAudited = auditedSolutions.OrderBy(s => s.TotalDeltaVKms).First();
                finalSolutions.Add(bestAudited);
            }

            onProgress?.Invoke(1.0f, $"自适应求解完成！输出 {finalSolutions.Count} 个 NASA 级精选方案");
            return finalSolutions;
        }

        public static Task<List<MGASolution>> SolveAsync(
            MGAAdaptiveSolverConfig config,
            CancellationToken ct,
            Action<float, string> onProgress = null)
        {
            return Task.Run(() => Solve(config, ct, onProgress), ct);
        }

        /// <summary>
        /// 零人工干预全自动求解引擎：
        /// 仅需输入出发天体、到达天体与当前日期历元，自动推导最优拓扑超参数四元组 (Swing-Bys, Resonances, Back-Legs, Spacing)，
        /// 并发求解所有高价值借力拓扑与直达窗口，自动输出全局最优解
        /// </summary>
        public static MGAAutonomousResult SolveAutonomous(
            string departurePlanet,
            string arrivalPlanet,
            double departureEpochUT,
            double? searchSpanDays = null,
            int maxCandidates = 16,
            CancellationToken ct = default,
            Action<float, string> onProgress = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new MGAAutonomousResult();

            onProgress?.Invoke(0.05f, "天体力学特征分析与拓扑控制超参数自动推导中...");
            var tuned = MGAMCTSTopologySelector.AutoTuneTopologyHyperparameters(departurePlanet, arrivalPlanet);
            result.AutoTunedParams = tuned;

            // 1. 会合周期与时域划分
            double synodicDays = PykepBridge.GetSynodicPeriodDays(departurePlanet, arrivalPlanet);
            if (synodicDays <= 0 || double.IsNaN(synodicDays)) synodicDays = 365.25;

            double spanDays = searchSpanDays.GetValueOrDefault(Math.Max(365.25, synodicDays * 1.15));
            double startUT = departureEpochUT;
            double endUT = startUT + spanDays * MGADefaults.SecondsPerDay;

            // 2. 估计合理的飞行时间边界 (按天体半长轴自动推导霍曼飞行时间)
            var pDep = SolarSystemCatalog.GetPlanet(departurePlanet);
            var pArr = SolarSystemCatalog.GetPlanet(arrivalPlanet);
            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : 1.496e8;
            double rArr = (pArr != null && pArr.SMA > 0) ? pArr.SMA : 1.496e8;
            double aTransfer = (rDep + rArr) * 0.5;
            double muCentralM = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11) * 1e9;
            double tHohmannSec = Math.PI * Math.Sqrt(Math.Pow(aTransfer * 1000.0, 3.0) / muCentralM);
            double tHohmannDays = tHohmannSec / 86400.0;
            if (tHohmannDays <= 0 || double.IsNaN(tHohmannDays)) tHohmannDays = 260.0;

            double minTofDays = Math.Max(50.0, tHohmannDays * 0.60);
            double maxTofDays = Math.Max(minTofDays + 100.0, tHohmannDays * 2.20);

            // 3. 构造基础配置
            var baseConfig = new MGAAdaptiveSolverConfig
            {
                DeparturePlanet = departurePlanet,
                ArrivalPlanet = arrivalPlanet,
                SearchStartUT = startUT,
                SearchEndUT = endUT,
                DeparturePolicy = DepartureModePolicy.FreeLaunch,
                TotalToFMinDays = minTofDays,
                TotalToFMaxDays = maxTofDays,
                PopulationSize = 60,
                MaxGenerations = 100,
                EnableSelfHealing = true
            };

            // 4. 生成高价值超大广度拓扑序列
            var candidateChains = MGAMCTSTopologySelector.GenerateOptimalFlybySequences(baseConfig, maxCandidates);
            result.CandidateChains = candidateChains;

            onProgress?.Invoke(0.15f, $"已自动识别 {candidateChains.Count} 组高价值拓扑链路，启动并发多岛寻优...");

            // 5. 并发求解所有拓扑链路
            var allSols = new ConcurrentBag<MGASolution>();
            int finished = 0;
            var lockObj = new object();

            Parallel.For(0, candidateChains.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, cIdx =>
            {
                if (ct.IsCancellationRequested) return;

                var chain = candidateChains[cIdx];
                var subCfg = baseConfig.Clone();
                subCfg.FlybySequence = new List<string>(chain);

                // 根据借力链条增加合理航时裕度
                if (chain.Count > 1)
                {
                    subCfg.TotalToFMinDays = minTofDays * 0.9;
                    bool isDeepWellArrival = (rArr < rDep * 0.5);
                    if (isDeepWellArrival)
                    {
                        // 深势阱多重共振必须留足充裕周期 (根据借力段数自适应展开)
                        subCfg.TotalToFMaxDays = Math.Max(800.0, maxTofDays * (1.0 + 1.2 * (chain.Count - 1)));
                    }
                    else
                    {
                        subCfg.TotalToFMaxDays = maxTofDays * (1.0 + 0.35 * (chain.Count - 1));
                    }
                }

                var sols = Solve(subCfg, ct);
                if (sols != null && sols.Count > 0)
                {
                    foreach (var s in sols)
                    {
                        if (s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < 250.0)
                        {
                            allSols.Add(s);
                        }
                    }
                }

                lock (lockObj)
                {
                    finished++;
                    float p = 0.15f + 0.80f * ((float)finished / candidateChains.Count);
                    double curBest = allSols.Any() ? allSols.Min(s => s.TotalDeltaVKms * 1000.0) : 0;
                    string bestStr = curBest > 0 ? $"当前最优: {curBest:F0} m/s" : "探索中";
                    onProgress?.Invoke(p, $"多链路并发求解: {finished}/{candidateChains.Count} 拓扑完成 ({bestStr})");
                }
            });

            // 6. 聚合与帕累托双轨排序 (最省 vs 最快)
            var rankedFuel = allSols.OrderBy(s => s.TotalDeltaVKms).ToList();
            var rankedTime = allSols.OrderBy(s => s.TotalFlightDays).ToList();

            result.AllRankedSolutions = rankedFuel;
            result.BestSolution = rankedFuel.FirstOrDefault();
            result.BestFuelSolution = rankedFuel.FirstOrDefault();
            result.BestTimeSolution = rankedTime.FirstOrDefault();

            // 帕累托非支配解集 (Pareto-optimal Front: 兼顾最小 Delta-V 与最小飞行历时)
            var pareto = new List<MGASolution>();
            double minTofSoFar = double.MaxValue;
            foreach (var s in rankedFuel)
            {
                if (s.TotalFlightDays < minTofSoFar)
                {
                    pareto.Add(s);
                    minTofSoFar = s.TotalFlightDays;
                }
            }
            result.ParetoFrontSolutions = pareto;

            sw.Stop();
            result.TotalElapsedMs = sw.ElapsedMilliseconds;

            // 7. 自动生成决策简报 (包含最省与最快双轨信息)
            if (result.BestSolution != null)
            {
                var bf = result.BestFuelSolution;
                var bt = result.BestTimeSolution;
                result.SummaryReport = $"[全自适应超大广度求解成功] 耗时: {result.TotalElapsedMs} ms (并发拓扑数: {candidateChains.Count})\n" +
                                       $"★ 自动推导演变参数: {tuned}\n" +
                                       $"★ 最省燃料方案 (Fuel-Optimal): {(string.IsNullOrEmpty(bf.FlybyChainSummary) ? arrivalPlanet : bf.FlybyChainSummary)} | ΔV={bf.TotalDeltaVKms * 1000:F1} m/s | 历时={bf.TotalFlightDays:F1} 天\n" +
                                       $"★ 最快到达方案 (Time-Optimal): {(string.IsNullOrEmpty(bt.FlybyChainSummary) ? arrivalPlanet : bt.FlybyChainSummary)} | ΔV={bt.TotalDeltaVKms * 1000:F1} m/s | 历时={bt.TotalFlightDays:F1} 天\n" +
                                       $"★ 帕累托非支配解集数: {result.ParetoFrontSolutions.Count} 组";
            }
            else
            {
                result.SummaryReport = $"[求解警报] 未能收敛出可行方案，耗时: {result.TotalElapsedMs} ms";
            }

            onProgress?.Invoke(1.0f, "全自适应求解完成！");
            return result;
        }

        /// <summary>
        /// 将自主求解得到的最优解直接应用到 MGAConfig 配置与任务执行流中，并导出各航段详细物理参数
        /// </summary>
        public static bool ApplySolutionToConfig(MGASolution solution, MGAConfig config)
        {
            if (solution == null || config == null || solution.Legs == null || solution.Legs.Count == 0)
                return false;

            config.EarliestDepartureUT = solution.DepartureUT;
            config.LatestDepartureUT = solution.DepartureUT + 86400.0;
            config.FlybySequence = solution.Legs.Select(l => l.ToBody).ToList();
            if (config.FlybySequence.Count > 0)
            {
                config.DestinationBody = config.FlybySequence[config.FlybySequence.Count - 1];
            }
            config.MinToFDays = solution.Legs.Select(l => Math.Max(5.0, l.ToFDays * 0.9)).ToList();
            config.MaxToFDays = solution.Legs.Select(l => l.ToFDays * 1.1).ToList();
            return true;
        }

        public static Task<MGAAutonomousResult> SolveAutonomousAsync(
            string departurePlanet,
            string arrivalPlanet,
            double departureEpochUT,
            double? searchSpanDays = null,
            int maxCandidates = 16,
            CancellationToken ct = default,
            Action<float, string> onProgress = null)
        {
            return Task.Run(() => SolveAutonomous(departurePlanet, arrivalPlanet, departureEpochUT, searchSpanDays, maxCandidates, ct, onProgress), ct);
        }

        // ===========================================================================
        // MGAConfig 门面入口 (从 PykepBridge 迁出，收敛为求解器层统一对外 API)
        // 编排逻辑归本层；底层动力学与解构映射仍委托 PykepBridge (baseline §0.2/§0.4)
        // ===========================================================================

        /// <summary>
        /// 全局多段差分进化寻优器 (基于 PyKEP 原生全流程评估，自动按天体公转/会合周期分窗并发寻优，杜绝单月早熟坍缩)
        /// </summary>
        public static List<MGASolution> Optimize(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default, bool allowReExpansion = true) {
            var solutions = new List<MGASolution>();
            if (config == null || config.FlybySequence == null || config.FlybySequence.Count == 0) return solutions;

            double startUT = config.EarliestDepartureUT;
            double endUT = (config.LatestDepartureUT > config.EarliestDepartureUT + 1.0)
                ? config.LatestDepartureUT
                : (config.EarliestDepartureUT + MGADefaults.SecondsPerDay * (config.DepartureSearchDays > 0 ? config.DepartureSearchDays : MGADefaults.DefaultSearchWindowDays));

            double totalSpanDays = (endUT - startUT) / MGADefaults.SecondsPerDay;
            string firstTarget = config.FlybySequence[0];
            double synodicDays = PykepBridge.GetSynodicPeriodDays(config.DepartureBody, firstTarget);
            if (synodicDays <= 0 || double.IsNaN(synodicDays)) {
                var pTarget = SolarSystemCatalog.GetPlanet(firstTarget);
                string central = config?.CentralBody ?? "Sun";
                double cMu = SolarSystemCatalog.GetCentralMu(central);
                double smaTarget = (pTarget != null && pTarget.SMA > 0) ? pTarget.SMA : 0.0;
                if (smaTarget <= 0.0 && pTarget != null) {
                    pTarget.GetRelativeState(central, startUT, out Vector3d rT, out _);
                    smaTarget = rT.magnitude;
                }
                if (smaTarget <= 0.0) {
                    double rC = SolarSystemCatalog.GetPlanet(central)?.RadiusKm ?? MGADefaults.EarthRadiusKm;
                    smaTarget = rC * 50.0;
                }
                double tTarget = PykepBridge.CalcOrbitPeriodSec(smaTarget, cMu) / MGADefaults.SecondsPerDay;
                synodicDays = (tTarget > 0 && !double.IsNaN(tTarget)) ? tTarget : 30.0;
            }

            // 若发射时限跨越 1.25 个以上会合周期 (如地月 90 天，或跨年行星际大窗口)，自动启用自适应多岛演化与数值自愈求解器 (彻底消除 45 天与 6 片截断硬编码)
            if (totalSpanDays > synodicDays * 1.25 && totalSpanDays > 30.0) {
                var adaptiveCfg = MGAAdaptiveSolverConfig.FromMGAConfig(config);
                adaptiveCfg.EnablePermutationsAndInsertions = allowReExpansion;
                return Solve(adaptiveCfg, ct, onProgress);
            }

            // 单时段标准优化
            return MGADifferentialEvolution.RunWindow(config, startUT, endUT, onProgress, ct);
        }

        public static Task<List<MGASolution>> OptimizeAsync(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default) {
            return Task.Run(() => Optimize(config, onProgress, ct), ct);
        }

        /// <summary>
        /// 全自动多序列智能批量寻优引擎：
        /// 根据配置参数自动 BFS 派生所有合法借力链路，多核并发寻优，自适应判别并激活 MGA-1DSM，闭环 8 大特性，最优解排在最前面
        /// </summary>
        public static async Task<List<MGASolution>> AutoOptimizeCandidateSequencesAsync(
            MGAConfig baseConfig,
            Action<float, string> onProgress = null,
            CancellationToken ct = default)
        {
            if (baseConfig == null || string.IsNullOrEmpty(baseConfig.DepartureBody) || string.IsNullOrEmpty(baseConfig.DestinationBody))
                return new List<MGASolution>();

            // 1. 自动广度优先生成候选借力链路队列 (统一真值源: MGAMCTSTopologySelector)
            var chains = MGAMCTSTopologySelector.GenerateCandidateSequences(baseConfig);
            if (chains == null || chains.Count == 0) {
                chains = new List<List<string>> { new List<string> { baseConfig.DestinationBody } };
            }

            int maxChains = Math.Min(16, chains.Count);
            var aggregatedSolutions = new System.Collections.Concurrent.ConcurrentBag<MGASolution>();

            onProgress?.Invoke(0.05f, $"启动全自动多序列智能寻优 (共 {maxChains} 条候选序列)...");

            int completedCount = 0;
            var lockObj = new object();
            var tasks = new List<Task>();

            for (int cIdx = 0; cIdx < maxChains; cIdx++) {
                var chain = chains[cIdx];
                var seqConfig = baseConfig.Clone();
                seqConfig.FlybySequence = new List<string>(chain);
                seqConfig.SyncStrategiesWithChain(chain.Count);

                // 关键物理修正 1: 清空单腿继承的硬编码 ToF，各航段独立按照真实天体霍曼与共振周期自适应确定物理搜索边界
                seqConfig.MinToFDays = null;
                seqConfig.MaxToFDays = null;

                // 关键物理修正 2: 确保计算减速入轨机动需求，寻优目标严格包含捕获入轨 ΔV
                seqConfig.NoInsertionBurn = false;

                // 智能分析借力链路动力学：检查是否存在同星共振或大偏角，自适应激活 1-DSM
                bool needsDSM = false;
                string prevBody = seqConfig.DepartureBody;
                for (int leg = 0; leg < chain.Count; leg++) {
                    string nextBody = chain[leg];
                    if (nextBody.Equals(prevBody, StringComparison.OrdinalIgnoreCase)) {
                        needsDSM = true;
                        if (seqConfig.LegStrategies != null && leg < seqConfig.LegStrategies.Count) {
                            seqConfig.LegStrategies[leg] = MGALegStrategy.DeepSpaceManeuver1DSM;
                        }
                    }
                    prevBody = nextBody;
                }
                if (needsDSM) {
                    seqConfig.EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM;
                }

                tasks.Add(Task.Run(() => {
                    try {
                        // 显式配置 allowReExpansion = false，杜绝已展开的单链进入求解器后被二次排列插序
                        var res = Optimize(seqConfig, null, ct, allowReExpansion: false);
                        if (res != null && res.Count > 0) {
                            foreach (var s in res) aggregatedSolutions.Add(s);
                        }
                    } catch (Exception ex) {
                        KSPGMATLog.Warn($"[PykepBridge] Multi-chain sequence note: {ex.Message}");
                    } finally {
                        lock (lockObj) {
                            completedCount++;
                            float p = 0.05f + 0.90f * ((float)completedCount / maxChains);
                            onProgress?.Invoke(p, $"多序列智能寻优: {completedCount}/{maxChains} 序列完成...");
                        }
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);

            // 结果汇总并按总 ΔV 严格升序排序 (最优解排在最前面)
            var sortedList = aggregatedSolutions
                .Where(s => s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < 1e7)
                .OrderBy(s => s.TotalDeltaVKms)
                .ToList();

            var finalSolutions = new List<MGASolution>();
            var seenKeys = new HashSet<string>();
            foreach (var sol in sortedList) {
                string chainKey = sol.FlybyChainSummary ?? "";
                int utBucket = (int)(sol.DepartureUT / 43200.0);
                int tofBucket = (int)(sol.TotalFlightDays / 1.0);
                string compositeKey = $"{chainKey}@{utBucket}_{tofBucket}";
                if (!seenKeys.Contains(compositeKey)) {
                    seenKeys.Add(compositeKey);
                    finalSolutions.Add(sol);
                }
                if (finalSolutions.Count >= 12) break;
            }

            if (finalSolutions.Count < 5) {
                foreach (var sol in sortedList) {
                    bool tooClose = finalSolutions.Any(s =>
                        (s.FlybyChainSummary ?? "") == (sol.FlybyChainSummary ?? "") &&
                        Math.Abs(s.DepartureUT - sol.DepartureUT) < 21600.0 &&
                        Math.Abs(s.TotalFlightDays - sol.TotalFlightDays) < 0.5
                    );
                    if (!tooClose) {
                        finalSolutions.Add(sol);
                    }
                    if (finalSolutions.Count >= 12) break;
                }
            }

            onProgress?.Invoke(1.0f, $"多序列智能寻优完成! 共收敛 {finalSolutions.Count} 个精选方案");
            return finalSolutions;
        }

        /// <summary>
        /// 基于行星轨道内外侧拓扑与物理约束自动广度优先生成候选借力序列 (已统一委托至 MGAMCTSTopologySelector 单一真值源)
        /// </summary>
        public static List<List<string>> GenerateCandidateSequences(
            string depBody, string destBody, int maxSwingBys = 6, int maxResonances = 0, int maxBackLegs = 2, int maxBackSpacing = 2)
        {
            return MGAMCTSTopologySelector.GenerateCandidateSequences(depBody, destBody, maxSwingBys, maxResonances, maxBackLegs, maxBackSpacing);
        }

        public static List<List<string>> GenerateCandidateSequences(MGAConfig config) {
            return MGAMCTSTopologySelector.GenerateCandidateSequences(config);
        }
    }

    /// <summary>
    /// 全自动一键求解输出结果包装 (支持帕累托最省/最快双轨决策)
    /// </summary>
    public sealed class MGAAutonomousResult
    {
        public TopologyHyperparameters AutoTunedParams { get; set; }
        public List<List<string>> CandidateChains { get; set; } = new List<List<string>>();
        public MGASolution BestSolution { get; set; }
        public MGASolution BestFuelSolution { get; set; }
        public MGASolution BestTimeSolution { get; set; }
        public List<MGASolution> ParetoFrontSolutions { get; set; } = new List<MGASolution>();
        public List<MGASolution> AllRankedSolutions { get; set; } = new List<MGASolution>();
        public long TotalElapsedMs { get; set; }
        public string SummaryReport { get; set; }
    }

    #endregion
}
