using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    public class HistoricalTopologiesDiagnosisTest : ITestModule
    {
        public string ModuleId => "historical_topologies_diagnosis";
        public string Name => "开普勒物理图推导与拓扑诊断测试套件";
        public string Description => "验证开普勒物理图纯数学推导与星历过滤拓扑诊断 (0 模板 0 预制)";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("HistoricalTopologies_Diagnosis");
            bool ok = true;
            try
            {
                DiagnoseHistoricalTopologies(null);
            }
            catch (Exception ex)
            {
                ctx.Fail($"拓扑诊断异常: {ex.Message}");
                ok = false;
            }
            ctx.EndCase(ok);
            return ok;
        }

        public static void DiagnoseHistoricalTopologies(string filter = null)
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🔬 [DiagnoseHistoricalTopologies] 开普勒物理图纯数学推导与星历过滤拓扑诊断 (0 模板 0 预制)");
            Console.WriteLine("==========================================================================================\n");

            var cases = MGAHistoricalBlindSearchSuite.HistoricalCatalog;
            if (!string.IsNullOrEmpty(filter))
            {
                cases = cases.Where(c => c.MissionId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         c.MissionName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         c.DestinationBody.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            var summaryList = new List<(string Name, bool InRaw, bool InDiverse, bool InPool, int Rank, int MaxPool)>();

            foreach (var bCase in cases)
            {
                Console.WriteLine($"------------------------------------------------------------------------------------------");
                Console.WriteLine($"🛰️ 任务: {bCase.MissionName} | {bCase.DepartureBody} ➔ {bCase.DestinationBody}");
                Console.WriteLine($"   历史真值拓扑: {(bCase.HistoricalFlybySequence.Count > 0 ? string.Join(" ➔ ", bCase.HistoricalFlybySequence) : "(直达)")}");

                double startUT = GMATClient.ParseUtcGregorian(bCase.HistoricalLaunchStr);
                double maxBudget = (bCase.VesselMaxDeltaVMs > 0) ? (bCase.VesselMaxDeltaVMs / 1000.0) : 7.0;
                var rawCandidates = MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.SearchTopologies(
                    bCase.DepartureBody, bCase.DestinationBody, "Sun", maxBudget, 8, bCase.MaxCandidateTopologies * 8);

                Console.WriteLine($"   • [1. 开普勒图论搜索] 原始生成: {rawCandidates.Count} 组拓扑");
                bool inRaw = rawCandidates.Any(c => c.FlybyChain.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase));
                Console.WriteLine($"     -> 包含历史拓扑: {(inRaw ? "✅ YES" : "❌ NO")}");

                var histInRaw = rawCandidates.FirstOrDefault(c => c.FlybyChain.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase));
                if (histInRaw.FlybyChain != null)
                {
                    Console.WriteLine($"     [Hist in Raw]: Category={histInRaw.TopologyCategory}, ParetoScore={histInRaw.ParetoScore:F2}, LaunchDv={histInRaw.LaunchDvKms:F2} km/s, Deficit={histInRaw.AssistDeficitKms:F2} km/s, ToF={histInRaw.TotalToFDays:F0}d");
                }

                if (!inRaw && bCase.HistoricalFlybySequence.Count > 0)
                {
                    var nodes = MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.GetSystemNodes("Sun", bCase.DepartureBody, bCase.DestinationBody);
                    double muCentral = SolarSystemCatalog.SunMu;
                    var full = new List<string> { bCase.DepartureBody };
                    full.AddRange(bCase.HistoricalFlybySequence);
                    full.Add(bCase.DestinationBody);
                    Console.WriteLine($"     [Prune Analysis for {string.Join("->", full)}]:");
                    double cumDef = 0;
                    double totToF = 0;
                    for (int k = 1; k < full.Count; k++)
                    {
                        string pFrom = full[k - 1];
                        string pCurr = full[k];
                        var nFrom = nodes.FirstOrDefault(n => n.Name.Equals(pFrom, StringComparison.OrdinalIgnoreCase));
                        var nCurr = nodes.FirstOrDefault(n => n.Name.Equals(pCurr, StringComparison.OrdinalIgnoreCase));
                        if (nFrom == null || nCurr == null) { Console.WriteLine($"       Leg {k}: Node not found: {pFrom} or {pCurr}!"); break; }
                        double stepDef = 0;
                        double legToF = 0;
                        if (k == 1)
                        {
                            MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.CalcHohmannLeg(nFrom.SMA, nCurr.SMA, muCentral, out _, out _, out legToF);
                        }
                        else
                        {
                            var nPrev = nodes.FirstOrDefault(n => n.Name.Equals(full[k - 2], StringComparison.OrdinalIgnoreCase));
                            stepDef = MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.CalcAssistDeficit(nFrom, nPrev.SMA, nCurr.SMA, muCentral, out _);
                            MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.CalcHohmannLeg(nFrom.SMA, nCurr.SMA, muCentral, out _, out _, out legToF);
                            if (nFrom.IsGiant && nCurr.SMA > nFrom.SMA) legToF *= 0.35;
                        }
                        cumDef += stepDef;
                        totToF += legToF;
                        Console.WriteLine($"       Step {pFrom}->{pCurr}: stepDef={stepDef:F2} km/s, cumDef={cumDef:F2} km/s (budget={maxBudget:F1} km/s), legToF={legToF:F0}d, totToF={totToF:F0}d");
                    }
                }

                var diverse = MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.SelectDiverseTopologies(
                    rawCandidates, Math.Max(bCase.MaxCandidateTopologies * 512, 4096), bCase.DestinationBody);
                Console.WriteLine($"   • [2. 多样性聚类池] 产生: {diverse.Count} 组拓扑");
                bool inDiverse = diverse.Any(c => c.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase));
                Console.WriteLine($"     -> 包含历史拓扑: {(inDiverse ? "✅ YES" : "❌ NO")}");

                var multiItems = diverse.Where(d => d.Count == bCase.HistoricalFlybySequence.Count).ToList();
                Console.WriteLine($"     [Matching Length ({bCase.HistoricalFlybySequence.Count}) in Diverse: {multiItems.Count}]:");
                foreach (var m in multiItems.Take(6)) Console.WriteLine($"       -> {string.Join("->", m)}");

                double budgetMs = bCase.VesselMaxDeltaVMs;
                double dsmBudgetMs = (bCase.PropulsionType == MGAPropulsionType.LowThrustElectric)
                    ? Math.Max(bCase.MaxDsmDeltaVMs, budgetMs)
                    : ((bCase.MaxDsmDeltaVMs > 0) ? bCase.MaxDsmDeltaVMs : budgetMs);

                if (bCase.HistoricalFlybySequence.Count > 0)
                {
                    var histRep = MGAEphemerisReachabilityAnalyzer.AnalyzeChainReachability(
                        bCase.DepartureBody, bCase.DestinationBody, bCase.HistoricalFlybySequence, startUT, budgetMs, dsmBudgetMs, bCase.NoInsertionBurn);
                    Console.WriteLine($"     [Reachability for History]: IsFeasible={histRep.IsFeasible}, EstTotalDv={histRep.EstimatedTotalDvMs:F1} m/s, Deficit={histRep.TotalDeficitMs:F1} m/s");
                    foreach (var l in histRep.LegReports)
                    {
                        Console.WriteLine($"       Leg {l.FromBody} -> {l.ToBody}: EnergyDeficit={l.EnergyDeficitMs:F1} m/s, TurnDeficit={l.TurnDeficitMs:F1} m/s, Rationale={l.Rationale}");
                    }
                }

                var filtered = MGAEphemerisReachabilityAnalyzer.FilterAndPrioritizeReachableSequences(
                    bCase.DepartureBody, bCase.DestinationBody, diverse, startUT, budgetMs, dsmBudgetMs, null, bCase.NoInsertionBurn);

                int histRankInFiltered = filtered.FindIndex(f => f.FlybyChain.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase));
                Console.WriteLine($"     [Hist Rank in Filtered]: {(histRankInFiltered >= 0 ? $"Rank #{histRankInFiltered + 1} of {filtered.Count} (Pareto={filtered[histRankInFiltered].ParetoScore:F2}, Dv={filtered[histRankInFiltered].EstimatedTotalDvMs:F0}, ToF={filtered[histRankInFiltered].EstimatedToFDays:F0}d, Def={filtered[histRankInFiltered].TotalDeficitMs:F0})" : "NOT FOUND")}");

                string histFirst = (bCase.HistoricalFlybySequence != null && bCase.HistoricalFlybySequence.Count > 0) ? bCase.HistoricalFlybySequence[0] : "Direct";
                var inSameGateway = filtered.Where(f => {
                    string fFirst = (f.FlybyChain != null && f.FlybyChain.Count > 0) ? f.FlybyChain[0] : "Direct";
                    return fFirst.Equals(histFirst, StringComparison.OrdinalIgnoreCase);
                }).ToList();
                int histRankInGateway = inSameGateway.FindIndex(f => f.FlybyChain.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase));
                Console.WriteLine($"     [Hist in Gateway '{histFirst}']: Rank #{histRankInGateway + 1} of {inSameGateway.Count}");
                Console.WriteLine($"     [All {inSameGateway.Count} in Gateway '{histFirst}']: ");
                for (int gi = 0; gi < inSameGateway.Count; gi++)
                {
                    var gfc = inSameGateway[gi];
                    Console.WriteLine($"       #{gi + 1}: Pareto={gfc.ParetoScore:F2}, Dv={gfc.EstimatedTotalDvMs:F0}, ToF={gfc.EstimatedToFDays:F0}d, Chain={string.Join("->", gfc.FlybyChain)}");
                }
                if (!filtered.Take(bCase.MaxCandidateTopologies).Any(c => c.FlybyChain.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"       [Top 10 in Filtered for Missed Mission]:");
                    for (int fi = 0; fi < Math.Min(10, filtered.Count); fi++)
                    {
                        var fc = filtered[fi];
                        double capDv = fc.EstimatedTotalDvMs - fc.EstimatedLaunchDvMs - fc.TotalDeficitMs;
                        Console.WriteLine($"         #{fi + 1}: Pareto={fc.ParetoScore:F2}, Dv={fc.EstimatedTotalDvMs:F0} (Lch={fc.EstimatedLaunchDvMs:F0}, Dsm={fc.TotalDeficitMs:F0}, Cap={capDv:F0}), ToF={fc.EstimatedToFDays:F0}d, Chain={string.Join("->", fc.FlybyChain)}");
                        if (fi == 0)
                        {
                            foreach (var l in fc.LegReports)
                            {
                                Console.WriteLine($"           Leg {l.FromBody}->{l.ToBody}: EDef={l.EnergyDeficitMs:F0}, TDef={l.TurnDeficitMs:F0}, {l.Rationale}");
                            }
                        }
                    }
                }

                var finalTopos = filtered.Select(f => f.FlybyChain).Take(bCase.MaxCandidateTopologies).ToList();
                bool inFinal = finalTopos.Any(c => c.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase));
                Console.WriteLine($"   • [3. 最终 DE 演化池] ({finalTopos.Count} 组): 包含历史拓扑: {(inFinal ? "✅ YES" : "❌ NO")}");
                for (int i = 0; i < Math.Min(15, filtered.Count); i++)
                {
                    var f = filtered[i];
                    string mark = f.FlybyChain.SequenceEqual(bCase.HistoricalFlybySequence, StringComparer.OrdinalIgnoreCase) ? " ★ [历史命中]" : "";
                    string inPool = i < bCase.MaxCandidateTopologies ? "[IN POOL]" : "[OUT]";
                    Console.WriteLine($"       #{i + 1} {inPool}: Pareto={f.ParetoScore:F2}, Dv={f.EstimatedTotalDvMs:F0}m/s, ToF={f.EstimatedToFDays:F0}d, Chain={bCase.DepartureBody} ➔ {(f.FlybyChain.Count > 0 ? string.Join(" ➔ ", f.FlybyChain) + " ➔ " : "")}{bCase.DestinationBody}{mark}");
                }

                summaryList.Add((bCase.MissionName, inRaw, inDiverse, inFinal, histRankInFiltered >= 0 ? (histRankInFiltered + 1) : -1, bCase.MaxCandidateTopologies));

                if (bCase.MissionId.Equals("bepicolombo", StringComparison.OrdinalIgnoreCase))
                {
                    DiagnoseBepiColomboVenusMercury();
                }
            }

            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("🏆 人类历史 13 项经典深空 MGA 任务纯盲搜推导演化池全景对齐测试大榜");
            Console.WriteLine("==========================================================================================");
            Console.WriteLine($"{"任务名称",-32} | {"图论推导",-6} | {"多样性池",-6} | {"DE演化池",-6} | {"实际排名 / 容量",-16} | 命中状态");
            Console.WriteLine(new string('-', 92));
            int hitCount = 0;
            foreach (var s in summaryList)
            {
                string status = s.InPool ? "★ 历史命中" : "❌ 未入池";
                if (s.InPool) hitCount++;
                string rankStr = s.Rank > 0 ? $"Rank #{s.Rank} / {s.MaxPool}" : "NOT FOUND";
                Console.WriteLine($"{s.Name,-32} | {(s.InRaw ? "✅ YES" : "❌ NO"),-6} | {(s.InDiverse ? "✅ YES" : "❌ NO"),-6} | {(s.InPool ? "✅ YES" : "❌ NO"),-6} | {rankStr,-16} | {status}");
            }
            Console.WriteLine(new string('-', 92));
            Console.WriteLine($"★ 历史任务入池综合命中率: {hitCount} / {summaryList.Count} ({hitCount * 100.0 / summaryList.Count:F1}%)");
            Console.WriteLine("==========================================================================================\n");
        }

        public static void DiagnoseBepiColomboVenusMercury()
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🔬 [BepiColombo Diagnostic] Venus 2 (10 Aug 2021) ➔ Mercury 1 Astrodynamics Scan");
            Console.WriteLine("==========================================================================================");
            var sunMu = SolarSystemCatalog.SunMu;
            var venus = SolarSystemCatalog.GetPlanet("Venus");
            var mercury = SolarSystemCatalog.GetPlanet("Mercury");

            // Historical departure from Venus 2: ~10 Aug 2021
            double depUT = GMATClient.ParseUtcGregorian("10 Aug 2021 13:51:00.000");
            venus.GetRelativeState("Sun", depUT, out Vector3d rV, out Vector3d vV);

            // Previous arrival into Venus: from Venus-Venus 4:3 resonance (299.6 days)
            double prevArrUT = depUT - 299.6 * 86400.0;
            venus.GetRelativeState("Sun", prevArrUT, out Vector3d rV0, out Vector3d vV0);
            PykepNative.SolveLambert(rV0, rV, 299.6 * 86400.0, sunMu, false, 1, out Vector3d vResDep, out Vector3d vResArr);
            Vector3d vInfIn = vResArr - vV;
            Console.WriteLine($"Venus 2 Incoming v_inf from 4:3 resonance: {vInfIn.magnitude:F2} km/s");

            double vSafeKm = venus.SafeRadiusKm > 0 ? venus.SafeRadiusKm : 6452.0;
            double eIn = 1.0 + vSafeKm * vInfIn.sqrMagnitude / venus.Mu;
            double deltaMax = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eIn)) * 180.0 / Math.PI;
            Console.WriteLine($"Venus Safe Turn Limit for v_inf: {deltaMax:F1}°");
            Console.WriteLine("------------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"{"ToF(d)",-8} | {"Arr Date",-15} | {"vDepInf",-8} | {"vArrInf",-8} | {"Turn(°)",-8} | {"TurnOk",-8} | {"rMerc(AU)",-10} | {"Score",-8}");
            Console.WriteLine(new string('-', 96));

            for (double tofD = 40.0; tofD <= 95.0; tofD += 2.0)
            {
                double arrUT = depUT + tofD * 86400.0;
                mercury.GetRelativeState("Sun", arrUT, out Vector3d rM, out Vector3d vM);
                if (PykepNative.SolveLambert(rV, rM, tofD * 86400.0, sunMu, false, 0, out Vector3d vDep, out Vector3d vArr))
                {
                    Vector3d vInfDep = vDep - vV;
                    Vector3d vInfArr = vArr - vM;
                    double dot = Vector3d.Dot(vInfIn.normalized, vInfDep.normalized);
                    dot = Math.Max(-1.0, Math.Min(1.0, dot));
                    double turn = Math.Acos(dot) * 180.0 / Math.PI;
                    bool turnOk = turn <= deltaMax + 5.0;
                    double rMercAU = rM.magnitude / 149597870.7;
                    string dateStr = GMATClient.FormatUtcGregorian(arrUT).Substring(0, 11);
                    double vPump = Math.Abs(vInfDep.magnitude - vInfIn.magnitude);
                    double score = turn + vPump * 2.0;
                    Console.WriteLine($"{tofD,8:F1} | {dateStr,-15} | {vInfDep.magnitude,8:F2} | {vInfArr.magnitude,8:F2} | {turn,8:F1} | {(turnOk ? "✅ YES" : "❌ NO"),-8} | {rMercAU,10:F3} | {score,8:F2}");
                }
            }
            Console.WriteLine("------------------------------------------------------------------------------------------------------------");
            Console.WriteLine("🔬 [BepiColombo Diagnostic] Mercury 1 (01 Oct 2021) ➔ Mercury 2 Resonant Scan (Incoming v_inf = 6.76 km/s)");
            Console.WriteLine("------------------------------------------------------------------------------------------------------------");
            double merc1UT = depUT + 52.0 * 86400.0; // 01 Oct 2021
            mercury.GetRelativeState("Sun", merc1UT, out Vector3d rM1, out Vector3d vM1);
            // Arrival asymptote from Venus transfer at 52d:
            PykepNative.SolveLambert(rV, rM1, 52.0 * 86400.0, sunMu, false, 0, out _, out Vector3d vArrM1);
            Vector3d vInfInMerc = vArrM1 - vM1;
            Console.WriteLine($"Mercury 1 Incoming v_inf: {vInfInMerc.magnitude:F2} km/s");

            double mSafeKm = mercury.SafeRadiusKm > 0 ? mercury.SafeRadiusKm : 2640.0;
            double eInMerc = 1.0 + mSafeKm * vInfInMerc.sqrMagnitude / mercury.Mu;
            double deltaMaxMerc = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eInMerc)) * 180.0 / Math.PI;
            Console.WriteLine($"Mercury Safe Turn Limit for v_inf: {deltaMaxMerc:F1}°");
            Console.WriteLine($"{"ToF(d)",-8} | {"Ratio",-8} | {"Arr Date",-15} | {"vDepInf",-8} | {"Turn(°)",-8} | {"TurnOk",-8} | {"1-DSM(m/s)",-10}");
            Console.WriteLine(new string('-', 85));

            double[] testToFs = new double[] { 87.97, 109.96, 175.94, 263.91, 351.88, 439.85 };
            string[] testRatios = new string[] { "1:1", "5:4", "2:1", "3:1", "4:1", "5:1" };
            for (int ti = 0; ti < testToFs.Length; ti++)
            {
                double tofM = testToFs[ti];
                double arrM2UT = merc1UT + tofM * 86400.0;
                mercury.GetRelativeState("Sun", arrM2UT, out Vector3d rM2, out Vector3d vM2);
                
                // Construct unpowered flyby outgoing v_inf:
                // If we deflect vInfInMerc by delta <= deltaMaxMerc:
                // Find turning direction that minimizes DSM
                double bestDsm = double.MaxValue;
                double bestTurn = 0.0;
                for (double eta = 0.15; eta <= 0.85; eta += 0.10)
                {
                    double dt1 = eta * tofM * 86400.0;
                    double dt2 = (1.0 - eta) * tofM * 86400.0;
                for (double turnFrac = 0.0; turnFrac <= 1.0; turnFrac += 0.25)
                {
                    double delta = deltaMaxMerc * turnFrac * Math.PI / 180.0;
                    for (double beta = -Math.PI; beta <= Math.PI; beta += Math.PI / 4.0)
                    {
                        // Get deflected v_dep
                        Vector3d e1 = Vector3d.Cross(vInfInMerc, rM1).normalized;
                        if (e1.sqrMagnitude < 0.001) e1 = Vector3d.Cross(vInfInMerc, vM1).normalized;
                        Vector3d e2 = Vector3d.Cross(vInfInMerc.normalized, e1).normalized;
                        Vector3d uOut = vInfInMerc.normalized * Math.Cos(delta) + (e1 * Math.Cos(beta) + e2 * Math.Sin(beta)) * Math.Sin(delta);
                        Vector3d vDep = vM1 + uOut * vInfInMerc.magnitude;

                        // Propagate dt1
                        Vector3d rDsm = rM1;
                        Vector3d vDsmM = vDep;
                        PykepNative.PropagateLagrangian(ref rDsm, ref vDsmM, dt1, sunMu);

                        // Solve Lambert dt2
                        int nrevDsm = Math.Max(0, (int)Math.Floor(dt2 / (87.97 * 86400.0)));
                        for (int tryNrev = 0; tryNrev <= nrevDsm; tryNrev++)
                        {
                            if (PykepNative.SolveLambert(rDsm, rM2, dt2, sunMu, false, tryNrev, out Vector3d vDsmP, out _))
                            {
                                double dsm = (vDsmP - vDsmM).magnitude * 1000.0;
                                if (dsm < bestDsm)
                                {
                                    bestDsm = dsm;
                                    bestTurn = delta * 180.0 / Math.PI;
                                }
                            }
                        }
                    }
                }
                }
                string dateStr = GMATClient.FormatUtcGregorian(arrM2UT).Substring(0, 11);
                Console.WriteLine($"{tofM,8:F1} | {testRatios[ti],-8} | {dateStr,-15} | {vInfInMerc.magnitude,8:F2} | {bestTurn,8:F1} | {(bestTurn <= deltaMaxMerc ? "✅ YES" : "❌ NO"),-8} | {bestDsm,10:F0}");
            }
            Console.WriteLine("==========================================================================================\n");
        }
    }
}
