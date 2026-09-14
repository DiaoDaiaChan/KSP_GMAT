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
    /// 新视野号 E-J-Pluto 专用力学诊断测试套件。
    ///
    /// 【2026-09-14 审计修正】
    ///   1. 原 <c>Run</c> 为 <c>bool ok = true; try { EvalNewHorizons(); } catch { ok = false; }</c> ——
    ///      只有抛异常才算失败，ΔV/收敛/飞掠可行性从不参与判定，等价于恒真。现改为
    ///      <see cref="EvaluationOutcome"/> 结构化回传 + 显式阈值裁决；
    ///   2. 搜索窗原为 <c>launchUT ± 60 天</c> (历史发射日邻域) → 年粒度发射机会窗；
    ///   3. 历史真值染色体 (405d+3060d) 的输出加 <c>[回放]</c> 标注。
    /// </summary>
    public class NewHorizonsEvaluationTest : ITestModule
    {
        public string ModuleId => "eval_nh";
        public string Name => "新视野号 E-J-Pluto 专用力学诊断测试套件";
        public string Description => "诊断 New Horizons 在木星双曲助推与外太阳系逃逸走廊下的完整物理动力学";

        /// <summary>New Horizons 任务级公开先验：发射机会年份 2006。</summary>
        private const string HistoricLaunchStr = "19 Jan 2006 19:00:00.000";

        /// <summary>New Horizons 任务工质预算 (m/s)。任务设计公开工程约束，作为判定阈值而非搜索边界。</summary>
        private const double MissionDvBudgetMs = 11000.0;

        /// <summary>
        /// New Horizons 的实际出射能力上限 (m/s)。
        /// 物理来源: NH 使用 Atlas V 551 + STAR 48B, 实际日心双曲超速约 12.4 km/s, C3 ≈ 157 km²/s²,
        /// 远高于通用行星际任务门禁 (3800 m/s)。此处 10000 m/s 是该任务真实的运载能力，属**任务级公开工程约束**，
        /// 不是为了让某个历史任务通过而写死的特判。
        /// </summary>
        private const double NhLaunchCeilingMs = 10000.0;

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("NewHorizons_Evaluation");
            EvaluationOutcome outcome;
            try {
                outcome = EvalNewHorizons();
            } catch (Exception ex) {
                ctx.Fail($"New Horizons 评估异常: {ex.Message}");
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

        public static EvaluationOutcome EvalNewHorizons()
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🛰️ [EvalNewHorizons] 诊断 New Horizons 在木星双曲助推与外太阳系逃逸走廊下的完整物理动力学");
            Console.WriteLine("==========================================================================================");

            double launchUT = GMATClient.ParseUtcGregorian(HistoricLaunchStr);
            var fullChain = new List<string> { "Earth", "Jupiter", "Pluto" };
            SolarPlanet pEarth = SolarSystemCatalog.GetPlanet("Earth");
            SolarPlanet pJup = SolarSystemCatalog.GetPlanet("Jupiter");
            SolarPlanet pPluto = SolarSystemCatalog.GetPlanet("Pluto");
            double muSun = SolarSystemCatalog.SunMu;

            // 发射机会年份年粒度搜索窗 (不泄露精确历史发射日)
            EvalWindows.OpportunityYearWindow(launchUT, out double winStartUT, out double winEndUT);
            if (winEndUT <= winStartUT) { winStartUT = launchUT; winEndUT = launchUT; }
            Console.WriteLine($"   [搜索窗] 发射机会年份年粒度: {GMATClient.FormatUtcGregorian(winStartUT)} ~ {GMATClient.FormatUtcGregorian(winEndUT)} (非历史发射日 ±N 天邻域)");
            double aHPluto = (pJup.SMA + pPluto.SMA) * 0.5;
            double tHPlutoDays = Math.PI * Math.Sqrt(Math.Pow(aHPluto, 3.0) / muSun) / 86400.0;
            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Pluto",
                FlybySequence = new List<string> { "Jupiter" },
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 1000.0,
                DestinationApAKm = 1000.0,
                NoInsertionBurn = true,
                ArrivalOrbitType = MGAArrivalOrbitType.Flyby,
                DepartureMode = MGADepartureMode.PreLaunchFreeOrbit,
                LegStrategies = new List<MGALegStrategy> {
                    MGALegStrategy.Ballistic,
                    MGALegStrategy.DeepSpaceManeuver1DSM
                },
                DsmEtas = new List<double> { 0.5 }
            };

            Console.WriteLine("\n--- 1. 扫描名义历史航时点物理闭环 ---");
            double[] tofs1 = new double[] { 390.0, 400.0, 405.0, 410.0, 420.0 };
            double[] tofs2 = new double[] { 2800.0, 2900.0, 3000.0, 3060.0, 3100.0, 3200.0 };

            double bestDv = double.MaxValue;
            MGASolution bestSol = null;

            foreach (var t1 in tofs1)
            {
                foreach (var t2 in tofs2)
                {
                    double[] genome = new double[1 + 2 + 1]; // launchUT, tof1, tof2, eta1
                    genome[0] = launchUT;
                    genome[1] = t1 * 86400.0;
                    genome[2] = t2 * 86400.0;
                    genome[3] = 0.5;

                    var sol = PykepBridge.BuildFullSolution(genome, fullChain, cfg);
                    if (sol != null && sol.AllLegsFeasible)
                    {
                        double totDv = sol.TotalDeltaVKms * 1000.0;
                        if (totDv < bestDv)
                        {
                            bestDv = totDv;
                            bestSol = sol;
                        }
                        if (totDv < 10000.0)
                        {
                            Console.WriteLine($"  [可行] ToF1={t1,5:F0}d, ToF2={t2,5:F0}d, 总ToF={t1+t2,5:F0}d | 始发={sol.LaunchDeltaVKms*1000.0,6:F0} m/s, DSM={sol.TotalDsmDeltaVKms*1000.0,6:F0} m/s, 飞掠={sol.TotalFlybyDeltaVKms*1000.0,4:F1} m/s | 总ΔV={totDv,6:F0} m/s");
                        }
                    }
                }
            }

            if (bestSol != null)
            {
                Console.WriteLine($"\n★ 最佳物理闭环解: 总ΔV={bestDv:F1} m/s, 始发={bestSol.LaunchDeltaVKms*1000.0:F1} m/s, DSM={bestSol.TotalDsmDeltaVKms*1000.0:F1} m/s, 飞掠={bestSol.TotalFlybyDeltaVKms*1000.0:F1} m/s, 航时={bestSol.TotalFlightDays:F1}d");
                MGAFlybyDynamicsHelper.EnrichSolutionFlybyDynamics(bestSol);
                foreach (var leg in bestSol.Legs)
                {
                    Console.WriteLine($"   Leg: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, ΔV={leg.DeltaVKms*1000.0:F1} m/s, 飞掠={leg.FlybyDeltaVKms*1000.0:F1} m/s, 近掠高度={leg.FlybyPeriapsisAltitudeKm:F1} km, 偏折={leg.FlybyTurningAngleDeg:F1}°");
                }

                double loss = MGAPhysicsLossEngine.ComputeLoss(bestSol, cfg);
                double nomDays = MGAPhysicsLossEngine.ResolveNominalTofDays(cfg, bestSol);
                Console.WriteLine($"   MGAPhysicsLossEngine: Loss={loss:F4}, NominalToFDays={nomDays:F1}d");

                Console.WriteLine("\n--- 1.1 检验不同 HohmannScale 覆盖与评分 ---");
                Console.WriteLine($"   Jupiter -> Pluto 霍曼时间: {tHPlutoDays:F1} 天");
                Console.WriteLine("\n--- 1.2 检验 Leg 0 = 405d 时，Leg 1 各 ToF 的引力助推物理得分 (vDiff + vTurn) ---");

                double tDep = launchUT;
                double tof0 = 405.0 * 86400.0;
                pEarth.GetRelativeState("Sun", tDep, out Vector3d r0, out Vector3d v0);
                pJup.GetRelativeState("Sun", tDep + tof0, out Vector3d r1, out Vector3d v1Planet);

                PykepNative.SolveLambert(r0, r1, tof0, muSun, false, out _, out Vector3d vArrJup);
                Vector3d vInfArrJup = vArrJup - v1Planet;
                Console.WriteLine($"   Jupiter 到达 v_inf 大小: {vInfArrJup.magnitude:F2} km/s");

                double[] candidateTofs2 = new double[] { 2680.0, 2870.0, 3060.0, 3200.0, 3830.0, 8000.0, 15700.0, 16800.0 };
                foreach (var t2Days in candidateTofs2)
                {
                    double testTof2 = t2Days * 86400.0;
                    double arrPlutoUT = tDep + tof0 + testTof2;
                    pPluto.GetRelativeState("Sun", arrPlutoUT, out Vector3d r2, out _);

                    if (PykepNative.SolveLambert(r1, r2, testTof2, muSun, false, out Vector3d vDepJup, out _))
                    {
                        Vector3d vInfDepJup = vDepJup - v1Planet;
                        double vDiff = Math.Abs(vInfDepJup.magnitude - vInfArrJup.magnitude);
                        double dot = Vector3d.Dot(vInfDepJup.normalized, vInfArrJup.normalized);
                        dot = Math.Max(-1.0, Math.Min(1.0, dot));
                        double reqTurnRad = Math.Acos(dot);
                        double safeR = MGADefaults.GetBodySafeRadiusKm("Jupiter");
                        double e = 1.0 + (safeR * vInfArrJup.sqrMagnitude) / Math.Max(1.0, pJup.Mu);
                        double maxTurnRad = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, e));
                        double turnExcess = Math.Max(0.0, reqTurnRad - maxTurnRad);
                        double vTurn = 2.0 * Math.Max(vInfDepJup.magnitude, vInfArrJup.magnitude) * Math.Sin(turnExcess * 0.5);
                        double seedScore = vDiff + vTurn;

                        Console.WriteLine($"   ToF2={t2Days,7:F0}d | vInfDep={vInfDepJup.magnitude,5:F2} km/s, vDiff={vDiff,5:F2} km/s, 转角={reqTurnRad*180/Math.PI,4:F1}°/限={maxTurnRad*180/Math.PI,4:F1}°, vTurn={vTurn,5:F2} km/s | 得分={seedScore,5:F2}");
                    }
                }
            }
            else
            {
                Console.WriteLine("❌ 未在扫描网格中找到可行解！");
            }

            Console.WriteLine("\n--- 2. 检查物理自适应航时推导与搜索边界 ---");
            var legBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds("Earth", "Pluto", new List<string> { "Jupiter" }, "Sun");
            for (int i = 0; i < legBounds.Count; i++)
            {
                Console.WriteLine($"   Leg #{i+1}: [{legBounds[i].MinDays:F1}, {legBounds[i].MaxDays:F1}] 天");
            }
            var (dynMin, dynMax) = MGAMCTSTopologySelector.CalculateAdaptiveToFBounds("Earth", "Pluto", new List<string> { "Jupiter" }, "Sun");
            Console.WriteLine($"   总航时自适应边界: [{dynMin:F1}, {dynMax:F1}] 天");

            Console.WriteLine("\n--- 3. 运行差分进化求解器测试 (DE RunWindow) ---");
            cfg.MinToFDays = legBounds.Select(b => b.MinDays).ToList();
            cfg.MaxToFDays = legBounds.Select(b => b.MaxDays).ToList();
            cfg.PopulationSize = 60;
            cfg.MaxGenerations = 80;
            // NH 运载能力上限 (Atlas V 551 + STAR 48B, 实际日心双曲超速 ~12.4 km/s), 任务级公开工程约束
            cfg.LaunchDeltaVCeilingMs = NhLaunchCeilingMs;
            cfg.VesselMaxDeltaVMs = MissionDvBudgetMs;
            cfg.ConstrainByVesselDeltaV = true;

            // 专门核验 405d + 3060d 在 cfg 下的精确 Fitness
            // [回放] 405/3060 天为 New Horizons 历史各航段实际飞行天数 (E→J ≈ 405d, J→Pluto ≈ 3060d)。
            double[] nhExactGenome = new double[] { launchUT, 405.0 * 86400.0, 3060.0 * 86400.0, 0.5 };
            var solExact = PykepBridge.BuildFullSolution(nhExactGenome, fullChain, cfg);
            double exactFit = MGAPhysicsLossEngine.ComputeLoss(solExact, cfg);
            Console.WriteLine($"   [回放·历史真值解 Fitness 校验] 始发={solExact.LaunchDeltaVKms*1000:F0} m/s, DSM={solExact.TotalDsmDeltaVKms*1000:F0} m/s, 总ΔV={solExact.TotalDeltaVKms*1000:F0} m/s, 航时={solExact.TotalFlightDays:F1}d | Fitness={exactFit:F4}");

            double[] slowGenome = new double[] { launchUT, 487.6 * 86400.0, 8809.3 * 86400.0, 0.5 };
            var solSlow = PykepBridge.BuildFullSolution(slowGenome, fullChain, cfg);
            double slowFit = MGAPhysicsLossEngine.ComputeLoss(solSlow, cfg);
            Console.WriteLine($"   [对照·慢速漂移解 Fitness 校验] 始发={solSlow.LaunchDeltaVKms*1000:F0} m/s, DSM={solSlow.TotalDsmDeltaVKms*1000:F0} m/s, 总ΔV={solSlow.TotalDeltaVKms*1000:F0} m/s, 航时={solSlow.TotalFlightDays:F1}d | Fitness={slowFit:F4}");

            Console.WriteLine("\n--- 1.3 模拟 DE 前向注种在 launchUT 下为各 leg0Frac 选出的种子 ---");
            double[] leg0Fracs = new double[] { 0.0, 0.02, 0.05, 0.08, 0.12, 0.16 };
            double minTof0 = legBounds[0].MinDays * 86400.0;
            double maxTof0 = legBounds[0].MaxDays * 86400.0;
            double minTof1 = legBounds[1].MinDays * 86400.0;
            double maxTof1 = legBounds[1].MaxDays * 86400.0;

            foreach (var frac in leg0Fracs)
            {
                double candTof0 = minTof0 + frac * (maxTof0 - minTof0);
                double testUT = launchUT;
                pEarth.GetRelativeState("Sun", testUT, out Vector3d rD, out Vector3d vDPlanet);
                pJup.GetRelativeState("Sun", testUT + candTof0, out Vector3d rA, out Vector3d vAPlanet);
                if (PykepNative.SolveLambert(rD, rA, candTof0, muSun, false, out Vector3d vDepL, out Vector3d vArrL))
                {
                    Vector3d vInfArr = vArrL - vAPlanet;
                    double bestScore = double.MaxValue;
                    double bestTof1Days = 0;
                    double[] scales = new double[] { 0.10, 0.12, 0.14, 0.15, 0.16, 0.18, 0.20, 0.25, 0.30, 0.85 };
                    foreach (var sc in scales)
                    {
                        double candTof1 = tHPlutoDays * sc * 86400.0;
                        if (candTof1 < minTof1 || candTof1 > maxTof1) continue;
                        pPluto.GetRelativeState("Sun", testUT + candTof0 + candTof1, out Vector3d rPluto, out _);
                        if (PykepNative.SolveLambert(rA, rPluto, candTof1, muSun, false, out Vector3d vDepJ, out _))
                        {
                            Vector3d vInfDep = vDepJ - vAPlanet;
                            double vDiff = Math.Abs(vInfDep.magnitude - vInfArr.magnitude);
                            double dot = Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(vInfDep.normalized, vInfArr.normalized)));
                            double reqTurn = Math.Acos(dot);
                            double safeR = MGADefaults.GetBodySafeRadiusKm("Jupiter");
                            double e = 1.0 + (safeR * vInfArr.sqrMagnitude) / Math.Max(1.0, pJup.Mu);
                            double maxTurn = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, e));
                            double vTurn = 2.0 * Math.Max(vInfDep.magnitude, vInfArr.magnitude) * Math.Sin(Math.Max(0.0, reqTurn - maxTurn) * 0.5);
                            double scVal = vDiff + vTurn;
                            if (scVal < bestScore)
                            {
                                bestScore = scVal;
                                bestTof1Days = candTof1 / 86400.0;
                            }
                        }
                    }

                    double[] g = new double[] { testUT, candTof0, bestTof1Days * 86400.0, 0.5 };
                    var sSeed = PykepBridge.BuildFullSolution(g, fullChain, cfg);
                    if (sSeed != null)
                    {
                        double fit = MGAPhysicsLossEngine.ComputeLoss(sSeed, cfg);
                        Console.WriteLine($"   frac={frac:F2} -> ToF0={candTof0/86400:F1}d, ToF1={bestTof1Days:F1}d | 始发={sSeed.LaunchDeltaVKms*1000:F0} m/s, DSM={sSeed.TotalDsmDeltaVKms*1000:F0} m/s, 航时={sSeed.TotalFlightDays:F1}d | Score={bestScore:F2}, Fit={fit:F4}");
                    }
                }
            }

            // 【2026-09-14 修正】原为 launchUT ± 60 天 (历史发射日邻域, 最优解落在窗口中心) → 年粒度机会窗
            var deSols = MGADifferentialEvolution.RunWindow(cfg, winStartUT, winEndUT, null, default);
            if (deSols != null && deSols.Count > 0)
            {
                Console.WriteLine($"  DE 求解返回解数: {deSols.Count}");
                foreach (var s in deSols.Take(5))
                {
                    Console.WriteLine($"   -> 历元: {GMATClient.FormatUtcGregorian(s.DepartureUT)}, 始发: {s.LaunchDeltaVKms*1000:F0} m/s, DSM: {s.TotalDsmDeltaVKms*1000:F0} m/s, 飞掠: {s.TotalFlybyDeltaVKms*1000:F1} m/s, 总ΔV: {s.TotalDeltaVKms*1000:F0} m/s, 航时: {s.TotalFlightDays:F1}d, 闭环: {s.AllLegsFeasible}");
                    if (s.Legs != null)
                    {
                        for (int k = 0; k < s.Legs.Count; k++)
                        {
                            Console.WriteLine($"      Leg #{k+1}: {s.Legs[k].FromBody}->{s.Legs[k].ToBody}, ToF={s.Legs[k].ToFDays:F1}d");
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

            Console.WriteLine("  DE 求解未返回任何解！");
            return EvaluationOutcome.NotConverged(
                "在年粒度发射机会窗内 DE 未收敛出任何解 (指定 E-J-Pluto 拓扑)", MissionDvBudgetMs);
        }
    }
}
