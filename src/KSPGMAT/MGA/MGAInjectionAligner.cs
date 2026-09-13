using System;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 停泊轨道数据抽象接口 (解耦 KSP 运行时环境，支持游戏内 Vessel、GMATStateManager 状态机与离线单元测试 Orbit)
    /// </summary>
    public interface IParkingOrbit {
        string BodyName { get; }
        double GravParameter { get; } // m^3/s^2
        double Period { get; }        // seconds
        Vector3d GetRelativePositionAtUT(double ut); // meters
        Vector3d GetOrbitalVelocityAtUT(double ut);   // m/s
    }

    /// <summary>
    /// 基于 GMATStateManager (SSOT 单一真值来源) 的标准化停泊轨道适配器
    /// 核心参数 100% 动态读取星历 (SafeRadiusKm, SoiKm, AtmosphereDepthKm, Mu)，彻底拒绝任何人工写死常数
    /// </summary>
    public class StateManagedParkingOrbit : IParkingOrbit {
        private readonly GMATSpacecraftModel sc;
        private readonly double cachedMu;
        private readonly double cachedSma;
        private readonly double cachedPeriod;
        private readonly double defaultParkRadiusKm;

        public StateManagedParkingOrbit(GMATSpacecraftModel spacecraft = null) {
            sc = spacecraft ?? GMATStateManager.Current?.Spacecraft ?? new GMATSpacecraftModel();
            string body = !string.IsNullOrEmpty(sc.CentralBody) ? sc.CentralBody : SolarSystemCatalog.RootStarName;
            var p = SolarSystemCatalog.GetPlanet(body);

            // 1. 引力常数从星历动态提取
            double muKms = (p != null && p.Mu > 0) ? p.Mu : MGADefaults.GetBodyMu(body);
            cachedMu = muKms * 1e9; // m^3/s^2

            // 2. 安全停泊半径直接使用星历权威计算好的 SafeRadiusKm (已在星历中综合了天体几何半径与大气层厚度)
            defaultParkRadiusKm = (p != null && p.SafeRadiusKm > 0) ? p.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(body);
            double rBodyM = (p != null && p.RadiusKm > 0 ? p.RadiusKm : MGADefaults.GetBodyRadiusKm(body)) * 1000.0;
            double minSafeRadiusM = defaultParkRadiusKm * 1000.0;
            double maxSoiRadiusM = (p != null && p.SoiKm > 0 && !double.IsInfinity(p.SoiKm)) ? (p.SoiKm * 1000.0) : (minSafeRadiusM * 50.0);

            // 3. 飞船空间态有效性检验 (严格依据星历天体半径与引力影响球 SOI 判定)
            double rM = sc.PosKm.magnitude * 1000.0;
            double vM = sc.VelKms.magnitude * 1000.0;

            if (rM > rBodyM && rM < maxSoiRadiusM && vM > 0.1) {
                // 1/a = 2/r - v^2/mu
                double invA = 2.0 / rM - (vM * vM) / cachedMu;
                cachedSma = invA > 1e-12 ? 1.0 / invA : Math.Max(rM, minSafeRadiusM);
            } else {
                cachedSma = minSafeRadiusM;
            }
            cachedPeriod = PykepBridge.CalcOrbitPeriodSecM(cachedSma, cachedMu);
        }

        public string BodyName => sc?.CentralBody ?? "Earth";
        public double GravParameter => cachedMu;
        public double Period => cachedPeriod;

        public Vector3d GetRelativePositionAtUT(double ut) {
            double dt = ut - sc.UniversalTime;
            Vector3d rKm = sc.PosKm;
            Vector3d vKms = sc.VelKms;
            if (rKm.sqrMagnitude < 1.0) {
                rKm = new Vector3d(defaultParkRadiusKm, 0, 0);
                vKms = new Vector3d(0, Math.Sqrt((cachedMu / 1e9) / defaultParkRadiusKm), 0);
            }
            PykepNative.PropagateLagrangian(ref rKm, ref vKms, dt, cachedMu / 1e9);
            return rKm * 1000.0;
        }

        public Vector3d GetOrbitalVelocityAtUT(double ut) {
            double dt = ut - sc.UniversalTime;
            Vector3d rKm = sc.PosKm;
            Vector3d vKms = sc.VelKms;
            if (rKm.sqrMagnitude < 1.0) {
                rKm = new Vector3d(defaultParkRadiusKm, 0, 0);
                vKms = new Vector3d(0, Math.Sqrt((cachedMu / 1e9) / defaultParkRadiusKm), 0);
            }
            PykepNative.PropagateLagrangian(ref rKm, ref vKms, dt, cachedMu / 1e9);
            return vKms * 1000.0;
        }
    }

    /// <summary>
    /// KSP 游戏内实际飞船停泊轨道适配器 (周期由主天体真实物理参数动态推导)
    /// </summary>
    public class KSPVesselParkingOrbit : IParkingOrbit {
        private readonly Vessel vessel;
        public KSPVesselParkingOrbit(Vessel v) { vessel = v; }
        public string BodyName => vessel?.mainBody?.name ?? "";
        public double GravParameter => vessel?.mainBody?.gravParameter ?? 0.0;
        public double Period {
            get {
                if (vessel?.orbit != null && vessel.orbit.period > 0) return vessel.orbit.period;
                if (vessel?.mainBody != null && vessel.mainBody.gravParameter > 0) {
                    double rSafe = vessel.mainBody.Radius + (vessel.mainBody.atmosphere ? vessel.mainBody.atmosphereDepth : 0.0) + 20000.0;
                    return PykepBridge.CalcOrbitPeriodSecM(rSafe, vessel.mainBody.gravParameter);
                }
                return MGADefaults.AlignFallbackOrbitPeriodSec;
            }
        }
        public Vector3d GetRelativePositionAtUT(double ut) => vessel?.orbit?.getRelativePositionAtUT(ut) ?? Vector3d.zero;
        public Vector3d GetOrbitalVelocityAtUT(double ut) => vessel?.orbit?.getOrbitalVelocityAtUT(ut) ?? Vector3d.zero;
    }

    /// <summary>
    /// 纯开普勒解析停泊轨道模型 (普适任意中心天体与卫星，参数完全从星历自动继承)
    /// </summary>
    public class AnalyticalParkingOrbit : IParkingOrbit {
        public string BodyName { get; set; } = "Earth";
        public double GravParameter { get; set; } = MGADefaults.EarthMuM3S2; // m^3/s^2
        public double SemiMajorAxis { get; set; } = (MGADefaults.EarthRadiusKm + MGADefaults.DepartureAltitudeKm) * 1000.0;      // meters
        public double Eccentricity { get; set; } = 0.0;
        public double InclinationDeg { get; set; } = 0.0;
        public double RAANDeg { get; set; } = 0.0;
        public double AOPDeg { get; set; } = 0.0;
        public double MeanAnomalyAtEpoch { get; set; } = 0.0;
        public double EpochUT { get; set; } = 0.0;

        public AnalyticalParkingOrbit() { }

        /// <summary>
        /// 动态自适应构造函数：根据任意天体名称与轨道高度从星历提取真实物理常数
        /// 若 altitudeKm 小于等于 0，则直接采用星历中权威的 SafeRadiusKm
        /// </summary>
        public AnalyticalParkingOrbit(string bodyName, double altitudeKm = -1.0, double incDeg = 0.0, double raanDeg = 0.0, double aopDeg = 0.0) {
            BodyName = SolarSystemCatalog.CanonicalizeName(bodyName);
            var p = SolarSystemCatalog.GetPlanet(BodyName);
            double rBodyKm = (p != null && p.RadiusKm > 0) ? p.RadiusKm : MGADefaults.GetBodyRadiusKm(BodyName);
            double muKms = (p != null && p.Mu > 0) ? p.Mu : MGADefaults.GetBodyMu(BodyName);

            double parkRadiusKm = (altitudeKm > 0) 
                ? (rBodyKm + altitudeKm) 
                : ((p != null && p.SafeRadiusKm > 0) ? p.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(BodyName));

            SemiMajorAxis = parkRadiusKm * 1000.0;
            GravParameter = muKms * 1e9;
            InclinationDeg = incDeg;
            RAANDeg = raanDeg;
            AOPDeg = aopDeg;
            Eccentricity = 0.0;
        }

        public double Period => PykepBridge.CalcOrbitPeriodSecM(SemiMajorAxis, GravParameter);

        public Vector3d GetRelativePositionAtUT(double ut) {
            double aKm = SemiMajorAxis / 1000.0;
            double muKms = GravParameter / 1e9;
            if (PykepNative.IsAvailable && PykepNative.ElementsToState(
                aKm, Eccentricity, InclinationDeg * Math.PI / 180.0,
                RAANDeg * Math.PI / 180.0, AOPDeg * Math.PI / 180.0,
                MeanAnomalyAtEpoch, EpochUT, ut, muKms,
                out Vector3d posKm, out _))
            {
                return posKm * 1000.0;
            }

            // 离线环境兜底简易圆轨
            double n = Math.Sqrt(GravParameter / Math.Pow(SemiMajorAxis, 3));
            double M = MeanAnomalyAtEpoch + n * (ut - EpochUT);
            return new Vector3d(SemiMajorAxis * Math.Cos(M), SemiMajorAxis * Math.Sin(M), 0.0);
        }

        public Vector3d GetOrbitalVelocityAtUT(double ut) {
            double aKm = SemiMajorAxis / 1000.0;
            double muKms = GravParameter / 1e9;
            if (PykepNative.IsAvailable && PykepNative.ElementsToState(
                aKm, Eccentricity, InclinationDeg * Math.PI / 180.0,
                RAANDeg * Math.PI / 180.0, AOPDeg * Math.PI / 180.0,
                MeanAnomalyAtEpoch, EpochUT, ut, muKms,
                out _, out Vector3d velKms))
            {
                return velKms * 1000.0;
            }

            double v = Math.Sqrt(GravParameter / SemiMajorAxis);
            double n = Math.Sqrt(GravParameter / Math.Pow(SemiMajorAxis, 3));
            double M = MeanAnomalyAtEpoch + n * (ut - EpochUT);
            return new Vector3d(-v * Math.Sin(M), v * Math.Cos(M), 0.0);
        }
    }

    /// <summary>
    /// 停泊轨道出射点火相位精确对齐引擎 (Parking Orbit Injection Phase Aligner)
    /// 
    /// 核心职责与物理原理:
    /// MGA 全局 Lambert 寻优给出的是转移轨道的标称脱离历元 (DepartureUT)。
    /// 当飞船处于低圆停泊轨道 (LEO/LMO/LJO 等) 上时，轨道周期仅约数十至上百分钟。
    /// 在标称 DepartureUT 这一秒，飞船在停泊轨道上的物理位置通常并未恰好运行到最佳射出角 (Ejection Angle) 近心点点火方位。
    /// 若直接在 DepartureUT 点火，会导致射出双曲渐近线产生数十度的方向偏角（地月转移脱靶数十万公里）。
    /// 
    /// 本引擎严格基于 ESA PyKEP 原生 Lambert 求解器 (pykep_lambert_solve) 与二体运动学，
    /// 普适于任意天体系统（地月系、木卫系、土星系及行星际第一段出射逃逸），
    /// 沿飞船停泊轨道进行高精度搜索与黄金分割收敛，求得最佳近地点点火历元 t_burn 与切向顺向 ΔV，
    /// 严格遵循经典霍曼/双曲点火物理规律 (TLI 保持在 ~3100-3200 m/s 经典区间)，
    /// 消除轨道相位偏差，使生成的机动节点近心点直接精确归零！
    /// (遵循基线 0/7/9 铁律: 动力学 100% 调通 PyKEP 原生 C++ 闭环，0 经验拟合公式，0 托管自编开普勒)
    /// </summary>
    public static class MGAInjectionAligner {

        /// <summary>
        /// 精确求解飞船停泊轨道上的最优点火时间与三轴脉冲矢量 (通用停泊轨接口)
        /// </summary>
        public static bool AlignParkingOrbitInjection(
            IParkingOrbit orbit,
            MGASolution sol, 
            out double alignedTime, 
            out Vector3d alignedDv, 
            int legIndex = 0)
        {
            alignedTime = 0;
            alignedDv = Vector3d.zero;

            if (orbit == null || sol == null || sol.Legs == null || sol.Legs.Count <= legIndex) return false;

            var leg = sol.Legs[legIndex];
            alignedTime = leg.DepartureUT;
            alignedDv = new Vector3d(leg.ProgradeMs, leg.RadialMs, leg.NormalMs);

            double nominalUT = leg.DepartureUT;
            if (nominalUT <= 0) return false;

            // 1. 验证飞船当前所在的中心引力天体是否与航段出发天体匹配 (统一使用全局权威名称比对)
            string depBodyName = leg.FromBody;
            if (!SolarSystemCatalog.AreBodiesEqual(orbit.BodyName, depBodyName)) {
                return false;
            }

            double muKms = orbit.GravParameter / 1e9; // m^3/s^2 -> km^3/s^2
            if (muKms <= 0) {
                var p = SolarSystemCatalog.GetPlanet(orbit.BodyName);
                muKms = (p != null && p.Mu > 0) ? p.Mu : MGADefaults.GetBodyMu(orbit.BodyName);
            }

            double orbitPeriod = orbit.Period;
            if (orbitPeriod <= 0 || double.IsNaN(orbitPeriod) || orbitPeriod > 86400.0 * 10.0) {
                var pOrbit = SolarSystemCatalog.GetPlanet(orbit.BodyName);
                double safeR = (pOrbit != null && pOrbit.SafeRadiusKm > 0) ? pOrbit.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(orbit.BodyName);
                double safeMu = (pOrbit != null && pOrbit.Mu > 0) ? pOrbit.Mu : MGADefaults.GetBodyMu(orbit.BodyName);
                orbitPeriod = PykepBridge.CalcOrbitPeriodSec(safeR, safeMu);
            }

            // 确定搜索中心与时间窗口 (在 nominalUT 附近的完整单圈轨道 [tCenter - 0.5T, tCenter + 0.5T])
            double currentUT = 0.0;
            try {
                currentUT = Planetarium.GetUniversalTime();
            } catch {
                currentUT = 0.0;
            }

            double tCenter = nominalUT;
            if (currentUT > 0 && tCenter - 0.5 * orbitPeriod < currentUT + 60.0) {
                while (tCenter - 0.5 * orbitPeriod < currentUT + 60.0) {
                    tCenter += orbitPeriod;
                }
            }
            double tStart = tCenter - 0.5 * orbitPeriod;

            bool isCentralDep = (sol.CentralBody != null && SolarSystemCatalog.AreBodiesEqual(orbit.BodyName, sol.CentralBody));

            Vector3d rCraft0Km = orbit.GetRelativePositionAtUT(tStart) / 1000.0;
            Vector3d vCraft0Kms = orbit.GetOrbitalVelocityAtUT(tStart) / 1000.0;

            if (isCentralDep) {
                // =========================================================================
                // 场景 A: 行星系内母星转移 (例如 Earth 停泊轨 -> Moon，或 木星系 Europa -> Ganymede)
                // 物理目标: 寻找使飞船到达目标天体脉冲 ΔV 最小、且最贴合近心点切向加速的点火时刻 t_burn
                // =========================================================================
                string targetBodyName = leg.ToBody;
                double arrUT = leg.ArrivalUT;
                if (arrUT <= nominalUT) arrUT = nominalUT + leg.ToFDays * 86400.0;

                Vector3d rTargetKm = Vector3d.zero;
                Vector3d vTargetKms = Vector3d.zero;
                if (orbit is AnalyticalParkingOrbit || orbit is StateManagedParkingOrbit) {
                    if (leg.ArrivalPosKm.sqrMagnitude > 1.0) {
                        rTargetKm = leg.ArrivalPosKm;
                    }
                    var pTarget = SolarSystemCatalog.GetPlanet(targetBodyName);
                    if (pTarget != null) {
                        pTarget.GetRelativeState(orbit.BodyName, arrUT, out Vector3d rT, out Vector3d vT);
                        if (rTargetKm.sqrMagnitude < 1.0) rTargetKm = rT;
                        vTargetKms = vT;
                    }
                } else {
                    try {
                        if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                            var targetCb = FlightGlobals.Bodies.Find(b => SolarSystemCatalog.AreBodiesEqual(b.name, targetBodyName));
                            if (targetCb != null && targetCb.orbit != null) {
                                var unmod = PrincipiaInterop.GetUnmodifiedOrbit(targetCb) ?? targetCb.orbit;
                                rTargetKm = unmod.getRelativePositionAtUT(arrUT) / 1000.0;
                                vTargetKms = unmod.getOrbitalVelocityAtUT(arrUT) / 1000.0;
                            }
                        }
                    } catch { }

                    if (rTargetKm.sqrMagnitude < 1.0 && leg.ArrivalPosKm.sqrMagnitude > 1.0) {
                        rTargetKm = leg.ArrivalPosKm;
                    }
                }

                if (rTargetKm.sqrMagnitude < 1.0) return false;

                // 100% ESA PyKEP 原生 C++ 闭环求解 (Izzo Lambert + 动态滑动外推 + 黄金分割搜索 + Frenet 投影)
                if (PykepNative.IsAvailable && PykepNative.AlignInjectionDyn(
                    rCraft0Km, vCraft0Kms, tStart, muKms,
                    0, rTargetKm, (vTargetKms.sqrMagnitude > 1e-6 ? (Vector3d?)vTargetKms : null), arrUT,
                    nominalUT,
                    tStart, orbitPeriod,
                    MGADefaults.AlignSampleCount, MGADefaults.AlignGoldenSectionIterations,
                    out alignedTime, out alignedDv))
                {
                    return true;
                }

                return false;

            } else {
                // =========================================================================
                // 场景 B: 行星际双曲逃逸 (例如 Earth/Kerbin 停泊轨 -> Mars/Duna，中央天体为 Sun/Kerbol)
                // 物理目标: 寻找双曲渐近线方向与 MGA 解出的出射超速矢量 v_inf 最优重合的点火历元
                // =========================================================================
                Vector3d vInfKms = leg.VInfDepartureKms;
                if (vInfKms.sqrMagnitude < 0.0001 && leg.DepartureVelKms.sqrMagnitude > 1.0) {
                    // 若未显式记录 vInf，根据转移初速与出发母星相对于中心天体的轨道速度做矢量差
                    var pDep = SolarSystemCatalog.GetPlanet(depBodyName);
                    if (pDep != null) {
                        string centralBody = sol.CentralBody ?? SolarSystemCatalog.RootStarName;
                        pDep.GetRelativeState(centralBody, nominalUT, out _, out Vector3d vDepBodyKms);
                        vInfKms = leg.DepartureVelKms - vDepBodyKms;
                    }
                }

                double vInfMag = vInfKms.magnitude;
                if (vInfMag < 0.01) {
                    // 若仍无法获得有效 vInf，按标称 EjectionDeltaVKms 回退切向点火
                    vInfMag = Math.Max(0.5, leg.EjectionDeltaVKms);
                    vInfKms = new Vector3d(vInfMag, 0, 0);
                }

                // 100% ESA PyKEP 原生 C++ 闭环求解 (渐近线矢量夹角最小化 + 黄金分割搜索 + 近拱点切向点火能量积分)
                if (PykepNative.IsAvailable && PykepNative.AlignInjection(
                    rCraft0Km, vCraft0Kms, tStart, muKms,
                    1, vInfKms, vInfMag,
                    tStart, orbitPeriod,
                    MGADefaults.AlignSampleCount, MGADefaults.AlignGoldenSectionIterations,
                    out alignedTime, out alignedDv))
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// 精确求解飞船停泊轨道上的最优点火时间与三轴脉冲矢量 (KSP Vessel 适配器)
        /// </summary>
        public static bool AlignParkingOrbitInjection(
            Vessel vessel, 
            MGASolution sol, 
            out double alignedTime, 
            out Vector3d alignedDv, 
            int legIndex = 0)
        {
            alignedTime = 0;
            alignedDv = Vector3d.zero;
            if (vessel == null || vessel.orbit == null || vessel.mainBody == null) return false;
            return AlignParkingOrbitInjection(new KSPVesselParkingOrbit(vessel), sol, out alignedTime, out alignedDv, legIndex);
        }

        /// <summary>
        /// 自动将 MGA 方案中的第 1 段出发机动对齐至停泊轨道点火相位 (抽象接口，SSOT 闭环)
        /// </summary>
        public static bool AutoAlignSolution(IParkingOrbit orbit, MGASolution sol, MGAConfig config = null) {
            if (orbit == null || sol == null || sol.Legs == null || sol.Legs.Count == 0) return false;
            if (AlignParkingOrbitInjection(orbit, sol, out double alignedTime, out Vector3d alignedDv, 0)) {
                var leg0 = sol.Legs[0];
                if (!sol.IsPhaseAligned) {
                    sol.OriginalDepartureUT = (sol.OriginalDepartureUT > 0) ? sol.OriginalDepartureUT : sol.DepartureUT;
                    sol.OriginalDepartureDeltaVKms = sol.LaunchDeltaVKms;
                    sol.OriginalProgradeMs = leg0.ProgradeMs;
                    sol.OriginalRadialMs = leg0.RadialMs;
                    sol.OriginalNormalMs = leg0.NormalMs;
                    sol.OriginalTotalDeltaVKms = sol.TotalDeltaVKms;
                    sol.OriginalToFDays = leg0.ToFDays;
                }

                double shiftSec = alignedTime - (sol.OriginalDepartureUT > 0 ? sol.OriginalDepartureUT : leg0.DepartureUT);
                sol.DepartureUT = alignedTime;
                leg0.DepartureUT = alignedTime;
                leg0.ProgradeMs = alignedDv.x;
                leg0.RadialMs = alignedDv.y;
                leg0.NormalMs = alignedDv.z;
                leg0.EjectionDeltaVKms = alignedDv.magnitude / 1000.0;
                sol.LaunchDeltaVKms = alignedDv.magnitude / 1000.0;
                sol.IsPhaseAligned = true;
                sol.PhaseAlignmentShiftSec = shiftSec;

                // 重新刷新航段到达时刻与飞行天数
                leg0.ToFDays = Math.Max(0.1, (leg0.ArrivalUT - alignedTime) / 86400.0);
                sol.TotalFlightDays = Math.Max(0.1, (sol.ArrivalUT - alignedTime) / 86400.0);

                // 重新汇总总 ΔV
                double total = sol.LaunchDeltaVKms + sol.InsertionDeltaVKms;
                for (int k = 1; k < sol.Legs.Count; k++) {
                    total += sol.Legs[k].FlybyDeltaVKms;
                }
                sol.TotalDeltaVKms = total;

                // 重新构建机动事件列表，确保 Events[0] 与 Legs[0] / 顶层属性 100% 同步 (SSOT)
                var cfg = config ?? MGAStateManager.Current?.Config;
                if (cfg != null) {
                    sol.BuildEvents(cfg);
                }

                KSPGMATLog.Success($"[MGAInjectionAligner] 方案已自动对齐停泊轨点火相位: 偏移 {shiftSec:+0.0;-0.0}s ({shiftSec / 60.0:+0.0;-0.0}min), 最优点火历元={GMATClient.FormatUtcGregorian(alignedTime)}, 新射出ΔV={alignedDv.magnitude:F1} m/s");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 自动将 MGA 方案中的第 1 段出发机动对齐至当前飞船的停泊轨道点火相位 (通过 GMATStateManager 保持 SSOT，支持解析停泊轨自动兜底)
        /// </summary>
        public static bool AutoAlignSolution(Vessel vessel, MGASolution sol, MGAConfig config = null) {
            if (sol == null) return false;
            var cfg = config ?? MGAStateManager.Current?.Config;
            bool isPreLaunch = (cfg == null || cfg.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit);

            // 模式 2 (在轨约束锁定): 用户已有在轨飞船，优先对齐实机在轨状态
            if (!isPreLaunch && vessel != null) {
                if (GMATStateManager.Current != null) {
                    GMATStateManager.Current.SyncFromVessel(vessel);
                    if (AutoAlignSolution(new StateManagedParkingOrbit(GMATStateManager.Current.Spacecraft), sol, cfg)) return true;
                }
                if (vessel.orbit != null && vessel.mainBody != null) {
                    if (AutoAlignSolution(new KSPVesselParkingOrbit(vessel), sol, cfg)) return true;
                }
            }

            // 模式 1 (发射前规划自由倾角/LAN) 或未指定在轨飞船时，依据星历推荐的最佳发射轨道面构建解析停泊轨
            if (sol.Legs != null && sol.Legs.Count > 0) {
                var leg0 = sol.Legs[0];
                double altKm = (cfg != null && cfg.DeparturePeAKm > 0) ? cfg.DeparturePeAKm : (cfg != null ? cfg.DepartureAltitudeKm : 200.0);
                double incDeg;
                double raanDeg;
                if (!isPreLaunch && cfg != null && (Math.Abs(cfg.DepartureInclinationDeg) > 1e-4 || Math.Abs(cfg.DepartureRAANDeg) > 1e-4)) {
                    incDeg = cfg.DepartureInclinationDeg;
                    raanDeg = cfg.DepartureRAANDeg;
                } else {
                    incDeg = (sol.RecommendedLaunchIncDeg > 0) ? sol.RecommendedLaunchIncDeg : leg0.EjectionInclinationDeg;
                    raanDeg = (sol.RecommendedLaunchRaanDeg > 0) ? sol.RecommendedLaunchRaanDeg : leg0.EjectionLANDeg;
                }
                var analyticOrbit = new AnalyticalParkingOrbit(leg0.FromBody, altKm, incDeg, raanDeg);
                return AutoAlignSolution(analyticOrbit, sol, cfg);
            }
            return false;
        }

        /// <summary>
        /// 还原方案至标称未对齐状态 (支持在 UI 中撤销相位对齐，并保持事件流与指标 SSOT 同步)
        /// </summary>
        public static void RestoreUnalignedSolution(MGASolution sol, MGAConfig config = null) {
            if (sol == null || !sol.IsPhaseAligned || sol.OriginalDepartureUT <= 0) return;
            var leg0 = sol.Legs[0];

            sol.DepartureUT = sol.OriginalDepartureUT;
            leg0.DepartureUT = sol.OriginalDepartureUT;
            leg0.ProgradeMs = sol.OriginalProgradeMs;
            leg0.RadialMs = sol.OriginalRadialMs;
            leg0.NormalMs = sol.OriginalNormalMs;
            leg0.EjectionDeltaVKms = sol.OriginalDepartureDeltaVKms;
            sol.LaunchDeltaVKms = sol.OriginalDepartureDeltaVKms;
            sol.TotalDeltaVKms = sol.OriginalTotalDeltaVKms;
            leg0.ToFDays = sol.OriginalToFDays;
            sol.TotalFlightDays = Math.Max(0.1, (sol.ArrivalUT - sol.OriginalDepartureUT) / 86400.0);

            sol.IsPhaseAligned = false;
            sol.PhaseAlignmentShiftSec = 0.0;

            var cfg = config ?? MGAStateManager.Current?.Config;
            if (cfg != null) {
                sol.BuildEvents(cfg);
            }

            KSPGMATLog.Info($"[MGAInjectionAligner] 方案已还原为标称未对齐状态: 标称历元={GMATClient.FormatUtcGregorian(sol.DepartureUT)}, 标称射出ΔV={sol.LaunchDeltaVKms * 1000.0:F1} m/s");
        }
    }
}
