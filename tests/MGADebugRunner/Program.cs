using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;
using MGADebugRunner.TestSuites;

namespace MGADebugRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🚀 [KSP_GMAT] 离线免启动游戏 GUI 动力学核心与全场景用例完全对齐测试套件");
            Console.WriteLine("==========================================================================================");

            // 1. 底盘与 SPICE 状态
            Console.WriteLine($"[1] 原生动力学引擎: {(PykepNative.IsAvailable ? "✅ 正常挂载" : "❌ 未挂载")} ({PykepNative.Version})");
            Console.WriteLine($"[2] SPICE 内核状态: {(PykepNative.HasSpiceLoaded ? "✅ 已自动载入 (" + PykepNative.LoadedSpicePath + ")" : "❌ 未载入")}");
            Console.WriteLine($"[3] 当前星历数据源: {SolarSystemCatalog.CurrentScheme} ({SolarSystemCatalog.GetSchemeDescription(SolarSystemCatalog.CurrentScheme)})");
            Console.WriteLine($"[4] CPU 多核加速池: {Environment.ProcessorCount} 逻辑核心 | 实时状态: {CPUMonitor.GetSnapshot()}");
            Console.WriteLine();

            var modules = new Dictionary<string, ITestModule>(StringComparer.OrdinalIgnoreCase)
            {
                { "fast", new Modules.FastSuiteModule() },
                { "core", new Modules.CoreSystemTestModule() },
                { "orbit", new Modules.OrbitDynamicsTestModule() },
                { "locked", new Modules.Mode2LockedOrbitTestModule() },
                { "direct", new Modules.DirectDiversityTestModule() },
                { "adaptive", new Modules.AdaptiveSolverTestModule() },
                { "autonomous", new Modules.AutonomousMGATestModule() },
                { "advanced", new Modules.AdvancedAstrodynamicsTestModule() }
            };

            var individualTests = new Dictionary<string, ITestModule>(StringComparer.OrdinalIgnoreCase)
            {
                { "planetary", new PlanetaryCatalogDynamicLoadingTest() },
                { "ephem", new EphemerisSchemeComparisonTest() },
                { "hohmann", new DirectHohmannMissionsTest() },
                { "history_bench", new HistoricalBenchmarkMissionsTest() },
                { "gui_presets", new GuiPresetOptimizationsTest() },
                { "advanced_features", new AdvancedAstrodynamicsFeaturesTest() },
                { "fictional", new FictionalMissionsTest() },
                { "lunar_script", new ParseLunarScriptTest() },
                { "dynamic_body", new UniversalDynamicBodyRegistryTest() },
                { "moons", new MultiSystemMoonTransfersTest() },
                { "scenario9", new Scenario9ChallengeTest() },
                { "history_debug", new HistoricalMissionsDebuggerTest() },
                { "blind_search", new HistoricalBlindSearchSuiteTest() },
                { "topologies", new HistoricalTopologiesDiagnosisTest() },
                { "porkchop_bench", new PorkchopScalesBenchmarkTest() },
                { "gpu_verify", new GpuAccelerationValidationTest() },
                { "tof_bounds", new ToFBoundDiagnosticsTest() },
                { "surrogate", new SurrogateTrainingTest() },
                { "galileo", new GalileoEvaluationTest() },
                { "juno", new JunoEvaluationTest() },
                { "cassini", new CassiniEvaluationTest() },
                { "eval_nh", new NewHorizonsEvaluationTest() },
                { "eval_messenger", new MessengerEvaluationTest() }
            };

            bool isScenario9Only = false;
            bool isHistoryOnly = false;
            bool isBlindSearchOnly = false;
            bool isLabelOnly = false;
            string labelFilter = null;
            bool trainSurrogateOnly = false;
            string blindFilter = null;
            bool isTopoDiagOnly = false;
            string topoFilter = null;
            bool isPorkchopBenchOnly = false;
            string selectedModule = null;
            string selectedTest = null;
            bool runFast = false;
            bool runAll = false;
            bool listModules = false;

            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string raw = args[i].Trim();
                    string clean = raw.ToLowerInvariant().TrimStart('-', '/');
                    if (clean == "fast" || clean == "quick" || clean == "f" || clean == "q") { runFast = true; continue; }
                    if (clean == "all" || clean == "a") { runAll = true; continue; }
                    if (clean == "list" || clean == "modules" || clean == "tests" || clean == "l") { listModules = true; continue; }
                    if ((clean == "module" || clean == "m" || clean == "mod") && i + 1 < args.Length)
                    {
                        selectedModule = args[i + 1].Trim().ToLowerInvariant();
                        i++;
                        continue;
                    }
                    if ((clean == "test" || clean == "t") && i + 1 < args.Length)
                    {
                        selectedTest = args[i + 1].Trim().ToLowerInvariant();
                        i++;
                        continue;
                    }
                    if (clean == "porkchop" || clean == "bench-porkchop" || clean == "porkchop-bench" || clean == "gpu")
                    {
                        isPorkchopBenchOnly = true;
                        continue;
                    }
                    if (clean == "topo" || clean == "diag-topo" || clean == "topodiag")
                    {
                        isTopoDiagOnly = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 1].StartsWith("/"))
                        {
                            topoFilter = args[i + 1].Trim();
                            i++;
                        }
                        continue;
                    }
                    if (clean == "blind" || clean == "history-blind" || clean == "blind-history" || clean == "hb" || clean == "blindsearch")
                    {
                        isBlindSearchOnly = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 1].StartsWith("/"))
                        {
                            blindFilter = args[i + 1].Trim();
                            i++;
                        }
                        continue;
                    }
                    if (clean == "label" || clean == "truth-label" || clean == "label-chains")
                    {
                        isLabelOnly = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 1].StartsWith("/"))
                        {
                            labelFilter = args[i + 1].Trim();
                            i++;
                        }
                        continue;
                    }
                    if (clean == "train-surrogate" || clean == "train" || clean == "ml" || clean == "surrogate")
                    {
                        trainSurrogateOnly = true;
                        continue;
                    }
                    if (clean == "history" || clean == "historical" || clean == "hist" || clean == "h")
                    {
                        isHistoryOnly = true;
                        continue;
                    }
                    if (clean == "eval-galileo" || clean == "eg")
                    {
                        GalileoEvaluationTest.EvalGalileo();
                        return;
                    }
                    if (clean == "eval-juno" || clean == "ej")
                    {
                        JunoEvaluationTest.EvalJuno();
                        return;
                    }
                    if (clean == "eval-cassini" || clean == "ec")
                    {
                        CassiniEvaluationTest.EvalCassini();
                        return;
                    }
                    if (clean == "eval-nh" || clean == "enh" || clean == "eval_nh")
                    {
                        NewHorizonsEvaluationTest.EvalNewHorizons();
                        return;
                    }
                    if (clean == "eval-messenger" || clean == "em" || clean == "eval_messenger")
                    {
                        MessengerEvaluationTest.EvalMessenger();
                        return;
                    }
                    if (clean == "9" || clean == "scenario9" || clean == "scenario-9" || clean == "scenario_9" || clean == "s9" || clean == "emn" || clean == "challenge" || clean == "project9" || clean == "p9")
                    {
                        isScenario9Only = true;
                        continue;
                    }
                }
            }

            if (listModules)
            {
                Console.WriteLine("📋 [已注册集成测试模块列表]:");
                foreach (var kv in modules)
                {
                    Console.WriteLine($"  • --module {kv.Key,-12} | {kv.Value.Name}");
                    Console.WriteLine($"    说明: {kv.Value.Description}");
                }
                Console.WriteLine("\n📋 [TestSuites 独立单项测试用例列表]:");
                foreach (var kv in individualTests)
                {
                    Console.WriteLine($"  • --test {kv.Key,-16} | {kv.Value.Name}");
                    Console.WriteLine($"    说明: {kv.Value.Description}");
                }
                Console.WriteLine("\n用法示例:");
                Console.WriteLine("  dotnet run --project tests/MGADebugRunner/MGADebugRunner.csproj -c Release -- --fast");
                Console.WriteLine("  dotnet run --project tests/MGADebugRunner/MGADebugRunner.csproj -c Release -- --module locked");
                Console.WriteLine("  dotnet run --project tests/MGADebugRunner/MGADebugRunner.csproj -c Release -- --test hohmann");
                Console.WriteLine("  dotnet run --project tests/MGADebugRunner/MGADebugRunner.csproj -c Release -- --all");
                return;
            }

            if (isPorkchopBenchOnly)
            {
                PorkchopScalesBenchmarkTest.BenchmarkPorkchopScales();
                return;
            }

            if (isTopoDiagOnly)
            {
                HistoricalTopologiesDiagnosisTest.DiagnoseHistoricalTopologies(topoFilter);
                return;
            }

            if (trainSurrogateOnly)
            {
                SurrogateTrainingTest.RunSurrogateTraining();
                return;
            }

            if (isLabelOnly)
            {
                Modules.ChainTruthLabelingModule.Run(labelFilter);
                return;
            }

            if (isBlindSearchOnly)
            {
                HistoricalBlindSearchSuiteTest.RunBlindSearchSuite(blindFilter);
                return;
            }

            if (isHistoryOnly)
            {
                HistoricalMissionsDebuggerTest.RunHistoricalMissionsDebugger(out _);
                return;
            }

            if (isScenario9Only)
            {
                Scenario9ChallengeTest.RunScenario9(false);
                return;
            }

            string reportDir = "reports/test_reports";
            System.IO.Directory.CreateDirectory(reportDir);

            // 1. 快速回归模式 (--fast / --quick)
            if (runFast)
            {
                var fastMod = modules["fast"];
                bool ok;
                int passC, totC;
                using (var ctx = new TestContext(fastMod.ModuleId, fastMod.Name, fastMod.Description, reportDir))
                {
                    ok = fastMod.Run(ctx);
                    ctx.SaveReport(reportDir);
                    passC = ctx.Report.PassCount;
                    totC = ctx.Report.TotalCount;
                }
                Console.WriteLine($"\n[Fast Suite] 最终判定: {(ok ? "✅ 全部通过" : "❌ 存在失败")} ({passC}/{totC})");
                return;
            }

            // 2. 单用例模式 (--test <name>)
            if (!string.IsNullOrEmpty(selectedTest))
            {
                if (!individualTests.TryGetValue(selectedTest, out var testCase))
                {
                    Console.WriteLine($"❌ 未知测试用例: '{selectedTest}'. 可用测试: {string.Join(", ", individualTests.Keys)}");
                    return;
                }
                Console.WriteLine($"🎯 [单用例定向测试] 正在执行: {testCase.Name} ...");
                bool ok;
                int passC, totC;
                using (var ctx = new TestContext(testCase.ModuleId, testCase.Name, testCase.Description, reportDir))
                {
                    ok = testCase.Run(ctx);
                    ctx.SaveReport(reportDir);
                    passC = ctx.Report.PassCount;
                    totC = ctx.Report.TotalCount;
                }
                Console.WriteLine($"\n[{testCase.Name}] 最终判定: {(ok ? "✅ 全部通过" : "❌ 存在失败")} ({passC}/{totC})");
                return;
            }

            // 3. 单模块模式 (--module <name>)
            if (!string.IsNullOrEmpty(selectedModule))
            {
                if (!modules.TryGetValue(selectedModule, out var mod))
                {
                    Console.WriteLine($"❌ 未知模块: '{selectedModule}'. 可用模块: {string.Join(", ", modules.Keys)}");
                    return;
                }
                Console.WriteLine($"🎯 [单模块定向测试] 正在执行: {mod.Name} ...");
                bool ok;
                int passC, totC;
                using (var ctx = new TestContext(mod.ModuleId, mod.Name, mod.Description, reportDir))
                {
                    ok = mod.Run(ctx);
                    ctx.SaveReport(reportDir);
                    passC = ctx.Report.PassCount;
                    totC = ctx.Report.TotalCount;
                }
                Console.WriteLine($"\n[{mod.Name}] 最终判定: {(ok ? "✅ 全部通过" : "❌ 存在失败")} ({passC}/{totC})");
                return;
            }

            // 4. 全模块运行模式 (默认 或 --all)
            if (runAll || (string.IsNullOrEmpty(selectedModule) && string.IsNullOrEmpty(selectedTest)))
            {
                Console.WriteLine("🚀 [全量模块化测试套件启动] 逐模块执行并独立导出 Markdown 报告与专用 Log...\n");
                var summaryList = new List<TestModuleReport>();
                var allSw = System.Diagnostics.Stopwatch.StartNew();

                string[] execOrder = new[] { "fast", "core", "orbit", "locked", "direct", "adaptive", "autonomous", "advanced" };
                foreach (var modKey in execOrder)
                {
                    if (!modules.TryGetValue(modKey, out var mod)) continue;
                    Console.WriteLine($"\n>>> 执行模块 [{modKey}]: {mod.Name} <<<");
                    using (var ctx = new TestContext(mod.ModuleId, mod.Name, mod.Description, reportDir))
                    {
                        mod.Run(ctx);
                        ctx.SaveReport(reportDir);
                        summaryList.Add(ctx.Report);
                    }
                }
                allSw.Stop();

                Console.WriteLine("\n==========================================================================================");
                Console.WriteLine("🏆 全模块测试综合大榜与独立报告/日志导出清单");
                Console.WriteLine("==========================================================================================");
                Console.WriteLine(string.Format("{0,-14} | {1,-24} | {2,-10} | {3,-9} | {4,-24} | {5,-22}", "模块标识", "模块名称", "通过状态", "耗时(ms)", "报告路径", "独立日志路径"));
                Console.WriteLine(new string('-', 116));

                int totalPassed = 0;
                int totalCases = 0;
                foreach (var rep in summaryList)
                {
                    totalPassed += rep.PassCount;
                    totalCases += rep.TotalCount;
                    string status = (rep.FailCount == 0) ? "✅ PASS" : $"❌ {rep.PassCount}/{rep.TotalCount}";
                    string rPath = $"reports/test_reports/report_{rep.ModuleId}.md";
                    string lPath = $"reports/test_reports/log_{rep.ModuleId}.log";
                    Console.WriteLine(string.Format("{0,-14} | {1,-24} | {2,-10} | {3,-9:F0} | {4,-24} | {5,-22}", rep.ModuleId, rep.ModuleName.Length > 22 ? rep.ModuleName.Substring(0, 22) : rep.ModuleName, status, rep.TotalElapsedMs, rPath, lPath));
                }
                Console.WriteLine(new string('-', 116));
                Console.WriteLine($"★ 综合通过统计: {totalPassed} / {totalCases} 用例通过 (通过率: {(totalCases > 0 ? (double)totalPassed / totalCases * 100.0 : 0):F1}%), 总耗时: {allSw.Elapsed.TotalSeconds:F2} 秒");
                Console.WriteLine("==========================================================================================\n");
            }
        }
    }
}
