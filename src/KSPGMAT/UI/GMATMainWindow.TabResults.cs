using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private bool showTrajPointsList = false;
        private double lastCalibrationFactor = 1.0;
        private List<PrincipiaInterop.FlightPlanNode> cachedFlightPlanNodes = null;
        private float lastFlightPlanReadTime = -999f;
        private void DrawTabResults() {
            Vessel vessel = FlightGlobals.ActiveVessel;

            scrollPosResults = GUILayout.BeginScrollView(scrollPosResults, GUILayout.ExpandHeight(true));

            // ==========================================
            // 1. TOP HEADER & PRINCIPIA STATUS BAR
            // ==========================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>📊 变轨时序动力学与 Principia 实机验证明细 (Maneuver Validation & Trajectory)</b>", headerStyle);
            GUILayout.FlexibleSpace();

            // Safe throttled read of Principia Flight Plan Nodes (avoid crashing native C++ on every frame)
            if (vessel != null && (cachedFlightPlanNodes == null || Time.realtimeSinceStartup - lastFlightPlanReadTime > 2.0f)) {
                lastFlightPlanReadTime = Time.realtimeSinceStartup;
                string pMsg = "";
                PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
            }

            List<PrincipiaInterop.FlightPlanNode> pNodes = cachedFlightPlanNodes;
            bool hasPrincipiaNodes = (pNodes != null && pNodes.Count > 0);

            if (GUILayout.Button("🔄 刷新实机节点", secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(22))) {
                if (vessel != null) {
                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                    string pMsg = "";
                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                    pNodes = cachedFlightPlanNodes;
                    hasPrincipiaNodes = (pNodes != null && pNodes.Count > 0);
                    ScreenMessages.PostScreenMessage(hasPrincipiaNodes ? $"✅ 已读取到 {pNodes.Count} 个 Principia 实机节点" : "ℹ️ 游戏内当前飞船暂无飞行计划机动", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            if (GUILayout.Button("🚀 写入 Principia", actionButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                WriteBurnsToPrincipia(vessel);
                if (vessel != null) {
                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                    string pMsg = "";
                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                }
            }
            GUILayout.EndHorizontal();

            // Status Banner
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (hasPrincipiaNodes) {
                GUILayout.Label($"<color=#34d399>● Principia 飞行计划: <b>已检测到 {pNodes.Count} 个实机机动节点</b> (以游戏内 N 体物理积分为最终真值基准)</color>", labelValueStyle);
            } else {
                GUILayout.Label("<color=#f59e0b>○ Principia 飞行计划: <b>暂未检测到机动节点</b> (点击右上角「🚀 写入 Principia」即可自动在游戏中创建对应节点)</color>", labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            if (lastCalibrationFactor != 1.0) {
                GUILayout.Label($"<color=#38bdf8>📐 当前动力学推力/重力损耗校正系数: <b>{lastCalibrationFactor:F4}</b></color>", labelValueStyle);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // ==========================================
            // 2. DUAL-ENGINE SIDE-BY-SIDE VERIFICATION CARDS
            // ==========================================
            if (latestResult != null && latestResult.success) {
                if (latestResult.solved_burns.Count > 0) {
                    for (int i = 0; i < latestResult.solved_burns.Count; i++) {
                        var b = latestResult.solved_burns[i];

                        // Find corresponding Principia node (by match or index)
                        PrincipiaInterop.FlightPlanNode pNode = null;
                        if (pNodes != null && i < pNodes.Count) {
                            pNode = pNodes[i];
                        }

                        // Outer Maneuver Verification Container
                        GUILayout.BeginVertical(targetGroupBoxStyle);

                        // Card Header
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<b>🎯 机动 #{i + 1}: {(string.IsNullOrEmpty(b.name) ? "变轨机动" : b.name)}</b> <color=#94a3b8>({b.type})</color>", headerStyle);
                        GUILayout.FlexibleSpace();

                        if (pNode != null) {
                            double dt = pNode.duration - b.duration;
                            string statusColor = (Math.Abs(dt) <= 3.0) ? "#34d399" : "#f59e0b";
                            string statusText = (Math.Abs(dt) <= 3.0) ? "✅ 物理校核高吻合" : "⚠️ 存在动力学积分差";
                            GUILayout.Label($"<color={statusColor}><b>[{statusText} · Δt: {dt:+0.0;-0.0;0.0}s]</b></color>", labelValueStyle);
                        } else {
                            GUILayout.Label("<color=#64748b>[待同步至 Principia 飞行计划]</color>", labelValueStyle);
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Space(4);

                        // Side-by-Side Comparison Columns
                        GUILayout.BeginHorizontal();

                        // LEFT COLUMN: NASA GMAT Theoretical Solution
                        GUILayout.BeginVertical(nestedCardBoxStyle, GUILayout.Width(340));
                        GUILayout.Label("<color=#38bdf8><b>🛰️ NASA GMAT 理论求解</b></color>", labelValueStyle);
                        GUILayout.Space(2);
                        GUILayout.Label($"合速度增量 (ΔV): <b>{b.magnitude:F3} m/s</b>", labelValueStyle);
                        GUILayout.Label($"分量: 顺行(V) {b.dv_v:F2} | 法向(N/变倾角) {b.dv_n:F2} | 径向(B/面内) {b.dv_b:F2} m/s", labelValueStyle);
                        GUILayout.Label($"点火时长 (Duration): <color=#38bdf8><b>{b.duration:F2} s</b></color>", labelValueStyle);
                        GUILayout.Label($"预计消耗推进剂: <b>{b.fuel_burned:F2} kg</b>", labelValueStyle);
                        GUILayout.Label($"动力学模型推力: {b.thrust_kn:F1} kN | 比冲: {b.isp:F1} s", labelValueStyle);
                        if (!string.IsNullOrEmpty(b.native_result_note)) {
                            GUILayout.Label($"<color=#94a3b8><i>{b.native_result_note}</i></color>", labelValueStyle);
                        }
                        GUILayout.EndVertical();

                        GUILayout.Space(6);

                        // RIGHT COLUMN: Principia Ground-Truth In-Game Node
                        GUILayout.BeginVertical(nestedCardBoxStyle, GUILayout.ExpandWidth(true));
                        if (pNode != null) {
                            GUILayout.Label("<color=#34d399><b>🎮 Principia 游戏实机节点 (Ground Truth 真值)</b></color>", labelValueStyle);
                            GUILayout.Space(2);
                            double pDvMag = pNode.deltaV.magnitude;
                            GUILayout.Label($"实机机动合 ΔV: <b>{pDvMag:F3} m/s</b>", labelValueStyle);
                            GUILayout.Label($"Principia分量: 切向 {pNode.deltaV.x:F2} | 径向 {pNode.deltaV.y:F2} | 副法向(变倾角) {pNode.deltaV.z:F2} m/s", labelValueStyle);
                            GUILayout.Label($"实机点火持续时间: <color=#34d399><b>{pNode.duration:F2} s</b></color>", labelValueStyle);
                            GUILayout.Label($"节点初始总质量: <b>{pNode.initial_mass_tonnes:F3} t</b> (真实实时构型湿重)", labelValueStyle);
                            GUILayout.Label($"发动机实测推力: {pNode.thrust_kn:F1} kN | 实测比冲: {pNode.isp:F1} s", labelValueStyle);
                        } else {
                            GUILayout.Label("<color=#94a3b8><b>🎮 Principia 游戏实机节点</b></color>", labelValueStyle);
                            GUILayout.Space(6);
                            GUILayout.Label("<color=#64748b>游戏飞行计划中尚未建立对应的 Principia 机动节点。</color>", labelValueStyle);
                            GUILayout.Label("<color=#64748b>点击下方按钮即可一键推送，获取游戏实机 N 体积时动力学真值。</color>", labelValueStyle);
                            GUILayout.Space(6);
                            if (GUILayout.Button("🚀 写入该机动至 Principia 飞行计划 (点火质心物理对齐)", actionButtonStyle, GUILayout.Height(24))) {
                                WriteSingleBurnToPrincipia(vessel, i);
                                if (vessel != null) {
                                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                                    string pMsg = "";
                                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                                }
                            }
                        }
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();

                        // PHYSICAL MANEUVER TIMING & CENTROID COMPENSATION BREAKDOWN
                        double tgtEpoch, leadSec;
                        double solveEpochUT = (latestResult != null && latestResult.solve_ut > 0) ? latestResult.solve_ut : Planetarium.GetUniversalTime();
                        double compInitial = ComputeBurnInitialTime(vessel, b, solveEpochUT, out tgtEpoch, out leadSec);

                        GUILayout.Space(4);
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<color=#38bdf8><b>⏱️ 齐奥尔科夫斯基动力学点火时相分解 (Physical Centroid Timing):</b></color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal();
                        double utNow = Planetarium.GetUniversalTime();
                        double dtToTarget = tgtEpoch - utNow;
                        double dtToIgnition = compInitial - utNow;

                        GUILayout.Label($"GMAT数值事件时刻: <b>+{dtToTarget:F1}s</b> | 冲量质心提前量: <color=#f59e0b><b>-{leadSec:F1}s</b></color> (半点火时长) | Principia点火起始时刻: <color=#34d399><b>+{dtToIgnition:F1}s (T-{dtToIgnition / 60.0:F1}min)</b></color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();

                        // DISCREPANCY ANALYSIS & RECONCILIATION ACTIONS BAR
                        if (pNode != null) {
                            double dtDiff = pNode.duration - b.duration;
                            double dtPercent = (b.duration > 0) ? (dtDiff / b.duration) * 100.0 : 0.0;
                            double dvDiff = pNode.deltaV.magnitude - b.magnitude;
                            double curRatio = (b.duration > 0) ? (pNode.duration / b.duration) : 1.0;

                            GUILayout.Space(4);
                            GUILayout.BeginVertical(nestedCardBoxStyle);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"<b>动力学偏差:</b> 时长差 Δt: <b>{dtDiff:+0.00;-0.00;0.00} s ({dtPercent:+0.1;-0.1;0.1}%)</b> | 速度差 Δ(ΔV): <b>{dvDiff:+0.000;-0.000;0.000} m/s</b>", labelValueStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.Label("<color=#94a3b8><i>注: 偏差主要源于 Principia 在高偏心率轨道的实时重力损耗与蒸发微漂移</i></color>", labelValueStyle);
                            GUILayout.EndHorizontal();

                            GUILayout.Space(4);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("<color=#38bdf8><b>实机真值对齐处理:</b></color>", labelValueStyle, GUILayout.Width(130));

                            // Action 1: Reconcile GMAT Variable with Principia Duration
                            if (GUILayout.Button($"⚡ 以 Principia 实机时长 ({pNode.duration:F1}s) 校准 GMAT 变量初猜", successButtonStyle, GUILayout.Height(22))) {
                                int reconciledCount = 0;
                                for (int s = 0; s < missionSequence.Count; s++) {
                                    var step = missionSequence[s];
                                    if (step.type == "Vary" && (step.vary_variable.ToLower().Contains("burn") || step.vary_variable.ToLower().Contains("time"))) {
                                        step.vary_initial = pNode.duration;
                                        reconciledCount++;
                                    } else if (step.type == "FiniteBurn") {
                                        step.duration = pNode.duration;
                                        reconciledCount++;
                                    }
                                }
                                lastCalibrationFactor = curRatio;
                                ScreenMessages.PostScreenMessage($"✅ 成功将 GMAT 变量初猜与点火时长校准为 Principia 游戏实机真值: {pNode.duration:F2} s！", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                            }

                            // Action 2: Calibrate Ratio
                            if (GUILayout.Button("📐 记录推力修正比", secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                                lastCalibrationFactor = curRatio;
                                ScreenMessages.PostScreenMessage($"📐 已记录动力学校准系数: {lastCalibrationFactor:F4}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                            }

                            // Action 3: Re-apply to Principia
                            if (GUILayout.Button("🔄 重新同步此节点", secondaryButtonStyle, GUILayout.Width(120), GUILayout.Height(22))) {
                                WriteBurnsToPrincipia(vessel);
                            }

                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                        }

                        GUILayout.EndVertical();
                        GUILayout.Space(6);
                    }
                } else {
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label("本次求解未包含有限点火或脉冲机动，为纯天体引力轨道积分。", labelValueStyle);
                    GUILayout.EndVertical();
                }

                // ==========================================
                // 3. TRAJECTORY EPHEMERIS POINTS LIST (COLLAPSIBLE)
                // ==========================================
                if (latestResult.trajectory != null && latestResult.trajectory.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.BeginHorizontal();
                    string toggleText = showTrajPointsList ? "▼ 收起高精度轨迹历元点表" : "▶ 查看高精度轨迹历元点表";
                    if (GUILayout.Button(toggleText, secondaryButtonStyle, GUILayout.Width(200), GUILayout.Height(22))) {
                        showTrajPointsList = !showTrajPointsList;
                    }
                    GUILayout.Label($"<color=#94a3b8>(共采样 {latestResult.trajectory.Count} 个高精度四阶 Runge-Kutta 积分状态点)</color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    if (showTrajPointsList) {
                        GUILayout.Space(4);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<b>历元 t (s)</b>", labelValueStyle, GUILayout.Width(80));
                        GUILayout.Label("<b>X (km)</b>", labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label("<b>Y (km)</b>", labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label("<b>Z (km)</b>", labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label("<b>速度模长 (km/s)</b>", labelValueStyle, GUILayout.Width(110));
                        GUILayout.Label("<b>飞船质量 (kg)</b>", labelValueStyle, GUILayout.Width(100));
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        int maxShow = Mathf.Min(latestResult.trajectory.Count, 25);
                        for (int tp = 0; tp < maxShow; tp++) {
                            var pt = latestResult.trajectory[tp];
                            double vMag = Math.Sqrt(pt.vx * pt.vx + pt.vy * pt.vy + pt.vz * pt.vz);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"{pt.t:F1}", labelValueStyle, GUILayout.Width(80));
                            GUILayout.Label($"{pt.x:F2}", labelValueStyle, GUILayout.Width(90));
                            GUILayout.Label($"{pt.y:F2}", labelValueStyle, GUILayout.Width(90));
                            GUILayout.Label($"{pt.z:F2}", labelValueStyle, GUILayout.Width(90));
                            GUILayout.Label($"{vMag:F3}", labelValueStyle, GUILayout.Width(110));
                            GUILayout.Label($"{pt.mass:F1}", labelValueStyle, GUILayout.Width(100));
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }
                        if (latestResult.trajectory.Count > maxShow) {
                            GUILayout.Label($"<color=#64748b>... 及剩余 {latestResult.trajectory.Count - maxShow} 个轨迹点已记录于内存</color>", labelValueStyle);
                        }
                    }
                    GUILayout.EndVertical();
                }
            } else {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("<color=#64748b>尚未运行任务序列求解，暂无变轨与轨迹明细数据。</color>", labelValueStyle);
                GUILayout.Label("<color=#64748b>请在「📋 任务序列与求解」面板点击下方「▶ 运行求解并执行序列」生成高精度飞行时序。</color>", labelValueStyle);
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
        }



        double ComputeBurnInitialTime(Vessel vessel, SolvedBurn burn, double solveUT, out double targetEpoch, out double centroidLead) {
            double currentUT = Planetarium.GetUniversalTime();
            double anchorUT = (solveUT > 0) ? solveUT : currentUT;
            centroidLead = 0.0;

            // 1. Target Event Epoch from GMAT multi-body numerical integration
            targetEpoch = anchorUT + burn.epoch_offset;

            // 2. Exact Tsiolkovsky Rocket Equation for finite burn duration
            double duration = burn.duration;
            if (duration <= 0.0 && burn.thrust_kn > 0 && vessel != null && burn.magnitude > 0) {
                double massKg = vessel.totalMass * 1000.0;
                double thrustN = burn.thrust_kn * 1000.0;
                double isp = burn.isp > 0 ? burn.isp : 310.0;
                double g0 = 9.80665;
                if (thrustN > 0 && isp > 0) {
                    duration = (massKg * g0 * isp / thrustN) * (1.0 - Math.Exp(-burn.magnitude / (g0 * isp)));
                }
            }

            // 3. Universal Centroid Law: finite burn begins half-duration before the target impulsive centroid
            centroidLead = (duration > 0.0) ? (duration * 0.5) : 0.0;
            double initialTime = targetEpoch - centroidLead;

            // Handle UI pause/delay wrapping in closed periodic orbits
            Orbit orb = (vessel != null) ? vessel.orbit : null;
            if (orb != null && orb.period > 0 && orb.eccentricity < 1.0 && initialTime < currentUT + 3.0 && burn.epoch_offset < orb.period * 2.0) {
                while (initialTime < currentUT + 3.0) {
                    initialTime += orb.period;
                    targetEpoch += orb.period;
                }
            }

            burn.computed_initial_time = initialTime;
            burn.centroid_lead_time = centroidLead;
            return initialTime;
        }

        private void WriteSingleBurnToPrincipia(Vessel vessel, int burnIndex) {
            if (latestResult == null || latestResult.solved_burns == null || burnIndex < 0 || burnIndex >= latestResult.solved_burns.Count) {
                ScreenMessages.PostScreenMessage("❌ 无可用机动数据", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            var b = latestResult.solved_burns[burnIndex];
            if (!b.principia_writeable) {
                ScreenMessages.PostScreenMessage("⚠️ 该机动节点不支持写入 Principia", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            double solveUT = (latestResult.solve_ut > 0) ? latestResult.solve_ut : Planetarium.GetUniversalTime();
            double targetEpoch, centroidLead;
            double burnInitialTime = ComputeBurnInitialTime(vessel, b, solveUT, out targetEpoch, out centroidLead);

            Vector3d dv = new Vector3d(b.dv_v, b.dv_b, b.dv_n); // GMAT VNB -> Principia Frenet: X=Tangent(V), Y=In-Plane Normal(B), Z=Out-of-Plane Binormal(N)
            double thrust = b.thrust_kn > 0 ? b.thrust_kn : 50.0;
            double isp = b.isp > 0 ? b.isp : 310.0;

            KSPGMATLog.Info($"[Principia Single Write] Burn #{burnIndex + 1} ({b.name}): TargetEpoch={targetEpoch:F2}s, Lead={centroidLead:F1}s -> InitialTime={burnInitialTime:F2}s, ΔV=({dv.x:F1}, {dv.y:F1}, {dv.z:F1})m/s");

            bool ok = PrincipiaInterop.InsertOrUpdateBurn(vessel, burnInitialTime, dv, thrust, isp, burnIndex, isFrenet: true, originBodyName: b.origin);
            if (ok) {
                ScreenMessages.PostScreenMessage($"✅ 成功将机动 #{burnIndex + 1} ({b.name}) 写入 Principia（冲量质心物理对齐）！", 4.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                ScreenMessages.PostScreenMessage($"⚠️ 机动 #{burnIndex + 1} 写入被拒绝或 Principia 未就绪", 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void WriteBurnsToPrincipia(Vessel vessel) {
            if (latestResult == null || latestResult.solved_burns == null || latestResult.solved_burns.Count == 0) {
                ScreenMessages.PostScreenMessage("❌ 无可用机动数据", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            double solveUT = (latestResult.solve_ut > 0) ? latestResult.solve_ut : Planetarium.GetUniversalTime();
            int writtenCount = 0;
            for (int i = 0; i < latestResult.solved_burns.Count; i++) {
                var b = latestResult.solved_burns[i];
                if (!b.principia_writeable) continue;

                double targetEpoch, centroidLead;
                double burnInitialTime = ComputeBurnInitialTime(vessel, b, solveUT, out targetEpoch, out centroidLead);

                Vector3d dv = new Vector3d(b.dv_v, b.dv_b, b.dv_n); // GMAT VNB -> Principia Frenet: X=Tangent(V), Y=In-Plane Normal(B), Z=Out-of-Plane Binormal(N)
                double thrust = b.thrust_kn > 0 ? b.thrust_kn : 50.0;
                double isp = b.isp > 0 ? b.isp : 310.0;

                KSPGMATLog.Info($"[Principia Write] Burn #{i + 1} ({b.name}): TargetEpoch={targetEpoch:F2}s, Lead={centroidLead:F1}s -> InitialTime={burnInitialTime:F2}s, ΔV=({dv.x:F1}, {dv.y:F1}, {dv.z:F1})m/s");

                bool ok = PrincipiaInterop.InsertOrUpdateBurn(vessel, burnInitialTime, dv, thrust, isp, i, isFrenet: true, originBodyName: b.origin);
                if (ok) writtenCount++;
            }

            if (writtenCount > 0) {
                ScreenMessages.PostScreenMessage($"✅ 成功将 {writtenCount} 个机动写入 Principia 飞行计划（齐奥尔科夫斯基冲量质心物理对齐）！", 4.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                ScreenMessages.PostScreenMessage("⚠️ 机动写入被拒绝或 Principia 未就绪", 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
