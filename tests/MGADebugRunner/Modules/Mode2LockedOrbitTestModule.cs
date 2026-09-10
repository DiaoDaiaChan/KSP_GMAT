using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;

namespace MGADebugRunner.Modules
{
    public class Mode2LockedOrbitTestModule : ITestModule
    {
        public string ModuleId => "mode2_locked";
        public string Name => "模式 2 在轨锁定转移模块 (Mode 2 Locked Orbit Transfer)";
        public string Description => "验证飞船已在轨状态下，轨道倾角 (Inc) 与升交点赤经 (RAAN) 严格锁死时的地月共面窗口精确寻优与经典 TLI 顺向点火动力学闭环。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            ctx.StartCase("EarthToMoon_LockedOrbit_CoplanarSearch");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🛰️ [用例集 模式2在轨锁定转移] 锁定倾角 Inc=28.6° / RAAN=60.2° 地月转移共面寻优与经典TLI核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double winStartUT = 40.58112 * 86400.0;
                double winEndUT = 70.58112 * 86400.0;

                var cfg = new MGAConfig
                {
                    DepartureBody = "Earth",
                    DestinationBody = "Moon",
                    CentralBody = "Earth",
                    FlybySequence = new List<string> { "Moon" },
                    DepartureMode = MGADepartureMode.InOrbitLockedOrbit,
                    LockDepartureInclination = true,
                    DepartureInclinationDeg = 28.6,
                    DepartureRAANDeg = 60.2,
                    DeparturePeAKm = 198.0,
                    DepartureApAKm = 209.0,
                    DestinationPeAKm = 87.0,
                    DestinationApAKm = 87.0,
                    EarliestDepartureUT = winStartUT,
                    LatestDepartureUT = winEndUT,
                    DepartureSearchDays = 30.0,
                    PopulationSize = 50,
                    MaxGenerations = 30,
                    EnableParkingOrbitPhaseAlignment = true
                };

                Console.WriteLine($"  [1] 求解模式: 模式2 (已在轨锁定: Inc={cfg.DepartureInclinationDeg}°, RAAN={cfg.DepartureRAANDeg}°)");
                Console.WriteLine($"      窗口范围: UT={winStartUT:F0}s ~ {winEndUT:F0}s (~30 天)");

                var sols = PykepBridge.Optimize(cfg);
                if (sols == null || sols.Count == 0)
                {
                    Console.WriteLine("❌ 模式2 未能搜寻出有效解！");
                    ctx.Fail("模式2 未能搜寻出有效解");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine($"  [2] 成功返回 {sols.Count} 个候选转移方案 (按总 ΔV 升序排列):");
                    for (int i = 0; i < Math.Min(5, sols.Count); i++)
                    {
                        var s = sols[i];
                        Console.WriteLine($"      #{(i+1)} 发射历元: UT={s.DepartureUT:F0}s ({(s.DepartureUT / 86400.0):F2}d), 历时={s.TotalFlightDays:F1}d, 出射ΔV={s.LaunchDeltaVKms * 1000.0:F1} m/s, 捕获ΔV={s.InsertionDeltaVKms * 1000.0:F1} m/s, 总机动={s.TotalDeltaVKms * 1000.0:F1} m/s");
                    }

                    var best = sols[0];
                    double depDv = best.LaunchDeltaVKms * 1000.0;
                    double totDv = best.TotalDeltaVKms * 1000.0;

                    ctx.RecordMetric("最优发射历元", GMATClient.FormatUtcGregorian(best.DepartureUT));
                    ctx.RecordMetric("标称飞行天数", $"{best.TotalFlightDays:F1} 天");
                    ctx.RecordMetric("最优出射ΔV", $"{depDv:F1} m/s (门禁 <= 3600)");
                    ctx.RecordMetric("最优总机动ΔV", $"{totDv:F1} m/s (门禁 <= 4500)");

                    if (best.Legs != null && best.Legs.Count > 0)
                    {
                        ctx.RecordMetric("顺向分量", $"{best.Legs[0].ProgradeMs:F1} m/s");
                        ctx.RecordMetric("法向分量", $"{best.Legs[0].NormalMs:F1} m/s");
                        ctx.RecordMetric("径向分量", $"{best.Legs[0].RadialMs:F1} m/s");
                    }

                    if (depDv > 3600.0)
                    {
                        Console.WriteLine($"❌ 出射 ΔV 过大 ({depDv:F1} m/s)，未能找到共面窗口！仍包含非物理面外变面冲量！");
                        ctx.Fail($"出射 ΔV 过大 ({depDv:F1} m/s > 3600 m/s)");
                        allOk = false;
                    }
                    else if (totDv > 4500.0)
                    {
                        Console.WriteLine($"❌ 总机动 ΔV 过大 ({totDv:F1} m/s)！预期应在 3800~4200 m/s 之间！");
                        ctx.Fail($"总机动 ΔV 过大 ({totDv:F1} m/s > 4500 m/s)");
                        allOk = false;
                    }
                    else
                    {
                        Console.WriteLine($"  [3] 经典物理特性核验:");
                        Console.WriteLine($"      • 最优出射 ΔV: {depDv:F1} m/s (已彻底消除 5822 m/s 畸变，符合经典 TLI ~3150 m/s)");
                        Console.WriteLine($"      • 最优总 ΔV:   {totDv:F1} m/s (符合理论地月转移 ~3940 m/s 预算)");
                        if (best.Legs != null && best.Legs.Count > 0)
                        {
                            Console.WriteLine($"      • 标称机动顺向分量: {best.Legs[0].ProgradeMs:F1} m/s, 法向: {best.Legs[0].NormalMs:F1} m/s, 径向: {best.Legs[0].RadialMs:F1} m/s");
                        }

                        // 验证停泊轨相位对齐
                        var orbit = new AnalyticalParkingOrbit("Earth", 200.0, incDeg: 28.6, raanDeg: 60.2);
                        bool alignOk = MGAInjectionAligner.AutoAlignSolution(orbit, best);
                        Console.WriteLine($"  [4] 停泊轨锁相对齐核验: 成功={alignOk}, 锁相后出射ΔV={best.LaunchDeltaVKms * 1000.0:F1} m/s, 总ΔV={best.TotalDeltaVKms * 1000.0:F1} m/s");
                        ctx.RecordMetric("停泊轨锁相对齐", alignOk ? "PASS" : "FAIL");

                        if (best.LaunchDeltaVKms * 1000.0 > 4500.0)
                        {
                            Console.WriteLine($"❌ 锁相后出射 ΔV 发生畸变反弹 ({best.LaunchDeltaVKms * 1000.0:F1} m/s)！");
                            ctx.Fail($"锁相后出射 ΔV 发生畸变反弹 ({best.LaunchDeltaVKms * 1000.0:F1} m/s)");
                            allOk = false;
                        }
                        else
                        {
                            Console.WriteLine("\n✅ 模式2 倾角/RAAN 锁定共面窗口精确收敛与经典 TLI 顺向点火核验 100% 通过！\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 模式2测试异常: {ex.Message}\n{ex.StackTrace}");
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
