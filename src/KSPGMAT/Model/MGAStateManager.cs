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
    /// MGA 官方标准缺省值与物理常数集中注册表 (Single Source of Default Values & Constants)
    /// 集中管理 MGA 模块所有物理、几何、算法超参数与安全阈值，彻底杜绝各文件中的局部硬编码 (基线 0/1/8/9/13)
    /// </summary>
    public static class MGADefaults {
        // --- 1. 轨道与天体几何基准 ---
        public const double DepartureAltitudeKm = 200.0;
        public const double DestinationAltitudeKm = 200.0;
        public const double DefaultFlybyAltitudeKm = 300.0;
        public const double MinSafeAltitudeKm = 30.0;
        public const double DefaultEarthInclinationDeg = 28.5; // KSC 经典发射倾角
        public const double DefaultKerbinInclinationDeg = 0.0; // Kerbin 赤道发射场倾角
        public const double KspOrbitPeAMinSafeM = 30000.0;     // 30 km 游戏停泊轨近心判定阈值

        // --- 2. 低推力推进与质量模型 ---
        public const double LowThrustMaxThrustN = 0.5;
        public const double LowThrustIspSec = 3000.0;
        public const double SpacecraftWetMassKg = 1500.0;
        public const int LowThrustSegments = 10;
        public const int LowThrustMinSegments = 4;
        public const int LowThrustMaxSegments = 32;
        public const double SimsFlanaganMassFractionGuess = 0.85;

        // --- 3. 任务与时间搜索窗口 ---
        public const double DsmEtaDefault = 0.5;
        public const double MinSearchYears = 3.0;
        public const double InterplanetarySearchDays = 1095.75; // 365.25 * 3.0
        public const double SystemSearchDays = 30.0;
        public const double MaxDurationDays = 4000.0;
        public const double DefaultSearchWindowDays = 30.0;
        public const double PresetSearchMarginDays = 20.0;
        public const double DefaultSequenceSearchMarginDays = 15.0;
        public const double DefaultMinToFDays = 50.0;
        public const double DefaultMaxToFDays = 300.0;
        public const double ToFMarginMinFactor = 0.7;
        public const double ToFMarginMaxFactor = 1.3;

        // --- 4. 差分寻优与全局求解超参数 ---
        public const int PopulationSize = 60;
        public const int MaxGenerations = 50;
        public const double DE_F_Min = 0.5;
        public const double DE_F_Range = 0.4;
        public const double DE_CR_Min = 0.7;
        public const double DE_CR_Range = 0.2;
        public const int SolutionPoolMaxCount = 10;
        public const double SolutionClusterTimeBucketSec = 259200.0; // 3.0 天

        // --- 5. 帕累托非劣前沿权衡参数 ---
        public const int ParetoPointCount = 9;
        public const double ParetoMinFlightDays = 20.0;
        public const double ParetoMinTofFactor = 0.65;
        public const double ParetoMaxDvMultiplier = 2.5;
        public const double ParetoMaxAbsoluteDvKms = 25.0;
        public const double ParetoEqualityToleranceDays = 0.1;
        public static readonly double[] ParetoScaleFactors = new double[] { 1.35, 1.22, 1.12, 1.05, 1.0, 0.94, 0.88, 0.82, 0.75 };

        // --- 6. 拓扑拓荒序列搜索超参数 (BFS) ---
        public const int MaxSwingBys = 4;
        public const int MaxResonances = 1;
        public const int MaxBackLegs = 1;
        public const int MaxBackSpacing = 2;
        public const int MaxBfsVisitedCount = 3000;
        public const int MaxCandidateResults = 100;

        // --- 7. 霍曼与扫描动力学估算 ---
        public const double HohmannDefaultToFDays = 200.0;
        public const double HohmannMinToFFactor = 0.35;
        public const double HohmannMaxToFFactor = 1.35;
        public const double ResonanceMinToFFactor = 0.50;
        public const double ResonanceMaxToFFactor = 3.60;
        public const double SynodicScanMultiple = 1.5;
        public const double SynodicScanMultipleWithInc = 2.0;

        // --- 8. 猪排图 (Porkchop) 引擎参数 ---
        public const int PorkchopDefaultWidth = 100;
        public const int PorkchopDefaultHeight = 75;
        public const double PorkchopMaxValidDv = 100000.0;

        // --- 9. 停泊轨点火锁相与双曲逃逸几何 ---
        public const int AlignSampleCount = 72;
        public const int AlignGoldenSectionIterations = 15;
        public const double AlignGoldenRatio = 0.618033988749895; // (sqrt(5)-1)/2
        public const double AlignMinLeadTimeSec = 60.0;
        public const double AlignFallbackOrbitPeriodSec = 5400.0; // 90 min

        // --- 10. 权威物理与天文常数 (SSOT & Ephemeris-Driven) ---
        public const double AU_KM = 149597870.7;
        public const double SecondsPerDay = 86400.0;
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;

        // 离线/星历未就绪时的兜底基准值 (Fallback Constants)
        public const double FallbackEarthMu = 398600.4418;
        public const double FallbackEarthRadiusKm = 6378.14;
        public const double FallbackEarthSafeRadiusKm = 6518.14;
        public const double FallbackMoonAverageSMASecKm = 384400.0;
        public const double FallbackSunMu = 1.32712440018e11;
        public const double FallbackDepartureAltitudeKm = 200.0;

        // 向下兼容现有调用的星历动态属性 (优先从 SolarSystemCatalog 提取权威真值)
        public static double EarthMu => GetBodyMu("Earth");
        public static double EarthMuM3S2 => EarthMu * 1e9;
        public static double EarthRadiusKm => GetBodyRadiusKm("Earth");
        public static double EarthSafeRadiusKm => GetBodySafeRadiusKm("Earth");
        public static double MoonAverageSMASecKm {
            get {
                var p = SolarSystemCatalog.GetPlanet("Moon");
                return (p != null && p.SMA > 0) ? p.SMA : FallbackMoonAverageSMASecKm;
            }
        }
        public static double SunMu => SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : FallbackSunMu;

        /// <summary>
        /// 动态提取天体物理引力常数 Mu (km^3/s^2)，优先读取权威星历，无数据时自动回退
        /// </summary>
        public static double GetBodyMu(string bodyName) {
            var p = SolarSystemCatalog.GetPlanet(bodyName);
            return (p != null && p.Mu > 0) ? p.Mu : FallbackEarthMu;
        }

        /// <summary>
        /// 动态提取天体物理半径 (km)，优先读取权威星历
        /// </summary>
        public static double GetBodyRadiusKm(string bodyName) {
            var p = SolarSystemCatalog.GetPlanet(bodyName);
            return (p != null && p.RadiusKm > 0) ? p.RadiusKm : FallbackEarthRadiusKm;
        }

        /// <summary>
        /// 动态提取天体安全防撞球半径 SafeRadiusKm (km)
        /// </summary>
        public static double GetBodySafeRadiusKm(string bodyName) {
            var p = SolarSystemCatalog.GetPlanet(bodyName);
            if (p != null && p.SafeRadiusKm > 0) return p.SafeRadiusKm;
            double r = GetBodyRadiusKm(bodyName);
            double atm = p != null ? p.AtmosphereDepthKm : 0.0;
            return SolarSystemCatalog.ComputeAutomaticSafeRadiusKm(bodyName, r, atm);
        }

        /// <summary>
        /// 根据天体大气与几何物理属性，动态推导权威安全出发/停泊轨道高度 (km)
        /// 大气天体自适应为大气顶层 + 20km 缓冲；无大气小天体自适应为 25km 或 5% 半径
        /// </summary>
        public static double GetSafeDepartureAltitudeKm(string bodyName) {
            var p = SolarSystemCatalog.GetPlanet(bodyName);
            if (p != null) {
                if (p.AtmosphereDepthKm > 0) return p.AtmosphereDepthKm + 20.0;
                return Math.Max(25.0, p.RadiusKm * 0.05);
            }
            return FallbackDepartureAltitudeKm;
        }

        /// <summary>
        /// 权威推导天体到达捕获轨道的物理安全边界与科学推荐值 (km)
        /// 1. 近心点安全底线 minPeAKm: 严禁触碰大气顶层 (有大气: atmDepth + 20km; 无大气小天体: 15km 或 2% 半径)
        /// 2. 推荐捕获高度 recommendedPeAKm: 经典科学捕获轨道高度 (气态巨行星 1500~2500km, 类地行星 150~300km, 卫星 50~100km)
        /// 3. 远心点安全上限 maxApAKm: 严禁逃逸引力球 SOI (不超过 0.80 * SoiKm, 无穷大时回退 50 * RadiusKm)
        /// </summary>
        public static void GetBodyCaptureAltitudeBounds(string bodyName, out double minPeAKm, out double recommendedPeAKm, out double maxApAKm) {
            var p = SolarSystemCatalog.GetPlanet(bodyName);
            if (p != null) {
                double r = p.RadiusKm > 0 ? p.RadiusKm : 6000.0;
                double atm = p.AtmosphereDepthKm;
                double soi = p.SoiKm;

                if (atm > 0) {
                    minPeAKm = Math.Round(atm + 20.0);
                    // 巨行星（如木星、土星、天王星、海王星等）按其巨大半径自适应推荐安全高度
                    recommendedPeAKm = Math.Round(atm + Math.Max(50.0, r * 0.03));
                } else {
                    minPeAKm = Math.Max(15.0, Math.Round(r * 0.02));
                    recommendedPeAKm = Math.Max(50.0, Math.Round(r * 0.05));
                }

                if (soi > 0 && !double.IsInfinity(soi)) {
                    maxApAKm = Math.Max(minPeAKm * 2.0, Math.Round(soi * 0.80));
                } else {
                    maxApAKm = Math.Max(minPeAKm * 2.0, Math.Round(r * 50.0));
                }
            } else {
                minPeAKm = 100.0;
                recommendedPeAKm = 200.0;
                maxApAKm = 500000.0;
            }
        }

        /// <summary>
        /// 动态解析天体推荐发射倾角 (Earth -> 28.5°, Kerbin -> 0.0°)
        /// </summary>
        public static double GetDefaultLaunchInclinationDeg(string bodyName) {
            string canon = SolarSystemCatalog.CanonicalizeName(bodyName);
            if (canon.Equals("Earth", StringComparison.OrdinalIgnoreCase)) return DefaultEarthInclinationDeg;
            return DefaultKerbinInclinationDeg;
        }

        /// <summary>
        /// 动态推导天体停泊轨道的安全运行周期 (s)
        /// </summary>
        public static double GetSafeOrbitPeriodSec(string bodyName, double altitudeKm = -1.0) {
            double alt = altitudeKm > 0 ? altitudeKm : GetSafeDepartureAltitudeKm(bodyName);
            double rKm = GetBodyRadiusKm(bodyName) + alt;
            double muKms = GetBodyMu(bodyName);
            if (muKms <= 0.0 || rKm <= 0.0) return AlignFallbackOrbitPeriodSec;
            return PykepBridge.CalcOrbitPeriodSec(rKm, muKms);
        }

        // --- 11. 求解器惩罚函数超参数 (Penalty Weights) ---
        public const double PenaltyInfeasibleBase = 10000.0;
        public const double PenaltyDepartureViolationDayWeight = 100.0;
        public const double PenaltyDepartureViolationBase = 1000.0;
        public const double PenaltyVesselDeltaVWeight = 15.0;
        public const double PenaltyVesselDeltaVBase = 30.0;
        public const double PenaltyIncDegWeight = 25.0;
        public const double PenaltyIncBase = 50.0;
        public const double PenaltyIncToleranceDeg = 0.05;
        public const double PenaltyPerDsmLeg = 300.0; // 每一条携带 DSM(>100m/s) 的腿固定惩罚, 强化纯弹道引力弹弓链偏好

        // --- 12. 引力势阱与梯级爬升常数 (已由 MGAPhysicalScales 与 KeplerAdjacencyGraph 连续物理取代) ---
        [Obsolete("Use MGAPhysicalScales or KeplerAdjacencyGraph instead")]
        public const double GiantAssistDistanceRatio = 3.0;
        [Obsolete("Use MGAPhysicalScales or KeplerAdjacencyGraph instead")]
        public const double GravityClimbingDistanceRatio = 2.0;
        public static double InnerSystemMaxSmaKm => 4.0 * AU_KM;
        [Obsolete("Use MGAPhysicalScales.TransferDifficultyRatio instead")]
        public const double MinAssistPowerIndexInner = 35.0;
        [Obsolete("Use Node.IsGiant instead")]
        public const double GiantPlanetMinMu = 1.0e7;
        [Obsolete("Use KeplerAdjacencyGraph instead")]
        public const double MinSteppingStoneMu = 500.0;
        [Obsolete("Use KeplerAdjacencyGraph instead")]
        public const double NeighborBodyMaxLogSmaRatio = 1.5;

        // --- 13. 开普勒共振与转角动力学常数 ---
        public const double ResonantOrbitVelocityBonusKms = 1.5;
        public const double MinEffectiveVInfKms = 2.5;
        public const double NominalInitialVInfKms = 5.0;
        public const double DeparturePhaseAngleToleranceDeg = 45.0;
        public const double DeparturePhaseSoftPenaltyFactorMsPerDeg = 5.0;
        public const double MinGeometryTurnAngleDeg = 25.0;
        public const double ApsidalTurnaroundAngleDeg = 30.0;
        public const double ResonantReverseTurnAngleDeg = 75.0;
        public const double DirectInwardOutwardTurnAngleDeg = 120.0;

        // --- 14. 航时与搜索空间缩放常数 (ToF & Search Bounds) ---
        public const double NearTransferToFMinFactor = 0.68;
        public const double NearTransferToFMaxFactor = 1.85;
        public const double FarTransferToFMinFactor = 0.35;
        public const double OuterTransferToFMinFactor = 0.20;
        public const double OuterTransferToFMaxFactor = 2.30;
        public const double ResonanceToFMinFactor = 0.85;
        public const double ResonanceToFMaxFactor = 4.25;
        public const double AdjacentTransferSmaRatioThreshold = 2.2; // 近邻 vs 跨越转移 Sma 比值阈值

        // --- 15. 多岛差分进化与控制常数 (Evolutionary Control & Early Pruning) ---
        public const double DefaultAdaptivePopulationSize = 100;
        public const double DefaultAdaptiveMaxGenerations = 150;
        public const double DefaultMaxDsmDeltaVMs = 500.0;
        public const double MaxPhysicalEnergyDeficitMs = 4000.0; // 单航段不可达物理速度断层剪枝硬门禁
        public const int EarlyAbortGenerationThreshold = 25;
        public const double EarlyAbortDeficitThresholdMs = 4000.0; // 早期赤字熔断门槛

        // --- 16. 可行性审计与门禁裕度 (Feasibility Audit & Gate Margins) ---
        public const double FeasibilityBudgetMarginFactor = 1.25; // 单段硬门禁与总预算软门禁统一裕度 (DE 可微调余量)

        // ⚠️ [实验归档 2026-09-11] 曾在此处引入 SupervisedTotalDeficitCeilingMs = 1521.0
        // (ROC Youden J 最优, F1 0.159 -> 0.282, 候选池缩小 ~85%), 但 13 任务 A/B 实测:
        //   结果 13/13 逐位一致 (零收益) + 求解耗时 +125% (68.2s -> 153.4s, 显著劣化).
        // 根因: 门禁变严 -> 可行链奖励信号稀疏 -> MCTS 需探索更多节点补偿,
        //       省下的 DE 精优化开销远小于 MCTS 探索膨胀开销。
        // 结论: 已回退。保留此注释作为"中间指标改善 != 端到端收益"的反例警示。
        // 详见 docs/reports/硬编码与经验公式审计报告.md §22。

        public const double MaxPlausibleTotalDvKms = 80.0; // 物理合理总 ΔV 上界 (km/s): 过滤动力学发散解 (多航段如 BepiColombo 10段任务允许中间态 < 80 km/s)
    }

    /// <summary>
    /// MGA 出发停泊轨道约束模式
    /// </summary>
    public enum MGADepartureMode {
        PreLaunchFreeOrbit = 0,    // 模式 1: 发射前规划 (火箭未发射，算法推荐最佳发射倾角与发射升交点)
        InOrbitLockedOrbit = 1     // 模式 2: 已在轨转移 (飞船已入轨，当前轨道参数 PeA/ApA/INC/RAAN/AOP 严格锁死)
    }

    /// <summary>
    /// 航段推进与动力学策略枚举 (Per-Leg Astrodynamic Strategy)
    /// </summary>
    public enum MGALegStrategy {
        Ballistic = 0,               // 纯弹道 (Pure Ballistic Lambert)
        DeepSpaceManeuver1DSM = 1,    // 深空机动 1-DSM (Midcourse In-flight Burn)
        ContinuousLowThrust = 2      // 连续低推力 (Sims-Flanagan SEP)
    }

    /// <summary>
    /// 中间交会节点飞掠与打靶策略枚举 (Per-Node Flyby & Targeting Strategy)
    /// </summary>
    public enum MGAFlybyStrategy {
        UnpoweredFlyby = 0,          // 纯无动力自由双曲借力 (Unpowered Flyby)
        PoweredFlyby = 1,            // 动力飞掠 (Powered Assist)
        BPlaneTargeted = 2           // B 平面精准穿靶 (B-Plane Targeted TCM)
    }

    /// <summary>
    /// MGA 求解引擎全局动力学模式 (Engine Dynamics Mode)
    /// </summary>
    public enum MGAEngineMode {
        BallisticImpulsive = 0,    // 传统纯脉冲借力 (Ballistic MGA)
        DeepSpaceManeuver1DSM = 1, // 官方 MGA-1DSM (含中道深空机动与共振)
        LowThrustSimsFlanagan = 2, // 连续低推力打靶 (Sims-Flanagan / SEP)
        HeterogeneousHybrid = 3    // 异构分段混合策略 (Multi-Strategy Hybrid)
    }

    /// <summary>
    /// MGA 事件类型枚举 (100% 对齐 KSP-MGA-Planner 与真实任务事件)
    /// </summary>
    public enum MGAEventType {
        Escape,          // 1: <Origin> escape (出发脱离机动)
        DSM,             // 2: <From>-<To> DSM (深空变轨/中途机动)
        Flyby,           // 3: <Body> flyby (经过天体的引力助推飞掠)
        Circularization, // 4: <Destination> circularization / capture (目标行星捕获/圆化机动)
        TCM,             // 5: <From>-<To> TCM (B平面中道修正)
        LowThrustSegment // 6: 连续低推力打靶航段
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

    public class DSMResult {
        public double EpochOffsetSec { get; set; }
        public Vector3d PositionKm { get; set; }
        public Vector3d DeltaVKms { get; set; }
        public double DeltaVMs { get; set; }
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }
    }

    public class BPlaneResult {
        public double BDotTKm { get; set; }
        public double BDotRKm { get; set; }
        public double BMagKm { get; set; }
        public double ThetaDeg { get; set; }
        public double VInfMagKms { get; set; }
        public Vector3d SHat { get; set; }
        public Vector3d THat { get; set; }
        public Vector3d RHat { get; set; }

        // B 平面协方差摄动分析与误差椭圆 (Covariance Dispersion Ellipse & Navigational Robustness)
        public double SigmaBTKm { get; set; }
        public double SigmaBRKm { get; set; }
        public double CovarianceBTRKm2 { get; set; }
        public double SemiMajorAxisSigmaKm { get; set; }
        public double SemiMinorAxisSigmaKm { get; set; }
        public double EllipseAngleDeg { get; set; }
        public double SafeFlybyRadiusKm { get; set; }
        public double EstimatedPericenterKm { get; set; }
        public double SafeMargin3SigmaKm { get; set; }
        public bool IsSafeFlyby { get; set; } = true;
        public double NavigationalTcmBudgetKms { get; set; }

        /// <summary>
        /// DirectCompute GPU 极速蒙特卡洛 6.5 万点轨道分散度与 B 平面协方差椭圆分析
        /// </summary>
        public static BPlaneResult ComputeMonteCarloDispersionGPU(
            Vector3d r0Km, Vector3d v0Kms,
            Vector3d targetRKm, Vector3d targetVKms,
            double targetMu, double targetRadiusKm,
            double tofSec, double centralMu,
            double sigmaPosKm = 5.0, double sigmaVelKms = 0.001,
            int sampleCount = 65536)
        {
            var res = new BPlaneResult();
            if (!PykepNative.IsGpuComputeAvailable)
            {
                MGA.Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution("GPU B-Plane Monte Carlo", 0.0, sampleCount, false);
                return res;
            }

            var sw = Stopwatch.StartNew();
            double[] r0 = new double[] { r0Km.x, r0Km.y, r0Km.z };
            double[] v0 = new double[] { v0Kms.x, v0Kms.y, v0Kms.z };
            double[] tr = new double[] { targetRKm.x, targetRKm.y, targetRKm.z };
            double[] tv = new double[] { targetVKms.x, targetVKms.y, targetVKms.z };

            int ret = PykepNative.BPlaneMonteCarloGpuEvaluate(
                r0, v0, tr, tv,
                targetMu, targetRadiusKm, tofSec, centralMu,
                sigmaPosKm, sigmaVelKms, sampleCount,
                out float meanBR, out float meanBT,
                out float sigRR, out float sigTT, out float sigRT,
                out float collProb);

            sw.Stop();
            MGA.Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution(
                "GPU B-Plane Monte Carlo", sw.Elapsed.TotalMilliseconds, sampleCount, ret == 0);

            if (ret == 0)
            {
                res.BDotRKm = meanBR;
                res.BDotTKm = meanBT;
                res.BMagKm = Math.Sqrt(meanBR * meanBR + meanBT * meanBT);
                res.ThetaDeg = Math.Atan2(meanBR, meanBT) * 180.0 / Math.PI;
                res.SigmaBRKm = sigRR;
                res.SigmaBTKm = sigTT;
                res.CovarianceBTRKm2 = sigRT;

                // 2D 协方差椭圆主轴特征值求解
                double a = sigRR * sigRR;
                double b = sigRT;
                double c = sigTT * sigTT;
                double lambda1 = 0.5 * (a + c + Math.Sqrt((a - c) * (a - c) + 4.0 * b * b));
                double lambda2 = 0.5 * (a + c - Math.Sqrt((a - c) * (a - c) + 4.0 * b * b));

                res.SemiMajorAxisSigmaKm = 3.0 * Math.Sqrt(Math.Max(0.0, lambda1));
                res.SemiMinorAxisSigmaKm = 3.0 * Math.Sqrt(Math.Max(0.0, lambda2));
                res.EllipseAngleDeg = 0.5 * Math.Atan2(2.0 * b, a - c) * 180.0 / Math.PI;
                res.IsSafeFlyby = collProb < 1e-4;
            }

            return res;
        }
    }

    /// <summary>
    /// 多体引力流形低能捕获与弱稳定性边界 (CR3BP Invariant Manifolds & WSB Low-Energy Capture)
    /// </summary>
    public class LowEnergyManifoldResult {
        public bool IsManifoldCaptureFeasible { get; set; }
        public double JacobiConstant { get; set; }
        public double HillRadiusKm { get; set; }
        public double WsbCaptureDeltaVKms { get; set; }
        public double ClassicalVisVivaDeltaVKms { get; set; }
        public double DeltaVSavedKms { get; set; }
        public double SavingPercentage { get; set; }
        public double IngressAngleDeg { get; set; }
        public double BallisticDurationDays { get; set; }
        public string Details { get; set; }
    }

    public class TCMResult {
        public Vector3d DeltaVKms { get; set; }
        public double DeltaVMs { get; set; }
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }
    }

    public class PrimerVectorResult {
        public int SampleCount { get; set; }
        public double[] TimesSec { get; set; }
        public double[] PMags { get; set; }
        public double MaxPMag { get; set; }
        public double MaxPTimeSec { get; set; }
        public bool IsOptimal { get; set; }
    }

    public class MEEResult {
        public double P { get; set; }
        public double F { get; set; }
        public double G { get; set; }
        public double H { get; set; }
        public double K { get; set; }
        public double L { get; set; }
    }

    public class SimsFlanaganResult {
        public double DeltaRKm { get; set; }
        public double DeltaVKms { get; set; }
        public double DeltaMKg { get; set; }
        public double TotalDeltaVMs { get; set; }
        public double FinalMassKg { get; set; }
        public int SegmentCount { get; set; }
        public List<Vector3d> SegmentPositionsKm { get; set; } = new List<Vector3d>();
        public List<Vector3d> ThrottleVectors { get; set; } = new List<Vector3d>();
    }

    public class ParetoTradeOffPoint {
        public double TotalFlightDays { get; set; }
        public double TotalDeltaVKms { get; set; }
        public double LaunchDeltaVKms { get; set; }
        public double FlybyDeltaVKms { get; set; }
        public double[] Genome { get; set; }
    }

    /// <summary>
    /// MGA 单个转移弧段求解明细 (Single Transfer Leg Result)
    /// </summary>
    public class MGALegResult {
        public int LegIndex { get; set; }
        public string FromBody { get; set; }
        public string ToBody { get; set; }
        public double DepartureUT { get; set; }
        public double ArrivalUT { get; set; }
        public double ToFDays { get; set; }
        public double DeltaVKms { get; set; }
        public Vector3d DeparturePosKm { get; set; }
        public Vector3d ArrivalPosKm { get; set; }
        public Vector3d DepartureVelKms { get; set; }
        public Vector3d ArrivalVelKms { get; set; }
        
        // Maneuver Vector Components in Local Orbital Frame (m/s)
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }

        // Interplanetary Ejection & Insertion Flight Mechanics Parameters
        public double PhaseAngleDeg { get; set; }
        public double EjectionAngleDeg { get; set; }
        public double EjectionLANDeg { get; set; }
        public double EjectionInclinationDeg { get; set; }
        public double InsertionInclinationDeg { get; set; }
        public Vector3d VInfDepartureKms { get; set; }
        public Vector3d VInfArrivalKms { get; set; }
        public double C3Kms2 { get; set; }
        public double EjectionDeltaVKms { get; set; }
        public double InsertionDeltaVKms { get; set; }

        public double FlybyTurningAngleDeg { get; set; }
        public double FlybyPeriapsisRadiusKm { get; set; }
        public double FlybyPeriapsisAltitudeKm { get; set; }
        public double FlybyDeltaVKms { get; set; }
        public bool IsFeasible { get; set; }

        public double IncomingDhaDeg { get; set; }
        public double IncomingDecDeg { get; set; }
        public BPlaneResult BPlane { get; set; }

        public bool HasDSM { get; set; }
        public DSMResult DSM { get; set; }
        public Vector3d DSMPositionKm { get; set; }
        public MGALegStrategy Strategy { get; set; } = MGALegStrategy.Ballistic;
    }

    /// <summary>
    /// MGA 离散任务事件与机动节点 (Discrete MGA Mission Event & Maneuver)
    /// </summary>
    public class MGAEventItem {
        public int SequenceNumber { get; set; }
        public string Title { get; set; }
        public MGAEventType EventType { get; set; }
        public string BodyName { get; set; }
        public string FromBody { get; set; }
        public string ToBody { get; set; }
        public double EpochUT { get; set; }
        public double DeltaVMs { get; set; }
        public Vector3d PositionKm { get; set; }
        
        // 3 轴机动矢量分量 (m/s)
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }
        
        // 飞掠引力助推特征参数 (Flyby Geometry)
        public double FlybyTurningAngleDeg { get; set; }
        public double FlybyPeriapsisRadiusKm { get; set; }
        public double FlybyPeriapsisAltitudeKm { get; set; }
        public double VInfInKms { get; set; }
        public double VInfOutKms { get; set; }
        public bool IsFeasible { get; set; } = true;
        
        // 出发脱离特征参数 (Escape / Ejection Geometry)
        public double PhaseAngleDeg { get; set; }
        public double EjectionAngleDeg { get; set; }
        public double EjectionLANDeg { get; set; }
        public double EjectionInclinationDeg { get; set; }
        public double C3Kms2 { get; set; }
        
        // 目标捕获特征参数 (Capture Geometry)
        public double InsertionAltitudeKm { get; set; }
        public double InsertionInclinationDeg { get; set; }
    }

    /// <summary>
    /// MGA 全局多重引力助推轨道规划完整方案 (Complete MGA Trajectory Solution)
    /// </summary>
    public class MGASolution {
        public double TotalDeltaVKms { get; set; }
        public double TotalFlightDays { get; set; }
        public double DepartureUT { get; set; }
        public double ArrivalUT { get; set; }
        public double LaunchDeltaVKms { get; set; }
        public double InsertionDeltaVKms { get; set; }
        public double TotalFlybyDeltaVKms => Legs != null ? Legs.Sum(l => l.FlybyDeltaVKms) : 0.0;
        public double TotalDsmDeltaVKms => Legs != null ? Legs.Sum(l => l.DSM != null ? l.DSM.DeltaVMs / 1000.0 : 0.0) : 0.0;

        // ===== 硬门禁合规审计字段 (Hard Gate Compliance) =====
        // 由 DE RunWindow 最终选择阶段统一赋值：无论进化期采用何种罚函数引导收敛，
        // 上报的最优解都必须经过严格硬校验，超标值显式标记，绝不让超门禁解冒充达标。
        public bool MeetsHardDvGate { get; set; }                 // 总ΔV + 发射 + 单腿/总DSM 全部落入硬门禁
        public double LaunchOverBudgetMs { get; set; }            // 发射超支 (>0 即超 LaunchDeltaVCeilingMs)
        public double DsmOverBudgetMs { get; set; }               // 总DSM 超支 (>0 即超 MaxDsmDeltaVMs)
        public double TotalOverBudgetMs { get; set; }             // 任务总ΔV 超支 (>0 即超 VesselMaxDeltaVMs)
        public List<MGALegResult> Legs { get; set; } = new List<MGALegResult>();
        public List<MGAEventItem> Events { get; set; } = new List<MGAEventItem>();
        public bool AllLegsFeasible { get; set; }
        public string FlybyChainSummary { get; set; }
        public string CentralBody { get; set; } = "Sun";

        // Departure Orbit Constraint State (模式 1: 发射前规划 vs 模式 2: 已在轨转移锁死)
        public MGADepartureMode DepartureMode { get; set; } = MGADepartureMode.PreLaunchFreeOrbit;
        public double RecommendedLaunchIncDeg { get; set; } = 0.0;
        public double RecommendedLaunchRaanDeg { get; set; } = 0.0;

        // Advanced Astrodynamics Results (Features 2 - 6)
        public PrimerVectorResult PrimerVector { get; set; }
        public BPlaneResult BPlane { get; set; }
        public TCMResult SuggestedTCM { get; set; }
        public LowEnergyManifoldResult ManifoldCapture { get; set; }
        public MEEResult DepartureMEE { get; set; }
        public SimsFlanaganResult LowThrustSolution { get; set; }
        public List<ParetoTradeOffPoint> ParetoFront { get; set; } = new List<ParetoTradeOffPoint>();

        // Parking Orbit Phase Alignment State (停泊轨道点火相位对齐状态)
        public bool IsPhaseAligned { get; set; } = false;
        public double PhaseAlignmentShiftSec { get; set; } = 0.0;
        public double OriginalDepartureUT { get; set; } = 0.0;
        public double OriginalDepartureDeltaVKms { get; set; } = 0.0;
        public double OriginalProgradeMs { get; set; } = 0.0;
        public double OriginalRadialMs { get; set; } = 0.0;
        public double OriginalNormalMs { get; set; } = 0.0;
        public double OriginalTotalDeltaVKms { get; set; } = 0.0;
        public double OriginalToFDays { get; set; } = 0.0;

        public void BuildEvents(MGAConfig config) {
            Events.Clear();
            if (Legs == null || Legs.Count == 0) return;

            int seq = 1;
            var leg0 = Legs[0];

            bool isCentralDep = (config != null && !string.IsNullOrEmpty(config.CentralBody) && 
                                leg0.FromBody.Equals(config.CentralBody, StringComparison.OrdinalIgnoreCase));
            string depTitle = isCentralDep ? $"{seq}: {leg0.FromBody} injection" : $"{seq}: {leg0.FromBody} escape";

            // 1. 出发逃逸/注入机动 (Origin Escape / Injection)
            Events.Add(new MGAEventItem {
                SequenceNumber = seq,
                Title = depTitle,
                EventType = MGAEventType.Escape,
                BodyName = leg0.FromBody,
                FromBody = leg0.FromBody,
                ToBody = leg0.ToBody,
                EpochUT = DepartureUT,
                PositionKm = leg0.DeparturePosKm,
                DeltaVMs = LaunchDeltaVKms * 1000.0,
                ProgradeMs = (!double.IsNaN(leg0.ProgradeMs) && Math.Abs(leg0.ProgradeMs) > 1e-4) ? leg0.ProgradeMs : (LaunchDeltaVKms * 1000.0),
                NormalMs = double.IsNaN(leg0.NormalMs) ? 0.0 : leg0.NormalMs,
                RadialMs = double.IsNaN(leg0.RadialMs) ? 0.0 : leg0.RadialMs,
                PhaseAngleDeg = double.IsNaN(leg0.PhaseAngleDeg) ? 0.0 : leg0.PhaseAngleDeg,
                EjectionAngleDeg = double.IsNaN(leg0.EjectionAngleDeg) ? 0.0 : leg0.EjectionAngleDeg,
                EjectionLANDeg = double.IsNaN(leg0.EjectionLANDeg) ? 0.0 : leg0.EjectionLANDeg,
                EjectionInclinationDeg = double.IsNaN(leg0.EjectionInclinationDeg) ? 0.0 : leg0.EjectionInclinationDeg,
                C3Kms2 = double.IsNaN(leg0.C3Kms2) ? 0.0 : leg0.C3Kms2,
                VInfOutKms = leg0.VInfDepartureKms.magnitude,
                IsFeasible = true
            });
            seq++;

            // 1.1 首段若存在 DSM (Deep Space Maneuver)
            if (leg0.HasDSM && leg0.DSM != null) {
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {leg0.FromBody}-{leg0.ToBody} DSM",
                    EventType = MGAEventType.DSM,
                    BodyName = leg0.FromBody,
                    FromBody = leg0.FromBody,
                    ToBody = leg0.ToBody,
                    EpochUT = leg0.DepartureUT + leg0.DSM.EpochOffsetSec,
                    PositionKm = leg0.DSMPositionKm,
                    DeltaVMs = leg0.DSM.DeltaVMs,
                    ProgradeMs = leg0.DSM.ProgradeMs,
                    NormalMs = leg0.DSM.NormalMs,
                    RadialMs = leg0.DSM.RadialMs,
                    IsFeasible = true
                });
                seq++;
            }

            // 2. 依次构建中间飞掠、推进机动与后续段 DSM
            for (int k = 1; k < Legs.Count; k++) {
                var leg = Legs[k];
                var prevLeg = Legs[k - 1];

                string flybyTitle = (leg.FlybyDeltaVKms > 0.001) ? 
                    $"{seq}: {leg.FromBody} powered flyby" : 
                    $"{seq}: {leg.FromBody} flyby";

                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = flybyTitle,
                    EventType = MGAEventType.Flyby,
                    BodyName = leg.FromBody,
                    FromBody = prevLeg.FromBody,
                    ToBody = leg.ToBody,
                    EpochUT = leg.DepartureUT,
                    PositionKm = leg.DeparturePosKm,
                    DeltaVMs = leg.FlybyDeltaVKms * 1000.0,
                    ProgradeMs = leg.ProgradeMs,
                    NormalMs = leg.NormalMs,
                    RadialMs = leg.RadialMs,
                    FlybyTurningAngleDeg = leg.FlybyTurningAngleDeg,
                    FlybyPeriapsisRadiusKm = leg.FlybyPeriapsisRadiusKm,
                    FlybyPeriapsisAltitudeKm = leg.FlybyPeriapsisAltitudeKm,
                    VInfInKms = prevLeg.VInfArrivalKms.magnitude,
                    VInfOutKms = leg.VInfDepartureKms.magnitude,
                    IsFeasible = leg.IsFeasible
                });
                seq++;

                // 后续段 DSM
                if (leg.HasDSM && leg.DSM != null) {
                    Events.Add(new MGAEventItem {
                        SequenceNumber = seq,
                        Title = $"{seq}: {leg.FromBody}-{leg.ToBody} DSM",
                        EventType = MGAEventType.DSM,
                        BodyName = leg.FromBody,
                        FromBody = leg.FromBody,
                        ToBody = leg.ToBody,
                        EpochUT = leg.DepartureUT + leg.DSM.EpochOffsetSec,
                        PositionKm = leg.DSMPositionKm,
                        DeltaVMs = leg.DSM.DeltaVMs,
                        ProgradeMs = leg.DSM.ProgradeMs,
                        NormalMs = leg.DSM.NormalMs,
                        RadialMs = leg.DSM.RadialMs,
                        IsFeasible = true
                    });
                    seq++;
                }
            }

            // 3. 目标天体到达 (Destination Arrival)
            var lastLeg = Legs.Last();
            if (config != null && config.NoInsertionBurn) {
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {lastLeg.ToBody} flyby",
                    EventType = MGAEventType.Flyby,
                    BodyName = lastLeg.ToBody,
                    FromBody = lastLeg.FromBody,
                    ToBody = lastLeg.ToBody,
                    EpochUT = ArrivalUT,
                    PositionKm = lastLeg.ArrivalPosKm,
                    DeltaVMs = 0.0,
                    VInfInKms = lastLeg.VInfArrivalKms.magnitude,
                    IsFeasible = true
                });
            } else {
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {lastLeg.ToBody} circularization",
                    EventType = MGAEventType.Circularization,
                    BodyName = lastLeg.ToBody,
                    FromBody = lastLeg.FromBody,
                    ToBody = lastLeg.ToBody,
                    EpochUT = ArrivalUT,
                    PositionKm = lastLeg.ArrivalPosKm,
                    DeltaVMs = InsertionDeltaVKms * 1000.0,
                    ProgradeMs = -InsertionDeltaVKms * 1000.0,
                    NormalMs = 0.0,
                    RadialMs = 0.0,
                    VInfInKms = lastLeg.VInfArrivalKms.magnitude,
                    InsertionAltitudeKm = (config != null) ? config.DestinationAltitudeKm : MGADefaults.DestinationAltitudeKm,
                    InsertionInclinationDeg = lastLeg.InsertionInclinationDeg,
                    IsFeasible = true
                });
            }
        }
    }

    /// <summary>
    /// 终端到达轨道物理模式 (与 GUI 下拉菜单无缝数据绑定)
    /// </summary>
    public enum MGAArrivalOrbitType
    {
        HighlyElliptic = 0,     // 科学高偏心率捕获椭圆 (外行星/巨行星默认标配)
        LowCircular = 1,        // 低圆轨道停泊 (内行星/环绕任务)
        Flyby = 2,              // 纯飞掠掠过 (零捕获机动，如旅行者、新视野)
        ParabolicThreshold = 3  // 临界抛物线捕获
    }

    /// <summary>
    /// 航天器推进系统物理类型 (解耦化学大脉冲与低推力离子电推进)
    /// </summary>
    public enum MGAPropulsionType
    {
        ChemicalImpulsive = 0,  // 化学推进瞬时大脉冲
        LowThrustElectric = 1   // 连续低推力离子电推进 (如黎明号 Dawn、深空1号 DS1)
    }

    /// <summary>
    /// MGA 规划配置输入 (100% 对齐 KSP-MGA-Planner 与高级动力学特性)
    /// </summary>
    public class MGAConfig {
        public MGAArrivalOrbitType ArrivalOrbitType { get; set; } = MGAArrivalOrbitType.HighlyElliptic;
        public bool AutoAdaptGiantPlanetEllipse { get; set; } = true;
        public MGAPropulsionType PropulsionType { get; set; } = MGAPropulsionType.ChemicalImpulsive;

        public string CentralBody { get; set; } = "Sun";
        public bool IsHeliocentric => string.IsNullOrEmpty(CentralBody) || 
                                      CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                                      CentralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) || 
                                      CentralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string TryGetFlightGlobalsBodyName() {
            try {
                if (FlightGlobals.ready && FlightGlobals.currentMainBody != null) {
                    return FlightGlobals.currentMainBody.name;
                }
            } catch { }
            return null;
        }

        private string _departureBody;
        public string DepartureBody {
            get {
                if (string.IsNullOrEmpty(_departureBody)) {
                    try {
                        _departureBody = TryGetFlightGlobalsBodyName();
                    } catch { }
                    if (string.IsNullOrEmpty(_departureBody)) {
                        _departureBody = (SolarSystemCatalog.GetPlanet("Earth") != null ? "Earth" : "Kerbin");
                    }
                }
                return _departureBody;
            }
            set {
                if (_departureBody != value) {
                    _departureBody = value;
                    if (!string.IsNullOrEmpty(value)) {
                        if (!LockDepartureInclination) {
                            DepartureInclinationDeg = MGADefaults.GetDefaultLaunchInclinationDeg(value);
                        }
                        double safeAlt = MGADefaults.GetSafeDepartureAltitudeKm(value);
                        if (Math.Abs(DeparturePeAKm - MGADefaults.FallbackDepartureAltitudeKm) < 1e-3 || DeparturePeAKm <= 0) {
                            DeparturePeAKm = safeAlt;
                            DepartureApAKm = safeAlt;
                        }
                    }
                }
            }
        }

        private string _destinationBody;
        public string DestinationBody {
            get {
                if (string.IsNullOrEmpty(_destinationBody)) {
                    if (_flybySequence != null && _flybySequence.Count > 0) {
                        return _flybySequence[_flybySequence.Count - 1];
                    }
                    if (!string.IsNullOrEmpty(CentralBody) && !CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase)) {
                        var sat = SolarSystemCatalog.GetMoonsOf(CentralBody);
                        if (sat != null && sat.Count > 0) return sat[0].Name;
                    }
                    _destinationBody = (SolarSystemCatalog.GetPlanet("Mars") != null ? "Mars" : "Duna");
                }
                return _destinationBody;
            }
            set {
                if (_destinationBody != value) {
                    _destinationBody = value;
                    if (!string.IsNullOrEmpty(value)) {
                        ApplyCaptureOrbitConstraints(forceResetIfInvalid: true);
                    }
                }
            }
        }

        private List<string> _flybySequence;
        public List<string> FlybySequence {
            get {
                if (_flybySequence == null || _flybySequence.Count == 0) {
                    string dest = !string.IsNullOrEmpty(_destinationBody)
                        ? _destinationBody
                        : (!string.IsNullOrEmpty(CentralBody) && !CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase)
                            ? (SolarSystemCatalog.GetMoonsOf(CentralBody)?.Count > 0 ? SolarSystemCatalog.GetMoonsOf(CentralBody)[0].Name : "Mars")
                            : (SolarSystemCatalog.GetPlanet("Mars") != null ? "Mars" : "Duna"));
                    _flybySequence = new List<string> { dest };
                }
                return _flybySequence;
            }
            set => _flybySequence = value;
        }
        
        public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive;
        public List<MGALegStrategy> LegStrategies { get; set; } = new List<MGALegStrategy>();
        public List<MGAFlybyStrategy> FlybyStrategies { get; set; } = new List<MGAFlybyStrategy>();

        private readonly object _syncLock = new object();
        public void SyncStrategiesWithChain(int legCount, List<string> fullChain = null) {
            lock (_syncLock) {
                MGALegStrategyManager.Synchronize(this, legCount, fullChain);
            }
        }

        public double DsmEtaDefault { get; set; } = MGADefaults.DsmEtaDefault;
        public List<double> DsmEtas { get; set; } = new List<double>();
        public List<double> FlybyRpKms { get; set; } = new List<double>();
        public List<double> FlybyBetasRad { get; set; } = new List<double>();

        public double LowThrustMaxThrustN { get; set; } = MGADefaults.LowThrustMaxThrustN;
        public double LowThrustIspSec { get; set; } = MGADefaults.LowThrustIspSec;
        public double SpacecraftWetMassKg { get; set; } = MGADefaults.SpacecraftWetMassKg;
        public int LowThrustSegments { get; set; } = MGADefaults.LowThrustSegments;
        public double TargetBDotTKm { get; set; } = 0.0;
        public double TargetBDotRKm { get; set; } = 0.0;

        public int MaxSwingBys { get; set; } = MGADefaults.MaxSwingBys;
        public int MaxResonances { get; set; } = MGADefaults.MaxResonances;
        public int MaxBackLegs { get; set; } = MGADefaults.MaxBackLegs;
        public int MaxBackSpacing { get; set; } = MGADefaults.MaxBackSpacing;
        public bool ConstrainByVesselDeltaV { get; set; } = false;
        public double VesselMaxDeltaVMs { get; set; } = 0.0;
        public double MaxDsmDeltaVMs { get; set; } = 0.0;
        public double LaunchDeltaVCeilingMs { get; set; } = 0.0;
        public MGAConstraintProfile ConstraintProfile { get; set; }
        public double DepartureToFMinFactor { get; set; } = 0.65;
        public double DepartureToFMaxFactor { get; set; } = 1.50;
        public double ResonantMaxOffsetDays { get; set; } = 0.0;
        public int LambertMaxRevs { get; set; } = 0;

        public double EarliestDepartureUT { get; set; }
        public double LatestDepartureUT { get; set; }
        public double SeedEpochUT { get; set; } = 0.0;
        public double SeedToFDays { get; set; } = 0.0;
        public double DepartureSearchDays {
            get => (LatestDepartureUT > EarliestDepartureUT) ? (LatestDepartureUT - EarliestDepartureUT) / MGADefaults.SecondsPerDay : (IsHeliocentric ? MGADefaults.InterplanetarySearchDays : MGADefaults.SystemSearchDays);
            set {
                if (value > 0) {
                    LatestDepartureUT = EarliestDepartureUT + value * MGADefaults.SecondsPerDay;
                }
            }
        }
        
        public MGADepartureMode DepartureMode { get; set; } = MGADepartureMode.PreLaunchFreeOrbit;
        public double DepartureAltitudeKm { get => DeparturePeAKm; set { DeparturePeAKm = value; DepartureApAKm = value; } }
        public double DeparturePeAKm { get; set; } = MGADefaults.DepartureAltitudeKm;
        public double DepartureApAKm { get; set; } = MGADefaults.DepartureAltitudeKm;
        public double DepartureInclinationDeg { get; set; } = MGADefaults.DefaultEarthInclinationDeg;
        public double DepartureRAANDeg { get; set; } = 0.0;
        public double DepartureAOPDeg { get; set; } = 0.0;
        public double DepartureEccentricity { get; set; } = 0.0;
        public bool LockDepartureInclination { get; set; } = false;
        public bool LockDepartureRAAN { get; set; } = false;

        public double DestinationAltitudeKm { get => DestinationPeAKm; set { DestinationPeAKm = value; DestinationApAKm = value; } }
        public double DestinationPeAKm { get; set; } = MGADefaults.DestinationAltitudeKm;
        public double DestinationApAKm { get; set; } = MGADefaults.DestinationAltitudeKm;
        public bool NoInsertionBurn { get; set; } = false;
        public bool EnableLowEnergyManifoldCapture { get; set; } = false; // 多体引力流形低能捕获与弱稳定性边界优化开关 (默认关闭，待定)

        /// <summary>
        /// 基于天体引力影响球 (SOI) 与大气顶层边界，自动应用物理刚性约束
        /// </summary>
        public void ApplyCaptureOrbitConstraints(bool forceResetIfInvalid = false) {
            string dest = DestinationBody;
            if (string.IsNullOrEmpty(dest)) return;
            MGADefaults.GetBodyCaptureAltitudeBounds(dest, out double minPeA, out double recPeA, out double maxApA);
            if (forceResetIfInvalid || DestinationPeAKm < minPeA || DestinationPeAKm > maxApA || DestinationPeAKm <= 0) {
                DestinationPeAKm = recPeA;
            }

            var destP = SolarSystemCatalog.GetPlanet(dest);
            bool isGiant = destP != null && (destP.Mu >= 5e6 || destP.RadiusKm >= 20000.0);

            if (ArrivalOrbitType == MGAArrivalOrbitType.Flyby || NoInsertionBurn) {
                // 飞掠模式：零制动
                DestinationApAKm = DestinationPeAKm;
            } else if (ArrivalOrbitType == MGAArrivalOrbitType.HighlyElliptic || (AutoAdaptGiantPlanetEllipse && isGiant)) {
                // 科学高偏心率捕获椭圆: 严格基于天体力学拉普拉斯引力球与 Hill 稳定界限推导 (ra = min(0.20 * SOI, 100 * rp))
                if (forceResetIfInvalid || DestinationApAKm <= DestinationPeAKm) {
                    double destR = destP != null ? destP.RadiusKm : 6000.0;
                    double rp = destR + DestinationPeAKm;
                    double soiKm = (destP != null && destP.SoiKm > 0) ? destP.SoiKm : destR * 100.0;
                    double ra = Math.Min(soiKm * 0.20, rp * 100.0);
                    DestinationApAKm = Math.Min(maxApA, Math.Round(ra - destR));
                }
            } else {
                if (forceResetIfInvalid || DestinationApAKm < DestinationPeAKm) {
                    DestinationApAKm = DestinationPeAKm;
                } else if (DestinationApAKm > maxApA) {
                    DestinationApAKm = maxApA;
                }
            }
        }

        public void ApplyDepartureOrbitConstraints(bool forceResetIfInvalid = false) {
            string dep = DepartureBody;
            if (string.IsNullOrEmpty(dep)) return;
            MGADefaults.GetBodyCaptureAltitudeBounds(dep, out double minPeA, out double recPeA, out double maxApA);
            if (forceResetIfInvalid || DeparturePeAKm < minPeA || DeparturePeAKm > maxApA || DeparturePeAKm <= 0) {
                DeparturePeAKm = recPeA;
            }
            if (forceResetIfInvalid || DepartureApAKm < DeparturePeAKm) {
                DepartureApAKm = DeparturePeAKm;
            } else if (DepartureApAKm > maxApA) {
                DepartureApAKm = maxApA;
            }
        }

        public double MaxDurationDays { get; set; } = MGADefaults.MaxDurationDays;
        public bool EnableMaxDuration { get; set; } = false;

        public List<double> MinToFDays { get; set; } = new List<double>();
        public List<double> MaxToFDays { get; set; } = new List<double>();
        public int PopulationSize { get; set; } = MGADefaults.PopulationSize;
        public int MaxGenerations { get; set; } = MGADefaults.MaxGenerations;
        public bool AllowPoweredFlyby { get; set; } = true;
        public bool EnableParkingOrbitPhaseAlignment { get; set; } = true;

        public MGAConfig Clone() {
            var copy = (MGAConfig)this.MemberwiseClone();
            if (this._flybySequence != null) copy._flybySequence = new List<string>(this._flybySequence);
            if (this.MinToFDays != null) copy.MinToFDays = new List<double>(this.MinToFDays);
            if (this.MaxToFDays != null) copy.MaxToFDays = new List<double>(this.MaxToFDays);
            if (this.LegStrategies != null) copy.LegStrategies = new List<MGALegStrategy>(this.LegStrategies);
            if (this.FlybyStrategies != null) copy.FlybyStrategies = new List<MGAFlybyStrategy>(this.FlybyStrategies);
            if (this.DsmEtas != null) copy.DsmEtas = new List<double>(this.DsmEtas);
            if (this.FlybyRpKms != null) copy.FlybyRpKms = new List<double>(this.FlybyRpKms);
            if (this.FlybyBetasRad != null) copy.FlybyBetasRad = new List<double>(this.FlybyBetasRad);
            return copy;
        }
    }

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
        public int ScrubberStep { get; set; } = 0;
        public float ScrubberProgress { get; set; } = 0f;
        public float TargetProgress { get; set; } = 0f;
        public bool IsPlayingAnimation { get; set; } = false;
        public float PlaySpeed { get; set; } = 1.0f;
        public bool IsDraggingCanvas { get; set; } = false;
        public bool IsRotatingCanvas { get; set; } = false;
        public Vector2 LastMouseCanvasPos { get; set; } = Vector2.zero;
    }

    /// <summary>
    /// MGA 全局 UI 交互、滚动条与模态框状态管理模型 (Single Source of Truth)
    /// 集中管理原本散落在 UI 类中的各种临时弹窗、滚动条与交互标记，彻底杜绝散落状态
    /// </summary>
    [Serializable]
    public class MGAUIInteractionState {
        // 1. 面板滚动条集中管理
        public Vector2 LeftScroll { get; set; } = Vector2.zero;
        public Vector2 TabScroll { get; set; } = Vector2.zero;
        public Vector2 SolutionScroll { get; set; } = Vector2.zero;
        public Vector2 ReportScrollPos { get; set; } = Vector2.zero;
        public bool ShowReportModal { get; set; } = false;
        public bool ShowChainSuggestions { get; set; } = false;

        // 2. 行星际天体弹出选择器 (Planet Picker Modal) 状态
        public bool ShowPlanetPickerModal { get; set; } = false;
        public Rect PlanetPickerWindowRect { get; set; } = new Rect(250, 100, 380, 520);
        public int PlanetPickerTargetType { get; set; } = 0; // 0: Departure, 1: Destination, 2: ReplaceFlyby, 3: AppendFlyby, 4: SequenceStepBody
        public int PlanetPickerFlybyIndex { get; set; } = -1;
        public string PlanetPickerTitle { get; set; } = "";
        public Vector2 PlanetPickerScrollPos { get; set; } = Vector2.zero;
        public string PlanetPickerSearch { get; set; } = "";

        // 3. 航段高精遥测明细弹窗 (Flight Telemetry Details) 状态
        public bool ShowTelemetryModal { get; set; } = false;
        public Rect TelemetryWindowRect { get; set; } = new Rect(300, 120, 540, 480);
        public int TelemetrySelectedStepIndex { get; set; } = 0;
        public Vector2 TelemetryScrollPos { get; set; } = Vector2.zero;

        // 4. 方案全量动力学详情弹窗 (Solution Detail Modal) 状态
        public bool ShowSolutionDetailModal { get; set; } = false;
        public MGASolution DetailModalSolution { get; set; } = null;
        public Rect SolutionDetailWindowRect { get; set; } = new Rect(280, 80, 720, 560);
        public Vector2 SolutionDetailScrollPos { get; set; } = Vector2.zero;

        // 5. 历史任务与统一配置库弹窗 (History Mission Picker) 状态
        public bool ShowHistoryMissionPickerModal { get; set; } = false;
        public Rect HistoryMissionPickerWindowRect { get; set; } = new Rect(260, 90, 680, 520);
        public string HistoryMissionSearch { get; set; } = "";
        public string HistoryMissionCategory { get; set; } = "全部";
        public Vector2 HistoryMissionScrollPos { get; set; } = Vector2.zero;

        // 6. 任务序列工作台拖拽重排与交互状态
        public int SeqDraggedIndex { get; set; } = -1;
        public int SeqStepToMoveUp { get; set; } = -1;
        public int SeqStepToMoveDown { get; set; } = -1;
        public int SeqStepToDelete { get; set; } = -1;
        public int SeqHoveredIndex { get; set; } = -1;
        public int SeqNextHoveredIndex { get; set; } = -1;
    }

    /// <summary>
    /// 3D 视口实时遥测与航天动力学几何追踪数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public struct CraftTelemetry {
        public bool HasCraft;
        public bool IsScrubbing;
        public double MissionUt;
        public Vector3d PositionKm;
        public Vector3d VelocityKmS;
        public double SpeedKmS;
        public double DistanceAu;
        public double DistanceKm;
        public int ActiveLegIndex;
        public int TotalLegs;
        public string FromBody;
        public string ToBody;
        public double MetDays;
        public double TotalFlightDays;
        public string CentralBody;
        public bool IsInterplanetary;
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
        public bool show_inline_telemetry = false;

        // 天体与轨道几何
        public string body_name = "Earth";
        public double periapsis_alt_km = MGADefaults.DepartureAltitudeKm;
        public double apoapsis_alt_km = MGADefaults.DepartureAltitudeKm;

        // 历元与时间 (双历元系统)
        public double epoch_ut = 0.0;
        public string epoch_utc_str = "";
        public double tof_days = 150.0;
        public double min_tof_days = MGADefaults.DefaultMinToFDays;
        public double max_tof_days = MGADefaults.DefaultMaxToFDays;

        // 机动与策略
        public MGALegStrategy strategy = MGALegStrategy.Ballistic;
        public MGAFlybyStrategy flyby_strategy = MGAFlybyStrategy.UnpoweredFlyby;
        public double dv_v = 0.0;
        public double dv_n = 0.0;
        public double dv_b = 0.0;
        public double dsm_eta = MGADefaults.DsmEtaDefault;
        public double target_b_dot_t_km = 0.0;
        public double target_b_dot_r_km = 0.0;
        public double incoming_dha_deg = 0.0;
        public double incoming_dec_deg = 0.0;
        public double flyby_rp_km = 0.0;
        public double flyby_turn_deg = 0.0;
        public double v_inf_kms = 0.0;

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
