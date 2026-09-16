using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.TestSuites
{
    public class HistoricalBenchmarkMissionsTest : ITestModule
    {
        public string ModuleId => "historical_benchmark";
        public string Name => "历史深空与经典飞行任务全量基准测试套件";
        public string Description => "验证游戏内 GUI [Historical Benchmark Suite] 全部真实历史与深空飞行任务全收敛 (区分 MGA / 直达 / 地月)";

        public static string MissionFilter { get; set; } = null;

        public bool Run(TestContext ctx)
        {
            int total = 0, passed = 0;
            RunTest(ctx, ref total, ref passed, includePresets: false);
            return passed == total && total > 0;
        }

        public static void RunTest(ref int total, ref int passed, bool includePresets = true)
        {
            RunTest(null, ref total, ref passed, includePresets);
        }

        public static void RunTest(TestContext ctx, ref int total, ref int passed, bool includePresets = true)
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🪐 [用例集 D] 历史基准飞行任务逐条验证 (区分 MGA 借力 / 行星际直达 / 地月任务)");
            if (!string.IsNullOrEmpty(MissionFilter))
            {
                Console.WriteLine($"   ★ 启用单项/子集过滤条件: \"{MissionFilter}\"");
            }
            Console.WriteLine("==========================================================================================");

            var categories = new (string Name, List<MGABenchmarkSuite.BenchmarkCase> List)[]
            {
                ("MGA 行星际引力助推任务集", MGABenchmarkSuite.MGA_Missions),
                ("行星际直达与轨道机动任务集", MGABenchmarkSuite.DirectTransfer_Missions),
                ("地月空间与中心引力场任务集", MGABenchmarkSuite.Cislunar_Missions)
            };

            int globalIndex = 0;

            foreach (var cat in categories)
            {
                Console.WriteLine($"\n------------------------------------------------------------------------------------------");
                Console.WriteLine($">>> [{cat.Name}] 共 {cat.List.Count} 项任务 <<<");
                Console.WriteLine($"------------------------------------------------------------------------------------------");

                for (int i = 0; i < cat.List.Count; i++)
                {
                    globalIndex++;
                    var testCase = cat.List[i];

                    // 过滤检查 (支持任务序号 1..21 或任务名称子串匹配)
                    if (!string.IsNullOrEmpty(MissionFilter))
                    {
                        bool matchIndex = int.TryParse(MissionFilter, out int targetIdx) && (targetIdx == globalIndex);
                        bool matchName = testCase.MissionName.IndexOf(MissionFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!matchIndex && !matchName)
                        {
                            continue;
                        }
                    }

                    total++;
                    Console.WriteLine($"\n==========================================================================================");
                    Console.WriteLine($"🚀 [任务 #{globalIndex:D2}] {testCase.MissionName}");
                    Console.WriteLine($"   描述: {testCase.Description}");
                    Console.WriteLine($"   参考天体链路: {testCase.DepartureBody} ➔ {string.Join(" ➔ ", testCase.FlybySequence)}");
                    Console.WriteLine($"   标称历史发射历元: {testCase.HistoricLaunchDateStr} (UT {testCase.HistoricLaunchUT:F0})");
                    Console.WriteLine($"   历史工程基准: ΔV={testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0} m/s | 航时={testCase.HistoricFlightDaysMin:F0}~{testCase.HistoricFlightDaysMax:F0} 天");
                    Console.WriteLine($"------------------------------------------------------------------------------------------");

                    ctx?.StartCase($"#{globalIndex:D2}_{testCase.MissionName}");
                    ctx?.RecordMetric("分类", cat.Name);

                    var outcome = HistoricalBenchmarkVerificationEngine.RunAndVerify(testCase, ctx);
                    if (outcome.Passed) passed++;

                    ctx?.EndCase(outcome.Passed);
                }
            }

            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine($"🪐 历史基准飞行任务执行完毕: 共执行 {total} 项，通过 {passed} 项 (通过率: {(total > 0 ? (double)passed / total * 100.0 : 0.0):F1}%)");
            Console.WriteLine("==========================================================================================\n");

            if (includePresets && string.IsNullOrEmpty(MissionFilter))
            {
                GuiPresetOptimizationsTest.RunTest(ref total, ref passed);
            }
        }
    }
}
