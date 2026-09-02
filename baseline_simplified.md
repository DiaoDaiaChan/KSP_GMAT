# KSP_GMAT 基线架构与开发规范 (简化版)

> **版本**：R2026a Standard Baseline（简化版）
> **工程目录**：`c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT`
> **适用范围**：KSP 模组核心、UI/UX 视图系统、数据模型、GMAT 脚本编译器、求解器通信管线与 Principia 动力学互操作系统。
> **说明**：本文为 `baseline.md` 的篇幅压缩版，需求语义与原文一致；各条款标注实施状态（依据《baseline_audit_report.md》）：
> - ✅ 已实现（审计确认达标）
> - 🟡 部分实现（已实现主体，但存在审计指出的缺口）
> - ❌ 未实现（审计确认缺失）

---

# 目录
- [0. 核心架构设计原则 (SSOT)](#0-核心架构设计原则)
- [1. GMATStateManager 状态统一管理](#1-gmatstatemanager)
- [2. UI 国际化 (i18n)](#2-ui-国际化-i18n)
- [3. GMATParamSchema 参数元数据字典](#3-gmatparamschema)
- [4. UI 设计系统](#4-ui-设计系统)
- [5. 物理时序、质心提前与 Principia 互操作](#5-物理时序质心提前与-principia-互操作)
- [6. 基线维护与提交守则](#6-基线维护与提交守则)
- [7. ESA PyKEP 原生动力学引擎与 MGA Planner](#7-esa-pykep)
- [8. 三核闭环钳制校准系统计划](#8-三核闭环钳制校准)

---

# 0. 核心架构设计原则 (Single Source of Truth)

航天器状态（位置/速度/历元/质量/气动/光压）、推进硬件（多级储罐、推力器、供油绑定）与任务打靶序列（Target/Vary/Achieve）具有强耦合性。为杜绝数据分裂、局部私有字段不同步、对象前缀污染与数值退化，确立核心铁律：

> ### 🛑 核心铁律：单一数据真值源 (SSOT)
> 所有 GMAT 相关的飞船变量、动力学状态、硬件配置、用户自定义变量与脚本合规过滤逻辑，**必须且只能由 `GMATStateManager` 统一维护、更新与导出**。严禁在 UI 窗口、IDE 缓冲区或子面板中开设分散私有变量。

---

# 1. GMATStateManager

核心文件：[`src/KSPGMAT/Model/GMATStateManager.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs)

## 1.1 架构流转全景拓扑 ✅

```
[KSP/Principia/RO] --(1.SyncFromVessel)--> GMATStateManager(SSOT 全局单例)
   ├─ GMATSpacecraftModel(Sat) · List<GMATTankModel> · List<GMATThrusterModel>
   ├─ List<StageData>(MechJeb分级) · GMATStageBudget · SanitizeGmatScript()
   └──(2.双向绑定)→ TabResources · (3.实时提取)→ TabSequence · (4.纯净编译)→ IDE · (5.合规拦截)→ GMATClient
```

## 1.2 状态数据模型规范

### 1.2.1 航天器基础模型 `GMATSpacecraftModel`（映射 `Create Spacecraft Sat;`）✅

| 字段 | 规范要求 | GMAT 脚本属性 |
| :--- | :--- | :--- |
| `Name` | `"Sat"`（强制规范化） | `Create Spacecraft Sat;` |
| `DateFormat` | `"UTCGregorian"` | `Sat.DateFormat = UTCGregorian;` |
| `EpochUtc` | 实时从 UT 计算（1951 起算） | `Sat.Epoch = '...';` |
| `UniversalTime` | `Planetarium.GetUniversalTime()` | 积分起算基准 |
| `CoordinateSystem` | `"{CentralBody}MJ2000Eq"` | `Sat.CoordinateSystem = ...;` |
| `DisplayStateType` | `"Cartesian"` | `Sat.DisplayStateType = Cartesian;` |
| `PosKm` / `VelKms` | `PrincipiaInterop.GetVesselStateICRF` | `Sat.X/Y/Z`、`Sat.VX/VY/VZ` |
| `DryMassKg` | 实测干重或用户自定义 | `Sat.DryMass = ...;` |
| `TotalMassKg` | `vessel.GetTotalMass()*1000` | 质量守恒校验基准 |
| `DragArea` | 默认 `15.0` | `Sat.DragArea = ...;` |
| `Cd` | 默认 `2.2` | `Sat.Cd = ...;` |
| `SRPArea` | 默认 `15.0` 🟡（审计：模型实为 `1.0`，未同步飞船时脚本写入 `Sat.SRPArea=1.0`） | `Sat.SRPArea = ...;` |
| `Cr` | 默认 `1.8` | `Sat.Cr = ...;` |
| `CentralBody` | `"Earth"`/`"Luna"`/`"Mars"` 等 | `Thrust.Origin = ...;` |

### 1.2.2 推进储罐模型 `GMATTankModel`（映射 `Create ChemicalTank TankX;`）✅

| 字段 | 规范要求 | GMAT 映射 |
| :--- | :--- | :--- |
| `Id` | 严格为 `Tank0`, `Tank1`, `Tank2`... | `Create ChemicalTank Tank0;` |
| `StageNumber` | KSP/MechJeb 分级索引 | 供油智能匹配 |
| `FuelMassKg` / `DryMassKg` / `VolumeM3` | 实时扫描 RO 真实质量/干重；容积 ≥1.0 | `Tank0.FuelMass/Volume = ...;` |
| `PressureModel` | `"PressureRegulated"` | `Tank0.PressureModel = '...';` |
| `AllowNegativeFuelMass` | 强制 `true` | `Tank0.AllowNegativeFuelMass = true;` |

### 1.2.3 发动机推力器模型 `GMATThrusterModel`（映射 `Create ChemicalThruster EngX;`）✅

| 字段 | 规范要求 | GMAT 映射 |
| :--- | :--- | :--- |
| `Id` | 合规化 ASCII 英文标识 | `Create ChemicalThruster RL10;` |
| `RawTitle` | KSP 零件原名 | UI 看板展示 |
| `StageNumber` | 用于与对应分级储罐自动匹配 | 自动级间供油解耦 |
| `ThrustKN` / `IspVac` | 实时提取 RO/原版真实推力/比冲 | `C1 = ThrustKN*1000` / `K1 = IspVac` |
| `BoundTankId` | 必须为有效储罐（如 `Tank0`） | `Eng.Tank = {Tank0};` |
| `ThrustDirection1/2/3` | 归一化 V/N/B 轴分量（默认 1/0/0） | `ThrustDirection1/2/3 = ...;` |
| `DecrementMass` | 强制 `true` | `Eng.DecrementMass = true;` |

## 1.3 全限定属性路径与命名规范 🟡（方法本身合规；但生成器/命名解析器存在硬编码 "Sat"，见审计 H2/L1）

所有脚本对象引用必须通过 `GMATStateManager` 标准构造器生成：

```csharp
string sc = GMATStateManager.Current.GetSpacecraftName();                      // 恒定返回 "Sat"
string propV  = GMATStateManager.Current.GetQualifiedThrusterProperty("RL10", "ThrustDirection1"); // "Sat.RL10.ThrustDirection1"
string propSMA= GMATStateManager.Current.GetQualifiedSpacecraftProperty("Earth.SMA");              // "Sat.Earth.SMA"
```

## 1.4 全流程脚本合规净化过滤器 `SanitizeGmatScript` 🟡（审计：仅实现第1条，第2~4条缺失；且后台求解路径绕过，见审计 H1/H3）

`GMATStateManager.SanitizeGmatScript(string rawScript)` 在**脚本导出、剪贴板复制、推送外部桌面端、后台物理求解**前必须 100% 介入执行：
1. **纯 ASCII 字符流强制转换**：彻底过滤中文与 Emoji；
2. **航天器标识符前缀自愈**：正则 `\bSat\d+\.` 自动对齐为 `Sat.`；
3. **打靶区间与容差防退化守护**：`Upper<=Lower` 或 `Upper==0` 自动修正安全边界；`Tolerance<=0` 按距离量 `0.5~1.0 km`、速度量 `0.01 km/s` 自动赋容差；
4. **终止条件合规化**：自动将 `Sat.Earth.Node` 转换为 `Sat.Earth.Latitude = 0.0`。

## 1.5 第三方异构脚本自动别名与容错规范 ❌（审计：全库无实现）

- 主航天器别名 `Tianwen`/`Change5`/`europaclipper`/`Juno` 全量重定向至 `Sat`；
- 外部储罐（`maintank`/`MMH`）对齐至 `Tank0`, `Tank1`...；推力器挂载至 `Sat.Thrusters` 并绑定储罐；
- 外部求解器（`DC1`/`NLP`/`VF13ad1`）自动纳管注册进候选池。

---

# 2. UI 国际化 (i18n)

核心文件：[`src/KSPGMAT/I18n.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/I18n.cs)

## 2.1 核心原则与防混杂铁律 🟡（框架已建，但约 95% UI 仍为硬编码文本，见审计 H8）

1. **禁止在 UI 视图中硬编码任何文本**：必须调用 `I18n.T("key")` 或 `I18n.Format("key", args)`；
2. **杜绝中英双语混杂与开发备注**：严禁括号混搭（如 `标量变量 (Variables)`）；严禁将内部开发备注（如 `(Exact f6273ae logic)`、`(对齐截图)`）暴露给用户；
3. **保持文本单一责任与精炼**：动力学提示仅显示单个最核心物理效应。

## 2.2 词典结构与 Key 命名规范 🟡（13 个 key 前缀不合规，见审计 M4）

Key 统一小写下划线 `snake_case`，按模块分类：`tab_`(主标签页)、`btn_`(按钮)、`add_`(添加步骤)、`type_`(步骤胶囊)、`craft_`(航天器参数)、`seq_`(任务队列)、`burn_`(有限点火)、`vector_`(推力矢量)、`target_`(打靶求解)、`ide_`(脚本审查)、`res_`(结果数据)、`settings_`(设置页)、`tip_`(物理知识提示)。

## 2.3 UI 代码接入标准规范 ✅

```csharp
GUILayout.Label(I18n.T("craft_dry_mass"), labelValueStyle);                    // 静态文本
GUILayout.Label(I18n.Format("burn_cluster_active", count), labelValueStyle);   // 动态格式化
if (GUILayout.Button(I18n.T("btn_solve"), actionButtonStyle, GUILayout.Height(24))) { TriggerPropagationSolve(); }
```

## 2.4 多语言扩展示范 🟡（`Lang` 缺 RU，133 条词条全部仅 ZH/EN，见审计 M3）

```csharp
public enum Lang { ZH, EN, RU }
// 词条定义：{"key", new[]{"中文","English","Русский"}}
{"craft_dry_mass", new[] { "航天器干重", "Dry Mass", "Сухая масса" }}
```

---

# 3. GMATParamSchema

核心文件：[`src/KSPGMAT/Model/GMATParamSchema.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATParamSchema.cs)

## 3.1 参数能力位掩码 `ParamCapability` ✅（静态目录合规；动态注册目录无能力位，见审计 M9）

```csharp
[Flags] public enum ParamCapability {
    None = 0,
    ContinuouslyDifferentiable = 1 << 0, // 连续可导物理量（适用于 Achieve 收敛与牛顿法）
    DiscontinuousExtremumEvent = 1 << 1, // 离散极值事件（仅用于 Propagate 停机，严禁用于 Achieve）
    ControlVariable = 1 << 2,            // 独立控制变量（适用于 Vary）
    HardwareProperty = 1 << 3,           // 硬件物理属性（DryMass/FuelMass/Isp/Cd/Cr...）
    CoordinateCartesian = 1 << 4,        // 笛卡尔状态向量（X/Y/Z/VX/VY/VZ/AX/AY/AZ/HMAG）
    AttitudeOrientation = 1 << 5,        // 姿态（Q1~Q4/Euler/MRP）
    PropagateCondition = ContinuouslyDifferentiable | DiscontinuousExtremumEvent,
    Reportable = ...全部
}
```

## 3.2 参数元数据类 `GMATParamDefinition` ✅

```csharp
public class GMATParamDefinition {
    public string Name; public string Category; public ParamCapability Capability;
    public string Unit; public double DefaultValue; public double TypicalStep;
    public string DocZh; public string DocEn;
}
```

## 3.3 11 大分类标准参数全景字典 🟡（GravitationalAccel 为多余假参数；BetaAngle 能力位缺失；C1 单位元数据 [kN] 与实际 N 矛盾，见审计 L2/L3/M10）

1. **开普勒根数**：`SMA[km]`、`ECC`、`INC[deg]`、`RAAN[deg]`、`RADN[deg]`、`AOP[deg]`、`TA/MA/EA/HA[deg]`、`MM[deg/s]`、`RadPer/RadApo[km]`、`SemilatusRectum[km]`、`OrbitPeriod[s]`、`VelApoapsis/VelPeriapsis[km/s]`、`C3Energy[km²/s²]`、`Energy[km²/s²]`
2. **春分点/德劳内根数**：`ModEquinoctialF/G/H/K`、`TLONG`、`TLONGDot`、`EquinoctialH/K/P/Q`、`MLONG`、`Delaunayl/g/h`、`DelaunayL/G/H`
3. **布劳威尔-利丹平根数**：`BrouwerShortSMA~MA`、`BrouwerLongSMA~MA`
4. **球面/大地测量坐标**：`Altitude[km]`、`RMAG[km]`、`VMAG[km/s]`、`AMAG[km/s²]`、`RA/DEC`、`RAV/DECV`、`AZI/FPA`、`Latitude/Longitude`、`LST/MHA`、`DLA/RLA`、`PlanetodeticLAT/LON/RMAG`
5. **双曲渐近线与 B 平面**：`IncomingDHA/RHA/RadPer/C3Energy/BVAZI`、`OutgoingDHA/RHA/RadPer/C3Energy/BVAZI`、`BdotT[km]`、`BdotR[km]`、`BVectorMag[km]`、`BVectorAngle[deg]`
6. **离散极值与停机事件** ⚠️（仅用于 Propagate，严禁用于 Achieve）：`Apoapsis`、`Periapsis`、`Node`、`AscendingNode`、`DescendingNode`
7. **笛卡尔状态/角动量**：`X/Y/Z[km]`、`VX/VY/VZ[km/s]`、`AX/AY/AZ[km/s²]`、`HMAG/HX/HY/HZ[km²/s]`、`BetaAngle[deg]`
8. **硬件属性与时间**：`TotalMass/DryMass/FuelMass[kg]`、`DragArea/SRPArea[m²]`、`Cd(2.2)/Cr(1.8)`、`ElapsedSecs/ElapsedDays`、`A1ModJulian/UTCGregorian`
9. **推力器与动力**：`ThrustDirection1(V)/2(N)/3(B)`、`C1[kN]`、`K1[s]`、`DutyCycle/ThrustScaleFactor`、`MassFlowRate[kg/s]`
10. **储罐与增压**：`FuelMass`、`Volume`、`FuelDensity`、`Pressure/Temperature/PressureModel`（`PressureRegulated`/`Blowdown`）
11. **姿态与定向**：`Q1~Q4`、`EulerAngle1/2/3`、`AngularVelocityX/Y/Z[deg/s]`

---

# 4. UI 设计系统

核心文件：[`Styles.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Styles.cs)、[`Widgets.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Widgets.cs)

## 4.1 深空暗黑设计令牌 (Zero-White-Flash) ✅（注意：`OnGUI` 入口存在 `GUI.backgroundColor=Color.white` 全局设置，见审计 L10）

```csharp
Color DarkBg          = new Color(0.038f,0.048f,0.070f,0.98f); // #0a0c12
Color DarkFocusedBg   = new Color(0.038f,0.048f,0.070f,0.98f); // 聚焦底色（消除白光）
Color CardBg          = new Color(0.065f,0.082f,0.120f,0.94f); // #11151f
Color NestedCardBg    = new Color(0.045f,0.060f,0.095f,0.95f); // #0c0f18
Color ActionBtnBg     = new Color(0.010f,0.420f,0.650f,1.00f); // 科技主蓝 #036ba6
Color SuccessBtnBg    = new Color(0.060f,0.420f,0.240f,1.00f); // 翡翠绿 #0f6b3d
Color DeleteBtnBg     = new Color(0.550f,0.140f,0.140f,1.00f); // 绯红 #8c2424
Color SecondaryBtnBg  = new Color(0.090f,0.120f,0.170f,1.00f); // 玄铁灰 #171f2b
```

## 4.2 核心控件与样式规范 ✅（focus 边框微蓝、卡片 border=2px 与"1px/去蓝"存在张力，见审计 L7/L8）

- **Window**：`border=RectOffset(3,3,3,3)`；`fontSize=13`；`fontStyle=Bold`；
- **TextField/TextArea**：聚焦用 `inputFocusBg`，禁止默认蓝色亮斑；文本统一柔白 `#eaf3fa`；
- **微光细边框**：卡片外边框 `1px` 半透明微光细线，左侧指示条 `2.0~3.5px` 极简风格。

## 4.3 任务步骤胶囊徽章配色 ✅

| 任务步类型 | 背景色调 | 边框微光色 | 物理含义 |
| :--- | :--- | :--- | :--- |
| `Propagate` | 深殷红 `(0.24,0.08,0.10)` | 玫瑰红 `(0.62,0.18,0.24)` | 轨道积分与滑行 |
| `FiniteBurn` / `ThrustVector` | 琥珀燃橙 `(0.24,0.12,0.05)` | 亮橙 `(0.65,0.35,0.12)` | 有限推力点火 / 推力矢量控制 |
| `ImpulsiveBurn` | 紫罗兰 `(0.18,0.08,0.24)` | 亮紫 `(0.50,0.22,0.65)` | 瞬时脉冲机动 |
| `Vary` | 科技深青 `(0.05,0.16,0.26)` | 亮青 `(0.12,0.42,0.65)` | 决策变量寻优 |
| `Achieve` | 极光墨绿 `(0.05,0.18,0.12)` | 亮翠绿 `(0.12,0.48,0.32)` | 目标约束达成 |
| `Stage` | 金黄 `(0.22,0.16,0.05)` | 暖金 `(0.60,0.45,0.12)` | 级间分离 |
| `Target` | 蔚蓝 `(0.06,0.14,0.24)` | 天蓝 `(0.18,0.40,0.65)` | 打靶组起点 |
| `EndTarget` | 钢铁玄灰 `(0.08,0.11,0.16)` | 灰蓝 `(0.20,0.28,0.38)` | 打靶组收敛闭环 |

## 4.4 弹性自适应布局与折叠托盘 ✅

- **折叠托盘**：标题栏 `[▼ / ▶ 📚 任务预设模板库]` 一键折叠；折叠时降至单行显示当前载入模板名；
- **自适应最大高度滚动**：模板 ≤6 个自然平铺；>6 个时最大高度 `130px` 并平滑滚动，不挤压任务序列视窗。

---

# 5. 物理时序、质心提前与 Principia 互操作

## 5.1 前向确定性仿真物理时钟递推 `simClock` ✅

$$T_{\text{ign}, k} = T_{\text{ign}, k-1} + \Delta t_{\text{burn}, k-1} + \Delta t_{\text{coast}, k}$$

- **定时滑行**（`ElapsedSecs/ElapsedDays`）：`simClock += duration`；
- **开普勒特征点过境**（`Apoapsis`/`Periapsis`/`Latitude=0`）：检索首个 `trajectory.Find(p => p.t > simClock + 0.1)` 对齐事件时刻；
- **有限点火**：起燃绝对锁定当前 `simClock`；关机推进为 `simClock + duration`。

## 5.2 专属里程碑节点提取 `GMATTimelineExtractor` ✅

核心文件：[`src/KSPGMAT/Service/GMATTimelineExtractor.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATTimelineExtractor.cs)。输出统一 `MissionMilestoneNode` 列表至 `MissionResult.milestones`：`MissionStart`(T=0) → `CoastStart`(滑行段) → `IgnitionStart`(点火) → `EngineCutoff`(MECO) → 开普勒几何事件(远/近地点、赤道交点) → `TargetBlockStart/End`(打靶闭环) → `MissionEnd`。

## 5.3 变质量冲量质心积分与提前量模型 🟡（审计：实际公式位于 UI 层且与基线公式不同；PrincipiaInterop 未实现，见审计 M11）

有限点火写入 Principia 脉冲节点时计算质心提前量：

$$\Delta t_{\text{lead}} = \frac{1}{\beta} - \frac{t_{\text{burn}} \cdot m_f}{m_{\text{prop}}}, \qquad \beta = \frac{\dot{m}}{m_0} = \frac{F}{I_{\text{sp}} g_0 m_0}, \qquad m_{\text{prop}} = m_0 - m_f$$

节点实际写入历元：`t_node = t_ignition + Δt_lead`（相比粗糙对半 `t + t_burn/2` 精度提升至毫米级）。

---

# 6. 基线维护与提交守则

1. **零警告编译**：任何新增功能必须通过 `deploy.ps1` 编译构建，确保 0 警告 0 错误；
2. **文档同步更新**：新增数据模型字段、UI 控件或 i18n 词条时，必须同步在本基线文档及对应子规范登记；
3. **SSOT 守护**：严禁绕过 `GMATStateManager` 读写孤立变量。

---

# 7. ESA PyKEP

## 7.1 绝对计算基线准则 (Zero-Managed-Math Mandate) 🟡（审计：PykepNative/PykepBridge/PorkchopGenerator 存在大量托管自编公式，见审计 H4/H5/H6）

> [!IMPORTANT]
> 1. **PyKEP 负责全部动力学计算**：单星直达转移、多段 MGA、深空机动、飞掠偏转角 δ、近心高度 r_p、猪排图网格、3 轴 Frenet/VNC 机动矢量，**必须 100% 由 ESA 官方原生 PyKEP C++ 核心库（`keplerian_toolbox` / `pykep_native`）负责**；
> 2. **C# 托管层仅做数据中继与桥接**：严禁自编任何开普勒推导、二体近似公式或经验补丁；仅负责读取 KSP/RSS 星历打包为结构体、UI 视觉呈现、GMAT 脚本 / Principia 飞行计划节点映射；
> 3. **彻底弃用 TWP 遗留近似算法**（旧版单中心简化公式在 RSS 太阳系与多段 MGA 下存在数值奇异点）。

**架构闭环**：`1. PyKEP 原生核心(兰伯特/飞掠/MGA)` → `2. C# 数据中继桥接(PykepBridge: NativePlanetState[] DTO + 差分进化)` → `3. GMAT 太阳系 B-Plane 打靶脚本(GMATMGAScriptGenerator: DeepSpace_ForceModel + BdotT/BdotR 初猜)` → `4. UI(TabMGA: 载入序列/导出脚本/写入计划)`。

## 7.2 原生 C-API 接口规范 ✅（声明侧合规；注意托管兜底在原生不可用时会静默回落，见审计 H4）

- 源码：`src/native/pykep_native/pykep_bridge.cpp`；编译：`src/native/build_native.bat`（MSVC x64 Release /O2 /LD /std:c++17，链接 `keplerian_toolbox.lib`、`boost_serialization.lib`）；
- 导出符号：`pykep_get_version()`、`pykep_lambert_solve()`（Izzo）、`pykep_flyby_con()`、`pykep_flyby_dv()`、`pykep_mga_evaluate()`、`pykep_propagate_lagrangian()`。

## 7.3 核心文件索引

| 职责 | 文件 |
| :--- | :--- |
| 原生 C++ 桥接 | `src/native/pykep_native/pykep_bridge.cpp` |
| 非托管加载器 | `src/KSPGMAT/MGA/PykepNative.cs` |
| 数据桥接与 DTO | `src/KSPGMAT/MGA/PykepBridge.cs` |
| 太阳系常数与星历 | `src/KSPGMAT/MGA/SolarSystemBodies.cs` |
| 脚本编译器 | `src/KSPGMAT/Service/GMATMGAScriptGenerator.cs` |
| 界面集成 | `src/KSPGMAT/UI/GMATMainWindow.TabMGA.cs` |

## 7.4 原生非托管 DLL 隔离部署 🟡（存储侧合规：`native/` 以 `.bin` 存放三库；但加载改为临时目录改名 `.dll` 副本，与字面要求不符，见审计 M5）

KSP 的 `AssemblyLoader` 会扫描 `Plugins/` 下所有 `.dll` 并解析 .NET 元数据，C++ 非托管 DLL 会触发 `ArgumentNullException: Value cannot be null (System.Version.Parse)` 导致游戏加载卡死。标准解决架构：
1. 所有 C++ 原生动态库以 `.bin` 格式存放于 `GameData/KSPGMAT/native/`，**严禁放入 `Plugins/`**；
2. C# 端在 `PykepNative` 静态构造函数中 `SetDllDirectory(".../native")`，经 `kernel32.dll!LoadLibrary` 依序预加载 `boost_serialization.bin` → `keplerian_toolbox.bin` → `pykep_native.bin`；
3. `deploy.ps1` 自动清理 `Plugins/` 中非托管二进制并同步至 `native/`。

## 7.5 双通道历元架构 🟡（GMTClient 双通道实现合规；但 TabMGA 导出路径用错通道，见审计 M6）

- **游戏内物理通道 `FormatUtcGregorian`**：100% 真实反映游戏 UT 历元（如 RP-1 1951 开局），确保行星日心位置 `getRelativePositionAtUT(ut)`、MGA 星历与 UI 视觉 1:1 锁相，杜绝人工年份偏移；
- **GMAT 后端输出通道 `FormatGmatSafeEpoch`**：GMAT DE405 要求历元 ≥1957-10-04；对早期存档(<1957)执行底层安全映射，避免 GMAT CLI 因 DE405 边界校验崩溃。

## 7.6 统一 SPICE 配置与纯净 Sol/RSS 太阳系 🟡（离线字典合规；但动态发现并绘制 Kerbol 虚构天体、后备星历硬编码，见审计 M7）

- **统一配置**：`GameData/KSPGMAT/Config/backend.cfg` 含 `gmat_root`（GMAT bin 目录）、`spice_bsp_path`（如 `de440.bsp`/`de430.bsp`/`de421.bsp`）；
- **纯净太阳系 (Pure Sol/RSS Only)**：彻底移除 Stock 虚构天体（Kerbin/Eve/Duna 等）；核心天体严格限定：水星、金星、地球、月球、火星、木星、土星、天王星、海王星、冥王星。

## 7.7 日心轨道画布高精渲染与等距物理比例 ✅

- **绝对等距比例**：X/Y 严格共享单一缩放因子 `S = min(W,H)×0.44×Zoom / ViewRadiusKm`，严禁按宽高分别缩放导致圆轨道形变；真实反映各行星天心距比例（水星 0.39AU、金星 0.72AU、地球 1.0AU、火星 1.52AU、木星 5.2AU、土星 9.58AU）；
- **平移锁相**：`ΔY_pan = −ΔY_mouse` 实现 1:1 拖拽；
- **工业级渲染**：Xiaolin Wu 亚像素抗锯齿；太阳多层色散径向光晕；行星专属光谱色调 + 土星光环 + 5×7 点阵文字标注。

## 7.8 霍曼相角先验窗口与 3D 交互视口 🟡（窗口逻辑为暴力扫描+调用了 TWP LambertSolver，非 7.8 解析式；俯仰钳位到 −89.9°，见审计 H5/L12）

- **霍曼窗口推荐 `CalculateHohmannWindow`**：相角 `φ = π − ω₂T_hoh`，会合周期 `T_syn = 2π/|ω₁−ω₂|`，快速解析最近转移窗口，注入差分进化种群秒级收敛（杜绝非窗口期 20+ km/s 暴力推进）；
- **3D 自由视口**：右键拖拽俯仰 `5°~89.9°` / 方位角 `0°~360°`；左键平移；滚轮平滑缩放；2D/3D 秒切；高倾角天体绘制 `Z=0` 黄道面投影虚线。

## 7.9 官方 PyKEP 高精猪排图生成器与 3 轴机动矢量 🟡（逐格 ΔV/C3 由 C# 公式计算，非原生，见审计 H6）

- **`PorkchopGenerator`**：多线程并行网格（`t_dep` vs ToF），每单元调用 `keplerian_toolbox::lambert_problem`（Izzo）；等高线热力纹理（Navy→Cyan→Emerald→Amber→Coral），自动标记全局最优★；鼠标悬停读取出发/到达历元、ToF、脱离/捕获 ΔV、C3，一键选取窗口；
- **行星际特征参数输出**：相角 φ、出射角 η、出射 LAN Ω、出射倾角 i、C3、捕获倾角；自动向 GMAT 脚本注入 `ImpulsiveBurn` 与 3 轴 VNC 矢量（ΔV_Prograde/Normal/Radial）。

---

# 8. 三核闭环钳制校准系统计划

> **目标**：在 KEP→GMAT 双向验证后，构建 **KEP → GMAT → Principia** 三核协同闭环，三者互相钳制校准数值，最终实现游戏内 Principia 真实引力机动节点与理论最优设计 1:1 闭环吻合。

```
KEP(全局寻优/猪排图) → GMAT(高精数值打靶/B-Plane) → Principia(游戏内辛积分/飞行计划) → 闭环残差校验(|ΔV|<0.1 m/s ?)
   残差超限 → 回 GMAT 二次微分修正；达标 → 游戏内完美执行
```

## 8.1 三核职责分工

1. **核一 KEP**：两体兰伯特 + MGA 差分进化，全局最小 ΔV 窗口与初始借力序列；产出 `t₀`、各段 `T_k`、借力近拱点 `r_p,k`、初始双曲脱离速度 `v∞`；
2. **核二 GMAT**：以 KEP 输出为初猜，引入全天体引力摄动、SRP、非球形 `J2/J4` 与高精积分器（Prince-Dormand 78 / Runge-Kutta 89）；Differential Corrector 精确打靶 B-Plane（`B·T, B·R`）与飞掠近拱点；产出高精 3 轴机动矢量与点火历元；
3. **核三 Principia**：对接 `PrincipiaInterop` 注入机动矢量/历元；调用内置飞掠优化器做本地钳制；产出游戏内可执行飞行计划点火节点。

## 8.2 实施里程碑 (TODO)

- [x] M1：编译 PyKEP/keplerian_toolbox 原生核心，打通 P/Invoke；
- [x] M2：解析霍曼窗口推荐 + 多线程高精猪排图生成器；
- [x] M3：行星际出发特征参数解析 + GMAT 3 轴 VNC 脚本导出；
- [ ] M4（当前阶段）：KEP→GMAT 数据与打靶双向验证（GMAT CLI 收敛 + B-Plane 达成 + TrajReport 残差校验）；
- [ ] M5（下一步）：Principia C-API 深层接入与飞掠优化联动（`OptimizeBurn`/`OptimizeFlightPlan`）；
- [ ] M6：三核闭环钳制与残差自动校准器（偏差回传二次微分修正，直至 <0.1 m/s）。
