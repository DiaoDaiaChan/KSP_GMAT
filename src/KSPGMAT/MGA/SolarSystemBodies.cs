using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 天体星历与物理参数的数据源方案
    /// </summary>
    public enum EphemerisScheme {
        /// <summary>方案 2 (首选): 游戏运行态 (FlightGlobals 实时天体树动态提取)</summary>
        Scheme2_FlightGlobals = 2,
        /// <summary>方案 1 (权威回退): NASA JPL SPICE 权威星历内核 (DE440 动态反算 / 原生常数)</summary>
        Scheme1_SpiceDE440 = 1
    }

    /// <summary>
    /// 方案 1 与方案 2 星历与轨道根数精确误差核验结果
    /// (100% C++ 原生计算矢量差与欧氏范数，0 托管物理计算)
    /// </summary>
    public class SchemeComparisonResult {
        public string BodyName = "";
        public double EpochUt = 0.0;
        public bool Scheme1Available = false;
        public bool Scheme2Available = false;
        public Vector3d PosScheme1 = Vector3d.zero; // SPICE (km)
        public Vector3d VelScheme1 = Vector3d.zero; // SPICE (km/s)
        public Vector3d PosScheme2 = Vector3d.zero; // FlightGlobals (km)
        public Vector3d VelScheme2 = Vector3d.zero; // FlightGlobals (km/s)
        public double SmaScheme1 = 0;
        public double SmaScheme2 = 0;
        public double EccScheme1 = 0;
        public double EccScheme2 = 0;
        public double IncScheme1 = 0;
        public double IncScheme2 = 0;

        // 100% Native C++ 原生计算误差指标
        public double PosDiffKm = 0.0;   // Δr = |r_KSP - r_SPICE|
        public double VelDiffMs = 0.0;   // Δv = |v_KSP - v_SPICE|
        public double DaKm = 0.0;        // |a_KSP - a_SPICE|
        public double De = 0.0;          // |e_KSP - e_SPICE|
        public double DincDeg = 0.0;     // |inc_KSP - inc_SPICE|
    }

    /// <summary>
    /// 行星天体模型与轨道星历 (支持 方案2 实时游戏天体 与 方案1 SPICE 权威回退)
    /// </summary>
    public class SolarPlanet {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Icon { get; set; }
        public double Mu { get; set; }              // km^3 / s^2
        public double RadiusKm { get; set; }        // km
        public double SafeRadiusKm { get; set; }    // km (Radius + Atmosphere / Safety margin)
        public double AtmosphereDepthKm { get; set; } // km (0 if airless)
        public double SoiKm { get; set; }             // km (Sphere of influence radius)
        public double SMA { get; set; }             // km
        public double ECC { get; set; }
        public double INC { get; set; }             // deg
        public double RAAN { get; set; }            // deg
        public double AOP { get; set; }             // deg
        public double MA0 { get; set; }             // Mean Anomaly at J2000 (deg)
        public double EpochJ2000 { get; set; } = 0.0; // UT of reference epoch
        public string ParentName { get; set; } = ""; // Reference central body name
        public bool IsMoon { get; set; } = false; // Whether this is a moon / secondary satellite
        /// <summary>
        /// 该天体的轨道要素**未能解析** (SPICE 与标准后备库均无此天体名, SMA 保持 &lt;= 0)。
        ///
        /// 【2026-09-14 第二轮审计新增】原实现在此**静默退化为地球** (1 AU / μ_Earth / R_Earth),
        /// 使任何未识别天体 (Kerbol 缩尺星系、JNSQ、自建星球包中的天体) 在物理上与地球等价 ——
        /// 由此产生的轨道、航时与 ΔV 全部是伪值, 且**不会被任何断言发现**。
        /// 现改为: 明确置位本标志, 且 SMA &lt;= 0, 由消费侧**拒绝**参与物理推导
        /// (见 MGADefaults / MGAFlybyDynamicsHelper.ResolveOrbitalRadiusKm 的参考系一致性门)。
        /// </summary>
        public bool IsOrbitUnresolved { get; set; } = false;
        public EphemerisScheme SourceScheme { get; set; } = EphemerisScheme.Scheme2_FlightGlobals;
        public EphemerisScheme ActiveEvaluationScheme { get; set; } = EphemerisScheme.Scheme2_FlightGlobals;

        public List<Orbit> OrbitChain { get; set; } = new List<Orbit>();

        /// <summary>
        /// 本地化天体显示名 (跟随当前语言，如 zh: "水星 (Mercury)" / en: "Mercury")。
        /// 未注册语言时回退到双语 DisplayName 以便搜索匹配。
        /// </summary>
        public string GetDisplayName() {
            return I18n.T("body_" + Name, DisplayName);
        }

        /// <summary>
        /// 计算指定 UT 时刻相对太阳的日心 ICRF 笛卡尔空间状态 (位置 km, 速度 km/s)
        /// 100% 线程安全且与 3D 开普勒轨道环几何严格锁相
        /// 优先执行方案 2 (游戏运行态)，不可用时自动平滑回退方案 1 (NASA JPL SPICE)
        /// </summary>
        public void GetHeliocentricState(double ut, out Vector3d posKm, out Vector3d velKms) {
            // 1. 方案 2 (第一优先级): 如果已预先提取 OrbitChain，在任意线程均可直接计算开普勒位置与速度
            // (100% 线程安全，纯 C# 开普勒求解，0 Unity API 跨线程调用)
            if (OrbitChain != null && OrbitChain.Count > 0) {
                try {
                    ActiveEvaluationScheme = EphemerisScheme.Scheme2_FlightGlobals;
                    Vector3d r_total = Vector3d.zero;
                    Vector3d v_total = Vector3d.zero;
                    for (int i = 0; i < OrbitChain.Count; i++) {
                        var orb = OrbitChain[i];
                        if (orb != null) {
                            r_total += orb.getRelativePositionAtUT(ut);
                            v_total += orb.getOrbitalVelocityAtUT(ut);
                        }
                    }
                    // KSP Orbit.getRelativePositionAtUT(ut) 内部经 Planetarium.Zup.WorldToLocal 求解，
                    // 输出已处于 Z-Up (黄道北极法向) 惯性直角坐标系：
                    // X=春分点, Y=黄道面内90°, Z=黄道北极法向，与 PyKEP 标准 ECLIPJ2000 完全一致 (严禁交换 Y 与 Z)
                    posKm = new Vector3d(r_total.x / 1000.0, r_total.y / 1000.0, r_total.z / 1000.0);
                    velKms = new Vector3d(v_total.x / 1000.0, v_total.y / 1000.0, v_total.z / 1000.0);
                    return;
                } catch { }
            }

            // 1.1 若在主线程且 FlightGlobals 就绪，优先调用 Principia 方案 B 真实星历与未篡改轨道
            if (SolarSystemCatalog.IsFlightGlobalsReady && SolarSystemCatalog.IsMainThread) {
                try {
                    var b = FlightGlobals.Bodies.Find(x => 
                        x.name.Equals(Name, StringComparison.OrdinalIgnoreCase) || 
                        x.bodyName.Equals(Name, StringComparison.OrdinalIgnoreCase));
                    if (b != null) {
                        ActiveEvaluationScheme = EphemerisScheme.Scheme2_FlightGlobals;
                        if (PrincipiaInterop.TryGetHeliocentricState(b, ut, out posKm, out velKms)) {
                            // 同步回填 OrbitChain (基于未篡改轨道)，保证多线程后续查询 100% 免疫发散
                            var chain = new List<Orbit>();
                            var curr = b;
                            while (curr != null && curr.referenceBody != null && curr.referenceBody != curr) {
                                var currOrb = PrincipiaInterop.GetUnmodifiedOrbit(curr) ?? curr.orbit;
                                if (currOrb != null) chain.Add(currOrb);
                                curr = curr.referenceBody;
                            }
                            OrbitChain = chain;
                            return;
                        }
                    }
                } catch { }
            }

            // 2. 方案 2 失败时，自动平滑回退 方案 1: NASA JPL SPICE 数值星历 (DE440 切比雪夫多项式插值)
            // 仅对 SPICE 内核支持的真实太阳系标准天体查询，避免虚构/KSP天体产生 IDCODENOTFOUND
            ActiveEvaluationScheme = EphemerisScheme.Scheme1_SpiceDE440;
            if (PykepNative.HasSpiceLoaded && SolarSystemCatalog.StandardSolarBodyNames.Contains(Name)) {
                if (PykepNative.GetSpicePlanetState(Name, ut, "J2000", out posKm, out velKms)) {
                    posKm = SolarSystemCatalog.AlignSpiceJ2000ToPrincipia(posKm);
                    velKms = SolarSystemCatalog.AlignSpiceJ2000ToPrincipia(velKms);
                    return;
                }
            }

            // 3. 独立后备星历: 方案 1 根数外推 (如果 SPICE 内核未载入，通过原生 C++ propagate_lagrangian 求解)
            //    (开普勒牛顿迭代 + par2ic + propagate_lagrangian, 0 托管自编公式, 基线7.0)
            if (PykepNative.IsAvailable && SMA > 0) {
                if (PykepNative.ElementsToState(
                        SMA, ECC, INC * Mathf.Deg2Rad, RAAN * Mathf.Deg2Rad, AOP * Mathf.Deg2Rad,
                        MA0 * Mathf.Deg2Rad, EpochJ2000, ut, SolarSystemCatalog.SunMu,
                        out posKm, out velKms)) {
                    return;
                }
                KSPGMATLog.Warn("Ephemeris", $"PykepNative 原生根数外推失败, 返回零状态: {Name}");
            }

            // 4. 原生不可用: 返回零状态降级 (严禁托管开普勒推导)
            posKm = Vector3d.zero;
            velKms = Vector3d.zero;
        }

        /// <summary>
        /// 计算指定 UT 时刻相对直接母星 (Parent Body) 的中心天体惯性系状态 (位置 km, 速度 km/s)
        /// 100% 线程安全，消解日心假定，直接用于行星系内转移 (如地月转移、土星/木星卫星巡查)
        /// </summary>
        public void GetParentCentricState(double ut, out Vector3d posKm, out Vector3d velKms) {
            // 1. 方案 2 (第一优先级): OrbitChain[0] 是卫星围绕直接父天体的轨道
            if (OrbitChain != null && OrbitChain.Count > 0) {
                try {
                    var orb = OrbitChain[0];
                    if (orb != null) {
                        Vector3d r_rel = orb.getRelativePositionAtUT(ut);
                        Vector3d v_rel = orb.getOrbitalVelocityAtUT(ut);
                        posKm = new Vector3d(r_rel.x / 1000.0, r_rel.y / 1000.0, r_rel.z / 1000.0);
                        velKms = new Vector3d(v_rel.x / 1000.0, v_rel.y / 1000.0, v_rel.z / 1000.0);
                        return;
                    }
                } catch { }
            }

            // 1.1 若在主线程且 FlightGlobals 就绪，直接提取游戏内天体围绕直接母星的相对开普勒状态
            if (SolarSystemCatalog.IsFlightGlobalsReady && SolarSystemCatalog.IsMainThread) {
                try {
                    var b = FlightGlobals.Bodies.Find(x => 
                        x.name.Equals(Name, StringComparison.OrdinalIgnoreCase) || 
                        x.bodyName.Equals(Name, StringComparison.OrdinalIgnoreCase));
                    if (b != null && b.orbit != null) {
                        var orb = PrincipiaInterop.GetUnmodifiedOrbit(b) ?? b.orbit;
                        if (orb != null) {
                            Vector3d r_rel = orb.getRelativePositionAtUT(ut);
                            Vector3d v_rel = orb.getOrbitalVelocityAtUT(ut);
                            posKm = new Vector3d(r_rel.x / 1000.0, r_rel.y / 1000.0, r_rel.z / 1000.0);
                            velKms = new Vector3d(v_rel.x / 1000.0, v_rel.y / 1000.0, v_rel.z / 1000.0);
                            return;
                        }
                    }
                } catch { }
            }

            // 1.2 方案 1 (第二优先级): 若已加载 SPICE DE440 且天体为真实太阳系标准天体
            if (PykepNative.HasSpiceLoaded && SolarSystemCatalog.StandardSolarBodyNames.Any(s => s.Equals(Name, StringComparison.OrdinalIgnoreCase))) {
                string pName = !string.IsNullOrEmpty(ParentName) ? ParentName : "Earth";
                if (PykepNative.GetSpicePlanetState(Name, ut, "J2000", out Vector3d pChild, out Vector3d vChild)) {
                    Vector3d pParent = Vector3d.zero, vParent = Vector3d.zero;
                    bool parentOk = true;
                    if (!string.IsNullOrEmpty(pName) && !pName.Equals("Sun", StringComparison.OrdinalIgnoreCase) && !pName.Equals("Sol", StringComparison.OrdinalIgnoreCase)) {
                        parentOk = PykepNative.GetSpicePlanetState(pName, ut, "J2000", out pParent, out vParent);
                    }
                    if (parentOk) {
                        posKm = SolarSystemCatalog.AlignSpiceJ2000ToPrincipia(pChild - pParent);
                        velKms = SolarSystemCatalog.AlignSpiceJ2000ToPrincipia(vChild - vParent);
                        return;
                    }
                }
            }

            // 2. 方案 1/离线兜底: 依据天体自身相对于父天体的开普勒根数，由 PyKEP 原生 C++ 解算
            double parentMu = SolarSystemCatalog.GetCentralMu(ParentName);
            if (PykepNative.IsAvailable && SMA > 0) {
                if (PykepNative.ElementsToState(
                        SMA, ECC, INC * Mathf.Deg2Rad, RAAN * Mathf.Deg2Rad, AOP * Mathf.Deg2Rad,
                        MA0 * Mathf.Deg2Rad, EpochJ2000, ut, parentMu,
                        out posKm, out velKms)) {
                    return;
                }
            }

            posKm = Vector3d.zero;
            velKms = Vector3d.zero;
        }

        /// <summary>
        /// 统一星历提取门禁：依据任务所属的中心天体系统 (centralBody) 自适应解析空间状态
        /// 若中心天体是太阳/日心，返回日心状态；若处于行星卫星系，返回相对母星的质心状态
        /// </summary>
        public void GetRelativeState(string centralBody, double ut, out Vector3d posKm, out Vector3d velKms) {
            // 特殊特判：如果查询的天体恰好就是中心母星本身 (相对自身的物理质心恒为绝对原点)
            if (string.IsNullOrEmpty(Name) || Name.Equals(centralBody, StringComparison.OrdinalIgnoreCase)) {
                posKm = Vector3d.zero;
                velKms = Vector3d.zero;
                return;
            }

            bool isHelio = string.IsNullOrEmpty(centralBody) || 
                           centralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                           centralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) || 
                           centralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

            if (isHelio) {
                GetHeliocentricState(ut, out posKm, out velKms);
            } else {
                GetParentCentricState(ut, out posKm, out velKms);
            }
        }

        /// <summary>
        /// 天体引力势阱特征深度 (km^2/s^2)，反映安全近心点处的引力势能深度 Phi_safe = Mu / SafeRadiusKm
        /// </summary>
        public double GravityPotentialDepth {
            get {
                double rSafe = SafeRadiusKm > 0 ? SafeRadiusKm : (RadiusKm > 0 ? RadiusKm + 100.0 : 6000.0);
                return (Mu > 0 && rSafe > 0) ? Mu / rSafe : 0.0;
            }
        }

        /// <summary>
        /// 给定进入双曲过剩速度 vInf (km/s) 下，天体无动力双曲飞越的最大几何偏转角 (deg)
        /// delta_max = 2 * arcsin(1 / (1 + r_safe * v_inf^2 / Mu))
        /// </summary>
        public double ComputeMaxDeflectionDeg(double vInfKms) {
            if (vInfKms <= 1e-6 || Mu <= 0.0) return 0.0;
            double rSafe = SafeRadiusKm > 0 ? SafeRadiusKm : (RadiusKm > 0 ? RadiusKm + 100.0 : 6000.0);
            double e = 1.0 + (rSafe * vInfKms * vInfKms) / Mu;
            if (e <= 1.0) return 180.0;
            double sinHalfDelta = 1.0 / e;
            if (sinHalfDelta > 1.0) sinHalfDelta = 1.0;
            return 2.0 * Math.Asin(sinHalfDelta) * (180.0 / Math.PI);
        }

        /// <summary>
        /// 给定双曲过剩速度 vInf (km/s) 下，纯双曲弹弓可获得的最大日心速度增量极值 (km/s) (仅启发式评估)
        /// DeltaV_boost_max = 2 * v_inf * sin(delta_max / 2) = 2 * v_inf / (1 + r_safe * v_inf^2 / Mu)
        /// </summary>
        public double ComputeMaxHeliocentricBoostKms(double vInfKms) {
            if (vInfKms <= 1e-6 || Mu <= 0.0) return 0.0;
            double rSafe = SafeRadiusKm > 0 ? SafeRadiusKm : (RadiusKm > 0 ? RadiusKm + 100.0 : 6000.0);
            double denom = 1.0 + (rSafe * vInfKms * vInfKms) / Mu;
            return denom > 0 ? (2.0 * vInfKms) / denom : 0.0;
        }
    }
}
