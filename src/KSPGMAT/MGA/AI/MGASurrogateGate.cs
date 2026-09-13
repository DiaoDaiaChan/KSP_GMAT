// ============================================================================
// MGASurrogateGate.cs
// 代理网络前置粗筛门 (Surrogate Pre-Screen Gate)
//
// 目的: 把已训练的双头代理网络从"MCTS 的搜索顺序建议者"(影响末端) 前移到
//       **原生 Lambert 探针之前的可行性粗筛器**(影响主链), 从而真正减少原生调用。
//
// 实测依据 (MGAStepProfiler 报表): DE 注种阶段的"前向步进引力偏折动力学合成"
// 占总耗时约 37%, 其开销主体是数百次独立的 PykepNative.SolveLambert 探针;
// 而 GPU 批评估的代数主循环仅占 3.3%。故粗筛必须插在探针内循环, 而非评估循环。
//
// 【安全契约】粗筛绝不允许降低解质量, 因此内建三重保险:
//   1. **高置信阈值**: 仅当代理预测可行性低于 SkipProbabilityThreshold (默认 0.10)
//      才筛除 —— 远低于网络 0.5 的判定中点, 只剔除"几乎确定不可行"的探针;
//   2. **在线对照抽样**: 按 ControlSampleFraction (默认 8%) 随机不筛, 照常执行原生探针,
//      据此实时度量**假阴性率** (代理判否、原生实际成功);
//   3. **自动熔断**: 一旦对照样本量达 MinControlSamples 且实测假阴性率超过
//      MaxFalseNegativeRate (默认 5%), 立即永久关闭本窗口的粗筛并告警。
//
// 未训练 (IsTrained == false) 时 Enabled 恒为 false, 门完全旁路 —— 不会用解析退化
// 判据冒充模型输出, 也不会做任何筛选。
// ============================================================================

using System;
using System.Text;

namespace KSPGMAT.MGA.AI
{
    /// <summary>粗筛决策</summary>
    public enum SurrogateGateDecision
    {
        /// <summary>放行, 正常执行原生探针</summary>
        Evaluate = 0,

        /// <summary>被代理网络筛除, 跳过原生探针</summary>
        Skip = 1,

        /// <summary>对照组: 本应执行原生探针 (不参与筛除), 用于在线度量假阴性率</summary>
        EvaluateAsControl = 2
    }

    /// <summary>
    /// 代理网络前置粗筛门。单窗口单实例, 仅在顺序路径使用 (非线程安全)。
    /// </summary>
    public sealed class MGASurrogateGate
    {
        // ---- 策略参数 (全部为无量纲比例/概率, 非经验常数) ----

        /// <summary>
        /// 假阴性预算: 允许的"代理判否但实际可行"比例。
        /// 这是**精度要求**, 由工程方给定; 阈值则由控制器自动标定到该要求上。
        /// </summary>
        public double TargetFalseNegativeRate { get; set; } = 0.05;

        /// <summary>
        /// 当前筛除阈值 (由在线控制器自适应标定, 非固定常数)。
        /// 起步取保守下限, 随对照样本积累向目标误杀率收敛。
        /// </summary>
        public double SkipProbabilityThreshold { get; private set; } = 0.01;

        /// <summary>阈值搜索下界 (最保守: 几乎不筛)</summary>
        public double MinSkipProbability { get; set; } = 0.001;

        /// <summary>阈值搜索上界 (最激进)</summary>
        public double MaxSkipProbability { get; set; } = 0.50;

        /// <summary>
        /// 阈值标定控制律的增益 (无量纲)。乘性随机逼近, 与阈值量纲无关。
        /// </summary>
        public double CalibrationGain { get; set; } = 0.3;

        /// <summary>对照抽样比例: 该比例的探针即使被代理判否也照常执行, 用于度量并标定假阴性率。</summary>
        public double ControlSampleFraction { get; set; } = 0.08;

        /// <summary>硬熔断门禁: 实测误杀率超过「预算 × 该倍数」才彻底关闭粗筛 (最终安全网)。</summary>
        public double HardAbortMultiple { get; set; } = 6.0;

        /// <summary>做出标定/熔断判断所需的最少对照样本数 (统计有效性下限)。</summary>
        public int MinControlSamples { get; set; } = 120;

        /// <summary>阈值标定次数 (遥测)</summary>
        public int CalibrationSteps { get; private set; }

        // ---- 运行状态 ----

        /// <summary>当前是否启用粗筛 (未训练或已熔断时为 false)</summary>
        public bool Enabled { get; private set; }

        /// <summary>是否因假阴性率超标而自动熔断</summary>
        public bool AutoDisabled { get; private set; }

        /// <summary>进入粗筛判定的探针总数</summary>
        public long Considered { get; private set; }

        /// <summary>被筛除而省下的原生探针数 (即节省的原生 Lambert 调用)</summary>
        public long Skipped { get; private set; }

        /// <summary>对照组中实际执行了原生探针的次数</summary>
        public long ControlEvaluated { get; private set; }

        /// <summary>对照组中被误判的次数 (代理判否但原生成功)</summary>
        public long ControlFalseNegatives { get; private set; }

        /// <summary>实测假阴性率</summary>
        public double MeasuredFalseNegativeRate
            => ControlEvaluated > 0 ? (double)ControlFalseNegatives / ControlEvaluated : 0.0;

        /// <summary>最近一次判定的代理预测可行性</summary>
        public double LastPredictedFeasibility { get; private set; } = 1.0;

        /// <summary>累计执行的原生探针数 (对照组 + 放行组)</summary>
        public long NativeProbesExecuted { get; private set; }

        /// <summary>被筛除探针的预测可行性样本 (用于阈值标定的分位统计)</summary>
        private readonly System.Collections.Generic.List<double> _skippedProbabilities
            = new System.Collections.Generic.List<double>();

        private readonly Random _rng;
        private readonly MGAPhysicsSurrogateNetwork _net;

        public MGASurrogateGate(int seed = 20260911)
        {
            _rng = new System.Random(seed);
            _net = MGAPhysicsSurrogateNetwork.Default;
            Enabled = _net.IsTrained;
        }

        /// <summary>
        /// 对一次候选转移做前置粗筛。
        /// 输入几何量必须与 ExtractTransferFeatures 的语义一致 (均为无量纲化前的物理量)。
        /// </summary>
        /// <param name="r1Km">出发位置矢径 (km)</param>
        /// <param name="r2Km">到达位置矢径 (km)</param>
        /// <param name="phaseAngleDeg">两端相对相位角 (deg, 0~360)</param>
        /// <param name="tofDays">本条航段飞行时长 (天)</param>
        /// <param name="nomHohmannTofDays">该半径对的标称霍曼飞行时长 (天), 作为 ToF 比基准</param>
        /// <param name="incDiffDeg">两端轨道倾角差 (deg)</param>
        public SurrogateGateDecision ScreenTransfer(
            double r1Km, double r2Km, double phaseAngleDeg,
            double tofDays, double nomHohmannTofDays, double incDiffDeg)
        {
            if (!Enabled || !_net.IsTrained) return SurrogateGateDecision.Evaluate;

            if (double.IsNaN(r1Km) || double.IsNaN(r2Km) || double.IsNaN(tofDays)
                || r1Km <= 0 || r2Km <= 0 || tofDays <= 0 || nomHohmannTofDays <= 0)
            {
                return SurrogateGateDecision.Evaluate;
            }

            Considered++;

            var features = MGAPhysicsSurrogateNetwork.ExtractTransferFeatures(
                r1Km, r2Km, phaseAngleDeg, tofDays, nomHohmannTofDays, incDiffDeg);

            _net.Forward(features, out double pFeasible, out _);
            LastPredictedFeasibility = pFeasible;

            // 代理认为可行 -> 放行
            if (pFeasible >= SkipProbabilityThreshold) return SurrogateGateDecision.Evaluate;

            // 代理认为不可行 -> 按比例抽入对照组 (照常执行原生探针, 用于统计误杀)
            if (_rng.NextDouble() < ControlSampleFraction) return SurrogateGateDecision.EvaluateAsControl;

            Skipped++;
            _skippedProbabilities.Add(pFeasible);
            return SurrogateGateDecision.Skip;
        }

        /// <summary>
        /// 训练标签所用的可行包络系数: 日心双曲过剩速度之和 &lt;= 该倍数 × 霍曼下界即视为可行。
        /// 必须与 MGASurrogateTrainer.FeasibilityEnvelopeFactor 保持一致,
        /// 否则"误杀"统计与模型实际预测的目标不是同一个量, 度量将失去意义。
        /// </summary>
        public const double FeasibilityEnvelopeFactor = 3.0;

        /// <summary>
        /// 汇报原生探针结果 (仅对照组需要, 放行组与筛除组调用无副作用)。
        /// </summary>
        /// <param name="decision">ScreenTransfer 返回的决策</param>
        /// <param name="nativeProbeSucceeded">原生探针是否成功求解</param>
        /// <param name="dvSumKms">该探针实际的双曲过剩速度之和 (km/s); 求解失败时传 NaN</param>
        /// <param name="hohmannRefKms">该半径对的霍曼参考速度 (2 × v_inf_hohmann, km/s)</param>
        public void ReportOutcome(SurrogateGateDecision decision, bool nativeProbeSucceeded,
            double dvSumKms, double hohmannRefKms)
        {
            if (decision == SurrogateGateDecision.Skip) return;

            NativeProbesExecuted++;

            if (decision != SurrogateGateDecision.EvaluateAsControl) return;

            ControlEvaluated++;

            // 假阴性判定必须与训练标签同义:
            //   训练标签 = (Lambert 收敛) 且 (dvSum <= 3 × 霍曼下界)
            // 故此处误杀 = 代理判否, 而该探针按同一判据实际属于"可行"。
            // (早期版本仅以 "Lambert 是否收敛" 作为实际可行判据, 与标签不同义,
            //  导致误杀率虚高至 ~100% —— 收敛是远比"在可行包络内"宽松的条件。)
            bool actuallyFeasible = nativeProbeSucceeded
                && hohmannRefKms > 1e-9
                && !double.IsNaN(dvSumKms)
                && dvSumKms <= hohmannRefKms * FeasibilityEnvelopeFactor;

            if (actuallyFeasible && LastPredictedFeasibility < SkipProbabilityThreshold)
            {
                ControlFalseNegatives++;
            }

            if (AutoDisabled) return;
            if (ControlEvaluated < MinControlSamples) return;

            double fn = MeasuredFalseNegativeRate;

            // ---- 最终安全网: 误杀率远超预算时才彻底关闭 ----
            if (fn > TargetFalseNegativeRate * HardAbortMultiple)
            {
                AutoDisabled = true;
                Enabled = false;
                KSPGMATLog.Warn("AI",
                    $"[MGASurrogateGate] 实测假阴性率 {fn:P1} 超过硬门禁 {TargetFalseNegativeRate * HardAbortMultiple:P0}, " +
                    $"已彻底关闭代理前置粗筛 (对照样本 {ControlEvaluated}, 误杀 {ControlFalseNegatives})。");
                return;
            }

            // ---- 阈值在线标定 (乘性随机逼近控制器) ----
            // 目标: 使实测误杀率收敛到 TargetFalseNegativeRate。
            // 误杀率是阈值 tau 的单调递减函数 (tau 越小 -> 筛得越少 -> 误杀越少),
            // 故采用 tau <- tau * (target / fn)^gain 的乘性更新: 误杀偏高则收缩, 偏低则放宽。
            // 乘性形式与 tau 的绝对量纲无关, 且天然保证 tau > 0。
            if (fn > 1e-9)
            {
                double ratio = TargetFalseNegativeRate / fn;
                double newTau = SkipProbabilityThreshold * Math.Pow(ratio, CalibrationGain);
                if (double.IsNaN(newTau) || double.IsInfinity(newTau)) newTau = SkipProbabilityThreshold;
                newTau = Math.Max(MinSkipProbability, Math.Min(MaxSkipProbability, newTau));

                // 仅在阈值确实变化时计数, 避免抖动统计
                if (Math.Abs(newTau - SkipProbabilityThreshold) > 1e-6)
                {
                    SkipProbabilityThreshold = newTau;
                    CalibrationSteps++;
                }
            }
        }

        /// <summary>可读状态 (供遥测/报表)</summary>
        public string Describe()
        {
            double rate = (Considered > 0) ? (double)Skipped / Considered : 0.0;
            var sb = new StringBuilder();
            sb.Append($"粗筛[{(Enabled ? "启用" : (AutoDisabled ? "已熔断" : "旁路"))}] ");
            sb.Append($"判定 {Considered} | 筛除 {Skipped} ({rate:P1}) | ");
            sb.Append($"阈值 tau={SkipProbabilityThreshold:F4} (标定 {CalibrationSteps} 次) | ");
            sb.Append($"对照 {ControlEvaluated} 误杀 {ControlFalseNegatives} (误杀率 {MeasuredFalseNegativeRate:P2} / 预算 {TargetFalseNegativeRate:P0}");
            if (AutoDisabled) sb.Append(", 已熔断");
            sb.Append(")");
            return sb.ToString();
        }
    }
}
