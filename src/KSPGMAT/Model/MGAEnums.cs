using System;

namespace KSPGMAT.Model {
    /// <summary>
    /// MGA 出发停泊轨道约束模式
    /// </summary>
    public enum MGADepartureMode {
        PreLaunchFreeOrbit = 0,    // 模式 1: 发射前规划 (火箭未发射，算法推荐最佳发射倾角与发射升交点)
        InOrbitLockedOrbit = 1     // 模式 2: 已在轨转移 (飞船已入轨，当前轨道参数 PeA/ApA/INC/RAAN/AOP 严格锁死)
    }

    /// <summary>
    /// 航段推进与动力学策略枚举 (Per-Leg Astrodynamic Strategy)
    /// </summary>
    public enum MGALegStrategy {
        Ballistic = 0,               // 纯弹道 (Pure Ballistic Lambert)
        DeepSpaceManeuver1DSM = 1,    // 深空机动 1-DSM (Midcourse In-flight Burn)
        ContinuousLowThrust = 2      // 连续低推力 (Sims-Flanagan SEP)
    }

    /// <summary>
    /// 中间交会节点飞掠与打靶策略枚举 (Per-Node Flyby & Targeting Strategy)
    /// </summary>
    public enum MGAFlybyStrategy {
        UnpoweredFlyby = 0,          // 纯无动力自由双曲借力 (Unpowered Flyby)
        PoweredFlyby = 1,            // 动力飞掠 (Powered Assist)
        BPlaneTargeted = 2           // B 平面精准穿靶 (B-Plane Targeted TCM)
    }

    /// <summary>
    /// MGA 求解引擎全局动力学模式 (Engine Dynamics Mode)
    /// </summary>
    public enum MGAEngineMode {
        BallisticImpulsive = 0,    // 传统纯脉冲借力 (Ballistic MGA)
        DeepSpaceManeuver1DSM = 1, // 官方 MGA-1DSM (含中道深空机动与共振)
        LowThrustSimsFlanagan = 2, // 连续低推力打靶 (Sims-Flanagan / SEP)
        HeterogeneousHybrid = 3    // 异构分段混合策略 (Multi-Strategy Hybrid)
    }

    /// <summary>
    /// MGA 事件类型枚举 (100% 对齐 KSP-MGA-Planner 与真实任务事件)
    /// </summary>
    public enum MGAEventType {
        Escape,          // 1: <Origin> escape (出发脱离机动)
        DSM,             // 2: <From>-<To> DSM (深空变轨/中途机动)
        Flyby,           // 3: <Body> flyby (经过天体的引力助推飞掠)
        Circularization, // 4: <Destination> circularization / capture (目标行星捕获/圆化机动)
        TCM,             // 5: <From>-<To> TCM (B平面中道修正)
        LowThrustSegment // 6: 连续低推力打靶航段
    }

    /// <summary>
    /// MGA 任务控制步骤类型 (与 Tab 2 序列设计语言统一)
    /// </summary>
    public enum MGAStepType {
        Departure,   // 始发脱离 (出发天体、停泊轨道、发射历元、C3、逃逸脉冲)
        Transfer,    // 转移巡航 (飞行时间、动力学策略: 弹道/1-DSM/低推力/B平面TCM)
        Maneuver,    // 深空机动 (DSM 历元、脉冲矢量 VNC、时间占比 eta)
        Flyby,       // 行星助推 (飞越天体、近心点高度、转向角、动力助推)
        Arrival      // 目标交会 (目标天体、捕获制动/探测飞越、捕获轨道)
    }

    /// <summary>
    /// 终端到达轨道物理模式 (与 GUI 下拉菜单无缝数据绑定)
    /// </summary>
    public enum MGAArrivalOrbitType
    {
        HighlyElliptic = 0,     // 科学高偏心率捕获椭圆 (外行星/巨行星默认标配)
        LowCircular = 1,        // 低圆轨道停泊 (内行星/环绕任务)
        Flyby = 2,              // 纯飞掠掠过 (零捕获机动，如旅行者、新视野)
        ParabolicThreshold = 3  // 临界抛物线捕获
    }

    /// <summary>
    /// 航天器推进系统物理类型 (解耦化学大脉冲与低推力离子电推进)
    /// </summary>
    public enum MGAPropulsionType
    {
        ChemicalImpulsive = 0,  // 化学推进瞬时大脉冲
        LowThrustElectric = 1   // 连续低推力离子电推进 (如黎明号 Dawn、深空1号 DS1)
    }
}
