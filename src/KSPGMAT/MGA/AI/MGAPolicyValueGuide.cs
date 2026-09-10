// ============================================================================
// MGAPolicyValueGuide.cs
// AlphaZero 范式 Policy-Value 策略与价值引导引擎 (Policy-Value MCTS Guidance)
// 理论来源: Silver et al. (2017) "Mastering Chess and Shogi by Self-Play with a General Reinforcement Learning Algorithm"
// 机制: 为 MCTS 提供天体选择先验分布 P(s, a) 与部分轨迹价值评估 V(s)，替代均一展开与深层盲目 Rollout
// ============================================================================

using System;
using System.Collections.Generic;
using KSPGMAT.Model;

namespace KSPGMAT.MGA.AI
{
    /// <summary>
    /// MCTS 拓扑探索的 Policy-Value 引导器
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

            double curSma = curPlanet != null ? curPlanet.SMA : 1.496e8;
            double tgtSma = tgtPlanet != null ? tgtPlanet.SMA : 1.496e8;
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
                        // 内行星 (如金星) 依然可作为共振蓄能跳板 (如 VEEGA)
                        logit += 0.8;
                    }
                }
                else
                {
                    // 向内太阳系 (如水星/太阳探测)
                    if (candSma < curSma && candSma >= tgtSma)
                    {
                        logit += 1.5;
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

            // Softmax 归一化为合法概率分布
            double sumExp = 0.0;
            foreach (var kv in logits)
            {
                sumExp += Math.Exp(kv.Value - maxLogit);
            }

            foreach (var kv in logits)
            {
                double prob = sumExp > 0.0 ? (Math.Exp(kv.Value - maxLogit) / sumExp) : (1.0 / candidateBodies.Count);
                priors[kv.Key] = prob;
            }

            return priors;
        }

        /// <summary>
        /// 价值网络前向评估: 预测部分拓扑序列的最终可行性期望价值 V(s) in [0, 1]
        /// 代替深层随机 Rollout，直接评估当前节点状态的成单潜力
        /// </summary>
        public double EvaluateSequenceValue(
            List<string> partialSequence, 
            string targetBody, 
            double accumulatedDeltaVKms, 
            double maxDeltaVLimitKms)
        {
            if (partialSequence == null || partialSequence.Count == 0) return 0.0;

            string lastBody = partialSequence[partialSequence.Count - 1];

            // 1. 已超能耗预算惩罚
            if (accumulatedDeltaVKms >= maxDeltaVLimitKms)
            {
                return 0.01;
            }

            // 2. 剩余能耗裕度评分
            double remainingMargin = (maxDeltaVLimitKms - accumulatedDeltaVKms) / Math.Max(1.0, maxDeltaVLimitKms);
            double baseValue = Math.Max(0.0, Math.Min(1.0, remainingMargin));

            // 3. 若已抵达目标，获得高额完成收益
            if (lastBody.Equals(targetBody, StringComparison.OrdinalIgnoreCase))
            {
                return Math.Min(1.0, 0.5 + 0.5 * baseValue);
            }

            // 4. 距离目标的能量跨度评估
            var lastP = SolarSystemCatalog.GetPlanet(lastBody);
            var tgtP = SolarSystemCatalog.GetPlanet(targetBody);
            if (lastP != null && tgtP != null)
            {
                double rDiffNorm = Math.Abs(lastP.SMA - tgtP.SMA) / 1.496e8;
                // 距离目标越近，折现价值越高
                double proximityBonus = 1.0 / (1.0 + 0.5 * rDiffNorm);
                baseValue *= proximityBonus;
            }

            return Math.Max(0.02, Math.Min(0.98, baseValue));
        }
    }
}
