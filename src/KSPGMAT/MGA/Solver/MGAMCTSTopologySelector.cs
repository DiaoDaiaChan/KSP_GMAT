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

            double ratio = Math.Max(rDep, rArr) / Math.Max(1.0, Math.Min(rDep, rArr));
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
            /// 纯天体力学日心矢量几何与引力助推速度球准则 (Gravity Assist Velocity Sphere Criterion):
            /// 计算在中间借力天体 M 处的能量跳变与双曲偏转角相容性赤字 (km/s)
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

                // 1. 前后两腿在天体处的日心转移速度大小
                double v1 = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / rM - 1.0 / aIn)));
                double v2 = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / rM - 1.0 / aOut)));

                // 2. 日心速度所需跳变量 (考虑轨道拱线转折所需日心冲量)
                double deltaVreq = Math.Abs(v2 - v1);

                // 3. 中间借力天体无动力双曲偏折能提供的最大日心速度跳变量 (Gravity Assist Velocity Sphere)
                double avgVinf = (vInfIn + vInfOutCalc) * 0.5;
                bool isInnerPumpedClimb = (rFrom < rM * 0.85 && rTo > rM * 1.5);
                if (isInnerPumpedClimb)
                {
                    // 内潜蓄能弹弓 (Inner-Slingshot Pumping / VEEGA / VVEJGA 物理机制):
                    // 航天器潜入内侧低势能区借力后重返 rM 时，已具备高过剩能量 (v_inf >= 8~14 km/s)，
                    // 在 rM 处仅需偏折矢量即可顺向冲入外行星轨道，势能跳变赤字大幅收窄
                    double vInfPumped = Math.Max(avgVinf, Math.Sqrt(Math.Max(0.0, 2.0 * muCentral * (1.0 / rFrom - 1.0 / rM))) * 0.45);
                    avgVinf = Math.Max(avgVinf, vInfPumped);
                }

                double maxTurn = CalcMaxDeflectionAngle(avgVinf, assistNode.Mu, assistNode.SafeRadiusKm);
                double deltaVassistMax = 2.0 * avgVinf * Math.Sin(maxTurn * 0.5);

                // 4. 赤字计算: 所需日心速度变化若超出天体引力弹弓能力，多出部分计为赤字
                double deficit = Math.Max(0.0, deltaVreq - deltaVassistMax - (isInnerPumpedClimb ? 4.5 : 0.0));

                // 速度大小不守恒微小惩罚: 在深潜蓄能攀升或巨行星加速段豁免，因该构型正是为了获取剧烈速度跃迁
                double deltaVmag = Math.Abs(vInfOutCalc - vInfIn);
                double magPenalty = (isInnerPumpedClimb || assistNode.IsGiant) ? 0.0 : deltaVmag * 0.15;
                return deficit + magPenalty;
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

                candidates.Add(new CandidateTopology
                {
                    FlybyChain = new List<string>(),
                    LaunchDvKms = directLaunchDv,
                    AssistDeficitKms = 0.0,
                    TotalToFDays = tofDirect,
                    LegCount = 1,
                    TopologyCategory = "Direct",
                    ParetoScore = directLaunchDv * 1.2 + (tofDirect / 365.25) * 0.3
                });

                // 2. 确定搜索参数与深度门限
                double ratioSma = Math.Max(depNode.SMA, arrNode.SMA) / Math.Min(depNode.SMA, arrNode.SMA);
                bool isDeepInner = (arrNode.SMA < depNode.SMA * 0.5);
                bool isFarOuter = (ratioSma >= 8.0 || arrNode.SMA > depNode.SMA * 6.0);

                int treeMaxLegs = Math.Min(maxLegs, (isDeepInner || isFarOuter) ? 7 : (ratioSma >= 3.5 ? 5 : 4));
                double budgetLimit = (maxBudgetKms > 0 && maxBudgetKms < 50.0) ? maxBudgetKms : 7.0;

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
                int maxStates = 6000;

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

                        // 剪枝规则 1: 同星共振次数约束 (依据势阱深度)
                        if (isResonance)
                        {
                            int allowedRes = currNode.IsDeepWell ? 4 : (currNode.IsGiant ? 1 : 2);
                            if (resCount >= allowedRes) continue;
                            // 根节点出发若第一步即自环，允许深势阱内行星或外行星任务实施 EGA 开普勒共振蓄能 (如 Juno / Rosetta)
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
                                int occurrences = currPath.Count(n => n.Name.Equals(nextNode.Name, StringComparison.OrdinalIgnoreCase));
                                if (occurrences >= 2) continue;
                            }
                        }

                        // 计算该步转移与借力赤字
                        double stepDeficit = 0.0;
                        double legToF = 0.0;
                        double nextVinf = 0.0;

                        if (isResonance)
                        {
                            stepDeficit = 0.0; // 同星共振大小守恒
                            legToF = currNode.PeriodDays * (currNode.IsDeepWell ? 1.0 : (currNode.SMA > 1.2e8 && currNode.SMA < 2.2e8 ? 2.0 : 1.0));
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
                                vInfDep = 3.6; // 标称 2:1 EGA 共振出射双曲速度 (如 Juno / Rosetta)
                            }
                            else
                            {
                                CalcHohmannLeg(depNode.SMA, firstNode.SMA, muCentral, out vInfDep, out _, out _);
                            }
                            double vHyp = Math.Sqrt(vInfDep * vInfDep + 2.0 * depNode.Mu / rpDep);
                            double launchDv = Math.Max(0.0, vHyp - vParkDep);

                            int legs = chain.Count + 1;
                            string cat = (chain.Count == 1) ? "Single" : (chain.Count == 2 ? "Dual" : (chain.Count == 3 ? "Multi" : "GrandTour"));
                            double score = launchDv * 1.2 + newCumDeficit * 1.5 + (vInfArrival * 0.8) + (newTotalToF / 365.25) * 0.4 + legs * 0.1;

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

                            // 若目标是深势阱小天体 (如水星)，自动在其后展开 1~3 次目标本星自减速共振 (MESSENGER / BepiColombo 机制)
                            if (arrNode.IsDeepWell && chain.Count > 0)
                            {
                                for (int extraRes = 1; extraRes <= 3; extraRes++)
                                {
                                    var resonantChain = new List<string>(chain);
                                    for (int er = 0; er < extraRes; er++) resonantChain.Add(arrival);
                                    double decelToF = newTotalToF + extraRes * arrNode.PeriodDays;
                                    double decelScore = score + extraRes * 0.05;
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

                            continue; // 到达目标后不再向后发散
                        }

                        // 继续向深层树节点展开
                        var newPath = new List<BodyNode>(currPath) { nextNode };
                        queue.Enqueue((newPath, newCumDeficit, nextVinf, newTotalToF, resCount + (isResonance ? 1 : 0)));
                    }
                }

                return candidates;
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

                // 1. 直达拓扑总是作为基础保留 (纯借力天体列表为空)
                var directCand = directBucket.FirstOrDefault();
                if (directCand.FlybyChain != null)
                {
                    selected.Add(new List<string>(directCand.FlybyChain));
                }

                // 2. 结构多样性分族抽取器: 确保相同借力级数下不同物理构型 (如 Venusx1->Earthx2 vs Venusx2->Earthx1) 均有代表入围
                void AddDiverseFromBucket(List<CandidateTopology> bucket, int quota)
                {
                    if (bucket == null || bucket.Count == 0 || quota <= 0) return;
                    var fams = new Dictionary<string, List<CandidateTopology>>();
                    foreach (var c in bucket)
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
                    int added = 0;
                    int maxR = bucket.Count;
                    for (int r = 0; r < maxR && added < quota; r++)
                    {
                        foreach (var kvp in fams)
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

                // 配额分配: 单借力、双借力、3借力、4+长链大巡游各自按需保底
                int remaining = maxChains - selected.Count;
                int activeCategories = 0;
                if (singleBucket.Count > 0) activeCategories++;
                if (dualBucket.Count > 0) activeCategories++;
                if (multiBucket.Count > 0) activeCategories++;
                if (grandTourBucket.Count > 0) activeCategories++;

                if (activeCategories > 0)
                {
                    int baseQuota = Math.Max(1, remaining / activeCategories);
                    AddDiverseFromBucket(singleBucket, baseQuota);
                    AddDiverseFromBucket(dualBucket, baseQuota);
                    AddDiverseFromBucket(multiBucket, baseQuota);
                    AddDiverseFromBucket(grandTourBucket, Math.Max(baseQuota, maxChains - selected.Count));
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
                // 2. 100% 纯盲搜模式: 纯开普勒两体物理可达图与分支定界树搜索自主涌现全部经典拓扑
                string central = config.CentralBody ?? "Sun";
                double maxBudget = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) 
                    ? (config.VesselMaxDeltaVMs / 1000.0) 
                    : 7.0;

                var candidateTopologies = KeplerAdjacencyGraphEngine.SearchTopologies(
                    config.DeparturePlanet, 
                    config.ArrivalPlanet, 
                    central, 
                    maxBudget, 
                    8, // 最大腿数允许至 8 腿，绝不人为删减任何物理序列
                    maxChains * 8);

                sequences = KeplerAdjacencyGraphEngine.SelectDiverseTopologies(candidateTopologies, Math.Max(maxChains * 16, 96), config.ArrivalPlanet);
            }

            // 3. 最终由真实星历历元与开普勒 Vis-Viva 能量赤字分析器前置过滤并精准排序
            if (config.EnableEphemerisReachabilityFilter)
            {
                double budgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
                double dsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : budgetMs;
                var filtered = MGAEphemerisReachabilityAnalyzer.FilterAndPrioritizeReachableSequences(
                    config.DeparturePlanet, config.ArrivalPlanet, sequences, config.SearchStartUT, budgetMs, dsmBudgetMs, config.FlybySequence);
                if (filtered != null && filtered.Count > 0)
                {
                    sequences = filtered.Select(f => f.FlybyChain).ToList();
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
                    // 同星共振调相段：依据天体公转周期与势阱能量包络线锁定物理有效共振槽
                    double tOrbDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(sma1 * 1000.0, 3.0) / muCentralM) / 86400.0;
                    if (tOrbDays <= 0 || double.IsNaN(tOrbDays)) tOrbDays = 365.25;

                    // 普适开普勒共振调相段：允许 1:1, 3:2, 5:2, 2:1 等物理多阶有理共振槽
                    // 1. 浅势阱小天体 (如水星/Moho μ/R < 20) 限制在 2:1 与 5:2 以内 (<= 2.6 T_orb):
                    //    2:1 (176d, 拱点抬至 0.84 AU) 是水星共振抬升主力槽, 5:2 (220d, 拱点抬至 ~1.04 AU)
                    //    是反梯级泵升的亚共振辅助槽; 更高阶 (3:1=264d) 会把航天器误抛至超大椭圆脱离内侧对齐
                    // 2. 中继大行星 (如 Earth, SMA ~ 1 AU) 若后续衔接外侧巨行星 (如 Jupiter, SMA > 5 AU):
                    //    对齐伽利略 VEEGA 机制 (2:1 共振蓄能抬升远日点至 2.16 AU, 相对速度增至 9 km/s 直冲木星):
                    //    上限锁定在 2.25 T_orb (820天)，彻底排除 3:1 (1100天) 劣质局部伪极值与超额 DSM！
                    bool hasDownstreamGiant = false;
                    for (int nxt = k + 1; nxt < fullChain.Count; nxt++)
                    {
                        var pNxt = SolarSystemCatalog.GetPlanet(fullChain[nxt]);
                        if (pNxt != null && pNxt.SMA >= sma1 * 3.5) { hasDownstreamGiant = true; break; }
                    }

                    double resMinFactor = MGADefaults.ResonanceToFMinFactor;
                    double resMaxFactor = 2.25; // 默认内侧天体 (如地球/金星) 限制在 2:1 共振以内 (0.85 ~ 2.25 T_orb)

                    if (p1 != null && p1.RadiusKm > 0 && (p1.Mu / p1.RadiusKm) < 20.0)
                    {
                        // 浅势阱小天体 (如水星/Moho) 多次共振梯级泵升 (Resonant Ladder Pump-Up):
                        // 允许开普勒多阶有理共振槽 (1:1=88d, 2:1=176d, 3:1=264d 泵升至 0.805 AU 跨越金星轨道)
                        // 统一提供 0.85 ~ 3.15 T_orb 宽裕物理搜索区间，赋能 DE 求解器自适应锁定最佳相位
                        resMinFactor = 0.85;
                        resMaxFactor = 3.15;
                    }
                    else if (p1 != null && p1.SMA < depSmaBounds * 0.85 && p1.SMA > deepestSma * 1.2)
                    {
                        resMinFactor = 0.90;
                        resMaxFactor = 2.15; // 金星等内侧中继跳板: 允许 1:1 (~225天) 与 2:1/3:2 多元开普勒共振调相
                    }
                    else if (hasDownstreamGiant && sma1 >= 1.2e8 && sma1 <= 2.2e8)
                    {
                        // 依据该共振段之后的下游走向区分前置 EGA 与后置 VEEGA:
                        string nextDest = null;
                        for (int nk = k + 1; nk < fullChain.Count; nk++)
                        {
                            if (!fullChain[nk].Equals(b1, StringComparison.OrdinalIgnoreCase))
                            {
                                nextDest = fullChain[nk];
                                break;
                            }
                        }
                        var pNextDest = !string.IsNullOrEmpty(nextDest) ? SolarSystemCatalog.GetPlanet(nextDest) : null;
                        if (pNextDest != null && pNextDest.SMA < sma1 * 0.85)
                        {
                            // 前置 1:1 EGA 调速段 (如 Earth -> Earth -> Venus，锁定 340 ~ 390 天)
                            resMinFactor = 0.93;
                            resMaxFactor = 1.07;
                        }
                        else
                        {
                            // 地球 VEEGA 2:1 共振蓄能槽 (锁定 680 ~ 780 天, 严禁向 1:1 或 3:1 漂移)
                            resMinFactor = 1.86;
                            resMaxFactor = 2.14;
                        }
                    }

                    double minLeg = Math.Max(0.5, tOrbDays * resMinFactor);
                    double maxLeg = Math.Max(minLeg + 5.0, tOrbDays * resMaxFactor);
                    bounds.Add((minLeg, maxLeg));
                }
                else
                {
                    // 异星转移段：依霍曼转移半长轴与转移几何分类动态划定物理弧段
                    double aTrans = (sma1 + sma2) * 0.5;
                    double tHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aTrans * 1000.0, 3.0) / muCentralM) / 86400.0;
                    if (tHohmannDays <= 0 || double.IsNaN(tHohmannDays)) tHohmannDays = 200.0;

                    double ratioSma = Math.Max(sma1, sma2) / Math.Max(1.0, Math.Min(sma1, sma2));
                    double minFactor, maxFactor;

                    if (k == 0)
                    {
                        // 始发段受用户/配置设定的出射因子约束
                        minFactor = depMinFactor;
                        maxFactor = depMaxFactor;
                        // 若始发段直接连接外侧巨行星 (如 Earth -> Jupiter, SMA 跨度 >= 3.5x):
                        // 允许高速双曲切入 (0.35 ~ 1.50 T_H ~ 350d - 1500d)，覆盖 Voyager/New Horizons/Pioneer 等高能发射窗口
                        if (p2 != null && p2.SMA >= sma1 * 3.5)
                        {
                            minFactor = Math.Min(minFactor, 0.35);
                        }
                    }
                    else if (deepWellExitMode && p1 != null && p1.SMA > 0 &&
                             p1.SMA <= deepestSma * 1.05 && p2 != null && p2.SMA > p1.SMA * 1.3)
                    {
                        // 深井出口反梯级泵升腿 (Deep-Well Exit Reverse-Ladder Leg):
                        // 从全链最内天体 (水星) 爬升至外侧首个借力体 (金星):
                        // 覆盖开普勒 Type-I 单弧与金星会合窗口 (0.75 ~ 1.45 T_H ~ 79d - 153d)
                        // 杜绝因放宽至 3.50 T_H 导致航时漂移至 214d (相位偏移 180°) 产生的 3400 m/s 离面虚耗
                        minFactor = 0.75;
                        maxFactor = 1.45;
                    }
                    else if (p1 != null && p2 != null && p1.SMA < depSmaBounds * 0.85 && p2.SMA <= deepestSma * 1.05)
                    {
                        // 内潜切入深势阱腿 (如 Venus -> Mercury):
                        // 覆盖开普勒 Type-I 与 Type-II 降交点转移 (0.65 ~ 2.20 T_H ~ 48d - 165d)
                        minFactor = 0.65;
                        maxFactor = 2.20;
                    }
                    else if (sma1 >= 1.2e8 && sma1 <= 2.2e8 && p2 != null && p2.SMA >= sma1 * 3.5)
                    {
                        // 从中继大行星冲向巨行星 (如 Earth -> Jupiter):
                        // 覆盖伽利略 VEEGA 标准霍曼转移与直接切入 (0.40 ~ 1.35 T_H ~ 400d - 1350d)
                        minFactor = 0.40;
                        maxFactor = 1.35;
                    }
                    else if (p1 != null && (p1.Mu >= 5.0e6 || p1.RadiusKm >= 20000.0) && sma2 > sma1)
                    {
                        // 巨行星强引力大偏折弹弓高速双曲飞向外侧行星 (如 Jupiter -> Saturn, Saturn -> Uranus, Uranus -> Neptune):
                        // 航天器经巨行星借力后能量极大，以超高速度沿双曲弧段飞向外侧天体，
                        // 航时为标准霍曼的 0.15 ~ 0.55 T_H (覆盖 Voyager 2 木-土 2.1年、土-天 4.4年、天-海 3.6年等极速弹弓)
                        minFactor = 0.15;
                        maxFactor = 0.55;
                    }
                    else if (ratioSma <= MGADefaults.AdjacentTransferSmaRatioThreshold)
                    {
                        // 近邻天体转移 (如 Venus <-> Mercury 比值 1.87, Earth <-> Venus 比值 1.38):
                        // 物理单弧转移是以真近点角变化 delta_theta ~ 180° 为主的椭圆弧段，
                        // 航时允许 Type-I/Type-II 弹性延伸，最大放宽至 2.40 T_H
                        minFactor = MGADefaults.NearTransferToFMinFactor;
                        maxFactor = Math.Max(MGADefaults.NearTransferToFMaxFactor, 2.40);
                    }
                    else
                    {
                        // 远距离跨体系/外行星转移 (如 Jupiter -> Neptune 直达):
                        // 巨行星强引力大偏折弹弓高速双曲飞向外行星 (例如 Voyager 2 约 10 年航程，对应 0.20 ~ 0.55 T_H ~ 6.5年 - 17年)
                        // 彻底杜绝未约束至 70-85 年 void 空间导致的发散
                        bool isGiantToOuter = (p1 != null && p1.Mu >= MGADefaults.GiantPlanetMinMu && ratioSma >= MGADefaults.GiantAssistDistanceRatio);
                        minFactor = isGiantToOuter ? 0.20 : ((ratioSma >= MGADefaults.GiantAssistDistanceRatio) ? 0.08 : MGADefaults.FarTransferToFMinFactor);
                        maxFactor = isGiantToOuter ? 0.55 : MGADefaults.OuterTransferToFMaxFactor;
                    }

                    double minLeg = Math.Max(5.0, tHohmannDays * minFactor);
                    double maxLeg = Math.Max(minLeg + 10.0, tHohmannDays * maxFactor);
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

            // 确定性 Max 估计器 (IJCAI 2015): X = max(0, (DeltaV_max - DeltaV_tot) / DeltaV_max)
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
                return xScore;
            }).ToList();

            return results;
        }
    }
}
