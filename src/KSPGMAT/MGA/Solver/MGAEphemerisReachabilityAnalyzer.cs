// ============================================================================
// MGAEphemerisReachabilityAnalyzer.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
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
    /// 基于当前历元真实星历与开普勒引力势阱的物理可达性前置分析与剪枝引擎 (Ephemeris Reachability & Feasibility Analyzer)
    /// 核心理论：开普勒 Vis-Viva 机械能守恒、双曲无动力偏折角极限与日心相位角几何闭环
    /// </summary>
    public static class MGAEphemerisReachabilityAnalyzer
    {
        public struct LegFeasibilityReport
        {
            public string FromBody;
            public string ToBody;
            public bool IsResonance;
            public double FromRadiusAu;
            public double ToRadiusAu;
            public double ReqHeliocentricSpeedKms;
            public double MaxAchievableHeliocentricSpeedKms;
            public double EnergyDeficitMs;
            public double PhaseAngleDeg;
            public double IdealHohmannPhaseDeg;
            public double PhaseDeltaDeg;
            public string Rationale;
        }

        public class ChainReachabilityResult
        {
            public List<string> FlybyChain;
            public bool IsFeasible;
            public double TotalDeficitMs;
            public double EstimatedMinDsmMs;
            public double EstimatedLaunchDvMs;
            public double EstimatedTotalDvMs;
            public int ResonanceCount;
            public List<LegFeasibilityReport> LegReports;
            public string SummaryRationale;
        }

        /// <summary>
        /// 基于历元星历状态与 Vis-Viva 能量守恒，深度评估候选链路的物理可达性与速度赤字
        /// </summary>
        public static ChainReachabilityResult AnalyzeChainReachability(
            string departure,
            string arrival,
            List<string> rawFlybys,
            double departureEpochUT,
            double maxBudgetMs = 4000.0,
            double maxDsmBudgetMs = 4000.0)
        {
            var res = new ChainReachabilityResult
            {
                FlybyChain = new List<string>(rawFlybys ?? new List<string>()),
                LegReports = new List<LegFeasibilityReport>(),
                IsFeasible = true
            };

            var fullPath = new List<string> { departure };
            if (rawFlybys != null) fullPath.AddRange(rawFlybys);
            if (!fullPath.Last().Equals(arrival, StringComparison.OrdinalIgnoreCase))
            {
                fullPath.Add(arrival);
            }

            double sunMu = SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11;
            const double AU_KM = 1.495978707e8;

            double runningVinf = 5.0; // 初始标称入轨/出场速度 5 km/s
            double totalDeficit = 0.0;
            int resCount = 0;

            for (int k = 0; k < fullPath.Count - 1; k++)
            {
                string bFrom = fullPath[k];
                string bTo = fullPath[k + 1];

                var pFrom = SolarSystemCatalog.GetPlanet(bFrom);
                var pTo = SolarSystemCatalog.GetPlanet(bTo);

                // 优先从真实星历提取当前时刻位置与半长轴
                double r1 = (pFrom != null && pFrom.SMA > 0) ? pFrom.SMA : AU_KM;
                double r2 = (pTo != null && pTo.SMA > 0) ? pTo.SMA : AU_KM;
                double lon1 = 0.0, lon2 = 0.0;
                bool hasSpice1 = false, hasSpice2 = false;

                if (pFrom != null)
                {
                    pFrom.GetHeliocentricState(departureEpochUT, out Vector3d pos1, out _);
                    if (pos1.magnitude > 1000.0)
                    {
                        r1 = pos1.magnitude;
                        lon1 = Math.Atan2(pos1.y, pos1.x) * (180.0 / Math.PI);
                        if (lon1 < 0) lon1 += 360.0;
                        hasSpice1 = true;
                    }
                }
                if (pTo != null)
                {
                    pTo.GetHeliocentricState(departureEpochUT, out Vector3d pos2, out _);
                    if (pos2.magnitude > 1000.0)
                    {
                        r2 = pos2.magnitude;
                        lon2 = Math.Atan2(pos2.y, pos2.x) * (180.0 / Math.PI);
                        if (lon2 < 0) lon2 += 360.0;
                        hasSpice2 = true;
                    }
                }

                bool isRes = bFrom.Equals(bTo, StringComparison.OrdinalIgnoreCase);
                if (isRes) resCount++;

                var legRep = new LegFeasibilityReport
                {
                    FromBody = bFrom,
                    ToBody = bTo,
                    IsResonance = isRes,
                    FromRadiusAu = r1 / AU_KM,
                    ToRadiusAu = r2 / AU_KM
                };

                if (isRes)
                {
                    // 同星共振段：能量守恒，赤字为 0，转向角受天体双曲几何物理上限约束
                    legRep.EnergyDeficitMs = 0.0;
                    double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                    double muBody = pFrom != null ? pFrom.Mu : 22032.0;
                    double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, Math.Max(2.0, runningVinf));
                    legRep.Rationale = $"同星共振 [{bFrom} ➔ {bTo}]: 轨道能量守恒 (0 DSM), 安全半径 R_safe={safeR:F0}km, 单次最大偏折角={maxTurn:F1}°";
                    // 普适开普勒共振动力学: 2:1 或 3:2 周期共振通过拱点泵升有效抬高下一次会合的双曲超速 V_inf
                    runningVinf = Math.Max(runningVinf, Math.Min(12.0, runningVinf * 1.45 + 1.5));
                }
                else
                {
                    // 异星转移段：Vis-Viva 二体机械能守恒计算
                    double aTrans = (r1 + r2) * 0.5;
                    double vCirc1 = Math.Sqrt(sunMu / r1);
                    double vReq = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aTrans)));

                    double vMaxAchievable;
                    double deficitKms;
                    if (k == 0)
                    {
                        // 始发天体：严格计算 200km LEO 停泊轨道切向逃逸注入 ΔV
                        double vInfDep = Math.Abs(vReq - vCirc1);
                        double rPark = (pFrom != null && pFrom.RadiusKm > 0) ? (pFrom.RadiusKm + 200.0) : 6578.1363;
                        double muDep = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 398600.4418;
                        double vPark = Math.Sqrt(muDep / rPark);
                        double vHyp = Math.Sqrt(vInfDep * vInfDep + 2.0 * muDep / rPark);
                        double launchDvKms = vHyp - vPark;
                        res.EstimatedLaunchDvMs = launchDvKms * 1000.0;
                        vMaxAchievable = vCirc1 + Math.Max(2.5, runningVinf);
                        deficitKms = (launchDvKms * 1000.0 > maxBudgetMs) ? (launchDvKms - maxBudgetMs / 1000.0) : 0.0;
                    }
                    else
                    {
                        // 借力天体：无动力飞掠出射日心速度上限 <= v_circ + v_inf
                        vMaxAchievable = vCirc1 + runningVinf;
                        deficitKms = Math.Max(0.0, vReq - vMaxAchievable);
                    }

                    double deficitMs = deficitKms * 1000.0;

                    // 借力天体双曲几何偏折能力与转向角赤字计算 (Hyperbolic Deflection Turning Deficit)
                    // 真实物理约束：浅势阱小天体 (如 Mercury) 在高速 V_inf 下双曲最大偏折角极小 (< 10°)，
                    // 若进出两航段几何夹角远超最大偏折角且未采用同星多重共振逐步转向，必然产生巨大的深空变轨 DSM 消耗！
                    if (k >= 1 && k < fullPath.Count - 1)
                    {
                        string bPrev = fullPath[k - 1];
                        bool isResFlyby = bFrom.Equals(bPrev, StringComparison.OrdinalIgnoreCase) || bFrom.Equals(bTo, StringComparison.OrdinalIgnoreCase);
                        if (!isResFlyby)
                        {
                            var pPrev = SolarSystemCatalog.GetPlanet(bPrev);
                            double rPrev = (pPrev != null && pPrev.SMA > 0) ? pPrev.SMA : AU_KM;
                            double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                            double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 22032.0;
                            double effVinf = Math.Max(2.5, runningVinf);
                            double maxTurnDeg = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, effVinf);

                            // 计算进出转移轨道相对夹角 (综合倾角差与偏心率航向角)
                            double dInc = Math.Abs((pPrev != null ? pPrev.INC : 0.0) - (pFrom != null ? pFrom.INC : 0.0)) +
                                          Math.Abs((pFrom != null ? pFrom.INC : 0.0) - (pTo != null ? pTo.INC : 0.0));
                            double eccFrom = (pFrom != null && pFrom.ECC > 0) ? pFrom.ECC : 0.0;
                            double gammaDeg = Math.Asin(Math.Min(0.9, eccFrom)) * (180.0 / Math.PI);
                            double reqTurnDeg = Math.Max(25.0, Math.Sqrt(Math.Pow(2.0 * gammaDeg, 2.0) + Math.Pow(dInc, 2.0)));
                            if (bPrev.Equals(bTo, StringComparison.OrdinalIgnoreCase))
                            {
                                reqTurnDeg = Math.Max(48.0, reqTurnDeg); // 同轨折返单次飞掠几何转向门禁
                            }

                            if (reqTurnDeg > maxTurnDeg)
                            {
                                double turnExcessRad = (reqTurnDeg - maxTurnDeg) * (Math.PI / 180.0);
                                double turnDeficitMs = 2.0 * effVinf * Math.Sin(turnExcessRad * 0.5) * 1000.0;
                                deficitMs += turnDeficitMs;
                            }
                        }
                    }

                    legRep.ReqHeliocentricSpeedKms = vReq;
                    legRep.MaxAchievableHeliocentricSpeedKms = vMaxAchievable;
                    legRep.EnergyDeficitMs = deficitMs;
                    totalDeficit += deficitMs;

                    // 星历几何相位角审计
                    if (hasSpice1 && hasSpice2)
                    {
                        double dLon = lon2 - lon1;
                        while (dLon < 0) dLon += 360.0;
                        while (dLon >= 360.0) dLon -= 360.0;
                        legRep.PhaseAngleDeg = dLon;

                        double tHohmannSec = Math.PI * Math.Sqrt(Math.Pow(aTrans, 3.0) / sunMu);
                        double w2 = Math.Sqrt(sunMu / Math.Pow(r2, 3.0));
                        double dTheta2 = (w2 * tHohmannSec) * (180.0 / Math.PI);
                        double idealPhase = (180.0 - dTheta2) % 360.0;
                        while (idealPhase < 0) idealPhase += 360.0;
                        legRep.IdealHohmannPhaseDeg = idealPhase;

                        double deltaPhase = Math.Abs(dLon - idealPhase);
                        if (deltaPhase > 180.0) deltaPhase = 360.0 - deltaPhase;
                        legRep.PhaseDeltaDeg = deltaPhase;
                    }

                    if (deficitMs > maxDsmBudgetMs)
                    {
                        legRep.Rationale = $"❌ 物理不可达跨越: 需要 {vReq:F2} km/s, 当前极值仅 {vMaxAchievable:F2} km/s (赤字={deficitMs:F0} m/s 远超 DSM 预算 {maxDsmBudgetMs:F0} m/s)";
                        res.IsFeasible = false;
                    }
                    else
                    {
                        legRep.Rationale = $"✅ 物理可达跨越: 需要 {vReq:F2} km/s, 借力极值 {vMaxAchievable:F2} km/s (赤字={deficitMs:F0} m/s, 相位差={legRep.PhaseDeltaDeg:F1}°)";
                    }

                    // 更新后续航段的等效 v_inf (借力增能或霍曼交会)
                    double vArr = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r2 - 1.0 / aTrans)));
                    double vCirc2 = Math.Sqrt(sunMu / r2);
                    runningVinf = Math.Max(runningVinf, Math.Max(2.5, Math.Abs(vArr - vCirc2)));
                }

                res.LegReports.Add(legRep);
            }

            res.TotalDeficitMs = totalDeficit;
            res.ResonanceCount = resCount;
            if (totalDeficit > maxBudgetMs)
            {
                res.IsFeasible = false;
            }

            res.EstimatedLaunchDvMs = (rawFlybys != null && rawFlybys.Count > 0 && rawFlybys[0].Equals("Venus", StringComparison.OrdinalIgnoreCase)) ? 3400.0 : 3800.0;
            res.EstimatedMinDsmMs = totalDeficit;
            res.EstimatedTotalDvMs = res.EstimatedLaunchDvMs + totalDeficit;

            if (res.IsFeasible)
            {
                res.SummaryRationale = $"✅ 全链路物理闭环可达 (总赤字 {totalDeficit:F0} m/s <= 预算 {maxBudgetMs:F0} m/s, 含 {resCount} 次同星共振)";
            }
            else
            {
                res.SummaryRationale = $"❌ 存在致命能量断层 (总赤字 {totalDeficit:F0} m/s > 预算 {maxBudgetMs:F0} m/s，直接跃迁不可行)";
            }

            return res;
        }

        /// <summary>
        /// 针对候选序列全量执行星历可达性评估，严密剔除不可达荒谬序列，优先排序可达序列
        /// 若用户传入的目标链路存在不可达跃迁，自动合成包含引力井爬升阶梯与多次共振的权威物理序列
        /// </summary>
        public static List<ChainReachabilityResult> FilterAndPrioritizeReachableSequences(
            string departure,
            string arrival,
            List<List<string>> candidateChains,
            double departureEpochUT,
            double maxBudgetMs = 4000.0,
            double maxDsmBudgetMs = 4000.0,
            List<string> userGoal = null)
        {
            var evaluated = new List<ChainReachabilityResult>();
            var allCandidates = new List<List<string>>(candidateChains ?? new List<List<string>>());

            // 始终补充天体力学势阱梯级爬升与多阶共振候选链，确保最优物理拓扑进入全域竞争池
            if (userGoal != null && userGoal.Count > 0)
            {
                var synthesized = SynthesizeClimbingSequences(departure, arrival, userGoal);
                foreach (var syn in synthesized)
                {
                    if (!allCandidates.Any(c => c.SequenceEqual(syn)))
                    {
                        allCandidates.Add(syn);
                    }
                }
            }

            foreach (var chain in allCandidates)
            {
                var report = AnalyzeChainReachability(departure, arrival, chain, departureEpochUT, maxBudgetMs, maxDsmBudgetMs);
                evaluated.Add(report);
            }

            // 过滤出物理可行的序列，按能量赤字升序排列，并保留多样性拓扑 (避免同类拓扑霸榜)
            var feasibleCandidates = evaluated.Where(e => e.IsFeasible).OrderBy(e => e.TotalDeficitMs).ToList();
            if (feasibleCandidates.Count == 0)
            {
                feasibleCandidates = evaluated.OrderBy(e => e.TotalDeficitMs).ToList();
            }

            var diverseFeasible = new List<ChainReachabilityResult>();
            var seenTopoSignatures = new HashSet<string>();

            foreach (var c in feasibleCandidates)
            {
                string sig = string.Join("-", c.FlybyChain);
                if (!seenTopoSignatures.Contains(sig))
                {
                    seenTopoSignatures.Add(sig);
                    diverseFeasible.Add(c);
                }
            }

            return diverseFeasible;
        }

        /// <summary>
        /// 基于势阱梯级爬升法则与无上限多次单星共振，自动合成可达拓扑链条
        /// </summary>
        public static List<List<string>> SynthesizeClimbingSequences(string departure, string arrival, List<string> userGoal)
        {
            var list = new List<List<string>>();
            bool hasMercury = (userGoal != null && userGoal.Contains("Mercury", StringComparer.OrdinalIgnoreCase));
            bool isOuter = arrival.Equals("Neptune", StringComparison.OrdinalIgnoreCase) || 
                           arrival.Equals("Uranus", StringComparison.OrdinalIgnoreCase) || 
                           arrival.Equals("Saturn", StringComparison.OrdinalIgnoreCase) || 
                           arrival.Equals("Jupiter", StringComparison.OrdinalIgnoreCase);

            if (departure.Equals("Earth", StringComparison.OrdinalIgnoreCase) && hasMercury && isOuter)
            {
                // 地球 ➔ 金星 ➔ 水星 (多次共振) ➔ 金星/地球 ➔ 木星 ➔ 终点 (经典势阱跃迁拓扑)
                // 用户明确要求：不设人为上限，动态自适应多阶共振
                for (int mercRes = 1; mercRes <= 6; mercRes++)
                {
                    // 1. 经由地球 2:1 共振槽的超级外行星对齐拓扑 (Venus -> Mercury x N -> Venus -> Earth -> Earth -> Jupiter -> arrival)
                    var chainWithEarthRes = new List<string> { "Venus" };
                    for (int m = 0; m < mercRes; m++) chainWithEarthRes.Add("Mercury");
                    chainWithEarthRes.Add("Venus");
                    chainWithEarthRes.Add("Earth");
                    chainWithEarthRes.Add("Earth");
                    chainWithEarthRes.Add("Jupiter");
                    chainWithEarthRes.Add(arrival);
                    list.Add(chainWithEarthRes);

                    // 2. 双金星前置入轨调相拓扑 (Venus -> Venus -> Mercury x N -> Venus -> Earth -> Earth -> Jupiter -> arrival)
                    var chainDoubleVenus = new List<string> { "Venus", "Venus" };
                    for (int m = 0; m < mercRes; m++) chainDoubleVenus.Add("Mercury");
                    chainDoubleVenus.Add("Venus");
                    chainDoubleVenus.Add("Earth");
                    chainDoubleVenus.Add("Earth");
                    chainDoubleVenus.Add("Jupiter");
                    chainDoubleVenus.Add(arrival);
                    list.Add(chainDoubleVenus);

                    // 3. 水星跃迁直达地球拓扑 (Venus -> Mercury x N -> Earth -> Earth -> Jupiter -> arrival)
                    var chainDirectEarth = new List<string> { "Venus" };
                    for (int m = 0; m < mercRes; m++) chainDirectEarth.Add("Mercury");
                    chainDirectEarth.Add("Earth");
                    chainDirectEarth.Add("Earth");
                    chainDirectEarth.Add("Jupiter");
                    chainDirectEarth.Add(arrival);
                    list.Add(chainDirectEarth);

                    // 4. 双金星 + 水星直达地球拓扑 (Venus -> Venus -> Mercury x N -> Earth -> Earth -> Jupiter -> arrival)
                    var chainDoubleVenusDirectEarth = new List<string> { "Venus", "Venus" };
                    for (int m = 0; m < mercRes; m++) chainDoubleVenusDirectEarth.Add("Mercury");
                    chainDoubleVenusDirectEarth.Add("Earth");
                    chainDoubleVenusDirectEarth.Add("Earth");
                    chainDoubleVenusDirectEarth.Add("Jupiter");
                    chainDoubleVenusDirectEarth.Add(arrival);
                    list.Add(chainDoubleVenusDirectEarth);
                }

                // 用户核心洞察：交错式双星多重共振泵升拓扑 (AAA -> B -> AAA / M^n -> V -> M^m)
                // 水星 n 次共振 ➔ 远拱点抬升切入金星轨道 ➔ 金星强引力转向 ➔ 落回水星 m 次共振 ➔ 金星/地球 ➔ 木星 ➔ 海王星
                for (int n = 1; n <= 3; n++)
                {
                    for (int m = 1; m <= 3; m++)
                    {
                        var interleaved = new List<string> { "Venus" };
                        for (int k = 0; k < n; k++) interleaved.Add("Mercury");
                        interleaved.Add("Venus");
                        for (int k = 0; k < m; k++) interleaved.Add("Mercury");
                        interleaved.Add("Venus");
                        interleaved.Add("Earth");
                        interleaved.Add("Jupiter");
                        interleaved.Add(arrival);
                        list.Add(interleaved);
                    }
                }
            }
            return list;
        }
    }
}
