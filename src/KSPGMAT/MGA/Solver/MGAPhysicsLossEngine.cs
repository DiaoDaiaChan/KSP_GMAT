// ============================================================================
// MGAPhysicsLossEngine.cs
// 无量纲归一化损失场 + 增广拉格朗日约束引擎
//
// 【本次重构解决的问题】
//   原实现 Loss = DeltaV(km/s, 典型 3~10) + 惩罚项(系数 1e4 ~ 2.5e4)
//   主目标与惩罚项相差 4 个数量级 ⇒ 实际退化为硬阶梯函数, 主目标梯度被完全淹没,
//   注释宣称的 "C1 连续势垒平滑引导" 在数值上并不成立; 且所有下游阈值
//   (< 1e8 / < 1e7 / < 1e6 / < 1e5 / >= 2e5) 只能跟着猜量级。
//
// 【重构后的数学形式】(全部无量纲)
//
//     J(x) = f_hat(x)  +  Σ_k [ λ_k · g_hat_k(x)  +  (ρ_k / 2) · g_hat_k(x)^2 ]
//
//     f_hat   = DeltaV_subject / DeltaV_ref                 [O(1), 相对任务基准]
//     g_hat_k = max(0, 越界量) / scale_k                    [O(1) 表示"刚好越界一个基准尺度"]
//     λ_k, ρ_k 由 MGAAugmentedLagrangian 对偶上升自适应给出  [零手调]
//
// 由此获得三个可直接使用的**规范阈值** (取代 1e8/1e7/1e6/1e5/2e5):
//     FeasibleLossCeiling   : J <= 该值 且违约归零 => 完全可行
//     DeepInfeasibilityLoss : J >= 该值 => 违背量已达基准预算量级 (可熔断)
//     SelectionLossCeiling  : 选解阶段可接受的最差损失
// 它们都是"参考预算的倍数", 与单位制、恒星系无关。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using KSPGMAT.Model;
using KSPGMAT.MGA.AI;
using UnityEngine;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 无量纲物理损失场与约束引擎
    /// </summary>
    public static class MGAPhysicsLossEngine
    {
        // =====================================================================
        // 规范阈值 (无量纲, 以"参考预算的倍数"表述, 取代原有 1e8/1e7/1e6/1e5/2e5)
        // =====================================================================

        /// <summary>
        /// 可行上界。归一化后主目标 f_hat = DeltaV/DeltaV_ref, 无约束违背时 J = f_hat。
        /// f_hat <= 1 表示"恰好用满参考预算", 故取 1.0 + 数值容差作为可行判定上界。
        /// </summary>
        public const double FeasibleLossCeiling = 1.0 + 1.0e-9;

        /// <summary>
        /// 深度不可行门禁。在规范化无量纲损失场 J(x) = f_hat + penalties 中，
        /// f_hat <= 1.0 表示满足参考预算，SelectionLossCeiling = 4.0 允许探索次优轨道。
        /// 当 J(x) >= 10.0 (基准预算的 10 倍，即 SelectionLossCeiling * 2.5) 时，
        /// 说明该解存在严重的物理不可行约束违规惩罚（如 Lambert 无解或极大超界），
        /// 是"应当熔断该拓扑"的数学标志。避免将早期单纯高能探索解误判为深层不可行。
        /// </summary>
        public const double DeepInfeasibilityLoss = 10.0;

        /// <summary>选解阶段可接受的最差损失 (仍允许 4 倍基准预算的候选进入最终审计)。</summary>
        public const double SelectionLossCeiling = 4.0;

        /// <summary>
        /// 倾角锁定对准容差 (deg)。这是**工程规格**, 不是物理常数: 它表达"发射倾角
        /// 允许偏离目标几度"。惩罚量以该容差自身为尺度归一化 (越界 1 倍容差 => g_hat = 1),
        /// 因此该数值只影响工程口径, 不影响惩罚量纲与量级。
        /// </summary>
        public const double InclinationAlignToleranceDeg = 0.05;

        /// <summary>
        /// 航时决胜正则系数 (无量纲)。物理背景: 双体开普勒转移在 T -> inf 时出射能量
        /// 单调递减, 若目标函数无时间项, 优化器必然滑向无限长周期的无界极值。
        /// 该项以极小权重打破平坦能量沼泽, 使同等 DeltaV 下优先收敛到航时更短的物理捷径。
        /// 取 1e-3 表示"航时偏长 1 倍参考航时, 约等于损失上升 0.001" —— 纯决胜项, 不干扰主目标。
        /// </summary>
        public const double DurationTieBreakerWeight = 1.0e-3;

        /// <summary>
        /// L1 稀疏机动正则总权重 (无量纲)。含义: "一条腿上的 DSM 若等于整个任务参考预算,
        /// 其惩罚相当于把主目标翻倍"。按腿数摊分 (见 DsmSparsityWeightPerLeg),
        /// 取代原手调常数 LambdaL1Dsm = 0.85。
        /// </summary>
        public const double DsmSparsityWeight = 1.0;

        // =====================================================================
        // 参考基准 (DeltaV_ref) 缓存提供器
        // =====================================================================

        /// <summary>
        /// 归一化参考基准缓存。归一化基准必须来自物理 (霍曼基准或飞船预算),
        /// 但计算涉及原生霍曼解算, 故按 (出发星|到达星|预算) 键缓存, 避免每代重复计算。
        /// </summary>
        internal static class LossReferenceCache
        {
            private static readonly ConcurrentDictionary<string, double> _cache =
                new ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            private const int MaxEntries = 128;

            public static double Get(MGAConfig config)
            {
                if (config == null) return 1.0;

                // 1) 若给定飞船总预算, 直接以预算为归一化基准 (预算即"1 个单位成本")
                if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0)
                {
                    return config.VesselMaxDeltaVMs / 1000.0;
                }

                string key = $"{config.DepartureBody ?? "?"}|{config.DestinationBody ?? "?"}|{config.DeparturePeAKm:F0}|{config.DestinationPeAKm:F0}";
                if (_cache.Count > MaxEntries) _cache.Clear();

                return _cache.GetOrAdd(key, _ =>
                {
                    var benchmark = MGAMCTSTopologySelector.CalculateUniversalHohmannBenchmark(
                        config.DepartureBody, config.DestinationBody,
                        config.DeparturePeAKm, config.DestinationPeAKm);
                    double tot = benchmark.TotHohmannKms;
                    // 霍曼基准不可用时退化为 1 km/s (仅防止除零, 不参与物理判断)
                    return (tot > 1e-6 && !double.IsNaN(tot)) ? tot : 1.0;
                });
            }
        }

        // =====================================================================
        // 无量纲损失计算
        // =====================================================================

        /// <summary>
        /// 计算候选轨道的无量纲归一化损失 J(x)。
        /// 兼容旧签名: alm 为 null 时使用一个临时 ALM 实例 (无跨代对偶上升, 但仍无量纲可比较)。
        /// </summary>
        public static double ComputeLoss(MGASolution sol, MGAConfig config)
        {
            return ComputeLoss(sol, config, null);
        }

        /// <summary>
        /// 计算候选轨道的无量纲归一化损失 J(x), 并在给定 ALM 上记录各约束的无量纲违约量。
        /// </summary>
        /// <param name="sol">候选解</param>
        /// <param name="config">求解配置</param>
        /// <param name="alm">增广拉格朗日乘子管理器 (可为 null, 此时使用一次性实例)</param>
        public static double ComputeLoss(MGASolution sol, MGAConfig config, MGAAugmentedLagrangian alm)
        {
            if (sol == null || double.IsNaN(sol.TotalDeltaVKms))
            {
                return 1.0e12; // 几何不可行解: 明确的极大值, 无参数
            }

            var lam = alm ?? ThreadLocalAlm.Value;

            // ---- 归一化基准: DeltaV_ref (物理量, 来自预算或霍曼基准) ----
            double dvRefKms = LossReferenceCache.Get(config);
            if (dvRefKms <= 1e-9) dvRefKms = 1.0;

            // ---- 主目标: 全任务总速度增量 / 参考预算  => O(1) ----
            double subjectDvKms = (config != null && config.NoInsertionBurn)
                ? (sol.LaunchDeltaVKms + sol.TotalDsmDeltaVKms + sol.TotalFlybyDeltaVKms)
                : sol.TotalDeltaVKms;
            double loss = subjectDvKms / dvRefKms;

            if (config == null) return loss;

            // =================================================================
            // 约束 1: 航段开普勒几何可行性 (离散型, 归一化尺度 = 1 次完全不可行)
            // =================================================================
            if (!sol.AllLegsFeasible)
            {
                loss += lam.Penalty(MGAConstraint.LegFeasibility, 1.0);
            }

            // =================================================================
            // 约束 2: 发射历元落在搜索窗口内 (归一化尺度 = 搜索窗口总跨度)
            // =================================================================
            double windowSpanDays = (config.LatestDepartureUT > config.EarliestDepartureUT)
                ? (config.LatestDepartureUT - config.EarliestDepartureUT) / MGADefaults.SecondsPerDay
                : 0.0;
            double epochScaleDays = windowSpanDays > 1e-6 ? windowSpanDays : 1.0;

            if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT)
            {
                double overDays = (config.EarliestDepartureUT - sol.DepartureUT) / MGADefaults.SecondsPerDay;
                loss += lam.Penalty(MGAConstraint.DepartureEpoch,
                    MGAPhysicalScales.NormalizedViolation(overDays, epochScaleDays));
            }
            if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT)
            {
                double overDays = (sol.DepartureUT - config.LatestDepartureUT) / MGADefaults.SecondsPerDay;
                loss += lam.Penalty(MGAConstraint.DepartureEpoch,
                    MGAPhysicalScales.NormalizedViolation(overDays, epochScaleDays));
            }

            if (sol.Legs != null && sol.Legs.Count > 0)
            {
                // =============================================================
                // 约束 3: L1 稀疏机动正则 —— 无量纲化, 尺度 = 参考预算, 按腿数摊分
                //   λ_L1_eff = DsmSparsityWeight / legCount
                //   数学依据: L1 范数在原点具有恒定下压次梯度, 使冗余假机动自发收敛到 0;
                //   按腿数摊分保证"每腿一条满预算 DSM"的惩罚与"主目标翻倍"等权。
                // =============================================================
                int legCount = Math.Max(1, sol.Legs.Count);
                double lambdaL1 = DsmSparsityWeight / legCount;
                loss += lambdaL1 * (sol.TotalDsmDeltaVKms / dvRefKms);

                // =============================================================
                // 约束 4: DSM 预算 (归一化尺度 = DSM 预算本身)
                // =============================================================
                double dsmBudgetKms = (config.MaxDsmDeltaVMs > 0) ? (config.MaxDsmDeltaVMs / 1000.0) : 0.0;
                if (dsmBudgetKms > 1e-9)
                {
                    double over = sol.TotalDsmDeltaVKms - dsmBudgetKms;
                    loss += lam.Penalty(MGAConstraint.DsmBudget,
                        MGAPhysicalScales.NormalizedViolation(over, dsmBudgetKms));
                }

                // =============================================================
                // 约束 5: 飞掠近心点安全半径 (归一化尺度 = 安全半径, 尺度无关)
                //   注: 飞掠发生在该航段的**到达**天体 (leg.ToBody), 而非出发天体。
                // =============================================================
                foreach (var leg in sol.Legs)
                {
                    double rp = leg.FlybyPeriapsisRadiusKm;
                    if (rp <= 0) continue;
                    double rSafe = MGADefaults.GetBodySafeRadiusKm(leg.ToBody);
                    if (rSafe <= 0) continue;
                    double fracViolation = (rSafe - rp) / rSafe;
                    loss += lam.Penalty(MGAConstraint.SafePeriapsis,
                        MGAPhysicalScales.NormalizedViolation(fracViolation, 1.0));
                }

                // =============================================================
                // 约束 9: 航时上限 (归一化尺度 = 航时上限本身)
                // =============================================================
                double maxTofDays = ResolveMaxTofDays(config, sol);
                if (maxTofDays > 0)
                {
                    double overDays = sol.TotalFlightDays - maxTofDays;
                    loss += lam.Penalty(MGAConstraint.MissionDuration,
                        MGAPhysicalScales.NormalizedViolation(overDays, maxTofDays));
                    // 航时决胜正则 (无量纲)
                    loss += DurationTieBreakerWeight * (sol.TotalFlightDays / maxTofDays);
                }
            }

            // =================================================================
            // 约束 6: 飞船总工质机动上限 (归一化尺度 = 预算本身)
            // =================================================================
            if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0)
            {
                double limitKms = config.VesselMaxDeltaVMs / 1000.0;
                double over = subjectDvKms - limitKms;
                loss += lam.Penalty(MGAConstraint.VesselBudget,
                    MGAPhysicalScales.NormalizedViolation(over, limitKms));
            }

            // =================================================================
            // 约束 7: 始发注入冲量上限 (归一化尺度 = 上限本身)
            // =================================================================
            if (config.LaunchDeltaVCeilingMs > 0)
            {
                double limitKms = config.LaunchDeltaVCeilingMs / 1000.0;
                double over = sol.LaunchDeltaVKms - limitKms;
                loss += lam.Penalty(MGAConstraint.LaunchCeiling,
                    MGAPhysicalScales.NormalizedViolation(over, limitKms));
            }

            // =================================================================
            // 约束 8: 纯无动力引力助推 —— 归一化尺度 = 参考预算的 1%
            //   (即"飞掠脉冲若超过总预算 1%, 视为发生了一次真实机动")
            //   取代原 PoweredFlybyPenaltyScale = 2500 的手调凸模。
            // =================================================================
            if (sol.TotalFlybyDeltaVKms > 1e-6)
            {
                double pulseScaleKms = 0.01 * dvRefKms;
                loss += lam.Penalty(MGAConstraint.PoweredFlyby,
                    MGAPhysicalScales.NormalizedViolation(sol.TotalFlybyDeltaVKms, pulseScaleKms));
            }

            // =================================================================
            // 约束 10: 发射倾角锁定 —— 归一化尺度 = 对准容差自身
            // =================================================================
            if (config.LockDepartureInclination && sol.Legs != null && sol.Legs.Count > 0)
            {
                double incErrDeg = Math.Abs(sol.Legs[0].EjectionInclinationDeg - config.DepartureInclinationDeg);
                double excessDeg = incErrDeg - InclinationAlignToleranceDeg;
                loss += lam.Penalty(MGAConstraint.InclinationLock,
                    MGAPhysicalScales.NormalizedViolation(excessDeg, InclinationAlignToleranceDeg));
            }

            // =================================================================
            // 约束 11: 双曲偏折几何可容纳性 —— 归一化尺度 = 参考预算
            //   物理: 借用解析判据 delta_max = 2 asin(1/(1+kappa)), 若所需的
            //   出射速度矢量变化超出该天体无动力偏折能力, 超出部分必须以有动力
            //   机动补偿, 其速度量级以参考预算归一化。
            // =================================================================
            if (sol.Legs != null && sol.Legs.Count > 1)
            {
                double maxPoweredKms = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0)
                    ? (config.VesselMaxDeltaVMs / 1000.0) : dvRefKms;

                for (int k = 0; k < sol.Legs.Count - 1; k++)
                {
                    var legIn = sol.Legs[k];
                    var legOut = sol.Legs[k + 1];
                    var planet = SolarSystemCatalog.GetPlanet(legIn.ToBody);
                    if (planet == null || planet.Mu <= 0) continue;

                    double rpMin = (planet.SafeRadiusKm > 0) ? planet.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(legIn.ToBody);
                    if (MGASymbolicPhysicsConstraints.IsFlybyTurnFeasible(
                            legIn.VInfArrivalKms, legOut.VInfDepartureKms, planet.Mu, rpMin,
                            maxPoweredKms, out double reqRad, out double maxRad))
                    {
                        continue;
                    }

                    double excess = Math.Max(0.0, reqRad - maxRad);
                    double vRef = Math.Min(legIn.VInfArrivalKms.magnitude, legOut.VInfDepartureKms.magnitude);
                    double turnDvExcess = 2.0 * vRef * Math.Sin(excess * 0.5) - maxPoweredKms;
                    loss += lam.Penalty(MGAConstraint.FlybyDeflection,
                        MGAPhysicalScales.NormalizedViolation(turnDvExcess, dvRefKms));
                }
            }

            return loss;
        }

        /// <summary>
        /// 解算该解的航时上限 (天)。优先级: 显式 MaxDurationDays -> 各腿上限求和 -> 物理标称尺度。
        /// </summary>
        private static double ResolveMaxTofDays(MGAConfig config, MGASolution sol)
        {
            if (config.EnableMaxDuration && config.MaxDurationDays > 0) return config.MaxDurationDays;

            if (config.MaxToFDays != null && config.MaxToFDays.Count > 0)
            {
                double sum = 0.0;
                foreach (var d in config.MaxToFDays) sum += d;
                if (sum > 0) return sum;
            }

            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return 0.0;

            // 未显式给定上限时, 由各航段的两体霍曼/共振特征尺度汇聚物理基准
            string central = config.CentralBody ?? "Sun";
            double muC = SolarSystemCatalog.GetCentralMu(central);
            if (muC <= 0) muC = SolarSystemCatalog.SunMu;

            double nominalDays = 0.0;
            foreach (var leg in sol.Legs)
            {
                var pF = SolarSystemCatalog.GetPlanet(leg.FromBody);
                var pT = SolarSystemCatalog.GetPlanet(leg.ToBody);
                double rF = (pF != null && pF.SMA > 0) ? pF.SMA : MGADefaults.AU_KM;
                double rT = (pT != null && pT.SMA > 0) ? pT.SMA : MGADefaults.AU_KM;

                if (!string.IsNullOrEmpty(leg.FromBody) &&
                    leg.FromBody.Equals(leg.ToBody, StringComparison.OrdinalIgnoreCase))
                {
                    // 同星共振段: 两个完整公转周期
                    nominalDays += 2.0 * MGAPhysicalScales.BodyPeriodSec(pF, muC) / MGADefaults.SecondsPerDay;
                }
                else
                {
                    nominalDays += MGAPhysicalScales.HohmannTofDays(muC, rF, rT) * 1.8;
                }
            }

            // 物理标称尺度的 2 倍作为松弛上限 (无量纲倍数, 非绝对天数)
            double scaled = nominalDays * 2.0 / 1.8;
            return Math.Max(1.0, scaled);
        }

        /// <summary>线程本地的无状态 ALM (用于 ComputeLoss 的兼容重载, 不参与跨代对偶上升)</summary>
        private static readonly System.Threading.ThreadLocal<MGAAugmentedLagrangian> ThreadLocalAlm =
            new System.Threading.ThreadLocal<MGAAugmentedLagrangian>(() => new MGAAugmentedLagrangian());

        // =====================================================================
        // 规范可行性判定 (取代 loss < 1e8 / < 1e7 一类魔法阈值)
        // =====================================================================

        /// <summary>损失是否落在"可完全可行"范围内 (无量纲规范上界)</summary>
        public static bool IsWithinFeasibleLoss(double loss)
        {
            return loss <= FeasibleLossCeiling;
        }

        /// <summary>损失是否严重不可行 (违背量已达基准预算量级, 可触发拓扑熔断)</summary>
        public static bool IsDeeplyInfeasible(double loss)
        {
            return loss >= DeepInfeasibilityLoss;
        }

        /// <summary>损失是否可接受进入选解/打磨阶段</summary>
        public static bool IsAcceptableForSelection(double loss)
        {
            return loss <= SelectionLossCeiling;
        }

        /// <summary>
        /// 直接物理可行性判定 (不依赖任何损失阈值)。用于取代所有基于损失量级的隐式判断。
        /// </summary>
        public static bool IsPhysicallyFeasible(MGASolution sol, MGAConfig config)
        {
            if (sol == null || !sol.AllLegsFeasible) return false;
            if (double.IsNaN(sol.TotalDeltaVKms)) return false;
            if (config == null) return true;

            if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs) return false;
            if (config.MaxDsmDeltaVMs > 0 && sol.TotalDsmDeltaVKms * 1000.0 > config.MaxDsmDeltaVMs) return false;
            if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0 && sol.TotalDeltaVKms * 1000.0 > config.VesselMaxDeltaVMs) return false;
            if (sol.TotalFlybyDeltaVKms > 1e-4) return false;
            return true;
        }

        // =====================================================================
        // 自适应模拟退火温标 (Parallel Tempering)
        // =====================================================================

        /// <summary>
        /// 计算当前代数与岛屿的局部退火温度。
        /// 【重构】原实现把初始温度硬编码为 t0 = 15.0 (km/s 量级的损失面), 在归一化损失下
        /// 该数值失去意义。现改为 **相对损失尺度** 表达:
        ///   T0 = InitialTemperatureInLossUnits × lossScale
        /// 其中 lossScale 由调用方从实测损失离散度 (MAD) 给出, 从而实现温标自标定。
        /// 归一化损失下 lossScale = 1 即可 (默认值), 无需任何经验调参。
        /// </summary>
        public static double GetIslandTemperature(int currentGen, int maxGens, int islandId, int totalIslands)
        {
            return GetIslandTemperature(currentGen, maxGens, islandId, totalIslands, 1.0);
        }

        /// <summary>
        /// 自标定版温度调度。
        /// </summary>
        /// <param name="lossScale">损失面特征尺度 (建议取初始种群的 MAD)。归一化损失下取 1。</param>
        public static double GetIslandTemperature(int currentGen, int maxGens, int islandId, int totalIslands, double lossScale)
        {
            if (maxGens <= 1) return 0.0;
            if (lossScale <= 1e-12 || double.IsNaN(lossScale)) lossScale = 1.0;

            double progress = Math.Max(0.0, Math.Min(1.0, (double)currentGen / maxGens));

            // 几何退火: T(t) = T0 * (Tmin/T0)^progress
            // T0 / Tmin 以"损失尺度"为单位给出 (无量纲): 初始可跨越约 1 个损失尺度的势垒,
            // 终态收敛到 1e-4 个损失尺度的精细分辨率。
            double t0 = InitialTemperatureInLossUnits * lossScale;
            double tMin = FinalTemperatureInLossUnits * lossScale;
            double baseT = t0 * Math.Pow(tMin / t0, progress);

            if (totalIslands <= 1) return baseT;

            // 异构岛屿温标层级 (Parallel Tempering): 高温探索 / 中温过渡 / 低温收敛
            switch (islandId % 3)
            {
                case 0: return Math.Max(baseT * HotIslandMultiplier, tMin);
                case 1: return baseT;
                default: return Math.Max(baseT * ColdIslandMultiplier, tMin * 0.1);
            }
        }

        /// <summary>初始退火温度, 以"损失尺度"为单位 (无量纲)。1.0 = 初始可跨越 1 个损失尺度。</summary>
        public const double InitialTemperatureInLossUnits = 1.0;

        /// <summary>终态退火温度, 以"损失尺度"为单位 (无量纲)。1e-4 = 精细收敛分辨率。</summary>
        public const double FinalTemperatureInLossUnits = 1.0e-4;

        /// <summary>高温探索岛相对温标倍率</summary>
        public const double HotIslandMultiplier = 3.5;

        /// <summary>低温收敛岛相对温标倍率</summary>
        public const double ColdIslandMultiplier = 0.15;

        /// <summary>
        /// 基于 Metropolis 统计力学准则评估是否接受变差的候选解。
        /// 若 candidateLoss &lt;= currentLoss: 100% 接受;
        /// 否则以概率 P = exp(-(L_cand - L_cur) / T) 接受。
        /// 此函数本身已无量纲 (ΔL 与 T 同尺度), 无需改动。
        /// </summary>
        public static bool MetropolisAccept(
            double currentLoss,
            double candidateLoss,
            double temperature,
            System.Random rand)
        {
            if (candidateLoss <= currentLoss) return true;
            if (temperature <= 1e-12 || double.IsNaN(temperature)) return false;

            double deltaLoss = candidateLoss - currentLoss;
            if (deltaLoss / temperature > 40.0) return false;

            double prob = Math.Exp(-deltaLoss / temperature);
            return (rand.NextDouble() < prob);
        }

        // =====================================================================
        // 机器学习代理损失 (取代原 ComputeFastSurrogateLoss 的未训练网络)
        // =====================================================================

        /// <summary>
        /// 代理模型快速预估损失 (用于大规模初筛, 单次前向 &lt; 1 us)。
        /// 【重构】原实现调用的是一个权重由 sin() 手工生成的**从未训练**的网络。
        /// 现改为: 仅当代理网络已被真实数据训练过 (IsTrained) 才启用;
        /// 否则退化为**解析霍曼下界**, 给出一个有物理意义且永远不会误导的粗估。
        /// </summary>
        public static double ComputeSurrogateLoss(
            double r1Km, double r2Km, double phaseAngleDeg,
            double tofDays, double nomHohmannTofDays, double incDiffDeg)
        {
            var net = MGAPhysicsSurrogateNetwork.Default;

            if (net.IsTrained)
            {
                var features = MGAPhysicsSurrogateNetwork.ExtractTransferFeatures(
                    r1Km, r2Km, phaseAngleDeg, tofDays, nomHohmannTofDays, incDiffDeg);

                net.Forward(features, out double pFeasible, out double estDvKms);
                if (pFeasible <= 0.0) return double.MaxValue;

                // 不可行概率以无量纲倒数形式进入损失, 避免任何绝对惩罚常数
                return estDvKms / Math.Max(1e-3, pFeasible);
            }

            // ---- 未训练时: 解析霍曼下界 + 相位失配惩罚 (纯物理, 无网络) ----
            double tofRatio = (nomHohmannTofDays > 1e-6) ? (tofDays / nomHohmannTofDays) : 1.0;
            if (tofRatio <= 1e-6) tofRatio = 1.0;
            // 开普勒时间尺度: T ~ a^(3/2), 故对给定 ToF 的等效半长轴比 = tofRatio^(2/3)
            double aRatio = Math.Pow(tofRatio, 2.0 / 3.0);
            double hohmannBound = aRatio + 1.0 / aRatio; // 最小能量转移的形状因子, 无量纲

            // 相位失配 (deg -> 无量纲, 以 180 deg 为尺度)
            double phaseMismatch = Math.Abs(((phaseAngleDeg % 360.0) + 360.0) % 360.0 - 180.0) / 180.0;
            return hohmannBound + phaseMismatch;
        }
    }
}
