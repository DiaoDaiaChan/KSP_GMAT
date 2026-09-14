using System;
using System.Collections.Generic;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    public class DirectHohmannMissionsTest : ITestModule
    {
        public string ModuleId => "hohmann_missions";
        public string Name => "经典直接霍曼转移全收敛测试套件";
        public string Description => "验证游戏内 GUI [AutoSolveDirectHohmann] 经典直接转移系列全收敛";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("DirectHohmann_AllPairs");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("转移对总数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"霍曼转移任务未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🎯 [用例集 C] 游戏内 GUI [AutoSolveDirectHohmann] 经典直接转移系列全收敛验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var transferPairs = new (string from, string to, double startUt, double expectedMaxDv)[] {
                ("Earth", "Mars", EphemerisTimeSystem.J2000_UT, 7000.0),
                ("Earth", "Venus", EphemerisTimeSystem.J2000_UT, 7500.0),
                ("Earth", "Venus", GMATClient.ParseUtcGregorian("02 Feb 1952 20:34:00"), 7500.0),
                ("Earth", "Jupiter", EphemerisTimeSystem.J2000_UT, 26000.0),
                ("Earth", "Mercury", EphemerisTimeSystem.J2000_UT, 15000.0),
                ("Mars", "Earth", EphemerisTimeSystem.J2000_UT + 86400.0 * 300.0, 7000.0)
            };

            foreach (var pair in transferPairs)
            {
                total++;
                Console.Write($"• 求解转移窗口: {pair.from,-6} ➔ {pair.to,-7} ... ");

                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(pair.from, pair.to, pair.startUt);

                if (double.IsNaN(optUT) || tofDays <= 0)
                {
                    Console.WriteLine("❌ 窗口预估发散！");
                    continue;
                }

                var config = new MGAConfig
                {
                    DepartureBody = pair.from,
                    DestinationBody = pair.to,
                    FlybySequence = new List<string> { pair.to },
                    EarliestDepartureUT = Math.Max(0, optUT - 86400.0 * 25.0),
                    LatestDepartureUT = optUT + 86400.0 * 25.0,
                    MinToFDays = new List<double> { Math.Max(15.0, tofDays * 0.75) },
                    MaxToFDays = new List<double> { tofDays * 1.35 },
                    DepartureAltitudeKm = 200.0,
                    DestinationAltitudeKm = 200.0,
                    NoInsertionBurn = false
                };

                var solutions = PykepBridge.Optimize(config);
                if (solutions != null && solutions.Count > 0)
                {
                    var sol = solutions[0];
                    sol.BuildEvents(config);
                    var leg0 = sol.Legs[0];

                    bool hasNaN = double.IsNaN(leg0.ProgradeMs) || double.IsNaN(leg0.NormalMs) || double.IsNaN(leg0.RadialMs) || double.IsNaN(leg0.C3Kms2);
                    double totalDv = sol.TotalDeltaVKms * 1000.0;

                    if (!hasNaN && totalDv > 100.0 && totalDv <= pair.expectedMaxDv * 1.3)
                    {
                        passed++;
                        Console.WriteLine($"✅ 收敛! 总ΔV={totalDv,7:F1} m/s (出射={sol.LaunchDeltaVKms * 1000:F1}, 制动={sol.InsertionDeltaVKms * 1000:F1}) | 飞行={sol.TotalFlightDays,5:F1}d | C3={leg0.C3Kms2,5:F1} km²/s²");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ 收敛但数值异常: NaN={hasNaN}, 总ΔV={totalDv:F1} m/s (上限 {pair.expectedMaxDv})");
                    }
                }
                else
                {
                    Console.WriteLine("❌ 未找到有效解！");
                }
            }
            Console.WriteLine();
        }
    }
}
