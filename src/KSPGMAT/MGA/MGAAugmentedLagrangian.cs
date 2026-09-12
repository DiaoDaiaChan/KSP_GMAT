// ============================================================================
// MGAAugmentedLagrangian.cs
// 增广拉格朗日自适应约束乘子管理器 (Augmented Lagrangian Multiplier Manager)
//
// 取代方案: 原实现用手调惩罚常数 (QuadraticBarrierScale=25000, PoweredFlybyPenaltyScale=2500,
//           PenaltyInfeasibleBase=10000 ...) 把约束硬压进目标函数。这些常数有三个致命缺陷:
//             (a) 带量纲混入无量纲主目标, 导致主目标梯度被淹没 (尺度差 4 个数量级);
//             (b) 无标定依据, 换任务/换恒星系即失效;
//             (c) 无论怎么调, 收敛解都只是"罚到够小", 而非严格满足约束。
//
// 数学替代: 增广拉格朗日法 (ALM / Powell-Hestenes-Rockafellar)
//   对约束 g_k(x) <= 0 (此处统一改写为无量纲违约量 g_hat_k >= 0):
//
//     L(x, λ, ρ) = f(x) + Σ_k [ λ_k · g_hat_k(x) + (ρ_k / 2) · g_hat_k(x)^2 ]
//
//   对偶上升 (每次迭代 / 每代):
//     λ_k  <-  clip( λ_k + ρ_k · g_hat_k(x_feasible_best) , 0 , λ_max )
//
//   罚参数自适应 (仅在违背量未按预期收缩时增长):
//     if ( g_hat_k 未下降至 (1 - contraction) 倍 )  ρ_k <- min(ρ_max, β · ρ_k)
//
// 关键性质 (为什么这比手调常数好):
//   1. λ_k 与 ρ_k 由违背量自身驱动 ⇒ 无需任何标定, 天然跨任务/跨恒星系成立;
//   2. 迭代收敛时 λ_k 有限且 g_hat_k -> 0 ⇒ **严格满足约束**, 而非近似;
//   3. g_hat_k 无量纲 ⇒ 惩罚项与归一化主目标同尺度, 梯度信息不再被淹没;
//   4. ρ_init = 1 是唯一自洽初值: 在 g_hat = 1 (刚好越界一个基准尺度) 时,
//      二次项与线性项等权, 不存在可调空间 ⇒ 彻底消除魔法数字。
// ============================================================================

using System;
using System.Text;
using System.Threading;

namespace KSPGMAT.MGA
{
    /// <summary>MGA 求解过程中受约束的物理量标识</summary>
    public enum MGAConstraint
    {
        LegFeasibility = 0,   // 航段开普勒几何可行 (lanbert 有解且非退化)
        DepartureEpoch = 1,   // 发射历元落在搜索窗口内
        DsmBudget = 2,        // 总深空机动 <= MaxDsmDeltaVMs
        VesselBudget = 3,     // 总工质机动 <= VesselMaxDeltaVMs
        LaunchCeiling = 4,    // 始发注入冲量 <= LaunchDeltaVCeilingMs
        SafePeriapsis = 5,    // 飞掠近心点 >= 天体安全半径
        PoweredFlyby = 6,     // 纯无动力飞掠 (DeltaV_fb -> 0)
        InclinationLock = 7,  // 发射倾角锁定
        FlybyDeflection = 8,  // 双曲偏折几何可容纳所需转角
        MissionDuration = 9,  // 总航时 <= 上限
        Count = 10
    }

    /// <summary>
    /// 增广拉格朗日乘子管理器。一次 MGA 求解 (单窗口) 持有一个实例, 跨代演化。
    /// 线程安全: Penalty() 只读 λ/ρ, 仅对"本代最大违背"做加锁记录;
    /// EndIteration() 由主线程在每代末尾调用一次。
    /// </summary>
    public sealed class MGAAugmentedLagrangian
    {
        private const int N = (int)MGAConstraint.Count;

        private readonly double[] _lambda = new double[N];
        private readonly double[] _rho = new double[N];
        private readonly double[] _violationPrev = new double[N];
        private readonly double[] _violationIterMax = new double[N];
        private readonly object _sync = new object();

        // ---- 无量纲算法参数 (全部由自洽性推导, 非经验值) ----

        /// <summary>罚参数初值。由 g_hat=1 时线性项与二次项等权自洽推出 ⇒ 必须为 1。</summary>
        public double RhoInit { get; set; } = 1.0;

        /// <summary>罚参数上限 (防数值爆炸)。1e6 使单项惩罚最大约 5e11, 远低于 double 溢出阈值。</summary>
        public double RhoMax { get; set; } = 1.0e6;

        /// <summary>罚参数增长因子。2.0 为 ALM 文献标准 (每轮翻倍)。</summary>
        public double RhoGrowth { get; set; } = 2.0;

        /// <summary>乘子上限, 防止病态问题的 λ 发散。</summary>
        public double LambdaMax { get; set; } = 1.0e6;

        /// <summary>
        /// 收缩判据: 若本代违背量未降至上一代的 (1 - Contraction) 倍以下, 即认为约束未被有效压制。
        /// 0.25 表示"至少下降 25% 才算有效进展"。
        /// </summary>
        public double Contraction { get; set; } = 0.25;

        /// <summary>可行判定阈值 (无量纲)。纯数值下溢容差, 非调参旋钮。</summary>
        public const double FeasibilityEpsilon = 1.0e-9;

        public MGAAugmentedLagrangian()
        {
            Reset();
        }

        /// <summary>复位全部乘子与罚参数 (每次单窗口演化开始时调用)</summary>
        public void Reset()
        {
            lock (_sync)
            {
                for (int k = 0; k < N; k++)
                {
                    _lambda[k] = 0.0;
                    _rho[k] = RhoInit;
                    _violationPrev[k] = 0.0;
                    _violationIterMax[k] = 0.0;
                }
                Iterations = 0;
            }
        }

        /// <summary>已执行的 ALM 迭代代数</summary>
        public int Iterations { get; private set; }

        /// <summary>本代所有约束中的最大无量纲违约量 (0 表示完全可行)</summary>
        public double MaxNormalizedViolation
        {
            get
            {
                lock (_sync)
                {
                    double m = 0.0;
                    for (int k = 0; k < N; k++) if (_violationIterMax[k] > m) m = _violationIterMax[k];
                    return m;
                }
            }
        }

        /// <summary>当前是否严格满足全部约束 (无量纲违约量在数值容差内归零)</summary>
        public bool IsFeasible => MaxNormalizedViolation <= FeasibilityEpsilon;

        /// <summary>
        /// 计算单个约束的增广拉格朗日惩罚项 φ_k = λ_k·ĝ_k + (ρ_k/2)·ĝ_k²,
        /// 并记录本代该约束的最大违背量 (供 EndIteration 对偶上升使用)。
        /// </summary>
        /// <param name="c">约束标识</param>
        /// <param name="normalizedViolation">无量纲违约量 ĝ_k >= 0</param>
        public double Penalty(MGAConstraint c, double normalizedViolation)
        {
            int k = (int)c;
            if (k < 0 || k >= N) return 0.0;

            double g = normalizedViolation;
            if (double.IsNaN(g) || g <= 0.0) return 0.0;
            if (double.IsInfinity(g)) g = 1.0e12;

            double lam, rho;
            lock (_sync)
            {
                lam = _lambda[k];
                rho = _rho[k];
                if (g > _violationIterMax[k]) _violationIterMax[k] = g;
            }

            return lam * g + 0.5 * rho * g * g;
        }

        /// <summary>
        /// 每代末尾调用一次: 执行对偶上升与罚参数自适应。
        /// 返回本代最大违约量, 便于日志。
        /// </summary>
        public double EndIteration()
        {
            lock (_sync)
            {
                Iterations++;
                double maxV = 0.0;

                for (int k = 0; k < N; k++)
                {
                    double g = _violationIterMax[k];
                    if (g > maxV) maxV = g;

                    // ---- 对偶上升: λ_k <- clip(λ_k + ρ_k · ĝ_k, 0, λ_max) ----
                    if (g > 0.0)
                    {
                        double next = _lambda[k] + _rho[k] * g;
                        _lambda[k] = next > LambdaMax ? LambdaMax : next;

                        // ---- 罚参数自适应: 违背量未有效收缩则加倍 ----
                        double prev = _violationPrev[k];
                        bool improvedEnough = (prev > 0.0) && (g <= prev * (1.0 - Contraction))
                                              && (g / prev <= (1.0 - Contraction));
                        if (!improvedEnough)
                        {
                            double grown = _rho[k] * RhoGrowth;
                            _rho[k] = grown > RhoMax ? RhoMax : grown;
                        }
                    }

                    _violationPrev[k] = g;
                    _violationIterMax[k] = 0.0; // 清零, 进入下一代统计
                }

                return maxV;
            }
        }

        /// <summary>供诊断报表使用的可读状态 (仅列出已被激活的约束)</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"ALM[代数={Iterations}, 最大违背={MaxNormalizedViolation:E3}, 可行={IsFeasible}]");
            lock (_sync)
            {
                for (int k = 0; k < N; k++)
                {
                    if (_lambda[k] > 0.0 || _rho[k] > RhoInit)
                    {
                        sb.Append($" {((MGAConstraint)k)}:λ={_lambda[k]:G4},ρ={_rho[k]:G4}");
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>导出乘子/罚参数快照 (供机器学习标定与回归分析使用)</summary>
        public void ExportSnapshot(double[] lambdaOut, double[] rhoOut)
        {
            lock (_sync)
            {
                for (int k = 0; k < N; k++)
                {
                    if (lambdaOut != null && k < lambdaOut.Length) lambdaOut[k] = _lambda[k];
                    if (rhoOut != null && k < rhoOut.Length) rhoOut[k] = _rho[k];
                }
            }
        }
    }
}
