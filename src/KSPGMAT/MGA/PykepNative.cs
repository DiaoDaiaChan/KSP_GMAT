using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KSPGMAT.MGA {
    /// <summary>
    /// ESA PyKEP / keplerian_toolbox 官方原生 C++ 核心动态库 100% 物理直通互操作层
    /// (Direct Dynamic Linkage Bridge to Official ESA ACT Native C++ Core; 0 Managed Fake Math)
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double PykepCalcDepartureDvDelegate(double v_inf, double mu, double radius, double pe_km, double ap_km);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double PykepCalcInsertionDvDelegate(double v_inf, double mu, double radius, double pe_km, double ap_km);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepElementsToStateDelegate(
            double a_km, double e, double i_rad, double raan_rad, double aop_rad,
            double m0_rad, double epoch_ut, double target_ut, double mu_kms,
            ref double rx, ref double ry, ref double rz,
            ref double vx, ref double vy, ref double vz);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepLoadSpiceKernelDelegate([MarshalAs(UnmanagedType.LPStr)] string kernelPath);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepHasSpiceLoadedDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PykepGetLoadedSpicePathDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepSpicePlanetStateDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string bodyName,
            double epochUt,
            [MarshalAs(UnmanagedType.LPStr)] string frame,
            out double rx, out double ry, out double rz,
            out double vx, out double vy, out double vz);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepSpicePlanetElementsDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string bodyName,
            double epochUt,
            double muCentralKms,
            out double outA, out double outE, out double outIDeg,
            out double outRaanDeg, out double outAopDeg, out double outMaDeg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepSpicePlanetConstantsDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string bodyName,
            out double outMu,
            out double outRadiusKm,
            out double outSafeRadiusKm);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCompareBodyStatesDelegate(
            double r1x, double r1y, double r1z,
            double v1x, double v1y, double v1z,
            double r2x, double r2y, double r2z,
            double v2x, double v2y, double v2z,
            double a1, double e1, double inc1Deg,
            double a2, double e2, double inc2Deg,
            out double outPosDiffKm,
            out double outVelDiffMs,
            out double outDaKm,
            out double outDe,
            out double outDincDeg);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        private static bool _isAvailable = false;
        private static string _versionString = "Native Core Unavailable";
        private static IntPtr _dllHandle = IntPtr.Zero;

        private static PykepGetVersionDelegate _fnGetVersion;
        private static PykepLambertSolveDelegate _fnLambertSolve;
        private static PykepFlybyConDelegate _fnFlybyCon;
        private static PykepFlybyDvDelegate _fnFlybyDv;
        private static PykepPropagateLagrangianDelegate _fnPropagateLagrangian;
        private static PykepCalcDepartureDvDelegate _fnCalcDepartureDv;
        private static PykepCalcInsertionDvDelegate _fnCalcInsertionDv;
        private static PykepElementsToStateDelegate _fnElementsToState;
        private static PykepLoadSpiceKernelDelegate _fnLoadSpiceKernel;
        private static PykepHasSpiceLoadedDelegate _fnHasSpiceLoaded;
        private static PykepGetLoadedSpicePathDelegate _fnGetLoadedSpicePath;
        private static PykepSpicePlanetStateDelegate _fnSpicePlanetState;
        private static PykepSpicePlanetElementsDelegate _fnSpicePlanetElements;
        private static PykepSpicePlanetConstantsDelegate _fnSpicePlanetConstants;
        private static PykepCompareBodyStatesDelegate _fnCompareBodyStates;

        private static bool _initAttempted = false;
        private static readonly object _initLock = new object();

        /// <summary>
        /// 严格惰性初始化底层 ESA C++ 原生动态链接库 (防 Loader Lock 死锁)
        /// 绝不在 .cctor / 游戏加载期执行任何 LoadLibrary / PInvoke，仅在实际执行 MGA 物理运算或星历查询时按需加载
        /// </summary>
        public static void EnsureLoaded() {
            if (_initAttempted) return;
            lock (_initLock) {
                if (_initAttempted) return;
                _initAttempted = true;
                InitializeNativeEngine();
            }
        }

        private static void InitializeNativeEngine() {
            try {
                string root = "";
                try { root = KSPUtil.ApplicationRootPath; } catch {}
                if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;

                string nativeDir = Path.GetFullPath(Path.Combine(root, "GameData", "KSPGMAT", "native"));
                if (!Directory.Exists(nativeDir)) {
                    nativeDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "KSPGMAT", "native"));
                }
                if (!Directory.Exists(nativeDir)) {
                    string cur = Directory.GetCurrentDirectory();
                    for (int i = 0; i < 5; i++) {
                        string cand = Path.Combine(cur, "GameData", "KSPGMAT", "native");
                        if (Directory.Exists(cand)) { nativeDir = cand; break; }
                        var parent = Directory.GetParent(cur);
                        if (parent == null) break;
                        cur = parent.FullName;
                    }
                }
                if (!Directory.Exists(nativeDir)) {
                    string fallbackSteam = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\KSPGMAT\native";
                    if (Directory.Exists(fallbackSteam)) nativeDir = fallbackSteam;
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
                        IntPtr pDepDv = GetProcAddress(_dllHandle, "pykep_calc_departure_dv");
                        IntPtr pInsDv = GetProcAddress(_dllHandle, "pykep_calc_insertion_dv");
                        IntPtr pElem = GetProcAddress(_dllHandle, "pykep_elements_to_state");
                        IntPtr pLoadSpice = GetProcAddress(_dllHandle, "pykep_load_spice_kernel");
                        IntPtr pHasSpice = GetProcAddress(_dllHandle, "pykep_has_spice_loaded");
                        IntPtr pGetSpicePath = GetProcAddress(_dllHandle, "pykep_get_loaded_spice_path");
                        IntPtr pSpiceState = GetProcAddress(_dllHandle, "pykep_spice_planet_state");
                        IntPtr pSpiceElem = GetProcAddress(_dllHandle, "pykep_spice_planet_elements");
                        IntPtr pSpiceConst = GetProcAddress(_dllHandle, "pykep_spice_planet_constants");
                        IntPtr pCompStates = GetProcAddress(_dllHandle, "pykep_compare_body_states");

                        if (pVer != IntPtr.Zero && pSolve != IntPtr.Zero) {
                            _fnGetVersion = (PykepGetVersionDelegate)Marshal.GetDelegateForFunctionPointer(pVer, typeof(PykepGetVersionDelegate));
                            _fnLambertSolve = (PykepLambertSolveDelegate)Marshal.GetDelegateForFunctionPointer(pSolve, typeof(PykepLambertSolveDelegate));
                            if (pFbCon != IntPtr.Zero) _fnFlybyCon = (PykepFlybyConDelegate)Marshal.GetDelegateForFunctionPointer(pFbCon, typeof(PykepFlybyConDelegate));
                            if (pFbDv != IntPtr.Zero) _fnFlybyDv = (PykepFlybyDvDelegate)Marshal.GetDelegateForFunctionPointer(pFbDv, typeof(PykepFlybyDvDelegate));
                            if (pProp != IntPtr.Zero) _fnPropagateLagrangian = (PykepPropagateLagrangianDelegate)Marshal.GetDelegateForFunctionPointer(pProp, typeof(PykepPropagateLagrangianDelegate));
                            if (pDepDv != IntPtr.Zero) _fnCalcDepartureDv = (PykepCalcDepartureDvDelegate)Marshal.GetDelegateForFunctionPointer(pDepDv, typeof(PykepCalcDepartureDvDelegate));
                            if (pInsDv != IntPtr.Zero) _fnCalcInsertionDv = (PykepCalcInsertionDvDelegate)Marshal.GetDelegateForFunctionPointer(pInsDv, typeof(PykepCalcInsertionDvDelegate));
                            if (pElem != IntPtr.Zero) _fnElementsToState = (PykepElementsToStateDelegate)Marshal.GetDelegateForFunctionPointer(pElem, typeof(PykepElementsToStateDelegate));
                            if (pLoadSpice != IntPtr.Zero) _fnLoadSpiceKernel = (PykepLoadSpiceKernelDelegate)Marshal.GetDelegateForFunctionPointer(pLoadSpice, typeof(PykepLoadSpiceKernelDelegate));
                            if (pHasSpice != IntPtr.Zero) _fnHasSpiceLoaded = (PykepHasSpiceLoadedDelegate)Marshal.GetDelegateForFunctionPointer(pHasSpice, typeof(PykepHasSpiceLoadedDelegate));
                            if (pGetSpicePath != IntPtr.Zero) _fnGetLoadedSpicePath = (PykepGetLoadedSpicePathDelegate)Marshal.GetDelegateForFunctionPointer(pGetSpicePath, typeof(PykepGetLoadedSpicePathDelegate));
                            if (pSpiceState != IntPtr.Zero) _fnSpicePlanetState = (PykepSpicePlanetStateDelegate)Marshal.GetDelegateForFunctionPointer(pSpiceState, typeof(PykepSpicePlanetStateDelegate));
                            if (pSpiceElem != IntPtr.Zero) _fnSpicePlanetElements = (PykepSpicePlanetElementsDelegate)Marshal.GetDelegateForFunctionPointer(pSpiceElem, typeof(PykepSpicePlanetElementsDelegate));
                            if (pSpiceConst != IntPtr.Zero) _fnSpicePlanetConstants = (PykepSpicePlanetConstantsDelegate)Marshal.GetDelegateForFunctionPointer(pSpiceConst, typeof(PykepSpicePlanetConstantsDelegate));
                            if (pCompStates != IntPtr.Zero) _fnCompareBodyStates = (PykepCompareBodyStatesDelegate)Marshal.GetDelegateForFunctionPointer(pCompStates, typeof(PykepCompareBodyStatesDelegate));

                            IntPtr ptr = _fnGetVersion();
                            if (ptr != IntPtr.Zero) {
                                _versionString = Marshal.PtrToStringAnsi(ptr);
                            }
                            _isAvailable = true;
                            KSPGMATLog.Info($"[PykepNative] Successfully linked official ESA Native C++ Engine: {_versionString}");

                            // 自动检测并导入 GMAT / 系统 SPICE 星历
                            TryAutoLoadSpiceFromGMAT();
                        }
                    }
                }
            } catch (Exception ex) {
                _isAvailable = false;
                KSPGMATLog.Warn($"[PykepNative] Native dynamic link note: {ex.Message}");
            }
        }

        public static bool IsAvailable {
            get {
                EnsureLoaded();
                return _isAvailable;
            }
        }

        public static string Version {
            get {
                EnsureLoaded();
                return _versionString;
            }
        }

        public static IntPtr GetProcAddressExport(string procName) {
            EnsureLoaded();
            if (_dllHandle == IntPtr.Zero) return IntPtr.Zero;
            return GetProcAddress(_dllHandle, procName);
        }

        /// <summary>
        /// 原生调用 ESA 官方 Izzo 兰伯特求解器 (100% 原生 C++ 求解，0 托管自编公式)
        /// </summary>
        public static bool SolveLambert(
            Vector3d r1, Vector3d r2, double tof_sec, double mu, bool retrograde,
            out Vector3d v1, out Vector3d v2)
        {
            EnsureLoaded();
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
                }
                return false;
            }

            KSPGMATLog.Error("pykep_lambert_solve 原生接口未就绪，严禁托管自编公式拟合");
            return false;
        }

        /// <summary>
        /// 原生计算双曲飞掠偏转角、近拱点半径与动力学可行性 (100% 原生 C++ fb_con & fb_vel)
        /// </summary>
        public static bool EvaluateFlyby(
            Vector3d v_in, Vector3d v_out, double mu, double safe_radius,
            out double turningAngleDeg, out double periapsisRadiusKm, out double requiredDeltaV)
        {
            EnsureLoaded();
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
                }
                return false;
            }

            KSPGMATLog.Error("pykep_flyby 原生接口未就绪，严禁托管自编公式拟合");
            return false;
        }

        /// <summary>
        /// 原生 ESA 两体拉格朗日状态外推
        /// </summary>
        public static bool PropagateLagrangian(
            ref Vector3d r, ref Vector3d v, double dt_sec, double mu)
        {
            EnsureLoaded();
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
                }
            }
            return false;
        }

        /// <summary>
        /// 原生轨道根数 (历元平近点角) → 指定时刻日心惯性状态
        /// (100% 原生: 开普勒牛顿迭代 + par2ic + propagate_lagrangian, 0 托管自编公式)
        /// </summary>
        public static bool ElementsToState(
            double aKm, double ecc, double incRad, double raanRad, double aopRad,
            double m0Rad, double epochUT, double targetUT, double muKms,
            out Vector3d posKm, out Vector3d velKms)
        {
            EnsureLoaded();
            posKm = Vector3d.zero;
            velKms = Vector3d.zero;
            if (muKms <= 0 || aKm <= 0 || !_isAvailable || _fnElementsToState == null) return false;
            try {
                double rx = 0, ry = 0, rz = 0, vx = 0, vy = 0, vz = 0;
                int res = _fnElementsToState(
                    aKm, ecc, incRad, raanRad, aopRad,
                    m0Rad, epochUT, targetUT, muKms,
                    ref rx, ref ry, ref rz, ref vx, ref vy, ref vz);
                if (res == 0) {
                    posKm = new Vector3d(rx, ry, rz);
                    velKms = new Vector3d(vx, vy, vz);
                    return true;
                }
            } catch {
            }
            return false;
        }

        /// <summary>
        /// 原生计算出发停泊轨脱离脉冲 ΔV (km/s)
        /// </summary>
        public static double CalculateDepartureDeltaV(double vInfDepKms, double mu, double radiusKm, double peAKm, double apAKm) {
            EnsureLoaded();
            if (_isAvailable && _fnCalcDepartureDv != null) {
                return _fnCalcDepartureDv(vInfDepKms, mu, radiusKm, peAKm, apAKm);
            }
            KSPGMATLog.Error("pykep_calc_departure_dv 原生接口未就绪");
            return double.NaN;
        }

        /// <summary>
        /// 原生计算到达目标星捕获机动脉冲 ΔV (km/s)
        /// </summary>
        public static double CalculateArrivalInsertionDeltaV(double vInfArrKms, double mu, double radiusKm, double peAKm, double apAKm, bool flybyOnly) {
            if (flybyOnly) return 0.0;
            EnsureLoaded();
            if (_isAvailable && _fnCalcInsertionDv != null) {
                return _fnCalcInsertionDv(vInfArrKms, mu, radiusKm, peAKm, apAKm);
            }
            KSPGMATLog.Error("pykep_calc_insertion_dv 原生接口未就绪");
            return double.NaN;
        }

        // =====================================================================
        // SPICE 数值星历原生直通接口 (GMAT DE440/DE430/DE421 自动导入与求值)
        // =====================================================================

        /// <summary>
        /// 当前是否已成功加载 SPICE 星历内核
        /// </summary>
        public static bool HasSpiceLoaded {
            get {
                EnsureLoaded();
                if (!_isAvailable || _fnHasSpiceLoaded == null) return false;
                try { return _fnHasSpiceLoaded() == 1; } catch { return false; }
            }
        }

        /// <summary>
        /// 获取当前加载的 SPICE 星历内核路径
        /// </summary>
        public static string LoadedSpicePath {
            get {
                EnsureLoaded();
                if (!_isAvailable || _fnGetLoadedSpicePath == null) return "";
                try {
                    IntPtr ptr = _fnGetLoadedSpicePath();
                    return (ptr != IntPtr.Zero) ? Marshal.PtrToStringAnsi(ptr) : "";
                } catch { return ""; }
            }
        }

        /// <summary>
        /// 原生导入 SPICE 星历内核 (.bsp/.tls/.tm) (100% 委托 ESA keplerian_toolbox)
        /// </summary>
        public static bool LoadSpiceKernel(string kernelPath) {
            EnsureLoaded();
            if (string.IsNullOrEmpty(kernelPath) || !File.Exists(kernelPath)) return false;
            if (!_isAvailable || _fnLoadSpiceKernel == null) return false;
            try {
                int res = _fnLoadSpiceKernel(kernelPath);
                if (res == 0) {
                    KSPGMATLog.Info($"[PykepNative] Successfully imported SPICE Ephemeris into PyKEP: {Path.GetFileName(kernelPath)} ({kernelPath})");
                    return true;
                } else {
                    KSPGMATLog.Warn($"[PykepNative] Failed to load SPICE kernel into PyKEP, code={res}: {kernelPath}");
                    return false;
                }
            } catch (Exception ex) {
                KSPGMATLog.Error($"[PykepNative] LoadSpiceKernel exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 原生从 SPICE 精确求取指定历元下天体的日心惯性状态 (位置 km, 速度 km/s)
        /// (100% 原生 CSPICE / keplerian_toolbox::planet::spice, 0 托管自编公式)
        /// </summary>
        public static bool GetSpicePlanetState(string bodyName, double epochUt, string frame, out Vector3d posKm, out Vector3d velKms) {
            EnsureLoaded();
            posKm = Vector3d.zero;
            velKms = Vector3d.zero;
            if (string.IsNullOrEmpty(bodyName) || !_isAvailable || _fnSpicePlanetState == null) return false;
            try {
                int res = _fnSpicePlanetState(
                    bodyName, epochUt, frame,
                    out double rx, out double ry, out double rz,
                    out double vx, out double vy, out double vz);
                if (res == 0) {
                    posKm = new Vector3d(rx, ry, rz);
                    velKms = new Vector3d(vx, vy, vz);
                    return true;
                }
            } catch { }
            return false;
        }

        /// <summary>
        /// 原生从 SPICE 切比雪夫多项式与 ic2par 权威反算瞬时开普勒 6 根数
        /// (100% 原生 C++ 转换，0 托管公式，基线 7.0)
        /// </summary>
        public static bool GetSpicePlanetElements(
            string bodyName, double epochUt, double muCentralKms,
            out double smaKm, out double ecc, out double incDeg,
            out double raanDeg, out double aopDeg, out double maDeg)
        {
            EnsureLoaded();
            smaKm = 0; ecc = 0; incDeg = 0; raanDeg = 0; aopDeg = 0; maDeg = 0;
            if (string.IsNullOrEmpty(bodyName) || !_isAvailable || _fnSpicePlanetElements == null) return false;
            try {
                int res = _fnSpicePlanetElements(
                    bodyName, epochUt, muCentralKms,
                    out smaKm, out ecc, out incDeg,
                    out raanDeg, out aopDeg, out maDeg);
                return res == 0;
            } catch { }
            return false;
        }

        /// <summary>
        /// 原生从 SPICE / NASA 权威物理核获取天体常数 (GM km^3/s^2, 物理半径 km, 安全飞越半径 km)
        /// </summary>
        public static bool GetSpicePlanetConstants(
            string bodyName, out double mu, out double radiusKm, out double safeRadiusKm)
        {
            EnsureLoaded();
            mu = 0; radiusKm = 0; safeRadiusKm = 0;
            if (string.IsNullOrEmpty(bodyName) || !_isAvailable || _fnSpicePlanetConstants == null) return false;
            try {
                int res = _fnSpicePlanetConstants(bodyName, out mu, out radiusKm, out safeRadiusKm);
                return res == 0;
            } catch { }
            return false;
        }

        /// <summary>
        /// 方案 1 (NASA JPL SPICE) 与方案 2 (FlightGlobals) 物理状态与轨道根数精确误差核验
        /// (100% 原生 C++ 矢量差与欧氏范数计算，0 托管物理计算，基线 7.0)
        /// </summary>
        public static bool CompareBodyStates(
            Vector3d r1, Vector3d v1, Vector3d r2, Vector3d v2,
            double a1, double e1, double inc1Deg,
            double a2, double e2, double inc2Deg,
            out double posDiffKm, out double velDiffMs,
            out double daKm, out double de, out double dincDeg)
        {
            EnsureLoaded();
            posDiffKm = 0; velDiffMs = 0; daKm = 0; de = 0; dincDeg = 0;
            if (!_isAvailable || _fnCompareBodyStates == null) return false;
            try {
                int res = _fnCompareBodyStates(
                    r1.x, r1.y, r1.z,
                    v1.x, v1.y, v1.z,
                    r2.x, r2.y, r2.z,
                    v2.x, v2.y, v2.z,
                    a1, e1, inc1Deg,
                    a2, e2, inc2Deg,
                    out posDiffKm, out velDiffMs,
                    out daKm, out de, out dincDeg);
                return res == 0;
            } catch { }
            return false;
        }

        /// <summary>
        /// 自动探测 GMAT / 系统中已配置的 SPICE 星历并同步导入至 PyKEP 原生引擎
        /// </summary>
        public static bool TryAutoLoadSpiceFromGMAT(string candidatePath = null) {
            EnsureLoaded();
            if (!_isAvailable || _fnLoadSpiceKernel == null) return false;
            try {
                // 1. 如果传入候选路径且存在，优先加载
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath)) {
                    return LoadSpiceKernel(candidatePath);
                }

                // 2. 探查 GMATClient 已经检测到的 SPICE 星历路径
                try {
                    string gmatSpice = GMATClient.DetectedSpiceKernelPath;
                    if (!string.IsNullOrEmpty(gmatSpice) && File.Exists(gmatSpice)) {
                        return LoadSpiceKernel(gmatSpice);
                    }
                } catch { }

                // 3. 从 KSPGMATSettings 统一配置路径检测
                try {
                    string cfgPath = KSPGMATSettings.SpiceBspPath;
                    if (!string.IsNullOrEmpty(cfgPath) && File.Exists(cfgPath)) {
                        return LoadSpiceKernel(cfgPath);
                    }
                } catch { }

                // 4. 常见候选路径搜索 (GMAT 目录、系统目录、KSP GameData)
                string root = "";
                try { root = KSPUtil.ApplicationRootPath; } catch { }
                if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;

                string binDir = "";
                try { binDir = Path.GetDirectoryName(GMATClient.GmatBinPath); } catch { }

                var searchDirs = new List<string>();
                if (!string.IsNullOrEmpty(binDir)) {
                    searchDirs.Add(Path.Combine(binDir, "..", "data", "planetary_ephem", "spk"));
                    searchDirs.Add(Path.Combine(binDir, "data", "planetary_ephem", "spk"));
                }
                searchDirs.Add(@"C:\Users\43701\gmat-win-R2026a\data\planetary_ephem\spk");
                searchDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GMAT", "spk"));
                searchDirs.Add(Path.Combine(root, "GameData", "KSPGMAT", "data"));

                string[] searchFiles = new string[] { "de440.bsp", "de430.bsp", "de421.bsp", "DE405AllPlanets.bsp" };
                foreach (var dir in searchDirs) {
                    try {
                        if (Directory.Exists(dir)) {
                            foreach (var fn in searchFiles) {
                                string p = Path.Combine(dir, fn);
                                if (File.Exists(p)) {
                                    return LoadSpiceKernel(p);
                                }
                            }
                        }
                    } catch { }
                }
            } catch (Exception ex) {
                KSPGMATLog.Warn($"[PykepNative] Auto-detect SPICE ephemeris note: {ex.Message}");
            }
            return false;
        }
    }
}
