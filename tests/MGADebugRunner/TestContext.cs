using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MGADebugRunner
{
    public class TestCaseResult
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public double ElapsedMs { get; set; }
        public Dictionary<string, string> Metrics { get; } = new Dictionary<string, string>();
        public List<string> Details { get; } = new List<string>();
        public string ErrorMessage { get; set; }
    }

    public class TestModuleReport
    {
        public string ModuleId { get; set; }
        public string ModuleName { get; set; }
        public string Description { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public double TotalElapsedMs { get; set; }
        public string LogFilePath { get; set; }
        public List<TestCaseResult> TestCases { get; } = new List<TestCaseResult>();

        public int TotalCount => TestCases.Count;
        public int PassCount => TestCases.FindAll(c => c.Passed).Count;
        public int FailCount => TotalCount - PassCount;
        public double PassRate => TotalCount > 0 ? (double)PassCount / TotalCount * 100.0 : 0.0;

        public void ExportMarkdown(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string filePath = Path.Combine(outputDir, $"report_{ModuleId}.md");

            var sb = new StringBuilder();
            sb.AppendLine($"# 测试模块诊断报告: {ModuleName}");
            sb.AppendLine();
            sb.AppendLine($"> **执行时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | **模块标识**: `{ModuleId}` | **总耗时**: {TotalElapsedMs:F0} ms");
            sb.AppendLine($"> **通过状态**: {(FailCount == 0 ? "✅ 全部通过 (100%)" : $"⚠️ 存在失败 ({PassCount}/{TotalCount} 通过, {PassRate:F1}%)")}");
            if (!string.IsNullOrEmpty(LogFilePath) && File.Exists(LogFilePath))
            {
                string fullLogPath = Path.GetFullPath(LogFilePath).Replace('\\', '/');
                sb.AppendLine($"> **完整原始执行日志**: [`log_{ModuleId}.log`](file:///{fullLogPath})");
            }
            sb.AppendLine();
            if (!string.IsNullOrEmpty(Description))
            {
                sb.AppendLine($"**模块说明**: {Description}");
                sb.AppendLine();
            }

            sb.AppendLine("## 1. 测试用例总览");
            sb.AppendLine();
            sb.AppendLine("| 用例序号 | 测试用例名称 | 状态 | 耗时 | 核心物理指标摘要 |");
            sb.AppendLine("|:---:|:---|:---:|:---:|:---|");

            for (int i = 0; i < TestCases.Count; i++)
            {
                var tc = TestCases[i];
                string status = tc.Passed ? "✅ PASS" : "❌ FAIL";
                string metricsSummary = "";
                if (tc.Metrics.Count > 0)
                {
                    var mList = new List<string>();
                    foreach (var kv in tc.Metrics)
                    {
                        mList.Add($"{kv.Key}={kv.Value}");
                    }
                    metricsSummary = string.Join(", ", mList);
                }
                sb.AppendLine($"| #{i + 1} | `{tc.Name}` | **{status}** | {tc.ElapsedMs:F0} ms | {metricsSummary} |");
            }

            sb.AppendLine();
            sb.AppendLine("## 2. 逐用例详细日志与物理数据");
            sb.AppendLine();

            for (int i = 0; i < TestCases.Count; i++)
            {
                var tc = TestCases[i];
                sb.AppendLine($"### #{i + 1} {tc.Name} ({(tc.Passed ? "✅ PASS" : "❌ FAIL")})");
                sb.AppendLine($"- **用例耗时**: {tc.ElapsedMs:F1} ms");
                if (tc.Metrics.Count > 0)
                {
                    sb.AppendLine("- **动力学参数**:");
                    foreach (var kv in tc.Metrics)
                    {
                        sb.AppendLine($"  - `{kv.Key}`: **{kv.Value}**");
                    }
                }
                if (!string.IsNullOrEmpty(tc.ErrorMessage))
                {
                    sb.AppendLine();
                    sb.AppendLine("> [!CAUTION]");
                    sb.AppendLine($"> **失败根因与异常**: {tc.ErrorMessage}");
                }
                if (tc.Details.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("```text");
                    foreach (var line in tc.Details)
                    {
                        sb.AppendLine(line);
                    }
                    sb.AppendLine("```");
                }
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"📄 独立测试报告已导出: {filePath}");
            if (!string.IsNullOrEmpty(LogFilePath) && File.Exists(LogFilePath))
            {
                Console.WriteLine($"📝 独立运行日志已保存: {LogFilePath}");
            }
        }
    }

    /// <summary>
    /// 控制台标准输出双路复制分流器：在正常打印控制台的同时，将每个模块的全部原始控制台日志独立录制到专属的 log 文件中。
    /// </summary>
    public class ModuleLogScope : IDisposable
    {
        private readonly TextWriter _origOut;
        private readonly TextWriter _origErr;
        private readonly StreamWriter _fileWriter;
        private readonly DualWriter _dualOut;
        private readonly DualWriter _dualErr;

        public ModuleLogScope(string logFilePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                _fileWriter = new StreamWriter(new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
                {
                    AutoFlush = true
                };

                _origOut = Console.Out;
                _origErr = Console.Error;

                _dualOut = new DualWriter(_origOut, _fileWriter);
                _dualErr = new DualWriter(_origErr, _fileWriter);

                Console.SetOut(_dualOut);
                Console.SetError(_dualErr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] 无法创建模块日志捕获器: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_origOut != null) Console.SetOut(_origOut);
            if (_origErr != null) Console.SetError(_origErr);
            try
            {
                _fileWriter?.Flush();
                _fileWriter?.Dispose();
            }
            catch {}
        }

        private class DualWriter : TextWriter
        {
            private readonly TextWriter _w1;
            private readonly TextWriter _w2;
            private readonly object _lock = new object();

            public DualWriter(TextWriter w1, TextWriter w2)
            {
                _w1 = w1;
                _w2 = w2;
            }

            public override Encoding Encoding => _w1.Encoding;

            public override void Write(char value)
            {
                lock (_lock)
                {
                    _w1.Write(value);
                    _w2.Write(value);
                }
            }

            public override void Write(string value)
            {
                lock (_lock)
                {
                    _w1.Write(value);
                    _w2.Write(value);
                }
            }

            public override void WriteLine(string value)
            {
                lock (_lock)
                {
                    _w1.WriteLine(value);
                    _w2.WriteLine(value);
                }
            }

            public override void Flush()
            {
                lock (_lock)
                {
                    _w1.Flush();
                    _w2.Flush();
                }
            }
        }
    }

    public class TestContext : IDisposable
    {
        private readonly TestModuleReport _report;
        private readonly ModuleLogScope _logScope;
        private TestCaseResult _currentCase;
        private Stopwatch _caseSw;

        public TestModuleReport Report => _report;
        public string LogFilePath => _report.LogFilePath;

        public TestContext(string moduleId, string moduleName, string description = null, string outputDir = "reports/test_reports")
        {
            Directory.CreateDirectory(outputDir);
            string logPath = Path.Combine(outputDir, $"log_{moduleId}.log");

            _report = new TestModuleReport
            {
                ModuleId = moduleId,
                ModuleName = moduleName,
                Description = description,
                LogFilePath = logPath
            };

            _logScope = new ModuleLogScope(logPath);
        }

        public void StartCase(string caseName)
        {
            _currentCase = new TestCaseResult { Name = caseName, Passed = true };
            _caseSw = Stopwatch.StartNew();
        }

        public void RecordMetric(string key, object value)
        {
            if (_currentCase != null && value != null)
            {
                _currentCase.Metrics[key] = value.ToString();
            }
        }

        public void Log(string message)
        {
            _currentCase?.Details.Add(message);
        }

        public void Fail(string errorMsg)
        {
            if (_currentCase != null)
            {
                _currentCase.Passed = false;
                _currentCase.ErrorMessage = errorMsg;
            }
        }

        public void EndCase(bool? passOverride = null)
        {
            if (_currentCase != null)
            {
                _caseSw.Stop();
                _currentCase.ElapsedMs = _caseSw.Elapsed.TotalMilliseconds;
                if (passOverride.HasValue) _currentCase.Passed = passOverride.Value;
                _report.TestCases.Add(_currentCase);
                _currentCase = null;
            }
        }

        public void SaveReport(string outputDir = "reports/test_reports")
        {
            _report.ExportMarkdown(outputDir);
        }

        public void Dispose()
        {
            _logScope?.Dispose();
        }
    }

    public interface ITestModule
    {
        string ModuleId { get; }
        string Name { get; }
        string Description { get; }
        bool Run(TestContext ctx);
    }
}
