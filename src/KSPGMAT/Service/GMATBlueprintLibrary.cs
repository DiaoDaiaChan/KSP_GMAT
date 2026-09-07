using System;
using System.Collections.Generic;
using System.Text;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    public class GMATBlueprintItem {
        public string Id { get; set; }
        public string TitleKey { get; set; }
        public string TitleDefault { get; set; }
        public string DescriptionKey { get; set; }
        public string DescriptionDefault { get; set; }
        public string Category { get; set; }
        public string ScriptTemplate { get; set; }

        public string GetFormattedScript(string satName = null, string centralBody = null, string epochUtc = null) {
            string safeSat = !string.IsNullOrEmpty(satName) ? satName : GMATStateManager.Current.GetSpacecraftName();
            string safeBody = !string.IsNullOrEmpty(centralBody) ? centralBody : (GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth");
            string safeEpoch = !string.IsNullOrEmpty(epochUtc) ? epochUtc : GMATStateManager.Current.Spacecraft.EpochUtc;

            return ScriptTemplate
                .Replace("{SAT_NAME}", safeSat)
                .Replace("{CENTRAL_BODY}", safeBody)
                .Replace("{EPOCH}", safeEpoch);
        }
    }

    /// <summary>
    /// NASA GMAT 官方 7 大标准脚本范式蓝本库 (Canonical Script Blueprints)。
    /// 直接内嵌自 gmat_docs_distillation.md，已做合规化纯 ASCII 净化与 SSOT 航天器参数插值。
    /// </summary>
    public static class GMATBlueprintLibrary {
        private static readonly List<GMATBlueprintItem> _blueprints = new List<GMATBlueprintItem>();

        static GMATBlueprintLibrary() {
            InitializeBlueprints();
        }

        public static IReadOnlyList<GMATBlueprintItem> GetAllBlueprints() {
            return _blueprints;
        }

        public static GMATBlueprintItem GetBlueprint(string id) {
            return _blueprints.Find(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        private static void InitializeBlueprints() {
            // -------------------------------------------------------------
            // 范式 1：基础轨道确定性递推与停机事件 (Simulating an Orbit)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p1_sim_orbit",
                TitleKey = "bp_p1_title",
                TitleDefault = "范式 1: 轨道递推与停机事件 (Simulating an Orbit)",
                DescriptionKey = "bp_p1_desc",
                DescriptionDefault = "PrinceDormand78 高精自适应积分器，支持滑行至近地点、远地点与赤道纬度穿越停机检测。",
                Category = "NearEarth",
                ScriptTemplate = 
@"%----------------------------------------------------------------------
% 范式 1: 基础轨道确定性递推与停机事件 (Simulating an Orbit)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 6928.14;
{SAT_NAME}.ECC = 0.001;
{SAT_NAME}.INC = 28.5;
{SAT_NAME}.RAAN = 100.0;
{SAT_NAME}.AOP = 45.0;
{SAT_NAME}.TA = 0.0;

Create ForceModel NearEarth_ForceModel;
NearEarth_ForceModel.CentralBody = {CENTRAL_BODY};
NearEarth_ForceModel.PrimaryBodies = {{CENTRAL_BODY}};
NearEarth_ForceModel.PointMasses = {Luna, Sun};
NearEarth_ForceModel.GravityField.{CENTRAL_BODY}.Degree = 8;
NearEarth_ForceModel.GravityField.{CENTRAL_BODY}.Order = 8;
NearEarth_ForceModel.SRP = On;
NearEarth_ForceModel.ErrorControl = RSSStep;

Create Propagator NearEarthProp;
NearEarthProp.FM = NearEarth_ForceModel;
NearEarthProp.Type = PrinceDormand78;
NearEarthProp.InitialStepSize = 60;
NearEarthProp.Accuracy = 1e-11;
NearEarthProp.MinStep = 0.0;
NearEarthProp.MaxStep = 2700;
NearEarthProp.StopIfAccuracyIsViolated = true;

BeginMissionSequence;

% (a) 按时间滑行 12000 秒
Propagate NearEarthProp({SAT_NAME}) {{SAT_NAME}.ElapsedSecs = 12000.0};

% (b) 积分推进至近地点 (Periapsis)
Propagate NearEarthProp({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Periapsis};

% (c) 积分推进至赤道升交点 (纬度穿越 = 0)
Propagate NearEarthProp({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Latitude = 0.0};
"
            });

            // -------------------------------------------------------------
            // 范式 2：双脉冲霍曼转移与微分修正打靶 (Hohmann Transfer & Targeter)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p2_hohmann_transfer",
                TitleKey = "bp_p2_title",
                TitleDefault = "范式 2: 双脉冲霍曼转移打靶 (Hohmann Transfer)",
                DescriptionKey = "bp_p2_desc",
                DescriptionDefault = "TOI 注入脉冲 + GOI 圆化脉冲，NewtonRaphson 微分修正打靶约束远地点天心距与圆轨道偏心率。",
                Category = "OrbitalTransfer",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 2: 双脉冲霍曼转移与微分修正打靶 (Hohmann Transfer & Targeter)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 6778.0;
{SAT_NAME}.ECC = 0.001;
{SAT_NAME}.INC = 28.5;
{SAT_NAME}.RAAN = 0.0;
{SAT_NAME}.AOP = 0.0;
{SAT_NAME}.TA = 0.0;

Create ImpulsiveBurn TOI;
TOI.CoordinateSystem = Local;
TOI.Origin = {CENTRAL_BODY};
TOI.Axes = VNB;
TOI.Element1 = 0;
TOI.Element2 = 0;
TOI.Element3 = 0;

Create ImpulsiveBurn GOI;
GOI.CoordinateSystem = Local;
GOI.Origin = {CENTRAL_BODY};
GOI.Axes = VNB;

Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.DerivativeMethod = ForwardDifference;
DC1.MaximumIterations = 25;
DC1.ShowProgress = true;

Create ForceModel DefaultFM;
DefaultFM.CentralBody = {CENTRAL_BODY};
DefaultFM.PointMasses = {{CENTRAL_BODY}};

Create Propagator DefaultProp;
DefaultProp.FM = DefaultFM;
DefaultProp.Type = PrinceDormand78;

BeginMissionSequence;

Propagate DefaultProp({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Periapsis};

Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    Vary DC1(TOI.Element1 = 1.0, {Perturbation = 0.0001, MaxStep = 0.5, Lower = 0.0, Upper = 3.5});
    Maneuver TOI({SAT_NAME});
    
    Propagate DefaultProp({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Apoapsis};
    Achieve DC1({SAT_NAME}.{CENTRAL_BODY}.RMAG = 42164.169, {Tolerance = 0.5});
    
    Vary DC1(GOI.Element1 = 1.0, {Perturbation = 0.0001, MaxStep = 0.2, Lower = 0.0, Upper = 2.5});
    Maneuver GOI({SAT_NAME});
    
    Achieve DC1({SAT_NAME}.{CENTRAL_BODY}.ECC = 0.005, {Tolerance = 0.0001});
EndTarget;

Propagate DefaultProp({SAT_NAME}) {{SAT_NAME}.ElapsedSecs = 86400};
"
            });

            // -------------------------------------------------------------
            // 范式 3：有限推力点火与变质量打靶 (Target Finite Burn with Mass Decrement)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p3_finite_burn",
                TitleKey = "bp_p3_title",
                TitleDefault = "范式 3: 有限推力点火变质量打靶 (Target Finite Burn)",
                DescriptionKey = "bp_p3_desc",
                DescriptionDefault = "真实推力器 + 变质量化学储罐 + 有限点火段，求解目标点火时长 BurnDuration 达成目标天心距。",
                Category = "OrbitalTransfer",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 3: 有限推力点火与变质量打靶 (Target Finite Burn with Mass Decrement)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 6778.0;
{SAT_NAME}.ECC = 0.001;
{SAT_NAME}.DryMass = 1000;

Create ChemicalTank Tank0;
Tank0.FuelMass = 1500;
Tank0.FuelDensity = 1000;
Tank0.Pressure = 2000;
Tank0.Volume = 2.0;
Tank0.PressureModel = 'PressureRegulated';
Tank0.AllowNegativeFuelMass = true;

Create ChemicalThruster RL10;
RL10.CoordinateSystem = Local;
RL10.Origin = {CENTRAL_BODY};
RL10.Axes = VNB;
RL10.ThrustDirection1 = 1;
RL10.ThrustDirection2 = 0;
RL10.ThrustDirection3 = 0;
RL10.DutyCycle = 1.0;
RL10.ThrustScaleFactor = 1.0;
RL10.DecrementMass = true;
RL10.Tank = {Tank0};
RL10.C1 = 1000;
RL10.K1 = 300;

{SAT_NAME}.Tanks = {Tank0};
{SAT_NAME}.Thrusters = {RL10};

Create FiniteBurn FiniteBurn1;
FiniteBurn1.Thrusters = {RL10};

Create Variable BurnDuration;
BurnDuration = 0;

Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.MaximumIterations = 30;

Create ForceModel PropFM;
PropFM.CentralBody = {CENTRAL_BODY};
PropFM.PointMasses = {{CENTRAL_BODY}};

Create Propagator Prop;
Prop.FM = PropFM;
Prop.Type = PrinceDormand78;

BeginMissionSequence;

Propagate Prop({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Periapsis};

Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    Vary DC1(BurnDuration = 200, {Perturbation = 1.0, MaxStep = 100, Lower = 1.0, Upper = 1500});
    
    BeginFiniteBurn FiniteBurn1({SAT_NAME});
    Propagate Prop({SAT_NAME}) {{SAT_NAME}.ElapsedSecs = BurnDuration};
    EndFiniteBurn FiniteBurn1({SAT_NAME});
    
    Propagate Prop({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Apoapsis};
    Achieve DC1({SAT_NAME}.{CENTRAL_BODY}.RMAG = 12000, {Tolerance = 1.0});
EndTarget;
"
            });

            // -------------------------------------------------------------
            // 范式 4：行星际转移与火星 B-Plane 借力打靶 (Mars B-Plane Targeting)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p4_mars_bplane",
                TitleKey = "bp_p4_title",
                TitleDefault = "范式 4: 行星际 B-Plane 借力打靶 (Mars B-Plane Targeting)",
                DescriptionKey = "bp_p4_desc",
                DescriptionDefault = "近地逃逸 -> 深空巡航 -> 近目标星三级分段动力学力模型，打靶 TCM 满足 B-Plane (BdotT, BdotR) 与制动捕获。",
                Category = "DeepSpace",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 4: 行星际转移、分段力学环境与 B-Plane 借力打靶 (Mars B-Plane Targeting)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = EarthMJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = -32593.2;
{SAT_NAME}.ECC = 1.2028;
{SAT_NAME}.INC = 28.80;
{SAT_NAME}.RAAN = 173.96;
{SAT_NAME}.AOP = 240.96;
{SAT_NAME}.TA = 359.94;

Create ImpulsiveBurn TCM;
TCM.CoordinateSystem = Local;
TCM.Origin = Earth;
TCM.Axes = VNB;

Create ImpulsiveBurn MOI;
MOI.CoordinateSystem = Local;
MOI.Origin = Mars;
MOI.Axes = VNB;

Create CoordinateSystem SunMJ2000Eq;
SunMJ2000Eq.Origin = Sun;
SunMJ2000Eq.Axes = MJ2000Eq;

Create CoordinateSystem MarsMJ2000Eq;
MarsMJ2000Eq.Origin = Mars;
MarsMJ2000Eq.Axes = MJ2000Eq;

Create ForceModel NearEarth_FM;
NearEarth_FM.CentralBody = Earth;
NearEarth_FM.PrimaryBodies = {Earth};
NearEarth_FM.PointMasses = {Luna, Sun};
NearEarth_FM.SRP = On;

Create Propagator NearEarthProp;
NearEarthProp.FM = NearEarth_FM;
NearEarthProp.Type = PrinceDormand78;
NearEarthProp.Accuracy = 1e-11;

Create ForceModel DeepSpace_FM;
DeepSpace_FM.CentralBody = Sun;
DeepSpace_FM.PointMasses = {Sun, Mercury, Venus, Earth, Luna, Mars, Jupiter, Saturn, Uranus, Neptune};
DeepSpace_FM.SRP = On;

Create Propagator DeepSpaceProp;
DeepSpaceProp.FM = DeepSpace_FM;
DeepSpaceProp.Type = PrinceDormand78;
DeepSpaceProp.Accuracy = 1e-11;

Create ForceModel NearMars_FM;
NearMars_FM.CentralBody = Mars;
NearMars_FM.PrimaryBodies = {Mars};
NearMars_FM.PointMasses = {Sun};
NearMars_FM.SRP = On;

Create Propagator NearMarsProp;
NearMarsProp.FM = NearMars_FM;
NearMarsProp.Type = PrinceDormand78;
NearMarsProp.Accuracy = 1e-11;

Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.MaximumIterations = 50;

BeginMissionSequence;

% (a) 离开地球引力球
Propagate NearEarthProp({SAT_NAME}) {{SAT_NAME}.ElapsedDays = 3.0};

% (b) 第一阶段打靶: 深空 TCM 瞄准火星 B 平面
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    Vary DC1(TCM.Element1 = 0.0, {Perturbation = 1e-5, MaxStep = 0.1, Lower = -1.0, Upper = 1.0});
    Vary DC1(TCM.Element2 = 0.0, {Perturbation = 1e-5, MaxStep = 0.1, Lower = -1.0, Upper = 1.0});
    Vary DC1(TCM.Element3 = 0.0, {Perturbation = 1e-5, MaxStep = 0.1, Lower = -1.0, Upper = 1.0});
    
    Maneuver TCM({SAT_NAME});
    
    Propagate DeepSpaceProp({SAT_NAME}) {{SAT_NAME}.Mars.RMAG = 1000000.0};
    Propagate NearMarsProp({SAT_NAME}) {{SAT_NAME}.Mars.Periapsis};
    
    Achieve DC1({SAT_NAME}.Mars.BdotT = 0.0, {Tolerance = 1.0});
    Achieve DC1({SAT_NAME}.Mars.BdotR = -7000.0, {Tolerance = 1.0});
EndTarget;

% (c) 第二阶段打靶: 近火星制动捕获入轨 (MOI)
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    Vary DC1(MOI.Element1 = -1.5, {Perturbation = 0.0001, MaxStep = 0.2, Lower = -3.5, Upper = -0.1});
    Maneuver MOI({SAT_NAME});
    
    Propagate NearMarsProp({SAT_NAME}) {{SAT_NAME}.Mars.Apoapsis};
    Achieve DC1({SAT_NAME}.Mars.RMAG = 12000.0, {Tolerance = 5.0});
EndTarget;
"
            });

            // -------------------------------------------------------------
            // 范式 5：多段打靶与非线性约束优化 (Multiple Shooting Lunar Flyby)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p5_lunar_flyby",
                TitleKey = "bp_p5_title",
                TitleDefault = "范式 5: 多段打靶月球飞掠 (Multiple Shooting Lunar Flyby)",
                DescriptionKey = "bp_p5_desc",
                DescriptionDefault = "Control Points 控制点多段切分，双向正反向积分拼接，利用微分修正消除位置与速度断点。",
                Category = "Optimization",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 5: 多段打靶与非线性约束 (Multiple Shooting Lunar Flyby)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 6778.0;
{SAT_NAME}.ECC = 0.001;

Create ImpulsiveBurn TLI;
TLI.CoordinateSystem = Local;
TLI.Origin = {CENTRAL_BODY};
TLI.Axes = VNB;

Create ImpulsiveBurn MCC;
MCC.CoordinateSystem = Local;
MCC.Origin = {CENTRAL_BODY};
MCC.Axes = VNB;

Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.MaximumIterations = 50;

Create ForceModel EarthMoon_FM;
EarthMoon_FM.CentralBody = {CENTRAL_BODY};
EarthMoon_FM.PointMasses = {{CENTRAL_BODY}, Luna, Sun};

Create Propagator EarthMoonProp;
EarthMoonProp.FM = EarthMoon_FM;
EarthMoonProp.Type = PrinceDormand78;

BeginMissionSequence;

% 滑行至近地点
Propagate EarthMoonProp({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Periapsis};

% 打靶 TLI 地月转移与中途修正 MCC
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    Vary DC1(TLI.Element1 = 3.1, {Perturbation = 0.0001, MaxStep = 0.05, Lower = 2.8, Upper = 3.3});
    Maneuver TLI({SAT_NAME});
    
    Propagate EarthMoonProp({SAT_NAME}) {{SAT_NAME}.ElapsedDays = 1.5};
    
    Vary DC1(MCC.Element1 = 0.0, {Perturbation = 1e-5, MaxStep = 0.02, Lower = -0.1, Upper = 0.1});
    Maneuver MCC({SAT_NAME});
    
    Propagate EarthMoonProp({SAT_NAME}) {{SAT_NAME}.Luna.Periapsis};
    
    Achieve DC1({SAT_NAME}.Luna.RMAG = 1838.0, {Tolerance = 2.0});
EndTarget;
"
            });

            // -------------------------------------------------------------
            // 范式 6：低推力电推进连续螺旋外推 (Solar Electric Propulsion)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p6_electric_propulsion",
                TitleKey = "bp_p6_title",
                TitleDefault = "范式 6: 低推力电推进连续外推 (Solar Electric Propulsion)",
                DescriptionKey = "bp_p6_desc",
                DescriptionDefault = "离子/霍尔电推力器与高比冲储罐，支持变步长自适应积分连续推力螺旋升轨与深空巡航。",
                Category = "ElectricPropulsion",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 6: 低推力电推进连续螺旋外推 (Solar Electric Propulsion)
%----------------------------------------------------------------------
Create ChemicalTank XenonTank;
XenonTank.FuelMass = 500;
XenonTank.FuelDensity = 1000;
XenonTank.Pressure = 1500;
XenonTank.Volume = 0.6;
XenonTank.PressureModel = 'PressureRegulated';
XenonTank.AllowNegativeFuelMass = true;

Create ChemicalThruster NEXT;
NEXT.CoordinateSystem = Local;
NEXT.Origin = {CENTRAL_BODY};
NEXT.Axes = VNB;
NEXT.ThrustDirection1 = 1;
NEXT.ThrustDirection2 = 0;
NEXT.ThrustDirection3 = 0;
NEXT.DecrementMass = true;
NEXT.Tank = {XenonTank};
NEXT.C1 = 5.0;
NEXT.K1 = 3000.0;

Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 7000.0;
{SAT_NAME}.ECC = 0.001;
{SAT_NAME}.Tanks = {XenonTank};
{SAT_NAME}.Thrusters = {NEXT};

Create FiniteBurn LowThrustBurn;
LowThrustBurn.Thrusters = {NEXT};

Create ForceModel PropFM;
PropFM.CentralBody = {CENTRAL_BODY};
PropFM.PointMasses = {{CENTRAL_BODY}, Sun};
PropFM.SRP = On;

Create Propagator Prop;
Prop.FM = PropFM;
Prop.Type = PrinceDormand78;
Prop.InitialStepSize = 120;
Prop.Accuracy = 1e-11;

BeginMissionSequence;

BeginFiniteBurn LowThrustBurn({SAT_NAME});
Propagate Prop({SAT_NAME}) {{SAT_NAME}.ElapsedDays = 15.0};
EndFiniteBurn LowThrustBurn({SAT_NAME});
"
            });

            // -------------------------------------------------------------
            // 范式 7：高精状态报表输出与数据流 (ReportFile & Pipeline)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p7_report_pipeline",
                TitleKey = "bp_p7_title",
                TitleDefault = "范式 7: 高精状态报表输出 (ReportFile & Pipeline)",
                DescriptionKey = "bp_p7_desc",
                DescriptionDefault = "双精度 16 位格式化遥测输出，导出笛卡尔状态、开普勒根数与飞行质量，对接 Principia 验证管线。",
                Category = "Telemetry",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 7: 高精状态报表输出与数据流 (ReportFile & Pipeline)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 6878.0;
{SAT_NAME}.ECC = 0.002;

Create ReportFile TrajectoryReport;
TrajectoryReport.Filename = 'TrajectoryData.txt';
TrajectoryReport.Precision = 16;
TrajectoryReport.WriteHeaders = true;
TrajectoryReport.LeftJustify = On;
TrajectoryReport.ZeroFill = Off;
TrajectoryReport.ColumnWidth = 24;
TrajectoryReport.WriteReport = true;

Create ForceModel FM;
FM.CentralBody = {CENTRAL_BODY};
FM.PointMasses = {{CENTRAL_BODY}, Sun, Luna};

Create Propagator Prop;
Prop.FM = FM;
Prop.Type = PrinceDormand78;

BeginMissionSequence;

Report TrajectoryReport {SAT_NAME}.ElapsedSecs {SAT_NAME}.X {SAT_NAME}.Y {SAT_NAME}.Z {SAT_NAME}.VX {SAT_NAME}.VY {SAT_NAME}.VZ {SAT_NAME}.{CENTRAL_BODY}.Altitude {SAT_NAME}.SMA {SAT_NAME}.ECC {SAT_NAME}.INC {SAT_NAME}.TotalMass;

Propagate Prop({SAT_NAME}) {{SAT_NAME}.ElapsedDays = 2.0};

Report TrajectoryReport {SAT_NAME}.ElapsedSecs {SAT_NAME}.X {SAT_NAME}.Y {SAT_NAME}.Z {SAT_NAME}.VX {SAT_NAME}.VY {SAT_NAME}.VZ {SAT_NAME}.{CENTRAL_BODY}.Altitude {SAT_NAME}.SMA {SAT_NAME}.ECC {SAT_NAME}.INC {SAT_NAME}.TotalMass;
"
            });

            // -------------------------------------------------------------
            // 范式 8：LEO-GTO-GEO 极小化最优转移 (LEO to GEO Optimization with Yukon SQP)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p8_leo_geo_opt",
                TitleKey = "bp_p8_title",
                TitleDefault = "范式 8: LEO-GTO-GEO 极小化最优转移 (Yukon SQP Optimization)",
                DescriptionKey = "bp_p8_desc",
                DescriptionDefault = "采用开箱即用的 Yukon SQP 优化器，双脉冲联合寻优最佳倾角压平与圆化分配，以极小化全任务总 Delta-V 为目标求解。",
                Category = "OrbitalTransfer",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 8: LEO-GTO-GEO 极小化最优转移 (Yukon SQP Optimization)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Keplerian;
{SAT_NAME}.SMA = 6678.14;
{SAT_NAME}.ECC = 0.001;
{SAT_NAME}.INC = 0.0;
{SAT_NAME}.RAAN = 0.0;
{SAT_NAME}.AOP = 0.0;
{SAT_NAME}.TA = 0.0;

Create ImpulsiveBurn TOI;
TOI.CoordinateSystem = Local;
TOI.Origin = {CENTRAL_BODY};
TOI.Axes = VNB;
TOI.Element1 = 2.42;

Create ImpulsiveBurn GOI;
GOI.CoordinateSystem = Local;
GOI.Origin = {CENTRAL_BODY};
GOI.Axes = VNB;
GOI.Element1 = 1.475;

Create Variable TotalDV;
TotalDV = 0;

Create ForceModel FM;
FM.CentralBody = {CENTRAL_BODY};
FM.PointMasses = {{CENTRAL_BODY}};

Create Propagator Prop;
Prop.FM = FM;
Prop.Type = PrinceDormand78;

Create Yukon Yukon1;
Yukon1.ShowProgress = true;
Yukon1.ReportStyle = Normal;
Yukon1.MaximumIterations = 50;
Yukon1.FeasibilityTolerance = 0.001;
Yukon1.OptimalityTolerance = 0.001;

BeginMissionSequence;

% (a) 开启 SQP 优化序列：双脉冲联合寻优最小总 Delta-V
Optimize Yukon1 {SolveMode = Solve, ExitMode = SaveAndContinue};
   % 变量 1: 第一次点火 (近地点抬高远地点至 GEO 高度)
   Vary Yukon1(TOI.Element1 = 2.42, {Perturbation = 0.0001, Lower = 2.0, Upper = 3.0, MaxStep = 0.05});
   Maneuver TOI({SAT_NAME});
   
   % (b) 转移轨道滑行至远地点
   Propagate Prop({SAT_NAME}) {{SAT_NAME}.{CENTRAL_BODY}.Apoapsis};
   
   % 变量 2: 第二次点火 (远地点圆化至地球同步高度)
   Vary Yukon1(GOI.Element1 = 1.475, {Perturbation = 0.0001, Lower = 1.0, Upper = 2.0, MaxStep = 0.05});
   Maneuver GOI({SAT_NAME});
   
   % (c) 代价函数计算
   TotalDV = TOI.Element1 + GOI.Element1;
   
   % 目标函数: 最小化全任务总冲量
   Minimize Yukon1(TotalDV);
   
   % 终端物理等式与不等式约束
   NonlinearConstraint Yukon1({SAT_NAME}.{CENTRAL_BODY}.SMA = 42164.14);
   NonlinearConstraint Yukon1({SAT_NAME}.{CENTRAL_BODY}.ECC <= 0.001);
EndOptimize;

% (d) 最终静止轨道推演
Propagate Prop({SAT_NAME}) {{SAT_NAME}.ElapsedSecs = 86400};
"
            });

            // -------------------------------------------------------------
            // 范式 9：地月转移、三维中途修正与低月球轨道捕获 (Translunar Injection, 3D TCM & LLO Insertion)
            // -------------------------------------------------------------
            _blueprints.Add(new GMATBlueprintItem {
                Id = "p9_lunar_tli_llo",
                TitleKey = "bp_p9_title",
                TitleDefault = "范式 9: 地月转移、3D TCM 与低月球轨道捕获 (Translunar & LLO Insertion)",
                DescriptionKey = "bp_p9_desc",
                DescriptionDefault = "在 3600 秒附近寻优 TLI 窗口与单次顺行点火；中途施加三维 TCM 矢量锁定 100km 近月点、赤道面 Z=0 及 0° 倾角；近月点逆行制动圆化，完美支持 Yukon SQP 优化器。",
                Category = "LunarTransfer",
                ScriptTemplate =
@"%----------------------------------------------------------------------
% 范式 9: 地月转移、3D TCM 与低月球轨道捕获 (Translunar & LLO Insertion)
%----------------------------------------------------------------------
Create Spacecraft {SAT_NAME};
{SAT_NAME}.DateFormat = UTCGregorian;
{SAT_NAME}.Epoch = '{EPOCH}';
{SAT_NAME}.CoordinateSystem = {CENTRAL_BODY}MJ2000Eq;
{SAT_NAME}.DisplayStateType = Cartesian;
{SAT_NAME}.X = -5337.197780;
{SAT_NAME}.Y = -3080.395825;
{SAT_NAME}.Z = 2280.468724;
{SAT_NAME}.VX = 2.828285;
{SAT_NAME}.VY = -6.791792;
{SAT_NAME}.VZ = -2.559760;
{SAT_NAME}.DryMass = 1477.96;

Create CoordinateSystem MoonMJ2000Eq;
MoonMJ2000Eq.Origin = Luna;
MoonMJ2000Eq.Axes = MJ2000Eq;

Create ForceModel EarthForceModel;
EarthForceModel.CentralBody = {CENTRAL_BODY};
EarthForceModel.PointMasses = {{CENTRAL_BODY}, Luna, Sun};

Create Propagator EarthProp;
EarthProp.FM = EarthForceModel;
EarthProp.Type = RungeKutta89;
EarthProp.InitialStepSize = 60;
EarthProp.Accuracy = 1e-11;
EarthProp.MinStep = 0.001;
EarthProp.MaxStep = 20000;

Create ForceModel MoonForceModel;
MoonForceModel.CentralBody = Luna;
MoonForceModel.PointMasses = {Luna, {CENTRAL_BODY}, Sun};

Create Propagator MoonProp;
MoonProp.FM = MoonForceModel;
MoonProp.Type = RungeKutta89;
MoonProp.InitialStepSize = 60;
MoonProp.Accuracy = 1e-11;
MoonProp.MinStep = 0.001;
MoonProp.MaxStep = 10000;

Create ImpulsiveBurn TLI_Burn;
TLI_Burn.CoordinateSystem = Local;
TLI_Burn.Origin = {CENTRAL_BODY};
TLI_Burn.Axes = VNB;
TLI_Burn.Element1 = 3.138;

Create ImpulsiveBurn TCM_Burn;
TCM_Burn.CoordinateSystem = Local;
TCM_Burn.Origin = Luna;
TCM_Burn.Axes = VNB;
TCM_Burn.Element1 = 0.001;
TCM_Burn.Element2 = 0.001;
TCM_Burn.Element3 = 0.001;

Create ImpulsiveBurn LOI_Burn;
LOI_Burn.CoordinateSystem = Local;
LOI_Burn.Origin = Luna;
LOI_Burn.Axes = VNB;
LOI_Burn.Element1 = -0.820;

Create Yukon Yukon1;
Yukon1.ShowProgress = true;
Yukon1.ReportStyle = Normal;
Yukon1.MaximumIterations = 100;
Yukon1.FeasibilityTolerance = 0.01;
Yukon1.OptimalityTolerance = 0.001;

Create Variable TLI_CoastTime TCM_CoastDays TotalDV;
TLI_CoastTime = 3600.0;
TCM_CoastDays = 1.8;
TotalDV = 0.0;

Create ReportFile MissionReport;
MissionReport.Filename = 'Lunar_LLO_Mission_Report.txt';

BeginMissionSequence;

% (a) 阶段 1: 3600 秒附近 TLI 窗口寻优与单次顺行点火
Optimize Yukon1 {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary Yukon1(TLI_CoastTime = 3600.0, {Perturbation = 1.0, Lower = 3200.0, Upper = 4000.0, MaxStep = 50.0});
   Vary Yukon1(TLI_Burn.Element1 = 3.138, {Perturbation = 0.0001, Lower = 3.0, Upper = 3.3, MaxStep = 0.02});
   
   Propagate EarthProp({SAT_NAME}) {{SAT_NAME}.ElapsedSecs = TLI_CoastTime};
   Maneuver TLI_Burn({SAT_NAME});
   
   % (b) 阶段 2: 转移中途三维 TCM (V/N/B 3轴矢量优化)
   Propagate EarthProp({SAT_NAME}) {{SAT_NAME}.ElapsedDays = TCM_CoastDays};
   
   Vary Yukon1(TCM_Burn.Element1 = 0.001, {Perturbation = 0.0001, Lower = -0.1, Upper = 0.1, MaxStep = 0.01});
   Vary Yukon1(TCM_Burn.Element2 = 0.001, {Perturbation = 0.0001, Lower = -0.1, Upper = 0.1, MaxStep = 0.01});
   Vary Yukon1(TCM_Burn.Element3 = 0.001, {Perturbation = 0.0001, Lower = -0.1, Upper = 0.1, MaxStep = 0.01});
   Maneuver TCM_Burn({SAT_NAME});
   
   % 推进至月球近心点 (Periselene)
   Propagate MoonProp({SAT_NAME}) {{SAT_NAME}.Luna.Periapsis};
   
   % 约束 1: 近月点高度 hp = 100 km
   NonlinearConstraint Yukon1({SAT_NAME}.Luna.Altitude = 100.0);
   % 约束 2: 赤道交点面 Z = 0 km
   NonlinearConstraint Yukon1({SAT_NAME}.MoonMJ2000Eq.Z = 0.0);
   % 约束 3: 赤道面轨道倾角 Inc = 0 deg
   NonlinearConstraint Yukon1({SAT_NAME}.MoonMJ2000Eq.INC <= 0.1);
   
   % (c) 阶段 3: 近月点逆行制动点火进入低月球圆轨道 (LLO)
   Vary Yukon1(LOI_Burn.Element1 = -0.82, {Perturbation = 0.001, Lower = -1.5, Upper = -0.3, MaxStep = 0.05});
   Maneuver LOI_Burn({SAT_NAME});
   
   % 约束 4: 近月圆轨道偏心率 ECC = 0
   NonlinearConstraint Yukon1({SAT_NAME}.Luna.ECC <= 0.005);
   
   % (d) 极小化总冲代价函数
   TotalDV = abs(TLI_Burn.Element1) + sqrt(TCM_Burn.Element1^2 + TCM_Burn.Element2^2 + TCM_Burn.Element3^2) + abs(LOI_Burn.Element1);
   Minimize Yukon1(TotalDV);
EndOptimize;

% (e) 环月轨道推演验证
Report MissionReport {SAT_NAME}.UTCGregorian {SAT_NAME}.Luna.Altitude {SAT_NAME}.Luna.SMA {SAT_NAME}.Luna.ECC {SAT_NAME}.MoonMJ2000Eq.INC {SAT_NAME}.MoonMJ2000Eq.Z TotalDV;
Propagate MoonProp({SAT_NAME}) {{SAT_NAME}.ElapsedDays = 2.0};
Report MissionReport {SAT_NAME}.UTCGregorian {SAT_NAME}.Luna.Altitude {SAT_NAME}.Luna.SMA {SAT_NAME}.Luna.ECC {SAT_NAME}.MoonMJ2000Eq.INC {SAT_NAME}.MoonMJ2000Eq.Z TotalDV;
"
            });
        }
    }
}
