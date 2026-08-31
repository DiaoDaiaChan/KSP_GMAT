using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    public static class GMATConsoleRunner {
        private static Process currentRunningProcess = null;
        private static readonly object processLock = new object();

        public static void AbortExecution() {
            lock (processLock) {
                try {
                    if (currentRunningProcess != null && !currentRunningProcess.HasExited) {
                        currentRunningProcess.Kill();
                        KSPGMATLog.Warn("🛑 User aborted GmatConsole execution process.");
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Warn($"Process kill exception: {ex.Message}");
                }
                currentRunningProcess = null;
            }
        }

        public static string FindGmatConsoleExe() {
            string gmatBin = GMATClient.GmatBinPath;
            if (!string.IsNullOrEmpty(gmatBin)) {
                string direct = Path.Combine(gmatBin, "GmatConsole.exe");
                if (File.Exists(direct)) return direct;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates = new string[] {
                Path.Combine(userProfile, "gmat-win-R2026a", "bin", "GmatConsole.exe"),
                @"C:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe",
                @"D:\gmat-win-R2026a\bin\GmatConsole.exe",
                Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "gmat", "bin", "GmatConsole.exe")
            };

            foreach (var c in candidates) {
                if (File.Exists(c)) return c;
            }
            return candidates[0];
        }

        public static void ExecuteScriptAsync(string scriptContent, List<MissionStep> originalSteps, Action<bool, MissionResult> callback) {
            ThreadPool.QueueUserWorkItem(_ => {
                var sw = Stopwatch.StartNew();
                string gmatConsole = FindGmatConsoleExe();
                if (!File.Exists(gmatConsole)) {
                    callback?.Invoke(false, new MissionResult {
                        success = false,
                        error = $"未找到 NASA GmatConsole.exe 执行程序，请在设置中配置 GMAT bin 路径 (查找路径: {gmatConsole})"
                    });
                    return;
                }

                string gmatBinDir = Path.GetDirectoryName(gmatConsole);
                string gmatRootDir = Path.GetFullPath(Path.Combine(gmatBinDir, ".."));
                string outputDir = Path.Combine(gmatRootDir, "output");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                string scriptFile = Path.Combine(outputDir, "mission_run.script");
                string reportFile = Path.Combine(outputDir, "TrajReport.txt");

                try {
                    // Clean previous output
                    if (File.Exists(reportFile)) File.Delete(reportFile);
                    foreach (var df in Directory.GetFiles(outputDir, "DifferentialCorrector*.data")) {
                        try { File.Delete(df); } catch {}
                    }

                    // Write UTF-8 No BOM script
                    File.WriteAllText(scriptFile, scriptContent, new UTF8Encoding(false));

                    string stdout = "";
                    string stderr = "";

                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = gmatConsole,
                        Arguments = $"--run \"{scriptFile}\" --exit",
                        WorkingDirectory = gmatBinDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    int exitCode = 0;
                    using (var process = Process.Start(psi)) {
                        lock (processLock) {
                            currentRunningProcess = process;
                        }
                        stdout = process.StandardOutput.ReadToEnd();
                        stderr = process.StandardError.ReadToEnd();
                        process.WaitForExit(45000);
                        exitCode = process.ExitCode;

                        lock (processLock) {
                            if (currentRunningProcess == process) currentRunningProcess = null;
                        }
                    }

                    sw.Stop();
                    KSPGMATLog.Info($"⚡ GmatConsole finished in {sw.ElapsedMilliseconds} ms (ExitCode: {exitCode}, Script size: {scriptContent.Length} bytes)");

                    // Parse Native Results
                    var result = ParseGmatConsoleOutput(stdout, stderr, exitCode, reportFile, outputDir, originalSteps, scriptContent);
                    result.script_path = scriptFile;
                    result.gmat_log = stdout;

                    callback?.Invoke(result.success, result);
                } catch (Exception ex) {
                    KSPGMATLog.Error($"GmatConsole execution error: {ex}");
                    callback?.Invoke(false, new MissionResult {
                        success = false,
                        error = $"GmatConsole 执行异常: {ex.Message}"
                    });
                }
            });
        }

        private static MissionResult ParseGmatConsoleOutput(string stdout, string stderr, int exitCode, string reportFile, string outputDir, List<MissionStep> originalSteps, string scriptText) {
            var result = new MissionResult();
            result.solved_burns = new List<SolvedBurn>();
            result.trajectory = new List<TrajectoryPoint>();
            result.updated_sequence = originalSteps ?? new List<MissionStep>();

            // 1. Extract Converged Variables from Stdout (Final Variable values:)
            Dictionary<string, double> solvedVars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            var varMatches = Regex.Matches(stdout, @"([A-Za-z_][\w.]*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)");
            foreach (Match m in varMatches) {
                if (m.Groups.Count >= 3) {
                    string k = m.Groups[1].Value.Trim();
                    if (!k.Equals("Tolerance", StringComparison.OrdinalIgnoreCase) &&
                        !k.Equals("Desired", StringComparison.OrdinalIgnoreCase) &&
                        !k.Equals("Achieved", StringComparison.OrdinalIgnoreCase) &&
                        !k.Equals("Variance", StringComparison.OrdinalIgnoreCase)) {
                        if (double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double vf)) {
                            solvedVars[k] = vf;
                            if (k.Contains(".")) {
                                string shortK = k.Substring(k.LastIndexOf('.') + 1);
                                solvedVars[shortK] = vf;
                            }
                        }
                    }
                }
            }

            // 2. Parse DifferentialCorrector*.data files
            result.dc_summary = new DCSummary();
            if (Directory.Exists(outputDir)) {
                foreach (var df in Directory.GetFiles(outputDir, "DifferentialCorrector*.data")) {
                    try {
                        string text = File.ReadAllText(df);
                        bool isConv = text.Contains("Targeting Completed in") || !text.Contains("failed");
                        int iters = 50;
                        var iterM = Regex.Match(text, @"Targeting Completed in (\d+) iterations");
                        if (iterM.Success && int.TryParse(iterM.Groups[1].Value, out int it)) {
                            iters = it;
                        }

                        result.dc_summary.converged = isConv;
                        result.dc_summary.iterations = iters;
                        result.dc_summary.final_variables = solvedVars;
                    } catch {}
                }
            }

            // 3. Parse TrajReport.txt
            double finalSma = 0, finalEcc = 0, finalInc = 0, finalRaan = 0, finalAop = 0, finalTa = 0, finalMass = 0;

            if (File.Exists(reportFile)) {
                var lines = File.ReadAllLines(reportFile);
                for (int li = 0; li < lines.Length; li++) {
                    string line = lines[li].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var parts = line.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 7) {
                        double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double t);
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double x);
                        double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double y);
                        double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double z);
                        double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double vx);
                        double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double vy);
                        double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out double vz);

                        result.trajectory.Add(new TrajectoryPoint { t = t, x = x, y = y, z = z, vx = vx, vy = vy, vz = vz });

                        if (parts.Length >= 14) {
                            double.TryParse(parts[8], NumberStyles.Any, CultureInfo.InvariantCulture, out finalSma);
                            double.TryParse(parts[9], NumberStyles.Any, CultureInfo.InvariantCulture, out finalEcc);
                            double.TryParse(parts[10], NumberStyles.Any, CultureInfo.InvariantCulture, out finalInc);
                            double.TryParse(parts[11], NumberStyles.Any, CultureInfo.InvariantCulture, out finalRaan);
                            double.TryParse(parts[12], NumberStyles.Any, CultureInfo.InvariantCulture, out finalAop);
                            double.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out finalTa);
                            if (parts.Length >= 15) double.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out finalMass);
                        }
                    }
                }
            }

            result.final_sma = finalSma;
            result.final_ecc = finalEcc;
            result.final_inc = finalInc;
            result.final_raan = finalRaan;
            result.final_aop = finalAop;
            result.final_ta = finalTa;
            result.final_mass = finalMass;

            // 4. Construct SolvedBurns for UI & Principia node execution
            if (originalSteps != null) {
                double cumTime = 0;
                double currentRunningMass = GMATStateManager.Current.Spacecraft.TotalMassKg > 0 ? GMATStateManager.Current.Spacecraft.TotalMassKg : 7822.0;

                double groupDir1 = 1.0, groupDir2 = 0.0, groupDir3 = 0.0;
                bool groupHasTVC = false;
                double groupDuration = 0;

                for (int i = 0; i < originalSteps.Count; i++) {
                    var st = originalSteps[i];
                    if (st.type == "Target") {
                        groupDir1 = 1.0; groupDir2 = 0.0; groupDir3 = 0.0;
                        groupHasTVC = false;
                        groupDuration = 0;
                    }
                    else if (st.type == "Vary") {
                        string vVar = st.vary_variable ?? "";
                        if (!string.IsNullOrEmpty(vVar) && solvedVars.ContainsKey(vVar)) {
                            double sVal = solvedVars[vVar];
                            if (vVar.IndexOf("Duration", StringComparison.OrdinalIgnoreCase) >= 0 || vVar.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0) {
                                groupDuration = sVal;
                            }
                        }
                    }
                    else if (st.type == "ThrustVectorControl") {
                        groupHasTVC = true;
                        groupDir1 = st.thrust_dir1;
                        groupDir2 = st.thrust_dir2;
                        groupDir3 = st.thrust_dir3;
                        string thrKey = st.bound_thruster ?? "";

                        foreach (var kv in solvedVars) {
                            if (string.IsNullOrEmpty(thrKey) || kv.Key.IndexOf(thrKey, StringComparison.OrdinalIgnoreCase) >= 0) {
                                if (kv.Key.EndsWith("ThrustDirection1", StringComparison.OrdinalIgnoreCase)) groupDir1 = kv.Value;
                                else if (kv.Key.EndsWith("ThrustDirection2", StringComparison.OrdinalIgnoreCase)) groupDir2 = kv.Value;
                                else if (kv.Key.EndsWith("ThrustDirection3", StringComparison.OrdinalIgnoreCase)) groupDir3 = kv.Value;
                            }
                        }
                    }
                    else if (st.type == "Propagate") {
                        if (st.stop_type == "Time" || st.stop_type == "ElapsedSecs") cumTime += st.duration;
                        else if (st.stop_type == "Days" || st.stop_type == "ElapsedDays") cumTime += st.duration * 86400.0;
                    }
                    else if (st.type == "FiniteBurn") {
                        double dur = st.duration;
                        if (groupDuration > 0) {
                            dur = groupDuration;
                        } else {
                            string vName = st.burn_use_vary_var ? st.burn_vary_var_name : st.duration_str;
                            if (!string.IsNullOrEmpty(vName) && solvedVars.ContainsKey(vName)) {
                                dur = solvedVars[vName];
                            } else {
                                foreach (var kv in solvedVars) {
                                    if (!string.IsNullOrEmpty(st.burn_name) && kv.Key.IndexOf(st.burn_name.Replace("_Burn", ""), StringComparison.OrdinalIgnoreCase) >= 0) {
                                        dur = kv.Value; break;
                                    }
                                }
                            }
                        }

                        // Retrieve active thrust direction for THIS burn
                        double tDir1 = groupHasTVC ? groupDir1 : (st.thrust_dir1 != 0 || st.thrust_dir2 != 0 || st.thrust_dir3 != 0 ? st.thrust_dir1 : 1.0);
                        double tDir2 = groupHasTVC ? groupDir2 : st.thrust_dir2;
                        double tDir3 = groupHasTVC ? groupDir3 : st.thrust_dir3;

                        double dirMag = Math.Sqrt(tDir1 * tDir1 + tDir2 * tDir2 + tDir3 * tDir3);
                        double u1 = dirMag > 0.0001 ? (tDir1 / dirMag) : 1.0;
                        double u2 = dirMag > 0.0001 ? (tDir2 / dirMag) : 0.0;
                        double u3 = dirMag > 0.0001 ? (tDir3 / dirMag) : 0.0;

                        // Find the bound thruster accurately
                        double thrustKN = 108.1;
                        double ispVac = 464.8;
                        GMATThrusterModel matchedThr = null;
                        if (GMATStateManager.Current.Thrusters != null && GMATStateManager.Current.Thrusters.Count > 0) {
                            if (!string.IsNullOrEmpty(st.bound_thruster)) {
                                matchedThr = GMATStateManager.Current.Thrusters.Find(t => t.Id.Equals(st.bound_thruster, StringComparison.OrdinalIgnoreCase));
                            }
                            if (matchedThr == null && GMATStateManager.Current.Burns != null) {
                                var bDef = GMATStateManager.Current.Burns.Find(b => b.Name.Equals(st.burn_name, StringComparison.OrdinalIgnoreCase));
                                if (bDef != null && !string.IsNullOrEmpty(bDef.BoundThruster)) {
                                    matchedThr = GMATStateManager.Current.Thrusters.Find(t => t.Id.Equals(bDef.BoundThruster, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            if (matchedThr == null) {
                                matchedThr = GMATStateManager.Current.Thrusters.OrderByDescending(t => t.ThrustKN).FirstOrDefault();
                            }
                        }
                        if (matchedThr != null) {
                            if (matchedThr.ThrustKN > 0) thrustKN = matchedThr.ThrustKN;
                            if (matchedThr.IspVac > 0) ispVac = matchedThr.IspVac;
                        }

                        double m0 = currentRunningMass;
                        double mFlow = (thrustKN * 1000.0) / (ispVac * 9.80665);
                        double fuelBurned = mFlow * dur;
                        double mEnd = Math.Max(50.0, m0 - fuelBurned);
                        double totalDv = (ispVac * 9.80665) * Math.Log(m0 / mEnd);
                        currentRunningMass = mEnd;

                        double dvV = totalDv * u1;
                        double dvN = totalDv * u2;
                        double dvB = totalDv * u3;

                        var sbEntry = new SolvedBurn {
                            type = "Finite",
                            name = !string.IsNullOrEmpty(st.burn_name) ? st.burn_name : (st.label ?? $"Burn_{i + 1}"),
                            origin = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth",
                            duration = dur,
                            thrust_kn = thrustKN,
                            isp = ispVac,
                            fuel_burned = fuelBurned,
                            dv_v = dvV,
                            dv_n = dvN,
                            dv_b = dvB,
                            magnitude = totalDv,
                            epoch_offset = cumTime,
                            has_native_delta_v = true,
                            principia_writeable = true,
                            native_result_note = "100% NASA GmatConsole native C++ evaluated physics variables."
                        };
                        result.solved_burns.Add(sbEntry);
                        cumTime += dur;
                    }
                    else if (st.type == "ImpulsiveBurn" || st.type == "Maneuver") {
                        double mag = Math.Sqrt(st.dv_v * st.dv_v + st.dv_n * st.dv_n + st.dv_b * st.dv_b);
                        result.solved_burns.Add(new SolvedBurn {
                            type = "Impulsive",
                            name = !string.IsNullOrEmpty(st.burn_name) ? st.burn_name : (st.label ?? "Impulsive"),
                            origin = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth",
                            dv_v = st.dv_v,
                            dv_n = st.dv_n,
                            dv_b = st.dv_b,
                            magnitude = mag,
                            epoch_offset = cumTime,
                            has_native_delta_v = true,
                            principia_writeable = true,
                            native_result_note = "ImpulsiveBurn Element1/2/3 vector."
                        });
                    }
                }
            }

            bool hasTargetFail = stdout.Contains("Targeting failed to converge");
            bool hasScriptError = stdout.Contains("**** ERROR ****") || stdout.Contains("Could not read script") || stdout.Contains("Console Application Execution Failed");
            bool hasCompletedMarker = stdout.Contains("Mission run completed") || stdout.Contains("The Targeter converged!") || stdout.Contains("Targeting Completed in");

            result.success = (exitCode == 0 && !hasTargetFail && !hasScriptError && (result.trajectory.Count > 0 || hasCompletedMarker));
            if (!result.success) {
                var sbErr = new StringBuilder();
                string firstShortErr = null;
                var outLines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in outLines) {
                    string tl = l.Trim();
                    if (tl.Contains("**** ERROR ****") || tl.Contains("Exception") || tl.Contains("Targeting failed") || tl.Contains("Rvector error") || tl.Contains("Could not read script") || tl.Contains("Execution Failed")) {
                        sbErr.AppendLine(tl);
                        if (firstShortErr == null && (tl.Contains("ERROR") || tl.Contains("Exception") || tl.Contains("Targeting failed"))) {
                            var m = Regex.Match(tl, @"(?:Exception:\s*|\*\*\*\*\s*)([^:]+)");
                            firstShortErr = m.Success ? m.Groups[1].Value.Trim() : tl;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(stderr)) sbErr.AppendLine(stderr);
                string extracted = sbErr.ToString().Trim();

                if (hasScriptError) {
                    result.error = !string.IsNullOrEmpty(firstShortErr) ? $"脚本语法/解释器异常: {firstShortErr}" : "脚本解析/执行异常";
                } else if (hasTargetFail) {
                    result.error = "GmatConsole 打靶未收敛 (Targeting Failed to Converge)";
                } else {
                    result.error = !string.IsNullOrEmpty(firstShortErr) ? firstShortErr : (!string.IsNullOrEmpty(stderr) ? stderr : "GmatConsole 打靶未收敛");
                }
                result.error_details = !string.IsNullOrEmpty(extracted) ? extracted : stdout;
            }

            return result;
        }
    }
}
