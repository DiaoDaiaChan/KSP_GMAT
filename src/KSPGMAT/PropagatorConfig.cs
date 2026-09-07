using System;
using System.Collections.Generic;

namespace KSPGMAT {
    [Serializable]
    public class PropagatorConfig {
        public string name = "LEOProp";
        public string integratorType = "RungeKutta89";
        public double initialStepSize = 60.0;
        public double accuracy = 1e-11;
        public double minStep = 0.001;
        public double maxStep = 2700.0;
        public int maxStepAttempts = 50;
        public bool stopIfAccuracyIsViolated = false;
        public bool isDefault = false;
        public double errorThreshold = 0.0;
        public double smallestInterval = 0.0;
        public double lowerError = 0.0;
        public double targetError = 0.0;
        public double stepSize = 0.0;
        public double minimumReduction = 0.0;
        public double maximumReduction = 0.0;
        public double minimumTolerance = 0.0;

        // ForceModel properties exposed by GMAT.
        public string centralBody = "Earth";
        public string errorControl = "RSSStep";
        public string gravityModel = "";
        public int gravityDegree = 0;
        public int gravityOrder = 0;
        public int gravityStmLimit = 0;
        public string gravityPotentialFile = "";
        public string tideModel = "";
        public string tidePotentialFile = "";
        public string dragModel = "None";
        public string dragAtmosphereModel = "";
        public double dragF107 = 0.0;
        public double dragF107A = 0.0;
        public double dragMagneticIndex = 0.0;
        public bool enableSRP = false;
        public double srpFlux = 1367.0;
        public bool enableRelativity = false;

        /// <summary>
        /// 点质量多体引力摄动天体动态集合 (Single Source of Truth，支持任意太阳系、外星系与第三方星球包天体)
        /// </summary>
        public List<string> pointMasses = new List<string> { "Earth", "Luna", "Sun", "Jupiter" };

        /// <summary>
        /// 向后兼容字段别名
        /// </summary>
        public List<string> customPointMasses {
            get => pointMasses;
            set => pointMasses = value ?? new List<string>();
        }

        /// <summary>
        /// 检查当前力模型是否包含指定天体的引力摄动 (大小写不敏感，自动标准化天体别名)
        /// </summary>
        public bool HasPointMass(string bodyName) {
            if (string.IsNullOrWhiteSpace(bodyName) || pointMasses == null) return false;
            string norm = Model.GMATSchemaRegistry.Instance.NormalizeCelestialName(bodyName);
            return pointMasses.Exists(b => string.Equals(Model.GMATSchemaRegistry.Instance.NormalizeCelestialName(b), norm, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 动态开启或关闭指定天体的引力摄动
        /// </summary>
        public void SetPointMass(string bodyName, bool enable) {
            if (string.IsNullOrWhiteSpace(bodyName)) return;
            if (pointMasses == null) pointMasses = new List<string>();
            string norm = Model.GMATSchemaRegistry.Instance.NormalizeCelestialName(bodyName);
            if (enable) {
                if (!HasPointMass(norm)) pointMasses.Add(norm);
            } else {
                pointMasses.RemoveAll(b => string.Equals(Model.GMATSchemaRegistry.Instance.NormalizeCelestialName(b), norm, StringComparison.OrdinalIgnoreCase));
            }
        }

        #region 向后兼容属性代理 (透明重定向至动态 pointMasses 集合)
        public bool gravityEarth { get => HasPointMass("Earth"); set => SetPointMass("Earth", value); }
        public bool gravityLuna { get => HasPointMass("Luna"); set => SetPointMass("Luna", value); }
        public bool gravitySun { get => HasPointMass("Sun"); set => SetPointMass("Sun", value); }
        public bool gravityJupiter { get => HasPointMass("Jupiter"); set => SetPointMass("Jupiter", value); }
        public bool gravityVenus { get => HasPointMass("Venus"); set => SetPointMass("Venus", value); }
        public bool gravityMars { get => HasPointMass("Mars"); set => SetPointMass("Mars", value); }
        #endregion

        /// <summary>
        /// 动态获取当前激活的所有点质量引力天体列表 (全动态解耦，杜绝硬编码天体白名单)
        /// 自动规范化天体命名并严格进行不区分大小写去重
        /// </summary>
        public List<string> GetActivePointMasses() {
            var result = new List<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (pointMasses != null) {
                for (int i = 0; i < pointMasses.Count; i++) {
                    string b = pointMasses[i];
                    if (!string.IsNullOrWhiteSpace(b)) {
                        string norm = Model.GMATSchemaRegistry.Instance.NormalizeCelestialName(b.Trim());
                        if (set.Add(norm)) {
                            result.Add(norm);
                        }
                    }
                }
            }
            return result;
        }

        public PropagatorConfig Clone() {
            var copy = (PropagatorConfig)this.MemberwiseClone();
            copy.pointMasses = new List<string>(this.pointMasses ?? new List<string>());
            return copy;
        }

        /// <summary>
        /// 生成默认传播子预设。
        /// 基线 7 说明: 此处为引导期兜底预设 (RungeKutta89 为 GMAT 原生积分器)，
        /// 权威积分器/大气模型清单以 gmat_capabilities.json 动态加载为准 (见 GMATParameterRegistry.AvailableIntegrators)。
        /// </summary>
        public static Dictionary<string, PropagatorConfig> CreateDefaultPresets() {
            var dict = new Dictionary<string, PropagatorConfig>();

            // 1. LEOProp: 近地轨道精密力模型 (JGM-2 4x4 + 多体引力 + MSISE90 + SRP)
            var leo = new PropagatorConfig {
                name = "LEOProp",
                integratorType = "RungeKutta89",
                initialStepSize = 30.0,
                accuracy = 1e-11,
                minStep = 0.001,
                maxStep = 2700.0,
                centralBody = "Earth",
                gravityEarth = true,
                gravityLuna = true,
                gravitySun = true,
                gravityJupiter = true,
                gravityModel = "JGM-2",
                gravityDegree = 4,
                gravityOrder = 4,
                dragModel = "MSISE90",
                dragAtmosphereModel = "MSISE90",
                enableSRP = true,
                enableRelativity = false,
                errorControl = "RSSStep"
            };
            dict["LEOProp"] = leo;

            // 2. CislunarProp: 地月大椭圆与跨月巡航高效积分器 (Earth+Luna+Sun 三体 + SRP)
            var cisl = new PropagatorConfig {
                name = "CislunarProp",
                integratorType = "RungeKutta89",
                initialStepSize = 60.0,
                accuracy = 1e-11,
                minStep = 0.001,
                maxStep = 21600.0, // 6 hours
                centralBody = "Earth",
                gravityEarth = true,
                gravityLuna = true,
                gravitySun = true,
                gravityJupiter = true,
                gravityModel = "PointMass",
                gravityDegree = 0,
                gravityOrder = 0,
                dragModel = "None",
                enableSRP = true,
                enableRelativity = false,
                errorControl = "RSSStep"
            };
            dict["CislunarProp"] = cisl;

            // 3. LunarNRHOProp: 月球近直线晕轨道/低月轨高阶力模型 (Luna LP165P 8x8 + 地日潮汐 + SRP)
            var nrho = new PropagatorConfig {
                name = "LunarNRHOProp",
                integratorType = "RungeKutta89",
                initialStepSize = 60.0,
                accuracy = 1e-11,
                minStep = 0.001,
                maxStep = 10800.0, // 3 hours
                centralBody = "Luna",
                gravityEarth = true,
                gravityLuna = true,
                gravitySun = true,
                gravityJupiter = true,
                gravityModel = "LP165P",
                gravityDegree = 8,
                gravityOrder = 8,
                dragModel = "None",
                enableSRP = true,
                enableRelativity = false,
                errorControl = "RSSStep"
            };
            dict["LunarNRHOProp"] = nrho;

            // 4. DeepSpaceProp: 太阳系深空行星际巡航动力学 (Sun 主引力 + 行星摄动 + 广义相对论后牛顿修正 + SRP)
            var dsp = new PropagatorConfig {
                name = "DeepSpaceProp",
                integratorType = "PrinceDormand78",
                initialStepSize = 300.0,
                accuracy = 1e-11,
                minStep = 0.01,
                maxStep = 86400.0, // 1 day
                centralBody = "Sun",
                gravityEarth = true,
                gravityLuna = true,
                gravitySun = true,
                gravityJupiter = true,
                gravityMars = true,
                gravityVenus = true,
                gravityModel = "PointMass",
                gravityDegree = 0,
                gravityOrder = 0,
                dragModel = "None",
                enableSRP = true,
                enableRelativity = true,
                errorControl = "RSSStep"
            };
            dict["DeepSpaceProp"] = dsp;

            return dict;
        }

        /// <summary>
        /// 一键应用官方动力学场景预设
        /// </summary>
        public void ApplyPreset(string presetKey) {
            var presets = CreateDefaultPresets();
            if (presets.TryGetValue(presetKey, out var p)) {
                this.integratorType = p.integratorType;
                this.initialStepSize = p.initialStepSize;
                this.accuracy = p.accuracy;
                this.minStep = p.minStep;
                this.maxStep = p.maxStep;
                this.centralBody = p.centralBody;
                this.gravityEarth = p.gravityEarth;
                this.gravityLuna = p.gravityLuna;
                this.gravitySun = p.gravitySun;
                this.gravityJupiter = p.gravityJupiter;
                this.gravityVenus = p.gravityVenus;
                this.gravityMars = p.gravityMars;
                this.gravityModel = p.gravityModel;
                this.gravityDegree = p.gravityDegree;
                this.gravityOrder = p.gravityOrder;
                this.dragModel = p.dragModel;
                this.dragAtmosphereModel = p.dragAtmosphereModel;
                this.enableSRP = p.enableSRP;
                this.enableRelativity = p.enableRelativity;
                this.errorControl = p.errorControl;
            }
        }

        public string ToJson() {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\": \"{name}\",");
            sb.Append($"\"integrator_type\": \"{integratorType}\",");
            sb.Append($"\"initial_step\": {initialStepSize.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"accuracy\": {accuracy.ToString("E2", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"min_step\": {minStep.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"max_step\": {maxStep.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"max_step_attempts\": {maxStepAttempts},");
            sb.Append($"\"stop_if_accuracy_is_violated\": {(stopIfAccuracyIsViolated ? "true" : "false")},");
            sb.Append($"\"central_body\": \"{centralBody}\",");
            sb.Append($"\"error_control\": \"{errorControl}\",");
            sb.Append($"\"gravity_model\": \"{gravityModel}\",");
            sb.Append($"\"gravity_degree\": {gravityDegree},");
            sb.Append($"\"gravity_order\": {gravityOrder},");
            sb.Append($"\"drag_model\": \"{dragModel}\",");
            sb.Append($"\"drag_atmosphere_model\": \"{dragAtmosphereModel}\",");
            sb.Append($"\"enable_srp\": {(enableSRP ? "true" : "false")},");
            sb.Append($"\"enable_relativity\": {(enableRelativity ? "true" : "false")},");
            sb.Append($"\"point_masses\": [\"{string.Join("\", \"", GetActivePointMasses().ToArray())}\"]");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
