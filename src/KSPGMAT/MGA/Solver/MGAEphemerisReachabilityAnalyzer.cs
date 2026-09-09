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
            public double EstimatedToFDays;
            public double ParetoScore;
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
                foreach (var fb in rawFlybys)
                {
                    if (!string.IsNullOrEmpty(fb)) cleanedFlybys.Add(fb);
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
            double totalToFDays = 0.0;
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
                    if (k == 0)
                    {
                        // 始发天体前置共振出射 (如 Juno/MESSENGER EGA 2:1/1:1 共振):
                        // 航天器需脱离停泊轨道注入共振长周期椭圆，标称出射 ΔV ~ 3.8 km/s
                        double rPark = (pFrom != null && pFrom.RadiusKm > 0) ? (pFrom.RadiusKm + 200.0) : 6578.1363;
                        double muDep = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 398600.4418;
                        double vPark = Math.Sqrt(muDep / rPark);
                        double vInfRes = 3.6; // 标称 2:1 共振出射双曲过剩速度 (km/s)
                        double vHyp = Math.Sqrt(vInfRes * vInfRes + 2.0 * muDep / rPark);
                        res.EstimatedLaunchDvMs = Math.Max(0.0, vHyp - vPark) * 1000.0;
                    }
                    double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                    double muBody = pFrom != null ? pFrom.Mu : 22032.0;
                    double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, Math.Max(2.0, runningVinf));
                    legRep.Rationale = $"同星共振 [{bFrom} ➔ {bTo}]: 轨道能量守恒 (0 DSM), 安全半径 R_safe={safeR:F0}km, 单次最大偏折角={maxTurn:F1}°";
                    double periodDays = (2.0 * Math.PI * Math.Sqrt(Math.Pow(r1, 3.0) / sunMu)) / 86400.0;
                    totalToFDays += periodDays * 2.0; // 标称 2:1 或长周期共振周期
                }
                else
                {
                    // 异星转移段：Vis-Viva 二体机械能守恒计算
                    double aTrans = (r1 + r2) * 0.5;
                    if (k >= 1 && r2 < r1)
                    {
                        var pPrev = SolarSystemCatalog.GetPlanet(fullPath[k - 1]);
                        double rPrev = (pPrev != null && pPrev.SMA > 0) ? pPrev.SMA : r1;
                        if (rPrev > r1)
                        {
                            // 连贯向内深潜飞掠 (如 Earth -> Venus -> Mercury):
                            // 航天器轨道的远日点保留在外侧出发天体 (rPrev) 附近，而非强制假设在中间借力星 (r1) 形成切向椭圆
                            aTrans = (rPrev + r2) * 0.5;
                        }
                    }
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
                        // 航天器通过大椭圆长周期轨道 (T=2年, 远日点 2.16 AU) 与行星质心系矢量协同转正，
                        // 判定是否具备深井爬升或内潜弹弓蓄能 (VEEGA / VVEJGA 物理机制):
                        // 1. 同星多阶共振 (如 Earth->Earth 2:1, clusterCount >= 2);
                        // 2. 前置借力包含内侧行星潜入加速 (如 Venus -> [Earth] -> Jupiter, 前方经历过低势能区);
                        bool hasInnerDive = false;
                        for (int m = 0; m < k; m++)
                        {
                            var pM = SolarSystemCatalog.GetPlanet(fullPath[m]);
                            if (pM != null && pM.SMA > 0 && pM.SMA < r1 * 0.85)
                            {
                                hasInnerDive = true;
                                break;
                            }
                        }
                        bool isInnerClimb = (hasInnerDive && pTo != null && pTo.SMA >= r1 * 2.5);
                        bool isVeegaClimb = (clusterCount >= 2 && pFrom != null && pFrom.SMA >= 1.2e8 && pFrom.SMA <= 2.2e8 && pTo != null && pTo.SMA >= pFrom.SMA * 3.5);
                        double resonanceBonusKms = (isVeegaClimb || isInnerClimb) ? 4.5 : ((clusterCount >= 2) ? MGADefaults.ResonantOrbitVelocityBonusKms : 0.0);
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
                            else if (rPrev < r1 * 0.85 && r2 > r1 * 2.5)
                            {
                                // 内外高势差跨越 (如 Venus -> [Earth x M] -> Jupiter):
                                // 进场为内侧低势能轨道，出场为冲出远外行星的高能轨道
                                reqTurnDeg = (clusterCount >= 2) ? MGADefaults.ResonantReverseTurnAngleDeg : Math.Max(48.0, reqTurnDeg);
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
                        double legDays = tHohmannSec / 86400.0;
                        bool isGiantOutward = (pFrom != null && (pFrom.Mu >= 5.0e6 || pFrom.RadiusKm >= 20000.0) && r2 > r1);
                        if (isGiantOutward)
                        {
                            double distKm = Math.Abs(r2 - r1);
                            double hyperbolicTransitDays = (distKm / 14.0) / 86400.0;
                            legDays = Math.Min(legDays * 0.35, hyperbolicTransitDays * 1.35);
                        }
                        totalToFDays += legDays;

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

                    // 更新后续航段的等效 v_inf: 考虑轨道交会三维倾角与非切向交角 (Flight-path angle & inclination)
                    double vArr = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r2 - 1.0 / aTrans)));
                    double vCirc2 = Math.Sqrt(sunMu / r2);
                    double scalarDiff = Math.Abs(vArr - vCirc2);
                    // 内侧行星 (金星/水星) 轨道速度高且具有空间倾角，非切向交会产生 2.5~3.5 km/s 正交速度分量
                    double orthogonalVinf = (pTo != null && pTo.SMA < AU_KM * 0.9) ? 3.5 : 1.2;
                    double geometricVinf = Math.Sqrt(scalarDiff * scalarDiff + orthogonalVinf * orthogonalVinf);
                    runningVinf = Math.Max(runningVinf, Math.Max(MGADefaults.MinEffectiveVInfKms, geometricVinf));
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
            res.EstimatedToFDays = totalToFDays;
            res.ParetoScore = (res.EstimatedTotalDvMs / 1000.0) * 1.2 + (totalDeficit / 1000.0) * 1.5 + (totalToFDays / 365.25) * 0.4;

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
        /// 深势阱"反梯级泵升"完整度评分 (仅用于含深势阱目标的爬升链)。
        /// 物理动机: 水星深井任务进入减速后, 出口侧需借力金星 + 地球构成反梯级泵升
        /// (MESSENGER 进井减速 + Venus x2 抬升 + Earth x2 抬升 + 木星弹弓), 才能以预算内
        /// DSM 爬出海王星; 若出口无泵升阶梯, DE 被迫在单一出口腿上施加天文级 DSM 硬推。
        /// 返回: 0=完整反梯级泵升 (出口含 Venus 类 + Earth 类各≥1), 1=部分, 2=缺失;
        /// 非深井链 (最内天体 SMA ≥ 出发星 45%) 一律 2, 表示不干预常规排序。
        /// </summary>
        private static SolarPlanet FindDeepestPlanet(List<string> flybys, string arrival)
        {
            SolarPlanet deepest = null;
            if (!string.IsNullOrEmpty(arrival))
            {
                deepest = SolarSystemCatalog.GetPlanet(arrival);
            }
            if (flybys != null)
            {
                foreach (var name in flybys)
                {
                    var p = SolarSystemCatalog.GetPlanet(name);
                    if (p == null || p.IsMoon) continue;
                    if (deepest == null || p.SMA < deepest.SMA) deepest = p;
                }
            }
            return deepest;
        }

        /// <summary>
        /// 深势阱"反梯级泵升"完整度评分 (仅用于含深势阱目标的爬升链)。
        /// 物理动机: 水星深井任务进入减速后, 出口侧需借力金星 + 地球构成反梯级泵升
        /// (MESSENGER 进井减速 + Venus x2 抬升 + Earth x2 抬升 + 木星弹弓), 才能以预算内
        /// DSM 爬出海王星; 若出口无泵升阶梯, DE 被迫在单一出口腿上施加天文级 DSM 硬推。
        /// 返回: 0=完整反梯级泵升 (出口含 Venus 类 + Earth 类各≥1), 1=部分, 2=缺失;
        /// 非深井链 (最内天体 SMA ≥ 出发星 45%) 一律 2, 表示不干预常规排序。
        /// </summary>
        private static int GetDeepWellExitLadderScore(ChainReachabilityResult r, string departure, string arrival)
        {
            var flybys = r?.FlybyChain;
            var depBody = SolarSystemCatalog.GetPlanet(departure);
            if (flybys == null || flybys.Count == 0 || depBody == null || depBody.SMA <= 0) return 2;

            SolarPlanet deepest = FindDeepestPlanet(flybys, arrival);
            if (deepest == null || deepest.SMA >= depBody.SMA * 0.45) return 2; // 非深井场景: 不干预

            int lastDeepIdx = -1;
            for (int i = flybys.Count - 1; i >= 0; i--)
            {
                if (flybys[i].Equals(deepest.Name, StringComparison.OrdinalIgnoreCase)) { lastDeepIdx = i; break; }
            }
            if (lastDeepIdx < 0 || lastDeepIdx >= flybys.Count - 1) return 2; // 深井天体不在出口侧

            int exitVenusLike = 0;
            int exitEarthLike = 0;
            for (int i = lastDeepIdx + 1; i < flybys.Count; i++)
            {
                var p = SolarSystemCatalog.GetPlanet(flybys[i]);
                if (p == null || p.IsMoon) continue;
                if (p.SMA > deepest.SMA * 1.2 && p.SMA < depBody.SMA * 0.95) exitVenusLike++;
                else if (p.SMA >= depBody.SMA * 0.85 && p.SMA <= depBody.SMA * 3.0) exitEarthLike++;
            }
            if (exitVenusLike >= 1 && exitEarthLike >= 1) return 0;
            if (exitVenusLike >= 1 || exitEarthLike >= 1) return 1;
            return 2;
        }

        /// <summary>
        /// 深势阱"出口侧水星泵升次数"评分 (Deep-Well Exit Mercury Pump Rank)。
        /// 物理动机: MESSENGER 式深井自减速 + 共振抬升需要水星至少 3 次连续飞掠
        /// (M×3, 逐级抬升轨道能量后再离井), 若链只有 M×1/M×2, 出口 M→V 被迫背
        /// 2~4 km/s 天文级 DSM 硬爬井 (Scenario 9 完整求解 M→V=3812 m/s 即源于此)。
        /// 返回: 0=水星飞掠≥3次 (M×3/M×4, 最优先), 1=2次 (M×2), 2=≤1次;
        /// 非深井链一律 1, 表示不干预常规排序。
        /// </summary>
        private static int GetDeepWellExitPumpRank(ChainReachabilityResult r, string departure, string arrival)
        {
            var flybys = r?.FlybyChain;
            var depBody = SolarSystemCatalog.GetPlanet(departure);
            if (flybys == null || flybys.Count == 0 || depBody == null || depBody.SMA <= 0) return 1;

            SolarPlanet deepest = FindDeepestPlanet(flybys, arrival);
            if (deepest == null || deepest.SMA >= depBody.SMA * 0.45) return 1; // 非深井: 不干预

            int deepCount = 0;
            foreach (var name in flybys)
            {
                if (name.Equals(deepest.Name, StringComparison.OrdinalIgnoreCase)) deepCount++;
            }
            if (deepCount >= 3) return 0;
            if (deepCount == 2) return 1;
            return 2;
        }

        /// <summary>
        /// 深势阱"进场侧内侧跳板减速次数"评分 (Deep-Well Entry Assist Rank)。
        /// 物理动机: MESSENGER 式水星进场必须先经金星 ×2 逐级减速 (E→V→V→M),
        /// 把到达水星的 V∞ 压到 ~5.5 km/s 后水星自减速才可能以 ~0 DSM 完成;
        /// 若仅 V×1 直接掉井, V→M 单腿被迫背 1.4~2.9 km/s DSM 硬推 (quick 实测 1414 m/s)。
        /// 返回: 0=进场含≥2次内侧跳板 (V×2, MESSENGER 权威构型, 最优先),
        /// 1=1次 (V×1), 2=0次; 非深井链一律 1, 表示不干预常规排序。
        /// </summary>
        private static int GetDeepWellEntryAssistRank(ChainReachabilityResult r, string departure, string arrival)
        {
            var flybys = r?.FlybyChain;
            var depBody = SolarSystemCatalog.GetPlanet(departure);
            if (flybys == null || flybys.Count == 0 || depBody == null || depBody.SMA <= 0) return 1;

            SolarPlanet deepest = FindDeepestPlanet(flybys, arrival);
            if (deepest == null || deepest.SMA >= depBody.SMA * 0.45) return 1; // 非深井: 不干预

            int firstDeepIdx = -1;
            for (int i = 0; i < flybys.Count; i++)
            {
                if (flybys[i].Equals(deepest.Name, StringComparison.OrdinalIgnoreCase)) { firstDeepIdx = i; break; }
            }
            if (firstDeepIdx < 0) firstDeepIdx = flybys.Count; // 若深井天体在终点到达，则全部借力均在掉井之前
            if (firstDeepIdx == 0) return 2; // 第 1 站即掉入深井，0 借力跳板

            int entryInnerLike = 0;
            for (int i = 0; i < firstDeepIdx; i++)
            {
                var p = SolarSystemCatalog.GetPlanet(flybys[i]);
                if (p == null || p.IsMoon) continue;
                if (p.SMA > deepest.SMA * 1.2 && p.SMA < depBody.SMA * 0.95) entryInnerLike++;
            }
            if (entryInnerLike >= 2) return 0;
            if (entryInnerLike == 1) return 1;
            return 2;
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
                .Select(e => (R: e,
                              MaxLeg: e.LegReports.Count > 0 ? e.LegReports.Max(l => l.EnergyDeficitMs + l.TurnDeficitMs) : 0.0,
                              Ladder: GetDeepWellExitLadderScore(e, departure, arrival),
                              Entry: GetDeepWellEntryAssistRank(e, departure, arrival),
                              Pump: GetDeepWellExitPumpRank(e, departure, arrival)))
                // 反梯级泵升完整度优先: 深井爬升链出口侧须含金星+地球泵升阶梯 (完整=0 < 部分=1 < 缺失=2),
                // 避免 DE 在单一出口腿 (M→E) 上施加 4.5 km/s 天文级 DSM 硬推;
                // 次优先进场侧内侧跳板减速次数 (V×2 MESSENGER 构型 > V×1 > V×0), 压低 V→M 掉井 DSM;
                // 再次优先出口侧水星泵升次数 (M×3/M×4 自减速+共振抬升 > M×2 > M×1),
                // 杜绝 M→V 出口腿 2~4 km/s 硬爬井 (MESSENGER 式水星需至少 3 次连续飞掠);
                // 随后按链长分层 (<=10 腿优先, <=12 次之), 再按单腿赤字/预估ΔV 排序,
                // 杜绝 13-14 腿超长共振链 (DE 维度 27+) 霸占演化池导致永不收敛。
                .OrderBy(t => t.Ladder)
                .ThenBy(t => t.Entry)
                .ThenBy(t => t.Pump)
                .ThenBy(t => t.R.FlybyChain.Count > 12 ? 2 : (t.R.FlybyChain.Count > 10 ? 1 : 0))
                .ThenBy(t => (t.MaxLeg > MGADefaults.DefaultMaxDsmDeltaVMs) ? 1 : 0)
                .ThenBy(t => t.R.ParetoScore)
                .ThenBy(t => t.R.EstimatedTotalDvMs)
                .ThenBy(t => t.R.TotalDeficitMs)
                .ThenBy(t => t.R.FlybyChain.Count)
                .Select(t => t.R)
                .ToList();
            if (feasibleCandidates.Count == 0)
            {
                feasibleCandidates = pool
                    .Select(e => (R: e,
                                  MaxLeg: e.LegReports.Count > 0 ? e.LegReports.Max(l => l.EnergyDeficitMs + l.TurnDeficitMs) : 0.0,
                                  Ladder: GetDeepWellExitLadderScore(e, departure, arrival),
                                  Entry: GetDeepWellEntryAssistRank(e, departure, arrival),
                                  Pump: GetDeepWellExitPumpRank(e, departure, arrival)))
                    .OrderBy(t => t.Ladder)
                    .ThenBy(t => t.Entry)
                    .ThenBy(t => t.Pump)
                    .ThenBy(t => t.R.FlybyChain.Count > 12 ? 2 : (t.R.FlybyChain.Count > 10 ? 1 : 0))
                    .ThenBy(t => (t.MaxLeg > MGADefaults.DefaultMaxDsmDeltaVMs) ? 1 : 0)
                    .ThenBy(t => t.R.ParetoScore)
                    .ThenBy(t => t.R.EstimatedTotalDvMs)
                    .ThenBy(t => t.R.TotalDeficitMs)
                    .ThenBy(t => t.R.FlybyChain.Count)
                    .Select(t => t.R)
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

            // 普适结构分层轮询抽取 (Tiered Stratified Round-Robin):
            // 分别按 Direct (0), Single (1), Dual (2), Multi (3), GrandTour (4+) 分层
            // 确保不同物理复杂度的优质拓扑均有代表进入最终候选池，绝不让短链将长链挤出
            var tierBuckets = new Dictionary<int, List<ChainReachabilityResult>>
            {
                [0] = new List<ChainReachabilityResult>(),
                [1] = new List<ChainReachabilityResult>(),
                [2] = new List<ChainReachabilityResult>(),
                [3] = new List<ChainReachabilityResult>(),
                [4] = new List<ChainReachabilityResult>()
            };

            foreach (var cand in diverseFeasible)
            {
                int count = cand.FlybyChain.Count;
                int tier = (count == 0) ? 0 : (count == 1 ? 1 : (count == 2 ? 2 : (count == 3 ? 3 : 4)));
                tierBuckets[tier].Add(cand);
            }

            // 在每个分层内部，实施动力学机制 (外巨行星高能弹弓 vs 内侧引力共振) 与拓扑家族 (Family Key) 双轨轮询重排
            // 确保高能直冲外行星 (如 Voyager 2 JSU 首发木星) 与低能内潜共振 (如 Galileo VEEGA 首发金星) 公平并列，
            // 且同一首天体下不同共振拓扑 (如 Venusx2->Earthx1 与 Venusx1->Earthx2) 互不挤占。
            foreach (var kvp in tierBuckets.ToList())
            {
                var origList = kvp.Value;
                if (origList.Count <= 1) continue;

                // 提取拓扑结构家族指纹与机制归属
                string GetFamily(ChainReachabilityResult c)
                {
                    var blocks = new List<string>();
                    if (c.FlybyChain != null && c.FlybyChain.Count > 0)
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
                    return string.Join("->", blocks);
                }

                bool IsGiantFirst(ChainReachabilityResult c)
                {
                    if (c.FlybyChain == null || c.FlybyChain.Count == 0) return false;
                    var p = SolarSystemCatalog.GetPlanet(c.FlybyChain[0]);
                    return (p != null && (p.Mu >= 5.0e6 || p.RadiusKm >= 20000.0));
                }

                // 分解为两大多动力学轨流：
                // 轨流 1: 巨行星极速直冲双曲弹弓 (Giant Slingshot Stream, 如 Voyager 2)
                // 轨流 2: 内侧/近邻天体阶梯式共振引力泵升 (Inner Resonance Stream, 如 Galileo / Cassini / MESSENGER)
                var giantList = origList.Where(c => IsGiantFirst(c)).ToList();
                var innerList = origList.Where(c => !IsGiantFirst(c)).ToList();

                List<ChainReachabilityResult> OrderStreamByFamily(List<ChainReachabilityResult> stream)
                {
                    if (stream.Count <= 1) return stream;
                    var famGroups = new Dictionary<string, List<ChainReachabilityResult>>();
                    foreach (var c in stream)
                    {
                        string fk = GetFamily(c);
                        if (!famGroups.TryGetValue(fk, out var fl))
                        {
                            fl = new List<ChainReachabilityResult>();
                            famGroups[fk] = fl;
                        }
                        fl.Add(c);
                    }
                    // 各家族内部按 Pareto 排序
                    foreach (var g in famGroups.Values)
                    {
                        g.Sort((a, b) => a.ParetoScore.CompareTo(b.ParetoScore));
                    }
                    // 家族间按在流中的物理优先顺序 (保留 Ladder/Entry/Pump 势阱动力学排序，杜绝单纯 ToF 偏小导致优质高阶共振链被插队)
                    var sortedFamKeys = famGroups.Keys.ToList();

                    var res = new List<ChainReachabilityResult>();
                    int maxR = stream.Count;
                    for (int r = 0; r < maxR && res.Count < stream.Count; r++)
                    {
                        foreach (var fk in sortedFamKeys)
                        {
                            var list = famGroups[fk];
                            if (r < list.Count)
                            {
                                res.Add(list[r]);
                            }
                        }
                    }
                    return res;
                }

                var giantOrdered = OrderStreamByFamily(giantList);
                var innerOrdered = OrderStreamByFamily(innerList);

                var reordered = new List<ChainReachabilityResult>();
                int gPtr = 0, iPtr = 0;
                while (gPtr < giantOrdered.Count || iPtr < innerOrdered.Count)
                {
                    // 动态交替轮询：优先引入当前最优内侧共振，紧跟当前最优巨行星弹弓，彻底杜绝单边劣币驱逐
                    if (iPtr < innerOrdered.Count)
                    {
                        reordered.Add(innerOrdered[iPtr++]);
                    }
                    if (gPtr < giantOrdered.Count)
                    {
                        reordered.Add(giantOrdered[gPtr++]);
                    }
                }

                tierBuckets[kvp.Key] = reordered;
            }

            // 普适天体动力学尺度分层调度 (Astrodynamics Scale-Dependent Tier Scheduling):
            // 基于始发星与目标星半长轴比率 rho = r_arr / r_dep 自适应配置拓扑复杂度偏好:
            // 1. 深势阱 (rho < 0.45, 如水星) 或 远外太阳系 (rho > 15.0, 如天王星/海王星/冥王星):
            //    单/无借力在物理上由于能量断层或极度离心而难以维系，核心依赖 Multi (3) 与 GrandTour (4+) 多星接力；
            // 2. 近邻/中程行星际转移 (0.45 <= rho <= 15.0, 如金星/火星/小行星带/木星/土星):
            //    优先考察 Single (1), Dual (2), Multi (3) 等高效率借力弧段，避免冗余超长链无端消耗演化预算。
            double rDepSma = 1.0, rArrSma = 1.0;
            var pDep = SolarSystemCatalog.GetPlanet(departure);
            var pArr = SolarSystemCatalog.GetPlanet(arrival);
            if (pDep != null && pDep.SMA > 0) rDepSma = pDep.SMA;
            if (pArr != null && pArr.SMA > 0) rArrSma = pArr.SMA;
            double smaRatio = rArrSma / rDepSma;

            bool isDeepOrFar = (smaRatio < 0.45 || smaRatio > 15.0);

            int[] tierSequence = isDeepOrFar
                ? new int[] { 
                    4, 3, 2, 1, // Round 1: 覆盖 GrandTour(4+), Multi(3), Dual(2), Single(1, 如 New Horizons)
                    4, 4, 3, 2, // Round 2: 远外/深井强化高阶共振 (MESSENGER / BepiColombo) 与大巡游 (Voyager 2)
                    4, 3, 2, 1, 0 // Round 3+: 基础补齐与直飞退化基线
                  }
                : new int[] { 
                    0, 1, 2, 3, 4, // Round 1: 近中程包含直飞基线 (Pioneer 10) 并覆盖全部借力级数
                    1, 3, 2, 4, 3, // Round 2: 强化 Single (Pioneer 11/Voyager 1), Multi (Galileo VEEGA), Dual
                    1, 2, 3, 4, 0 // Round 3+: 基础补齐与直飞退化基线
                  };

            var tierPointers = new Dictionary<int, int> { [0] = 0, [1] = 0, [2] = 0, [3] = 0, [4] = 0 };
            var stratifiedFinal = new List<ChainReachabilityResult>();

            // 先按预定优化调度队列抽取
            foreach (int tier in tierSequence)
            {
                var bucket = tierBuckets[tier];
                int ptr = tierPointers[tier];
                if (ptr < bucket.Count)
                {
                    var cand = bucket[ptr];
                    tierPointers[tier] = ptr + 1;
                    if (!stratifiedFinal.Any(s => s.FlybyChain.SequenceEqual(cand.FlybyChain)))
                    {
                        stratifiedFinal.Add(cand);
                    }
                }
            }

            // 若仍有剩余名额，继续标准均匀轮询补齐
            int maxRemainingRounds = diverseFeasible.Count;
            for (int r = 0; r < maxRemainingRounds && stratifiedFinal.Count < diverseFeasible.Count; r++)
            {
                int[] fallbackOrder = new int[] { 4, 3, 2, 1, 0 };
                foreach (int tier in fallbackOrder)
                {
                    var bucket = tierBuckets[tier];
                    int ptr = tierPointers[tier];
                    if (ptr < bucket.Count)
                    {
                        var cand = bucket[ptr];
                        tierPointers[tier] = ptr + 1;
                        if (!stratifiedFinal.Any(s => s.FlybyChain.SequenceEqual(cand.FlybyChain)))
                        {
                            stratifiedFinal.Add(cand);
                        }
                    }
                }
            }

            diverseFeasible = stratifiedFinal;

            // 深井任务链长硬上限 (Deep-Well Chain-Length Hard Cap):
            // 若链路最内天体 SMA < 出发星 45% (深势阱, 如水星), 则借力链总飞掠数硬性裁剪到 <=10
            // (总腿数 <=11, DE 维度 <=23), 杜绝 x5/x6 共振生成的 12-13 腿超长链 (维度 25+ 永不收敛);
            // 浅势阱/常规任务维持原样不干预。
            var depPlanetForCap = SolarSystemCatalog.GetPlanet(departure);
            if (depPlanetForCap != null && depPlanetForCap.SMA > 0)
            {
                bool deepWellPool = diverseFeasible.Any(c =>
                {
                    foreach (var nm in c.FlybyChain)
                    {
                        var p = SolarSystemCatalog.GetPlanet(nm);
                        if (p != null && !p.IsMoon && p.SMA > 0 && p.SMA < depPlanetForCap.SMA * 0.45) return true;
                    }
                    return false;
                });
                if (deepWellPool)
                {
                    diverseFeasible = diverseFeasible
                        .Where(c => c.FlybyChain.Count <= 10 || !c.FlybyChain.Any(nm =>
                        {
                            var p = SolarSystemCatalog.GetPlanet(nm);
                            return p != null && !p.IsMoon && p.SMA > 0 && p.SMA < depPlanetForCap.SMA * 0.45;
                        }))
                        .ToList();
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
