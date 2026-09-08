// ============================================================================
// MGAPlanetaryAssistManager.cs
// 专门的行星借力统一管理器：管理所有可能借力的行星，彻底杜绝任何星球名称硬编码。
// 联动“引力必吃榜”与真实开普勒轨道根数，支持任意出发星、任意到达星与任意目标天体，
// 通用推导内侧跳板天体、中继爬升天体、外侧巨行星弹弓以及深引力势阱所需共振借力次数。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 行星借力统一管理器 (Planetary Assist Manager)
    /// 核心职责：
    /// 1. 0 行星名称硬编码：消灭对特定星球 (如 Earth, Venus, Mercury, Jupiter 等) 的写死认知偏差；
    /// 2. 联动引力必吃榜 (Gravity Assist Rankings)：基于特征势深 Phi_safe = Mu / SafeRadiusKm 自动排序；
    /// 3. 通用几何与动力学角色推选：
    ///    - 内侧变轨跳板天体 (Inner Assist Jumping Stone)
    ///    - 爬升中继天体 (Mid Assist / Stepping Stone)
    ///    - 外太阳系巨行星超级弹弓 (Outer Giant Slingshots)
    /// 4. 势阱折返共振借力次数 N_req 理论解析与阶梯池生成 (完全对齐 BepiColombo / MESSENGER 物理机理)；
    /// 5. 开普勒停泊轨道发射双曲逃逸与注入冲量普适测算。
    /// </summary>
    public static class MGAPlanetaryAssistManager
    {
        /// <summary>
        /// 获取给定中心天体系统下的“引力必吃榜” (纯物理势深降序排列)
        /// </summary>
        public static List<(SolarPlanet Body, double Ratio, double PotentialDepth)> GetGravityAssistRankings(string centralBody)
        {
            return SolarSystemCatalog.GetGravityAssistRankings(centralBody);
        }

        /// <summary>
        /// 动态推选内侧变轨跳板天体 (Inner Assist Jumping Stone)
        /// 定义：半长轴介于深势阱目标天体 deepW 与出发星 pDep 之间、具有最高引力势深的内行星 (例如太阳系中的金星、坎巴拉系中的 Eve)
        /// </summary>
        public static SolarPlanet GetInnerAssistJumpingStone(SolarPlanet pDep, SolarPlanet deepW, string centralBody)
        {
            if (pDep == null || deepW == null || pDep.SMA <= 0 || deepW.SMA <= 0) return null;

            var rankings = GetGravityAssistRankings(centralBody);
            if (rankings == null || rankings.Count == 0) return null;

            double minSma = Math.Min(deepW.SMA, pDep.SMA);
            double maxSma = Math.Max(deepW.SMA, pDep.SMA);

            var candidates = rankings
                .Where(r => r.Body != null && !r.Body.IsMoon &&
                            r.Body.SMA > minSma * 1.05 && r.Body.SMA < maxSma * 0.95 &&
                            !r.Body.Name.Equals(pDep.Name, StringComparison.OrdinalIgnoreCase) &&
                            !r.Body.Name.Equals(deepW.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.PotentialDepth)
                .Select(r => r.Body)
                .ToList();

            return candidates.FirstOrDefault();
        }

        /// <summary>
        /// 动态推选中继爬升天体 (Mid Assist / Stepping Stone)
        /// 定义：位于 deepW 外侧、在出发星公转轨道附近具有显著引力势深的天体 (优先考虑出发星本身或同级大行星，如 Earth 或 Kerbin)
        /// </summary>
        public static SolarPlanet GetMidClimbingSteppingStone(SolarPlanet pDep, SolarPlanet deepW, string centralBody)
        {
            if (pDep == null) return null;

            var rankings = GetGravityAssistRankings(centralBody);
            if (rankings == null || rankings.Count == 0) return pDep;

            double refSma = pDep.SMA;
            var candidates = rankings
                .Where(r => r.Body != null && !r.Body.IsMoon &&
                            r.Body.SMA >= refSma * 0.85 && r.Body.SMA <= refSma * 1.8 &&
                            (deepW == null || r.Body.SMA > deepW.SMA * 1.2))
                .OrderByDescending(r => r.PotentialDepth)
                .Select(r => r.Body)
                .ToList();

            return candidates.FirstOrDefault() ?? pDep;
        }

        /// <summary>
        /// 动态推选外侧巨行星超级弹弓 (Outer Giant Slingshots)
        /// 定义：位于出发星与到达星之间、引力势深极强的大行星 (例如木星、土星、坎巴拉 Jool 等)
        /// </summary>
        public static List<SolarPlanet> GetOuterGiantSlingshots(SolarPlanet pDep, SolarPlanet pArr, string centralBody, int maxCount = 2)
        {
            var list = new List<SolarPlanet>();
            if (pDep == null || pArr == null || pDep.SMA <= 0 || pArr.SMA <= 0) return list;

            var rankings = GetGravityAssistRankings(centralBody);
            if (rankings == null || rankings.Count == 0) return list;

            double minSma = pDep.SMA * 1.4;
            double maxSma = pArr.SMA * 0.95;

            if (minSma >= maxSma) return list;

            var giants = rankings
                .Where(r => r.Body != null && !r.Body.IsMoon &&
                            r.Body.SMA >= minSma && r.Body.SMA <= maxSma &&
                            !r.Body.Name.Equals(pDep.Name, StringComparison.OrdinalIgnoreCase) &&
                            !r.Body.Name.Equals(pArr.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.PotentialDepth)
                .Take(maxCount)
                .Select(r => r.Body)
                .ToList();

            return giants;
        }

        /// <summary>
        /// 提取顺向/逆向连续转移的中间所有可用行星跳板 (按半长轴严格排序)
        /// </summary>
        public static List<SolarPlanet> GetForwardSteppingStones(SolarPlanet pDep, SolarPlanet pArr, string centralBody)
        {
            var list = new List<SolarPlanet>();
            if (pDep == null || pArr == null || pDep.SMA <= 0 || pArr.SMA <= 0) return list;

            var rankings = GetGravityAssistRankings(centralBody);
            if (rankings == null || rankings.Count == 0) return list;

            bool isOutward = pArr.SMA > pDep.SMA;
            double minSma = Math.Min(pDep.SMA, pArr.SMA);
            double maxSma = Math.Max(pDep.SMA, pArr.SMA);

            var stones = rankings
                .Where(r => r.Body != null && !r.Body.IsMoon &&
                            r.Body.SMA > minSma * 1.05 && r.Body.SMA < maxSma * 0.95 &&
                            !r.Body.Name.Equals(pDep.Name, StringComparison.OrdinalIgnoreCase) &&
                            !r.Body.Name.Equals(pArr.Name, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Body);

            if (isOutward)
            {
                return stones.OrderBy(b => b.SMA).ToList();
            }
            else
            {
                return stones.OrderByDescending(b => b.SMA).ToList();
            }
        }

        /// <summary>
        /// 通用天体力学推导深势阱天体无动力双曲偏折所需转角与共振借力次数 N_req
        /// </summary>
        public static void ComputeResonantDeflectionNeeds(
            SolarPlanet targetBody,
            SolarPlanet incomingBody,
            SolarPlanet outgoingBody,
            string centralBody,
            out double vInfKms,
            out double singleTurnDeg,
            out double reqTurnDeg,
            out int nReq)
        {
            vInfKms = 3.0;
            singleTurnDeg = 10.0;
            reqTurnDeg = 30.0;
            nReq = 1;

            if (targetBody == null || targetBody.SMA <= 0) return;

            double muCentral = SolarSystemCatalog.GetCentralMu(centralBody);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;

            double rIn = incomingBody != null && incomingBody.SMA > 0 ? incomingBody.SMA : targetBody.SMA * 1.8;
            double rOut = outgoingBody != null && outgoingBody.SMA > 0 ? outgoingBody.SMA : targetBody.SMA * 2.5;

            // 1. 估算沿切向转移进入目标天体轨道时的相对双曲过剩速度 V_inf (含三维倾角差)
            double aApproach = (targetBody.SMA + rIn) * 0.5;
            double vCraft = Math.Sqrt(muCentral * (2.0 / targetBody.SMA - 1.0 / aApproach));
            double vPlanet = Math.Sqrt(muCentral / targetBody.SMA);
            double incIn = incomingBody != null ? incomingBody.INC : 0.0;
            double dInc = Math.Abs(targetBody.INC - incIn);
            double dPlane = vPlanet * Math.Sin(dInc * Math.PI / 180.0);
            vInfKms = Math.Max(1.5, Math.Sqrt(Math.Pow(vCraft - vPlanet, 2.0) + Math.Pow(dPlane, 2.0)));

            // 2. 目标天体在 vInfKms 下的最大单次物理偏折角 delta_max
            double rSafe = targetBody.SafeRadiusKm > 0 ? targetBody.SafeRadiusKm : (targetBody.RadiusKm > 0 ? targetBody.RadiusKm + 100.0 : 3000.0);
            singleTurnDeg = targetBody.ComputeMaxDeflectionDeg(vInfKms);
            if (singleTurnDeg <= 0.1)
            {
                singleTurnDeg = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(targetBody.Mu, rSafe, vInfKms);
            }
            if (singleTurnDeg <= 0.1) singleTurnDeg = 1.0;

            // 3. 拱点折返几何偏折所需三维总偏折角 theta_req (结合偏心率航向角与倾角差)
            double eIn = Math.Abs(rIn - targetBody.SMA) / (rIn + targetBody.SMA);
            double eOut = Math.Abs(rOut - targetBody.SMA) / (rOut + targetBody.SMA);
            double fpaSumDeg = (Math.Asin(Math.Min(0.95, eIn)) + Math.Asin(Math.Min(0.95, eOut))) * (180.0 / Math.PI);
            reqTurnDeg = Math.Max(10.0, Math.Sqrt(Math.Pow(fpaSumDeg * 0.75, 2.0) + Math.Pow(dInc, 2.0)));

            // 4. 理论无动力共振借力次数 N_req = ceil(reqTurn / delta_max)
            nReq = Math.Max(1, (int)Math.Ceiling(reqTurnDeg / singleTurnDeg));
        }

        /// <summary>
        /// 通用估算从任意出发星停泊轨道逃逸并注入首个借力星轨道的发射冲量 (m/s)
        /// 0 硬编码：纯开普勒能量与二体势阱逃逸解算，杜绝将出发星默认为地球或金星特判
        /// </summary>
        public static double EstimateLaunchInjectionDvMs(string departure, string firstFlyby)
        {
            var pDep = SolarSystemCatalog.GetPlanet(departure);
            if (pDep == null || pDep.SMA <= 0 || pDep.Mu <= 0) return 3800.0;

            var pFirst = !string.IsNullOrEmpty(firstFlyby) ? SolarSystemCatalog.GetPlanet(firstFlyby) : null;
            if (pFirst == null || pFirst.SMA <= 0) return 3800.0;

            string cBody = !string.IsNullOrEmpty(pDep.ParentName) ? pDep.ParentName : SolarSystemCatalog.RootStarName;
            double muCentral = SolarSystemCatalog.GetCentralMu(cBody);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;

            double r1 = pDep.SMA;
            double r2 = pFirst.SMA;
            double aTrans = (r1 + r2) * 0.5;
            double v1 = Math.Sqrt(muCentral / r1);
            double vTrans1 = Math.Sqrt(muCentral * (2.0 / r1 - 1.0 / aTrans));
            double vInf = Math.Abs(vTrans1 - v1);

            double rSafe = pDep.SafeRadiusKm > 0 ? pDep.SafeRadiusKm : (pDep.RadiusKm > 0 ? pDep.RadiusKm + 200.0 : 6571.0);
            double vEsc = Math.Sqrt(2.0 * pDep.Mu / rSafe);
            double vCirc = Math.Sqrt(pDep.Mu / rSafe);
            double vInj = Math.Sqrt(vInf * vInf + vEsc * vEsc) - vCirc;

            // 叠加上升重力损失与名义大气耗损余量 (~250 m/s)
            return Math.Max(2500.0, Math.Min(7000.0, (vInj + 0.25) * 1000.0));
        }

        /// <summary>
        /// 基于势阱梯级爬升法则与“引力必吃榜”，普适自主推导高效物理拓扑序列。
        /// 100% 拒绝任何星球名称的硬编码，全面支持太阳系 (Sol)、坎巴拉系 (Kerbol) 及任意自定义恒星系，
        /// 自主识别深引力势阱目标天体、内侧变轨跳板、中继泵升天体与外侧巨行星超级弹弓。
        /// </summary>
        public static List<List<string>> SynthesizePhysicalClimbingChains(string departure, string arrival, List<string> userGoal)
        {
            var list = new List<List<string>>();
            var pDep = SolarSystemCatalog.GetPlanet(departure);
            var pArr = SolarSystemCatalog.GetPlanet(arrival);
            if (pDep == null || pArr == null || pDep.SMA <= 0 || pArr.SMA <= 0) return list;

            string centralBody = !string.IsNullOrEmpty(pDep.ParentName) ? pDep.ParentName : SolarSystemCatalog.RootStarName;

            // 1. 获取中心恒星系统内由天体力学势深排序的“引力必吃榜”
            var assistRankings = GetGravityAssistRankings(centralBody);
            if (assistRankings == null || assistRankings.Count == 0) return list;

            // 2. 提取用户指定的深空关键任务天体 (Goal Waypoints)
            var goalPlanets = (userGoal ?? new List<string>())
                .Select(g => SolarSystemCatalog.GetPlanet(g))
                .Where(p => p != null && p.SMA > 0)
                .ToList();

            // 识别深引力势阱天体：轨道半长轴显著低于出发星的天体 (如从地球/火星出发访问水星/Moho)
            var innerDeepGoals = goalPlanets.Where(p => p.SMA < pDep.SMA).OrderBy(p => p.SMA).ToList();
            bool isOutwardTransfer = pArr.SMA > pDep.SMA * 1.3;

            // 场景 A: 存在深势阱天体目标，且最终目标处于外部系统 (经典的深井折返爬升任务，如 Earth/Mars ➔ Mercury ➔ Neptune/Jupiter)
            if (innerDeepGoals.Count > 0 && isOutwardTransfer)
            {
                var deepW = innerDeepGoals.First(); // 最深势阱目标天体 (如 Mercury)

                // 2.1 动态推选内侧变轨跳板天体 (Inner Assist Jumping Stone)
                var innerAssist = GetInnerAssistJumpingStone(pDep, deepW, centralBody);

                // 2.2 动态推选爬升中继天体 (Mid Assist / Stepping Stone)
                var midAssist = GetMidClimbingSteppingStone(pDep, deepW, centralBody);

                // 2.3 动态推选外侧巨行星超级弹弓 (Outer Giant Slingshots)
                var outerGiants = GetOuterGiantSlingshots(pDep, pArr, centralBody, 2);
                var giant1 = outerGiants.FirstOrDefault();
                var giant2 = outerGiants.Count > 1 ? outerGiants[1] : null;

                // 2.4 普适动力学推导深势阱小天体所需共振借力次数 N_req
                ComputeResonantDeflectionNeeds(deepW, innerAssist ?? pDep, midAssist, centralBody,
                    out double vInfDeep, out double singleTurnW, out double reqTurnW, out int nReq);

                // 确保候选次数覆盖充分范围 (涵盖 1 到 maxCandidateN，让优化器全自动搜索最优 n*)
                int maxCandidateN = Math.Max(nReq, 6);
                var resonantCounts = new List<int>();
                for (int c = maxCandidateN; c >= 1; c--)
                {
                    resonantCounts.Add(c);
                }

                // 2.5 拓扑组合生成：构建完整物理爬升梯级 (涵盖单/双内侧跳板进场与离场)
                int[] vPreCounts = (innerAssist != null) ? new int[] { 1, 2 } : new int[] { 0 };
                int[] vPostCounts = (innerAssist != null) ? new int[] { 1, 0, 2 } : new int[] { 0 };
                int[] midPostCounts = new int[] { 2, 1, 3 };

                foreach (int m in resonantCounts)
                {
                    // 拓扑家族 1: 单/双内侧跳板前置 + W x m + 内侧跳板回升 + 中继星共振 + 巨行星
                    foreach (int vPre in vPreCounts)
                    {
                        foreach (int vPost in vPostCounts)
                        {
                            foreach (int e in midPostCounts)
                            {
                                var chain = new List<string>();
                                if (innerAssist != null)
                                {
                                    for (int k = 0; k < vPre; k++) chain.Add(innerAssist.Name);
                                }
                                for (int k = 0; k < m; k++) chain.Add(deepW.Name);
                                if (innerAssist != null)
                                {
                                    for (int k = 0; k < vPost; k++) chain.Add(innerAssist.Name);
                                }
                                for (int k = 0; k < e; k++) chain.Add(midAssist.Name);
                                if (giant1 != null) chain.Add(giant1.Name);

                                list.Add(chain);

                                // 双巨行星超级弹弓 (如 Jupiter + Saturn)
                                if (giant1 != null && giant2 != null)
                                {
                                    var chainDualGiant = new List<string>(chain);
                                    int gIdx = chainDualGiant.LastIndexOf(giant1.Name);
                                    if (gIdx >= 0)
                                    {
                                        chainDualGiant.Insert(gIdx + 1, giant2.Name);
                                        list.Add(chainDualGiant);
                                    }
                                }
                            }
                        }
                    }

                    // 拓扑家族 2: 直出回跳中继星构型 (Dep ➔ Inner x 1~2 ➔ W x N ➔ Mid x 2 ➔ Giant)
                    if (innerAssist != null)
                    {
                        foreach (int vPre in vPreCounts)
                        {
                            var chainDirectMid = new List<string>();
                            for (int k = 0; k < vPre; k++) chainDirectMid.Add(innerAssist.Name);
                            for (int k = 0; k < m; k++) chainDirectMid.Add(deepW.Name);
                            chainDirectMid.Add(midAssist.Name);
                            chainDirectMid.Add(midAssist.Name);
                            if (giant1 != null) chainDirectMid.Add(giant1.Name);
                            list.Add(chainDirectMid);
                            if (giant1 != null && giant2 != null)
                            {
                                var chainDual = new List<string>(chainDirectMid);
                                int gIdx = chainDual.LastIndexOf(giant1.Name);
                                if (gIdx >= 0) { chainDual.Insert(gIdx + 1, giant2.Name); list.Add(chainDual); }
                            }
                        }
                    }
                }

                // 拓扑家族 4: 交错式双星共振泵升拓扑 (Inner ➔ W x n ➔ Inner ➔ W x m ➔ Inner ➔ Mid x 2 ➔ Giant)
                if (innerAssist != null)
                {
                    for (int n = 1; n <= 2; n++)
                    {
                        for (int m = 1; m <= 2; m++)
                        {
                            var interleaved = new List<string> { innerAssist.Name };
                            for (int k = 0; k < n; k++) interleaved.Add(deepW.Name);
                            interleaved.Add(innerAssist.Name);
                            for (int k = 0; k < m; k++) interleaved.Add(deepW.Name);
                            interleaved.Add(innerAssist.Name);
                            interleaved.Add(midAssist.Name);
                            interleaved.Add(midAssist.Name);
                            if (giant1 != null) interleaved.Add(giant1.Name);
                            list.Add(interleaved);
                        }
                    }
                }
            }
            else
            {
                // 场景 B: 普适星际顺向/逆向借力梯级合成 (从任意出发星到任意到达星)
                var forwardBodies = GetForwardSteppingStones(pDep, pArr, centralBody);
                if (forwardBodies.Count > 0)
                {
                    list.Add(forwardBodies.Select(b => b.Name).ToList());

                    // 添加含首天体 2:1 调相共振的梯级
                    var withRes = new List<string> { forwardBodies[0].Name, forwardBodies[0].Name };
                    for (int i = 1; i < forwardBodies.Count; i++) withRes.Add(forwardBodies[i].Name);
                    list.Add(withRes);
                }
            }

            return list;
        }
    }
}
