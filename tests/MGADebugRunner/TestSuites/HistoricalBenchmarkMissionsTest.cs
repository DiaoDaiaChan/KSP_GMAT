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

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var res = MGABenchmarkSuite.RunBenchmark(testCase);
                    sw.Stop();

                    bool casePassed = res.Success && res.DeltaVWithinTolerance && res.FlightDaysWithinTolerance && res.TopologyMatchesHistoric && res.Solution != null;
                    if (casePassed) passed++;

                    var sol = res.Solution;
                    double totalDv = res.ComputedTotalDV;
                    double flightDays = res.ComputedFlightDays;

                    if (sol != null)
                    {
                        double pNorm = (sol.PrimerVector != null && sol.PrimerVector.MaxPMag > 0) ? sol.PrimerVector.MaxPMag : 1.0;
                        Console.WriteLine($"✅ 求解结果: {(casePassed ? "【PASS 物理全闭环收敛且严格符合历史】" : "【FAIL 指标超限或拓扑异化】")} | 耗时: {sw.Elapsed.TotalSeconds:F2} 秒");
                        Console.WriteLine($"• 始发历元: {GMATClient.FormatUtcGregorian(sol.DepartureUT)} (UT {sol.DepartureUT:F0})");
                        Console.WriteLine($"• 总速度增量 ΔV: {totalDv:F1} m/s (历史允许范围: {testCase.HistoricTotalDeltaVMinMs:F0} ~ {testCase.HistoricTotalDeltaVMaxMs:F0} m/s) -> {(res.DeltaVWithinTolerance ? "✅ 合规" : "❌ 超标")}");
                        Console.WriteLine($"• 飞行总天数: {flightDays:F1} 天 ({flightDays / 365.25:F2} 年 | 历史范围: {testCase.HistoricFlightDaysMin:F0} ~ {testCase.HistoricFlightDaysMax:F0} 天) -> {(res.FlightDaysWithinTolerance ? "✅ 合规" : "❌ 严重超标")}");
                        Console.WriteLine($"• 借力拓扑构型: {(res.TopologyMatchesHistoric ? "✅ 吻合历史" : "❌ 发生异化/非标插入")}");
                        Console.WriteLine($"• 冲量详细构成:");
                        Console.WriteLine($"    - 始发脱离脉冲 (Launch ΔV):     {sol.LaunchDeltaVKms * 1000.0,8:F1} m/s (C3 = {(sol.Legs != null && sol.Legs.Count > 0 ? sol.Legs[0].C3Kms2.ToString("F2") : "0.00")} km²/s²)");
                        Console.WriteLine($"    - 深空中途机动 (DSM ΔV):        {sol.TotalDsmDeltaVKms * 1000.0,8:F1} m/s");
                        Console.WriteLine($"    - 飞掠偏折脉冲 (Flyby ΔV):      {sol.TotalFlybyDeltaVKms * 1000.0,8:F1} m/s (无动力合规: {sol.TotalFlybyDeltaVKms < 1e-4})");
                        Console.WriteLine($"    - 目标捕获制动 (Insertion ΔV):  {sol.InsertionDeltaVKms * 1000.0,8:F1} m/s");
                        Console.WriteLine($"• 劳登主矢量极值 (|p| Norm): {pNorm:F3} (最优性判据: {(pNorm <= 1.05 ? "严格冲量最优" : (pNorm <= 1.15 ? "次优" : "非最优"))})");

                        if (sol.Legs != null && sol.Legs.Count > 0)
                        {
                            Console.WriteLine($"• 各航段精细动力学参数 (共 {sol.Legs.Count} 段):");
                            for (int legIdx = 0; legIdx < sol.Legs.Count; legIdx++)
                            {
                                var leg = sol.Legs[legIdx];
                                string clearanceStr = leg.FlybyPeriapsisAltitudeKm > 0 ? $"{leg.FlybyPeriapsisAltitudeKm:F1} km" : "-";
                                Console.WriteLine($"    [Leg #{legIdx + 1}] {leg.FromBody,-7} ➔ {leg.ToBody,-8} | 航时={leg.ToFDays,6:F1}d | 脉冲={leg.DeltaVKms * 1000.0,6:F1} m/s | C3={leg.C3Kms2,7:F2} km²/s² | 借力近拱净空={clearanceStr}");
                                ctx?.Log($"Leg #{legIdx + 1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, ΔV={leg.DeltaVKms * 1000.0:F1}m/s, C3={leg.C3Kms2:F2}, PeAlt={clearanceStr}");
                            }
                        }


                        if (!casePassed)
                        {
                            var failReasons = new List<string>();
                            if (!res.DeltaVWithinTolerance) failReasons.Add($"总ΔV超出历史容差({totalDv:F1} m/s vs {testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0} m/s)");
                            if (!res.FlightDaysWithinTolerance) failReasons.Add($"总航时严重超标({flightDays:F1}天 vs 历史参考 {testCase.HistoricFlightDaysMin:F0}~{testCase.HistoricFlightDaysMax:F0}天)");
                            if (!res.TopologyMatchesHistoric) failReasons.Add("拓扑构型与历史任务不符(发生插序/非标共振)");
                            if (!res.VectorContinuityPassed) failReasons.Add("位置矢量不连续");
                            if (!res.FlybyFeasibilityPassed) failReasons.Add("借力飞掠超出物理界限");
                            ctx?.Fail(string.Join("; ", failReasons));
                        }

                        if (ctx != null)
                        {
                            ctx.RecordMetric("分类", cat.Name);
                            ctx.RecordMetric("始发历元", GMATClient.FormatUtcGregorian(sol.DepartureUT));
                            ctx.RecordMetric("计算总ΔV", $"{totalDv:F1} m/s");
                            ctx.RecordMetric("历史参考ΔV", $"{testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0} m/s");
                            ctx.RecordMetric("ΔV判定", res.DeltaVWithinTolerance ? "✅ 合规" : "❌ 超标");
                            ctx.RecordMetric("总航时", $"{flightDays:F1} 天");
                            ctx.RecordMetric("历史航时", $"{testCase.HistoricFlightDaysMin:F0}~{testCase.HistoricFlightDaysMax:F0} 天");
                            ctx.RecordMetric("航时判定", res.FlightDaysWithinTolerance ? "✅ 合规" : "❌ 严重超标");
                            ctx.RecordMetric("拓扑判定", res.TopologyMatchesHistoric ? "✅ 吻合" : "❌ 异化/插入");
                            ctx.RecordMetric("始发脉冲", $"{sol.LaunchDeltaVKms * 1000.0:F1} m/s");
                            ctx.RecordMetric("DSM机动", $"{sol.TotalDsmDeltaVKms * 1000.0:F1} m/s");
                            ctx.RecordMetric("借力脉冲", $"{sol.TotalFlybyDeltaVKms * 1000.0:F1} m/s");
                            ctx.RecordMetric("捕获制动", $"{sol.InsertionDeltaVKms * 1000.0:F1} m/s");
                            ctx.RecordMetric("劳登最优|p|", $"{pNorm:F3}");
                            ctx.RecordMetric("航段数", sol.Legs != null ? sol.Legs.Count.ToString() : "1");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ 求解失败: 未能生成有效解！");
                        ctx?.Fail($"未能生成有效解: Continuity={res.VectorContinuityPassed}, Feasibility={res.FlybyFeasibilityPassed}");
                    }

                    ctx?.EndCase(casePassed);
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
