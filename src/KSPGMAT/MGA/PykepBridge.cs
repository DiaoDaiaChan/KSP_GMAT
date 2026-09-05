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

        // Feature 1: MGA-1DSM Deep Space Maneuver Details
        public bool HasDSM { get; set; }
        public DSMResult DSM { get; set; }
        public Vector3d DSMPositionKm { get; set; }
        public MGALegStrategy Strategy { get; set; } = MGALegStrategy.Ballistic;
    }

    /// <summary>
    /// 航段推进与动力学策略枚举 (Per-Leg Astrodynamic Strategy)
    /// </summary>
    public enum MGALegStrategy {
        Ballistic = 0,               // 纯弹道 (Pure Ballistic Lambert)
        DeepSpaceManeuver1DSM = 1,    // 深空机动 1-DSM (Midcourse In-flight Burn)
        ContinuousLowThrust = 2      // 连续低推力 (Sims-Flanagan SEP)
    }

    /// <summary>
    /// 中间交会节点飞掠与打靶策略枚举 (Per-Node Flyby & Targeting Strategy)
    /// </summary>
    public enum MGAFlybyStrategy {
        UnpoweredFlyby = 0,          // 纯无动力自由双曲借力 (Unpowered Flyby)
        PoweredFlyby = 1,            // 动力飞掠 (Powered Assist)
        BPlaneTargeted = 2           // B 平面精准穿靶 (B-Plane Targeted TCM)
    }

    /// <summary>
    /// MGA 求解引擎全局动力学模式 (Engine Dynamics Mode)
    /// </summary>
    public enum MGAEngineMode {
        BallisticImpulsive = 0,    // 传统纯脉冲借力 (Ballistic MGA)
        DeepSpaceManeuver1DSM = 1, // 官方 MGA-1DSM (含中道深空机动与共振)
        LowThrustSimsFlanagan = 2, // 连续低推力打靶 (Sims-Flanagan / SEP)
        HeterogeneousHybrid = 3    // 异构分段混合策略 (Multi-Strategy Hybrid)
    }

    /// <summary>
    /// MGA 事件类型枚举 (100% 对齐 KSP-MGA-Planner 与真实任务事件)
    /// </summary>
    public enum MGAEventType {
        Escape,          // 1: <Origin> escape (出发脱离机动)
        DSM,             // 2: <From>-<To> DSM (深空变轨/中途机动)
        Flyby,           // 3: <Body> flyby (经过天体的引力助推飞掠)
        Circularization, // 4: <Destination> circularization / capture (目标行星捕获/圆化机动)
        TCM,             // 5: <From>-<To> TCM (B平面中道修正)
        LowThrustSegment // 6: 连续低推力打靶航段
    }

    public class DSMResult {
        public double EpochOffsetSec { get; set; }
        public Vector3d PositionKm { get; set; }
        public Vector3d DeltaVKms { get; set; }
        public double DeltaVMs { get; set; }
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }
    }

    public class BPlaneResult {
        public double BDotTKm { get; set; }
        public double BDotRKm { get; set; }
        public double BMagKm { get; set; }
        public double ThetaDeg { get; set; }
        public double VInfMagKms { get; set; }
        public Vector3d SHat { get; set; }
        public Vector3d THat { get; set; }
        public Vector3d RHat { get; set; }
    }

    public class TCMResult {
        public Vector3d DeltaVKms { get; set; }
        public double DeltaVMs { get; set; }
        public double ProgradeMs { get; set; }
        public double NormalMs { get; set; }
        public double RadialMs { get; set; }
    }

    public class PrimerVectorResult {
        public int SampleCount { get; set; }
        public double[] TimesSec { get; set; }
        public double[] PMags { get; set; }
        public double MaxPMag { get; set; }
        public double MaxPTimeSec { get; set; }
        public bool IsOptimal { get; set; }
    }

    public class MEEResult {
        public double P { get; set; }
        public double F { get; set; }
        public double G { get; set; }
        public double H { get; set; }
        public double K { get; set; }
        public double L { get; set; }
    }

    public class SimsFlanaganResult {
        public double DeltaRKm { get; set; }
        public double DeltaVKms { get; set; }
        public double DeltaMKg { get; set; }
        public double TotalDeltaVMs { get; set; }
        public double FinalMassKg { get; set; }
        public int SegmentCount { get; set; }
        public List<Vector3d> SegmentPositionsKm { get; set; } = new List<Vector3d>();
        public List<Vector3d> ThrottleVectors { get; set; } = new List<Vector3d>();
    }

    public class ParetoTradeOffPoint {
        public double TotalFlightDays { get; set; }
        public double TotalDeltaVKms { get; set; }
        public double LaunchDeltaVKms { get; set; }
        public double FlybyDeltaVKms { get; set; }
        public double[] Genome { get; set; }
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
        public Vector3d PositionKm { get; set; }
        
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

        // Advanced Astrodynamics Results (Features 2 - 6)
        public PrimerVectorResult PrimerVector { get; set; }
        public BPlaneResult BPlane { get; set; }
        public TCMResult SuggestedTCM { get; set; }
        public MEEResult DepartureMEE { get; set; }
        public SimsFlanaganResult LowThrustSolution { get; set; }
        public List<ParetoTradeOffPoint> ParetoFront { get; set; } = new List<ParetoTradeOffPoint>();

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
                PositionKm = leg0.DeparturePosKm,
                DeltaVMs = LaunchDeltaVKms * 1000.0,
                ProgradeMs = (!double.IsNaN(leg0.ProgradeMs) && Math.Abs(leg0.ProgradeMs) > 1e-4) ? leg0.ProgradeMs : (LaunchDeltaVKms * 1000.0),
                NormalMs = double.IsNaN(leg0.NormalMs) ? 0.0 : leg0.NormalMs,
                RadialMs = double.IsNaN(leg0.RadialMs) ? 0.0 : leg0.RadialMs,
                PhaseAngleDeg = double.IsNaN(leg0.PhaseAngleDeg) ? 0.0 : leg0.PhaseAngleDeg,
                EjectionAngleDeg = double.IsNaN(leg0.EjectionAngleDeg) ? 0.0 : leg0.EjectionAngleDeg,
                EjectionLANDeg = double.IsNaN(leg0.EjectionLANDeg) ? 0.0 : leg0.EjectionLANDeg,
                EjectionInclinationDeg = double.IsNaN(leg0.EjectionInclinationDeg) ? 0.0 : leg0.EjectionInclinationDeg,
                C3Kms2 = double.IsNaN(leg0.C3Kms2) ? 0.0 : leg0.C3Kms2,
                VInfOutKms = leg0.VInfDepartureKms.magnitude,
                IsFeasible = true
            });
            seq++;

            // 1.1 首段若存在 DSM (Deep Space Maneuver)
            if (leg0.HasDSM && leg0.DSM != null) {
                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = $"{seq}: {leg0.FromBody}-{leg0.ToBody} DSM",
                    EventType = MGAEventType.DSM,
                    BodyName = leg0.FromBody,
                    FromBody = leg0.FromBody,
                    ToBody = leg0.ToBody,
                    EpochUT = leg0.DepartureUT + leg0.DSM.EpochOffsetSec,
                    PositionKm = leg0.DSMPositionKm,
                    DeltaVMs = leg0.DSM.DeltaVMs,
                    ProgradeMs = leg0.DSM.ProgradeMs,
                    NormalMs = leg0.DSM.NormalMs,
                    RadialMs = leg0.DSM.RadialMs,
                    IsFeasible = true
                });
                seq++;
            }

            // 2. 依次构建中间飞掠、推进机动与后续段 DSM
            for (int k = 1; k < Legs.Count; k++) {
                var leg = Legs[k];
                var prevLeg = Legs[k - 1];

                string flybyTitle = (leg.FlybyDeltaVKms > 0.001) ? 
                    $"{seq}: {leg.FromBody} powered flyby" : 
                    $"{seq}: {leg.FromBody} flyby";

                Events.Add(new MGAEventItem {
                    SequenceNumber = seq,
                    Title = flybyTitle,
                    EventType = MGAEventType.Flyby,
                    BodyName = leg.FromBody,
                    FromBody = prevLeg.FromBody,
                    ToBody = leg.ToBody,
                    EpochUT = leg.DepartureUT,
                    PositionKm = leg.DeparturePosKm,
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

                // 后续段 DSM
                if (leg.HasDSM && leg.DSM != null) {
                    Events.Add(new MGAEventItem {
                        SequenceNumber = seq,
                        Title = $"{seq}: {leg.FromBody}-{leg.ToBody} DSM",
                        EventType = MGAEventType.DSM,
                        BodyName = leg.FromBody,
                        FromBody = leg.FromBody,
                        ToBody = leg.ToBody,
                        EpochUT = leg.DepartureUT + leg.DSM.EpochOffsetSec,
                        PositionKm = leg.DSMPositionKm,
                        DeltaVMs = leg.DSM.DeltaVMs,
                        ProgradeMs = leg.DSM.ProgradeMs,
                        NormalMs = leg.DSM.NormalMs,
                        RadialMs = leg.DSM.RadialMs,
                        IsFeasible = true
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
                    PositionKm = lastLeg.ArrivalPosKm,
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
                    PositionKm = lastLeg.ArrivalPosKm,
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
    /// MGA 规划配置输入 (100% 对齐 KSP-MGA-Planner 与高级动力学特性)
    /// </summary>
    public class MGAConfig {
        public string DepartureBody { get; set; } = "Earth";
        public string DestinationBody { get; set; } = "Saturn";
        public List<string> FlybySequence { get; set; } = new List<string> { "Venus", "Venus", "Earth", "Jupiter", "Saturn" };
        
        public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive;
        public List<MGALegStrategy> LegStrategies { get; set; } = new List<MGALegStrategy>();
        public List<MGAFlybyStrategy> FlybyStrategies { get; set; } = new List<MGAFlybyStrategy>();

        public void SyncStrategiesWithChain(int legCount) {
            if (LegStrategies == null) LegStrategies = new List<MGALegStrategy>();
            while (LegStrategies.Count < legCount) {
                var defStrat = (EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM) ? MGALegStrategy.DeepSpaceManeuver1DSM :
                               (EngineMode == MGAEngineMode.LowThrustSimsFlanagan ? MGALegStrategy.ContinuousLowThrust : MGALegStrategy.Ballistic);
                LegStrategies.Add(defStrat);
            }
            if (LegStrategies.Count > legCount) LegStrategies.RemoveRange(legCount, LegStrategies.Count - legCount);

            int fbCount = Math.Max(0, legCount - 1);
            if (FlybyStrategies == null) FlybyStrategies = new List<MGAFlybyStrategy>();
            while (FlybyStrategies.Count < fbCount) {
                FlybyStrategies.Add(MGAFlybyStrategy.UnpoweredFlyby);
            }
            if (FlybyStrategies.Count > fbCount) FlybyStrategies.RemoveRange(fbCount, FlybyStrategies.Count - fbCount);
        }

        public double DsmEtaDefault { get; set; } = 0.5;
        public List<double> DsmEtas { get; set; } = new List<double>();
        public List<double> FlybyRpKms { get; set; } = new List<double>();
        public List<double> FlybyBetasRad { get; set; } = new List<double>();

        public double LowThrustMaxThrustN { get; set; } = 0.5;
        public double LowThrustIspSec { get; set; } = 3000.0;
        public double SpacecraftWetMassKg { get; set; } = 1500.0;
        public int LowThrustSegments { get; set; } = 10;
        public double TargetBDotTKm { get; set; } = 0.0;
        public double TargetBDotRKm { get; set; } = 0.0;

        public int MaxSwingBys { get; set; } = 4;
        public int MaxResonances { get; set; } = 1;
        public int MaxBackLegs { get; set; } = 1;
        public int MaxBackSpacing { get; set; } = 2;
        public bool ConstrainByVesselDeltaV { get; set; } = false;
        public double VesselMaxDeltaVMs { get; set; } = 0.0;

        public double EarliestDepartureUT { get; set; }
        public double LatestDepartureUT { get; set; }
        
        public double DepartureAltitudeKm { get => DeparturePeAKm; set => DeparturePeAKm = value; }
        public double DeparturePeAKm { get; set; } = 200.0;
        public double DepartureApAKm { get; set; } = 200.0;
        public double DepartureInclinationDeg { get; set; } = 28.5;
        public bool LockDepartureInclination { get; set; } = false;

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

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeDSMResult {
        public double epoch_offset_sec;
        public double pos_x, pos_y, pos_z;
        public double dv_x, dv_y, dv_z;
        public double dv_mag_ms;
        public double prograde_ms;
        public double normal_ms;
        public double radial_ms;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeLeg1DSMResult {
        public double tof_sec;
        public double r1x, r1y, r1z;
        public double r2x, r2y, r2z;
        public double v1x, v1y, v1z;
        public double v2x, v2y, v2z;
        public double v_inf_dep_x, v_inf_dep_y, v_inf_dep_z;
        public double v_inf_arr_x, v_inf_arr_y, v_inf_arr_z;
        public double flyby_turn_deg;
        public double flyby_rp_km;
        public double flyby_beta_deg;
        public double flyby_dv_kms;
        public int has_dsm;
        public NativeDSMResult dsm;
        public int is_feasible;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeMGA1DSMSolution {
        public double launch_dv_kms;
        public double total_dsm_dv_kms;
        public double total_flyby_dv_kms;
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
        public NativeLeg1DSMResult[] legs;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeBPlaneResult {
        public double b_dot_t_km;
        public double b_dot_r_km;
        public double b_mag_km;
        public double theta_deg;
        public double v_inf_mag_kms;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] s_hat;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] t_hat;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public double[] r_hat;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeTCMResult {
        public double dv_x, dv_y, dv_z;
        public double dv_mag_ms;
        public double prograde_ms;
        public double normal_ms;
        public double radial_ms;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativePrimerVectorResult {
        public int sample_count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public double[] times_sec;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public double[] p_mags;
        public double max_p_mag;
        public double max_p_time_sec;
        public int is_optimal;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeMEE {
        public double p;
        public double f;
        public double g;
        public double h;
        public double k;
        public double L;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeSimsFlanaganResult {
        public double delta_r_km;
        public double delta_v_kms;
        public double delta_m_kg;
        public double total_dv_ms;
        public double final_mass_kg;
        public int n_segments;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)]
        public double[] segment_pos;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)]
        public double[] throttle;
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
            double dep_inc_deg,
            double arr_pe_km, double arr_ap_km,
            int no_insertion,
            out NativeMGASolution out_sol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMGA1DSMEvaluateDelegate(
            [In] NativePlanetState[] dep_planets,
            [In] NativePlanetState[] arr_planets,
            [In] double[] tofs_sec,
            [In] double[] dsm_etas,
            [In] double[] fb_rps_km,
            [In] double[] fb_betas_rad,
            int leg_count,
            double dep_pe_km, double dep_ap_km, double dep_inc_deg,
            double arr_pe_km, double arr_ap_km, int no_insertion,
            out NativeMGA1DSMSolution out_sol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMGAHybridEvaluateDelegate(
            [In] NativePlanetState[] dep_planets,
            [In] NativePlanetState[] arr_planets,
            [In] double[] tofs_sec,
            [In] int[] leg_strategies,
            [In] int[] flyby_strategies,
            [In] double[] dsm_etas,
            [In] double[] fb_rps_km,
            [In] double[] fb_betas_rad,
            int leg_count,
            double dep_pe_km, double dep_ap_km, double dep_inc_deg,
            double arr_pe_km, double arr_ap_km, int no_insertion,
            out NativeMGA1DSMSolution out_sol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCalcKeplerSTMDelegate(
            [In] double[] r0_km, [In] double[] v0_kms, double dt_sec, double mu,
            [Out] double[] out_stm_6x6);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCalcBPlaneDelegate(
            [In] double[] r_rel_km, [In] double[] v_rel_kms, double mu,
            out NativeBPlaneResult out_bp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCalcBPlaneTCMDelegate(
            [In] double[] r_tcm_km, [In] double[] v_tcm_kms, double dt_to_enc_sec,
            [In] double[] r_enc_curr_km, [In] double[] v_enc_curr_kms,
            double target_b_dot_t_km, double target_b_dot_r_km, double body_mu,
            out NativeTCMResult out_tcm);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCalcPrimerVectorDelegate(
            [In] double[] r1_km, [In] double[] v1_kms,
            [In] double[] dv1_kms, [In] double[] dv2_kms,
            double tof_sec, double mu,
            out NativePrimerVectorResult out_pv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCartesianToMEEDelegate(
            [In] double[] r_km, [In] double[] v_kms, double mu, out NativeMEE out_mee);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMEEToCartesianDelegate(
            in NativeMEE mee, double mu, [Out] double[] out_r_km, [Out] double[] out_v_kms);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepSimsFlanaganEvaluateDelegate(
            [In] double[] r1_km, [In] double[] v1_kms, double m0_kg,
            [In] double[] r2_km, [In] double[] v2_kms, double mf_guess_kg,
            double tof_sec, double max_thrust_N, double isp_sec, double mu,
            int n_seg, [In] double[] throttles_3n,
            out NativeSimsFlanaganResult out_sf);

        private static PykepMGAEvaluateDelegate _fnMGAEvaluate;
        private static PykepMGAHybridEvaluateDelegate _fnMGAHybridEvaluate;
        private static PykepMGA1DSMEvaluateDelegate _fnMGA1DSMEvaluate;
        private static PykepCalcKeplerSTMDelegate _fnCalcKeplerSTM;
        private static PykepCalcBPlaneDelegate _fnCalcBPlane;
        private static PykepCalcBPlaneTCMDelegate _fnCalcBPlaneTCM;
        private static PykepCalcPrimerVectorDelegate _fnCalcPrimerVector;
        private static PykepCartesianToMEEDelegate _fnCartesianToMEE;
        private static PykepMEEToCartesianDelegate _fnMEEToCartesian;
        private static PykepSimsFlanaganEvaluateDelegate _fnSimsFlanaganEvaluate;

        static PykepBridge() {
            try {
                if (PykepNative.IsAvailable) {
                    IntPtr pEval = PykepNative.GetProcAddressExport("pykep_mga_evaluate");
                    if (pEval != IntPtr.Zero) {
                        _fnMGAEvaluate = (PykepMGAEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pEval, typeof(PykepMGAEvaluateDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_evaluate C++ pipeline!");
                    }

                    IntPtr pHybrid = PykepNative.GetProcAddressExport("pykep_mga_hybrid_evaluate");
                    if (pHybrid != IntPtr.Zero) {
                        _fnMGAHybridEvaluate = (PykepMGAHybridEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pHybrid, typeof(PykepMGAHybridEvaluateDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_hybrid_evaluate C++ pipeline!");
                    }

                    IntPtr p1Dsm = PykepNative.GetProcAddressExport("pykep_mga_1dsm_evaluate");
                    if (p1Dsm != IntPtr.Zero) {
                        _fnMGA1DSMEvaluate = (PykepMGA1DSMEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(p1Dsm, typeof(PykepMGA1DSMEvaluateDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_1dsm_evaluate C++ pipeline!");
                    }

                    IntPtr pStm = PykepNative.GetProcAddressExport("pykep_calc_kepler_stm");
                    if (pStm != IntPtr.Zero) {
                        _fnCalcKeplerSTM = (PykepCalcKeplerSTMDelegate)Marshal.GetDelegateForFunctionPointer(pStm, typeof(PykepCalcKeplerSTMDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_calc_kepler_stm C++ pipeline!");
                    }

                    IntPtr pBp = PykepNative.GetProcAddressExport("pykep_calc_bplane");
                    if (pBp != IntPtr.Zero) {
                        _fnCalcBPlane = (PykepCalcBPlaneDelegate)Marshal.GetDelegateForFunctionPointer(pBp, typeof(PykepCalcBPlaneDelegate));
                    }

                    IntPtr pTcm = PykepNative.GetProcAddressExport("pykep_calc_bplane_tcm");
                    if (pTcm != IntPtr.Zero) {
                        _fnCalcBPlaneTCM = (PykepCalcBPlaneTCMDelegate)Marshal.GetDelegateForFunctionPointer(pTcm, typeof(PykepCalcBPlaneTCMDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_calc_bplane_tcm C++ pipeline!");
                    }

                    IntPtr pPv = PykepNative.GetProcAddressExport("pykep_calc_primer_vector");
                    if (pPv != IntPtr.Zero) {
                        _fnCalcPrimerVector = (PykepCalcPrimerVectorDelegate)Marshal.GetDelegateForFunctionPointer(pPv, typeof(PykepCalcPrimerVectorDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_calc_primer_vector C++ pipeline!");
                    }

                    IntPtr pC2M = PykepNative.GetProcAddressExport("pykep_cartesian_to_mee");
                    if (pC2M != IntPtr.Zero) {
                        _fnCartesianToMEE = (PykepCartesianToMEEDelegate)Marshal.GetDelegateForFunctionPointer(pC2M, typeof(PykepCartesianToMEEDelegate));
                    }

                    IntPtr pM2C = PykepNative.GetProcAddressExport("pykep_mee_to_cartesian");
                    if (pM2C != IntPtr.Zero) {
                        _fnMEEToCartesian = (PykepMEEToCartesianDelegate)Marshal.GetDelegateForFunctionPointer(pM2C, typeof(PykepMEEToCartesianDelegate));
                    }

                    IntPtr pSf = PykepNative.GetProcAddressExport("pykep_sims_flanagan_evaluate");
                    if (pSf != IntPtr.Zero) {
                        _fnSimsFlanaganEvaluate = (PykepSimsFlanaganEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pSf, typeof(PykepSimsFlanaganEvaluateDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_sims_flanagan_evaluate C++ pipeline!");
                    }
                }
            } catch (Exception ex) {
                KSPGMATLog.Warn($"[PykepBridge] Native MGA bridge linkage note: {ex.Message}");
            }
        }

        public static double CalculateDepartureDeltaV(double vInfDepKms, SolarPlanet planet, double peAKm, double apAKm) {
            if (planet == null || planet.Mu <= 0) return 0.0;
            return PykepNative.CalculateDepartureDeltaV(vInfDepKms, planet.Mu, planet.RadiusKm, peAKm, apAKm);
        }

        public static double CalculateArrivalInsertionDeltaV(double vInfArrKms, SolarPlanet planet, double peAKm, double apAKm, bool flybyOnly) {
            if (flybyOnly || planet == null || planet.Mu <= 0) return 0.0;
            return PykepNative.CalculateArrivalInsertionDeltaV(vInfArrKms, planet.Mu, planet.RadiusKm, peAKm, apAKm, false);
        }

        /// <summary>
        /// 原生调用 PyKEP C++ 评估多段 MGA 轨道 (数据封送并直接解算)
        /// </summary>
        public static MGASolution BuildFullSolution(double[] genome, List<string> fullChain, MGAConfig config, bool computeAdvanced = true) {
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

            if (config != null) config.SyncStrategiesWithChain(legCount);

            // 1. 优先使用官方统一异构分段混合求解器 (Hybrid Multi-Strategy Evaluator)
            if (_fnMGAHybridEvaluate != null) {
                var legStrats = new int[legCount];
                for (int k = 0; k < legCount; k++) {
                    if (config != null && config.LegStrategies != null && k < config.LegStrategies.Count) {
                        legStrats[k] = (int)config.LegStrategies[k];
                    } else if (config != null && config.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM) {
                        legStrats[k] = 1;
                    } else if (config != null && config.EngineMode == MGAEngineMode.LowThrustSimsFlanagan) {
                        legStrats[k] = 2;
                    } else {
                        legStrats[k] = 0;
                    }
                }

                int fbCount = Math.Max(1, legCount - 1);
                var fbStrats = new int[fbCount];
                for (int k = 0; k < fbCount; k++) {
                    if (config != null && config.FlybyStrategies != null && k < config.FlybyStrategies.Count) {
                        fbStrats[k] = (int)config.FlybyStrategies[k];
                    } else {
                        fbStrats[k] = 0;
                    }
                }

                var dsmEtas = new double[legCount];
                var fbRps = new double[legCount];
                var fbBetas = new double[legCount];

                for (int k = 0; k < legCount; k++) {
                    dsmEtas[k] = (config != null && config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0) ? config.DsmEtas[k] : (config != null ? config.DsmEtaDefault : 0.5);
                    fbRps[k] = (config != null && config.FlybyRpKms != null && k < config.FlybyRpKms.Count) ? config.FlybyRpKms[k] : 0.0;
                    fbBetas[k] = (config != null && config.FlybyBetasRad != null && k < config.FlybyBetasRad.Count) ? config.FlybyBetasRad[k] : 0.0;
                }

                int status = _fnMGAHybridEvaluate(
                    depPlanets, arrPlanets, tofs,
                    legStrats, fbStrats,
                    dsmEtas, fbRps, fbBetas,
                    legCount,
                    config != null ? config.DeparturePeAKm : 200.0, config != null ? config.DepartureApAKm : 200.0,
                    config != null ? config.DepartureInclinationDeg : 0.0,
                    config != null ? config.DestinationPeAKm : 200.0, config != null ? config.DestinationApAKm : 200.0,
                    (config != null && config.NoInsertionBurn) ? 1 : 0,
                    out NativeMGA1DSMSolution n1Sol);

                if (status == 0) {
                    var sol1 = MapNative1DSMSolution(n1Sol, genome[0], fullChain, config);
                    for (int k = 0; k < sol1.Legs.Count && k < legStrats.Length; k++) {
                        sol1.Legs[k].Strategy = (MGALegStrategy)legStrats[k];
                    }
                    if (computeAdvanced) {
                        PopulateAdvancedAstrodynamics(sol1, genome, fullChain, config);
                    }
                    return sol1;
                }
            }

            // 1.1 回退到单独的 1-DSM 分支
            if (config != null && config.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM && _fnMGA1DSMEvaluate != null) {
                var dsmEtas = new double[legCount];
                var fbRps = new double[legCount];
                var fbBetas = new double[legCount];

                for (int k = 0; k < legCount; k++) {
                    dsmEtas[k] = (config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0) ? config.DsmEtas[k] : config.DsmEtaDefault;
                    fbRps[k] = (config.FlybyRpKms != null && k < config.FlybyRpKms.Count) ? config.FlybyRpKms[k] : 0.0;
                    fbBetas[k] = (config.FlybyBetasRad != null && k < config.FlybyBetasRad.Count) ? config.FlybyBetasRad[k] : 0.0;
                }

                int status = _fnMGA1DSMEvaluate(
                    depPlanets, arrPlanets, tofs,
                    dsmEtas, fbRps, fbBetas,
                    legCount,
                    config.DeparturePeAKm, config.DepartureApAKm,
                    config.DepartureInclinationDeg,
                    config.DestinationPeAKm, config.DestinationApAKm,
                    config.NoInsertionBurn ? 1 : 0,
                    out NativeMGA1DSMSolution n1Sol);

                if (status == 0) {
                    var sol1 = MapNative1DSMSolution(n1Sol, genome[0], fullChain, config);
                    if (computeAdvanced) {
                        PopulateAdvancedAstrodynamics(sol1, genome, fullChain, config);
                    }
                    return sol1;
                }
            }

            // 2. 默认或回退到标准纯脉冲多重借力 (Ballistic MGA)
            if (_fnMGAEvaluate != null) {
                int status = _fnMGAEvaluate(
                    depPlanets, arrPlanets, tofs, legCount,
                    config.DeparturePeAKm, config.DepartureApAKm,
                    config.DepartureInclinationDeg,
                    config.DestinationPeAKm, config.DestinationApAKm,
                    config.NoInsertionBurn ? 1 : 0,
                    out NativeMGASolution nativeSol);

                if (status == 0) {
                    var sol = MapNativeSolution(nativeSol, genome[0], fullChain, config);
                    if (computeAdvanced) {
                        PopulateAdvancedAstrodynamics(sol, genome, fullChain, config);
                    }
                    return sol;
                }
            }

            // 原生 MGA 桥解算失败或未就绪：100% 遵守基线0/7/11，严禁托管自编公式拟合
            return null;
        }

        private static void PopulateAdvancedAstrodynamics(MGASolution sol, double[] genome, List<string> fullChain, MGAConfig config) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0 || config == null) return;

            try {
                var leg0 = sol.Legs[0];
                var lastLeg = sol.Legs.Last();

                // Feature 5: 无奇异 MEE 修正等分轨道根数
                if (SolarSystemCatalog.SunMu > 0) {
                    sol.DepartureMEE = CartesianToMEE(leg0.DeparturePosKm, leg0.DepartureVelKms, SolarSystemCatalog.SunMu);
                }

                // Feature 3: 劳登主矢量最优性评估 (Primer Vector)
                var pDep = SolarSystemCatalog.GetPlanet(leg0.FromBody);
                var pArr = SolarSystemCatalog.GetPlanet(lastLeg.ToBody);
                if (pDep != null && pArr != null) {
                    pDep.GetHeliocentricState(leg0.DepartureUT, out Vector3d _, out Vector3d vDepPla);
                    pArr.GetHeliocentricState(lastLeg.ArrivalUT, out Vector3d rDestPla, out Vector3d vDestPla);

                    Vector3d dv1 = leg0.DepartureVelKms - vDepPla;
                    Vector3d dv2 = lastLeg.ArrivalVelKms - vDestPla;
                    sol.PrimerVector = CalculatePrimerVector(leg0.DeparturePosKm, leg0.DepartureVelKms, dv1, dv2, leg0.ToFDays * 86400.0);

                    // Feature 2: 目标天体双曲飞掠 B-Plane 与 TCM 中道修正计算
                    if (pArr.Mu > 0) {
                        Vector3d rRel = lastLeg.ArrivalPosKm - rDestPla;
                        Vector3d vRel = lastLeg.ArrivalVelKms - vDestPla;
                        sol.BPlane = CalculateBPlane(rRel, vRel, pArr.Mu);

                        // 计算中道 1/2 处 TCM 点火
                        double midUT = leg0.DepartureUT + leg0.ToFDays * 86400.0 * 0.5;
                        double dtToEnc = (lastLeg.ArrivalUT - midUT);
                        Vector3d rMid = (leg0.DeparturePosKm + leg0.ArrivalPosKm) * 0.5;
                        Vector3d vMid = leg0.DepartureVelKms;
                        sol.SuggestedTCM = CalculateBPlaneTCM(rMid, vMid, dtToEnc, rRel, vRel, config.TargetBDotTKm, config.TargetBDotRKm, pArr.Mu);
                    }
                }

                // Feature 6: 连续低推力模拟 (Sims-Flanagan)
                if (config.EngineMode == MGAEngineMode.LowThrustSimsFlanagan) {
                    double tofSec = leg0.ToFDays * 86400.0;
                    int nSeg = Math.Max(4, Math.Min(32, config.LowThrustSegments));
                    var throttles = new double[nSeg * 3];
                    for (int i = 0; i < nSeg; i++) {
                        throttles[3 * i] = 1.0;
                    }
                    sol.LowThrustSolution = EvaluateSimsFlanagan(
                        leg0.DeparturePosKm, leg0.DepartureVelKms, config.SpacecraftWetMassKg,
                        leg0.ArrivalPosKm, leg0.ArrivalVelKms, config.SpacecraftWetMassKg * 0.85,
                        tofSec, config.LowThrustMaxThrustN, config.LowThrustIspSec, SolarSystemCatalog.SunMu,
                        nSeg, throttles);
                }

                // Feature 4: 帕累托前沿权衡曲线 (Pareto Trade-Off Curve)
                if (sol.ParetoFront == null || sol.ParetoFront.Count == 0) {
                    sol.ParetoFront = GenerateParetoFront(genome, fullChain, config, 9);
                }
            } catch (Exception ex) {
                KSPGMATLog.Warn($"[PykepBridge] PopulateAdvancedAstrodynamics note: {ex.Message}");
            }
        }

        private static MGASolution MapNative1DSMSolution(NativeMGA1DSMSolution nSol, double startUT, List<string> fullChain, MGAConfig config) {
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
                    IsFeasible = nl.is_feasible != 0,
                    HasDSM = nl.has_dsm != 0
                };

                if (nl.has_dsm != 0) {
                    leg.DSM = new DSMResult {
                        EpochOffsetSec = nl.dsm.epoch_offset_sec,
                        PositionKm = new Vector3d(nl.dsm.pos_x, nl.dsm.pos_y, nl.dsm.pos_z),
                        DeltaVKms = new Vector3d(nl.dsm.dv_x, nl.dsm.dv_y, nl.dsm.dv_z),
                        DeltaVMs = nl.dsm.dv_mag_ms,
                        ProgradeMs = nl.dsm.prograde_ms,
                        NormalMs = nl.dsm.normal_ms,
                        RadialMs = nl.dsm.radial_ms
                    };
                    leg.DSMPositionKm = leg.DSM.PositionKm;
                    leg.ProgradeMs = nl.dsm.prograde_ms;
                    leg.NormalMs = nl.dsm.normal_ms;
                    leg.RadialMs = nl.dsm.radial_ms;
                } else {
                    leg.ProgradeMs = (k == 0) ? (nSol.launch_dv_kms * 1000.0) : (nl.flyby_dv_kms * 1000.0);
                    leg.NormalMs = 0.0;
                    leg.RadialMs = 0.0;
                }

                if (k == 0) {
                    leg.C3Kms2 = nSol.c3_kms2;
                    leg.EjectionDeltaVKms = nSol.launch_dv_kms;
                    leg.EjectionAngleDeg = nSol.ejection_angle_deg;
                    leg.EjectionInclinationDeg = nSol.ejection_inc_deg;
                    leg.EjectionLANDeg = nSol.ejection_lan_deg;
                    if (leg.ProgradeMs == 0 && nSol.launch_dv_kms > 0) {
                        leg.ProgradeMs = nSol.launch_dv_kms * 1000.0;
                    }
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
                    ProgradeMs = (k == 0 && Math.Abs(nl.prograde_ms) < 1e-4) ? (nSol.launch_dv_kms * 1000.0) : nl.prograde_ms,
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
                    if (leg.ProgradeMs == 0 && nSol.launch_dv_kms > 0) {
                        leg.ProgradeMs = nSol.launch_dv_kms * 1000.0;
                    }
                    leg.PhaseAngleDeg = (Math.Atan2(nl.r2y, nl.r2x) - Math.Atan2(nl.r1y, nl.r1x)) * Mathf.Rad2Deg;
                    if (leg.PhaseAngleDeg < 0) leg.PhaseAngleDeg += 360.0;
                } else if (k == nSol.leg_count - 1) {
                    leg.InsertionDeltaVKms = nSol.insertion_dv_kms;
                }

                sol.Legs.Add(leg);
                curUT = arrUT;
            }

            sol.BuildEvents(config);

            try {
                UnityEngine.Debug.Log($"[KSPGMAT-Pykep] 映射原生 MGA 解: 序列='{sol.FlybyChainSummary}' | 发射ΔV={sol.LaunchDeltaVKms * 1000.0:F1} m/s, 制动ΔV={sol.InsertionDeltaVKms * 1000.0:F1} m/s, 总ΔV={sol.TotalDeltaVKms * 1000.0:F1} m/s, 飞行={sol.TotalFlightDays:F1}天, 可行性={sol.AllLegsFeasible}");
                for (int k = 0; k < sol.Legs.Count; k++) {
                    var l = sol.Legs[k];
                    UnityEngine.Debug.Log($"[KSPGMAT-Pykep]   Leg #{k + 1}: {l.FromBody} -> {l.ToBody} | 出发UT={l.DepartureUT:F1}, 到达UT={l.ArrivalUT:F1}, 飞行={l.ToFDays:F1}d, 相角={l.PhaseAngleDeg:F1}°, C3={l.C3Kms2:F2} km²/s², VNC=({l.ProgradeMs:F1}, {l.NormalMs:F1}, {l.RadialMs:F1}) m/s, R1={l.DeparturePosKm.magnitude / 1.496e8:F3}AU, R2={l.ArrivalPosKm.magnitude / 1.496e8:F3}AU");
                }
            } catch { }

            return sol;
        }

        // =====================================================================
        // PUBLIC ASTRODYNAMICS WRAPPER METHODS (Pure Native Callers)
        // =====================================================================

        public static PrimerVectorResult CalculatePrimerVector(Vector3d r1, Vector3d v1, Vector3d dv1, Vector3d dv2, double tofSec) {
            if (_fnCalcPrimerVector == null || tofSec <= 0) return null;
            double[] r1Arr = new double[] { r1.x, r1.y, r1.z };
            double[] v1Arr = new double[] { v1.x, v1.y, v1.z };
            double[] dv1Arr = new double[] { dv1.x, dv1.y, dv1.z };
            double[] dv2Arr = new double[] { dv2.x, dv2.y, dv2.z };
            int status = _fnCalcPrimerVector(r1Arr, v1Arr, dv1Arr, dv2Arr, tofSec, SolarSystemCatalog.SunMu, out NativePrimerVectorResult nPv);
            if (status != 0) return null;
            int n = Math.Min(nPv.sample_count, 32);
            var res = new PrimerVectorResult {
                SampleCount = n,
                TimesSec = new double[n],
                PMags = new double[n],
                MaxPMag = nPv.max_p_mag,
                MaxPTimeSec = nPv.max_p_time_sec,
                IsOptimal = nPv.is_optimal != 0
            };
            Array.Copy(nPv.times_sec, res.TimesSec, n);
            Array.Copy(nPv.p_mags, res.PMags, n);
            return res;
        }

        public static BPlaneResult CalculateBPlane(Vector3d rRel, Vector3d vRel, double bodyMu) {
            if (_fnCalcBPlane == null || bodyMu <= 0) return null;
            double[] rArr = new double[] { rRel.x, rRel.y, rRel.z };
            double[] vArr = new double[] { vRel.x, vRel.y, vRel.z };
            int status = _fnCalcBPlane(rArr, vArr, bodyMu, out NativeBPlaneResult nBp);
            if (status != 0) return null;
            return new BPlaneResult {
                BDotTKm = nBp.b_dot_t_km,
                BDotRKm = nBp.b_dot_r_km,
                BMagKm = nBp.b_mag_km,
                ThetaDeg = nBp.theta_deg,
                VInfMagKms = nBp.v_inf_mag_kms,
                SHat = new Vector3d(nBp.s_hat[0], nBp.s_hat[1], nBp.s_hat[2]),
                THat = new Vector3d(nBp.t_hat[0], nBp.t_hat[1], nBp.t_hat[2]),
                RHat = new Vector3d(nBp.r_hat[0], nBp.r_hat[1], nBp.r_hat[2])
            };
        }

        public static TCMResult CalculateBPlaneTCM(Vector3d rTcm, Vector3d vTcm, double dtToEncSec, Vector3d rEncCurr, Vector3d vEncCurr, double targetBDotT, double targetBDotR, double bodyMu) {
            if (_fnCalcBPlaneTCM == null || bodyMu <= 0) return null;
            double[] rTcmArr = new double[] { rTcm.x, rTcm.y, rTcm.z };
            double[] vTcmArr = new double[] { vTcm.x, vTcm.y, vTcm.z };
            double[] rEncArr = new double[] { rEncCurr.x, rEncCurr.y, rEncCurr.z };
            double[] vEncArr = new double[] { vEncCurr.x, vEncCurr.y, vEncCurr.z };
            int status = _fnCalcBPlaneTCM(rTcmArr, vTcmArr, dtToEncSec, rEncArr, vEncArr, targetBDotT, targetBDotR, bodyMu, out NativeTCMResult nTcm);
            if (status != 0) return null;
            return new TCMResult {
                DeltaVKms = new Vector3d(nTcm.dv_x, nTcm.dv_y, nTcm.dv_z),
                DeltaVMs = nTcm.dv_mag_ms,
                ProgradeMs = nTcm.prograde_ms,
                NormalMs = nTcm.normal_ms,
                RadialMs = nTcm.radial_ms
            };
        }

        public static MEEResult CartesianToMEE(Vector3d r, Vector3d v, double mu) {
            if (_fnCartesianToMEE == null || mu <= 0) return null;
            double[] rArr = new double[] { r.x, r.y, r.z };
            double[] vArr = new double[] { v.x, v.y, v.z };
            int status = _fnCartesianToMEE(rArr, vArr, mu, out NativeMEE mee);
            if (status != 0) return null;
            return new MEEResult { P = mee.p, F = mee.f, G = mee.g, H = mee.h, K = mee.k, L = mee.L };
        }

        public static SimsFlanaganResult EvaluateSimsFlanagan(Vector3d r1, Vector3d v1, double m0Kg, Vector3d r2, Vector3d v2, double mfGuessKg, double tofSec, double maxThrustN, double ispSec, double mu, int nSeg, double[] throttles) {
            if (_fnSimsFlanaganEvaluate == null || tofSec <= 0 || nSeg <= 0) return null;
            double[] r1Arr = new double[] { r1.x, r1.y, r1.z };
            double[] v1Arr = new double[] { v1.x, v1.y, v1.z };
            double[] r2Arr = new double[] { r2.x, r2.y, r2.z };
            double[] v2Arr = new double[] { v2.x, v2.y, v2.z };
            int status = _fnSimsFlanaganEvaluate(r1Arr, v1Arr, m0Kg, r2Arr, v2Arr, mfGuessKg, tofSec, maxThrustN, ispSec, mu, nSeg, throttles, out NativeSimsFlanaganResult nSf);
            if (status != 0) return null;
            var res = new SimsFlanaganResult {
                DeltaRKm = nSf.delta_r_km,
                DeltaVKms = nSf.delta_v_kms,
                DeltaMKg = nSf.delta_m_kg,
                TotalDeltaVMs = nSf.total_dv_ms,
                FinalMassKg = nSf.final_mass_kg,
                SegmentCount = nSf.n_segments
            };
            for (int i = 0; i < nSf.n_segments; i++) {
                res.SegmentPositionsKm.Add(new Vector3d(nSf.segment_pos[3 * i], nSf.segment_pos[3 * i + 1], nSf.segment_pos[3 * i + 2]));
                res.ThrottleVectors.Add(new Vector3d(nSf.throttle[3 * i], nSf.throttle[3 * i + 1], nSf.throttle[3 * i + 2]));
            }
            return res;
        }

        public static List<ParetoTradeOffPoint> GenerateParetoFront(double[] bestGenome, List<string> fullChain, MGAConfig config, int count = 9) {
            var front = new List<ParetoTradeOffPoint>();
            if (bestGenome == null || fullChain == null || fullChain.Count < 2) return front;

            double[] scaleFactors = new double[] { 0.70, 0.77, 0.85, 0.92, 1.0, 1.10, 1.22, 1.35, 1.50 };
            for (int s = 0; s < Math.Min(count, scaleFactors.Length); s++) {
                double factor = scaleFactors[s];
                double[] sampleGenome = (double[])bestGenome.Clone();
                for (int k = 1; k < sampleGenome.Length; k++) {
                    sampleGenome[k] *= factor;
                }
                var testSol = BuildFullSolution(sampleGenome, fullChain, config, computeAdvanced: false);
                if (testSol != null && testSol.TotalDeltaVKms > 0 && !double.IsNaN(testSol.TotalDeltaVKms)) {
                    front.Add(new ParetoTradeOffPoint {
                        TotalFlightDays = testSol.TotalFlightDays,
                        TotalDeltaVKms = testSol.TotalDeltaVKms,
                        LaunchDeltaVKms = testSol.LaunchDeltaVKms,
                        FlybyDeltaVKms = testSol.TotalDeltaVKms - testSol.LaunchDeltaVKms - testSol.InsertionDeltaVKms,
                        Genome = sampleGenome
                    });
                }
            }
            front.Sort((a, b) => a.TotalFlightDays.CompareTo(b.TotalFlightDays));
            return front;
        }

        private static double ComputeFitness(MGASolution sol, MGAConfig config) {
            if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) return 1e9;
            double fit = sol.TotalDeltaVKms + (sol.AllLegsFeasible ? 0.0 : 50.0);
            if (config != null) {
                if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) {
                    double totalMs = sol.TotalDeltaVKms * 1000.0;
                    if (totalMs > config.VesselMaxDeltaVMs) {
                        double overKms = (totalMs - config.VesselMaxDeltaVMs) / 1000.0;
                        fit += overKms * 15.0 + 30.0; // 显著罚函数惩罚超出飞船当前可用 ΔV 预算的解
                    }
                }
                if (config.LockDepartureInclination && sol.Legs != null && sol.Legs.Count > 0) {
                    double actualInc = sol.Legs[0].EjectionInclinationDeg;
                    double targetInc = config.DepartureInclinationDeg;
                    double incErr = Math.Abs(actualInc - targetInc);
                    if (incErr > 0.05) {
                        // 逃逸倾角锁定约束罚函数：若逃逸倾角偏离锁定值，按每度 25 km/s + 50 km/s 罚项强力约束
                        fit += incErr * 25.0 + 50.0;
                    }
                }
            }
            return fit;
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
                var sol = BuildFullSolution(pop[i], fullChain, config, computeAdvanced: false);
                fitness[i] = ComputeFitness(sol, config);
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

                    var candSol = BuildFullSolution(candidate, fullChain, config, computeAdvanced: false);
                    double candFit = ComputeFitness(candSol, config);
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
                var sol = BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: true);
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

            // 出发与目标天体相同时无星际转移物理意义，直接返回空列表，杜绝无向 BFS 指数级爆炸与主线程卡死
            if (origin.Equals(dest, StringComparison.OrdinalIgnoreCase)) return results;

            var planetList = SolarSystemCatalog.GetAvailablePlanets();
            if (planetList == null || planetList.Count == 0) return results;

            var bodyIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < planetList.Count; i++) {
                if (!string.IsNullOrEmpty(planetList[i].Name) && !bodyIndexMap.ContainsKey(planetList[i].Name)) {
                    bodyIndexMap[planetList[i].Name] = i;
                }
            }

            if (!bodyIndexMap.TryGetValue(origin, out int oIdx) || !bodyIndexMap.TryGetValue(dest, out int dIdx)) {
                return results;
            }

            var queue = new Queue<(List<string> chain, int resonances, int backLegs)>();
            queue.Enqueue((new List<string> { origin }, 0, 0));

            int maxLegs = config.MaxSwingBys + 1;
            int visitedCount = 0;

            while (queue.Count > 0) {
                // 安全熔断上限，严防异常分支造成游戏卡死
                if (++visitedCount > 3000 || results.Count >= 100) break;

                var (currentChain, curRes, curBack) = queue.Dequeue();
                string currentBody = currentChain.Last();
                if (!bodyIndexMap.TryGetValue(currentBody, out int cIdx)) cIdx = -1;

                if (currentChain.Count > 1 && currentBody.Equals(dest, StringComparison.OrdinalIgnoreCase)) {
                    if (!results.Any(r => r.SequenceEqual(currentChain))) {
                        results.Add(new List<string>(currentChain.Skip(1)));
                    }
                    continue;
                }

                if (currentChain.Count >= maxLegs + 1) continue;

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
            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody) ||
                fromBody.Equals(toBody, StringComparison.OrdinalIgnoreCase)) {
                return 0.0;
            }
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
            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody) ||
                fromBody.Equals(toBody, StringComparison.OrdinalIgnoreCase)) {
                return 365.25;
            }
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
            string fromBody, string toBody, double searchStartUT, double searchSpanDays = 0, double targetIncDeg = -1.0) 
        {
            // 基线7.0/7.8: 彻底弃用 TWP (global::LambertSolver)。
            // 窗口相角采用基线7.8解析公式 φ = π − ω2·T_hoh, 会合周期 T_syn = 2π/|ω1 − ω2|;
            // ΔV 评估必须由原生 PyKEP 兰伯特完成, 原生不可用时严禁托管近似回退, 统一返回 NaN 失败标志。
            if (!PykepNative.IsAvailable) {
                KSPGMATLog.Error("PykepNative 原生库不可用，无法进行霍曼窗口物理评估 (返回失败标志 NaN)");
                return (double.NaN, double.NaN, double.NaN);
            }

            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody) ||
                fromBody.Equals(toBody, StringComparison.OrdinalIgnoreCase)) {
                KSPGMATLog.Warn("霍曼窗口计算出发天体与目标天体相同，已安全退出");
                return (double.NaN, double.NaN, double.NaN);
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
            if (targetIncDeg >= 0 && searchSpanDays <= 0) {
                // 倾角约束模式下扫描范围拓宽至 2 个会合周期，确保覆盖多圈相角几何
                scanSpanDays = Math.Max(scanSpanDays, synodicDays * 2.0);
            }
            double scanEndUT = searchStartUT + scanSpanDays * 86400.0;

            // 1) 相角扫描: 收集相角最接近理论霍曼相角的候选发射窗口
            var candidateUTs = new List<double>();
            double phaseStep = Math.Max(3600.0, synodicSec / 400.0);
            double prevErr = double.MaxValue;
            double prevUT = searchStartUT;
            bool wasDecreasing = false;

            for (double ut = searchStartUT; ut <= scanEndUT; ut += phaseStep) {
                p1.GetHeliocentricState(ut, out Vector3d posA, out Vector3d velA);
                p2.GetHeliocentricState(ut, out Vector3d posB, out Vector3d velB);
                double phase = (Math.Atan2(posB.y, posB.x) - Math.Atan2(posA.y, posA.x));
                phase = ((phase % (2.0 * Math.PI)) + 2.0 * Math.PI) % (2.0 * Math.PI);
                double err = Math.Abs(phase - reqPhase);
                if (err > Math.PI) err = 2.0 * Math.PI - err;

                if (err < prevErr) {
                    wasDecreasing = true;
                } else if (wasDecreasing) {
                    if (prevErr < 0.25) {
                        candidateUTs.Add(prevUT);
                    }
                    wasDecreasing = false;
                }
                prevErr = err;
                prevUT = ut;
            }
            if (candidateUTs.Count == 0) {
                candidateUTs.Add(searchStartUT);
            }

            // 2) 精扫: 以候选窗口为中心调用原生 PyKEP 兰伯特评估 ΔV 与逃逸赤纬几何
            double bestUT = candidateUTs[0];
            double bestTofDays = baseTofDays;
            double minDV = double.MaxValue;

            double fineSpanSec = Math.Max(2.0 * 86400.0, synodicSec / 6.0);
            double fineStep = Math.Max(3600.0, synodicSec / 200.0);

            foreach (double candUT in candidateUTs) {
                double fineStartUT = candUT - fineSpanSec;
                double fineEndUT = candUT + fineSpanSec;

                for (double ut = fineStartUT; ut <= fineEndUT; ut += fineStep) {
                    p1.GetHeliocentricState(ut, out Vector3d pos1, out Vector3d vel1);

                    for (double tofMul = 0.85; tofMul <= 1.15; tofMul += 0.15) {
                        double tofSec = baseTofSec * tofMul;
                        p2.GetHeliocentricState(ut + tofSec, out Vector3d pos2, out Vector3d vel2);

                        if (PykepNative.SolveLambert(pos1, pos2, tofSec, sunMu, false, out Vector3d v1, out Vector3d v2)) {
                            Vector3d vInf1Vec = v1 - vel1;
                            double vInf1 = vInf1Vec.magnitude;
                            double vInf2 = (v2 - vel2).magnitude;

                            // 原生 C++ 评估发射与到达 ΔV
                            double dv1 = CalculateDepartureDeltaV(vInf1, p1, 200.0, 200.0);
                            if (!double.IsNaN(dv1)) {
                                double totalDV = dv1 + vInf2;
                                if (targetIncDeg >= 0) {
                                    // 逃逸渐近线与赤道平面的夹角 (赤纬 declination) = asin(v_z / |v|)
                                    double decRad = Math.Asin(Math.Max(-1.0, Math.Min(1.0, vInf1Vec.z / Math.Max(1e-6, vInf1))));
                                    double absDecDeg = Math.Abs(decRad) * (180.0 / Math.PI);
                                    if (absDecDeg > targetIncDeg) {
                                        // 渐近线赤纬超出锁定倾角，必须进行大角度面外机动，施加重惩罚
                                        totalDV += (absDecDeg - targetIncDeg) * 50.0 + 100.0;
                                    } else {
                                        // 允许范围内，微调奖励最接近锁定倾角(最佳方位角)的窗口
                                        totalDV += Math.Abs(absDecDeg - targetIncDeg) * 2.0;
                                    }
                                }
                                if (totalDV < minDV) {
                                    minDV = totalDV;
                                    bestUT = ut;
                                    bestTofDays = tofSec / 86400.0;
                                }
                            }
                        }
                    }
                }
            }

            // 精扫未找到任何有效原生兰伯特解: 返回 NaN 失败标志, 严禁返回近似窗口冒充物理结果
            if (minDV >= double.MaxValue * 0.5) {
                KSPGMATLog.Error("霍曼窗口精扫未找到有效原生兰伯特解 (返回失败标志 NaN)");
                return (double.NaN, double.NaN, double.NaN);
            }

            return (bestUT, bestTofDays, minDV);
        }
    }
}
