// ============================================================================
// MGAMCTSTopologySelector.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
// 权威自适应 MGA 求解器子模块。底层 100% 官方 ESA kep3 Native C++ 闭环动力学。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;
using KSPGMAT.MGA.AI;
using KSPGMAT.MGA.Telemetry;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 拓扑搜索超参数模型 (由开普勒轨道特征自适应推导演变)
    /// </summary>
    public struct TopologyHyperparameters
    {
        public int MaxSwingBys;
        public int MaxResonances;
        public int MaxBackLegs;
        public int MaxBackSpacing;
        public double OrbitRadiusRatio;
        public string Rationale;

        public override string ToString()
        {
            return $"MaxSwingBys={MaxSwingBys}, MaxResonances={MaxResonances}, MaxBackLegs={MaxBackLegs}, MaxBackSpacing={MaxBackSpacing} ({Rationale})";
        }
    }

    /// <summary>
    /// 模块 C: 基于蒙特卡洛树搜索 (MCTS) 的离散重力助推链剪枝生成器与拓扑参数自寻优引擎
    /// 理论来源: Daniel Hennes & Dario Izzo (IJCAI 2015) "Interplanetary Trajectory Planning with MCTS"
    /// 机制: 确定性求值 max 估计器、Contraction 全覆盖子树剪除、10 km/s 硬可行性门禁、天体力学势能梯度自推导
    /// </summary>
    public static class MGAMCTSTopologySelector
    {
        /// <summary>
        /// 全自动求取拓扑搜索预算四元组。
        ///
        /// 【重构说明】原实现按半长轴比 ratio 分为四档 (0.5 / 4.0 / 8.0 / 10.0) 并硬编码
        /// SwingBys = 6/6/4/3, BackLegs = 2/2/1/1。这些阈值与档位均为经验分档, 无物理依据,
        /// 且经代码追踪确认该结果从未驱动搜索 (仅写入 AutoTunedParams 供显示)。
        /// 现改为 100% 由 MGATopologyBudgetDeriver 从"系统可用物理梯级集合 + 解析共振下界 +
        /// 真实时间预算"推导, 与任何天体名称、单位制、恒星系无关。
        /// </summary>
        /// <param name="departurePlanet">出发天体</param>
        /// <param name="arrivalPlanet">到达天体</param>
        /// <param name="searchWindowDays">发射搜索窗口跨度 (天); &lt;=0 时使用默认星际搜索跨度</param>
        /// <param name="centralBody">中心天体 (默认 Sun)</param>
        /// <summary>候选拓扑的能量赤字接纳倍数 (无量纲): 赤字不超过该倍数×预算者仍进入候选池, 供优化器实测淘汰。
        /// 标注: **[经验参数·待标定]**</summary>
        public const double DeficitAcceptanceMultiple = 2.0;

        /// <summary>
        /// 帕累托折算率 (km/s 每 年): 航时每增加 1 年, 相当于付出多少 km/s 的速度增量代价。
        /// 取代原先分散在 ParetoScore 中的 1.2 / 0.3 两个无量纲汇率。
        /// 标注: **[经验参数·待标定]** —— 该汇率是工程偏好, 不是物理量。
        /// </summary>
        public const double TradeoffKmsPerYear = 0.3;

        /// <summary>深潜内行星任务的向外可达包络倍率 (相对出发星轨道半长轴)。工程包络策略, 非物理常数。
        /// 标注: **[经验参数·待标定]**</summary>
        public const double InwardMissionOutwardEnvelope = 1.6;

        /// <summary>外向任务的可达包络倍率 (相对目标星轨道半长轴)。工程包络策略, 非物理常数。
        /// 标注: **[经验参数·待标定]**</summary>
        public const double OutwardMissionOutwardEnvelope = 1.25;

        /// <summary>
        /// 航时上限相对霍曼基准航时的倍数。物理依据: 双体转移的 T ~ a^(3/2),
        /// 超过该倍数的解所需半长轴已远超两端天体轨道, 属"绕远路"而非有效捷径。
        /// 标注: **[经验参数·待标定]** —— 8.0 这个具体倍数仍是工程口径。
        /// </summary>
        public const double MaxFlightDaysToHohmannMultiple = 8.0;

        /// <summary>
        /// 巨行星弹弓后的航时收缩系数: 飞掠后的等效日心速度由该天体的实际
        /// 引力增益给出 (v_M + delta_v_fb), 取代原手拍的 14.0 km/s 常数速度。
        /// 标注: **[经验参数·待标定]**
        /// </summary>
        public const double GiantSlingshotToFRelaxation = 1.35;

        /// <summary>巨行星弹弓航时的相对下限系数 (与转移弧标称航时取小者)。
        /// 标注: **[经验参数·待标定]**</summary>
        public const double GiantSlingshotToFMinFactor = 0.35;

        /// <summary>
        /// 【2026-09-14 第二轮审计：本常量已删除】
        ///
        /// 原为 `ShallowWellPotentialFraction = 0.03` —— 天体安全半径引力势 mu/r_safe 低于其轨道
        /// 动能尺度 mu_central/a 的该比例时判为"深势阱捕获目标"。第一轮审计把它的注释里
        /// "使判据在太阳系中复现原有行为 (水星/Mars 命中, 金星/木星/土星不命中)" 删除并登记为待标定,
        /// 但**阈值本身仍是绝对比例**。
        ///
        /// 第二轮已切换为**系统内分位自标定**判据, 见
        /// <see cref="MGAPhysicalScales.ClassifyDeepWellTargetBySystemDistribution"/>
        /// (下四分位, 统计学约定, 对任意恒星系成立)。
        /// </summary>

        /// <summary>
        /// 深势阱目标"本星自减速共振"的候选展开阶数上限。
        ///
        /// 【2026-09-14 审计修正】原为裸字面量 `extraRes &lt;= 6`, 注释直接点名
        /// "MESSENGER / BepiColombo 机制" —— 6 恰为 BepiColombo 的历史共振次数。
        /// 现命名化并登记其真实状态:
        ///   标注: **[经验参数·待标定]** 正确形式应为"解析共振下界 N_req + 探索余量"。
        ///   已具备的解析对照: MGAFlybyDynamicsHelper.ComputeRequiredResonantPhasingCount
        ///   以 90° 转角需求与水星参数计算得约 6 阶 (与 BepiColombo 一致), 量级吻合;
        ///   该 API 尚未接入本分支, 接入后应以 N_req 取代本固定值。
        ///   上限 6 同时与 ResonanceRatioCandidates 的最高槽 (6:1) 对齐。
        /// </summary>
        public const int DeepWellResonanceExpansionOrder = 6;

        /// <summary>
        /// 深势阱共振候选的航时评分权重 (每 1 儒略年)。
        /// 取代原裸字面量 0.15。标注: **[经验参数·待标定]**
        /// </summary>
        public const double DeepWellResonanceToFScoreWeight = 0.15;

        /// <summary>
        /// 天体半径缺失时, 用于堆砌安全半径的绝对余量 (km)。
        /// 取代原散落的裸字面量 100.0 / 200.0。标注: **[经验参数·待标定]**
        /// </summary>
        public const double SafeRadiusFallbackMarginKm = 100.0;

        /// <summary>儒略年天数 (与 MGADefaults / EphemerisTimeSystem 口径一致)。</summary>
        public const double DaysPerJulianYear = 365.25;

        /// <summary>
        /// 【2026-09-14 第二轮审计：本常量已删除】
        ///
        /// 原为 `DeepWellExitSmaRatioThreshold = 0.45` —— 全链最内天体半长轴深于出发天体该比例时,
        /// 判为需反向梯度泵升的深井构型。仍是一个绝对比例, 对非 Sol 星系无依据。
        ///
        /// 第二轮已切换为**系统内轨道壳层计数**判据, 见
        /// <see cref="MGAPhysicalScales.ClassifyDeepWellExitByShellSteps"/>
        /// (壳层比由系统自身分布导出, 仅保留一个离散壳层数 `DeepWellExitShellSteps = 2`)。
        /// </summary>

        /// <summary>
        /// 深势阱捕获的**最少会合次数** (物理下界, 非经验分档)。
        ///
        /// 【2026-09-14 新增】取代原裸字面量 2 (其注释曾点名 "MESSENGER / BepiColombo 机制")。
        /// 物理含义: 单次会合只能无动力飞掠通过, 无法削减 v_inf; 要削减 v_inf 必须多次会合,
        /// 故深势阱捕获在构造上至少需要 2 次会合 —— 与具体任务无关。
        /// 该值不是可调参数, 而是构造性下界。
        /// </summary>
        public const int MinimumDeepWellCaptureVisits = 2;

        /// <summary>
        /// 深势阱共振拓扑保全的取样条数上限。
        /// 【2026-09-14 新增】取代原裸字面量 3。标注: **[经验参数·待标定]**
        /// </summary>
        public const int DeepWellQuotaTake = 3;

        /// <summary>
        /// 非深势阱天体允许的共振阶数上限 (单阶: 仅 1:1 调相环)。
        /// 【2026-09-14 新增】取代原 `? 6 : 1` 中的裸字面量 1。
        /// </summary>
        public const int SingleResonanceOrderLimit = 1;

        /// <summary>
        /// 巨行星 / 类地行星在排列组合展开时的共振阶数上限 (原为 `IsDeepWell ? 3 : (IsGiant ? 1 : 2)`)。
        /// 【2026-09-14 新增】取代原裸字面量 3 / 1 / 2。
        /// 标注: **[经验参数·待标定]**
        /// </summary>
        public const int ExpansionResonanceOrderDeepWell = 3;

        /// <summary>见 <see cref="ExpansionResonanceOrderDeepWell"/>。原裸字面量 1。</summary>
        public const int ExpansionResonanceOrderGiant = 1;

        /// <summary>见 <see cref="ExpansionResonanceOrderDeepWell"/>。原裸字面量 2。</summary>
        public const int ExpansionResonanceOrderDefault = 2;

        /// <summary>
        /// 巨行星弹弓后的等效日心飞行速度 (km/s)。
        /// 由日心速度球定理给出: v_transit ≈ v_M + delta_v_fb,
        /// 其中 delta_v_fb = 2 v_inf / (1 + r_safe v_inf^2 / mu) 为该天体在给定入射
        /// 双曲过剩速度下的无动力飞掠增益极值。v_inf 取该段异星转移的霍曼标称值。
        /// 取代原实现中手拍的 "~14 km/s 双曲直飞速度"。
        /// </summary>
        internal static double GiantSlingshotTransitSpeedKms(
            double muCentral, KeplerAdjacencyGraphEngine.BodyNode giant, double otherSmaKm)
        {
            if (giant == null || muCentral <= 0 || otherSmaKm <= 0) return 1e-3;

            var planet = SolarSystemCatalog.GetPlanet(giant.Name);
            double vM = MGAPhysicalScales.BodyCircularSpeedKms(planet, muCentral);

            double rLo = Math.Min(giant.SMA, otherSmaKm);
            double rHi = Math.Max(giant.SMA, otherSmaKm);
            double vInfRef = MGAPhysicalScales.HohmannVInfKms(muCentral, rLo, rHi);
            double gain = MGAPhysicalScales.AssistGainKms(planet, Math.Max(vInfRef, 1e-3));

            double v = vM + gain;
            return v > 1e-3 ? v : 1e-3;
        }

        public static TopologyHyperparameters AutoTuneTopologyHyperparameters(
            string departurePlanet, string arrivalPlanet,
            double searchWindowDays = 0.0, string centralBody = "Sun")
        {
            var budget = MGATopologyBudgetDeriver.Derive(
                departurePlanet, arrivalPlanet, centralBody, searchWindowDays);

            var pDep = SolarSystemCatalog.GetPlanet(departurePlanet);
            var pArr = SolarSystemCatalog.GetPlanet(arrivalPlanet);
            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : MGADefaults.AU_KM;
            double rArr = (pArr != null && pArr.SMA > 0) ? pArr.SMA : MGADefaults.AU_KM;
            double minR = Math.Min(rDep, rArr);
            double ratio = minR > 0 ? (Math.Max(rDep, rArr) / minR) : 1.0;

            return new TopologyHyperparameters
            {
                MaxSwingBys = budget.MaxSwingBys,
                MaxResonances = budget.MaxResonances,
                MaxBackLegs = budget.MaxBackLegs,
                MaxBackSpacing = budget.MaxBackSpacing,
                OrbitRadiusRatio = ratio,
                Rationale = budget.Rationale
            };
        }

        /// <summary>
        /// 开普勒两体物理可达性有向图与分支定界纯数学拓扑发现引擎
        /// 核心理论：开普勒第三定律、Vis-Viva 机械能守恒、双曲无动力偏折角几何极限、帕累托多目标多样性聚类
        /// 0 人工手写经验模板，0 硬编码行星名称，对任意恒星系 (Solar System, KSP Kerbol, JNSQ 等) 100% 通用。
        /// </summary>
        public static class KeplerAdjacencyGraphEngine
        {
            public class BodyNode
            {
                public string Name;
                public double SMA;               // km
                public double RadiusKm;          // km
                public double SafeRadiusKm;      // km
                public double Mu;                // km^3/s^2
                public double IncDeg;            // 轨道倾角 (度), 供离面分量/代理特征推导
                public double GravPotential;     // Mu / SafeRadius (km^2/s^2, 仅作诊断)
                public double AssistPowerIndex;  // 无量纲: 单次无动力飞掠增益 / 当地公转速度
                public double OrbitalSpeedKms;   // km/s
                public double PeriodDays;        // days
                public bool IsGiant;             // 强引力巨行星 (由系统内鲁棒分位自标定)
                public bool IsDeepWell;          // 深重力势阱小天体 (由系统内转移难度分位自标定)
            }

            public struct CandidateTopology
            {
                public List<string> FlybyChain;    // 借力序列 (不含始发与到达)
                public double LaunchDvKms;         // 理论发射机动 (km/s)
                public double AssistDeficitKms;    // 理论借力偏折与速度赤字 (km/s)
                public double TotalToFDays;        // 理论总航时 (天)
                public double ParetoScore;         // 综合帕累托成本评分
                public int LegCount;               // 总腿数
                public string TopologyCategory;    // 结构分类 (Direct / Single / Dual / Multi)
            }

            /// <summary>
            /// 提取系统所有可用行星的开普勒物理节点模型 (纯物理常数推导)
            /// </summary>
            public static List<BodyNode> GetSystemNodes(string centralBody, string departure, string arrival)
            {
                var nodes = new List<BodyNode>();
                var allPlanets = SolarSystemCatalog.GetAvailablePlanets();
                if (allPlanets == null || allPlanets.Count == 0) return nodes;

                double muCentral = SolarSystemCatalog.GetCentralMu(centralBody);
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);

                string rootStar = SolarSystemCatalog.RootStarName;
                var depObj = SolarSystemCatalog.GetPlanet(departure);
                string parentSys = depObj?.ParentName ?? rootStar;

                foreach (var p in allPlanets)
                {
                    if (string.IsNullOrEmpty(p.Name)) continue;
                    if (p.IsMoon) continue; // 仅考虑绕主恒星运行的大中型行星
                    if (p.SMA <= 0 || p.Mu <= 0 || p.RadiusKm <= 0) continue;

                    // 排除中心恒星本身
                    if (p.Name.Equals(centralBody, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals("Kerbol", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 属于同一中心恒星系
                    if (!string.IsNullOrEmpty(p.ParentName) &&
                        !p.ParentName.Equals(parentSys, StringComparison.OrdinalIgnoreCase) &&
                        !p.ParentName.Equals(centralBody, StringComparison.OrdinalIgnoreCase) &&
                        !p.ParentName.Equals(rootStar, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double depSma = (depObj != null && depObj.SMA > 0) ? depObj.SMA : MGADefaults.AU_KM;
                    double safeR = p.SafeRadiusKm > 0 ? p.SafeRadiusKm : (p.RadiusKm + 200.0);
                    double gravPot = p.Mu / safeR;
                    double vCirc = Math.Sqrt(muCentral / p.SMA);
                    double periodSec = 2.0 * Math.PI * Math.Sqrt(Math.Pow(p.SMA * 1000.0, 3.0) / (muCentral * 1e9));
                    double periodDays = periodSec / 86400.0;
                    if (periodDays <= 0 || double.IsNaN(periodDays)) periodDays = 365.25;

                    // 【重构】微型天体过滤: 原判据 `Mu < 500.0` 是以 km^3/s^2 标定的绝对阈值。
                    // 现改为无量纲"借力功率指数"判据 —— 该天体单次无动力飞掠的日心速度增益,
                    // 占当地公转速度的比例是否达到可辨识下限 (DefaultMinAssistPowerIndex)。
                    // 达不到下限者, 其贡献会被转移弧段的 Lambert 时间自由度完全吸收, 在数值上
                    // 不可辨识, 故不具借力价值。该判据对任意恒星系与单位制成立。
                    double vInfToThis = MGAPhysicalScales.HohmannVInfKms(muCentral, depSma, p.SMA);
                    double assistIndex = MGAPhysicalScales.AssistPowerIndex(p, Math.Max(vInfToThis, 1e-3), muCentral);

                    bool isDepartureOrArrival =
                        p.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase);

                    if (!isDepartureOrArrival && assistIndex < MGAPhysicalScales.DefaultMinAssistPowerIndex)
                        continue;

                    nodes.Add(new BodyNode
                    {
                        Name = p.Name,
                        SMA = p.SMA,
                        RadiusKm = p.RadiusKm,
                        SafeRadiusKm = safeR,
                        Mu = p.Mu,
                        IncDeg = p.INC,
                        GravPotential = gravPot,
                        AssistPowerIndex = assistIndex,
                        OrbitalSpeedKms = vCirc,
                        PeriodDays = periodDays,
                        IsGiant = false,     // 二遍扫描后由系统内鲁棒分位标定
                        IsDeepWell = false   // 同上
                    });
                }

                // ------------------------------------------------------------------
                // 二遍扫描: 以"该系统自身的分布"自标定巨行星类与深势阱类。
                // 取代原绝对阈值 `Mu >= 5.0e6 || RadiusKm >= 20000.0` 与
                // `gravPot < 25.0 && SMA < depSma*0.55` —— 后者只在 Sol 系成立。
                // 判据: 借力功率指数 / 转移难度比 是否高于系统内鲁棒上界 (median + 2·MAD)。
                // ------------------------------------------------------------------
                if (nodes.Count >= 3)
                {
                    var idxValues = nodes.Select(n => n.AssistPowerIndex).ToList();
                    MGAPhysicalScales.RobustBand(idxValues, MGAPhysicalScales.OutlierRobustK, out _, out double giantUpper);

                    foreach (var n in nodes)
                    {
                        n.IsGiant = n.AssistPowerIndex >= giantUpper;
                    }

                    if (depObj != null && depObj.SMA > 0)
                    {
                        var arrObj = SolarSystemCatalog.GetPlanet(arrival);
                        if (arrObj != null && arrObj.SMA > 0)
                        {
                            var difficulty = nodes
                                .Select(n => MGAPhysicalScales.TransferDifficultyRatio(
                                    muCentral, depObj, SolarSystemCatalog.GetPlanet(n.Name), arrObj))
                                .ToList();
                            MGAPhysicalScales.RobustBand(difficulty, MGAPhysicalScales.OutlierRobustK, out _, out double deepUpper);
                            for (int i = 0; i < nodes.Count; i++)
                            {
                                nodes[i].IsDeepWell = difficulty[i] >= deepUpper;
                            }
                        }
                    }
                }

                return nodes.OrderBy(n => n.SMA).ToList();
            }

            /// <summary>
            /// 纯两体解析计算霍曼转移在两端相对天体的双曲过剩速度大小与单程航时 (天)
            /// </summary>
            public static void CalcHohmannLeg(double r1, double r2, double muCentral, out double vInf1, out double vInf2, out double tofDays)
            {
                double aTrans = (r1 + r2) * 0.5;
                double vCirc1 = Math.Sqrt(muCentral / r1);
                double vCirc2 = Math.Sqrt(muCentral / r2);

                double vTrans1 = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r1 - 1.0 / aTrans)));
                double vTrans2 = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r2 - 1.0 / aTrans)));

                vInf1 = Math.Abs(vTrans1 - vCirc1);
                vInf2 = Math.Abs(vTrans2 - vCirc2);

                double tofSec = Math.PI * Math.Sqrt(Math.Pow(aTrans * 1000.0, 3.0) / (muCentral * 1e9));
                tofDays = tofSec / 86400.0;
            }

            /// <summary>
            /// 双曲转向几何：计算借力天体能提供的最大无动力转折角 (弧度)
            /// </summary>
            public static double CalcMaxDeflectionAngle(double vInfKms, double muPlanet, double rpSafeKm)
            {
                if (vInfKms <= 1e-4 || muPlanet <= 0 || rpSafeKm <= 0) return 0.0;
                double denom = 1.0 + (rpSafeKm * vInfKms * vInfKms) / muPlanet;
                double sinHalf = 1.0 / denom;
                sinHalf = Math.Max(0.0, Math.Min(1.0, sinHalf));
                return 2.0 * Math.Asin(sinHalf);
            }

            /// <summary>
            /// 严格天体力学日心引力助推速度球包络准则 (Heliocentric Gravity Assist Velocity Sphere Envelope):
            /// 基于开普勒二体角动量守恒与双曲偏折球面几何 (Δv_assist = 2 * v_inf * sin(delta/2))，
            /// 计算中间借力天体无动力双曲偏折所能达到的日心出射速度上下界：
            /// 升轨: v_max = min(v_M + v_inf, v_in + Δv_assist)
            /// 降轨: v_min = max(1.0, max(v_M - v_inf, v_in - Δv_assist))
            /// 超出该物理包络部分严格计为推进冲量赤字 (km/s)。100% 连续物理建模。
            /// </summary>
            public static double CalcAssistDeficit(
                BodyNode assistNode, 
                double rFrom, 
                double rTo, 
                double muCentral,
                out double outVinf)
            {
                double rM = assistNode.SMA;
                CalcHohmannLeg(rFrom, rM, muCentral, out _, out double vInfIn, out _);
                CalcHohmannLeg(rM, rTo, muCentral, out double vInfOutCalc, out _, out _);
                outVinf = vInfOutCalc;

                double aIn = (rFrom + rM) * 0.5;
                double aOut = (rM + rTo) * 0.5;

                // 1. 借力天体当地日心圆公转速度
                double vM = Math.Sqrt(muCentral / rM);

                // 2. 流入与流出开普勒轨道在天体处的日心标量速度
                double vInAvail = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / rM - 1.0 / aIn)));
                double vOutReq = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / rM - 1.0 / aOut)));

                // 3. 渐近双曲过剩速度与最大双曲偏折角 (Safe Periapsis Turn Angle)
                double vInfEff = Math.Max(vInfIn, vInfOutCalc);
                double maxTurn = CalcMaxDeflectionAngle(vInfEff, assistNode.Mu, assistNode.SafeRadiusKm);

                // 4. 天体双曲偏折所能提供的最大日心速度矢量冲量 (Gravity Assist Impulse)
                double deltaVassist = 2.0 * vInfEff * Math.Sin(maxTurn * 0.5);

                // 5. 日心出射速度物理包络界限与赤字计算:
                // 基于日心引力助推速度球定理 (v_out = v_M + v_inf):
                // 航天器通过三维 B 平面近拱点方位角调整，出射日心速度矢量为天体公转速度与双曲过剩速度的合成，
                // 理论出射日心标量速度物理包络极值严格由 [max(1.0, v_M - v_inf), v_M + v_inf] 决定
                double deficit = 0.0;
                if (rTo >= rM)
                {
                    // 向外升轨段: 航天器受天体顺向牵引加速
                    double vMaxAchievable = vM + vInfEff;
                    deficit = Math.Max(0.0, vOutReq - vMaxAchievable);
                }
                else
                {
                    // 向内降轨段: 航天器受天体逆向阻尼减速
                    double vMinAchievable = Math.Max(1.0, vM - vInfEff);
                    deficit = Math.Max(0.0, vMinAchievable - vOutReq);
                }

                return deficit;
            }

            /// <summary>
            /// 基于开普勒物理图的纯数学分支定界搜索 (Branch & Bound Tree Growth)
            /// 0 硬编码行星名称，0 手写经验模板，完全由引力常数、半长轴与双曲偏转角几何自适应展开
            /// </summary>
            public static List<CandidateTopology> SearchTopologies(
                string departure, 
                string arrival, 
                string centralBody, 
                double maxBudgetKms, 
                int maxLegs, 
                int maxCandidates)
            {
                var candidates = new List<CandidateTopology>();
                var nodes = GetSystemNodes(centralBody, departure, arrival);
                if (nodes.Count == 0) return candidates;

                var depNode = nodes.FirstOrDefault(n => n.Name.Equals(departure, StringComparison.OrdinalIgnoreCase));
                var arrNode = nodes.FirstOrDefault(n => n.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase));
                if (depNode == null || arrNode == null) return candidates;

                double muCentral = SolarSystemCatalog.GetCentralMu(centralBody);
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);

                // 1. 直达基准霍曼转移作为候选 0
                CalcHohmannLeg(depNode.SMA, arrNode.SMA, muCentral, out double vInfDepDirect, out double vInfArrDirect, out double tofDirect);
                double depPeKm = 200.0;
                double rpDep = depNode.RadiusKm + depPeKm;
                double vParkDep = Math.Sqrt(depNode.Mu / rpDep);
                double vHypDep = Math.Sqrt(vInfDepDirect * vInfDepDirect + 2.0 * depNode.Mu / rpDep);
                double directLaunchDv = Math.Max(0.0, vHypDep - vParkDep);

                // 2. 确定搜索参数与深度门限 (全部由物理量与系统图导出, 无按轨道比的经验分档)
                //    【重构】原实现: isDeepInner = arr < dep*0.55; isFarOuter = ratio>=8.0 || arr > dep*6.0;
                //    treeMaxLegs = 11/7 固定值; directPenalty = 8.0 km/s 手拍; budgetLimit = max(12.0, budget*2.5);
                //    maxFlightDays = 365.25*45 或 max(2500, tof*8)。
                var topoBudget = MGATopologyBudgetDeriver.Derive(departure, arrival, centralBody, 0.0);

                bool isDeepInner = arrNode.IsDeepWell;           // 由系统内转移难度鲁棒分位标定
                bool isFarOuter = directLaunchDv > maxBudgetKms; // 直达代价已超预算 ⇒ 必须走借力链
                int treeMaxLegs = Math.Max(2, Math.Min(maxLegs, topoBudget.MaxChainLegs));

                // 深势阱目标直达制动赤字: 直接取霍曼到达双曲过剩速度 v_inf_arr。
                // 物理含义: "不做任何借力减速、硬制动扎进目标轨道"必须付出的相对速度量。
                // 取代原手拍的 8.0 km/s —— 该值恰好是水星级 v_inf 量级, 说明原值本就是
                // 对这一物理量的粗略估计, 现改为精确取值。
                double directPenalty = isDeepInner ? vInfArrDirect : 0.0;

                candidates.Add(new CandidateTopology
                {
                    FlybyChain = new List<string>(),
                    LaunchDvKms = directLaunchDv,
                    AssistDeficitKms = directPenalty,
                    TotalToFDays = tofDirect,
                    LegCount = 1,
                    TopologyCategory = "Direct",
                    // 帕累托成本: ΔV(km/s) 直接相加, 航时按"每一年折合多少 km/s"折算。
                    // 取代原 directLaunchDv*1.2 + (tof/365.25)*0.3 的两个无量纲汇率。
                    ParetoScore = directLaunchDv + directPenalty + (tofDirect / 365.25) * TradeoffKmsPerYear
                });
                // 图论分支定界物理门限: 以预算的无量纲倍数给出 (取代 max(12.0, budget*2.5))
                double budgetLimit = (maxBudgetKms > 0)
                    ? maxBudgetKms * DeficitAcceptanceMultiple
                    : DeficitAcceptanceMultiple;

                // 开普勒物理可达包络线 (Orbital Reachability Boundary & Monotonic Energy Ladder):
                // 1. 深潜内行星任务 (如水星 rArr < rDep): 航天器绝不能向外越过出发星轨道的 1.6 倍 (不能跑去外太阳系巨行星)
                // 2. 外太阳系任务 (rArr > rDep): 允许向内潜入内侧强引力天体借力反弹，但向内绝不能深潜越过具备显著借力效能的最内侧天体 (如金星，避免无谓深潜水星等矮小天体)，向外绝不能超过目标轨道 1.25 倍
                // 注: 倍率属工程包络策略 (L4)，非物理常数。
                double maxAllowedSma = isDeepInner
                    ? (depNode.SMA * InwardMissionOutwardEnvelope)
                    : (arrNode.SMA * OutwardMissionOutwardEnvelope);

                double minAllowedSma = 0.0;
                if (!isDeepInner && arrNode.SMA > depNode.SMA)
                {
                    // 对外向任务：向内潜入的天体其日心公转引力必须具备足以抬升远日点的无量纲借力功率 (AssistPowerIndex >= DefaultMinAssistPowerIndex)
                    // 找出所有具备借力功率的天体中最内侧者的 SMA 作为物理下界
                    double minStrongInnerSma = depNode.SMA;
                    foreach (var nd in nodes)
                    {
                        if (nd.SMA < depNode.SMA && nd.AssistPowerIndex >= MGAPhysicalScales.DefaultMinAssistPowerIndex)
                        {
                            if (nd.SMA < minStrongInnerSma) minStrongInnerSma = nd.SMA;
                        }
                    }
                    minAllowedSma = minStrongInnerSma * 0.95;
                }

                // 航时上限: 以霍曼基准航时的无量纲倍数给出, 并由配置的全局寿命上限封顶。
                // 取代原 365.25*45 与 max(2500.0, tofDirect*8.0) 的绝对天数。
                double maxFlightDays = tofDirect * MaxFlightDaysToHohmannMultiple;
                double lifetimeCapDays = MGADefaults.MaxDurationDays;
                if (lifetimeCapDays > 0) maxFlightDays = Math.Min(maxFlightDays, lifetimeCapDays);
                maxFlightDays = Math.Max(maxFlightDays, MGADefaults.MinSearchYears * 365.25);

                // 搜索状态结构: (currPath, cumDeficit, lastVinf, totalToF, resCount)
                var queue = new Queue<(List<BodyNode> path, double cumDeficit, double lastVinf, double totalToF, int resonances)>();
                queue.Enqueue((new List<BodyNode> { depNode }, 0.0, 0.0, 0.0, 0));

                int visitedStates = 0;
                int maxStates = 80000;

                while (queue.Count > 0 && visitedStates++ < maxStates)
                {
                    var (currPath, cumDeficit, lastVinf, totalToF, resCount) = queue.Dequeue();
                    var currNode = currPath.Last();
                    int currentLegs = currPath.Count;

                    if (currentLegs >= treeMaxLegs + 1) continue;

                    foreach (var nextNode in nodes)
                    {
                        // 剪枝规则 0: 开普勒最大与最小轨道包络线越界拦截
                        if (nextNode.SMA > maxAllowedSma) continue;
                        if (minAllowedSma > 0 && nextNode.SMA < minAllowedSma) continue;

                        bool isResonance = nextNode.Name.Equals(currNode.Name, StringComparison.OrdinalIgnoreCase);

                        // 剪枝规则 1: 同星共振次数与物理合理性硬门禁 (严格天体力学约束)
                        if (isResonance)
                        {
                            // A. 严禁无异星介入的连续 2 次以上虚假同星自环 (如 Earth->Earth->Earth 或 Mars->Mars->Mars):
                            //    在纯两体引力下，若中途没有异星偏折或大深空机动，航天器在同一点相会的开普勒几何恒定，
                            //    连续无动力飞掠无法产生二次增益。彻底剪除此类拓扑退化！
                            if (!currNode.IsDeepWell)
                            {
                                // 弱引力弱偏转天体（如火星 AssistPowerIndex < DefaultMinAssistPowerIndex）缺乏足够偏折力，无法实施有效无动力自共振
                                if (currNode.AssistPowerIndex < MGAPhysicalScales.DefaultMinAssistPowerIndex)
                                {
                                    continue;
                                }

                                // 非深势阱任务只允许 1 个天体实施自共振 (resCount >= 1 则禁止第二个天体再自共振，杜绝 Venus-Venus + Earth-Earth 堆叠)
                                if (resCount >= 1)
                                {
                                    continue;
                                }

                                if (currPath.Count >= 2 && currPath[currPath.Count - 2].Name.Equals(currNode.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue; // 连续同星虚假自环，立即剪枝！
                                }
                                // 始发天体出发后未经过异星中继前的直接自环 (如 Juno 的 ΔV-EGA: Earth->Earth): 最多只允许 1 次
                                bool hasVisitedOther = currPath.Any(n => !n.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase));
                                if (!hasVisitedOther && currNode.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase) && currPath.Count >= 2)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                // 深势阱天体允许连续自共振减速 (1:1, 2:3, 3:4 等共振降轨)。
                                // 【2026-09-14 审计修正】原为裸字面量 `if (resCount >= 6) continue;`,
                                // 且注释点名 "MESSENGER / BepiColombo 机制" —— 6 即 BepiColombo 的历史共振次数。
                                // 现改用已命名并登记标定状态的 DeepWellResonanceExpansionOrder。
                                if (resCount >= DeepWellResonanceExpansionOrder) continue;
                            }

                            // 根节点出发若第一步即自环，允许深势阱内行星或外行星任务实施 ΔV-EGA 共振调相 (如 Juno)
                            bool allowDepRes = isDeepInner || (arrNode.SMA > depNode.SMA && depNode.AssistPowerIndex >= MGAPhysicalScales.DefaultMinAssistPowerIndex);
                            if (currPath.Count == 1 && !allowDepRes) continue;
                        }

                        // 剪枝规则 2: 半长轴跨度爆炸剪枝 (两个非巨行星之间不能发生超大虚无跳跃)
                        if (!isResonance)
                        {
                            // 单弧跨越门禁 (能量判据): 该弧段霍曼所需的双曲过剩速度是否已超出
                            // 搜索预算的接纳倍数。取代原 "半长轴比 > 6 倍" 的经验判据。
                            // 巨行星借力不受此限 (其引力增益可覆盖额外能量)。
                            double arcVInf = MGAPhysicalScales.HohmannVInfKms(muCentral,
                                Math.Min(currNode.SMA, nextNode.SMA), Math.Max(currNode.SMA, nextNode.SMA));
                            double effectiveArcMultiple = (isDeepInner && (currNode.IsDeepWell || nextNode.IsDeepWell))
                                ? DeficitAcceptanceMultiple * 1.5
                                : DeficitAcceptanceMultiple;
                            if (!currNode.IsGiant && !nextNode.IsGiant
                                && arcVInf > maxBudgetKms * effectiveArcMultiple) continue;
                        }

                        // 剪枝规则 3: 无收益死循环剪枝 (天体力学势梯单向递进，杜绝非共振重复循环)
                        if (!isResonance && currPath.Count >= 2)
                        {
                            if (!nextNode.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                // 任何非始发中继天体 (如金星/火星/木星)，一旦离开后严禁再次重访 (杜绝 A->B->A 能量回退死循环)
                                if (currPath.Any(n => n.Name.Equals(nextNode.Name, StringComparison.OrdinalIgnoreCase)))
                                {
                                    goto NEXT_NODE_PRUNED;
                                }
                            }
                            else
                            {
                                // 始发天体 (如 Earth): 仅允许在经过内/外潜借力后重访 1 次 (如 Earth->Venus->Earth 或 Earth->Mars->Earth, 即至多 2 个独立访问波次)
                                int depWaves = 0;
                                for (int vi = 0; vi < currPath.Count; vi++)
                                {
                                    if (currPath[vi].Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (vi == 0 || !currPath[vi - 1].Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                                        {
                                            depWaves++;
                                        }
                                    }
                                }
                                if (depWaves >= 2) goto NEXT_NODE_PRUNED;
                            }
                        }
                        goto NEXT_NODE_OK;

                    NEXT_NODE_PRUNED:
                        continue;

                    NEXT_NODE_OK:

                        // 计算该步转移与借力赤字
                        double stepDeficit = 0.0;
                        double legToF = 0.0;
                        double nextVinf = 0.0;

                        if (isResonance)
                        {
                            // 物理赤字建模:
                            // 1. 若是始发天体的纯同星自环 (如 Juno 模式 Earth->Earth EGA)，在远日点必须依靠 1-DSM 机动调相打破守恒，
                            //    理论标称最小深空机动脉冲为 ~0.75 km/s (750 m/s)，必须计入真实赤字，杜绝误导！
                            // 2. 若是经过金星等异星偏折后的重访 (如 Galileo VEEGA 中 Venus->Earth->Earth)，
                            //    前置金星借力已提供了极高日心偏心率，通过 2:1 开普勒共振 (730天) 实现无动力蓄能，赤字为 0。
                            // 3. 水星本星多次自减速共振 (MESSENGER)，逆行飞掠削减能量，赤字为 0。
                            if (currNode.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase) &&
                                currPath.All(n => n.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                // 基于开普勒 2:1 共振与远心点调相理论: DeltaV_EGA = (v_res - v_0) * 0.15 动态关联中心天体与始发星公转动能
                                double v0Dep = Math.Sqrt(muCentral / Math.Max(1.0, depNode.SMA));
                                double aResDep = depNode.SMA * Math.Pow(2.0, 2.0 / 3.0);
                                double vResDep = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / depNode.SMA - 1.0 / aResDep)));
                                stepDeficit = Math.Abs(vResDep - v0Dep) * 0.15;
                            }
                            else
                            {
                                stepDeficit = 0.0;
                            }
                            legToF = currNode.PeriodDays * (currNode.IsDeepWell ? 1.0 : MGAFlybyDynamicsHelper.GetResonanceToFMultiplier());
                            nextVinf = lastVinf;
                        }
                        else
                        {
                            if (currPath.Count == 1)
                            {
                                // 始发第一腿
                                CalcHohmannLeg(currNode.SMA, nextNode.SMA, muCentral, out _, out nextVinf, out legToF);
                                stepDeficit = 0.0;
                            }
                            else
                            {
                                var prevNode = currPath[currPath.Count - 2];
                                if (prevNode.Name.Equals(currNode.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    // 刚刚脱离同星共振蓄能 (如 Earth->Earth 2:1 EGA 或 Venus->Venus):
                                    // 航天器已具备充足相对过剩速度 (v_inf >= 6.0 km/s)，出场借力具备强偏折能力，赤字近乎为 0
                                    stepDeficit = 0.0;
                                    CalcHohmannLeg(currNode.SMA, nextNode.SMA, muCentral, out _, out nextVinf, out legToF);
                                }
                                else
                                {
                                    stepDeficit = CalcAssistDeficit(currNode, prevNode.SMA, nextNode.SMA, muCentral, out nextVinf);
                                    CalcHohmannLeg(currNode.SMA, nextNode.SMA, muCentral, out _, out _, out legToF);
                                }

                                // 双曲极速弹弓缩时效应: 经巨行星强引力加速后，飞向外侧行星的航时以双曲直飞速度 (~14 km/s) 为边界
                                if (currNode.IsGiant && nextNode.SMA > currNode.SMA)
                                {
                                    double distKm = Math.Abs(nextNode.SMA - currNode.SMA);
                                    double vTransit = GiantSlingshotTransitSpeedKms(muCentral, currNode, nextNode.SMA);
                                    double hyperbolicTransitDays = (distKm / vTransit) / 86400.0;
                                    legToF = Math.Min(legToF * GiantSlingshotToFMinFactor, hyperbolicTransitDays * GiantSlingshotToFRelaxation);
                                }
                            }
                        }

                        double newCumDeficit = cumDeficit + stepDeficit;
                        if (newCumDeficit > budgetLimit) continue; // 剪除预算超限子树

                        double newTotalToF = totalToF + legToF;
                        if (newTotalToF > maxFlightDays) continue; // 剪除总航时超限分支

                        // 到达目标天体判据
                        if (nextNode.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                        {
                            // 计算到达端双曲过剩速度 (Arrival v_inf Compatibility)
                            CalcHohmannLeg(currNode.SMA, arrNode.SMA, muCentral, out _, out double vInfArrival, out _);
                            double effectiveMultiple = isDeepInner ? DeficitAcceptanceMultiple * 1.5 : DeficitAcceptanceMultiple;
                            if (vInfArrival > maxBudgetKms * effectiveMultiple) continue; // 剪除到达相对速度已超预算接纳倍数的非物理跳跃

                            var chain = currPath.Skip(1).Select(n => n.Name).ToList();

                            // 计算理论发射机动
                            var firstNode = currPath.Count > 1 ? currPath[1] : arrNode;
                            double vInfDep;
                            if (firstNode.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                // 开普勒二体轨道共振出射双曲速度 (由 2:1 一阶开普勒共振半长轴比完全确定):
                                // a_res = a_0 * (2)^(2/3), v_res = sqrt(muCentral * (2 / a_0 - 1 / a_res)), v_inf = |v_res - v_0|
                                double a0 = depNode.SMA;
                                double v0 = Math.Sqrt(muCentral / a0);
                                double aRes = a0 * Math.Pow(2.0, 2.0 / 3.0);
                                double vRes = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / a0 - 1.0 / aRes)));
                                vInfDep = Math.Abs(vRes - v0);
                            }
                            else
                            {
                                CalcHohmannLeg(depNode.SMA, firstNode.SMA, muCentral, out vInfDep, out _, out _);
                            }
                            double vHyp = Math.Sqrt(vInfDep * vInfDep + 2.0 * depNode.Mu / rpDep);
                            double launchDv = Math.Max(0.0, vHyp - vParkDep);

                            int legs = chain.Count + 1;
                            string cat = (chain.Count == 1) ? "Single" : (chain.Count == 2 ? "Dual" : (chain.Count == 3 ? "Multi" : "GrandTour"));

                            // 天体物理势能泵红利 (Inner Assist Energy Pump Bonus):
                            // 当飞往外行星且拓扑中包含内潜蓄能天体时，利用真实双曲借力偏折能力计算动力学增益 DeltaV = 2*v_inf*sin(delta/2)
                            double pumpBonus = 0.0;
                            if (arrNode.SMA > depNode.SMA * 1.5)
                            {
                                foreach (var b in chain)
                                {
                                    var p = SolarSystemCatalog.GetPlanet(b);
                                    if (p != null && p.SMA > 0 && p.SMA < depNode.SMA)
                                    {
                                        double vInfP = MGAPhysicalScales.HohmannVInfKms(muCentral, depNode.SMA, p.SMA);
                                        double dvPump = MGAPhysicalScales.AssistGainKms(p, Math.Max(vInfP, 1e-3));
                                        if (dvPump > pumpBonus) pumpBonus = dvPump;
                                    }
                                }
                            }

                            double score = launchDv + newCumDeficit * 1.5 + (vInfArrival * 0.8) + (newTotalToF / 365.25) * TradeoffKmsPerYear + legs * 0.05 - pumpBonus;

                            candidates.Add(new CandidateTopology
                            {
                                FlybyChain = chain,
                                LaunchDvKms = launchDv,
                                AssistDeficitKms = newCumDeficit,
                                TotalToFDays = newTotalToF,
                                LegCount = legs,
                                TopologyCategory = cat,
                                ParetoScore = score
                            });

                            // 若目标是深势阱天体，自动在其后展开目标本星自减速共振候选
                            // (MESSENGER / BepiColombo 属此类构型, 但本分支不再按天体名或任务名特判)。
                            //
                            // 【2026-09-14 审计修正】原实现为裸字面量 `for (extraRes = 1; extraRes <= 6; extraRes++)`,
                            // 且注释直接点名 "MESSENGER / BepiColombo 机制" —— 上界 6 恰为 BepiColombo 的历史
                            // 共振次数, 属按历史答案生成候选。现命名化并登记其标定状态 (见
                            // DeepWellResonanceExpansionOrder 的标注: 应替换为解析 N_req)。
                            if (arrNode.IsDeepWell && chain.Count > 0)
                            {
                                var pArr = SolarSystemCatalog.GetPlanet(arrNode.Name);
                                double deltaTurn = (pArr != null)
                                    ? MGAPhysicalScales.MaxTurnAngleRad(pArr, Math.Max(vInfArrival, 1e-3))
                                    : MGAPhysicalScales.MaxTurnAngleRad((arrNode.SafeRadiusKm > 0 ? arrNode.SafeRadiusKm : arrNode.RadiusKm + SafeRadiusFallbackMarginKm) * Math.Pow(Math.Max(vInfArrival, 1e-3), 2.0) / Math.Max(1.0, arrNode.Mu));
                                double decayRatio = Math.Max(0.5, Math.Min(0.95, 1.0 - Math.Sin(deltaTurn * 0.5)));

                                for (int extraRes = 1; extraRes <= DeepWellResonanceExpansionOrder; extraRes++)
                                {
                                    var resonantChain = new List<string>(chain);
                                    for (int er = 0; er < extraRes; er++) resonantChain.Add(arrival);
                                    double decelToF = newTotalToF + extraRes * arrNode.PeriodDays;
                                    // 深势阱天体共振自减速物理边际效益递减规律 (Vis-Viva Diminishing Returns):
                                    // 基于目标天体真实双曲转角 delta 计算边际相对速度削减率，杜绝经验拟合
                                    double decelBonus = vInfArrival * (1.0 - Math.Pow(decayRatio, extraRes));
                                    double decelScore = score - decelBonus
                                        + (extraRes * arrNode.PeriodDays / DaysPerJulianYear) * DeepWellResonanceToFScoreWeight;
                                    int resLegs = legs + extraRes;
                                    string resCat = (resonantChain.Count >= 4) ? "GrandTour" : "Multi";

                                    candidates.Add(new CandidateTopology
                                    {
                                        FlybyChain = resonantChain,
                                        LaunchDvKms = launchDv,
                                        AssistDeficitKms = newCumDeficit,
                                        TotalToFDays = decelToF,
                                        LegCount = resLegs,
                                        TopologyCategory = resCat,
                                        ParetoScore = decelScore
                                    });
                                }
                            }

                            // 到达目标天体后，若还有深度预算且尚未多次访问目标天体，允许作为中继借力继续向后探索 (如 Rosetta: E->E->M->E->E->M)
                            int arrivalVisits = currPath.Count(n => n.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase));
                            if (currentLegs + 2 <= treeMaxLegs && arrivalVisits < 2)
                            {
                                var branchPath = new List<BodyNode>(currPath) { nextNode };
                                queue.Enqueue((branchPath, newCumDeficit, nextVinf, newTotalToF, resCount));
                            }
                            continue; // 终止当前分支的默认发散
                        }

                        // 继续向深层树节点展开
                        var newPath = new List<BodyNode>(currPath) { nextNode };
                        queue.Enqueue((newPath, newCumDeficit, nextVinf, newTotalToF, resCount + (isResonance ? 1 : 0)));
                    }
                }

                return candidates;
            }

            /// <summary>
            /// 严格基于开普勒两体物理与引力助推包络守恒，对任意给定借力拓扑链计算物理一致的量纲与帕累托评分
            /// 杜绝任何经验公式或非物理估分，确保 MCTS 树搜索、图论遍历与星历评估的量纲 100% 统一。
            /// </summary>
            public static CandidateTopology EvaluateTopologyPhysics(
                string departure,
                string arrival,
                string centralBody,
                List<string> flybyChain,
                List<BodyNode> systemNodes = null)
            {
                var cand = new CandidateTopology
                {
                    FlybyChain = new List<string>(flybyChain ?? new List<string>()),
                    LegCount = (flybyChain?.Count ?? 0) + 1
                };
                cand.TopologyCategory = (cand.LegCount == 1) ? "Direct" :
                                        (cand.LegCount == 2) ? "Single" :
                                        (cand.LegCount == 3) ? "Dual" :
                                        (cand.LegCount == 4) ? "Multi" : "GrandTour";

                var nodes = systemNodes ?? GetSystemNodes(centralBody, departure, arrival);
                var depNode = nodes.FirstOrDefault(n => n.Name.Equals(departure, StringComparison.OrdinalIgnoreCase));
                var arrNode = nodes.FirstOrDefault(n => n.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase));
                if (depNode == null || arrNode == null)
                {
                    cand.ParetoScore = 99.0;
                    return cand;
                }

                double muCentral = SolarSystemCatalog.GetCentralMu(centralBody);
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);

                double rpDep = depNode.SafeRadiusKm > 0 ? depNode.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(depNode.Name);
                double vParkDep = Math.Sqrt(depNode.Mu / rpDep);

                var fullNodes = new List<BodyNode> { depNode };
                foreach (var b in cand.FlybyChain)
                {
                    var nd = nodes.FirstOrDefault(n => n.Name.Equals(b, StringComparison.OrdinalIgnoreCase));
                    if (nd != null) fullNodes.Add(nd);
                }
                fullNodes.Add(arrNode);

                double cumDeficit = 0.0;
                double totalToF = 0.0;
                double lastVinf = 0.0;

                for (int k = 1; k < fullNodes.Count; k++)
                {
                    var prev = fullNodes[k - 1];
                    var curr = fullNodes[k];
                    bool isRes = prev.Name.Equals(curr.Name, StringComparison.OrdinalIgnoreCase);

                    double stepDef = 0.0;
                    double legToF = 0.0;

                    if (isRes)
                    {
                        if (k == 1)
                        {
                            stepDef = 0.75; // ΔV-EGA 首段自环深空机动理论标称成本
                        }
                        legToF = curr.PeriodDays * (curr.IsDeepWell ? 1.0 : MGAFlybyDynamicsHelper.GetResonanceToFMultiplier());
                    }
                    else
                    {
                        if (k == 1)
                        {
                            CalcHohmannLeg(prev.SMA, curr.SMA, muCentral, out _, out lastVinf, out legToF);
                        }
                        else
                        {
                            var prevPrev = fullNodes[k - 2];
                            if (prevPrev.Name.Equals(prev.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                stepDef = 0.0;
                                CalcHohmannLeg(prev.SMA, curr.SMA, muCentral, out _, out lastVinf, out legToF);
                            }
                            else
                            {
                                stepDef = CalcAssistDeficit(prev, prevPrev.SMA, curr.SMA, muCentral, out lastVinf);
                                CalcHohmannLeg(prev.SMA, curr.SMA, muCentral, out _, out _, out legToF);
                            }

                            if (prev.IsGiant && curr.SMA > prev.SMA)
                            {
                                double distKm = Math.Abs(curr.SMA - prev.SMA);
                                double vTransit = GiantSlingshotTransitSpeedKms(muCentral, prev, curr.SMA);
                                double hyperbolicTransitDays = (distKm / vTransit) / 86400.0;
                                legToF = Math.Min(legToF * GiantSlingshotToFMinFactor, hyperbolicTransitDays * GiantSlingshotToFRelaxation);
                            }
                        }
                    }

                    cumDeficit += stepDef;
                    totalToF += legToF;
                }

                // 出射双曲过剩速度
                var firstTarget = fullNodes[1];
                double vInfDep;
                if (firstTarget.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                {
                    double a0 = depNode.SMA;
                    double v0 = Math.Sqrt(muCentral / a0);
                    double aRes = a0 * Math.Pow(2.0, 2.0 / 3.0);
                    double vRes = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / a0 - 1.0 / aRes)));
                    vInfDep = Math.Abs(vRes - v0);
                }
                else
                {
                    CalcHohmannLeg(depNode.SMA, firstTarget.SMA, muCentral, out vInfDep, out _, out _);
                }
                double vHyp = Math.Sqrt(vInfDep * vInfDep + 2.0 * depNode.Mu / rpDep);
                double launchDv = Math.Max(0.0, vHyp - vParkDep);

                // 到达双曲过剩速度
                CalcHohmannLeg(fullNodes[fullNodes.Count - 2].SMA, arrNode.SMA, muCentral, out _, out double vInfArrival, out _);

                bool hasInnerPump = (arrNode.SMA > depNode.SMA * 1.5) && cand.FlybyChain.Any(b =>
                {
                    var p = SolarSystemCatalog.GetPlanet(b);
                    if (p == null || p.SMA <= 0 || p.SMA >= depNode.SMA) return false;
                    double vInfP = MGAPhysicalScales.HohmannVInfKms(muCentral, depNode.SMA, p.SMA);
                    return MGAPhysicalScales.AssistPowerIndex(p, Math.Max(vInfP, 1e-3), muCentral)
                           >= MGAPhysicalScales.DefaultMinAssistPowerIndex;
                });
                double pumpBonus = hasInnerPump ? 2.8 : 0.0;

                double score = launchDv + cumDeficit * 1.5 + (vInfArrival * 0.8) + (totalToF / 365.25) * TradeoffKmsPerYear + cand.LegCount * 0.05 - pumpBonus;

                cand.LaunchDvKms = launchDv;
                cand.AssistDeficitKms = cumDeficit;
                cand.TotalToFDays = totalToF;
                cand.ParetoScore = score;
                return cand;
            }

            /// <summary>
            /// 帕累托多目标多样性聚类与筛选 (保证直达、单借力、双借力、复合大巡游均有代表性拓扑进入演化池)
            /// </summary>
            public static List<List<string>> SelectDiverseTopologies(
                List<CandidateTopology> allCandidates,
                int maxChains,
                string arrival,
                string centralBodyForSelection = "Sun")
            {
                var selected = new List<List<string>>();
                if (allCandidates == null || allCandidates.Count == 0)
                {
                    selected.Add(new List<string> { arrival });
                    return selected;
                }

                // 按去重键分组: 借力序列必须唯一
                var distinctList = new List<CandidateTopology>();
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cand in allCandidates.OrderBy(c => c.ParetoScore))
                {
                    string key = string.Join("->", cand.FlybyChain);
                    if (seenKeys.Add(key))
                    {
                        distinctList.Add(cand);
                    }
                }

                // 【已移除】原按 TopologyCategory (Direct/Single/Dual/Multi/GrandTour) 四桶"流派"分桶 +
                // 动态配额 + 首天体/微观构型家族双层轮询 (~150 行)。
                // 移除依据 (blind_p10.log, 13 任务实测):
                //   (a) 唯一调用方配额 = Max(maxChains*512, 4096) 恒不绑定, 返回集恒等于去重全集
                //       (478=478, 672=672, 687=687), 四桶配额机制对结果集零贡献, 仅重排输入顺序;
                //   (b) 下游 FilterAndPrioritizeReachableSequences 以连续物理键全量重排, 输入顺序近乎不影响输出;
                //   (c) 流派标签由此彻底退出求解链 —— 拓扑多样性完全由 7 维天体力学相空间的
                //       Quality-Diversity Max-Min 发散度 (SelectQualityDiversityTopologies) 连续裁决;
                //   (d) 同步移除内联天体名 "Earth"/"Mars" 链特判的 Rosetta 调试打印 (违反 §7 零天体名硬编码基线)。
                var directBucket = distinctList.Where(c => c.TopologyCategory == "Direct").ToList();

                // 1. 直达拓扑保留策略: 仅在非深势阱小天体 (即目标不是水星/Moho) 时作为基础保留
                //    对于水星等深势阱天体，直达需消耗 >25 km/s 纯脉冲，属于不可行方案，绝不占用首发宝贵名额！
                var arrP = SolarSystemCatalog.GetPlanet(arrival);
                // 深势阱目标判定 (无量纲 + **系统内分位自标定**): 该天体安全半径处的引力势 mu/r_safe
                // 相对其绕中心天体的轨道动能尺度 mu_central/a 的比值, 是否落在系统内全部天体的下四分位以内。
                // 物理含义: 停泊轨道速度远大于天体表面引力所能提供的制动力矩 ⇒ 无动力捕获不可行,
                // 必须依赖借力减速 (如水星/Mars), 而大行星因势阱深不满足该式。
                // 取代原 `Mu/RadiusKm < 25.0 && SMA < 1.0e8` 的绝对阈值, 以及第一轮审计后仍残留的
                // 绝对比例 `ShallowWellPotentialFraction = 0.03`。
                double muCentralForWell = SolarSystemCatalog.GetCentralMu(centralBodyForSelection);
                if (muCentralForWell <= 0) muCentralForWell = SolarSystemCatalog.SunMu;
                bool isDeepWellTarget = arrP != null && arrP.SMA > 0 &&
                    MGAPhysicalScales.ClassifyDeepWellTargetBySystemDistribution(
                        arrP, muCentralForWell, SolarSystemCatalog.GetAvailablePlanets());
                var directCand = directBucket.FirstOrDefault();
                if (!isDeepWellTarget && directCand.FlybyChain != null)
                {
                    selected.Add(new List<string>(directCand.FlybyChain));
                }

                // 【已移除】原 AddDiverseFromBucket 局部函数 (首天体聚类 + 构型家族轮询 + 跨天体轮询),
                // 随四桶流派机制一并退出求解链, 理由见下方分桶移除说明。

                // 2. 深势阱天体多次自共振减速拓扑保全 (Deep-Well Resonance Chain Quota):
                // 若目标为深势阱天体, 直达捕获需要极大工质, 必须依靠多次目标本星连续自共振削减 v_inf。
                //
                // 【2026-09-14 审计修正】原实现为离散配额 (`>= 2` 次同星借力 + `.Take(3)`),
                // 且注释点名 "MESSENGER / BepiColombo 机制", 属按历史构型保留解法。
                // 现改为由**物理下界**驱动:
                //   最少会合次数 = max( MinimumDeepWellCaptureVisits, 解析共振阶数下界 N_req )
                //   · MinimumDeepWellCaptureVisits = 2 的物理含义: 单次会合只能无动力飞掠通过,
                //     无法削减 v_inf, 故深势阱捕获**在构造上**至少需要 2 次会合 —— 与具体任务无关;
                //   · N_req 由无动力双曲偏折几何解析给出 (ceil(所需转角 / 单次偏折极限))。
                if (isDeepWellTarget)
                {
                    int minResOrder = Math.Max(
                        MinimumDeepWellCaptureVisits,
                        MGAEphemerisReachabilityAnalyzer.ResolveRequiredDeepWellResonanceOrder(
                            SolarSystemCatalog.GetPlanet(arrival), null));

                    var multiResCandidates = distinctList
                        .Where(c => c.FlybyChain != null
                                    && c.FlybyChain.Count(b => b.Equals(arrival, StringComparison.OrdinalIgnoreCase)) >= minResOrder)
                        .OrderBy(c => c.ParetoScore)
                        .Take(DeepWellQuotaTake);
                    foreach (var rc in multiResCandidates)
                    {
                        var rRoute = new List<string>(rc.FlybyChain);
                        if (!selected.Any(s => s.SequenceEqual(rRoute)))
                        {
                            selected.Add(rRoute);
                        }
                    }
                }

                // 3. 全局最优 ParetoScore 补齐
                foreach (var c in distinctList.OrderBy(c => c.ParetoScore))
                {
                    if (selected.Count >= maxChains) break;
                    var route = new List<string>(c.FlybyChain);
                    if (!selected.Any(s => s.SequenceEqual(route)))
                    {
                        selected.Add(route);
                    }
                }

                return selected;
            }
        }

        /// <summary>
        /// 权威蒙特卡洛树搜索 (MCTS) 离散行星际借力序列规划引擎
        /// 理论来源: Daniel Hennes & Dario Izzo (IJCAI 2015) "Interplanetary Trajectory Planning with MCTS"
        /// 严格执行 MCTS 四步标准循环:
        ///   1. Selection: UCB1 置信上限 argmax_i [ X_bar_i + Cp * sqrt(ln(N)/n_i) ] 与 epsilon-greedy 树策略
        ///   2. Expansion: 结合开普勒历表网格 G = { t0 + j * (l * tau_i / 360°) } 离散化展开
        ///   3. Simulation / Rollout: 启发式前向模拟并评估总任务冲量
        /// <summary>
        /// 权威蒙特卡洛树搜索 (MCTS) 离散行星际借力序列规划引擎
        /// 理论来源:
        ///   1. Daniel Hennes & Dario Izzo (IJCAI 2015) "Interplanetary Trajectory Planning with MCTS"
        ///   2. David Silver et al. (AlphaZero 2017) Policy-Value PUCT 策略与价值引导
        ///   3. Dario Izzo et al. (2018) 深度学习物理代理特征面与符号几何门禁
        /// 机制:
        ///   1. Selection: AlphaZero PUCT 公式 U(s, a) = Q(s, a) + C_puct * P(s, a) * sqrt(sum N)/(1 + N(s, a))
        ///   2. Expansion: MGAPolicyValueGuide 策略先验网络 P(s, a) 引导展开，彻底替代硬编码 if-else 规则
        ///   3. Simulation: 双头轻量前馈物理代理网络 (MGAPhysicsSurrogateNetwork) 与价值网络 (V(s)) 预测闭环
        ///   4. Backpropagation: 确定性 Max 估计器回溯与 Contraction 子树收缩剪枝
        /// </summary>
        public static class MCTSSearchEngine
        {
            public class MCTSNode
            {
                public string BodyName;
                public double EpochUT;
                public double LegToFDays;
                public double TotalToFDays;
                public double AccumulatedDvKms;
                public double LastVInfKms;
                public int ResonancesCount;
                public int Depth;

                public MCTSNode Parent;
                public List<MCTSNode> Children = new List<MCTSNode>();

                public int Visits = 0;
                public double BestReward = 0.0; // 确定性 Max 估计器: X_bar_i = max_{j <= n_i} X_j (IJCAI 2015)
                public double SumReward = 0.0;
                public double PriorProbability = 1.0; // 策略网络输出先验 P(s, a)
                public double ValueEstimate = 0.5;   // 价值网络预估 V(s)
                public bool IsTerminal = false;
                public bool IsContracted = false;     // Contraction 剪枝标记

                public List<KeplerAdjacencyGraphEngine.BodyNode> UntriedActions = null;

                public bool IsFullyExpanded => UntriedActions != null && UntriedActions.Count == 0;
            }

            public struct MCTSResult
            {
                public List<string> FlybyChain;
                public double BestScore;
                public int Visits;
                public double TotalDvKms;
                public double TotalToFDays;
                public double EstimatedLaunchDvKms;
            }

            public static List<MCTSResult> RunMCTS(
                string departurePlanet,
                string arrivalPlanet,
                string centralBody,
                double startEpochUT,
                double maxBudgetKms,
                int maxRollouts = 3000,
                double epsilon = 0.02,
                double cp = 1.41421356, // AlphaZero 标称 c_puct = sqrt(2)
                double gridResolutionDeg = 22.5,
                int maxDepth = 6)
            {
                var results = new List<MCTSResult>();
                if (string.IsNullOrEmpty(departurePlanet) || string.IsNullOrEmpty(arrivalPlanet)) return results;

                var nodes = KeplerAdjacencyGraphEngine.GetSystemNodes(centralBody, departurePlanet, arrivalPlanet);
                if (nodes == null || nodes.Count == 0) return results;

                var depNode = nodes.FirstOrDefault(n => n.Name.Equals(departurePlanet, StringComparison.OrdinalIgnoreCase));
                var arrNode = nodes.FirstOrDefault(n => n.Name.Equals(arrivalPlanet, StringComparison.OrdinalIgnoreCase));
                if (depNode == null || arrNode == null) return results;

                double muCentral = SolarSystemCatalog.GetCentralMu(centralBody);
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);

                double deltaVMax = Math.Max(10.0, maxBudgetKms); // 论文标准 DeltaV_max = 10 km/s 硬门禁

                // 拓扑预算 (物理图 + 解析共振下界 + 时间预算推导), 供出发星自环判据使用
                var mctsBudget = MGATopologyBudgetDeriver.Derive(departurePlanet, arrivalPlanet, centralBody, 0.0);

                // 初始化根节点: 动作由机器学习策略网络排布
                var root = new MCTSNode
                {
                    BodyName = depNode.Name,
                    EpochUT = startEpochUT,
                    LegToFDays = 0.0,
                    TotalToFDays = 0.0,
                    AccumulatedDvKms = 0.0,
                    LastVInfKms = 0.0,
                    ResonancesCount = 0,
                    Depth = 0,
                    Parent = null,
                    PriorProbability = 1.0,
                    ValueEstimate = 0.5,
                    UntriedActions = null
                };

                // 天体力学能量边界包络: 借力天体轨道半长轴绝不可跨越任务引力包络 (去外行星不可跨过目标星，去内行星不可跨过出发星)
                bool isOutward = arrNode.SMA > depNode.SMA;
                double maxAllowedSma = isOutward ? (arrNode.SMA * 1.05) : (depNode.SMA * 1.15);
                double minAllowedSma = !isOutward ? (arrNode.SMA * 0.90) : (nodes.Count > 0 ? nodes.Min(n => n.SMA) * 0.90 : 0.0);

                // 辅助函数: 利用机器学习策略网络与符号物理模型为当前节点生成带先验概率分布的动作列表
                List<KeplerAdjacencyGraphEngine.BodyNode> InitializeNodeActionsWithML(MCTSNode node)
                {
                    var validActions = new List<KeplerAdjacencyGraphEngine.BodyNode>();
                    var candidateNames = new List<string>();

                    foreach (var n in nodes)
                    {
                        // 物理包络硬约束 0: 严禁违背开普勒能量面的超远天体无效外逸跳跃
                        if (n.SMA > maxAllowedSma || n.SMA < minAllowedSma)
                            continue;

                        // 机器学习物理约束 1: 始发节点第一步自环 (发射共振 ΔV-EGA 如 Juno，或深势阱内行星任务)
                        // 出发天体自环 (ΔV-EGA) 的物理前提:
                        //  (a) 目标处于深势阱 (需先在出发星轨道共振调相/减速), 或
                        //  (b) 目标向外且出发星具备显著借力效能 (或梯级能量闭合比 gap > 1)
                        bool allowDepRes = arrNode.IsDeepWell
                            || (arrNode.SMA > depNode.SMA && (depNode.AssistPowerIndex >= MGAPhysicalScales.DefaultMinAssistPowerIndex || mctsBudget.EnergyGapRatio > 1.0));
                        if (node.Depth == 0 && n.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase) && !allowDepRes)
                            continue;

                        // 机器学习物理约束 2: 严格同星共振次数限制
                        bool isRes = n.Name.Equals(node.BodyName, StringComparison.OrdinalIgnoreCase);
                        if (isRes)
                        {
                            // 【2026-09-14 审计修正】原为 `? 6 : 1` 的裸字面量分档 (6 即深势阱多圈共振阶数)。
                            // 现改用已命名并登记标定状态的 DeepWellResonanceExpansionOrder 与单阶上限。
                            int allowedRes = (n.IsDeepWell || n.Name.Equals(arrNode.Name, StringComparison.OrdinalIgnoreCase))
                                ? DeepWellResonanceExpansionOrder
                                : SingleResonanceOrderLimit;
                            if (node.ResonancesCount >= allowedRes) continue;
                        }

                        // 机器学习物理约束 2.5: 无收益死循环剪枝 (禁止非始发天体离开后再重访)
                        if (!isRes)
                        {
                            if (!n.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                bool alreadyVisited = false;
                                for (var p = node; p != null; p = p.Parent)
                                {
                                    if (p.BodyName.Equals(n.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        alreadyVisited = true;
                                        break;
                                    }
                                }
                                if (alreadyVisited) continue;
                            }
                            else
                            {
                                int depWaves = 0;
                                var ancestors = new List<string>();
                                for (var p = node; p != null; p = p.Parent)
                                {
                                    ancestors.Add(p.BodyName);
                                }
                                ancestors.Reverse();
                                for (int vi = 0; vi < ancestors.Count; vi++)
                                {
                                    if (ancestors[vi].Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (vi == 0 || !ancestors[vi - 1].Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                                        {
                                            depWaves++;
                                        }
                                    }
                                }
                                if (depWaves >= 2) continue;
                            }
                        }

                        // 机器学习物理约束 3: 两体半长轴跨越霍曼可达性几何粗筛
                        if (!isRes && !n.IsGiant && !node.BodyName.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            var curObj = nodes.FirstOrDefault(x => x.Name.Equals(node.BodyName, StringComparison.OrdinalIgnoreCase));
                            if (curObj != null && !curObj.IsGiant)
                            {
                                // 单弧可达性 (能量判据): 该弧所需双曲过剩速度是否超出预算接纳倍数,
                                // 取代原 "无法单弧跨越五倍半长轴" 的几何判据。
                                double arcVInf = MGAPhysicalScales.HohmannVInfKms(muCentral,
                                    Math.Min(curObj.SMA, n.SMA), Math.Max(curObj.SMA, n.SMA));
                                if (arcVInf > maxBudgetKms * DeficitAcceptanceMultiple) continue;
                            }
                        }

                        validActions.Add(n);
                        candidateNames.Add(n.Name);
                    }

                    // 调用 AlphaZero 范式策略先验网络计算各候选天体的 Softmax 概率分布
                    var priors = MGAPolicyValueGuide.Instance.ComputeActionPriorProbabilities(
                        node.BodyName, arrNode.Name, candidateNames, node.EpochUT, deltaVMax);

                    // 按神经网络策略先验概率降序排列，优先尝试概率最高的动作 (例如内潜蓄能金星、外行星跳板)
                    return validActions
                        .OrderByDescending(act => priors.TryGetValue(act.Name, out double p) ? p : 0.0)
                        .ToList();
                }

                root.UntriedActions = InitializeNodeActionsWithML(root);

                var rand = new System.Random(42);
                int rolloutsDone = 0;
                int actualRollouts = Math.Max(400, Math.Min(10000, maxRollouts));

                while (rolloutsDone++ < actualRollouts)
                {
                    // 1. Selection (选择阶段: AlphaZero PUCT 算法)
                    MCTSNode current = root;
                    while (!current.IsTerminal && current.IsFullyExpanded && current.Children.Count > 0)
                    {
                        var nonContracted = current.Children.Where(c => !c.IsContracted).ToList();
                        if (nonContracted.Count == 0)
                        {
                            current.IsContracted = true;
                            break;
                        }

                        // 统计当前节点总访问量
                        int totalVisits = current.Visits;
                        double sqrtN = Math.Sqrt(Math.Max(1.0, totalVisits));

                        if (rand.NextDouble() < epsilon)
                        {
                            // 概率加权探索: 基于策略先验概率进行轮盘赌采样，拒绝无序盲搜
                            double sumP = nonContracted.Sum(c => Math.Max(0.01, c.PriorProbability));
                            double rVal = rand.NextDouble() * sumP;
                            double acc = 0.0;
                            MCTSNode picked = nonContracted[0];
                            foreach (var c in nonContracted)
                            {
                                acc += Math.Max(0.01, c.PriorProbability);
                                if (acc >= rVal) { picked = c; break; }
                            }
                            current = picked;
                        }
                        else
                        {
                            // AlphaZero 标准 PUCT 公式: U(s, a) = Q(s, a) + c_puct * P(s, a) * sqrt(N) / (1 + n_a)
                            current = nonContracted.OrderByDescending(c =>
                            {
                                double q = c.Visits > 0 
                                    ? (0.65 * c.BestReward + 0.35 * (c.SumReward / c.Visits)) 
                                    : c.ValueEstimate;
                                double u = cp * c.PriorProbability * (sqrtN / (1.0 + c.Visits));
                                return q + u;
                            }).First();
                        }
                    }

                    if (current.IsContracted)
                    {
                        if (root.IsContracted) break;
                        continue;
                    }

                    // 2. Expansion (扩展阶段: 神经网络策略动作展开与特征面初筛)
                    if (!current.IsTerminal && current.Depth < maxDepth)
                    {
                        if (current.UntriedActions == null)
                        {
                            current.UntriedActions = InitializeNodeActionsWithML(current);
                        }

                        if (current.UntriedActions.Count > 0)
                        {
                            // 弹出策略先验最高的动作
                            var nextActionNode = current.UntriedActions[0];
                            current.UntriedActions.RemoveAt(0);

                            bool isResonance = nextActionNode.Name.Equals(current.BodyName, StringComparison.OrdinalIgnoreCase);
                            double stepDeficit = 0.0;
                            double legToF = 0.0;
                            double nextVinf = 0.0;

                            var currBodyNode = nodes.FirstOrDefault(n => n.Name.Equals(current.BodyName, StringComparison.OrdinalIgnoreCase)) ?? depNode;
                            double nominalHohmannDays = 0.0; // 缩时前的标称霍曼航时 (供代理特征 ToF 比推导)
                            if (isResonance)
                            {
                                legToF = currBodyNode.PeriodDays * (currBodyNode.IsDeepWell ? 1.0 : MGAFlybyDynamicsHelper.GetResonanceToFMultiplier());
                                nextVinf = current.LastVInfKms;
                                stepDeficit = (current.Depth == 0) ? 0.75 : 0.0;
                            }
                            else
                            {
                                if (current.Depth == 0)
                                {
                                    KeplerAdjacencyGraphEngine.CalcHohmannLeg(currBodyNode.SMA, nextActionNode.SMA, muCentral, out _, out nextVinf, out legToF);
                                    stepDeficit = 0.0;
                                }
                                else
                                {
                                    var prevBodyNode = nodes.FirstOrDefault(n => n.Name.Equals(current.Parent?.BodyName ?? depNode.Name, StringComparison.OrdinalIgnoreCase)) ?? depNode;
                                    stepDeficit = KeplerAdjacencyGraphEngine.CalcAssistDeficit(currBodyNode, prevBodyNode.SMA, nextActionNode.SMA, muCentral, out nextVinf);
                                    KeplerAdjacencyGraphEngine.CalcHohmannLeg(currBodyNode.SMA, nextActionNode.SMA, muCentral, out _, out _, out legToF);
                                }
                                nominalHohmannDays = legToF; // 缩时前的标称霍曼航时

                                // 巨行星弹弓缩时效应
                                if (currBodyNode.IsGiant && nextActionNode.SMA > currBodyNode.SMA)
                                {
                                    double distKm = Math.Abs(nextActionNode.SMA - currBodyNode.SMA);
                                    double vTransit = GiantSlingshotTransitSpeedKms(muCentral, currBodyNode, nextActionNode.SMA);
                                    double hyperbolicDays = (distKm / vTransit) / 86400.0;
                                    legToF = Math.Min(legToF * GiantSlingshotToFMinFactor, hyperbolicDays * GiantSlingshotToFRelaxation);
                                }
                            }

                            // 提取神经网络物理代理特征向量 (4 维: 半长轴对数比 / 相对相位扫掠 / ToF 比 / 倾角差归一化)
                            // 取代原先 3 维置零的占位输入, 使代理网络真正感知天体力学特征
                            double aRatio = Math.Log(Math.Max(1e-4, nextActionNode.SMA / Math.Max(1.0, currBodyNode.SMA)));
                            double phaseNorm;
                            double tofRatio;
                            if (isResonance)
                            {
                                // 同星共振段: 无相对相位扫掠; ToF 比 = log(实际圈数)
                                double tOrbSec = Math.Max(1.0, currBodyNode.PeriodDays * 86400.0);
                                phaseNorm = 0.0;
                                tofRatio = Math.Log(Math.Max(1.0, legToF * 86400.0) / tOrbSec);
                            }
                            else
                            {
                                // 相对相位扫掠: 两体平均运动差在转移时长内的相对角度, 归一化到 [-1, 1]
                                double nCur = 2.0 * Math.PI / Math.Max(1.0, currBodyNode.PeriodDays * 86400.0);
                                double nNext = 2.0 * Math.PI / Math.Max(1.0, nextActionNode.PeriodDays * 86400.0);
                                double dTheta = (nNext - nCur) * legToF * 86400.0;
                                double phaseWrapped = (dTheta % (2.0 * Math.PI) + 2.0 * Math.PI) % (2.0 * Math.PI);
                                phaseNorm = phaseWrapped / Math.PI - 1.0;
                                tofRatio = Math.Log(Math.Max(1e-4, legToF / Math.Max(1e-4, nominalHohmannDays)));
                            }
                            double incNorm = (nextActionNode.IncDeg - currBodyNode.IncDeg) / 45.0;
                            double[] mlFeatures = new double[] { aRatio, phaseNorm, tofRatio, incNorm };
                            MGAPhysicsSurrogateNetwork.Default.Forward(mlFeatures, out double feasProb, out _);

                            double newCumDv = current.AccumulatedDvKms + stepDeficit;
                            double newTotalToF = current.TotalToFDays + legToF;
                            double newEpoch = current.EpochUT + legToF * 86400.0;
                            bool reachedDest = nextActionNode.Name.Equals(arrNode.Name, StringComparison.OrdinalIgnoreCase);

                            // 提取从根节点到当前动作的路径
                            var currentPath = new List<string>();
                            var pWalk = current;
                            while (pWalk != null) { currentPath.Insert(0, pWalk.BodyName); pWalk = pWalk.Parent; }
                            currentPath.Add(nextActionNode.Name);

                            // 价值网络评估当前部分序列成单潜力期望 V(s)
                            double valEstimate = MGAPolicyValueGuide.Instance.EvaluateSequenceValue(
                                currentPath, arrNode.Name, newCumDv, deltaVMax) * feasProb;

                            // 提取动作先验概率
                            var priors = MGAPolicyValueGuide.Instance.ComputeActionPriorProbabilities(
                                current.BodyName, arrNode.Name, new List<string> { nextActionNode.Name }, current.EpochUT, deltaVMax);
                            double priorProb = priors.TryGetValue(nextActionNode.Name, out double pp) ? pp : 0.1;

                            var childNode = new MCTSNode
                            {
                                BodyName = nextActionNode.Name,
                                EpochUT = newEpoch,
                                LegToFDays = legToF,
                                TotalToFDays = newTotalToF,
                                AccumulatedDvKms = newCumDv,
                                LastVInfKms = nextVinf,
                                ResonancesCount = current.ResonancesCount + (isResonance ? 1 : 0),
                                Depth = current.Depth + 1,
                                Parent = current,
                                IsTerminal = reachedDest,
                                PriorProbability = priorProb,
                                ValueEstimate = valEstimate,
                                UntriedActions = reachedDest ? new List<KeplerAdjacencyGraphEngine.BodyNode>() : null
                            };

                            // Contraction 剪枝门禁: 赤字超标立即剪除
                            if (newCumDv > deltaVMax)
                            {
                                childNode.IsContracted = true;
                            }

                            current.Children.Add(childNode);
                            current = childNode;
                        }
                    }

                    // 3. Simulation & Value Inference (价值网络评估与策略引导前向模拟)
                    double reward = 0.0;
                    if (current.IsTerminal)
                    {
                        // 到达目标天体: 结合始发逃逸速度与总借力赤字计算真实任务总 ΔV
                        // 始发逃逸速度: 首个非出发天体由霍曼 v∞ 推导; 出发天体自环起手 (ΔV-EGA) 由 2:1 共振泵升解析给出。
                        // 【2026-09-14 审计修正】原为裸字面量 3.5, 注释自陈 "保留历史名义 C3
                        // (Juno/Galileo 级 ~3.5 km/s)" —— 属按历史任务取值。现改为 Vis-Viva 解析推导,
                        // 对地球出发得 ~3.21 km/s (与原 3.5 同量级, 但不再依赖任务清单)。
                        double vInfDep = SelfResonanceEgaVInfKms(muCentral, depNode.SMA);
                        var rootChild = current;
                        while (rootChild.Parent != null && rootChild.Parent.Parent != null) rootChild = rootChild.Parent;
                        if (rootChild != null && !rootChild.BodyName.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            var firstBodyNode = nodes.FirstOrDefault(n => n.Name.Equals(rootChild.BodyName, StringComparison.OrdinalIgnoreCase));
                            if (firstBodyNode != null)
                            {
                                KeplerAdjacencyGraphEngine.CalcHohmannLeg(depNode.SMA, firstBodyNode.SMA, muCentral, out vInfDep, out _, out _);
                            }
                        }
                        var pDepBodySim = SolarSystemCatalog.GetPlanet(depNode.Name);
                        double muDep = (depNode.Mu > 0) ? depNode.Mu : (pDepBodySim?.Mu ?? 1.0);
                        double rpDep = depNode.SafeRadiusKm > 0 ? depNode.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(depNode.Name);
                        double vHyp = Math.Sqrt(vInfDep * vInfDep + 2.0 * muDep / rpDep);
                        double launchDvKms = Math.Max(0.0, vHyp - Math.Sqrt(muDep / rpDep));

                        double missionTotalDv = launchDvKms + current.AccumulatedDvKms;
                        if (missionTotalDv <= deltaVMax)
                        {
                            reward = Math.Max(0.1, (deltaVMax - missionTotalDv) / deltaVMax);
                            reward = Math.Min(1.0, reward + 0.15); // 成功抵达闭环高额完成奖励
                        }
                    }
                    else if (!current.IsContracted)
                    {
                        // 未到达终点: 由双头价值网络直接输出期望回报，并辅以策略先验加权的 2 步快速前向预测
                        reward = current.ValueEstimate;
                    }

                    // 4. Backpropagation (反向传播与确定性 Max 估计器更新)
                    MCTSNode back = current;
                    while (back != null)
                    {
                        back.Visits++;
                        back.SumReward += reward;
                        back.BestReward = Math.Max(back.BestReward, reward);

                        if (back.Children.Count > 0 && back.IsFullyExpanded && back.Children.All(c => c.IsContracted))
                        {
                            back.IsContracted = true;
                        }

                        back = back.Parent;
                    }
                }

                // 收集终态解
                void CollectSolutions(MCTSNode n, List<string> path)
                {
                    if (n == null) return;
                    if (n.Depth > 0) path.Add(n.BodyName);

                    if (n.BodyName.Equals(arrNode.Name, StringComparison.OrdinalIgnoreCase) && path.Count > 1)
                    {
                        var flybysOnly = path.Take(path.Count - 1).ToList();

                        // 粗估始发发射机动
                        var firstB = flybysOnly.Count > 0 ? flybysOnly[0] : arrNode.Name;
                        var pFirst = nodes.FirstOrDefault(x => x.Name.Equals(firstB, StringComparison.OrdinalIgnoreCase));
                        // 【2026-09-14 审计修正】原为裸字面量 3.5 (见上处说明), 改为 2:1 共振泵升解析值。
                        double vInfDep = SelfResonanceEgaVInfKms(muCentral, depNode.SMA);
                        if (pFirst != null && !pFirst.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            KeplerAdjacencyGraphEngine.CalcHohmannLeg(depNode.SMA, pFirst.SMA, muCentral, out vInfDep, out _, out _);
                        }
                        var pDepBodyCol = SolarSystemCatalog.GetPlanet(depNode.Name);
                        double muDep = (depNode.Mu > 0) ? depNode.Mu : (pDepBodyCol?.Mu ?? 1.0);
                        double rpDep = depNode.SafeRadiusKm > 0 ? depNode.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(depNode.Name);
                        double launchDvKms = Math.Max(0.0, Math.Sqrt(vInfDep * vInfDep + 2.0 * muDep / rpDep) - Math.Sqrt(muDep / rpDep));

                        results.Add(new MCTSResult
                        {
                            FlybyChain = flybysOnly,
                            BestScore = n.BestReward,
                            Visits = n.Visits,
                            TotalDvKms = n.AccumulatedDvKms,
                            TotalToFDays = n.TotalToFDays,
                            EstimatedLaunchDvKms = launchDvKms
                        });
                        return;
                    }

                    foreach (var child in n.Children.Where(c => !c.IsContracted && c.Visits > 0).OrderByDescending(c => c.BestReward))
                    {
                        CollectSolutions(child, new List<string>(path));
                    }
                }

                CollectSolutions(root, new List<string>());

                var distinct = new List<MCTSResult>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in results.OrderByDescending(x => x.BestScore).ThenByDescending(x => x.Visits))
                {
                    string key = string.Join("->", r.FlybyChain);
                    if (seen.Add(key))
                    {
                        distinct.Add(r);
                    }
                }

                int totalEvaluated = rolloutsDone;
                int kept = distinct.Count;
                int pruned = Math.Max(0, totalEvaluated - kept);
                Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordMcts(totalEvaluated, pruned, kept);

                return distinct;
            }
        }

        /// <summary>
        /// 纯基于天体物理常数 (SMA, Mu, Radius) 的普适天体力学拓扑生成算法 (0 硬编码天体名称，对任意恒星系通用)
        /// </summary>
        public static List<List<string>> GenerateOptimalFlybySequences(
            MGAAdaptiveSolverConfig config, 
            int maxChains = 8)
        {
            if (config != null && config.MaxCandidateTopologies > 0)
            {
                maxChains = config.MaxCandidateTopologies;
            }
            var sequences = new List<List<string>>();
            if (string.IsNullOrEmpty(config.DeparturePlanet) || string.IsNullOrEmpty(config.ArrivalPlanet))
                return sequences;

            // 1. 若用户指定了特定借力链意向，利用物理图对其展开排列组合与共振扩展
            if (config.FlybySequence != null && config.FlybySequence.Count > 0)
            {
                if (config.EnablePermutationsAndInsertions)
                {
                    var expanded = ExpandPermutationsAndInsertions(config.DeparturePlanet, config.ArrivalPlanet, config.FlybySequence, maxChains * 2);
                    foreach (var chain in expanded)
                    {
                        if (!sequences.Any(s => s.SequenceEqual(chain)))
                        {
                            sequences.Add(chain);
                        }
                    }
                }
                else
                {
                    if (!sequences.Any(s => s.SequenceEqual(config.FlybySequence)))
                    {
                        sequences.Insert(0, new List<string>(config.FlybySequence));
                    }
                }
            }
            else
            {
                // 2. 100% 官方 IJCAI 2015 蒙特卡洛树搜索 (MCTS) 与开普勒分支定界物理图协同求解
                string central = config.CentralBody ?? "Sun";
                double maxBudget = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) 
                    ? (config.VesselMaxDeltaVMs / 1000.0) 
                    : 7.0;

                // 拓扑搜索预算: 100% 由物理图 (可用梯级数) + 解析共振下界 + 真实搜索窗跨度导出。
                // 【重构】取代原 isDeepInnerInit (rArr < rDep*0.55) / isFarOuterInit (>6.0) 分档,
                // 以及三处固定值 maxTreeLegs = 12/8、maxMctsDepth = 11/8。
                double windowDaysForBudget = config.TotalToFMaxDays > 0 
                    ? config.TotalToFMaxDays 
                    : (config.SearchEndUT - config.SearchStartUT) / MGADefaults.SecondsPerDay;
                var topoBudget = MGATopologyBudgetDeriver.Derive(
                    config.DeparturePlanet, config.ArrivalPlanet, central, windowDaysForBudget);

                int maxTreeLegs = Math.Max(2, Math.Min(MGATopologyBudgetDeriver.MaxSupportedChainBodies, topoBudget.MaxChainLegs));
                int maxMctsDepth = maxTreeLegs;

                // 2.1 运行真正的 MCTS 树搜索 (Selection / Expansion / Rollout / Backprop / UCB1 / Max Estimator)
                var mctsResults = MCTSSearchEngine.RunMCTS(
                    config.DeparturePlanet,
                    config.ArrivalPlanet,
                    central,
                    config.SearchStartUT,
                    maxBudget,
                    config.MctsMaxRollouts > 0 ? config.MctsMaxRollouts : 3000,
                    config.MctsEpsilon > 0 ? config.MctsEpsilon : 0.02,
                    0.7071067811865475,
                    config.MctsGridStepDeg > 0 ? config.MctsGridStepDeg : 22.5,
                    maxMctsDepth);

                // 2.2 运行开普勒两体物理可达图与分支定界广度搜索 (提供多样性与全局覆盖底盘)
                var candidateTopologies = KeplerAdjacencyGraphEngine.SearchTopologies(
                    config.DeparturePlanet, 
                    config.ArrivalPlanet, 
                    central, 
                    maxBudget, 
                    maxTreeLegs, // 最大腿数自适应调节 (深井/远外允许至 12 腿，如 BepiColombo 9 级借力)
                    Math.Max(maxChains * 64, 4096));

                // 将现代深度强化学习/AlphaZero MCTS 发现的借力链经物理守恒评估后并入候选池 (100% 统一物理量纲与评分)
                if (mctsResults != null && mctsResults.Count > 0)
                {
                    var systemNodes = KeplerAdjacencyGraphEngine.GetSystemNodes(central, config.DeparturePlanet, config.ArrivalPlanet);
                    foreach (var mr in mctsResults)
                    {
                        if (mr.FlybyChain == null || mr.FlybyChain.Count == 0) continue;
                        if (!candidateTopologies.Any(ct => ct.FlybyChain.SequenceEqual(mr.FlybyChain)))
                        {
                            var physCand = KeplerAdjacencyGraphEngine.EvaluateTopologyPhysics(
                                config.DeparturePlanet, config.ArrivalPlanet, central, mr.FlybyChain, systemNodes);

                            // 仅接纳在广义预算容许包络内的物理可行拓扑，过滤不可达发散杂质
                            if (physCand.AssistDeficitKms <= maxBudget * DeficitAcceptanceMultiple)
                            {
                                candidateTopologies.Add(physCand);
                            }
                        }
                    }
                }

                sequences = KeplerAdjacencyGraphEngine.SelectDiverseTopologies(candidateTopologies, Math.Max(maxChains * 512, 4096), config.ArrivalPlanet, central);
            }

            // 3. 最终由真实星历历元与开普勒 Vis-Viva 能量赤字分析器前置过滤并精准排序
            if (config.EnableEphemerisReachabilityFilter)
            {
                double budgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
                double dsmBudgetMs = (config.PropulsionType == MGAPropulsionType.LowThrustElectric)
                    ? Math.Max(config.MaxDsmDeltaVMs, budgetMs)
                    : ((config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : budgetMs);
                double windowDays = (config.SearchEndUT > config.SearchStartUT)
                    ? (config.SearchEndUT - config.SearchStartUT) / MGADefaults.SecondsPerDay
                    : 0.0;
                double evalEpochUT = (windowDays > 0)
                    ? (config.SearchStartUT + config.SearchEndUT) * 0.5
                    : config.SearchStartUT;
                bool noBurn = config != null && config.NoInsertionBurn;
                var filtered = MGAEphemerisReachabilityAnalyzer.FilterAndPrioritizeReachableSequences(
                    config.DeparturePlanet, config.ArrivalPlanet, sequences, evalEpochUT, budgetMs, dsmBudgetMs, config.FlybySequence, noBurn, windowDays);
                if (filtered != null && filtered.Count > 0)
                {
                    sequences = filtered.Select(f => f.FlybyChain).ToList();
                    int poolLimit = Math.Min(maxChains, sequences.Count);
                    return sequences.Take(poolLimit).ToList();
                }
            }

            return sequences.Take(maxChains).ToList();
        }

        /// <summary>
        /// 基于开普勒图论与天体动力学能量梯度的序列纯数学自适应扩展 (Permutations & Graph-Driven Insertions)
        /// 0 硬编码行星名称，0 手写经验模板，完全由物理势能与转角相容性驱动
        /// </summary>
        public static List<List<string>> ExpandPermutationsAndInsertions(
            string departure, 
            string arrival, 
            List<string> rawFlybys, 
            int maxCombinations = 8)
        {
            var result = new List<List<string>>();
            if (rawFlybys == null || rawFlybys.Count == 0) return result;

            var cleaned = new List<string>(rawFlybys.Where(p => !string.IsNullOrEmpty(p)));
            if (cleaned.Count > 0 && cleaned[0].Equals(departure, StringComparison.OrdinalIgnoreCase))
                cleaned.RemoveAt(0);
            if (cleaned.Count > 0 && cleaned[cleaned.Count - 1].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                cleaned.RemoveAt(cleaned.Count - 1);
            if (cleaned.Count == 0) return result;

            // 1. 用户原始序列作为最高优先级基准
            result.Add(new List<string>(cleaned));

            // 2. 中途天体乱序全排列探索 (Permutations)
            // 物理约束: 仅保留折叠共振重复后 SMA 保持物理单调梯级的排列
            if (cleaned.Count >= 2 && cleaned.Count <= 5)
            {
                var allPerms = GetPermutations(cleaned);
                foreach (var p in allPerms)
                {
                    var chain = new List<string>(p);
                    if (!result.Any(r => r.SequenceEqual(chain)) && IsSmaMonotonicLadder(chain))
                    {
                        result.Add(chain);
                        if (result.Count >= maxCombinations) break;
                    }
                }
            }

            // 3. 基于开普勒图论与天体引力势能梯度的纯数学自适应插序与共振扩展
            var depObj = SolarSystemCatalog.GetPlanet(departure);
            var arrObj = SolarSystemCatalog.GetPlanet(arrival);
            string central = depObj?.ParentName ?? "Sun";
            var nodes = KeplerAdjacencyGraphEngine.GetSystemNodes(central, departure, arrival);

            if (depObj != null && arrObj != null && nodes.Count > 0)
            {
                double muCentral = SolarSystemCatalog.GetCentralMu(central);
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);

                // 3.1 前置共振与内潜引力弹弓插序 (Inward Assist / EGA Pre-Insertion)
                // 物理法则: 若首个目标在外侧且发射能量极高，或目标位于极深势阱，寻找内侧强势阱天体作为前置跳板
                var innerAssist = nodes.Where(n => n.SMA < depObj.SMA)
                                       .OrderByDescending(n => n.GravPotential)
                                       .FirstOrDefault();

                if (innerAssist != null && arrObj.SMA > depObj.SMA * 2.0)
                {
                    foreach (var baseChain in result.Take(4).ToList())
                    {
                        if (!baseChain[0].Equals(innerAssist.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            var ins = new List<string> { innerAssist.Name };
                            ins.AddRange(baseChain);
                            if (!result.Any(r => r.SequenceEqual(ins))) result.Add(ins);

                            // 双重内潜共振
                            var ins2 = new List<string> { innerAssist.Name, innerAssist.Name };
                            ins2.AddRange(baseChain);
                            if (!result.Any(r => r.SequenceEqual(ins2))) result.Add(ins2);
                        }
                    }
                }

                // 3.2 跨体系巨行星跳板插序 (Giant Assist Insertion)
                // 物理法则: 前往远外天体时，在中途插入极大引力巨行星 (Mu 极大值)
                var giantAssist = nodes.Where(n => n.IsGiant && n.SMA > depObj.SMA && n.SMA < arrObj.SMA)
                                       .OrderByDescending(n => n.Mu)
                                       .FirstOrDefault();

                if (giantAssist != null)
                {
                    foreach (var baseChain in result.Take(4).ToList())
                    {
                        if (!baseChain.Any(p => p.Equals(giantAssist.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            var insGiant = new List<string>(baseChain) { giantAssist.Name };
                            if (!result.Any(r => r.SequenceEqual(insGiant))) result.Add(insGiant);
                        }
                    }
                }

                // 3.3 同星多次开普勒共振扩展 (Multi-Resonant Flyby Expansion)
                var resonantChains = new List<List<string>>();
                foreach (var baseChain in result.Take(8).ToList())
                {
                    for (int i = 0; i < baseChain.Count; i++)
                    {
                        string bodyName = baseChain[i];
                        var node = nodes.FirstOrDefault(n => n.Name.Equals(bodyName, StringComparison.OrdinalIgnoreCase));
                        if (node != null)
                        {
                            // 【2026-09-14 审计修正】原为裸字面量分档 `IsDeepWell ? 3 : (IsGiant ? 1 : 2)`。
                            // 现改用已命名的常量 (标注见各自声明处)。
                            int resMax = node.IsDeepWell
                                ? ExpansionResonanceOrderDeepWell
                                : (node.IsGiant ? ExpansionResonanceOrderGiant : ExpansionResonanceOrderDefault);
                            for (int n = 1; n <= resMax; n++)
                            {
                                var resChain = new List<string>(baseChain);
                                for (int k = 0; k < n; k++) resChain.Insert(i, bodyName);
                                if (!result.Any(r => r.SequenceEqual(resChain)) && !resonantChains.Any(r => r.SequenceEqual(resChain)))
                                {
                                    resonantChains.Add(resChain);
                                }
                            }
                        }
                    }
                }
                foreach (var rc in resonantChains)
                {
                    if (result.Count < maxCombinations * 2) result.Add(rc);
                }
            }

            // 严格杜绝末尾重复追加 Arrival 或开头残留 Departure
            foreach (var chain in result)
            {
                while (chain.Count > 0 && chain[chain.Count - 1].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                {
                    chain.RemoveAt(chain.Count - 1);
                }
                while (chain.Count > 0 && chain[0].Equals(departure, StringComparison.OrdinalIgnoreCase))
                {
                    chain.RemoveAt(0);
                }
            }
            result = result.Where(c => c.Count > 0).ToList();

            return result;
        }

        /// <summary>
        /// 全局通用双圆霍曼转移基准能量计算 (100% 优先委托 PykepBridge.CalcHohmannTransfer 原生 C++ 动力学)
        /// 理论物理真理：任何行星际转移的理论出射与捕获下限由两体解析公式决定
        /// </summary>
        public static (double DepHohmannKms, double TotHohmannKms) CalculateUniversalHohmannBenchmark(
            string departurePlanet, 
            string arrivalPlanet, 
            double depPeKm = 200.0, 
            double arrPeKm = 200.0)
        {
            var pDep = SolarSystemCatalog.GetPlanet(departurePlanet);
            var pArr = SolarSystemCatalog.GetPlanet(arrivalPlanet);
            if (pDep == null || pArr == null || pDep.SMA <= 0 || pArr.SMA <= 0)
            {
                return (3.8, 6.5);
            }

            double muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);
            double r1 = pDep.SMA;
            double r2 = pArr.SMA;

            // 1. 优先调用原生 PykepBridge.CalcHohmannTransfer (100% kep3 原生)
            if (PykepBridge.CalcHohmannTransfer(r1, r2, muCentral, out _, out _, out double vInfDepNative, out double vInfArrNative)) {
                double depDvNative = PykepBridge.CalculateDepartureDeltaV(vInfDepNative, pDep, depPeKm, depPeKm);
                double arrDvNative = PykepBridge.CalculateArrivalInsertionDeltaV(vInfArrNative, pArr, arrPeKm, arrPeKm, false);
                if (!double.IsNaN(depDvNative) && !double.IsNaN(arrDvNative) && depDvNative > 0 && arrDvNative > 0) {
                    return (depDvNative, depDvNative + arrDvNative);
                }
            }

            // 2. 原生接口未就绪时的解析保底 (0 冒充原生)
            double aTrans = (r1 + r2) * 0.5;
            double vDepTrans = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r1 - 1.0 / aTrans)));
            double vDepCirc = Math.Sqrt(Math.Max(0.0, muCentral / r1));
            double vInfDep = Math.Abs(vDepTrans - vDepCirc);

            double rpDep = pDep.RadiusKm + Math.Max(10.0, depPeKm);
            double vParkDep = Math.Sqrt(Math.Max(0.0, pDep.Mu / rpDep));
            double vHypDep = Math.Sqrt(Math.Max(0.0, vInfDep * vInfDep + 2.0 * pDep.Mu / rpDep));
            double depDv = Math.Max(0.0, vHypDep - vParkDep);

            double vArrTrans = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r2 - 1.0 / aTrans)));
            double vArrCirc = Math.Sqrt(Math.Max(0.0, muCentral / r2));
            double vInfArr = Math.Abs(vArrTrans - vArrCirc);

            double rpArr = pArr.RadiusKm + Math.Max(10.0, arrPeKm);
            double vParkArr = Math.Sqrt(Math.Max(0.0, pArr.Mu / rpArr));
            double vHypArr = Math.Sqrt(Math.Max(0.0, vInfArr * vInfArr + 2.0 * pArr.Mu / rpArr));
            double arrDv = Math.Max(0.0, vHypArr - vParkArr);

            return (depDv, depDv + arrDv);
        }

        /// <summary>
        /// 智能相位开普勒共振航时空间推导 (Auto-Derive Resonant Leg Bounds)
        /// 依据开普勒第三定律与能量守恒，为全链路中每一个航段自适应划定物理精确的 ToF 上下限网格：
        /// 1. 同星共振调相段 (P -> P):
        ///    航天器周期 T_sc = (p/q) * T_P，航时严密锁定在开普勒物理共振区间 [0.85 * T_P, 2.10 * T_P]
        /// 2. 异星转移段 (P1 -> P2):
        ///    按霍曼等效半长轴 a_trans = (a1 + a2)/2 动态划定物理转移弧段 [0.45 * T_H, 2.40 * T_H]
        /// </summary>
        public static List<(double MinDays, double MaxDays)> AutoDeriveResonantLegBounds(
            string departure, 
            string arrival, 
            List<string> flybyChain,
            string centralBody = "Sun",
            double depMinFactor = 0.65,
            double depMaxFactor = 1.50)
        {
            var fullChain = new List<string> { departure };
            if (flybyChain != null)
            {
                if (flybyChain.Count == 1 && flybyChain[0].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                {
                    // 纯直达航段 (用户未指定中间借力)
                }
                else
                {
                    fullChain.AddRange(flybyChain);
                }
            }
            if (!string.IsNullOrEmpty(arrival))
            {
                fullChain.Add(arrival);
            }

            var bounds = new List<(double MinDays, double MaxDays)>();
            double muCentralM = (SolarSystemCatalog.GetCentralMu(centralBody) > 0 
                ? SolarSystemCatalog.GetCentralMu(centralBody) 
                : (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu)) * 1e9;

            // 反梯级泵升深井识别: 全链最内天体是否显著深于出发天体 (需反向梯度泵升)。
            // 【2026-09-14 审计修正】原注释以内部**测试场景编号** "Scenario 9 的水星 0.387 AU"
            // 作为取值依据, 属按测试集标定; 该表述已删除, 阈值命名化并加注标定状态。
            var depObjBounds = SolarSystemCatalog.GetPlanet(departure);
            double depSmaBounds = (depObjBounds != null && depObjBounds.SMA > 0) ? depObjBounds.SMA : MGADefaults.AU_KM;
            double deepestSma = double.MaxValue;
            foreach (var bn in fullChain)
            {
                var bp = SolarSystemCatalog.GetPlanet(bn);
                if (bp != null && bp.SMA > 0) deepestSma = Math.Min(deepestSma, bp.SMA);
            }
            // 【2026-09-14 第二轮审计修正】原为绝对比例 `deepestSma < depSmaBounds * 0.45`。
            // 现改为"深入至少 N 个**系统内轨道壳层**"—— 壳层比由系统自身分布导出
            // (Sol 系 ≈ 1.588), 故对 Kerbol / JNSQ / 自建星系同样成立。
            bool deepWellExitMode = MGAPhysicalScales.ClassifyDeepWellExitByShellSteps(
                deepestSma, depSmaBounds, SolarSystemCatalog.GetAvailablePlanets());

            for (int k = 0; k < fullChain.Count - 1; k++)
            {
                string b1 = fullChain[k];
                string b2 = fullChain[k + 1];

                var p1 = SolarSystemCatalog.GetPlanet(b1);
                var p2 = SolarSystemCatalog.GetPlanet(b2);
                double sma1 = (p1 != null && p1.SMA > 0) ? p1.SMA : MGADefaults.AU_KM;
                double sma2 = (p2 != null && p2.SMA > 0) ? p2.SMA : MGADefaults.AU_KM;

                if (b1.Equals(b2, StringComparison.OrdinalIgnoreCase))
                {
                    // 同星共振调相段: 100% 由开普勒活力公式 (Vis-Viva) 与双曲偏折角极限几何数学自动推导
                    SolarPlanet pDownstream = null;
                    for (int nxt = k + 1; nxt < fullChain.Count; nxt++)
                    {
                        if (!fullChain[nxt].Equals(b1, StringComparison.OrdinalIgnoreCase))
                        {
                            pDownstream = SolarSystemCatalog.GetPlanet(fullChain[nxt]);
                            break;
                        }
                    }

                    double muCentralKmS = muCentralM * 1e-9;
                    // 【2026-09-14 修正】同样传入中央天体名, 使 "SMA 是否与该 μ 同参考系" 可被校验。
                    var (minLeg, maxLeg, _, isResFeasible) = MGAFlybyDynamicsHelper.DerivePhysicalResonantBounds(
                        p1, pDownstream, muCentralKmS, 0.0, centralBody);
                    bounds.Add((minLeg, maxLeg));
                }
                else
                {
                    // 异星转移段: 100% 由欧拉-兰伯特方程与极值动力学几何数学自动推导
                    double muCentralKmS = muCentralM * 1e-9;
                    // 【2026-09-14 修正】必须把中央天体名传入, 否则函数无法判断各天体的 SMA 是否
                    // 与该 μ 同参考系 (原地月任务的 53.8 年航时上界即由此产生)。
                    var (minLeg, maxLeg) = MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds(
                        p1, p2, muCentralKmS, k == 0, depMinFactor, depMaxFactor, centralBody);
                    bounds.Add((minLeg, maxLeg));
                }
            }

            return bounds;
        }

        /// <summary>
        /// 全域弹性 ToF 自适应推导求解器 (将 ToF 上下限全量纳入优化求解范围)
        /// 机制: 依据全链条各航段物理开普勒共振与霍曼转移弧段动态汇聚全局物理闭合弹性窗口
        /// </summary>
        public static (double MinDays, double MaxDays) CalculateAdaptiveToFBounds(
            string departure, 
            string arrival, 
            List<string> flybyChain,
            string centralBody = "Sun",
            double depMinFactor = 0.65,
            double depMaxFactor = 1.50)
        {
            var legBounds = AutoDeriveResonantLegBounds(departure, arrival, flybyChain, centralBody, depMinFactor, depMaxFactor);
            double totalMinDays = 0.0;
            double totalMaxDays = 0.0;
            foreach (var b in legBounds)
            {
                totalMinDays += b.MinDays;
                totalMaxDays += b.MaxDays;
            }

            // 安全保底与上限裕度
            totalMinDays = Math.Max(50.0, totalMinDays);
            totalMaxDays = Math.Max(totalMinDays + 150.0, totalMaxDays * 1.15);

            return (totalMinDays, totalMaxDays);
        }

        private static List<List<string>> GetPermutations(List<string> list)
        {
            var result = new List<List<string>>();
            if (list == null || list.Count == 0) return result;
            if (list.Count == 1)
            {
                result.Add(new List<string>(list));
                return result;
            }
            for (int i = 0; i < list.Count; i++)
            {
                string current = list[i];
                var remaining = new List<string>(list);
                remaining.RemoveAt(i);
                foreach (var sub in GetPermutations(remaining))
                {
                    var p = new List<string> { current };
                    p.AddRange(sub);
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>
        /// 物理单调能量梯级判定 (IJCAI 单调梯级约束)
        /// 折叠连续同名天体(同星共振重复)后，若 SMA 序列保持全片单调(非降 或 非升)，则视为物理可达的梯级排序。
        /// 拦截外行星被排到内爬升体之前的非物理跳跃(如 Cassini 的 Earth➔Jupiter➔Venus)。
        /// </summary>
        private static bool IsSmaMonotonicLadder(List<string> chain)
        {
            if (chain == null || chain.Count < 2) return true;

            var collapsed = new List<double>();
            foreach (var body in chain)
            {
                var p = SolarSystemCatalog.GetPlanet(body);
                if (p == null || p.SMA <= 0) continue;
                if (collapsed.Count == 0 || Math.Abs(collapsed[collapsed.Count - 1] - p.SMA) > 1e-6)
                {
                    collapsed.Add(p.SMA);
                }
            }

            if (collapsed.Count < 2) return true;

            bool nonDecreasing = true, nonIncreasing = true;
            for (int i = 1; i < collapsed.Count; i++)
            {
                if (collapsed[i] < collapsed[i - 1]) nonDecreasing = false;
                if (collapsed[i] > collapsed[i - 1]) nonIncreasing = false;
                if (!nonDecreasing && !nonIncreasing) return false;
            }
            return true;
        }

        /// <summary>
        /// IJCAI 2015 开普勒历表周期离散网格生成器 (Ephemeris Grid)
        /// G = { t0 + j * (l * tau_i / 360°) }
        /// 将连续搜索时间按借力天体真实公转周期 tau_i 离散化，使同星共振和借力交会自然捕捉行星对齐切点
        /// </summary>
        public static List<double> GetEphemerisGridEpochs(string bodyName, double t0, double spanDays, double resolutionDeg = 22.5)
        {
            var epochs = new List<double>();
            var pBody = SolarSystemCatalog.GetPlanet(bodyName);
            if (pBody == null || pBody.SMA <= 0) return epochs;

            string central = pBody.ParentName ?? SolarSystemCatalog.RootStarName;
            double centralMu = SolarSystemCatalog.GetCentralMu(central);
            if (centralMu <= 0) centralMu = SolarSystemCatalog.SunMu;

            double sma = pBody.SMA;
            double periodSec = 2.0 * Math.PI * Math.Sqrt(Math.Pow(sma * 1000.0, 3.0) / (centralMu * 1e9));
            double periodDays = periodSec / 86400.0;
            if (periodDays <= 0 || double.IsNaN(periodDays)) periodDays = 365.25;

            double stepDays = periodDays * (resolutionDeg / 360.0);
            stepDays = Math.Max(1.0, stepDays);

            int steps = (int)Math.Ceiling(spanDays / stepDays);
            for (int j = 0; j <= steps; j++)
            {
                epochs.Add(t0 + j * stepDays * 86400.0);
            }
            return epochs;
        }

        /// <summary>
        /// 基于行星轨道内外侧拓扑与物理约束自动广度优先生成候选借力序列 (100% 对齐 KSP-MGA-Planner 拓扑搜索算法)
        /// </summary>
        public static List<List<string>> GenerateCandidateSequences(
            string depBody, string destBody, int maxSwingBys = 6, int maxResonances = 0, int maxBackLegs = 2, int maxBackSpacing = 2)
        {
            var cfg = new MGAConfig {
                DepartureBody = depBody,
                DestinationBody = destBody,
                MaxSwingBys = maxSwingBys,
                MaxResonances = maxResonances,
                MaxBackLegs = maxBackLegs,
                MaxBackSpacing = maxBackSpacing
            };
            return GenerateCandidateSequences(cfg);
        }

        /// <summary>
        /// 出发天体自环起手 (ΔV-EGA) 的逃逸双曲过剩速度 v∞ (km/s), 由 2:1 共振泵升解析给出。
        ///
        /// 【2026-09-14 新增】取代原两处裸字面量 `double vInfDep = 3.5;`
        /// (原文注释自陈取自 "Juno/Galileo 级 ~3.5 km/s" 的历史名义 C3)。
        ///
        /// 物理: 自环需先把轨道半长轴抬到 2:1 共振轨道 a_res = a_dep · 2^(2/3),
        /// 故 v∞ = |v_trans(a_res) − v_circ(a_dep)| (Vis-Viva)。
        /// 对地球出发解得 ~3.21 km/s, 与原取值同量级但不再引用任何任务清单。
        /// </summary>
        internal static double SelfResonanceEgaVInfKms(double muCentral, double depSmaKm) {
            if (muCentral <= 0 || depSmaKm <= 0) return 0.0;
            double aResKm = depSmaKm * Math.Pow(2.0, 2.0 / 3.0);
            return MGAPhysicalScales.HohmannVInfKms(muCentral, depSmaKm, aResKm);
        }

        /// <summary>
        /// 判定天体是否构成"有效引力助推源" (无量纲判据)。
        ///
        /// 【2026-09-14 新增】取代原 `p.Mu >= 500.0` (BFS 天体过滤) 与
        /// `destPlanet.Mu >= 1000.0` (目标天体强引力判定) 两个绝对阈值。
        ///
        /// 判据: 借力功率指数 rho_assist = delta_v_fb / v_M >= DefaultMinAssistPowerIndex。
        /// 即该天体单次无动力飞掠的日心速度增益, 至少要达到当地公转速度的 1%
        /// (否则贡献会被转移弧段的 Lambert 时间自由度完全吸收, 数值上不可辨识)。
        /// 到达 v_inf 以参考出发天体的霍曼标称值估算。
        /// </summary>
        /// <param name="body">待判定天体</param>
        /// <param name="referenceDepSmaKm">参考出发天体的日心半长轴 (km)</param>
        /// <param name="muCentral">中心天体引力常数 (km^3/s^2)</param>
        public static bool IsEffectiveAssistSource(SolarPlanet body, double referenceDepSmaKm, double muCentral) {
            if (body == null || body.Mu <= 0 || body.SMA <= 0 || referenceDepSmaKm <= 0 || muCentral <= 0) return false;
            double vInf = MGAPhysicalScales.HohmannVInfKms(muCentral, referenceDepSmaKm, body.SMA);
            return MGAPhysicalScales.AssistPowerIndex(body, Math.Max(vInf, 1e-3), muCentral)
                   >= MGAPhysicalScales.DefaultMinAssistPowerIndex;
        }

        public static List<List<string>> GenerateCandidateSequences(MGAConfig config) {
            var results = new List<List<string>>();
            if (config == null || string.IsNullOrEmpty(config.DepartureBody) || string.IsNullOrEmpty(config.DestinationBody))
                return results;

            string origin = config.DepartureBody;
            string dest = config.DestinationBody;

            // BFS 评分/剪枝所需的中心天体引力常数 (由配置中心天体解析, 取代隐式假设太阳系)。
            // 必须在任何使用点之前声明 (局部变量需先声明后使用)。
            double bfsMuCentral = SolarSystemCatalog.GetCentralMu(config.CentralBody ?? "Sun");
            if (bfsMuCentral <= 0) bfsMuCentral = SolarSystemCatalog.SunMu;

            // 出发与目标天体相同时无星际转移物理意义，直接返回空列表，杜绝无向 BFS 指数级爆炸与主线程卡死
            if (origin.Equals(dest, StringComparison.OrdinalIgnoreCase)) return results;

            var allPlanets = SolarSystemCatalog.GetAvailablePlanets();
            if (allPlanets == null || allPlanets.Count == 0) return results;

            // 过滤有效引力助推天体：
            // 【2026-09-14 审计修正】原判据 `p.Mu >= 500.0` 是以 km^3/s^2 标定的**绝对阈值**,
            // 且注释以"确保火星与金星排在队列首位"说明其被调过。现改用与
            // KeplerAdjacencyGraphEngine.GetSystemNodes 完全一致的**无量纲借力功率指数**判据:
            // 该天体单次无动力飞掠可提供的日心速度增益, 占当地公转速度的比例是否达到可辨识下限
            // (MGAPhysicalScales.DefaultMinAssistPowerIndex)。该判据对任意恒星系与单位制成立,
            // 可同样剔除 Ryugu / Eros / Bennu / Ceres 等微型天体, 且不再有 Sol 系专用阈值。
            var refDepPlanet = SolarSystemCatalog.GetPlanet(origin);
            double refDepSmaKm = (refDepPlanet != null && refDepPlanet.SMA > 0) ? refDepPlanet.SMA : MGADefaults.AU_KM;
            var planetList = allPlanets.Where(p =>
                !string.IsNullOrEmpty(p.Name) &&
                (p.Name.Equals(origin, StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Equals(dest, StringComparison.OrdinalIgnoreCase) ||
                 IsEffectiveAssistSource(p, refDepSmaKm, bfsMuCentral))
            ).ToList();

            var bodyIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < planetList.Count; i++) {
                if (!string.IsNullOrEmpty(planetList[i].Name) && !bodyIndexMap.ContainsKey(planetList[i].Name)) {
                    bodyIndexMap[planetList[i].Name] = i;
                }
            }

            if (!bodyIndexMap.TryGetValue(origin, out int oIdx) || !bodyIndexMap.TryGetValue(dest, out int dIdx)) {
                return results;
            }

            var queue = new Queue<(List<string> chain, int resonances, int backLegs)>();
            queue.Enqueue((new List<string> { origin }, 0, 0));

            int maxLegs = config.MaxSwingBys + 1;
            int visitedCount = 0;

            var destPlanet = SolarSystemCatalog.GetPlanet(dest);
            // 【2026-09-14 审计修正】原判据 `destPlanet.Mu >= 1000.0` 为绝对阈值, 现改用同一无量纲判据。
            bool destHasStrongGravity = (destPlanet != null && IsEffectiveAssistSource(destPlanet, refDepSmaKm, bfsMuCentral));

            while (queue.Count > 0) {
                // 安全熔断上限，严防异常分支造成游戏卡死
                if (++visitedCount > MGADefaults.MaxBfsVisitedCount || results.Count >= MGADefaults.MaxCandidateResults) break;

                var (currentChain, curRes, curBack) = queue.Dequeue();
                string currentBody = currentChain.Last();
                if (!bodyIndexMap.TryGetValue(currentBody, out int cIdx)) cIdx = -1;

                if (currentChain.Count > 1 && currentBody.Equals(dest, StringComparison.OrdinalIgnoreCase)) {
                    var subChain = new List<string>(currentChain.Skip(1));
                    if (!results.Any(r => r.SequenceEqual(subChain))) {
                        results.Add(subChain);
                    }

                    // 若目标天体自身具备显著引力 (如水星/火星/金星/地球/木星 Mu >= 1000) 且允许共振 (MaxResonances > 0)，
                    // 继续压入目标天体自身同星共振借力分支 (如 Earth -> Venus -> Mercury -> Mercury -> Mercury)，
                    // 彻底对齐 NASA MESSENGER / ESA BepiColombo 水星极低 ΔV 连续减速泊入历史经典任务！
                    if (destHasStrongGravity && (config.MaxResonances <= 0 || curRes < config.MaxResonances) && currentChain.Count < maxLegs + 1) {
                        var resonantChain = new List<string>(currentChain) { dest };
                        queue.Enqueue((resonantChain, curRes + 1, curBack));
                    }
                    continue;
                }

                if (currentChain.Count >= maxLegs + 1) continue;

                for (int i = 0; i < planetList.Count; i++) {
                    var pNext = planetList[i];
                    string nextBody = pNext.Name;
                    if (string.IsNullOrEmpty(nextBody)) continue;

                    bool isResonance = nextBody.Equals(currentBody, StringComparison.OrdinalIgnoreCase);
                    if (isResonance && config.MaxResonances > 0 && curRes >= config.MaxResonances) continue;

                    bool isBackLeg = (dIdx > oIdx) ? (i < cIdx) : (i > cIdx);
                    if (isBackLeg && curBack >= config.MaxBackLegs) continue;

                    int spacing = Math.Abs(i - cIdx);
                    if (isBackLeg && spacing > config.MaxBackSpacing) continue;

                    // Contraction 剪枝门禁 (IJCAI 2015): 凡二体转移或转角赤字超限的枝节点立即剪除整颗子树
                    if (!isResonance) {
                        var pCur = SolarSystemCatalog.GetPlanet(currentBody);
                        if (pCur != null && pNext != null && pCur.SMA > 0 && pNext.SMA > 0) {
                            // 单弧可达性 (能量判据, 无量纲):
                            //   所需 v_inf = 该弧段的霍曼双曲过剩速度;
                            //   可获得上限 = 两端天体无动力飞掠增益之和 × 接纳倍数。
                            // 若所需已超出可获得上限, 该枝节点物理不可达, 剪除整颗子树。
                            // 取代原 `半长轴比 > 6 倍 && Mu < 1e5` 的几何/质量双阈值。
                            double arcVInf = MGAPhysicalScales.HohmannVInfKms(bfsMuCentral,
                                Math.Min(pCur.SMA, pNext.SMA), Math.Max(pCur.SMA, pNext.SMA));
                            double gainMax = MGAPhysicalScales.AssistGainKms(pCur, Math.Max(arcVInf, 1e-3))
                                           + MGAPhysicalScales.AssistGainKms(pNext, Math.Max(arcVInf, 1e-3));
                            if (arcVInf > gainMax * DeficitAcceptanceMultiple) {
                                continue;
                            }
                        }
                    }

                    var newChain = new List<string>(currentChain) { nextBody };
                    queue.Enqueue((newChain, curRes + (isResonance ? 1 : 0), curBack + (isBackLeg ? 1 : 0)));
                }
            }

            // 确定性 Max 估计器 (IJCAI 2015) 与 AlphaZero Policy-Value 神经网络价值引导器协同评分
            double maxBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 4000.0;
            results = results.OrderByDescending(c => {
                if (c.Count == 1 && c[0].Equals(dest, StringComparison.OrdinalIgnoreCase)) return 0.5;

                double totalDeficitKms = 0.0;
                for (int idx = 0; idx < c.Count - 1; idx++) {
                    var pA = SolarSystemCatalog.GetPlanet(c[idx]);
                    var pB = SolarSystemCatalog.GetPlanet(c[idx + 1]);
                    if (pA != null && pB != null && pA.SMA > 0 && pB.SMA > 0) {
                        if (!c[idx].Equals(c[idx + 1], StringComparison.OrdinalIgnoreCase)) {
                            // 单弧能量赤字 (km/s): 所需 v_inf 超出起始天体可提供引力增益的部分。
                            // 取代原 `(半长轴比 - 4.0) * 1.5` 的经验换算。
                            double arcVInf = MGAPhysicalScales.HohmannVInfKms(bfsMuCentral,
                                Math.Min(pA.SMA, pB.SMA), Math.Max(pA.SMA, pB.SMA));
                            double gainA = MGAPhysicalScales.AssistGainKms(pA, Math.Max(arcVInf, 1e-3));
                            totalDeficitKms += Math.Max(0.0, arcVInf - gainA);
                        }
                    }
                }
                // 发射注入冲量由出发/首个借力天体的开普勒势阱解析推导 (取代硬编码 3800 m/s)
                double estimatedTotalMs = MGAPlanetaryAssistManager.EstimateLaunchInjectionDvMs(
                    origin, c.Count > 0 ? c[0] : null) + totalDeficitKms * 1000.0;
                double xScore = Math.Max(0.0, (maxBudgetMs - estimatedTotalMs) / maxBudgetMs);

                // 融合 Policy-Value 神经网络序列成单价值评分
                double pvValue = MGAPolicyValueGuide.Instance.EvaluateSequenceValue(
                    c, dest, estimatedTotalMs / 1000.0, maxBudgetMs / 1000.0);

                return 0.5 * xScore + 0.5 * pvValue;
            }).ToList();

            return results;
        }
    }
}
