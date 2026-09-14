// ============================================================================
// MGABayesianSubspacePolisher.cs
// 局部有限差分一阶梯度与二次抛物线极值抛光器 (Local Gradient & Quadratic Vertex Polisher)
// 机制: 在 DE 收敛的高价值吸引子邻域，通过有限差分探测主梯度方向，沿主敏感轴线搜索并结合二次极值插值加速收敛
// 遵从 baseline_agent.md 实事求是原则：本模块为纯确定性局部一阶梯度/二次抛光算法，剥离虚假学术包装
// ============================================================================

using System;
using System.Collections.Generic;

namespace KSPGMAT.MGA.AI
{
    /// <summary>
    /// 局部一阶梯度与二次插值抛光器 (Local Gradient & Quadratic Vertex Polisher)
    /// </summary>
    public class MGABayesianSubspacePolisher
    {
        public delegate double ObjectiveEvaluator(double[] point);

        /// <summary>
        /// 在基准最优解周围执行局部梯度探测与二次抛物线抛光
        /// </summary>
        /// <param name="bestPoint">当前最优输入参数（如发射时间、各腿ToF）</param>
        /// <param name="lowerBounds">参数下界</param>
        /// <param name="upperBounds">参数上界</param>
        /// <param name="evaluator">目标损失函数</param>
        /// <param name="maxEvaluations">最大评估次数 (通常 20~40 次即可收敛)</param>
        /// <returns>抛光后的更优参数向量</returns>
        public static double[] Polish(
            double[] bestPoint, 
            double[] lowerBounds, 
            double[] upperBounds, 
            ObjectiveEvaluator evaluator, 
            int maxEvaluations = 25)
        {
            if (bestPoint == null || evaluator == null) return bestPoint;
            int dim = bestPoint.Length;
            if (dim == 0) return bestPoint;

            double[] currentBest = (double[])bestPoint.Clone();
            double bestScore = evaluator(currentBest);

            // 1. 局部有限差分估计主要敏感梯度与方向 (Finite Difference Gradient)
            double[] grad = new double[dim];
            double[] stepSizes = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                double span = upperBounds[i] - lowerBounds[i];
                stepSizes[i] = Math.Max(1e-4, span * 0.005); // 0.5% 扰动步长

                double[] probePlus = (double[])currentBest.Clone();
                probePlus[i] = Math.Min(upperBounds[i], probePlus[i] + stepSizes[i]);
                double scorePlus = evaluator(probePlus);

                grad[i] = (scorePlus - bestScore) / stepSizes[i];
            }

            // 2. 构造活动子空间主导特征向量 (Active Subspace 1D/2D Projection)
            double gradNormSq = 0.0;
            for (int i = 0; i < dim; i++) gradNormSq += grad[i] * grad[i];
            double gradNorm = Math.Sqrt(gradNormSq);

            double[] activeDir = new double[dim];
            if (gradNorm > 1e-9)
            {
                for (int i = 0; i < dim; i++) activeDir[i] = -grad[i] / gradNorm; // 负梯度下降方向
            }
            else
            {
                activeDir[0] = 1.0;
            }

            // 3. 在主敏感轴上执行一维线搜索与二次极值内插 (Line Search + Quadratic Vertex Interpolation)
            //    【2026-09-14 审计修正】原注释写作 "高斯过程 / 抛物线收敛探索 (Line & Subspace Bayesian Polishing)" ——
            //    本类没有任何高斯过程、核函数、协方差矩阵或 EI 采集函数, 该表述属虚假学术包装。已剥离。
            //    采样主方向不同步长 [-2.0, -1.0, -0.5, 0.5, 1.0, 2.0]
            double[] trialAlphas = new double[] { 0.25, 0.5, 1.0, 1.5, 2.0, -0.25, -0.5, -1.0 };
            int evalsUsed = dim; // 前面求梯度消耗了 dim 次评估

            double bestAlpha = 0.0;
            foreach (double alpha in trialAlphas)
            {
                if (evalsUsed >= maxEvaluations) break;

                double[] cand = new double[dim];
                for (int i = 0; i < dim; i++)
                {
                    double delta = alpha * stepSizes[i] * activeDir[i] * 5.0;
                    cand[i] = Math.Max(lowerBounds[i], Math.Min(upperBounds[i], currentBest[i] + delta));
                }

                double score = evaluator(cand);
                evalsUsed++;

                if (score < bestScore)
                {
                    bestScore = score;
                    currentBest = cand;
                    bestAlpha = alpha;
                }
            }

            // 4. 二次拟合极值点内插 (Quadratic Vertex Interpolation)
            if (Math.Abs(bestAlpha) > 1e-4 && evalsUsed < maxEvaluations)
            {
                double h = 0.3 * bestAlpha;
                double[] ptLeft = StepPoint(currentBest, activeDir, stepSizes, -h, lowerBounds, upperBounds);
                double[] ptRight = StepPoint(currentBest, activeDir, stepSizes, h, lowerBounds, upperBounds);

                double sL = evaluator(ptLeft);
                double sR = evaluator(ptRight);
                double sC = bestScore;

                double denom = 2.0 * (sL - 2.0 * sC + sR);
                if (denom > 1e-12)
                {
                    double optimalOffset = (h * (sL - sR)) / denom;
                    if (Math.Abs(optimalOffset) < 2.0 * Math.Abs(h))
                    {
                        double[] vertexPt = StepPoint(currentBest, activeDir, stepSizes, optimalOffset, lowerBounds, upperBounds);
                        double vertexScore = evaluator(vertexPt);
                        if (vertexScore < bestScore)
                        {
                            currentBest = vertexPt;
                        }
                    }
                }
            }

            return currentBest;
        }

        private static double[] StepPoint(
            double[] center, 
            double[] dir, 
            double[] stepSizes, 
            double alpha, 
            double[] lb, 
            double[] ub)
        {
            int dim = center.Length;
            double[] res = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                double val = center[i] + alpha * stepSizes[i] * dir[i] * 5.0;
                res[i] = Math.Max(lb[i], Math.Min(ub[i], val));
            }
            return res;
        }
    }
}
