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
        public string errorControl = "RSSStep";

        // Force Model & Perturbations
        public string centralBody = "Earth";
        public bool gravityEarth = true;
        public bool gravityLuna = true;
        public bool gravitySun = true;
        public bool gravityJupiter = true;
        public bool gravityVenus = false;
        public bool gravityMars = false;
        public bool enableSRP = true;
        public double srpFlux = 1367.0;
        public string dragModel = "None"; // None, Exponential, MSISE90
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
                enableSRP = true,
                dragModel = "None",
                enableRelativity = false
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
                enableSRP = true,
                dragModel = "None",
                enableRelativity = false
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
                enableSRP = true,
                dragModel = "None",
                enableRelativity = false
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
            sb.Append($"\"central_body\": \"{centralBody}\",");
            sb.Append($"\"enable_srp\": {(enableSRP ? "true" : "false")},");
            sb.Append($"\"srp_flux\": {srpFlux.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"drag_model\": \"{dragModel}\",");
            sb.Append($"\"enable_relativity\": {(enableRelativity ? "true" : "false")},");
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
