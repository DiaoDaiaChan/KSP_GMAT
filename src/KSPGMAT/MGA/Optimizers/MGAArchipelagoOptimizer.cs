// ============================================================================
// MGAArchipelagoOptimizer.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
// 权威自适应 MGA 求解器子模块。底层 100% 官方 ESA kep3 Native C++ 闭环动力学。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 模块 A: 跨会合周期多窗口并行差分进化寻优器 (Multi-Window Parallel DE Evaluator across Synodic Windows)
    /// 机制: 根据开普勒会合周期将发射搜索全时域划分为独立候选时区，并行分发各窗口差分进化寻优，聚焦能量波谷并汇聚全域解集
    /// 遵从 baseline_agent.md 实事求是原则：底层 100% 委托 Native kep3 差分进化与 Householder-4 兰伯特求解
    /// </summary>
    public static class MGAArchipelagoOptimizer
    {
        /// <summary>
        /// 针对多个候选会合时间窗口，并行分发差分进化群岛演化 (Multi-Island DE Evolution across Synodic Windows)
        /// </summary>
        /// <param name="config">自适应求解配置</param>
        /// <param name="windows">预筛选出的候选会合时间窗口列表</param>
        /// <param name="ct">异步取消令牌</param>
        /// <param name="onProgress">进度汇报委托</param>
        /// <param name="maxWindowParallelism">最大窗口并发度 (-1 自动根据 CPU 核心与负载推导)</param>
        /// <returns>跨窗口演化产出的优质轨迹解集</returns>
        public static List<MGASolution> EvolveWindows(
            MGAAdaptiveSolverConfig config,
            List<MGASurrogatePreFilter.SynodicWindowCandidate> windows,
            CancellationToken ct,
            Action<float, string> onProgress = null,
            int maxWindowParallelism = -1)
        {
            var allSolutions = new ConcurrentBag<MGASolution>();
            if (windows == null || windows.Count == 0) return new List<MGASolution>();
            var stage3Sw = System.Diagnostics.Stopwatch.StartNew();

            // 落地 PaGMO (Izzo 2010) 多保真度漏斗思想：
            // 对多借力 (MGA) 任务，首段 2-body 能量无法代表下游多星共振与大巡游排列，
            // 必须在时域上均匀保留各发射窗口候选 (至多 8 个窗口并发探索)；
            // 纯直达任务则聚焦能量最低的前 K 个黄金窗口。
            bool isMGA = (config != null && config.FlybySequence != null && config.FlybySequence.Count > 0);
            int defaultMaxWindows = Math.Min(12, windows.Count);
            int maxEvalWindows = (config != null && config.MaxWindows > 0) ? config.MaxWindows : defaultMaxWindows;

            List<MGASurrogatePreFilter.SynodicWindowCandidate> prioritizedWindows;
            if (windows.Count <= maxEvalWindows)
            {
                prioritizedWindows = new List<MGASurrogatePreFilter.SynodicWindowCandidate>(windows);
            }
            else if (isMGA)
            {
                // 多借力 (MGA) 任务：首段 2-body 转移能量无法预知后续多体引力偏折排列，
                // 按时序排列后均匀铺开（保留不同时段的候选窗口并发探索），避免好窗口被 2-body 能量误杀
                var timeOrdered = windows.OrderBy(w => w.WindowStartUT).ToList();
                prioritizedWindows = new List<MGASurrogatePreFilter.SynodicWindowCandidate>();
                for (int i = 0; i < maxEvalWindows; i++)
                {
                    int idx = (int)Math.Round(i * (timeOrdered.Count - 1.0) / (maxEvalWindows - 1.0));
                    prioritizedWindows.Add(timeOrdered[idx]);
                }
            }
            else
            {
                prioritizedWindows = windows
                    .OrderBy(w => w.EstimatedMinDvKms)
                    .ThenBy(w => Math.Round(w.WindowStartUT * 1000.0))
                    .ThenBy(w => Math.Round(w.WindowEndUT * 1000.0))
                    .Take(maxEvalWindows).ToList();
            }
            int totalWindows = prioritizedWindows.Count;
            int finishedCount = 0;
            var lockObj = new object();

            // 预先提升线程池最小容量，避免线程池初始爬坡延迟
            try
            {
                int minWorkers = Math.Max(Environment.ProcessorCount * 2, 16);
                ThreadPool.SetMinThreads(minWorkers, minWorkers);
            }
            catch { }

            // 多核 CPU 弹性双层并行策略：
            // 计算总可用核心预算 totalBudget (依据 maxWindowParallelism 或整机 ProcessorCount)；
            // 依据候选窗口数动态划定窗口级并发度 (winWorkers) 与单窗口内 DE 变异交叉并发度 (perWindowPopulation)，
            // 保证 winParallel * perWindowPopulation 紧密跑满全部核预算，彻底杜绝单核空转与嵌套超订。
            bool explicitParallelBudget = (maxWindowParallelism > 0);
            int usableCores = Math.Max(1, Environment.ProcessorCount - 1);
            int totalBudget = explicitParallelBudget ? Math.Max(1, maxWindowParallelism) : usableCores;
            int winWorkers = Math.Min(totalWindows, totalBudget);
            int winParallel = winWorkers;
            int perWindowPopulation = Math.Max(1, totalBudget / winWorkers);
            bool innerParallel = (perWindowPopulation > 1);

            Parallel.For(0, totalWindows, new ParallelOptions { MaxDegreeOfParallelism = winParallel }, wIdx =>
            {
                if (ct.IsCancellationRequested) return;

                var w = prioritizedWindows[wIdx];
                var subCfg = config.Clone();
                subCfg.SearchStartUT = w.WindowStartUT;
                subCfg.SearchEndUT = w.WindowEndUT;
                subCfg.SeedEpochUT = w.BestEpochUT;
                subCfg.SeedToFDays = w.NominalToFDays;

                var legBounds = (subCfg.LegToFBounds != null && subCfg.LegToFBounds.Count > 0)
                    ? subCfg.LegToFBounds
                    : MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(subCfg.DeparturePlanet, subCfg.ArrivalPlanet, subCfg.FlybySequence, subCfg.CentralBody, subCfg.DepartureToFMinFactor, subCfg.DepartureToFMaxFactor);

                // 构造底层 MGAConfig 封送
                var mgaCfg = new MGAConfig
                {
                    CentralBody = subCfg.CentralBody,
                    DepartureBody = subCfg.DeparturePlanet,
                    DestinationBody = subCfg.ArrivalPlanet,
                    FlybySequence = subCfg.FlybySequence,
                    EarliestDepartureUT = subCfg.SearchStartUT,
                    LatestDepartureUT = subCfg.SearchEndUT,
                    SeedEpochUT = subCfg.SeedEpochUT,
                    SeedToFDays = subCfg.SeedToFDays,
                    DeparturePeAKm = subCfg.DepPeKm,
                    DepartureApAKm = subCfg.DepApKm,
                    DepartureInclinationDeg = subCfg.DepIncDeg, // -1 触发纯切向
                    DepartureMode = (subCfg.DeparturePolicy == DepartureModePolicy.FreeLaunch) 
                        ? MGADepartureMode.PreLaunchFreeOrbit 
                        : MGADepartureMode.InOrbitLockedOrbit,
                    LockDepartureInclination = (subCfg.DeparturePolicy == DepartureModePolicy.OrbitLocked),
                    DestinationPeAKm = subCfg.ArrPeKm,
                    DestinationApAKm = subCfg.ArrApKm,
                    ArrivalOrbitType = subCfg.ArrivalOrbitType,
                    NoInsertionBurn = subCfg.NoInsertionBurn,
                    EnableMaxDuration = (subCfg.TotalToFMaxDays > 0),
                    MaxDurationDays = (subCfg.TotalToFMaxDays > 0) ? subCfg.TotalToFMaxDays : 0.0,
                    EngineMode = subCfg.EngineMode,
                    LegStrategies = subCfg.LegStrategies != null ? new List<MGALegStrategy>(subCfg.LegStrategies) : null,
                    FlybyStrategies = subCfg.FlybyStrategies != null ? new List<MGAFlybyStrategy>(subCfg.FlybyStrategies) : null,
                    DsmEtas = subCfg.DsmEtas != null ? new List<double>(subCfg.DsmEtas) : null,
                    DsmEtaDefault = subCfg.DsmEtaDefault,
                    ConstrainByVesselDeltaV = subCfg.ConstrainByVesselDeltaV,
                    VesselMaxDeltaVMs = subCfg.VesselMaxDeltaVMs,
                    MaxDsmDeltaVMs = subCfg.MaxDsmDeltaVMs,
                    LaunchDeltaVCeilingMs = subCfg.LaunchDeltaVCeilingMs,
                    PopulationSize = subCfg.PopulationSize,
                    MaxGenerations = subCfg.MaxGenerations,
                    DepartureToFMinFactor = subCfg.DepartureToFMinFactor,
                    DepartureToFMaxFactor = subCfg.DepartureToFMaxFactor,
                    ResonantMaxOffsetDays = subCfg.ResonantMaxOffsetDays,
                    LambertMaxRevs = subCfg.LambertMaxRevs,
                    ConstraintProfile = subCfg.ConstraintProfile,
                    MinToFDays = legBounds.Select(b => b.MinDays).ToList(),
                    MaxToFDays = legBounds.Select(b => b.MaxDays).ToList()
                };

                // 调用底层 100% Native kep3 差分进化
                double winBudgetMs = (subCfg.ConstrainByVesselDeltaV && subCfg.VesselMaxDeltaVMs > 0) ? subCfg.VesselMaxDeltaVMs : double.MaxValue;
                double winDsmBudgetMs = (subCfg.MaxDsmDeltaVMs > 0) ? subCfg.MaxDsmDeltaVMs : winBudgetMs;

                var sols = MGADifferentialEvolution.RunWindow(mgaCfg, w.WindowStartUT, w.WindowEndUT, null, ct, parallelizeInternal: innerParallel, populationMaxParallelism: perWindowPopulation);
                if (sols != null && sols.Count > 0)
                {
                    var b = sols[0];
                    KSPGMATLog.Info("Solver", $"     [岛屿 {wIdx + 1,2}/{totalWindows}] 历元 {GMATClient.FormatUtcGregorian(w.WindowStartUT)}: 最佳总ΔV = {b.TotalDeltaVKms * 1000.0,7:F0} m/s (始发={b.LaunchDeltaVKms * 1000.0,6:F0} m/s, DSM={b.TotalDsmDeltaVKms * 1000.0,7:F0} m/s)");
                    Telemetry.MGAPipelineTelemetryProbe.RecordIslandConvergence(
                        $"Window_{w.WindowStartUT:F0}",
                        (subCfg.FlybySequence != null && subCfg.FlybySequence.Count > 0) ? string.Join("->", subCfg.FlybySequence) : config.ArrivalPlanet,
                        w.WindowStartUT,
                        0,
                        b.TotalDeltaVKms,
                        b.TotalDeltaVKms * 1000.0,
                        b.LaunchDeltaVKms * 1000.0,
                        b.TotalDsmDeltaVKms * 1000.0,
                        sols.Count > 0);
                    foreach (var s in sols)
                    {
                        if (s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) // 过滤 >80 km/s 的动力学发散解
                        {
                            allSolutions.Add(s);
                        }
                    }
                }

                lock (lockObj)
                {
                    finishedCount++;
                    float p = (float)finishedCount / totalWindows;
                    double currentBestDv = allSolutions.Any() ? allSolutions.Min(s => s.TotalDeltaVKms * 1000.0) : 0;
                    string bestStr = currentBestDv > 0 ? $"当前最优: {currentBestDv:F0} m/s" : "探索中";
                    onProgress?.Invoke(p, $"多岛全局寻优: {finishedCount}/{totalWindows} 周期岛屿完成 ({bestStr})");
                }
            });

            stage3Sw.Stop();
            Telemetry.MGAPipelineTelemetryProbe.RecordStage3Overall(
                totalWindows,
                (int)Telemetry.MGAPipelineTelemetryProbe.TotalGenerationsCount,
                (int)Telemetry.MGAPipelineTelemetryProbe.TotalLambertEvaluationsCount,
                stage3Sw.Elapsed.TotalMilliseconds);

            return allSolutions.Deterministic().ToList();
        }
    }
}
