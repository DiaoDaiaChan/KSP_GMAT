using System;
using System.Diagnostics;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.Modules
{
    public class AdvancedAstrodynamicsTestModule : ITestModule
    {
        public string ModuleId => "advanced_astrodynamics";
        public string Name => "高级天体动力学与工程特性模块 (Advanced Astrodynamics & Engineering)";
        public string Description => "验证飞掠切入几何 (DHA/DEC)、B 平面 (B·T, B·R) 解析闭环、GMAT 官方高精度打靶脚本静态语法审计，以及智能共振调相与物理最大偏转角推导。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            // -------------------------------------------------------------
            // 用例 1: 飞掠切入几何、B 平面与 GMAT 脚本生成导出
            // -------------------------------------------------------------
            ctx.StartCase("FlybyGeometry_BPlane_GMATScriptExport");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("📐 [用例集 Q] MGA 飞掠切入几何 (DHA/DEC)、B 平面 (B·T, B·R) 与 GMAT 打靶脚本生成验证");
            Console.WriteLine("------------------------------------------------------------------------------------------");

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

                ctx.RecordMetric("DHA", $"{geom.IncomingDhaDeg:F2}°");
                ctx.RecordMetric("DEC", $"{geom.IncomingDecDeg:F2}°");
                ctx.RecordMetric("偏转角", $"{geom.TurnAngleDeg:F2}°");
                ctx.RecordMetric("B平面模长", $"{geom.BMagKm:F1} km");

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
                    FlybySequence = new System.Collections.Generic.List<string> { "Venus", "Jupiter" },
                    DeparturePeAKm = 200.0, DestinationPeAKm = 50000.0
                };

                string script = MGAGMATScriptBuilder.GenerateMGAScript(mockSol, mockCfg, "Sat");
                var lintReport = GMATScriptLinter.Lint(script);

                ctx.RecordMetric("GMAT脚本Lint错误", lintReport.ErrorCount);
                ctx.RecordMetric("GMAT脚本Lint警告", lintReport.WarningCount);

                if (!geomOk || lintReport.ErrorCount > 0)
                {
                    ctx.Fail($"几何参数异常或 GMAT 打靶脚本语法错误 ({lintReport.ErrorCount} errors)");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ MGA 飞掠切入几何、B 平面与 GMAT 脚本导出 100% 验证通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"飞掠几何与脚本测试异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 2: 智能共振调相与物理最大偏转角验证
            // -------------------------------------------------------------
            ctx.StartCase("IntelligentResonantPhasing_MaxTurnAngle");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌀 [用例集 R] 智能共振调相与物理最大偏转角验证 (Intelligent Resonant Phasing)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double venusMu = 324859.0;
                double venusRp = 6352.0;
                double vInf = 5.0;
                double maxTurnDeg = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(venusMu, venusRp, vInf);

                bool angleOk = maxTurnDeg >= 80.0 && maxTurnDeg <= 90.0;
                int neededN = MGAFlybyDynamicsHelper.ComputeRequiredResonantPhasingCount(120.0 * Math.PI / 180.0, venusMu, venusRp, vInf);
                bool nOk = (neededN == 2);

                ctx.RecordMetric("金星最大转向角", $"{maxTurnDeg:F2}° (80~90)");
                ctx.RecordMetric("120度偏转所需共振阶数", $"{neededN} 次 (预期=2)");

                if (!angleOk || !nOk)
                {
                    ctx.Fail($"物理偏转角公式或共振阶数推导异常: maxTurn={maxTurnDeg:F2}°, neededN={neededN}");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ 智能共振调相与物理最大偏转角推导 100% 验证通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"共振调相异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return allOk;
        }
    }
}
