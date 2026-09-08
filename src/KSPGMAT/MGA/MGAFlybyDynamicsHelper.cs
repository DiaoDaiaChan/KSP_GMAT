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
    }
}
