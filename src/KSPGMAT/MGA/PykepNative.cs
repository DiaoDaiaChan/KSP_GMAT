using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KSPGMAT.MGA {
    /// <summary>
    /// ESA PyKEP / keplerian_toolbox 官方原生 C++ 核心动态库高精互操作层
    /// (Direct Dynamic Linkage Bridge to Official ESA ACT Native C++ Core;
    ///  Managed math gated by AllowManagedFallback per Baseline 7 - disabled by default)
    /// </summary>
    public static class PykepNative {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PykepGetVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepLambertSolveDelegate(
            double r1x, double r1y, double r1z,
            double r2x, double r2y, double r2z,
            double tof_sec, double mu, int retrograde,
            out double v1x, out double v1y, out double v1z,
            out double v2x, out double v2y, out double v2z);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepFlybyConDelegate(
            double v_in_x, double v_in_y, double v_in_z,
            double v_out_x, double v_out_y, double v_out_z,
            double mu, double safe_radius,
            out double eq_V2, out double ineq_delta, out double delta_deg, out double rp_km);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double PykepFlybyDvDelegate(
            double v_in_x, double v_in_y, double v_in_z,
            double v_out_x, double v_out_y, double v_out_z,
            double mu, double safe_radius);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepPropagateLagrangianDelegate(
            ref double rx, ref double ry, ref double rz,
            ref double vx, ref double vy, ref double vz,
            double dt_sec, double mu);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        private static bool _isAvailable = false;
        private static string _versionString = "Native Core Unavailable";
        private static IntPtr _dllHandle = IntPtr.Zero;

        /// <summary>
        /// 基线7铁律开关: 原生 PyKEP 不可用时是否允许托管近似回退。
        /// 默认 false —— 托管自编公式禁止执行, 方法返回失败; 仅开发者显式开启后允许
        /// (且 _versionString 如实显示 "Managed Estimate", 不再伪装原生核心)。
        /// </summary>
        public static bool AllowManagedFallback = false;

        private static bool _managedFallbackBlockedLogged = false;

        /// <summary>
        /// 记录"原生库不可用, 已按基线7禁止托管近似回退"的清晰错误 (每会话仅记录一次, 避免循环内刷屏)。
        /// </summary>
        public static void ReportManagedFallbackBlocked() {
            if (_managedFallbackBlockedLogged) return;
            _managedFallbackBlockedLogged = true;
            KSPGMATLog.Error("pykep_native 原生库不可用，已按基线7禁止托管近似回退");
        }

        private static PykepGetVersionDelegate _fnGetVersion;
        private static PykepLambertSolveDelegate _fnLambertSolve;
        private static PykepFlybyConDelegate _fnFlybyCon;
        private static PykepFlybyDvDelegate _fnFlybyDv;
        private static PykepPropagateLagrangianDelegate _fnPropagateLagrangian;

        static PykepNative() {
            try {
                string root = "";
                try { root = KSPUtil.ApplicationRootPath; } catch {}
                if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;

                string nativeDir = Path.GetFullPath(Path.Combine(root, "GameData", "KSPGMAT", "native"));
                if (!Directory.Exists(nativeDir)) {
                    nativeDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "KSPGMAT", "native"));
                }

                if (Directory.Exists(nativeDir)) {
                    string tempDir = Path.Combine(Path.GetTempPath(), "KSPGMAT_native");
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                    // Copy all dependent .bin files to tempDir as .dll for Windows PE dependency resolution
                    var binFiles = Directory.GetFiles(nativeDir, "*.bin");
                    foreach (var bf in binFiles) {
                        string baseName = Path.GetFileNameWithoutExtension(bf);
                        string dstDll = Path.Combine(tempDir, baseName + ".dll");
                        try { File.Copy(bf, dstDll, true); } catch {}
                    }

                    SetDllDirectory(tempDir);
                    LoadLibrary(Path.Combine(tempDir, "msvcp140.dll"));
                    LoadLibrary(Path.Combine(tempDir, "vcruntime140.dll"));
                    LoadLibrary(Path.Combine(tempDir, "vcruntime140_1.dll"));
                    LoadLibrary(Path.Combine(tempDir, "boost_serialization.dll"));
                    LoadLibrary(Path.Combine(tempDir, "keplerian_toolbox.dll"));

                    string mainDllPath = Path.Combine(tempDir, "pykep_native.dll");
                    _dllHandle = LoadLibrary(mainDllPath);
                    if (_dllHandle != IntPtr.Zero) {
                        IntPtr pVer = GetProcAddress(_dllHandle, "pykep_get_version");
                        IntPtr pSolve = GetProcAddress(_dllHandle, "pykep_lambert_solve");
                        IntPtr pFbCon = GetProcAddress(_dllHandle, "pykep_flyby_con");
                        IntPtr pFbDv = GetProcAddress(_dllHandle, "pykep_flyby_dv");
                        IntPtr pProp = GetProcAddress(_dllHandle, "pykep_propagate_lagrangian");

                        if (pVer != IntPtr.Zero && pSolve != IntPtr.Zero) {
                            _fnGetVersion = (PykepGetVersionDelegate)Marshal.GetDelegateForFunctionPointer(pVer, typeof(PykepGetVersionDelegate));
                            _fnLambertSolve = (PykepLambertSolveDelegate)Marshal.GetDelegateForFunctionPointer(pSolve, typeof(PykepLambertSolveDelegate));
                            if (pFbCon != IntPtr.Zero) _fnFlybyCon = (PykepFlybyConDelegate)Marshal.GetDelegateForFunctionPointer(pFbCon, typeof(PykepFlybyConDelegate));
                            if (pFbDv != IntPtr.Zero) _fnFlybyDv = (PykepFlybyDvDelegate)Marshal.GetDelegateForFunctionPointer(pFbDv, typeof(PykepFlybyDvDelegate));
                            if (pProp != IntPtr.Zero) _fnPropagateLagrangian = (PykepPropagateLagrangianDelegate)Marshal.GetDelegateForFunctionPointer(pProp, typeof(PykepPropagateLagrangianDelegate));

                            IntPtr ptr = _fnGetVersion();
                            if (ptr != IntPtr.Zero) {
                                _versionString = Marshal.PtrToStringAnsi(ptr);
                            }
                            _isAvailable = true;
                            KSPGMATLog.Info($"[PykepNative] Successfully linked official ESA Native C++ Engine: {_versionString}");
                        }
                    }
                }
            } catch (Exception ex) {
                _isAvailable = false;
                KSPGMATLog.Warn($"[PykepNative] Native dynamic link note (using high-precision managed math): {ex.Message}");
            }
        }

        public static bool IsAvailable => _isAvailable;
        public static string Version => _versionString;

        public static IntPtr GetProcAddressExport(string procName) {
            if (_dllHandle == IntPtr.Zero) return IntPtr.Zero;
            return GetProcAddress(_dllHandle, procName);
        }

        /// <summary>
        /// 原生调用 ESA 官方 Izzo 兰伯特求解器 (带高精自适应托管兜底)
        /// </summary>
        public static bool SolveLambert(
            Vector3d r1, Vector3d r2, double tof_sec, double mu, bool retrograde,
            out Vector3d v1, out Vector3d v2)
        {
            v1 = Vector3d.zero;
            v2 = Vector3d.zero;
            if (tof_sec <= 0 || mu <= 0) return false;

            if (_isAvailable && _fnLambertSolve != null) {
                try {
                    int res = _fnLambertSolve(
                        r1.x, r1.y, r1.z,
                        r2.x, r2.y, r2.z,
                        tof_sec, mu, retrograde ? 1 : 0,
                        out double v1x, out double v1y, out double v1z,
                        out double v2x, out double v2y, out double v2z);

                    if (res == 0) {
                        v1 = new Vector3d(v1x, v1y, v1z);
                        v2 = new Vector3d(v2x, v2y, v2z);
                        return true;
                    }
                } catch {
                    // 原生调用异常 -> 权威失败, 按基线7不再静默回退托管公式
                }
                return false;
            }

            // 原生核心不可用
            if (!AllowManagedFallback) {
                ReportManagedFallbackBlocked();
                return false;
            }

            // 仅开发者显式开启托管回退时允许, 且版本串如实显示托管估算
            _versionString = "Managed Estimate";
            return SolveLambertManaged(r1, r2, tof_sec, mu, retrograde, out v1, out v2);
        }

        /// <summary>
        /// 原生计算双曲飞掠偏转角、近拱点半径与动力学可行性
        /// </summary>
        public static bool EvaluateFlyby(
            Vector3d v_in, Vector3d v_out, double mu, double safe_radius,
            out double turningAngleDeg, out double periapsisRadiusKm, out double requiredDeltaV)
        {
            turningAngleDeg = 0.0;
            periapsisRadiusKm = 0.0;
            requiredDeltaV = 0.0;

            if (_isAvailable && _fnFlybyCon != null && _fnFlybyDv != null) {
                try {
                    int feasible = _fnFlybyCon(
                        v_in.x, v_in.y, v_in.z,
                        v_out.x, v_out.y, v_out.z,
                        mu, safe_radius,
                        out double eq_V2, out double ineq_delta, out turningAngleDeg, out periapsisRadiusKm);

                    requiredDeltaV = _fnFlybyDv(
                        v_in.x, v_in.y, v_in.z,
                        v_out.x, v_out.y, v_out.z,
                        mu, safe_radius);

                    return feasible == 0;
                } catch {
                    // 原生调用异常 -> 权威失败, 按基线7不再静默回退托管公式
                }
                return false;
            }

            // 原生核心不可用
            if (!AllowManagedFallback) {
                ReportManagedFallbackBlocked();
                return false;
            }

            // 仅开发者显式开启托管回退时允许, 且版本串如实显示托管估算
            _versionString = "Managed Estimate";
            return EvaluateFlybyManaged(v_in, v_out, mu, safe_radius, out turningAngleDeg, out periapsisRadiusKm, out requiredDeltaV);
        }

        /// <summary>
        /// 原生 ESA 两体拉格朗日状态外推
        /// </summary>
        public static bool PropagateLagrangian(
            ref Vector3d r, ref Vector3d v, double dt_sec, double mu)
        {
            if (mu <= 0) return false;
            if (_isAvailable && _fnPropagateLagrangian != null) {
                try {
                    double rx = r.x, ry = r.y, rz = r.z;
                    double vx = v.x, vy = v.y, vz = v.z;
                    int res = _fnPropagateLagrangian(ref rx, ref ry, ref rz, ref vx, ref vy, ref vz, dt_sec, mu);
                    if (res == 0) {
                        r = new Vector3d(rx, ry, rz);
                        v = new Vector3d(vx, vy, vz);
                        return true;
                    }
                } catch {
                    // Fall through
                }
            }
            return false;
        }

        // =========================================================================
        // MANAGED C# IZZO LAMBERT & FLYBY ENGINE
        // (Gated by AllowManagedFallback per Baseline 7 - ONLY used when the developer
        //  explicitly enables managed fallback; results are approximate estimates,
        //  never a substitute for the official ESA native core.)
        // =========================================================================

        private static bool SolveLambertManaged(
            Vector3d r1, Vector3d r2, double tof_sec, double mu, bool retrograde,
            out Vector3d v1, out Vector3d v2)
        {
            v1 = Vector3d.zero;
            v2 = Vector3d.zero;
            if (tof_sec <= 0 || mu <= 0) return false;

            double r1_norm = r1.magnitude;
            double r2_norm = r2.magnitude;
            if (r1_norm <= 0 || r2_norm <= 0) return false;

            double c = (r2 - r1).magnitude;
            double s = (r1_norm + r2_norm + c) * 0.5;

            Vector3d ir1 = r1.normalized;
            Vector3d ir2 = r2.normalized;
            Vector3d ih = Vector3d.Cross(ir1, ir2).normalized;
            if (ih.sqrMagnitude < 1e-12) ih = new Vector3d(0, 0, 1);

            double lambda2 = Math.Max(0.0, 1.0 - c / s);
            double lambda = Math.Sqrt(lambda2);

            Vector3d it1 = Vector3d.Cross(ih, ir1).normalized;
            Vector3d it2 = Vector3d.Cross(ih, ir2).normalized;

            if (ih.z < 0.0) {
                lambda = -lambda;
                it1 = -it1;
                it2 = -it2;
            }

            if (retrograde) {
                lambda = -lambda;
                it1 = -it1;
                it2 = -it2;
            }

            double T = Math.Sqrt(2.0 * mu / (s * s * s)) * tof_sec;
            double lambda3 = lambda * lambda2;

            double T0 = Math.Acos(Math.Max(-1.0, Math.Min(1.0, lambda))) + lambda * Math.Sqrt(Math.Max(0.0, 1.0 - lambda2));
            double T1 = 2.0 / 3.0 * (1.0 - lambda3);

            double x0 = 0.0;
            if (T >= T0) {
                x0 = (T0 - T) / (T0 - T + 4.0);
            } else if (T <= T1) {
                x0 = 2.5 * T1 * (T1 - T) / (T * (1.0 - lambda3 * lambda2)) + 1.0;
            } else {
                x0 = Math.Pow((T0 - T) / (T0 - T1), Math.Log(T0 / T1) / Math.Log(2.0)) - 1.0;
            }

            double x = HouseholderIterationsManaged(T, x0, 1e-12, lambda, 25);

            double gamma = Math.Sqrt(mu * s / 2.0);
            double rho = (r1_norm - r2_norm) / c;
            double sigma = Math.Sqrt(Math.Max(0.0, 1.0 - rho * rho));
            double y = Math.Sqrt(Math.Max(0.0, 1.0 - lambda2 + lambda2 * x * x));

            double vr1 = gamma * ((lambda * y - x) - rho * (lambda * y + x)) / r1_norm;
            double vr2 = -gamma * ((lambda * y - x) + rho * (lambda * y + x)) / r2_norm;
            double vt1 = gamma * sigma * (y + lambda * x) / r1_norm;
            double vt2 = gamma * sigma * (y + lambda * x) / r2_norm;

            v1 = ir1 * vr1 + it1 * vt1;
            v2 = ir2 * vr2 + it2 * vt2;
            return true;
        }

        private static double HouseholderIterationsManaged(double T, double x0, double eps, double lambda, int maxIters) {
            double err = 1.0;
            double DT = 0.0, DDT = 0.0, DDDT = 0.0;
            for (int it = 0; err > eps && it < maxIters; it++) {
                double tof = X2TofManaged(x0, lambda);
                DTdxManaged(ref DT, ref DDT, ref DDDT, x0, tof, lambda);
                double delta = tof - T;
                double DT2 = DT * DT;
                double denom = DT * (DT2 - delta * DDT) + DDDT * delta * delta / 6.0;
                if (Math.Abs(denom) < 1e-15) break;
                double xnew = x0 - delta * (DT2 - delta * DDT / 2.0) / denom;
                err = Math.Abs(x0 - xnew);
                x0 = xnew;
            }
            return x0;
        }

        private static double X2TofManaged(double x, double lambda) {
            double dist = Math.Abs(x - 1.0);
            if (dist < 0.2 && dist > 0.01) {
                double a = 1.0 / (1.0 - x * x);
                if (a > 0.0) {
                    double alfa = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, x)));
                    double beta = 2.0 * Math.Asin(Math.Max(-1.0, Math.Min(1.0, Math.Sqrt(Math.Max(0.0, lambda * lambda / a)))));
                    if (lambda < 0.0) beta = -beta;
                    return (a * Math.Sqrt(a) * ((alfa - Math.Sin(alfa)) - (beta - Math.Sin(beta)))) / 2.0;
                } else {
                    double alfa = 2.0 * Math.Log(x + Math.Sqrt(x * x - 1.0));
                    double arg = Math.Sqrt(Math.Max(0.0, -lambda * lambda / a));
                    double beta = 2.0 * Math.Log(arg + Math.Sqrt(arg * arg + 1.0));
                    if (lambda < 0.0) beta = -beta;
                    return (-a * Math.Sqrt(-a) * ((beta - Math.Sinh(beta)) - (alfa - Math.Sinh(alfa)))) / 2.0;
                }
            }

            double K = lambda * lambda;
            double E = x * x - 1.0;
            double rho = Math.Abs(E);
            double z = Math.Sqrt(Math.Max(0.0, 1.0 + K * E));
            if (dist < 0.01) {
                double eta = z - lambda * x;
                double S1 = 0.5 * (1.0 - lambda - x * eta);
                double Q = 4.0 / 3.0 * HypergeometricFManaged(S1);
                return (eta * eta * eta * Q + 4.0 * lambda * eta) / 2.0;
            } else {
                double y = Math.Sqrt(rho);
                double g = x * z - lambda * E;
                double d = (E < 0.0) ? Math.Acos(Math.Max(-1.0, Math.Min(1.0, g))) : Math.Log(Math.Max(1e-15, y * (z - lambda * x) + g));
                return (x - lambda * z - d / y) / E;
            }
        }

        private static double HypergeometricFManaged(double z, double tol = 1e-11) {
            double Sj = 1.0, Cj = 1.0, err = 1.0;
            int j = 0;
            while (err > tol && j < 100) {
                double Cj1 = Cj * (3.0 + j) * (1.0 + j) / (2.5 + j) * z / (j + 1.0);
                double Sj1 = Sj + Cj1;
                err = Math.Abs(Cj1);
                Sj = Sj1;
                Cj = Cj1;
                j++;
            }
            return Sj;
        }

        private static void DTdxManaged(ref double DT, ref double DDT, ref double DDDT, double x, double T, double lambda) {
            double l2 = lambda * lambda;
            double l3 = l2 * lambda;
            double umx2 = 1.0 - x * x;
            double y = Math.Sqrt(Math.Max(1e-12, 1.0 - l2 * umx2));
            double y2 = y * y;
            double y3 = y2 * y;
            DT = 1.0 / umx2 * (3.0 * T * x - 2.0 + 2.0 * l3 * x / y);
            DDT = 1.0 / umx2 * (3.0 * T + 5.0 * x * DT + 2.0 * (1.0 - l2) * l3 / y3);
            DDDT = 1.0 / umx2 * (7.0 * x * DDT + 8.0 * DT - 6.0 * (1.0 - l2) * l2 * l3 * x / y3 / y2);
        }

        private static bool EvaluateFlybyManaged(
            Vector3d v_in, Vector3d v_out, double mu, double safe_radius,
            out double turningAngleDeg, out double periapsisRadiusKm, out double requiredDeltaV)
        {
            double vin2 = v_in.sqrMagnitude;
            double vout2 = v_out.sqrMagnitude;
            double vin = Math.Sqrt(vin2);
            double vout = Math.Sqrt(vout2);

            double cos_delta = Vector3d.Dot(v_in, v_out) / Math.Max(1e-12, vin * vout);
            cos_delta = Math.Max(-1.0, Math.Min(1.0, cos_delta));
            turningAngleDeg = Math.Acos(cos_delta) * (180.0 / Math.PI);

            double sin_half_delta = Math.Sin(Math.Acos(cos_delta) * 0.5);
            if (sin_half_delta > 1e-6 && vin2 > 1e-6) {
                periapsisRadiusKm = (mu / vin2) * (1.0 / sin_half_delta - 1.0);
            } else {
                periapsisRadiusKm = safe_radius;
            }

            double e_min = 1.0 + (safe_radius / mu) * vin2;
            double max_turning_cos = 1.0 - 2.0 / (e_min * e_min);
            double ineq_delta = max_turning_cos - cos_delta;

            double max_turn = 2.0 * Math.Asin(1.0 / e_min);
            double delta = Math.Acos(cos_delta);
            if (delta <= max_turn) {
                requiredDeltaV = Math.Abs(vin - vout);
            } else {
                double v_inf = Math.Min(vin, vout);
                double deficit = 2.0 * v_inf * Math.Sin((delta - max_turn) * 0.5);
                requiredDeltaV = deficit + Math.Abs(vin - vout);
            }

            return (ineq_delta <= 0.0) && (periapsisRadiusKm >= safe_radius);
        }
    }
}
