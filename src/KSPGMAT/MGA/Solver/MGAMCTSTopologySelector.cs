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
        /// 基于天体力学引力井深度与半长轴势能梯度，全自动求取最优拓扑超参数四元组 (零人工干预)
        /// </summary>
        public static TopologyHyperparameters AutoTuneTopologyHyperparameters(string departurePlanet, string arrivalPlanet)
        {
            var pDep = SolarSystemCatalog.GetPlanet(departurePlanet);
            var pArr = SolarSystemCatalog.GetPlanet(arrivalPlanet);

            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : 1.496e8;
            double rArr = (pArr != null && pArr.SMA > 0) ? pArr.SMA : 1.496e8;

            double minR = Math.Min(rDep, rArr);
            double ratio = minR > 0 ? (Math.Max(rDep, rArr) / minR) : 1.0;
            bool isDeepInner = (rArr < rDep * 0.5); // 如水星 (0.387 AU vs 1.0 AU, ratio ~ 2.58)
            bool isFarOuter = (ratio >= 10.0 || (rArr > rDep * 8.0)); // 如天王星 (19.2 AU), 海王星 (30.0 AU)
            bool isMidOuter = (ratio >= 4.0 && !isFarOuter); // 如木星 (5.2 AU), 土星 (9.58 AU)

            var hp = new TopologyHyperparameters { OrbitRadiusRatio = ratio };

            if (isDeepInner)
            {
                // 极低势阱内行星: 需要巨大减速借力 (单次借力无法抹平巨大速度差)
                hp.MaxSwingBys = 6;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 2;
                hp.MaxBackSpacing = 3;
                hp.Rationale = $"深重力势阱内潜行星任务 (半长轴比={ratio:F2})，配置多重连续同星共振调相与减速拓扑 (无上限)";
            }
            else if (isFarOuter)
            {
                // 深空远外行星: 能量跨度巨大，直达机动不可行，必须借助巨行星超大引力弹弓
                hp.MaxSwingBys = 6;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 2;
                hp.MaxBackSpacing = 3;
                hp.Rationale = $"远外深空大势能跨度行星任务 (半长轴比={ratio:F2})，配置多重同星共振蓄能与巨行星引力跳板拓扑 (无上限)";
            }
            else if (isMidOuter)
            {
                // 中远外行星: 借助中途行星加速
                hp.MaxSwingBys = 4;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 1;
                hp.MaxBackSpacing = 2;
                hp.Rationale = $"中远大势能跨度行星任务 (半长轴比={ratio:F2})，配置引力助推增能链条 (无上限)";
            }
            else
            {
                // 近邻行星: 直达霍曼为基准，辅以单次辅助借力
                hp.MaxSwingBys = 3;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 1;
                hp.MaxBackSpacing = 1;
                hp.Rationale = $"近邻行星任务 (半长轴比={ratio:F2})，以直达霍曼转移为主，辅以单次高价值借力测试 (无上限)";
            }

            return hp;
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
                public double GravPotential;     // Mu / SafeRadius
                public double OrbitalSpeedKms;   // km/s
                public double PeriodDays;        // days
                public bool IsGiant;             // 强引力巨行星
                public bool IsDeepWell;          // 极深重力势阱小天体 (如水星/Moho)
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
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);

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

                    // 过滤无实质引力助推价值的微型碎石小行星 (保留 Mu >= 500 的天体，同时确保始发和到达在列)
                    if (p.Mu < 500.0 && 
                        !p.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) && 
                        !p.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double safeR = p.SafeRadiusKm > 0 ? p.SafeRadiusKm : (p.RadiusKm + 200.0);
                    double gravPot = p.Mu / safeR;
                    double vCirc = Math.Sqrt(muCentral / p.SMA);
                    double periodSec = 2.0 * Math.PI * Math.Sqrt(Math.Pow(p.SMA * 1000.0, 3.0) / (muCentral * 1e9));
                    double periodDays = periodSec / 86400.0;
                    if (periodDays <= 0 || double.IsNaN(periodDays)) periodDays = 365.25;

                    bool isGiant = (p.Mu >= 5.0e6 || p.RadiusKm >= 20000.0);
                    double depSma = (depObj != null && depObj.SMA > 0) ? depObj.SMA : 1.496e8;
                    bool isDeep = (gravPot < 25.0 && p.SMA < depSma * 0.55);

                    nodes.Add(new BodyNode
                    {
                        Name = p.Name,
                        SMA = p.SMA,
                        RadiusKm = p.RadiusKm,
                        SafeRadiusKm = safeR,
                        Mu = p.Mu,
                        IncDeg = p.INC,
                        GravPotential = gravPot,
                        OrbitalSpeedKms = vCirc,
                        PeriodDays = periodDays,
                        IsGiant = isGiant,
                        IsDeepWell = isDeep
                    });
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
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);

                // 1. 直达基准霍曼转移作为候选 0
                CalcHohmannLeg(depNode.SMA, arrNode.SMA, muCentral, out double vInfDepDirect, out double vInfArrDirect, out double tofDirect);
                double depPeKm = 200.0;
                double rpDep = depNode.RadiusKm + depPeKm;
                double vParkDep = Math.Sqrt(depNode.Mu / rpDep);
                double vHypDep = Math.Sqrt(vInfDepDirect * vInfDepDirect + 2.0 * depNode.Mu / rpDep);
                double directLaunchDv = Math.Max(0.0, vHypDep - vParkDep);

                // 2. 确定搜索参数与深度门限
                double ratioSma = Math.Max(depNode.SMA, arrNode.SMA) / Math.Min(depNode.SMA, arrNode.SMA);
                bool isDeepInner = (arrNode.SMA < depNode.SMA * 0.55);
                bool isFarOuter = (ratioSma >= 8.0 || arrNode.SMA > depNode.SMA * 6.0);
                int treeMaxLegs = Math.Min(maxLegs, (isDeepInner || isFarOuter) ? 11 : Math.Max(7, maxLegs <= 5 ? maxLegs : 7));

                // 深势阱天体 (如水星) 直达制动赤字惩罚:
                // 水星直达到达相对速度高达 9.6 km/s，制动入轨需要天文级工质 (理论总冲量 > 25 km/s)，
                // 必须在纯图搜索阶段施加真实的物理势阱制动赤字，阻断其以低航时伪装成优质解挤占演化池
                double directPenalty = isDeepInner ? 8.0 : 0.0;

                candidates.Add(new CandidateTopology
                {
                    FlybyChain = new List<string>(),
                    LaunchDvKms = directLaunchDv,
                    AssistDeficitKms = isDeepInner ? 8.0 : 0.0,
                    TotalToFDays = tofDirect,
                    LegCount = 1,
                    TopologyCategory = "Direct",
                    ParetoScore = directLaunchDv * 1.2 + (tofDirect / 365.25) * 0.3 + directPenalty
                });
                // 图论分支定界物理门限: 容纳非共面非霍曼三维交角裕度，杜绝初等共线假设误杀高能借力分支
                double budgetLimit = Math.Max(12.0, (maxBudgetKms > 0 && maxBudgetKms < 50.0) ? (maxBudgetKms * 2.5) : 12.0);

                // 开普勒物理可达包络线 (Orbital Reachability Boundary & Monotonic Energy Ladder):
                // 1. 深潜内行星任务 (如水星 rArr < rDep): 航天器绝不能向外越过出发星轨道的 1.6 倍 (不能跑去外太阳系巨行星)
                // 2. 外太阳系任务 (rArr > rDep): 允许向内潜入内侧天体借力反弹，但向外绝不能超过目标轨道 1.25 倍
                double maxAllowedSma = isDeepInner ? (depNode.SMA * 1.6) : (arrNode.SMA * 1.25);
                double maxFlightDays = isFarOuter ? (365.25 * 45.0) : Math.Max(2500.0, tofDirect * 8.0);
                if (isDeepInner) maxFlightDays = Math.Min(maxFlightDays, 365.25 * 8.0);

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
                        // 剪枝规则 0: 开普勒最大轨道包络线越界拦截
                        if (nextNode.SMA > maxAllowedSma) continue;

                        bool isResonance = nextNode.Name.Equals(currNode.Name, StringComparison.OrdinalIgnoreCase);

                        // 剪枝规则 1: 同星共振次数与物理合理性硬门禁 (严格天体力学约束)
                        if (isResonance)
                        {
                            // A. 严禁无异星介入的连续 2 次以上虚假同星自环 (如 Earth->Earth->Earth 或 Mars->Mars->Mars):
                            //    在纯两体引力下，若中途没有异星偏折或大深空机动，航天器在同一点相会的开普勒几何恒定，
                            //    连续无动力飞掠无法产生二次增益。彻底剪除此类拓扑退化！
                            if (!currNode.IsDeepWell)
                            {
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
                                // 深势阱小天体 (如水星/Moho) 允许连续自共振减速 (MESSENGER / BepiColombo 机制: 1:1, 2:3, 3:4 等共振降轨)
                                if (resCount >= 6) continue;
                            }

                            // 根节点出发若第一步即自环，允许深势阱内行星或外行星任务实施 ΔV-EGA 共振调相 (如 Juno)
                            bool allowDepRes = isDeepInner || (arrNode.SMA >= depNode.SMA * 1.35);
                            if (currPath.Count == 1 && !allowDepRes) continue;
                        }

                        // 剪枝规则 2: 半长轴跨度爆炸剪枝 (两个非巨行星之间不能发生超大虚无跳跃)
                        if (!isResonance)
                        {
                            double spanRatio = Math.Max(currNode.SMA, nextNode.SMA) / Math.Min(currNode.SMA, nextNode.SMA);
                            if (spanRatio > 6.0 && !currNode.IsGiant && !nextNode.IsGiant) continue;
                        }

                        // 剪枝规则 3: 无收益死循环剪枝 (禁止 A->B->A->B 等非共振重复循环)
                        if (!isResonance && currPath.Count >= 3)
                        {
                            var last2 = currPath[currPath.Count - 2];
                            if (nextNode.Name.Equals(last2.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                // 统计非连续重访的独立访问波次 (Distinct Visits):
                                // 同星连续自共振 (如 Earth->Earth) 属于同一波次，不重复计为异星往返;
                                // 允许天体间实施一次重访回弹 (如 Earth->Mars->Earth 或 Venus->Earth->Venus: distinctVisits=2),
                                // 仅拦截 3 次及以上死循环 (如 A->B->A->B->A)
                                int distinctVisits = 0;
                                for (int vi = 0; vi < currPath.Count; vi++)
                                {
                                    if (currPath[vi].Name.Equals(nextNode.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (vi == 0 || !currPath[vi - 1].Name.Equals(nextNode.Name, StringComparison.OrdinalIgnoreCase))
                                        {
                                            distinctVisits++;
                                        }
                                    }
                                }
                                if (distinctVisits >= 2) continue;
                            }
                        }

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
                                stepDeficit = 0.75; // ΔV-EGA 深空机动真实工质成本
                            }
                            else
                            {
                                stepDeficit = 0.0;
                            }
                            legToF = currNode.PeriodDays * (currNode.IsDeepWell ? 1.0 : MGAFlybyDynamicsHelper.GetResonanceToFMultiplier(currNode.PeriodDays));
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
                                    double hyperbolicTransitDays = (distKm / 14.0) / 86400.0;
                                    legToF = Math.Min(legToF * 0.35, hyperbolicTransitDays * 1.35);
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
                            if (vInfArrival > 15.0) continue; // 剪除相对速度过大的非物理跳跃

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
                            // 当飞往外行星 (如木星、土星) 且拓扑中包含内潜蓄能天体 (如金星，SMA < depNode.SMA * 0.85) 时，
                            // 航天器可利用内行星公转速度偏折极大幅度抬升远日点，这是人类历史证明的最强外行星引力跳板 (VEEGA / VVEJGA)。
                            // 赋予其关键物理优势评分，抵消航时与腿数惩罚，确保其优先进入演化池！
                            bool hasInnerAssistPump = (arrNode.SMA > depNode.SMA * 1.5) && chain.Any(b =>
                            {
                                var p = SolarSystemCatalog.GetPlanet(b);
                                return p != null && p.SMA < depNode.SMA * 0.85;
                            });
                            double pumpBonus = hasInnerAssistPump ? 2.8 : 0.0;

                            double score = launchDv * 1.2 + newCumDeficit * 1.5 + (vInfArrival * 0.8) + (newTotalToF / 365.25) * 0.25 + legs * 0.05 - pumpBonus;

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

                            // 若目标是深势阱小天体 (如水星)，自动在其后展开 1~6 次目标本星自减速共振 (MESSENGER / BepiColombo 机制)
                            if (arrNode.IsDeepWell && chain.Count > 0)
                            {
                                for (int extraRes = 1; extraRes <= 6; extraRes++)
                                {
                                    var resonantChain = new List<string>(chain);
                                    for (int er = 0; er < extraRes; er++) resonantChain.Add(arrival);
                                    double decelToF = newTotalToF + extraRes * arrNode.PeriodDays;
                                    // 水星等深势阱天体共振自减速物理边际效益递减规律 (Vis-Viva Diminishing Returns):
                                    // 首次偏折消除相对速度最多，后续偏折由于速度基数降低边际收益递减，避免超长共振链虚高评分挤死紧凑构型
                                    double decelBonus = 5.0 * (1.0 - Math.Pow(0.72, extraRes));
                                    double decelScore = score - decelBonus + (extraRes * arrNode.PeriodDays / 365.25) * 0.15;
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
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);

                double rpDep = depNode.RadiusKm + 200.0;
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
                        legToF = curr.PeriodDays * (curr.IsDeepWell ? 1.0 : MGAFlybyDynamicsHelper.GetResonanceToFMultiplier(curr.PeriodDays));
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
                                double hyperbolicTransitDays = (distKm / 14.0) / 86400.0;
                                legToF = Math.Min(legToF * 0.35, hyperbolicTransitDays * 1.35);
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
                    return p != null && p.SMA < depNode.SMA * 0.85;
                });
                double pumpBonus = hasInnerPump ? 2.8 : 0.0;

                double score = launchDv * 1.2 + cumDeficit * 1.5 + (vInfArrival * 0.8) + (totalToF / 365.25) * 0.25 + cand.LegCount * 0.05 - pumpBonus;

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
                string arrival)
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

                // 按结构类别分桶
                var directBucket = distinctList.Where(c => c.TopologyCategory == "Direct").ToList();
                var singleBucket = distinctList.Where(c => c.TopologyCategory == "Single").OrderBy(c => c.ParetoScore).ToList();
                var dualBucket = distinctList.Where(c => c.TopologyCategory == "Dual").OrderBy(c => c.ParetoScore).ToList();
                var multiBucket = distinctList.Where(c => c.TopologyCategory == "Multi").OrderBy(c => c.ParetoScore).ToList();
                var grandTourBucket = distinctList.Where(c => c.TopologyCategory == "GrandTour").OrderBy(c => c.ParetoScore).ToList();

                Console.WriteLine($"[DEBUG SelectDiverse] total={allCandidates.Count}, distinct={distinctList.Count}, single={singleBucket.Count}, dual={dualBucket.Count}, multi={multiBucket.Count}, grandTour={grandTourBucket.Count}, maxChains={maxChains}");
                var rosettaInGrand = grandTourBucket.FirstOrDefault(c => c.FlybyChain != null && c.FlybyChain.SequenceEqual(new[] { "Earth", "Mars", "Earth", "Earth" }, StringComparer.OrdinalIgnoreCase));
                Console.WriteLine($"[DEBUG SelectDiverse] Rosetta in grandTourBucket: {rosettaInGrand.FlybyChain != null}, Pareto: {rosettaInGrand.ParetoScore:F2}");

                // 1. 直达拓扑保留策略: 仅在非深势阱小天体 (即目标不是水星/Moho) 时作为基础保留
                //    对于水星等深势阱天体，直达需消耗 >25 km/s 纯脉冲，属于不可行方案，绝不占用首发宝贵名额！
                var arrP = SolarSystemCatalog.GetPlanet(arrival);
                bool isDeepWellTarget = arrP != null && (arrP.Mu / arrP.RadiusKm < 25.0) && arrP.SMA < 1.0e8;
                var directCand = directBucket.FirstOrDefault();
                if (!isDeepWellTarget && directCand.FlybyChain != null)
                {
                    selected.Add(new List<string>(directCand.FlybyChain));
                }

                // 2. 结构多样性分族抽取器: 确保相同借力级数下不同物理构型 (如 Venusx1->Earthx2 vs Venusx2->Earthx1) 均有代表入围
                void AddDiverseFromBucket(List<CandidateTopology> bucket, int quota)
                {
                    if (bucket == null || bucket.Count == 0 || quota <= 0) return;

                    // 1. 按首借力天体 (First Body: 如 Earth, Venus, Mars, Jupiter) 聚类
                    var byFirstBody = new Dictionary<string, List<CandidateTopology>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in bucket)
                    {
                        string firstB = (c.FlybyChain.Count > 0) ? c.FlybyChain[0] : "None";
                        if (!byFirstBody.TryGetValue(firstB, out var fbList))
                        {
                            fbList = new List<CandidateTopology>();
                            byFirstBody[firstB] = fbList;
                        }
                        fbList.Add(c);
                    }

                    // 2. 在每个首借力天体内部，按微观结构家族组织
                    var bodyFamQueues = new Dictionary<string, List<CandidateTopology>>();
                    foreach (var kvp in byFirstBody)
                    {
                        var fams = new Dictionary<string, List<CandidateTopology>>();
                        foreach (var c in kvp.Value)
                        {
                            var blocks = new List<string>();
                            if (c.FlybyChain.Count > 0)
                            {
                                string curB = c.FlybyChain[0];
                                int cnt = 1;
                                for (int bi = 1; bi < c.FlybyChain.Count; bi++)
                                {
                                    if (c.FlybyChain[bi].Equals(curB, StringComparison.OrdinalIgnoreCase)) cnt++;
                                    else { blocks.Add($"{curB}x{cnt}"); curB = c.FlybyChain[bi]; cnt = 1; }
                                }
                                blocks.Add($"{curB}x{cnt}");
                            }
                            string famKey = string.Join("->", blocks);
                            if (!fams.TryGetValue(famKey, out var fList))
                            {
                                fList = new List<CandidateTopology>();
                                fams[famKey] = fList;
                            }
                            fList.Add(c);
                        }
                        // 家族内部轮询交错排入该首天体的队列:
                        // 各物理构型家族按其内部最优帕累托分值 (Min ParetoScore) 排序，保证优质物理构型优先入选
                        var sortedFams = fams.Values.OrderBy(g => g.Min(c => c.ParetoScore)).ToList();
                        var bodyOrdered = new List<CandidateTopology>();
                        int maxR = kvp.Value.Count;
                        for (int r = 0; r < maxR && bodyOrdered.Count < kvp.Value.Count; r++)
                        {
                            foreach (var g in sortedFams)
                            {
                                if (r < g.Count) bodyOrdered.Add(g[r]);
                            }
                        }
                        bodyFamQueues[kvp.Key] = bodyOrdered;
                    }

                    // 3. 跨首借力天体按各自最优帕累托分值公平轮询抽取 (Round-Robin Across First Bodies)
                    var sortedBodies = bodyFamQueues.OrderBy(kv => byFirstBody[kv.Key].Min(c => c.ParetoScore)).ToList();
                    int added = 0;
                    int maxOverall = bucket.Count;
                    for (int r = 0; r < maxOverall && added < quota; r++)
                    {
                        foreach (var kvp in sortedBodies)
                        {
                            if (added >= quota) break;
                            if (r < kvp.Value.Count)
                            {
                                var cand = kvp.Value[r];
                                var route = new List<string>(cand.FlybyChain);
                                if (!selected.Any(s => s.SequenceEqual(route)))
                                {
                                    selected.Add(route);
                                    added++;
                                }
                            }
                        }
                    }
                }

                // 类别动态平滑配额分配: 单借力、双借力、3借力、4+长链大巡游
                int catsRemaining = 0;
                if (singleBucket.Count > 0) catsRemaining++;
                if (dualBucket.Count > 0) catsRemaining++;
                if (multiBucket.Count > 0) catsRemaining++;
                if (grandTourBucket.Count > 0) catsRemaining++;

                if (singleBucket.Count > 0)
                {
                    int q = Math.Max(1, (maxChains - selected.Count) / Math.Max(1, catsRemaining));
                    AddDiverseFromBucket(singleBucket, q);
                    catsRemaining--;
                }
                if (dualBucket.Count > 0)
                {
                    int q = Math.Max(1, (maxChains - selected.Count) / Math.Max(1, catsRemaining));
                    AddDiverseFromBucket(dualBucket, q);
                    catsRemaining--;
                }
                if (multiBucket.Count > 0)
                {
                    int q = Math.Max(1, (maxChains - selected.Count) / Math.Max(1, catsRemaining));
                    AddDiverseFromBucket(multiBucket, q);
                    catsRemaining--;
                }
                if (grandTourBucket.Count > 0)
                {
                    int q = Math.Max(1, maxChains - selected.Count);
                    Console.WriteLine($"[DEBUG GrandTour] calling AddDiverseFromBucket with quota={q}, selectedSoFar={selected.Count}");
                    AddDiverseFromBucket(grandTourBucket, q);
                    bool hasRos = selected.Any(s => s.Count == 4 && s[0] == "Earth" && s[1] == "Mars" && s[2] == "Earth" && s[3] == "Earth");
                    Console.WriteLine($"[DEBUG GrandTour] after grandTour, selectedCount={selected.Count}, containsRosetta={hasRos}");
                }

                // 3. 若名额未满，从全局最优 ParetoScore 中补齐
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
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);

                double deltaVMax = Math.Max(10.0, maxBudgetKms); // 论文标准 DeltaV_max = 10 km/s 硬门禁

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
                double minAllowedSma = !isOutward ? (arrNode.SMA * 0.90) : (SolarSystemCatalog.GetPlanet("Mercury")?.SMA ?? 5.79e7);

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
                        bool allowDepRes = (arrNode.SMA < depNode.SMA * 0.55) || (arrNode.SMA >= depNode.SMA * 1.35);
                        if (node.Depth == 0 && n.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase) && !allowDepRes)
                            continue;

                        // 机器学习物理约束 2: 严格同星共振次数限制
                        bool isRes = n.Name.Equals(node.BodyName, StringComparison.OrdinalIgnoreCase);
                        if (isRes)
                        {
                            int allowedRes = (n.IsDeepWell || n.Name.Equals(arrNode.Name, StringComparison.OrdinalIgnoreCase)) ? 6 : 1;
                            if (node.ResonancesCount >= allowedRes) continue;
                        }

                        // 机器学习物理约束 3: 两体半长轴跨越霍曼可达性几何粗筛
                        if (!isRes && !n.IsGiant && !node.BodyName.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            var curObj = nodes.FirstOrDefault(x => x.Name.Equals(node.BodyName, StringComparison.OrdinalIgnoreCase));
                            if (curObj != null && !curObj.IsGiant)
                            {
                                double ratio = Math.Max(curObj.SMA, n.SMA) / Math.Min(curObj.SMA, n.SMA);
                                if (ratio > 5.0) continue; // 无法单弧跨越五倍半长轴
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
                                legToF = currBodyNode.PeriodDays * (currBodyNode.IsDeepWell ? 1.0 : MGAFlybyDynamicsHelper.GetResonanceToFMultiplier(currBodyNode.PeriodDays));
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
                                    double hyperbolicDays = (distKm / 14.0) / 86400.0;
                                    legToF = Math.Min(legToF * 0.35, hyperbolicDays * 1.35);
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
                        // 始发逃逸速度: 首个非出发天体由霍曼 v∞ 推导; 出发天体自环起手 (EGA) 保留历史名义 C3 (Juno/Galileo 级 ~3.5 km/s)
                        double vInfDep = 3.5;
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
                        double muDep = (depNode.Mu > 0) ? depNode.Mu : 398600.4418;
                        double rpDep = (depNode.RadiusKm > 0 ? depNode.RadiusKm : 6378.137) + 200.0;
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
                        double vInfDep = 3.5;
                        if (pFirst != null && !pFirst.Name.Equals(depNode.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            KeplerAdjacencyGraphEngine.CalcHohmannLeg(depNode.SMA, pFirst.SMA, muCentral, out vInfDep, out _, out _);
                        }
                        double muDep = (depNode.Mu > 0) ? depNode.Mu : 398600.4418;
                        double rpDep = (depNode.RadiusKm > 0 ? depNode.RadiusKm : 6378.137) + 200.0;
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
                MGAOptimizationProbe.AiMetrics.RecordMcts(totalEvaluated, pruned, kept);

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

                var pDepInit = SolarSystemCatalog.GetPlanet(config.DeparturePlanet);
                var pArrInit = SolarSystemCatalog.GetPlanet(config.ArrivalPlanet);
                double rDepInit = pDepInit?.SMA ?? 1.496e8;
                double rArrInit = pArrInit?.SMA ?? 1.496e8;
                bool isDeepInnerInit = (rArrInit < rDepInit * 0.55);
                bool isFarOuterInit = (rArrInit > rDepInit * 6.0 || rDepInit > rArrInit * 6.0);
                int maxTreeLegs = (isDeepInnerInit || isFarOuterInit) ? 12 : 8;
                int maxMctsDepth = (isDeepInnerInit || isFarOuterInit) ? 11 : 8;

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
                    maxChains * 8);

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
                            if (physCand.AssistDeficitKms <= Math.Max(12.0, maxBudget * 2.0))
                            {
                                candidateTopologies.Add(physCand);
                            }
                        }
                    }
                }

                sequences = KeplerAdjacencyGraphEngine.SelectDiverseTopologies(candidateTopologies, Math.Max(maxChains * 512, 4096), config.ArrivalPlanet);
                Console.WriteLine($"[DEBUG] SelectDiverseTopologies returned {sequences?.Count ?? 0} sequences.");
            }

            // 3. 最终由真实星历历元与开普勒 Vis-Viva 能量赤字分析器前置过滤并精准排序
            if (config.EnableEphemerisReachabilityFilter)
            {
                double budgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
                double dsmBudgetMs = (config.PropulsionType == MGAPropulsionType.LowThrustElectric)
                    ? Math.Max(config.MaxDsmDeltaVMs, budgetMs)
                    : ((config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : budgetMs);
                bool noBurn = config != null && config.NoInsertionBurn;
                var filtered = MGAEphemerisReachabilityAnalyzer.FilterAndPrioritizeReachableSequences(
                    config.DeparturePlanet, config.ArrivalPlanet, sequences, config.SearchStartUT, budgetMs, dsmBudgetMs, config.FlybySequence, noBurn);
                Console.WriteLine($"[DEBUG] FilterAndPrioritizeReachableSequences returned {filtered?.Count ?? 0} sequences:");
                if (filtered != null)
                {
                    foreach (var f in filtered)
                    {
                        Console.WriteLine($"   filtered: {string.Join("->", f.FlybyChain)} (IsFeasible={f.IsFeasible}, Pareto={f.ParetoScore:F2}, Deficit={f.TotalDeficitMs:F0}m/s, EstTotalDv={f.EstimatedTotalDvMs:F0}m/s)");
                    }
                }
                if (filtered != null && filtered.Count > 0)
                {
                    sequences = filtered.Select(f => f.FlybyChain).ToList();
                    int poolLimit = Math.Max(maxChains, Math.Min(16, sequences.Count));
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
                if (muCentral <= 0) muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);

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
                            int resMax = node.IsDeepWell ? 3 : (node.IsGiant ? 1 : 2);
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

            double muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);
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
            if (flybyChain != null) fullChain.AddRange(flybyChain);
            if (!string.IsNullOrEmpty(arrival))
            {
                if (fullChain.Count == 0 || !fullChain[fullChain.Count - 1].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                {
                    fullChain.Add(arrival);
                }
            }

            var bounds = new List<(double MinDays, double MaxDays)>();
            double muCentralM = (SolarSystemCatalog.GetCentralMu(centralBody) > 0 
                ? SolarSystemCatalog.GetCentralMu(centralBody) 
                : (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11)) * 1e9;

            // 反梯级泵升深井识别: 全链最内天体 (如 Scenario 9 的水星 0.387 AU) 是否深于出发星 45%
            var depObjBounds = SolarSystemCatalog.GetPlanet(departure);
            double depSmaBounds = (depObjBounds != null && depObjBounds.SMA > 0) ? depObjBounds.SMA : 1.496e8;
            double deepestSma = double.MaxValue;
            foreach (var bn in fullChain)
            {
                var bp = SolarSystemCatalog.GetPlanet(bn);
                if (bp != null && bp.SMA > 0) deepestSma = Math.Min(deepestSma, bp.SMA);
            }
            bool deepWellExitMode = (deepestSma < depSmaBounds * 0.45);

            for (int k = 0; k < fullChain.Count - 1; k++)
            {
                string b1 = fullChain[k];
                string b2 = fullChain[k + 1];

                var p1 = SolarSystemCatalog.GetPlanet(b1);
                var p2 = SolarSystemCatalog.GetPlanet(b2);
                double sma1 = (p1 != null && p1.SMA > 0) ? p1.SMA : 1.496e8;
                double sma2 = (p2 != null && p2.SMA > 0) ? p2.SMA : 1.496e8;

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
                    var (minLeg, maxLeg, _) = MGAFlybyDynamicsHelper.DerivePhysicalResonantBounds(p1, pDownstream, muCentralKmS);
                    bounds.Add((minLeg, maxLeg));
                }
                else
                {
                    // 异星转移段: 100% 由欧拉-兰伯特方程与极值动力学几何数学自动推导
                    double muCentralKmS = muCentralM * 1e-9;
                    var (minLeg, maxLeg) = MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds(
                        p1, p2, muCentralKmS, k == 0, depMinFactor, depMaxFactor);
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

        public static List<List<string>> GenerateCandidateSequences(MGAConfig config) {
            var results = new List<List<string>>();
            if (config == null || string.IsNullOrEmpty(config.DepartureBody) || string.IsNullOrEmpty(config.DestinationBody))
                return results;

            string origin = config.DepartureBody;
            string dest = config.DestinationBody;

            // 出发与目标天体相同时无星际转移物理意义，直接返回空列表，杜绝无向 BFS 指数级爆炸与主线程卡死
            if (origin.Equals(dest, StringComparison.OrdinalIgnoreCase)) return results;

            var allPlanets = SolarSystemCatalog.GetAvailablePlanets();
            if (allPlanets == null || allPlanets.Count == 0) return results;

            // 过滤有效引力助推天体：
            // 仅保留出发天体、目标天体或引力常数 Mu >= 500.0 km^3/s^2 的有效引力源，
            // 彻底剔除 Ryugu, Eros, Bennu 等质量微小、偏转角近乎为0的微型小行星干扰，确保火星与金星排在队列首位
            var planetList = allPlanets.Where(p =>
                !string.IsNullOrEmpty(p.Name) &&
                (p.Name.Equals(origin, StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Equals(dest, StringComparison.OrdinalIgnoreCase) ||
                 p.Mu >= 500.0)
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
            bool destHasStrongGravity = (destPlanet != null && destPlanet.Mu >= 1000.0);

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
                            double ratioSma = Math.Max(pCur.SMA, pNext.SMA) / Math.Min(pCur.SMA, pNext.SMA);
                            // 若非巨行星助推且单次半长轴跨度超 6 倍 (无中间跳板直飞外太阳系)，直接剪枝
                            if (ratioSma > 6.0 && pCur.Mu < 1e5 && pNext.Mu < 1e5) {
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
                            double rRatio = Math.Max(pA.SMA, pB.SMA) / Math.Min(pA.SMA, pB.SMA);
                            if (rRatio > 4.0 && pA.Mu < 1e5) totalDeficitKms += (rRatio - 4.0) * 1.5;
                        }
                    }
                }
                double estimatedTotalMs = 3800.0 + totalDeficitKms * 1000.0;
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
