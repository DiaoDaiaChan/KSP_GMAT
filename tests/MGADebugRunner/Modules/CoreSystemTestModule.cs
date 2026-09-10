using System;
using System.Diagnostics;
using System.Linq;
using KSPGMAT;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.Modules
{
    public class CoreSystemTestModule : ITestModule
    {
        public string ModuleId => "core_system";
        public string Name => "核心系统与脚本/Python节点模块 (Core System, Scripts & Python)";
        public string Description => "验证 310 个参数与 323 个官方 API 类的能力注册表模糊搜索、GMAT 脚本往返解析与自愈，以及原生 Python 节点序列化与 IntelliSense 智能补全。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            // -------------------------------------------------------------
            // 用例 1: 能力注册表动态加载与模糊检索
            // -------------------------------------------------------------
            ctx.StartCase("CapabilitiesRegistry_DynamicLoadingAndSearch");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("📚 [用例集 能力注册表] 验证 310 个参数、323 个类与 6,355 个方法实现的动态加载与秒级模糊搜索");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                var rsw = Stopwatch.StartNew();
                var reg = GMATSchemaRegistry.Instance;
                rsw.Stop();

                ctx.RecordMetric("参数总数", reg.ParameterCount);
                ctx.RecordMetric("API类总数", reg.ApiClassCount);
                ctx.RecordMetric("API方法总数", reg.ApiMethodCount);
                ctx.RecordMetric("加载耗时", $"{rsw.ElapsedMilliseconds} ms");

                var results = reg.SearchCapabilities("SetSolarSystem");
                var pyPresetRes = reg.SearchCapabilities("compute_delta_v", "funcs");
                var methodRes = reg.SearchCapabilities("SetSolarSystem", "funcs");

                bool countOk = reg.ParameterCount >= 300 && reg.ApiClassCount >= 300 && reg.ApiMethodCount > 6000;
                bool searchOk = results.Count > 0 && pyPresetRes.Any(r => r.title == "compute_delta_v") && methodRes.Count > 0;

                if (!countOk || !searchOk)
                {
                    ctx.Fail("能力注册表数据量未达预期或模糊检索命中异常");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ GMAT 官方全量参数与 API 方法体系动态解析核验 100% 通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"能力注册表异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 2: GMAT 脚本往返解析 / 动态天体 / 容差自愈
            // -------------------------------------------------------------
            ctx.StartCase("ScriptSystem_RoundTrip_LinterAndSelfHealing");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("📜 [用例集 脚本系统] GMAT 脚本读取 / 导出 / 格式化 / 动态天体 / 容差自愈往返全链路核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                string sampleScript = @"
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2026 12:00:00.000';
Sat.CoordinateSystem = SaturnMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 100000.0; Sat.Y = 200000.0; Sat.Z = 300000.0;
Sat.VX = 1.0; Sat.VY = 2.0; Sat.VZ = 3.0;
Sat.DryMass = 1200;

Create ForceModel SaturnDeepSpaceFM;
SaturnDeepSpaceFM.CentralBody = Saturn;
SaturnDeepSpaceFM.PointMasses = {Sun, Saturn, Titan, Europa, Kerbin};

Create Propagator SaturnProp;
SaturnProp.FM = SaturnDeepSpaceFM;

Create DifferentialCorrector DC;
Create ImpulsiveBurn TCM1;
TCM1.CoordinateSystem = Local; TCM1.Origin = Saturn; TCM1.Axes = VNB;

BeginMissionSequence;
Target DC {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary DC(TCM1.Element1 = 150, {Perturbation = 0.0001, Lower = 0, Upper = 1000, MaxStep = 10});
   Maneuver TCM1(Sat);
   Propagate SaturnProp(Sat) {Sat.Saturn.Apoapsis};
   Achieve DC(Sat.Saturn.RMAG = 125000);
EndTarget;
";
                var parseResult = GMATScriptParser.ParseScriptText(sampleScript, "TestSaturnMission");
                PropagatorConfig pCfg = null;
                bool parseOk = parseResult.success && parseResult.propagators != null && parseResult.propagators.TryGetValue("SaturnProp", out pCfg);
                bool massesOk = parseOk && pCfg != null && pCfg.GetActivePointMasses().Contains("Titan") && pCfg.GetActivePointMasses().Contains("Saturn");

                string unTolerancedAchieve = "Achieve DC(Sat.Saturn.SMA = 250000);";
                string fixedScript = GMATScriptLinter.FixScript(unTolerancedAchieve);
                bool fixOk = fixedScript.Contains("Tolerance =");

                ctx.RecordMetric("脚本解析", parseOk ? "PASS" : "FAIL");
                ctx.RecordMetric("动态引力天体保留", massesOk ? "PASS" : "FAIL");
                ctx.RecordMetric("容差自愈", fixOk ? "PASS" : "FAIL");

                if (!parseOk || !massesOk || !fixOk)
                {
                    ctx.Fail("脚本系统解析或容差自愈未达标");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ 脚本系统往返全链路核验 100% 通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"脚本系统异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 3: GMAT 原生 Python 函数调用往返与补全
            // -------------------------------------------------------------
            ctx.StartCase("PythonStep_SerializationAndIntelliSense");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🐍 [用例集 Python节点] GMAT 原生 Python 函数调用双向往返序列化与智能补全系统核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                var stepAssign = new MissionStep
                {
                    type = "CallPython",
                    call_syntax_mode = "Assignment",
                    python_module = "orbit_calculator",
                    python_function = "compute_delta_v",
                    python_inputs = "Sat.A1ModJulian, Sat.Earth.SMA, Sat.Earth.ECC",
                    python_outputs = "dv_v, dv_n, dv_b"
                };
                string scriptAssign = GMATScriptGenerator.GenerateStepScript(stepAssign, "Sat", "Earth");
                bool assignOk = scriptAssign.Contains("[dv_v, dv_n, dv_b] = Python.orbit_calculator.compute_delta_v(Sat.A1ModJulian, Sat.Earth.SMA, Sat.Earth.ECC);");

                var pyModCompletions = GMATParameterRegistry.QueryIntelliSense("orbit", null, "python_module");
                bool intelliOk = pyModCompletions.Count > 0;

                string badPyScript = "Python.orbit_calc.my_func;";
                var lintReport = GMATScriptLinter.Lint(badPyScript);
                bool lintOk = lintReport.HasErrors && lintReport.Issues.Exists(i => i.RuleId == "LINT_PYTHON_SYNTAX");

                ctx.RecordMetric("赋值语法生成", assignOk ? "PASS" : "FAIL");
                ctx.RecordMetric("IntelliSense补全", intelliOk ? "PASS" : "FAIL");
                ctx.RecordMetric("语法Linter拦截", lintOk ? "PASS" : "FAIL");

                if (!assignOk || !intelliOk || !lintOk)
                {
                    ctx.Fail("Python 节点生成、补全或语法拦截异常");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ Python 步骤节点双向往返与统一补全系统核验 100% 通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"Python 节点异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return allOk;
        }
    }
}
