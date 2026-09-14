using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// 卡西尼 VVEJGA 任务专用深度评估测试套件。
    ///
    /// 【2026-09-14 审计修正】
    ///   1. 原 <c>Run</c> 为 <c>EvalCassini(); ctx.EndCase(true); return true;</c> —— 评测函数返回值被
    ///      丢弃、判定硬编码 true，**该用例在结构上不可能失败**。现改为 <see cref="EvaluationOutcome"/>
    ///      结构化回传 + 显式阈值裁决 (收敛 &amp;&amp; 全航段闭环 &amp;&amp; 未超工质预算)；
    ///   2. 搜索窗原为 <c>launchUT ± 10 天</c> (以精确历史发射日为圆心，最优解恰在窗口中心) →
    ///      改为 <see cref="EvalWindows.OpportunityYearWindow"/> 的"发射机会年份"年粒度窗；
    ///   3. 历史真值染色体 (<c>nominalGenome</c>) 的输出全部加 <c>[回放]</c> 标注，与寻优路径分离。
    /// </summary>
    public class CassiniEvaluationTest : ITestModule
    {
        public string ModuleId => "eval_cassini";
        public string Name => "卡西尼 VVEJGA 任务专用深度评估测试套件";
        public string Description => "诊断 Cassini VVEJGA 在不同共振航时与历元下的完整物理动力学与注种质量";

        /// <summary>Cassini-Huygens 任务级公开先验：发射机会年份 1997。仅用于构造年粒度搜索窗与事后比对。</summary>
        private const string HistoricLaunchStr = "15 Oct 1997 00:00:00.000";

        /// <summary>真实 Cassini 任务工质预算 (m/s)。来自任务设计的公开工程约束，作为判定阈值而非搜索边界。</summary>
        private const double MissionDvBudgetMs = 6200.0;

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("Cassini_VvejgaEvaluation");
            var outcome = EvalCassini();
            ctx.RecordMetric("模式", outcome.ModeTag);
            ctx.RecordMetric("收敛", outcome.Converged);
            ctx.RecordMetric("全航段闭环", outcome.AllLegsFeasible);
            ctx.RecordMetric("总ΔV(m/s)", double.IsNaN(outcome.TotalDvMs) ? "N/A" : $"{outcome.TotalDvMs:F1}");
            ctx.RecordMetric("工质预算(m/s)", outcome.BudgetMs > 0 ? $"{outcome.BudgetMs:F0}" : "未设");
            ctx.RecordMetric("预算合规", outcome.WithinBudget);
            if (!outcome.Passed) ctx.Fail(outcome.Summary);
            ctx.EndCase(outcome.Passed);
            return outcome.Passed;
        }

        public static EvaluationOutcome EvalCassini()
        {
            double launchUT = GMATClient.ParseUtcGregorian(HistoricLaunchStr);
            double leg0Tof = 193.0 * 86400.0;
            double leg1Tof = 424.0 * 86400.0;

            // 发射机会年份年粒度窗口 (不泄露精确历史发射日)
            EvalWindows.OpportunityYearWindow(launchUT, out double winStartUT, out double winEndUT);
            if (winEndUT <= winStartUT) { winStartUT = launchUT; winEndUT = launchUT; }
            Console.WriteLine($"   [搜索窗] 发射机会年份年粒度: {GMATClient.FormatUtcGregorian(winStartUT)} ~ {GMATClient.FormatUtcGregorian(winEndUT)} (非历史发射日 ±N 天邻域)");

            var pVenus = SolarSystemCatalog.GetPlanet("Venus");
            var pEarth = SolarSystemCatalog.GetPlanet("Earth");
            var pJupiter = SolarSystemCatalog.GetPlanet("Jupiter");
            double centralMu = SolarSystemCatalog.SunMu;

            double tVenus1 = launchUT + leg0Tof;
            double tVenus2 = tVenus1 + leg1Tof;

            pVenus.GetRelativeState("Sun", tVenus1, out Vector3d rV1, out Vector3d vV1);
            pEarth.GetRelativeState("Sun", launchUT, out Vector3d rE1, out _);
            PykepNative.SolveLambert(rE1, rV1, leg0Tof, centralMu, false, out _, out Vector3d vArrV1);
            Vector3d prevArrVinf = vArrV1 - vV1;

            double smaV = PykepBridge.GetRelativeSMA(pVenus, "Sun", 0.0);
            double smaE = PykepBridge.GetRelativeSMA(pEarth, "Sun", 0.0);
            double aH = (smaV + smaE) * 0.5;
            double tHohmann = Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
            Console.WriteLine($"tHohmann Venus->Earth = {tHohmann/86400.0:F1}d");

            var fullChain = new List<string> { "Earth", "Venus", "Venus", "Earth", "Jupiter", "Saturn" };
            var legBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds("Earth", "Saturn", new List<string> { "Venus", "Venus", "Earth", "Jupiter" }, "Sun");
            for (int i = 0; i < legBounds.Count; i++)
            {
                Console.WriteLine($"Leg #{i+1} ({fullChain[i]} -> {fullChain[i+1]}): Bounds = [{legBounds[i].MinDays:F1}, {legBounds[i].MaxDays:F1}] days");
            }

            // Test what FindAndRankResonantCombos returns for Leg 2
            double[] lowB = new double[1 + 5 + 5];
            double[] upB = new double[1 + 5 + 5];
            for (int i = 0; i < legBounds.Count; i++)
            {
                lowB[1 + i] = legBounds[i].MinDays * 86400.0;
                upB[1 + i] = legBounds[i].MaxDays * 86400.0;
            }
            pVenus.GetRelativeState("Sun", tVenus1, out _, out Vector3d vV1_test);
            pEarth.GetRelativeState("Sun", launchUT, out Vector3d rE1_test, out _);
            pVenus.GetRelativeState("Sun", tVenus1, out Vector3d rV1_test, out _);
            PykepNative.SolveLambert(rE1_test, rV1_test, leg0Tof, centralMu, false, out _, out Vector3d vArrV1_test);
            Vector3d vinfAtV1 = vArrV1_test - vV1_test;

            Console.WriteLine($"vinfAtV1 mag = {vinfAtV1.magnitude:F2} km/s");
            var ranked = MGADifferentialEvolution.FindAndRankResonantCombos(
                "Venus", "Earth", 1, tVenus1, vinfAtV1, lowB, upB, 1, "Sun", centralMu, 0.0, false, "Jupiter");
            Console.WriteLine($"\n[FindAndRankResonantCombos top 5 for Venus->Venus->Earth (downstream=Jupiter)]:");
            foreach (var rc in ranked.Take(5)) {
                Console.WriteLine($"   ratio={rc.Item1[0]:F3} ({rc.Item1[0]*224.7:F1}d), exitToF={rc.Item2/86400.0:F1}d");
            }

            pVenus.GetRelativeState("Sun", tVenus2, out Vector3d rV2, out Vector3d vV2);
            Console.WriteLine($"\n[Detailed Scan: Leg 2 Venus -> Venus 1-DSM Physics]");
            Console.WriteLine($"   tV1={GMATClient.FormatUtcGregorian(tVenus1)}, tV2={GMATClient.FormatUtcGregorian(tVenus2)}");
            Console.WriteLine($"   rV1 mag={rV1.magnitude:F1}, rV2 mag={rV2.magnitude:F1}, angle between rV1 and rV2 = {Vector3d.Angle(rV1, rV2):F2}°");

            // Construct flyby frame {u_in, e1, e2} exactly as in pykep_mga_hybrid
            Vector3d u_in = prevArrVinf.normalized;
            double v_in_mag = prevArrVinf.magnitude;
            double safe_rp = MGADefaults.GetBodySafeRadiusKm("Venus");
            double e_min = 1.0 + (safe_rp * v_in_mag * v_in_mag) / Math.Max(1.0, pVenus.Mu);
            double delta_max = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, e_min));
            Console.WriteLine($"   v_in_mag = {v_in_mag:F3} km/s, delta_max = {delta_max * 180.0 / Math.PI:F2}°");

            Vector3d u_p1 = vV1.normalized;
            Vector3d u_r1 = rV1.normalized;
            Vector3d u_h = Vector3d.Cross(rV1, vV1).normalized;
            Vector3d u_theta = Vector3d.Cross(u_h, u_r1).normalized;

            Vector3d w1 = u_theta - Vector3d.Dot(u_theta, u_in) * u_in;
            if (w1.magnitude < 1e-5) {
                w1 = u_p1 - Vector3d.Dot(u_p1, u_in) * u_in;
                if (w1.magnitude < 1e-5) w1 = u_h;
            }
            Vector3d e1 = w1.normalized;
            Vector3d e2 = Vector3d.Cross(u_in, e1).normalized;

            // Scan eta and flyby direction
            double bestDsmFound = 1e9;
            double bestEtaFound = 0.5;
            double bestDeltaFound = 0;
            double bestBetaFound = 0;
            Vector3d bestVinfArr2 = Vector3d.zero;

            for (double curEta = 0.38; curEta <= 0.60; curEta += 0.02) {
                double dt1 = leg1Tof * curEta;
                double dt2 = leg1Tof * (1.0 - curEta);

                for (int dStep = 5; dStep <= 10; dStep++) {
                    double curDel = delta_max * (dStep / 10.0);
                    for (int bStep = 0; bStep < 72; bStep++) {
                        double curBeta = -Math.PI + bStep * (2.0 * Math.PI / 72.0);
                        Vector3d u_out = Math.Cos(curDel) * u_in + Math.Sin(curDel) * (Math.Cos(curBeta) * e1 + Math.Sin(curBeta) * e2);
                        Vector3d v_dep = vV1 + v_in_mag * u_out;

                        // Propagate dt1
                        Vector3d r_d = rV1;
                        Vector3d v_d = v_dep;
                        try {
                            if (PykepNative.PropagateLagrangian(ref r_d, ref v_d, dt1, centralMu)) {
                                Vector3d r_dsm = r_d;
                                Vector3d v_dsm_m = v_d;
                                // Solve Lambert dt2
                                if (PykepNative.SolveLambert(r_dsm, rV2, dt2, centralMu, false, out Vector3d v_dsm_p, out Vector3d v2_arr)) {
                                    double dsmMag = (v_dsm_p - v_dsm_m).magnitude;
                                    Vector3d vinfArr2 = v2_arr - vV2;
                                    if (dsmMag < bestDsmFound) {
                                        bestDsmFound = dsmMag;
                                        bestEtaFound = curEta;
                                        bestDeltaFound = curDel;
                                        bestBetaFound = curBeta;
                                        bestVinfArr2 = vinfArr2;
                                    }
                                }
                            }
                        } catch { }
                    }
                }
            }
            Console.WriteLine($"   [Fine Scan Result across (eta, delta, beta)] Best DSM = {bestDsmFound * 1000.0:F1} m/s at eta={bestEtaFound:F3}, delta={bestDeltaFound*180/Math.PI:F1}°, beta={bestBetaFound*180/Math.PI:F1}°, resulting vInfArr2 = {bestVinfArr2.magnitude:F2} km/s");



            var cfg = new MGAConfig
            {
                CentralBody = "Sun",
                DepartureBody = "Earth",
                DestinationBody = "Saturn",
                FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter" },
                DeparturePeAKm = 200.0,
                DestinationPeAKm = 60330.0,
                DestinationApAKm = 60330.0 * 49.0,
                NoInsertionBurn = false,
                DepartureMode = MGADepartureMode.PreLaunchFreeOrbit,
                EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM,
                // 【2026-09-14 修正】原为 launchUT ± 10 天 —— 以精确历史发射日为圆心开窗，最优解恰在中心。
                EarliestDepartureUT = winStartUT,
                LatestDepartureUT = winEndUT,
                PopulationSize = 100,
                MaxGenerations = 100,
                ConstrainByVesselDeltaV = true,
                VesselMaxDeltaVMs = MissionDvBudgetMs,
                LaunchDeltaVCeilingMs = 4500.0,
                MinToFDays = legBounds.Select(b => b.MinDays).ToList(),
                MaxToFDays = legBounds.Select(b => b.MaxDays).ToList()
            };

            // -----------------------------------------------------------------
            // [回放] 历史真值单点回放 (Answer Replay)
            //
            // 该染色体逐位复刻 Cassini 任务的历史发射时刻与各航段实际飞行天数
            // (193/424/55/499/1278 天)。它**不是**求解器的输出, 只用于验证
            // "星历 + 开普勒链路在已知真值点上能否复现历史量级的 ΔV"。
            // 其结果不得作为求解器寻优能力的证据。
            // -----------------------------------------------------------------
            double[] nominalGenome = new double[]
            {
                launchUT,
                193.0 * 86400.0,
                424.0 * 86400.0,
                55.0 * 86400.0,
                499.0 * 86400.0,
                1278.0 * 86400.0,
                0.5, 0.5, 0.5, 0.5, 0.5
            };

            var sol = PykepBridge.BuildFullSolution(nominalGenome, fullChain, cfg, false);
            if (sol != null)
            {
                Console.WriteLine($"[回放·历史真值单点] Launch={GMATClient.FormatUtcGregorian(sol.DepartureUT)}, TotalDv={sol.TotalDeltaVKms*1000:F1} m/s, DepDv={sol.LaunchDeltaVKms*1000:F1} m/s, ArrDv={sol.InsertionDeltaVKms*1000:F1} m/s, DSM={sol.TotalDsmDeltaVKms*1000:F1} m/s");
                for (int i = 0; i < sol.Legs.Count; i++)
                {
                    var l = sol.Legs[i];
                    double dsmVal = l.DSM != null ? l.DSM.DeltaVMs : 0.0;
                    Console.WriteLine($"   Leg #{i+1}: {l.FromBody} -> {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsmVal:F1} m/s, FB_dv={l.FlybyDeltaVKms*1000:F1} m/s, Defl={l.FlybyTurningAngleDeg:F1}°, vInfDep={l.VInfDepartureKms.magnitude:F2}, vInfArr={l.VInfArrivalKms.magnitude:F2}");
                }
            }
            else
            {
                Console.WriteLine("[回放·历史真值单点] BuildFullSolution returned null!");
            }

            Console.WriteLine("\n[回放·Hooke-Jeeves 局部精修] 起点为上方历史真值染色体:");
            double[] lowBounds = new double[11];
            double[] upBounds = new double[11];
            // 【2026-09-14 修正】原为 launchUT ± 10 天，为历史发射日的直接邻域。
            lowBounds[0] = winStartUT;
            upBounds[0] = winEndUT;
            for (int i = 0; i < 5; i++) {
                lowBounds[1 + i] = legBounds[i].MinDays * 86400.0;
                upBounds[1 + i] = legBounds[i].MaxDays * 86400.0;
                lowBounds[6 + i] = 0.1;
                upBounds[6 + i] = 0.9;
            }
            double[] polGene = (double[])nominalGenome.Clone();
            for (int iter = 0; iter < 10; iter++) {
                for (int d = 0; d < 11; d++) {
                    double[] deltas = (d > 5) 
                        ? new double[] { -0.05, 0.05, -0.01, 0.01 } 
                        : new double[] { -0.5 * 86400.0, 0.5 * 86400.0, -0.1 * 86400.0, 0.1 * 86400.0, -1.0 * 86400.0, 1.0 * 86400.0 };
                    double bestVal = polGene[d];
                    double bestDv = PykepBridge.BuildFullSolution(polGene, fullChain, cfg, false)?.TotalDeltaVKms ?? 1e9;
                    foreach (double dt in deltas) {
                        double testVal = Math.Max(lowBounds[d], Math.Min(upBounds[d], bestVal + dt));
                        polGene[d] = testVal;
                        var testS = PykepBridge.BuildFullSolution(polGene, fullChain, cfg, false);
                        if (testS != null && testS.TotalDeltaVKms < bestDv) {
                            bestDv = testS.TotalDeltaVKms;
                            bestVal = testVal;
                        }
                    }
                    polGene[d] = bestVal;
                }
            }
            var polSol = PykepBridge.BuildFullSolution(polGene, fullChain, cfg, false);
            if (polSol != null) {
                Console.WriteLine($"   Polished: Launch={GMATClient.FormatUtcGregorian(polSol.DepartureUT)}, TotalDv={polSol.TotalDeltaVKms*1000:F1} m/s, DepDv={polSol.LaunchDeltaVKms*1000:F1} m/s, ArrDv={polSol.InsertionDeltaVKms*1000:F1} m/s, DSM={polSol.TotalDsmDeltaVKms*1000:F1} m/s");
                for (int i = 0; i < polSol.Legs.Count; i++) {
                    var l = polSol.Legs[i];
                    double dsmVal = l.DSM != null ? l.DSM.DeltaVMs : 0.0;
                    Console.WriteLine($"      Leg #{i+1}: {l.FromBody} -> {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsmVal:F1} m/s, FB_dv={l.FlybyDeltaVKms*1000:F1} m/s, Defl={l.FlybyTurningAngleDeg:F1}°, vInfDep={l.VInfDepartureKms.magnitude:F2}, vInfArr={l.VInfArrivalKms.magnitude:F2}");
                }
            }

            // -----------------------------------------------------------------
            // [求解] 给定 NASA VVEJGA 拓扑下的差分进化寻优
            //
            // 说明: 本用例的定位是**特定拓扑的深度诊断**, 拓扑 (Venus-Venus-Earth-Jupiter)
            // 由任务定义给出, 不属"拓扑盲搜"。判定只依据: 是否收敛、物理是否闭环、是否超工质预算。
            // -----------------------------------------------------------------
            var deSols = MGADifferentialEvolution.RunWindow(cfg, winStartUT, winEndUT, null, default);
            if (deSols != null && deSols.Count > 0)
            {
                var best = deSols[0];
                Console.WriteLine($"   DE Best: Launch={GMATClient.FormatUtcGregorian(best.DepartureUT)}, TotalDv={best.TotalDeltaVKms*1000:F1} m/s, DepDv={best.LaunchDeltaVKms*1000:F1} m/s, ArrDv={best.InsertionDeltaVKms*1000:F1} m/s, DSM={best.TotalDsmDeltaVKms*1000:F1} m/s, Feasible={best.AllLegsFeasible}");
                for (int i = 0; i < best.Legs.Count; i++)
                {
                    var l = best.Legs[i];
                    double dsmVal = l.DSM != null ? l.DSM.DeltaVMs : 0.0;
                    Console.WriteLine($"      Leg #{i+1}: {l.FromBody} -> {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsmVal:F1} m/s, FB_dv={l.FlybyDeltaVKms*1000:F1} m/s, Defl={l.FlybyTurningAngleDeg:F1}°, vInfDep={l.VInfDepartureKms.magnitude:F2}, vInfArr={l.VInfArrivalKms.magnitude:F2}");
                }

                double dvMs = best.TotalDeltaVKms * 1000.0;
                bool within = dvMs <= MissionDvBudgetMs;
                return new EvaluationOutcome {
                    Converged = true,
                    AllLegsFeasible = best.AllLegsFeasible,
                    TotalDvMs = dvMs,
                    BudgetMs = MissionDvBudgetMs,
                    IsHistoricReplay = false,
                    Summary = within
                        ? $"收敛且合规 (总ΔV={dvMs:F0} m/s ≤ 预算 {MissionDvBudgetMs:F0} m/s, 闭环={best.AllLegsFeasible})"
                        : $"超出工质预算: 总ΔV={dvMs:F0} m/s > {MissionDvBudgetMs:F0} m/s (或未闭环: {best.AllLegsFeasible})"
                };
            }

            Console.WriteLine("   DE returned no solutions!");
            return EvaluationOutcome.NotConverged(
                "给定 VVEJGA 拓扑下 DE 未收敛出任何解 (OriginalDeadline: 年粒度发射机会窗内)", MissionDvBudgetMs);
        }
    }
}
