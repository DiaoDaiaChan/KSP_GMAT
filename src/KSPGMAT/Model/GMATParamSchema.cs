using System;
using System.Collections.Generic;

namespace KSPGMAT.Model {
    /// <summary>
    /// GMAT 参数能力与上下文约束位掩码标签 (Field Capability Flags)
    /// </summary>
    [Flags]
    public enum ParamCapability {
        None = 0,
        /// <summary>连续可导物理量 (适用于 Achieve 目标收敛与牛顿法迭代，如 SMA, INC, ECC, Altitude, RMAG, Latitude)</summary>
        ContinuouslyDifferentiable = 1 << 0,
        /// <summary>离散极值与几何事件 (仅用于 Propagate 停机条件，如 Apoapsis, Periapsis, AscendingNode，严禁用于 Achieve)</summary>
        DiscontinuousExtremumEvent = 1 << 1,
        /// <summary>独立控制与寻优变量 (适用于 Vary 决策变量，如 ThrustDirection1/2/3, 点火时长, 脉冲分量)</summary>
        ControlVariable = 1 << 2,
        /// <summary>航天器与动力学硬件物理属性 (如 DryMass, FuelMass, TotalMass, Volume, Isp, Cd, Cr, SRPArea)</summary>
        HardwareProperty = 1 << 3,
        /// <summary>参考系笛卡尔空间状态向量 (X, Y, Z, VX, VY, VZ, AX, AY, AZ)</summary>
        CoordinateCartesian = 1 << 4,
        /// <summary>积分器停机条件 (包含连续量阈值与极值事件)</summary>
        PropagateCondition = ContinuouslyDifferentiable | DiscontinuousExtremumEvent,
        /// <summary>全量可报表输出与监视量</summary>
        Reportable = ContinuouslyDifferentiable | DiscontinuousExtremumEvent | ControlVariable | HardwareProperty | CoordinateCartesian
    }

    /// <summary>
    /// GMAT 官方标准参数元数据定义 (Schema Metadata Definition)
    /// </summary>
    public class GMATParamDefinition {
        public string Name { get; set; }
        public string Category { get; set; }
        public ParamCapability Capability { get; set; }
        public string Unit { get; set; }
        public double DefaultValue { get; set; }
        public double TypicalStep { get; set; }
        public string DocZh { get; set; }
        public string DocEn { get; set; }

        public GMATParamDefinition(string name, string category, ParamCapability cap, string unit, double defVal, double step, string docZh, string docEn) {
            Name = name;
            Category = category;
            Capability = cap;
            Unit = unit;
            DefaultValue = defVal;
            TypicalStep = step;
            DocZh = docZh;
            DocEn = docEn;
        }

        public string GetDoc(I18n.Lang lang) {
            return (lang == I18n.Lang.EN) ? DocEn : DocZh;
        }
    }

    /// <summary>
    /// GMAT 官方标准参数元数据中心字典 (Single Source of Catalog Schema)
    /// </summary>
    public static class GMATParamCatalog {
        public static readonly List<GMATParamDefinition> DirectKeplerian = new List<GMATParamDefinition> {
            new GMATParamDefinition("SMA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "轨道半长轴 (Semi-Major Axis: 决定轨道周期与能量)", "Semi-major axis of the orbit"),
            new GMATParamDefinition("ECC", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "轨道偏心率 (Eccentricity: 0 为正圆，消除偏心率目标 ECC=0)", "Orbital eccentricity"),
            new GMATParamDefinition("INC", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "轨道倾角 (Inclination: 消除倾角目标 INC=0)", "Orbital inclination"),
            new GMATParamDefinition("RAAN", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "升交点赤经 (Right Ascension of Ascending Node)", "Right ascension of the ascending node"),
            new GMATParamDefinition("AOP", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "近心点幅角 (Argument of Periapsis: 椭圆长轴朝向)", "Argument of periapsis"),
            new GMATParamDefinition("TA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "真近点角 (True Anomaly: 当前航天器相对近心点的角位置)", "True anomaly"),
            new GMATParamDefinition("EA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "偏近点角 (Eccentric Anomaly)", "Eccentric anomaly"),
            new GMATParamDefinition("MA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "平近点角 (Mean Anomaly)", "Mean anomaly"),
            new GMATParamDefinition("RadPer", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6578.14, 10.0, "近心点地心距/天心距半径 (Periapsis Radius: 目标 RadPer)", "Radius of periapsis"),
            new GMATParamDefinition("RadApo", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "远心点地心距/天心距半径 (Apoapsis Radius: 目标 RadApo)", "Radius of apoapsis")
        };

        public static readonly List<GMATParamDefinition> BodyRelativeParameters = new List<GMATParamDefinition> {
            new GMATParamDefinition("Altitude", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 35786.0, 10.0, "距参考天体海平面/基准面高度 (Altitude)", "Geometric altitude above body reference surface"),
            new GMATParamDefinition("RMAG", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "相对天体质心的空间距离模长 (Radial Distance)", "Magnitude of position vector from body center"),
            new GMATParamDefinition("VMAG", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 3.075, 0.01, "相对天体质心的空间速度模长 (Velocity Magnitude)", "Magnitude of velocity vector"),
            new GMATParamDefinition("VelApoapsis", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 1.64, 0.01, "远心点瞬时切向速度 (Velocity at Apoapsis)", "Velocity magnitude at apoapsis"),
            new GMATParamDefinition("VelPeriapsis", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 10.24, 0.01, "近心点瞬时切向速度 (Velocity at Periapsis)", "Velocity magnitude at periapsis"),
            new GMATParamDefinition("Latitude", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "天体地理纬度 (赤道面穿过点约束 Latitude=0)", "Planetodetic latitude"),
            new GMATParamDefinition("Longitude", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "天体地理经度 (地面站覆盖与定点约束)", "Planetodetic longitude"),
            new GMATParamDefinition("OrbitPeriod", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[s]", 86164.1, 10.0, "轨道运行周期 (Orbital Period)", "Orbital period"),
            new GMATParamDefinition("C3Energy", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 0.0, 0.1, "特征特征能量 C3 (双曲线逃逸指标 C3 > 0)", "Characteristic energy C3"),
            new GMATParamDefinition("Energy", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", -4.72, 0.1, "轨道比机械能 (Specific Mechanical Energy)", "Specific mechanical energy"),
            new GMATParamDefinition("BetaAngle", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "太阳光压与轨道面夹角 Beta", "Beta angle relative to Sun vector"),
            new GMATParamDefinition("BdotT", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 1.0, "B平面截线 T 轴分量 (深空打靶约束)", "B-plane coordinate B.T"),
            new GMATParamDefinition("BdotR", "BodyRelative", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 1.0, "B平面截线 R 轴分量 (深空打靶约束)", "B-plane coordinate B.R")
        };

        public static readonly List<GMATParamDefinition> ExtremumEvents = new List<GMATParamDefinition> {
            new GMATParamDefinition("Apoapsis", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 极值事件: 积分至远心点/远地点 (无需设值，自动找根)", "Event: Root-finding stop at Apoapsis"),
            new GMATParamDefinition("Periapsis", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 极值事件: 积分至近心点/近地点 (无需设值，自动找根)", "Event: Root-finding stop at Periapsis"),
            new GMATParamDefinition("Node", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 几何事件: 积分至轨道赤道交点 (纬度=0)", "Event: Stop at equatorial node crossing"),
            new GMATParamDefinition("AscendingNode", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 几何事件: 积分至轨道升交点", "Event: Stop at ascending node"),
            new GMATParamDefinition("DescendingNode", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 几何事件: 积分至轨道降交点", "Event: Stop at descending node")
        };

        public static readonly List<GMATParamDefinition> CoordinateCartesian = new List<GMATParamDefinition> {
            new GMATParamDefinition("Z", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "垂向位置分量 (NRHO / 晕轨道面穿过约束 Z=0)", "Z-axis coordinate in frame"),
            new GMATParamDefinition("X", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "主轴 X 方向空间位置", "X-axis coordinate in frame"),
            new GMATParamDefinition("Y", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "侧向 Y 方向空间位置", "Y-axis coordinate in frame"),
            new GMATParamDefinition("VX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "X 轴方向速度分量", "X-axis velocity component"),
            new GMATParamDefinition("VY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "Y 轴方向速度分量", "Y-axis velocity component"),
            new GMATParamDefinition("VZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "Z 轴方向垂向速度分量 (消除垂向速度 VZ=0)", "Z-axis velocity component"),
            new GMATParamDefinition("AX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "X 轴方向瞬时加速度", "X-axis acceleration"),
            new GMATParamDefinition("AY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "Y 轴方向瞬时加速度", "Y-axis acceleration"),
            new GMATParamDefinition("AZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "Z 轴方向瞬时加速度", "Z-axis acceleration")
        };

        public static readonly List<GMATParamDefinition> SpacecraftHardware = new List<GMATParamDefinition> {
            new GMATParamDefinition("TotalMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 5000.0, 1.0, "航天器当前总质量 (湿重)", "Spacecraft total wet mass"),
            new GMATParamDefinition("DryMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 850.0, 1.0, "航天器结构干重", "Spacecraft structure dry mass"),
            new GMATParamDefinition("FuelMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 4000.0, 1.0, "可用推进剂剩余质量", "Usable propellant mass"),
            new GMATParamDefinition("DragArea", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m²]", 15.0, 0.1, "大气气动迎风阻力面积", "Aerodynamic drag cross-section area"),
            new GMATParamDefinition("SRPArea", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m²]", 1.0, 0.1, "太阳光辐射压迎光截面积", "Solar radiation pressure cross-section area"),
            new GMATParamDefinition("Cd", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Unitless]", 2.2, 0.01, "气动阻力系数 Cd", "Aerodynamic drag coefficient"),
            new GMATParamDefinition("Cr", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Unitless]", 1.8, 0.01, "太阳光辐射反射系数 Cr", "Solar radiation pressure coefficient"),
            new GMATParamDefinition("ElapsedSecs", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[s]", 0.0, 1.0, "任务积分经过总秒数", "Elapsed simulation time in seconds"),
            new GMATParamDefinition("A1ModJulian", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Day]", 21545.0, 0.001, "A1 修正儒略日时间历元", "A.1 modified Julian date epoch")
        };

        public static readonly List<GMATParamDefinition> ThrusterProperties = new List<GMATParamDefinition> {
            new GMATParamDefinition("ThrustDirection1", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[切向 +V/-V]", 1.0, 0.05, "切向推力矢量分量 (V: 顺行 +1 / 逆行 -1 主推进控制变量)", "Tangential velocity-aligned thrust vector component"),
            new GMATParamDefinition("ThrustDirection2", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[副法向 +N/-N]", 0.0, 0.05, "副法向推力偏转分量 (N: 变倾角与消除法向误差控制变量)", "Normal out-of-plane thrust vector component for inclination control"),
            new GMATParamDefinition("ThrustDirection3", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[法向/径向 +B/-B]", 0.0, 0.05, "法向/径向推力分量 (B: 径向高度与偏心率微调控制变量)", "Binormal/radial thrust vector component"),
            new GMATParamDefinition("C1", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[kN]", 100.0, 1.0, "推力器最大额定真空推力 (C1 系数)", "Maximum rated vacuum thrust magnitude"),
            new GMATParamDefinition("K1", "Thruster", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[s]", 300.0, 1.0, "推力器真空比冲 Isp (K1 系数)", "Thruster vacuum specific impulse"),
            new GMATParamDefinition("GravitationalAccel", "Thruster", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m/s²]", 9.80665, 0.0, "标准海平面重力加速度常数", "Standard gravity constant for Isp"),
            new GMATParamDefinition("DutyCycle", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[0~1]", 1.0, 0.05, "推力器工作占空比", "Thruster operational duty cycle")
        };

        public static readonly List<GMATParamDefinition> TankProperties = new List<GMATParamDefinition> {
            new GMATParamDefinition("FuelMass", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 1000.0, 1.0, "储罐当前剩余燃料质量", "Tank current fuel mass"),
            new GMATParamDefinition("Volume", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m³]", 10.0, 0.1, "储罐几何物理容积", "Tank internal geometric volume"),
            new GMATParamDefinition("FuelDensity", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg/m³]", 1000.0, 1.0, "推进剂物理密度", "Propellant bulk density"),
            new GMATParamDefinition("PressureModel", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Model]", 0.0, 0.0, "储罐压力调节模式 (PressureRegulated / Blowdown)", "Tank pressure pressurization model")
        };
    }
}
