using System;
using UnityEngine;

namespace KSPGMAT.Model {
    /// <summary>
    /// 3D 视口渲染与相机状态数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public class MGAViewportModel {
        public float CanvasZoom { get; set; } = 1.0f;
        public Vector2 CanvasPan { get; set; } = Vector2.zero;
        public float Pitch { get; set; } = 50.0f;
        public float Yaw { get; set; } = -25.0f;
        public bool ShowSois { get; set; } = true;
        public bool ShowMET { get; set; } = false;
        public bool AutoFocus { get; set; } = true;
        public int LastCanvasW { get; set; } = 600;
        public int LastCanvasH { get; set; } = 400;
        public int ScrubberStep { get; set; } = 0;
        public float ScrubberProgress { get; set; } = 0f;
        public float TargetProgress { get; set; } = 0f;
        public bool IsPlayingAnimation { get; set; } = false;
        public float PlaySpeed { get; set; } = 1.0f;
        public bool IsDraggingCanvas { get; set; } = false;
        public bool IsRotatingCanvas { get; set; } = false;
        public Vector2 LastMouseCanvasPos { get; set; } = Vector2.zero;
    }

    /// <summary>
    /// MGA 全局 UI 交互、滚动条与模态框状态管理模型 (Single Source of Truth)
    /// 集中管理原本散落在 UI 类中的各种临时弹窗、滚动条与交互标记，彻底杜绝散落状态
    /// </summary>
    [Serializable]
    public class MGAUIInteractionState {
        // 1. 面板滚动条集中管理
        public Vector2 LeftScroll { get; set; } = Vector2.zero;
        public Vector2 TabScroll { get; set; } = Vector2.zero;
        public Vector2 SolutionScroll { get; set; } = Vector2.zero;
        public Vector2 ReportScrollPos { get; set; } = Vector2.zero;
        public bool ShowReportModal { get; set; } = false;
        public bool ShowChainSuggestions { get; set; } = false;

        // 2. 行星际天体弹出选择器 (Planet Picker Modal) 状态
        public bool ShowPlanetPickerModal { get; set; } = false;
        public Rect PlanetPickerWindowRect { get; set; } = new Rect(250, 100, 380, 520);
        public int PlanetPickerTargetType { get; set; } = 0; // 0: Departure, 1: Destination, 2: ReplaceFlyby, 3: AppendFlyby, 4: SequenceStepBody
        public int PlanetPickerFlybyIndex { get; set; } = -1;
        public string PlanetPickerTitle { get; set; } = "";
        public Vector2 PlanetPickerScrollPos { get; set; } = Vector2.zero;
        public string PlanetPickerSearch { get; set; } = "";

        // 3. 航段高精遥测明细弹窗 (Flight Telemetry Details) 状态
        public bool ShowTelemetryModal { get; set; } = false;
        public Rect TelemetryWindowRect { get; set; } = new Rect(300, 120, 540, 480);
        public int TelemetrySelectedStepIndex { get; set; } = 0;
        public Vector2 TelemetryScrollPos { get; set; } = Vector2.zero;

        // 4. 方案全量动力学详情弹窗 (Solution Detail Modal) 状态
        public bool ShowSolutionDetailModal { get; set; } = false;
        public MGASolution DetailModalSolution { get; set; } = null;
        public Rect SolutionDetailWindowRect { get; set; } = new Rect(280, 80, 720, 560);
        public Vector2 SolutionDetailScrollPos { get; set; } = Vector2.zero;

        // 5. 历史任务与统一配置库弹窗 (History Mission Picker) 状态
        public bool ShowHistoryMissionPickerModal { get; set; } = false;
        public Rect HistoryMissionPickerWindowRect { get; set; } = new Rect(260, 90, 680, 520);
        public string HistoryMissionSearch { get; set; } = "";
        public string HistoryMissionCategory { get; set; } = "全部";
        public Vector2 HistoryMissionScrollPos { get; set; } = Vector2.zero;

        // 6. 任务序列工作台拖拽重排与交互状态
        public int SeqDraggedIndex { get; set; } = -1;
        public int SeqStepToMoveUp { get; set; } = -1;
        public int SeqStepToMoveDown { get; set; } = -1;
        public int SeqStepToDelete { get; set; } = -1;
        public int SeqHoveredIndex { get; set; } = -1;
        public int SeqNextHoveredIndex { get; set; } = -1;
    }

    /// <summary>
    /// 3D 视口实时遥测与航天动力学几何追踪数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public struct CraftTelemetry {
        public bool HasCraft;
        public bool IsScrubbing;
        public double MissionUt;
        public Vector3d PositionKm;
        public Vector3d VelocityKmS;
        public double SpeedKmS;
        public double DistanceAu;
        public double DistanceKm;
        public int ActiveLegIndex;
        public int TotalLegs;
        public string FromBody;
        public string ToBody;
        public double MetDays;
        public double TotalFlightDays;
        public string CentralBody;
        public bool IsInterplanetary;
    }

    /// <summary>
    /// MGA 任务步骤条目模型 (SSOT 数据源)
    /// </summary>
    [Serializable]
    public class MGAMissionStep {
        public string id = Guid.NewGuid().ToString();
        public MGAStepType type = MGAStepType.Transfer;
        public string label = "";
        public bool is_collapsed = false;
        public bool show_inline_telemetry = false;

        // 天体与轨道几何
        public string body_name = "Earth";
        public double periapsis_alt_km = MGADefaults.DepartureAltitudeKm;
        public double apoapsis_alt_km = MGADefaults.DepartureAltitudeKm;

        // 历元与时间 (双历元系统)
        public double epoch_ut = 0.0;
        public string epoch_utc_str = "";
        public double tof_days = 150.0;
        public double min_tof_days = MGADefaults.DefaultMinToFDays;
        public double max_tof_days = MGADefaults.DefaultMaxToFDays;

        // 机动与策略
        public MGALegStrategy strategy = MGALegStrategy.Ballistic;
        public MGAFlybyStrategy flyby_strategy = MGAFlybyStrategy.UnpoweredFlyby;
        public double dv_v = 0.0;
        public double dv_n = 0.0;
        public double dv_b = 0.0;
        public double dsm_eta = MGADefaults.DsmEtaDefault;
        public double target_b_dot_t_km = 0.0;
        public double target_b_dot_r_km = 0.0;
        public double incoming_dha_deg = 0.0;
        public double incoming_dec_deg = 0.0;
        public double flyby_rp_km = 0.0;
        public double flyby_turn_deg = 0.0;
        public double v_inf_kms = 0.0;

        // 求解结果与回填状态
        public bool is_solved = false;
        public double solved_dv_ms = 0.0;
        public double solved_c3_kms2 = 0.0;
        public double solved_phase_deg = 0.0;
        public bool is_feasible = true;
    }
}
