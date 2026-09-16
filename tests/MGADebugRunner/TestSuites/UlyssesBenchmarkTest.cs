using System;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;

namespace MGADebugRunner.TestSuites
{
    public class UlyssesBenchmarkTest : ITestModule
    {
        public string ModuleId => "ulysses_bench";
        public string Name => "Ulysses II 历史基准单项调试测试";
        public string Description => "定向验证 Ulysses II 任务在全域搜索下的开普勒与数值收敛";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("Ulysses_II_Benchmark");
            var testCase = MGABenchmarkSuite.MGA_Missions.FirstOrDefault(m => m.MissionName.Contains("Ulysses"));
            if (testCase == null)
            {
                ctx.Fail("未在 MGA_Missions 中找到 Ulysses II 用例");
                ctx.EndCase(false);
                return false;
            }

            var res = MGABenchmarkSuite.RunBenchmark(testCase);
            Console.WriteLine($"Success: {res.Success}");
            Console.WriteLine($"ComputedTotalDV: {res.ComputedTotalDV:F1} m/s (历史范围: {testCase.HistoricTotalDeltaVMinMs}~{testCase.HistoricTotalDeltaVMaxMs})");
            Console.WriteLine($"ComputedFlightDays: {res.ComputedFlightDays:F1} d");
            Console.WriteLine($"DeltaVWithinTolerance: {res.DeltaVWithinTolerance}");
            Console.WriteLine($"VectorContinuityPassed: {res.VectorContinuityPassed}");
            Console.WriteLine($"FlybyFeasibilityPassed: {res.FlybyFeasibilityPassed}");
            if (res.Solution != null && res.Solution.Legs != null)
            {
                for (int i = 0; i < res.Solution.Legs.Count; i++)
                {
                    var leg = res.Solution.Legs[i];
                    Console.WriteLine($"  Leg {i + 1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, TransDV={leg.DeltaVKms * 1000:F1}m/s, C3={leg.C3Kms2:F2}");
                }
            }

            bool ok = res.Success && res.DeltaVWithinTolerance;
            ctx.RecordMetric("TotalDV", res.ComputedTotalDV);
            ctx.RecordMetric("FlightDays", res.ComputedFlightDays);
            if (!ok)
            {
                ctx.Fail($"Ulysses II 偏差超限: 计算总ΔV={res.ComputedTotalDV:F1} m/s (历史范围: {testCase.HistoricTotalDeltaVMinMs}~{testCase.HistoricTotalDeltaVMaxMs})");
            }
            ctx.EndCase(ok);
            return ok;
        }
    }
}

