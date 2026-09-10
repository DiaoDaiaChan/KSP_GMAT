using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;

namespace MGADebugRunner.Modules
{
    public class AutonomousMGATestModule : ITestModule
    {
        public string ModuleId => "autonomous_mga";
        public string Name => "全自动 MGA 与超大广度拓扑智能搜索模块 (Autonomous MGA & Wide Search)";
        public string Description => "验证零人工干预水星/天王星极深势阱自主演化、超大广度候选拓扑并发打擂、帕累托最省/最快双轨决策及全航段动力学可行性闭环。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            // -------------------------------------------------------------
            // 用例 1: 零人工干预全自动飞掠寻优 (水星 & 天王星)
            // -------------------------------------------------------------
            ctx.StartCase("Autonomous_MercuryAndUranus");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🚀 [用例集 N] 零人工干预全自动飞掠寻优: 地球 ➔ 水星 (极深势阱) 与 地球 ➔ 天王星 (深空远外行星)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double epoch1951 = GMATClient.ParseUtcGregorian("10 Feb 1951 00:00:00.000");

                Console.WriteLine("\n--- [任务 1: 地球 ➔ 水星 (Earth ➔ Mercury) - 零人工干预端到端全自动求解] ---");
                var rMercury = MGAAdaptiveSolver.SolveAutonomous(
                    departurePlanet: "Earth",
                    arrivalPlanet: "Mercury",
                    departureEpochUT: epoch1951,
                    searchSpanDays: 365.25,
                    maxCandidates: 6
                );

                if (rMercury.BestSolution == null || rMercury.CandidateChains.Count == 0)
                {
                    Console.WriteLine("❌ 水星全自动求解未能收敛出有效方案！");
                    ctx.Fail("水星全自动求解未收敛");
                    allOk = false;
                }
                else
                {
                    var bMerc = rMercury.BestSolution;
                    double mercDepDv = bMerc.LaunchDeltaVKms * 1000.0;
                    double mercTotDv = bMerc.TotalDeltaVKms * 1000.0;
                    Console.WriteLine($"  [1.1] 水星自主推荐最佳借力拓扑: Earth ➔ {bMerc.FlybyChainSummary}");
                    Console.WriteLine($"  [1.2] 水星最优解物理指标: 发射历元={GMATClient.FormatUtcGregorian(bMerc.DepartureUT)}, 始发ΔV={mercDepDv:F1} m/s, 总机动={mercTotDv:F1} m/s");
                    ctx.RecordMetric("水星推荐链路", bMerc.FlybyChainSummary);
                    ctx.RecordMetric("水星最优总ΔV", $"{mercTotDv:F1} m/s");
                }

                Console.WriteLine("\n--- [任务 2: 地球 ➔ 天王星 (Earth ➔ Uranus) - 深空远外行星巨行星借力自动涌现] ---");
                var rUranus = MGAAdaptiveSolver.SolveAutonomous(
                    departurePlanet: "Earth",
                    arrivalPlanet: "Uranus",
                    departureEpochUT: epoch1951,
                    searchSpanDays: 500.0,
                    maxCandidates: 6
                );

                if (rUranus.BestSolution == null || rUranus.CandidateChains.Count == 0)
                {
                    Console.WriteLine("❌ 天王星全自动求解未能收敛出有效方案！");
                    ctx.Fail("天王星全自动求解未收敛");
                    allOk = false;
                }
                else
                {
                    var bUranus = rUranus.BestSolution;
                    double uDepDv = bUranus.LaunchDeltaVKms * 1000.0;
                    double uTotDv = bUranus.TotalDeltaVKms * 1000.0;
                    Console.WriteLine($"  [2.1] 天王星自主推荐最佳借力拓扑: Earth ➔ {bUranus.FlybyChainSummary}");
                    Console.WriteLine($"  [2.2] 天王星最优解物理指标: 发射历元={GMATClient.FormatUtcGregorian(bUranus.DepartureUT)}, 始发ΔV={uDepDv:F1} m/s, 总机动={uTotDv:F1} m/s");
                    ctx.RecordMetric("天王星推荐链路", bUranus.FlybyChainSummary);
                    ctx.RecordMetric("天王星最优总ΔV", $"{uTotDv:F1} m/s");
                    Console.WriteLine("\n✅ [用例集 N] 水星与天王星零人工干预全自动求解 100% 完美通过！\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 自动飞掠寻优异常: {ex.Message}\n{ex.StackTrace}");
                ctx.Fail($"自动飞掠寻优异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 2: 超大广度拓扑智能搜索与序列直接应用实测
            // -------------------------------------------------------------
            ctx.StartCase("WideSearch_Neptune_DirectApplication");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 P] 超大广度拓扑智能搜索、最省/最快帕累托决策与序列直接应用实际求解全流程实测");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double epoch1952 = GMATClient.ParseUtcGregorian("01 Jan 1952 00:00:00.000");

                Console.WriteLine("  • 测试任务: 地球出发前往深远外太阳系海王星 (Earth ➔ Neptune，跨度 >30 AU)");
                Console.WriteLine("  • 搜索策略: 敞开超大广度拓扑候选池 (maxCandidates = 8 组复杂高能中继拓扑并发打擂)");

                var rNeptune = MGAAdaptiveSolver.SolveAutonomous(
                    departurePlanet: "Earth",
                    arrivalPlanet: "Neptune",
                    departureEpochUT: epoch1952,
                    searchSpanDays: 780.0,
                    maxCandidates: 8
                );

                if (rNeptune.BestSolution == null)
                {
                    Console.WriteLine("❌ 超大广度搜索未收敛出有效解！");
                    ctx.Fail("超大广度搜索未收敛出有效解");
                    allOk = false;
                }
                else
                {
                    var bf = rNeptune.BestFuelSolution ?? rNeptune.BestSolution;
                    var bt = rNeptune.BestTimeSolution ?? rNeptune.BestSolution;

                    ctx.RecordMetric("最省燃料方案", bf.FlybyChainSummary);
                    ctx.RecordMetric("最省总机动ΔV", $"{bf.TotalDeltaVKms * 1000:F1} m/s");
                    ctx.RecordMetric("最省全程飞行", $"{bf.TotalFlightDays:F1} 天");
                    ctx.RecordMetric("航段可行性", bf.AllLegsFeasible ? "PASS" : "FAIL");

                    Console.WriteLine($"\n  🏆 最省燃料方案: Earth ➔ {bf.FlybyChainSummary} | 总ΔV={bf.TotalDeltaVKms * 1000:F1} m/s | 历时={bf.TotalFlightDays:F1}d");
                    Console.WriteLine($"  ⚡ 最快到达方案: Earth ➔ {bt.FlybyChainSummary} | 总ΔV={bt.TotalDeltaVKms * 1000:F1} m/s | 历时={bt.TotalFlightDays:F1}d");

                    // 验证航段可行性
                    if (!bf.AllLegsFeasible)
                    {
                        Console.WriteLine("❌ 最优解存在不可行航段！");
                        ctx.Fail("最优解存在不可行航段");
                        allOk = false;
                    }
                    else
                    {
                        Console.WriteLine("  ✅ 最优解所有航段均为物理可行解！");
                        Console.WriteLine("\n✅ [用例集 P] 超大广度拓扑智能搜索与序列直接应用 100% 验证通过！\n");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 超大广度测试异常: {ex.Message}\n{ex.StackTrace}");
                ctx.Fail($"超大广度测试异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return allOk;
        }
    }
}
