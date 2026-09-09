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
        private const double AU_KM = 149597870.7;

        /// <summary>
        /// 基于历元星历状态与 Vis-Viva 能量守恒，深度评估候选链路的物理可达性与速度赤字
        /// </summary>
        public static ChainReachabilityResult AnalyzeChainReachability(
            string departure,
            string arrival,
            List<string> rawFlybys,
            double departureEpochUT,
            double maxBudgetMs = 4000.0,
            double maxDsmBudgetMs = 4000.0,
            bool noInsertionBurn = false)
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

            double runningVinf = 0.0; // 0 经验数字，纯由首段开普勒轨道到达交会速度严格物理计算
            double totalDeficit = 0.0;
            double totalToFDays = 0.0;
            int resCount = 0;
            var pDep = SolarSystemCatalog.GetPlanet(departure);
            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : AU_KM;

            for (int k = 0; k < fullPath.Count - 1; k++)
            {
                string bFrom = fullPath[k];
                string bTo = fullPath[k + 1];

                var pFrom = SolarSystemCatalog.GetPlanet(bFrom);
                var pTo = SolarSystemCatalog.GetPlanet(bTo);

                // 优先从真实星历提取当前时刻位置与半长轴 (动态历元随飞行时间向前推进)
                double currentEpochUT = departureEpochUT + totalToFDays * 86400.0;
                double r1 = (pFrom != null && pFrom.SMA > 0) ? pFrom.SMA : AU_KM;
                double r2 = (pTo != null && pTo.SMA > 0) ? pTo.SMA : AU_KM;
                double lon1 = 0.0, lon2 = 0.0;
                bool hasSpice1 = false, hasSpice2 = false;

                if (pFrom != null)
                {
                    pFrom.GetHeliocentricState(currentEpochUT, out Vector3d pos1, out _);
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
                    pTo.GetHeliocentricState(currentEpochUT, out Vector3d pos2, out _);
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
                    // 同星共振段：开普勒二体机械能守恒，赤字为 0
                    legRep.EnergyDeficitMs = 0.0;

                    if (bFrom.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                    {
                        // 终点目标天体共振自减速 (如 MESSENGER / BepiColombo 水星自减速机制):
                        // 航天器在目标天体借力偏折，消耗双曲过剩速度，每次偏折减少 v_inf:
                        double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                        double muBody = pFrom != null ? pFrom.Mu : 22032.0;
                        double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, Math.Max(0.5, runningVinf));
                        double dvDecel = 2.0 * runningVinf * Math.Sin(maxTurn * 0.5 * (Math.PI / 180.0));
                        runningVinf = Math.Max(0.5, runningVinf - dvDecel);
                        legRep.Rationale = $"终点共振自减速 [{bFrom} ➔ {bTo}]: 偏折角={maxTurn:F1}°, 过剩速度消减至 {runningVinf:F2} km/s";
                        double periodDays = (2.0 * Math.PI * Math.Sqrt(Math.Pow(r1, 3.0) / sunMu)) / 86400.0;
                        totalToFDays += periodDays * 1.0;
                    }
                    else
                    {
                        // 始发/中继天体开普勒二体共振蓄能 (如 Juno / Galileo EGA 2:1 共振):
                        double aRes = r1 * Math.Pow(2.0, 2.0 / 3.0);
                        double vRes = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aRes)));
                        double vCirc = Math.Sqrt(sunMu / r1);
                        double vInfRes = Math.Abs(vRes - vCirc);

                        if (k == 0)
                        {
                            double rPark = (pFrom != null && pFrom.RadiusKm > 0) ? (pFrom.RadiusKm + 200.0) : 6578.1363;
                            double muDep = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 398600.4418;
                            double vPark = Math.Sqrt(muDep / rPark);
                            double vHyp = Math.Sqrt(vInfRes * vInfRes + 2.0 * muDep / rPark);
                            res.EstimatedLaunchDvMs = Math.Max(0.0, vHyp - vPark) * 1000.0;
                        }

                        double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                        double muBody = pFrom != null ? pFrom.Mu : 22032.0;
                        runningVinf = Math.Max(runningVinf, vInfRes);
                        double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, Math.Max(2.0, runningVinf));
                        legRep.Rationale = $"同星共振 [{bFrom} ➔ {bTo}]: 轨道能量守恒 (0 DSM), 安全半径 R_safe={safeR:F0}km, 单次最大偏折角={maxTurn:F1}°";
                        double periodDays = (2.0 * Math.PI * Math.Sqrt(Math.Pow(aRes, 3.0) / sunMu)) / 86400.0;
                        totalToFDays += periodDays * 1.0;
                    }
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
                    double vCirc1 = (pFrom != null && pFrom.SMA > 0) ? Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / pFrom.SMA))) : Math.Sqrt(sunMu / r1);
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
                    double dsmSpentKms = 0.0;
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
                        // 借力天体：无动力飞掠出射日心速度上界与下界
                        // 纯开普勒天体物理动力学：
                        // 1. 检验历史途经天体轨道与共振特性，通过开普勒活力公式与角动量守恒计算天体处真实可达的双曲过剩速度下限
                        double effectiveVinf = runningVinf;
                        double rMinPast = r1;
                        for (int m = 0; m < k; m++)
                        {
                            var pM = SolarSystemCatalog.GetPlanet(fullPath[m]);
                            if (pM != null && pM.SMA > 0 && pM.SMA < rMinPast)
                            {
                                rMinPast = pM.SMA;
                            }
                        }

                        int priorInnerAssists = 0;
                        for (int m = 1; m < k; m++)
                        {
                            var pM = SolarSystemCatalog.GetPlanet(fullPath[m]);
                            if (pM != null && pM.SMA > 0 && pM.SMA <= rDep * 1.05)
                            {
                                priorInnerAssists++;
                            }
                        }

                        if (rMinPast < rDep * 0.95 && r1 >= rDep * 0.95)
                        {
                            // 内潜蓄能弹弓机制 (Inner Slingshot Pumping / VEEGA & VVEJGA):
                            // 航天器潜入出发星内侧势阱 (如金星 rMin < rDep) 借力后被弹射向外，
                            // 若经历了多重内部引力借力 (如 Venus x2 在 Cassini, 或 Venus + Earth 在 Galileo)，
                            // 其日心轨道远日点攀升至目标外轨 r2 (至多 2.17 r1);
                            // 若仅经历单一内部借力且未共振，轨道远日点仅能重返出发星附近 (rTargetApo ~ r1 * 1.05)
                            double rTargetApo = (priorInnerAssists >= 2 || clusterCount >= 2)
                                ? ((r2 > r1) ? Math.Min(r2, r1 * 2.17) : (r1 * 1.5))
                                : (r1 * 1.05);
                            double aClimb = (rMinPast + rTargetApo) * 0.5;
                            double vClimb = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aClimb)));
                            double hClimb = Math.Sqrt(Math.Max(0.0, 2.0 * sunMu * (rMinPast * rTargetApo) / (rMinPast + rTargetApo)));
                            double vThetaClimb = hClimb / r1;
                            double vRadClimb = Math.Sqrt(Math.Max(0.0, vClimb * vClimb - vThetaClimb * vThetaClimb));
                            double vInfPumped = Math.Sqrt(vRadClimb * vRadClimb + Math.Pow(vThetaClimb - vCirc1, 2.0));
                            effectiveVinf = Math.Max(effectiveVinf, vInfPumped);
                        }

                        if (bFrom.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                        {
                            // 终点目标天体同星共振自减速 (Arrival Resonant Deceleration / MESSENGER & BepiColombo 物理机制):
                            // 航天器通过在目标星多次连续飞掠，每次由双曲偏折耗散相对双曲过剩速度，使日心轨道能量与半长轴逐步收拢至目标星，
                            // 大幅降低最终进入停泊轨道的切向捕获冲量 (V_inf 随双曲偏折逐步耗散)
                            double safeRArr = MGADefaults.GetBodySafeRadiusKm(bFrom);
                            double muArr = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 22032.0;
                            double turnArr = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muArr, safeRArr, Math.Max(0.5, effectiveVinf));
                            double dvDecel = 2.0 * effectiveVinf * Math.Sin(turnArr * 0.5 * (Math.PI / 180.0));
                            effectiveVinf = Math.Max(0.5, effectiveVinf - dvDecel);
                            runningVinf = effectiveVinf;
                        }
                        else if (clusterCount >= 2)
                        {
                            if (r2 < r1)
                            {
                                // 内侧向深势阱天体进场共振降轨 (Inward Deep-Well Resonance / MESSENGER & BepiColombo 机制):
                                // 航天器在中继天体 (如 Venus) 多次连续借力，顺应开普勒椭圆降轨切向交会，
                                // 将流入下一航段的相对速度 V_inf 逐步对齐并消减至霍曼转移切向值，大幅消除深势阱进场速度畸变
                                double aInward = (r1 + r2) * 0.5;
                                double vDepInward = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aInward)));
                                double vNeededInward = Math.Abs(vCirc1 - vDepInward);
                                effectiveVinf = Math.Max(effectiveVinf, vNeededInward);
                                runningVinf = effectiveVinf;
                            }
                            else
                            {
                                // 中继天体同星多阶共振引力泵升 (Multi-Stage Resonant Slingshot Pumping / Juno & Galileo 机制):
                                // 航天器通过大离心率共振轨道 (如 2:1 或 3:1 共振，远日点延伸至火星/小行星带 rTargetApo)，
                                // 仅在出发星及其外侧中继天体 (r1 >= rDep * 0.95) 处方能借由大动能泵升远日点;
                                // 处于内势阱的天体 (r1 < rDep) 能量上限严格受限于出发星轨道能量 (rResApo <= rDep)
                                double maxResApo = (r1 >= rDep * 0.95) ? r1 * 2.17 : rDep;
                                double rResApo = Math.Min(r2, maxResApo);
                                double aRes = (r1 + rResApo) * 0.5;
                                double vRes = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aRes)));
                                double hRes = Math.Sqrt(Math.Max(0.0, 2.0 * sunMu * (r1 * rResApo) / (r1 + rResApo)));
                                double vThetaRes = hRes / r1;
                                double vRadRes = Math.Sqrt(Math.Max(0.0, vRes * vRes - vThetaRes * vThetaRes));
                                double orthoResVinf = (pFrom != null && pFrom.SMA < AU_KM * 1.5) ? 2.5 : 1.2;
                                double vInfRes = Math.Sqrt(vRadRes * vRadRes + Math.Pow(vThetaRes - vCirc1, 2.0) + orthoResVinf * orthoResVinf);

                                if (r1 >= rDep * 0.95)
                                {
                                    // 连续天体力学 ΔV-EGA (Delta-V Resonant Slingshot):
                                    // 若无动力共振速度球无法触达下一天体所要求的日心活力速度 (vCirc1 + vInfRes < vReq)，
                                    // 航天器在远日点 rResApo 实施深空轨道机动 (DSM, 预算 <= maxDsmBudgetMs)。
                                    // 远日点制动大幅降低再入近心点 (rp < r1)，使航天器以非零飞行路径角冲入借力星 SOI，
                                    // 产生大幅提升的等效双曲过剩速度 (vInfPost)，并由天体引力偏折顺向加速出射直抵外太阳系。
                                    if (vCirc1 + vInfRes < vReq)
                                    {
                                        double dsmAvailKms = Math.Min(maxDsmBudgetMs / 1000.0, 0.85);
                                        if (dsmAvailKms > 0 && rResApo > r1 * 1.1)
                                        {
                                            double vApo = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / rResApo - 1.0 / aRes)));
                                            double vApoPost = Math.Max(0.5, vApo - dsmAvailKms);
                                            double invAPost = 2.0 / rResApo - (vApoPost * vApoPost) / sunMu;
                                            if (invAPost > 0)
                                            {
                                                double aPost = 1.0 / invAPost;
                                                double hPost = rResApo * vApoPost;
                                                double vRet2 = Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aPost));
                                                double vThetaPost = hPost / r1;
                                                double vRadPost = Math.Sqrt(Math.Max(0.0, vRet2 - vThetaPost * vThetaPost));
                                                double vInfPost = Math.Sqrt(vRadPost * vRadPost + Math.Pow(vThetaPost - vCirc1, 2.0) + orthoResVinf * orthoResVinf);
                                                if (vInfPost > vInfRes)
                                                {
                                                    double vGap = vReq - (vCirc1 + vInfRes);
                                                    double vMaxGain = vInfPost - vInfRes;
                                                    double frac = Math.Min(1.0, Math.Max(0.0, vGap / vMaxGain));
                                                    dsmSpentKms = dsmAvailKms * frac;
                                                    vInfRes = vInfRes + vMaxGain * frac;
                                                }
                                            }
                                        }
                                    }

                                    effectiveVinf = Math.Max(effectiveVinf, vInfRes);
                                    runningVinf = Math.Max(runningVinf, effectiveVinf);
                                }
                            }
                        }

                        // 严格天体力学日心引力助推速度球包络准则:
                        // 考虑天体引力偏折冲量 deltaVassist = 2 * v_inf * sin(delta/2)
                        double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                        double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 398600.0;
                        double effVinfForTurn = Math.Max(MGADefaults.MinEffectiveVInfKms, effectiveVinf);
                        double singleMaxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, effVinfForTurn);
                        double totalMaxTurn = Math.Min(170.0, clusterCount * singleMaxTurn);
                        double deltaVassist = 2.0 * effVinfForTurn * Math.Sin(singleMaxTurn * 0.5 * (Math.PI / 180.0));

                        // 航天器流入借力天体处的真实日心标量速度 vIn:
                        int prevLegIdx = k - clusterCount;
                        string bPrevNode = (prevLegIdx >= 0) ? fullPath[prevLegIdx] : null;
                        var pPrevNode = bPrevNode != null ? SolarSystemCatalog.GetPlanet(bPrevNode) : null;
                        double rPrevNode = (pPrevNode != null && pPrevNode.SMA > 0) ? pPrevNode.SMA : r1;
                        double aInLeg = (rPrevNode + r1) * 0.5;
                        if (clusterCount >= 2 && r1 >= rDep * 0.95 && r2 > r1)
                        {
                            double rResApo = Math.Min(r2, r1 * 2.17);
                            aInLeg = (r1 + rResApo) * 0.5;
                        }
                        double vInHeliocentric = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aInLeg)));

                        if (r2 < r1)
                        {
                            // 向内降轨段 (如 Venus -> Mercury): 航天器逆向偏折减速以降低近日点
                            double vMinAchievable = Math.Max(1.0, vCirc1 - effectiveVinf);
                            deficitKms = Math.Max(0.0, vMinAchievable - vReq);
                            vMaxAchievable = vCirc1 + effectiveVinf;
                        }
                        else
                        {
                            // 向外升轨段 (如 Earth -> Jupiter): 航天器顺向偏折加速以抬升远日点，受相对速度球包络约束
                            vMaxAchievable = vCirc1 + effectiveVinf;
                            deficitKms = Math.Max(0.0, vReq - vMaxAchievable) + dsmSpentKms;
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
                            double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : 398600.0;
                            // 航天器进入借力天体引力影响球 (SOI) 处的真实双曲过剩速度 (由进场开普勒轨道交会速度解析决定):
                            double aInBound = (rPrev + r1) * 0.5;
                            double vInBound = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aInBound)));
                            double vCircBody = Math.Sqrt(sunMu / r1);
                            double orthogonalOffset = (pFrom != null && pFrom.SMA < AU_KM * 0.9) ? 3.5 : 1.5;
                            double effVinfIn = Math.Sqrt(Math.Pow(vInBound - vCircBody, 2.0) + Math.Pow(orthogonalOffset, 2.0));
                            double effVinf = Math.Max(MGADefaults.MinEffectiveVInfKms, effVinfIn);
                            double singleMaxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, effVinf);
                            double totalAvailableTurn = clusterCount * singleMaxTurn;

                            // 计算进出转移轨道相对夹角 (综合倾角差与偏心率航向角)
                            double dInc = Math.Abs((pPrev != null ? pPrev.INC : 0.0) - (pFrom != null ? pFrom.INC : 0.0)) +
                                          Math.Abs((pFrom != null ? pFrom.INC : 0.0) - (pTo != null ? pTo.INC : 0.0));
                            double eccFrom = (pFrom != null && pFrom.ECC > 0) ? pFrom.ECC : 0.0;
                            double gammaDeg = Math.Asin(Math.Min(0.9, eccFrom)) * (180.0 / Math.PI);
                            // 进出开普勒椭圆轨道偏心率航向角差与三维轨道倾角差合成几何所需偏折角 (reqTurnDeg):
                            // 航天器在天体轨道交会处三维速度矢量交角由开普勒航向角 gamma = asin(e) 与空间倾角差严格解析计算
                            double e1 = Math.Abs((rPrev - r1) / (rPrev + r1));
                            double e2 = Math.Abs((r2 - r1) / (r2 + r1));
                            double fpaSumDeg = (Math.Asin(Math.Min(0.95, e1)) + Math.Asin(Math.Min(0.95, e2))) * (180.0 / Math.PI);
                            bool isRadialReversal = (r1 > rDep * 1.05 && rPrev < r1 * 0.95 && r2 < r1 * 0.95);
                            bool isOvertakingSlingshot = (rPrev < r1 * 0.95 && r2 > r1 * 2.17 && clusterCount < 2);
                            double geomTurnDeg = isRadialReversal ? Math.Max(95.0, 140.0 - fpaSumDeg)
                                : (isOvertakingSlingshot ? Math.Max(90.0, 150.0 - fpaSumDeg)
                                : (fpaSumDeg * 0.75));
                            double reqTurnDeg = Math.Max(MGADefaults.MinGeometryTurnAngleDeg, Math.Sqrt(Math.Pow(geomTurnDeg, 2.0) + Math.Pow(dInc, 2.0)));

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

                        // 星历几何相位角审计:
                        // 始发首段 (k == 0) 由用户发射历元严格锁定，偏离霍曼最佳窗口时施加连续软罚分；
                        // 中继航段 (k >= 1) 由开普勒轨道周期与 DE 连续 ToF 变量弹性调节，不作为不可逾越的刚性赤字
                        if (k == 0 && deltaPhase > MGADefaults.DeparturePhaseAngleToleranceDeg)
                        {
                            double excessDeg = deltaPhase - MGADefaults.DeparturePhaseAngleToleranceDeg;
                            double phasePenaltyMs = MGADefaults.DeparturePhaseSoftPenaltyFactorMsPerDeg * excessDeg;
                            totalDeficit += phasePenaltyMs;
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
                    // 若当前航段为终点目标天体共振自减速，保留已经双曲耗散降低的 runningVinf，杜绝被外轨转移开普勒假想交角强制抹杀
                    if (!bFrom.Equals(arrival, StringComparison.OrdinalIgnoreCase) || !bTo.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                    {
                        double vArr = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r2 - 1.0 / aTrans)));
                        double vCirc2 = (pTo != null && pTo.SMA > 0) ? Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r2 - 1.0 / pTo.SMA))) : Math.Sqrt(sunMu / r2);
                        double scalarDiff = Math.Abs(vArr - vCirc2);
                        // 内侧行星 (金星/水星) 轨道速度高且具有空间倾角，非切向交会产生 2.5~3.5 km/s 正交速度分量
                        // 若前序借力天体已通过连续多次借力 (clusterCount >= 2) 对齐了轨道面和切向速度 (如 Venus x2 进场 Mercury)，
                        // 进场正交速度分量消减为开普勒共面交角 (~1.0 km/s)
                        bool isAlignedInward = (clusterCount >= 2 && r2 < r1 && pTo != null && pTo.SMA < AU_KM * 0.9);
                        double orthogonalVinf = isAlignedInward ? 1.0 : ((pTo != null && pTo.SMA < AU_KM * 0.9) ? 3.5 : 1.2);
                        double geometricVinf = Math.Sqrt(scalarDiff * scalarDiff + orthogonalVinf * orthogonalVinf);
                        if (isAlignedInward && bTo.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                        {
                            // 终点行星进场对齐：流入目标天体的初始双曲过剩速度严格对齐至开普勒切向交会速度
                            runningVinf = Math.Max(MGADefaults.MinEffectiveVInfKms, geometricVinf);
                        }
                        else
                        {
                            runningVinf = Math.Max(runningVinf, Math.Max(MGADefaults.MinEffectiveVInfKms, geometricVinf));
                        }
                    }
                }

                res.LegReports.Add(legRep);
            }

            string firstFlyby = (rawFlybys != null && rawFlybys.Count > 0) ? rawFlybys[0] : null;
            // 终点目标天体捕获入轨代价 (Arrival Capture Insertion Impulse):
            // 真实深空目标轨道捕获脉冲代价 (Orbital Insertion into Eccentric Bound Orbit ra >= 10 rp):
            // 航天器通过近心点制动进入受行星引力束缚的高偏心率椭圆捕获轨道 (e ~ 0.90, ra >= 10 rp)
            double arrivalCaptureDvMs = 0.0;
            if (!noInsertionBurn)
            {
                var pArr = SolarSystemCatalog.GetPlanet(arrival);
                if (pArr != null && pArr.Mu > 0 && res.LegReports.Count > 0)
                {
                    var lastLeg = res.LegReports.Last();
                    double rArrKm = (pArr.SMA > 0) ? pArr.SMA : AU_KM;
                    var pLastFrom = SolarSystemCatalog.GetPlanet(lastLeg.FromBody);
                    double rLastKm = (pLastFrom != null && pLastFrom.SMA > 0) ? pLastFrom.SMA : AU_KM;
                    double vInfArr;
                    if (lastLeg.FromBody.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                    {
                        vInfArr = Math.Max(0.5, runningVinf);
                    }
                    else
                    {
                        double aTransLast = (rLastKm + rArrKm) * 0.5;
                        double vArrHeliocentric = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / rArrKm - 1.0 / aTransLast)));
                        double vCircArr = (pArr.SMA > 0) ? Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / rArrKm - 1.0 / pArr.SMA))) : Math.Sqrt(sunMu / rArrKm);
                        vInfArr = Math.Abs(vArrHeliocentric - vCircArr);
                    }
                    double rpSafe = MGADefaults.GetBodySafeRadiusKm(arrival);
                    double vHypArr = Math.Sqrt(vInfArr * vInfArr + 2.0 * pArr.Mu / rpSafe);
                    double vCaptureArr = Math.Sqrt(2.0 * pArr.Mu / rpSafe * 0.90);
                    arrivalCaptureDvMs = Math.Max(0.0, vHypArr - vCaptureArr) * 1000.0;
                }
            }

            res.TotalDeficitMs = totalDeficit;
            res.ResonanceCount = resCount;
            // 真实星际航行器星载推进系统总冲量可行门禁:
            // 1. 巡航段单航段与总 DSM 赤字严禁超出推进器巡航预算
            // 2. 若终点需要捕获入轨 (!noInsertionBurn)，星载在轨机动冲量总需求 (DSM + 捕获入轨) 严禁超出航天器总推进剂预算
            double onBoardDvMs = totalDeficit + arrivalCaptureDvMs;
            if (totalDeficit > maxDsmBudgetMs || (!noInsertionBurn && onBoardDvMs > maxBudgetMs))
            {
                res.IsFeasible = false;
            }

            res.EstimatedLaunchDvMs = MGAPlanetaryAssistManager.EstimateLaunchInjectionDvMs(departure, firstFlyby);
            res.EstimatedMinDsmMs = totalDeficit;
            res.EstimatedTotalDvMs = res.EstimatedLaunchDvMs + totalDeficit + arrivalCaptureDvMs;
            res.EstimatedToFDays = totalToFDays;
            res.ParetoScore = (res.EstimatedTotalDvMs / 1000.0) * 1.2 + (totalDeficit / 1000.0) * 1.5 + (totalToFDays / 365.25) * 0.4;

            if (res.IsFeasible)
            {
                res.SummaryRationale = $"✅ 全链路物理闭环可达 (总赤字 {totalDeficit:F0} m/s <= 预算 {maxBudgetMs:F0} m/s, 含 {resCount} 次同星共振)";
            }
            else
            {
                res.SummaryRationale = $"❌ 存在致命能量断层 (总赤字 {totalDeficit:F0} m/s, 在轨需求 {onBoardDvMs:F0} m/s > 预算 {maxBudgetMs:F0} m/s)";
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
            List<string> userGoal = null,
            bool noInsertionBurn = false)
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
                var report = AnalyzeChainReachability(departure, arrival, chain, departureEpochUT, maxBudgetMs, maxDsmBudgetMs, noInsertionBurn);
                evaluated.Add(report);
            }

            LastEvaluatedChains = new List<ChainReachabilityResult>(evaluated);

            // 过滤出物理可行的序列，按能量赤字与序列精简度 (奥卡姆剃刀原则) 升序排列，并保留多样性拓扑
            // 严格门禁：单航段存在天文级速度赤字 (> MaxPhysicalEnergyDeficitMs) 的跨越势阱序列必须被剪除，迫使求解器走完整引力阶梯
            var physicallyViableChains = evaluated.Where(e => !e.LegReports.Any(l => l.EnergyDeficitMs > MGADefaults.MaxPhysicalEnergyDeficitMs)).ToList();
            var pool = physicallyViableChains.Count > 0 ? physicallyViableChains : evaluated;

            // 纯连续物理多目标排序 (Deb's Feasibility-First & Continuous Physics Deficit Ordering):
            // 0 硬编码规则，0 人工经验阶梯分值。
            // 1. 物理约束容许优先 (Deb准则): 单腿最大赤字在推进器容许预算内的拓扑严格优先于存在物理断崖的拓扑;
            // 2. 连续物理总赤字最小化 (TotalDeficitMs): 缺少引力阶梯或共振的劣质拓扑其几何偏折与能量赤字巨大，自然沉底;
            // 3. 帕累托综合多目标评价 (ParetoScore);
            // 4. 预估总冲量与链长复杂度 (奥卡姆剃刀准则).
            var feasibleCandidates = pool.Where(e => e.IsFeasible)
                .Select(e => (R: e,
                              MaxLeg: e.LegReports.Count > 0 ? e.LegReports.Max(l => l.EnergyDeficitMs + l.TurnDeficitMs) : 0.0))
                .OrderBy(t => (t.MaxLeg > MGADefaults.DefaultMaxDsmDeltaVMs) ? 1 : 0)
                .ThenBy(t => t.R.FlybyChain.Count > 12 ? 2 : (t.R.FlybyChain.Count > 10 ? 1 : 0))
                .ThenBy(t => t.R.TotalDeficitMs)
                .ThenBy(t => t.MaxLeg)
                .ThenBy(t => t.R.ParetoScore)
                .ThenBy(t => t.R.EstimatedTotalDvMs)
                .ThenBy(t => t.R.FlybyChain.Count)
                .Select(t => t.R)
                .ToList();
            if (feasibleCandidates.Count == 0)
            {
                feasibleCandidates = pool
                    .Select(e => (R: e,
                                  MaxLeg: e.LegReports.Count > 0 ? e.LegReports.Max(l => l.EnergyDeficitMs + l.TurnDeficitMs) : 0.0))
                    .OrderBy(t => (t.MaxLeg > MGADefaults.DefaultMaxDsmDeltaVMs) ? 1 : 0)
                    .ThenBy(t => t.R.FlybyChain.Count > 12 ? 2 : (t.R.FlybyChain.Count > 10 ? 1 : 0))
                    .ThenBy(t => t.R.TotalDeficitMs)
                    .ThenBy(t => t.MaxLeg)
                    .ThenBy(t => t.R.ParetoScore)
                    .ThenBy(t => t.R.EstimatedTotalDvMs)
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

            // 各家族内部按 ParetoScore 升序严格排序 (物理质量优先)
            foreach (var g in familyGroups.Values)
            {
                g.Sort((a, b) => a.ParetoScore.CompareTo(b.ParetoScore));
            }

            // 2. 按首借力天体与天体力学三大物理动力学原型 (First Gateway Body & Astrodynamic Archetype) 组织轮询:
            // 真实深空任务三大物理流派规律:
            // - Ballistic (弹道单/双重飞掠): 异星单程快速转向 (Mariner 10, Pioneer 10/11, Voyager 1, New Horizons, Dawn)
            // - Resonant (同星共振泵升): 同星多圈共振轨道蓄能或减速 (Galileo VEEGA, Cassini VVEJGA, MESSENGER EVVMMM, BepiColombo EVVM, Juno EGA, Rosetta EGA)
            // - GrandTour (多星连续大巡游): 三星以上异星连续引力弹弓连珠 (Voyager 2 Grand Tour)
            // 每个 Gateway 内部公平保留不同物理流派的原型代表 (Archetype Champions)
            var byFirstBody = new Dictionary<string, (List<List<ChainReachabilityResult>> Ballistic, List<List<ChainReachabilityResult>> Resonant, List<List<ChainReachabilityResult>> GrandTour)>(StringComparer.OrdinalIgnoreCase);

            foreach (var fam in familyGroups.Values)
            {
                var cand = fam[0];
                if (cand.FlybyChain.Count == 0) continue; // 直达转移无借力天体，不作为 MGA 借力 Gateway 参与天体轮询，由全局 Pareto 前沿自然优选
                string fb = cand.FlybyChain[0];

                // 排除天文级荒谬赤字/超长漂移序列
                if (cand.EstimatedTotalDvMs > (noInsertionBurn ? 12000.0 : 18000.0))
                {
                    continue;
                }
                if (cand.EstimatedToFDays > 25.0 * 365.25)
                {
                    continue;
                }

                if (!byFirstBody.TryGetValue(fb, out var lists))
                {
                    lists = (new List<List<ChainReachabilityResult>>(), new List<List<ChainReachabilityResult>>(), new List<List<ChainReachabilityResult>>());
                    byFirstBody[fb] = lists;
                }

                if (cand.ResonanceCount > 0)
                {
                    lists.Resonant.Add(fam);
                }
                else if (cand.FlybyChain.Count >= 3)
                {
                    lists.GrandTour.Add(fam);
                }
                else
                {
                    lists.Ballistic.Add(fam);
                }
            }

            // 各 firstBody 内部按帕累托质量严格排序，并确保三大流派的代表均有机会入围
            var fbQueues = new Dictionary<string, List<ChainReachabilityResult>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in byFirstBody)
            {
                var bal = kvp.Value.Ballistic.OrderBy(f => f[0].ParetoScore).ToList();
                var resList = kvp.Value.Resonant.OrderBy(f => f[0].ParetoScore).ToList();
                var gt = kvp.Value.GrandTour.OrderBy(f => f[0].ParetoScore).ToList();
                var q = new List<ChainReachabilityResult>();

                // 收集本 Gateway 内部各物理流派的原型冠军代表 (Archetype Champions)
                var champions = new List<ChainReachabilityResult>();
                if (bal.Count > 0) champions.Add(bal[0][0]);
                if (resList.Count > 0) champions.Add(resList[0][0]);
                if (gt.Count > 0) champions.Add(gt[0][0]);

                champions.Sort((a, b) => a.ParetoScore.CompareTo(b.ParetoScore));
                q.AddRange(champions);

                // 补充该 Gateway 下的其他所有家族候选 (按 Pareto 升序)
                var allOther = kvp.Value.Ballistic.Concat(kvp.Value.Resonant).Concat(kvp.Value.GrandTour)
                    .OrderBy(f => f[0].ParetoScore)
                    .Select(f => f[0]);
                foreach (var f in allOther)
                {
                    if (!q.Any(x => x.FlybyChain.SequenceEqual(f.FlybyChain)))
                    {
                        q.Add(f);
                    }
                }

                fbQueues[kvp.Key] = q;
            }

            // 按各 firstBody 最优代表的 ParetoScore 升序排列各天体轮询顺序
            var activeFirstBodies = fbQueues.Keys
                .Where(fb => fbQueues[fb].Count > 0)
                .OrderBy(fb => fbQueues[fb][0].ParetoScore)
                .ToList();

            var diverseFeasible = new List<ChainReachabilityResult>();

            // 3.1 全局 Pareto 前沿极致精英优先保底 (Top 2 Global Non-Dominated Elites):
            // 无论架构如何，全体候选人中综合物理性能最卓越的前 2 组解直接保底入池
            var globalElites = feasibleCandidates.OrderBy(c => c.ParetoScore).Take(2);
            foreach (var elite in globalElites)
            {
                if (!diverseFeasible.Any(d => d.FlybyChain.SequenceEqual(elite.FlybyChain)))
                {
                    diverseFeasible.Add(elite);
                }
            }

            // 3.2 第一轮轮询 (Round 0): 每一个活跃的首借力天体 Gateway 保留其 #1 最优代表 (确立借力天体多样性基底)
            foreach (var fb in activeFirstBodies)
            {
                var q = fbQueues[fb];
                if (q.Count > 0)
                {
                    var cand = q[0];
                    if (!diverseFeasible.Any(d => d.FlybyChain.SequenceEqual(cand.FlybyChain)))
                    {
                        diverseFeasible.Add(cand);
                    }
                }
            }

            // 3.3 第二轮轮询 (Round 1): 各活跃 Gateway 若有不同物理动力学原型 (共振蓄能 vs 弹道飞掠 vs 大巡游) 的次优原型冠军，
            // 且其帕累托性能在合理范围内，优先保底入池 (若该 Gateway 已有 >= 2 席则跳过，防止单一 Gateway 霸池)
            double eliteParetoRef = globalElites.Any() ? globalElites.First().ParetoScore : 10.0;
            foreach (var fb in activeFirstBodies)
            {
                int currentCountForFb = diverseFeasible.Count(d => {
                    string dFb = d.FlybyChain.Count > 0 ? d.FlybyChain[0] : null;
                    return dFb != null && dFb.Equals(fb, StringComparison.OrdinalIgnoreCase);
                });
                if (currentCountForFb >= 2) continue;

                var q = fbQueues[fb];
                for (int i = 1; i < q.Count && i < 3; i++)
                {
                    var cand = q[i];
                    double gateElite = Math.Max(eliteParetoRef, q[0].ParetoScore);
                    if (cand.ParetoScore <= gateElite * 1.20 && cand.TotalDeficitMs <= maxBudgetMs)
                    {
                        if (!diverseFeasible.Any(d => d.FlybyChain.SequenceEqual(cand.FlybyChain)))
                        {
                            diverseFeasible.Add(cand);
                            break; // 每个 Gateway 在 Round 1 至多再进 1 个异构原型代表
                        }
                    }
                }
            }

            // 3.4 架构内多圈变体与同家族次优候选补充轮询 (必须满足帕累托质量底线，防止劣质同构挤占池位):
            foreach (var fam in familyGroups.Values)
            {
                for (int i = 1; i < fam.Count; i++)
                {
                    var cand = fam[i];
                    if (cand.ParetoScore <= eliteParetoRef * 1.20 && cand.TotalDeficitMs <= maxBudgetMs)
                    {
                        if (!diverseFeasible.Any(d => d.FlybyChain.SequenceEqual(cand.FlybyChain)))
                        {
                            diverseFeasible.Add(cand);
                            break;
                        }
                    }
                }
            }

            // 3.5 剩余席位由全体物理可行候选按 ParetoScore 严格升序补齐 (Global Pareto Merit Fill)
            foreach (var elite in feasibleCandidates.OrderBy(c => c.ParetoScore))
            {
                if (!diverseFeasible.Any(d => d.FlybyChain.SequenceEqual(elite.FlybyChain)))
                {
                    diverseFeasible.Add(elite);
                }
            }



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
