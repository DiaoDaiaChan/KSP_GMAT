#nullable disable
using System;
using System.Diagnostics;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 实时 MGA 计算进度与 CPU 状态 DEBUG 报告器
    /// 提供毫秒级平滑刷新、进度条展示、CPU 监控状态与瞬时吞吐量显示
    /// </summary>
    public class MGADebugReporter
    {
        private readonly string _taskName;
        private readonly Stopwatch _sw = new Stopwatch();
        private DateTime _lastPrintTime = DateTime.MinValue;
        private readonly object _consoleLock = new object();
        private readonly int _throttleMs;
        private string _lastStage = "";

        public MGADebugReporter(string taskName = "MGA Optimizer", int throttleMs = 120)
        {
            _taskName = taskName;
            _throttleMs = throttleMs;
            _sw.Start();
        }

        /// <summary>
        /// 转换为标准 Action<float, string> 委托传递给优化器
        /// </summary>
        public Action<float, string> AsProgressCallback()
        {
            return ReportProgress;
        }

        /// <summary>
        /// 报告当前进度与阶段信息
        /// </summary>
        public void ReportProgress(float progress, string stageInfo)
        {
            lock (_consoleLock)
            {
                var now = DateTime.UtcNow;
                bool isStageChanged = stageInfo != _lastStage && !string.IsNullOrEmpty(_lastStage);
                bool isFinished = progress >= 1.0f;
                double elapsedSinceLastPrint = (now - _lastPrintTime).TotalMilliseconds;

                if (!isFinished && !isStageChanged && elapsedSinceLastPrint < _throttleMs)
                {
                    return; // 节流控制，避免高频 I/O 阻塞线程
                }

                _lastPrintTime = now;
                _lastStage = stageInfo;

                var snapshot = CPUMonitor.GetSnapshot();
                int pct = (int)Math.Max(0, Math.Min(100, progress * 100));

                // 绘制简洁进度条 (长度 20)
                int barFilled = Math.Max(0, Math.Min(20, pct / 5));
                string bar = new string('=', barFilled) + (barFilled < 20 ? ">" : "") + new string('.', Math.Max(0, 20 - barFilled - (barFilled < 20 ? 1 : 0)));

                string timeElapsedStr = $"{_sw.Elapsed.TotalSeconds:F1}s";
                string cpuStr = snapshot.ToString();

                // 终端格式化行
                string line = $"[{_taskName}] [{bar}] {pct,3}% ({timeElapsedStr}) | {stageInfo} | {cpuStr}";

                if (isFinished)
                {
                    KSPGMATLog.Success("Solver", line);
                }
                else if (isStageChanged)
                {
                    KSPGMATLog.Info("Solver", line);
                }
                else
                {
                    KSPGMATLog.Debug("Solver", line);
                }
            }
        }

        /// <summary>
        /// 标记任务完成
        /// </summary>
        public void Complete(string message = "计算完成")
        {
            _sw.Stop();
            ReportProgress(1.0f, message);
        }
    }
}
