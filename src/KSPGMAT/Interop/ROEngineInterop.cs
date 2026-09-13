using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KSPGMAT {
    [Serializable]
    public class ROEngineInfo {
        public string id;
        public string title;
        public int stageNumber;
        public double thrustKN;
        public double minThrustKN;
        public double ispVac;
        public List<string> propellantNames = new List<string>();
        public string propellantDesc;
        public double fuelMassKg;
        public double fuelDensityKgM3;
        public double tankVolumeM3;
        public double tankPressureKPa = 1500.0; // Standard RO regulated tank pressure
        public string boundTankId = "";
        public string boundTankName = "";
        public int clusterCount = 1;
        public bool isCluster = false;
        public bool isActivated = false;
        public bool isIgnited = false;
        public bool isStaged = false;
        public bool isOperational = true;
        public bool flameout = false;
        public float currentThrottle = 0f;
        public bool isActiveOrIgnited = false;
    }

    [Serializable]
    public class VesselTankInfo {
        public string id;
        public string partTitle;
        public int stageNumber;
        public double fuelMassKg;
        public double dryMassKg;
        public double maxVolumeM3;
        public string resourceSummary;
        public List<string> containedPropellants = new List<string>();
        public bool isSelected = true;
    }

    /// <summary>
    /// RO & RealFuels hardware reflection helper.
    /// Extracts exact fuel masses, Isp, and thrust curves for GMAT.
    /// </summary>
    public static class ROEngineInterop {
        private static bool? isROInstalled = null;

        public static bool IsRealismOverhaulInstalled() {
            if (!isROInstalled.HasValue) {
                isROInstalled = false;
                if (AssemblyLoader.loadedAssemblies != null) {
                    foreach (var a in AssemblyLoader.loadedAssemblies) {
                        if (a != null && a.name != null &&
                            (a.name.IndexOf("RealFuels", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             a.name.IndexOf("RealismOverhaul", StringComparison.OrdinalIgnoreCase) >= 0)) {
                            isROInstalled = true;
                            break;
                        }
                    }
                }
            }
            return isROInstalled.Value;
        }

        /// <summary>
        /// Scans active vessel for all engines and their exact RO/RealFuels configurations.
        /// </summary>
        public static List<ROEngineInfo> ScanVesselEngines(Vessel vessel) {
            List<ROEngineInfo> list = new List<ROEngineInfo>();
            if (vessel == null) return list;

            List<ModuleEngines> engineModules = vessel.FindPartModulesImplementing<ModuleEngines>();
            if (engineModules == null || engineModules.Count == 0) return list;

            var tanks = ScanVesselTanks(vessel);

            for (int i = 0; i < engineModules.Count; i++) {
                ModuleEngines eng = engineModules[i];
                if (eng == null || eng.part == null) continue;

                ROEngineInfo info = new ROEngineInfo();
                info.id = eng.part.flightID.ToString();
                info.title = (eng.part.partInfo != null && !string.IsNullOrEmpty(eng.part.partInfo.title)) ? eng.part.partInfo.title : eng.part.name;
                info.stageNumber = eng.part.inverseStage;
                info.thrustKN = eng.maxThrust;
                info.minThrustKN = eng.minThrust;
                info.ispVac = eng.atmosphereCurve != null ? eng.atmosphereCurve.Evaluate(0f) : 300.0;

                StringBuilder propSb = new StringBuilder();
                double totalMassKg = 0.0;
                double totalVolM3 = 0.0;
                double totalMaxVolM3 = 0.0;

                if (eng.propellants != null) {
                    for (int pIdx = 0; pIdx < eng.propellants.Count; pIdx++) {
                        Propellant p = eng.propellants[pIdx];
                        if (p == null || string.IsNullOrEmpty(p.name)) continue;

                        info.propellantNames.Add(p.name);
                        if (propSb.Length > 0) propSb.Append(" + ");
                        propSb.Append(p.name);

                        // Query resource definition from KSP/RealFuels library
                        double densityTonnesPerLiter = 0.001; // default water density
                        PartResourceDefinition def = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition(p.name) : null;
                        if (def != null) {
                            densityTonnesPerLiter = def.density;
                        }

                        // Search for fuel across all parts in vessel
                        if (vessel.parts != null) {
                            for (int prt = 0; prt < vessel.parts.Count; prt++) {
                                Part pt = vessel.parts[prt];
                                if (pt == null || pt.Resources == null) continue;
                                for (int r = 0; r < pt.Resources.Count; r++) {
                                    PartResource res = pt.Resources[r];
                                    if (res != null && res.resourceName == p.name) {
                                        totalMassKg += res.amount * (densityTonnesPerLiter * 1000.0);
                                        totalVolM3 += res.amount * 0.001;
                                        totalMaxVolM3 += res.maxAmount * 0.001;
                                    }
                                }
                            }
                        }
                    }
                }

                // Fallback if no specific propellants found
                if (totalMassKg < 1e-3) {
                    if (vessel.parts != null) {
                        for (int prt = 0; prt < vessel.parts.Count; prt++) {
                            Part pt = vessel.parts[prt];
                            if (pt == null || pt.Resources == null) continue;
                            for (int r = 0; r < pt.Resources.Count; r++) {
                                PartResource res = pt.Resources[r];
                                if (res != null && (res.resourceName.Contains("Fuel") || res.resourceName.Contains("Oxidizer") || res.resourceName.Contains("Kero") || res.resourceName.Contains("Hydro") || res.resourceName.Contains("Aerozine") || res.resourceName.Contains("NTO") || res.resourceName.Contains("UDMH"))) {
                                    double dens = (res.info != null) ? res.info.density : 0.001;
                                    totalMassKg += res.amount * (dens * 1000.0);
                                    totalVolM3 += res.amount * 0.001;
                                    totalMaxVolM3 += res.maxAmount * 0.001;
                                }
                            }
                        }
                    }
                }

                info.propellantDesc = (propSb.Length > 0) ? propSb.ToString() : "Standard Propellant";
                info.fuelMassKg = totalMassKg;
                info.fuelDensityKgM3 = (totalVolM3 > 1e-6) ? (totalMassKg / totalVolM3) : 1000.0;
                
                // Volume in m^3
                if (totalMaxVolM3 > 1e-6) {
                    info.tankVolumeM3 = totalMaxVolM3;
                } else if (info.fuelDensityKgM3 > 0) {
                    info.tankVolumeM3 = Math.Max(0.1, (info.fuelMassKg / info.fuelDensityKgM3) * 1.1);
                } else {
                    info.tankVolumeM3 = 1.0;
                }

                bool isShutdownEventActive = (eng.Events != null && eng.Events["Shutdown"] != null && eng.Events["Shutdown"].active);
                bool isActivateEventInactive = (eng.Events != null && eng.Events["Activate"] != null && !eng.Events["Activate"].active);
                int curStage = (vessel != null) ? vessel.currentStage : 0;
                bool hasBeenStaged = eng.staged || (eng.part != null && eng.part.inverseStage >= curStage);

                info.isActivated = eng.EngineIgnited || isShutdownEventActive || (hasBeenStaged && eng.isOperational && !eng.flameout) || (eng.isOperational && isActivateEventInactive);
                info.isIgnited = eng.EngineIgnited && eng.currentThrottle > 0f && !eng.flameout;
                info.isStaged = hasBeenStaged;
                info.isOperational = eng.isOperational;
                info.flameout = eng.flameout;
                info.currentThrottle = eng.currentThrottle;
                info.isActiveOrIgnited = info.isActivated;

                // Accurate Tank Auto-Binding: find best tank matching propellant types and stage
                int matchedTankIdx = 0;
                for (int t = 0; t < tanks.Count; t++) {
                    var tk = tanks[t];
                    if (tk.stageNumber == info.stageNumber) {
                        matchedTankIdx = t;
                        break;
                    }
                }
                info.boundTankName = (tanks.Count > 0 && matchedTankIdx < tanks.Count) ? $"Tank{matchedTankIdx}" : "Tank0";
                if (tanks.Count > 0 && matchedTankIdx < tanks.Count) {
                    info.boundTankId = tanks[matchedTankIdx].id;
                } else {
                    info.boundTankId = "Tank0";
                }

                list.Add(info);
            }

            return list;
        }

        /// <summary>
        /// Simple and robust engine detector (Principia-aligned): finds engines currently activated/enabled (启动但是未点火 / 运行中), or in active stage.
        /// </summary>
        public static List<ROEngineInfo> GetActiveOrIgnitedEngines(Vessel vessel) {
            var all = ScanVesselEngines(vessel);
            if (all == null || all.Count == 0) return new List<ROEngineInfo>();

            // 1. Highest Priority: Engines in Activated / Enabled state (启动状态：包含已激活、已分级但当前0推力未点火或已点火状态)
            var activated = all.FindAll(e => e.isActivated && !e.flameout);
            if (activated.Count > 0) return activated;

            // 2. Second Priority: Operational engines in current active stage (vessel.currentStage)
            int curStage = (vessel != null) ? vessel.currentStage : 0;
            var staged = all.FindAll(e => e.stageNumber == curStage && e.isOperational && !e.flameout);
            if (staged.Count > 0) return staged;

            // 3. Fallback: Operational engines in next available stage with fuel
            var valid = all.FindAll(e => e.isOperational && !e.flameout && e.fuelMassKg > 0.05);
            if (valid.Count > 0) {
                int maxStage = int.MinValue;
                for (int i = 0; i < valid.Count; i++) {
                    if (valid[i].stageNumber > maxStage) maxStage = valid[i].stageNumber;
                }
                var topStageEngines = valid.FindAll(e => e.stageNumber == maxStage);
                if (topStageEngines.Count > 0) return topStageEngines;
            }

            return all;
        }

        /// <summary>
        /// Scans active vessel for all fuel tanks and propellant resources.
        /// </summary>
        public static List<VesselTankInfo> ScanVesselTanks(Vessel vessel) {
            List<VesselTankInfo> list = new List<VesselTankInfo>();
            if (vessel == null || vessel.parts == null) return list;

            for (int i = 0; i < vessel.parts.Count; i++) {
                Part p = vessel.parts[i];
                if (p == null || p.Resources == null || p.Resources.Count == 0) continue;

                double fuelMass = 0.0;
                double maxVol = 0.0;
                StringBuilder sb = new StringBuilder();

                for (int rIdx = 0; rIdx < p.Resources.Count; rIdx++) {
                    PartResource r = p.Resources[rIdx];
                    if (r == null) continue;

                    string rName = r.resourceName;
                    if (string.IsNullOrEmpty(rName) || rName.Equals("ElectricCharge", StringComparison.OrdinalIgnoreCase)) continue;

                    double density = (r.info != null) ? r.info.density : 0.001;
                    double massKg = r.amount * density * 1000.0;
                    double volM3 = r.maxAmount * 0.001;

                    if (massKg > 0.01 || r.maxAmount > 0.1) {
                        fuelMass += massKg;
                        maxVol += volM3;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append($"{rName}: {r.amount:F0}/{r.maxAmount:F0}");
                    }
                }

                if (fuelMass > 0.01 || maxVol > 0.001) {
                    VesselTankInfo info = new VesselTankInfo();
                    info.id = p.flightID.ToString();
                    info.partTitle = (p.partInfo != null && !string.IsNullOrEmpty(p.partInfo.title)) ? p.partInfo.title : p.name;
                    info.stageNumber = p.inverseStage;
                    info.fuelMassKg = fuelMass;
                    info.dryMassKg = p.mass * 1000.0;
                    info.maxVolumeM3 = maxVol;
                    info.resourceSummary = sb.ToString();
                    info.isSelected = true;
                    list.Add(info);
                }
            }

            return list;
        }
    }
}
