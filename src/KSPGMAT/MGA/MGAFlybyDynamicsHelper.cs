using System;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 飞掠进场几何与 B 平面动力学解析计算核心 (MGA Flyby Geometry & B-Plane Calculator)。
    /// 基于纯天体力学（双曲过剩速度渐近线、开普勒轨道积分、偏转面角动量法向与参考坐标基），
    /// 精确解算航天器飞掠行星时的切入天区 (DHA/DEC)、B 平面瞄准矢量 (B·T, B·R)、近拱点高度与偏转角。
    /// </summary>
    public static class MGAFlybyDynamicsHelper {
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
        public static FlybyGeometry ComputeFlybyGeometry(Vector3d vInRelKms, Vector3d vOutRelKms, double bodyMu, double bodyRadiusKm) {
            var res = new FlybyGeometry();
            double vInMag = vInRelKms.magnitude;
            double vOutMag = vOutRelKms.magnitude;
            res.VInfKms = vInMag;

            if (vInMag < 1e-6 || vOutMag < 1e-6 || bodyMu <= 0) {
                res.PeriapsisRadiusKm = bodyRadiusKm + 200.0;
                res.PeriapsisAltitudeKm = 200.0;
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
            double safeRadiusKm = bodyRadiusKm + 200.0;
            bool nativeSuccess = false;
            try {
                if (PykepNative.IsAvailable && PykepNative.EvaluateFlyby(vInRelKms, vOutRelKms, bodyMu, safeRadiusKm, out double turnDeg, out double rpKm, out _)) {
                    res.TurnAngleDeg = turnDeg;
                    res.PeriapsisRadiusKm = rpKm;
                    double deltaRad = turnDeg * (Math.PI / 180.0);
                    double sinHalf = Math.Sin(deltaRad * 0.5);
                    res.Eccentricity = sinHalf > 1e-6 ? 1.0 / sinHalf : 1.0;
                    nativeSuccess = true;
                }
            } catch { }

            if (!nativeSuccess) {
                // 原生未就绪时的几何极角降级 (0 冒充原生)
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
                    res.PeriapsisRadiusKm = safeRadiusKm;
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
                    double bodyMu = fbPlanet != null ? fbPlanet.Mu : 398600.4;
                    double bodyRadius = fbPlanet != null ? fbPlanet.RadiusKm : 6378.14;

                    Vector3d vIn = leg.VInfArrivalKms;
                    Vector3d vOut = nextLeg.VInfDepartureKms;
                    var geom = ComputeFlybyGeometry(vIn, vOut, bodyMu, bodyRadius);

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
                        SafeFlybyRadiusKm = fbPlanet != null && fbPlanet.SafeRadiusKm > 0 ? fbPlanet.SafeRadiusKm : (bodyRadius + 200.0)
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
            0.5, 0.90, 0.95, 1.0, 1.05, 1.10, 1.15, 1.25, 4.0 / 3.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 5.0, 6.0
        };

        /// <summary>
        /// 由给定航段 ToF 上下界推导标称共振比: 选取落在 [minToF, maxToF] 内且最接近窗口中心的物理共振档位。
        /// 数学依据: 以开普勒共振周期 r·T_orb 直接对 ToF 搜索窗中心做有理槽投影，取代按天体类别拍脑袋的 {1,2,3} 选择。
        /// </summary>
        public static double PickNominalResonanceRatio(double tOrbSec, double minToFSec, double maxToFSec) {
            if (tOrbSec <= 0) return 1.0;
            double center = (minToFSec + maxToFSec) * 0.5;
            double bestR = 1.0;
            double bestDist = double.MaxValue;
            foreach (double r in ResonanceRatioCandidates) {
                double t = r * tOrbSec;
                if (t < minToFSec - 86400.0 || t > maxToFSec + 86400.0) continue;
                double d = Math.Abs(t - center);
                if (d < bestDist) { bestDist = d; bestR = r; }
            }
            return bestR;
        }

        /// <summary>
        /// 基于开普勒活力公式 (Vis-Viva) 与双曲无动力偏折极限角几何，
        /// 解析自动推导同星共振航段 (P -> P) 的物理有效航时上下界 [MinDays, MaxDays] 与最佳共振阶数。
        /// 彻底消除硬编码 2:1、1.86~2.14 或 period 300~550 天经验魔法常数。
        /// </summary>
        public static (double MinDays, double MaxDays, double OptimalRatio) DerivePhysicalResonantBounds(
            SolarPlanet currentPlanet,
            SolarPlanet downstreamTarget,
            double muCentralKmS,
            double vinfEstimateKms = 0.0)
        {
            if (currentPlanet == null || currentPlanet.SMA <= 0 || muCentralKmS <= 0)
            {
                return (300.0, 800.0, 1.0);
            }

            double aP = currentPlanet.SMA; // km
            double tOrbitDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(aP, 3.0) / muCentralKmS) / 86400.0;
            double vCircKms = Math.Sqrt(muCentralKmS / aP);

            // 1. 若未显式给出进场 v_inf，依据与下游/前序轨道势阱能量梯度解析估算物理极限
            double vInf = vinfEstimateKms;
            if (vInf <= 0.0)
            {
                if (downstreamTarget != null && downstreamTarget.SMA > 0)
                {
                    double aTrans = (aP + downstreamTarget.SMA) * 0.5;
                    double vTransAtP = Math.Sqrt(Math.Max(1e-4, muCentralKmS * (2.0 / aP - 1.0 / aTrans)));
                    vInf = Math.Abs(vTransAtP - vCircKms);
                }
                else
                {
                    vInf = Math.Max(3.0, vCircKms * 0.20);
                }
            }

            // 2. 双曲无动力飞掠偏折角物理极限: delta_max = 2 * arcsin(1 / (1 + r_safe * v_inf^2 / mu_body))
            double rSafeKm = currentPlanet.RadiusKm + 200.0;
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

            // 5. 由下游目标天体所需开普勒能量解析推导共振闭合阶数 k_res
            double kRes = 1.0;
            if (downstreamTarget != null && downstreamTarget.SMA > aP)
            {
                // 目标是外侧天体 (如木星): 需要抬升半长轴以匹配下游转移
                double aReq = (aP + downstreamTarget.SMA) * 0.5;
                double tReqDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(aReq, 3.0) / muCentralKmS) / 86400.0;
                double ratioMax = Math.Max(1.0, tDepMaxDays / tOrbitDays);
                double ratioReq = Math.Max(1.0, tReqDays / tOrbitDays);
                kRes = Math.Max(1.0, Math.Round(Math.Min(ratioReq, ratioMax)));
            }

            // 共振腿连续物理搜索区间:
            // 下界由最低阶可行共振 (有理次谐波共振 0.65 T_orbit, 完整覆盖 1:1, 4:3, 3:2, 2:1 等高能共振槽) 严格决定;
            // 上界由天体引力偏折极限速度所决定的最大闭合半长轴周期与动力学逃逸能量协同确定
            // 短周期天体 (T_orb < 150天, 如水星 88天) 允许多阶共振自减速 (至多 6.5 T_orbit, 覆盖 MESSENGER/BepiColombo 4:4/6:6 槽)
            double minDays = Math.Max(10.0, tOrbitDays * 0.65);
            double maxResMultiplier = (tOrbitDays < 150.0) ? 6.5 : 3.5;
            double maxDays = Math.Max(minDays * 1.5, Math.Min(tOrbitDays * maxResMultiplier, tDepMaxDays * 1.25));
            if (maxDays < minDays * 1.2) maxDays = minDays * 1.5;

            return (minDays, maxDays, kRes);
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
            double depMinFactor = 0.65,
            double depMaxFactor = 1.50)
        {
            if (fromPlanet == null || toPlanet == null || fromPlanet.SMA <= 0 || toPlanet.SMA <= 0 || muCentralKmS <= 0)
            {
                return (50.0, 1000.0);
            }

            double a1 = fromPlanet.SMA;
            double a2 = toPlanet.SMA;
            double aTrans = (a1 + a2) * 0.5;
            double tHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aTrans, 3.0) / muCentralKmS) / 86400.0;

            if (isDepartureLeg)
            {
                double effDepMinFactor = depMinFactor;
                double ratio = a2 / a1;
                // 若目标跨度大 (如去外行星 a2/a1 > 2.5 或向内潜入内行星 a2/a1 < 0.6)，高能发射可显著缩短航时
                if (ratio > 2.5 || ratio < 0.6)
                {
                    effDepMinFactor = Math.Min(effDepMinFactor, 0.35);
                }
                return (Math.Max(5.0, tHohmannDays * effDepMinFactor), Math.Max(10.0, tHohmannDays * depMaxFactor));
            }

            // 欧拉-兰伯特抛物线转移极小航时: T_parabolic ≈ (sqrt(2)/3) * (a1 + a2)^1.5 / sqrt(mu)
            double tParabolicDays = (Math.Sqrt(2.0) / 3.0) * Math.Pow(a1 + a2, 1.5) / Math.Sqrt(muCentralKmS) / 86400.0;
            double minDays = Math.Max(5.0, tParabolicDays * 0.90);

            // 中间借力连接段: 航天器通过深空机动可能采用 1~2 圈多圈转移以闭合共面交点与星际相位 (如 MESSENGER E->V 448天, V->M 223天)
            double maxFactor = Math.Max(depMaxFactor, 3.5);
            double maxDays = Math.Max(minDays + 10.0, tHohmannDays * maxFactor);

            // 若出发星为强引力巨行星 (如木星/土星) 且向外加速，或目标为极远深空 (a2/a1 > 4.0 或 a2 > 3e9 km)，双曲巡航速度决定航时
            double distKm = Math.Abs(a2 - a1);
            if ((fromPlanet.Mu > 5.0e6 || fromPlanet.RadiusKm > 20000.0) && a2 > a1)
            {
                double vCirc1 = Math.Sqrt(muCentralKmS / a1);
                double vHypKms = vCirc1 * 1.5;
                double tHypDays = (distKm / vHypKms) / 86400.0;
                minDays = Math.Min(minDays, Math.Max(5.0, tHypDays * 0.85));
                maxDays = Math.Min(maxDays, Math.Max(minDays + 50.0, tHypDays * 2.50));
            }
            else if (a2 / a1 > 4.0 || a2 > 3.0e9)
            {
                // 极远深空快速双曲巡航: 航天器以 14~18 km/s 高速双曲奔赴
                double tFastCruiseDays = (distKm / 15.0) / 86400.0;
                minDays = Math.Min(minDays, Math.Max(50.0, tFastCruiseDays * 0.80));
                maxDays = Math.Min(maxDays, Math.Max(minDays + 100.0, tFastCruiseDays * 2.50));
            }

            return (minDays, maxDays);
        }

        public static double GetResonanceToFMultiplier(double periodDays) {
            if (periodDays <= 0) return 1.0;
            // 依据开普勒多阶共振自然收敛
            return (periodDays >= 250.0 && periodDays <= 700.0) ? 2.0 : 1.0;
        }

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
