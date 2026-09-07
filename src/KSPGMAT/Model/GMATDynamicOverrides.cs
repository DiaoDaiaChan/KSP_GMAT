using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using KSPGMAT.MGA;

namespace KSPGMAT.Model {
    /// <summary>
    /// GMAT 动态运行态实时覆盖管理器 (Dynamic Runtime Overrides Manager)。
    /// 严格遵守 baseline_agent.md 铁律，将“静态任务拓扑与算法结构”与“游戏内实时物理状态 (历元/轨道/储罐质量)”解耦，
    /// 支持在 100% 保留原生脚本任何结构与注释的前提下，精确覆盖游戏运行态物理量。
    /// </summary>
    public class GMATDynamicOverrides {
        // --- 覆盖开关控制 ---
        public bool EnableEpochOverride { get; set; } = true;
        public bool EnableStateVectorOverride { get; set; } = true;
        public bool EnableFuelMassOverride { get; set; } = true;
        public bool EnableDryMassOverride { get; set; } = true;

        // --- 实时物理量快照 ---
        public string EpochUtcGregorian { get; set; } = "";
        public double UniversalTime { get; set; } = 0.0;
        public Vector3d PosKm { get; set; } = Vector3d.zero;
        public Vector3d VelKms { get; set; } = Vector3d.zero;
        public double DryMassKg { get; set; } = 850.0;
        public double TotalMassKg { get; set; } = 5000.0;
        public string CentralBody { get; set; } = "Earth";

        // 储罐实时燃料覆盖表: TankId/TankName -> FuelMassKg
        public Dictionary<string, double> TankFuelOverrides { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 从当前活动载具实时采集物理状态与硬件燃料快照
        /// </summary>
        public void UpdateFromVessel(Vessel v) {
            if (v == null) return;

            // 1. 历元时间 (统一由双通道历元系统提供)
            UniversalTime = Planetarium.GetUniversalTime();
            double jdVal = EphemerisTimeSystem.UtToJd(UniversalTime);
            EpochUtcGregorian = GMATClient.JdToUtcGregorian(jdVal);

            // 2. 惯性坐标系 6 态矢量
            PrincipiaInterop.GetVesselStateICRF(v, out Vector3d pKm, out Vector3d vKms);
            PosKm = pKm;
            VelKms = vKms;
            TotalMassKg = v.GetTotalMass() * 1000.0;

            // 3. 规范化主天体名 (统一经 SSOT GMATSchemaRegistry 映射)
            string cb = v.mainBody != null ? v.mainBody.bodyName : "Earth";
            CentralBody = GMATSchemaRegistry.Instance.NormalizeCelestialName(cb);

            // 4. 扫描整船储罐实时燃料量
            TankFuelOverrides.Clear();
            double totalFuelKg = 0.0;
            var rawTanks = ROEngineInterop.ScanVesselTanks(v);
            if (rawTanks != null && rawTanks.Count > 0) {
                for (int i = 0; i < rawTanks.Count; i++) {
                    var rt = rawTanks[i];
                    string tkKey = $"Tank{i}";
                    TankFuelOverrides[tkKey] = rt.fuelMassKg;
                    totalFuelKg += rt.fuelMassKg;
                }
            } else {
                TankFuelOverrides["Tank0"] = Math.Max(0.0, TotalMassKg - 850.0);
                totalFuelKg = TankFuelOverrides["Tank0"];
            }

            DryMassKg = Math.Max(50.0, TotalMassKg - totalFuelKg);
        }

        /// <summary>
        /// 将实时运行态物理量就地覆盖到任意原生 GMAT 脚本文本中。
        /// 100% 完整保留原始脚本的所有注释、布局、力模型、坐标系定义与复杂控制流，仅就地替换初始运行态数值！
        /// </summary>
        public string OverlayOnScriptText(string scriptText, string targetSatName = "Sat") {
            if (string.IsNullOrEmpty(scriptText)) return scriptText;
            string safeSat = !string.IsNullOrEmpty(targetSatName) ? targetSatName : "Sat";
            string s = scriptText;

            // 1. 覆盖历元 Epoch (自动遵循 baseline 7: FormatGmatSafeEpochString)
            if (EnableEpochOverride && !string.IsNullOrEmpty(EpochUtcGregorian)) {
                string safeEpoch = GMATClient.FormatGmatSafeEpochString(EpochUtcGregorian);
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.Epoch\s*=\s*)'[^']*'(;?)", $"${{1}}'{safeEpoch}'$2");
            }

            // 2. 覆盖笛卡尔态矢量 (X, Y, Z, VX, VY, VZ)
            if (EnableStateVectorOverride && (PosKm != Vector3d.zero || VelKms != Vector3d.zero)) {
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.X\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{PosKm.x.ToString("F6", CultureInfo.InvariantCulture)}$2");
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.Y\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{PosKm.y.ToString("F6", CultureInfo.InvariantCulture)}$2");
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.Z\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{PosKm.z.ToString("F6", CultureInfo.InvariantCulture)}$2");
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.VX\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{VelKms.x.ToString("F6", CultureInfo.InvariantCulture)}$2");
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.VY\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{VelKms.y.ToString("F6", CultureInfo.InvariantCulture)}$2");
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.VZ\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{VelKms.z.ToString("F6", CultureInfo.InvariantCulture)}$2");
            }

            // 3. 覆盖干重 DryMass
            if (EnableDryMassOverride && DryMassKg > 0) {
                s = Regex.Replace(s, $@"(?m)^(\s*{safeSat}\.DryMass\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                    $"${{1}}{DryMassKg.ToString("G", CultureInfo.InvariantCulture)}$2");
            }

            // 4. 覆盖各储罐 FuelMass
            if (EnableFuelMassOverride && TankFuelOverrides.Count > 0) {
                foreach (var kvp in TankFuelOverrides) {
                    string tk = kvp.Key;
                    double fm = kvp.Value;
                    s = Regex.Replace(s, $@"(?m)^(\s*{tk}\.FuelMass\s*=\s*)[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?(;?)",
                        $"${{1}}{fm.ToString("G", CultureInfo.InvariantCulture)}$2");
                }
            }

            return s;
        }

        /// <summary>
        /// 将当前覆盖物理量同步至单一真值源 GMATStateManager
        /// </summary>
        public void ApplyToStateManager(GMATStateManager state) {
            if (state == null) return;
            if (EnableEpochOverride && !string.IsNullOrEmpty(EpochUtcGregorian)) {
                state.Spacecraft.EpochUtc = EpochUtcGregorian;
                state.Spacecraft.UniversalTime = UniversalTime;
            }
            if (EnableStateVectorOverride && (PosKm != Vector3d.zero || VelKms != Vector3d.zero)) {
                state.Spacecraft.PosKm = PosKm;
                state.Spacecraft.VelKms = VelKms;
                state.Spacecraft.CentralBody = CentralBody;
            }
            if (EnableDryMassOverride && DryMassKg > 0) {
                state.Spacecraft.DryMassKg = DryMassKg;
                state.Spacecraft.TotalMassKg = TotalMassKg;
            }
            if (EnableFuelMassOverride && TankFuelOverrides.Count > 0 && state.Tanks != null) {
                for (int i = 0; i < state.Tanks.Count; i++) {
                    string tkKey = $"Tank{i}";
                    if (TankFuelOverrides.TryGetValue(tkKey, out double fm)) {
                        state.Tanks[i].FuelMassKg = fm;
                    }
                }
            }
        }
    }
}
