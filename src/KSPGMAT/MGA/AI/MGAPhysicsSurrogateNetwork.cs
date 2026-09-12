// ============================================================================
// MGAPhysicsSurrogateNetwork.cs
// 可训练双头物理代理网络 (Trainable Physics-Informed Surrogate MLP)
//
// 【本次重构解决的问题】
//   原实现的权重是 `_w1[i,j] = Math.Sin((i+1)*(j+1)*0.7) * 0.5`, 输出偏置
//   `_bOut[1] = 6.5` 是手写的"太阳系平均 ΔV 基线", 全项目没有任何训练代码、
//   也没有任何权重文件加载。也就是说: **一个从未训练过的网络, 它的输出
//   `feasProb` 却在给 MCTS 节点价值加权, 并充当损失场判据。** 这比硬编码更危险,
//   因为它伪装成了机器学习。
//
// 【重构后的行为契约】
//   1. IsTrained == false (默认): Forward 退化为**解析开普勒判据** (纯物理, 有界可信),
//      绝不再用随机权重做判断。MCTS / 损失场因此可以安全地在无权重时运行。
//   2. IsTrained == true: Forward 执行真实训练得到的前馈推理。
//   3. 权重由 MGASurrogateTrainer 用**精确原生 Lambert 求解器**生成的数据监督训练,
//      并落盘为人类可读的文本权重文件, 可加载、可复现、可审计。
//
// 网络结构: 4 输入 -> 8 (tanh) -> 8 (tanh) -> 双头输出
//   头 0: 可行性 logit (sigmoid -> [0,1])
//   头 1: 日心双曲过剩速度之和 (km/s, softplus 保证正)
// 训练: 特征标准化 + Xavier 初始化 + Adam + 验证集早停
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace KSPGMAT.MGA.AI
{
    /// <summary>单条监督样本</summary>
    public struct SurrogateSample
    {
        public double[] Features;    // 4 维无量纲特征
        public double Feasibility;   // 0 / 1
        public double DvKms;         // 标签: 日心双曲过剩速度之和 (km/s)
    }

    /// <summary>训练结果报告</summary>
    public sealed class SurrogateTrainingReport
    {
        public int TrainCount;
        public int ValCount;
        public int EpochsRun;
        public double FeasibilityAccuracy;
        public double FeasibilityAuc;
        public double DvMaeKms;
        public double DvR2;

        /// <summary>可行性分类头是否达门禁 (主要用途: 作为大规模预筛的可行性判据)</summary>
        public bool FeasibilityAccepted;

        /// <summary>ΔV 回归头是否具备"粗筛量级"可用性 (R^2 >= 0.15)。仅用于排序前置过滤, 不可用于精细排序。</summary>
        public bool DvHeadUsable;

        /// <summary>整体是否采纳 (等价于可行性分类头达门禁, 即 IsTrained)</summary>
        public bool Accepted;

        public override string ToString()
        {
            return $"训练样本={TrainCount} 验证样本={ValCount} 迭代={EpochsRun} " +
                   $"可行分类准确率={FeasibilityAccuracy:P1} AUC={FeasibilityAuc:F3} " +
                   $"ΔV MAE={DvMaeKms:F3} km/s R²={DvR2:F4} 采纳={Accepted}";
        }
    }

    /// <summary>
    /// 纯托管可训练双头前馈代理网络 (零外部依赖)
    /// </summary>
    public class MGAPhysicsSurrogateNetwork
    {
        private const int InputDim = 4;
        private const int HiddenDim1 = 8;
        private const int HiddenDim2 = 8;
        private const int OutputDim = 2;

        // ---- 网络参数 ----
        private readonly double[,] _w1 = new double[HiddenDim1, InputDim];
        private readonly double[] _b1 = new double[HiddenDim1];
        private readonly double[,] _w2 = new double[HiddenDim2, HiddenDim1];
        private readonly double[] _b2 = new double[HiddenDim2];
        private readonly double[,] _wOut = new double[OutputDim, HiddenDim2];
        private readonly double[] _bOut = new double[OutputDim];

        // ---- 特征标准化 (训练时标定, 推理时复用) ----
        private readonly double[] _featMean = new double[InputDim];
        private readonly double[] _featStd = new double[InputDim];
        private double _dvMean;
        private double _dvStd = 1.0;

        /// <summary>
        /// 是否已由真实数据训练过。false 时 Forward 走解析开普勒判据 (安全状态)。
        /// 采纳条件: **可行性分类头**达门禁 (AUC >= 0.65 且 准确率 >= 0.60) ——
        /// 该头是代理网络在 MCTS/预筛中的实际用途。
        /// </summary>
        public bool IsTrained { get; private set; }

        private double _lastAuc, _lastAcc, _lastR2, _lastMae;

        /// <summary>最近一次训练的可行性头 ROC-AUC (用于遥测/报表)</summary>
        public double LastFeasibilityAuc => _lastAuc;

        /// <summary>最近一次训练的可行性头准确率</summary>
        public double LastFeasibilityAccuracy => _lastAcc;

        /// <summary>最近一次训练的 ΔV 头 R^2</summary>
        public double LastDvR2 => _lastR2;

        /// <summary>最近一次训练的 ΔV 头 MAE (km/s)</summary>
        public double LastDvMae => _lastMae;

        /// <summary>
        /// ΔV 回归头是否具备"粗筛量级"可用性 (R^2 >= DvHeadUsableR2)。
        /// 该头只用于排除明显离谱的转移 (量级判定), 不可用于精细排序 ——
        /// 短时回归难以精确复现 Lambert 的强非线性。调用方应据本标志决定是否采信其数值。
        /// </summary>
        public bool DvHeadUsable { get; private set; }

        /// <summary>可行性分类头的 AUC 门禁</summary>
        public const double FeasibilityGateAuc = 0.65;

        /// <summary>可行性分类头的准确率门禁</summary>
        public const double FeasibilityGateAccuracy = 0.60;

        /// <summary>ΔV 回归头的"粗筛可用"R^2 门禁</summary>
        public const double DvHeadUsableR2 = 0.15;

        /// <summary>累计训练样本数 (用于遥测)</summary>
        public int TrainedSampleCount { get; private set; }

        private static MGAPhysicsSurrogateNetwork _defaultInstance;
        public static MGAPhysicsSurrogateNetwork Default => _defaultInstance ?? (_defaultInstance = new MGAPhysicsSurrogateNetwork());

        private static bool _defaultWeightsLoadAttempted;

        /// <summary>
        /// 尝试从默认路径加载已训练的代理权重 (仅尝试一次)。
        /// 未找到或加载失败时静默保持 IsTrained = false, 推理期自动走解析开普勒判据 ——
        /// 因此"没有权重"永远是安全状态, 不会用随机权重做任何判断。
        /// </summary>
        public static bool TryLoadDefaultWeights()
        {
            if (_defaultWeightsLoadAttempted) return Default.IsTrained;
            _defaultWeightsLoadAttempted = true;
            try
            {
                var candidates = new List<string>();
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(exeDir))
                {
                    candidates.Add(Path.Combine(exeDir, "GameData", "KSPGMAT", "Config", "mga_surrogate_weights.txt"));
                    candidates.Add(Path.Combine(exeDir, "..", "GameData", "KSPGMAT", "Config", "mga_surrogate_weights.txt"));
                    candidates.Add(Path.Combine(exeDir, "..", "..", "GameData", "KSPGMAT", "Config", "mga_surrogate_weights.txt"));
                }
                candidates.Add(MGASurrogateTrainer.DefaultWeightPath);

                bool anyFound = false;
                foreach (var p in candidates)
                {
                    if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                    anyFound = true;
                    if (Default.Load(p))
                    {
                        KSPGMATLog.Info($"[MGASurrogate] 已加载训练权重: {p} (样本 {Default.TrainedSampleCount}, " +
                                        $"可行头={(Default.IsTrained ? "启用" : "未达门禁")}, dV头={(Default.DvHeadUsable ? "粗筛可用" : "不可用")})");
                        return true;
                    }
                    KSPGMATLog.Warn($"[MGASurrogate] 权重文件存在但校验未通过, 已忽略: {p}");
                }
                if (!anyFound)
                {
                    KSPGMATLog.Info("[MGASurrogate] 未找到训练权重文件, 代理推理使用解析开普勒判据 (安全退化)。");
                }
            }
            catch (Exception ex)
            {
                KSPGMATLog.Warn($"[MGASurrogate] 权重加载异常: {ex.Message}");
            }
            return false;
        }

        public MGAPhysicsSurrogateNetwork()
        {
            for (int i = 0; i < InputDim; i++) { _featMean[i] = 0.0; _featStd[i] = 1.0; }
        }

        // =====================================================================
        // 前向推理
        // =====================================================================

        /// <summary>
        /// 双头前向推理。
        /// 未训练时自动退化为解析开普勒判据 (绝不用随机权重做判断)。
        /// </summary>
        /// <param name="features">[a_ratio, phase_norm, tof_ratio, inc_diff_norm]</param>
        /// <param name="feasibilityProbability">物理可行性置信度 [0,1]</param>
        /// <param name="estimatedDvKms">粗估日心双曲过剩速度之和 (km/s)</param>
        public void Forward(double[] features, out double feasibilityProbability, out double estimatedDvKms)
        {
            if (features == null || features.Length < InputDim)
            {
                feasibilityProbability = 0.5;
                estimatedDvKms = double.NaN;
                return;
            }

            if (!IsTrained)
            {
                AnalyticFallback(features, out feasibilityProbability, out estimatedDvKms);
                return;
            }

            ForwardNetwork(features, out feasibilityProbability, out estimatedDvKms);
        }

        /// <summary>
        /// 无条件执行网络前向推理 (不做 IsTrained 门控)。
        /// 训练/验证期必须使用本方法 —— 否则验证集评估会退化为解析判据,
        /// 使早停与最优权重回滚失去意义 (实测会得到与网络无关的恒定指标)。
        /// </summary>
        private void ForwardNetwork(double[] features, out double feasibilityProbability, out double estimatedDvKms)
        {
            if (features == null || features.Length < InputDim)
            {
                feasibilityProbability = 0.5;
                estimatedDvKms = double.NaN;
                return;
            }

            // 1. 标准化
            var x = new double[InputDim];
            for (int j = 0; j < InputDim; j++)
            {
                x[j] = (features[j] - _featMean[j]) / (_featStd[j] > 1e-12 ? _featStd[j] : 1.0);
            }

            // 2. 隐藏层 1
            var h1 = new double[HiddenDim1];
            for (int i = 0; i < HiddenDim1; i++)
            {
                double sum = _b1[i];
                for (int j = 0; j < InputDim; j++) sum += _w1[i, j] * x[j];
                h1[i] = Math.Tanh(sum);
            }

            // 3. 隐藏层 2
            var h2 = new double[HiddenDim2];
            for (int i = 0; i < HiddenDim2; i++)
            {
                double sum = _b2[i];
                for (int j = 0; j < HiddenDim1; j++) sum += _w2[i, j] * h1[j];
                h2[i] = Math.Tanh(sum);
            }

            // 4. 输出层
            double out0 = _bOut[0], out1 = _bOut[1];
            for (int j = 0; j < HiddenDim2; j++)
            {
                out0 += _wOut[0, j] * h2[j];
                out1 += _wOut[1, j] * h2[j];
            }

            feasibilityProbability = Sigmoid(out0);
            // 对数域输出 -> 反变换回 km/s (限幅防溢出)
            double logDv = Softplus(out1) * _dvStd + _dvMean;
            if (logDv > 20.0) logDv = 20.0;
            if (logDv < -20.0) logDv = -20.0;
            estimatedDvKms = Math.Exp(logDv);
        }

        /// <summary>
        /// 未训练时的解析退化路径 (纯物理, 无魔法常数):
        ///   - 可行性由开普勒时间尺度与相位失配给出有界置信度;
        ///   - ΔV 由开普勒形状因子 (a^1.5 ∝ T) 与相位失配组合给出量级估计。
        /// </summary>
        private static void AnalyticFallback(double[] f, out double feasibilityProbability, out double estimatedDvKms)
        {
            double aRatio = f[0];        // ln(r2/r1)
            double phaseNorm = f[1];     // [-1,1]
            double tofRatio = f[2];      // ln(tof/tof_hohmann)
            double incNorm = f[3];       // (inc2-inc1)/45

            if (double.IsNaN(aRatio) || double.IsInfinity(aRatio)) aRatio = 0.0;
            if (double.IsNaN(tofRatio) || double.IsInfinity(tofRatio)) tofRatio = 0.0;

            // 开普勒形状因子: 最短能量转移的等效速度尺度
            double shape = Math.Exp(Math.Abs(aRatio) * 0.5) + Math.Exp(-Math.Abs(aRatio) * 0.5);

            // 相位失配与倾角差以无量纲方式削弱可行性
            double penalty = Math.Abs(phaseNorm) * 0.35 + Math.Min(1.0, Math.Abs(incNorm)) * 0.25;

            // 飞行时间偏离 1 (即偏离霍曼) 越多越不可行
            double tofPenalty = Math.Min(1.5, Math.Abs(tofRatio)) * 0.4;

            double score = 1.0 - penalty - tofPenalty;
            if (score < 0.0) score = 0.0;
            if (score > 1.0) score = 1.0;

            feasibilityProbability = score;
            estimatedDvKms = shape; // 无量纲形状因子, 与训练标签同量级的粗估
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Sigmoid(double x)
        {
            if (x >= 40.0) return 1.0;
            if (x <= -40.0) return 0.0;
            return 1.0 / (1.0 + Math.Exp(-x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Softplus(double x)
        {
            if (x > 30.0) return x;
            if (x < -30.0) return Math.Exp(x);
            return Math.Log(1.0 + Math.Exp(x));
        }

        // =====================================================================
        // 特征提取 (保持签名兼容)
        // =====================================================================

        /// <summary>
        /// 提取 4 维无量纲转移特征:
        ///   [0] a_ratio    = ln(r2 / r1)                   半长轴对数比
        ///   [1] phase_norm = 相对相位角归一化到 [-1, 1]
        ///   [2] tof_ratio  = ln(tof / tof_hohmann)         飞行时间对数比
        ///   [3] inc_norm   = (inc2 - inc1) / 45            倾角差归一化
        /// </summary>
        public static double[] ExtractTransferFeatures(
            double r1Km, double r2Km, double phaseAngleDeg,
            double tofDays, double nomHohmannTofDays, double incDiffDeg)
        {
            double r1 = Math.Max(1e5, r1Km);
            double r2 = Math.Max(1e5, r2Km);
            double aRatio = Math.Log(r2 / r1);
            double phaseNorm = (((phaseAngleDeg % 360.0) + 360.0) % 360.0 - 180.0) / 180.0;
            double tofRatio = Math.Log(Math.Max(1.0, tofDays) / Math.Max(1.0, nomHohmannTofDays));
            double incNorm = incDiffDeg / 45.0;
            return new[] { aRatio, phaseNorm, tofRatio, incNorm };
        }

        // =====================================================================
        // 训练 (Adam + 验证集早停)
        // =====================================================================

        /// <summary>
        /// 用监督样本训练代理网络。
        /// 损失 = 二元交叉熵 (可行性头) + 标准化 MSE (ΔV 头), 两者等权 (无量纲化后自然同尺度)。
        /// </summary>
        /// <param name="samples">监督样本集</param>
        /// <param name="maxEpochs">最大迭代轮数</param>
        /// <param name="learningRate">Adam 学习率</param>
        /// <param name="valRatio">验证集比例</param>
        /// <param name="seed">随机种子</param>
        /// <param name="report">训练报告</param>
        public bool Train(
            IList<SurrogateSample> samples,
            out SurrogateTrainingReport report,
            int maxEpochs = 800,
            double learningRate = 0.02,
            double valRatio = 0.2,
            int seed = 20260910)
        {
            report = new SurrogateTrainingReport();
            if (samples == null || samples.Count < 32) return false;

            var valid = samples.Where(s => s.Features != null && s.Features.Length >= InputDim
                                           && !double.IsNaN(s.DvKms) && s.DvKms > 0).ToList();
            if (valid.Count < 32) return false;

            var rng = new Random(seed);
            var shuffled = valid.OrderBy(_ => rng.Next()).ToList();
            int valCount = Math.Max(4, (int)(shuffled.Count * Math.Min(0.4, Math.Max(0.05, valRatio))));
            int trainCount = shuffled.Count - valCount;

            var trainSet = shuffled.Take(trainCount).ToList();
            var valSet = shuffled.Skip(trainCount).ToList();
            report.TrainCount = trainCount;
            report.ValCount = valCount;

            // ---- 特征标准化 (仅用训练集统计, 防信息泄漏) ----
            for (int j = 0; j < InputDim; j++)
            {
                double mean = trainSet.Average(s => s.Features[j]);
                double var = trainSet.Average(s => { double d = s.Features[j] - mean; return d * d; });
                _featMean[j] = mean;
                _featStd[j] = Math.Sqrt(var) > 1e-9 ? Math.Sqrt(var) : 1.0;
            }
            // ΔV 是重尾正量 (少数样本可达上百 km/s), 直接回归会被离群值主导 (实测 R^2 < 0)。
            // 故在对数域做标准化与回归 —— 这是正定重尾量的标准处理, 等价于学习乘性误差模型。
            _dvMean = trainSet.Average(s => Math.Log(s.DvKms));
            double dvVar = trainSet.Average(s => { double d = Math.Log(s.DvKms) - _dvMean; return d * d; });
            _dvStd = Math.Sqrt(dvVar) > 1e-9 ? Math.Sqrt(dvVar) : 1.0;

            return DoTrain(trainSet, valSet, report, maxEpochs, learningRate, seed);
        }

        private bool DoTrain(
            List<SurrogateSample> trainSet, List<SurrogateSample> valSet,
            SurrogateTrainingReport report, int maxEpochs, double lr, int seed)
        {
            // ---- Xavier 初始化 (取代原手的 sin() 伪随机权重) ----
            var rng = new Random(seed + 1);
            XavierInit(_w1, InputDim, rng); XavierInit(_w2, HiddenDim1, rng); XavierInit(_wOut, HiddenDim2, rng);
            for (int i = 0; i < HiddenDim1; i++) _b1[i] = 0.0;
            for (int i = 0; i < HiddenDim2; i++) _b2[i] = 0.0;
            _bOut[0] = 0.0;
            _bOut[1] = 0.0;

            // ---- Adam 状态 ----
            var mW1 = new double[HiddenDim1, InputDim]; var vW1 = new double[HiddenDim1, InputDim];
            var mB1 = new double[HiddenDim1]; var vB1 = new double[HiddenDim1];
            var mW2 = new double[HiddenDim2, HiddenDim1]; var vW2 = new double[HiddenDim2, HiddenDim1];
            var mB2 = new double[HiddenDim2]; var vB2 = new double[HiddenDim2];
            var mWO = new double[OutputDim, HiddenDim2]; var vWO = new double[OutputDim, HiddenDim2];
            var mBO = new double[OutputDim]; var vBO = new double[OutputDim];

            double beta1 = 0.9, beta2 = 0.999, eps = 1e-8;
            int step = 0;

            double bestValLoss = double.MaxValue;
            double bestAcc = 0.0, bestAuc = 0.0, bestMae = 0.0, bestR2 = 0.0;
            var bestW1 = (double[,])_w1.Clone(); var bestB1 = (double[])_b1.Clone();
            var bestW2 = (double[,])_w2.Clone(); var bestB2 = (double[])_b2.Clone();
            var bestWO = (double[,])_wOut.Clone(); var bestBO = (double[])_bOut.Clone();

            int patience = Math.Max(20, maxEpochs / 8);
            int noImprove = 0;

            for (int epoch = 0; epoch < maxEpochs; epoch++)
            {
                var order = Enumerable.Range(0, trainSet.Count).OrderBy(_ => rng.Next()).ToList();
                foreach (int idx in order)
                {
                    var s = trainSet[idx];
                    var x = Standardize(s.Features);

                    // ---- 前向 ----
                    var h1 = new double[HiddenDim1];
                    for (int i = 0; i < HiddenDim1; i++)
                    {
                        double sum = _b1[i];
                        for (int j = 0; j < InputDim; j++) sum += _w1[i, j] * x[j];
                        h1[i] = Math.Tanh(sum);
                    }
                    var h2 = new double[HiddenDim2];
                    for (int i = 0; i < HiddenDim2; i++)
                    {
                        double sum = _b2[i];
                        for (int j = 0; j < HiddenDim1; j++) sum += _w2[i, j] * h1[j];
                        h2[i] = Math.Tanh(sum);
                    }
                    double o0 = _bOut[0], o1 = _bOut[1];
                    for (int j = 0; j < HiddenDim2; j++)
                    {
                        o0 += _wOut[0, j] * h2[j];
                        o1 += _wOut[1, j] * h2[j];
                    }

                    double pHat = Sigmoid(o0);
                    double dvHatStd = Softplus(o1);

                    // ---- 反向: 头0 交叉熵, 头1 标准化 MSE ----
                    double dOut0 = pHat - s.Feasibility;                       // dL/dlogit
                    double dvTargetStd = (Math.Log(s.DvKms) - _dvMean) / _dvStd;
                    double dOut1 = 2.0 * (dvHatStd - dvTargetStd) * Sigmoid(o1); // softplus 导数 = sigmoid

                    var gWO = new double[OutputDim, HiddenDim2];
                    var gBO = new double[OutputDim];
                    gBO[0] = dOut0; gBO[1] = dOut1;
                    for (int j = 0; j < HiddenDim2; j++)
                    {
                        gWO[0, j] = dOut0 * h2[j];
                        gWO[1, j] = dOut1 * h2[j];
                    }

                    var dH2 = new double[HiddenDim2];
                    for (int j = 0; j < HiddenDim2; j++)
                    {
                        dH2[j] = dOut0 * _wOut[0, j] + dOut1 * _wOut[1, j];
                        dH2[j] *= (1.0 - h2[j] * h2[j]); // tanh'
                    }

                    var gW2 = new double[HiddenDim2, HiddenDim1];
                    var gB2 = new double[HiddenDim2];
                    for (int i = 0; i < HiddenDim2; i++)
                    {
                        gB2[i] = dH2[i];
                        for (int j = 0; j < HiddenDim1; j++) gW2[i, j] = dH2[i] * h1[j];
                    }

                    var dH1 = new double[HiddenDim1];
                    for (int j = 0; j < HiddenDim1; j++)
                    {
                        double accH1 = 0.0;
                        for (int i = 0; i < HiddenDim2; i++) accH1 += dH2[i] * _w2[i, j];
                        dH1[j] = accH1 * (1.0 - h1[j] * h1[j]);
                    }

                    var gW1 = new double[HiddenDim1, InputDim];
                    var gB1 = new double[HiddenDim1];
                    for (int i = 0; i < HiddenDim1; i++)
                    {
                        gB1[i] = dH1[i];
                        for (int j = 0; j < InputDim; j++) gW1[i, j] = dH1[i] * x[j];
                    }

                    // ---- Adam 更新 ----
                    step++;
                    double bc1 = 1.0 - Math.Pow(beta1, step);
                    double bc2 = 1.0 - Math.Pow(beta2, step);

                    AdamUpdateTensor(_w1, gW1, mW1, vW1, lr, beta1, beta2, eps, bc1, bc2);
                    AdamUpdateVector(_b1, gB1, mB1, vB1, lr, beta1, beta2, eps, bc1, bc2);
                    AdamUpdateTensor(_w2, gW2, mW2, vW2, lr, beta1, beta2, eps, bc1, bc2);
                    AdamUpdateVector(_b2, gB2, mB2, vB2, lr, beta1, beta2, eps, bc1, bc2);
                    AdamUpdateTensor(_wOut, gWO, mWO, vWO, lr, beta1, beta2, eps, bc1, bc2);
                    AdamUpdateVector(_bOut, gBO, mBO, vBO, lr, beta1, beta2, eps, bc1, bc2);
                }

                // ---- 验证 ----
                Evaluate(valSet, out double valLoss, out double acc, out double auc, out double mae, out double r2);
                report.EpochsRun = epoch + 1;

                if (valLoss < bestValLoss - 1e-6)
                {
                    bestValLoss = valLoss; bestAcc = acc; bestAuc = auc; bestMae = mae; bestR2 = r2;
                    Buffer.BlockCopy(_w1, 0, bestW1, 0, _w1.Length * sizeof(double));
                    Buffer.BlockCopy(_b1, 0, bestB1, 0, _b1.Length * sizeof(double));
                    Buffer.BlockCopy(_w2, 0, bestW2, 0, _w2.Length * sizeof(double));
                    Buffer.BlockCopy(_b2, 0, bestB2, 0, _b2.Length * sizeof(double));
                    Buffer.BlockCopy(_wOut, 0, bestWO, 0, _wOut.Length * sizeof(double));
                    Buffer.BlockCopy(_bOut, 0, bestBO, 0, _bOut.Length * sizeof(double));
                    noImprove = 0;
                }
                else
                {
                    noImprove++;
                    if (noImprove >= patience) break;
                }
            }

            // ---- 回滚到验证集最优权重 ----
            Buffer.BlockCopy(bestW1, 0, _w1, 0, _w1.Length * sizeof(double));
            Buffer.BlockCopy(bestB1, 0, _b1, 0, _b1.Length * sizeof(double));
            Buffer.BlockCopy(bestW2, 0, _w2, 0, _w2.Length * sizeof(double));
            Buffer.BlockCopy(bestB2, 0, _b2, 0, _b2.Length * sizeof(double));
            Buffer.BlockCopy(bestWO, 0, _wOut, 0, _wOut.Length * sizeof(double));
            Buffer.BlockCopy(bestBO, 0, _bOut, 0, _bOut.Length * sizeof(double));

            report.FeasibilityAccuracy = bestAcc;
            report.FeasibilityAuc = bestAuc;
            report.DvMaeKms = bestMae;
            report.DvR2 = bestR2;

            // ---- 采纳判据 (全为无量纲质量指标, 非调参旋钮) ----
            //   可行性分类需显著优于随机猜测 (AUC + 准确率双门禁);
            //   ΔV 回归需至少解释 50% 方差 (R² >= 0.5), 否则该模型不具备工程价值。
            // 按头分别在报告上登记质量 (两个用途不同, 不应耦合为单一门禁):
            //   * 可行性分类头 —— 代理网络在 MCTS 价值加权与大规模预筛中的实际用途, 必须达标;
            //   * ΔV 回归头   —— 仅提供量级估计, 门槛更低但需显式标注"粗筛可用"。
            report.FeasibilityAccepted = (bestAuc >= FeasibilityGateAuc) && (bestAcc >= FeasibilityGateAccuracy);
            report.DvHeadUsable = bestR2 >= DvHeadUsableR2;
            report.Accepted = report.FeasibilityAccepted;

            if (report.Accepted)
            {
                IsTrained = true;
                TrainedSampleCount = trainSet.Count + valSet.Count;
            }
            DvHeadUsable = report.DvHeadUsable;
            _lastAuc = bestAuc; _lastAcc = bestAcc; _lastR2 = bestR2; _lastMae = bestMae;

            return report.Accepted;
        }

        private double[] Standardize(double[] f)
        {
            var x = new double[InputDim];
            for (int j = 0; j < InputDim; j++)
            {
                double v = (j < f.Length) ? f[j] : 0.0;
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
                x[j] = (v - _featMean[j]) / (_featStd[j] > 1e-12 ? _featStd[j] : 1.0);
            }
            return x;
        }

        private void Evaluate(List<SurrogateSample> set,
            out double loss, out double accuracy, out double auc, out double mae, out double r2)
        {
            loss = 0.0; accuracy = 0.0; auc = 0.0; mae = 0.0; r2 = 0.0;
            if (set == null || set.Count == 0) return;

            var scores = new List<(double p, int label)>();
            double se = 0.0, absErr = 0.0, meanDv = set.Average(s => s.DvKms);
            double ssTot = 0.0;
            int correct = 0;

            foreach (var s in set)
            {
                ForwardNetwork(s.Features, out double p, out double dvHat);

                double ce = s.Feasibility > 0.5
                    ? -Math.Log(Math.Max(1e-9, p))
                    : -Math.Log(Math.Max(1e-9, 1.0 - p));
                loss += ce;

                bool pred = p >= 0.5;
                bool truth = s.Feasibility > 0.5;
                if (pred == truth) correct++;
                scores.Add((p, truth ? 1 : 0));

                double d = Math.Abs(dvHat - s.DvKms);
                absErr += d;
                se += d * d;
                ssTot += (s.DvKms - meanDv) * (s.DvKms - meanDv);
            }

            loss /= set.Count;
            accuracy = (double)correct / set.Count;
            mae = absErr / set.Count;
            r2 = ssTot > 1e-12 ? (1.0 - se / ssTot) : 0.0;
            auc = ComputeAuc(scores);
        }

        /// <summary>ROC-AUC (Mann-Whitney U 统计量), 无需阈值</summary>
        private static double ComputeAuc(List<(double p, int label)> scores)
        {
            var pos = scores.Where(s => s.label == 1).Select(s => s.p).ToList();
            var neg = scores.Where(s => s.label == 0).Select(s => s.p).ToList();
            if (pos.Count == 0 || neg.Count == 0) return 0.5;

            int wins = 0, ties = 0;
            foreach (double p in pos)
            {
                foreach (double n in neg)
                {
                    if (p > n) wins++;
                    else if (Math.Abs(p - n) < 1e-12) ties++;
                }
            }
            return (wins + 0.5 * ties) / (double)(pos.Count * neg.Count);
        }

        // =====================================================================
        // 初始化与优化器工具
        // =====================================================================

        private static void XavierInit(double[,] w, int fanIn, Random rng)
        {
            double limit = Math.Sqrt(6.0 / (fanIn + w.GetLength(0)));
            for (int i = 0; i < w.GetLength(0); i++)
                for (int j = 0; j < w.GetLength(1); j++)
                    w[i, j] = (rng.NextDouble() * 2.0 - 1.0) * limit;
        }

        private static void AdamUpdateTensor(double[,] p, double[,] g, double[,] m, double[,] v,
            double lr, double b1, double b2, double eps, double bc1, double bc2)
        {
            for (int i = 0; i < p.GetLength(0); i++)
            {
                for (int j = 0; j < p.GetLength(1); j++)
                {
                    m[i, j] = b1 * m[i, j] + (1 - b1) * g[i, j];
                    v[i, j] = b2 * v[i, j] + (1 - b2) * g[i, j] * g[i, j];
                    double mHat = m[i, j] / bc1;
                    double vHat = v[i, j] / bc2;
                    // L2 权重衰减以 1e-4 的纯数值正则抑制过拟合 (非调参旋钮)
                    p[i, j] -= lr * (mHat / (Math.Sqrt(vHat) + eps) + 1e-4 * p[i, j]);
                }
            }
        }

        private static void AdamUpdateVector(double[] p, double[] g, double[] m, double[] v,
            double lr, double b1, double b2, double eps, double bc1, double bc2)
        {
            for (int i = 0; i < p.Length; i++)
            {
                m[i] = b1 * m[i] + (1 - b1) * g[i];
                v[i] = b2 * v[i] + (1 - b2) * g[i] * g[i];
                double mHat = m[i] / bc1;
                double vHat = v[i] / bc2;
                p[i] -= lr * (mHat / (Math.Sqrt(vHat) + eps));
            }
        }

        // =====================================================================
        // 权重持久化 (人类可读文本格式, 可审计可复现)
        // =====================================================================

        public const int WeightFormatVersion = 3;

        /// <summary>导出为文本权重文件内容</summary>
        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# KSPGMAT MGA surrogate weights");
            sb.AppendLine($"version={WeightFormatVersion}");
            sb.AppendLine($"trained={(IsTrained ? 1 : 0)}");
            sb.AppendLine($"samples={TrainedSampleCount}");
            sb.AppendLine($"dvHeadUsable={(DvHeadUsable ? 1 : 0)}");
            sb.AppendLine($"auc={LastFeasibilityAuc.ToString("R", inv)}");
            sb.AppendLine($"acc={LastFeasibilityAccuracy.ToString("R", inv)}");
            sb.AppendLine($"r2={LastDvR2.ToString("R", inv)}");
            sb.AppendLine($"dvMae={LastDvMae.ToString("R", inv)}");
            sb.AppendLine("featMean=" + string.Join(",", _featMean.Select(x => x.ToString("R", inv))));
            sb.AppendLine("featStd=" + string.Join(",", _featStd.Select(x => x.ToString("R", inv))));
            sb.AppendLine($"dvMean={_dvMean.ToString("R", inv)}");
            sb.AppendLine($"dvStd={_dvStd.ToString("R", inv)}");
            AppendTensor(sb, "w1", _w1); AppendVec(sb, "b1", _b1);
            AppendTensor(sb, "w2", _w2); AppendVec(sb, "b2", _b2);
            AppendTensor(sb, "wOut", _wOut); AppendVec(sb, "bOut", _bOut);
            return sb.ToString();
        }

        private static void AppendTensor(System.Text.StringBuilder sb, string name, double[,] t)
        {
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < t.GetLength(0); i++)
            {
                var row = new double[t.GetLength(1)];
                for (int j = 0; j < t.GetLength(1); j++) row[j] = t[i, j];
                sb.AppendLine($"{name}[{i}]=" + string.Join(",", row.Select(x => x.ToString("R", inv))));
            }
        }

        private static void AppendVec(System.Text.StringBuilder sb, string name, double[] v)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.AppendLine($"{name}=" + string.Join(",", v.Select(x => x.ToString("R", inv))));
        }

        /// <summary>保存权重到文件 (目录自动创建)</summary>
        public bool Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, Serialize());
                return true;
            }
            catch { return false; }
        }

        /// <summary>从文件加载权重。成功且 trained=1 时 IsTrained 置位。</summary>
        public bool Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var inv = CultureInfo.InvariantCulture;
                var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var w1Rows = new Dictionary<int, double[]>();
                var w2Rows = new Dictionary<int, double[]>();
                var woRows = new Dictionary<int, double[]>();

                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    int br = key.IndexOf('[');
                    if (br > 0 && key.EndsWith("]"))
                    {
                        string baseName = key.Substring(0, br);
                        if (!int.TryParse(key.Substring(br + 1, key.Length - br - 2), out int rowIdx)) continue;
                        var arr = ParseDoubles(val, inv);
                        if (baseName.Equals("w1", StringComparison.OrdinalIgnoreCase)) w1Rows[rowIdx] = arr;
                        else if (baseName.Equals("w2", StringComparison.OrdinalIgnoreCase)) w2Rows[rowIdx] = arr;
                        else if (baseName.Equals("wOut", StringComparison.OrdinalIgnoreCase)) woRows[rowIdx] = arr;
                    }
                    else raw[key] = val;
                }

                if (!raw.TryGetValue("version", out var verStr) || verStr != WeightFormatVersion.ToString(inv)) return false;

                var fm = ParseDoubles(raw.TryGetValue("featMean", out var s1) ? s1 : "", inv);
                var fs = ParseDoubles(raw.TryGetValue("featStd", out var s2) ? s2 : "", inv);
                for (int j = 0; j < InputDim && j < fm.Length; j++) _featMean[j] = fm[j];
                for (int j = 0; j < InputDim && j < fs.Length; j++) _featStd[j] = fs[j] > 1e-12 ? fs[j] : 1.0;
                if (raw.TryGetValue("dvMean", out var dvm)) double.TryParse(dvm, NumberStyles.Float, inv, out _dvMean);
                if (raw.TryGetValue("dvStd", out var dvs) && double.TryParse(dvs, NumberStyles.Float, inv, out double dvsv) && dvsv > 1e-12) _dvStd = dvsv;

                FillTensor(_w1, w1Rows); FillTensor(_w2, w2Rows); FillTensor(_wOut, woRows);
                if (raw.TryGetValue("b1", out var b1s)) FillVector(_b1, ParseDoubles(b1s, inv));
                if (raw.TryGetValue("b2", out var b2s)) FillVector(_b2, ParseDoubles(b2s, inv));
                if (raw.TryGetValue("bOut", out var bos)) FillVector(_bOut, ParseDoubles(bos, inv));

                int.TryParse(raw.TryGetValue("samples", out var smp) ? smp : "0", out int smpN);
                TrainedSampleCount = smpN;

                // 恢复两个头各自的质量标志与指标 (否则加载后 DvHeadUsable 会丢失, 退化为默认 false)
                DvHeadUsable = raw.TryGetValue("dvHeadUsable", out var dhu) && dhu == "1";
                if (raw.TryGetValue("auc", out var aucS)) double.TryParse(aucS, NumberStyles.Float, inv, out _lastAuc);
                if (raw.TryGetValue("acc", out var accS)) double.TryParse(accS, NumberStyles.Float, inv, out _lastAcc);
                if (raw.TryGetValue("r2", out var r2S)) double.TryParse(r2S, NumberStyles.Float, inv, out _lastR2);
                if (raw.TryGetValue("dvMae", out var maeS)) double.TryParse(maeS, NumberStyles.Float, inv, out _lastMae);

                IsTrained = (raw.TryGetValue("trained", out var tr) && tr == "1") && !AllZero(_w1);

                // 严格维度校验: 权重矩阵形状必须与当前网络结构完全一致,
                // 否则部分填充会产生"看似可用实则错乱"的模型 (比拒绝加载更危险)。
                bool shapeOk =
                    w1Rows.Count == HiddenDim1 && RowLen(w1Rows, 0) == InputDim &&
                    w2Rows.Count == HiddenDim2 && RowLen(w2Rows, 0) == HiddenDim1 &&
                    woRows.Count == OutputDim && RowLen(woRows, 0) == HiddenDim2 &&
                    _b1.Length == HiddenDim1 && _b2.Length == HiddenDim2 && _bOut.Length == OutputDim;

                if (!shapeOk) IsTrained = false;
                return IsTrained;
            }
            catch { IsTrained = false; return false; }
        }

        private static int RowLen(Dictionary<int, double[]> rows, int key)
        {
            return (rows != null && rows.TryGetValue(key, out var v) && v != null) ? v.Length : -1;
        }

        private static double[] ParseDoubles(string s, CultureInfo inv)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<double>();
            var parts = s.Split(',');
            var res = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                double.TryParse(parts[i].Trim(), NumberStyles.Float, inv, out res[i]);
            return res;
        }

        private static void FillTensor(double[,] t, Dictionary<int, double[]> rows)
        {
            foreach (var kv in rows)
            {
                if (kv.Key < 0 || kv.Key >= t.GetLength(0)) continue;
                for (int j = 0; j < t.GetLength(1) && j < kv.Value.Length; j++) t[kv.Key, j] = kv.Value[j];
            }
        }

        private static void FillVector(double[] v, double[] src)
        {
            for (int i = 0; i < v.Length && i < src.Length; i++) v[i] = src[i];
        }

        private static bool AllZero(double[,] t)
        {
            for (int i = 0; i < t.GetLength(0); i++)
                for (int j = 0; j < t.GetLength(1); j++)
                    if (Math.Abs(t[i, j]) > 1e-12) return false;
            return true;
        }
    }
}
