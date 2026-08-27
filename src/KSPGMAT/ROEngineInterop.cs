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
    }

    /// <summary>
    /// Interoperability module for RealismOverhaul & RealFuels engine parameters and propellant density.
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
        /// Scans active vessel for all engines and calculates real propellant density and tank mass from RO/RealFuels.
        /// </summary>
        public static List<ROEngineInfo> ScanVesselEngines(Vessel vessel) {
            List<ROEngineInfo> list = new List<ROEngineInfo>();
            if (vessel == null) return list;

            List<ModuleEngines> engineModules = vessel.FindPartModulesImplementing<ModuleEngines>();
            if (engineModules == null || engineModules.Count == 0) return list;

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

                        // Aggregate resource amount across vessel
                        double resAmountLiters = 0.0;
                        double resMaxAmountLiters = 0.0;

                        if (vessel.parts != null) {
                            for (int pPart = 0; pPart < vessel.parts.Count; pPart++) {
                                Part part = vessel.parts[pPart];
                                if (part.Resources != null) {
                                    PartResource res = part.Resources[p.name];
                                    if (res != null) {
                                        resAmountLiters += res.amount;
                                        resMaxAmountLiters += res.maxAmount;
                                    }
                                }
                            }
                        }

                        // In RealFuels, density is tonnes/Liter.
                        // 1 tonne / Liter = 1,000,000 kg / m^3.
                        // 1 Liter = 0.001 m^3.
                        double massKg = resAmountLiters * densityTonnesPerLiter * 1000.0;
                        double volM3 = resAmountLiters * 0.001;
                        double maxVolM3 = resMaxAmountLiters * 0.001;

                        totalMassKg += massKg;
                        totalVolM3 += volM3;
                        totalMaxVolM3 += maxVolM3;
                    }
                }

                info.propellantDesc = propSb.ToString();
                info.fuelMassKg = totalMassKg;

                // Weighted density in kg/m^3
                if (totalVolM3 > 1e-6) {
                    info.fuelDensityKgM3 = totalMassKg / totalVolM3;
                } else if (eng.propellants != null && eng.propellants.Count > 0) {
                    // Fallback to theoretical mixture density from propellant ratios
                    double sumRatio = 0.0;
                    double sumRatioDensity = 0.0;
                    foreach (var p in eng.propellants) {
                        PartResourceDefinition def = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition(p.name) : null;
                        double rho = def != null ? def.density * 1000000.0 : 1000.0;
                        sumRatio += p.ratio;
                        sumRatioDensity += p.ratio * rho;
                    }
                    info.fuelDensityKgM3 = sumRatio > 0 ? (sumRatioDensity / sumRatio) : 1260.0;
                } else {
                    info.fuelDensityKgM3 = 1260.0;
                }

                // Volume in m^3
                if (totalMaxVolM3 > 1e-6) {
                    info.tankVolumeM3 = totalMaxVolM3;
                } else if (info.fuelDensityKgM3 > 0) {
                    info.tankVolumeM3 = Math.Max(0.1, (info.fuelMassKg / info.fuelDensityKgM3) * 1.1);
                } else {
                    info.tankVolumeM3 = 1.0;
                }

                list.Add(info);
            }

            return list;
        }
    }
}
