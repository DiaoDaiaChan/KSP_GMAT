using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPGMAT.MGA {
    /// <summary>
    /// MGA 单个转移弧段求解明细 (Single Transfer Leg Result)
    /// </summary>
    public class MGALegResult {
        public int LegIndex { get; set; }
        public string FromBody { get; set; }
        public string ToBody { get; set; }
        public double DepartureUT { get; set; }
        public double ArrivalUT { get; set; }
        public double ToFDays { get; set; }
        public double DeltaVKms { get; set; }
        public Vector3d DeparturePosKm { get; set; }
        public Vector3d ArrivalPosKm { get; set; }
        public Vector3d DepartureVelKms { get; set; }
        public Vector3d ArrivalVelKms { get; set; }
        
        // Maneuver Vector Components in Local Orbital Frame (m/s)
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }

        // Interplanetary Ejection & Insertion Flight Mechanics Parameters
        public double PhaseAngleDeg { get; set; }
        public double EjectionAngleDeg { get; set; }
        public double EjectionLANDeg { get; set; }
        public double EjectionInclinationDeg { get; set; }
        public double InsertionInclinationDeg { get; set; }
        public Vector3d VInfDepartureKms { get; set; }
        public Vector3d VInfArrivalKms { get; set; }
        public double C3Kms2 { get; set; }
        public double EjectionDeltaVKms { get; set; }
        public double InsertionDeltaVKms { get; set; }

        public double FlybyTurningAngleDeg { get; set; }
        public double FlybyPeriapsisRadiusKm { get; set; }
        public double FlybyPeriapsisAltitudeKm { get; set; }
        public double FlybyDeltaVKms { get; set; }
        public bool IsFeasible { get; set; }
    }

    /// <summary>
    /// MGA 事件类型枚举 (100% 对齐 KSP-MGA-Planner)
    /// </summary>
    public enum MGAEventType {
        Escape,          // 1: <Origin> escape (出发脱离机动)
        DSM,             // 2: <From>-<To> DSM (深空变轨/中途机动)
        Flyby,           // 3: <Body> flyby (经过天体的引力助推飞掠)
        Circularization  // 4: <Destination> circularization / capture (目标行星捕获/圆化机动)
    }

    /// <summary>
    /// MGA 离散任务事件与机动节点 (Discrete MGA Mission Event & Maneuver)
    /// </summary>
    public class MGAEventItem {
        public int SequenceNumber { get; set; }
        public string Title { get; set; }
        public MGAEventType EventType { get; set; }
        public string BodyName { get; set; }
        public string FromBody { get; set; }
        public string ToBody { get; set; }
        public double EpochUT { get; set; }
        public double DeltaVMs { get; set; }
        
        // 3 轴机动矢量分量 (m/s)
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }
        
        // 飞掠引力助推特征参数 (Flyby Geometry)
        public double FlybyTurningAngleDeg { get; set; }
        public double FlybyPeriapsisRadiusKm { get; set; }
        public double FlybyPeriapsisAltitudeKm { get; set; }
        public double VInfInKms { get; set; }
        public double VInfOutKms { get; set; }
        public bool IsFeasible { get; set; } = true;
        
        // 出发脱离特征参数 (Escape / Ejection Geometry)
        public double PhaseAngleDeg { get; set; }
        public double EjectionAngleDeg { get; set; }
        public double EjectionLANDeg { get; set; }
        public double EjectionInclinationDeg { get; set; }
        public double C3Kms2 { get; set; }
        
        // 目标捕获特征参数 (Capture Geometry)
        public double InsertionAltitudeKm { get; set; }
        public double InsertionInclinationDeg { get; set; }
    }

    /// <summary>
    /// MGA 全局多重引力助推轨道规划完整方案 (Complete MGA Trajectory Solution)
    /// </summary>
    public class MGASolution {
        public double TotalDeltaVKms { get; set; }
        public double TotalFlightDays { get; set; }
        public double DepartureUT { get; set; }
        public double ArrivalUT { get; set; }
        public double LaunchDeltaVKms { get; set; }
        public double InsertionDeltaVKms { get; set; }
        public List<MGALegResult> Legs { get; set; } = new List<MGALegResult>();
        public List<MGAEventItem> Events { get; set; } = new List<MGAEventItem>();
        public bool AllLegsFeasible { get; set; }
        public string FlybyChainSummary { get; set; }

        public void BuildEvents(MGAConfig config) {
            Events.Clear();
            if (Legs == null || Legs.Count == 0) return;

            int seq = 1;
            var leg0 = Legs[0];

            // 1. 出发逃逸机动 (Origin Escape)
            Events.Add(new MGAEventItem {
                SequenceNumber = seq,
                Title = $"{seq}: {leg0.FromBody} escape",
                EventType = MGAEventType.Escape,
                BodyName = leg0.FromBody,
                FromBody = leg0.FromBody,
                ToBody = leg0.ToBody,
                EpochUT = DepartureUT,
                DeltaVMs = LaunchDeltaVKms * 1000.0,
                ProgradeMs = leg0.ProgradeMs,
                NormalMs = leg0.NormalMs,
                RadialMs = leg0.RadialMs,
                PhaseAngleDeg = leg0.PhaseAngleDeg,
                EjectionAngleDeg = leg0.EjectionAngleDeg,
                EjectionLANDeg = leg0.EjectionLANDeg,
                EjectionInclinationDeg = leg0.EjectionInclinationDeg,
                C3Kms2 = leg0.C3Kms2,
                VInfOutKms = leg0.VInfDepartureKms.magnitude,
                IsFeasible = true
            });
            seq++;

            // 2. 依次构建中间飞掠与深空机动 (Flybys & DSMs)
            for (int k = 1; k < Legs.Count; k++) {
                var leg = Legs[k];
                var prevLeg = Legs[k - 1];

                // 2.1 天体引力助推飞掠事件 (Flyby)
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {leg.FromBody} flyby",
                    EventType = MGAEventType.Flyby,
                    BodyName = leg.FromBody,
                    FromBody = prevLeg.FromBody,
                    ToBody = leg.ToBody,
                    EpochUT = leg.DepartureUT,
                    DeltaVMs = leg.FlybyDeltaVKms * 1000.0,
                    ProgradeMs = leg.ProgradeMs,
                    NormalMs = leg.NormalMs,
                    RadialMs = leg.RadialMs,
                    FlybyTurningAngleDeg = leg.FlybyTurningAngleDeg,
                    FlybyPeriapsisRadiusKm = leg.FlybyPeriapsisRadiusKm,
                    FlybyPeriapsisAltitudeKm = leg.FlybyPeriapsisAltitudeKm,
                    VInfInKms = prevLeg.VInfArrivalKms.magnitude,
                    VInfOutKms = leg.VInfDepartureKms.magnitude,
                    IsFeasible = leg.IsFeasible
                });
                seq++;

                // 2.2 如果需要深空机动动力修正 (DSM)
                if (leg.FlybyDeltaVKms > 0.001) {
                    Events.Add(new MGAEventItem {
                        SequenceNumber = seq,
                        Title = $"{seq}: {leg.FromBody}-{leg.ToBody} DSM",
                        EventType = MGAEventType.DSM,
                        BodyName = leg.FromBody,
                        FromBody = leg.FromBody,
                        ToBody = leg.ToBody,
                        EpochUT = leg.DepartureUT,
                        DeltaVMs = leg.FlybyDeltaVKms * 1000.0,
                        ProgradeMs = leg.ProgradeMs,
                        NormalMs = leg.NormalMs,
                        RadialMs = leg.RadialMs,
                        IsFeasible = leg.IsFeasible
                    });
                    seq++;
                }
            }

            // 3. 目标天体到达 (Destination Arrival)
            var lastLeg = Legs.Last();
            if (config != null && config.NoInsertionBurn) {
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {lastLeg.ToBody} flyby",
                    EventType = MGAEventType.Flyby,
                    BodyName = lastLeg.ToBody,
                    FromBody = lastLeg.FromBody,
                    ToBody = lastLeg.ToBody,
                    EpochUT = ArrivalUT,
                    DeltaVMs = 0.0,
                    VInfInKms = lastLeg.VInfArrivalKms.magnitude,
                    IsFeasible = true
                });
            } else {
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {lastLeg.ToBody} circularization",
                    EventType = MGAEventType.Circularization,
                    BodyName = lastLeg.ToBody,
                    FromBody = lastLeg.FromBody,
                    ToBody = lastLeg.ToBody,
                    EpochUT = ArrivalUT,
                    DeltaVMs = InsertionDeltaVKms * 1000.0,
                    ProgradeMs = -InsertionDeltaVKms * 1000.0,
                    NormalMs = 0.0,
                    RadialMs = 0.0,
                    VInfInKms = lastLeg.VInfArrivalKms.magnitude,
                    InsertionAltitudeKm = (config != null) ? config.DestinationAltitudeKm : 200.0,
                    InsertionInclinationDeg = lastLeg.InsertionInclinationDeg,
                    IsFeasible = true
                });
            }
        }
    }

    /// <summary>
    /// MGA 规划配置输入 (100% 对齐 KSP-MGA-Planner)
    /// </summary>
    public class MGAConfig {
        public string DepartureBody { get; set; } = "Earth";
        public string DestinationBody { get; set; } = "Saturn";
        public List<string> FlybySequence { get; set; } = new List<string> { "Venus", "Venus", "Earth", "Jupiter", "Saturn" };
        
        public int MaxSwingBys { get; set; } = 4;
        public int MaxResonances { get; set; } = 1;
        public int MaxBackLegs { get; set; } = 1;
        public int MaxBackSpacing { get; set; } = 2;

        public double EarliestDepartureUT { get; set; }
        public double LatestDepartureUT { get; set; }
        
        public double DepartureAltitudeKm { get => DeparturePeAKm; set => DeparturePeAKm = value; }
        public double DeparturePeAKm { get; set; } = 200.0;
        public double DepartureApAKm { get; set; } = 200.0;
        public double DepartureInclinationDeg { get; set; } = 0.0;

        public double DestinationAltitudeKm { get => DestinationPeAKm; set => DestinationPeAKm = value; }
        public double DestinationPeAKm { get; set; } = 200.0;
        public double DestinationApAKm { get; set; } = 200.0;
        public bool NoInsertionBurn { get; set; } = false;

        public double MaxDurationDays { get; set; } = 4000.0;
        public bool EnableMaxDuration { get; set; } = false;

        public List<double> MinToFDays { get; set; } = new List<double>();
        public List<double> MaxToFDays { get; set; } = new List<double>();
        public int PopulationSize { get; set; } = 60;
        public int MaxGenerations { get; set; } = 50;
        public bool AllowPoweredFlyby { get; set; } = true;
    }

    // =========================================================================
    // NATIVE C-API P/INVOKE STRUCTURES (Aligned with pykep_bridge.cpp)
    // =========================================================================

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativePlanetState {
        public double rx, ry, rz; // km
        public double vx, vy, vz; // km/s
        public double mu;         // km^3/s^2
        public double radius;     // km
        public double safe_radius;// km
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeLegResult {
        public double tof_sec;
        public double r1x, r1y, r1z;
        public double r2x, r2y, r2z;
        public double v1x, v1y, v1z;
        public double v2x, v2y, v2z;
        public double v_inf_dep_x, v_inf_dep_y, v_inf_dep_z;
        public double v_inf_arr_x, v_inf_arr_y, v_inf_arr_z;
        public double flyby_turn_deg;
        public double flyby_rp_km;
        public double flyby_dv_kms;
        public double prograde_ms;
        public double normal_ms;
        public double radial_ms;
        public int is_feasible;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeMGASolution {
        public double launch_dv_kms;
        public double insertion_dv_kms;
        public double total_dv_kms;
        public double total_flight_days;
        public double c3_kms2;
        public double ejection_angle_deg;
        public double ejection_inc_deg;
        public double ejection_lan_deg;
        public int all_feasible;
        public int leg_count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public NativeLegResult[] legs;
    }

    /// <summary>
    /// 纯粹的 PyKEP 官方 C++ 原生动力学桥接层与数据转换器
    /// (Pure Native PyKEP Bridge & Data Marshaler - 0 Custom Math Calculation in C#)
    /// </summary>
    public static class PykepBridge {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMGAEvaluateDelegate(
            [In] NativePlanetState[] dep_planets,
            [In] NativePlanetState[] arr_planets,
            [In] double[] tofs_sec,
            int leg_count,
            double dep_pe_km, double dep_ap_km,
            double arr_pe_km, double arr_ap_km,
            int no_insertion,
            out NativeMGASolution out_sol);

        private static PykepMGAEvaluateDelegate _fnMGAEvaluate;

        private static bool _patchFormulaBlockedLogged = false;

        /// <summary>
        /// 基线7: C# 二体ΔV近似公式仅允许在显式开启托管回退时使用 (每会话仅记录一次禁用警告)。
        /// </summary>
        private static void LogPatchFormulaBlocked() {
            if (_patchFormulaBlockedLogged) return;
            _patchFormulaBlockedLogged = true;
            KSPGMATLog.Error("C# 二体ΔV近似公式已被基线7禁用，仅 AllowManagedFallback=true 时可作显式近似使用");
        }

        static PykepBridge() {
            try {
                if (PykepNative.IsAvailable) {
                    IntPtr pEval = PykepNative.GetProcAddressExport("pykep_mga_evaluate");
                    if (pEval != IntPtr.Zero) {
                        _fnMGAEvaluate = (PykepMGAEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pEval, typeof(PykepMGAEvaluateDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_evaluate C++ pipeline!");
                    }
                }
            } catch (Exception ex) {
                KSPGMATLog.Warn($"[PykepBridge] Native MGA bridge linkage note: {ex.Message}");
            }
        }

        public static double CalculateDepartureDeltaV(double vInfDepKms, SolarPlanet planet, double peAKm, double apAKm) {
            if (!PykepNative.AllowManagedFallback) {
                LogPatchFormulaBlocked();
                return double.NaN;
            }
            if (planet == null || planet.Mu <= 0) return 0.0;
            double rp = planet.RadiusKm + Math.Max(0.0, peAKm);
            double ra = planet.RadiusKm + Math.Max(peAKm, apAKm);
            double aPark = (rp + ra) * 0.5;
            double vPark = Math.Sqrt(planet.Mu * (2.0 / rp - 1.0 / aPark));
            double vHypPeri = Math.Sqrt(vInfDepKms * vInfDepKms + 2.0 * planet.Mu / rp);
            return Math.Max(0.0, vHypPeri - vPark);
        }

        public static double CalculateArrivalInsertionDeltaV(double vInfArrKms, SolarPlanet planet, double peAKm, double apAKm, bool flybyOnly) {
            if (!PykepNative.AllowManagedFallback) {
                LogPatchFormulaBlocked();
                return double.NaN;
            }
            if (flybyOnly || planet == null || planet.Mu <= 0) return 0.0;
            double rp = planet.RadiusKm + Math.Max(0.0, peAKm);
            double ra = planet.RadiusKm + Math.Max(peAKm, apAKm);
            double aTarget = (rp + ra) * 0.5;
            double vTargetPeri = Math.Sqrt(planet.Mu * (2.0 / rp - 1.0 / aTarget));
            double vHypPeri = Math.Sqrt(vInfArrKms * vInfArrKms + 2.0 * planet.Mu / rp);
            return Math.Max(0.0, vHypPeri - vTargetPeri);
        }

        /// <summary>
        /// 原生调用 PyKEP C++ 评估多段 MGA 轨道 (数据封送并直接解算)
        /// </summary>
        public static MGASolution BuildFullSolution(double[] genome, List<string> fullChain, MGAConfig config) {
            if (genome == null || fullChain == null || fullChain.Count < 2) return null;
            int legCount = fullChain.Count - 1;
            if (legCount > 16) return null;

            var depPlanets = new NativePlanetState[legCount];
            var arrPlanets = new NativePlanetState[legCount];
            var tofs = new double[legCount];

            double currentUT = genome[0];

            for (int k = 0; k < legCount; k++) {
                string fromName = fullChain[k];
                string toName = fullChain[k + 1];
                var p1 = SolarSystemCatalog.GetPlanet(fromName);
                var p2 = SolarSystemCatalog.GetPlanet(toName);

                double tof = genome[1 + k];
                tofs[k] = tof;
                double arrUT = currentUT + tof;

                p1.GetHeliocentricState(currentUT, out Vector3d r1, out Vector3d v1);
                p2.GetHeliocentricState(arrUT, out Vector3d r2, out Vector3d v2);

                depPlanets[k] = new NativePlanetState {
                    rx = r1.x, ry = r1.y, rz = r1.z,
                    vx = v1.x, vy = v1.y, vz = v1.z,
                    mu = p1.Mu, radius = p1.RadiusKm, safe_radius = p1.SafeRadiusKm
                };

                arrPlanets[k] = new NativePlanetState {
                    rx = r2.x, ry = r2.y, rz = r2.z,
                    vx = v2.x, vy = v2.y, vz = v2.z,
                    mu = p2.Mu, radius = p2.RadiusKm, safe_radius = p2.SafeRadiusKm
                };

                currentUT = arrUT;
            }

            if (_fnMGAEvaluate != null) {
                int status = _fnMGAEvaluate(
                    depPlanets, arrPlanets, tofs, legCount,
                    config.DeparturePeAKm, config.DepartureApAKm,
                    config.DestinationPeAKm, config.DestinationApAKm,
                    config.NoInsertionBurn ? 1 : 0,
                    out NativeMGASolution nativeSol);

                if (status == 0) {
                    return MapNativeSolution(nativeSol, genome[0], fullChain, config);
                }
            }

            // 原生 MGA 桥不可用或评估失败 -> 基线7门控: 默认禁止托管近似回退
            if (!PykepNative.AllowManagedFallback) {
                PykepNative.ReportManagedFallbackBlocked();
                return null;
            }
            return ManagedMGAEvaluate(depPlanets, arrPlanets, tofs, genome[0], fullChain, config);
        }

        private static MGASolution MapNativeSolution(NativeMGASolution nSol, double startUT, List<string> fullChain, MGAConfig config) {
            var sol = new MGASolution {
                DepartureUT = startUT,
                ArrivalUT = startUT + nSol.total_flight_days * 86400.0,
                TotalFlightDays = nSol.total_flight_days,
                LaunchDeltaVKms = nSol.launch_dv_kms,
                InsertionDeltaVKms = nSol.insertion_dv_kms,
                TotalDeltaVKms = nSol.total_dv_kms,
                AllLegsFeasible = nSol.all_feasible != 0,
                FlybyChainSummary = string.Join("  ->  ", fullChain)
            };

            double curUT = startUT;
            for (int k = 0; k < nSol.leg_count; k++) {
                var nl = nSol.legs[k];
                double arrUT = curUT + nl.tof_sec;

                var leg = new MGALegResult {
                    LegIndex = k,
                    FromBody = fullChain[k],
                    ToBody = fullChain[k + 1],
                    DepartureUT = curUT,
                    ArrivalUT = arrUT,
                    ToFDays = nl.tof_sec / 86400.0,
                    DeparturePosKm = new Vector3d(nl.r1x, nl.r1y, nl.r1z),
                    ArrivalPosKm = new Vector3d(nl.r2x, nl.r2y, nl.r2z),
                    DepartureVelKms = new Vector3d(nl.v1x, nl.v1y, nl.v1z),
                    ArrivalVelKms = new Vector3d(nl.v2x, nl.v2y, nl.v2z),
                    VInfDepartureKms = new Vector3d(nl.v_inf_dep_x, nl.v_inf_dep_y, nl.v_inf_dep_z),
                    VInfArrivalKms = new Vector3d(nl.v_inf_arr_x, nl.v_inf_arr_y, nl.v_inf_arr_z),
                    FlybyTurningAngleDeg = nl.flyby_turn_deg,
                    FlybyPeriapsisRadiusKm = nl.flyby_rp_km,
                    FlybyPeriapsisAltitudeKm = nl.flyby_rp_km - SolarSystemCatalog.GetPlanet(fullChain[k]).RadiusKm,
                    FlybyDeltaVKms = nl.flyby_dv_kms,
                    ProgradeMs = nl.prograde_ms,
                    NormalMs = nl.normal_ms,
                    RadialMs = nl.radial_ms,
                    IsFeasible = nl.is_feasible != 0
                };

                if (k == 0) {
                    leg.C3Kms2 = nSol.c3_kms2;
                    leg.EjectionDeltaVKms = nSol.launch_dv_kms;
                    leg.EjectionAngleDeg = nSol.ejection_angle_deg;
                    leg.EjectionInclinationDeg = nSol.ejection_inc_deg;
                    leg.EjectionLANDeg = nSol.ejection_lan_deg;
                    leg.PhaseAngleDeg = (Math.Atan2(nl.r2y, nl.r2x) - Math.Atan2(nl.r1y, nl.r1x)) * Mathf.Rad2Deg;
                    if (leg.PhaseAngleDeg < 0) leg.PhaseAngleDeg += 360.0;
                } else if (k == nSol.leg_count - 1) {
                    leg.InsertionDeltaVKms = nSol.insertion_dv_kms;
                }

                sol.Legs.Add(leg);
                curUT = arrUT;
            }

            sol.BuildEvents(config);
            return sol;
        }

        private static MGASolution ManagedMGAEvaluate(
            NativePlanetState[] depPlanets, NativePlanetState[] arrPlanets, double[] tofs,
            double startUT, List<string> fullChain, MGAConfig config)
        {
            // 基线7: 托管近似评估仅在显式开启回退时允许
            if (!PykepNative.AllowManagedFallback) {
                PykepNative.ReportManagedFallbackBlocked();
                return null;
            }
            int legCount = depPlanets.Length;
            var sol = new MGASolution {
                DepartureUT = startUT,
                AllLegsFeasible = true,
                FlybyChainSummary = string.Join("  ->  ", fullChain)
            };

            double curUT = startUT;
            double totalSec = 0.0;
            Vector3d lastVOut = Vector3d.zero;
            double totalDV = 0.0;

            for (int k = 0; k < legCount; k++) {
                double tof = tofs[k];
                totalSec += tof;
                double arrUT = curUT + tof;

                var p1 = depPlanets[k];
                var p2 = arrPlanets[k];

                Vector3d r1 = new Vector3d(p1.rx, p1.ry, p1.rz);
                Vector3d r2 = new Vector3d(p2.rx, p2.ry, p2.rz);
                Vector3d vp1 = new Vector3d(p1.vx, p1.vy, p1.vz);
                Vector3d vp2 = new Vector3d(p2.vx, p2.vy, p2.vz);

                PykepNative.SolveLambert(r1, r2, tof, SolarSystemCatalog.SunMu, false, out Vector3d v1, out Vector3d v2);

                var leg = new MGALegResult {
                    LegIndex = k,
                    FromBody = fullChain[k],
                    ToBody = fullChain[k + 1],
                    DepartureUT = curUT,
                    ArrivalUT = arrUT,
                    ToFDays = tof / 86400.0,
                    DeparturePosKm = r1,
                    ArrivalPosKm = r2,
                    DepartureVelKms = v1,
                    ArrivalVelKms = v2,
                    VInfDepartureKms = v1 - vp1,
                    VInfArrivalKms = v2 - vp2,
                    IsFeasible = true
                };

                double vInfDepMag = leg.VInfDepartureKms.magnitude;

                if (k == 0) {
                    leg.C3Kms2 = vInfDepMag * vInfDepMag;
                    sol.LaunchDeltaVKms = CalculateDepartureDeltaV(vInfDepMag, SolarSystemCatalog.GetPlanet(fullChain[0]), config.DeparturePeAKm, config.DepartureApAKm);
                    leg.EjectionDeltaVKms = sol.LaunchDeltaVKms;
                    totalDV += sol.LaunchDeltaVKms;

                    leg.ProgradeMs = sol.LaunchDeltaVKms * 1000.0;
                    leg.NormalMs = 0.0;
                    leg.RadialMs = 0.0;

                    double dec = Math.Asin(Math.Max(-1.0, Math.Min(1.0, leg.VInfDepartureKms.z / Math.Max(1e-6, vInfDepMag)))) * Mathf.Rad2Deg;
                    double ra = Math.Atan2(leg.VInfDepartureKms.y, leg.VInfDepartureKms.x) * Mathf.Rad2Deg;
                    if (ra < 0) ra += 360.0;
                    leg.EjectionInclinationDeg = Math.Abs(dec);
                    leg.EjectionLANDeg = ra;

                    double rPark = p1.radius + config.DeparturePeAKm;
                    double eHyp = 1.0 + (rPark * vInfDepMag * vInfDepMag) / Math.Max(1e-6, p1.mu);
                    double beta = Math.Acos(-1.0 / Math.Max(1.0001, eHyp)) * Mathf.Rad2Deg;
                    double ejAng = 180.0 - (beta - 90.0);
                    if (ejAng > 180.0) ejAng = 360.0 - ejAng;
                    leg.EjectionAngleDeg = ejAng;
                    leg.PhaseAngleDeg = (Math.Atan2(r2.y, r2.x) - Math.Atan2(r1.y, r1.x)) * Mathf.Rad2Deg;
                    if (leg.PhaseAngleDeg < 0) leg.PhaseAngleDeg += 360.0;
                } else {
                    Vector3d vIn = lastVOut;
                    Vector3d vOut = leg.VInfDepartureKms;

                    bool feasible = PykepNative.EvaluateFlyby(
                        vIn, vOut, p1.mu, p1.safe_radius,
                        out double turnDeg, out double rpKm, out double flybyDV);

                    leg.FlybyTurningAngleDeg = turnDeg;
                    leg.FlybyPeriapsisRadiusKm = rpKm;
                    leg.FlybyPeriapsisAltitudeKm = rpKm - p1.radius;
                    leg.FlybyDeltaVKms = flybyDV;
                    leg.IsFeasible = feasible && (rpKm >= p1.safe_radius);
                    if (!leg.IsFeasible) sol.AllLegsFeasible = false;

                    totalDV += flybyDV;

                    if (flybyDV > 0.001) {
                        Vector3d dVVec = vOut - vIn;
                        Vector3d vHat = (vIn.sqrMagnitude > 1e-6) ? vIn.normalized : vp1.normalized;
                        Vector3d nHat = Vector3d.Cross(r1, vHat).normalized;
                        if (nHat.sqrMagnitude < 0.5) nHat = Vector3d.up;
                        Vector3d cHat = Vector3d.Cross(vHat, nHat).normalized;

                        leg.ProgradeMs = Vector3d.Dot(dVVec, vHat) * 1000.0;
                        leg.NormalMs = Vector3d.Dot(dVVec, nHat) * 1000.0;
                        leg.RadialMs = Vector3d.Dot(dVVec, cHat) * 1000.0;
                    }
                }

                lastVOut = leg.VInfArrivalKms;
                sol.Legs.Add(leg);
                curUT = arrUT;
            }

            if (!config.NoInsertionBurn) {
                double vInfArr = lastVOut.magnitude;
                var pDest = SolarSystemCatalog.GetPlanet(fullChain.Last());
                sol.InsertionDeltaVKms = CalculateArrivalInsertionDeltaV(vInfArr, pDest, config.DestinationPeAKm, config.DestinationApAKm, false);
                totalDV += sol.InsertionDeltaVKms;
                sol.Legs.Last().InsertionDeltaVKms = sol.InsertionDeltaVKms;
            }

            sol.ArrivalUT = curUT;
            sol.TotalFlightDays = totalSec / 86400.0;
            sol.TotalDeltaVKms = totalDV;
            sol.BuildEvents(config);
            return sol;
        }

        /// <summary>
        /// 全局多段差分进化寻优器 (基于 PyKEP 原生全流程评估)
        /// </summary>
        public static List<MGASolution> Optimize(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default) {
            var solutions = new List<MGASolution>();
            if (config == null || config.FlybySequence == null || config.FlybySequence.Count == 0) return solutions;

            var fullChain = new List<string> { config.DepartureBody };
            fullChain.AddRange(config.FlybySequence);
            int legCount = fullChain.Count - 1;
            int dim = 1 + legCount;

            double[] lowerBounds = new double[dim];
            double[] upperBounds = new double[dim];

            lowerBounds[0] = config.EarliestDepartureUT;
            upperBounds[0] = Math.Max(config.EarliestDepartureUT + 86400.0 * 30.0, config.LatestDepartureUT);

            double sunMu = SolarSystemCatalog.SunMu;

            for (int k = 0; k < legCount; k++) {
                string fromN = fullChain[k];
                string toN = fullChain[k + 1];
                var p1 = SolarSystemCatalog.GetPlanet(fromN);
                var p2 = SolarSystemCatalog.GetPlanet(toN);

                double minD, maxD;
                if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                    double tOrbDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(p1.SMA, 3) / sunMu) / 86400.0;
                    if (tOrbDays <= 0 || double.IsNaN(tOrbDays)) tOrbDays = 365.25;
                    minD = Math.Max(30.0, tOrbDays * 0.85);
                    maxD = Math.Max(minD + 100.0, tOrbDays * 3.15);
                } else {
                    double aMean = (p1.SMA + p2.SMA) * 0.5;
                    double approxHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aMean, 3) / sunMu) / 86400.0;
                    if (approxHohmannDays <= 0 || double.IsNaN(approxHohmannDays)) approxHohmannDays = 200.0;
                    minD = Math.Max(20.0, approxHohmannDays * 0.35);
                    maxD = Math.Max(minD + 40.0, approxHohmannDays * 2.5);
                }

                if (config.MinToFDays != null && k < config.MinToFDays.Count && config.MinToFDays[k] > 0) minD = config.MinToFDays[k];
                if (config.MaxToFDays != null && k < config.MaxToFDays.Count && config.MaxToFDays[k] > 0) maxD = config.MaxToFDays[k];

                lowerBounds[1 + k] = minD * 86400.0;
                upperBounds[1 + k] = maxD * 86400.0;
            }

            int popSize = Math.Max(60, config.PopulationSize);
            int maxGens = Math.Max(50, config.MaxGenerations);

            double[][] pop = new double[popSize][];
            double[] fitness = new double[popSize];
            var random = new System.Random(42);

            for (int i = 0; i < popSize; i++) {
                pop[i] = new double[dim];
                for (int d = 0; d < dim; d++) {
                    pop[i][d] = lowerBounds[d] + random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                }
                var sol = BuildFullSolution(pop[i], fullChain, config);
                fitness[i] = (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) ? (sol.TotalDeltaVKms + (sol.AllLegsFeasible ? 0.0 : 50.0)) : 1e9;
            }

            for (int gen = 0; gen < maxGens; gen++) {
                if (ct.IsCancellationRequested) break;

                float prog = (float)gen / maxGens;
                onProgress?.Invoke(prog, $"PyKEP 全局差分寻优: {gen + 1} / {maxGens} (种群: {popSize})...");

                Parallel.For(0, popSize, i => {
                    var localRand = new System.Random(gen * popSize + i + 17);

                    int a, b, c;
                    do { a = localRand.Next(popSize); } while (a == i);
                    do { b = localRand.Next(popSize); } while (b == i || b == a);
                    do { c = localRand.Next(popSize); } while (c == i || c == a || c == b);

                    double F = 0.5 + 0.4 * localRand.NextDouble();
                    double CR = 0.7 + 0.2 * localRand.NextDouble();
                    int R = localRand.Next(dim);

                    double[] candidate = new double[dim];
                    for (int d = 0; d < dim; d++) {
                        if (d == R || localRand.NextDouble() < CR) {
                            candidate[d] = pop[a][d] + F * (pop[b][d] - pop[c][d]);
                            candidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], candidate[d]));
                        } else {
                            candidate[d] = pop[i][d];
                        }
                    }

                    var candSol = BuildFullSolution(candidate, fullChain, config);
                    double candFit = (candSol != null && !double.IsNaN(candSol.TotalDeltaVKms)) ? (candSol.TotalDeltaVKms + (candSol.AllLegsFeasible ? 0.0 : 50.0)) : 1e9;
                    if (candFit < fitness[i]) {
                        pop[i] = candidate;
                        fitness[i] = candFit;
                    }
                });
            }

            onProgress?.Invoke(1.0f, "PyKEP 寻优完成，正在输出物理方案...");

            var rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();
            var seenDeparture = new HashSet<int>();

            foreach (var idx in rankedIndices) {
                if (fitness[idx] >= 1e8) continue;
                var sol = BuildFullSolution(pop[idx], fullChain, config);
                if (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) {
                    int keyUT = (int)(sol.DepartureUT / (86400.0 * 3.0));
                    if (!seenDeparture.Contains(keyUT)) {
                        seenDeparture.Add(keyUT);
                        solutions.Add(sol);
                    }
                }
                if (solutions.Count >= 10) break;
            }

            return solutions;
        }

        /// <summary>
        /// 基于行星轨道内外侧拓扑与物理约束自动广度优先生成候选借力序列 (100% 对齐 KSP-MGA-Planner 拓扑搜索算法)
        /// </summary>
        public static List<List<string>> GenerateCandidateSequences(
            string depBody, string destBody, int maxSwingBys = 4, int maxResonances = 1, int maxBackLegs = 1, int maxBackSpacing = 2)
        {
            var cfg = new MGAConfig {
                DepartureBody = depBody,
                DestinationBody = destBody,
                MaxSwingBys = maxSwingBys,
                MaxResonances = maxResonances,
                MaxBackLegs = maxBackLegs,
                MaxBackSpacing = maxBackSpacing
            };
            return GenerateCandidateSequences(cfg);
        }

        public static List<List<string>> GenerateCandidateSequences(MGAConfig config) {
            var results = new List<List<string>>();
            if (config == null || string.IsNullOrEmpty(config.DepartureBody) || string.IsNullOrEmpty(config.DestinationBody))
                return results;

            string origin = config.DepartureBody;
            string dest = config.DestinationBody;

            var originP = SolarSystemCatalog.GetPlanet(origin);
            var destP = SolarSystemCatalog.GetPlanet(dest);

            int oIdx = SolarSystemCatalog.GetPlanetIndex(origin);
            int dIdx = SolarSystemCatalog.GetPlanetIndex(dest);

            if (oIdx < 0 || dIdx < 0) return results;

            var queue = new Queue<(List<string> chain, int resonances, int backLegs)>();
            queue.Enqueue((new List<string> { origin }, 0, 0));

            int maxLegs = config.MaxSwingBys + 1;

            while (queue.Count > 0) {
                var (currentChain, curRes, curBack) = queue.Dequeue();
                string currentBody = currentChain.Last();
                int cIdx = SolarSystemCatalog.GetPlanetIndex(currentBody);

                if (currentChain.Count > 1 && currentBody.Equals(dest, StringComparison.OrdinalIgnoreCase)) {
                    if (!results.Any(r => r.SequenceEqual(currentChain))) {
                        results.Add(new List<string>(currentChain.Skip(1)));
                    }
                    continue;
                }

                if (currentChain.Count >= maxLegs + 1) continue;

                var planetList = SolarSystemCatalog.GetAvailablePlanets();
                for (int nextIdx = 0; nextIdx < planetList.Count; nextIdx++) {
                    var nextP = planetList[nextIdx];
                    string nextBody = nextP.Name;

                    int nextRes = curRes;
                    int nextBack = curBack;

                    if (nextBody.Equals(currentBody, StringComparison.OrdinalIgnoreCase)) {
                        nextRes++;
                        if (nextRes > config.MaxResonances) continue;
                    }

                    if (oIdx < dIdx && nextIdx < cIdx) {
                        nextBack++;
                        if (nextBack > config.MaxBackLegs) continue;
                        if (Math.Abs(cIdx - nextIdx) > config.MaxBackSpacing) continue;
                    } else if (oIdx > dIdx && nextIdx > cIdx) {
                        nextBack++;
                        if (nextBack > config.MaxBackLegs) continue;
                        if (Math.Abs(nextIdx - cIdx) > config.MaxBackSpacing) continue;
                    }

                    var newChain = new List<string>(currentChain) { nextBody };
                    queue.Enqueue((newChain, nextRes, nextBack));
                }
            }

            return results.OrderBy(r => r.Count).ToList();
        }

        public static Task<List<MGASolution>> OptimizeAsync(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default) {
            return Task.Run(() => Optimize(config, onProgress, ct), ct);
        }

        public static double GetHohmannTransferTimeDays(string fromBody, string toBody) {
            CelestialBody cb1 = null, cb2 = null;
            if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                cb1 = FlightGlobals.Bodies.Find(b => b.name.Equals(fromBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(fromBody, StringComparison.OrdinalIgnoreCase));
                cb2 = FlightGlobals.Bodies.Find(b => b.name.Equals(toBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(toBody, StringComparison.OrdinalIgnoreCase));
                if (cb1 != null && cb2 != null && cb1.orbit != null && cb2.orbit != null) {
                    try {
                        // 基线7: 弃用 TWP (global::LambertSolver), 采用解析霍曼转移时间 (基于真实轨道半长轴)
                        // KSP orbit.semiMajorAxis is in meters; convert to km to match SolarSystemCatalog.SunMu (km^3/s^2)
                        double a1 = cb1.orbit.semiMajorAxis / 1000.0;
                        double a2 = cb2.orbit.semiMajorAxis / 1000.0;
                        if (a1 > 0 && a2 > 0) {
                            double aTxLocal = (a1 + a2) * 0.5;
                            return (Math.PI * Math.Sqrt(Math.Pow(aTxLocal, 3) / SolarSystemCatalog.SunMu)) / 86400.0;
                        }
                    } catch { }
                }
            }

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            double sunMu = SolarSystemCatalog.SunMu;
            double aTx = (p1.SMA + p2.SMA) * 0.5;
            double tofSec = Math.PI * Math.Sqrt(Math.Pow(aTx, 3) / sunMu);
            return tofSec / 86400.0;
        }

        public static double GetSynodicPeriodDays(string fromBody, string toBody) {
            CelestialBody cb1 = null, cb2 = null;
            if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                cb1 = FlightGlobals.Bodies.Find(b => b.name.Equals(fromBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(fromBody, StringComparison.OrdinalIgnoreCase));
                cb2 = FlightGlobals.Bodies.Find(b => b.name.Equals(toBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(toBody, StringComparison.OrdinalIgnoreCase));
                if (cb1 != null && cb2 != null && cb1.orbit != null && cb2.orbit != null && cb1.orbit.period > 0 && cb2.orbit.period > 0) {
                    double synSec = Math.Abs(1.0 / (1.0 / cb1.orbit.period - 1.0 / cb2.orbit.period));
                    return synSec / 86400.0;
                }
            }

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            double sunMu = SolarSystemCatalog.SunMu;
            double t1 = 2.0 * Math.PI * Math.Sqrt(Math.Pow(p1.SMA, 3) / sunMu);
            double t2 = 2.0 * Math.PI * Math.Sqrt(Math.Pow(p2.SMA, 3) / sunMu);
            double diff = Math.Abs(t1 - t2);
            if (diff < 1e-6) return 365.25;
            return (t1 * t2 / diff) / 86400.0;
        }

        public static (double bestDepUT, double tofDays, double approxDV) CalculateHohmannWindow(
            string fromBody, string toBody, double searchStartUT, double searchSpanDays = 0) 
        {
            // 基线7: 彻底弃用 TWP (global::LambertSolver)。
            // 窗口相角采用基线7.8解析公式 φ = π − ω2·T_hoh, 会合周期 T_syn = 2π/|ω1 − ω2|;
            // ΔV 评估必须由原生 PyKEP 兰伯特完成, 原生不可用且未开启托管回退时报错。
            if (!PykepNative.IsAvailable && !PykepNative.AllowManagedFallback) {
                PykepNative.ReportManagedFallbackBlocked();
            }

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            double sunMu = SolarSystemCatalog.SunMu;

            // 平均角速度 (rad/s) 与会合周期 (基线7.8)
            double w1 = 2.0 * Math.PI / Math.Max(1e-12, 2.0 * Math.PI * Math.Sqrt(Math.Pow(p1.SMA, 3) / sunMu));
            double w2 = 2.0 * Math.PI / Math.Max(1e-12, 2.0 * Math.PI * Math.Sqrt(Math.Pow(p2.SMA, 3) / sunMu));
            double synodicSec = 2.0 * Math.PI / Math.Max(1e-12, Math.Abs(w1 - w2));
            double synodicDays = synodicSec / 86400.0;

            // 标称霍曼转移时间与所需发射相角 (基线7.8: φ = π − ω2·T_hoh), 折叠入 [0, 2π)
            double aTx = (p1.SMA + p2.SMA) * 0.5;
            double baseTofSec = Math.PI * Math.Sqrt(Math.Pow(aTx, 3) / sunMu);
            double baseTofDays = baseTofSec / 86400.0;

            double reqPhase = Math.PI - w2 * baseTofSec;
            reqPhase = ((reqPhase % (2.0 * Math.PI)) + 2.0 * Math.PI) % (2.0 * Math.PI);

            double scanSpanDays = (searchSpanDays > 0) ? searchSpanDays : (synodicDays * 1.5);
            double scanEndUT = searchStartUT + scanSpanDays * 86400.0;

            // 1) 相角粗扫描: 定位 φ(t) = (θ2 − θ1) mod 2π 最接近 φ 的标称发射窗口
            double nominalUT = searchStartUT;
            double minPhaseErr = double.MaxValue;
            double phaseStep = Math.Max(3600.0, synodicSec / 400.0);
            for (double ut = searchStartUT; ut <= scanEndUT; ut += phaseStep) {
                p1.GetHeliocentricState(ut, out Vector3d posA, out Vector3d velA);
                p2.GetHeliocentricState(ut, out Vector3d posB, out Vector3d velB);
                double phase = (Math.Atan2(posB.y, posB.x) - Math.Atan2(posA.y, posA.x));
                phase = ((phase % (2.0 * Math.PI)) + 2.0 * Math.PI) % (2.0 * Math.PI);
                double err = Math.Abs(phase - reqPhase);
                if (err > Math.PI) err = 2.0 * Math.PI - err;
                if (err < minPhaseErr) {
                    minPhaseErr = err;
                    nominalUT = ut;
                }
            }

            // 2) 精扫: 以标称窗口为中心调用原生 PyKEP 兰伯特评估 ΔV (禁止 TWP 遗留算法)
            double bestUT = nominalUT;
            double bestTofDays = baseTofDays;
            double minDV = double.MaxValue;

            double fineSpanSec = Math.Max(2.0 * 86400.0, synodicSec / 4.0);
            double fineStartUT = nominalUT - fineSpanSec;
            double fineEndUT = nominalUT + fineSpanSec;
            double fineStep = Math.Max(3600.0, synodicSec / 200.0);

            for (double ut = fineStartUT; ut <= fineEndUT; ut += fineStep) {
                p1.GetHeliocentricState(ut, out Vector3d pos1, out Vector3d vel1);

                for (double tofMul = 0.85; tofMul <= 1.15; tofMul += 0.15) {
                    double tofSec = baseTofSec * tofMul;
                    p2.GetHeliocentricState(ut + tofSec, out Vector3d pos2, out Vector3d vel2);

                    if (PykepNative.SolveLambert(pos1, pos2, tofSec, sunMu, false, out Vector3d v1, out Vector3d v2)) {
                        double vInf1 = (v1 - vel1).magnitude;
                        double vInf2 = (v2 - vel2).magnitude;

                        // 基线7: 出射 ΔV 补丁为 C# 二体公式, 仅在显式开启托管回退时允许
                        if (PykepNative.AllowManagedFallback) {
                            double dv1 = CalculateDepartureDeltaV(vInf1, p1, 200.0, 200.0);
                            if (double.IsNaN(dv1)) continue;

                            double totalDV = dv1 + vInf2;
                            if (totalDV < minDV) {
                                minDV = totalDV;
                                bestUT = ut;
                                bestTofDays = tofSec / 86400.0;
                            }
                        }
                    }
                }
            }

            return (bestUT, bestTofDays, minDV);
        }
    }
}
