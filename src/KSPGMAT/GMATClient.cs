using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace KSPGMAT {
    [Serializable]
    public class GMATUserVariable {
        public string name = "Var1";
        public double value = 0.0;
        public string description = "";
    }

    [Serializable]
    public class GMATUserArray {
        public string name = "Arr1";
        public int rows = 3;
        public int cols = 3;
        public List<double> values = new List<double>();
        public string description = "";
    }

    [Serializable]
    public class GMATUserString {
        public string name = "Str1";
        public string value = "";
        public string description = "";
    }

    [Serializable]
    public class GMATImportResult {
        public bool success;
        public string message;
        public string script_name;
        public string primary_craft_name;
        public List<string> all_spacecraft_names = new List<string>();
        public List<GMATUserVariable> user_variables = new List<GMATUserVariable>();
        public List<GMATUserArray> user_arrays = new List<GMATUserArray>();
        public List<GMATUserString> user_strings = new List<GMATUserString>();
        public List<string> solvers = new List<string>();
        public List<Model.GMATSolverModel> solver_models = new List<Model.GMATSolverModel>();
        public List<Model.GMATTankModel> tanks = new List<Model.GMATTankModel>();
        public List<Model.GMATThrusterModel> thrusters = new List<Model.GMATThrusterModel>();
        public List<MissionStep> sequence = new List<MissionStep>();
        public double craft_dry_mass = 1000.0;
        public double craft_fuel_mass = 0.0;
        public double craft_drag_area = 15.0;
        public double craft_cd = 2.2;
        public double craft_cr = 1.8;
        public double craft_srp_area = 15.0;
        public string craft_epoch = "";
        public string craft_date_format = "UTCGregorian";
        public string craft_coord_system = "EarthMJ2000Eq";
        public string craft_state_type = "Cartesian";
        public double craft_x = 0.0;
        public double craft_y = 0.0;
        public double craft_z = 0.0;
        public double craft_vx = 0.0;
        public double craft_vy = 0.0;
        public double craft_vz = 0.0;
        public double craft_sma = 0.0;
        public double craft_ecc = 0.0;
        public double craft_inc = 0.0;
        public double craft_raan = 0.0;
        public double craft_aop = 0.0;
        public double craft_ta = 0.0;
        public string raw_script_content = "";
    }

    [Serializable]
    public class MissionStep {
        public string type = "Propagate"; // 27 Native GMAT Commands: Propagate, Maneuver, BeginFiniteBurn, EndFiniteBurn, BeginFileThrust, EndFileThrust, Target, Vary, Achieve, EndTarget, Optimize, Minimize, NonlinearConstraint, EndOptimize, If, Else, EndIf, While, EndWhile, For, EndFor, Equation, ScriptEvent, Set, Global, CallGmatFunction, CallBuiltinGmatFunction, Report, Write, Toggle, ClearPlot, PenUp, PenDown, UpdateDynamicData, FindEvents, Stop, Breakpoint, CommandEcho, MarkPoint
        public string propagator = "LEOProp"; // GMAT Propagator object name
        public string stop_type = "Time"; // Time, Days, Apoapsis, Periapsis, Altitude, RMAG, TA, Node
        public double duration = 3600.0;
        public string duration_str = ""; // Supports variable assignment e.g. "GTODuration" or "Burntime"
        public double target_val = 200.0; // Stop condition parameter value (km, deg, etc.)
        public string target_val_str = ""; // Supports variable names or mathematical expressions
        public double target_alt = 35786.0; // Legacy fallback
        public string target_goal = "Apoapsis"; // Stop condition target goal
        public string target_body = ""; // Celestial body for body-relative conditions
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
        public string vary_perturbation_str = "";
        public double vary_lower = 0.0;
        public string vary_lower_str = "";
        public double vary_upper = 0.0;
        public string vary_upper_str = "";
        public double vary_max_step = 0.0;
        public string vary_max_step_str = "";
        public double vary_additive_scale = 0.0;
        public double vary_multiplicative_scale = 1.0;
        public string vary_solver = "DefaultDC";
        public double epoch_offset = 0.0;
        public string variable = ""; // for Equation / Set, e.g. "Sat.SMA" or "burntime"
        public string expression = ""; // for Equation / Set, e.g. "-173.0"
        public double thrust_dir1 = 1.0; // V-axis component
        public double thrust_dir2 = 0.0; // N-axis component
        public double thrust_dir3 = 0.0; // B-axis component
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
        public string vary_initial_str = "";
        public string achieve_param = "Earth.RMAG";
        public double achieve_val = 40000.0;
        public string achieve_val_str = "";
        public double achieve_tolerance = 0.1;
        public string achieve_tolerance_str = "";
        public string var_name = "burntime";
        public string var_expr = "150.0";
        public bool burn_use_vary_var = false;
        public string burn_vary_var_name = "burntime";
        public string burn_name = "";
        public string bound_tank = "";
        public string bound_thruster = "";
        public List<string> bound_thrusters = new List<string>(); // For multi-engine / heterogenous engine clusters
        public double vary_dir1_lower = -1.0, vary_dir1_upper = 1.0, vary_dir1_max_step = 0.1;
        public double vary_dir2_lower = -1.0, vary_dir2_upper = 1.0, vary_dir2_max_step = 0.1;
        public double vary_dir3_lower = -1.0, vary_dir3_upper = 1.0, vary_dir3_max_step = 0.1;
        public bool is_vary_vector = true;
        public bool vary_dir1_enabled = true;
        public bool vary_dir2_enabled = true;
        public bool vary_dir3_enabled = false;
        public double separated_mass = 0.0;
        public double remaining_dry_mass = 0.0;
        public int separated_stage_index = 0; // GMAT Burn object name

        // === PER-STEP SOLVER RESULTS FOR RICH UI DISPLAY ===
        public double solved_value = 0.0;
        public string solved_value_str = "";
        public bool has_solved_value = false;
        public double solved_dir1 = 1.0;
        public double solved_dir2 = 0.0;
        public double solved_dir3 = 0.0;
        public double achieved_value = 0.0;
        public double achieved_variance = 0.0;
        public bool achieve_passed = false;
        public bool has_achieve_result = false;
        public int solver_iterations = 0;
        public bool solver_converged = false;

        // === EXTENDED FIELDS FOR FULL 27 COMMANDS ===
        // 1. File Thrust (BeginFileThrust / EndFileThrust)
        public string file_thrust_name = "ThrustHistory1";
        public string file_thrust_path = "";

        // 2. Optimization (Optimize / Minimize / NonlinearConstraint / EndOptimize)
        public string optimizer_name = "SQP1"; // SQP1, VF13ad, SNOPT, Fmincon
        public string minimize_variable = "Sat.FuelMassBurned";
        public string constraint_lhs = "Sat.Luna.RMAG";
        public string constraint_op = ">="; // >=, <=, ==
        public double constraint_val = 1937.4;
        public double constraint_tol = 0.1;

        // 3. Control Logic (If / Else / EndIf / While / EndWhile / For / EndFor)
        public string condition_expr = "Sat.Earth.RMAG < 7000.0";
        public string for_var = "i";
        public string for_range = "1:1:5"; // start:step:end

        // 4. Script Event & Math (ScriptEvent / Set / Global)
        public string script_content = "% Custom GMAT Script Block\n";
        public string set_target = "Sat.Epoch";
        public string set_expr = "'01 Jan 2026 12:00:00.000'";
        public string global_objects = "Sat PropTank";

        // 5. Function Calls (CallGmatFunction / CallBuiltinGmatFunction)
        public string function_name = "MyCustomFunc";
        public string func_inputs = "Sat.X, Sat.Y";
        public string func_outputs = "out_x, out_y";

        // 6. Output, Visualization & Draw Control (Write / Toggle / ClearPlot / PenUp / PenDown / UpdateDynamicData)
        public string subscriber_name = "EarthInertialView";
        public string action_state = "Off"; // On, Off
        public string write_fields = "Sat.UTCGregorian Sat.X Sat.Y Sat.Z";
        public bool write_msg_window = true;
        public bool write_log_file = true;

        // 7. Events & Debug (FindEvents / Breakpoint / CommandEcho / MarkPoint)
        public string event_locator = "EclipseLocator1";
        public bool append_events = true;
        public string mark_label = "StageSeparation";
        public string echo_state = "On"; // On, Off
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
        public double computed_initial_time;
        public double centroid_lead_time;
        public bool enable_timing_optimization;
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
    public class DCGoalEntry {
        public string GoalName;
        public double Desired;
        public double Achieved;
        public double Tolerance;
    }

    [Serializable]
    public class DCSummary {
        public bool converged;
        public int iterations;
        public double desired;
        public double achieved;
        public double variance;
        public Dictionary<string, double> final_variables = new Dictionary<string, double>();
        public List<DCGoalEntry> goals_summary = new List<DCGoalEntry>();
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
        public double vmag_km_s;
        public double periapsis_km;
        public double apoapsis_km;
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
        public string error_details;
        public string gmat_log;
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
        public Dictionary<string, DCSummary> dc_summaries = new Dictionary<string, DCSummary>();
        public bool all_solvers_converged = true;
        public List<string> unconverged_solvers = new List<string>();
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
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string[] candidates = new string[] {
                    Path.Combine(userProfile, "gmat-win-R2026a", "bin"),
                    Path.Combine(userProfile, "GMAT", "bin"),
                    Path.Combine(programFiles, "GMAT", "bin"),
                    @"C:\GMAT\bin",
                    @"D:\GMAT\bin",
                    Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "gmat", "bin")
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
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates = new string[] {
                "python.exe",
                Path.Combine(localAppData, @"Programs\Python\Python310\python.exe"),
                Path.Combine(localAppData, @"Programs\Python\Python311\python.exe"),
                Path.Combine(localAppData, @"Programs\Python\Python312\python.exe"),
                Path.Combine(programFiles, @"Python310\python.exe"),
                Path.Combine(programFiles, @"Python311\python.exe"),
                Path.Combine(programFiles, @"Python312\python.exe")
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
            // NASA GMAT ephemeris requires Gregorian date >= 04 Oct 1957 (JD 2436116.0)
            // For RP-1 / early career saves (e.g. 1950/1951), safely map the baseline epoch to valid range
            if (jd < 2436116.0) {
                double safeBaseJd = 2437000.0; // 07 Mar 1960
                double startOffset = (jd > 2430000.0) ? (jd - 2433647.5) : jd;
                jd = safeBaseJd + startOffset;
            }
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

        public static double KspUtToJd(double ut) {
            double gameEpochJd = 2433647.5; // Sol-Configs / RSS default: 1951-01-01
            double jd = gameEpochJd + (ut / 86400.0);
            if (jd < 2436116.5) jd = 2437000.0 + (ut / 86400.0);
            return jd;
        }

        public static double JdToGmatMjd(double jd) {
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

        public static void AbortCurrentExecution() {
            GMATConsoleRunner.AbortExecution();
        }

        public static void PropagateMission(Vessel vessel, StageData stage, ROEngineInfo engine, List<MissionStep> steps, Action<bool, MissionResult> callback) {
            List<StageData> list = stage != null ? new List<StageData> { stage } : null;
            PropagateMission(vessel, list, engine, steps, null, null, callback);
        }

        public static void PropagateMission(Vessel vessel, List<StageData> selectedStages, ROEngineInfo engine, List<MissionStep> steps, Action<bool, MissionResult> callback) {
            PropagateMission(vessel, selectedStages, engine, steps, null, null, callback);
        }

        public static void PropagateMission(Vessel vessel, List<StageData> selectedStages, ROEngineInfo engine, List<MissionStep> steps, Dictionary<string, PropagatorConfig> propagatorConfigs, Action<bool, MissionResult> callback) {
            PropagateMission(vessel, selectedStages, engine, steps, propagatorConfigs, null, callback);
        }

        public static void PropagateMissionWithRawScript(Vessel vessel, List<StageData> selectedStages, ROEngineInfo engine, List<MissionStep> steps, Dictionary<string, PropagatorConfig> propagatorConfigs, string rawScript, Action<bool, MissionResult> callback) {
            PropagateMission(vessel, selectedStages, engine, steps, propagatorConfigs, rawScript, callback);
        }

        public static void PropagateMission(Vessel vessel, List<StageData> selectedStages, ROEngineInfo engine, List<MissionStep> steps, Dictionary<string, PropagatorConfig> propagatorConfigs, string rawScript, Action<bool, MissionResult> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    KSPGMATLog.Section("🚀 GMAT NATIVE MISSION PROPAGATION STARTED");
                    if (vessel != null) {
                        GMATStateManager.Current.SyncFromVessel(vessel);
                    }

                    string scriptToSend = !string.IsNullOrEmpty(rawScript)
                        ? rawScript
                        : GMATScriptGenerator.GenerateScript(vessel, steps, null, propagatorConfigs);

                    GMATConsoleRunner.ExecuteScriptAsync(scriptToSend, steps, (success, result) => {
                        sw.Stop();
                        if (result != null) {
                            result.solve_ut = Planetarium.GetUniversalTime();
                            result.solve_principia_time = PrincipiaInterop.GetCurrentPrincipiaTime();
                            if (result.success) {
                                KSPGMATLog.Success($"GMAT execution succeeded in {sw.ElapsedMilliseconds} ms!");
                                KSPGMATLog.Success($"Final Orbit: SMA={result.final_sma:F2}km, ECC={result.final_ecc:F5}, INC={result.final_inc:F2}°");
                                KSPGMATLog.Success($"Solved Burns ({result.solved_burns.Count}):");
                                for (int b = 0; b < result.solved_burns.Count; b++) {
                                    var bn = result.solved_burns[b];
                                    KSPGMATLog.Success($"  [Burn {b+1}] {bn.name} ({bn.type}): ΔV=({bn.dv_v:F1}, {bn.dv_n:F1}, {bn.dv_b:F1}) m/s, Mag={bn.magnitude:F1} m/s, Dur={bn.duration:F1}s");
                                }
                            } else {
                                KSPGMATLog.Error($"GMAT execution failed in {sw.ElapsedMilliseconds} ms: {result.error}");
                            }
                        }
                        KSPGMATLog.Section("GMAT MISSION PROPAGATION COMPLETED");
                        KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(success, result));
                    });
                } catch (Exception ex) {
                    sw.Stop();
                    KSPGMATLog.Error($"GMAT PropagateMission Exception in {sw.ElapsedMilliseconds} ms: {ex}");
                    KSPGMATLog.Section("GMAT MISSION PROPAGATION FAILED");
                    MissionResult err = new MissionResult {
                        success = false,
                        error = ex.Message
                    };
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

        /// <summary>
        /// Compiles the full in-game mission state and exports it as a standalone GMAT .script file.
        /// </summary>
        public static void ExportScript(string targetPath, Vessel vessel, List<MissionStep> sequence, List<GMATUserVariable> userVars, List<GMATUserArray> userArrays, List<GMATUserString> userStrings, Action<bool, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    if (vessel != null) {
                        GMATStateManager.Current.SyncFromVessel(vessel);
                    }
                    string scriptContent = GMATScriptGenerator.GenerateScript(vessel, sequence, GMATStateManager.Current.Spacecraft.CentralBody);
                    string sanitized = GMATStateManager.SanitizeGmatScript(scriptContent);
                    File.WriteAllText(targetPath, sanitized, Utf8NoBom);
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(true, $"✅ 成功导出高保真 GMAT 脚本至: {targetPath}"));
                } catch (Exception ex) {
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, "导出异常: " + ex.Message));
                }
            });
        }

        public static void ImportScript(string scriptPath, Vessel vessel, Action<bool, GMATImportResult, string> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    var result = GMATScriptParser.ParseScriptFile(scriptPath, vessel);
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(result.success, result, result.message ?? (result.success ? "成功" : "解析失败")));
                } catch (Exception ex) {
                    KSPGMAT.UI.GMATMainWindow.RunOnMainThread(() => callback?.Invoke(false, null, ex.Message));
                }
            });
        }

        public static void ImportScript(string scriptPath, Vessel vessel, Action<bool, List<MissionStep>, string> callback) {
            ImportScript(scriptPath, vessel, (ok, res, msg) => {
                callback?.Invoke(ok, res != null ? res.sequence : null, msg);
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
            return GMATParameterRegistry.SanitizeGmatIdentifier(rawName, fallback);
        }

        private static MissionResult ParseMissionResult(string json) {
            MissionResult res = new MissionResult();
            try {
                res.success = json.Contains("\"success\": true");
                res.error = ExtractString(json, "error");
                res.error_details = ExtractString(json, "error_details");
                res.gmat_log = ExtractString(json, "gmat_log");
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
                        List<string> items = SplitJsonObjects(repSection);
                        foreach (string block in items) {
                            if (block.Contains("\"step_index\":") || block.Contains("\"step_label\":")) {
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
                    if (endIdx > burnsIdx) {
                        string burnsSection = json.Substring(burnsIdx, endIdx - burnsIdx + 1);
                        List<string> items = SplitJsonObjects(burnsSection);
                        foreach (string item in items) {
                            if (item.Contains("\"type\":")) {
                                SolvedBurn b = new SolvedBurn();
                                b.type = ExtractString(item, "type");
                                b.name = ExtractString(item, "name");
                                b.origin = ExtractString(item, "origin");
                                b.dv_v = ExtractDouble(item, "dv_v");
                                b.dv_n = ExtractDouble(item, "dv_n");
                                b.dv_b = ExtractDouble(item, "dv_b");
                                b.magnitude = ExtractDouble(item, "magnitude");
                                b.duration = ExtractDouble(item, "duration");
                                b.thrust_kn = ExtractDouble(item, "thrust_kn");
                                b.isp = ExtractDouble(item, "isp");
                                b.fuel_burned = ExtractDouble(item, "fuel_burned");
                                b.epoch_offset = ExtractDouble(item, "epoch_offset");
                                b.burn_x = ExtractDouble(item, "burn_x");
                                b.burn_y = ExtractDouble(item, "burn_y");
                                b.burn_z = ExtractDouble(item, "burn_z");
                                b.burn_vx = ExtractDouble(item, "burn_vx");
                                b.burn_vy = ExtractDouble(item, "burn_vy");
                                b.burn_vz = ExtractDouble(item, "burn_vz");
                                b.has_native_delta_v = item.Contains("\"has_native_delta_v\": true");
                                b.principia_writeable = item.Contains("\"principia_writeable\": true");
                                b.native_result_note = ExtractString(item, "native_result_note");
                                res.solved_burns.Add(b);
                            }
                        }
                    }
                }

                // Parse updated sequence with real-time solved parameters
                int seqIdx = json.IndexOf("\"updated_sequence\":");
                if (seqIdx >= 0) {
                    int endSeq = json.IndexOf("]", seqIdx);
                    if (endSeq > seqIdx) {
                        string seqSection = json.Substring(seqIdx, endSeq - seqIdx + 1);
                        List<string> sItems = SplitJsonObjects(seqSection);
                        foreach (string sItem in sItems) {
                            if (sItem.Contains("\"type\":")) {
                                MissionStep step = new MissionStep();
                                step.type = ExtractString(sItem, "type");
                                step.duration = ExtractDouble(sItem, "duration");
                                step.dv_v = ExtractDouble(sItem, "dv_v");
                                step.dv_n = ExtractDouble(sItem, "dv_n");
                                step.dv_b = ExtractDouble(sItem, "dv_b");
                                step.label = ExtractString(sItem, "label");
                                step.target_goal = ExtractString(sItem, "target_goal");
                                if (string.IsNullOrEmpty(step.target_goal)) step.target_goal = "Apoapsis";
                                step.solved_value = ExtractDouble(sItem, "solved_value");
                                step.solved_value_str = ExtractString(sItem, "solved_value_str");
                                step.has_solved_value = ExtractBool(sItem, "has_solved_value") || !string.IsNullOrEmpty(step.solved_value_str);
                                step.achieved_value = ExtractDouble(sItem, "achieved_value");
                                step.achieved_variance = ExtractDouble(sItem, "achieved_variance");
                                step.achieve_passed = ExtractBool(sItem, "achieve_passed");
                                step.has_achieve_result = ExtractBool(sItem, "has_achieve_result") || (step.achieved_value != 0.0);
                                step.solver_iterations = (int)ExtractDouble(sItem, "solver_iterations");
                                step.solver_converged = ExtractBool(sItem, "solver_converged");
                                res.updated_sequence.Add(step);
                            }
                        }
                    }
                }

                res.all_solvers_converged = !json.Contains("\"all_solvers_converged\": false");
                if (!res.all_solvers_converged) res.success = false;

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

        private static List<string> SplitJsonObjects(string section) {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(section)) return result;

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < section.Length; i++) {
                char c = section[i];

                if (escape) {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString) {
                    escape = true;
                    continue;
                }

                if (c == '"') {
                    inString = !inString;
                    continue;
                }

                if (!inString) {
                    if (c == '{') {
                        if (depth == 0) start = i;
                        depth++;
                    } else if (c == '}') {
                        depth--;
                        if (depth == 0 && start >= 0) {
                            result.Add(section.Substring(start, i - start + 1));
                            start = -1;
                        }
                    }
                }
            }

            return result;
        }
    
        public static string EscapeJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }

        public static string SanitizeGmatScript(string rawScript) {
            return KSPGMAT.Model.GMATStateManager.SanitizeGmatScript(rawScript);
        }
    }
}
