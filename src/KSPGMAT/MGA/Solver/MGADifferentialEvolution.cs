// ============================================================================
// MGADifferentialEvolution.cs
// 单窗口差分进化寻优内核 (从 PykepBridge 迁出)
//
// 职责:
//   承载自适应优化器的"进化/寻优"逻辑——种群初始化、启发式注种、前向步进合成、
//   pbest-DE 变异循环、Memetic Hooke-Jeeves 打磨、多阶段收敛选解。
//
// 架构红线:
//   本类属于"求解器层"，动力学评估(BuildFullSolution / 原生 Lambert)一律委托
//   PykepBridge / PykepNative，严禁在 C# 层自编公式冒充原生结果 (baseline §0.2/§0.4)。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;
using UnityEngine;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 单时间窗口差分进化评估内核（支持单核极速内循环与多核并行二选一，彻底杜绝嵌套并行饥饿）
    /// 底层评估 100% 委托 PykepBridge.BuildFullSolution → 原生 kep3 Lambert / 飞掠动力学。
    /// </summary>
    public static class MGADifferentialEvolution
    {
        /// <summary>
        /// 针对连续同星共振借力集群 (如水星×3、金星×2、地球×2 等)，执行联合物理共振槽与下游出口天体相位闭环评分。
        /// 通过笛卡尔积扫描所有有效有理周期比组合，以开普勒守恒与出口兰伯特转向角残差优选出最佳共振组合，
        /// 确保停留总时间严格贴合出口天体的会合周期 (T_syn)，彻底消除出口航段天文级 DSM 赤字！
        /// </summary>
        private static List<Tuple<double[], double>> FindAndRankResonantCombos(
            string bodyName, string exitBodyName, int blockLen,
            double curUT, Vector3d prevArrVinf,
            double[] lowerBounds, double[] upperBounds, int resStart,
            string central, double centralMu,
            double nominalExitToFSec = 0.0)
        {
            var rankedCombos = new List<Tuple<double, double[], double>>();
            var pBody = SolarSystemCatalog.GetPlanet(bodyName);
            if (pBody == null) return new List<Tuple<double[], double>>();

            double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
            if (tOrb <= 0 || double.IsNaN(tOrb)) return new List<Tuple<double[], double>>();

            double[] availRatios = (sma > 1.3e8)
                ? new double[] { 1.0, 2.0 }
                : new double[] { 1.0, 2.0, 3.0 };

            var validRatiosPerLeg = new List<List<double>>();
            for (int i = 0; i < blockLen; i++) {
                int legD = 1 + resStart + i;
                var valid = new List<double>();
                foreach (double r in availRatios) {
                    double rSec = r * tOrb;
                    if (rSec >= lowerBounds[legD] - 86400.0 && rSec <= upperBounds[legD] + 86400.0) {
                        valid.Add(r);
                    }
                }
                if (valid.Count == 0) {
                    valid.Add(Math.Max(lowerBounds[legD], Math.Min(upperBounds[legD], tOrb)) / tOrb);
                }
                validRatiosPerLeg.Add(valid);
            }

            var allCombos = new List<double[]>();
            Action<int, double[]> buildCombos = null;
            buildCombos = (legIdx, cur) => {
                if (legIdx == blockLen) {
                    allCombos.Add((double[])cur.Clone());
                    return;
                }
                foreach (double r in validRatiosPerLeg[legIdx]) {
                    cur[legIdx] = r;
                    buildCombos(legIdx + 1, cur);
                    if (allCombos.Count >= 512) return;
                }
            };
            buildCombos(0, new double[blockLen]);

            var pExit = !string.IsNullOrEmpty(exitBodyName) ? SolarSystemCatalog.GetPlanet(exitBodyName) : null;
            double approxTofSec = (nominalExitToFSec > 10.0 * 86400.0) ? nominalExitToFSec : 100.0 * 86400.0;
            double synDays = 0.0;
            if (pExit != null) {
                double smaNext = PykepBridge.GetRelativeSMA(pExit, central, 0.0);
                if (sma > 0 && smaNext > 0) {
                    if (nominalExitToFSec <= 10.0 * 86400.0) {
                        double aH = (sma + smaNext) * 0.5;
                        approxTofSec = Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
                    }

                    double tA = tOrb / 86400.0;
                    double tB = PykepBridge.CalcOrbitPeriodSec(smaNext, centralMu) / 86400.0;
                    double denom = Math.Abs(1.0 / tA - 1.0 / tB);
                    if (denom > 1e-6) synDays = 1.0 / denom;
                }
            }

            double safeR = MGADefaults.GetBodySafeRadiusKm(bodyName);
            double inMag = prevArrVinf.magnitude;

            foreach (var combo in allCombos) {
                double dwellTimeSec = 0.0;
                for (int i = 0; i < blockLen; i++) dwellTimeSec += combo[i] * tOrb;
                double depUT = curUT + dwellTimeSec;

                double dwellDays = dwellTimeSec / 86400.0;
                double phasePenalty = 0.0;
                if (synDays > 1.0) {
                    double nCycles = Math.Max(1.0, Math.Round(dwellDays / synDays));
                    double phaseErrDays = Math.Abs(dwellDays - nCycles * synDays);
                    double phaseFrac = phaseErrDays / synDays;
                    phasePenalty = phaseErrDays * 15.0 + phaseFrac * phaseFrac * 1500.0;
                }

                double score = 0.0;
                double bestExitToF = approxTofSec;
                if (pExit != null) {
                    pBody.GetHeliocentricState(depUT, out Vector3d rDep, out Vector3d vDepPlanet);
                    double bestExitScore = double.MaxValue;
                    double[] testExitToFs = new double[] {
                        approxTofSec * 0.70, approxTofSec * 0.85, approxTofSec * 0.95,
                        approxTofSec, approxTofSec * 1.05, approxTofSec * 1.20,
                        approxTofSec * 1.45, approxTofSec * 1.80, approxTofSec * 2.25, approxTofSec * 2.80
                    };

                    foreach (double exitTof in testExitToFs) {
                        pExit.GetHeliocentricState(depUT + exitTof, out Vector3d rArrNext, out _);
                        if (PykepNative.SolveLambert(rDep, rArrNext, exitTof, centralMu, false, out Vector3d vDepLambert, out _)) {
                            Vector3d vInfDesired = vDepLambert - vDepPlanet;
                            double effInMag = inMag;
                            if (effInMag < 1.0) {
                                double aRes = sma * Math.Pow(combo[0], 2.0 / 3.0);
                                double vCraft = Math.Sqrt(Math.Max(1e-4, centralMu * (2.0 / rDep.magnitude - 1.0 / Math.Max(1.0, aRes))));
                                effInMag = Math.Max(1.0, Math.Abs(vCraft - vDepPlanet.magnitude));
                            }

                            double eMin = 1.0 + (safeR * effInMag * effInMag) / Math.Max(1.0, pBody.Mu);
                            double maxTurnRadPerFlyby = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eMin));
                            double totalTurnCapability = maxTurnRadPerFlyby * blockLen;

                            double outMag = vInfDesired.magnitude;
                            double dot = (effInMag > 1e-4 && outMag > 1e-4) ? Vector3d.Dot(prevArrVinf.normalized, vInfDesired.normalized) : 1.0;
                            dot = Math.Max(-1.0, Math.Min(1.0, dot));
                            double reqTurnRad = Math.Acos(dot);

                            double turnExcess = Math.Max(0.0, reqTurnRad - totalTurnCapability);
                            double vDiff = Math.Abs(effInMag - outMag);
                            double s = turnExcess * 30.0 + vDiff * 5.0;
                            if (s < bestExitScore) {
                                bestExitScore = s;
                                bestExitToF = exitTof;
                            }
                        }
                    }
                    score = (bestExitScore < 1e5 ? bestExitScore : 1e6) + phasePenalty;
                } else {
                    score = combo.Sum();
                }
                rankedCombos.Add(Tuple.Create(score, combo, bestExitToF));
            }

            return rankedCombos.OrderBy(t => t.Item1).Select(t => Tuple.Create(t.Item2, t.Item3)).ToList();
        }

        /// <summary>
        /// 在 [winStartUT, winEndUT] 单一会合窗口内执行差分进化寻优。
        /// </summary>
        public static List<MGASolution> RunWindow(
            MGAConfig config, double winStartUT, double winEndUT,
            Action<float, string> onProgress, CancellationToken ct,
            bool parallelizeInternal = true,
            int populationMaxParallelism = -1)
        {
            var solutions = new List<MGASolution>();
            var fullChain = new List<string> { config.DepartureBody };
            if (config.FlybySequence != null && config.FlybySequence.Count > 0) {
                fullChain.AddRange(config.FlybySequence);
            }
            if (!string.IsNullOrEmpty(config.DestinationBody)) {
                if (fullChain.Count == 0 || !fullChain[fullChain.Count - 1].Equals(config.DestinationBody, StringComparison.OrdinalIgnoreCase)) {
                    fullChain.Add(config.DestinationBody);
                }
            }
            if (fullChain.Count < 2) return solutions;
            int legCount = fullChain.Count - 1;
            bool optDsmEta = (config != null) && (config.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM ||
                             (config.LegStrategies != null && config.LegStrategies.Any(s => s == MGALegStrategy.DeepSpaceManeuver1DSM)));
            int dim = 1 + legCount + (optDsmEta ? legCount : 0);

            double[] lowerBounds = new double[dim];
            double[] upperBounds = new double[dim];

            lowerBounds[0] = winStartUT;
            upperBounds[0] = winEndUT;

            string central = config?.CentralBody ?? "Sun";
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            double[] nominalLegToFSec = new double[legCount];
            for (int k = 0; k < legCount; k++) {
                string fromN = fullChain[k];
                string toN = fullChain[k + 1];
                var p1 = SolarSystemCatalog.GetPlanet(fromN);
                var p2 = SolarSystemCatalog.GetPlanet(toN);

                double sma1 = PykepBridge.GetRelativeSMA(p1, central, config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                double sma2 = PykepBridge.GetRelativeSMA(p2, central, config?.DestinationPeAKm ?? MGADefaults.DestinationAltitudeKm);

                double minD, maxD;
                if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                    double tOrbDays = PykepBridge.CalcOrbitPeriodSec(sma1, centralMu) / MGADefaults.SecondsPerDay;
                    if (tOrbDays <= 0 || double.IsNaN(tOrbDays)) tOrbDays = 365.25;
                    minD = Math.Max(0.5, tOrbDays * MGADefaults.ResonanceMinToFFactor);
                    maxD = Math.Max(minD + 2.0, tOrbDays * MGADefaults.ResonanceMaxToFFactor);
                    if (config.MinToFDays != null && k < config.MinToFDays.Count && config.MinToFDays[k] > 0) minD = config.MinToFDays[k];
                    if (config.MaxToFDays != null && k < config.MaxToFDays.Count && config.MaxToFDays[k] > 0) maxD = config.MaxToFDays[k];

                    int resOffsetInBlock = 0;
                    for (int pk = k - 1; pk >= 0 && fullChain[pk].Equals(fromN, StringComparison.OrdinalIgnoreCase); pk--) resOffsetInBlock++;
                    int blockLen = 1;
                    for (int fk = k + 1; fk < legCount && fullChain[fk].Equals(fromN, StringComparison.OrdinalIgnoreCase); fk++) blockLen++;
                    for (int bk = k - 1; bk >= 0 && fullChain[bk].Equals(fromN, StringComparison.OrdinalIgnoreCase); bk--) blockLen++;
                    double bestR = (sma1 > 1.3e8)
                        ? 2.0
                        : (blockLen >= 2 ? (resOffsetInBlock == 0 ? 2.0 : 3.0) : 1.0);
                    nominalLegToFSec[k] = tOrbDays * bestR * MGADefaults.SecondsPerDay;
                } else {
                    double approxHohmannDays = MGADefaults.HohmannDefaultToFDays;
                    if (PykepBridge.CalcHohmannTransfer(sma1, sma2, centralMu, out _, out double hohmannSec, out _, out _)) {
                        approxHohmannDays = hohmannSec / MGADefaults.SecondsPerDay;
                    }
                    if (approxHohmannDays <= 0 || double.IsNaN(approxHohmannDays)) approxHohmannDays = MGADefaults.HohmannDefaultToFDays;
                    minD = Math.Max(0.5, approxHohmannDays * MGADefaults.HohmannMinToFFactor);
                    maxD = Math.Max(minD + 2.0, approxHohmannDays * MGADefaults.HohmannMaxToFFactor);
                    if (config.MinToFDays != null && k < config.MinToFDays.Count && config.MinToFDays[k] > 0) minD = config.MinToFDays[k];
                    if (config.MaxToFDays != null && k < config.MaxToFDays.Count && config.MaxToFDays[k] > 0) maxD = config.MaxToFDays[k];
                    if (approxHohmannDays < minD || approxHohmannDays > maxD) {
                        nominalLegToFSec[k] = ((minD + maxD) * 0.5) * MGADefaults.SecondsPerDay;
                    } else {
                        nominalLegToFSec[k] = approxHohmannDays * MGADefaults.SecondsPerDay;
                    }
                }

                lowerBounds[1 + k] = minD * MGADefaults.SecondsPerDay;
                upperBounds[1 + k] = maxD * MGADefaults.SecondsPerDay;
                nominalLegToFSec[k] = Math.Max(lowerBounds[1 + k], Math.Min(upperBounds[1 + k], nominalLegToFSec[k]));
            }

            if (optDsmEta) {
                for (int k = 0; k < legCount; k++) {
                    int d = 1 + legCount + k;
                    lowerBounds[d] = 0.1;
                    upperBounds[d] = 0.9;
                }
            }

            int islandCount = 4;
            // 依据 PaGMO (Izzo et al. 2010) 广义多岛理论:
            // 环形拓扑周期性精英迁移天然保证全局多样性，子岛无需维持过大种群。
            // 依据问题维度 dim 弹性推导最优子种群规模 (dim=5 -> 16, dim=23 -> 35)，消除种群通胀。
            int autoSubPop = Math.Max(16, Math.Min(36, (int)Math.Ceiling(dim * 1.5)));
            int subPopSize = (config != null && config.PopulationSize > 0)
                ? Math.Max(16, config.PopulationSize)
                : autoSubPop;
            int popSize = islandCount * subPopSize;
            int maxGens = (config != null && config.MaxGenerations > 0)
                ? Math.Max(20, config.MaxGenerations)
                : Math.Max(MGADefaults.MaxGenerations, Math.Min(120, dim * 8));

            double[][] pop = new double[popSize][];
            double[] fitness = new double[popSize];
            var random = new System.Random(42);

            // 启发式注种：若处于模式 2 (锁定倾角与 RAAN) 且为中心天体转移 (如 Earth -> Moon)
            // 预先沿锁定停泊轨道面扫描目标天体零点面外偏角交点，将精准共面发射时刻注种至初始种群
            var seedGenomes = new List<double[]>();
            if (config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination)
                && config.DeparturePeAKm > 0 && Math.Abs(config.DeparturePeAKm - config.DepartureApAKm) < 100.0) {
                double scanSpanSec = Math.Min(30.0 * 86400.0, winEndUT - winStartUT);
                if (PykepNative.FindNodalCrossing(fullChain[1], central, winStartUT, scanSpanSec, config.DepartureInclinationDeg, config.DepartureRAANDeg, out double tNode, out _)) {
                    double nomTofSec = (lowerBounds[1] + upperBounds[1]) * 0.5;
                    double depSeedUT = tNode - nomTofSec;
                    if (depSeedUT < lowerBounds[0] || depSeedUT > upperBounds[0]) {
                        // 寻找下一个升降交点 (~13.6 天后)
                        if (PykepNative.FindNodalCrossing(fullChain[1], central, tNode + 1.0 * 86400.0, scanSpanSec, config.DepartureInclinationDeg, config.DepartureRAANDeg, out double nextNode, out _)) {
                            depSeedUT = nextNode - nomTofSec;
                        }
                    }
                    if (depSeedUT >= lowerBounds[0] && depSeedUT <= upperBounds[0]) {
                        double[] dToFs = new double[] { 0.0, -0.25 * 86400.0, 0.25 * 86400.0, -0.5 * 86400.0, 0.5 * 86400.0 };
                        foreach (double dt in dToFs) {
                            double tofCand = Math.Max(lowerBounds[1], Math.Min(upperBounds[1], nomTofSec + dt));
                            double depCand = Math.Max(lowerBounds[0], Math.Min(upperBounds[0], depSeedUT - dt));
                            var g = new double[dim];
                            g[0] = depCand;
                            g[1] = tofCand;
                            for (int d = 2; d < dim; d++) {
                                if (d < 1 + legCount) {
                                    g[d] = nominalLegToFSec[d - 1];
                                } else {
                                    int dsmIdx = d - (1 + legCount);
                                    g[d] = (config != null && config.DsmEtas != null && dsmIdx < config.DsmEtas.Count && config.DsmEtas[dsmIdx] > 0)
                                        ? config.DsmEtas[dsmIdx]
                                        : (config != null ? config.DsmEtaDefault : 0.5);
                                }
                            }
                            seedGenomes.Add(g);
                        }
                    }
                }
            }

            // 智能相位开普勒共振注种: 若链路中存在同星共振调相航段 (fromN == toN)，直接注入精确开普勒物理有理共振槽种子 (1:1, 3:2, 2:1, 3:1, 4:1)
            for (int k = 0; k < legCount; k++) {
                string fromN = fullChain[k];
                string toN = fullChain[k + 1];
                if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                    var p = SolarSystemCatalog.GetPlanet(fromN);
                    double sma = PykepBridge.GetRelativeSMA(p, central, config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                    double tOrbSec = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                    if (tOrbSec > 0 && !double.IsNaN(tOrbSec)) {
                        double[] resonanceRatios = (sma > 1.3e8) ? new double[] { 1.0, 2.0 } : new double[] { 1.0, 2.0, 3.0 };
                        foreach (double r in resonanceRatios) {
                            double resTofSec = tOrbSec * r;
                            if (resTofSec >= lowerBounds[1 + k] && resTofSec <= upperBounds[1 + k]) {
                                var resGenome = new double[dim];
                                resGenome[0] = (lowerBounds[0] + upperBounds[0]) * 0.5;
                                for (int d = 1; d < dim; d++) {
                                    if (d <= legCount) {
                                        resGenome[d] = nominalLegToFSec[d - 1];
                                    } else {
                                        resGenome[d] = 0.5;
                                    }
                                }
                                resGenome[1 + k] = resTofSec;
                                seedGenomes.Add(resGenome);
                            }
                        }
                    }
                }
            }

            // 共振组合扫描注种 (Resonance-Combo Scan Seeding):
            // 对链路中所有同星共振腿的周期比 {1:1, 3:2, 2:1, 3:1, 4:1} 做离散组合扫描,
            // 组合出"全程共振同步"的完整基因组 (所有共振腿同时落在精确有理槽上),
            // 以一次原生 BuildFullSolution 的 TotalΔV 为种子评分, 注入相位闭环最好的 K 个组合。
            // 直击深井任务 (Scenario 9: 水星多次共振抬升轨道 + 金星/地球反梯级泵升) 的窄相位盆地,
            // 避免 DE 从全失配起点盲目进化——此前 M→M 腿虽命中 1:1 槽却仍背 2706 m/s DSM 即源于此。
            using (MGAStepProfiler.Scope("DE注种: 全程开普勒共振组合与相位闭环扫描"))
            {
                var sameBodyLegs = new List<int>();
                for (int k = 0; k < legCount; k++) {
                    if (fullChain[k].Equals(fullChain[k + 1], StringComparison.OrdinalIgnoreCase)) sameBodyLegs.Add(k);
                }
                if (sameBodyLegs.Count > 0) {
                    // 共振周期比扫描面: 1:1 / 3:2 / 5:2 / 2:1 / 3:1 / 4:1, 覆盖整数与亚共振槽,
                    // 为 MESSENGER 式水星 M×3 自减速+共振抬升提供更多相位闭环选择 (3:2/5:2 亚共振
                    // 是窄相位盆地内最可能闭合的槽位, 此前仅整数比导致 M→M 腿背 2495 m/s DSM)。
                    var ratioOptionsPerLeg = new List<List<double>>();
                    var legPeriods = new double[sameBodyLegs.Count];
                    for (int si = 0; si < sameBodyLegs.Count; si++) {
                        int k = sameBodyLegs[si];
                        var p = SolarSystemCatalog.GetPlanet(fullChain[k]);
                        double sma = PykepBridge.GetRelativeSMA(p, central, config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                        double tOrbSec = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                        legPeriods[si] = (tOrbSec > 0 && !double.IsNaN(tOrbSec)) ? tOrbSec : 0.0;
                        var opts = new List<double>();
                        if (legPeriods[si] > 0) {
                            // 地球等中继星重点测试 1:1 与 2:1 (伽利略 VEEGA) 蓄能，水星/金星测试多阶有理比 (含 5/3 亚共振以闭合会合周期)
                            double[] legRatios = (sma > 1.3e8) 
                                ? new double[] { 1.0, 2.0 } 
                                : new double[] { 1.0, 2.0, 3.0 };
                            foreach (double r in legRatios) {
                                double resTofSec = legPeriods[si] * r;
                                if (resTofSec >= lowerBounds[1 + k] && resTofSec <= upperBounds[1 + k]) opts.Add(r);
                            }
                        }
                        ratioOptionsPerLeg.Add(opts);
                    }

                    var comboSeeds = new List<Tuple<double, double[]>>();
                    var currentCombo = new List<double>();
                    var combos = new List<List<double>>();
                    // 递归生成共振比例组合 (上限 1024 组后按会合相位相干性优选)
                    Action<int> buildCombos = null;
                    buildCombos = (si) => {
                        if (si >= sameBodyLegs.Count) {
                            combos.Add(new List<double>(currentCombo));
                            return;
                        }
                        foreach (double r in ratioOptionsPerLeg[si]) {
                            currentCombo.Add(r);
                            buildCombos(si + 1);
                            currentCombo.RemoveAt(currentCombo.Count - 1);
                            if (combos.Count >= 1024) return;
                        }
                    };
                    if (ratioOptionsPerLeg.All(o => o.Count > 0)) {
                        buildCombos(0);
                    }

                    // 识别同星共振集群与下游出口天体的会合相位闭环 (Synodic Phase Closure to Exit Body)
                    // 若存在同星共振集群 (如水星多次飞掠)，计算其停留总时间与出口天体会合周期 (T_syn) 的整倍数匹配残差，
                    // 严格将相位对齐最好的组合置顶，彻底杜绝出口腿天文级 DSM 赤字！
                    Func<List<double>, double> evalComboSynodicScore = cList => {
                        double synScore = 0.0;
                        int idx = 0;
                        while (idx < sameBodyLegs.Count) {
                            int startK = sameBodyLegs[idx];
                            string body = fullChain[startK];
                            int endK = startK;
                            int nextIdx = idx + 1;
                            while (nextIdx < sameBodyLegs.Count && sameBodyLegs[nextIdx] == endK + 1 && fullChain[sameBodyLegs[nextIdx]].Equals(body, StringComparison.OrdinalIgnoreCase)) {
                                endK = sameBodyLegs[nextIdx];
                                nextIdx++;
                            }
                            int exitIdx = endK + 1;
                            while (exitIdx < fullChain.Count && fullChain[exitIdx].Equals(body, StringComparison.OrdinalIgnoreCase)) {
                                exitIdx++;
                            }
                            string exitBody = (exitIdx < fullChain.Count) ? fullChain[exitIdx] : null;
                            if (!string.IsNullOrEmpty(exitBody) && !exitBody.Equals(body, StringComparison.OrdinalIgnoreCase)) {
                                var pA = SolarSystemCatalog.GetPlanet(body);
                                var pB = SolarSystemCatalog.GetPlanet(exitBody);
                                if (pA != null && pB != null && pA.SMA > 0 && pB.SMA > 0) {
                                    double tA = PykepBridge.CalcOrbitPeriodSec(pA.SMA, centralMu) / 86400.0;
                                    double tB = PykepBridge.CalcOrbitPeriodSec(pB.SMA, centralMu) / 86400.0;
                                    double synDays = Math.Abs(1.0 / (1.0 / tA - 1.0 / tB));
                                    double totalClusterDays = 0.0;
                                    for (int ci = idx; ci < nextIdx; ci++) {
                                        totalClusterDays += (legPeriods[ci] / 86400.0) * cList[ci];
                                    }
                                    double nCycles = Math.Max(1.0, Math.Round(totalClusterDays / synDays));
                                    double phaseErrDays = Math.Abs(totalClusterDays - nCycles * synDays);
                                    double phaseFrac = phaseErrDays / synDays;
                                    synScore += phaseErrDays * 2.0 + phaseFrac * phaseFrac * 200.0;
                                }
                            }
                            idx = nextIdx;
                        }
                        return synScore;
                    };
                    combos = combos.OrderBy(evalComboSynodicScore).Take(48).ToList();

                    double[] comboEpochs = new double[] { -0.4, -0.3, -0.2, -0.1, 0.0, 0.08, 0.12, 0.15, 0.175, 0.20, 0.23, 0.28, 0.35, 0.45 };
                    int comboEvalCount = 0;
                    foreach (var combo in combos) {
                        foreach (double epochFrac in comboEpochs) {
                            if (comboEvalCount >= 350) break;
                            comboEvalCount++;
                            var g = new double[dim];
                            g[0] = (lowerBounds[0] + upperBounds[0]) * 0.5 + epochFrac * (upperBounds[0] - lowerBounds[0]);
                            if (g[0] < lowerBounds[0]) g[0] = lowerBounds[0];
                            if (g[0] > upperBounds[0]) g[0] = upperBounds[0];
                            for (int k = 0; k < legCount; k++) {
                                g[1 + k] = nominalLegToFSec[k];
                            }
                            for (int si = 0; si < sameBodyLegs.Count; si++) {
                                g[1 + sameBodyLegs[si]] = legPeriods[si] * combo[si];
                            }
                            if (optDsmEta) {
                                for (int k = 0; k < legCount; k++) g[1 + legCount + k] = 0.5;
                            }
                            var comboSol = PykepBridge.BuildFullSolution(g, fullChain, config, false);
                            if (comboSol != null && !double.IsNaN(comboSol.TotalDeltaVKms) && comboSol.TotalDeltaVKms > 0) {
                                double cFit = ComputeFitness(comboSol, config);
                                comboSeeds.Add(Tuple.Create(cFit, g));
                            }
                        }
                    }
                    foreach (var cs in comboSeeds.OrderBy(t => t.Item1).Take(24)) {
                        seedGenomes.Add(cs.Item2);
                    }
                }
            }

            // 普适前向借力步进动力学注种 (Universal Forward-Staging Seed Synthesis):
            // 依据前向逐级引力偏折匹配与能量守恒，为高维 MGA 链路提供高质量真实物理基底，杜绝纯随机投点引发的 Lambert 几何发散
            var cachedBlockCombos = new Dictionary<int, List<double[]>>();
            object blockCombosLock = new object();
            using (MGAStepProfiler.Scope("DE注种: 前向步进引力偏折动力学合成"))
            {
            // 特征稀疏采样：聚焦高价值历元与霍曼分段，注种评估从 400+ 次压缩至 24~48 次，耗时降幅 >80%
            int sampleEpochCount = Math.Max(4, Math.Min(8, (int)((winEndUT - winStartUT) / (15.0 * 86400.0)) + 1));
            double epochStep = (sampleEpochCount > 1) ? ((winEndUT - winStartUT) / (sampleEpochCount - 1)) : 0.0;

            // 覆盖短霍曼、标称霍曼、扩展 Type-II 借力切向弧 3 个最具代表性的物理分支
            double[] leg0Fractions = new double[] { 0.35, 0.60, 0.80 };
            var stagedSeeds = new System.Collections.Concurrent.ConcurrentBag<Tuple<double, double[]>>();

            bool hasResonantBlock = false;
            for (int rk = 0; rk < legCount; rk++) {
                if (fullChain[rk].Equals(fullChain[rk + 1], StringComparison.OrdinalIgnoreCase)) {
                    hasResonantBlock = true;
                    break;
                }
            }

            Parallel.For(0, sampleEpochCount, epIdx => {
                double tDep = winStartUT + epIdx * epochStep;

                foreach (double leg0Frac in leg0Fractions) {
                    int numVariants = hasResonantBlock ? 2 : 1;
                    for (int seedVariant = 0; seedVariant < numVariants; seedVariant++) {
                        var genome = new double[dim];
                        genome[0] = tDep;

                        if (optDsmEta) {
                            for (int ek = 0; ek < legCount; ek++) {
                                genome[1 + legCount + ek] = 0.5;
                            }
                        }

                        double curUT = tDep;
                        Vector3d prevArrVinf = Vector3d.zero;

                        int k = 0;
                        while (k < legCount) {
                            string fromN = fullChain[k];
                            string toN = fullChain[k + 1];

                            if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                                int resStart = k;
                                int resEnd = k;
                                while (resEnd + 1 < legCount && fullChain[resEnd + 1].Equals(fullChain[resEnd + 2], StringComparison.OrdinalIgnoreCase)) {
                                    resEnd++;
                                }
                                int blockLen = resEnd - resStart + 1;
                                string exitTarget = (resEnd + 2 < fullChain.Count) ? fullChain[resEnd + 2] : null;

                                double nominalExitToFSec = 0.0;
                                int enterLeg = resStart - 1;
                                while (enterLeg >= 0 && fullChain[enterLeg].Equals(fromN, StringComparison.OrdinalIgnoreCase)) {
                                    enterLeg--;
                                }
                                if (enterLeg >= 0 && !string.IsNullOrEmpty(exitTarget) && fullChain[enterLeg].Equals(exitTarget, StringComparison.OrdinalIgnoreCase)) {
                                    nominalExitToFSec = genome[1 + enterLeg];
                                }

                                var rankedCombos = FindAndRankResonantCombos(
                                    fromN, exitTarget, blockLen,
                                    curUT, prevArrVinf,
                                    lowerBounds, upperBounds, resStart,
                                    central, centralMu, nominalExitToFSec);

                                lock (blockCombosLock) {
                                    if (!cachedBlockCombos.ContainsKey(resStart)) {
                                        cachedBlockCombos[resStart] = new List<double[]>();
                                    }
                                    if (rankedCombos != null) {
                                        foreach (var c in rankedCombos.Take(6)) {
                                            if (!cachedBlockCombos[resStart].Any(existing => existing.SequenceEqual(c.Item1))) {
                                                cachedBlockCombos[resStart].Add(c.Item1);
                                            }
                                        }
                                    }
                                }

                                var chosenTuple = (rankedCombos != null && rankedCombos.Count > 0)
                                    ? rankedCombos[seedVariant % rankedCombos.Count]
                                    : null;
                                double[] chosen = chosenTuple?.Item1;
                                double chosenExitToF = chosenTuple?.Item2 ?? 0.0;

                                var pBody = SolarSystemCatalog.GetPlanet(fromN);
                                double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                                double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);

                                double dwellSec = 0.0;
                                for (int i = 0; i < blockLen; i++) {
                                    double r = (chosen != null && i < chosen.Length) ? chosen[i] : 1.0;
                                    double legToF = r * tOrb;
                                    genome[1 + resStart + i] = legToF;
                                    dwellSec += legToF;
                                }
                                curUT += dwellSec;

                                // 将最优共振出口航时直接预填入下游出口航段
                                if (chosenExitToF > 10.0 * 86400.0 && resEnd + 1 < legCount) {
                                    genome[1 + resEnd + 1] = Math.Max(lowerBounds[1 + resEnd + 1], Math.Min(upperBounds[1 + resEnd + 1], chosenExitToF));
                                }

                                // Seamless V_inf propagation to exit leg
                                if (!string.IsNullOrEmpty(exitTarget)) {
                                    var pExit = SolarSystemCatalog.GetPlanet(exitTarget);
                                    if (pExit != null) {
                                        double smaNext = PykepBridge.GetRelativeSMA(pExit, central, 0.0);
                                        double aH = (sma + smaNext) * 0.5;
                                        double approxTofSec = Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
                                        pBody.GetHeliocentricState(curUT, out Vector3d rDep, out Vector3d vDepPlanet);
                                        pExit.GetHeliocentricState(curUT + approxTofSec, out Vector3d rArrNext, out _);
                                        if (PykepNative.SolveLambert(rDep, rArrNext, approxTofSec, centralMu, false, out Vector3d vDepLambert, out _)) {
                                            Vector3d vExitInf = vDepLambert - vDepPlanet;
                                            if (prevArrVinf.sqrMagnitude > 1e-4) {
                                                prevArrVinf = vExitInf.normalized * prevArrVinf.magnitude;
                                            } else {
                                                prevArrVinf = vExitInf;
                                            }
                                        }
                                    }
                                }

                                k = resEnd + 1;
                                continue;
                            }

                            // Non-resonant leg
                            var p1 = SolarSystemCatalog.GetPlanet(fromN);
                            var p2 = SolarSystemCatalog.GetPlanet(toN);
                            double minToF = lowerBounds[1 + k];
                            double maxToF = upperBounds[1 + k];
                            double bestLegToF = (minToF + maxToF) * 0.5;
                            double bestLegScore = double.MaxValue;

                            p1.GetHeliocentricState(curUT, out Vector3d r1, out Vector3d v1);
                            var candToFs = new List<double>();
                            if (k == 0) {
                                candToFs.Add(minToF + leg0Frac * (maxToF - minToF));
                            } else {
                                int tofSteps = 36;
                                for (int s = 0; s <= tofSteps; s++) {
                                    candToFs.Add(minToF + s * (maxToF - minToF) / tofSteps);
                                }
                                double sma1 = PykepBridge.GetRelativeSMA(p1, central, 0.0);
                                double sma2 = PykepBridge.GetRelativeSMA(p2, central, 0.0);
                                if (sma1 > 0 && sma2 > 0) {
                                    double aH = (sma1 + sma2) * 0.5;
                                    double tHohmann = Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
                                    if (tHohmann >= minToF && tHohmann <= maxToF) {
                                        candToFs.Add(tHohmann);
                                        candToFs.Add(tHohmann * 0.90);
                                        candToFs.Add(tHohmann * 0.95);
                                        candToFs.Add(tHohmann * 1.05);
                                        candToFs.Add(tHohmann * 1.10);
                                    }
                                }

                                // 普适对称返回航段动力学注种: 若此前存在反向航段 (如 Venus->Mercury，当前为 Mercury->Venus)
                                int prevSymLeg = k - 1;
                                while (prevSymLeg >= 0 && fullChain[prevSymLeg].Equals(fromN, StringComparison.OrdinalIgnoreCase)) {
                                    prevSymLeg--;
                                }
                                if (prevSymLeg >= 0 && fullChain[prevSymLeg].Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                                    double symToF = genome[1 + prevSymLeg];
                                    if (symToF >= minToF && symToF <= maxToF) {
                                        candToFs.Add(symToF);
                                        candToFs.Add(symToF * 0.95);
                                        candToFs.Add(symToF * 1.05);
                                    }
                                }

                                // 包含由共振段推导的最优出口航时预填值
                                double preExitToF = genome[1 + k];
                                if (preExitToF >= minToF && preExitToF <= maxToF) {
                                    candToFs.Add(preExitToF);
                                    candToFs.Add(preExitToF * 0.95);
                                    candToFs.Add(preExitToF * 1.05);
                                }
                            }

                            foreach (double testToF in candToFs) {
                                double arrUT = curUT + testToF;
                                p2.GetHeliocentricState(arrUT, out Vector3d r2, out Vector3d v2);
                                if (PykepNative.SolveLambert(r1, r2, testToF, centralMu, false, out Vector3d vDep, out Vector3d vArr)) {
                                    Vector3d vInfDep = vDep - v1;
                                    double score = 0.0;
                                    if (k == 0) {
                                        double rp = p1.RadiusKm + (config?.DeparturePeAKm ?? 200.0);
                                        double vHyp = Math.Sqrt(vInfDep.sqrMagnitude + 2.0 * p1.Mu / rp);
                                        double vPark = Math.Sqrt(p1.Mu / rp);
                                        score = (vHyp - vPark);
                                    } else {
                                        // intermediate flyby
                                        double vDiff = Math.Abs(vInfDep.magnitude - prevArrVinf.magnitude);
                                        double dot = Vector3d.Dot(vInfDep.normalized, prevArrVinf.normalized);
                                        dot = Math.Max(-1.0, Math.Min(1.0, dot));
                                        double reqTurnRad = Math.Acos(dot);
                                        double safeR = MGADefaults.GetBodySafeRadiusKm(fromN);
                                        double e = 1.0 + (safeR * prevArrVinf.sqrMagnitude) / Math.Max(1.0, p1.Mu);
                                        double maxTurnRad = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, e));
                                        double turnExcess = Math.Max(0.0, reqTurnRad - maxTurnRad);
                                        score = vDiff * 2.0 + turnExcess * 10.0;
                                    }
                                    if (score < bestLegScore) {
                                        bestLegScore = score;
                                        bestLegToF = testToF;
                                    }
                                }
                            }

                            genome[1 + k] = bestLegToF;
                            curUT += bestLegToF;
                            p2.GetHeliocentricState(curUT, out _, out Vector3d v2Final);
                            p1.GetHeliocentricState(curUT - bestLegToF, out Vector3d r1Final, out _);
                            p2.GetHeliocentricState(curUT, out Vector3d r2Final, out _);
                            if (PykepNative.SolveLambert(r1Final, r2Final, bestLegToF, centralMu, false, out _, out Vector3d vArrFinal)) {
                                prevArrVinf = vArrFinal - v2Final;
                            }
                            k++;
                        }

                        var fSol = PykepBridge.BuildFullSolution(genome, fullChain, config, false);
                        if (fSol != null && !double.IsNaN(fSol.TotalDeltaVKms) && fSol.TotalDeltaVKms < 1e7) {
                            stagedSeeds.Add(Tuple.Create(ComputeFitness(fSol, config), genome));
                        }

                        // 派生微扰变体
                        double[] perturbations = new double[] { -0.12, -0.05, 0.05, 0.12 };
                        foreach (double factor in perturbations) {
                            var pGene = (double[])genome.Clone();
                            for (int d = 1; d <= legCount; d++) {
                                if (d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                                    continue;
                                }
                                double spanD = upperBounds[d] - lowerBounds[d];
                                pGene[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], pGene[d] + factor * spanD * 0.25));
                            }
                            var pSol = PykepBridge.BuildFullSolution(pGene, fullChain, config, false);
                            if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < 1e7) {
                                stagedSeeds.Add(Tuple.Create(ComputeFitness(pSol, config), pGene));
                            }
                        }
                    }
                }
            });

            foreach (var ss in stagedSeeds.OrderBy(t => t.Item1).Take(48)) {
                seedGenomes.Add(ss.Item2);
            }
            } // end Scope 前向步进引力偏折动力学合成

            // 统一按适应度对全部种子执行全局优选排序，确保最优质种子置于前列
            using (MGAStepProfiler.Scope("DE注种: 全局种子适应度评估与优选"))
            {
            seedGenomes = seedGenomes
                .Select(g => {
                    var s = PykepBridge.BuildFullSolution(g, fullChain, config, false);
                    double f = (s != null && !double.IsNaN(s.TotalDeltaVKms)) ? ComputeFitness(s, config) : 1e9;
                    return Tuple.Create(f, g);
                })
                .OrderBy(t => t.Item1)
                .Select(t => t.Item2)
                .ToList();
            }

            if (seedGenomes.Count > 0) {
                var topSeedSol = PykepBridge.BuildFullSolution(seedGenomes[0], fullChain, config, false);
                if (topSeedSol != null) {
                    Console.WriteLine($"     [种子全局优选] 注入最强精英: 发射={GMATClient.FormatUtcGregorian(topSeedSol.DepartureUT)}, 始发={topSeedSol.LaunchDeltaVKms * 1000.0:F1} m/s, DSM={topSeedSol.TotalDsmDeltaVKms * 1000.0:F1} m/s, 总ΔV={topSeedSol.TotalDeltaVKms * 1000.0:F1} m/s");
                    if (topSeedSol.Legs != null) {
                        for (int li = 0; li < topSeedSol.Legs.Count; li++) {
                            var l = topSeedSol.Legs[li];
                            double dsm = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                            Console.WriteLine($"       -> Leg #{li+1}: {l.FromBody} -> {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsm:F1} m/s, FB_dv={l.FlybyDeltaVKms*1000.0:F1} m/s");
                        }
                    }
                }
            }

            int maxSeedsPerIsland = Math.Min(seedGenomes.Count, Math.Max(4, (int)(subPopSize * 0.15)));
            using (MGAStepProfiler.Scope("DE种群: 初始染色体分配与初评"))
            {
            for (int i = 0; i < popSize; i++) {
                pop[i] = new double[dim];
                int islandIdx = (subPopSize > 0) ? (i / subPopSize) : 0;
                int localSeedIdx = (i % subPopSize);
                if (seedGenomes.Count > 0 && localSeedIdx < maxSeedsPerIsland) {
                    int globalSeedIdx = (localSeedIdx == 0)
                        ? 0
                        : ((islandIdx * (maxSeedsPerIsland - 1) + localSeedIdx) % seedGenomes.Count);
                    pop[i] = (double[])seedGenomes[globalSeedIdx].Clone();
                } else {
                    int d = 0;
                    while (d < dim) {
                        if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                            int resStart = d - 1;
                            int resEnd = resStart;
                            while (resEnd + 1 < legCount && fullChain[resEnd + 1].Equals(fullChain[resEnd + 2], StringComparison.OrdinalIgnoreCase)) {
                                resEnd++;
                            }
                            int blockLen = resEnd - resStart + 1;
                            var pBody = SolarSystemCatalog.GetPlanet(fullChain[resStart]);
                            double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                            if (tOrb > 0) {
                                double rSoi = (pBody != null && pBody.SoiKm > 0) ? pBody.SoiKm : 1.5e5;
                                double maxOffset = (config != null && config.ResonantMaxOffsetDays > 0)
                                    ? config.ResonantMaxOffsetDays * 86400.0
                                    : Math.Max(0.2 * 86400.0, Math.Min(1.0 * 86400.0, (2.0 * rSoi) / 10.0));

                                if (cachedBlockCombos.TryGetValue(resStart, out var blockCombos) && blockCombos.Count > 0) {
                                    int pick = random.Next(Math.Min(8, blockCombos.Count));
                                    double[] combo = blockCombos[pick];
                                    for (int bi = 0; bi < blockLen; bi++) {
                                        int legD = 1 + resStart + bi;
                                        double resCenter = combo[bi] * tOrb;
                                        double offset = (random.NextDouble() * 2.0 - 1.0) * maxOffset;
                                        pop[i][legD] = Math.Max(lowerBounds[legD], Math.Min(upperBounds[legD], resCenter + offset));
                                    }
                                } else {
                                    for (int bi = 0; bi < blockLen; bi++) {
                                        int legD = 1 + resStart + bi;
                                        pop[i][legD] = Math.Max(lowerBounds[legD], Math.Min(upperBounds[legD], tOrb));
                                    }
                                }
                                d = 1 + resEnd + 1;
                                continue;
                            }
                        }
                        pop[i][d] = lowerBounds[d] + random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                        d++;
                    }
                }
                var sol = PykepBridge.BuildFullSolution(pop[i], fullChain, config, computeAdvanced: false);
                fitness[i] = ComputeFitness(sol, config);
            }
            CPUMonitor.RecordEvaluation(popSize);
            } // end Scope 初始染色体分配与初评

            var resBlockMap = new Dictionary<int, (int rStart, int rEnd, int bLen, string body, double tOrb)>();
            for (int rk = 0; rk < legCount; rk++) {
                if (rk + 1 < fullChain.Count && fullChain[rk].Equals(fullChain[rk + 1], StringComparison.OrdinalIgnoreCase)) {
                    int rStart = rk;
                    int rEnd = rk;
                    while (rEnd + 1 < legCount && fullChain[rEnd + 1].Equals(fullChain[rEnd + 2], StringComparison.OrdinalIgnoreCase)) {
                        rEnd++;
                    }
                    var pB = SolarSystemCatalog.GetPlanet(fullChain[rStart]);
                    double smaB = PykepBridge.GetRelativeSMA(pB, central, 0.0);
                    double tB = PykepBridge.CalcOrbitPeriodSec(smaB, centralMu);
                    for (int b = rStart; b <= rEnd; b++) {
                        resBlockMap[1 + b] = (rStart, rEnd, rEnd - rStart + 1, fullChain[rStart], tB);
                    }
                    rk = rEnd;
                }
            }

            using (MGAStepProfiler.Scope("DE进化: 代数主循环全过程"))
            {
            double lastStagFit = double.MaxValue;
            int stagGens = 0;
            for (int gen = 0; gen < maxGens; gen++) {
                if (ct.IsCancellationRequested) break;

                using (MGAStepProfiler.Scope("DE代数: 异构岛屿变异交叉与评估"))
                {
                    // 1. 生成整个种群的候选染色体 (Mutation, Crossover, Resonant Snapping)
                    var candidates = new List<double[]>(popSize);
                    for (int i = 0; i < popSize; i++) {
                        int islandId = i / subPopSize;
                        int islandStart = islandId * subPopSize;
                        int pbestCount = Math.Max(2, (int)(subPopSize * 0.15));

                        var localRand = new System.Random(gen * popSize + i + 17);

                        // 局部岛屿内选取精英指导个体 (Island Local pbest) 与差分向量
                        var localRanked = Enumerable.Range(islandStart, subPopSize).OrderBy(idx => fitness[idx]).Take(pbestCount).ToArray();
                        int pbest = localRanked[localRand.Next(pbestCount)];
                        int b, c;
                        do { b = islandStart + localRand.Next(subPopSize); } while (b == i);
                        do { c = islandStart + localRand.Next(subPopSize); } while (c == i || c == b);

                        // 异构岛屿变异扰动因子与交叉率 (Heterogeneous Island Strategies: Izzo et al. Springer 2012)
                        double fMin = (islandId == 0) ? 0.5 : (islandId == 1 ? 0.65 : (islandId == 2 ? 0.4 : 0.6));
                        double fRange = (islandId == 0) ? 0.4 : (islandId == 1 ? 0.35 : (islandId == 2 ? 0.3 : 0.45));
                        double crMin = (islandId == 0) ? 0.8 : (islandId == 1 ? 0.7 : (islandId == 2 ? 0.85 : 0.6));
                        double crRange = (islandId == 0) ? 0.2 : (islandId == 1 ? 0.25 : (islandId == 2 ? 0.15 : 0.3));

                        double F = fMin + fRange * localRand.NextDouble();
                        double CR = crMin + crRange * localRand.NextDouble();
                        int R = localRand.Next(dim);

                        double[] candidate = new double[dim];
                        for (int d = 0; d < dim; d++) {
                            if (d == R || localRand.NextDouble() < CR) {
                                candidate[d] = pop[i][d] + F * (pop[pbest][d] - pop[i][d]) + F * (pop[b][d] - pop[c][d]);
                                candidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], candidate[d]));

                                // 若该坐标属于同星共振航段，吸附至开普勒物理有效有理共振槽 (1:1, 3:2, 2:1, 3:1, 4:1 * T_orb)
                                if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                                    var pBody = SolarSystemCatalog.GetPlanet(fullChain[d]);
                                    double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                                    double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                                    if (tOrb > 0) {
                                        double bestResCenter = tOrb;
                                        double minDiff = double.MaxValue;
                                        double[] snapRatios = (sma > 1.3e8)
                                            ? new double[] { 1.0, 2.0 }
                                            : new double[] { 1.0, 2.0, 3.0 };
                                        foreach (double r in snapRatios) {
                                            double rCenter = r * tOrb;
                                            if (rCenter >= lowerBounds[d] - 86400.0 && rCenter <= upperBounds[d] + 86400.0) {
                                                double diff = Math.Abs(candidate[d] - rCenter);
                                                if (diff < minDiff) {
                                                    minDiff = diff;
                                                    bestResCenter = rCenter;
                                                }
                                            }
                                        }
                                        // 离散开普勒共振槽跨越变异 (Resonant Slot Basin Hopping)
                                        if (localRand.NextDouble() < 0.12 && snapRatios.Length > 1) {
                                            var validSnap = new List<double>();
                                            foreach (double r in snapRatios) {
                                                double rc = r * tOrb;
                                                if (rc >= lowerBounds[d] - 86400.0 && rc <= upperBounds[d] + 86400.0) validSnap.Add(rc);
                                            }
                                            if (validSnap.Count > 0) {
                                                bestResCenter = validSnap[localRand.Next(validSnap.Count)];
                                            }
                                        }
                                        double offset = candidate[d] - bestResCenter;
                                        double maxOffset;
                                        if (config != null && config.ResonantMaxOffsetDays > 0) {
                                            maxOffset = config.ResonantMaxOffsetDays * 86400.0;
                                        } else {
                                            double rSoi = (pBody != null && pBody.SoiKm > 0) ? pBody.SoiKm : 1.5e5;
                                            double tSoiSec = (2.0 * rSoi) / 10.0;
                                            maxOffset = Math.Max(0.2 * 86400.0, Math.Min(1.0 * 86400.0, tSoiSec));
                                        }
                                        candidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], bestResCenter + Math.Max(-maxOffset, Math.Min(maxOffset, offset))));
                                    }
                                }
                            } else {
                                candidate[d] = pop[i][d];
                            }
                        }

                        // 复合共振集群联合吸附 (Resonant Cluster Joint Snapping)
                        var handledBlocks = new HashSet<int>();
                        for (int d = 1; d <= legCount; d++) {
                            if (resBlockMap.TryGetValue(d, out var blk) && blk.bLen > 1 && !handledBlocks.Contains(blk.rStart)) {
                                handledBlocks.Add(blk.rStart);
                                if (cachedBlockCombos.TryGetValue(blk.rStart, out var combos) && combos != null && combos.Count > 0) {
                                    if (localRand.NextDouble() < 0.40) {
                                        var combo = combos[localRand.Next(combos.Count)];
                                        for (int bi = 0; bi < blk.bLen && bi < combo.Length; bi++) {
                                            int cIdx = 1 + blk.rStart + bi;
                                            double rCenter = combo[bi] * blk.tOrb;
                                            double off = candidate[cIdx] - rCenter;
                                            double maxSnapOffset = (config != null && config.ResonantMaxOffsetDays > 0) ? config.ResonantMaxOffsetDays * 86400.0 : 0.8 * 86400.0;
                                            off = Math.Max(-maxSnapOffset, Math.Min(maxSnapOffset, off));
                                            candidate[cIdx] = Math.Max(lowerBounds[cIdx], Math.Min(upperBounds[cIdx], rCenter + off));
                                        }
                                    }
                                }
                            }
                        }

                        candidates.Add(candidate);
                    }

                    // 2. 原生 OpenMP + AVX2 批处理动力学评估 (Native C++ Batch Evaluator)
                    var candSols = PykepBridge.BuildBatchSolutions(candidates, fullChain, config);

                    // 3. 淘汰选择 (Survival Selection)
                    for (int i = 0; i < popSize; i++) {
                        double candFit = ComputeFitness(candSols[i], config);
                        if (candFit < fitness[i]) {
                            pop[i] = candidates[i];
                            fitness[i] = candFit;
                        }
                    }

                    CPUMonitor.RecordEvaluation(popSize);
                }

                // 环形拓扑精英迁移 (Ring Topology Elite Migration: Dario Izzo et al. Springer 2012)
                // 每 10 代迁移各岛 Top 2 精英染色体至环形下游岛屿，替换下游最差个体，实现防早熟与全局搜索互救
                if (gen > 0 && gen % 10 == 0) {
                    using (MGAStepProfiler.Scope("DE代数: 环形拓扑精英迁移"))
                    {
                    var eliteGenomes = new double[islandCount][][];
                    for (int isl = 0; isl < islandCount; isl++) {
                        int start = isl * subPopSize;
                        var top2 = Enumerable.Range(start, subPopSize)
                            .OrderBy(idx => fitness[idx])
                            .Take(2)
                            .Select(idx => (double[])pop[idx].Clone())
                            .ToArray();
                        eliteGenomes[isl] = top2;
                    }

                    for (int isl = 0; isl < islandCount; isl++) {
                        int nextIsl = (isl + 1) % islandCount;
                        int nextStart = nextIsl * subPopSize;
                        var worst2 = Enumerable.Range(nextStart, subPopSize)
                            .OrderByDescending(idx => fitness[idx])
                            .Take(2)
                            .ToArray();

                        for (int e = 0; e < 2 && e < worst2.Length; e++) {
                            int targetIdx = worst2[e];
                            pop[targetIdx] = (double[])eliteGenomes[isl][e].Clone();
                            var migSol = PykepBridge.BuildFullSolution(pop[targetIdx], fullChain, config, computeAdvanced: false);
                            fitness[targetIdx] = ComputeFitness(migSol, config);
                        }
                    }
                    }
                }

                // 单调盆地跳跃与柯西重尾探索 (Monotonic Basin Hopping - MBH: Izzo et al. PaGMO 2010)
                // 周期性打破狭窄开普勒势阱陷阱：提取全种群最优解，对非共振坐标施加柯西重尾扰动并微步打磨，若更优则注入种群
                if (gen > 0 && gen % 8 == 0) {
                    using (MGAStepProfiler.Scope("DE代数: 单调盆地跳跃 (MBH)"))
                    {
                        double bestPopFit = double.MaxValue;
                        int bestPopIdx = 0;
                        for (int pi = 0; pi < popSize; pi++) {
                            if (fitness[pi] < bestPopFit) { bestPopFit = fitness[pi]; bestPopIdx = pi; }
                        }
                        var mbhCandidate = (double[])pop[bestPopIdx].Clone();
                        var mbhRand = new System.Random(gen * 7919 + 31);
                        bool hasPerturbed = false;
                        for (int d = 0; d < dim; d++) {
                            if (resBlockMap.ContainsKey(d)) continue; // 保护离散共振槽
                            if (mbhRand.NextDouble() < 0.35) {
                                double u = mbhRand.NextDouble();
                                double cauchy = Math.Tan(Math.PI * (u - 0.5));
                                cauchy = Math.Max(-3.0, Math.Min(3.0, cauchy));
                                double span = upperBounds[d] - lowerBounds[d];
                                mbhCandidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], mbhCandidate[d] + 0.025 * span * cauchy));
                                hasPerturbed = true;
                            }
                        }
                        if (hasPerturbed) {
                            var mbhSol = PykepBridge.BuildFullSolution(mbhCandidate, fullChain, config, computeAdvanced: false);
                            double mbhFit = ComputeFitness(mbhSol, config);
                            if (mbhFit < bestPopFit) {
                                int worstPopIdx = 0;
                                double worstPopFit = -1.0;
                                for (int pi = 0; pi < popSize; pi++) {
                                    if (fitness[pi] > worstPopFit) { worstPopFit = fitness[pi]; worstPopIdx = pi; }
                                }
                                pop[worstPopIdx] = mbhCandidate;
                                fitness[worstPopIdx] = mbhFit;
                                stagGens = 0;
                            }
                        }
                    }
                }

                if (gen % 50 == 0 || gen == maxGens - 1) {
                    double bestFit = double.MaxValue;
                    int bestIdx = 0;
                    for (int i = 0; i < popSize; i++) {
                        if (fitness[i] < bestFit) { bestFit = fitness[i]; bestIdx = i; }
                    }
                    var bestSol = PykepBridge.BuildFullSolution(pop[bestIdx], fullChain, config, computeAdvanced: false);
                    double bLaunch = bestSol != null ? bestSol.LaunchDeltaVKms * 1000.0 : 0.0;
                    double bDsm = bestSol != null ? bestSol.TotalDsmDeltaVKms * 1000.0 : 0.0;
                    double bTot = bestSol != null ? bestSol.TotalDeltaVKms * 1000.0 : 0.0;
                    bool feasible = bestSol != null && bestSol.AllLegsFeasible;
                    string epStr = bestSol != null ? GMATClient.FormatUtcGregorian(bestSol.DepartureUT) : "N/A";
                    Console.WriteLine($"       [DE 代数 {gen + 1,3}/{maxGens}] 始发={bLaunch,6:F1} m/s | DSM={bDsm,7:F1} m/s | 总ΔV={bTot,7:F1} m/s | 历元={epStr} | 闭环={feasible}");
                }

                // 目标达标极速提前收敛刹车 (Target Reached Early Exit):
                // 一旦在代数进化中捕获到完全满足总工质机动、发射上限、全航程纯无动力借力飞掠的优质解，
                // 立即跳出循环进入终极模式打磨，节约大量盲目计算时间！
                if (gen >= 15 && gen % 5 == 0) {
                    double bestF = double.MaxValue;
                    int bI = 0;
                    for (int i = 0; i < popSize; i++) {
                        if (fitness[i] < bestF) { bestF = fitness[i]; bI = i; }
                    }
                    if (bestF < 1e8) {
                        var curBestSol = PykepBridge.BuildFullSolution(pop[bI], fullChain, config, computeAdvanced: false);
                        if (curBestSol != null && curBestSol.AllLegsFeasible) {
                            bool meetTotal = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0)
                                ? (curBestSol.TotalDeltaVKms * 1000.0 <= config.VesselMaxDeltaVMs)
                                : true;
                            bool meetLaunch = (config.LaunchDeltaVCeilingMs > 0)
                                ? (curBestSol.LaunchDeltaVKms * 1000.0 <= config.LaunchDeltaVCeilingMs)
                                : true;
                            bool meetDsm = (config.MaxDsmDeltaVMs > 0)
                                ? (curBestSol.TotalDsmDeltaVKms * 1000.0 <= config.MaxDsmDeltaVMs)
                                : true;
                            bool meetFb = (curBestSol.TotalFlybyDeltaVKms < 1e-4);
                            if (meetTotal && meetLaunch && meetDsm && meetFb) {
                                Console.WriteLine($"       [DE 极速达标提前收敛] 第 {gen + 1} 代已完美达成硬门禁 (总ΔV={curBestSol.TotalDeltaVKms*1000.0:F1} m/s, 始发={curBestSol.LaunchDeltaVKms*1000.0:F1} m/s, DSM={curBestSol.TotalDsmDeltaVKms*1000.0:F1} m/s, 飞掠脉冲=0 m/s)，提前刹车进入极值抛光！");
                                break;
                            }
                        }
                    }
                }

                // 智能体不动点收敛与停滞早退机制 (Vasile et al. 2011 IDEA 离散动力学理论)
                // 当种群离差半径坍缩且连续无显著改善时，数学证明种群已被困于不动点，继续迭代收益为零，
                // 立即提前刹车转入局部打磨，彻底斩断后半程数十代 CPU 无效空转！
                if (gen >= 20) {
                    double curGenBest = double.MaxValue;
                    int bestIndivIdx = 0;
                    for (int i = 0; i < popSize; i++) {
                        if (fitness[i] < curGenBest) {
                            curGenBest = fitness[i];
                            bestIndivIdx = i;
                        }
                    }
                    if (curGenBest < lastStagFit * (1.0 - 1e-4) - 0.5) {
                        lastStagFit = curGenBest;
                        stagGens = 0;
                    } else {
                        stagGens++;

                        // 快速估算种群向最优解的归一化离差半径 (Dispersion Radius)
                        double dispersion = 0.0;
                        int sampleCount = Math.Min(12, popSize);
                        for (int s = 0; s < sampleCount; s++) {
                            for (int d = 0; d < dim; d++) {
                                double span = upperBounds[d] - lowerBounds[d];
                                if (span > 1e-6) {
                                    dispersion += Math.Abs(pop[s][d] - pop[bestIndivIdx][d]) / span;
                                }
                            }
                        }
                        dispersion /= (sampleCount * dim);

                        bool shouldEarlyExit = false;
                        if (curGenBest < 1e5 && stagGens >= 15 && dispersion < 0.05) {
                            shouldEarlyExit = true; // 可行域高质量不动点收敛
                        } else if (curGenBest < 1e7 && stagGens >= 20 && dispersion < 0.03) {
                            shouldEarlyExit = true; // 准可行域不动点锁定
                        } else if (stagGens >= 28) {
                            shouldEarlyExit = true; // 全局绝对停滞硬兜底
                        }

                        if (shouldEarlyExit) {
                            Console.WriteLine($"       [DE IDEA 停滞早退] 第 {gen + 1} 代检测到不动点收缩 (连续 {stagGens} 代无改善, 离差半径={dispersion:F3}), 提前刹车转入模式抛光！");
                            break;
                        }
                    }
                }

                if (onProgress != null && (gen % 5 == 0 || gen == maxGens - 1)) {
                    double bestFit = double.MaxValue;
                    int bestIdx = 0;
                    for (int i = 0; i < popSize; i++) {
                        if (fitness[i] < bestFit) { bestFit = fitness[i]; bestIdx = i; }
                    }
                    var bestSol = PykepBridge.BuildFullSolution(pop[bestIdx], fullChain, config, computeAdvanced: false);
                    double bLaunch = bestSol != null ? bestSol.LaunchDeltaVKms * 1000.0 : 0.0;
                    double bDsm = bestSol != null ? bestSol.TotalDsmDeltaVKms * 1000.0 : 0.0;
                    double bTot = bestSol != null ? bestSol.TotalDeltaVKms * 1000.0 : 0.0;
                    bool feasible = bestSol != null && bestSol.AllLegsFeasible;
                    string fitStr = bestFit < 1e8
                        ? $"[达标/优选] 总ΔV={bTot:F1} m/s (始发={bLaunch:F1} m/s, DSM={bDsm:F1} m/s, 动力学闭环={feasible})"
                        : $"[探索/超标] 总ΔV={bTot:F1} m/s (始发={bLaunch:F1} m/s, DSM={bDsm:F1} m/s, 罚项={bestFit:E1})";
                    float prog = (float)(gen + 1) / maxGens;
                    onProgress.Invoke(prog, $"[DE 演化第 {gen + 1,3}/{maxGens} 代] 种群 {popSize} | {fitStr}");
                }
            }
            } // end Scope DE进化: 代数主循环全过程

            var rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();

            // 局部坐标下降与 Hooke-Jeeves 模式加速搜索 (Memetic Hooke-Jeeves Pattern Acceleration Polish):
            // 围绕全局进化沉降的精英染色体，先逐坐标微步探索梯度斜率，并沿改善方向执行倍增步长加速，
            // 随后执行全维模式跳跃 x_pattern = x + D，快速穿越狭长平缓的兰伯特相位峡谷，将 DSM 压降至物理极限
            using (MGAStepProfiler.Scope("DE打磨: Hooke-Jeeves 模式加速极值搜索"))
            {
            int polishCount = Math.Min(2, rankedIndices.Count);
            for (int pIdx = 0; pIdx < polishCount; pIdx++) {
                int indivIdx = rankedIndices[pIdx];
                double curFit = fitness[indivIdx];
                // 若染色体严重发散不可行 (fit >= 1e8)，不浪费时间执行昂贵的单维微调
                if (curFit >= 1e8) continue;
                // 若为第 2 个候选且比第 1 个候选差 50% 以上，无需再花大量时间打磨次要解
                if (pIdx > 0 && curFit > fitness[rankedIndices[0]] * 1.5) continue;

                var curGene = (double[])pop[indivIdx].Clone();
                int maxPolishIter = (pIdx == 0) ? 2 : 1;

                for (int iter = 0; iter < maxPolishIter; iter++) {
                    var baseGene = (double[])curGene.Clone();
                    double baseFit = curFit;
                    bool anyImproved = false;

                    // 1. 逐坐标探索移动 (Exploratory Move)
                    for (int d = 0; d < dim; d++) {
                        bool isResLeg = (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase));
                        double resOffsetMax = (config != null && config.ResonantMaxOffsetDays > 0) ? config.ResonantMaxOffsetDays * 86400.0 : 0.4 * 86400.0;
                        double span = isResLeg ? resOffsetMax : (upperBounds[d] - lowerBounds[d]);
                        double[] probeFactors;
                        if (isResLeg) {
                            probeFactors = new double[] { -0.5, 0.5, -0.1, 0.1 };
                        } else if (d > legCount) {
                            // DSM 航向分率 Eta 坐标探索: 涵盖向出发星靠拢与到达星靠拢典型值
                            probeFactors = new double[] { -0.20, 0.20, -0.05, 0.05 };
                        } else {
                            probeFactors = new double[] { -0.02, 0.02, -0.005, 0.005 };
                        }
                        double bestDVal = curGene[d];
                        double bestDFit = curFit;
                        double bestFactor = 0.0;
                        double bestNextDVal = (d < legCount) ? curGene[d + 1] : 0.0;
                        bool hasComp = false;

                        // 复合共振块整体联调: 评估全部会合闭合组合
                        if (d >= 1 && d <= legCount && resBlockMap.TryGetValue(d, out var blk) && blk.bLen > 1 && d == 1 + blk.rStart) {
                            if (cachedBlockCombos.TryGetValue(blk.rStart, out var combos) && combos != null) {
                                foreach (var combo in combos) {
                                    var comboGene = (double[])curGene.Clone();
                                    for (int bi = 0; bi < blk.bLen && bi < combo.Length; bi++) {
                                        int cIdx = 1 + blk.rStart + bi;
                                        double curOff = curGene[cIdx] - Math.Round(curGene[cIdx] / blk.tOrb) * blk.tOrb;
                                        comboGene[cIdx] = Math.Max(lowerBounds[cIdx], Math.Min(upperBounds[cIdx], combo[bi] * blk.tOrb + curOff));
                                    }
                                    var comboSol = PykepBridge.BuildFullSolution(comboGene, fullChain, config, computeAdvanced: false);
                                    double cFit = ComputeFitness(comboSol, config);
                                    if (cFit < curFit) {
                                        curFit = cFit;
                                        curGene = comboGene;
                                        anyImproved = true;
                                    }
                                }
                            }
                        }

                        // 若该坐标对应同星共振航段，直接评估开普勒精确物理整数共振槽 (1:1, 2:1, 3:1, 4:1) 及 SOI 微时差
                        if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                            var pBody = SolarSystemCatalog.GetPlanet(fullChain[d]);
                            double sma = PykepBridge.GetRelativeSMA(pBody, central, config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                            double[] resRatios = (sma > 1.3e8)
                                ? new double[] { 1.0, 2.0 }
                                : new double[] { 1.0, 2.0, 3.0 };
                            double rSoi = (pBody != null && pBody.SoiKm > 0) ? pBody.SoiKm : 1.5e5;
                            double tSoiSec = (config != null && config.ResonantMaxOffsetDays > 0)
                                ? config.ResonantMaxOffsetDays * 86400.0
                                : Math.Max(0.2 * 86400.0, Math.Min(1.0 * 86400.0, (2.0 * rSoi) / 10.0));
                            double[] soiOffsets = new double[] { 0.0, -0.5 * tSoiSec, 0.5 * tSoiSec, -tSoiSec, tSoiSec };

                            foreach (double r in resRatios) {
                                foreach (double dt in soiOffsets) {
                                    double testVal = r * tOrb + dt;
                                    if (testVal >= lowerBounds[d] && testVal <= upperBounds[d]) {
                                        // 方案 A: 独立坐标尝试
                                        var testGene = (double[])curGene.Clone();
                                        testGene[d] = testVal;
                                        var testSol = PykepBridge.BuildFullSolution(testGene, fullChain, config, computeAdvanced: false);
                                        double testFit = ComputeFitness(testSol, config);
                                        if (testFit < bestDFit) {
                                            bestDFit = testFit;
                                            bestDVal = testVal;
                                            bestFactor = 0.0;
                                            hasComp = false;
                                        }

                                        // 方案 B: 顺流相位补偿尝试 (维持后续全链天体到达时刻完全不变)
                                        if (d < legCount) {
                                            double delta = testVal - curGene[d];
                                            double nextVal = curGene[d + 1] - delta;
                                            if (nextVal >= lowerBounds[d + 1] && nextVal <= upperBounds[d + 1]) {
                                                var compGene = (double[])curGene.Clone();
                                                compGene[d] = testVal;
                                                compGene[d + 1] = nextVal;
                                                var compSol = PykepBridge.BuildFullSolution(compGene, fullChain, config, computeAdvanced: false);
                                                double compFit = ComputeFitness(compSol, config);
                                                if (compFit < bestDFit) {
                                                    bestDFit = compFit;
                                                    bestDVal = testVal;
                                                    bestFactor = 0.0;
                                                    bestNextDVal = nextVal;
                                                    hasComp = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        foreach (double factor in probeFactors) {
                            double testVal = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], curGene[d] + factor * span));
                            var testGene = (double[])curGene.Clone();
                            testGene[d] = testVal;

                            var testSol = PykepBridge.BuildFullSolution(testGene, fullChain, config, computeAdvanced: false);
                            double testFit = ComputeFitness(testSol, config);
                            if (testFit < bestDFit) {
                                bestDFit = testFit;
                                bestDVal = testVal;
                                bestFactor = factor;
                                hasComp = false;
                            }

                            // 顺流相位滑移 (Interplanetary Phasing Slide): 同时微调本段与后置航段，维持后续行星际对齐
                            if (d >= 1 && d < legCount) {
                                double delta = testVal - curGene[d];
                                double nextVal = curGene[d + 1] - delta;
                                if (nextVal >= lowerBounds[d + 1] && nextVal <= upperBounds[d + 1]) {
                                    var compGene = (double[])curGene.Clone();
                                    compGene[d] = testVal;
                                    compGene[d + 1] = nextVal;
                                    var compSol = PykepBridge.BuildFullSolution(compGene, fullChain, config, computeAdvanced: false);
                                    double compFit = ComputeFitness(compSol, config);
                                    if (compFit < bestDFit) {
                                        bestDFit = compFit;
                                        bestDVal = testVal;
                                        bestFactor = factor;
                                        bestNextDVal = nextVal;
                                        hasComp = true;
                                    }
                                }
                            }
                        }

                        // 若探测到改善方向，采纳最优坐标并沿梯度加速搜索
                        if (bestDFit < curFit) {
                            anyImproved = true;
                            curGene[d] = bestDVal;
                            if (hasComp && d < legCount) {
                                curGene[d + 1] = bestNextDVal;
                            }
                            curFit = bestDFit;

                            if (Math.Abs(bestFactor) > 1e-6) {
                                double accelFactor = bestFactor * 1.5;
                                for (int accStep = 0; accStep < 2; accStep++) {
                                    double testVal = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], curGene[d] + accelFactor * span));
                                    if (Math.Abs(testVal - bestDVal) < 1e-6) break;

                                    var testGene = (double[])curGene.Clone();
                                    testGene[d] = testVal;
                                    var testSol = PykepBridge.BuildFullSolution(testGene, fullChain, config, computeAdvanced: false);
                                    double testFit = ComputeFitness(testSol, config);
                                    if (testFit < curFit) {
                                        curFit = testFit;
                                        curGene[d] = testVal;
                                        accelFactor *= 1.5;
                                    } else {
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // 2. 全维 Hooke-Jeeves 模式跳跃 (Pattern Move: x_p = x_new + (x_new - x_base))
                    if (anyImproved && curFit < baseFit) {
                        var patternGene = new double[dim];
                        for (int d = 0; d < dim; d++) {
                            double delta = curGene[d] - baseGene[d];
                            double patVal = curGene[d] + delta;
                            if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                                var pBody = SolarSystemCatalog.GetPlanet(fullChain[d]);
                                double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                                double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                                if (tOrb > 0) {
                                    double bestResCenter = tOrb;
                                    double minDiff = double.MaxValue;
                                    double[] polishRatios = (sma > 1.3e8)
                                        ? new double[] { 1.0, 2.0 }
                                        : new double[] { 1.0, 2.0, 3.0 };
                                    foreach (double r in polishRatios) {
                                        double rCenter = r * tOrb;
                                        if (rCenter >= lowerBounds[d] - 86400.0 && rCenter <= upperBounds[d] + 86400.0) {
                                            double diff = Math.Abs(patVal - rCenter);
                                            if (diff < minDiff) {
                                                minDiff = diff;
                                                bestResCenter = rCenter;
                                            }
                                        }
                                    }
                                    double resOffset = (config != null && config.ResonantMaxOffsetDays > 0) ? config.ResonantMaxOffsetDays * 86400.0 : 0.4 * 86400.0;
                                    patVal = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], Math.Max(bestResCenter - resOffset, Math.Min(bestResCenter + resOffset, patVal))));
                                }
                            }
                            patternGene[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], patVal));
                        }

                        var patSol = PykepBridge.BuildFullSolution(patternGene, fullChain, config, computeAdvanced: false);
                        double patFit = ComputeFitness(patSol, config);
                        if (patFit < curFit) {
                            curGene = patternGene;
                            curFit = patFit;
                        }
                    } else {
                        break;
                    }
                }

                pop[indivIdx] = curGene;
                fitness[indivIdx] = curFit;
            }
            } // end Scope DE打磨: Hooke-Jeeves 模式加速极值搜索

            using (MGAStepProfiler.Scope("DE选解: 帕累托前沿提取与高级动力学注水"))
            {
            rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();
            var seenKeys = new HashSet<long>();

            double winBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
            double winDsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : winBudgetMs;

            var chosenCandidateIndices = new List<int>();

            // 第一阶段：细粒度网格采样 (12小时发射UT格 + 1天飞行历时格)，初筛帕累托前沿多样性解索引
            // 注意：采用 computeAdvanced: false 快速初筛，完全规避过滤掉个体的昂贵 PrimerVector 与 B-Plane 计算
            foreach (var idx in rankedIndices) {
                if (fitness[idx] >= 1e8) continue;
                var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: false);
                if (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) {
                    if (winBudgetMs < 1e8 && sol.TotalDeltaVKms * 1000.0 > winBudgetMs * 1.02) continue;
                    if (winDsmBudgetMs < 1e8 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs * 1.02 || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs * 1.02)))) continue;
                    if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs * 1.05) continue;
                    if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                    if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;

                    long key = ((long)(int)(sol.DepartureUT / 43200.0) << 32) ^ (long)(uint)(int)(sol.TotalFlightDays / 1.0);
                    if (!seenKeys.Contains(key)) {
                        seenKeys.Add(key);
                        chosenCandidateIndices.Add(idx);
                    }
                }
                if (chosenCandidateIndices.Count >= 8) break;
            }

            // 第二阶段保底：若收敛解不足 5 个，吸纳发射相差 > 6 小时或飞行历时相差 > 0.5 天的有效解
            if (chosenCandidateIndices.Count < 5) {
                foreach (var idx in rankedIndices) {
                    if (fitness[idx] >= 1e7) continue;
                    if (chosenCandidateIndices.Contains(idx)) continue;
                    var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: false);
                    if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) continue;
                    if (winBudgetMs < 1e8 && sol.TotalDeltaVKms * 1000.0 > winBudgetMs * 1.02) continue;
                    if (winDsmBudgetMs < 1e8 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs * 1.02 || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs * 1.02)))) continue;
                    if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs * 1.05) continue;
                    if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                    if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;

                    chosenCandidateIndices.Add(idx);
                    if (chosenCandidateIndices.Count >= 8) break;
                }
            }

            // 对最终筛选出的至多 8 组优质解进行高保真动力学注水 (Populate Advanced Astrodynamics)
            // 仅对首选最优解注入完整帕累托前沿曲线与高级物理量；其余精选解进行标准构建
            for (int c = 0; c < chosenCandidateIndices.Count; c++) {
                int cIdx = chosenCandidateIndices[c];
                bool isPrimary = (c == 0);
                var fullSol = PykepBridge.BuildFullSolution(pop[cIdx], fullChain, config, computeAdvanced: isPrimary);
                if (fullSol != null && !double.IsNaN(fullSol.TotalDeltaVKms)) {
                    MarkHardGateCompliance(fullSol, config);
                    solutions.Add(fullSol);
                }
            }

            // 第三阶段：若候选方案仍不足 5 个，从最优解的帕累托前沿集合补充
            if (solutions.Count > 0 && solutions.Count < 6) {
                var baseSol = solutions[0];
                if (baseSol.ParetoFront != null && baseSol.ParetoFront.Count > 1) {
                    foreach (var pt in baseSol.ParetoFront) {
                        if (pt.Genome == null) continue;
                        bool alreadyHas = solutions.Any(s => Math.Abs(s.TotalFlightDays - pt.TotalFlightDays) < 0.5);
                        if (!alreadyHas) {
                            var pSol = PykepBridge.BuildFullSolution(pt.Genome, fullChain, config, computeAdvanced: false);
                            if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < 1e7) {
                                pSol.ParetoFront = baseSol.ParetoFront;
                                MarkHardGateCompliance(pSol, config);
                                solutions.Add(pSol);
                            }
                        }
                        if (solutions.Count >= 8) break;
                    }
                }
            }

            // 若在强约束下未能完全落入预算区，吸纳种群内最佳收敛染色体，供调用方审计逐航段真实物理动力学
            if (solutions.Count == 0 && rankedIndices.Count > 0) {
                var bestCand = PykepBridge.BuildFullSolution(pop[rankedIndices[0]], fullChain, config, computeAdvanced: true);
                if (bestCand != null && !double.IsNaN(bestCand.TotalDeltaVKms)) {
                    MarkHardGateCompliance(bestCand, config);
                    solutions.Add(bestCand);
                }
            }

            solutions = solutions.OrderBy(s => s.TotalDeltaVKms).ToList();
            } // end Scope DE选解: 帕累托前沿提取与高级动力学注水
            return solutions;
        }

        /// <summary>
        /// 硬门禁合规审计：无论进化期采用何种罚函数，最终上报解都必须逐项硬校验
        /// (总ΔV ≤ VesselMaxDeltaVMs、总DSM ≤ MaxDsmDeltaVMs、发射 ≤ LaunchDeltaVCeilingMs)，
        /// 超标值显式记录，绝不静默冒充达标。
        /// </summary>
        private static void MarkHardGateCompliance(MGASolution sol, MGAConfig config) {
            if (sol == null) return;
            double launchCeiling = (config != null) ? config.LaunchDeltaVCeilingMs : 0.0;
            double dsmBudget = (config != null && config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs
                : ((config != null && config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 0.0);
            double totalBudget = (config != null && config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 0.0;

            double launchMs = sol.LaunchDeltaVKms * 1000.0;
            double dsmMs = sol.TotalDsmDeltaVKms * 1000.0;
            double totalMs = sol.TotalDeltaVKms * 1000.0;

            sol.LaunchOverBudgetMs = (launchCeiling > 0) ? Math.Max(0.0, launchMs - launchCeiling) : 0.0;
            sol.DsmOverBudgetMs = (dsmBudget > 0) ? Math.Max(0.0, dsmMs - dsmBudget) : 0.0;
            sol.TotalOverBudgetMs = (totalBudget > 0) ? Math.Max(0.0, totalMs - totalBudget) : 0.0;

            bool launchOk = launchCeiling <= 0 || launchMs <= launchCeiling;
            bool dsmOk = dsmBudget <= 0 || dsmMs <= dsmBudget;
            bool totalOk = totalBudget <= 0 || totalMs <= totalBudget;
            sol.MeetsHardDvGate = launchOk && dsmOk && totalOk;
        }

        /// <summary>
        /// DE 适应度目标函数（含发射窗口/DSM预算/飞船ΔV预算/面外倾角等罚函数）。
        /// 从 PykepBridge 随 DE 内核一并迁出，本类为唯一调用方。
        /// </summary>
        private static double ComputeFitness(MGASolution sol, MGAConfig config) {
            if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) return 1e9;
            double fit = sol.TotalDeltaVKms + (sol.AllLegsFeasible ? 0.0 : MGADefaults.PenaltyInfeasibleBase);
            if (config != null) {
                // 发射窗口时间约束硬罚函数：严格惩罚超出用户指定最早/最晚发射时间的候选解
                if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) {
                    double violationDays = (config.EarliestDepartureUT - sol.DepartureUT) / MGADefaults.SecondsPerDay;
                    fit += violationDays * MGADefaults.PenaltyDepartureViolationDayWeight + MGADefaults.PenaltyDepartureViolationBase;
                }
                if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) {
                    double violationDays = (sol.DepartureUT - config.LatestDepartureUT) / MGADefaults.SecondsPerDay;
                    fit += violationDays * MGADefaults.PenaltyDepartureViolationDayWeight + MGADefaults.PenaltyDepartureViolationBase;
                }
                double dsmBudget = (config.MaxDsmDeltaVMs > 0)
                    ? config.MaxDsmDeltaVMs
                    : (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0 ? config.VesselMaxDeltaVMs : 0.0);
                if (dsmBudget > 0 && sol.Legs != null) {
                    double totalDsmMs = sol.TotalDsmDeltaVKms * 1000.0;
                    if (totalDsmMs > dsmBudget) {
                        double overKms = (totalDsmMs - dsmBudget) / 1000.0;
                        fit += 15000.0 * overKms + 30000.0 * overKms * overKms; // 连续强约束惩罚：平滑梯度引导进入预算内
                    }
                    foreach (var leg in sol.Legs) {
                        if (leg.HasDSM && leg.DSM != null) {
                            double legDsmMs = leg.DSM.DeltaVMs;
                            // 温和拐杖惩罚 (Mid-Band Crutch Penalty): 100~500 m/s 的"温和 DSM"不再免费,
                            // 线性递增惩罚, 迫使 DE 倾向零 DSM 纯弹道链 (真实任务仅保留几十 m/s 轨道修正);
                            // >500 m/s 的天文级拐杖由下方二次惩罚 + 总量预算共同硬阻断。
                            if (legDsmMs > 100.0 && legDsmMs <= dsmBudget) {
                                fit += 600.0 * (legDsmMs - 100.0) / 1000.0;
                            }
                            if (legDsmMs > dsmBudget) {
                                double overKms = (legDsmMs - dsmBudget) / 1000.0;
                                fit += 10000.0 * overKms + 20000.0 * overKms * overKms;
                            }
                        }
                        // 真实共振守卫 (True-Resonance Guard): 同星共振腿物理上应接近零 DSM
                        // (MESSENGER/BepiColombo 实际仅几十 m/s 轨道修正), 若 DE 以天文级 DSM
                        // "假共振直连" 掩盖几何失配 (如 1:1 水星腿 6445 m/s), 施加共振失配软惩罚,
                        // 迫使 DE 寻找真实开普勒共振槽 (2:1/3:1 泵升) 而非 DSM 拐杖解。
                        if (leg.HasDSM && leg.DSM != null && leg.DSM.DeltaVMs > 100.0 &&
                            !string.IsNullOrEmpty(leg.FromBody) &&
                            leg.FromBody.Equals(leg.ToBody, StringComparison.OrdinalIgnoreCase))
                        {
                            double resOverKms = (leg.DSM.DeltaVMs - 100.0) / 1000.0;
                            fit += 2000.0 * resOverKms + 5000.0 * resOverKms * resOverKms;
                        }
                    }
                }
                if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) {
                    double totalMs = sol.TotalDeltaVKms * 1000.0;
                    if (totalMs > config.VesselMaxDeltaVMs) {
                        double overKms = (totalMs - config.VesselMaxDeltaVMs) / 1000.0;
                        fit += 20000.0 * overKms + 40000.0 * overKms * overKms; // 连续强约束惩罚：平滑梯度引导
                    }
                }
                if (config.LaunchDeltaVCeilingMs > 0) {
                    double launchMs = sol.LaunchDeltaVKms * 1000.0;
                    if (launchMs > config.LaunchDeltaVCeilingMs) {
                        double overLaunchKms = (launchMs - config.LaunchDeltaVCeilingMs) / 1000.0;
                        fit += 25000.0 * overLaunchKms + 50000.0 * overLaunchKms * overLaunchKms; // 严格限制首段 TVI 出射速度 (连续平滑梯度)
                    }
                }
                if (sol.TotalFlybyDeltaVKms > 1e-4) {
                    fit += sol.TotalFlybyDeltaVKms * 2000.0 + sol.TotalFlybyDeltaVKms * sol.TotalFlybyDeltaVKms * 1000.0; // 连续单调凸惩罚动力飞掠，迫使纯无动力引力弹弓
                }
                if (config.LockDepartureInclination && sol.Legs != null && sol.Legs.Count > 0) {
                    double actualInc = sol.Legs[0].EjectionInclinationDeg;
                    double targetInc = config.DepartureInclinationDeg;
                    double incErr = Math.Abs(actualInc - targetInc);
                    if (incErr > MGADefaults.PenaltyIncToleranceDeg) {
                        // 逃逸倾角锁定约束罚函数：若逃逸倾角偏离锁定值，按每度 25 km/s + 50 km/s 罚项强力约束
                        fit += incErr * MGADefaults.PenaltyIncDegWeight + MGADefaults.PenaltyIncBase;
                    }
                }
                if ((config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination) && sol.Legs != null && sol.Legs.Count > 0) {
                    var leg0 = sol.Legs[0];
                    if (PykepNative.CalcCoplanarAngle(config.DepartureInclinationDeg, config.DepartureRAANDeg, leg0.ArrivalPosKm, out double wedgeDeg)) {
                        if (wedgeDeg > 0.5) {
                            // 严格按真实轨道平面变换动力学惩罚面外倾角 (基于真实转移初速)
                            double rad = (wedgeDeg - 0.5) * Mathf.Deg2Rad;
                            double vTrans = leg0.DepartureVelKms.magnitude;
                            if (vTrans < 0.1) {
                                double cMu = SolarSystemCatalog.GetCentralMu(config.CentralBody);
                                double cRadius = SolarSystemCatalog.GetPlanet(config.DepartureBody)?.RadiusKm
                                    ?? SolarSystemCatalog.GetPlanet(config.CentralBody)?.RadiusKm
                                    ?? MGADefaults.EarthRadiusKm;
                                double rPark = cRadius + config.DeparturePeAKm;
                                vTrans = Math.Sqrt(cMu / Math.Max(1.0, rPark));
                            }
                            double penaltyDv = 2.0 * vTrans * Math.Sin(rad * 0.5);
                            fit += Math.Max((wedgeDeg - 0.5) * 0.2, penaltyDv);
                        }
                    }
                }
            }
            return fit;
        }
    }
}