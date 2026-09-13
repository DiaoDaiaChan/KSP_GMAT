using System;
using System.Collections.Generic;
using UnityEngine;
using MuMech;
using MechJebLib.FuelFlowSimulation;

namespace KSPGMAT {
    [Serializable]
    public class StageData {
        public int stageNumber;
        public double wetMassTonnes;
        public double dryMassTonnes;
        public double fuelMassTonnes;
        public double deltaV;
        public double thrustKN;
        public double isp;
        public double burnTimeSeconds;
        public string displayName;
    }

    /// <summary>
    /// Interoperability layer for MechJeb2 staging and mass stats.
    /// </summary>
    public static class MechJebInterop {
        public static MechJebModuleStageStats GetStageStats(Vessel vessel) {
            if (vessel == null) return null;
            MechJebCore core = vessel.GetMasterMechJeb();
            if (core == null) return null;
            return core.GetComputerModule<MechJebModuleStageStats>();
        }

        public static bool IsMechJebAvailable() {
            return FlightGlobals.ActiveVessel != null && GetStageStats(FlightGlobals.ActiveVessel) != null;
        }

        /// <summary>
        /// Reads all stages from MechJeb stage statistics.
        /// </summary>
        public static List<StageData> GetStages(Vessel vessel) {
            List<StageData> result = new List<StageData>();
            if (vessel == null) return result;

            var stats = GetStageStats(vessel);
            if (stats != null && stats.VacStats != null && stats.VacStats.Count > 0) {
                foreach (FuelStats fs in stats.VacStats) {
                    StageData sd = new StageData();
                    sd.stageNumber = fs.KSPStage;
                    sd.wetMassTonnes = fs.StartMass;
                    sd.dryMassTonnes = fs.EndMass;
                    sd.fuelMassTonnes = fs.ResourceMass;
                    sd.deltaV = fs.DeltaV;
                    sd.thrustKN = fs.Thrust;
                    sd.isp = fs.Isp;
                    sd.burnTimeSeconds = fs.DeltaTime;
                    sd.displayName = $"Stage {fs.KSPStage} (Δv: {fs.DeltaV:F0} m/s, Fuel: {fs.ResourceMass:F2}t, Dry: {fs.EndMass:F2}t)";
                    result.Add(sd);
                }
            }

            return result;
        }

        /// <summary>
        /// Adjusts stage mass for current real-time vessel mass (accounts for fuel burned during LEO insertion).
        /// </summary>
        public static void CalibrateStageWithCurrentVessel(Vessel vessel, StageData stage) {
            if (vessel == null || stage == null) return;
            double currentTotalMass = vessel.totalMass;
            // If current total mass is less than the stage's initial wet mass, update remaining fuel
            if (currentTotalMass < stage.wetMassTonnes && currentTotalMass > stage.dryMassTonnes) {
                stage.wetMassTonnes = currentTotalMass;
                stage.fuelMassTonnes = Math.Max(0.0, currentTotalMass - stage.dryMassTonnes);
                // MechJeb's stage delta-v is intentionally not recomputed here.
                // All trajectory and maneuver results must come from GMAT.
            }
        }
    }
}
