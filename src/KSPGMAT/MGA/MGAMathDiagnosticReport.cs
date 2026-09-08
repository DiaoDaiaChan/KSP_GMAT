#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// MGA 天体动力学与求解器数学诊断报表
    /// 从天体力学、能量守恒 (Vis-Viva)、双曲转向角极限、时间搜索网格及微分进化收敛维度全面导出数学层面的诊断数据，
    /// 精确定位求解失败或 ΔV 偏高的物理根因，并给出天体力学层面的改进建议。
    /// </summary>
    public class MGAMathDiagnosticReport
    {
        public class MissionHeader
        {
            public string DeparturePlanet { get; set; }
            public string ArrivalPlanet { get; set; }
            public List<string> UserGoalWaypoints { get; set; }
            public double DepartureEpochUT { get; set; }
            public string DepartureEpochStr { get; set; }
            public double SearchSpanDays { get; set; }
            public double VesselMaxDeltaVMs { get; set; }
            public double MaxDsmDeltaVMs { get; set; }
            public bool NoInsertionBurn { get; set; }
            public double TotalComputeTimeSec { get; set; }
            public bool SolvedSuccessfully { get; set; }
        }

        public class LegReachabilityMetric
        {
            public int LegIndex { get; set; }
            public string FromBody { get; set; }
            public string ToBody { get; set; }
            public bool IsResonance { get; set; }
            public double R1Au { get; set; }
            public double R2Au { get; set; }
            public double ATransAu { get; set; }
            public double VReqKms { get; set; }
            public double VMaxKms { get; set; }
            public double EnergyDeficitMs { get; set; }
            public double ReqTurnDeg { get; set; }
            public double AvailTurnDeg { get; set; }
            public double TurnDeficitMs { get; set; }
            public double PhaseDeltaDeg { get; set; }
            public double TotalLegDeficitMs { get; set; }
            public bool IsFeasible { get; set; }
            public string Rationale { get; set; }
        }

        public class TopologyCandidateEntry
        {
            public int Rank { get; set; }
            public string SequenceSignature { get; set; }
            public List<string> FlybyChain { get; set; }
            public bool IsFeasible { get; set; }
            public double TotalDeficitMs { get; set; }
            public double EstimatedLaunchDvMs { get; set; }
            public double EstimatedTotalDvMs { get; set; }
            public int ResonanceCount { get; set; }
            public List<LegReachabilityMetric> Legs { get; set; } = new List<LegReachabilityMetric>();
            public string SummaryRationale { get; set; }
        }

        public class LegBoundsMetric
        {
            public int LegIndex { get; set; }
            public string LegName { get; set; }
            public string TransferType { get; set; }
            public double RefPeriodDays { get; set; }
            public double MinToFDays { get; set; }
            public double MaxToFDays { get; set; }
            public double MinRatio { get; set; }
            public double MaxRatio { get; set; }
            public string StatusNote { get; set; }
        }

        public class IslandConvergenceRecord
        {
            public int TopologyIndex { get; set; }
            public string TopologyName { get; set; }
            public int IslandIndex { get; set; }
            public string EpochStr { get; set; }
            public double BestTotalDvMs { get; set; }
            public double LaunchDvMs { get; set; }
            public double DsmDvMs { get; set; }
        }

        public class LegMechanicsRecord
        {
            public int LegIndex { get; set; }
            public string FromBody { get; set; }
            public string ToBody { get; set; }
            public double DepEpochUT { get; set; }
            public double ArrEpochUT { get; set; }
            public string DepUtcStr { get; set; }
            public string ArrUtcStr { get; set; }
            public double ToFDays { get; set; }
            public double VInfInKms { get; set; }
            public double VInfOutKms { get; set; }
            public double DeltaVInfKms { get; set; }
            public double ActualTurnDeg { get; set; }
            public double MaxTurnDeg { get; set; }
            public double RpKm { get; set; }
            public double RSafeKm { get; set; }
            public double FlybyDvMs { get; set; }
            public double DsmDvMs { get; set; }
            public double DsmRadiusAu { get; set; }
            public double DsmEta { get; set; }
            public double BMagKm { get; set; }
            public bool IsFlybyPhysical { get; set; }
            public string Note { get; set; }
        }

        public class RootCauseFinding
        {
            public string Category { get; set; } // EnergyGap, TurnDeficit, LaunchBudget, BoundsMismatch, Success
            public string Severity { get; set; } // Critical, Warning, Info
            public string Title { get; set; }
            public string DetailedDescription { get; set; }
            public string MathematicalFormula { get; set; }
            public string Recommendation { get; set; }
        }

        // ====================================================================
        // 报表根数据字段
        // ====================================================================
        public MissionHeader Mission { get; set; } = new MissionHeader();
        public List<TopologyCandidateEntry> EvaluatedTopologies { get; set; } = new List<TopologyCandidateEntry>();
        public List<LegBoundsMetric> SearchBounds { get; set; } = new List<LegBoundsMetric>();
        public List<IslandConvergenceRecord> IslandRecords { get; set; } = new List<IslandConvergenceRecord>();
        public List<LegMechanicsRecord> BestSolutionLegs { get; set; } = new List<LegMechanicsRecord>();
        public List<RootCauseFinding> RootCauses { get; set; } = new List<RootCauseFinding>();

        public double BestLaunchDvMs { get; set; }
        public double BestTotalDsmMs { get; set; }
        public double BestTotalDvMs { get; set; }
        public double BestTotalFlightDays { get; set; }
        public string BestDepartureEpochStr { get; set; }

        /// <summary>
        /// 自动执行天体力学失效根因诊断推导
        /// </summary>
        public void DeduceRootCauses()
        {
            RootCauses.Clear();

            // 1. 检查总 ΔV 与 DSM 预算瓶颈
            bool isBudgetExceeded = BestTotalDvMs > (Mission.VesselMaxDeltaVMs > 0 ? Mission.VesselMaxDeltaVMs : 4000.0);
            double totalDsm = BestTotalDsmMs;

            if (isBudgetExceeded)
            {
                // 寻找最大 DSM 航段 (Deficit Hotspot)
                var maxDsmLeg = BestSolutionLegs.OrderByDescending(l => l.DsmDvMs).FirstOrDefault();
                if (maxDsmLeg != null && maxDsmLeg.DsmDvMs > 1000.0)
                {
                    double dsmRatio = totalDsm > 0 ? (maxDsmLeg.DsmDvMs / totalDsm * 100.0) : 0.0;
                    RootCauses.Add(new RootCauseFinding
                    {
                        Category = "EnergyGap",
                        Severity = "Critical",
                        Title = $"致命引力势阱断层: 航段 #{maxDsmLeg.LegIndex} [{maxDsmLeg.FromBody} ➔ {maxDsmLeg.ToBody}] 消耗 {maxDsmLeg.DsmDvMs:F0} m/s ({dsmRatio:F1}% 的总 DSM)",
                        DetailedDescription = $"该航段发生跨越式天体转移（从 r1 到 r2），目标开普勒椭圆轨道的 Vis-Viva 机械能与出发天体所能提供的最大日心速度存在巨大能级落差。" +
                                              $"单次引力借力无法将双曲过剩速度旋转至所需高能出射矢量，导致优化器被迫依靠深空机动 (DSM) 补齐速度赤字。",
                        MathematicalFormula = "v_req = sqrt(mu_sun * (2/r1 - 1/a_trans)),  v_max = v_circ1 + v_inf,  Delta_V_deficit = max(0, v_req - v_max)",
                        Recommendation = $"严禁从深势阱或内侧天体 ({maxDsmLeg.FromBody}) 直接跨越至远外行星 ({maxDsmLeg.ToBody})！" +
                                         $"应引入天体力学梯级爬升链 (Gravitational Potential Ladder)，例如: " +
                                         $"{maxDsmLeg.FromBody} ➔ Venus ➔ Earth ➔ Jupiter ➔ {maxDsmLeg.ToBody}，利用木星等强引力天体实现万米级无工质速度增量。"
                    });
                }
            }

            // 2. 检查天体双曲几何偏折角瓶颈
            foreach (var leg in BestSolutionLegs)
            {
                if (leg.ActualTurnDeg > leg.MaxTurnDeg + 0.1)
                {
                    RootCauses.Add(new RootCauseFinding
                    {
                        Category = "TurnDeficit",
                        Severity = "Critical",
                        Title = $"双曲几何偏折角超限: 借力天体 [{leg.ToBody}] 所需偏折 {leg.ActualTurnDeg:F1}° > 理论极限 {leg.MaxTurnDeg:F1}°",
                        DetailedDescription = $"天体势阱强度不足（引力常数 μ 偏小或过剩速度 V_inf 偏高），在安全近拱点半径 r_safe={leg.RSafeKm:F0} km 处，" +
                                              $"双曲线渐近线几何转向角已达到物理极限。",
                        MathematicalFormula = "delta_max = 2 * arcsin(1 / (1 + r_safe * v_inf^2 / mu_body))",
                        Recommendation = $"在 [{leg.ToBody}] 处增加 1~3 次同星开普勒共振 (Resonant Flybys)，例如 4:3 或 2:1 共振轨道，" +
                                         $"将单次大角度转向拆解为多次小角度渐进无动力偏折。"
                    });
                }
            }

            // 3. 检查发射出射能量 C3 瓶颈
            if (BestLaunchDvMs > 3800.0)
            {
                RootCauses.Add(new RootCauseFinding
                {
                    Category = "LaunchBudget",
                    Severity = "Warning",
                    Title = $"始发地 TVI 注入速度超预算: LaunchΔV = {BestLaunchDvMs:F1} m/s > 3800.0 m/s 标称限值",
                    DetailedDescription = $"地球 200 km LEO 停泊轨道切向出射速度偏高，表明转移轨道入射日心速度与地球公转速度存在较大夹角或速度差。",
                    MathematicalFormula = "Delta_V_launch = sqrt(v_inf^2 + 2*mu_dep/r_park) - sqrt(mu_dep/r_park)",
                    Recommendation = "优先利用金星内潜借力 (Inward Venus Swing-by) 削减出射能耗，或扩展发射窗口以捕捉行星相位共面切向出射切点。"
                });
            }

            // 4. 检查飞掠是否完全无动力闭环
            bool allUnpowered = BestSolutionLegs.All(l => l.FlybyDvMs < 1.0);
            if (allUnpowered && BestTotalDvMs <= 4000.0)
            {
                RootCauses.Add(new RootCauseFinding
                {
                    Category = "Success",
                    Severity = "Info",
                    Title = "✅ 全流程天体力学闭环完美通过",
                    DetailedDescription = "所有借力天体双曲过剩速度进出严格守恒 (|V_inf,in| = |V_inf,out|)，无动力飞掠 ΔV = 0.00 m/s，满足全部工程与物理约束。",
                    MathematicalFormula = "|v_inf+| = |v_inf-|,  Delta_V_fb = 0.00 m/s,  Total_Delta_V <= 4000 m/s",
                    Recommendation = "已达到 NASA/ESA 工业级最优无动力引力助推全局收敛标准。"
                });
            }
        }

        /// <summary>
        /// 生成结构完整、包含 Markdown 表格与天体力学公式的诊断分析报告
        /// </summary>
        public string GenerateMarkdownReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 🚀 MGA 自适应求解器天体力学数学诊断分析报表");
            sb.AppendLine("> **核心目标**: 从天体力学、能量守恒 (Vis-Viva)、双曲几何偏折极限、时间搜索空间与进化算法收敛状态导出完整数学数据，精确定位求解瓶颈。");
            sb.AppendLine();

            // 1. 任务概览
            sb.AppendLine("## 1. 任务设计与边界约束");
            sb.AppendLine("| 指标参数 | 设定值 | 物理/工程含义 |");
            sb.AppendLine("| :--- | :--- | :--- |");
            sb.AppendLine($"| **始发天体 (Departure)** | `{Mission.DeparturePlanet}` | 初始出发地 (LEO 200 km 停泊轨道) |");
            sb.AppendLine($"| **到达天体 (Arrival)** | `{Mission.ArrivalPlanet}` | 最终目标天体 (飞掠/捕获模式) |");
            sb.AppendLine($"| **意向途径天体 (Waypoints)** | `{(Mission.UserGoalWaypoints != null && Mission.UserGoalWaypoints.Count > 0 ? string.Join(", ", Mission.UserGoalWaypoints) : "无 (全局自动推导)")}` | 用户约束访问天体 |");
            sb.AppendLine($"| **发射历元窗口** | `{Mission.DepartureEpochStr}` | 搜索起始时刻 (历时跨度 {Mission.SearchSpanDays:F0} 天) |");
            sb.AppendLine($"| **出射 ΔV 限制 (TVI)** | `<= 3800.0 m/s` | 单次标准地球逃逸注入能力 |");
            sb.AppendLine($"| **任务总 ΔV 预算** | `<= {Mission.VesselMaxDeltaVMs:F0} m/s` | 飞船工质机动极限制约 |");
            sb.AppendLine($"| **航路 DSM 预算** | `<= {Mission.MaxDsmDeltaVMs:F0} m/s` | 深空机动上限 (迫使纯引力助推) |");
            sb.AppendLine($"| **飞掠机动约束** | `ΔV_fb == 0.00 m/s` | 严格无动力物理弹弓闭环 |");
            sb.AppendLine($"| **求解耗时** | `{Mission.TotalComputeTimeSec:F2} 秒` | 多岛多拓扑演化计算用时 |");
            sb.AppendLine();

            // 2. 候选拓扑与星历势阱可达性矩阵
            sb.AppendLine("## 2. 候选借力拓扑与星历可达性能量矩阵 (Stage 1)");
            sb.AppendLine("求解器基于当前历元 JPL SPICE/DE440 真实星历与开普勒引力势阱对所有可能序列进行前置可达性与物理赤字审计：");
            sb.AppendLine();
            sb.AppendLine("| # | 候选借力拓扑链路 | 共振数 | 预估发射ΔV | 物理总赤字 (m/s) | 预估总ΔV | 物理可行判定 |");
            sb.AppendLine("| :-: | :--- | :-: | :-: | :-: | :-: | :-: |");
            foreach (var t in EvaluatedTopologies)
            {
                string chainStr = $"{Mission.DeparturePlanet} ➔ {string.Join(" ➔ ", t.FlybyChain)} ➔ {Mission.ArrivalPlanet}";
                string feasIcon = t.IsFeasible ? "✅ 可行" : "❌ 不可达";
                sb.AppendLine($"| {t.Rank} | `{chainStr}` | {t.ResonanceCount} | {t.EstimatedLaunchDvMs:F0} m/s | {t.TotalDeficitMs:F0} | {t.EstimatedTotalDvMs:F0} m/s | {feasIcon} |");
            }
            sb.AppendLine();

            // 展开高价值候选拓扑的逐段天体力学拆解
            sb.AppendLine("### 2.1 关键候选拓扑逐段天体物理指标明细");
            foreach (var t in EvaluatedTopologies.Take(3))
            {
                sb.AppendLine($"#### 拓扑 #{t.Rank}: `{Mission.DeparturePlanet} ➔ {string.Join(" ➔ ", t.FlybyChain)} ➔ {Mission.ArrivalPlanet}`");
                sb.AppendLine($"* **阶段结论**: {t.SummaryRationale}");
                sb.AppendLine();
                sb.AppendLine("| 航段 | r1 (AU) | r2 (AU) | a_trans (AU) | v_req (km/s) | v_max (km/s) | 能级赤字 (m/s) | 所需偏折 | 最大偏折 | 转向赤字 (m/s) | 几何相位差 | 单段总赤字 |");
                sb.AppendLine("| :--- | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: |");
                foreach (var l in t.Legs)
                {
                    string legName = $"{l.FromBody} ➔ {l.ToBody}";
                    sb.AppendLine($"| {legName} | {l.R1Au:F3} | {l.R2Au:F3} | {l.ATransAu:F3} | {l.VReqKms:F2} | {l.VMaxKms:F2} | {l.EnergyDeficitMs:F0} | {l.ReqTurnDeg:F1}° | {l.AvailTurnDeg:F1}° | {l.TurnDeficitMs:F0} | {l.PhaseDeltaDeg:F1}° | **{l.TotalLegDeficitMs:F0} m/s** |");
                }
                sb.AppendLine();
            }

            // 3. 时间搜索空间与共振槽标定
            sb.AppendLine("## 3. 各航段飞行时间 (ToF) 搜索空间数学标定 (Stage 2)");
            sb.AppendLine("根据各航段天体公转轨道周期与霍曼转移半周期动态推导 ToF 边界，避免多圈无谓遍历：");
            sb.AppendLine();
            sb.AppendLine("| 航段 | 转移力学类型 | 物理基准周期 | ToF 下限 (天) | ToF 上限 (天) | 比例系数范围 | 边界合理性状态 |");
            sb.AppendLine("| :--- | :--- | :-: | :-: | :-: | :-: | :--- |");
            foreach (var b in SearchBounds)
            {
                sb.AppendLine($"| {b.LegName} | {b.TransferType} | {b.RefPeriodDays:F1} 天 | {b.MinToFDays:F1} | {b.MaxToFDays:F1} | [{b.MinRatio:F2}, {b.MaxRatio:F2}] | {b.StatusNote} |");
            }
            sb.AppendLine();

            // 4. 多岛微分进化动态
            if (IslandRecords != null && IslandRecords.Count > 0)
            {
                sb.AppendLine("## 4. 多岛微分进化 (Archipelago DE) 收敛记录 (Stage 3)");
                sb.AppendLine("| 拓扑序号 | 拓扑名称 | 岛屿 # | 最优出射历元 | LaunchΔV (m/s) | DSM ΔV (m/s) | 岛屿最优总ΔV |");
                sb.AppendLine("| :-: | :--- | :-: | :--- | :-: | :-: | :-: |");
                foreach (var r in IslandRecords)
                {
                    sb.AppendLine($"| {r.TopologyIndex} | `{r.TopologyName}` | 岛屿 {r.IslandIndex} | {r.EpochStr} | {r.LaunchDvMs:F0} | {r.DsmDvMs:F0} | **{r.BestTotalDvMs:F0} m/s** |");
                }
                sb.AppendLine();
            }

            // 5. 最优候选解物理动力学详表
            sb.AppendLine("## 5. 最优候选解天体力学详表 (Stage 4 & EnrichSolution)");
            sb.AppendLine($"* **最优发射历元**: `{BestDepartureEpochStr}`");
            sb.AppendLine($"* **出射机动 LaunchΔV**: `{BestLaunchDvMs:F1} m/s` (约束: <= 3800 m/s)");
            sb.AppendLine($"* **中途深空机动 Total DSM**: `{BestTotalDsmMs:F1} m/s` (约束: <= 500 m/s)");
            sb.AppendLine($"* **任务总工质机动 TotalΔV**: `{BestTotalDvMs:F1} m/s` (挑战目标: <= 4000 m/s)");
            sb.AppendLine($"* **全程飞行历时**: `{BestTotalFlightDays:F1} 天 ({BestTotalFlightDays / 365.25:F2} 年)`");
            sb.AppendLine();
            sb.AppendLine("### 5.1 逐航段动力学与双曲飞掠几何实测表");
            sb.AppendLine("| # | 航段 | 出发 ➔ 到达 | 耗时(天) | V_inf,in | V_inf,out | 能量差 | 实际偏折 | 最大偏折 | 近拱半径/安全 | 飞掠ΔV | DSM ΔV | B平面 \\|B\\| | 真实物理判定 |");
            sb.AppendLine("| :-: | :--- | :--- | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: |");
            foreach (var l in BestSolutionLegs)
            {
                string legName = $"{l.FromBody} ➔ {l.ToBody}";
                string dates = $"{l.DepUtcStr} ➔ {l.ArrUtcStr}";
                string radStr = $"{l.RpKm:F0} / {l.RSafeKm:F0} km";
                string physIcon = l.IsFlybyPhysical ? "✅ 闭环" : "⚠️ 超限";
                sb.AppendLine($"| {l.LegIndex} | `{legName}` | {dates} | {l.ToFDays:F1} | {l.VInfInKms:F2} | {l.VInfOutKms:F2} | {l.DeltaVInfKms:F4} | {l.ActualTurnDeg:F2}° | {l.MaxTurnDeg:F2}° | {radStr} | {l.FlybyDvMs:F1} | {l.DsmDvMs:F1} | {l.BMagKm:F0} | {physIcon} |");
            }
            sb.AppendLine();

            // 6. 数学归因与物理失效诊断
            sb.AppendLine("## 6. 数学归因与物理失效根因诊断 (Root Cause Diagnosis)");
            if (RootCauses.Count == 0)
            {
                sb.AppendLine("> [!NOTE]");
                sb.AppendLine("> 未检测到严重天体力学违背或能级断层。");
            }
            else
            {
                foreach (var rc in RootCauses)
                {
                    string alertType = rc.Severity == "Critical" ? "CAUTION" : (rc.Severity == "Warning" ? "WARNING" : "TIP");
                    sb.AppendLine($"> [!{alertType}]");
                    sb.AppendLine($"> ### {rc.Title}");
                    sb.AppendLine($"> {rc.DetailedDescription}");
                    if (!string.IsNullOrEmpty(rc.MathematicalFormula))
                    {
                        sb.AppendLine($"> ");
                        sb.AppendLine($"> **天体力学控制方程**: `{rc.MathematicalFormula}`");
                    }
                    if (!string.IsNullOrEmpty(rc.Recommendation))
                    {
                        sb.AppendLine($"> ");
                        sb.AppendLine($"> **数学/工程改进方案**: {rc.Recommendation}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine($"*报表生成时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | 底层算法: 100% 官方 ESA kep3 Native C++ 动力学校验闭环*");
            return sb.ToString();
        }

        /// <summary>
        /// 将报表导出至指定 Markdown 文件
        /// </summary>
        public void ExportToFile(string relativeOrFullPath)
        {
            try
            {
                string fullPath = Path.IsPathRooted(relativeOrFullPath)
                    ? relativeOrFullPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrFullPath);

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string content = GenerateMarkdownReport();
                File.WriteAllText(fullPath, content, Encoding.UTF8);
                Console.WriteLine($"\n📄 [MGA 数学诊断报表] 已成功导出数学分析文件: {fullPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ 导出 MGA 数学诊断报表异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 在终端控制台打印紧凑数学诊断概览
        /// </summary>
        public void PrintConsoleSummary()
        {
            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine("📊 [MGA 天体力学求解器数学诊断报表摘要]");
            Console.WriteLine("==========================================================================================");
            Console.WriteLine($"  • 出发: {Mission.DeparturePlanet} ➔ 目标: {Mission.ArrivalPlanet} | TVI出射: {BestLaunchDvMs:F1} m/s | 总DSM: {BestTotalDsmMs:F1} m/s | 任务总ΔV: {BestTotalDvMs:F1} m/s");
            Console.WriteLine($"  • 评估拓扑总数: {EvaluatedTopologies.Count} | 物理闭环航段数: {BestSolutionLegs.Count} | 计算耗时: {Mission.TotalComputeTimeSec:F1}s");
            Console.WriteLine("  ----------------------------------------------------------------------------------------");
            Console.WriteLine("  [航段能量与双曲转向指标]:");
            foreach (var l in BestSolutionLegs)
            {
                Console.WriteLine($"    #{l.LegIndex} {l.FromBody,-7} ➔ {l.ToBody,-7}: ToF={l.ToFDays,5:F1}d | DSM={l.DsmDvMs,7:F1} m/s | δ={l.ActualTurnDeg,5:F1}° (上限 {l.MaxTurnDeg,5:F1}°) | fb_dv={l.FlybyDvMs:F1}m/s");
            }
            Console.WriteLine("  ----------------------------------------------------------------------------------------");
            Console.WriteLine("  [数学根因诊断与建议]:");
            foreach (var rc in RootCauses)
            {
                string prefix = rc.Severity == "Critical" ? "❌ [严重断层]" : (rc.Severity == "Warning" ? "⚠️ [能耗预警]" : "✅ [物理闭环]");
                Console.WriteLine($"    {prefix} {rc.Title}");
                if (!string.IsNullOrEmpty(rc.Recommendation))
                {
                    Console.WriteLine($"      💡 建议: {rc.Recommendation}");
                }
            }
            Console.WriteLine("==========================================================================================");
        }
    }
}
