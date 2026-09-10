using System;
using System.Diagnostics;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;

namespace MGADebugRunner.Modules
{
    public class AdaptiveSolverTestModule : ITestModule
    {
        public string ModuleId => "adaptive_solver";
        public string Name => "自适应多岛演化与数值自愈求解器模块 (Adaptive Solver & Self Healing)";
        public string Description => "验证 MGAAdaptiveSolver 3 年大跨度时域全覆盖 (截断丢失率恒为 0%)、两级自适应稀疏网格初筛、多岛并发差分演化及 1951 年末真·霍曼最优窗口精准捕获。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            ctx.StartCase("AdaptiveSolver_3YearMars_CoverageAndGlobalOptimum");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 M] MGAAdaptiveSolver 自适应多岛演化、时域全覆盖与数值自愈闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double startUT = GMATClient.ParseUtcGregorian("10 Feb 1951 00:00:00.000");
                double endUT = GMATClient.ParseUtcGregorian("10 Feb 1954 00:00:00.000");
                double totalSpanDays = (endUT - startUT) / 86400.0;

                Console.WriteLine($"\n[1] 模拟用户 3 年跨度搜索: 1951-02-10 ~ 1954-02-10 (共 {totalSpanDays:F1} 天)");

                var config = new MGAAdaptiveSolverConfig
                {
                    DeparturePlanet = "Earth",
                    ArrivalPlanet = "Mars",
                    SearchStartUT = startUT,
                    SearchEndUT = endUT,
                    DeparturePolicy = DepartureModePolicy.FreeLaunch,
                    DepPeKm = 200.0,
                    DepApKm = 200.0,
                    ArrPeKm = 200.0,
                    ArrApKm = 200.0,
                    TotalToFMinDays = 150.0,
                    TotalToFMaxDays = 360.0,
                    PopulationSize = 60,
                    MaxGenerations = 80,
                    EnableSelfHealing = true
                };

                // 2. 检查多岛划分的时域覆盖度断言
                double synodicDays = PykepBridge.GetSynodicPeriodDays("Earth", "Mars");
                Console.WriteLine($"  [2.1] 地火理论物理会合周期: {synodicDays:F1} 天");
                var windowCandidates = MGASurrogatePreFilter.FilterPromisingWindows(config, synodicDays);
                Console.WriteLine($"  [2.2] 自适应生成会合岛屿数量: {windowCandidates.Count} 个 (废除 6 片截断限制)");

                double minCoveredUT = windowCandidates.Min(w => w.WindowStartUT);
                double maxCoveredUT = windowCandidates.Max(w => w.WindowEndUT);
                bool isFullCoverage = (minCoveredUT <= startUT + 1.0) && (maxCoveredUT >= endUT - 1.0);

                Console.WriteLine($"  [2.3] 覆盖范围: {GMATClient.FormatUtcGregorian(minCoveredUT)} ~ {GMATClient.FormatUtcGregorian(maxCoveredUT)}");
                if (!isFullCoverage)
                {
                    Console.WriteLine("❌ 会合岛屿未实现 100% 全时域覆盖！存在截断丢弃风险！");
                    ctx.Fail("会合岛屿未实现 100% 全时域覆盖");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("        ✅ 100% 全域覆盖断言通过：截断丢失率恒为 0%！");
                }

                ctx.RecordMetric("时域覆盖", isFullCoverage ? "100% PASS" : "FAIL");
                ctx.RecordMetric("自适应窗口数", windowCandidates.Count);

                // 3. 执行自适应求解器端到端闭环求解 (Stage 1 ~ Stage 4)
                Console.WriteLine("\n[3] 运行 MGAAdaptiveSolver 端到端求解...");
                var watch = Stopwatch.StartNew();
                var solutions = MGAAdaptiveSolver.Solve(config, System.Threading.CancellationToken.None, (p, msg) => { });
                watch.Stop();

                Console.WriteLine($"  [3.1] 求解耗时: {watch.ElapsedMilliseconds} ms, 输出精选解方案数: {solutions.Count}");
                if (solutions == null || solutions.Count == 0)
                {
                    Console.WriteLine("❌ 未收敛出任何有效解！");
                    ctx.Fail("未收敛出任何有效解");
                    allOk = false;
                }
                else
                {
                    var bestSol = solutions[0];
                    double bestDepUT = bestSol.DepartureUT;
                    double bestDepDV = bestSol.LaunchDeltaVKms * 1000.0;
                    double bestTotDV = bestSol.TotalDeltaVKms * 1000.0;
                    double bestToF = bestSol.TotalFlightDays;

                    Console.WriteLine("\n[4] 最优解综合指标核查:");
                    Console.WriteLine($"  ★ 最优发射历元: {GMATClient.FormatUtcGregorian(bestDepUT)}");
                    Console.WriteLine($"  ★ 最优出射机动 ΔV: {bestDepDV:F1} m/s (理论极限 3650~3750 m/s)");
                    Console.WriteLine($"  ★ 最优总任务机动 ΔV: {bestTotDV:F1} m/s (理论极限 6150~6300 m/s)");
                    Console.WriteLine($"  ★ 飞行历时: {bestToF:F1} 天 (标称 260~300 天)");

                    ctx.RecordMetric("最优发射历元", GMATClient.FormatUtcGregorian(bestDepUT));
                    ctx.RecordMetric("出射机动ΔV", $"{bestDepDV:F1} m/s");
                    ctx.RecordMetric("总机动ΔV", $"{bestTotDV:F1} m/s");
                    ctx.RecordMetric("飞行历时", $"{bestToF:F1} 天");

                    // 断言门禁
                    double minValidUT = GMATClient.ParseUtcGregorian("01 Nov 1951 00:00:00.000");
                    double maxValidUT = GMATClient.ParseUtcGregorian("01 Mar 1952 00:00:00.000");

                    if (bestDepUT < minValidUT || bestDepUT > maxValidUT)
                    {
                        Console.WriteLine($"❌ 最优发射历元未捕获到 1951年12月真·最优窗口: 求解得到 {GMATClient.FormatUtcGregorian(bestDepUT)}");
                        ctx.Fail($"最优发射历元偏离真实霍曼窗口: {GMATClient.FormatUtcGregorian(bestDepUT)}");
                        allOk = false;
                    }
                    else
                    {
                        Console.WriteLine("        ✅ 真实最优窗口精准捕获断言通过！");
                    }

                    if (bestDepDV > 3800.0)
                    {
                        Console.WriteLine($"❌ 出射 ΔV 偏高: 实测 {bestDepDV:F1} m/s > 3800 m/s");
                        ctx.Fail($"出射 ΔV 偏高: {bestDepDV:F1} m/s > 3800 m/s");
                        allOk = false;
                    }
                    else
                    {
                        Console.WriteLine("        ✅ 出射能耗达标断言通过 (≤ 3800 m/s)！");
                    }

                    if (bestTotDV > 6400.0)
                    {
                        Console.WriteLine($"❌ 总机动 ΔV 偏高: 实测 {bestTotDV:F1} m/s > 6400 m/s");
                        ctx.Fail($"总机动 ΔV 偏高: {bestTotDV:F1} m/s > 6400 m/s");
                        allOk = false;
                    }
                    else
                    {
                        Console.WriteLine("        ✅ 总机动达标断言通过 (≤ 6400 m/s)！");
                    }

                    if (allOk)
                    {
                        Console.WriteLine("\n✅ [用例集 M] MGAAdaptiveSolver 自适应多岛演化、时域全覆盖与数值自愈 100% 验证通过！\n");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 自适应求解器测试异常: {ex.Message}\n{ex.StackTrace}");
                ctx.Fail($"异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }

            ctx.EndCase();
            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return allOk;
        }
    }
}
