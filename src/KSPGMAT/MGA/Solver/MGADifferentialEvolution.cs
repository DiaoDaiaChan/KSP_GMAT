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
using KSPGMAT.MGA.AI;
using KSPGMAT.MGA.Telemetry;
using KSPGMAT.MGA.GPU;
using UnityEngine;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 单时间窗口差分进化评估内核（支持单核极速内循环与多核并行二选一，彻底杜绝嵌套并行饥饿）
    /// 底层评估 100% 委托 PykepBridge.BuildFullSolution → 原生 kep3 Lambert / 飞掠动力学。
    /// </summary>
    public static partial class MGADifferentialEvolution
    {
        /// <summary>
        /// 默认随机种子。固定值使同一配置的求解结果可逐位复现, 便于基准 A/B 回归对比。
        /// </summary>
        public const int DefaultRandomSeed = 42;

        /// <summary>
        /// 解析 DE 随机种子。可用环境变量 KSPGMAT_MGA_RANDOM_SEED 覆盖:
        ///   * 未设置      -> DefaultRandomSeed (42), 保证基准可复现;
        ///   * 设为正数    -> 使用该值;
        ///   * 设为 0      -> 按当前时钟随机 (打破固定随机流带来的探索局限)。
        /// </summary>
        private static int ResolveRandomSeed()
        {
            try
            {
                string raw = Environment.GetEnvironmentVariable("KSPGMAT_MGA_RANDOM_SEED");
                if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out int seed))
                {
                    return seed == 0 ? Environment.TickCount : seed;
                }
            }
            catch { }
            return DefaultRandomSeed;
        }

        // =====================================================================
        // 收敛/熔断判据 —— 全部以"代数预算或搜索域的**比例**"表达 (无量纲, 自缩放)
        // 取代原绝对阈值: gen>=25 / stagGens>=15,6,10,14 / dispersion<0.05,0.04。
        // 由于 maxGens 已按 dim 自适应, 比例形式使判据在任何问题规模下保持等效语义。
        // =====================================================================

        /// <summary>拓扑熔断的最早代数 (占代数预算比例)</summary>
        private const double CircuitBreakerGenFraction = 0.25;

        /// <summary>拓扑熔断所需的连续停滞代数 (占代数预算比例)</summary>
        private const double CircuitBreakerStagFraction = 0.15;

        /// <summary>早退判定所需的连续停滞代数 (占代数预算比例)</summary>
        private const double StagnationEarlyGenFraction = 0.04;

        /// <summary>绝对停滞硬兜底的连续停滞代数 (占代数预算比例)</summary>
        private const double StagnationHardGenFraction = 0.12;

        /// <summary>种群离差半径收敛判据 (占搜索域的比例; 5% 表示种群已收缩到搜索域 5% 以内)</summary>
        private const double StagnationDispersionFraction = 0.05;

        /// <summary>IDEA 重启所需的最小剩余代数 (占代数预算比例)</summary>
        private const double IdeaRestartMinRemainingFraction = 0.10;

        /// <summary>IDEA 排斥超球半径 = 该倍数 × 实测种群离差 (相对量, 非绝对半径)</summary>
        private const double InflationRadiusFactor = 2.0;

        /// <summary>排斥超球半径下限 (占搜索域比例)</summary>
        private const double MinInflationRadiusFraction = 0.03;

        /// <summary>排斥超球半径上限 (占搜索域比例)</summary>
        private const double MaxInflationRadiusFraction = 0.15;

        /// <summary>
        /// 针对连续同星共振借力集群 (如水星×3、金星×2、地球×2 等)，执行联合物理共振槽与下游出口天体相位闭环评分。
        /// 通过笛卡尔积扫描所有有效有理周期比组合，以开普勒守恒与出口兰伯特转向角残差优选出最佳共振组合，
        /// 确保停留总时间严格贴合出口天体的会合周期 (T_syn)，彻底消除出口航段天文级 DSM 赤字！
        /// </summary>
        public static List<Tuple<double[], double>> FindAndRankResonantCombos(
            string bodyName, string exitBodyName, int blockLen,
            double curUT, Vector3d prevArrVinf,
            double[] lowerBounds, double[] upperBounds, int resStart,
            string central, double centralMu,
            double nominalExitToFSec = 0.0,
            bool isExitDestination = false,
            string downstreamBodyName = null)
        {
            var rankedCombos = new List<Tuple<double, double[], double>>();
            var pBody = SolarSystemCatalog.GetPlanet(bodyName);
            if (pBody == null) return new List<Tuple<double[], double>>();

            double sma = PykepBridge.GetRelativeSMA(pBody, central, 0.0);
            double tOrb = PykepBridge.CalcOrbitPeriodSec(sma, centralMu);
            if (tOrb <= 0 || double.IsNaN(tOrb)) return new List<Tuple<double[], double>>();

            // 全部低阶有理共振槽候选: 同星自共振重遇段 (无论是启航共振、中间调相还是终点自减速)
            // 采用开普勒二体与共振动力学低阶有理共振槽序列 (ResonanceRatioCandidates)
            double[] availRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;

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

            int maxCombosCap = (string.IsNullOrEmpty(exitBodyName)) ? 256 : 64;
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
                    if (allCombos.Count >= maxCombosCap) return;
                }
            };
            buildCombos(0, new double[blockLen]);

            var pExit = !string.IsNullOrEmpty(exitBodyName) ? SolarSystemCatalog.GetPlanet(exitBodyName) : null;
            var pDown = (!isExitDestination && !string.IsNullOrEmpty(downstreamBodyName)) ? SolarSystemCatalog.GetPlanet(downstreamBodyName) : null;
            if (pExit != null && pDown != null && pDown.Name.Equals(pExit.Name, StringComparison.OrdinalIgnoreCase)) {
                pDown = null;
            }
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

                double score = 0.0;
                double bestExitToF = approxTofSec;
                if (pExit != null) {
                    pBody.GetRelativeState(central, depUT, out Vector3d rDep, out Vector3d vDepPlanet);
                    // 出口霍曼/快速切向转移的测试航时区间: 覆盖自极速飞掠转移 (0.35) 至慢外向转移 (3.00)
                    double[] testExitToFs = new double[] {
                        approxTofSec * 0.35, approxTofSec * 0.50, approxTofSec * 0.70,
                        approxTofSec * 0.85, approxTofSec * 1.00, approxTofSec * 1.20,
                        approxTofSec * 1.50, approxTofSec * 2.00, approxTofSec * 3.00
                    };
                    double bestExitScore = double.MaxValue;
                    double bestVTurn = 0.0, bestArrPen = 0.0, bestDownDef = 0.0;

                    foreach (double exitTof in testExitToFs) {
                        pExit.GetRelativeState(central, depUT + exitTof, out Vector3d rArrNext, out Vector3d vArrPlanet);
                        double rExitAvg = (rDep.magnitude + rArrNext.magnitude) * 0.5;
                        double tOrbExitAvg = 2.0 * Math.PI * Math.Sqrt(Math.Pow(rExitAvg * 1000.0, 3.0) / (centralMu * 1e9));
                        int nmax = (tOrbExitAvg > 10.0) ? (int)Math.Floor(exitTof / tOrbExitAvg + 0.25) : 0;
                        nmax = Math.Max(0, Math.Min(3, nmax));
                        if (PykepNative.SolveLambert(rDep, rArrNext, exitTof, centralMu, false, nmax, out Vector3d vDepLambert, out Vector3d vArrLambert)) {
                            Vector3d vInfDesired = vDepLambert - vDepPlanet;
                            double outMag = vInfDesired.magnitude;
                            Vector3d effectiveInVinf = prevArrVinf;
                            double effInMag = inMag;
                            bool isSelfResonantDeparture = (effectiveInVinf.sqrMagnitude < 1e-4 || effInMag < 1.0);
                            double reqTurnRad = 0.0;
                            double totalTurnCapability = 0.0;
                            if (isSelfResonantDeparture) {
                                double lastR = (combo != null && combo.Length > 0) ? combo[combo.Length - 1] : 1.0;
                                // 自共振启航段 (如 MESSENGER 1年 EGA 或 Juno 2年 EGA: Earth ➔ Earth ➔ NextPlanet):
                                // 航天器首段通过发射入轨能量与深空微调，在重遇借力行星时满足 |v_inf,in| = |v_inf,out| = outMag，
                                // 以实现无动力双曲偏折 (Flyby DeltaV = 0)。
                                effInMag = outMag;

                                // 开普勒活力方程解析推导自共振发射天区角 alpha:
                                // cos(alpha) = [ mu_central * (1/r_dep - 1/a_res) - v_inf^2 ] / (2 * v_planet * v_inf)
                                double aRes = sma * Math.Pow(lastR, 2.0 / 3.0);
                                double vPlanetMag = vDepPlanet.magnitude;
                                double rMag = rDep.magnitude;
                                double aPlanet = (sma > 0) ? sma : rMag;
                                double vPlanetSq = centralMu * (2.0 / Math.Max(1.0, rMag) - 1.0 / Math.Max(1.0, aPlanet));
                                double vCraftSq = centralMu * (2.0 / Math.Max(1.0, rMag) - 1.0 / Math.Max(1.0, aRes));
                                double numEnergy = vCraftSq - vPlanetSq - effInMag * effInMag;
                                double denomEnergy = 2.0 * Math.Max(1e-4, vPlanetMag) * Math.Max(1e-4, effInMag);
                                double cosAlpha = Math.Max(-1.0, Math.Min(1.0, numEnergy / denomEnergy));
                                double alpha = Math.Acos(cosAlpha);

                                // 目标出口渐近线相对行星速度夹角 theta_desired:
                                double dotPlanet = (vPlanetMag > 1e-4 && outMag > 1e-4)
                                    ? Vector3d.Dot(vDepPlanet.normalized, vInfDesired.normalized)
                                    : 1.0;
                                dotPlanet = Math.Max(-1.0, Math.Min(1.0, dotPlanet));
                                double thetaDesired = Math.Acos(dotPlanet);

                                // 发射入轨能量与方位在三维圆锥自由度上的最小物理折角:
                                reqTurnRad = Math.Abs(thetaDesired - alpha);

                                double eMin = 1.0 + (safeR * effInMag * effInMag) / Math.Max(1.0, pBody.Mu);
                                double maxTurnRadPerFlyby = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eMin));
                                totalTurnCapability = maxTurnRadPerFlyby * blockLen;
                            } else {
                                double eMin = 1.0 + (safeR * effInMag * effInMag) / Math.Max(1.0, pBody.Mu);
                                double maxTurnRadPerFlyby = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eMin));
                                totalTurnCapability = maxTurnRadPerFlyby * (blockLen + 1);

                                double dot = (effInMag > 1e-4 && outMag > 1e-4 && effectiveInVinf.sqrMagnitude > 1e-4)
                                    ? Vector3d.Dot(effectiveInVinf.normalized, vInfDesired.normalized)
                                    : 1.0;
                                dot = Math.Max(-1.0, Math.Min(1.0, dot));
                                reqTurnRad = Math.Acos(dot);
                            }

                            double turnExcess = Math.Max(0.0, reqTurnRad - totalTurnCapability);
                            // 物理深空机动脉冲估算: 无法通过双曲偏转吸收的转角转化为向量差脉冲 DeltaV_turn = 2 * v_inf * sin(turnExcess/2)
                            double vTurnLoss = 2.0 * Math.Max(effInMag, outMag) * Math.Sin(turnExcess * 0.5);
                            double vPumpLoss = isSelfResonantDeparture ? 0.0 : Math.Abs(effInMag - outMag);
                            double vArrInf = (vArrLambert - vArrPlanet).magnitude;
                            // 物理发射与制动能耗: 
                            // 1. 自共振启航段计入初始注入轨道速度 (发射 DeltaV);
                            // 2. 中继/终点飞掠计入到达超速势能 (为后续借力保留高偏折窗口)
                            double depLaunchDv = 0.0;
                            if (isSelfResonantDeparture) {
                                double rpDep = pBody.RadiusKm + 200.0;
                                double vHypDep = Math.Sqrt(outMag * outMag + 2.0 * pBody.Mu / Math.Max(1.0, rpDep));
                                double vParkDep = Math.Sqrt(pBody.Mu / Math.Max(1.0, rpDep));
                                depLaunchDv = (vHypDep - vParkDep);
                            }
                            double arrPenalty = isExitDestination ? (0.25 * vArrInf) : 0.0;

                            // 普适下游天体开普勒活力能量与偏折连续性校验 (Vis-Viva Downstream Linkage):
                            // 若出口星为中间跳板且后续存在借力/目标天体，出口星到达超速必须满足下游转移的能量底线
                            double downstreamDeficit = 0.0;
                            if (pDown != null && pDown.SMA > 0 && pExit.SMA > 0) {
                                double aTransDown = (pExit.SMA + pDown.SMA) * 0.5;
                                double vTransExit = Math.Sqrt(Math.Max(1e-4, centralMu * (2.0 / pExit.SMA - 1.0 / aTransDown)));
                                double vCircExit = Math.Sqrt(Math.Max(1e-4, centralMu / pExit.SMA));
                                double vInfReqDown = Math.Abs(vTransExit - vCircExit);

                                // 动能亏空: 到达出口星的双曲超速如果低于前往下游天体所需的最低超速, 必然导致刚性工质赤字
                                double energyDeficit = Math.Max(0.0, vInfReqDown - vArrInf);

                                // 物理转向赤字: 计算在出口星处将入射渐近线偏折至下游转移弧所需的双曲偏转角
                                double tNomDown = Math.PI * Math.Sqrt(Math.Pow(aTransDown * 1000.0, 3.0) / (centralMu * 1e9));
                                double arrExitUT = depUT + exitTof;
                                double[] testDownFactors = new double[] { 0.85, 1.00, 1.25, 1.50 };
                                double bestDownDeficit = double.MaxValue;

                                foreach (double factor in testDownFactors) {
                                    double tDownTest = tNomDown * factor;
                                    pDown.GetRelativeState(central, arrExitUT + tDownTest, out Vector3d rDown, out _);
                                    double turnDeficit = 0.0;
                                    double rDownAvg = (rArrNext.magnitude + rDown.magnitude) * 0.5;
                                    double tOrbDownAvg = 2.0 * Math.PI * Math.Sqrt(Math.Pow(rDownAvg * 1000.0, 3.0) / (centralMu * 1e9));
                                    int nmaxDown = (tOrbDownAvg > 10.0) ? (int)Math.Floor(tDownTest / tOrbDownAvg + 0.25) : 0;
                                    nmaxDown = Math.Max(0, Math.Min(3, nmaxDown));
                                    if (PykepNative.SolveLambert(rArrNext, rDown, tDownTest, centralMu, false, nmaxDown, out Vector3d vDepToDown, out _)) {
                                        Vector3d vDepExitInf = vDepToDown - vArrPlanet;
                                        double exitSafeR = (pExit.SafeRadiusKm > 0) ? pExit.SafeRadiusKm : MGADefaults.GetBodySafeRadiusKm(exitBodyName);
                                        double eExit = 1.0 + (exitSafeR * vArrInf * vArrInf) / Math.Max(1.0, pExit.Mu);
                                        double maxTurnExit = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eExit));
                                        double dotDown = Vector3d.Dot(vArrLambert - vArrPlanet, vDepExitInf);
                                        if (vArrInf > 1e-4 && vDepExitInf.magnitude > 1e-4) {
                                            dotDown /= (vArrInf * vDepExitInf.magnitude);
                                        }
                                        dotDown = Math.Max(-1.0, Math.Min(1.0, dotDown));
                                        double reqTurnDown = Math.Acos(dotDown);
                                        double excessTurnDown = Math.Max(0.0, reqTurnDown - maxTurnExit);
                                        turnDeficit = 2.0 * Math.Max(vArrInf, vDepExitInf.magnitude) * Math.Sin(excessTurnDown * 0.5);
                                        double diffDown = Math.Abs(vArrInf - vDepExitInf.magnitude);
                                        double dDef = turnDeficit + diffDown;
                                        if (dDef < bestDownDeficit) bestDownDeficit = dDef;
                                    } else {
                                        double dDef = energyDeficit * 2.0;
                                        if (dDef < bestDownDeficit) bestDownDeficit = dDef;
                                    }
                                }
                                downstreamDeficit = (bestDownDeficit < 1e5 ? bestDownDeficit : energyDeficit * 2.0);
                            }

                            double s = vTurnLoss + depLaunchDv + 0.5 * vPumpLoss + arrPenalty + downstreamDeficit;
                            if (s < bestExitScore) {
                                bestExitScore = s;
                                bestExitToF = exitTof;
                                bestVTurn = vTurnLoss;
                                bestArrPen = arrPenalty;
                                bestDownDef = downstreamDeficit;
                            }
                        }
                    }
                    score = (bestExitScore < 1e5 ? bestExitScore : 1e6);
                    if (string.Equals(bodyName, "Earth", StringComparison.OrdinalIgnoreCase) && string.Equals(exitBodyName, "Venus", StringComparison.OrdinalIgnoreCase)) {
                        KSPGMATLog.Debug("Solver", $"   [ResCombDiag] r={combo[0]:F2} ({combo[0]*365.25:F0}d) -> score={score:F2}, exitToF={bestExitToF/86400.0:F1}d (vTurn={bestVTurn:F2}, arrPen={bestArrPen:F2}, downDef={bestDownDef:F2})");
                    }
                } else {
                    // 出口天体为空：此共振块位于任务终点目标天体 (如 MESSENGER / BepiColombo 在水星连续共振自减速入轨)
                    // 物理目标：严格满足各次飞掠双曲偏折角限制，通过逐级共振将到达双曲超速 v_inf 削减至极小，
                    // 彻底最小化终点泊入捕获脉冲 (Orbit Insertion DeltaV)!
                    pBody.GetRelativeState(central, depUT, out Vector3d rDep, out Vector3d vDepPlanet);
                    double curEffVinf = (prevArrVinf.sqrMagnitude > 1e-4) ? prevArrVinf.magnitude : (vDepPlanet.magnitude * 0.18);
                    double vTransPenalty = 0.0;
                    for (int i = 0; i < blockLen; i++) {
                        double pRatio = (i < combo.Length) ? combo[i] : 1.0;
                        int maxQ = Math.Min(8, Math.Max(1, (int)Math.Floor(2.8 * pRatio)));
                        double bestDiff = double.MaxValue;
                        double bestVInf = curEffVinf;

                        double eHyp = 1.0 + (safeR * curEffVinf * curEffVinf) / Math.Max(1.0, pBody.Mu);
                        double deltaMax = 2.0 * Math.Asin(1.0 / Math.Max(1.0001, eHyp));
                        double maxTurnDv = 2.0 * curEffVinf * Math.Sin(deltaMax * 0.5);

                        for (int q = 1; q <= maxQ; q++) {
                            double tSc = (pRatio / q) * tOrb;
                            double aRes = Math.Pow((tSc / (2.0 * Math.PI)) * (tSc / (2.0 * Math.PI)) * centralMu, 1.0 / 3.0);
                            double vSq = centralMu * (2.0 / rDep.magnitude - 1.0 / Math.Max(1.0, aRes));
                            if (vSq <= 0) continue;
                            double vCraft = Math.Sqrt(vSq);
                            double vInfCand = Math.Abs(vCraft - vDepPlanet.magnitude);

                            double diff = Math.Abs(curEffVinf - vInfCand);
                            double excessTurn = Math.Max(0.0, diff - maxTurnDv);
                            double penalty = excessTurn * 3.0 + diff * 0.5;

                            if (penalty < bestDiff) {
                                bestDiff = penalty;
                                bestVInf = vInfCand;
                            }
                        }

                        vTransPenalty += bestDiff;
                        curEffVinf = bestVInf;
                    }

                    // 目标椭圆轨道捕获脉冲估算 (高偏心率捕获)
                    double rpCap = pBody.RadiusKm + 200.0;
                    double raCap = pBody.RadiusKm + 15000.0;
                    double aCap = (rpCap + raCap) * 0.5;
                    double vCap = Math.Sqrt(Math.Max(1e-4, pBody.Mu * (2.0 / rpCap - 1.0 / aCap)));
                    double vHypArr = Math.Sqrt(curEffVinf * curEffVinf + 2.0 * pBody.Mu / rpCap);
                    double dvCap = Math.Max(0.0, vHypArr - vCap);

                    score = vTransPenalty + dvCap;
                }
                rankedCombos.Add(Tuple.Create(score, combo, bestExitToF));
            }

            return rankedCombos.OrderBy(t => t.Item1).Select(t => Tuple.Create(t.Item2, t.Item3)).ToList();
        }

        /// <summary>
        /// 在 [winStartUT, winEndUT] 单一会合窗口内执行差分进化寻优。
        /// </summary>
        /// <summary>
        /// 基于 MGAConfig 自适应解析出权威的完整天体链路 (fullChain: [Departure, Flyby1, ..., Destination])。
        /// 具备鲁棒的终点天体防重复与共振飞掠保全机制，彻底杜绝合法的共振借力被误当冗余终点截断。
        /// </summary>
        public static List<string> ResolveFullChain(MGAConfig config)
        {
            var fullChain = new List<string>();
            if (!string.IsNullOrEmpty(config?.DepartureBody))
            {
                fullChain.Add(config.DepartureBody);
            }

            if (config?.FlybySequence != null && config.FlybySequence.Count > 0)
            {
                var intermediateFlybys = config.FlybySequence;
                // 判定 FlybySequence 是否已包含终点天体 DestinationBody:
                // 1. 若显式给定了航段数参考 (LegStrategies, MinToFDays, MaxToFDays):
                //    - 若 FlybySequence.Count == expectedLegs - 1: 说明 FlybySequence 仅包含中间借力天体 (末尾同名天体是合法的共振借力，如 Rosetta 的 Earth 借力)
                //    - 若 FlybySequence.Count == expectedLegs: 说明 FlybySequence 包含了终点天体
                // 2. 若显式给定了借力数参考 (FlybyStrategies):
                //    - 若 FlybySequence.Count == FlybyStrategies.Count: 说明为纯中间借力
                // 3. 若无任何参考元数据:
                //    - 若 Count == 1 且等于 DestinationBody: 为纯直达航段 (如 Direct Hohmann)
                //    - 若 Count > 1 且末尾等于 DestinationBody: 默认视为包含了终点天体截断
                int expectedLegs = -1;
                if (config.LegStrategies != null && config.LegStrategies.Count > 0)
                    expectedLegs = config.LegStrategies.Count;
                else if (config.MinToFDays != null && config.MinToFDays.Count > 0)
                    expectedLegs = config.MinToFDays.Count;
                else if (config.MaxToFDays != null && config.MaxToFDays.Count > 0)
                    expectedLegs = config.MaxToFDays.Count;
                else if (config.FlybyStrategies != null && config.FlybyStrategies.Count > 0)
                    expectedLegs = config.FlybyStrategies.Count + 1;

                bool hasDestinationAtEnd = false;
                if (!string.IsNullOrEmpty(config.DestinationBody) &&
                    intermediateFlybys.Last().Equals(config.DestinationBody, StringComparison.OrdinalIgnoreCase))
                {
                    if (expectedLegs > 0)
                    {
                        hasDestinationAtEnd = (intermediateFlybys.Count == expectedLegs);
                    }
                    else
                    {
                        hasDestinationAtEnd = true;
                    }
                }

                if (hasDestinationAtEnd)
                {
                    intermediateFlybys = intermediateFlybys.Take(intermediateFlybys.Count - 1).ToList();
                }
                fullChain.AddRange(intermediateFlybys);
            }

            if (!string.IsNullOrEmpty(config?.DestinationBody))
            {
                fullChain.Add(config.DestinationBody);
            }

            return fullChain;
        }

        public static List<MGASolution> RunWindow(
            MGAConfig config, double winStartUT, double winEndUT,
            Action<float, string> onProgress, CancellationToken ct,
            bool parallelizeInternal = true,
            int populationMaxParallelism = -1)
        {
            var solutions = new List<MGASolution>();
            var fullChain = ResolveFullChain(config);
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

                    // 标称共振比由该腿 ToF 上下界对开普勒共振槽投影推导 (取代按 SMA 类别拍脑袋的 {1,2,3} 选择)
                    double bestR = MGAFlybyDynamicsHelper.PickNominalResonanceRatio(
                        tOrbDays * MGADefaults.SecondsPerDay,
                        minD * MGADefaults.SecondsPerDay,
                        maxD * MGADefaults.SecondsPerDay);
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

            // 若某航段紧随同星共振块之后 (如 Earth->Earth 出口前往 Venus, 或 Venus->Venus 出口前往 Mercury),
            // 由开普勒会合相位与共振解算器优先推导出最优物理出口航时，校准标称航时 nominalLegToFSec
            for (int k = 1; k < legCount; k++) {
                if (fullChain[k - 1].Equals(fullChain[k], StringComparison.OrdinalIgnoreCase) &&
                    !fullChain[k].Equals(fullChain[k + 1], StringComparison.OrdinalIgnoreCase)) {
                    int rStart = k - 1;
                    while (rStart > 0 && fullChain[rStart - 1].Equals(fullChain[rStart], StringComparison.OrdinalIgnoreCase)) {
                        rStart--;
                    }
                    int bLen = k - rStart;
                    string rBody = fullChain[rStart];
                    string exitBody = fullChain[k + 1];
                    double midUT = (winStartUT + winEndUT) * 0.5;
                    double estResUT = midUT;
                    for (int preK = 0; preK < rStart; preK++) estResUT += nominalLegToFSec[preK];

                    var rCombos = FindAndRankResonantCombos(
                        rBody, exitBody, bLen, estResUT, Vector3d.zero,
                        lowerBounds, upperBounds, rStart, central, centralMu, 0.0, false, null);
                    if (rCombos != null && rCombos.Count > 0 && rCombos[0].Item2 > 10.0 * 86400.0) {
                        double exitToF = rCombos[0].Item2;
                        if (exitToF >= lowerBounds[1 + k] && exitToF <= upperBounds[1 + k]) {
                            nominalLegToFSec[k] = exitToF;
                        }
                    }
                }
            }

            bool hasResonantBlock = false;
            for (int rk = 0; rk < legCount; rk++) {
                if (fullChain[rk].Equals(fullChain[rk + 1], StringComparison.OrdinalIgnoreCase)) {
                    hasResonantBlock = true;
                    break;
                }
            }

            if (optDsmEta) {
                for (int k = 0; k < legCount; k++) {
                    int d = 1 + legCount + k;
                    lowerBounds[d] = 0.1;
                    upperBounds[d] = 0.9;
                }
            }

            // 依据 PaGMO (Izzo et al. 2010) 与 jDE (Brest et al.) 维度标度律:
            // 依据问题维度 dim 弹性推导最优子种群规模与进化代数，兼顾低维极速求解与超高维充分收敛。
            int islandCount = 4;
            int autoSubPop = Math.Max(20, Math.Min(48, (int)Math.Ceiling(dim * 2.0)));
            int subPopSize = (config != null && config.PopulationSize > 0)
                ? Math.Max(config.PopulationSize, (int)Math.Ceiling(dim * 1.8))
                : autoSubPop;
            int popSize = islandCount * subPopSize;
            int adaptiveMinGens = Math.Max(MGADefaults.MaxGenerations, (int)(dim * 10.0));
            // ⚠️ [实验归档 2026-09-11] 曾将下方维度标度系数由 14.0 提至 25.0,
            // 试图解决"复杂链并行 DE 欠收敛"(galileo VEEGA 单链 6430 vs 并行 11311)。
            // 实测 13 任务 A/B: 结果 13/13 逐位一致 (零收益), 耗时 +7% (154.6s -> 165.4s)。
            // 且提至 125 代后 VEEGA 仍停在 11311.1 完全不变 —— 说明它已陷入局部最优,
            // 瓶颈不是进化代数。已回退。
            // 结论: DE 在当前配置下已充分收敛; 单链/并行差异另有其原因 (推测为逐链 ToF
            // 边界调谐导致的搜索空间差异, 非 DE 资源不足), 待查。
            int maxGens = (config != null && config.MaxGenerations > 0)
                ? Math.Max(config.MaxGenerations, Math.Min(350, (int)(dim * 14.0)))
                : Math.Max(adaptiveMinGens, Math.Min(350, (int)(dim * 16.0)));

            // ------------------------------------------------------------------
            // 增广拉格朗日约束引擎 (Augmented Lagrangian Multipliers)
            // 取代全部手调惩罚常数 (QuadraticBarrierScale=25000 / PoweredFlybyPenaltyScale=2500 ...):
            // 约束权重 λ/ρ 由违背量自身的对偶上升自适应给出 ⇒ 零标定, 跨任务/跨恒星系成立,
            // 且收敛时约束严格满足而非"罚到够小"。本实例跨代存活, 每代末尾 EndIteration() 更新。
            // ------------------------------------------------------------------
            var alm = new MGAAugmentedLagrangian();

            // 注: MGASurrogateGate 已实现并测试 (含在线假阴性对照与阈值自标定), 但经实测
            // 在本求解链的任何位置都无正收益 (见审计报告 §15), 故默认不接入。
            // 代码保留供后续接入点 (如全任务级评估前筛) 复用。

            // 局部适应度函数: 捕获本窗口的 ALM 实例, 使所有评估点共享同一组乘子
            double Fitness(MGASolution s) => MGAPhysicsLossEngine.ComputeLoss(s, config, alm);

            double[][] pop = new double[popSize][];
            double[] fitness = new double[popSize];
            var random = new System.Random(ResolveRandomSeed());

            // 启发式注种：节点共面与开普勒离散共振组合扫描
            var seedGenomes = GenerateAnalyticalSeedGenomes(
                config, fullChain, dim, legCount, optDsmEta,
                winStartUT, winEndUT, central, centralMu,
                lowerBounds, upperBounds, nominalLegToFSec, Fitness);

            // 5. 普适前向借力步进动力学注种与全局适应度初评优选
            SynthesizeForwardStagedSeeds(
                seedGenomes, out var cachedBlockCombos,
                config, fullChain, dim, legCount, optDsmEta, hasResonantBlock,
                winStartUT, winEndUT, central, centralMu,
                lowerBounds, upperBounds, Fitness);

            // 6. 初始种群染色体分配与初评
            InitializePopulation(
                pop, fitness, popSize, subPopSize, dim, legCount,
                optDsmEta, hasResonantBlock, centralMu, central,
                lowerBounds, upperBounds, fullChain, config,
                seedGenomes, cachedBlockCombos, Fitness, random);

            // 7. 构建同星连续共振块拓扑映射
            var resBlockMap = BuildResonanceBlockMap(fullChain, legCount, central, centralMu);

            // 8. 异构多岛差分进化主代数循环
            RunEvolutionLoop(
                pop, fitness, popSize, subPopSize, islandCount, maxGens,
                dim, legCount, optDsmEta, hasResonantBlock, central, centralMu,
                lowerBounds, upperBounds, fullChain, config,
                Fitness, alm, resBlockMap, cachedBlockCombos, onProgress, ct);

            // 局部坐标下降与 Hooke-Jeeves 模式加速搜索
            PolishElitesHookeJeeves(
                pop, fitness, popSize, lowerBounds, upperBounds,
                fullChain, config, Fitness, cachedBlockCombos,
                resBlockMap, central, centralMu, dim, legCount);

            return ExtractAndEnrichSolutions(pop, fitness, popSize, fullChain, config);
        }

        /// <summary>
        /// 任务飞行总历时投影算子 (Mission Duration Simplex Projection)
        /// 当染色体的各航段航时之和 sum(ToF) 超过任务最大允许总历时 maxDurationSec 时，
        /// 依据各航段超出下界的可缩减量 (T_k - L_k)，按比例投影至可行超平面 sum(T_k) <= maxDurationSec。
        /// 彻底杜绝染色体在高维搜索空间中盲目漂移至荒谬航时（如 27 年），保证种群 100% 产生物理闭合解。
        /// </summary>
        public static void ProjectFlightDuration(
            double[] genome,
            int legCount,
            double[] lowerBounds,
            double[] upperBounds,
            double maxDurationSec)
        {
            if (genome == null || legCount <= 0 || maxDurationSec <= 0) return;
            double sumTof = 0.0;
            double sumMin = 0.0;
            for (int k = 1; k <= legCount; k++)
            {
                sumTof += genome[k];
                sumMin += lowerBounds[k];
            }
            if (sumTof <= maxDurationSec) return;

            if (maxDurationSec <= sumMin)
            {
                for (int k = 1; k <= legCount; k++)
                {
                    genome[k] = lowerBounds[k];
                }
                return;
            }

            double reducible = sumTof - sumMin;
            if (reducible <= 1e-6) return;

            double scale = (maxDurationSec - sumMin) / reducible;
            for (int k = 1; k <= legCount; k++)
            {
                double legRed = genome[k] - lowerBounds[k];
                double newVal = lowerBounds[k] + legRed * scale;
                genome[k] = Math.Max(lowerBounds[k], Math.Min(upperBounds[k], newVal));
            }
        }
    }
}
