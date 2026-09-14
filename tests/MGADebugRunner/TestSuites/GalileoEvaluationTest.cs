using System;
using System.Collections.Generic;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// 伽利略 VEEGA 任务专用深度评估测试套件。
    ///
    /// 【2026-09-14 审计修正 —— 本套件为**历史真值回放诊断**, 不是能力测试】
    ///   原 <c>Run</c> 为 <c>bool ok = true; try { EvalGalileo(); } catch { ok = false; }</c> ——
    ///   只有抛异常才算失败, 评估数值从不参与判定 (等价恒真)。
    ///   现改为: <see cref="EvaluationOutcome"/> 结构化回传, 判定 = "星历链路在历史真值点上
    ///   能否给出物理闭环解" (Converged ∧ AllLegsFeasible), 可证伪。
    ///   因输入染色体即历史真值 (115/301/731/1093 天), 结果标记为 [回放],
    ///   **不构成求解器寻优能力的证据**。
    /// </summary>
    public class GalileoEvaluationTest : ITestModule
    {
        public string ModuleId => "eval_galileo";
        public string Name => "伽利略 VEEGA 任务专用深度评估测试套件";
        public string Description => "诊断 Galileo VEEGA 在历史真值航段与历元下的星历/开普勒链路闭环自洽性 (回放诊断, 非盲搜)";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("Galileo_VeegaEvaluation");
            EvaluationOutcome outcome;
            try {
                outcome = EvalGalileo();
            } catch (Exception ex) {
                ctx.Fail($"Galileo 评估异常: {ex.Message}");
                ctx.EndCase(false);
                return false;
            }
            ctx.RecordMetric("模式", outcome.ModeTag);
            ctx.RecordMetric("收敛(星历链路闭环)", outcome.Converged);
            ctx.RecordMetric("全航段闭环", outcome.AllLegsFeasible);
            ctx.RecordMetric("总ΔV(m/s)", double.IsNaN(outcome.TotalDvMs) ? "N/A" : $"{outcome.TotalDvMs:F1}");
            if (!outcome.Passed) ctx.Fail(outcome.Summary);
            ctx.EndCase(outcome.Passed);
            return outcome.Passed;
        }

        public static EvaluationOutcome EvalGalileo()
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🛰️ [EvalGalileo·回放] 诊断 Galileo VEEGA 在历史真值航段与历元下的星历/开普勒链路闭环");
            Console.WriteLine("==========================================================================================");

            double launchUT = GMATClient.ParseUtcGregorian("18 Oct 1989 00:00:00.000");
            var fullChain = new List<string> { "Earth", "Venus", "Earth", "Earth", "Jupiter" };
            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Jupiter",
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 1000.0,
                DestinationApAKm = 1000.0,
                ArrivalOrbitType = MGAArrivalOrbitType.HighlyElliptic,
                DepartureMode = MGADepartureMode.PreLaunchFreeOrbit,
                LegStrategies = new List<MGALegStrategy> {
                    MGALegStrategy.Ballistic,
                    MGALegStrategy.DeepSpaceManeuver1DSM,
                    MGALegStrategy.DeepSpaceManeuver1DSM,
                    MGALegStrategy.DeepSpaceManeuver1DSM
                }
            };

            // 测试 1: [回放] 历史名义航时 (115d, 301d, 731d, 1093d) —— Galileo 各航段实际飞行天数
            double[] tofsDays = new double[] { 115.0, 301.0, 731.0, 1093.0 };
            double[] genome = new double[1 + 4 + 4];
            genome[0] = launchUT;
            for (int i = 0; i < 4; i++) genome[1 + i] = tofsDays[i] * 86400.0;
            genome[5] = 0.5;
            genome[6] = 0.5;
            genome[7] = 0.5;
            genome[8] = 0.5;

            var sol = PykepBridge.BuildFullSolution(genome, fullChain, cfg, computeAdvanced: false);
            if (sol != null)
            {
                Console.WriteLine($"[回放·历史名义] 发射={GMATClient.FormatUtcGregorian(sol.DepartureUT)}, 始发={sol.LaunchDeltaVKms*1000.0:F1} m/s, 终点捕获={sol.InsertionDeltaVKms*1000.0:F1} m/s, 总ΔV={sol.TotalDeltaVKms*1000.0:F1} m/s, 全可行={sol.AllLegsFeasible}");
                for (int li = 0; li < sol.Legs.Count; li++)
                {
                    var l = sol.Legs[li];
                    string dsmStr = (l.DSM != null) ? $"DSM={l.DSM.DeltaVMs:F1} m/s (pro={l.DSM.ProgradeMs:F1}, nml={l.DSM.NormalMs:F1}, rad={l.DSM.RadialMs:F1})" : "无DSM";
                    Console.WriteLine($"   Leg #{li+1}: {l.FromBody} ➔ {l.ToBody}, ToF={l.ToFDays:F1}d, 偏折={l.FlybyTurningAngleDeg:F1}°, 飞掠ΔV={l.FlybyDeltaVKms*1000.0:F1} m/s, {dsmStr}, 可行={l.IsFeasible}");
                    Console.WriteLine($"          V_inf_dep = ({l.VInfDepartureKms.x:F3}, {l.VInfDepartureKms.y:F3}, {l.VInfDepartureKms.z:F3}) mag={l.VInfDepartureKms.magnitude:F3} km/s");
                    Console.WriteLine($"          V_inf_arr = ({l.VInfArrivalKms.x:F3}, {l.VInfArrivalKms.y:F3}, {l.VInfArrivalKms.z:F3}) mag={l.VInfArrivalKms.magnitude:F3} km/s");
                }
                double loss = MGAPhysicsLossEngine.ComputeLoss(sol, cfg);
                Console.WriteLine($"   => MGAPhysicsLossEngine ComputeLoss = {loss:F2}");

                // [回放] 判定: 星历链路能否在历史真值点给出物理闭环解 (可证伪)。
                // 因输入即答案, 此处**不设工质预算判据** (BudgetMs = 0 ⇒ 不做超限判定)。
                return new EvaluationOutcome {
                    Converged = true,
                    AllLegsFeasible = sol.AllLegsFeasible,
                    TotalDvMs = sol.TotalDeltaVKms * 1000.0,
                    BudgetMs = 0.0,
                    IsHistoricReplay = true,
                    Summary = sol.AllLegsFeasible
                        ? $"星历链路闭环自洽 (回放; 总ΔV={sol.TotalDeltaVKms * 1000.0:F0} m/s)。注意: 该结果不作为寻优能力证据。"
                        : $"星历链路在历史真值点上存在不可行航段 (回放失败, 需检查星历/开普勒链路)"
                };
            }

            Console.WriteLine("[回放·历史名义] BuildFullSolution 返回 null (星历链路不可用)");
            return EvaluationOutcome.NotConverged("星历链路不可用: BuildFullSolution 返回 null (历史真值回放)");
        }
    }
}
