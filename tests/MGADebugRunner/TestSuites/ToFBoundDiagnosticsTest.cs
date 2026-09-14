using System;
using System.Collections.Generic;
using System.Linq;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// 航时边界 (ToF Bounds) 推导的**可证伪**诊断探针。
    ///
    /// 【2026-09-14 新增 —— 用于量化"航时包络不自洽"这一原始代码缺陷】
    ///
    /// 背景: `MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds` 的航时上界实现为
    ///   `maxDays = max(minDays + 10, tHohmannDays * 3.5)` (外太阳系分支另有条件性年上限)。
    /// 其中 `×3.5 × 霍曼时间` 对短周期内行星给出合理量级, 但对木星→冥王星这类转移,
    /// 霍曼时间本身已 ~53 年, ×3.5 = 185 年 —— 求解器会在该区间内自由选择世纪尺度解,
    /// 实测已导致 GUI 预设路径产出 Cassini 30261 天 / New Horizons 115165 天的**退化解**。
    ///
    /// 本探针的判定是**可证伪**的:
    ///   PASS ⟺ 全部被检查的航段都满足
    ///          (a) `0 < minDays`, (b) `maxDays >= minDays`, (c) `maxDays <= 20 儒略年`。
    /// 条件 (c) 是**工程口径**的航时理智上界 (作一条单弧转移超过 20 年在任何任务里都无工程意义),
    /// 不是物理常数 —— 但它必须被显式声明, 而不是从 `霍曼时间 × 3.5` 里隐式得到。
    ///
    /// 该探针**不修改任何生产代码**, 只读并裁决; 修复 `DerivePhysicalInterplanetaryBounds` 后应转为 PASS。
    /// </summary>
    public class ToFBoundDiagnosticsTest : ITestModule
    {
        public string ModuleId => "tof_bounds";
        public string Name => "航时边界推导理智性诊断 (可证伪)";
        public string Description => "检查 MGAFlybyDynamicsHelper / MGAMCTSTopologySelector 推导的航段航时上下界是否存在世纪尺度荒谬值";

        /// <summary>
        /// 航时上界的工程理智上界 (儒略年)。
        /// 标注: **[工程口径, 非物理常数]** —— 单弧转移超过 20 年在任务设计上无实际意义。
        /// 该阈值在此处**显式声明**, 目的正是取代"霍曼时间 × 3.5"这种把上界藏在倍数里的隐式做法。
        /// </summary>
        private const double MaxSaneToFYears = 20.0;

        private const double DaysPerYear = 365.25;

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("ToFBounds_ReasonableSanity");

            Console.WriteLine("==========================================================================================");
            Console.WriteLine("📐 [ToFBoundDiagnostics] 航时边界推导理智性诊断 (可证伪: PASS ⟺ 无世纪尺度荒谬值)");
            Console.WriteLine($"   判定阈值: minDays > 0 且 maxDays >= minDays 且 maxDays <= {MaxSaneToFYears:F0} 儒略年");
            Console.WriteLine("==========================================================================================");

            int legTotal = 0, violatingLegs = 0, invertedLegs = 0;
            double worstYears = 0.0;
            string worstDesc = "—";
            double worstMissionSumYears = 0.0;
            string worstMissionDesc = "—";
            int refusedPairs = 0;

            void CheckLeg(string ctxDesc, double minDays, double maxDays)
            {
                legTotal++;
                if (minDays <= 0.0 || maxDays < minDays)
                {
                    invertedLegs++;
                    Console.WriteLine($"   ❌ [边界倒置/非正] {ctxDesc}: min={minDays:F1}d, max={maxDays:F1}d");
                }
                double years = maxDays / DaysPerYear;
                if (years > worstYears) { worstYears = years; worstDesc = $"{ctxDesc} (max={maxDays:F0}d)"; }
                if (years > MaxSaneToFYears)
                {
                    violatingLegs++;
                    Console.WriteLine($"   ⚠️ [世纪尺度] {ctxDesc}: max={maxDays:F0}d = {years:F1} 年 (上限 {MaxSaneToFYears:F0} 年)");
                }
            }

            // ---------------------------------------------------------------
            // A. 太阳系内全部天体对 (中段转移, 非出发段) —— 系统性扫描
            // ---------------------------------------------------------------
            Console.WriteLine("\n--- A. 太阳系全部天体对的中段转移航时上界扫描 ---");
            var planets = SolarSystemCatalog.GetAvailablePlanets()
                .Where(p => p != null && p.SMA > 0)
                .OrderBy(p => p.SMA)
                .ToList();
            double muSun = SolarSystemCatalog.SunMu;
            Console.WriteLine($"   天体数: {planets.Count}, 中央天体 μ = {muSun:E4} km³/s²");
            Console.WriteLine("   " + string.Format("{0,-14} {1,-14} {2,-12} {3,-12} {4,-10} {5,-12}",
                "出发", "到达", "minDays", "maxDays", "max(年)", "max/霍曼"));

            foreach (var from in planets)
            {
                foreach (var to in planets)
                {
                    if (ReferenceEquals(from, to)) continue;
                    var (mn, mx) = MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds(
                        from, to, muSun, false, MGAFlybyDynamicsHelper.DepartureLegMinToFFactor,
                        MGAFlybyDynamicsHelper.DepartureLegMaxToFFactor, SolarSystemCatalog.RootStarName);
                    // 参考系不一致时函数会拒绝并返回带标注的保守回退值 —— 统计之 (这是"暴露不足"的关键指标)
                    if (Math.Abs(mn - 50.0) < 1e-9 && Math.Abs(mx - 1000.0) < 1e-9) { refusedPairs++; continue; }
                    CheckLeg($"{from.Name}➔{to.Name}", mn, mx);
                    // 霍曼时间: 用原生开普勒周期给出 (不自行编公式)
                    double aTrans = (from.SMA + to.SMA) * 0.5;
                    double tHohmann = 0.5 * PykepBridge.CalcOrbitPeriodSec(aTrans, muSun) / 86400.0;
                    double ratio = tHohmann > 0 ? mx / tHohmann : 0.0;
                    if (mx / DaysPerYear > MaxSaneToFYears)
                    {
                        Console.WriteLine("   " + string.Format("{0,-14} {1,-14} {2,-12:F0} {3,-12:F0} {4,-10:F1} {5,-12:F2}",
                            from.Name, to.Name, mn, mx, mx / DaysPerYear, ratio));
                    }
                }
            }
            Console.WriteLine($"   → 因参考系不一致被**拒绝**的配对: {refusedPairs} 个 (返回保守回退 50~1000 天, 而非伪边界)");

            // ---------------------------------------------------------------
            // A3. 同星共振腿 (X→X) 的航时上界 —— 原 DerivePhysicalResonantBounds 无绝对上限
            // ---------------------------------------------------------------
            Console.WriteLine("\n--- A3. 同星共振腿 (X➔X) 航时上界 (含公转周期与阶数上界) ---");
            Console.WriteLine("   " + string.Format("{0,-14} {1,-12} {2,-14} {3,-12} {4,-10} {5,-12}",
                "天体", "公转周期(d)", "minDays", "maxDays", "max(年)", "max/周期"));
            foreach (var p in planets)
            {
                if (p.SMA <= 0 || p.IsMoon && p.ParentName != SolarSystemCatalog.RootStarName) continue;
                double periodDays = PykepBridge.CalcOrbitPeriodSec(p.SMA, muSun) / 86400.0;
                var (mn, mx, _, isFeas) = MGAFlybyDynamicsHelper.DerivePhysicalResonantBounds(p, null, muSun, 0.0, SolarSystemCatalog.RootStarName);
                if (double.IsNaN(mx)) continue;
                if (!isFeas)
                {
                    Console.WriteLine("   " + string.Format("{0,-14} {1,-12:F0} {2,-14:F0} {3,-12:F0} {4,-10} {5,-12}",
                        p.Name, periodDays, mn, mx, "不可行", "物理超限剪枝"));
                    continue;
                }
                CheckLeg($"{p.Name}➔{p.Name}(共振)", mn, mx);
                if (mx / DaysPerYear > MaxSaneToFYears || mn / DaysPerYear > MaxSaneToFYears)
                {
                    Console.WriteLine("   " + string.Format("{0,-14} {1,-12:F0} {2,-14:F0} {3,-12:F0} {4,-10:F1} {5,-12:F2}",
                        p.Name, periodDays, mn, mx, mx / DaysPerYear, periodDays > 0 ? mx / periodDays : 0.0));
                }
            }

            // ---------------------------------------------------------------
            // A2. 以母体为中央天体的系统内转移 (地月 / 木卫 / 土卫) —— 原实现在此产生 53.8 年
            // ---------------------------------------------------------------
            Console.WriteLine("\n--- A2. 系统内转移 (中央天体 = 母体) 航时上界 ---");
            Console.WriteLine("   " + string.Format("{0,-40} {1,-12} {2,-12} {3,-10}", "航段", "minDays", "maxDays", "max(年)"));
            foreach (var parent in planets)
            {
                var moons = SolarSystemCatalog.GetMoonsOf(parent.Name);
                if (moons == null || moons.Count == 0) continue;
                double muP = parent.Mu;
                if (muP <= 0) continue;
                foreach (var moon in moons)
                {
                    if (moon == null || moon.SMA <= 0) continue;
                    // 母体 → 卫星 (出发段)
                    var (mn1, mx1) = MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds(
                        parent, moon, muP, true, MGAFlybyDynamicsHelper.DepartureLegMinToFFactor,
                        MGAFlybyDynamicsHelper.DepartureLegMaxToFFactor, parent.Name);
                    CheckLeg($"{parent.Name}➔{moon.Name}(系统内)", mn1, mx1);
                    Console.WriteLine("   " + string.Format("{0,-40} {1,-12:F1} {2,-12:F1} {3,-10:F2}",
                        $"{parent.Name}➔{moon.Name}", mn1, mx1, mx1 / DaysPerYear));
                }
            }

            // ---------------------------------------------------------------
            // B. 22 个历史基准任务的逐段边界 + 包络合计 (即被测系统实际会用的值)
            // ---------------------------------------------------------------
            Console.WriteLine("\n--- B. StandardHistoricalMissions 逐任务逐段航时边界 ---");
            Console.WriteLine("   " + string.Format("{0,-42} {1,-10} {2,-12} {3,-14} {4,-8} {5,-12}", "任务", "航段数", "包络合计(d)", "包络合计(年)", "合计判定", "单段最长(年)"));
            foreach (var c in MGABenchmarkSuite.StandardHistoricalMissions)
            {
                var intermediates = c.FlybySequence.Take(Math.Max(0, c.FlybySequence.Count - 1)).ToList();
                // 使用与实际求解器一致的中央天体 (原探针写死 "Sun" 对地月任务不成立)
                string central = SolarSystemCatalog.ResolveTransferCentralBody(c.DepartureBody, c.FlybySequence.Last());
                if (string.IsNullOrEmpty(central)) central = "Sun";
                var bounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(
                    c.DepartureBody, c.FlybySequence.Last(), intermediates, central);
                double sum = 0.0;
                double legWorstYears = 0.0;
                for (int i = 0; i < bounds.Count; i++)
                {
                    CheckLeg($"{c.MissionName} leg#{i + 1}", bounds[i].MinDays, bounds[i].MaxDays);
                    sum += bounds[i].MaxDays;
                    legWorstYears = Math.Max(legWorstYears, bounds[i].MaxDays / DaysPerYear);
                }
                // 判定按**单段** (这才是 DerivePhysicalInterplanetaryBounds 的契约范围);
                // 总航时包络只作为**不掩盖的指标**打印 —— 它应由任务级 TotalToFMaxDays 约束,
                // 不由单段包络负责 (见修复记录遗留项)。
                double sumYears = sum / DaysPerYear;
                string note = sumYears > MaxSaneToFYears ? "⚠️ 合计偏大" : "";
                if (sumYears > worstMissionSumYears) { worstMissionSumYears = sumYears; worstMissionDesc = c.MissionName; }
                Console.WriteLine("   " + string.Format("{0,-42} {1,-10} {2,-12:F0} {3,-14:F1} {4,-8} {5,-12:F1}",
                    c.MissionName.Length > 42 ? c.MissionName.Substring(0, 42) : c.MissionName,
                    bounds.Count, sum, sumYears, note, legWorstYears));
            }

            // ---------------------------------------------------------------
            // 裁决
            // ---------------------------------------------------------------
            bool pass = invertedLegs == 0 && violatingLegs == 0;
            ctx.RecordMetric("受检航段总数", legTotal);
            ctx.RecordMetric("世纪尺度航段", violatingLegs);
            ctx.RecordMetric("边界倒置航段", invertedLegs);
            ctx.RecordMetric("最长航时(年)", $"{worstYears:F1}");
            ctx.RecordMetric("最长航段", worstDesc);

            Console.WriteLine("\n==========================================================================================");
            Console.WriteLine($"📊 【单段判据】受检航段 {legTotal} 个 | 世纪尺度 (> {MaxSaneToFYears:F0} 年) {violatingLegs} 个 | 边界倒置 {invertedLegs} 个");
            Console.WriteLine($"   最长单段航时上界: {worstYears:F1} 年 — {worstDesc}");
            Console.WriteLine($"📊 【不掩盖的指标】任务级包络合计最大: {worstMissionSumYears:F1} 年 — {worstMissionDesc}");
            Console.WriteLine("   (任务级总航时上界应由 TotalToFMaxDays 约束, 不由单段包络负责 —— 见修复记录遗留项)");
            Console.WriteLine("==========================================================================================");

            if (!pass)
            {
                ctx.Fail($"航时上界存在 {violatingLegs} 个世纪尺度荒谬值 (最长 {worstYears:F1} 年) + {invertedLegs} 个边界倒置。" +
                         "根因: DerivePhysicalInterplanetaryBounds 的 maxDays = 霍曼时间 × 3.5 对长周期转移会放大到百年级。");
            }
            ctx.EndCase(pass);
            return pass;
        }
    }
}
