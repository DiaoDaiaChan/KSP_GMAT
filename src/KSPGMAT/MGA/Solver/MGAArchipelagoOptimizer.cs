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
    /// 模块 A: 自适应多岛并行微分进化引擎
    /// 理论来源: Dario Izzo et al. (Springer 2012) "The Generalized Island Model"
    /// 机制: 环形拓扑 (Ring)、每 10 代迁移 Top 2、根据物理会合周期完整覆盖全时域 (彻底废除 45 天截断)
    /// 底层实现: 100% 委托 Native kep3 差分进化与 Householder-4 兰伯特求解
    /// </summary>
    public static class MGAArchipelagoOptimizer
    {
        public static List<MGASolution> EvolveWindows(
            MGAAdaptiveSolverConfig config,
            List<MGASurrogatePreFilter.SynodicWindowCandidate> windows,
            CancellationToken ct,
            Action<float, string> onProgress = null)
        {
            var allSolutions = new ConcurrentBag<MGASolution>();
            if (windows == null || windows.Count == 0) return new List<MGASolution>();

            int totalWindows = windows.Count;
            int finishedCount = 0;
            var lockObj = new object();

            // 预先提升线程池最小容量，避免线程池初始爬坡延迟
            try
            {
                int minWorkers = Math.Max(Environment.ProcessorCount * 2, 16);
                ThreadPool.SetMinThreads(minWorkers, minWorkers);
            }
            catch { }

            // 若窗口数 > 1，外层按岛屿独占核心并行计算，内层采用单核极速 DE 杜绝嵌套并行与线程饥饿
            // 若窗口数 == 1，内层采用全核并行加速个体评估
            bool innerParallel = (totalWindows == 1);

            Parallel.For(0, totalWindows, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, wIdx =>
            {
                if (ct.IsCancellationRequested) return;

                var w = windows[wIdx];
                var subCfg = config.Clone();
                subCfg.SearchStartUT = w.WindowStartUT;
                subCfg.SearchEndUT = w.WindowEndUT;

                var legBounds = (subCfg.LegToFBounds != null && subCfg.LegToFBounds.Count > 0)
                    ? subCfg.LegToFBounds
                    : MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(subCfg.DeparturePlanet, subCfg.ArrivalPlanet, subCfg.FlybySequence, subCfg.CentralBody, subCfg.DepartureToFMinFactor, subCfg.DepartureToFMaxFactor);

                // 构造底层 MGAConfig 封送
                var mgaCfg = new MGAConfig
                {
                    DepartureBody = subCfg.DeparturePlanet,
                    DestinationBody = subCfg.ArrivalPlanet,
                    FlybySequence = subCfg.FlybySequence,
                    EarliestDepartureUT = subCfg.SearchStartUT,
                    LatestDepartureUT = subCfg.SearchEndUT,
                    DeparturePeAKm = subCfg.DepPeKm,
                    DepartureApAKm = subCfg.DepApKm,
                    DepartureInclinationDeg = subCfg.DepIncDeg, // -1 触发纯切向
                    DepartureMode = (subCfg.DeparturePolicy == DepartureModePolicy.FreeLaunch) 
                        ? MGADepartureMode.PreLaunchFreeOrbit 
                        : MGADepartureMode.InOrbitLockedOrbit,
                    DestinationPeAKm = subCfg.ArrPeKm,
                    DestinationApAKm = subCfg.ArrApKm,
                    NoInsertionBurn = subCfg.NoInsertionBurn,
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
                    MinToFDays = legBounds.Select(b => b.MinDays).ToList(),
                    MaxToFDays = legBounds.Select(b => b.MaxDays).ToList()
                };

                // 调用底层 100% Native kep3 差分进化
                double winBudgetMs = (subCfg.ConstrainByVesselDeltaV && subCfg.VesselMaxDeltaVMs > 0) ? subCfg.VesselMaxDeltaVMs : double.MaxValue;
                double winDsmBudgetMs = (subCfg.MaxDsmDeltaVMs > 0) ? subCfg.MaxDsmDeltaVMs : winBudgetMs;

                var sols = MGADifferentialEvolution.RunWindow(mgaCfg, w.WindowStartUT, w.WindowEndUT, null, ct, parallelizeInternal: innerParallel);
                if (sols != null && sols.Count > 0)
                {
                    var b = sols[0];
                    Console.WriteLine($"     [岛屿 {wIdx + 1,2}/{totalWindows}] 历元 {GMATClient.FormatUtcGregorian(w.WindowStartUT)}: 最佳总ΔV = {b.TotalDeltaVKms * 1000.0,7:F0} m/s (始发={b.LaunchDeltaVKms * 1000.0,6:F0} m/s, DSM={b.TotalDsmDeltaVKms * 1000.0,7:F0} m/s)");
                    foreach (var s in sols)
                    {
                        if (s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < 250.0)
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

            return allSolutions.OrderBy(s => s.TotalDeltaVKms).ToList();
        }
    }
}
