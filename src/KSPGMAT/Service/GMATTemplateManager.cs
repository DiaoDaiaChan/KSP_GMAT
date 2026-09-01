using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KSPGMAT {
    /// <summary>
    /// 单个任务模板元数据项 (Template Metadata)
    /// </summary>
    public class GMATTemplateInfo {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string DisplayTitle { get; set; }
        public string Icon { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public int EstimatedSteps { get; set; }
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// GMAT 任务预设模板动态扫描与管理服务
    /// (Elastic Dynamic Script Template Scanner & Manager)
    /// </summary>
    public static class GMATTemplateManager {
        private static List<GMATTemplateInfo> _cachedTemplates = new List<GMATTemplateInfo>();
        private static float _lastScanTime = 0f;
        private const float AutoRefreshInterval = 5f; // Auto re-scan every 5s if requested

        public static List<GMATTemplateInfo> GetTemplates(bool forceRefresh = false) {
            if (forceRefresh || _cachedTemplates == null || _cachedTemplates.Count == 0 || (Time.realtimeSinceStartup - _lastScanTime > AutoRefreshInterval)) {
                RefreshTemplates();
            }
            return _cachedTemplates;
        }

        public static string GetPrimaryTemplatesDirectory() {
            // Priority 1: GameData/KSPGMAT/templates
            string gamedataDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "templates");
            if (Directory.Exists(gamedataDir)) return gamedataDir;

            // Priority 2: GMAT bin relative templates
            if (!string.IsNullOrEmpty(GMATClient.GmatBinPath)) {
                string gmatDir = Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "GameData", "KSPGMAT", "templates");
                if (Directory.Exists(gmatDir)) return gmatDir;
            }

            // Fallback: create primary
            try {
                if (!Directory.Exists(gamedataDir)) Directory.CreateDirectory(gamedataDir);
                return gamedataDir;
            } catch {
                return KSPUtil.ApplicationRootPath;
            }
        }

        public static void RefreshTemplates() {
            var list = new List<GMATTemplateInfo>();
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var searchDirs = new List<string> {
                Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "templates"),
                Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "scripts", "templates"),
                Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPGMAT", "scripts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KSPGMAT", "templates")
            };

            if (!string.IsNullOrEmpty(GMATClient.GmatBinPath)) {
                searchDirs.Add(Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "GameData", "KSPGMAT", "templates"));
                searchDirs.Add(Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "scripts", "templates"));
                searchDirs.Add(Path.Combine(GMATClient.GmatBinPath, "KSP_GMAT", "scripts"));
            }

            foreach (var dir in searchDirs) {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                try {
                    var files = Directory.GetFiles(dir, "*.script", SearchOption.AllDirectories);
                    foreach (var file in files) {
                        string fileName = Path.GetFileName(file);
                        if (seenFiles.Contains(fileName)) continue;
                        seenFiles.Add(fileName);

                        var info = ParseTemplateHeader(file);
                        if (info != null) {
                            list.Add(info);
                        }
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Warn($"[GMATTemplateManager] Error scanning templates in {dir}: {ex.Message}");
                }
            }

            // Sort templates naturally by name
            _cachedTemplates = list.OrderBy(t => t.FileName).ToList();
            _lastScanTime = Time.realtimeSinceStartup;
            KSPGMATLog.Info($"[GMATTemplateManager] Discovered {_cachedTemplates.Count} dynamic script templates across search paths.");
        }

        private static GMATTemplateInfo ParseTemplateHeader(string filePath) {
            try {
                var fi = new FileInfo(filePath);
                string fileName = fi.Name;
                string title = null;
                string desc = null;
                int stepCount = 0;

                string[] lines = File.ReadAllLines(filePath);
                for (int i = 0; i < Math.Min(lines.Length, 35); i++) {
                    string line = lines[i].Trim();
                    if (line.StartsWith("%")) {
                        string comment = line.TrimStart('%', '-', '=', ' ').Trim();
                        if (string.IsNullOrEmpty(comment)) continue;

                        if (title == null && (comment.Contains("Preset") || comment.Contains("Title") || comment.Contains("Mission") || comment.Contains("Standard") || comment.Contains("LEO") || comment.Contains("Apollo") || comment.Contains("ChangE") || comment.Contains("NRHO") || comment.Contains("Mars"))) {
                            title = CleanCommentTitle(comment);
                        } else if (desc == null && comment.Length > 10 && !comment.StartsWith("---")) {
                            desc = comment;
                        }
                    } else if (line.StartsWith("Propagate ") || line.StartsWith("BeginFiniteBurn ") || line.StartsWith("Maneuver ") || line.StartsWith("Target ") || line.StartsWith("Achieve ")) {
                        stepCount++;
                    }
                }

                if (string.IsNullOrEmpty(title)) {
                    title = FormatFallbackTitle(fileName);
                }

                string icon = "📜";
                string category = "通用";

                string lower = (fileName + " " + title).ToLowerInvariant();
                if (lower.Contains("moon") || lower.Contains("lunar") || lower.Contains("change") || lower.Contains("apollo") || lower.Contains("nrho") || lower.Contains("halo") || lower.Contains("tli") || lower.Contains("loi")) {
                    icon = "🌕"; category = "地月系";
                } else if (lower.Contains("mars") || lower.Contains("tmi") || lower.Contains("hohmann") || lower.Contains("interplanetary") || lower.Contains("venus") || lower.Contains("jupiter")) {
                    icon = "🔴"; category = "深空/行星际";
                } else if (lower.Contains("geo") || lower.Contains("gto") || lower.Contains("leo") || lower.Contains("earth") || lower.Contains("orbit")) {
                    icon = "🚀"; category = "近地/地球";
                } else if (lower.Contains("test") || lower.Contains("offset") || lower.Contains("dual") || lower.Contains("burns")) {
                    icon = "🧪"; category = "测试/验证";
                }

                return new GMATTemplateInfo {
                    FileName = fileName,
                    FullPath = filePath,
                    DisplayTitle = title,
                    Icon = icon,
                    Category = category,
                    Description = desc ?? $"标准预设模板: {fileName}",
                    EstimatedSteps = Math.Max(1, stepCount),
                    LastModified = fi.LastWriteTime
                };
            } catch {
                return null;
            }
        }

        private static string CleanCommentTitle(string comment) {
            string cleaned = Regex.Replace(comment, @"^(GMAT Verification Preset:|NASA GMAT Mission Script -|NASA Official Sample:|Mission Preset:)\s*", "", RegexOptions.IgnoreCase).Trim();
            return cleaned.Length > 40 ? cleaned.Substring(0, 37) + "..." : cleaned;
        }

        private static string FormatFallbackTitle(string fileName) {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            // Replace underscores with spaces
            string clean = nameWithoutExt.Replace('_', ' ');
            // Format numbers like 01 -> 1.
            clean = Regex.Replace(clean, @"^0*(\d+)\s*", "$1. ");
            return clean;
        }

        public static void OpenTemplatesFolder() {
            try {
                string dir = GetPrimaryTemplatesDirectory();
                if (Directory.Exists(dir)) {
                    System.Diagnostics.Process.Start("explorer.exe", dir);
                }
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage($"❌ 打开文件夹失败: {ex.Message}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
