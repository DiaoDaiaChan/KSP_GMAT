using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KSPGMAT {
    /// <summary>
    /// Decoupled Internationalization (i18n) Engine for KSP-GMAT.
    /// Provides clean, pure UI text without bilingual clutter or obscure internal remarks.
    ///
    /// Translation tables are loaded from external JSON files at runtime (GameData/KSPGMAT/loc/*.json).
    /// Language registration is dynamic: dropping a new <code>ru.json</code> into the loc/ folder
    /// automatically registers the Russian locale with no code changes.
    /// </summary>
    public static class I18n {
        /// <summary>Core doc-language used for binary EN/other decisions (parameter registry etc.).</summary>
        public enum Lang { ZH, EN }

        /// <summary>Core doc language (EN vs non-EN fallback for parameter descriptions/categories).</summary>
        public static Lang Current = Lang.ZH;

        /// <summary>Active fine-grained language code (e.g. "zh", "en", "ru").</summary>
        public static string CurrentCode = "zh";

        // langCode -> key -> localized text (loaded from loc/*.json)
        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _codes = new List<string>();
        private static readonly List<string> _orbKeys = new List<string>();
        private static string _lastFingerprint = "";

        /// <summary>Registered language codes in UI order (zh, en first, then alphabetical).</summary>
        public static IReadOnlyList<string> AvailableLanguages => _codes;

        /// <summary>Normalized orbital-mechanics term suffixes (orb_* keys) for fuzzy badge matching.</summary>
        public static IReadOnlyList<string> OrbKeys => _orbKeys;

        static I18n() {
            Reload();
        }

        /// <summary>
        /// (Re)loads all language tables from the loc/ directory. Safe to call at any time;
        /// language switching stays hot because IMGUI re-reads I18n.T() every frame.
        /// </summary>
        public static void Reload() {
            try {
                _codes.Clear();
                Tables.Clear();
                _orbKeys.Clear();

                string locDir = LocateLocDirectory();
                if (locDir == null) {
                    Debug.LogWarning("[KSPGMAT] i18n loc/ directory not found; UI strings may fall back to raw keys.");
                    return;
                }

                foreach (string file in Directory.GetFiles(locDir, "*.json")) {
                    string code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    int dash = code.IndexOf('-');
                    if (dash > 0) code = code.Substring(0, dash);
                    if (string.IsNullOrEmpty(code)) continue;

                    string text = File.ReadAllText(file, Encoding.UTF8);
                    var map = ParseFlatJson(text);
                    if (map.Count == 0) continue;

                    Tables[code] = map;
                    if (!_codes.Contains(code)) _codes.Add(code);
                }

                // Stable UI order: zh, en first, then remaining codes alphabetically.
                _codes.Sort(StringComparer.Ordinal);
                var preferred = new List<string>();
                foreach (var c in new[] { "zh", "en" }) {
                    if (_codes.Remove(c)) preferred.Add(c);
                }
                _codes.InsertRange(0, preferred);

                // Collect orbital-mechanics term suffixes for GetParamBadgeI18n fuzzy matching
                // (iterate in UI order so matching stays deterministic).
                foreach (var code in _codes) {
                    if (!Tables.TryGetValue(code, out var table)) continue;
                    foreach (var key in table.Keys) {
                        if (key.StartsWith("orb_", StringComparison.OrdinalIgnoreCase) && key.Length > 4) {
                            string suffix = key.Substring(4);
                            if (!_orbKeys.Contains(suffix)) _orbKeys.Add(suffix);
                        }
                    }
                }

                // Keep the active language valid.
                if (!Tables.ContainsKey(CurrentCode)) {
                    if (Tables.ContainsKey("en")) SetLangCode("en");
                    else if (Tables.ContainsKey("zh")) SetLangCode("zh");
                }

                _lastFingerprint = ComputeFingerprint();
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] i18n Reload error: " + ex.Message);
            }
        }

        /// <summary>
        /// Lightweight fingerprint of the loc/ directory (file names + sizes).
        /// Used to detect newly added language files (e.g. ru.json) at runtime.
        /// </summary>
        private static string ComputeFingerprint() {
            try {
                string locDir = LocateLocDirectory();
                if (locDir == null) return "";
                string[] files = Directory.GetFiles(locDir, "*.json");
                Array.Sort(files, StringComparer.Ordinal);
                var sb = new StringBuilder();
                foreach (string f in files) {
                    sb.Append(Path.GetFileName(f)).Append(':').Append(new FileInfo(f).Length).Append(';');
                }
                return sb.ToString();
            } catch {
                return _lastFingerprint;
            }
        }

        /// <summary>
        /// Re-scans the loc/ directory only when its contents changed (new/modified language files).
        /// Safe to call on a timer; returns true if a reload actually happened.
        /// </summary>
        public static bool RescanIfChanged() {
            string fp = ComputeFingerprint();
            if (fp == _lastFingerprint) return false;
            Reload();
            return true;
        }

        /// <summary>Cycle to the next registered language (falls back to ZH/EN toggle).</summary>
        public static void Toggle() {
            int idx = _codes.IndexOf(CurrentCode);
            if (idx >= 0 && _codes.Count > 0) {
                SetLangCode(_codes[(idx + 1) % _codes.Count]);
            } else {
                SetLang((Current == Lang.ZH) ? Lang.EN : Lang.ZH);
            }
        }

        /// <summary>Switch to a core language (ZH -> "zh", EN -> "en").</summary>
        public static void SetLang(Lang lang) {
            Current = lang;
            CurrentCode = (lang == Lang.EN) ? "en" : "zh";
            try { GMATParameterRegistry.ReloadCategories(); } catch {}
        }

        /// <summary>
        /// Switch to any registered language code (e.g. "ru"). Unknown codes are ignored.
        /// Accepts full tags such as "zh-CN" / "en-US" and normalizes them to the base code.
        /// </summary>
        public static void SetLangCode(string code) {
            if (string.IsNullOrEmpty(code)) return;
            code = code.ToLowerInvariant();
            int dash = code.IndexOf('-');
            if (dash > 0) code = code.Substring(0, dash);
            if (!Tables.ContainsKey(code)) return;

            CurrentCode = code;
            Current = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? Lang.EN : Lang.ZH;
            try { GMATParameterRegistry.ReloadCategories(); } catch {}
        }

        /// <summary>Alias of T().</summary>
        public static string Get(string key) => T(key);

        /// <summary>Detect the best matching registered language from KSP locale, then system culture.</summary>
        public static void AutoDetect() {
            string code = DetectCode();
            if (Tables.ContainsKey(code)) { SetLangCode(code); return; }
            if (code.Length >= 2 && Tables.ContainsKey(code.Substring(0, 2))) { SetLangCode(code.Substring(0, 2)); return; }
            if (Tables.ContainsKey("en")) SetLangCode("en");
            else if (Tables.ContainsKey("zh")) SetLangCode("zh");
        }

        private static string DetectCode() {
            try {
                string kspLang = KSP.Localization.Localizer.CurrentLanguage;
                if (!string.IsNullOrEmpty(kspLang)) {
                    kspLang = kspLang.ToLowerInvariant();
                    if (kspLang.Contains("zh") || kspLang.Contains("chinese")) return "zh";
                    if (kspLang.Contains("ja") || kspLang.Contains("jp") || kspLang.Contains("japanese")) return Tables.ContainsKey("jp") ? "jp" : "ja";
                    if (kspLang.Contains("ru") || kspLang.Contains("russian")) return "ru";
                    if (kspLang.Contains("en") || kspLang.Contains("english")) return "en";
                    string first = kspLang.Split('-')[0].Trim();
                    if (first.Length == 2) return first;
                }
            } catch {}

            try {
                string ui = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
                if (ui.Contains("zh")) return "zh";
                if (ui.Contains("ja") || ui.Contains("jp")) return Tables.ContainsKey("jp") ? "jp" : "ja";
                if (ui.Contains("ru")) return "ru";
                string first = ui.Split('-')[0].Trim();
                if (first.Length == 2) return first;
            } catch {}

            return "en";
        }

        /// <summary>Native-language display name of a registered language (from __meta_native).</summary>
        public static string GetNativeName(string code) {
            if (string.IsNullOrEmpty(code)) return code;
            if (Tables.TryGetValue(code, out var t) && t.TryGetValue("__meta_native", out var n) && !string.IsNullOrEmpty(n)) {
                return n;
            }
            return code.ToUpperInvariant();
        }

        /// <summary>English display name of a registered language (from __meta_english).</summary>
        public static string GetEnglishName(string code) {
            if (string.IsNullOrEmpty(code)) return code;
            if (Tables.TryGetValue(code, out var t) && t.TryGetValue("__meta_english", out var n) && !string.IsNullOrEmpty(n)) {
                return n;
            }
            return code.ToUpperInvariant();
        }

        /// <summary>True if any loaded language table defines the key.</summary>
        public static bool HasKey(string key) {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var table in Tables.Values) {
                if (table.ContainsKey(key)) return true;
            }
            return false;
        }

        /// <summary>
        /// Get the localized string for the given key.
        /// Resolution order: current language -> English -> Chinese -> key itself.
        /// </summary>
        public static string T(string key, string fallback = null) {
            if (string.IsNullOrEmpty(key)) return "";

            if (Tables.TryGetValue(CurrentCode, out var table) && table.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val)) {
                return val;
            }
            if (!string.Equals(CurrentCode, "en", StringComparison.OrdinalIgnoreCase)
                && Tables.TryGetValue("en", out table) && table.TryGetValue(key, out val) && !string.IsNullOrEmpty(val)) {
                return val;
            }
            if (!string.Equals(CurrentCode, "zh", StringComparison.OrdinalIgnoreCase)
                && Tables.TryGetValue("zh", out table) && table.TryGetValue(key, out val) && !string.IsNullOrEmpty(val)) {
                return val;
            }
            return fallback ?? key;
        }

        /// <summary>
        /// Get localized string with format arguments (for string.Format patterns).
        /// Values keep C#/string.Format syntax ({{, {0}}) so formatting is identical.
        /// </summary>
        public static string Format(string key, params object[] args) {
            string fmt = T(key);
            try {
                return string.Format(fmt, args);
            } catch {
                return fmt;
            }
        }

        /// <summary>Alias for Format.</summary>
        public static string TF(string key, params object[] args) => Format(key, args);

        /// <summary>
        /// Format with semantic rich-text styling defined in language templates.
        /// Retains backwards compatibility with ColorFormat callers by directly formatting arguments.
        /// </summary>
        public static string ColorFormat(string key, params object[] args) {
            return Format(key, args);
        }

        // =============================================================================
        //  loc/ directory discovery & flat JSON parsing
        // =============================================================================

        private static string LocateLocDirectory() {
            string dllDir = "";
            try { dllDir = Path.GetDirectoryName(typeof(I18n).Assembly.Location); } catch {}
            string userProfile = "";
            try { userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch {}
            string kspRoot = "";
            try { kspRoot = KSPUtil.ApplicationRootPath; } catch {}

            string[] candidates = new string[] {
                !string.IsNullOrEmpty(dllDir) ? Path.Combine(dllDir, "loc") : "",
                !string.IsNullOrEmpty(dllDir) ? Path.Combine(dllDir, "..", "loc") : "",
                !string.IsNullOrEmpty(dllDir) ? Path.Combine(dllDir, "..", "GameData", "KSPGMAT", "loc") : "",
                !string.IsNullOrEmpty(dllDir) ? Path.Combine(dllDir, "GameData", "KSPGMAT", "loc") : "",
                !string.IsNullOrEmpty(kspRoot) ? Path.Combine(kspRoot, "GameData", "KSPGMAT", "loc") : "",
                !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "gmat-win-R2026a", "bin", "KSP_GMAT", "GameData", "KSPGMAT", "loc") : "",
                !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "gmat-win-R2026a", "bin", "KSP_GMAT", "loc") : ""
            };

            foreach (var p in candidates) {
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>
        /// Parses a flat JSON object {"key":"value", ...} into a dictionary.
        /// Handles escaped quotes and preserves string.Format placeholders ({0}, {{, }}).
        /// </summary>
        private static Dictionary<string, string> ParseFlatJson(string json) {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return map;
            try {
                int n = json.Length;
                int i = json.IndexOf('{');
                while (i >= 0 && i < n) {
                    int q = json.IndexOf('"', i);
                    if (q < 0) break;
                    int kStart = q + 1;
                    int kEnd = json.IndexOf('"', kStart);
                    if (kEnd < 0) break;
                    string key = json.Substring(kStart, kEnd - kStart);

                    int colon = json.IndexOf(':', kEnd);
                    if (colon < 0) break;
                    int v = colon + 1;
                    while (v < n && char.IsWhiteSpace(json[v])) v++;
                    if (v >= n || json[v] != '"') { i = colon + 1; continue; }
                    v++;

                    var sb = new StringBuilder();
                    while (v < n) {
                        char c = json[v];
                        if (c == '\\') {
                            if (v + 1 < n) {
                                char nxt = json[v + 1];
                                if (nxt == '"') sb.Append('"');
                                else if (nxt == '\\') sb.Append('\\');
                                else if (nxt == 'n') sb.Append('\n');
                                else if (nxt == 'r') sb.Append('\r');
                                else if (nxt == 't') sb.Append('\t');
                                else sb.Append(nxt);
                                v += 2;
                                continue;
                            }
                            v++;
                            continue;
                        }
                        if (c == '"') break;
                        sb.Append(c);
                        v++;
                    }

                    if (!string.IsNullOrEmpty(key)) {
                        map[key] = sb.ToString();
                    }
                    i = v + 1;
                }
            } catch {}
            return map;
        }
    }
}
