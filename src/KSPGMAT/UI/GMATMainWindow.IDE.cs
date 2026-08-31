using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void TriggerPropagationSolve(string rawCustomScript = null) {
            Vessel vessel = FlightGlobals.ActiveVessel;
            activeTab = 2; // Switch to Mission Sequence (Tab 2)
            isPropagating = true;
            solveStartTime = Time.realtimeSinceStartup;
            solveProgress = 0.05f;
            solveStageText = "1/4 正在序列化物理状态与航天器资源...";

            List<StageData> selectedStages = GetSelectedStagesList();
            ROEngineInfo selectedEngine = GetDefaultOrActiveEngine();

            if (!string.IsNullOrEmpty(rawCustomScript)) {
                GMATClient.PropagateMissionWithRawScript(vessel, selectedStages, selectedEngine, missionSequence, propagatorConfigs, rawCustomScript, (success, result) => {
                    isPropagating = false;
                    solveProgress = success ? 1.0f : 0.0f;
                    solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                    latestResult = result;
                    if (success && result != null) {
                        syncStatusMessage = $"✅ GMAT 物理求解收敛成功！耗时: {solveTotalTime:F2}s";
                        if (result.solved_burns != null && result.solved_burns.Count > 0) {
                            activeTab = 3; // Switch to Results tab
                        }
                    } else {
                        syncStatusMessage = $"❌ 求解未收敛: {(result != null ? (result.error ?? "未知错误") : "通信异常")}";
                    }
                });
            } else {
                GMATClient.PropagateMission(vessel, selectedStages, selectedEngine, missionSequence, propagatorConfigs, (success, result) => {
                    isPropagating = false;
                    solveProgress = success ? 1.0f : 0.0f;
                    solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                    latestResult = result;
                    if (success && result != null) {
                        syncStatusMessage = $"✅ GMAT 物理求解收敛成功！耗时: {solveTotalTime:F2}s";
                        if (result.solved_burns != null && result.solved_burns.Count > 0) {
                            activeTab = 3; // Switch to Results tab
                        }
                    } else {
                        syncStatusMessage = $"❌ 求解未收敛: {(result != null ? (result.error ?? "未知错误") : "通信异常")}";
                    }
                });
            }
        }

        private string GenerateLiveGmatScript(int mode) {
            Vessel v = FlightGlobals.ActiveVessel;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            List<string> engNames = new List<string>();
            if (cachedEngines != null && cachedEngines.Count > 0) {
                for (int e = 0; e < cachedEngines.Count; e++) {
                    string engId = GMATParameterRegistry.SanitizeGmatIdentifier(cachedEngines[e].title, "Engine");
                    if (!engNames.Contains(engId)) engNames.Add(engId);
                }
            }
            if (engNames.Count == 0) engNames.Add("MainEngine");

            // 1. Resources Section
            if (mode == 0 || mode == 1) {
                sb.AppendLine("% -------------------------------------------------------------");
                sb.AppendLine("% GMAT Spacecraft & Hardware Architecture (Mod-wide Sat)");
                sb.AppendLine("% -------------------------------------------------------------");
                sb.AppendLine("Create Spacecraft Sat;");
                sb.AppendLine($"Sat.DateFormat = {customSatDateFormat ?? "UTCGregorian"};");

                // Sync live epoch with in-game universal time (Planetarium / RSS / Principia)
                double ut = (v != null) ? Planetarium.GetUniversalTime() : 0.0;
                double jdVal = 2433647.5 + (ut / 86400.0);
                string epochUtcVal = GMATClient.JdToUtcGregorian(jdVal);
                sb.AppendLine($"Sat.Epoch = '{epochUtcVal}';");

                sb.AppendLine($"Sat.CoordinateSystem = {customSatCoordinateSystem ?? "EarthMJ2000Eq"};");
                sb.AppendLine($"Sat.DisplayStateType = {customSatDisplayStateType ?? "Cartesian"};");

                // Live Cartesian initial state vector in ICRF
                Vector3d pos_km = Vector3d.zero;
                Vector3d vel_kms = Vector3d.zero;
                if (v != null) {
                    PrincipiaInterop.GetVesselStateICRF(v, out pos_km, out vel_kms);
                }
                sb.AppendLine($"Sat.X = {pos_km.x.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.Y = {pos_km.y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.Z = {pos_km.z.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.VX = {vel_kms.x.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.VY = {vel_kms.y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.VZ = {vel_kms.z.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)};");

                double wetM = (v != null) ? v.totalMass * 1000.0 : 5000.0;
                double fuelM = 0.0;
                if (cachedTanks != null) {
                    for (int t = 0; t < cachedTanks.Count; t++) fuelM += cachedTanks[t].fuelMassKg;
                }
                double dryM = (customSatDryMass >= 0) ? customSatDryMass : Math.Max(100.0, wetM - fuelM);
                sb.AppendLine($"Sat.DryMass = {dryM.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.DragArea = {customSatDragArea.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.Cd = {customSatCd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.Cr = {customSatCr.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine($"Sat.SRPArea = {customSatSRPArea.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                sb.AppendLine();

                // Tanks
                List<string> tkNames = new List<string>();
                var stateTanks = GMATStateManager.Current.Tanks;
                if (stateTanks != null && stateTanks.Count > 0) {
                    for (int t = 0; t < stateTanks.Count; t++) {
                        string tkId = stateTanks[t].Id;
                        if (!tkNames.Contains(tkId)) {
                            tkNames.Add(tkId);
                            sb.AppendLine($"Create ChemicalTank {tkId};");
                            sb.AppendLine($"{tkId}.AllowNegativeFuelMass = true;");
                            sb.AppendLine($"{tkId}.FuelMass = {stateTanks[t].FuelMassKg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"{tkId}.PressureModel = '{stateTanks[t].PressureModel}';");
                        }
                    }
                }
                if (tkNames.Count == 0) {
                    tkNames.Add("Tank0");
                    sb.AppendLine("Create ChemicalTank Tank0;");
                    sb.AppendLine("Tank0.AllowNegativeFuelMass = true;");
                    sb.AppendLine($"Tank0.FuelMass = {fuelM.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                    sb.AppendLine("Tank0.PressureModel = 'PressureRegulated';");
                }
                sb.AppendLine();

                // Thrusters
                HashSet<string> declaredEngines = new HashSet<string>();
                var stateThrusters = GMATStateManager.Current.Thrusters;
                if (stateThrusters != null && stateThrusters.Count > 0) {
                    for (int e = 0; e < stateThrusters.Count; e++) {
                        var thr = stateThrusters[e];
                        string engId = thr.Id;
                        if (!declaredEngines.Contains(engId)) {
                            declaredEngines.Add(engId);
                            string boundTk = !string.IsNullOrEmpty(thr.BoundTankId) && tkNames.Contains(thr.BoundTankId) ? thr.BoundTankId : tkNames[0];

                            sb.AppendLine($"Create ChemicalThruster {engId};");
                            sb.AppendLine($"{engId}.CoordinateSystem = Local;");
                            sb.AppendLine($"{engId}.Origin = {GMATStateManager.Current.Spacecraft.CentralBody};");
                            sb.AppendLine($"{engId}.Axes = VNB;");
                            sb.AppendLine($"{engId}.ThrustDirection1 = {thr.ThrustDirection1.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"{engId}.ThrustDirection2 = {thr.ThrustDirection2.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"{engId}.ThrustDirection3 = {thr.ThrustDirection3.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"{engId}.DecrementMass = {thr.DecrementMass.ToString().ToLower()};");
                            sb.AppendLine($"{engId}.Tank = {{{boundTk}}};");
                            sb.AppendLine($"{engId}.C1 = {(thr.ThrustKN * 1000.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"{engId}.K1 = {thr.IspVac.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};");
                        }
                    }
                }
                if (declaredEngines.Count == 0) {
                    declaredEngines.Add("MainEngine");
                    sb.AppendLine("Create ChemicalThruster MainEngine;");
                    sb.AppendLine("MainEngine.CoordinateSystem = Local;");
                    sb.AppendLine("MainEngine.Origin = Earth;");
                    sb.AppendLine("MainEngine.Axes = VNB;");
                    sb.AppendLine("MainEngine.ThrustDirection1 = 1;");
                    sb.AppendLine("MainEngine.ThrustDirection2 = 0;");
                    sb.AppendLine("MainEngine.ThrustDirection3 = 0;");
                    sb.AppendLine("MainEngine.DecrementMass = true;");
                    sb.AppendLine($"MainEngine.Tank = {{{tkNames[0]}}};");
                    sb.AppendLine("MainEngine.C1 = 3000.0;");
                    sb.AppendLine("MainEngine.K1 = 300.0;");
                }
                sb.AppendLine();

                // Spacecraft Tank and Thruster Attachments
                sb.AppendLine($"Sat.Tanks = {{{string.Join(", ", tkNames.ToArray())}}};");
                sb.AppendLine($"Sat.Thrusters = {{{string.Join(", ", new List<string>(declaredEngines).ToArray())}}};");
                sb.AppendLine();

                // Finite Burns
                HashSet<string> createdBurns = new HashSet<string>();
                for (int s = 0; s < missionSequence.Count; s++) {
                    if (missionSequence[s].type == "FiniteBurn") {
                        string bName = GMATParameterRegistry.SanitizeGmatIdentifier(missionSequence[s].burn_name, "FiniteBurn");
                        if (!createdBurns.Contains(bName)) {
                            createdBurns.Add(bName);
                            List<string> thrustersList = new List<string>();
                            if (missionSequence[s].bound_thrusters != null && missionSequence[s].bound_thrusters.Count > 0) {
                                for (int bt = 0; bt < missionSequence[s].bound_thrusters.Count; bt++) {
                                    string cand = GMATParameterRegistry.SanitizeGmatIdentifier(missionSequence[s].bound_thrusters[bt], "Engine");
                                    if (engNames.Contains(cand) && !thrustersList.Contains(cand)) thrustersList.Add(cand);
                                }
                            }
                            if (thrustersList.Count == 0) {
                                string thrusterToBind = engNames[0];
                                if (!string.IsNullOrEmpty(missionSequence[s].bound_thruster)) {
                                    string cand = GMATParameterRegistry.SanitizeGmatIdentifier(missionSequence[s].bound_thruster, "Engine");
                                    if (engNames.Contains(cand)) thrusterToBind = cand;
                                }
                                thrustersList.Add(thrusterToBind);
                            }
                            sb.AppendLine($"Create FiniteBurn {bName};");
                            sb.AppendLine($"{bName}.Thrusters = {{{string.Join(", ", thrustersList.ToArray())}}};");
                            sb.AppendLine($"{bName}.ThrottleLogicAlgorithm = 'MaxNumberOfThrusters';");
                            sb.AppendLine();
                        }
                    }
                }

                // Impulsive Burns
                if (userImpulsiveBurns != null && userImpulsiveBurns.Count > 0) {
                    for (int ib = 0; ib < userImpulsiveBurns.Count; ib++) {
                        var imp = userImpulsiveBurns[ib];
                        string ibName = GMATParameterRegistry.SanitizeGmatIdentifier(imp.name, "Impulsive");
                        sb.AppendLine($"Create ImpulsiveBurn {ibName};");
                        sb.AppendLine($"{ibName}.CoordinateSystem = Local;");
                        sb.AppendLine($"{ibName}.Origin = {imp.origin};");
                        sb.AppendLine($"{ibName}.Axes = {imp.axes};");
                        sb.AppendLine($"{ibName}.Element1 = {imp.element1.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)};");
                        sb.AppendLine($"{ibName}.Element2 = {imp.element2.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)};");
                        sb.AppendLine($"{ibName}.Element3 = {imp.element3.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)};");
                        sb.AppendLine($"{ibName}.Isp = {imp.isp.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)};");
                        sb.AppendLine();
                    }
                }

                // Propagators
                if (propagatorConfigs != null && propagatorConfigs.Count > 0) {
                    foreach (var kvp in propagatorConfigs) {
                        string pName = kvp.Key;
                        var pCfg = kvp.Value;
                        string fmName = $"{pName}ForceModel";
                        sb.AppendLine($"Create ForceModel {fmName};");
                        sb.AppendLine($"Create Propagator {pName};");
                        sb.AppendLine($"{pName}.FM = {fmName};");
                        sb.AppendLine($"{pName}.Type = {pCfg.integratorType};");
                    }
                } else {
                    sb.AppendLine("Create ForceModel LEOPropForceModel;");
                    sb.AppendLine("Create Propagator LEOProp;");
                    sb.AppendLine("LEOProp.FM = LEOPropForceModel;");
                    sb.AppendLine("LEOProp.Type = RungeKutta89;");
                }
                sb.AppendLine();

                // Coordinate Systems
                if (GMATParameterRegistry.UserCoordinateSystems != null && GMATParameterRegistry.UserCoordinateSystems.Count > 0) {
                    for (int u = 0; u < GMATParameterRegistry.UserCoordinateSystems.Count; u++) {
                        var ucs = GMATParameterRegistry.UserCoordinateSystems[u];
                        string csName = GMATParameterRegistry.SanitizeGmatIdentifier(ucs.name, "CS");
                        sb.AppendLine($"Create CoordinateSystem {csName};");
                        sb.AppendLine($"{csName}.Origin = {ucs.origin};");
                        sb.AppendLine($"{csName}.Axes = {ucs.axes};");
                    }
                    sb.AppendLine();
                }

                // Solvers
                if (userCustomSolvers != null && userCustomSolvers.Count > 0) {
                    for (int s = 0; s < userCustomSolvers.Count; s++) {
                        var sol = userCustomSolvers[s];
                        string solName = GMATParameterRegistry.SanitizeGmatIdentifier(sol.name, "DC");
                        sb.AppendLine($"Create DifferentialCorrector {solName};");
                        sb.AppendLine($"{solName}.Algorithm = '{sol.algorithm}';");
                        sb.AppendLine($"{solName}.MaximumIterations = {sol.maxIterations};");
                    }
                } else {
                    List<string> dcNames = GetActiveSolverNames();
                    for (int d = 0; d < dcNames.Count; d++) {
                        sb.AppendLine($"Create DifferentialCorrector {dcNames[d]};");
                        sb.AppendLine($"{dcNames[d]}.Algorithm = 'NewtonRaphson';");
                        sb.AppendLine($"{dcNames[d]}.MaximumIterations = 50;");
                    }
                }
                sb.AppendLine();

                // User Variables, Arrays & Strings
                HashSet<string> userVars = new HashSet<string>();
                if (userCustomVariables != null) {
                    for (int vidx = 0; vidx < userCustomVariables.Count; vidx++) {
                        string vn = GMATParameterRegistry.SanitizeGmatIdentifier(userCustomVariables[vidx].name, "Var");
                        userVars.Add(vn);
                    }
                }
                for (int s = 0; s < missionSequence.Count; s++) {
                    var st = missionSequence[s];
                    if (st.type == "Vary" && !string.IsNullOrEmpty(st.vary_variable) && !st.vary_variable.Contains(".")) {
                        userVars.Add(st.vary_variable);
                    } else if (st.type == "FiniteBurn" && st.burn_use_vary_var && !string.IsNullOrEmpty(st.burn_vary_var_name) && !st.burn_vary_var_name.Contains(".")) {
                        userVars.Add(st.burn_vary_var_name);
                    }
                }
                if (userVars.Count > 0) {
                    List<string> uvList = new List<string>(userVars);
                    sb.AppendLine($"Create Variable {string.Join(" ", uvList.ToArray())};");
                }
                if (userCustomArrays != null && userCustomArrays.Count > 0) {
                    for (int a = 0; a < userCustomArrays.Count; a++) {
                        var arr = userCustomArrays[a];
                        string arrName = GMATParameterRegistry.SanitizeGmatIdentifier(arr.name, "Arr");
                        sb.AppendLine($"Create Array {arrName}[{arr.rows},{arr.cols}];");
                    }
                }
                if (userCustomStrings != null && userCustomStrings.Count > 0) {
                    for (int st = 0; st < userCustomStrings.Count; st++) {
                        var ustr = userCustomStrings[st];
                        string sName = GMATParameterRegistry.SanitizeGmatIdentifier(ustr.name, "Str");
                        sb.AppendLine($"Create String {sName};");
                        sb.AppendLine($"{sName} = '{ustr.value}';");
                    }
                }
                sb.AppendLine();
            }

            // 2. Mission Sequence Section
            if (mode == 0 || mode == 2) {
                sb.AppendLine("% -------------------------------------------------------------");
                sb.AppendLine("% GMAT Mission Sequence Execution Control");
                sb.AppendLine("% -------------------------------------------------------------");
                sb.AppendLine("BeginMissionSequence;");
                sb.AppendLine();

                for (int s = 0; s < missionSequence.Count; s++) {
                    var step = missionSequence[s];
                    if (step.type == "Propagate") {
                        string stopCond;
                        if (step.stop_type == "Sat.Earth.Apoapsis" || step.stop_type == "Sat.Earth.Periapsis" || step.stop_type.EndsWith(".Apoapsis") || step.stop_type.EndsWith(".Periapsis")) {
                            stopCond = step.stop_type;
                        } else if (step.stop_type == "Sat.ElapsedSecs" || step.stop_type == "Sat.ElapsedDays") {
                            stopCond = $"{step.stop_type} = {step.duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}";
                        } else {
                            stopCond = $"{step.stop_type} = {step.target_val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}";
                        }
                        sb.AppendLine($"Propagate '{step.label}' {step.propagator}(Sat) {{{stopCond}}};");
                    } else if (step.type == "Target") {
                        sb.AppendLine($"Target '{step.label}' {step.dc_name} {{SolveMode = Solve, ExitMode = SaveAndContinue}};");
                    } else if (step.type == "Vary") {
                        sb.AppendLine($"   Vary '{step.label}' {step.dc_name}({step.vary_variable} = {step.vary_initial.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, {{Perturbation = 0.0001, Lower = {step.vary_lower.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, Upper = {step.vary_upper.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, MaxStep = {step.vary_max_step.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}}});");
                    } else if (step.type == "FiniteBurn") {
                        string bName = GMATParameterRegistry.SanitizeGmatIdentifier(step.burn_name, "FiniteBurn");
                        sb.AppendLine($"   BeginFiniteBurn {bName}(Sat);");
                        string durVar = step.burn_use_vary_var && !string.IsNullOrEmpty(step.burn_vary_var_name) ? step.burn_vary_var_name : step.duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                        sb.AppendLine($"   Propagate {step.propagator}(Sat) {{Sat.ElapsedSecs = {durVar}}};");
                        sb.AppendLine($"   EndFiniteBurn {bName}(Sat);");
                    } else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                        string engToUse = engNames[0];
                        if (!string.IsNullOrEmpty(step.bound_thruster)) {
                            string cand = GMATParameterRegistry.SanitizeGmatIdentifier(step.bound_thruster, "Engine");
                            if (engNames.Contains(cand)) engToUse = cand;
                        }
                        string parentDc = GetParentTargetDcName(s);
                        string dcToUse = (!string.IsNullOrEmpty(parentDc) && parentDc != "DefaultDC") ? parentDc : (string.IsNullOrEmpty(step.dc_name) ? "DefaultDC" : step.dc_name);
                        bool isInsideTarget = false;
                        for (int tj = s - 1; tj >= 0; tj--) {
                            if (missionSequence[tj].type == "Target") { isInsideTarget = true; break; }
                            if (missionSequence[tj].type == "EndTarget") break;
                        }
                        if (isInsideTarget || step.is_vary_vector) {
                            sb.AppendLine($"   % 🧭 推力矢量配置: {engToUse} (VNB 三轴方向)");
                            if (step.vary_dir1_enabled) {
                                sb.AppendLine($"   Vary '{step.label} V' {dcToUse}({engToUse}.ThrustDirection1 = {step.thrust_dir1.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, {{Perturbation = 0.0001, Lower = {step.vary_dir1_lower.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, Upper = {step.vary_dir1_upper.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, MaxStep = {step.vary_dir1_max_step.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}}});");
                            } else {
                                sb.AppendLine($"   {engToUse}.ThrustDirection1 = {step.thrust_dir1.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            }
                            if (step.vary_dir2_enabled) {
                                sb.AppendLine($"   Vary '{step.label} N' {dcToUse}({engToUse}.ThrustDirection2 = {step.thrust_dir2.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, {{Perturbation = 0.0001, Lower = {step.vary_dir2_lower.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, Upper = {step.vary_dir2_upper.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, MaxStep = {step.vary_dir2_max_step.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}}});");
                            } else {
                                sb.AppendLine($"   {engToUse}.ThrustDirection2 = {step.thrust_dir2.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            }
                            if (step.vary_dir3_enabled) {
                                sb.AppendLine($"   Vary '{step.label} B' {dcToUse}({engToUse}.ThrustDirection3 = {step.thrust_dir3.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, {{Perturbation = 0.0001, Lower = {step.vary_dir3_lower.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, Upper = {step.vary_dir3_upper.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}, MaxStep = {step.vary_dir3_max_step.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}}});");
                            } else {
                                sb.AppendLine($"   {engToUse}.ThrustDirection3 = {step.thrust_dir3.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            }
                        } else {
                            sb.AppendLine($"   % 🧭 设定推力矢量: {engToUse} (VNB 三轴方向)");
                            sb.AppendLine($"   {engToUse}.ThrustDirection1 = {step.thrust_dir1.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"   {engToUse}.ThrustDirection2 = {step.thrust_dir2.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                            sb.AppendLine($"   {engToUse}.ThrustDirection3 = {step.thrust_dir3.ToString("G", System.Globalization.CultureInfo.InvariantCulture)};");
                        }
                    } else if (step.type == "Achieve") {
                        sb.AppendLine($"   Achieve '{step.label}' {step.dc_name}({step.achieve_param} = {step.achieve_val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, {{Tolerance = {step.achieve_tolerance.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}}});");
                    } else if (step.type == "EndTarget") {
                        sb.AppendLine($"EndTarget;  % For {step.dc_name}");
                    } else if (step.type == "Report") {
                        sb.AppendLine($"Report TrajReport Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ;");
                    }
                }
            }

            return sb.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex GmatSyntaxRegex = new System.Text.RegularExpressions.Regex(
            @"\b(BeginMissionSequence|DifferentialCorrector|ChemicalThruster|BeginFiniteBurn|CoordinateSystem|EndFiniteBurn|ChemicalTank|FiniteBurn|Propagator|ForceModel|Spacecraft|Propagate|EndTarget|Achieve|Report|Target|Create|Vary|Sat)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private string ColorizeGmatSyntax(string line) {
            if (string.IsNullOrEmpty(line)) return "";
            if (line.TrimStart().StartsWith("%")) {
                return $"<color=#64748b><i>{line}</i></color>";
            }
            return GmatSyntaxRegex.Replace(line, match => {
                string kw = match.Value;
                string col = (kw == "Create" || kw == "BeginMissionSequence" || kw == "Target" || kw == "EndTarget") ? "#f43f5e" :
                             (kw == "Sat" || kw == "Spacecraft") ? "#38bdf8" :
                             (kw == "ChemicalTank" || kw == "ChemicalThruster" || kw == "FiniteBurn" || kw == "BeginFiniteBurn" || kw == "EndFiniteBurn") ? "#facc15" :
                             (kw == "Propagate" || kw == "Propagator" || kw == "Vary" || kw == "Achieve") ? "#34d399" : "#a78bfa";
                return $"<color={col}><b>{kw}</b></color>";
            });
        }
    }
}
