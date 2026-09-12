using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;
using UnityEngine;

namespace MGADebugRunner.Modules
{
    /// <summary>
    /// 链级真值标注模块 (审计报告 §18.3 / §17.4 的前置基础设施)。
    ///
    /// 对盲搜套件发现的**全部**候选链 (LastEvaluatedChains, 即 QD 选择前的完整评估池)
    /// 逐链执行与生产管线同源的 DE 精优化 (EnablePermutationsAndInsertions=false 单链模式),
    /// 产出 "QD 估计 vs DE 真值" 的标定数据集 (CSV)。
    ///
    /// 用途:
    ///   1. QD 质量分四系数 / 特征权重 / λ 的有监督标定 (取代手拍);
    ///   2. 星历可达性门禁重建的真值标签 (当前门禁在历史集上从未触发, §17.3);
    ///   3. QD 16 席的查全率评估: 池内未被选中的链里是否存在 DE 真值更优者。
    ///
    /// 真值语义: 与盲搜套件对 QD 选中链的评估**完全同源** ——
    ///   同一 DE 引擎、同一代数/种群、同一考核口径 (getSubjectDvMs), 因此
    ///   "DE 真值" 就是该链在本管线中的最终命运 (收敛与否、最终 ΔV)。
    ///
    /// 历史教训 (P4): 曾尝试粗 Lambert 网格做真值, 对 QD 选中且 DE 实测收敛的链
    /// 产生 ~60% 假阴性 —— 粗网格测的是 ToF 分配启发式而非链本身, 已弃用。
    /// </summary>
    public static class ChainTruthLabelingModule
    {
        public static void Run(string filter = null)
        {
            // 带过滤时写入独立文件, 避免误覆盖全量数据集 (曾因 --label dawn 覆盖 3463 行全量 CSV)
            string outPath = string.IsNullOrEmpty(filter)
                ? Path.Combine("reports", "test_reports", "chain_truth_labels.csv")
                : Path.Combine("reports", "test_reports", "chain_truth_labels_" + filter + ".csv");
            try { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); } catch { }

            var sb = new StringBuilder();
            sb.AppendLine("task,chain,nFlybys,resonanceCount,estFeasible,estDvMs,estDefMs,estTofDays,pareto,qualityScore,"
                        + "qdSelected,qdRank,deConverged,deTaskOk,deBestDvMs,deLaunchMs,deTofDays,deAllLegsFeasible,"
                        + "deUncDvMs,deUncTofDays,deElapsedMs");

            var cases = MGAHistoricalBlindSearchSuite.HistoricalCatalog;
            int totalRows = 0;
            foreach (var bCase in cases)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    bCase.MissionId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                double startUT = GMATClient.ParseUtcGregorian(bCase.HistoricalLaunchStr);
                double endUT = startUT + bCase.WindowSpanDays * 86400.0;

                var baseConfig = new MGAAdaptiveSolverConfig
                {
                    CentralBody = "Sun",
                    DeparturePlanet = bCase.DepartureBody,
                    ArrivalPlanet = bCase.DestinationBody,
                    FlybySequence = new List<string>(),
                    SearchStartUT = startUT,
                    SearchEndUT = endUT,
                    DeparturePolicy = DepartureModePolicy.FreeLaunch,
                    NoInsertionBurn = bCase.NoInsertionBurn,
                    ArrivalOrbitType = bCase.ArrivalOrbitType,
                    PropulsionType = bCase.PropulsionType,
                    AutoAdaptGiantPlanetEllipse = true,
                    ConstrainByVesselDeltaV = true,
                    VesselMaxDeltaVMs = bCase.VesselMaxDeltaVMs,
                    MaxDsmDeltaVMs = bCase.MaxDsmDeltaVMs,
                    LaunchDeltaVCeilingMs = bCase.LaunchCeilingMs,
                    EnablePermutationsAndInsertions = true,
                    EnableEphemerisReachabilityFilter = true,
                    MaxCandidateTopologies = bCase.MaxCandidateTopologies,
                    PopulationSize = bCase.PopulationSize,
                    MaxGenerations = bCase.MaxGenerations,
                    AutoTuneToFBounds = true,
                    TotalToFMaxDays = bCase.HistoricalFlightDays > 0 ? bCase.HistoricalFlightDays * 1.5 : -1.0
                };

                // 原始用户 ToF 上限 (盲搜语义: Solve 入口捕获 userSpecifiedMaxToF, 逐链调谐时优先于 dynMax)
                double userMaxOriginal = bCase.HistoricalFlightDays > 0 ? bCase.HistoricalFlightDays * 1.5 : -1.0;

                // ToF 上下界 (阶段1 用): 复刻 Solve 入口的【空序列】调谐 —— 盲搜在拓扑生成前完成这一步,
                // 拓扑管线 (MCTS/QD 估计) 看到的是调谐后的界。本模块不调 Solve(base), 故手动执行同款调谐。
                {
                    var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds(
                        baseConfig.DeparturePlanet, baseConfig.ArrivalPlanet, baseConfig.FlybySequence);
                    baseConfig.TotalToFMinDays = dynMin;
                    baseConfig.TotalToFMaxDays = (userMaxOriginal > 0) ? userMaxOriginal : dynMax;
                }

                // 阶段 1: 触发完整拓扑发现链 (MCTS + 图 + 星历过滤), 副作用是填充 LastEvaluatedChains
                var selected = MGAMCTSTopologySelector.GenerateOptimalFlybySequences(baseConfig, 16);
                var pool = MGAEphemerisReachabilityAnalyzer.LastEvaluatedChains
                         ?? new List<MGAEphemerisReachabilityAnalyzer.ChainReachabilityResult>();

                Console.WriteLine($"🏷️ [{bCase.MissionId}] 候选池 {pool.Count} 条, QD 选中 {selected.Count} 条");

                // QD 选中名次 (1-based; 空链的键为 "Direct" —— string.Join 对空链产出 "" 会导致查找失效)
                var qdRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selected.Count; i++)
                {
                    string key = (selected[i] != null && selected[i].Count > 0) ? string.Join("->", selected[i]) : "Direct";
                    qdRank[key] = i + 1;
                }

                // 考核口径: 与盲搜套件 getSubjectDvMs 完全一致
                Func<MGASolution, double> getSubjectDvMs = s => (bCase.NoInsertionBurn
                    ? (s.LaunchDeltaVKms + s.TotalDsmDeltaVKms + s.TotalFlybyDeltaVKms)
                    : s.TotalDeltaVKms) * 1000.0;

                var swTask = Stopwatch.StartNew();
                int rows = 0;
                foreach (var c in pool)
                {
                    string chainKey = (c.FlybyChain != null && c.FlybyChain.Count > 0) ? string.Join("->", c.FlybyChain) : "Direct";
                    double quality = MGAEphemerisReachabilityAnalyzer.ComputeQualityScore(c);

                    // 阶段 2: 单链 DE 精优化 —— 逐字复刻盲搜 Parallel.For 内的 subCfg 构造 (MGAAdaptiveSolver.cs:483-501):
                    //   Clone() 全字段 + 锁链 + 关排列/插入 + 清 Leg 策略 + AutoTuneToFBounds=true (逐链重调谐) +
                    //   TotalToFMaxDays 恢复原始用户值 (Solve 入口据此捕获 userSpecifiedMaxToF, 与盲搜语义一致)。
                    var chainConfig = CloneForSingleChain(baseConfig, c.FlybyChain, userMaxOriginal);
                    var sw = Stopwatch.StartNew();
                    List<MGASolution> solutions = null;
                    try { solutions = MGAAdaptiveSolver.Solve(chainConfig, default(System.Threading.CancellationToken), null); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠️ [{chainKey}] 求解异常: {ex.Message}");
                    }
                    sw.Stop();

                    bool converged = solutions != null && solutions.Count > 0;
                    // 任务硬约束 (与 Solve 全局池 line 726/728 同款判据, 但按链独立应用):
                    //   ToF 上限 (userMax*1.15) 与总工质预算 (vessel*1.05, 按 TotalDeltaV 口径) 是任务属性;
                    //   合并池的 12 解截断是全局资源竞争, 与链质量无关, 不复制。
                    //   (单链 Solve 的空池兜底会复活超限解 —— 曾致 Venus->Venus 的 551 天解虚假胜出, 已实测。)
                    List<MGASolution> constrained = null;
                    if (converged)
                    {
                        constrained = solutions.Where(s =>
                            (userMaxOriginal <= 0 || s.TotalFlightDays <= userMaxOriginal * 1.15) &&
                            (bCase.VesselMaxDeltaVMs <= 0 || s.TotalDeltaVKms * 1000.0 <= bCase.VesselMaxDeltaVMs * 1.05)).ToList();
                    }
                    bool taskOk = constrained != null && constrained.Count > 0;

                    double bestDv = double.NaN, tofDays = double.NaN, launchMs = double.NaN, uncDv = double.NaN, uncTof = double.NaN;
                    bool allFeas = false;
                    MGASolution best = null;
                    if (converged)
                    {
                        // 无约束链最优 (参考列: 该链 DE 能力的上界, 不受任务 ToF/工质约束)
                        var uncBest = solutions
                            .OrderBy(s => (bCase.LaunchCeilingMs > 0 && s.LaunchDeltaVKms * 1000.0 > bCase.LaunchCeilingMs * 1.05 ? 1e6 : 0.0)
                                        + (bCase.VesselMaxDeltaVMs > 0 && getSubjectDvMs(s) > bCase.VesselMaxDeltaVMs * 1.05 ? 1e6 : 0.0)
                                        + (!s.AllLegsFeasible ? 1e5 : 0.0)
                                        + getSubjectDvMs(s))
                            .First();
                        uncDv = getSubjectDvMs(uncBest);
                        uncTof = uncBest.TotalFlightDays;

                        // 任务约束内链最优 (真值主列, 与盲搜套件最终选解同序键)
                        if (taskOk)
                        {
                            best = constrained
                                .OrderBy(s => (bCase.LaunchCeilingMs > 0 && s.LaunchDeltaVKms * 1000.0 > bCase.LaunchCeilingMs * 1.05 ? 1e6 : 0.0)
                                            + (bCase.VesselMaxDeltaVMs > 0 && getSubjectDvMs(s) > bCase.VesselMaxDeltaVMs * 1.05 ? 1e6 : 0.0)
                                            + (!s.AllLegsFeasible ? 1e5 : 0.0)
                                            + getSubjectDvMs(s))
                                .First();
                        }
                    }
                    if (best != null)
                    {
                        bestDv = getSubjectDvMs(best);
                        launchMs = best.LaunchDeltaVKms * 1000.0;
                        tofDays = best.TotalFlightDays;
                        allFeas = best.AllLegsFeasible;
                    }

                    int rank;
                    bool qdSel = qdRank.TryGetValue(chainKey, out rank);
                    if (!qdSel) rank = 0;

                    sb.Append(bCase.MissionId).Append(',')
                      .Append('"').Append(chainKey).Append('"').Append(',')
                      .Append(c.FlybyChain != null ? c.FlybyChain.Count : 0).Append(',')
                      .Append(c.ResonanceCount).Append(',')
                      .Append(c.IsFeasible ? 1 : 0).Append(',')
                      .Append(F(c.EstimatedTotalDvMs)).Append(',')
                      .Append(F(c.TotalDeficitMs)).Append(',')
                      .Append(F(c.EstimatedToFDays)).Append(',')
                      .Append(F(c.ParetoScore)).Append(',')
                      .Append(F(quality)).Append(',')
                      .Append(qdSel ? 1 : 0).Append(',')
                      .Append(rank).Append(',')
                      .Append(converged ? 1 : 0).Append(',')
                      .Append(taskOk ? 1 : 0).Append(',')
                      .Append(F(bestDv)).Append(',')
                      .Append(F(launchMs)).Append(',')
                      .Append(F(tofDays)).Append(',')
                      .Append(allFeas ? 1 : 0).Append(',')
                      .Append(F(uncDv)).Append(',')
                      .Append(F(uncTof)).Append(',')
                      .Append(sw.ElapsedMilliseconds)
                      .AppendLine();
                    rows++;

                    if (rows % 20 == 0)
                        Console.WriteLine($"   ... {rows}/{pool.Count} ({swTask.Elapsed.TotalSeconds:F0}s)");
                }
                totalRows += rows;
                Console.WriteLine($"   -> [{bCase.MissionId}] 标注 {rows} 行, 耗时 {swTask.Elapsed.TotalSeconds:F0}s");
            }

            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"\n📄 真值标注完成: {totalRows} 行 -> {outPath}");
        }

        /// <summary>
        /// 克隆为单链求解配置: 逐字复刻盲搜逐链演化循环的 subCfg 构造 (MGAAdaptiveSolver.cs:483-501)。
        /// 关键点:
        ///   - 用真正的 Clone() (MemberwiseClone 全字段), 不手搓字段清单 (手搓曾漏字段导致 DE 口径漂移);
        ///   - AutoTuneToFBounds=true: 盲搜对每条链都按链重调谐 ToF 并重推导共振腿界;
        ///   - TotalToFMaxDays 恢复【原始用户值】: Solve 入口的 userSpecifiedMaxToF 捕获语义与盲搜完全一致
        ///     (有历史航时 → hist*1.5 优先; 无 → 逐链 dynMax)。
        /// </summary>
        private static MGAAdaptiveSolverConfig CloneForSingleChain(MGAAdaptiveSolverConfig src, List<string> chain, double userMaxOriginal)
        {
            var sub = src.Clone();
            sub.FlybySequence = chain != null ? new List<string>(chain) : new List<string>();
            sub.EnablePermutationsAndInsertions = false;   // 单链模式: 不再触发 MCTS/图/QD
            sub.LegStrategies = null;
            sub.LegToFBounds = new List<(double, double)>();   // 置空列表而非 null: Clone() 对 null 会抛 ArgumentNullException (默认值即空列表)
            sub.AutoTuneToFBounds = true;
            sub.TotalToFMaxDays = userMaxOriginal;         // -1 或 hist*1.5, 与盲搜入口捕获语义一致
            sub.SearchStartUT = src.SearchStartUT;
            sub.SearchEndUT = src.SearchEndUT;

            // [口径修复 2026-09-11] 完整复刻生产端 MGAAdaptiveSolver.cs:490-496 的【逐链】调谐。
            // 原实现仅沿用阶段1基于【空序列】调谐出的 ToF 界与空 LegToFBounds,
            // 与生产"对每条链单独重新推导 dynMin/dynMax + AutoDeriveResonantLegBounds"不同,
            // 导致真值是在不匹配该链的 ToF 界下取得的 —— 直接证据:
            //   galileo VEEGA 链标注真值 6430.1 m/s, 而生产盲搜同一条链仅 11311.1 m/s。
            // 修复后两者口径一致, 真值才可用于监督标定/查全率等下游分析。
            var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds(
                sub.DeparturePlanet, sub.ArrivalPlanet, sub.FlybySequence, sub.CentralBody,
                sub.DepartureToFMinFactor, sub.DepartureToFMaxFactor);
            sub.TotalToFMinDays = dynMin;
            sub.TotalToFMaxDays = (userMaxOriginal > 0) ? userMaxOriginal : dynMax;
            sub.LegToFBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(
                sub.DeparturePlanet, sub.ArrivalPlanet, sub.FlybySequence, sub.CentralBody,
                sub.DepartureToFMinFactor, sub.DepartureToFMaxFactor);
            return sub;
        }

        private static string F(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "NaN";
            return v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
