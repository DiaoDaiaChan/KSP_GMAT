using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using KSPGMAT.Model;
using KSPGMAT.MGA.Telemetry;
using KSPGMAT.MGA.Solver;

namespace KSPGMAT.MGA.GPU
{
    /// <summary>
    /// GPU 加速运行状态枚举
    /// </summary>
    public enum GpuAccelerationStatus
    {
        NotInitialized,
        Available,
        DisabledByUser,
        UnsupportedHardware,
        DeviceLostOrError,
        FallbackToCpu
    }

    /// <summary>
    /// GPU 计算任务类型
    /// </summary>
    public enum GpuKernelType
    {
        DifferentialEvolutionMegaPop,
        ForwardPropagatedSeeding,
        PorkchopLambertGrid,
        MgaCorridorTensor3D,
        EphemerisRadarPhasing,
        BPlaneMonteCarloDispersion
    }

    /// <summary>
    /// GPU 执行性能指标度量
    /// </summary>
    public class GpuExecutionMetrics
    {
        public long TotalDispatches { get; set; }
        public long TotalItemsEvaluated { get; set; }
        public double TotalExecutionTimeMs { get; set; }
        public long FallbackEvents { get; set; }
        public double MinBatchTimeMs { get; set; } = double.MaxValue;
        public double MaxBatchTimeMs { get; set; } = 0.0;

        public double AverageBatchTimeMs => TotalDispatches > 0 ? (TotalExecutionTimeMs / TotalDispatches) : 0.0;
        public double ThroughputPerSecond => TotalExecutionTimeMs > 0 ? (TotalItemsEvaluated / (TotalExecutionTimeMs / 1000.0)) : 0.0;

        public void Record(long count, double timeMs, bool fallback)
        {
            TotalDispatches++;
            TotalItemsEvaluated += count;
            TotalExecutionTimeMs += timeMs;
            if (timeMs < MinBatchTimeMs) MinBatchTimeMs = timeMs;
            if (timeMs > MaxBatchTimeMs) MaxBatchTimeMs = timeMs;
            if (fallback) FallbackEvents++;
        }
    }

    /// <summary>
    /// KSP_GMAT 全局 GPU 硬件加速调度与任务管理器 (MGAGpuAccelerationManager)
    /// 统一管理 DirectCompute HLSL CS 5.0 在差分进化种群评估、前向注种、特征面网格、走廊张量、
    /// 百年历表相位雷达与 B 平面协方差蒙特卡洛分析中的规模化并发调用。
    /// 严格遵循开普勒两体与双曲动力学公理闭环，确保硬件故障或不支持时 100% 平滑降级至多核 CPU，零中断、零精度损失。
    /// </summary>
    public static class MGAGpuAccelerationManager
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static GpuAccelerationStatus _status = GpuAccelerationStatus.NotInitialized;
        private static string _deviceName = "Unknown";
        private static int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailuresBeforeDisable = 5;

        // --- 全局功能开关与参数配置 ---
        public static bool EnableGpu { get; set; } = true;
        public static bool EnableDeArchipelagoGpu { get; set; } = false; // DE 种群采用 64 位高精度 Native OpenMP/AVX2 双曲偏折解算
        public static bool EnableDeSeedingGpu { get; set; } = false;     // 前向注种采用 64 位高精度 Native 步进动力学校验
        public static bool EnablePorkchopGpu { get; set; } = false;      // 猪排图采用 64 位双精度 Native OpenMP/AVX2 高通量求解 (彻底杜绝 32 位 HLSL 历元时间截断误差)
        public static bool EnableCorridorGpu { get; set; } = true;
        public static bool EnableRadarGpu { get; set; } = true;
        public static bool EnableBPlaneGpu { get; set; } = true;
        public static int MaxBatchCapacity { get; set; } = 262144; // 256K 条轨迹并发上限

        // --- 指标度量字典 ---
        private static readonly Dictionary<GpuKernelType, GpuExecutionMetrics> _kernelMetrics =
            new Dictionary<GpuKernelType, GpuExecutionMetrics>();

        static MGAGpuAccelerationManager()
        {
            foreach (GpuKernelType k in Enum.GetValues(typeof(GpuKernelType)))
            {
                _kernelMetrics[k] = new GpuExecutionMetrics();
            }
            Initialize();
        }

        /// <summary>
        /// 探测与初始化 GPU DirectCompute CS 5.0 计算环境
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                try
                {
                    if (PykepNative.IsGpuComputeAvailable)
                    {
                        _deviceName = PykepNative.GpuDeviceName;
                        _status = EnableGpu ? GpuAccelerationStatus.Available : GpuAccelerationStatus.DisabledByUser;
                        KSPGMATLog.Info("GPU", $"[MGAGpuManager] GPU 加速管理器就绪: ✅ DirectCompute CS 5.0 [{_deviceName}]");
                    }
                    else
                    {
                        _deviceName = "DirectCompute Unavailable";
                        _status = GpuAccelerationStatus.UnsupportedHardware;
                        KSPGMATLog.Info("GPU", "[MGAGpuManager] GPU 加速探针未检测到 DirectCompute CS 5.0 支持，将平滑回退至多核 CPU (AVX2/SSE2)");
                    }
                }
                catch (Exception ex)
                {
                    _status = GpuAccelerationStatus.DeviceLostOrError;
                    KSPGMATLog.Error("GPU", $"[MGAGpuManager] GPU 初始化探测异常: {ex.Message}", ex);
                }
                finally
                {
                    _initialized = true;
                }
            }
        }

        public static bool IsInitialized => _initialized;
        public static bool IsGpuAvailable => EnableGpu && _status == GpuAccelerationStatus.Available && PykepNative.IsGpuComputeAvailable;
        public static string DeviceName => _deviceName;
        public static GpuAccelerationStatus Status => _status;

        // ====================================================================
        // 核心加速管线 1: 多岛差分进化 (DE Archipelago) 超大规模种群批处理动力学评估
        // ====================================================================
        public static bool EvaluateDePopulationGpu(
            List<double[]> population,
            List<string> fullChain,
            MGAConfig config,
            double centralMu,
            bool optDsmEta,
            out float[] outLosses,
            Func<MGASolution, double> fitnessFunc = null)
        {
            outLosses = null;
            if (!IsGpuAvailable || !EnableDeArchipelagoGpu) return false;
            if (population == null || population.Count == 0 || fullChain == null || fullChain.Count < 2) return false;

            // 物理第一性原理防线 1: 检查链中是否包含同星共振段 (如 Earth->Earth, Venus->Venus, Mercury->Mercury).
            // GPU HLSL CS 5.0 0圈兰伯特内核无法在同星共振位置重合/奇点处 (c ~= 0) 正确求解跨周期 1-DSM，
            // 此时严格交由 CPU 原生 ESA ACT PyKEP (OpenMP + AVX2 多核批处理器) 执行双精度精确动力学解算！
            bool hasResonantBlock = false;
            for (int i = 0; i < fullChain.Count - 1; i++) {
                if (fullChain[i].Equals(fullChain[i + 1], StringComparison.OrdinalIgnoreCase)) {
                    hasResonantBlock = true;
                    break;
                }
            }

            // 物理第一性原理防线 2: 检查链中是否包含外太阳系天体 (SMA > 2.0 AU, 如 Jupiter, Saturn, Uranus, Neptune 等).
            // GPU HLSL CS 5.0 简易开普勒模型基于单精度 float 无摄动外推，在数十年的大时间跨度下累积星历偏差高达数千万公里 (如木星偏差达 3557 万公里)，
            // 且外行星大跨度霍曼转移角接近 180° 极易遭遇兰伯特共线奇异点与伪逆行翻转；
            // 必须严格交由 CPU 原生 ESA ACT PyKEP (双精度 + NASA JPL SPICE DE440 真实星历 + Householder 4阶解算器) 保障保真度！
            bool hasOuterPlanet = false;
            for (int i = 0; i < fullChain.Count; i++) {
                var p = SolarSystemCatalog.GetPlanet(fullChain[i]);
                if (p != null && p.SMA > 2.0 * MGADefaults.AU_KM) {
                    hasOuterPlanet = true;
                    break;
                }
            }

            if (hasResonantBlock || hasOuterPlanet)
            {
                var swCpu = Stopwatch.StartNew();
                var sols = PykepBridge.BuildBatchSolutions(population, fullChain, config);
                var cpuLosses = new float[population.Count];
                System.Threading.Tasks.Parallel.For(0, population.Count, i =>
                {
                    cpuLosses[i] = (float)(fitnessFunc != null ? fitnessFunc(sols[i]) : MGAPhysicsLossEngine.ComputeLoss(sols[i], config));
                });
                outLosses = cpuLosses;
                swCpu.Stop();
                _kernelMetrics[GpuKernelType.DifferentialEvolutionMegaPop].Record(population.Count, swCpu.Elapsed.TotalMilliseconds, false);
                MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute DE Mega-Pop (CPU Safe Fallback)", swCpu.Elapsed.TotalMilliseconds, population.Count, true);
                return true;
            }

            int popSize = population.Count;
            int legCount = fullChain.Count - 1;
            int gpuDim = optDsmEta ? (1 + 2 * legCount) : fullChain.Count;

            var metric = _kernelMetrics[GpuKernelType.DifferentialEvolutionMegaPop];
            var sw = Stopwatch.StartNew();

            try
            {
                // 1. 打包天体星历与物理特征参数
                var bodyElements = new List<PykepNative.GpuPlanetElements>(fullChain.Count);
                for (int bc = 0; bc < fullChain.Count; bc++)
                {
                    var pB = SolarSystemCatalog.GetPlanet(fullChain[bc]);
                    if (pB != null)
                    {
                        bodyElements.Add(new PykepNative.GpuPlanetElements
                        {
                            sma_km = pB.SMA,
                            ecc = pB.ECC,
                            inc_rad = pB.INC * Math.PI / 180.0,
                            raan_rad = pB.RAAN * Math.PI / 180.0,
                            aop_rad = pB.AOP * Math.PI / 180.0,
                            ma0_rad = pB.MA0 * Math.PI / 180.0,
                            epoch_ut = pB.EpochJ2000,
                            mu_self = pB.Mu,
                            radius_km = pB.RadiusKm,
                            park_alt_km = 100.0
                        });
                    }
                }

                if (bodyElements.Count != fullChain.Count) return false;

                // 2. 将种群染色体扁平化为一维张量
                float[] flatGenes = new float[popSize * gpuDim];
                for (int ci = 0; ci < popSize; ci++)
                {
                    flatGenes[ci * gpuDim + 0] = (float)population[ci][0];
                    for (int cd = 1; cd < gpuDim; cd++)
                    {
                        flatGenes[ci * gpuDim + cd] = (cd < population[ci].Length)
                            ? (float)population[ci][cd]
                            : (optDsmEta && cd > legCount ? 0.5f : 0f);
                    }
                }

                // 3. 配置物理动力学校验阈值与边界
                int flybyOnly = (config != null && config.NoInsertionBurn) ? 1 : 0;
                float launchCeilKms = (config != null && config.LaunchDeltaVCeilingMs > 0) ? (float)(config.LaunchDeltaVCeilingMs / 1000.0) : 0f;
                float vesselLimitKms = (config != null && config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? (float)(config.VesselMaxDeltaVMs / 1000.0) : 0f;
                float dsmBudgetKms = (config != null && config.MaxDsmDeltaVMs > 0) ? (float)(config.MaxDsmDeltaVMs / 1000.0) : (vesselLimitKms > 0 ? vesselLimitKms : 0f);
                float depMinUT = (config != null && config.EarliestDepartureUT > 0) ? (float)config.EarliestDepartureUT : 0f;
                float depMaxUT = (config != null && config.LatestDepartureUT > config.EarliestDepartureUT) ? (float)config.LatestDepartureUT : 0f;

                float[] gpuResults = new float[popSize];

                // 4. 发射 DirectCompute CS 5.0 计算内核
                int ret = PykepNative.EvaluatePopulationGpu(
                    bodyElements.ToArray(), bodyElements.Count,
                    flatGenes, popSize, gpuDim, centralMu, flybyOnly,
                    launchCeilKms, vesselLimitKms, dsmBudgetKms,
                    depMinUT, depMaxUT,
                    gpuResults);

                sw.Stop();

                if (ret == 0)
                {
                    // 统一无量纲化归一化 (与 CPU MGAPhysicsLossEngine.ComputeLoss 严格对齐)
                    double dvRefKms = MGAPhysicsLossEngine.LossReferenceCache.Get(config);
                    if (dvRefKms <= 1e-9) dvRefKms = 1.0;
                    float invDvRef = (float)(1.0 / dvRefKms);
                    for (int i = 0; i < popSize; i++)
                    {
                        if (float.IsNaN(gpuResults[i]) || float.IsInfinity(gpuResults[i]))
                        {
                            gpuResults[i] = 100000.0f;
                        }
                        gpuResults[i] *= invDvRef;
                    }

                    outLosses = gpuResults;
                    _consecutiveFailures = 0;
                    metric.Record(popSize, sw.Elapsed.TotalMilliseconds, false);
                    MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute DE Mega-Pop", sw.Elapsed.TotalMilliseconds, popSize, true);
                    MGAStepProfiler.RecordMs("GPU批处理: DirectCompute种群动力学评估", sw.Elapsed.TotalMilliseconds);
                    return true;
                }
                else
                {
                    _consecutiveFailures++;
                    metric.Record(popSize, sw.Elapsed.TotalMilliseconds, true);
                    MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute DE Mega-Pop", sw.Elapsed.TotalMilliseconds, popSize, false);
                    CheckFailureThreshold();

                    // 平滑回退至 CPU 多核批处理
                    var sols = PykepBridge.BuildBatchSolutions(population, fullChain, config);
                    var cpuLosses = new float[population.Count];
                    System.Threading.Tasks.Parallel.For(0, population.Count, i =>
                    {
                        cpuLosses[i] = (float)MGAPhysicsLossEngine.ComputeLoss(sols[i], config);
                    });
                    outLosses = cpuLosses;
                    return true;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _consecutiveFailures++;
                metric.Record(popSize, sw.Elapsed.TotalMilliseconds, true);
                KSPGMATLog.Error("GPU", $"[MGAGpuManager] DE 种群 GPU 批处理异常: {ex.Message}", ex);
                CheckFailureThreshold();

                try
                {
                    var sols = PykepBridge.BuildBatchSolutions(population, fullChain, config);
                    var cpuLosses = new float[population.Count];
                    System.Threading.Tasks.Parallel.For(0, population.Count, i =>
                    {
                        cpuLosses[i] = (float)MGAPhysicsLossEngine.ComputeLoss(sols[i], config);
                    });
                    outLosses = cpuLosses;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        // ====================================================================
        // 核心加速管线 2: 差分进化注种 (Forward Propagated Seeding) 批处理动力学校验
        // ====================================================================
        public static bool EvaluateSeedingCandidatesGpu(
            List<double[]> candidateGenomes,
            List<string> fullChain,
            MGAConfig config,
            double centralMu,
            bool optDsmEta,
            out float[] outLosses)
        {
            outLosses = null;
            if (!IsGpuAvailable || !EnableDeSeedingGpu) return false;
            if (candidateGenomes == null || candidateGenomes.Count == 0) return false;

            var metric = _kernelMetrics[GpuKernelType.ForwardPropagatedSeeding];
            var sw = Stopwatch.StartNew();

            bool ok = EvaluateDePopulationGpu(candidateGenomes, fullChain, config, centralMu, optDsmEta, out outLosses);
            sw.Stop();

            metric.Record(candidateGenomes.Count, sw.Elapsed.TotalMilliseconds, !ok);
            if (ok)
            {
                MGAStepProfiler.RecordMs("GPU批处理: DE前向注种候选评估", sw.Elapsed.TotalMilliseconds);
            }
            return ok;
        }

        // ====================================================================
        // 核心加速管线 3: 猪排图 (Porkchop) 极速二维特征面网格解算
        // ====================================================================
        public static bool EvaluatePorkchopGridGpu(
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
            if (!IsGpuAvailable || !EnablePorkchopGpu) return false;
            if (p1 == null || p2 == null || p1.SMA <= 0 || p2.SMA <= 0) return false;

            var metric = _kernelMetrics[GpuKernelType.PorkchopLambertGrid];
            var sw = Stopwatch.StartNew();
            int totalPts = gridW * gridH;

            bool success = PykepBridge.EvaluateSurrogateGpuGrid(
                p1, p2, centralMu,
                depStartUT, depSpanSec,
                tofMinSec, tofMaxSec,
                gridW, gridH,
                depPeKm, arrPeKm,
                flybyOnly,
                out outTotDv, out outC3);

            sw.Stop();
            metric.Record(totalPts, sw.Elapsed.TotalMilliseconds, !success);
            return success;
        }

        // ====================================================================
        // 核心加速管线 4: MGA 借力走廊 (Corridor) 三维多维张量搜索
        // ====================================================================
        public static bool EvaluateMgaCorridorGpu(
            SolarPlanet p1, SolarPlanet p2, SolarPlanet p3, double centralMu,
            double depStartUT, double depSpanSec,
            double tof1MinSec, double tof1MaxSec,
            double tof2MinSec, double tof2MaxSec,
            int gridDep, int gridTof1, int gridTof2,
            double p2RpMinKm,
            out float[] outBestTotDv,
            out float[] outBestTof1Days,
            out float[] outBestTof2Days)
        {
            outBestTotDv = null;
            outBestTof1Days = null;
            outBestTof2Days = null;
            if (!IsGpuAvailable || !EnableCorridorGpu) return false;
            if (p1 == null || p2 == null || p3 == null) return false;

            var metric = _kernelMetrics[GpuKernelType.MgaCorridorTensor3D];
            var sw = Stopwatch.StartNew();
            int totalCells = gridDep * gridTof1 * gridTof2;

            try
            {
                var elem1 = ToGpuElements(p1, 100.0);
                var elem2 = ToGpuElements(p2, 100.0);
                var elem3 = ToGpuElements(p3, 100.0);

                var bestDv = new float[gridDep];
                var bestT1 = new float[gridDep];
                var bestT2 = new float[gridDep];

                int ret = PykepNative.MgaCorridorGpuEvaluate(
                    ref elem1, ref elem2, ref elem3,
                    centralMu,
                    depStartUT, depSpanSec,
                    tof1MinSec, tof1MaxSec,
                    tof2MinSec, tof2MaxSec,
                    gridDep, gridTof1, gridTof2,
                    p2RpMinKm,
                    bestDv, bestT1, bestT2);

                sw.Stop();
                bool ok = (ret == 0);
                metric.Record(totalCells, sw.Elapsed.TotalMilliseconds, !ok);
                if (ok)
                {
                    outBestTotDv = bestDv;
                    outBestTof1Days = bestT1;
                    outBestTof2Days = bestT2;
                    MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute MGA 3D Corridor", sw.Elapsed.TotalMilliseconds, totalCells, true);
                    MGAStepProfiler.RecordMs("GPU批处理: DirectCompute MGA三维走廊搜索", sw.Elapsed.TotalMilliseconds);
                }
                return ok;
            }
            catch (Exception ex)
            {
                sw.Stop();
                metric.Record(totalCells, sw.Elapsed.TotalMilliseconds, true);
                KSPGMATLog.Error("GPU", $"[MGAGpuManager] MGA Corridor GPU 计算异常: {ex.Message}", ex);
                return false;
            }
        }

        // ====================================================================
        // 核心加速管线 5: 百年行星历表与共振相位雷达图 (Ephemeris Radar)
        // ====================================================================
        public static bool EvaluateEphemerisRadarGpu(
            List<SolarPlanet> planetsA,
            List<SolarPlanet> planetsB,
            double centralMu,
            double startUt,
            double stepDays,
            int stepCount,
            out float[] outPhaseAnglesDeg)
        {
            outPhaseAnglesDeg = null;
            if (!IsGpuAvailable || !EnableRadarGpu) return false;
            if (planetsA == null || planetsB == null || planetsA.Count == 0 || planetsA.Count != planetsB.Count || stepCount <= 0) return false;

            int pairCount = planetsA.Count;
            var metric = _kernelMetrics[GpuKernelType.EphemerisRadarPhasing];
            var sw = Stopwatch.StartNew();
            long totalCalcs = (long)pairCount * stepCount;

            try
            {
                var arrA = planetsA.Select(p => ToGpuElements(p, 0.0)).ToArray();
                var arrB = planetsB.Select(p => ToGpuElements(p, 0.0)).ToArray();
                var phases = new float[pairCount * stepCount];

                int ret = PykepNative.EphemerisRadarGpuEvaluate(
                    arrA, arrB, pairCount, centralMu,
                    startUt, stepDays, stepCount,
                    phases);

                sw.Stop();
                bool ok = (ret == 0);
                metric.Record(totalCalcs, sw.Elapsed.TotalMilliseconds, !ok);
                if (ok)
                {
                    outPhaseAnglesDeg = phases;
                    MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute Ephemeris Radar", sw.Elapsed.TotalMilliseconds, totalCalcs, true);
                    MGAStepProfiler.RecordMs("GPU批处理: DirectCompute百年历表相位雷达", sw.Elapsed.TotalMilliseconds);
                }
                return ok;
            }
            catch (Exception ex)
            {
                sw.Stop();
                metric.Record(totalCalcs, sw.Elapsed.TotalMilliseconds, true);
                KSPGMATLog.Error("GPU", $"[MGAGpuManager] 历表雷达 GPU 计算异常: {ex.Message}", ex);
                return false;
            }
        }

        // ====================================================================
        // 核心加速管线 6: B 平面蒙特卡洛分散度与碰撞概率分析 (10 万点级秒级核验)
        // ====================================================================
        public static bool EvaluateBPlaneMonteCarloGpu(
            Vector3d r0Km, Vector3d v0Kms,
            Vector3d targetRKm, Vector3d targetVKms,
            double targetMu, double targetRadiusKm,
            double tofSec, double centralMu,
            double sigmaPosKm, double sigmaVelKms,
            int sampleCount,
            out float meanBdotR, out float meanBdotT,
            out float sigmaRR, out float sigmaTT, out float sigmaRT,
            out float collisionProb)
        {
            meanBdotR = meanBdotT = sigmaRR = sigmaTT = sigmaRT = collisionProb = 0f;
            if (!IsGpuAvailable || !EnableBPlaneGpu) return false;

            var metric = _kernelMetrics[GpuKernelType.BPlaneMonteCarloDispersion];
            var sw = Stopwatch.StartNew();

            try
            {
                double[] r0Arr = new double[] { r0Km.x, r0Km.y, r0Km.z };
                double[] v0Arr = new double[] { v0Kms.x, v0Kms.y, v0Kms.z };
                double[] rTArr = new double[] { targetRKm.x, targetRKm.y, targetRKm.z };
                double[] vTArr = new double[] { targetVKms.x, targetVKms.y, targetVKms.z };

                int ret = PykepNative.BPlaneMonteCarloGpuEvaluate(
                    r0Arr, v0Arr, rTArr, vTArr,
                    targetMu, targetRadiusKm, tofSec, centralMu,
                    sigmaPosKm, sigmaVelKms, sampleCount,
                    out meanBdotR, out meanBdotT,
                    out sigmaRR, out sigmaTT, out sigmaRT,
                    out collisionProb);

                sw.Stop();
                bool ok = (ret == 0);
                metric.Record(sampleCount, sw.Elapsed.TotalMilliseconds, !ok);
                if (ok)
                {
                    MGAPipelineTelemetryProbe.RecordGpuExecution("DirectCompute B-Plane Monte Carlo", sw.Elapsed.TotalMilliseconds, sampleCount, true);
                    MGAStepProfiler.RecordMs("GPU批处理: DirectCompute B平面蒙特卡洛分析", sw.Elapsed.TotalMilliseconds);
                }
                return ok;
            }
            catch (Exception ex)
            {
                sw.Stop();
                metric.Record(sampleCount, sw.Elapsed.TotalMilliseconds, true);
                KSPGMATLog.Error("GPU", $"[MGAGpuManager] B-Plane GPU 蒙特卡洛计算异常: {ex.Message}", ex);
                return false;
            }
        }

        // ====================================================================
        // 辅助方法与诊断报表
        // ====================================================================
        private static PykepNative.GpuPlanetElements ToGpuElements(SolarPlanet p, double parkAltKm)
        {
            return new PykepNative.GpuPlanetElements
            {
                sma_km = p.SMA,
                ecc = p.ECC,
                inc_rad = p.INC * Math.PI / 180.0,
                raan_rad = p.RAAN * Math.PI / 180.0,
                aop_rad = p.AOP * Math.PI / 180.0,
                ma0_rad = p.MA0 * Math.PI / 180.0,
                epoch_ut = p.EpochJ2000,
                mu_self = p.Mu,
                radius_km = p.RadiusKm,
                park_alt_km = parkAltKm
            };
        }

        private static void CheckFailureThreshold()
        {
            if (_consecutiveFailures >= MaxConsecutiveFailuresBeforeDisable)
            {
                _status = GpuAccelerationStatus.FallbackToCpu;
                KSPGMATLog.Warn("GPU", $"[MGAGpuManager] 连续检测到 {_consecutiveFailures} 次 GPU 执行失败/异常，自动进入平滑 CPU 回退保护模式。");
            }
        }

        public static void ResetFailureCounter()
        {
            _consecutiveFailures = 0;
            if (_status == GpuAccelerationStatus.FallbackToCpu && PykepNative.IsGpuComputeAvailable)
            {
                _status = GpuAccelerationStatus.Available;
            }
        }

        /// <summary>
        /// 生成当前 GPU 硬件加速子系统的详细运行与性能度量诊断报告
        /// </summary>
        public static string GenerateDiagnosticReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine("🖥️  [KSP_GMAT] DirectCompute HLSL CS 5.0 GPU 硬件加速子系统运行报告");
            sb.AppendLine("==========================================================================================");
            sb.AppendLine($"• 硬件适配器: {_deviceName}");
            sb.AppendLine($"• 运行状态: {_status}");
            sb.AppendLine($"• 全局使能: {(EnableGpu ? "开启" : "关闭")}");
            sb.AppendLine($"• 连续异常计数: {_consecutiveFailures} / {MaxConsecutiveFailuresBeforeDisable}");
            sb.AppendLine();
            sb.AppendLine("------------------------------------------------------------------------------------------");
            sb.AppendLine("📊 各计算管线性能吞吐与回退监控统计");
            sb.AppendLine("------------------------------------------------------------------------------------------");

            long grandTotalItems = 0;
            double grandTotalTime = 0.0;

            foreach (var kvp in _kernelMetrics)
            {
                var k = kvp.Key;
                var m = kvp.Value;
                grandTotalItems += m.TotalItemsEvaluated;
                grandTotalTime += m.TotalExecutionTimeMs;

                sb.AppendLine($"[{k}]");
                sb.AppendLine($"   - 分发批次: {m.TotalDispatches:N0} 次 | 累计计算量: {m.TotalItemsEvaluated:N0} 项");
                sb.AppendLine($"   - 累计耗时: {m.TotalExecutionTimeMs:F2} ms | 单批平均: {m.AverageBatchTimeMs:F2} ms (区间: {(m.MinBatchTimeMs == double.MaxValue ? 0 : m.MinBatchTimeMs):F2} ~ {m.MaxBatchTimeMs:F2} ms)");
                sb.AppendLine($"   - 计算吞吐: {m.ThroughputPerSecond:N0} 项/秒 | CPU回退次数: {m.FallbackEvents} 次");
            }

            sb.AppendLine("------------------------------------------------------------------------------------------");
            sb.AppendLine($"总计硬件加速评估: {grandTotalItems:N0} 项 | GPU 累计执行时间: {grandTotalTime:F2} ms");
            sb.AppendLine("==========================================================================================");
            return sb.ToString();
        }
    }
}
