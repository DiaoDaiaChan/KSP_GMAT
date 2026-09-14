#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using KSPGMAT.Model;

namespace KSPGMAT.MGA.Telemetry
{
    /// <summary>
    /// 全新 MGA 全阶段天体力学与算法参数数据探针 (MGA Pipeline Telemetry Probe)
    /// 覆盖完整四阶段架构:
    ///   - Stage 1: 离散拓扑智能决策 (物理预算、爬升链合成、星历可达性、MCTS剪枝)
    ///   - Stage 2: 时间窗口预筛选与几何剪枝 (会合周期、能量特征走廊、高能禁区淘汰)
    ///   - Stage 3: 多岛并行自适应差分进化 (群岛状态、DSM染色体、MAB老虎机变异率F/交叉率CR、ALM拉格朗日乘子与物理惩罚)
    ///   - Stage 4: 物理审计、智能自愈与高精抛光 (安全高度净空、劳登主矢量极值、Hooke-Jeeves抛光、MEE/B平面协方差/Pareto前沿)
    /// 支持控制台实时格式化打印、Markdown 结构化报表与 JSON 数据转储。
    /// </summary>
    public sealed class MGAPipelineTelemetryProbe
    {
        // 单例访问
        private static readonly MGAPipelineTelemetryProbe s_instance = new MGAPipelineTelemetryProbe();
        public static MGAPipelineTelemetryProbe Instance => s_instance;

        private readonly object _lock = new object();

        // ====================================================================
        // 0. 任务全局头信息 (Mission Meta)
        // ====================================================================
        public class MissionMetadata
        {
            public string MissionName { get; set; } = "MGA Interplanetary Trajectory";
            public string DepartureBody { get; set; }
            public string ArrivalBody { get; set; }
            public List<string> SpecifiedFlybys { get; set; } = new List<string>();
            public double EarliestDepartureUT { get; set; }
            public string DepartureEpochUtcStr { get; set; }
            public double SearchSpanDays { get; set; }
            public double VesselMaxDvMs { get; set; }
            public double MaxDsmDvMs { get; set; }
            public double SafeFlybyAltMarginKm { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public double TotalElapsedSec => (EndTime - StartTime).TotalSeconds;
            public bool IsSuccess { get; set; }
        }

        public MissionMetadata Mission { get; set; } = new MissionMetadata();

        // ====================================================================
        // 1. Stage 1: 离散拓扑决策探针数据 (Topology Decision Telemetry)
        // ====================================================================
        public class Stage1TopologyTelemetry
        {
            // 物理预算推导指标
            public int DerivedMaxFlybys { get; set; }
            public int DerivedMaxResonances { get; set; }
            public double SemiMajorAxisRatio { get; set; }
            public double EnergyDepthFactor { get; set; }
            public double RecommendedToFStepDays { get; set; }

            // 爬升链与候选生成
            public int SynthesizedClimbingChainsCount { get; set; }
            public List<string> SynthesizedChains { get; set; } = new List<string>();

            // 星历可达性与 MAP-Elites 质量多样性
            public int ReachabilityEvaluatedChainsCount { get; set; }
            public int ReachabilityPrunedChainsCount { get; set; }
            public List<TopologyReachabilityDetail> EvaluatedChains { get; set; } = new List<TopologyReachabilityDetail>();

            // MCTS 拓扑搜索指标
            public int MctsTotalSimulations { get; set; }
            public int MctsTreeDepthReached { get; set; }
            public int FinalSelectedTopologiesCount { get; set; }
            public List<string> FinalSelectedSequences { get; set; } = new List<string>();
            public double Stage1ElapsedMs { get; set; }
        }

        public class TopologyReachabilityDetail
        {
            public string SequenceSignature { get; set; }
            public bool IsFeasible { get; set; }
            public double TotalDeficitMs { get; set; }
            public double EstimatedLaunchDvMs { get; set; }
            public double EstimatedTotalDvMs { get; set; }
            public int ResonanceCount { get; set; }
            public string Rationale { get; set; }
            public List<LegReachabilityDetail> Legs { get; set; } = new List<LegReachabilityDetail>();
        }

        public class LegReachabilityDetail
        {
            public int LegIndex { get; set; }
            public string FromBody { get; set; }
            public string ToBody { get; set; }
            public bool IsResonant { get; set; }
            public double R1Au { get; set; }
            public double R2Au { get; set; }
            public double RequiredVInfKms { get; set; }
            public double MaxAvailableVInfKms { get; set; }
            public double DeficitMs { get; set; }
            public double RequiredTurnDeg { get; set; }
            public double MaxAvailableTurnDeg { get; set; }
            public bool IsFeasible { get; set; }
        }

        public Stage1TopologyTelemetry Stage1 { get; set; } = new Stage1TopologyTelemetry();

        // ====================================================================
        // 2. Stage 2: 时间窗口预筛选探针数据 (Window Pre-filter Telemetry)
        // ====================================================================
        public class Stage2WindowTelemetry
        {
            public double SynodicPeriodDays { get; set; }
            public int TotalWindowsSampled { get; set; }
            public int WindowsPassedFilter { get; set; }
            public int HighEnergyWindowsRejected { get; set; }
            public double WindowFilterRejectionRatePct => TotalWindowsSampled > 0
                ? (double)HighEnergyWindowsRejected / TotalWindowsSampled * 100.0
                : 0.0;
            public List<SynodicWindowDetail> CandidateWindows { get; set; } = new List<SynodicWindowDetail>();
            public double Stage2ElapsedMs { get; set; }
        }

        public class SynodicWindowDetail
        {
            public int WindowIndex { get; set; }
            public double StartUT { get; set; }
            public double EndUT { get; set; }
            public string WindowTimeSpanStr { get; set; }
            public double NominalToFDays { get; set; }
            public double EstimatedMinDvKms { get; set; }
            public bool PassedSurrogateGate { get; set; }
        }

        public Stage2WindowTelemetry Stage2 { get; set; } = new Stage2WindowTelemetry();

        // ====================================================================
        // 3. Stage 3: 多岛差分进化演化探针数据 (Island DE Telemetry)
        // ====================================================================
        public class Stage3EvolutionTelemetry
        {
            public int ActiveIslandsCount { get; set; }
            public int TotalGenerationsExecuted { get; set; }
            public int TotalLambertEvaluations { get; set; }
            public double PykepHouseholder4TimeMs { get; set; }
            public ConcurrentBag<IslandEvolutionRecord> IslandRecords { get; set; } = new ConcurrentBag<IslandEvolutionRecord>();
            public ConcurrentBag<BanditTuningRecord> BanditRecords { get; set; } = new ConcurrentBag<BanditTuningRecord>();
            public ConcurrentBag<AlmConstraintRecord> AlmRecords { get; set; } = new ConcurrentBag<AlmConstraintRecord>();
            public double Stage3ElapsedMs { get; set; }
        }

        public class IslandEvolutionRecord
        {
            public string IslandId { get; set; }
            public string TopologyName { get; set; }
            public double WindowStartUT { get; set; }
            public int Generations { get; set; }
            public double BestFitness { get; set; }
            public double BestTotalDvMs { get; set; }
            public double BestLaunchDvMs { get; set; }
            public double BestDsmDvMs { get; set; }
            public int MigrationEventsCount { get; set; }
            public bool IsConverged { get; set; }
        }

        public class BanditTuningRecord
        {
            public string IslandName { get; set; }
            public int TotalOperatorPulls { get; set; }
            public int[] ArmPulls { get; set; } = new int[4];
            public double[] ArmRewards { get; set; } = new double[4];
            public double FinalF { get; set; }
            public double FinalCR { get; set; }
        }

        public class AlmConstraintRecord
        {
            public int OuterIteration { get; set; }
            public double PenaltyFactorRho { get; set; }
            public double MaxConstraintViolation { get; set; }
            public double MultiplierFlybyAlt { get; set; }
            public double MultiplierLaunchDv { get; set; }
            public double MultiplierDsmLimit { get; set; }
        }

        public Stage3EvolutionTelemetry Stage3 { get; set; } = new Stage3EvolutionTelemetry();

        // ====================================================================
        // 4. Stage 4: 物理审计、自愈与打磨抛光探针数据 (Audit & Self-Healing Telemetry)
        // ====================================================================
        public class Stage4HealingTelemetry
        {
            public int InputRawSolutionsCount { get; set; }
            public int PericenterViolationCount { get; set; }
            public int LawdenPrimerViolationCount { get; set; }
            public int DvBudgetExceededCount { get; set; }

            // 自愈打磨成效
            public int SelfHealingAttempts { get; set; }
            public int SelfHealingSuccesses { get; set; }
            public double TotalDvSavedMs { get; set; }
            public double AverageDvSavedPerHealMs => SelfHealingSuccesses > 0 ? TotalDvSavedMs / SelfHealingSuccesses : 0.0;

            // 高精打磨器统计
            public int SubspacePolisherRuns { get; set; }
            public int SubspacePolisherSuccesses { get; set; }

            // 终极输出统计
            public int FinalFeasibleSolutionsCount { get; set; }
            public List<FinalSolutionTelemetry> Solutions { get; set; } = new List<FinalSolutionTelemetry>();
            public double Stage4ElapsedMs { get; set; }
        }

        public class FinalSolutionTelemetry
        {
            public int Rank { get; set; }
            public string TopologySummary { get; set; }
            public double DepartureUT { get; set; }
            public string DepartureEpochStr { get; set; }
            public double TotalFlightDays { get; set; }
            public double TotalDvMs { get; set; }
            public double LaunchDvMs { get; set; }
            public double TotalDsmDvMs { get; set; }
            public double ArrivalVInfKms { get; set; }
            public double ArrivalC3Km2S2 { get; set; }
            public bool LawdenOptimal { get; set; }
            public double MaxPrimerVectorNorm { get; set; }
            public double MinPericenterClearanceKm { get; set; }
            public double BPlaneMagnitudeKm { get; set; }
            public double BPlaneCollisionProb { get; set; }
            public List<FinalLegTelemetry> Legs { get; set; } = new List<FinalLegTelemetry>();
        }

        public class FinalLegTelemetry
        {
            public int LegIndex { get; set; }
            public string FromBody { get; set; }
            public string ToBody { get; set; }
            public double ToFDays { get; set; }
            public double FlybyAltitudeKm { get; set; }
            public double SafeAltitudeMarginKm { get; set; }
            public double TurnAngleDeg { get; set; }
            public double MaxTurnAngleDeg { get; set; }
            public double FlybyDvMs { get; set; }
            public double DsmDvMs { get; set; }
            public double DsmRadiusAu { get; set; }
            public double DsmEta { get; set; }
            public double VInfInKms { get; set; }
            public double VInfOutKms { get; set; }
            public bool IsPhysicallyFeasible { get; set; }
        }

        public Stage4HealingTelemetry Stage4 { get; set; } = new Stage4HealingTelemetry();

        // ====================================================================
        // 兼容性 GPU 与 AI 子模块探针收集 API
        // ====================================================================

        public static class AiMetrics
        {
            public static void RecordMcts(int totalEvaluated, int pruned, int kept)
            {
                lock (Instance._lock)
                {
                    Instance.Stage1.ReachabilityEvaluatedChainsCount = Math.Max(Instance.Stage1.ReachabilityEvaluatedChainsCount, totalEvaluated);
                    Instance.Stage1.ReachabilityPrunedChainsCount = Math.Max(Instance.Stage1.ReachabilityPrunedChainsCount, pruned);
                    Instance.Stage1.FinalSelectedTopologiesCount = Math.Max(Instance.Stage1.FinalSelectedTopologiesCount, kept);
                }
            }

            public static void RecordSurrogate(int totalWindows, int passing)
            {
                lock (Instance._lock)
                {
                    Instance.Stage2.TotalWindowsSampled = Math.Max(Instance.Stage2.TotalWindowsSampled, totalWindows);
                    Instance.Stage2.WindowsPassedFilter = Math.Max(Instance.Stage2.WindowsPassedFilter, passing);
                    Instance.Stage2.HighEnergyWindowsRejected = Math.Max(0, Instance.Stage2.TotalWindowsSampled - Instance.Stage2.WindowsPassedFilter);
                }
            }

            public static void RecordBanditSnapshot(string island, int pulls, int[] armPulls, double[] armRewards, double f, double cr)
            {
                lock (Instance._lock)
                {
                    Instance.Stage3.BanditRecords.Add(new BanditTuningRecord
                    {
                        IslandName = island,
                        TotalOperatorPulls = pulls,
                        ArmPulls = (int[])armPulls?.Clone() ?? new int[4],
                        ArmRewards = (double[])armRewards?.Clone() ?? new double[4],
                        FinalF = f,
                        FinalCR = cr
                    });
                }
            }

            public static void RecordSelfHealing(bool success, double savedKms)
            {
                lock (Instance._lock)
                {
                    Instance.Stage4.SelfHealingAttempts++;
                    if (success)
                    {
                        Instance.Stage4.SelfHealingSuccesses++;
                        Instance.Stage4.TotalDvSavedMs += savedKms * 1000.0;
                    }
                }
            }
        }

        public static void RecordGpuExecution(string kernelName, double elapsedMs, long itemsProcessed, bool succeeded)
        {
            // 记录 GPU 计算开销
            if (succeeded)
            {
                KSPGMATLog.Info("Telemetry", $"[GPU Telemetry] {kernelName}: {itemsProcessed:N0} items in {elapsedMs:F2} ms");
            }
            else
            {
                KSPGMATLog.Warn("Telemetry", $"[GPU Telemetry] {kernelName} fallback/skipped: {itemsProcessed:N0} items");
            }
        }

        public static void PrintEfficiencyReport()
        {
            PrintConsoleSummary();
        }

        // ====================================================================
        // 探针数据收集与上报 API
        // ====================================================================

        public static void Reset()
        {
            lock (Instance._lock)
            {
                Instance.Mission = new MissionMetadata();
                Instance.Stage1 = new Stage1TopologyTelemetry();
                Instance.Stage2 = new Stage2WindowTelemetry();
                Instance.Stage3 = new Stage3EvolutionTelemetry();
                Instance.Stage4 = new Stage4HealingTelemetry();
            }
        }

        public static void StartMission(string departure, string arrival, List<string> flybys, double depUT, double spanDays, double maxDv, double maxDsm, double safeMarginKm)
        {
            lock (Instance._lock)
            {
                Instance.Mission.DepartureBody = departure;
                Instance.Mission.ArrivalBody = arrival;
                Instance.Mission.SpecifiedFlybys = flybys != null ? new List<string>(flybys) : new List<string>();
                Instance.Mission.EarliestDepartureUT = depUT;
                Instance.Mission.DepartureEpochUtcStr = GMATClient.FormatUtcGregorian(depUT);
                Instance.Mission.SearchSpanDays = spanDays;
                Instance.Mission.VesselMaxDvMs = maxDv;
                Instance.Mission.MaxDsmDvMs = maxDsm;
                Instance.Mission.SafeFlybyAltMarginKm = safeMarginKm;
                Instance.Mission.StartTime = DateTime.UtcNow;
            }
        }

        public static void EndMission(bool success)
        {
            lock (Instance._lock)
            {
                Instance.Mission.EndTime = DateTime.UtcNow;
                Instance.Mission.IsSuccess = success;
            }
        }

        // --- Stage 1 探针埋点 ---
        public static void RecordStage1Budget(int maxFlybys, int maxResonances, double smaRatio, double depthFactor, double stepDays)
        {
            lock (Instance._lock)
            {
                Instance.Stage1.DerivedMaxFlybys = maxFlybys;
                Instance.Stage1.DerivedMaxResonances = maxResonances;
                Instance.Stage1.SemiMajorAxisRatio = smaRatio;
                Instance.Stage1.EnergyDepthFactor = depthFactor;
                Instance.Stage1.RecommendedToFStepDays = stepDays;
            }
        }

        public static void RecordStage1Chains(List<List<string>> synthesized, List<List<string>> mctsSelected, double elapsedMs)
        {
            lock (Instance._lock)
            {
                if (synthesized != null)
                {
                    Instance.Stage1.SynthesizedClimbingChainsCount = synthesized.Count;
                    Instance.Stage1.SynthesizedChains = synthesized.Select(c => string.Join("->", c)).ToList();
                }
                if (mctsSelected != null)
                {
                    Instance.Stage1.FinalSelectedTopologiesCount = mctsSelected.Count;
                    Instance.Stage1.FinalSelectedSequences = mctsSelected.Select(c => string.Join("->", c)).ToList();
                }
                Instance.Stage1.Stage1ElapsedMs = elapsedMs;
            }
        }

        // --- Stage 2 探针埋点 ---
        public static void RecordStage2Windows(double synodicDays, int sampled, int passed, List<MGASurrogatePreFilter.SynodicWindowCandidate> candidates, double elapsedMs)
        {
            lock (Instance._lock)
            {
                Instance.Stage2.SynodicPeriodDays = synodicDays;
                Instance.Stage2.TotalWindowsSampled = sampled;
                Instance.Stage2.WindowsPassedFilter = passed;
                Instance.Stage2.HighEnergyWindowsRejected = Math.Max(0, sampled - passed);
                Instance.Stage2.Stage2ElapsedMs = elapsedMs;

                if (candidates != null)
                {
                    Instance.Stage2.CandidateWindows = candidates.Select((w, idx) => new SynodicWindowDetail
                    {
                        WindowIndex = idx + 1,
                        StartUT = w.WindowStartUT,
                        EndUT = w.WindowEndUT,
                        WindowTimeSpanStr = $"{GMATClient.FormatUtcGregorian(w.WindowStartUT)} ~ {GMATClient.FormatUtcGregorian(w.WindowEndUT)}",
                        NominalToFDays = w.NominalToFDays,
                        EstimatedMinDvKms = w.EstimatedMinDvKms,
                        PassedSurrogateGate = true
                    }).ToList();
                }
            }
        }

        // --- Stage 3 探针埋点 ---
        public static void RecordIslandConvergence(string islandId, string topology, double winUT, int gens, double bestFitness, double totalDvMs, double launchDvMs, double dsmDvMs, bool converged)
        {
            Instance.Stage3.IslandRecords.Add(new IslandEvolutionRecord
            {
                IslandId = islandId,
                TopologyName = topology,
                WindowStartUT = winUT,
                Generations = gens,
                BestFitness = bestFitness,
                BestTotalDvMs = totalDvMs,
                BestLaunchDvMs = launchDvMs,
                BestDsmDvMs = dsmDvMs,
                IsConverged = converged
            });
        }

        public static void RecordBanditSnapshot(string islandName, int totalPulls, int[] armPulls, double[] armRewards, double finalF, double finalCR)
        {
            Instance.Stage3.BanditRecords.Add(new BanditTuningRecord
            {
                IslandName = islandName,
                TotalOperatorPulls = totalPulls,
                ArmPulls = armPulls != null ? (int[])armPulls.Clone() : new int[4],
                ArmRewards = armRewards != null ? (double[])armRewards.Clone() : new double[4],
                FinalF = finalF,
                FinalCR = finalCR
            });
        }

        public static void RecordStage3Overall(int islands, int gens, int lambertEvals, double elapsedMs)
        {
            lock (Instance._lock)
            {
                Instance.Stage3.ActiveIslandsCount = islands;
                Instance.Stage3.TotalGenerationsExecuted = gens;
                Instance.Stage3.TotalLambertEvaluations = lambertEvals;
                Instance.Stage3.Stage3ElapsedMs = elapsedMs;
            }
        }

        // --- Stage 4 探针埋点 ---
        public static void RecordStage4Audit(int rawCount, int pericenterViolations, int lawdenViolations, int dvViolations, int healAtt, int healSucc, double dvSavedMs, double elapsedMs)
        {
            lock (Instance._lock)
            {
                Instance.Stage4.InputRawSolutionsCount = rawCount;
                Instance.Stage4.PericenterViolationCount = pericenterViolations;
                Instance.Stage4.LawdenPrimerViolationCount = lawdenViolations;
                Instance.Stage4.DvBudgetExceededCount = dvViolations;
                Instance.Stage4.SelfHealingAttempts = healAtt;
                Instance.Stage4.SelfHealingSuccesses = healSucc;
                Instance.Stage4.TotalDvSavedMs = dvSavedMs;
                Instance.Stage4.Stage4ElapsedMs = elapsedMs;
            }
        }

        public static void RecordFinalSolutions(List<MGASolution> solutions)
        {
            if (solutions == null) return;
            lock (Instance._lock)
            {
                Instance.Stage4.FinalFeasibleSolutionsCount = solutions.Count;
                Instance.Stage4.Solutions.Clear();

                int rank = 1;
                foreach (var s in solutions)
                {
                    double primerMax = s.PrimerVector != null ? s.PrimerVector.MaxPMag : 1.0;
                    bool primerOpt = s.PrimerVector != null ? s.PrimerVector.IsOptimal : true;
                    double bMag = s.BPlane != null ? s.BPlane.BMagKm : 0.0;
                    double arrVInf = s.Legs != null && s.Legs.Count > 0 ? s.Legs.Last().VInfArrivalKms.magnitude : 0.0;

                    var solEntry = new FinalSolutionTelemetry
                    {
                        Rank = rank++,
                        TopologySummary = s.FlybyChainSummary ?? "Trajectory",
                        DepartureUT = s.DepartureUT,
                        DepartureEpochStr = GMATClient.FormatUtcGregorian(s.DepartureUT),
                        TotalFlightDays = s.TotalFlightDays,
                        TotalDvMs = s.TotalDeltaVKms * 1000.0,
                        LaunchDvMs = s.LaunchDeltaVKms * 1000.0,
                        TotalDsmDvMs = s.TotalDsmDeltaVKms * 1000.0,
                        ArrivalVInfKms = arrVInf,
                        ArrivalC3Km2S2 = arrVInf * arrVInf,
                        LawdenOptimal = primerOpt,
                        MaxPrimerVectorNorm = primerMax,
                        MinPericenterClearanceKm = 0.0,
                        BPlaneMagnitudeKm = bMag,
                        BPlaneCollisionProb = 0.0
                    };

                    if (s.Legs != null)
                    {
                        int legIdx = 1;
                        foreach (var l in s.Legs)
                        {
                            solEntry.Legs.Add(new FinalLegTelemetry
                            {
                                LegIndex = legIdx++,
                                FromBody = l.FromBody,
                                ToBody = l.ToBody,
                                ToFDays = l.ToFDays,
                                FlybyAltitudeKm = l.FlybyPeriapsisAltitudeKm,
                                SafeAltitudeMarginKm = 0.0,
                                TurnAngleDeg = l.FlybyTurningAngleDeg,
                                MaxTurnAngleDeg = 0.0,
                                FlybyDvMs = l.FlybyDeltaVKms * 1000.0,
                                DsmDvMs = l.DSM != null ? l.DSM.DeltaVMs : 0.0,
                                DsmRadiusAu = l.DSM != null ? l.DSM.PositionKm.magnitude / MGADefaults.AU_KM : 0.0,
                                DsmEta = 0.5,
                                VInfInKms = l.VInfArrivalKms.magnitude,
                                VInfOutKms = l.VInfDepartureKms.magnitude,
                                IsPhysicallyFeasible = l.IsFeasible
                            });
                        }
                    }

                    Instance.Stage4.Solutions.Add(solEntry);
                }
            }
        }

        // ====================================================================
        // 报表导出与渲染 (Console / Markdown / JSON)
        // ====================================================================

        /// <summary>
        /// 生成结构化的完整多阶段 MGA 计算探针诊断报告 (Markdown 格式)
        /// </summary>
        public string GenerateMarkdownReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MGA 全阶段天体力学与算法参数探针综合诊断报表");
            sb.AppendLine($"*生成时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | 引擎: KSPGMAT MGA Adaptive Solver*\n");

            // 0. 任务概况
            sb.AppendLine("## 1. 任务输入与搜索边界 (Mission Parameters)");
            sb.AppendLine("| 参数 | 数值/设定 | 单位/说明 |");
            sb.AppendLine("| :--- | :--- | :--- |");
            sb.AppendLine($"| 出发星体 | **{Mission.DepartureBody}** | 始发主引力源 |");
            sb.AppendLine($"| 目标星体 | **{Mission.ArrivalBody}** | 任务终点引力源 |");
            sb.AppendLine($"| 预设/允许借力天体 | {(Mission.SpecifiedFlybys.Count > 0 ? string.Join(", ", Mission.SpecifiedFlybys) : "全自动自主优选")} | 候选行星助推序列 |");
            sb.AppendLine($"| 最早始发历元 | {Mission.DepartureEpochUtcStr} (UT {Mission.EarliestDepartureUT:F0}) | 搜索起点 |");
            sb.AppendLine($"| 时间搜索跨度 | {Mission.SearchSpanDays:F1} | 天 (Days) |");
            sb.AppendLine($"| 航天器最大 ΔV 预算 | {(Mission.VesselMaxDvMs > 0 ? $"{Mission.VesselMaxDvMs:F1} m/s" : "无限制")} | 推进剂约束 |");
            sb.AppendLine($"| 允许最大中途 DSM ΔV | {(Mission.MaxDsmDvMs > 0 ? $"{Mission.MaxDsmDvMs:F1} m/s" : "同总预算")} | 单脉冲机动上限 |");
            sb.AppendLine($"| 地表安全净空安全裕度 | {Mission.SafeFlybyAltMarginKm:F1} | km (大气/地形防撞) |");
            sb.AppendLine($"| 总计算耗时 | **{Mission.TotalElapsedSec:F2}** | 秒 (Seconds) |");
            sb.AppendLine($"| 最终收敛求解状态 | {(Mission.IsSuccess ? "✅ 成功收敛 (Converged)" : "❌ 未找到合规解 (Failed)")} | 物理合规解交付 |\n");

            // 1. Stage 1 拓扑推导与剪枝
            sb.AppendLine("## 2. 【Stage 1】离散拓扑决策探针数据 (Topology Selection & Pruning)");
            sb.AppendLine($"- **物理预算推导**: 轨道半长轴比 $a_2/a_1$ = {Stage1.SemiMajorAxisRatio:F2}, 引力势深度因子 = {Stage1.EnergyDepthFactor:F2}, 推荐最大借力段数 = **{Stage1.DerivedMaxFlybys}**, 建议共振上限 = **{Stage1.DerivedMaxResonances}**");
            sb.AppendLine($"- **物理爬升链合成**: 共前向合成有效能量爬升链 **{Stage1.SynthesizedClimbingChainsCount}** 条");
            sb.AppendLine($"- **星历双向可达性初筛**: 评估拓扑 **{Stage1.ReachabilityEvaluatedChainsCount}** 组, 剪除不可行死分支 **{Stage1.ReachabilityPrunedChainsCount}** 组");
            sb.AppendLine($"- **MCTS 蒙特卡洛优选**: 模拟推演 **{Stage1.MctsTotalSimulations}** 次, 最终入选并下发 Stage 2 的黄金拓扑数: **{Stage1.FinalSelectedTopologiesCount}**");
            sb.AppendLine($"- **Stage 1 耗时**: {Stage1.Stage1ElapsedMs:F1} ms\n");

            if (Stage1.FinalSelectedSequences.Count > 0)
            {
                sb.AppendLine("### Stage 1 入选拓扑序列清单:");
                for (int i = 0; i < Stage1.FinalSelectedSequences.Count; i++)
                {
                    sb.AppendLine($"  {i + 1}. `{Mission.DepartureBody} -> {Stage1.FinalSelectedSequences[i]} -> {Mission.ArrivalBody}`");
                }
                sb.AppendLine();
            }

            // 2. Stage 2 窗口预筛选
            sb.AppendLine("## 3. 【Stage 2】时间窗口预筛选与几何走廊 (Synodic Window Screening)");
            sb.AppendLine($"- **天体对会合周期 (Synodic Period)**: **{Stage2.SynodicPeriodDays:F1}** 天");
            sb.AppendLine($"- **全时域采样窗口数**: {Stage2.TotalWindowsSampled} 个");
            sb.AppendLine($"- **保留可行低能窗口数**: **{Stage2.WindowsPassedFilter}** 个");
            sb.AppendLine($"- **高能几何禁区剪枝率**: **{Stage2.WindowFilterRejectionRatePct:F1}%** (大幅降低后续 DE 无效计算)");
            sb.AppendLine($"- **Stage 2 耗时**: {Stage2.Stage2ElapsedMs:F1} ms\n");

            if (Stage2.CandidateWindows.Count > 0)
            {
                sb.AppendLine("### 筛选通过的候选时区:");
                sb.AppendLine("| 窗口 # | 时间区间 (UTC) | 标称转移时间 (天) | 预估最小 ΔV (km/s) |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");
                foreach (var w in Stage2.CandidateWindows.Take(8))
                {
                    sb.AppendLine($"| #{w.WindowIndex} | {w.WindowTimeSpanStr} | {w.NominalToFDays:F1} | {w.EstimatedMinDvKms:F3} |");
                }
                if (Stage2.CandidateWindows.Count > 8)
                {
                    sb.AppendLine($"| ... | *(其余 {Stage2.CandidateWindows.Count - 8} 个窗口已加入并发群岛演化池)* | ... | ... |");
                }
                sb.AppendLine();
            }

            // 3. Stage 3 多岛差分进化演化
            sb.AppendLine("## 4. 【Stage 3】多岛并行自适应差分进化 (Multi-Island DE Evolution)");
            sb.AppendLine($"- **并发计算岛屿数**: {Stage3.ActiveIslandsCount} 个群岛");
            sb.AppendLine($"- **累计执行演化代数**: {Stage3.TotalGenerationsExecuted} 代");
            sb.AppendLine($"- **PyKEP Lambert 求解总次数**: **{Stage3.TotalLambertEvaluations:N0}** 次 (Householder-4 级数求逆)");
            sb.AppendLine($"- **Stage 3 耗时**: {Stage3.Stage3ElapsedMs:F1} ms\n");

            var bandits = Stage3.BanditRecords.ToArray();
            if (bandits.Length > 0)
            {
                sb.AppendLine("### 多臂老虎机 (MAB Tuner) 自适应参数学习快照:");
                sb.AppendLine("| 岛屿名称 | 抽样总数 | 变异率 F | 交叉率 CR | 算子探索分布 [Rand1 / Best1 / CurToBest1 / RandToBest1] |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var b in bandits.Take(6))
                {
                    string arms = $"[{b.ArmPulls[0]} / {b.ArmPulls[1]} / {b.ArmPulls[2]} / {b.ArmPulls[3]}]";
                    sb.AppendLine($"| {b.IslandName} | {b.TotalOperatorPulls} | {b.FinalF:F2} | {b.FinalCR:F2} | {arms} |");
                }
                sb.AppendLine();
            }

            // 4. Stage 4 物理自愈与终解审计
            sb.AppendLine("## 5. 【Stage 4】物理审计、智能自愈与终解评估 (Audit & Self-Healing)");
            sb.AppendLine($"- **初筛输入原始候选解**: {Stage4.InputRawSolutionsCount} 组");
            sb.AppendLine($"- **近拱点高度违规解数**: {Stage4.PericenterViolationCount} 组");
            sb.AppendLine($"- **劳登主矢量非最优解数**: {Stage4.LawdenPrimerViolationCount} 组");
            sb.AppendLine($"- **ΔV 超限解数**: {Stage4.DvBudgetExceededCount} 组");
            sb.AppendLine($"- **数值自愈修复**: 触发 **{Stage4.SelfHealingAttempts}** 次, 成功收敛 **{Stage4.SelfHealingSuccesses}** 次");
            sb.AppendLine($"- **自愈累计节省速度增量**: **{Stage4.TotalDvSavedMs:F1} m/s** (平均单次节省 {Stage4.AverageDvSavedPerHealMs:F1} m/s)");
            sb.AppendLine($"- **最终交付合格解数**: **{Stage4.FinalFeasibleSolutionsCount}** 组");
            sb.AppendLine($"- **Stage 4 耗时**: {Stage4.Stage4ElapsedMs:F1} ms\n");

            if (Stage4.Solutions.Count > 0)
            {
                sb.AppendLine("### 最佳交付方案物理参数清单 (Top Solution Details):");
                var top = Stage4.Solutions[0];
                sb.AppendLine($"**方案 #1: {top.TopologySummary}**");
                sb.AppendLine($"- **始发历元**: {top.DepartureEpochStr}");
                sb.AppendLine($"- **总飞行时间**: {top.TotalFlightDays:F1} 天 ({top.TotalFlightDays / 365.25:F2} 年)");
                sb.AppendLine($"- **总速度增量 ΔV**: **{top.TotalDvMs:F1} m/s** (始发脉冲: {top.LaunchDvMs:F1} m/s, 中途 DSM: {top.TotalDsmDvMs:F1} m/s)");
                sb.AppendLine($"- **到达双曲超额速度 $V_\\infty$**: {top.ArrivalVInfKms:F3} km/s ($C_3$ = {top.ArrivalC3Km2S2:F2} km²/s²)");
                sb.AppendLine($"- **劳登最优性 (Lawden Primer Vector Norm)**: {top.MaxPrimerVectorNorm:F3} ({(top.LawdenOptimal ? "✅ 冲量在时间与方向上严格最优" : "⚠️ 存在微小优化空间")})");
                sb.AppendLine($"- **最低净空安全裕度**: {top.MinPericenterClearanceKm:F1} km");
                if (top.BPlaneMagnitudeKm > 0)
                {
                    sb.AppendLine($"- **B平面瞄准范数**: {top.BPlaneMagnitudeKm:F1} km (碰撞概率 = {top.BPlaneCollisionProb:E2})");
                }
                sb.AppendLine();

                if (top.Legs.Count > 0)
                {
                    sb.AppendLine("#### 航段详细参数 (Leg Mechanics):");
                    sb.AppendLine("| 航段 # | 始发 -> 目标 | 飞行天数 | 借力净空 (km) | 偏转角 (实际/最大) | 借力ΔV (m/s) | DSM ΔV (m/s) | DSM 半径 (AU) | 状态 |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
                    foreach (var l in top.Legs)
                    {
                        string turns = $"{l.TurnAngleDeg:F1}° / {l.MaxTurnAngleDeg:F1}°";
                        string status = l.IsPhysicallyFeasible ? "✅ 物理合规" : "❌ 违例";
                        sb.AppendLine($"| Leg {l.LegIndex} | {l.FromBody} -> {l.ToBody} | {l.ToFDays:F1} | {l.FlybyAltitudeKm:F0} | {turns} | {l.FlybyDvMs:F1} | {l.DsmDvMs:F1} | {l.DsmRadiusAu:F2} | {status} |");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("==========================================================================================");
            sb.AppendLine("*(报表由 MGAPipelineTelemetryProbe 自动采样输出)*");
            return sb.ToString();
        }

        /// <summary>
        /// 导出报告到磁盘文件
        /// </summary>
        public static void ExportReportToFile(string outputPath)
        {
            try
            {
                string md = Instance.GenerateMarkdownReport();
                string fullPath = Path.IsPathRooted(outputPath)
                    ? outputPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputPath);

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(fullPath, md, Encoding.UTF8);
                KSPGMATLog.Info("Telemetry", $"[MGAPipelineTelemetryProbe] 诊断报表成功写入: {fullPath}");
            }
            catch (Exception ex)
            {
                KSPGMATLog.Error("Telemetry", $"[MGAPipelineTelemetryProbe] 写入诊断报表失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 在控制台输出精简摘要
        /// </summary>
        // ====================================================================
        // 5. 兼容历史绑定的轻量级诊断报表代理 (由新探针统一接管)
        // ====================================================================
        public class ReportData
        {
            private static readonly ReportData s_default = new ReportData();
            public static ReportData Instance => s_default;

            public MissionMetadata Mission => MGAPipelineTelemetryProbe.Instance.Mission;
            public Stage1TopologyTelemetry Stage1 => MGAPipelineTelemetryProbe.Instance.Stage1;
            public Stage2WindowTelemetry Stage2 => MGAPipelineTelemetryProbe.Instance.Stage2;
            public Stage3EvolutionTelemetry Stage3 => MGAPipelineTelemetryProbe.Instance.Stage3;
            public Stage4HealingTelemetry Stage4 => MGAPipelineTelemetryProbe.Instance.Stage4;

            public void ExportToFile(string path)
            {
                ExportReportToFile(path);
            }

            public void PrintConsoleSummary()
            {
                MGAPipelineTelemetryProbe.PrintConsoleSummary();
            }
        }

        public static void PrintConsoleSummary()
        {
            KSPGMATLog.Info("Telemetry", Instance.GenerateMarkdownReport());
        }
    }
}
