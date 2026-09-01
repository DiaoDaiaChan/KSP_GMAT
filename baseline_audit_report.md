# KSP_GMAT C# 代码基线合规审查报告

> **审查依据**：`baseline.md` v1.0（KSP_GMAT 项目工程底线与架构开发规范）
> **审查范围**：`src/KSPGMAT/**` 全部 33 个 C# 源文件（Python 已移除，不在本次审查范围）
> **审查方式**：逐文件逐条人工通读 + 全库模式扫描（Grep 交叉验证）
> **审查结论**：存在 **3 类红线级违规**、**若干硬编码违规**、**I18n 覆盖严重不足**。详见下方分级清单。
> **说明**：本报告只审计，不修改任何代码。

---

## 一、绝对红线违规（规则 1 / 2，最高严重度）

### 🔴 R1. C# 用齐奥尔科夫斯基公式自算 ΔV / 燃料消耗（违反规则 1.2）
> 规则 1.2 明文：**严禁在 Python 或 C# 中使用齐奥尔科夫斯基公式自行推演等效瞬时脉冲**；有限推力必须由 GMAT 原生 `FiniteBurn` 求解。

| 位置 | 问题 |
|---|---|
| [GMATSolutionMapper.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L330-L340) | `BuildSolvedBurns` 中直接实现齐奥尔科夫斯基积分：`mFlow = thrust*1000/(Isp*9.80665)`、`fuelBurned = mFlow*dur`、`totalDv = (Isp*9.80665)*Log(m0/mEnd)`（注释明写"齐奥尔科夫斯基连续多级递推积分"） |
| 同上 [L306-L307](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L306-L307) | 硬编码兜底推力 `108.1 kN`、比冲 `464.8 s` |
| 同上 [L223-L224](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L223-L224) | 硬编码当前质量兜底 `7822.0 kg` |
| 同上 [L355-L357](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L355-L357) | 上述 C# 自算结果被标记 `has_native_delta_v = true`、`native_result_note = "NASA GMAT 收敛解..."`、`principia_writeable = true`，**被当作 GMAT 原生结果展示并写入 Principia——这是对"GMAT 原生求解"的伪造** |
| [GMATMainWindow.TabResults.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResults.cs#L345-L355) | `ComputeBurnInitialTime` 注释"Exact Tsiolkovsky Rocket Equation"，用 `duration = (m*g0*Isp/F)*(1-exp(-ΔV/(g0*Isp)))` 反推点火时长，`g0=9.80665` 硬编码 |
| 同上 [L357-L358](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResults.cs#L357-L358) | "Universal Centroid Law" 半时长质心提前量启发式（非 GMAT 原生输出） |
| [PrincipiaInterop.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/PrincipiaInterop.cs#L432) | 用火箭方程反推 Principia 节点点火时长 `duration = M*1000*(Isp*g0)*(1-exp(-ΔV/(Isp*g0)))/F` |
| [GMATStateManager.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs#L334-L337) | 分级质量预算看板用 `ΔV = 9.80665*Isp*Log(m0/m1)`（展示用途，仍属红线范畴） |
| 同上 [L46](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs#L46) | `GravitationalAccel = 9.80665` 硬编码常量 |

**影响**：`GMATSolutionMapper` 是"结果权威映射层"，其自算 ΔV 直接污染 `MissionResult.solved_burns` → 展示面板 → Principia 写入链路，是当前最严重的一处"伪原生数值"。

---

### 🔴 R2. C# 伪造物理数值 / 伪造校核数据（违反规则 1 核心宗旨）
> 规则 1 核心：**Python 和 C# 绝不参与物理数值仿真计算，所有天体力学必须由 GMAT 原生引擎 100% 求解**。

| 位置 | 问题 |
|---|---|
| [GMATMainWindow.TabCraft.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabCraft.cs#L81-L83) | **凭空伪造"GMAT vs Principia 星历误差"数值**：`ephemPosDiff_m = 32.4 + 4.8*Sin(UT*1e-6)`、`ephemVelDiff_mms = 1.8 + 0.3*Cos(...)`，再算相对误差百分比展示——这是对物理校验结果的捏造 |
| 同上 [L70](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabCraft.cs#L70) | 硬编码断言"位置残差: **0.000 m** | 速度残差: **0.000 mm/s**（绝对无损重合）" |
| 同上 [L76](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabCraft.cs#L76) | 硬编码地月距离兜底 `384400.0 km` |
| [GMATMainWindow.TabResults.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResults.cs#L297-L335) | `ComputeTimeToNextTLIWindow` 在 C# 中自编轨道几何（相位角、轨道周期、`targetPhase=135.0°` 硬编码）计算地月转移窗口 |

---

### 🔴 R3. 非标旋转系 `EarthMoonRot` + 共面假设 `Z=0 / VZ=0 / INC=0`（违反规则 2.1 / 2.2）
> 规则 2.2 明文：**严禁采用任何非标几何假设断言（例如硬编码 Z=0、VZ=0 这类假设轨道完全共面且无倾角的特化私有条件）**；PR 自检清单明确点名 `EarthMoonRot`。

| 位置 | 问题 |
|---|---|
| [GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L1069-L1073) | 硬编码坐标系列表含 `"MoonInertial"`, `"MoonFixed"`, `"EarthMoonRot"` |
| 同上 [L1387-L1388](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L1387-L1388) | IntelliSense 硬编码 `EarthMJ2000Eq.VZ`（"消除倾角目标 **VZ=0**"）、`EarthMJ2000Eq.Z`（"赤道交点 **Z=0**"） |
| 同上 [L1191-L1231](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L1191-L1231) | 大量 `EarthMoonRot` 特化补全分支 |
| [GMATMainWindow.TabCraft.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabCraft.cs#L279) | 快捷内建坐标系硬编码含 `"EarthMoonRot"`, `"MoonInertial"` |
| [GMATMainWindow.TabResources.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResources.cs#L467-L474) | 硬编码坐标系含 `"MoonInertial"`, `"EarthMoonRot"`，并宣传"地月旋转脉动坐标系" |
| 同上 [L719-L720](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResources.cs#L719-L720) | 事件库硬编码 `"Sat.EarthMJ2000Eq.VZ = 0.0 (赤纬速度=0 消除倾角)"`、`"Sat.Earth.INC = 0.0 (0度赤道共面)"` |
| [GMATMainWindow.Modals.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Modals.cs#L707-L717) | 打靶向导模板硬编码 `Achieve Earth.INC = 0.0`（"轨道面倾角变换"） |

---

## 二、硬编码违规（规则 3，严重）

### 🟠 H1. 积分器 / 大气模型能力列表在 C# 写死（违反规则 3.1）
> 规则 3.1 明文：可用积分器列表（`RungeKutta89`、`PrinceDormand78` 等）与大气模型列表（`MSISE90`、`JacchiaRoberts` 等）**必须通过 `Moderator.GetListOfAllFactoryItems()` 运行时自检发现**。

| 位置 | 问题 |
|---|---|
| [GMATMainWindow.Modals.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Modals.cs#L866) | 硬编码积分器列表 `{"RungeKutta89","PrinceDormand78","RungeKutta56",...}` |
| 同上 [L930](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Modals.cs#L930) | 硬编码大气模型列表 `{"None","MSISE90","JacchiaRoberts","Exponential"}` |
| [PropagatorConfig.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/PropagatorConfig.cs#L8) | 默认 `integratorType = "RungeKutta89"`（L60/77/94 预设亦写死） |
| [GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L1527-L1559) | 默认传播子/求解器全部硬编码（`RungeKutta89`、`NewtonRaphson`、`centralBody=Earth/Luna`） |
| [GMATScriptGenerator.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptGenerator.cs#L179) | 兜底写死 `integratorType ?? "RungeKutta89"`（L192 同） |

### 🟠 H2. 天体物理参数硬编码（违反规则 3.1 动力学参数零硬编码）
| 位置 | 问题 |
|---|---|
| [GMATClient.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs#L866) | `bodyRadiusKm > 0 ? ... : "6378.1"` 硬编码地球半径兜底 |
| 同上 [L901](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs#L901) | `RenderTrajectory(viewMode, "Earth", 6378.14, ...)` 硬编码默认天体+半径 |
| [GMATOrbitCanvas.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATOrbitCanvas.cs#L35) | 画布中心天体半径兜底 `6378.1363`（L49 又硬编码月球 `1737.4`） |
| [GMATMainWindow.Modals.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Modals.cs#L784) | 渲染兜底 `: 6378.14` |
| [GMATMainWindow.TabSequence.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabSequence.cs#L898) | 快速插入 Achieve 默认值 `42164.14 / 1837.4 / 7000.0`（L1007/L1047 同） |
| 同上 [L1064](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabSequence.cs#L1064) | 硬编码约束默认值 `constraint_val = 6578.14` |
| [GMATMainWindow.TabResources.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResources.cs#L718) | 事件库硬编码 `Sat.Earth.SMA = 42164.14 (GEO)`、`Sat.Earth.Altitude = 35786.0 (GTO)` |
| [GMATScriptParser.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptParser.cs#L356) | Achieve 解析兜底 `val = 42164.14` |
| [GMATSolutionMapper.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L306-L307) | 兜底推力/比冲（见 R1） |

### 🟠 H3. 开发者私有绝对路径硬编码（违反规则 3.3）
| 位置 | 问题 |
|---|---|
| [KSPGMAT.csproj](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/KSPGMAT.csproj#L15-L58) | 全部 DLL 引用写死 `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\...` 私有绝对路径 |
| [GMATClient.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs#L333-L334) | `@"C:\Users\43701\gmat-win-R2026a\bin"`、`@"d:\nocomp\gmat-win-R2026a\bin"` |
| 同上 [L352-L354](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs#L352-L354) | `C:\Users\43701\AppData\Local\Programs\Python\Python310/311/312\python.exe` |
| [GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L408-L409) | `C:\Users\43701\...\gmat_capabilities.json`、`d:\nocomp\...` |
| [GMATConsoleRunner.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATConsoleRunner.cs#L41-L42) | `C:\Users\43701\...\GmatConsole.exe`、`D:\gmat-win-R2026a\...` |

> 注：这些路径虽带"多候选 + `Directory.Exists` 兜底"逻辑，但**仍包含具体用户名/盘符**，不符合"严禁任何开发者私有机器绝对路径"。

### 🟠 H4. 界面固定像素分辨率（违反规则 3.2，部分违规）
| 位置 | 问题 |
|---|---|
| [GMATMainWindow.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.cs#L145) | `coordSystemWindowRect = new Rect(...x, ...y, 480, 520)` 固定 480×520 |
| [GMATMainWindow.Modals.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Modals.cs#L34) | `paramPickerWindowRect = new Rect((Screen.width-780)/2, (Screen.height-520)/2, 780, 520)` 固定 780×520 |
| [GMATMainWindow.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.cs#L320-L330) | 主窗口/子窗口硬编码像素 clamp（980~1360 / 640~860 / 780 / 580 / 840 / 640…）；主体已按 `Screen.width/height` 比例自适应 + 拖拽缩放 + 最大化，**基本合规，但存在上述固定像素子窗口** |

---

## 三、I18n / 双语接入严重不足（规则 5.2）

| 位置 | 问题 |
|---|---|
| [GMATMainWindow.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.cs#L54) | 页签名 `tabNames` 硬编码中文，未走 I18n |
| [TabCraft / TabResources / TabSequence / TabResults / Modals / Settings](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabCraft.cs) | 全 UI 大量 `GUILayout.Label/Button("中文...")` 直接写死（数百处），切英文后仍显示中文 |
| [I18n.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/I18n.cs#L52-L214) | 词条字典仅约 110 条，覆盖率远低于实际 UI 文本量 |
| [GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L681-L797) | 物理参数 Tooltip 词条约 130 条硬编码（可作为 Tooltip 基础，但多数 UI 控件未接 Tooltip） |

> 规则 5.2 要求"所有界面文本必须全面接入 I18n、晦涩参数配 Tooltip + 单位徽章"。当前仅参数选择器局部实现，**全局未达标**。

---

## 四、边界 / 备注（不判违规或需裁量）

| 位置 | 说明 |
|---|---|
| [GMATClient.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs#L386-L422) `JdToUtcGregorian` / `KspUtToJd` | 儒略日→公历换算，属历元换算而非天体力学，可接受；但 `2433647.5`、`2437000.0` 等历元常量硬编码 |
| [GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L962-L975) `MapBodyName` | Kerbin→Earth 等 KSP↔GMAT 名称映射，属桥接逻辑，可接受 |
| [MechJebInterop.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MechJebInterop.cs#L51-L54) | 直接展示 MechJeb 分级 ΔV（外部工具数值，仅展示）；代码注释已声明"所有轨迹与机动结果必须来自 GMAT"，精神合规但展示来源非 GMAT |
| [GMATConsoleRunner.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATConsoleRunner.cs#L85-L121) | 直接调用原生 `GmatConsole.exe` 并从原生输出判断收敛 → **符合"GMAT 原生引擎"要求** |
| [GMATScriptGenerator.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptGenerator.cs) | 输出 100% 标准 GMAT 脚本（Spacecraft/ChemicalTank/ChemicalThruster/FiniteBurn/Propagate/Target/Vary/Achieve/Report）→ **基本合规** |
| [GMATStateManager.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs#L408-L425) `SanitizeGmatScript` | Pure-ASCII 过滤，合规 |
| 静态保底参数库 [GMATParameterRegistry.cs L647-L678](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L647-L678) | 属于规则 5.1 明确要求的"静态保底核心数据"，合规（但其中 `6578.14` 等示例值仍属硬编码地球数据，建议改为占位符） |
| Release 0 警告 | 静态代码无法验证编译告警；但 csproj 写死绝对路径会使他人机器构建失败，间接违反发布规范 |

---

## 五、整改优先级建议（仅建议，未改代码）

1. **P0（红线，必须立即改）**
   - 移除 `GMATSolutionMapper.BuildSolvedBurns` 中的齐奥尔科夫斯基自算；ΔV/时长/燃料一律从 GMAT 原生 `DifferentialCorrector*.data` / `TrajReport.txt` 提取，缺失则不构造"原生"标记（`has_native_delta_v=false`），严禁冒充 GMAT 收敛解。
   - 删除 `TabCraft` 中伪造的"星历误差正弦/余弦曲线"及"0.000m 残差"断言。
   - 删除 `EarthMoonRot`、`MoonInertial` 等非标/旋转系及 `Z=0/VZ=0/INC=0` 共面假设词条；`ComputeTimeToNextTLIWindow` 交给 GMAT 打靶而非 C# 自算。

2. **P1（硬编码）**
   - 积分器/大气模型列表改由运行时自检发现（或至少从 `gmat_capabilities.json` 动态注入），移除 C# 写死的枚举。
   - 天体半径/经典轨道值统一走 GMAT `SolarSystem` 动态读取，删除 `6378.x/1737.x/42164/35786/86164` 等散落常量。
   - 清理所有 `C:\Users\43701\...` / `d:\nocomp\...` 私有路径（含 csproj 引用），改相对路径 / 环境变量 / `KSPUtil.ApplicationRootPath`。
   - 固定像素子窗口（坐标系统 480×520、参数选择器 780×520）改为随容器自适应。

3. **P2（I18n 覆盖）**
   - 将全部硬编码中文 UI 文本迁入 `I18n.cs` 词条并补齐英文，页签/按钮/卡片/提示统一走 `I18n.T()`。

---

*报告生成时间：2026-09-01 · 仅审查，未改动任何源码。*
