using System;
using System.Collections.Generic;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// GMAT 树状命名空间反射与智能补全解析引擎 (Hierarchical Namespace Resolver)
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
            var lang = I18n.Current;

            List<string> allBodies = GMATParameterRegistry.GetAllBodies(vessel);
            List<string> allCoords = GMATParameterRegistry.GetAllCoordinateSystems();
            List<string> allCraft = GMATParameterRegistry.GetAllSpacecraft(vessel);

            // =========================================================================
            // 1. MULTI-LEVEL DOT SYNTAX (e.g. "Sat.Earth.SMA", "Sat.EarthMoonRot.Z", "Sat.RL10.ThrustDirection1")
            // =========================================================================
            if (q.Contains(".")) {
                string[] parts = q.Split('.');

                // ---------------------------------------------------------------------
                // CASE A: 3-Level or higher hierarchy (e.g. "Sat.EarthMoonRot.Z", "Sat.Earth.SMA", "Sat.RL10.ThrustDirection1")
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
                            for (int k = 0; k < GMATParamCatalog.CoordinateCartesian.Count; k++) {
                                var def = GMATParamCatalog.CoordinateCartesian[k];
                                if ((def.Capability & filterFlags) != 0) {
                                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs)) {
                                        string doc = (lang == I18n.Lang.EN) ? $"{p0} in {p1}: {def.DocEn}" : $"{p0} 在 {p1} 参考系: {def.DocZh}";
                                        items.Add(new IntelliSenseItem($"{p0}.{p1}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.CoordinateSystem, def.DefaultValue, def.TypicalStep));
                                    }
                                }
                            }
                            return items;
                        }

                        // Sub-case A2: p1 is an Attached Thruster (e.g. "Sat.RL10.ThrustDirection1")
                        GMATThrusterModel matchedThruster = null;
                        if (state != null && state.Thrusters != null) {
                            matchedThruster = state.Thrusters.Find(t => t.Id.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        }
                        if (matchedThruster != null || p1.StartsWith("Engine", StringComparison.OrdinalIgnoreCase) || p1.StartsWith("Burn", StringComparison.OrdinalIgnoreCase) || p1.Equals("MainEngine", StringComparison.OrdinalIgnoreCase)) {
                            for (int k = 0; k < GMATParamCatalog.ThrusterProperties.Count; k++) {
                                var def = GMATParamCatalog.ThrusterProperties[k];
                                if ((def.Capability & filterFlags) != 0) {
                                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs)) {
                                        string doc = (lang == I18n.Lang.EN) ? $"{p0}.{p1}: {def.DocEn}" : $"{p0}.{p1}: {def.DocZh}";
                                        items.Add(new IntelliSenseItem($"{p0}.{p1}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                                    }
                                }
                            }
                            return items;
                        }

                        // Sub-case A3: p1 is an Attached Tank (e.g. "Sat.Tank0.FuelMass")
                        GMATTankModel matchedTank = null;
                        if (state != null && state.Tanks != null) {
                            matchedTank = state.Tanks.Find(t => t.Id.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        }
                        if (matchedTank != null || p1.StartsWith("Tank", StringComparison.OrdinalIgnoreCase)) {
                            for (int k = 0; k < GMATParamCatalog.TankProperties.Count; k++) {
                                var def = GMATParamCatalog.TankProperties[k];
                                if ((def.Capability & filterFlags) != 0) {
                                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs)) {
                                        string doc = (lang == I18n.Lang.EN) ? $"{p0}.{p1}: {def.DocEn}" : $"{p0}.{p1}: {def.DocZh}";
                                        items.Add(new IntelliSenseItem($"{p0}.{p1}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                                    }
                                }
                            }
                            return items;
                        }

                        // Sub-case A4: p1 is a Celestial Body (e.g. "Sat.Earth.SMA", "Sat.Luna.RadApo", "Sat.Earth.Apoapsis")
                        bool isBody = allBodies.Exists(b => b.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        if (isBody) {
                            // 1. Continuous parameters (Altitude, RMAG, VMAG, Latitude...)
                            for (int k = 0; k < GMATParamCatalog.BodyRelativeParameters.Count; k++) {
                                var def = GMATParamCatalog.BodyRelativeParameters[k];
                                if ((def.Capability & filterFlags) != 0) {
                                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs)) {
                                        string doc = (lang == I18n.Lang.EN) ? $"{p0} relative to {p1}: {def.DocEn}" : $"{p0} 相对 {p1} 中心: {def.DocZh}";
                                        items.Add(new IntelliSenseItem($"{p0}.{p1}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                                    }
                                }
                            }

                            // 2. Direct Keplerian elements relative to this body (SMA, ECC, INC...)
                            for (int k = 0; k < GMATParamCatalog.DirectKeplerian.Count; k++) {
                                var def = GMATParamCatalog.DirectKeplerian[k];
                                if ((def.Capability & filterFlags) != 0) {
                                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs)) {
                                        string doc = (lang == I18n.Lang.EN) ? $"{p0} in {p1} orbit: {def.DocEn}" : $"{p0} 在 {p1} 轨道: {def.DocZh}";
                                        items.Add(new IntelliSenseItem($"{p0}.{p1}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                                    }
                                }
                            }

                            // 3. Extremum events (Apoapsis, Periapsis...) ONLY if filterFlags allows ExtremumEvents!
                            if ((filterFlags & ParamCapability.DiscontinuousExtremumEvent) != 0) {
                                for (int k = 0; k < GMATParamCatalog.ExtremumEvents.Count; k++) {
                                    var def = GMATParamCatalog.ExtremumEvents[k];
                                    if (string.IsNullOrEmpty(rhs) || def.Name.ToLowerInvariant().StartsWith(rhs)) {
                                        string doc = (lang == I18n.Lang.EN) ? $"{p0} around {p1}: {def.DocEn}" : $"{p0} 绕 {p1}: {def.DocZh}";
                                        items.Add(new IntelliSenseItem($"{p0}.{p1}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                                    }
                                }
                            }
                            return items;
                        }
                    }
                }

                // ---------------------------------------------------------------------
                // CASE B: 2-Level hierarchy (e.g. "Sat.", "Sat.Earth", "Sat.RL10", "Sat.SMA")
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
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.ThrustDirection1", $"{thName}.ThrustDirection1 [切向 V]", "[切向 +V/-V]", $"{thName}: 切向推力分量 (顺行+1/逆行-1 主推进)", IntelliSenseKind.Parameter, 1.0, 0.05));
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.ThrustDirection2", $"{thName}.ThrustDirection2 [副法向 N]", "[副法向 +N/-N]", $"{thName}: 副法向推力偏转 (变倾角打靶变量)", IntelliSenseKind.Parameter, 0.0, 0.05));
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.ThrustDirection3", $"{thName}.ThrustDirection3 [法向 B]", "[法向 +B/-B]", $"{thName}: 法向/径向推力偏转 (高度与偏心率微调)", IntelliSenseKind.Parameter, 0.0, 0.05));
                                items.Add(new IntelliSenseItem($"{cr}.{thName}.", $"{thName}...", "[推力器对象]", $"{thName} 硬件推力器", IntelliSenseKind.Spacecraft));
                            }
                        }
                    }

                    // B2. Direct Keplerian Orbit Elements (Sat.SMA, Sat.INC, Sat.ECC...)
                    for (int op = 0; op < GMATParamCatalog.DirectKeplerian.Count; op++) {
                        var def = GMATParamCatalog.DirectKeplerian[op];
                        if ((def.Capability & filterFlags) != 0) {
                            if (string.IsNullOrEmpty(rhsSingle) || def.Name.ToLowerInvariant().StartsWith(rhsSingle)) {
                                string doc = (lang == I18n.Lang.EN) ? $"{cr}: {def.DocEn}" : $"{cr}: {def.DocZh}";
                                items.Add(new IntelliSenseItem($"{cr}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                            }
                        }
                    }

                    // B3. Active Central Body Dependent Orbit Properties (e.g. Sat.Earth.Altitude, Sat.Earth.RMAG)
                    for (int bp = 0; bp < GMATParamCatalog.BodyRelativeParameters.Count; bp++) {
                        var def = GMATParamCatalog.BodyRelativeParameters[bp];
                        if ((def.Capability & filterFlags) != 0) {
                            if (string.IsNullOrEmpty(rhsSingle) || def.Name.ToLowerInvariant().StartsWith(rhsSingle) || rhsSingle.Equals(actBody.ToLowerInvariant())) {
                                string doc = (lang == I18n.Lang.EN) ? $"{actBody} {def.DocEn}" : $"{actBody} {def.DocZh}";
                                items.Add(new IntelliSenseItem($"{cr}.{actBody}.{def.Name}", $"{actBody}.{def.Name}", def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                            }
                        }
                    }

                    // B4. Extremum Events (ONLY if filterFlags allows ExtremumEvents, e.g. Propagate Stop)
                    if ((filterFlags & ParamCapability.DiscontinuousExtremumEvent) != 0) {
                        for (int ep = 0; ep < GMATParamCatalog.ExtremumEvents.Count; ep++) {
                            var def = GMATParamCatalog.ExtremumEvents[ep];
                            if (string.IsNullOrEmpty(rhsSingle) || def.Name.ToLowerInvariant().StartsWith(rhsSingle)) {
                                items.Add(new IntelliSenseItem($"{cr}.{actBody}.{def.Name}", $"{actBody}.{def.Name}", def.Unit, def.DocZh, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                            }
                        }
                    }

                    // B5. Coordinate System Cartesian Parameters (e.g. Sat.EarthMJ2000Eq.VZ, Sat.EarthMoonRot.Z)
                    items.Add(new IntelliSenseItem($"{cr}.EarthMJ2000Eq.VZ", "EarthMJ2000Eq.VZ", "[Km/s]", "J2000赤道惯性系法向/垂向速度 (VZ)", IntelliSenseKind.Parameter, 0.0, 0.001));
                    items.Add(new IntelliSenseItem($"{cr}.EarthMJ2000Eq.Z", "EarthMJ2000Eq.Z", "[Km]", "J2000赤道面垂直位置 (Z=0)", IntelliSenseKind.Parameter, 0.0, 0.01));
                    items.Add(new IntelliSenseItem($"{cr}.EarthMoonRot.Z", "EarthMoonRot.Z", "[Km]", "地月旋转系垂向位置 (NRHO / 晕轨道穿过点)", IntelliSenseKind.Parameter, 0.0, 0.01));

                    // B6. Spacecraft Direct Physical Hardware Properties
                    for (int p = 0; p < GMATParamCatalog.SpacecraftHardware.Count; p++) {
                        var def = GMATParamCatalog.SpacecraftHardware[p];
                        if ((def.Capability & filterFlags) != 0) {
                            if (string.IsNullOrEmpty(rhsSingle) || def.Name.ToLowerInvariant().StartsWith(rhsSingle)) {
                                string doc = (lang == I18n.Lang.EN) ? $"{cr}: {def.DocEn}" : $"{cr}: {def.DocZh}";
                                items.Add(new IntelliSenseItem($"{cr}.{def.Name}", def.Name, def.Unit, doc, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                            }
                        }
                    }

                    // B7. Celestial Body Names Namespace Expanders
                    for (int b = 0; b < allBodies.Count; b++) {
                        string bName = allBodies[b];
                        if (string.IsNullOrEmpty(rhsSingle) || bName.ToLowerInvariant().StartsWith(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cr}.{bName}.", $"{bName}...", "[天体命名空间]", $"{bName} 天体相关轨道参数", IntelliSenseKind.CelestialBody));
                        }
                    }

                    // B8. Coordinate Systems Namespace Expanders
                    for (int c = 0; c < allCoords.Count; c++) {
                        string cName = allCoords[c];
                        if (string.IsNullOrEmpty(rhsSingle) || cName.ToLowerInvariant().StartsWith(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cr}.{cName}.", $"{cName}...", "[坐标系命名空间]", $"{cName} 坐标系空间状态", IntelliSenseKind.CoordinateSystem));
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
                        items.Add(new IntelliSenseItem(v.Name, v.Name, "[Variable]", $"全局标量变量 (当前值: {v.InitialValue})", IntelliSenseKind.Variable, v.InitialValue, 1.0));
                    }
                }
            }

            // 2b. Global Solvers from GMATStateManager
            if (state != null && state.Solvers != null) {
                for (int i = 0; i < state.Solvers.Count; i++) {
                    var slv = state.Solvers[i];
                    if (string.IsNullOrEmpty(q) || slv.Name.ToLowerInvariant().Contains(qLower)) {
                        items.Add(new IntelliSenseItem(slv.Name, slv.Name, "[Solver]", $"差分修正求解器 ({slv.Algorithm}, 最大迭代: {slv.MaxIterations})", IntelliSenseKind.Solver));
                    }
                }
            }

            // 2c. Spacecraft Root Objects (Sat.)
            for (int i = 0; i < allCraft.Count; i++) {
                string cr = allCraft[i];
                if (string.IsNullOrEmpty(q) || cr.ToLowerInvariant().Contains(qLower)) {
                    items.Add(new IntelliSenseItem($"{cr}.", $"{cr}...", "[航天器对象]", $"主航天器实例 ({cr})", IntelliSenseKind.Spacecraft));
                }
            }

            // 2d. Direct Keplerian Elements if partially matching (SMA, INC, ECC...)
            for (int i = 0; i < GMATParamCatalog.DirectKeplerian.Count; i++) {
                var def = GMATParamCatalog.DirectKeplerian[i];
                if ((def.Capability & filterFlags) != 0) {
                    if (string.IsNullOrEmpty(q) || def.Name.ToLowerInvariant().StartsWith(qLower)) {
                        items.Add(new IntelliSenseItem($"Sat.{def.Name}", def.Name, def.Unit, def.DocZh, IntelliSenseKind.Parameter, def.DefaultValue, def.TypicalStep));
                    }
                }
            }

            return items;
        }
    }
}
