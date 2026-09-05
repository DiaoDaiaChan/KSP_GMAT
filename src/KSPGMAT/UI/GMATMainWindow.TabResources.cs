using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void EnsureDefaultResourceState() {
            GMATStateManager.Current.EnsureDefaultResources();
        }

        /// <summary>
        /// GMAT Resources Library Tab: Reconstructed to mirror the complete official GMAT Resources Tree.
        /// Covers Spacecraft, Hardware (Tanks & Thrusters), Burns, Propagators, Coordinate Systems,
        /// Solvers, Outputs, Variables/Arrays/Strings, and Event Locators with full live preview and Export/Import.
        /// </summary>
        private void DrawTabResources() {
            Vessel vessel = FlightGlobals.ActiveVessel;
            float availW = windowRect.width - 36f;
            float leftW = availW * 0.52f;
            float rightW = availW - leftW - 12f;

            // Compute true spacecraft mass budget from vessel
            double totalWetMassKg = (vessel != null) ? vessel.totalMass * 1000.0 : 0.0;
            double totalFuelMassKg = 0.0;
            if (cachedTanks != null && cachedTanks.Count > 0) {
                for (int t = 0; t < cachedTanks.Count; t++) {
                    totalFuelMassKg += cachedTanks[t].fuelMassKg;
                }
            } else if (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) {
                totalFuelMassKg = cachedEngines[selectedEngineIndex].fuelMassKg;
            }
            double totalDryMassKg = Math.Max(100.0, totalWetMassKg - totalFuelMassKg);

            GUILayout.BeginHorizontal();

            // =========================================================================
            // LEFT COLUMN: DYNAMIC GMAT RESOURCES EXPLORER (TREE INSPECTOR)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(leftW));

            // Folder Categories Bar (Matching Official GMAT Desktop Tree)
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("res_explorer_header"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_refresh_vessel"), secondaryButtonStyle, GUILayout.Height(20))) {
                cachedEngines = ROEngineInterop.ScanVesselEngines(vessel);
                cachedTanks = ROEngineInterop.ScanVesselTanks(vessel);
                ScreenMessages.PostScreenMessage(I18n.T("res_refreshed"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            string[] treeCategories = new string[] {
                I18n.T("rescat_craft"), I18n.T("rescat_hw"), I18n.T("rescat_burns"),
                I18n.T("rescat_prop"), I18n.T("rescat_coord"), I18n.T("rescat_solver"),
                I18n.T("rescat_output"), I18n.T("rescat_var"), I18n.T("rescat_event")
            };

            GUILayout.BeginHorizontal();
            for (int tc = 0; tc < 5; tc++) {
                bool isCur = (selectedResourceCategory == tc);
                if (GUILayout.Button(treeCategories[tc], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    selectedResourceCategory = tc;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            for (int tc = 5; tc < treeCategories.Length; tc++) {
                bool isCur = (selectedResourceCategory == tc);
                if (GUILayout.Button(treeCategories[tc], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    selectedResourceCategory = tc;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            EnsureDefaultResourceState();

            var valAlerts = GMATStateManager.Current.ValidationAlerts;
            if (valAlerts != null && valAlerts.Count > 0) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.Format("res_validation", valAlerts.Count), labelValueStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_dismiss"), secondaryButtonStyle, GUILayout.Width(50), GUILayout.Height(18))) {
                    valAlerts.Clear();
                }
                GUILayout.EndHorizontal();
                for (int a = 0; a < valAlerts.Count; a++) {
                    GUILayout.Label($"<color=#fca5a5>• {valAlerts[a]}</color>", labelValueStyle);
                }
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            scrollPosResources = GUILayout.BeginScrollView(scrollPosResources, GUILayout.ExpandHeight(true));

            // -------------------------------------------------------------
            // 0. SPACECRAFT (Spacecraft Sat)
            // -------------------------------------------------------------
            if (selectedResourceCategory == 0) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_sc_header"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_sync_state"), secondaryButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.SyncFromVessel(vessel);
                    ScreenMessages.PostScreenMessage(I18n.T("res_sc_synced"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                var scModel = GMATStateManager.Current.Spacecraft;

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("res_sc_coord_header"), subHeaderStyle);
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                scModel.DateFormat = DrawTextField(I18n.T("res_sc_dateformat"), scModel.DateFormat ?? "UTCGregorian", 160, 130);
                scModel.CoordinateSystem = DrawTextField(I18n.T("res_sc_coordsys"), scModel.CoordinateSystem ?? "EarthMJ2000Eq", 180, 140);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                scModel.DisplayStateType = DrawTextField(I18n.T("res_sc_state_type"), scModel.DisplayStateType ?? "Cartesian", 160, 130);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("res_sc_mass_header"), subHeaderStyle);
                GUILayout.Space(2);
                double effDryMass = scModel.DryMassKg > 0 ? scModel.DryMassKg : totalDryMassKg;
                GUILayout.BeginHorizontal();
                double newDry = DrawDoubleField(I18n.T("res_sc_dry"), effDryMass, 140, 100, "F2", "sc_dry_mass");
                if (newDry != effDryMass) scModel.DryMassKg = newDry;
                GUILayout.Label(I18n.Format("res_sc_fuel", totalFuelMassKg), labelValueStyle, GUILayout.Width(180));
                GUILayout.Label(I18n.Format("res_sc_wet", effDryMass + totalFuelMassKg), labelValueStyle);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                scModel.DragArea = DrawDoubleField(I18n.T("res_sc_dragarea"), scModel.DragArea, 140, 100, "F2", "sc_drag_area");
                scModel.Cd = DrawDoubleField(I18n.T("res_sc_cd"), scModel.Cd, 100, 70, "F2", "sc_cd");
                scModel.Cr = DrawDoubleField(I18n.T("res_sc_cr"), scModel.Cr, 100, 70, "F2", "sc_cr");
                scModel.SRPArea = DrawDoubleField(I18n.T("res_sc_srparea"), scModel.SRPArea, 140, 70, "F2", "sc_srp_area");
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("res_sc_hw_header"), subHeaderStyle);
                List<string> attachedTkNames = GMATStateManager.Current.GetAttachedTankIds();
                List<string> attachedEngNames = GMATStateManager.Current.GetAttachedThrusterIds();

                GUILayout.Label(I18n.Format("res_sc_tanks", string.Join(", ", attachedTkNames.ToArray())), labelValueStyle);
                GUILayout.Label(I18n.Format("res_sc_thrusters", string.Join(", ", attachedEngNames.ToArray())), labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 1. HARDWARE: CHEMICAL TANKS & THRUSTERS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 1) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_hw_header"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_new_tank"), actionButtonStyle, GUILayout.Height(20))) {
                    int tkIdx = GMATStateManager.Current.Tanks.Count;
                    GMATStateManager.Current.Tanks.Add(new GMATTankModel {
                        Id = $"Tank{tkIdx}",
                        StageNumber = tkIdx,
                        FuelMassKg = 1000.0,
                        DryMassKg = 100.0,
                        VolumeM3 = 2.0
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_new_thruster"), actionButtonStyle, GUILayout.Height(20))) {
                    int egIdx = GMATStateManager.Current.Thrusters.Count + 1;
                    string defTk = GMATStateManager.Current.Tanks.Count > 0 ? GMATStateManager.Current.Tanks[0].Id : "Tank0";
                    GMATStateManager.Current.Thrusters.Add(new GMATThrusterModel {
                        Id = $"EngineCustom{egIdx}",
                        RawTitle = $"Custom Thruster {egIdx}",
                        ThrustKN = 50.0,
                        IspVac = 350.0,
                        StageNumber = 0,
                        BoundTankId = defTk
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label(I18n.T("res_tanks_header"), subHeaderStyle);
                var curTanks = GMATStateManager.Current.Tanks;
                if (curTanks != null && curTanks.Count > 0) {
                    for (int tk = 0; tk < curTanks.Count; tk++) {
                        var tnk = curTanks[tk];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        tnk.Id = DrawTextField(I18n.T("res_tank_id"), tnk.Id, 70, 100);
                        tnk.StageNumber = DrawIntField(I18n.T("res_tank_stage"), tnk.StageNumber, 80, 35);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            curTanks.RemoveAt(tk);
                            tk--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        tnk.FuelMassKg = DrawDoubleField(I18n.T("res_tank_fuel"), tnk.FuelMassKg, 90, 80, "F2", $"tank_fuel_{tk}_{tnk.Id}");
                        tnk.DryMassKg = DrawDoubleField(I18n.T("res_tank_dry"), tnk.DryMassKg, 90, 70, "F2", $"tank_dry_{tk}_{tnk.Id}");
                        tnk.VolumeM3 = DrawDoubleField(I18n.T("res_tank_vol"), tnk.VolumeM3, 70, 60, "F3", $"tank_vol_{tk}_{tnk.Id}");
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label(I18n.T("res_tank_empty"), labelValueStyle);
                }

                GUILayout.Space(6);
                GUILayout.Label(I18n.T("res_thr_header"), subHeaderStyle);
                var curThrusters = GMATStateManager.Current.Thrusters;
                if (curThrusters != null && curThrusters.Count > 0) {
                    for (int eg = 0; eg < curThrusters.Count; eg++) {
                        var eng = curThrusters[eg];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        eng.Id = DrawTextField(I18n.T("res_thr_id"), eng.Id, 85, 140);
                        eng.StageNumber = DrawIntField(I18n.T("res_thr_stage"), eng.StageNumber, 75, 35);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            curThrusters.RemoveAt(eg);
                            eg--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        eng.ThrustKN = DrawDoubleField(I18n.T("res_thr_thrust"), eng.ThrustKN, 95, 75, "F2", $"thr_thrust_{eg}_{eng.Id}");
                        eng.IspVac = DrawDoubleField(I18n.T("res_thr_isp"), eng.IspVac, 85, 65, "F1", $"thr_isp_{eg}_{eng.Id}");
                        
                        // Bound Tank Switcher
                        GUILayout.Label(I18n.Format("res_thr_bound_tank", eng.BoundTankId), labelValueStyle, GUILayout.Width(130));
                        if (curTanks.Count > 1) {
                            if (GUILayout.Button(I18n.T("btn_switch"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(45))) {
                                int curTkIdx = curTanks.FindIndex(t => t.Id == eng.BoundTankId);
                                int nextTkIdx = (curTkIdx + 1) % curTanks.Count;
                                eng.BoundTankId = curTanks[nextTkIdx].Id;
                            }
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label(I18n.T("res_thr_empty"), labelValueStyle);
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 2. BURNS: FINITE BURNS & IMPULSES
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 2) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_burns_header"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_add_impulsive"), actionButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.Burns.Add(new Model.GMATBurnModel {
                        Name = $"Impulsive{GMATStateManager.Current.Burns.Count + 1}",
                        Origin = "Earth",
                        Axes = "VNB",
                        Element1 = 0.5,
                        Isp = 320.0
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label(I18n.T("res_fb_header"), subHeaderStyle);
                bool hasBurns = false;
                for (int i = 0; i < missionSequence.Count; i++) {
                    var st = missionSequence[i];
                    if (st.type == "FiniteBurn") {
                        hasBurns = true;
                        string bName = string.IsNullOrEmpty(st.burn_name) ? $"Finite_{i + 1}" : st.burn_name;
                        string boundEng = string.IsNullOrEmpty(st.bound_thruster) ? (cachedEngines != null && cachedEngines.Count > 0 ? GMATParameterRegistry.SanitizeGmatIdentifier(cachedEngines[0].title, "Engine") : "MainEngine") : st.bound_thruster;
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        st.burn_name = DrawTextField(I18n.T("res_fb_name"), bName, 90, 140);
                        st.bound_thruster = DrawTextField(I18n.T("res_fb_thruster"), boundEng, 90, 140);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        st.burn_direction = DrawTextField(I18n.T("res_fb_direction"), st.burn_direction, 100, 100);
                        st.thrust_dir1 = DrawDoubleField(I18n.T("res_fb_v"), st.thrust_dir1, 50, 50, "F3", $"fb_v_{i}_{st.burn_name}");
                        st.thrust_dir2 = DrawDoubleField(I18n.T("res_fb_n"), st.thrust_dir2, 50, 50, "F3", $"fb_n_{i}_{st.burn_name}");
                        st.thrust_dir3 = DrawDoubleField(I18n.T("res_fb_b"), st.thrust_dir3, 50, 50, "F3", $"fb_b_{i}_{st.burn_name}");
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }
                if (!hasBurns) {
                    GUILayout.Label(I18n.T("res_fb_empty"), labelValueStyle);
                }

                GUILayout.Space(6);
                GUILayout.Label(I18n.T("res_imp_header"), subHeaderStyle);
                var impulsiveBurns = GMATStateManager.Current.Burns;
                if (impulsiveBurns != null && impulsiveBurns.Count > 0) {
                    for (int ib = 0; ib < impulsiveBurns.Count; ib++) {
                        var imp = impulsiveBurns[ib];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        imp.Name = DrawTextField(I18n.T("res_imp_name"), imp.Name, 80, 120);
                        imp.Origin = DrawTextField(I18n.T("res_imp_origin"), imp.Origin, 70, 80);
                        imp.Axes = DrawTextField(I18n.T("res_imp_axes"), imp.Axes, 80, 80);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            impulsiveBurns.RemoveAt(ib);
                            ib--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        imp.Element1 = DrawDoubleField(I18n.T("res_imp_dv1"), imp.Element1, 80, 60, "F4", $"imp_v1_{ib}_{imp.Name}");
                        imp.Element2 = DrawDoubleField(I18n.T("res_imp_dv2"), imp.Element2, 80, 60, "F4", $"imp_v2_{ib}_{imp.Name}");
                        imp.Element3 = DrawDoubleField(I18n.T("res_imp_dv3"), imp.Element3, 80, 60, "F4", $"imp_v3_{ib}_{imp.Name}");
                        imp.Isp = DrawDoubleField(I18n.T("res_imp_isp"), imp.Isp, 60, 60, "F1", $"imp_isp_{ib}_{imp.Name}");
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label(I18n.T("res_imp_empty"), labelValueStyle);
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 3. PROPAGATORS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 3) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_prop_header"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_new_prop2"), actionButtonStyle, GUILayout.Height(20))) {
                    string newKey = $"CustomProp_{propagatorConfigs.Count + 1}";
                    propagatorConfigs[newKey] = new PropagatorConfig {
                        integratorType = "RungeKutta89",
                        accuracy = 1e-11,
                        minStep = 0.001,
                        maxStep = 2700.0,
                        centralBody = "Earth",
                        gravityModel = "JGM2",
                        enableSRP = true,
                        dragAtmosphereModel = "None"
                    };
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                List<string> pKeys = new List<string>(propagatorConfigs.Keys);
                for (int pr = 0; pr < pKeys.Count; pr++) {
                    string pKey = pKeys[pr];
                    var cfg = propagatorConfigs[pKey];
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.Format("res_prop_item", pKey), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(I18n.T("btn_force_model"), secondaryButtonStyle, GUILayout.Height(18))) {
                        selectedPropagatorKey = pKey;
                        showPropagatorConfigWindow = true;
                    }
                    if (pKeys.Count > 1) {
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            propagatorConfigs.Remove(pKey);
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    cfg.integratorType = DrawTextField(I18n.T("res_prop_integrator"), cfg.integratorType, 80, 130);
                    cfg.accuracy = DrawDoubleField(I18n.T("res_prop_accuracy"), cfg.accuracy, 80, 90, "E2", $"prop_acc_{pKey}");
                    cfg.minStep = DrawDoubleField(I18n.T("res_prop_min"), cfg.minStep, 90, 60, "F3", $"prop_min_{pKey}");
                    cfg.maxStep = DrawDoubleField(I18n.T("res_prop_max"), cfg.maxStep, 90, 70, "F1", $"prop_max_{pKey}");
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    cfg.centralBody = DrawTextField(I18n.T("res_prop_central"), cfg.centralBody, 80, 80);
                    cfg.gravityModel = DrawTextField(I18n.T("res_prop_gravity"), cfg.gravityModel, 80, 80);
                    cfg.dragAtmosphereModel = DrawTextField(I18n.T("res_prop_atmo"), cfg.dragAtmosphereModel, 70, 90);
                    GUILayout.Label(I18n.Format("res_prop_srp", cfg.enableSRP ? I18n.T("res_prop_on") : I18n.T("res_prop_off")), labelValueStyle, GUILayout.Width(100));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 4. COORDINATE SYSTEMS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 4) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_cs_header"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_new_cs"), actionButtonStyle, GUILayout.Height(20))) {
                    showCoordSystemWindow = true;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label(I18n.T("res_cs_std_header"), subHeaderStyle);
                string[] coords = new string[] { "EarthMJ2000Eq", "EarthFixed", "MoonInertial", "EarthMoonRot", "SunEcliptic" };
                string[] descs = new string[] {
                    I18n.T("csdesc_0"), I18n.T("csdesc_1"), I18n.T("csdesc_2"), I18n.T("csdesc_3"), I18n.T("csdesc_4")
                };
                for (int cs = 0; cs < coords.Length; cs++) {
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.Label(I18n.Format("res_cs_item", coords[cs]), labelValueStyle);
                    GUILayout.Label($"<color=#94a3b8>{descs[cs]}</color>", labelValueStyle);
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                if (GMATParameterRegistry.UserCoordinateSystems.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label(I18n.T("res_cs_custom_header"), subHeaderStyle);
                    for (int u = 0; u < GMATParameterRegistry.UserCoordinateSystems.Count; u++) {
                        var ucs = GMATParameterRegistry.UserCoordinateSystems[u];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        ucs.name = DrawTextField(I18n.T("res_cs_name"), ucs.name, 70, 120);
                        ucs.origin = DrawTextField(I18n.T("res_cs_origin"), ucs.origin, 70, 80);
                        ucs.axes = DrawTextField(I18n.T("res_cs_axes"), ucs.axes, 70, 100);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            GMATParameterRegistry.DeleteCoordinateSystem(ucs.name);
                            break;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 5. SOLVERS (DifferentialCorrectors)
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 5) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_solver_header"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_new_solver2"), actionButtonStyle, GUILayout.Height(20))) {
                    string newName = $"DC_{GMATStateManager.Current.Solvers.Count + 1}";
                    GMATStateManager.Current.Solvers.Add(new Model.GMATSolverModel {
                        Name = newName,
                        Algorithm = "NewtonRaphson",
                        MaxIterations = 50,
                        DerivativeMethod = "ForwardDifference",
                        ShowProgress = true,
                        ReportStyle = "Normal",
                        ReportFile = $"DifferentialCorrector{newName}.data"
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                var solvers = GMATStateManager.Current.Solvers;
                for (int d = 0; d < solvers.Count; d++) {
                    var sol = solvers[d];
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    
                    // Card Title Bar
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.Format("res_solver_item", sol.Name), labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (solvers.Count > 1) {
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            solvers.RemoveAt(d);
                            d--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);

                    // --- Options Group Box ---
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label(I18n.T("res_solver_options"), subHeaderStyle);
                    GUILayout.Space(2);

                    // 1. Algorithm Selector
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("res_solver_alg"), labelValueStyle, GUILayout.Width(150));
                    string[] algs = new string[] { "NewtonRaphson", "Broyden", "ModifiedBroyden" };
                    for (int a = 0; a < algs.Length; a++) {
                        bool isCur = (sol.Algorithm == algs[a]);
                        if (GUILayout.Button(algs[a], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            sol.Algorithm = algs[a];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 2. Derivative Method Selector
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("res_solver_deriv"), labelValueStyle, GUILayout.Width(150));
                    string[] dmList = new string[] { "ForwardDifference", "CentralDifference" };
                    for (int m = 0; m < dmList.Length; m++) {
                        bool isCur = (sol.DerivativeMethod == dmList[m]);
                        string desc = (dmList[m] == "ForwardDifference") ? I18n.T("res_solver_deriv_fwd") : I18n.T("res_solver_deriv_cent");
                        if (GUILayout.Button(desc, isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            sol.DerivativeMethod = dmList[m];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 3. Max Iterations Field & Quick Presets
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    sol.MaxIterations = DrawIntField(I18n.T("res_solver_maxiter"), sol.MaxIterations, 150, 60);
                    GUILayout.Space(6);
                    GUILayout.Label(I18n.T("res_preset_label"), gridLabelStyle, GUILayout.Width(35));
                    int[] iterPresets = new int[] { 25, 50, 100, 200 };
                    for (int ip = 0; ip < iterPresets.Length; ip++) {
                        if (GUILayout.Button(iterPresets[ip].ToString(), secondaryButtonStyle, GUILayout.Width(35), GUILayout.Height(18))) {
                            sol.MaxIterations = iterPresets[ip];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();

                    GUILayout.Space(3);

                    // --- Output Group Box ---
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label(I18n.T("res_solver_output"), subHeaderStyle);
                    GUILayout.Space(2);

                    // 1. Show Progress Toggle
                    GUILayout.BeginHorizontal();
                    sol.ShowProgress = GUILayout.Toggle(sol.ShowProgress, I18n.T("res_solver_showprogress"), GUILayout.Height(18));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 2. Report Style Selector
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("res_solver_repstyle"), labelValueStyle, GUILayout.Width(150));
                    string[] repStyles = new string[] { "Normal", "Concise", "Verbose", "Debug" };
                    for (int rs = 0; rs < repStyles.Length; rs++) {
                        bool isCur = (sol.ReportStyle == repStyles[rs]);
                        if (GUILayout.Button(repStyles[rs], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            sol.ReportStyle = repStyles[rs];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 3. Report File Text Field
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    string defRepFile = string.IsNullOrEmpty(sol.ReportFile) ? $"DifferentialCorrector{sol.Name}.data" : sol.ReportFile;
                    sol.ReportFile = DrawTextField(I18n.T("res_solver_repfile"), defRepFile, 150, 220);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();

                    GUILayout.EndVertical();
                    GUILayout.Space(3);
                }
                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 6. SUBSCRIBERS & REPORTS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 6) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label(I18n.T("res_sub_header"), headerStyle);
                GUILayout.Space(4);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("res_reportfile1"), labelValueStyle);
                GUILayout.Label(I18n.T("res_reportfile1_desc"), labelValueStyle);
                GUILayout.EndVertical();
                GUILayout.Space(2);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("res_reportfile2"), labelValueStyle);
                GUILayout.Label(I18n.T("res_reportfile2_desc"), labelValueStyle);
                GUILayout.EndVertical();
                GUILayout.Space(2);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("res_orbitview"), labelValueStyle);
                GUILayout.Label(I18n.T("res_orbitview_desc"), labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 7. VARIABLES, ARRAYS & STRINGS (REAL DECLARED OBJECTS ONLY)
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 7) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_var_header2"), headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(I18n.T("btn_add_var"), actionButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.Variables.Add(new Model.GMATVariableModel {
                        Name = $"Var{GMATStateManager.Current.Variables.Count + 1}",
                        InitialValue = 0.0
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_add_array"), actionButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.Arrays.Add(new Model.GMATArrayModel {
                        Name = $"Arr{GMATStateManager.Current.Arrays.Count + 1}",
                        Rows = 3,
                        Cols = 3
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button(I18n.T("btn_add_string"), actionButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.Strings.Add(new Model.GMATStringModel {
                        Name = $"Str{GMATStateManager.Current.Strings.Count + 1}",
                        Value = "Value"
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                var vars = GMATStateManager.Current.Variables;
                GUILayout.Label(I18n.T("res_var_list"), subHeaderStyle);
                if (vars.Count > 0) {
                    for (int v = 0; v < vars.Count; v++) {
                        var uvar = vars[v];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        uvar.Name = DrawTextField(I18n.T("res_var_name"), uvar.Name, 60, 140);
                        uvar.InitialValue = DrawDoubleField(I18n.T("res_var_init"), uvar.InitialValue, 50, 90, "auto", $"uvar_val_{v}_{uvar.Name}");
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            vars.RemoveAt(v);
                            v--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label(I18n.T("res_var_empty"), labelValueStyle);
                }

                var arrays = GMATStateManager.Current.Arrays;
                if (arrays.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label(I18n.T("res_arr_list"), subHeaderStyle);
                    for (int a = 0; a < arrays.Count; a++) {
                        var uarr = arrays[a];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        uarr.Name = DrawTextField(I18n.T("res_arr_name"), uarr.Name, 60, 140);
                        uarr.Rows = DrawIntField(I18n.T("res_arr_rows"), uarr.Rows, 50, 40);
                        uarr.Cols = DrawIntField(I18n.T("res_arr_cols"), uarr.Cols, 50, 40);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            arrays.RemoveAt(a);
                            a--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                var strings = GMATStateManager.Current.Strings;
                if (strings.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label(I18n.T("res_str_list"), subHeaderStyle);
                    for (int st = 0; st < strings.Count; st++) {
                        var ustr = strings[st];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        ustr.Name = DrawTextField(I18n.T("res_str_name"), ustr.Name, 60, 140);
                        ustr.Value = DrawTextField(I18n.T("res_str_value"), ustr.Value, 80, 180);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("btn_delete_small"), deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            strings.RemoveAt(st);
                            st--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 8. SPACE EVENTS & PARAMETER CATALOG
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 8) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("res_event_header"), headerStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.BeginHorizontal(nestedCardBoxStyle);
                eventSearchKeyword = DrawTextField(I18n.T("res_event_search"), eventSearchKeyword, 120, 200);
                if (!string.IsNullOrEmpty(eventSearchKeyword) && GUILayout.Button(I18n.T("ide_clear_search"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                    eventSearchKeyword = "";
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                string[] evts = new string[] {
                    I18n.T("res_evt_0"), I18n.T("res_evt_1"),
                    I18n.T("res_evt_2"), I18n.T("res_evt_3"),
                    I18n.T("res_evt_4"), I18n.T("res_evt_5"),
                    I18n.T("res_evt_6"), I18n.T("res_evt_7"),
                    I18n.T("res_evt_8"), I18n.T("res_evt_9"),
                    I18n.T("res_evt_10"), I18n.T("res_evt_11"),
                    I18n.T("res_evt_12"), I18n.T("res_evt_13")
                };

                for (int e = 0; e < evts.Length; e++) {
                    string ev = evts[e];
                    if (!string.IsNullOrEmpty(eventSearchKeyword) && ev.IndexOf(eventSearchKeyword, StringComparison.OrdinalIgnoreCase) < 0) {
                        continue;
                    }

                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#38bdf8><b>{ev}</b></color>", labelValueStyle);
                    GUILayout.FlexibleSpace();

                    string cleanParam = ev.Split(' ')[0];
                    if (cleanParam.Contains("=")) cleanParam = cleanParam.Split('=')[0].Trim();

                    if (GUILayout.Button(I18n.T("btn_insert_target"), actionButtonStyle, GUILayout.Height(18))) {
                        missionSequence.Add(new MissionStep {
                            type = "Achieve",
                            label = I18n.Format("res_evt_label_achieve", cleanParam),
                            achieve_param = cleanParam,
                            achieve_val = ev.Contains("=") ? double.Parse(ev.Split('=')[1].Split(' ')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 0.0,
                            achieve_tolerance = 0.5,
                            dc_name = GetActiveSolverNames()[0]
                        });
                        ScreenMessages.PostScreenMessage(I18n.Format("res_evt_inserted_achieve", cleanParam), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    GUILayout.Space(2);
                    if (GUILayout.Button(I18n.T("btn_insert_stop"), secondaryButtonStyle, GUILayout.Height(18))) {
                        missionSequence.Add(new MissionStep {
                            type = "Propagate",
                            label = I18n.Format("res_evt_label_prop", cleanParam),
                            propagator = "LEOProp",
                            stop_type = cleanParam,
                            target_val = ev.Contains("=") ? double.Parse(ev.Split('=')[1].Split(' ')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 0.0
                        });
                        ScreenMessages.PostScreenMessage(I18n.Format("res_evt_inserted_stop", cleanParam), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // =========================================================================
            // RIGHT COLUMN: UNIFIED BUILT-IN GMAT SCRIPT IDE & LIVE INSPECTOR
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(rightW));

            // IDE Header & Mode Switcher
            // Generate the live script based on active view mode
            string liveScript = GenerateLiveGmatScript(ideScriptViewMode);

            // Real-time synchronization check: if user hasn't made custom edits, automatically keep in sync with GUI sequence
            if (string.IsNullOrEmpty(ideCustomBuffer) || ideCustomBuffer == ideLastLiveScriptSnapshot) {
                ideCustomBuffer = liveScript;
                ideLastLiveScriptSnapshot = liveScript;
                ideHasUnsavedEdits = false;
            } else if (ideCustomBuffer != liveScript) {
                ideHasUnsavedEdits = true;
            } else {
                ideHasUnsavedEdits = false;
            }

            string currentScriptToUse = (ideViewMode != 0 && ideHasUnsavedEdits) ? ideCustomBuffer : liveScript;

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("res_ide_header"), headerStyle);
            GUILayout.Space(8);

            if (ideHasUnsavedEdits) {
                GUILayout.Label(I18n.T("res_ide_unsaved"), labelValueStyle);
                if (GUILayout.Button(I18n.T("btn_discard"), secondaryButtonStyle, GUILayout.Height(20))) {
                    ideCustomBuffer = liveScript;
                    ideLastLiveScriptSnapshot = liveScript;
                    ideHasUnsavedEdits = false;
                    ScreenMessages.PostScreenMessage(I18n.T("res_ide_reset_gui"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            } else {
                GUILayout.Label(I18n.T("res_ide_synced"), labelValueStyle);
            }

            GUILayout.FlexibleSpace();

            string[] ideModes = new string[] { I18n.T("ide_mode_full"), I18n.T("ide_mode_res"), I18n.T("ide_mode_seq") };
            for (int m = 0; m < ideModes.Length; m++) {
                bool isM = (ideScriptViewMode == m);
                if (GUILayout.Button(ideModes[m], isM ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    ideScriptViewMode = m;
                    liveScript = GenerateLiveGmatScript(ideScriptViewMode);
                    if (!ideHasUnsavedEdits) {
                        ideCustomBuffer = liveScript;
                        ideLastLiveScriptSnapshot = liveScript;
                    }
                }
            }
            GUILayout.Space(6);

            // 3-Mode Switcher: Preview, Edit, Split View
            string[] viewModes = new string[] { I18n.T("ide_view_highlight"), I18n.T("ide_view_edit"), I18n.T("ide_view_split") };
            for (int vm = 0; vm < viewModes.Length; vm++) {
                bool isVm = (ideViewMode == vm);
                if (GUILayout.Button(viewModes[vm], isVm ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    ideViewMode = vm;
                    if (!ideHasUnsavedEdits) {
                        ideCustomBuffer = liveScript;
                        ideLastLiveScriptSnapshot = liveScript;
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Action Toolbar (Save & Sync, Export, External IDE, Copy, Reset, Solve)
            GUILayout.BeginHorizontal(nestedCardBoxStyle);

            GUI.enabled = (ideViewMode != 0) && !isSavingAndSyncing;
            if (GUILayout.Button(isSavingAndSyncing ? I18n.T("btn_saving") : I18n.T("btn_save_sync"), successButtonStyle, GUILayout.Height(22))) {
                SaveAndSyncIdeScriptToSequence(currentScriptToUse, vessel);
            }
            GUI.enabled = true;

            if (GUILayout.Button(showBlueprintDrawer ? I18n.T("ide_btn_blueprints") + " ▲" : I18n.T("ide_btn_blueprints") + " ▼", showBlueprintDrawer ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                showBlueprintDrawer = !showBlueprintDrawer;
            }

            if (GUILayout.Button(I18n.T("btn_external_ide"), secondaryButtonStyle, GUILayout.Height(22))) {
                OpenScriptInExternalEditor(currentScriptToUse);
            }
            if (GUILayout.Button(I18n.T("btn_reload_external"), secondaryButtonStyle, GUILayout.Height(22))) {
                ReloadScriptFromExternalFile();
            }

            if (GUILayout.Button(I18n.T("btn_copy_code"), secondaryButtonStyle, GUILayout.Height(22))) {
                GUIUtility.systemCopyBuffer = GMATClient.SanitizeGmatScript(currentScriptToUse);
                ScreenMessages.PostScreenMessage(I18n.T("res_copy_ok"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button(I18n.T("btn_export_script2"), secondaryButtonStyle, GUILayout.Height(22))) {
                try {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string outputDir = GMATClient.GetOutputDir();
                    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                    string fullScript = (ideViewMode == 1) ? currentScriptToUse : GenerateLiveGmatScript(0);
                    string expPath = Path.Combine(outputDir, $"mission_{timestamp}.script");
                    string sanitized = GMATClient.SanitizeGmatScript(fullScript);
                    File.WriteAllText(expPath, sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_latest.script"), sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_run.script"), sanitized, new System.Text.UTF8Encoding(false));
                    ScreenMessages.PostScreenMessage(I18n.Format("res_export_ok", expPath), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage(I18n.Format("res_export_fail", ex.Message), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (GUILayout.Button(I18n.T("btn_push_gmat"), actionButtonStyle, GUILayout.Height(22))) {
                try {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string outputDir = GMATClient.GetOutputDir();
                    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                    string fullScript = (ideViewMode == 1) ? currentScriptToUse : GenerateLiveGmatScript(0);
                    string expPath = Path.Combine(outputDir, $"mission_{timestamp}.script");
                    string sanitized = GMATClient.SanitizeGmatScript(fullScript);
                    File.WriteAllText(expPath, sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_latest.script"), sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_run.script"), sanitized, new System.Text.UTF8Encoding(false));
                    GMATClient.OpenGmatDesktop(expPath);
                    ScreenMessages.PostScreenMessage(I18n.Format("res_push_ok", Path.GetFileName(expPath)), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage(I18n.Format("res_push_fail", ex.Message), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (ideViewMode != 0) {
                if (GUILayout.Button(I18n.T("btn_restore_auto"), secondaryButtonStyle, GUILayout.Height(22))) {
                    ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                    ideLastLiveScriptSnapshot = ideCustomBuffer;
                    ideHasUnsavedEdits = false;
                    ScreenMessages.PostScreenMessage(I18n.T("res_restored"), 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (isPropagating) {
                if (GUILayout.Button(I18n.T("btn_abort_solve"), deleteButtonStyle, GUILayout.Height(22))) {
                    GMATClient.AbortCurrentExecution();
                    isPropagating = false;
                    solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                    solveStageText = I18n.Format("solve_user_abort", solveTotalTime);
                    solveProgress = 0f;
                    ScreenMessages.PostScreenMessage(I18n.T("solve_user_abort_msg"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            } else {
                if (GUILayout.Button((ideViewMode != 0) ? I18n.T("ide_solve_edited") : I18n.T("btn_solve_now"), actionButtonStyle, GUILayout.Height(22))) {
                    if (ideViewMode != 0 && !string.IsNullOrEmpty(ideCustomBuffer)) {
                        TriggerPropagationSolve(ideCustomBuffer);
                    } else {
                        TriggerPropagationSolve();
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // --- 官方 7 大标准脚本范式库抽屉 (Canonical Blueprints Drawer) ---
            DrawScriptBlueprintDrawer();

            // --- 前置静态语法分析与自愈诊断条 (Pre-flight Static Linter) ---
            DrawScriptLinterStatusBar(currentScriptToUse, vessel);

            GUILayout.Space(4);

            if (ideViewMode == 0) {
                // 1. Full Syntax Highlighted Preview (Always real-time live script)
                ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
                string[] scriptLines = liveScript.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int l = 0; l < scriptLines.Length; l++) {
                    string rawLine = scriptLines[l];
                    string lineNum = $"<color=#64748b>{l + 1,3}:</color> ";
                    string coloredLine = ColorizeGmatSyntax(rawLine);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(lineNum + coloredLine, codeBlockStyle);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            } else if (ideViewMode == 1) {
                // 2. Full Free Edit Mode with Floating Autocomplete
                GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
                GUI.SetNextControlName("GmatIdeTextArea");
                ideCustomBuffer = GUILayout.TextArea(ideCustomBuffer, ideEditorStyle ?? GUI.skin.textArea, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
                GUILayout.EndScrollView();

                // Draw Floating Autocomplete Popup if typing a keyword/parameter
                DrawFloatingAutocompletePopup(vessel);
                GUILayout.EndVertical();
            } else {
                // 3. True Visual Diff & Merge Mode (Myers Diff algorithm)
                string baselineText = liveScript ?? "";
                string userText = ideCustomBuffer ?? baselineText;
                DrawScriptDiffViewer(baselineText, userText, vessel, rightW);
            }

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制现代 IDE 风格的浮动智能自动补全悬浮窗 (Floating IntelliSense Popup)
        /// 支持方向键 ↑↓ 滚动高亮选择，Enter/Tab 插入，Esc 关闭
        /// </summary>
        private void DrawFloatingAutocompletePopup(Vessel vessel) {
            TextEditor te = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (te == null || string.IsNullOrEmpty(ideCustomBuffer)) return;

            int cursor = te.cursorIndex;
            int wordStart;
            string currentWord = GetWordUnderCursor(ideCustomBuffer, cursor, out wordStart);
            ideCurrentWordUnderCursor = currentWord;
            ideCursorWordStart = wordStart;

            if (string.IsNullOrEmpty(currentWord) || currentWord.Length < 1) {
                ideSelectedCompletionIndex = 0;
                return;
            }

            var completions = GMATParameterRegistry.QueryIntelliSense(currentWord, vessel);
            if (completions == null || completions.Count == 0) {
                ideSelectedCompletionIndex = 0;
                return;
            }

            int maxItems = Mathf.Min(completions.Count, 7);
            if (ideSelectedCompletionIndex >= maxItems) ideSelectedCompletionIndex = 0;
            if (ideSelectedCompletionIndex < 0) ideSelectedCompletionIndex = maxItems - 1;

            // Handle Arrow Keys and Enter / Tab / Esc
            Event e = Event.current;
            if (e.type == EventType.KeyDown) {
                if (e.keyCode == KeyCode.DownArrow) {
                    ideSelectedCompletionIndex = (ideSelectedCompletionIndex + 1) % maxItems;
                    e.Use();
                } else if (e.keyCode == KeyCode.UpArrow) {
                    ideSelectedCompletionIndex = (ideSelectedCompletionIndex - 1 + maxItems) % maxItems;
                    e.Use();
                } else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab) {
                    if (ideSelectedCompletionIndex >= 0 && ideSelectedCompletionIndex < completions.Count) {
                        ApplyAutocompleteCompletion(completions[ideSelectedCompletionIndex].InsertText);
                        e.Use();
                        return;
                    }
                } else if (e.keyCode == KeyCode.Escape) {
                    ideCurrentWordUnderCursor = "";
                    e.Use();
                    return;
                }
            }

            // Floating Popup Container
            GUILayout.BeginVertical(nestedCardBoxStyle, GUILayout.Height(Mathf.Min(170, maxItems * 26 + 36)));
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.Format("res_autocomplete_title", currentWord), labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(I18n.T("res_autocomplete_hint"), labelValueStyle);
            GUILayout.EndHorizontal();

            ideAutocompleteScroll = GUILayout.BeginScrollView(ideAutocompleteScroll);
            for (int k = 0; k < maxItems; k++) {
                var itm = completions[k];
                bool isSelected = (k == ideSelectedCompletionIndex);
                GUILayout.BeginHorizontal();
                GUILayout.Label(isSelected ? "<color=#38bdf8>▶</color>" : itm.GetIcon(), GUILayout.Width(18));
                if (GUILayout.Button($"<b>{itm.Label}</b>  <color=#38bdf8>[{itm.TypeTag}]</color> <color=#94a3b8>{itm.Doc}</color>", isSelected ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    ApplyAutocompleteCompletion(itm.InsertText);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private string GetWordUnderCursor(string text, int cursorIndex, out int wordStartIndex) {
            wordStartIndex = cursorIndex;
            if (string.IsNullOrEmpty(text) || cursorIndex <= 0 || cursorIndex > text.Length) return "";

            int start = cursorIndex - 1;
            while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '.' || text[start] == '_')) {
                start--;
            }
            wordStartIndex = start + 1;
            int length = cursorIndex - wordStartIndex;
            if (length <= 0) return "";
            return text.Substring(wordStartIndex, length);
        }

        private void ApplyAutocompleteCompletion(string completionText) {
            if (string.IsNullOrEmpty(completionText)) return;
            TextEditor te = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (te != null && !string.IsNullOrEmpty(ideCustomBuffer)) {
                int cursor = te.cursorIndex;
                int wordStart = ideCursorWordStart;
                if (wordStart < 0 || wordStart > ideCustomBuffer.Length) wordStart = cursor;

                string before = ideCustomBuffer.Substring(0, wordStart);
                string after = (cursor <= ideCustomBuffer.Length) ? ideCustomBuffer.Substring(cursor) : "";
                ideCustomBuffer = before + completionText + after;

                // Move cursor directly to the end of inserted word
                int newCursorPos = wordStart + completionText.Length;
                te.cursorIndex = newCursorPos;
                te.selectIndex = newCursorPos;
            } else {
                InsertIntoIdeBuffer(completionText);
            }
        }

        private void OpenScriptInExternalEditor(string scriptText) {
            try {
                string outputDir = GMATClient.GetOutputDir();
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                string scriptPath = Path.Combine(outputDir, "user_custom_edit.script");
                File.WriteAllText(scriptPath, scriptText, new System.Text.UTF8Encoding(false));

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = scriptPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                ScreenMessages.PostScreenMessage(I18n.Format("res_external_opened", Path.GetFileName(scriptPath)), 3.5f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                try {
                    string scriptPath = Path.Combine(GMATClient.GetOutputDir(), "user_custom_edit.script");
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{scriptPath}\"");
                    ScreenMessages.PostScreenMessage(I18n.T("res_external_located"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception) {
                    ScreenMessages.PostScreenMessage(I18n.Format("res_external_fail", ex.Message), 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        private void ReloadScriptFromExternalFile() {
            try {
                string scriptPath = Path.Combine(GMATClient.GetOutputDir(), "user_custom_edit.script");
                if (File.Exists(scriptPath)) {
                    string loaded = File.ReadAllText(scriptPath, System.Text.Encoding.UTF8);
                    ideCustomBuffer = loaded;
                    ideViewMode = 1; // Switch to Edit mode
                    SaveAndSyncIdeScriptToSequence(loaded, FlightGlobals.ActiveVessel);
                } else {
                    ScreenMessages.PostScreenMessage(I18n.T("res_reload_missing"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage(I18n.Format("res_reload_fail", ex.Message), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void SaveAndSyncIdeScriptToSequence(string scriptContent, Vessel vessel) {
            if (string.IsNullOrEmpty(scriptContent)) return;
            isSavingAndSyncing = true;
            try {
                // Ensure full script is passed to import
                string fullScript = scriptContent;
                if (ideScriptViewMode == 1) { // Resources only
                    fullScript = scriptContent.TrimEnd() + "\n\nBeginMissionSequence;\n" + GenerateLiveGmatScript(2);
                } else if (ideScriptViewMode == 2) { // Sequence only
                    string seqBody = scriptContent.Trim();
                    if (!seqBody.StartsWith("BeginMissionSequence;", StringComparison.OrdinalIgnoreCase)) {
                        seqBody = "BeginMissionSequence;\n" + seqBody;
                    }
                    fullScript = GenerateLiveGmatScript(1).TrimEnd() + "\n\n" + seqBody;
                }

                string tempScriptPath = Path.Combine(GMATClient.GetOutputDir(), "user_custom_edit.script");
                File.WriteAllText(tempScriptPath, fullScript, new System.Text.UTF8Encoding(false));

                GMATClient.ImportScript(tempScriptPath, vessel, (ok, result, msg) => {
                    isSavingAndSyncing = false;
                    if (ok && result != null) {
                        ApplyImportResultToState(result);

                        ideLastLiveScriptSnapshot = GenerateLiveGmatScript(ideScriptViewMode);
                        ideCustomBuffer = ideLastLiveScriptSnapshot;
                        ideHasUnsavedEdits = false;

                        ScreenMessages.PostScreenMessage(I18n.Format("res_saved_msg", (result.sequence != null ? result.sequence.Count : 0)), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    } else {
                        ideCustomBuffer = scriptContent;
                        ScreenMessages.PostScreenMessage(I18n.T("res_saved_kept"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                });
            } catch (Exception ex) {
                isSavingAndSyncing = false;
                ScreenMessages.PostScreenMessage(I18n.Format("res_save_fail", ex.Message), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ApplyImportResultToState(GMATImportResult result) {
            if (result == null) return;

            if (result.sequence != null && result.sequence.Count > 0) {
                missionSequence = result.sequence;
                ClearSolutionResults();
            }

            if (GMATStateManager.Current != null) {
                // 1. Solvers
                if (result.solver_models != null && result.solver_models.Count > 0) {
                    GMATStateManager.Current.Solvers = new List<Model.GMATSolverModel>(result.solver_models);
                } else if (result.solvers != null && result.solvers.Count > 0) {
                    GMATStateManager.Current.Solvers.Clear();
                    for (int s = 0; s < result.solvers.Count; s++) {
                        string sName = result.solvers[s];
                        GMATStateManager.Current.Solvers.Add(new Model.GMATSolverModel {
                            Name = sName,
                            Algorithm = "NewtonRaphson",
                            DerivativeMethod = "ForwardDifference",
                            MaxIterations = 50,
                            ReportStyle = "Normal"
                        });
                    }
                }

                // 2. User Variables
                if (result.user_variables != null && result.user_variables.Count > 0) {
                    GMATStateManager.Current.Variables.Clear();
                    for (int uv = 0; uv < result.user_variables.Count; uv++) {
                        GMATStateManager.Current.Variables.Add(new Model.GMATVariableModel {
                            Name = result.user_variables[uv].name,
                            InitialValue = result.user_variables[uv].value,
                            Description = result.user_variables[uv].description
                        });
                    }
                }

                // 3. User Arrays
                if (result.user_arrays != null && result.user_arrays.Count > 0) {
                    GMATStateManager.Current.Arrays.Clear();
                    for (int ua = 0; ua < result.user_arrays.Count; ua++) {
                        GMATStateManager.Current.Arrays.Add(new Model.GMATArrayModel {
                            Name = result.user_arrays[ua].name,
                            Rows = result.user_arrays[ua].rows,
                            Cols = result.user_arrays[ua].cols,
                            Description = result.user_arrays[ua].description
                        });
                    }
                }

                // 4. User Strings
                if (result.user_strings != null && result.user_strings.Count > 0) {
                    GMATStateManager.Current.Strings.Clear();
                    for (int us = 0; us < result.user_strings.Count; us++) {
                        GMATStateManager.Current.Strings.Add(new Model.GMATStringModel {
                            Name = result.user_strings[us].name,
                            Value = result.user_strings[us].value,
                            Description = result.user_strings[us].description
                        });
                    }
                }

                // 5. Spacecraft & Hardware
                if (result.craft_dry_mass > 0) GMATStateManager.Current.Spacecraft.DryMassKg = result.craft_dry_mass;
                if (result.craft_drag_area > 0) GMATStateManager.Current.Spacecraft.DragArea = result.craft_drag_area;
                if (result.craft_cd > 0) GMATStateManager.Current.Spacecraft.Cd = result.craft_cd;
                if (result.craft_cr > 0) GMATStateManager.Current.Spacecraft.Cr = result.craft_cr;
                if (result.craft_srp_area > 0) GMATStateManager.Current.Spacecraft.SRPArea = result.craft_srp_area;
                if (!string.IsNullOrEmpty(result.craft_epoch)) GMATStateManager.Current.Spacecraft.EpochUtc = result.craft_epoch;
                if (!string.IsNullOrEmpty(result.craft_coord_system)) GMATStateManager.Current.Spacecraft.CoordinateSystem = result.craft_coord_system;

                if (result.craft_x != 0 || result.craft_y != 0 || result.craft_z != 0) {
                    GMATStateManager.Current.Spacecraft.PosKm = new Vector3d(result.craft_x, result.craft_y, result.craft_z);
                    GMATStateManager.Current.Spacecraft.VelKms = new Vector3d(result.craft_vx, result.craft_vy, result.craft_vz);
                }

                if (result.tanks != null && result.tanks.Count > 0) {
                    GMATStateManager.Current.Tanks = result.tanks;
                } else if (FlightGlobals.ActiveVessel != null) {
                    GMATStateManager.Current.SyncFromVessel(FlightGlobals.ActiveVessel);
                }

                if (result.thrusters != null && result.thrusters.Count > 0) {
                    GMATStateManager.Current.Thrusters = result.thrusters;
                }
            }
        }

        private void InsertIntoIdeBuffer(string textToInsert) {
            if (string.IsNullOrEmpty(textToInsert)) return;
            if (ideCustomBuffer == null) ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);

            if (!ideCustomBuffer.EndsWith("\n") && !ideCustomBuffer.EndsWith("\r")) {
                ideCustomBuffer += "\n";
            }
            ideCustomBuffer += textToInsert + "\n";
            ScreenMessages.PostScreenMessage(I18n.Format("res_inserted", textToInsert.Split('\n')[0]), 2.0f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
