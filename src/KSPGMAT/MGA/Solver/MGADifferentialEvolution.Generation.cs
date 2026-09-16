// ============================================================================
// MGADifferentialEvolution.Generation
// 初始群体 / 启发式注种 / 前向步进合成 / 共振块拓扑映射 (部分拆分部)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;
using KSPGMAT.MGA.AI;
using KSPGMAT.MGA.Telemetry;
using KSPGMAT.MGA.GPU;
using UnityEngine;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 单时间窗口差分进化评估内核（partial 拆分，动力学评估仍 100% 委托 PykepBridge.BuildFullSolution）。
    /// </summary>
    public static partial class MGADifferentialEvolution
    {
        /// <summary>
        /// 普适前向借力步进动力学注种与全局适应度初评优选
        /// 依据前向逐级引力偏折匹配与能量守恒，为高维 MGA 链路提供高质量真实物理基底，杜绝纯随机投点引发的 Lambert 几何发散
        /// </summary>
        private static void SynthesizeForwardStagedSeeds(
            List<double[]> seedGenomes,
            out Dictionary<int, List<double[]>> cachedBlockCombos,
            MGAConfig config,
            List<string> fullChain,
            int dim,
            int legCount,
            bool optDsmEta,
            bool hasResonantBlock,
            double winStartUT,
            double winEndUT,
            string central,
            double centralMu,
            double[] lowerBounds,
            double[] upperBounds,
            Func<MGASolution, double> fitnessFunc)
        {
            // 普适前向借力步进动力学注种 (Universal Forward-Staging Seed Synthesis):
            // 依据前向逐级引力偏折匹配与能量守恒，为高维 MGA 链路提供高质量真实物理基底，杜绝纯随机投点引发的 Lambert 几何发散
            cachedBlockCombos = new Dictionary<int, List<double[]>>();
            // 【确定性契约】并行采样的结果不再直接写入共享 List, 而是先落入各历元本地缓冲,
            // 循环结束后按 epIdx 升序合并。原因: "首次出现者胜出"的去重语义若由线程时序决定,
            // 则保留的组合集合运行间不同 -> 注种不同 -> 解不可复现 (实测 cassini 相差 78.7 m/s)。
            List<Tuple<int, double[]>>[] perEpochCombos = null;
            double maxDurationSec = (config != null && config.EnableMaxDuration && config.MaxDurationDays > 0) ? config.MaxDurationDays * 86400.0 : 0.0;
            using (MGAStepProfiler.Scope("DE注种: 前向步进引力偏折动力学合成"))
            {
            // 特征稀疏采样：聚焦高价值历元与霍曼分段，步长依据行星际窗口特征(约20天)自适应设定
            // 采用奇数个采样点以保证窗口两端与正中心对齐 (Window Center Symmetry)
            int sampleEpochCount = Math.Max(5, Math.Min(7, (int)Math.Ceiling((winEndUT - winStartUT) / (20.0 * 86400.0)) + 1));
            if (sampleEpochCount % 2 == 0) sampleEpochCount++;
            double epochStep = (sampleEpochCount > 1) ? ((winEndUT - winStartUT) / (sampleEpochCount - 1)) : 0.0;

            // 覆盖极速高能出射、短霍曼、标称霍曼、扩展 Type-II 借力切向弧等具代表性的开普勒物理分支
            double[] leg0Fractions = new double[] { 0.00, 0.04, 0.08, 0.12, 0.16, 0.20, 0.25, 0.30, 0.35, 0.42, 0.50, 0.65, 0.80, 0.95 };
            var stagedSeeds = new System.Collections.Concurrent.ConcurrentBag<Tuple<double, double[]>>();



            perEpochCombos = new List<Tuple<int, double[]>>[sampleEpochCount];

            Parallel.For(0, sampleEpochCount, epIdx => {
                double tDep = winStartUT + epIdx * epochStep;
                var localCombos = new List<Tuple<int, double[]>>();
                var resonantCombosCache = new Dictionary<string, List<Tuple<double[], double>>>(StringComparer.Ordinal);
                var epochSeeds = new List<Tuple<double, double[]>>();

                foreach (double leg0Frac in leg0Fractions) {
                    int numVariants = hasResonantBlock ? 4 : 1;
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
                                string downstreamTarget = null;
                                if (resEnd + 3 < fullChain.Count && !string.IsNullOrEmpty(exitTarget)) {
                                    // 仅当下一步是前往另一个不同的天体时，才存在跨天体下游活力亏空校验
                                    // 若下一步仍为同一天体 (如 Venus ➔ Venus 同星自共振调相)，则出口星自身即为共振跳板，严禁跨级强加直飞下游的虚假亏空
                                    if (!fullChain[resEnd + 3].Equals(exitTarget, StringComparison.OrdinalIgnoreCase)) {
                                        downstreamTarget = fullChain[resEnd + 3];
                                    }
                                }

                                double nominalExitToFSec = 0.0;
                                int enterLeg = resStart - 1;
                                while (enterLeg >= 0 && fullChain[enterLeg].Equals(fromN, StringComparison.OrdinalIgnoreCase)) {
                                    enterLeg--;
                                }
                                if (enterLeg >= 0 && !string.IsNullOrEmpty(exitTarget) && fullChain[enterLeg].Equals(exitTarget, StringComparison.OrdinalIgnoreCase)) {
                                    nominalExitToFSec = genome[1 + enterLeg];
                                }

                                bool isExitDestination = (resEnd + 2 >= fullChain.Count - 1);
                                string rCacheKey = $"{fromN}->{exitTarget ?? "NONE"}->{downstreamTarget ?? "NONE"}#{blockLen}#{resStart}#{Math.Round(curUT / 43200.0)}#{Math.Round(prevArrVinf.magnitude * 5.0)}#{Math.Round(nominalExitToFSec / 86400.0)}#{isExitDestination}";
                                if (!resonantCombosCache.TryGetValue(rCacheKey, out var rankedCombos)) {
                                    rankedCombos = FindAndRankResonantCombos(
                                        fromN, exitTarget, blockLen,
                                        curUT, prevArrVinf,
                                        lowerBounds, upperBounds, resStart,
                                        central, centralMu, nominalExitToFSec,
                                        isExitDestination, downstreamTarget);
                                    resonantCombosCache[rCacheKey] = rankedCombos;
                                }

                                if (rankedCombos != null) {
                                    foreach (var c in rankedCombos.Take(6)) {
                                        localCombos.Add(Tuple.Create(resStart, c.Item1));
                                    }
                                }

                                int comboIdx = (rankedCombos != null && rankedCombos.Count > 0)
                                    ? seedVariant % rankedCombos.Count
                                    : 0;
                                var chosenTuple = (rankedCombos != null && rankedCombos.Count > comboIdx)
                                    ? rankedCombos[comboIdx]
                                    : null;
                                double[] chosen = chosenTuple?.Item1;
                                double chosenExitToF = chosenTuple?.Item2 ?? 0.0;

                                var pBody = SolarSystemCatalog.GetPlanet(fromN);
                                double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                                double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);

                                double[] phasePerturbations = new double[] { 1.0, 1.005, 1.01, 0.995 };
                                double phaseScale = phasePerturbations[seedVariant % 4];

                                double dwellSec = 0.0;
                                for (int i = 0; i < blockLen; i++) {
                                    double r = (chosen != null && i < chosen.Length) ? chosen[i] : 1.0;
                                    double legToF = r * tOrb * phaseScale;
                                    legToF = Math.Max(lowerBounds[1 + resStart + i], Math.Min(upperBounds[1 + resStart + i], legToF));
                                    genome[1 + resStart + i] = legToF;
                                    dwellSec += legToF;
                                }
                                curUT += dwellSec;

                                // 将最优共振出口航时直接预填入下游出口航段，并针对共振出口航段配置自适应 DSM eta (打破 0.5 固定点)
                                if (chosenExitToF > 10.0 * 86400.0 && resEnd + 1 < legCount) {
                                    genome[1 + resEnd + 1] = Math.Max(lowerBounds[1 + resEnd + 1], Math.Min(upperBounds[1 + resEnd + 1], chosenExitToF));
                                    if (optDsmEta) {
                                        // 基于低差异黄金分割序列在 [0.2, 0.8] 连续空间均匀分散注种，避免对称固定点与特定任务过拟合
                                        double quasiEta = 0.2 + 0.6 * (((seedVariant + 1) * 0.618033988749895) % 1.0);
                                        genome[1 + legCount + (resEnd + 1)] = Math.Max(lowerBounds[1 + legCount + (resEnd + 1)], Math.Min(upperBounds[1 + legCount + (resEnd + 1)], quasiEta));
                                    }
                                }

                                // 针对共振段内部各腿，采用低差异序列均匀铺开 DSM eta 分布
                                if (optDsmEta) {
                                    for (int bi = 0; bi < blockLen; bi++) {
                                        double rRatio = (chosen != null && bi < chosen.Length) ? chosen[bi] : 1.0;
                                        int qEst = (rRatio > 2.2) ? (int)Math.Round(rRatio * 0.8) : 1;
                                        if (qEst < 1) qEst = 1;
                                        int apoIdx = seedVariant % qEst;
                                        double apoEta = (2.0 * apoIdx + 1.0) / (2.0 * qEst);
                                        double quasiResEta = (seedVariant % 3 == 0)
                                            ? Math.Max(0.10, Math.Min(0.90, apoEta))
                                            : (0.15 + 0.70 * (((seedVariant + bi * 3 + 1) * 0.618033988749895) % 1.0));
                                        int geneIdx = 1 + legCount + resStart + bi;
                                        genome[geneIdx] = Math.Max(lowerBounds[geneIdx], Math.Min(upperBounds[geneIdx], quasiResEta));
                                    }
                                }

                                // Seamless V_inf propagation to exit leg
                                if (!string.IsNullOrEmpty(exitTarget)) {
                                    var pExit = SolarSystemCatalog.GetPlanet(exitTarget);
                                    if (pExit != null) {
                                        double smaNext = PykepBridge.GetRelativeSMA(pExit, central, 0.0);
                                        double aH = (sma + smaNext) * 0.5;
                                        double approxTofSec = (chosenExitToF > 10.0 * 86400.0) ? chosenExitToF : Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
                                        pBody.GetRelativeState(central, curUT, out Vector3d rDep, out Vector3d vDepPlanet);

                                        pExit.GetRelativeState(central, curUT + approxTofSec, out Vector3d rArrNext, out _);

                                        double rExitAvg = (rDep.magnitude + rArrNext.magnitude) * 0.5;
                                        double tOrbExitAvg = 2.0 * Math.PI * Math.Sqrt(Math.Pow(rExitAvg * 1000.0, 3.0) / (centralMu * 1e9));
                                        int nmaxExit = (tOrbExitAvg > 10.0) ? (int)Math.Floor(approxTofSec / tOrbExitAvg + 0.25) : 0;
                                        nmaxExit = Math.Max(0, Math.Min(3, nmaxExit));
                                        if (PykepNative.SolveLambert(rDep, rArrNext, approxTofSec, centralMu, false, nmaxExit, out Vector3d vDepLambert, out _)) {
                                            Vector3d vExitInf = vDepLambert - vDepPlanet;
                                            // 航天器经由自共振 DeltaV-EGA (或中间借力) 离场时，其能量已与出口轨道匹配:
                                            // 满足无动力飞掠守恒公理: |v_inf,in| = |v_inf,out| = vExitInf.magnitude
                                            prevArrVinf = vExitInf;
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

                            p1.GetRelativeState(central, curUT, out Vector3d r1, out Vector3d v1);
                            var candToFs = new List<double>();
                            if (k == 0) {
                                candToFs.Add(minToF + leg0Frac * (maxToF - minToF));
                            } else {
                                double preExitToF = genome[1 + k];
                                bool hasPreExit = (preExitToF >= minToF && preExitToF <= maxToF && preExitToF > 10.0 * 86400.0);

                                if (hasPreExit) {
                                    // 共振出口锁定优先：若本段已由会合周期与物理共振段精准求解出出口航时，
                                    // 重点探索该出口点附近的物理微调候选 (严格收敛在共振吸引盆内)
                                    candToFs.Add(preExitToF);
                                    candToFs.Add(preExitToF * 0.98);
                                    candToFs.Add(preExitToF * 1.02);
                                    candToFs.Add(preExitToF * 0.95);
                                    candToFs.Add(preExitToF * 1.05);
                                }

                                // 开普勒物理特征自适应稀疏网格采样 (Adaptive Kepler Feature Mesh)
                                // 依据天体动力学能量曲率与霍曼骨干自适应取样，完整覆盖极速双曲飞掠弧至慢外向转移
                                double sma1 = PykepBridge.GetRelativeSMA(p1, central, 0.0);
                                double sma2 = PykepBridge.GetRelativeSMA(p2, central, 0.0);
                                double tHohmann = 0.0;
                                if (sma1 > 0 && sma2 > 0) {
                                    double aH = (sma1 + sma2) * 0.5;
                                    tHohmann = Math.PI * Math.Sqrt(Math.Pow(aH * 1000.0, 3.0) / (centralMu * 1e9));
                                }

                                candToFs.Add(minToF);
                                candToFs.Add(maxToF);
                                candToFs.Add((minToF + maxToF) * 0.5);

                                if (tHohmann > 0) {
                                    // 涵盖极速双曲飞掠弧 (0.08~0.38 如外行星双曲逃逸快弧、金星-地球55天、水星48~52天)、标称霍曼、及外向慢弧
                                    double[] hohmannScales = new double[] { 0.08, 0.10, 0.12, 0.15, 0.20, 0.25, 0.28, 0.32, 0.38, 0.45, 0.50, 0.58, 0.65, 0.70, 0.75, 0.85, 1.00, 1.30, 1.80, 2.50, 3.30 };
                                    foreach (double sc in hohmannScales) {
                                        double tH = tHohmann * sc;
                                        if (tH >= minToF && tH <= maxToF) candToFs.Add(tH);
                                    }
                                } else {
                                    int coarseSteps = 8;
                                    for (int s = 1; s < coarseSteps; s++) {
                                        candToFs.Add(minToF + s * (maxToF - minToF) / coarseSteps);
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
                            }

                            foreach (double testToF in candToFs) {
                                double arrUT = curUT + testToF;
                                p2.GetRelativeState(central, arrUT, out Vector3d r2, out Vector3d v2);

                                // 【已回退】此处曾接入代理前置粗筛。实测后移除, 依据见
                                // docs/reports/硬编码与经验公式审计报告.md §15:
                                //   本循环的原生 Lambert 探针总耗时仅 155 ms, 占"DE注种"阶段 11.09 s 的 1.4%;
                                //   代理门自身 100 ms (2.1 us/次) 与之同量级, 且筛除会改变搜索路径分支,
                                //   净效果为负。故粗筛在此位置无收益, 恢复原始直调。
                                double rTestAvg = (r1.magnitude + r2.magnitude) * 0.5;
                                double tOrbTestAvg = 2.0 * Math.PI * Math.Sqrt(Math.Pow(rTestAvg * 1000.0, 3.0) / (centralMu * 1e9));
                                int nmaxTest = (tOrbTestAvg > 10.0) ? (int)Math.Floor(testToF / tOrbTestAvg + 0.25) : 0;
                                nmaxTest = Math.Max(0, Math.Min(3, nmaxTest));
                                if (PykepNative.SolveLambert(r1, r2, testToF, centralMu, false, nmaxTest, out Vector3d vDep, out Vector3d vArr)) {
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
                                        double vTurn = 2.0 * Math.Max(vInfDep.magnitude, prevArrVinf.magnitude) * Math.Sin(turnExcess * 0.5);
                                        score = vDiff + vTurn;
                                    }
                                    if (maxDurationSec > 0)
                                    {
                                        double remMinSec = 0.0;
                                        for (int rk = k + 1; rk < legCount; rk++) remMinSec += lowerBounds[1 + rk];
                                        double totalSoFar = (curUT - tDep) + testToF + remMinSec;
                                        if (totalSoFar > maxDurationSec)
                                        {
                                            double overDays = (totalSoFar - maxDurationSec) / 86400.0;
                                            score += 20.0 * overDays;
                                        }
                                    }
                                    if (score < bestLegScore) {
                                        bestLegScore = score;
                                        bestLegToF = testToF;
                                    }
                                }
                            }

                            // 围绕最佳物理骨干候选执行两步自适应轻量微调 (Localized Gradient Refinement)
                            if (k > 0 && bestLegScore < 1e5) {
                                double stepSpan = Math.Min((maxToF - minToF) * 0.03, bestLegToF * 0.03);
                                double[] refineToFs = new double[] { bestLegToF - stepSpan, bestLegToF + stepSpan };
                                foreach (double refToF in refineToFs) {
                                    if (refToF >= minToF && refToF <= maxToF) {
                                        double arrUT = curUT + refToF;

                                        p2.GetRelativeState(central, arrUT, out Vector3d r2Ref, out _);

                                        double rRefAvg = (r1.magnitude + r2Ref.magnitude) * 0.5;
                                        double tOrbRefAvg = 2.0 * Math.PI * Math.Sqrt(Math.Pow(rRefAvg * 1000.0, 3.0) / (centralMu * 1e9));
                                        int nmaxRef = (tOrbRefAvg > 10.0) ? (int)Math.Floor(refToF / tOrbRefAvg + 0.25) : 0;
                                        nmaxRef = Math.Max(0, Math.Min(3, nmaxRef));
                                        if (PykepNative.SolveLambert(r1, r2Ref, refToF, centralMu, false, nmaxRef, out Vector3d vDepRef, out _)) {
                                            Vector3d vInfDep = vDepRef - v1;
                                            double vDiff = Math.Abs(vInfDep.magnitude - prevArrVinf.magnitude);
                                            double dot = Vector3d.Dot(vInfDep.normalized, prevArrVinf.normalized);
                                            dot = Math.Max(-1.0, Math.Min(1.0, dot));
                                            double reqTurnRad = Math.Acos(dot);
                                            double safeR = MGADefaults.GetBodySafeRadiusKm(fromN);
                                            double e = 1.0 + (safeR * prevArrVinf.sqrMagnitude) / Math.Max(1.0, p1.Mu);
                                            double maxTurnRad = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, e));
                                            double turnExcess = Math.Max(0.0, reqTurnRad - maxTurnRad);
                                            double vTurn = 2.0 * Math.Max(vInfDep.magnitude, prevArrVinf.magnitude) * Math.Sin(turnExcess * 0.5);
                                            double sRef = vDiff + vTurn;
                                            if (sRef < bestLegScore) {
                                                bestLegScore = sRef;
                                                bestLegToF = refToF;
                                            }
                                        }
                                    }
                                }
                            }

                            genome[1 + k] = bestLegToF;
                            curUT += bestLegToF;
                            p2.GetRelativeState(central, curUT, out _, out Vector3d v2Final);
                            p1.GetRelativeState(central, curUT - bestLegToF, out Vector3d r1Final, out _);
                            p2.GetRelativeState(central, curUT, out Vector3d r2Final, out _);
                            double rFinalAvg = (r1Final.magnitude + r2Final.magnitude) * 0.5;
                            double tOrbFinalAvg = 2.0 * Math.PI * Math.Sqrt(Math.Pow(rFinalAvg * 1000.0, 3.0) / (centralMu * 1e9));
                            int nmaxFinal = (tOrbFinalAvg > 10.0) ? (int)Math.Floor(bestLegToF / tOrbFinalAvg + 0.25) : 0;
                            nmaxFinal = Math.Max(0, Math.Min(3, nmaxFinal));
                            if (PykepNative.SolveLambert(r1Final, r2Final, bestLegToF, centralMu, false, nmaxFinal, out _, out Vector3d vArrFinal)) {
                                prevArrVinf = vArrFinal - v2Final;
                            }
                            k++;
                        }

                        ProjectFlightDuration(genome, legCount, lowerBounds, upperBounds, maxDurationSec);
                        var fSol = PykepBridge.BuildFullSolution(genome, fullChain, config, false);
                        if (fSol != null && !double.IsNaN(fSol.TotalDeltaVKms) && fSol.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) {
                            epochSeeds.Add(Tuple.Create(fitnessFunc(fSol), genome));
                        }

                        // 派生微扰变体 (高轻量化探索)
                        double[] perturbations = new double[] { -0.05, 0.05 };
                        foreach (double factor in perturbations) {
                            var pGene = (double[])genome.Clone();
                            for (int d = 1; d <= legCount; d++) {
                                if (d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                                    continue;
                                }
                                double spanD = upperBounds[d] - lowerBounds[d];
                                pGene[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], pGene[d] + factor * spanD * 0.25));
                            }
                            ProjectFlightDuration(pGene, legCount, lowerBounds, upperBounds, maxDurationSec);
                            var pSol = PykepBridge.BuildFullSolution(pGene, fullChain, config, false);
                            if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) {
                                epochSeeds.Add(Tuple.Create(fitnessFunc(pSol), pGene));
                            }
                        }
                    }
                }

                // 普适动力学历元内多天体借力微步抛光 (Per-Epoch Memetic Polish):
                // 前向步进合成的候选已锁定物理共振槽与借力几何，轻量对 top 候选执行极速坐标微调
                if (epochSeeds.Count > 0) {
                    var topEpoch = epochSeeds
                        .OrderBy(t => t.Item1)
                        .ThenBy(t => t.Item2, MGASolutionOrdering.GenomeComparer.Instance)
                        .Take(Math.Min(3, epochSeeds.Count))
                        .ToList();

                    foreach (var seedTuple in topEpoch) {
                        var curG = (double[])seedTuple.Item2.Clone();
                        double bestF = seedTuple.Item1;
                        int passes = 1;

                        for (int pass = 0; pass < passes; pass++) {
                            bool improved = false;
                            for (int d = 1; d <= legCount; d++) {
                                double step = Math.Max(0.1 * 86400.0, curG[d] * 0.005);
                                double origVal = curG[d];
                                double[] signs = new double[] { -1.0, 1.0 };
                                for (int sIdx = 0; sIdx < signs.Length; sIdx++) {
                                    double candVal = origVal + signs[sIdx] * step;
                                    if (candVal < lowerBounds[d] || candVal > upperBounds[d]) continue;
                                    curG[d] = candVal;
                                    var candSol = PykepBridge.BuildFullSolution(curG, fullChain, config, false);
                                    if (candSol != null && !double.IsNaN(candSol.TotalDeltaVKms)) {
                                        double candF = fitnessFunc(candSol);
                                        if (candF < bestF) {
                                            bestF = candF;
                                            origVal = candVal;
                                            improved = true;
                                        }
                                    }
                                }
                                curG[d] = origVal;
                            }

                            if (optDsmEta) {
                                for (int ek = 0; ek < legCount; ek++) {
                                    int geneD = 1 + legCount + ek;
                                    double stepEta = 0.05;
                                    double origEta = curG[geneD];
                                    double[] signsEta = new double[] { -1.0, 1.0 };
                                    for (int sIdx = 0; sIdx < signsEta.Length; sIdx++) {
                                        double candEta = origEta + signsEta[sIdx] * stepEta;
                                        if (candEta < lowerBounds[geneD] || candEta > upperBounds[geneD]) continue;
                                        curG[geneD] = candEta;
                                        var candSol = PykepBridge.BuildFullSolution(curG, fullChain, config, false);
                                        if (candSol != null && !double.IsNaN(candSol.TotalDeltaVKms)) {
                                            double candF = fitnessFunc(candSol);
                                            if (candF < bestF) {
                                                bestF = candF;
                                                origEta = candEta;
                                                improved = true;
                                            }
                                        }
                                    }
                                    curG[geneD] = origEta;
                                }
                            }

                            if (!improved) break;
                        }

                        ProjectFlightDuration(curG, legCount, lowerBounds, upperBounds, maxDurationSec);
                        stagedSeeds.Add(Tuple.Create(bestF, curG));
                    }
                }

                perEpochCombos[epIdx] = localCombos;
            });

            // ---- 确定性合并: 按 epIdx 升序 (即历元先后) 重放各历元的本地结果 ----
            // "首次出现者胜出"的去重语义被固定到历元顺序上, 与线程调度无关。
            for (int e = 0; e < perEpochCombos.Length; e++) {
                var lc = perEpochCombos[e];
                if (lc == null) continue;
                foreach (var t in lc) {
                    if (!cachedBlockCombos.TryGetValue(t.Item1, out var comboList)) {
                        comboList = new List<double[]>();
                        cachedBlockCombos[t.Item1] = comboList;
                    }
                    if (!comboList.Any(existing => existing.SequenceEqual(t.Item2))) {
                        comboList.Add(t.Item2);
                    }
                }
            }

            // 种子去重与多历元空间分散覆盖:
            // 避免单一历元的微扰变体占据全部种子槽, 保证发射窗口各代表性开普勒几何分支均衡入选种群
            var sortedStaged = stagedSeeds
                .OrderBy(t => t.Item1)
                .ThenBy(t => t.Item2, MGASolutionOrdering.GenomeComparer.Instance)
                .ToList();

            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            var epochSeedCount = new Dictionary<long, int>();
            int maxPerEpoch = Math.Max(3, 48 / Math.Max(1, sampleEpochCount));

            // 第一轮: 各历元均衡择优录取 (基于历元 1.0天与各段航时 2.0天离散网格去重)
            foreach (var ss in sortedStaged) {
                var g = ss.Item2;
                if (g == null || g.Length == 0) continue;

                long epochBucket = (long)Math.Round(g[0] / (86400.0 * 1.0));
                string key = $"{epochBucket}_" + string.Join("_", g.Skip(1).Take(legCount).Select(v => Math.Round(v / (86400.0 * 2.0)).ToString()));
                if (selectedKeys.Contains(key)) continue;

                epochSeedCount.TryGetValue(epochBucket, out int cnt);
                if (cnt < maxPerEpoch) {
                    selectedKeys.Add(key);
                    seedGenomes.Add(g);
                    epochSeedCount[epochBucket] = cnt + 1;
                    if (seedGenomes.Count >= 48) break;
                }
            }

            // 第二轮: 若尚未填满 48 槽，由全局最优且未去重的其余种子补齐
            if (seedGenomes.Count < 48) {
                foreach (var ss in sortedStaged) {
                    var g = ss.Item2;
                    if (g == null || g.Length == 0) continue;
                    long epochBucket = (long)Math.Round(g[0] / (86400.0 * 1.0));
                    string key = $"{epochBucket}_" + string.Join("_", g.Skip(1).Take(legCount).Select(v => Math.Round(v / (86400.0 * 2.0)).ToString()));
                    if (!selectedKeys.Contains(key)) {
                        selectedKeys.Add(key);
                        seedGenomes.Add(g);
                        if (seedGenomes.Count >= 48) break;
                    }
                }
            }
            } // end Scope 前向步进引力偏折动力学合成

            // 统一按适应度对全部种子执行全局优选排序，采用批量化动力学评估提速
            using (MGAStepProfiler.Scope("DE注种: 全局种子适应度评估与优选"))
            {
            if (seedGenomes.Count > 0) {
                if (maxDurationSec > 0) {
                    foreach (var g in seedGenomes) {
                        ProjectFlightDuration(g, legCount, lowerBounds, upperBounds, maxDurationSec);
                    }
                }
                var batchSols = PykepBridge.BuildBatchSolutions(seedGenomes, fullChain, config);
                var scoredSeeds = new List<Tuple<double, double[]>>(seedGenomes.Count);
                for (int si = 0; si < seedGenomes.Count; si++) {
                    var s = (batchSols != null && si < batchSols.Count) ? batchSols[si] : null;
                    double f = (s != null && !double.IsNaN(s.TotalDeltaVKms)) ? fitnessFunc(s) : 1e9;
                    scoredSeeds.Add(Tuple.Create(f, seedGenomes[si]));
                }
                var ordered = scoredSeeds
                    .OrderBy(t => t.Item1)
                    .ThenBy(t => t.Item2, MGASolutionOrdering.GenomeComparer.Instance)
                    .Select(t => t.Item2)
                    .ToList();
                seedGenomes.Clear();
                seedGenomes.AddRange(ordered);
            }
            }

            if (seedGenomes.Count > 0) {
                for (int si = 0; si < Math.Min(3, seedGenomes.Count); si++) {
                    var sSol = PykepBridge.BuildFullSolution(seedGenomes[si], fullChain, config, false);
                    if (sSol != null) {
                        KSPGMATLog.Info("Solver", $"     [种子 #{si+1}] 发射={GMATClient.FormatUtcGregorian(sSol.DepartureUT)}, 始发={sSol.LaunchDeltaVKms * 1000.0:F1} m/s, DSM={sSol.TotalDsmDeltaVKms * 1000.0:F1} m/s, 总ΔV={sSol.TotalDeltaVKms * 1000.0:F1} m/s, 适应度={fitnessFunc(sSol):F1}");
                        if (sSol.Legs != null) {
                            for (int li = 0; li < sSol.Legs.Count; li++) {
                                var l = sSol.Legs[li];
                                double dsm = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                                KSPGMATLog.Info("Solver", $"       -> Leg #{li+1}: {l.FromBody} -> {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsm:F1} m/s, FB_dv={l.FlybyDeltaVKms*1000.0:F1} m/s");
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// 启发式注种：节点共面与开普勒离散共振组合扫描
        /// </summary>
        private static List<double[]> GenerateAnalyticalSeedGenomes(
            MGAConfig config,
            List<string> fullChain,
            int dim,
            int legCount,
            bool optDsmEta,
            double winStartUT,
            double winEndUT,
            string central,
            double centralMu,
            double[] lowerBounds,
            double[] upperBounds,
            double[] nominalLegToFSec,
            Func<MGASolution, double> fitnessFunc)
        {
            var seedGenomes = new List<double[]>();

            // 0. Stage 2 会合特征面波谷注种 (Stage 2 Porkchop Valley Direct Seeding):
            // 将 Stage 2 GPU/CPU 特征面扫描定位的极低能量波谷 (BestEpochUT, NominalToFDays)
            // 连同邻域摄动网格精准注入 DE 初始种子池，打通 Stage 2 与 Stage 3 动力学闭环
            if (config != null && config.SeedEpochUT >= lowerBounds[0] && config.SeedEpochUT <= upperBounds[0]) {
                double seedUT = config.SeedEpochUT;
                double nomTof1 = (config.SeedToFDays > 0)
                    ? config.SeedToFDays * 86400.0
                    : ((nominalLegToFSec != null && nominalLegToFSec.Length > 0 && nominalLegToFSec[0] > 0) ? nominalLegToFSec[0] : (lowerBounds[1] + upperBounds[1]) * 0.5);

                double[] utOffsets = new double[] { 0.0, -1.0 * 86400.0, 1.0 * 86400.0, -3.0 * 86400.0, 3.0 * 86400.0, -7.0 * 86400.0, 7.0 * 86400.0, -15.0 * 86400.0, 15.0 * 86400.0 };
                double[] tofFracs = new double[] { 1.0, 0.95, 1.05, 0.90, 1.10, 0.85, 1.15 };

                foreach (double dt in utOffsets) {
                    double depUT = Math.Max(lowerBounds[0], Math.Min(upperBounds[0], seedUT + dt));
                    foreach (double tf in tofFracs) {
                        double leg1Tof = Math.Max(lowerBounds[1], Math.Min(upperBounds[1], nomTof1 * tf));
                        var g = new double[dim];
                        g[0] = depUT;
                        g[1] = leg1Tof;
                        for (int d = 2; d < dim; d++) {
                            if (d < 1 + legCount) {
                                g[d] = (nominalLegToFSec != null && d - 1 < nominalLegToFSec.Length) ? nominalLegToFSec[d - 1] : (lowerBounds[d] + upperBounds[d]) * 0.5;
                            } else {
                                int dsmIdx = d - (1 + legCount);
                                g[d] = (config.DsmEtas != null && dsmIdx < config.DsmEtas.Count && config.DsmEtas[dsmIdx] > 0)
                                    ? config.DsmEtas[dsmIdx]
                                    : config.DsmEtaDefault;
                            }
                        }
                        seedGenomes.Add(g);
                    }
                }
            }

            // 启发式注种：若处于模式 2 (锁定倾角与 RAAN) 且为中心天体转移 (如 Earth -> Moon)
            // 预先沿锁定停泊轨道面扫描目标天体零点面外偏角交点，将精准共面发射时刻注种至初始种群
            if (config != null && (config.DepartureMode == MGADepartureMode.InOrbitLockedOrbit || config.LockDepartureInclination)
                && config.DeparturePeAKm > 0 && Math.Abs(config.DeparturePeAKm - config.DepartureApAKm) < 100.0) {
                double scanSpanSec = Math.Min(30.0 * 86400.0, winEndUT - winStartUT);
                if (PykepNative.FindNodalCrossing(fullChain[1], central, winStartUT, scanSpanSec, config.DepartureInclinationDeg, config.DepartureRAANDeg, out double tNode, out _)) {
                    double nomTofSec = (nominalLegToFSec != null && nominalLegToFSec.Length > 0 && nominalLegToFSec[0] > 0) ? nominalLegToFSec[0] : (lowerBounds[1] + upperBounds[1]) * 0.5;
                    double depSeedUT = tNode - nomTofSec;
                    KSPGMATLog.Debug("Solver", $"       [DEBUG NodalCrossing] tNode={GMATClient.FormatUtcGregorian(tNode)}, nomTofSec={nomTofSec/86400.0:F2}d, depSeedUT={GMATClient.FormatUtcGregorian(depSeedUT)}, inBounds={depSeedUT >= lowerBounds[0] && depSeedUT <= upperBounds[0]}");
                    if (depSeedUT < lowerBounds[0] || depSeedUT > upperBounds[0]) {
                        // 寻找下一个升降交点 (~13.6 天后)
                        if (PykepNative.FindNodalCrossing(fullChain[1], central, tNode + 1.0 * 86400.0, scanSpanSec, config.DepartureInclinationDeg, config.DepartureRAANDeg, out double nextNode, out _)) {
                            depSeedUT = nextNode - nomTofSec;
                            KSPGMATLog.Debug("Solver", $"       [DEBUG NextNode] nextNode={GMATClient.FormatUtcGregorian(nextNode)}, depSeedUT={GMATClient.FormatUtcGregorian(depSeedUT)}, inBounds={depSeedUT >= lowerBounds[0] && depSeedUT <= upperBounds[0]}");
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
                            var testSol = PykepBridge.BuildFullSolution(g, fullChain, config, false);
                            double testFit = testSol != null ? fitnessFunc(testSol) : 1e9;
                            double testInc = (testSol != null && testSol.Legs != null && testSol.Legs.Count > 0) ? testSol.Legs[0].EjectionInclinationDeg : -1;
                            double testDv = testSol != null ? testSol.LaunchDeltaVKms * 1000.0 : -1;
                            KSPGMATLog.Debug("Solver", $"       [DEBUG Nodal Seed] dep={GMATClient.FormatUtcGregorian(depCand)}, tof={tofCand/86400.0:F2}d, EjectionInc={testInc:F2}°, LaunchDv={testDv:F1} m/s, Fit={testFit:F1}");
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
                        // 全部低阶有理共振槽候选, 由下方 ToF 边界过滤，并在全域发射窗口多历元均匀注种
                        double[] resonanceRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;
                        double[] seedEpochFracs = new double[] { 0.0, 0.15, 0.35, 0.5, 0.65, 0.85, 1.0 };
                        foreach (double r in resonanceRatios) {
                            double resTofSec = tOrbSec * r;
                            if (resTofSec >= lowerBounds[1 + k] && resTofSec <= upperBounds[1 + k]) {
                                foreach (double eFrac in seedEpochFracs) {
                                    var resGenome = new double[dim];
                                    resGenome[0] = lowerBounds[0] + eFrac * (upperBounds[0] - lowerBounds[0]);
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
            }

            // 共振组合扫描注种 (Resonance-Combo Scan Seeding):
            // 对链路中所有同星共振腿的周期比 {1:1, 3:2, 2:1, 3:1, 4:1} 做离散组合扫描,
            // 组合出"全程共振同步"的完整基因组 (所有共振腿同时落在精确有理槽上),
            // 以一次原生 BuildFullSolution 的 TotalΔV 为种子评分, 注入相位闭环最好的 K 个组合。
            using (MGAStepProfiler.Scope("DE注种: 全程开普勒共振组合与相位闭环扫描"))
            {
                var sameBodyLegs = new List<int>();
                for (int k = 0; k < legCount; k++) {
                    if (fullChain[k].Equals(fullChain[k + 1], StringComparison.OrdinalIgnoreCase)) sameBodyLegs.Add(k);
                }
                if (sameBodyLegs.Count > 0) {
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
                            double[] legRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;
                            foreach (double r in legRatios) {
                                double resTofSec = legPeriods[si] * r;
                                if (resTofSec >= lowerBounds[1 + k] && resTofSec <= upperBounds[1 + k]) opts.Add(r);
                            }
                        }
                        ratioOptionsPerLeg.Add(opts);
                    }

                    // 1. 将链路中所有同星共振腿按天体连续性划分为独立的物理共振块
                    var blocks = new List<List<int>>();
                    int curBlockStart = 0;
                    for (int si = 0; si < sameBodyLegs.Count; si++) {
                        if (si > 0) {
                            int prevLeg = sameBodyLegs[si - 1];
                            int curLeg = sameBodyLegs[si];
                            bool isConsecutive = (curLeg == prevLeg + 1) &&
                                fullChain[curLeg].Equals(fullChain[prevLeg], StringComparison.OrdinalIgnoreCase);
                            if (!isConsecutive) {
                                var b = new List<int>();
                                for (int bi = curBlockStart; bi < si; bi++) b.Add(bi);
                                blocks.Add(b);
                                curBlockStart = si;
                            }
                        }
                    }
                    var lastB = new List<int>();
                    for (int bi = curBlockStart; bi < sameBodyLegs.Count; bi++) lastB.Add(bi);
                    blocks.Add(lastB);

                    // 2. 为每个物理共振块独立生成高质量天体力学共振候选组合:
                    var blockCandidates = new List<List<double[]>>();
                    foreach (var b in blocks) {
                        var bCands = new List<double[]>();
                        if (b.Count == 1) {
                            int si = b[0];
                            var opts = ratioOptionsPerLeg[si];
                            var sortedOpts = opts.OrderBy(r => Math.Abs(r - 1.0)).ToList();
                            int takeN = (blocks.Count == 1) ? Math.Min(32, sortedOpts.Count) : Math.Min(4, sortedOpts.Count);
                            foreach (double r in sortedOpts.Take(takeN)) {
                                bCands.Add(new double[] { r });
                            }
                        } else {
                            var localCombos = new List<double[]>();
                            Action<int, double[]> buildLocal = null;
                            buildLocal = (legIdx, cur) => {
                                if (legIdx == b.Count) {
                                    localCombos.Add((double[])cur.Clone());
                                    return;
                                }
                                int si = b[legIdx];
                                foreach (double r in ratioOptionsPerLeg[si]) {
                                    cur[legIdx] = r;
                                    buildLocal(legIdx + 1, cur);
                                    if (localCombos.Count >= 2048) return;
                                }
                            };
                            buildLocal(0, new double[b.Count]);

                            var monoInc = localCombos.Where(c => {
                                for (int i = 1; i < c.Length; i++) if (c[i] < c[i - 1]) return false;
                                return true;
                            }).ToList();

                            var monoDec = localCombos.Where(c => {
                                for (int i = 1; i < c.Length; i++) if (c[i] > c[i - 1]) return false;
                                return true;
                            }).ToList();

                            var equalSeq = localCombos.Where(c => {
                                for (int i = 1; i < c.Length; i++) if (Math.Abs(c[i] - c[0]) > 1e-4) return false;
                                return true;
                            }).ToList();

                            var intMonoInc = monoInc.Where(c => c.All(r => Math.Abs(r - Math.Round(r)) < 1e-4)).ToList();
                            var otherMono = monoInc.Except(intMonoInc).ToList();

                            var selected = new List<double[]>();
                            selected.AddRange(intMonoInc);
                            selected.AddRange(equalSeq);
                            selected.AddRange(otherMono.Take(20));
                            selected.AddRange(monoDec.Take(15));
                            selected.AddRange(localCombos.OrderBy(c => c.Sum()).Take(15));

                            var distinctCands = new List<double[]>();
                            foreach (var sc in selected) {
                                bool exists = false;
                                foreach (var dc in distinctCands) {
                                    if (dc.Length == sc.Length) {
                                        bool eq = true;
                                        for (int ei = 0; ei < dc.Length; ei++) {
                                            if (Math.Abs(dc[ei] - sc[ei]) > 1e-4) { eq = false; break; }
                                        }
                                        if (eq) { exists = true; break; }
                                    }
                                }
                                if (!exists) distinctCands.Add(sc);
                                if (distinctCands.Count >= 64) break;
                            }
                            bCands.AddRange(distinctCands);
                        }
                        blockCandidates.Add(bCands);
                    }

                    // 3. 跨块组装全局共振组合
                    var combos = new List<List<double>>();
                    Action<int, List<double>> assembleBlocks = null;
                    assembleBlocks = (bIdx, cur) => {
                        if (bIdx == blocks.Count) {
                            combos.Add(new List<double>(cur));
                            return;
                        }
                        foreach (var cand in blockCandidates[bIdx]) {
                            cur.AddRange(cand);
                            assembleBlocks(bIdx + 1, cur);
                            cur.RemoveRange(cur.Count - cand.Length, cand.Length);
                            if (combos.Count >= 1024) return;
                        }
                    };
                    assembleBlocks(0, new List<double>());

                    var comboSeeds = new List<Tuple<double, double[]>>();
                    double[] comboEpochs = new double[] { 0.0, -0.2, 0.2, -0.4, 0.4 };
                    int comboEvalCount = 0;
                    foreach (var combo in combos) {
                        foreach (double epochFrac in comboEpochs) {
                            if (comboEvalCount >= 2500) break;
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
                                double cFit = fitnessFunc(comboSol);
                                comboSeeds.Add(Tuple.Create(cFit, g));
                            }
                        }
                    }
                    foreach (var cs in comboSeeds.OrderBy(t => t.Item1).Take(24)) {
                        seedGenomes.Add(cs.Item2);
                    }
                }
            }

            double maxDurationSec = (config != null && config.EnableMaxDuration && config.MaxDurationDays > 0) ? config.MaxDurationDays * 86400.0 : 0.0;
            if (maxDurationSec > 0) {
                foreach (var g in seedGenomes) {
                    ProjectFlightDuration(g, legCount, lowerBounds, upperBounds, maxDurationSec);
                }
            }

            return seedGenomes;
        }

        /// <summary>
        /// 初始种群染色体分配与动力学初评 (支持 GPU DirectCompute CS 5.0 与多核 CPU 弹性加速)
        /// </summary>
        private static void InitializePopulation(
            double[][] pop,
            double[] fitness,
            int popSize,
            int subPopSize,
            int dim,
            int legCount,
            bool optDsmEta,
            bool hasResonantBlock,
            double centralMu,
            string central,
            double[] lowerBounds,
            double[] upperBounds,
            List<string> fullChain,
            MGAConfig config,
            List<double[]> seedGenomes,
            Dictionary<int, List<double[]>> cachedBlockCombos,
            Func<MGASolution, double> fitnessFunc,
            System.Random random)
        {
            int maxSeedsPerIsland = Math.Min(seedGenomes.Count, Math.Max(4, (int)(subPopSize * 0.15)));
            double maxDurationSec = (config != null && config.EnableMaxDuration && config.MaxDurationDays > 0) ? config.MaxDurationDays * 86400.0 : 0.0;
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
                                    double maxOffset = (config != null && config.ResonantMaxOffsetDays > 0)
                                        ? config.ResonantMaxOffsetDays * 86400.0
                                        : Math.Max(5.0 * 86400.0, tOrb * 0.10);

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
                            if (d > legCount) {
                                double defEta = (config != null && config.DsmEtaDefault > 0) ? config.DsmEtaDefault : 0.5;
                                pop[i][d] = Math.Max(0.1, Math.Min(0.9, defEta + (random.NextDouble() - 0.5) * 0.2));
                            } else {
                                pop[i][d] = lowerBounds[d] + random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                            }
                            d++;
                        }
                    }
                    ProjectFlightDuration(pop[i], legCount, lowerBounds, upperBounds, maxDurationSec);
                }

                // 初始种群批量动力学评估 (支持 GPU DirectCompute CS 5.0 与多核 CPU 弹性加速)
                float[] initLosses = null;
                if (!hasResonantBlock && MGAGpuAccelerationManager.EvaluateDePopulationGpu(pop.ToList(), fullChain, config, centralMu, optDsmEta, out initLosses, fitnessFunc)) {
                    for (int i = 0; i < popSize; i++) {
                        fitness[i] = initLosses[i];
                    }
                } else {
                    var initSols = PykepBridge.BuildBatchSolutions(pop.ToList(), fullChain, config);
                    for (int i = 0; i < popSize; i++) {
                        fitness[i] = (initSols != null && i < initSols.Count && initSols[i] != null && !double.IsNaN(initSols[i].TotalDeltaVKms))
                            ? fitnessFunc(initSols[i])
                            : 1e9;
                    }
                }
                CPUMonitor.RecordEvaluation(popSize);
            } // end Scope 初始染色体分配与初评
        }
        /// <summary>
        /// 构建连续同星共振块物理拓扑映射 (Contiguous Resonant Block Map)
        /// </summary>
        private static Dictionary<int, (int rStart, int rEnd, int bLen, string body, double tOrb)> BuildResonanceBlockMap(
            List<string> fullChain, int legCount, string central, double centralMu)
        {
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
            return resBlockMap;
        }
    }
}
