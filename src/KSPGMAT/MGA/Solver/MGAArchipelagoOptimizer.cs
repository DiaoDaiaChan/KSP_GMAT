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

            // 落地 PaGMO (Izzo 2010) 多保真度漏斗思想：按估算发射能量排序，聚焦最具潜力的前 K 个黄金窗口
            // 基础 2 个窗口；若任务覆盖时段超过 2 个会合周期且未显式指定，动态提升至 3-4 个窗口，兼顾长周期任务解完整性
            int maxEvalWindows = (config != null && config.MaxWindows > 0) ? config.MaxWindows : 2;
            if (config != null && config.MaxWindows <= 0 && windows.Count > 2)
            {
                var depP = SolarSystemCatalog.GetPlanet(config.DeparturePlanet);
                var arrP = SolarSystemCatalog.GetPlanet(config.ArrivalPlanet);
                if (depP != null && arrP != null)
                {
                    double sma1 = PykepBridge.GetRelativeSMA(depP, config.CentralBody, 0.0);
                    double sma2 = PykepBridge.GetRelativeSMA(arrP, config.CentralBody, 0.0);
                    double muSun = SolarSystemCatalog.GetCentralMu(config.CentralBody ?? "Sun");
                    double t1 = PykepBridge.CalcOrbitPeriodSec(sma1, muSun);
                    double t2 = PykepBridge.CalcOrbitPeriodSec(sma2, muSun);
                    if (t1 > 0 && t2 > 0 && Math.Abs(t1 - t2) > 1.0)
                    {
                        double tSynSec = Math.Abs(t1 * t2 / (t1 - t2));
                        double searchSpanSec = config.SearchEndUT - config.SearchStartUT;
                        if (searchSpanSec > 2.5 * tSynSec)
                        {
                            maxEvalWindows = Math.Min(4, windows.Count);
                        }
                    }
                }
            }
            // 确定性次级键: 窗口候选集由并行粗筛产生, 估算能量并列时选取哪 K 个窗口
            // 会随线程时序变化 —— 这是影响最大的一处不可复现点 (决定进入 DE 的搜索区间)。
            var prioritizedWindows = windows
                .OrderBy(w => w.EstimatedMinDvKms)
                .ThenBy(w => Math.Round(w.WindowStartUT * 1000.0))
                .ThenBy(w => Math.Round(w.WindowEndUT * 1000.0))
                .Take(maxEvalWindows).ToList();
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
            int totalBudget = explicitParallelBudget ? Math.Max(1, maxWindowParallelism) : Environment.ProcessorCount;
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
                    LambertMaxRevs = subCfg.LambertMaxRevs,
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
                    Console.WriteLine($"     [岛屿 {wIdx + 1,2}/{totalWindows}] 历元 {GMATClient.FormatUtcGregorian(w.WindowStartUT)}: 最佳总ΔV = {b.TotalDeltaVKms * 1000.0,7:F0} m/s (始发={b.LaunchDeltaVKms * 1000.0,6:F0} m/s, DSM={b.TotalDsmDeltaVKms * 1000.0,7:F0} m/s)");
                    foreach (var s in sols)
                    {
                        if (s != null && !double.IsNaN(s.TotalDeltaVKms) && s.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) // 过滤 >50 km/s 的动力学发散解
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

            return allSolutions.Deterministic().ToList();
        }
    }
}
