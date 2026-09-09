#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// MGA 全流程细粒度步骤耗时诊断与性能剖析器 (Fine-Grained Step Performance Profiler)
    /// 测量并统计求解全生命周期各核心算法步骤 (MCTS拓扑推演、前向步进注种、开普勒共振槽扫描、
    /// DE代数演化循环、Hooke-Jeeves极值打磨、数值自愈与约束审计等) 的真实耗时与调用频次，
    /// 自动识别并高亮耗时第一瓶颈，为针对性工程优化提供第一手实测数据。
    /// </summary>
    public static class MGAStepProfiler
    {
        private sealed class StepMetric
        {
            public string Name;
            public long TotalTicks;
            public int CallCount;
            public long MinTicks = long.MaxValue;
            public long MaxTicks = 0;
            private readonly object _lock = new object();

            public void Record(long ticks)
            {
                lock (_lock)
                {
                    TotalTicks += ticks;
                    CallCount++;
                    if (ticks < MinTicks) MinTicks = ticks;
                    if (ticks > MaxTicks) MaxTicks = ticks;
                }
            }

            public double TotalMs => (double)TotalTicks / Stopwatch.Frequency * 1000.0;
            public double TotalSec => (double)TotalTicks / Stopwatch.Frequency;
            public double AvgMs => CallCount > 0 ? TotalMs / CallCount : 0.0;
            public double MinMs => MinTicks == long.MaxValue ? 0.0 : (double)MinTicks / Stopwatch.Frequency * 1000.0;
            public double MaxMs => (double)MaxTicks / Stopwatch.Frequency * 1000.0;
        }

        private static readonly ConcurrentDictionary<string, StepMetric> s_metrics = new ConcurrentDictionary<string, StepMetric>();
        private static readonly Stopwatch s_globalWatch = new Stopwatch();

        /// <summary>
        /// 重置所有性能剖析数据
        /// </summary>
        public static void Reset()
        {
            s_metrics.Clear();
            s_globalWatch.Restart();
        }

        /// <summary>
        /// 开启一个被度量的步骤作用域 (IDisposable 模式)
        /// </summary>
        public static IDisposable Scope(string stepName)
        {
            if (!s_globalWatch.IsRunning) s_globalWatch.Start();
            return new StepScope(stepName);
        }

        /// <summary>
        /// 手工记录某步骤耗时
        /// </summary>
        public static void Record(string stepName, long elapsedTicks)
        {
            var metric = s_metrics.GetOrAdd(stepName, name => new StepMetric { Name = name });
            metric.Record(elapsedTicks);
        }

        /// <summary>
        /// 手工记录某步骤耗时 (毫秒)
        /// </summary>
        public static void RecordMs(string stepName, double elapsedMs)
        {
            long ticks = (long)(elapsedMs / 1000.0 * Stopwatch.Frequency);
            Record(stepName, ticks);
        }

        private sealed class StepScope : IDisposable
        {
            private readonly string _name;
            private readonly long _startTick;

            public StepScope(string name)
            {
                _name = name;
                _startTick = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                long elapsed = Stopwatch.GetTimestamp() - _startTick;
                Record(_name, elapsed);
            }
        }

        /// <summary>
        /// 生成全流程耗时排名报表文本
        /// </summary>
        public static string GenerateReport()
        {
            var sb = new StringBuilder();
            double globalTotalSec = s_globalWatch.Elapsed.TotalSeconds;
            var list = s_metrics.Values.OrderByDescending(m => m.TotalTicks).ToList();
            double sumMeasuredSec = list.Sum(m => m.TotalSec);
            double baseSec = Math.Max(globalTotalSec, sumMeasuredSec);

            sb.AppendLine("==========================================================================================");
            sb.AppendLine("⏱️  [MGAStepProfiler] MGA 天体力学求解器全流程细粒度步骤耗时诊断报表");
            sb.AppendLine("==========================================================================================");
            sb.AppendLine($"  • 全局总运行耗时: {globalTotalSec:F2} 秒 | 剖析监控步骤总数: {list.Count} 项");
            sb.AppendLine("------------------------------------------------------------------------------------------");
            sb.AppendLine($"{"排名",-4} {"核心算法步骤名称",-36} {"总耗时",12} {"占比",8} {"调用次数",10} {"单次平均",12}");
            sb.AppendLine("------------------------------------------------------------------------------------------");

            int rank = 1;
            foreach (var m in list)
            {
                double pct = baseSec > 1e-4 ? (m.TotalSec / baseSec) * 100.0 : 0.0;
                string timeStr = m.TotalSec >= 1.0 ? $"{m.TotalSec:F2}s" : $"{m.TotalMs:F1}ms";
                string avgStr = m.AvgMs >= 1000.0 ? $"{m.AvgMs / 1000.0:F2}s" : $"{m.AvgMs:F1}ms";
                sb.AppendLine($"#{rank++,-3} {m.Name,-36} {timeStr,12} {pct,7:F1}% {m.CallCount,10} {avgStr,12}");
            }
            sb.AppendLine("------------------------------------------------------------------------------------------");

            if (list.Count > 0)
            {
                var top1 = list[0];
                double top1Pct = baseSec > 1e-4 ? (top1.TotalSec / baseSec) * 100.0 : 0.0;
                sb.AppendLine($"🔥 [最大耗时瓶颈 #1]: 【{top1.Name}】");
                sb.AppendLine($"   累计耗时: {top1.TotalSec:F2} 秒 (占总耗时 {top1Pct:F1}%)，累计执行 {top1.CallCount} 次，单次均耗: {top1.AvgMs:F1} ms (Min={top1.MinMs:F1}ms, Max={top1.MaxMs:F1}ms)");

                if (list.Count > 1)
                {
                    var top2 = list[1];
                    double top2Pct = baseSec > 1e-4 ? (top2.TotalSec / baseSec) * 100.0 : 0.0;
                    sb.AppendLine($"⚡ [次席耗时瓶颈 #2]: 【{top2.Name}】耗时 {top2.TotalSec:F2} 秒 ({top2Pct:F1}%)，执行 {top2.CallCount} 次");
                }
                if (list.Count > 2)
                {
                    var top3 = list[2];
                    double top3Pct = baseSec > 1e-4 ? (top3.TotalSec / baseSec) * 100.0 : 0.0;
                    sb.AppendLine($"⚡ [第三耗时瓶颈 #3]: 【{top3.Name}】耗时 {top3.TotalSec:F2} 秒 ({top3Pct:F1}%)，执行 {top3.CallCount} 次");
                }
            }

            sb.AppendLine("==========================================================================================");
            return sb.ToString();
        }

        /// <summary>
        /// 直接在控制台打印耗时剖析摘要
        /// </summary>
        public static void PrintSummary()
        {
            Console.WriteLine(GenerateReport());
        }
    }
}
