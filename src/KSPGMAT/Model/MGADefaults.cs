using System;
using KSPGMAT.MGA;

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

        public const double MaxPlausibleTotalDvKms = 80.0; // 物理合理总 ΔV 上界 (km/s): 过滤动力学发散解 (多航段如 BepiColombo 10段任务允许中间态 < 80 km/s)
    }
}
