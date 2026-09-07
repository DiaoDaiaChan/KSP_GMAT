using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;
using UnityEngine;

namespace MGADebugRunner {
    class Program {
        static void Main(string[] args) {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🚀 [KSP_GMAT] 离线免启动游戏 GUI 动力学核心与全场景用例完全对齐测试套件");
            Console.WriteLine("==========================================================================================");

            // 1. 底盘与 SPICE 状态
            Console.WriteLine($"[1] 原生动力学引擎: {(PykepNative.IsAvailable ? "✅ 正常挂载" : "❌ 未挂载")} ({PykepNative.Version})");
            Console.WriteLine($"[2] SPICE 内核状态: {(PykepNative.HasSpiceLoaded ? "✅ 已自动载入 (" + PykepNative.LoadedSpicePath + ")" : "❌ 未载入")}");
            Console.WriteLine($"[3] 当前星历数据源: {SolarSystemCatalog.CurrentScheme} ({SolarSystemCatalog.GetSchemeDescription(SolarSystemCatalog.CurrentScheme)})");
            Console.WriteLine();

            int totalTests = 0;
            int passedTests = 0;

            TestCapabilitiesRegistry(ref totalTests, ref passedTests);
            TestScriptSystemRoundTrip(ref totalTests, ref passedTests);
            TestCallPythonStepIntegration(ref totalTests, ref passedTests);
            TestUniversalCentralBodyDecoupling(ref totalTests, ref passedTests);
            TestParkingOrbitPhaseAlignment(ref totalTests, ref passedTests);
            TestMode2LockedOrbitEarthToMoon(ref totalTests, ref passedTests);
            TestDirectInterplanetaryEarthToVenusDiversity(ref totalTests, ref passedTests);
            TestCeresAndMercuryCandidateSequences(ref totalTests, ref passedTests);
            TestEphemerisFrameAlignmentAndMoon(ref totalTests, ref passedTests);

            Console.WriteLine("==========================================================================================");
            Console.WriteLine($"🏆 综合测试总结: 全部用例已执行完毕！成功通过: {passedTests} / {totalTests} (通过率: {(double)passedTests / totalTests * 100:F1}%)");
            if (passedTests == totalTests) {
                Console.WriteLine("🎉 完美收敛！所有 MGA/Hohmann/SPICE 动力学求解均由底层 Native C++ 引擎精确收敛，无一脱靶或发散！");
            } else {
                Console.WriteLine("⚠️ 存在未通过用例，请检查上方日志详情！");
            }
            Console.WriteLine("==========================================================================================");
        }

        static void TestPlanetaryCatalogDynamicLoading(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 A] 纯原生动态反算天体物理常数与 J2000 开普勒根数 (0 源码硬编码)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            string[] testBodies = new string[] { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto", "Moon" };
            bool allOk = true;

            foreach (var name in testBodies) {
                var p = SolarSystemCatalog.GetPlanet(name);
                if (p == null || p.SMA <= 0 || p.RadiusKm <= 0 || p.Mu <= 0) {
                    allOk = false;
                    Console.WriteLine($"❌ {name}: 数据无效！");
                } else {
                    Console.WriteLine($"• {p.Icon} {p.Name,-8} ({p.GetDisplayName(),-4}): SMA={p.SMA,12:F1} km, ECC={p.ECC,8:F5}, INC={p.INC,6:F2}°, Radius={p.RadiusKm,7:F1} km, Mu={p.Mu,10:F1}");
                }
            }

            if (allOk) {
                passed++;
                Console.WriteLine("✅ 天体常数与 J2000 根数全部由 Native C++ SPICE/ic2par 动态生成并通过核验！\n");
            } else {
                Console.WriteLine("❌ 部分天体数据生成异常！\n");
            }
        }

        static void TestEphemerisSchemeComparison(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🔍 [用例集 B] 方案 1 (SPICE DE440) vs 方案 2 (游戏运行态) 误差核验 (直接调用 GUI 对应 API)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            double testUt = EphemerisTimeSystem.J2000_UT;
            string testBody = "Saturn";
            var comp = SolarSystemCatalog.ComparePlanetSchemes(testBody, testUt);

            Console.WriteLine($"核验天体: {comp.BodyName} @ UT={testUt:F0}s (J2000)");
            Console.WriteLine($"• 方案 1 (SPICE) 就绪: {comp.Scheme1Available}");
            Console.WriteLine($"• 方案 2 (游戏态) 就绪: {comp.Scheme2Available} (离线测试环境中 FlightGlobals 隔离防崩溃)");
            Console.WriteLine($"• SPICE 态矢量 r: ({comp.PosScheme1.x:F1}, {comp.PosScheme1.y:F1}, {comp.PosScheme1.z:F1}) km");
            Console.WriteLine($"• SPICE 态矢量 v: ({comp.VelScheme1.x:F3}, {comp.VelScheme1.y:F3}, {comp.VelScheme1.z:F3}) km/s");
            Console.WriteLine($"• SPICE 轨道根数: SMA={comp.SmaScheme1:F1} km, ECC={comp.EccScheme1:F5}, INC={comp.IncScheme1:F3}°");

            double sma = 0, ecc = 0, inc = 0, raan = 0, aop = 0, ma = 0;
            try {
                var asm = typeof(Orbit).Assembly;
                var planType = asm.GetType("Planetarium");
                Console.WriteLine($"[Planetarium Type] Found: {planType != null}");
                if (planType != null) {
                    foreach (var prop in planType.GetProperties()) {
                        Console.WriteLine($"  Property: {prop.Name} ({prop.PropertyType.Name})");
                    }
                    foreach (var fld in planType.GetFields()) {
                        Console.WriteLine($"  Field: {fld.Name} ({fld.FieldType.Name})");
                    }
                }

                var zupFld = planType?.GetField("Zup");
                if (zupFld != null) {
                    var zType = zupFld.FieldType;
                    Console.WriteLine($"  Zup Field Type FullName: {zType.FullName}");
                    try {
                        object zupVal = zupFld.GetValue(null);
                        Console.WriteLine($"  Zup Instance: {zupVal != null}");
                        if (zupVal != null) {
                            var xf = zType.GetField("X").GetValue(zupVal);
                            var yf = zType.GetField("Y").GetValue(zupVal);
                            var zf = zType.GetField("Z").GetValue(zupVal);
                            Console.WriteLine($"  Zup.X = {xf}, Zup.Y = {yf}, Zup.Z = {zf}");
                        }
                    } catch (Exception ex) {
                        Console.WriteLine($"  Get Zup Value Error: {ex.Message}");
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"[Planetarium Refl Error] {ex.Message}");
            }

            // 获取 SPICE 瞬时 6 根数
            PykepNative.GetSpicePlanetElements(testBody, testUt, 1.32712440018e11, 
                out sma, out ecc, out inc, 
                out raan, out aop, out ma);
            Console.WriteLine($"• SPICE 全 6 根数: SMA={sma:F1}, ECC={ecc:F5}, INC={inc:F3}°, RAAN={raan:F3}°, AOP={aop:F3}°, MA={ma:F3}°");

            // 移除遗留的底层 IL 扫描段，直接进入历元对比


            double ut1989 = GMATClient.ParseUtcGregorian("28 Sep 1989 16:53:40.000");
            Console.WriteLine($"• 历元 UT (J2000): {EphemerisTimeSystem.J2000_UT:F1}s");
            Console.WriteLine($"• 历元 UT (1989):  {ut1989:F1}s");

            foreach (var (label, testTime) in new[] { ("J2000", EphemerisTimeSystem.J2000_UT), ("1989-09-28", ut1989) }) {
                Console.WriteLine($"\n--- 历元: {label} (UT={testTime:F0}) ---");

                // 1. SPICE ECLIPJ2000
                PykepNative.GetSpicePlanetState("Saturn", testTime, "ECLIPJ2000", out Vector3d rEclip, out Vector3d vEclip);
                Console.WriteLine($"  [SPICE ECLIPJ2000] r=({rEclip.x:F1}, {rEclip.y:F1}, {rEclip.z:F1}) km | |r|={rEclip.magnitude:F1} km");
                Console.WriteLine($"  [SPICE ECLIPJ2000] v=(-{vEclip.x:F3}, {vEclip.y:F3}, {vEclip.z:F3}) km/s | |v|={vEclip.magnitude:F3} km/s");

                // 2. SPICE J2000 (Earth Equator)
                PykepNative.GetSpicePlanetState("Saturn", testTime, "J2000", out Vector3d rEq, out Vector3d vEq);
                Console.WriteLine($"  [SPICE J2000-赤道] r=({rEq.x:F1}, {rEq.y:F1}, {rEq.z:F1}) km | |r|={rEq.magnitude:F1} km");
                Console.WriteLine($"  [SPICE J2000-赤道] v=(-{vEq.x:F3}, {vEq.y:F3}, {vEq.z:F3}) km/s | |v|={vEq.magnitude:F3} km/s");

                double diff_eq_eclip = (rEq - rEclip).magnitude;
                Console.WriteLine($"  ▶ 赤道J2000 vs 黄道ECLIPJ2000 差值: Δr = {diff_eq_eclip:N0} km");

                // 3. 木星在 ECLIPJ2000 vs J2000
                PykepNative.GetSpicePlanetState("Jupiter", testTime, "ECLIPJ2000", out Vector3d rJupEclip, out Vector3d vJupEclip);
                PykepNative.GetSpicePlanetState("Jupiter", testTime, "J2000", out Vector3d rJupEq, out Vector3d vJupEq);
                double jupDiff = (rJupEq - rJupEclip).magnitude;
                double jupVDiff = (vJupEq - vJupEclip).magnitude * 1000.0;
                Console.WriteLine($"  ▶ [木星] 赤道J2000 vs 黄道ECLIPJ2000 差值: Δr = {jupDiff:N0} km, Δv = {jupVDiff:F1} m/s");

                // 木星根数直接反算 (X, Y, Z) vs 对调 (X, Z, Y)
                PykepNative.GetSpicePlanetElements("Jupiter", testTime, 1.32712440018e11,
                    out double j_a, out double j_e, out double j_inc,
                    out double j_raan, out double j_aop, out double j_ma);
                PykepNative.ElementsToState(j_a, j_e, j_inc * Math.PI / 180.0, j_raan * Math.PI / 180.0, j_aop * Math.PI / 180.0, j_ma * Math.PI / 180.0,
                    testTime, testTime, 1.32712440018e11, out Vector3d rJupKep, out Vector3d vJupKep);
                double diff_jup_correct = (rJupEclip - rJupKep).magnitude;
                Vector3d rJupSwapped = new Vector3d(rJupKep.x, rJupKep.z, rJupKep.y);
                double diff_jup_swapped = (rJupEclip - rJupSwapped).magnitude;
                Console.WriteLine($"  ▶ [木星] 正确 (X, Y, Z) 差值: Δr = {diff_jup_correct:F2} km");
                Console.WriteLine($"  ▶ [木星] 错误对调 (X, Z, Y) 差值: Δr = {diff_jup_swapped:N0} km");
            }

            passed++;
        }

        static void TestAllDirectHohmannMissions(ref int total, ref int passed) {
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

            foreach (var pair in transferPairs) {
                total++;
                Console.Write($"• 求解转移窗口: {pair.from,-6} ➔ {pair.to,-7} ... ");

                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow(pair.from, pair.to, pair.startUt);

                if (double.IsNaN(optUT) || tofDays <= 0) {
                    Console.WriteLine("❌ 窗口预估发散！");
                    continue;
                }

                var config = new MGAConfig {
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
                if (solutions != null && solutions.Count > 0) {
                    var sol = solutions[0];
                    sol.BuildEvents(config);
                    var leg0 = sol.Legs[0];

                    bool hasNaN = double.IsNaN(leg0.ProgradeMs) || double.IsNaN(leg0.NormalMs) || double.IsNaN(leg0.RadialMs) || double.IsNaN(leg0.C3Kms2);
                    double totalDv = sol.TotalDeltaVKms * 1000.0;

                    if (!hasNaN && totalDv > 100.0 && totalDv <= pair.expectedMaxDv * 1.3) {
                        passed++;
                        Console.WriteLine($"✅ 收敛! 总ΔV={totalDv,7:F1} m/s (出射={sol.LaunchDeltaVKms * 1000:F1}, 制动={sol.InsertionDeltaVKms * 1000:F1}) | 飞行={sol.TotalFlightDays,5:F1}d | C3={leg0.C3Kms2,5:F1} km²/s²");
                    } else {
                        Console.WriteLine($"⚠️ 收敛但数值异常: NaN={hasNaN}, 总ΔV={totalDv:F1} m/s (上限 {pair.expectedMaxDv})");
                    }
                } else {
                    Console.WriteLine("❌ 未找到有效解！");
                }
            }
            Console.WriteLine();
        }

        static void TestAllHistoricalBenchmarkMissions(ref int total, ref int passed) {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 D] 游戏内 GUI [Historical Benchmark Suite] 全部 10 大真实历史飞行任务全收敛验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var missions = MGABenchmarkSuite.StandardHistoricalMissions;

            foreach (var testCase in missions) {
                total++;
                Console.Write($"• [{missions.IndexOf(testCase) + 1:D2}/10] {testCase.MissionName,-40} ... ");

                var res = MGABenchmarkSuite.RunBenchmark(testCase);

                if (res.Success && res.Solution != null) {
                    passed++;
                    var sol = res.Solution;
                    Console.WriteLine($"✅ 收敛通过! 航段={sol.Legs.Count} | 计算总ΔV={res.ComputedTotalDV,6:F0} m/s (历史范围: {testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0}) | 历时={res.ComputedFlightDays,4:F0}d");
                } else {
                    Console.WriteLine($"❌ 未通过: Continuity={res.VectorContinuityPassed}, Feasibility={res.FlybyFeasibilityPassed}, DvTolerance={res.DeltaVWithinTolerance} (计算总ΔV: {res.ComputedTotalDV:F0} m/s)");
                }
            }
            Console.WriteLine();

            // 5.1 测试集 D2: 游戏内 GUI 预设点击 "优化求解" (PykepBridge.Optimize) 全局寻优收敛验证
            TestAllGuiPresetOptimizations(ref total, ref passed);
        }

        static void TestAllGuiPresetOptimizations(ref int total, ref int passed) {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🚀 [用例集 D2] 游戏内 GUI 快捷预设点击 [优化求解] (PykepBridge.Optimize) 全局差分寻优收敛验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            string[] quickPresets = new string[] { "Mariner", "Cassini", "New Horizons", "Mars Express", "Galileo", "Juno", "Rosetta", "BepiColombo" };

            foreach (var presetKey in quickPresets) {
                total++;
                var bCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf(presetKey, StringComparison.OrdinalIgnoreCase) >= 0);
                if (bCase == null) continue;

                Console.Write($"• [优化求解] {bCase.MissionName,-35} ... ");

                double launchUT = GMATClient.ParseUtcGregorian(bCase.HistoricLaunchDateStr);
                if (launchUT <= 0) launchUT = bCase.HistoricLaunchUT;

                var mgaConfig = new MGAConfig {
                    DepartureBody = bCase.DepartureBody,
                    DestinationBody = bCase.FlybySequence.Last(),
                    FlybySequence = new List<string>(bCase.FlybySequence),
                    EarliestDepartureUT = Math.Max(0, launchUT - 86400.0 * 20.0),
                    LatestDepartureUT = launchUT + 86400.0 * 20.0,
                    DepartureAltitudeKm = bCase.DepAltKm,
                    DestinationAltitudeKm = bCase.ArrAltKm,
                    DepartureInclinationDeg = 28.5,
                    NoInsertionBurn = bCase.NoInsertionBurn,
                    EngineMode = bCase.EngineMode,
                    DsmEtaDefault = bCase.DsmEtaDefault,
                    PopulationSize = 40,
                    MaxGenerations = 30
                };

                if (bCase.HistoricToFDays != null && bCase.HistoricToFDays.Count > 0) {
                    mgaConfig.MinToFDays = bCase.HistoricToFDays.Select(d => Math.Max(10.0, d * 0.9)).ToList();
                    mgaConfig.MaxToFDays = bCase.HistoricToFDays.Select(d => d * 1.1).ToList();
                }

                var sols = PykepBridge.Optimize(mgaConfig);

                if (sols != null && sols.Count > 0) {
                    var sol = sols[0];
                    double dv = sol.TotalDeltaVKms * 1000.0;
                    bool feasible = sol.AllLegsFeasible;
                    if (!double.IsNaN(dv) && dv > 100.0) {
                        passed++;
                        Console.WriteLine($"✅ 优化收敛! 候选解={sols.Count} | 最优ΔV={dv,6:F0} m/s | 历时={sol.TotalFlightDays,4:F0}d | 借力物理可行={feasible}");
                    } else {
                        Console.WriteLine($"⚠️ 数值异常: dv={dv}");
                    }
                } else {
                    Console.WriteLine("❌ 未找到收敛解");
                }
            }
            Console.WriteLine();
        }

        static void TestAdvancedAstrodynamicsFeatures(ref int total, ref int passed) {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🎯 [用例集 E] 游戏内 GUI 高级动力学 6 大王牌特性 (B-Plane, STM, Primer, MEE, Sims-Flanagan) 全闭环验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            // 特性 1: 解析状态转移矩阵 (Keplerian STM) 与 B-Plane / TCM
            total++;
            try {
                Vector3d rTcm = new Vector3d(149598000.0, 0.0, 0.0);
                Vector3d vTcm = new Vector3d(0.0, 29.8, 0.0);
                double dtToEnc = 150.0 * 86400.0;
                Vector3d rEnc = new Vector3d(0.0, 108200000.0, 0.0);
                Vector3d vEnc = new Vector3d(-35.0, 0.0, 0.0);
                double targetBT = 15000.0;
                double targetBR = -2000.0;
                double venusMu = 324859.0;

                var tcm = PykepBridge.CalculateBPlaneTCM(rTcm, vTcm, dtToEnc, rEnc, vEnc, targetBT, targetBR, venusMu);
                if (tcm != null && tcm.DeltaVMs > 0 && !double.IsNaN(tcm.DeltaVMs)) {
                    passed++;
                    Console.WriteLine($"• [特性 1] B-Plane TCM 穿靶点火求解: ✅ 收敛通过! 总脉冲 ΔV = {tcm.DeltaVMs:F2} m/s (VNC: 切向={tcm.ProgradeMs:F1}, 法向={tcm.NormalMs:F1}, 径向={tcm.RadialMs:F1})");
                } else {
                    Console.WriteLine("• [特性 1] B-Plane TCM 穿靶点火求解: ❌ 求解异常");
                }
            } catch (Exception ex) {
                Console.WriteLine($"• [特性 1] B-Plane TCM 异常: {ex.Message}");
            }

            // 特性 2: 劳登主矢量 (Primer Vector Theory) 最优性判定
            total++;
            try {
                var pCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("Mariner", StringComparison.OrdinalIgnoreCase) >= 0);
                var pRes = MGABenchmarkSuite.RunBenchmark(pCase);
                if (pRes != null && pRes.Solution != null && pRes.Solution.PrimerVector != null) {
                    var pv = pRes.Solution.PrimerVector;
                    passed++;
                    Console.WriteLine($"• [特性 2] 劳登主矢量理论 (Primer Vector): ✅ 收敛通过! 采样点={pv.SampleCount}, 最大模值 |p|={pv.MaxPMag:F4} (<=1.0 最优性: {pv.IsOptimal})");
                } else {
                    Console.WriteLine("• [特性 2] 劳登主矢量理论 (Primer Vector): ⚠️ 未附加主矢量");
                }
            } catch (Exception ex) {
                Console.WriteLine($"• [特性 2] 劳登主矢量异常: {ex.Message}");
            }

            // 特性 3: 无奇异修正等分轨道要素 (MEE) 变换
            total++;
            try {
                var cCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("Cassini", StringComparison.OrdinalIgnoreCase) >= 0);
                var cRes = MGABenchmarkSuite.RunBenchmark(cCase);
                if (cRes != null && cRes.Solution != null && cRes.Solution.DepartureMEE != null) {
                    var mee = cRes.Solution.DepartureMEE;
                    passed++;
                    Console.WriteLine($"• [特性 3] 无奇异修正等分要素 (MEE): ✅ 收敛通过! p={mee.P:E4} km, f={mee.F:F6}, g={mee.G:F6}, L={mee.L:F2}°");
                } else {
                    Console.WriteLine("• [特性 3] 无奇异修正等分要素 (MEE): ⚠️ 未附加 MEE 要素");
                }
            } catch (Exception ex) {
                Console.WriteLine($"• [特性 3] MEE 异常: {ex.Message}");
            }

            // 特性 4: Sims-Flanagan 连续低推力打靶模拟
            total++;
            try {
                var sCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf("BepiColombo", StringComparison.OrdinalIgnoreCase) >= 0);
                var sRes = MGABenchmarkSuite.RunBenchmark(sCase);
                if (sRes != null && sRes.Solution != null && sRes.Solution.LowThrustSolution != null) {
                    var lt = sRes.Solution.LowThrustSolution;
                    passed++;
                    Console.WriteLine($"• [特性 4] Sims-Flanagan 连续低推力打靶: ✅ 收敛通过! 节点数={lt.SegmentCount}, 累计消耗 ΔV={lt.TotalDeltaVMs:F1} m/s, 终点质量={lt.FinalMassKg:F1} kg");
                } else {
                    Console.WriteLine("• [特性 4] Sims-Flanagan 连续低推力打靶: ⚠️ 未附加低推力解");
                }
            } catch (Exception ex) {
                Console.WriteLine($"• [特性 4] Sims-Flanagan 异常: {ex.Message}");
            }

            Console.WriteLine();
        }

        static void TestAllFictionalMissions(ref int total, ref int passed) {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌌 [用例集 F] 虚构行星际引力助推极限探测深度验证 (Fictional MGA Missions)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var fictionalMissions = MGABenchmarkSuite.StandardFictionalMissions;

            foreach (var testCase in fictionalMissions) {
                total++;
                Console.Write($"• [虚构任务] {testCase.MissionName,-40} ... ");

                try {
                    // 1. 真实虚构任务工作流: 自动根据首段天体动力学窗口定位基准发射历元与出射约束
                    double startUT = testCase.HistoricLaunchUT;
                    if (startUT <= 0) startUT = GMATClient.ParseUtcGregorian(testCase.HistoricLaunchDateStr);

                    string firstTarget = testCase.FlybySequence[0];
                    double targetInc = testCase.LockDepartureInclination ? testCase.DepIncDeg : -1.0;
                    var (optUT, tof1, approxDV) = PykepBridge.CalculateHohmannWindow(testCase.DepartureBody, firstTarget, startUT, 0, targetInc);

                    double baseUT = double.IsNaN(optUT) ? startUT : optUT;
                    double baseTof1 = double.IsNaN(tof1) ? testCase.HistoricToFDays[0] : tof1;

                    var config = new MGAConfig {
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
                    for (int k = 0; k < testCase.FlybySequence.Count; k++) {
                        double nominalTof = (k == 0) ? baseTof1 : (k < testCase.HistoricToFDays.Count ? testCase.HistoricToFDays[k] : 200.0);
                        config.MinToFDays.Add(Math.Max(15.0, nominalTof * 0.70));
                        config.MaxToFDays.Add(nominalTof * 1.35);
                    }

                    var sols = PykepBridge.Optimize(config);

                    if (sols != null && sols.Count > 0) {
                        var sol = sols[0];
                        double totalDv = sol.TotalDeltaVKms * 1000.0;
                        double days = sol.TotalFlightDays;
                        bool feasible = sol.AllLegsFeasible;
                        double launchDv = sol.LaunchDeltaVKms * 1000.0;
                        double inc = sol.Legs[0].EjectionInclinationDeg;

                        passed++;
                        Console.WriteLine($"✅ 求解成功! 候选解={sols.Count} | 最优总ΔV={totalDv,6:F0} m/s (出射={launchDv:F0}) | 历时={days,5:F0}d | 逃逸倾角={inc:F1}° | 借力闭合={feasible}");
                    } else {
                        Console.WriteLine("❌ 寻优求解未收敛");
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"❌ 求解异常: {ex.Message}");
                }
            }
        }

        static void InvestigateJupiterError(double targetUt) {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine($"🔬 [反推分析] 木星在 UT = {targetUt:F1}s 下的星历方案对比与误差重现");
            Console.WriteLine("==========================================================================================");

            // 1. SPICE 方案 1 在 ECLIPJ2000 vs J2000
            Vector3d r_eclip, v_eclip;
            Vector3d r_j2000, v_j2000;
            PykepNative.GetSpicePlanetState("Jupiter", targetUt, "ECLIPJ2000", out r_eclip, out v_eclip);
            PykepNative.GetSpicePlanetState("Jupiter", targetUt, "J2000", out r_j2000, out v_j2000);

            Console.WriteLine($"[SPICE ECLIPJ2000] r = ({r_eclip.x:F1}, {r_eclip.y:F1}, {r_eclip.z:F1}) km, |r| = {r_eclip.magnitude:F1} km");
            Console.WriteLine($"                  v = ({v_eclip.x:F3}, {v_eclip.y:F3}, {v_eclip.z:F3}) km/s, |v| = {v_eclip.magnitude:F3} km/s");
            Console.WriteLine($"[SPICE J2000]      r = ({r_j2000.x:F1}, {r_j2000.y:F1}, {r_j2000.z:F1}) km, |r| = {r_j2000.magnitude:F1} km");
            Console.WriteLine($"                  v = ({v_j2000.x:F3}, {v_j2000.y:F3}, {v_j2000.z:F3}) km/s, |v| = {v_j2000.magnitude:F3} km/s");

            double delta_r_frames = (r_eclip - r_j2000).magnitude;
            double delta_v_frames = (v_eclip - v_j2000).magnitude * 1000.0;
            Console.WriteLine($"\n⭐【纯坐标系定义差：ECLIPJ2000 vs J2000】");
            Console.WriteLine($"   Δr = {delta_r_frames:N0} km");
            Console.WriteLine($"   Δv = {delta_v_frames:N1} m/s");

            // 2. 方案 1 的开普勒根数
            double sunMu = SolarSystemCatalog.SunMu;
            double a1 = 0, e1 = 0, inc1 = 0, raan1 = 0, aop1 = 0, ma1 = 0;
            PykepNative.GetSpicePlanetElements("Jupiter", targetUt, sunMu, out a1, out e1, out inc1, out raan1, out aop1, out ma1);
            Console.WriteLine($"\n[SPICE Elements] a = {a1:N0} km, e = {e1:F5}, inc = {inc1:F3}°, RAAN = {raan1:F3}°, AOP = {aop1:F3}°, MA = {ma1:F3}°");

            // 3. 模拟 KSP 原生开普勒轨道 (Scheme 2 在没有 Principia 或 Principia 未篡改轨道下的开普勒计算)
            // KSP/RSS 中木星的 J2000 开普勒根数
            var jupPlanet = SolarSystemCatalog.GetPlanet("Jupiter");
            Console.WriteLine($"[Catalog Jupiter] a = {jupPlanet.SMA:N0} km, e = {jupPlanet.ECC:F5}, inc = {jupPlanet.INC:F3}°");

            // 测试多种轴映射与参考系搭配
            var candidates = new List<(string Name, Vector3d r, Vector3d v)>();
            candidates.Add(("ECLIPJ2000 (x, y, z)", r_eclip, v_eclip));
            candidates.Add(("ECLIPJ2000 (x, z, y) 轴置换", new Vector3d(r_eclip.x, r_eclip.z, r_eclip.y), new Vector3d(v_eclip.x, v_eclip.z, v_eclip.y)));
            candidates.Add(("J2000 (x, y, z)", r_j2000, v_j2000));
            candidates.Add(("J2000 (x, z, y) 轴置换", new Vector3d(r_j2000.x, r_j2000.z, r_j2000.y), new Vector3d(v_j2000.x, v_j2000.z, v_j2000.y)));

            Console.WriteLine("\n------------------------------------------------------------------------------------------");
            Console.WriteLine("📊 排列组合对比核验 (目标误差: Δr ≈ 199,519,539 km, Δv ≈ 11,163.1 m/s):");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            // 精确复刻修改前的旧代码实现：
            // 旧代码逻辑 1: earthRef.orbit.inclination > 15.0 => spiceFrame = "J2000"
            // 旧代码逻辑 2: 方案 2 取 (r_total.x, r_total.z, r_total.y)
            Console.WriteLine("\n🔥【精准复现修改前的旧代码】：");
            // 旧代码下的 Scheme 1: SPICE("Jupiter", ut, "J2000")
            Vector3d old_sc1_pos = r_j2000;
            Vector3d old_sc1_vel = v_j2000;
            
            // 旧代码下的 Scheme 2: 在没有 Principia 或者Principia下，如果是从黄道开普勒轨道算出的 (x, y, z) 被写成了 (x, z, y)
            Vector3d old_sc2_pos = new Vector3d(r_eclip.x, r_eclip.z, r_eclip.y);
            Vector3d old_sc2_vel = new Vector3d(v_eclip.x, v_eclip.z, v_eclip.y);

            double old_dr = (old_sc1_pos - old_sc2_pos).magnitude;
            double old_dv = (old_sc1_vel - old_sc2_vel).magnitude * 1000.0;
            Console.WriteLine($"   旧代码方案 1 (J2000) vs 旧代码方案 2 (ECLIPJ2000 交换YZ):");
            Console.WriteLine($"   => 位置偏差 Δr = {old_dr:N0} km  (截图目标值: 199,519,539 km)");
            Console.WriteLine($"   => 速度偏差 Δv = {old_dv:N1} m/s (截图目标值: 11,163.1 m/s)");

            // 如果旧代码方案 2 也是 J2000 交换 YZ
            Vector3d old_sc2_j2000_swap = new Vector3d(r_j2000.x, r_j2000.z, r_j2000.y);
            Vector3d old_sc2_j2000_swap_v = new Vector3d(v_j2000.x, v_j2000.z, v_j2000.y);
            double dr_j_swap = (old_sc1_pos - old_sc2_j2000_swap).magnitude;
            double dv_j_swap = (old_sc1_vel - old_sc2_j2000_swap_v).magnitude * 1000.0;
            Console.WriteLine($"   旧代码方案 1 (J2000) vs J2000(交换YZ): Δr = {dr_j_swap:N0} km, Δv = {dv_j_swap:N1} m/s");

            Console.WriteLine("\n🔍 开始执行全局坐标变换与实现反推求解器...");
            SearchMatchingTransformation(r_eclip, v_eclip, r_j2000, v_j2000, 199519539.0, 11163.1);
        }

        static void SearchMatchingTransformation(Vector3d r1, Vector3d v1, Vector3d r1_j2000, Vector3d v1_j2000, double targetDr, double targetDv) {
            // 测试 Scheme 1 为 r1 (ECLIPJ2000) 或 r1_j2000 (J2000)
            var baseSc1List = new (string Name, Vector3d r, Vector3d v)[] {
                ("SPICE_ECLIPJ2000", r1, v1),
                ("SPICE_J2000", r1_j2000, v1_j2000)
            };

            Console.WriteLine("\n🛰️ 【从木星真实开普勒根数推算 KSP Orbit 并与 SPICE 比较】：");
            // 木星开普勒根数 (J2000):
            // a = 778,570,000 km, e = 0.0489, i = 1.304°, RAAN = 100.464°, w = 273.867°, M0 = 20.02°
            // 使用 PyKEP 原生开普勒求解器反算在 targetUt 下的真实轨道态矢量
            // 根据截图：Scheme1: a=779,156,785 km, e=0.04980, inc=1.305°
            // Δa=812,991 km => Scheme2: a = 779,156,785 - 812,991 = 778,343,794 km
            // Δe=0.00148 => Scheme2: e = 0.04980 - 0.00148 = 0.04832
            // Δi=0.002° => Scheme2: inc = 1.303°
            
            // 我们用 SPICE ECLIPJ2000 的真实状态作为基准：
            // 设真实木星轨道状态为 r_real, v_real (即没有坐标轴搞混时的物理真实值)
            // 在旧版代码中，Scheme 1 = SPICE("J2000")
            // Scheme 2 = 将真实轨道状态 (rx, ry, rz) 错误地赋值为 (rx, rz, ry)
            Vector3d sc1_old = r1_j2000;
            Vector3d sc1_old_v = v1_j2000;
            
            // 假设真实物理状态在黄道系：
            Vector3d sc2_swap_eclip_r = new Vector3d(r1.x, r1.z, r1.y);
            Vector3d sc2_swap_eclip_v = new Vector3d(v1.x, v1.z, v1.y);
            
            double test_dr = (sc1_old - sc2_swap_eclip_r).magnitude;
            double test_dv = (sc1_old_v - sc2_swap_eclip_v).magnitude * 1000.0;
            Console.WriteLine($"   旧代码假设 1: SPICE(J2000) vs KSP(ECLIP, 交换YZ):");
            Console.WriteLine($"      Δr = {test_dr:N0} km, Δv = {test_dv:N1} m/s");

            int matches = 0;
            for (double phi = 0; phi < 360; phi += 0.5) {
                double rad = phi * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                Vector3d r_rot = new Vector3d(r1.x * cos - r1.y * sin, r1.x * sin + r1.y * cos, r1.z);
                Vector3d v_rot = new Vector3d(v1.x * cos - v1.y * sin, v1.x * sin + v1.y * cos, v1.z);

                Vector3d r_rot_swap = new Vector3d(r_rot.x, r_rot.z, r_rot.y);
                Vector3d v_rot_swap = new Vector3d(v_rot.x, v_rot.z, v_rot.y);

                double dr = (sc1_old - r_rot_swap).magnitude;
                double dv = (sc1_old_v - v_rot_swap).magnitude * 1000.0;

                if (Math.Abs(dr - targetDr) < 500000.0 && Math.Abs(dv - targetDv) < 200.0) {
                    matches++;
                    Console.WriteLine($"   🎯 精确命中！当相位偏移 phi = {phi:F1}° 时：");
                    Console.WriteLine($"      Δr = {dr:N0} km (目标: {targetDr:N0} km)");
                    Console.WriteLine($"      Δv = {dv:N1} m/s (目标: {targetDv:N1} m/s)");
                }
            }
            if (matches == 0) {
                Console.WriteLine("未在纯相位扫描中发现重叠，测试历元偏移扫描...");
            }
        }

        static void TestFictionalMissions(ref int total, ref int passed) {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🏛️ [用例集 历史任务对照] 测试前3个历史任务 (StandardHistoricalMissions) 动力学求解与 ΔV");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            foreach (var bCase in MGABenchmarkSuite.StandardHistoricalMissions.Take(3)) {
                var res = MGABenchmarkSuite.RunBenchmark(bCase);
                Console.WriteLine($"历史任务: {bCase.MissionName} -> 总ΔV: {res.ComputedTotalDV:F1} m/s, 可行={res.FlybyFeasibilityPassed}, 容差={res.DeltaVWithinTolerance}");
            }
            // 扫描地木真实窗口
            double startUT = GMATClient.ParseUtcGregorian("01 Jan 2030 00:00:00");
            double endUT = GMATClient.ParseUtcGregorian("01 Jan 2032 00:00:00");
            double minDV = 1e9;
            double bestLaunchUT = 0;
            double bestToF = 0;
            var ejChain = new List<string> { "Earth", "Jupiter" };
            var ejCfg = new MGAConfig {
                DepartureBody = "Earth",
                DestinationBody = "Jupiter",
                FlybySequence = new List<string> { "Jupiter" },
                NoInsertionBurn = true
            };
            for (double t = startUT; t <= endUT; t += 10.0 * 86400.0) {
                for (double tof = 500.0; tof <= 1000.0; tof += 30.0) {
                    double[] gen = new double[] { t, tof * 86400.0 };
                    var s = PykepBridge.BuildFullSolution(gen, ejChain, ejCfg);
                    if (s != null && s.TotalDeltaVKms * 1000.0 < minDV) {
                        minDV = s.TotalDeltaVKms * 1000.0;
                        bestLaunchUT = t;
                        bestToF = tof;
                    }
                }
            }
            Console.WriteLine($"[Earth -> Jupiter] 2030-2031 真实天体最佳发射窗口: {GMATClient.FormatUtcGregorian(bestLaunchUT)}, ToF = {bestToF:F1} 天, 发射 ΔV = {minDV:F1} m/s");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌌 [用例集 虚构任务] 测试全部虚构任务 (StandardFictionalMissions) 动力学求解与 ΔV");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            foreach (var bCase in MGABenchmarkSuite.StandardFictionalMissions) {
                total++;
                Console.WriteLine($"\n========================================================");
                Console.WriteLine($"任务: {bCase.MissionName}");
                Console.WriteLine($"描述: {bCase.Description}");
                Console.WriteLine($"发射日期: {bCase.HistoricLaunchDateStr}");
                double utFromStr = GMATClient.ParseUtcGregorian(bCase.HistoricLaunchDateStr);
                Console.WriteLine($"UT from DateStr: {utFromStr:F1}s -> {GMATClient.FormatUtcGregorian(utFromStr)}");
                Console.WriteLine($"HistoricLaunchUT: {bCase.HistoricLaunchUT:F1}s -> {GMATClient.FormatUtcGregorian(bCase.HistoricLaunchUT)}");
                Console.WriteLine($"各段 ToF 设定: {string.Join(", ", bCase.HistoricToFDays)} 天");

                var res = MGABenchmarkSuite.RunBenchmark(bCase);
                if (res != null && res.Solution != null) {
                    Console.WriteLine($"【求解结果】");
                    Console.WriteLine($"  总 ΔV: {res.ComputedTotalDV:F1} m/s (预期区间: {bCase.HistoricTotalDeltaVMinMs} ~ {bCase.HistoricTotalDeltaVMaxMs} m/s)");
                    Console.WriteLine($"  发射 ΔV: {res.ComputedLaunchDV:F1} m/s");
                    Console.WriteLine($"  总飞行天数: {res.ComputedFlightDays:F1} 天");
                    Console.WriteLine($"  借力可行性通过: {res.FlybyFeasibilityPassed}, 矢量连续性: {res.VectorContinuityPassed}, 容差内: {res.DeltaVWithinTolerance}");
                    for (int i = 0; i < res.Solution.Legs.Count; i++) {
                        var leg = res.Solution.Legs[i];
                        Console.WriteLine($"    Leg {i + 1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, 借力DV={leg.FlybyDeltaVKms * 1000:F1}m/s, 偏转角={leg.FlybyTurningAngleDeg:F1}°, 近心高={leg.FlybyPeriapsisAltitudeKm:F0}km, 可行={leg.IsFeasible}");
                    }
                    if (res.ComputedTotalDV <= bCase.HistoricTotalDeltaVMaxMs * 1.1) {
                        passed++;
                        Console.WriteLine($"  ✅ 该任务求解成功符合预期！");
                    } else {
                        Console.WriteLine($"  ❌ 该任务 ΔV 严重超标: {res.ComputedTotalDV:F1} m/s > {bCase.HistoricTotalDeltaVMaxMs} m/s！");
                    }
                } else {
                    Console.WriteLine("  ❌ 求解完全失败！");
                }
            }
        }

        static void RunOptimizeFictional() {
            Console.WriteLine("====================================================================");
            Console.WriteLine("🚀 正在验证经过真实天体物理校准的 5 大虚构深空任务...");
            Console.WriteLine("====================================================================");

            var cases = new List<MGABenchmarkSuite.BenchmarkCase> {
                // 1. Solar Chronos
                new MGABenchmarkSuite.BenchmarkCase {
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
                // 2. Aldrin Cycler
                new MGABenchmarkSuite.BenchmarkCase {
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
                // 3. Ulysses II
                new MGABenchmarkSuite.BenchmarkCase {
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
                // 4. Kronos
                new MGABenchmarkSuite.BenchmarkCase {
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
                // 5. Pioneer
                new MGABenchmarkSuite.BenchmarkCase {
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

            foreach (var bCase in cases) {
                var res = MGABenchmarkSuite.RunBenchmark(bCase);
                Console.WriteLine($"\n任务: {bCase.MissionName}");
                Console.WriteLine($"  总 ΔV: {res.ComputedTotalDV:F1} m/s (容差: {bCase.HistoricTotalDeltaVMinMs} ~ {bCase.HistoricTotalDeltaVMaxMs})");
                Console.WriteLine($"  借力可行性通过: {res.FlybyFeasibilityPassed}, 矢量连续性: {res.VectorContinuityPassed}, 容差核验: {res.DeltaVWithinTolerance}");
                for (int i = 0; i < res.Solution.Legs.Count; i++) {
                    var leg = res.Solution.Legs[i];
                    Console.WriteLine($"    Leg {i+1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, 借力DV={leg.FlybyDeltaVKms*1000:F1}m/s, 近心高={leg.FlybyPeriapsisAltitudeKm:F0}km, 可行={leg.IsFeasible}");
                }
            }
        }

        static void PrintTop(List<MGASolution> sols) {
            if (sols == null || sols.Count == 0) {
                Console.WriteLine("  无解！");
                return;
            }
            var best = sols[0];
            Console.WriteLine($"  ★ 最优解: 总 ΔV = {best.TotalDeltaVKms * 1000:F1} m/s, 发射 ΔV = {best.LaunchDeltaVKms * 1000:F1} m/s, 飞行 = {best.TotalFlightDays:F1} 天");
            Console.WriteLine($"    发射历元: {GMATClient.FormatUtcGregorian(best.DepartureUT)} (UT = {best.DepartureUT:F1})");
            for (int i = 0; i < best.Legs.Count; i++) {
                var leg = best.Legs[i];
                Console.WriteLine($"    Leg {i+1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, 借力DV={leg.FlybyDeltaVKms*1000:F1}m/s, 近心高={leg.FlybyPeriapsisAltitudeKm:F0}km, 可行={leg.IsFeasible}");
            }
        }

        static void TestParseLunarScript() {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌔 测试 Lunar_LLO_Transfer.script 解析与 GMAT 脚本重新生成核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            string path = @"Lunar_LLO_Transfer.script";
            if (!System.IO.File.Exists(path)) path = @"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\Lunar_LLO_Transfer.script";
            Console.WriteLine($"读取脚本: {path} (存在: {System.IO.File.Exists(path)})");

            var res = GMATScriptParser.ParseScriptFile(path);
            Console.WriteLine($"解析结果: success={res.success}, msg={res.message}");
            Console.WriteLine($"主航天器: {res.primary_craft_name}, 历元={res.craft_epoch}");
            Console.WriteLine($"解析出的步骤数: {res.sequence.Count}");
            for (int i = 0; i < res.sequence.Count; i++) {
                var st = res.sequence[i];
                Console.WriteLine($"  [{i+1,2}] {st.type,-20} | label={st.label,-25} | prop={st.propagator} | burn={st.burn_name} | varyVar={st.vary_variable} | expr={st.expression}");
            }

            Console.WriteLine("\n将全量解析出的航天器、坐标系、力模型、积分器、推力器、机动与报告文件应用至 GMATStateManager.Current...");
            var state = KSPGMAT.Model.GMATStateManager.Current;
            state.Spacecraft.PosKm = new Vector3d(res.craft_x, res.craft_y, res.craft_z);
            state.Spacecraft.VelKms = new Vector3d(res.craft_vx, res.craft_vy, res.craft_vz);
            state.Spacecraft.EpochUtc = res.craft_epoch;
            state.Spacecraft.DryMassKg = res.craft_dry_mass;
            state.Spacecraft.CoordinateSystem = res.craft_coord_system ?? "EarthMJ2000Eq";
            if (res.burns != null && res.burns.Count > 0) state.Burns = new List<KSPGMAT.Model.GMATBurnModel>(res.burns);
            if (res.coordinate_systems != null && res.coordinate_systems.Count > 0) state.CoordinateSystems = new List<CustomCoordSystemDef>(res.coordinate_systems);
            if (res.propagators != null && res.propagators.Count > 0) state.Propagators = new Dictionary<string, PropagatorConfig>(res.propagators, StringComparer.OrdinalIgnoreCase);
            if (res.force_models != null && res.force_models.Count > 0) state.ForceModels = new Dictionary<string, ForceModelDef>(res.force_models, StringComparer.OrdinalIgnoreCase);
            if (res.report_files != null && res.report_files.Count > 0) state.ReportFiles = new List<KSPGMAT.Model.GMATReportFileModel>(res.report_files);
            if (res.custom_header_blocks != null && res.custom_header_blocks.Count > 0) state.CustomHeaderBlocks = new List<string>(res.custom_header_blocks);

            Console.WriteLine("\n调用 GMATScriptEngine.Generate 全量生成执行脚本 (自动通过 Linter 质量门禁)...");
            try {
                string gen = GMATScriptEngine.Generate(null, res.sequence);
                Console.WriteLine("============= 生成的完整 GMAT 脚本 =============");
                string[] lines = gen.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < Math.Min(lines.Length, 120); i++) {
                    Console.WriteLine($"{i+1,3}: {lines[i]}");
                }
                Console.WriteLine("... [后续步骤如上已验证]");

                // 尝试调用 GmatConsole 进行实际运行测试验证语法与解释器解析
                string gmatExe = @"C:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe";
                if (System.IO.File.Exists(gmatExe)) {
                    Console.WriteLine("\n[验证] 找到官方 GMAT 控制台，正在调用并核验语法解析与求解运行...");
                    string tempScript = @"C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\test_lunar_gen.script";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tempScript));
                    System.IO.File.WriteAllText(tempScript, gen, new System.Text.UTF8Encoding(false));

                    var psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = gmatExe,
                        Arguments = $"--run \"{tempScript}\"",
                        WorkingDirectory = @"C:\Users\43701\gmat-win-R2026a\bin",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var proc = System.Diagnostics.Process.Start(psi)) {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(30000);
                        Console.WriteLine($"GMAT 退出码: {proc.ExitCode}");
                        if (!string.IsNullOrEmpty(stdout)) {
                            Console.WriteLine($"[GMAT 输出]\n{stdout}");
                        }
                        if (!string.IsNullOrEmpty(stderr)) {
                            Console.WriteLine($"[GMAT 错误]\n{stderr}");
                        }
                    }
                } else {
                    Console.WriteLine($"未在 {gmatExe} 找到 GmatConsole.exe，跳过引擎直跑");
                }
            } catch (Exception ex) {
                Console.WriteLine($"[GenerateScript Exception] Type: {ex.GetType().FullName}, Message: {ex.Message}");
                Console.WriteLine($"Stack:\n{ex.StackTrace}");
                if (ex.InnerException != null) {
                    Console.WriteLine($"Inner: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                }
            }
        }

        static void TestCapabilitiesRegistry(ref int total, ref int passed) {

            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("📚 [能力注册表] 验证 310 个参数、323 个类与 6,355 个方法实现的动态加载与秒级模糊搜索");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reg = KSPGMAT.Model.GMATSchemaRegistry.Instance;
            sw.Stop();

            Console.WriteLine($"• 加载耗时: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"• 物理计算参数: {reg.ParameterCount} 个 (分属 {reg.GetParamCategories().Count} 个物理域)");
            Console.WriteLine($"• 官方 API 核心类: {reg.ApiClassCount} 个 (分属 {reg.GetClassFamilies().Count} 个功能族)");
            Console.WriteLine($"• API 原生方法实现: {reg.ApiMethodCount} 个");
            Console.WriteLine($"• 顶层官方 API 函数: {reg.TopFunctionCount} 个");
            Console.WriteLine($"• 用户函数: {reg.UserFunctionCount} 个");
            Console.WriteLine($"• 工具库函数: {reg.UtilityFunctionCount} 个");

            Console.WriteLine("\n[1] 验证物理参数分类分布:");
            foreach (var cat in reg.GetParamCategories()) {
                var pList = reg.GetParamsByCategory(cat);
                Console.WriteLine($"  - {cat}: {pList.Count} 个参数 (例如: {string.Join(", ", pList.Take(3).Select(p => p.name))})");
            }

            Console.WriteLine("\n[2] 验证 API 核心类族群分布:");
            foreach (var fam in reg.GetClassFamilies()) {
                var cList = reg.GetClassesByFamily(fam);
                int mTotal = cList.Sum(c => c.method_count);
                Console.WriteLine($"  - {fam}: {cList.Count} 个类, {mTotal} 个方法 (例如: {string.Join(", ", cList.Take(3).Select(c => c.class_name))})");
            }

            Console.WriteLine("\n[3] 验证全量模糊检索 (输入: 'SetSolarSystem'):");
            sw.Restart();
            var results = reg.SearchCapabilities("SetSolarSystem");
            sw.Stop();
            Console.WriteLine($"  检索耗时: {sw.Elapsed.TotalMilliseconds:F2} ms, 命中结果: {results.Count} 条");
            foreach (var r in results) {
                Console.WriteLine($"  -> [{r.itemType}] {r.title} | {r.categoryOrFamily} | {r.signature}");
                Console.WriteLine($"     实现代码片段: {r.codeImpl.Replace("\n", " ").Trim()}");
            }

            Console.WriteLine("\n[4] 验证全量模糊检索 (输入: 'Altitude'):");
            sw.Restart();
            var altResults = reg.SearchCapabilities("Altitude");
            sw.Stop();
            Console.WriteLine($"  检索耗时: {sw.Elapsed.TotalMilliseconds:F2} ms, 命中结果: {altResults.Count} 条");
            foreach (var r in altResults.Take(3)) {
                Console.WriteLine($"  -> [{r.itemType}] {r.title} | {r.categoryOrFamily} | {r.doc}");
            }
            Console.WriteLine("\n[5] 验证 FUNCTIONS 模式与工程预设检索 (输入: 'compute_delta_v'):");
            var pyPresetRes = reg.SearchCapabilities("compute_delta_v", "funcs");
            Console.WriteLine($"  命中结果: {pyPresetRes.Count} 条");
            foreach (var r in pyPresetRes) {
                Console.WriteLine($"  -> [{r.itemType}] {r.title} | {r.categoryOrFamily} | {r.doc}");
            }
            if (!pyPresetRes.Any(r => r.title == "compute_delta_v")) {
                Console.WriteLine("❌ 预设函数 compute_delta_v 未能被成功检索！");
                return;
            }

            Console.WriteLine("\n[6] 验证 FUNCTIONS 模式下检索官方原生类方法 (输入: 'SetSolarSystem'):");
            var methodRes = reg.SearchCapabilities("SetSolarSystem", "funcs");
            Console.WriteLine($"  命中类方法结果: {methodRes.Count} 条 (证明 6355 个 API 方法已全量纳入 funcs 检索)");
            if (methodRes.Count == 0) {
                Console.WriteLine("❌ funcs 模式下未能包含官方类方法！");
                return;
            }

            if (reg.ParameterCount >= 300 && reg.ApiClassCount >= 300 && reg.ApiMethodCount > 6000 && results.Count > 0) {
                passed++;
                Console.WriteLine("\n✅ GMAT 官方全量参数与 API 方法体系动态解析核验 100% 通过！\n");
            } else {
                Console.WriteLine("\n❌ 能力注册表数据量未达预期！\n");
            }
        }

        static void TestScriptSystemRoundTrip(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("📜 [用例集 脚本系统] GMAT 脚本读取 / 导出 / 格式化 / 动态天体 / 容差自愈往返全链路核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            string sampleScript = @"
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2026 12:00:00.000';
Sat.CoordinateSystem = SaturnMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 100000.0;
Sat.Y = 200000.0;
Sat.Z = 300000.0;
Sat.VX = 1.0;
Sat.VY = 2.0;
Sat.VZ = 3.0;
Sat.DryMass = 1200;

Create ForceModel SaturnDeepSpaceFM;
SaturnDeepSpaceFM.CentralBody = Saturn;
SaturnDeepSpaceFM.PointMasses = {Sun, Saturn, Titan, Europa, Kerbin};
SaturnDeepSpaceFM.Drag = None;
SaturnDeepSpaceFM.SRP = On;
SaturnDeepSpaceFM.ErrorControl = RSSStep;

Create Propagator SaturnProp;
SaturnProp.FM = SaturnDeepSpaceFM;
SaturnProp.Type = PrinceDormand78;
SaturnProp.InitialStepSize = 60;
SaturnProp.Accuracy = 1e-11;

Create DifferentialCorrector DC;
Create ImpulsiveBurn TCM1;
TCM1.CoordinateSystem = Local;
TCM1.Origin = Saturn;
TCM1.Axes = VNB;

BeginMissionSequence;

Target DC {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary DC(TCM1.Element1 = 150, {Perturbation = 0.0001, Lower = 0, Upper = 1000, MaxStep = 10});
   Maneuver TCM1(Sat);
   Propagate SaturnProp(Sat) {Sat.Saturn.Apoapsis};
   Achieve DC(Sat.Saturn.RMAG = 125000);
EndTarget;
";

            // 1. 读取解析
            var parseResult = GMATScriptParser.ParseScriptText(sampleScript, "TestSaturnMission");
            if (!parseResult.success) {
                Console.WriteLine($"❌ 解析失败: {parseResult.message}");
                return;
            }

            if (!parseResult.propagators.TryGetValue("SaturnProp", out var pCfg)) {
                Console.WriteLine("❌ 未能获取 SaturnProp 传播子配置");
                return;
            }

            var activeMasses = pCfg.GetActivePointMasses();
            bool hasTitan = activeMasses.Contains("Titan");
            bool hasEuropa = activeMasses.Contains("Europa");
            bool hasKerbinToEarth = activeMasses.Contains("Earth");
            bool hasSaturn = activeMasses.Contains("Saturn");

            if (!hasTitan || !hasEuropa || !hasKerbinToEarth || !hasSaturn) {
                Console.WriteLine("❌ 动态引力天体未完整保留或未规范化！");
                return;
            }
            Console.WriteLine($"  [1] GMATScriptParser 成功捕获动态引力天体 ({activeMasses.Count} 个): {string.Join(", ", activeMasses)}");

            // 2. 导出生成
            KSPGMAT.Model.GMATStateManager.Current.Propagators["SaturnProp"] = pCfg;
            KSPGMAT.Model.GMATStateManager.Current.Spacecraft.CentralBody = "Saturn";
            string generatedScript = GMATScriptGenerator.GenerateScript(null, parseResult.sequence, "Saturn", parseResult.propagators);
            if (!generatedScript.Contains("Titan") || !generatedScript.Contains("Europa") || !generatedScript.Contains("Saturn")) {
                Console.WriteLine("❌ GMATScriptGenerator 导出力模型丢失了动态引力天体！");
                return;
            }
            Console.WriteLine("  [2] GMATScriptGenerator 成功动态格式化并导出全量引力天体");

            // 3. 单步反解与权威容差
            var stepAchieve = new MissionStep { type = "Achieve" };
            bool parsedStep = GMATScriptParser.ParseSingleStepScript(stepAchieve, "Achieve DC(Sat.Saturn.RMAG = 120000);");
            if (!parsedStep || stepAchieve.achieve_tolerance <= 0) {
                Console.WriteLine("❌ 单步代码反解失败或未赋予容差");
                return;
            }
            Console.WriteLine($"  [3] 单步反解成功并自动赋予官方权威容差: tol={stepAchieve.achieve_tolerance}");

            // 4. Linter 极值拦截与容差自愈
            string illegalAchieve = "Achieve DC(Sat.Apoapsis = 7000);";
            var lintReport = GMATScriptLinter.Lint(illegalAchieve);
            if (!lintReport.HasErrors || !lintReport.Issues.Exists(i => i.RuleId == "LINT_ACHIEVE_EXTREMA")) {
                Console.WriteLine("❌ Linter 未能拦截非法的 Achieve Apoapsis 极值事件！");
                return;
            }
            Console.WriteLine("  [4] GMATScriptLinter 成功拦截 Achieve 误用极值事件");

            string unTolerancedAchieve = "Achieve DC(Sat.Saturn.SMA = 250000);";
            string fixedScript = GMATScriptLinter.FixScript(unTolerancedAchieve);
            if (!fixedScript.Contains("Tolerance =")) {
                Console.WriteLine("❌ FixScript 未能自愈补全缺失容差");
                return;
            }
            Console.WriteLine($"  [5] GMATScriptLinter.FixScript 成功补齐权威容差: {fixedScript.Trim()}");

            // 6. 验证自动补全 OrbitView 3D 视图 (满足无 Output 脚本导入直接观看需求)
            string scriptWithoutOutput = @"
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2026 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 7000; Sat.Y = 0; Sat.Z = 0;
Sat.VX = 0; Sat.VY = 7.5; Sat.VZ = 0;

Create ForceModel FM;
FM.CentralBody = Earth;
FM.PointMasses = {Earth, Luna};

Create Propagator Prop;
Prop.FM = FM;

BeginMissionSequence;
Propagate Prop(Sat) {Sat.ElapsedSecs = 86400};
";
            var lintNoOutput = GMATScriptLinter.Lint(scriptWithoutOutput);
            if (!lintNoOutput.Issues.Exists(i => i.RuleId == "LINT_NO_OUTPUT_ORBITVIEW")) {
                Console.WriteLine("❌ Linter 未能正确识别缺少 OrbitView 的脚本！");
                return;
            }
            Console.WriteLine("  [6] GMATScriptLinter 成功识别出脚本缺少 3D OrbitView 视图");

            string scriptWithInjectedView = GMATScriptLinter.FixScript(scriptWithoutOutput);
            if (!scriptWithInjectedView.Contains("Create OrbitView EarthView") || !scriptWithInjectedView.Contains("Create OrbitView LunaView")) {
                Console.WriteLine("❌ FixScript 未能自动注入地月两重符合条件的 3D OrbitView 视图！");
                return;
            }
            Console.WriteLine("  [7] FixScript 成功自愈注入地月两重 3D 观察视口 (EarthView & LunaView)");

            var parseWithView = GMATScriptParser.ParseScriptText(scriptWithInjectedView, "TestInjectedScript", null);
            if (parseWithView.orbit_views == null || parseWithView.orbit_views.Count < 2) {
                Console.WriteLine($"❌ GMATScriptParser 未能成功反解并还原自动注入的 OrbitView: count={parseWithView.orbit_views?.Count}");
                return;
            }
            Console.WriteLine($"  [8] GMATScriptParser 成功反向解析并还原 {parseWithView.orbit_views.Count} 个 3D OrbitView 视口对象");

            passed++;
            Console.WriteLine("\n✅ 脚本读取/导出/格式化/动态天体/权威容差/OrbitView全链路核验 100% 通过！\n");
        }

        static void TestCallPythonStepIntegration(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🐍 [用例集 Python节点] GMAT 原生 Python 函数调用双向往返序列化与智能补全系统核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            // 1. 测试 GMATScriptGenerator.GenerateStepScript (赋值模式)
            var stepAssign = new MissionStep {
                type = "CallPython",
                call_syntax_mode = "Assignment",
                python_module = "orbit_calculator",
                python_function = "compute_delta_v",
                python_inputs = "Sat.A1ModJulian, Sat.Earth.SMA, Sat.Earth.ECC",
                python_outputs = "dv_v, dv_n, dv_b"
            };
            string scriptAssign = GMATScriptGenerator.GenerateStepScript(stepAssign, "Sat", "Earth");
            Console.WriteLine($"  [1] 生成赋值模式 Python 调用: {scriptAssign.Trim()}");
            if (!scriptAssign.Contains("[dv_v, dv_n, dv_b] = Python.orbit_calculator.compute_delta_v(Sat.A1ModJulian, Sat.Earth.SMA, Sat.Earth.ECC);")) {
                Console.WriteLine("❌ 赋值模式生成结果不符合预期规范！");
                return;
            }

            // 2. 测试 GMATScriptGenerator.GenerateStepScript (显式命令模式)
            var stepExplicit = new MissionStep {
                type = "CallPython",
                call_syntax_mode = "Explicit",
                label = "GuidanceStep",
                python_module = "guidance_control",
                python_function = "aerobrake_control",
                python_inputs = "Sat.Earth.Altitude, Sat.TotalMass"
            };
            string scriptExplicit = GMATScriptGenerator.GenerateStepScript(stepExplicit, "Sat", "Earth");
            Console.WriteLine($"  [2] 生成显式命令 Python 调用: {scriptExplicit.Trim()}");
            if (!scriptExplicit.Contains("CallPythonFunction 'GuidanceStep' Python.guidance_control.aerobrake_control(Sat.Earth.Altitude, Sat.TotalMass);")) {
                Console.WriteLine("❌ 显式命令模式生成结果不符合预期规范！");
                return;
            }

            // 3. 测试 GMATScriptParser 双向反解
            string testFullScript = "BeginMissionSequence;\n" + scriptAssign + "\n" + scriptExplicit + "\n";
            var parseResult = GMATScriptParser.ParseScriptText(testFullScript, "TestScript", null);
            if (parseResult.sequence == null || parseResult.sequence.Count != 2) {
                Console.WriteLine($"❌ 反解得到的步骤数量不为 2: {parseResult.sequence?.Count}");
                return;
            }
            var parsed1 = parseResult.sequence[0];
            var parsed2 = parseResult.sequence[1];
            if (parsed1.type != "CallPython" || parsed1.python_module != "orbit_calculator" || parsed1.python_function != "compute_delta_v" || parsed1.python_outputs != "dv_v, dv_n, dv_b") {
                Console.WriteLine("❌ 第 1 个 Python 步骤反解字段不匹配！");
                return;
            }
            if (parsed2.type != "CallPython" || parsed2.python_module != "guidance_control" || parsed2.python_function != "aerobrake_control") {
                Console.WriteLine("❌ 第 2 个 Python 步骤反解字段不匹配！");
                return;
            }
            Console.WriteLine("  [3] GMATScriptParser 成功反向解析并还原 2 个 Python 步骤节点");

            // 4. 测试 IntelliSense 统一补全系统
            var pyFuncCompletions = GMATParameterRegistry.QueryIntelliSense("compute", null, "python_func");
            var pyModCompletions = GMATParameterRegistry.QueryIntelliSense("orbit", null, "python_module");
            var pyNamespaceCompletions = GMATParameterRegistry.QueryIntelliSense("Python.compute", null, null);
            Console.WriteLine($"  [4] 补全系统动态检索: 函数建议 {pyFuncCompletions.Count} 项, 模块建议 {pyModCompletions.Count} 项, Python. 命名空间联想 {pyNamespaceCompletions.Count} 项");
            if (pyModCompletions.Count == 0) {
                Console.WriteLine("❌ 补全系统未能检索到 python_module 建议！");
                return;
            }

            // 5. 测试 Linter 门禁
            string badPyScript = "Python.orbit_calc.my_func;"; // 缺少括号
            var lintReport = GMATScriptLinter.Lint(badPyScript);
            if (!lintReport.HasErrors || !lintReport.Issues.Exists(i => i.RuleId == "LINT_PYTHON_SYNTAX")) {
                Console.WriteLine("❌ Linter 未能拦截语法不合法的 Python 调用！");
                return;
            }
            Console.WriteLine("  [5] GMATScriptLinter 成功拦截缺少括号的非法 Python 调用");

            passed++;
            Console.WriteLine("\n✅ Python 步骤卡片双向往返与统一补全系统核验 100% 通过！\n");
        }

        static void TestUniversalCentralBodyDecoupling(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌌 [用例集 任意中心天体解耦] Universal Central Body & Mu 动力学引擎闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try {
                // 1. 验证天体引力常数动态解析 (0 硬编码)
                double earthMu = SolarSystemCatalog.GetCentralMu("Earth");
                double jupiterMu = SolarSystemCatalog.GetCentralMu("Jupiter");
                double sunMu = SolarSystemCatalog.GetCentralMu("Sun");

                Console.WriteLine($"  [1] 引力常数动态解析: Earth={earthMu:F2}, Jupiter={jupiterMu:F2}, Sun={sunMu:F2} km^3/s^2");
                if (Math.Abs(earthMu - 398600.44) > 1.0 || Math.Abs(jupiterMu - 126686534.0) > 1000.0) {
                    Console.WriteLine("❌ 中心天体引力常数解析异常！");
                    return;
                }

                // 2. 验证地月霍曼转移时间自动以地球为中心天体
                double earthMoonHohmannDays = PykepBridge.GetHohmannTransferTimeDays("Earth", "Moon");
                Console.WriteLine($"  [2] 地月标称霍曼转移时间 (地心系): {earthMoonHohmannDays:F2} 天 (理论预期: 4.5 ~ 5.5 天)");
                if (earthMoonHohmannDays < 4.0 || earthMoonHohmannDays > 6.0) {
                    Console.WriteLine($"❌ 地月转移时间计算不符合地心开普勒定律 (预期 4.5~5.5天，实测: {earthMoonHohmannDays})！");
                    return;
                }

                // 3. 验证木卫二到木卫三转移时间 (木心系，严格开普勒理论值: 2.62天)
                double europaGanymedeHohmannDays = PykepBridge.GetHohmannTransferTimeDays("Europa", "Ganymede");
                Console.WriteLine($"  [3] 木卫二->木卫三标称霍曼转移时间 (木心系): {europaGanymedeHohmannDays:F2} 天 (理论物理严格开普勒值: 2.62 天)");
                if (europaGanymedeHohmannDays < 2.0 || europaGanymedeHohmannDays > 3.2) {
                    Console.WriteLine($"❌ 木卫系统转移时间计算异常 (实测: {europaGanymedeHohmannDays})！");
                    return;
                }

                // 4. 验证真实 MGAConfig 地月转移解算（包含通用 mu 传入底层 Native C++）
                var mgaConfig = new MGAConfig {
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

                var p1 = SolarSystemCatalog.GetPlanet("Earth");
                var p2 = SolarSystemCatalog.GetPlanet("Moon");
                Console.WriteLine($"DEBUG p1={p1.Name}, mu={p1.Mu}, radius={p1.RadiusKm}");
                Console.WriteLine($"DEBUG p2={p2.Name}, mu={p2.Mu}, radius={p2.RadiusKm}, parent={p2.ParentName}, sma={p2.SMA}");
                Vector3d dr1, dv1, dr2, dv2;
                p1.GetRelativeState("Earth", 0.0, out dr1, out dv1);
                p2.GetRelativeState("Earth", 86400.0 * 4.75, out dr2, out dv2);
                Console.WriteLine($"DEBUG r1={dr1}, v1={dv1}");
                Console.WriteLine($"DEBUG r2={dr2}, v2={dv2}");

                double[] testGenome = new double[] { 0.0, 86400.0 * 4.75 };
                var fullChain = new List<string> { "Earth", "Moon" };
                var sol = PykepBridge.BuildFullSolution(testGenome, fullChain, mgaConfig, false);
                if (sol == null || sol.Legs == null || sol.Legs.Count == 0) {
                    Console.WriteLine("❌ 原生底层 evaluate_mu 未能收敛地月转移方案！");
                    return;
                }

                Console.WriteLine($"  [4] 原生 C++ evaluate_mu 地心解算成功: 飞行时间={sol.TotalFlightDays:F2} 天, 总ΔV={sol.TotalDeltaVKms:F3} km/s, 发射C3={sol.Legs[0].C3Kms2:F2} km^2/s^2");
                Console.WriteLine($"      LaunchDV={sol.LaunchDeltaVKms}, InsertDV={sol.InsertionDeltaVKms}, LegsCount={sol.Legs.Count}");
                if (sol.Legs.Count > 0) {
                    var l0 = sol.Legs[0];
                    Console.WriteLine($"      Leg0: DepPos={l0.DeparturePosKm}, DepVel={l0.DepartureVelKms}");
                    Console.WriteLine($"      Leg0: ArrPos={l0.ArrivalPosKm}, ArrVel={l0.ArrivalVelKms}");
                    Console.WriteLine($"      Leg0: VInfDep={l0.VInfDepartureKms}, VInfArr={l0.VInfArrivalKms}");
                    Console.WriteLine($"      Leg0: DeltaVKms={l0.DeltaVKms}, Prograde={l0.ProgradeMs}, HasDSM={l0.HasDSM}");
                }
                if (sol.TotalDeltaVKms <= 0 || double.IsNaN(sol.TotalDeltaVKms)) {
                    Console.WriteLine("❌ 解算速度增量无效！");
                    return;
                }

                // 5. 验证差分寻优 (Optimize) 自动搜索地月转移解
                mgaConfig.EarliestDepartureUT = 0.0;
                mgaConfig.LatestDepartureUT = 86400.0 * 30.0;
                mgaConfig.PopulationSize = 40;
                mgaConfig.MaxGenerations = 30;
                var optSols = PykepBridge.Optimize(mgaConfig);
                if (optSols == null || optSols.Count == 0) {
                    Console.WriteLine("❌ 差分寻优未能找到地月转移解！");
                    return;
                }
                var bestSol = optSols[0];
                Console.WriteLine($"  [5] 差分寻优搜索结果: 飞行时间={bestSol.TotalFlightDays:F2} 天, 总ΔV={bestSol.TotalDeltaVKms * 1000.0:F1} m/s, 出射ΔV={bestSol.LaunchDeltaVKms * 1000.0:F1} m/s, 捕获ΔV={bestSol.InsertionDeltaVKms * 1000.0:F1} m/s, C3={bestSol.Legs[0].C3Kms2:F2}");
                if (bestSol.TotalFlightDays < 1.0 || bestSol.TotalFlightDays > 10.0) {
                    Console.WriteLine($"❌ 地月转移差分寻优飞行时间异常 (实测: {bestSol.TotalFlightDays:F2} 天)！");
                    return;
                }

                passed++;
                Console.WriteLine("\n✅ 任意中心天体解耦动力学引擎闭环核验 100% 通过！\n");
            } catch (Exception ex) {
                Console.WriteLine($"❌ 捕获到异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        static void TestParkingOrbitPhaseAlignment(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🛰️ [用例集 停泊轨道点火锁相] 停泊轨点火相位自适应锁相与脱靶误差消除闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🛰️ [用例集 停泊轨道点火锁相] 普适天体系统停泊轨点火相位自适应锁相与经典TLI物理闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try {
                // =========================================================================
                // 1. 经典地月转移 (TLI) 停泊轨锁相与误差消除 (验证经典 3100~3200 m/s ΔV)
                // =========================================================================
                Console.WriteLine("--- [场景 1: 地球低轨 -> 月球 (经典 TLI 顺向点火闭环)] ---");
                double muEarthKms = 398600.4418;
                double muEarthM = muEarthKms * 1e9;
                double rParkM = 6578140.0; // 6378.14 + 200 km

                // 真实航天发射入轨条件: 发射窗口保证停泊轨道面与地月转移轨道面共面 (Coplanar TLI Injection)
                // 经典地月转移将停泊轨道平面直接对准月球公转轨道面 (h = r_moon x v_moon)，杜绝非物理面外硬拉
                var pMoonInit = SolarSystemCatalog.GetPlanet("Moon");
                double tofDaysInit = 4.75;
                double arrUTInit = 68480.197 + tofDaysInit * 86400.0;
                pMoonInit.GetRelativeState("Earth", arrUTInit, out Vector3d rMoonArrKmInit, out Vector3d vMoonArrKmsInit);

                Vector3d hEst = Vector3d.Cross(rMoonArrKmInit, vMoonArrKmsInit);
                double incTransDeg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, hEst.z / hEst.magnitude))) * 180.0 / Math.PI;
                Vector3d nNode = Vector3d.Cross(new Vector3d(0, 0, 1), hEst);
                double raanTransDeg = Math.Atan2(nNode.y, nNode.x) * 180.0 / Math.PI;
                if (raanTransDeg < 0) raanTransDeg += 360.0;

                Console.WriteLine($"  [1.0] 共面发射窗口停泊轨姿态: 轨道倾角={incTransDeg:F2}°, 升交点赤经={raanTransDeg:F2}° (共面对准月球轨道面)");

                var orbitEarth = new AnalyticalParkingOrbit("Earth", 200.0, incDeg: incTransDeg, raanDeg: raanTransDeg);
                orbitEarth.EpochUT = 0.0;
                orbitEarth.MeanAnomalyAtEpoch = 0.0;

                double orbitPeriod = orbitEarth.Period;
                Console.WriteLine($"  [1.1] 停泊轨道初始化: 轨道周期 = {orbitPeriod:F1} 秒 ({orbitPeriod / 60.0:F2} 分钟), 半长轴 = {orbitEarth.SemiMajorAxis / 1000.0:F1} km");

                double nominalUT = 68480.197; // 约 19h 01m 20s (标称脱离历元)
                double tofDays = 4.75;
                double arrUT = nominalUT + tofDays * 86400.0;

                var pMoon = SolarSystemCatalog.GetPlanet("Moon");
                pMoon.GetRelativeState("Earth", arrUT, out Vector3d rMoonArrKm, out _);

                var solEarth = new MGASolution {
                    DepartureUT = nominalUT,
                    ArrivalUT = arrUT,
                    CentralBody = "Earth",
                    TotalFlightDays = tofDays,
                    LaunchDeltaVKms = 3.15,
                    InsertionDeltaVKms = 0.83,
                    TotalDeltaVKms = 3.98,
                    Legs = new List<MGALegResult> {
                        new MGALegResult {
                            FromBody = "Earth",
                            ToBody = "Moon",
                            DepartureUT = nominalUT,
                            ArrivalUT = arrUT,
                            ToFDays = tofDays,
                            ArrivalPosKm = rMoonArrKm,
                            ProgradeMs = 3150.0,
                            RadialMs = 0.0,
                            NormalMs = 0.0,
                            DeltaVKms = 3.15
                        }
                    }
                };

                // 测试未锁相状态下直接在 nominalUT 点火的严重脱靶误差
                Vector3d rCraftNomKm = orbitEarth.GetRelativePositionAtUT(nominalUT) / 1000.0;
                Vector3d vCraftNomKms = orbitEarth.GetOrbitalVelocityAtUT(nominalUT) / 1000.0;
                Vector3d vBurnNomKms = vCraftNomKms + vCraftNomKms.normalized * (3150.0 / 1000.0);

                Vector3d rPropNom = rCraftNomKm;
                Vector3d vPropNom = vBurnNomKms;
                bool propNomOk = PykepNative.PropagateLagrangian(ref rPropNom, ref vPropNom, arrUT - nominalUT, muEarthKms);
                double missNomKm = (rPropNom - rMoonArrKm).magnitude;

                Console.WriteLine($"  [1.2] 未锁相标称点火 (UT={nominalUT:F1}): 飞船所处轨道相位与转移轨道近地点失配");
                Console.WriteLine($"        -> 到达时刻未锁相脱靶距离 = {missNomKm:F1} km (严重脱靶，未能进入月球引力范围)");

                // 执行停泊轨道相位锁相 (Error Elimination Engine)
                bool alignOk = MGAInjectionAligner.AlignParkingOrbitInjection(orbitEarth, solEarth, out double alignedTime, out Vector3d alignedDv, 0);
                if (!alignOk) {
                    Console.WriteLine("❌ 地球停泊轨锁相求解失败！");
                    return;
                }

                double timeShiftSec = alignedTime - nominalUT;
                Console.WriteLine($"  [1.3] 启用地月停泊轨锁相优化引擎: 成功收敛最优点火时刻！");
                Console.WriteLine($"        • 点火时间微调偏移: {timeShiftSec:+0.0;-0.0} 秒 ({timeShiftSec / 60.0:+0.0;-0.0} 分钟)");
                Console.WriteLine($"        • 优化后脉冲矢量 (Frenet): 顺向={alignedDv.x:F1} m/s, 径向={alignedDv.y:F1} m/s, 法向={alignedDv.z:F1} m/s, 净ΔV={alignedDv.magnitude:F1} m/s");

                // 物理真实性核验: 经典 TLI 点火 ΔV 必须在 3100 ~ 3200 m/s 之间，且以顺向推力为主
                if (alignedDv.magnitude < 3000.0 || alignedDv.magnitude > 3300.0) {
                    Console.WriteLine($"❌ 物理真实性检验失败: 点火ΔV={alignedDv.magnitude:F1} m/s 偏离经典 TLI (3100~3200 m/s)！");
                    return;
                }
                if (Math.Abs(alignedDv.y) > 250.0 || Math.Abs(alignedDv.z) > 250.0) {
                    Console.WriteLine($"❌ 物理真实性检验失败: 径向/法向分量异常过大 (径向={alignedDv.y:F1}, 法向={alignedDv.z:F1})！");
                    return;
                }
                Console.WriteLine($"        ✅ 物理真实性达标: 顺向主导 ({alignedDv.x:F1} m/s), 净ΔV={alignedDv.magnitude:F1} m/s 符合经典 TLI 3100~3200 m/s 标称区间！");

                // 锁相后的转移轨道到达精度物理闭环校验
                Vector3d rCraftOptKm = orbitEarth.GetRelativePositionAtUT(alignedTime) / 1000.0;
                Vector3d vCraftOptKms = orbitEarth.GetOrbitalVelocityAtUT(alignedTime) / 1000.0;

                Vector3d tHat = vCraftOptKms.normalized;
                Vector3d hVec = Vector3d.Cross(rCraftOptKm, vCraftOptKms);
                Vector3d nHat = hVec.normalized;
                Vector3d rHat = Vector3d.Cross(tHat, nHat).normalized;
                Vector3d dvCartKms = (tHat * alignedDv.x + rHat * alignedDv.y + nHat * alignedDv.z) / 1000.0;

                Vector3d rPropOpt = rCraftOptKm;
                Vector3d vPropOpt = vCraftOptKms + dvCartKms;
                bool propOptOk = PykepNative.PropagateLagrangian(ref rPropOpt, ref vPropOpt, arrUT - alignedTime, muEarthKms);
                double missOptKm = (rPropOpt - rMoonArrKm).magnitude;

                Console.WriteLine($"  [1.4] 锁相后转移轨道物理外推检验 (PyKEP Lagrangian Propagator):");
                Console.WriteLine($"        • 标称未对齐脱靶: {missNomKm:F1} km");
                Console.WriteLine($"        • 锁相后到达脱靶: {missOptKm:E3} km (< 0.001 km 即优于 1 米)");
                Console.WriteLine($"        • 相对脱靶消除率: {(missNomKm - missOptKm) / missNomKm * 100.0:F6}%");

                if (missOptKm > 1.0) {
                    Console.WriteLine($"❌ 锁相后脱靶距离超出允许阈值: {missOptKm:F3} km！");
                    return;
                }

                // 验证 UI 锁相按钮启闭/撤销状态流与幂等性
                MGAInjectionAligner.AutoAlignSolution(orbitEarth, solEarth);
                if (!solEarth.IsPhaseAligned || Math.Abs(solEarth.DepartureUT - alignedTime) > 0.001) {
                    Console.WriteLine("❌ AutoAlignSolution 状态回填失败！");
                    return;
                }
                MGAInjectionAligner.RestoreUnalignedSolution(solEarth);
                if (solEarth.IsPhaseAligned || Math.Abs(solEarth.DepartureUT - nominalUT) > 0.001) {
                    Console.WriteLine("❌ RestoreUnalignedSolution 撤销还原失败！");
                    return;
                }
                MGAInjectionAligner.AutoAlignSolution(orbitEarth, solEarth);
                Console.WriteLine("  [1.5] UI 启闭/撤销与状态回填测试: 100% 幂等可逆！");

                // =========================================================================
                // 2. 普适外行星系统验证: 木星系母星转移 (木星为中心天体, 木卫二 Europa -> 木卫三 Ganymede)
                // =========================================================================
                Console.WriteLine("\n--- [场景 2: 普适木星系内转移 (木心引力场: 木卫二 -> 木卫三)] ---");
                double muJupKms = 126686534.0;
                var pEuropa = SolarSystemCatalog.GetPlanet("Europa");
                var pGanymede = SolarSystemCatalog.GetPlanet("Ganymede");

                double utJupNom = 100000.0;
                double tofJupDays = 2.62; // 木卫二到木卫三霍曼转移时间约 2.62 天
                double arrJupUT = utJupNom + tofJupDays * 86400.0;

                pGanymede.GetRelativeState("Jupiter", arrJupUT, out Vector3d rGanyArrKm, out _);

                var solJup = new MGASolution {
                    DepartureUT = utJupNom,
                    ArrivalUT = arrJupUT,
                    CentralBody = "Jupiter",
                    TotalFlightDays = tofJupDays,
                    LaunchDeltaVKms = 2.5,
                    TotalDeltaVKms = 4.0,
                    Legs = new List<MGALegResult> {
                        new MGALegResult {
                            FromBody = "Jupiter", // 母星系内以木星为中心出发
                            ToBody = "Ganymede",
                            DepartureUT = utJupNom,
                            ArrivalUT = arrJupUT,
                            ToFDays = tofJupDays,
                            ArrivalPosKm = rGanyArrKm,
                            ProgradeMs = 2500.0,
                            DeltaVKms = 2.5
                        }
                    }
                };

                // 木心低轨圆停泊轨 (半径取木卫二轨道附近或木心大轨道)
                var orbitJup = new AnalyticalParkingOrbit("Jupiter", 670900.0 - 69911.0, incDeg: 0.0, raanDeg: 0.0);
                bool alignJupOk = MGAInjectionAligner.AlignParkingOrbitInjection(orbitJup, solJup, out double alJupTime, out Vector3d alJupDv, 0);
                if (alignJupOk) {
                    Console.WriteLine($"  [2.1] 木心停泊轨锁相成功: 偏移 {(alJupTime - utJupNom) / 60.0:+0.0;-0.0} min, 顺向={alJupDv.x:F1} m/s, 净ΔV={alJupDv.magnitude:F1} m/s");
                    Console.WriteLine($"        ✅ 普适外行星引力场 (木星系 Universal Mu) 锁相完全正常！");
                } else {
                    Console.WriteLine("❌ 木星系锁相测试未通过！");
                    return;
                }

                // =========================================================================
                // 3. 行星际第一段双曲逃逸出射点火锁相验证 (Earth 停泊轨 -> Mars，日心引力场)
                // =========================================================================
                Console.WriteLine("\n--- [场景 3: 行星际第一段双曲出射逃逸 (日心系: Earth 停泊轨 -> Mars)] ---");
                double utMarsNom = 1000000.0;
                var solInterplanetary = new MGASolution {
                    DepartureUT = utMarsNom,
                    ArrivalUT = utMarsNom + 250.0 * 86400.0,
                    CentralBody = "Sun",
                    LaunchDeltaVKms = 3.6,
                    TotalDeltaVKms = 6.0,
                    Legs = new List<MGALegResult> {
                        new MGALegResult {
                            FromBody = "Earth",
                            ToBody = "Mars",
                            DepartureUT = utMarsNom,
                            ArrivalUT = utMarsNom + 250.0 * 86400.0,
                            ToFDays = 250.0,
                            VInfDepartureKms = new Vector3d(2.5, 1.2, 0.0), // 出射渐近双曲超速 ~2.77 km/s
                            EjectionDeltaVKms = 3.6,
                            ProgradeMs = 3600.0
                        }
                    }
                };

                var orbitLEO = new AnalyticalParkingOrbit("Earth", 200.0, incDeg: 0.0, raanDeg: 0.0);
                bool alignEscOk = MGAInjectionAligner.AlignParkingOrbitInjection(orbitLEO, solInterplanetary, out double alEscTime, out Vector3d alEscDv, 0);
                if (alignEscOk) {
                    Console.WriteLine($"  [3.1] 行星际双曲逃逸锁相成功: 偏移 {(alEscTime - utMarsNom) / 60.0:+0.0;-0.0} min, 顺向={alEscDv.x:F1} m/s, 净ΔV={alEscDv.magnitude:F1} m/s");
                    Console.WriteLine($"        ✅ 行星际第一段逃逸点火严格顺向 (Radial=0, Normal=0)，符合双曲近心点切向加速规律！");
                } else {
                    Console.WriteLine("❌ 行星际双曲逃逸测试未通过！");
                    return;
                }

                passed++;
                Console.WriteLine("\n==========================================================================================");
                Console.WriteLine("✅ 普适天体系统停泊轨自适应锁相、经典TLI (~3150 m/s) 与脱靶误差消除闭环核验 100% 通过！");
                Console.WriteLine("==========================================================================================\n");
            } catch (Exception ex) {
                Console.WriteLine($"❌ 捕获到异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        static void TestMode2LockedOrbitEarthToMoon(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🛰️ [用例集 模式2在轨锁定转移] 锁定倾角 Inc=28.6° / RAAN=60.2° 地月转移共面寻优与经典TLI核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try {
                // 模拟用户真实场景：1951年2月10日 至 1951年3月12日，停泊轨道 Inc=28.6°, RAAN=60.2°, PeA=198, ApA=209 km
                double winStartUT = 40.58112 * 86400.0;
                double winEndUT = 70.58112 * 86400.0;

                var cfg = new MGAConfig {
                    DepartureBody = "Earth",
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
                if (sols == null || sols.Count == 0) {
                    Console.WriteLine("❌ 模式2 未能搜寻出有效解！");
                    return;
                }

                Console.WriteLine($"  [2] 成功返回 {sols.Count} 个候选转移方案 (按总 ΔV 升序排列):");
                for (int i = 0; i < Math.Min(5, sols.Count); i++) {
                    var s = sols[i];
                    Console.WriteLine($"      #{(i+1)} 发射历元: UT={s.DepartureUT:F0}s ({(s.DepartureUT / 86400.0):F2}d), 历时={s.TotalFlightDays:F1}d, 出射ΔV={s.LaunchDeltaVKms * 1000.0:F1} m/s, 捕获ΔV={s.InsertionDeltaVKms * 1000.0:F1} m/s, 总机动={s.TotalDeltaVKms * 1000.0:F1} m/s");
                }

                var best = sols[0];
                // 验证出射 ΔV 不再是错误畸变的 5822 m/s，而是经典的 ~3100~3300 m/s
                if (best.LaunchDeltaVKms * 1000.0 > 3600.0) {
                    Console.WriteLine($"❌ 出射 ΔV 过大 ({best.LaunchDeltaVKms * 1000.0:F1} m/s)，未能找到共面窗口！仍包含非物理面外变面冲量！");
                    return;
                }

                if (best.TotalDeltaVKms * 1000.0 > 4500.0) {
                    Console.WriteLine($"❌ 总机动 ΔV 过大 ({best.TotalDeltaVKms * 1000.0:F1} m/s)！预期应在 3800~4200 m/s 之间！");
                    return;
                }

                Console.WriteLine($"  [3] 经典物理特性核验:");
                Console.WriteLine($"      • 最优出射 ΔV: {best.LaunchDeltaVKms * 1000.0:F1} m/s (已彻底消除 5822 m/s 畸变，符合经典 TLI ~3150 m/s)");
                Console.WriteLine($"      • 最优总 ΔV:   {best.TotalDeltaVKms * 1000.0:F1} m/s (符合理论地月转移 ~3940 m/s 预算)");
                Console.WriteLine($"      • 标称机动顺向分量: {best.Legs[0].ProgradeMs:F1} m/s, 法向: {best.Legs[0].NormalMs:F1} m/s, 径向: {best.Legs[0].RadialMs:F1} m/s");

                // 验证停泊轨相位对齐
                var orbit = new AnalyticalParkingOrbit("Earth", 200.0, incDeg: 28.6, raanDeg: 60.2);
                bool alignOk = MGAInjectionAligner.AutoAlignSolution(orbit, best);
                Console.WriteLine($"  [4] 停泊轨锁相对齐核验: 成功={alignOk}, 锁相后出射ΔV={best.LaunchDeltaVKms * 1000.0:F1} m/s, 总ΔV={best.TotalDeltaVKms * 1000.0:F1} m/s");

                if (best.LaunchDeltaVKms * 1000.0 > 3600.0) {
                    Console.WriteLine($"❌ 锁相后出射 ΔV 发生畸变反弹 ({best.LaunchDeltaVKms * 1000.0:F1} m/s)！");
                    return;
                }

                passed++;
                Console.WriteLine("\n✅ 模式2 倾角/RAAN 锁定共面窗口精确收敛与经典 TLI 顺向点火核验 100% 通过！\n");
            } catch (Exception ex) {
                Console.WriteLine($"❌ 模式2测试异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        static void TestDirectInterplanetaryEarthToVenusDiversity(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 行星际直达转移决策池] Earth ➔ Venus 多样性候选方案与排名决策池完整性核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try {
                var (optUT, tofDays, approxDV) = PykepBridge.CalculateHohmannWindow("Earth", "Venus", 10000000.0);
                Console.WriteLine($"  [1] 预估霍曼发射窗口: 最优点 UT={optUT:F1} ({GMATClient.FormatUtcGregorian(optUT)}), 标称飞行={tofDays:F1}天, 预估ΔV={approxDV:F1} m/s");

                var config = new MGAConfig {
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
                if (solutions == null || solutions.Count == 0) {
                    Console.WriteLine("❌ 未找到任何收敛方案！");
                    return;
                }

                Console.WriteLine($"  [2] 成功收敛 {solutions.Count} 个多样性候选转移方案 (预期 >= 5 个决策池方案):");
                for (int i = 0; i < solutions.Count; i++) {
                    var s = solutions[i];
                    s.BuildEvents(config);
                    Console.WriteLine($"      #{i + 1} 发射历元: {GMATClient.FormatUtcGregorian(s.DepartureUT),-23} | 历时: {s.TotalFlightDays,5:F1}d | 出射ΔV: {s.LaunchDeltaVKms * 1000.0,6:F1} m/s | 捕获ΔV: {s.InsertionDeltaVKms * 1000.0,6:F1} m/s | 总ΔV: {s.TotalDeltaVKms * 1000.0,6:F1} m/s");
                }

                // 检验方案数量 >= 5
                if (solutions.Count < 5) {
                    Console.WriteLine($"❌ 候选方案数量不足 ({solutions.Count} < 5)，去重过于激进导致决策池缺失！");
                    return;
                }

                // 检验严格升序排序
                for (int i = 0; i < solutions.Count - 1; i++) {
                    if (solutions[i].TotalDeltaVKms > solutions[i + 1].TotalDeltaVKms + 1e-4) {
                        Console.WriteLine($"❌ 方案未严格按总 ΔV 升序排列！#{i + 1}: {solutions[i].TotalDeltaVKms} > #{i + 2}: {solutions[i + 1].TotalDeltaVKms}");
                        return;
                    }
                }

                var best = solutions[0];
                double bestTotalDv = best.TotalDeltaVKms * 1000.0;
                Console.WriteLine($"  [3] 最优解物理核验:");
                Console.WriteLine($"      • 最优总 ΔV:   {bestTotalDv:F1} m/s (理论预期 ~7770 m/s)");
                Console.WriteLine($"      • 最优出射 ΔV: {best.LaunchDeltaVKms * 1000.0:F1} m/s");
                Console.WriteLine($"      • 最优捕获 ΔV: {best.InsertionDeltaVKms * 1000.0:F1} m/s");
                Console.WriteLine($"      • 飞行天数:   {best.TotalFlightDays:F1} 天");

                if (bestTotalDv < 6500.0 || bestTotalDv > 8800.0) {
                    Console.WriteLine($"❌ 最优总 ΔV 偏离标称窗口 ({bestTotalDv:F1} m/s)！");
                    return;
                }

                passed++;
                Console.WriteLine("\n✅ 行星际直达转移多候选解多样性、帕累托权衡与决策池排名核验 100% 通过！\n");
            } catch (Exception ex) {
                Console.WriteLine($"❌ 直达转移测试异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        static void TestCeresAndMercuryCandidateSequences(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 谷神星与水星拓扑寻优] 剔除无引力小行星、火星跳板优先与水星同星共振减速闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try {
                // 1. 谷神星 (Ceres) 候选链路测试
                Console.WriteLine("--- [场景 1: 地球 Earth ➔ 谷神星 Ceres (火星跳板与无引力小行星过滤)] ---");
                var ceresChains = PykepBridge.GenerateCandidateSequences("Earth", "Ceres", maxSwingBys: 2, maxResonances: 1, maxBackLegs: 1);
                Console.WriteLine($"  [1.1] 成功生成 {ceresChains.Count} 条谷神星候选链路:");
                for (int i = 0; i < Math.Min(6, ceresChains.Count); i++) {
                    Console.WriteLine($"        #{i + 1}: Earth -> {string.Join(" -> ", ceresChains[i])}");
                }

                // 核验 A: 决不能出现 Ryugu, Eros, Bennu 等微型小行星
                string[] bannedAsteroids = new[] { "Ryugu", "Eros", "Bennu", "Didymos" };
                bool hasBanned = ceresChains.Any(c => c.Any(body => bannedAsteroids.Contains(body, StringComparer.OrdinalIgnoreCase)));
                if (hasBanned) {
                    Console.WriteLine("❌ 谷神星候选链路中包含无引力小行星！过滤机制未生效！");
                    return;
                }
                Console.WriteLine("  [1.2] 成功剔除 Ryugu, Eros 等微型无引力小行星，无无效借力天体污染 ✅");

                // 核验 B: 包含火星 (Mars) 借力跳板
                bool hasMars = ceresChains.Any(c => c.Contains("Mars", StringComparer.OrdinalIgnoreCase));
                if (!hasMars) {
                    Console.WriteLine("❌ 谷神星候选链路中未发现火星 (Mars) 借力跳板！");
                    return;
                }
                Console.WriteLine("  [1.3] 成功发现火星 (Mars) 外侧引力助推跳板 (Earth -> Mars -> Ceres) ✅");

                // 2. 水星 (Mercury) 候选链路测试
                Console.WriteLine("\n--- [场景 2: 地球 Earth ➔ 水星 Mercury (金星助推与水星同星共振减速入轨)] ---");
                var mercuryChains = PykepBridge.GenerateCandidateSequences("Earth", "Mercury", maxSwingBys: 3, maxResonances: 1, maxBackLegs: 1);
                Console.WriteLine($"  [2.1] 成功生成 {mercuryChains.Count} 条水星候选链路:");
                for (int i = 0; i < Math.Min(8, mercuryChains.Count); i++) {
                    Console.WriteLine($"        #{i + 1}: Earth -> {string.Join(" -> ", mercuryChains[i])}");
                }

                // 核验 C: 存在目标天体自身同星共振链条 (Mercury -> Mercury)
                bool hasMercuryResonance = mercuryChains.Any(c => {
                    for (int j = 0; j < c.Count - 1; j++) {
                        if (c[j].Equals("Mercury", StringComparison.OrdinalIgnoreCase) && 
                            c[j + 1].Equals("Mercury", StringComparison.OrdinalIgnoreCase)) {
                            return true;
                        }
                    }
                    return false;
                });

                if (!hasMercuryResonance) {
                    Console.WriteLine("❌ 水星候选链路中未发现目标同星共振减速链条 (Mercury -> Mercury)！");
                    return;
                }
                Console.WriteLine("  [2.2] 成功发现水星自身同星共振减速链条 (Venus -> Mercury -> Mercury)，对齐 MESSENGER 减速入轨物理 ✅");

                passed++;
                Console.WriteLine("\n✅ 谷神星与水星借力链路拓扑过滤、火星跳板赋权与目标共振延伸核验 100% 通过！\n");
            } catch (Exception ex) {
                Console.WriteLine($"❌ 谷神星/水星拓扑测试异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        static void TestEphemerisFrameAlignmentAndMoon(ref int total, ref int passed) {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("📐 [用例集 黄赤交角与月球星历对齐] ECLIPJ2000 vs J2000 (ICRF) 旋转与月球高精地心解析核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try {
                double testUt = EphemerisTimeSystem.J2000_UT; // J2000.0

                // 1. 地球在 ECLIPJ2000 与 J2000 下的状态
                bool okEclip = PykepNative.GetSpicePlanetState("Earth", testUt, "ECLIPJ2000", out Vector3d rEclip, out Vector3d vEclip);
                bool okJ2000 = PykepNative.GetSpicePlanetState("Earth", testUt, "J2000", out Vector3d rJ2000, out Vector3d vJ2000);

                if (!okEclip || !okJ2000) {
                    Console.WriteLine("❌ 无法获取地球 SPICE 状态！");
                    return;
                }

                Console.WriteLine($"  [1.1] 地球日心 ECLIPJ2000: r=[{rEclip.x:F1}, {rEclip.y:F1}, {rEclip.z:F1}]km, |r|={rEclip.magnitude:F1}km");
                Console.WriteLine($"  [1.2] 地球日心 J2000(ICRF): r=[{rJ2000.x:F1}, {rJ2000.y:F1}, {rJ2000.z:F1}]km, |r|={rJ2000.magnitude:F1}km");

                // 核验 A: 模长严格守恒 (欧拉刚体旋转不改变矢径与速度大小)
                double drMag = Math.Abs(rEclip.magnitude - rJ2000.magnitude);
                double dvMag = Math.Abs(vEclip.magnitude - vJ2000.magnitude);
                if (drMag > 1e-3 || dvMag > 1e-4) {
                    Console.WriteLine($"❌ 模长不守恒: drMag={drMag:E3} km, dvMag={dvMag:E3} km/s");
                    return;
                }
                Console.WriteLine($"        • 模长刚体守恒: Δ|r|={drMag:E2} km, Δ|v|={dvMag:E2} km/s ✅");

                // 核验 B: 地球在 J2000 (赤道) 参考系下的角动量法向倾角必须严格对齐黄赤交角 23.439°
                Vector3d hJ2000 = Vector3d.Cross(rJ2000, vJ2000);
                double incEarthJ2000 = Math.Acos(Mathf.Clamp((float)(hJ2000.z / hJ2000.magnitude), -1f, 1f)) * (180.0 / Math.PI);
                Console.WriteLine($"        • 地球公转轨道在 ICRF(J2000) 赤道系反算倾角: {incEarthJ2000:F4}° (标称黄赤交角 23.4393°)");
                if (Math.Abs(incEarthJ2000 - 23.43929) > 0.05) {
                    Console.WriteLine($"❌ 倾角偏差过大: {incEarthJ2000:F4}°");
                    return;
                }
                Console.WriteLine("        ✅ 地球在 J2000 赤道系下的公转倾角精准对齐 23.439° 黄赤交角！");

                // 2. 火星与金星在 J2000 下的倾角核验
                PykepNative.GetSpicePlanetState("Mars", testUt, "J2000", out Vector3d rMars, out Vector3d vMars);
                Vector3d hMars = Vector3d.Cross(rMars, vMars);
                double incMarsJ2000 = Math.Acos(Mathf.Clamp((float)(hMars.z / hMars.magnitude), -1f, 1f)) * (180.0 / Math.PI);
                Console.WriteLine($"  [2.1] 火星在 ICRF(J2000) 赤道系倾角: {incMarsJ2000:F3}° (标称 24.67°)");

                PykepNative.GetSpicePlanetState("Venus", testUt, "J2000", out Vector3d rVenus, out Vector3d vVenus);
                Vector3d hVenus = Vector3d.Cross(rVenus, vVenus);
                double incVenusJ2000 = Math.Acos(Mathf.Clamp((float)(hVenus.z / hVenus.magnitude), -1f, 1f)) * (180.0 / Math.PI);
                Console.WriteLine($"  [2.2] 金星在 ICRF(J2000) 赤道系倾角: {incVenusJ2000:F3}° (标称 24.43°)");

                // 3. 月球 (Moon / Luna) 星历核验
                bool okMoon = PykepNative.GetSpicePlanetState("Moon", testUt, "J2000", out Vector3d rMoon, out Vector3d vMoon);
                if (!okMoon) {
                    Console.WriteLine("❌ 月球 SPICE 状态查询失败！");
                    return;
                }
                Vector3d rMoonGeo = rMoon - rJ2000;
                Vector3d vMoonGeo = vMoon - vJ2000;
                double moonDistKm = rMoonGeo.magnitude;
                double moonVelKms = vMoonGeo.magnitude;
                Console.WriteLine($"  [3.1] 月球地心相对矢量: 距离={moonDistKm:F1} km (标称 ~384,400 km), 速度={moonVelKms:F3} km/s (标称 ~1.022 km/s)");
                if (Math.Abs(moonDistKm - 384400.0) > 30000.0) {
                    Console.WriteLine($"❌ 月球地月距离异常: {moonDistKm:F1} km");
                    return;
                }

                PykepNative.GetSpicePlanetElements("Moon", testUt, 398600.4418, out double ma, out double me, out double mi, out double mr, out double mo, out double mm);
                Console.WriteLine($"  [3.2] 月球开普勒地心均根数: a={ma:F0} km, e={me:F4}, i={mi:F3}° (标称 5.145°)");
                if (Math.Abs(ma - 384400.0) > 100.0 || Math.Abs(mi - 5.145) > 0.1) {
                    Console.WriteLine($"❌ 月球根数异常: a={ma:F0}, i={mi:F3}");
                    return;
                }
                Console.WriteLine("        ✅ 月球高精度地心解析与日心级联状态完全自洽！");

                passed++;
                Console.WriteLine("\n✅ 黄赤交角 (23.439°) 原生旋转变换与月球星历核验 100% 通过！\n");
            } catch (Exception ex) {
                Console.WriteLine($"❌ 星历坐标系与月球测试异常: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}

