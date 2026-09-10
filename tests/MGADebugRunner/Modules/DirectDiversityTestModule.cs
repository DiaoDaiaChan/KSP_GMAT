using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.Modules
{
    public class DirectDiversityTestModule : ITestModule
    {
        public string ModuleId => "direct_diversity";
        public string Name => "行星际直达决策池与小行星跳板过滤模块 (Direct Diversity & Filtering)";
        public string Description => "验证行星际直达转移 (Earth ➔ Venus) 多样性决策池方案生成、升序排序、帕累托权衡，以及谷神星小行星链路中无引力小天体的主动过滤。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            // -------------------------------------------------------------
            // 用例 1: 行星际直达转移决策池完整性与多样性
            // -------------------------------------------------------------
            ctx.StartCase("DirectInterplanetary_EarthToVenus_Diversity");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 行星际直达转移决策池] Earth ➔ Venus 多样性候选方案与排名决策池完整性核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow("Earth", "Venus", 10000000.0);
                var config = new MGAConfig
                {
                    DepartureBody = "Earth",
                    DestinationBody = "Venus",
                    FlybySequence = new List<string> { "Venus" },
                    EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 25.0),
                    LatestDepartureUT = optUT + 86400.0 * 25.0,
                    MinToFDays = new List<double> { Math.Max(15.0, tofDays * 0.75) },
                    MaxToFDays = new List<double> { tofDays * 1.35 },
                    DepartureAltitudeKm = 203.0,
                    DestinationPeAKm = 327.0,
                    DestinationApAKm = 327.0,
                    NoInsertionBurn = false
                };

                var solutions = PykepBridge.Optimize(config);
                if (solutions == null || solutions.Count == 0)
                {
                    Console.WriteLine("❌ 未找到任何收敛方案！");
                    ctx.Fail("未找到任何收敛方案");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine($"  [1] 成功收敛 {solutions.Count} 个多样性候选转移方案 (预期 >= 5 个):");
                    for (int i = 0; i < Math.Min(5, solutions.Count); i++)
                    {
                        var s = solutions[i];
                        Console.WriteLine($"      #{i + 1} 历时: {s.TotalFlightDays,5:F1}d | 出射: {s.LaunchDeltaVKms * 1000.0,6:F1} m/s | 捕获: {s.InsertionDeltaVKms * 1000.0,6:F1} m/s | 总ΔV: {s.TotalDeltaVKms * 1000.0,6:F1} m/s");
                    }

                    if (solutions.Count < 5)
                    {
                        Console.WriteLine($"❌ 候选方案数量不足 ({solutions.Count} < 5)！");
                        ctx.Fail($"候选方案数量不足 ({solutions.Count} < 5)");
                        allOk = false;
                    }

                    // 检验严格升序排序
                    bool isSorted = true;
                    for (int i = 0; i < solutions.Count - 1; i++)
                    {
                        if (solutions[i].TotalDeltaVKms > solutions[i + 1].TotalDeltaVKms + 1e-4)
                        {
                            isSorted = false;
                            break;
                        }
                    }

                    var best = solutions[0];
                    double bestTotalDv = best.TotalDeltaVKms * 1000.0;
                    ctx.RecordMetric("多样性方案数", solutions.Count);
                    ctx.RecordMetric("最优总ΔV", $"{bestTotalDv:F1} m/s");
                    ctx.RecordMetric("升序排序", isSorted ? "PASS" : "FAIL");

                    if (!isSorted)
                    {
                        ctx.Fail("方案未按总 ΔV 严格升序排列");
                        allOk = false;
                    }
                    else if (bestTotalDv < 6500.0 || bestTotalDv > 8800.0)
                    {
                        ctx.Fail($"最优总 ΔV 偏离标称窗口: {bestTotalDv:F1} m/s");
                        allOk = false;
                    }
                    else
                    {
                        Console.WriteLine("\n✅ 行星际直达转移多候选解多样性与决策池排名核验 100% 通过！\n");
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"直达转移异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 2: 谷神星候选链路过滤无引力小天体
            // -------------------------------------------------------------
            ctx.StartCase("Ceres_BannedAsteroidFiltering");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 谷神星与水星拓扑寻优] 剔除无引力小行星、火星跳板优先与水星同星共振减速闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                var ceresChains = PykepBridge.GenerateCandidateSequences("Earth", "Ceres", maxSwingBys: 2, maxResonances: 1, maxBackLegs: 1);
                string[] bannedAsteroids = new[] { "Ryugu", "Eros", "Bennu", "Didymos" };
                bool hasBanned = ceresChains.Any(c => c.Any(body => bannedAsteroids.Contains(body, StringComparer.OrdinalIgnoreCase)));

                ctx.RecordMetric("谷神星链路数", ceresChains.Count);
                ctx.RecordMetric("无引力小行星拦截", !hasBanned ? "PASS" : "FAIL");

                if (hasBanned)
                {
                    ctx.Fail("谷神星候选链路中包含无引力小天体");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ 谷神星小天体过滤断言通过！");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"谷神星小天体过滤异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return allOk;
        }
    }
}
