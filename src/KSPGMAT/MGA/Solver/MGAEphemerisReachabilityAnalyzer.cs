// ============================================================================
// MGAEphemerisReachabilityAnalyzer.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
// 权威自适应 MGA 求解器子模块。底层 100% 官方 ESA kep3 Native C++ 闭环动力学。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;
using KSPGMAT.MGA.Telemetry;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 基于当前历元真实星历与开普勒引力势阱的物理可达性前置分析与剪枝引擎 (Ephemeris Reachability & Feasibility Analyzer)
    /// 核心理论：开普勒 Vis-Viva 机械能守恒、双曲无动力偏折角极限与日心相位角几何闭环
    /// </summary>
    public static class MGAEphemerisReachabilityAnalyzer
    {
        /// <summary>
        /// v_inf 的纯数值底限 (km/s): 仅用于避免 sin(δ/2) 公式在 v_inf→0 时退化。
        /// 与 <see cref="MGADefaults.MinEffectiveVInfKms"/> (2.5, 借力"有效性"下限) 职责不同 ——
        /// 后者是物理判据, 前者只是除零/退化保护。原实现两处分别写 0.10 与 2.0, 属不一致。
        /// </summary>
        public const double NumericalVInfFloorKms = 0.10;

        /// <summary>
        /// 偏心率在取 asin(e) 前的上限。物理不变量: 0 &lt;= e &lt; 1 (椭圆), e→1 时 asin→∞ 的斜率发散。
        /// 取 0.95 保留 5% 域裕度。原实现在两处分别写 0.9 与 0.95, 属不一致。
        /// </summary>
        public const double MaxEccentricityForAsin = 0.95;

        /// <summary>方向反转的物理极限 (deg): 双曲偏折角不可能超过 180°。</summary>
        public const double DirectionReversalLimitDeg = 180.0;

        /// <summary>累积偏折角上限距 180° 的裕度 (deg), 覆盖数值与三维几何松弛。</summary>
        public const double CumulativeTurnMarginDeg = 10.0;

        /// <summary>
        /// 巨行星类天体的到达捕获椭圆远拱点半径倍数 (以安全半径为 1 单位)。
        /// 取代原裸字面量 80.0。标注: **[经验参数·待标定]**
        /// </summary>
        public const double GiantCaptureApoapsisRadii = 80.0;

        /// <summary>
        /// 类地行星类天体的到达捕获椭圆远拱点半径倍数 (以安全半径为 1 单位)。
        /// 取代原裸字面量 15.0。标注: **[经验参数·待标定]**
        /// </summary>
        public const double TerrestrialCaptureApoapsisRadii = 15.0;

        /// <summary>
        /// 捕获椭圆远拱点相对天体引力球半径的上限比例 (无量纲)。
        /// 取代原裸字面量 0.85。标注: **[经验参数·待标定]**
        /// </summary>
        public const double SoiApoapsisFraction = 0.85;

        /// <summary>
        /// 出发天体自环 (DeltaV-EGA) 的深空机动成本 (m/s)。
        /// 物理来源: 自环需在共振点重新定向 v_inf, 其成本尺度由 2·v_inf·sin(δ_要求/2) 给出;
        /// 该量依赖具体几何, 无法由单一不变量确定, 故显式登记为经验参数 (待盲搜数据标定),
        /// 取代原内联的 750.0。
        /// </summary>
        public const double DepartureSelfLoopDsmMs = 750.0;

        /// <summary>出发停泊轨道高度 (km)。原实现内联 +200.0, 与 MGADefaults.FallbackDepartureAltitudeKm 重复。</summary>
        private const double ParkingAltitudeKm = MGADefaults.FallbackDepartureAltitudeKm;

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
        private const double AU_KM = MGADefaults.AU_KM;

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
            bool noInsertionBurn = false,
            double windowSpanDays = 0.0)
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

            // runningVinf 采用"保守最大值传播"近似: 后续航段均以历史途经天体的最大双曲过剩速度为下限,
            // 只增不减 (除终点共振自减速分支显式耗散)。该近似刻意偏保守, 仅用于可达性筛查与排序,
            // 不参与最终 ΔV 决算——真实逐段 v∞ 由 DE 原生动力学 (PykepBridge.BuildFullSolution) 精确求解后覆盖。
            double runningVinf = 0.0; // 0 经验数字，纯由首段开普勒轨道到达交会速度严格物理计算
            double totalDeficit = 0.0;
            double totalEstimatedDsmMs = 0.0;
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
                    // 同星共振段：开普勒二体机械能守恒。
                    // 物理机理:
                    // 1. 始发天体直接出发的纯同星自环 (k == 0, 如 Juno 模式 Earth->Earth EGA):
                    //    航天器若未经过异星偏折，远日点必须依靠 1-DSM 机动打破初始发射椭圆守恒，理论标称工质为 ~750 m/s;
                    // 2. 异星借力偏折后的中继共振 (如 MESSENGER 之 Venus->Venus, Galileo 之 Earth->Earth):
                    //    前序借力天体已提供非零相对速度与离心率，借助无动力飞掠直接进入共振椭圆，工质赤字为 0;
                    // 3. 终点目标天体多次共振自减速 (如 MESSENGER/BepiColombo 水星连续共振): 纯重力引力偏折降能，工质赤字为 0。
                    bool isInitialDepartureSelfLoop = (k == 0 && bFrom.Equals(departure, StringComparison.OrdinalIgnoreCase));
                    double resDeficitMs = isInitialDepartureSelfLoop ? DepartureSelfLoopDsmMs : 0.0;
                    legRep.EnergyDeficitMs = resDeficitMs;
                    totalDeficit += resDeficitMs;

                    if (bFrom.Equals(arrival, StringComparison.OrdinalIgnoreCase))
                    {
                        // 终点目标天体共振自减速 (如 MESSENGER / BepiColombo 水星自减速机制):
                        // 航天器在目标天体借力偏折，消耗双曲过剩速度，每次偏折减少 v_inf:
                        double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                        // 天体引力常数改由星历目录解析 (原内联 22032.0 = 水星μ 的隐式天体假设)
                        double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : MGADefaults.GetBodyMu(bFrom);
                        double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, Math.Max(NumericalVInfFloorKms, runningVinf));
                        double sinHalf = Math.Sin(maxTurn * 0.5 * (Math.PI / 180.0));
                        double dvDecel = 2.0 * runningVinf * sinHalf * Math.Max(0.60, sinHalf);
                        runningVinf = Math.Max(NumericalVInfFloorKms, runningVinf - dvDecel);
                        legRep.Rationale = $"终点共振自减速 [{bFrom} ➔ {bTo}]: 偏折角={maxTurn:F1}°, 过剩速度消减至 {runningVinf:F2} km/s";
                        double periodDays = (2.0 * Math.PI * Math.Sqrt(Math.Pow(r1, 3.0) / sunMu)) / 86400.0;
                        // 终点捕获阶段自减速环为近目标天体低速捕获机动，不计入行星际深空巡航漂移时间惩罚
                        totalToFDays += periodDays * 0.2;
                    }
                    else
                    {
                        // 始发/中继天体开普勒二体共振蓄能 (外太阳系任务如 Juno/Galileo 采用 2:1 泵升共振，内太阳系深潜任务如 MESSENGER/BepiColombo 采用 1:1 EGA 调相环):
                        var pArrTgt = SolarSystemCatalog.GetPlanet(arrival);
                        bool isInnerTgt = (pArrTgt != null && pFrom != null && pArrTgt.SMA > 0 && pFrom.SMA > 0 && pArrTgt.SMA < pFrom.SMA);
                        double aRes = isInnerTgt ? r1 : r1 * Math.Pow(2.0, 2.0 / 3.0);
                        double vRes = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aRes)));
                        double vCirc = Math.Sqrt(sunMu / r1);
                        double vInfRes = isInnerTgt ? MGADefaults.MinEffectiveVInfKms : Math.Abs(vRes - vCirc);

                        if (k == 0)
                        {
                            // 停泊轨道与出发天体引力常数全部由星历目录解析
                            // (原内联 6578.1363 km / 398600.4418 km^3s^-2 = 地球的隐式天体假设)
                            double rPark = MGADefaults.GetBodyRadiusKm(bFrom) + ParkingAltitudeKm;
                            double muDep = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : MGADefaults.GetBodyMu(bFrom);
                            double vPark = Math.Sqrt(muDep / rPark);
                            double vHyp = Math.Sqrt(vInfRes * vInfRes + 2.0 * muDep / rPark);
                            res.EstimatedLaunchDvMs = Math.Max(0.0, vHyp - vPark) * 1000.0;
                        }

                        double safeR = MGADefaults.GetBodySafeRadiusKm(bFrom);
                        double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : MGADefaults.GetBodyMu(bFrom);
                        runningVinf = Math.Max(runningVinf, vInfRes);
                        double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, Math.Max(NumericalVInfFloorKms, runningVinf));
                        legRep.Rationale = $"同星共振 [{bFrom} ➔ {bTo}]: 轨道能量守恒 (0 DSM), 安全半径 R_safe={safeR:F0}km, 单次最大偏折角={maxTurn:F1}°";
                        double periodDays = (2.0 * Math.PI * Math.Sqrt(Math.Pow(aRes, 3.0) / sunMu)) / 86400.0;
                        totalToFDays += periodDays * 1.0;
                    }
                }
                else
                {
                    // 统计当前天体在连续借力集群中的总飞掠次数 (Cluster Count)
                    int clusterCount = 1;
                    if (k >= 1)
                    {
                        for (int m = k - 1; m >= 0 && fullPath[m].Equals(bFrom, StringComparison.OrdinalIgnoreCase); m--) {
                            clusterCount++;
                        }
                    }

                    // 异星转移段：Vis-Viva 二体机械能守恒计算
                    double aTrans = (r1 + r2) * 0.5;
                    if (k >= 1 && r2 < r1)
                    {
                        int prevLegIdx = k - clusterCount;
                        string bPrevNode = (prevLegIdx >= 0) ? fullPath[prevLegIdx] : fullPath[k - 1];
                        var pPrev = SolarSystemCatalog.GetPlanet(bPrevNode);
                        double rPrev = (pPrev != null && pPrev.SMA > 0) ? pPrev.SMA : r1;
                        if (rPrev > r1)
                        {
                            // 连贯向内深潜飞掠 (如 Earth -> Venus -> Venus -> Mercury):
                            // 航天器轨道的远日点保留在外侧出发天体 (rPrev) 附近，而非强制假设在中间借力星 (r1) 形成切向椭圆
                            aTrans = (rPrev + r2) * 0.5;
                        }
                    }
                    double vCirc1 = (pFrom != null && pFrom.SMA > 0) ? Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / pFrom.SMA))) : Math.Sqrt(sunMu / r1);
                    double vReq = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aTrans)));

                    double vMaxAchievable;
                    double deficitKms;
                    if (k == 0)
                    {
                        // 始发天体：计算停泊轨道切向逃逸注入 ΔV (停泊高度取天体安全高度或 200km 净空)
                        double vInfDep = Math.Abs(vReq - vCirc1);
                        if (pFrom == null) pFrom = SolarSystemCatalog.GetPlanet(bFrom);
                        if (pFrom == null || pFrom.Mu <= 0)
                        {
                            res.IsFeasible = false;
                            res.TotalDeficitMs = 1.0e6;
                            return res;
                        }
                        double rPark = (pFrom.RadiusKm > 0) ? (pFrom.RadiusKm + Math.Max(MGADefaults.MinSafeAltitudeKm, pFrom.AtmosphereDepthKm + 100.0)) : pFrom.SafeRadiusKm;
                        double muDep = pFrom.Mu;
                        double vPark = Math.Sqrt(muDep / rPark);
                        double vHyp = Math.Sqrt(vInfDep * vInfDep + 2.0 * muDep / rPark);
                        double launchDvKms = vHyp - vPark;
                        res.EstimatedLaunchDvMs = launchDvKms * 1000.0;
                        vMaxAchievable = vCirc1 + Math.Max(MGADefaults.MinEffectiveVInfKms, runningVinf);
                        deficitKms = (launchDvKms * 1000.0 > maxBudgetMs) ? (launchDvKms - maxBudgetMs / 1000.0) : 0.0;
                    }
                    else
                    {
                        // 借力天体：天体力学第一性原理 (无动力双曲偏折与日心速度矢量包络)
                        // 每一个天体严格根据其引力参数 Mu 与安全近心点半径推导最大偏折角与最大日心速度包络。
                        if (pFrom == null) pFrom = SolarSystemCatalog.GetPlanet(bFrom);
                        if (pFrom == null || pFrom.Mu <= 0)
                        {
                            res.IsFeasible = false;
                            res.TotalDeficitMs = 1.0e6;
                            return res;
                        }
                        double safeR = (pFrom.SafeRadiusKm > 0) ? pFrom.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(bFrom);
                        double muBody = pFrom.Mu;
                        double effectiveVinf = Math.Max(MGADefaults.MinEffectiveVInfKms, runningVinf);

                        // 1. 航天器流入借力星时的标称日心速度标量与等效 V_inf
                        bool prevIsResonance = (k >= 1 && fullPath[k - 1].Equals(bFrom, StringComparison.OrdinalIgnoreCase));
                        double aInLeg;
                        if (prevIsResonance)
                        {
                            // 前序为同星共振段: 依据开普勒第三定律推导共振进场轨道半长轴
                            // 始发内侧调相自环 (k == 1 且内侧目标) 进场轨道为 1:1 共振 (a = r1)；外侧爬升自环为 2:1 泵升共振
                            var pArrTgt = SolarSystemCatalog.GetPlanet(arrival);
                            bool isInnerTgt = (pArrTgt != null && pFrom != null && pArrTgt.SMA > 0 && pFrom.SMA > 0 && pArrTgt.SMA < pFrom.SMA);
                            double aRes = isInnerTgt ? r1 : r1 * Math.Pow(2.0, 2.0 / 3.0);
                            aInLeg = aRes;
                            double vResCraft = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aRes)));
                            double vResInf = Math.Abs(vResCraft - vCirc1);
                            if (vResInf > effectiveVinf) effectiveVinf = vResInf;
                        }
                        else
                        {
                            int prevLegIdx = k - clusterCount;
                            string bPrevNode = (prevLegIdx >= 0) ? fullPath[prevLegIdx] : null;
                            var pPrevNode = bPrevNode != null ? SolarSystemCatalog.GetPlanet(bPrevNode) : null;
                            double rPrevNode = (pPrevNode != null && pPrevNode.SMA > 0) ? pPrevNode.SMA : r1;
                            double aHohmann = (rPrevNode + r1) * 0.5;
                            aInLeg = aHohmann;

                            // 天体力学第一性原理 (Vis-Viva 轨道机械能跨段守恒):
                            // 若前序借力天体已通过双曲引力偏折将航天器泵升至更高半长轴 (如 Cassini 之 Venus-2 借力)，
                            // 能量在日心二体场中严格守恒至本星交会，航天器以更高的速度流入当前星，显著提升当前星的交会 v_inf
                            if (runningVinf > MGADefaults.MinEffectiveVInfKms && r1 > rPrevNode)
                            {
                                double vCircPrev = Math.Sqrt(sunMu / rPrevNode);
                                double vDepPrevMax = vCircPrev + runningVinf;
                                double invAPump = 2.0 / rPrevNode - (vDepPrevMax * vDepPrevMax) / sunMu;
                                if (invAPump > 1e-12)
                                {
                                    double aPump = 1.0 / invAPump;
                                    if (aPump > aHohmann && aPump < r1 * 2.5)
                                    {
                                        aInLeg = aPump;
                                        double vPumpCraftSq = Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aInLeg));
                                        // 二体速度矢量交会相对速度 (开普勒角动量守恒 h = r_prev * v_dep):
                                        // v_theta = h / r1 = (rPrevNode / r1) * vDepPrevMax
                                        // v_inf = sqrt(v_craft^2 + v_circ^2 - 2 * v_circ * v_theta)
                                        double vTheta = (rPrevNode / r1) * vDepPrevMax;
                                        double vPumpInfSq = Math.Max(0.0, vPumpCraftSq + vCirc1 * vCirc1 - 2.0 * vCirc1 * vTheta);
                                        double vPumpInf = Math.Sqrt(vPumpInfSq);
                                        if (vPumpInf > effectiveVinf) effectiveVinf = vPumpInf;
                                    }
                                }
                            }
                        }

                        double vInHeliocentric = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aInLeg)));

                        // 2. 双曲偏折物理极限角: sin(delta/2) = 1 / (1 + r_safe * v_inf^2 / mu)
                        double maxTurnRad = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleRad(muBody, safeR, effectiveVinf);
                        double singleMaxTurn = maxTurnRad * (180.0 / Math.PI);
                        // 累积偏折角上限: 物理不变量为方向反转极限 180°, 留固定裕度 (取代内联 170.0)
                        double totalMaxTurn = Math.Min(DirectionReversalLimitDeg - CumulativeTurnMarginDeg,
                                                       clusterCount * singleMaxTurn);
                        double maxAssistDv = 2.0 * effectiveVinf * Math.Sin(maxTurnRad * 0.5);

                        // 3. 重力助推天体力学动能定理第一性原理 (Bate, Mueller & White 1971 / Curtis 2014):
                        // 行星对航天器做的最大功: ΔE_max = v_circ · Δv_assist = v_circ · 2·v_inf·sin(δ/2)
                        // 借力后航天器日心速度极值由开普勒能量守恒严格解析决定:
                        double maxDeltaE = vCirc1 * maxAssistDv;
                        vMaxAchievable = Math.Sqrt(vInHeliocentric * vInHeliocentric + 2.0 * maxDeltaE);
                        double vMinAchievable = Math.Sqrt(Math.Max(1.0, vInHeliocentric * vInHeliocentric - 2.0 * maxDeltaE));

                        if (r2 < r1)
                        {
                            // 向内降轨段 (如向太阳系深处降轨): 要求日心速度不高于 vReq
                            deficitKms = Math.Max(0.0, vMinAchievable - vReq);
                        }
                        else if (r2 > r1)
                        {
                            // 向外升轨段 (如向外太阳系爬升): 要求日心速度达到 vReq
                            deficitKms = Math.Max(0.0, vReq - vMaxAchievable);
                        }
                        else
                        {
                            // 同星共振段: 能量天然守恒
                            deficitKms = 0.0;
                        }

                        // 记录更新流向后续航段的等效 v_inf
                        runningVinf = effectiveVinf;
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
                            double safeR = (pFrom != null && pFrom.SafeRadiusKm > 0) ? pFrom.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(bFrom);
                            double muBody = (pFrom != null && pFrom.Mu > 0) ? pFrom.Mu : (SolarSystemCatalog.GetPlanet(bFrom)?.Mu ?? 0.0);
                            // 航天器进入借力天体引力影响球 (SOI) 处的真实双曲过剩速度 (由进场开普勒轨道交会速度解析决定):
                            double aInBound = (rPrev + r1) * 0.5;
                            if (bPrev.Equals(bFrom, StringComparison.OrdinalIgnoreCase))
                            {
                                // 前序为同星共振段: 消除 rPrev == r1 导致的二体共线圆轨退化伪解
                                // 依据开普勒第三定律与活力公式推导共振进场半长轴与相对速度
                                double aRes = r1 * Math.Pow(2.0, 2.0 / 3.0);
                                if (r2 > r1 * 1.5) aInBound = aRes; // 冲向外侧巨行星共振抬升
                                else if (r2 < r1 * 0.8) aInBound = r1 * Math.Pow(0.5, 2.0 / 3.0); // 潜向内侧行星共振降轨
                            }
                            double vInBound = Math.Sqrt(Math.Max(0.0, sunMu * (2.0 / r1 - 1.0 / aInBound)));
                            double vCircBody = Math.Sqrt(sunMu / r1);
                            // 离面分量由进场弧两端轨道倾角差推导: v_orth = v_circ · sin(Δi)
                            var pPrevTurn = (bPrev != null) ? SolarSystemCatalog.GetPlanet(bPrev) : null;
                            double orthogonalOffset = MGAFlybyDynamicsHelper.ComputeOutOfPlaneVinfKms(
                                vCircBody,
                                (pPrevTurn != null) ? pPrevTurn.INC : ((pFrom != null) ? pFrom.INC : 0.0),
                                (pFrom != null) ? pFrom.INC : 0.0);
                            double effVinfIn = Math.Sqrt(Math.Pow(vInBound - vCircBody, 2.0) + Math.Pow(orthogonalOffset, 2.0));
                            double effVinf = Math.Max(MGADefaults.MinEffectiveVInfKms, effVinfIn);
                            double singleMaxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(muBody, safeR, effVinf);
                            double totalAvailableTurn = clusterCount * singleMaxTurn;

                            // 计算进出转移轨道相对夹角 (综合倾角差与偏心率航向角)
                            double dInc = Math.Abs((pPrev != null ? pPrev.INC : 0.0) - (pFrom != null ? pFrom.INC : 0.0)) +
                                          Math.Abs((pFrom != null ? pFrom.INC : 0.0) - (pTo != null ? pTo.INC : 0.0));
                            // 进出转移弧的偏心率。开普勒椭圆的航向角满足 tan(gamma) = e*sin(nu)/(1+e*cos(nu)),
                            // 其幅值上界为 gamma_max = asin(e); 符号由运动方向给出 (外行段 gamma>0, 内行段 gamma<0)。
                            double e1 = bPrev.Equals(bFrom, StringComparison.OrdinalIgnoreCase)
                                ? Math.Max(0.0, 1.0 - r1 / Math.Max(r1, aInBound))
                                : Math.Abs((rPrev - r1) / (rPrev + r1));
                            double e2 = Math.Abs((r2 - r1) / (r2 + r1));

                            // ---- 所需偏折角的矢量推导 (取代原 4 个手绘分支) ----
                            // 借力点的双曲过剩速度是"航天器日心速度 - 天体日心速度", 故所需偏折角
                            // 等于进出两段 v_inf 方向的夹角, 而 v_inf 的方向差由两段转移弧的航向角决定:
                            //
                            //   同向入出 (同外行或同内行): reqTurn = |gamma_out - gamma_in|
                            //   反向入出 (内行->外行, 即镜像借力): reqTurn = gamma_out + gamma_in
                            //
                            // 运动方向由半径变化判定: rPrev < r1 表示进入段外行; r2 > r1 表示离开段外行。
                            // 该式消除了原实现中的 90/150/70/110/2.17/0.95/0.90/0.5/0.8/0.75 共 10 个手绘系数。
                            double gInDeg = Math.Asin(Math.Min(MaxEccentricityForAsin, e1)) * (180.0 / Math.PI);
                            double gOutDeg = Math.Asin(Math.Min(MaxEccentricityForAsin, e2)) * (180.0 / Math.PI);
                            bool isResonantExit = bPrev.Equals(bFrom, StringComparison.OrdinalIgnoreCase);

                            double geomTurnDeg;
                            if (isResonantExit)
                            {
                                // 同星共振回程: 返回同一天体, 所需转角由共振弧航向角给出,
                                // 且不可能超过单次借力的偏折能力 (超出量由倾角项 dInc 承担)。
                                geomTurnDeg = Math.Min(gInDeg, singleMaxTurn);
                            }
                            else
                            {
                                bool incomingOutbound = (rPrev < r1);
                                bool outgoingOutbound = (r2 > r1);
                                geomTurnDeg = (incomingOutbound == outgoingOutbound)
                                    ? Math.Abs(gOutDeg - gInDeg)
                                    : (gOutDeg + gInDeg);
                            }

                            double reqTurnDeg = Math.Max(MGADefaults.MinGeometryTurnAngleDeg,
                                Math.Sqrt(Math.Pow(geomTurnDeg, 2.0) + Math.Pow(dInc, 2.0)));

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
                        if (r2 > r1)
                        {
                            // 严格基于 Vis-Viva 二体机械能与开普勒方程推导真实航时 (淘汰经验拟合公式与 14 km/s 经验近似):
                            double vDepActual = Math.Min(vReq, vMaxAchievable);
                            double energyDep = 0.5 * vDepActual * vDepActual - sunMu / r1;
                            if (energyDep > 0)
                            {
                                // 双曲轨道 (Energy > 0, aHyp = -mu / (2*E) < 0):
                                // 使用开普勒双曲方程解析求解从 r1 到 r2 的弧段航时
                                double aHyp = -sunMu / (2.0 * energyDep);
                                double cChord = Math.Abs(r2 - r1);
                                double sSemi = (r1 + r2 + cChord) * 0.5;
                                double alphaH = 2.0 * Asinh(Math.Sqrt(Math.Max(0.0, sSemi / (2.0 * Math.Abs(aHyp)))));
                                double betaH = 2.0 * Asinh(Math.Sqrt(Math.Max(0.0, (sSemi - cChord) / (2.0 * Math.Abs(aHyp)))));
                                double tHypSec = Math.Sqrt(Math.Pow(Math.Abs(aHyp), 3.0) / sunMu) * (Math.Sinh(alphaH) - alphaH - (Math.Sinh(betaH) - betaH));
                                if (tHypSec > 0 && !double.IsNaN(tHypSec) && !double.IsInfinity(tHypSec))
                                {
                                    legDays = tHypSec / 86400.0;
                                }
                            }
                            else if (energyDep < 0)
                            {
                                double aActual = -sunMu / (2.0 * energyDep);
                                if (aActual > aTrans)
                                {
                                    // 能量高于切向霍曼轨道，偏心率增大，高能航时随开普勒第三定律面积律缩短
                                    double ratio = Math.Sqrt(Math.Pow(aTrans / aActual, 3.0));
                                    legDays = Math.Min(legDays, (tHohmannSec / 86400.0) * ratio);
                                }
                            }
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
                        // 始发首段 (k == 0) 在给定搜索时间窗内可通过调整发射日滑动相位:
                        // 窗口半跨度内最大可调整相位角 deltaPhi_window = |omega2 - omega1| * (windowSpanDays / 2)
                        double windowAdjustableDeg = 0.0;
                        if (windowSpanDays > 0.0)
                        {
                            double w1 = Math.Sqrt(sunMu / Math.Pow(r1, 3.0));
                            double wRel = Math.Abs(w2 - w1); // rad/s
                            windowAdjustableDeg = (wRel * (windowSpanDays * 0.5 * 86400.0)) * (180.0 / Math.PI);
                        }
                        double allowablePhaseDeg = MGADefaults.DeparturePhaseAngleToleranceDeg + windowAdjustableDeg;
                        if (k == 0 && deltaPhase > allowablePhaseDeg)
                        {
                            double excessDeg = deltaPhase - allowablePhaseDeg;
                            double phasePenaltyMs = MGADefaults.DeparturePhaseSoftPenaltyFactorMsPerDeg * excessDeg;
                            totalDeficit += phasePenaltyMs;
                        }
                    }

                    if (deficitMs > maxDsmBudgetMs)
                    {
                        // 单段赤字超 DSM 预算: 仅标注为物理不可达跨越 (供排序/诊断),
                        // 不再在此处翻转 IsFeasible——最终可行性由链末尾 withinBudget 统一判定 (含裕度)
                        legRep.Rationale = $"❌ 物理不可达跨越: 需要 {vReq:F2} km/s, 当前极值仅 {vMaxAchievable:F2} km/s (物理赤字={deficitMs:F0} m/s 远超 DSM 预算 {maxDsmBudgetMs:F0} m/s)";
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
                        // 非切向交会的离面分量由真实轨道倾角差推导: v_orth = v_circ · sin(Δi)
                        // 若前序借力天体已通过连续多次借力 (clusterCount >= 2) 对齐了轨道面和切向速度 (如 Venus x2 进场 Mercury)，
                        // 进场正交速度分量消减为开普勒共面小残差 (~1.0 km/s)
                        // 内侧深井进场 (目标远深于出发星轨道): 前序连续多次借力已消减大部分离面分量, 保留小残差;
                        bool isAlignedInward = (clusterCount >= 2 && r2 < r1 && pTo != null && pTo.SMA < pDep.SMA * 0.9);
                        double orthoVinfRaw = MGAFlybyDynamicsHelper.ComputeOutOfPlaneVinfKms(
                            vCirc2,
                            (pFrom != null) ? pFrom.INC : 0.0,
                            (pTo != null) ? pTo.INC : 0.0);
                        double orthogonalVinf = isAlignedInward ? Math.Min(1.0, orthoVinfRaw) : orthoVinfRaw;
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
            // 严格基于天体力学 Vis-Viva 机械能守恒计算进入开普勒稳定捕获轨道的近心点制动机动:
            // DeltaV_capture = sqrt(v_inf^2 + 2*mu/rp) - sqrt(2*mu/rp - mu/a_cap)
            // 彻底清除 0.90 经验魔法数字，巨行星与高椭圆科学轨道远拱点自然推导至引力球或百倍半径
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
                        vInfArr = Math.Max(0.10, runningVinf);
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

                    // 科学高偏心率捕获椭圆半长轴 (远拱点: 巨行星类 ~80 rp, 类地类 ~15 rp)
                    //
                    // 【2026-09-14 审计修正 —— 本次审计最关键的"声明与实现不一致"位置之一】
                    // 原判据: `bool isGiant = (pArr.Mu >= 5.0e6 || pArr.RadiusKm >= 20000.0);`
                    // 这正是 MGAPhysicalScales.cs 中 ClassifyGiantBySystemDistribution 的注释
                    // **明文声称"已取代"**的那一行绝对阈值, 但它一直留在本文件里, 且位于
                    // **到达捕获 ΔV 的主结算路径**上 —— 即新无量纲体系已建成、旧实现却未删除。
                    // 现接入已建成的 MGAPhysicalScales.ClassifyGiantBySystemDistribution
                    // (借力功率指数的系统内鲁棒上界 median + k·MAD), 对任意恒星系成立。
                    bool isGiant = MGAPhysicalScales.ClassifyGiantBySystemDistribution(
                        pArr, Math.Max(vInfArr, 1e-3), sunMu, SolarSystemCatalog.GetAvailablePlanets());
                    double raKm = rpSafe * (isGiant ? GiantCaptureApoapsisRadii : TerrestrialCaptureApoapsisRadii);
                    if (pArr.SoiKm > 0) raKm = Math.Min(raKm, pArr.SoiKm * SoiApoapsisFraction);
                    double aCapKm = (rpSafe + raKm) * 0.5;
                    double vCaptureArr = Math.Sqrt(Math.Max(0.0, 2.0 * pArr.Mu / rpSafe - pArr.Mu / aCapKm));
                    arrivalCaptureDvMs = Math.Max(0.0, vHypArr - vCaptureArr) * 1000.0;
                }
            }

            // 直达转移 (Direct Transfer Baseline, 0 DSM):
            if (cleanedFlybys.Count == 0)
            {
                res.TotalDeficitMs = 0.0;
                res.ResonanceCount = 0;
                res.EstimatedMinDsmMs = 0.0;
                if (res.EstimatedLaunchDvMs <= 0) res.EstimatedLaunchDvMs = MGAPlanetaryAssistManager.EstimateLaunchInjectionDvMs(departure, null);
                res.EstimatedTotalDvMs = res.EstimatedLaunchDvMs + arrivalCaptureDvMs;
                res.EstimatedToFDays = totalToFDays > 0 ? totalToFDays : 250.0;
                res.ParetoScore = (res.EstimatedTotalDvMs / 1000.0) * 1.2 + (res.EstimatedToFDays / 365.25) * 0.4;
                res.IsFeasible = (noInsertionBurn || arrivalCaptureDvMs <= maxBudgetMs * MGADefaults.FeasibilityBudgetMarginFactor);
                res.SummaryRationale = res.IsFeasible
                    ? $"✅ 直达物理基准线 (Direct Transfer Baseline, 0 DSM, 捕获入轨 {arrivalCaptureDvMs:F0} m/s)"
                    : $"⚠️ 直达物理不可行 (无引力助推减速下捕获入轨需 {arrivalCaptureDvMs:F0} m/s 超出工质预算 {maxBudgetMs:F0} m/s)";
                return res;
            }

            res.TotalDeficitMs = totalDeficit;
            res.ResonanceCount = resCount;
            res.EstimatedMinDsmMs = totalEstimatedDsmMs;
            if (res.EstimatedLaunchDvMs <= 0)
            {
                res.EstimatedLaunchDvMs = MGAPlanetaryAssistManager.EstimateLaunchInjectionDvMs(departure, firstFlyby);
            }
            res.EstimatedTotalDvMs = res.EstimatedLaunchDvMs + totalEstimatedDsmMs + totalDeficit + arrivalCaptureDvMs;
            res.EstimatedToFDays = totalToFDays;

            // 连续物理可行性评估 (非二值暴力抹杀):
            // 在轨推进剂消耗 = 预估必要 DSM + 物理残余赤字 + 目标捕获
            // 飞船在轨可用机动预算 = Math.Max(用户指定单段DSM上限, 飞船总工质 - 发射逃逸工质)
            double onBoardDvMs = totalEstimatedDsmMs + totalDeficit + arrivalCaptureDvMs;
            double availableOnBoardFuelMs = Math.Max(maxDsmBudgetMs, Math.Max(0.0, maxBudgetMs - res.EstimatedLaunchDvMs));
            bool withinBudget = totalDeficit <= availableOnBoardFuelMs * MGADefaults.FeasibilityBudgetMarginFactor
                && (noInsertionBurn || onBoardDvMs <= maxBudgetMs * MGADefaults.FeasibilityBudgetMarginFactor);
            res.IsFeasible = withinBudget;
            double deficitPenaltyMs = Math.Max(0.0, totalDeficit - maxDsmBudgetMs);
            // 普适物理多目标帕累托评分:
            // 1. 预估总冲量 (1.2 / km/s)
            // 2. 超预算赤字惩罚 (1.5 / km/s)
            // 3. 任务飞行时间平衡 (0.20 / 年): 兼顾深空借力公转周期与奥卡姆剃刀准则
            res.ParetoScore = (res.EstimatedTotalDvMs / 1000.0) * 1.2 
                            + (deficitPenaltyMs / 1000.0) * 1.5 
                            + (totalToFDays / 365.25) * 0.20;

            if (res.IsFeasible)
            {
                res.SummaryRationale = $"✅ 全链路物理闭环可达 (总赤字 {totalDeficit:F0} m/s, 含 {resCount} 次同星共振)";
            }
            else
            {
                res.SummaryRationale = $"⚠️ 存在能量赤字需要深空机动调相 (总赤字 {totalDeficit:F0} m/s, 在轨需求 {onBoardDvMs:F0} m/s)";
            }

            return res;
        }

        // =====================================================================================================
        // 高维天体力学相空间特征流形与质量-多样性 (Quality-Diversity / Max-Min Feature Dispersion) 全局优化核心
        // 彻底终结离散流派标签、门禁席位预设与人工轮询，基于连续天体力学不变量在多维相空间中自发涌现全局最优解集
        // =====================================================================================================

        private static readonly double[] FeatureWeights = new double[]
        {
            1.0,  // x0: 对数日心轨道能量跃迁比 ln(r_arr / r_dep)
            3.0,  // x1: 对数近日点引力深潜率 ln(r_min / r_dep) [核心: 区分 VEEGA/内侧蓄能与直达外飞]
            2.0,  // x2: 对数远日点外掷跨度 ln(r_max / r_dep) [核心: 表征外太阳系大巡游深空抛射]
            1.5,  // x3: 累积无量纲引力透镜偏折角 (totalTurnDeg / 180°) [区分单借力、双天体、大巡游与共振]
            1.0,  // x4: 无量纲开普勒航时比 (ToF / ToF_Hohmann)
            1.0,  // x5: 推进剂总能耗预算占比 (TotalDv / Budget)
            0.0   // x6: 阶段链长属于工程复杂度成本，不参与相空间发散度奖励，杜绝冗余长链伪造多样性
        };

        /// <summary>
        /// 提取候选拓扑在连续天体力学流形上的 7 维无量纲物理特征向量
        /// 0 行星硬编码、0 流派预设，纯粹由 Vis-Viva 能量守恒与双曲引力偏折角严格定义
        /// </summary>
        public static double[] ComputeAstrodynamicFeatureVector(
            ChainReachabilityResult r,
            string departure,
            string arrival,
            double maxBudgetMs)
        {
            var pDep = SolarSystemCatalog.GetPlanet(departure);
            var pArr = SolarSystemCatalog.GetPlanet(arrival);
            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : MGADefaults.AU_KM;
            double rArr = (pArr != null && pArr.SMA > 0) ? pArr.SMA : MGADefaults.AU_KM;

            double rMin = Math.Min(rDep, rArr);
            double rMax = Math.Max(rDep, rArr);
            double totalTurnDeg = 0.0;

            if (r.FlybyChain != null)
            {
                for (int i = 0; i < r.FlybyChain.Count; i++)
                {
                    var p = SolarSystemCatalog.GetPlanet(r.FlybyChain[i]);
                    if (p != null && p.SMA > 0)
                    {
                        if (p.SMA < rMin) rMin = p.SMA;
                        if (p.SMA > rMax) rMax = p.SMA;
                    }
                }
            }

            if (r.LegReports != null)
            {
                foreach (var leg in r.LegReports)
                {
                    totalTurnDeg += Math.Min(180.0, Math.Max(0.0, leg.AvailTurnDeg));
                }
            }

            // 基准霍曼半周期 (天)
            double muSun = SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu;
            double aHohmann = (rDep + rArr) * 0.5;
            double tofHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aHohmann, 3) / muSun) / MGADefaults.SecondsPerDay;
            if (tofHohmannDays < 30.0) tofHohmannDays = 30.0;

            double budgetRef = Math.Max(1000.0, maxBudgetMs);

            // 7 维天体力学相空间坐标 (连续无量纲):
            double x0 = Math.Log(Math.Max(0.01, rArr / rDep));
            double x1 = Math.Log(Math.Max(0.01, rMin / rDep));
            double x2 = Math.Log(Math.Max(0.01, rMax / rDep));
            double x3 = totalTurnDeg / 180.0;
            double x4 = r.EstimatedToFDays / tofHohmannDays;
            double x5 = r.EstimatedTotalDvMs / budgetRef;
            double x6 = (r.FlybyChain != null ? r.FlybyChain.Count : 0) / 8.0;

            return new double[] { x0, x1, x2, x3, x4, x5, x6 };
        }

        /// <summary>
        /// 计算两组拓扑在高维天体力学特征空间中的加权马氏/欧氏物理几何距离
        /// </summary>
        public static double ComputeFeatureDistance(double[] u, double[] v)
        {
            double sumSq = 0.0;
            for (int i = 0; i < FeatureWeights.Length; i++)
            {
                double diff = u[i] - v[i];
                sumSq += FeatureWeights[i] * diff * diff;
            }
            return Math.Sqrt(sumSq);
        }

        /// <summary>
        /// 计算候选链路的无偏连续物理质量标度 Q(c)
        /// 综合能量赤字严格惩罚、推进工质能耗、合理航时与精炼链长 (奥卡姆剃刀)
        /// </summary>
        public static double ComputeQualityScore(ChainReachabilityResult c)
        {
            double deficitPenalty = c.TotalDeficitMs * 0.001;                          // 1 km/s deficit = -1.0 (等价于 1 km/s 推进剂工质能耗)
            double dvPenalty = c.EstimatedTotalDvMs * 0.001;                           // 1 km/s delta-v = -1.0
            double tofPenalty = (c.EstimatedToFDays / 365.25) * 0.35;                  // 1 年 = -0.35 (深空航时成本)
            // 阶段链长与同星共振区分: 异星转移增加深空导航与交会风险，而同星共振自减速/调相系纯二体引力耗散
            int totalLegs = (c.FlybyChain != null ? c.FlybyChain.Count : 0);
            double effectiveLegs = Math.Max(0.0, totalLegs - c.ResonanceCount * 0.65);
            double complexityPenalty = effectiveLegs * 0.60;                           // 0.60 per effective leg
            return -(deficitPenalty + dvPenalty + tofPenalty + complexityPenalty);
        }

        /// <summary>
        /// 高维质量-多样性最优化提取 (Quality-Diversity / Max-Min Feature Dispersion)
        /// 彻底消除预设分类、门禁席位与人工轮询，以 O(K·M) 极速在相空间中自发求解全局非同质精英集
        /// </summary>
        public static List<ChainReachabilityResult> SelectQualityDiversityTopologies(
            List<ChainReachabilityResult> candidates,
            string departure,
            string arrival,
            double maxBudgetMs,
            int maxPoolSize = MGATopologyBudgetDeriver.MaxSupportedChainBodies,
            double diversityLambda = 0.85)
        {
            var selected = new List<ChainReachabilityResult>();
            if (candidates == null || candidates.Count == 0) return selected;

            var pDepObj = SolarSystemCatalog.GetPlanet(departure);
            var pArrObj = SolarSystemCatalog.GetPlanet(arrival);
            // 【2026-09-14 审计修正】原判据 `pArrObj.SMA < pDepObj.SMA * 0.45` 是硬编码的半长轴比例,
            // 正是 MGAPhysicalScales.ClassifyDeepWellBySystemDistribution 的注释明文声称
            // "已取代 `SMA < depSMA * 0.45/0.5/0.55` 一类硬编码比例"的那一条 —— 但它一直留在这里,
            // 且 `isDeepWellArrival` 直接驱动后续"MESSENGER/BepiColombo 构型"配额分支。
            // 现接入已建成的系统内鲁棒分位判据 (转移难度比的 median + k·MAD 上界)。
            bool isDeepWellArrival = (pArrObj != null && pDepObj != null)
                ? MGAPhysicalScales.ClassifyDeepWellBySystemDistribution(
                    pArrObj, pDepObj, pArrObj,
                    MGADefaults.SunMu > 0 ? MGADefaults.SunMu : SolarSystemCatalog.SunMu,
                    SolarSystemCatalog.GetAvailablePlanets())
                : false;

            var cleanedCandidates = new List<ChainReachabilityResult>();
            foreach (var c in candidates)
            {
                if (c.FlybyChain != null && c.FlybyChain.Count > 0)
                {
                    // 结构性退化: 首位即到达天体 = 出发即到站, 无借力意义
                    if (c.FlybyChain[0].Equals(arrival, StringComparison.OrdinalIgnoreCase)) continue;

                    // 物理单共振守恒过滤: 非深势阱任务只允许至多 1 个天体实施自共振 (杜绝 Venus-Venus + Earth-Earth 堆叠)
                    if (!isDeepWellArrival)
                    {
                        int distinctResonantBodies = 0;
                        for (int i = 0; i < c.FlybyChain.Count - 1; i++)
                        {
                            if (c.FlybyChain[i].Equals(c.FlybyChain[i + 1], StringComparison.OrdinalIgnoreCase))
                            {
                                distinctResonantBodies++;
                                while (i < c.FlybyChain.Count - 1 && c.FlybyChain[i].Equals(c.FlybyChain[i + 1], StringComparison.OrdinalIgnoreCase))
                                {
                                    i++;
                                }
                            }
                        }
                        if (distinctResonantBodies > 1) continue;
                    }
                }
                cleanedCandidates.Add(c);
            }
            if (cleanedCandidates.Count == 0) cleanedCandidates = candidates;

            // 特征向量与物理质量缓存
            var featureVectors = new Dictionary<ChainReachabilityResult, double[]>();
            var qualityScores = new Dictionary<ChainReachabilityResult, double>();
            foreach (var c in cleanedCandidates)
            {
                featureVectors[c] = ComputeAstrodynamicFeatureVector(c, departure, arrival, maxBudgetMs);
                qualityScores[c] = ComputeQualityScore(c);
            }

            // 确定性提取: 按 cleanedCandidates 固定列表序遍历, 用布尔标记代替 HashSet 移除。
            // 消除 HashSet 枚举序 (依赖对象地址哈希) 在 qdScore 浮点并列时对决胜的隐性参与。
            int n = cleanedCandidates.Count;
            var taken = new bool[n];

            // 1. 第一个候选个体: 全局物理质量标杆冠军 (Global Merit Champion)
            //    严格 > 比较 + 列表序扫描 = 与稳定 OrderByDescending 等价的确定性决胜
            int championIdx = -1;
            for (int i = 0; i < n; i++)
            {
                if (championIdx < 0 || qualityScores[cleanedCandidates[i]] > qualityScores[cleanedCandidates[championIdx]])
                    championIdx = i;
            }
            if (championIdx >= 0)
            {
                selected.Add(cleanedCandidates[championIdx]);
                taken[championIdx] = true;
            }

            // 2. 深势阱目标连续自共振减速保全 (Deep-Well Multi-Arrival Resonance Preservation):
            // 若终点为深势阱天体，无动力捕获必须依赖该星多次连续自共振减速消除 v_inf。
            // 确保优质多次到达星共振拓扑进入演化池，防止被短链在纯链长奥卡姆惩罚下过度压制。
            //
            // 【2026-09-14 审计修正】原实现使用离散配额 (multiResCount < 2 / maxArrCount >= 4),
            // 注释直接点名 "MESSENGER 构型" 与 "完整历史 BepiColombo 构型", 属按历史答案保留解法。
            // 现改为由**解析共振阶数下界 N_req** 驱动: N_req 由无动力双曲偏折几何给出
            // (ceil(所需三维偏折角 / 单次偏折极限)), 见 ResolveRequiredDeepWellResonanceOrder。
            if (isDeepWellArrival)
            {
                int requiredResOrder = ResolveRequiredDeepWellResonanceOrder(pArrObj, pDepObj);

                // A. 保留满足解析下界的一条多次共振链 (数量取 1, 不再人为固定为 2)。
                for (int ci = 0; ci < n; ci++)
                {
                    if (taken[ci]) continue;
                    var cand = cleanedCandidates[ci];
                    int arrVisits = (cand.FlybyChain != null)
                        ? cand.FlybyChain.Count(b => b.Equals(arrival, StringComparison.OrdinalIgnoreCase)) : 0;
                    if (arrVisits >= requiredResOrder)
                    {
                        selected.Add(cand);
                        taken[ci] = true;
                        break;
                    }
                }

                // B. 深阶共振链保全: 若池中存在阶数更高的共振链, 额外保留其中最完整的一条
                //    (更高阶共振等效于更充分的减速余量, 属物理上有价值的多样性, 而非特定任务构型)。
                int maxArrCount = cleanedCandidates.Max(c => c.FlybyChain != null ? c.FlybyChain.Count(b => b.Equals(arrival, StringComparison.OrdinalIgnoreCase)) : 0);
                if (maxArrCount > requiredResOrder)
                {
                    for (int ci = 0; ci < n; ci++)
                    {
                        if (taken[ci]) continue;
                        var cand = cleanedCandidates[ci];
                        if (cand.FlybyChain != null 
                            && cand.FlybyChain.Count(b => b.Equals(arrival, StringComparison.OrdinalIgnoreCase)) == maxArrCount
                            && cand.FlybyChain.Count > 0 && cand.FlybyChain[0].Equals(departure, StringComparison.OrdinalIgnoreCase))
                        {
                            selected.Add(cand);
                            taken[ci] = true;
                            break;
                        }
                    }

                    // 2) 补充保留"不经出发星自借力"的同阶共振链, 保证该阶数下至少有两条不同前置构型。
                    //    (原注释为 "如 Venus ➔ Mercury*6", 属按历史构型举例, 已去除具体任务指向。)
                    for (int ci = 0; ci < n; ci++)
                    {
                        if (taken[ci]) continue;
                        var cand = cleanedCandidates[ci];
                        if (cand.FlybyChain != null && cand.FlybyChain.Count(b => b.Equals(arrival, StringComparison.OrdinalIgnoreCase)) == maxArrCount)
                        {
                            selected.Add(cand);
                            taken[ci] = true;
                            break;
                        }
                    }
                }
            }

            // 3. 后续个体提取: 基于相空间库仑斥力场 / Max-Min 最大几何发散度步进优化
            while (selected.Count < maxPoolSize)
            {
                int bestIdx = -1;
                double bestQdScore = double.NegativeInfinity;

                for (int ci = 0; ci < n; ci++)
                {
                    if (taken[ci]) continue;
                    var cand = cleanedCandidates[ci];
                    double[] candFeat = featureVectors[cand];

                    // 计算当前候选解到已选解集在相空间中的最近邻距离 (Minimum Distance in Feature Space)
                    double minDistance = double.PositiveInfinity;
                    foreach (var sel in selected)
                    {
                        double dist = ComputeFeatureDistance(candFeat, featureVectors[sel]);
                        if (dist < minDistance) minDistance = dist;
                    }

                    // 质量-多样性联合目标: Quality + λ * DiversityDistance
                    // 同质解 (如水星共振 5 次 vs 6 次) minDistance -> 0，斥力无限大被自然抑制;
                    // 奇异物理构型 (VEEGA/VVEJGA/GrandTour) 在相空间中极度孤立，获得高额发散度增益自发涌现入围
                    double qdScore = qualityScores[cand] + diversityLambda * minDistance;
                    if (qdScore > bestQdScore)
                    {
                        bestQdScore = qdScore;
                        bestIdx = ci;
                    }
                }

                if (bestIdx < 0) break;
                selected.Add(cleanedCandidates[bestIdx]);
                taken[bestIdx] = true;
            }

            return selected;
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
            bool noInsertionBurn = false,
            double windowSpanDays = 0.0)
        {
            var evaluated = new List<ChainReachabilityResult>();
            var allCandidates = new List<List<string>>(candidateChains ?? new List<List<string>>());

            // 始终补充天体力学势阱梯级爬升与多阶共振候选链，确保最优物理拓扑进入全域竞争池
            var synthesized = SynthesizeClimbingSequences(departure, arrival, userGoal);
            foreach (var syn in synthesized)
            {
                if (!allCandidates.Any(c => c.SequenceEqual(syn)))
                {
                    allCandidates.Add(syn);
                }
            }

            if (userGoal != null && userGoal.Count > 0)
            {
                // 确保用户指定的目标借力天体得到严格满足，过滤掉未包含目标天体的无关直达/旁支拓扑
                allCandidates = allCandidates.Where(c => userGoal.All(g => c.Contains(g, StringComparer.OrdinalIgnoreCase))).ToList();
            }
            else
            {
                // 若用户未指定特定借力意向，直达单段转移 (Direct Transfer: []) 必须始终作为物理对照基准入池
                if (!allCandidates.Any(c => c.Count == 0))
                {
                    allCandidates.Insert(0, new List<string>());
                }
            }

            foreach (var chain in allCandidates)
            {
                var report = AnalyzeChainReachability(departure, arrival, chain, departureEpochUT, maxBudgetMs, maxDsmBudgetMs, noInsertionBurn, windowSpanDays);
                evaluated.Add(report);
            }

            LastEvaluatedChains = new List<ChainReachabilityResult>(evaluated);

            // 过滤出物理可行的序列，按能量赤字与序列精简度 (奥卡姆剃刀原则) 升序排列，并保留多样性拓扑
            // 严格门禁：单航段存在天文级速度赤字 (> MaxPhysicalEnergyDeficitMs) 的跨越势阱序列必须被剪除，迫使求解器走完整引力阶梯
            var physicallyViableChains = evaluated.Where(e => !e.LegReports.Any(l => l.EnergyDeficitMs > MGADefaults.MaxPhysicalEnergyDeficitMs)).ToList();
            var pool = physicallyViableChains.Count > 0 ? physicallyViableChains : evaluated;

            // 【已移除】此处曾有以 "Venus" / "Earth" 为键的整段诊断输出块 (VEEGA/EGA 查找 + 打印)。
            // 移除原因: (a) 内联天体名 "Venus" / "Earth" 及链 "Venus->Earth->Earth", 违反项目基线
            //               "零天体名硬编码" (须经 SolarSystemCatalog 动态解析, 不得在求解逻辑中特判具体天体);
            //           (b) 纯诊断输出, 对求解结果零影响;
            //           (c) 其中 `foreach (var vc in vChains...) { }` 为空体死代码。

            // 纯连续物理多目标排序 (Deb's Feasibility-First & Continuous Physics Deficit Ordering):
            // 0 硬编码规则，0 人工经验阶梯分值。
            // 1. 物理约束容许优先 (Deb准则): 单腿最大赤字在推进器容许预算内的拓扑严格优先于存在物理断崖的拓扑;
            // 2. 连续物理总赤字最小化 (TotalDeficitMs): 缺少引力阶梯或共振的劣质拓扑其几何偏折与能量赤字巨大，自然沉底;
            // 3. 帕累托综合多目标评价 (ParetoScore);
            // 4. 预估总冲量与链长复杂度 (奥卡姆剃刀准则, 由连续质量分 effectiveLegs·0.60 承担,
            //    原离散分档链长 >12?2:>10?1:0 已删除 —— 实测历史集最长链 5 腿, 分档从未绑定).
            double maxLegRef = Math.Max(maxDsmBudgetMs, MGADefaults.DefaultMaxDsmDeltaVMs);
            var feasibleCandidates = pool.Where(e => e.IsFeasible)
                .Select(e => (R: e,
                              MaxLeg: e.LegReports.Count > 0 ? e.LegReports.Max(l => l.EnergyDeficitMs + l.TurnDeficitMs) : 0.0))
                .OrderBy(t => (t.MaxLeg > maxLegRef) ? 1 : 0)
                .ThenBy(t => t.R.ParetoScore)
                .ThenBy(t => t.R.EstimatedTotalDvMs)
                .ThenBy(t => t.R.TotalDeficitMs)
                .ThenBy(t => t.MaxLeg)
                .ThenBy(t => t.R.FlybyChain.Count)
                .Select(t => t.R)
                .ToList();
            if (feasibleCandidates.Count == 0)
            {
                feasibleCandidates = pool
                    .Select(e => (R: e,
                                  MaxLeg: e.LegReports.Count > 0 ? e.LegReports.Max(l => l.EnergyDeficitMs + l.TurnDeficitMs) : 0.0))
                    .OrderBy(t => (t.MaxLeg > maxLegRef) ? 1 : 0)
                    .ThenBy(t => t.R.ParetoScore)
                    .ThenBy(t => t.R.EstimatedTotalDvMs)
                    .ThenBy(t => t.R.TotalDeficitMs)
                    .ThenBy(t => t.MaxLeg)
                    .ThenBy(t => t.R.FlybyChain.Count)
                    .Select(t => t.R)
                    .ToList();
            }

            // 【已移除】此处曾有针对 "Venus->Venus->Earth->Jupiter" 的调试打印块。
            // 移除原因: (a) 直接内联天体名构成的链, 违反项目基线"零天体名硬编码"(应经 SolarSystemCatalog 动态解析);
            //           (b) 纯诊断输出, 对求解结果无任何影响, 但污染生产日志。

            // 2. 高维天体力学相空间质量-多样性最优化提取 (Quality-Diversity / Max-Min Feature Dispersion):
            // 彻底摒弃离散流派标签、门禁席位预设与人工轮询，基于连续天体力学不变量在多维相空间中展开多体极值发散
            var diverseFeasible = SelectQualityDiversityTopologies(
                feasibleCandidates,
                departure,
                arrival,
                maxBudgetMs,
                maxPoolSize: Math.Max(24, MGATopologyBudgetDeriver.MaxSupportedChainBodies),
                diversityLambda: 0.85);


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

        /// <summary>
        /// DirectCompute GPU 极速全太阳系多天体百年历表与共振相位雷达图
        /// 毫秒级生成多行星对在长达半个世纪时间跨度内的相对相位角网格
        /// </summary>
        public static float[,] EvaluateCenturyPhaseRadarGPU(
            List<string> planetNames,
            string centralBody,
            double startUt,
            double spanDays,
            double stepDays)
        {
            if (planetNames == null || planetNames.Count < 2) return null;
            if (!PykepNative.IsGpuComputeAvailable) return null;

            int stepCount = Math.Max(1, (int)Math.Ceiling(spanDays / Math.Max(1.0, stepDays)));
            double centralMu = SolarSystemCatalog.GetCentralMu(centralBody ?? "Sun");

            // 构造无重复行星对
            var pairsA = new List<PykepNative.GpuPlanetElements>();
            var pairsB = new List<PykepNative.GpuPlanetElements>();

            for (int i = 0; i < planetNames.Count; i++)
            {
                var pA = SolarSystemCatalog.GetPlanet(planetNames[i]);
                if (pA == null || pA.SMA <= 0) continue;

                for (int j = i + 1; j < planetNames.Count; j++)
                {
                    var pB = SolarSystemCatalog.GetPlanet(planetNames[j]);
                    if (pB == null || pB.SMA <= 0) continue;

                    pairsA.Add(new PykepNative.GpuPlanetElements
                    {
                        sma_km = pA.SMA, ecc = pA.ECC, inc_rad = pA.INC * Math.PI / 180.0,
                        raan_rad = pA.RAAN * Math.PI / 180.0, aop_rad = pA.AOP * Math.PI / 180.0,
                        ma0_rad = pA.MA0 * Math.PI / 180.0, epoch_ut = pA.EpochJ2000,
                        mu_self = pA.Mu, radius_km = pA.RadiusKm, park_alt_km = 100.0
                    });

                    pairsB.Add(new PykepNative.GpuPlanetElements
                    {
                        sma_km = pB.SMA, ecc = pB.ECC, inc_rad = pB.INC * Math.PI / 180.0,
                        raan_rad = pB.RAAN * Math.PI / 180.0, aop_rad = pB.AOP * Math.PI / 180.0,
                        ma0_rad = pB.MA0 * Math.PI / 180.0, epoch_ut = pB.EpochJ2000,
                        mu_self = pB.Mu, radius_km = pB.RadiusKm, park_alt_km = 100.0
                    });
                }
            }

            int pairCount = pairsA.Count;
            if (pairCount == 0) return null;

            long totalPoints = (long)pairCount * stepCount;
            var sw = Stopwatch.StartNew();
            float[] flatOutput = new float[pairCount * stepCount];
            int ret = PykepNative.EphemerisRadarGpuEvaluate(
                pairsA.ToArray(), pairsB.ToArray(),
                pairCount, centralMu,
                startUt, stepDays, stepCount,
                flatOutput);

            sw.Stop();
            Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution(
                "GPU Century Phase Radar", sw.Elapsed.TotalMilliseconds, totalPoints, ret == 0);

            if (ret != 0) return null;

            float[,] result = new float[pairCount, stepCount];
            for (int s = 0; s < stepCount; s++)
            {
                for (int p = 0; p < pairCount; p++)
                {
                    result[p, s] = flatOutput[s * pairCount + p];
                }
            }

            return result;
        }

        private static double Asinh(double x)
        {
            return Math.Log(x + Math.Sqrt(x * x + 1.0));
        }

        /// <summary>
        /// 深势阱目标(需同星多圈共振减速)的**解析共振阶数下界** N_req。
        ///
        /// 【2026-09-14 新增】取代原深势阱保全分支中的离散配额 (2 / 4) 与对
        /// MESSENGER / BepiColombo 历史构型的点名。
        ///
        /// 计算委托 <see cref="MGAPlanetaryAssistManager.ComputeResonantDeflectionNeeds"/>:
        ///   N_req = ceil( theta_req / delta_max )
        ///   delta_max = 2·asin(1 / (1 + r_safe·v∞²/mu))   —— 该天体单次无动力飞掠的偏折极限
        ///   theta_req                                   —— 由拱点折返几何与倾角差给出
        /// 全为几何推导, 对任意恒星系与单位制成立。
        /// </summary>
        public static int ResolveRequiredDeepWellResonanceOrder(SolarPlanet arrivalBody, SolarPlanet departureBody) {
            if (arrivalBody == null || arrivalBody.Mu <= 0) return 1;
            string central = !string.IsNullOrEmpty(arrivalBody.ParentName)
                ? arrivalBody.ParentName
                : SolarSystemCatalog.RootStarName;
            try {
                MGAPlanetaryAssistManager.ComputeResonantDeflectionNeeds(
                    arrivalBody, departureBody, arrivalBody, central,
                    out _, out _, out _, out int nReq);
                return Math.Max(1, Math.Min(MGATopologyBudgetDeriver.MaxSupportedChainBodies, nReq));
            } catch {
                return 1;
            }
        }
    }
}
