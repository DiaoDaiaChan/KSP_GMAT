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
    public static class MGADifferentialEvolution
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
            // 物理公理: 航天器重遇同一开普勒偏心天体，该天体必须公转整数圈才能回到同一空间几何点，故重遇航时比必须为整数阶 p in {1, 2, 3, 4, 5, 6}
            double[] availRatios = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 };

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
        public static List<MGASolution> RunWindow(
            MGAConfig config, double winStartUT, double winEndUT,
            Action<float, string> onProgress, CancellationToken ct,
            bool parallelizeInternal = true,
            int populationMaxParallelism = -1)
        {
            var solutions = new List<MGASolution>();
            var fullChain = new List<string> { config.DepartureBody };
            if (config.FlybySequence != null && config.FlybySequence.Count > 0) {
                if (config.FlybySequence.Count == 1 && config.FlybySequence[0].Equals(config.DestinationBody, StringComparison.OrdinalIgnoreCase)) {
                    // 纯直达航段
                } else {
                    fullChain.AddRange(config.FlybySequence);
                }
            }
            if (!string.IsNullOrEmpty(config.DestinationBody)) {
                fullChain.Add(config.DestinationBody);
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

            // 启发式注种：若处于模式 2 (锁定倾角与 RAAN) 且为中心天体转移 (如 Earth -> Moon)
            // 预先沿锁定停泊轨道面扫描目标天体零点面外偏角交点，将精准共面发射时刻注种至初始种群
            var seedGenomes = new List<double[]>();
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
                            double testFit = testSol != null ? Fitness(testSol) : 1e9;
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
                            // 全部低阶有理共振槽候选 (水星/金星多阶有理比, 地球 2:1 VEEGA), 由下方 ToF 边界过滤
                            double[] legRatios = MGAFlybyDynamicsHelper.ResonanceRatioCandidates;
                            foreach (double r in legRatios) {
                                double resTofSec = legPeriods[si] * r;
                                if (resTofSec >= lowerBounds[1 + k] && resTofSec <= upperBounds[1 + k]) opts.Add(r);
                            }
                        }
                        ratioOptionsPerLeg.Add(opts);
                    }

                    // 1. 将链路中所有同星共振腿按天体连续性划分为独立的物理共振块 (Contiguous Resonant Blocks)
                    // 例如: 地球×1 为块 0, 金星×1 为块 1, 水星×3 为块 2 (MESSENGER EVVMMM)
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
                            // 优先锚定 1:1 基础开普勒无偏折物理共振槽 (以 |r - 1.0| 排序)
                            var sortedOpts = opts.OrderBy(r => Math.Abs(r - 1.0)).ToList();
                            int takeN = (blocks.Count == 1) ? Math.Min(32, sortedOpts.Count) : Math.Min(4, sortedOpts.Count);
                            foreach (double r in sortedOpts.Take(takeN)) {
                                bCands.Add(new double[] { r });
                            }
                        } else {
                            // 多级连续同星自共振减速/调相块 (如水星 M×3 / 木卫多重借力):
                            // 依据开普勒两体动力学，多级自共振通过单调阶梯减速 (p1 <= p2 <= p3, 如 MESSENGER 3:3 ➔ 4:4 ➔ 6:6)
                            // 或均等调相 (1:1, 2:2) 逐步削减双曲超速。
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

                            // 筛选最具代表性的天体力学序列:
                            // a) 单调非降序列 (自减速连续泵升): MESSENGER 典型轨迹 [3, 4, 6] 即属于此
                            var monoInc = localCombos.Where(c => {
                                for (int i = 1; i < c.Length; i++) if (c[i] < c[i - 1]) return false;
                                return true;
                            }).ToList();

                            // b) 单调非增序列 (加速阶段)
                            var monoDec = localCombos.Where(c => {
                                for (int i = 1; i < c.Length; i++) if (c[i] > c[i - 1]) return false;
                                return true;
                            }).ToList();

                            // c) 等周期调相序列
                            var equalSeq = localCombos.Where(c => {
                                for (int i = 1; i < c.Length; i++) if (Math.Abs(c[i] - c[0]) > 1e-4) return false;
                                return true;
                            }).ToList();

                            // 优先保留关键的整数阶跃序列 (例如 1..6 范围内的单调组合, 如 [3,4,6], [2,3,4] 等)
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

                    // 3. 跨块组装全局共振组合 (Cartesian assembly across blocks, 杜绝单腿深度穷举引发的截断饥饿):
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
                                double cFit = Fitness(comboSol);
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
            // 【确定性契约】并行采样的结果不再直接写入共享 List, 而是先落入各历元本地缓冲,
            // 循环结束后按 epIdx 升序合并。原因: "首次出现者胜出"的去重语义若由线程时序决定,
            // 则保留的组合集合运行间不同 -> 注种不同 -> 解不可复现 (实测 cassini 相差 78.7 m/s)。
            List<Tuple<int, double[]>>[] perEpochCombos = null;
            using (MGAStepProfiler.Scope("DE注种: 前向步进引力偏折动力学合成"))
            {
            // 特征稀疏采样：聚焦高价值历元与霍曼分段，步长依据行星际窗口特征(约20天)自适应设定
            // 采用奇数个采样点以保证窗口两端与正中心对齐 (Window Center Symmetry)
            int sampleEpochCount = Math.Max(5, Math.Min(7, (int)Math.Ceiling((winEndUT - winStartUT) / (20.0 * 86400.0)) + 1));
            if (sampleEpochCount % 2 == 0) sampleEpochCount++;
            double epochStep = (sampleEpochCount > 1) ? ((winEndUT - winStartUT) / (sampleEpochCount - 1)) : 0.0;

            // 覆盖极速高能出射、短霍曼、标称霍曼、扩展 Type-II 借力切向弧等具代表性的开普勒物理分支
            double[] leg0Fractions = new double[] { 0.05, 0.20, 0.40, 0.65, 0.85 };
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
                                    // 涵盖极速双曲飞掠弧 (0.15~0.38 如外行星双曲逃逸快弧、金星-地球55天、水星48~52天)、标称霍曼、及外向慢弧
                                    double[] hohmannScales = new double[] { 0.15, 0.25, 0.38, 0.50, 0.65, 0.85, 1.00, 1.30, 1.80, 2.50, 3.30 };
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

                        var fSol = PykepBridge.BuildFullSolution(genome, fullChain, config, false);
                        if (fSol != null && !double.IsNaN(fSol.TotalDeltaVKms) && fSol.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) {
                            epochSeeds.Add(Tuple.Create(Fitness(fSol), genome));
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
                            var pSol = PykepBridge.BuildFullSolution(pGene, fullChain, config, false);
                            if (pSol != null && !double.IsNaN(pSol.TotalDeltaVKms) && pSol.TotalDeltaVKms < MGADefaults.MaxPlausibleTotalDvKms) {
                                epochSeeds.Add(Tuple.Create(Fitness(pSol), pGene));
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
                                        double candF = Fitness(candSol);
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
                                            double candF = Fitness(candSol);
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
                var batchSols = PykepBridge.BuildBatchSolutions(seedGenomes, fullChain, config);
                var scoredSeeds = new List<Tuple<double, double[]>>(seedGenomes.Count);
                for (int si = 0; si < seedGenomes.Count; si++) {
                    var s = (batchSols != null && si < batchSols.Count) ? batchSols[si] : null;
                    double f = (s != null && !double.IsNaN(s.TotalDeltaVKms)) ? Fitness(s) : 1e9;
                    scoredSeeds.Add(Tuple.Create(f, seedGenomes[si]));
                }
                seedGenomes = scoredSeeds
                    .OrderBy(t => t.Item1)
                    .ThenBy(t => t.Item2, MGASolutionOrdering.GenomeComparer.Instance)
                    .Select(t => t.Item2)
                    .ToList();
            }
            }

            if (seedGenomes.Count > 0) {
                for (int si = 0; si < Math.Min(3, seedGenomes.Count); si++) {
                    var sSol = PykepBridge.BuildFullSolution(seedGenomes[si], fullChain, config, false);
                    if (sSol != null) {
                        KSPGMATLog.Debug("Solver", $"     [种子 #{si+1}] 发射={GMATClient.FormatUtcGregorian(sSol.DepartureUT)}, 始发={sSol.LaunchDeltaVKms * 1000.0:F1} m/s, DSM={sSol.TotalDsmDeltaVKms * 1000.0:F1} m/s, 总ΔV={sSol.TotalDeltaVKms * 1000.0:F1} m/s, 适应度={Fitness(sSol):F1}");
                        if (sSol.Legs != null && sSol.Legs.Count > 0) {
                            var l0 = sSol.Legs[0];
                            KSPGMATLog.Debug("Solver", $"       [DEBUG 种子 #{si+1}] EjectionInc={l0.EjectionInclinationDeg:F2}°, EjectionLAN={l0.EjectionLANDeg:F2}°, TargetInc={config?.DepartureInclinationDeg:F2}°, Feasible={sSol.AllLegsFeasible}");
                            KSPGMATLog.Debug("Solver", $"         r1=({l0.DeparturePosKm.x:F1}, {l0.DeparturePosKm.y:F1}, {l0.DeparturePosKm.z:F1}), v1=({l0.DepartureVelKms.x:F3}, {l0.DepartureVelKms.y:F3}, {l0.DepartureVelKms.z:F3})");
                            KSPGMATLog.Debug("Solver", $"         r2=({l0.ArrivalPosKm.x:F1}, {l0.ArrivalPosKm.y:F1}, {l0.ArrivalPosKm.z:F1}), v2=({l0.ArrivalVelKms.x:F3}, {l0.ArrivalVelKms.y:F3}, {l0.ArrivalVelKms.z:F3})");
                        }
                        if (sSol.Legs != null) {
                            for (int li = 0; li < sSol.Legs.Count; li++) {
                                var l = sSol.Legs[li];
                                double dsm = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                                KSPGMATLog.Debug("Solver", $"       -> Leg #{li+1}: {l.FromBody} -> {l.ToBody}, ToF={l.ToFDays:F1}d, DSM={dsm:F1} m/s, FB_dv={l.FlybyDeltaVKms*1000.0:F1} m/s");
                            }
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
            }

            // 初始种群批量动力学评估 (支持 GPU DirectCompute CS 5.0 与多核 CPU 弹性加速)
            float[] initLosses = null;
            if (!hasResonantBlock && MGAGpuAccelerationManager.EvaluateDePopulationGpu(pop.ToList(), fullChain, config, centralMu, optDsmEta, out initLosses)) {
                for (int i = 0; i < popSize; i++) {
                    fitness[i] = initLosses[i];
                }
            } else {
                var initSols = PykepBridge.BuildBatchSolutions(pop.ToList(), fullChain, config);
                for (int i = 0; i < popSize; i++) {
                    fitness[i] = (initSols != null && i < initSols.Count && initSols[i] != null && !double.IsNaN(initSols[i].TotalDeltaVKms))
                        ? Fitness(initSols[i])
                        : 1e9;
                }
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

            // 强化学习 MAB 自适应变异算子与超参数调控器池 (Multi-Armed Bandit Tuners)
            var banditTuners = new MGABanditHyperparameterTuner[islandCount];
            for (int isl = 0; isl < islandCount; isl++) {
                banditTuners[isl] = new MGABanditHyperparameterTuner();
            }

            using (MGAStepProfiler.Scope("DE进化: 代数主循环全过程"))
            {
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
                                            : Math.Max(5.0 * 86400.0, tOrb * 0.10);
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

                        candidates.Add(candidate);
                    }

                    // 2. DirectCompute HLSL GPU 硬件加速 (统一由 MGAGpuAccelerationManager 全生命周期管理)
                    float[] gpuLosses = null;
                    float[] gLosses = null;
                    if (!hasResonantBlock && MGAGpuAccelerationManager.EvaluateDePopulationGpu(candidates, fullChain, config, centralMu, optDsmEta, out gLosses)) {
                        gpuLosses = gLosses;
                    }

                    List<MGASolution> candSols = null;
                    if (gpuLosses == null) {
                        candSols = PykepBridge.BuildBatchSolutions(candidates, fullChain, config);
                    }

                    // 3. 淘汰选择 (Survival Selection): 集成自适应模拟退火与 Metropolis 准则 (Parallel Tempering ASA)
                    // 高温探索岛允许以统计力学概率跨越开普勒势阱假壁垒，低温收敛岛严格执行梯度抛光
                    for (int i = 0; i < popSize; i++) {
                        double candFit = (gpuLosses != null) ? gpuLosses[i] : Fitness(candSols[i]);
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
                    }

                    // 岛屿多样性方差感知与超参数自适应调谐
                    for (int isl = 0; isl < islandCount; isl++) {
                        int start = isl * subPopSize;
                        double varSum = 0.0;
                        for (int d = 0; d < dim; d++) {
                            double meanVal = 0.0;
                            for (int k = 0; k < subPopSize; k++) meanVal += pop[start + k][d];
                            meanVal /= Math.Max(1, subPopSize);
                            double dVar = 0.0;
                            for (int k = 0; k < subPopSize; k++) {
                                double diff = pop[start + k][d] - meanVal;
                                dVar += diff * diff;
                            }
                            varSum += dVar / Math.Max(1, subPopSize);
                        }
                        banditTuners[isl].AdaptHyperparameters(gen, maxGens, varSum, stagGens);
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
                            fitness[targetIdx] = Fitness(migSol);
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
                            var mbhSol = PykepBridge.BuildFullSolution(mbhCandidate, fullChain, config, computeAdvanced: false);
                            double mbhFit = Fitness(mbhSol);
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
                            double nomTof = MGAPhysicsLossEngine.ResolveNominalTofDays(config, curBestSol);
                            bool meetTof = (nomTof <= 0 || curBestSol.TotalFlightDays <= nomTof * 1.5);
                            if (meetTotal && meetLaunch && meetDsm && meetFb && meetTof) {
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
                                    pop[pIdx] = newGene;
                                    var newSol = PykepBridge.BuildFullSolution(newGene, fullChain, config, computeAdvanced: false);
                                    fitness[pIdx] = Fitness(newSol);
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
            }
            } // end Scope DE进化: 代数主循环全过程

            // 记录强化学习 MAB 调参器各岛屿探索收益快照到探针
            for (int isl = 0; isl < islandCount; isl++) {
                var t = banditTuners[isl];
                Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordBanditSnapshot(
                    $"Island_{isl + 1}", t.TotalPulls, t.ArmPulls, t.ArmRewards, t.CurrentF, t.CurrentCR);
            }

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
                // 若染色体严重发散不可行 (归一化损失超出选解上界)，不浪费时间执行昂贵的单维微调
                if (!MGAPhysicsLossEngine.IsAcceptableForSelection(curFit)) continue;
                // 若为第 2 个候选且比第 1 个候选差 50% 以上，无需再花大量时间打磨次要解
                if (pIdx > 0 && curFit > fitness[rankedIndices[0]] * 1.5) continue;

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
                            return Fitness(pSol);
                        },
                        maxEvaluations: 20);

                    var bayesSol = PykepBridge.BuildFullSolution(polishedBayesGene, fullChain, config, computeAdvanced: false);
                    double bayesFit = Fitness(bayesSol);
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
                                    double cFit = Fitness(comboSol);
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
                                        double testFit = Fitness(testSol);
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
                                                double compFit = Fitness(compSol);
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
                            double testFit = Fitness(testSol);
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
                                    double compFit = Fitness(compSol);
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
                                    double testFit = Fitness(testSol);
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

                        var patSol = PykepBridge.BuildFullSolution(patternGene, fullChain, config, computeAdvanced: false);
                        double patFit = Fitness(patSol);
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

                double dvSavedKms = (curFit < initialFit && MGAPhysicsLossEngine.IsAcceptableForSelection(initialFit)) ? Math.Max(0.0, (initialFit - curFit) / 1000.0) : 0.0;
                Telemetry.MGAPipelineTelemetryProbe.AiMetrics.RecordSelfHealing(curFit < initialFit, dvSavedKms);
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
                if (!MGAPhysicsLossEngine.IsAcceptableForSelection(fitness[idx])) continue;
                var sol = PykepBridge.BuildFullSolution(pop[idx], fullChain, config, computeAdvanced: false);
                if (sol != null && !double.IsNaN(sol.TotalDeltaVKms)) {
                    double subjectMs = (config.NoInsertionBurn ? (sol.LaunchDeltaVKms + sol.TotalDsmDeltaVKms + sol.TotalFlybyDeltaVKms) : sol.TotalDeltaVKms) * 1000.0;
                    if (winBudgetMs < MGADefaults.MaxPlausibleTotalDvKms * 1000.0 && subjectMs > winBudgetMs) continue;
                    if (winDsmBudgetMs < MGADefaults.MaxPlausibleTotalDvKms * 1000.0 && (sol.TotalDsmDeltaVKms * 1000.0 > winDsmBudgetMs || (sol.Legs != null && sol.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > winDsmBudgetMs)))) continue;
                    if (config.LaunchDeltaVCeilingMs > 0 && sol.LaunchDeltaVKms * 1000.0 > config.LaunchDeltaVCeilingMs) continue;
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
            sol.MeetsHardDvGate = launchOk && dsmOk && totalOk;
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