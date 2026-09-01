using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// 专门负责 GMAT 求解结果解析、作用域变量提取、任务序列数值回调同步以及 Principia 节点写入的权威独立服务模块。
    /// 遵循 NASA GMAT 官方 API 数据链路，杜绝模糊硬猜与跨打靶器变量交叉污染。
    /// </summary>
    public static class GMATSolutionMapper {

        /// <summary>
        /// 完整解析 GMAT 运行输出，生成标准 MissionResult 单一事实对象
        /// </summary>
        public static MissionResult ParseAndMapResult(
            string stdout,
            string stderr,
            int exitCode,
            string reportFile,
            string outputDir,
            List<MissionStep> originalSteps,
            string scriptText,
            string scriptFile = null) 
        {
            var result = new MissionResult {
                success = (exitCode == 0),
                error = stderr,
                gmat_log = stdout,
                script_path = !string.IsNullOrEmpty(scriptFile) ? scriptFile : reportFile,
                solved_burns = new List<SolvedBurn>(),
                trajectory = new List<TrajectoryPoint>(),
                updated_sequence = originalSteps ?? new List<MissionStep>(),
                dc_summary = new DCSummary()
            };

            // 1. 解析各个 DifferentialCorrector*.data 官方结构化文件 (Per-DC 作用域隔离)
            var dcSummaries = ParseDifferentialCorrectorFiles(outputDir);
            var globalSolvedVars = ExtractGlobalSolvedVariables(stdout);

            // 整合汇总 DC 数据
            bool anyDcFailed = false;
            int totalIterations = 0;
            foreach (var kvp in dcSummaries) {
                totalIterations += kvp.Value.iterations;
                if (!kvp.Value.converged) anyDcFailed = true;
                foreach (var v in kvp.Value.final_variables) {
                    globalSolvedVars[v.Key] = v.Value;
                }
            }

            result.dc_summary.converged = (dcSummaries.Count > 0) ? !anyDcFailed : (exitCode == 0);
            result.dc_summary.iterations = totalIterations;
            result.dc_summary.final_variables = globalSolvedVars;
            result.dc_summaries = dcSummaries;

            // 2. 解析 TrajReport.txt 轨迹全时序与最终轨道六根数
            ParseTrajectoryReport(reportFile, result);

            // 3. 构建高精度 SolvedBurn (模型驱动绑定推进器与逐级质量积分，并提取 GMAT 原生积分事件时刻)
            result.solved_burns = BuildSolvedBurns(originalSteps, dcSummaries, globalSolvedVars, result.trajectory);

            // 4. 同步更新任务序列的实际求解值 (数值回调回填)
            SyncSolvedValuesToSequence(result.updated_sequence, dcSummaries, globalSolvedVars, result.solved_burns);

            // 5. 解析各个自定义瞬时状态报告 (StepReport)
            ParseStepReports(outputDir, originalSteps, result);

            // 6. 提取全任务关键里程碑时序 (Milestone Timeline)
            result.milestones = GMATTimelineExtractor.ExtractMilestones(
                originalSteps, 
                dcSummaries, 
                globalSolvedVars, 
                result.trajectory, 
                result.step_reports, 
                result.solve_ut
            );

            return result;
        }

        /// <summary>
        /// 解析 DifferentialCorrector*.data 文件，按打靶器名称隔离
        /// </summary>
        public static Dictionary<string, DCSummary> ParseDifferentialCorrectorFiles(string outputDir) {
            var map = new Dictionary<string, DCSummary>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir)) return map;

            foreach (var file in Directory.GetFiles(outputDir, "DifferentialCorrector*.data")) {
                try {
                    string filename = Path.GetFileNameWithoutExtension(file);
                    string dcName = filename.Replace("DifferentialCorrector", "").Trim();
                    if (string.IsNullOrEmpty(dcName)) dcName = "DefaultDC";

                    string content = File.ReadAllText(file);
                    var summary = new DCSummary {
                        converged = content.Contains("Targeting Completed in") || !content.Contains("failed"),
                        iterations = 1,
                        final_variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                        goals_summary = new List<DCGoalEntry>()
                    };

                    // 提取迭代次数
                    var iterMatch = Regex.Match(content, @"Targeting Completed in (\d+) iterations");
                    if (iterMatch.Success && int.TryParse(iterMatch.Groups[1].Value, out int iters)) {
                        summary.iterations = iters;
                    }

                    // 提取最后一次 Iteration 的最终变量与目标值
                    int lastIterIdx = content.LastIndexOf("Iteration ");
                    string finalBlock = (lastIterIdx >= 0) ? content.Substring(lastIterIdx) : content;

                    // 提取变量赋值
                    var varMatches = Regex.Matches(finalBlock, @"([A-Za-z_][\w.]*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)");
                    foreach (Match m in varMatches) {
                        string vName = m.Groups[1].Value.Trim();
                        if (double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val)) {
                            summary.final_variables[vName] = val;
                            if (vName.Contains(".")) {
                                string shortName = vName.Substring(vName.LastIndexOf('.') + 1);
                                summary.final_variables[shortName] = val;
                            }
                        }
                    }

                    // 提取目标值 Achieved vs Desired
                    var goalMatches = Regex.Matches(finalBlock, @"([A-Za-z_][\w.]*)\s+Desired:\s*([-+]?[\d.eE+-]+)\s+Achieved:\s*([-+]?[\d.eE+-]+)");
                    foreach (Match gm in goalMatches) {
                        string gName = gm.Groups[1].Value.Trim();
                        double.TryParse(gm.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double des);
                        double.TryParse(gm.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double ach);
                        summary.goals_summary.Add(new DCGoalEntry {
                            GoalName = gName,
                            Desired = des,
                            Achieved = ach
                        });
                    }

                    map[dcName] = summary;
                } catch (Exception ex) {
                    KSPGMATLog.Warn($"[GMATSolutionMapper] Error parsing {file}: {ex.Message}");
                }
            }
            return map;
        }

        /// <summary>
        /// 从控制台 Stdout 中提取全局备用变量字典
        /// </summary>
        public static Dictionary<string, double> ExtractGlobalSolvedVariables(string stdout) {
            var vars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(stdout)) return vars;

            var matches = Regex.Matches(stdout, @"([A-Za-z_][\w.]*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)");
            foreach (Match m in matches) {
                string k = m.Groups[1].Value.Trim();
                if (k.Equals("Tolerance", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Desired", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Achieved", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("Variance", StringComparison.OrdinalIgnoreCase)) continue;

                if (double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double vf)) {
                    vars[k] = vf;
                    if (k.Contains(".")) {
                        string shortK = k.Substring(k.LastIndexOf('.') + 1);
                        vars[shortK] = vf;
                    }
                }
            }
            return vars;
        }

        /// <summary>
        /// 解析 TrajReport.txt 报表文件
        /// </summary>
        public static void ParseTrajectoryReport(string reportFile, MissionResult result) {
            if (string.IsNullOrEmpty(reportFile) || !File.Exists(reportFile)) return;

            try {
                var lines = File.ReadAllLines(reportFile);
                double finalSma = 0, finalEcc = 0, finalInc = 0, finalRaan = 0, finalAop = 0, finalTa = 0, finalMass = 0;

                for (int li = 0; li < lines.Length; li++) {
                    string line = lines[li].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var parts = line.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 7) {
                        double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double t);
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
                        double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
                        double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double z);
                        double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double vx);
                        double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double vy);
                        double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out double vz);

                        // Filter out intermediate solver iteration rollbacks to maintain clean strictly monotonic converged trajectory
                        if (result.trajectory.Count > 0 && t < result.trajectory[result.trajectory.Count - 1].t - 0.5) {
                            while (result.trajectory.Count > 0 && result.trajectory[result.trajectory.Count - 1].t >= t - 0.5) {
                                result.trajectory.RemoveAt(result.trajectory.Count - 1);
                            }
                        }

                        result.trajectory.Add(new TrajectoryPoint { t = t, x = x, y = y, z = z, vx = vx, vy = vy, vz = vz });

                        if (parts.Length >= 14) {
                            double.TryParse(parts[8], NumberStyles.Any, CultureInfo.InvariantCulture, out finalSma);
                            double.TryParse(parts[9], NumberStyles.Any, CultureInfo.InvariantCulture, out finalEcc);
                            double.TryParse(parts[10], NumberStyles.Any, CultureInfo.InvariantCulture, out finalInc);
                            double.TryParse(parts[11], NumberStyles.Any, CultureInfo.InvariantCulture, out finalRaan);
                            double.TryParse(parts[12], NumberStyles.Any, CultureInfo.InvariantCulture, out finalAop);
                            double.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out finalTa);
                            if (parts.Length >= 15) double.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out finalMass);
                        }
                    }
                }
                result.final_sma = finalSma;
                result.final_ecc = finalEcc;
                result.final_inc = finalInc;
                result.final_raan = finalRaan;
                result.final_aop = finalAop;
                result.final_ta = finalTa;
                result.final_mass = finalMass;
            } catch (Exception ex) {
                KSPGMATLog.Warn($"[GMATSolutionMapper] Error parsing TrajReport: {ex.Message}");
            }
        }

        /// <summary>
        /// 基于模型推进器与多级质量递推构建高精度机动列表
        /// </summary>
        public static List<SolvedBurn> BuildSolvedBurns(
            List<MissionStep> originalSteps, 
            Dictionary<string, DCSummary> dcSummaries, 
            Dictionary<string, double> globalSolvedVars,
            List<TrajectoryPoint> trajectory = null) 
        {
            var burns = new List<SolvedBurn>();
            if (originalSteps == null || originalSteps.Count == 0) return burns;

            double simClock = 0.0;
            double currentRunningMass = GMATStateManager.Current.Spacecraft.TotalMassKg > 0 
                ? GMATStateManager.Current.Spacecraft.TotalMassKg 
                : (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.GetTotalMass() * 1000.0 : 1000.0);

            string currentDC = "";
            double currentGroupDuration = 0;
            double currentGroupDir1 = 1.0, currentGroupDir2 = 0.0, currentGroupDir3 = 0.0;
            bool currentGroupHasTVC = false;
            bool insideTarget = false;

            for (int i = 0; i < originalSteps.Count; i++) {
                var st = originalSteps[i];

                if (st.type == "Target") {
                    insideTarget = true;
                    currentDC = st.dc_name ?? "";
                    currentGroupDuration = 0;
                    currentGroupDir1 = 1.0; currentGroupDir2 = 0.0; currentGroupDir3 = 0.0;
                    currentGroupHasTVC = false;
                }
                else if (st.type == "EndTarget") {
                    insideTarget = false;
                }
                else if (st.type == "Vary") {
                    string vVar = st.vary_variable ?? "";
                    double val = 0;
                    if (!string.IsNullOrEmpty(currentDC) && dcSummaries.ContainsKey(currentDC) && dcSummaries[currentDC].final_variables.ContainsKey(vVar)) {
                        val = dcSummaries[currentDC].final_variables[vVar];
                    } else if (globalSolvedVars.ContainsKey(vVar)) {
                        val = globalSolvedVars[vVar];
                    }

                    if (val > 0 && (vVar.IndexOf("Duration", StringComparison.OrdinalIgnoreCase) >= 0 || vVar.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0 || vVar.IndexOf("dur", StringComparison.OrdinalIgnoreCase) >= 0)) {
                        currentGroupDuration = val;
                    }
                }
                else if (st.type == "ThrustVectorControl") {
                    currentGroupHasTVC = true;
                    currentGroupDir1 = st.thrust_dir1;
                    currentGroupDir2 = st.thrust_dir2;
                    currentGroupDir3 = st.thrust_dir3;
                    string thrKey = st.bound_thruster ?? "";

                    Dictionary<string, double> activeVars = (!string.IsNullOrEmpty(currentDC) && dcSummaries.ContainsKey(currentDC))
                        ? dcSummaries[currentDC].final_variables : globalSolvedVars;

                    foreach (var kv in activeVars) {
                        if (string.IsNullOrEmpty(thrKey) || kv.Key.IndexOf(thrKey, StringComparison.OrdinalIgnoreCase) >= 0) {
                            if (kv.Key.EndsWith("ThrustDirection1", StringComparison.OrdinalIgnoreCase)) currentGroupDir1 = kv.Value;
                            else if (kv.Key.EndsWith("ThrustDirection2", StringComparison.OrdinalIgnoreCase)) currentGroupDir2 = kv.Value;
                            else if (kv.Key.EndsWith("ThrustDirection3", StringComparison.OrdinalIgnoreCase)) currentGroupDir3 = kv.Value;
                        }
                    }
                }
                else if (st.type == "Propagate") {
                    double pDur = 0.0;
                    string sType = (st.stop_type ?? "").Trim();
                    bool isElapsed = sType.EndsWith("ElapsedSecs", StringComparison.OrdinalIgnoreCase) 
                                  || sType.EndsWith("Time", StringComparison.OrdinalIgnoreCase)
                                  || sType.EndsWith("ElapsedDays", StringComparison.OrdinalIgnoreCase);

                    if (isElapsed) {
                        double scale = sType.EndsWith("ElapsedDays", StringComparison.OrdinalIgnoreCase) ? 86400.0 : 1.0;
                        string vName = !string.IsNullOrEmpty(st.target_val_str) ? st.target_val_str.Trim() : "";
                        Dictionary<string, double> activeVars = (!string.IsNullOrEmpty(currentDC) && dcSummaries != null && dcSummaries.ContainsKey(currentDC))
                            ? dcSummaries[currentDC].final_variables : globalSolvedVars;

                        if (!string.IsNullOrEmpty(vName) && activeVars != null && activeVars.ContainsKey(vName)) {
                            pDur = activeVars[vName] * scale;
                        } else if (!string.IsNullOrEmpty(vName) && globalSolvedVars != null && globalSolvedVars.ContainsKey(vName)) {
                            pDur = globalSolvedVars[vName] * scale;
                        } else if (st.target_val > 0) {
                            pDur = st.target_val * scale;
                        } else if (st.duration > 0) {
                            pDur = st.duration * scale;
                        }
                        simClock += pDur;
                    } else {
                        // Non-elapsed event condition (e.g. Apoapsis, Periapsis, Latitude, RMAG...)
                        if (trajectory != null && trajectory.Count > 0) {
                            var nextPt = trajectory.Find(p => p.t > simClock + 0.1);
                            if (nextPt != null) {
                                simClock = nextPt.t;
                            }
                        }
                    }
                }
                else if (st.type == "FiniteBurn") {
                    double burnStartTime = simClock;

                    double dur = st.duration;
                    if (currentGroupDuration > 0) {
                        dur = currentGroupDuration;
                    } else {
                        string vName = st.burn_use_vary_var ? st.burn_vary_var_name : st.duration_str;
                        Dictionary<string, double> activeVars = (!string.IsNullOrEmpty(currentDC) && dcSummaries.ContainsKey(currentDC))
                            ? dcSummaries[currentDC].final_variables : globalSolvedVars;

                        if (!string.IsNullOrEmpty(vName) && activeVars.ContainsKey(vName)) {
                            dur = activeVars[vName];
                        } else {
                            foreach (var kv in activeVars) {
                                if (!string.IsNullOrEmpty(st.burn_name) && kv.Key.IndexOf(st.burn_name.Replace("_Burn", ""), StringComparison.OrdinalIgnoreCase) >= 0) {
                                    dur = kv.Value; break;
                                }
                            }
                        }
                    }

                    // 检索当前点火的推力方向
                    double tDir1 = currentGroupHasTVC ? currentGroupDir1 : (st.thrust_dir1 != 0 || st.thrust_dir2 != 0 || st.thrust_dir3 != 0 ? st.thrust_dir1 : 1.0);
                    double tDir2 = currentGroupHasTVC ? currentGroupDir2 : st.thrust_dir2;
                    double tDir3 = currentGroupHasTVC ? currentGroupDir3 : st.thrust_dir3;

                    double dirMag = Math.Sqrt(tDir1 * tDir1 + tDir2 * tDir2 + tDir3 * tDir3);
                    double u1 = dirMag > 0.0001 ? (tDir1 / dirMag) : 1.0;
                    double u2 = dirMag > 0.0001 ? (tDir2 / dirMag) : 0.0;
                    double u3 = dirMag > 0.0001 ? (tDir3 / dirMag) : 0.0;

                    // 动态匹配推进器模型 (优先 bound_thruster -> Burn.BoundThruster -> 最大主发动机 -> 当前活动飞船引擎)
                    double thrustKN = 0.0;
                    double ispVac = 300.0;
                    GMATThrusterModel matchedThr = null;

                    if (GMATStateManager.Current.Thrusters != null && GMATStateManager.Current.Thrusters.Count > 0) {
                        if (!string.IsNullOrEmpty(st.bound_thruster)) {
                            matchedThr = GMATStateManager.Current.Thrusters.Find(t => t.Id.Equals(st.bound_thruster, StringComparison.OrdinalIgnoreCase));
                        }
                        if (matchedThr == null && GMATStateManager.Current.Burns != null) {
                            var bDef = GMATStateManager.Current.Burns.Find(b => b.Name.Equals(st.burn_name, StringComparison.OrdinalIgnoreCase));
                            if (bDef != null && !string.IsNullOrEmpty(bDef.BoundThruster)) {
                                matchedThr = GMATStateManager.Current.Thrusters.Find(t => t.Id.Equals(bDef.BoundThruster, StringComparison.OrdinalIgnoreCase));
                            }
                        }
                        if (matchedThr == null) {
                            matchedThr = GMATStateManager.Current.Thrusters.OrderByDescending(t => t.ThrustKN).FirstOrDefault();
                        }
                    }

                    if (matchedThr != null) {
                        thrustKN = matchedThr.ThrustKN;
                        ispVac = matchedThr.IspVac > 0 ? matchedThr.IspVac : 300.0;
                    } else if (FlightGlobals.ActiveVessel != null) {
                        var engs = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleEngines>();
                        if (engs != null && engs.Count > 0) {
                            thrustKN = engs.Max(e => (double)e.maxThrust);
                            ispVac = engs.Max(e => (double)e.atmosphereCurve.Evaluate(0));
                        }
                    }

                    const double standardG0 = 9.80665;
                    double m0 = currentRunningMass;
                    double mFlow = (thrustKN > 0 && ispVac > 0) ? (thrustKN * 1000.0) / (ispVac * standardG0) : 0.0;
                    double fuelBurned = mFlow * dur;
                    double mEnd = Math.Max(10.0, m0 - fuelBurned);
                    double totalDv = (m0 > mEnd && ispVac > 0) ? (ispVac * standardG0) * Math.Log(m0 / mEnd) : 0.0;
                    currentRunningMass = mEnd;

                    double dvV = totalDv * u1;
                    double dvN = totalDv * u2;
                    double dvB = totalDv * u3;

                    var sbEntry = new SolvedBurn {
                        type = "Finite",
                        name = !string.IsNullOrEmpty(st.burn_name) ? st.burn_name : (st.label ?? $"Burn_{i + 1}"),
                        origin = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth",
                        duration = dur,
                        thrust_kn = thrustKN,
                        isp = ispVac,
                        fuel_burned = fuelBurned,
                        dv_v = dvV,
                        dv_n = dvN,
                        dv_b = dvB,
                        magnitude = totalDv,
                        epoch_offset = burnStartTime,
                        has_native_delta_v = true,
                        principia_writeable = true,
                        enable_timing_optimization = true,
                        native_result_note = $"NASA GMAT 收敛解 (所属DC: {(string.IsNullOrEmpty(currentDC) ? "默认" : currentDC)})"
                    };
                    burns.Add(sbEntry);

                    simClock += dur;
                }
                else if (st.type == "ImpulsiveBurn" || st.type == "Maneuver") {
                    double mag = Math.Sqrt(st.dv_v * st.dv_v + st.dv_n * st.dv_n + st.dv_b * st.dv_b);
                    double burnStartTime = simClock;

                    burns.Add(new SolvedBurn {
                        type = "Impulsive",
                        name = !string.IsNullOrEmpty(st.burn_name) ? st.burn_name : (st.label ?? $"Impulsive_{i + 1}"),
                        origin = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth",
                        duration = 0,
                        thrust_kn = 0,
                        isp = 0,
                        fuel_burned = 0,
                        dv_v = st.dv_v,
                        dv_n = st.dv_n,
                        dv_b = st.dv_b,
                        magnitude = mag,
                        epoch_offset = burnStartTime,
                        has_native_delta_v = true,
                        principia_writeable = true,
                        enable_timing_optimization = true
                    });
                }
            }

            return burns;
        }

        /// <summary>
        /// 将打靶结果数值实时回调同步回任务序列 GUI 步骤中
        /// </summary>
        public static void SyncSolvedValuesToSequence(
            List<MissionStep> steps,
            Dictionary<string, DCSummary> dcSummaries,
            Dictionary<string, double> globalSolvedVars,
            List<SolvedBurn> solvedBurns = null) 
        {
            if (steps == null || steps.Count == 0) return;

            string currentDC = "";
            int burnIdx = 0;

            for (int i = 0; i < steps.Count; i++) {
                var step = steps[i];
                if (step.type == "Target") {
                    currentDC = GMATParameterRegistry.SanitizeGmatIdentifier(step.dc_name ?? "DC", "DC");
                }
                else if (step.type == "EndTarget") {
                    currentDC = "";
                }

                DCSummary activeDcSummary = null;
                if (!string.IsNullOrEmpty(currentDC) && dcSummaries != null && dcSummaries.ContainsKey(currentDC)) {
                    activeDcSummary = dcSummaries[currentDC];
                }

                Dictionary<string, double> activeVars = (activeDcSummary != null)
                    ? activeDcSummary.final_variables : globalSolvedVars;

                if (step.type == "Vary") {
                    string vName = step.vary_variable ?? "";
                    string shortVName = vName.Contains(".") ? vName.Substring(vName.LastIndexOf('.') + 1) : vName;
                    double solvedVal = 0.0;
                    bool found = false;

                    if (activeVars != null && activeVars.ContainsKey(vName)) {
                        solvedVal = activeVars[vName];
                        found = true;
                    } else if (activeVars != null && activeVars.ContainsKey(shortVName)) {
                        solvedVal = activeVars[shortVName];
                        found = true;
                    } else if (globalSolvedVars != null && globalSolvedVars.ContainsKey(vName)) {
                        solvedVal = globalSolvedVars[vName];
                        found = true;
                    } else if (globalSolvedVars != null && globalSolvedVars.ContainsKey(shortVName)) {
                        solvedVal = globalSolvedVars[shortVName];
                        found = true;
                    }

                    if (found) {
                        step.has_solved_value = true;
                        step.solved_value = solvedVal;
                        step.solved_value_str = solvedVal.ToString("F4", CultureInfo.InvariantCulture);
                    }
                    if (activeDcSummary != null) {
                        step.solver_iterations = activeDcSummary.iterations;
                        step.solver_converged = activeDcSummary.converged;
                    }
                }
                else if (step.type == "Achieve") {
                    string rawParam = step.achieve_param ?? "";
                    string cleanParam = GMATParameterRegistry.StripCraftPrefix(rawParam);
                    if (cleanParam.Contains("=")) cleanParam = cleanParam.Split('=')[0].Trim();

                    DCGoalEntry matchedGoal = null;
                    if (activeDcSummary != null && activeDcSummary.goals_summary != null) {
                        foreach (var g in activeDcSummary.goals_summary) {
                            string gClean = GMATParameterRegistry.StripCraftPrefix(g.GoalName);
                            if (g.GoalName.Equals(rawParam, StringComparison.OrdinalIgnoreCase) ||
                                gClean.Equals(cleanParam, StringComparison.OrdinalIgnoreCase) ||
                                g.GoalName.EndsWith(cleanParam, StringComparison.OrdinalIgnoreCase) ||
                                cleanParam.EndsWith(gClean, StringComparison.OrdinalIgnoreCase)) {
                                matchedGoal = g;
                                break;
                            }
                        }
                        // Fallback: if only 1 goal in this DC and only 1 achieve step in target group
                        if (matchedGoal == null && activeDcSummary.goals_summary.Count == 1) {
                            matchedGoal = activeDcSummary.goals_summary[0];
                        }
                    }

                    if (matchedGoal != null) {
                        step.has_achieve_result = true;
                        step.achieved_value = matchedGoal.Achieved;
                        step.achieved_variance = matchedGoal.Achieved - matchedGoal.Desired;
                        double tol = step.achieve_tolerance > 0 ? step.achieve_tolerance : 1.0;
                        step.achieve_passed = Math.Abs(step.achieved_variance) <= tol;
                    }
                    if (activeDcSummary != null) {
                        step.solver_iterations = activeDcSummary.iterations;
                        step.solver_converged = activeDcSummary.converged;
                    }
                }
                else if (step.type == "FiniteBurn") {
                    if (solvedBurns != null && burnIdx < solvedBurns.Count) {
                        var sb = solvedBurns[burnIdx];
                        step.has_solved_value = true;
                        step.solved_value = sb.magnitude;
                        step.solved_value_str = sb.magnitude.ToString("F2", CultureInfo.InvariantCulture);
                    }
                    burnIdx++;
                }
                else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                    string thrKey = step.bound_thruster ?? "";
                    double sDir1 = step.thrust_dir1;
                    double sDir2 = step.thrust_dir2;
                    double sDir3 = step.thrust_dir3;
                    bool hasSolved = false;

                    if (activeVars != null) {
                        foreach (var kv in activeVars) {
                            if (string.IsNullOrEmpty(thrKey) || kv.Key.IndexOf(thrKey, StringComparison.OrdinalIgnoreCase) >= 0) {
                                if (kv.Key.EndsWith("ThrustDirection1", StringComparison.OrdinalIgnoreCase)) { sDir1 = kv.Value; hasSolved = true; }
                                else if (kv.Key.EndsWith("ThrustDirection2", StringComparison.OrdinalIgnoreCase)) { sDir2 = kv.Value; hasSolved = true; }
                                else if (kv.Key.EndsWith("ThrustDirection3", StringComparison.OrdinalIgnoreCase)) { sDir3 = kv.Value; hasSolved = true; }
                            }
                        }
                    }

                    if (hasSolved) {
                        step.has_solved_value = true;
                        step.solved_dir1 = sDir1;
                        step.solved_dir2 = sDir2;
                        step.solved_dir3 = sDir3;
                    }
                    if (activeDcSummary != null) {
                        step.solver_iterations = activeDcSummary.iterations;
                        step.solver_converged = activeDcSummary.converged;
                    }
                }
                else if (step.type == "ImpulsiveBurn" || step.type == "Maneuver") {
                    if (solvedBurns != null && burnIdx < solvedBurns.Count) {
                        var sb = solvedBurns[burnIdx];
                        step.has_solved_value = true;
                        step.solved_value = sb.magnitude;
                        step.solved_value_str = sb.magnitude.ToString("F2", CultureInfo.InvariantCulture);
                    }
                    burnIdx++;
                }
                else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                    string thr = step.bound_thruster ?? "";
                    if (activeVars != null) {
                        foreach (var kv in activeVars) {
                            if (string.IsNullOrEmpty(thr) || kv.Key.IndexOf(thr, StringComparison.OrdinalIgnoreCase) >= 0) {
                                if (kv.Key.EndsWith("ThrustDirection1", StringComparison.OrdinalIgnoreCase) ||
                                    kv.Key.EndsWith("ThrustDirection2", StringComparison.OrdinalIgnoreCase) ||
                                    kv.Key.EndsWith("ThrustDirection3", StringComparison.OrdinalIgnoreCase)) {
                                    step.has_solved_value = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 解析各个自定义瞬时状态报告 (*.txt) 并构建 StepReportData
        /// </summary>
        public static void ParseStepReports(string outputDir, List<MissionStep> originalSteps, MissionResult result) {
            if (result.step_reports == null) result.step_reports = new List<StepReportData>();
            if (originalSteps == null || originalSteps.Count == 0 || string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir)) return;

            for (int i = 0; i < originalSteps.Count; i++) {
                var step = originalSteps[i];
                if (step.type == "Report") {
                    string sub = GMATParameterRegistry.SanitizeGmatIdentifier(!string.IsNullOrEmpty(step.subscriber_name) ? step.subscriber_name : $"StepReport_{i + 1}", $"StepReport_{i + 1}");
                    string reportPath = Path.Combine(outputDir, $"{sub}.txt");

                    if (File.Exists(reportPath)) {
                        try {
                            string[] lines = File.ReadAllLines(reportPath);
                            string lastDataLine = null;
                            for (int l = lines.Length - 1; l >= 0; l--) {
                                string trim = lines[l].Trim();
                                if (!string.IsNullOrEmpty(trim) && (char.IsDigit(trim[0]) || trim[0] == '-' || trim[0] == '+')) {
                                    lastDataLine = trim;
                                    break;
                                }
                            }

                            if (!string.IsNullOrEmpty(lastDataLine)) {
                                string[] tokens = lastDataLine.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                if (tokens.Length >= 8) {
                                    var rep = new StepReportData {
                                        step_index = i,
                                        step_label = !string.IsNullOrEmpty(step.label) ? step.label : $"Report #{i + 1}",
                                        raw_report_text = lastDataLine
                                    };

                                    // Columns: ElapsedSecs, X, Y, Z, VX, VY, VZ, Altitude, SMA, ECC, INC, RAAN, AOP, TA, RMAG, TotalMass, FuelMass
                                    if (tokens.Length > 0) double.TryParse(tokens[0], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.elapsed_secs);
                                    if (tokens.Length > 1) double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.x);
                                    if (tokens.Length > 2) double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.y);
                                    if (tokens.Length > 3) double.TryParse(tokens[3], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.z);
                                    if (tokens.Length > 4) double.TryParse(tokens[4], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.vx);
                                    if (tokens.Length > 5) double.TryParse(tokens[5], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.vy);
                                    if (tokens.Length > 6) double.TryParse(tokens[6], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.vz);
                                    if (tokens.Length > 7) double.TryParse(tokens[7], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.altitude_km);
                                    if (tokens.Length > 8) double.TryParse(tokens[8], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.sma_km);
                                    if (tokens.Length > 9) double.TryParse(tokens[9], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.ecc);
                                    if (tokens.Length > 10) double.TryParse(tokens[10], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.inc_deg);
                                    if (tokens.Length > 11) double.TryParse(tokens[11], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.raan_deg);
                                    if (tokens.Length > 12) double.TryParse(tokens[12], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.aop_deg);
                                    if (tokens.Length > 13) double.TryParse(tokens[13], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.ta_deg);
                                    if (tokens.Length > 14) double.TryParse(tokens[14], NumberStyles.Any, CultureInfo.InvariantCulture, out rep.rmag_km);
                                    double dryMass = 0.0;
                                    if (tokens.Length > 16) double.TryParse(tokens[16], NumberStyles.Any, CultureInfo.InvariantCulture, out dryMass);
                                    rep.fuel_mass_kg = rep.total_mass_kg > dryMass ? (rep.total_mass_kg - dryMass) : 0.0;

                                    rep.vmag_km_s = Math.Sqrt(rep.vx * rep.vx + rep.vy * rep.vy + rep.vz * rep.vz);
                                    if (rep.sma_km > 0) {
                                        double rBody = rep.rmag_km > rep.altitude_km ? (rep.rmag_km - rep.altitude_km) : 6378.137;
                                        rep.periapsis_km = rep.sma_km * (1.0 - rep.ecc) - rBody;
                                        rep.apoapsis_km = rep.sma_km * (1.0 + rep.ecc) - rBody;
                                    }

                                    result.step_reports.Add(rep);
                                }
                            }
                        } catch (Exception ex) {
                            KSPGMATLog.Warn($"[GMATSolutionMapper] Failed to parse report {reportPath}: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}
