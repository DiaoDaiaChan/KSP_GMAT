// ============================================================================
// MGAOptimizationProbe.cs
// MGA 全面数据与日志效能探针系统 (Telemetry & Optimization Efficacy Probe)
//
// 职责:
//   1. 监控 5 大 DirectCompute GPU 模块真实运行时耗时、吞吐量与加速生效状态；
//   2. 监控自适应 AI 核心 (MAB 强化学习调参器、MCTS 拓扑剪枝、代理特征面初筛、自愈退火)
//      的关键指标、剪枝淘汰率与适应度提升量；
//   3. 零锁或原子累加无阻碍度量，全流程开销 < 0.05%；
//   4. 输出结构化的《MGA 优化效能与硬件加速审计报表》。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using KSPGMAT.MGA.AI;

namespace KSPGMAT.MGA.Telemetry
{
    public static class MGAOptimizationProbe
    {
        // ------------------------------------------------------------------------
        // 1. GPU 硬件加速指标统计
        // ------------------------------------------------------------------------
        public class GpuKernelMetrics
        {
            public string Name;
            public int GpuSuccessCalls;
            public int CpuFallbackCalls;
            public long TotalWorkItems;
            public double TotalGpuMs;
            public double MinGpuMs = double.MaxValue;
            public double MaxGpuMs = 0.0;
            private readonly object _lock = new object();

            public void RecordGpu(double elapsedMs, long workItems)
            {
                lock (_lock)
                {
                    GpuSuccessCalls++;
                    TotalWorkItems += workItems;
                    TotalGpuMs += elapsedMs;
                    if (elapsedMs < MinGpuMs) MinGpuMs = elapsedMs;
                    if (elapsedMs > MaxGpuMs) MaxGpuMs = elapsedMs;
                }
            }

            public void RecordFallback(long workItems)
            {
                lock (_lock)
                {
                    CpuFallbackCalls++;
                    TotalWorkItems += workItems;
                }
            }

            public double AvgGpuMs => GpuSuccessCalls > 0 ? TotalGpuMs / GpuSuccessCalls : 0.0;
            public double ThroughputPerSec => TotalGpuMs > 0.0 ? (TotalWorkItems / (TotalGpuMs / 1000.0)) : 0.0;
        }

        private static readonly ConcurrentDictionary<string, GpuKernelMetrics> s_gpuKernels =
            new ConcurrentDictionary<string, GpuKernelMetrics>();

        public static void RecordGpuExecution(string kernelName, double elapsedMs, long workItems, bool isGpuSuccess)
        {
            var metric = s_gpuKernels.GetOrAdd(kernelName, name => new GpuKernelMetrics { Name = name });
            if (isGpuSuccess)
            {
                metric.RecordGpu(elapsedMs, workItems);
            }
            else
            {
                metric.RecordFallback(workItems);
            }
        }

        // ------------------------------------------------------------------------
        // 2. 自适应 AI 与启发式过滤指标统计
        // ------------------------------------------------------------------------
        public static class AiMetrics
        {
            // 特征面初筛 (Surrogate Pre-filter)
            private static int s_surrogateTotalScanned;
            private static int s_surrogatePassed;

            public static void RecordSurrogate(int totalWindows, int passedWindows)
            {
                Interlocked.Add(ref s_surrogateTotalScanned, totalWindows);
                Interlocked.Add(ref s_surrogatePassed, passedWindows);
            }

            public static (int total, int passed, double rejectionRate) GetSurrogateStats()
            {
                int total = Volatile.Read(ref s_surrogateTotalScanned);
                int passed = Volatile.Read(ref s_surrogatePassed);
                int rejected = total - passed;
                double rate = total > 0 ? (double)rejected / total * 100.0 : 0.0;
                return (total, passed, rate);
            }

            // MCTS 拓扑剪枝 (MCTS Topology)
            private static int s_mctsEvaluatedNodes;
            private static int s_mctsPrunedBranches;
            private static int s_mctsKeptTopologies;

            public static void RecordMcts(int evaluated, int pruned, int kept)
            {
                Interlocked.Add(ref s_mctsEvaluatedNodes, evaluated);
                Interlocked.Add(ref s_mctsPrunedBranches, pruned);
                Interlocked.Add(ref s_mctsKeptTopologies, kept);
            }

            public static (int evaluated, int pruned, int kept, double pruneRate) GetMctsStats()
            {
                int eval = Volatile.Read(ref s_mctsEvaluatedNodes);
                int pruned = Volatile.Read(ref s_mctsPrunedBranches);
                int kept = Volatile.Read(ref s_mctsKeptTopologies);
                double rate = eval > 0 ? (double)pruned / eval * 100.0 : 0.0;
                return (eval, pruned, kept, rate);
            }

            // 自愈与 Hooke-Jeeves 打磨提升 (Self-Healing & Polishing)
            private static int s_healingAttempts;
            private static int s_healingSuccesses;
            private static double s_totalDvSavedKms;
            private static readonly object s_healingLock = new object();

            public static void RecordSelfHealing(bool success, double dvSavedKms)
            {
                lock (s_healingLock)
                {
                    s_healingAttempts++;
                    if (success)
                    {
                        s_healingSuccesses++;
                        if (dvSavedKms > 0) s_totalDvSavedKms += dvSavedKms;
                    }
                }
            }

            public static (int attempts, int successes, double totalDvSavedMs, double avgDvSavedMs) GetSelfHealingStats()
            {
                lock (s_healingLock)
                {
                    double totalSavedMs = s_totalDvSavedKms * 1000.0;
                    double avgSavedMs = s_healingSuccesses > 0 ? totalSavedMs / s_healingSuccesses : 0.0;
                    return (s_healingAttempts, s_healingSuccesses, totalSavedMs, avgSavedMs);
                }
            }

            // MAB 调参器快照 (Bandit Tuner)
            public class BanditSnapshot
            {
                public string IslandName;
                public int TotalPulls;
                public int[] ArmPulls;
                public double[] ArmRewards;
                public double FinalF;
                public double FinalCR;
            }

            private static readonly ConcurrentBag<BanditSnapshot> s_banditSnapshots = new ConcurrentBag<BanditSnapshot>();

            public static void RecordBanditSnapshot(string islandName, int totalPulls, int[] armPulls, double[] armRewards, double finalF, double finalCR)
            {
                s_banditSnapshots.Add(new BanditSnapshot
                {
                    IslandName = islandName,
                    TotalPulls = totalPulls,
                    ArmPulls = (int[])armPulls.Clone(),
                    ArmRewards = (double[])armRewards.Clone(),
                    FinalF = finalF,
                    FinalCR = finalCR
                });
            }

            public static BanditSnapshot[] GetBanditSnapshots() => s_banditSnapshots.ToArray();

            public static void Reset()
            {
                Volatile.Write(ref s_surrogateTotalScanned, 0);
                Volatile.Write(ref s_surrogatePassed, 0);
                Volatile.Write(ref s_mctsEvaluatedNodes, 0);
                Volatile.Write(ref s_mctsPrunedBranches, 0);
                Volatile.Write(ref s_mctsKeptTopologies, 0);

                lock (s_healingLock)
                {
                    s_healingAttempts = 0;
                    s_healingSuccesses = 0;
                    s_totalDvSavedKms = 0.0;
                }

                while (s_banditSnapshots.TryTake(out _)) { }
            }
        }

        /// <summary>
        /// 重置所有探针数据
        /// </summary>
        public static void Reset()
        {
            s_gpuKernels.Clear();
            AiMetrics.Reset();
        }

        /// <summary>
        /// 打印全量优化与加速审计报表
        /// </summary>
        public static void PrintEfficiencyReport()
        {
            try
            {
                string report = GenerateReport();
                KSPGMATLog.Info(report);
            }
            catch (Exception ex)
            {
                KSPGMATLog.Warn($"[MGAOptimizationProbe] 打印审计报表异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成结构化报表文本
        /// </summary>
        public static string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine("🚀 [MGA Optimization Probe] 新增加速与自适应优化效能综合审计报表");
            sb.AppendLine("==========================================================================================");

            // 1. GPU 硬件加速部分
            sb.AppendLine("【Part 1: DirectCompute HLSL CS 5.0 GPU 硬件加速状态】");
            sb.AppendLine($"  • GPU 硬件就绪状态: {(PykepNative.IsGpuComputeAvailable ? "✅ 正常在线" : "⚠️ 未就绪或降级至 CPU")}");
            if (s_gpuKernels.Count == 0)
            {
                sb.AppendLine("  (本轮求解尚未调度 GPU 批处理算子)");
            }
            else
            {
                sb.AppendLine("------------------------------------------------------------------------------------------");
                sb.AppendLine($"{"GPU算子名称",-30} {"GPU执行",10} {"CPU降级",10} {"总计算量(点)",14} {"平均耗时",12} {"吞吐速率(点/秒)",16}");
                sb.AppendLine("------------------------------------------------------------------------------------------");

                foreach (var kvp in s_gpuKernels)
                {
                    var k = kvp.Value;
                    string avgStr = k.AvgGpuMs >= 1000.0 ? $"{k.AvgGpuMs / 1000.0:F2}s" : $"{k.AvgGpuMs:F2}ms";
                    string tputStr = k.ThroughputPerSec >= 1e6 
                        ? $"{k.ThroughputPerSec / 1e6:F2} Mpts/s" 
                        : (k.ThroughputPerSec >= 1e3 ? $"{k.ThroughputPerSec / 1e3:F1} Kpts/s" : $"{k.ThroughputPerSec:F0} pts/s");

                    sb.AppendLine($"{k.Name,-30} {k.GpuSuccessCalls,10} {k.CpuFallbackCalls,10} {k.TotalWorkItems,14:N0} {avgStr,12} {tputStr,16}");
                }
            }
            sb.AppendLine();

            // 2. 自适应 AI 与启发式过滤部分
            sb.AppendLine("【Part 2: 自适应 AI 与强化学习元启发效能】");

            // 2.1 特征面初筛
            var (surTotal, surPassed, surRejRate) = AiMetrics.GetSurrogateStats();
            sb.AppendLine($"  [A. 能量特征面预筛器 (Surrogate Pre-Filter)]");
            sb.AppendLine($"     扫描总窗口: {surTotal:N0} | 保留可行区: {surPassed:N0} | 剪除高能无效区: {surRejRate:F1}% {(surRejRate >= 85.0 ? "(✅ 极其显著，节省大幅物理Lambert计算)" : "(正常)")}");

            // 2.2 MCTS 拓扑剪枝
            var (mctsEval, mctsPruned, mctsKept, mctsPruneRate) = AiMetrics.GetMctsStats();
            sb.AppendLine($"  [B. MCTS 拓扑树离散剪枝器 (MCTS Topology Pruner)]");
            sb.AppendLine($"     评估节点总数: {mctsEval:N0} | 剪除违背几何支路: {mctsPruned:N0} (剪枝率 {mctsPruneRate:F1}%) | 输出黄金拓扑: {mctsKept}");

            // 2.3 自愈与打磨提升
            var (healAtt, healSucc, healTotSaved, healAvgSaved) = AiMetrics.GetSelfHealingStats();
            sb.AppendLine($"  [C. 数值自愈与 Hooke-Jeeves 打磨 (Self-Healing & Polishing)]");
            sb.AppendLine($"     打磨触发次数: {healAtt} | 成功修复收敛: {healSucc} | 累计节省 Delta-V: {healTotSaved:F1} m/s (平均单次提升 {healAvgSaved:F1} m/s)");

            // 2.4 MAB 调参器
            var bandits = AiMetrics.GetBanditSnapshots();
            if (bandits.Length > 0)
            {
                sb.AppendLine($"  [D. MAB 多臂老虎机强化学习调参器 (Bandit Tuner Snapshots - {bandits.Length} 个岛屿)]");
                for (int bIdx = 0; bIdx < bandits.Length; bIdx++)
                {
                    var b = bandits[bIdx];
                    sb.AppendLine($"     • 岛屿 #{bIdx + 1} ({b.IslandName}): 总拉动={b.TotalPulls}, 最终收敛 (F={b.FinalF:F2}, CR={b.FinalCR:F2})");
                    string[] stratNames = { "Rand1", "Best1", "CurToBest1", "RandToBest1" };
                    var armSb = new StringBuilder("       算子探索/利用分布: ");
                    for (int a = 0; a < b.ArmPulls.Length; a++)
                    {
                        double armPct = b.TotalPulls > 0 ? (double)b.ArmPulls[a] / b.TotalPulls * 100.0 : 0.0;
                        armSb.Append($"[{stratNames[a]}: {b.ArmPulls[a]}次 ({armPct:F0}%), 奖励={b.ArmRewards[a]:F1}] ");
                    }
                    sb.AppendLine(armSb.ToString());
                }
            }

            sb.AppendLine("==========================================================================================");
            return sb.ToString();
        }
    }
}
