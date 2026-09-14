using System;

namespace MGADebugRunner.TestSuites
{
    public class SurrogateTrainingTest : ITestModule
    {
        public string ModuleId => "surrogate_training";
        public string Name => "机器学习代理模型训练管线测试";
        public string Description => "验证用原生 kep3 Lambert 求解器生成监督数据并训练双头网络";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("SurrogateTraining_Pipeline");
            bool ok = RunSurrogateTraining();
            ctx.RecordMetric("训练判定", ok ? "PASS" : "FAIL");
            if (!ok) ctx.Fail("代理模型训练未达门禁或未成功");
            ctx.EndCase(ok);
            return ok;
        }

        public static bool RunSurrogateTraining()
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("  机器学习代理模型训练管线 (MGASurrogateTrainer)");
            Console.WriteLine("  数据标签 100% 由原生 ESA kep3 Lambert 求解器生成 (零托管拟合)");
            Console.WriteLine("==========================================================================================");

            string weightPath = KSPGMAT.MGA.AI.MGASurrogateTrainer.DefaultWeightPath;
            string csvPath = System.IO.Path.ChangeExtension(weightPath, ".csv");

            var samples = KSPGMAT.MGA.AI.MGASurrogateTrainer.GenerateSamples(4000, 20260910,
                (prog, msg) => { if (!string.IsNullOrEmpty(msg)) Console.WriteLine("  " + msg); });

            if (samples == null || samples.Count < 64)
            {
                Console.WriteLine("样本不足 (需原生 kep3 可用)。");
                return false;
            }

            int pos = 0;
            foreach (var smp in samples) if (smp.Feasibility > 0.5) pos++;
            Console.WriteLine($"  样本 {samples.Count} 条 | 正类(可行) {pos} ({100.0 * pos / samples.Count:F1}%) | 负类 {samples.Count - pos}");

            if (KSPGMAT.MGA.AI.MGASurrogateTrainer.ExportSamplesCsv(samples, csvPath))
            {
                Console.WriteLine($"  样本已导出 CSV: {csvPath}");
            }

            var net = KSPGMAT.MGA.AI.MGAPhysicsSurrogateNetwork.Default;
            bool accepted = net.Train(samples, out var report);

            Console.WriteLine();
            Console.WriteLine("------------------------------ 训练结果 ------------------------------");
            Console.WriteLine($"  训练集 {report.TrainCount} / 验证集 {report.ValCount} | 迭代 {report.EpochsRun} 轮");
            Console.WriteLine($"  可行分类: 准确率 {report.FeasibilityAccuracy:P1} | ROC-AUC {report.FeasibilityAuc:F3}");
            Console.WriteLine($"  dV 回归 : MAE {report.DvMaeKms:F3} km/s | R^2 {report.DvR2:F4}");
            Console.WriteLine($"  可行性头门禁: AUC>=0.65 & Acc>=0.60  =>  {(report.FeasibilityAccepted ? "PASS" : "FAIL")}");
            Console.WriteLine($"  dV 回归头   : R^2>=0.15 (粗筛可用)   =>  {(report.DvHeadUsable ? "PASS" : "FAIL")}");

            if (net.Save(weightPath))
            {
                Console.WriteLine($"  权重已保存: {weightPath}");
                Console.WriteLine($"  IsTrained = {net.IsTrained} (false 时代理推理自动退回解析开普勒判据)");
            }
            Console.WriteLine("==========================================================================================");
            return accepted;
        }
    }
}
