// ============================================================================
// MGAPolicyValueGuide.cs
// 启发式动力学先验概率与部分轨迹价值评分器 (Heuristic Prior Probability & Value Scorer)
// 机制: 为 MCTS 提供天体选择动力学先验分布 P(s, a) 与部分轨迹势能价值评估 V(s)，替代均一展开与深层盲目 Rollout
// 遵从 baseline_agent.md 实事求是原则：本模块为基于引力功率指数与开普勒几何特征的先验评分器，剥离虚假学术包装
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT.Model;

namespace KSPGMAT.MGA.AI
{
    /// <summary>
    /// MCTS 拓扑探索的启发式先验概率与价值评分器 (Heuristic Prior Scorer)
    /// </summary>
    public class MGAPolicyValueGuide
    {
        private static MGAPolicyValueGuide _instance;
        public static MGAPolicyValueGuide Instance => _instance ?? (_instance = new MGAPolicyValueGuide());

        /// <summary>
        /// 策略先验分布计算: 输入当前所在天体、目标天体、及候选飞越天体列表，输出各候选天体的 Softmax 先验概率
        /// </summary>
        public Dictionary<string, double> ComputeActionPriorProbabilities(
            string currentBody, 
            string targetBody, 
            List<string> candidateBodies,
            double currentEpochUT,
            double maxDeltaVLimitKms)
        {
            var priors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (candidateBodies == null || candidateBodies.Count == 0) return priors;

            var curPlanet = SolarSystemCatalog.GetPlanet(currentBody);
            var tgtPlanet = SolarSystemCatalog.GetPlanet(targetBody);

            double curSma = curPlanet != null ? curPlanet.SMA : MGADefaults.AU_KM;
            double tgtSma = tgtPlanet != null ? tgtPlanet.SMA : MGADefaults.AU_KM;
            bool isOutward = tgtSma > curSma;

            var logits = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double maxLogit = double.MinValue;

            foreach (var body in candidateBodies)
            {
                var p = SolarSystemCatalog.GetPlanet(body);
                if (p == null)
                {
                    logits[body] = 0.0;
                    continue;
                }

                double logit = 0.0;

                // 1. 引力助推能力先验：天体质量越大（Mu 越大），引力偏折潜力越高
                // 木星/土星 > 金星/地球 > 火星/水星
                double muFactor = Math.Log10(Math.Max(1.0, p.Mu / 1000.0));
                logit += muFactor * 0.4;

                // 2. 轨道几何前进方向一致性奖励
                // 若向外太阳系进发，中间天体半长轴介于 curSma 与 tgtSma 之间获得高奖励
                double candSma = p.SMA;
                if (isOutward)
                {
                    if (candSma > curSma && candSma <= tgtSma)
                    {
                        logit += 1.5; // 正向踏板奖励
                    }
                    else if (candSma < curSma)
                    {
                        // 内行星仅在具备显著借力偏折能力 (无量纲借力功率指数) 时提供动态先验加分，杜绝无脑 +0.8 硬编码
                        double vInfInner = MGAPhysicalScales.HohmannVInfKms(SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu, curSma, candSma);
                        double assistIdx = MGAPhysicalScales.AssistPowerIndex(p, Math.Max(vInfInner, 1e-3), SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);
                        if (assistIdx >= MGAPhysicalScales.DefaultMinAssistPowerIndex)
                        {
                            logit += assistIdx * 10.0;
                        }
                    }
                }
                else
                {
                    // 向内太阳系 (如水星/太阳探测)
                    if (candSma < curSma && candSma >= tgtSma)
                    {
                        logit += 1.5;
                    }
                    else if (candSma > curSma)
                    {
                        double vInfOuter = MGAPhysicalScales.HohmannVInfKms(SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu, curSma, candSma);
                        double assistIdx = MGAPhysicalScales.AssistPowerIndex(p, Math.Max(vInfOuter, 1e-3), SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : MGADefaults.FallbackSunMu);
                        if (assistIdx >= MGAPhysicalScales.DefaultMinAssistPowerIndex)
                        {
                            logit += assistIdx * 10.0;
                        }
                    }
                }

                // 3. 终点直达诱引
                if (body.Equals(targetBody, StringComparison.OrdinalIgnoreCase))
                {
                    logit += 2.0; // 鼓励完成收尾
                }

                logits[body] = logit;
                if (logit > maxLogit) maxLogit = logit;
            }

            // Softmax 归一化输出先验概率
            double sumExp = 0.0;
            var expLogits = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in logits)
            {
                double e = Math.Exp(kvp.Value - maxLogit);
                expLogits[kvp.Key] = e;
                sumExp += e;
            }

            foreach (var kvp in expLogits)
            {
                priors[kvp.Key] = sumExp > 1e-9 ? kvp.Value / sumExp : 1.0 / candidateBodies.Count;
            }

            return priors;
        }

        /// <summary>
        /// 状态价值网络评估函数 V(s)
        /// 评估从当前局部拓扑到达最终目标天体的潜力和预期惩罚，输出标量价值 [0.0, 1.0]
        /// </summary>
        public static double EvaluateStateValue(
            List<string> currentSequence,
            string targetBody,
            double accumulatedDeltaVKms,
            double maxDeltaVLimitKms,
            double currentFlightDays,
            double maxFlightDaysLimit)
        {
            if (currentSequence == null || currentSequence.Count == 0) return 0.5;

            string lastBody = currentSequence.Last();
            if (string.Equals(lastBody, targetBody, StringComparison.OrdinalIgnoreCase))
            {
                // 到达目标天体：依据剩余预算给出高价值
                double dvRatio = Math.Max(0.0, 1.0 - (accumulatedDeltaVKms / Math.Max(maxDeltaVLimitKms, 1e-3)));
                double tofRatio = Math.Max(0.0, 1.0 - (currentFlightDays / Math.Max(maxFlightDaysLimit, 1.0)));
                return 0.7 + 0.2 * dvRatio + 0.1 * tofRatio;
            }

            // 中间节点状态评估
            double baseValue = 0.5;

            // 1. 消耗惩罚
            double dvConsumedRatio = accumulatedDeltaVKms / Math.Max(maxDeltaVLimitKms, 1e-3);
            if (dvConsumedRatio > 1.0) return 0.01; // 爆表惩罚
            baseValue -= 0.3 * dvConsumedRatio;

            // 2. 航时消耗惩罚
            double tofConsumedRatio = currentFlightDays / Math.Max(maxFlightDaysLimit, 1.0);
            if (tofConsumedRatio > 1.0) return 0.01;
            baseValue -= 0.2 * tofConsumedRatio;

            // 3. 借力增益奖励 (巨行星作为中继奖励更高)
            var pNode = SolarSystemCatalog.GetPlanet(lastBody);
            if (pNode != null && pNode.Mu > 1e6)
            {
                baseValue += 0.15; // 巨行星中继奖励
            }

            // 4. 距离目标的能量跨度评估
            var lastP = SolarSystemCatalog.GetPlanet(lastBody);
            var tgtP = SolarSystemCatalog.GetPlanet(targetBody);
            if (lastP != null && tgtP != null)
            {
                double rDiffNorm = Math.Abs(lastP.SMA - tgtP.SMA) / MGADefaults.AU_KM;
                // 距离目标越近，折现价值越高
                double proximityBonus = 1.0 / (1.0 + 0.5 * rDiffNorm);
                baseValue *= proximityBonus;
            }

            return Math.Max(0.02, Math.Min(0.98, baseValue));
        }

        /// <summary>
        /// 兼容实例调用: 评估部分序列价值 V(s)
        /// </summary>
        public double EvaluateSequenceValue(
            List<string> partialSequence,
            string targetBody,
            double accumulatedDeltaVKms,
            double maxDeltaVLimitKms)
        {
            return EvaluateStateValue(partialSequence, targetBody, accumulatedDeltaVKms, maxDeltaVLimitKms, 0.0, 10000.0);
        }
    }
}
