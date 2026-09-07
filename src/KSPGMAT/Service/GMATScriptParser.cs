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

            // 利用 GMATScriptLinter 铁律门禁前置自愈 (纯 ASCII、Sat. 前缀规范化、机动矢量前缀剥离、Vary/Achieve 容差自愈)
            scriptContent = GMATScriptLinter.FixScript(scriptContent);
            result.raw_script_content = scriptContent;

            string defaultBody = vessel?.mainBody?.name;
            if (string.IsNullOrEmpty(defaultBody)) {
                try {
                    defaultBody = FlightGlobals.ActiveVessel?.mainBody?.name ?? FlightGlobals.currentMainBody?.name ?? GMATStateManager.Current?.Spacecraft?.CentralBody ?? "Earth";
                } catch {
                    defaultBody = "Earth";
                }
            }
            defaultBody = GMATSchemaRegistry.Instance.NormalizeCelestialName(defaultBody);

            var lines = scriptContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inMissionSeq = false;
            string primaryCraft = null;
            var thrustersDict = new Dictionary<string, GMATThrusterModel>(StringComparer.OrdinalIgnoreCase);
            var tanksDict = new Dictionary<string, GMATTankModel>(StringComparer.OrdinalIgnoreCase);
            var finiteBurnsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // BurnName -> ThrusterName
            var burnsDict = new Dictionary<string, GMATBurnModel>(StringComparer.OrdinalIgnoreCase);
            var coordSystemsDict = new Dictionary<string, CustomCoordSystemDef>(StringComparer.OrdinalIgnoreCase);
            var propConfigsDict = new Dictionary<string, PropagatorConfig>(StringComparer.OrdinalIgnoreCase);
            var forceModelsDict = new Dictionary<string, (string centralBody, List<string> pointMasses, string drag, string srp)>(StringComparer.OrdinalIgnoreCase);
            var forceModelDefsDict = new Dictionary<string, ForceModelDef>(StringComparer.OrdinalIgnoreCase);
            var reportFilesDict = new Dictionary<string, GMATReportFileModel>(StringComparer.OrdinalIgnoreCase);
            var orbitViewsDict = new Dictionary<string, GMATOrbitViewModel>(StringComparer.OrdinalIgnoreCase);
            var customHeaderBlocks = new List<string>();

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

                    // 5. Solvers / DifferentialCorrectors & Optimizers (动态元数据架构识别)
                    var genericCreateMatch = Regex.Match(rawLine, @"Create\s+([A-Za-z0-9_]+)\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (genericCreateMatch.Success) {
                        string objType = genericCreateMatch.Groups[1].Value.Trim();
                        string namesStr = genericCreateMatch.Groups[2].Value;
                        var schema = GMATSchemaRegistry.Instance;

                        if (objType.Equals("DifferentialCorrector", StringComparison.OrdinalIgnoreCase)) {
                            foreach (var sName in namesStr.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries)) {
                                string clean = sName.Trim();
                                if (!string.IsNullOrEmpty(clean)) {
                                    if (!result.solvers.Contains(clean)) result.solvers.Add(clean);
                                    if (!result.solver_models.Exists(s => s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))) {
                                        result.solver_models.Add(new GMATSolverModel { Name = clean, Type = "DifferentialCorrector", Algorithm = "NewtonRaphson", DerivativeMethod = "ForwardDifference", MaxIterations = 50, ShowProgress = true });
                                    }
                                }
                            }
                        } else if (schema.IsOptimizerSolver(objType)) {
                            foreach (var sName in namesStr.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries)) {
                                string clean = sName.Trim();
                                if (!string.IsNullOrEmpty(clean)) {
                                    if (!result.solvers.Contains(clean)) result.solvers.Add(clean);
                                    if (!result.solver_models.Exists(s => s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))) {
                                        result.solver_models.Add(new GMATSolverModel { Name = clean, Type = objType, Algorithm = "SQP", MaxIterations = 200, ShowProgress = true });
                                    }
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

                    // 7. ImpulsiveBurn
                    var ibMatch = Regex.Match(rawLine, @"Create\s+ImpulsiveBurn\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (ibMatch.Success) {
                        string[] ibNames = ibMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var ibName in ibNames) {
                            string clean = ibName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !burnsDict.ContainsKey(clean)) {
                                burnsDict[clean] = new GMATBurnModel { Name = clean, Type = "ImpulsiveBurn", CoordinateSystem = "Local", Axes = "VNB", Origin = defaultBody };
                            }
                        }
                    }

                    // 8. CoordinateSystem
                    var csMatch = Regex.Match(rawLine, @"Create\s+CoordinateSystem\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (csMatch.Success) {
                        string[] csNames = csMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var csName in csNames) {
                            string clean = csName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !coordSystemsDict.ContainsKey(clean)) {
                                coordSystemsDict[clean] = new CustomCoordSystemDef { name = clean, origin = defaultBody, axes = "MJ2000Eq" };
                            }
                        }
                    }

                    // 9. ForceModel
                    var fmMatch = Regex.Match(rawLine, @"Create\s+ForceModel\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (fmMatch.Success) {
                        string[] fmNames = fmMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var fmName in fmNames) {
                            string clean = fmName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !forceModelsDict.ContainsKey(clean)) {
                                forceModelsDict[clean] = (defaultBody, new List<string>(), "None", "Off");
                                forceModelDefsDict[clean] = new ForceModelDef { name = clean, centralBody = defaultBody };
                            }
                        }
                    }

                    // 10. Propagator
                    var propSetupMatch = Regex.Match(rawLine, @"Create\s+Propagator\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (propSetupMatch.Success) {
                        string[] pNames = propSetupMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pName in pNames) {
                            string clean = pName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !propConfigsDict.ContainsKey(clean)) {
                                propConfigsDict[clean] = new PropagatorConfig { name = clean, integratorType = "RungeKutta89", initialStepSize = 60, accuracy = 1e-11, minStep = 0.001, maxStep = 21600 };
                            }
                        }
                    }

                    // 11. ReportFile
                    var rfMatch = Regex.Match(rawLine, @"Create\s+ReportFile\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (rfMatch.Success) {
                        string[] rfNames = rfMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var rfName in rfNames) {
                            string clean = rfName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !reportFilesDict.ContainsKey(clean)) {
                                reportFilesDict[clean] = new GMATReportFileModel { Name = clean, Filename = $"{clean}.txt", WriteHeaders = true };
                            }
                        }
                    }

                    // 12. OrbitView
                    var ovMatch = Regex.Match(rawLine, @"Create\s+OrbitView\s+([^;]+);?", RegexOptions.IgnoreCase);
                    if (ovMatch.Success) {
                        string[] ovNames = ovMatch.Groups[1].Value.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var ovName in ovNames) {
                            string clean = ovName.Trim();
                            if (!string.IsNullOrEmpty(clean) && !orbitViewsDict.ContainsKey(clean)) {
                                orbitViewsDict[clean] = new GMATOrbitViewModel { Name = clean };
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
                                result.craft_extended_properties[prop] = rhs;

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

                            // ImpulsiveBurn properties
                            foreach (var kvp in burnsDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("CoordinateSystem", StringComparison.OrdinalIgnoreCase)) kvp.Value.CoordinateSystem = rhs;
                                    else if (prop.Equals("Origin", StringComparison.OrdinalIgnoreCase)) kvp.Value.Origin = rhs;
                                    else if (prop.Equals("Axes", StringComparison.OrdinalIgnoreCase)) kvp.Value.Axes = rhs;
                                    else if (prop.Equals("Element1", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double e1)) kvp.Value.Element1 = e1;
                                    else if (prop.Equals("Element2", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double e2)) kvp.Value.Element2 = e2;
                                    else if (prop.Equals("Element3", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double e3)) kvp.Value.Element3 = e3;
                                    else if (prop.Equals("Isp", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double isp)) kvp.Value.Isp = isp;
                                    else if (prop.Equals("DecrementMass", StringComparison.OrdinalIgnoreCase) && bool.TryParse(rhs, out bool dm)) kvp.Value.DecrementMass = dm;
                                }
                            }

                            // CoordinateSystem properties
                            foreach (var kvp in coordSystemsDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("Origin", StringComparison.OrdinalIgnoreCase)) kvp.Value.origin = rhs;
                                    else if (prop.Equals("Axes", StringComparison.OrdinalIgnoreCase)) kvp.Value.axes = rhs;
                                    else if (prop.Equals("Primary", StringComparison.OrdinalIgnoreCase)) kvp.Value.primary = rhs;
                                    else if (prop.Equals("Secondary", StringComparison.OrdinalIgnoreCase)) kvp.Value.secondary = rhs;
                                    else if (prop.Equals("XAxis", StringComparison.OrdinalIgnoreCase)) kvp.Value.xAxis = rhs;
                                    else if (prop.Equals("YAxis", StringComparison.OrdinalIgnoreCase)) kvp.Value.yAxis = rhs;
                                    else if (prop.Equals("ZAxis", StringComparison.OrdinalIgnoreCase)) kvp.Value.zAxis = rhs;
                                    else if (prop.Equals("Epoch", StringComparison.OrdinalIgnoreCase)) kvp.Value.epoch = rhs;
                                }
                            }

                            // ForceModel properties
                            var fmKeys = new List<string>(forceModelsDict.Keys);
                            foreach (var fmKey in fmKeys) {
                                if (lhs.StartsWith(fmKey + ".")) {
                                    string prop = lhs.Substring(fmKey.Length + 1);
                                    var cur = forceModelsDict[fmKey];
                                    if (prop.Equals("CentralBody", StringComparison.OrdinalIgnoreCase)) {
                                        string normBody = GMATSchemaRegistry.Instance.NormalizeCelestialName(rhs);
                                        forceModelsDict[fmKey] = (normBody, cur.pointMasses, cur.drag, cur.srp);
                                    }
                                    else if (prop.Equals("PointMasses", StringComparison.OrdinalIgnoreCase)) {
                                        var masses = rhs.Trim('{', '}').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                        cur.pointMasses.Clear();
                                        foreach (var m in masses) {
                                            string norm = GMATSchemaRegistry.Instance.NormalizeCelestialName(m.Trim());
                                            if (!cur.pointMasses.Contains(norm)) cur.pointMasses.Add(norm);
                                        }
                                    }
                                    else if (prop.StartsWith("Drag", StringComparison.OrdinalIgnoreCase)) forceModelsDict[fmKey] = (cur.centralBody, cur.pointMasses, rhs, cur.srp);
                                    else if (prop.Equals("SRP", StringComparison.OrdinalIgnoreCase)) forceModelsDict[fmKey] = (cur.centralBody, cur.pointMasses, cur.drag, rhs);

                                    if (forceModelDefsDict.ContainsKey(fmKey)) {
                                        var fmd = forceModelDefsDict[fmKey];
                                        fmd.rawLines.Add(rawLine);
                                        if (prop.Equals("CentralBody", StringComparison.OrdinalIgnoreCase)) fmd.centralBody = GMATSchemaRegistry.Instance.NormalizeCelestialName(rhs);
                                        else if (prop.Equals("PointMasses", StringComparison.OrdinalIgnoreCase)) {
                                            fmd.pointMasses.Clear();
                                            foreach (var m in rhs.Trim('{', '}').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                                                string norm = GMATSchemaRegistry.Instance.NormalizeCelestialName(m.Trim());
                                                if (!fmd.pointMasses.Contains(norm)) fmd.pointMasses.Add(norm);
                                            }
                                        }
                                        else if (prop.StartsWith("Drag", StringComparison.OrdinalIgnoreCase)) fmd.drag = rhs;
                                        else if (prop.Equals("SRP", StringComparison.OrdinalIgnoreCase)) fmd.srp = rhs;
                                        else if (prop.Equals("ErrorControl", StringComparison.OrdinalIgnoreCase)) fmd.errorControl = rhs;
                                        else if (prop.Equals("RelativisticCorrection", StringComparison.OrdinalIgnoreCase)) fmd.enableRelativity = rhs.Equals("On", StringComparison.OrdinalIgnoreCase);
                                        else if (prop.Contains("GravityField") && prop.EndsWith("PotentialFile", StringComparison.OrdinalIgnoreCase)) fmd.gravityModel = rhs;
                                        else if (prop.Contains("GravityField") && prop.EndsWith("Degree", StringComparison.OrdinalIgnoreCase) && int.TryParse(rhs, out int gDeg)) fmd.gravityDegree = gDeg;
                                        else if (prop.Contains("GravityField") && prop.EndsWith("Order", StringComparison.OrdinalIgnoreCase) && int.TryParse(rhs, out int gOrd)) fmd.gravityOrder = gOrd;
                                    }
                                }
                            }

                            // ReportFile properties
                            foreach (var kvp in reportFilesDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("Filename", StringComparison.OrdinalIgnoreCase)) kvp.Value.Filename = rhs;
                                    else if (prop.Equals("WriteHeaders", StringComparison.OrdinalIgnoreCase) && bool.TryParse(rhs, out bool wh)) kvp.Value.WriteHeaders = wh;
                                }
                            }

                            // Propagator properties
                            foreach (var kvp in propConfigsDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("FM", StringComparison.OrdinalIgnoreCase)) {
                                        if (forceModelDefsDict.ContainsKey(rhs)) {
                                            var fmd = forceModelDefsDict[rhs];
                                            kvp.Value.centralBody = fmd.centralBody;
                                            kvp.Value.customPointMasses = new List<string>(fmd.pointMasses);
                                            kvp.Value.gravityEarth = fmd.pointMasses.Exists(m => m.Equals("Earth", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityLuna = fmd.pointMasses.Exists(m => m.Equals("Luna", StringComparison.OrdinalIgnoreCase) || m.Equals("Moon", StringComparison.OrdinalIgnoreCase) || m.Equals("Mun", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravitySun = fmd.pointMasses.Exists(m => m.Equals("Sun", StringComparison.OrdinalIgnoreCase) || m.Equals("Sol", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityJupiter = fmd.pointMasses.Exists(m => m.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) || m.Equals("Jool", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityVenus = fmd.pointMasses.Exists(m => m.Equals("Venus", StringComparison.OrdinalIgnoreCase) || m.Equals("Eve", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityMars = fmd.pointMasses.Exists(m => m.Equals("Mars", StringComparison.OrdinalIgnoreCase) || m.Equals("Duna", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.enableSRP = fmd.srp.Equals("On", StringComparison.OrdinalIgnoreCase);
                                            kvp.Value.enableRelativity = fmd.enableRelativity;
                                            kvp.Value.errorControl = !string.IsNullOrEmpty(fmd.errorControl) ? fmd.errorControl : "RSSStep";
                                            kvp.Value.gravityModel = fmd.gravityModel;
                                            kvp.Value.gravityDegree = fmd.gravityDegree;
                                            kvp.Value.gravityOrder = fmd.gravityOrder;
                                            if (fmd.drag.Equals("None", StringComparison.OrdinalIgnoreCase)) {
                                                kvp.Value.dragModel = "None";
                                                kvp.Value.dragAtmosphereModel = "None";
                                            } else {
                                                kvp.Value.dragModel = fmd.drag;
                                                kvp.Value.dragAtmosphereModel = fmd.drag;
                                            }
                                        } else if (forceModelsDict.ContainsKey(rhs)) {
                                            var fm = forceModelsDict[rhs];
                                            kvp.Value.centralBody = fm.centralBody;
                                            kvp.Value.customPointMasses = new List<string>(fm.pointMasses);
                                            kvp.Value.gravityEarth = fm.pointMasses.Exists(m => m.Equals("Earth", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityLuna = fm.pointMasses.Exists(m => m.Equals("Luna", StringComparison.OrdinalIgnoreCase) || m.Equals("Moon", StringComparison.OrdinalIgnoreCase) || m.Equals("Mun", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravitySun = fm.pointMasses.Exists(m => m.Equals("Sun", StringComparison.OrdinalIgnoreCase) || m.Equals("Sol", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityJupiter = fm.pointMasses.Exists(m => m.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) || m.Equals("Jool", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityVenus = fm.pointMasses.Exists(m => m.Equals("Venus", StringComparison.OrdinalIgnoreCase) || m.Equals("Eve", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.gravityMars = fm.pointMasses.Exists(m => m.Equals("Mars", StringComparison.OrdinalIgnoreCase) || m.Equals("Duna", StringComparison.OrdinalIgnoreCase));
                                            kvp.Value.enableSRP = fm.srp.Equals("On", StringComparison.OrdinalIgnoreCase);
                                        }
                                    }
                                    else if (prop.Equals("Type", StringComparison.OrdinalIgnoreCase)) kvp.Value.integratorType = rhs;
                                    else if (prop.Equals("InitialStepSize", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double initStep)) kvp.Value.initialStepSize = initStep;
                                    else if (prop.Equals("Accuracy", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double acc)) kvp.Value.accuracy = acc;
                                    else if (prop.Equals("MinStep", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double minS)) kvp.Value.minStep = minS;
                                    else if (prop.Equals("MaxStep", StringComparison.OrdinalIgnoreCase) && double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxS)) kvp.Value.maxStep = maxS;
                                }
                            }

                            // OrbitView properties
                            foreach (var kvp in orbitViewsDict) {
                                if (lhs.StartsWith(kvp.Key + ".")) {
                                    string prop = lhs.Substring(kvp.Key.Length + 1);
                                    if (prop.Equals("CoordinateSystem", StringComparison.OrdinalIgnoreCase)) kvp.Value.CoordinateSystem = rhs;
                                    else if (prop.Equals("ViewPointReference", StringComparison.OrdinalIgnoreCase) || prop.Equals("ViewPointRef", StringComparison.OrdinalIgnoreCase)) kvp.Value.ViewPointReference = rhs;
                                    else if (prop.Equals("SolverIterations", StringComparison.OrdinalIgnoreCase)) kvp.Value.SolverIterations = rhs;
                                    else if (prop.Equals("ShowPlot", StringComparison.OrdinalIgnoreCase) && bool.TryParse(rhs, out bool sp)) kvp.Value.ShowPlot = sp;
                                    else if (prop.Equals("UpdatePlotFrequency", StringComparison.OrdinalIgnoreCase) && int.TryParse(rhs, out int upf)) kvp.Value.UpdatePlotFrequency = upf;
                                    else if (prop.Equals("MaxPlotPoints", StringComparison.OrdinalIgnoreCase) && int.TryParse(rhs, out int mpp)) kvp.Value.MaxPlotPoints = mpp;
                                    else if (prop.Equals("Add", StringComparison.OrdinalIgnoreCase)) {
                                        var items = rhs.Trim('{', '}').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                        kvp.Value.AddObjects.Clear();
                                        foreach (var item in items) kvp.Value.AddObjects.Add(item.Trim());
                                    }
                                    else if (prop.Equals("ViewPointVector", StringComparison.OrdinalIgnoreCase)) {
                                        var coords = rhs.Trim('[', ']').Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (coords.Length >= 3 &&
                                            double.TryParse(coords[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double vx) &&
                                            double.TryParse(coords[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double vy) &&
                                            double.TryParse(coords[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double vz)) {
                                            kvp.Value.ViewPointVector = new double[] { vx, vy, vz };
                                        }
                                    }
                                }
                            }
                        }
                    } else if (!rawLine.StartsWith("Create ", StringComparison.OrdinalIgnoreCase)) {
                        // 收集未被标准解析消费的特殊原生头部语句 (如 SPICE 配置、自定义全局设定等)
                        customHeaderBlocks.Add(rawLine);
                    }
                } else {
                    // --- Mission Sequence Commands Parsing ---
                    // 1. BeginFiniteBurn (coalesce with following Propagate & EndFiniteBurn)
                    var bfbMatch = Regex.Match(rawLine, @"BeginFiniteBurn\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (bfbMatch.Success) {
                        string fbName = bfbMatch.Groups[1].Value;
                        string boundEng = finiteBurnsDict.ContainsKey(fbName) ? finiteBurnsDict[fbName] : "";
                        // 基线 7: 缺省传播子从 GMAT 注册表获取，杜绝 C# 层硬编码 "LEOProp"
                        string propName = GetDefaultPropagatorName();
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

                    // 2. Propagate (Supports Multi-Stopping Conditions e.g. {Sat.Luna.Periapsis, Sat.ElapsedDays = 6})
                    var propMatch = Regex.Match(rawLine, @"Propagate(?:\s+Backwards)?(?:\s+Synchronized|\s+Single)?\s+([A-Za-z0-9_]+)\s*\(\s*([^)]+)\s*\)\s*\{\s*([^}]+)\s*\}", RegexOptions.IgnoreCase);
                    if (propMatch.Success) {
                        string pName = propMatch.Groups[1].Value;
                        string stopCondRaw = propMatch.Groups[3].Value.Trim();

                        bool isBack = rawLine.IndexOf("Backwards", StringComparison.OrdinalIgnoreCase) >= 0;
                        var st = new MissionStep {
                            type = "Propagate",
                            propagator = pName,
                            is_backwards = isBack,
                            label = isBack ? $"BackProp ({pName})" : $"Propagate ({pName})"
                        };

                        // Split by comma while respecting potential nested braces
                        string[] condChunks = stopCondRaw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int c = 0; c < condChunks.Length; c++) {
                            string chunk = condChunks[c].Trim();
                            if (string.IsNullOrEmpty(chunk)) continue;

                            // Check if chunk is a StopTolerance option, e.g. "StopTolerance = 1e-5"
                            if (Regex.IsMatch(chunk, @"^StopTolerance\s*=", RegexOptions.IgnoreCase)) {
                                var tolParts = chunk.Split('=');
                                if (tolParts.Length > 1 && double.TryParse(tolParts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedTol)) {
                                    st.stop_tolerance = parsedTol;
                                    if (st.stop_conditions.Count > 0) st.stop_conditions[st.stop_conditions.Count - 1].stop_tolerance = parsedTol;
                                }
                                continue;
                            }

                            var cond = new StopCondition();
                            if (chunk.Contains("=")) {
                                var parts = chunk.Split('=');
                                cond.parameter = parts[0].Trim();
                                string rVal = parts.Length > 1 ? parts[1].Trim() : "";
                                cond.target_val_str = rVal;
                                if (double.TryParse(rVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) {
                                    cond.target_val = dVal;
                                }
                            } else {
                                cond.parameter = chunk;
                            }
                            st.stop_conditions.Add(cond);
                        }

                        // Sync primary stop condition for legacy backward compatibility
                        if (st.stop_conditions.Count > 0) {
                            var first = st.stop_conditions[0];
                            st.stop_type = first.parameter;
                            st.target_val_str = first.target_val_str;
                            st.target_val = first.target_val;
                            st.duration_str = first.target_val_str;

                            if (double.TryParse(first.target_val_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur)) {
                                if (st.stop_type.Contains("ElapsedDays")) st.duration = dur * 86400.0;
                                else st.duration = dur;
                                st.target_val = dur;
                                st.burn_use_vary_var = false;
                            } else if (!string.IsNullOrEmpty(first.target_val_str)) {
                                st.burn_use_vary_var = true;
                                st.burn_vary_var_name = first.target_val_str;
                            }
                        } else {
                            st.stop_type = stopCondRaw;
                        }

                        result.sequence.Add(st);
                        continue;
                    }

                    // 3. Target
                    var tgtMatch = Regex.Match(rawLine, @"Target\s+([A-Za-z0-9_]+)(?:\s*\{\s*([^}]+)\s*\})?", RegexOptions.IgnoreCase);
                    if (tgtMatch.Success) {
                        currentDC = tgtMatch.Groups[1].Value;
                        var step = new MissionStep {
                            type = "Target",
                            dc_name = currentDC,
                            label = $"Target ({currentDC})"
                        };
                        if (tgtMatch.Groups.Count > 2 && !string.IsNullOrEmpty(tgtMatch.Groups[2].Value)) {
                            string body = tgtMatch.Groups[2].Value;
                            var smM = Regex.Match(body, @"SolveMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (smM.Success) step.solve_mode = smM.Groups[1].Value;
                            var emM = Regex.Match(body, @"ExitMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (emM.Success) step.exit_mode = emM.Groups[1].Value;
                        }
                        result.sequence.Add(step);
                        continue;
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

                        string lowStr = "", upStr = "", stepStr = "", pertStr = "";
                        double low = 0.0, up = 1000.0, maxStep = 10.0, pert = 0.0001;
                        if (!string.IsNullOrEmpty(opts)) {
                            var lowM = Regex.Match(opts, @"Lower\s*=\s*([^,}]+)");
                            if (lowM.Success) {
                                lowStr = lowM.Groups[1].Value.Trim();
                                double.TryParse(lowStr, NumberStyles.Any, CultureInfo.InvariantCulture, out low);
                            }
                            var upM = Regex.Match(opts, @"Upper\s*=\s*([^,}]+)");
                            if (upM.Success) {
                                upStr = upM.Groups[1].Value.Trim();
                                double.TryParse(upStr, NumberStyles.Any, CultureInfo.InvariantCulture, out up);
                            }
                            var stepM = Regex.Match(opts, @"MaxStep\s*=\s*([^,}]+)");
                            if (stepM.Success) {
                                stepStr = stepM.Groups[1].Value.Trim();
                                double.TryParse(stepStr, NumberStyles.Any, CultureInfo.InvariantCulture, out maxStep);
                            }
                            var pertM = Regex.Match(opts, @"Perturbation\s*=\s*([^,}]+)");
                            if (pertM.Success) {
                                pertStr = pertM.Groups[1].Value.Trim();
                                double.TryParse(pertStr, NumberStyles.Any, CultureInfo.InvariantCulture, out pert);
                            }
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
                                    label = I18n.Format("sp_thrust_vector_label", thrName),
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
                                vary_initial_str = initValStr,
                                vary_lower = low,
                                vary_lower_str = lowStr,
                                vary_upper = up,
                                vary_upper_str = upStr,
                                vary_max_step = maxStep,
                                vary_max_step_str = stepStr,
                                vary_perturbation = pert,
                                vary_perturbation_str = pertStr,
                                label = $"Vary {varExpr}"
                            });
                        }
                        continue;
                    }

                    // 5. Achieve
                    var achMatch = Regex.Match(rawLine, @"Achieve\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*=\s*([^,]+)(?:,\s*\{\s*Tolerance\s*=\s*([^}]+)\s*\})?\s*\)", RegexOptions.IgnoreCase);
                    if (achMatch.Success) {
                        string dc = achMatch.Groups[1].Value;
                        string param = achMatch.Groups[2].Value.Trim();
                        string valStr = achMatch.Groups[3].Value.Trim();
                        double val = 0.0;
                        double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out val);
                        double tol = 0.1;
                        string tolStr = "";
                        if (achMatch.Groups.Count > 4 && !string.IsNullOrEmpty(achMatch.Groups[4].Value)) {
                            tolStr = achMatch.Groups[4].Value.Trim();
                            double.TryParse(tolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out tol);
                        }

                        result.sequence.Add(new MissionStep {
                            type = "Achieve",
                            dc_name = dc,
                            achieve_param = param,
                            achieve_val = val,
                            achieve_val_str = valStr,
                            achieve_tolerance = tol,
                            achieve_tolerance_str = tolStr,
                            label = $"Achieve {param}"
                        });
                        continue;
                    }

                    // 6. EndTarget
                    if (rawLine.StartsWith("EndTarget", StringComparison.OrdinalIgnoreCase)) {
                        result.sequence.Add(new MissionStep {
                            type = "EndTarget",
                            dc_name = currentDC,
                            label = $"EndTarget ({currentDC})"
                        });
                        continue;
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
                        continue;
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
                        continue;
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
                                label = I18n.Format("sp_thrust_vector_label", thrName),
                                thrust_dir1 = (dirIdx == 1) ? val : 1.0,
                                thrust_dir2 = (dirIdx == 2) ? val : 0.0,
                                thrust_dir3 = (dirIdx == 3) ? val : 0.0
                            };
                            result.sequence.Add(st);
                        }
                        continue;
                    }

                    // 10. Optimization Commands
                    var optBeginMatch = Regex.Match(rawLine, @"Optimize\s+([A-Za-z0-9_]+)(?:\s*\{\s*([^}]+)\s*\})?", RegexOptions.IgnoreCase);
                    if (optBeginMatch.Success) {
                        string optName = optBeginMatch.Groups[1].Value;
                        var step = new MissionStep {
                            type = "Optimize",
                            optimizer_name = optName,
                            label = $"Optimize ({optName})"
                        };
                        if (optBeginMatch.Groups.Count > 2 && !string.IsNullOrEmpty(optBeginMatch.Groups[2].Value)) {
                            string body = optBeginMatch.Groups[2].Value;
                            var smM = Regex.Match(body, @"SolveMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (smM.Success) step.solve_mode = smM.Groups[1].Value;
                            var emM = Regex.Match(body, @"ExitMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (emM.Success) step.exit_mode = emM.Groups[1].Value;
                        }
                        result.sequence.Add(step);
                        continue;
                    }
                    var minMatch = Regex.Match(rawLine, @"Minimize\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*\)", RegexOptions.IgnoreCase);
                    if (minMatch.Success) {
                        string optName = minMatch.Groups[1].Value;
                        string minVar = minMatch.Groups[2].Value.Trim();
                        result.sequence.Add(new MissionStep {
                            type = "Minimize",
                            optimizer_name = optName,
                            minimize_variable = minVar,
                            label = $"Minimize ({minVar})"
                        });
                        continue;
                    }
                    var nlcMatch = Regex.Match(rawLine, @"NonlinearConstraint\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*(<=|>=|==|<|>|=)\s*([^)]+)\)", RegexOptions.IgnoreCase);
                    if (nlcMatch.Success) {
                        string optName = nlcMatch.Groups[1].Value;
                        string lhs = nlcMatch.Groups[2].Value.Trim();
                        string op = nlcMatch.Groups[3].Value.Trim();
                        string valStr = nlcMatch.Groups[4].Value.Trim();
                        double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double nlcVal);
                        result.sequence.Add(new MissionStep {
                            type = "NonlinearConstraint",
                            optimizer_name = optName,
                            constraint_lhs = lhs,
                            constraint_op = op,
                            constraint_val = nlcVal,
                            label = $"NLC ({lhs} {op} {valStr})"
                        });
                        continue;
                    }
                    if (rawLine.StartsWith("EndOptimize", StringComparison.OrdinalIgnoreCase)) {
                        result.sequence.Add(new MissionStep {
                            type = "EndOptimize",
                            label = "EndOptimize"
                        });
                        continue;
                    }

                    // 10B. Native Python Function Call Commands ([out1, out2] = Python.Module.Func(in) OR CallPythonFunction 'Lbl' Python...)
                    bool isPyCall = rawLine.IndexOf("Python.", StringComparison.OrdinalIgnoreCase) >= 0 || rawLine.StartsWith("CallPythonFunction", StringComparison.OrdinalIgnoreCase);
                    if (isPyCall) {
                        var explicitMatch = Regex.Match(rawLine, @"CallPythonFunction\s+['""]?([^'""\s]+)['""]?\s+Python\.([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)\s*\((.*?)\);?", RegexOptions.IgnoreCase);
                        if (explicitMatch.Success) {
                            string lbl = explicitMatch.Groups[1].Value.Trim();
                            string mod = explicitMatch.Groups[2].Value.Trim();
                            string func = explicitMatch.Groups[3].Value.Trim();
                            string inputs = explicitMatch.Groups[4].Value.Trim();
                            result.sequence.Add(new MissionStep {
                                type = "CallPython",
                                call_syntax_mode = "Explicit",
                                label = lbl,
                                python_module = mod,
                                python_function = func,
                                python_inputs = inputs,
                                python_outputs = ""
                            });
                            continue;
                        }

                        var assignMatch = Regex.Match(rawLine, @"(?:\[([^\]]*)\]|([A-Za-z0-9_.]+))\s*=\s*Python\.([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)\s*\((.*?)\);?", RegexOptions.IgnoreCase);
                        if (assignMatch.Success) {
                            string outputs = !string.IsNullOrEmpty(assignMatch.Groups[1].Value) ? assignMatch.Groups[1].Value.Trim() : assignMatch.Groups[2].Value.Trim();
                            string mod = assignMatch.Groups[3].Value.Trim();
                            string func = assignMatch.Groups[4].Value.Trim();
                            string inputs = assignMatch.Groups[5].Value.Trim();
                            result.sequence.Add(new MissionStep {
                                type = "CallPython",
                                call_syntax_mode = "Assignment",
                                label = $"{mod}.{func}",
                                python_module = mod,
                                python_function = func,
                                python_inputs = inputs,
                                python_outputs = outputs
                            });
                            continue;
                        }

                        var directMatch = Regex.Match(rawLine, @"Python\.([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)\s*\((.*?)\);?", RegexOptions.IgnoreCase);
                        if (directMatch.Success) {
                            string mod = directMatch.Groups[1].Value.Trim();
                            string func = directMatch.Groups[2].Value.Trim();
                            string inputs = directMatch.Groups[3].Value.Trim();
                            result.sequence.Add(new MissionStep {
                                type = "CallPython",
                                call_syntax_mode = "Assignment",
                                label = $"{mod}.{func}",
                                python_module = mod,
                                python_function = func,
                                python_inputs = inputs,
                                python_outputs = ""
                            });
                            continue;
                        }
                    }

                    // 11. Equation / Assignment Command (e.g. Sat.DryMass = 1200 or burntime = 150)
                    if (rawLine.Contains("=") && 
                        !rawLine.StartsWith("Propagate", StringComparison.OrdinalIgnoreCase) && 
                        !rawLine.StartsWith("Vary", StringComparison.OrdinalIgnoreCase) && 
                        !rawLine.StartsWith("Achieve", StringComparison.OrdinalIgnoreCase) && 
                        !rawLine.StartsWith("Target", StringComparison.OrdinalIgnoreCase) &&
                        !rawLine.StartsWith("NonlinearConstraint", StringComparison.OrdinalIgnoreCase) &&
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
                            continue;
                        }
                    }

                    // 11. 无损往返保留 (Lossless Round-Trip): 收集无法结构化的原生 GMAT 语句 (如 If, While, CallGmatFunction, 原始命令等)
                    if (!string.IsNullOrWhiteSpace(rawLine) && rawLine != ";" && !rawLine.StartsWith("%") && !rawLine.StartsWith("BeginMissionSequence", StringComparison.OrdinalIgnoreCase)) {
                        if (result.sequence.Count > 0 && result.sequence[result.sequence.Count - 1].type == "CustomScript") {
                            result.sequence[result.sequence.Count - 1].custom_verbatim_script += "\n" + rawLine;
                        } else {
                            result.sequence.Add(new MissionStep {
                                type = "CustomScript",
                                custom_verbatim_script = rawLine,
                                label = rawLine.Length > 28 ? rawLine.Substring(0, 25) + "..." : rawLine
                            });
                        }
                    }
                }
            }

            result.tanks = new List<GMATTankModel>(tanksDict.Values);
            result.thrusters = new List<GMATThrusterModel>(thrustersDict.Values);
            result.burns = new List<GMATBurnModel>(burnsDict.Values);
            result.coordinate_systems = new List<CustomCoordSystemDef>(coordSystemsDict.Values);
            result.propagators = propConfigsDict;
            result.force_models = forceModelDefsDict;
            result.report_files = new List<GMATReportFileModel>(reportFilesDict.Values);
            result.orbit_views = new List<GMATOrbitViewModel>(orbitViewsDict.Values);
            result.custom_header_blocks = customHeaderBlocks;
            double totalFuel = 0;
            foreach (var tk in result.tanks) totalFuel += tk.FuelMassKg;
            result.craft_fuel_mass = totalFuel;

            return result;
        }

        /// <summary>
        /// 获取缺省传播子名称：优先取注册表中 isDefault 或首个传播子；注册表为空时回退 "DefaultProp"。
        /// 基线 7: 杜绝 C# 层硬编码动力学清单。
        /// </summary>
        private static string GetDefaultPropagatorName() {
            try {
                var reg = GMATParameterRegistry.GetAllPropagatorConfigs();
                if (reg != null && reg.Count > 0) {
                    foreach (var kvp in reg) {
                        if (kvp.Value != null && kvp.Value.isDefault) return kvp.Key;
                    }
                    using (var e = reg.GetEnumerator()) {
                        if (e.MoveNext()) return e.Current.Key;
                    }
                }
            } catch { /* 注册表异常时回退默认名 */ }
            return "DefaultProp";
        }

        /// <summary>
        /// 单步 GMAT 脚本即时反向解析引擎：支持用户在单步代码透视框中直接就地编辑代码与数值，
        /// 实时反向解包并同步更新当前 MissionStep 的参数模型 (Two-Way Binding)。
        /// </summary>
        /// <param name="step">待更新的任务步骤模型</param>
        /// <param name="lineScript">用户在单步代码透视框中输入的单步脚本文本</param>
        /// <returns>若成功匹配并解析更新则返回 true；若语法未完成或不匹配则返回 false</returns>
        public static bool ParseSingleStepScript(MissionStep step, string lineScript) {
            if (step == null || string.IsNullOrWhiteSpace(lineScript)) return false;
            string clean = lineScript.Trim();
            if (clean.EndsWith(";")) clean = clean.Substring(0, clean.Length - 1).Trim();

            try {
                // 1. ACHIEVE
                if (step.type == "Achieve") {
                    var m = Regex.Match(clean, @"Achieve\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*=\s*([^,]+)(?:,\s*\{\s*Tolerance\s*=\s*([^}]+)\s*\})?\s*\)", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.dc_name = m.Groups[1].Value.Trim();
                        step.achieve_param = m.Groups[2].Value.Trim();
                        string valStr = m.Groups[3].Value.Trim();
                        step.achieve_val_str = valStr;
                        if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedVal)) {
                            step.achieve_val = parsedVal;
                        }
                        if (m.Groups.Count > 4 && !string.IsNullOrEmpty(m.Groups[4].Value)) {
                            string tolStr = m.Groups[4].Value.Trim();
                            step.achieve_tolerance_str = tolStr;
                            if (double.TryParse(tolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedTol) && parsedTol > 0.0) {
                                step.achieve_tolerance = parsedTol;
                            } else {
                                step.achieve_tolerance = GMATSchemaRegistry.Instance.GetAuthoritativeTolerance(step.achieve_param, 0.1);
                                step.achieve_tolerance_str = step.achieve_tolerance.ToString("G", CultureInfo.InvariantCulture);
                            }
                        } else {
                            step.achieve_tolerance = GMATSchemaRegistry.Instance.GetAuthoritativeTolerance(step.achieve_param, 0.1);
                            step.achieve_tolerance_str = step.achieve_tolerance.ToString("G", CultureInfo.InvariantCulture);
                        }
                        step.label = $"Achieve {step.achieve_param}";
                        return true;
                    }
                }

                // 2. VARY
                else if (step.type == "Vary") {
                    var m = Regex.Match(clean, @"Vary\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*=\s*([^,]+)(?:,\s*\{([^}]+)\})?\s*\)", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.dc_name = m.Groups[1].Value.Trim();
                        step.vary_variable = m.Groups[2].Value.Trim();
                        string initStr = m.Groups[3].Value.Trim();
                        step.vary_initial_str = initStr;
                        if (double.TryParse(initStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedInit)) {
                            step.vary_initial = parsedInit;
                        }
                        string opts = m.Groups[4].Value;
                        if (!string.IsNullOrEmpty(opts)) {
                            var lowM = Regex.Match(opts, @"Lower\s*=\s*([^,}]+)");
                            if (lowM.Success) {
                                step.vary_lower_str = lowM.Groups[1].Value.Trim();
                                if (double.TryParse(step.vary_lower_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double l)) step.vary_lower = l;
                            }
                            var upM = Regex.Match(opts, @"Upper\s*=\s*([^,}]+)");
                            if (upM.Success) {
                                step.vary_upper_str = upM.Groups[1].Value.Trim();
                                if (double.TryParse(step.vary_upper_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double u)) step.vary_upper = u;
                            }
                            var stepM = Regex.Match(opts, @"MaxStep\s*=\s*([^,}]+)");
                            if (stepM.Success) {
                                step.vary_max_step_str = stepM.Groups[1].Value.Trim();
                                if (double.TryParse(step.vary_max_step_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double s)) step.vary_max_step = s;
                            }
                            var pertM = Regex.Match(opts, @"Perturbation\s*=\s*([^,}]+)");
                            if (pertM.Success) {
                                step.vary_perturbation_str = pertM.Groups[1].Value.Trim();
                                if (double.TryParse(step.vary_perturbation_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double p)) step.vary_perturbation = p;
                            }
                        }
                        step.label = $"Vary {step.vary_variable}";
                        return true;
                    }
                }

                // 3. PROPAGATE
                else if (step.type == "Propagate") {
                    var m = Regex.Match(clean, @"Propagate(?:\s+Backwards)?(?:\s+Synchronized|\s+Single)?\s+([A-Za-z0-9_]+)\s*\(\s*([^)]+)\s*\)\s*\{\s*([^}]+)\s*\}", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.propagator = m.Groups[1].Value.Trim();
                        step.is_backwards = clean.IndexOf("Backwards", StringComparison.OrdinalIgnoreCase) >= 0;
                        string stopCondRaw = m.Groups[3].Value.Trim();

                        string[] condChunks = stopCondRaw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        step.stop_conditions.Clear();
                        for (int c = 0; c < condChunks.Length; c++) {
                            string chunk = condChunks[c].Trim();
                            if (string.IsNullOrEmpty(chunk)) continue;
                            if (Regex.IsMatch(chunk, @"^StopTolerance\s*=", RegexOptions.IgnoreCase)) {
                                var tolParts = chunk.Split('=');
                                if (tolParts.Length > 1 && double.TryParse(tolParts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedTol)) {
                                    step.stop_tolerance = parsedTol;
                                }
                                continue;
                            }
                            var cond = new StopCondition();
                            if (chunk.Contains("=")) {
                                var parts = chunk.Split('=');
                                cond.parameter = parts[0].Trim();
                                string rVal = parts.Length > 1 ? parts[1].Trim() : "";
                                cond.target_val_str = rVal;
                                if (double.TryParse(rVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) {
                                    cond.target_val = dVal;
                                }
                            } else {
                                cond.parameter = chunk;
                            }
                            step.stop_conditions.Add(cond);
                        }
                        if (step.stop_conditions.Count > 0) {
                            var first = step.stop_conditions[0];
                            step.stop_type = first.parameter;
                            step.target_val_str = first.target_val_str;
                            step.target_val = first.target_val;
                            step.duration_str = first.target_val_str;
                            if (double.TryParse(first.target_val_str, NumberStyles.Any, CultureInfo.InvariantCulture, out double dur)) {
                                if (step.stop_type.Contains("ElapsedDays")) step.duration = dur * 86400.0;
                                else step.duration = dur;
                            }
                        }
                        return true;
                    }
                }

                // 4. FINITE BURN
                else if (step.type == "FiniteBurn") {
                    var m = Regex.Match(clean, @"BeginFiniteBurn\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.burn_name = m.Groups[1].Value.Trim();
                        return true;
                    }
                }

                // 5. IMPULSIVE BURN / MANEUVER
                else if (step.type == "ImpulsiveBurn") {
                    var m = Regex.Match(clean, @"Maneuver\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.burn_name = m.Groups[1].Value.Trim();
                        return true;
                    }
                }

                // 6. TARGET
                else if (step.type == "Target") {
                    var m = Regex.Match(clean, @"Target\s+([A-Za-z0-9_]+)(?:\s*\{\s*([^}]+)\s*\})?", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.dc_name = m.Groups[1].Value.Trim();
                        if (m.Groups.Count > 2 && !string.IsNullOrEmpty(m.Groups[2].Value)) {
                            string body = m.Groups[2].Value;
                            var smM = Regex.Match(body, @"SolveMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (smM.Success) step.solve_mode = smM.Groups[1].Value;
                            var emM = Regex.Match(body, @"ExitMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (emM.Success) step.exit_mode = emM.Groups[1].Value;
                        }
                        return true;
                    }
                }

                // 7. OPTIMIZE
                else if (step.type == "Optimize") {
                    var m = Regex.Match(clean, @"Optimize\s+([A-Za-z0-9_]+)(?:\s*\{\s*([^}]+)\s*\})?", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.optimizer_name = m.Groups[1].Value.Trim();
                        if (m.Groups.Count > 2 && !string.IsNullOrEmpty(m.Groups[2].Value)) {
                            string body = m.Groups[2].Value;
                            var smM = Regex.Match(body, @"SolveMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (smM.Success) step.solve_mode = smM.Groups[1].Value;
                            var emM = Regex.Match(body, @"ExitMode\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (emM.Success) step.exit_mode = emM.Groups[1].Value;
                        }
                        return true;
                    }
                }

                // 8. MINIMIZE
                else if (step.type == "Minimize") {
                    var m = Regex.Match(clean, @"Minimize\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*\)", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.optimizer_name = m.Groups[1].Value.Trim();
                        step.minimize_variable = m.Groups[2].Value.Trim();
                        return true;
                    }
                }

                // 9. NONLINEAR CONSTRAINT
                else if (step.type == "NonlinearConstraint") {
                    var m = Regex.Match(clean, @"NonlinearConstraint\s+([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_.]+)\s*(<=|>=|==|<|>|=)\s*([^)]+)\)", RegexOptions.IgnoreCase);
                    if (m.Success) {
                        step.optimizer_name = m.Groups[1].Value.Trim();
                        step.constraint_lhs = m.Groups[2].Value.Trim();
                        step.constraint_op = m.Groups[3].Value.Trim();
                        string valStr = m.Groups[4].Value.Trim();
                        if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double nlcVal)) {
                            step.constraint_val = nlcVal;
                        }
                        return true;
                    }
                }

                // 10. EQUATION
                else if (step.type == "Equation") {
                    var m = Regex.Match(clean, @"([A-Za-z0-9_.]+)\s*=\s*([^;]+);?");
                    if (m.Success) {
                        step.variable = m.Groups[1].Value.Trim();
                        step.expression = m.Groups[2].Value.Trim();
                        return true;
                    }
                }

                // 11. CUSTOM SCRIPT
                else if (step.type == "CustomScript") {
                    step.custom_verbatim_script = clean;
                    return true;
                }
            } catch {
                return false;
            }
            return false;
        }
    }
}
