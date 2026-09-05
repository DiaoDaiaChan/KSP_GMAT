# KSP_GMAT Agent 编码守则 (baseline_agent.md)

> **用法**：每次对话开始时，把本文件内容附给 coding agent，要求其编写/修改代码时**始终遵守**以下铁律。完成代码时重复：我已遵守baseline

---

## 0. 三条最高铁律
1. **SSOT 单一真值源**：所有 GMAT 飞船状态/储罐/推力器/变量/净化逻辑，**只能**经 `GMATStateManager` 读写。严禁在 UI/IDE/子面板开私有副本，严禁绕过 SSOT 直接读 `FlightGlobals`。
2. **零托管计算**：动力学（兰伯特/双曲飞掠/MGA/猪排图/开普勒/ΔV）**必须**由 PyKEP / GMAT / Principia 原生完成。C# 层**严禁自编公式冒充原生结果**（如标 "NASA GMAT 收敛解"）。
3. **净化闸门**：脚本在导出/剪贴板/求解前**必须**过 `SanitizeGmatScript`（含 1.5 别名归一化），任何路径不得绕过。

## 1. 命名与模型
- 航天器名恒为 `"Sat"`：一律用 `GetSpacecraftName()` / `GetQualifiedSpacecraftProperty()` / `GetQualifiedThrusterProperty()`，**严禁硬编码**。
- 储罐 `Tank0/Tank1...`；`PressureModel=PressureRegulated`；`AllowNegativeFuelMass=true`；推力器 `DecrementMass=true`，`BoundTankId` 必须有效。
- 默认值：`DragArea=15`、`SRPArea=15`、`Cd=2.2`、`Cr=1.8`。

## 2. SanitizeGmatScript 四条
1. 纯 ASCII（过滤中文/Emoji）；
2. `\bSat\d+\.` → `Sat.` 前缀自愈；
3. Vary：`Upper<=Lower` 或 `Upper==0` 自动修正安全上界；`Perturbation<=0`→0.0001。Achieve：`Tolerance<=0` 按物理量赋容差（距离 0.5~1 km / 速度 0.01 km/s / 角度 0.01°）；
4. `X.Y.Node|AscendingNode|DescendingNode` → `X.Y.Latitude = 0.0`。

## 3. 参数与能力 (ParamCapability)
- 极值事件（`Apoapsis/Periapsis/Node/...`）**仅用于 Propagate**，严禁用于 Achieve。
- 参数名/单位用 GMAT 官方（`C1` 单位是 **N** 非 kN）；**禁止新增假参数**（如 `GravitationalAccel`）。
- 动态参数目录必须带 `Capability` 能力位；停止条件用官方 `ElapsedSecs/ElapsedDays`。

## 4. UI 国际化 (i18n)
- UI 文本**严禁硬编码**：静态 `I18n.T("key")`，动态 `I18n.Format("key",{0})`。
- 严禁 `中文 (English)` 括号混搭、严禁泄漏开发备注（如 `(Exact f6273ae)`）。
- key 用 snake_case + 规范前缀（`tab_/btn_/add_/type_/craft_/seq_/burn_/vector_/target_/ide_/res_/settings_/tip_`）；词条为 `{ZH, EN, RU}` 三语言数组。

## 5. UI 设计系统
- 暗黑令牌（`DarkBg #0a0c12` 等）；**禁止** `Color.white` 背景 / 蓝色聚焦亮斑。
- 胶囊徽章配色固定：Propagate 深红、FiniteBurn 与 ThrustVector 琥珀、Impulsive 紫、Vary 青、Achieve 绿、Stage 金、Target 蓝、EndTarget 玄灰。
- 模板折叠托盘：>6 个时最大高度 130px 平滑滚动。
- 统一使用GMATMainWindow.Styles.cs里面的模板

## 6. 物理时序 (simClock / 5.3)
- 滑行 `simClock += duration`；特征点过境 `trajectory.Find(p => p.t > simClock + 0.1)`；点火锁定当前 `simClock`。
- 质心提前量**统一调** `PrincipiaInterop.ComputeBurnCentroidLead(...)`（服务层），**严禁在 UI 层自算公式**。

## 7. 双通道历元
- 游戏内物理：`FormatUtcGregorian`（真实 UT，1:1 锁相）。
- GMAT 后端脚本输出：**一律** `FormatGmatSafeEpoch`（<1957 安全映射）。

## 8. 天体系统通用直读原则 (Planet Pack Universal In-Game Discovery)
- 严禁硬编码白名单过滤星球包。直接从 `FlightGlobals.Bodies` 动态读取游戏内物理天体参数与开普勒轨道根数，全量支持 RSS / OPM / JNSQ / Stock 及任意第三方星球包。离线独立测试环境自动回退标准太阳系离线字典。

## 9. 动力学清单
- 积分器/求解器/大气模型清单以 `gmat_capabilities.json` 注册表为真值源，**严禁在 C# 写死清单**（如 `LEOProp`/`RungeKutta89` 兜底要经注册表）。

## 10. 维护守则
- 改完必须 `dotnet build` 且 **0 警告 0 错误**；新增字段/控件/词条同步登记基线文档。

## 11. 审计红线（禁止再犯）
- 严禁 C# 自算 ΔV/质量流/开普勒推导并冒充 GMAT 原生。
- 严禁绕过 `SanitizeGmatScript` 推送脚本到后台求解。
- 严禁绕过 `GMATStateManager` 直读活动飞船（`FlightGlobals.ActiveVessel.GetTotalMass()` / `FindPartModulesImplementing<ModuleEngines>()`）。
- 严禁硬编码 `"Sat"` / `"LEOProp"` / `"RungeKutta89"` 等名称清单。
- 严禁删除/禁用 PyKEP 原生路径后静默回退托管公式；原生不可用应明确报错。
