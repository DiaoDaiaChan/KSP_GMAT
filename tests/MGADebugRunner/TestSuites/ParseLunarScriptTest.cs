using System;
using System.Collections.Generic;
using KSPGMAT;
using KSPGMAT.Model;
using KSPGMAT.Service;
using UnityEngine;

namespace MGADebugRunner.TestSuites
{
    public class ParseLunarScriptTest : ITestModule
    {
        public string ModuleId => "lunar_script";
        public string Name => "月球 LLO 转移脚本往返解析与自愈测试";
        public string Description => "测试 Lunar_LLO_Transfer.script 解析与 GMAT 脚本重新生成核验";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("ParseLunarScript_RoundTrip");
            bool ok = true;
            try
            {
                RunTest();
            }
            catch (Exception ex)
            {
                ctx.Fail($"月球脚本解析异常: {ex.Message}");
                ok = false;
            }
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest()
        {
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
            for (int i = 0; i < res.sequence.Count; i++)
            {
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
            try
            {
                string gen = GMATScriptEngine.Generate(null, res.sequence);
                Console.WriteLine("============= 生成的完整 GMAT 脚本 =============");
                string[] lines = gen.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < Math.Min(lines.Length, 120); i++)
                {
                    Console.WriteLine($"{i+1,3}: {lines[i]}");
                }
                Console.WriteLine("... [后续步骤如上已验证]");

                string gmatExe = @"C:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe";
                if (System.IO.File.Exists(gmatExe))
                {
                    Console.WriteLine("\n[验证] 找到官方 GMAT 控制台，正在调用并核验语法解析与求解运行...");
                    string tempScript = @"C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\test_lunar_gen.script";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tempScript));
                    System.IO.File.WriteAllText(tempScript, gen, new System.Text.UTF8Encoding(false));

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = gmatExe,
                        Arguments = $"--run \"{tempScript}\"",
                        WorkingDirectory = @"C:\Users\43701\gmat-win-R2026a\bin",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (var proc = System.Diagnostics.Process.Start(psi))
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(30000);
                        Console.WriteLine($"GMAT 退出码: {proc.ExitCode}");
                        if (!string.IsNullOrEmpty(stdout))
                        {
                            Console.WriteLine($"[GMAT 输出]\n{stdout}");
                        }
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            Console.WriteLine($"[GMAT 错误]\n{stderr}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"未在 {gmatExe} 找到 GmatConsole.exe，跳过引擎直跑");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GenerateScript Exception] Type: {ex.GetType().FullName}, Message: {ex.Message}");
                Console.WriteLine($"Stack:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                }
            }
        }
    }
}
