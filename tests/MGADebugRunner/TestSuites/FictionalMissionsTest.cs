using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    public class FictionalMissionsTest : ITestModule
    {
        public string ModuleId => "fictional_missions";
        public string Name => "虚构行星际引力助推极限探测测试套件";
        public string Description => "验证虚构行星际引力助推极限任务动力学求解 (Solar Chronos, Aldrin Cycler, Ulysses II, Kronos, Pioneer)";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("FictionalMissions_Optimization");
            int total = 0, passed = 0;
            RunAllFictionalMissions(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("虚构任务数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"虚构任务求解未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunAllFictionalMissions(ref int total, ref int passed)
        {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌌 [用例集 F] 虚构行星际引力助推极限探测深度验证 (Fictional MGA Missions)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var fictionalMissions = MGABenchmarkSuite.StandardFictionalMissions;

            foreach (var testCase in fictionalMissions)
            {
                total++;
                Console.Write($"• [虚构任务] {testCase.MissionName,-40} ... ");

                try
                {
                    double startUT = testCase.HistoricLaunchUT;
                    if (startUT <= 0) startUT = GMATClient.ParseUtcGregorian(testCase.HistoricLaunchDateStr);

                    string firstTarget = testCase.FlybySequence[0];
                    double targetInc = testCase.LockDepartureInclination ? testCase.DepIncDeg : -1.0;
                    var (optUT, tof1, approxDV) = PykepBridge.CalculateHohmannWindow(testCase.DepartureBody, firstTarget, startUT, 0, targetInc);

                    double baseUT = double.IsNaN(optUT) ? startUT : optUT;
                    double baseTof1 = double.IsNaN(tof1) ? testCase.HistoricToFDays[0] : tof1;

                    var config = new MGAConfig
                    {
                        DepartureBody = testCase.DepartureBody,
                        DestinationBody = testCase.FlybySequence.Last(),
                        FlybySequence = new List<string>(testCase.FlybySequence),
                        EarliestDepartureUT = Math.Max(0, baseUT - 86400.0 * 25.0),
                        LatestDepartureUT = baseUT + 86400.0 * 25.0,
                        DepartureAltitudeKm = testCase.DepAltKm,
                        DestinationAltitudeKm = testCase.ArrAltKm,
                        DepartureInclinationDeg = testCase.DepIncDeg,
                        LockDepartureInclination = testCase.LockDepartureInclination,
                        NoInsertionBurn = testCase.NoInsertionBurn,
                        EngineMode = testCase.EngineMode,
                        DsmEtaDefault = testCase.DsmEtaDefault,
                        PopulationSize = 50,
                        MaxGenerations = 35
                    };

                    config.MinToFDays = new List<double>();
                    config.MaxToFDays = new List<double>();
                    for (int k = 0; k < testCase.FlybySequence.Count; k++)
                    {
                        double nominalTof = (k == 0) ? baseTof1 : (k < testCase.HistoricToFDays.Count ? testCase.HistoricToFDays[k] : 200.0);
                        config.MinToFDays.Add(Math.Max(15.0, nominalTof * 0.70));
                        config.MaxToFDays.Add(nominalTof * 1.35);
                    }

                    var sols = PykepBridge.Optimize(config);

                    if (sols != null && sols.Count > 0)
                    {
                        var sol = sols[0];
                        double totalDv = sol.TotalDeltaVKms * 1000.0;
                        double days = sol.TotalFlightDays;
                        bool feasible = sol.AllLegsFeasible;
                        double launchDv = sol.LaunchDeltaVKms * 1000.0;
                        double inc = sol.Legs[0].EjectionInclinationDeg;

                        passed++;
                        Console.WriteLine($"✅ 求解成功! 候选解={sols.Count} | 最优总ΔV={totalDv,6:F0} m/s (出射={launchDv:F0}) | 历时={days,5:F0}d | 逃逸倾角={inc:F1}° | 借力闭合={feasible}");
                    }
                    else
                    {
                        Console.WriteLine("❌ 寻优求解未收敛");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 求解异常: {ex.Message}");
                }
            }
        }

        public static void RunOptimizeFictional()
        {
            Console.WriteLine("====================================================================");
            Console.WriteLine("🚀 正在验证经过真实天体物理校准的 5 大虚构深空任务...");
            Console.WriteLine("====================================================================");

            var cases = new List<MGABenchmarkSuite.BenchmarkCase>
            {
                new MGABenchmarkSuite.BenchmarkCase
                {
                    MissionName = "Solar Chronos (太阳时空神号, 2029)",
                    Description = "【虚构深空】内太阳系金星引力弹弓降能，高效实施水星近心点轨道借力交会 (E-V-M)",
                    DepartureBody = "Earth",
                    FlybySequence = new List<string> { "Venus", "Mercury" },
                    HistoricLaunchDateStr = "18 Nov 2029 14:00:00",
                    HistoricLaunchUT = GMATClient.ParseUtcGregorian("18 Nov 2029 14:00:00"),
                    HistoricTotalDeltaVMinMs = 4000.0,
                    HistoricTotalDeltaVMaxMs = 9500.0,
                    HistoricFlightDaysMin = 200.0,
                    HistoricFlightDaysMax = 250.0,
                    HistoricToFDays = new List<double> { 111.0, 103.0 },
                    DepIncDeg = 28.6,
                    NoInsertionBurn = true
                },
                new MGABenchmarkSuite.BenchmarkCase
                {
                    MissionName = "Aldrin Cycler (奥尔德林地火循环飞船, 2033)",
                    Description = "【虚构深空】巴兹·奥尔德林构想的星际穿梭巴士，火星天然引力弹弓零机动往返穿梭 (E-M-E)",
                    DepartureBody = "Earth",
                    FlybySequence = new List<string> { "Mars", "Earth" },
                    HistoricLaunchDateStr = "28 Apr 2033 15:30:00",
                    HistoricLaunchUT = GMATClient.ParseUtcGregorian("28 Apr 2033 15:30:00"),
                    HistoricTotalDeltaVMinMs = 3200.0,
                    HistoricTotalDeltaVMaxMs = 5000.0,
                    HistoricFlightDaysMin = 900.0,
                    HistoricFlightDaysMax = 1000.0,
                    HistoricToFDays = new List<double> { 274.0, 689.0 },
                    DepIncDeg = 28.6,
                    NoInsertionBurn = true
                },
                new MGABenchmarkSuite.BenchmarkCase
                {
                    MissionName = "Ulysses II (尤利西斯II号 太阳极区俯瞰, 2030)",
                    Description = "【虚构深空】锁定 28.6°(KSC) 发射逃逸倾角，在木星实施深空垂直面外偏转，直冲太阳极轨 (E-J)",
                    DepartureBody = "Earth",
                    FlybySequence = new List<string> { "Jupiter" },
                    HistoricLaunchDateStr = "20 Jan 2030 23:30:00",
                    HistoricLaunchUT = GMATClient.ParseUtcGregorian("20 Jan 2030 23:30:00"),
                    HistoricTotalDeltaVMinMs = 6000.0,
                    HistoricTotalDeltaVMaxMs = 7500.0,
                    HistoricFlightDaysMin = 800.0,
                    HistoricFlightDaysMax = 900.0,
                    HistoricToFDays = new List<double> { 841.0 },
                    DepIncDeg = 28.6,
                    NoInsertionBurn = true
                },
                new MGABenchmarkSuite.BenchmarkCase
                {
                    MissionName = "Kronos-Poseidon (泰坦-海神双星巡航, 2030)",
                    Description = "【虚构深空】木星超级引力弹弓无动力借力偏转，直奔外层冰巨星天王星 (E-J-U)",
                    DepartureBody = "Earth",
                    FlybySequence = new List<string> { "Jupiter", "Uranus" },
                    HistoricLaunchDateStr = "20 Jan 2030 23:30:00",
                    HistoricLaunchUT = GMATClient.ParseUtcGregorian("20 Jan 2030 23:30:00"),
                    HistoricTotalDeltaVMinMs = 6000.0,
                    HistoricTotalDeltaVMaxMs = 8000.0,
                    HistoricFlightDaysMin = 5000.0,
                    HistoricFlightDaysMax = 5500.0,
                    HistoricToFDays = new List<double> { 841.0, 4500.0 },
                    DepIncDeg = 28.6,
                    NoInsertionBurn = true
                },
                new MGABenchmarkSuite.BenchmarkCase
                {
                    MissionName = "Interstellar Pioneer (星际先驱号 太阳系边界逃逸, 2030)",
                    Description = "【虚构深空】木星超级引力弹弓连珠加速转向土星，以极高双曲速度冲出太阳系日球层 (E-J-S)",
                    DepartureBody = "Earth",
                    FlybySequence = new List<string> { "Jupiter", "Saturn" },
                    HistoricLaunchDateStr = "20 Jan 2030 23:30:00",
                    HistoricLaunchUT = GMATClient.ParseUtcGregorian("20 Jan 2030 23:30:00"),
                    HistoricTotalDeltaVMinMs = 6000.0,
                    HistoricTotalDeltaVMaxMs = 8000.0,
                    HistoricFlightDaysMin = 2200.0,
                    HistoricFlightDaysMax = 2500.0,
                    HistoricToFDays = new List<double> { 841.0, 1450.0 },
                    DepIncDeg = 28.6,
                    NoInsertionBurn = true
                }
            };

            foreach (var bCase in cases)
            {
                var res = MGABenchmarkSuite.RunBenchmark(bCase);
                Console.WriteLine($"\n任务: {bCase.MissionName}");
                Console.WriteLine($"  总 ΔV: {res.ComputedTotalDV:F1} m/s (容差: {bCase.HistoricTotalDeltaVMinMs} ~ {bCase.HistoricTotalDeltaVMaxMs})");
                Console.WriteLine($"  借力可行性通过: {res.FlybyFeasibilityPassed}, 矢量连续性: {res.VectorContinuityPassed}, 容差核验: {res.DeltaVWithinTolerance}");
                for (int i = 0; i < res.Solution.Legs.Count; i++)
                {
                    var leg = res.Solution.Legs[i];
                    Console.WriteLine($"    Leg {i+1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, 借力DV={leg.FlybyDeltaVKms*1000:F1}m/s, 近心高={leg.FlybyPeriapsisAltitudeKm:F0}km, 可行={leg.IsFeasible}");
                }
            }
        }

        public static void PrintTop(List<MGASolution> sols)
        {
            if (sols == null || sols.Count == 0)
            {
                Console.WriteLine("  无解！");
                return;
            }
            var best = sols[0];
            Console.WriteLine($"  ★ 最优解: 总 ΔV = {best.TotalDeltaVKms * 1000:F1} m/s, 发射 ΔV = {best.LaunchDeltaVKms * 1000:F1} m/s, 飞行 = {best.TotalFlightDays:F1} 天");
            Console.WriteLine($"    发射历元: {GMATClient.FormatUtcGregorian(best.DepartureUT)} (UT = {best.DepartureUT:F1})");
            for (int i = 0; i < best.Legs.Count; i++)
            {
                var leg = best.Legs[i];
                Console.WriteLine($"    Leg {i+1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, 借力DV={leg.FlybyDeltaVKms*1000:F1}m/s, 近心高={leg.FlybyPeriapsisAltitudeKm:F0}km, 可行={leg.IsFeasible}");
            }
        }
    }
}
