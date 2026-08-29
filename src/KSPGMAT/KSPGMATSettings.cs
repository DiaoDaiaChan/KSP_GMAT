using System;
using System.IO;
using System.Text.RegularExpressions;

namespace KSPGMAT {
    public static class KSPGMATSettings {
        private static string root;
        private static string configText;

        public static string ConfigPath {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "Config", "backend.cfg"); }
        }

        public static string RootPath {
            get {
                if (root == null) root = Read("gmat_root", "");
                return root;
            }
        }

        public static string ServiceScriptPath { get { return Path.Combine(RootPath, "gmat_service", "gmat_bridge_server.py"); } }
        public static string OutputPath { get { return Path.Combine(RootPath, "output"); } }

        private static string Read(string key, string fallback) {
            if (configText == null) {
                try { configText = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : ""; }
                catch { configText = ""; }
            }
            Match match = Regex.Match(configText, "^\\s*" + Regex.Escape(key) + "\\s*=\\s*(.*?)\\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim().Trim('"') : fallback;
        }
    }
}
