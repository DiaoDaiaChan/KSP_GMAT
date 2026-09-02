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
        /// <summary>i18n 词典键 (param_*)，描述文本存于 loc/*.json，随当前语言热切换。</summary>
        public string DocKey { get; set; }

        public GMATParamDefinition(string name, string category, ParamCapability cap, string unit, double defVal, double step, string docKey) {
            Name = name;
            Category = category;
            Capability = cap;
            Unit = unit;
            DefaultValue = defVal;
            TypicalStep = step;
            DocKey = docKey;
        }

        /// <summary>本地化参数描述 (跟随当前语言，支持 zh/en/ru 等动态语言)。</summary>
        public string GetDoc() {
            return I18n.T(DocKey);
        }

        /// <summary>本地化单位显示 (param_XXX_unit 键存在时走 i18n，否则回退原始单位符号)。</summary>
        public string GetUnit() {
            return I18n.HasKey(DocKey + "_unit") ? I18n.T(DocKey + "_unit") : Unit;
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
            new GMATParamDefinition("SMA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "param_SMA"),
            new GMATParamDefinition("ECC", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "param_ECC"),
            new GMATParamDefinition("INC", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_INC"),
            new GMATParamDefinition("RAAN", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_RAAN"),
            new GMATParamDefinition("RADN", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 180.0, 1.0, "param_RADN"),
            new GMATParamDefinition("AOP", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_AOP"),
            new GMATParamDefinition("TA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_TA"),
            new GMATParamDefinition("MA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_MA"),
            new GMATParamDefinition("EA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_EA"),
            new GMATParamDefinition("HA", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_HA"),
            new GMATParamDefinition("MM", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg/s]", 0.00417, 0.0001, "param_MM"),
            new GMATParamDefinition("RadPer", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6578.14, 10.0, "param_RadPer"),
            new GMATParamDefinition("RadApo", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "param_RadApo"),
            new GMATParamDefinition("SemilatusRectum", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "param_SemilatusRectum"),
            new GMATParamDefinition("SMADot", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "param_SMADot"),
            new GMATParamDefinition("OrbitPeriod", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[s]", 86164.1, 10.0, "param_OrbitPeriod"),
            new GMATParamDefinition("VelApoapsis", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 1.64, 0.01, "param_VelApoapsis"),
            new GMATParamDefinition("VelPeriapsis", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 10.24, 0.01, "param_VelPeriapsis"),
            new GMATParamDefinition("C3Energy", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 0.0, 0.1, "param_C3Energy"),
            new GMATParamDefinition("Energy", "Keplerian", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", -4.72, 0.1, "param_Energy")
        };

        // =========================================================================
        // 2. EQUINOCTIAL & DELAUNAY ELEMENTS (春分点根数、修正春分点与德劳内根数 - 消除奇点)
        // =========================================================================
        public static readonly List<GMATParamDefinition> EquinoctialAndDelaunay = new List<GMATParamDefinition> {
            new GMATParamDefinition("ModEquinoctialF", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_ModEquinoctialF"),
            new GMATParamDefinition("ModEquinoctialG", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_ModEquinoctialG"),
            new GMATParamDefinition("ModEquinoctialH", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_ModEquinoctialH"),
            new GMATParamDefinition("ModEquinoctialK", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_ModEquinoctialK"),
            new GMATParamDefinition("TLONG", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_TLONG"),
            new GMATParamDefinition("TLONGDot", "ModEquinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg/s]", 0.00417, 0.0001, "param_TLONGDot"),
            new GMATParamDefinition("EquinoctialH", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_EquinoctialH"),
            new GMATParamDefinition("EquinoctialK", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_EquinoctialK"),
            new GMATParamDefinition("EquinoctialP", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_EquinoctialP"),
            new GMATParamDefinition("EquinoctialQ", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0, 0.001, "param_EquinoctialQ"),
            new GMATParamDefinition("MLONG", "Equinoctial", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_MLONG"),
            new GMATParamDefinition("Delaunayl", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_Delaunayl"),
            new GMATParamDefinition("Delaunayg", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_Delaunayg"),
            new GMATParamDefinition("Delaunayh", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_Delaunayh"),
            new GMATParamDefinition("DelaunayL", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "param_DelaunayL"),
            new GMATParamDefinition("DelaunayG", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "param_DelaunayG"),
            new GMATParamDefinition("DelaunayH", "Delaunay", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "param_DelaunayH")
        };

        // =========================================================================
        // 3. BROUWER-LYDDANE MEAN ELEMENTS (布劳威尔-利丹平根数 - J2摄动长期平均)
        // =========================================================================
        public static readonly List<GMATParamDefinition> BrouwerMeanElements = new List<GMATParamDefinition> {
            new GMATParamDefinition("BrouwerShortSMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "param_BrouwerShortSMA"),
            new GMATParamDefinition("BrouwerShortECC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "param_BrouwerShortECC"),
            new GMATParamDefinition("BrouwerShortINC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_BrouwerShortINC"),
            new GMATParamDefinition("BrouwerShortRAAN", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BrouwerShortRAAN"),
            new GMATParamDefinition("BrouwerShortAOP", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BrouwerShortAOP"),
            new GMATParamDefinition("BrouwerShortMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BrouwerShortMA"),
            new GMATParamDefinition("BrouwerLongSMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "param_BrouwerLongSMA"),
            new GMATParamDefinition("BrouwerLongECC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Unitless]", 0.0001, 0.001, "param_BrouwerLongECC"),
            new GMATParamDefinition("BrouwerLongINC", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_BrouwerLongINC"),
            new GMATParamDefinition("BrouwerLongRAAN", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BrouwerLongRAAN"),
            new GMATParamDefinition("BrouwerLongAOP", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BrouwerLongAOP"),
            new GMATParamDefinition("BrouwerLongMA", "Brouwer", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BrouwerLongMA")
        };

        // =========================================================================
        // 4. SPHERICAL & PLANETODETIC (球面坐标、大地测量坐标与天文时角)
        // =========================================================================
        public static readonly List<GMATParamDefinition> SphericalAndPlanetodetic = new List<GMATParamDefinition> {
            new GMATParamDefinition("Altitude", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 35786.0, 10.0, "param_Altitude"),
            new GMATParamDefinition("RMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 42164.14, 10.0, "param_RMAG"),
            new GMATParamDefinition("VMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 3.075, 0.01, "param_VMAG"),
            new GMATParamDefinition("AMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s²]", 0.0002, 0.0001, "param_AMAG"),
            new GMATParamDefinition("RA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_RA"),
            new GMATParamDefinition("DEC", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_DEC"),
            new GMATParamDefinition("RAV", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_RAV"),
            new GMATParamDefinition("DECV", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_DECV"),
            new GMATParamDefinition("AZI", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 90.0, 1.0, "param_AZI"),
            new GMATParamDefinition("FPA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "param_FPA"),
            new GMATParamDefinition("Latitude", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "param_Latitude"),
            new GMATParamDefinition("Longitude", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_Longitude"),
            new GMATParamDefinition("LST", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_LST"),
            new GMATParamDefinition("MHA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_MHA"),
            new GMATParamDefinition("DLA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_DLA"),
            new GMATParamDefinition("RLA", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_RLA"),
            new GMATParamDefinition("PlanetodeticLAT", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.01, "param_PlanetodeticLAT"),
            new GMATParamDefinition("PlanetodeticLON", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_PlanetodeticLON"),
            new GMATParamDefinition("PlanetodeticRMAG", "Spherical", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6378.14, 1.0, "param_PlanetodeticRMAG")
        };

        // =========================================================================
        // 5. HYPERBOLIC ASYMPTOTES & B-PLANE FLYBY (双曲渐近线进出场、B平面截距与深空引力助推)
        // =========================================================================
        public static readonly List<GMATParamDefinition> HyperbolicAndBPlane = new List<GMATParamDefinition> {
            new GMATParamDefinition("IncomingDHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_IncomingDHA"),
            new GMATParamDefinition("IncomingRHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_IncomingRHA"),
            new GMATParamDefinition("IncomingRadPer", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6678.0, 10.0, "param_IncomingRadPer"),
            new GMATParamDefinition("IncomingC3Energy", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 15.0, 0.1, "param_IncomingC3Energy"),
            new GMATParamDefinition("IncomingBVAZI", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_IncomingBVAZI"),
            new GMATParamDefinition("OutgoingDHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_OutgoingDHA"),
            new GMATParamDefinition("OutgoingRHA", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_OutgoingRHA"),
            new GMATParamDefinition("OutgoingRadPer", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 6678.0, 10.0, "param_OutgoingRadPer"),
            new GMATParamDefinition("OutgoingC3Energy", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s²]", 15.0, 0.1, "param_OutgoingC3Energy"),
            new GMATParamDefinition("OutgoingBVAZI", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_OutgoingBVAZI"),
            new GMATParamDefinition("BdotT", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 1.0, "param_BdotT"),
            new GMATParamDefinition("BdotR", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 1.0, "param_BdotR"),
            new GMATParamDefinition("BVectorMag", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 10000.0, 10.0, "param_BVectorMag"),
            new GMATParamDefinition("BVectorAngle", "Hyperbolic", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_BVectorAngle")
        };

        // =========================================================================
        // 6. EXTREMUM EVENTS (离散极值停机找根事件 - 严禁用于 Achieve 连续求解)
        // =========================================================================
        public static readonly List<GMATParamDefinition> ExtremumEvents = new List<GMATParamDefinition> {
            new GMATParamDefinition("Apoapsis", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "param_Apoapsis"),
            new GMATParamDefinition("Periapsis", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "param_Periapsis"),
            new GMATParamDefinition("Node", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "param_Node"),
            new GMATParamDefinition("AscendingNode", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "param_AscendingNode"),
            new GMATParamDefinition("DescendingNode", "ExtremumEvent", ParamCapability.DiscontinuousExtremumEvent, "[Event]", 0.0, 0.0, "param_DescendingNode")
        };

        // =========================================================================
        // 7. COORDINATE CARTESIAN & ANGULAR MOMENTUM (笛卡尔状态与角动量矢量)
        // =========================================================================
        public static readonly List<GMATParamDefinition> CoordinateCartesian = new List<GMATParamDefinition> {
            new GMATParamDefinition("Z", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "param_Z"),
            new GMATParamDefinition("X", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "param_X"),
            new GMATParamDefinition("Y", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km]", 0.0, 0.01, "param_Y"),
            new GMATParamDefinition("VX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "param_VX"),
            new GMATParamDefinition("VY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "param_VY"),
            new GMATParamDefinition("VZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s]", 0.0, 0.001, "param_VZ"),
            new GMATParamDefinition("AX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "param_AX"),
            new GMATParamDefinition("AY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "param_AY"),
            new GMATParamDefinition("AZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km/s²]", 0.0, 0.0001, "param_AZ"),
            new GMATParamDefinition("HMAG", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 60000.0, 10.0, "param_HMAG"),
            new GMATParamDefinition("HX", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "param_HX"),
            new GMATParamDefinition("HY", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 0.0, 10.0, "param_HY"),
            new GMATParamDefinition("HZ", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Km²/s]", 60000.0, 10.0, "param_HZ"),
            new GMATParamDefinition("BetaAngle", "Cartesian", ParamCapability.CoordinateCartesian | ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Deg]", 0.0, 0.1, "param_BetaAngle")
        };

        // =========================================================================
        // 8. SPACECRAFT HARDWARE & TIME (航天器结构、质量、气动光压与历元时间)
        // =========================================================================
        public static readonly List<GMATParamDefinition> SpacecraftHardware = new List<GMATParamDefinition> {
            new GMATParamDefinition("TotalMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 5000.0, 1.0, "param_TotalMass"),
            new GMATParamDefinition("DryMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 850.0, 1.0, "param_DryMass"),
            new GMATParamDefinition("FuelMass", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 4000.0, 1.0, "param_FuelMass"),
            new GMATParamDefinition("DragArea", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m²]", 15.0, 0.1, "param_DragArea"),
            new GMATParamDefinition("SRPArea", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m²]", 15.0, 0.1, "param_SRPArea"),
            new GMATParamDefinition("Cd", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Unitless]", 2.2, 0.01, "param_Cd"),
            new GMATParamDefinition("Cr", "Hardware", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Unitless]", 1.8, 0.01, "param_Cr"),
            new GMATParamDefinition("ElapsedSecs", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[s]", 0.0, 1.0, "param_ElapsedSecs"),
            new GMATParamDefinition("ElapsedDays", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Days]", 0.0, 0.01, "param_ElapsedDays"),
            new GMATParamDefinition("A1ModJulian", "Hardware", ParamCapability.ContinuouslyDifferentiable | ParamCapability.Reportable, "[Day]", 21545.0, 0.001, "param_A1ModJulian"),
            new GMATParamDefinition("UTCGregorian", "Hardware", ParamCapability.Reportable, "[UTC]", 0.0, 0.0, "param_UTCGregorian")
        };

        // =========================================================================
        // 9. THRUSTER & PROPULSION (推力器 TVC、多项式推力比冲系数与流量)
        // =========================================================================
        public static readonly List<GMATParamDefinition> ThrusterProperties = new List<GMATParamDefinition> {
            new GMATParamDefinition("ThrustDirection1", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[切向 +V/-V]", 1.0, 0.05, "param_ThrustDirection1"),
            new GMATParamDefinition("ThrustDirection2", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[副法向 +N/-N]", 0.0, 0.05, "param_ThrustDirection2"),
            new GMATParamDefinition("ThrustDirection3", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[法向/径向 +B/-B]", 0.0, 0.05, "param_ThrustDirection3"),
            new GMATParamDefinition("C1", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[N]", 100.0, 1.0, "param_C1"),
            new GMATParamDefinition("K1", "Thruster", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[s]", 300.0, 1.0, "param_K1"),
            new GMATParamDefinition("DutyCycle", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[0~1]", 1.0, 0.05, "param_DutyCycle"),
            new GMATParamDefinition("ThrustScaleFactor", "Thruster", ParamCapability.ControlVariable | ParamCapability.Reportable, "[Scale]", 1.0, 0.01, "param_ThrustScaleFactor"),
            new GMATParamDefinition("MassFlowRate", "Thruster", ParamCapability.Reportable, "[kg/s]", 33.9, 0.1, "param_MassFlowRate")
        };

        // =========================================================================
        // 10. TANK & PRESSURIZATION (化学储罐、压力容积与密度)
        // =========================================================================
        public static readonly List<GMATParamDefinition> TankProperties = new List<GMATParamDefinition> {
            new GMATParamDefinition("FuelMass", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg]", 1000.0, 1.0, "param_FuelMass_Tank"),
            new GMATParamDefinition("Volume", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[m³]", 10.0, 0.1, "param_Volume"),
            new GMATParamDefinition("FuelDensity", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kg/m³]", 1000.0, 1.0, "param_FuelDensity"),
            new GMATParamDefinition("Pressure", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[kPa]", 1500.0, 10.0, "param_Pressure"),
            new GMATParamDefinition("Temperature", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[K]", 293.15, 1.0, "param_Temperature"),
            new GMATParamDefinition("PressureModel", "Tank", ParamCapability.HardwareProperty | ParamCapability.Reportable, "[Model]", 0.0, 0.0, "param_PressureModel")
        };

        // =========================================================================
        // 11. ATTITUDE & ORIENTATION (空间姿态、四元数、欧拉角与 MRP)
        // =========================================================================
        public static readonly List<GMATParamDefinition> AttitudeAndOrientation = new List<GMATParamDefinition> {
            new GMATParamDefinition("Q1", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 0.0, 0.01, "param_Q1"),
            new GMATParamDefinition("Q2", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 0.0, 0.01, "param_Q2"),
            new GMATParamDefinition("Q3", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 0.0, 0.01, "param_Q3"),
            new GMATParamDefinition("Q4", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Unitless]", 1.0, 0.01, "param_Q4"),
            new GMATParamDefinition("EulerAngle1", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_EulerAngle1"),
            new GMATParamDefinition("EulerAngle2", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_EulerAngle2"),
            new GMATParamDefinition("EulerAngle3", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg]", 0.0, 1.0, "param_EulerAngle3"),
            new GMATParamDefinition("AngularVelocityX", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg/s]", 0.0, 0.1, "param_AngularVelocityX"),
            new GMATParamDefinition("AngularVelocityY", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg/s]", 0.0, 0.1, "param_AngularVelocityY"),
            new GMATParamDefinition("AngularVelocityZ", "Attitude", ParamCapability.AttitudeOrientation | ParamCapability.Reportable, "[Deg/s]", 0.0, 0.1, "param_AngularVelocityZ")
        };
    }
}
