# KSP_GMAT 基线合规审计报告 (Baseline Compliance Audit Report)

> **审计依据**：`baseline.md` (Master Baseline Specification, R2026a Standard Baseline)
> **审计范围**：`src/KSPGMAT/` 下全部 46 个 C# 源码文件（约 1.9 万行）
> **审计方式**：逐行通读 + 交叉核对调用链 + 全量 grep 统计；**仅审查，未修改任何代码**
> **审计日期**：2026-09-02

---

## 0. 合规性总览

| 严重级别 | 数量 | 说明 |
| :--- | :--- | :--- |
| 🔴 高 | 9 | 违反核心铁律 / 零托管计算铁律 / 数据造假 / 净化器缺失 |
| 🟠 中 | 18 | i18n 三语言缺失、DLL 隔离机制偏离、纯净太阳系未强制、C# 层写死动力学清单、5.3 公式不符等 |
| 🟡 低 | 14 | 硬编码前缀、能力标记不对称、样式微偏差、经验补丁、文案残留等 |

**最严重结论（Top 3）：**
1. **`SanitizeGmatScript` 名不副实**：基线 1.4 要求 4 条子规则，实际只实现了第 1 条（纯 ASCII），且后台物理求解路径完全绕过它。
2. **C# 托管层大量自编动力学公式**（违反第 7 章 Zero-Managed-Math 铁律）：自编 Izzo 兰伯特、双曲飞掠、MGA 评估、猪排图 ΔV/C3、Hohmann 窗口，部分还**冒充 GMAT 原生收敛解**。
3. **UI 层约 633 处硬编码中文 + 103 处中文 ScreenMessage**，绝大多数未接入 `I18n.T/Format`，词典 133 条全部缺俄语。

---

## 1. 高严重级违规 (🔴)

### H1. SanitizeGmatScript 仅实现 1.4 的 1/4 子规则
- **文件**：[GMATStateManager.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs#L586-L603) `:586-603`
- **违规条款**：1.4（子规则 2/3/4）、0（SSOT 合规闸门）
- **证据**：函数体仅逐字符 ASCII 白名单过滤（`c >= 32 && c < 127`），以下全部缺失：
  - 子规则 2：正则 `\bSat\d+\.` 前缀自愈 → 无任何正则处理；
  - 子规则 3：`Upper<=Lower` / `Upper==0` 安全边界修正、`Tolerance<=0` 按距离量(0.5~1.0km)/速度量(0.01km/s)赋容差 → 无逻辑；
  - 子规则 4：`Sat.Earth.Node` → `Sat.Earth.Latitude = 0.0` → 无逻辑。
- **连带**：条款 1.5 的别名归一化（Tianwen/Change5/europaclipper/Juno、maintank/MMH、DC1/NLP/VF13ad1）**全库无任何实现**，grep 无命中。

### H2. GMATSolutionMapper 自编物理公式并冒充 GMAT 原生结果
- **文件**：[GMATSolutionMapper.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L391-L419) `:391-419`
- **违规条款**：7（零托管计算）、重点检查项（冒充原生）
- **证据**：`mFlow = thrustKN*1000/(isp*9.80665)`（393）、`totalDv = 9.80665*isp*Log(m0/mEnd)`（396，齐奥尔科夫斯基），却标记 `has_native_delta_v = true`（416）、`native_result_note = "NASA GMAT 收敛解"`（419）。该 ΔV 随后被 TabResults 写入 Principia 节点。
- **附带**：`:666-670` 在 C# 层自编开普勒近/远心距 `r_p = a(1−e)−R`、`r_a = a(1+e)−R`，并隐藏硬编码地球半径 6378.137，本应从 GMAT 原生报告直接读取。

### H3. 后台物理求解路径完全绕过 SanitizeGmatScript
- **文件**：[GMATClient.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs#L697-L701) `:697-701` + [GMATConsoleRunner.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATConsoleRunner.cs#L85-L86) `:85-86`
- **违规条款**：1.4（后台物理求解前必须 100% 介入）、1.1（GMAT 物理求解须经 SSOT 合规拦截）
- **证据**：`PropagateMission` 中 `scriptToSend = rawScript ?? GenerateScript(...)` 原样推送（697-701），落盘 `File.WriteAllText(scriptFile, scriptContent)`（85-86）——全程无 `SanitizeGmatScript`。上游 IDE 缓冲区（[IDE.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.IDE.cs#L24-L28) `:24-28`、TabSequence.cs `:1295-1299`）的原始脚本直接进入此入口。
- **对比**：`ExportScript`（GMATClient.cs `:761-775`）与剪贴板/模板导出路径均正确调用净化器，唯独后台求解漏掉。

### H4. PykepNative 托管层自编全套 Izzo 兰伯特/双曲飞掠公式
- **文件**：[PykepNative.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/PykepNative.cs#L220-L407) `:220-407`
- **违规条款**：7（第 1/2/3 条：PyKEP 100% 负责计算、C# 严禁自编开普勒推导、弃用 TWP）
- **证据**：`SolveLambertManaged`（224-292，C# 版 Izzo 全流程 + Householder 迭代）、`HouseholderIterationsManaged`（294-309）、`EvaluateFlybyManaged`（372-407，C# 自算 δ 角/近拱半径/动力助推 ΔV）；`:52` `_versionString = "Managed Fallback Core"`，`:160/:193` 原生不可用时**静默回落**到自编公式——等同恢复被弃用的遗留近似引擎。

### H5. PykepBridge C# 自编 MGA 评估 / 出射几何 / ΔV / Hohmann 公式
- **文件**：[PykepBridge.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/PykepBridge.cs#L343-L361) `:343-361`、[PykepBridge.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/PykepBridge.cs#L482-L603) `:482-603`、[PykepBridge.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/PykepBridge.cs#L813-L921) `:813-921`
- **违规条款**：7、7.2（精神）、7.8
- **证据**：
  - `CalculateDepartureDeltaV/CalculateArrivalInsertionDeltaV`（343-361）：C# 自编 `v_park=√(μ(2/rp−1/a))`、`v_hyp=√(v∞²+2μ/rp)`；
  - `ManagedMGAEvaluate`（482-603）：原生 `pykep_mga_evaluate` 为 null 时 C# 完整实现多段 MGA——C# 算 v∞(524)、C3(532)、出射赤经/赤纬/出射角(547-552 自编双曲几何)、相角(553)、VNC 三轴分解(572-582)、捕获 ΔV(590-596)；
  - `CalculateHohmannWindow`（813-921）：调用**项目外遗留 `global::LambertSolver`（TWP 类，不在本仓库）**（820/865/870），C# 自编圆轨道 ΔV 公式（906-908），且用暴力 for 双重扫描而非 7.8 的解析式 `φ=π−ω₂·T_hoh`、`T_syn=2π/|ω₁−ω₂|`。

### H6. PorkchopGenerator 猪排图网格 ΔV/C3 由 C# 公式计算
- **文件**：[PorkchopGenerator.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/PorkchopGenerator.cs#L166-L177) `:166-177`（另 `:142-144`）
- **违规条款**：7（第 1 条）、7.9
- **证据**：每网格兰伯特速度解调用原生 `PykepNative.SolveLambert`（合规部分，166），但逐格 `vInfDep = v1−vP1`(167)、`c3 = vInfDep²`(171)、`ejDV = CalculateDepartureDeltaV`(173)、`inDV = CalculateArrivalInsertionDeltaV`(175)、`totDV = ejDV+inDV`(177) 均为 C# 二体拼接公式；`vPark1=√(mu/rPark1)`(142) 为 C# 圆轨道假设。7.9 要求猪排图网格 100% 由原生 PyKEP 计算。

### H7. GMATStateManager 托管层自编齐奥尔科夫斯基 ΔV 预算
- **文件**：[GMATStateManager.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs#L512-L515) `:512-515`
- **违规条款**：7（零托管计算精神）
- **证据**：`budget.DeltaV = 9.80665 * isp * Math.Log(wet/dry)`（515）在托管层自算并展示为看板预算，非取自 MechJeb/GMAT 原生输出。属质量预算而非开普勒推导，但违背零托管计算精神。

### H8. i18n：全 UI 系统性未接入词典（633 处硬编码中文 + 103 处中文 ScreenMessage）
- **文件**：全部 UI 文件（分布见统计表 §4.1）
- **违规条款**：2.1（第 1 条）、2.3
- **证据**：仅 Tab 顶栏按钮、序列工具栏部分按钮、ThrustVector 卡片等少数位置使用 `I18n.T(...)`；约 **95%+** UI 文本仍为硬编码中文字面量。重灾区：TabSequence.cs 208 处、TabMGA.cs 121 处、Modals.cs 95 处、TabResources.cs 87 处、TabCraft.cs 53 处、TabResults.cs 47 处。且大量 `中文 (English)` 括号混搭（见 §3.2）。

### H9. GMATSolutionMapper 绕过 SSOT 直读活动飞船
- **文件**：[GMATSolutionMapper.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATSolutionMapper.cs#L246) `:246`、`:383-388`
- **违规条款**：0（严禁绕过 GMATStateManager 读写孤立变量）
- **证据**：SSOT 质量/推力器为空时直接 `FlightGlobals.ActiveVessel.GetTotalMass()*1000`（246）与 `FindPartModulesImplementing<ModuleEngines>()`（384）反射读取，未回写 SSOT，可能产生与 SSOT 不一致的烧蚀质量基准。

---

## 2. 中严重级违规 (🟠)

### M1. SRPArea 默认值与基线不符（1.0 vs 15.0）
- **文件**：[GMATStateManager.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATStateManager.cs#L65) `:65`、`:151`
- **违规条款**：1.2.1（SRPArea 默认 15.0）
- **证据**：模型与默认字典均 `SRPArea = 1.0`（官方 GMAT 缺省），而基线规定 15.0。未同步飞船时生成的脚本会写入 `Sat.SRPArea = 1.0`；且与 GMATClient.cs:56 导入侧 `craft_srp_area = 15.0` 自相矛盾。

### M2. GMATScriptGenerator 硬编码 "Sat" 并内联拼接路径，生成脚本未净化
- **文件**：[GMATScriptGenerator.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptGenerator.cs#L14) `:14`、`:435-436`
- **违规条款**：1.3（必须通过标准构造器）、1.4（后台求解前净化）
- **证据**：`string satName = "Sat";`(14) 硬编码，全文 `$"{satName}.{...}"` 内联拼接，从未调用 `GetQualifiedSpacecraftProperty/GetQualifiedThrusterProperty`；`GenerateScript` 直接 `return sb.ToString()` 无净化。Vary 区间/容差（336-342）无 1.4 子规则 3 的防退化守护。

### M3. 词典三语言规范未落实（Lang 缺 RU，133 条全缺俄语）
- **文件**：[I18n.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/I18n.cs#L11) `:11`、`:52-215`
- **违规条款**：2.4
- **证据**：`public enum Lang { ZH, EN }`（11）缺 RU；全部 133 条词条均为两元素数组 `new[]{"中文","English"}`，无任何俄语。

### M4. 词典 Key 前缀不合规（13 条无规范前缀）+ 词典内混搭
- **文件**：[I18n.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/I18n.cs#L56-L114) `:56,63,65,68-71,109-114`
- **违规条款**：2.2、2.1（第 2 条）
- **证据**：`window_title/no_vessel/close/status_ready/status_computing/status_done/status_fail/preset_header/preset_geo/preset_tli/preset_ce6/preset_nrho/preset_mars` 13 个 key 不在 `tab_/btn_/add_/type_/craft_/seq_/burn_/vector_/target_/ide_/res_/settings_/tip_` 规范前缀内；词典值本身含 `"🪐 深空借力 (MGA)"`(60)、`"参考历元 (UTC)"`(79)、`"✏️ Vary 寻优"`(136)、`"🎯 Achieve 目标"`(137) 等被条款明文禁止的混搭。

### M5. PykepNative DLL 隔离机制偏离 7.4 字面要求
- **文件**：[PykepNative.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/PykepNative.cs#L61-L120) `:61-120`
- **违规条款**：7.4
- **证据**：存储侧合规（`GameData/KSPGMAT/native/` 确以 `.bin` 存放三库）；但加载机制改为**复制到临时目录并改名 `.dll`**（73-82）、`SetDllDirectory(tempDir)`（84）而非 7.4 规定的 `SetDllDirectory(".../native")` + 直接 `LoadLibrary` `.bin`（85-92 加载的是改名副本）。加载失败静默降级托管计算（116-119）。

### M6. TabMGA 的 GMAT 脚本导出未走 FormatGmatSafeEpoch（双通道不一致）
- **文件**：[TabMGA.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabMGA.cs#L370) `:370`（另 `:361,381`）
- **违规条款**：7.5
- **证据**：`GMAT SC.Epoch = '{FormatUtcGregorian(sol.DepartureUT)}'`（370）使用真实历元通道，RP-1 1951 存档会写入 <1957 历元触发 GMAT DE405 边界崩溃；而 [GMATMGAScriptGenerator.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATMGAScriptGenerator.cs#L67) `:67` 正确使用 `FormatGmatSafeEpoch`（合规），两处行为不一致。

### M7. SolarSystemBodies 支持/绘制 Kerbol 虚构天体 + 后备星历硬编码
- **文件**：[SolarSystemBodies.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/SolarSystemBodies.cs#L157-L253) `:157-253`、[GMATOrbitCanvas.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATOrbitCanvas.cs#L317-L321) `:317-321`
- **违规条款**：7.6
- **证据**：离线字典（109-155）仅 9 大真实天体（合规），但 `GetPlanet/GetAvailablePlanets`（161-211）优先从 `FlightGlobals` 动态发现，Stock 下返回 Moho/Eve/Kerbin/Mun/Minmus/Duna/Dres/Jool/Eeloo；`GetDefaultIcon`（241-250）与 `IsMajorPlanetOrMissionBody`（OrbitCanvas 317-321）**主动支持并绘制**虚构天体而非过滤；后备星历（44-88）为硬编码 J2000 平根数 + C# 牛顿迭代解开普勒方程，未接入 `spice_bsp_path`（de440.bsp 已在 backend.cfg 配置但未使用）。

### M8. 动力学清单在 C# 层写死（积分器/大气模型/求解器/传播子）
- **文件**：[GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L1273-L1306) `:1273-1306`、[PropagatorConfig.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/PropagatorConfig.cs#L8-L109) `:8,41,54-109`、[SolverConfig.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/SolverConfig.cs#L8-L68) `:8,22-68`、[Widgets.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Widgets.cs#L203) `:203`、[GMATScriptParser.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptParser.cs#L229) `:229`
- **违规条款**：7（第 2 条精神：动力学清单由 GMAT 原生决定，C# 层不得写死）
- **证据**：`integratorType="RungeKutta89"`、`dragModel="None"` 默认值；`InitDefaultPropagatorsAndSolvers` 硬编码 LEOProp/LunarNRHOProp/DefaultDC/BroydenDC；Widgets `DrawInlinePropagatorPicker` 硬编码 `{"LEOProp","CislunarProp","LunarNRHOProp","DefaultProp"}`；Parser 有限点火兜底 `propName="LEOProp"`。虽支持 JSON 动态加载，默认注册路径仍写死。

### M9. GMATParameterRegistry 动态参数无能力位标记，极值过滤靠名字白名单
- **文件**：[GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L203-L205) `:203-205`、`:465-482`、`:575`
- **违规条款**：3.1
- **证据**：动态 `GMATParam` 无 `Capability/ParamCapability` 字段；`LoadFromJson` 仅按 5 个名字硬编码过滤极值事件（`if (p.name=="Apoapsis"||...) continue;`），`is_extremum` 标志解析后从未用于"禁止用于 Achieve"的能力约束——任何 JSON 新增极值参数都会漏入 Achieve 目录。

### M10. C1 参数单位元数据与脚本实际单位矛盾（[kN] vs N）
- **文件**：[GMATScriptParser.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptParser.cs#L181) `:181` + [GMATParamSchema.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATParamSchema.cs#L229) `:229`
- **违规条款**：3.3（单位一致性）
- **证据**：schema 声明 `C1` 单位 `[kN]`，但生成端（GMATScriptGenerator.cs:97）按牛顿写 `C1 = ThrustKN*1000`，解析端按牛顿读 `ThrustKN = thrustN/1000`。内部自洽但与自身元数据矛盾，存在 1000 倍单位语义错位。

### M11. 5.3 质心提前量公式未按基线实现（且位于 UI 层）
- **文件**：[TabResults.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResults.cs#L482) `:482` + [PrincipiaInterop.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/PrincipiaInterop.cs#L380) `:380` + [TabMGA.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabMGA.cs#L1707-L1715) `:1707-1715`
- **违规条款**：5.3
- **证据**：基线要求 `Δt_lead = 1/β − t_burn·m_f/m_prop`；实际代码用 `centroidLead = (ve*m0*(1−√(mf/m0)))/thrust`（TabResults.cs:482，公式不同且位于 UI 层）；`PrincipiaInterop.InsertOrUpdateBurn` 直接把 `burnInitialTime` 写入 `fBurn_InitialTime`（380）**无任何提前量计算**；TabMGA 用固定 50s 燃烧时长粗糙对半写入。

### M12. 后台物理求解绕过净化（同 H3，此处强调中级别连带：IDE 缓冲区脚本无兜底）
- 详见 H3。中文/Emoji/SatX 前缀/退化区间/Node 终止条件在后台求解路径均无兜底。

---

## 3. 低严重级违规 (🟡)

| 编号 | 文件:行号 | 条款 | 说明 |
| :--- | :--- | :--- | :--- |
| L1 | [GMATNamespaceResolver.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATNamespaceResolver.cs#L197-L199) `:197-199` | 1.3 | 顶层 IntelliSense 硬编码 `"Sat"` / `"Sat.Earth"`，天体写死 Earth，未走标准构造器 |
| L2 | [GMATParamSchema.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATParamSchema.cs#L231) `:231` | 3.3 | `GravitationalAccel` 不在 11 大分类清单、非 GMAT 官方参数（g0 为常数），属多余"假参数" |
| L3 | [GMATParamSchema.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATParamSchema.cs#L202) `:202` | 3.1/3.3 | `BetaAngle` 归入 Cartesian 分类却缺 `CoordinateCartesian` 能力位，分类与能力自相矛盾 |
| L4 | [GMATParamSchema.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Model/GMATParamSchema.cs#L195-L201) `:195-201` | 3.1 | 笛卡尔同族能力不对称：`AX/AY/AZ`、`HX/HY/HZ` 缺 `ContinuouslyDifferentiable`，而 `VX/VY/VZ`、`HMAG` 有，导致加速度/角动量无法作 Achieve 连续目标 |
| L5 | [GMATParameterRegistry.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATParameterRegistry.cs#L681) `:681` | 3.3 | 停止条件 fallback 用 `"Time"` 而非官方 `ElapsedSecs/ElapsedDays` |
| L6 | [Styles.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Styles.cs#L11) `:11` | 4.1 | `darkFocusedBg` 边框色 `(0.14,0.38,0.58)` 与 `darkBg` 边框 `(0.16,0.20,0.26)` 不同，未做到"DarkFocusedBg 与 DarkBg 完全一致" |
| L7 | [Styles.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Styles.cs#L33) `:33`、`:89` | 4.2 | `inputFocusBg`/`textAreaFocusBg` 仍带高亮蓝/青边框，与"禁止默认蓝色亮斑"存在张力 |
| L8 | [Styles.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Styles.cs#L119) `:119` | 4.2 | 卡片 GUIStyle border=2px，而基线要求"卡片外边框 1px 半透明微光细线"（纹理 borderWidth=1，两者不一致） |
| L9 | [Styles.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.Styles.cs#L487-L488) `:487-488` + [TabSequence.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabSequence.cs#L851) `:851` | 4.3/4.2 | `GetStepThemeColor("ThrustVectorControl")` 返回海洋蓝 `(0.14,0.65,0.96)`，与 4.3 ThrustVector 应用的琥珀色不一致；左侧指示条悬停态 6px 超出 2.0~3.5px 上限 |
| L10 | [GMATMainWindow.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.cs#L604) `:604` | 4.1 | `GUI.backgroundColor = Color.white;` 在 OnGUI 入口全局设置，是潜在白光闪烁源头（当前靠自定义纹理兜底，未覆盖的默认 skin 控件会现白底） |
| L11 | [GMATOrbitCanvas.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATOrbitCanvas.cs#L408-L484) `:408-484` | 7（边界） | 3D 转移弧线渲染用 C# 自编完整二体开普勒传播（423-432 根数转换、449-469 牛顿迭代解 KEP）；双曲段 477-483 用 `Vector3d.Lerp` 线性拟合（非真实轨道）。属渲染视觉呈现可容忍，但建议改 `pykep_propagate_lagrangian` |
| L12 | [TabMGA.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabMGA.cs#L1041-L1047) `:1041-1047` | 7.8 | 右键俯仰被钳到 `−89.9°`（允许负俯仰穿到黄道面下方），基线要求 `5°~89.9°` 无下限钳制 |
| L13 | [TabMGA.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabMGA.cs#L881-L887) `:881-887` | 7（经验补丁） | "椭圆捕获轨"预设硬编码 `ra = rp*39.0`（e=0.95 经验倍数），属 C# 经验补丁 |
| L14 | [MGABenchmarkSuite.cs](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/MGABenchmarkSuite.cs#L170-L267) `:170-267` | 7（第 3 条，命名） | 输出文案 `"TWP 权威脱离几何"`(227)、`"TWP 权威 + PyKEP"`(259)、`"100% 物理矢量严密闭环"`(290)、TabMGA.cs:205 `"ESA PyKEP & TWP Lambert"` 与弃用 TWP 口径冲突；且 `DeltaVWithinTolerance`(176) 从未计算，PASS 判定(256)不含 ΔV 容差，属过度宣称 |

---

## 4. 统计信息

### 4.1 i18n 硬编码中文分布（违反 2.1/2.3）

| 文件 | 硬编码中文处数 | 备注 |
| :--- | :--- | :--- |
| GMATMainWindow.TabSequence.cs | 208 | 最严重：序列卡片/求解中枢/命令库/打靶矩阵全硬编码 |
| GMATMainWindow.TabMGA.cs | 121 | MGA 面板/历史基准/分析报表全硬编码 |
| GMATMainWindow.Modals.cs | 95 | 参数/求解器/传播子/坐标系/行星选择器全硬编码 |
| GMATMainWindow.TabResources.cs | 87 | 资源树 8 大分类/IDE 工具栏全硬编码 |
| GMATMainWindow.TabCraft.cs | 53 | 飞船参数卡/RO 卡/坐标系卡全硬编码 |
| GMATMainWindow.TabResults.cs | 47 | 结果页全硬编码 |
| GMATMainWindow.TabSettings.cs | 11 | 设置页全硬编码（词典已有 settings_* 却未用） |
| GMATMainWindow.Widgets.cs | 6 | 行内选择器/变量徽章/补全栏 |
| GMATMainWindow.cs | 5 | 顶栏/标题/状态 |
| **合计** | **633** | 另有 103 处中文 `ScreenMessages.PostScreenMessage`、73 处硬编码步骤 label |

### 4.2 词典 (I18n.cs)

| 指标 | 数值 |
| :--- | :--- |
| 总词条 | 133 |
| 缺俄语（仅 ZH/EN） | 133（100%） |
| Key 前缀不合规 | 13 |
| 词条值含括号混搭/双语混杂 | ≈6 |
| Lang 枚举缺 RU | 1 |

### 4.3 合规确认项（未发现明显违规）

- **GMATTimelineExtractor**：5.1 simClock（`trajectory.Find(p => p.t > simClock + 0.1)`）、5.2 全部里程碑节点——**合规**。
- **GMATParamSchema**：`ParamCapability` 位掩码、`GMATParamDefinition` 8 字段、极值事件仅 `DiscontinuousExtremumEvent`（未误标可用于 Achieve）——**合规**。
- **Styles.cs 设计令牌**：8 个颜色令牌、window border/fontSize、8 个胶囊徽章配色——**合规**（偏差见 L6-L9）。
- **4.4 折叠托盘**：`▼/▶ 📚 任务预设模板库` + 模板数>6 时 `GUILayout.MaxHeight(130)` 滚动——**合规**（TabSequence.cs:174-177,250-253）。
- **GMATOrbitCanvas 等距几何**：单一缩放因子 `S=min(W,H)*0.44/ViewRadiusKm`、Xiaolin Wu 抗锯齿、太阳光晕、5×7 点阵字体、`ΔY_pan=−ΔY_mouse`——**合规**。
- **GMATConsoleRunner**：真实调用 `GmatConsole.exe --run --exit` 并解析输出判收敛，无科学计算伪装——**合规**。
- **GMATMGAScriptGenerator**：`FormatGmatSafeEpoch` + ImpulsiveBurn 3 轴 VNC 注入——**合规**。
- **KSPGMATBridge / MechJebInterop / ROEngineInterop / KSPGMATSettings / GMATMainWindow.Models / ToolbarRegistration / PrincipiaNodeComparison**：纯数据中继，**合规**（MechJebInterop.cs:73-74 明确"All trajectory and maneuver results must come from GMAT"）。
- **5.3 提前量公式**：TabResults.cs:482 与 5.3 基线公式不一致（见 M11）；TimelineExtractor/PrincipiaInterop 均未实现基线公式。
- **GMATClient 双通道历元**：`FormatUtcGregorian`（游戏真实）/`FormatGmatSafeEpoch`（<1957 安全映射）实现本身符合 7.5（问题仅在 TabMGA 导出路径用错通道，见 M6）。

---

## 5. 整改优先级建议（仅建议，未实施）

1. **🔴 高**：重建 `SanitizeGmatScript` 为完整 4 子规则实现（ASCII + SatX 自愈 + 区间/容差守护 + Node→Latitude），并在后台求解入口（GMATClient.PropagateMission）强制调用；补齐 1.5 别名归一化。
2. **🔴 高**：删除/禁用 `PykepNative` 托管兜底（Managed Lambert/Flyby）与 `PykepBridge.ManagedMGAEvaluate`，原生不可用时明确报错而非静默回落；猪排图 ΔV/C3 改走原生评估入口；`CalculateHohmannWindow` 改按 7.8 解析式并去除 `global::LambertSolver` 依赖。
3. **🔴 高**：移除 `GMATSolutionMapper` 的托管 ΔV/质量流/开普勒推导，改为从 GMAT 官方报告字段取数；去掉 `native_result_note="NASA GMAT 收敛解"` 的伪造标注。
4. **🟠 中**：将全部硬编码 UI 文本迁入 `I18n`，删除括号混搭（含词典自身 6 条混搭），补齐 RU 三语言，注册 `preset_/status_` 等前缀。
5. **🟠 中**：`PrincipiaInterop.InsertOrUpdateBurn` 实现 5.3 质心提前量；`TabMGA.GenerateGmatScriptForMGA` 改用 `FormatGmatSafeEpoch`。
6. **🟠 中**：`SolarSystemCatalog` 增加纯 Sol 白名单（拒绝 Kerbol 虚构天体），后备星历接入 `spice_bsp_path`。
7. **🟠 中**：动态参数目录接入 `ParamCapability` 能力模型，替换 5 名字白名单硬编码。
8. **🟡 低**：清理 `TWP` 残留文案、俯仰钳位下限、`e=0.95` 经验预设、`GUI.backgroundColor=Color.white`、单位元数据（C1 kN/N）对齐等。

---

*本报告为静态代码审查结论，未对任何源码文件进行修改。行号基于 2026-09-02 工作区快照。*
