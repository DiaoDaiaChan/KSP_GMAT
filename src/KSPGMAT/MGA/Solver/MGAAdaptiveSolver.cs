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
using KSPGMAT.MGA.Solver;
using KSPGMAT.MGA.Telemetry;

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
        // =====================================================================
        // 【2026-09-14 审计标注 · 本配置块中经验参数的统一说明】
        // 下属全部数值默认值 (停泊高度 200 km、出射门禁 3800 m/s、搜索窗 3 年、
        // DE 种群 80 / 代数 150 / F 0.7 / CR 0.85、停滞限 15、群岛 4 岛 10 代迁移、
        // MCTS ε 0.02 / 网格 22.5° / 5 万次 rollout、Hohmann 能量上限 1.35、
        // 自愈窗口 30 天 / 网格 0.5 天、B 平面 3σ 初值 10 km / 0.05 m/s 等)
        // 均为**以太阳系内行星际任务标定的经验默认值**, 尚未完成有监督标定。
        //
        // 已集中登记于 MGAEmpiricalRegistry (见 "Model/MGAStateManager.cs (MGADefaults)" 条目
        // 及各项对应条目), 请勿在此处继续新增未登记的裸数字。
        // 物理常数 (AU、秒/天、角度换算) 与数值保护 (除零钳位) 不属经验参数。
        // =====================================================================
        // 1. 轨道动力学与几何
        public string CentralBody { get; set; } = SolarSystemCatalog.RootStarName;
        public string DeparturePlanet { get; set; } = SolarSystemCatalog.DefaultDepartureBody;
        public string ArrivalPlanet { get; set; } = SolarSystemCatalog.DefaultArrivalBody;
        public List<string> FlybySequence { get; set; } = new List<string>();
        public DepartureModePolicy DeparturePolicy { get; set; } = DepartureModePolicy.FreeLaunch;
        public double DepPeKm { get; set; } = 200.0;
        public double DepApKm { get; set; } = 200.0;
        public double DepIncDeg { get; set; } = -1.0; // 默认 -1 触发纯切向
        public double ArrPeKm { get; set; } = 200.0;
        public double ArrApKm { get; set; } = 200.0;
        public MGAArrivalOrbitType ArrivalOrbitType { get; set; } = MGAArrivalOrbitType.HighlyElliptic; // 预留 GUI 终端轨道模式 (高椭圆/圆轨/飞掠)
        public bool AutoAdaptGiantPlanetEllipse { get; set; } = true; // 预留 GUI 自动按巨行星科学标准椭圆推算远拱点
        public MGAPropulsionType PropulsionType { get; set; } = MGAPropulsionType.ChemicalImpulsive; // 预留 GUI 推进系统模式 (化学脉冲 vs 低推力电推)
        public bool NoInsertionBurn { get; set; } = false; // 高速飞掠模式 (Flyby Only / 终点刹车为0)
        public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive; // 纯弹道 vs 1-DSM深空机动
        public List<MGALegStrategy> LegStrategies { get; set; } = null;
        public List<MGAFlybyStrategy> FlybyStrategies { get; set; } = null;
        public List<double> DsmEtas { get; set; } = null;
        public double DsmEtaDefault { get; set; } = 0.5;
        public bool ConstrainByVesselDeltaV { get; set; } = false;
        public double VesselMaxDeltaVMs { get; set; } = 0.0;
        public double MaxDsmDeltaVMs { get; set; } = 0.0; // 权威 DSM 机动上限约束 (m/s)，不可超过总任务 ΔV 或用户指定预算
        public double LaunchDeltaVCeilingMs { get; set; } = 3800.0; // 权威始发 TVI 出射速度增量门禁 (m/s)
        public double VinfFreeKmS { get; set; } = 0.0;
        public MGAConstraintProfile ConstraintProfile { get; set; }
        public bool AddVinfDep { get; set; } = true;
        public bool AddVinfArr { get; set; } = true;
        public bool EnablePermutationsAndInsertions { get; set; } = true; // 自动乱序排列与中继天体插序尝试
        public bool EnableAlternatingPumping { get; set; } = true;         // 自动衍生交替回折提升PE能量拓扑 (如 E-M-E-M / E-V-E)
        public bool AutoTuneToFBounds { get; set; } = true;                // ToF 上下限全量纳入优化求解器范围 (根据天体公转与共振自适应弹性延展)
        public int MaxResonances { get; set; } = int.MaxValue;             // 单星多次共振阶数上限 (用户明确要求：不设人为上限，动态自适应求解)
        public bool EnableEphemerisReachabilityFilter { get; set; } = true; // 启用基于当前星历与天体引力势阱的物理可达性前置剪枝
        public int MaxCandidateTopologies { get; set; } = 8; // MCTS 派生与可达性初筛评估的拓扑候选上限
        public int MaxWindows { get; set; } = 0; // 单拓扑会合窗口上限 (0=不限制; 用于快速验证/预算控制, 截断多窗口)
        public double DepartureToFMinFactor { get; set; } = 0.65;
        public double DepartureToFMaxFactor { get; set; } = 1.50;
        public double ResonantMaxOffsetDays { get; set; } = 0.0;

        // 2. 搜索历元与时间编码
        public double SearchStartUT { get; set; }
        public double SearchEndUT { get; set; }
        public ToFEncodingMode ToFEncoding { get; set; } = ToFEncodingMode.Alpha;
        public double TotalToFMinDays { get; set; } = -1.0;
        public double TotalToFMaxDays { get; set; } = -1.0;
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

        /// <summary>
        /// 深拷贝当前求解配置对象，以便在分阶段优化中独立修改超参数
        /// </summary>
        /// <returns>配置副本</returns>
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
            c.LaunchDeltaVCeilingMs = this.LaunchDeltaVCeilingMs;
            c.PopulationSize = this.PopulationSize;
            c.MaxGenerations = this.MaxGenerations;
            c.EnablePermutationsAndInsertions = this.EnablePermutationsAndInsertions;
            c.EnableAlternatingPumping = this.EnableAlternatingPumping;
            c.AutoTuneToFBounds = this.AutoTuneToFBounds;
            c.MaxResonances = this.MaxResonances;
            c.EnableEphemerisReachabilityFilter = this.EnableEphemerisReachabilityFilter;
            c.MaxCandidateTopologies = this.MaxCandidateTopologies;
            c.DepartureToFMinFactor = this.DepartureToFMinFactor;
            c.DepartureToFMaxFactor = this.DepartureToFMaxFactor;
            c.ResonantMaxOffsetDays = this.ResonantMaxOffsetDays;
            c.LegToFBounds = (this.LegToFBounds != null) ? new List<(double, double)>(this.LegToFBounds) : null;   // null 防护: 避免对 null 源抛 ArgumentNullException
            c.EnableLowEnergyManifoldCapture = this.EnableLowEnergyManifoldCapture;
            c.EnableBPlaneCovarianceAnalysis = this.EnableBPlaneCovarianceAnalysis;
            c.InitialPosSigmaKm = this.InitialPosSigmaKm;
            c.InitialVelSigmaMs = this.InitialVelSigmaMs;
            c.FlybySafeAtmosphereMarginKm = this.FlybySafeAtmosphereMarginKm;
            c.EnforceSafeFlybyMargin = this.EnforceSafeFlybyMargin;
            return c;
        }

        /// <summary>
        /// 从全局通用的 MGAConfig 结构构建高内聚的 MGAAdaptiveSolverConfig 实例
        /// </summary>
        /// <param name="cfg">全局 MGA 配置对象</param>
        /// <returns>自适应求解器专用配置对象</returns>
        public static MGAAdaptiveSolverConfig FromMGAConfig(MGAConfig cfg)
        {
            double maxTof = (cfg.MaxToFDays != null && cfg.MaxToFDays.Count > 0) ? cfg.MaxToFDays.Sum() : -1.0;
            double minTof = (cfg.MinToFDays != null && cfg.MinToFDays.Count > 0) ? cfg.MinToFDays.Sum() : -1.0;

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
                ArrivalOrbitType = cfg.ArrivalOrbitType,
                AutoAdaptGiantPlanetEllipse = cfg.AutoAdaptGiantPlanetEllipse,
                PropulsionType = cfg.PropulsionType,
                NoInsertionBurn = cfg.NoInsertionBurn || (cfg.ArrivalOrbitType == MGAArrivalOrbitType.Flyby),
                EnableLowEnergyManifoldCapture = cfg.EnableLowEnergyManifoldCapture,
                EngineMode = cfg.EngineMode,
                LegStrategies = cfg.LegStrategies != null ? new List<MGALegStrategy>(cfg.LegStrategies) : null,
                FlybyStrategies = cfg.FlybyStrategies != null ? new List<MGAFlybyStrategy>(cfg.FlybyStrategies) : null,
                DsmEtas = cfg.DsmEtas != null ? new List<double>(cfg.DsmEtas) : null,
                DsmEtaDefault = cfg.DsmEtaDefault,
                ConstrainByVesselDeltaV = cfg.ConstrainByVesselDeltaV,
                VesselMaxDeltaVMs = cfg.VesselMaxDeltaVMs,
                MaxDsmDeltaVMs = cfg.MaxDsmDeltaVMs > 0 ? cfg.MaxDsmDeltaVMs : (cfg.ConstrainByVesselDeltaV ? cfg.VesselMaxDeltaVMs : 0.0),
                LaunchDeltaVCeilingMs = cfg.LaunchDeltaVCeilingMs,
                PopulationSize = cfg.PopulationSize > 0 ? cfg.PopulationSize : MGADefaults.PopulationSize,
                MaxGenerations = cfg.MaxGenerations > 0 ? cfg.MaxGenerations : MGADefaults.MaxGenerations,
                DepartureToFMinFactor = cfg.DepartureToFMinFactor,
                DepartureToFMaxFactor = cfg.DepartureToFMaxFactor,
                ResonantMaxOffsetDays = cfg.ResonantMaxOffsetDays,
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
                c.LegToFBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(c.DeparturePlanet, c.ArrivalPlanet, c.FlybySequence, c.CentralBody, c.DepartureToFMinFactor, c.DepartureToFMaxFactor);
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
        public static Telemetry.MGAPipelineTelemetryProbe.ReportData LastPipelineReport => Telemetry.MGAPipelineTelemetryProbe.ReportData.Instance;

        /// <summary>
        /// 判定是否为"深井爬升"任务: 出发内行星 → 途经显著内深势阱天体(如 Mercury) → 到达远外行星。
        /// 此场景中段跨势阱腿必须依赖多圈共振泵能，而非单段大 DSM 硬推。
        /// </summary>
        private static bool IsDeepWellClimbScenario(MGAAdaptiveSolverConfig config)
        {
            if (config == null) return false;
            var pD = SolarSystemCatalog.GetPlanet(config.DeparturePlanet);
            var pA = SolarSystemCatalog.GetPlanet(config.ArrivalPlanet);
            if (pD == null || pA == null || pD.SMA <= 0 || pA.SMA <= 0) return false;

            // 必须为外爬升(到达星 SMA 显著高于出发星)
            if (pA.SMA <= pD.SMA * 1.3) return false;

            // 且目标借力链中存在显著内深势阱天体(如访问 Mercury/Venus 后外逸)
            bool hasDeepWell = false;
            if (config.FlybySequence != null)
            {
                foreach (var b in config.FlybySequence)
                {
                    var pb = SolarSystemCatalog.GetPlanet(b);
                    if (pb != null && pb.SMA > 0 && pb.SMA < pD.SMA * 0.6) { hasDeepWell = true; break; }
                }
            }
            return hasDeepWell;
        }

        /// <summary>
        /// 判断借力链是否包含相邻同星共振泵能段 (如 Venus→Venus / Mercury→Mercury)。
        /// </summary>
        private static bool HasAdjacentResonanceSegment(List<string> chain)
        {
            if (chain == null || chain.Count < 2) return false;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                if (chain[i].Equals(chain[i + 1], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// MGA 四阶段流水线同步求解核心入口 (Stage 1 拓扑选择 -> Stage 2 窗口筛选 -> Stage 3 DE寻优 -> Stage 4 物理自愈)
        /// </summary>
        /// <param name="config">自适应求解配置</param>
        /// <param name="ct">异步取消令牌</param>
        /// <param name="onProgress">进度回调委派 (进度百分比 0~100, 阶段状态文本)</param>
        /// <returns>收敛并经过自愈校验的最优 MGA 轨迹解集合</returns>
        public static List<MGASolution> Solve(
            MGAAdaptiveSolverConfig config,
            CancellationToken ct,
            Action<float, string> onProgress = null)
        {
            if (config == null) return new List<MGASolution>();

            // 代理模型权重按需加载: 未训练 (或权重文件缺失) 时推理期自动退化为解析开普勒判据,
            // 属安全状态 —— 绝不再使用未经训练的随机权重参与任何判断。
            KSPGMAT.MGA.AI.MGAPhysicsSurrogateNetwork.TryLoadDefaultWeights();

            MGAStepProfiler.Reset();
            Telemetry.MGAPipelineTelemetryProbe.Reset();
            Telemetry.MGAPipelineTelemetryProbe.StartMission(
                config.DeparturePlanet,
                config.ArrivalPlanet,
                config.FlybySequence,
                config.SearchStartUT,
                (config.SearchEndUT - config.SearchStartUT) / 86400.0,
                (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 4000.0,
                config.MaxDsmDeltaVMs > 0 ? config.MaxDsmDeltaVMs : 500.0,
                0.0
            );
            var reportSw = System.Diagnostics.Stopwatch.StartNew();

            // 终端捕获模型物理对齐 (GUI 预留接口统一自适应推导)
            if (config.ArrivalOrbitType == MGAArrivalOrbitType.Flyby || config.NoInsertionBurn)
            {
                config.NoInsertionBurn = true;
            }
            else
            {
                var destP = SolarSystemCatalog.GetPlanet(config.ArrivalPlanet);
                // 【重构】巨行星判别由"绝对 μ/半径阈值"改为**系统内自标定的无量纲判据**:
                // 该天体一次无动力飞掠的日心速度增益, 是否显著高于系统内全部天体的鲁棒上界
                // (median + 2·MAD)。原 `Mu >= 5e6 || RadiusKm >= 20000` 是以 km³/s² 与 km
                // 标定的绝对阈值, 仅对 Sol 系成立, 换 Kerbol / JNSQ / 自建星系即失效。
                double muCForGiant = SolarSystemCatalog.GetCentralMu(config.CentralBody ?? "Sun");
                if (muCForGiant <= 0) muCForGiant = SolarSystemCatalog.SunMu;
                var depPForGiant = SolarSystemCatalog.GetPlanet(config.DeparturePlanet);
                double vInfForGiant = MGAPhysicalScales.HohmannVInfKms(muCForGiant,
                    (depPForGiant != null && depPForGiant.SMA > 0) ? depPForGiant.SMA : MGADefaults.AU_KM,
                    (destP != null && destP.SMA > 0) ? destP.SMA : MGADefaults.AU_KM);
                bool isGiant = MGAPhysicalScales.ClassifyGiantBySystemDistribution(
                    destP, vInfForGiant, muCForGiant, SolarSystemCatalog.GetAvailablePlanets());
                if (config.ArrivalOrbitType == MGAArrivalOrbitType.HighlyElliptic || (config.AutoAdaptGiantPlanetEllipse && isGiant))
                {
                    MGADefaults.GetBodyCaptureAltitudeBounds(config.ArrivalPlanet, out double minPeA, out double recPeA, out double maxApA);
                    if (config.ArrPeKm < minPeA || config.ArrPeKm <= 0)
                    {
                        config.ArrPeKm = recPeA;
                    }
                    if (config.ArrApKm <= config.ArrPeKm)
                    {
                        double destR = destP != null && destP.RadiusKm > 0 ? destP.RadiusKm : MGADefaults.FallbackEarthRadiusKm;
                        double rp = destR + config.ArrPeKm;
                        double soiKm = (destP != null && destP.SoiKm > 0) ? destP.SoiKm : destR * 100.0;
                        double ra = Math.Min(soiKm * 0.20, rp * 100.0);
                        config.ArrApKm = Math.Min(maxApA, Math.Round(ra - destR));
                    }
                }
            }

            double userSpecifiedMaxToF = (config != null && config.TotalToFMaxDays > 0) ? config.TotalToFMaxDays : -1.0;
            double userSpecifiedMinToF = (config != null && config.TotalToFMinDays > 0) ? config.TotalToFMinDays : -1.0;
            if (config.AutoTuneToFBounds)
            {
                var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds(config.DeparturePlanet, config.ArrivalPlanet, config.FlybySequence);

                // 【2026-09-14 第二轮审计修正 —— 调用方指定的航时边界必须是**约束**而非"建议"】
                //
                // 原实现为**无条件赋值**:
                //     config.TotalToFMinDays = dynMin;
                //     config.TotalToFMaxDays = (userSpecifiedMaxToF > 0) ? userSpecifiedMaxToF : dynMax;
                // 即: 上界虽尊重调用方, **下界一律被覆盖**; 而 `AutoTuneToFBounds` 在
                // `MGAAdaptiveSolverConfig.FromMGAConfig` 中被**硬编码为 true** ——
                // 于是所有经 MGAConfig 进入的路径 (GUI 预设、基准套件、盲搜套件) 传入的
                // `MinToFDays` / `MaxToFDays` 派生的总航时下界全部被静默丢弃, 上界仅在调用方
                // 显式给出 TotalToFMaxDays 时才生效。
                // 实测后果: GUI 预设路径在 New Horizons 上产出 **49950 天 (136.8 年)** 的解,
                // 而调用方给出的物理包络只有 6978 天 —— 边界完全没有约束力。
                //
                // 现改为**仅在没有明确指定时补默认值** (fill-in, 不覆盖)。
                if (userSpecifiedMinToF <= 0) config.TotalToFMinDays = dynMin;
                if (userSpecifiedMaxToF <= 0) config.TotalToFMaxDays = dynMax;
            }

            double effectiveMaxToF = (config != null && config.TotalToFMaxDays > 0) ? config.TotalToFMaxDays : -1.0;
            double effectiveMinToF = (config != null && config.TotalToFMinDays > 0) ? config.TotalToFMinDays : -1.0;

            var candidateChains = new List<List<string>>();
            if (!config.EnablePermutationsAndInsertions)
            {
                var newFlybys = (config.FlybySequence ?? new List<string>())
                    .Where(b => !b.Equals(config.ArrivalPlanet, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                candidateChains.Add(newFlybys);
            }
            else
            {
                onProgress?.Invoke(0.05f, "Stage 1: 启动 MCTS 离散借力链与动力学拓扑分析...");
                List<List<string>> sequences;
                using (MGAStepProfiler.Scope("Stage 1: MCTS 拓扑生成与可达性剪枝"))
                {
                    sequences = MGAMCTSTopologySelector.GenerateOptimalFlybySequences(config);
                }
                if (sequences != null && sequences.Count > 0)
                {
                    int targetLimit = config.MaxCandidateTopologies > 0 ? config.MaxCandidateTopologies : 8;
                    int maxChainsToEval = Math.Min(targetLimit, sequences.Count);
                    for (int c = 0; c < maxChainsToEval; c++)
                    {
                        var chain = sequences[c];
                        var newFlybys = new List<string>(chain);
                        if (newFlybys.Count == 1 && newFlybys[0].Equals(config.ArrivalPlanet, StringComparison.OrdinalIgnoreCase))
                        {
                            newFlybys.Clear();
                        }
                        if (!candidateChains.Any(cand => cand.SequenceEqual(newFlybys)))
                        {
                            candidateChains.Add(newFlybys);
                        }
                    }

                    KSPGMATLog.Info("Solver", $"🎯 共筛选出 {candidateChains.Count} 组最优物理借力拓扑进入 DE 演化池:");
                    for (int i = 0; i < candidateChains.Count; i++)
                    {
                        string mid = (candidateChains[i].Count > 0) ? (string.Join(" ➔ ", candidateChains[i]) + " ➔ ") : "";
                        KSPGMATLog.Info("Solver", $"   #{i + 1}: {config.DeparturePlanet} ➔ {mid}{config.ArrivalPlanet}");
                    }
                }
                else
                {
                    candidateChains.Add(config.FlybySequence ?? new List<string>());
                }
            }

            // 候选拓扑评估完成，进入演化计算阶段

            var allRawSolutions = new List<MGASolution>();
            int totalTopos = candidateChains.Count;
            double globalBestEpochUT = 0.0;
            double globalBestDvMs = double.MaxValue;
            // 拓扑层动态工作流去超订 (Thread De-oversubscription & Workflow Stream):
            // 将外层拓扑并发度限制为 2~3，避免外层拓扑与内层多岛差分进化/前向注种/C++ OpenMP 产生多层嵌套超订 (Over-subscription)，
            // 确保每个活跃拓扑能充分享受全部 CPU 核心的高速缓存与 AVX2 算力。
            var aggLock = new object();
            int usableCores = Math.Max(1, Environment.ProcessorCount - 1);
            int maxOuterTopo = (usableCores >= 16) ? 2 : 1;
            int topoParallel = Math.Min(totalTopos, maxOuterTopo);
            int perTopoWindowParallel = Math.Max(1, usableCores / Math.Max(1, topoParallel));

            // 纯解析开普勒 Vis-Viva 霍曼物理基准用于健康度门禁 (0 源码硬编码，对任意恒星系通用)。
            // 提前至并行循环外计算一次: 逐链自愈 (Stage 4 per-topology) 需要在链内使用同一基准,
            // 而它仅依赖 config 的出发/到达天体, 与具体拓扑无关, 无需每条链重算。
            var (depHohmann, totHohmann) = MGAMCTSTopologySelector.CalculateUniversalHohmannBenchmark(
                config.DeparturePlanet,
                config.ArrivalPlanet,
                config.DepPeKm,
                config.ArrPeKm);

            Parallel.For(0, totalTopos, new ParallelOptions { MaxDegreeOfParallelism = topoParallel }, tIdx =>
            {
                if (ct.IsCancellationRequested) return;

                var topoFlybys = candidateChains[tIdx];
                KSPGMATLog.Info("Solver", $">>> 正在演化拓扑 #{tIdx + 1}/{totalTopos}: {config.DeparturePlanet} ➔ {string.Join(" ➔ ", topoFlybys)} ➔ {config.ArrivalPlanet} <<<");
                var subCfg = config.Clone();
                subCfg.FlybySequence = topoFlybys;
                subCfg.EnablePermutationsAndInsertions = false;
                subCfg.LegStrategies = null;
                subCfg.LegToFBounds = null;
                subCfg.AutoTuneToFBounds = true;

                // 天体力学第一性原理：各拓扑的首段借力目标天体不同，星际会合周期与物理窗口完全独立，
                // 严禁将不同首段天体的拓扑强行锁死在单一历元，确保各拓扑独立在全域搜索窗口内遍历其真实星际会合面。
                subCfg.SearchStartUT = config.SearchStartUT;
                subCfg.SearchEndUT = config.SearchEndUT;

                if (subCfg.AutoTuneToFBounds)
                {
                    var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds(subCfg.DeparturePlanet, subCfg.ArrivalPlanet, subCfg.FlybySequence, subCfg.CentralBody, subCfg.DepartureToFMinFactor, subCfg.DepartureToFMaxFactor);
                    subCfg.TotalToFMinDays = (userSpecifiedMinToF > 0) ? userSpecifiedMinToF : dynMin;
                    subCfg.TotalToFMaxDays = (userSpecifiedMaxToF > 0) ? userSpecifiedMaxToF : dynMax;
                    subCfg.LegToFBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(subCfg.DeparturePlanet, subCfg.ArrivalPlanet, subCfg.FlybySequence, subCfg.CentralBody, subCfg.DepartureToFMinFactor, subCfg.DepartureToFMaxFactor);
                }

                // DeltaV-EGA (深空机动与借力协同): 在跨势阱折返或多次共振航段中，自适应激活 1-DSM 与多圈共振调相
                var fullChainEga = new List<string> { subCfg.DeparturePlanet };
                if (subCfg.FlybySequence != null)
                {
                    var cleanFlybys = subCfg.FlybySequence
                        .Where(b => !string.IsNullOrEmpty(b) && !b.Equals(subCfg.DeparturePlanet, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (cleanFlybys.Count > 0 && cleanFlybys.Last().Equals(subCfg.ArrivalPlanet, StringComparison.OrdinalIgnoreCase))
                    {
                        cleanFlybys = cleanFlybys.Take(cleanFlybys.Count - 1).ToList();
                    }
                    fullChainEga.AddRange(cleanFlybys);
                }
                if (fullChainEga.Count == 0 || !fullChainEga.Last().Equals(subCfg.ArrivalPlanet, StringComparison.OrdinalIgnoreCase))
                {
                    fullChainEga.Add(subCfg.ArrivalPlanet);
                }
                int neededLegs = fullChainEga.Count - 1;

                if (subCfg.LegStrategies == null || subCfg.LegStrategies.Count != neededLegs)
                {
                    var strats = new List<MGALegStrategy>();
                    for (int s = 0; s < neededLegs; s++)
                    {
                        // 天体力学第一性原理 (MGA-1DSM 统一公理):
                        // 1. 首航段 (s == 0): 若出发星与到达星不同 (From != To)，由运载火箭工质驱动发射注入开普勒弹道 (Ballistic)；
                        //    若出发星与到达星相同 (From == To，如 Juno 的 ΔV-EGA: Earth ➔ Earth，Rosetta: Earth ➔ Earth)，
                        //    根据开普勒守恒定律，航天器在同星之间无法通过纯弹道改变周期与相位，必须具备 1-DSM 自由度在远日点调相变轨！
                        // 2. 所有借力后航段 (s >= 1)，均从中间借力天体双曲流形出场。赋予 1-DSM (DeepSpaceManeuver1DSM) 自由度，
                        //    从物理几何上严格保证借力天体处为 100% 纯无动力引力偏折 (ΔV_FB = 0.00 m/s，|V_inf,out| = |V_inf,in|)；
                        //    对于天生共振或无需机动的航段，差分进化与能量优化会自动将 DSM 冲量解析收敛至 0 m/s。
                        bool isFirstLeg = (s == 0);
                        bool isResonantLeg = fullChainEga[s].Equals(fullChainEga[s + 1], StringComparison.OrdinalIgnoreCase);
                        if (isFirstLeg && !isResonantLeg)
                        {
                            strats.Add(MGALegStrategy.Ballistic);
                        }
                        else
                        {
                            strats.Add(MGALegStrategy.DeepSpaceManeuver1DSM);
                        }
                    }
                    subCfg.LegStrategies = strats;
                }
                else
                {
                    // 确保首航段若为同星共振，必须具备 1-DSM 自由度
                    bool isFirstResonant = fullChainEga[0].Equals(fullChainEga[1], StringComparison.OrdinalIgnoreCase);
                    if (isFirstResonant && subCfg.LegStrategies[0] == MGALegStrategy.Ballistic)
                    {
                        subCfg.LegStrategies[0] = MGALegStrategy.DeepSpaceManeuver1DSM;
                    }
                }



                string firstTarget = null;
                if (subCfg.FlybySequence != null && subCfg.FlybySequence.Count > 0)
                {
                    foreach (var b in subCfg.FlybySequence)
                    {
                        if (!string.IsNullOrEmpty(b) && !b.Equals(subCfg.DeparturePlanet, StringComparison.OrdinalIgnoreCase))
                        {
                            firstTarget = b;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(firstTarget)) firstTarget = subCfg.ArrivalPlanet;

                double synodicDays = PykepBridge.GetSynodicPeriodDays(subCfg.DeparturePlanet, firstTarget);
                if (synodicDays <= 0 || double.IsNaN(synodicDays)) synodicDays = 365.25;

                float stageP0 = 0.10f + 0.70f * ((float)tIdx / totalTopos);
                float stageP1 = 0.10f + 0.70f * ((float)(tIdx + 1) / totalTopos);
                string mid = (subCfg.FlybySequence != null && subCfg.FlybySequence.Count > 0) ? $"{string.Join(" ➔ ", subCfg.FlybySequence)} ➔ " : "";
                string topoSummary = $"{subCfg.DeparturePlanet} ➔ {mid}{subCfg.ArrivalPlanet}";

                onProgress?.Invoke(stageP0, $"Stage 2/3 [{tIdx + 1}/{totalTopos}]: 拓扑 [{topoSummary}] 会合时区初筛与多岛微分进化...");

                List<MGASurrogatePreFilter.SynodicWindowCandidate> windowCandidates;
                using (MGAStepProfiler.Scope("Stage 2: 窗口候选生成与会合面初筛"))
                {
                    windowCandidates = MGASurrogatePreFilter.FilterPromisingWindows(subCfg, synodicDays);
                    if (subCfg.MaxWindows > 0 && windowCandidates.Count > subCfg.MaxWindows)
                        windowCandidates = windowCandidates.Take(subCfg.MaxWindows).ToList();
                }

                List<MGASolution> rawSols;
                using (MGAStepProfiler.Scope("Stage 3: 多岛差分进化全流程 (DE Archipelago)"))
                {
                    rawSols = MGAArchipelagoOptimizer.EvolveWindows(
                        subCfg, 
                        windowCandidates, 
                        ct, 
                        (p, msg) => onProgress?.Invoke(stageP0 + (stageP1 - stageP0) * p, msg),
                        perTopoWindowParallel);
                }

                if (rawSols != null && rawSols.Count > 0)
                {
                    // 确定性次级键: 拓扑并行时 rawSols 来自多线程, 并列解顺序不定会改变 topoBest,
                    // 进而改变全局最优历元的选取。次级键全部由解自身物理量导出。
                    double subLaunchTol = subCfg.ConstraintProfile?.GetSpec(MGAConstraintType.LaunchC3Ceiling)?.ToleranceValue ?? 10.0;
                    var topoBest = rawSols
                        .OrderBy(x => (subCfg.LaunchDeltaVCeilingMs > 0 && x.LaunchDeltaVKms * 1000.0 > (subCfg.LaunchDeltaVCeilingMs + subLaunchTol) ? 1e6 : 0.0) + x.TotalDeltaVKms)
                        .ThenBy(x => Math.Round(x.DepartureUT * 1000.0))
                        .ThenBy(x => Math.Round(x.TotalFlightDays * 10000.0))
                        .FirstOrDefault();
                    // [优化] 逐链自愈 (Stage 4 per-topology 自适应质量调度):
                    // 绝望无潜力剪枝: 若拓扑最优解显著劣于当前全局最优 (且已有有效解) 或严重超出飞船预算，则跳过昂贵的二次演化自愈
                    bool isHopeless = (topoBest == null)
                        || (globalBestDvMs < 1e7 && topoBest.TotalDeltaVKms * 1000.0 > globalBestDvMs * 1.6)
                        || (subCfg.VesselMaxDeltaVMs > 0 && topoBest.TotalDeltaVKms * 1000.0 > subCfg.VesselMaxDeltaVMs * 2.5);

                    bool fastTier = (topoBest != null && subCfg.VesselMaxDeltaVMs > 0 && topoBest.TotalDeltaVKms * 1000.0 > subCfg.VesselMaxDeltaVMs * 1.5)
                                    || (tIdx >= Math.Max(4, totalTopos / 2));

                    if (!isHopeless)
                    {
                        var healedSols = MGASelfHealingEngine.AuditAndHeal(subCfg, rawSols, depHohmann, totHohmann, ct, fastTier);
                        if (healedSols != null && healedSols.Count > 0)
                        {
                            rawSols = healedSols;
                            topoBest = rawSols
                                .OrderBy(x => (subCfg.LaunchDeltaVCeilingMs > 0 && x.LaunchDeltaVKms * 1000.0 > (subCfg.LaunchDeltaVCeilingMs + subLaunchTol) ? 1e6 : 0.0) + x.TotalDeltaVKms)
                                .ThenBy(x => Math.Round(x.DepartureUT * 1000.0))
                                .ThenBy(x => Math.Round(x.TotalFlightDays * 10000.0))
                                .FirstOrDefault();
                        }
                    }

                    // 共享聚合态 (globalBest / allRawSolutions) 统一锁保护：
                    // 仅保护共享内存集合更新与标量刷新，严禁在临界区内包裹物理耗时演化与自愈重计算！
                    lock (aggLock)
                    {
                        if (topoBest != null)
                        {
                            double topoDv = topoBest.TotalDeltaVKms * 1000.0;
                            bool isLaunchCompliant = (subCfg.LaunchDeltaVCeilingMs <= 0 || topoBest.LaunchDeltaVKms * 1000.0 <= (subCfg.LaunchDeltaVCeilingMs + subLaunchTol));
                            KSPGMATLog.Info("Solver", $"   [拓扑 #{tIdx + 1}/{totalTopos} 收敛] {topoSummary}: TotalΔV={topoDv:F1} m/s (始发={topoBest.LaunchDeltaVKms * 1000.0:F1} m/s, 历元={GMATClient.FormatUtcGregorian(topoBest.DepartureUT)}, 合规={isLaunchCompliant})");
                            if (isLaunchCompliant && topoDv < globalBestDvMs)
                            {
                                globalBestDvMs = topoDv;
                                globalBestEpochUT = topoBest.DepartureUT;
                                KSPGMATLog.Success("Solver", $"   ★ [双向联动] 拓扑 #{tIdx + 1} 刷新全局最优: TotalΔV={globalBestDvMs:F1} m/s (始发={topoBest.LaunchDeltaVKms * 1000.0:F1} m/s), 最优历元={GMATClient.FormatUtcGregorian(globalBestEpochUT)}");
                            }
                        }



                        foreach (var s in rawSols)
                        {
                            if (s != null && !double.IsNaN(s.TotalDeltaVKms))
                            {
                                allRawSolutions.Add(s);
                            }
                        }
                    }
                }
            });

            if (allRawSolutions.Count == 0)
            {
                reportSw.Stop();
                Telemetry.MGAPipelineTelemetryProbe.EndMission(false);
                Telemetry.MGAPipelineTelemetryProbe.ExportReportToFile("reports/mga_math_diagnostic_report.md");
                return allRawSolutions;
            }

            double launchTol = config.ConstraintProfile?.GetSpec(MGAConstraintType.LaunchC3Ceiling)?.ToleranceValue ?? 10.0;
            double tofTolOrdering = config.ConstraintProfile?.GetSpec(MGAConstraintType.MaxMissionDuration)?.ToleranceValue ?? 10.0;

            var rawSolutions = allRawSolutions
                .OrderBy(s => (config.LaunchDeltaVCeilingMs > 0 && s.LaunchDeltaVKms * 1000.0 > (config.LaunchDeltaVCeilingMs + launchTol) ? 1e6 : 0.0)
                            + (effectiveMaxToF > 0 && s.TotalFlightDays > (effectiveMaxToF + tofTolOrdering) ? 1e6 : 0.0)
                            + (!s.AllLegsFeasible ? 1e5 : 0.0)
                            + s.TotalDeltaVKms)
                .ToList();

            onProgress?.Invoke(0.85f, "Stage 4: 物理基线校验与智能数值自愈门禁...");

            // (霍曼基准已在并行循环外计算, 见上方 depHohmann/totHohmann)

            List<MGASolution> auditedSolutions;
            using (MGAStepProfiler.Scope("Stage 4: 物理基线校验与数值自愈"))
            {
                auditedSolutions = MGASelfHealingEngine.AuditAndHeal(
                    config, 
                    rawSolutions, 
                    depHohmann, 
                    totHohmann, 
                    ct);
            }

            KSPGMATLog.Info("Solver", $"[AuditedSolutions Count] rawSolutions: {rawSolutions.Count}, auditedSolutions: {auditedSolutions.Count}");
            foreach (var r in auditedSolutions)
            {
                KSPGMATLog.Info("Solver", $"  • AuditedSol: {r.FlybyChainSummary}, TotalDv={r.TotalDeltaVKms * 1000.0:F1} m/s, LaunchDv={r.LaunchDeltaVKms * 1000.0:F1} m/s, Epoch={GMATClient.FormatUtcGregorian(r.DepartureUT)}");
            }

            // 方案去重与候选池精选: 实施拓扑配额保护 (每种独立借力拓扑至多 2 个最优解进入候选池)，
            // 彻底防止单一短周期拓扑因微小发射历元采样离散垄断全部候选池席位，确保深空大借力拓扑顺利交付。
            var finalSolutions = new List<MGASolution>();
            var seenKeys = new HashSet<string>();
            var topoCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            double finalBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
            double finalDsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : finalBudgetMs;

            double dvTol = config.ConstraintProfile?.GetSpec(MGAConstraintType.TotalVesselDeltaV)?.ToleranceValue ?? 10.0;
            double dsmTol = config.ConstraintProfile?.GetSpec(MGAConstraintType.MaxDsmDeltaV)?.ToleranceValue ?? 5.0;
            double tofTol = config.ConstraintProfile?.GetSpec(MGAConstraintType.MaxMissionDuration)?.ToleranceValue ?? 10.0;

            foreach (var sol in auditedSolutions)
            {
                if (finalBudgetMs < 1e8 && sol.TotalDeltaVKms * 1000.0 > (finalBudgetMs + dvTol))
                {
                    continue;
                }
                if (finalDsmBudgetMs < 1e8 && (sol.TotalDsmDeltaVKms * 1000.0 > (finalDsmBudgetMs + dsmTol) || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > (finalDsmBudgetMs + dsmTol)))))
                {
                    continue;
                }
                if (effectiveMaxToF > 0 && sol.TotalFlightDays > (effectiveMaxToF + tofTol))
                {
                    continue;
                }

                string chainKey = sol.FlybyChainSummary ?? "";
                topoCounts.TryGetValue(chainKey, out int chainCount);
                if (chainCount >= 2)
                {
                    continue;
                }

                // 1. 挂载 B 平面协方差分析与导航鲁棒性
                if (config.EnableBPlaneCovarianceAnalysis && sol.Legs != null && sol.Legs.Count > 1 && sol.BPlane == null)
                {
                    for (int k = 0; k < sol.Legs.Count - 1; k++)
                    {
                        var leg = sol.Legs[k];
                        var fbP = SolarSystemCatalog.GetPlanet(leg.ToBody);
                        if (fbP != null && fbP.Mu > 0)
                        {
                            using (MGAStepProfiler.Scope("Stage 4: B平面协方差分析与鲁棒性评估"))
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

                int utBucket = (int)(sol.DepartureUT / 43200.0);
                int tofBucket = (int)(sol.TotalFlightDays / 1.0);
                string compositeKey = $"{chainKey}@{utBucket}_{tofBucket}";

                if (!seenKeys.Contains(compositeKey))
                {
                    seenKeys.Add(compositeKey);
                    finalSolutions.Add(sol);
                    topoCounts[chainKey] = chainCount + 1;
                }
                if (finalSolutions.Count >= 24) break;
            }

            if (finalSolutions.Count == 0 && auditedSolutions.Count > 0)
            {
                var bestAudited = auditedSolutions.Deterministic().First();
                finalSolutions.Add(bestAudited);
            }

            var bestSol = finalSolutions.FirstOrDefault() ?? auditedSolutions.FirstOrDefault() ?? rawSolutions.FirstOrDefault();
            if (bestSol != null)
            {
                MGAFlybyDynamicsHelper.EnrichSolutionFlybyDynamics(bestSol);
            }

            reportSw.Stop();
            Telemetry.MGAPipelineTelemetryProbe.RecordFinalSolutions(finalSolutions);
            Telemetry.MGAPipelineTelemetryProbe.EndMission(finalSolutions.Count > 0);
            Telemetry.MGAPipelineTelemetryProbe.ExportReportToFile("reports/mga_math_diagnostic_report.md");

            // 打印全流程细粒度步骤耗时排名与最大瓶颈诊断
            MGAStepProfiler.PrintSummary();

            // 打印新版四阶段管线优化探针报表
            Telemetry.MGAPipelineTelemetryProbe.PrintEfficiencyReport();

            onProgress?.Invoke(1.0f, $"自适应求解完成！输出 {finalSolutions.Count} 个 NASA 级精选方案");
            return finalSolutions;
        }

        /// <summary>
        /// MGA 四阶段流水线异步求解入口 (在 Task.Run 独立线程池中执行 Solve，避免阻塞 UI 线程)
        /// </summary>
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
            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : MGADefaults.AU_KM;
            double rArr = (pArr != null && pArr.SMA > 0) ? pArr.SMA : MGADefaults.AU_KM;
            double aTransfer = (rDep + rArr) * 0.5;
            double muCentralM = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu) * 1e9;
            double tHohmannSec = Math.PI * Math.Sqrt(Math.Pow(aTransfer * 1000.0, 3.0) / muCentralM);
            double tHohmannDays = tHohmannSec / MGADefaults.SecondsPerDay;
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

            int stage1Parallel = Math.Min(candidateChains.Count, (Environment.ProcessorCount >= 16) ? 3 : ((Environment.ProcessorCount >= 8) ? 2 : 1));
            Parallel.For(0, candidateChains.Count, new ParallelOptions { MaxDegreeOfParallelism = stage1Parallel }, cIdx =>
            {
                if (ct.IsCancellationRequested) return;

                var chain = candidateChains[cIdx];
                var subCfg = baseConfig.Clone();
                subCfg.FlybySequence = new List<string>(chain);
                subCfg.EnablePermutationsAndInsertions = false;
                subCfg.LegStrategies = null;
                subCfg.LegToFBounds = null;
                subCfg.AutoTuneToFBounds = true;

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
                        if (s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) // 过滤 >80 km/s 的动力学发散解
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

            // 6. 聚合与帕累托双轨排序 (最省 vs 最快，优先全航段可行解)
            var feasibleSols = allSols.Where(s => s.AllLegsFeasible).ToList();
            var candidatePool = (feasibleSols.Count > 0) ? feasibleSols : allSols.ToList();
            var rankedFuel = candidatePool.Deterministic().ToList();
            var rankedTime = candidatePool.DeterministicByFlightDays().ToList();

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
            // 【2026-09-14 修正】取出与损失函数**同一个**参考预算 dvRef, 使"优化目标"与"选解排序"
            // 使用完全一致的航时软界尺度 (原为两处各自拍定的不同形式: 加性 vs 乘性)。
            double dvRefKmsForOrder = MGAPhysicsLossEngine.LossReferenceCache.Get(baseConfig);
            result.BestSolution = MGASolutionOrdering.SelectParetoKneeSolution(pareto, baseConfig?.CentralBody ?? "Sun", dvRefKmsForOrder) ?? rankedFuel.FirstOrDefault();

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

        // ---------------------------------------------------------------------
        // 【2026-09-14 审计处置 —— 已删除 public static bool ApplySolutionToConfig(...)】
        //
        // 原实现把**已收敛解**的物理量回写为下一轮搜索的边界:
        //     config.EarliestDepartureUT = solution.DepartureUT;
        //     config.LatestDepartureUT   = solution.DepartureUT + 86400.0;
        //     config.MinToFDays = solution.Legs.Select(l => Math.Max(5.0, l.ToFDays * 0.9)).ToList();
        //     config.MaxToFDays = solution.Legs.Select(l => l.ToFDays * 1.1).ToList();
        // 这正是历史审计声称"已清除"的**航时 ±10% 夹逼**的实际形态 ——
        // 它把任意一次求解的输出变成下一次求解的窄邻域, 使后续"寻优"退化为在已知解附近精修。
        //
        // 处置: 删除。理由: (1) 全仓 (src/ + tests/) 0 个调用点; (2) 属未受保护的 API,
        // 一旦被接线即构成作弊路径 (把答案喂回搜索域)。
        // 若后续确有"从已得解热启动"的需求, 必须新建**显式命名**的 API
        // (如 WarmStartFromSolution(...)), 并在 MGAEmpiricalRegistry 登记其使用条件与风险。
        // ---------------------------------------------------------------------

        /// <summary>
        /// 全自动自主求解异步门面方法 (包装 SolveAutonomous 至后台线程)
        /// </summary>
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
                    double rC = SolarSystemCatalog.GetPlanet(central)?.RadiusKm ?? MGADefaults.GetBodyRadiusKm(central);
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

        /// <summary>
        /// 全局多段差分进化寻优器 (基于 PyKEP 原生全流程评估，自动按天体公转/会合周期分窗并发寻优，杜绝单月早熟坍缩)
        /// 异步门面包装方法
        /// </summary>
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
                        KSPGMATLog.Error("Solver", $"Multi-chain sequence note: {ex.Message}", ex);
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
            // 确定性排序: ConcurrentBag 的枚举顺序随线程调度变化, 仅按 ΔV 排序会使
            // 近并列解的顺序不确定, 破坏"固定种子可复现"的前提 (实测 cassini 两次差 78.7 m/s)。
            var sortedList = aggregatedSolutions
                .Where(s => s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < 1e7)
                .Deterministic()
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

        /// <summary>
        /// 基于行星轨道内外侧拓扑与物理约束自动生成候选借力序列（单序列入口包装）
        /// </summary>
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
