using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace KSPGMAT {
    /// <summary>
    /// 任务关键节点与全时序里程碑高精提取服务
    /// (GMAT Dedicated Mission Timeline & Milestone Extraction Service)
    /// </summary>
    public static class GMATTimelineExtractor {

        /// <summary>
        /// 全局提取任务全生命周期的全部关键里程碑节点
        /// </summary>
        public static List<MissionMilestoneNode> ExtractMilestones(
            List<MissionStep> steps,
            Dictionary<string, DCSummary> dcSummaries,
            Dictionary<string, double> globalSolvedVars,
            List<TrajectoryPoint> trajectory,
            List<StepReportData> stepReports,
            double solveUT) 
        {
            var milestones = new List<MissionMilestoneNode>();
            if (steps == null || steps.Count == 0) return milestones;

            double simClock = 0.0;
            double anchorUT = (solveUT > 0) ? solveUT : Planetarium.GetUniversalTime();
            int mIndex = 0;

            // 1. Initial Mission Start Milestone
            var startNode = new MissionMilestoneNode {
                milestone_index = mIndex++,
                step_index = 0,
                label = "🚀 任务纪元起点 (Mission Epoch Start)",
                type = MilestoneType.MissionStart,
                elapsed_sim_secs = 0.0,
                ut_epoch = anchorUT,
                duration_secs = 0.0,
                details = "初始星历时刻，航天器状态积分开始"
            };
            if (trajectory != null && trajectory.Count > 0) {
                var p0 = trajectory[0];
                startNode.altitude_km = Math.Sqrt(p0.x * p0.x + p0.y * p0.y + p0.z * p0.z) - 6378.14; // Default Earth radius
                startNode.mass_kg = p0.mass;
            }
            milestones.Add(startNode);

            string currentDC = "";
            double currentGroupDuration = 0;
            double currentGroupDir1 = 1.0, currentGroupDir2 = 0.0, currentGroupDir3 = 0.0;
            bool currentGroupHasTVC = false;

            for (int i = 0; i < steps.Count; i++) {
                var st = steps[i];

                if (st.type == "Target") {
                    currentDC = st.dc_name ?? "DC";
                    currentGroupDuration = 0;
                    currentGroupDir1 = 1.0; currentGroupDir2 = 0.0; currentGroupDir3 = 0.0;
                    currentGroupHasTVC = false;

                    milestones.Add(new MissionMilestoneNode {
                        milestone_index = mIndex++,
                        step_index = i,
                        label = $"🎯 打靶求解器起点 [{currentDC}]",
                        type = MilestoneType.TargetBlockStart,
                        elapsed_sim_secs = simClock,
                        ut_epoch = anchorUT + simClock,
                        details = $"进入微分修正打靶闭环控制 ({currentDC})"
                    });
                }
                else if (st.type == "EndTarget") {
                    milestones.Add(new MissionMilestoneNode {
                        milestone_index = mIndex++,
                        step_index = i,
                        label = $"🏁 打靶求解器收敛结束 [{currentDC}]",
                        type = MilestoneType.TargetBlockEnd,
                        elapsed_sim_secs = simClock,
                        ut_epoch = anchorUT + simClock,
                        details = $"打靶目标已达成并保存状态"
                    });
                    currentDC = "";
                }
                else if (st.type == "Vary") {
                    string vVar = st.vary_variable ?? "";
                    double val = 0;
                    if (!string.IsNullOrEmpty(currentDC) && dcSummaries != null && dcSummaries.ContainsKey(currentDC) && dcSummaries[currentDC].final_variables.ContainsKey(vVar)) {
                        val = dcSummaries[currentDC].final_variables[vVar];
                    } else if (globalSolvedVars != null && globalSolvedVars.ContainsKey(vVar)) {
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

                    Dictionary<string, double> activeVars = (!string.IsNullOrEmpty(currentDC) && dcSummaries != null && dcSummaries.ContainsKey(currentDC))
                        ? dcSummaries[currentDC].final_variables : globalSolvedVars;

                    if (activeVars != null) {
                        foreach (var kv in activeVars) {
                            if (string.IsNullOrEmpty(thrKey) || kv.Key.IndexOf(thrKey, StringComparison.OrdinalIgnoreCase) >= 0) {
                                if (kv.Key.EndsWith("ThrustDirection1", StringComparison.OrdinalIgnoreCase)) currentGroupDir1 = kv.Value;
                                else if (kv.Key.EndsWith("ThrustDirection2", StringComparison.OrdinalIgnoreCase)) currentGroupDir2 = kv.Value;
                                else if (kv.Key.EndsWith("ThrustDirection3", StringComparison.OrdinalIgnoreCase)) currentGroupDir3 = kv.Value;
                            }
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

                        milestones.Add(new MissionMilestoneNode {
                            milestone_index = mIndex++,
                            step_index = i,
                            label = $"⏱️ 定时无动力滑行 (Coast {pDur:F1}s)",
                            type = MilestoneType.CoastStart,
                            elapsed_sim_secs = simClock,
                            ut_epoch = anchorUT + simClock,
                            duration_secs = pDur,
                            details = $"自由惯性滑行 {pDur:F1} 秒"
                        });

                        simClock += pDur;
                    } else {
                        // Celestial / orbital feature event (Apoapsis, Periapsis, Latitude...)
                        MilestoneType mType = MilestoneType.CustomEvent;
                        string eDesc = $"轨道特征事件: {sType}";

                        if (sType.IndexOf("Apoapsis", StringComparison.OrdinalIgnoreCase) >= 0) {
                            mType = MilestoneType.ApoapsisPassage;
                            eDesc = "到达轨道远地点 (Apoapsis Passage)";
                        } else if (sType.IndexOf("Periapsis", StringComparison.OrdinalIgnoreCase) >= 0) {
                            mType = MilestoneType.PeriapsisPassage;
                            eDesc = "到达轨道近地点 (Periapsis Passage)";
                        } else if (sType.IndexOf("Latitude", StringComparison.OrdinalIgnoreCase) >= 0 || sType.IndexOf("Node", StringComparison.OrdinalIgnoreCase) >= 0) {
                            mType = MilestoneType.EquatorialNodePassage;
                            eDesc = "过赤道面/交点 (Equatorial Node Passage)";
                        }

                        if (trajectory != null && trajectory.Count > 0) {
                            var nextPt = trajectory.Find(p => p.t > simClock + 0.1);
                            if (nextPt != null) {
                                simClock = nextPt.t;
                            }
                        }

                        milestones.Add(new MissionMilestoneNode {
                            milestone_index = mIndex++,
                            step_index = i,
                            label = $"🌐 {eDesc}",
                            type = mType,
                            elapsed_sim_secs = simClock,
                            ut_epoch = anchorUT + simClock,
                            details = eDesc
                        });
                    }
                }
                else if (st.type == "FiniteBurn") {
                    double burnStart = simClock;
                    double dur = st.duration;
                    if (currentGroupDuration > 0) {
                        dur = currentGroupDuration;
                    } else {
                        string vName = st.burn_use_vary_var ? st.burn_vary_var_name : st.duration_str;
                        Dictionary<string, double> activeVars = (!string.IsNullOrEmpty(currentDC) && dcSummaries != null && dcSummaries.ContainsKey(currentDC))
                            ? dcSummaries[currentDC].final_variables : globalSolvedVars;

                        if (!string.IsNullOrEmpty(vName) && activeVars != null && activeVars.ContainsKey(vName)) {
                            dur = activeVars[vName];
                        }
                    }

                    string burnName = !string.IsNullOrEmpty(st.burn_name) ? st.burn_name : $"Burn_{i + 1}";

                    // 1. Ignition Start Milestone
                    milestones.Add(new MissionMilestoneNode {
                        milestone_index = mIndex++,
                        step_index = i,
                        label = $"🔥 主发动机点火起燃 [{burnName}]",
                        type = MilestoneType.IgnitionStart,
                        elapsed_sim_secs = burnStart,
                        ut_epoch = anchorUT + burnStart,
                        duration_secs = dur,
                        details = $"点火时长: {dur:F2}s, 预期起燃时刻: T+{burnStart:F1}s"
                    });

                    simClock += dur;

                    // 2. Engine Cutoff Milestone
                    milestones.Add(new MissionMilestoneNode {
                        milestone_index = mIndex++,
                        step_index = i,
                        label = $"🛑 主发动机关机 (MECO) [{burnName}]",
                        type = MilestoneType.EngineCutoff,
                        elapsed_sim_secs = simClock,
                        ut_epoch = anchorUT + simClock,
                        duration_secs = 0.0,
                        details = $"机动关机时刻: T+{simClock:F1}s"
                    });
                }
                else if (st.type == "ImpulsiveBurn" || st.type == "Maneuver") {
                    string bName = !string.IsNullOrEmpty(st.burn_name) ? st.burn_name : $"Impulse_{i + 1}";
                    double mag = Math.Sqrt(st.dv_v * st.dv_v + st.dv_n * st.dv_n + st.dv_b * st.dv_b);

                    milestones.Add(new MissionMilestoneNode {
                        milestone_index = mIndex++,
                        step_index = i,
                        label = $"⚡ 瞬时脉冲机动 [{bName}]",
                        type = MilestoneType.IgnitionStart,
                        elapsed_sim_secs = simClock,
                        ut_epoch = anchorUT + simClock,
                        delta_v = mag,
                        dv_v = st.dv_v,
                        dv_n = st.dv_n,
                        dv_b = st.dv_b,
                        details = $"合ΔV: {mag:F2} m/s (V={st.dv_v:F1}, N={st.dv_n:F1}, B={st.dv_b:F1})"
                    });
                }
                else if (st.type == "StageSeparation") {
                    milestones.Add(new MissionMilestoneNode {
                        milestone_index = mIndex++,
                        step_index = i,
                        label = "🚀 级间分离 / 抛罩 (Stage Separation)",
                        type = MilestoneType.StageSeparation,
                        elapsed_sim_secs = simClock,
                        ut_epoch = anchorUT + simClock,
                        details = "级间钝化与级间分离完成"
                    });
                }
            }

            // Final Mission Completed Milestone
            milestones.Add(new MissionMilestoneNode {
                milestone_index = mIndex++,
                step_index = steps.Count,
                label = "🏁 任务规划终点 (Mission Terminal)",
                type = MilestoneType.MissionEnd,
                elapsed_sim_secs = simClock,
                ut_epoch = anchorUT + simClock,
                details = $"规划总耗时: {simClock:F1}s ({simClock / 60.0:F1} min / {simClock / 3600.0:F2} h)"
            });

            return milestones;
        }

        /// <summary>
        /// 格式化全任务时序报告为结构化清晰文本
        /// </summary>
        public static string FormatTimelineReport(List<MissionMilestoneNode> milestones) {
            if (milestones == null || milestones.Count == 0) return "暂无时序里程碑节点。";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("================================================================");
            sb.AppendLine("           NASA GMAT 任务关键节点全时序明细 (Timeline Milestones)         ");
            sb.AppendLine("================================================================");

            foreach (var m in milestones) {
                string timeStr = $"T+{(int)(m.elapsed_sim_secs / 3600):D2}h:{(int)((m.elapsed_sim_secs % 3600) / 60):D2}m:{(m.elapsed_sim_secs % 60):F1}s";
                sb.AppendLine($"[{timeStr.PadRight(16)}] {m.label}");
                if (!string.IsNullOrEmpty(m.details)) {
                    sb.AppendLine($"                    └─ {m.details}");
                }
            }
            sb.AppendLine("================================================================");
            return sb.ToString();
        }
    }
}
