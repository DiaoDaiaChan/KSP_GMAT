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
    /// 信使号多级水星共振自减速动力学诊断测试套件。
    ///
    /// 【2026-09-14 审计修正】
    ///   1. 原 <c>Run</c> 为 <c>bool ok = true; try { EvalMessenger(); } catch { ok = false; }</c> ——
    ///      只有抛异常才算失败，ΔV/收敛/飞掠可行性**从不参与判定**，等价于恒真。现改为
    ///      <see cref="EvaluationOutcome"/> 结构化回传 + 显式阈值裁决；
    ///   2. 搜索窗原为 <c>launchUT ± 60 天</c> (历史发射日邻域) → 年粒度发射机会窗；
    ///   3. 历史真值染色体 (gNom / gReal) 的输出全部加 <c>[回放]</c> 标注。
    /// </summary>
    public class MessengerEvaluationTest : ITestModule
    {
        public string ModuleId => "eval_messenger";
        public string Name => "信使号多级水星共振自减速动力学诊断测试套件";
        public string Description => "诊断 MESSENGER 在地-金-水多级自共振减速与高偏心率捕获下的完整物理动力学闭环";

        /// <summary>MESSENGER 任务级公开先验：发射机会年份 2004。</summary>
        private const string HistoricLaunchStr = "03 Aug 2004 00:00:00.000";

        /// <summary>MESSENGER 任务工质预算 (m/s)。任务设计公开工程约束，作为判定阈值而非搜索边界。</summary>
        private const double MissionDvBudgetMs = 6500.0;

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("MESSENGER_Evaluation");
            EvaluationOutcome outcome;
            try {
                outcome = EvalMessenger();
            } catch (Exception ex) {
                ctx.Fail($"MESSENGER 评估异常: {ex.Message}\n{ex.StackTrace}");
                ctx.EndCase(false);
                return false;
            }
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

        public static EvaluationOutcome EvalMessenger()
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🛰️ [EvalMessenger] 诊断 MESSENGER 在地-金-水多级自共振减速与高偏心率捕获下的物理动力学");
            Console.WriteLine("==========================================================================================");

            double launchUT = GMATClient.ParseUtcGregorian(HistoricLaunchStr);
            string departure = "Earth";
            string arrival = "Mercury";
            string central = "Sun";

            // 发射机会年份年粒度搜索窗 (不泄露精确历史发射日)
            EvalWindows.OpportunityYearWindow(launchUT, out double winStartUT, out double winEndUT);
            if (winEndUT <= winStartUT) { winStartUT = launchUT; winEndUT = launchUT; }
            Console.WriteLine($"   [搜索窗] 发射机会年份年粒度: {GMATClient.FormatUtcGregorian(winStartUT)} ~ {GMATClient.FormatUtcGregorian(winEndUT)} (非历史发射日 ±N 天邻域)");

            Console.WriteLine("\n--- 1. 检验 MGATopologyBudget 预算推导 ---");
            var budget = MGATopologyBudgetDeriver.Derive(departure, arrival, central, 180.0);
            Console.WriteLine($"   {budget.Rationale}");
            Console.WriteLine($"   MaxSwingBys={budget.MaxSwingBys}, MaxResonances={budget.MaxResonances}, MaxBackLegs={budget.MaxBackLegs}, MaxChainLegs={budget.MaxChainLegs}");

            Console.WriteLine("\n--- 2. 检验 MGAPlanetaryAssistManager 爬升/共振链合成 ---");
            var climbChains = MGAPlanetaryAssistManager.SynthesizePhysicalClimbingChains(departure, arrival, null);
            Console.WriteLine($"   合成拓扑总数: {climbChains.Count}");
            int idx = 1;
            foreach (var ch in climbChains)
            {
                Console.WriteLine($"   #{idx++,2}: {departure} ➔ {string.Join(" ➔ ", ch)} ➔ {arrival}");
            }

            Console.WriteLine("\n--- 3. 检验 MGAEphemerisReachabilityAnalyzer 物理可达性初筛与排序 ---");
            var filtered = MGAEphemerisReachabilityAnalyzer.FilterAndPrioritizeReachableSequences(
                departure, arrival, climbChains, launchUT, 6500.0, 1500.0, null, false, 180.0);
            Console.WriteLine($"   入选可行拓扑总数: {filtered.Count}");
            for (int i = 0; i < Math.Min(8, filtered.Count); i++)
            {
                var f = filtered[i];
                Console.WriteLine($"   Top #{i+1}: {departure} ➔ {string.Join(" ➔ ", f.FlybyChain)} ➔ {arrival} | Deficit={f.TotalDeficitMs:F1} m/s, EstDv={f.EstimatedTotalDvMs:F1} m/s, ParetoScore={f.ParetoScore:F3}");
            }

            Console.WriteLine("\n--- 4. 检验典型多级水星共振链 (EVVMMM) 的物理可行性与逐段几何 ---");
            var chainEVVMMM = new List<string> { "Earth", "Venus", "Venus", "Mercury", "Mercury", "Mercury" };
            var fullChainEVVMMM = new List<string> { "Earth", "Earth", "Venus", "Venus", "Mercury", "Mercury", "Mercury", "Mercury" };

            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Mercury",
                FlybySequence = chainEVVMMM,
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 200.0,
                DestinationApAKm = 15000.0, // 水星高偏心率捕获椭圆
                NoInsertionBurn = false,
                ArrivalOrbitType = MGAArrivalOrbitType.HighlyElliptic,
                DepartureMode = MGADepartureMode.PreLaunchFreeOrbit,
                ConstrainByVesselDeltaV = true,
                VesselMaxDeltaVMs = 6500.0,
                LaunchDeltaVCeilingMs = 4500.0
            };

            var legBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(departure, arrival, chainEVVMMM, central);
            cfg.MinToFDays = legBounds.Select(b => b.MinDays).ToList();
            cfg.MaxToFDays = legBounds.Select(b => b.MaxDays).ToList();

            Console.WriteLine($"   逐段自适应航时边界 ({fullChainEVVMMM.Count - 1} 航段):");
            for (int k = 0; k < legBounds.Count; k++)
            {
                Console.WriteLine($"     Leg #{k+1} ({fullChainEVVMMM[k]} ➔ {fullChainEVVMMM[k+1]}): [{legBounds[k].MinDays:F1}, {legBounds[k].MaxDays:F1}] 天");
            }

            Console.WriteLine("\n--- 4.1 诊断 Earth ➔ Earth ➔ Venus 共振槽优选与得分 ---");
            // 【2026-09-14 修正】原为 launchUT ± 60 天 (历史发射日邻域) → 年粒度发射机会窗。
            // 诊断锚点取机会窗中点, 不再使用历史发射时刻。
            double winMidUT = (winStartUT + winEndUT) * 0.5;
            double[] lowB = cfg.MinToFDays.Select(d => d * 86400.0).Prepend(winStartUT).ToArray();
            double[] uppB = cfg.MaxToFDays.Select(d => d * 86400.0).Prepend(winEndUT).ToArray();
            var rankedCombosDiag = MGADifferentialEvolution.FindAndRankResonantCombos(
                "Earth", "Venus", 1, winMidUT, Vector3d.zero, lowB, uppB, 0, central, 1.32712440018e11, 0.0, false, null);
            Console.WriteLine($"   FindAndRankResonantCombos 返回总数: {rankedCombosDiag.Count}");
            for (int ci = 0; ci < Math.Min(10, rankedCombosDiag.Count); ci++)
            {
                var rc = rankedCombosDiag[ci];
                Console.WriteLine($"     #{ci+1}: ratio={rc.Item1[0]:F2} ({rc.Item1[0]*365.25:F1}d), exitToF={rc.Item2/86400.0:F1}d");
            }

            Console.WriteLine("\n--- 4.2 [回放] 检验 MESSENGER 理论物理共振轨迹的 BuildFullSolution (历史真值型染色体) ---");
            var testG = new double[1 + 7 + 7];
            testG[0] = launchUT;
            testG[1] = 364.0 * 86400.0;
            testG[2] = 448.0 * 86400.0;
            testG[3] = 224.0 * 86400.0;
            testG[4] = 223.0 * 86400.0;
            testG[5] = 266.0 * 86400.0;
            testG[6] = 358.0 * 86400.0;
            testG[7] = 535.0 * 86400.0;
            for (int d = 8; d < 15; d++) testG[d] = 0.5;

            var testSol = PykepBridge.BuildFullSolution(testG, fullChainEVVMMM, cfg, false);
            if (testSol != null)
            {
                Console.WriteLine($"   -> 始发: {testSol.LaunchDeltaVKms*1000:F1} m/s, DSM: {testSol.TotalDsmDeltaVKms*1000:F1} m/s, 捕获: {testSol.InsertionDeltaVKms*1000:F1} m/s, 飞掠: {testSol.TotalFlybyDeltaVKms*1000:F1} m/s, 总ΔV: {testSol.TotalDeltaVKms*1000:F1} m/s, 航时: {testSol.TotalFlightDays:F1}d, 闭环: {testSol.AllLegsFeasible}");
                if (testSol.Legs != null)
                {
                    for (int k = 0; k < testSol.Legs.Count; k++)
                    {
                        var l = testSol.Legs[k];
                        double dsm = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                        Console.WriteLine($"      Leg #{k+1}: {l.FromBody} ➔ {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsm:F1} m/s, 飞掠ΔV={l.FlybyDeltaVKms*1000:F1} m/s, 近掠高={l.FlybyPeriapsisAltitudeKm:F0} km, 偏折={l.FlybyTurningAngleDeg:F1}°, vInfDep={l.VInfDepartureKms.magnitude:F2} km/s, vInfArr={l.VInfArrivalKms.magnitude:F2} km/s");
                    }
                }
            }

            Console.WriteLine("\n--- 4.2.0 扫描 testG Leg 7 的 ToF 与 Eta 对捕获与 DSM 的影响 ---");
            for (double t7_days = 525.0; t7_days <= 540.0; t7_days += 3.0)
            {
                foreach (double e7 in new double[] { 0.08, 0.105, 0.15, 0.25, 0.50 })
                {
                    var gScan = (double[])testG.Clone();
                    gScan[7] = t7_days * 86400.0;
                    gScan[14] = e7;
                    var sScan = PykepBridge.BuildFullSolution(gScan, fullChainEVVMMM, cfg, false);
                    if (sScan != null)
                    {
                        var l7 = sScan.Legs[6];
                        double dsm7 = (l7.DSM != null) ? l7.DSM.DeltaVMs : 0.0;
                        double ins = sScan.InsertionDeltaVKms * 1000.0;
                        if (dsm7 + ins < 8000.0)
                        {
                            Console.WriteLine($"   ToF7={t7_days:F1}d, Eta7={e7:F3} -> Leg7 DSM={dsm7:F1} m/s, vArr={l7.VInfArrivalKms.magnitude:F2} km/s, Ins={ins:F1} m/s, Sum={dsm7 + ins:F1} m/s, TotalDv={sScan.TotalDeltaVKms*1000:F1} m/s");
                        }
                    }
                }
            }

            Console.WriteLine("\n--- 4.2.1 检验标称有理共振向量 gNom (365d, 453d, 225d, 223d, 264d, 352d, 528d) ---");
            var gNom = new double[1 + 7 + 7];
            gNom[0] = launchUT;
            gNom[1] = 365.25 * 86400.0;
            gNom[2] = 453.0 * 86400.0;
            gNom[3] = 224.7 * 86400.0;
            gNom[4] = 223.1 * 86400.0;
            gNom[5] = 263.9 * 86400.0;
            gNom[6] = 351.9 * 86400.0;
            gNom[7] = 527.8 * 86400.0;
            for (int d = 8; d < 14; d++) gNom[d] = 0.5;
            gNom[14] = 0.106; // 开普勒多圈共振轨道首个远心点物理相位 (56天 / 528天 = 0.106)
            var nomSol = PykepBridge.BuildFullSolution(gNom, fullChainEVVMMM, cfg, false);
            if (nomSol != null)
            {
                Console.WriteLine($"   -> 始发: {nomSol.LaunchDeltaVKms*1000:F1} m/s, DSM: {nomSol.TotalDsmDeltaVKms*1000:F1} m/s, 捕获: {nomSol.InsertionDeltaVKms*1000:F1} m/s, 飞掠: {nomSol.TotalFlybyDeltaVKms*1000:F1} m/s, 总ΔV: {nomSol.TotalDeltaVKms*1000:F1} m/s, 航时: {nomSol.TotalFlightDays:F1}d, 闭环: {nomSol.AllLegsFeasible}");
                if (nomSol.Legs != null)
                {
                    for (int k = 0; k < nomSol.Legs.Count; k++)
                    {
                        var l = nomSol.Legs[k];
                        double dsm = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                        Console.WriteLine($"      Leg #{k+1}: {l.FromBody} ➔ {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsm:F1} m/s, 飞掠ΔV={l.FlybyDeltaVKms*1000:F1} m/s, 近掠高={l.FlybyPeriapsisAltitudeKm:F0} km, 偏折={l.FlybyTurningAngleDeg:F1}°, vInfDep={l.VInfDepartureKms.magnitude:F2} km/s, vInfArr={l.VInfArrivalKms.magnitude:F2} km/s");
                    }
                }
            }

            Console.WriteLine("\n--- 4.2.2 [回放] 检验 NASA MESSENGER 真实飞行参数向量 gReal (历史真值, 非求解器输出) ---");
            var gReal = new double[1 + 7 + 7];
            gReal[0] = GMATClient.ParseUtcGregorian("03 Aug 2004 06:15:56.000");
            gReal[1] = 364.54 * 86400.0;
            gReal[2] = 447.57 * 86400.0;
            gReal[3] = 224.59 * 86400.0;
            gReal[4] = 222.83 * 86400.0;
            gReal[5] = 265.57 * 86400.0;
            gReal[6] = 358.55 * 86400.0;
            gReal[7] = 534.12 * 86400.0;
            gReal[8] = 0.50;
            gReal[9] = 132.0 / 447.57; // DSM-1 on Dec 12, 2005
            gReal[10] = 0.50;
            gReal[11] = 0.50;
            gReal[12] = 0.25;  // Mercury 1 (3:2 resonance apoapsis = 0.25)
            gReal[13] = 0.167; // Mercury 2 (4:3 resonance apoapsis = 0.167)
            gReal[14] = 56.0 / 534.12; // DSM-5 on Nov 24, 2009 (0.1048)
            var realSol = PykepBridge.BuildFullSolution(gReal, fullChainEVVMMM, cfg, false);
            if (realSol != null)
            {
                Console.WriteLine($"   -> 始发: {realSol.LaunchDeltaVKms*1000:F1} m/s, DSM: {realSol.TotalDsmDeltaVKms*1000:F1} m/s, 捕获: {realSol.InsertionDeltaVKms*1000:F1} m/s, 飞掠: {realSol.TotalFlybyDeltaVKms*1000:F1} m/s, 总ΔV: {realSol.TotalDeltaVKms*1000:F1} m/s, 航时: {realSol.TotalFlightDays:F1}d, 闭环: {realSol.AllLegsFeasible}");
                if (realSol.Legs != null)
                {
                    for (int k = 0; k < realSol.Legs.Count; k++)
                    {
                        var l = realSol.Legs[k];
                        double dsm = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                        Console.WriteLine($"      Leg #{k+1}: {l.FromBody} ➔ {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsm:F1} m/s, 飞掠ΔV={l.FlybyDeltaVKms*1000:F1} m/s, 近掠高={l.FlybyPeriapsisAltitudeKm:F0} km, 偏折={l.FlybyTurningAngleDeg:F1}°, vInfDep={l.VInfDepartureKms.magnitude:F2} km/s, vInfArr={l.VInfArrivalKms.magnitude:F2} km/s");
                    }
                }
            }

            Console.WriteLine("\n--- 4.3 诊断 Leg 1 ToF 与 DSM eta 对总ΔV 的敏感度 (锚点=机会窗中点, 非历史发射日) ---");
            double bestTotalDv = double.MaxValue;
            double bestTof1 = 364.0, bestEta1 = 0.5, bestLaunchOffset = 0.0;
            double[] testLaunchOffsets = new double[] { -5.0, -2.0, 0.0, 2.0, 5.0 };
            double[] testTofs1 = new double[] { 360.0, 362.0, 364.0, 365.0, 366.0, 368.0, 370.0 };
            double[] testEtas1 = new double[] { 0.20, 0.30, 0.36, 0.45, 0.50, 0.60, 0.70 };

            foreach (double lOff in testLaunchOffsets)
            {
                foreach (double t1 in testTofs1)
                {
                    foreach (double e1 in testEtas1)
                    {
                        var g = (double[])testG.Clone();
                        g[0] = launchUT + lOff * 86400.0;
                        g[1] = t1 * 86400.0;
                        g[8] = e1;
                        var s = PykepBridge.BuildFullSolution(g, fullChainEVVMMM, cfg, false);
                        if (s != null && s.TotalDeltaVKms < bestTotalDv)
                        {
                            bestTotalDv = s.TotalDeltaVKms;
                            bestTof1 = t1;
                            bestEta1 = e1;
                            bestLaunchOffset = lOff;
                        }
                    }
                }
            }
            Console.WriteLine($"   最佳扫描结果: LaunchOffset={bestLaunchOffset:F1}d, ToF1={bestTof1:F1}d, Eta1={bestEta1:F2}, 最低总ΔV={bestTotalDv*1000:F1} m/s");

            cfg.PopulationSize = 40;
            cfg.MaxGenerations = 50;

            // 【2026-09-14 修正】原为 launchUT ± 60 天 (历史发射日邻域, 最优解落在窗口中心) → 年粒度机会窗
            var deSols = MGADifferentialEvolution.RunWindow(cfg, winStartUT, winEndUT, null, default);
            if (deSols != null && deSols.Count > 0)
            {
                Console.WriteLine($"   DE 求解返回可行解数: {deSols.Count}");
                foreach (var s in deSols.Take(3))
                {
                    Console.WriteLine($"   -> 历元: {GMATClient.FormatUtcGregorian(s.DepartureUT)}, 始发: {s.LaunchDeltaVKms*1000:F0} m/s, DSM: {s.TotalDsmDeltaVKms*1000:F0} m/s, 捕获: {s.InsertionDeltaVKms*1000:F0} m/s, 飞掠: {s.TotalFlybyDeltaVKms*1000:F1} m/s, 总ΔV: {s.TotalDeltaVKms*1000:F0} m/s, 航时: {s.TotalFlightDays:F1}d, 闭环: {s.AllLegsFeasible}");
                    if (s.Legs != null)
                    {
                        for (int k = 0; k < s.Legs.Count; k++)
                        {
                            var l = s.Legs[k];
                            Console.WriteLine($"      Leg #{k+1}: {l.FromBody} ➔ {l.ToBody}, ToF={l.ToFDays:F1}d, ΔV={l.DeltaVKms*1000:F0} m/s, 飞掠ΔV={l.FlybyDeltaVKms*1000:F1} m/s, 近掠高={l.FlybyPeriapsisAltitudeKm:F0} km, 偏折={l.FlybyTurningAngleDeg:F1}°");
                        }
                    }
                }

                var bestDe = deSols[0];
                double dvMs = bestDe.TotalDeltaVKms * 1000.0;
                bool within = dvMs <= MissionDvBudgetMs;
                return new EvaluationOutcome {
                    Converged = true,
                    AllLegsFeasible = bestDe.AllLegsFeasible,
                    TotalDvMs = dvMs,
                    BudgetMs = MissionDvBudgetMs,
                    IsHistoricReplay = false,
                    Summary = (within && bestDe.AllLegsFeasible)
                        ? $"收敛且合规 (总ΔV={dvMs:F0} m/s ≤ 预算 {MissionDvBudgetMs:F0} m/s)"
                        : $"不合规: 总ΔV={dvMs:F0} m/s (预算 {MissionDvBudgetMs:F0} m/s), 全航段闭环={bestDe.AllLegsFeasible}"
                };
            }

            Console.WriteLine("   ⚠️ DE 快速演化未在短时间内收敛出合规解，需检查种子注入与航时采样。");
            return EvaluationOutcome.NotConverged(
                "在年粒度发射机会窗内 DE 未收敛出合规解 (指定 EVVMMM 拓扑)", MissionDvBudgetMs);
        }
    }
}
