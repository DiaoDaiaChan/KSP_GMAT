using System;
using System.IO;
using System.Text.RegularExpressions;

namespace KSPGMAT {
    public static class KSPGMATSettings {
        private static string root;
        private static string spiceBspPath;
        private static string configText;

        public static string ConfigPath {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "Config", "backend.cfg"); }
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

        public static string OutputPath { get { return Path.Combine(RootPath, "output"); } }

        public static void Reload() {
            configText = null;
            root = null;
            spiceBspPath = null;
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
