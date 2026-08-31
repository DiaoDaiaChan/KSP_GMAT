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
                    var tkMatch = Regex.Match(rawLine, @"Create\s+ChemicalTank\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (tkMatch.Success) {
                        string tkName = tkMatch.Groups[1].Value;
                        if (!tanksDict.ContainsKey(tkName)) {
                            var tkObj = new GMATTankModel { Id = tkName, StageNumber = tanksDict.Count };
                            tanksDict[tkName] = tkObj;
                        }
                    }

                    // 3. Thrusters
                    var thrMatch = Regex.Match(rawLine, @"Create\s+ChemicalThruster\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (thrMatch.Success) {
                        string thrName = thrMatch.Groups[1].Value;
                        if (!thrustersDict.ContainsKey(thrName)) {
                            var thrObj = new GMATThrusterModel { Id = thrName, RawTitle = thrName, StageNumber = thrustersDict.Count };
                            thrustersDict[thrName] = thrObj;
                        }
                    }

                    // 4. FiniteBurns
                    var fbMatch = Regex.Match(rawLine, @"Create\s+FiniteBurn\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (fbMatch.Success) {
                        finiteBurnsDict[fbMatch.Groups[1].Value] = "";
                    }

                    // 5. Solvers / DifferentialCorrectors
                    var dcMatch = Regex.Match(rawLine, @"Create\s+DifferentialCorrector\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (dcMatch.Success) {
                        string dcName = dcMatch.Groups[1].Value;
                        if (!result.solvers.Contains(dcName)) result.solvers.Add(dcName);
                    }

                    // 6. Variables
                    var varMatch = Regex.Match(rawLine, @"Create\s+Variable\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (varMatch.Success) {
                        string vName = varMatch.Groups[1].Value;
                        if (!result.user_variables.Exists(v => v.name == vName)) {
                            result.user_variables.Add(new GMATUserVariable { name = vName, value = 0.0 });
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
                                else if (prop.Equals("CoordinateSystem", StringComparison.OrdinalIgnoreCase)) result.craft_coord_system = rhs;
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
                        }
                    }
                } else {
                    // --- Mission Sequence Commands Parsing ---
                    // 1. Propagate
                    var propMatch = Regex.Match(rawLine, @"Propagate\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_]+)\s*\)\s*\{\s*([^}]+)\s*\}", RegexOptions.IgnoreCase);
                    if (propMatch.Success) {
                        string pName = propMatch.Groups[1].Value;
                        string stopCond = propMatch.Groups[3].Value.Trim();

                        var st = new MissionStep {
                            type = "Propagate",
                            propagator = pName,
                            label = $"Propagate ({pName})"
                        };

                        if (stopCond.Contains("ElapsedSecs") || stopCond.Contains("ElapsedDays") || stopCond.Contains("=")) {
                            var parts = stopCond.Split('=');
                            st.stop_type = parts[0].Trim();
                            string rVal = parts.Length > 1 ? parts[1].Trim() : "3600.0";
                            if (double.TryParse(rVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur)) {
                                if (st.stop_type.Contains("ElapsedDays")) st.duration = dur * 86400.0;
                                else st.duration = dur;
                                st.target_val = dur;
                            } else {
                                st.duration_str = rVal;
                                st.burn_use_vary_var = true;
                                st.burn_vary_var_name = rVal;
                            }
                        } else {
                            st.stop_type = stopCond;
                        }
                        result.sequence.Add(st);
                    }

                    // 2. Target
                    var tgtMatch = Regex.Match(rawLine, @"Target\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (tgtMatch.Success) {
                        currentDC = tgtMatch.Groups[1].Value;
                        result.sequence.Add(new MissionStep {
                            type = "Target",
                            dc_name = currentDC,
                            label = $"Target ({currentDC})"
                        });
                    }

                    // 3. Vary
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
                            var st = new MissionStep {
                                type = "ThrustVectorControl",
                                dc_name = dc,
                                label = $"TVC Direction{dirIdx}",
                                thrust_dir1 = dirIdx == 1 ? initVal : 1.0,
                                thrust_dir2 = dirIdx == 2 ? initVal : 0.0,
                                thrust_dir3 = dirIdx == 3 ? initVal : 0.0,
                                vary_dir1_enabled = dirIdx == 1,
                                vary_dir2_enabled = dirIdx == 2,
                                vary_dir3_enabled = dirIdx == 3
                            };
                            result.sequence.Add(st);
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

                    // 4. BeginFiniteBurn
                    var bfbMatch = Regex.Match(rawLine, @"BeginFiniteBurn\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (bfbMatch.Success) {
                        string fbName = bfbMatch.Groups[1].Value;
                        string boundEng = finiteBurnsDict.ContainsKey(fbName) ? finiteBurnsDict[fbName] : "";
                        result.sequence.Add(new MissionStep {
                            type = "FiniteBurn",
                            burn_name = fbName,
                            bound_thruster = boundEng,
                            duration = 120.0,
                            label = $"FiniteBurn ({fbName})"
                        });
                    }

                    // 5. Achieve
                    var achMatch = Regex.Match(rawLine, @"Achieve\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*=\s*([^,]+)(?:,\s*\{\s*Tolerance\s*=\s*([\d.]+)\s*\})?\s*\)", RegexOptions.IgnoreCase);
                    if (achMatch.Success) {
                        string dc = achMatch.Groups[1].Value;
                        string param = achMatch.Groups[2].Value.Trim();
                        string valStr = achMatch.Groups[3].Value.Trim();
                        double val = 42164.14;
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
