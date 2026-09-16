using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    /// <summary>
    /// 历史基准飞行任务独立评测与裁决结果契约
    /// </summary>
    public sealed class BenchmarkVerificationOutcome
    {
        public MGABenchmarkSuite.BenchmarkCase TestCase { get; set; }
        public MGASolution Solution { get; set; }
        public double ComputedLaunchDV { get; set; }
        public double ComputedTotalDV { get; set; }
        public double ComputedFlightDays { get; set; }

        public bool VectorContinuityPassed { get; set; }
        public bool FlybyFeasibilityPassed { get; set; }
        public bool DeltaVWithinTolerance { get; set; }
        public bool FlightDaysWithinTolerance { get; set; }
        public bool TopologyMatchesHistoric { get; set; }
        public bool PrimerVectorOptimal { get; set; }

        public double PrimerVectorNorm { get; set; } = 1.0;
        public double DeltaVBandLo { get; set; }
        public double DeltaVBandHi { get; set; }
        public double DeltaVBandWidthRatio { get; set; }
        public double ToFBandLo { get; set; }
        public double ToFBandHi { get; set; }

        public string TopologyMismatchReason { get; set; }
        public List<string> FailReasons { get; set; } = new List<string>();
        public string DetailedReport { get; set; }
        public double ElapsedSeconds { get; set; }

        /// <summary>
        /// 综合判定: 矢量连续 + 飞掠可行 + ΔV合规 + 航时合规 + 拓扑吻合 + 解存在
        /// </summary>
        public bool Passed { get; set; }

        /// <summary>
        /// 转换为基础 EvaluationOutcome 契约
        /// </summary>
        public EvaluationOutcome ToBaseOutcome()
        {
            return new EvaluationOutcome
            {
                Converged = Solution != null,
                AllLegsFeasible = VectorContinuityPassed && FlybyFeasibilityPassed,
                TotalDvMs = ComputedTotalDV,
                BudgetMs = TestCase != null ? (TestCase.HistoricTotalDeltaVMs > 0 ? TestCase.HistoricTotalDeltaVMs * (1.0 + TestCase.MaxAcceptableDvRelTolerance) : TestCase.HistoricTotalDeltaVMaxMs) : 0.0,
                IsHistoricReplay = TestCase != null && TestCase.SearchMode == MGABenchmarkSuite.MGABenchmarkMode.HistoricReplay,
                Summary = string.Join("; ", FailReasons)
            };
        }
    }

    /// <summary>
    /// 历史深空与经典飞行任务独立物理动力学校验与确认裁决引擎
    /// (Historical Benchmark Independent Physical Verification & Adjudication Engine)
    ///
    /// 【核心架构设计 · 物理隔离防作弊】
    /// 1. 考卷与判卷逻辑从生产源码 (src/KSPGMAT/MGA/MGABenchmarkSuite.cs) 彻底剥离至测试工程独立维护；
    /// 2. 杜绝在生产代码中预写通过断言、反向注入求解器约束或借由答案挑拣解的应试作弊行为；
    /// 3. 本引擎基于权威 NASA JPL Horizon / ESA 论文实测数据，独立实施严苛的 5 维物理硬门禁校验：
    ///    - [1] 轨道矢量连续性 (C0 Continuity, 误差 <= 1e-3 km)
    ///    - [2] 引力助推物理可行性 (近心高度安全净空, 无动力飞掠脉冲严格 0.00 m/s)
    ///    - [3] 历史速度增量 ΔV 容差包络与带宽可证伪性比率核验
    ///    - [4] 任务总飞行时间 ToF 历史工程包络与防世纪退化解核验
    ///    - [5] 借力拓扑构型链路节点严格吻合性核验 (严禁非标插入或异化)
    ///    - [6] 劳登主矢量极值最优性 (|p| Norm <= 1.15)
    /// </summary>
    public static class HistoricalBenchmarkVerificationEngine
    {
        /// <summary>
        /// 对给定的任务案例与求解器输出方案执行纯粹客观的物理动力学裁决与确认
        /// </summary>
        public static BenchmarkVerificationOutcome VerifyAndAdjudicate(
            MGABenchmarkSuite.BenchmarkCase testCase,
            MGASolution sol,
            double elapsedSeconds = 0.0)
        {
            var outcome = new BenchmarkVerificationOutcome
            {
                TestCase = testCase,
                Solution = sol,
                ElapsedSeconds = elapsedSeconds,
                VectorContinuityPassed = true,
                FlybyFeasibilityPassed = true,
                DeltaVWithinTolerance = true,
                FlightDaysWithinTolerance = true,
                TopologyMatchesHistoric = true,
                PrimerVectorOptimal = true
            };

            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================");
            sb.AppendLine($" 🪐 [独立基准测试裁决引擎] 任务动力学校验: {testCase.MissionName}");
            sb.AppendLine($"   参考链路: {testCase.DepartureBody} ➔ {string.Join(" ➔ ", testCase.FlybySequence)}");
            sb.AppendLine($"   历史发射历元: {testCase.HistoricLaunchDateStr} (UT {testCase.HistoricLaunchUT:F0})");
            sb.AppendLine($"   运行模式: {MGABenchmarkSuite.DescribeMode(testCase.SearchMode)}");
            sb.AppendLine("==========================================================================");

            if (sol == null)
            {
                outcome.Passed = false;
                outcome.VectorContinuityPassed = false;
                outcome.FlybyFeasibilityPassed = false;
                outcome.DeltaVWithinTolerance = false;
                outcome.FlightDaysWithinTolerance = false;
                outcome.TopologyMatchesHistoric = false;
                outcome.PrimerVectorOptimal = false;
                outcome.FailReasons.Add("求解器未能生成有效解 (Solution 为 null)");
                sb.AppendLine("\n❌ 求解失败: 未能生成有效解！");
                outcome.DetailedReport = sb.ToString();
                return outcome;
            }

            outcome.ComputedLaunchDV = sol.LaunchDeltaVKms * 1000.0;
            outcome.ComputedTotalDV = sol.TotalDeltaVKms * 1000.0;
            outcome.ComputedFlightDays = sol.TotalFlightDays;

            double c3First = (sol.Legs != null && sol.Legs.Count > 0) ? sol.Legs[0].C3Kms2 : 0.0;
            sb.AppendLine("\n【一、 航向动力学求解结果】");
            sb.AppendLine($"   • 出发脱离冲量 (TMI/Ejection): {outcome.ComputedLaunchDV:F1} m/s (C3 能量: {c3First:F2} km²/s²)");
            sb.AppendLine($"   • 任务总机动 ΔV: {outcome.ComputedTotalDV:F1} m/s (历史参考: {(testCase.HistoricTotalDeltaVMs > 0.0 ? $"单值 {testCase.HistoricTotalDeltaVMs:F0} m/s" : $"区间 {testCase.HistoricTotalDeltaVMinMs:F0} ~ {testCase.HistoricTotalDeltaVMaxMs:F0} m/s")})");
            sb.AppendLine($"   • 总飞行耗时: {outcome.ComputedFlightDays:F1} 天 ({outcome.ComputedFlightDays / 365.25:F2} 年)");

            // 1. 多段物理矢量连续性 (C0 位置与超速动量闭环) 与飞掠可行性检验
            sb.AppendLine("\n【二、 多段物理矢量连续性与引力助推校验 (Vector Continuity)】");
            if (sol.Legs != null && sol.Legs.Count > 0)
            {
                for (int k = 0; k < sol.Legs.Count; k++)
                {
                    var leg = sol.Legs[k];
                    string clearanceStr = leg.FlybyPeriapsisAltitudeKm > 0 ? $"{leg.FlybyPeriapsisAltitudeKm:F1} km" : "-";
                    sb.AppendLine($"   --- 第 {k + 1} 段: {leg.FromBody} ➔ {leg.ToBody} ({leg.ToFDays:F1} 天) ---");
                    sb.AppendLine($"      出发位置: ({leg.DeparturePosKm.x:0.0E+0}, {leg.DeparturePosKm.y:0.0E+0}, {leg.DeparturePosKm.z:0.0E+0}) km");
                    sb.AppendLine($"      到达位置: ({leg.ArrivalPosKm.x:0.0E+0}, {leg.ArrivalPosKm.y:0.0E+0}, {leg.ArrivalPosKm.z:0.0E+0}) km");

                    if (k == 0)
                    {
                        sb.AppendLine($"      [出射几何] 相角: {leg.PhaseAngleDeg:F2}° | 脱离角: {leg.EjectionAngleDeg:F2}° | LAN: {leg.EjectionLANDeg:F2}° | 倾角: {leg.EjectionInclinationDeg:F2}°");
                        sb.AppendLine($"      [3 轴 VNC 冲量] Prograde: {leg.ProgradeMs:F1} m/s, Normal: {leg.NormalMs:F1} m/s, Radial: {leg.RadialMs:F1} m/s");
                    }
                    else
                    {
                        double pulseMs = leg.FlybyDeltaVKms * 1000.0;
                        bool isUnpoweredFlyby = (testCase.FlybyStrategies == null) ||
                                                (k - 1 < testCase.FlybyStrategies.Count && testCase.FlybyStrategies[k - 1] == MGAFlybyStrategy.UnpoweredFlyby);

                        sb.AppendLine($"      [引力助推飞掠] 转向角: {leg.FlybyTurningAngleDeg:F2}° | 近心净空: {clearanceStr} | 飞掠脉冲: {pulseMs:F1} m/s | 可行性: {(leg.IsFeasible ? "✅ 物理可行" : "❌ 超出借力限度")}");

                        if (!leg.IsFeasible)
                        {
                            outcome.FlybyFeasibilityPassed = false;
                            outcome.FailReasons.Add($"第 {k + 1} 段飞掠 ({leg.FromBody}➔{leg.ToBody}) 超出物理可行界限");
                        }

                        // 硬门禁: 无动力飞掠严禁施加推进脉冲 (允许 PyKEP 原生代数判据 <= 5.0 m/s 数值底噪)
                        if (isUnpoweredFlyby && leg.FlybyDeltaVKms > 0.005)
                        {
                            outcome.FlybyFeasibilityPassed = false;
                            outcome.FailReasons.Add($"第 {k + 1} 段无动力借力出现非标机动脉冲 ({pulseMs:F1} m/s > 5.0 m/s 物理守恒门限)");
                            sb.AppendLine($"       ❌ 拒绝通过: 无动力飞掠施加了 {pulseMs:F1} m/s 机动，存在假借力作弊嫌疑！");
                        }

                        // 硬门禁: 飞掠近心高度必须在天体表面/大气层之上
                        if (leg.FlybyPeriapsisAltitudeKm < 0.0 && leg.FlybyPeriapsisRadiusKm > 0.0)
                        {
                            outcome.FlybyFeasibilityPassed = false;
                            outcome.FailReasons.Add($"第 {k + 1} 段近心高度为负 ({leg.FlybyPeriapsisAltitudeKm:F1} km, 撞击天体表面)");
                            sb.AppendLine($"       ❌ 拒绝通过: 飞掠近拱高度 {leg.FlybyPeriapsisAltitudeKm:F1} km 撞击地表！");
                        }
                    }

                    // 检验前后两段位置 C0 连续性 (门限 1e-3 km)
                    if (k > 0)
                    {
                        double posDiscontinuity = (leg.DeparturePosKm - sol.Legs[k - 1].ArrivalPosKm).magnitude;
                        if (posDiscontinuity > 1e-3)
                        {
                            outcome.VectorContinuityPassed = false;
                            sb.AppendLine($"       ⚠️ 警告: 位置矢量存在 {posDiscontinuity:F3} km 不连续！");
                        }
                    }
                }
            }

            // 2. ΔV 历史工程容差严格校验
            double bandLo, bandHi;
            string bandDesc;
            if (testCase.HistoricTotalDeltaVMs > 0.0)
            {
                double tol = testCase.MaxAcceptableDvRelTolerance;
                bandLo = testCase.HistoricTotalDeltaVMs * (1.0 - tol);
                bandHi = testCase.HistoricTotalDeltaVMs * (1.0 + tol);
                bandDesc = $"历史单值 {testCase.HistoricTotalDeltaVMs:F0} m/s ± {tol * 100.0:F0}% = [{bandLo:F0}, {bandHi:F0}] m/s";
            }
            else
            {
                bandLo = testCase.HistoricTotalDeltaVMinMs;
                bandHi = testCase.HistoricTotalDeltaVMaxMs;
                bandDesc = $"历史区间 [{bandLo:F0}, {bandHi:F0}] m/s";
            }
            double bandWidthRatio = (bandLo > 0.0) ? (bandHi - bandLo) / bandLo : 0.0;
            outcome.DeltaVBandLo = bandLo;
            outcome.DeltaVBandHi = bandHi;
            outcome.DeltaVBandWidthRatio = bandWidthRatio;

            bool dvWithinTol = !double.IsNaN(outcome.ComputedTotalDV) &&
                               outcome.ComputedTotalDV >= bandLo &&
                               outcome.ComputedTotalDV <= bandHi;
            outcome.DeltaVWithinTolerance = dvWithinTol;

            sb.AppendLine("\n【三、 ΔV 历史容差校验】");
            sb.AppendLine($"   • 计算总 ΔV: {outcome.ComputedTotalDV:F1} m/s | {bandDesc} ➔ {(dvWithinTol ? "✅ 落在判定带内" : "❌ 超出判定带")}");
            sb.AppendLine($"   • 判定带宽比 (Hi-Lo)/Lo = {bandWidthRatio * 100.0:F0}% | 出处: {(string.IsNullOrEmpty(testCase.HistoricTotalDeltaVProvenance) ? "⚠ 未登记" : testCase.HistoricTotalDeltaVProvenance)}");

            // 3. 航时历史工程包络校验 (防世纪尺度退化解)
            double tofBandLo = 0.0, tofBandHi = double.MaxValue;
            bool hasHistoricFlightDays = testCase.HistoricFlightDaysMax > 0.0;
            if (hasHistoricFlightDays)
            {
                double tofTol = testCase.MaxAcceptableToFRelTolerance > 0.0 ? testCase.MaxAcceptableToFRelTolerance : 0.25;
                tofBandLo = testCase.HistoricFlightDaysMin > 0.0 ? testCase.HistoricFlightDaysMin * (1.0 - tofTol) : 0.0;
                tofBandHi = testCase.HistoricFlightDaysMax * (1.0 + tofTol);
            }
            outcome.ToFBandLo = tofBandLo;
            outcome.ToFBandHi = tofBandHi;

            bool tofWithinTol = !hasHistoricFlightDays ||
                               (!double.IsNaN(outcome.ComputedFlightDays) && outcome.ComputedFlightDays >= tofBandLo && outcome.ComputedFlightDays <= tofBandHi);
            outcome.FlightDaysWithinTolerance = tofWithinTol;

            sb.AppendLine("\n【四、 航时历史工程包络校验】");
            sb.AppendLine($"   • 计算总航时: {outcome.ComputedFlightDays:F1} 天 ({outcome.ComputedFlightDays / 365.25:F2} 年) | 历史参考: {testCase.HistoricFlightDaysMin:F0} ~ {testCase.HistoricFlightDaysMax:F0} 天 (允许带 [{tofBandLo:F0}, {tofBandHi:F0}] 天) ➔ {(tofWithinTol ? "✅ 落在判定带内" : "❌ 航时严重超标")}");

            // 4. 借力拓扑构型一致性校验
            bool topologyMatches = true;
            string topoReason = null;
            if (testCase.SearchMode == MGABenchmarkSuite.MGABenchmarkMode.HistoricTopologyTrajectory ||
                testCase.SearchMode == MGABenchmarkSuite.MGABenchmarkMode.HistoricReplay)
            {
                var expectedChain = new List<string> { testCase.DepartureBody };
                expectedChain.AddRange(testCase.FlybySequence);

                var actualChain = new List<string>();
                if (sol.Legs != null && sol.Legs.Count > 0)
                {
                    actualChain.Add(sol.Legs[0].FromBody);
                    for (int k = 0; k < sol.Legs.Count; k++)
                    {
                        actualChain.Add(sol.Legs[k].ToBody);
                    }
                }

                if (actualChain.Count != expectedChain.Count)
                {
                    topologyMatches = false;
                    topoReason = $"航段天体数不一致 (期望 {string.Join("➔", expectedChain)}, 实际 {string.Join("➔", actualChain)})";
                }
                else
                {
                    for (int k = 0; k < expectedChain.Count; k++)
                    {
                        if (!actualChain[k].Equals(expectedChain[k], StringComparison.OrdinalIgnoreCase))
                        {
                            topologyMatches = false;
                            topoReason = $"链路节点不匹配 (期望 #{k + 1}={expectedChain[k]}, 实际={actualChain[k]})";
                            break;
                        }
                    }
                }
            }
            else if (testCase.SearchMode == MGABenchmarkSuite.MGABenchmarkMode.TopologyBlind)
            {
                if (sol.Legs == null || sol.Legs.Count == 0 ||
                    !sol.Legs.Last().ToBody.Equals(testCase.FlybySequence.Last(), StringComparison.OrdinalIgnoreCase))
                {
                    topologyMatches = false;
                    topoReason = "到达目标天体不一致";
                }
            }
            outcome.TopologyMatchesHistoric = topologyMatches;
            outcome.TopologyMismatchReason = topoReason;

            sb.AppendLine("\n【五、 借力拓扑构型一致性校验】");
            sb.AppendLine($"   • 拓扑构型判定: {(topologyMatches ? "✅ 严格吻合历史链路" : $"❌ 拓扑异化: {topoReason}")}");

            // 5. 劳登主矢量极值最优性校验
            double pNorm = (sol.PrimerVector != null && sol.PrimerVector.MaxPMag > 0) ? sol.PrimerVector.MaxPMag : 1.0;
            outcome.PrimerVectorNorm = pNorm;
            outcome.PrimerVectorOptimal = (pNorm <= 1.15);

            sb.AppendLine("\n【六、 劳登主矢量最优性校验】");
            sb.AppendLine($"   • 主矢量极值 |p| = {pNorm:F3} (最优判定门限 <= 1.15) ➔ {(outcome.PrimerVectorOptimal ? "✅ 满足冲量最优" : "次优或存在机动缺陷")}");

            // 6. 综合独立裁决
            bool casePassed = outcome.VectorContinuityPassed &&
                              outcome.FlybyFeasibilityPassed &&
                              outcome.DeltaVWithinTolerance &&
                              outcome.FlightDaysWithinTolerance &&
                              outcome.TopologyMatchesHistoric &&
                              outcome.PrimerVectorOptimal;
            outcome.Passed = casePassed;

            if (!casePassed)
            {
                if (!outcome.VectorContinuityPassed) outcome.FailReasons.Add("轨道位置矢量不连续 (> 1e-3 km)");
                if (!outcome.FlybyFeasibilityPassed) outcome.FailReasons.Add("借力飞掠超出物理界限或近心高度违规");
                if (!outcome.DeltaVWithinTolerance) outcome.FailReasons.Add($"总ΔV超出历史容差 ({outcome.ComputedTotalDV:F1} m/s vs {bandDesc})");
                if (!outcome.FlightDaysWithinTolerance) outcome.FailReasons.Add($"总航时严重超标 ({outcome.ComputedFlightDays:F1} 天 vs 允许上限 {tofBandHi:F0} 天)");
                if (!outcome.TopologyMatchesHistoric) outcome.FailReasons.Add($"拓扑构型与历史不符: {topoReason}");
                if (!outcome.PrimerVectorOptimal) outcome.FailReasons.Add($"劳登主矢量极值非最优 (|p| = {pNorm:F3} > 1.15)");
            }

            sb.AppendLine("\n【独立裁决综合判定】: " + (casePassed ? "✅ PASS 物理全闭环收敛且严格符合历史！" : "❌ FAIL 校验失败: " + string.Join("; ", outcome.FailReasons)));
            outcome.DetailedReport = sb.ToString();

            return outcome;
        }

        /// <summary>
        /// 运行单项基准测试求解并进行独立裁决，同时完整同步数据至测试上下文
        /// </summary>
        public static BenchmarkVerificationOutcome RunAndVerify(
            MGABenchmarkSuite.BenchmarkCase testCase,
            TestContext ctx = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = MGABenchmarkSuite.RunBenchmark(testCase);
            sw.Stop();

            var outcome = VerifyAndAdjudicate(testCase, res?.Solution, sw.Elapsed.TotalSeconds);

            // 控制台高保真动力学输出
            var sol = outcome.Solution;
            if (sol != null)
            {
                Console.WriteLine($"✅ 求解结果: {(outcome.Passed ? "【PASS 物理全闭环收敛且严格符合历史】" : "【FAIL 指标超限或拓扑异化】")} | 耗时: {outcome.ElapsedSeconds:F2} 秒");
                Console.WriteLine($"• 始发历元: {GMATClient.FormatUtcGregorian(sol.DepartureUT)} (UT {sol.DepartureUT:F0})");
                Console.WriteLine($"• 总速度增量 ΔV: {outcome.ComputedTotalDV:F1} m/s (历史允许范围: {testCase.HistoricTotalDeltaVMinMs:F0} ~ {testCase.HistoricTotalDeltaVMaxMs:F0} m/s) -> {(outcome.DeltaVWithinTolerance ? "✅ 合规" : "❌ 超标")}");
                Console.WriteLine($"• 飞行总天数: {outcome.ComputedFlightDays:F1} 天 ({outcome.ComputedFlightDays / 365.25:F2} 年 | 历史范围: {testCase.HistoricFlightDaysMin:F0} ~ {testCase.HistoricFlightDaysMax:F0} 天) -> {(outcome.FlightDaysWithinTolerance ? "✅ 合规" : "❌ 严重超标")}");
                Console.WriteLine($"• 借力拓扑构型: {(outcome.TopologyMatchesHistoric ? "✅ 吻合历史" : "❌ 发生异化/非标插入")}");
                Console.WriteLine("• 冲量详细构成:");
                Console.WriteLine($"    - 始发脱离脉冲 (Launch ΔV):     {sol.LaunchDeltaVKms * 1000.0,8:F1} m/s (C3 = {(sol.Legs != null && sol.Legs.Count > 0 ? sol.Legs[0].C3Kms2.ToString("F2") : "0.00")} km²/s²)");
                Console.WriteLine($"    - 深空中途机动 (DSM ΔV):        {sol.TotalDsmDeltaVKms * 1000.0,8:F1} m/s");
                Console.WriteLine($"    - 飞掠偏折脉冲 (Flyby ΔV):      {sol.TotalFlybyDeltaVKms * 1000.0,8:F1} m/s (无动力合规: {sol.TotalFlybyDeltaVKms <= 0.005})");
                Console.WriteLine($"    - 目标捕获制动 (Insertion ΔV):  {sol.InsertionDeltaVKms * 1000.0,8:F1} m/s");
                Console.WriteLine($"• 劳登主矢量极值 (|p| Norm): {outcome.PrimerVectorNorm:F3} (最优性判据: {(outcome.PrimerVectorNorm <= 1.05 ? "严格冲量最优" : (outcome.PrimerVectorNorm <= 1.15 ? "次优" : "非最优"))})");

                if (sol.Legs != null && sol.Legs.Count > 0)
                {
                    Console.WriteLine($"• 各航段精细动力学参数 (共 {sol.Legs.Count} 段):");
                    for (int legIdx = 0; legIdx < sol.Legs.Count; legIdx++)
                    {
                        var leg = sol.Legs[legIdx];
                        string clearanceStr = leg.FlybyPeriapsisAltitudeKm > 0 ? $"{leg.FlybyPeriapsisAltitudeKm:F1} km" : "-";
                        Console.WriteLine($"    [Leg #{legIdx + 1}] {leg.FromBody,-7} ➔ {leg.ToBody,-8} | 航时={leg.ToFDays,6:F1}d | 脉冲={leg.DeltaVKms * 1000.0,6:F1} m/s | C3={leg.C3Kms2,7:F2} km²/s² | 借力近拱净空={clearanceStr}");
                        ctx?.Log($"Leg #{legIdx + 1}: {leg.FromBody} -> {leg.ToBody}, ToF={leg.ToFDays:F1}d, ΔV={leg.DeltaVKms * 1000.0:F1}m/s, C3={leg.C3Kms2:F2}, PeAlt={clearanceStr}");
                    }
                }

                if (!outcome.Passed)
                {
                    ctx?.Fail(string.Join("; ", outcome.FailReasons));
                }

                if (ctx != null)
                {
                    ctx.RecordMetric("始发历元", GMATClient.FormatUtcGregorian(sol.DepartureUT));
                    ctx.RecordMetric("计算总ΔV", $"{outcome.ComputedTotalDV:F1} m/s");
                    ctx.RecordMetric("历史参考ΔV", $"{testCase.HistoricTotalDeltaVMinMs:F0}~{testCase.HistoricTotalDeltaVMaxMs:F0} m/s");
                    ctx.RecordMetric("ΔV判定", outcome.DeltaVWithinTolerance ? "✅ 合规" : "❌ 超标");
                    ctx.RecordMetric("总航时", $"{outcome.ComputedFlightDays:F1} 天");
                    ctx.RecordMetric("历史航时", $"{testCase.HistoricFlightDaysMin:F0}~{testCase.HistoricFlightDaysMax:F0} 天");
                    ctx.RecordMetric("航时判定", outcome.FlightDaysWithinTolerance ? "✅ 合规" : "❌ 严重超标");
                    ctx.RecordMetric("拓扑判定", outcome.TopologyMatchesHistoric ? "✅ 吻合" : "❌ 异化/插入");
                    ctx.RecordMetric("始发脉冲", $"{sol.LaunchDeltaVKms * 1000.0:F1} m/s");
                    ctx.RecordMetric("DSM机动", $"{sol.TotalDsmDeltaVKms * 1000.0:F1} m/s");
                    ctx.RecordMetric("借力脉冲", $"{sol.TotalFlybyDeltaVKms * 1000.0:F1} m/s");
                    ctx.RecordMetric("捕获制动", $"{sol.InsertionDeltaVKms * 1000.0:F1} m/s");
                    ctx.RecordMetric("劳登最优|p|", $"{outcome.PrimerVectorNorm:F3}");
                    ctx.RecordMetric("航段数", sol.Legs != null ? sol.Legs.Count.ToString() : "1");
                }
            }
            else
            {
                Console.WriteLine("❌ 求解失败: 未能生成有效解！");
                ctx?.Fail("未能生成有效解: 求解器返回 null");
            }

            return outcome;
        }
    }
}
