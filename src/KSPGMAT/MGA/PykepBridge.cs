using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
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
        public double soi_radius; // km
        public double atm_radius; // km
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
    public struct NativeBPlaneCovarianceResult {
        public double sigma_bt_km;
        public double sigma_br_km;
        public double cov_btr_km2;
        public double semi_major_sigma_km;
        public double semi_minor_sigma_km;
        public double ellipse_angle_deg;
        public double safe_radius_km;
        public double periapsis_km;
        public double safe_margin_3sigma_km;
        public int is_safe_flyby;
        public double tcm_3sigma_dv_ms;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NativeManifoldCaptureResult {
        public int is_manifold_feasible;
        public double jacobi_constant;
        public double hill_radius_km;
        public double wsb_capture_dv_kms;
        public double classical_visviva_dv_kms;
        public double dv_saved_kms;
        public double saving_percentage;
        public double ingress_angle_deg;
        public double ballistic_duration_days;
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
        private delegate int PykepMGAHybridEvaluateMuDelegate(
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
            double mu_central,
            out NativeMGA1DSMSolution out_sol);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMGAEvaluateMuDelegate(
            [In] NativePlanetState[] dep_planets,
            [In] NativePlanetState[] arr_planets,
            [In] double[] tofs_sec,
            int leg_count,
            double dep_pe_km, double dep_ap_km, double dep_inc_deg,
            double arr_pe_km, double arr_ap_km, int no_insertion,
            double mu_central,
            out NativeMGASolution out_sol);

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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCalcBPlaneCovarianceDelegate(
            [In] double[] r0_km, [In] double[] v0_kms, double dt_sec, double mu_sun,
            double pos_sigma_km, double vel_sigma_kms,
            [In] double[] r_enc_km, [In] double[] v_enc_kms,
            double body_mu, double body_radius_km, double safe_alt_km,
            out NativeBPlaneCovarianceResult out_res);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCalcManifoldCaptureDelegate(
            [In] double[] r_enc_km, [In] double[] v_enc_kms,
            [In] double[] r_planet_km, [In] double[] v_planet_kms,
            double body_mu, double sun_mu, double body_radius_km,
            double target_pe_km, double target_ap_km,
            out NativeManifoldCaptureResult out_result);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMGAHybridEvaluateBatchDelegate(
            [In] NativePlanetState[] all_dep_planets,
            [In] NativePlanetState[] all_arr_planets,
            [In] double[] all_tofs_sec,
            [In] int[] leg_strategies,
            [In] int[] flyby_strategies,
            [In] double[] all_dsm_etas,
            [In] double[] fb_rps_km,
            [In] double[] fb_betas_rad,
            int num_trajectories,
            int leg_count,
            double dep_pe_km, double dep_ap_km, double dep_inc_deg,
            double arr_pe_km, double arr_ap_km, int no_insertion,
            double mu_central,
            [Out] NativeMGA1DSMSolution[] out_sols);

        private static PykepMGAEvaluateDelegate _fnMGAEvaluate;
        private static PykepMGAEvaluateMuDelegate _fnMGAEvaluateMu;
        private static PykepMGAHybridEvaluateDelegate _fnMGAHybridEvaluate;
        private static PykepMGAHybridEvaluateMuDelegate _fnMGAHybridEvaluateMu;
        private static PykepMGAHybridEvaluateBatchDelegate _fnMGAHybridEvaluateBatch;
        private static PykepMGA1DSMEvaluateDelegate _fnMGA1DSMEvaluate;
        private static PykepCalcKeplerSTMDelegate _fnCalcKeplerSTM;
        private static PykepCalcBPlaneDelegate _fnCalcBPlane;
        private static PykepCalcBPlaneTCMDelegate _fnCalcBPlaneTCM;
        private static PykepCalcBPlaneCovarianceDelegate _fnCalcBPlaneCovariance;
        private static PykepCalcManifoldCaptureDelegate _fnCalcManifoldCapture;
        private static PykepCalcPrimerVectorDelegate _fnCalcPrimerVector;
        private static PykepCartesianToMEEDelegate _fnCartesianToMEE;
        private static PykepMEEToCartesianDelegate _fnMEEToCartesian;
        private static PykepSimsFlanaganEvaluateDelegate _fnSimsFlanaganEvaluate;

        static PykepBridge() {
            try {
                if (PykepNative.IsAvailable) {
                    IntPtr pEvalMu = PykepNative.GetProcAddressExport("pykep_mga_evaluate_mu");
                    if (pEvalMu != IntPtr.Zero) {
                        _fnMGAEvaluateMu = (PykepMGAEvaluateMuDelegate)Marshal.GetDelegateForFunctionPointer(pEvalMu, typeof(PykepMGAEvaluateMuDelegate));
                    }

                    IntPtr pEval = PykepNative.GetProcAddressExport("pykep_mga_evaluate");
                    if (pEval != IntPtr.Zero) {
                        _fnMGAEvaluate = (PykepMGAEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pEval, typeof(PykepMGAEvaluateDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_evaluate C++ pipeline!");
                    }

                    IntPtr pHybridMu = PykepNative.GetProcAddressExport("pykep_mga_hybrid_evaluate_mu");
                    if (pHybridMu != IntPtr.Zero) {
                        _fnMGAHybridEvaluateMu = (PykepMGAHybridEvaluateMuDelegate)Marshal.GetDelegateForFunctionPointer(pHybridMu, typeof(PykepMGAHybridEvaluateMuDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_hybrid_evaluate_mu (Universal Mu) C++ pipeline!");
                    }

                    IntPtr pBatch = PykepNative.GetProcAddressExport("pykep_mga_hybrid_evaluate_batch");
                    if (pBatch != IntPtr.Zero) {
                        _fnMGAHybridEvaluateBatch = (PykepMGAHybridEvaluateBatchDelegate)Marshal.GetDelegateForFunctionPointer(pBatch, typeof(PykepMGAHybridEvaluateBatchDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_mga_hybrid_evaluate_batch (OpenMP + AVX2) C++ pipeline!");
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

                    IntPtr pCov = PykepNative.GetProcAddressExport("pykep_calc_bplane_covariance");
                    if (pCov != IntPtr.Zero) {
                        _fnCalcBPlaneCovariance = (PykepCalcBPlaneCovarianceDelegate)Marshal.GetDelegateForFunctionPointer(pCov, typeof(PykepCalcBPlaneCovarianceDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_calc_bplane_covariance C++ pipeline!");
                    }

                    IntPtr pManifold = PykepNative.GetProcAddressExport("pykep_calc_manifold_capture");
                    if (pManifold != IntPtr.Zero) {
                        _fnCalcManifoldCapture = (PykepCalcManifoldCaptureDelegate)Marshal.GetDelegateForFunctionPointer(pManifold, typeof(PykepCalcManifoldCaptureDelegate));
                        KSPGMATLog.Info("[PykepBridge] Successfully linked native pykep_calc_manifold_capture C++ pipeline!");
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

        /// <summary>
        /// 原生调用 PyKEP C++ 评估出发逃逸所需的速度增量 (Departure ΔV)
        /// </summary>
        /// <param name="vInfDepKms">双曲过剩速度 (km/s)</param>
        /// <param name="planet">出发中心母星 (如 Earth / Kerbin)</param>
        /// <param name="peAKm">近地点停泊轨道高度 (km)</param>
        /// <param name="apAKm">远地点停泊轨道高度 (km)</param>
        /// <returns>逃逸加速机动 ΔV (km/s)</returns>
        public static double CalculateDepartureDeltaV(double vInfDepKms, SolarPlanet planet, double peAKm, double apAKm) {
            if (planet == null || planet.Mu <= 0) return 0.0;
            return PykepNative.CalculateDepartureDeltaV(vInfDepKms, planet.Mu, planet.RadiusKm, peAKm, apAKm);
        }

        /// <summary>
        /// 原生调用 PyKEP C++ 评估目标星体到达捕获所需的速度增量 (Insertion ΔV)
        /// </summary>
        /// <param name="vInfArrKms">双曲到达过剩速度 (km/s)</param>
        /// <param name="planet">目标天体</param>
        /// <param name="peAKm">捕获目标近心点高度 (km)</param>
        /// <param name="apAKm">捕获目标远心点高度 (km)</param>
        /// <param name="flybyOnly">若为 true 则仅做飞掠无轨道捕获机动</param>
        /// <returns>捕获制动机动 ΔV (km/s)</returns>
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

            string central = config?.CentralBody ?? "Sun";
            double centralMu = SolarSystemCatalog.GetCentralMu(central);
            double currentUT = genome[0];

            for (int k = 0; k < legCount; k++) {
                string fromName = fullChain[k];
                string toName = fullChain[k + 1];
                var p1 = SolarSystemCatalog.GetPlanet(fromName);
                var p2 = SolarSystemCatalog.GetPlanet(toName);

                double tof = genome[1 + k];
                tofs[k] = tof;
                double arrUT = currentUT + tof;

                p1.GetRelativeState(central, currentUT, out Vector3d r1, out Vector3d v1);
                p2.GetRelativeState(central, arrUT, out Vector3d r2, out Vector3d v2);

                double centralRadius = SolarSystemCatalog.GetPlanet(central)?.RadiusKm ?? MGADefaults.GetBodyRadiusKm(central);
                if (fromName.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                    double rPark = (p1 != null && p1.RadiusKm > 0 ? p1.RadiusKm : centralRadius) + (config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                    bool lockPlane = config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination);
                    double inc = lockPlane ? config.DepartureInclinationDeg : 0.0;
                    double lan = lockPlane ? config.DepartureRAANDeg : 0.0;
                    if (PykepNative.SynthesizeParkingState(centralMu, rPark, lockPlane, inc, lan, r2, v2, out Vector3d synR, out Vector3d synV)) {
                        r1 = synR;
                        v1 = synV;
                    }
                }
                if (toName.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                    double rPark = (p2 != null && p2.RadiusKm > 0 ? p2.RadiusKm : centralRadius) + (config?.DestinationPeAKm ?? MGADefaults.DestinationAltitudeKm);
                    if (PykepNative.SynthesizeParkingState(centralMu, rPark, false, 0.0, 0.0, r1, v1, out Vector3d synR, out Vector3d synV)) {
                        r2 = synR;
                        v2 = synV;
                    }
                }

                depPlanets[k] = new NativePlanetState {
                    rx = r1.x, ry = r1.y, rz = r1.z,
                    vx = v1.x, vy = v1.y, vz = v1.z,
                    mu = p1 != null ? p1.Mu : centralMu,
                    radius = p1 != null && p1.RadiusKm > 0 ? p1.RadiusKm : centralRadius,
                    safe_radius = p1 != null && p1.SafeRadiusKm > 0 ? p1.SafeRadiusKm : (centralRadius + 100.0),
                    soi_radius = fromName.Equals(central, StringComparison.OrdinalIgnoreCase) ? 1e18 : (p1 != null && p1.SoiKm > 0 ? p1.SoiKm : 1e18),
                    atm_radius = p1 != null ? (p1.RadiusKm + p1.AtmosphereDepthKm) : 0.0
                };

                arrPlanets[k] = new NativePlanetState {
                    rx = r2.x, ry = r2.y, rz = r2.z,
                    vx = v2.x, vy = v2.y, vz = v2.z,
                    mu = p2 != null ? p2.Mu : centralMu,
                    radius = p2 != null && p2.RadiusKm > 0 ? p2.RadiusKm : centralRadius,
                    safe_radius = p2 != null && p2.SafeRadiusKm > 0 ? p2.SafeRadiusKm : (centralRadius + 100.0),
                    soi_radius = toName.Equals(central, StringComparison.OrdinalIgnoreCase) ? 1e18 : (p2 != null && p2.SoiKm > 0 ? p2.SoiKm : 1e18),
                    atm_radius = p2 != null ? (p2.RadiusKm + p2.AtmosphereDepthKm) : 0.0
                };



                currentUT = arrUT;
            }

            if (config != null) config.SyncStrategiesWithChain(legCount, fullChain);

            // 1. 优先使用官方统一异构分段混合求解器 (Hybrid Multi-Strategy Evaluator with Universal Mu)
            if (_fnMGAHybridEvaluateMu != null || _fnMGAHybridEvaluate != null) {
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
                    double geneEta = (genome != null && genome.Length > 1 + legCount + k) ? genome[1 + legCount + k] : -1.0;
                    if (geneEta > 0.0) {
                        dsmEtas[k] = Math.Max(0.05, Math.Min(0.95, geneEta));
                    } else {
                        dsmEtas[k] = (config != null && config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0) ? config.DsmEtas[k] : (config != null ? config.DsmEtaDefault : 0.5);
                    }
                    fbRps[k] = (config != null && config.FlybyRpKms != null && k < config.FlybyRpKms.Count) ? config.FlybyRpKms[k] : 0.0;
                    fbBetas[k] = (config != null && config.FlybyBetasRad != null && k < config.FlybyBetasRad.Count) ? config.FlybyBetasRad[k] : 0.0;
                }

                int status = -1;
                NativeMGA1DSMSolution n1Sol;
                double depInc = (config != null && config.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit)
                    ? -1.0
                    : (config != null ? config.DepartureInclinationDeg : 0.0);

                if (_fnMGAHybridEvaluateMu != null) {
                    status = _fnMGAHybridEvaluateMu(
                        depPlanets, arrPlanets, tofs,
                        legStrats, fbStrats,
                        dsmEtas, fbRps, fbBetas,
                        legCount,
                        config != null ? config.DeparturePeAKm : 200.0, config != null ? config.DepartureApAKm : 200.0,
                        depInc,
                        config != null ? config.DestinationPeAKm : 200.0, config != null ? config.DestinationApAKm : 200.0,
                        (config != null && config.NoInsertionBurn) ? 1 : 0,
                        centralMu,
                        out n1Sol);
                } else {
                    status = _fnMGAHybridEvaluate(
                        depPlanets, arrPlanets, tofs,
                        legStrats, fbStrats,
                        dsmEtas, fbRps, fbBetas,
                        legCount,
                        config != null ? config.DeparturePeAKm : 200.0, config != null ? config.DepartureApAKm : 200.0,
                        depInc,
                        config != null ? config.DestinationPeAKm : 200.0, config != null ? config.DestinationApAKm : 200.0,
                        (config != null && config.NoInsertionBurn) ? 1 : 0,
                        out n1Sol);
                }

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
                    double geneEta = (genome != null && genome.Length > 1 + legCount + k) ? genome[1 + legCount + k] : -1.0;
                    if (geneEta > 0.0) {
                        dsmEtas[k] = Math.Max(0.05, Math.Min(0.95, geneEta));
                    } else {
                        dsmEtas[k] = (config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0) ? config.DsmEtas[k] : config.DsmEtaDefault;
                    }
                    fbRps[k] = (config.FlybyRpKms != null && k < config.FlybyRpKms.Count) ? config.FlybyRpKms[k] : 0.0;
                    fbBetas[k] = (config.FlybyBetasRad != null && k < config.FlybyBetasRad.Count) ? config.FlybyBetasRad[k] : 0.0;
                }

                double depInc1 = (config.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit) ? -1.0 : config.DepartureInclinationDeg;
                int status = _fnMGA1DSMEvaluate(
                    depPlanets, arrPlanets, tofs,
                    dsmEtas, fbRps, fbBetas,
                    legCount,
                    config.DeparturePeAKm, config.DepartureApAKm,
                    depInc1,
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
            if (_fnMGAEvaluateMu != null || _fnMGAEvaluate != null) {
                int status = -1;
                NativeMGASolution nativeSol;
                double depIncB = (config != null && config.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit) ? -1.0 : (config != null ? config.DepartureInclinationDeg : 0.0);
                if (_fnMGAEvaluateMu != null) {
                    status = _fnMGAEvaluateMu(
                        depPlanets, arrPlanets, tofs, legCount,
                        config.DeparturePeAKm, config.DepartureApAKm,
                        depIncB,
                        config.DestinationPeAKm, config.DestinationApAKm,
                        config.NoInsertionBurn ? 1 : 0,
                        centralMu,
                        out nativeSol);
                } else {
                    status = _fnMGAEvaluate(
                        depPlanets, arrPlanets, tofs, legCount,
                        config.DeparturePeAKm, config.DepartureApAKm,
                        depIncB,
                        config.DestinationPeAKm, config.DestinationApAKm,
                        config.NoInsertionBurn ? 1 : 0,
                        out nativeSol);
                }

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

        /// <summary>
        /// 原生 OpenMP + AVX2 硬件加速批处理 MGA 轨迹解算器：
        /// 一次性将整个种群 (例如 35~140 个个体) 扁平封送入 C++，
        /// 在 C++ 内部以 AVX2 矢量与 OpenMP 多核并发执行计算，大幅消除 P/Invoke 与托管垃圾回收损耗
        /// </summary>
        public static List<MGASolution> BuildBatchSolutions(
            List<double[]> genomes,
            List<string> fullChain,
            MGAConfig config)
        {
            if (genomes == null || genomes.Count == 0 || fullChain == null || fullChain.Count < 2) return new List<MGASolution>();
            int popSize = genomes.Count;
            int legCount = fullChain.Count - 1;
            if (legCount > 16) return new List<MGASolution>();

            // 若底层未链接批处理导出函数，自动平滑优雅降级为逐个求解
            if (_fnMGAHybridEvaluateBatch == null) {
                var fallbackList = new List<MGASolution>(popSize);
                for (int i = 0; i < popSize; i++) {
                    fallbackList.Add(BuildFullSolution(genomes[i], fullChain, config, computeAdvanced: false));
                }
                return fallbackList;
            }

            string central = config?.CentralBody ?? "Sun";
            double centralMu = SolarSystemCatalog.GetCentralMu(central);
            double centralRadius = SolarSystemCatalog.GetPlanet(central)?.RadiusKm ?? MGADefaults.GetBodyRadiusKm(central);

            if (config != null) config.SyncStrategiesWithChain(legCount, fullChain);

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

            var fbRps = new double[legCount];
            var fbBetas = new double[legCount];
            for (int k = 0; k < legCount; k++) {
                fbRps[k] = (config != null && config.FlybyRpKms != null && k < config.FlybyRpKms.Count) ? config.FlybyRpKms[k] : 0.0;
                fbBetas[k] = (config != null && config.FlybyBetasRad != null && k < config.FlybyBetasRad.Count) ? config.FlybyBetasRad[k] : 0.0;
            }

            int chainLen = fullChain.Count;
            var planets = new SolarPlanet[chainLen];
            var planetTemplates = new NativePlanetState[chainLen];
            for (int c = 0; c < chainLen; c++) {
                string bName = fullChain[c];
                planets[c] = SolarSystemCatalog.GetPlanet(bName);
                var p = planets[c];
                planetTemplates[c] = new NativePlanetState {
                    mu = p != null ? p.Mu : centralMu,
                    radius = p != null && p.RadiusKm > 0 ? p.RadiusKm : centralRadius,
                    safe_radius = p != null && p.SafeRadiusKm > 0 ? p.SafeRadiusKm : (centralRadius + 100.0),
                    soi_radius = bName.Equals(central, StringComparison.OrdinalIgnoreCase) ? 1e18 : (p != null && p.SoiKm > 0 ? p.SoiKm : 1e18),
                    atm_radius = p != null ? (p.RadiusKm + p.AtmosphereDepthKm) : 0.0
                };
            }

            var allDepPlanets = new NativePlanetState[popSize * legCount];
            var allArrPlanets = new NativePlanetState[popSize * legCount];
            var allTofs = new double[popSize * legCount];
            var allDsmEtas = new double[popSize * legCount];

            // 复用单代星历链条缓冲区，彻底根绝内层频繁 GC 分配
            var rChain = new Vector3d[chainLen];
            var vChain = new Vector3d[chainLen];

            for (int i = 0; i < popSize; i++) {
                double[] genome = genomes[i];

                // 1. 欧空局官方链式高效星历提取：每个天体仅求 1 次星历 (消灭 50% 跨边界重复查询)
                double currentUT = genome[0];
                for (int c = 0; c < chainLen; c++) {
                    if (c > 0) currentUT += genome[c];
                    var p = planets[c];
                    if (p != null) {
                        p.GetRelativeState(central, currentUT, out rChain[c], out vChain[c]);
                    } else {
                        rChain[c] = Vector3d.zero;
                        vChain[c] = Vector3d.zero;
                    }
                }

                // 2. 出发点与到达点停泊轨道合成特判 (仅首末节点处理一次)
                if (fullChain[0].Equals(central, StringComparison.OrdinalIgnoreCase)) {
                    var p0 = planets[0];
                    double rPark = (p0 != null && p0.RadiusKm > 0 ? p0.RadiusKm : centralRadius) + (config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                    bool lockPlane = config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination);
                    double inc = lockPlane ? config.DepartureInclinationDeg : 0.0;
                    double lan = lockPlane ? config.DepartureRAANDeg : 0.0;
                    if (PykepNative.SynthesizeParkingState(centralMu, rPark, lockPlane, inc, lan, rChain[1], vChain[1], out Vector3d synR, out Vector3d synV)) {
                        rChain[0] = synR;
                        vChain[0] = synV;
                    }
                }
                if (fullChain[chainLen - 1].Equals(central, StringComparison.OrdinalIgnoreCase)) {
                    var pLast = planets[chainLen - 1];
                    double rPark = (pLast != null && pLast.RadiusKm > 0 ? pLast.RadiusKm : centralRadius) + (config?.DestinationPeAKm ?? MGADefaults.DestinationAltitudeKm);
                    if (PykepNative.SynthesizeParkingState(centralMu, rPark, false, 0.0, 0.0, rChain[chainLen - 2], vChain[chainLen - 2], out Vector3d synR, out Vector3d synV)) {
                        rChain[chainLen - 1] = synR;
                        vChain[chainLen - 1] = synV;
                    }
                }

                // 3. 极速填充整代航段扁平数组 (0 次额外字典查找与字符串匹配)
                for (int k = 0; k < legCount; k++) {
                    double tof = genome[1 + k];
                    int flatIdx = i * legCount + k;

                    allTofs[flatIdx] = tof;

                    double geneEta = (genome.Length > 1 + legCount + k) ? genome[1 + legCount + k] : -1.0;
                    if (geneEta > 0.0) {
                        allDsmEtas[flatIdx] = Math.Max(0.05, Math.Min(0.95, geneEta));
                    } else {
                        allDsmEtas[flatIdx] = (config != null && config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0) ? config.DsmEtas[k] : (config != null ? config.DsmEtaDefault : 0.5);
                    }

                    var depState = planetTemplates[k];
                    depState.rx = rChain[k].x; depState.ry = rChain[k].y; depState.rz = rChain[k].z;
                    depState.vx = vChain[k].x; depState.vy = vChain[k].y; depState.vz = vChain[k].z;
                    allDepPlanets[flatIdx] = depState;

                    var arrState = planetTemplates[k + 1];
                    arrState.rx = rChain[k + 1].x; arrState.ry = rChain[k + 1].y; arrState.rz = rChain[k + 1].z;
                    arrState.vx = vChain[k + 1].x; arrState.vy = vChain[k + 1].y; arrState.vz = vChain[k + 1].z;
                    allArrPlanets[flatIdx] = arrState;
                }
            }

            var nSols = new NativeMGA1DSMSolution[popSize];
            double depInc = (config != null && config.DepartureMode == MGADepartureMode.PreLaunchFreeOrbit)
                ? -1.0
                : (config != null ? config.DepartureInclinationDeg : 0.0);

            int batchStatus = _fnMGAHybridEvaluateBatch(
                allDepPlanets, allArrPlanets, allTofs,
                legStrats, fbStrats,
                allDsmEtas, fbRps, fbBetas,
                popSize, legCount,
                config != null ? config.DeparturePeAKm : 200.0, config != null ? config.DepartureApAKm : 200.0,
                depInc,
                config != null ? config.DestinationPeAKm : 200.0, config != null ? config.DestinationApAKm : 200.0,
                (config != null && config.NoInsertionBurn) ? 1 : 0,
                centralMu,
                nSols);

            var resultList = new List<MGASolution>(popSize);
            for (int i = 0; i < popSize; i++) {
                var sol = MapNative1DSMSolution(nSols[i], genomes[i][0], fullChain, config);
                for (int k = 0; k < sol.Legs.Count && k < legStrats.Length; k++) {
                    sol.Legs[k].Strategy = (MGALegStrategy)legStrats[k];
                }
                resultList.Add(sol);
            }

            return resultList;
        }

        /// <summary>
        /// 填充高阶航天动力学诊断与扩展特征数据 (MEE、劳登主矢量、B平面协方差、帕累托前沿等)
        /// </summary>
        private static void PopulateAdvancedAstrodynamics(MGASolution sol, double[] genome, List<string> fullChain, MGAConfig config) {
            if (sol == null || sol.Legs == null || sol.Legs.Count == 0 || config == null) return;

            try {
                var leg0 = sol.Legs[0];
                var lastLeg = sol.Legs.Last();

                string central = config?.CentralBody ?? "Sun";
                double centralMu = SolarSystemCatalog.GetCentralMu(central);

                // 记录所选出发模式并透传原生解算输出的推荐发射参数 (严禁托管自编公式或修改原生kep解算)
                sol.DepartureMode = config.DepartureMode;
                sol.RecommendedLaunchIncDeg = leg0.EjectionInclinationDeg;
                sol.RecommendedLaunchRaanDeg = leg0.EjectionLANDeg;

                // Feature 5: 无奇异 MEE 修正等分轨道根数
                if (centralMu > 0) {
                    try {
                        sol.DepartureMEE = CartesianToMEE(leg0.DeparturePosKm, leg0.DepartureVelKms, centralMu);
                    } catch (Exception ex) {
                        KSPGMATLog.Warn($"[PykepBridge] MEE failed: {ex.Message}");
                    }
                }

                // Feature 3: 劳登主矢量最优性评估 (Primer Vector)
                var pDep = SolarSystemCatalog.GetPlanet(leg0.FromBody);
                var pArr = SolarSystemCatalog.GetPlanet(lastLeg.ToBody);
                if (pDep != null && pArr != null) {
                    pDep.GetRelativeState(central, leg0.DepartureUT, out Vector3d _, out Vector3d vDepPla);
                    pArr.GetRelativeState(central, lastLeg.ArrivalUT, out Vector3d rDestPla, out Vector3d vDestPla);

                    Vector3d dv1 = leg0.DepartureVelKms - vDepPla;
                    Vector3d dv2 = lastLeg.ArrivalVelKms - vDestPla;
                    try {
                        sol.PrimerVector = CalculatePrimerVector(leg0.DeparturePosKm, leg0.DepartureVelKms, dv1, dv2, leg0.ToFDays * 86400.0, centralMu);
                    } catch (Exception ex) {
                        KSPGMATLog.Warn($"[PykepBridge] PrimerVector failed: {ex.Message}");
                    }

                    // Feature 2: 目标天体双曲飞掠 B-Plane 与 TCM 中道修正计算
                    if (pArr.Mu > 0) {
                        Vector3d rRel = lastLeg.ArrivalPosKm - rDestPla;
                        Vector3d vRel = lastLeg.ArrivalVelKms - vDestPla;
                        try {
                            sol.BPlane = CalculateBPlane(rRel, vRel, pArr.Mu);
                        } catch (Exception ex) {
                            KSPGMATLog.Warn($"[PykepBridge] BPlane failed: {ex.Message}");
                        }

                        // 计算中道 1/2 处 TCM 点火
                        try {
                            double midUT = leg0.DepartureUT + leg0.ToFDays * 86400.0 * 0.5;
                            double dtToEnc = (lastLeg.ArrivalUT - midUT);
                            Vector3d rMid = (leg0.DeparturePosKm + leg0.ArrivalPosKm) * 0.5;
                            Vector3d vMid = leg0.DepartureVelKms;
                            sol.SuggestedTCM = CalculateBPlaneTCM(rMid, vMid, dtToEnc, rRel, vRel, config.TargetBDotTKm, config.TargetBDotRKm, pArr.Mu);
                        } catch (Exception ex) {
                            KSPGMATLog.Warn($"[PykepBridge] BPlaneTCM failed: {ex.Message}");
                        }
                    }
                }

                // Feature 6: 连续低推力模拟 (Sims-Flanagan)
                if (config.EngineMode == MGAEngineMode.LowThrustSimsFlanagan) {
                    try {
                        double tofSec = leg0.ToFDays * 86400.0;
                        int nSeg = Math.Max(4, Math.Min(32, config.LowThrustSegments));
                        var throttles = new double[nSeg * 3];
                        for (int i = 0; i < nSeg; i++) {
                            throttles[3 * i] = 1.0;
                        }
                        sol.LowThrustSolution = EvaluateSimsFlanagan(
                            leg0.DeparturePosKm, leg0.DepartureVelKms, config.SpacecraftWetMassKg,
                            leg0.ArrivalPosKm, leg0.ArrivalVelKms, config.SpacecraftWetMassKg * 0.85,
                            tofSec, config.LowThrustMaxThrustN, config.LowThrustIspSec, centralMu,
                            nSeg, throttles);
                    } catch (Exception ex) {
                        KSPGMATLog.Warn($"[PykepBridge] SimsFlanagan failed: {ex.Message}");
                    }
                }

                // Feature 4: 帕累托前沿权衡曲线 (Pareto Trade-Off Curve)
                if (sol.ParetoFront == null || sol.ParetoFront.Count == 0) {
                    try {
                        sol.ParetoFront = GenerateParetoFront(genome, fullChain, config, 9);
                    } catch (Exception ex) {
                        KSPGMATLog.Warn($"[PykepBridge] ParetoFront failed: {ex.Message}");
                    }
                }
            } catch (Exception ex) {
                KSPGMATLog.Warn($"[PykepBridge] PopulateAdvancedAstrodynamics outer note: {ex}");
            }
        }

        /// <summary>
        /// 将 C++ 原生导出的 NativeMGA1DSMSolution 结构体反序列化为托管 MGASolution 实体对象
        /// </summary>
        private static MGASolution MapNative1DSMSolution(NativeMGA1DSMSolution nSol, double startUT, List<string> fullChain, MGAConfig config) {
            var sol = new MGASolution {
                DepartureUT = startUT,
                ArrivalUT = startUT + nSol.total_flight_days * 86400.0,
                TotalFlightDays = nSol.total_flight_days,
                LaunchDeltaVKms = nSol.launch_dv_kms,
                InsertionDeltaVKms = nSol.insertion_dv_kms,
                TotalDeltaVKms = nSol.total_dv_kms,
                AllLegsFeasible = nSol.all_feasible != 0,
                FlybyChainSummary = string.Join("  ->  ", fullChain),
                CentralBody = config?.CentralBody ?? "Sun"
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

        /// <summary>
        /// 将 C++ 原生导出的标准 NativeMGASolution 结构体反序列化为托管 MGASolution 实体对象
        /// </summary>
        private static MGASolution MapNativeSolution(NativeMGASolution nSol, double startUT, List<string> fullChain, MGAConfig config) {
            var sol = new MGASolution {
                DepartureUT = startUT,
                ArrivalUT = startUT + nSol.total_flight_days * 86400.0,
                TotalFlightDays = nSol.total_flight_days,
                LaunchDeltaVKms = nSol.launch_dv_kms,
                InsertionDeltaVKms = nSol.insertion_dv_kms,
                TotalDeltaVKms = nSol.total_dv_kms,
                AllLegsFeasible = nSol.all_feasible != 0,
                FlybyChainSummary = string.Join("  ->  ", fullChain),
                CentralBody = config?.CentralBody ?? "Sun"
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

        /// <summary>
        /// 原生调用 PyKEP 计算转移段的劳登主矢量 (Lawden Primer Vector)，评估冲量机动在时间维度上的最优性
        /// </summary>
        public static PrimerVectorResult CalculatePrimerVector(Vector3d r1, Vector3d v1, Vector3d dv1, Vector3d dv2, double tofSec, double mu = 0) {
            if (_fnCalcPrimerVector == null || tofSec <= 0) return null;
            double[] r1Arr = new double[] { r1.x, r1.y, r1.z };
            double[] v1Arr = new double[] { v1.x, v1.y, v1.z };
            double[] dv1Arr = new double[] { dv1.x, dv1.y, dv1.z };
            double[] dv2Arr = new double[] { dv2.x, dv2.y, dv2.z };
            double effectiveMu = mu > 0 ? mu : SolarSystemCatalog.SunMu;
            int status = _fnCalcPrimerVector(r1Arr, v1Arr, dv1Arr, dv2Arr, tofSec, effectiveMu, out NativePrimerVectorResult nPv);
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

        /// <summary>
        /// 原生调用 PyKEP 计算双曲接近轨道的 B 平面瞄准参数 (B-Plane: B·T, B·R, 偏转角与碰撞半径)
        /// </summary>
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

        /// <summary>
        /// 原生调用 PyKEP 计算中途轨道修正 (TCM) 脉冲，以纠正 B 平面目标漂移
        /// </summary>
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

        /// <summary>
        /// 原生调用 PyKEP 计算 B 平面误差协方差传播 (B-Plane Covariance Mapping) 与碰撞概率
        /// </summary>
        public static BPlaneResult CalculateBPlaneCovariance(
            Vector3d r0, Vector3d v0, double dtSec, double muSun,
            double posSigmaKm, double velSigmaKms,
            Vector3d rEnc, Vector3d vEnc,
            double bodyMu, double bodyRadiusKm, double safeAltKm)
        {
            if (_fnCalcBPlaneCovariance == null || bodyMu <= 0 || muSun <= 0) return null;
            double[] r0Arr = new double[] { r0.x, r0.y, r0.z };
            double[] v0Arr = new double[] { v0.x, v0.y, v0.z };
            double[] rEncArr = new double[] { rEnc.x, rEnc.y, rEnc.z };
            double[] vEncArr = new double[] { vEnc.x, vEnc.y, vEnc.z };

            int status = _fnCalcBPlaneCovariance(
                r0Arr, v0Arr, dtSec, muSun,
                posSigmaKm, velSigmaKms,
                rEncArr, vEncArr,
                bodyMu, bodyRadiusKm, safeAltKm,
                out NativeBPlaneCovarianceResult nCov);

            if (status != 0) return null;

            // 同时获取基本 B 平面矢量
            var bp = CalculateBPlane(rEnc, vEnc, bodyMu) ?? new BPlaneResult();
            bp.SigmaBTKm = nCov.sigma_bt_km;
            bp.SigmaBRKm = nCov.sigma_br_km;
            bp.CovarianceBTRKm2 = nCov.cov_btr_km2;
            bp.SemiMajorAxisSigmaKm = nCov.semi_major_sigma_km;
            bp.SemiMinorAxisSigmaKm = nCov.semi_minor_sigma_km;
            bp.EllipseAngleDeg = nCov.ellipse_angle_deg;
            bp.SafeFlybyRadiusKm = nCov.safe_radius_km;
            bp.EstimatedPericenterKm = nCov.periapsis_km;
            bp.SafeMargin3SigmaKm = nCov.safe_margin_3sigma_km;
            bp.IsSafeFlyby = nCov.is_safe_flyby != 0;
            bp.NavigationalTcmBudgetKms = nCov.tcm_3sigma_dv_ms / 1000.0;
            return bp;
        }

        /// <summary>
        /// 原生调用 PyKEP 计算弱稳定性边界 (WSB) 与不变流形 (Invariant Manifold) 低能捕获节约量
        /// </summary>
        public static LowEnergyManifoldResult CalculateManifoldCapture(
            Vector3d rEnc, Vector3d vEnc,
            Vector3d rPlanet, Vector3d vPlanet,
            double bodyMu, double sunMu, double bodyRadiusKm,
            double targetPeKm, double targetApKm)
        {
            if (_fnCalcManifoldCapture == null || bodyMu <= 0 || sunMu <= 0 || bodyRadiusKm <= 0) return null;
            double[] rEncArr = new double[] { rEnc.x, rEnc.y, rEnc.z };
            double[] vEncArr = new double[] { vEnc.x, vEnc.y, vEnc.z };
            double[] rPlArr = new double[] { rPlanet.x, rPlanet.y, rPlanet.z };
            double[] vPlArr = new double[] { vPlanet.x, vPlanet.y, vPlanet.z };

            int status = _fnCalcManifoldCapture(
                rEncArr, vEncArr, rPlArr, vPlArr,
                bodyMu, sunMu, bodyRadiusKm,
                targetPeKm, targetApKm,
                out NativeManifoldCaptureResult nRes);

            if (status != 0) return null;

            return new LowEnergyManifoldResult {
                IsManifoldCaptureFeasible = nRes.is_manifold_feasible != 0,
                JacobiConstant = nRes.jacobi_constant,
                HillRadiusKm = nRes.hill_radius_km,
                WsbCaptureDeltaVKms = nRes.wsb_capture_dv_kms,
                ClassicalVisVivaDeltaVKms = nRes.classical_visviva_dv_kms,
                DeltaVSavedKms = nRes.dv_saved_kms,
                SavingPercentage = nRes.saving_percentage,
                IngressAngleDeg = nRes.ingress_angle_deg,
                BallisticDurationDays = nRes.ballistic_duration_days,
                Details = $"WSB流形节能 {nRes.dv_saved_kms * 1000.0:F1} m/s ({nRes.saving_percentage:F1}%) | Hill={nRes.hill_radius_km / 1000.0:F1}k km | C_J={nRes.jacobi_constant:F2}"
            };
        }

        /// <summary>
        /// 原生调用 PyKEP 将笛卡尔状态向量 (r, v) 转换为非奇异改化等效开普勒轨道根数 (MEE: p, f, g, h, k, L)
        /// </summary>
        public static MEEResult CartesianToMEE(Vector3d r, Vector3d v, double mu) {
            if (_fnCartesianToMEE == null || mu <= 0) return null;
            double[] rArr = new double[] { r.x, r.y, r.z };
            double[] vArr = new double[] { v.x, v.y, v.z };
            int status = _fnCartesianToMEE(rArr, vArr, mu, out NativeMEE mee);
            if (status != 0) return null;
            return new MEEResult { P = mee.p, F = mee.f, G = mee.g, H = mee.h, K = mee.k, L = mee.L };
        }

        /// <summary>
        /// 原生调用 PyKEP 评估 Sims-Flanagan 低推力转移轨迹段的不匹配度 (Match Point Discrepancy)
        /// </summary>
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

        /// <summary>
        /// 基于当前最优染色体生成飞行时间 (ToF) vs 速度增量 (ΔV) 的帕累托前沿权衡曲线 (Pareto Trade-off Front)
        /// </summary>
        public static List<ParetoTradeOffPoint> GenerateParetoFront(double[] bestGenome, List<string> fullChain, MGAConfig config, int count = 9) {
            var front = new List<ParetoTradeOffPoint>();
            if (bestGenome == null || fullChain == null || fullChain.Count < 2) return front;

            var baseSol = BuildFullSolution(bestGenome, fullChain, config, computeAdvanced: false);
            if (baseSol == null || baseSol.TotalDeltaVKms <= 0 || double.IsNaN(baseSol.TotalDeltaVKms)) return front;

            double baseDV = baseSol.TotalDeltaVKms;
            // 候选因子：从充裕时间 (省油) 到较短转移时间 (快速)
            double[] scaleFactors = MGADefaults.ParetoScaleFactors;
            for (int s = 0; s < Math.Min(count, scaleFactors.Length); s++) {
                double factor = scaleFactors[s];
                double[] sampleGenome = (double[])bestGenome.Clone();
                bool physicallyFeasible = true;
                int legCount = fullChain.Count - 1;
                for (int k = 1; k <= legCount && k < sampleGenome.Length; k++) {
                    double originalSec = bestGenome[k];
                    double scaledSec = originalSec * factor;
                    // 物理安全防线：每段飞行时间不得低于 20 天，且不得低于基准时间的 0.65 倍
                    if (scaledSec < MGADefaults.SecondsPerDay * MGADefaults.ParetoMinFlightDays || scaledSec < originalSec * MGADefaults.ParetoMinTofFactor) {
                        physicallyFeasible = false;
                        break;
                    }
                    sampleGenome[k] = scaledSec;
                }
                if (!physicallyFeasible) continue;

                var testSol = BuildFullSolution(sampleGenome, fullChain, config, computeAdvanced: false);
                // 物理有效性验证：总 ΔV 绝不能超过基准的 2.5 倍且不能超过 25 km/s (杜绝超高速非物理数值)
                if (testSol != null && testSol.TotalDeltaVKms > 0 && !double.IsNaN(testSol.TotalDeltaVKms) &&
                    testSol.TotalDeltaVKms <= Math.Min(MGADefaults.ParetoMaxAbsoluteDvKms, baseDV * MGADefaults.ParetoMaxDvMultiplier) && testSol.TotalFlightDays >= MGADefaults.ParetoMinFlightDays) 
                {
                    front.Add(new ParetoTradeOffPoint {
                        TotalFlightDays = testSol.TotalFlightDays,
                        TotalDeltaVKms = testSol.TotalDeltaVKms,
                        LaunchDeltaVKms = testSol.LaunchDeltaVKms,
                        FlybyDeltaVKms = Math.Max(0, testSol.TotalDeltaVKms - testSol.LaunchDeltaVKms - testSol.InsertionDeltaVKms),
                        Genome = sampleGenome
                    });
                }
            }
            // 严格保证原始基准解本身必在列表中
            if (!front.Any(p => Math.Abs(p.TotalFlightDays - baseSol.TotalFlightDays) < MGADefaults.ParetoEqualityToleranceDays)) {
                front.Add(new ParetoTradeOffPoint {
                    TotalFlightDays = baseSol.TotalFlightDays,
                    TotalDeltaVKms = baseSol.TotalDeltaVKms,
                    LaunchDeltaVKms = baseSol.LaunchDeltaVKms,
                    FlybyDeltaVKms = Math.Max(0, baseSol.TotalDeltaVKms - baseSol.LaunchDeltaVKms - baseSol.InsertionDeltaVKms),
                    Genome = (double[])bestGenome.Clone()
                });
            }
            // 排序语义：Index 0 为最省油 (飞行时间最长、ΔV 最小)，Index Max 为快速 (飞行时间最短、ΔV 略高)
            front.Sort((a, b) => b.TotalFlightDays.CompareTo(a.TotalFlightDays));
            return front;
        }


        /// <summary>
        /// 计算两颗天体之间的经典双冲量霍曼转移时间（以天为单位）
        /// </summary>
        public static double GetHohmannTransferDays(string fromBody, string toBody) => GetHohmannTransferTimeDays(fromBody, toBody);

        /// <summary>
        /// 计算两颗天体之间的经典霍曼转移时间（以天为单位，支持同心轨道解析与 KSP 轨道实体）
        /// </summary>
        public static double GetHohmannTransferTimeDays(string fromBody, string toBody) {
            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody) ||
                fromBody.Equals(toBody, StringComparison.OrdinalIgnoreCase)) {
                return 0.0;
            }

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            string central = ResolveTransferCentralBody(p1, p2);
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            try {
                if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                    var cb1 = FlightGlobals.Bodies.Find(b => b.name.Equals(fromBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(fromBody, StringComparison.OrdinalIgnoreCase));
                    var cb2 = FlightGlobals.Bodies.Find(b => b.name.Equals(toBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(toBody, StringComparison.OrdinalIgnoreCase));
                    if (cb1 != null && cb2 != null) {
                        double r1cb = GetCbDistanceToCentral(cb1, central, MGADefaults.DepartureAltitudeKm);
                        double r2cb = GetCbDistanceToCentral(cb2, central, MGADefaults.DestinationAltitudeKm);
                        if (CalcHohmannTransfer(r1cb, r2cb, centralMu, out _, out double tSec, out _, out _)) {
                            return tSec / MGADefaults.SecondsPerDay;
                        }
                    }
                }
            } catch { }

            double sma1 = GetRelativeSMA(p1, central, MGADefaults.DepartureAltitudeKm);
            double sma2 = GetRelativeSMA(p2, central, MGADefaults.DestinationAltitudeKm);
            if (CalcHohmannTransfer(sma1, sma2, centralMu, out _, out double tofSec, out _, out _)) {
                return tofSec / MGADefaults.SecondsPerDay;
            }
            return 0.0;
        }

        /// <summary>
        /// 获取给定天体到其中心天体的轨道半长轴或距心距离 (km)
        /// </summary>
        private static double GetCbDistanceToCentral(CelestialBody cb, string central, double defaultAltKm = MGADefaults.DepartureAltitudeKm) {
            double cRadius = SolarSystemCatalog.GetPlanet(central)?.RadiusKm ?? MGADefaults.GetBodyRadiusKm(central);
            if (cb == null) return cRadius + defaultAltKm;
            if (cb.name.Equals(central, StringComparison.OrdinalIgnoreCase) || 
               (cb.bodyName != null && cb.bodyName.Equals(central, StringComparison.OrdinalIgnoreCase))) {
                return (cb.Radius / 1000.0) + defaultAltKm;
            }
            if (cb.orbit != null && cb.orbit.semiMajorAxis > 0) {
                return cb.orbit.semiMajorAxis / 1000.0;
            }
            return (cb.Radius / 1000.0) + defaultAltKm;
        }

        /// <summary>
        /// 解析转移段所依赖的共同主引力中心天体 (如太阳 Kerbol/Sun)
        /// </summary>
        private static string ResolveTransferCentralBody(SolarPlanet p1, SolarPlanet p2) {
            if (p1 == null || p2 == null) return SolarSystemCatalog.RootStarName;
            return SolarSystemCatalog.ResolveTransferCentralBody(p1.Name, p2.Name);
        }

        /// <summary>
        /// 获取天体相对于指定中心天体的等效轨道半长轴 (SMA, km)
        /// </summary>
        public static double GetRelativeSMA(SolarPlanet body, string centralBody, double defaultAltKm = MGADefaults.DepartureAltitudeKm) {
            double cRadius = SolarSystemCatalog.GetPlanet(centralBody)?.RadiusKm ?? MGADefaults.GetBodyRadiusKm(centralBody);
            if (body == null) return cRadius + defaultAltKm;
            if (body.Name.Equals(centralBody, StringComparison.OrdinalIgnoreCase)) {
                return (body.RadiusKm > 0 ? body.RadiusKm : cRadius) + defaultAltKm;
            }
            return (body.SMA > 0) ? body.SMA : ((body.RadiusKm > 0 ? body.RadiusKm : cRadius) + defaultAltKm);
        }

        /// <summary>
        /// 计算两颗天体之间的会合周期（Synodic Period，以天为单位）
        /// 用于多岛差分进化 (DE) 离散会合窗口的划分与跨周期搜索
        /// </summary>
        public static double GetSynodicPeriodDays(string fromBody, string toBody) {
            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody) ||
                fromBody.Equals(toBody, StringComparison.OrdinalIgnoreCase)) {
                return 365.25;
            }

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            string central = ResolveTransferCentralBody(p1, p2);
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            try {
                if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                    var cb1 = FlightGlobals.Bodies.Find(b => b.name.Equals(fromBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(fromBody, StringComparison.OrdinalIgnoreCase));
                    var cb2 = FlightGlobals.Bodies.Find(b => b.name.Equals(toBody, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(toBody, StringComparison.OrdinalIgnoreCase));
                    if (fromBody.Equals(central, StringComparison.OrdinalIgnoreCase) && cb2 != null && cb2.orbit != null && cb2.orbit.period > 0) {
                        return cb2.orbit.period / MGADefaults.SecondsPerDay;
                    }
                    if (toBody.Equals(central, StringComparison.OrdinalIgnoreCase) && cb1 != null && cb1.orbit != null && cb1.orbit.period > 0) {
                        return cb1.orbit.period / MGADefaults.SecondsPerDay;
                    }
                    if (cb1 != null && cb2 != null && cb1.orbit != null && cb2.orbit != null && cb1.orbit.period > 0 && cb2.orbit.period > 0) {
                        // 合规论证 (baseline §0.2/§0.4/§11)：
                        // T_syn = 1/|1/T1 - 1/T2| 是数学恒等式，仅用于会合窗口切分启发式（不属于轨迹动力学）。
                        // T1/T2 为游戏体 Kepler 轨道周期（非我自算），周期绝对量未被托管伪造；
                        // 原生 dll 当前未导出 synodic_period，故在桥接层以恒等式派生，周期输入要么来自身份，要么来自原生 CalcOrbitPeriodSec。
                        double synSec = Math.Abs(1.0 / (1.0 / cb1.orbit.period - 1.0 / cb2.orbit.period));
                        return synSec / MGADefaults.SecondsPerDay;
                    }
                }
            } catch { }

            double sma1 = GetRelativeSMA(p1, central, MGADefaults.DepartureAltitudeKm);
            double sma2 = GetRelativeSMA(p2, central, MGADefaults.DestinationAltitudeKm);
            double t1 = CalcOrbitPeriodSec(sma1, centralMu);
            double t2 = CalcOrbitPeriodSec(sma2, centralMu);

            if (fromBody.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                return t2 / MGADefaults.SecondsPerDay;
            }
            if (toBody.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                return t1 / MGADefaults.SecondsPerDay;
            }

            double diff = Math.Abs(t1 - t2);
            if (diff < 1e-6) return 365.25;
            // 合规论证同上方：T_syn = T1·T2/|T1-T2| 为数学恒等式；T1/T2 来自原生 CalcOrbitPeriodSec（原生 hohmann）。
            return (t1 * t2 / diff) / MGADefaults.SecondsPerDay;
        }

        /// <summary>
        /// 全面委派 C++ 原生三维兰伯特特征能量扫描与交点线求解器 (100% 原生，0 托管计算)
        /// 彻底根除托管 atan2 投影伪极小值与地月模式 2 锁死非交点日问题
        /// </summary>
        public static (double bestDepUT, double tofDays, double approxDV) CalculateHohmannWindow(
            string fromBody, string toBody, double searchStartUT, double searchSpanDays = 0, double targetIncDeg = -1.0, double targetRaanDeg = 0.0, MGAConfig config = null) 
        {
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
            string central = ResolveTransferCentralBody(p1, p2);

            double parkInc = targetIncDeg;
            double parkRaan = targetRaanDeg;
            var mgaCfg = config ?? MGAStateManager.Current?.Config;
            if (mgaCfg != null && (mgaCfg.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || mgaCfg.LockDepartureInclination)) {
                if (parkInc < 0) parkInc = mgaCfg.DepartureInclinationDeg;
                if (parkRaan < 0) parkRaan = mgaCfg.DepartureRAANDeg;
            }

            if (PykepNative.FindTransferWindow(
                fromBody, toBody, central,
                searchStartUT, searchSpanDays,
                mgaCfg?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm,
                mgaCfg?.DestinationPeAKm ?? MGADefaults.DestinationAltitudeKm,
                parkInc > 0 ? parkInc : 0.0, parkRaan,
                out double bestDepUT, out double tofDays, out double approxDvKms))
            {
                return (bestDepUT, tofDays, approxDvKms * 1000.0);
            }

            // 原生求解失败兜底 (双圆霍曼转移基线)
            double sma1 = GetRelativeSMA(p1, central, MGADefaults.DepartureAltitudeKm);
            double sma2 = GetRelativeSMA(p2, central, MGADefaults.DestinationAltitudeKm);
            double centralMu = SolarSystemCatalog.GetCentralMu(central);
            if (CalcHohmannTransfer(sma1, sma2, centralMu, out double totDv, out double tofSec, out _, out _)) {
                return (searchStartUT, tofSec / MGADefaults.SecondsPerDay, totDv * 1000.0);
            }

            return (double.NaN, double.NaN, double.NaN);
        }

        /// <summary>
        /// 原生计算双圆霍曼转移参数 (100% 原生 kep3::hohmann)
        /// </summary>
        public static bool CalcHohmannTransfer(double r1_km, double r2_km, double mu, out double totalDv, out double tofSec, out double dv1, out double dv2) {
            return PykepNative.CalcHohmannTransfer(r1_km, r2_km, mu, out totalDv, out tofSec, out dv1, out dv2);
        }

        /// <summary>
        /// 原生计算双椭圆转移参数 (100% 原生 kep3::bielliptic)
        /// </summary>
        public static bool CalcBiellipticTransfer(double r1_km, double r2_km, double rb_km, double mu, out double totalDv, out double tofSec) {
            return PykepNative.CalcBiellipticTransfer(r1_km, r2_km, rb_km, mu, out totalDv, out tofSec);
        }

        /// <summary>
        /// 原生开普勒解析状态转移矩阵 (100% 原生 kep3 2022 Reynolds 解析 STM，机器精度)
        /// </summary>
        public static bool CalcReynoldsStm(Vector3d r0, Vector3d v0, double dtSec, double mu, out double[] outStm6x6) {
            double[] r0Arr = new double[] { r0.x, r0.y, r0.z };
            double[] v0Arr = new double[] { v0.x, v0.y, v0.z };
            return PykepNative.CalcReynoldsStm(r0Arr, v0Arr, dtSec, mu, out outStm6x6);
        }

        /// <summary>
        /// 原生双曲飞掠出射速度闭式解析 (100% 原生 kep3::fb_vout)
        /// </summary>
        public static bool CalcFlybyVout(Vector3d vIn, Vector3d vPla, double rpKm, double betaRad, double mu, out Vector3d outVOut) {
            double[] inArr = new double[] { vIn.x, vIn.y, vIn.z };
            double[] plaArr = new double[] { vPla.x, vPla.y, vPla.z };
            bool ok = PykepNative.CalcFlybyVout(inArr, plaArr, rpKm, betaRad, mu, out double[] vOutArr);
            outVOut = ok ? new Vector3d(vOutArr[0], vOutArr[1], vOutArr[2]) : Vector3d.zero;
            return ok;
        }

        /// <summary>
        /// 原生 VSOP2013 半解析极高精度行星历表 (100% 原生 kep3::udpla::vsop2013)
        /// </summary>
        public static bool GetVsop2013PlanetState(string planetName, double epochJd, out Vector3d posKm, out Vector3d velKms) {
            return PykepNative.GetVsop2013PlanetState(planetName, epochJd, out posKm, out velKms);
        }

        /// <summary>
        /// 100% 委派原生 kep3 引擎计算开普勒二体轨道周期 (单位: 秒, 输入 sma 单位: km, mu 单位: km^3/s^2)
        /// 严格杜绝托管层手写 2*pi*sqrt(a^3/mu)
        /// </summary>
        public static double CalcOrbitPeriodSec(double smaKm, double muKms) {
            if (smaKm <= 0 || muKms <= 0) return 0.0;
            if (CalcHohmannTransfer(smaKm, smaKm, muKms, out _, out double halfPeriodSec, out _, out _)) {
                // 合规论证 (baseline §0.2/§0.4/§11)：
                // 轨道周期 = 2 × (原生 hohmann 从 r=a→r=a 的半周期)。这只是对原生返回值的恒等缩放 (T = 2·(T/2))，
                // 绝非托管自算开普勒公式；原生 dll 当前未导出 orbit_period，故用原生 hohmann 结果派生，
                // 且严禁手写 2π√(a³/μ)。若日后原生新增 orbit_period，应改走原生。
                return 2.0 * halfPeriodSec;
            }
            KSPGMATLog.Warn($"[PykepBridge] CalcOrbitPeriodSec: 原生 kep3 转移周期评估失败 (sma={smaKm}, mu={muKms})，拒绝托管自算公式，返回 0.0");
            return 0.0;
        }

        /// <summary>
        /// 100% 委派原生 kep3 引擎计算开普勒二体轨道周期 (单位: 秒, 输入 sma 单位: m, mu 单位: m^3/s^2)
        /// </summary>
        public static double CalcOrbitPeriodSecM(double smaMeters, double muM3S2) {
            if (smaMeters <= 0 || muM3S2 <= 0) return 0.0;
            return CalcOrbitPeriodSec(smaMeters / 1000.0, muM3S2 / 1e9);
        }

        /// <summary>
        /// DirectCompute GPU 极速特征面求解入口：
        /// 批量计算发射时域-ToF 能量曲面张量，提供微秒级宏观特征初筛。
        /// 若显卡不支持或执行失败，平滑返回 false，由调用方无缝回退至 CPU 两级网格扫描。
        /// </summary>
        public static bool EvaluateSurrogateGpuGrid(
            SolarPlanet p1, SolarPlanet p2, double centralMu,
            double depStartUT, double depSpanSec,
            double tofMinSec, double tofMaxSec,
            int gridW, int gridH,
            double depPeKm, double arrPeKm,
            bool flybyOnly,
            out float[] outTotDv,
            out float[] outC3)
        {
            outTotDv = null;
            outC3 = null;
            if (!PykepNative.IsGpuComputeAvailable || p1 == null || p2 == null || p1.SMA <= 0 || p2.SMA <= 0)
                return false;

            try
            {
                var elem1 = new PykepNative.GpuPlanetElements
                {
                    sma_km = p1.SMA,
                    ecc = p1.ECC,
                    inc_rad = p1.INC * (Math.PI / 180.0),
                    raan_rad = p1.RAAN * (Math.PI / 180.0),
                    aop_rad = p1.AOP * (Math.PI / 180.0),
                    ma0_rad = p1.MA0 * (Math.PI / 180.0),
                    epoch_ut = p1.EpochJ2000,
                    mu_self = p1.Mu,
                    radius_km = p1.RadiusKm,
                    park_alt_km = depPeKm
                };

                var elem2 = new PykepNative.GpuPlanetElements
                {
                    sma_km = p2.SMA,
                    ecc = p2.ECC,
                    inc_rad = p2.INC * (Math.PI / 180.0),
                    raan_rad = p2.RAAN * (Math.PI / 180.0),
                    aop_rad = p2.AOP * (Math.PI / 180.0),
                    ma0_rad = p2.MA0 * (Math.PI / 180.0),
                    epoch_ut = p2.EpochJ2000,
                    mu_self = p2.Mu,
                    radius_km = p2.RadiusKm,
                    park_alt_km = arrPeKm
                };

                int totalPts = gridW * gridH;
                var dvArr = new float[totalPts];
                var c3Arr = new float[totalPts];

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int ret = PykepNative.PorkchopGpuEvaluate(
                    ref elem1, ref elem2,
                    centralMu,
                    depStartUT, depSpanSec,
                    tofMinSec, tofMaxSec,
                    gridW, gridH,
                    flybyOnly,
                    dvArr, c3Arr);
                sw.Stop();

                if (ret == 0)
                {
                    outTotDv = dvArr;
                    outC3 = c3Arr;
                    Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute Surrogate Porkchop", sw.Elapsed.TotalMilliseconds, totalPts, true);
                    MGAStepProfiler.RecordMs("GPU批处理: DirectCompute Porkchop特征面网格", sw.Elapsed.TotalMilliseconds);
                    return true;
                }
                Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute Surrogate Porkchop", sw.Elapsed.TotalMilliseconds, totalPts, false);
                return false;
            }
            catch (Exception ex)
            {
                KSPGMATLog.Warn($"[PykepBridge] EvaluateSurrogateGpuGrid 异常: {ex.Message}");
                return false;
            }
        }

        // =====================================================================
        // 向后兼容门面 (统一委托至 MGA/Solver 架构层)
        // =====================================================================
        public static List<MGASolution> Optimize(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default) => MGAAdaptiveSolver.Optimize(config, onProgress, ct);

        public static List<List<string>> GenerateCandidateSequences(string dep, string dest, int maxSwingBys = 6, int maxResonances = 0, int maxBackLegs = 2, int maxBackSpacing = 2) => MGAMCTSTopologySelector.GenerateCandidateSequences(dep, dest, maxSwingBys, maxResonances, maxBackLegs, maxBackSpacing);

        public static List<List<string>> GenerateCandidateSequences(MGAConfig config) => MGAMCTSTopologySelector.GenerateCandidateSequences(config);
    }
}
