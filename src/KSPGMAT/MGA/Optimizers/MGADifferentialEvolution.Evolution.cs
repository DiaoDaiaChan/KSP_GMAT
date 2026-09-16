// ============================================================================
// MGADifferentialEvolution.Evolution
// 异构多岛差分进化主代数循环 (部分拆分部)

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
        /// 异构多岛差分进化 (Multi-Island DE) 主代数循环全过程
        /// 集成：强化学习 MAB 自适应算子调控、ASA 模拟退火并行回火、环形拓扑精英迁移、MBH 单调盆地跳跃、IDEA 膨胀超球禁区阻断与引导重启
        /// </summary>
        private static void RunEvolutionLoop(
            double[][] pop,
            double[] fitness,
            int popSize,
            int subPopSize,
            int islandCount,
            int maxGens,
            int dim,
            int legCount,
            bool optDsmEta,
            bool hasResonantBlock,
            string central,
            double centralMu,
            double[] lowerBounds,
            double[] upperBounds,
            List<string> fullChain,
            MGAConfig config,
            Func<MGASolution, double> fitnessFunc,
            MGAAugmentedLagrangian alm,
            Dictionary<int, (int rStart, int rEnd, int bLen, string body, double tOrb)> resBlockMap,
            Dictionary<int, List<double[]>> cachedBlockCombos,
            Action<float, string> onProgress,
            CancellationToken ct)
        {
            // 强化学习 MAB 自适应变异算子与超参数调控器池 (Multi-Armed Bandit Tuners)
            var banditTuners = new MGABanditHyperparameterTuner[islandCount];
            for (int isl = 0; isl < islandCount; isl++) {
                banditTuners[isl] = new MGABanditHyperparameterTuner();
            }

            using (MGAStepProfiler.Scope("DE进化: 代数主循环全过程"))
            {
            double maxDurationSec = (config != null && config.EnableMaxDuration && config.MaxDurationDays > 0) ? config.MaxDurationDays * 86400.0 : 0.0;
            double lastStagFit = double.MaxValue;
            int stagGens = 0;
            // Vasile et al. 2011 膨胀差分进化 (IDEA: Inflationary Differential Evolution)
            // 归档历史局部极小不动点超球禁区：B(x*, r_infl)，阻断 DE 种群重复跌入已知局部伪极值吸引盆地
            var inflationArchive = new List<(double[] attractor, double rInfl)>();
            int ideaRestartCount = 0;
            const int maxIdeaRestarts = 2; // 最多允许 2 轮精英引导重启，兼顾深度全局探索与实时算力开销

            for (int gen = 0; gen < maxGens; gen++) {
                if (ct.IsCancellationRequested) break;

                using (MGAStepProfiler.Scope("DE代数: 异构岛屿变异交叉与评估"))
                {
                    // 1. 生成整个种群的候选染色体 (Mutation, Crossover, Resonant Snapping)
                    var candidates = new List<double[]>(popSize);
                    var selectedStrategies = new DEMutationStrategy[popSize];
                    var selectedF = new double[popSize];
                    var selectedCR = new double[popSize];
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

                        // 强化学习 MAB 自适应变异策略选择与扰动因子 (Multi-Armed Bandit Strategy Selection)
                        var tuner = banditTuners[islandId];
                        var strat = tuner.SelectStrategy(localRand);
                        selectedStrategies[i] = strat;

                        int a = islandStart + localRand.Next(subPopSize);
                        while (a == i || a == b || a == c) {
                            a = islandStart + localRand.Next(subPopSize);
                        }

                        double F = tuner.CurrentF * (0.85 + 0.3 * localRand.NextDouble());
                        double CR = tuner.CurrentCR * (0.85 + 0.3 * localRand.NextDouble());
                        selectedF[i] = F;
                        selectedCR[i] = CR;
                        int R = localRand.Next(dim);

                        double[] candidate = new double[dim];
                        for (int d = 0; d < dim; d++) {
                            if (d == R || localRand.NextDouble() < CR) {
                                double mutVal;
                                switch (strat) {
                                    case DEMutationStrategy.Best1:
                                        mutVal = pop[pbest][d] + F * (pop[b][d] - pop[c][d]);
                                        break;
                                    case DEMutationStrategy.Rand1:
                                        mutVal = pop[a][d] + F * (pop[b][d] - pop[c][d]);
                                        break;
                                    case DEMutationStrategy.RandToBest1:
                                        mutVal = pop[a][d] + 0.5 * F * (pop[pbest][d] - pop[a][d]) + F * (pop[b][d] - pop[c][d]);
                                        break;
                                    case DEMutationStrategy.CurrentToBest1:
                                    default:
                                        mutVal = pop[i][d] + F * (pop[pbest][d] - pop[i][d]) + F * (pop[b][d] - pop[c][d]);
                                        break;
                                }
                                candidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], mutVal));

                                // 若该坐标属于同星共振航段，吸附至开普勒物理有效有理共振槽 (1:1, 3:2, 2:1, 3:1, 4:1 * T_orb)
                                if (d >= 1 && d <= legCount && fullChain[d - 1].Equals(fullChain[d], StringComparison.OrdinalIgnoreCase)) {
                                    var pBody = SolarSystemCatalog.GetPlanet(fullChain[d]);
                                    double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
                                    double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
                                    if (tOrb > 0) {
                                        double bestResCenter = tOrb;
                                        double minDiff = double.MaxValue;
                                        double[] snapRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;
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
                                        // 离散开普勒共振槽跨越变异 (Resonant Slot Basin Hopping: 自适应退火概率，前期探索后期收敛)
                                        double progress = (double)gen / Math.Max(1, maxGens);
                                        double hopProb = 0.18 * (1.0 - 0.6 * progress); // 0.18 -> 0.072 动态退火
                                        if (localRand.NextDouble() < hopProb && snapRatios.Length > 1) {
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
                                        double maxOffset = (config != null && config.ResonantMaxOffsetDays > 0)
                                            ? config.ResonantMaxOffsetDays * 86400.0
                                            : Math.Max(10.0 * 86400.0, tOrb * 0.20);
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
                                            double maxSnapOffset = (config != null && config.ResonantMaxOffsetDays > 0) ? config.ResonantMaxOffsetDays * 86400.0 : Math.Max(5.0 * 86400.0, blk.tOrb * 0.10);
                                            off = Math.Max(-maxSnapOffset, Math.Min(maxSnapOffset, off));
                                            candidate[cIdx] = Math.Max(lowerBounds[cIdx], Math.Min(upperBounds[cIdx], rCenter + off));
                                        }
                                    }
                                }
                            }
                        }

                        // Vasile 2011 IDEA 排斥超球禁区阻断 (Repulsion Hypersphere Barrier):
                        // 若变异个体跌入已归档的局部伪极值超球禁区内，施加排斥微步推离禁区，阻断反复跌入已知伪极值
                        if (inflationArchive.Count > 0) {
                            for (int ia = 0; ia < inflationArchive.Count; ia++) {
                                var (attractor, rInfl) = inflationArchive[ia];
                                double distSq = 0.0;
                                for (int d = 0; d < dim; d++) {
                                    double span = upperBounds[d] - lowerBounds[d];
                                    if (span > 1e-6) {
                                        double diff = (candidate[d] - attractor[d]) / span;
                                        distSq += diff * diff;
                                    }
                                }
                                double distNorm = Math.Sqrt(distSq / Math.Max(1, dim));
                                if (distNorm < rInfl) {
                                    double pushFactor = (rInfl - distNorm + 0.01);
                                    for (int d = 0; d < dim; d++) {
                                        double span = upperBounds[d] - lowerBounds[d];
                                        double dir = (candidate[d] >= attractor[d]) ? 1.0 : -1.0;
                                        candidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], candidate[d] + dir * pushFactor * span));
                                    }
                                    break;
                                }
                            }
                        }

                        ProjectFlightDuration(candidate, legCount, lowerBounds, upperBounds, maxDurationSec);
                        candidates.Add(candidate);
                    }

                    // 2. DirectCompute HLSL GPU 硬件加速 (统一由 MGAGpuAccelerationManager 全生命周期管理)
                    float[] gpuLosses = null;
                    float[] gLosses = null;
                    if (!hasResonantBlock && MGAGpuAccelerationManager.EvaluateDePopulationGpu(candidates, fullChain, config, centralMu, optDsmEta, out gLosses, fitnessFunc)) {
                        gpuLosses = gLosses;
                    }

                    List<MGASolution> candSols = null;
                    if (gpuLosses == null) {
                        candSols = PykepBridge.BuildBatchSolutions(candidates, fullChain, config);
                    }

                    // 3. 淘汰选择 (Survival Selection): 集成自适应模拟退火与 Metropolis 准则 (Parallel Tempering ASA)
                    // 高温探索岛允许以统计力学概率跨越开普勒势阱假壁垒，低温收敛岛严格执行梯度抛光
                    for (int i = 0; i < popSize; i++) {
                        double candFit = (gpuLosses != null) ? gpuLosses[i] : fitnessFunc(candSols[i]);
                        int islandId = i / subPopSize;
                        double T = MGAPhysicsLossEngine.GetIslandTemperature(gen, maxGens, islandId, islandCount);
                        var localRand = new System.Random(gen * popSize + i + 1337);
                        bool isStrictlyImproved = candFit < fitness[i];
                        double fitGain = Math.Max(0.0, fitness[i] - candFit);

                        if (MGAPhysicsLossEngine.MetropolisAccept(fitness[i], candFit, T, localRand)) {
                            pop[i] = candidates[i];
                            fitness[i] = candFit;
                        }
                        banditTuners[islandId].RecordReward(selectedStrategies[i], fitGain, isStrictlyImproved);
                        if (isStrictlyImproved && fitGain > 0.0) {
                            banditTuners[islandId].RecordSuccessfulParameter(selectedF[i], selectedCR[i], fitGain);
                        }
                    }

                    // 岛屿多样性方差感知与超参数自适应调谐 (基于无量纲归一化特征方差)
                    for (int isl = 0; isl < islandCount; isl++) {
                        int start = isl * subPopSize;
                        double varSum = 0.0;
                        for (int d = 0; d < dim; d++) {
                            double span = upperBounds[d] - lowerBounds[d];
                            if (span <= 1e-12) span = 1.0;
                            double meanVal = 0.0;
                            for (int k = 0; k < subPopSize; k++) meanVal += (pop[start + k][d] - lowerBounds[d]) / span;
                            meanVal /= Math.Max(1, subPopSize);
                            double dVar = 0.0;
                            for (int k = 0; k < subPopSize; k++) {
                                double diff = (pop[start + k][d] - lowerBounds[d]) / span - meanVal;
                                dVar += diff * diff;
                            }
                            varSum += dVar / Math.Max(1, subPopSize);
                        }
                        double avgNormVar = varSum / Math.Max(1, dim);
                        banditTuners[isl].AdaptHyperparameters(gen, maxGens, avgNormVar, stagGens);
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
                            fitness[targetIdx] = fitnessFunc(migSol);
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
                                // 自适应柯西尺度: 随代际从 0.04 退火至 0.01，兼顾大范围跳出局部极值与后期细致探索
                                double genFraction = (double)gen / Math.Max(1, maxGens);
                                double cauchyScale = 0.04 * (1.0 - 0.75 * genFraction);
                                mbhCandidate[d] = Math.Max(lowerBounds[d], Math.Min(upperBounds[d], mbhCandidate[d] + cauchyScale * span * cauchy));
                                hasPerturbed = true;
                            }
                        }
                        if (hasPerturbed) {
                            ProjectFlightDuration(mbhCandidate, legCount, lowerBounds, upperBounds, maxDurationSec);
                            var mbhSol = PykepBridge.BuildFullSolution(mbhCandidate, fullChain, config, computeAdvanced: false);
                            double mbhFit = fitnessFunc(mbhSol);
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
                    KSPGMATLog.Info("Solver", $"       [DE 代数 {gen + 1,3}/{maxGens}] 始发={bLaunch,6:F1} m/s | DSM={bDsm,7:F1} m/s | 总ΔV={bTot,7:F1} m/s | 历元={epStr} | 闭环={feasible}");
                }

                // 目标达标极速提前收敛刹车 (Target Reached Early Exit):
                // 一旦在代数进化中捕获到完全满足总工质机动、发射上限、全航程纯无动力借力飞掠的优质解，
                // 立即跳出循环进入终极模式打磨，节约大量盲目计算时间！
                if (gen >= 8 && gen % 2 == 0) {
                    double bestF = double.MaxValue;
                    int bI = 0;
                    for (int i = 0; i < popSize; i++) {
                        if (fitness[i] < bestF) { bestF = fitness[i]; bI = i; }
                    }
                    if (MGAPhysicsLossEngine.IsAcceptableForSelection(bestF)) {
                        var curBestSol = PykepBridge.BuildFullSolution(pop[bI], fullChain, config, computeAdvanced: false);
                        if (curBestSol != null && curBestSol.AllLegsFeasible) {
                            bool hasHardBudget = config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0;
                            bool meetTotal = hasHardBudget && (curBestSol.TotalDeltaVKms * 1000.0 <= config.VesselMaxDeltaVMs);
                            bool meetLaunch = (config.LaunchDeltaVCeilingMs > 0)
                                ? (curBestSol.LaunchDeltaVKms * 1000.0 <= config.LaunchDeltaVCeilingMs)
                                : true;
                            bool meetDsm = (config.MaxDsmDeltaVMs > 0)
                                ? (curBestSol.TotalDsmDeltaVKms * 1000.0 <= config.MaxDsmDeltaVMs)
                                : true;
                            bool meetFb = (curBestSol.TotalFlybyDeltaVKms < 1e-4);
                            double nomTof = MGAPhysicsLossEngine.ResolveNominalTofDays(config, curBestSol);
                            bool meetTof = (nomTof <= 0 || curBestSol.TotalFlightDays <= nomTof * 1.5);
                            if (hasHardBudget && meetTotal && meetLaunch && meetDsm && meetFb && meetTof) {
                                KSPGMATLog.Success("Solver", $"       [DE 极速达标提前收敛] 第 {gen + 1} 代已完美达成硬门禁 (总ΔV={curBestSol.TotalDeltaVKms*1000.0:F1} m/s, 始发={curBestSol.LaunchDeltaVKms*1000.0:F1} m/s, DSM={curBestSol.TotalDsmDeltaVKms*1000.0:F1} m/s, 航时={curBestSol.TotalFlightDays:F0}d, 飞掠脉冲=0 m/s)，提前刹车进入极值抛光！");
                                break;
                            }
                        }
                    }
                }

                // 智能体不动点收敛、敏捷停滞早退与不可行拓扑快速熔断 (Vasile et al. 2011 IDEA 离散动力学理论)
                // 1. 当种群被困于高能惩罚区且无突破时，快速熔断该劣质拓扑，杜绝整机数十代无效空转；
                // 2. 当种群离差半径坍缩且连续无显著改善时，数学证明已进入不动点，提前转入局部打磨。
                if (gen >= 4) {
                    double curGenBest = double.MaxValue;
                    int bestIndivIdx = 0;
                    for (int i = 0; i < popSize; i++) {
                        if (fitness[i] < curGenBest) {
                            curGenBest = fitness[i];
                            bestIndivIdx = i;
                        }
                    }
                    if (curGenBest < lastStagFit * (1.0 - 1e-4) - 1e-6) {
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

                        // 核心机制 A: 拓扑深层不可行快速熔断 (Circuit Breaker)
                        // 熔断门槛全部改为"代数预算的比例 + 规范化违约判据", 无绝对量级:
                        //   IsDeeplyInfeasible <=> 约束违背量已达基准预算量级 (g_hat >= 1)
                        int breakerGen = Math.Max(4, (int)Math.Round(CircuitBreakerGenFraction * maxGens));
                        int breakerStag = Math.Max(3, (int)Math.Round(CircuitBreakerStagFraction * maxGens));
                        if (gen >= breakerGen && MGAPhysicsLossEngine.IsDeeplyInfeasible(curGenBest) && stagGens >= breakerStag) {
                            KSPGMATLog.Warn("Solver", $"       [DE 拓扑不可行敏捷熔断] 第 {gen + 1} 代检测到深层动力学受罚 (Loss={curGenBest:E2}, 连续 {stagGens} 代无进展)，提前熔断剪枝！");
                            break;
                        }

                        // 核心机制 B: 敏捷不动点早退与膨胀差分进化引导重启 (IDEA Guided Restart: Vasile et al. 2011)
                        // 判据 (全部为相对量): 种群离差半径收缩至搜索域的指定比例 且 停滞代际达代数预算的比例。
                        // 取代原三级绝对阈值 (dispersion<0.05/0.04 + stagGens>=6/10/14 + Loss<1e5/1e7)。
                        int stagEarly = Math.Max(3, (int)Math.Round(StagnationEarlyGenFraction * maxGens));
                        int stagHard = Math.Max(6, (int)Math.Round(StagnationHardGenFraction * maxGens));
                        bool shouldTriggerStag =
                            (stagGens >= stagEarly && dispersion < StagnationDispersionFraction)
                            || (stagGens >= stagHard);

                        if (shouldTriggerStag) {
                            // 若仍有充足代数预算且未超过最大重启轮次，执行 IDEA 膨胀禁区归档 + 精英引导重启
                            // 剩余代数门槛同样以预算比例给出 (取代原固定 15 代)
                            int restartMinRemaining = Math.Max(5, (int)Math.Round(IdeaRestartMinRemainingFraction * maxGens));
                            if (ideaRestartCount < maxIdeaRestarts && (maxGens - gen) >= restartMinRemaining) {
                                var attractor = (double[])pop[bestIndivIdx].Clone();
                                double rInfl = Math.Max(MinInflationRadiusFraction, Math.Min(MaxInflationRadiusFraction, dispersion * InflationRadiusFactor));
                                inflationArchive.Add((attractor, rInfl));
                                ideaRestartCount++;

                                int eliteKeep = Math.Max(2, islandCount * 2);
                                var rankedIdxs = Enumerable.Range(0, popSize).OrderBy(k => fitness[k]).ToArray();
                                var restartRand = new System.Random(gen * 31337 + ideaRestartCount * 7919);

                                for (int ri = eliteKeep; ri < popSize; ri++) {
                                    int pIdx = rankedIdxs[ri];
                                    double[] newGene = new double[dim];
                                    for (int attempt = 0; attempt < 10; attempt++) {
                                        for (int d = 0; d < dim; d++) {
                                            newGene[d] = lowerBounds[d] + (upperBounds[d] - lowerBounds[d]) * restartRand.NextDouble();
                                        }
                                        // 确保新采样点位于所有已知排斥超球禁区外
                                        bool insideAnyInfl = false;
                                        foreach (var (archAttractor, archR) in inflationArchive) {
                                            double dSq = 0.0;
                                            for (int d = 0; d < dim; d++) {
                                                double span = upperBounds[d] - lowerBounds[d];
                                                if (span > 1e-6) {
                                                    double diff = (newGene[d] - archAttractor[d]) / span;
                                                    dSq += diff * diff;
                                                }
                                            }
                                            if (Math.Sqrt(dSq / Math.Max(1, dim)) < archR) {
                                                insideAnyInfl = true;
                                                break;
                                            }
                                        }
                                        if (!insideAnyInfl) break;
                                    }
                                    ProjectFlightDuration(newGene, legCount, lowerBounds, upperBounds, maxDurationSec);
                                    pop[pIdx] = newGene;
                                    var newSol = PykepBridge.BuildFullSolution(newGene, fullChain, config, computeAdvanced: false);
                                    fitness[pIdx] = fitnessFunc(newSol);
                                }

                                stagGens = 0;
                                lastStagFit = curGenBest;
                                KSPGMATLog.Warn("Solver", $"       [DE IDEA 引导重启 #{ideaRestartCount}] 归档第 {inflationArchive.Count} 个局部吸引子超球禁区 (半径 r_infl={rInfl:F3})，保留前 {eliteKeep} 个精英个体，在禁区外重新采样重启！");
                                continue;
                            } else {
                                KSPGMATLog.Info("Solver", $"       [DE IDEA 停滞收敛] 经历 {ideaRestartCount} 轮膨胀引导后锁定全局不动点 (连续 {stagGens} 代无改善, 离差半径={dispersion:F3}), 提前刹车转入模式抛光！");
                                break;
                            }
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
                    // 损失已无量纲归一化, 判据为规范阈值而非量级猜测:
                    //   IsDeeplyInfeasible <=> 约束违背量已达基准预算量级 (g_hat >= 1)
                    string fitStr = !MGAPhysicsLossEngine.IsDeeplyInfeasible(bestFit)
                        ? $"[达标/优选] 总ΔV={bTot:F1} m/s (始发={bLaunch:F1} m/s, DSM={bDsm:F1} m/s, 动力学闭环={feasible})"
                        : $"[探索/超标] 总ΔV={bTot:F1} m/s (始发={bLaunch:F1} m/s, DSM={bDsm:F1} m/s, 罚项={bestFit:E1})";
                    float prog = (float)(gen + 1) / maxGens;
                    onProgress.Invoke(prog, $"[DE 演化第 {gen + 1,3}/{maxGens} 代] 种群 {popSize} | {fitStr}");
                }

                // 增广拉格朗日对偶上升与罚参数自适应 (每代一次, 取代手调惩罚常数的静态权重)
                alm.EndIteration();

                Telemetry.MGAPipelineTelemetryProbe.RecordGeneration();
            }
            } // end Scope DE进化: 代数主循环全过程

            // 记录强化学习 MAB 调参器各岛屿探索收益快照到探针
            for (int isl = 0; isl < islandCount; isl++) {
                var t = banditTuners[isl];
                Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordBanditSnapshot(
                    $"Island_{isl + 1}", t.TotalPulls, t.ArmPulls, t.ArmRewards, t.CurrentF, t.CurrentCR);
            }

            // (已在 PolishElitesHookeJeeves 与 ExtractAndEnrichSolutions 内按需排序)
        }
    }
}
