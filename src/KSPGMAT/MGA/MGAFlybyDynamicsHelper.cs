using System;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 飞掠进场几何与 B 平面动力学解析计算核心 (MGA Flyby Geometry & B-Plane Calculator)。
    /// 基于纯天体力学（双曲过剩速度渐近线、开普勒轨道积分、偏转面角动量法向与参考坐标基），
    /// 精确解算航天器飞掠行星时的切入天区 (DHA/DEC)、B 平面瞄准矢量 (B·T, B·R)、近拱点高度与偏转角。
    ///
    /// 【2026-09-14 审计修正记录】
    ///   1. 删除 <see cref="DerivePhysicalResonantBounds"/> 中 `tOrbitDays &lt; 150.0` 的
    ///      "短周期天体 (如水星) 特许 6.5 倍共振" 分档 (其注释直接点名 MESSENGER/BepiColombo),
    ///      改为对任意天体统一的阶数上界 (只放宽、不排除可行解);
    ///   2. 把 `2500.0 天` 的绝对天数分档改为无量纲轨道半长轴比判据;
    ///   3. 把 `5478.75` 还原为 15.0 儒略年 (15 × 365.25 = 5478.75) 并命名;
    ///   4. 其余经验系数 (安全半径余量、航时收窄系数等) 全部命名化并加注标定状态;
    ///   5. <see cref="GetResonanceToFMultiplier"/> 明确其"忽略入参、恒返回 2:1"的真实语义
    ///      (原注释包装为"全面兼容金星/地球/火星")。
    /// </summary>
    public static class MGAFlybyDynamicsHelper {
        /// <summary>安全半径最小绝对余量 (km)。取代原裸字面量 25.0。</summary>
        public const double MinSafeRadiusMarginKm = 25.0;

        /// <summary>安全半径相对天体半径的余量比例。取代原裸字面量 0.05。</summary>
        public const double SafeRadiusFraction = 0.05;

        /// <summary>天体半径完全未知时的回退半径 (km)。取代原裸字面量 1000.0。</summary>
        public const double UnknownBodyRadiusFallbackKm = 1000.0;

        public struct FlybyGeometry {
            public double TurnAngleDeg;
            public double PeriapsisRadiusKm;
            public double PeriapsisAltitudeKm;
            public double VInfKms;
            public double IncomingDhaDeg;
            public double IncomingDecDeg;
            public double BDotTKm;
            public double BDotRKm;
            public double BMagKm;
            public double ThetaDeg;
            public double Eccentricity;
            public Vector3d SHat;
            public Vector3d THat;
            public Vector3d RHat;
            public Vector3d PeriapsisHat;
        }

        /// <summary>
        /// 基于借力天体相对入射速度 vIn 与出射速度 vOut 解算完整的飞掠几何与 B 平面参数
        /// </summary>
        public static FlybyGeometry ComputeFlybyGeometry(Vector3d vInRelKms, Vector3d vOutRelKms, double bodyMu, double bodyRadiusKm, double safeRadiusKm = -1.0, string bodyName = null) {
            var res = new FlybyGeometry();
            double vInMag = vInRelKms.magnitude;
            double vOutMag = vOutRelKms.magnitude;
            res.VInfKms = vInMag;

            // 【2026-09-14 审计修正】原为裸字面量 25.0 / 0.05 / 1000.0, 现命名为带标注的常量。
            double safeR = safeRadiusKm > 0 ? safeRadiusKm : (!string.IsNullOrEmpty(bodyName) ? MGADefaults.GetBodySafeRadiusKm(bodyName) : (bodyRadiusKm > 0 ? bodyRadiusKm + Math.Max(MinSafeRadiusMarginKm, bodyRadiusKm * SafeRadiusFraction) : UnknownBodyRadiusFallbackKm));

            if (vInMag < 1e-6 || vOutMag < 1e-6 || bodyMu <= 0) {
                res.PeriapsisRadiusKm = safeR;
                res.PeriapsisAltitudeKm = Math.Max(0.0, safeR - bodyRadiusKm);
                return res;
            }

            // 1. 入射渐近线单位矢量 S_hat
            res.SHat = vInRelKms / vInMag;

            // 2. 入射天区：赤经 DHA (0~360°) 与赤纬 DEC (-90~+90°)
            double dha = Math.Atan2(res.SHat.y, res.SHat.x) * (180.0 / Math.PI);
            if (dha < 0) dha += 360.0;
            res.IncomingDhaDeg = dha;
            double zClamped = Math.Max(-1.0, Math.Min(1.0, res.SHat.z));
            res.IncomingDecDeg = Math.Asin(zClamped) * (180.0 / Math.PI);

            // 3. 原生 C++ 解算双曲飞掠偏转角与近拱点半径 (100% 委托 PykepNative.EvaluateFlyby / pykep_flyby_con)
            double safeRadiusKmVal = safeR;
            bool nativeSuccess = false;
            try {
                if (PykepNative.IsAvailable && PykepNative.EvaluateFlyby(vInRelKms, vOutRelKms, bodyMu, safeRadiusKmVal, out double turnDeg, out double rpKm, out _)) {
                    res.TurnAngleDeg = turnDeg;
                    res.PeriapsisRadiusKm = rpKm;
                    double deltaRad = turnDeg * (Math.PI / 180.0);
                    double sinHalf = Math.Sin(deltaRad * 0.5);
                    res.Eccentricity = sinHalf > 1e-6 ? 1.0 / sinHalf : 1.0;
                    nativeSuccess = true;
                }
            } catch { }

            if (!nativeSuccess) {
                // 【2026-09-14 审计标注 · 托管计算的真实登记】
                // 本分支是**托管 (C#) 自编的飞掠几何**, 不冒充原生结果:
                //   转角 = acos( v_in·v_out / (|v_in||v_out|) ) —— 两渐近速度的夹角, 属纯矢量恒等式;
                //   偏心率 e = 1 / sin(δ/2), 近拱点 rp = (μ/v∞²)(e-1) —— 双曲线几何的解析反解。
                // 已登记的局限: 用 acos(dot) 求转角仅对**共面**的 v_in/v_out 精确, 对三维异面情形
                // 给出的是转角的下界; 且未考虑安全半径截断 (原生路径会做 rp >= r_safe 的约束)。
                // 因此本分支结果仅可用于 UI 预览与剪枝, **不得**作为 ΔV/可行性决算的输入。
                // 若需要严格几何, 应确保 PykepNative.EvaluateFlyby 可用。
                double dot = Vector3d.Dot(vInRelKms, vOutRelKms);
                double cosDelta = Math.Max(-1.0, Math.Min(1.0, dot / (vInMag * vOutMag)));
                double deltaRad = Math.Acos(cosDelta);
                res.TurnAngleDeg = deltaRad * (180.0 / Math.PI);
                double sinHalf = Math.Sin(deltaRad * 0.5);
                if (sinHalf > 1e-6) {
                    res.Eccentricity = 1.0 / sinHalf;
                    double vInfSq = vInMag * vInMag;
                    res.PeriapsisRadiusKm = (bodyMu / vInfSq) * (res.Eccentricity - 1.0);
                } else {
                    res.Eccentricity = 1.0;
                    res.PeriapsisRadiusKm = safeR;
                }
            }
            res.PeriapsisAltitudeKm = res.PeriapsisRadiusKm - bodyRadiusKm;

            // 5. 轨道动量法向与近拱点方向
            Vector3d hCross = Vector3d.Cross(vInRelKms, vOutRelKms);
            Vector3d hHat = hCross.magnitude > 1e-9 ? (hCross / hCross.magnitude) : Vector3d.Cross(res.SHat, new Vector3d(0, 0, 1)).normalized;
            Vector3d eDir = vOutRelKms - vInRelKms;
            res.PeriapsisHat = eDir.magnitude > 1e-9 ? (eDir / eDir.magnitude) : res.SHat;

            // 6. B 平面坐标基 (S_hat, T_hat, R_hat)
            Vector3d kRef = Math.Abs(res.SHat.z) < 0.999 ? new Vector3d(0, 0, 1) : new Vector3d(0, 1, 0);
            Vector3d tCross = Vector3d.Cross(res.SHat, kRef);
            res.THat = tCross.magnitude > 1e-9 ? (tCross / tCross.magnitude) : new Vector3d(1, 0, 0);
            res.RHat = Vector3d.Cross(res.SHat, res.THat).normalized;

            // 7. B 平面瞄准矢量 |B| 与投影 (B·T, B·R)
            double vInf2 = vInMag * vInMag;
            double bMag = res.PeriapsisRadiusKm * Math.Sqrt(Math.Max(0.0, 1.0 + (2.0 * bodyMu) / (res.PeriapsisRadiusKm * vInf2)));
            res.BMagKm = bMag;

            // 瞄准矢量 B_hat 位于偏转面内且垂直于 S_hat，指向双曲线内侧
            Vector3d bHat = Vector3d.Cross(hHat, res.SHat).normalized;
            res.BDotTKm = bMag * Vector3d.Dot(bHat, res.THat);
            res.BDotRKm = bMag * Vector3d.Dot(bHat, res.RHat);
            double theta = Math.Atan2(res.BDotRKm, res.BDotTKm) * (180.0 / Math.PI);
            if (theta < 0) theta += 360.0;
            res.ThetaDeg = theta;

            return res;
        }

        /// <summary>
        /// 为 MGASolution 中各航段回填完整的飞掠几何与 B 平面遥测数据
        /// </summary>
        public static void EnrichSolutionFlybyDynamics(MGASolution sol) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0) return;

            for (int k = 0; k < sol.Legs.Count; k++) {
                var leg = sol.Legs[k];
                // 中间借力点：连接上一个航段到达与下一个航段出发
                if (k < sol.Legs.Count - 1) {
                    var nextLeg = sol.Legs[k + 1];
                    string fbPlanetName = leg.ToBody;
                    var fbPlanet = SolarSystemCatalog.GetPlanet(fbPlanetName);
                    double bodyMu = fbPlanet != null ? fbPlanet.Mu : 0.0;
                    double bodyRadius = fbPlanet != null ? fbPlanet.RadiusKm : 0.0;
                    double safeRadius = fbPlanet != null && fbPlanet.SafeRadiusKm > 0 ? fbPlanet.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(fbPlanetName);

                    Vector3d vIn = leg.VInfArrivalKms;
                    Vector3d vOut = nextLeg.VInfDepartureKms;
                    var geom = ComputeFlybyGeometry(vIn, vOut, bodyMu, bodyRadius, safeRadius, fbPlanetName);

                    leg.FlybyTurningAngleDeg = geom.TurnAngleDeg;
                    leg.FlybyPeriapsisRadiusKm = geom.PeriapsisRadiusKm;
                    leg.FlybyPeriapsisAltitudeKm = geom.PeriapsisAltitudeKm;
                    leg.IncomingDhaDeg = geom.IncomingDhaDeg;
                    leg.IncomingDecDeg = geom.IncomingDecDeg;

                    leg.BPlane = new BPlaneResult {
                        BDotTKm = geom.BDotTKm,
                        BDotRKm = geom.BDotRKm,
                        BMagKm = geom.BMagKm,
                        ThetaDeg = geom.ThetaDeg,
                        VInfMagKms = geom.VInfKms,
                        SHat = geom.SHat,
                        THat = geom.THat,
                        RHat = geom.RHat,
                        EstimatedPericenterKm = geom.PeriapsisRadiusKm,
                        SafeFlybyRadiusKm = safeRadius
                    };
                }
            }
        }

        /// <summary>
        /// 计算无动力双曲线飞掠下天体能提供的最大物理偏转角 δ_max (rad)
        /// 理论公式: sin(δ_max / 2) = 1 / (1 + r_p_min * v_inf^2 / mu)
        /// </summary>
        public static double ComputeMaxTurnAngleRad(double bodyMu, double rpMinKm, double vInfKms) {
            if (bodyMu <= 0 || rpMinKm <= 0 || vInfKms <= 1e-6) return 0.0;
            double denom = 1.0 + (rpMinKm * vInfKms * vInfKms) / bodyMu;
            if (denom <= 1.0) return Math.PI;
            double sinHalf = 1.0 / denom;
            sinHalf = Math.Max(0.0, Math.Min(1.0, sinHalf));
            return 2.0 * Math.Asin(sinHalf);
        }

        /// <summary>
        /// 计算无动力双曲线飞掠下天体能提供的最大物理偏转角 δ_max (度)
        /// </summary>
        public static double ComputeMaxTurnAngleDeg(double bodyMu, double rpMinKm, double vInfKms) {
            return ComputeMaxTurnAngleRad(bodyMu, rpMinKm, vInfKms) * (180.0 / Math.PI);
        }

        /// <summary>
        /// 评估飞掠节点所需转角是否超出天体物理最大转向能力，若超出，推导开普勒共振调相次数 N
        /// 公式: N = ceil(θ_req / δ_max)
        /// </summary>
        public static int ComputeRequiredResonantPhasingCount(double reqTurnAngleRad, double bodyMu, double rpMinKm, double vInfKms) {
            double maxDelta = ComputeMaxTurnAngleRad(bodyMu, rpMinKm, vInfKms);
            if (maxDelta <= 1e-5) return 1;
            if (reqTurnAngleRad <= maxDelta) return 1;
            return (int)Math.Ceiling(reqTurnAngleRad / maxDelta);
        }

        /// <summary>
        /// 低阶有理开普勒共振比候选集: 1:1, 5:4, 4:3, 3:2, 5:3, 2:1, 5:2, 3:1, 4:1 (p:q, p,q ≤ 5)。
        /// 纯数学推导：由开普勒第三定律 T_sc = (p/q)·T_body 生成全部低阶有理共振槽。
        /// 具体哪一档物理可达由各调用点的 ToF 上下界过滤决定（多圈数档位在长周期天体上自然被边界排除），
        /// 彻底取代按 SMA 硬编码带挑选 {1,2} 或 {1,2,3} 的经验分支。
        /// </summary>
        public static readonly double[] ResonanceRatioCandidates = new double[] {
            0.5, 2.0 / 3.0, 0.75, 0.80, 1.0, 1.25, 4.0 / 3.0, 1.5, 5.0 / 3.0, 1.75, 2.0, 2.25, 2.5, 8.0 / 3.0, 3.0, 3.5, 4.0, 4.5, 5.0, 6.0
        };

        /// <summary>
        /// 自外侧天体进入时允许的最小共振周期比: 1.0 (1:1), 即禁止共振降能。
        /// </summary>
        public const double NoShrinkResonanceRatio = 1.0;

        // ---------------------------------------------------------------------
        // 共振航段物理边界常量 (2026-09-14 从裸字面量命名化)
        // ---------------------------------------------------------------------

        /// <summary>
        /// 同星共振航段的阶数上界 (以天体公转周期为单位的最大共振比)。
        ///
        /// 【2026-09-14 新增】取代原裸字面量 `6.5` 及其 `tOrbitDays &lt; 150.0` 天体周期分档分支
        /// (后者的注释直接点名 MESSENGER/BepiColombo)。
        /// 标注: **[经验参数·待标定]** 单次无动力偏折的解析泵升极限 (ratioMax) 显著小于本值
        /// (水星约 1.2), 说明多圈共振借力的**累计**泵升能力无法由单次偏折极限表达。
        /// 有监督标定完成后应替换为多圈累计泵升的解析上界。
        /// </summary>
        public const double MaxResonanceOrderCap = 6.5;

        /// <summary>同星共振航段下界: 最低阶可行共振对应的公转周期比例 (0.65 T_orbit)。</summary>
        public const double ResonantToFMinPeriodFactor = 0.65;

        /// <summary>同星共振航段下界的绝对天数地板 (天)。</summary>
        public const double ResonantToFMinFloorDays = 10.0;

        /// <summary>航段上界相对下界的最小展宽倍数 (共振段与异星转移段共用)。</summary>
        public const double ToFMinSpanFactor = 1.5;

        /// <summary>同星共振航段上界相对"最大可达周期"的能量逃逸裕度。</summary>
        public const double ResonantToFMaxEscapeMargin = 1.25;

        /// <summary>
        /// 航段上界相对下界的最小展宽守卫倍数 (共振段与异星转移段共用)。
        ///
        /// 【2026-09-14 第二轮审计修正】原名为 `ResonantToFMinSpanGuard`, 但异星转移段需要同样的守卫,
        /// 故改为中性名并在 <see cref="DerivePhysicalInterplanetaryBounds"/> 中一并使用。
        /// </summary>
        public const double ToFMinSpanGuard = 1.2;

        /// <summary>天体目录不可用时的同星共振航段回退边界 (天)。</summary>
        public const double InvalidInputFallbackMinDays = 300.0;

        /// <summary>天体目录不可用时的同星共振航段回退边界 (天)。</summary>
        public const double InvalidInputFallbackMaxDays = 800.0;

        /// <summary>天体目录不可用时异星转移航段的回退边界 (天)。</summary>
        public const double InvalidInterplanetaryFallbackMinDays = 50.0;

        /// <summary>天体目录不可用时异星转移航段的回退边界 (天)。</summary>
        public const double InvalidInterplanetaryFallbackMaxDays = 1000.0;

        /// <summary>
        /// 进场 v∞ 估算的无量纲下限: 不低于该天体圆轨速度的该比例。
        /// 取代原裸字面量 0.20 与以外的绝对地板 3.0 km/s。
        /// </summary>
        public const double MinVInfOfCircularSpeed = 0.20;

        /// <summary>异星转移段绝对最小天数地板。</summary>
        public const double MinLegToFFloorDays = 5.0;

        /// <summary>边界展宽用的最小天数偏置 (天)。</summary>
        public const double MinLegToFSpanDays = 10.0;

        /// <summary>出发段最小航时相对霍曼的标准比例。</summary>
        public const double DepartureLegMinToFFactor = 0.65;

        /// <summary>出发段最大航时相对霍曼的标准比例。</summary>
        public const double DepartureLegMaxToFFactor = 1.50;

        /// <summary>大跨度转移时放宽的出发段最小航时比例 (高能发射可显著缩短航时)。</summary>
        public const double WideSpanDepartureMinToFFactor = 0.35;

        /// <summary>判定"大跨度转移"的外向轨道半长轴比 (无量纲)。取代原裸字面量 2.5。</summary>
        public const double WideSpanOutwardRatio = 2.5;

        /// <summary>判定"大跨度转移"的内向轨道半长轴比 (无量纲)。取代原裸字面量 0.6。</summary>
        public const double WideSpanInwardRatio = 0.6;

        /// <summary>出发段最大航时相对霍曼的最小比例地板。取代原裸字面量 1.25。</summary>
        public const double DepartureLegMaxToFFloorFactor = 1.25;

        /// <summary>抛物线转移航时的收窄系数 (欧拉-兰伯特极小航时界)。取代原裸字面量 0.90。</summary>
        public const double ParabolicToFShrinkFactor = 0.90;

        /// <summary>恒星逃逸动能极速巡航航时的收窄系数。取代原裸字面量 0.80。</summary>
        public const double FastCruiseToFShrinkFactor = 0.80;

        /// <summary>中间借力连接段的航时上界相对霍曼的放大幅度。取代原裸字面量 3.5。</summary>
        public const double OuterTransferToFMaxFactor = 3.5;

        /// <summary>
        /// **单航段**航时上界的工程硬上限 (儒略年)。**对所有航段、所有天体对统一施加**。
        ///
        /// 【2026-09-14 第二轮审计修正 —— 本条取代了原先两个互相削弱的结构】
        ///
        /// 原实现有两处缺陷, 实测已量化 (见 `--test tof_bounds`):
        ///   1. 中段上界为 `maxDays = 霍曼时间 × 3.5` (无绝对上限)。霍曼时间本身随
        ///      (a1+a2)^1.5 增长, 于是木星→冥王星 (霍曼 ≈ 53 年) 得到 **183.5 年**,
        ///      天王星→冥王星 **276.8 年**, 海王星→冥王星 **357.5 年**。
        ///      316 个受检天体中段里有 **86 个超过 20 年**。
        ///   2. 原有一个 `DeepSpaceCruiseToFCapYears = 15` 的绝对上限, 但
        ///      (a) 被 `a2/a1 > 10` 的比值门**条件性**启用, 地球→土星 (9.58) 等恰好漏网;
        ///      (b) 施加方式是 `Math.Max(cap, tParabolic × 1.25)` —— **用 Math.Max 实现的"上限"
        ///          不可能起到上限作用**, 月球→冥王星因此被抬到 23.1 年。
        ///
        /// 现改为: `maxDays = min(霍曼时间 × 放大幅度, MaxTransferToFYears × 365.25)`,
        /// 对出发段与中段**一视同仁**, 并删除 `Max` 形式的伪上限与比值门。
        ///
        /// 取值依据 (工程口径, 非物理常数):
        ///   现实中最长的**单航段** MGA 转移是 New Horizons 的木星→冥王星 ≈ 8.4 年。
        ///   取 15 年给出 ≈1.8 倍余量, 足以覆盖任何真实任务, 同时杜绝求解器挑世纪尺度退化解
        ///   (该退化解的成因正是"航时长 → ΔV 低", 属搜索域缺陷而非物理必然)。
        ///
        /// 标注: **[工程口径·待标定]** —— 应由有监督标定 (以真值链的实际航段分布) 收紧;
        /// 但**它必须是一个显式声明的年数**, 而不是藏在"霍曼 × 常数"里隐式产生。
        /// </summary>
        public const double MaxTransferToFYears = 15.0;

        /// <summary>儒略年天数。</summary>
        public const double DaysPerJulianYear = 365.25;

        /// <summary>
        /// 由给定航段 ToF 上下界推导标称共振比: 选取落在 [minToF, maxToF] 内且最接近窗口中心的物理共振档位。
        /// 数学依据: 以开普勒共振周期 r·T_orb 直接对 ToF 搜索窗中心做有理槽投影，取代按天体类别拍脑袋的 {1,2,3} 选择。
        /// </summary>
        public static double PickNominalResonanceRatio(double tOrbSec, double minToFSec, double maxToFSec) {
            if (tOrbSec <= 0) return 1.0;
            // 物理共振基准优先：优先选取落在物理有效区间内的基础有理共振槽 (以 1:1 基础槽为首选锚点)
            double targetR = 1.0;
            double bestR = 1.0;
            double bestDist = double.MaxValue;
            foreach (double r in ResonanceRatioCandidates) {
                double t = r * tOrbSec;
                if (t < minToFSec - 86400.0 || t > maxToFSec + 86400.0) continue;
                double d = Math.Abs(r - targetR);
                if (d < bestDist) { bestDist = d; bestR = r; }
            }
            return bestR;
        }

        /// <summary>
        /// 基于开普勒活力公式 (Vis-Viva) 与双曲无动力偏折极限角几何，
        /// 解析自动推导同星共振航段 (P -> P) 的物理有效航时上下界 [MinDays, MaxDays] 与最佳共振阶数。
        /// 彻底消除硬编码 2:1、1.86~2.14 或 period 300~550 天经验魔法常数。
        /// </summary>
        public static (double MinDays, double MaxDays, double OptimalRatio, bool IsFeasible) DerivePhysicalResonantBounds(
            SolarPlanet currentPlanet,
            SolarPlanet downstreamTarget,
            double muCentralKmS,
            double vinfEstimateKms = 0.0,
            string centralBodyName = null)
        {
            if (currentPlanet == null || muCentralKmS <= 0)
            {
                // 【2026-09-14 审计修正】原为裸字面量 (300.0, 800.0, 1.0)。命名化并标注为回退值。
                return (InvalidInputFallbackMinDays, InvalidInputFallbackMaxDays, NoShrinkResonanceRatio, false);
            }

            // 【2026-09-14 第二轮审计修正 —— 参考系一致性门 (与异星转移段同规则)】
            // `currentPlanet.SMA` 是相对其**自身母体**的半长轴, 必须与传入 μ 的中央天体同参考系,
            // 否则推出的公转周期与共振区间在物理上无意义。
            string centralRes = !string.IsNullOrEmpty(centralBodyName) ? centralBodyName : SolarSystemCatalog.RootStarName;
            double aP = ResolveOrbitalRadiusKm(currentPlanet, centralRes);
            if (aP <= 0.0)
            {
                return (InvalidInputFallbackMinDays, InvalidInputFallbackMaxDays, NoShrinkResonanceRatio, false);
            }

            double tOrbitDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(aP, 3.0) / muCentralKmS) / 86400.0;
            double vCircKms = Math.Sqrt(muCentralKmS / aP);

            // 1. 若未显式给出进场 v_inf，依据与下游/前序轨道势阱能量梯度解析估算物理极限
            // 【2026-09-14 审计修正】原实现使用裸字面量 0.20 与绝对下限 3.0 km/s。
            // 现改为单一**无量纲比例**: vInf 下界 = 该天体圆轨速度 × MinVInfOfCircularSpeed。
            // 原绝对 3.0 km/s 下限被移除 —— 它以外太阳系天体的量级为基准, 对短周期小天体过大。
            double vInf = vinfEstimateKms;
            double aDownstream = (downstreamTarget != null) ? ResolveOrbitalRadiusKm(downstreamTarget, centralRes) : -1.0;
            if (vInf <= 0.0)
            {
                if (aDownstream > 0)
                {
                    double aTrans = (aP + aDownstream) * 0.5;
                    double vTransAtP = Math.Sqrt(Math.Max(1e-4, muCentralKmS * (2.0 / aP - 1.0 / aTrans)));
                    vInf = Math.Max(Math.Abs(vTransAtP - vCircKms), vCircKms * MinVInfOfCircularSpeed);
                }
                else
                {
                    vInf = vCircKms * MinVInfOfCircularSpeed;
                }
            }

            // 2. 双曲无动力飞掠偏折角物理极限: delta_max = 2 * arcsin(1 / (1 + r_safe * v_inf^2 / mu_body))
            double rSafeKm = (currentPlanet.SafeRadiusKm > 0) ? currentPlanet.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(currentPlanet.Name);
            double eMin = 1.0 + (rSafeKm * vInf * vInf) / Math.Max(1e-6, currentPlanet.Mu);
            double deltaMaxRad = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eMin));

            // 3. 最大顺向增能日心速度 (Prograde Pump Limit):
            //    离开行星时的最大日心速度标量由矢量三角形余弦定理决定
            double cosThetaMin = Math.Cos(Math.Max(0.0, 0.5 * Math.PI - deltaMaxRad));
            double vDepMax = Math.Sqrt(vCircKms * vCircKms + vInf * vInf + 2.0 * vCircKms * vInf * cosThetaMin);

            // 4. 由活力公式严格解析推导共振轨道的最大可达半长轴与最大自然周期
            //    1 / a_max = 2 / aP - vDepMax^2 / muCentral
            double invAMax = 2.0 / aP - (vDepMax * vDepMax) / muCentralKmS;
            double aMax = (invAMax > 1e-12) ? (1.0 / invAMax) : (aP * 3.5);
            double tDepMaxDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(aMax, 3.0) / muCentralKmS) / 86400.0;
            double ratioMax = Math.Max(1.0, tDepMaxDays / tOrbitDays);

            // 5. 由下游目标天体所需开普勒能量解析推导共振闭合阶数 k_res
            double kRes = 1.0;
            if (aDownstream > aP)
            {
                // 目标是外侧天体 (如木星): 需要抬升半长轴以匹配下游转移
                double aReq = (aP + aDownstream) * 0.5;
                double tReqDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(aReq, 3.0) / muCentralKmS) / 86400.0;
                double ratioReq = Math.Max(1.0, tReqDays / tOrbitDays);
                kRes = Math.Max(1.0, Math.Round(Math.Min(ratioReq, ratioMax)));
            }

            // 共振腿连续物理搜索区间:
            // 下界由最低阶可行共振 (有理次谐波共振 0.65 T_orbit, 完整覆盖 1:1, 4:3, 3:2, 2:1 等高能共振槽) 严格决定;
            // 上界由天体引力偏折极限速度所决定的最大闭合半长轴周期与动力学逃逸能量协同确定。
            //
            // 【2026-09-14 审计修正】原实现为:
            //     double maxResMultiplier = (tOrbitDays < 150.0) ? 6.5 : Math.Min(6.5, Math.Max(3.0, ratioMax));
            // 并以注释点名 "短周期天体 (如水星 88天) ... 覆盖 MESSENGER/BepiColombo 4:4/6:6 槽",
            // 属按已知测试任务的天体公转周期分档。
            //
            // 现改为**对任意天体统一**的阶数上界: 阶数上限取低阶有理共振槽的可用上界
            // (ResonanceRatioCandidates 的最大槽 6:1), 不再按周期分档。该变更只会**放宽**搜索域
            // (长周期天体原被 3.0 夹紧, 现同样放宽到 6.0), 不会排除任何可行解。
            //
            // 标注: 单次无动力偏折的解析泵升极限 (ratioMax, 如水星仅 ~1.2) 显著小于 6.0,
            //       说明多圈共振借力的累计泵升能力无法由单次偏折极限表达, 该上界
            //       **[经验参数·待标定]** —— 应由有监督标定替换为多圈累计泵升的解析解。
            double minDays = Math.Max(ResonantToFMinFloorDays, tOrbitDays * ResonantToFMinPeriodFactor);
            double maxResMultiplier = Math.Max(ratioMax, MaxResonanceOrderCap);
            // 注: `Math.Max(minDays * ToFMinSpanFactor, …)` 已保证上界 ≥ 1.5 × 下界,
            //     原实现随后又写了一个 `< minDays * 守卫(1.2)` 的检查, 该检查**永不可能为真** (死代码),
            //     已于 2026-09-14 第二轮审计移除。
            double maxDays = Math.Max(minDays * ToFMinSpanFactor,
                                      Math.Max(tOrbitDays * maxResMultiplier, tDepMaxDays * ResonantToFMaxEscapeMargin));

            // -----------------------------------------------------------------
            // 【2026-09-14 第二轮审计修正 —— 共振路径的航时上界同样必须受限】
            //
            // 上界原为 `tOrbitDays × max(ratioMax, 6.5)`, **没有任何绝对上限**。
            // 对长周期天体这直接炸掉 (实测, 见 --test tof_bounds 的 A3 段):
            //     土星 70453 天 (192.9 年) | 天王星 200211 天 (548.1 年)
            //     海王星 392170 天 (1073.7 年) | 冥王星 584130 天 (**1599.3 年**)
            // 这是 GUI 预设路径产出 49950 天退化解的直接来源 (该路径允许求解器在共振腿内自由取点)。
            //
            // 现施加与异星转移段**同一条**工程上限 `MaxTransferToFYears`。
            // 注意: 长周期天体的**物理下界** (0.65 × 公转周期) 本身就可能超过该上限
            // (冥王星 58413 天)。此时把下界压到 `上限 / ToFMinSpanFactor`, 以保持 min &lt; max
            // 且区间不塌缩为一点 (塌缩会使下游的 ToF 采样出现 0/0)。该压缩意味着
            // "这条共振腿的物理下界已超任务包络", 相应拓扑会在下游可行性判定中被淘汰。
            // -----------------------------------------------------------------
            double capDays = MaxTransferToFYears * DaysPerJulianYear;
            bool isFeasible = (minDays <= capDays);
            if (!isFeasible)
            {
                // 不可行：标记 isFeasible = false，杜绝将超出任务包络的长周期共振压为伪可行窄区间
                return (minDays, maxDays, kRes, false);
            }

            maxDays = Math.Min(maxDays, capDays);
            if (maxDays < minDays * ToFMinSpanGuard) maxDays = Math.Min(capDays, minDays * ToFMinSpanFactor);

            return (minDays, maxDays, kRes, true);
        }

        /// <summary>
        /// 基于欧拉-兰伯特方程与霍曼转移极值几何，自动解析推导两异星间转移航时上下界 [MinDays, MaxDays]。
        /// 彻底消除硬编码 0.75~1.45, 0.15~0.55 等经验常数。
        /// </summary>
        public static (double MinDays, double MaxDays) DerivePhysicalInterplanetaryBounds(
            SolarPlanet fromPlanet,
            SolarPlanet toPlanet,
            double muCentralKmS,
            bool isDepartureLeg = false,
            double depMinFactor = DepartureLegMinToFFactor,
            double depMaxFactor = DepartureLegMaxToFFactor,
            string centralBodyName = null)
        {
            if (fromPlanet == null || toPlanet == null || muCentralKmS <= 0)
            {
                return (InvalidInterplanetaryFallbackMinDays, InvalidInterplanetaryFallbackMaxDays);
            }

            // -----------------------------------------------------------------
            // 【2026-09-14 第二轮审计修正 —— 参考系一致性门】
            //
            // 原实现直接使用 `fromPlanet.SMA` / `toPlanet.SMA`。但 `SolarPlanet.SMA` 是
            // **相对该天体自身母体 (ParentName)** 的轨道半长轴, 而不是相对传入 μ 对应的中央天体。
            // 二者不一致时, 结果在物理上无意义, 且实测会给出**荒谬量级**:
            //
            //   例: `ResolveTransferCentralBody("Earth","Moon") == "Earth"`,
            //       于是以 μ_Earth 配上 `Earth.SMA = 1.496e8 km` (地球的**日心**半长轴) 与
            //       `Moon.SMA = 3.844e5 km` (月球的**地心**半长轴) —— 两个不同参考系混算,
            //       实测航时上界 = 19649 天 = **53.8 年** (阿波罗 11 号地月转移)。
            //
            // 现改为: 先解析各天体在**给定中央天体**参考系下的轨道半径; 任一不可解析即**拒绝**
            // 给出伪边界, 退回带标注的保守回退值 (而不是静默产出巨型数字)。
            // -----------------------------------------------------------------
            string central = !string.IsNullOrEmpty(centralBodyName) ? centralBodyName : SolarSystemCatalog.RootStarName;
            double a1 = ResolveOrbitalRadiusKm(fromPlanet, central);
            double a2 = ResolveOrbitalRadiusKm(toPlanet, central);
            if (a1 <= 0.0 || a2 <= 0.0)
            {
                return (InvalidInterplanetaryFallbackMinDays, InvalidInterplanetaryFallbackMaxDays);
            }

            double aTrans = (a1 + a2) * 0.5;
            double tHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aTrans, 3.0) / muCentralKmS) / 86400.0;

            // 上界统一由两项取小: (1) 霍曼时间 × 多圈/高能放大幅度; (2) 单航段工程年限硬上限。
            // 该"取小"对出发段与中段一视同仁 —— 原实现对两者用了两套互不相容的规则。
            double maxTransferDays = MaxTransferToFYears * DaysPerJulianYear;

            if (isDepartureLeg)
            {
                double effDepMinFactor = depMinFactor;
                double ratio = a2 / a1;
                // 若目标跨度大 (如去外行星 a2/a1 > 2.5 或向内潜入内行星 a2/a1 < 0.6)，高能发射可显著缩短航时
                if (ratio > WideSpanOutwardRatio || ratio < WideSpanInwardRatio)
                {
                    effDepMinFactor = Math.Min(effDepMinFactor, WideSpanDepartureMinToFFactor);
                }
                double minDepDays = Math.Max(MinLegToFFloorDays, tHohmannDays * effDepMinFactor);
                double maxDepDays = Math.Min(
                    Math.Max(minDepDays + MinLegToFSpanDays, tHohmannDays * Math.Max(DepartureLegMaxToFFloorFactor, depMaxFactor)),
                    maxTransferDays);
                if (maxDepDays < minDepDays * ToFMinSpanGuard) maxDepDays = minDepDays * ToFMinSpanFactor;
                return (minDepDays, maxDepDays);
            }

            // 欧拉-兰伯特抛物线转移极小航时与恒星逃逸动能极速巡航界 (Euler-Lambert Parabolic & Star Escape Bounds)
            double tParabolicDays = (Math.Sqrt(2.0) / 3.0) * Math.Pow(a1 + a2, 1.5) / Math.Sqrt(muCentralKmS) / 86400.0;
            double distKm = Math.Abs(a2 - a1);
            double vFastCruise = Math.Sqrt(2.0 * muCentralKmS / Math.Max(1.0, Math.Min(a1, a2)));
            double tFastCruiseDays = (distKm / Math.Max(1.0, vFastCruise)) / 86400.0;
            double minDays = Math.Max(MinLegToFFloorDays,
                Math.Min(tParabolicDays * ParabolicToFShrinkFactor, tFastCruiseDays * FastCruiseToFShrinkFactor));

            // 中间借力连接段: 航天器可采用单弧霍曼、双曲借力加速、或通过深空机动进行 1~2 圈多圈调相以闭合行星相位。
            //
            // 【2026-09-14 第二轮审计修正】原实现为两个分支:
            //     double maxFactor = Math.Max(depMaxFactor, 3.5);
            //     bool isDeepSpace = (a2/a1) > 10.0;
            //     maxDays = isDeepSpace
            //         ? Math.Min(tHohmann*maxFactor, Math.Max(15年, tParabolic*1.25))  // ← Math.Max 让"上限"失去意义
            //         : Math.Max(minDays + 10, tHohmann*maxFactor);                    // ← 无绝对上限
            // 实测后果: 316 个天体对中 86 个上界 > 20 年, 最长 357.5 年 (海王星→冥王星);
            // 并对 Voyager 2 给出 349 年的四段包络合计, 直接导致求解器选出世纪尺度退化解。
            // 现改为对全部航段统一的 `min(霍曼 × 放大, 年限硬上限)`。
            double maxDays = Math.Min(tHohmannDays * Math.Max(depMaxFactor, OuterTransferToFMaxFactor), maxTransferDays);
            if (maxDays < minDays * ToFMinSpanGuard) maxDays = minDays * ToFMinSpanFactor;

            return (minDays, maxDays);
        }

        /// <summary>
        /// 解析天体在**给定中央天体**参考系下的轨道半径 (km)。
        ///
        /// 【2026-09-14 第二轮审计新增 —— 消除"参考系混用"】
        /// 返回 &lt;= 0 表示"该天体的半长轴不在该中央天体的参考系内", 调用方**必须拒绝**使用它,
        /// 而不是拿它与该中央天体的 μ 硬凑 (原实现在此静默产出过 53.8 年的航时上界)。
        ///
        /// 规则 (按 SolarSystemBodies 的目录约定):
        ///   · 天体 == 中央天体本身: 用其**停泊轨道半径** (半径 + 安全发射高度)。
        ///     "绕自身的半长轴"无意义, 原实现把日心 1 AU 当成地球绕地球的半径 —— 这是 53.8 年的根因。
        ///   · 天体是卫星 (IsMoon)   : 半长轴以 ParentName 为参考系, 仅当 ParentName == 中央天体时可用。
        ///   · 天体是行星 (非卫星)   : 半长轴以**根恒星**为参考系, 仅当中央天体 == 根恒星时可用。
        /// </summary>
        public static double ResolveOrbitalRadiusKm(SolarPlanet p, string centralName)
        {
            if (p == null) return -1.0;

            // 1) 天体即中央天体本身 → 停泊轨道半径
            if (p.Name.Equals(centralName, StringComparison.OrdinalIgnoreCase))
            {
                double rBody = p.RadiusKm > 0 ? p.RadiusKm : MGADefaults.GetBodyRadiusKm(p.Name);
                double alt = MGADefaults.GetSafeDepartureAltitudeKm(p.Name);
                double rPark = rBody + Math.Max(0.0, alt);
                return rPark > 0 ? rPark : -1.0;
            }

            // 2) 卫星 → 以其 ParentName 为参考系
            if (p.IsMoon)
            {
                if (!string.IsNullOrEmpty(p.ParentName) &&
                    p.ParentName.Equals(centralName, StringComparison.OrdinalIgnoreCase) && p.SMA > 0)
                {
                    return p.SMA;
                }
                return -1.0;
            }

            // 3) 行星 → 以根恒星为参考系
            if (p.SMA <= 0) return -1.0;
            return centralName.Equals(SolarSystemCatalog.RootStarName, StringComparison.OrdinalIgnoreCase) ? p.SMA : -1.0;
        }

        /// <summary>
        /// 蓄能共振的标称航时倍数 (以借力天体公转周期为单位)。
        ///
        /// 【2026-09-14 审计标注】本方法**忽略入参 periodDays**, 恒返回 2:1 ——
        /// 因为 2:1 是开普勒二体标准蓄能共振槽 (已含于 <see cref="ResonanceRatioCandidates"/>),
        /// 对任意恒星系成立, 不需要按天体周期分档 (原实现在这里既未使用入参, 又写死 2.0,
        /// 且被注释包装成"全面兼容金星/地球/火星"——现明确其真实语义)。
        ///
        /// 调用方注意: 若已知航时窗口, 应优先使用 <see cref="PickNominalResonanceRatio"/>
        /// 由窗口反解真实槽位; 本方法仅作为无窗口信息时的保守默认。
        /// 标注: **[经验默认·待标定]**
        /// </summary>
        /// <summary>
        /// 蓄能共振的标称航时倍数 (以借力天体公转周期为单位)。
        /// 若已知航时搜索窗口 [minToFDays, maxToFDays]，优先调用 <see cref="PickNominalResonanceRatio"/>
        /// 反解最匹配的有理共振槽；无窗口信息时返回标准开普勒 2:1 蓄能共振槽 <see cref="PumpResonanceSlotRatio"/>。
        /// </summary>
        public static double GetResonanceToFMultiplier() {
            return PumpResonanceSlotRatio;
        }

        public static double GetResonanceToFMultiplier(double minToFDays, double maxToFDays, double periodDays) {
            if (periodDays <= 0) return NoShrinkResonanceRatio;
            if (minToFDays > 0 && maxToFDays >= minToFDays) {
                return PickNominalResonanceRatio(periodDays * 86400.0, minToFDays * 86400.0, maxToFDays * 86400.0);
            }
            return PumpResonanceSlotRatio;
        }

        [Obsolete("periodDays is insufficient to uniquely determine resonance ratio. Call GetResonanceToFMultiplier() or GetResonanceToFMultiplier(minToFDays, maxToFDays, periodDays).")]
        public static double GetResonanceToFMultiplier(double periodDays) {
            if (periodDays <= 0) return NoShrinkResonanceRatio;
            return PumpResonanceSlotRatio;
        }

        /// <summary>
        /// 标准蓄能共振槽 2:1 的周期比。取代原裸字面量 2.0。
        /// </summary>
        public const double PumpResonanceSlotRatio = 2.0;

        /// <summary>
        /// 计算两行星轨道面倾角差产生的相对速度离面分量 (km/s): v_orth = v_circ · sin(Δi)。
        /// 数学依据: 转移轨道面近似位于出发天体轨道面，到达目标天体时航天器日心速度的
        /// 离面分量为目标公转圆速度在倾角差方向上的投影，取代按 SMA 大小拍脑袋的 1.0/1.2/2.5/3.5 km/s 占位常数。
        /// 近共面行星（如地球-火星 Δi≈1.9°）自动趋零，大倾角差（如地球-水星 Δi≈7°）自动增大。
        /// </summary>
        public static double ComputeOutOfPlaneVinfKms(double vCircKms, double incFromDeg, double incToDeg) {
            if (vCircKms <= 0) return 0.0;
            double dInc = Math.Abs(incFromDeg - incToDeg) * (Math.PI / 180.0);
            return vCircKms * Math.Sin(dInc);
        }
    }
}
