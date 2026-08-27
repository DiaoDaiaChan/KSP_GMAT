using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

namespace KSPGMAT {
    [Serializable]
    public class MissionStep {
        public string type = "Propagate"; // Propagate, ImpulsiveBurn, FiniteBurn, TargetApoapsis, Targeter, Equation
        public string propagator = "LEOProp"; // LEOProp, CislunarProp, LunarNRHOProp, DefaultProp
        public string stop_type = "Time"; // Time, Days, Apoapsis, Periapsis, Altitude, RMAG, TA, Node
        public double duration = 3600.0;
        public double target_val = 200.0; // Altitude (km), RMAG (km), TrueAnomaly (deg)
        public double target_alt = 35786.0; // km for TargetApoapsis
        public string target_goal = "Apoapsis"; // Apoapsis, Periapsis, SMA, ECC, INC, RMAG, LunarPerilune
        public double dv_v = 0.0; // m/s
        public double dv_n = 0.0; // m/s
        public double dv_b = 0.0; // m/s
        public double epoch_offset = 0.0;
        public string variable = ""; // for Equation, e.g. "EarthMoonRot.X"
        public string expression = ""; // for Equation, e.g. "-173.0"
        public string label = "";
    }

    [Serializable]
    public class SolvedBurn {
        public string type;
        public string name;
        public string origin;
        public double dv_v;
        public double dv_n;
        public double dv_b;
        public double magnitude;
        public double duration;
        public double thrust_kn;
        public double isp;
        public double fuel_burned;
        public double epoch_offset;
    }

    [Serializable]
    public class TrajectoryPoint {
        public double t;
        public double x;
        public double y;
        public double z;
        public double vx;
        public double vy;
        public double vz;
        public double mass;
    }

    [Serializable]
    public class MissionResult {
        public bool success;
        public string error;
        public string script_path;
        public double final_sma;
        public double final_ecc;
        public double final_inc;
        public double final_raan;
        public double final_aop;
        public double final_ta;
        public double final_mass;
        public string final_epoch;
        public List<SolvedBurn> solved_burns = new List<SolvedBurn>();
        public List<TrajectoryPoint> trajectory = new List<TrajectoryPoint>();
        public List<MissionStep> updated_sequence = new List<MissionStep>();
        public string render_image_path;
    }

    public static class GMATClient {
        public static string ServerUrl = "http://127.0.0.1:8055";
        public static bool IsConnected = false;
        public static string ServerStatusText = "Connecting...";

        public static void CheckConnection(Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ServerUrl + "/api/status");
                    request.Timeout = 2000;
                    request.Method = "GET";
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream())) {
                        string json = reader.ReadToEnd();
                        IsConnected = true;
                        ServerStatusText = "Online (GMAT R2026a)";
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, json));
                    }
                } catch (Exception ex) {
                    IsConnected = false;
                    ServerStatusText = "Offline (" + ex.Message + ")";
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, ex.Message));
                }
            });
        }

        public static void CalibrateEphem(double ut, Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    string jsonPayload = $"{{\"ut\": {ut.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}}}";
                    string res = PostJson(ServerUrl + "/api/calibrate_ephem", jsonPayload);
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, res));
                } catch (Exception ex) {
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, ex.Message));
                }
            });
        }

        public static void PropagateMission(Vessel vessel, StageData stage, ROEngineInfo engine, List<MissionStep> steps, Action<bool, MissionResult> callback) {
            List<StageData> list = stage != null ? new List<StageData> { stage } : null;
            PropagateMission(vessel, list, engine, steps, null, callback);
        }

        public static void PropagateMission(Vessel vessel, List<StageData> selectedStages, ROEngineInfo engine, List<MissionStep> steps, Action<bool, MissionResult> callback) {
            PropagateMission(vessel, selectedStages, engine, steps, null, callback);
        }

        public static void PropagateMission(Vessel vessel, List<StageData> selectedStages, ROEngineInfo engine, List<MissionStep> steps, Dictionary<string, PropagatorConfig> propagatorConfigs, Action<bool, MissionResult> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    Vector3d pos_km, vel_kms;
                    PrincipiaInterop.GetVesselStateICRF(vessel, out pos_km, out vel_kms);

                    double ut = Planetarium.GetUniversalTime();

                    // Multi-stage mass calculation
                    double dryMassKg = 0;
                    double fuelMassKg = 0;
                    double defaultThrust = 50000.0;
                    double defaultIsp = 310.0;

                    if (selectedStages != null && selectedStages.Count > 0) {
                        double minDryTonnes = double.MaxValue;
                        double sumFuelTonnes = 0.0;
                        foreach (var st in selectedStages) {
                            sumFuelTonnes += st.fuelMassTonnes;
                            if (st.dryMassTonnes < minDryTonnes) minDryTonnes = st.dryMassTonnes;
                            if (st.thrustKN > 0) defaultThrust = st.thrustKN * 1000.0;
                            if (st.isp > 0) defaultIsp = st.isp;
                        }
                        dryMassKg = minDryTonnes * 1000.0;
                        fuelMassKg = sumFuelTonnes * 1000.0;
                    } else {
                        dryMassKg = Math.Max(1000.0, vessel.totalMass * 400.0);
                        fuelMassKg = Math.Max(1000.0, vessel.totalMass * 600.0);
                    }

                    if (engine != null && engine.fuelMassKg > 0) {
                        fuelMassKg = Math.Max(fuelMassKg, engine.fuelMassKg);
                    }
                    double fuelDensity = engine != null && engine.fuelDensityKgM3 > 0 ? engine.fuelDensityKgM3 : 1260.0;
                    double tankVolume = engine != null && engine.tankVolumeM3 > 0 ? engine.tankVolumeM3 : (fuelMassKg / fuelDensity * 1.1);
                    double tankPressure = engine != null ? engine.tankPressureKPa : 1500.0;
                    double thrustN = engine != null && engine.thrustKN > 0 ? engine.thrustKN * 1000.0 : defaultThrust;
                    double ispS = engine != null && engine.ispVac > 0 ? engine.ispVac : defaultIsp;
                    string engName = engine != null ? engine.title : "MainEngine";

                    // ======================= DETAILED DEBUG LOGGING =======================
                    KSPGMATLog.Section("GMAT MISSION PROPAGATION STARTED");
                    KSPGMATLog.Info($"Vessel Name: {vessel.vesselName} (ID: {vessel.id}), CentralBody: {vessel.mainBody.name}, UT: {ut:F3} s");
                    KSPGMATLog.Info($"ICRF Position: [{pos_km.x:F3}, {pos_km.y:F3}, {pos_km.z:F3}] km (|r| = {pos_km.magnitude:F1} km)");
                    KSPGMATLog.Info($"ICRF Velocity: [{vel_kms.x:F6}, {vel_kms.y:F6}, {vel_kms.z:F6}] km/s (|v| = {vel_kms.magnitude:F3} km/s)");
                    if (vessel.orbit != null) {
                        KSPGMATLog.Info($"Orbit: SMA={vessel.orbit.semiMajorAxis / 1000.0:F1}km, ECC={vessel.orbit.eccentricity:F5}, INC={vessel.orbit.inclination:F3}°, RAAN={vessel.orbit.LAN:F3}°, PeA={vessel.orbit.PeA / 1000.0:F1}km, ApA={vessel.orbit.ApA / 1000.0:F1}km");
                    }
                    KSPGMATLog.Info($"Mass & Engine: DryMass={dryMassKg:F1}kg, FuelMass={fuelMassKg:F1}kg, WetMass={(dryMassKg+fuelMassKg):F1}kg, Thrust={thrustN/1000.0:F1}kN, Isp={ispS:F1}s, Propellant={engine?.propellantDesc ?? "Default"}");
                    if (selectedStages != null && selectedStages.Count > 0) {
                        List<string> sList = new List<string>();
                        foreach (var st in selectedStages) sList.Add($"S{st.stageNumber}(Δv={st.deltaV:F0}m/s, wet={st.wetMassTonnes:F2}t)");
                        KSPGMATLog.Info($"Selected Stages ({selectedStages.Count}): " + string.Join(", ", sList.ToArray()));
                    }
                    KSPGMATLog.Info($"Mission Sequence ({steps.Count} steps):");
                    for (int sIdx = 0; sIdx < steps.Count; sIdx++) {
                        var st = steps[sIdx];
                        if (st.type == "Propagate") {
                            KSPGMATLog.Info($"  [Step {sIdx+1}] Propagate '{st.label}' ({st.propagator}) -> Stop: {st.stop_type}, Duration: {st.duration:F1}s, TargetVal: {st.target_val:F2}");
                        } else if (st.type == "ImpulsiveBurn") {
                            KSPGMATLog.Info($"  [Step {sIdx+1}] ImpulsiveBurn '{st.label}' -> ΔV=({st.dv_v:F1}, {st.dv_n:F1}, {st.dv_b:F1}) m/s, Mag={Math.Sqrt(st.dv_v*st.dv_v + st.dv_n*st.dv_n + st.dv_b*st.dv_b):F1} m/s");
                        } else if (st.type == "FiniteBurn") {
                            KSPGMATLog.Info($"  [Step {sIdx+1}] FiniteBurn '{st.label}' -> Duration: {st.duration:F1}s");
                        } else if (st.type == "Equation") {
                            KSPGMATLog.Info($"  [Step {sIdx+1}] Equation -> Sat.{st.variable} = {st.expression}");
                        } else {
                            KSPGMATLog.Info($"  [Step {sIdx+1}] {st.type} '{st.label}'");
                        }
                    }
                    if (propagatorConfigs != null && propagatorConfigs.Count > 0) {
                        KSPGMATLog.Info($"Custom Propagators ({propagatorConfigs.Count}):");
                        foreach (var kv in propagatorConfigs) {
                            var p = kv.Value;
                            KSPGMATLog.Info($"  - {kv.Key}: Type={p.integratorType}, Step={p.initialStepSize}s, MaxStep={p.maxStep}s, Center={p.centralBody}, SRP={p.enableSRP}");
                        }
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.Append("{");
                    sb.Append("\"craft\": {");
                    sb.Append($"\"name\": \"{Sanitize(vessel.vesselName)}\",");
                    sb.Append($"\"central_body\": \"{vessel.mainBody.name}\",");
                    sb.Append($"\"ut\": {ut.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"x\": {pos_km.x.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"y\": {pos_km.y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"z\": {pos_km.z.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"vx\": {vel_kms.x.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"vy\": {vel_kms.y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"vz\": {vel_kms.z.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"dry_mass\": {dryMassKg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"fuel_mass\": {fuelMassKg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"fuel_density\": {fuelDensity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"tank_volume\": {tankVolume.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"tank_pressure\": {tankPressure.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"engine_name\": \"{Sanitize(engName)}\",");
                    sb.Append($"\"thrust\": {thrustN.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"isp\": {ispS.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                    sb.Append("},");

                    // Steps array
                    sb.Append("\"sequence\": [");
                    for (int i = 0; i < steps.Count; i++) {
                        var st = steps[i];
                        if (i > 0) sb.Append(",");
                        sb.Append("{");
                        sb.Append($"\"type\": \"{st.type}\",");
                        sb.Append($"\"propagator\": \"{st.propagator}\",");
                        sb.Append($"\"stop_type\": \"{st.stop_type}\",");
                        sb.Append($"\"duration\": {st.duration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"target_val\": {st.target_val.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"target_alt\": {st.target_alt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"target_goal\": \"{st.target_goal ?? "Apoapsis"}\",");
                        sb.Append($"\"dv_v\": {st.dv_v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_n\": {st.dv_n.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_b\": {st.dv_b.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"variable\": \"{st.variable}\",");
                        sb.Append($"\"expression\": \"{st.expression}\",");
                        sb.Append($"\"label\": \"{st.label}\",");
                        sb.Append($"\"epoch_offset\": {st.epoch_offset.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                        sb.Append("}");
                    }
                    sb.Append("]");

                    if (propagatorConfigs != null && propagatorConfigs.Count > 0) {
                        sb.Append(",\"propagator_configs\": {");
                        bool first = true;
                        foreach (var kv in propagatorConfigs) {
                            if (!first) sb.Append(",");
                            sb.Append($"\"{kv.Key}\": {kv.Value.ToJson()}");
                            first = false;
                        }
                        sb.Append("}");
                    }

                    sb.Append("}");

                    string responseJson = PostJson(ServerUrl + "/api/propagate", sb.ToString());
                    sw.Stop();

                    MissionResult result = ParseMissionResult(responseJson);

                    if (result.success) {
                        KSPGMATLog.Success($"GMAT execution succeeded in {sw.ElapsedMilliseconds} ms!");
                        KSPGMATLog.Success($"Script Location: {result.script_path}");
                        KSPGMATLog.Success($"Final Orbit State: SMA={result.final_sma:F2}km, ECC={result.final_ecc:F5}, INC={result.final_inc:F2}°, RAAN={result.final_raan:F2}°, Epoch={result.final_epoch}, Mass={result.final_mass:F1}kg");
                        KSPGMATLog.Success($"Trajectory Points: {result.trajectory.Count}");
                        KSPGMATLog.Success($"Solved Burns ({result.solved_burns.Count}):");
                        for (int b = 0; b < result.solved_burns.Count; b++) {
                            var bn = result.solved_burns[b];
                            KSPGMATLog.Success($"  [Burn {b+1}] {bn.name} ({bn.type}) on {bn.origin}: t=+{bn.epoch_offset:F1}s (+{bn.epoch_offset/86400.0:F2}d), ΔV=({bn.dv_v:F1}, {bn.dv_n:F1}, {bn.dv_b:F1}) m/s, Mag={bn.magnitude:F1} m/s");
                        }
                        if (result.updated_sequence != null && result.updated_sequence.Count > 0) {
                            KSPGMATLog.Info($"Targeter Updated Values reflected in {result.updated_sequence.Count} steps.");
                        }
                        if (!string.IsNullOrEmpty(result.render_image_path)) {
                            KSPGMATLog.Info($"Simulation Render Image: {result.render_image_path}");
                        }
                    } else {
                        KSPGMATLog.Error($"GMAT execution failed in {sw.ElapsedMilliseconds} ms: {result.error}");
                    }
                    KSPGMATLog.Section("GMAT MISSION PROPAGATION COMPLETED");

                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(result.success, result));
                } catch (Exception ex) {
                    sw.Stop();
                    KSPGMATLog.Error($"GMAT PropagateMission Exception in {sw.ElapsedMilliseconds} ms: {ex}");
                    KSPGMATLog.Section("GMAT MISSION PROPAGATION FAILED");
                    MissionResult err = new MissionResult();
                    err.success = false;
                    err.error = ex.Message;
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, err));
                }
            });
        }

        public static void OpenGmatDesktop(string scriptPath) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    string payload = $"{{\"script_path\": \"{scriptPath.Replace("\\", "\\\\")}\"}}";
                    PostJson(ServerUrl + "/api/open_gmat_gui", payload);
                } catch (Exception ex) {
                    Debug.LogWarning("[KSPGMAT] Error opening GMAT desktop: " + ex.Message);
                }
            });
        }

        public static void ImportScript(string scriptPath, Vessel vessel, Action<bool, List<MissionStep>, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    double ut = Planetarium.GetUniversalTime();
                    string vName = vessel != null ? Sanitize(vessel.vesselName) : "NRHOSat";

                    StringBuilder sb = new StringBuilder();
                    sb.Append("{");
                    sb.Append($"\"script_path\": \"{scriptPath.Replace("\\", "\\\\")}\",");
                    sb.Append($"\"ut\": {ut.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                    sb.Append($"\"vessel_name\": \"{vName}\"");
                    sb.Append("}");

                    string json = PostJson(ServerUrl + "/api/import_script", sb.ToString());
                    if (json.Contains("\"success\": true")) {
                        List<MissionStep> steps = new List<MissionStep>();
                        int seqIdx = json.IndexOf("\"sequence\":");
                        if (seqIdx >= 0) {
                            int endIdx = json.LastIndexOf("]");
                            string seqSection = json.Substring(seqIdx, endIdx - seqIdx + 1);
                            string[] items = seqSection.Split(new string[] { "{" }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string item in items) {
                                if (item.Contains("\"type\":")) {
                                    MissionStep st = new MissionStep();
                                    st.type = ExtractString("{" + item, "type");
                                    st.propagator = ExtractString("{" + item, "propagator");
                                    st.stop_type = ExtractString("{" + item, "stop_type");
                                    st.duration = ExtractDouble("{" + item, "duration");
                                    st.target_val = ExtractDouble("{" + item, "target_val");
                                    st.target_alt = ExtractDouble("{" + item, "target_alt");
                                    st.dv_v = ExtractDouble("{" + item, "dv_v");
                                    st.dv_n = ExtractDouble("{" + item, "dv_n");
                                    st.dv_b = ExtractDouble("{" + item, "dv_b");
                                    st.variable = ExtractString("{" + item, "variable");
                                    st.expression = ExtractString("{" + item, "expression");
                                    st.label = ExtractString("{" + item, "label");
                                    st.target_goal = ExtractString("{" + item, "target_goal");
                                    if (string.IsNullOrEmpty(st.target_goal)) st.target_goal = "Apoapsis";

                                    if (string.IsNullOrEmpty(st.stop_type)) st.stop_type = "Time";
                                    if (string.IsNullOrEmpty(st.propagator)) st.propagator = "LEOProp";
                                    steps.Add(st);
                                }
                            }
                        }
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, steps, "导入成功"));
                    } else {
                        string err = ExtractString(json, "error");
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, null, err ?? "导入失败"));
                    }
                } catch (Exception ex) {
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, null, ex.Message));
                }
            });
        }

        private static string PostJson(string url, string jsonPayload) {
            byte[] bytes = Encoding.UTF8.GetBytes(jsonPayload);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.ContentLength = bytes.Length;
            request.Timeout = 15000;

            using (Stream reqStream = request.GetRequestStream()) {
                reqStream.Write(bytes, 0, bytes.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream())) {
                return reader.ReadToEnd();
            }
        }

        public static string Sanitize(string rawName, string fallback = "Sat") {
            if (string.IsNullOrEmpty(rawName)) return fallback;
            StringBuilder sb = new StringBuilder();
            foreach (char c in rawName) {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') {
                    sb.Append(c);
                }
            }
            string cleaned = sb.ToString();
            if (string.IsNullOrEmpty(cleaned)) {
                return fallback;
            }
            if (cleaned[0] >= '0' && cleaned[0] <= '9') {
                cleaned = fallback + "_" + cleaned;
            }
            return cleaned;
        }

        private static MissionResult ParseMissionResult(string json) {
            MissionResult res = new MissionResult();
            try {
                res.success = json.Contains("\"success\": true");
                res.error = ExtractString(json, "error");
                res.script_path = ExtractString(json, "script_path");
                res.final_epoch = ExtractString(json, "final_epoch");

                res.final_sma = ExtractDouble(json, "final_sma");
                res.final_ecc = ExtractDouble(json, "final_ecc");
                res.final_inc = ExtractDouble(json, "final_inc");
                res.final_raan = ExtractDouble(json, "final_raan");
                res.final_aop = ExtractDouble(json, "final_aop");
                res.final_ta = ExtractDouble(json, "final_ta");
                res.final_mass = ExtractDouble(json, "final_mass");

                // Parse burns
                int burnsIdx = json.IndexOf("\"solved_burns\":");
                if (burnsIdx >= 0) {
                    int endIdx = json.IndexOf("]", burnsIdx);
                    string burnsSection = json.Substring(burnsIdx, endIdx - burnsIdx + 1);
                    string[] items = burnsSection.Split(new string[] { "{" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string item in items) {
                        if (item.Contains("\"type\":")) {
                            SolvedBurn b = new SolvedBurn();
                            b.type = ExtractString("{" + item, "type");
                            b.name = ExtractString("{" + item, "name");
                            b.origin = ExtractString("{" + item, "origin");
                            b.dv_v = ExtractDouble("{" + item, "dv_v");
                            b.dv_n = ExtractDouble("{" + item, "dv_n");
                            b.dv_b = ExtractDouble("{" + item, "dv_b");
                            b.magnitude = ExtractDouble("{" + item, "magnitude");
                            b.duration = ExtractDouble("{" + item, "duration");
                            b.thrust_kn = ExtractDouble("{" + item, "thrust_kn");
                            b.isp = ExtractDouble("{" + item, "isp");
                            b.fuel_burned = ExtractDouble("{" + item, "fuel_burned");
                            b.epoch_offset = ExtractDouble("{" + item, "epoch_offset");
                            res.solved_burns.Add(b);
                        }
                    }
                }

                // Parse updated sequence with real-time solved parameters
                int seqIdx = json.IndexOf("\"updated_sequence\":");
                if (seqIdx >= 0) {
                    int endSeq = json.IndexOf("]", seqIdx);
                    string seqSection = json.Substring(seqIdx, endSeq - seqIdx + 1);
                    string[] sItems = seqSection.Split(new string[] { "{" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string sItem in sItems) {
                        if (sItem.Contains("\"type\":")) {
                            MissionStep step = new MissionStep();
                            step.type = ExtractString("{" + sItem, "type");
                            step.duration = ExtractDouble("{" + sItem, "duration");
                            step.dv_v = ExtractDouble("{" + sItem, "dv_v");
                            step.dv_n = ExtractDouble("{" + sItem, "dv_n");
                            step.dv_b = ExtractDouble("{" + sItem, "dv_b");
                            step.label = ExtractString("{" + sItem, "label");
                            step.target_goal = ExtractString("{" + sItem, "target_goal");
                            if (string.IsNullOrEmpty(step.target_goal)) step.target_goal = "Apoapsis";
                            res.updated_sequence.Add(step);
                        }
                    }
                }

                res.render_image_path = ExtractString(json, "render_image_path");
            } catch (Exception ex) {
                res.success = false;
                res.error = "JSON 解析异常: " + ex.Message;
            }
            return res;
        }

        public static void RenderTrajectory(string viewMode, string centralBody, double bodyRadiusKm, Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    string cBody = string.IsNullOrEmpty(centralBody) ? "Earth" : centralBody;
                    string rKm = bodyRadiusKm > 0 ? bodyRadiusKm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "6378.1";
                    string payload = $"{{\"view_mode\": \"{viewMode}\", \"central_body\": \"{cBody}\", \"body_radius\": {rKm}}}";
                    string json = PostJson(ServerUrl + "/api/render_trajectory", payload);
                    string path = ExtractString(json, "image_path");
                    bool ok = json.Contains("\"success\": true");
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(ok, path));
                } catch (Exception ex) {
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, ex.Message));
                }
            });
        }

        public static void RenderTrajectory(string viewMode, Action<bool, string> callback) {
            RenderTrajectory(viewMode, "Earth", 6378.14, callback);
        }

        private static string ExtractString(string json, string key) {
            string pattern = $"\"{key}\": \"";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return null;
            int start = idx + pattern.Length;
            int end = json.IndexOf("\"", start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        private static double ExtractDouble(string json, string key) {
            string pattern = $"\"{key}\": ";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return 0.0;
            int start = idx + pattern.Length;
            int end = json.IndexOfAny(new char[] { ',', '}', '\n', '\r' }, start);
            if (end < 0) end = json.Length;
            string valStr = json.Substring(start, end - start).Trim().Replace("\"", "");
            double res;
            double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out res);
            return res;
        }
    }
}
