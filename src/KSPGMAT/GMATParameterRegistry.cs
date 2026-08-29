using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

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

        public GMATParam(string name, string category, string desc, string example) {
            Name = name;
            Category = category;
            Description = desc;
            DescriptionZh = desc;
            DescriptionEn = desc;
            Example = example;
            Unit = "";
            DependencyObject = "";
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
            switch (Kind) {
                case IntelliSenseKind.CelestialBody: return "🪐";
                case IntelliSenseKind.CoordinateSystem: return "📐";
                case IntelliSenseKind.Spacecraft: return "🛰️";
                case IntelliSenseKind.Parameter: return "📏";
                case IntelliSenseKind.Variable: return "⚡";
                case IntelliSenseKind.Propagator: return "🚀";
                case IntelliSenseKind.Solver: return "🎯";
                case IntelliSenseKind.DirectCoord: return "🧭";
                default: return "🔹";
            }
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
                List<string> list = new List<string> { "🌟 全部" };
                list.AddRange(CategoriesList);
                return list.ToArray();
            }
        }

        public static string[] PropStopCategories {
            get {
                List<string> list = new List<string> { "🌟 全部" };
                list.AddRange(PropStopCategoriesList);
                return list.ToArray();
            }
        }

        public static bool IsLoaded { get; private set; } = false;

        static GMATParameterRegistry() {
            PopulateFallbackCore();
            InitCatalog();
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
                @"C:\Users\43701\gmat-win-R2026a\bin\gmat_service\gmat_capabilities.json",
                @"d:\nocomp\gmat-win-R2026a\bin\gmat_service\gmat_capabilities.json"
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
                    string cat = (I18n.Current == I18n.Lang.EN) ? p.category_en : p.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = p.category_zh ?? "动力学参数";
                    catSet.Add(cat);

                    string desc = p.description ?? p.name;
                    if (!string.IsNullOrEmpty(p.unit)) desc += $" ({p.unit})";
                    string example = p.default_value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                    GMATParam catalogParam = new GMATParam(p.key, cat, desc, example);
                    catalogParam.Unit = p.unit ?? "";
                    catalogParam.DependencyObject = p.dep_object ?? "";
                    newParams.Add(catalogParam);
                }

                HashSet<string> stopCatSet = new HashSet<string>();
                List<PropStopDef> newStopDefs = new List<PropStopDef>();

                foreach (var s in rawStops) {
                    string cat = (I18n.Current == I18n.Lang.EN) ? s.category_en : s.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = s.category_zh ?? "终止条件";
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
            // Core standard Keplerian, Distance, Time elements (GMAT standard)
            CategoriesList = new List<string> { "⏱️ 时间与历元", "🌐 轨道根数", "📏 空间距离与高度", "🪐 轨道极值与交点", "⚖️ 质量与硬件" };
            PropStopCategoriesList = new List<string> { "⏱️ 时间与历元", "🪐 轨道极值与交点", "🌐 轨道根数", "📏 空间距离与高度" };

            AllParameters = new List<GMATParam> {
                new GMATParam("ElapsedSecs", "⏱️ 时间与历元", "任务经过秒数 (s)", "1800.0"),
                new GMATParam("ElapsedDays", "⏱️ 时间与历元", "任务经过天数 (Days)", "2.2"),
                new GMATParam("A1ModJulian", "⏱️ 时间与历元", "A1修正儒略日", "21545.0"),
                new GMATParam("SMA", "🌐 轨道根数", "半长轴 (Km)", "6578.14"),
                new GMATParam("ECC", "🌐 轨道根数", "轨道离心率", "0.0012"),
                new GMATParam("INC", "🌐 轨道根数", "轨道倾角 (Deg)", "28.5"),
                new GMATParam("RAAN", "🌐 轨道根数", "升交点赤经 (Deg)", "120.0"),
                new GMATParam("AOP", "🌐 轨道根数", "近心点幅角 (Deg)", "45.0"),
                new GMATParam("TA", "🌐 轨道根数", "真近点角 (Deg)", "180.0"),
                new GMATParam("Altitude", "📏 空间距离与高度", "海平面高度 (Km)", "200.0"),
                new GMATParam("RMAG", "📏 空间距离与高度", "距心距离模长 (Km)", "6578.14"),
                new GMATParam("TotalMass", "⚖️ 质量与硬件", "飞船总质量 (kg)", "7500.0"),
                new GMATParam("DryMass", "⚖️ 质量与硬件", "飞船干重 (kg)", "1500.0")
            };

            AllPropStopDefs = new List<PropStopDef> {
                new PropStopDef("Time", "⏱️ 飞行秒数 (ElapsedSecs) [s]", "⏱️ 时间与历元", "s", "推进指定秒数", false, 3600.0, 1e-5),
                new PropStopDef("Days", "📅 飞行天数 (ElapsedDays) [Day]", "⏱️ 时间与历元", "Day", "推进指定天数", false, 3.0, 1e-5),
                new PropStopDef("Apoapsis", "🔺 远拱点 (Apoapsis)", "🪐 轨道极值与交点", "", "自动积分至远地点/远月点", true, 0.0, 1e-5),
                new PropStopDef("Periapsis", "🔻 近拱点 (Periapsis)", "🪐 轨道极值与交点", "", "自动积分至近地点/近月点", true, 0.0, 1e-5),
                new PropStopDef("Node", "🧭 赤道交点 (Latitude = 0°) [°]", "🪐 轨道极值与交点", "°", "自动积分至赤道交点", false, 0.0, 1e-5),
                new PropStopDef("TA", "📐 真近点角 (TA) [°]", "🌐 轨道根数", "°", "真近点角 0~360°", false, 180.0, 1e-4),
                new PropStopDef("SMA", "🌐 半长轴 (SMA) [Km]", "🌐 轨道根数", "Km", "半长轴", false, 7000.0, 0.1),
                new PropStopDef("Altitude", "📏 距地高度 (Altitude) [Km]", "📏 空间距离与高度", "Km", "距地几何高度", false, 400.0, 0.1),
                new PropStopDef("RMAG", "🌐 距心距离 (RMAG) [Km]", "📏 空间距离与高度", "Km", "距心模长", false, 42164.0, 0.1)
            };
        }

        public static PropStopDef GetStopDef(string key) {
            if (string.IsNullOrEmpty(key)) return AllPropStopDefs.Count > 0 ? AllPropStopDefs[0] : new PropStopDef();
            for (int i = 0; i < AllPropStopDefs.Count; i++) {
                if (AllPropStopDefs[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                    return AllPropStopDefs[i];
                }
            }
            return new PropStopDef(key, key, "自定义", "", key, false, 0.0, 1e-5);
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
            if (rawParamsCache != null && rawParamsCache.Count > 0) {
                HashSet<string> catSet = new HashSet<string>();
                List<GMATParam> newParams = new List<GMATParam>();

                foreach (var p in rawParamsCache) {
                    string cat = (I18n.Current == I18n.Lang.EN) ? p.category_en : p.category_zh;
                    if (string.IsNullOrEmpty(cat)) cat = p.category_zh ?? "动力学参数";
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
                    if (string.IsNullOrEmpty(cat)) cat = s.category_zh ?? "终止条件";
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
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Active vessel
            string curName = "Sat_3";
            if (vessel != null && !string.IsNullOrEmpty(vessel.vesselName)) {
                curName = SanitizeGmatIdentifier(vessel.vesselName, "Sat");
            }
            list.Add(curName);
            seen.Add(curName);

            // 2. Real-time User Spacecraft
            foreach (var sc in RealtimeUserSpacecraft) {
                if (!seen.Contains(sc)) { list.Add(sc); seen.Add(sc); }
            }

            // 3. Nearby vessels in FlightGlobals
            try {
                if (FlightGlobals.Vessels != null) {
                    for (int i = 0; i < FlightGlobals.Vessels.Count && list.Count < 6; i++) {
                        var v = FlightGlobals.Vessels[i];
                        if (v != null && v != vessel && !string.IsNullOrEmpty(v.vesselName)) {
                            string sName = SanitizeGmatIdentifier(v.vesselName, "Sat");
                            if (!seen.Contains(sName)) { list.Add(sName); seen.Add(sName); }
                        }
                    }
                }
            } catch {}

            // 4. Cached default spacecraft from GMAT
            for (int i = 0; i < AvailableDefaultSpacecraft.Count; i++) {
                string sc = AvailableDefaultSpacecraft[i];
                if (!seen.Contains(sc)) { list.Add(sc); seen.Add(sc); }
            }

            return list;
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
            for (int i = 0; i < AvailableSolvers.Count; i++) {
                list.Add(AvailableSolvers[i]);
            }
            return list;
        }

        public static List<string> GetAllVariables() {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            for (int i = 0; i < raw.Length; i++) {
                char c = raw[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') {
                    sb.Append(c);
                } else if (c == ' ' || c == '-' || c == '.') {
                    sb.Append('_');
                }
            }
            string res = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(res) || char.IsDigit(res[0])) {
                res = prefix + "_" + res;
            }
            return res;
        }

        public static List<IntelliSenseItem> QueryIntelliSense(string text, Vessel vessel = null) {
            List<IntelliSenseItem> items = new List<IntelliSenseItem>();
            List<string> allBodies = GetAllBodies(vessel);
            List<string> allCoords = GetAllCoordinateSystems();
            List<string> allCraft = GetAllSpacecraft(vessel);
            List<string> allProps = GetAllPropagators();
            List<string> allSolvers = GetAllSolvers();
            List<string> allVars = GetAllVariables();

            string q = (text ?? "").Trim();
            string qLower = q.ToLowerInvariant();

            // =========================================================================
            // MULTI-LEVEL DOT PARSER (e.g. "Sat.", "Sat.EarthMoonRot.", "Sat.EarthMoonRot.Z")
            // =========================================================================
            if (q.Contains(".")) {
                string[] parts = q.Split('.');

                // ---------------------------------------------------------------------
                // CASE A: 2-LEVEL OR HIGHER DOT HIERARCHY (e.g. "Sat.EarthMoonRot.Z", "Sat.Earth.SMA")
                // ---------------------------------------------------------------------
                if (parts.Length >= 3) {
                    string p0 = parts[0].Trim();
                    string p1 = parts[1].Trim();
                    string rhs = parts[2].Trim().ToLowerInvariant();

                    bool isCraft = allCraft.Exists(c => c.Equals(p0, StringComparison.OrdinalIgnoreCase)) || p0.StartsWith("Sat", StringComparison.OrdinalIgnoreCase);
                    
                    if (isCraft) {
                        // Sub-case A1: p1 is a Coordinate System (e.g. "Sat.EarthMoonRot.Z")
                        bool isCoord = allCoords.Exists(c => c.Equals(p1, StringComparison.OrdinalIgnoreCase)) ||
                                       p1.IndexOf("Rot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("Eq", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("Inertial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("Fixed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       p1.IndexOf("ICRF", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isCoord) {
                            string[] coordVars = new string[] { "Z", "X", "Y", "VX", "VY", "VZ", "AX", "AY", "AZ", "RMAG", "Altitude" };
                            string[] coordUnits = new string[] { "[Km]", "[Km]", "[Km]", "[Km/s]", "[Km/s]", "[Km/s]", "[Km/s²]", "[Km/s²]", "[Km/s²]", "[Km]", "[Km]" };
                            string[] coordDocsZh = new string[] {
                                "垂向位置分量 (赤道面或旋转轨道面垂向偏离，常用作地月 NRHO / 晕轨道穿越约束)",
                                "主轴 X 轴位置分量",
                                "侧向 Y 轴位置分量",
                                "X 轴方向速度分量",
                                "Y 轴方向速度分量",
                                "Z 轴方向垂向速度分量",
                                "X 轴方向瞬时加速度",
                                "Y 轴方向瞬时加速度",
                                "Z 轴方向瞬时加速度",
                                "相对该参考系原点的总空间半径模长",
                                "距参考天体基准面高度"
                            };
                            string[] coordDocsEn = new string[] {
                                "Z-axis out-of-plane coordinate (common for Earth-Moon NRHO / Halo orbit crossings)",
                                "X-axis in-plane coordinate",
                                "Y-axis lateral coordinate",
                                "X-axis velocity component",
                                "Y-axis velocity component",
                                "Z-axis out-of-plane velocity component",
                                "X-axis acceleration",
                                "Y-axis acceleration",
                                "Z-axis acceleration",
                                "Radial distance magnitude from frame origin",
                                "Geometric altitude above reference surface"
                            };

                            for (int k = 0; k < coordVars.Length; k++) {
                                string cv = coordVars[k];
                                if (string.IsNullOrEmpty(rhs) || cv.ToLowerInvariant().StartsWith(rhs)) {
                                    string doc = (I18n.Current == I18n.Lang.EN) ? $"{p0} in {p1}: {coordDocsEn[k]}" : $"{p0} 在 {p1} 参考系: {coordDocsZh[k]}";
                                    items.Add(new IntelliSenseItem($"{p0}.{p1}.{cv}", cv, coordUnits[k], doc, IntelliSenseKind.CoordinateSystem, 0.0, 0.01));
                                }
                            }
                            return items;
                        }

                        // Sub-case A2: p1 is a Celestial Body (e.g. "Sat.Earth.SMA", "Sat.Luna.RadApo")
                        bool isBody = allBodies.Exists(b => b.Equals(p1, StringComparison.OrdinalIgnoreCase));
                        if (isBody) {
                            string[] bodyElements = new string[] { "SMA", "ECC", "INC", "RAAN", "AOP", "TA", "Altitude", "RMAG", "RadApo", "RadPer", "Apoapsis", "Periapsis", "OrbitPeriod", "C3Energy", "Energy" };
                            string[] bodyUnits = new string[] { "[Km]", "[Unitless]", "[Deg]", "[Deg]", "[Deg]", "[Deg]", "[Km]", "[Km]", "[Km]", "[Km]", "[Km]", "[Km]", "[s]", "[Km²/s²]", "[Km²/s²]" };
                            string[] bodyDocsZh = new string[] {
                                "开普勒轨道半长轴", "轨道偏心率", "轨道倾角", "升交点赤经", "近心点幅角", "真近点角",
                                "距海平面高度", "距质心轨道半径模长", "远心点半径", "近心点半径", "远拱点高度", "近拱点高度",
                                "开普勒轨道运行周期", "特征能量 C3 (双曲线逃逸指标)", "轨道比机械能"
                            };
                            string[] bodyDocsEn = new string[] {
                                "Semi-major axis", "Orbital eccentricity", "Orbit inclination", "RAAN", "Argument of periapsis", "True anomaly",
                                "Altitude above surface", "Radial distance from center", "Apoapsis radius", "Periapsis radius", "Apoapsis altitude", "Periapsis altitude",
                                "Orbital period", "Characteristic energy C3", "Specific mechanical energy"
                            };
                            double[] bodyDefs = new double[] { 42164.0, 0.0, 28.5, 0.0, 0.0, 0.0, 35786.0, 42164.0, 42164.0, 6578.0, 35786.0, 200.0, 86164.0, 0.0, -10.0 };

                            for (int k = 0; k < bodyElements.Length; k++) {
                                string be = bodyElements[k];
                                if (string.IsNullOrEmpty(rhs) || be.ToLowerInvariant().StartsWith(rhs)) {
                                    string doc = (I18n.Current == I18n.Lang.EN) ? $"{p0} relative to {p1}: {bodyDocsEn[k]}" : $"{p0} 相对 {p1} 中心: {bodyDocsZh[k]}";
                                    items.Add(new IntelliSenseItem($"{p0}.{p1}.{be}", be, bodyUnits[k], doc, IntelliSenseKind.Parameter, bodyDefs[k], 0.1));
                                }
                            }
                            return items;
                        }
                    }
                }

                // ---------------------------------------------------------------------
                // CASE B: SINGLE DOT ACCESS (e.g. "Sat.", "EarthMoonRot.", "Earth.")
                // ---------------------------------------------------------------------
                int lastDotIdx = q.LastIndexOf('.');
                string lhs = q.Substring(0, lastDotIdx).Trim();
                string rhsSingle = q.Substring(lastDotIdx + 1).Trim().ToLowerInvariant();

                // 1. LHS is a Spacecraft (e.g. "Sat.") -> Suggest Coordinate Systems, Bodies, & Properties!
                string matchedCraft = allCraft.Find(cr => cr.Equals(lhs, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matchedCraft) || lhs.StartsWith("Sat", StringComparison.OrdinalIgnoreCase)) {
                    string cr = !string.IsNullOrEmpty(matchedCraft) ? matchedCraft : lhs;

                    // 1a. Coordinate Systems (as nested dot targets like Sat.EarthMoonRot.)
                    for (int c = 0; c < allCoords.Count; c++) {
                        string cs = allCoords[c];
                        if (string.IsNullOrEmpty(rhsSingle) || cs.ToLowerInvariant().StartsWith(rhsSingle) || cs.ToLowerInvariant().Contains(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cr}.{cs}.", $"{cs}...", "[CoordSystem]", (I18n.Current == I18n.Lang.EN) ? $"{cs} coordinate frame (type dot to pick Z, X, Y...)" : $"{cs} 空间坐标系 (继续键入点号以选择 Z, X, Y 坐标分量)", IntelliSenseKind.CoordinateSystem));
                        }
                    }

                    // 1b. Celestial Bodies (as nested dot targets like Sat.Earth.)
                    for (int b = 0; b < allBodies.Count; b++) {
                        string bd = allBodies[b];
                        if (string.IsNullOrEmpty(rhsSingle) || bd.ToLowerInvariant().StartsWith(rhsSingle) || bd.ToLowerInvariant().Contains(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cr}.{bd}.", $"{bd}...", "[Body]", (I18n.Current == I18n.Lang.EN) ? $"Orbital elements relative to {bd} (type dot to pick SMA, ECC...)" : $"相对 {bd} 的轨道根数 (继续键入点号以选择 SMA, ECC, 高度...)", IntelliSenseKind.CelestialBody));
                        }
                    }

                    // 1c. Spacecraft Hardware & State Properties
                    string[] craftDirectProps = new string[] { "TotalMass", "DryMass", "FuelMass", "ElapsedSecs", "A1ModJulian", "Z", "X", "Y" };
                    string[] craftUnits = new string[] { "[kg]", "[kg]", "[kg]", "[s]", "[Day]", "[Km]", "[Km]", "[Km]" };
                    string[] craftDocsZh = new string[] { "航天器当前总湿重", "航天器结构干重", "可用推进剂质量", "任务运行秒数", "A1修正儒略日历元", "地心惯性系Z坐标", "地心惯性系X坐标", "地心惯性系Y坐标" };
                    string[] craftDocsEn = new string[] { "Total wet mass", "Dry structural mass", "Usable propellant mass", "Elapsed mission duration", "A1 Modified Julian Date", "Inertial Z coordinate", "Inertial X coordinate", "Inertial Y coordinate" };

                    for (int p = 0; p < craftDirectProps.Length; p++) {
                        string cp = craftDirectProps[p];
                        if (string.IsNullOrEmpty(rhsSingle) || cp.ToLowerInvariant().StartsWith(rhsSingle)) {
                            string doc = (I18n.Current == I18n.Lang.EN) ? craftDocsEn[p] : craftDocsZh[p];
                            items.Add(new IntelliSenseItem($"{cr}.{cp}", cp, craftUnits[p], $"{cr}: {doc}", IntelliSenseKind.Spacecraft, 0.0, 0.1));
                        }
                    }

                    return items;
                }

                // 2. LHS is a Coordinate System (e.g. "EarthMoonRot.", "EarthMJ2000Eq.")
                string matchedCoord = allCoords.Find(c => c.Equals(lhs, StringComparison.OrdinalIgnoreCase) || c.StartsWith(lhs, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matchedCoord) || lhs.Contains("Rot") || lhs.Contains("MJ") || lhs.Contains("Inertial") || lhs.Contains("Fixed") || lhs.Contains("ICRF")) {
                    string cs = !string.IsNullOrEmpty(matchedCoord) ? matchedCoord : lhs;
                    string[] coordVars = new string[] { "Z", "X", "Y", "VX", "VY", "VZ", "AX", "AY", "AZ", "RMAG", "Altitude" };
                    string[] coordUnits = new string[] { "[Km]", "[Km]", "[Km]", "[Km/s]", "[Km/s]", "[Km/s]", "[Km/s²]", "[Km/s²]", "[Km/s²]", "[Km]", "[Km]" };
                    string[] coordDocsZh = new string[] {
                        "垂向位置分量 (赤道面或旋转轨道面垂向偏离，地月 NRHO 晕轨道常用约束)",
                        "主轴 X 轴位置分量",
                        "侧向 Y 轴位置分量",
                        "X 轴方向速度分量",
                        "Y 轴方向速度分量",
                        "Z 轴方向垂向速度分量",
                        "X 轴方向瞬时加速度",
                        "Y 轴方向瞬时加速度",
                        "Z 轴方向瞬时加速度",
                        "总空间半径模长",
                        "距海平面高度"
                    };

                    for (int m = 0; m < coordVars.Length; m++) {
                        string cv = coordVars[m];
                        if (string.IsNullOrEmpty(rhsSingle) || cv.ToLowerInvariant().StartsWith(rhsSingle)) {
                            items.Add(new IntelliSenseItem($"{cs}.{cv}", cv, coordUnits[m], $"{cs}: {coordDocsZh[m]}", IntelliSenseKind.CoordinateSystem, 0.0, 0.01));
                        }
                    }
                    return items;
                }

                // 3. LHS is a Celestial Body (e.g. "Earth.", "Luna.")
                string matchedBody = allBodies.Find(b => b.Equals(lhs, StringComparison.OrdinalIgnoreCase) || b.StartsWith(lhs, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matchedBody)) {
                    if (rhsSingle == "z" || rhsSingle == "x" || rhsSingle == "y" || rhsSingle == "vx" || rhsSingle == "vy" || rhsSingle == "vz") {
                        string sUp = rhsSingle.ToUpper();
                        items.Add(new IntelliSenseItem($"EarthMJ2000Eq.{sUp}", $"EarthMJ2000Eq.{sUp}", "[CoordinateSystem]", $"J2000 惯性参考系下的空间 {sUp} 分量", IntelliSenseKind.CoordinateSystem, 0.0, 0.1));
                        items.Add(new IntelliSenseItem($"{matchedBody}.Altitude", "Altitude", "[Km]", $"距 {matchedBody} 海平面几何高度", IntelliSenseKind.Parameter, matchedBody == "Earth" ? 35786.0 : 1500.0, 0.1));
                        items.Add(new IntelliSenseItem($"{matchedBody}.RMAG", "RMAG", "[Km]", $"距 {matchedBody} 质心轨道半径模长", IntelliSenseKind.Parameter, matchedBody == "Earth" ? 42164.0 : 3229.0, 0.1));
                        return items;
                    }

                    for (int i = 0; i < AllParameters.Count; i++) {
                        var p = AllParameters[i];
                        if (DirectParams.Contains(p.Name)) continue;
                        if (string.IsNullOrEmpty(rhsSingle) || p.Name.ToLowerInvariant().StartsWith(rhsSingle) || p.Name.ToLowerInvariant().Contains(rhsSingle) || p.GetLocalizedDescription().ToLowerInvariant().Contains(rhsSingle)) {
                            double val = 0.0;
                            double.TryParse(p.Example, out val);
                            items.Add(new IntelliSenseItem($"{matchedBody}.{p.Name}", p.Name, !string.IsNullOrEmpty(p.Unit) ? $"[{p.Unit}]" : "[Param]", $"{matchedBody} {p.GetLocalizedDescription()}", IntelliSenseKind.Parameter, val, 0.1));
                            if (items.Count >= 12) break;
                        }
                    }
                    return items;
                }
            }

            // =========================================================================
            // TOP-LEVEL KEYWORD & ENTITY SUGGESTIONS (when no dot typed yet)
            // =========================================================================
            // 1. Celestial Bodies
            for (int i = 0; i < allBodies.Count; i++) {
                string b = allBodies[i];
                if (string.IsNullOrEmpty(q) || b.ToLowerInvariant().StartsWith(qLower)) {
                    items.Add(new IntelliSenseItem($"{b}.", $"{b}...", "[Body]", (I18n.Current == I18n.Lang.EN) ? $"Celestial Body {b} (type dot to pick parameters)" : $"中心天体 {b} (输入点号继续选择轨道根数)", IntelliSenseKind.CelestialBody));
                }
            }

            // 2. Coordinate Systems (including EarthMoonRot!)
            for (int i = 0; i < allCoords.Count; i++) {
                string cs = allCoords[i];
                if (string.IsNullOrEmpty(q) || cs.ToLowerInvariant().StartsWith(qLower) || cs.ToLowerInvariant().Contains(qLower)) {
                    items.Add(new IntelliSenseItem($"{cs}.", $"{cs}...", "[CoordSystem]", (I18n.Current == I18n.Lang.EN) ? $"Coordinate System {cs} (type dot to pick Z, X, Y)" : $"空间参考系 {cs} (输入点号继续选择坐标分量)", IntelliSenseKind.CoordinateSystem));
                }
            }

            // 3. Spacecraft
            for (int i = 0; i < allCraft.Count; i++) {
                string sc = allCraft[i];
                if (string.IsNullOrEmpty(q) || sc.ToLowerInvariant().StartsWith(qLower)) {
                    items.Add(new IntelliSenseItem($"{sc}.", $"{sc}...", "[Spacecraft]", (I18n.Current == I18n.Lang.EN) ? $"Spacecraft {sc} (type dot to pick coordinate systems or properties)" : $"航天器 {sc} (输入点号继续选择坐标系或属性)", IntelliSenseKind.Spacecraft));
                }
            }

            // 4. Standalone Common Variables
            for (int i = 0; i < allVars.Count; i++) {
                string vName = allVars[i];
                if (string.IsNullOrEmpty(q) || vName.ToLowerInvariant().StartsWith(qLower)) {
                    items.Add(new IntelliSenseItem(vName, vName, "[Var]", (I18n.Current == I18n.Lang.EN) ? "Mission user variable" : "任务自定义变量", IntelliSenseKind.Variable));
                }
            }

            // 5. Popular Direct Parameters
            string[] popular = new string[] { "SMA", "ECC", "INC", "Altitude", "RMAG", "Z", "X", "Y", "TotalMass" };
            for (int i = 0; i < popular.Length; i++) {
                string pop = popular[i];
                if (string.IsNullOrEmpty(q) || pop.ToLowerInvariant().StartsWith(qLower)) {
                    items.Add(new IntelliSenseItem(pop, pop, "[Param]", (I18n.Current == I18n.Lang.EN) ? $"Direct orbital parameter {pop}" : $"常用动力学参数 {pop}", IntelliSenseKind.Parameter));
                }
            }

            return items;
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

    }
}
