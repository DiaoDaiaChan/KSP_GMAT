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
        private delegate int PykepLambertMultiRevDelegate(
            double r1x, double r1y, double r1z,
            double r2x, double r2y, double r2z,
            double tof_sec, double mu, int retrograde, int nmax,
            int max_solutions,
            out int out_solution_count,
            [Out] double[] out_v1_xyz,
            [Out] double[] out_v2_xyz);

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
        private delegate int PykepSpicePlanetPropertiesDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string bodyName,
            out double outMu,
            out double outRadiusKm,
            out double outSafeRadiusKm,
            out double outSoiRadiusKm,
            out double outAtmDepthKm);

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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepRegisterBodyDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string parentName,
            double mu,
            double radiusKm,
            double safeRadiusKm,
            double soiRadiusKm,
            double atmDepthKm,
            double smaKm,
            double ecc,
            double incDeg,
            double raanDeg,
            double aopDeg,
            double ma0Deg,
            double epochUt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepClearRegisteredBodiesDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepAlignInjectionDelegate(
            [In] double[] rPark0,
            [In] double[] vPark0,
            double tEpoch,
            double muCentral,
            int alignMode,
            [In] double[] targetArg,
            double targetScalar,
            double tStart,
            double tSpan,
            int sampleCount,
            int goldenIterations,
            out double outAlignedUt,
            [Out] double[] outAlignedDvFrenet,
            [Out] double[] outAlignedDvCart);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepAlignInjectionDynDelegate(
            [In] double[] rPark0,
            [In] double[] vPark0,
            double tEpoch,
            double muCentral,
            int alignMode,
            [In] double[] targetArg,
            [In] double[] targetVel,
            double targetScalar,
            double tNominalDep,
            double tStart,
            double tSpan,
            int sampleCount,
            int goldenIterations,
            out double outAlignedUt,
            [Out] double[] outAlignedDvFrenet,
            [Out] double[] outAlignedDvCart);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3HohmannTransferDelegate(
            double r1_km, double r2_km, double mu,
            out double outTotalDv, out double outTofSec, out double outDv1, out double outDv2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3OrbitPeriodDelegate(
            double sma_km, double mu, out double outPeriodSec);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3BiellipticTransferDelegate(
            double r1_km, double r2_km, double rb_km, double mu,
            out double outTotalDv, out double outTofSec);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3CalcReynoldsStmDelegate(
            [In] double[] r0_km, [In] double[] v0_kms, double dt_sec, double mu,
            [Out] double[] out_stm_6x6);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3FlybyVoutDelegate(
            [In] double[] v_in, [In] double[] v_pla, double rp_km, double beta_rad, double mu,
            [Out] double[] out_v_out);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3Vsop2013PlanetStateDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string planetName, double epochJd,
            out double outRx, out double outRy, out double outRz,
            out double outVx, out double outVy, out double outVz);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Kep3CalcCoplanarAngleDelegate(
            double inc_deg, double lan_deg,
            double rx, double ry, double rz,
            out double out_wedge_angle_deg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double PykepCalcCentralDepartureDvDelegate(
            double mu, double radius, double pe_km, double ap_km, double r_dest_km);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepFindNodalCrossingDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string bodyName,
            [MarshalAs(UnmanagedType.LPStr)] string centralBody,
            double epochStartUt, double epochSpanSec,
            double parkIncDeg, double parkRaanDeg,
            out double outNodeUt, out double outNodeDistKm);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepFindTransferWindowDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string fromBody,
            [MarshalAs(UnmanagedType.LPStr)] string toBody,
            [MarshalAs(UnmanagedType.LPStr)] string centralBody,
            double searchStartUt, double searchSpanDays,
            double depAltKm, double arrAltKm,
            double parkIncDeg, double parkRaanDeg,
            out double outBestDepUt, out double outBestTofDays, out double outBestDvKms);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepSynthesizeParkingStateDelegate(
            double muCentral, double rParkKm, int lockPlane,
            double incDeg, double lanDeg,
            [In] double[] rTarget, [In] double[] vTarget,
            [Out] double[] outR1, [Out] double[] outV1);

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
        private static PykepLambertMultiRevDelegate _fnLambertMultiRev;
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
        private static PykepSpicePlanetPropertiesDelegate _fnSpicePlanetProperties;
        private static PykepCompareBodyStatesDelegate _fnCompareBodyStates;
        private static PykepRegisterBodyDelegate _fnRegisterBody;
        private static PykepClearRegisteredBodiesDelegate _fnClearRegisteredBodies;
        private static PykepAlignInjectionDelegate _fnAlignInjection;
        private static PykepAlignInjectionDynDelegate _fnAlignInjectionDyn;

        // kep3 new algorithm hooks
        private static Kep3HohmannTransferDelegate _fnKep3Hohmann;
        private static Kep3OrbitPeriodDelegate _fnKep3OrbitPeriod;
        private static Kep3BiellipticTransferDelegate _fnKep3Bielliptic;
        private static Kep3CalcReynoldsStmDelegate _fnKep3ReynoldsStm;
        private static Kep3FlybyVoutDelegate _fnKep3FlybyVout;
        private static Kep3Vsop2013PlanetStateDelegate _fnKep3Vsop2013;
        private static Kep3CalcCoplanarAngleDelegate _fnKep3CoplanarAngle;
        private static PykepCalcCentralDepartureDvDelegate _fnCalcCentralDepDv;
        private static PykepFindNodalCrossingDelegate _fnFindNodalCrossing;
        private static PykepFindTransferWindowDelegate _fnFindTransferWindow;
        private static PykepSynthesizeParkingStateDelegate _fnSynthesizeParkingState;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCheckCpuFeaturesDelegate(out int sse2, out int avx, out int avx2, out int fma);
        private static PykepCheckCpuFeaturesDelegate _fnCheckCpuFeatures;

        public static bool HasAVX2 { get; private set; } = false;
        public static bool HasFMA { get; private set; } = false;
        public static string CpuFeaturesSummary { get; private set; } = "Unknown";

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct GpuPlanetElements {
            public double sma_km;
            public double ecc;
            public double inc_rad;
            public double raan_rad;
            public double aop_rad;
            public double ma0_rad;
            public double epoch_ut;
            public double mu_self;
            public double radius_km;
            public double park_alt_km;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepCheckGpuComputeSupportDelegate(
            [Out] byte[] outAdapterName,
            int maxLen,
            out int outSupported);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepPorkchopGpuEvaluateDelegate(
            ref GpuPlanetElements p1,
            ref GpuPlanetElements p2,
            double centralMu,
            double depStartUt,
            double depSpanSec,
            double tofMinSec,
            double tofMaxSec,
            int width,
            int height,
            int flybyOnly,
            [Out] float[] outTotDv,
            [Out] float[] outC3);

        private static PykepCheckGpuComputeSupportDelegate _fnCheckGpuComputeSupport;
        private static PykepPorkchopGpuEvaluateDelegate _fnPorkchopGpuEvaluate;
        private static PykepPorkchopGpuEvaluateDelegate _fnPorkchopCpuEvaluate;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepMgaCorridorGpuEvaluateDelegate(
            ref GpuPlanetElements p1,
            ref GpuPlanetElements p2,
            ref GpuPlanetElements p3,
            double centralMu,
            double depStartUt,
            double depSpanSec,
            double tof1MinSec,
            double tof1MaxSec,
            double tof2MinSec,
            double tof2MaxSec,
            int gridDep,
            int gridTof1,
            int gridTof2,
            double p2RpMinKm,
            [Out] float[] outBestTotDv,
            [Out] float[] outBestTof1Days,
            [Out] float[] outBestTof2Days);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepEphemerisRadarGpuEvaluateDelegate(
            [In] GpuPlanetElements[] planetsA,
            [In] GpuPlanetElements[] planetsB,
            int pairCount,
            double centralMu,
            double startUt,
            double stepDays,
            int stepCount,
            [Out] float[] outPhaseAnglesDeg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepEvaluatePopulationGpuDelegate(
            [In] GpuPlanetElements[] bodies,
            int bodyCount,
            [In] float[] populationGenes,
            int popSize,
            int geneDim,
            double centralMu,
            int flybyOnly,
            float launchCeilingKms,
            float vesselBudgetKms,
            float dsmBudgetKms,
            float earliestDepUT,
            float latestDepUT,
            [Out] float[] outPopulationLoss);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepBPlaneMonteCarloGpuEvaluateDelegate(
            [In] double[] r0Km,
            [In] double[] v0Kms,
            [In] double[] targetRKm,
            [In] double[] targetVKms,
            double targetMu,
            double targetRadiusKm,
            double tofSec,
            double centralMu,
            double sigmaPosKm,
            double sigmaVelKms,
            int sampleCount,
            out float outMeanBdotR,
            out float outMeanBdotT,
            out float outSigmaRR,
            out float outSigmaTT,
            out float outSigmaRT,
            out float outCollisionProb);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepSimsFlanaganGpuEvaluateDelegate(
            [In] double[] r1Km,
            [In] double[] v1Kms,
            [In] double[] r2Km,
            [In] double[] v2Kms,
            double tofSec,
            double centralMu,
            int segmentCount,
            int candidateCount,
            [In] float[] impulsesXyz,
            [Out] float[] outMismatchLoss);

        private static PykepMgaCorridorGpuEvaluateDelegate _fnMgaCorridorGpuEvaluate;
        private static PykepEphemerisRadarGpuEvaluateDelegate _fnEphemerisRadarGpuEvaluate;
        private static PykepEvaluatePopulationGpuDelegate _fnEvaluatePopulationGpu;
        private static PykepBPlaneMonteCarloGpuEvaluateDelegate _fnBPlaneMonteCarloGpuEvaluate;
        private static PykepSimsFlanaganGpuEvaluateDelegate _fnSimsFlanaganGpuEvaluate;

        public static bool IsGpuComputeAvailable { get; private set; } = false;
        public static string GpuDeviceName { get; private set; } = "Unknown";

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

        /// <summary>
        /// 在相对路径与工作目录上溯均未命中时, 按 Steam 标准库结构解析 native 目录。
        ///
        /// 【2026-09-14 新增】取代原开发机私有绝对路径
        /// `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\KSPGMAT\native`。
        /// 解析顺序:
        ///   1. 环境变量 `KSPGMAT_NATIVE_DIR` 显式指定 (最高优先级, 便于多实例部署);
        ///   2. 环境变量 `KSP_ROOT` / `KSP_DIR` 指向的安装根;
        ///   3. 常见 Steam 库根目录下的 `steamapps/common/&lt;含 "Kerbal" 的任一安装目录&gt;/GameData/KSPGMAT/native`
        ///      —— 安装目录名通过枚举匹配, **不写死**任何一个具体名称。
        /// 全部未命中时返回空串, 调用方据此判定原生引擎不可用, 不再加载任何来源不明的 DLL。
        /// </summary>
        private static string ResolveSteamLibraryNativeDir() {
            try {
                string explicitDir = Environment.GetEnvironmentVariable("KSPGMAT_NATIVE_DIR");
                if (!string.IsNullOrEmpty(explicitDir) && Directory.Exists(explicitDir)) return explicitDir;

                string kspRoot = Environment.GetEnvironmentVariable("KSP_ROOT");
                if (string.IsNullOrEmpty(kspRoot)) kspRoot = Environment.GetEnvironmentVariable("KSP_DIR");
                if (!string.IsNullOrEmpty(kspRoot)) {
                    string cand = Path.Combine(kspRoot, "GameData", "KSPGMAT", "native");
                    if (Directory.Exists(cand)) return cand;
                }

                var libraryRoots = new List<string>();
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(programFilesX86)) libraryRoots.Add(Path.Combine(programFilesX86, "Steam", "steamapps"));
                if (!string.IsNullOrEmpty(programFiles)) libraryRoots.Add(Path.Combine(programFiles, "Steam", "steamapps"));
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) {
                    libraryRoots.Add(Path.Combine(home, ".steam", "steam", "steamapps"));
                    libraryRoots.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps"));
                }

                foreach (string steamApps in libraryRoots) {
                    string commonDir = Path.Combine(steamApps, "common");
                    if (!Directory.Exists(commonDir)) continue;
                    foreach (string kspInstall in Directory.GetDirectories(commonDir)) {
                        string installName = Path.GetFileName(kspInstall);
                        if (installName.IndexOf("Kerbal", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string cand = Path.Combine(kspInstall, "GameData", "KSPGMAT", "native");
                        if (Directory.Exists(cand)) return cand;
                    }
                }
            } catch { }
            return "";
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
                    // 【2026-09-14 审计修正】此处原为**开发机私有 Steam 库绝对路径**:
                    //     @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\KSPGMAT\native"
                    // 该路径对除作者以外的任何机器都无意义; 更糟的是, 若某台机器恰好存在同名目录,
                    // 会**静默加载该目录下的 DLL**, 形成不可预期的原生库来源。
                    // 现改为按 KSP 安装约定扫描 Steam 标准库位置, 并支持环境变量显式指定。
                    nativeDir = ResolveSteamLibraryNativeDir();
                }

                if (Directory.Exists(nativeDir)) {
                    string tempDir = Path.Combine(Path.GetTempPath(), "KSPGMAT_native");
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                    // Copy all dependent .bin files to tempDir as .dll for Windows PE dependency resolution.
                    // If locked by another running process (e.g. running KSP), fallback to a process-isolated subfolder.
                    var binFiles = Directory.GetFiles(nativeDir, "*.bin");
                    bool copySuccess = true;
                    foreach (var bf in binFiles) {
                        string baseName = Path.GetFileNameWithoutExtension(bf);
                        string dstDll = Path.Combine(tempDir, baseName + ".dll");
                        try {
                            File.Copy(bf, dstDll, true);
                        } catch {
                            copySuccess = false;
                            break;
                        }
                    }

                    if (!copySuccess) {
                        tempDir = Path.Combine(tempDir, "proc_" + System.Diagnostics.Process.GetCurrentProcess().Id);
                        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                        foreach (var bf in binFiles) {
                            string baseName = Path.GetFileNameWithoutExtension(bf);
                            string dstDll = Path.Combine(tempDir, baseName + ".dll");
                            try { File.Copy(bf, dstDll, true); } catch { }
                        }
                    }

                    SetDllDirectory(tempDir);
                    string[] preloads = new[] {
                        "msvcp140", "vcruntime140", "vcruntime140_1",
                        "libomp", "openblas",
                        "libiomp5md", "mkl_core.3", "mkl_def.3", "mkl_intel_thread.3", // 向后兼容回退
                        "fmt", "boost_serialization", "kep3"
                    };
                    foreach (var pl in preloads) {
                        string p = Path.Combine(tempDir, pl + ".dll");
                        if (File.Exists(p)) {
                            IntPtr h = LoadLibrary(p);
                            if (h == IntPtr.Zero) {
                                int err = Marshal.GetLastWin32Error();
                                KSPGMATLog.Warn($"[PykepNative] Failed to preload {pl}.dll, error: {err}");
                            }
                        }
                    }

                    string mainDllPath = Path.Combine(tempDir, "pykep_native.dll");
                    _dllHandle = LoadLibrary(mainDllPath);
                    if (_dllHandle != IntPtr.Zero) {
                        IntPtr pVer = GetProcAddress(_dllHandle, "pykep_get_version");
                        IntPtr pSolve = GetProcAddress(_dllHandle, "pykep_lambert_solve");
                        IntPtr pMultiSolve = GetProcAddress(_dllHandle, "pykep_lambert_multirev");
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
                        IntPtr pSpiceProps = GetProcAddress(_dllHandle, "pykep_spice_planet_properties");
                        IntPtr pCompStates = GetProcAddress(_dllHandle, "pykep_compare_body_states");

                        if (pVer != IntPtr.Zero && pSolve != IntPtr.Zero) {
                            _fnGetVersion = (PykepGetVersionDelegate)Marshal.GetDelegateForFunctionPointer(pVer, typeof(PykepGetVersionDelegate));
                            _fnLambertSolve = (PykepLambertSolveDelegate)Marshal.GetDelegateForFunctionPointer(pSolve, typeof(PykepLambertSolveDelegate));
                            if (pMultiSolve != IntPtr.Zero) _fnLambertMultiRev = (PykepLambertMultiRevDelegate)Marshal.GetDelegateForFunctionPointer(pMultiSolve, typeof(PykepLambertMultiRevDelegate));
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
                            if (pSpiceProps != IntPtr.Zero) _fnSpicePlanetProperties = (PykepSpicePlanetPropertiesDelegate)Marshal.GetDelegateForFunctionPointer(pSpiceProps, typeof(PykepSpicePlanetPropertiesDelegate));
                            if (pCompStates != IntPtr.Zero) _fnCompareBodyStates = (PykepCompareBodyStatesDelegate)Marshal.GetDelegateForFunctionPointer(pCompStates, typeof(PykepCompareBodyStatesDelegate));

                            IntPtr pRegBody = GetProcAddress(_dllHandle, "pykep_register_body");
                            if (pRegBody != IntPtr.Zero) _fnRegisterBody = (PykepRegisterBodyDelegate)Marshal.GetDelegateForFunctionPointer(pRegBody, typeof(PykepRegisterBodyDelegate));
                            IntPtr pClearBodies = GetProcAddress(_dllHandle, "pykep_clear_registered_bodies");
                            if (pClearBodies != IntPtr.Zero) _fnClearRegisteredBodies = (PykepClearRegisteredBodiesDelegate)Marshal.GetDelegateForFunctionPointer(pClearBodies, typeof(PykepClearRegisteredBodiesDelegate));

                            IntPtr pAlign = GetProcAddress(_dllHandle, "pykep_align_injection");
                            if (pAlign != IntPtr.Zero) _fnAlignInjection = (PykepAlignInjectionDelegate)Marshal.GetDelegateForFunctionPointer(pAlign, typeof(PykepAlignInjectionDelegate));
                            IntPtr pAlignDyn = GetProcAddress(_dllHandle, "pykep_align_injection_dyn");
                            if (pAlignDyn != IntPtr.Zero) _fnAlignInjectionDyn = (PykepAlignInjectionDynDelegate)Marshal.GetDelegateForFunctionPointer(pAlignDyn, typeof(PykepAlignInjectionDynDelegate));

                            IntPtr pHohmann = GetProcAddress(_dllHandle, "kep3_hohmann_transfer");
                            if (pHohmann != IntPtr.Zero) _fnKep3Hohmann = (Kep3HohmannTransferDelegate)Marshal.GetDelegateForFunctionPointer(pHohmann, typeof(Kep3HohmannTransferDelegate));
                            IntPtr pOrbitPeriod = GetProcAddress(_dllHandle, "kep3_orbit_period");
                            if (pOrbitPeriod != IntPtr.Zero) _fnKep3OrbitPeriod = (Kep3OrbitPeriodDelegate)Marshal.GetDelegateForFunctionPointer(pOrbitPeriod, typeof(Kep3OrbitPeriodDelegate));
                            IntPtr pBielliptic = GetProcAddress(_dllHandle, "kep3_bielliptic_transfer");
                            if (pBielliptic != IntPtr.Zero) _fnKep3Bielliptic = (Kep3BiellipticTransferDelegate)Marshal.GetDelegateForFunctionPointer(pBielliptic, typeof(Kep3BiellipticTransferDelegate));
                            IntPtr pReynolds = GetProcAddress(_dllHandle, "kep3_calc_reynolds_stm");
                            if (pReynolds != IntPtr.Zero) _fnKep3ReynoldsStm = (Kep3CalcReynoldsStmDelegate)Marshal.GetDelegateForFunctionPointer(pReynolds, typeof(Kep3CalcReynoldsStmDelegate));
                            IntPtr pFlybyVout = GetProcAddress(_dllHandle, "kep3_flyby_vout");
                            if (pFlybyVout != IntPtr.Zero) _fnKep3FlybyVout = (Kep3FlybyVoutDelegate)Marshal.GetDelegateForFunctionPointer(pFlybyVout, typeof(Kep3FlybyVoutDelegate));
                            IntPtr pVsop = GetProcAddress(_dllHandle, "kep3_vsop2013_planet_state");
                            if (pVsop != IntPtr.Zero) _fnKep3Vsop2013 = (Kep3Vsop2013PlanetStateDelegate)Marshal.GetDelegateForFunctionPointer(pVsop, typeof(Kep3Vsop2013PlanetStateDelegate));
                            IntPtr pCoplanar = GetProcAddress(_dllHandle, "kep3_calc_coplanar_angle");
                            if (pCoplanar != IntPtr.Zero) _fnKep3CoplanarAngle = (Kep3CalcCoplanarAngleDelegate)Marshal.GetDelegateForFunctionPointer(pCoplanar, typeof(Kep3CalcCoplanarAngleDelegate));
                            IntPtr pCentralDep = GetProcAddress(_dllHandle, "pykep_calc_central_departure_dv");
                            if (pCentralDep != IntPtr.Zero) _fnCalcCentralDepDv = (PykepCalcCentralDepartureDvDelegate)Marshal.GetDelegateForFunctionPointer(pCentralDep, typeof(PykepCalcCentralDepartureDvDelegate));

                            IntPtr pNode = GetProcAddress(_dllHandle, "pykep_find_nodal_crossing");
                            if (pNode != IntPtr.Zero) _fnFindNodalCrossing = (PykepFindNodalCrossingDelegate)Marshal.GetDelegateForFunctionPointer(pNode, typeof(PykepFindNodalCrossingDelegate));

                            IntPtr pWin = GetProcAddress(_dllHandle, "pykep_find_transfer_window");
                            if (pWin != IntPtr.Zero) _fnFindTransferWindow = (PykepFindTransferWindowDelegate)Marshal.GetDelegateForFunctionPointer(pWin, typeof(PykepFindTransferWindowDelegate));

                            IntPtr pSynPark = GetProcAddress(_dllHandle, "pykep_synthesize_parking_state");
                            if (pSynPark != IntPtr.Zero) _fnSynthesizeParkingState = (PykepSynthesizeParkingStateDelegate)Marshal.GetDelegateForFunctionPointer(pSynPark, typeof(PykepSynthesizeParkingStateDelegate));

                            IntPtr ptr = _fnGetVersion();
                            if (ptr != IntPtr.Zero) {
                                _versionString = Marshal.PtrToStringAnsi(ptr);
                            }
                            _isAvailable = true;
                            KSPGMATLog.Info($"[PykepNative] Successfully linked official ESA Native C++ Engine: {_versionString}");

                            IntPtr pCpu = GetProcAddress(_dllHandle, "pykep_check_cpu_features");
                            if (pCpu != IntPtr.Zero) {
                                _fnCheckCpuFeatures = (PykepCheckCpuFeaturesDelegate)Marshal.GetDelegateForFunctionPointer(pCpu, typeof(PykepCheckCpuFeaturesDelegate));
                                _fnCheckCpuFeatures(out int sse2, out int avx, out int avx2, out int fma);
                                HasAVX2 = (avx2 != 0);
                                HasFMA = (fma != 0);
                                CpuFeaturesSummary = $"[SSE2={sse2}, AVX={avx}, AVX2={avx2}, FMA={fma}]";
                                if (HasAVX2 && HasFMA) {
                                    KSPGMATLog.Info($"[PykepNative] CPU 硬件加速探针: ✅ 已激活 AVX2 + FMA3 极速硬件加速流 {CpuFeaturesSummary}");
                                } else {
                                    KSPGMATLog.Info($"[PykepNative] CPU 硬件探针: 运行于标准兼容模式 (SSE2) {CpuFeaturesSummary}");
                                }
                            }

                            IntPtr pGpuSupport = GetProcAddress(_dllHandle, "pykep_check_gpu_compute_support");
                            IntPtr pGpuPorkchop = GetProcAddress(_dllHandle, "pykep_porkchop_gpu_evaluate");
                            if (pGpuSupport != IntPtr.Zero && pGpuPorkchop != IntPtr.Zero) {
                                _fnCheckGpuComputeSupport = (PykepCheckGpuComputeSupportDelegate)Marshal.GetDelegateForFunctionPointer(pGpuSupport, typeof(PykepCheckGpuComputeSupportDelegate));
                                _fnPorkchopGpuEvaluate = (PykepPorkchopGpuEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pGpuPorkchop, typeof(PykepPorkchopGpuEvaluateDelegate));

                                byte[] nameBuf = new byte[256];
                                int ret = _fnCheckGpuComputeSupport(nameBuf, nameBuf.Length, out int supp);
                                if (ret == 0 && supp != 0) {
                                    IsGpuComputeAvailable = true;
                                    string raw = System.Text.Encoding.ASCII.GetString(nameBuf);
                                    int nullIdx = raw.IndexOf('\0');
                                    GpuDeviceName = nullIdx >= 0 ? raw.Substring(0, nullIdx) : raw;
                                    KSPGMATLog.Info($"[PykepNative] 通用 GPU 加速探针: ✅ DirectCompute CS 5.0 可用 [{GpuDeviceName}]");
                                } else {
                                    IsGpuComputeAvailable = false;
                                    GpuDeviceName = "DirectCompute Unavailable";
                                    KSPGMATLog.Info("[PykepNative] 通用 GPU 加速探针: DirectCompute 不可用，将安全回退至多核 CPU (AVX2)");
                                }
                            }

                            IntPtr pCpuPorkchop = GetProcAddress(_dllHandle, "pykep_porkchop_cpu_evaluate");
                            if (pCpuPorkchop != IntPtr.Zero) {
                                _fnPorkchopCpuEvaluate = (PykepPorkchopGpuEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pCpuPorkchop, typeof(PykepPorkchopGpuEvaluateDelegate));
                                KSPGMATLog.Info("[PykepNative] 原生多核 CPU (OpenMP + AVX2) Porkchop 算子: ✅ 已挂载");
                            }

                            IntPtr pMgaCorridor = GetProcAddress(_dllHandle, "pykep_mga_corridor_gpu_evaluate");
                            if (pMgaCorridor != IntPtr.Zero) {
                                _fnMgaCorridorGpuEvaluate = (PykepMgaCorridorGpuEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pMgaCorridor, typeof(PykepMgaCorridorGpuEvaluateDelegate));
                            }

                            IntPtr pRadar = GetProcAddress(_dllHandle, "pykep_ephemeris_radar_gpu_evaluate");
                            if (pRadar != IntPtr.Zero) {
                                _fnEphemerisRadarGpuEvaluate = (PykepEphemerisRadarGpuEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pRadar, typeof(PykepEphemerisRadarGpuEvaluateDelegate));
                            }

                            IntPtr pMegaPop = GetProcAddress(_dllHandle, "pykep_evaluate_population_gpu");
                            if (pMegaPop != IntPtr.Zero) {
                                _fnEvaluatePopulationGpu = (PykepEvaluatePopulationGpuDelegate)Marshal.GetDelegateForFunctionPointer(pMegaPop, typeof(PykepEvaluatePopulationGpuDelegate));
                            }

                            IntPtr pBPlaneMc = GetProcAddress(_dllHandle, "pykep_bplane_monte_carlo_gpu_evaluate");
                            if (pBPlaneMc != IntPtr.Zero) {
                                _fnBPlaneMonteCarloGpuEvaluate = (PykepBPlaneMonteCarloGpuEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pBPlaneMc, typeof(PykepBPlaneMonteCarloGpuEvaluateDelegate));
                            }

                            IntPtr pSimsFlanagan = GetProcAddress(_dllHandle, "pykep_sims_flanagan_gpu_evaluate");
                            if (pSimsFlanagan != IntPtr.Zero) {
                                _fnSimsFlanaganGpuEvaluate = (PykepSimsFlanaganGpuEvaluateDelegate)Marshal.GetDelegateForFunctionPointer(pSimsFlanagan, typeof(PykepSimsFlanaganGpuEvaluateDelegate));
                            }

                            // 自动检测并导入 GMAT / 系统 SPICE 星历
                            TryAutoLoadSpiceFromGMAT();
                        }
                    }
                }
            } catch (Exception ex) {
                _isAvailable = false;
                KSPGMATLog.Error("PykepNative", $"Native dynamic link note: {ex.Message}", ex);
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
        /// 原生调用 ESA 官方 Izzo 兰伯特多圈求解器 (返回指定圈次上限内所有几何可行解，0 托管自编公式)
        /// </summary>
        public static bool SolveLambertMultiRev(
            Vector3d r1, Vector3d r2, double tof_sec, double mu, bool retrograde, int maxRevs,
            out List<Vector3d> v1List, out List<Vector3d> v2List)
        {
            EnsureLoaded();
            v1List = new List<Vector3d>();
            v2List = new List<Vector3d>();
            if (tof_sec <= 0 || mu <= 0) return false;

            if (_isAvailable && _fnLambertMultiRev != null && maxRevs > 0) {
                try {
                    int maxSolutions = Math.Max(2, (maxRevs + 1) * 2);
                    double[] v1Buffer = new double[maxSolutions * 3];
                    double[] v2Buffer = new double[maxSolutions * 3];

                    int res = _fnLambertMultiRev(
                        r1.x, r1.y, r1.z,
                        r2.x, r2.y, r2.z,
                        tof_sec, mu, retrograde ? 1 : 0, maxRevs,
                        maxSolutions,
                        out int solCount,
                        v1Buffer,
                        v2Buffer);

                    if (res == 0 && solCount > 0) {
                        for (int i = 0; i < solCount; i++) {
                            v1List.Add(new Vector3d(v1Buffer[i * 3 + 0], v1Buffer[i * 3 + 1], v1Buffer[i * 3 + 2]));
                            v2List.Add(new Vector3d(v2Buffer[i * 3 + 0], v2Buffer[i * 3 + 1], v2Buffer[i * 3 + 2]));
                        }
                        return true;
                    }
                } catch {
                }
            }

            // 若多圈未找到或接口未就绪，保底单圈求解
            if (SolveLambert(r1, r2, tof_sec, mu, retrograde, out Vector3d sv1, out Vector3d sv2)) {
                v1List.Add(sv1);
                v2List.Add(sv2);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 原生调用 ESA 官方 Izzo 兰伯特求解器 (支持多圈次，自动选取能量最低最优分支)
        /// </summary>
        public static bool SolveLambert(
            Vector3d r1, Vector3d r2, double tof_sec, double mu, bool retrograde, int maxRevs,
            out Vector3d v1, out Vector3d v2)
        {
            if (maxRevs > 0 && SolveLambertMultiRev(r1, r2, tof_sec, mu, retrograde, maxRevs, out var v1List, out var v2List) && v1List.Count > 0) {
                int bestIdx = 0;
                double minV2 = double.MaxValue;
                for (int i = 0; i < v1List.Count; i++) {
                    double v2Val = v1List[i].sqrMagnitude + v2List[i].sqrMagnitude;
                    if (v2Val < minV2) {
                        minV2 = v2Val;
                        bestIdx = i;
                    }
                }
                v1 = v1List[bestIdx];
                v2 = v2List[bestIdx];
                return true;
            }
            return SolveLambert(r1, r2, tof_sec, mu, retrograde, out v1, out v2);
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
        /// 原生 ESA PyKEP 停泊轨道注入相位与渐近线对齐引擎
        /// </summary>
        public static bool AlignInjection(
            Vector3d rPark0Km,
            Vector3d vPark0Kms,
            double tEpoch,
            double muCentralKms,
            int alignMode, // 0 = 目标位置 Lambert 点火, 1 = 给定 v_inf 双曲逃逸
            Vector3d targetArg,
            double targetScalar, // mode 0: arrUT; mode 1: v_inf magnitude
            double tStart,
            double tSpan,
            int sampleCount,
            int goldenIterations,
            out double alignedUt,
            out Vector3d alignedDvFrenet)
        {
            alignedUt = tStart;
            alignedDvFrenet = Vector3d.zero;
            EnsureLoaded();
            if (!_isAvailable || _fnAlignInjection == null) return false;

            double[] r0 = new double[] { rPark0Km.x, rPark0Km.y, rPark0Km.z };
            double[] v0 = new double[] { vPark0Kms.x, vPark0Kms.y, vPark0Kms.z };
            double[] targ = new double[] { targetArg.x, targetArg.y, targetArg.z };
            double[] dvFrenet = new double[3];

            try {
                int res = _fnAlignInjection(
                    r0, v0, tEpoch, muCentralKms, alignMode, targ, targetScalar,
                    tStart, tSpan, sampleCount, goldenIterations,
                    out alignedUt, dvFrenet, null);

                if (res == 0) {
                    alignedDvFrenet = new Vector3d(dvFrenet[0], dvFrenet[1], dvFrenet[2]);
                    return true;
                }
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"[PykepNative.AlignInjection] Native exception: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// 原生 ESA PyKEP 停泊轨道高精动态滑动靶标锁相引擎
        /// </summary>
        public static bool AlignInjectionDyn(
            Vector3d rPark0Km,
            Vector3d vPark0Kms,
            double tEpoch,
            double muCentralKms,
            int alignMode,
            Vector3d targetArg,
            Vector3d? targetVelKms,
            double targetScalar,
            double tNominalDep,
            double tStart,
            double tSpan,
            int sampleCount,
            int goldenIterations,
            out double alignedUt,
            out Vector3d alignedDvFrenet)
        {
            alignedUt = tStart;
            alignedDvFrenet = Vector3d.zero;
            EnsureLoaded();

            if (_isAvailable && _fnAlignInjectionDyn != null) {
                double[] r0 = new double[] { rPark0Km.x, rPark0Km.y, rPark0Km.z };
                double[] v0 = new double[] { vPark0Kms.x, vPark0Kms.y, vPark0Kms.z };
                double[] targ = new double[] { targetArg.x, targetArg.y, targetArg.z };
                double[] targV = targetVelKms.HasValue ? new double[] { targetVelKms.Value.x, targetVelKms.Value.y, targetVelKms.Value.z } : null;
                double[] dvFrenet = new double[3];

                try {
                    int res = _fnAlignInjectionDyn(
                        r0, v0, tEpoch, muCentralKms, alignMode, targ, targV, targetScalar,
                        tNominalDep, tStart, tSpan, sampleCount, goldenIterations,
                        out alignedUt, dvFrenet, null);

                    if (res == 0) {
                        alignedDvFrenet = new Vector3d(dvFrenet[0], dvFrenet[1], dvFrenet[2]);
                        return true;
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Error("PykepNative", $"[PykepNative.AlignInjectionDyn] Native exception: {ex.Message}", ex);
                }
            }

            // 退化回基准 AlignInjection
            return AlignInjection(rPark0Km, vPark0Kms, tEpoch, muCentralKms, alignMode, targetArg, targetScalar, tStart, tSpan, sampleCount, goldenIterations, out alignedUt, out alignedDvFrenet);
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
                KSPGMATLog.Error("PykepNative", $"LoadSpiceKernel exception: {ex.Message}", ex);
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
            if (string.IsNullOrEmpty(bodyName) || !_isAvailable) return false;
            if (_fnSpicePlanetConstants != null) {
                try {
                    int res = _fnSpicePlanetConstants(bodyName, out mu, out radiusKm, out safeRadiusKm);
                    return res == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生从 SPICE / NASA 权威物理核获取天体完整物理属性 (GM, 物理半径, 安全飞越半径, SOI 影响球半径, 大气高度)
        /// 100% 原生 C++ 动力学核输出，0 托管硬编码 (基线 8.0 & 9.0)
        /// </summary>
        public static bool GetSpicePlanetProperties(
            string bodyName, out double mu, out double radiusKm, out double safeRadiusKm, out double soiRadiusKm, out double atmDepthKm)
        {
            EnsureLoaded();
            mu = 0; radiusKm = 0; safeRadiusKm = 0; soiRadiusKm = 0; atmDepthKm = 0;
            if (string.IsNullOrEmpty(bodyName) || !_isAvailable) return false;
            if (_fnSpicePlanetProperties != null) {
                try {
                    int res = _fnSpicePlanetProperties(bodyName, out mu, out radiusKm, out safeRadiusKm, out soiRadiusKm, out atmDepthKm);
                    return res == 0;
                } catch { }
            }
            if (_fnSpicePlanetConstants != null) {
                try {
                    int res = _fnSpicePlanetConstants(bodyName, out mu, out radiusKm, out safeRadiusKm);
                    return res == 0;
                } catch { }
            }
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
        /// 动态向底层 C++ 注册天体物理常数与开普勒轨道根数（彻底消除硬编码，支持任意行星系统）
        /// </summary>
        public static bool RegisterBody(
            string name, string parentName, double mu, double radiusKm, double safeRadiusKm,
            double soiRadiusKm, double atmDepthKm, double smaKm, double ecc, double incDeg,
            double raanDeg, double aopDeg, double ma0Deg, double epochUt)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name) || !_isAvailable || _fnRegisterBody == null) return false;
            try {
                return _fnRegisterBody(name, parentName ?? "", mu, radiusKm, safeRadiusKm, soiRadiusKm, atmDepthKm, smaKm, ecc, incDeg, raanDeg, aopDeg, ma0Deg, epochUt) == 0;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// 清空底层 C++ 动态注册表并重置为太阳系基准状态
        /// </summary>
        public static bool ClearRegisteredBodies()
        {
            EnsureLoaded();
            if (!_isAvailable || _fnClearRegisteredBodies == null) return false;
            try {
                return _fnClearRegisteredBodies() == 0;
            } catch {
                return false;
            }
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
                string envGmatData = Environment.GetEnvironmentVariable("GMAT_DATA_DIR");
                if (!string.IsNullOrEmpty(envGmatData)) searchDirs.Add(envGmatData);
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
                KSPGMATLog.Error("PykepNative", $"Auto-detect SPICE ephemeris note: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// 原生计算双圆霍曼转移 (100% 原生 kep3::hohmann)
        /// </summary>
        public static bool CalcHohmannTransfer(double r1_km, double r2_km, double mu, out double totalDv, out double tofSec, out double dv1, out double dv2) {
            EnsureLoaded();
            totalDv = 0; tofSec = 0; dv1 = 0; dv2 = 0;
            if (_isAvailable && _fnKep3Hohmann != null) {
                try {
                    return _fnKep3Hohmann(r1_km, r2_km, mu, out totalDv, out tofSec, out dv1, out dv2) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生计算开普勒二体轨道周期 (100% 原生 kep3_orbit_period)
        /// </summary>
        public static bool CalcOrbitPeriodSec(double smaKm, double mu, out double periodSec) {
            EnsureLoaded();
            periodSec = 0.0;
            if (_isAvailable && _fnKep3OrbitPeriod != null) {
                try {
                    return _fnKep3OrbitPeriod(smaKm, mu, out periodSec) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生计算双椭圆转移 (100% 原生 kep3::bielliptic)
        /// </summary>
        public static bool CalcBiellipticTransfer(double r1_km, double r2_km, double rb_km, double mu, out double totalDv, out double tofSec) {
            EnsureLoaded();
            totalDv = 0; tofSec = 0;
            if (_isAvailable && _fnKep3Bielliptic != null) {
                try {
                    return _fnKep3Bielliptic(r1_km, r2_km, rb_km, mu, out totalDv, out tofSec) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生开普勒解析状态转移矩阵 (100% 原生 kep3 2022 Reynolds 解析 STM，机器精度，无有限差分误差)
        /// </summary>
        public static bool CalcReynoldsStm(double[] r0_km, double[] v0_kms, double dt_sec, double mu, out double[] out_stm_6x6) {
            EnsureLoaded();
            out_stm_6x6 = new double[36];
            if (_isAvailable && _fnKep3ReynoldsStm != null && r0_km != null && v0_kms != null) {
                try {
                    return _fnKep3ReynoldsStm(r0_km, v0_kms, dt_sec, mu, out_stm_6x6) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生双曲飞掠出射速度闭式解析 (100% 原生 kep3::fb_vout)
        /// </summary>
        public static bool CalcFlybyVout(double[] v_in, double[] v_pla, double rp_km, double beta_rad, double mu, out double[] out_v_out) {
            EnsureLoaded();
            out_v_out = new double[3];
            if (_isAvailable && _fnKep3FlybyVout != null && v_in != null && v_pla != null) {
                try {
                    return _fnKep3FlybyVout(v_in, v_pla, rp_km, beta_rad, mu, out_v_out) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生 VSOP2013 半解析极高精度行星历表 (100% 原生 kep3::udpla::vsop2013)
        /// </summary>
        public static bool GetVsop2013PlanetState(string planetName, double epochJd, out Vector3d posKm, out Vector3d velKms) {
            EnsureLoaded();
            posKm = Vector3d.zero;
            velKms = Vector3d.zero;
            if (_isAvailable && _fnKep3Vsop2013 != null && !string.IsNullOrEmpty(planetName)) {
                try {
                    int rc = _fnKep3Vsop2013(planetName, epochJd, out double rx, out double ry, out double rz, out double vx, out double vy, out double vz);
                    if (rc == 0) {
                        posKm = new Vector3d(rx, ry, rz);
                        velKms = new Vector3d(vx, vy, vz);
                        return true;
                    }
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生开普勒停泊轨道面与目标天体空间共面偏角与变面夹角计算 (100% 原生 C++ kep3_calc_coplanar_angle)
        /// </summary>
        public static bool CalcCoplanarAngle(double incDeg, double lanDeg, Vector3d rTargetKm, out double wedgeAngleDeg) {
            EnsureLoaded();
            wedgeAngleDeg = 0.0;
            if (_isAvailable && _fnKep3CoplanarAngle != null) {
                try {
                    return _fnKep3CoplanarAngle(incDeg, lanDeg, rTargetKm.x, rTargetKm.y, rTargetKm.z, out wedgeAngleDeg) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生中心天体近地/地月/同系统转移近拱点切向注入能量差速度增量 (100% 原生 C++ pykep_calc_central_departure_dv)
        /// </summary>
        public static double CalcCentralDepartureDv(double mu, double radius, double peKm, double apKm, double rDestKm) {
            EnsureLoaded();
            if (_isAvailable && _fnCalcCentralDepDv != null) {
                try {
                    return _fnCalcCentralDepDv(mu, radius, peKm, apKm, rDestKm);
                } catch { }
            }
            return 0.0;
        }

        /// <summary>
        /// 原生极速求解目标天体穿过停泊轨道平面的升/降交点历元 (100% 原生 C++ pykep_find_nodal_crossing)
        /// 彻底消除托管启发式注种与手写交点扫描，0 托管计算
        /// </summary>
        public static bool FindNodalCrossing(
            string bodyName, string centralBody,
            double epochStartUt, double epochSpanSec,
            double parkIncDeg, double parkRaanDeg,
            out double outNodeUt, out double outNodeDistKm)
        {
            EnsureLoaded();
            outNodeUt = 0.0;
            outNodeDistKm = 0.0;
            if (_isAvailable && _fnFindNodalCrossing != null) {
                try {
                    return _fnFindNodalCrossing(bodyName, centralBody, epochStartUt, epochSpanSec, parkIncDeg, parkRaanDeg, out outNodeUt, out outNodeDistKm) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生全周期三维兰伯特特征能量窗口求解器 (100% 原生 C++ pykep_find_transfer_window)
        /// 运行真实 3D 兰伯特能量全周期扫描与高精局部细化，彻底杜绝托管 atan2 投影伪波谷
        /// </summary>
        public static bool FindTransferWindow(
            string fromBody, string toBody, string centralBody,
            double searchStartUt, double searchSpanDays,
            double depAltKm, double arrAltKm,
            double parkIncDeg, double parkRaanDeg,
            out double outBestDepUt, out double outBestTofDays, out double outBestDvKms)
        {
            EnsureLoaded();
            outBestDepUt = double.NaN;
            outBestTofDays = double.NaN;
            outBestDvKms = double.NaN;
            if (_isAvailable && _fnFindTransferWindow != null) {
                try {
                    return _fnFindTransferWindow(fromBody, toBody, centralBody, searchStartUt, searchSpanDays, depAltKm, arrAltKm, parkIncDeg, parkRaanDeg, out outBestDepUt, out outBestTofDays, out outBestDvKms) == 0;
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// 原生无奇异开普勒停泊轨道边界状态合成器 (100% 原生 C++ pykep_synthesize_parking_state)
        /// 基于真实转移相角 (160°~172°) 确定切向注入点，彻底消除 180° 反共线奇异与人工经验偏置角
        /// </summary>
        public static bool SynthesizeParkingState(
            double muCentral, double rParkKm, bool lockPlane,
            double incDeg, double lanDeg,
            Vector3d rTarget, Vector3d vTarget,
            out Vector3d outR1, out Vector3d outV1)
        {
            outR1 = Vector3d.zero;
            outV1 = Vector3d.zero;
            EnsureLoaded();
            if (_isAvailable && _fnSynthesizeParkingState != null) {
                try {
                    double[] rt = new double[] { rTarget.x, rTarget.y, rTarget.z };
                    double[] vt = new double[] { vTarget.x, vTarget.y, vTarget.z };
                    double[] r1 = new double[3];
                    double[] v1 = new double[3];
                    int ok = _fnSynthesizeParkingState(muCentral, rParkKm, lockPlane ? 1 : 0, incDeg, lanDeg, rt, vt, r1, v1);
                    if (ok == 0) {
                        outR1 = new Vector3d(r1[0], r1[1], r1[2]);
                        outV1 = new Vector3d(v1[0], v1[1], v1[2]);
                        return true;
                    }
                } catch { }
            }
            return false;
        }

        /// <summary>
        /// DirectCompute GPU 极速 Porkchop 批处理求解入口
        /// (通用 DirectX 11 HLSL CS 5.0 并发计算网格点，零额外驱动依赖)
        /// </summary>
        public static int PorkchopGpuEvaluate(
            ref GpuPlanetElements p1,
            ref GpuPlanetElements p2,
            double centralMu,
            double depStartUt,
            double depSpanSec,
            double tofMinSec,
            double tofMaxSec,
            int width,
            int height,
            bool flybyOnly,
            float[] outTotDv,
            float[] outC3)
        {
            EnsureLoaded();
            if (!_isAvailable || !IsGpuComputeAvailable || _fnPorkchopGpuEvaluate == null) return -2;
            try {
                return _fnPorkchopGpuEvaluate(
                    ref p1, ref p2,
                    centralMu,
                    depStartUt, depSpanSec,
                    tofMinSec, tofMaxSec,
                    width, height,
                    flybyOnly ? 1 : 0,
                    outTotDv, outC3);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"GPU 计算异常回退: {ex.Message}", ex);
                return -3;
            }
        }

        /// <summary>
        /// 原生多核 CPU (OpenMP + AVX2) Porkchop 批处理高通量求解入口
        /// (C++ 内部多核并发计算，消灭百万次 P/Invoke 与托管垃圾回收，具备核心数保留防卡死保护)
        /// </summary>
        public static int PorkchopCpuEvaluate(
            ref GpuPlanetElements p1,
            ref GpuPlanetElements p2,
            double centralMu,
            double depStartUt,
            double depSpanSec,
            double tofMinSec,
            double tofMaxSec,
            int width,
            int height,
            bool flybyOnly,
            float[] outTotDv,
            float[] outC3)
        {
            EnsureLoaded();
            if (!_isAvailable || _fnPorkchopCpuEvaluate == null) return -2;
            try {
                return _fnPorkchopCpuEvaluate(
                    ref p1, ref p2,
                    centralMu,
                    depStartUt, depSpanSec,
                    tofMinSec, tofMaxSec,
                    width, height,
                    flybyOnly ? 1 : 0,
                    outTotDv, outC3);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"原生 CPU Porkchop 计算异常: {ex.Message}", ex);
                return -99;
            }
        }

        /// <summary>
        /// DirectCompute GPU 极速多腿 MGA 3D/4D 走廊张量扫描
        /// </summary>
        public static int MgaCorridorGpuEvaluate(
            ref GpuPlanetElements p1,
            ref GpuPlanetElements p2,
            ref GpuPlanetElements p3,
            double centralMu,
            double depStartUt,
            double depSpanSec,
            double tof1MinSec,
            double tof1MaxSec,
            double tof2MinSec,
            double tof2MaxSec,
            int gridDep,
            int gridTof1,
            int gridTof2,
            double p2RpMinKm,
            float[] outBestTotDv,
            float[] outBestTof1Days,
            float[] outBestTof2Days)
        {
            EnsureLoaded();
            if (!_isAvailable || !IsGpuComputeAvailable || _fnMgaCorridorGpuEvaluate == null) return -2;
            try {
                return _fnMgaCorridorGpuEvaluate(
                    ref p1, ref p2, ref p3,
                    centralMu,
                    depStartUt, depSpanSec,
                    tof1MinSec, tof1MaxSec,
                    tof2MinSec, tof2MaxSec,
                    gridDep, gridTof1, gridTof2,
                    p2RpMinKm,
                    outBestTotDv, outBestTof1Days, outBestTof2Days);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"GPU MGA 走廊张量求解异常: {ex.Message}", ex);
                return -3;
            }
        }

        /// <summary>
        /// DirectCompute GPU 极速全太阳系多天体百年历表与共振相位雷达图
        /// </summary>
        public static int EphemerisRadarGpuEvaluate(
            GpuPlanetElements[] planetsA,
            GpuPlanetElements[] planetsB,
            int pairCount,
            double centralMu,
            double startUt,
            double stepDays,
            int stepCount,
            float[] outPhaseAnglesDeg)
        {
            EnsureLoaded();
            if (!_isAvailable || !IsGpuComputeAvailable || _fnEphemerisRadarGpuEvaluate == null) return -2;
            try {
                return _fnEphemerisRadarGpuEvaluate(
                    planetsA, planetsB,
                    pairCount, centralMu,
                    startUt, stepDays, stepCount,
                    outPhaseAnglesDeg);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"GPU 历表雷达计算异常: {ex.Message}", ex);
                return -3;
            }
        }

        /// <summary>
        /// DirectCompute GPU 超大规模种群批处理动力学评估
        /// </summary>
        public static int EvaluatePopulationGpu(
            GpuPlanetElements[] bodies,
            int bodyCount,
            float[] populationGenes,
            int popSize,
            int geneDim,
            double centralMu,
            int flybyOnly,
            float launchCeilingKms,
            float vesselBudgetKms,
            float dsmBudgetKms,
            float earliestDepUT,
            float latestDepUT,
            float[] outPopulationLoss)
        {
            EnsureLoaded();
            if (!_isAvailable || !IsGpuComputeAvailable || _fnEvaluatePopulationGpu == null) return -2;
            try {
                return _fnEvaluatePopulationGpu(
                    bodies, bodyCount,
                    populationGenes, popSize, geneDim,
                    centralMu, flybyOnly,
                    launchCeilingKms, vesselBudgetKms, dsmBudgetKms,
                    earliestDepUT, latestDepUT,
                    outPopulationLoss);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"GPU 种群批处理求解异常: {ex.Message}", ex);
                return -3;
            }
        }

        /// <summary>
        /// DirectCompute GPU 蒙特卡洛轨道分散度与 B 平面协方差分析 (10 万点秒级评估)
        /// </summary>
        public static int BPlaneMonteCarloGpuEvaluate(
            double[] r0Km,
            double[] v0Kms,
            double[] targetRKm,
            double[] targetVKms,
            double targetMu,
            double targetRadiusKm,
            double tofSec,
            double centralMu,
            double sigmaPosKm,
            double sigmaVelKms,
            int sampleCount,
            out float outMeanBdotR,
            out float outMeanBdotT,
            out float outSigmaRR,
            out float outSigmaTT,
            out float outSigmaRT,
            out float outCollisionProb)
        {
            outMeanBdotR = 0f;
            outMeanBdotT = 0f;
            outSigmaRR = 0f;
            outSigmaTT = 0f;
            outSigmaRT = 0f;
            outCollisionProb = 0f;

            EnsureLoaded();
            if (!_isAvailable || !IsGpuComputeAvailable || _fnBPlaneMonteCarloGpuEvaluate == null) return -2;
            try {
                return _fnBPlaneMonteCarloGpuEvaluate(
                    r0Km, v0Kms, targetRKm, targetVKms,
                    targetMu, targetRadiusKm, tofSec, centralMu,
                    sigmaPosKm, sigmaVelKms, sampleCount,
                    out outMeanBdotR, out outMeanBdotT,
                    out outSigmaRR, out outSigmaTT, out outSigmaRT,
                    out outCollisionProb);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"GPU 蒙特卡洛 B 平面评估异常: {ex.Message}", ex);
                return -3;
            }
        }

        /// <summary>
        /// DirectCompute GPU 低推力 Sims-Flanagan 离散脉冲段张量推演
        /// </summary>
        public static int SimsFlanaganGpuEvaluate(
            double[] r1Km,
            double[] v1Kms,
            double[] r2Km,
            double[] v2Kms,
            double tofSec,
            double centralMu,
            int segmentCount,
            int candidateCount,
            float[] impulsesXyz,
            float[] outMismatchLoss)
        {
            EnsureLoaded();
            if (!_isAvailable || !IsGpuComputeAvailable || _fnSimsFlanaganGpuEvaluate == null) return -2;
            try {
                return _fnSimsFlanaganGpuEvaluate(
                    r1Km, v1Kms, r2Km, v2Kms,
                    tofSec, centralMu,
                    segmentCount, candidateCount,
                    impulsesXyz, outMismatchLoss);
            } catch (Exception ex) {
                KSPGMATLog.Error("PykepNative", $"GPU Sims-Flanagan 评估异常: {ex.Message}", ex);
                return -3;
            }
        }
    }
}
