using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// 通用人类深空历史任务**拓扑盲搜**重现调试器。
    ///
    /// 【2026-09-14 审计修正 —— 本文件的自我声明与其行为曾严重不符】
    ///   原实现横幅写着 "100% 纯通用算法自适应推导，0 针对特定任务的硬编码特判代码！"，
    ///   但通过 <c>waypointIntentions: gt.ExpectedWaypoints.ToList()</c> 把**历史借力天体序列**
    ///   直接交给求解器 (下游 MGAMasterOptimizer 将其设为 FlybySequence)，实际是"给答案做题"。
    ///   同时判定为 <c>passed &gt; 0</c> (5 个任务过 1 个即 PASS)、
    ///   判据为 <c>totalDv &lt;= gt.MaxExpectedDvMs * 1.35</c>，
    ///   而 <c>gt.MaxExpectedDvMs</c> 同时又作为求解器硬预算 ⇒ 该判据**恒真**。
    ///
    ///   修正:
    ///     (1) 默认 <see cref="TopologyBlindSearch"/> = true：**不再向求解器传递任何中间借力天体**，
    ///         拓扑完全由 MCTS + 星历可达性自主推导；
    ///     (2) 通过判据改为可证伪的三合一：收敛 &amp;&amp; 全航段物理闭环 &amp;&amp; **拓扑独立重现**历史借力天体集合。
    ///         历史数据 (waypoints / ΔV / 航时) 仅用于**事后比对**，不进入求解器；
    ///     (3) PASS 判据由 <c>passed &gt; 0</c> 改为 <c>passed == total</c>；
    ///     (4) 保留 <see cref="TopologyBlindSearch"/> = false 的标定回放通道，但会明确标注为 [回放]。
    /// </summary>
    public class HistoricalMissionsDebuggerTest : ITestModule
    {
        public string ModuleId => "historical_missions_debugger";
        public string Name => "通用人类深空历史任务拓扑盲搜重现测试套件";
        public string Description => "在**不给任何中间借力天体提示**的条件下，验证纯通用算法能否自主推导出历史经典深空任务的借力构型 (Voyager 2 / Galileo / Cassini / BepiColombo / Scenario 9)";

        /// <summary>
        /// 是否执行真正的**拓扑盲搜** (默认 true)。
        ///
        /// true  : 不向求解器传递任何中间借力天体 (waypointIntentions 传空)，
        ///         求解器须自主推导出包含历史借力天体的拓扑 —— 这才是本套件名字所声称的能力。
        /// false : 历史轨迹复现标定通道 (把历史 waypoints 交给求解器)，仅用于星历/链路诊断，
        ///         结果在报告中标注为 [回放]，**不得**作为算法能力证据。
        /// </summary>
        public static bool TopologyBlindSearch { get; set; } = true;

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("HistoricalMissions_TopologyBlindSearch");
            int passed = RunHistoricalMissionsDebugger(out int total);
            // 【2026-09-14 修正】原为 passed > 0 —— 5 个任务过 1 个即 PASS，属恒真式。
            bool ok = total > 0 && passed == total;
            ctx.RecordMetric("任务总数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"历史任务拓扑盲搜未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        /// <summary>
        /// 历史任务真值参考表。
        ///
        /// 【用途边界】本表全部字段**仅用于事后比对与结果标注**，严禁作为求解器输入：
        ///   - <c>ExpectedWaypoints</c> : 用于判定"算法是否独立重现历史借力构型"，不传给求解器；
        ///   - <c>MaxExpectedDvMs</c>    : 作为航天器**工质预算** (真实的工程约束) 传入求解器；
        ///                                同时也作为报告中的 ΔV 对比基准。判定不依赖它 (避免恒真)。
        /// </summary>
        public static class HistoryMissionGroundTruth
        {
            public static readonly (string Mission, string Departure, string Arrival, string[] ExpectedWaypoints, string LaunchWindowStr, double WindowSpanDays, double MaxExpectedDvMs, MGAPreset Preset)[] GroundTruths = new[] {
                ("Voyager 2 (1977 Grand Tour)", "Earth", "Neptune", new[] { "Jupiter", "Saturn", "Uranus" }, "01 Aug 1977 00:00:00.000", 120.0, 7500.0, MGAPreset.GrandTour),
                ("Galileo (1989 VEEGA)", "Earth", "Jupiter", new[] { "Venus", "Earth", "Earth" }, "18 Oct 1989 00:00:00.000", 120.0, 4500.0, MGAPreset.StandardEngineering),
                ("Cassini-Huygens (1997 VVEJPS)", "Earth", "Saturn", new[] { "Venus", "Venus", "Earth", "Jupiter" }, "15 Oct 1997 00:00:00.000", 120.0, 8500.0, MGAPreset.StandardEngineering),
                ("BepiColombo (2018 EVVM)", "Earth", "Mercury", new[] { "Venus" }, "20 Oct 2018 00:00:00.000", 120.0, 4500.0, MGAPreset.InnerSystemResonantDeceleration),
                ("Scenario 9 (Mercury-Neptune Challenge)", "Earth", "Neptune", new[] { "Mercury" }, "01 Jan 1951 00:00:00.000", 800.0, 4000.0, MGAPreset.ExtremeDeepSpaceChallenge)
            };
        }

        public static int RunHistoricalMissionsDebugger(out int total)
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🏛️  [MGAHistoricalMissionDebugger] 通用人类深空历史任务拓扑盲搜重现调试器");
            Console.WriteLine(TopologyBlindSearch
                ? "   原则: 100% 纯通用算法自适应推导 —— 【不传递任何中间借力天体提示】, 拓扑由 MCTS 自主涌现！"
                : "   ⚠ 当前处于【历史轨迹复现标定】通道 (已注入历史 waypoints), 结果不构成算法能力证据！");
            Console.WriteLine("==========================================================================================\n");

            var masterOpt = new MGAMasterOptimizer();
            total = HistoryMissionGroundTruth.GroundTruths.Length;
            int passed = 0;

            foreach (var gt in HistoryMissionGroundTruth.GroundTruths)
            {
                Console.WriteLine($"\n--- [历史任务{(TopologyBlindSearch ? "拓扑盲搜" : "回放标定")}] {gt.Mission} ---");
                Console.WriteLine($"  • 目标: {gt.Departure} ➔ {(TopologyBlindSearch ? "(拓扑未知, 算法自主推导)" : string.Join(" ➔ ", gt.ExpectedWaypoints))} ➔ {gt.Arrival}");
                Console.WriteLine($"  • 参考发射机会年: {gt.LaunchWindowStr} (仅取年份构造机会窗), 工质预算: {gt.MaxExpectedDvMs:F0} m/s");
                if (TopologyBlindSearch)
                {
                    Console.WriteLine($"  • 事后比对目标 (历史借力天体, **不传给求解器**): {string.Join(", ", gt.ExpectedWaypoints)}");
                }

                // 发射机会年份年粒度窗口 (不泄露精确历史发射日)
                double anchorUT = GMATClient.ParseUtcGregorian(gt.LaunchWindowStr);
                EvalWindows.OpportunityYearWindow(anchorUT, out double startUT, out double endUT);
                if (endUT <= startUT) { startUT = anchorUT; endUT = anchorUT + gt.WindowSpanDays * 86400.0; }

                var hp = MGAHighValueParameters.CreatePreset(gt.Preset);
                hp.MaxVesselDeltaVMs = gt.MaxExpectedDvMs;   // 任务工质预算 (真实工程约束)
                hp.CandidateTopologyPoolSize = 6;
                hp.PopulationPerIsland = 60;
                hp.MaxGenerations = 80;

                // 【2026-09-14 修正】拓扑盲搜时不传递任何中间借力天体。
                var waypoints = TopologyBlindSearch ? new List<string>() : gt.ExpectedWaypoints.ToList();

                var res = masterOpt.OptimizeMission(
                    departureBody: gt.Departure,
                    arrivalBody: gt.Arrival,
                    waypointIntentions: waypoints,
                    searchStartUT: startUT,
                    searchEndUT: endUT,
                    parameters: hp
                );

                if (res.Success && res.BestSolution != null)
                {
                    var best = res.BestSolution;
                    double totalDv = best.TotalDeltaVKms * 1000.0;
                    string topoStr = string.Join(" ➔ ", res.BestTopology);
                    bool rediscovered = EvalWindows.TopologyContainsAll(res.BestTopology, gt.ExpectedWaypoints);

                    Console.WriteLine($"  ★ 算法自主推导拓扑: {topoStr}");
                    Console.WriteLine($"    • LaunchΔV: {best.LaunchDeltaVKms * 1000.0:F1} m/s, TotalΔV: {totalDv:F1} m/s, 历时: {best.TotalFlightDays:F1} 天");
                    Console.WriteLine($"    • 物理闭环: {best.AllLegsFeasible} | 工质预算 {gt.MaxExpectedDvMs:F0} m/s → 占比 {totalDv / gt.MaxExpectedDvMs * 100.0:F1}%");
                    Console.WriteLine($"    • 历史借力天体独立重现: {(rediscovered ? "✅ 是" : "❌ 否")} (目标 {string.Join("+", gt.ExpectedWaypoints)})");

                    // 【可证伪判据】收敛 + 全航段物理闭环 + 拓扑独立重现历史借力天体。
                    // 不再使用 "totalDv <= MaxExpectedDvMs * 1.35" —— 该判据因预算即取自同一数值而恒真。
                    if (best.AllLegsFeasible && rediscovered)
                    {
                        passed++;
                        Console.WriteLine($"    ✅ 判据通过: 物理闭环且独立重现历史借力构型");
                    }
                    else if (TopologyBlindSearch)
                    {
                        Console.WriteLine($"    ⚠️ 判据未通过: " +
                            (!best.AllLegsFeasible ? "存在不可行航段" : $"未重现历史借力天体 ({topoStr})"));
                    }
                    else
                    {
                        // 回放通道不参与判据 (输入即答案, 无信息量)
                        Console.WriteLine($"    ℹ️ [回放] 通道不参与能力判定 (输入已含答案)");
                    }
                }
                else
                {
                    Console.WriteLine($"    ⚠️ 该窗口下未收敛或超时: {res.Message}");
                }
            }

            Console.WriteLine($"\n🏛️ 历史任务拓扑盲搜重现运行完毕: {passed}/{total} 任务通过 (收敛 ∧ 物理闭环 ∧ 独立重现历史借力构型)！\n");
            return passed;
        }
    }
}
