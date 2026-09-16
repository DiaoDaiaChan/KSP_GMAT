// ============================================================================
// MGADifferentialEvolution.Refinement
// 帕累托选解 / Hooke-Jeeves 打磨 / 硬门禁 / 适应度 (部分拆分部)

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
        /// 差分进化后处理：帕累托前沿提取、多样性网格过滤与高保真动力学注水
        /// </summary>
        private static List<MGASolution> ExtractAndEnrichSolutions(
            double[][] pop,
            double[] fitness,
            int popSize,
            List<string> fullChain,
            MGAConfig config)
        {
            var solutions = new List<MGASolution>();

            using (MGAStepProfiler.Scope("DE选解: 帕累托前沿提取与高级动力学注水"))
            {
                var rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();
                var seenKeys = new HashSet<long>();

                double winBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
                double winDsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : winBudgetMs;

                var chosenCandidateIndices = new List<int>();

                // 第一阶段：细粒度网格采样 (12小时发射UT格 + 1天飞行历时格)，初筛帕累托前沿多样性解索引
                // 注意：采用 computeAdvanced: false 快速初筛，完全规避过滤掉个体的昂贵 PrimerVector 与 B-Plane 计算
                foreach (var idx in rankedIndices) {
                    if (!MGAPhysicsLossEngine.IsAcceptableForSelection(fitness[idx])) continue;
                    var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: false);
                    if (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) {
                        double subjectMs = (config.NoInsertionBurn ? (sol.LaunchDeltaVKms + sol.TotalDsmDeltaVKms + sol.TotalFlybyDeltaVKms) : sol.TotalDeltaVKms) * 1000.0;
                        if (winBudgetMs < MGADefaults.MaxPlausibleTotalDvKms * 1000.0 && subjectMs > winBudgetMs) continue;
                        if (winDsmBudgetMs < MGADefaults.MaxPlausibleTotalDvKms * 1000.0 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs)))) continue;
                        if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs) continue;
                        if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                        if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;
                        if (config.EnableMaxDuration && config.MaxDurationDays > 0 && sol.TotalFlightDays > config.MaxDurationDays + 1.0) continue;

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
                        if (!MGAPhysicsLossEngine.IsAcceptableForSelection(fitness[idx])) continue;
                        if (chosenCandidateIndices.Contains(idx)) continue;
                        var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: false);
                        if (sol == null || double.IsNaN(sol.TotalDeltaVKms)) continue;
                        double subjectMs = (config.NoInsertionBurn ? (sol.LaunchDeltaVKms + sol.TotalDsmDeltaVKms + sol.TotalFlybyDeltaVKms) : sol.TotalDeltaVKms) * 1000.0;
                        if (winBudgetMs < MGADefaults.MaxPlausibleTotalDvKms * 1000.0 && subjectMs > winBudgetMs) continue;
                        if (winDsmBudgetMs < MGADefaults.MaxPlausibleTotalDvKms * 1000.0 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs)))) continue;
                        if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs) continue;
                        if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT - 1.0) continue;
                        if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT + 1.0) continue;
                        if (config.EnableMaxDuration && config.MaxDurationDays > 0 && sol.TotalFlightDays > config.MaxDurationDays + 1.0) continue;

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
                                if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) {
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

                solutions = solutions.OrderBy(s => config.NoInsertionBurn 
                    ? (s.LaunchDeltaVKms + s.TotalDsmDeltaVKms + s.TotalFlybyDeltaVKms) 
                    : s.TotalDeltaVKms).ToList();
            } // end Scope DE选解: 帕累托前沿提取与高级动力学注水

            return solutions;
        }
        /// <summary>
        /// 局部坐标下降与 Hooke-Jeeves 模式加速搜索 (Memetic Hooke-Jeeves Pattern Acceleration Polish):
        /// 围绕全局进化沉降的精英染色体，先逐坐标微步探索梯度斜率，并沿改善方向执行倍增步长加速，
        /// 随后执行全维模式跳跃 x_pattern = x + D，快速穿越狭长平缓的兰伯特相位峡谷，将 DSM 压降至物理极限
        /// </summary>
        private static void PolishElitesHookeJeeves(
            double[][] pop,
            double[] fitness,
            int popSize,
            double[] lowerBounds,
            double[] upperBounds,
            List<string> fullChain,
            MGAConfig config,
            Func<MGASolution, double> fitnessFunc,
            Dictionary<int, List<double[]>> cachedBlockCombos,
            Dictionary<int, (int rStart, int rEnd, int bLen, string body, double tOrb)> resBlockMap,
            string central,
            double centralMu,
            int dim,
            int legCount)
        {
            using (MGAStepProfiler.Scope("DE打磨: Hooke-Jeeves 模式加速极值搜索"))
            {
                var rankedIndices = Enumerable.Range(0, popSize).OrderBy(idx => fitness[idx]).ToList();
                double maxDurationSec = (config != null && config.EnableMaxDuration && config.MaxDurationDays > 0) ? config.MaxDurationDays * 86400.0 : 0.0;
                int polishCount = Math.Min(2, rankedIndices.Count);
                for (int pIdx = 0; pIdx < polishCount; pIdx++) {
                    int indivIdx = rankedIndices[pIdx];
                    double curFit = fitness[indivIdx];
                    // 若染色体严重发散不可行 (损失极大)，不浪费时间执行单维微调
                    if (MGAPhysicsLossEngine.IsDeeplyInfeasible(curFit)) continue;
                    // 若为第 2 个候选且比第 1 个候选差 100% 以上，无需再花大量时间打磨次要解
                    if (pIdx > 0 && curFit > fitness[rankedIndices[0]] * 2.0) continue;

                    var curGene = (double[])pop[indivIdx].Clone();
                    double initialFit = curFit;

                    // 0. 活动子空间贝叶斯高斯过程局部抛光 (Active Subspace Bayesian Optimization): 挖掘主敏感梯度加速收敛
                    try {
                        var polishedBayesGene = MGABayesianSubspacePolisher.Polish(
                            curGene,
                            lowerBounds,
                            upperBounds,
                            probePt => {
                                var pSol = PykepBridge.BuildFullSolution(probePt, fullChain, config, computeAdvanced: false);
                                return fitnessFunc(pSol);
                            },
                            maxEvaluations: 20);

                        var bayesSol = PykepBridge.BuildFullSolution(polishedBayesGene, fullChain, config, computeAdvanced: false);
                        double bayesFit = fitnessFunc(bayesSol);
                        if (bayesFit < curFit) {
                            curFit = bayesFit;
                            curGene = polishedBayesGene;
                        }
                    } catch (Exception ex) {
                        KSPGMATLog.Debug("Solver", $"Bayesian polishing fallback: {ex.Message}");
                    }

                    int maxPolishIter = (pIdx == 0) ? 2 : 1;

                    for (int iter = 0; iter < maxPolishIter; iter++) {
                        var baseGene = (double[])curGene.Clone();
                        double baseFit = curFit;
                        bool anyImproved = false;

                        // 1. 逐坐标探索移动 (Exploratory Move)
                        for (int d = 0; d < dim; d++) {
                            bool isResLeg = (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase));
                            double resOffsetMax = (config != null && config.ResonantMaxOffsetDays > 0)
                                ? config.ResonantMaxOffsetDays * 86400.0
                                : Math.Max(3.0 * 86400.0, (upperBounds[d] - lowerBounds[d]) * 0.05);
                            double span = isResLeg ? resOffsetMax : (upperBounds[d] - lowerBounds[d]);
                            double[] probeFactors;
                            if (isResLeg) {
                                probeFactors = new double[] { -0.5, 0.5, -0.2, 0.2, -0.05, 0.05 };
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
                                        double cFit = fitnessFunc(comboSol);
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
                                double[] resRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;
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
                                            double testFit = fitnessFunc(testSol);
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
                                                    double compFit = fitnessFunc(compSol);
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
                                double testFit = fitnessFunc(testSol);
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
                                        double compFit = fitnessFunc(compSol);
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
                                    // 自适应步长加速: 依据适应度相对改善率动态调节探索倍数 (1.2 ~ 1.8)
                                    double relImprovement = Math.Max(0.0, (curFit - bestDFit) / Math.Max(1e-4, Math.Abs(curFit)));
                                    double stepMultiplier = 1.2 + Math.Min(0.6, relImprovement * 2.0);
                                    double accelFactor = bestFactor * stepMultiplier;
                                    for (int accStep = 0; accStep < 2; accStep++) {
                                        double testVal = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], curGene[d] + accelFactor * span));
                                        if (Math.Abs(testVal - bestDVal) < 1e-6) break;

                                        var testGene = (double[])curGene.Clone();
                                        testGene[d] = testVal;
                                        var testSol = PykepBridge.BuildFullSolution(testGene, fullChain, config, computeAdvanced: false);
                                        double testFit = fitnessFunc(testSol);
                                        if (testFit < curFit) {
                                            double stepGain = (curFit - testFit) / Math.Max(1e-4, Math.Abs(curFit));
                                            curFit = testFit;
                                            curGene[d] = testVal;
                                            accelFactor *= (1.1 + Math.Min(0.5, stepGain * 2.0));
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
                                        double[] polishRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;
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
                                        double resOffset = (config != null && config.ResonantMaxOffsetDays > 0) ? config.ResonantMaxOffsetDays * 86400.0 : Math.Max(5.0 * 86400.0, tOrb * 0.10);
                                        patVal = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], Math.Max(bestResCenter - resOffset, Math.Min(bestResCenter + resOffset, patVal))));
                                    }
                                }
                                patternGene[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], patVal));
                            }
                            ProjectFlightDuration(patternGene, legCount, lowerBounds, upperBounds, maxDurationSec);

                            var patSol = PykepBridge.BuildFullSolution(patternGene, fullChain, config, computeAdvanced: false);
                            double patFit = fitnessFunc(patSol);
                            if (patFit < curFit) {
                                curGene = patternGene;
                                curFit = patFit;
                            }
                        } else {
                            break;
                        }
                    }

                    ProjectFlightDuration(curGene, legCount, lowerBounds, upperBounds, maxDurationSec);
                    pop[indivIdx] = curGene;
                    fitness[indivIdx] = curFit;

                    double dvSavedKms = (curFit < initialFit && MGAPhysicsLossEngine.IsAcceptableForSelection(initialFit)) ? Math.Max(0.0, (initialFit - curFit) / 1000.0) : 0.0;
                    Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordSelfHealing(curFit < initialFit, dvSavedKms);
                }
            } // end Scope DE打磨: Hooke-Jeeves 模式加速极值搜索
        }
        /// <summary>
        /// 硬门禁合规审计：无论进化期采用何种罚函数，最终上报解都必须逐项硬校验
        /// (总ΔV ≤ VesselMaxDeltaVMs、总DSM ≤ MaxDsmDeltaVMs、发射 ≤ LaunchDeltaVCeilingMs)，
        /// 超标值显式记录，绝不静默冒充达标。
        /// 遵循两大场景口径：
        /// 场景1 (全任务捕获): 考核 Launch + DSM + Insertion + Flyby;
        /// 场景2 (脱离与巡航/飞掠): 考核 Launch + DSM + Flyby.
        /// </summary>
        private static void MarkHardGateCompliance(MGASolution sol, MGAConfig config) {
            if (sol == null) return;
            double launchCeiling = (config != null) ? config.LaunchDeltaVCeilingMs : 0.0;
            double dsmBudget = (config != null && config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs
                : ((config != null && config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 0.0);
            double totalBudget = (config != null && config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 0.0;

            double launchMs = sol.LaunchDeltaVKms * 1000.0;
            double dsmMs = sol.TotalDsmDeltaVKms * 1000.0;
            double subjectMs = (config != null && config.NoInsertionBurn 
                ? (sol.LaunchDeltaVKms + sol.TotalDsmDeltaVKms + sol.TotalFlybyDeltaVKms) 
                : sol.TotalDeltaVKms) * 1000.0;

            sol.LaunchOverBudgetMs = (launchCeiling > 0) ? Math.Max(0.0, launchMs - launchCeiling) : 0.0;
            sol.DsmOverBudgetMs = (dsmBudget > 0) ? Math.Max(0.0, dsmMs - dsmBudget) : 0.0;
            sol.TotalOverBudgetMs = (totalBudget > 0) ? Math.Max(0.0, subjectMs - totalBudget) : 0.0;

            bool launchOk = launchCeiling <= 0 || launchMs <= launchCeiling;
            bool dsmOk = dsmBudget <= 0 || dsmMs <= dsmBudget;
            bool totalOk = totalBudget <= 0 || subjectMs <= totalBudget;
            bool tofOk = !config.EnableMaxDuration || config.MaxDurationDays <= 0 || sol.TotalFlightDays <= config.MaxDurationDays + 1.0;
            sol.MeetsHardDvGate = launchOk && dsmOk && totalOk && tofOk;
        }
        /// <summary>
        /// DE 适应度目标函数: 100% 委托 MGAPhysicsLossEngine 连续物理损失场引擎
        /// (包含 C1 连续光滑二次障碍、L1 稀疏机动正则项、严格零动力飞掠凸惩罚)
        /// </summary>
        private static double ComputeFitness(MGASolution sol, MGAConfig config) {
            return MGAPhysicsLossEngine.ComputeLoss(sol, config);
        }
    }
}
