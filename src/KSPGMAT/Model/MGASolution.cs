using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KSPGMAT.Model {
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
        public double TotalFlybyDeltaVKms => Legs != null ? Legs.Sum(l => l.FlybyDeltaVKms) : 0.0;
        public double TotalDsmDeltaVKms => Legs != null ? Legs.Sum(l => l.DSM != null ? l.DSM.DeltaVMs / 1000.0 : 0.0) : 0.0;

        // ===== 硬门禁合规审计字段 (Hard Gate Compliance) =====
        // 由 DE RunWindow 最终选择阶段统一赋值：无论进化期采用何种罚函数引导收敛，
        // 上报的最优解都必须经过严格硬校验，超标值显式标记，绝不让超门禁解冒充达标。
        public bool MeetsHardDvGate { get; set; }                 // 总ΔV + 发射 + 单腿/总DSM 全部落入硬门禁
        public double LaunchOverBudgetMs { get; set; }            // 发射超支 (>0 即超 LaunchDeltaVCeilingMs)
        public double DsmOverBudgetMs { get; set; }               // 总DSM 超支 (>0 即超 MaxDsmDeltaVMs)
        public double TotalOverBudgetMs { get; set; }             // 任务总ΔV 超支 (>0 即超 VesselMaxDeltaVMs)
        public List<MGALegResult> Legs { get; set; } = new List<MGALegResult>();
        public List<MGAEventItem> Events { get; set; } = new List<MGAEventItem>();
        public bool AllLegsFeasible { get; set; }
        public string FlybyChainSummary { get; set; }
        public string CentralBody { get; set; } = "Sun";

        // Departure Orbit Constraint State (模式 1: 发射前规划 vs 模式 2: 已在轨转移锁死)
        public MGADepartureMode DepartureMode { get; set; } = MGADepartureMode.PreLaunchFreeOrbit;
        public double RecommendedLaunchIncDeg { get; set; } = 0.0;
        public double RecommendedLaunchRaanDeg { get; set; } = 0.0;

        // Advanced Astrodynamics Results (Features 2 - 6)
        public PrimerVectorResult PrimerVector { get; set; }
        public BPlaneResult BPlane { get; set; }
        public TCMResult SuggestedTCM { get; set; }
        public LowEnergyManifoldResult ManifoldCapture { get; set; }
        public MEEResult DepartureMEE { get; set; }
        public SimsFlanaganResult LowThrustSolution { get; set; }
        public List<ParetoTradeOffPoint> ParetoFront { get; set; } = new List<ParetoTradeOffPoint>();

        // Parking Orbit Phase Alignment State (停泊轨道点火相位对齐状态)
        public bool IsPhaseAligned { get; set; } = false;
        public double PhaseAlignmentShiftSec { get; set; } = 0.0;
        public double OriginalDepartureUT { get; set; } = 0.0;
        public double OriginalDepartureDeltaVKms { get; set; } = 0.0;
        public double OriginalProgradeMs { get; set; } = 0.0;
        public double OriginalRadialMs { get; set; } = 0.0;
        public double OriginalNormalMs { get; set; } = 0.0;
        public double OriginalTotalDeltaVKms { get; set; } = 0.0;
        public double OriginalToFDays { get; set; } = 0.0;

        public void BuildEvents(MGAConfig config) {
            Events.Clear();
            if (Legs == null || Legs.Count == 0) return;

            int seq = 1;
            var leg0 = Legs[0];

            bool isCentralDep = (config != null && !string.IsNullOrEmpty(config.CentralBody) && 
                                leg0.FromBody.Equals(config.CentralBody, StringComparison.OrdinalIgnoreCase));
            string depTitle = isCentralDep ? $"{seq}: {leg0.FromBody} injection" : $"{seq}: {leg0.FromBody} escape";

            // 1. 出发逃逸/注入机动 (Origin Escape / Injection)
            Events.Add(new MGAEventItem {
                SequenceNumber = seq,
                Title = depTitle,
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
                    InsertionAltitudeKm = (config != null) ? config.DestinationAltitudeKm : MGADefaults.DestinationAltitudeKm,
                    InsertionInclinationDeg = lastLeg.InsertionInclinationDeg,
                    IsFeasible = true
                });
            }
        }
    }
}
