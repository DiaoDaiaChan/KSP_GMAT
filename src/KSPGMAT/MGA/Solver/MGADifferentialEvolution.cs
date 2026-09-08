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
        /// 在 [winStartUT, winEndUT] 单一会合窗口内执行差分进化寻优。
        /// </summary>
        public static List<MGASolution> RunWindow(
            MGAConfig config, double winStartUT, double winEndUT,
            Action<float, string> onProgress, CancellationToken ct,
            bool parallelizeInternal = true)
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
                } else {
                    double approxHohmannDays = MGADefaults.HohmannDefaultToFDays;
                    if (PykepBridge.CalcHohmannTransfer(sma1, sma2, centralMu, out _, out double hohmannSec, out _, out _)) {
                        approxHohmannDays = hohmannSec / MGADefaults.SecondsPerDay;
                    }
                    if (approxHohmannDays <= 0 || double.IsNaN(approxHohmannDays)) approxHohmannDays = MGADefaults.HohmannDefaultToFDays;
                    minD = Math.Max(0.5, approxHohmannDays * MGADefaults.HohmannMinToFFactor);
                    maxD = Math.Max(minD + 2.0, approxHohmannDays * MGADefaults.HohmannMaxToFFactor);
                }

                if (config.MinToFDays != null && k < config.MinToFDays.Count && config.MinToFDays[k] > 0) minD = config.MinToFDays[k];
                if (config.MaxToFDays != null && k < config.MaxToFDays.Count && config.MaxToFDays[k] > 0) maxD = config.MaxToFDays[k];

                lowerBounds[1 + k] = minD * MGADefaults.SecondsPerDay;
                upperBounds[1 + k] = maxD * MGADefaults.SecondsPerDay;
            }

            if (optDsmEta) {
                for (int k = 0; k < legCount; k++) {
                    int d = 1 + legCount + k;
                    lowerBounds[d] = 0.1;
                    upperBounds[d] = 0.9;
                }
            }

            int islandCount = 4;
            int subPopSize = Math.Max(80, config != null && config.PopulationSize > 0 ? config.PopulationSize : 80);
            int popSize = islandCount * subPopSize;
            int maxGens = Math.Max(Math.Max(MGADefaults.MaxGenerations, config != null && config.MaxGenerations > 0 ? config.MaxGenerations : 150), dim * 12);

            double[][] pop = new double[popSize][];
            double[] fitness = new double[popSize];
            var random = new System.Random(42);

            // 启发式注种：若处于模式 2 (锁定倾角与 RAAN) 且为中心天体转移 (如 Earth -> Moon)
            // 预先沿锁定停泊轨道面扫描目标天体零点面外偏角交点，将精准共面发射时刻注种至初始种群
            var seedGenomes = new List<double[]>();
            if (config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination)
                && fullChain.Count == 2 && fullChain[0].Equals(central, StringComparison.OrdinalIgnoreCase))
            {
                double nomTofSec = (lowerBounds[1] + upperBounds[1]) * 0.5;
                double scanStartUT = winStartUT;
                double scanSpanSec = Math.Max(30.0 * 86400.0, winEndUT - winStartUT);

                if (PykepNative.FindNodalCrossing(fullChain[1], central, scanStartUT, scanSpanSec, config.DepartureInclinationDeg, config.DepartureRAANDeg, out double tNode, out _)) {
                    double depSeedUT = tNode - nomTofSec;
                    if (depSeedUT < lowerBounds[0]) {
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
                                    g[d] = (lowerBounds[d] + upperBounds[d]) * 0.5;
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
                        double[] resonanceRatios = new double[] { 1.0, 1.25, 1.333, 1.5, 2.0, 2.5, 3.0, 4.0 };
                        foreach (double r in resonanceRatios) {
                            double resTofSec = tOrbSec * r;
                            if (resTofSec >= lowerBounds[1 + k] && resTofSec <= upperBounds[1 + k]) {
                                var resGenome = new double[dim];
                                resGenome[0] = (lowerBounds[0] + upperBounds[0]) * 0.5;
                                for (int d = 1; d < dim; d++) {
                                    resGenome[d] = (lowerBounds[d] + upperBounds[d]) * 0.5;
                                }
                                resGenome[1 + k] = resTofSec;
                                seedGenomes.Add(resGenome);
                            }
                        }
                    }
                }
            }

            // 普适前向借力步进动力学注种 (Universal Forward-Staging Seed Synthesis):
            // 依据前向逐级引力偏折匹配与能量守恒，为高维 MGA 链路提供高质量真实物理基底，杜绝纯随机投点引发的 Lambert 几何发散
            int sampleEpochCount = Math.Max(6, Math.Min(12, (int)((winEndUT - winStartUT) / (10.0 * 86400.0)) + 1));
            double epochStep = (sampleEpochCount > 1) ? ((winEndUT - winStartUT) / (sampleEpochCount - 1)) : 0.0;

            for (int epIdx = 0; epIdx < sampleEpochCount; epIdx++) {
                double tDep = winStartUT + epIdx * epochStep;
                var genome = new double[dim];
                genome[0] = tDep;

                if (optDsmEta) {
                    for (int k = 0; k < legCount; k++) {
                        genome[1 + legCount + k] = 0.5;
                    }
                }

                double curUT = tDep;
                Vector3d prevArrVinf = Vector3d.zero;

                for (int k = 0; k < legCount; k++) {
                    string fromN = fullChain[k];
                    string toN = fullChain[k + 1];
                    var p1 = SolarSystemCatalog.GetPlanet(fromN);
                    var p2 = SolarSystemCatalog.GetPlanet(toN);

                    double minToF = lowerBounds[1 + k];
                    double maxToF = upperBounds[1 + k];
                    int tofSteps = 36;
                    double bestLegToF = (minToF + maxToF) * 0.5;
                    double bestLegScore = double.MaxValue;

                    p1.GetHeliocentricState(curUT, out Vector3d r1, out Vector3d v1);

                    var candToFs = new List<double>();
                    if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                        double sma = PykepBridge.GetRelativeSMA(p1, central, 0.0);
                        double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                        double[] ratios = new double[] { 1.0, 1.25, 1.333, 1.5, 2.0, 2.5, 3.0, 4.0 };
                        foreach (double r in ratios) {
                            double resTof = r * tOrb;
                            if (resTof >= minToF && resTof <= maxToF) {
                                candToFs.Add(resTof);
                            }
                        }
                        if (candToFs.Count == 0) candToFs.Add(Math.Max(minToF, Math.Min(maxToF, tOrb)));
                    } else {
                        for (int s = 0; s <= tofSteps; s++) {
                            candToFs.Add(minToF + s * (maxToF - minToF) / tofSteps);
                        }
                    }

                    foreach (double testToF in candToFs) {
                        double arrUT = curUT + testToF;
                        p2.GetHeliocentricState(arrUT, out Vector3d r2, out Vector3d v2);

                        if (fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                            // 同星共振段：解析开普勒闭式守恒评估，消除共点兰伯特奇异发散
                            double sma = PykepBridge.GetRelativeSMA(p1, central, 0.0);
                            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                            double ratio = testToF / Math.Max(1.0, tOrb);
                            double minResDiff = double.MaxValue;
                            foreach (double r in new double[] { 1.0, 1.25, 1.333, 1.5, 2.0, 2.5, 3.0, 4.0 }) {
                                double diff = Math.Abs(ratio - r);
                                if (diff < minResDiff) minResDiff = diff;
                            }
                            double score = minResDiff * 5.0; // 越贴近精确有理共振槽得分越高
                            if (score < bestLegScore) {
                                bestLegScore = score;
                                bestLegToF = testToF;
                            }
                        } else if (PykepNative.SolveLambert(r1, r2, testToF, centralMu, false, out Vector3d vDep, out Vector3d vArr)) {
                            Vector3d vInfDep = vDep - v1;
                            double score = 0.0;

                            if (k == 0) {
                                double rp = p1.RadiusKm + (config?.DeparturePeAKm ?? 200.0);
                                double vHyp = Math.Sqrt(vInfDep.sqrMagnitude + 2.0 * p1.Mu / rp);
                                double vPark = Math.Sqrt(p1.Mu / rp);
                                score = (vHyp - vPark); // LEO 逃逸 ΔV (km/s)
                            } else {
                                double inMag = prevArrVinf.magnitude;
                                double outMag = vInfDep.magnitude;
                                double vDiff = Math.Abs(inMag - outMag);

                                double safeR = MGADefaults.GetBodySafeRadiusKm(fromN);
                                double eMin = 1.0 + (safeR * Math.Max(1.0, inMag * inMag)) / Math.Max(1.0, p1.Mu);
                                double maxTurnRad = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eMin));

                                double dot = (inMag > 1e-4 && outMag > 1e-4) ? Vector3d.Dot(prevArrVinf.normalized, vInfDep.normalized) : 1.0;
                                dot = Math.Max(-1.0, Math.Min(1.0, dot));
                                double reqTurnRad = Math.Acos(dot);

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

                    if (!fromN.Equals(toN, StringComparison.OrdinalIgnoreCase)) {
                        p2.GetHeliocentricState(curUT, out _, out Vector3d v2Final);
                        p1.GetHeliocentricState(curUT - bestLegToF, out Vector3d r1Final, out _);
                        p2.GetHeliocentricState(curUT, out Vector3d r2Final, out _);
                        if (PykepNative.SolveLambert(r1Final, r2Final, bestLegToF, centralMu, false, out _, out Vector3d vArrFinal)) {
                            prevArrVinf = vArrFinal - v2Final;
                        }
                    } else {
                        // 同星共振调相段：天体引力井进出场双曲过剩速度大小物理严格守恒 |v_inf,out| = |v_inf,in|。
                        // 关键动力学机制：在无动力双曲物理偏折极限 (delta_max) 范围内，
                        // 沿借力天体双曲轨道将过剩速度向量偏转朝向下一段行星际目标，
                        // 累积各阶共振偏折角 (N x delta_max)，彻底消除出射航段的几何赤字断层！
                        double inMag = prevArrVinf.magnitude;
                        if (inMag < 1e-4) {
                            p1.GetHeliocentricState(curUT, out Vector3d r1Now, out Vector3d v1Now);
                            double sma = PykepBridge.GetRelativeSMA(p1, central, 0.0);
                            double aRes = sma * Math.Pow(bestLegToF / (PykepBridge.CalcOrbitPeriodSec(sma, centralMu) + 1e-5), 2.0 / 3.0);
                            double vCraft = Math.Sqrt(Math.Max(1e-4, centralMu * (2.0 / r1Now.magnitude - 1.0 / Math.Max(1.0, aRes))));
                            double vInfMag = Math.Abs(vCraft - v1Now.magnitude);
                            inMag = Math.Max(1.0, vInfMag);
                            prevArrVinf = v1Now.normalized * inMag;
                        }

                        double safeR = MGADefaults.GetBodySafeRadiusKm(fromN);
                        double eMin = 1.0 + (safeR * inMag * inMag) / Math.Max(1.0, p1.Mu);
                        double maxTurnRad = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eMin));

                        // 动态查找全链后续的首个异星目标
                        string nextTarget = null;
                        for (int nk = k + 1; nk < fullChain.Count; nk++) {
                            if (!fullChain[nk].Equals(fromN, StringComparison.OrdinalIgnoreCase)) {
                                nextTarget = fullChain[nk];
                                break;
                            }
                        }

                        if (nextTarget != null) {
                            var pNext = SolarSystemCatalog.GetPlanet(nextTarget);
                            if (pNext != null) {
                                double sma1 = PykepBridge.GetRelativeSMA(p1, central, 0.0);
                                double smaNext = PykepBridge.GetRelativeSMA(pNext, central, 0.0);
                                double approxTofSec = 100.0 * 86400.0;
                                if (sma1 > 0 && smaNext > 0) {
                                    double aH = (sma1 + smaNext) * 0.5;
                                    approxTofSec = Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
                                }
                                pNext.GetHeliocentricState(curUT + approxTofSec, out Vector3d rNext, out _);
                                p1.GetHeliocentricState(curUT, out Vector3d r1Now, out Vector3d v1Now);
                                Vector3d dirTrans = (rNext - r1Now).normalized;
                                double vTransSpeed = Math.Sqrt(Math.Max(1e-4, centralMu * (2.0 / r1Now.magnitude - 2.0 / (r1Now.magnitude + rNext.magnitude))));
                                Vector3d vDesiredDep = dirTrans * vTransSpeed;
                                Vector3d vInfDesired = vDesiredDep - v1Now;
                                if (vInfDesired.sqrMagnitude > 1e-4) {
                                    Vector3d uIn = prevArrVinf.normalized;
                                    Vector3d uTgt = vInfDesired.normalized;
                                    double dot = Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(uIn, uTgt)));
                                    double reqTurn = Math.Acos(dot);
                                    double turnAngle = Math.Min(reqTurn, maxTurnRad);

                                    Vector3d rotAxis = Vector3d.Cross(uIn, uTgt);
                                    if (rotAxis.sqrMagnitude < 1e-6) {
                                        rotAxis = Vector3d.Cross(uIn, Vector3d.up);
                                        if (rotAxis.sqrMagnitude < 1e-6) rotAxis = Vector3d.right;
                                    }
                                    rotAxis = rotAxis.normalized;
                                    double cosA = Math.Cos(turnAngle);
                                    double sinA = Math.Sin(turnAngle);
                                    Vector3d uOut = uIn * cosA + Vector3d.Cross(rotAxis, uIn) * sinA + rotAxis * Vector3d.Dot(rotAxis, uIn) * (1.0 - cosA);
                                    prevArrVinf = uOut.normalized * inMag;
                                }
                            }
                        }
                    }
                }

                seedGenomes.Add(genome);

                // 派生微扰变体
                double[] perturbations = new double[] { -0.05, 0.05 };
                foreach (double factor in perturbations) {
                    var pGene = (double[])genome.Clone();
                    for (int d = 1; d <= legCount; d++) {
                        if (d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                            // 共振调相段保持在精确物理开普勒共振槽上，不进行大跨步微扰
                            continue;
                        }
                        double spanD = upperBounds[d] - lowerBounds[d];
                        pGene[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], pGene[d] + factor * spanD * 0.25));
                    }
                    seedGenomes.Add(pGene);
                }
            }

            for (int i = 0; i < popSize; i++) {
                pop[i] = new double[dim];
                if (i < seedGenomes.Count) {
                    pop[i] = (double[])seedGenomes[i].Clone();
                } else {
                    for (int d = 0; d < dim; d++) {
                        if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                            var pBody = SolarSystemCatalog.GetPlanet(fullChain[d]);
                            double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                            if (tOrb > 0) {
                                var validRatios = new List<double>();
                                foreach (double r in new double[] { 1.0, 1.25, 1.333, 1.5, 2.0, 2.5, 3.0, 4.0 }) {
                                    double rSec = r * tOrb;
                                    if (rSec >= lowerBounds[d] - 86400.0 && rSec <= upperBounds[d] + 86400.0) {
                                        validRatios.Add(r);
                                    }
                                }
                                if (validRatios.Count > 0) {
                                    double pickedRatio = validRatios[random.Next(validRatios.Count)];
                                    double resCenter = pickedRatio * tOrb;
                                    double rSoi = (pBody != null && pBody.SoiKm > 0) ? pBody.SoiKm : 1.5e5;
                                    double maxOffset = (config != null && config.ResonantMaxOffsetDays > 0)
                                        ? config.ResonantMaxOffsetDays * 86400.0
                                        : Math.Max(0.2 * 86400.0, Math.Min(1.0 * 86400.0, (2.0 * rSoi) / 10.0));
                                    double offset = (random.NextDouble() * 2.0 - 1.0) * maxOffset;
                                    pop[i][d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], resCenter + offset));
                                    continue;
                                }
                            }
                        }
                        pop[i][d] = lowerBounds[d] + random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                    }
                }
                var sol = PykepBridge.BuildFullSolution(pop[i], fullChain, config, computeAdvanced: false);
                fitness[i] = ComputeFitness(sol, config);
            }
            CPUMonitor.RecordEvaluation(popSize);

            for (int gen = 0; gen < maxGens; gen++) {
                if (ct.IsCancellationRequested) break;

                Action<int> evalIndividual = i => {
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
                                    foreach (double r in new double[] { 1.0, 2.0, 3.0, 4.0 }) {
                                        double rCenter = r * tOrb;
                                        if (rCenter >= lowerBounds[d] - 86400.0 && rCenter <= upperBounds[d] + 86400.0) {
                                            double diff = Math.Abs(candidate[d] - rCenter);
                                            if (diff < minDiff) {
                                                minDiff = diff;
                                                bestResCenter = rCenter;
                                            }
                                        }
                                    }
                                    double offset = candidate[d] - bestResCenter;
                                    double maxOffset;
                                    if (config != null && config.ResonantMaxOffsetDays > 0) {
                                        maxOffset = config.ResonantMaxOffsetDays * 86400.0;
                                    } else {
                                        double rSoi = (pBody != null && pBody.SoiKm > 0) ? pBody.SoiKm : 1.5e5;
                                        double tSoiSec = (2.0 * rSoi) / 10.0; // 假定平均 V_inf ~ 10 km/s
                                        maxOffset = Math.Max(0.2 * 86400.0, Math.Min(1.0 * 86400.0, tSoiSec));
                                    }
                                    candidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], bestResCenter + Math.Max(-maxOffset, Math.Min(maxOffset, offset))));
                                }
                            }
                        } else {
                            candidate[d] = pop[i][d];
                        }
                    }

                    var candSol = PykepBridge.BuildFullSolution(candidate, fullChain, config, computeAdvanced: false);
                    double candFit = ComputeFitness(candSol, config);
                    if (candFit < fitness[i]) {
                        pop[i] = candidate;
                        fitness[i] = candFit;
                    }
                };

                if (parallelizeInternal) {
                    Parallel.For(0, popSize, evalIndividual);
                } else {
                    for (int i = 0; i < popSize; i++) {
                        evalIndividual(i);
                    }
                }
                CPUMonitor.RecordEvaluation(popSize);

                // 环形拓扑精英迁移 (Ring Topology Elite Migration: Dario Izzo et al. Springer 2012)
                // 每 10 代迁移各岛 Top 2 精英染色体至环形下游岛屿，替换下游最差个体，实现防早熟与全局搜索互救
                if (gen > 0 && gen % 10 == 0) {
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

            var rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();

            // 局部坐标下降与 Hooke-Jeeves 模式加速搜索 (Memetic Hooke-Jeeves Pattern Acceleration Polish):
            // 围绕全局进化沉降的精英染色体，先逐坐标微步探索梯度斜率，并沿改善方向执行倍增步长加速，
            // 随后执行全维模式跳跃 x_pattern = x + D，快速穿越狭长平缓的兰伯特相位峡谷，将 DSM 压降至物理极限
            int polishCount = Math.Min(6, rankedIndices.Count);
            for (int pIdx = 0; pIdx < polishCount; pIdx++) {
                int indivIdx = rankedIndices[pIdx];
                var curGene = (double[])pop[indivIdx].Clone();
                double curFit = fitness[indivIdx];

                for (int iter = 0; iter < 12; iter++) {
                    var baseGene = (double[])curGene.Clone();
                    double baseFit = curFit;
                    bool anyImproved = false;

                    // 1. 逐坐标探索移动 (Exploratory Move)
                    for (int d = 0; d < dim; d++) {
                        bool isResLeg = (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase));
                        double resOffsetMax = (config != null && config.ResonantMaxOffsetDays > 0) ? config.ResonantMaxOffsetDays * 86400.0 : 0.4 * 86400.0;
                        double span = isResLeg ? resOffsetMax : (upperBounds[d] - lowerBounds[d]);
                        double[] probeFactors = isResLeg
                            ? new double[] { -1.0, 1.0, -0.5, 0.5, -0.2, 0.2 }
                            : new double[] { -0.04, 0.04, -0.01, 0.01, -0.002, 0.002 };
                        double bestDVal = curGene[d];
                        double bestDFit = curFit;
                        double bestFactor = 0.0;
                        double bestNextDVal = (d < legCount) ? curGene[d + 1] : 0.0;
                        bool hasComp = false;

                        // 若该坐标对应同星共振航段，直接评估开普勒精确物理整数共振槽 (1:1, 2:1, 3:1, 4:1) 及 SOI 微时差
                        if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                            var pBody = SolarSystemCatalog.GetPlanet(fullChain[d]);
                            double sma = PykepBridge.GetRelativeSMA(pBody, central, config?.DeparturePeAKm ?? MGADefaults.DepartureAltitudeKm);
                            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                            double[] resRatios = new double[] { 1.0, 1.25, 1.333, 1.5, 2.0, 2.5, 3.0, 4.0 };
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
                                for (int accStep = 0; accStep < 3; accStep++) {
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
                                    foreach (double r in new double[] { 1.0, 2.0, 3.0, 4.0 }) {
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

            rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();
            var seenKeys = new HashSet<long>();

            double winBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
            double winDsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : winBudgetMs;

            // 第一阶段：细粒度网格采样 (12小时发射UT格 + 1天飞行历时格)，提取帕累托前沿不同飞行时长的多样性解
            foreach (var idx in rankedIndices) {
                if (fitness[idx] >= 1e8) continue;
                var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: true);
                if (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) {
                    if (winBudgetMs < 1e8 && sol.TotalDeltaVKms * 1000.0 > winBudgetMs * 1.02) continue;
                    if (winDsmBudgetMs < 1e8 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs * 1.02 || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs * 1.02)))) continue;
                    if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs * 1.05) continue;
                    if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                    if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;

                    long key = ((long)(int)(sol.DepartureUT / 43200.0) << 32) ^ (long)(uint)(int)(sol.TotalFlightDays / 1.0);
                    if (!seenKeys.Contains(key)) {
                        seenKeys.Add(key);
                        solutions.Add(sol);
                    }
                }
                if (solutions.Count >= 8) break;
            }

            // 第二阶段保底：若收敛解不足 5 个（如单腿直达转移收敛较紧凑），吸纳发射相差 > 6 小时或飞行历时相差 > 0.5 天的有效解
            if (solutions.Count < 5) {
                foreach (var idx in rankedIndices) {
                    if (fitness[idx] >= 1e7) continue;
                    var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: true);
                    if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) continue;
                    if (winBudgetMs < 1e8 && sol.TotalDeltaVKms * 1000.0 > winBudgetMs * 1.02) continue;
                    if (winDsmBudgetMs < 1e8 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs * 1.02 || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs * 1.02)))) continue;
                    if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs * 1.05) continue;
                    if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                    if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;

                    bool tooClose = solutions.Any(s =>
                        Math.Abs(s.DepartureUT - sol.DepartureUT) < 21600.0 &&
                        Math.Abs(s.TotalFlightDays - sol.TotalFlightDays) < 0.5
                    );
                    if (!tooClose) {
                        solutions.Add(sol);
                    }
                    if (solutions.Count >= 8) break;
                }
            }

            // 第三阶段：若候选方案仍不足 5 个（例如单腿直达转移仅有 2 个变量，DE 种群高度收敛至单一点），
            // 从最优解的帕累托前沿集合 (Pareto Trade-Off Curve) 提取多样性时间/燃料权衡方案，保证决策池完备
            if (solutions.Count > 0 && solutions.Count < 6) {
                var baseSol = solutions[0];
                if (baseSol.ParetoFront != null && baseSol.ParetoFront.Count > 1) {
                    foreach (var pt in baseSol.ParetoFront) {
                        if (pt.Genome == null) continue;
                        bool alreadyHas = solutions.Any(s => Math.Abs(s.TotalFlightDays - pt.TotalFlightDays) < 0.5);
                        if (!alreadyHas) {
                            var pSol = PykepBridge.BuildFullSolution(pt.Genome, fullChain, config, computeAdvanced: true);
                            if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < 1e7) {
                                pSol.ParetoFront = baseSol.ParetoFront;
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
                    solutions.Add(bestCand);
                }
            }

            solutions = solutions.OrderBy(s => s.TotalDeltaVKms).ToList();
            return solutions;
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
                        fit += MGADefaults.PenaltyInfeasibleBase + 1000.0 * overKms + 500.0 * overKms * overKms; // 强约束惩罚：硬性阻断超预算解
                    }
                    foreach (var leg in sol.Legs) {
                        if (leg.HasDSM && leg.DSM != null && leg.DSM.DeltaVMs > dsmBudget) {
                            double overKms = (leg.DSM.DeltaVMs - dsmBudget) / 1000.0;
                            fit += 500.0 * overKms + 250.0 * overKms * overKms;
                        }
                    }
                }
                if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) {
                    double totalMs = sol.TotalDeltaVKms * 1000.0;
                    if (totalMs > config.VesselMaxDeltaVMs) {
                        double overKms = (totalMs - config.VesselMaxDeltaVMs) / 1000.0;
                        fit += MGADefaults.PenaltyInfeasibleBase + 1000.0 * overKms + 500.0 * overKms * overKms; // 强约束惩罚：硬性阻断超预算解
                    }
                }
                if (config.LaunchDeltaVCeilingMs > 0) {
                    double launchMs = sol.LaunchDeltaVKms * 1000.0;
                    if (launchMs > config.LaunchDeltaVCeilingMs) {
                        double overLaunchKms = (launchMs - config.LaunchDeltaVCeilingMs) / 1000.0;
                        fit += MGADefaults.PenaltyInfeasibleBase + 2000.0 * overLaunchKms + 1000.0 * overLaunchKms * overLaunchKms; // 严格限制首段 TVI 出射速度
                    }
                }
                if (sol.TotalFlybyDeltaVKms > 1e-4) {
                    fit += sol.TotalFlybyDeltaVKms * 30.0 + 50.0; // 惩罚动力飞掠，迫使纯无动力引力弹弓
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