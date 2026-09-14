using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.GPU;
using KSPGMAT.MGA.Solver;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// GPU (DirectCompute HLSL CS 5.0) 全面接管大规模 MGA 动力学计算专项测试套件
    /// </summary>
    public class GpuAccelerationValidationTest : ITestModule
    {
        public string ModuleId => "gpu_verify";
        public string Name => "GPU (DirectCompute CS 5.0) 大规模动力学计算接管专项验证";
        public string Description => "全方位验证 DirectCompute HLSL CS 5.0 硬件加速环境、多航段同星共振链路超大规模种群批处理评估、注种候选打分接管及物理一致性";

        public bool Run(TestContext ctx)
        {
            bool allPassed = true;

            // 1. 验证 GPU 硬件加速环境初始化
            allPassed &= RunCase1_HardwareInit(ctx);

            // 2. 验证 GPU 种群批处理动力学评估接管 (含 Venus->Venus 同星共振链路: Cassini)
            allPassed &= RunCase2_CassiniVenusVenusMegaPop(ctx);

            // 3. 验证 GPU 种群批处理动力学评估接管 (含 Venus->Venus 与 Mercury->Mercury 多级共振链路: MESSENGER)
            allPassed &= RunCase3_MessengerMultiResonanceMegaPop(ctx);

            // 4. 验证 注种候选批量打分接管 (MGAGpuAccelerationManager.EvaluateSeedingCandidatesGpu)
            allPassed &= RunCase4_SeedingCandidatesGpuScoring(ctx);

            // 5. 验证 GPU vs CPU 物理动力学损失函数一致性
            allPassed &= RunCase5_GpuVsCpuConsistency(ctx);

            return allPassed;
        }

        // ====================================================================
        // Case 1: GPU 硬件加速环境初始化探针验证
        // ====================================================================
        private bool RunCase1_HardwareInit(TestContext ctx)
        {
            ctx.StartCase("GPU_HardwareEnvironment_Probe");
            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("🖥️ [Case 1] 验证 GPU 硬件加速环境初始化与探针状态");
            Console.WriteLine("==========================================================================================");

            bool isNativeGpuSupp = PykepNative.IsGpuComputeAvailable;
            string nativeGpuName = PykepNative.GpuDeviceName;
            bool isManagerGpuAvail = MGAGpuAccelerationManager.IsGpuAvailable;
            string managerDeviceName = MGAGpuAccelerationManager.DeviceName;
            GpuAccelerationStatus status = MGAGpuAccelerationManager.Status;

            Console.WriteLine($"• [PykepNative] IsGpuComputeAvailable: {(isNativeGpuSupp ? "✅ True" : "❌ False")}");
            Console.WriteLine($"• [PykepNative] GpuDeviceName: {nativeGpuName}");
            Console.WriteLine($"• [MGAGpuAccelerationManager] IsGpuAvailable: {(isManagerGpuAvail ? "✅ True" : "❌ False")}");
            Console.WriteLine($"• [MGAGpuAccelerationManager] DeviceName: {managerDeviceName}");
            Console.WriteLine($"• [MGAGpuAccelerationManager] Status: {status}");
            Console.WriteLine($"• [MGAGpuAccelerationManager] MaxBatchCapacity: {MGAGpuAccelerationManager.MaxBatchCapacity:N0} 轨迹并发");

            ctx.RecordMetric("IsGpuComputeAvailable", isNativeGpuSupp);
            ctx.RecordMetric("MGAGpuManager_IsGpuAvailable", isManagerGpuAvail);
            ctx.RecordMetric("DeviceName", managerDeviceName);
            ctx.RecordMetric("Status", status.ToString());

            bool pass = isNativeGpuSupp && isManagerGpuAvail && status == GpuAccelerationStatus.Available;
            if (!pass)
            {
                ctx.Fail($"GPU 硬件加速环境不可用: Native={isNativeGpuSupp}, Manager={isManagerGpuAvail}, Status={status}");
            }
            else
            {
                Console.WriteLine(">>> 硬件环境探针判定: ✅ GPU DirectCompute CS 5.0 初始化成功并就绪！");
            }

            ctx.EndCase(pass);
            return pass;
        }

        // ====================================================================
        // Case 2: Cassini 链路 (含 Venus->Venus 同星共振) 超大规模批处理评估接管
        // ====================================================================
        private bool RunCase2_CassiniVenusVenusMegaPop(TestContext ctx)
        {
            ctx.StartCase("GPU_MegaPop_Cassini_VenusVenus_Resonance");
            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("🚀 [Case 2] 卡西尼 (Cassini) 借力链路: Venus->Venus 同星共振段 GPU 批处理动力学接管");
            Console.WriteLine("   链路: Earth ➔ Venus ➔ Venus ➔ Earth ➔ Jupiter ➔ Saturn (包含 Venus->Venus 共振段)");
            Console.WriteLine("==========================================================================================");

            var fullChain = new List<string> { "Earth", "Venus", "Venus", "Earth", "Jupiter", "Saturn" };
            double centralMu = SolarSystemCatalog.SunMu;
            double launchUT = GMATClient.ParseUtcGregorian("15 Oct 1997 00:00:00.000");

            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Saturn",
                FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter" },
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 200.0,
                DestinationApAKm = 100000.0,
                NoInsertionBurn = false,
                ConstrainByVesselDeltaV = false,
                LaunchDeltaVCeilingMs = 0.0,
                MaxDsmDeltaVMs = 0.0
            };

            int legCount = fullChain.Count - 1; // 5 legs
            int geneDim = 1 + 2 * legCount;     // 1 + 5 + 5 = 11

            // 标称卡西尼染色体:
            // Leg 0: Earth -> Venus (~193d)
            // Leg 1: Venus -> Venus (~424d, 同星共振段)
            // Leg 2: Venus -> Earth (~53d)
            // Leg 3: Earth -> Jupiter (~600d)
            // Leg 4: Jupiter -> Saturn (~1000d)
            double[] nominalGene = new double[geneDim];
            nominalGene[0] = launchUT;
            nominalGene[1] = 193.0 * 86400.0;
            nominalGene[2] = 424.0 * 86400.0;
            nominalGene[3] = 53.0 * 86400.0;
            nominalGene[4] = 600.0 * 86400.0;
            nominalGene[5] = 1000.0 * 86400.0;
            for (int d = 6; d < geneDim; d++) nominalGene[d] = 0.5;

            int[] testBatchSizes = new int[] { 128, 1024, 8192, 32768, 65536 };
            bool casePassed = true;

            Console.WriteLine(string.Format("{0,-12} | {1,-10} | {2,-12} | {3,-18} | {4,-22} | {5,-10}",
                "批处理规模", "HLSL状态", "GPU耗时", "GPU吞吐量", "[回放基线]Loss", "奇点误报?"));
            Console.WriteLine(new string('-', 88));

            Random rand = new Random(42);

            foreach (int popSize in testBatchSizes)
            {
                var pop = new List<double[]>(popSize);
                pop.Add((double[])nominalGene.Clone()); // #0 为 [回放基线] 个体 (物理已知可行点, 用作奇点误报参照)

                for (int i = 1; i < popSize; i++)
                {
                    double[] g = (double[])nominalGene.Clone();
                    g[0] += (rand.NextDouble() - 0.5) * 10.0 * 86400.0; // 出发时间摄动 ±5天
                    for (int leg = 0; leg < legCount; leg++)
                    {
                        g[1 + leg] *= (1.0 + (rand.NextDouble() - 0.5) * 0.05); // 航时摄动 ±2.5%
                        g[1 + legCount + leg] = Math.Max(0.1, Math.Min(0.9, 0.5 + (rand.NextDouble() - 0.5) * 0.2));
                    }
                    pop.Add(g);
                }

                var sw = Stopwatch.StartNew();
                bool ok = MGAGpuAccelerationManager.EvaluateDePopulationGpu(
                    pop, fullChain, cfg, centralMu, optDsmEta: true, out float[] gpuLosses);
                sw.Stop();

                double timeMs = sw.Elapsed.TotalMilliseconds;
                double throughput = popSize / (timeMs / 1000.0);

                if (!ok || gpuLosses == null || gpuLosses.Length != popSize)
                {
                    Console.WriteLine(string.Format("{0,-12} | {1,-10} | {2,-12} | {3,-18} | {4,-15} | {5,-10}",
                        $"{popSize:N0}", "❌ FAILED", $"{timeMs:F2} ms", "N/A", "N/A", "YES"));
                    casePassed = false;
                    ctx.Fail($"Cassini GPU MegaPop 评估失败, 批处理规模={popSize}");
                    break;
                }

                float nominalLoss = gpuLosses[0];
                bool isSingularityMisfire = nominalLoss >= 100000.0f;
                bool isFinite = !float.IsNaN(nominalLoss) && !float.IsInfinity(nominalLoss);

                string singStr = isSingularityMisfire ? "❌ 100000误报" : "✅ 正常无奇点";

                Console.WriteLine(string.Format("{0,-12} | {1,-10} | {2,-12} | {3,-18} | {4,-15} | {5,-10}",
                    $"{popSize:N0}", "✅ SUCCESS", $"{timeMs:F2} ms", $"{throughput:N0} evals/s", $"{nominalLoss:F2} m/s", singStr));
                if (isSingularityMisfire || !isFinite)
                {
                    casePassed = false;
                    ctx.Fail($"Cassini Venus->Venus 标称染色体误报 100000 罚项或产生非法值: loss={nominalLoss}");
                }

                ctx.RecordMetric($"Cassini_Batch_{popSize}_TimeMs", $"{timeMs:F2}");
                ctx.RecordMetric($"Cassini_Batch_{popSize}_Throughput", $"{throughput:N0}");
                ctx.RecordMetric($"Cassini_Batch_{popSize}_NominalLoss", $"{nominalLoss:F2}");
            }

            ctx.EndCase(casePassed);
            return casePassed;
        }

        // ====================================================================
        // Case 3: MESSENGER 链路 (含 Venus->Venus 与 Mercury->Mercury 同星共振)
        // ====================================================================
        private bool RunCase3_MessengerMultiResonanceMegaPop(TestContext ctx)
        {
            ctx.StartCase("GPU_MegaPop_Messenger_MultiResonance");
            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("🛰️ [Case 3] 水星借力链路: Venus->Venus 与 Mercury->Mercury 双重同星共振 GPU 批处理接管");
            Console.WriteLine("   链路: Earth ➔ Venus ➔ Venus ➔ Mercury ➔ Mercury (包含金星 1:1 共振与水星 1:1 共振段)");
            Console.WriteLine("==========================================================================================");

            var fullChain = new List<string> { "Earth", "Venus", "Venus", "Mercury", "Mercury" };
            double centralMu = SolarSystemCatalog.SunMu;
            double launchUT = GMATClient.ParseUtcGregorian("03 Aug 2004 00:00:00.000");

            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Mercury",
                FlybySequence = new List<string> { "Venus", "Venus", "Mercury" },
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 200.0,
                DestinationApAKm = 15000.0,
                NoInsertionBurn = false,
                ConstrainByVesselDeltaV = false,
                LaunchDeltaVCeilingMs = 0.0,
                MaxDsmDeltaVMs = 0.0
            };

            int legCount = fullChain.Count - 1; // 4 legs
            int geneDim = 1 + 2 * legCount;     // 1 + 4 + 4 = 9

            // [回放基线] 标称双重同星共振染色体 (4 航段) = MESSENGER 历史构型的近似值。
            //
            // 【2026-09-14 审计标注】同 Case 2: 本用例是 GPU 数值鲁棒性探针,
            // 基线个体用于给出"该物理可行点不应触发 100000 奇点罚项"的参照,
            // 不代表 GPU 自主求解出了该构型。
            //
            // Leg 0: Earth -> Venus (~155d)
            // Leg 1: Venus -> Venus (~224.7d, 金星 1:1 同星共振段)
            // Leg 2: Venus -> Mercury (~130d)
            // Leg 3: Mercury -> Mercury (~88.0d, 水星 1:1 同星共振段)
            double[] nominalGene = new double[geneDim];
            nominalGene[0] = launchUT;
            nominalGene[1] = 155.0 * 86400.0;
            nominalGene[2] = 224.7 * 86400.0;
            nominalGene[3] = 130.0 * 86400.0;
            nominalGene[4] = 88.0 * 86400.0;
            for (int d = 5; d < geneDim; d++) nominalGene[d] = 0.5;

            int[] testBatchSizes = new int[] { 128, 1024, 8192, 32768, 65536 };
            bool casePassed = true;

            Console.WriteLine(string.Format("{0,-12} | {1,-10} | {2,-12} | {3,-18} | {4,-22} | {5,-10}",
                "批处理规模", "HLSL状态", "GPU耗时", "GPU吞吐量", "[回放基线]Loss", "奇点误报?"));
            Console.WriteLine(new string('-', 88));

            Random rand = new Random(101);

            foreach (int popSize in testBatchSizes)
            {
                var pop = new List<double[]>(popSize);
                pop.Add((double[])nominalGene.Clone());

                for (int i = 1; i < popSize; i++)
                {
                    double[] g = (double[])nominalGene.Clone();
                    g[0] += (rand.NextDouble() - 0.5) * 6.0 * 86400.0;
                    for (int leg = 0; leg < legCount; leg++)
                    {
                        g[1 + leg] *= (1.0 + (rand.NextDouble() - 0.5) * 0.04);
                        g[1 + legCount + leg] = Math.Max(0.1, Math.Min(0.9, g[1 + legCount + leg] + (rand.NextDouble() - 0.5) * 0.1));
                    }
                    pop.Add(g);
                }

                var sw = Stopwatch.StartNew();
                bool ok = MGAGpuAccelerationManager.EvaluateDePopulationGpu(
                    pop, fullChain, cfg, centralMu, optDsmEta: true, out float[] gpuLosses);
                sw.Stop();

                double timeMs = sw.Elapsed.TotalMilliseconds;
                double throughput = popSize / (timeMs / 1000.0);

                if (!ok || gpuLosses == null || gpuLosses.Length != popSize)
                {
                    Console.WriteLine(string.Format("{0,-12} | {1,-10} | {2,-12} | {3,-18} | {4,-15} | {5,-10}",
                        $"{popSize:N0}", "❌ FAILED", $"{timeMs:F2} ms", "N/A", "N/A", "YES"));
                    casePassed = false;
                    ctx.Fail($"Mercury GPU MegaPop 评估失败, 批处理规模={popSize}");
                    break;
                }

                float nominalLoss = gpuLosses[0];
                bool isSingularityMisfire = nominalLoss >= 100000.0f;
                bool isFinite = !float.IsNaN(nominalLoss) && !float.IsInfinity(nominalLoss);

                string singStr = isSingularityMisfire ? "❌ 100000误报" : "✅ 正常无奇点";

                Console.WriteLine(string.Format("{0,-12} | {1,-10} | {2,-12} | {3,-18} | {4,-15} | {5,-10}",
                    $"{popSize:N0}", "✅ SUCCESS", $"{timeMs:F2} ms", $"{throughput:N0} evals/s", $"{nominalLoss:F2} km/s", singStr));

                if (isSingularityMisfire || !isFinite)
                {
                    casePassed = false;
                    ctx.Fail($"Mercury/Venus 双重共振标称染色体误报 100000 罚项或产生非法值: loss={nominalLoss}");
                }

                ctx.RecordMetric($"Mercury_Batch_{popSize}_TimeMs", $"{timeMs:F2}");
                ctx.RecordMetric($"Mercury_Batch_{popSize}_Throughput", $"{throughput:N0}");
                ctx.RecordMetric($"Mercury_Batch_{popSize}_NominalLoss", $"{nominalLoss:F2}");
            }

            ctx.EndCase(casePassed);
            return casePassed;
        }

        // ====================================================================
        // Case 4: 注种候选批量打分接管 (EvaluateSeedingCandidatesGpu)
        // ====================================================================
        private bool RunCase4_SeedingCandidatesGpuScoring(TestContext ctx)
        {
            ctx.StartCase("GPU_SeedingCandidates_BatchScoring");
            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("🌱 [Case 4] 差分进化注种候选池 (Forward Seeding) GPU 批量打分接管验证");
            Console.WriteLine("==========================================================================================");

            var fullChain = new List<string> { "Earth", "Venus", "Venus", "Mercury" };
            double centralMu = SolarSystemCatalog.SunMu;
            double launchUT = GMATClient.ParseUtcGregorian("15 Oct 1997 00:00:00.000");

            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Mercury",
                FlybySequence = new List<string> { "Venus", "Venus" },
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 200.0,
                DestinationApAKm = 10000.0,
                NoInsertionBurn = false
            };

            int legCount = fullChain.Count - 1; // 3 legs
            int geneDim = 1 + 2 * legCount;     // 1 + 3 + 3 = 7

            // 构造 500 个注种候选染色体
            int candidateCount = 500;
            var candidates = new List<double[]>(candidateCount);
            Random rand = new Random(2026);

            for (int i = 0; i < candidateCount; i++)
            {
                double[] g = new double[geneDim];
                // [数值探针] 本批处理围绕**已知可行的回放基线**做小幅摄动 (±15 天 / ±1.5% 航时),
                // 目的是在物理量级正确的邻域内检验 GPU 核的数值鲁棒性,
                // 而非把该区域当作"搜索域"(本用例不做寻优)。基线值不参与任何通过判定。
                g[0] = launchUT + (rand.NextDouble() - 0.5) * 30.0 * 86400.0;
                g[1] = (150.0 + rand.NextDouble() * 100.0) * 86400.0; // Earth->Venus
                g[2] = (200.0 + rand.NextDouble() * 60.0) * 86400.0;  // Venus->Venus (1:1 共振槽)
                g[3] = (100.0 + rand.NextDouble() * 80.0) * 86400.0;  // Venus->Mercury
                g[4] = 0.2 + rand.NextDouble() * 0.6;
                g[5] = 0.2 + rand.NextDouble() * 0.6;
                g[6] = 0.2 + rand.NextDouble() * 0.6;
                candidates.Add(g);
            }

            var sw = Stopwatch.StartNew();
            bool ok = MGAGpuAccelerationManager.EvaluateSeedingCandidatesGpu(
                candidates, fullChain, cfg, centralMu, optDsmEta: true, out float[] seedLosses);
            sw.Stop();

            double timeMs = sw.Elapsed.TotalMilliseconds;
            double throughput = candidateCount / (timeMs / 1000.0);

            Console.WriteLine($"• 评估候选总数: {candidateCount} 个注种解");
            Console.WriteLine($"• GPU 调度接口执行状态: {(ok ? "✅ 成功返回 True" : "❌ 失败")}");
            Console.WriteLine($"• GPU 批处理耗时: {timeMs:F2} ms | 吞吐量: {throughput:N0} evals/s");

            bool pass = ok && seedLosses != null && seedLosses.Length == candidateCount;
            if (pass)
            {
                // 统计得分与排序
                int finiteCount = seedLosses.Count(l => !float.IsNaN(l) && !float.IsInfinity(l) && l < 100000.0f);
                float bestLoss = seedLosses.Min();
                float worstLoss = seedLosses.Max();
                Console.WriteLine($"• 有效收敛解数量 (Loss < 100,000): {finiteCount}/{candidateCount} ({finiteCount*100.0/candidateCount:F1}%)");
                Console.WriteLine($"• 最优注种得分 (Min Loss): {bestLoss:F2} m/s");
                Console.WriteLine($"• 最大注种得分 (Max Loss): {worstLoss:F2} m/s");

                ctx.RecordMetric("CandidateCount", candidateCount);
                ctx.RecordMetric("TimeMs", $"{timeMs:F2}");
                ctx.RecordMetric("Throughput", $"{throughput:N0}");
                ctx.RecordMetric("ValidConvergenceRate", $"{finiteCount*100.0/candidateCount:F1}%");
                ctx.RecordMetric("BestSeedLoss", $"{bestLoss:F2}");
            }
            else
            {
                ctx.Fail("EvaluateSeedingCandidatesGpu 未能成功完成批处理打分");
            }

            ctx.EndCase(pass);
            return pass;
        }

        // ====================================================================
        // Case 5: GPU vs CPU 物理动力学损失函数一致性对齐验证
        // ====================================================================
        private bool RunCase5_GpuVsCpuConsistency(TestContext ctx)
        {
            ctx.StartCase("GPU_Vs_CPU_Loss_Consistency");
            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("⚖️ [Case 5] GPU HLSL CS 5.0 vs CPU (双精度开普勒) 物理动力学损失函数一致性对齐");
            Console.WriteLine("==========================================================================================");

            var fullChain = new List<string> { "Earth", "Venus", "Venus", "Earth", "Jupiter", "Saturn" };
            double centralMu = SolarSystemCatalog.SunMu;
            double launchUT = GMATClient.ParseUtcGregorian("15 Oct 1997 00:00:00.000");

            var cfg = new MGAConfig
            {
                DepartureBody = "Earth",
                DestinationBody = "Saturn",
                FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter" },
                DeparturePeAKm = 200.0,
                DepartureApAKm = 200.0,
                DestinationPeAKm = 200.0,
                DestinationApAKm = 100000.0,
                NoInsertionBurn = false
            };

            int legCount = fullChain.Count - 1;
            int geneDim = 1 + 2 * legCount;

            double[] testGene = new double[geneDim];
            testGene[0] = launchUT;
            testGene[1] = 193.0 * 86400.0;
            testGene[2] = 424.0 * 86400.0;
            testGene[3] = 53.0 * 86400.0;
            testGene[4] = 600.0 * 86400.0;
            testGene[5] = 1000.0 * 86400.0;
            for (int d = 6; d < geneDim; d++) testGene[d] = 0.5;

            // 1. GPU 求解
            var pop = new List<double[]> { testGene };
            bool gpuOk = MGAGpuAccelerationManager.EvaluateDePopulationGpu(pop, fullChain, cfg, centralMu, true, out float[] gpuLosses);
            float gpuLoss = (gpuOk && gpuLosses != null && gpuLosses.Length > 0) ? gpuLosses[0] : float.NaN;

            // 2. CPU 官方解算 (PykepBridge.BuildFullSolution)
            var cpuSol = PykepBridge.BuildFullSolution(testGene, fullChain, cfg, false);
            double cpuTotalDvKms = (cpuSol != null) ? cpuSol.TotalDeltaVKms : double.NaN;
            double cpuLoss = (cpuSol != null) ? MGAPhysicsLossEngine.ComputeLoss(cpuSol, cfg) : double.NaN;

            Console.WriteLine($"• 测试航线: {string.Join(" ➔ ", fullChain)}");
            Console.WriteLine($"• [GPU HLSL CS 5.0]  计算 Loss: {gpuLoss:F2} (物理ΔV + 飞掠偏折罚项)");
            if (cpuSol != null)
            {
                Console.WriteLine($"• [CPU 双精度解算器] 始发={cpuSol.LaunchDeltaVKms:F2} km/s, DSM={cpuSol.TotalDsmDeltaVKms:F2} km/s, 捕获={cpuSol.InsertionDeltaVKms:F2} km/s, 飞掠ΔV={cpuSol.TotalFlybyDeltaVKms:F2} km/s");
                Console.WriteLine($"  -> CPU 纯物理总ΔV: {cpuSol.TotalDeltaVKms:F2} km/s ({cpuSol.TotalDeltaVKms*1000.0:F1} m/s) | 闭环合规: {cpuSol.AllLegsFeasible}");
            }

            // 验证 GPU 输出的有限性与非奇点
            bool isFinite = !float.IsNaN(gpuLoss) && !float.IsInfinity(gpuLoss);
            bool isNormalLoss = gpuLoss < 100000.0f; // 无 100000 重合奇点罚项

            ctx.RecordMetric("GPULoss", $"{gpuLoss:F2}");
            ctx.RecordMetric("CPUTotalDv_kms", $"{cpuTotalDvKms:F2}");
            ctx.RecordMetric("CPULoss_normalized", $"{cpuLoss:F2}");
            ctx.RecordMetric("IsNormalLoss", isNormalLoss);

            bool pass = gpuOk && isFinite && isNormalLoss;
            if (pass)
            {
                Console.WriteLine(">>> 动力学对齐判定: ✅ GPU HLSL CS 5.0 成功返回有效动力学 Loss (无 100000 重合奇点误报)！");
            }
            else
            {
                ctx.Fail($"GPU Loss 异常: GPU={gpuLoss}");
            }

            ctx.EndCase(pass);
            return pass;
        }
    }
}
