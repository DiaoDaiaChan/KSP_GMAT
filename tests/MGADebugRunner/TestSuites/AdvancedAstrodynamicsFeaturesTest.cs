using System;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;
using UnityEngine;

namespace MGADebugRunner.TestSuites
{
    public class AdvancedAstrodynamicsFeaturesTest : ITestModule
    {
        public string ModuleId => "advanced_features";
        public string Name => "高级动力学 6 大王牌特性闭环测试套件";
        public string Description => "验证 B-Plane, STM, Primer, MEE, Sims-Flanagan, WSB 流形捕获特性";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("AdvancedAstrodynamics_SixFeatures");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("特性总数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"高级动力学特性测试未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🎯 [用例集 E] 游戏内 GUI 高级动力学 6 大王牌特性 (B-Plane, STM, Primer, MEE, Sims-Flanagan) 全闭环验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            // 特性 1: 解析状态转移矩阵 (Keplerian STM) 与 B-Plane / TCM
            total++;
            try
            {
                Vector3d rTcm = new Vector3d(149598000.0, 0.0, 0.0);
                Vector3d vTcm = new Vector3d(0.0, 29.8, 0.0);
                double dtToEnc = 150.0 * 86400.0;
                Vector3d rEnc = new Vector3d(0.0, 108200000.0, 0.0);
                Vector3d vEnc = new Vector3d(-35.0, 0.0, 0.0);
                double targetBT = 15000.0;
                double targetBR = -2000.0;
                double venusMu = 324859.0;

                var tcm = PykepBridge.CalculateBPlaneTCM(rTcm, vTcm, dtToEnc, rEnc, vEnc, targetBT, targetBR, venusMu);
                if (tcm != null && tcm.DeltaVMs > 0 && !double.IsNaN(tcm.DeltaVMs))
                {
                    passed++;
                    Console.WriteLine($"• [特性 1] B-Plane TCM 穿靶点火求解: ✅ 收敛通过! 总脉冲 ΔV = {tcm.DeltaVMs:F2} m/s (VNC: 切向={tcm.ProgradeMs:F1}, 法向={tcm.NormalMs:F1}, 径向={tcm.RadialMs:F1})");
                }
                else
                {
                    Console.WriteLine("• [特性 1] B-Plane TCM 穿靶点火求解: ❌ 求解异常");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"• [特性 1] B-Plane TCM 异常: {ex.Message}");
            }

            // 特性 2: 劳登主矢量 (Primer Vector Theory) 最优性判定
            total++;
            try
            {
                var pCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("Mariner", StringComparison.OrdinalIgnoreCase) >= 0);
                var pRes = MGABenchmarkSuite.RunBenchmark(pCase);
                if (pRes != null && pRes.Solution != null && pRes.Solution.PrimerVector != null)
                {
                    var pv = pRes.Solution.PrimerVector;
                    passed++;
                    Console.WriteLine($"• [特性 2] 劳登主矢量理论 (Primer Vector): ✅ 收敛通过! 采样点={pv.SampleCount}, 最大模值 |p|={pv.MaxPMag:F4} (<=1.0 最优性: {pv.IsOptimal})");
                }
                else
                {
                    Console.WriteLine("• [特性 2] 劳登主矢量理论 (Primer Vector): ⚠️ 未附加主矢量");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"• [特性 2] 劳登主矢量异常: {ex.Message}");
            }

            // 特性 3: 无奇异修正等分轨道要素 (MEE) 变换
            total++;
            try
            {
                var cCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("Cassini", StringComparison.OrdinalIgnoreCase) >= 0);
                var cRes = MGABenchmarkSuite.RunBenchmark(cCase);
                if (cRes != null && cRes.Solution != null && cRes.Solution.DepartureMEE != null)
                {
                    var mee = cRes.Solution.DepartureMEE;
                    passed++;
                    Console.WriteLine($"• [特性 3] 无奇异修正等分要素 (MEE): ✅ 收敛通过! p={mee.P:E4} km, f={mee.F:F6}, g={mee.G:F6}, L={mee.L:F2}°");
                }
                else
                {
                    Console.WriteLine("• [特性 3] 无奇异修正等分要素 (MEE): ⚠️ 未附加 MEE 要素");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"• [特性 3] MEE 异常: {ex.Message}");
            }

            // 特性 4: Sims-Flanagan 连续低推力打靶模拟
            total++;
            try
            {
                var sCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("BepiColombo", StringComparison.OrdinalIgnoreCase) >= 0);
                var sRes = MGABenchmarkSuite.RunBenchmark(sCase);
                if (sRes != null && sRes.Solution != null && sRes.Solution.LowThrustSolution != null)
                {
                    var lt = sRes.Solution.LowThrustSolution;
                    passed++;
                    Console.WriteLine($"• [特性 4] Sims-Flanagan 连续低推力打靶: ✅ 收敛通过! 节点数={lt.SegmentCount}, 累计消耗 ΔV={lt.TotalDeltaVMs:F1} m/s, 终点质量={lt.FinalMassKg:F1} kg");
                }
                else
                {
                    Console.WriteLine("• [特性 4] Sims-Flanagan 连续低推力打靶: ⚠️ 未附加低推力解");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"• [特性 4] Sims-Flanagan 异常: {ex.Message}");
            }

            // 特性 5: B 平面协方差分析与 3-sigma 误差椭圆 / 导航安全容差
            total++;
            try
            {
                Vector3d r0 = new Vector3d(149598000.0, 0.0, 0.0);
                Vector3d v0 = new Vector3d(0.0, 29.8, 0.0);
                double dtSec = 150.0 * 86400.0;
                Vector3d rEnc = new Vector3d(0.0, 108200000.0, 0.0);
                Vector3d vEnc = new Vector3d(-35.0, 0.0, 0.0);
                double venusMu = 324859.0;
                double venusRadius = 6052.0;

                var bpCov = PykepBridge.CalculateBPlaneCovariance(
                    r0, v0, dtSec, SolarSystemCatalog.SunMu,
                    10.0, 0.00005,
                    rEnc, vEnc, venusMu, venusRadius, 50.0);

                if (bpCov != null && bpCov.SemiMajorAxisSigmaKm > 0 && !double.IsNaN(bpCov.SemiMajorAxisSigmaKm))
                {
                    passed++;
                    Console.WriteLine($"• [特性 5] B 平面协方差与误差椭圆: ✅ 验证通过! 1σ椭圆=({bpCov.SemiMajorAxisSigmaKm:F1} × {bpCov.SemiMinorAxisSigmaKm:F1}) km, 倾角={bpCov.EllipseAngleDeg:F1}°, 3σ安全裕度={bpCov.SafeMargin3SigmaKm:F1} km, 安全评级={bpCov.IsSafeFlyby}, 导航TCM预算={bpCov.NavigationalTcmBudgetKms*1000:F1} m/s");
                }
                else
                {
                    Console.WriteLine("• [特性 5] B 平面协方差与误差椭圆: ❌ 求解异常");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"• [特性 5] B 平面协方差异常: {ex.Message}");
            }

            // 特性 6: 多体引力流形低能捕获与弱稳定性边界 (WSB)
            total++;
            try
            {
                Vector3d rMars = new Vector3d(2.279e8, 0.0, 0.0);
                Vector3d vMars = new Vector3d(0.0, 24.1, 0.0);
                Vector3d rEnc = rMars + new Vector3d(500000.0, 200000.0, 0.0);
                Vector3d vEnc = vMars + new Vector3d(-0.6, 0.5, 0.0);
                double marsMu = 42828.0;
                double marsRadius = 3390.0;

                var manifold = PykepBridge.CalculateManifoldCapture(
                    rEnc, vEnc, rMars, vMars,
                    marsMu, SolarSystemCatalog.SunMu, marsRadius,
                    200.0, 200.0);

                if (manifold != null && manifold.WsbCaptureDeltaVKms > 0 && !double.IsNaN(manifold.WsbCaptureDeltaVKms))
                {
                    passed++;
                    Console.WriteLine($"• [特性 6] 多体引力流形 WSB 低能捕获: ✅ 验证通过! 传统双曲捕获={manifold.ClassicalVisVivaDeltaVKms*1000:F1} m/s, WSB流形捕获={manifold.WsbCaptureDeltaVKms*1000:F1} m/s, 节省ΔV={manifold.DeltaVSavedKms*1000:F1} m/s ({manifold.SavingPercentage:F1}%), Hill球={manifold.HillRadiusKm/1000:F1}k km, Jacobi={manifold.JacobiConstant:F2}");
                }
                else
                {
                    Console.WriteLine("• [特性 6] 多体引力流形 WSB 低能捕获: ❌ 求解异常");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"• [特性 6] 多体引力流形捕获异常: {ex.Message}");
            }

            Console.WriteLine();
        }
    }
}
