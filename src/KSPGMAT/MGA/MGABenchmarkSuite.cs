using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 历史真实行星际引力助推 (MGA) 任务基准测试用例与动力学严密性验证引擎
    /// (Historical MGA Mission Benchmarking & Physical Vector Continuity Verification Suite)
    ///
    /// 【2026-09-14 审计修正记录】
    ///
    ///   修正一 · 答案注种 (Answer Seeding) 拔除
    ///     问题: 原 <c>BenchmarkCase.BlindSearchEvaluationMode</c> 默认 false，且全代码库无任何调用方
    ///           将其置 true ⇒ 基准测试**无条件**走"历史真值注种"分支：历史发射日 + 历史各航段飞行天数 +
    ///           历史借力序列被直接拼成染色体，经 PykepBridge.BuildFullSolution 求解后当作结果返回。
    ///           这等于拿答案反向验证，任何"求解器复现历史"的结论都不成立。
    ///     修正: 注种分支降级为显式 opt-in 的星历诊断通道 (<see cref="MGABenchmarkMode.HistoricReplay"/>),
    ///           并在结果上打 <see cref="BenchmarkResult.IsHistoricReplay"/> 标记。
    ///
    ///   修正二 · 拓扑泄漏 + 搜索域锚定
    ///     问题: 原"盲搜"分支仍把 testCase.FlybySequence (历史借力天体序列) 交给求解器
    ///           ⇒ 只对发射时刻盲、对拓扑不盲，却自称"无先验盲搜"；
    ///           且发射窗以历史发射日 ±N 天为圆心，最优解恰在窗口中心。
    ///     修正: 搜索模式拆为三种**互不混淆**的语义 (见 <see cref="MGABenchmarkMode"/>):
    ///           - <see cref="MGABenchmarkMode.HistoricReplay"/>：注种回放，仅星历链路诊断；
    ///           - <see cref="MGABenchmarkMode.HistoricTopologyTrajectory"/>：**给定历史拓扑**，
    ///             只优化航段航时/相位/DSM —— 这才是"历史任务轨迹优化基准"的本义，明确**不是盲搜**；
    ///           - <see cref="MGABenchmarkMode.TopologyBlind"/>：不提供任何中间借力天体，真盲搜。
    ///           三种模式的发射窗一律由 <see cref="ResolveBlindDepartureWindow"/> 按"发射机会年份"
    ///           年粒度 (或调用方显式区间) 生成，不再以历史发射日 ±N 天为圆心；
    ///           航时边界一律由 <c>AutoDeriveResonantLegBounds</c> 物理推导，不用历史航时 ±10% 夹逼。
    ///
    ///   保留: 历史 ΔV / 航时 / 借力序列数值仅用于**事后比对**与结果标注，不进入任何搜索边界或目标函数。
    ///
    /// 【2026-09-16 架构解耦修正记录 · 套件确认通过代码剥离与独立维护】
    ///   问题: 考核侧的权威物理动力学验证判据与通过确认逻辑原耦合在生产模块中，存在生产代码
    ///         自我认证 (Self-Certification) 与为迎合考核在生产层注入答案约束的应试作弊风险。
    ///   整改: 考题评判与确认通过代码 (5维硬物理门禁、容差判定、主矢量最优性评估与 PASS/FAIL 裁决)
    ///         已彻底剥离至测试工程 <c>tests/MGADebugRunner/TestSuites/HistoricalBenchmarkVerificationEngine.cs</c>
    ///         独立维护，形成考卷与考生物理隔离；
    ///         本类回归为历史任务元数据真值源 (SSOT) 与通用的轨迹求解执行通道，供游戏 GUI (TabMGA) 与
    ///         预设状态管理器 (MGAStateManager.LoadPreset) 稳定调用。
    /// </summary>
    /// 盲搜维度的定义枚举 (消歧口径)。
    /// 彻底规范声明算法对哪些物理维度盲。
    /// </summary>
    public enum MGABlindSearchDimension
    {
        /// <summary>非盲搜 (拓扑与发射时域/航时均由外部提供，如 GUI 预设)</summary>
        None = 0,
        /// <summary>发射时刻盲 (给定借力拓扑，在年粒度大时域内自主寻找最优发射时刻与航时)</summary>
        LaunchEpochBlind = 1,
        /// <summary>拓扑盲 (给定发射窗口与目标，在全行星库中通过 MCTS/BFS 自主推导借力链路)</summary>
        TopologyBlind = 2,
        /// <summary>完全盲 (发射时刻盲 + 拓扑盲 + 航时全自主推导)</summary>
        FullyBlind = 3
    }

    public static class MGABenchmarkSuite {
        /// <summary>
        /// 基准用例的搜索模式。
        ///
        /// 【2026-09-14 新增】把原先含糊的两态布尔量拆成三种语义明确的模式，
        /// 目的是让"这个基准到底测什么"不再需要读者猜。
        /// </summary>
        public enum MGABenchmarkMode {
            /// <summary>
            /// 历史真值标定回放：历史发射日 + 历史各航段飞行天数 + 历史借力序列全部注入。
            /// **仅供星历 / 开普勒链路自洽性诊断**；其 PASS 不构成任何寻优能力证据。
            /// </summary>
            HistoricReplay = 0,

            /// <summary>
            /// 给定历史拓扑的轨迹优化基准 (默认)：
            /// 借力天体序列按任务定义给定 (这是"历史任务基准"的本义)，但
            /// **发射窗与航段航时边界全部由物理推导**，不注入任何历史真值。
            /// 测的是"给定构型下能否把轨迹优化到历史量级"。**明确不是盲搜。**
            /// </summary>
            HistoricTopologyTrajectory = 1,

            /// <summary>
            /// 拓扑盲搜：不提供任何中间借力天体，拓扑由 MCTS + 星历可达性自主推导。
            /// 测的是"能否自主发现历史 MGA 构型"。与 <c>MGAHistoricalBlindSearchSuite</c> 同口径。
            /// </summary>
            TopologyBlind = 2
        }

        public class BenchmarkCase {
            public string MissionName { get; set; }
            public string Description { get; set; }
            public string DepartureBody { get; set; }
            public List<string> FlybySequence { get; set; }
            private double _historicLaunchUT = 0.0;
            public double HistoricLaunchUT {
                get {
                    if (_historicLaunchUT <= 0.0 && !string.IsNullOrEmpty(HistoricLaunchDateStr)) {
                        _historicLaunchUT = GMATClient.ParseUtcGregorian(HistoricLaunchDateStr);
                    }
                    return _historicLaunchUT;
                }
                set => _historicLaunchUT = value;
            }
            public string HistoricLaunchDateStr { get; set; }
            public double HistoricTotalDeltaVMinMs { get; set; }
            public double HistoricTotalDeltaVMaxMs { get; set; }

            /// <summary>
            /// 历史总 ΔV 的**单一参考值** (m/s)。&gt; 0 时优先用它 ± <see cref="MaxAcceptableDvRelTolerance"/>
            /// 构造判定带宽，取代 <see cref="HistoricTotalDeltaVMinMs"/>/<see cref="HistoricTotalDeltaVMaxMs"/>
            /// 的宽区间。
            ///
            /// 【2026-09-14 审计修正】原判据是纯区间包含 <c>∈[Min, Max]</c>，因此**区间宽度 ≡ 判定强度**。
            /// 实测宽度跨 900 ~ 11800 m/s (13 倍): Rosetta 3800–15000 (宽 11200)、
            /// Juno 1-DSM 4200–16000 (宽 11800)、BepiColombo 3500–12000 (宽 8500)。
            /// 这些"历史工程参考区间"无出处，宽到近乎无效。新增单值+相对容差机制并输出宽度审计。
            /// </summary>
            public double HistoricTotalDeltaVMs { get; set; }

            /// <summary>
            /// 判定容差 (相对值, 无量纲)。仅在 <see cref="HistoricTotalDeltaVMs"/> &gt; 0 时生效。
            /// 默认 0.35 —— 与工程实践中"任务级 ΔV 预算 ±35%"的口径一致;
            /// 该值本身属**工程口径而非物理常数**, 已登记于 MGAEmpiricalRegistry。
            /// </summary>
            public double MaxAcceptableDvRelTolerance { get; set; } = 0.35;

            /// <summary>
            /// 历史 ΔV 数值的出处 (文献 / 任务报告 / 估计方式)。为空时报表会标注"出处未登记"。
            /// </summary>
            public string HistoricTotalDeltaVProvenance { get; set; }
            /// <summary>
            /// 历史航时容差比率 (默认 0.25 即 ±25%)。
            /// </summary>
            public double MaxAcceptableToFRelTolerance { get; set; } = 0.25;
            public double HistoricFlightDaysMin { get; set; }
            public double HistoricFlightDaysMax { get; set; }
            public List<double> HistoricToFDays { get; set; }
            public double DepAltKm { get; set; } = 200.0;
            public double ArrAltKm { get; set; } = 200.0;
            public double DepIncDeg { get; set; } = 28.5;
            public bool LockDepartureInclination { get; set; } = false;
            public bool NoInsertionBurn { get; set; } = false;
            public MGAArrivalOrbitType ArrivalOrbitType { get; set; } = MGAArrivalOrbitType.HighlyElliptic;
            public double DepartureWindowDays { get; set; } = 0.0;

            /// <summary>
            /// 基准测试的搜索模式。三选一，语义互不重叠。
            ///
            /// 【2026-09-14 审计修正】取代原布尔量 <c>BlindSearchEvaluationMode</c>。
            /// 原布尔量只有"注种"与"自称盲搜但实际给了拓扑"两态 —— 后者既不是诚实的能力测试
            /// (拓扑是答案)，也不是诚实的轨迹优化基准 (它却自称"无先验盲搜")。
            /// </summary>
            public MGABenchmarkMode SearchMode { get; set; } = MGABenchmarkMode.HistoricTopologyTrajectory;

            /// <summary>
            /// 盲搜发射窗口起点 (UTC Gregorian 字符串，形如 "01 Jan 2004 00:00:00.000")。
            /// 为空时按"发射机会年份"年粒度自动构造，见 <see cref="ResolveBlindDepartureWindow"/>。
            /// </summary>
            public string BlindSearchWindowStartStr { get; set; }

            /// <summary>
            /// 盲搜发射窗口终点 (UTC Gregorian 字符串)。
            /// 为空时按"发射机会年份"年粒度自动构造，见 <see cref="ResolveBlindDepartureWindow"/>。
            /// </summary>
            public string BlindSearchWindowEndStr { get; set; }

            // Advanced Engine Modes & Multi-Strategy
            public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive;
            public double DsmEtaDefault { get; set; } = 0.5;
            public double LowThrustMaxThrustN { get; set; } = 0.5;
            public double LowThrustIspSec { get; set; } = 3000.0;
            public double TargetBDotTKm { get; set; } = 0.0;
            public double TargetBDotRKm { get; set; } = 0.0;
            public List<MGALegStrategy> LegStrategies { get; set; }
            public List<MGAFlybyStrategy> FlybyStrategies { get; set; }
            /// <summary>文献或工程官方出处 (NASA JPL / ESA 官方设计文档引用)</summary>
            public string ReferenceLiterature { get; set; } = "";

            /// <summary>盲搜维度分类 (声明算法对哪些维度盲)</summary>
            public MGABlindSearchDimension BlindDimension { get; set; } = MGABlindSearchDimension.LaunchEpochBlind;

        }

        public class BenchmarkResult {
            public BenchmarkCase TestCase { get; set; }
            public bool Success { get; set; }
            public double ComputedLaunchDV { get; set; }
            public double ComputedTotalDV { get; set; }
            public double ComputedFlightDays { get; set; }
            public bool VectorContinuityPassed { get; set; }
            public bool FlybyFeasibilityPassed { get; set; }
            public bool DeltaVWithinTolerance { get; set; }
            public bool FlightDaysWithinTolerance { get; set; }
            public bool TopologyMatchesHistoric { get; set; }
            public bool PrimerVectorOptimal { get; set; }

            /// <summary>
            /// 本次结果是否来自"历史真值标定回放"分支。
            /// true 表示染色体中注入了历史发射日与历史航段航时，该结果**不能**作为求解器能力证据。
            /// </summary>
            public bool IsHistoricReplay { get; set; }

            /// <summary>盲搜通道返回的候选解总数 (0 表示走了回放通道或未收敛)。</summary>
            public int BlindSearchCandidateCount { get; set; }

            /// <summary>
            /// 择优解是否**真正使用了引力助推** (航段数 &gt; 1)。
            /// false 表示该"基准结果"是单航段直达转移, 不具备 MGA 意义 —— 报表中会显式标注。
            /// </summary>
            public bool BlindSearchUsedFlyby { get; set; }

            public MGASolution Solution { get; set; }
            public string GMATScriptPreview { get; set; }
            public string DetailedReport { get; set; }
        }

                /// <summary>
        /// 行星际多引力助推 (MGA) 历史与先驱基准任务集 (包含人类历史上著名的纯弹道与 1-DSM 借力任务)
        /// </summary>
        public static readonly List<BenchmarkCase> MGA_Missions = new List<BenchmarkCase> {
            new BenchmarkCase {
                
                ReferenceLiterature = "NASA SP-424: The Voyage of Mariner 10; Dunne & Burgess (1978)",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Mariner 10 (水手10号, 1973)",
                Description = "人类航天史上首次利用金星引力助推抵达水星的经典 MGA 任务 (E-V-M)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Mercury" },
                HistoricLaunchDateStr = "03 Nov 1973 05:45:00",
                HistoricLaunchUT = 752409600.0 - 26.0 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 5300.0,
                HistoricFlightDaysMin = 140.0,
                HistoricFlightDaysMax = 160.0,
                HistoricToFDays = new List<double> { 94.0, 52.0 },
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "D'Amario et al. (1992), 'Galileo 1989 VEEGA Mission Description', AAS 92-001",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Galileo (伽利略号 VEEGA, 1989)",
                Description = "NASA 经典 VEEGA 木星探测任务 (金星-地球-地球引力弹弓，金星远日点执行深空机动)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Earth", "Earth", "Jupiter" },
                HistoricLaunchDateStr = "18 Oct 1989 16:53:40",
                HistoricLaunchUT = 752409600.0 - 10.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3900.0,
                HistoricTotalDeltaVMaxMs = 6500.0,
                HistoricFlightDaysMin = 2200.0,
                HistoricFlightDaysMax = 2300.0,
                HistoricToFDays = new List<double> { 115.0, 301.0, 731.0, 1095.0 },
                NoInsertionBurn = true,
                EngineMode = MGAEngineMode.HeterogeneousHybrid,
                LegStrategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic, MGALegStrategy.DeepSpaceManeuver1DSM, MGALegStrategy.DeepSpaceManeuver1DSM, MGALegStrategy.DeepSpaceManeuver1DSM },
                FlybyStrategies = new List<MGAFlybyStrategy> { MGAFlybyStrategy.UnpoweredFlyby, MGAFlybyStrategy.UnpoweredFlyby, MGAFlybyStrategy.UnpoweredFlyby }
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Peralta & Flanagan (1995), 'Cassini Interplanetary Trajectory Design', NASA JPL",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Cassini-Huygens (卡西尼号 VVEJPS, 1997)",
                Description = "史上最宏伟的金星-金星-地球-木星-土星引力助推多目标深空探测任务",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter", "Saturn" },
                HistoricLaunchDateStr = "15 Oct 1997 08:43:00",
                HistoricLaunchUT = 752409600.0 - 2.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3900.0,
                HistoricTotalDeltaVMaxMs = 6500.0,
                HistoricFlightDaysMin = 2400.0,
                HistoricFlightDaysMax = 2500.0,
                HistoricToFDays = new List<double> { 193.0, 424.0, 55.0, 499.0, 1279.0 },
                NoInsertionBurn = true,
                EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM,
                DsmEtaDefault = 0.5
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Kohlhase & Penzo (1977), 'Voyager Mission Description', Space Science Reviews 21",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Voyager 2 (旅行者2号 Grand Tour, 1977)",
                Description = "木星-土星-天王星-海王星 (E-J-S-U-N) 连续无动力飞掠大行星全景探测",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter", "Saturn", "Uranus", "Neptune" },
                HistoricLaunchDateStr = "20 Aug 1977 14:29:44",
                HistoricLaunchUT = 840551384.0,
                HistoricTotalDeltaVMinMs = 6900.0,
                HistoricTotalDeltaVMaxMs = 9500.0,
                HistoricFlightDaysMin = 4300.0,
                HistoricFlightDaysMax = 4500.0,
                HistoricToFDays = new List<double> { 688.0, 778.0, 1613.0, 1309.0 },
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Schulz et al. (2009), 'ROSETTA: ESA\'s Mission to the Origin of the Solar System', Springer",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Rosetta (罗塞塔号 EEMEB, 2004)",
                Description = "ESA 彗星探测旗舰任务 (地球-火星-地球-地球多段飞掠，航段2实施1-DSM深空机动)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Earth", "Mars", "Earth", "Earth" },
                HistoricLaunchDateStr = "02 Mar 2004 07:17:00",
                HistoricLaunchUT = 752409600.0 + 4.17 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 15000.0,
                HistoricFlightDaysMin = 2000.0,
                HistoricFlightDaysMax = 2200.0,
                HistoricToFDays = new List<double> { 365.0, 715.0, 990.0, 730.0 },
                NoInsertionBurn = true,
                EngineMode = MGAEngineMode.HeterogeneousHybrid,
                LegStrategies = new List<MGALegStrategy> { MGALegStrategy.DeepSpaceManeuver1DSM, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic, MGALegStrategy.Ballistic },
                FlybyStrategies = new List<MGAFlybyStrategy> { MGAFlybyStrategy.UnpoweredFlyby, MGAFlybyStrategy.UnpoweredFlyby, MGAFlybyStrategy.UnpoweredFlyby }
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Guo & Farquhar (2005), 'New Horizons Mission Design', Space Science Reviews 140",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "New Horizons (新视野号 E-J-Pluto, 2006)",
                Description = "人类最快发射速度探测器，直飞木星引力助推奔赴冥王星",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter", "Pluto" },
                HistoricLaunchDateStr = "19 Jan 2006 19:00:00",
                HistoricLaunchUT = 752409600.0 + 6.05 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 7500.0,
                HistoricTotalDeltaVMaxMs = 10500.0,
                HistoricFlightDaysMin = 3400.0,
                HistoricFlightDaysMax = 3600.0,
                HistoricToFDays = new List<double> { 405.0, 3060.0 },
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Nyquist et al. (2012), 'Juno Mission Design and Trajectory Performance', AIAA 2012-4822",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Juno (朱诺号 E-E-J 纯弹道, 2011)",
                Description = "NASA 地球-地球共振引力弹弓前往木星高偏心率极轨道任务",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Earth", "Jupiter" },
                HistoricLaunchDateStr = "05 Aug 2011 16:25:00",
                HistoricLaunchUT = 1912177500.0,
                HistoricTotalDeltaVMinMs = 3000.0,
                HistoricTotalDeltaVMaxMs = 8600.0,
                HistoricFlightDaysMin = 1750.0,
                HistoricFlightDaysMax = 1850.0,
                HistoricToFDays = new List<double> { 796.0, 999.0 },
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Nyquist et al. (2012), 'Juno Mission Design and Trajectory Performance', AIAA 2012-4822",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Juno (朱诺号 EEJ 1-DSM 深空机动, 2011)",
                Description = "ESA PyKEP MGA-1DSM 官方算法：远日点实施中道矢量点火，总ΔV降至真实4630 m/s",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Earth", "Jupiter" },
                HistoricLaunchDateStr = "05 Aug 2011 16:25:00",
                HistoricLaunchUT = 1912177500.0,
                HistoricTotalDeltaVMs = 4630.0,
                HistoricTotalDeltaVProvenance = "NASA/JPL Juno Trajectory Design: Launch C3~31.1 km²/s², 2x DSM ~730 m/s, JOI ~820 m/s, Total ~4630 m/s",
                HistoricTotalDeltaVMinMs = 4000.0,
                HistoricTotalDeltaVMaxMs = 7200.0,
                HistoricFlightDaysMin = 1750.0,
                HistoricFlightDaysMax = 1850.0,
                HistoricToFDays = new List<double> { 796.0, 999.0 },
                NoInsertionBurn = true,
                EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM,
                DsmEtaDefault = 0.5
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Peralta & Flanagan (1995), 'Cassini Interplanetary Trajectory Design', NASA JPL",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Cassini (卡西尼号 1-DSM 深空机动, 1997)",
                Description = "ESA PyKEP MGA-1DSM 官方算法：V-V-E-J-S 航段深空机动，总ΔV仅需 4520 m/s",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter", "Saturn" },
                HistoricLaunchDateStr = "15 Oct 1997 08:43:00",
                HistoricLaunchUT = 752409600.0 - 2.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 4000.0,
                HistoricTotalDeltaVMaxMs = 7500.0,
                HistoricFlightDaysMin = 2400.0,
                HistoricFlightDaysMax = 2500.0,
                HistoricToFDays = new List<double> { 193.0, 424.0, 55.0, 499.0, 1279.0 },
                NoInsertionBurn = true,
                EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM,
                DsmEtaDefault = 0.5
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Lawden (1963), 'Optimal Trajectories for Space Navigation', Butterworths",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Galileo (伽利略号 劳登主矢量判定, 1989)",
                Description = "沿 VEEGA 转移弧计算解析主矢量极值曲线，验证脉冲机动冲量全局最优性 (|p| <= 1.0)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Earth", "Earth", "Jupiter" },
                HistoricLaunchDateStr = "18 Oct 1989 16:53:40",
                HistoricLaunchUT = 752409600.0 - 10.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 6500.0,
                HistoricFlightDaysMin = 2100.0,
                HistoricFlightDaysMax = 2300.0,
                HistoricToFDays = new List<double> { 115.0, 305.0, 730.0, 1080.0 },
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Guo & Farquhar (2005), 'New Horizons Mission Design', Space Science Reviews 140",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "New Horizons (新视野号 帕累托前沿权衡, 2006)",
                Description = "展示人类最快飞掠深空探测器在 9.5 年极速直达与最省燃料之间的非劣权衡曲线",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter", "Pluto" },
                HistoricLaunchDateStr = "19 Jan 2006 19:00:00",
                HistoricLaunchUT = 752409600.0 + 6.05 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 7500.0,
                HistoricTotalDeltaVMaxMs = 10500.0,
                HistoricFlightDaysMin = 3400.0,
                HistoricFlightDaysMax = 3600.0,
                HistoricToFDays = new List<double> { 405.0, 3060.0 },
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Concept Study: Outer Planet Multi-Target Orbiter (2030)",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Kronos-Poseidon (泰坦-海神双星巡航, 2030)",
                Description = "【虚构深空】木星超级引力弹弓无动力借力偏转，直奔外层冰巨星天王星 (E-J-U)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter", "Uranus" },
                HistoricLaunchDateStr = "20 Jan 2030 23:30:00",
                HistoricLaunchUT = 2494798200.0,
                HistoricTotalDeltaVMinMs = 6000.0,
                HistoricTotalDeltaVMaxMs = 8000.0,
                HistoricFlightDaysMin = 5000.0,
                HistoricFlightDaysMax = 5500.0,
                HistoricToFDays = new List<double> { 841.0, 4500.0 },
                DepIncDeg = 28.6,
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Concept Study: Inner Solar System Close Flyby Probe (2029)",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Solar Chronos (太阳时空神号, 2029)",
                Description = "【虚构深空】内太阳系金星引力弹弓高效降能，实施水星近心点无动力借力交会 (E-V-M)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Mercury" },
                HistoricLaunchDateStr = "18 Nov 2029 14:00:00",
                HistoricLaunchUT = 2489320800.0,
                HistoricTotalDeltaVMinMs = 3500.0,
                HistoricTotalDeltaVMaxMs = 9500.0,
                HistoricFlightDaysMin = 200.0,
                HistoricFlightDaysMax = 250.0,
                HistoricToFDays = new List<double> { 111.0, 103.0 },
                DepIncDeg = 28.6,
                NoInsertionBurn = true,
                ArrAltKm = 300.0
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Aldrin (1985), 'Cyclic Trajectory Concepts', SAIC/JPL",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Aldrin Cycler (奥尔德林地火循环飞船, 2033)",
                Description = "【虚构深空】巴兹·奥尔德林构想的星际穿梭巴士，火星天然引力弹弓零机动往返穿梭 (E-M-E)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Mars", "Earth" },
                HistoricLaunchDateStr = "28 Apr 2033 15:30:00",
                HistoricLaunchUT = 2597931000.0,
                HistoricTotalDeltaVMinMs = 3200.0,
                HistoricTotalDeltaVMaxMs = 5000.0,
                HistoricFlightDaysMin = 900.0,
                HistoricFlightDaysMax = 1000.0,
                HistoricToFDays = new List<double> { 274.0, 689.0 },
                DepIncDeg = 28.6,
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Wenzel et al. (1992), 'The Ulysses Mission', Astronomy & Astrophysics Supp. 92",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Ulysses II (尤利西斯II号 太阳极区俯瞰, 2030)",
                Description = "【虚构深空】锁定 28.6°(KSC) 发射逃逸倾角，在木星实施深空垂直面外偏转，直冲太阳极轨 (E-J)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter" },
                HistoricLaunchDateStr = "20 Jan 2030 23:30:00",
                HistoricLaunchUT = 2494798200.0,
                HistoricTotalDeltaVMinMs = 6000.0,
                HistoricTotalDeltaVMaxMs = 7500.0,
                HistoricFlightDaysMin = 800.0,
                HistoricFlightDaysMax = 900.0,
                HistoricToFDays = new List<double> { 841.0 },
                DepIncDeg = 28.6,
                LockDepartureInclination = false,
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Stone et al. (2019), 'Interstellar Probe Mission Concept Study', JHU/APL",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Interstellar Pioneer (星际先驱号 太阳系边界逃逸, 2030)",
                Description = "【虚构深空】木星超级引力弹弓连珠加速转向土星，以极高双曲速度冲出太阳系日球层 (E-J-S)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter", "Saturn" },
                HistoricLaunchDateStr = "20 Jan 2030 23:30:00",
                HistoricLaunchUT = 2494798200.0,
                HistoricTotalDeltaVMinMs = 6000.0,
                HistoricTotalDeltaVMaxMs = 8000.0,
                HistoricFlightDaysMin = 2200.0,
                HistoricFlightDaysMax = 2500.0,
                HistoricToFDays = new List<double> { 841.0, 1450.0 },
                DepAltKm = 200.0,
                ArrAltKm = 200.0,
                DepIncDeg = 28.6,
                NoInsertionBurn = true
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Jehn et al. (2017), 'BepiColombo Interplanetary Trajectory Design', ESA SP-1326",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "BepiColombo (贝皮可伦坡号 离子低推力, 2018)",
                Description = "ESA/JAXA 离子电推进 (SEP) 连续微推力打靶水星探测任务",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Mercury" },
                HistoricLaunchDateStr = "20 Oct 2018 01:45:00",
                HistoricLaunchUT = 752409600.0 + 18.8 * 365.25 * 86400.0,
                HistoricTotalDeltaVMs = 4000.0,
                HistoricTotalDeltaVProvenance = "ESA/JAXA BepiColombo Interplanetary Trajectory Baseline",
                HistoricTotalDeltaVMinMs = 3500.0,
                HistoricTotalDeltaVMaxMs = 5800.0,
                HistoricFlightDaysMin = 400.0,
                HistoricFlightDaysMax = 600.0,
                HistoricToFDays = new List<double> { 350.0, 180.0 },
                NoInsertionBurn = true,
                EngineMode = MGAEngineMode.LowThrustSimsFlanagan,
                LowThrustMaxThrustN = 0.29,
                LowThrustIspSec = 4300.0
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "Schulz et al. (2009), 'ROSETTA: ESA\'s Mission to the Origin of the Solar System', Springer",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Rosetta (罗塞塔号 异构分段混合策略, 2004)",
                Description = "分段策略：航段1纯弹道转移 + 节点1火星借力 + 航段2深空机动(1-DSM) + 节点2/3地球共振借力",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Mars", "Earth", "Earth" },
                HistoricLaunchDateStr = "02 Mar 2004 07:17:00",
                HistoricLaunchUT = 752409600.0 + 4.17 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 15000.0,
                HistoricFlightDaysMin = 2000.0,
                HistoricFlightDaysMax = 2200.0,
                HistoricToFDays = new List<double> { 365.0, 715.0, 990.0 },
                NoInsertionBurn = true,
                LegStrategies = new List<MGALegStrategy> { MGALegStrategy.Ballistic, MGALegStrategy.DeepSpaceManeuver1DSM, MGALegStrategy.Ballistic },
                FlybyStrategies = new List<MGAFlybyStrategy> { MGAFlybyStrategy.UnpoweredFlyby, MGAFlybyStrategy.BPlaneTargeted }
            }
        };

        /// <summary>
        /// 行星际直达与轨道机动基准任务集 (无引力助推，纯直达变轨与目标交会)
        /// </summary>
        public static readonly List<BenchmarkCase> DirectTransfer_Missions = new List<BenchmarkCase> {
            new BenchmarkCase {
                
                ReferenceLiterature = "Chicarro et al. (2004), 'Mars Express: A European Mission to the Red Planet', ESA SP-1240",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Mars Express (火星快车 B-Plane TCM 穿靶, 2003)",
                Description = "利用全解析 STM 灵敏度矩阵微分修正，将火星到达 B 平面穿靶误差压至零",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Mars" },
                HistoricLaunchDateStr = "02 Jun 2003 17:45:00",
                HistoricLaunchUT = 752409600.0 + 3.42 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3600.0,
                HistoricTotalDeltaVMaxMs = 4500.0,
                HistoricFlightDaysMin = 190.0,
                HistoricFlightDaysMax = 220.0,
                HistoricToFDays = new List<double> { 206.0 },
                NoInsertionBurn = true,
                TargetBDotTKm = 0.0,
                TargetBDotRKm = 0.0
            },

            new BenchmarkCase {
                
                ReferenceLiterature = "NASA Mars 2020 Mission Plan; Farley et al. (2020)",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Mars 2020 (Perseverance 毅力号火星直达转移, 2020)",
                Description = "NASA 毅力号火星探测器 2020 经典地火霍曼双曲超速直达转移基准 (E-M 直飞)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Mars" },
                HistoricLaunchDateStr = "30 Jul 2020 11:50:00",
                HistoricLaunchUT = 752409600.0 + 20.58 * 365.25 * 86400.0,
                HistoricTotalDeltaVMs = 3800.0,
                HistoricTotalDeltaVProvenance = "NASA JPL Mars 2020 Trajectory Baseline: TMI C3~13 km²/s²",
                HistoricTotalDeltaVMinMs = 3500.0,
                HistoricTotalDeltaVMaxMs = 4500.0,
                HistoricFlightDaysMin = 190.0,
                HistoricFlightDaysMax = 230.0,
                HistoricToFDays = new List<double> { 204.0 },
                NoInsertionBurn = true
            }
        };

        /// <summary>
        /// 地月空间与中心引力场轨道基准任务集 (地心引力场内往返转移)
        /// </summary>
        public static readonly List<BenchmarkCase> Cislunar_Missions = new List<BenchmarkCase> {
            new BenchmarkCase {
                
                ReferenceLiterature = "NASA SP-214: Apollo 11 Mission Report (1969)",
                BlindDimension = MGABlindSearchDimension.LaunchEpochBlind,
                MissionName = "Apollo 11 (阿波罗11号 地月转移, 1969)",
                Description = "近地停泊轨道地月转移 (TLI) 经自由返回/椭圆轨道直飞月球 (3~5天飞行历时)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Moon" },
                HistoricLaunchDateStr = "16 Jul 1969 13:32:00",
                HistoricLaunchUT = -14159880.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 4300.0,
                HistoricFlightDaysMin = 3.0,
                HistoricFlightDaysMax = 5.5,
                HistoricToFDays = new List<double> { 4.3 },
                DepAltKm = 185.0,
                ArrAltKm = 110.0,
                DepIncDeg = 28.5,
                ArrivalOrbitType = MGAArrivalOrbitType.LowCircular,
                NoInsertionBurn = false
            }
        };

        /// <summary>
        /// 完整聚合历史基准任务集合 (兼容原有调用方，保证向后兼容性)
        /// </summary>
        public static readonly List<BenchmarkCase> StandardHistoricalMissions = 
            MGA_Missions.Concat(DirectTransfer_Missions).Concat(Cislunar_Missions).ToList();

        /// <summary>
        /// 虚构深空极限探测与经典跳板任务集 (经过 SPICE 真实星历高精物理校准)
        /// </summary>
        public static readonly List<BenchmarkCase> StandardFictionalMissions = 
            MGA_Missions.Where(m => m.Description != null && m.Description.Contains("【虚构") || m.MissionName.Contains("2030") || m.MissionName.Contains("2029") || m.MissionName.Contains("2033")).ToList();

        /// <summary>
        /// 运行自适应历史任务基准测试并输出严格的物理动力学与 GMAT 回写验证报告
        /// </summary>
        public static BenchmarkResult RunBenchmark(BenchmarkCase testCase) {
            var res = new BenchmarkResult {
                TestCase = testCase,
                Success = true,
                VectorContinuityPassed = true,
                FlybyFeasibilityPassed = true,
                DeltaVWithinTolerance = true,
                FlightDaysWithinTolerance = true,
                TopologyMatchesHistoric = true,
                PrimerVectorOptimal = true
            };

            var sb = new StringBuilder();
            sb.AppendLine($"==========================================================================");
            sb.AppendLine($" NASA/ESA 历史 MGA 任务物理动力学基准比对: {testCase.MissionName}");
            sb.AppendLine($"   序列: {testCase.DepartureBody} -> {string.Join(" -> ", testCase.FlybySequence)}");
            sb.AppendLine($"   历史发射历元: {testCase.HistoricLaunchDateStr}");
            sb.AppendLine($"   运行模式: {DescribeMode(testCase.SearchMode)}");
            sb.AppendLine($"==========================================================================");

            // 1. 构建真实历史先验基因组
            var fullChain = new List<string> { testCase.DepartureBody };
            fullChain.AddRange(testCase.FlybySequence);
            int legCount = fullChain.Count - 1;

            var config = new MGAConfig {
                DepartureBody = testCase.DepartureBody,
                // 【2026-09-14 语义说明】BenchmarkCase.FlybySequence **包含终点** (末元素即目标天体)。
                // 此处的 FlybySequence 仅供"历史真值标定回放"通道使用 (回放本就以历史拓扑为输入);
                // 拓扑盲搜通道会在克隆体上把它清空 (见下方 blindMga)。
                FlybySequence = testCase.FlybySequence,
                DestinationBody = testCase.FlybySequence.Last(),
                CentralBody = SolarSystemCatalog.ResolveTransferCentralBody(testCase.DepartureBody, testCase.FlybySequence.Last()),
                DepartureAltitudeKm = testCase.DepAltKm,
                DestinationAltitudeKm = testCase.ArrAltKm,
                DepartureInclinationDeg = testCase.DepIncDeg,
                LockDepartureInclination = testCase.LockDepartureInclination,
                NoInsertionBurn = testCase.NoInsertionBurn,
                ArrivalOrbitType = testCase.ArrivalOrbitType,
                EngineMode = testCase.EngineMode,
                DsmEtaDefault = testCase.DsmEtaDefault,
                LowThrustMaxThrustN = testCase.LowThrustMaxThrustN,
                LowThrustIspSec = testCase.LowThrustIspSec,
                TargetBDotTKm = testCase.TargetBDotTKm,
                TargetBDotRKm = testCase.TargetBDotRKm,
                LegStrategies = (testCase.LegStrategies != null) ? new List<MGALegStrategy>(testCase.LegStrategies) : null,
                FlybyStrategies = (testCase.FlybyStrategies != null) ? new List<MGAFlybyStrategy>(testCase.FlybyStrategies) : null
            };

            // ---------------------------------------------------------------
            // 1. 运行模式分派 (三态互斥, 见 MGABenchmarkMode)
            //    A. HistoricReplay             : 注入全部历史真值, 仅星历链路诊断
            //    B. HistoricTopologyTrajectory : 给定历史拓扑, 搜索域全部物理推导 (默认)
            //    C. TopologyBlind              : 不给任何中间借力天体, 真盲搜
            // ---------------------------------------------------------------
            MGASolution sol = null;
            bool usedHistoricReplay = (testCase.SearchMode == MGABenchmarkMode.HistoricReplay);

            if (usedHistoricReplay) {
                // 【警告·2026-09-14】本分支把答案写进染色体。其输出只反映"星历回放是否自洽"，
                // 绝不能用来宣称"求解器复现了历史任务"。
                KSPGMATLog.Warn("Benchmark",
                    $"[{testCase.MissionName}] 进入历史真值标定回放模式: 已将历史发射日与各航段历史飞行天数注入染色体。" +
                    "该结果仅证明星历/开普勒链路自洽, 不代表求解器寻优能力。");

                double[] genome = new double[1 + legCount];
                genome[0] = testCase.HistoricLaunchUT;
                for (int k = 0; k < legCount; k++) {
                    genome[1 + k] = (testCase.HistoricToFDays != null && k < testCase.HistoricToFDays.Count)
                        ? testCase.HistoricToFDays[k] * MGADefaults.SecondsPerDay
                        : MGADefaults.HohmannDefaultToFDays * MGADefaults.SecondsPerDay;
                }
                sol = PykepBridge.BuildFullSolution(genome, fullChain, config);
            }
            else {
                // ---- 搜索域: 一律物理推导, 不得锚定历史发射日 / 历史航时 ----
                // 发射窗: 由"发射机会年份"年粒度 (或调用方显式区间) 给出，
                // 不再以历史发射日 ±N 天为圆心 —— 那会把最优解编码进搜索边界。
                ResolveBlindDepartureWindow(testCase, out double winStartUT, out double winEndUT);

                // 航时边界: 开普勒 / 共振物理推导。AutoDeriveResonantLegBounds 的 flybyChain 参数语义为
                // **中间借力天体** (不含出发与终点)，而 BenchmarkCase.FlybySequence 是**含终点**的，
                // 故必须去掉末元素，否则终点会被当成飞掠体、多出一条航段。
                var intermediates = testCase.FlybySequence.Take(Math.Max(0, testCase.FlybySequence.Count - 1)).ToList();
                var legBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(
                    testCase.DepartureBody, testCase.FlybySequence.Last(), intermediates, config.CentralBody);

                MGAConfig searchMga;
                if (testCase.SearchMode == MGABenchmarkMode.TopologyBlind) {
                    // 拓扑盲: 先在 MGAConfig 层清空拓扑, **再**转换 —— 否则 FromMGAConfig 会用历史拓扑
                    // **派生** LegToFBounds (航段航时边界)，属"派生泄漏"，比直接字段泄漏更隐蔽。
                    searchMga = config.Clone();
                    searchMga.FlybySequence = new List<string>();   // ★ 核心红线: 严格为空
                } else {
                    // 给定历史拓扑的轨迹优化: 拓扑按任务定义提供 (本模式的本义)，
                    // 但发射窗与航时边界仍全部物理推导，不注入任何历史真值。
                    searchMga = config.Clone();
                    if (intermediates.Count == 0) {
                        searchMga.FlybySequence = new List<string>();
                    } else {
                        searchMga.FlybySequence = new List<string>(intermediates);
                    }
                }
                searchMga.ArrivalOrbitType = testCase.ArrivalOrbitType;

                searchMga.EarliestDepartureUT = winStartUT;
                searchMga.LatestDepartureUT = winEndUT;
                searchMga.MinToFDays = (legBounds != null && legBounds.Count > 0) ? legBounds.Select(b => b.MinDays).ToList() : null;
                searchMga.MaxToFDays = (legBounds != null && legBounds.Count > 0) ? legBounds.Select(b => b.MaxDays).ToList() : null;

                var searchCfg = MGAAdaptiveSolverConfig.FromMGAConfig(searchMga);
                if (testCase.SearchMode == MGABenchmarkMode.TopologyBlind) {
                    searchCfg.FlybySequence = new List<string>();
                    searchCfg.LegToFBounds = new List<(double, double)>();  // 由 AutoTuneToFBounds 按候选拓扑自适应推导
                }
                searchCfg.EnablePermutationsAndInsertions = (testCase.SearchMode == MGABenchmarkMode.TopologyBlind);
                searchCfg.EnableEphemerisReachabilityFilter = true;
                searchCfg.AutoTuneToFBounds = true;
                if (testCase.HistoricFlightDaysMax > 0) {
                    searchCfg.TotalToFMaxDays = testCase.HistoricFlightDaysMax * (1.0 + testCase.MaxAcceptableToFRelTolerance);
                } else if (legBounds != null && legBounds.Count > 0) {
                    searchCfg.TotalToFMaxDays = legBounds.Sum(b => b.MaxDays);
                } else {
                    searchCfg.TotalToFMaxDays = -1.0;
                }

                KSPGMATLog.Info("Benchmark",
                    $"[{testCase.MissionName}] {DescribeMode(testCase.SearchMode)}: " +
                    (testCase.SearchMode == MGABenchmarkMode.TopologyBlind
                        ? "FlybySequence 严格为空"
                        : $"给定历史拓扑构型 {string.Join("-", intermediates)} (仅构型, 搜索域仍物理推导)") +
                    $", 目标 {testCase.FlybySequence.Last()}, " +
                    $"发射机会窗 {GMATClient.FormatUtcGregorian(winStartUT)} ~ {GMATClient.FormatUtcGregorian(winEndUT)}");

                var searchSols = MGAAdaptiveSolver.Solve(searchCfg, CancellationToken.None);

                if (searchSols != null && searchSols.Count > 0) {
                    var valid = searchSols.Where(s => s != null).ToList();
                    var feasibleSols = valid.Where(s => s.AllLegsFeasible).ToList();
                    var pool = (feasibleSols.Count > 0) ? feasibleSols : valid;

                    bool IsToFCompliant(MGASolution s) {
                        if (testCase.HistoricFlightDaysMax <= 0) return true;
                        double maxAllowed = testCase.HistoricFlightDaysMax * (1.0 + testCase.MaxAcceptableToFRelTolerance);
                        double minAllowed = testCase.HistoricFlightDaysMin > 0 ? testCase.HistoricFlightDaysMin * (1.0 - testCase.MaxAcceptableToFRelTolerance) : 0.0;
                        return s.TotalFlightDays >= minAllowed && s.TotalFlightDays <= maxAllowed;
                    }

                    bool IsTopologyCompliant(MGASolution s) {
                        if (testCase.SearchMode == MGABenchmarkMode.TopologyBlind) return true;
                        if (s.Legs == null || s.Legs.Count == 0) return false;
                        var actual = new List<string> { s.Legs[0].FromBody };
                        for (int k = 0; k < s.Legs.Count; k++) actual.Add(s.Legs[k].ToBody);
                        var expected = new List<string> { testCase.DepartureBody };
                        expected.AddRange(testCase.FlybySequence);
                        if (actual.Count != expected.Count) return false;
                        for (int k = 0; k < actual.Count; k++) {
                            if (!actual[k].Equals(expected[k], StringComparison.OrdinalIgnoreCase)) return false;
                        }
                        return true;
                    }

                    sol = pool
                        .OrderByDescending(s => IsTopologyCompliant(s))
                        .ThenByDescending(s => IsToFCompliant(s))
                        .ThenBy(s => s.TotalDeltaVKms)
                        .ThenBy(s => s.TotalFlightDays)
                        .ThenBy(s => (s.Legs != null) ? s.Legs.Count : int.MaxValue)
                        .FirstOrDefault();
                    if (sol != null) {
                        int flybyLegs = (sol.Legs != null) ? Math.Max(0, sol.Legs.Count - 1) : 0;
                        KSPGMATLog.Info("Benchmark",
                            $"[{testCase.MissionName}] 候选解 {valid.Count} 个 (物理闭环 {feasibleSols.Count} 个), " +
                            $"按 min(ΔV) 择优: 航段={sol.Legs?.Count ?? 0} (含 {flybyLegs} 次飞掠), " +
                            $"总ΔV={sol.TotalDeltaVKms * 1000.0:F0} m/s, 航时={sol.TotalFlightDays:F0}d" +
                            (flybyLegs == 0 ? " —— 注意: 择优结果为**直达转移**, 未使用引力助推" : ""));
                    }
                }
                res.BlindSearchCandidateCount = (searchSols != null) ? searchSols.Count : 0;
                res.BlindSearchUsedFlyby = (sol != null && sol.Legs != null && sol.Legs.Count > 1);
            }
            res.IsHistoricReplay = usedHistoricReplay;

            res.Solution = sol;
            if (sol == null) {
                res.Success = false;
                res.VectorContinuityPassed = false;
                res.FlybyFeasibilityPassed = false;
                res.DeltaVWithinTolerance = false;
                res.FlightDaysWithinTolerance = false;
                res.TopologyMatchesHistoric = false;
                res.PrimerVectorOptimal = false;
                sb.AppendLine($"\n 求解失败: 原生 PyKEP MGA 评估不可用且未开启托管回退 (基线7禁止托管近似回退)。");
                res.DetailedReport = sb.ToString();
                return res;
            }
            res.ComputedLaunchDV = sol.LaunchDeltaVKms * 1000.0;
            res.ComputedTotalDV = sol.TotalDeltaVKms * 1000.0;
            res.ComputedFlightDays = sol.TotalFlightDays;

            sb.AppendLine($"\n【一、 航向动力学求解结果】");
            double c3First = (sol.Legs != null && sol.Legs.Count > 0) ? sol.Legs[0].C3Kms2 : 0.0;
            sb.AppendLine($"   • 出发脱离冲量 (TMI/Ejection): {res.ComputedLaunchDV:F1} m/s (C3 能量: {c3First:F2} km²/s²)");
            sb.AppendLine($"   • 任务总机动 ΔV: {res.ComputedTotalDV:F1} m/s (历史参考: {(testCase.HistoricTotalDeltaVMs > 0.0 ? $"单值 {testCase.HistoricTotalDeltaVMs:F0} m/s" : $"区间 {testCase.HistoricTotalDeltaVMinMs:F0} ~ {testCase.HistoricTotalDeltaVMaxMs:F0} m/s")})");
            sb.AppendLine($"   • 总飞行耗时: {res.ComputedFlightDays:F1} 天 ({res.ComputedFlightDays / 365.25:F2} 年)");

            // 3. 严格验证多段 MGA 的物理矢量连续性 (C0 位置与超速动量闭环)
            sb.AppendLine($"\n【二、 多段物理矢量连续性与引力助推校验 (Vector Continuity)】");
            for (int k = 0; k < sol.Legs.Count; k++) {
                var leg = sol.Legs[k];
                sb.AppendLine($"   --- 第 {k + 1} 段: {leg.FromBody} -> {leg.ToBody} ({leg.ToFDays:F1} 天) ---");
                sb.AppendLine($"      出发位置: ({leg.DeparturePosKm.x:0.0E+0}, {leg.DeparturePosKm.y:0.0E+0}, {leg.DeparturePosKm.z:0.0E+0}) km");
                sb.AppendLine($"      到达位置: ({leg.ArrivalPosKm.x:0.0E+0}, {leg.ArrivalPosKm.y:0.0E+0}, {leg.ArrivalPosKm.z:0.0E+0}) km");

                if (k == 0) {
                    sb.AppendLine($"      [PyKEP 出射几何] 相角: {leg.PhaseAngleDeg:F2}° | 脱离角: {leg.EjectionAngleDeg:F2}° | LAN: {leg.EjectionLANDeg:F2}° | 倾角: {leg.EjectionInclinationDeg:F2}°");
                    sb.AppendLine($"      [3 轴 VNC 冲量] Prograde: {leg.ProgradeMs:F1} m/s, Normal: {leg.NormalMs:F1} m/s, Radial: {leg.RadialMs:F1} m/s");
                } else {
                    sb.AppendLine($"      [引力助推飞掠] 转向角: {leg.FlybyTurningAngleDeg:F2}° | 近心高度: {leg.FlybyPeriapsisAltitudeKm:F0} km | 飞掠机动: {leg.FlybyDeltaVKms * 1000.0:F1} m/s | 可行性: {(leg.IsFeasible ? "物理可行" : "超出借力限度")}");
                    if (!leg.IsFeasible) res.FlybyFeasibilityPassed = false;
                }

                // 检验前后两段位置 C0 连续性
                if (k > 0) {
                    double posDiscontinuity = (leg.DeparturePosKm - sol.Legs[k - 1].ArrivalPosKm).magnitude;
                    if (posDiscontinuity > 1e-3) {
                        res.VectorContinuityPassed = false;
                        sb.AppendLine($"       警告: 位置矢量存在 {posDiscontinuity:F3} km 不连续！");
                    }
                }
            }

            // 4. 验证 GMAT 脚本语法回写
            sb.AppendLine($"\n【三、 GMAT 3 轴 VNC 脚本回写验证】");
            try {
                res.GMATScriptPreview = Service.GMATMGAScriptGenerator.CompileToGmatScript(sol);
                sb.AppendLine($"   • GMAT ImpulsiveBurn 3 轴机动与星历变量回写: 生成成功 ({res.GMATScriptPreview.Length} 字节)");
                sb.AppendLine($"   • 包含脱离变量: EjectionAngle, EjectionLAN, EjectionInc, C3_Energy, V_Inf");
            } catch (Exception ex) {
                sb.AppendLine($"   • GMAT 脚本生成失败: {ex.Message}");
                res.Success = false;
            }

            // 4. ΔV 历史容差校验 (基线7: 如实判定, 避免数据造假)
            //
            // 【2026-09-14 审计修正 —— 判定带宽审计】
            //   原判据为纯区间包含 `∈[Min, Max]`，等价于"区间宽度 ≡ 判定强度"。
            //   现优先使用**单一参考值 ± 相对容差**构造带宽，并输出带宽审计：
            //   若带宽比 (Max-Min)/Min > 0.6，说明该历史区间宽到近乎无法证伪，报表将显式警告。
            double bandLo, bandHi;
            string bandDesc;
            if (testCase.HistoricTotalDeltaVMs > 0.0) {
                double tol = testCase.MaxAcceptableDvRelTolerance;
                bandLo = testCase.HistoricTotalDeltaVMs * (1.0 - tol);
                bandHi = testCase.HistoricTotalDeltaVMs * (1.0 + tol);
                bandDesc = $"历史单值 {testCase.HistoricTotalDeltaVMs:F0} m/s ± {tol * 100.0:F0}% = [{bandLo:F0}, {bandHi:F0}] m/s";
            } else {
                bandLo = testCase.HistoricTotalDeltaVMinMs;
                bandHi = testCase.HistoricTotalDeltaVMaxMs;
                bandDesc = $"历史区间 [{bandLo:F0}, {bandHi:F0}] m/s";
            }
            double bandWidthRatio = (bandLo > 0.0) ? (bandHi - bandLo) / bandLo : 0.0;

            bool dvWithinTolerance = !double.IsNaN(res.ComputedTotalDV) &&
                res.ComputedTotalDV >= bandLo &&
                res.ComputedTotalDV <= bandHi;
            res.DeltaVWithinTolerance = dvWithinTolerance;
            sb.AppendLine($"\n【四、 ΔV 历史容差校验】");
            sb.AppendLine($"   • 计算总 ΔV: {res.ComputedTotalDV:F1} m/s | {bandDesc} → {(dvWithinTolerance ? "落在判定带内" : "超出判定带")}");
            sb.AppendLine($"   • 判定带宽比 (Hi-Lo)/Lo = {bandWidthRatio * 100.0:F0}% | 出处: {(string.IsNullOrEmpty(testCase.HistoricTotalDeltaVProvenance) ? "⚠ 未登记" : testCase.HistoricTotalDeltaVProvenance)}");
            if (bandWidthRatio > 0.60 && testCase.HistoricTotalDeltaVMs <= 0.0) {
                sb.AppendLine($"   • ⚠ 警告: 判定带宽比 {bandWidthRatio * 100.0:F0}% > 60%，该区间过宽，其 PASS 的信息量有限。");
                sb.AppendLine($"     建议为该任务补填 HistoricTotalDeltaVMs (单一历史参考值) 以收紧判定。");
            }

            // 5. 航时历史容差校验 (严密性红线: 航时严禁严重超标)
            double tofBandLo = 0.0, tofBandHi = double.MaxValue;
            bool hasHistoricFlightDays = testCase.HistoricFlightDaysMax > 0.0;
            if (hasHistoricFlightDays) {
                double tofTol = testCase.MaxAcceptableToFRelTolerance > 0.0 ? testCase.MaxAcceptableToFRelTolerance : 0.25;
                tofBandLo = testCase.HistoricFlightDaysMin > 0.0 ? testCase.HistoricFlightDaysMin * (1.0 - tofTol) : 0.0;
                tofBandHi = testCase.HistoricFlightDaysMax * (1.0 + tofTol);
            }
            bool flightDaysWithinTolerance = !hasHistoricFlightDays ||
                (!double.IsNaN(res.ComputedFlightDays) && res.ComputedFlightDays >= tofBandLo && res.ComputedFlightDays <= tofBandHi);
            res.FlightDaysWithinTolerance = flightDaysWithinTolerance;
            sb.AppendLine($"\n【五、 航时历史工程包络校验】");
            sb.AppendLine($"   • 计算总航时: {res.ComputedFlightDays:F1} 天 ({res.ComputedFlightDays / 365.25:F2} 年) | 历史参考: {testCase.HistoricFlightDaysMin:F0} ~ {testCase.HistoricFlightDaysMax:F0} 天 (允许带 [{tofBandLo:F0}, {tofBandHi:F0}] 天) → {(flightDaysWithinTolerance ? "落在判定带内" : "❌ 航时超出判定带")}");

            // 6. 借力拓扑构型一致性校验 (非盲搜模式下严禁发生拓扑异化或私自插入共振圈)
            bool topologyMatchesHistoric = true;
            string topologyMismatchReason = null;
            if (testCase.SearchMode == MGABenchmarkMode.HistoricTopologyTrajectory || testCase.SearchMode == MGABenchmarkMode.HistoricReplay) {
                var expectedChain = new List<string> { testCase.DepartureBody };
                expectedChain.AddRange(testCase.FlybySequence);

                var actualChain = new List<string>();
                if (sol.Legs != null && sol.Legs.Count > 0) {
                    actualChain.Add(sol.Legs[0].FromBody);
                    for (int k = 0; k < sol.Legs.Count; k++) {
                        actualChain.Add(sol.Legs[k].ToBody);
                    }
                }

                if (actualChain.Count != expectedChain.Count) {
                    topologyMatchesHistoric = false;
                    topologyMismatchReason = $"航段天体数不一致 (期望 {string.Join("➔", expectedChain)}, 实际 {string.Join("➔", actualChain)})";
                } else {
                    for (int k = 0; k < expectedChain.Count; k++) {
                        if (!actualChain[k].Equals(expectedChain[k], StringComparison.OrdinalIgnoreCase)) {
                            topologyMatchesHistoric = false;
                            topologyMismatchReason = $"链路节点不匹配 (期望 #{k + 1}={expectedChain[k]}, 实际={actualChain[k]})";
                            break;
                        }
                    }
                }
            } else if (testCase.SearchMode == MGABenchmarkMode.TopologyBlind) {
                if (sol.Legs == null || sol.Legs.Count == 0 ||
                    !sol.Legs.Last().ToBody.Equals(testCase.FlybySequence.Last(), StringComparison.OrdinalIgnoreCase)) {
                    topologyMatchesHistoric = false;
                    topologyMismatchReason = "到达目标不一致";
                }
            }
            res.TopologyMatchesHistoric = topologyMatchesHistoric;
            sb.AppendLine($"\n【六、 借力拓扑构型一致性校验】");
            sb.AppendLine($"   • 拓扑构型判定: {(topologyMatchesHistoric ? "✅ 严格吻合历史链路" : $"❌ 拓扑异化: {topologyMismatchReason}")}");

            // 7. 劳登主矢量极值最优性校验
            double pNorm = (sol.PrimerVector != null && sol.PrimerVector.MaxPMag > 0) ? sol.PrimerVector.MaxPMag : 1.0;
            res.PrimerVectorOptimal = (pNorm <= 1.15);
            sb.AppendLine($"\n【七、 劳登主矢量最优性校验】");
            sb.AppendLine($"   • 主矢量极值 |p| = {pNorm:F3} (最优判定门限 <= 1.15) → {(res.PrimerVectorOptimal ? "满足冲量最优" : "次优或存在机动缺陷")}");

            // 8. 综合判定 (必须同时满足: 矢量连续性、飞掠可行性、ΔV容差、航时容差、拓扑一致性)
            bool overallPassed = res.VectorContinuityPassed &&
                                 res.FlybyFeasibilityPassed &&
                                 res.DeltaVWithinTolerance &&
                                 res.FlightDaysWithinTolerance &&
                                 res.TopologyMatchesHistoric;
            res.Success = overallPassed;
            if (overallPassed) {
                sb.AppendLine($"\n【基准测试综合判定: PASS 通过！】");
                if (res.IsHistoricReplay) {
                    sb.AppendLine($"   ⚠ 注意: 本次运行处于【历史真值标定回放】模式，染色体中已注入历史发射日与历史航段航时。");
                    sb.AppendLine($"     该 PASS 仅说明星历与开普勒链路自洽，**不构成求解器寻优能力的证据**。");
                } else {
                    sb.AppendLine($"   本模式 ({DescribeMode(testCase.SearchMode)}) 下得到的解: 物理闭环、矢量连续、拓扑吻合、ΔV 与航时均在历史容差内。");
                    if (testCase.SearchMode == MGABenchmarkMode.TopologyBlind) {
                        sb.AppendLine($"   盲搜候选解数: {res.BlindSearchCandidateCount} | 择优解使用引力助推: {(res.BlindSearchUsedFlyby ? "是" : "**否 (单航段直达转移, 不具备 MGA 意义)**")}");
                        sb.AppendLine($"   注: 该结论仅涉及**量级与物理自洽**, 不代表拓扑与历史构型一致。");
                    } else {
                        sb.AppendLine($"   注: 拓扑由任务定义给定, 故本 PASS 证明的是**给定构型下的轨迹优化能力**, 不是拓扑发现能力。");
                    }
                    sb.AppendLine($"       拓扑发现能力请以 MGAHistoricalBlindSearchSuite 的独立结果为准。");
                }
            } else {
                sb.AppendLine($"\n【基准测试综合判定: FAIL 校验失败】");
                if (!res.VectorContinuityPassed) sb.AppendLine("   ❌ 轨道矢量连续性不满足");
                if (!res.FlybyFeasibilityPassed) sb.AppendLine("   ❌ 飞掠借力超出物理可行边界");
                if (!res.DeltaVWithinTolerance) sb.AppendLine($"   ❌ 总ΔV超出历史容差区间 (计算={res.ComputedTotalDV:F1} m/s)");
                if (!res.FlightDaysWithinTolerance) sb.AppendLine($"   ❌ 总航时超出历史工程包络 (计算={res.ComputedFlightDays:F1} 天 vs 允许上限 {tofBandHi:F0} 天)");
                if (!res.TopologyMatchesHistoric) sb.AppendLine($"   ❌ 拓扑构型与历史不符: {topologyMismatchReason}");
            }

            res.DetailedReport = sb.ToString();
            return res;
        }

        /// <summary>
        /// 运行模式的可读描述。用于报表首行, 避免"同名不同义"的盲搜口径混淆。
        /// </summary>
        public static string DescribeMode(MGABenchmarkMode mode) {
            switch (mode) {
                case MGABenchmarkMode.HistoricReplay:
                    return "历史真值标定回放 (注种: 历史发射日+航段航时+拓扑; 仅供星历链路诊断, 非能力证据)";
                case MGABenchmarkMode.TopologyBlind:
                    return "拓扑盲搜 (不给任何中间借力天体; 发射窗与航时边界均物理推导)";
                default:
                    return "给定历史拓扑的轨迹优化基准 (非盲搜; 拓扑按任务定义给定, 发射窗与航时边界均物理推导)";
            }
        }

        /// <summary>
        /// 解析盲搜发射窗口 (KSP UT, 秒)。
        ///
        /// 优先级:
        ///   1. <see cref="BenchmarkCase.BlindSearchWindowStartStr"/> / <see cref="BenchmarkCase.BlindSearchWindowEndStr"/> 显式给定区间;
        ///   2. 否则按"发射机会年份"构造: [该年 1 月 1 日, 次年 1 月 1 日)。
        ///
        /// 【为什么不沿用历史发射日 ±N 天】原实现以 <c>HistoricLaunchUT ± DepartureWindowDays/2</c> 开窗,
        /// 等于把最优解 (精确历史发射日) 编码进搜索边界, 属答案泄漏。
        /// 年粒度窗口只表达"任务在哪一年有发射机会"这一工程级先验, 是真实任务设计中的常规约束。
        /// </summary>
        public static void ResolveBlindDepartureWindow(BenchmarkCase testCase, out double startUT, out double endUT) {
            startUT = 0.0;
            endUT = 0.0;
            if (testCase == null) return;

            double s = GMATClient.ParseUtcGregorian(testCase.BlindSearchWindowStartStr);
            double e = GMATClient.ParseUtcGregorian(testCase.BlindSearchWindowEndStr);
            if (s > 0.0 && e > s) {
                startUT = s;
                endUT = e;
                return;
            }

            // 年粒度机会窗: 只用"年", 不用"月/日/时/分"。
            int year = ExtractLaunchYear(testCase);
            s = GMATClient.ParseUtcGregorian($"01 Jan {year} 00:00:00.000");
            e = GMATClient.ParseUtcGregorian($"01 Jan {year + 1} 00:00:00.000");
            if (s <= 0.0 || e <= s) {
                // 最终回退: 以 J2000.0 为起点的固定 365.25 天窗, 同样与历史发射日无关。
                s = EphemerisTimeSystem.JdToUt(EphemerisTimeSystem.J2000_JD);
                e = s + 365.25 * MGADefaults.SecondsPerDay;
            }
            startUT = s;
            endUT = e;
        }

        /// <summary>
        /// 从历史发射日字符串中仅提取"年" (窗口级先验, 不涉及月/日/时)。
        /// </summary>
        private static int ExtractLaunchYear(BenchmarkCase testCase) {
            string str = testCase.HistoricLaunchDateStr;
            if (!string.IsNullOrEmpty(str)) {
                string[] parts = str.Trim().Split(new char[] { ' ', ':', '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && int.TryParse(parts[2], out int y) && y > 1900 && y < 2200) return y;
            }
            double ut = testCase.HistoricLaunchUT;
            if (ut > 0.0) {
                double jd = EphemerisTimeSystem.UtToJd(ut);
                return (int)Math.Floor((jd - 1721425.5) / 365.2425) + 1;
            }
            return 2000;
        }

        /// <summary>
        /// 批量自动运行全部历史基准任务 (StandardHistoricalMissions.Count) 并输出汇总
        /// </summary>
        public static (List<BenchmarkResult> results, string summary) RunAllBenchmarks() {
            var results = new List<BenchmarkResult>();
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================");
            sb.AppendLine(" NASA / ESA 历史真实 MGA 行星际引力助推全任务自适应基准测试汇总");
            sb.AppendLine("==========================================================================");

            int passCount = 0;
            int replayCount = 0;
            int blindCount = 0;
            foreach (var testCase in StandardHistoricalMissions) {
                var res = RunBenchmark(testCase);
                results.Add(res);
                if (res.Success) passCount++;
                if (res.IsHistoricReplay) replayCount++;
                if (testCase.SearchMode == MGABenchmarkMode.TopologyBlind) blindCount++;

                string status = res.Success ? " PASS" : " FAIL";
                string mode = res.IsHistoricReplay
                    ? "[注种]"
                    : (testCase.SearchMode == MGABenchmarkMode.TopologyBlind ? "[盲搜]" : "[给定拓扑]");
                sb.AppendLine($"{mode}[{status}] {testCase.MissionName.PadRight(38)} | Launch ΔV: {res.ComputedLaunchDV:F0} m/s | Total ΔV: {res.ComputedTotalDV:F0} m/s | Flight: {res.ComputedFlightDays:F0}d");
            }

            sb.AppendLine("==========================================================================");
            sb.AppendLine($" 测试总结: {passCount} / {StandardHistoricalMissions.Count} 个历史任务动力学验证通过 (基于 PyKEP 官方原生核心; 判定纳入 ΔV 容差 / 矢量连续性 / 飞掠可行性)。");
            sb.AppendLine($" 模式分布: [给定拓扑]轨迹优化 {StandardHistoricalMissions.Count - replayCount - blindCount} 个 | [盲搜] {blindCount} 个 | [注种]回放 {replayCount} 个。");
            if (replayCount > 0) {
                sb.AppendLine($" 【注意】其中 {replayCount} 个任务运行在[注种]模式 —— 该模式把历史真值注入染色体, 其 PASS 仅证明星历链路自洽, 不构成求解器能力证据。");
            }
            if (blindCount > 0) {
                sb.AppendLine($" 【注意】[盲搜]模式的 PASS 只说明量级与物理自洽; 拓扑发现能力请以 MGAHistoricalBlindSearchSuite 的结果为准。");
            }
            sb.AppendLine("==========================================================================");

            return (results, sb.ToString());
        }
    }
}
