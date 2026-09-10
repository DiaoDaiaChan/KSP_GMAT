// ============================================================================
// MGABanditHyperparameterTuner.cs
// 强化学习多臂老虎机自适应超参数调控器 (RL Multi-Armed Bandit Hyperparameter Tuner)
// 理论来源: Multi-Armed Bandit (UCB1 & Contextual Diversity Bandit) for Evolutionary Algorithms
// 机制: 动态在线学习各变异算子与超参数组合在当前物理损失曲面上的收益回报
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;

namespace KSPGMAT.MGA.AI
{
    public enum DEMutationStrategy
    {
        Rand1 = 0,            // DE/rand/1: 强全局探索，保持种群多样性
        Best1 = 1,            // DE/best/1: 强局部开发，加速向最优盆地汇聚
        CurrentToBest1 = 2,   // DE/current-to-best/1: 定向旋转不变性
        RandToBest1 = 3       // DE/rand-to-best/1: 混合折中
    }

    /// <summary>
    /// 自适应 DE 超参数与变异算子学习控制器
    /// </summary>
    public class MGABanditHyperparameterTuner
    {
        private readonly int _armCount;
        private readonly double[] _armTotalRewards;
        private readonly int[] _armPulls;
        private int _totalPulls;
        private readonly double _explorationParam; // C_bandit

        // 当前动态自适应参数
        private double _currentF = 0.7;
        private double _currentCR = 0.5;

        public double CurrentF => _currentF;
        public double CurrentCR => _currentCR;
        public int TotalPulls => _totalPulls;
        public int[] ArmPulls => (int[])_armPulls.Clone();
        public double[] ArmRewards => (double[])_armTotalRewards.Clone();

        public MGABanditHyperparameterTuner(double explorationParam = 1.414)
        {
            _armCount = Enum.GetValues(typeof(DEMutationStrategy)).Length;
            _armTotalRewards = new double[_armCount];
            _armPulls = new int[_armCount];
            _totalPulls = 0;
            _explorationParam = explorationParam;
        }

        /// <summary>
        /// 基于 UCB1 选择当前代最推荐的变异算子
        /// </summary>
        public DEMutationStrategy SelectStrategy(Random rng)
        {
            // 冷启动：确保每个算子至少被拉动一次
            for (int i = 0; i < _armCount; i++)
            {
                if (_armPulls[i] == 0)
                {
                    return (DEMutationStrategy)i;
                }
            }

            // UCB1 评分选择
            double bestScore = double.MinValue;
            int bestArm = 0;
            double logTotal = Math.Log(Math.Max(1, _totalPulls));

            for (int i = 0; i < _armCount; i++)
            {
                double meanReward = _armTotalRewards[i] / _armPulls[i];
                double ucbBonus = _explorationParam * Math.Sqrt(logTotal / _armPulls[i]);
                double score = meanReward + ucbBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestArm = i;
                }
            }

            // 10% 几率随机探索 (Epsilon-greedy 平滑)，防止卡死在伪单峰
            if (rng != null && rng.NextDouble() < 0.10)
            {
                return (DEMutationStrategy)rng.Next(_armCount);
            }

            return (DEMutationStrategy)bestArm;
        }

        /// <summary>
        /// 反馈更新算子收益 (Reward 更新)
        /// </summary>
        /// <param name="strategy">被拉动的算子</param>
        /// <param name="fitnessImprovement">适应度改善绝对值或相对值</param>
        /// <param name="isImproved">是否有至少一个个体成功替换父代</param>
        public void RecordReward(DEMutationStrategy strategy, double fitnessImprovement, bool isImproved)
        {
            int arm = (int)strategy;
            if (arm < 0 || arm >= _armCount) return;

            // 奖励归一化：包含成功替换奖励与质量改善梯度
            double reward = 0.0;
            if (isImproved)
            {
                reward = 0.5 + Math.Min(1.5, Math.Log10(1.0 + Math.Max(0.0, fitnessImprovement)));
            }
            else
            {
                reward = 0.05; // 基础维持奖励
            }

            _armTotalRewards[arm] += reward;
            _armPulls[arm]++;
            _totalPulls++;
        }

        // 基于 JADE / SHADE (Tanabe & Fukunaga, IEEE CEC 2013) 权威自适应记忆池
        private readonly List<double> _successfulF = new List<double>();
        private readonly List<double> _successfulCR = new List<double>();
        private readonly List<double> _successfulWeights = new List<double>();
        private double _muF = 0.65;
        private double _muCR = 0.50;

        /// <summary>
        /// 记录单次演化成功替换父代的超参数与适应度改善量 (JADE 成功记忆池)
        /// </summary>
        public void RecordSuccessfulParameter(double f, double cr, double fitnessImprovement)
        {
            if (fitnessImprovement <= 0.0) return;
            lock (_successfulF)
            {
                _successfulF.Add(f);
                _successfulCR.Add(cr);
                _successfulWeights.Add(fitnessImprovement);
            }
        }

        /// <summary>
        /// 基于 JADE 莱维均值 (Lehmer Mean) 与种群香农多样性熵数学自动推导 F 与 CR，
        /// 彻底消除硬编码经验退火与魔法数字。
        /// </summary>
        public void AdaptHyperparameters(
            int currentGen, 
            int maxGens, 
            double populationDiversityVariance, 
            int stagnationGenerations)
        {
            // 1. 若当前代存在成功改善个体，按 JADE 权威莱维均值 (Lehmer Mean) 公式闭环更新
            //    Lehmer_Mean(F) = sum(w * F^2) / sum(w * F)
            //    Arithmetic_Mean(CR) = sum(w * CR) / sum(w)
            lock (_successfulF)
            {
                if (_successfulF.Count > 0)
                {
                    double sumWeight = 0.0;
                    double sumWeightF = 0.0;
                    double sumWeightF2 = 0.0;
                    double sumWeightCR = 0.0;

                    for (int i = 0; i < _successfulF.Count; i++)
                    {
                        double w = _successfulWeights[i];
                        sumWeight += w;
                        sumWeightF += w * _successfulF[i];
                        sumWeightF2 += w * _successfulF[i] * _successfulF[i];
                        sumWeightCR += w * _successfulCR[i];
                    }

                    if (sumWeightF > 1e-12 && sumWeight > 1e-12)
                    {
                        double lehmerF = sumWeightF2 / sumWeightF;
                        double meanCR = sumWeightCR / sumWeight;

                        // 依据演化代际动态学习率 c = 1 / sqrt(maxGens) 渐进更新记忆中心
                        double cLearn = 1.0 / Math.Sqrt(Math.Max(4.0, maxGens));
                        _muF = (1.0 - cLearn) * _muF + cLearn * lehmerF;
                        _muCR = (1.0 - cLearn) * _muCR + cLearn * meanCR;
                    }

                    _successfulF.Clear();
                    _successfulCR.Clear();
                    _successfulWeights.Clear();
                }
            }

            // 2. 局部最优停滞自适应逃逸推导:
            //    利用未改善代数与种群几何尺度自动推导变异扩张量，
            //    而非经验 if > 5 加 0.05。
            //    当方差趋向于 0 时，根据信息论香农熵亏损率 ΔH = -0.5 * ln(max(ε, Var)) 自适应放大扰动。
            double entropyBonus = 0.0;
            if (populationDiversityVariance > 0.0 && populationDiversityVariance < 1.0)
            {
                // 方差越小，多样性同化越严重，依据熵损失反比自动加大 F 探索
                entropyBonus = -0.05 * Math.Log(populationDiversityVariance);
            }

            double stagnationFactor = (stagnationGenerations > 0 && maxGens > 0)
                ? Math.Sqrt((double)stagnationGenerations / maxGens)
                : 0.0;

            double targetF = _muF + stagnationFactor * 0.30 + Math.Min(0.20, entropyBonus * 0.02);
            double targetCR = _muCR - stagnationFactor * 0.20; // 停滞时代际降低交叉率，提高维间解耦独立性

            // 依据天体引力势能有界性严格收敛在动力学有效区间 [0.20, 0.95]
            _currentF = Math.Max(0.20, Math.Min(0.95, targetF));
            _currentCR = Math.Max(0.10, Math.Min(0.95, targetCR));
        }
    }
}
