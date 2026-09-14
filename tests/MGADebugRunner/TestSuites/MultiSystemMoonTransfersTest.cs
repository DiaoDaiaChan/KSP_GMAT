using System;
using System.Collections.Generic;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.TestSuites
{
    public class MultiSystemMoonTransfersTest : ITestModule
    {
        public string ModuleId => "multi_system_moons";
        public string Name => "多行星系统母星到任意卫星转移规划测试";
        public string Description => "验证 Earth->Moon, Jupiter->Europa, Saturn->Titan 母星到卫星转移与猪排图计算";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("MultiSystemMoonTransfers_AllSystems");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("场景总数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"多系统卫星转移测试未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 多行星系统母星到任意卫星转移规划] Earth->Moon, Jupiter->Europa, Saturn->Titan");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                // 场景 1: 地月转移 (Earth -> Moon)
                Console.WriteLine("--- [场景 1: 地月系统 Earth ➔ Moon (地心系)] ---");
                var (depUt1, tof1, dv1) = PykepBridge.CalculateHohmannWindow("Earth", "Moon", 0.0);
                Console.WriteLine($"  • 霍曼窗口求解: 标称飞行={tof1:F2}天, 预估总ΔV={dv1:F1} m/s");
                if (tof1 < 4.0 || tof1 > 6.0 || dv1 < 3500.0 || dv1 > 4500.0)
                {
                    Console.WriteLine($"❌ 地月转移窗口动力学指标异常: tof={tof1}, dv={dv1}");
                    return;
                }

                var cfgEarthMoon = new MGAConfig
                {
                    CentralBody = "Earth",
                    DepartureBody = "Earth",
                    DestinationBody = "Moon",
                    FlybySequence = new List<string> { "Moon" },
                    DeparturePeAKm = 200.0,
                    DepartureApAKm = 200.0,
                    DestinationPeAKm = 100.0,
                    DestinationApAKm = 100.0,
                    EarliestDepartureUT = 0.0,
                    LatestDepartureUT = 86400.0 * 30.0
                };
                var solEM = PykepBridge.BuildFullSolution(new double[] { 0.0, tof1 * 86400.0 }, new List<string> { "Earth", "Moon" }, cfgEarthMoon, false);
                if (solEM == null || solEM.LaunchDeltaVKms <= 0)
                {
                    Console.WriteLine("❌ 地月转移全方案解算失败！");
                    return;
                }
                Console.WriteLine($"  • 地月全方案收敛: 出射ΔV={solEM.LaunchDeltaVKms * 1000:F1} m/s, 捕获ΔV={solEM.InsertionDeltaVKms * 1000:F1} m/s, 总ΔV={solEM.TotalDeltaVKms * 1000:F1} m/s ✅");

                // 场景 2: 木星到木卫二 (Jupiter -> Europa)
                Console.WriteLine("\n--- [场景 2: 木星系统 Jupiter ➔ Europa (木心系)] ---");
                var (depUt2, tof2, dv2) = PykepBridge.CalculateHohmannWindow("Jupiter", "Europa", 0.0);
                Console.WriteLine($"  • 霍曼窗口求解: 标称飞行={tof2:F2}天, 预估总ΔV={dv2:F1} m/s");
                if (tof2 < 0.5 || tof2 > 1.2 || dv2 < 15000.0 || dv2 > 26000.0)
                {
                    Console.WriteLine($"❌ 木卫二转移窗口动力学指标异常: tof={tof2}, dv={dv2}");
                    return;
                }

                var cfgJupEuropa = new MGAConfig
                {
                    CentralBody = "Jupiter",
                    DepartureBody = "Jupiter",
                    DestinationBody = "Europa",
                    FlybySequence = new List<string> { "Europa" },
                    DeparturePeAKm = 500.0,
                    DepartureApAKm = 500.0,
                    DestinationPeAKm = 100.0,
                    DestinationApAKm = 100.0,
                    EarliestDepartureUT = 0.0,
                    LatestDepartureUT = 86400.0 * 10.0
                };
                var solJE = PykepBridge.BuildFullSolution(new double[] { 0.0, tof2 * 86400.0 }, new List<string> { "Jupiter", "Europa" }, cfgJupEuropa, false);
                if (solJE == null || solJE.LaunchDeltaVKms <= 0)
                {
                    Console.WriteLine("❌ 木星-木卫二转移方案解算失败！");
                    return;
                }
                Console.WriteLine($"  • 木星-木卫二全方案收敛: 出射ΔV={solJE.LaunchDeltaVKms * 1000:F1} m/s, 捕获ΔV={solJE.InsertionDeltaVKms * 1000:F1} m/s, 总ΔV={solJE.TotalDeltaVKms * 1000:F1} m/s ✅");

                // 场景 3: 土星到土卫六 (Saturn -> Titan)
                Console.WriteLine("\n--- [场景 3: 土星系统 Saturn ➔ Titan (土心系)] ---");
                var (depUt3, tof3, dv3) = PykepBridge.CalculateHohmannWindow("Saturn", "Titan", 0.0);
                Console.WriteLine($"  • 霍曼窗口求解: 标称飞行={tof3:F2}天, 预估总ΔV={dv3:F1} m/s");
                if (tof3 < 2.5 || tof3 > 5.5 || dv3 < 5000.0 || dv3 > 14000.0)
                {
                    Console.WriteLine($"❌ 土卫六转移窗口动力学指标异常: tof={tof3}, dv={dv3}");
                    return;
                }

                var cfgSaturnTitan = new MGAConfig
                {
                    CentralBody = "Saturn",
                    DepartureBody = "Saturn",
                    DestinationBody = "Titan",
                    FlybySequence = new List<string> { "Titan" },
                    DeparturePeAKm = 1000.0,
                    DepartureApAKm = 1000.0,
                    DestinationPeAKm = 500.0,
                    DestinationApAKm = 500.0,
                    EarliestDepartureUT = 0.0,
                    LatestDepartureUT = 86400.0 * 30.0
                };
                var solST = PykepBridge.BuildFullSolution(new double[] { 0.0, tof3 * 86400.0 }, new List<string> { "Saturn", "Titan" }, cfgSaturnTitan, false);
                if (solST == null || solST.LaunchDeltaVKms <= 0)
                {
                    Console.WriteLine("❌ 土星-土卫六转移方案解算失败！");
                    return;
                }
                Console.WriteLine($"  • 土星-土卫六全方案收敛: 出射ΔV={solST.LaunchDeltaVKms * 1000:F1} m/s, 捕获ΔV={solST.InsertionDeltaVKms * 1000:F1} m/s, 总ΔV={solST.TotalDeltaVKms * 1000:F1} m/s ✅");

                // 场景 4: 猪排图计算跨行星系母星->卫星
                Console.WriteLine("\n--- [场景 4: 猪排图引擎跨行星系母星->卫星网格计算] ---");
                var porkchop = PorkchopGenerator.ComputePorkchop("Jupiter", "Europa", 0.0, 5.0, 0.5, 1.2, 500.0, 100.0, false, 10, 10);
                if (porkchop == null || porkchop.BestTotalDV <= 0 || double.IsNaN(porkchop.BestTotalDV))
                {
                    Console.WriteLine("❌ 木星-木卫二猪排图生成失败！");
                    return;
                }
                Console.WriteLine($"  • 木星-木卫二 10x10 网格计算成功: 最优总ΔV={porkchop.BestTotalDV:F1} m/s, 最优飞行={porkchop.BestToFDays:F2}天 ✅");

                passed++;
                Console.WriteLine("\n✅ 多行星系统母星到任意卫星转移规划 (Earth->Moon, Jupiter->Europa, Saturn->Titan) 100% 验证通过！\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 多行星系统母星到任意卫星转移测试异常: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
