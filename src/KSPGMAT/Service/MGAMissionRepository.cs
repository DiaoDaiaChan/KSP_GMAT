using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// 统一行星际引力助推 (MGA) 任务配置文件仓储服务 (Repository Service)
    /// 管理本地 GameData/KSPGMAT/Missions 目录下全部统一 .json 任务配置文件
    /// </summary>
    public static class MGAMissionRepository {
        private static List<MGAMissionConfigFile> _cachedMissions = null;
        private static string _missionsDirOverride = null;

        public static void SetMissionsDirectoryOverride(string dir) {
            _missionsDirOverride = dir;
            _cachedMissions = null;
        }

        public static string GetMissionsDirectory() {
            if (!string.IsNullOrEmpty(_missionsDirOverride)) {
                if (!Directory.Exists(_missionsDirOverride)) Directory.CreateDirectory(_missionsDirOverride);
                return _missionsDirOverride;
            }

            string baseDir = null;
            try {
                if (KSPUtil.ApplicationRootPath != null) {
                    baseDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KSPGMAT/Missions");
                }
            } catch {
                // KSPUtil not available in offline test harness
            }

            if (string.IsNullOrEmpty(baseDir)) {
                string cur = Directory.GetCurrentDirectory();
                string candidate = Path.Combine(cur, "GameData", "KSPGMAT", "Missions");
                if (Directory.Exists(Path.Combine(cur, "GameData"))) {
                    baseDir = candidate;
                } else {
                    baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "KSPGMAT", "Missions");
                }
            }

            if (!Directory.Exists(baseDir)) {
                try {
                    Directory.CreateDirectory(baseDir);
                } catch (Exception ex) {
                    KSPGMATLog.Error("MissionRepo", "CreateDirectory error", ex);
                }
            }
            return baseDir;
        }

        /// <summary>
        /// 确保全部权威历史任务与经典构型在 GameData/KSPGMAT/Missions/*.json 中就绪
        /// </summary>
        public static void EnsureDefaultMissionFiles() {
            string dir = GetMissionsDirectory();
            if (!Directory.Exists(dir)) return;

            // 0. 清理历史遗留的纯英文旧命名及未校准的旧虚构任务文件
            string[] legacyFiles = new string[] {
                "BepiColombo_SEP_LowThrust.json",
                "Cassini_1DSM_1997.json",
                "Cassini_Huygens_1997.json",
                "Galileo_VEEGA_1989.json",
                "Juno_EEJ_Ballistic_2011.json",
                "Juno_EEJ_MGA1DSM.json",
                "Mariner_10_1973.json",
                "New_Horizons_2006.json",
                "Rosetta_EEMEB_2004.json",
                "Voyager_2_GrandTour_1977.json",
                "Solar_Chronos_太阳时空神号_2028.json",
                "Kronos-Poseidon_泰坦-海神双星巡航_2032.json",
                "Interstellar_Pioneer_星际先驱号_太阳系边界逃逸_2036.json",
                "Aldrin_Cycler_奥尔德林地火循环飞船_2033.json",
                "Ulysses_II_尤利西斯II号_太阳极区俯瞰_2030.json"
            };
            foreach (var leg in legacyFiles) {
                string legPath = Path.Combine(dir, leg);
                if (File.Exists(legPath)) {
                    try { File.Delete(legPath); } catch { }
                }
            }

            // 1. 标准历史任务集 (BenchmarkSuite)
            var histList = MGABenchmarkSuite.StandardHistoricalMissions;
            for (int i = 0; i < histList.Count; i++) {
                var bCase = histList[i];
                string cleanName = CleanFileName(bCase.MissionName);
                string path = Path.Combine(dir, $"{cleanName}.json");
                if (!File.Exists(path)) {
                    string icon = "🪐";
                    if (bCase.MissionName.IndexOf("Mariner", StringComparison.OrdinalIgnoreCase) >= 0) icon = "☀️";
                    else if (bCase.MissionName.IndexOf("Galileo", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🛰️";
                    else if (bCase.MissionName.IndexOf("Cassini", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🪐";
                    else if (bCase.MissionName.IndexOf("Voyager", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🌌";
                    else if (bCase.MissionName.IndexOf("Rosetta", StringComparison.OrdinalIgnoreCase) >= 0) icon = "☄️";
                    else if (bCase.MissionName.IndexOf("Horizons", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🛸";
                    else if (bCase.MissionName.IndexOf("Juno", StringComparison.OrdinalIgnoreCase) >= 0) icon = "⚡";
                    else if (bCase.MissionName.IndexOf("Bepi", StringComparison.OrdinalIgnoreCase) >= 0) icon = "⚡";

                    string category = "经典历史";
                    if (bCase.EngineMode == MGAEngineMode.DeepSpaceManeuver1DSM || bCase.EngineMode == MGAEngineMode.LowThrustSimsFlanagan || bCase.EngineMode == MGAEngineMode.HeterogeneousHybrid) {
                        category = "深空机动/低推力";
                    }

                    var cfg = MGAMissionConfigFile.FromBenchmarkCase(bCase, category, icon);
                    string json = JsonUtility.ToJson(cfg, true);
                    File.WriteAllText(path, json);
                }
            }

            // 1.2 虚构深空极限探测与经典跳板任务集 (经过 SPICE 真实星历高精物理校准)
            var fictList = MGABenchmarkSuite.StandardFictionalMissions;
            for (int i = 0; i < fictList.Count; i++) {
                var bCase = fictList[i];
                string cleanName = CleanFileName(bCase.MissionName);
                string path = Path.Combine(dir, $"{cleanName}.json");
                if (!File.Exists(path)) {
                    string icon = "🛸";
                    if (bCase.MissionName.IndexOf("Kronos", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🪐";
                    else if (bCase.MissionName.IndexOf("Solar", StringComparison.OrdinalIgnoreCase) >= 0) icon = "☀️";
                    else if (bCase.MissionName.IndexOf("Aldrin", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🚀";
                    else if (bCase.MissionName.IndexOf("Ulysses", StringComparison.OrdinalIgnoreCase) >= 0) icon = "🛰️";
                    else if (bCase.MissionName.IndexOf("Kerbin", StringComparison.OrdinalIgnoreCase) >= 0) icon = "✨";

                    string category = "虚构/深空极限探测";
                    if (bCase.MissionName.IndexOf("Kerbin", StringComparison.OrdinalIgnoreCase) >= 0) {
                        category = "坎巴拉/原版";
                    }

                    var cfg = MGAMissionConfigFile.FromBenchmarkCase(bCase, category, icon);
                    string json = JsonUtility.ToJson(cfg, true);
                    File.WriteAllText(path, json);
                }
            }

            // 2. 经典构型直达与原版巡游模版
            CreatePresetIfMissing(dir, "Earth_Mars_Hohmann_Direct.json", new MGAMissionConfigFile {
                mission_name = "地球-火星经典霍曼转移直达",
                category = "直达/内行星",
                description = "经典火星直接转移窗口，双脉冲脱离与捕获，单次朗伯转移",
                icon = "🚀",
                departure_body = "Earth",
                destination_body = "Mars",
                flyby_sequence = new List<string> { "Mars" },
                departure_pe_km = 200.0,
                destination_pe_km = 250.0,
                earliest_departure_date = "01 Jan 2024 12:00:00",
                latest_departure_date = "01 Jul 2024 12:00:00",
                tof_days = new List<double> { 210.0 },
                min_tof_days = new List<double> { 120.0 },
                max_tof_days = new List<double> { 350.0 }
            });

            CreatePresetIfMissing(dir, "Kerbin_Eve_Jool_GrandTour.json", new MGAMissionConfigFile {
                mission_name = "坎巴拉原版绿星-宿命大巡游",
                category = "坎巴拉/原版",
                description = "Kerbin-Eve-Kerbin-Jool 坎系经典行星际引力跳板架构",
                icon = "✨",
                departure_body = "Kerbin",
                destination_body = "Jool",
                flyby_sequence = new List<string> { "Eve", "Kerbin", "Jool" },
                departure_pe_km = 100.0,
                destination_pe_km = 500.0,
                earliest_departure_date = "01 Jan 2001 00:00:00",
                latest_departure_date = "01 Jan 2004 00:00:00",
                tof_days = new List<double> { 60.0, 90.0, 240.0 }
            });
        }

        private static void CreatePresetIfMissing(string dir, string fileName, MGAMissionConfigFile cfg) {
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path)) {
                string json = JsonUtility.ToJson(cfg, true);
                File.WriteAllText(path, json);
            }
        }

        public static string CleanFileName(string name) {
            if (string.IsNullOrEmpty(name)) return "Mission_Config";
            char[] invalids = Path.GetInvalidFileNameChars();
            string clean = new string(name.Select(ch => invalids.Contains(ch) ? '_' : ch).ToArray());
            return clean.Replace(" ", "_").Replace(":", "").Replace("->", "_to_").Replace("➔", "_to_").Replace("(", "").Replace(")", "").Replace(",", "");
        }

        /// <summary>
        /// 加载 Missions 目录下全部统一任务配置文件 (自动自愈与排序)
        /// </summary>
        public static List<MGAMissionConfigFile> LoadAllMissions(bool forceReload = false) {
            if (_cachedMissions != null && !forceReload) {
                return _cachedMissions;
            }

            EnsureDefaultMissionFiles();

            var uniqueMap = new Dictionary<string, MGAMissionConfigFile>(StringComparer.OrdinalIgnoreCase);
            string dir = GetMissionsDirectory();
            if (Directory.Exists(dir)) {
                var files = Directory.GetFiles(dir, "*.json");
                foreach (var f in files) {
                    try {
                        string json = File.ReadAllText(f);
                        var mission = JsonUtility.FromJson<MGAMissionConfigFile>(json);
                        if (mission != null && !string.IsNullOrEmpty(mission.mission_name)) {
                            // 自愈缺省类别
                            if (string.IsNullOrEmpty(mission.category)) mission.category = "经典历史";
                            if (string.IsNullOrEmpty(mission.icon)) mission.icon = "🪐";
                            
                            string key = mission.mission_name.Trim();
                            // 字典去重，若重复优先保留较长或更规范的文件（或者跳过重复项）
                            if (!uniqueMap.ContainsKey(key)) {
                                uniqueMap[key] = mission;
                            }
                        }
                    } catch (Exception ex) {
                        KSPGMATLog.Error("MissionRepo", $"Failed to read {f}", ex);
                    }
                }
            }

            var list = new List<MGAMissionConfigFile>(uniqueMap.Values);

            // 按类别与名称排序
            list.Sort((a, b) => {
                int catOrderA = GetCategoryOrder(a.category);
                int catOrderB = GetCategoryOrder(b.category);
                if (catOrderA != catOrderB) return catOrderA.CompareTo(catOrderB);
                return string.Compare(a.mission_name, b.mission_name, StringComparison.OrdinalIgnoreCase);
            });

            _cachedMissions = list;
            return _cachedMissions;
        }

        private static int GetCategoryOrder(string cat) {
            if (string.IsNullOrEmpty(cat)) return 99;
            if (cat.Contains("历史")) return 1;
            if (cat.Contains("直达") || cat.Contains("内行星")) return 2;
            if (cat.Contains("深空") || cat.Contains("外行星")) return 3;
            if (cat.Contains("低推力") || cat.Contains("机动")) return 4;
            if (cat.Contains("坎巴拉") || cat.Contains("原版")) return 5;
            if (cat.Contains("自定义")) return 6;
            return 10;
        }

        /// <summary>
        /// 保存指定任务配置文件至本地磁盘
        /// </summary>
        public static string SaveMission(MGAMissionConfigFile mission, string customFileName = null) {
            if (mission == null) return null;
            string dir = GetMissionsDirectory();
            string fileName = !string.IsNullOrEmpty(customFileName) ? customFileName : CleanFileName(mission.mission_name);
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";
            string fullPath = Path.Combine(dir, fileName);

            string json = JsonUtility.ToJson(mission, true);
            File.WriteAllText(fullPath, json);

            // 更新内存缓存
            LoadAllMissions(true);
            return fullPath;
        }

        /// <summary>
        /// 删除指定任务配置文件
        /// </summary>
        public static bool DeleteMission(string missionName) {
            string dir = GetMissionsDirectory();
            string cleanName = CleanFileName(missionName);
            string fullPath = Path.Combine(dir, $"{cleanName}.json");
            if (File.Exists(fullPath)) {
                File.Delete(fullPath);
                LoadAllMissions(true);
                return true;
            }
            return false;
        }
    }
}
