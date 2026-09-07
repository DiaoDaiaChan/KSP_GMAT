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

        private static PykepMGAEvaluateDelegate _fnMGAEvaluate;
        private static PykepMGAEvaluateMuDelegate _fnMGAEvaluateMu;
        private static PykepMGAHybridEvaluateDelegate _fnMGAHybridEvaluate;
        private static PykepMGAHybridEvaluateMuDelegate _fnMGAHybridEvaluateMu;
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

                if (fromName.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                    double rPark = (p1 != null ? p1.RadiusKm : MGADefaults.EarthRadiusKm) + (config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                    Vector3d normal;
                    if (config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination)) {
                        double iRad = config.DepartureInclinationDeg * Mathf.Deg2Rad;
                        double oRad = config.DepartureRAANDeg * Mathf.Deg2Rad;
                        normal = new Vector3d(Math.Sin(iRad) * Math.Sin(oRad), -Math.Sin(iRad) * Math.Cos(oRad), Math.Cos(iRad));
                        if (normal.sqrMagnitude < 0.01) normal = new Vector3d(0, 0, 1);
                    } else {
                        // 模式 1 (发射前规划): 自由寻找最优发射面，对准目标天体公转面 (h = r2 x v2)，消除面外变轨
                        Vector3d hTarget = Vector3d.Cross(r2, v2);
                        if (hTarget.sqrMagnitude > 1e-4) {
                            normal = hTarget.normalized;
                        } else {
                            Vector3d dirArr = r2.normalized;
                            if (dirArr.sqrMagnitude < 0.01) dirArr = new Vector3d(1, 0, 0);
                            normal = Vector3d.Cross(new Vector3d(0, 0, 1), dirArr).normalized;
                            if (normal.sqrMagnitude < 0.01) normal = new Vector3d(0, 0, 1);
                        }
                    }

                    // 严格将 r2 投影到停泊轨道平面上，消除 radialDir 的面外分量，确保 r1 与 v1 严格位于锁定停泊轨道面内
                    Vector3d r2Proj = r2 - Vector3d.Dot(r2, normal) * normal;
                    if (r2Proj.sqrMagnitude < 1e-6) {
                        Vector3d arbitrary = Math.Abs(normal.z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
                        r2Proj = Vector3d.Cross(normal, arbitrary);
                    }
                    Vector3d radialDir = r2Proj.normalized;
                    Vector3d planeDir = Vector3d.Cross(normal, radialDir).normalized;

                    // 消除两点边界值 Lambert 求解器在 180° 反共线平面的退化奇异性 (indeterminate orbital plane)
                    // 仅施加微小的自适应规避扰动角 (~0.5°)，避免大角度偏移 (5°) 破坏理想霍曼共面精度
                    double angOffset = 0.5 * Math.PI / 180.0;
                    r1 = (-radialDir * Math.Cos(angOffset) + planeDir * Math.Sin(angOffset)) * rPark;
                    v1 = (planeDir * Math.Cos(angOffset) + radialDir * Math.Sin(angOffset)) * Math.Sqrt(centralMu / rPark);
                }
                if (toName.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                    double rPark = (p2 != null ? p2.RadiusKm : MGADefaults.EarthRadiusKm) + (config?.DestinationPeAKm ?? MGADefaults.DestinationAltitudeKm);
                    Vector3d dirDep = r1.normalized;
                    if (dirDep.sqrMagnitude < 0.01) dirDep = new Vector3d(1, 0, 0);
                    Vector3d normal = Vector3d.Cross(dirDep, new Vector3d(0, 0, 1)).normalized;
                    if (normal.sqrMagnitude < 0.01) normal = new Vector3d(0, 1, 0);
                    double angOffset = 0.5 * Math.PI / 180.0;
                    r2 = (-dirDep * Math.Cos(angOffset) + normal * Math.Sin(angOffset)) * rPark;
                    v2 = (normal * Math.Cos(angOffset) + dirDep * Math.Sin(angOffset)) * Math.Sqrt(centralMu / rPark);
                }

                depPlanets[k] = new NativePlanetState {
                    rx = r1.x, ry = r1.y, rz = r1.z,
                    vx = v1.x, vy = v1.y, vz = v1.z,
                    mu = p1 != null ? p1.Mu : centralMu,
                    radius = p1 != null ? p1.RadiusKm : MGADefaults.EarthRadiusKm,
                    safe_radius = p1 != null ? p1.SafeRadiusKm : MGADefaults.EarthSafeRadiusKm,
                    soi_radius = fromName.Equals(central, StringComparison.OrdinalIgnoreCase) ? 1e18 : (p1 != null && p1.SoiKm > 0 ? p1.SoiKm : 1e18),
                    atm_radius = p1 != null ? (p1.RadiusKm + p1.AtmosphereDepthKm) : 0.0
                };

                arrPlanets[k] = new NativePlanetState {
                    rx = r2.x, ry = r2.y, rz = r2.z,
                    vx = v2.x, vy = v2.y, vz = v2.z,
                    mu = p2 != null ? p2.Mu : centralMu,
                    radius = p2 != null ? p2.RadiusKm : MGADefaults.EarthRadiusKm,
                    safe_radius = p2 != null ? p2.SafeRadiusKm : MGADefaults.EarthSafeRadiusKm,
                    soi_radius = toName.Equals(central, StringComparison.OrdinalIgnoreCase) ? 1e18 : (p2 != null && p2.SoiKm > 0 ? p2.SoiKm : 1e18),
                    atm_radius = p2 != null ? (p2.RadiusKm + p2.AtmosphereDepthKm) : 0.0
                };

                currentUT = arrUT;
            }

            if (config != null) config.SyncStrategiesWithChain(legCount);

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
                if (_fnMGAHybridEvaluateMu != null) {
                    status = _fnMGAHybridEvaluateMu(
                        depPlanets, arrPlanets, tofs,
                        legStrats, fbStrats,
                        dsmEtas, fbRps, fbBetas,
                        legCount,
                        config != null ? config.DeparturePeAKm : 200.0, config != null ? config.DepartureApAKm : 200.0,
                        config != null ? config.DepartureInclinationDeg : 0.0,
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
                        config != null ? config.DepartureInclinationDeg : 0.0,
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
            if (_fnMGAEvaluateMu != null || _fnMGAEvaluate != null) {
                int status = -1;
                NativeMGASolution nativeSol;
                if (_fnMGAEvaluateMu != null) {
                    status = _fnMGAEvaluateMu(
                        depPlanets, arrPlanets, tofs, legCount,
                        config.DeparturePeAKm, config.DepartureApAKm,
                        config.DepartureInclinationDeg,
                        config.DestinationPeAKm, config.DestinationApAKm,
                        config.NoInsertionBurn ? 1 : 0,
                        centralMu,
                        out nativeSol);
                } else {
                    status = _fnMGAEvaluate(
                        depPlanets, arrPlanets, tofs, legCount,
                        config.DeparturePeAKm, config.DepartureApAKm,
                        config.DepartureInclinationDeg,
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

        private static double ComputeFitness(MGASolution sol, MGAConfig config) {
            if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) return 1e9;
            double fit = sol.TotalDeltaVKms + (sol.AllLegsFeasible ? 0.0 : MGADefaults.PenaltyInfeasibleBase);
            if (config != null) {
                // 发射窗口时间约束硬罚函数：严格惩罚超出用户指定最早/最晚发射时间的候选解
                if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) {
                    double violationDays = (config.EarliestDepartureUT - sol.DepartureUT) / MGADefaults.SecondsPerDay;
                    fit += violationDays * MGADefaults.PenaltyDepartureViolationDayWeight + MGADefaults.PenaltyDepartureViolationBase;
                }
                if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) {
                    double violationDays = (sol.DepartureUT - config.LatestDepartureUT) / MGADefaults.SecondsPerDay;
                    fit += violationDays * MGADefaults.PenaltyDepartureViolationDayWeight + MGADefaults.PenaltyDepartureViolationBase;
                }
                if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) {
                    double totalMs = sol.TotalDeltaVKms * 1000.0;
                    if (totalMs > config.VesselMaxDeltaVMs) {
                        double overKms = (totalMs - config.VesselMaxDeltaVMs) / 1000.0;
                        fit += overKms * MGADefaults.PenaltyVesselDeltaVWeight + MGADefaults.PenaltyVesselDeltaVBase; // 显著罚函数惩罚超出飞船当前可用 ΔV 预算的解
                    }
                }
                if (config.LockDepartureInclination && sol.Legs != null && sol.Legs.Count > 0) {
                    double actualInc = sol.Legs[0].EjectionInclinationDeg;
                    double targetInc = config.DepartureInclinationDeg;
                    double incErr = Math.Abs(actualInc - targetInc);
                    if (incErr > MGADefaults.PenaltyIncToleranceDeg) {
                        // 逃逸倾角锁定约束罚函数：若逃逸倾角偏离锁定值，按每度 25 km/s + 50 km/s 罚项强力约束
                        fit += incErr * MGADefaults.PenaltyIncDegWeight + MGADefaults.PenaltyIncBase;
                    }
                }
                if ((config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination) && sol.Legs != null && sol.Legs.Count > 0) {
                    var leg0 = sol.Legs[0];
                    if (PykepNative.CalcCoplanarAngle(config.DepartureInclinationDeg, config.DepartureRAANDeg, leg0.ArrivalPosKm, out double wedgeDeg)) {
                        if (wedgeDeg > 0.5) {
                            // 严格按真实轨道平面变换动力学惩罚面外倾角 (每度 ~190 m/s 物理变轨开销)
                            double rad = (wedgeDeg - 0.5) * Mathf.Deg2Rad;
                            double vTrans = 10.8; // km/s 标称转移初速
                            double penaltyDv = 2.0 * vTrans * Math.Sin(rad * 0.5);
                            fit += Math.Max((wedgeDeg - 0.5) * 0.2, penaltyDv);
                        }
                    }
                }
            }
            return fit;
        }

        /// <summary>
        /// 全局多段差分进化寻优器 (基于 PyKEP 原生全流程评估)
        /// </summary>
        /// <summary>
        /// 全局多段差分进化寻优器 (基于 PyKEP 原生全流程评估，自动按天体公转/会合周期分窗并发寻优，杜绝单月早熟坍缩)
        /// </summary>
        public static List<MGASolution> Optimize(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default) {
            var solutions = new List<MGASolution>();
            if (config == null || config.FlybySequence == null || config.FlybySequence.Count == 0) return solutions;

            double startUT = config.EarliestDepartureUT;
            double endUT = (config.LatestDepartureUT > config.EarliestDepartureUT + 1.0)
                ? config.LatestDepartureUT
                : (config.EarliestDepartureUT + MGADefaults.SecondsPerDay * (config.DepartureSearchDays > 0 ? config.DepartureSearchDays : MGADefaults.DefaultSearchWindowDays));

            double totalSpanDays = (endUT - startUT) / MGADefaults.SecondsPerDay;
            string firstTarget = config.FlybySequence[0];
            double synodicDays = GetSynodicPeriodDays(config.DepartureBody, firstTarget);
            if (synodicDays <= 0 || double.IsNaN(synodicDays)) synodicDays = 27.3;

            // 若发射时限跨越 1.25 个以上会合周期 (如地月 90 天，或行星际 100 天)，自动启用会合周期多窗口并发多岛分段搜索
            if (totalSpanDays > synodicDays * 1.25 && totalSpanDays > 30.0) {
                double sliceDays = Math.Max(25.0, Math.Min(45.0, synodicDays));
                double overlapDays = 3.0;
                int sliceCount = Math.Min(6, (int)Math.Ceiling(totalSpanDays / sliceDays));

                var allSubSolutions = new System.Collections.Concurrent.ConcurrentBag<MGASolution>();
                int finishedSlices = 0;
                var lockObj = new object();

                Parallel.For(0, sliceCount, sIdx => {
                    if (ct.IsCancellationRequested) return;

                    double subStartUT = startUT + sIdx * (sliceDays - overlapDays) * MGADefaults.SecondsPerDay;
                    double subEndUT = Math.Min(endUT, subStartUT + sliceDays * MGADefaults.SecondsPerDay);
                    if (subStartUT >= endUT) return;

                    var subCfg = config.Clone();
                    subCfg.EarliestDepartureUT = subStartUT;
                    subCfg.LatestDepartureUT = subEndUT;
                    subCfg.PopulationSize = Math.Max(40, config.PopulationSize / 2);
                    subCfg.MaxGenerations = Math.Max(80, config.MaxGenerations / 2);

                    var subRes = OptimizeWindowInternal(subCfg, subStartUT, subEndUT, null, ct);
                    if (subRes != null) {
                        foreach (var s in subRes) allSubSolutions.Add(s);
                    }

                    lock (lockObj) {
                        finishedSlices++;
                        float p = (float)finishedSlices / sliceCount;
                        onProgress?.Invoke(p, $"PyKEP 多窗口多岛并发寻优: {finishedSlices} / {sliceCount} 周期窗口完成...");
                    }
                });

                var rawList = allSubSolutions.OrderBy(s => s.TotalDeltaVKms).ToList();
                var seenKeys = new HashSet<long>();
                foreach (var s in rawList) {
                    long key = ((long)(int)(s.DepartureUT / 43200.0) << 32) ^ (long)(uint)(int)(s.TotalFlightDays / 1.0);
                    if (!seenKeys.Contains(key)) {
                        seenKeys.Add(key);
                        solutions.Add(s);
                    }
                    if (solutions.Count >= 10) break;
                }

                if (solutions.Count < 5) {
                    foreach (var s in rawList) {
                        bool tooClose = solutions.Any(cur => 
                            Math.Abs(cur.DepartureUT - s.DepartureUT) < 21600.0 && 
                            Math.Abs(cur.TotalFlightDays - s.TotalFlightDays) < 0.5
                        );
                        if (!tooClose) {
                            solutions.Add(s);
                        }
                        if (solutions.Count >= 8) break;
                    }
                }

                solutions = solutions.OrderBy(s => s.TotalDeltaVKms).ToList();
                return solutions;
            }

            // 单时段标准优化
            return OptimizeWindowInternal(config, startUT, endUT, onProgress, ct);
        }

        /// <summary>
        /// 单时间窗口多核并发差分进化评估内核
        /// </summary>
        private static List<MGASolution> OptimizeWindowInternal(
            MGAConfig config, double winStartUT, double winEndUT, 
            Action<float, string> onProgress, CancellationToken ct) 
        {
            var solutions = new List<MGASolution>();
            if (config == null || config.FlybySequence == null || config.FlybySequence.Count == 0) return solutions;

            var fullChain = new List<string> { config.DepartureBody };
            fullChain.AddRange(config.FlybySequence);
            int legCount = fullChain.Count - 1;
            bool optDsmEta = (config != null) && (config.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM ||
                             (config.LegStrategies != null && config.LegStrategies.Any(s => s == MGALegStrategy.DeepSpaceManeuver1DSM)));
            int dim = 1 + legCount + (optDsmEta ? legCount : 0);

            double[] lowerBounds = new double[dim];
            double[] upperBounds = new double[dim];

            lowerBounds[0] = winStartUT;
            upperBounds[0] = winEndUT;

            string central = config?.CentralBody ?? "Sun";
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            for (int k = 0; k < legCount; k++) {
                string fromN = fullChain[k];
                string toN = fullChain[k + 1];
                var p1 = SolarSystemCatalog.GetPlanet(fromN);
                var p2 = SolarSystemCatalog.GetPlanet(toN);

                double sma1 = GetRelativeSMA(p1, central, config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                double sma2 = GetRelativeSMA(p2, central, config?.DestinationPeAKm ?? MGADefaults.DestinationAltitudeKm);

                double minD, maxD;
                if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                    double tOrbDays = CalcOrbitPeriodSec(sma1, centralMu) / MGADefaults.SecondsPerDay;
                    if (tOrbDays <= 0 || double.IsNaN(tOrbDays)) tOrbDays = 365.25;
                    minD = Math.Max(0.5, tOrbDays * MGADefaults.ResonanceMinToFFactor);
                    maxD = Math.Max(minD + 2.0, tOrbDays * MGADefaults.ResonanceMaxToFFactor);
                } else {
                    double approxHohmannDays = MGADefaults.HohmannDefaultToFDays;
                    if (CalcHohmannTransfer(sma1, sma2, centralMu, out _, out double hohmannSec, out _, out _)) {
                        approxHohmannDays = hohmannSec / MGADefaults.SecondsPerDay;
                    }
                    if (approxHohmannDays <= 0 || double.IsNaN(approxHohmannDays)) approxHohmannDays = MGADefaults.HohmannDefaultToFDays;
                    minD = Math.Max(0.5, approxHohmannDays * MGADefaults.HohmannMinToFFactor);
                    maxD = Math.Max(minD + 2.0, approxHohmannDays * MGADefaults.HohmannMaxToFFactor);
                }

                if (config.MinToFDays != null && k < config.MinToFDays.Count && config.MinToFDays[k] > 0) minD = config.MinToFDays[k];
                if (config.MaxToFDays != null && k < config.MaxToFDays.Count && config.MaxToFDays[k] > 0) maxD = config.MaxToFDays[k];

                lowerBounds[1 + k] = minD * MGADefaults.SecondsPerDay;
                upperBounds[1 + k] = maxD * MGADefaults.SecondsPerDay;
            }

            if (optDsmEta) {
                for (int k = 0; k < legCount; k++) {
                    int d = 1 + legCount + k;
                    lowerBounds[d] = 0.1;
                    upperBounds[d] = 0.9;
                }
            }

            int popSize = Math.Max(MGADefaults.PopulationSize, config.PopulationSize);
            int maxGens = Math.Max(MGADefaults.MaxGenerations, config.MaxGenerations);

            double[][] pop = new double[popSize][];
            double[] fitness = new double[popSize];
            var random = new System.Random(42);

            // 启发式注种：若处于模式 2 (锁定倾角与 RAAN) 且为中心天体转移 (如 Earth -> Moon)
            // 预先沿锁定停泊轨道面扫描目标天体零点面外偏角交点，将精准共面发射时刻注种至初始种群
            var seedGenomes = new List<double[]>();
            if (config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination)
                && fullChain.Count == 2 && fullChain[0].Equals(central, StringComparison.OrdinalIgnoreCase))
            {
                var pTgt = SolarSystemCatalog.GetPlanet(fullChain[1]);
                if (pTgt != null) {
                    double iRad = config.DepartureInclinationDeg * Mathf.Deg2Rad;
                    double oRad = config.DepartureRAANDeg * Mathf.Deg2Rad;
                    Vector3d hNorm = new Vector3d(Math.Sin(iRad) * Math.Sin(oRad), -Math.Sin(iRad) * Math.Cos(oRad), Math.Cos(iRad));
                    if (hNorm.sqrMagnitude < 0.01) hNorm = new Vector3d(0, 0, 1);
                    else hNorm = hNorm.normalized;

                    double nomTofSec = (lowerBounds[1] + upperBounds[1]) * 0.5;
                    double scanStartUT = winStartUT + lowerBounds[1];
                    double scanEndUT = winEndUT + upperBounds[1];
                    double scanStepSec = 21600.0; // 0.25 天步长细致扫描

                    double prevT = scanStartUT;
                    pTgt.GetRelativeState(central, prevT, out Vector3d rPrev, out _);
                    double prevZ = Vector3d.Dot(rPrev, hNorm);

                    for (double t = scanStartUT + scanStepSec; t <= scanEndUT; t += scanStepSec) {
                        pTgt.GetRelativeState(central, t, out Vector3d rCurr, out _);
                        double currZ = Vector3d.Dot(rCurr, hNorm);

                        if (prevZ * currZ <= 0.0) {
                            // 二分法高精度求解目标天体穿过停泊轨道平面的确切历元 tNode
                            double t1 = prevT, t2 = t;
                            double z1 = prevZ;
                            for (int it = 0; it < 10; it++) {
                                double tMid = 0.5 * (t1 + t2);
                                pTgt.GetRelativeState(central, tMid, out Vector3d rMid, out _);
                                double zMid = Vector3d.Dot(rMid, hNorm);
                                if (zMid * z1 <= 0.0) {
                                    t2 = tMid;
                                } else {
                                    t1 = tMid;
                                    z1 = zMid;
                                }
                            }
                            double tNode = 0.5 * (t1 + t2);

                            // 对应该节点交点的名义发射时刻
                            double depSeedUT = tNode - nomTofSec;
                            if (depSeedUT >= lowerBounds[0] && depSeedUT <= upperBounds[0]) {
                                double[] dToFs = new double[] { 0.0, -0.25 * 86400.0, 0.25 * 86400.0, -0.5 * 86400.0, 0.5 * 86400.0 };
                                foreach (double dt in dToFs) {
                                    double tofCand = Math.Max(lowerBounds[1], Math.Min(upperBounds[1], nomTofSec + dt));
                                    double depCand = Math.Max(lowerBounds[0], Math.Min(upperBounds[0], tNode - tofCand));
                                    var g = new double[dim];
                                    g[0] = depCand;
                                    g[1] = tofCand;
                                    for (int d = 2; d < dim; d++) {
                                        if (d < 1 + legCount) {
                                            g[d] = (lowerBounds[d] + upperBounds[d]) * 0.5;
                                        } else {
                                            int dsmIdx = d - (1 + legCount);
                                            g[d] = (config != null && config.DsmEtas != null && dsmIdx < config.DsmEtas.Count && config.DsmEtas[dsmIdx] > 0)
                                                ? config.DsmEtas[dsmIdx]
                                                : (config != null ? config.DsmEtaDefault : 0.5);
                                        }
                                    }
                                    seedGenomes.Add(g);
                                }
                            }
                        }
                        prevT = t;
                        prevZ = currZ;
                    }
                }
            }

            for (int i = 0; i < popSize; i++) {
                pop[i] = new double[dim];
                if (i < seedGenomes.Count) {
                    pop[i] = (double[])seedGenomes[i].Clone();
                } else {
                    for (int d = 0; d < dim; d++) {
                        pop[i][d] = lowerBounds[d] + random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                    }
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

                    double F = MGADefaults.DE_F_Min + MGADefaults.DE_F_Range * localRand.NextDouble();
                    double CR = MGADefaults.DE_CR_Min + MGADefaults.DE_CR_Range * localRand.NextDouble();
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

            var rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();
            var seenKeys = new HashSet<long>();

            // 第一阶段：细粒度网格采样 (12小时发射UT格 + 1天飞行历时格)，提取帕累托前沿不同飞行时长的多样性解
            foreach (var idx in rankedIndices) {
                if (fitness[idx] >= 1e8) continue;
                var sol = BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: true);
                if (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) {
                    if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                    if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;

                    long key = ((long)(int)(sol.DepartureUT / 43200.0) << 32) ^ (long)(uint)(int)(sol.TotalFlightDays / 1.0);
                    if (!seenKeys.Contains(key)) {
                        seenKeys.Add(key);
                        solutions.Add(sol);
                    }
                }
                if (solutions.Count >= 8) break;
            }

            // 第二阶段保底：若收敛解不足 5 个（如单腿直达转移收敛较紧凑），吸纳发射相差 > 6 小时或飞行历时相差 > 0.5 天的有效解
            if (solutions.Count < 5) {
                foreach (var idx in rankedIndices) {
                    if (fitness[idx] >= 1e8) continue;
                    var sol = BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: true);
                    if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) continue;
                    if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                    if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;

                    bool tooClose = solutions.Any(s => 
                        Math.Abs(s.DepartureUT - sol.DepartureUT) < 21600.0 && 
                        Math.Abs(s.TotalFlightDays - sol.TotalFlightDays) < 0.5
                    );
                    if (!tooClose) {
                        solutions.Add(sol);
                    }
                    if (solutions.Count >= 8) break;
                }
            }

            // 第三阶段：若候选方案仍不足 5 个（例如单腿直达转移仅有 2 个变量，DE 种群高度收敛至单一点），
            // 从最优解的帕累托前沿集合 (Pareto Trade-Off Curve) 提取多样性时间/燃料权衡方案，保证决策池完备
            if (solutions.Count > 0 && solutions.Count < 6) {
                var baseSol = solutions[0];
                if (baseSol.ParetoFront != null && baseSol.ParetoFront.Count > 1) {
                    foreach (var pt in baseSol.ParetoFront) {
                        if (pt.Genome == null) continue;
                        bool alreadyHas = solutions.Any(s => Math.Abs(s.TotalFlightDays - pt.TotalFlightDays) < 0.5);
                        if (!alreadyHas) {
                            var pSol = BuildFullSolution(pt.Genome, fullChain, config, computeAdvanced: true);
                            if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < 1e7) {
                                pSol.ParetoFront = baseSol.ParetoFront;
                                solutions.Add(pSol);
                            }
                        }
                        if (solutions.Count >= 8) break;
                    }
                }
            }

            solutions = solutions.OrderBy(s => s.TotalDeltaVKms).ToList();
            return solutions;
        }

        /// <summary>
        /// 全自动多序列智能批量寻优引擎：
        /// 根据配置参数自动 BFS 派生所有合法借力链路，多核并发寻优，自适应判别并激活 MGA-1DSM，闭环 8 大特性，最优解排在最前面
        /// </summary>
        public static async Task<List<MGASolution>> AutoOptimizeCandidateSequencesAsync(
            MGAConfig baseConfig, 
            Action<float, string> onProgress = null, 
            CancellationToken ct = default)
        {
            if (baseConfig == null || string.IsNullOrEmpty(baseConfig.DepartureBody) || string.IsNullOrEmpty(baseConfig.DestinationBody))
                return new List<MGASolution>();

            // 1. 自动广度优先生成候选借力链路队列
            var chains = GenerateCandidateSequences(baseConfig);
            if (chains == null || chains.Count == 0) {
                chains = new List<List<string>> { new List<string> { baseConfig.DestinationBody } };
            }

            int maxChains = Math.Min(16, chains.Count);
            var aggregatedSolutions = new System.Collections.Concurrent.ConcurrentBag<MGASolution>();

            onProgress?.Invoke(0.05f, $"启动全自动多序列智能寻优 (共 {maxChains} 条候选序列)...");

            int completedCount = 0;
            var lockObj = new object();
            var tasks = new List<Task>();

            for (int cIdx = 0; cIdx < maxChains; cIdx++) {
                var chain = chains[cIdx];
                var seqConfig = baseConfig.Clone();
                seqConfig.FlybySequence = new List<string>(chain);
                seqConfig.SyncStrategiesWithChain(chain.Count);

                // 关键物理修正 1: 清空单腿继承的硬编码 ToF，各航段独立按照真实天体霍曼与共振周期自适应确定物理搜索边界
                seqConfig.MinToFDays = null;
                seqConfig.MaxToFDays = null;

                // 关键物理修正 2: 确保计算减速入轨机动需求，寻优目标严格包含捕获入轨 ΔV
                seqConfig.NoInsertionBurn = false;

                // 智能分析借力链路动力学：检查是否存在同星共振或大偏角，自适应激活 1-DSM
                bool needsDSM = false;
                string prevBody = seqConfig.DepartureBody;
                for (int leg = 0; leg < chain.Count; leg++) {
                    string nextBody = chain[leg];
                    if (nextBody.Equals(prevBody, StringComparison.OrdinalIgnoreCase)) {
                        needsDSM = true;
                        if (seqConfig.LegStrategies != null && leg < seqConfig.LegStrategies.Count) {
                            seqConfig.LegStrategies[leg] = MGALegStrategy.DeepSpaceManeuver1DSM;
                        }
                    }
                    prevBody = nextBody;
                }
                if (needsDSM) {
                    seqConfig.EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM;
                }

                tasks.Add(Task.Run(() => {
                    try {
                        var res = Optimize(seqConfig, null, ct);
                        if (res != null && res.Count > 0) {
                            foreach (var s in res) aggregatedSolutions.Add(s);
                        }
                    } catch (Exception ex) {
                        KSPGMATLog.Warn($"[PykepBridge] Multi-chain sequence note: {ex.Message}");
                    } finally {
                        lock (lockObj) {
                            completedCount++;
                            float p = 0.05f + 0.90f * ((float)completedCount / maxChains);
                            onProgress?.Invoke(p, $"多序列智能寻优: {completedCount}/{maxChains} 序列完成...");
                        }
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);

            // 结果汇总并按总 ΔV 严格升序排序 (最优解排在最前面)
            var sortedList = aggregatedSolutions
                .Where(s => s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < 1e7)
                .OrderBy(s => s.TotalDeltaVKms)
                .ToList();

            var finalSolutions = new List<MGASolution>();
            var seenKeys = new HashSet<string>();
            foreach (var sol in sortedList) {
                string chainKey = sol.FlybyChainSummary ?? "";
                int utBucket = (int)(sol.DepartureUT / 43200.0);
                int tofBucket = (int)(sol.TotalFlightDays / 1.0);
                string compositeKey = $"{chainKey}@{utBucket}_{tofBucket}";
                if (!seenKeys.Contains(compositeKey)) {
                    seenKeys.Add(compositeKey);
                    finalSolutions.Add(sol);
                }
                if (finalSolutions.Count >= 12) break;
            }

            if (finalSolutions.Count < 5) {
                foreach (var sol in sortedList) {
                    bool tooClose = finalSolutions.Any(s => 
                        (s.FlybyChainSummary ?? "") == (sol.FlybyChainSummary ?? "") &&
                        Math.Abs(s.DepartureUT - sol.DepartureUT) < 21600.0 &&
                        Math.Abs(s.TotalFlightDays - sol.TotalFlightDays) < 0.5
                    );
                    if (!tooClose) {
                        finalSolutions.Add(sol);
                    }
                    if (finalSolutions.Count >= 12) break;
                }
            }

            onProgress?.Invoke(1.0f, $"多序列智能寻优完成! 共收敛 {finalSolutions.Count} 个精选方案");
            return finalSolutions;
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

            var allPlanets = SolarSystemCatalog.GetAvailablePlanets();
            if (allPlanets == null || allPlanets.Count == 0) return results;

            // 过滤有效引力助推天体：
            // 仅保留出发天体、目标天体或引力常数 Mu >= 500.0 km^3/s^2 的有效引力源，
            // 彻底剔除 Ryugu, Eros, Bennu 等质量微小、偏转角近乎为0的微型小行星干扰，确保火星与金星排在队列首位
            var planetList = allPlanets.Where(p => 
                !string.IsNullOrEmpty(p.Name) && 
                (p.Name.Equals(origin, StringComparison.OrdinalIgnoreCase) || 
                 p.Name.Equals(dest, StringComparison.OrdinalIgnoreCase) || 
                 p.Mu >= 500.0)
            ).ToList();

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

            var destPlanet = SolarSystemCatalog.GetPlanet(dest);
            bool destHasStrongGravity = (destPlanet != null && destPlanet.Mu >= 1000.0);

            while (queue.Count > 0) {
                // 安全熔断上限，严防异常分支造成游戏卡死
                if (++visitedCount > MGADefaults.MaxBfsVisitedCount || results.Count >= MGADefaults.MaxCandidateResults) break;

                var (currentChain, curRes, curBack) = queue.Dequeue();
                string currentBody = currentChain.Last();
                if (!bodyIndexMap.TryGetValue(currentBody, out int cIdx)) cIdx = -1;

                if (currentChain.Count > 1 && currentBody.Equals(dest, StringComparison.OrdinalIgnoreCase)) {
                    var subChain = new List<string>(currentChain.Skip(1));
                    if (!results.Any(r => r.SequenceEqual(subChain))) {
                        results.Add(subChain);
                    }

                    // 若目标天体自身具备显著引力 (如水星/火星/金星/地球/木星 Mu >= 1000) 且允许共振 (MaxResonances > 0)，
                    // 继续压入目标天体自身同星共振借力分支 (如 Earth -> Venus -> Mercury -> Mercury -> Mercury)，
                    // 彻底对齐 NASA MESSENGER / ESA BepiColombo 水星极低 ΔV 连续减速泊入历史经典任务！
                    if (destHasStrongGravity && config.MaxResonances > 0 && curRes < config.MaxResonances && currentChain.Count < maxLegs + 1) {
                        var resonantChain = new List<string>(currentChain) { dest };
                        queue.Enqueue((resonantChain, curRes + 1, curBack));
                    }
                    continue;
                }

                if (currentChain.Count >= maxLegs + 1) continue;

                for (int i = 0; i < planetList.Count; i++) {
                    var pNext = planetList[i];
                    string nextBody = pNext.Name;
                    if (string.IsNullOrEmpty(nextBody)) continue;

                    bool isResonance = nextBody.Equals(currentBody, StringComparison.OrdinalIgnoreCase);
                    if (isResonance && curRes >= config.MaxResonances) continue;

                    bool isBackLeg = (dIdx > oIdx) ? (i < cIdx) : (i > cIdx);
                    if (isBackLeg && curBack >= config.MaxBackLegs) continue;

                    int spacing = Math.Abs(i - cIdx);
                    if (isBackLeg && spacing > config.MaxBackSpacing) continue;

                    var newChain = new List<string>(currentChain) { nextBody };
                    queue.Enqueue((newChain, curRes + (isResonance ? 1 : 0), curBack + (isBackLeg ? 1 : 0)));
                }
            }

            // 智能天体力学价值赋权排序 (Zero Hardcoding / 完全基于开普勒轨道半长轴阶梯与引力势能深度标准化物理量)
            var pOrigin = SolarSystemCatalog.GetPlanet(origin);
            double rDep = (pOrigin != null && pOrigin.SMA > 0) ? pOrigin.SMA : 1.496e8;
            double rDest = (destPlanet != null && destPlanet.SMA > 0) ? destPlanet.SMA : 1.496e8;
            double rMin = Math.Min(rDep, rDest);
            double rMax = Math.Max(rDep, rDest);
            double rSpan = Math.Max(1.0, rMax - rMin);

            results = results.OrderByDescending(c => {
                // 直接到达标杆基准给予基础基准分
                if (c.Count == 1 && c[0].Equals(dest, StringComparison.OrdinalIgnoreCase)) return 50.0;

                double score = 0.0;
                for (int idx = 0; idx < c.Count; idx++) {
                    string bName = c[idx];
                    var p = SolarSystemCatalog.GetPlanet(bName);
                    if (p == null) continue;

                    // 物理量 1: 天体引力势能深度贡献 (引力井越深，双曲转角与动量改变能力越大)
                    if (p.Mu > 0) {
                        score += 10.0 * Math.Log10(Math.Max(1.0, p.Mu));
                    }

                    // 物理量 2: 轨道阶梯几何匹配 (中间天体公转轨道半长轴处于起点与目标之间时，天然充当轨道能量过渡阶梯)
                    if (p.SMA >= rMin && p.SMA <= rMax) {
                        double midProgress = 1.0 - Math.Abs(p.SMA - (rMin + rMax) * 0.5) / (rSpan * 0.5 + 1e-5);
                        score += 35.0 * Math.Max(0.2, midProgress);
                    }

                    // 物理量 3: 目标天体同星共振减速价值 (当目标天体具备显著引力井时，同星共振逐次削减到达双曲渐近速度 V_inf)
                    if (bName.Equals(dest, StringComparison.OrdinalIgnoreCase) && idx < c.Count - 1 && p.Mu >= 1000.0) {
                        score += 15.0 * Math.Log10(Math.Max(1.0, p.Mu));
                    }
                }

                // 物理量 4: 航段复杂度适度正则化 (2~4 段为工程最实用区间)
                if (c.Count >= 2 && c.Count <= 4) score += 10.0;
                else if (c.Count > 4) score -= (c.Count - 4) * 15.0;

                return score;
            }).ToList();

            return results;
        }

        public static Task<List<MGASolution>> OptimizeAsync(MGAConfig config, Action<float, string> onProgress = null, CancellationToken ct = default) {
            return Task.Run(() => Optimize(config, onProgress, ct), ct);
        }

        public static double GetHohmannTransferDays(string fromBody, string toBody) => GetHohmannTransferTimeDays(fromBody, toBody);

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

        private static double GetCbDistanceToCentral(CelestialBody cb, string central, double defaultAltKm = MGADefaults.DepartureAltitudeKm) {
            if (cb == null) return MGADefaults.EarthRadiusKm + defaultAltKm;
            if (cb.name.Equals(central, StringComparison.OrdinalIgnoreCase) || 
               (cb.bodyName != null && cb.bodyName.Equals(central, StringComparison.OrdinalIgnoreCase))) {
                return (cb.Radius / 1000.0) + defaultAltKm;
            }
            if (cb.orbit != null && cb.orbit.semiMajorAxis > 0) {
                return cb.orbit.semiMajorAxis / 1000.0;
            }
            return (cb.Radius / 1000.0) + defaultAltKm;
        }

        private static string ResolveTransferCentralBody(SolarPlanet p1, SolarPlanet p2) {
            if (p1 == null || p2 == null) return SolarSystemCatalog.RootStarName;
            return SolarSystemCatalog.ResolveTransferCentralBody(p1.Name, p2.Name);
        }

        private static double GetRelativeSMA(SolarPlanet body, string centralBody, double defaultAltKm = MGADefaults.DepartureAltitudeKm) {
            if (body == null) return MGADefaults.EarthRadiusKm + defaultAltKm;
            if (body.Name.Equals(centralBody, StringComparison.OrdinalIgnoreCase)) {
                return (body.RadiusKm > 0 ? body.RadiusKm : MGADefaults.EarthRadiusKm) + defaultAltKm;
            }
            return (body.SMA > 0) ? body.SMA : ((body.RadiusKm > 0 ? body.RadiusKm : MGADefaults.EarthRadiusKm) + defaultAltKm);
        }

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
            return (t1 * t2 / diff) / MGADefaults.SecondsPerDay;
        }

        public static (double bestDepUT, double tofDays, double approxDV) CalculateHohmannWindow(
            string fromBody, string toBody, double searchStartUT, double searchSpanDays = 0, double targetIncDeg = -1.0) 
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
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            // 如果是在行星系内部由母星泊车轨道向卫星转移 (例如 地球 -> 月球, Kerbin -> Mun)
            if (fromBody.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                double r1 = (p1 != null ? p1.RadiusKm : MGADefaults.EarthRadiusKm) + MGADefaults.DepartureAltitudeKm;
                double r2 = (p2 != null && p2.SMA > 0) ? p2.SMA : MGADefaults.MoonAverageSMASecKm;
                if (CalcHohmannTransfer(r1, r2, centralMu, out _, out double tofSec, out double dvDep, out double dvArrHoh)) {
                    double dvArr = (p2 != null && p2.Mu > 0) ? CalculateArrivalInsertionDeltaV(dvArrHoh, p2, MGADefaults.DestinationAltitudeKm, MGADefaults.DestinationAltitudeKm, false) : dvArrHoh;
                    double totalDV = dvDep + dvArr;
                    return (searchStartUT, tofSec / MGADefaults.SecondsPerDay, totalDV);
                }
            }

            // 如果由卫星向母星返回 (例如 月球 -> 地球)
            if (toBody.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                double r1 = (p1 != null && p1.SMA > 0) ? p1.SMA : MGADefaults.MoonAverageSMASecKm;
                double r2 = (p2 != null ? p2.RadiusKm : MGADefaults.EarthRadiusKm) + MGADefaults.DestinationAltitudeKm;
                if (CalcHohmannTransfer(r1, r2, centralMu, out _, out double tofSec, out double dvDepHoh, out double dvArr)) {
                    double dvDep = (p1 != null && p1.Mu > 0) ? CalculateDepartureDeltaV(dvDepHoh, p1, MGADefaults.DepartureAltitudeKm, MGADefaults.DepartureAltitudeKm) : dvDepHoh;
                    double totalDV = dvDep + dvArr;
                    return (searchStartUT, tofSec / MGADefaults.SecondsPerDay, totalDV);
                }
            }

            double sma1 = GetRelativeSMA(p1, central, MGADefaults.DepartureAltitudeKm);
            double sma2 = GetRelativeSMA(p2, central, MGADefaults.DestinationAltitudeKm);

            // 平均角速度 (rad/s) 与会合周期 (基线7.8，100% kep3 原生周期求解)
            double t1 = CalcOrbitPeriodSec(sma1, centralMu);
            double t2 = CalcOrbitPeriodSec(sma2, centralMu);
            double w1 = 2.0 * Math.PI / Math.Max(1e-12, t1);
            double w2 = 2.0 * Math.PI / Math.Max(1e-12, t2);
            double synodicSec = 2.0 * Math.PI / Math.Max(1e-12, Math.Abs(w1 - w2));
            double synodicDays = synodicSec / 86400.0;

            // 标称霍曼转移时间 (通过 kep3 原生求解) 与所需发射相角 (基线7.8: φ = π − ω2·T_hoh), 折叠入 [0, 2π)
            double baseTofSec = 0.0;
            if (!CalcHohmannTransfer(sma1, sma2, centralMu, out _, out baseTofSec, out _, out _)) {
                baseTofSec = 0.5 * CalcOrbitPeriodSec((sma1 + sma2) * 0.5, centralMu);
            }
            double baseTofDays = baseTofSec / 86400.0;

            double reqPhase = Math.PI - w2 * baseTofSec;
            reqPhase = ((reqPhase % (2.0 * Math.PI)) + 2.0 * Math.PI) % (2.0 * Math.PI);

            double scanSpanDays = (searchSpanDays > 0) ? searchSpanDays : (synodicDays * 1.5);
            if (targetIncDeg >= 0 && searchSpanDays <= 0) {
                scanSpanDays = Math.Max(scanSpanDays, synodicDays * 2.0);
            }
            double scanEndUT = searchStartUT + scanSpanDays * 86400.0;

            // 1) 相角扫描: 收集相角最接近理论霍曼相角的候选发射窗口
            var candidateUTs = new List<double>();
            double phaseStep = Math.Max(1800.0, synodicSec / 400.0);
            double prevErr = double.MaxValue;
            double prevUT = searchStartUT;
            bool wasDecreasing = false;

            for (double ut = searchStartUT; ut <= scanEndUT; ut += phaseStep) {
                p1.GetRelativeState(central, ut, out Vector3d posA, out Vector3d velA);
                p2.GetRelativeState(central, ut, out Vector3d posB, out Vector3d velB);
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

            double fineSpanSec = Math.Max(86400.0, synodicSec / 6.0);
            double fineStep = Math.Max(1800.0, synodicSec / 200.0);

            foreach (double candUT in candidateUTs) {
                double fineStartUT = candUT - fineSpanSec;
                double fineEndUT = candUT + fineSpanSec;

                for (double ut = fineStartUT; ut <= fineEndUT; ut += fineStep) {
                    p1.GetRelativeState(central, ut, out Vector3d pos1, out Vector3d vel1);

                    for (double tofMul = 0.85; tofMul <= 1.15; tofMul += 0.15) {
                        double tofSec = baseTofSec * tofMul;
                        p2.GetRelativeState(central, ut + tofSec, out Vector3d pos2, out Vector3d vel2);

                        if (PykepNative.SolveLambert(pos1, pos2, tofSec, centralMu, false, out Vector3d v1, out Vector3d v2)) {
                            Vector3d vInf1Vec = v1 - vel1;
                            double vInf1 = vInf1Vec.magnitude;
                            double vInf2 = (v2 - vel2).magnitude;

                            // 原生 C++ 评估发射与到达 ΔV
                            double dv1 = CalculateDepartureDeltaV(vInf1, p1, 200.0, 200.0);
                            if (!double.IsNaN(dv1)) {
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
            }

            if (minDV >= double.MaxValue * 0.5) {
                KSPGMATLog.Error("霍曼窗口精扫未找到有效原生兰伯特解 (返回失败标志 NaN)");
                return (double.NaN, double.NaN, double.NaN);
            }

            return (bestUT, bestTofDays, minDV);
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
    }
}
