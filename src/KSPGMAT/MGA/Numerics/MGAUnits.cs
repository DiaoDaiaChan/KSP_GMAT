// ============================================================================
// MGAUnits.cs  ——  MGA 全域单位换算与天体力学公式单一真值源 (SSOT)
//
// 【为什么需要这个文件】
// 审计发现源码中散落着大量"看起来像魔法数字、实际是单位换算或数学定义"的字面量：
//   86400.0 / 1000.0 / 365.25 / Math.PI / 180.0 / Math.Pow(a, 3.0) / (r1 + r2) * 0.5 ...
// 这类字面量不是经验假定，但混在业务公式里会造成两个问题：
//   (1) 无法区分"这是单位换算"还是"这是拍脑袋的系数"，审计工具会把它们误判为 P0 高危；
//   (2) 同一换算在多处手写，一旦量纲理解不一致（km/m、s/day、deg/rad）就会静默出错。
//
// 因此本文件把单位换算与天体力学基本公式集中为唯一真值源：
//   - 任何 km↔m、s↔day、deg↔rad、AU↔km 的换算都必须调用本文件；
//   - 任何 vis-viva / 开普勒三定律 / 霍曼半长轴 / 双曲偏折角 公式都必须调用本文件；
//   - 业务代码中不得再出现裸写的 86400.0 / 1000.0 / 365.25 / (r1+r2)*0.5。
//
// 【开发规范】详见 docs/DEVELOPMENT_STANDARDS.md §1「单位与公式铁律」。
//
// 【命名约定】(与既有代码风格保持一致)
//   - 换算函数:  动词 + 目标单位后缀, 如 KmToM / MToKm / SecToDay / DayToSec
//   - 参数化公式: 纯几何/力学名, 如 KeplerThirdLaw / HohmannSemiMajorAxis / VisVivaSpeed
//   例: MGAUnits.KmToM(451525)  —— 把 451525 km 公里化为米
//       MGAUnits.KeplerThirdLaw(aKm, mu) —— 开普勒第三定律周期
//
// 【量纲后缀表】(全文件严格遵守)
//   Km  公里        M   米          Au  天文单位
//   Ms  m/s         Kms km/s        KmSqKm^3/s^2 (引力常数)
//   Sec 秒          Day 天          Yr  儒略年
//   Deg 度          Rad 弧度
// ============================================================================

using System;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// MGA 全域单位换算与天体力学公式单一真值源。
    /// <para>
    /// <b>铁律</b>：本工程任何源文件中，禁止裸写下列字面量用于换算或物理公式 ——
    /// <c>86400.0</c> / <c>1000.0</c>(km↔m) / <c>365.25</c> / <c>149597870.7</c> /
    /// <c>Math.PI / 180.0</c> / <c>180.0 / Math.PI</c> /
    /// <c>(r1 + r2) * 0.5</c> / <c>2.0 / r - 1.0 / a</c> / <c>Math.Pow(a, 3.0)</c>。
    /// 一律改为调用本类具名成员。
    /// </para>
    /// </summary>
    public static class MGAUnits
    {
        // ====================================================================
        // A. 基本换算因子常量 (供常量表达式使用; 运行时代码优先用下方函数)
        // ====================================================================

        /// <summary>每分钟秒数。</summary>
        public const double SecondsPerMinute = 60.0;

        /// <summary>每小时秒数。</summary>
        public const double SecondsPerHour = 3600.0;

        /// <summary>每日秒数 (SI 定义, 精确值)。</summary>
        public const double SecondsPerDay = MGADefaults.SecondsPerDay;

        /// <summary>每千米米数 (SI 定义, 精确值)。</summary>
        public const double MetersPerKm = 1000.0;

        /// <summary>
        /// 儒略年天数 (国际天文学联合会 IAU 定义, 精确值 = 365.25 日)。
        /// 注意: 这是**定义值**而非近似值, 不可改写为 365.2422 (回归年) —— 二者物理含义不同。
        /// </summary>
        public const double DaysPerJulianYear = 365.25;

        /// <summary>天文单位长度 (IAU 2012 定义, 单位 km)。</summary>
        public const double KmPerAu = MGADefaults.AU_KM;

        /// <summary>度 → 弧度 因子。</summary>
        public const double RadPerDeg = Math.PI / 180.0;

        /// <summary>弧度 → 度 因子。</summary>
        public const double DegPerRad = 180.0 / Math.PI;

        /// <summary>一个完整圆的角度 (度)。</summary>
        public const double DegPerFullCircle = 360.0;

        /// <summary>半圆角度 (度) —— 方向反转 / 对冲几何的基准。</summary>
        public const double DegPerHalfCircle = 180.0;

        // ====================================================================
        // B. 长度换算 (长度量纲族: Km / M / Au)
        // ====================================================================

        /// <summary>公里 → 米。例: <c>KmToM(451525)</c> = 451525000。</summary>
        public static double KmToM(double km) => km * MetersPerKm;

        /// <summary>米 → 公里。</summary>
        public static double MToKm(double m) => m / MetersPerKm;

        /// <summary>天文单位 → 公里。</summary>
        public static double AuToKm(double au) => au * KmPerAu;

        /// <summary>公里 → 天文单位。</summary>
        public static double KmToAu(double km) => km / KmPerAu;

        // ====================================================================
        // C. 时间换算 (时间量纲族: Sec / Day / Yr)
        // ====================================================================

        /// <summary>秒 → 日。</summary>
        public static double SecToDay(double sec) => sec / SecondsPerDay;

        /// <summary>日 → 秒。</summary>
        public static double DayToSec(double day) => day * SecondsPerDay;

        /// <summary>日 → 儒略年。</summary>
        public static double DayToJulianYear(double day) => day / DaysPerJulianYear;

        /// <summary>儒略年 → 日。</summary>
        public static double JulianYearToDay(double yr) => yr * DaysPerJulianYear;

        /// <summary>秒 → 儒略年。</summary>
        public static double SecToJulianYear(double sec) => SecToDay(sec) / DaysPerJulianYear;

        /// <summary>儒略年 → 秒。</summary>
        public static double JulianYearToSec(double yr) => DayToSec(JulianYearToDay(yr));

        // ====================================================================
        // D. 速度换算 (速度量纲族: Ms / Kms)
        // ====================================================================

        /// <summary>公里/秒 → 米/秒。例: <c>KmsToMs(3.21)</c> = 3210。</summary>
        public static double KmsToMs(double kms) => kms * MetersPerKm;

        /// <summary>米/秒 → 公里/秒。</summary>
        public static double MsToKms(double ms) => ms / MetersPerKm;

        // ====================================================================
        // E. 角度换算 (角度量纲族: Deg / Rad)
        // ====================================================================

        /// <summary>度 → 弧度。</summary>
        public static double DegToRad(double deg) => deg * RadPerDeg;

        /// <summary>弧度 → 度。</summary>
        public static double RadToDeg(double rad) => rad * DegPerRad;

        /// <summary>把任意角度规范化到 [0, 360) 区间。</summary>
        public static double NormalizeDeg360(double deg)
        {
            double r = deg % DegPerFullCircle;
            return r < 0.0 ? r + DegPerFullCircle : r;
        }

        /// <summary>
        /// 两个角度之间的最小夹角 (∈ [0, 180])。
        /// 取代手写的 <c>Math.Abs(a - b); if (x > 180) x = 360 - x;</c> 模式。
        /// </summary>
        public static double AngularSeparationDeg(double aDeg, double bDeg)
        {
            double d = Math.Abs(NormalizeDeg360(aDeg) - NormalizeDeg360(bDeg));
            return d > DegPerHalfCircle ? DegPerFullCircle - d : d;
        }

        // ====================================================================
        // F. 天体力学基本公式 (Kepler / Vis-Viva / Hohmann)
        //
        // 说明: 本类只提供**常量表达式与单点公式**。凡是需要委托原生动力学内核的
        // 完整轨道传播, 必须走 PykepNative / PykepBridge (基线 §7「零托管数学」);
        // 此处仅提供"估计器/剪枝器"所需的解析标量公式, 与原实现数值严格等价。
        // ====================================================================

        /// <summary>
        /// 开普勒第三定律 —— 由半长轴与引力常数求轨道周期 (秒)。
        /// <para><c>T = 2π · sqrt(a³ / μ)</c></para>
        /// <para>量纲: aKm [km], mu [km³/s²] → 返回 [s]。</para>
        /// </summary>
        public static double KeplerThirdLawSec(double aKm, double mu)
        {
            if (aKm <= 0.0 || mu <= 0.0) return 0.0;
            // v v v 原实现: (2.0 * Math.PI * Math.Sqrt(Math.Pow(aKm, 3.0) / mu)) / 86400.0
            return 2.0 * Math.PI * Math.Sqrt(aKm * aKm * aKm / mu);
        }

        /// <summary>
        /// 开普勒第三定律 (日为单位)。量纲: aKm [km], mu [km³/s²] → 返回 [day]。
        /// </summary>
        public static double KeplerThirdLawDay(double aKm, double mu)
            => SecToDay(KeplerThirdLawSec(aKm, mu));

        /// <summary>
        /// 开普勒第三定律的**比例式** —— 两个轨道的周期比等于半长轴比的 3/2 次方。
        /// <para><c>T2/T1 = (a2/a1)^(3/2)</c></para>
        /// 取代手写的 <c>Math.Sqrt(Math.Pow(aTrans / aActual, 3.0))</c>。
        /// </summary>
        public static double KeplerPeriodRatio(double aRef, double aOther)
        {
            if (aRef <= 0.0 || aOther <= 0.0) return 1.0;
            double ratio = aOther / aRef;
            return ratio * Math.Sqrt(ratio);   // ratio^(3/2), 无 Pow 调用
        }

        /// <summary>
        /// 活力公式 (Vis-Viva) —— 由半径与半长轴求轨道速率。
        /// <para><c>v = sqrt( μ · (2/r − 1/a) )</c></para>
        /// <para>量纲: rKm [km], aKm [km], mu [km³/s²] → 返回 [km/s]。</para>
        /// <para>aKm 为正即椭圆/圆轨; 为负即双曲轨 (a = −μ/(2E) &lt; 0), 公式同样成立。</para>
        /// </summary>
        public static double VisVivaSpeedKms(double rKm, double aKm, double mu)
        {
            if (rKm <= 0.0 || mu <= 0.0 || aKm == 0.0) return 0.0;
            double vSq = mu * (2.0 / rKm - 1.0 / aKm);
            return vSq > 0.0 ? Math.Sqrt(vSq) : 0.0;
        }

        /// <summary>
        /// 圆轨道速率 (活力公式的 a = r 特例, 无 1/a 项以规避除零)。
        /// <para><c>v_circ = sqrt( μ / r )</c></para>
        /// </summary>
        public static double CircularSpeedKms(double rKm, double mu)
        {
            if (rKm <= 0.0 || mu <= 0.0) return 0.0;
            return Math.Sqrt(mu / rKm);
        }

        /// <summary>
        /// 由停泊轨道速度与目标双曲过剩速度求逃逸注入速度。
        /// <para><c>v_hyp = sqrt( v_inf² + 2μ/r_park )</c></para>
        /// 该 2μ/r 项来自能量守恒, 非经验系数。
        /// </summary>
        public static double HyperbolicSpeedAtRadiusKms(double vInfKms, double rKm, double mu)
        {
            if (rKm <= 0.0 || mu <= 0.0) return 0.0;
            double vSq = vInfKms * vInfKms + 2.0 * mu / rKm;
            return vSq > 0.0 ? Math.Sqrt(vSq) : 0.0;
        }

        /// <summary>
        /// 霍曼转移半长轴 —— 共面两圆轨道切向转移的椭圆半长轴。
        /// <para><c>a_trans = (r1 + r2) / 2</c></para>
        /// 取代手写的 <c>(r1 + r2) * 0.5</c> / <c>(rPrev + r1) * 0.5</c>。
        /// </summary>
        public static double HohmannSemiMajorAxisKm(double r1Km, double r2Km)
            => (r1Km + r2Km) * 0.5;

        /// <summary>
        /// 霍曼转移椭圆上的飞行时间 (半周期)。
        /// <para><c>t = π · sqrt( a_trans³ / μ )</c></para>
        /// 取代手写的 <c>Math.PI * Math.Sqrt(Math.Pow(aTrans, 3.0) / sunMu)</c>。
        /// </summary>
        public static double HohmannTransferTimeSec(double aTransKm, double mu)
        {
            if (aTransKm <= 0.0 || mu <= 0.0) return 0.0;
            return Math.PI * Math.Sqrt(aTransKm * aTransKm * aTransKm / mu);
        }

        /// <summary>
        /// 霍曼转移椭圆上的飞行时间 (日)。
        /// </summary>
        public static double HohmannTransferTimeDay(double aTransKm, double mu)
            => SecToDay(HohmannTransferTimeSec(aTransKm, mu));

        /// <summary>
        /// 共振轨道半长轴 —— 与参考圆轨道成 p:q 通约比的开普勒半长轴。
        /// <para><c>a_res = r_ref · (q/p)^(2/3)</c></para>
        /// 例: 2:1 泵升 (外太阳系常用) → <c>ResonantSemiMajorAxisKm(r, 2, 1)</c> = r·2^(2/3);
        /// 1:2 降轨 (内侧深潜常用) → <c>ResonantSemiMajorAxisKm(r, 1, 2)</c> = r·(1/2)^(2/3)。
        /// <para>指数 2/3 来自开普勒第三定律 T ∝ a^(3/2), 非经验系数。</para>
        /// </summary>
        public static double ResonantSemiMajorAxisKm(double rRefKm, int periodRatioOuter, int periodRatioInner)
        {
            if (rRefKm <= 0.0 || periodRatioOuter <= 0 || periodRatioInner <= 0) return rRefKm;
            double ratio = (double)periodRatioOuter / periodRatioInner;
            double cubeRoot = Math.Pow(ratio, 1.0 / 3.0);
            return rRefKm * cubeRoot * cubeRoot;   // r · ratio^(2/3)
        }

        /// <summary>
        /// 双曲飞掠的最大偏折角 (弧度) —— 由中心天体引力参数、安全近心点与 v∞ 决定。
        /// <para><c>δ_max = 2·asin( 1 / (1 + r_safe·v∞²/μ) )</c></para>
        /// 该式即 <see cref="MGAFlybyDynamicsHelper.ComputeMaxTurnAngleRad"/> 的底层形式,
        /// 此处提供以便无 flyby 依赖的纯估计器复用。
        /// </summary>
        public static double HyperbolicMaxDeflectionRad(double mu, double rSafeKm, double vInfKms)
        {
            if (mu <= 0.0 || rSafeKm <= 0.0) return 0.0;
            double vInf = Math.Max(vInfKms, 1e-9);
            double sinArg = 1.0 / (1.0 + rSafeKm * vInf * vInf / mu);
            if (sinArg >= 1.0) return Math.PI;          // v∞→0: 完全反向
            return 2.0 * Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinArg)));
        }

        /// <summary>
        /// 由偏折角求可获得的速率增量 (矢量差模长)。
        /// <para><c>Δv = 2·v∞·sin(δ/2)</c></para>
        /// 该式是余弦定理的直接推论 (两个等长矢量夹角 δ 时的差模), 非经验系数。
        /// </summary>
        public static double DeflectionSpeedGainKms(double vInfKms, double deflectionRad)
            => 2.0 * vInfKms * Math.Sin(deflectionRad * 0.5);

        /// <summary>
        /// 反向同义: 由目标速率增量求所需偏折角 (弧度)。
        /// <para><c>δ = 2·asin( Δv / (2·v∞) )</c></para>
        /// 广义相对论外的经典极限要求 Δv ≤ 2·v∞, 超出即夹取到 π (方向反转极限)。
        /// </summary>
        public static double RequiredDeflectionRad(double vInfKms, double deltaVKms)
        {
            if (vInfKms <= 0.0) return Math.PI;
            double arg = deltaVKms / (2.0 * vInfKms);
            if (arg >= 1.0) return Math.PI;
            if (arg <= -1.0) return -Math.PI;
            return 2.0 * Math.Asin(arg);
        }

        /// <summary>
        /// 双曲轨道的双曲正弦反函数 (asinh) 的数值稳定实现。
        /// 大 x 时直接算 <c>x + sqrt(x²+1)</c> 会因 x² 溢出而失真, 故分支处理。
        /// </summary>
        public static double Asinh(double x)
        {
            double ax = Math.Abs(x);
            if (ax < 1.0e-8) return x;
            if (ax > 1.0e8) return Math.Sign(x) * (Math.Log(ax) + Math.Log(2.0));
            return Math.Sign(x) * Math.Log(ax + Math.Sqrt(ax * ax + 1.0));
        }

        /// <summary>
        /// 由位置矢量求日心(或中心天体)相位角 (deg, ∈ [0, 360))。
        /// 取代手写的 <c>Math.Atan2(y, x) * (180/PI); if (&lt; 0) += 360;</c>。
        /// </summary>
        public static double Atan2PhaseDeg(double y, double x)
            => NormalizeDeg360(RadToDeg(Math.Atan2(y, x)));

        // ====================================================================
        // G. 常用数学恒等式 (消除裸写的 0.5 / 2.0 表达式)
        // ====================================================================

        /// <summary>中点 / 算术平均 —— 同时覆盖"半和"与"半差"两类几何用法。</summary>
        public static double Midpoint(double a, double b) => (a + b) * 0.5;

        /// <summary>半值。</summary>
        public static double Half(double x) => x * 0.5;

        /// <summary>二倍值。</summary>
        public static double Twice(double x) => x * 2.0;

        /// <summary>平方 (避免 Math.Pow(x, 2.0) 的浮点开销与可读性损失)。</summary>
        public static double Square(double x) => x * x;

        /// <summary>立方 (避免 Math.Pow(x, 3.0))。</summary>
        public static double Cube(double x) => x * x * x;

        /// <summary>直角三角形斜边 / 三维矢量模长 (由分量给出)。</summary>
        public static double Hypotenuse(double a, double b) => Math.Sqrt(a * a + b * b);

        /// <summary>
        /// 余弦定理的矢量差形式 —— 两个模长与夹角已知时求差矢量的模。
        /// <para><c>|A−B| = sqrt( a² + b² − 2ab·cos(θ) )</c></para>
        /// 这是 <see cref="DeflectionSpeedGainKms"/> 的推广 (a = b = v∞ 时退化为 2v∞sin(θ/2))。
        /// </summary>
        public static double VectorDifferenceMagnitude(double a, double b, double angleRad)
            => Math.Sqrt(Math.Max(0.0, a * a + b * b - 2.0 * a * b * Math.Cos(angleRad)));

        /// <summary>半角正弦 —— 用于所有 <c>sin(θ/2)</c> 形态的公式。</summary>
        public static double SinHalfAngle(double angleRad) => Math.Sin(angleRad * 0.5);
    }
}
