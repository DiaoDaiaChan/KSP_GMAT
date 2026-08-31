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

                        sw.Stop();
                        KSPGMATLog.Info($"⚡ GmatConsole finished in {sw.ElapsedMilliseconds} ms (ExitCode: {exitCode}, Script size: {scriptContent.Length} bytes)");

                        var result = GMATSolutionMapper.ParseAndMapResult(stdout, stderr, exitCode, reportFile, outputDir, originalSteps, scriptContent);

                        // Error formatting and diagnostics extraction
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

                        callback?.Invoke(result.success, result);
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Error($"GmatConsole execution error: {ex}");
                    callback?.Invoke(false, new MissionResult {
                        success = false,
                        error = $"GmatConsole 执行异常: {ex.Message}"
                    });
                }
            });
        }
    }
}
