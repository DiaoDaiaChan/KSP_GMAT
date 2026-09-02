using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        public enum IgnitionCorrectionMode {
            RawUnmodified = 0,      // 1. GMAT 原生未修正 (Simple geometric half duration, theoretical solve epoch)
            PrecisionDynamics = 1,  // 2. 单系统高精动力学修正 (Principia native variable-mass impulse centroid + landmark lock)
            ClosedLoopAuto = 2      // 3. 闭环二次自适应优化 (Feedback Principia live flight-plan state -> auto GMAT re-solve -> auto sync)
        }

        private IgnitionCorrectionMode currentIgnitionMode = IgnitionCorrectionMode.PrecisionDynamics;
        private bool isAutoClosedLoopRunning = false;
        private bool showTrajPointsList = false;
        private List<PrincipiaInterop.FlightPlanNode> cachedFlightPlanNodes = null;
        private float lastFlightPlanReadTime = -999f;
        private void DrawTabResults() {
            hoveredBurnIndex = nextHoveredBurnIndex;
            nextHoveredBurnIndex = -1;

            Vessel vessel = FlightGlobals.ActiveVessel;

            scrollPosResults = GUILayout.BeginScrollView(scrollPosResults, GUILayout.ExpandHeight(true));

            // ==========================================
            // 1. TOP HEADER & PRINCIPIA STATUS BAR
            // ==========================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("results_header"), headerStyle);
            GUILayout.FlexibleSpace();

            // Safe throttled read of Principia Flight Plan Nodes (avoid crashing native C++ on every frame)
            if (vessel != null && (cachedFlightPlanNodes == null || Time.realtimeSinceStartup - lastFlightPlanReadTime > 2.0f)) {
                lastFlightPlanReadTime = Time.realtimeSinceStartup;
                string pMsg = "";
                PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
            }

            List<PrincipiaInterop.FlightPlanNode> pNodes = cachedFlightPlanNodes;
            bool hasPrincipiaNodes = (pNodes != null && pNodes.Count > 0);

            if (GUILayout.Button(I18n.T("btn_refresh_nodes"), secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(22))) {
                if (vessel != null) {
                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                    string pMsg = "";
                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                    pNodes = cachedFlightPlanNodes;
                    hasPrincipiaNodes = (pNodes != null && pNodes.Count > 0);
                    ScreenMessages.PostScreenMessage(hasPrincipiaNodes ? I18n.Format("results_nodes_read", pNodes.Count) : I18n.T("results_no_nodes"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            if (GUILayout.Button(I18n.T("btn_clear_nodes"), secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
                if (vessel != null) {
                    PrincipiaInterop.ClearAllFlightPlanNodes(vessel);
                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                    string pMsg = "";
                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                    pNodes = cachedFlightPlanNodes;
                    hasPrincipiaNodes = (pNodes != null && pNodes.Count > 0);
                    ScreenMessages.PostScreenMessage(I18n.T("results_nodes_cleared"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            if (GUILayout.Button(I18n.T("btn_write_principia"), actionButtonStyle, GUILayout.Width(130), GUILayout.Height(22))) {
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
                GUILayout.Label(I18n.ColorFormat("results_principia_detected", pNodes.Count), labelValueStyle);
            } else {
                GUILayout.Label(I18n.T("results_principia_missing"), labelValueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ==========================================
            // 1.1 THREE-MODE IGNITION TIMING SELECTOR & CLOSED-LOOP ACTION BAR
            // ==========================================
            GUILayout.Space(4);
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("results_ignition_mode"), labelValueStyle, GUILayout.Width(170));

            bool isRaw = (currentIgnitionMode == IgnitionCorrectionMode.RawUnmodified);
            bool isPrec = (currentIgnitionMode == IgnitionCorrectionMode.PrecisionDynamics);
            bool isLoop = (currentIgnitionMode == IgnitionCorrectionMode.ClosedLoopAuto);

            if (GUILayout.Button(I18n.T("mode_raw"), isRaw ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                currentIgnitionMode = IgnitionCorrectionMode.RawUnmodified;
                if (latestResult != null && latestResult.solved_burns != null) {
                    foreach (var b in latestResult.solved_burns) b.enable_timing_optimization = false;
                }
                if (vessel != null) {
                    PrincipiaInterop.ClearAllFlightPlanNodes(vessel);
                    WriteBurnsToPrincipia(vessel);
                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                    string pMsg = "";
                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                }
                ScreenMessages.PostScreenMessage(I18n.T("results_switched_raw"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button(I18n.T("mode_precision"), isPrec ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                currentIgnitionMode = IgnitionCorrectionMode.PrecisionDynamics;
                if (latestResult != null && latestResult.solved_burns != null) {
                    foreach (var b in latestResult.solved_burns) b.enable_timing_optimization = true;
                }
                if (vessel != null) {
                    PrincipiaInterop.ClearAllFlightPlanNodes(vessel);
                    WriteBurnsToPrincipia(vessel);
                    lastFlightPlanReadTime = Time.realtimeSinceStartup;
                    string pMsg = "";
                    PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                }
                ScreenMessages.PostScreenMessage(I18n.T("results_switched_prec"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button(isAutoClosedLoopRunning ? I18n.T("mode_closed_loop_running") : I18n.T("mode_closed_loop"), isLoop ? tabButtonActiveStyle : successButtonStyle, GUILayout.Height(22))) {
                currentIgnitionMode = IgnitionCorrectionMode.ClosedLoopAuto;
                if (latestResult != null && latestResult.solved_burns != null) {
                    foreach (var b in latestResult.solved_burns) b.enable_timing_optimization = true;
                }
                ExecuteClosedLoopAutoRefinement(vessel);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Description of active mode
            string modeDesc = isRaw 
                ? I18n.T("results_mode1_desc")
                : isPrec 
                ? I18n.T("results_mode2_desc")
                : I18n.T("results_mode3_desc");
            GUILayout.Label(modeDesc, labelValueStyle);

            // Summary Evaluation Strip across all burns
            if (latestResult != null && latestResult.solved_burns != null && latestResult.solved_burns.Count > 0) {
                double solveEpochUT = (latestResult.solve_ut > 0) ? latestResult.solve_ut : Planetarium.GetUniversalTime();
                var firstBurn = latestResult.solved_burns[0];
                double rawTgt1 = solveEpochUT + firstBurn.epoch_offset;
                double rawLead1 = (firstBurn.duration > 0) ? (firstBurn.duration * 0.5) : 0.0;
                double rawInit1 = rawTgt1 - rawLead1;

                double tgtEpoch1, leadSec1;
                double compInit1 = ComputeBurnInitialTime(vessel, firstBurn, solveEpochUT, out tgtEpoch1, out leadSec1);

                double b1CentroidShift = leadSec1 - rawLead1;
                double b1PhaseShift = tgtEpoch1 - rawTgt1;
                double b1IgnitionDelta = compInit1 - rawInit1;

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                string statusColor = isRaw ? "#94a3b8" : "#34d399";
                string tag = isRaw ? I18n.T("results_tag_baseline") : I18n.T("results_tag_corrected");
                GUILayout.Label(I18n.ColorFormat("results_correction_compare", statusColor, tag), labelValueStyle);
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                if (isRaw) {
                    GUILayout.Label(I18n.T("results_raw_state"), labelValueStyle);
                } else {
                    string multiBurnTag = (latestResult.solved_burns.Count > 1) ? I18n.Format("results_multi_burn", latestResult.solved_burns.Count) : "";
                    GUILayout.Label(I18n.ColorFormat("results_burn1_summary", b1CentroidShift, b1PhaseShift, b1IgnitionDelta, multiBurnTag), labelValueStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
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
                        bool isBurnHovered = (hoveredBurnIndex == i);
                        GUILayout.BeginVertical(GetTargetGroupBoxStyle(i, isBurnHovered));

                        // Card Header
                        GUILayout.BeginHorizontal();
                        string burnNameDisp = string.IsNullOrEmpty(b.name) ? I18n.T("results_burn_default_name") : b.name;
                        GUILayout.Label(I18n.Format("results_burn_title", i + 1, burnNameDisp, b.type), headerStyle);
                        GUILayout.Space(10);

                        // Per-Burn Checkbox: If checked, apply ignition timing optimization (mode selected on top)
                        bool newOpt = GUILayout.Toggle(b.enable_timing_optimization, I18n.T("results_toggle_opt"), GUILayout.Height(20));
                        if (newOpt != b.enable_timing_optimization) {
                            b.enable_timing_optimization = newOpt;
                            if (vessel != null) {
                                PrincipiaInterop.ClearAllFlightPlanNodes(vessel);
                                WriteBurnsToPrincipia(vessel);
                                lastFlightPlanReadTime = Time.realtimeSinceStartup;
                                string pMsg = "";
                                PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
                            }
                        }

                        GUILayout.Space(6);
                        // Smart Recommendation Badge
                        string recText;
                        if (b.duration < 20.0) {
                            recText = I18n.T("results_rec_short");
                        } else if (b.duration < 60.0) {
                            recText = I18n.T("results_rec_medium");
                        } else {
                            recText = I18n.T("results_rec_long");
                        }
                        GUILayout.Label(recText, labelValueStyle);

                        GUILayout.FlexibleSpace();

                        if (pNode != null) {
                            double dt = pNode.duration - b.duration;
                            string statusColor = (Math.Abs(dt) <= 3.0) ? "#34d399" : "#f59e0b";
                            string statusText = (Math.Abs(dt) <= 3.0) ? I18n.T("results_phys_match") : I18n.T("results_phys_diff");
                            GUILayout.Label(I18n.ColorFormat("results_status_badge", statusColor, statusText, dt), labelValueStyle);
                        } else {
                            GUILayout.Label(I18n.T("results_pending_sync"), labelValueStyle);
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Space(4);

                        // Side-by-Side Comparison Columns
                        GUILayout.BeginHorizontal();

                        // LEFT COLUMN: NASA GMAT Theoretical Solution
                        GUILayout.BeginVertical(nestedCardBoxStyle, GUILayout.Width(350));
                        GUILayout.Label(I18n.T("results_gmat_col"), labelValueStyle);
                        GUILayout.Space(4);

                        // Hero Metric 1: Total Delta-V
                        GUILayout.Label(I18n.ColorFormat("results_dv_total", b.magnitude), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("results_dv_components", b.dv_v, b.dv_n, b.dv_b), labelValueStyle);

                        GUILayout.Space(4);
                        // Hero Metric 2: Duration
                        GUILayout.Label(I18n.ColorFormat("results_duration", b.duration), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("results_fuel_burned", b.fuel_burned), labelValueStyle);
                        GUILayout.Label(I18n.ColorFormat("results_thrust_isp", b.thrust_kn, b.isp), labelValueStyle);

                        if (!string.IsNullOrEmpty(b.native_result_note)) {
                            GUILayout.Space(2);
                            GUILayout.Label($"<color=#64748b><i>{b.native_result_note}</i></color>", labelValueStyle);
                        }
                        GUILayout.EndVertical();

                        GUILayout.Space(6);

                        // RIGHT COLUMN: Principia Ground-Truth In-Game Node
                        GUILayout.BeginVertical(nestedCardBoxStyle, GUILayout.ExpandWidth(true));
                        if (pNode != null) {
                            GUILayout.Label(I18n.T("results_principia_col"), labelValueStyle);
                            GUILayout.Space(4);

                            // Hero Metric 1: Total Delta-V
                            double pDvMag = pNode.deltaV.magnitude;
                            GUILayout.Label(I18n.ColorFormat("results_p_dv", pDvMag), labelValueStyle);
                            GUILayout.Label(I18n.ColorFormat("results_p_components", pNode.deltaV.x, pNode.deltaV.y, pNode.deltaV.z), labelValueStyle);

                            GUILayout.Space(4);
                            // Hero Metric 2: Duration & Mass
                            GUILayout.Label(I18n.ColorFormat("results_p_duration", pNode.duration), labelValueStyle);
                            GUILayout.Label(I18n.ColorFormat("results_p_mass", pNode.initial_mass_tonnes), labelValueStyle);
                            GUILayout.Label(I18n.ColorFormat("results_p_thrust", pNode.thrust_kn, pNode.isp), labelValueStyle);
                        } else {
                            GUILayout.Label(I18n.T("results_p_node_header"), labelValueStyle);
                            GUILayout.Space(6);
                            GUILayout.Label(I18n.T("results_p_no_node"), labelValueStyle);
                            GUILayout.Label(I18n.T("results_p_no_node2"), labelValueStyle);
                            GUILayout.Space(6);
                            if (GUILayout.Button(I18n.T("btn_write_single"), actionButtonStyle, GUILayout.Height(24))) {
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

                        double rawTargetEpoch = solveEpochUT + b.epoch_offset;
                        double rawLead = (b.duration > 0) ? (b.duration * 0.5) : 0.0;
                        double rawInit = rawTargetEpoch - rawLead;

                        double centroidShift = leadSec - rawLead;
                        double phaseShift = tgtEpoch - rawTargetEpoch;
                        double totalBurnShift = compInitial - rawInit;

                        GUILayout.Space(4);
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.T("results_timing_header"), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal();
                        double utNow = Planetarium.GetUniversalTime();
                        double dtToTarget = tgtEpoch - utNow;
                        double dtToIgnition = compInitial - utNow;

                        GUILayout.Label(I18n.ColorFormat("results_timing_row", dtToTarget, leadSec, dtToIgnition, dtToIgnition / 60.0), labelValueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        // EVALUATION ROW: Deviation against raw unmodified baseline
                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal();
                        if (!b.enable_timing_optimization) {
                            GUILayout.Label(I18n.T("results_state_direct"), labelValueStyle);
                        } else if (currentIgnitionMode == IgnitionCorrectionMode.RawUnmodified) {
                            GUILayout.Label(I18n.ColorFormat("results_state_mode1", rawLead), labelValueStyle);
                        } else {
                            string evalGrade = (Math.Abs(centroidShift) > 0.05 || Math.Abs(phaseShift) > 0.5) ? I18n.T("results_grade_ok") : I18n.T("results_grade_minor");
                            GUILayout.Label(I18n.ColorFormat("results_opt_active", centroidShift, rawLead, phaseShift, totalBurnShift, evalGrade), labelValueStyle);
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        // DYNAMIC INTERACTIVE TIMELINE STRIP
                        GUILayout.Space(2);
                        GUILayout.BeginHorizontal();
                        double tIgnRel = compInitial - solveEpochUT;
                        double tCentroidRel = compInitial + leadSec - solveEpochUT;
                        double tCutoffRel = compInitial + b.duration - solveEpochUT;

                        if (!b.enable_timing_optimization) {
                            GUILayout.Label(I18n.ColorFormat("results_timeline_direct", tIgnRel, tCentroidRel, tCutoffRel), labelValueStyle);
                        } else {
                            GUILayout.Label(I18n.ColorFormat("results_timeline_opt", tIgnRel, leadSec, tCentroidRel, tCutoffRel), labelValueStyle);
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.EndVertical();

                        GUILayout.EndVertical();
                        if (Event.current.type == EventType.Repaint) {
                            Rect cardRect = GUILayoutUtility.GetLastRect();
                            bool isHoveredNow = cardRect.Contains(Event.current.mousePosition);
                            if (isHoveredNow) {
                                nextHoveredBurnIndex = i;
                            }
                            Color themeColor = GetStepThemeColor("FiniteBurn");
                            float stripW = isHoveredNow ? 3.5f : 2.0f;
                            Color stripCol = isHoveredNow ? themeColor : new Color(themeColor.r, themeColor.g, themeColor.b, 0.70f);
                            GUI.DrawTexture(new Rect(cardRect.x + 1, cardRect.y + 2, stripW, cardRect.height - 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, stripCol, 0, 0);

                            if (isHoveredNow) {
                                Color glowBorder = new Color(themeColor.r, themeColor.g, themeColor.b, 0.85f);
                                GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, cardRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                                GUI.DrawTexture(new Rect(cardRect.x, cardRect.y + cardRect.height - 1, cardRect.width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                                GUI.DrawTexture(new Rect(cardRect.x + cardRect.width - 1, cardRect.y, 1, cardRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, glowBorder, 0, 0);
                            }
                        }
                        GUILayout.Space(6);
                    }
                } else {
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label(I18n.T("results_no_burn_solve"), labelValueStyle);
                    GUILayout.EndVertical();
                }

                // ==========================================
                // 3. TRAJECTORY EPHEMERIS POINTS LIST (COLLAPSIBLE)
                // ==========================================
                if (latestResult.trajectory != null && latestResult.trajectory.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.BeginHorizontal();
                    string toggleText = showTrajPointsList ? I18n.T("results_traj_toggle_close") : I18n.T("results_traj_toggle_open");
                    if (GUILayout.Button(toggleText, secondaryButtonStyle, GUILayout.Width(200), GUILayout.Height(22))) {
                        showTrajPointsList = !showTrajPointsList;
                    }
                    GUILayout.Label(I18n.ColorFormat("results_traj_count", latestResult.trajectory.Count), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    if (showTrajPointsList) {
                        GUILayout.Space(4);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(I18n.T("results_col_t"), labelValueStyle, GUILayout.Width(80));
                        GUILayout.Label(I18n.T("results_col_x"), labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label(I18n.T("results_col_y"), labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label(I18n.T("results_col_z"), labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label(I18n.T("results_col_vel"), labelValueStyle, GUILayout.Width(110));
                        GUILayout.Label(I18n.T("results_col_mass"), labelValueStyle, GUILayout.Width(100));
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
                            GUILayout.Label(I18n.ColorFormat("results_more_points", latestResult.trajectory.Count - maxShow), labelValueStyle);
                        }
                    }
                    GUILayout.EndVertical();
                }
            } else {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label(I18n.T("results_no_data"), labelValueStyle);
                GUILayout.Label(I18n.T("results_no_data2"), labelValueStyle);
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
        }

        double ComputeBurnInitialTime(Vessel vessel, SolvedBurn burn, double solveUT, out double targetEpoch, out double centroidLead, double globalTimeShift = 0.0) {
            double currentUT = Planetarium.GetUniversalTime();
            double anchorUT = (solveUT > 0) ? solveUT : currentUT;
            centroidLead = 0.0;

            double duration = burn.duration;
            double m0 = GMATStateManager.Current.Spacecraft.TotalMassKg > 0 
                ? GMATStateManager.Current.Spacecraft.TotalMassKg 
                : (vessel != null ? vessel.totalMass * 1000.0 : 1000.0);
            double thrustN = burn.thrust_kn * 1000.0;
            double isp = burn.isp > 0 ? burn.isp : 310.0;
            double g0 = 9.80665;
            double ve = isp * g0;

            if (duration <= 0.0 && thrustN > 0 && burn.magnitude > 0) {
                duration = (m0 * ve / thrustN) * (1.0 - Math.Exp(-burn.magnitude / ve));
            }

            // 1. Compute Centroid Lead Time (基线 5.3 变质量冲量质心提前量，统一由 PrincipiaInterop 服务层计算)
            if (currentIgnitionMode == IgnitionCorrectionMode.RawUnmodified) {
                centroidLead = (duration > 0.0) ? (duration * 0.5) : 0.0;
            } else {
                if (duration > 0.0 && m0 > 0.0) {
                    double fuelBurned = burn.fuel_burned > 0 ? burn.fuel_burned : (thrustN > 0 && ve > 0 ? (thrustN / ve) * duration : 0.0);
                    double mf = Math.Max(1.0, m0 - fuelBurned);
                    centroidLead = PrincipiaInterop.ComputeBurnCentroidLead(duration, m0, mf);
                } else if (duration > 0.0) {
                    centroidLead = duration * 0.5;
                }
            }

            // 2. Strict Exact GMAT Mission Epoch Mapping (Zero hidden snapping, Zero period wrapping)
            double initialTime;
            if (burn.enable_timing_optimization) {
                // User explicitly checked optimization: shift ignition start by centroid lead to achieve symmetric centroid on target
                targetEpoch = anchorUT + burn.epoch_offset + globalTimeShift;
                initialTime = targetEpoch - centroidLead;
            } else {
                // Default: 100% Pure 1:1 direct ignition start from GMAT simulation timeline (Zero offset, zero manipulation)
                initialTime = anchorUT + burn.epoch_offset + globalTimeShift;
                targetEpoch = initialTime + centroidLead;
            }

            burn.computed_initial_time = initialTime;
            burn.centroid_lead_time = centroidLead;
            return initialTime;
        }

        private void ExecuteClosedLoopAutoRefinement(Vessel vessel) {
            if (vessel == null) {
                ScreenMessages.PostScreenMessage(I18n.T("btn_no_vessel"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (latestResult == null || latestResult.solved_burns == null || latestResult.solved_burns.Count == 0) {
                ScreenMessages.PostScreenMessage(I18n.T("results_no_initial_solve"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Step 1: Push initial precision burn to Principia
            WriteBurnsToPrincipia(vessel);

            // Step 2: Read live Principia node
            lastFlightPlanReadTime = Time.realtimeSinceStartup;
            string pMsg = "";
            PrincipiaInterop.TryReadFlightPlanNodes(vessel, out cachedFlightPlanNodes, out pMsg);
            List<PrincipiaInterop.FlightPlanNode> pNodes = cachedFlightPlanNodes;

            if (pNodes == null || pNodes.Count == 0) {
                ScreenMessages.PostScreenMessage(I18n.T("results_principia_not_ready"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Step 3: Strictly 1-to-1 Burn-Indexed Reconciliation with Principia live ground truth
            int currentBurnIdx = 0;
            for (int s = 0; s < missionSequence.Count; s++) {
                var step = missionSequence[s];
                if (step.type == "Vary" && (step.vary_variable.ToLower().Contains("burn") || step.vary_variable.ToLower().Contains("time") || step.vary_variable.ToLower().Contains("dur"))) {
                    if (pNodes != null && currentBurnIdx < pNodes.Count) {
                        step.vary_initial = pNodes[currentBurnIdx].duration;
                    }
                } else if (step.type == "FiniteBurn") {
                    if (pNodes != null && currentBurnIdx < pNodes.Count) {
                        step.duration = pNodes[currentBurnIdx].duration;
                    }
                    currentBurnIdx++;
                } else if (step.type == "ImpulsiveBurn" || step.type == "Maneuver") {
                    currentBurnIdx++;
                }
            }

            // Step 4: Seamless Background GMAT Solve & Auto-Sync
            isAutoClosedLoopRunning = true;
            ScreenMessages.PostScreenMessage(I18n.T("results_closed_loop_start"), 3.0f, ScreenMessageStyle.UPPER_CENTER);

            string scriptText = GMATScriptGenerator.GenerateScript(vessel, missionSequence);
            GMATConsoleRunner.ExecuteScriptAsync(scriptText, missionSequence, (success, result) => {
                lock (mainThreadQueue) {
                    mainThreadQueue.Enqueue(() => {
                        isAutoClosedLoopRunning = false;
                        if (success && result != null && result.success) {
                            latestResult = result;
                            // Auto-sync the new refined solution into Principia on the Unity Main Thread
                            Vessel activeV = FlightGlobals.ActiveVessel;
                            if (activeV != null) {
                                WriteBurnsToPrincipia(activeV);
                            }
                            ScreenMessages.PostScreenMessage(I18n.T("results_closed_loop_done"), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                        } else {
                            ScreenMessages.PostScreenMessage(I18n.T("results_closed_loop_warn"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    });
                }
            });
        }

        private void WriteSingleBurnToPrincipia(Vessel vessel, int burnIndex) {
            if (latestResult == null || latestResult.solved_burns == null || burnIndex < 0 || burnIndex >= latestResult.solved_burns.Count) {
                ScreenMessages.PostScreenMessage(I18n.T("results_no_burn_data"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            var b = latestResult.solved_burns[burnIndex];
            if (!b.principia_writeable) {
                ScreenMessages.PostScreenMessage(I18n.T("results_not_writeable"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
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
                string optMsg = b.enable_timing_optimization ? I18n.Format("results_opt_suffix", centroidLead) : I18n.T("results_opt_none");
                ScreenMessages.PostScreenMessage(I18n.Format("results_burn_written", burnIndex + 1, b.name, optMsg), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                ScreenMessages.PostScreenMessage(I18n.Format("results_burn_rejected", burnIndex + 1), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void WriteBurnsToPrincipia(Vessel vessel) {
            if (latestResult == null || latestResult.solved_burns == null || latestResult.solved_burns.Count == 0) {
                ScreenMessages.PostScreenMessage(I18n.T("results_no_burn_data"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Always clear existing flight plan nodes first so new burns are cleanly written to indexes 0, 1, 2...
            PrincipiaInterop.ClearAllFlightPlanNodes(vessel);

            double solveUT = (latestResult.solve_ut > 0) ? latestResult.solve_ut : Planetarium.GetUniversalTime();
            double currentUT = Planetarium.GetUniversalTime();

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
                ScreenMessages.PostScreenMessage(I18n.Format("results_burns_written", writtenCount), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            } else {
                ScreenMessages.PostScreenMessage(I18n.T("results_write_rejected"), 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
