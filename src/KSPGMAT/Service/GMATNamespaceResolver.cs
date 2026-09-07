using System;
using System.Collections.Generic;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// GMAT 树状命名空间反射与智能补全解析引擎 (Hierarchical Namespace Resolver - 310+ Parameters Complete Coverage)
    /// </summary>
    public static class GMATNamespaceResolver {
        /// <summary>
        /// 执行多级点语法解析、能力掩码过滤与动态状态汇聚
        /// </summary>
        public static List<IntelliSenseItem> Resolve(string input, Vessel vessel = null, ParamCapability filterFlags = ParamCapability.Reportable) {
            List<IntelliSenseItem> items = new List<IntelliSenseItem>();
            string q = (input ?? "").Trim();
            string qLower = q.ToLowerInvariant();
            var state = GMATStateManager.Current;

            List<string> allBodies = GMATParameterRegistry.GetAllBodies(vessel);
            List<string> allCoords = GMATParameterRegistry.GetAllCoordinateSystems();
            List<string> allCraft = GMATParameterRegistry.GetAllSpacecraft(vessel);

            // =========================================================================
            // 0. PYTHON NATIVE API & FUNCTIONS NAMESPACE (e.g. "Python.orbit_calculator", "Python.gmatpy")
            // =========================================================================
            if (q.StartsWith("Python.", StringComparison.OrdinalIgnoreCase)) {
                string pySub = q.Substring("Python.".Length).Trim();
                var pyRes = GMATSchemaRegistry.Instance.SearchCapabilities(pySub, "funcs", 20);
                for (int p = 0; p < pyRes.Count; p++) {
                    var pf = pyRes[p];
                    string insert = $"Python.{pf.title}";
                    items.Add(new IntelliSenseItem(insert, pf.title, "Python", pf.doc ?? pf.subtitle, IntelliSenseKind.Function));
                }
                if (items.Count > 0) return items;
            }

            // =========================================================================
            // 1. MULTI-LEVEL DOT SYNTAX (e.g. "Sat.Earth.SMA", "Sat.EarthMoonRot.Z", "Sat.RL10.ThrustDirection1")
            // =========================================================================
            if (q.Contains(".")) {
                string[] parts = q.Split('.');

                // ---------------------------------------------------------------------
                // CASE A: 3-Level or higher hierarchy (e.g. "Sat.EarthMoonRot.Z", "Sat.Earth.IncomingDHA")
                // ---------------------------------------------------------------------
                if (parts.Length >= 3) {
                    string p0 = parts[0].Trim();
                    string p1 = parts[1].Trim();
                    string rhs = parts[2].Trim().ToLowerInvariant();

                    bool isCraft = allCraft.Exists(c => c.Equals(p0, StringComparison.OrdinalIgnoreCase)) || p0.StartsWith("Sat", StringComparison.OrdinalIgnoreCase);

                    if (isCraft) {
                        // Sub-case A1: p1 is a Coordinate System (e.g. "Sat.EarthMoonRot.Z", "Sat.EarthMJ2000Eq.VZ")
                        bool isCoord = allCoords.Exists(c => c.Equals(p1, StringComparison.OrdinalIgnoreCase)) ||
                                       p1.IndexOf("Rot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("Eq", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("Inertial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("Fixed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("ICRF", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isCoord) {
                            AddMatchingParams(items, GMATParamCatalog.CoordinateCartesian, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.CoordinateSystem);
                            return items;
                        }

                        // Sub-case A2: p1 is an Attached Thruster (e.g. "Sat.RL10.ThrustDirection1")
                        GMATThrusterModel matchedThruster = null;
                        if (state != null && state.Thrusters != null) {
                            matchedThruster = state.Thrusters.Find(t => t.Id.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        }
                        if (matchedThruster != null || p1.StartsWith("Engine", StringComparison.OrdinalIgnoreCase) || p1.StartsWith("Burn", StringComparison.OrdinalIgnoreCase) || p1.Equals("MainEngine", StringComparison.OrdinalIgnoreCase)) {
                            AddMatchingParams(items, GMATParamCatalog.ThrusterProperties, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            return items;
                        }

                        // Sub-case A3: p1 is an Attached Tank (e.g. "Sat.Tank0.FuelMass")
                        GMATTankModel matchedTank = null;
                        if (state != null && state.Tanks != null) {
                            matchedTank = state.Tanks.Find(t => t.Id.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        }
                        if (matchedTank != null || p1.StartsWith("Tank", StringComparison.OrdinalIgnoreCase)) {
                            AddMatchingParams(items, GMATParamCatalog.TankProperties, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            return items;
                        }

                        // Sub-case A4: p1 is a Celestial Body (e.g. "Sat.Earth.SMA", "Sat.Luna.RadApo", "Sat.Earth.IncomingDHA")
                        bool isBody = allBodies.Exists(b => b.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        if (isBody) {
                            AddMatchingParams(items, GMATParamCatalog.DirectKeplerian, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            AddMatchingParams(items, GMATParamCatalog.SphericalAndPlanetodetic, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            AddMatchingParams(items, GMATParamCatalog.HyperbolicAndBPlane, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            AddMatchingParams(items, GMATParamCatalog.EquinoctialAndDelaunay, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            AddMatchingParams(items, GMATParamCatalog.BrouwerMeanElements, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);

                            // Extremum events (ONLY if filterFlags allows ExtremumEvents, e.g. Propagate Stop)
                            if ((filterFlags & ParamCapability.DiscontinuousExtremumEvent) != 0) {
                                AddMatchingParams(items, GMATParamCatalog.ExtremumEvents, $"{p0}.{p1}", rhs, filterFlags, IntelliSenseKind.Parameter);
                            }
                            return items;
                        }
                    }
                }

                // ---------------------------------------------------------------------
                // CASE B: 2-Level hierarchy (e.g. "Sat.", "Sat.Earth", "Sat.RL10", "Sat.SMA", "Sat.IncomingDHA")
                // ---------------------------------------------------------------------
                int lastDotIdx = q.LastIndexOf('.');
                string lhs = q.Substring(0, lastDotIdx).Trim();
                string rhsSingle = q.Substring(lastDotIdx + 1).Trim().ToLowerInvariant();

                string matchedCraft = allCraft.Find(cr => cr.Equals(lhs, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matchedCraft) || lhs.StartsWith("Sat", StringComparison.OrdinalIgnoreCase)) {
                    string cr = !string.IsNullOrEmpty(matchedCraft) ? matchedCraft : lhs;
                    string actBody = (vessel != null && vessel.mainBody != null) ? GMATParameterRegistry.MapBodyName(vessel.mainBody.name) : "Earth";

                    // B1. Attached Thrusters Steering Vectors (TOP PRIORITY for Vary & Control)
                    if (state != null && state.Thrusters != null && state.Thrusters.Count > 0) {
                        for (int t = 0; t < state.Thrusters.Count; t++) {
                            var thr = state.Thrusters[t];
                            string thName = thr.Id;
                            string thLower = thName.ToLowerInvariant();
                            if (string.IsNullOrEmpty(rhsSingle) || thLower.StartsWith(rhsSingle) || thLower.Contains(rhsSingle)) {
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.ThrustDirection1", I18n.Format("ns_tdir1_label", thName), I18n.T("ns_tdir1_unit"), I18n.Format("ns_tdir1_doc", thName), IntelliSenseKind.Parameter, 1.0, 0.05));
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.ThrustDirection2", I18n.Format("ns_tdir2_label", thName), I18n.T("ns_tdir2_unit"), I18n.Format("ns_tdir2_doc", thName), IntelliSenseKind.Parameter, 0.0, 0.05));
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.ThrustDirection3", I18n.Format("ns_tdir3_label", thName), I18n.T("ns_tdir3_unit"), I18n.Format("ns_tdir3_doc", thName), IntelliSenseKind.Parameter, 0.0, 0.05));
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.", I18n.Format("ns_thruster_obj_label", thName), I18n.T("ns_thruster_obj_tag"), I18n.Format("ns_thruster_obj_doc", thName), IntelliSenseKind.Spacecraft));
                            }
                        }
                    }

                    // B2. Direct Keplerian Orbit Elements (Sat.SMA, Sat.INC, Sat.ECC...)
                    AddMatchingParams(items, GMATParamCatalog.DirectKeplerian, cr, rhsSingle, filterFlags, IntelliSenseKind.Parameter);

                    // B3. Active Central Body Relative Properties (Sat.Earth.Altitude, Sat.Earth.IncomingDHA, Sat.Earth.BdotT...)
                    AddMatchingParams(items, GMATParamCatalog.SphericalAndPlanetodetic, $"{cr}.{actBody}", rhsSingle, filterFlags, IntelliSenseKind.Parameter);
                    AddMatchingParams(items, GMATParamCatalog.HyperbolicAndBPlane, $"{cr}.{actBody}", rhsSingle, filterFlags, IntelliSenseKind.Parameter);
                    AddMatchingParams(items, GMATParamCatalog.EquinoctialAndDelaunay, $"{cr}.{actBody}", rhsSingle, filterFlags, IntelliSenseKind.Parameter);
                    AddMatchingParams(items, GMATParamCatalog.BrouwerMeanElements, $"{cr}.{actBody}", rhsSingle, filterFlags, IntelliSenseKind.Parameter);

                    // B4. Extremum Events (ONLY if filterFlags allows ExtremumEvents, e.g. Propagate Stop)
                    if ((filterFlags & ParamCapability.DiscontinuousExtremumEvent) != 0) {
                        AddMatchingParams(items, GMATParamCatalog.ExtremumEvents, $"{cr}.{actBody}", rhsSingle, filterFlags, IntelliSenseKind.Parameter);
                    }

                    // B5. Spacecraft Direct Physical Hardware Properties (Mass, Cd, Cr, Time)
                    AddMatchingParams(items, GMATParamCatalog.SpacecraftHardware, cr, rhsSingle, filterFlags, IntelliSenseKind.Parameter);

                    // B7. Attitude & Orientation (Q1~Q4, Euler)
                    AddMatchingParams(items, GMATParamCatalog.AttitudeAndOrientation, cr, rhsSingle, filterFlags, IntelliSenseKind.Parameter);

                    // B8. Celestial Body Names Namespace Expanders
                    for (int b = 0; b < allBodies.Count; b++) {
                        string bd = allBodies[b];
                        if (string.IsNullOrEmpty(rhsSingle) || bd.ToLowerInvariant().StartsWith(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cr}.{bd}.", $"{bd}...", I18n.T("ns_body_ns_tag"), I18n.Format("ns_body_ns_doc", bd), IntelliSenseKind.CelestialBody));
                        }
                    }

                    // B9. Coordinate Systems Namespace Expanders
                    for (int c = 0; c < allCoords.Count; c++) {
                        string cs = allCoords[c];
                        if (string.IsNullOrEmpty(rhsSingle) || cs.ToLowerInvariant().StartsWith(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cr}.{cs}.", $"{cs}...", I18n.T("ns_coord_ns_tag"), I18n.Format("ns_coord_ns_doc", cs), IntelliSenseKind.CoordinateSystem));
                        }
                    }

                    return items;
                }
            }

            // =========================================================================
            // 2. ROOT-LEVEL IDENTIFIERS & GLOBAL VARIABLES
            // =========================================================================

            // 2a. Global Variables from GMATStateManager
            if (state != null && state.Variables != null) {
                for (int i = 0; i < state.Variables.Count; i++) {
                    var v = state.Variables[i];
                    if (string.IsNullOrEmpty(q) || v.Name.ToLowerInvariant().Contains(qLower)) {
                        items.Add(new IntelliSenseItem(v.Name, v.Name, I18n.T("ns_var_tag"), I18n.Format("ns_var_doc", v.InitialValue), IntelliSenseKind.Variable, v.InitialValue, 1.0));
                    }
                }
            }

            // 2b. Global Solvers from GMATStateManager
            if (state != null && state.Solvers != null) {
                for (int i = 0; i < state.Solvers.Count; i++) {
                    var slv = state.Solvers[i];
                    if (string.IsNullOrEmpty(q) || slv.Name.ToLowerInvariant().Contains(qLower)) {
                        items.Add(new IntelliSenseItem(slv.Name, slv.Name, I18n.T("ns_solver_tag"), I18n.Format("ns_solver_doc", slv.Algorithm, slv.MaxIterations), IntelliSenseKind.Solver));
                    }
                }
            }

            // 2c. Spacecraft Root Objects (Sat.)
            for (int i = 0; i < allCraft.Count; i++) {
                string cr = allCraft[i];
                if (string.IsNullOrEmpty(q) || cr.ToLowerInvariant().StartsWith(qLower)) {
                    items.Add(new IntelliSenseItem($"{cr}.", $"{cr}...", I18n.T("ns_sc_obj_tag"), I18n.Format("ns_sc_obj_doc", cr), IntelliSenseKind.Spacecraft));
                }
            }

            // 2d. Direct Keplerian Elements if partially matching (SMA, INC, ECC...) 基线1.3: 前缀经 SSOT 动态获取
            string craft = GMATStateManager.Current.GetSpacecraftName();
            string actBodyRoot = (vessel != null && vessel.mainBody != null) ? GMATParameterRegistry.MapBodyName(vessel.mainBody.name) : "Earth";
            string craftCurrentBody = GMATStateManager.Current.GetQualifiedSpacecraftProperty(actBodyRoot);
            AddMatchingParams(items, GMATParamCatalog.DirectKeplerian, craft, qLower, filterFlags, IntelliSenseKind.Parameter);
            AddMatchingParams(items, GMATParamCatalog.SphericalAndPlanetodetic, craftCurrentBody, qLower, filterFlags, IntelliSenseKind.Parameter);
            AddMatchingParams(items, GMATParamCatalog.HyperbolicAndBPlane, craftCurrentBody, qLower, filterFlags, IntelliSenseKind.Parameter);

            // 2e. GMAT Official API Classes & Native Solvers (for IDE/Script code completions)
            if (!string.IsNullOrEmpty(q) && q.Length >= 2 && !q.Contains(".")) {
                var classMatches = GMATSchemaRegistry.Instance.SearchCapabilities(q, "classes", 10);
                for (int i = 0; i < classMatches.Count; i++) {
                    var cm = classMatches[i];
                    items.Add(new IntelliSenseItem(
                        cm.insertSnippet ?? cm.title,
                        cm.title,
                        cm.signature ?? "API",
                        cm.doc ?? cm.subtitle,
                        IntelliSenseKind.Solver
                    ));
                }
            }

            return items;
        }

        private static void AddMatchingParams(List<IntelliSenseItem> items, List<GMATParamDefinition> catalog, string prefix, string rhs, ParamCapability filterFlags, IntelliSenseKind kind) {
            for (int i = 0; i < catalog.Count; i++) {
                var def = catalog[i];
                if ((def.Capability & filterFlags) != 0) {
                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs) || def.Name.ToLowerInvariant().Contains(rhs)) {
                        string doc = def.GetDoc();
                        string fullPath = $"{prefix}.{def.Name}";
                        items.Add(new IntelliSenseItem(fullPath, def.Name, def.GetUnit(), doc, kind, def.DefaultValue, def.TypicalStep));
                    }
                }
            }
        }
    }
}
