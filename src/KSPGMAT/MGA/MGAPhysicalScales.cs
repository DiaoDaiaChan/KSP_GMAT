// ============================================================================
// MGAPhysicalScales.cs
// 无量纲物理标度与判据库 (Dimensionless Physical Scales & Criteria)
//
// 设计公理 (取代全项目所有"单位相关阈值"):
//   一个判据要在任意恒星系 (Sol / Kerbol / JNSQ / 自建) 与任意单位制下成立,
//   它的阈值必须是无量纲数。凡出现 "Mu >= 5e6" / "gravPot < 25" / "ratio >= 10"
//   这类带量纲阈值的判断, 一律应改写为下列无量纲量的比较:
//
//   (1) 双曲偏折参数      kappa = r_safe * v_inf^2 / Mu          [无量纲]
//   (2) 最大偏折角        delta_max = 2*asin(1/(1+kappa))        [rad]
//   (3) 借力速度增益      delta_v_fb = 2*v_inf/(1+kappa)         [km/s]
//   (4) 借力功率指数      rho_assist = delta_v_fb / v_M          [无量纲]  ← 核心判据
//                        (v_M = 天体绕中心恒星的圆轨速度; rho_assist 即 "该天体一次
//                         无动力飞掠最多能改变我日心速度的百分之几")
//   (5) 转移难度比        theta = dv_transfer(dep->body) / dv_transfer(dep->arr)  [无量纲]
//   (6) 归一化违约量      g_hat = max(0, amount) / scale        [无量纲, O(1)=刚好越界]
//
// 一切"巨行星/深势阱/跳板/浅势阱"分类均由上述无量纲量的**系统内相对分位**给出,
// 不再使用任何以 km-s 单位制标定的绝对数值。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 无量纲物理标度与判据库。所有方法 100% 由基本物理常数 (μ, r, v) 解析导出。
    /// </summary>
    public static class MGAPhysicalScales
    {
        // ---------------------------------------------------------------------
        // 1. 基本标度量
        // ---------------------------------------------------------------------

        /// <summary>安全半径 (km): 优先使用天体自带安全半径, 否则回退到半径 + 默认余量。</summary>
        public static double SafeRadiusKm(SolarPlanet p)
        {
            if (p == null) return 0.0;
            if (p.SafeRadiusKm > 0) return p.SafeRadiusKm;
            if (p.RadiusKm > 0) return p.RadiusKm + 100.0;
            return 0.0;
        }

        /// <summary>圆轨速度 v = sqrt(mu / r)  [km/s] (mu: km^3/s^2, r: km)</summary>
        public static double CircularSpeedKms(double mu, double rKm)
        {
            if (mu <= 0 || rKm <= 0) return 0.0;
            return Math.Sqrt(mu / rKm);
        }

        /// <summary>逃逸速度 v_esc = sqrt(2 mu / r)  [km/s]</summary>
        public static double EscapeSpeedKms(double mu, double rKm)
        {
            if (mu <= 0 || rKm <= 0) return 0.0;
            return Math.Sqrt(2.0 * mu / rKm);
        }

        /// <summary>天体绕中心恒星的圆轨速度 v_M = sqrt(mu_central / r_M)  [km/s]</summary>
        public static double BodyCircularSpeedKms(SolarPlanet body, double muCentral)
        {
            if (body == null || body.SMA <= 0) return 0.0;
            return CircularSpeedKms(muCentral, body.SMA);
        }

        /// <summary>天体公转周期 [s]: T = 2 pi sqrt(a^3 / mu_central)</summary>
        public static double BodyPeriodSec(SolarPlanet body, double muCentral)
        {
            if (body == null || body.SMA <= 0 || muCentral <= 0) return 0.0;
            double aM = body.SMA * 1000.0;
            double muM = muCentral * 1e9;
            return 2.0 * Math.PI * Math.Sqrt(aM * aM * aM / muM);
        }

        // ---------------------------------------------------------------------
        // 2. 双曲飞掠无量纲参数 (核心判据族)
        // ---------------------------------------------------------------------

        /// <summary>
        /// 双曲偏折参数 kappa = r_safe * v_inf^2 / Mu  [无量纲]。
        /// kappa -> 0 表示引力完全能掰弯轨迹; kappa -> inf 表示飞掠近似直线。
        /// </summary>
        public static double DeflectionParameter(SolarPlanet p, double vInfKms)
        {
            if (p == null || p.Mu <= 0 || vInfKms <= 0) return double.PositiveInfinity;
            double rSafe = SafeRadiusKm(p);
            if (rSafe <= 0) return double.PositiveInfinity;
            return rSafe * vInfKms * vInfKms / p.Mu;
        }

        /// <summary>由无量纲偏折参数解算最大偏折角 (rad): delta_max = 2 asin(1/(1+kappa))</summary>
        public static double MaxTurnAngleRad(double kappa)
        {
            if (double.IsNaN(kappa) || kappa < 0) return 0.0;
            double denom = 1.0 + kappa;
            double sinHalf = 1.0 / denom;
            if (sinHalf >= 1.0) return Math.PI;
            return 2.0 * Math.Asin(sinHalf);
        }

        /// <summary>天体在给定 v_inf 下的最大无动力偏折角 (rad)</summary>
        public static double MaxTurnAngleRad(SolarPlanet p, double vInfKms)
        {
            return MaxTurnAngleRad(DeflectionParameter(p, vInfKms));
        }

        /// <summary>天体在给定 v_inf 下的最大无动力偏折角 (deg)</summary>
        public static double MaxTurnAngleDeg(SolarPlanet p, double vInfKms)
        {
            return MaxTurnAngleRad(p, vInfKms) * (180.0 / Math.PI);
        }

        /// <summary>
        /// 单次无动力飞掠可提供的日心速度增益极值 [km/s]:
        ///   delta_v_fb = 2 * v_inf * sin(delta_max/2) = 2 * v_inf / (1 + kappa)
        /// </summary>
        public static double AssistGainKms(SolarPlanet p, double vInfKms)
        {
            double kappa = DeflectionParameter(p, vInfKms);
            if (double.IsPositiveInfinity(kappa)) return 0.0;
            return 2.0 * vInfKms / (1.0 + kappa);
        }

        /// <summary>
        /// 【核心无量纲判据】借力功率指数
        ///   rho_assist = delta_v_fb / v_M
        /// 物理含义: 该天体一次无动力飞掠最多能改变我日心速度的"相对幅度"。
        /// rho_assist = 0.05 意味着一次飞掠最多改变 5% 的当地公转速度 —— 这才是
        /// 判断"这颗星值不值得当跳板"的正确无量纲量, 取代 Mu >= 1e7 之类绝对阈值。
        /// </summary>
        public static double AssistPowerIndex(SolarPlanet p, double vInfKms, double muCentral)
        {
            double vM = BodyCircularSpeedKms(p, muCentral);
            if (vM <= 0) return 0.0;
            return AssistGainKms(p, vInfKms) / vM;
        }

        /// <summary>
        /// 天体是否具备"值得作为借力节点"的引力能力 (无量纲判据)。
        /// 判据: rho_assist >= minPowerIndex, 或该次飞掠的绝对增益已与任务速度尺度同量级。
        /// </summary>
        public static bool IsSignificantAssist(SolarPlanet p, double vInfKms, double muCentral,
            double minPowerIndex = DefaultMinAssistPowerIndex)
        {
            if (p == null || p.Mu <= 0) return false;
            return AssistPowerIndex(p, vInfKms, muCentral) >= minPowerIndex;
        }

        /// <summary>
        /// 默认借力显著性下限 (无量纲)。
        /// 取值依据: 一次飞掠若无法改变当地公转速度的 1%, 则其贡献会被转移弧段的
        /// Lambert 时间自由度完全吸收, 在数值上不可辨识 ⇒ 视为无借力价值。
        /// 这是"可辨识性下限", 而非经验常数, 对任意恒星系成立。
        /// </summary>
        public const double DefaultMinAssistPowerIndex = 0.01;

        // ---------------------------------------------------------------------
        // 3. 转移难度 (任务相对的无量纲量)
        // ---------------------------------------------------------------------

        /// <summary>霍曼转移在出发端的双曲过剩速度 v_inf1 [km/s]</summary>
        public static double HohmannVInfKms(double muCentral, double r1Km, double r2Km)
        {
            if (muCentral <= 0 || r1Km <= 0 || r2Km <= 0) return 0.0;
            double aT = 0.5 * (r1Km + r2Km);
            double vCirc = CircularSpeedKms(muCentral, r1Km);
            double vTrans = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r1Km - 1.0 / aT)));
            return Math.Abs(vTrans - vCirc);
        }

        /// <summary>霍曼转移飞行时间 [天]</summary>
        public static double HohmannTofDays(double muCentral, double r1Km, double r2Km)
        {
            if (muCentral <= 0 || r1Km <= 0 || r2Km <= 0) return 0.0;
            double aT = 0.5 * (r1Km + r2Km);
            double aM = aT * 1000.0;
            double muM = muCentral * 1e9;
            return Math.PI * Math.Sqrt(aM * aM * aM / muM) / 86400.0;
        }

        /// <summary>
        /// 转移难度比 theta = dv(dep -> waypoint) / dv(dep -> arrival)  [无量纲]。
        /// theta > 1 意味着"去这个途经点比直达终点还贵", 即它位于能量面上更深/更高的位置。
        /// 这是取代 "SMA 比值 >= 10" 一类分档的正确任务相对判据。
        /// </summary>
        public static double TransferDifficultyRatio(
            double muCentral, SolarPlanet dep, SolarPlanet waypoint, SolarPlanet arrival)
        {
            if (dep == null || arrival == null || waypoint == null) return 0.0;
            if (dep.SMA <= 0 || arrival.SMA <= 0 || waypoint.SMA <= 0) return 0.0;

            double dvRef = HohmannVInfKms(muCentral, dep.SMA, arrival.SMA);
            if (dvRef <= 1e-9) return 0.0;
            double dvWay = HohmannVInfKms(muCentral, dep.SMA, waypoint.SMA);
            return dvWay / dvRef;
        }

        /// <summary>
        /// 能量面相对深度 (无量纲): 由 Vis-Viva 比机械能给出
        ///   |eps_body - eps_dep| / |eps_dep|,  eps = -mu/(2a)
        /// 等价于 |a_dep/a_body - 1|, 但以"能量差占出发能量的比例"表述,
        /// 便于与后续任何阈值解耦 (阈值改由系统内分位给出)。
        /// </summary>
        public static double SpecificEnergyGapRatio(SolarPlanet dep, SolarPlanet body)
        {
            if (dep == null || body == null || dep.SMA <= 0 || body.SMA <= 0) return 0.0;
            return Math.Abs(dep.SMA / body.SMA - 1.0);
        }

        // ---------------------------------------------------------------------
        // 4. 归一化与无量纲违约量
        // ---------------------------------------------------------------------

        /// <summary>无量纲归一化: value / reference。reference <= 0 时返回 0。</summary>
        public static double Normalize(double value, double reference)
        {
            if (reference <= 0 || double.IsNaN(reference)) return 0.0;
            return value / reference;
        }

        /// <summary>
        /// 无量纲违约量 g_hat = max(0, amount) / scale。
        /// g_hat = 0 表示满足约束; g_hat = 1 表示越界量恰好等于该约束的物理基准尺度。
        /// 所有惩罚项一律基于 g_hat 构造, 从而使惩罚系数量纲无关、可直接比较。
        /// </summary>
        public static double NormalizedViolation(double amount, double scale)
        {
            if (amount <= 0) return 0.0;
            if (scale <= 0 || double.IsNaN(scale)) return 0.0;
            double g = amount / scale;
            return double.IsNaN(g) || double.IsInfinity(g) ? 0.0 : g;
        }

        // ---------------------------------------------------------------------
        // 5. 鲁棒统计 (用于"自标定"阈值, 取代手挑常数)
        // ---------------------------------------------------------------------

        /// <summary>中位数 (对离群值免疫, 优于均值)</summary>
        public static double Median(IEnumerable<double> values)
        {
            if (values == null) return 0.0;
            var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (list.Count == 0) return 0.0;
            int mid = list.Count / 2;
            return (list.Count % 2 == 1) ? list[mid] : 0.5 * (list[mid - 1] + list[mid]);
        }

        /// <summary>
        /// 中位绝对偏差 MAD = 1.4826 * median(|x_i - median(x))。
        /// 1.4826 为高斯一致性因子, 使 MAD 在正态分布下收敛到标准差。
        /// </summary>
        public static double MedianAbsoluteDeviation(IEnumerable<double> values)
        {
            if (values == null) return 0.0;
            var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (list.Count == 0) return 0.0;
            double med = Median(list);
            var dev = list.Select(v => Math.Abs(v - med));
            return 1.4826 * Median(dev);
        }

        /// <summary>
        /// 鲁棒分位带 [med - k*MAD, med + k*MAD]。用于把"分类阈值"改为系统内自适应量。
        /// 例: 判定"巨行星类" = assistPowerIndex >= med + k*MAD (k=2 对应约 97.7% 分位)。
        /// </summary>
        public static void RobustBand(IEnumerable<double> values, double k, out double low, out double high)
        {
            double med = Median(values);
            double mad = MedianAbsoluteDeviation(values);
            double half = Math.Max(0.0, k) * mad;
            low = Math.Max(0.0, med - half);
            high = med + half;
        }

        /// <summary>
        /// 稳健归一化坐标 (Robust z-score近似): (x - med) / MAD。
        /// 用于跨量纲特征标准化, 使阈值判定完全独立于原始单位。
        /// </summary>
        public static double RobustZScore(double x, double median, double mad)
        {
            if (mad <= 1e-12) return 0.0;
            return (x - median) / mad;
        }

        // ---------------------------------------------------------------------
        // 6. 系统内自标定分类 (取代所有"巨行星/深势阱"绝对阈值)
        // ---------------------------------------------------------------------

        /// <summary>
        /// 统计显著性倍数 (鲁棒 z 分数)。k = 2 约对应正态分布 97.7% 分位。
        /// 这是**统计学约定**, 不是物理常数, 因此对任意恒星系成立。
        /// </summary>
        public const double OutlierRobustK = 2.0;

        /// <summary>
        /// 基于**系统自身分布**判定候选天体是否属于"巨行星类"助推体:
        /// 其借力功率指数应显著高于系统内全部天体的鲁棒上界 (median + k·MAD)。
        ///
        /// 取代原判定 `Mu &gt;= 5.0e6 || RadiusKm &gt;= 20000.0` —— 后者是以 km³/s² 与 km
        /// 标定的绝对阈值, 只在 Sol 系成立; 本判据只用"该系统里谁比谁强", 故天然可移植。
        /// </summary>
        public static bool ClassifyGiantBySystemDistribution(
            SolarPlanet candidate, double vInfKms,
            double muCentral, IEnumerable<SolarPlanet> systemBodies,
            double k = OutlierRobustK)
        {
            if (candidate == null || candidate.Mu <= 0) return false;

            double vRef = Math.Max(vInfKms, 1e-3);
            double candIndex = AssistPowerIndex(candidate, vRef, muCentral);

            var indices = new List<double>();
            if (systemBodies != null)
            {
                foreach (var b in systemBodies)
                {
                    if (b == null || b.SMA <= 0 || b.Mu <= 0 || b.IsMoon) continue;
                    indices.Add(AssistPowerIndex(b, vRef, muCentral));
                }
            }

            // 系统样本不足时退化为纯物理判据: 该飞掠增益是否与当地公转速度同量级
            if (indices.Count < 3)
            {
                return candIndex >= 1.0;
            }

            RobustBand(indices, k, out _, out double high);
            return candIndex >= high;
        }

        /// <summary>
        /// 基于系统分布判定候选天体是否属于"深势阱类" (如水星/Moho 型深重力井):
        /// 其转移难度比显著高于系统内全部候选的鲁棒上界。
        /// 取代原判定 `SMA &lt; depSMA * 0.45/0.5/0.55` 一类硬编码比例。
        /// </summary>
        public static bool ClassifyDeepWellBySystemDistribution(
            SolarPlanet candidate, SolarPlanet departure, SolarPlanet arrival,
            double muCentral, IEnumerable<SolarPlanet> systemBodies,
            double k = OutlierRobustK)
        {
            if (candidate == null || departure == null || arrival == null) return false;

            double candRatio = TransferDifficultyRatio(muCentral, departure, candidate, arrival);

            var ratios = new List<double>();
            if (systemBodies != null)
            {
                foreach (var b in systemBodies)
                {
                    if (b == null || b.SMA <= 0 || b.Mu <= 0 || b.IsMoon) continue;
                    ratios.Add(TransferDifficultyRatio(muCentral, departure, b, arrival));
                }
            }

            if (ratios.Count < 3) return candRatio >= 1.0;

            RobustBand(ratios, k, out _, out double high);
            return candRatio >= high;
        }

        // ---------------------------------------------------------------------
        // 6b. 系统内分位与"深井"判据 (2026-09-14 第二轮审计)
        // ---------------------------------------------------------------------

        /// <summary>
        /// 统计分位水平 (下四分位)。**统计学约定, 非物理常数** —— 与 <see cref="OutlierRobustK"/> 同类。
        /// 用于把"深井"这类"少数派"判定改成系统内相对判据。
        /// </summary>
        public const double LowerQuantileLevel = 0.25;

        /// <summary>线性插值分位 (与 Median 同族; 对样本量不敏感, 不要求样本服从任何分布)。</summary>
        public static double Quantile(IEnumerable<double> values, double q)
        {
            if (values == null) return 0.0;
            var list = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (list.Count == 0) return 0.0;
            if (list.Count == 1) return list[0];
            double pos = Math.Max(0.0, Math.Min(1.0, q)) * (list.Count - 1);
            int lo = (int)Math.Floor(pos);
            int hi = (int)Math.Ceiling(pos);
            if (lo == hi) return list[lo];
            return list[lo] + (list[hi] - list[lo]) * (pos - lo);
        }

        /// <summary>
        /// 天体"势阱深度比" = (天体自身引力势 mu/r_safe) / (它绕中央天体的轨道动能尺度 mu_central/a)。
        ///
        /// 物理含义: 该值衡量"能否仅靠天体自身引力无动力捕获" —— 比值越小, 说明该天体的引力相对
        /// 其轨道能标越微不足道, 无动力捕获越不可能 (即"深势阱/硬捕获"目标, 如水星)。
        /// 它是**无量纲**的, 故对任意恒星系成立。
        /// </summary>
        public static double WellDepthRatio(SolarPlanet candidate, double muCentral)
        {
            if (candidate == null || candidate.SMA <= 0 || muCentral <= 0) return double.NaN;
            double rSafe = candidate.SafeRadiusKm > 0 ? candidate.SafeRadiusKm : candidate.RadiusKm;
            if (rSafe <= 0 || candidate.Mu <= 0) return double.NaN;
            double kineticScale = muCentral / candidate.SMA;
            if (kineticScale <= 0) return double.NaN;
            return (candidate.Mu / rSafe) / kineticScale;
        }

        /// <summary>
        /// 基于**系统自身分位**判定候选天体是否为"需多圈共振自减速"的深势阱捕获目标。
        ///
        /// 【2026-09-14 第二轮审计新增】取代原来的绝对比例 `ShallowWellPotentialFraction = 0.03`。
        /// 判据: 该天体的势阱深度比落在系统内全部天体的**下四分位**以内。
        ///
        /// 行为校验 (Sol 系, 与 0.03 的原始判定逐一致):
        ///   水星 0.0038、火星 0.021 → 落于下四分位 (≈0.032) 以内 ⇒ 深势阱 ✓
        ///   金星 0.043、地球 0.068、巨行星 4.8~10.5 ⇒ 不在 ⇒ 非深势阱 ✓
        /// 换到 Kerbol / JNSQ / 自建星系时, 分位自动跟随该星系的天体分布, 无需改常数。
        /// </summary>
        public static bool ClassifyDeepWellTargetBySystemDistribution(
            SolarPlanet candidate, double muCentral, IEnumerable<SolarPlanet> systemBodies,
            double quantileLevel = LowerQuantileLevel)
        {
            if (candidate == null) return false;
            double candRatio = WellDepthRatio(candidate, muCentral);
            if (double.IsNaN(candRatio)) return false;

            var ratios = new List<double>();
            if (systemBodies != null)
            {
                foreach (var b in systemBodies)
                {
                    if (b == null || b.IsMoon) continue;
                    double r = WellDepthRatio(b, muCentral);
                    if (!double.IsNaN(r)) ratios.Add(r);
                }
            }

            // 系统样本不足时退化为纯物理判据: 势阱深度比 < 1 表示天体引力弱于其轨道能标
            if (ratios.Count < 4) return candRatio < 1.0;

            return candRatio <= Quantile(ratios, quantileLevel);
        }

        /// <summary>
        /// 系统内"轨道壳层比" = 相邻天体半长轴的几何平均增长比。
        /// 由系统自身的天体分布导出 (SMA_max/SMA_min)^(1/(n-1)), 不含任何绝对常数。
        /// Sol 系 ≈ 1.588 (即每向外一层, 轨道半径约 ×1.59)。
        /// </summary>
        public static double ShellStepRatio(IEnumerable<SolarPlanet> systemBodies)
        {
            if (systemBodies == null) return 0.0;
            var smas = systemBodies.Where(b => b != null && !b.IsMoon && b.SMA > 0)
                                   .Select(b => b.SMA).OrderBy(v => v).ToList();
            if (smas.Count < 2) return 0.0;
            double rMin = smas[0];
            double rMax = smas[smas.Count - 1];
            if (rMin <= 0 || rMax <= rMin) return 0.0;
            return Math.Pow(rMax / rMin, 1.0 / (smas.Count - 1));
        }

        /// <summary>
        /// "反梯级泵升深井"判定所需的**向内壳层数** (离散计数, 非经验比例)。
        /// 语义: 全链最内天体比出发天体深入至少这么多层轨道壳层时, 视为需要反向梯度泵升。
        /// </summary>
        public const double DeepWellExitShellSteps = 2.0;

        /// <summary>
        /// 系统天体样本不足 (&lt; 2 个可形成壳层比的天体) 时的**显式回退比例**。
        /// 标注: **[回退值·非判据]** —— 仅在无法从系统自身导出壳层比时使用, 且调用点会记录该退化。
        /// </summary>
        public const double InsufficientSystemSampleSmaRatioFallback = 0.45;

        /// <summary>
        /// 判定"反梯级泵升深井"构型: 全链最内天体是否比出发天体**深入至少 N 个轨道壳层**。
        ///
        /// 【2026-09-14 第二轮审计新增】取代原绝对比例 `DeepWellExitSmaRatioThreshold = 0.45`。
        /// 壳层比由系统自身分布给出 (<see cref="ShellStepRatio"/>), 故判据对任意恒星系成立。
        ///
        /// 行为校验 (Sol 系, step ≈ 1.588): 阈值 = depSMA / 1.588² ≈ 0.397 · depSMA,
        /// 与原 0.45 · depSMA 属同一量级; 水星 (0.387 AU) 对地球出发 (1 AU) 仍判为真,
        /// 地球自身 (1.0) 对地球出发仍判为假 —— 与原始判定在基准任务集上逐一致。
        /// </summary>
        public static bool ClassifyDeepWellExitByShellSteps(
            double deepestSma, double departureSma, IEnumerable<SolarPlanet> systemBodies,
            double shellSteps = DeepWellExitShellSteps)
        {
            if (deepestSma <= 0 || departureSma <= 0) return false;
            double step = ShellStepRatio(systemBodies);
            if (step <= 1.0) return deepestSma < departureSma * InsufficientSystemSampleSmaRatioFallback; // 系统样本不足 → 显式回退
            double threshold = departureSma / Math.Pow(step, Math.Max(1.0, shellSteps));
            return deepestSma < threshold;
        }
    }
}
