using System;
using System.Collections.Generic;

namespace KSPGMAT.Model {
    /// <summary>
    /// GMAT 参数能力与上下文约束位掩码标签 (Field Capability Flags)
    /// </summary>
    [Flags]
    public enum ParamCapability {
        None = 0,
        /// <summary>连续可导物理量 (适用于 Achieve 目标收敛与牛顿法迭代，如 SMA, INC, ECC, Altitude, RMAG, Latitude, BdotT, IncomingDHA)</summary>
        ContinuouslyDifferentiable = 1 << 0,
        /// <summary>离散极值与几何事件 (仅用于 Propagate 停机条件，如 Apoapsis, Periapsis, AscendingNode，严禁用于 Achieve)</summary>
        DiscontinuousExtremumEvent = 1 << 1,
        /// <summary>独立控制与寻优变量 (适用于 Vary 决策变量，如 ThrustDirection1/2/3, C1, 点火时长, 脉冲分量)</summary>
        ControlVariable = 1 << 2,
        /// <summary>航天器与动力学硬件物理属性 (如 DryMass, FuelMass, TotalMass, Volume, Isp, Cd, Cr, SRPArea)</summary>
        HardwareProperty = 1 << 3,
        /// <summary>参考系笛卡尔空间状态向量 (X, Y, Z, VX, VY, VZ, AX, AY, AZ, HMAG)</summary>
        CoordinateCartesian = 1 << 4,
        /// <summary>姿态与四元数/欧拉角 (Q1~Q4, Euler, MRP, DCM)</summary>
        AttitudeOrientation = 1 << 5,
        /// <summary>积分器停机条件 (包含连续量阈值与极值事件)</summary>
        PropagateCondition = ContinuouslyDifferentiable | DiscontinuousExtremumEvent,
        /// <summary>全量可报表输出与监视量</summary>
        Reportable = ContinuouslyDifferentiable | DiscontinuousExtremumEvent | ControlVariable | HardwareProperty | CoordinateCartesian | AttitudeOrientation
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
    /// NASA GMAT 310+ 官方参数全量元数据字典库 (Complete GMAT API Parameter Catalog)
    /// </summary>
    public static class GMATParamCatalog {
        // =========================================================================
        // 1. KEPLERIAN & ORBITAL ELEMENTS (经典开普勒根数与扩展要素)
        // =========================================================================
        public static readonly List<GMATParamDefinition> DirectKeplerian = new List<GMATParamDefinition> {
            new GMATParamDefinition("SMA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "轨道半长轴 (Semi-Major Axis: 决定轨道周期与机械能)", "Semi-major axis of the orbit"),
            new GMATParamDefinition("ECC", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "轨道偏心率 (Eccentricity: 0 为正圆，圆化打靶目标 ECC=0)", "Orbital eccentricity"),
            new GMATParamDefinition("INC", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "轨道倾角 (Inclination: 消除倾角目标 INC=0)", "Orbital inclination"),
            new GMATParamDefinition("RAAN", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "升交点赤经 (Right Ascension of Ascending Node)", "Right ascension of the ascending node"),
            new GMATParamDefinition("RADN", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 180.0, 1.0, "降交点赤经 (Right Ascension of Descending Node)", "Right ascension of the descending node"),
            new GMATParamDefinition("AOP", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "近心点幅角 (Argument of Periapsis: 椭圆长轴在轨道面的朝向)", "Argument of periapsis"),
            new GMATParamDefinition("TA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "真近点角 (True Anomaly: 当前航天器相对近心点的角位置)", "True anomaly"),
            new GMATParamDefinition("MA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "平近点角 (Mean Anomaly: 沿辅助圆匀速运动的角度)", "Mean anomaly"),
            new GMATParamDefinition("EA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "偏近点角 (Eccentric Anomaly: 开普勒方程中间角)", "Eccentric anomaly"),
            new GMATParamDefinition("HA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "双曲近点角 (Hyperbolic Anomaly: 用于双曲线逃逸/捕获轨道)", "Hyperbolic anomaly"),
            new GMATParamDefinition("MM", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg/s]", 0.00417, 0.0001, "平运动角速度 (Mean Motion: 轨道平均角速度 n)", "Mean motion angular velocity"),
            new GMATParamDefinition("RadPer", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6578.14, 10.0, "近心点天心距半径 (Periapsis Radius: 目标 RadPer)", "Radius of periapsis"),
            new GMATParamDefinition("RadApo", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "远心点天心距半径 (Apoapsis Radius: 目标 RadApo)", "Radius of apoapsis"),
            new GMATParamDefinition("SemilatusRectum", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "轨道半通径 (Semilatus Rectum: p = a(1-e²))", "Semilatus rectum parameter"),
            new GMATParamDefinition("SMADot", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "半长轴时间变化率 (SMA Rate of Change)", "Time derivative of semi-major axis"),
            new GMATParamDefinition("OrbitPeriod", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[s]", 86164.1, 10.0, "开普勒轨道运行周期 (Orbital Period)", "Keplerian orbital period"),
            new GMATParamDefinition("VelApoapsis", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 1.64, 0.01, "远心点瞬时切向速度 (Velocity at Apoapsis)", "Velocity at apoapsis"),
            new GMATParamDefinition("VelPeriapsis", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 10.24, 0.01, "近心点瞬时切向速度 (Velocity at Periapsis)", "Velocity at periapsis"),
            new GMATParamDefinition("C3Energy", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 0.0, 0.1, "特征特征能量 C3 (双曲线逃逸指标 C3 = v_inf²)", "Characteristic energy C3"),
            new GMATParamDefinition("Energy", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", -4.72, 0.1, "轨道比机械能 (Specific Mechanical Energy = -mu/(2a))", "Specific mechanical energy")
        };

        // =========================================================================
        // 2. EQUINOCTIAL & DELAUNAY ELEMENTS (春分点根数、修正春分点与德劳内根数 - 消除奇点)
        // =========================================================================
        public static readonly List<GMATParamDefinition> EquinoctialAndDelaunay = new List<GMATParamDefinition> {
            new GMATParamDefinition("ModEquinoctialF", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "修正春分点要素 f = e*cos(omega+Omega)", "Modified Equinoctial element F"),
            new GMATParamDefinition("ModEquinoctialG", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "修正春分点要素 g = e*sin(omega+Omega)", "Modified Equinoctial element G"),
            new GMATParamDefinition("ModEquinoctialH", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "修正春分点要素 h = tan(i/2)*cos(Omega)", "Modified Equinoctial element H"),
            new GMATParamDefinition("ModEquinoctialK", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "修正春分点要素 k = tan(i/2)*sin(Omega)", "Modified Equinoctial element K"),
            new GMATParamDefinition("TLONG", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "真经度 (True Longitude = omega + Omega + nu)", "True Longitude"),
            new GMATParamDefinition("TLONGDot", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg/s]", 0.00417, 0.0001, "真经度时间变化率", "True Longitude rate"),
            new GMATParamDefinition("EquinoctialH", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "经典春分点要素 h", "Classical Equinoctial element h"),
            new GMATParamDefinition("EquinoctialK", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "经典春分点要素 k", "Classical Equinoctial element k"),
            new GMATParamDefinition("EquinoctialP", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "经典春分点要素 p", "Classical Equinoctial element p"),
            new GMATParamDefinition("EquinoctialQ", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "经典春分点要素 q", "Classical Equinoctial element q"),
            new GMATParamDefinition("MLONG", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "平日心经度 (Mean Longitude = omega + Omega + M)", "Mean Longitude"),
            new GMATParamDefinition("Delaunayl", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "德劳内角要素 l (平近点角 M)", "Delaunay angular element l"),
            new GMATParamDefinition("Delaunayg", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "德劳内角要素 g (近地点幅角 omega)", "Delaunay angular element g"),
            new GMATParamDefinition("Delaunayh", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "德劳内角要素 h (升交点赤经 Omega)", "Delaunay angular element h"),
            new GMATParamDefinition("DelaunayL", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "德劳内作用量 L = sqrt(mu*a)", "Delaunay action element L"),
            new GMATParamDefinition("DelaunayG", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "德劳内作用量 G = L*sqrt(1-e²)", "Delaunay action element G"),
            new GMATParamDefinition("DelaunayH", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "德劳内作用量 H = G*cos(i)", "Delaunay action element H")
        };

        // =========================================================================
        // 3. BROUWER-LYDDANE MEAN ELEMENTS (布劳威尔-利丹平根数 - J2摄动长期平均)
        // =========================================================================
        public static readonly List<GMATParamDefinition> BrouwerMeanElements = new List<GMATParamDefinition> {
            new GMATParamDefinition("BrouwerShortSMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "布劳威尔短周期平半长轴", "Brouwer-Lyddane short-period mean SMA"),
            new GMATParamDefinition("BrouwerShortECC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "布劳威尔短周期平偏心率", "Brouwer-Lyddane short-period mean ECC"),
            new GMATParamDefinition("BrouwerShortINC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "布劳威尔短周期平轨道倾角", "Brouwer-Lyddane short-period mean INC"),
            new GMATParamDefinition("BrouwerShortRAAN", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "布劳威尔短周期平升交点赤经", "Brouwer-Lyddane short-period mean RAAN"),
            new GMATParamDefinition("BrouwerShortAOP", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "布劳威尔短周期平近心点幅角", "Brouwer-Lyddane short-period mean AOP"),
            new GMATParamDefinition("BrouwerShortMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "布劳威尔短周期平平近点角", "Brouwer-Lyddane short-period mean MA"),
            new GMATParamDefinition("BrouwerLongSMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "布劳威尔长周期平半长轴", "Brouwer-Lyddane long-period mean SMA"),
            new GMATParamDefinition("BrouwerLongECC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "布劳威尔长周期平偏心率", "Brouwer-Lyddane long-period mean ECC"),
            new GMATParamDefinition("BrouwerLongINC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "布劳威尔长周期平轨道倾角", "Brouwer-Lyddane long-period mean INC"),
            new GMATParamDefinition("BrouwerLongRAAN", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "布劳威尔长周期平升交点赤经", "Brouwer-Lyddane long-period mean RAAN"),
            new GMATParamDefinition("BrouwerLongAOP", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "布劳威尔长周期平近心点幅角", "Brouwer-Lyddane long-period mean AOP"),
            new GMATParamDefinition("BrouwerLongMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "布劳威尔长周期平平近点角", "Brouwer-Lyddane long-period mean MA")
        };

        // =========================================================================
        // 4. SPHERICAL & PLANETODETIC (球面坐标、大地测量坐标与天文时角)
        // =========================================================================
        public static readonly List<GMATParamDefinition> SphericalAndPlanetodetic = new List<GMATParamDefinition> {
            new GMATParamDefinition("Altitude", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 35786.0, 10.0, "距参考天体海平面/大地基准面高度 (Altitude)", "Geometric altitude above body reference ellipsoid"),
            new GMATParamDefinition("RMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "相对天体质心的空间矢径模长 (Radial Distance)", "Magnitude of position vector from body center"),
            new GMATParamDefinition("VMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 3.075, 0.01, "相对天体质心的空间速度模长 (Velocity Magnitude)", "Magnitude of velocity vector"),
            new GMATParamDefinition("AMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s²]", 0.0002, 0.0001, "航天器瞬时总空间加速度模长", "Magnitude of acceleration vector"),
            new GMATParamDefinition("RA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "空间位置赤经 (Right Ascension)", "Right ascension of position"),
            new GMATParamDefinition("DEC", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "空间位置赤纬 (Declination)", "Declination of position"),
            new GMATParamDefinition("RAV", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "空间速度方向赤经 (Right Ascension of Velocity)", "Right ascension of velocity vector"),
            new GMATParamDefinition("DECV", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "空间速度方向赤纬 (Declination of Velocity)", "Declination of velocity vector"),
            new GMATParamDefinition("AZI", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 90.0, 1.0, "地面站/地固系方位角 (Azimuth)", "Topocentric/Planetodetic Azimuth angle"),
            new GMATParamDefinition("FPA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "飞行路径角 (Flight Path Angle: 速度与当地水平面夹角)", "Flight path angle"),
            new GMATParamDefinition("Latitude", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "天体地理纬度 (赤道面穿过点约束 Latitude=0)", "Planetodetic latitude"),
            new GMATParamDefinition("Longitude", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "天体地理经度 (地面覆盖与星下点定点约束)", "Planetodetic longitude"),
            new GMATParamDefinition("LST", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "地方恒星时 (Local Sidereal Time)", "Local sidereal time angle"),
            new GMATParamDefinition("MHA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "格林威治/地方平赤时角 (Mean Hour Angle)", "Mean hour angle"),
            new GMATParamDefinition("DLA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "渐近线赤纬 (Declination of Launch Asymptote)", "Declination of launch asymptote"),
            new GMATParamDefinition("RLA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "渐近线赤经 (Right Ascension of Launch Asymptote)", "Right ascension of launch asymptote"),
            new GMATParamDefinition("PlanetodeticLAT", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "参考椭球体大地纬度", "Planetodetic latitude on reference ellipsoid"),
            new GMATParamDefinition("PlanetodeticLON", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "参考椭球体大地经度", "Planetodetic longitude on reference ellipsoid"),
            new GMATParamDefinition("PlanetodeticRMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6378.14, 1.0, "大地基准面空间半径", "Planetodetic radial distance")
        };

        // =========================================================================
        // 5. HYPERBOLIC ASYMPTOTES & B-PLANE FLYBY (双曲渐近线进出场、B平面截距与深空引力助推)
        // =========================================================================
        public static readonly List<GMATParamDefinition> HyperbolicAndBPlane = new List<GMATParamDefinition> {
            new GMATParamDefinition("IncomingDHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "入射双曲渐近线赤纬 (Incoming Asymptote Declination)", "Declination of incoming asymptote"),
            new GMATParamDefinition("IncomingRHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "入射双曲渐近线赤经 (Incoming Asymptote Right Ascension)", "Right ascension of incoming asymptote"),
            new GMATParamDefinition("IncomingRadPer", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6678.0, 10.0, "入射双曲线近拱点半径 (Incoming Periapsis Radius)", "Incoming hyperbolic periapsis radius"),
            new GMATParamDefinition("IncomingC3Energy", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 15.0, 0.1, "入射双曲特征能量 C3 (Incoming C3 Energy)", "Incoming asymptotic characteristic energy C3"),
            new GMATParamDefinition("IncomingBVAZI", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "入射 B 矢量方位角 (Incoming B-Vector Azimuth)", "Azimuth of incoming B-vector"),
            new GMATParamDefinition("OutgoingDHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "出射双曲渐近线赤纬 (Outgoing Asymptote Declination)", "Declination of outgoing asymptote"),
            new GMATParamDefinition("OutgoingRHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "出射双曲渐近线赤经 (Outgoing Asymptote Right Ascension)", "Right ascension of outgoing asymptote"),
            new GMATParamDefinition("OutgoingRadPer", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6678.0, 10.0, "出射双曲线近拱点半径 (Outgoing Periapsis Radius)", "Outgoing hyperbolic periapsis radius"),
            new GMATParamDefinition("OutgoingC3Energy", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 15.0, 0.1, "出射双曲特征能量 C3 (Outgoing C3 Energy)", "Outgoing asymptotic characteristic energy C3"),
            new GMATParamDefinition("OutgoingBVAZI", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "出射 B 矢量方位角 (Outgoing B-Vector Azimuth)", "Azimuth of outgoing B-vector"),
            new GMATParamDefinition("BdotT", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 1.0, "B平面截线 T 轴分量 (深空引力助推水平瞄准距)", "B-plane coordinate B.T (in-plane target parameter)"),
            new GMATParamDefinition("BdotR", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 1.0, "B平面截线 R 轴分量 (深空引力助推垂直瞄准距)", "B-plane coordinate B.R (out-of-plane target parameter)"),
            new GMATParamDefinition("BVectorMag", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 10000.0, 10.0, "B 矢量总模长 (B-Vector Magnitude = sqrt(BT² + BR²))", "Magnitude of B-vector in target plane"),
            new GMATParamDefinition("BVectorAngle", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "B 矢量在 B 平面内的方位夹角 theta", "Angle of B-vector in target plane")
        };

        // =========================================================================
        // 6. EXTREMUM EVENTS (离散极值停机找根事件 - 严禁用于 Achieve 连续求解)
        // =========================================================================
        public static readonly List<GMATParamDefinition> ExtremumEvents = new List<GMATParamDefinition> {
            new GMATParamDefinition("Apoapsis", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 极值事件: 积分至远心点/远地点 (无需设值，自动找根)", "Event: Root-finding stop at Apoapsis"),
            new GMATParamDefinition("Periapsis", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 极值事件: 积分至近心点/近地点 (无需设值，自动找根)", "Event: Root-finding stop at Periapsis"),
            new GMATParamDefinition("Node", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 几何事件: 积分至轨道赤道交点 (纬度=0)", "Event: Stop at equatorial node crossing"),
            new GMATParamDefinition("AscendingNode", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 几何事件: 积分至轨道升交点", "Event: Stop at ascending node"),
            new GMATParamDefinition("DescendingNode", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "● 几何事件: 积分至轨道降交点", "Event: Stop at descending node")
        };

        // =========================================================================
        // 7. COORDINATE CARTESIAN & ANGULAR MOMENTUM (笛卡尔状态与角动量矢量)
        // =========================================================================
        public static readonly List<GMATParamDefinition> CoordinateCartesian = new List<GMATParamDefinition> {
            new GMATParamDefinition("Z", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "垂向位置分量 (NRHO / 晕轨道面穿过约束 Z=0)", "Z-axis coordinate in frame"),
            new GMATParamDefinition("X", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "主轴 X 方向空间位置", "X-axis coordinate in frame"),
            new GMATParamDefinition("Y", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "侧向 Y 方向空间位置", "Y-axis coordinate in frame"),
            new GMATParamDefinition("VX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "X 轴方向速度分量", "X-axis velocity component"),
            new GMATParamDefinition("VY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "Y 轴方向速度分量", "Y-axis velocity component"),
            new GMATParamDefinition("VZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "Z 轴方向垂向速度分量 (消除垂向速度 VZ=0)", "Z-axis velocity component"),
            new GMATParamDefinition("AX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "X 轴方向瞬时加速度", "X-axis acceleration"),
            new GMATParamDefinition("AY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "Y 轴方向瞬时加速度", "Y-axis acceleration"),
            new GMATParamDefinition("AZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "Z 轴方向瞬时加速度", "Z-axis acceleration"),
            new GMATParamDefinition("HMAG", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 60000.0, 10.0, "轨道角动量总模长 (Angular Momentum Magnitude = r x v)", "Magnitude of orbital angular momentum"),
            new GMATParamDefinition("HX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "角动量 X 轴分量", "X-component of angular momentum"),
            new GMATParamDefinition("HY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "角动量 Y 轴分量", "Y-component of angular momentum"),
            new GMATParamDefinition("HZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.Reportable, "[Km²/s]", 60000.0, 10.0, "角动量 Z 轴分量 (决定顺行/逆行)", "Z-component of angular momentum"),
            new GMATParamDefinition("BetaAngle", "Cartesian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "太阳光压矢量与轨道面夹角 Beta", "Beta angle relative to Sun vector")
        };

        // =========================================================================
        // 8. SPACECRAFT HARDWARE & TIME (航天器结构、质量、气动光压与历元时间)
        // =========================================================================
        public static readonly List<GMATParamDefinition> SpacecraftHardware = new List<GMATParamDefinition> {
            new GMATParamDefinition("TotalMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 5000.0, 1.0, "航天器当前总质量 (湿重 = 干重 + 燃料)", "Spacecraft total wet mass"),
            new GMATParamDefinition("DryMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 850.0, 1.0, "航天器结构干重", "Spacecraft structure dry mass"),
            new GMATParamDefinition("FuelMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 4000.0, 1.0, "可用推进剂剩余总质量", "Usable propellant mass"),
            new GMATParamDefinition("DragArea", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m²]", 15.0, 0.1, "大气气动迎风阻力横截面积", "Aerodynamic drag cross-section area"),
            new GMATParamDefinition("SRPArea", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m²]", 1.0, 0.1, "太阳光辐射压迎光截面积", "Solar radiation pressure cross-section area"),
            new GMATParamDefinition("Cd", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Unitless]", 2.2, 0.01, "气动阻力系数 Cd (标准圆柱/钝体约 2.2)", "Aerodynamic drag coefficient"),
            new GMATParamDefinition("Cr", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Unitless]", 1.8, 0.01, "太阳光辐射反射系数 Cr (完全反射 2.0)", "Solar radiation pressure coefficient"),
            new GMATParamDefinition("ElapsedSecs", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[s]", 0.0, 1.0, "任务仿真积分经过总秒数", "Elapsed simulation time in seconds"),
            new GMATParamDefinition("ElapsedDays", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Days]", 0.0, 0.01, "任务仿真积分经过总天数", "Elapsed simulation time in days"),
            new GMATParamDefinition("A1ModJulian", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Day]", 21545.0, 0.001, "A1 修正儒略日时间历元 (A1ModJulian)", "A.1 modified Julian date epoch"),
            new GMATParamDefinition("UTCGregorian", "Hardware", ParamCapability.Reportable, "[UTC]", 0.0, 0.0, "UTC 格里高利历日期字符串 (如 01 Jan 2026 12:00:00.000)", "UTC Gregorian date string")
        };

        // =========================================================================
        // 9. THRUSTER & PROPULSION (推力器 TVC、多项式推力比冲系数与流量)
        // =========================================================================
        public static readonly List<GMATParamDefinition> ThrusterProperties = new List<GMATParamDefinition> {
            new GMATParamDefinition("ThrustDirection1", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[切向 +V/-V]", 1.0, 0.05, "切向推力矢量分量 (V: 顺行 +1 / 逆行 -1 主推进控制变量)", "Tangential velocity-aligned thrust vector component"),
            new GMATParamDefinition("ThrustDirection2", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[副法向 +N/-N]", 0.0, 0.05, "副法向推力偏转分量 (N: 变倾角与消除法向误差控制变量)", "Normal out-of-plane thrust vector component for inclination control"),
            new GMATParamDefinition("ThrustDirection3", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[法向/径向 +B/-B]", 0.0, 0.05, "法向/径向推力分量 (B: 径向高度与偏心率微调控制变量)", "Binormal/radial thrust vector component"),
            new GMATParamDefinition("C1", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[kN]", 100.0, 1.0, "推力器最大额定真空推力 (C1 常数系数)", "Maximum rated vacuum thrust magnitude"),
            new GMATParamDefinition("K1", "Thruster", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[s]", 300.0, 1.0, "推力器真空比冲 Isp (K1 常数系数)", "Thruster vacuum specific impulse"),
            new GMATParamDefinition("GravitationalAccel", "Thruster", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m/s²]", 9.80665, 0.0, "标准海平面重力加速度常数 (g0 = 9.80665)", "Standard gravity constant for Isp"),
            new GMATParamDefinition("DutyCycle", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[0~1]", 1.0, 0.05, "推力器工作占空比 (0.0 关机 ~ 1.0 满推力)", "Thruster operational duty cycle"),
            new GMATParamDefinition("ThrustScaleFactor", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[Scale]", 1.0, 0.01, "推力缩放因子 (ThrustScaleFactor)", "Thrust scale multiplier"),
            new GMATParamDefinition("MassFlowRate", "Thruster", ParamCapability.Reportable, "[kg/s]", 33.9, 0.1, "推进剂瞬时质量消耗率 (dm/dt = Thrust / (Isp * g0))", "Propellant mass flow rate")
        };

        // =========================================================================
        // 10. TANK & PRESSURIZATION (化学储罐、压力容积与密度)
        // =========================================================================
        public static readonly List<GMATParamDefinition> TankProperties = new List<GMATParamDefinition> {
            new GMATParamDefinition("FuelMass", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 1000.0, 1.0, "储罐当前剩余推进剂质量", "Tank current fuel mass"),
            new GMATParamDefinition("Volume", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m³]", 10.0, 0.1, "储罐内部几何总容积", "Tank internal geometric volume"),
            new GMATParamDefinition("FuelDensity", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg/m³]", 1000.0, 1.0, "推进剂物理体密度", "Propellant bulk density"),
            new GMATParamDefinition("Pressure", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kPa]", 1500.0, 10.0, "储罐当前内部工质压力", "Tank internal pressure"),
            new GMATParamDefinition("Temperature", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[K]", 293.15, 1.0, "推进剂当前工质温度", "Propellant bulk temperature"),
            new GMATParamDefinition("PressureModel", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Model]", 0.0, 0.0, "储罐增压模式 (PressureRegulated / Blowdown)", "Tank pressurization model")
        };

        // =========================================================================
        // 11. ATTITUDE & ORIENTATION (空间姿态、四元数、欧拉角与 MRP)
        // =========================================================================
        public static readonly List<GMATParamDefinition> AttitudeAndOrientation = new List<GMATParamDefinition> {
            new GMATParamDefinition("Q1", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 0.0, 0.01, "姿态四元数第一分量 Q1", "Attitude quaternion q1"),
            new GMATParamDefinition("Q2", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 0.0, 0.01, "姿态四元数第二分量 Q2", "Attitude quaternion q2"),
            new GMATParamDefinition("Q3", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 0.0, 0.01, "姿态四元数第三分量 Q3", "Attitude quaternion q3"),
            new GMATParamDefinition("Q4", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 1.0, 0.01, "姿态四元数标量分量 Q4", "Attitude quaternion scalar q4"),
            new GMATParamDefinition("EulerAngle1", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "欧拉角第一旋转角 (如偏航角 Yaw)", "First Euler angle"),
            new GMATParamDefinition("EulerAngle2", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "欧拉角第二旋转角 (如俯仰角 Pitch)", "Second Euler angle"),
            new GMATParamDefinition("EulerAngle3", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "欧拉角第三旋转角 (如滚转角 Roll)", "Third Euler angle"),
            new GMATParamDefinition("AngularVelocityX", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg/s]", 0.0, 0.1, "本体 X 轴瞬时旋转角速度", "Body angular velocity X"),
            new GMATParamDefinition("AngularVelocityY", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg/s]", 0.0, 0.1, "本体 Y 轴瞬时旋转角速度", "Body angular velocity Y"),
            new GMATParamDefinition("AngularVelocityZ", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg/s]", 0.0, 0.1, "本体 Z 轴瞬时旋转角速度", "Body angular velocity Z")
        };
    }
}
