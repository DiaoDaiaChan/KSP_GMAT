using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace KSPGMAT {
    [Serializable]
    public struct GMATParam {
        public string Name;
        public string Category;
        public string Description;
        public string DescriptionZh;
        public string DescriptionEn;
        public string Example;
        public string Unit;
        public string DependencyObject;
        public ParamCapability Capability; // 基线 3.1: 参数能力位掩码

        public GMATParam(string name, string category, string desc, string example) {
            Name = name;
            Category = category;
            Description = desc;
            DescriptionZh = desc;
            DescriptionEn = desc;
            Example = example;
            Unit = "";
            DependencyObject = "";
            Capability = ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable; // 默认连续可导
        }

        public string GetLocalizedDescription() {
            if (I18n.Current == I18n.Lang.EN) {
                return !string.IsNullOrEmpty(DescriptionEn) ? DescriptionEn : Description;
            } else {
                return !string.IsNullOrEmpty(DescriptionZh) ? DescriptionZh : Description;
            }
        }
    }

    [Serializable]
    public struct PropStopDef {
        public string Key;         // GMAT 内部变量名 (如 Apoapsis, Altitude)
        public string DisplayName; // 显示名称 (包含单位和说明)
        public string Category;    // 参数分类
        public string Unit;        // 单位 (km, °, s 等)
        public string Description; // 详细技术说明
        public string DescriptionZh;
        public string DescriptionEn;
        public bool IsExtremum;    // 是否为极值点 (无需输入右侧目标值)
        public double DefaultValue;
        public double DefaultTolerance;

        public PropStopDef(string key, string name, string category, string unit, string desc, bool isExtremum, double defVal, double defTol = 1e-5) {
            Key = key;
            DisplayName = name;
            Category = category;
            Unit = unit;
            Description = desc;
            IsExtremum = isExtremum;
            DefaultValue = defVal;
            DefaultTolerance = defTol;
            DescriptionZh = desc;
            DescriptionEn = desc;
        }

        public string GetLocalizedDescription() {
            if (I18n.Current == I18n.Lang.EN) {
                return !string.IsNullOrEmpty(DescriptionEn) ? DescriptionEn : Description;
            } else {
                return !string.IsNullOrEmpty(DescriptionZh) ? DescriptionZh : Description;
            }
        }
    }

    // JSON DTOs matching gmatpy-generated capabilities
    [Serializable]
    public class RawGmatParamItem {
        public string key;
        public string name;
        public string description;
        public string description_zh;
        public string description_en;
        public string category_zh;
        public string category_en;
        public string unit;
        public string dep_object;
        public float default_value;
        public float default_tolerance;
        public bool is_extremum;
    }

    [Serializable]
    public class RawGmatCapabilitiesPayload {
        public string engine;
        public string source;
        public int parameter_count;
        public int stop_condition_count;
        public List<RawGmatParamItem> parameters = new List<RawGmatParamItem>();
        public List<RawGmatParamItem> stop_conditions = new List<RawGmatParamItem>();
        public List<string> integrators = new List<string>();
        public List<string> physical_models = new List<string>();
        public List<string> bodies = new List<string>();
    }

    [Serializable]
    public struct ParamSuggestion {
        public string Key;
        public string DisplayText;
        public double DefaultVal;
        public double DefaultTol;

        public ParamSuggestion(string key, string text, double val, double tol) {
            Key = key;
            DisplayText = text;
            DefaultVal = val;
            DefaultTol = tol;
        }
    }

    /// <summary>
    /// Dynamic GMAT Parameter & Capability Registry.
    /// Loaded dynamically from gmatpy introspection via gmat_capabilities.json or /api/parameters REST API.
    /// No parameters are hardcoded directly in C# code.
    /// </summary>

        public enum IntelliSenseKind {
        CelestialBody,
        CoordinateSystem,
        Spacecraft,
        Parameter,
        Variable,
        Propagator,
        Solver,
        DirectCoord
    }

    [Serializable]
    public class IntelliSenseItem {
        public string InsertText;
        public string Label;
        public string TypeTag;
        public string Doc;
        public IntelliSenseKind Kind;
        public double RecommendedValue;
        public double RecommendedTolerance;

        public IntelliSenseItem(string insert, string label, string type, string doc, IntelliSenseKind kind, double val = 0.0, double tol = 0.0) {
            InsertText = insert;
            Label = label;
            TypeTag = type;
            Doc = doc;
            Kind = kind;
            RecommendedValue = val;
            RecommendedTolerance = tol;
        }

        public string GetIcon() {
            // IMGUI 字体不渲染彩色 emoji，图标前缀统一移除，仅保留文本标签。
            return "";
        }
    }

    [Serializable]
    public class CustomCoordSystemDef {
        public string name = "NewCoordSys";
        public string origin = "Earth";
        public string axes = "MJ2000Eq";
        public string primary = "Earth";
        public string secondary = "Luna";
        public string xAxis = "R";
        public string yAxis = "None";
        public string zAxis = "N";
        public string epoch = "";
        public string description = "";
        public bool isBuiltIn = false;

        public CustomCoordSystemDef Clone() {
            return new CustomCoordSystemDef {
                name = this.name,
                origin = this.origin,
                axes = this.axes,
                primary = this.primary,
                secondary = this.secondary,
                xAxis = this.xAxis,
                yAxis = this.yAxis,
                zAxis = this.zAxis,
                epoch = this.epoch,
                description = this.description,
                isBuiltIn = this.isBuiltIn
            };
        }
    }

    public static class GMATParameterRegistry {
        public static List<string> CategoriesList = new List<string>();
        public static List<GMATParam> AllParameters = new List<GMATParam>();

        public static List<string> PropStopCategoriesList = new List<string>();
        public static List<PropStopDef> AllPropStopDefs = new List<PropStopDef>();
        public static List<RawGmatParamItem> rawParamsCache = new List<RawGmatParamItem>();
        public static List<RawGmatParamItem> rawStopsCache = new List<RawGmatParamItem>();

        public static List<string> AvailableIntegrators = new List<string>();
        public static List<string> AvailablePhysicalModels = new List<string>();
        public static List<string> AvailableBodies = new List<string>();
        public static List<string> AvailableCoordinateSystems = new List<string>();
        public static List<string> AvailableSolvers = new List<string>();
        public static List<string> AvailablePropagators = new List<string>();
        public static List<string> AvailableDefaultSpacecraft = new List<string>();
        public static List<string> AvailableStandardVariables = new List<string>();

        // Real-Time Runtime Registries (Mutated live during flight session & mission editing)
        public static readonly Dictionary<string, string> RealtimeUserVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> RealtimeUserBurns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> RealtimeUserPropagators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> RealtimeUserSpacecraft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> RealtimeUserBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> RealtimeUserCoordinateSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static List<CustomCoordSystemDef> UserCoordinateSystems = new List<CustomCoordSystemDef>();
        public static List<string> AllowedAxisTypes = new List<string>() {
            "MJ2000Eq", "MJ2000Ec", "ICRF", "ObjectReferenced", "BodyInertial", "BodyFixed",
            "BodySpinSun", "Equator", "Topocentric", "GSE", "GSM", "MODEq", "MODEc", "TODEq", "TODEc"
        };
        public static List<string> AllowedAxesVectors = new List<string>() { "R", "-R", "V", "-V", "N", "-N", "None" };

        public static void AddOrUpdateCoordinateSystem(CustomCoordSystemDef cs) {
            if (cs == null || string.IsNullOrEmpty(cs.name)) return;
            string cleanName = SanitizeGmatIdentifier(cs.name, "CoordSys");
            cs.name = cleanName;

            int idx = UserCoordinateSystems.FindIndex(c => c.name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) {
                UserCoordinateSystems[idx] = cs;
            } else {
                UserCoordinateSystems.Add(cs);
            }

            if (!RealtimeUserCoordinateSystems.Contains(cleanName)) {
                RealtimeUserCoordinateSystems.Add(cleanName);
            }
            if (!AvailableCoordinateSystems.Contains(cleanName)) {
                AvailableCoordinateSystems.Add(cleanName);
            }
            SaveUserCoordinateSystems();
        }

        public static void DeleteCoordinateSystem(string name) {
            if (string.IsNullOrEmpty(name)) return;
            UserCoordinateSystems.RemoveAll(c => c.name.Equals(name, StringComparison.OrdinalIgnoreCase));
            RealtimeUserCoordinateSystems.Remove(name);
            AvailableCoordinateSystems.Remove(name);
            SaveUserCoordinateSystems();
        }

        public static string StripCraftPrefix(string param) {
            if (string.IsNullOrEmpty(param)) return "";
            param = param.Trim();
            int firstDot = param.IndexOf('.');
            if (firstDot > 0) {
                string prefix = param.Substring(0, firstDot);
                string remainder = param.Substring(firstDot + 1);
                if (remainder.StartsWith("Earth", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Luna", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Sun", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Jupiter", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Mars", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Venus", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Elapsed", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("SMA", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("ECC", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("INC", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("RAAN", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("AOP", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("TA", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Altitude", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Apoapsis", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Periapsis", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("RMAG", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Latitude", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Longitude", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("X", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Y", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("VX", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("VY", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("VZ", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("DryMass", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith("TotalMass", StringComparison.OrdinalIgnoreCase) ||
                    remainder.Contains("MJ2000") ||
                    prefix.StartsWith("Sat", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("Spacecraft", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("SC", StringComparison.OrdinalIgnoreCase)) {
                    return remainder;
                }
            }
            return param;
        }

        public static bool IsSpacecraftDirectProperty(string paramName) {
            if (string.IsNullOrEmpty(paramName)) return false;
            string[] direct = new string[] {
                "SMA", "ECC", "INC", "RAAN", "AOP", "TA", "EA", "MA", "RadPer", "RadApo",
                "TotalMass", "DryMass", "FuelMass", "ElapsedSecs", "ElapsedDays",
                "A1ModJulian", "TAIModJulian", "UTCModJulian", "C3Energy", "Energy"
            };
            return Array.Exists(direct, p => p.Equals(paramName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetUserCoordsSavePath() {
            try {
                string root = KSPUtil.ApplicationRootPath;
                string dir = Path.Combine(root, "GameData/KSPGMAT/PluginData");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "user_coordinate_systems.json");
            } catch {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_coordinate_systems.json");
            }
        }

        public static void SaveUserCoordinateSystems() {
            try {
                string path = GetUserCoordsSavePath();
                List<string> jsonItems = new List<string>();
                for (int i = 0; i < UserCoordinateSystems.Count; i++) {
                    jsonItems.Add(JsonUtility.ToJson(UserCoordinateSystems[i]));
                }
                string fullJson = "[" + "\n" + string.Join("," + "\n", jsonItems.ToArray()) + "\n" + "]";
                File.WriteAllText(path, fullJson, System.Text.Encoding.UTF8);
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] Failed to save user coordinate systems: " + ex.Message);
            }
        }

        public static void LoadUserCoordinateSystems() {
            try {
                string path = GetUserCoordsSavePath();
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                if (string.IsNullOrEmpty(json)) return;

                int cur = 0;
                while (cur < json.Length) {
                    int openBrace = json.IndexOf('{', cur);
                    if (openBrace < 0) break;
                    int closeBrace = json.IndexOf('}', openBrace);
                    if (closeBrace < 0) break;
                    string objJson = json.Substring(openBrace, closeBrace - openBrace + 1);
                    try {
                        var cs = JsonUtility.FromJson<CustomCoordSystemDef>(objJson);
                        if (cs != null && !string.IsNullOrEmpty(cs.name)) {
                            if (!UserCoordinateSystems.Exists(c => c.name.Equals(cs.name, StringComparison.OrdinalIgnoreCase))) {
                                UserCoordinateSystems.Add(cs);
                            }
                            if (!RealtimeUserCoordinateSystems.Contains(cs.name)) {
                                RealtimeUserCoordinateSystems.Add(cs.name);
                            }
                        }
                    } catch {}
                    cur = closeBrace + 1;
                }
                Debug.Log($"[KSPGMAT] Loaded {UserCoordinateSystems.Count} custom user coordinate systems.");
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] Failed to load user coordinate systems: " + ex.Message);
            }
        }

        public static string[] Categories {
            get {
                List<string> list = new List<string> { I18n.T("paramcat_all") };
                list.AddRange(CategoriesList);
                return list.ToArray();
            }
        }

        public static string[] PropStopCategories {
            get {
                List<string> list = new List<string> { I18n.T("paramcat_all") };
                list.AddRange(PropStopCategoriesList);
                return list.ToArray();
            }
        }

        public static bool IsLoaded { get; private set; } = false;

        static GMATParameterRegistry() {
            try {
                PopulateFallbackCore();
                InitCatalog();
            } catch {}
        }

        /// <summary>
        /// Attempts to locate and load gmat_capabilities.json from disk.
        /// </summary>
        public static void InitCatalog() {
            string dllDir = "";
            try { dllDir = Path.GetDirectoryName(typeof(GMATParameterRegistry).Assembly.Location); } catch {}

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string gmatBin = GMATClient.GmatBinPath;

            string[] candidatePaths = new string[] {
                !string.IsNullOrEmpty(dllDir) ? Path.Combine(dllDir, "gmat_capabilities.json") : "",
                !string.IsNullOrEmpty(dllDir) ? Path.Combine(dllDir, "..", "gmat_capabilities.json") : "",
                Path.Combine(gmatBin, "KSP_GMAT", "Plugins", "gmat_capabilities.json"),
                Path.Combine(gmatBin, "KSP_GMAT", "GameData", "KSPGMAT", "gmat_capabilities.json"),
                Path.Combine(gmatBin, "gmat_service", "gmat_capabilities.json"),
                Path.Combine(gmatBin, "gmat_capabilities.json"),
                Path.Combine(userProfile, "gmat-win-R2026a", "bin", "gmat_service", "gmat_capabilities.json"),
                Path.Combine(userProfile, "GMAT", "bin", "gmat_capabilities.json"),
                Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "gmat_capabilities.json")
            };

            foreach (var path in candidatePaths) {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                    try {
                        string json = File.ReadAllText(path);
                        LoadFromJson(json);
                        Debug.Log($"[KSPGMAT] Successfully loaded {AllParameters.Count} parameters and {AllPropStopDefs.Count} stop conditions from {path}");
                        LoadUserCoordinateSystems();
                        return;
                    } catch (Exception ex) {
                        Debug.LogWarning($"[KSPGMAT] Failed to parse {path}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Parses the gmatpy-generated JSON payload and populates all collections dynamically.
        /// Uses robust parser with zero external dependencies.
        /// </summary>
        public static void LoadFromJson(string json) {
            if (string.IsNullOrEmpty(json)) return;

            try {
                // 1. Try built-in direct string parser first (handles double/float and formatting seamlessly)
                List<RawGmatParamItem> rawParams = ParseItemsFromJsonBlock(json, "parameters");
                List<RawGmatParamItem> rawStops = ParseItemsFromJsonBlock(json, "stop_conditions");

                // 2. If direct parser found none, attempt Unity JsonUtility
                if (rawParams.Count == 0 || rawStops.Count == 0) {
                    try {
                        var payload = JsonUtility.FromJson<RawGmatCapabilitiesPayload>(json);
                        if (payload != null) {
                            if (rawParams.Count == 0 && payload.parameters != null) rawParams = payload.parameters;
                            if (rawStops.Count == 0 && payload.stop_conditions != null) rawStops = payload.stop_conditions;
                        }
                    } catch {}
                }

                HashSet<string> catSet = new HashSet<string>();
                List<GMATParam> newParams = new List<GMATParam>();

                foreach (var p in rawParams) {
                    // 基线 3.1: 过滤纯停机事件参数 (is_extremum 或官方极值名单)，严禁用于 Achieve 连续求解
                    if (p.is_extremum || p.name == "Apoapsis" || p.name == "Periapsis" || p.name == "Node" || p.name == "AscendingNode" || p.name == "DescendingNode") {
                        continue;
                    }
                    string cat = (I18n.Current == I18n.Lang.EN) ? p.category_en : p.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = p.category_zh ?? I18n.T("paramcat_dynamics");
                    catSet.Add(cat);

                    string desc = p.description ?? p.name;
                    if (!string.IsNullOrEmpty(p.unit)) desc += $" ({p.unit})";
                    string example = p.default_value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                    GMATParam catalogParam = new GMATParam(p.key, cat, desc, example);
                    catalogParam.Unit = p.unit ?? "";
                    catalogParam.DependencyObject = p.dep_object ?? "";
                    // 基线 3.1: 按 is_extremum 接入能力位掩码 (极值事件仅 Propagate，不得用于 Achieve)
                    catalogParam.Capability = p.is_extremum
                        ? ParamCapability.DiscontinuousExtremumEvent
                        : (ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable);
                    newParams.Add(catalogParam);
                }

                HashSet<string> stopCatSet = new HashSet<string>();
                List<PropStopDef> newStopDefs = new List<PropStopDef>();

                foreach (var s in rawStops) {
                    string cat = (I18n.Current == I18n.Lang.EN) ? s.category_en : s.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = s.category_zh ?? I18n.T("paramcat_stopcond");
                    stopCatSet.Add(cat);

                    string disp = s.name;
                    if (!string.IsNullOrEmpty(s.unit)) disp += $" [{s.unit}]";

                    newStopDefs.Add(new PropStopDef(
                        s.key,
                        disp,
                        cat,
                        s.unit ?? "",
                        s.description ?? s.name,
                        s.is_extremum,
                        s.default_value,
                        s.default_tolerance > 0 ? s.default_tolerance : 1e-5
                    ));
                }

                if (newParams.Count > 0) {
                    AllParameters = newParams;
                    CategoriesList = new List<string>(catSet);
                }
                if (newStopDefs.Count > 0) {
                    AllPropStopDefs = newStopDefs;
                    PropStopCategoriesList = new List<string>(stopCatSet);
                }

                var ints = ExtractJsonStringArray(json, "integrators");
                if (ints.Count > 0) AvailableIntegrators = ints;

                var pms = ExtractJsonStringArray(json, "physical_models");
                if (pms.Count > 0) AvailablePhysicalModels = pms;

                var bds = ExtractJsonStringArray(json, "bodies");
                if (bds.Count > 0) AvailableBodies = bds;

                var css = ExtractJsonStringArray(json, "coordinate_systems");
                if (css.Count > 0) AvailableCoordinateSystems = css;

                var slvs = ExtractJsonStringArray(json, "solvers");
                if (slvs.Count > 0) AvailableSolvers = slvs;

                var prps = ExtractJsonStringArray(json, "propagators");
                if (prps.Count > 0) AvailablePropagators = prps;

                var scs = ExtractJsonStringArray(json, "default_spacecraft");
                if (scs.Count > 0) AvailableDefaultSpacecraft = scs;

                var stdVars = ExtractJsonStringArray(json, "standard_variables");
                if (stdVars.Count > 0) AvailableStandardVariables = stdVars;

                IsLoaded = true;
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] Error loading capabilities JSON: " + ex.Message);
            }
        }

        #region Robust JSON Helpers
        private static List<RawGmatParamItem> ParseItemsFromJsonBlock(string json, string arrayName) {
            List<RawGmatParamItem> list = new List<RawGmatParamItem>();
            try {
                int arrIdx = json.IndexOf("\"" + arrayName + "\"");
                if (arrIdx < 0) return list;
                int startBracket = json.IndexOf('[', arrIdx);
                if (startBracket < 0) return list;

                int pos = startBracket + 1;
                while (pos < json.Length) {
                    int objStart = json.IndexOf('{', pos);
                    if (objStart < 0) break;
                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    string objStr = json.Substring(objStart, objEnd - objStart + 1);
                    var item = new RawGmatParamItem();
                    item.key = ExtractJsonString(objStr, "key");
                    item.name = ExtractJsonString(objStr, "name");
                    item.description = ExtractJsonString(objStr, "description");
                    item.description_zh = ExtractJsonString(objStr, "description_zh");
                    item.description_en = ExtractJsonString(objStr, "description_en");
                    item.category_zh = ExtractJsonString(objStr, "category_zh");
                    item.category_en = ExtractJsonString(objStr, "category_en");
                    item.unit = ExtractJsonString(objStr, "unit");
                    item.dep_object = ExtractJsonString(objStr, "dep_object");
                    item.default_value = (float)ExtractJsonDouble(objStr, "default_value", 0.0);
                    item.default_tolerance = (float)ExtractJsonDouble(objStr, "default_tolerance", 1e-5);
                    item.is_extremum = ExtractJsonBool(objStr, "is_extremum");

                    if (!string.IsNullOrEmpty(item.key)) {
                        list.Add(item);
                    }
                    pos = objEnd + 1;
                }
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] ParseItemsFromJsonBlock error: " + ex.Message);
            }
            return list;
        }

        private static string ExtractJsonString(string json, string key) {
            string token = "\"" + key + "\":";
            int idx = json.IndexOf(token);
            if (idx < 0) {
                token = "\"" + key + "\" :";
                idx = json.IndexOf(token);
            }
            if (idx < 0) return "";
            int startQuote = json.IndexOf('"', idx + token.Length);
            if (startQuote < 0) return "";
            int endQuote = json.IndexOf('"', startQuote + 1);
            if (endQuote < 0) return "";
            return json.Substring(startQuote + 1, endQuote - startQuote - 1);
        }

        private static double ExtractJsonDouble(string json, string key, double defaultVal = 0.0) {
            string token = "\"" + key + "\":";
            int idx = json.IndexOf(token);
            if (idx < 0) {
                token = "\"" + key + "\" :";
                idx = json.IndexOf(token);
            }
            if (idx < 0) return defaultVal;
            int start = idx + token.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+' || json[end] == 'e' || json[end] == 'E')) end++;
            if (end > start) {
                string numStr = json.Substring(start, end - start).Trim();
                double val;
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val)) {
                    return val;
                }
            }
            return defaultVal;
        }

        private static bool ExtractJsonBool(string json, string key) {
            string token = "\"" + key + "\":";
            int idx = json.IndexOf(token);
            if (idx < 0) {
                token = "\"" + key + "\" :";
                idx = json.IndexOf(token);
            }
            if (idx < 0) return false;
            int start = idx + token.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            return json.Substring(start).TrimStart().StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> ExtractJsonStringArray(string json, string key) {
            List<string> list = new List<string>();
            try {
                int arrIdx = json.IndexOf("\"" + key + "\"");
                if (arrIdx < 0) return list;
                int start = json.IndexOf('[', arrIdx);
                if (start < 0) return list;
                int end = json.IndexOf(']', start);
                if (end > start) {
                    string inside = json.Substring(start + 1, end - start - 1);
                    string[] tokens = inside.Split(',');
                    foreach (var t in tokens) {
                        string s = t.Trim().Trim('"', '\'', ' ', '\r', '\n');
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                }
            } catch {}
            return list;
        }
        #endregion

        private static void PopulateFallbackCore() {
            // Core standard Keplerian, Distance, Time elements (GMAT standard).
            // Display strings are localized through I18n (loc/*.json) so language switches stay hot.
            CategoriesList = new List<string> {
                I18n.T("paramcat_time_epoch"),
                I18n.T("paramcat_orbit_elements"),
                I18n.T("paramcat_dist_altitude"),
                I18n.T("paramcat_orbit_extremes"),
                I18n.T("paramcat_mass_hardware")
            };
            PropStopCategoriesList = new List<string> {
                I18n.T("paramcat_time_epoch"),
                I18n.T("paramcat_orbit_extremes"),
                I18n.T("paramcat_orbit_elements"),
                I18n.T("paramcat_dist_altitude")
            };

            AllParameters = new List<GMATParam> {
                new GMATParam("ElapsedSecs", I18n.T("paramcat_time_epoch"), I18n.T("fb_elapsedsecs"), "1800.0"),
                new GMATParam("ElapsedDays", I18n.T("paramcat_time_epoch"), I18n.T("fb_elapseddays"), "2.2"),
                new GMATParam("A1ModJulian", I18n.T("paramcat_time_epoch"), I18n.T("fb_a1modjulian"), "21545.0"),
                new GMATParam("SMA", I18n.T("paramcat_orbit_elements"), I18n.T("fb_sma"), "6578.14"),
                new GMATParam("ECC", I18n.T("paramcat_orbit_elements"), I18n.T("fb_ecc"), "0.0012"),
                new GMATParam("INC", I18n.T("paramcat_orbit_elements"), I18n.T("fb_inc"), "28.5"),
                new GMATParam("RAAN", I18n.T("paramcat_orbit_elements"), I18n.T("fb_raan"), "120.0"),
                new GMATParam("AOP", I18n.T("paramcat_orbit_elements"), I18n.T("fb_aop"), "45.0"),
                new GMATParam("TA", I18n.T("paramcat_orbit_elements"), I18n.T("fb_ta"), "180.0"),
                new GMATParam("Altitude", I18n.T("paramcat_dist_altitude"), I18n.T("fb_altitude"), "200.0"),
                new GMATParam("RMAG", I18n.T("paramcat_dist_altitude"), I18n.T("fb_rmag"), "6578.14"),
                new GMATParam("TotalMass", I18n.T("paramcat_mass_hardware"), I18n.T("fb_totalmass"), "7500.0"),
                new GMATParam("DryMass", I18n.T("paramcat_mass_hardware"), I18n.T("fb_drymass"), "1500.0")
            };

            AllPropStopDefs = new List<PropStopDef> {
                // 基线 3.3: 停止条件 Key 使用 GMAT 官方命名 (ElapsedSecs/ElapsedDays)
                new PropStopDef("ElapsedSecs", I18n.T("fb_stop_elapsedsecs_name"), I18n.T("paramcat_time_epoch"), "s", I18n.T("fb_stop_elapsedsecs_desc"), false, 3600.0, 1e-5),
                new PropStopDef("ElapsedDays", I18n.T("fb_stop_elapseddays_name"), I18n.T("paramcat_time_epoch"), "Day", I18n.T("fb_stop_elapseddays_desc"), false, 3.0, 1e-5),
                new PropStopDef("Apoapsis", I18n.T("fb_stop_apoapsis_name"), I18n.T("paramcat_orbit_extremes"), "", I18n.T("fb_stop_apoapsis_desc"), true, 0.0, 1e-5),
                new PropStopDef("Periapsis", I18n.T("fb_stop_periapsis_name"), I18n.T("paramcat_orbit_extremes"), "", I18n.T("fb_stop_periapsis_desc"), true, 0.0, 1e-5),
                new PropStopDef("Latitude", I18n.T("fb_stop_latitude_name"), I18n.T("paramcat_orbit_extremes"), "°", I18n.T("fb_stop_latitude_desc"), false, 0.0, 1e-5),
                new PropStopDef("EarthMJ2000Eq.Z", I18n.T("fb_stop_earthmj2000z_name"), I18n.T("paramcat_orbit_extremes"), "Km", I18n.T("fb_stop_earthmj2000z_desc"), false, 0.0, 1e-5),
                new PropStopDef("TA", I18n.T("fb_stop_ta_name"), I18n.T("paramcat_orbit_elements"), "°", I18n.T("fb_stop_ta_desc"), false, 180.0, 1e-4),
                new PropStopDef("SMA", I18n.T("fb_stop_sma_name"), I18n.T("paramcat_orbit_elements"), "Km", I18n.T("fb_stop_sma_desc"), false, 7000.0, 0.1),
                new PropStopDef("Altitude", I18n.T("fb_stop_altitude_name"), I18n.T("paramcat_dist_altitude"), "Km", I18n.T("fb_stop_altitude_desc"), false, 400.0, 0.1),
                new PropStopDef("RMAG", I18n.T("fb_stop_rmag_name"), I18n.T("paramcat_dist_altitude"), "Km", I18n.T("fb_stop_rmag_desc"), false, 42164.0, 0.1)
            };
        }

        /// <summary>
        /// Retrieves concise localized technical annotation for any parameter, stop condition, or variable.
        /// Handles dot-syntax paths (e.g. Sat.Earth.Apoapsis, Sat.Luna.Altitude, EarthMoonRot.Z, burntime).
        /// Terms live in the i18n loc/*.json tables under orb_* keys.
        /// </summary>
        public static string GetParamBadgeI18n(string key) {
            if (string.IsNullOrEmpty(key)) return "";
            string clean = key.Trim();
            string[] parts = clean.Split('.');
            string field = parts[parts.Length - 1].Trim();
            string bodyOrCoord = parts.Length >= 2 ? parts[parts.Length - 2].Trim() : "";

            string badge = "";
            string normField = field.ToLower().Replace("_", "");
            if (I18n.HasKey("orb_" + normField)) {
                badge = I18n.T("orb_" + normField);
            } else {
                foreach (var suffix in I18n.OrbKeys) {
                    if (normField.Contains(suffix) || suffix.Contains(normField)) {
                        badge = I18n.T("orb_" + suffix);
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(badge)) {
                if (!string.IsNullOrEmpty(bodyOrCoord) && !bodyOrCoord.Equals("Sat", StringComparison.OrdinalIgnoreCase) && !bodyOrCoord.Equals("Vessel", StringComparison.OrdinalIgnoreCase)) {
                    return $"{bodyOrCoord} | {badge}";
                }
                return badge;
            }
            return "";
        }

        public static PropStopDef GetStopDef(string key) {
            if (string.IsNullOrEmpty(key)) return AllPropStopDefs.Count > 0 ? AllPropStopDefs[0] : new PropStopDef();
            for (int i = 0; i < AllPropStopDefs.Count; i++) {
                if (AllPropStopDefs[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                    return AllPropStopDefs[i];
                }
            }
            return new PropStopDef(key, key, I18n.T("paramcat_custom"), "", key, false, 0.0, 1e-5);
        }

        public static List<PropStopDef> GetStopDefsByCategory(string cat) {
            List<PropStopDef> list = new List<PropStopDef>();
            if (string.IsNullOrEmpty(cat) || cat.Contains("全部") || cat.Contains("All")) {
                return new List<PropStopDef>(AllPropStopDefs);
            }
            for (int i = 0; i < AllPropStopDefs.Count; i++) {
                if (AllPropStopDefs[i].Category.Equals(cat, StringComparison.OrdinalIgnoreCase) ||
                    AllPropStopDefs[i].Category.Contains(cat) || cat.Contains(AllPropStopDefs[i].Category)) {
                    list.Add(AllPropStopDefs[i]);
                }
            }
            if (list.Count == 0) {
                return new List<PropStopDef>(AllPropStopDefs);
            }
            return list;
        }

        public static List<GMATParam> FindMatches(string query, int maxResults = 80) {
            List<GMATParam> list = new List<GMATParam>();
            if (string.IsNullOrEmpty(query)) return list;
            string lower = query.ToLowerInvariant().Trim();
            foreach (var p in AllParameters) {
                if (p.Name.ToLowerInvariant().Contains(lower) || p.Description.ToLowerInvariant().Contains(lower) || p.Category.ToLowerInvariant().Contains(lower)) {
                    list.Add(p);
                    if (list.Count >= maxResults) break;
                }
            }
            return list;
        }

                public static void ReloadCategories() {
            // Fallback mode (no gmat_capabilities.json loaded): rebuild localized fallback defs so
            // a language switch still refreshes categories/descriptions.
            bool fallbackMode = (rawParamsCache == null || rawParamsCache.Count == 0)
                             && (rawStopsCache == null || rawStopsCache.Count == 0);
            if (fallbackMode) {
                try { PopulateFallbackCore(); } catch {}
                return;
            }

            if (rawParamsCache != null && rawParamsCache.Count > 0) {
                HashSet<string> catSet = new HashSet<string>();
                List<GMATParam> newParams = new List<GMATParam>();

                foreach (var p in rawParamsCache) {
                    string cat = (I18n.Current == I18n.Lang.EN) ? p.category_en : p.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = p.category_zh ?? I18n.T("paramcat_dynamics");
                    catSet.Add(cat);

                    string descZh = !string.IsNullOrEmpty(p.description_zh) ? p.description_zh : (p.description ?? p.name);
                    string descEn = !string.IsNullOrEmpty(p.description_en) ? p.description_en : (p.description ?? p.name);
                    if (!string.IsNullOrEmpty(p.unit)) {
                        descZh += $" ({p.unit})";
                        descEn += $" ({p.unit})";
                    }
                    string activeDesc = (I18n.Current == I18n.Lang.EN) ? descEn : descZh;
                    string example = p.default_value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                    GMATParam catalogParam = new GMATParam(p.key, cat, activeDesc, example);
                    catalogParam.DescriptionZh = descZh;
                    catalogParam.DescriptionEn = descEn;
                    catalogParam.Unit = p.unit ?? "";
                    catalogParam.DependencyObject = p.dep_object ?? "";
                    newParams.Add(catalogParam);
                }

                AllParameters = newParams;
                CategoriesList = new List<string>(catSet);
            }

            if (rawStopsCache != null && rawStopsCache.Count > 0) {
                HashSet<string> stopCatSet = new HashSet<string>();
                List<PropStopDef> newStopDefs = new List<PropStopDef>();

                foreach (var s in rawStopsCache) {
                    string cat = (I18n.Current == I18n.Lang.EN) ? s.category_en : s.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = s.category_zh ?? I18n.T("paramcat_stopcond");
                    stopCatSet.Add(cat);

                    string disp = s.name;
                    if (!string.IsNullOrEmpty(s.unit)) disp += $" [{s.unit}]";

                    string descZh = !string.IsNullOrEmpty(s.description_zh) ? s.description_zh : (s.description ?? s.name);
                    string descEn = !string.IsNullOrEmpty(s.description_en) ? s.description_en : (s.description ?? s.name);
                    string activeDesc = (I18n.Current == I18n.Lang.EN) ? descEn : descZh;

                    var stopDef = new PropStopDef(
                        s.key,
                        disp,
                        cat,
                        s.unit,
                        activeDesc,
                        s.is_extremum,
                        s.default_value,
                        s.default_tolerance
                    );
                    stopDef.DescriptionZh = descZh;
                    stopDef.DescriptionEn = descEn;
                    newStopDefs.Add(stopDef);
                }

                AllPropStopDefs = newStopDefs;
                PropStopCategoriesList = new List<string>(stopCatSet);
            }
        }

        public static List<GMATParam> GetByCategory(string category) {
            List<GMATParam> list = new List<GMATParam>();
            if (string.IsNullOrEmpty(category) || category.Contains("全部") || category.Contains("All")) {
                return new List<GMATParam>(AllParameters);
            }
            foreach (var p in AllParameters) {
                if (p.Category.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(category) || category.Contains(p.Category)) {
                    list.Add(p);
                }
            }
            if (list.Count == 0) {
                return new List<GMATParam>(AllParameters);
            }
            return list;
        }

        private static readonly HashSet<string> DirectParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "X", "Y", "Z", "VX", "VY", "VZ", "AX", "AY", "AZ",
            "INC", "AOP", "RAAN", "VMAG", "Energy", "C3Energy",
            "TotalMass", "DryMass", "ElapsedSecs", "ElapsedDays"
        };

        public static string MapBodyName(string name) {
            if (string.IsNullOrEmpty(name)) return "Earth";
            if (name.Equals("Kerbin", StringComparison.OrdinalIgnoreCase)) return "Earth";
            if (name.Equals("Mun", StringComparison.OrdinalIgnoreCase)) return "Luna";
            if (name.Equals("Moon", StringComparison.OrdinalIgnoreCase)) return "Luna";
            if (name.Equals("Sun", StringComparison.OrdinalIgnoreCase)) return "Sun";
            if (name.Equals("Kerbol", StringComparison.OrdinalIgnoreCase)) return "Sun";
            if (name.Equals("Duna", StringComparison.OrdinalIgnoreCase)) return "Mars";
            if (name.Equals("Eve", StringComparison.OrdinalIgnoreCase)) return "Venus";
            if (name.Equals("Jool", StringComparison.OrdinalIgnoreCase)) return "Jupiter";
            if (name.Equals("Dres", StringComparison.OrdinalIgnoreCase)) return "Ceres";
            if (name.Equals("Eeloo", StringComparison.OrdinalIgnoreCase)) return "Pluto";
            return name;
        }

        public static void SyncRealtimeSequenceObjects(List<MissionStep> sequence, Vessel vessel) {
            if (vessel != null && !string.IsNullOrEmpty(vessel.vesselName)) {
                RealtimeUserSpacecraft.Add(SanitizeGmatIdentifier(vessel.vesselName, "Sat"));
            }
            if (sequence != null) {
                for (int i = 0; i < sequence.Count; i++) {
                    var s = sequence[i];
                    if (!string.IsNullOrEmpty(s.variable)) {
                        RealtimeUserVariables[s.variable] = s.expression ?? "0.0";
                    }
                    if (!string.IsNullOrEmpty(s.vary_variable)) {
                        RealtimeUserVariables[s.vary_variable] = s.vary_initial.ToString("F1");
                    }
                    if (!string.IsNullOrEmpty(s.burn_vary_var_name)) {
                        RealtimeUserVariables[s.burn_vary_var_name] = s.duration.ToString("F0");
                    }
                    if (!string.IsNullOrEmpty(s.propagator)) {
                        RealtimeUserPropagators.Add(s.propagator);
                    }
                    if (!string.IsNullOrEmpty(s.label) && (s.type == "FiniteBurn" || s.type == "ImpulsiveBurn")) {
                        RealtimeUserBurns.Add(s.label);
                    }
                    if (!string.IsNullOrEmpty(s.burn_coordinate_system) && s.burn_coordinate_system != "Local") {
                        RealtimeUserCoordinateSystems.Add(s.burn_coordinate_system);
                    }
                }
            }
            if (FlightGlobals.Bodies != null) {
                for (int i = 0; i < FlightGlobals.Bodies.Count; i++) {
                    var b = FlightGlobals.Bodies[i];
                    if (b != null && !string.IsNullOrEmpty(b.name)) {
                        RealtimeUserBodies.Add(MapBodyName(b.name));
                    }
                }
            }
        }

        public static List<string> GetAllBodies(Vessel vessel = null) {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Vessel active SOI body (Top Priority)
            if (vessel != null && vessel.mainBody != null) {
                string main = MapBodyName(vessel.mainBody.name);
                if (!seen.Contains(main)) { list.Add(main); seen.Add(main); }
            }

            // 2. Vessel target body (if any)
            if (vessel != null && vessel.targetObject != null) {
                try {
                    ITargetable tgt = vessel.targetObject;
                    Vessel tgtVessel = tgt.GetVessel();
                    CelestialBody tgtBody = (tgt is CelestialBody) ? (CelestialBody)tgt : (tgtVessel != null ? tgtVessel.mainBody : null);
                    if (tgtBody != null) {
                        string tb = MapBodyName(tgtBody.name);
                        if (!seen.Contains(tb)) { list.Add(tb); seen.Add(tb); }
                    }
                } catch {}
            }

            // 3. Dynamic FlightGlobals all active celestial bodies in game session!
            try {
                if (FlightGlobals.Bodies != null) {
                    for (int i = 0; i < FlightGlobals.Bodies.Count; i++) {
                        var cb = FlightGlobals.Bodies[i];
                        if (cb != null) {
                            string mapped = MapBodyName(cb.name);
                            if (!seen.Contains(mapped)) { list.Add(mapped); seen.Add(mapped); }
                        }
                    }
                }
            } catch {}

            // 4. Standard Cislunar and Solar System GMAT celestial bodies
            string[] stdBodies = new string[] { "Earth", "Luna", "Sun", "Mars", "Jupiter", "Venus", "Mercury", "Saturn", "Uranus", "Neptune", "Pluto" };
            for (int i = 0; i < stdBodies.Length; i++) {
                if (!seen.Contains(stdBodies[i])) { list.Add(stdBodies[i]); seen.Add(stdBodies[i]); }
            }

            return list;
        }

        public static List<string> GetAllSpacecraft(Vessel vessel = null) {
            // Mod-wide uniform spacecraft identifier
            return new List<string> { "Sat" };
        }

        public static List<string> GetAllCoordinateSystems() {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Standard Cislunar and Solar Coordinate Systems
            string[] stdCoords = new string[] {
                "EarthMJ2000Eq", "EarthMJ2000Ec", "EarthFixed", "EarthICRF",
                "MoonInertial", "MoonFixed", "EarthMoonRot",
                "SunEcliptic", "MarsInertial", "MarsFixed"
            };
            for (int i = 0; i < stdCoords.Length; i++) {
                if (!seen.Contains(stdCoords[i])) { list.Add(stdCoords[i]); seen.Add(stdCoords[i]); }
            }

            // 2. User-Defined Coordinate Systems
            if (UserCoordinateSystems != null) {
                for (int i = 0; i < UserCoordinateSystems.Count; i++) {
                    var cs = UserCoordinateSystems[i];
                    if (cs != null && !string.IsNullOrEmpty(cs.name) && !seen.Contains(cs.name)) {
                        list.Add(cs.name);
                        seen.Add(cs.name);
                    }
                }
            }

            // 3. Realtime Coordinate Systems
            foreach (var cs in RealtimeUserCoordinateSystems) {
                if (!seen.Contains(cs)) { list.Add(cs); seen.Add(cs); }
            }

            // 4. Cached Available Coordinate Systems
            for (int i = 0; i < AvailableCoordinateSystems.Count; i++) {
                string cs = AvailableCoordinateSystems[i];
                if (!seen.Contains(cs)) { list.Add(cs); seen.Add(cs); }
            }

            return list;
        }

        public static List<string> GetAllPropagators() {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Cached Propagators from GMAT
            for (int i = 0; i < AvailablePropagators.Count; i++) {
                string p = AvailablePropagators[i];
                if (!seen.Contains(p)) { list.Add(p); seen.Add(p); }
            }

            // 2. Real-time User Propagators
            foreach (var p in RealtimeUserPropagators) {
                if (!seen.Contains(p)) { list.Add(p); seen.Add(p); }
            }

            return list;
        }

        public static List<string> GetAllSolvers() {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (GMATStateManager.Current != null && GMATStateManager.Current.Solvers != null) {
                for (int i = 0; i < GMATStateManager.Current.Solvers.Count; i++) {
                    var s = GMATStateManager.Current.Solvers[i];
                    if (s != null && !string.IsNullOrEmpty(s.Name) && !seen.Contains(s.Name)) {
                        list.Add(s.Name);
                        seen.Add(s.Name);
                    }
                }
            }

            for (int i = 0; i < AvailableSolvers.Count; i++) {
                string slv = AvailableSolvers[i];
                if (!seen.Contains(slv)) {
                    list.Add(slv);
                    seen.Add(slv);
                }
            }
            return list;
        }

        public static List<string> GetAllVariables() {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (GMATStateManager.Current != null) {
                if (GMATStateManager.Current.Variables != null) {
                    for (int i = 0; i < GMATStateManager.Current.Variables.Count; i++) {
                        var v = GMATStateManager.Current.Variables[i];
                        if (v != null && !string.IsNullOrEmpty(v.Name) && !seen.Contains(v.Name)) {
                            list.Add(v.Name);
                            seen.Add(v.Name);
                        }
                    }
                }
                if (GMATStateManager.Current.Arrays != null) {
                    for (int i = 0; i < GMATStateManager.Current.Arrays.Count; i++) {
                        var a = GMATStateManager.Current.Arrays[i];
                        if (a != null && !string.IsNullOrEmpty(a.Name) && !seen.Contains(a.Name)) {
                            list.Add(a.Name);
                            seen.Add(a.Name);
                        }
                    }
                }
                if (GMATStateManager.Current.Strings != null) {
                    for (int i = 0; i < GMATStateManager.Current.Strings.Count; i++) {
                        var s = GMATStateManager.Current.Strings[i];
                        if (s != null && !string.IsNullOrEmpty(s.Name) && !seen.Contains(s.Name)) {
                            list.Add(s.Name);
                            seen.Add(s.Name);
                        }
                    }
                }
                if (GMATStateManager.Current.Thrusters != null) {
                    for (int i = 0; i < GMATStateManager.Current.Thrusters.Count; i++) {
                        var thr = GMATStateManager.Current.Thrusters[i];
                        if (thr != null && !string.IsNullOrEmpty(thr.Id)) {
                            string d1 = $"Sat.{thr.Id}.ThrustDirection1";
                            string d2 = $"Sat.{thr.Id}.ThrustDirection2";
                            string d3 = $"Sat.{thr.Id}.ThrustDirection3";
                            if (!seen.Contains(d1)) { list.Add(d1); seen.Add(d1); }
                            if (!seen.Contains(d2)) { list.Add(d2); seen.Add(d2); }
                            if (!seen.Contains(d3)) { list.Add(d3); seen.Add(d3); }
                        }
                    }
                }
            }

            // 1. Real-time user variables from sequence
            foreach (var kv in RealtimeUserVariables) {
                if (!seen.Contains(kv.Key)) { list.Add(kv.Key); seen.Add(kv.Key); }
            }

            // 2. Real-time user burns
            foreach (var b in RealtimeUserBurns) {
                string v1 = $"{b}.Element1";
                if (!seen.Contains(v1)) { list.Add(v1); seen.Add(v1); }
            }

            // 3. Cached standard variables from GMAT
            for (int i = 0; i < AvailableStandardVariables.Count; i++) {
                string v = AvailableStandardVariables[i];
                if (!seen.Contains(v)) { list.Add(v); seen.Add(v); }
            }

            return list;
        }

        public static string SanitizeGmatIdentifier(string raw, string prefix = "Sat") {
            if (string.IsNullOrEmpty(raw)) return prefix;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool capitalizeNext = false;
            for (int i = 0; i < raw.Length; i++) {
                char c = raw[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') {
                    if (capitalizeNext && c >= 'a' && c <= 'z') {
                        sb.Append(char.ToUpperInvariant(c));
                    } else {
                        sb.Append(c);
                    }
                    capitalizeNext = false;
                } else {
                    capitalizeNext = true;
                }
            }
            string res = sb.ToString();
            if (string.IsNullOrEmpty(res)) return prefix;
            if (char.IsDigit(res[0])) {
                res = prefix + res;
            }
            return res;
        }

        public static List<IntelliSenseItem> QueryIntelliSense(string text, Vessel vessel = null, string contextMode = null) {
            ParamCapability filterFlags = ParamCapability.Reportable;
            if (!string.IsNullOrEmpty(contextMode)) {
                string cm = contextMode.ToLowerInvariant();
                if (cm == "achieve") {
                    filterFlags = ParamCapability.ContinuouslyDifferentiable;
                } else if (cm == "vary") {
                    filterFlags = ParamCapability.ControlVariable;
                } else if (cm == "stop" || cm == "prop_stop") {
                    filterFlags = ParamCapability.PropagateCondition;
                } else if (cm == "prop_val" || cm == "variable_or_value") {
                    filterFlags = ParamCapability.Reportable | ParamCapability.ControlVariable;
                }
            }
            return GMATNamespaceResolver.Resolve(text, vessel, filterFlags);
        }



        // Dynamic User Configs & Registry
        private static readonly Dictionary<string, PropagatorConfig> registeredPropagators = new Dictionary<string, PropagatorConfig>();
        private static readonly Dictionary<string, SolverConfig> registeredSolvers = new Dictionary<string, SolverConfig>();

        public static void InitDefaultPropagatorsAndSolvers() {
            if (registeredPropagators.Count == 0) {
                registeredPropagators["LEOProp"] = new PropagatorConfig {
                    name = "LEOProp", integratorType = "RungeKutta89", initialStepSize = 60, accuracy = 1e-11,
                    minStep = 0.001, maxStep = 2700, maxStepAttempts = 50, centralBody = "Earth",
                    gravityEarth = true, gravityLuna = true, gravitySun = true, gravityJupiter = true, isDefault = true
                };
                registeredPropagators["CislunarProp"] = new PropagatorConfig {
                    name = "CislunarProp", integratorType = "RungeKutta89", initialStepSize = 60, accuracy = 1e-11,
                    minStep = 0.001, maxStep = 21600, maxStepAttempts = 50, centralBody = "Earth",
                    gravityEarth = true, gravityLuna = true, gravitySun = true, gravityJupiter = true, isDefault = true
                };
                registeredPropagators["LunarNRHOProp"] = new PropagatorConfig {
                    name = "LunarNRHOProp", integratorType = "RungeKutta89", initialStepSize = 60, accuracy = 1e-11,
                    minStep = 0.001, maxStep = 10800, maxStepAttempts = 50, centralBody = "Luna",
                    gravityEarth = true, gravityLuna = true, gravitySun = true, gravityJupiter = true, isDefault = true
                };
            }

            if (registeredSolvers.Count == 0) {
                registeredSolvers["DefaultDC"] = new SolverConfig {
                    name = "DefaultDC", algorithm = "NewtonRaphson", derivativeMethod = "ForwardDifference",
                    maxIterations = 50, solveMode = "Solve", exitMode = "SaveAndContinue", reportStyle = "Normal", isDefault = true
                };
                registeredSolvers["PrecisionDC"] = new SolverConfig {
                    name = "PrecisionDC", algorithm = "NewtonRaphson", derivativeMethod = "CentralDifference",
                    maxIterations = 100, solveMode = "Solve", exitMode = "SaveAndContinue", reportStyle = "Normal", isDefault = true
                };
                registeredSolvers["BroydenDC"] = new SolverConfig {
                    name = "BroydenDC", algorithm = "Broyden", derivativeMethod = "ForwardDifference",
                    maxIterations = 75, solveMode = "Solve", exitMode = "SaveAndContinue", reportStyle = "Normal", isDefault = true
                };
            }
        }

        public static Dictionary<string, PropagatorConfig> GetAllPropagatorConfigs() {
            InitDefaultPropagatorsAndSolvers();
            return new Dictionary<string, PropagatorConfig>(registeredPropagators);
        }

        public static Dictionary<string, SolverConfig> GetAllSolverConfigs() {
            InitDefaultPropagatorsAndSolvers();
            return new Dictionary<string, SolverConfig>(registeredSolvers);
        }

        public static void AddOrUpdatePropagator(PropagatorConfig cfg) {
            if (cfg == null || string.IsNullOrEmpty(cfg.name)) return;
            InitDefaultPropagatorsAndSolvers();
            registeredPropagators[cfg.name] = cfg;
        }

        public static bool DeletePropagator(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            InitDefaultPropagatorsAndSolvers();
            if (registeredPropagators.ContainsKey(name) && !registeredPropagators[name].isDefault) {
                registeredPropagators.Remove(name);
                return true;
            }
            return false;
        }

        public static void AddOrUpdateSolver(SolverConfig cfg) {
            if (cfg == null || string.IsNullOrEmpty(cfg.name)) return;
            InitDefaultPropagatorsAndSolvers();
            registeredSolvers[cfg.name] = cfg;
        }

        public static bool DeleteSolver(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            InitDefaultPropagatorsAndSolvers();
            if (registeredSolvers.ContainsKey(name) && !registeredSolvers[name].isDefault) {
                registeredSolvers.Remove(name);
                return true;
            }
            return false;
        }
        // =========================================================================
        // Native GMAT 27 Commands Registry & Categorization
        // =========================================================================
        private static readonly List<GMATCommandDef> registeredCommands = new List<GMATCommandDef>();

        public static void InitCommandRegistry() {
            if (registeredCommands.Count > 0) return;

            // 1. Burns & Thrust
            registeredCommands.Add(new GMATCommandDef("Maneuver", "cat_burns", "", "cmd_Maneuver_label", "cmd_Maneuver_tooltip"));
            registeredCommands.Add(new GMATCommandDef("BeginFiniteBurn", "cat_burns", "", "cmd_BeginFiniteBurn_label", "cmd_BeginFiniteBurn_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndFiniteBurn", "cat_burns", "", "cmd_EndFiniteBurn_label", "cmd_EndFiniteBurn_tooltip"));
            registeredCommands.Add(new GMATCommandDef("BeginFileThrust", "cat_burns", "", "cmd_BeginFileThrust_label", "cmd_BeginFileThrust_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndFileThrust", "cat_burns", "", "cmd_EndFileThrust_label", "cmd_EndFileThrust_tooltip"));

            // 2. Propagation
            registeredCommands.Add(new GMATCommandDef("Propagate", "cat_prop", "", "cmd_Propagate_label", "cmd_Propagate_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Stop", "cat_prop", "", "cmd_Stop_label", "cmd_Stop_tooltip"));

            // 3. Targeting & Optimization
            registeredCommands.Add(new GMATCommandDef("Target", "cat_target", "", "cmd_Target_label", "cmd_Target_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Vary", "cat_target", "", "cmd_Vary_label", "cmd_Vary_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Achieve", "cat_target", "", "cmd_Achieve_label", "cmd_Achieve_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndTarget", "cat_target", "", "cmd_EndTarget_label", "cmd_EndTarget_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Optimize", "cat_optimize", "", "cmd_Optimize_label", "cmd_Optimize_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Minimize", "cat_optimize", "", "cmd_Minimize_label", "cmd_Minimize_tooltip"));
            registeredCommands.Add(new GMATCommandDef("NonlinearConstraint", "cat_optimize", "", "cmd_NonlinearConstraint_label", "cmd_NonlinearConstraint_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndOptimize", "cat_optimize", "", "cmd_EndOptimize_label", "cmd_EndOptimize_tooltip"));

            // 4. Control Logic
            registeredCommands.Add(new GMATCommandDef("If", "cat_control", "", "cmd_If_label", "cmd_If_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Else", "cat_control", "", "cmd_Else_label", "cmd_Else_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndIf", "cat_control", "", "cmd_EndIf_label", "cmd_EndIf_tooltip"));
            registeredCommands.Add(new GMATCommandDef("While", "cat_control", "", "cmd_While_label", "cmd_While_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndWhile", "cat_control", "", "cmd_EndWhile_label", "cmd_EndWhile_tooltip"));
            registeredCommands.Add(new GMATCommandDef("For", "cat_control", "", "cmd_For_label", "cmd_For_tooltip"));
            registeredCommands.Add(new GMATCommandDef("EndFor", "cat_control", "", "cmd_EndFor_label", "cmd_EndFor_tooltip"));

            // 5. Math & Data
            registeredCommands.Add(new GMATCommandDef("Equation", "cat_math", "", "cmd_Equation_label", "cmd_Equation_tooltip"));
            registeredCommands.Add(new GMATCommandDef("ScriptEvent", "cat_math", "", "cmd_ScriptEvent_label", "cmd_ScriptEvent_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Set", "cat_math", "", "cmd_Set_label", "cmd_Set_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Global", "cat_math", "", "cmd_Global_label", "cmd_Global_tooltip"));

            // 6. Functions
            registeredCommands.Add(new GMATCommandDef("CallGmatFunction", "cat_func", "", "cmd_CallGmatFunction_label", "cmd_CallGmatFunction_tooltip"));
            registeredCommands.Add(new GMATCommandDef("CallBuiltinGmatFunction", "cat_func", "", "cmd_CallBuiltinGmatFunction_label", "cmd_CallBuiltinGmatFunction_tooltip"));

            // 7. Subscribers & Drawing
            registeredCommands.Add(new GMATCommandDef("Report", "cat_report", "", "cmd_Report_label", "cmd_Report_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Write", "cat_report", "", "cmd_Write_label", "cmd_Write_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Toggle", "cat_report", "", "cmd_Toggle_label", "cmd_Toggle_tooltip"));
            registeredCommands.Add(new GMATCommandDef("ClearPlot", "cat_report", "", "cmd_ClearPlot_label", "cmd_ClearPlot_tooltip"));
            registeredCommands.Add(new GMATCommandDef("PenUp", "cat_report", "", "cmd_PenUp_label", "cmd_PenUp_tooltip"));
            registeredCommands.Add(new GMATCommandDef("PenDown", "cat_report", "", "cmd_PenDown_label", "cmd_PenDown_tooltip"));
            registeredCommands.Add(new GMATCommandDef("UpdateDynamicData", "cat_report", "", "cmd_UpdateDynamicData_label", "cmd_UpdateDynamicData_tooltip"));

            // 8. Events & Debug
            registeredCommands.Add(new GMATCommandDef("FindEvents", "cat_events", "", "cmd_FindEvents_label", "cmd_FindEvents_tooltip"));
            registeredCommands.Add(new GMATCommandDef("Breakpoint", "cat_events", "", "cmd_Breakpoint_label", "cmd_Breakpoint_tooltip"));
            registeredCommands.Add(new GMATCommandDef("CommandEcho", "cat_events", "", "cmd_CommandEcho_label", "cmd_CommandEcho_tooltip"));
            registeredCommands.Add(new GMATCommandDef("MarkPoint", "cat_events", "", "cmd_MarkPoint_label", "cmd_MarkPoint_tooltip"));
        }

        public static List<GMATCommandDef> GetAllCommands() {
            InitCommandRegistry();
            return registeredCommands;
        }

        public static List<GMATCommandDef> GetCommandsByCategory(string categoryKey) {
            InitCommandRegistry();
            List<GMATCommandDef> res = new List<GMATCommandDef>();
            foreach (var cmd in registeredCommands) {
                if (cmd.CategoryKey == categoryKey) res.Add(cmd);
            }
            return res;
        }
    }

    [Serializable]
    public struct GMATCommandDef {
        public string Type;
        public string CategoryKey;
        public string Icon;
        /// <summary>i18n 词典键 (cmd_*_label)，标签文本存于 loc/*.json。</summary>
        public string LabelKey;
        /// <summary>i18n 词典键 (cmd_*_tooltip)，悬浮提示存于 loc/*.json。</summary>
        public string TooltipKey;

        public GMATCommandDef(string type, string cat, string icon, string labelKey, string tooltipKey) {
            Type = type;
            CategoryKey = cat;
            Icon = icon;
            LabelKey = labelKey;
            TooltipKey = tooltipKey;
        }

        public string GetLocalizedLabel() {
            return I18n.T(LabelKey);
        }

        public string GetLocalizedTooltip() {
            return I18n.T(TooltipKey);
        }
    }
}
