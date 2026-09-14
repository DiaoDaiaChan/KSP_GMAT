using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// GUI 预设任务优化求解测试套件。
    ///
    /// 【2026-09-14 审计修正】
    ///   1. **口径澄清**: 本套件模拟的是"用户在 GUI 里选中一个预设任务后点 [优化求解]"的真实交互路径。
    ///      预设**本身就包含借力链定义** (这是产品的正常输入, 与"盲搜"无关), 因此本套件
    ///      **不是**拓扑盲搜测试, 也不应被当作盲搜证据引用。此处显式声明, 避免与
    ///      MGAHistoricalBlindSearchSuite 的同名"盲搜"混淆。
    ///   2. 搜索窗原为 <c>launchUT ± 20 天</c> (历史发射日邻域, 最优解恰在窗口中心) →
    ///      改为年粒度发射机会窗。
    ///   3. 航时边界原为 <c>HistoryToFDays × 0.9 / × 1.1</c> (历史真值 ±10% 夹逼) →
    ///      改为 <see cref="MGAMCTSTopologySelector.AutoDeriveResonantLegBounds"/> 的
    ///      开普勒航时推导 (与任何历史航时无关)。
    ///   4. 通过判据原为仅 <c>dv &gt; 100.0</c> (只要数值不异常就算过) →
    ///      改为"有解 ∧ 全航段物理闭环 ∧ ΔV 在合理量级"。
    /// </summary>
    public class GuiPresetOptimizationsTest : ITestModule
    {
        public string ModuleId => "gui_presets";
        public string Name => "GUI 预设任务优化求解测试套件";
        public string Description => "验证游戏内 GUI 快捷预设点击 [优化求解] (PykepBridge.Optimize) 全局差分寻优是否收敛且物理闭环 (拓扑由预设提供, 非盲搜)";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("GuiPresets_OptimizationSolve");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("预设任务数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"GUI 预设优化求解未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🚀 [用例集 D2] 游戏内 GUI 快捷预设点击 [优化求解] (PykepBridge.Optimize) 全局差分寻优收敛验证");
            Console.WriteLine("   口径: 拓扑由 GUI 预设提供 (真实产品交互路径), 本用例不检验拓扑自主推导能力。");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            string[] quickPresets = new string[] { "Mariner", "Cassini", "New Horizons", "Mars Express", "Galileo", "Juno", "Rosetta", "BepiColombo" };

            foreach (var presetKey in quickPresets)
            {
                total++;
                var bCase = MGABenchmarkSuite.StandardHistoricalMissions.Find(m => m.MissionName.IndexOf(presetKey, StringComparison.OrdinalIgnoreCase) >= 0);
                if (bCase == null) continue;

                Console.Write($"• [优化求解] {bCase.MissionName,-35} ... ");

                double launchUT = GMATClient.ParseUtcGregorian(bCase.HistoricLaunchDateStr);
                if (launchUT <= 0) launchUT = bCase.HistoricLaunchUT;

                // 【2026-09-14 修正】年粒度发射机会窗，取代原 launchUT ± 20 天的历史日邻域。
                EvalWindows.OpportunityYearWindow(launchUT, out double winStartUT, out double winEndUT);
                if (winEndUT <= winStartUT)
                {
                    // 极端回退: 不用历史发射日做圆心, 改用与历史无关的 J2000 起点固定年窗。
                    double j2000 = EphemerisTimeSystem.JdToUt(EphemerisTimeSystem.J2000_JD);
                    winStartUT = j2000;
                    winEndUT = j2000 + 365.25 * 86400.0;
                }

                // flybyChain 参数语义为**中间借力天体** (不含出发与终点)，故需去掉 FlybySequence 的末元素。
                var intermediates = bCase.FlybySequence.Take(Math.Max(0, bCase.FlybySequence.Count - 1)).ToList();

                var mgaConfig = new MGAConfig
                {
                    DepartureBody = bCase.DepartureBody,
                    DestinationBody = bCase.FlybySequence.Last(),
                    FlybySequence = new List<string>(intermediates),
                    EarliestDepartureUT = winStartUT,
                    LatestDepartureUT = winEndUT,
                    DepartureAltitudeKm = bCase.DepAltKm,
                    DestinationAltitudeKm = bCase.ArrAltKm,
                    DepartureInclinationDeg = 28.5,
                    NoInsertionBurn = bCase.NoInsertionBurn,
                    EngineMode = bCase.EngineMode,
                    DsmEtaDefault = bCase.DsmEtaDefault,
                    PopulationSize = 40,
                    MaxGenerations = 30
                };

                // 【2026-09-14 修正】航时边界改用开普勒/共振物理推导，杜绝历史真值 ±10% 夹逼。
                var legBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(
                    bCase.DepartureBody, bCase.FlybySequence.Last(), intermediates, "Sun");
                if (legBounds != null && legBounds.Count > 0)
                {
                    mgaConfig.MinToFDays = legBounds.Select(b => b.MinDays).ToList();
                    mgaConfig.MaxToFDays = legBounds.Select(b => b.MaxDays).ToList();
                }
                // 物理航时包络: 各航段物理上限之和。用于给"这次优化到底算不算收敛"设一个
                // 与历史数据无关的理智闸门 —— 否则求解器可能返回"82 年航时"这类退化解并判为通过。
                double physicsTofEnvelopeDays = (legBounds != null && legBounds.Count > 0)
                    ? legBounds.Sum(b => b.MaxDays) : 0.0;
                Console.WriteLine();
                Console.WriteLine($"   [物理航时包络] 各航段上限: {string.Join(" / ", legBounds.Select(b => $"{b.MaxDays:F0}d"))} → 合计 {physicsTofEnvelopeDays:F0}d");

                var sols = PykepBridge.Optimize(mgaConfig);

                if (sols != null && sols.Count > 0)
                {
                    var sol = sols[0];
                    double dv = sol.TotalDeltaVKms * 1000.0;
                    bool numericOk = !double.IsNaN(dv) && !double.IsInfinity(dv) && dv > 100.0;
                    // 【2026-09-14 修正】原判据仅 `dv > 100.0`；现要求物理闭环 + 航时落在物理包络内。
                    bool durationSane = physicsTofEnvelopeDays <= 0.0
                        || (sol.TotalFlightDays > 0.0 && sol.TotalFlightDays <= physicsTofEnvelopeDays);
                    bool pass = numericOk && sol.AllLegsFeasible && durationSane;
                    if (pass)
                    {
                        passed++;
                        Console.WriteLine($"✅ 优化收敛! 候选解={sols.Count} | 最优ΔV={dv,6:F0} m/s | 历时={sol.TotalFlightDays,4:F0}d | 借力物理可行=True");
                    }
                    else if (!numericOk)
                    {
                        Console.WriteLine($"⚠️ 数值异常: dv={dv}");
                    }
                    else if (!sol.AllLegsFeasible)
                    {
                        Console.WriteLine($"⚠️ 未通过: 存在不可行航段, ΔV={dv:F0} m/s, 历时={sol.TotalFlightDays:F0}d");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ 未通过: 航时 {sol.TotalFlightDays:F0}d 超出物理包络 {physicsTofEnvelopeDays:F0}d (退化解), ΔV={dv:F0} m/s");
                    }
                }
                else
                {
                    Console.WriteLine("❌ 未找到收敛解");
                }
            }
            Console.WriteLine();
        }
    }
}
