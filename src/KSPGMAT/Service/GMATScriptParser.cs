using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    public static class GMATScriptParser {
        public static GMATImportResult ParseScriptFile(string filePath, Vessel vessel = null) {
            if (!File.Exists(filePath)) {
                return new GMATImportResult {
                    success = false,
                    message = $"脚本文件不存在: {filePath}"
                };
            }
            string content = File.ReadAllText(filePath);
            return ParseScriptText(content, Path.GetFileName(filePath), vessel);
        }

        public static GMATImportResult ParseScriptText(string scriptContent, string scriptName = "ImportedScript", Vessel vessel = null) {
            var result = new GMATImportResult {
                success = true,
                script_name = scriptName,
                primary_craft_name = "Sat"
            };

            if (string.IsNullOrEmpty(scriptContent)) {
                result.success = false;
                result.message = "脚本内容为空";
                return result;
            }

            result.raw_script_content = scriptContent;

            var lines = scriptContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inMissionSeq = false;
            string primaryCraft = null;
            var thrustersDict = new Dictionary<string, GMATThrusterModel>(StringComparer.OrdinalIgnoreCase);
            var tanksDict = new Dictionary<string, GMATTankModel>(StringComparer.OrdinalIgnoreCase);
            var finiteBurnsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // BurnName -> ThrusterName

            string currentDC = "DefaultDC";

            for (int i = 0; i < lines.Length; i++) {
                string rawLine = lines[i].Trim();
                if (string.IsNullOrEmpty(rawLine) || rawLine.StartsWith("%")) continue;

                // Strip inline comments
                int commentIdx = rawLine.IndexOf('%');
                if (commentIdx >= 0) rawLine = rawLine.Substring(0, commentIdx).Trim();
                if (string.IsNullOrEmpty(rawLine)) continue;

                if (rawLine.StartsWith("BeginMissionSequence", StringComparison.OrdinalIgnoreCase)) {
                    inMissionSeq = true;
                    continue;
                }

                if (!inMissionSeq) {
                    // --- Header Resources Parsing ---
                    // 1. Spacecraft
                    var scMatch = Regex.Match(rawLine, @"Create\s+Spacecraft\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (scMatch.Success) {
                        string scName = scMatch.Groups[1].Value;
                        if (string.IsNullOrEmpty(primaryCraft)) {
                            primaryCraft = scName;
                            result.primary_craft_name = scName;
                        }
                        if (!result.all_spacecraft_names.Contains(scName)) result.all_spacecraft_names.Add(scName);
                    }

                    // 2. Tanks
                    var tkMatch = Regex.Match(rawLine, @"Create\s+ChemicalTank\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (tkMatch.Success) {
                        string[] tkNames = tkMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var tkName in tkNames) {
                            string clean = tkName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !tanksDict.ContainsKey(clean)) {
                                tanksDict[clean] = new GMATTankModel { Id = clean, StageNumber = tanksDict.Count };
                            }
                        }
                    }

                    // 3. Thrusters
                    var thrMatch = Regex.Match(rawLine, @"Create\s+ChemicalThruster\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (thrMatch.Success) {
                        string[] thrNames = thrMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var thrName in thrNames) {
                            string clean = thrName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !thrustersDict.ContainsKey(clean)) {
                                thrustersDict[clean] = new GMATThrusterModel { Id = clean, RawTitle = clean, StageNumber = thrustersDict.Count };
                            }
                        }
                    }

                    // 4. FiniteBurns
                    var fbMatch = Regex.Match(rawLine, @"Create\s+FiniteBurn\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (fbMatch.Success) {
                        string[] fbNames = fbMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var fbName in fbNames) {
                            string clean = fbName.Trim();
                            if (!string.IsNullOrEmpty(clean)) finiteBurnsDict[clean] = "";
                        }
                    }

                    // 5. Solvers / DifferentialCorrectors
                    var dcMatch = Regex.Match(rawLine, @"Create\s+DifferentialCorrector\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (dcMatch.Success) {
                        string[] dcNames = dcMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var dcName in dcNames) {
                            string clean = dcName.Trim();
                            if (!string.IsNullOrEmpty(clean)) {
                                if (!result.solvers.Contains(clean)) result.solvers.Add(clean);
                                if (!result.solver_models.Exists(s => s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))) {
                                    result.solver_models.Add(new GMATSolverModel { Name = clean, Algorithm = "NewtonRaphson", DerivativeMethod = "ForwardDifference", MaxIterations = 50, ShowProgress = true });
                                }
                            }
                        }
                    }

                    // 6. Variables
                    var varMatch = Regex.Match(rawLine, @"Create\s+Variable\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (varMatch.Success) {
                        string[] vNames = varMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var vName in vNames) {
                            string clean = vName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !result.user_variables.Exists(v => v.name.Equals(clean, StringComparison.OrdinalIgnoreCase))) {
                                result.user_variables.Add(new GMATUserVariable { name = clean, value = 0.0 });
                            }
                        }
                    }

                    // Assignments
                    if (rawLine.Contains("=")) {
                        var assignMatch = Regex.Match(rawLine, @"([A-Za-z0-9_.]+)\s*=\s*([^;]+);?");
                        if (assignMatch.Success) {
                            string lhs = assignMatch.Groups[1].Value.Trim();
                            string rhs = assignMatch.Groups[2].Value.Trim().Trim('\'', '"');

                            // Spacecraft properties
                            if (!string.IsNullOrEmpty(primaryCraft) && lhs.StartsWith(primaryCraft + ".")) {
                                string prop = lhs.Substring(primaryCraft.Length + 1);
                                if (prop.Equals("DryMass", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double dm)) result.craft_dry_mass = dm;
                                else if (prop.Equals("DragArea", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double da)) result.craft_drag_area = da;
                                else if (prop.Equals("Cd", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double cd)) result.craft_cd = cd;
                                else if (prop.Equals("Cr", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double cr)) result.craft_cr = cr;
                                else if (prop.Equals("SRPArea", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double srp)) result.craft_srp_area = srp;
                                else if (prop.Equals("Epoch", StringComparison.OrdinalIgnoreCase)) result.craft_epoch = rhs;
                                else if (prop.Equals("DateFormat", StringComparison.OrdinalIgnoreCase)) result.craft_date_format = rhs;
                                else if (prop.Equals("CoordinateSystem", StringComparison.OrdinalIgnoreCase)) result.craft_coord_system = rhs;
                                else if (prop.Equals("DisplayStateType", StringComparison.OrdinalIgnoreCase)) result.craft_state_type = rhs;
                                else if (prop.Equals("X", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double x)) result.craft_x = x;
                                else if (prop.Equals("Y", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double y)) result.craft_y = y;
                                else if (prop.Equals("Z", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double z)) result.craft_z = z;
                                else if (prop.Equals("VX", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double vx)) result.craft_vx = vx;
                                else if (prop.Equals("VY", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double vy)) result.craft_vy = vy;
                                else if (prop.Equals("VZ", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double vz)) result.craft_vz = vz;
                                else if (prop.Equals("SMA", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double sma)) result.craft_sma = sma;
                                else if (prop.Equals("ECC", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double ecc)) result.craft_ecc = ecc;
                                else if (prop.Equals("INC", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double inc)) result.craft_inc = inc;
                                else if (prop.Equals("RAAN", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double raan)) result.craft_raan = raan;
                                else if (prop.Equals("AOP", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double aop)) result.craft_aop = aop;
                                else if (prop.Equals("TA", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double ta)) result.craft_ta = ta;
                            }

                            // Tank properties
                            foreach (var kvp in tanksDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("FuelMass", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double fm)) kvp.Value.FuelMassKg = fm;
                                    else if (prop.Equals("Volume", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double vol)) kvp.Value.VolumeM3 = vol;
                                    else if (prop.Equals("FuelDensity", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double fd)) kvp.Value.FuelDensityKgM3 = fd;
                                    else if (prop.Equals("PressureModel", StringComparison.OrdinalIgnoreCase)) kvp.Value.PressureModel = rhs;
                                }
                            }

                            // Thruster properties
                            foreach (var kvp in thrustersDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("C1", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double thrustN)) kvp.Value.ThrustKN = thrustN / 1000.0;
                                    else if (prop.Equals("K1", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double isp)) kvp.Value.IspVac = isp;
                                    else if (prop.Equals("Tank", StringComparison.OrdinalIgnoreCase)) kvp.Value.BoundTankId = rhs.Trim('{', '}').Trim();
                                    else if (prop.Equals("ThrustDirection1", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double d1)) kvp.Value.ThrustDirection1 = d1;
                                    else if (prop.Equals("ThrustDirection2", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double d2)) kvp.Value.ThrustDirection2 = d2;
                                    else if (prop.Equals("ThrustDirection3", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double d3)) kvp.Value.ThrustDirection3 = d3;
                                    else if (prop.Equals("DecrementMass", StringComparison.OrdinalIgnoreCase) && bool.TryParse(rhs, out bool dm)) kvp.Value.DecrementMass = dm;
                                    else if (prop.Equals("Axes", StringComparison.OrdinalIgnoreCase)) kvp.Value.Axes = rhs;
                                    else if (prop.Equals("Origin", StringComparison.OrdinalIgnoreCase)) kvp.Value.Origin = rhs;
                                    else if (prop.Equals("CoordinateSystem", StringComparison.OrdinalIgnoreCase)) kvp.Value.CoordinateSystem = rhs;
                                }
                            }

                            // FiniteBurn properties
                            var fbKeys = new List<string>(finiteBurnsDict.Keys);
                            foreach (var fbKey in fbKeys) {
                                if (lhs.StartsWith(fbKey + ".Thrusters")) {
                                    finiteBurnsDict[fbKey] = rhs.Trim('{', '}').Trim();
                                }
                            }

                            // Variables
                            var uVar = result.user_variables.Find(v => v.name.Equals(lhs, StringComparison.OrdinalIgnoreCase));
                            if (uVar != null && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double vVal)) {
                                uVar.value = vVal;
                            }

                            // DifferentialCorrector properties
                            foreach (var slv in result.solver_models) {
                                if (lhs.StartsWith(slv.Name + ".")) {
                                    string prop = lhs.Substring(slv.Name.Length + 1);
                                    if (prop.Equals("Algorithm", StringComparison.OrdinalIgnoreCase)) slv.Algorithm = rhs;
                                    else if (prop.Equals("DerivativeMethod", StringComparison.OrdinalIgnoreCase)) slv.DerivativeMethod = rhs;
                                    else if (prop.Equals("MaximumIterations", StringComparison.OrdinalIgnoreCase) && int.TryParse(rhs, out int mi)) slv.MaxIterations = mi;
                                    else if (prop.Equals("ShowProgress", StringComparison.OrdinalIgnoreCase) && bool.TryParse(rhs, out bool sp)) slv.ShowProgress = sp;
                                    else if (prop.Equals("ReportStyle", StringComparison.OrdinalIgnoreCase)) slv.ReportStyle = rhs;
                                    else if (prop.Equals("ReportFile", StringComparison.OrdinalIgnoreCase)) slv.ReportFile = rhs;
                                }
                            }
                        }
                    }
                } else {
                    // --- Mission Sequence Commands Parsing ---
                    // 1. BeginFiniteBurn (coalesce with following Propagate & EndFiniteBurn)
                    var bfbMatch = Regex.Match(rawLine, @"BeginFiniteBurn\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (bfbMatch.Success) {
                        string fbName = bfbMatch.Groups[1].Value;
                        string boundEng = finiteBurnsDict.ContainsKey(fbName) ? finiteBurnsDict[fbName] : "";
                        string propName = "LEOProp";
                        double dur = 120.0;
                        string durVar = "";
                        bool useVar = false;

                        // Peek ahead in lines to find duration and EndFiniteBurn
                        for (int look = i + 1; look < Math.Min(lines.Length, i + 6); look++) {
                            string peek = lines[look].Trim();
                            var pM = Regex.Match(peek, @"Propagate\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_]+)\s*\)\s*\{\s*([^}]+)\s*\}", RegexOptions.IgnoreCase);
                            if (pM.Success) {
                                propName = pM.Groups[1].Value;
                                string stopC = pM.Groups[3].Value.Trim();
                                if (stopC.Contains("=")) {
                                    string rVal = stopC.Split('=')[1].Trim();
                                    if (double.TryParse(rVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) {
                                        dur = dVal;
                                    } else {
                                        useVar = true;
                                        durVar = rVal;
                                    }
                                }
                            }
                            if (Regex.IsMatch(peek, @"EndFiniteBurn\s+" + fbName, RegexOptions.IgnoreCase)) {
                                i = look; // Advance main loop past EndFiniteBurn
                                break;
                            }
                        }

                        result.sequence.Add(new MissionStep {
                            type = "FiniteBurn",
                            burn_name = fbName,
                            bound_thruster = boundEng,
                            propagator = propName,
                            duration = dur,
                            burn_use_vary_var = useVar,
                            burn_vary_var_name = durVar,
                            duration_str = durVar,
                            label = $"FiniteBurn ({fbName})"
                        });
                        continue;
                    }

                    if (rawLine.StartsWith("EndFiniteBurn", StringComparison.OrdinalIgnoreCase)) {
                        continue; // Handled in BeginFiniteBurn coalesce
                    }

                    // 2. Propagate
                    var propMatch = Regex.Match(rawLine, @"Propagate\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_]+)\s*\)\s*\{\s*([^}]+)\s*\}", RegexOptions.IgnoreCase);
                    if (propMatch.Success) {
                        string pName = propMatch.Groups[1].Value;
                        string stopCond = propMatch.Groups[3].Value.Trim();

                        var st = new MissionStep {
                            type = "Propagate",
                            propagator = pName,
                            label = $"Propagate ({pName})"
                        };

                        if (stopCond.Contains("=")) {
                            var parts = stopCond.Split('=');
                            st.stop_type = parts[0].Trim();
                            string rVal = parts.Length > 1 ? parts[1].Trim() : "3600.0";
                            st.target_val_str = rVal;
                            st.duration_str = rVal;
                            if (double.TryParse(rVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur)) {
                                if (st.stop_type.Contains("ElapsedDays")) st.duration = dur * 86400.0;
                                else st.duration = dur;
                                st.target_val = dur;
                                st.burn_use_vary_var = false;
                            } else {
                                st.duration_str = rVal;
                                st.target_val_str = rVal;
                                st.burn_use_vary_var = true;
                                st.burn_vary_var_name = rVal;
                            }
                        } else {
                            st.stop_type = stopCond;
                        }
                        result.sequence.Add(st);
                    }

                    // 3. Target
                    var tgtMatch = Regex.Match(rawLine, @"Target\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (tgtMatch.Success) {
                        currentDC = tgtMatch.Groups[1].Value;
                        result.sequence.Add(new MissionStep {
                            type = "Target",
                            dc_name = currentDC,
                            label = $"Target ({currentDC})"
                        });
                    }

                    // 4. Vary
                    var varyMatch = Regex.Match(rawLine, @"Vary\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*=\s*([^,]+)(?:,\s*\{([^}]+)\})?\s*\)", RegexOptions.IgnoreCase);
                    if (varyMatch.Success) {
                        string dc = varyMatch.Groups[1].Value;
                        string varExpr = varyMatch.Groups[2].Value.Trim();
                        string initValStr = varyMatch.Groups[3].Value.Trim();
                        string opts = varyMatch.Groups[4].Value;

                        double initVal = 150.0;
                        double.TryParse(initValStr, NumberStyles.Any, CultureInfo.InvariantCulture, out initVal);

                        double low = 0.0, up = 1000.0, maxStep = 10.0, pert = 0.0001;
                        if (!string.IsNullOrEmpty(opts)) {
                            var lowM = Regex.Match(opts, @"Lower\s*=\s*([-+]?[\d.]+)");
                            if (lowM.Success) double.TryParse(lowM.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out low);
                            var upM = Regex.Match(opts, @"Upper\s*=\s*([-+]?[\d.]+)");
                            if (upM.Success) double.TryParse(upM.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out up);
                            var stepM = Regex.Match(opts, @"MaxStep\s*=\s*([-+]?[\d.]+)");
                            if (stepM.Success) double.TryParse(stepM.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out maxStep);
                            var pertM = Regex.Match(opts, @"Perturbation\s*=\s*([-+]?[\d.]+)");
                            if (pertM.Success) double.TryParse(pertM.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out pert);
                        }

                        if (varExpr.Contains("ThrustDirection")) {
                            // Thrust Vector Control
                            int dirIdx = varExpr.EndsWith("1") ? 1 : (varExpr.EndsWith("2") ? 2 : 3);
                            string thrName = "";
                            var thMatch = Regex.Match(varExpr, @"(?:[A-Za-z0-9_]+\.)?([A-Za-z0-9_]+)\.ThrustDirection");
                            if (thMatch.Success) thrName = thMatch.Groups[1].Value;

                            MissionStep existingTvc = (result.sequence.Count > 0 && result.sequence[result.sequence.Count - 1].type == "ThrustVectorControl" && (string.IsNullOrEmpty(thrName) || result.sequence[result.sequence.Count - 1].bound_thruster == thrName)) 
                                ? result.sequence[result.sequence.Count - 1] 
                                : null;

                            if (existingTvc != null) {
                                if (dirIdx == 1) {
                                    existingTvc.thrust_dir1 = initVal;
                                    existingTvc.vary_dir1_enabled = true;
                                    existingTvc.vary_dir1_lower = low;
                                    existingTvc.vary_dir1_upper = up;
                                    existingTvc.vary_dir1_max_step = maxStep;
                                } else if (dirIdx == 2) {
                                    existingTvc.thrust_dir2 = initVal;
                                    existingTvc.vary_dir2_enabled = true;
                                    existingTvc.vary_dir2_lower = low;
                                    existingTvc.vary_dir2_upper = up;
                                    existingTvc.vary_dir2_max_step = maxStep;
                                } else if (dirIdx == 3) {
                                    existingTvc.thrust_dir3 = initVal;
                                    existingTvc.vary_dir3_enabled = true;
                                    existingTvc.vary_dir3_lower = low;
                                    existingTvc.vary_dir3_upper = up;
                                    existingTvc.vary_dir3_max_step = maxStep;
                                }
                            } else {
                                var st = new MissionStep {
                                    type = "ThrustVectorControl",
                                    dc_name = !string.IsNullOrEmpty(dc) ? dc : currentDC,
                                    bound_thruster = thrName,
                                    label = $"推力矢量 ({thrName})",
                                    thrust_dir1 = (dirIdx == 1) ? initVal : 1.0,
                                    thrust_dir2 = (dirIdx == 2) ? initVal : 0.0,
                                    thrust_dir3 = (dirIdx == 3) ? initVal : 0.0,
                                    vary_dir1_enabled = (dirIdx == 1),
                                    vary_dir2_enabled = (dirIdx == 2),
                                    vary_dir3_enabled = (dirIdx == 3),
                                    vary_dir1_lower = (dirIdx == 1) ? low : -1.0,
                                    vary_dir1_upper = (dirIdx == 1) ? up : 1.0,
                                    vary_dir1_max_step = (dirIdx == 1) ? maxStep : 0.05,
                                    vary_dir2_lower = (dirIdx == 2) ? low : -1.0,
                                    vary_dir2_upper = (dirIdx == 2) ? up : 1.0,
                                    vary_dir2_max_step = (dirIdx == 2) ? maxStep : 0.05,
                                    vary_dir3_lower = (dirIdx == 3) ? low : -1.0,
                                    vary_dir3_upper = (dirIdx == 3) ? up : 1.0,
                                    vary_dir3_max_step = (dirIdx == 3) ? maxStep : 0.05
                                };
                                result.sequence.Add(st);
                            }
                        } else {
                            result.sequence.Add(new MissionStep {
                                type = "Vary",
                                dc_name = dc,
                                vary_variable = varExpr,
                                vary_initial = initVal,
                                vary_lower = low,
                                vary_upper = up,
                                vary_max_step = maxStep,
                                vary_perturbation = pert,
                                label = $"Vary {varExpr}"
                            });
                        }
                    }

                    // 5. Achieve
                    var achMatch = Regex.Match(rawLine, @"Achieve\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*=\s*([^,]+)(?:,\s*\{\s*Tolerance\s*=\s*([\d.]+)\s*\})?\s*\)", RegexOptions.IgnoreCase);
                    if (achMatch.Success) {
                        string dc = achMatch.Groups[1].Value;
                        string param = achMatch.Groups[2].Value.Trim();
                        string valStr = achMatch.Groups[3].Value.Trim();
                        double val = 0.0;
                        double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out val);
                        double tol = 0.1;
                        if (achMatch.Groups.Count > 4 && !string.IsNullOrEmpty(achMatch.Groups[4].Value)) {
                            double.TryParse(achMatch.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out tol);
                        }

                        result.sequence.Add(new MissionStep {
                            type = "Achieve",
                            dc_name = dc,
                            achieve_param = param,
                            achieve_val = val,
                            achieve_tolerance = tol,
                            label = $"Achieve {param}"
                        });
                    }

                    // 6. EndTarget
                    if (rawLine.StartsWith("EndTarget", StringComparison.OrdinalIgnoreCase)) {
                        result.sequence.Add(new MissionStep {
                            type = "EndTarget",
                            dc_name = currentDC,
                            label = $"EndTarget ({currentDC})"
                        });
                    }

                    // 7. Impulsive Maneuver
                    var manMatch = Regex.Match(rawLine, @"Maneuver\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (manMatch.Success) {
                        string bName = manMatch.Groups[1].Value;
                        result.sequence.Add(new MissionStep {
                            type = "ImpulsiveBurn",
                            burn_name = bName,
                            label = $"Maneuver ({bName})"
                        });
                    }

                    // 8. Report Command
                    var repMatch = Regex.Match(rawLine, @"Report\s+([A-Za-z0-9_]+)\s*(.*);?", RegexOptions.IgnoreCase);
                    if (repMatch.Success) {
                        string subName = repMatch.Groups[1].Value;
                        string repParams = repMatch.Groups[2].Value.Trim().TrimEnd(';');
                        if (!subName.Equals("TrajReport", StringComparison.OrdinalIgnoreCase)) {
                            result.sequence.Add(new MissionStep {
                                type = "Report",
                                subscriber_name = subName,
                                expression = repParams,
                                label = $"Report ({subName})"
                            });
                        }
                    }

                    // 9. Direct Thrust Vector Assignment in Sequence (e.g. Sat.RL10.ThrustDirection1 = 1;)
                    var directTvcMatch = Regex.Match(rawLine, @"(?:[A-Za-z0-9_]+\.)?([A-Za-z0-9_]+)\.ThrustDirection([123])\s*=\s*([-+]?[\d.]+)");
                    if (directTvcMatch.Success) {
                        string thrName = directTvcMatch.Groups[1].Value;
                        int dirIdx = int.Parse(directTvcMatch.Groups[2].Value);
                        double val = double.Parse(directTvcMatch.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                        MissionStep existingTvc = (result.sequence.Count > 0 && result.sequence[result.sequence.Count - 1].type == "ThrustVectorControl" && (string.IsNullOrEmpty(thrName) || result.sequence[result.sequence.Count - 1].bound_thruster == thrName)) 
                            ? result.sequence[result.sequence.Count - 1] 
                            : null;

                        if (existingTvc != null) {
                            if (dirIdx == 1) existingTvc.thrust_dir1 = val;
                            else if (dirIdx == 2) existingTvc.thrust_dir2 = val;
                            else if (dirIdx == 3) existingTvc.thrust_dir3 = val;
                        } else {
                            var st = new MissionStep {
                                type = "ThrustVectorControl",
                                dc_name = currentDC,
                                bound_thruster = thrName,
                                label = $"推力矢量 ({thrName})",
                                thrust_dir1 = (dirIdx == 1) ? val : 1.0,
                                thrust_dir2 = (dirIdx == 2) ? val : 0.0,
                                thrust_dir3 = (dirIdx == 3) ? val : 0.0
                            };
                            result.sequence.Add(st);
                        }
                        continue;
                    }

                    // 10. Equation / Assignment Command (e.g. Sat.DryMass = 1200 or burntime = 150)
                    if (rawLine.Contains("=") && 
                        !rawLine.StartsWith("Propagate", StringComparison.OrdinalIgnoreCase) && 
                        !rawLine.StartsWith("Vary", StringComparison.OrdinalIgnoreCase) && 
                        !rawLine.StartsWith("Achieve", StringComparison.OrdinalIgnoreCase) && 
                        !rawLine.StartsWith("Target", StringComparison.OrdinalIgnoreCase) &&
                        !rawLine.Contains(".ThrustDirection")) {
                        var eqMatch = Regex.Match(rawLine, @"([A-Za-z0-9_.]+)\s*=\s*([^;]+);?");
                        if (eqMatch.Success) {
                            string lhs = eqMatch.Groups[1].Value.Trim();
                            string rhs = eqMatch.Groups[2].Value.Trim();
                            result.sequence.Add(new MissionStep {
                                type = "Equation",
                                variable = lhs,
                                expression = rhs,
                                label = $"{lhs} = {rhs}"
                            });
                        }
                    }
                }
            }

            result.tanks = new List<GMATTankModel>(tanksDict.Values);
            result.thrusters = new List<GMATThrusterModel>(thrustersDict.Values);
            double totalFuel = 0;
            foreach (var tk in result.tanks) totalFuel += tk.FuelMassKg;
            result.craft_fuel_mass = totalFuel;

            return result;
        }
    }
}
