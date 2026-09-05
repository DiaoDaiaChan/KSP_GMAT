# NASA GMAT 官方文档核心技术提炼与工程脚本蓝本 (GMAT Master Distillation for KSP_GMAT)

> **关联基线**：[`c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\baseline.md`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/baseline.md)  
> **文档数据源**：`GMAT_Docs_Markdown`（01_Preface ~ 07_System，原 1894 页官方用户指南）  
> **面向目标**：为 KSP_GMAT 模组的 `GMATStateManager`（状态机与脚本净化）、`GMATParamSchema`（参数字典与约束）、`GMATMGAScriptGenerator`（深空借力/B-Plane 编译器）、`DifferentialCorrector`（求解器管线）、`GMATTimelineExtractor`（时钟与事件提取）提供权威规范与真实脚本蓝本。

---

# 目录 (Table of Contents)
- [1. 官方文档全局知识体系与提纲拓扑](#1-官方文档全局知识体系与提纲拓扑)
- [2. 辅助本项目开发的 7 大官方标准脚本范式库 (Canonical Script Blueprints)](#2-辅助本项目开发的-7-大官方标准脚本范式库-canonical-script-blueprints)
  - [范式 1：基础轨道确定性递推与停机事件 (Simulating an Orbit)](#范式-1基础轨道确定性递推与停机事件-simulating-an-orbit)
  - [范式 2：双脉冲霍曼转移与微分修正打靶 (Hohmann Transfer & Targeter)](#范式-2双脉冲霍曼转移与微分修正打靶-hohmann-transfer--targeter)
  - [范式 3：有限推力点火与变质量打靶 (Target Finite Burn with Mass Decrement)](#范式-3有限推力点火与变质量打靶-target-finite-burn-with-mass-decrement)
  - [范式 4：行星际转移、分段力学环境与 B-Plane 借力打靶 (Mars B-Plane Targeting)](#范式-4行星际转移分段力学环境与-b-plane-借力打靶-mars-b-plane-targeting)
  - [范式 5：多段打靶与非线性约束优化 (Multiple Shooting Lunar Flyby)](#范式-5多段打靶与非线性约束优化-multiple-shooting-lunar-flyby)
  - [范式 6：低推力电推进连续螺旋外推 (Solar Electric Propulsion)](#范式-6低推力电推进连续螺旋外推-solar-electric-propulsion)
  - [范式 7：高精状态报表输出与数据流 (ReportFile & Pipeline)](#范式-7高精状态报表输出与数据流-reportfile--pipeline)
- [3. 对接 baseline.md 核心系统的工程落地要点](#3-对接-baselinemd-核心系统的工程落地要点)
  - [3.1 GMATStateManager 脚本生成与 Sanitize 铁律对照](#31-gmatstatemanager-脚本生成与-sanitize-铁律对照)
  - [3.2 GMATParamSchema 依赖体系与能力位掩码映射](#32-gmatparamschema-依赖体系与能力位掩码映射)
  - [3.3 积分器选型与 ForceModel 工业级配置准则](#33-积分器选型与-forcemodel-工业级配置准则)
  - [3.4 DifferentialCorrector 算法选型与打靶容错准则](#34-differentialcorrector-算法选型与打靶容错准则)
  - [3.5 CLI 静默执行与后台任务解耦](#35-cli-静默执行与后台任务解耦)
- [4. 官方底层陷阱与故障自愈速查 (Pitfalls & Troubleshooting)](#4-官方底层陷阱与故障自愈速查-pitfalls--troubleshooting)

---

# 1. 官方文档全局知识体系与提纲拓扑

官方文档整体由 7 大部分构成，涵盖了从航天器物理建模、力模型设定、数值积分、微分修正打靶、非线性约束优化，到测距定轨（OD）的全流程：

```
NASA GMAT User Guide (1894 Pages)
├── 01_Preface_概览           --> 架构概览、航天飞行动力学背景
├── 02_Using_GMAT_使用GMAT    --> GUI 导航、资源树 (Resource Tree)、任务树 (Mission Tree)
├── 03_Tutorials_教程         --> 10 大核心任务设计工程实践（初级到高阶）
├── 04_Reference_参考         --> 快速功能交叉索引
├── 05_Resources_资源 (44类)  --> 核心对象定义（Spacecraft, ChemicalThruster, DifferentialCorrector...）
├── 06_Commands_命令 (25类)   --> 任务控制指令（Propagate, Target, Vary, Achieve, Optimize...）
└── 07_System_系统 (11篇)     --> 脚本语法规范、参数元数据、CLI 命令行参数、外部接口
```

### 核心分工与本项目开发关联：
1. **脚本语言规范（07_System/ScriptLanguage）**：确立了 GMAT 脚本必须分为 **资源初始化区（Initialization）** 与 **任务序列区（Mission Sequence，以 `BeginMissionSequence` 起算）**，严格基于 7-bit US-ASCII。
2. **计算参数元数据（07_System/CalculationParameters）**：明确了 310+ 参数的依赖关系（CentralBody、CoordinateSystem、AttachedHardware、ForceModel）与能力属性（Settable 可写、Plottable 可读）。
3. **命令控制引擎（06_Commands）**：`Target/Vary/Achieve` 与 `Optimize/Vary/NonlinearConstraint/Minimize` 构成了求解闭环；`Propagate` 支持多航天器、正向/反向（BackProp）与多样化停机条件（StopConditions）。
4. **推进与动力系统（05_Resources）**：`ChemicalThruster`（$C_1$ 推力, $K_1$ 比冲, VNB 姿态）、`ChemicalTank`（压力/容积/质量耗减）与 `FiniteBurn` 有限点火绑定。

---

# 2. 辅助本项目开发的 7 大官方标准脚本范式库 (Canonical Script Blueprints)

以下脚本提炼自官方教程及核心参考用例，已做合规化净化（去除 GUI 冗余元数据、统一规范航天器名称为 `Sat`，确保纯 ASCII），可直接作为 KSP_GMAT 代码生成器的标准模版：

---

## 范式 1：基础轨道确定性递推与停机事件 (Simulating an Orbit)
> 对应教程：`03_Tutorials/SimulatinganOrbit.md`、`06_Commands/Propagate.md`  
> 适用场景：KSP 飞船轨道滑行、周期递推、过近地点/远地点/赤道面停机检测。

```gmat
%----------------------------------------------------------------------
% 1. 资源初始化区 (Initialization)
%----------------------------------------------------------------------
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2026 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Keplerian;
Sat.SMA = 6928.14;
Sat.ECC = 0.001;
Sat.INC = 28.5;
Sat.RAAN = 100.0;
Sat.AOP = 45.0;
Sat.TA = 0.0;

% 定义近地力模型与 PrinceDormand78 高精自适应积分器
Create ForceModel NearEarth_ForceModel;
NearEarth_ForceModel.CentralBody = Earth;
NearEarth_ForceModel.PrimaryBodies = {Earth};
NearEarth_ForceModel.PointMasses = {Luna, Sun};
NearEarth_ForceModel.GravityField.Earth.Degree = 8;
NearEarth_ForceModel.GravityField.Earth.Order = 8;
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

%----------------------------------------------------------------------
% 2. 任务执行序列 (Mission Sequence)
%----------------------------------------------------------------------
BeginMissionSequence;

% (a) 按时间滑行 12000 秒
Propagate NearEarthProp(Sat) {Sat.ElapsedSecs = 12000.0};

% (b) 积分推进至近地点 (Periapsis)
Propagate NearEarthProp(Sat) {Sat.Earth.Periapsis};

% (c) 积分推进至赤道升交点 (纬度穿越 = 0)
Propagate NearEarthProp(Sat) {Sat.Earth.Latitude = 0.0};
```

---

## 范式 2：双脉冲霍曼转移与微分修正打靶 (Hohmann Transfer & Targeter)
> 对应教程：`03_Tutorials/SimpleOrbitTransfer.md`、`06_Commands/Target.md`  
> 适用场景：KSP_GMAT 经典升轨、降轨、共面圆化霍曼转移，打靶求解脉冲机动 $\Delta V$。

```gmat
%----------------------------------------------------------------------
% 资源定义
%----------------------------------------------------------------------
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2026 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Keplerian;
Sat.SMA = 6778.0;      % 400km 初始圆轨道
Sat.ECC = 0.001;
Sat.INC = 28.5;
Sat.RAAN = 0.0;
Sat.AOP = 0.0;
Sat.TA = 0.0;

% 定义两个脉冲机动 (TOI: 转移轨道注入; GOI: 地球同步轨道圆化)
Create ImpulsiveBurn TOI;
TOI.CoordinateSystem = Local;
TOI.Origin = Earth;
TOI.Axes = VNB;
TOI.Element1 = 0;      % 切向速度分量 V (km/s)
TOI.Element2 = 0;      % 法向速度分量 N (km/s)
TOI.Element3 = 0;      % 径向速度分量 B (km/s)

Create ImpulsiveBurn GOI;
GOI.CoordinateSystem = Local;
GOI.Origin = Earth;
GOI.Axes = VNB;

% 定义牛顿-拉夫逊微分校正器
Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.DerivativeMethod = ForwardDifference;
DC1.MaximumIterations = 25;
DC1.ShowProgress = true;

Create ForceModel DefaultFM;
DefaultFM.CentralBody = Earth;
DefaultFM.PointMasses = {Earth};

Create Propagator DefaultProp;
DefaultProp.FM = DefaultFM;
DefaultProp.Type = PrinceDormand78;

%----------------------------------------------------------------------
% 任务打靶序列
%----------------------------------------------------------------------
BeginMissionSequence;

% 1. 滑行至近地点
Propagate DefaultProp(Sat) {Sat.Earth.Periapsis};

% 2. 霍曼双脉冲打靶闭环
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    % 调整 TOI 切向分量，初始猜想 1.0 km/s，最大单步 0.5 km/s
    Vary DC1(TOI.Element1 = 1.0, {Perturbation = 0.0001, MaxStep = 0.5, Lower = 0.0, Upper = 3.5});
    Maneuver TOI(Sat);
    
    % 积分至转移轨道远地点 (Apoapsis 停机检测)
    Propagate DefaultProp(Sat) {Sat.Earth.Apoapsis};
    % 约束远地点半径达成 42164.169 km (GEO 高度)，容差 0.5 km
    Achieve DC1(Sat.Earth.RMAG = 42164.169, {Tolerance = 0.5});
    
    % 调整 GOI 切向分量，圆化轨道
    Vary DC1(GOI.Element1 = 1.0, {Perturbation = 0.0001, MaxStep = 0.2, Lower = 0.0, Upper = 2.5});
    Maneuver GOI(Sat);
    
    % 约束最终偏心率 ECC = 0.005，容差 0.0001
    Achieve DC1(Sat.Earth.ECC = 0.005, {Tolerance = 0.0001});
EndTarget;

% 3. 打靶完成后验证滑行 1 整天
Propagate DefaultProp(Sat) {Sat.ElapsedSecs = 86400};
```

---

## 范式 3：有限推力点火与变质量打靶 (Target Finite Burn with Mass Decrement)
> 对应教程：`03_Tutorials/TargetFiniteBurntoRaiseApogee.md`、`05_Resources/ChemicalThruster.md`  
> 适用场景：真实发动机有限点火、消耗推进剂变质量、打靶求解点火时长 `BurnDuration`。

```gmat
%----------------------------------------------------------------------
% 硬件与推进系统定义
%----------------------------------------------------------------------
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2026 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Keplerian;
Sat.SMA = 6778.0;
Sat.ECC = 0.001;
Sat.DryMass = 1000;

% 推进储罐
Create ChemicalTank Tank0;
Tank0.FuelMass = 1500;
Tank0.FuelDensity = 1000;
Tank0.Pressure = 2000;
Tank0.Volume = 2.0;
Tank0.PressureModel = 'PressureRegulated';
Tank0.AllowNegativeFuelMass = true;

% 推力器：额定真空推力 1000 N (C1=1000)，比冲 300 s (K1=300)
Create ChemicalThruster RL10;
RL10.CoordinateSystem = Local;
RL10.Origin = Earth;
RL10.Axes = VNB;
RL10.ThrustDirection1 = 1;     % 顺行加速
RL10.ThrustDirection2 = 0;
RL10.ThrustDirection3 = 0;
RL10.DutyCycle = 1.0;
RL10.ThrustScaleFactor = 1.0;
RL10.DecrementMass = true;     % 启用质量消耗
RL10.Tank = {Tank0};
RL10.C1 = 1000;                % 推力 1000 N
RL10.K1 = 300;                 % 比冲 300 s

% 将储罐与推力器挂载至航天器
Sat.Tanks = {Tank0};
Sat.Thrusters = {RL10};

% 定义有限点火对象
Create FiniteBurn FiniteBurn1;
FiniteBurn1.Thrusters = {RL10};

% 目标决策变量：点火时长
Create Variable BurnDuration;
BurnDuration = 0;

Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.MaximumIterations = 30;

Create ForceModel PropFM;
PropFM.CentralBody = Earth;
PropFM.PointMasses = {Earth};

Create Propagator Prop;
Prop.FM = PropFM;
Prop.Type = PrinceDormand78;

%----------------------------------------------------------------------
% 任务打靶序列
%----------------------------------------------------------------------
BeginMissionSequence;

% 1. 先滑行至近地点起燃
Propagate Prop(Sat) {Sat.Earth.Periapsis};

% 2. 打靶点火时长
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    % 调整点火时长 BurnDuration，初始猜想 200s，上限 1000s
    Vary DC1(BurnDuration = 200, {Perturbation = 1.0, MaxStep = 100, Lower = 1.0, Upper = 1500});
    
    % 起燃
    BeginFiniteBurn FiniteBurn1(Sat);
    % 在点火推力下积分持续 BurnDuration 秒
    Propagate Prop(Sat) {Sat.ElapsedSecs = BurnDuration};
    % 关机
    EndFiniteBurn FiniteBurn1(Sat);
    
    % 滑行至远地点检测目标
    Propagate Prop(Sat) {Sat.Earth.Apoapsis};
    % 约束目标远地点天心距 RMAG = 12000 km，容差 1.0 km
    Achieve DC1(Sat.Earth.RMAG = 12000, {Tolerance = 1.0});
EndTarget;
```

---

## 范式 4：行星际转移、分段力学环境与 B-Plane 借力打靶 (Mars B-Plane Targeting)
> 对应教程：`03_Tutorials/MarsB-PlaneTargeting.md`  
> 适用场景：**深空多重借力助推 (MGA) 与行星际入轨**。针对地球逃逸段、日心深空巡航段、目标行星入轨段采用 3 套物理摄动力模型，打靶 B-Plane 参数（$B\cdot T, B\cdot R$）。

```gmat
%----------------------------------------------------------------------
% 1. 航天器与坐标系定义
%----------------------------------------------------------------------
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '18 Nov 2026 20:26:24.315';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Keplerian;
% 地球双曲脱离轨道参数
Sat.SMA = -32593.2;
Sat.ECC = 1.2028;
Sat.INC = 28.80;
Sat.RAAN = 173.96;
Sat.AOP = 240.96;
Sat.TA = 359.94;

% 中途修正脉冲机动 (TCM)
Create ImpulsiveBurn TCM;
TCM.CoordinateSystem = Local;
TCM.Origin = Earth;
TCM.Axes = VNB;

% 目标星入轨制动脉冲机动 (MOI)
Create ImpulsiveBurn MOI;
MOI.CoordinateSystem = Local;
MOI.Origin = Mars;
MOI.Axes = VNB;

% 日心与火星中心坐标系
Create CoordinateSystem SunMJ2000Eq;
SunMJ2000Eq.Origin = Sun;
SunMJ2000Eq.Axes = MJ2000Eq;

Create CoordinateSystem MarsMJ2000Eq;
MarsMJ2000Eq.Origin = Mars;
MarsMJ2000Eq.Axes = MJ2000Eq;

%----------------------------------------------------------------------
% 2. 三级分段动力学力模型 (NearEarth -> DeepSpace -> NearMars)
%----------------------------------------------------------------------
% 近地段：地球高阶重力场 + 日月摄动 + SRP
Create ForceModel NearEarth_FM;
NearEarth_FM.CentralBody = Earth;
NearEarth_FM.PrimaryBodies = {Earth};
NearEarth_FM.GravityField.Earth.Degree = 8;
NearEarth_FM.GravityField.Earth.Order = 8;
NearEarth_FM.PointMasses = {Luna, Sun};
NearEarth_FM.SRP = On;

Create Propagator NearEarthProp;
NearEarthProp.FM = NearEarth_FM;
NearEarthProp.Type = PrinceDormand78;
NearEarthProp.Accuracy = 1e-11;

% 深空巡航段：太阳为中心引力，太阳系所有主要行星作为点质点摄动
Create ForceModel DeepSpace_FM;
DeepSpace_FM.CentralBody = Sun;
DeepSpace_FM.PointMasses = {Sun, Mercury, Venus, Earth, Luna, Mars, Jupiter, Saturn, Uranus, Neptune};
DeepSpace_FM.SRP = On;

Create Propagator DeepSpaceProp;
DeepSpaceProp.FM = DeepSpace_FM;
DeepSpaceProp.Type = PrinceDormand78;
DeepSpaceProp.Accuracy = 1e-11;

% 近火星段：火星高阶引力场 + 太阳点质点 + SRP
Create ForceModel NearMars_FM;
NearMars_FM.CentralBody = Mars;
NearMars_FM.PrimaryBodies = {Mars};
NearMars_FM.GravityField.Mars.Degree = 8;
NearMars_FM.GravityField.Mars.Order = 8;
NearMars_FM.PointMasses = {Sun};
NearMars_FM.SRP = On;

Create Propagator NearMarsProp;
NearMarsProp.FM = NearMars_FM;
NearMarsProp.Type = PrinceDormand78;
NearMarsProp.Accuracy = 1e-11;

Create DifferentialCorrector DC1;
DC1.Algorithm = NewtonRaphson;
DC1.MaximumIterations = 50;

%----------------------------------------------------------------------
% 3. 任务打靶序列 (B-Plane 打靶核心)
%----------------------------------------------------------------------
BeginMissionSequence;

% (a) 离开地球引力球 (3天)
Propagate NearEarthProp(Sat) {Sat.ElapsedDays = 3.0};

% (b) 第一阶段打靶：在深空执行 TCM 修正，瞄准火星 B 平面 (BdotT, BdotR)
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    % 微调 TCM 的 3 轴分量 (V, N, B)
    Vary DC1(TCM.Element1 = 0.0, {Perturbation = 1e-5, MaxStep = 0.1, Lower = -1.0, Upper = 1.0});
    Vary DC1(TCM.Element2 = 0.0, {Perturbation = 1e-5, MaxStep = 0.1, Lower = -1.0, Upper = 1.0});
    Vary DC1(TCM.Element3 = 0.0, {Perturbation = 1e-5, MaxStep = 0.1, Lower = -1.0, Upper = 1.0});
    
    % 执行中途修正
    Maneuver TCM(Sat);
    
    % 深空巡航推进至火星附近 (天心距 RMAG <= 1,000,000 km)
    Propagate DeepSpaceProp(Sat) {Sat.Mars.RMAG = 1000000.0};
    % 进入火星引力球，高精积分至火星近拱点 (Periapsis)
    Propagate NearMarsProp(Sat) {Sat.Mars.Periapsis};
    
    % 达成 B 平面目标约束：BdotT = 0 km (面内)，BdotR = -7000 km (面外倾角控制)
    Achieve DC1(Sat.Mars.BdotT = 0.0, {Tolerance = 1.0});
    Achieve DC1(Sat.Mars.BdotR = -7000.0, {Tolerance = 1.0});
EndTarget;

% (c) 第二阶段打靶：在火星近拱点制动捕获入轨 (MOI)
Target DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
    % 调整逆行制动分量
    Vary DC1(MOI.Element1 = -1.5, {Perturbation = 0.0001, MaxStep = 0.2, Lower = -3.5, Upper = -0.1});
    Maneuver MOI(Sat);
    
    % 推进至火星捕获椭圆轨道的远地点
    Propagate NearMarsProp(Sat) {Sat.Mars.Apoapsis};
    % 约束捕获轨道远地点天心距 RMAG = 12000 km
    Achieve DC1(Sat.Mars.RMAG = 12000.0, {Tolerance = 5.0});
EndTarget;
```

---

## 范式 5：多段打靶与非线性约束优化 (Multiple Shooting Lunar Flyby)
> 对应教程：`03_Tutorials/OptimalLunarFlybyusingMultipleShooting.md`  
> 适用场景：深空复杂轨道、借力飞掠（Lunar / Planetary Flyby）敏感两边界问题，切分为多段（Segments）并建立 Control Points 与 Patch Points，使用 `Optimize` 与 `VF13ad` 消除位置/速度断点并最小化燃料。

```gmat
%----------------------------------------------------------------------
% 多段航天器对象定义 (Control Points)
%----------------------------------------------------------------------
% Segment 1: 地球出发段
Create Spacecraft satTOI;
satTOI.DateFormat = TAIModJulian;
satTOI.CoordinateSystem = EarthMJ2000Eq;

% Segment 2 & 3: 月球飞掠点 (正向与反向积分)
Create Spacecraft satFlyBy_Forward;
Create Spacecraft satFlyBy_Backward;
satFlyBy_Forward.CoordinateSystem = MoonMJ2000Eq;
satFlyBy_Backward.CoordinateSystem = MoonMJ2000Eq;

% 定义 VF13ad 序列二次规划 (SQP) 优化器
Create VF13ad SQP1;
SQP1.MaximumIterations = 50;
SQP1.Tolerance = 1e-4;

% 成本函数：总燃料消耗 / 速度增量平方和
Create Variable Cost;

%----------------------------------------------------------------------
% 优化序列
%----------------------------------------------------------------------
BeginMissionSequence;

Optimize SQP1;
    % 1. Vary 控制点的历元与三维笛卡尔状态 (X, Y, Z, VX, VY, VZ)
    Vary SQP1(satTOI.X = ...);
    Vary SQP1(satFlyBy_Forward.X = ...);
    
    % 2. 将正向和反向飞船对齐到同一飞掠初始状态
    satFlyBy_Backward = satFlyBy_Forward;
    
    % 3. 正向传播出发段，反向传播飞掠段
    Propagate EarthProp(satTOI) {satTOI.ElapsedDays = 2.0};
    Propagate BackProp MoonProp(satFlyBy_Backward) {satFlyBy_Backward.ElapsedDays = -1.5};
    
    % 4. 在 Patch Point 施加非线性位置与速度连续性约束 (C0 / C1 连续)
    NonlinearConstraint SQP1(satTOI.EarthMJ2000Eq.X = satFlyBy_Backward.EarthMJ2000Eq.X);
    NonlinearConstraint SQP1(satTOI.EarthMJ2000Eq.Y = satFlyBy_Backward.EarthMJ2000Eq.Y);
    NonlinearConstraint SQP1(satTOI.EarthMJ2000Eq.Z = satFlyBy_Backward.EarthMJ2000Eq.Z);
    NonlinearConstraint SQP1(satTOI.EarthMJ2000Eq.VX = satFlyBy_Backward.EarthMJ2000Eq.VX);
    NonlinearConstraint SQP1(satTOI.EarthMJ2000Eq.VY = satFlyBy_Backward.EarthMJ2000Eq.VY);
    NonlinearConstraint SQP1(satTOI.EarthMJ2000Eq.VZ = satFlyBy_Backward.EarthMJ2000Eq.VZ);
    
    % 5. 最小化推进剂消耗成本函数
    Minimize SQP1(Cost);
EndOptimize;
```

---

## 范式 6：低推力电推进连续螺旋外推 (Solar Electric Propulsion)
> 对应教程：`03_Tutorials/ElectricPropulsion.md`  
> 适用场景：离子/霍尔推进器长期点火升轨（低推力外螺旋线）。

```gmat
% 太阳能供电系统
Create SolarPowerSystem SolarPanel;
SolarPanel.EpochFormat = 'UTCGregorian';
SolarPanel.InitialEpoch = '01 Jan 2026 12:00:00.000';
SolarPanel.ShadowBodies = {'Earth'};
SolarPanel.SolarCoefficients = [1.32077 -0.10848 -0.11665 0.10843 -0.01279];

% 电子储罐与电推力器 (常推力 5.0 N，比冲 3000 s)
Create ElectricTank XenonTank;
XenonTank.FuelMass = 500;

Create ElectricThruster NEXT;
NEXT.CoordinateSystem = Local;
NEXT.Axes = VNB;
NEXT.ThrustDirection1 = 1;
NEXT.DecrementMass = true;
NEXT.Tank = {XenonTank};
NEXT.ThrustModel = 'ConstantThrustAndIsp';
NEXT.ConstantThrust = 5.0;     % 5 N 连续推力
NEXT.ConstantIsp = 3000;       % 3000 s 高比冲

Create Spacecraft Sat;
Sat.Tanks = {XenonTank};
Sat.Thrusters = {NEXT};
Sat.PowerSystem = SolarPanel;

Create FiniteBurn LowThrustBurn;
LowThrustBurn.Thrusters = {NEXT};

BeginMissionSequence;
BeginFiniteBurn LowThrustBurn(Sat);
% 连续低推力推进 30 天
Propagate NearEarthProp(Sat) {Sat.ElapsedDays = 30.0};
EndFiniteBurn LowThrustBurn(Sat);
```

---

## 范式 7：高精状态报表输出与数据流 (ReportFile & Pipeline)
> 对应系统：`05_Resources/ReportFile.md`、`06_Commands/Report.md`  
> 适用场景：供 KSP_GMAT 后台驱动解析物理轨迹点、提取里程碑时刻与写入 Principia 飞行计划。

```gmat
Create ReportFile TrajectoryReport;
TrajectoryReport.Filename = 'TrajectoryData.txt';
TrajectoryReport.Precision = 16;           % 双精度 16 位保证毫米级精度
TrajectoryReport.WriteHeaders = true;
TrajectoryReport.LeftJustify = On;
TrajectoryReport.ZeroFill = Off;
TrajectoryReport.ColumnWidth = 24;
TrajectoryReport.WriteReport = true;

BeginMissionSequence;

% 循环积分并每步输出标准 ICRF 状态矢量与质量
While Sat.ElapsedDays <= 5.0
    Propagate NearEarthProp(Sat);
    Report TrajectoryReport Sat.UTCGregorian Sat.A1ModJulian ...
           Sat.EarthMJ2000Eq.X Sat.EarthMJ2000Eq.Y Sat.EarthMJ2000Eq.Z ...
           Sat.EarthMJ2000Eq.VX Sat.EarthMJ2000Eq.VY Sat.EarthMJ2000Eq.VZ ...
           Sat.TotalMass Sat.Tank0.FuelMass;
EndWhile;
```

---

# 3. 对接 baseline.md 核心系统的工程落地要点

针对 `baseline.md` 规定的架构规范，官方文档明确了以下核心工程边界：

## 3.1 GMATStateManager 脚本生成与 Sanitize 铁律对照
1. **7-bit US-ASCII 规范与编码防御**：
   - 官方语法明确规定：解析器仅接受 7-bit ASCII。中文字符、制表符混杂或非打印字符（NUL/SUB）会导致词法解析器在 `LoadScript()` 时发生不可逆内存偏移。
   - `SanitizeGmatScript` 必须将所有描述性字段、注释强制清除或转换为纯英文字符串。
2. **航天器标识符与前缀对齐**：
   - 官方多处指出：Spacecraft 对象名称全局唯一且必须与属性路径前缀完全一致。若命名为 `Sat`，所有属性引用必须严格为 `Sat.X`, `Sat.Earth.SMA`。
   - 若脚本中出现 `Sat1.` 或异构名称（如 `Tianwen`、`MAVEN`），GMAT 求解器会直接抛出 `Unknown object` 异常中断。
3. **Vary 与 Achieve 边界防御**：
   - 官方规范明确规定：`Vary` 中的 `Lower` 必须严格小于 `Upper`（`Lower < Upper`）。若 `Upper == 0` 或 `Upper <= Lower`，求解器将产生除以零或矩阵奇异；
   - `Tolerance` 必须严格 $> 0$。

## 3.2 GMATParamSchema 依赖体系与能力位掩码映射
官方 `07_System/CalculationParameters.md` 将参数严格划分为 4 类依赖体系，这与本项目的 `ParamCapability` 位掩码完美呼应：

| 官方依赖分类 (Dependency) | 官方语法范例 | baseline.md 映射规范 | 关键能力约束 |
| :--- | :--- | :--- | :--- |
| **CentralBody 依赖** | `Sat.Mars.Altitude`<br>`Sat.Earth.SMA`<br>`Sat.Luna.RadPer` | `GetQualifiedSpacecraftProperty("{Body}.SMA")` | 必须动态指定当前引力中心；可用于 Achieve / Report |
| **CoordinateSystem 依赖** | `Sat.EarthMJ2000Eq.X`<br>`Sat.SunMJ2000Eq.VX` | 依赖 `CoordinateSystem` 资源定义 | 笛卡尔/开普勒状态换算；可用于 Vary / Achieve |
| **AttachedHardware 依赖** | `Sat.Tank0.FuelMass`<br>`Sat.RL10.Thrust` | 必须在航天器上 `Sat.Tanks={...}` 绑定后才可访问 | 仅硬件绑定后有效；可用于 Vary（有限推力方向） |
| **无依赖 (Standalone)** | `Sat.ElapsedSecs`<br>`Sat.DryMass`<br>`Sat.TotalMass` | 直接 `Sat.Property` 访问 | 全局时钟与质量守恒基准 |
| **离散极值事件 (Extremum)** | `Sat.Earth.Periapsis`<br>`Sat.Earth.Apoapsis` | 仅在 `Propagate` StopConditions 中使用 | **严禁用于 Achieve 连续目标求解！** 否则目标雅可比矩阵全零退化。 |

## 3.3 积分器选型与 ForceModel 工业级配置准则
1. **PrinceDormand78 为最高优先级**：
   - 官方经 LEO、Molniya、Mars Transfer、Lunar Flyby、Finite Burn 等 6 大真实力学算例横向对比证实：**PrinceDormand78 在精度与耗时平衡上表现最佳**（多段深空比 RK89 快且误差低 2 个数量级）；
   - **自适应步长铁律**：务必将 `MinStep` 设为 `0.0`，让嵌入式自适应步长算法自动控制最小步长；若设置非零 `MinStep`，在遭遇近拱点大摄动时极易触发 `Accuracy Violated` 致命异常；
   - 默认容差设置：`Accuracy = 1e-11`，`ErrorControl = RSSStep`。
2. **分级力模型切换（NearEarth / DeepSpace / NearTarget）**：
   - 地球/行星近地段：开启 8x8 或更高阶谐波引力（`JGM-2` / `EGM-96`）、日月点质点摄动与太阳光压（`SRP = On`）；
   - 行星际深空段：太阳为中心体，水星至海王星全行星作为 `PointMasses`，开启 `SRP`；严禁在深空开启复杂非球形引力场（大幅增加无谓计算耗时）。

## 3.4 DifferentialCorrector 算法选型与打靶容错准则
1. **NewtonRaphson（牛顿-拉夫逊法）**：
   - 最稳定、收敛半径大，利用有限微商（`ForwardDifference` / `CentralDifference`）构建完整雅可比矩阵；
   - 适用于单脉冲/双脉冲打靶、中途修正（TCM）与 B-Plane 瞄准。
2. **Broyden 与 ModifiedBroyden**：
   - 利用割线法更新雅可比逆矩阵，函数评估次数显著少于 NewtonRaphson；
   - 适用于多变量高维打靶（变量数 $> 4$），可大幅缩短整体耗时。
3. **奇异矩阵自愈（Rmatrix is singular）**：
   - 官方指出：当 `Vary` 变量与 `Achieve` 目标之间不存在动力学因果影响时（例如 Vary 了法向分量却试图 Achieve 轨道半长轴 SMA，或忘了在中间执行 `Maneuver` / `BeginFiniteBurn`），雅可比矩阵行列式为 0；
   - 防御机制：在 C# 脚本生成器中强制配对校验控制变量与物理目标。

## 3.5 CLI 静默执行与后台任务解耦
对接 `GMATClient.cs` 的后台求解，官方命令行支持极佳的静默无干扰调用：
- **执行命令**：`GMAT.exe -m -ns -r "<path_to_script>" -x`
  - `-m` (`--minimize`)：最小化启动，不掠夺 Unity/KSP 主窗口焦点；
  - `-ns` (`--no_splash`)：禁用 NASA 启动闪屏，提速 1~2 秒；
  - `-r` (`--run`)：载入后立即触发物理递推与打靶求解；
  - `-x` (`--exit`)：运行完毕后自动杀掉自身进程并退出释放句柄；
- **控制台模式**：`GMATConsole.exe -r "<path_to_script>"`（更纯粹的无界面控制台调用）。

---

# 4. 官方底层陷阱与故障自愈速查 (Pitfalls & Troubleshooting)

| 官方典型错误信息 | 根因剖析 (Root Cause) | baseline.md 预设自愈解决方案 |
| :--- | :--- | :--- |
| `Rmatrix error: matrix is singular` | `Vary` 的控制变量未能物理影响 `Achieve` 目标，或缺少 Maneuver 动作。 | 自动检查 Target 块内是否存在对应的机动指令，并校验物理通道相关性。 |
| `Cannot bracket the root of the stopping condition` | `Propagate` 停机条件（如 `Periapsis` / `Apoapsis`）在积分步长跨度内无法定位根。 | 减小积分器 `MaxStep`，或改用 `ElapsedSecs` 时间推进至特征点附近再找根。 |
| `Spacecraft epoch is earlier than SPICE/DE405 minimum limit` | GMAT 官方 DE405 行星历表硬性要求历元 $\ge \text{1957-10-04}$（Sputnik 时刻）。 | 启用 `FormatGmatSafeEpoch` 双通道历元架构，对早期存档（$< 1957$）自动映射至安全基准。 |
| `Upper bound must be greater than Lower bound` | Vary 语句中设置了不合规的打靶区间（如 `Lower=0, Upper=0`）。 | `SanitizeGmatScript` 强制过滤并自愈赋初值：距离量 $\pm 1000\text{ km}$，速度量 $\pm 0.5\text{ km/s}$。 |
| `Atmosphere model not allowed below 90 km` | MSISE 系列大气模型对 $< 90\text{ km}$ 高度抛出数值不连续异常。 | 在近地再入或大气刹车段自动关闭高阶大气模型，或使用固定步长切换。 |

---
*本文档由 Antigravity 自动提炼自 NASA GMAT 官方 Markdown 技术库，为 KSP_GMAT 项目 R2026a 基线提供权威航天动力学与脚本规范支撑。*
