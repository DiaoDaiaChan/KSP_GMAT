#nullable disable
using System;
using System.Diagnostics;
using System.Threading;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 高性能轻量级 CPU 与计算性能实时监控器
    /// 测量当前进程 CPU 利用率、逻辑核心数、线程池状态、内存与评估吞吐量 (evals/s)
    /// </summary>
    public static class CPUMonitor
    {
        private static readonly Process s_process = Process.GetCurrentProcess();
        private static readonly int s_processorCount = Environment.ProcessorCount;

        private static long s_totalEvaluations = 0;
        private static DateTime s_lastSampleTime = DateTime.UtcNow;
        private static TimeSpan s_lastCpuTime = s_process.TotalProcessorTime;
        private static long s_lastEvaluations = 0;

        private static double s_cachedCpuUsagePercent = 0.0;
        private static double s_cachedEvalsPerSecond = 0.0;
        private static readonly object s_sampleLock = new object();

        static CPUMonitor()
        {
            // 初始化提高线程池最低预备线程，避免 .NET 线程池爬升延迟
            try
            {
                int minWorkers = Math.Max(s_processorCount * 2, 16);
                int minIOCP = Math.Max(s_processorCount, 8);
                ThreadPool.SetMinThreads(minWorkers, minIOCP);
            }
            catch
            {
                // 忽略系统权限受限
            }
        }

        /// <summary>
        /// 记录一次或多次弹道/适应度评估
        /// </summary>
        public static void RecordEvaluation(int count = 1)
        {
            Interlocked.Add(ref s_totalEvaluations, count);
        }

        /// <summary>
        /// 重置性能计数器
        /// </summary>
        public static void Reset()
        {
            lock (s_sampleLock)
            {
                s_totalEvaluations = 0;
                s_lastEvaluations = 0;
                s_lastSampleTime = DateTime.UtcNow;
                try
                {
                    s_process.Refresh();
                    s_lastCpuTime = s_process.TotalProcessorTime;
                }
                catch { }
                s_cachedCpuUsagePercent = 0.0;
                s_cachedEvalsPerSecond = 0.0;
            }
        }

        /// <summary>
        /// 获取 CPU 与性能快照（采样间隔内部平滑限频 >= 100ms）
        /// </summary>
        public static CPUSnapshot GetSnapshot()
        {
            lock (s_sampleLock)
            {
                var now = DateTime.UtcNow;
                double elapsedSec = (now - s_lastSampleTime).TotalSeconds;

                if (elapsedSec >= 0.1) // 每 100ms 刷新一次平滑值
                {
                    try
                    {
                        s_process.Refresh();
                        var currentCpuTime = s_process.TotalProcessorTime;
                        long currentEvals = Interlocked.Read(ref s_totalEvaluations);

                        double cpuUsedSec = (currentCpuTime - s_lastCpuTime).TotalSeconds;
                        // 进程总 CPU 百分比 (0% ~ 100%)
                        double cpuPct = (cpuUsedSec / (elapsedSec * s_processorCount)) * 100.0;
                        if (cpuPct < 0.0) cpuPct = 0.0;
                        if (cpuPct > 100.0) cpuPct = 100.0;

                        double evalsDelta = currentEvals - s_lastEvaluations;
                        double evalsPerSec = evalsDelta / elapsedSec;

                        s_cachedCpuUsagePercent = cpuPct;
                        s_cachedEvalsPerSecond = evalsPerSec > 0 ? evalsPerSec : 0;

                        s_lastSampleTime = now;
                        s_lastCpuTime = currentCpuTime;
                        s_lastEvaluations = currentEvals;
                    }
                    catch
                    {
                        // 兜底保护
                    }
                }

                ThreadPool.GetAvailableThreads(out int availWorkers, out _);
                ThreadPool.GetMaxThreads(out int maxWorkers, out _);
                int activeWorkers = Math.Max(0, maxWorkers - availWorkers);
                long memBytes = 0;
                try { memBytes = s_process.WorkingSet64; } catch { }

                return new CPUSnapshot
                {
                    CpuUsagePercent = s_cachedCpuUsagePercent,
                    ProcessorCount = s_processorCount,
                    ActiveThreadPoolWorkers = activeWorkers,
                    MaxThreadPoolWorkers = maxWorkers,
                    EvaluationsPerSecond = s_cachedEvalsPerSecond,
                    TotalEvaluations = Interlocked.Read(ref s_totalEvaluations),
                    MemoryWorkingSetMB = memBytes / (1024.0 * 1024.0)
                };
            }
        }
    }

    /// <summary>
    /// CPU 与计算性能快照数据
    /// </summary>
    public struct CPUSnapshot
    {
        public double CpuUsagePercent;
        public int ProcessorCount;
        public int ActiveThreadPoolWorkers;
        public int MaxThreadPoolWorkers;
        public double EvaluationsPerSecond;
        public long TotalEvaluations;
        public double MemoryWorkingSetMB;

        public override string ToString()
        {
            string evalRateStr = EvaluationsPerSecond >= 1000000 
                ? $"{EvaluationsPerSecond / 1000000.0:F2}M evals/s" 
                : (EvaluationsPerSecond >= 1000 ? $"{EvaluationsPerSecond / 1000.0:F1}k evals/s" : $"{EvaluationsPerSecond:F0} evals/s");

            return $"CPU: {CpuUsagePercent,5:F1}% ({ProcessorCount} 核心) | 速度: {evalRateStr,13} | 内存: {MemoryWorkingSetMB:F0} MB";
        }
    }
}
