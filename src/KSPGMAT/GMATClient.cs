using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KSPGMAT {
    [Serializable]
    public class MissionStep {
        public string type = "Propagate"; // Propagate, Report, FiniteBurn, Targeter, Equation
        public string propagator = "LEOProp"; // GMAT Propagator object name
        public string stop_type = "Time"; // Time, Days, Apoapsis, Periapsis, Altitude, RMAG, TA, Node
        public double duration = 3600.0;
        public double target_val = 200.0; // Altitude (km), RMAG (km), TrueAnomaly (deg)
        public double target_alt = 35786.0; // km for TargetApoapsis
        public string target_goal = "Apoapsis"; // Apoapsis, Periapsis, SMA, ECC, INC, RMAG, LunarPerilune
        public string target_body = ""; // Body used by body-relative goals such as LunarPerilune
        public double dv_v = 0.0; // m/s
        public double dv_n = 0.0; // m/s
        public double dv_b = 0.0; // m/s
        public string burn_coordinate_system = "Local";
        public string burn_axes = "VNB";
        public string burn_origin = "";
        public double burn_isp = 0.0;
        public bool burn_decrement_mass = false;
        public double target_tolerance = 0.0;
        public double vz_tolerance = 0.0; // km/s; zero uses GMAT numerical default
        public double stop_tolerance = 1e-5; // GMAT Propagate StopTolerance (e.g. 1e-5)
        public double vary_perturbation = 0.0;
        public double vary_lower = 0.0;
        public double vary_upper = 0.0;
        public double vary_max_step = 0.0;
        public double epoch_offset = 0.0;
        public string variable = ""; // for Equation, e.g. "Sat.SMA" or "burntime"
        public string expression = ""; // for Equation, e.g. "-173.0"
        public string burn_direction = "Prograde"; // Prograde, Retrograde, Normal, AntiNormal, RadialIn, RadialOut
        public string utc_epoch = ""; // for UTCGregorian stop, e.g. "08 Mar 1960 12:00:00.000"
        public string dc_algorithm = "NewtonRaphson"; // NewtonRaphson, Broyden
        public string dc_derivative_method = "ForwardDifference"; // ForwardDifference, CentralDifference
        public int dc_max_iter = 25;
        public bool show_advanced_dc = false;
        public string label = "";
        public bool is_collapsed = false; // Target group collapsed in GUI
        public bool is_expanded = false; // Individual step details expanded in GUI
        public string dc_name = "DefaultDC";
        public string solve_mode = "Solve"; // Solve, RunInitialGuess
        public string exit_mode = "SaveAndContinue"; // SaveAndContinue, DiscardAndContinue
        public string vary_variable = "burntime";
        public double vary_initial = 150.0;
        public string achieve_param = "Earth.RMAG";
        public double achieve_val = 40000.0;
        public double achieve_tolerance = 0.1;
        public string var_name = "burntime";
        public string var_expr = "150.0";
        public bool burn_use_vary_var = false;
        public string burn_vary_var_name = "burntime";
        public string burn_name = ""; // GMAT Burn object name
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
        public double burn_x;
        public double burn_y;
        public double burn_z;
        public double burn_vx;
        public double burn_vy;
        public double burn_vz;
        public bool has_native_delta_v;
        public bool principia_writeable;
        public string native_result_note;
        public string align_mode = "Auto"; // Auto, GMAT, SnapAN, SnapDN, SnapAp, SnapPe
        public string target_event = "";   // Detected or chosen target event: AN, DN, Apoapsis, Periapsis, Time
        public double computed_initial_time;
        public double centroid_lead_time;
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
    public class DCSummary {
        public bool converged;
        public int iterations;
        public double desired;
        public double achieved;
        public double variance;
    }

    [Serializable]
    public class StepReportData {
        public int step_index;
        public string step_label;
        public double elapsed_secs;
        public string epoch_utc;
        public double altitude_km;
        public double sma_km;
        public double ecc;
        public double inc_deg;
        public double raan_deg;
        public double aop_deg;
        public double ta_deg;
        public double rmag_km;
        public double total_mass_kg;
        public double fuel_mass_kg;
        public double x;
        public double y;
        public double z;
        public double vx;
        public double vy;
        public double vz;
        public string raw_report_text;
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
        public double solve_ut;
        public double solve_principia_time;
        public string warning;
        public bool has_fuel_deficit;
        public double fuel_deficit;
        public List<SolvedBurn> solved_burns = new List<SolvedBurn>();
        public List<TrajectoryPoint> trajectory = new List<TrajectoryPoint>();
        public List<MissionStep> updated_sequence = new List<MissionStep>();
        public string render_image_path;
        public DCSummary dc_summary;
        public List<StepReportData> step_reports = new List<StepReportData>();
    }

    public static class GMATClient {
        public static bool IsConnected = false;
        public static string ServerStatusText = "Initializing...";
        private static string _customGmatBinPath = "";
        public static string GmatBinPath {
            get {
                if (!string.IsNullOrEmpty(_customGmatBinPath) && Directory.Exists(_customGmatBinPath)) return _customGmatBinPath;
                string env = Environment.GetEnvironmentVariable("GMAT_BIN");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] candidates = new string[] {
                    Path.Combine(userProfile, "gmat-win-R2026a", "bin"),
                    @"C:\Users\43701\gmat-win-R2026a\bin",
                    @"d:\nocomp\gmat-win-R2026a\bin",
                    @"C:\GMAT\bin",
                    @"D:\GMAT\bin"
                };
                foreach (var c in candidates) {
                    if (Directory.Exists(c)) return c;
                }
                return Path.Combine(userProfile, "gmat-win-R2026a", "bin");
            }
            set { _customGmatBinPath = value; }
        }
        public static string CustomPythonPath = "";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string FindPythonExe() {
            if (!string.IsNullOrEmpty(CustomPythonPath) && File.Exists(CustomPythonPath)) return CustomPythonPath;
            string[] candidates = new string[] {
                "python.exe",
                @"C:\Users\43701\AppData\Local\Programs\Python\Python310\python.exe",
                @"C:\Users\43701\AppData\Local\Programs\Python\Python311\python.exe",
                @"C:\Users\43701\AppData\Local\Programs\Python\Python312\python.exe",
                @"C:\Program Files\Python310\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python312\python.exe"
            };
            foreach (var c in candidates) {
                if (c == "python.exe") return c; // try PATH
                if (File.Exists(c)) return c;
            }
            return "python.exe";
        }

        public static string FindCliScript() {
            string[] candidates = new string[] {
                Path.Combine(GmatBinPath, "KSP_GMAT", "gmat_service", "gmat_cli.py"),
                Path.Combine(GmatBinPath, "gmat_service", "gmat_cli.py"),
                Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "GMATService", "gmat_cli.py")
            };
            foreach (var c in candidates) {
                if (File.Exists(c)) return c;
            }
            return Path.Combine(GmatBinPath, "KSP_GMAT", "gmat_service", "gmat_cli.py");
        }

        public static string GetOutputDir() {
            string dir = Path.Combine(Path.GetDirectoryName(GmatBinPath), "output");
            if (!Directory.Exists(dir)) {
                try { Directory.CreateDirectory(dir); } catch {}
            }
            return dir;
        }

        public static string JdToUtcGregorian(double jd) {
            double jdAdj = jd + 0.5;
            long Z = (long)Math.Floor(jdAdj);
            double F = jdAdj - Z;
            long A = Z;
            if (Z >= 2299161) {
                long alpha = (long)Math.Floor((Z - 1867216.25) / 36524.25);
                A = Z + 1 + alpha - (long)Math.Floor(alpha / 4.0);
            }
            long B = A + 1524;
            long C = (long)Math.Floor((B - 122.1) / 365.25);
            long D = (long)Math.Floor(365.25 * C);
            long E = (long)Math.Floor((B - D) / 30.6001);

            double dayWithFrac = B - D - (long)Math.Floor(30.6001 * E) + F;
            int day = (int)Math.Floor(dayWithFrac);
            int month = (int)(E < 14 ? E - 1 : E - 13);
            int year = (int)(month > 2 ? C - 4716 : C - 4715);

            double dayFrac = dayWithFrac - day;
            double totalSeconds = dayFrac * 86400.0;
            int hours = (int)(totalSeconds / 3600.0);
            double rem = totalSeconds - hours * 3600.0;
            int minutes = (int)(rem / 60.0);
            double seconds = rem - minutes * 60.0;

            string[] months = new string[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            string monStr = (month >= 1 && month <= 12) ? months[month] : "Jan";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:D2} {1} {2:D4} {3:D2}:{4:D2}:{5:00.000}", day, monStr, year, hours, minutes, seconds);
        }

        public static void CheckConnection(Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                string cli = FindCliScript();
                if (File.Exists(cli)) {
                    IsConnected = true;
                    ServerStatusText = "⚡ GMAT 原生引擎就绪 (无需后台服务)";
                    FetchParameters();
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, "CLI Ready"));
                } else {
                    IsConnected = false;
                    ServerStatusText = "❌ gmat_cli.py 未找到: " + cli;
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, ServerStatusText));
                }
            });
        }

        public static void FetchParameters(Action<bool> callback = null) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    // Load capabilities from local JSON file directly
                    string[] candidates = new string[] {
                        Path.Combine(GmatBinPath, "KSP_GMAT", "gmat_service", "gmat_capabilities.json"),
                        Path.Combine(GmatBinPath, "KSP_GMAT", "Plugins", "gmat_capabilities.json"),
                        Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "Plugins", "gmat_capabilities.json")
                    };
                    foreach (var path in candidates) {
                        if (File.Exists(path)) {
                            string json = File.ReadAllText(path, Encoding.UTF8);
                            if (!string.IsNullOrEmpty(json)) {
                                UI.GMATMainWindow.RunOnMainThread(() => {
                                    GMATParameterRegistry.LoadFromJson(json);
                                    callback?.Invoke(true);
                                });
                                return;
                            }
                        }
                    }
                } catch {}
                UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false));
            });
        }

        private static double KspUtToJd(double ut) {
            double gameEpochJd = 2433647.5; // Sol-Configs / RSS default: 1951-01-01
            double jd = gameEpochJd + (ut / 86400.0);
            if (jd < 2436116.5) jd = 2437000.0 + (ut / 86400.0);
            return jd;
        }

        private static double JdToGmatMjd(double jd) {
            return jd - 2430000.0;
        }

        public static void CalibrateEphem(double ut, Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    double jd = KspUtToJd(ut);
                    double mjd = JdToGmatMjd(jd);
                    string utcStr = JdToUtcGregorian(jd);
                    string res = $"{{\"success\": true, \"ut\": {ut.ToString("F3", CultureInfo.InvariantCulture)}, " +
                                 $"\"jd\": {jd.ToString("F6", CultureInfo.InvariantCulture)}, " +
                                 $"\"mjd\": {mjd.ToString("F6", CultureInfo.InvariantCulture)}, " +
                                 $"\"utc_gregorian\": \"{utcStr}\", " +
                                 $"\"status\": \"Calibrated\"}}";
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

                    if (selectedStages != null && selectedStages.Count > 0) {
                        double minDryTonnes = double.MaxValue;
                        double sumFuelTonnes = 0.0;
                        foreach (var st in selectedStages) {
                            sumFuelTonnes += st.fuelMassTonnes;
                            if (st.dryMassTonnes < minDryTonnes) minDryTonnes = st.dryMassTonnes;
                        }
                        dryMassKg = minDryTonnes * 1000.0;
                        fuelMassKg = sumFuelTonnes * 1000.0;
                    } else {
                        fuelMassKg = engine != null ? Math.Max(0.0, engine.fuelMassKg) : 0.0;
                        dryMassKg = Math.Max(0.0, vessel.totalMass * 1000.0 - fuelMassKg);
                    }

                    if (engine != null && engine.fuelMassKg > 0) {
                        fuelMassKg = Math.Max(fuelMassKg, engine.fuelMassKg);
                    }
                    double fuelDensity = engine != null ? engine.fuelDensityKgM3 : 0.0;
                    double tankVolume = engine != null ? engine.tankVolumeM3 : 0.0;
                    double tankPressure = engine != null ? engine.tankPressureKPa : 0.0;
                    double thrustN = engine != null ? engine.thrustKN * 1000.0 : 0.0;
                    double ispS = engine != null ? engine.ispVac : 0.0;
                    string engName = engine != null ? engine.title : "";

                    bool needsFiniteBurn = false;
                    for (int i = 0; i < steps.Count; i++) if (steps[i].type == "FiniteBurn") needsFiniteBurn = true;
                    if (needsFiniteBurn && (engine == null || fuelDensity <= 0.0 || tankVolume <= 0.0 || thrustN <= 0.0 || ispS <= 0.0)) {
                        throw new InvalidOperationException("有限推力任务需要从游戏中读取到完整发动机、燃料密度、储罐容积、推力和 Isp 参数");
                    }

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
                            KSPGMATLog.Info($"  - {kv.Key}: Type={p.integratorType}, Step={p.initialStepSize}s, MaxStep={p.maxStep}s, Center={p.centralBody}");
                        }
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.Append("{");
                    sb.Append("\"craft\": {");
                    sb.Append($"\"name\": \"{Sanitize(vessel.vesselName)}\",");
                    sb.Append($"\"central_body\": \"{vessel.mainBody.name}\",");
                    sb.Append($"\"ut\": {ut.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                    double jdVal = 2433647.5 + (ut / 86400.0);
                    if (jdVal < 2436116.5) jdVal = 2437000.0 + (ut / 86400.0);
                    string epochUtcVal = JdToUtcGregorian(jdVal);
                    sb.Append($"\"epoch_utc\": \"{epochUtcVal}\",");
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
                        sb.Append($"\"target_body\": \"{st.target_body ?? ""}\",");
                        sb.Append($"\"dv_v\": {st.dv_v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_n\": {st.dv_n.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_b\": {st.dv_b.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"burn_coordinate_system\": \"{st.burn_coordinate_system}\",");
                        sb.Append($"\"burn_axes\": \"{st.burn_axes}\",");
                        sb.Append($"\"burn_origin\": \"{st.burn_origin}\",");
                        sb.Append($"\"burn_isp\": {st.burn_isp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"burn_decrement_mass\": {(st.burn_decrement_mass ? "true" : "false")},");
                        sb.Append($"\"burn_name\": \"{st.burn_name ?? ""}\",");
                        sb.Append($"\"target_tolerance\": {st.target_tolerance.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"vz_tolerance\": {st.vz_tolerance.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"vary_perturbation\": {st.vary_perturbation.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"vary_lower\": {st.vary_lower.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"vary_upper\": {st.vary_upper.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"vary_max_step\": {st.vary_max_step.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"variable\": \"{st.variable}\",");
                        sb.Append($"\"expression\": \"{st.expression}\",");
                        sb.Append($"\"burn_direction\": \"{st.burn_direction ?? "Prograde"}\",");
                        sb.Append($"\"utc_epoch\": \"{st.utc_epoch ?? ""}\",");
                        sb.Append($"\"dc_algorithm\": \"{st.dc_algorithm ?? "NewtonRaphson"}\",");
                        sb.Append($"\"dc_derivative_method\": \"{st.dc_derivative_method ?? "ForwardDifference"}\",");
                        sb.Append($"\"dc_max_iter\": {st.dc_max_iter},");
                        sb.Append($"\"label\": \"{st.label}\",");
                        sb.Append($"\"dc_name\": \"{st.dc_name ?? "DefaultDC"}\",");
                        sb.Append($"\"solve_mode\": \"{st.solve_mode ?? "Solve"}\",");
                        sb.Append($"\"exit_mode\": \"{st.exit_mode ?? "SaveAndContinue"}\",");
                        sb.Append($"\"vary_variable\": \"{st.vary_variable ?? "burntime"}\",");
                        sb.Append($"\"vary_initial\": {st.vary_initial.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"achieve_param\": \"{st.achieve_param ?? "Earth.RMAG"}\",");
                        sb.Append($"\"achieve_val\": {st.achieve_val.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"achieve_tolerance\": {st.achieve_tolerance.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},");
                        sb.Append($"\"var_name\": \"{st.var_name ?? "burntime"}\",");
                        sb.Append($"\"var_expr\": \"{st.var_expr ?? "150.0"}\",");
                        sb.Append($"\"burn_use_vary_var\": {(st.burn_use_vary_var ? "true" : "false")},");
                        sb.Append($"\"burn_vary_var_name\": \"{st.burn_vary_var_name ?? "burntime"}\",");
                        sb.Append($"\"stop_tolerance\": {st.stop_tolerance.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},");
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

                    // Custom User Coordinate Systems
                    if (GMATParameterRegistry.UserCoordinateSystems != null && GMATParameterRegistry.UserCoordinateSystems.Count > 0) {
                        sb.Append(",\"custom_coordinate_systems\": [");
                        for (int csIdx = 0; csIdx < GMATParameterRegistry.UserCoordinateSystems.Count; csIdx++) {
                            var cs = GMATParameterRegistry.UserCoordinateSystems[csIdx];
                            if (csIdx > 0) sb.Append(",");
                            sb.Append("{");
                            sb.Append($"\"name\": \"{cs.name}\",");
                            sb.Append($"\"origin\": \"{cs.origin}\",");
                            sb.Append($"\"axes\": \"{cs.axes}\",");
                            sb.Append($"\"primary\": \"{cs.primary}\",");
                            sb.Append($"\"secondary\": \"{cs.secondary}\",");
                            sb.Append($"\"xAxis\": \"{cs.xAxis}\",");
                            sb.Append($"\"yAxis\": \"{cs.yAxis}\",");
                            sb.Append($"\"zAxis\": \"{cs.zAxis}\",");
                            sb.Append($"\"epoch\": \"{cs.epoch}\"");
                            sb.Append("}");
                        }
                        sb.Append("]");
                    }

                    sb.Append("}");

                    string responseJson = "";
                    string cliScript = FindCliScript();
                    string outputDir = GetOutputDir();
                    string inputFile = Path.Combine(outputDir, "mission_input.json");
                    string outputFile = Path.Combine(outputDir, "mission_result.json");
                    if (File.Exists(outputFile)) {
                        try { File.Delete(outputFile); } catch {}
                    }

                    File.WriteAllText(inputFile, sb.ToString(), Utf8NoBom);

                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = FindPythonExe(),
                        Arguments = $"\"{cliScript}\" --action solve --input \"{inputFile}\" --output \"{outputFile}\"",
                        WorkingDirectory = GmatBinPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = System.Diagnostics.Process.Start(psi)) {
                        process.WaitForExit(45000);
                    }

                    if (File.Exists(outputFile)) {
                        responseJson = File.ReadAllText(outputFile, Encoding.UTF8);
                    } else {
                        throw new Exception("GMAT CLI 执行超时或未生成结果文件。");
                    }
                    sw.Stop();

                    MissionResult result = ParseMissionResult(responseJson);
                    result.solve_ut = ut;
                    result.solve_principia_time = PrincipiaInterop.GetCurrentPrincipiaTime();

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
                    string gmatExe = Path.Combine(GmatBinPath, "GMAT.exe");
                    if (File.Exists(gmatExe)) {
                        ProcessStartInfo psi = new ProcessStartInfo {
                            FileName = gmatExe,
                            WorkingDirectory = GmatBinPath,
                            UseShellExecute = true
                        };
                        if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath)) {
                            psi.Arguments = $"\"{scriptPath}\"";
                        }
                        Process.Start(psi);
                        UnityEngine.Debug.Log("[KSPGMAT] GMAT GUI launched.");
                    } else {
                        UnityEngine.Debug.LogWarning("[KSPGMAT] GMAT.exe not found at " + gmatExe);
                    }
                } catch (Exception ex) {
                    UnityEngine.Debug.LogWarning("[KSPGMAT] Error opening GMAT desktop: " + ex.Message);
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

                    string cliScript = FindCliScript();
                    string outputDir = GetOutputDir();
                    string inputFile = Path.Combine(outputDir, "import_input.json");
                    string outputFile = Path.Combine(outputDir, "import_result.json");
                    if (File.Exists(outputFile)) {
                        try { File.Delete(outputFile); } catch {}
                    }
                    File.WriteAllText(inputFile, sb.ToString(), Utf8NoBom);

                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = FindPythonExe(),
                        Arguments = $"\"{cliScript}\" --action import --input \"{inputFile}\" --output \"{outputFile}\"",
                        WorkingDirectory = GmatBinPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var process = System.Diagnostics.Process.Start(psi)) {
                        process.WaitForExit(15000);
                    }

                    string json = File.Exists(outputFile) ? File.ReadAllText(outputFile, Encoding.UTF8) : "{}";
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
                                    string importedType = st.type;
                                    if (string.Equals(importedType, "Maneuver", StringComparison.OrdinalIgnoreCase)) st.type = "ImpulsiveBurn";
                                    if (string.Equals(importedType, "EndTarget", StringComparison.OrdinalIgnoreCase)) st.type = "EndTarget";
                                    st.propagator = ExtractString("{" + item, "propagator");
                                    if (st.propagator == "NearEarth") st.propagator = "LEOProp";
                                    else if (st.propagator == "NearMoon") st.propagator = "LunarNRHOProp";
                                    st.stop_type = ExtractString("{" + item, "stop_type");
                                    st.duration = ExtractDouble("{" + item, "duration");
                                    st.target_val = ExtractDouble("{" + item, "target_val");
                                    st.target_alt = ExtractDouble("{" + item, "target_alt");
                                    st.dv_v = ExtractDouble("{" + item, "dv_v");
                                    st.dv_n = ExtractDouble("{" + item, "dv_n");
                                    st.dv_b = ExtractDouble("{" + item, "dv_b");
                                    st.burn_name = ExtractString("{" + item, "name");
                                    st.burn_origin = ExtractString("{" + item, "origin");
                                    st.burn_axes = ExtractString("{" + item, "axes");
                                    st.burn_coordinate_system = ExtractString("{" + item, "coordinate_system");
                                    st.burn_isp = ExtractDouble("{" + item, "isp");
                                    st.burn_decrement_mass = ExtractBool("{" + item, "decrement_mass");
                                    st.variable = ExtractString("{" + item, "variable");
                                    st.expression = ExtractString("{" + item, "expression");
                                    st.label = ExtractString("{" + item, "label");
                                    st.vary_variable = ExtractString("{" + item, "vary_variable");
                                    st.vary_initial = ExtractDouble("{" + item, "vary_initial");
                                    st.vary_perturbation = ExtractDouble("{" + item, "vary_perturbation");
                                    st.vary_lower = ExtractDouble("{" + item, "vary_lower");
                                    st.vary_upper = ExtractDouble("{" + item, "vary_upper");
                                    st.vary_max_step = ExtractDouble("{" + item, "vary_max_step");
                                    st.achieve_param = ExtractString("{" + item, "achieve_param");
                                    st.achieve_val = ExtractDouble("{" + item, "achieve_val");
                                    st.achieve_tolerance = ExtractDouble("{" + item, "achieve_tolerance");
                                    st.solve_mode = ExtractString("{" + item, "solve_mode");
                                    st.exit_mode = ExtractString("{" + item, "exit_mode");
                                    st.target_goal = ExtractString("{" + item, "target_goal");
                                    if (string.IsNullOrEmpty(st.target_goal)) st.target_goal = "Apoapsis";

                                    if (string.IsNullOrEmpty(st.stop_type)) st.stop_type = "Time";
                                    if (string.IsNullOrEmpty(st.propagator)) st.propagator = "LEOProp";
                                    steps.Add(st);
                                }
                            }
                        }
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, steps, "成功"));
                    } else {
                        string err = ExtractString(json, "error");
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, null, err ?? "失败"));
                    }
                } catch (Exception ex) {
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, null, ex.Message));
                }
            });
        }

        public static void FetchComparison(Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    // Read the most recent comparison result from the last sync output
                    string outputDir = GetOutputDir();
                    string compFile = Path.Combine(outputDir, "principia_comp.json");
                    if (File.Exists(compFile)) {
                        string json = File.ReadAllText(compFile, Encoding.UTF8);
                        UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, json));
                    } else {
                        UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, "比较数据不可用，请先执行 Principia 同步"));
                    }
                } catch (Exception ex) {
                    UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, ex.Message));
                }
            });
        }

        public static void SyncPrincipiaNodes(Vessel vessel, List<PrincipiaInterop.FlightPlanNode> nodes, double predictedApoapsisKm, Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    double ut = Planetarium.GetUniversalTime();
                    double pTime = PrincipiaInterop.GetCurrentPrincipiaTime();
                    StringBuilder sb = new StringBuilder();
                    sb.Append("{");
                    sb.Append("\"vessel_state\": {");
                    sb.Append($"\"vessel_name\": \"{Sanitize(vessel != null ? vessel.vesselName : "Vessel")}\",");
                    sb.Append($"\"ut\": {ut.ToString("F3", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"principia_time\": {pTime.ToString("F3", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"principia_final_apoapsis\": {predictedApoapsisKm.ToString("F1", CultureInfo.InvariantCulture)},");
                    sb.Append($"\"mass_tonnes\": {(vessel != null ? vessel.totalMass : 0.0).ToString("F3", CultureInfo.InvariantCulture)}");
                    sb.Append("},");
                    sb.Append("\"nodes\": [");
                    for (int i = 0; i < (nodes != null ? nodes.Count : 0); i++) {
                        var n = nodes[i];
                        if (i > 0) sb.Append(",");
                        sb.Append("{");
                        sb.Append($"\"index\": {i+1},");
                        sb.Append($"\"initial_time\": {n.initialTime.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"duration\": {n.duration.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"magnitude\": {n.deltaV.magnitude.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_tangent\": {n.deltaV.x.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_normal\": {n.deltaV.y.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"dv_binormal\": {n.deltaV.z.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"thrust_kn\": {n.thrust_kn.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"isp\": {n.isp.ToString("F3", CultureInfo.InvariantCulture)},");
                        sb.Append($"\"initial_mass_tonnes\": {n.initial_mass_tonnes.ToString("F3", CultureInfo.InvariantCulture)}");
                        sb.Append("}");
                    }
                    sb.Append("]");
                    sb.Append("}");

                    string cliScript = FindCliScript();
                    string outputDir = GetOutputDir();
                    string inputFile = Path.Combine(outputDir, "principia_sync.json");
                    string outputFile = Path.Combine(outputDir, "principia_comp.json");
                    if (File.Exists(outputFile)) {
                        try { File.Delete(outputFile); } catch {}
                    }
                    File.WriteAllText(inputFile, sb.ToString(), Utf8NoBom);
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = FindPythonExe(),
                        Arguments = $"\"{cliScript}\" --action sync --input \"{inputFile}\" --output \"{outputFile}\"",
                        WorkingDirectory = GmatBinPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var process = System.Diagnostics.Process.Start(psi)) {
                        process.WaitForExit(15000);
                    }
                    string responseJson = "";
                    if (File.Exists(outputFile)) {
                        responseJson = File.ReadAllText(outputFile, Encoding.UTF8);
                    }
                    UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, responseJson));
                } catch (Exception ex) {
                    UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, ex.Message));
                }
            });
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
                res.warning = ExtractString(json, "warning");
                res.has_fuel_deficit = json.Contains("\"has_fuel_deficit\": true");
                res.fuel_deficit = ExtractDouble(json, "fuel_deficit");
                res.script_path = ExtractString(json, "script_path");
                res.final_epoch = ExtractString(json, "final_epoch");

                res.final_sma = ExtractDouble(json, "final_sma");
                res.final_ecc = ExtractDouble(json, "final_ecc");
                res.final_inc = ExtractDouble(json, "final_inc");
                res.final_raan = ExtractDouble(json, "final_raan");
                res.final_aop = ExtractDouble(json, "final_aop");
                res.final_ta = ExtractDouble(json, "final_ta");
                res.final_mass = ExtractDouble(json, "final_mass");

                // Parse step reports
                int repIdx = json.IndexOf("\"step_reports\":");
                if (repIdx >= 0) {
                    int endIdx = json.IndexOf("]", repIdx);
                    if (endIdx > repIdx) {
                        string repSection = json.Substring(repIdx, endIdx - repIdx + 1);
                        string[] items = repSection.Split(new string[] { "{" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string item in items) {
                            if (item.Contains("\"step_index\":") || item.Contains("\"step_label\":")) {
                                string block = "{" + item;
                                StepReportData rep = new StepReportData();
                                rep.step_index = (int)ExtractDouble(block, "step_index");
                                rep.step_label = ExtractString(block, "step_label");
                                rep.elapsed_secs = ExtractDouble(block, "elapsed_secs");
                                rep.epoch_utc = ExtractString(block, "epoch_utc");
                                rep.altitude_km = ExtractDouble(block, "altitude_km");
                                rep.sma_km = ExtractDouble(block, "sma_km");
                                rep.ecc = ExtractDouble(block, "ecc");
                                rep.inc_deg = ExtractDouble(block, "inc_deg");
                                rep.raan_deg = ExtractDouble(block, "raan_deg");
                                rep.aop_deg = ExtractDouble(block, "aop_deg");
                                rep.ta_deg = ExtractDouble(block, "ta_deg");
                                rep.rmag_km = ExtractDouble(block, "rmag_km");
                                rep.total_mass_kg = ExtractDouble(block, "total_mass_kg");
                                rep.fuel_mass_kg = ExtractDouble(block, "fuel_mass_kg");
                                rep.x = ExtractDouble(block, "x");
                                rep.y = ExtractDouble(block, "y");
                                rep.z = ExtractDouble(block, "z");
                                rep.vx = ExtractDouble(block, "vx");
                                rep.vy = ExtractDouble(block, "vy");
                                rep.vz = ExtractDouble(block, "vz");
                                rep.raw_report_text = ExtractString(block, "raw_report_text");
                                res.step_reports.Add(rep);
                            }
                        }
                    }
                }

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
                            b.burn_x = ExtractDouble("{" + item, "burn_x");
                            b.burn_y = ExtractDouble("{" + item, "burn_y");
                            b.burn_z = ExtractDouble("{" + item, "burn_z");
                            b.burn_vx = ExtractDouble("{" + item, "burn_vx");
                            b.burn_vy = ExtractDouble("{" + item, "burn_vy");
                            b.burn_vz = ExtractDouble("{" + item, "burn_vz");
                            b.has_native_delta_v = item.Contains("\"has_native_delta_v\": true");
                            b.principia_writeable = item.Contains("\"principia_writeable\": true");
                            b.native_result_note = ExtractString("{" + item, "native_result_note");
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

                int dcIdx = json.IndexOf("\"dc_summary\":");
                if (dcIdx >= 0) {
                    int endDc = json.IndexOf("}", dcIdx);
                    if (endDc > dcIdx) {
                        string dcBlock = json.Substring(dcIdx, endDc - dcIdx + 1);
                        res.dc_summary = new DCSummary();
                        res.dc_summary.converged = dcBlock.Contains("\"converged\": true");
                        res.dc_summary.iterations = (int)ExtractDouble(dcBlock, "iterations");
                        res.dc_summary.desired = ExtractDouble(dcBlock, "desired");
                        res.dc_summary.achieved = ExtractDouble(dcBlock, "achieved");
                        res.dc_summary.variance = ExtractDouble(dcBlock, "variance");
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

                    string cliScript = FindCliScript();
                    string outputDir = GetOutputDir();
                    string inputFile = Path.Combine(outputDir, "render_input.json");
                    string outputFile = Path.Combine(outputDir, "render_result.json");
                    File.WriteAllText(inputFile, payload, Utf8NoBom);
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = FindPythonExe(),
                        Arguments = $"\"{cliScript}\" --action render --input \"{inputFile}\" --output \"{outputFile}\"",
                        WorkingDirectory = GmatBinPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var process = System.Diagnostics.Process.Start(psi)) {
                        process.WaitForExit(15000);
                    }
                    if (File.Exists(outputFile)) {
                        string resJson = File.ReadAllText(outputFile, Encoding.UTF8);
                        string path = ExtractString(resJson, "image_path");
                        bool ok = resJson.Contains("\"success\": true");
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(ok, path));
                    } else {
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, "渲染进程未生成输出文件"));
                    }
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
            string str = json.Substring(start, end - start);
            try {
                return System.Text.RegularExpressions.Regex.Unescape(str);
            } catch {
                return str;
            }
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

        private static bool ExtractBool(string json, string key) {
            string pattern = $"\"{key}\":";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            idx += pattern.Length;
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            return json.IndexOf("true", idx, StringComparison.OrdinalIgnoreCase) == idx;
        }
    }
}
