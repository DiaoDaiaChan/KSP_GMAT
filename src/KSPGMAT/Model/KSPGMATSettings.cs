using System;
using System.IO;
using System.Text.RegularExpressions;

namespace KSPGMAT {
    public static class KSPGMATSettings {
        private static string root;
        private static string spiceBspPath;
        private static string configText;
        private static LogLevel? logLevel;
        private static bool? logFileEnabled;
        private static string logFilePath;

        public static string ConfigPath {
            get {
                string kspRoot = GetApplicationRootPath();
                if (!string.IsNullOrEmpty(kspRoot)) {
                    return Path.Combine(kspRoot, "GameData", "KSPGMAT", "Config", "backend.cfg");
                }
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "KSPGMAT", "Config", "backend.cfg");
            }
        }

        private static string GetApplicationRootPath() {
            try {
                Type t = Type.GetType("KSPUtil, Assembly-CSharp");
                if (t == null) {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                        t = asm.GetType("KSPUtil");
                        if (t != null) break;
                    }
                }
                if (t != null) {
                    var prop = t.GetProperty("ApplicationRootPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop != null) {
                        string val = prop.GetValue(null, null) as string;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            } catch { }
            return null;
        }

        public static string RootPath {
            get {
                if (root == null) root = Read("gmat_root", "");
                return root;
            }
            set {
                root = value;
                Write("gmat_root", value);
            }
        }

        public static string SpiceBspPath {
            get {
                if (spiceBspPath == null) spiceBspPath = Read("spice_bsp_path", "");
                return spiceBspPath;
            }
            set {
                spiceBspPath = value;
                Write("spice_bsp_path", value);
                try {
                    if (!string.IsNullOrEmpty(value) && File.Exists(value) && KSPGMAT.MGA.PykepNative.IsAvailable) {
                        KSPGMAT.MGA.PykepNative.LoadSpiceKernel(value);
                    }
                } catch { }
            }
        }

        public static LogLevel LogLevel {
            get {
                if (!logLevel.HasValue) {
                    string val = Read("log_level", "Info");
                    if (Enum.TryParse<LogLevel>(val, true, out var parsed)) {
                        logLevel = parsed;
                    } else {
                        logLevel = LogLevel.Info;
                    }
                }
                return logLevel.Value;
            }
            set {
                logLevel = value;
                Write("log_level", value.ToString());
                try { KSPGMATLog.SetLogLevel(value); } catch { }
            }
        }

        public static bool LogFileEnabled {
            get {
                if (!logFileEnabled.HasValue) {
                    string val = Read("log_file_enabled", "true");
                    logFileEnabled = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
                }
                return logFileEnabled.Value;
            }
            set {
                logFileEnabled = value;
                Write("log_file_enabled", value ? "true" : "false");
                try { KSPGMATLog.SetFileLoggingEnabled(value); } catch { }
            }
        }

        public static string LogFilePath {
            get {
                if (logFilePath == null) {
                    logFilePath = Read("log_file_path", "");
                }
                return logFilePath;
            }
            set {
                logFilePath = value ?? "";
                Write("log_file_path", logFilePath);
            }
        }

        public static string OutputPath { get { return Path.Combine(RootPath, "output"); } }

        public static void Reload() {
            configText = null;
            root = null;
            spiceBspPath = null;
            logLevel = null;
            logFileEnabled = null;
            logFilePath = null;
        }

        private static string Read(string key, string fallback) {
            if (configText == null) {
                try { configText = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : ""; }
                catch { configText = ""; }
            }
            Match match = Regex.Match(configText, "^\\s*" + Regex.Escape(key) + "\\s*=\\s*(.*?)\\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim().Trim('"') : fallback;
        }

        private static void Write(string key, string value) {
            try {
                string current = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "";
                string pattern = "^\\s*" + Regex.Escape(key) + "\\s*=.*$";
                string newLine = $"{key}={value}";
                string updated;
                if (Regex.IsMatch(current, pattern, RegexOptions.Multiline)) {
                    updated = Regex.Replace(current, pattern, newLine, RegexOptions.Multiline);
                } else {
                    updated = string.IsNullOrEmpty(current) ? newLine : current.TrimEnd() + Environment.NewLine + newLine;
                }
                File.WriteAllText(ConfigPath, updated);
                configText = updated;
            } catch {}
        }
    }
}
