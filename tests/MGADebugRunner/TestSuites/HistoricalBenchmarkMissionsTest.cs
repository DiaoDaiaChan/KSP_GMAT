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
        public string Name => "10大历史深空任务全量基准测试套件";
        public string Description => "验证游戏内 GUI [Historical Benchmark Suite] 全部 10 大真实历史飞行任务全收敛";

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
            Console.WriteLine("🪐 [用例集 D] 游戏内 GUI [Historical Benchmark Suite] 全部 10 大真实历史飞行任务全收敛验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var missions = MGABenchmarkSuite.StandardHistoricalMissions;

            foreach (var testCase in missions)
            {
                total++;
                Console.Write($"• [{missions.IndexOf(testCase) + 1:D2}/10] {testCase.MissionName,-40} ... ");

                var res = MGABenchmarkSuite.RunBenchmark(testCase);

                if (res.Success && res.Solution != null)
                {
                    passed++;
                    var sol = res.Solution;
                    Console.WriteLine($"✅ 收敛通过! 航段={sol.Legs.Count} | 计算总ΔV={res.ComputedTotalDV,6:F0} m/s (历史范围: {testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0}) | 历时={res.ComputedFlightDays,4:F0}d");
                }
                else
                {
                    Console.WriteLine($"❌ 未通过: Continuity={res.VectorContinuityPassed}, Feasibility={res.FlybyFeasibilityPassed}, DvTolerance={res.DeltaVWithinTolerance} (计算总ΔV: {res.ComputedTotalDV:F0} m/s)");
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
