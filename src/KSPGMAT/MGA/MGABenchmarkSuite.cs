using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 历史真实行星际引力助推 (MGA) 任务基准测试用例与动力学严密性验证引擎
    /// (Historical MGA Mission Benchmarking & Physical Vector Continuity Verification Suite)
    /// </summary>
    public static class MGABenchmarkSuite {
        public class BenchmarkCase {
            public string MissionName { get; set; }
            public string Description { get; set; }
            public string DepartureBody { get; set; }
            public List<string> FlybySequence { get; set; }
            public double HistoricLaunchUT { get; set; }
            public string HistoricLaunchDateStr { get; set; }
            public double HistoricTotalDeltaVMinMs { get; set; }
            public double HistoricTotalDeltaVMaxMs { get; set; }
            public double HistoricFlightDaysMin { get; set; }
            public double HistoricFlightDaysMax { get; set; }
            public List<double> HistoricToFDays { get; set; }
            public double DepAltKm { get; set; } = 200.0;
            public double ArrAltKm { get; set; } = 200.0;
            public bool NoInsertionBurn { get; set; } = false;
        }

        public class BenchmarkResult {
            public BenchmarkCase TestCase { get; set; }
            public bool Success { get; set; }
            public double ComputedLaunchDV { get; set; }
            public double ComputedTotalDV { get; set; }
            public double ComputedFlightDays { get; set; }
            public bool VectorContinuityPassed { get; set; }
            public bool FlybyFeasibilityPassed { get; set; }
            public bool DeltaVWithinTolerance { get; set; }
            public MGASolution Solution { get; set; }
            public string GMATScriptPreview { get; set; }
            public string DetailedReport { get; set; }
        }

        public static readonly List<BenchmarkCase> StandardHistoricalMissions = new List<BenchmarkCase> {
            // 1. 水手 10 号 (Mariner 10) - 人类首次行星际引力助推任务 (Earth -> Venus -> Mercury)
            new BenchmarkCase {
                MissionName = "Mariner 10 (水手10号, 1973)",
                Description = "人类航天史上首次利用金星引力助推抵达水星的经典 MGA 任务 (E-V-M)",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Mercury" },
                HistoricLaunchDateStr = "03 Nov 1973 05:45:00",
                HistoricLaunchUT = 752409600.0 - 26.0 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 5200.0,
                HistoricFlightDaysMin = 140.0,
                HistoricFlightDaysMax = 160.0,
                HistoricToFDays = new List<double> { 94.0, 52.0 },
                NoInsertionBurn = true
            },

            // 2. 伽利略号 (Galileo - VEEGA, 1989) - 经典金星-地球-地球引力助推木星探测
            new BenchmarkCase {
                MissionName = "Galileo (伽利略号 VEEGA, 1989)",
                Description = "NASA 经典 VEEGA (Venus-Earth-Earth Gravity Assist) 木星轨道器任务",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Earth", "Earth", "Jupiter" },
                HistoricLaunchDateStr = "18 Oct 1989 16:53:40",
                HistoricLaunchUT = 752409600.0 - 10.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3900.0,
                HistoricTotalDeltaVMaxMs = 5800.0,
                HistoricFlightDaysMin = 2200.0,
                HistoricFlightDaysMax = 2300.0,
                HistoricToFDays = new List<double> { 115.0, 301.0, 731.0, 1095.0 },
                NoInsertionBurn = true
            },

            // 3. 卡西尼-惠更斯号 (Cassini-Huygens - VVEJPS, 1997) - 太阳系外行星探测巅峰之作
            new BenchmarkCase {
                MissionName = "Cassini-Huygens (卡西尼号 VVEJPS, 1997)",
                Description = "史上最宏伟的金星-金星-地球-木星-土星引力助推多目标深空探测任务",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Venus", "Venus", "Earth", "Jupiter", "Saturn" },
                HistoricLaunchDateStr = "15 Oct 1997 08:43:00",
                HistoricLaunchUT = 752409600.0 - 2.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3900.0,
                HistoricTotalDeltaVMaxMs = 6200.0,
                HistoricFlightDaysMin = 2400.0,
                HistoricFlightDaysMax = 2500.0,
                HistoricToFDays = new List<double> { 193.0, 424.0, 55.0, 499.0, 1279.0 },
                NoInsertionBurn = true
            },

            // 4. 旅行者 2 号 (Voyager 2 - Grand Tour, 1977) - 175年一遇太阳系大四重奏
            new BenchmarkCase {
                MissionName = "Voyager 2 (旅行者2号 Grand Tour, 1977)",
                Description = "木星-土星-天王星-海王星 (E-J-S-U-N) 连续无动力飞掠大行星全景探测",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Jupiter", "Saturn", "Uranus", "Neptune" },
                HistoricLaunchDateStr = "20 Aug 1977 14:29:44",
                HistoricLaunchUT = 752409600.0 - 22.4 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 6900.0,
                HistoricTotalDeltaVMaxMs = 7600.0,
                HistoricFlightDaysMin = 4300.0,
                HistoricFlightDaysMax = 4450.0,
                HistoricToFDays = new List<double> { 688.0, 762.0, 1618.0, 1307.0 },
                NoInsertionBurn = true
            },

            // 5. 罗塞塔号 (Rosetta - EEMEB, 2004) - 彗星 67P 探测复杂共振借力
            new BenchmarkCase {
                MissionName = "Rosetta (罗塞塔号 E-E-M-E, 2004)",
                Description = "地球-地球-火星-地球 (E-E-M-E) 多重引力弹弓与深空机动经典范例",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Earth", "Mars", "Earth" },
                HistoricLaunchDateStr = "02 Mar 2004 07:17:00",
                HistoricLaunchUT = 752409600.0 + 4.2 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3600.0,
                HistoricTotalDeltaVMaxMs = 5200.0,
                HistoricFlightDaysMin = 2000.0,
                HistoricFlightDaysMax = 2200.0,
                HistoricToFDays = new List<double> { 365.0, 725.0, 985.0 },
                NoInsertionBurn = true
            },

            // 6. 贝皮可伦坡号 (BepiColombo - EVVM, 2018)
            new BenchmarkCase {
                MissionName = "BepiColombo (贝皮可伦坡号 EVVM, 2018)",
                Description = "ESA/JAXA 地球-金星-金星-水星 (E-V-V-M) 极高难度深空减速引力助推",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Earth", "Venus", "Venus", "Mercury" },
                HistoricLaunchDateStr = "20 Oct 2018 01:45:00",
                HistoricLaunchUT = 752409600.0 + 18.8 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 5400.0,
                HistoricFlightDaysMin = 2500.0,
                HistoricFlightDaysMax = 2650.0,
                HistoricToFDays = new List<double> { 540.0, 190.0, 420.0, 1350.0 },
                NoInsertionBurn = true
            },

            // 7. 朱诺号 (Juno - EEJ, 2011)
            new BenchmarkCase {
                MissionName = "Juno (朱诺号 E-E-J, 2011)",
                Description = "NASA 地球-地球共振引力弹弓前往木星高偏心率极轨道任务",
                DepartureBody = "Earth",
                FlybySequence = new List<string> { "Earth", "Jupiter" },
                HistoricLaunchDateStr = "05 Aug 2011 16:25:00",
                HistoricLaunchUT = 752409600.0 + 11.6 * 365.25 * 86400.0,
                HistoricTotalDeltaVMinMs = 3800.0,
                HistoricTotalDeltaVMaxMs = 5200.0,
                HistoricFlightDaysMin = 1750.0,
                HistoricFlightDaysMax = 1850.0,
                HistoricToFDays = new List<double> { 800.0, 990.0 },
                NoInsertionBurn = false
            }
        };

        static MGABenchmarkSuite() {
            foreach (var b in StandardHistoricalMissions) {
                if (!string.IsNullOrEmpty(b.HistoricLaunchDateStr)) {
                    double ut = GMATClient.ParseUtcGregorian(b.HistoricLaunchDateStr);
                    if (ut > 0) b.HistoricLaunchUT = ut;
                }
            }
        }

        /// <summary>
        /// 运行自适应历史任务基准测试并输出严格的物理动力学与 GMAT 回写验证报告
        /// </summary>
        public static BenchmarkResult RunBenchmark(BenchmarkCase testCase) {
            var res = new BenchmarkResult {
                TestCase = testCase,
                Success = false,
                VectorContinuityPassed = true,
                FlybyFeasibilityPassed = true,
                DeltaVWithinTolerance = true
            };

            var sb = new StringBuilder();
            sb.AppendLine($"==========================================================================");
            sb.AppendLine($"[MGA 历史任务动力学校验报告] - {testCase.MissionName}");
            sb.AppendLine($"   序列: {testCase.DepartureBody} -> {string.Join(" -> ", testCase.FlybySequence)}");
            sb.AppendLine($"   历史发射历元: {testCase.HistoricLaunchDateStr}");
            sb.AppendLine($"==========================================================================");

            // 1. 构建真实历史先验基因组
            var fullChain = new List<string> { testCase.DepartureBody };
            fullChain.AddRange(testCase.FlybySequence);
            int legCount = fullChain.Count - 1;

            double[] genome = new double[1 + legCount];
            genome[0] = testCase.HistoricLaunchUT;
            for (int k = 0; k < legCount; k++) {
                genome[1 + k] = (k < testCase.HistoricToFDays.Count ? testCase.HistoricToFDays[k] : 200.0) * 86400.0;
            }

            var config = new MGAConfig {
                DepartureBody = testCase.DepartureBody,
                FlybySequence = testCase.FlybySequence,
                DestinationBody = testCase.FlybySequence.Last(),
                DepartureAltitudeKm = testCase.DepAltKm,
                DestinationAltitudeKm = testCase.ArrAltKm,
                NoInsertionBurn = testCase.NoInsertionBurn
            };

            // 2. 调用新架构求解器 (PyKEP 官方原生 MGA 求解)
            MGASolution sol = PykepBridge.BuildFullSolution(genome, fullChain, config);
            res.Solution = sol;
            if (sol == null) {
                res.Success = false;
                res.VectorContinuityPassed = false;
                res.FlybyFeasibilityPassed = false;
                res.DeltaVWithinTolerance = false;
                sb.AppendLine($"\n 求解失败: 原生 PyKEP MGA 评估不可用且未开启托管回退 (基线7禁止托管近似回退)。");
                res.DetailedReport = sb.ToString();
                return res;
            }
            res.ComputedLaunchDV = sol.LaunchDeltaVKms * 1000.0;
            res.ComputedTotalDV = sol.TotalDeltaVKms * 1000.0;
            res.ComputedFlightDays = sol.TotalFlightDays;

            sb.AppendLine($"\n【一、 航向动力学求解结果】");
            double c3First = (sol.Legs != null && sol.Legs.Count > 0) ? sol.Legs[0].C3Kms2 : 0.0;
            sb.AppendLine($"   • 出发脱离冲量 (TMI/Ejection): {res.ComputedLaunchDV:F1} m/s (C3 能量: {c3First:F2} km²/s²)");
            sb.AppendLine($"   • 任务总机动 ΔV: {res.ComputedTotalDV:F1} m/s (历史工程参考区间: {testCase.HistoricTotalDeltaVMinMs:F0} ~ {testCase.HistoricTotalDeltaVMaxMs:F0} m/s)");
            sb.AppendLine($"   • 总飞行耗时: {res.ComputedFlightDays:F1} 天 ({res.ComputedFlightDays / 365.25:F2} 年)");

            // 3. 严格验证多段 MGA 的物理矢量连续性 (C0 位置与超速动量闭环)
            sb.AppendLine($"\n【二、 多段物理矢量连续性与引力助推校验 (Vector Continuity)】");
            for (int k = 0; k < sol.Legs.Count; k++) {
                var leg = sol.Legs[k];
                sb.AppendLine($"   --- 第 {k + 1} 段: {leg.FromBody} -> {leg.ToBody} ({leg.ToFDays:F1} 天) ---");
                sb.AppendLine($"      出发位置: ({leg.DeparturePosKm.x:0.0E+0}, {leg.DeparturePosKm.y:0.0E+0}, {leg.DeparturePosKm.z:0.0E+0}) km");
                sb.AppendLine($"      到达位置: ({leg.ArrivalPosKm.x:0.0E+0}, {leg.ArrivalPosKm.y:0.0E+0}, {leg.ArrivalPosKm.z:0.0E+0}) km");

                if (k == 0) {
                    sb.AppendLine($"      [PyKEP 出射几何] 相角: {leg.PhaseAngleDeg:F2}° | 脱离角: {leg.EjectionAngleDeg:F2}° | LAN: {leg.EjectionLANDeg:F2}° | 倾角: {leg.EjectionInclinationDeg:F2}°");
                    sb.AppendLine($"      [3 轴 VNC 冲量] Prograde: {leg.ProgradeMs:F1} m/s, Normal: {leg.NormalMs:F1} m/s, Radial: {leg.RadialMs:F1} m/s");
                } else {
                    sb.AppendLine($"      [引力助推飞掠] 转向角: {leg.FlybyTurningAngleDeg:F2}° | 近心高度: {leg.FlybyPeriapsisAltitudeKm:F0} km | 飞掠机动: {leg.FlybyDeltaVKms * 1000.0:F1} m/s | 可行性: {(leg.IsFeasible ? "物理可行" : "超出借力限度")}");
                    if (!leg.IsFeasible) res.FlybyFeasibilityPassed = false;
                }

                // 检验前后两段位置 C0 连续性
                if (k > 0) {
                    double posDiscontinuity = (leg.DeparturePosKm - sol.Legs[k - 1].ArrivalPosKm).magnitude;
                    if (posDiscontinuity > 1e-3) {
                        res.VectorContinuityPassed = false;
                        sb.AppendLine($"       警告: 位置矢量存在 {posDiscontinuity:F3} km 不连续！");
                    }
                }
            }

            // 4. 验证 GMAT 脚本语法回写
            sb.AppendLine($"\n【三、 GMAT 3 轴 VNC 脚本回写验证】");
            try {
                res.GMATScriptPreview = Service.GMATMGAScriptGenerator.CompileToGmatScript(sol);
                sb.AppendLine($"   • GMAT ImpulsiveBurn 3 轴机动与星历变量回写: 生成成功 ({res.GMATScriptPreview.Length} 字节)");
                sb.AppendLine($"   • 包含脱离变量: EjectionAngle, EjectionLAN, EjectionInc, C3_Energy, V_Inf");
            } catch (Exception ex) {
                sb.AppendLine($"   • GMAT 脚本生成失败: {ex.Message}");
                res.Success = false;
            }

            // 4. ΔV 历史区间容差校验 (基线7: 如实判定, 避免数据造假)
            bool dvWithinTolerance = !double.IsNaN(res.ComputedTotalDV) &&
                res.ComputedTotalDV >= testCase.HistoricTotalDeltaVMinMs &&
                res.ComputedTotalDV <= testCase.HistoricTotalDeltaVMaxMs;
            res.DeltaVWithinTolerance = dvWithinTolerance;
            sb.AppendLine($"\n【四、 ΔV 历史区间容差校验】");
            sb.AppendLine($"   • 计算总 ΔV: {res.ComputedTotalDV:F1} m/s | 历史工程参考区间: [{testCase.HistoricTotalDeltaVMinMs:F0}, {testCase.HistoricTotalDeltaVMaxMs:F0}] m/s → {(dvWithinTolerance ? "落在历史区间内" : "超出历史区间")}");

            // 5. 综合判定 (纳入 ΔV 容差校验)
            if (res.VectorContinuityPassed && res.FlybyFeasibilityPassed && res.DeltaVWithinTolerance) {
                res.Success = true;
                sb.AppendLine($"\n【基准测试综合判定: PASS 通过！】");
                sb.AppendLine($"   新架构 (PyKEP 官方原生核心) 在历史真实任务数据下表现出良好的严谨性与连续性。");
            } else {
                res.Success = false;
                sb.AppendLine($"\n【基准测试综合判定: FAIL 校验失败 (矢量连续性 / 飞掠可行性 / ΔV 容差)】");
            }

            res.DetailedReport = sb.ToString();
            return res;
        }

        /// <summary>
        /// 批量自动运行全部 5 个历史基准任务并输出汇总
        /// </summary>
        public static (List<BenchmarkResult> results, string summary) RunAllBenchmarks() {
            var results = new List<BenchmarkResult>();
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================");
            sb.AppendLine(" NASA / ESA 历史真实 MGA 行星际引力助推全任务自适应基准测试汇总");
            sb.AppendLine("==========================================================================");

            int passCount = 0;
            foreach (var testCase in StandardHistoricalMissions) {
                var res = RunBenchmark(testCase);
                results.Add(res);
                if (res.Success) passCount++;

                string status = res.Success ? " PASS" : " FAIL";
                sb.AppendLine($"[{status}] {testCase.MissionName.PadRight(38)} | Launch ΔV: {res.ComputedLaunchDV:F0} m/s | Total ΔV: {res.ComputedTotalDV:F0} m/s | Flight: {res.ComputedFlightDays:F0}d");
            }

            sb.AppendLine("==========================================================================");
            sb.AppendLine($" 测试总结: {passCount} / {StandardHistoricalMissions.Count} 个历史任务动力学验证通过 (基于 PyKEP 官方原生核心; 判定纳入 ΔV 容差 / 矢量连续性 / 飞掠可行性)。");
            sb.AppendLine("==========================================================================");

            return (results, sb.ToString());
        }
    }
}
