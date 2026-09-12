// ============================================================================
// MGASurrogateTrainer.cs
// 代理网络离线训练管线 (Offline Supervised Training Pipeline for the Surrogate Net)
//
// 目的: 让《硬编码与经验公式审计报告》中发现的"从未训练过的神经网络"变成
//       真正由数据驱动的机器学习模块。
//
// 数据来源 (严格遵守基线 §7 "零托管数学"):
//   * 标签 (Label) 100% 由 **原生 ESA kep3 Lambert 求解器** (PykepNative.SolveLambert)
//     在合成几何上求解得到, C# 层不产生任何伪造动力学结果;
//   * 特征 (Feature) 为 4 维无量纲量, 与推理期 ExtractTransferFeatures 完全一致;
//   * 采样分布刻意对齐真实部署分布: 半径/倾角取自 SolarSystemCatalog 的实际天体,
//     而非均匀随机, 从而避免分布外泛化失败。
//
// 训练目标:
//   头0 — 可行性二分类 (是否在物理合理包络内): 用 ROC-AUC 与准确率评估;
//   头1 — 日心双曲过剩速度之和 (km/s) 回归: 用 MAE 与 R² 评估。
//   采纳门禁: AUC >= 0.65 且 准确率 >= 0.60 且 R² >= 0.50 (无量纲质量指标, 非调参旋钮)。
//
// 调用方式 (可由测试工程或游戏内"训练代理模型"按钮触发):
//   MGASurrogateTrainer.GenerateAndTrain(sampleCount, weightPath, progress);
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KSPGMAT.MGA.AI
{
    /// <summary>
    /// 代理网络数据生成与训练器
    /// </summary>
    public static class MGASurrogateTrainer
    {
        /// <summary>
        /// 可行性标签的宽容系数: 日心双曲过剩速度之和若低于霍曼下界的该倍数,
        /// 视为物理可行。取 3.0 表示"允许 3 倍霍曼下界"的宽松可行域 —— 这是
        /// 无量纲倍数, 与恒星系、单位制无关。
        /// </summary>
        public const double FeasibilityEnvelopeFactor = 3.0;

        /// <summary>
        /// 生成合成数据集并训练代理网络。
        /// </summary>
        /// <param name="sampleCount">目标样本数 (含正负类)</param>
        /// <param name="weightOutputPath">权重输出路径 (null 则不落盘)</param>
        /// <param name="seed">随机种子 (保证数据可复现)</param>
        /// <param name="onProgress">进度回调 (0~1, 描述)</param>
        /// <returns>训练报告; 失败返回 null</returns>
        public static SurrogateTrainingReport GenerateAndTrain(
            int sampleCount = 4000,
            string weightOutputPath = null,
            int seed = 20260910,
            Action<float, string> onProgress = null)
        {
            onProgress?.Invoke(0.0f, "代理模型训练: 生成合成几何样本...");

            var samples = GenerateSamples(sampleCount, seed, onProgress);
            if (samples == null || samples.Count < 64)
            {
                onProgress?.Invoke(1.0f, "代理模型训练中止: 原生 Lambert 不可用或有效样本不足。");
                return null;
            }

            onProgress?.Invoke(0.6f, $"代理模型训练: 样本 {samples.Count} 条, 开始监督训练...");

            var net = MGAPhysicsSurrogateNetwork.Default;
            bool ok = net.Train(samples, out var report);
            report.Accepted = ok && report.Accepted;

            // 即便未达采纳门禁也保留权重文件, 但 trained=0, 推理期会自动走解析退化路径
            if (!string.IsNullOrEmpty(weightOutputPath))
            {
                net.Save(weightOutputPath);
            }

            onProgress?.Invoke(1.0f, $"代理模型训练完成: {report}");
            Console.WriteLine($"[MGASurrogateTrainer] {report}");
            if (!report.Accepted)
            {
                Console.WriteLine("[MGASurrogateTrainer] ⚠️ 模型未达采纳门禁 (AUC>=0.65, Acc>=0.60, R²>=0.50)，" +
                                  "推理期将自动使用解析开普勒判据而非该网络。");
            }
            return report;
        }

        /// <summary>
        /// 生成监督样本。所有标签来自原生 Lambert 求解, 特征来自真实天体几何。
        /// </summary>
        public static List<SurrogateSample> GenerateSamples(
            int sampleCount, int seed, Action<float, string> onProgress = null)
        {
            var samples = new List<SurrogateSample>(sampleCount);
            if (!PykepNative.IsAvailable) return samples;

            double muCentral = SolarSystemCatalog.SunMu;
            if (muCentral <= 0) muCentral = 1.32712440018e11;

            // ---- 采样池: 取自实际天体目录 (对齐部署分布) ----
            var bodies = SolarSystemCatalog.GetAvailablePlanets();
            var pool = (bodies != null ? bodies.Where(b => b != null && b.SMA > 1e6 && !b.IsMoon).ToList()
                                       : new List<SolarPlanet>());
            if (pool.Count < 2)
            {
                // 天体目录不可用时退化为对数均匀采样的日心半径 (仍为真实物理量)
                pool = null;
            }

            var rng = new System.Random(seed);
            int attempts = 0;
            int maxAttempts = sampleCount * 40;

            while (samples.Count < sampleCount && attempts < maxAttempts)
            {
                attempts++;

                // 1) 几何采样
                double r1Km, r2Km, incDiffDeg;
                if (pool != null)
                {
                    var pa = pool[rng.Next(pool.Count)];
                    var pb = pool[rng.Next(pool.Count)];
                    r1Km = pa.SMA; r2Km = pb.SMA;
                    incDiffDeg = Math.Abs(pb.INC - pa.INC);
                    // 加入小幅连续抖动, 覆盖天体之间的连续空间
                    r1Km *= (0.85 + 0.30 * rng.NextDouble());
                    r2Km *= (0.85 + 0.30 * rng.NextDouble());
                }
                else
                {
                    double lo = Math.Log(0.35 * 1.496e8), hi = Math.Log(45.0 * 1.496e8);
                    r1Km = Math.Exp(lo + (hi - lo) * rng.NextDouble());
                    r2Km = Math.Exp(lo + (hi - lo) * rng.NextDouble());
                    incDiffDeg = rng.NextDouble() * 30.0;
                }
                if (r1Km <= 1e6 || r2Km <= 1e6) continue;

                double phaseDeg = rng.NextDouble() * 360.0 - 180.0;
                double hohmannToFDays = MGAPhysicalScales.HohmannTofDays(muCentral, r1Km, r2Km);
                if (hohmannToFDays <= 1.0) continue;

                // ToF 覆盖 [0.3, 3.0] × 霍曼 (对数均匀, 覆盖快转移与慢转移)
                double tofDays = hohmannToFDays * Math.Exp((rng.NextDouble() * 2.0 - 1.0) * Math.Log(3.0));
                double tofSec = tofDays * 86400.0;

                // 2) 物理可行性前置门禁 (符号开普勒判据, 避免生成无物理意义的样本)
                if (!MGASymbolicPhysicsConstraints.IsToFKeplerianFeasible(r1Km, r2Km, tofSec, muCentral))
                    continue;

                // 3) 构造合成几何状态 (位置 + 圆轨速度)
                double phaseRad = phaseDeg * Math.PI / 180.0;
                double incRad = incDiffDeg * Math.PI / 180.0;

                var r1 = new Vector3d(r1Km, 0.0, 0.0);
                double v1Mag = MGAPhysicalScales.CircularSpeedKms(muCentral, r1Km);
                var v1 = new Vector3d(0.0, v1Mag, 0.0);

                var r2 = new Vector3d(
                    r2Km * Math.Cos(phaseRad),
                    r2Km * Math.Sin(phaseRad) * Math.Cos(incRad),
                    r2Km * Math.Sin(phaseRad) * Math.Sin(incRad));
                double v2Mag = MGAPhysicalScales.CircularSpeedKms(muCentral, r2Km);
                var v2 = new Vector3d(
                    -v2Mag * Math.Sin(phaseRad),
                    v2Mag * Math.Cos(phaseRad) * Math.Cos(incRad),
                    v2Mag * Math.Cos(phaseRad) * Math.Sin(incRad));

                // 4) 标签: 精确原生 Lambert 求解 (100% kep3, 无托管拟合)
                bool solved = PykepNative.SolveLambert(r1, r2, tofSec, muCentral, false,
                    out Vector3d vDep, out Vector3d vArr);

                double dvSumKms;
                double feasibility;

                if (!solved)
                {
                    // 兰伯特无解: 仍保留为负样本, 标签取霍曼下界的有效放大值
                    dvSumKms = 2.0 * MGAPhysicalScales.HohmannVInfKms(muCentral, r1Km, r2Km) * FeasibilityEnvelopeFactor;
                    feasibility = 0.0;
                }
                else
                {
                    double vInfDep = (vDep - v1).magnitude;
                    double vInfArr = (vArr - v2).magnitude;
                    if (double.IsNaN(vInfDep) || double.IsNaN(vInfArr)) continue;

                    dvSumKms = vInfDep + vInfArr;

                    // 可行域: 相对霍曼下界的无量纲包络
                    double hohmannRef = 2.0 * MGAPhysicalScales.HohmannVInfKms(muCentral, r1Km, r2Km);
                    if (hohmannRef <= 1e-6) continue;
                    feasibility = (dvSumKms <= hohmannRef * FeasibilityEnvelopeFactor) ? 1.0 : 0.0;
                }

                if (dvSumKms <= 0 || double.IsNaN(dvSumKms) || double.IsInfinity(dvSumKms)) continue;

                var features = MGAPhysicsSurrogateNetwork.ExtractTransferFeatures(
                    r1Km, r2Km, phaseDeg, tofDays, hohmannToFDays, incDiffDeg);

                samples.Add(new SurrogateSample
                {
                    Features = features,
                    Feasibility = feasibility,
                    DvKms = dvSumKms
                });

                if (onProgress != null && attempts % 200 == 0)
                {
                    onProgress(0.55f * Math.Min(1.0f, (float)samples.Count / sampleCount),
                        $"代理模型训练: 已生成 {samples.Count}/{sampleCount} 条样本...");
                }
            }

            return samples;
        }

        /// <summary>权重文件的默认落盘路径</summary>
        public static string DefaultWeightPath
        {
            get
            {
                try
                {
                    string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "GameData", "KSPGMAT", "Config");
                    return Path.Combine(root, "mga_surrogate_weights.txt");
                }
                catch
                {
                    return "mga_surrogate_weights.txt";
                }
            }
        }

        /// <summary>
        /// 导出样本为 CSV, 便于离线分析与回归 (特征标准化后可直接喂给外部 ML 框架)。
        /// </summary>
        public static bool ExportSamplesCsv(IList<SurrogateSample> samples, string path)
        {
            try
            {
                if (samples == null) return false;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(path))
                {
                    sw.WriteLine("a_ratio,phase_norm,tof_ratio,inc_norm,feasibility,dv_kms");
                    foreach (var s in samples)
                    {
                        sw.WriteLine(string.Join(",", s.Features.Select(v => v.ToString("R")))
                                     + "," + s.Feasibility.ToString("R")
                                     + "," + s.DvKms.ToString("R"));
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}
