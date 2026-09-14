using System;
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

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("HistoricalBenchmark_AllMissions");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed, includePresets: false);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("历史任务总数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"历史任务基准未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed, bool includePresets = true)
        {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 D] 历史基准飞行任务全量验证 (区分 MGA 借力 / 行星际直达 / 地月任务)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var categories = new (string Name, System.Collections.Generic.List<MGABenchmarkSuite.BenchmarkCase> List)[]
            {
                ("MGA 行星际引力助推任务集", MGABenchmarkSuite.MGA_Missions),
                ("行星际直达与轨道机动任务集", MGABenchmarkSuite.DirectTransfer_Missions),
                ("地月空间与中心引力场任务集", MGABenchmarkSuite.Cislunar_Missions)
            };

            foreach (var cat in categories)
            {
                Console.WriteLine($"\n>>> [{cat.Name}] 共 {cat.List.Count} 项任务 <<<");
                for (int i = 0; i < cat.List.Count; i++)
                {
                    total++;
                    var testCase = cat.List[i];
                    Console.Write($"• [{i + 1:D2}/{cat.List.Count:D2}] {testCase.MissionName,-40} ... ");

                    var res = MGABenchmarkSuite.RunBenchmark(testCase);

                    if (res.Success && res.Solution != null)
                    {
                        passed++;
                        var sol = res.Solution;
                        Console.WriteLine($"✅ 收敛通过! 航段={sol.Legs.Count} | 计算总ΔV={res.ComputedTotalDV,6:F0} m/s (历史范围: {testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0}) | 历时={res.ComputedFlightDays,4:F0}d | 口径={testCase.BlindDimension}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ 未通过: Continuity={res.VectorContinuityPassed}, Feasibility={res.FlybyFeasibilityPassed}, DvTolerance={res.DeltaVWithinTolerance} (计算总ΔV: {res.ComputedTotalDV:F0} m/s)");
                    }
                }
            }

            Console.WriteLine();

            if (includePresets)
            {
                GuiPresetOptimizationsTest.RunTest(ref total, ref passed);
            }
        }
    }
}
