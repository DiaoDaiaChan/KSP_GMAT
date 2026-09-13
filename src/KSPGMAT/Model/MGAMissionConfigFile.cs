using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPGMAT.MGA;

namespace KSPGMAT.Model {
    /// <summary>
    /// 统一行星际多引力助推 (MGA) 与任务序列设计通用配置文件模型 (SSOT)
    /// 统一历史权威任务库、任务序列工作台队列与外置 JSON 配置文件
    /// </summary>
    [Serializable]
    public class MGAMissionConfigFile {
        public string mission_name = "Universal MGA Mission";
        public string category = "经典历史"; // "经典历史", "直达/内行星", "深空/外行星", "深空机动/低推力", "坎巴拉/原版", "用户自定义"
        public string description = "";
        public string icon = "🪐";
        public string departure_body = "Earth";
        public string destination_body = "Mars";
        public string central_body = "Sun";
        public List<string> flyby_sequence = new List<string>();
        public double departure_pe_km = 200.0;
        public double departure_ap_km = 200.0;
        public double destination_pe_km = 200.0;
        public double destination_ap_km = 200.0;
        public bool no_insertion_burn = false;
        public int arrival_orbit_type = 1; // 0: Circular, 1: HighlyElliptic
        public int engine_mode = 0; // 0: BallisticImpulsive, 1: DeepSpaceManeuver1DSM, 2: LowThrustSimsFlanagan, 3: HeterogeneousHybrid
        public int departure_mode = 0; // 0: PreLaunchFreeOrbit, 1: InOrbitLockedOrbit
        public double departure_inc_deg = 28.5;
        public double departure_raan_deg = 0.0;
        public bool lock_departure_inc = false;
        public bool lock_departure_raan = false;
        public int lambert_max_revs = 0;
        public double vessel_max_dv_ms = 0.0;
        public double max_dsm_dv_ms = 500.0;
        public double launch_dv_ceiling_ms = 0.0;
        public bool allow_powered_flyby = true;
        public bool enable_phase_alignment = false;
        public List<int> leg_strategies = new List<int>();
        public List<int> flyby_strategies = new List<int>();
        public string earliest_departure_date = "";
        public string latest_departure_date = "";
        public List<double> tof_days = new List<double>();
        public List<double> min_tof_days = new List<double>();
        public List<double> max_tof_days = new List<double>();
        public double dsm_eta_default = 0.5;
        public double low_thrust_max_thrust_n = 0.5;
        public double low_thrust_isp_s = 3000.0;
        public double spacecraft_wet_mass_kg = 1500.0;
        public double target_b_dot_t_km = 0.0;
        public double target_b_dot_r_km = 0.0;

        // 历史任务基准物理指标 (供基准核查与容差判定使用)
        public double historic_launch_ut = 0.0;
        public double historic_total_dv_min_ms = 0.0;
        public double historic_total_dv_max_ms = 0.0;
        public double historic_flight_days_min = 0.0;
        public double historic_flight_days_max = 0.0;

        // 任务序列编辑队列显式步骤 (当步骤由用户精细自定义时持久化保存)
        public List<MGAMissionStep> sequence_steps = new List<MGAMissionStep>();

        public static MGAMissionConfigFile FromBenchmarkCase(MGABenchmarkSuite.BenchmarkCase bCase, string category = "经典历史", string icon = "🪐") {
            if (bCase == null) return null;
            var cfg = new MGAMissionConfigFile {
                mission_name = bCase.MissionName,
                category = category,
                description = bCase.Description,
                icon = icon,
                departure_body = bCase.DepartureBody,
                destination_body = (bCase.FlybySequence != null && bCase.FlybySequence.Count > 0) ? bCase.FlybySequence.Last() : "Mars",
                flyby_sequence = new List<string>(bCase.FlybySequence ?? new List<string>()),
                departure_pe_km = bCase.DepAltKm,
                departure_ap_km = bCase.DepAltKm,
                destination_pe_km = bCase.ArrAltKm,
                destination_ap_km = bCase.ArrAltKm,
                no_insertion_burn = bCase.NoInsertionBurn,
                engine_mode = (int)bCase.EngineMode,
                earliest_departure_date = bCase.HistoricLaunchDateStr,
                latest_departure_date = bCase.HistoricLaunchDateStr,
                historic_launch_ut = bCase.HistoricLaunchUT,
                historic_total_dv_min_ms = bCase.HistoricTotalDeltaVMinMs,
                historic_total_dv_max_ms = bCase.HistoricTotalDeltaVMaxMs,
                historic_flight_days_min = bCase.HistoricFlightDaysMin,
                historic_flight_days_max = bCase.HistoricFlightDaysMax,
                tof_days = (bCase.HistoricToFDays != null) ? new List<double>(bCase.HistoricToFDays) : new List<double>(),
                dsm_eta_default = bCase.DsmEtaDefault,
                low_thrust_max_thrust_n = bCase.LowThrustMaxThrustN,
                low_thrust_isp_s = bCase.LowThrustIspSec,
                target_b_dot_t_km = bCase.TargetBDotTKm,
                target_b_dot_r_km = bCase.TargetBDotRKm
            };

            if (bCase.LegStrategies != null) {
                cfg.leg_strategies = bCase.LegStrategies.Select(s => (int)s).ToList();
            }
            if (bCase.FlybyStrategies != null) {
                cfg.flyby_strategies = bCase.FlybyStrategies.Select(s => (int)s).ToList();
            }
            return cfg;
        }

        public MGABenchmarkSuite.BenchmarkCase ToBenchmarkCase() {
            var b = new MGABenchmarkSuite.BenchmarkCase {
                MissionName = mission_name,
                Description = description,
                DepartureBody = departure_body,
                FlybySequence = new List<string>(flyby_sequence ?? new List<string>()),
                HistoricLaunchDateStr = earliest_departure_date,
                HistoricLaunchUT = historic_launch_ut > 0 ? historic_launch_ut : GMATClient.ParseUtcGregorian(earliest_departure_date),
                HistoricTotalDeltaVMinMs = historic_total_dv_min_ms,
                HistoricTotalDeltaVMaxMs = historic_total_dv_max_ms,
                HistoricFlightDaysMin = historic_flight_days_min,
                HistoricFlightDaysMax = historic_flight_days_max,
                HistoricToFDays = new List<double>(tof_days ?? new List<double>()),
                DepAltKm = departure_pe_km,
                ArrAltKm = destination_pe_km,
                NoInsertionBurn = no_insertion_burn,
                EngineMode = (MGAEngineMode)engine_mode,
                DsmEtaDefault = dsm_eta_default,
                LowThrustMaxThrustN = low_thrust_max_thrust_n,
                LowThrustIspSec = low_thrust_isp_s,
                TargetBDotTKm = target_b_dot_t_km,
                TargetBDotRKm = target_b_dot_r_km
            };

            if (leg_strategies != null && leg_strategies.Count > 0) {
                b.LegStrategies = leg_strategies.Select(i => (MGALegStrategy)i).ToList();
            }
            if (flyby_strategies != null && flyby_strategies.Count > 0) {
                b.FlybyStrategies = flyby_strategies.Select(i => (MGAFlybyStrategy)i).ToList();
            }
            return b;
        }

        public static MGAMissionConfigFile FromSequence(List<MGAMissionStep> sequence, MGAConfig cfg, string name, string category, string desc, string icon = "📋") {
            var file = new MGAMissionConfigFile {
                mission_name = !string.IsNullOrEmpty(name) ? name : "Custom_MGA_Sequence",
                category = !string.IsNullOrEmpty(category) ? category : "用户自定义",
                description = desc,
                icon = icon,
                departure_body = cfg.DepartureBody,
                destination_body = cfg.DestinationBody,
                central_body = cfg.CentralBody,
                flyby_sequence = new List<string>(cfg.FlybySequence ?? new List<string>()),
                departure_pe_km = cfg.DeparturePeAKm,
                departure_ap_km = cfg.DepartureApAKm,
                destination_pe_km = cfg.DestinationPeAKm,
                destination_ap_km = cfg.DestinationApAKm,
                no_insertion_burn = cfg.NoInsertionBurn,
                arrival_orbit_type = (int)cfg.ArrivalOrbitType,
                engine_mode = (int)cfg.EngineMode,
                departure_mode = (int)cfg.DepartureMode,
                departure_inc_deg = cfg.DepartureInclinationDeg,
                departure_raan_deg = cfg.DepartureRAANDeg,
                lock_departure_inc = cfg.LockDepartureInclination,
                lock_departure_raan = cfg.LockDepartureRAAN,
                lambert_max_revs = cfg.LambertMaxRevs,
                vessel_max_dv_ms = cfg.VesselMaxDeltaVMs,
                max_dsm_dv_ms = cfg.MaxDsmDeltaVMs,
                launch_dv_ceiling_ms = cfg.LaunchDeltaVCeilingMs,
                allow_powered_flyby = cfg.AllowPoweredFlyby,
                enable_phase_alignment = cfg.EnableParkingOrbitPhaseAlignment,
                earliest_departure_date = GMATClient.FormatUtcGregorian(cfg.EarliestDepartureUT),
                latest_departure_date = GMATClient.FormatUtcGregorian(cfg.LatestDepartureUT),
                dsm_eta_default = cfg.DsmEtaDefault,
                low_thrust_max_thrust_n = cfg.LowThrustMaxThrustN,
                low_thrust_isp_s = cfg.LowThrustIspSec,
                spacecraft_wet_mass_kg = cfg.SpacecraftWetMassKg,
                target_b_dot_t_km = cfg.TargetBDotTKm,
                target_b_dot_r_km = cfg.TargetBDotRKm,
                sequence_steps = (sequence != null) ? new List<MGAMissionStep>(sequence) : new List<MGAMissionStep>()
            };

            if (cfg.MinToFDays != null) file.min_tof_days = new List<double>(cfg.MinToFDays);
            if (cfg.MaxToFDays != null) file.max_tof_days = new List<double>(cfg.MaxToFDays);
            if (cfg.LegStrategies != null) file.leg_strategies = cfg.LegStrategies.Select(s => (int)s).ToList();
            if (cfg.FlybyStrategies != null) file.flyby_strategies = cfg.FlybyStrategies.Select(s => (int)s).ToList();
            return file;
        }

        /// <summary>
        /// 将当前持久化配置完整对称地还原注入到 MGAConfig 中 (SSOT 对称还原)
        /// </summary>
        public void ApplyToMGAConfig(MGAConfig cfg) {
            if (cfg == null) return;
            if (!string.IsNullOrEmpty(departure_body)) cfg.DepartureBody = departure_body;
            if (!string.IsNullOrEmpty(destination_body)) cfg.DestinationBody = destination_body;
            if (!string.IsNullOrEmpty(central_body)) cfg.CentralBody = central_body;
            if (departure_pe_km > 0) cfg.DeparturePeAKm = departure_pe_km;
            if (departure_ap_km > 0) cfg.DepartureApAKm = departure_ap_km;
            if (destination_pe_km > 0) cfg.DestinationPeAKm = destination_pe_km;
            if (destination_ap_km > 0) cfg.DestinationApAKm = destination_ap_km;
            cfg.NoInsertionBurn = no_insertion_burn;
            cfg.ArrivalOrbitType = (MGAArrivalOrbitType)arrival_orbit_type;
            cfg.EngineMode = (MGAEngineMode)engine_mode;
            cfg.DepartureMode = (MGADepartureMode)departure_mode;
            cfg.DepartureInclinationDeg = departure_inc_deg;
            cfg.DepartureRAANDeg = departure_raan_deg;
            cfg.LockDepartureInclination = lock_departure_inc;
            cfg.LockDepartureRAAN = lock_departure_raan;
            cfg.LambertMaxRevs = lambert_max_revs;
            cfg.VesselMaxDeltaVMs = vessel_max_dv_ms;
            cfg.MaxDsmDeltaVMs = max_dsm_dv_ms;
            cfg.LaunchDeltaVCeilingMs = launch_dv_ceiling_ms;
            cfg.AllowPoweredFlyby = allow_powered_flyby;
            cfg.EnableParkingOrbitPhaseAlignment = enable_phase_alignment;

            if (flyby_sequence != null && flyby_sequence.Count > 0) {
                cfg.FlybySequence = new List<string>(flyby_sequence);
            }
            if (min_tof_days != null && min_tof_days.Count > 0) {
                cfg.MinToFDays = new List<double>(min_tof_days);
            }
            if (max_tof_days != null && max_tof_days.Count > 0) {
                cfg.MaxToFDays = new List<double>(max_tof_days);
            }
            if (leg_strategies != null && leg_strategies.Count > 0) {
                cfg.LegStrategies = leg_strategies.Select(s => (MGALegStrategy)s).ToList();
            }
            if (flyby_strategies != null && flyby_strategies.Count > 0) {
                cfg.FlybyStrategies = flyby_strategies.Select(s => (MGAFlybyStrategy)s).ToList();
            }

            double depUt = GMATClient.ParseUtcGregorian(earliest_departure_date);
            if (depUt > 0) cfg.EarliestDepartureUT = depUt;
            else if (historic_launch_ut > 0) cfg.EarliestDepartureUT = historic_launch_ut;

            double endUt = GMATClient.ParseUtcGregorian(latest_departure_date);
            if (endUt > 0) cfg.LatestDepartureUT = endUt;
            else if (cfg.EarliestDepartureUT > 0) {
                cfg.LatestDepartureUT = cfg.EarliestDepartureUT + MGADefaults.SecondsPerDay * MGADefaults.DefaultSearchWindowDays;
            }
        }
    }
}
