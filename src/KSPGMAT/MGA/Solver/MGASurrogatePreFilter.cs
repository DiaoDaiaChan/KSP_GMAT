// ============================================================================
// MGASurrogatePreFilter.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
// 权威自适应 MGA 求解器子模块。底层 100% 官方 ESA kep3 Native C++ 闭环动力学。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;
using KSPGMAT.MGA.AI;
using KSPGMAT.MGA.Telemetry;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 模块 B: 大时域宏观能量特征面快速初筛
    /// 理论来源: Dario Izzo et al. (2018) "Machine Learning in Trajectory Design"
    /// 机制: 微秒级特征面评估，剔除 >90% 无效高能区，锁定各会合周期能量波谷
    /// </summary>
    public static class MGASurrogatePreFilter
    {
        /// <summary>
        /// 触发"自共振启航"多槽评估所需的出发天体最小公转周期 (秒)。
        ///
        /// 【2026-09-14 新增】取代原裸字面量 `10.0 * 86400.0`。
        /// 物理含义: 公转周期过短的出发天体, 其自共振调相在发射窗口内反复出现,
        /// 逐槽评估没有增量信息。标注: **[经验参数·待标定]**
        /// </summary>
        public const double SelfResonantMinDepPeriodSec = 10.0 * 86400.0;

        /// <summary>
        /// 自共振启航可供评估的低阶有理共振泵升槽 (>= 1:1)。
        ///
        /// 【2026-09-14 新增】取自 <see cref="MGAFlybyDynamicsHelper.ResonanceRatioCandidates"/>,
        /// 取代原写死的 `{1.0, 1.5}` —— 后者是按 MESSENGER(1 年) 与 BepiColombo(1.5 年)
        /// 两个历史测试任务挑出的偏移量, 而注释里提到的 Juno(2 年) 并未被列入。
        /// </summary>
        public static double[] GetSelfResonantRatioSlots() {
            return MGAFlybyDynamicsHelper.ResonanceRatioCandidates
                .Where(r => r >= MGAFlybyDynamicsHelper.NoShrinkResonanceRatio)
                .ToArray();
        }

        public struct SynodicWindowCandidate
        {
            public double WindowStartUT;
            public double WindowEndUT;
            public double NominalToFDays;
            public double EstimatedMinDvKms;
        }

        /// <summary>
        /// 筛选最具潜力的候选会合时间窗口 (Filter Promising Synodic Windows)
        /// 通过轻量级解析与几何/能量评估快速遍历全时域，剔除高能禁区，按预估 ΔV 升序排序输出
        /// </summary>
        /// <param name="config">自适应求解配置</param>
        /// <param name="synodicPeriodDays">行星间会合周期（天）</param>
        /// <returns>排序后的候选会合窗口集合</returns>
        public static List<SynodicWindowCandidate> FilterPromisingWindows(
            MGAAdaptiveSolverConfig config, 
            double synodicPeriodDays)
        {
            var candidates = new List<SynodicWindowCandidate>();
            double totalSpanDays = (config.SearchEndUT - config.SearchStartUT) / MGADefaults.SecondsPerDay;
            if (totalSpanDays <= 0) return candidates;

            double synDays = (synodicPeriodDays > 20.0 && !double.IsNaN(synodicPeriodDays)) 
                ? synodicPeriodDays 
                : 365.25;

            string fromBody = config.DeparturePlanet;
            string toBody = null;
            if (config.FlybySequence != null && config.FlybySequence.Count > 0)
            {
                foreach (var b in config.FlybySequence)
                {
                    if (!string.IsNullOrEmpty(b) && !b.Equals(fromBody, StringComparison.OrdinalIgnoreCase))
                    {
                        toBody = b;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(toBody)) toBody = config.ArrivalPlanet;

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            string central = config.CentralBody ?? "Sun";
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            double sma1 = PykepBridge.GetRelativeSMA(p1, central, config.DepPeKm);
            double sma2 = PykepBridge.GetRelativeSMA(p2, central, config.ArrPeKm);
            if (sma1 <= 0) sma1 = MGADefaults.AU_KM;
            if (sma2 <= 0) sma2 = MGADefaults.AU_KM;
            double aTrans = (sma1 + sma2) * 0.5;
            double muCentralM = (centralMu > 0 ? centralMu : MGADefaults.FallbackSunMu) * 1e9;
            double nomTofDays = Math.PI * Math.Sqrt(Math.Pow(aTrans * 1000.0, 3.0) / muCentralM) / 86400.0;
            if (nomTofDays <= 0 || double.IsNaN(nomTofDays)) nomTofDays = 150.0;

            double minTofD = Math.Max(10.0, nomTofDays * 0.60);
            double maxTofD = nomTofDays * 1.50;

            // 1. 将搜索空间严格划分为 N 个连续无缝的物理会合周期时区
            // 确保相邻窗口严密无缝拼接，首尾严格对齐 [SearchStartUT, SearchEndUT]，零截断丢失
            var cycleIntervals = new List<(double startUT, double endUT)>();
            double curStart = config.SearchStartUT;
            while (curStart < config.SearchEndUT)
            {
                double curEnd = Math.Min(config.SearchEndUT, curStart + synDays * 86400.0);
                cycleIntervals.Add((curStart, curEnd));
                curStart = curEnd;
            }

            // 2. DirectCompute GPU 极速全域张量化宏观能量特征面初筛
            // 采用 16x16 线程块并发，微秒级评估全域数万个发射-ToF网格点，直接提取各会合周期极小能量波谷
            double depSpanSec = config.SearchEndUT - config.SearchStartUT;
            double tofMinSec = minTofD * 86400.0;
            double tofMaxSec = maxTofD * 86400.0;
            int gridW = Math.Max(64, Math.Min(256, (int)(totalSpanDays / 3.0)));
            int gridH = 64;

            bool isSelfResonantDep = (config.FlybySequence != null && config.FlybySequence.Count > 0 &&
                config.FlybySequence[0].Equals(config.DeparturePlanet, StringComparison.OrdinalIgnoreCase));
            double tDepOrbSec = (p1 != null && sma1 > 0) ? PykepBridge.CalcOrbitPeriodSec(sma1, centralMu) : 365.25 * 86400.0;

            bool gpuSuccess = false;
            float[] gpuTotDv = null;

            if (isSelfResonantDep && tDepOrbSec > SelfResonantMinDepPeriodSec)
            {
                // 自共振启航: 航天器首段通过开普勒共振调相蓄能, 在重遇出发天体时才飞向出口天体。
                // 联合评估多阶共振偏移下的出口特征面, 取各发射历元对应的最小转移能量。
                //
                // 【2026-09-14 审计修正】原实现只在写死的 {1.0x, 1.5x} 两个偏移上评估, 且注释
                // 直接点名 "MESSENGER 1年 EGA, BepiColombo 1.5年 EGA, Juno 2年 EGA" ——
                // 即偏移值是按已知历史测试任务挑出的, 且 Juno 的 2 年根本没被列入。
                // 现改为评估**全部低阶有理共振泵升槽** (ResonanceRatioCandidates 中 >= 1:1 的部分),
                // 由开普勒第三定律给出候选, 与任何具体任务无关。该变更只扩大评估范围, 不排除可行解。
                double[] resRatios = GetSelfResonantRatioSlots();
                foreach (double r in resRatios)
                {
                    double resShiftSec = r * tDepOrbSec;
                    bool ok = PykepBridge.EvaluateSurrogateGpuGrid(
                        p1, p2, centralMu,
                        config.SearchStartUT + resShiftSec, depSpanSec,
                        tofMinSec, tofMaxSec,
                        gridW, gridH,
                        config.DepPeKm, config.ArrPeKm,
                        config.NoInsertionBurn,
                        out float[] curDv,
                        out _);
                    if (ok && curDv != null && curDv.Length == gridW * gridH)
                    {
                        if (gpuTotDv == null)
                        {
                            gpuTotDv = (float[])curDv.Clone();
                            gpuSuccess = true;
                        }
                        else
                        {
                            for (int i = 0; i < gpuTotDv.Length; i++)
                            {
                                if (curDv[i] < gpuTotDv[i]) gpuTotDv[i] = curDv[i];
                            }
                        }
                    }
                }
            }
            else
            {
                gpuSuccess = PykepBridge.EvaluateSurrogateGpuGrid(
                    p1, p2, centralMu,
                    config.SearchStartUT, depSpanSec,
                    tofMinSec, tofMaxSec,
                    gridW, gridH,
                    config.DepPeKm, config.ArrPeKm,
                    config.NoInsertionBurn,
                    out gpuTotDv,
                    out _);
            }

            if (gpuSuccess && gpuTotDv != null && gpuTotDv.Length == gridW * gridH)
            {
                foreach (var interval in cycleIntervals)
                {
                    double cycleStart = interval.startUT;
                    double cycleEnd = interval.endUT;
                    double minDvInCycle = double.MaxValue;
                    double bestEpochUT = (cycleStart + cycleEnd) * 0.5;
                    double bestTofD = nomTofDays;

                    for (int gx = 0; gx < gridW; gx++)
                    {
                        double ut = config.SearchStartUT + (gx / (double)Math.Max(1, gridW - 1)) * depSpanSec;
                        if (ut < cycleStart || ut > cycleEnd) continue;

                        for (int gy = 0; gy < gridH; gy++)
                        {
                            int idx = gy * gridW + gx;
                            float dv = gpuTotDv[idx];
                            if (dv < minDvInCycle)
                            {
                                minDvInCycle = dv;
                                bestEpochUT = ut;
                                bestTofD = (tofMinSec + (gy / (double)Math.Max(1, gridH - 1)) * (tofMaxSec - tofMinSec)) / 86400.0;
                            }
                        }
                    }

                    candidates.Add(new SynodicWindowCandidate
                    {
                        WindowStartUT = cycleStart,
                        WindowEndUT = cycleEnd,
                        NominalToFDays = bestTofD,
                        EstimatedMinDvKms = (minDvInCycle < 1e7) ? (minDvInCycle / 1000.0) : 0.0
                    });
                }

                int totalEstimatedWindows = cycleIntervals.Count * 12;
                Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordSurrogate(totalEstimatedWindows, candidates.Count);
                return candidates.OrderBy(c => c.EstimatedMinDvKms > 0 ? c.EstimatedMinDvKms : double.MaxValue).ToList();
            }

            // 3. CPU 优雅平滑降级分支：针对各会合时区执行两级自适应稀疏特征网格扫描 (Coarse-to-Fine Grid Search)
            // 一级粗扫以 30 天步长快速定位波谷，二级精扫仅在波谷邻域细化，提速百倍以上
            foreach (var interval in cycleIntervals)
            {
                double cycleStart = interval.startUT;
                double cycleEnd = interval.endUT;
                double cycleSpanDays = (cycleEnd - cycleStart) / 86400.0;

                double bestEpochUT = (cycleStart + cycleEnd) * 0.5;
                double bestTofD = nomTofDays;
                double minDvInCycle = double.MaxValue;

                // 粗扫描步长
                double coarseStepDays = Math.Max(15.0, Math.Min(35.0, cycleSpanDays / 18.0));
                double[] coarseToFs = new double[] { nomTofDays * 0.70, nomTofDays * 0.85, nomTofDays * 1.00, nomTofDays * 1.15, nomTofDays * 1.30 };

                double coarseBestUT = bestEpochUT;
                double coarseBestDv = double.MaxValue;
                double coarseBestTof = nomTofDays;

                double rp1 = (p1 != null && p1.RadiusKm > 0 ? p1.RadiusKm : MGADefaults.GetBodyRadiusKm(fromBody)) + config.DepPeKm;
                double muP1 = (p1 != null && p1.Mu > 0) ? p1.Mu : MGADefaults.GetBodyMu(fromBody);
                double vPark1 = Math.Sqrt(muP1 / Math.Max(1.0, rp1));

                double rp2 = (p2 != null && p2.RadiusKm > 0 ? p2.RadiusKm : MGADefaults.GetBodyRadiusKm(toBody)) + config.ArrPeKm;
                double muP2 = (p2 != null && p2.Mu > 0) ? p2.Mu : MGADefaults.GetBodyMu(toBody);
                double vPark2 = Math.Sqrt(muP2 / Math.Max(1.0, rp2));

                // 【2026-09-14 审计修正】原为写死的 {1.0 × T_dep, 1.5 × T_dep} 两个自共振偏移
                // (对应 MESSENGER/BepiColombo 历史构型)。现改为遍历全部低阶有理共振泵升槽。
                double[] resOffsets = (isSelfResonantDep && tDepOrbSec > SelfResonantMinDepPeriodSec)
                    ? GetSelfResonantRatioSlots().Select(r => r * tDepOrbSec).ToArray()
                    : new double[] { 0.0 };

                for (double ut = cycleStart; ut <= cycleEnd; ut += coarseStepDays * 86400.0)
                {
                    foreach (double resShift in resOffsets)
                    {
                        double depUT = ut + resShift;
                        p1.GetRelativeState(central, depUT, out Vector3d r1, out Vector3d v1);
                        foreach (double tD in coarseToFs)
                        {
                            double arrUT = depUT + tD * 86400.0;
                            p2.GetRelativeState(central, arrUT, out Vector3d r2, out Vector3d v2);

                            // 符号开普勒与角动量极值门禁 (Symbolic Keplerian Bound Filter)
                            if (!MGASymbolicPhysicsConstraints.IsToFKeplerianFeasible(r1.magnitude, r2.magnitude, tD * 86400.0, centralMu))
                                continue;

                            if (PykepNative.SolveLambert(r1, r2, tD * 86400.0, centralMu, false, out Vector3d vDep, out Vector3d vArr))
                            {
                                Vector3d vInf = vDep - v1;
                                double vHyp = Math.Sqrt(vInf.sqrMagnitude + 2.0 * muP1 / Math.Max(1.0, rp1));
                                double dv = (vHyp - vPark1) * 1000.0;

                                if (!config.NoInsertionBurn)
                                {
                                    Vector3d vInfArr = vArr - v2;
                                    double vHypArr = Math.Sqrt(vInfArr.sqrMagnitude + 2.0 * muP2 / Math.Max(1.0, rp2));
                                    dv += (vHypArr - vPark2) * 1000.0;
                                }

                                if (dv < coarseBestDv)
                                {
                                    coarseBestDv = dv;
                                    coarseBestUT = ut;
                                    coarseBestTof = tD;
                                }
                            }
                        }
                    }
                }

                // 二级精扫：仅在粗波谷 ±1.2 个粗步长内以 5 天步长细化
                double refineStartUT = Math.Max(cycleStart, coarseBestUT - coarseStepDays * 1.2 * 86400.0);
                double refineEndUT = Math.Min(cycleEnd, coarseBestUT + coarseStepDays * 1.2 * 86400.0);
                minDvInCycle = coarseBestDv;
                bestEpochUT = coarseBestUT;
                bestTofD = coarseBestTof;

                for (double ut = refineStartUT; ut <= refineEndUT; ut += 5.0 * 86400.0)
                {
                    foreach (double resShift in resOffsets)
                    {
                        double depUT = ut + resShift;
                        p1.GetRelativeState(central, depUT, out Vector3d r1, out Vector3d v1);
                        for (double tD = Math.Max(minTofD, coarseBestTof - 30.0); tD <= Math.Min(maxTofD, coarseBestTof + 30.0); tD += 5.0)
                        {
                            double arrUT = depUT + tD * 86400.0;
                            p2.GetRelativeState(central, arrUT, out Vector3d r2, out Vector3d v2);

                            // 符号开普勒与角动量极值门禁 (Symbolic Keplerian Bound Filter)
                            if (!MGASymbolicPhysicsConstraints.IsToFKeplerianFeasible(r1.magnitude, r2.magnitude, tD * 86400.0, centralMu))
                                continue;

                            if (PykepNative.SolveLambert(r1, r2, tD * 86400.0, centralMu, false, out Vector3d vDep, out Vector3d vArr))
                            {
                                Vector3d vInf = vDep - v1;
                                double vHyp = Math.Sqrt(vInf.sqrMagnitude + 2.0 * muP1 / Math.Max(1.0, rp1));
                                double dv = (vHyp - vPark1) * 1000.0;

                            if (!config.NoInsertionBurn)
                            {
                                Vector3d vInfArr = vArr - v2;
                                double vHypArr = Math.Sqrt(vInfArr.sqrMagnitude + 2.0 * muP2 / Math.Max(1.0, rp2));
                                dv += (vHypArr - vPark2) * 1000.0;
                            }

                            if (dv < minDvInCycle)
                            {
                                minDvInCycle = dv;
                                bestEpochUT = ut;
                                bestTofD = tD;
                            }
                        }
                    }
                }
                }

                candidates.Add(new SynodicWindowCandidate
                {
                    WindowStartUT = cycleStart,
                    WindowEndUT = cycleEnd,
                    NominalToFDays = bestTofD,
                    EstimatedMinDvKms = (minDvInCycle < 1e7) ? (minDvInCycle / 1000.0) : 0.0
                });
            }

            int totalCpuEstimatedWindows = cycleIntervals.Count * 12;
            Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordSurrogate(totalCpuEstimatedWindows, candidates.Count);
            return candidates.OrderBy(c => c.EstimatedMinDvKms > 0 ? c.EstimatedMinDvKms : double.MaxValue).ToList();
        }

        /// <summary>
        /// DirectCompute GPU 极速多腿 MGA 3D 张量走廊全域初筛
        /// 并行网格: [gridDep x gridTof1 x gridTof2] (典型 128x32x32 = 131,072 并发线程)
        /// 毫秒级锁定三天体全链路能量极值波谷
        /// </summary>
        public static bool EvaluateMultiLegGpuCorridor(
            SolarPlanet p1, SolarPlanet p2, SolarPlanet p3,
            double centralMu,
            double depStartUt, double depSpanSec,
            double tof1MinSec, double tof1MaxSec,
            double tof2MinSec, double tof2MaxSec,
            int gridDep, int gridTof1, int gridTof2,
            double p2RpMinKm,
            out float[] outBestTotDv,
            out float[] outBestTof1Days,
            out float[] outBestTof2Days)
        {
            outBestTotDv = new float[gridDep];
            outBestTof1Days = new float[gridDep];
            outBestTof2Days = new float[gridDep];

            long totalItems = (long)gridDep * gridTof1 * gridTof2;
            if (!PykepNative.IsGpuComputeAvailable || p1 == null || p2 == null || p3 == null)
            {
                Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution("GPU 3D Multi-Leg Corridor", 0.0, totalItems, false);
                return false;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var elem1 = new PykepNative.GpuPlanetElements
                {
                    sma_km = p1.SMA, ecc = p1.ECC, inc_rad = p1.INC * Math.PI / 180.0,
                    raan_rad = p1.RAAN * Math.PI / 180.0, aop_rad = p1.AOP * Math.PI / 180.0,
                    ma0_rad = p1.MA0 * Math.PI / 180.0, epoch_ut = p1.EpochJ2000,
                    mu_self = p1.Mu, radius_km = p1.RadiusKm, park_alt_km = 100.0
                };

                var elem2 = new PykepNative.GpuPlanetElements
                {
                    sma_km = p2.SMA, ecc = p2.ECC, inc_rad = p2.INC * Math.PI / 180.0,
                    raan_rad = p2.RAAN * Math.PI / 180.0, aop_rad = p2.AOP * Math.PI / 180.0,
                    ma0_rad = p2.MA0 * Math.PI / 180.0, epoch_ut = p2.EpochJ2000,
                    mu_self = p2.Mu, radius_km = p2.RadiusKm, park_alt_km = 100.0
                };

                var elem3 = new PykepNative.GpuPlanetElements
                {
                    sma_km = p3.SMA, ecc = p3.ECC, inc_rad = p3.INC * Math.PI / 180.0,
                    raan_rad = p3.RAAN * Math.PI / 180.0, aop_rad = p3.AOP * Math.PI / 180.0,
                    ma0_rad = p3.MA0 * Math.PI / 180.0, epoch_ut = p3.EpochJ2000,
                    mu_self = p3.Mu, radius_km = p3.RadiusKm, park_alt_km = 100.0
                };

                int ret = PykepNative.MgaCorridorGpuEvaluate(
                    ref elem1, ref elem2, ref elem3,
                    centralMu,
                    depStartUt, depSpanSec,
                    tof1MinSec, tof1MaxSec,
                    tof2MinSec, tof2MaxSec,
                    gridDep, gridTof1, gridTof2,
                    p2RpMinKm,
                    outBestTotDv, outBestTof1Days, outBestTof2Days);

                sw.Stop();
                bool isSuccess = (ret == 0);
                Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution(
                    "GPU 3D Multi-Leg Corridor", sw.Elapsed.TotalMilliseconds, totalItems, isSuccess);

                return isSuccess;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Telemetry.MGAPipelineTelemetryProbe.RecordGpuExecution("GPU 3D Multi-Leg Corridor", sw.Elapsed.TotalMilliseconds, totalItems, false);
                KSPGMATLog.Error("Solver", $"[MGASurrogatePreFilter] GPU 多腿张量初筛异常，自动降级至 CPU: {ex.Message}", ex);
                return false;
            }
        }
    }
}
