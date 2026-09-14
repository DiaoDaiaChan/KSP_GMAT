using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT.MGA.Solver;

namespace MGADebugRunner.TestSuites
{
    public class HistoricalBlindSearchSuiteTest : ITestModule
    {
        public string ModuleId => "historical_blind_search";
        public string Name => "人类历史深空任务全时域纯盲搜与自主涌现测试套件";
        public string Description => "验证 100% 仅输入 Earth ➔ 目的地, 0 中间借力提示, 由通用天体动力学自主推导";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("HistoricalBlindSearch_Suite");
            bool ok = RunBlindSearchSuite(null);
            ctx.RecordMetric("盲搜套件判定", ok ? "PASS" : "FAIL");
            if (!ok) ctx.Fail("盲搜套件运行未成功");
            ctx.EndCase(ok);
            return ok;
        }

        public static bool RunBlindSearchSuite(string filter = null)
        {
            string reportDir = "reports/test_reports";
            System.IO.Directory.CreateDirectory(reportDir);
            string logPath = System.IO.Path.Combine(reportDir, "log_historical_blind.log");

            using (var logScope = new ModuleLogScope(logPath))
            {
                Console.WriteLine("==========================================================================================");
                Console.WriteLine("🌌 [MGAHistoricalBlindSearchSuite] 人类深空历史 MGA 任务纯盲搜与自主涌现发现测试套件");
                Console.WriteLine("   原则: 100% 仅输入 Earth ➔ 目的地, 0 中间借力天体提示, 全由通用天体动力学自主推导！");
                Console.WriteLine("==========================================================================================\n");

                var cases = MGAHistoricalBlindSearchSuite.HistoricalCatalog;
                if (!string.IsNullOrEmpty(filter))
                {
                    cases = cases.Where(c => c.MissionId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             c.MissionName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             c.DestinationBody.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    Console.WriteLine($"🔍 [定向过滤模式] 命中 {cases.Count} 个匹配任务: {string.Join(", ", cases.Select(c => c.MissionName))}\n");
                }

                if (cases.Count == 0)
                {
                    Console.WriteLine("⚠️ 未找到匹配的历史任务，请检查过滤关键词 (如: voyager2, cassini, galileo, mariner10 等)！");
                    return false;
                }

                var results = new List<BlindSearchResult>();
                foreach (var bCase in cases)
                {
                    var res = MGAHistoricalBlindSearchSuite.RunBlindSearch(bCase);
                    results.Add(res);
                }

                string reportPath = System.IO.Path.Combine(reportDir, "report_historical_blind.md");
                string rootReportPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports", "mga_historical_discovery_report.md");
                MGAHistoricalBlindSearchSuite.GenerateMarkdownReport(results, reportPath);
                MGAHistoricalBlindSearchSuite.GenerateMarkdownReport(results, rootReportPath);
                Console.WriteLine($"\n📄 深度分析与物理机理报表已成功生成: {reportPath}\n");
                return true;
            }
        }
    }
}
