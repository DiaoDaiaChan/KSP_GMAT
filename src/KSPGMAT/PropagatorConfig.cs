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
        public bool gravityEarth = true;
        public bool gravityLuna = true;
        public bool gravitySun = true;
        public bool gravityJupiter = true;
        public bool gravityVenus = false;
        public bool gravityMars = false;
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

        public PropagatorConfig Clone() {
            return (PropagatorConfig)this.MemberwiseClone();
        }

        public static Dictionary<string, PropagatorConfig> CreateDefaultPresets() {
            var dict = new Dictionary<string, PropagatorConfig>();

            // 1. LEOProp: Near-Earth 200km - 1000km orbit integrator
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
                dragModel = "None"
            };
            dict["LEOProp"] = leo;

            // 2. CislunarProp: High-efficiency Cislunar transfer cruise integrator
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
                dragModel = "None"
            };
            dict["CislunarProp"] = cisl;

            // 3. LunarNRHOProp: High-precision Lunar NRHO halo orbit integrator
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
                dragModel = "None"
            };
            dict["LunarNRHOProp"] = nrho;

            return dict;
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
            sb.Append($"\"error_threshold\": {errorThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"smallest_interval\": {smallestInterval.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"lower_error\": {lowerError.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"target_error\": {targetError.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"step_size\": {stepSize.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"minimum_reduction\": {minimumReduction.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"maximum_reduction\": {maximumReduction.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"minimum_tolerance\": {minimumTolerance.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"central_body\": \"{centralBody}\",");
            sb.Append($"\"error_control\": \"{errorControl}\",");
            sb.Append($"\"gravity_model\": \"{gravityModel}\",");
            sb.Append($"\"gravity_degree\": {gravityDegree},");
            sb.Append($"\"gravity_order\": {gravityOrder},");
            sb.Append($"\"gravity_stm_limit\": {gravityStmLimit},");
            sb.Append($"\"gravity_potential_file\": \"{gravityPotentialFile}\",");
            sb.Append($"\"tide_model\": \"{tideModel}\",");
            sb.Append($"\"tide_potential_file\": \"{tidePotentialFile}\",");
            sb.Append($"\"drag_model\": \"{dragModel}\",");
            sb.Append($"\"drag_atmosphere_model\": \"{dragAtmosphereModel}\",");
            sb.Append($"\"drag_f107\": {dragF107.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"drag_f107a\": {dragF107A.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"drag_magnetic_index\": {dragMagneticIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append("\"point_masses\": {");
            sb.Append($"\"Earth\": {(gravityEarth ? "true" : "false")},");
            sb.Append($"\"Luna\": {(gravityLuna ? "true" : "false")},");
            sb.Append($"\"Sun\": {(gravitySun ? "true" : "false")},");
            sb.Append($"\"Jupiter\": {(gravityJupiter ? "true" : "false")},");
            sb.Append($"\"Venus\": {(gravityVenus ? "true" : "false")},");
            sb.Append($"\"Mars\": {(gravityMars ? "true" : "false")}");
            sb.Append("}");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
