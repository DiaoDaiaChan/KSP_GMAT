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
        public List<string> flyby_sequence = new List<string>();
        public double departure_pe_km = 200.0;
        public double departure_ap_km = 200.0;
        public double destination_pe_km = 200.0;
        public double destination_ap_km = 200.0;
        public bool no_insertion_burn = false;
        public int engine_mode = 0; // 0: Ballistic, 1: 1-DSM, 2: HeterogeneousHybrid, 3: LowThrust
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
                flyby_sequence = new List<string>(cfg.FlybySequence ?? new List<string>()),
                departure_pe_km = cfg.DeparturePeAKm,
                departure_ap_km = cfg.DepartureApAKm,
                destination_pe_km = cfg.DestinationPeAKm,
                destination_ap_km = cfg.DestinationApAKm,
                no_insertion_burn = cfg.NoInsertionBurn,
                engine_mode = (int)cfg.EngineMode,
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
    }
}
