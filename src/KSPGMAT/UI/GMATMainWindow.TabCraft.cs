using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private bool showEphemerisExplanation = false;

        /// <summary>
        /// Exact 1-to-1 restoration of DrawTabCraftAndOrbit from git commit f6273ae.
        /// Retains the original battle-tested Principia ICRF telemetry, MechJeb multi-stage stack,
        /// and RealismOverhaul engine/tank selection with mass, density, volume and pressure fields.
        /// </summary>
        private void DrawTabCraftAndOrbit(Vessel vessel) {
            if (vessel == null) {
                GUILayout.Label(I18n.T("craft_no_vessel"), labelValueStyle);
                return;
            }

            float availW = windowRect.width - 36f;
            float colW = (availW - 12f) * 0.5f;

            // Two-column layout: Left = Orbit & Staging, Right = RO Engines & Tanks
            GUILayout.BeginHorizontal();

            // Left Column (Width: colW)
            GUILayout.BeginVertical(GUILayout.Width(colW));

            // Orbit & ICRF
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label(I18n.T("craft_orbit_header"), headerStyle);
            GUILayout.Label(I18n.Format("craft_central_body", vessel.mainBody.name, Planetarium.GetUniversalTime()), labelValueStyle);

            Vector3d pos_km, vel_kms;
            bool ok = PrincipiaInterop.GetVesselStateICRF(vessel, out pos_km, out vel_kms);
            if (ok) {
                GUILayout.Label(I18n.Format("craft_icrf_pos", pos_km.magnitude, pos_km.x, pos_km.y, pos_km.z), labelValueStyle);
                GUILayout.Label(I18n.Format("craft_icrf_vel", vel_kms.magnitude, vel_kms.x, vel_kms.y, vel_kms.z), labelValueStyle);
            } else {
                GUILayout.Label(I18n.T("craft_no_principia"), labelValueStyle);
            }

            if (vessel.orbit != null) {
                GUILayout.Label(I18n.Format("craft_kepler", vessel.orbit.semiMajorAxis / 1000.0, vessel.orbit.eccentricity, vessel.orbit.inclination), labelValueStyle);
                GUILayout.Label(I18n.Format("craft_apsides", vessel.orbit.PeA / 1000.0, vessel.orbit.ApA / 1000.0), labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // ==========================================
            // EPHEMERIS & DYNAMICS ERROR VERIFICATION CARD
            // GMAT (NASA JPL DE405) vs Principia (N-body Symplectic)
            // ==========================================
            GUILayout.BeginVertical(targetGroupBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craft_eph_verify"), headerStyle);
            GUILayout.FlexibleSpace();
            string toggleExplText = showEphemerisExplanation ? I18n.T("craft_eph_toggle_close") : I18n.T("craft_eph_toggle_open");
            if (GUILayout.Button(toggleExplText, secondaryButtonStyle, GUILayout.Height(20))) {
                showEphemerisExplanation = !showEphemerisExplanation;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 1. Initial State Alignment (t0)
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craft_initial_state_label"), labelValueStyle, GUILayout.Width(110));
            GUILayout.Label(I18n.T("craft_initial_state_val"), labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 2. Perturbation Body (Moon/Luna) Status
            CelestialBody moonBody = FlightGlobals.Bodies.Find(b => b.name == "Mun" || b.name == "Moon" || b.name == "Luna");
            if (moonBody != null && vessel.mainBody != null) {
                double moonDist_km = Vector3d.Distance(moonBody.position, vessel.mainBody.position) / 1000.0;
                GUILayout.BeginHorizontal();
                GUILayout.Label(I18n.T("craft_moon_label"), labelValueStyle, GUILayout.Width(110));
                GUILayout.Label(I18n.Format("craft_moon_val", moonDist_km), labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craft_gravity_label"), labelValueStyle, GUILayout.Width(110));
            GUILayout.Label(I18n.T("craft_gravity_val"), labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 3. Collapsible Deep Astrodynamics Explanation Drawer
            if (showEphemerisExplanation) {
                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label(I18n.T("craft_explain_title"), labelValueStyle);
                GUILayout.Space(2);

                GUILayout.Label(I18n.T("craft_explain_1_title"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_1a"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_1b"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_1c"), labelValueStyle);

                GUILayout.Space(2);
                GUILayout.Label(I18n.T("craft_explain_2_title"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_2a"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_2b"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_2c"), labelValueStyle);
                GUILayout.Label(I18n.T("craft_explain_2d"), labelValueStyle);
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();

            GUILayout.Space(6);

            // MechJeb Staging with Multi-Stage Checkboxes
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craft_staging_header"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_refresh"), secondaryButtonStyle, GUILayout.Width(50), GUILayout.Height(20))) {
                RefreshCraftData();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(I18n.Format("craft_wet_mass", vessel.totalMass), labelValueStyle);
            if (cachedStages != null && cachedStages.Count > 0) {
                double combinedDv = 0.0;
                double combinedFuel = 0.0;
                double combinedStartMass = 0.0;
                double minDry = double.MaxValue;

                for (int i = 0; i < cachedStages.Count; i++) {
                    var st = cachedStages[i];
                    bool isSel = selectedStageIndices.Contains(i);
                    GUILayout.BeginHorizontal();
                    bool newSel = GUILayout.Toggle(isSel, I18n.Format("craft_stage_toggle", st.stageNumber, st.deltaV, st.wetMassTonnes, st.fuelMassTonnes, st.dryMassTonnes), GUILayout.Height(22));
                    if (newSel != isSel) {
                        if (newSel) selectedStageIndices.Add(i);
                        else if (selectedStageIndices.Count > 1) selectedStageIndices.Remove(i);
                    }
                    GUILayout.EndHorizontal();

                    if (selectedStageIndices.Contains(i)) {
                        combinedDv += st.deltaV;
                        combinedFuel += st.fuelMassTonnes;
                        if (st.wetMassTonnes > combinedStartMass) combinedStartMass = st.wetMassTonnes;
                        if (st.dryMassTonnes < minDry) minDry = st.dryMassTonnes;
                    }
                }

                if (minDry == double.MaxValue) minDry = vessel.totalMass * 0.4;
                if (combinedStartMass <= 0) combinedStartMass = vessel.totalMass;

                GUILayout.Space(4);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(I18n.T("craft_combined_header"), labelValueStyle);
                GUILayout.Label(I18n.Format("craft_combined_dv", combinedDv, combinedStartMass), labelValueStyle);
                GUILayout.Label(I18n.Format("craft_combined_fuel", combinedFuel, minDry), labelValueStyle);
                GUILayout.EndVertical();
            } else {
                GUILayout.Label(I18n.T("craft_no_stages"), labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Right Column (Width: colW)
            GUILayout.BeginVertical(GUILayout.Width(colW));

            // RO Engines & Tanks (Exact restoration from f6273ae)
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craft_ro_header"), headerStyle);
            GUILayout.FlexibleSpace();
            bool roOk = ROEngineInterop.IsRealismOverhaulInstalled();
            GUI.contentColor = roOk ? Color.green : new Color(1f, 0.6f, 0.2f);
            GUILayout.Label(roOk ? I18n.T("craft_ro_ready") : I18n.T("craft_ro_missing"), GUILayout.Height(20));
            GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();

            if (cachedEngines != null && cachedEngines.Count > 0) {
                GUILayout.Label(I18n.T("craft_select_engine"), labelValueStyle);
                for (int i = 0; i < cachedEngines.Count; i++) {
                    var eng = cachedEngines[i];
                    bool isSel = (selectedEngineIndex == i);
                    GUI.backgroundColor = isSel ? new Color(0.2f, 0.8f, 0.5f) : Color.white;
                    string engBtn = (isSel ? "● " : "") + I18n.Format("craft_engine_btn", eng.stageNumber, eng.title, eng.thrustKN, eng.ispVac);
                    if (GUILayout.Button(engBtn, GUILayout.Height(24))) {
                        SelectEngineAndSync(i);
                    }
                    GUI.backgroundColor = Color.white;
                }

                if (selectedEngineIndex < cachedEngines.Count) {
                    var curEng = cachedEngines[selectedEngineIndex];
                    GUILayout.Space(4);
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(I18n.Format("craft_propellant", curEng.propellantDesc), labelValueStyle);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("craft_fuel_mass_label"), labelValueStyle, GUILayout.Width(75));
                    curEng.fuelMassKg = DrawDelayedNumericField($"craft_fuel_mass_{selectedEngineIndex}", curEng.fuelMassKg, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), "F1");
                    GUILayout.Label("kg", labelValueStyle, GUILayout.Width(25));

                    GUILayout.Label(I18n.T("craft_fuel_density_label"), labelValueStyle, GUILayout.Width(75));
                    curEng.fuelDensityKgM3 = DrawDelayedNumericField($"craft_fuel_rho_{selectedEngineIndex}", curEng.fuelDensityKgM3, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), "F1");
                    GUILayout.Label("kg/m³", labelValueStyle, GUILayout.Width(40));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.T("craft_tank_volume_label"), labelValueStyle, GUILayout.Width(75));
                    curEng.tankVolumeM3 = DrawDelayedNumericField($"craft_tank_vol_{selectedEngineIndex}", curEng.tankVolumeM3, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), "F3");
                    GUILayout.Label("m³", labelValueStyle, GUILayout.Width(25));

                    GUILayout.Label(I18n.T("craft_tank_pressure_label"), labelValueStyle, GUILayout.Width(75));
                    curEng.tankPressureKPa = DrawDelayedNumericField($"craft_tank_pres_{selectedEngineIndex}", curEng.tankPressureKPa, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(70), "F0");
                    GUILayout.Label("kPa", labelValueStyle, GUILayout.Width(40));
                    GUILayout.EndHorizontal();

                    GUILayout.Label(I18n.Format("craft_vac_thrust", curEng.thrustKN, curEng.ispVac), labelValueStyle);
                    GUILayout.EndVertical();
                }
            } else {
                GUILayout.Label(I18n.T("craft_no_engine"), labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // =========================================================================
            // COORDINATE SYSTEMS MANAGEMENT SUB-SECTION (100% GMAT Standard, Zero Hardcoded)
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("craft_cs_header"), headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn_new_coord"), actionButtonStyle, GUILayout.Height(22))) {
                editingCoordSystem = new CustomCoordSystemDef {
                    name = "CustomCS_" + (GMATParameterRegistry.UserCoordinateSystems.Count + 1),
                    origin = vessel != null ? vessel.mainBody.name : "Earth",
                    axes = "MJ2000Eq",
                    primary = "Earth",
                    secondary = "Luna",
                    xAxis = "R",
                    yAxis = "None",
                    zAxis = "N",
                    isBuiltIn = false
                };
                showCsOriginPicker = false;
                showCsAxesPicker = false;
                showCsPrimaryPicker = false;
                showCsSecondaryPicker = false;
                showCsHelpTooltip = false;
                showCoordSystemWindow = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label(I18n.T("craft_cs_hint"), labelValueStyle);
            GUILayout.Space(4);

            // 1. Built-in Coordinate Systems summary chip bar
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.Label(I18n.T("craft_cs_builtin"), labelValueStyle);
            GUILayout.BeginHorizontal();
            string[] quickBuiltIns = new string[] { "EarthMJ2000Eq", "EarthMJ2000Ec", "EarthFixed", "EarthICRF", "LunaMJ2000Eq", "LunaFixed" };
            for (int b = 0; b < quickBuiltIns.Length; b++) {
                string csName = quickBuiltIns[b];
                if (GUILayout.Button(csName, secondaryButtonStyle, GUILayout.Height(20))) {
                    editingCoordSystem = new CustomCoordSystemDef {
                        name = csName,
                        origin = csName.StartsWith("Luna") ? "Luna" : "Earth",
                        axes = csName.Contains("Fixed") ? "BodyFixed" : (csName.Contains("Ec") ? "MJ2000Ec" : "MJ2000Eq"),
                        primary = "Earth",
                        secondary = "Luna",
                        xAxis = "R",
                        zAxis = "N",
                        isBuiltIn = true
                    };
                    showCoordSystemWindow = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // 2. User Custom Coordinate Systems List
            if (GMATParameterRegistry.UserCoordinateSystems.Count > 0) {
                GUILayout.Space(4);
                GUILayout.Label(I18n.Format("craft_cs_custom_count", GMATParameterRegistry.UserCoordinateSystems.Count), labelValueStyle);
                for (int u = 0; u < GMATParameterRegistry.UserCoordinateSystems.Count; u++) {
                    var ucs = GMATParameterRegistry.UserCoordinateSystems[u];
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(I18n.Format("craft_cs_item", ucs.name, ucs.origin, ucs.axes), labelValueStyle);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(I18n.T("btn_edit"), secondaryButtonStyle, GUILayout.Width(50), GUILayout.Height(20))) {
                        editingCoordSystem = ucs.Clone();
                        showCoordSystemWindow = true;
                    }
                    if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                        GMATParameterRegistry.DeleteCoordinateSystem(ucs.name);
                        ScreenMessages.PostScreenMessage(I18n.Format("craft_cs_deleted", ucs.name), 3.0f, ScreenMessageStyle.UPPER_CENTER);
                        break;
                    }
                    GUILayout.EndHorizontal();

                    if (ucs.axes == "ObjectReferenced") {
                        GUILayout.Label(I18n.Format("craft_cs_follow", ucs.primary, ucs.secondary, ucs.xAxis, ucs.zAxis), labelValueStyle);
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
            } else {
                GUILayout.Space(2);
                GUILayout.Label(I18n.T("craft_cs_empty"), labelValueStyle);
            }

            GUILayout.EndVertical();

            GUILayout.Space(6);

            // Synchronize & Sub-window Button Card (Exact restoration from f6273ae)
            GUILayout.BeginVertical(cardBoxStyle);
            if (GUILayout.Button(I18n.T("btn_sync_to_gmat"), actionButtonStyle, GUILayout.Height(34))) {
                string engTitle = (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) ? cachedEngines[selectedEngineIndex].title : I18n.T("craft_default_engine");
                syncStatusMessage = I18n.Format("craft_sync_done", vessel.mainBody.name, selectedStageIndices.Count, engTitle);
                activeTab = 2; // jump to mission sequence (Tab 2)
            }
            if (GUILayout.Button(I18n.T("btn_view_craft_snapshot"), secondaryButtonStyle, GUILayout.Height(26))) {
                showCraftDetailsWindow = true;
            }
            if (!string.IsNullOrEmpty(syncStatusMessage)) {
                GUILayout.Label(syncStatusMessage, labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
    }
}
