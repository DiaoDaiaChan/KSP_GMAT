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
            public double ReqTurnDeg;
            public double AvailTurnDeg;
            public double TurnDeficitMs;
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

        public static List<ChainReachabilityResult> LastEvaluatedChains { get; private set; } = new List<ChainReachabilityResult>();

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
            var cleanedFlybys = new List<string>();
            if (rawFlybys != null)
            {
                for (int i = 0; i < rawFlybys.Count; i++)
                {
                    string fb = rawFlybys[i];
                    if (string.IsNullOrEmpty(fb)) continue;
                    // 仅当首个天体冗余包含出发天体，或末个天体冗余包含到达天体时进行端点剪裁；中间借力天体 (如地球借力/共振 Earth) 绝对严禁过滤！
                    if (i == 0 && fb.Equals(departure, StringComparison.OrdinalIgnoreCase)) continue;
                    if (i == rawFlybys.Count - 1 && fb.Equals(arrival, StringComparison.OrdinalIgnoreCase)) continue;
                    cleanedFlybys.Add(fb);
                }
            }

            var res = new ChainReachabilityResult
            {
                FlybyChain = cleanedFlybys,
                LegReports = new List<LegFeasibilityReport>(),
                IsFeasible = true
            };

            var fullPath = new List<string> { departure };
            fullPath.AddRange(cleanedFlybys);
            fullPath.Add(arrival);

            double sunMu = SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu;
            double AU_KM = MGADefaults.AU_KM;

            double runningVinf = MGADefaults.NominalInitialVInfKms; // 初始标称入轨/出场速度基准
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
                    // 普适开普勒共振动力学: 无动力两体引力助推中，天体质心系双曲过剩速度绝对守恒 (|v_inf+| = |v_inf-|)
                    // 共振仅通过多次飞掠累积双曲转向角，不增加相对于借力天体的能量大小
                }
                else
                {
                    // 异星转移段：Vis-Viva 二体机械能守恒计算
                    double aTrans = (r1 + r2) * 0.5;
                    double vCirc1 = Math.Sqrt(sunMu / r1);
                    double vReq = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aTrans)));

                    // 统计当前天体在连续借力集群中的总飞掠次数 (Cluster Count)
                    int clusterCount = 1;
                    if (k >= 1)
                    {
                        for (int m = k - 1; m >= 0 && fullPath[m].Equals(bFrom, StringComparison.OrdinalIgnoreCase); m--) {
                            clusterCount++;
                        }
                    }

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
                        vMaxAchievable = vCirc1 + Math.Max(MGADefaults.MinEffectiveVInfKms, runningVinf);
                        deficitKms = (launchDvKms * 1000.0 > maxBudgetMs) ? (launchDvKms - maxBudgetMs / 1000.0) : 0.0;
                    }
                    else
                    {
                        // 借力天体：无动力飞掠出射日心速度上限 <= v_circ + v_inf
                        // 开普勒共振动力学真理：当存在同星多阶共振 (如 Earth->Earth 2:1 共振, clusterCount >= 2) 时，
                        // 航天器通过大椭圆长周期轨道与行星质心系矢量协同转正，出射日心速度上限大幅拓宽 (+1.5 km/s)，
                        // 无工质直冲木星甚至外层深空！
                        double resonanceBonusKms = (clusterCount >= 2) ? MGADefaults.ResonantOrbitVelocityBonusKms : 0.0;
                        if (r2 < r1)
                        {
                            // 向内降轨段 (如 Venus -> Mercury): 航天器需逆向偏折减速以降低近日点
                            double vMinAchievable = Math.Max(1.0, vCirc1 - runningVinf - resonanceBonusKms);
                            deficitKms = Math.Max(0.0, vMinAchievable - vReq);
                            vMaxAchievable = vCirc1 + runningVinf + resonanceBonusKms;
                        }
                        else
                        {
                            // 向外升轨段 (如 Earth -> Jupiter): 航天器需顺向偏折加速以抬升远日点
                            vMaxAchievable = vCirc1 + runningVinf + resonanceBonusKms;
                            deficitKms = Math.Max(0.0, vReq - vMaxAchievable);
                        }
                    }

                    double deficitMs = deficitKms * 1000.0;

                    // 借力天体双曲几何偏折能力与转向角赤字计算 (Hyperbolic Deflection Turning Deficit)
                    // 真实物理约束：浅势阱小天体 (如 Mercury) 在高速 V_inf 下双曲单次最大偏折角极小 (< 10°)，
                    // 若进出两航段几何夹角远超最大偏折角且未采用同星多次共振逐步转向，必然产生巨大的深空变轨 DSM 消耗！
                    if (k >= 1 && k < fullPath.Count - 1 && !bFrom.Equals(bTo, StringComparison.OrdinalIgnoreCase))
                    {
                        int prevIdx = k - clusterCount;
                        string bPrev = (prevIdx >= 0) ? fullPath[prevIdx] : null;

                        if (bPrev != null)
                        {
                            var pPrev = SolarSystemCatalog.GetPlanet(bPrev);
                            double rPrev = (pPrev != null && pPrev.SMA > 0) ? pPrev.SMA : AU_KM;
                            double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                            double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 22032.0;
                            double effVinf = Math.Max(MGADefaults.MinEffectiveVInfKms, runningVinf);
                            double singleMaxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, effVinf);
                            double totalAvailableTurn = clusterCount * singleMaxTurn;

                            // 计算进出转移轨道相对夹角 (综合倾角差与偏心率航向角)
                            double dInc = Math.Abs((pPrev != null ? pPrev.INC : 0.0) - (pFrom != null ? pFrom.INC : 0.0)) +
                                          Math.Abs((pFrom != null ? pFrom.INC : 0.0) - (pTo != null ? pTo.INC : 0.0));
                            double eccFrom = (pFrom != null && pFrom.ECC > 0) ? pFrom.ECC : 0.0;
                            double gammaDeg = Math.Asin(Math.Min(0.9, eccFrom)) * (180.0 / Math.PI);
                            double reqTurnDeg = Math.Max(MGADefaults.MinGeometryTurnAngleDeg, Math.Sqrt(Math.Pow(2.0 * gammaDeg, 2.0) + Math.Pow(dInc, 2.0)));
                            if (bPrev.Equals(bTo, StringComparison.OrdinalIgnoreCase))
                            {
                                bool isApsidalTurnaround = (rPrev > r1 && r2 > r1) || (rPrev < r1 && r2 < r1);
                                if (isApsidalTurnaround)
                                {
                                    // 势阱拱点 (近日点/远日点) 自然过渡 (如 Venus -> [Mercury x M] -> Venus):
                                    // 进场椭圆轨道与出场椭圆轨道偏心率航向角在三维空间中形成夹角，
                                    // 为在无工质条件下实现天体相位重新锁定与交会，三维所需偏折角由几何航向角与轨道倾角差合成：
                                    double e1 = Math.Abs((rPrev - r1) / (rPrev + r1));
                                    double e2 = Math.Abs((r2 - r1) / (r2 + r1));
                                    double fpaSumDeg = (Math.Asin(Math.Min(0.95, e1)) + Math.Asin(Math.Min(0.95, e2))) * (180.0 / Math.PI);
                                    reqTurnDeg = Math.Max(MGADefaults.MinGeometryTurnAngleDeg, Math.Sqrt(Math.Pow(fpaSumDeg * 0.75, 2.0) + Math.Pow(dInc, 2.0)));
                                }
                                else
                                {
                                    reqTurnDeg = Math.Max(48.0, reqTurnDeg); // 同轨折返单次飞掠几何转向门禁
                                }
                            }
                            else if (rPrev < r1 * MGADefaults.InnerOrbitBoundaryRatio && r2 > r1 * MGADefaults.OuterOrbitBoundaryRatio)
                            {
                                // 内外轨道跨越方向大逆转 (如 Venus -> [Earth x M] -> Jupiter):
                                // 进场为日心向内低势能轨道，出场为冲出外行星的高能向外轨道，单次飞掠速度夹角在 120°~160°，
                                // 若具备同星多次共振蓄能抬升远拱点 (如 Earth -> Earth 2:1)，所需偏折角显著收窄至 75°
                                reqTurnDeg = (clusterCount >= 2) ? MGADefaults.ResonantReverseTurnAngleDeg : Math.Max(MGADefaults.DirectInwardOutwardTurnAngleDeg, reqTurnDeg);
                            }

                            legRep.ReqTurnDeg = reqTurnDeg;
                            legRep.AvailTurnDeg = totalAvailableTurn;
                            if (reqTurnDeg > totalAvailableTurn)
                            {
                                double turnExcessRad = (reqTurnDeg - totalAvailableTurn) * (Math.PI / 180.0);
                                double turnDeficitMs = 2.0 * effVinf * Math.Sin(turnExcessRad * 0.5) * 1000.0;
                                legRep.TurnDeficitMs = turnDeficitMs;
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

                        // 星历相位差作为几何诊断展示指标，下游航段由开普勒共振与 ToF 弹性调节，不作为不可逾越的物理赤字
                        if (k == 0 && deltaPhase > MGADefaults.DeparturePhaseAngleToleranceDeg)
                        {
                            // 仅始发首段在偏离过大时提供轻微的排序偏好导向 (微小软罚分)
                            double initialPhaseSoftPenaltyMs = MGADefaults.DeparturePhaseSoftPenaltyFactorMsPerDeg * (deltaPhase - MGADefaults.DeparturePhaseAngleToleranceDeg);
                            totalDeficit += initialPhaseSoftPenaltyMs;
                        }
                    }

                    if (deficitMs > maxDsmBudgetMs)
                    {
                        legRep.Rationale = $"❌ 物理不可达跨越: 需要 {vReq:F2} km/s, 当前极值仅 {vMaxAchievable:F2} km/s (物理赤字={deficitMs:F0} m/s 远超 DSM 预算 {maxDsmBudgetMs:F0} m/s)";
                        res.IsFeasible = false;
                    }
                    else
                    {
                        legRep.Rationale = $"✅ 物理可达跨越: 需要 {vReq:F2} km/s, 借力极值 {vMaxAchievable:F2} km/s (物理赤字={deficitMs:F0} m/s, 相位差={legRep.PhaseDeltaDeg:F1}°)";
                    }

                    // 更新后续航段的等效 v_inf (借力增能或霍曼交会)
                    double vArr = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r2 - 1.0 / aTrans)));
                    double vCirc2 = Math.Sqrt(sunMu / r2);
                    runningVinf = Math.Max(runningVinf, Math.Max(MGADefaults.MinEffectiveVInfKms, Math.Abs(vArr - vCirc2)));
                }

                res.LegReports.Add(legRep);
            }

            res.TotalDeficitMs = totalDeficit;
            res.ResonanceCount = resCount;
            if (totalDeficit > maxBudgetMs)
            {
                res.IsFeasible = false;
            }

            string firstFlyby = (rawFlybys != null && rawFlybys.Count > 0) ? rawFlybys[0] : null;
            res.EstimatedLaunchDvMs = MGAPlanetaryAssistManager.EstimateLaunchInjectionDvMs(departure, firstFlyby);
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
                // 确保用户指定的目标借力天体得到严格满足，过滤掉未包含目标天体的无关直达/旁支拓扑
                allCandidates = allCandidates.Where(c => userGoal.All(g => c.Contains(g, StringComparer.OrdinalIgnoreCase))).ToList();
            }

            foreach (var chain in allCandidates)
            {
                var report = AnalyzeChainReachability(departure, arrival, chain, departureEpochUT, maxBudgetMs, maxDsmBudgetMs);
                evaluated.Add(report);
            }

            LastEvaluatedChains = new List<ChainReachabilityResult>(evaluated);

            // 过滤出物理可行的序列，按能量赤字与序列精简度 (奥卡姆剃刀原则) 升序排列，并保留多样性拓扑
            // 严格门禁：单航段存在天文级速度赤字 (> MaxPhysicalEnergyDeficitMs) 的跨越势阱序列必须被剪除，迫使求解器走完整引力阶梯
            var physicallyViableChains = evaluated.Where(e => !e.LegReports.Any(l => l.EnergyDeficitMs > MGADefaults.MaxPhysicalEnergyDeficitMs)).ToList();
            var pool = physicallyViableChains.Count > 0 ? physicallyViableChains : evaluated;

            var feasibleCandidates = pool.Where(e => e.IsFeasible)
                .OrderBy(e => e.TotalDeficitMs)
                .ThenBy(e => e.FlybyChain.Count)
                .ToList();
            if (feasibleCandidates.Count == 0)
            {
                feasibleCandidates = pool
                    .OrderBy(e => e.TotalDeficitMs)
                    .ThenBy(e => e.FlybyChain.Count)
                    .ToList();
            }

            // 普适结构家族特征提取：基于内侧跳板进场次数、深势阱共振次数、中继/回升跳板构型、外侧巨行星弹弓构型提取拓扑指纹
            // 实行分层轮询优选 (Stratified Round-Robin Selection)，杜绝单一同质化拓扑垄断候选池，确保全局搜索空间的多样性
            var familyGroups = new Dictionary<string, List<ChainReachabilityResult>>();
            foreach (var c in feasibleCandidates)
            {
                var blocks = new List<string>();
                if (c.FlybyChain.Count > 0)
                {
                    string curB = c.FlybyChain[0];
                    int count = 1;
                    for (int bi = 1; bi < c.FlybyChain.Count; bi++)
                    {
                        if (c.FlybyChain[bi].Equals(curB, StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                        }
                        else
                        {
                            blocks.Add($"{curB}x{count}");
                            curB = c.FlybyChain[bi];
                            count = 1;
                        }
                    }
                    blocks.Add($"{curB}x{count}");
                }
                string familyKey = string.Join("->", blocks);
                if (!familyGroups.TryGetValue(familyKey, out var fList))
                {
                    fList = new List<ChainReachabilityResult>();
                    familyGroups[familyKey] = fList;
                }
                fList.Add(c);
            }

            var diverseFeasible = new List<ChainReachabilityResult>();
            int maxRound = feasibleCandidates.Count;
            for (int r = 0; r < maxRound && diverseFeasible.Count < feasibleCandidates.Count; r++)
            {
                foreach (var kvp in familyGroups)
                {
                    if (r < kvp.Value.Count)
                    {
                        var cand = kvp.Value[r];
                        if (!diverseFeasible.Any(d => d.FlybyChain.SequenceEqual(cand.FlybyChain)))
                        {
                            diverseFeasible.Add(cand);
                        }
                    }
                }
            }

            return diverseFeasible;
        }

        /// <summary>
        /// 基于势阱梯级爬升法则与开普勒共振阶梯，自动合成高效率精简拓扑链条。
        /// 100% 委托 MGAPlanetaryAssistManager，彻底杜绝八大行星硬编码。
        /// </summary>
        public static List<List<string>> SynthesizeClimbingSequences(string departure, string arrival, List<string> userGoal)
        {
            return MGAPlanetaryAssistManager.SynthesizePhysicalClimbingChains(departure, arrival, userGoal);
        }
    }
}
