using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Telemetry;

namespace KSPGMAT.Model {
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

        // B 平面协方差摄动分析与误差椭圆 (Covariance Dispersion Ellipse & Navigational Robustness)
        public double SigmaBTKm { get; set; }
        public double SigmaBRKm { get; set; }
        public double CovarianceBTRKm2 { get; set; }
        public double SemiMajorAxisSigmaKm { get; set; }
        public double SemiMinorAxisSigmaKm { get; set; }
        public double EllipseAngleDeg { get; set; }
        public double SafeFlybyRadiusKm { get; set; }
        public double EstimatedPericenterKm { get; set; }
        public double SafeMargin3SigmaKm { get; set; }
        public bool IsSafeFlyby { get; set; } = true;
        public double NavigationalTcmBudgetKms { get; set; }

        /// <summary>
        /// DirectCompute GPU 极速蒙特卡洛 6.5 万点轨道分散度与 B 平面协方差椭圆分析
        /// </summary>
        public static BPlaneResult ComputeMonteCarloDispersionGPU(
            Vector3d r0Km, Vector3d v0Kms,
            Vector3d targetRKm, Vector3d targetVKms,
            double targetMu, double targetRadiusKm,
            double tofSec, double centralMu,
            double sigmaPosKm = 5.0, double sigmaVelKms = 0.001,
            int sampleCount = 65536)
        {
            var res = new BPlaneResult();
            if (!PykepNative.IsGpuComputeAvailable)
            {
                MGAPipelineTelemetryProbe.RecordGpuExecution("GPU B-Plane Monte Carlo", 0.0, sampleCount, false);
                return res;
            }

            var sw = Stopwatch.StartNew();
            double[] r0 = new double[] { r0Km.x, r0Km.y, r0Km.z };
            double[] v0 = new double[] { v0Kms.x, v0Kms.y, v0Kms.z };
            double[] tr = new double[] { targetRKm.x, targetRKm.y, targetRKm.z };
            double[] tv = new double[] { targetVKms.x, targetVKms.y, targetVKms.z };

            int ret = PykepNative.BPlaneMonteCarloGpuEvaluate(
                r0, v0, tr, tv,
                targetMu, targetRadiusKm, tofSec, centralMu,
                sigmaPosKm, sigmaVelKms, sampleCount,
                out float meanBR, out float meanBT,
                out float sigRR, out float sigTT, out float sigRT,
                out float collProb);

            sw.Stop();
            MGAPipelineTelemetryProbe.RecordGpuExecution(
                "GPU B-Plane Monte Carlo", sw.Elapsed.TotalMilliseconds, sampleCount, ret == 0);

            if (ret == 0)
            {
                res.BDotRKm = meanBR;
                res.BDotTKm = meanBT;
                res.BMagKm = Math.Sqrt(meanBR * meanBR + meanBT * meanBT);
                res.ThetaDeg = Math.Atan2(meanBR, meanBT) * 180.0 / Math.PI;
                res.SigmaBRKm = sigRR;
                res.SigmaBTKm = sigTT;
                res.CovarianceBTRKm2 = sigRT;

                // 2D 协方差椭圆主轴特征值求解
                double a = sigRR * sigRR;
                double b = sigRT;
                double c = sigTT * sigTT;
                double lambda1 = 0.5 * (a + c + Math.Sqrt((a - c) * (a - c) + 4.0 * b * b));
                double lambda2 = 0.5 * (a + c - Math.Sqrt((a - c) * (a - c) + 4.0 * b * b));

                res.SemiMajorAxisSigmaKm = 3.0 * Math.Sqrt(Math.Max(0.0, lambda1));
                res.SemiMinorAxisSigmaKm = 3.0 * Math.Sqrt(Math.Max(0.0, lambda2));
                res.EllipseAngleDeg = 0.5 * Math.Atan2(2.0 * b, a - c) * 180.0 / Math.PI;
                res.IsSafeFlyby = collProb < 1e-4;
            }

            return res;
        }
    }

    /// <summary>
    /// 多体引力流形低能捕获与弱稳定性边界 (CR3BP Invariant Manifolds & WSB Low-Energy Capture)
    /// </summary>
    public class LowEnergyManifoldResult {
        public bool IsManifoldCaptureFeasible { get; set; }
        public double JacobiConstant { get; set; }
        public double HillRadiusKm { get; set; }
        public double WsbCaptureDeltaVKms { get; set; }
        public double ClassicalVisVivaDeltaVKms { get; set; }
        public double DeltaVSavedKms { get; set; }
        public double SavingPercentage { get; set; }
        public double IngressAngleDeg { get; set; }
        public double BallisticDurationDays { get; set; }
        public string Details { get; set; }
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

        public double IncomingDhaDeg { get; set; }
        public double IncomingDecDeg { get; set; }
        public BPlaneResult BPlane { get; set; }

        public bool HasDSM { get; set; }
        public DSMResult DSM { get; set; }
        public Vector3d DSMPositionKm { get; set; }
        public MGALegStrategy Strategy { get; set; } = MGALegStrategy.Ballistic;
    }
}
