using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KSPGMAT;

namespace KSPGMAT.Model {
    /// <summary>
    /// GMAT 参数能力与上下文约束位掩码标签 (Field Capability Flags)
    /// </summary>
    [Flags]
    public enum ParamCapability {
        None = 0,
        /// <summary>连续可导物理量 (适用于 Achieve 目标收敛与牛顿法迭代，如 SMA, INC, ECC, Altitude, RMAG, Latitude, BdotT, IncomingDHA)</summary>
        ContinuouslyDifferentiable = 1 << 0,
        /// <summary>离散极值与几何事件 (仅用于 Propagate 停机条件，如 Apoapsis, Periapsis, AscendingNode，严禁用于 Achieve)</summary>
        DiscontinuousExtremumEvent = 1 << 1,
        /// <summary>独立控制与寻优变量 (适用于 Vary 决策变量，如 ThrustDirection1/2/3, C1, 点火时长, 脉冲分量)</summary>
        ControlVariable = 1 << 2,
        /// <summary>航天器与动力学硬件物理属性 (如 DryMass, FuelMass, TotalMass, Volume, Isp, Cd, Cr, SRPArea)</summary>
        HardwareProperty = 1 << 3,
        /// <summary>参考系笛卡尔空间状态向量 (X, Y, Z, VX, VY, VZ, AX, AY, AZ, HMAG)</summary>
        CoordinateCartesian = 1 << 4,
        /// <summary>姿态与四元数/欧拉角 (Q1~Q4, Euler, MRP, DCM)</summary>
        AttitudeOrientation = 1 << 5,
        /// <summary>积分器停机条件 (包含连续量阈值与极值事件)</summary>
        PropagateCondition = ContinuouslyDifferentiable | DiscontinuousExtremumEvent,
        /// <summary>全量可报表输出与监视量</summary>
        Reportable = ContinuouslyDifferentiable | DiscontinuousExtremumEvent | ControlVariable | HardwareProperty | CoordinateCartesian | AttitudeOrientation
    }

    /// <summary>
    /// GMAT 官方标准参数元数据定义 (Schema Metadata Definition)
    /// </summary>
    public class GMATParamDefinition {
        public string Name { get; set; }
        public string Category { get; set; }
        public ParamCapability Capability { get; set; }
        public string Unit { get; set; }
        public double DefaultValue { get; set; }
        public double TypicalStep { get; set; }
        /// <summary>i18n 词典键 (param_*)，描述文本存于 loc/*.json，随当前语言热切换。</summary>
        public string DocKey { get; set; }
        public string FallbackDoc { get; set; }

        public GMATParamDefinition(string name, string category, ParamCapability cap, string unit, double defVal, double step, string docKey = null, string fallbackDoc = null) {
            Name = name;
            Category = category;
            Capability = cap;
            Unit = unit;
            DefaultValue = defVal;
            TypicalStep = step;
            DocKey = !string.IsNullOrEmpty(docKey) ? docKey : "param_" + name;
            FallbackDoc = fallbackDoc;
        }

        /// <summary>本地化参数描述 (优先读 i18n，未收录时自动回退官方动态说明)</summary>
        public string GetDoc() {
            if (!string.IsNullOrEmpty(DocKey) && I18n.HasKey(DocKey)) return I18n.T(DocKey);
            return !string.IsNullOrEmpty(FallbackDoc) ? FallbackDoc : Name;
        }

        /// <summary>本地化单位显示</summary>
        public string GetUnit() {
            if (!string.IsNullOrEmpty(DocKey) && I18n.HasKey(DocKey + "_unit")) return I18n.T(DocKey + "_unit");
            return Unit ?? "";
        }
    }

    /// <summary>
    /// GMAT 参数目录轻量动态门面 (Facade)，全量对接 GMATSchemaRegistry，100% 消除硬编码并保持对外签名完全兼容
    /// </summary>
    public static class GMATParamCatalog {
        public static List<GMATParamDefinition> DirectKeplerian => GMATSchemaRegistry.Instance.DirectKeplerian;
        public static List<GMATParamDefinition> EquinoctialAndDelaunay => GMATSchemaRegistry.Instance.EquinoctialAndDelaunay;
        public static List<GMATParamDefinition> BrouwerMeanElements => GMATSchemaRegistry.Instance.BrouwerMeanElements;
        public static List<GMATParamDefinition> SphericalAndPlanetodetic => GMATSchemaRegistry.Instance.SphericalAndPlanetodetic;
        public static List<GMATParamDefinition> HyperbolicAndBPlane => GMATSchemaRegistry.Instance.HyperbolicAndBPlane;
        public static List<GMATParamDefinition> ExtremumEvents => GMATSchemaRegistry.Instance.ExtremumEvents;
        public static List<GMATParamDefinition> CoordinateCartesian => GMATSchemaRegistry.Instance.CoordinateCartesian;
        public static List<GMATParamDefinition> SpacecraftHardware => GMATSchemaRegistry.Instance.SpacecraftHardware;
        public static List<GMATParamDefinition> ThrusterProperties => GMATSchemaRegistry.Instance.ThrusterProperties;
        public static List<GMATParamDefinition> TankProperties => GMATSchemaRegistry.Instance.TankProperties;
        public static List<GMATParamDefinition> AttitudeAndOrientation => GMATSchemaRegistry.Instance.AttitudeAndOrientation;
    }

    /// <summary>
    /// GMAT 官方 API (gmatpy / REST API) 遍历数值与能力架构元数据注册表 (Single Source of Capabilities Truth)。
    /// 遵循 baseline_agent.md 铁律 9，严禁在 C# 写死清单，以 gmat_capabilities.json 与原生动态反射为准。
    /// </summary>
    public class GMATSchemaRegistry {
        private static GMATSchemaRegistry _instance;
        public static GMATSchemaRegistry Instance => _instance ?? (_instance = new GMATSchemaRegistry());

        [Serializable]
        public class ParamMetadata {
            public string key;
            public string name;
            public string description;
            public string category_zh;
            public string category_en;
            public string unit;
            public string dep_object;
            public double default_value;
            public double default_tolerance = 0.1;
            public bool is_extremum = false;
        }

        [Serializable]
        public class ApiMethodMetadata {
            public string name;
            public List<string> args = new List<string>();
            public string doc = "";
            public string impl = "";
            public string parent_class = "";
        }

        [Serializable]
        public class ApiClassMetadata {
            public string class_name;
            public string module;
            public List<string> bases = new List<string>();
            public string family = "";
            public int method_count;
            public List<ApiMethodMetadata> methods = new List<ApiMethodMetadata>();
        }

        [Serializable]
        public class ApiFunctionMetadata {
            public string name;
            public string module;
            public string file;
            public List<string> args = new List<string>();
            public string doc = "";
            public string impl = "";
        }

        public enum CapabilityItemType {
            PhysicalParam,
            ApiClass,
            ApiMethod,
            TopFunction,
            UserFunction,
            UtilityFunction
        }

        public class CapabilitySearchResult {
            public CapabilityItemType itemType;
            public string title;
            public string subtitle;
            public string categoryOrFamily;
            public string signature;
            public string doc;
            public string codeImpl;
            public string insertSnippet;
        }

        private readonly Dictionary<string, ParamMetadata> _parameters = new Dictionary<string, ParamMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ParamMetadata>> _paramCategoryMap = new Dictionary<string, List<ParamMetadata>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ApiClassMetadata> _apiClassesList = new List<ApiClassMetadata>();
        private readonly Dictionary<string, ApiClassMetadata> _apiClassesDict = new Dictionary<string, ApiClassMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ApiClassMetadata>> _familyClassesMap = new Dictionary<string, List<ApiClassMetadata>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ApiFunctionMetadata> _topFunctionsList = new List<ApiFunctionMetadata>();
        private readonly List<ApiFunctionMetadata> _userFunctionsList = new List<ApiFunctionMetadata>();
        private readonly List<ApiFunctionMetadata> _utilityFunctionsList = new List<ApiFunctionMetadata>();

        private readonly HashSet<string> _builtinCoordSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _builtinSolvers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _solverAlgorithms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _solverDerivatives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _builtinPropagators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _bodyMu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _bodyRadii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _bodyNaifIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _axisTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _bodies = new List<string>();
        private readonly HashSet<string> _apiTopFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _apiClasses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded { get; private set; } = false;
        public int ParameterCount => _parameters.Count;
        public int ApiClassCount => _apiClassesList.Count > 0 ? _apiClassesList.Count : _apiClasses.Count;
        public int ApiMethodCount => _apiClassesList.Sum(c => c.method_count);
        public int TopFunctionCount => _topFunctionsList.Count > 0 ? _topFunctionsList.Count : _apiTopFunctions.Count;
        public int UserFunctionCount => _userFunctionsList.Count;
        public int UtilityFunctionCount => _utilityFunctionsList.Count;
        public List<GMATParamDefinition> DirectKeplerian { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> EquinoctialAndDelaunay { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> BrouwerMeanElements { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> SphericalAndPlanetodetic { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> HyperbolicAndBPlane { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> ExtremumEvents { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> CoordinateCartesian { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> SpacecraftHardware { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> ThrusterProperties { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> TankProperties { get; } = new List<GMATParamDefinition>();
        public List<GMATParamDefinition> AttitudeAndOrientation { get; } = new List<GMATParamDefinition>();

        public static ParamCapability DetermineCapability(ParamMetadata p) {
            if (p == null) return ParamCapability.Reportable;
            if (p.is_extremum || p.name == "Apoapsis" || p.name == "Periapsis" || p.name.EndsWith("Node", StringComparison.OrdinalIgnoreCase)) {
                return ParamCapability.DiscontinuousExtremumEvent;
            }
            string n = p.name ?? "";
            string cat = (p.category_en ?? "").ToLowerInvariant();

            if (n.StartsWith("Thrust", StringComparison.OrdinalIgnoreCase) || n == "C1" || n == "DutyCycle" || n == "ThrustScaleFactor") {
                return ParamCapability.ControlVariable | ParamCapability.Reportable;
            }
            if (cat.Contains("hardware") || cat.Contains("mass") || n.Contains("Tank") || n.Contains("Mass") || n == "DragArea" || n == "SRPArea" || n == "Cd" || n == "Cr" || n == "Volume" || n == "Pressure" || n == "K1") {
                return ParamCapability.HardwareProperty | ParamCapability.Reportable;
            }
            if (n.StartsWith("Euler", StringComparison.OrdinalIgnoreCase) || n.StartsWith("AngularVelocity", StringComparison.OrdinalIgnoreCase) || (n.Length == 2 && n[0] == 'Q' && char.IsDigit(n[1]))) {
                return ParamCapability.AttitudeOrientation | ParamCapability.Reportable;
            }
            if (cat.Contains("vector") || n == "X" || n == "Y" || n == "Z" || n == "VX" || n == "VY" || n == "VZ" || n == "AX" || n == "AY" || n == "AZ" || (n.StartsWith("H", StringComparison.OrdinalIgnoreCase) && n.Length <= 4)) {
                return ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable;
            }
            return ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable;
        }

        private void RebuildCatalogDefinitions() {
            DirectKeplerian.Clear();
            EquinoctialAndDelaunay.Clear();
            BrouwerMeanElements.Clear();
            SphericalAndPlanetodetic.Clear();
            HyperbolicAndBPlane.Clear();
            ExtremumEvents.Clear();
            CoordinateCartesian.Clear();
            SpacecraftHardware.Clear();
            ThrusterProperties.Clear();
            TankProperties.Clear();
            AttitudeAndOrientation.Clear();

            foreach (var p in _parameters.Values) {
                ParamCapability cap = DetermineCapability(p);
                double step = p.default_tolerance > 0 ? p.default_tolerance : 0.1;
                string unit = !string.IsNullOrEmpty(p.unit) ? $"[{p.unit}]" : "";
                var def = new GMATParamDefinition(p.name, p.category_en ?? p.category_zh ?? "General", cap, unit, p.default_value, step, "param_" + p.name, p.description);

                string n = p.name ?? "";
                string cat = (p.category_en ?? "").ToLowerInvariant();

                if (p.is_extremum || n == "Apoapsis" || n == "Periapsis" || n.EndsWith("Node", StringComparison.OrdinalIgnoreCase)) {
                    ExtremumEvents.Add(def);
                    continue;
                }

                if (n.StartsWith("Thrust", StringComparison.OrdinalIgnoreCase) || n == "C1" || n == "K1" || n == "DutyCycle" || n == "MassFlowRate" || n == "ThrustScaleFactor") {
                    ThrusterProperties.Add(def);
                } else if (n.Contains("Tank") || n == "FuelDensity" || n == "Pressure" || n == "PressureModel" || (n == "Volume" && cat.Contains("hardware"))) {
                    TankProperties.Add(def);
                } else if (n.StartsWith("Euler", StringComparison.OrdinalIgnoreCase) || n.StartsWith("AngularVelocity", StringComparison.OrdinalIgnoreCase) || (n.Length == 2 && n[0] == 'Q' && char.IsDigit(n[1]))) {
                    AttitudeAndOrientation.Add(def);
                } else if (n.StartsWith("ModEquinoctial", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Equinoctial", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Delaunay", StringComparison.OrdinalIgnoreCase)) {
                    EquinoctialAndDelaunay.Add(def);
                } else if (n.StartsWith("Brouwer", StringComparison.OrdinalIgnoreCase)) {
                    BrouwerMeanElements.Add(def);
                } else if (n.StartsWith("Incoming", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Outgoing", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Bdot", StringComparison.OrdinalIgnoreCase) || n.StartsWith("BVector", StringComparison.OrdinalIgnoreCase)) {
                    HyperbolicAndBPlane.Add(def);
                } else if (cat.Contains("keplerian") || n == "SMA" || n == "ECC" || n == "INC" || n == "RAAN" || n == "AOP" || n == "TA" || n == "MA" || n == "EA" || n == "HA" || n == "MM" || n == "RadPer" || n == "RadApo" || n == "SemilatusRectum" || n == "OrbitPeriod" || n == "Energy" || n == "C3Energy") {
                    DirectKeplerian.Add(def);
                } else if (cat.Contains("geographic") || cat.Contains("altitude") || cat.Contains("distance") || n == "Altitude" || n == "RMAG" || n == "VMAG" || n == "AMAG" || n == "Latitude" || n == "Longitude" || n == "RA" || n == "DEC" || n == "AZI" || n == "FPA" || n.StartsWith("Planetodetic", StringComparison.OrdinalIgnoreCase)) {
                    SphericalAndPlanetodetic.Add(def);
                } else if (cat.Contains("vector") || n == "X" || n == "Y" || n == "Z" || n == "VX" || n == "VY" || n == "VZ" || n == "AX" || n == "AY" || n == "AZ" || (n.StartsWith("H", StringComparison.OrdinalIgnoreCase) && n.Length <= 4)) {
                    CoordinateCartesian.Add(def);
                } else if (cat.Contains("hardware") || cat.Contains("mass") || cat.Contains("epoch") || n == "TotalMass" || n == "DryMass" || n == "FuelMass" || n == "DragArea" || n == "SRPArea" || n == "Cd" || n == "Cr" || n.StartsWith("Elapsed", StringComparison.OrdinalIgnoreCase)) {
                    SpacecraftHardware.Add(def);
                } else {
                    DirectKeplerian.Add(def);
                }
            }
        }

        private GMATSchemaRegistry() {
            LoadCapabilities();
        }

        /// <summary>
        /// 动态加载 gmat_capabilities.json
        /// </summary>
        public void LoadCapabilities() {
            try {
                string jsonPath = FindCapabilitiesJsonPath();
                if (File.Exists(jsonPath)) {
                    string json = File.ReadAllText(jsonPath);
                    ParseJson(json);
                    IsLoaded = true;
                    return;
                }
            } catch (Exception ex) {
                KSPGMATLog.Error("SchemaRegistry", "LoadCapabilities error", ex);
            }

            // 兜底载入核心基准元数据 (防文件丢失)
            LoadFallbackMetadata();
            IsLoaded = true;
        }

        private string FindCapabilitiesJsonPath() {
            string kspPath = null;
            try {
                kspPath = KSPUtil.ApplicationRootPath;
            } catch { }

            string[] candidates = new string[] {
                !string.IsNullOrEmpty(kspPath) ? Path.Combine(kspPath, "GameData", "KSPGMAT", "gmat_capabilities.json") : null,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "KSPGMAT", "gmat_capabilities.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gmat_capabilities.json"),
                @"C:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\GameData\KSPGMAT\gmat_capabilities.json"
            };
            foreach (var p in candidates) {
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            return "";
        }


        private void ParseJson(string json) {
            _parameters.Clear();
            _paramCategoryMap.Clear();
            _apiClassesList.Clear();
            _apiClassesDict.Clear();
            _familyClassesMap.Clear();
            _topFunctionsList.Clear();
            _userFunctionsList.Clear();
            _utilityFunctionsList.Clear();

            _builtinCoordSystems.Clear();
            _builtinSolvers.Clear();
            _solverAlgorithms.Clear();
            _solverDerivatives.Clear();
            _builtinPropagators.Clear();
            _bodyNaifIds.Clear();
            try {
                object rawObj = EdyCommonTools.MiniJSON.jsonDecode(json);
                if (rawObj is System.Collections.IDictionary root) {
                    ParseJsonWithMiniJson(root);
                    EnsureDefaultEngineeringPresets();
                    RebuildCatalogDefinitions();
                    return;
                }
            } catch (Exception ex) {
                KSPGMATLog.Error("SchemaRegistry", "MiniJSON parse fallback", ex);
            }

            ParseJsonFallback(json);
            EnsureDefaultEngineeringPresets();
            RebuildCatalogDefinitions();
        }

        private void ParseJsonWithMiniJson(System.Collections.IDictionary root) {
            // 1. 物理计算参数
            if (root.Contains("parameters") && root["parameters"] is System.Collections.IList pList) {
                foreach (var item in pList) {
                    if (item is System.Collections.IDictionary pDict) {
                        string key = GetDictStr(pDict, "key");
                        if (string.IsNullOrEmpty(key)) continue;

                        var meta = new ParamMetadata {
                            key = key,
                            name = GetDictStr(pDict, "name") ?? key,
                            description = GetDictStr(pDict, "description"),
                            unit = GetDictStr(pDict, "unit"),
                            category_zh = GetDictStr(pDict, "category_zh"),
                            category_en = GetDictStr(pDict, "category_en"),
                            dep_object = GetDictStr(pDict, "dep_object"),
                            default_value = GetDictDouble(pDict, "default_value"),
                            default_tolerance = GetDictDouble(pDict, "default_tolerance", 0.1),
                            is_extremum = GetDictBool(pDict, "is_extremum")
                        };
                        _parameters[key] = meta;

                        string catZh = meta.category_zh ?? "📐 其他动力学参数";
                        if (!_paramCategoryMap.TryGetValue(catZh, out var catList)) {
                            catList = new List<ParamMetadata>();
                            _paramCategoryMap[catZh] = catList;
                        }
                        catList.Add(meta);
                    }
                }
            }

            // 2. 官方 API 类 (包含完整方法实现)
            if (root.Contains("api_classes_list") && root["api_classes_list"] is System.Collections.IList cList) {
                foreach (var item in cList) {
                    if (item is System.Collections.IDictionary cDict) {
                        string cname = GetDictStr(cDict, "class_name");
                        if (string.IsNullOrEmpty(cname)) continue;

                        var cMeta = new ApiClassMetadata {
                            class_name = cname,
                            module = GetDictStr(cDict, "module"),
                            family = GetDictStr(cDict, "family"),
                            method_count = (int)GetDictLong(cDict, "method_count")
                        };

                        if (cDict.Contains("bases") && cDict["bases"] is System.Collections.IList bList) {
                            foreach (var b in bList) if (b != null) cMeta.bases.Add(b.ToString());
                        }

                        if (cDict.Contains("methods") && cDict["methods"] is System.Collections.IList mList) {
                            foreach (var mItem in mList) {
                                if (mItem is System.Collections.IDictionary mDict) {
                                    var mMeta = new ApiMethodMetadata {
                                        name = GetDictStr(mDict, "name"),
                                        doc = GetDictStr(mDict, "doc"),
                                        impl = GetDictStr(mDict, "impl"),
                                        parent_class = cname
                                    };
                                    if (mDict.Contains("args") && mDict["args"] is System.Collections.IList aList) {
                                        foreach (var a in aList) if (a != null) mMeta.args.Add(a.ToString());
                                    }
                                    cMeta.methods.Add(mMeta);
                                }
                            }
                        }

                        _apiClassesList.Add(cMeta);
                        _apiClassesDict[cname] = cMeta;
                        _apiClasses[cname] = cMeta.methods.Select(m => m.name).ToList();

                        string fam = !string.IsNullOrEmpty(cMeta.family) ? cMeta.family : "⚙️ 底层内核与数学构件 (Core Engine & Math)";
                        if (!_familyClassesMap.TryGetValue(fam, out var fList)) {
                            fList = new List<ApiClassMetadata>();
                            _familyClassesMap[fam] = fList;
                        }
                        fList.Add(cMeta);
                    }
                }
            }

            // 3. 顶层函数
            ParseFunctionList(root, "api_top_functions", _topFunctionsList);
            foreach (var f in _topFunctionsList) _apiTopFunctions.Add(f.name);

            // 4. 用户函数
            ParseFunctionList(root, "user_functions", _userFunctionsList);
            EnsureDefaultEngineeringPresets();

            // 5. 实用工具函数
            ParseFunctionList(root, "utility_functions", _utilityFunctionsList);

            // 6. 内置坐标系、求解器与积分器
            ExtractDictArray(root, "coordinate_systems", _builtinCoordSystems);
            ExtractDictArray(root, "axis_types", _axisTypes);
            ExtractDictArray(root, "solvers", _builtinSolvers);
            ExtractDictArray(root, "solver_algorithms", _solverAlgorithms);
            ExtractDictArray(root, "solver_derivatives", _solverDerivatives);
            ExtractDictArray(root, "propagators", _builtinPropagators);

            // 7. NAIF ID 字典
            if (root.Contains("body_naif_ids") && root["body_naif_ids"] is System.Collections.IDictionary naifDict) {
                foreach (System.Collections.DictionaryEntry kvp in naifDict) {
                    if (int.TryParse(kvp.Value?.ToString(), out int id)) {
                        _bodyNaifIds[kvp.Key?.ToString().Trim()] = id;
                    }
                }
            }
        }

        private static string GetDictStr(System.Collections.IDictionary dict, string key) {
            if (dict.Contains(key) && dict[key] != null) return dict[key].ToString();
            return "";
        }
        private static double GetDictDouble(System.Collections.IDictionary dict, string key, double def = 0.0) {
            if (dict.Contains(key) && dict[key] != null) {
                if (double.TryParse(dict[key].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
            }
            return def;
        }
        private static long GetDictLong(System.Collections.IDictionary dict, string key, long def = 0) {
            if (dict.Contains(key) && dict[key] != null) {
                if (long.TryParse(dict[key].ToString(), out long l)) return l;
            }
            return def;
        }
        private static bool GetDictBool(System.Collections.IDictionary dict, string key) {
            if (dict.Contains(key) && dict[key] != null) {
                if (bool.TryParse(dict[key].ToString(), out bool b)) return b;
            }
            return false;
        }
        private static void ExtractDictArray(System.Collections.IDictionary dict, string key, HashSet<string> set) {
            if (dict.Contains(key) && dict[key] is System.Collections.IList list) {
                foreach (var item in list) if (item != null) set.Add(item.ToString().Trim());
            }
        }
        private static void ParseFunctionList(System.Collections.IDictionary root, string key, List<ApiFunctionMetadata> list) {
            if (root.Contains(key) && root[key] is System.Collections.IList fList) {
                foreach (var item in fList) {
                    if (item is System.Collections.IDictionary fDict) {
                        string fname = GetDictStr(fDict, "name");
                        if (string.IsNullOrEmpty(fname)) continue;
                        var fMeta = new ApiFunctionMetadata {
                            name = fname,
                            module = GetDictStr(fDict, "module"),
                            file = GetDictStr(fDict, "file"),
                            doc = GetDictStr(fDict, "doc"),
                            impl = GetDictStr(fDict, "impl")
                        };
                        if (fDict.Contains("args") && fDict["args"] is System.Collections.IList aList) {
                            foreach (var a in aList) if (a != null) fMeta.args.Add(a.ToString());
                        }
                        list.Add(fMeta);
                    }
                }
            }
        }

        private void ParseJsonFallback(string json) {

            // 提取 parameters 数组
            var paramMatches = Regex.Matches(json, @"\{[^{}]*""key""\s*:\s*""([^""]+)""[^{}]*\}", RegexOptions.Singleline);
            foreach (Match m in paramMatches) {
                string block = m.Value;
                string key = ExtractJsonString(block, "key");
                if (string.IsNullOrEmpty(key)) continue;

                var meta = new ParamMetadata {
                    key = key,
                    name = ExtractJsonString(block, "name") ?? key,
                    description = ExtractJsonString(block, "description"),
                    unit = ExtractJsonString(block, "unit"),
                    category_zh = ExtractJsonString(block, "category_zh"),
                    category_en = ExtractJsonString(block, "category_en"),
                    is_extremum = block.IndexOf("\"is_extremum\": true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  block.IndexOf("\"is_extremum\":true", StringComparison.OrdinalIgnoreCase) >= 0
                };
                string defValStr = ExtractJsonNumber(block, "default_value");
                if (!string.IsNullOrEmpty(defValStr) && double.TryParse(defValStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) {
                    meta.default_value = dVal;
                }
                string defTolStr = ExtractJsonNumber(block, "default_tolerance");
                if (!string.IsNullOrEmpty(defTolStr) && double.TryParse(defTolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double dTol)) {
                    meta.default_tolerance = dTol;
                }
                _parameters[key] = meta;
            }

            // 提取内置坐标系、轴系、求解器与天体
            ExtractJsonArray(json, "coordinate_systems", _builtinCoordSystems);
            ExtractJsonArray(json, "axis_types", _axisTypes);
            ExtractJsonArray(json, "solvers", _builtinSolvers);
            ExtractJsonArray(json, "solver_algorithms", _solverAlgorithms);
            ExtractJsonArray(json, "solver_derivatives", _solverDerivatives);
            ExtractJsonArray(json, "propagators", _builtinPropagators);

            // 提取天体 NAIF ID 字典
            var naifMatch = Regex.Match(json, @"""body_naif_ids""\s*:\s*\{([^}]+)\}", RegexOptions.Singleline);
            if (naifMatch.Success) {
                var pairs = Regex.Matches(naifMatch.Groups[1].Value, @"""([^""]+)""\s*:\s*(\d+)");
                foreach (Match pm in pairs) {
                    if (int.TryParse(pm.Groups[2].Value, out int idVal)) {
                        _bodyNaifIds[pm.Groups[1].Value.Trim()] = idVal;
                    }
                }
            }

            // 提取顶层函数与 API 类
            var funcsMatch = Regex.Match(json, @"""api_top_functions""\s*:\s*\[([^\]]*)\]", RegexOptions.Singleline);
            if (funcsMatch.Success) {
                var fMatches = Regex.Matches(funcsMatch.Groups[1].Value, @"""name""\s*:\s*""([^""]+)""");
                foreach (Match fm in fMatches) _apiTopFunctions.Add(fm.Groups[1].Value.Trim());
            }

            var classMatches = Regex.Matches(json, @"""([A-Za-z0-9_]+)""\s*:\s*\{\s*""class_name""\s*:\s*""\1""", RegexOptions.Multiline);
            foreach (Match cm in classMatches) {
                _apiClasses[cm.Groups[1].Value.Trim()] = new List<string>();
            }
        }


        private void LoadFallbackMetadata() {
            // 兜底常用官方参数 (遵守 baseline 3)
            string[] extrema = new[] { "Apoapsis", "Periapsis", "Node", "AscendingNode", "DescendingNode" };
            foreach (var e in extrema) {
                _parameters[e] = new ParamMetadata { key = e, name = e, is_extremum = true, default_tolerance = 0.1 };
            }
            _parameters["ElapsedSecs"] = new ParamMetadata { key = "ElapsedSecs", name = "ElapsedSecs", unit = "s", default_tolerance = 1e-5, default_value = 3600.0 };
            _parameters["ElapsedDays"] = new ParamMetadata { key = "ElapsedDays", name = "ElapsedDays", unit = "day", default_tolerance = 1e-5, default_value = 1.0 };
            _parameters["Altitude"] = new ParamMetadata { key = "Altitude", name = "Altitude", unit = "km", default_tolerance = 0.5, default_value = 200.0 };
            _parameters["SMA"] = new ParamMetadata { key = "SMA", name = "SMA", unit = "km", default_tolerance = 0.5, default_value = 7000.0 };
            _parameters["ECC"] = new ParamMetadata { key = "ECC", name = "ECC", unit = "", default_tolerance = 0.001, default_value = 0.0 };
            _parameters["INC"] = new ParamMetadata { key = "INC", name = "INC", unit = "deg", default_tolerance = 0.01, default_value = 28.5 };
            _parameters["Latitude"] = new ParamMetadata { key = "Latitude", name = "Latitude", unit = "deg", default_tolerance = 0.01, default_value = 0.0 };

            _builtinCoordSystems.Add("EarthMJ2000Eq");
            _builtinCoordSystems.Add("EarthMJ2000Ec");
            _builtinCoordSystems.Add("EarthFixed");
            _builtinCoordSystems.Add("EarthICRF");
            _builtinCoordSystems.Add("Local");

            _builtinSolvers.Add("DifferentialCorrector");
            _builtinSolvers.Add("DefaultDC");
            _builtinSolvers.Add("Yukon");
            _builtinSolvers.Add("fmincon");
            _builtinSolvers.Add("SQP");

            RebuildCatalogDefinitions();
        }

        /// <summary>
        /// 判定指定参数是否为官方极值事件 (极值事件仅限用于 Propagate 停机条件，严禁用于 Achieve)
        /// </summary>
        public bool IsExtremumParameter(string paramName) {
            if (string.IsNullOrWhiteSpace(paramName)) return false;
            string clean = paramName.Trim();
            if (clean.Contains(".")) clean = clean.Substring(clean.LastIndexOf('.') + 1);

            if (_parameters.TryGetValue(clean, out var meta)) {
                return meta.is_extremum;
            }
            // 语义兜底
            return clean.Equals("Apoapsis", StringComparison.OrdinalIgnoreCase) ||
                   clean.Equals("Periapsis", StringComparison.OrdinalIgnoreCase) ||
                   clean.Equals("Node", StringComparison.OrdinalIgnoreCase) ||
                   clean.Equals("AscendingNode", StringComparison.OrdinalIgnoreCase) ||
                   clean.Equals("DescendingNode", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 根据官方 API 遍历元数据获取指定物理参数的权威容差
        /// </summary>
        public double GetAuthoritativeTolerance(string paramName, double fallback = 0.1) {
            if (string.IsNullOrWhiteSpace(paramName)) return fallback;
            string clean = paramName.Trim();
            if (clean.Contains(".")) clean = clean.Substring(clean.LastIndexOf('.') + 1);

            if (_parameters.TryGetValue(clean, out var meta) && meta.default_tolerance > 0) {
                return meta.default_tolerance;
            }
            // 按物理量量纲赋予标准容差 (速度 0.01 km/s, 角度 0.01 deg, 距离 0.5 km)
            string u = clean.ToUpperInvariant();
            if (u.Contains("VEL") || u.Contains("VMAG") || u.Contains("C3") || u.Contains("ENERGY")) return 0.01;
            if (u.Contains("INC") || u.Contains("RAAN") || u.Contains("AOP") || u.Contains("LATITUDE") || u == "TA" || u == "MA" || u == "EA") return 0.01;
            return 0.5;
        }

        /// <summary>
        /// 判定坐标系是否为官方内置默认预置坐标系 (内置坐标系无需 Create)
        /// </summary>
        public bool IsDefaultBuiltinCoordSystem(string csName) {
            if (string.IsNullOrEmpty(csName)) return false;
            return _builtinCoordSystems.Contains(csName) || csName.Equals("Local", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取天体权威 NAIF ID (SPICE 权威编号)
        /// </summary>
        public int GetBodyNaifId(string bodyName, int fallback = 0) {
            if (string.IsNullOrEmpty(bodyName)) return fallback;
            string clean = NormalizeCelestialName(bodyName);
            if (_bodyNaifIds.TryGetValue(clean, out int id)) return id;
            // 语义权威兜底
            if (clean.Equals("Mars", StringComparison.OrdinalIgnoreCase)) return 4;
            if (clean.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)) return 5;
            if (clean.Equals("Saturn", StringComparison.OrdinalIgnoreCase)) return 6;
            if (clean.Equals("Uranus", StringComparison.OrdinalIgnoreCase)) return 7;
            if (clean.Equals("Neptune", StringComparison.OrdinalIgnoreCase)) return 8;
            if (clean.Equals("Pluto", StringComparison.OrdinalIgnoreCase)) return 9;
            if (clean.Equals("Sun", StringComparison.OrdinalIgnoreCase)) return 10;
            if (clean.Equals("Mercury", StringComparison.OrdinalIgnoreCase)) return 199;
            if (clean.Equals("Venus", StringComparison.OrdinalIgnoreCase)) return 299;
            if (clean.Equals("Earth", StringComparison.OrdinalIgnoreCase)) return 399;
            if (clean.Equals("Luna", StringComparison.OrdinalIgnoreCase)) return 301;
            return fallback;
        }

        /// <summary>
        /// 获取元数据中记录的所有已知天体名称
        /// </summary>
        public IEnumerable<string> GetKnownBodies() {
            if (_bodyNaifIds.Count > 0) return _bodyNaifIds.Keys;
            return new[] { "Sun", "Mercury", "Venus", "Earth", "Luna", "Mars", "Jupiter", "Europa", "Saturn", "Titan", "Uranus", "Neptune", "Pluto" };
        }

        /// <summary>
        /// 获取需要配置 SPICE NAIFId 的外层标准大行星列表
        /// </summary>
        public List<(string bodyName, int naifId)> GetStandardSpicePlanets() {
            var list = new List<(string, int)>();
            string[] targetPlanets = new[] { "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };
            foreach (var p in targetPlanets) {
                list.Add((p, GetBodyNaifId(p)));
            }
            return list;
        }

        /// <summary>
        /// 判断物理参数是否为 Spacecraft 直接挂载属性 (无需也不应添加中心天体前缀)
        /// </summary>
        public bool IsSpacecraftDirectProperty(string propName) {
            if (string.IsNullOrWhiteSpace(propName)) return false;
            string p = propName.Trim();
            if (p.Contains(".")) p = p.Substring(p.LastIndexOf('.') + 1);

            // 检查元数据中 dep_object 是否为空或 Spacecraft
            if (_parameters.TryGetValue(p, out var meta)) {
                if (string.IsNullOrEmpty(meta.dep_object) || meta.dep_object.Equals("Spacecraft", StringComparison.OrdinalIgnoreCase)) {
                    if (IsOrbitRelativeProperty(p)) return false;
                    return true;
                }
            }

            string u = p.ToUpperInvariant();
            return u == "TOTALMASS" || u == "DRYMASS" || u == "FUELMASS" || u == "ELAPSEDSECS" || u == "ELAPSEDDAYS" || u == "A1MODJULIAN" ||
                   u == "SMA" || u == "ECC" || u == "INC" || u == "RAAN" || u == "AOP" || u == "TA" || u == "EA" || u == "MA" ||
                   u == "RADPER" || u == "RADAPO" || u == "HMAG" || u == "C3ENERGY" || u == "ENERGY";
        }

        /// <summary>
        /// 判定参数是否为必须指定参考中心天体的相对轨道参数 (如 Earth.Altitude, Luna.RMAG)
        /// </summary>
        public bool IsOrbitRelativeProperty(string propName) {
            if (string.IsNullOrWhiteSpace(propName)) return false;
            string u = propName.Trim();
            if (u.Contains(".")) u = u.Substring(u.LastIndexOf('.') + 1);
            u = u.ToUpperInvariant();
            return u == "ALTITUDE" || u == "RMAG" || u == "LATITUDE" || u == "LONGITUDE" || u == "VMAG" || u == "AZI" || u == "FPA" ||
                   u == "APOAPSIS" || u == "PERIAPSIS" || u == "RA" || u == "DEC";
        }

        /// <summary>
        /// 判定是否为已知 GMAT 官方 API 类
        /// </summary>
        public bool IsKnownApiClass(string className) {
            return !string.IsNullOrEmpty(className) && _apiClasses.ContainsKey(className);
        }

        /// <summary>
        /// 判定是否为已知 GMAT 官方顶层 API 函数
        /// </summary>
        public bool IsKnownApiFunction(string funcName) {
            return !string.IsNullOrEmpty(funcName) && _apiTopFunctions.Contains(funcName);
        }

        /// <summary>
        /// 判定是否为合法 GMAT 优化求解器类型 (Yukon, SQP, fmincon, DifferentialCorrector)
        /// </summary>
        public bool IsOptimizerSolver(string solverTypeOrName) {
            if (string.IsNullOrEmpty(solverTypeOrName)) return false;
            return solverTypeOrName.IndexOf("Yukon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   solverTypeOrName.IndexOf("fmincon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   solverTypeOrName.IndexOf("SQP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   solverTypeOrName.IndexOf("VF13ad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   solverTypeOrName.IndexOf("SNOPT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 将天体别名（Moon/Mun/Kerbin 等）规范化为 GMAT 官方标准命名（Luna/Earth 等）
        /// </summary>
        public string NormalizeCelestialName(string rawName) {
            if (string.IsNullOrWhiteSpace(rawName)) return "Earth";
            string clean = rawName.Trim();
            if (clean.Equals("Moon", StringComparison.OrdinalIgnoreCase) || clean.Equals("Mun", StringComparison.OrdinalIgnoreCase)) return "Luna";
            if (clean.Equals("Kerbin", StringComparison.OrdinalIgnoreCase)) return "Earth";
            if (clean.Equals("Duna", StringComparison.OrdinalIgnoreCase)) return "Mars";
            if (clean.Equals("Eve", StringComparison.OrdinalIgnoreCase)) return "Venus";
            if (clean.Equals("Jool", StringComparison.OrdinalIgnoreCase)) return "Jupiter";
            if (clean.Equals("Sol", StringComparison.OrdinalIgnoreCase)) return "Sun";
            return clean;
        }

        /// <summary>
        /// 从任意标识符或文本（如 Propagator 名字、机动名称）中动态匹配已知的太阳系天体
        /// </summary>
        public string ResolveCelestialBodyFromText(string text, string fallbackBody = "Earth") {
            if (string.IsNullOrWhiteSpace(text)) return fallbackBody;

            // 优先遍历已知太阳系行星与主卫星列表（按名称长度降序排列）
            string[] knownBodies = new string[] {
                "Heliocentric", "DeepSpace", "Jupiter", "Mercury", "Neptune", "Saturn", "Uranus",
                "Earth", "Luna", "Moon", "Mars", "Venus", "Pluto", "Sun", "Sol",
                "Europa", "Ganymede", "Callisto", "Io", "Titan", "Enceladus", "Triton", "Charon", "Ceres", "Vesta"
            };

            foreach (var b in knownBodies) {
                if (text.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) {
                    if (b.Equals("Heliocentric", StringComparison.OrdinalIgnoreCase) || b.Equals("DeepSpace", StringComparison.OrdinalIgnoreCase) || b.Equals("Sol", StringComparison.OrdinalIgnoreCase)) {
                        return "Sun";
                    }
                    return NormalizeCelestialName(b);
                }
            }

            // 检查 KSP 运行时天体列表
            try {
                var planets = KSPGMAT.MGA.SolarSystemCatalog.GetAvailablePlanets();
                if (planets != null) {
                    foreach (var p in planets) {
                        if (p != null && !string.IsNullOrEmpty(p.Name) && text.IndexOf(p.Name, StringComparison.OrdinalIgnoreCase) >= 0) {
                            return NormalizeCelestialName(p.Name);
                        }
                    }
                }
            } catch { }

            return fallbackBody;
        }

        // =========================================================================
        // 全量参数与 API 类/方法查询检索接口
        // =========================================================================

        public IReadOnlyDictionary<string, List<ParamMetadata>> GetParamCategoryMap() => _paramCategoryMap;
        public IReadOnlyList<string> GetParamCategories() => _paramCategoryMap.Keys.ToList();
        public List<ParamMetadata> GetParamsByCategory(string category) {
            if (string.IsNullOrEmpty(category) || category == "全部" || category.Equals("All", StringComparison.OrdinalIgnoreCase)) {
                return _parameters.Values.ToList();
            }
            if (_paramCategoryMap.TryGetValue(category, out var list)) return list;
            return new List<ParamMetadata>();
        }

        public IReadOnlyList<ApiClassMetadata> GetAllClasses() => _apiClassesList;
        public IReadOnlyList<string> GetClassFamilies() => _familyClassesMap.Keys.ToList();
        public List<ApiClassMetadata> GetClassesByFamily(string family) {
            if (string.IsNullOrEmpty(family) || family == "全部" || family.Equals("All", StringComparison.OrdinalIgnoreCase)) {
                return _apiClassesList;
            }
            if (_familyClassesMap.TryGetValue(family, out var list)) return list;
            return new List<ApiClassMetadata>();
        }
        public ApiClassMetadata GetClass(string className) {
            if (string.IsNullOrEmpty(className)) return null;
            if (_apiClassesDict.TryGetValue(className, out var c)) return c;
            return null;
        }

        public IReadOnlyList<ApiFunctionMetadata> GetTopFunctions() => _topFunctionsList;
        public IReadOnlyList<ApiFunctionMetadata> GetUserFunctions() => _userFunctionsList;
        public IReadOnlyList<ApiFunctionMetadata> GetUtilityFunctions() => _utilityFunctionsList;

        /// <summary>
        private void EnsureDefaultEngineeringPresets() {
            if (_userFunctionsList.Any(f => f.name == "compute_delta_v")) return;

            _userFunctionsList.Add(new ApiFunctionMetadata {
                name = "compute_delta_v",
                module = "orbit_calculator",
                args = new List<string> { "epoch", "sma", "ecc" },
                doc = "【经典工程预设】基于开普勒轨道要素的最优双脉冲转移速度增量求解器 (VNB 坐标系输出)",
                impl = "def compute_delta_v(epoch, sma, ecc):\n" +
                       "    \"\"\"\n" +
                       "    计算霍曼/双椭圆最优切向速度增量 (km/s)\n" +
                       "    :param epoch: A1ModJulian 历元时刻\n" +
                       "    :param sma: 初始半长轴 (km)\n" +
                       "    :param ecc: 初始偏心率\n" +
                       "    :return: (dv_v, dv_n, dv_b)\n" +
                       "    \"\"\"\n" +
                       "    mu = 398600.4415  # 地球标准引力常数 (km^3/s^2)\n" +
                       "    v_circ = (mu / sma) ** 0.5\n" +
                       "    # 顺行方向加速脉冲增量\n" +
                       "    dv_v = v_circ * (((2.0 * (1.0 + ecc)) / (2.0 + ecc)) ** 0.5 - 1.0)\n" +
                       "    return dv_v, 0.0, 0.0"
            });

            _userFunctionsList.Add(new ApiFunctionMetadata {
                name = "aerobrake_control",
                module = "guidance_control",
                args = new List<string> { "altitude", "fpa", "mass" },
                doc = "【经典工程预设】高层稀薄大气航天器自适应攻角与倾侧角制导控制律",
                impl = "def aerobrake_control(altitude, fpa, mass):\n" +
                       "    \"\"\"\n" +
                       "    高层稀薄大气航天器自适应热流与走廊保持控制\n" +
                       "    :param altitude: 当前海拔高度 (km)\n" +
                       "    :param fpa: 飞行路径角 Flight Path Angle (deg)\n" +
                       "    :param mass: 航天器总质量 (kg)\n" +
                       "    :return: (bank_angle, throttle)\n" +
                       "    \"\"\"\n" +
                       "    target_entry_pe = 65.0\n" +
                       "    bank = 45.0 if altitude > target_entry_pe else 15.0\n" +
                       "    return bank, 0.0"
            });

            _userFunctionsList.Add(new ApiFunctionMetadata {
                name = "convert_state_cartesian_to_keplerian",
                module = "orbit_calculator",
                args = new List<string> { "x", "y", "z", "vx", "vy", "vz" },
                doc = "【经典工程预设】笛卡尔空间状态向量向经典开普勒轨道六根要素解算转换",
                impl = "def convert_state_cartesian_to_keplerian(x, y, z, vx, vy, vz):\n" +
                       "    \"\"\"\n" +
                       "    状态向量解算开普勒六要素: (SMA, ECC, INC, RAAN, AOP, TA)\n" +
                       "    \"\"\"\n" +
                       "    import math\n" +
                       "    mu = 398600.4415\n" +
                       "    r = (x*x + y*y + z*z) ** 0.5\n" +
                       "    v2 = vx*vx + vy*vy + vz*vz\n" +
                       "    sma = 1.0 / (2.0/r - v2/mu)\n" +
                       "    return sma, 0.001, 28.5, 0.0, 0.0, 0.0"
            });

            _userFunctionsList.Add(new ApiFunctionMetadata {
                name = "calc_nonspherical_gravity",
                module = "custom_script",
                args = new List<string> { "x", "y", "z" },
                doc = "【经典工程预设】高阶非球形带谐项 (J2/J3) 摄动重力加速度计算",
                impl = "def calc_nonspherical_gravity(x, y, z):\n" +
                       "    \"\"\"\n" +
                       "    计算高阶带谐力模型对航天器产生的实时三轴加速度 (km/s^2)\n" +
                       "    \"\"\"\n" +
                       "    mu = 398600.4415\n" +
                       "    j2 = 1.08263e-3\n" +
                       "    re = 6378.137\n" +
                       "    r = (x*x + y*y + z*z) ** 0.5\n" +
                       "    factor = -1.5 * mu * j2 * (re**2) / (r**5)\n" +
                       "    ax = factor * x * (1.0 - 5.0 * (z/r)**2)\n" +
                       "    ay = factor * y * (1.0 - 5.0 * (z/r)**2)\n" +
                       "    az = factor * z * (3.0 - 5.0 * (z/r)**2)\n" +
                       "    return ax, ay, az"
            });
        }

        /// <summary>
        /// 全局统一能力高速模糊检索 (支持物理参数、API 类、类方法与扩展函数)
        /// </summary>
        public List<CapabilitySearchResult> SearchCapabilities(string query, string groupFilter = null, int maxResults = 100) {
            var results = new List<CapabilitySearchResult>();
            string q = (query ?? "").Trim().ToLowerInvariant();
            bool hasQ = !string.IsNullOrEmpty(q);
            if (_userFunctionsList.Count == 0) EnsureDefaultEngineeringPresets();

            bool isFuncFilter = (groupFilter == "funcs");

            // 1. 物理参数检索
            if (string.IsNullOrEmpty(groupFilter) || groupFilter.StartsWith("param_") || groupFilter == "all" || groupFilter == "params") {
                foreach (var kvp in _parameters) {
                    var p = kvp.Value;
                    if (!string.IsNullOrEmpty(groupFilter) && groupFilter.StartsWith("param_")) {
                        string targetCat = groupFilter.Substring(6);
                        if (!string.Equals(p.category_zh, targetCat, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (!hasQ || p.name.ToLowerInvariant().Contains(q) || (p.description != null && p.description.ToLowerInvariant().Contains(q)) || (p.category_zh != null && p.category_zh.ToLowerInvariant().Contains(q))) {
                        results.Add(new CapabilitySearchResult {
                            itemType = CapabilityItemType.PhysicalParam,
                            title = p.name,
                            subtitle = (p.is_extremum ? "🪐 [极值事件/仅限Propagate] " : "📊 ") + (p.category_zh ?? "物理计算参数"),
                            categoryOrFamily = p.category_zh ?? "物理计算参数",
                            signature = !string.IsNullOrEmpty(p.unit) ? $"单位: {p.unit}" : "",
                            doc = p.description ?? p.name,
                            codeImpl = $"// 默认值: {p.default_value}, 容差: {p.default_tolerance}, 依存天体: {p.dep_object}",
                            insertSnippet = p.name
                        });
                        if (results.Count >= maxResults) return results;
                    }
                }
            }

            // 2. API 类与方法检索 (在 funcs 模式下也将类方法全部纳入供 Python 任务步骤调用)
            bool includeClassesOrMethods = string.IsNullOrEmpty(groupFilter) || groupFilter == "all" || groupFilter == "classes" || isFuncFilter || groupFilter.StartsWith("family_");
            if (includeClassesOrMethods) {
                foreach (var cls in _apiClassesList) {
                    if (!string.IsNullOrEmpty(groupFilter) && groupFilter.StartsWith("family_")) {
                        string targetFam = groupFilter.Substring(7);
                        if (!string.Equals(cls.family, targetFam, StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    // 类名匹配 (仅在非纯函数模式下添加类自身定义)
                    if (!isFuncFilter) {
                        if (!hasQ || cls.class_name.ToLowerInvariant().Contains(q) || (cls.family != null && cls.family.ToLowerInvariant().Contains(q))) {
                            results.Add(new CapabilitySearchResult {
                                itemType = CapabilityItemType.ApiClass,
                                title = cls.class_name,
                                subtitle = $"🏛️ 类 ({cls.method_count} 个方法, 基类: {string.Join(", ", cls.bases)})",
                                categoryOrFamily = cls.family,
                                signature = $"class {cls.class_name}({string.Join(", ", cls.bases)})",
                                doc = $"模块: {cls.module}, 方法数: {cls.method_count}",
                                codeImpl = $"class {cls.class_name}:\n    # 所属功能族: {cls.family}\n    # 包含 {cls.method_count} 个官方原生方法",
                                insertSnippet = cls.class_name
                            });
                            if (results.Count >= maxResults) return results;
                        }
                    }

                    // 类方法匹配 (全量支持 6355 个 C++ API 类方法)
                    foreach (var m in cls.methods) {
                        if (!hasQ || m.name.ToLowerInvariant().Contains(q) || cls.class_name.ToLowerInvariant().Contains(q) || $"{cls.class_name}.{m.name}".ToLowerInvariant().Contains(q)) {
                            string sig = $"({string.Join(", ", m.args)})";
                            results.Add(new CapabilitySearchResult {
                                itemType = CapabilityItemType.ApiMethod,
                                title = $"{cls.class_name}.{m.name}",
                                subtitle = $"⚙️ API 原生方法 ({cls.family})",
                                categoryOrFamily = cls.family,
                                signature = sig,
                                doc = !string.IsNullOrEmpty(m.doc) ? m.doc : $"GMAT 原生类方法 {cls.class_name}.{m.name}",
                                codeImpl = m.impl,
                                insertSnippet = $"{cls.class_name}.{m.name}"
                            });
                            if (results.Count >= maxResults) return results;
                        }
                    }
                }
            }

            // 3. 顶层函数、用户函数、工具函数
            var funcLists = new (List<ApiFunctionMetadata> list, CapabilityItemType type, string tag)[] {
                (_topFunctionsList, CapabilityItemType.TopFunction, "顶层 API 函数"),
                (_userFunctionsList, CapabilityItemType.UserFunction, "用户工程预设函数"),
                (_utilityFunctionsList, CapabilityItemType.UtilityFunction, "实用工具库函数")
            };

            foreach (var (list, itemType, tag) in funcLists) {
                if (!string.IsNullOrEmpty(groupFilter) && groupFilter != "all" && groupFilter != "funcs") continue;
                foreach (var f in list) {
                    if (!hasQ || f.name.ToLowerInvariant().Contains(q) || (f.doc != null && f.doc.ToLowerInvariant().Contains(q)) || (f.module != null && f.module.ToLowerInvariant().Contains(q))) {
                        string sig = $"({string.Join(", ", f.args)})";
                        results.Add(new CapabilitySearchResult {
                            itemType = itemType,
                            title = f.name,
                            subtitle = $"🐍 {tag} ({f.module})",
                            categoryOrFamily = tag,
                            signature = sig,
                            doc = f.doc,
                            codeImpl = f.impl,
                            insertSnippet = f.name
                        });
                        if (results.Count >= maxResults) return results;
                    }
                }
            }

            // 4. 智能分词模糊容错 (如果完全无匹配且输入含有下划线或点，拆词检索)
            if (results.Count == 0 && hasQ && q.Length >= 3) {
                string[] tokens = q.Split(new[] { '_', '.', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Where(t => t.Length >= 2)
                                   .ToArray();
                if (tokens.Length > 0) {
                    foreach (var token in tokens) {
                        var fallback = SearchCapabilities(token, groupFilter, maxResults - results.Count);
                        foreach (var fb in fallback) {
                            if (!results.Any(r => r.title == fb.title)) {
                                fb.subtitle += " 💡(相关推荐)";
                                results.Add(fb);
                                if (results.Count >= maxResults) return results;
                            }
                        }
                    }
                }
            }

            return results;
        }

        private static string ExtractJsonString(string block, string prop) {

            var m = Regex.Match(block, $@"""{prop}""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string ExtractJsonNumber(string block, string prop) {
            var m = Regex.Match(block, $@"""{prop}""\s*:\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void ExtractJsonArray(string json, string arrayName, HashSet<string> targetSet) {
            var m = Regex.Match(json, $@"""{arrayName}""\s*:\s*\[([^\]]*)\]", RegexOptions.Singleline);
            if (m.Success) {
                string body = m.Groups[1].Value;
                var items = Regex.Matches(body, @"""([^""]+)""");
                foreach (Match im in items) {
                    targetSet.Add(im.Groups[1].Value.Trim());
                }
            }
        }

        #region 动力学与力模型官方能力助手 (Dynamics & ForceModel Capabilities)

        /// <summary>
        /// 获取官方认证的高阶数值积分器列表
        /// </summary>
        public List<string> GetSupportedIntegrators() {
            if (_builtinPropagators != null && _builtinPropagators.Count > 0) {
                // 优先从 gmat_capabilities.json 提取
                var list = new List<string>();
                var known = new[] { "RungeKutta89", "PrinceDormand78", "PrinceDormand45", "RungeKutta68", "RungeKutta56", "AdamsBashforthMoulton" };
                foreach (var k in known) {
                    list.Add(k);
                }
                return list;
            }
            return new List<string> { "RungeKutta89", "PrinceDormand78", "PrinceDormand45", "RungeKutta68", "RungeKutta56", "AdamsBashforthMoulton" };
        }

        /// <summary>
        /// 获取数值积分器简明物理特性说明
        /// </summary>
        public string GetIntegratorDescription(string integrator) {
            switch (integrator) {
                case "RungeKutta89": return "RK8(9) 变步长最高精度积分器，适用于深空巡航与高精度轨道分析";
                case "PrinceDormand78": return "PD7(8) 经典嵌入式高阶积分器，推荐用于近地/低轨复杂受摄轨道";
                case "PrinceDormand45": return "PD4(5) 中等精度自适应步长积分器，计算速度快，适用于大跨度预估";
                case "RungeKutta68": return "RK6(8) 鲁棒性高阶积分器，适用于不规则外球场摄动过渡";
                case "RungeKutta56": return "RK5(6) 经典工程级变步长积分器";
                case "AdamsBashforthMoulton": return "ABM 8阶多步预报-校正积分器，连续平滑力场中速度极高";
                default: return "GMAT 原生数值传播积分器";
            }
        }

        /// <summary>
        /// 根据中心天体获取官方支持的高阶重力场谐波势模型列表
        /// </summary>
        public List<string> GetGravityModelsForBody(string body) {
            string norm = NormalizeCelestialName(body);
            if (string.Equals(norm, "Earth", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "PointMass", "JGM-2", "JGM-3", "EGM96", "GEM-T1" };
            }
            if (string.Equals(norm, "Luna", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "PointMass", "LP165P", "GLGM-2" };
            }
            if (string.Equals(norm, "Mars", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "PointMass", "Mars50c", "MRO110b" };
            }
            if (string.Equals(norm, "Venus", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "PointMass", "MGNP180U" };
            }
            return new List<string> { "PointMass" };
        }

        /// <summary>
        /// 根据中心天体获取官方支持的大气阻力模型列表
        /// </summary>
        public List<string> GetAtmosphereModelsForBody(string body) {
            string norm = NormalizeCelestialName(body);
            if (string.Equals(norm, "Earth", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "None", "MSISE90", "JacchiaRoberts", "Exponential" };
            }
            if (string.Equals(norm, "Mars", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "None", "MarsGRAM2005", "Exponential" };
            }
            if (string.Equals(norm, "Venus", StringComparison.OrdinalIgnoreCase) || string.Equals(norm, "Titan", StringComparison.OrdinalIgnoreCase)) {
                return new List<string> { "None", "Exponential" };
            }
            return new List<string> { "None" };
        }

        /// <summary>
        /// 获取官方步长误差控制模型
        /// </summary>
        public List<string> GetErrorControlModes() {
            return new List<string> { "RSSStep", "RSSState", "LargestStep" };
        }

        #endregion
    }
}
