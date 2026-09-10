using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.Modules
{
    public class FastSuiteModule : ITestModule
    {
        public string ModuleId => "fast_suite";
        public string Name => "极速秒级回归套件 (Fast Regression Suite)";
        public string Description => "秒级回归 MGA 求解器五大核心物理生命线：粗筛网格覆盖、GPU猪排图回填、模式2锁相TLI、B平面几何与脚本静态审计、SPICE权威霍曼基准。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine("==========================================================================");
            Console.WriteLine("⚡ [Fast Suite] 极速秒级回归测试套件启动");
            Console.WriteLine("==========================================================================");

            bool allOk = true;

            // -------------------------------------------------------------
            // 用例 1: 粗筛两级自适应网格提速与 100% 全时域覆盖
            // -------------------------------------------------------------
            ctx.StartCase("MGASurrogatePreFilter_100PercentCoverageAndSpeed");
            Console.WriteLine("\n[1] 核验 MGASurrogatePreFilter: 两级自适应稀疏网格提速与全时域 100% 覆盖");
            try
            {
                double startUT = GMATClient.ParseUtcGregorian("10 Feb 1951 00:00:00.000");
                double endUT = GMATClient.ParseUtcGregorian("10 Feb 1954 00:00:00.000");
                var cfg = new MGAAdaptiveSolverConfig
                {
                    DeparturePlanet = "Earth",
                    ArrivalPlanet = "Mars",
                    SearchStartUT = startUT,
                    SearchEndUT = endUT,
                    DepPeKm = 200.0, DepApKm = 200.0,
                    ArrPeKm = 200.0, ArrApKm = 200.0
                };

                double synodicDays = PykepBridge.GetSynodicPeriodDays("Earth", "Mars");
                var csw = Stopwatch.StartNew();
                var windows = MGASurrogatePreFilter.FilterPromisingWindows(cfg, synodicDays);
                csw.Stop();

                double minCoveredUT = windows.Count > 0 ? windows[0].WindowStartUT : 0;
                double maxCoveredUT = windows.Count > 0 ? windows[windows.Count - 1].WindowEndUT : 0;
                for (int i = 1; i < windows.Count; i++)
                {
                    if (windows[i].WindowStartUT < minCoveredUT) minCoveredUT = windows[i].WindowStartUT;
                    if (windows[i].WindowEndUT > maxCoveredUT) maxCoveredUT = windows[i].WindowEndUT;
                }
                bool isFullCoverage = (minCoveredUT <= startUT + 1.0) && (maxCoveredUT >= endUT - 1.0);

                ctx.RecordMetric("粗筛耗时", $"{csw.ElapsedMilliseconds} ms");
                ctx.RecordMetric("会合窗口数", windows.Count);
                ctx.RecordMetric("覆盖范围", $"{GMATClient.FormatUtcGregorian(minCoveredUT)} ~ {GMATClient.FormatUtcGregorian(maxCoveredUT)}");
                ctx.RecordMetric("100%全域覆盖", isFullCoverage ? "PASS" : "FAIL");

                ctx.Log($"粗筛耗时: {csw.ElapsedMilliseconds} ms, 窗口数={windows.Count}, 覆盖=[{GMATClient.FormatUtcGregorian(minCoveredUT)} ~ {GMATClient.FormatUtcGregorian(maxCoveredUT)}]");
                Console.WriteLine($"    • 粗筛耗时: {csw.ElapsedMilliseconds} ms (毫秒级极速)");
                Console.WriteLine($"    • 100% 全域覆盖判定: {(isFullCoverage ? "✅ PASS" : "❌ FAIL")}");

                if (!isFullCoverage || csw.ElapsedMilliseconds > 3000)
                {
                    ctx.Fail($"粗筛未实现 100% 连续时域覆盖或耗时超标 ({csw.ElapsedMilliseconds}ms)");
                    allOk = false;
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"用例 1 发生未捕获异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 2: PorkchopGenerator GPU 回填与 ΔV 一致性
            // -------------------------------------------------------------
            ctx.StartCase("PorkchopGenerator_DirectComputeGpuDeltaVConsistency");
            Console.WriteLine("\n[2] 核验 PorkchopGenerator: GPU 计算后 EjectDVGrid 与 InsertDVGrid 解析回填");
            try
            {
                double depStartUT = GMATClient.ParseUtcGregorian("01 Jan 2026 00:00:00.000");
                var porkchop = PorkchopGenerator.ComputePorkchop(
                    "Earth", "Mars",
                    depStartUT, 100.0,
                    150.0, 350.0,
                    200.0, 200.0,
                    flybyOnly: false,
                    gridW: 40, gridH: 40,
                    onProgress: null,
                    forceCpu: false);

                if (porkchop != null)
                {
                    double ejVal = porkchop.EjectDVGrid[10, 10];
                    double inVal = porkchop.InsertDVGrid[10, 10];
                    double totVal = porkchop.DeltaVGrid[10, 10];

                    bool hasEject = !double.IsNaN(ejVal) && ejVal > 1000.0;
                    bool hasInsert = !double.IsNaN(inVal) && inVal >= 0.0;
                    bool dvConsistent = !double.IsNaN(totVal) && Math.Abs(totVal - (ejVal + inVal)) < 1.0;

                    ctx.RecordMetric("计算后端", porkchop.ComputeBackend);
                    ctx.RecordMetric("出射脉冲", $"{ejVal:F1} m/s");
                    ctx.RecordMetric("捕获脉冲", $"{inVal:F1} m/s");
                    ctx.RecordMetric("总机动", $"{totVal:F1} m/s");
                    ctx.RecordMetric("数值一致性闭环", dvConsistent ? "PASS" : "FAIL");

                    Console.WriteLine($"    • 后端: {porkchop.ComputeBackend}, 采样点 [10,10]: Eject={ejVal:F1} m/s, Insert={inVal:F1} m/s, Tot={totVal:F1} m/s");
                    Console.WriteLine($"    • 总机动数值一致性闭环: {(dvConsistent ? "✅ PASS" : "❌ FAIL")}");

                    if (!hasEject || !hasInsert || !dvConsistent)
                    {
                        ctx.Fail("GPU 猪排图 EjectDV/InsertDV 解析无效或与 DeltaVGrid 不一致");
                        allOk = false;
                    }
                }
                else
                {
                    ctx.Fail("生成猪排图返回 null");
                    allOk = false;
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"用例 2 异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 3: 模式 2 地月在轨锁定转移 (TLI 顺向/共面点火)
            // -------------------------------------------------------------
            ctx.StartCase("Mode2LockedOrbit_EarthToMoon_CoplanarTLI");
            Console.WriteLine("\n[3] 核验 MGADifferentialEvolution: 模式 2 地月转移锁相寻优与 TLI 闭环");
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
                    PopulationSize = 40,
                    MaxGenerations = 25,
                    EnableParkingOrbitPhaseAlignment = true
                };

                var msw = Stopwatch.StartNew();
                var sols = PykepBridge.Optimize(cfg);
                msw.Stop();

                if (sols != null && sols.Count > 0)
                {
                    var best = sols[0];
                    double depDv = best.LaunchDeltaVKms * 1000.0;
                    double totDv = best.TotalDeltaVKms * 1000.0;

                    ctx.RecordMetric("求解耗时", $"{msw.ElapsedMilliseconds} ms");
                    ctx.RecordMetric("出射ΔV", $"{depDv:F1} m/s (门禁<=3600)");
                    ctx.RecordMetric("总机动ΔV", $"{totDv:F1} m/s (门禁<=4500)");

                    Console.WriteLine($"    • 求解耗时: {msw.ElapsedMilliseconds} ms, 最优出射: {depDv:F1} m/s, 总机动: {totDv:F1} m/s");
                    bool launchOk = depDv <= 3600.0;
                    bool totOk = totDv <= 4500.0;

                    if (launchOk && totOk)
                    {
                        Console.WriteLine("    • 地月在轨锁定共面 TLI 寻优: ✅ PASS");
                    }
                    else
                    {
                        Console.WriteLine("    • 地月在轨锁定共面 TLI 寻优: ❌ 门禁超标");
                        ctx.Fail($"模式 2 出射或总机动超标: 出射={depDv:F1} m/s, 总={totDv:F1} m/s");
                        allOk = false;
                    }
                }
                else
                {
                    ctx.Fail("模式 2 未返回任何有效解");
                    allOk = false;
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"用例 3 异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 4: 飞掠切入几何、B 平面与 GMAT 脚本静态语法合规
            // -------------------------------------------------------------
            ctx.StartCase("FlybyGeometry_BPlaneAndGMATScriptLinter");
            Console.WriteLine("\n[4] 核验 MGA 飞掠切入几何、B 平面与 GMAT 脚本静态语法检查");
            try
            {
                var vIn = new Vector3d(5.2, -3.1, 1.8);
                var vOut = new Vector3d(2.4, 5.5, -0.6);
                double muVenus = 324859.0;
                double rVenus = 6051.8;
                var geom = MGAFlybyDynamicsHelper.ComputeFlybyGeometry(vIn, vOut, muVenus, rVenus);

                bool geomOk = geom.IncomingDhaDeg >= 0 && geom.IncomingDhaDeg <= 360.0 &&
                             geom.IncomingDecDeg >= -90.0 && geom.IncomingDecDeg <= 90.0 &&
                             geom.TurnAngleDeg > 0 && geom.TurnAngleDeg < 180.0 &&
                             geom.BMagKm > 0;

                var mockSol = new MGASolution
                {
                    DepartureUT = 0.0,
                    TotalFlightDays = 850.0,
                    LaunchDeltaVKms = 3.65,
                    TotalDeltaVKms = 4.20,
                    AllLegsFeasible = true
                };
                mockSol.Legs.Add(new MGALegResult
                {
                    LegIndex = 0, FromBody = "Earth", ToBody = "Venus",
                    DepartureUT = 0.0, ArrivalUT = 150.0 * 86400.0,
                    ToFDays = 150.0, DeltaVKms = 3.65, VInfArrivalKms = vIn
                });
                mockSol.Legs.Add(new MGALegResult
                {
                    LegIndex = 1, FromBody = "Venus", ToBody = "Jupiter",
                    DepartureUT = 150.0 * 86400.0, ArrivalUT = 850.0 * 86400.0,
                    ToFDays = 700.0, DeltaVKms = 0.55, VInfDepartureKms = vOut,
                    VInfArrivalKms = new Vector3d(4.0, 1.0, -0.5)
                });

                var mockCfg = new MGAConfig
                {
                    CentralBody = "Sun", DepartureBody = "Earth", DestinationBody = "Jupiter",
                    FlybySequence = new List<string> { "Venus", "Jupiter" },
                    DeparturePeAKm = 200.0, DestinationPeAKm = 50000.0
                };

                string script = MGAGMATScriptBuilder.GenerateMGAScript(mockSol, mockCfg, "Sat");
                var lintReport = GMATScriptLinter.Lint(script);

                ctx.RecordMetric("DHA", $"{geom.IncomingDhaDeg:F2}°");
                ctx.RecordMetric("DEC", $"{geom.IncomingDecDeg:F2}°");
                ctx.RecordMetric("偏转角", $"{geom.TurnAngleDeg:F2}°");
                ctx.RecordMetric("B平面模长", $"{geom.BMagKm:F1} km");
                ctx.RecordMetric("GMAT脚本Lint错误数", lintReport.ErrorCount);

                Console.WriteLine($"    • 飞掠几何: DHA={geom.IncomingDhaDeg:F1}°, DEC={geom.IncomingDecDeg:F1}°, 偏转角={geom.TurnAngleDeg:F1}°, |B|={geom.BMagKm:F1} km");
                Console.WriteLine($"    • GMAT 脚本静态语法 Lint 检查: 错误={lintReport.ErrorCount}, 警告={lintReport.WarningCount} -> {(lintReport.ErrorCount == 0 ? "✅ PASS" : "❌ FAIL")}");

                if (!geomOk || lintReport.ErrorCount > 0)
                {
                    ctx.Fail($"几何断言异常或 GMAT 脚本存在语法错误 ({lintReport.ErrorCount} errors)");
                    allOk = false;
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"用例 4 异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 5: 经典行星际霍曼窗口基准核验 (Earth -> Mars)
            // -------------------------------------------------------------
            ctx.StartCase("Hohmann_EarthToMars_Benchmark");
            Console.WriteLine("\n[5] 核验经典行星际霍曼基准: Earth ➔ Mars");
            try
            {
                double ut1951 = GMATClient.ParseUtcGregorian("10 Feb 1951 14:52:52.244");
                var (marsOptUT, marsToFDays, marsApproxDV) = PykepBridge.CalculateHohmannWindow("Earth", "Mars", ut1951);

                bool hohmannOk = (marsToFDays >= 200.0 && marsToFDays <= 320.0 && marsApproxDV >= 5000.0 && marsApproxDV <= 6500.0);

                ctx.RecordMetric("最佳发射历元", GMATClient.FormatUtcGregorian(marsOptUT));
                ctx.RecordMetric("标称飞行天数", $"{marsToFDays:F1} 天");
                ctx.RecordMetric("预估总ΔV", $"{marsApproxDV:F1} m/s");

                Console.WriteLine($"    • 发射历元: {GMATClient.FormatUtcGregorian(marsOptUT)}, 飞行天数: {marsToFDays:F1}d, 预估ΔV: {marsApproxDV:F1} m/s");
                Console.WriteLine($"    • 霍曼权威基准断言: {(hohmannOk ? "✅ PASS" : "❌ FAIL")}");

                if (!hohmannOk)
                {
                    ctx.Fail($"火星霍曼基准指标偏离预期: ToF={marsToFDays:F1}d, ΔV={marsApproxDV:F1}m/s");
                    allOk = false;
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"用例 5 异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine("\n==========================================================================");
            Console.WriteLine($"🏁 [Fast Suite] 完成！通过: {ctx.Report.PassCount} / {ctx.Report.TotalCount} (耗时: {sw.ElapsedMilliseconds} ms)");
            Console.WriteLine("==========================================================================");

            return allOk;
        }
    }
}
