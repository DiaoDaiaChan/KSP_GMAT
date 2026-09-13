# KSP_GMAT Agent 编码守则 (baseline_agent.md)

> **用法**：每次对话开始时，把本文件内容附给 coding agent，要求其编写/修改代码时**始终遵守**以下铁律。完成代码时重复：我已遵守baseline
> **全工程职责全景脑图**：请查阅 [`docs/CODEBASE_ARCHITECTURE_MINDMAP.md`](docs/CODEBASE_ARCHITECTURE_MINDMAP.md)，严格遵守 148 个文件的职责划分、Mermaid 拓扑流转与组件级契约。
# 每一步都应该commit介绍编写/修改了什么代码

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

## 3. 参数与能力注册表 (GMATSchemaRegistry & ParamCapability)
- 官方 310 个参数、323 个类（6355 个原生方法）全量通过 `GMATSchemaRegistry.Instance` 单一真值源动态读取与检索，**严禁本地硬编码静态参数字典**。
- 极值事件（`Apoapsis/Periapsis/Node/...`）判定**必须调用** `GMATSchemaRegistry.Instance.IsExtremumParameter(...)`，**仅用于 Propagate 停机条件，严禁用于 Achieve 连续求解**。
- Achieve 容差赋权**必须调用** `GMATSchemaRegistry.Instance.GetAuthoritativeTolerance(param, 0.1)` 动态获取官方权威推荐值。
- 航天器参数直属判定**必须调用** `GMATSchemaRegistry.Instance.IsSpacecraftDirectProperty(param)`，严禁本地写死 `craftDirect` 数组。
- 官方 API/类/函数查询与弹窗字典统一走 `GMATDynamicInspectorModal` 与 `SearchCapabilities`。

## 4. UI 国际化 (i18n)
- UI 文本**严禁硬编码**：静态 `I18n.T("key")`，动态 `I18n.Format("key",{0})`。
- 严禁 `中文 (English)` 括号混搭、严禁泄漏开发备注（如 `(Exact f6273ae)`）。
- key 用 snake_case + 规范前缀（`tab_/btn_/add_/type_/craft_/seq_/burn_/vector_/target_/ide_/res_/settings_/tip_/inspector_`）；词条为 `{ZH, EN, RU, JP}` 四语言对称数组。

## 5. UI 设计系统
- 暗黑令牌（`DarkBg #0a0c12` 等）；**禁止** `Color.white` 背景 / 蓝色聚焦亮斑。
- 胶囊徽章配色固定：Propagate 深红、FiniteBurn 与 ThrustVector 琥珀、Impulsive 紫、Vary 青、Achieve 绿、Stage 金、Target 蓝、EndTarget 玄灰。
- 模板折叠托盘：>6 个时最大高度 130px 平滑滚动。
- 统一使用 `GMATMainWindow.Styles.cs` 与 `GMATMainWindow.Widgets.cs` 里面的模板与高精度代码块渲染。

## 6. 物理时序 (simClock / 5.3)
- 滑行 `simClock += duration`；特征点过境 `trajectory.Find(p => p.t > simClock + 0.1)`；点火锁定当前 `simClock`。
- 质心提前量**统一调** `PrincipiaInterop.ComputeBurnCentroidLead(...)`（服务层），**严禁在 UI 层自算公式**。

## 7. 双通道历元
- 游戏内物理：`FormatUtcGregorian`（真实 UT，1:1 锁相）。
- GMAT 后端脚本输出：**一律** `FormatGmatSafeEpoch`（<1957 安全映射）。

## 8. 天体系统通用直读原则 (Planet Pack Universal In-Game Discovery)
- 严禁硬编码白名单过滤星球包。直接从 `FlightGlobals.Bodies` 动态读取游戏内物理天体参数与开普勒轨道根数，全量支持 RSS / OPM / JNSQ / Stock 及任意第三方星球包。离线独立测试环境自动回退标准太阳系离线字典。

## 9. 动力学力模型与天体通用解耦 (ForceModel Universal Decoupling)
- 积分器/求解器/大气模型清单以 `gmat_capabilities.json` 注册表为真值源，**严禁在 C# 写死清单**。
- 力模型天体**严禁写死 6 天体分支**，生成 ForceModel 时**必须**调用 `pCfg.GetActivePointMasses()` 动态格式化输出全量天体（支持 Saturn, Titan, Europa, Kerbin 等）。
- 脚本读取解析 ForceModel 时，**必须**将 PointMasses 全量保留至 `pCfg.customPointMasses`，天体名称统一经 `GMATSchemaRegistry.Instance.NormalizeCelestialName` 洗练。

## 10. 脚本生命周期与单步双向绑定 (Round-Trip & Two-Way Binding)
- 脚本生成或导出后**必须**通过 `GMATScriptLinter.FixScript` 进行门禁自愈（含纯 ASCII、机动前缀剥离、缺失容差自动补齐）。
- 单步代码透视与就地编辑（`ParseSingleStepScript`）必须接入 `GMATSchemaRegistry` 进行反解自愈与参数校验。
- MGA 脚本生成必须通过 `GetStandardSpicePlanets()` 动态输出 NAIFId，力模型必须动态聚合助推链全天体。

## 11. Python 任务步骤与统一补全系统 (CallPython & Unified IntelliSense)
- **统一真值源接入**：智能补全系统 (`QueryIntelliSense` / `GMATNamespaceResolver`) 必须全量打通 `GMATSchemaRegistry` 的 323 个官方 API 类与 222 个 Python 函数，严禁写死补全候选项。
- **Python 语法双向保真**：任务序列生成与反解必须无损支持赋值模式 (`[out] = Python.Mod.Func(in);`) 与显式命令模式 (`CallPythonFunction 'Lbl' Python.Mod.Func(in);`)。
- **门禁校验**：`GMATScriptLinter` 必须校验 Python 调用的括号闭合与语法结构；UI 卡片提供一键调阅原生源码实现与 4 大工程预设。

## 12. 维护守则
- 改完必须 `pwsh -File deploy.ps1` 且 **0 警告 0 错误**；每次编译部署自动触发 `tools/ComplianceChecker` (Roslyn AST) 编译器级合规自查，确保 **0 阻断性违规 (0 Critical Errors)**；新增字段/控件/词条同步登记基线文档。

## 13. 审计红线（禁止再犯）
- 严禁 C# 自算 ΔV/质量流/开普勒推导并冒充 GMAT 原生。
- 严禁绕过 `SanitizeGmatScript` / `FixScript` 推送脚本到后台求解。
- 严禁绕过 `GMATStateManager` 直读活动飞船（`FlightGlobals.ActiveVessel.GetTotalMass()` / `FindPartModulesImplementing<ModuleEngines>()`）。
- 严禁硬编码 `"Sat"` / `"LEOProp"` / `"RungeKutta89"` 等名称清单。
- 严禁在 C# 中硬编码极值事件列表（如 `ExtremaKeywords`）或航天器直属属性列表（如 `craftDirect`）。
- 严禁在 ForceModel 导出时写死仅判断 6 天体布尔变量。
- 严禁删除/禁用 PyKEP 原生路径后静默回退托管公式；原生不可用应明确报错。
- 严禁在 MGA 模块中散落业务状态变量；MGA 唯一真值源必须且只能为 `MGAStateManager`。
- 严禁在 MGA 算法或 UI 代码中硬编码天文单位 (AU)、太阳引力常数 (Mu) 等物理参数；必须全量引用 `MGADefaults`。
- 严禁在 Tab 视图类中直接编写底层通用 UI 控件逻辑；UI 控件一律集中由 `GMATMainWindow.Widgets.cs` 统一托管。

## 14. MGA 唯一真值源 (SSOT) 与 UI 控件托管守则
- **状态归口**：MGA 的任务配置 (`Config`)、任务序列 (`MissionSequence`)、方案池 (`Solutions`)、激活解 (`ActiveSolution`)、视口 (`Viewport`) 与模态框交互 (`UI`) 100% 托管于 `MGAStateManager.Current`。UI 表现层只允许声明委托属性，严禁私设业务状态字段。
- **控件归口**：MGA 所有通用控件（出发模式选择器、方案候选表、步骤卡片、数值延迟输入框）必须集中在 `GMATMainWindow.Widgets.cs` 中实现。`TabMGA.cs` 与 `TabMGASequence.cs` 只负责页面高层布局编排与事件分发。
- **物理常数注册**：所有天文单位 (`MGADefaults.AU_KM`)、引力参数 (`MGADefaults.FallbackSunMu` / `GetBodyMu`) 与安全高度阈值均严格调用 `MGADefaults`。
- **编译器门禁**：新增或修改 MGA 代码后必须通过 `tools/ComplianceChecker` (Roslyn AST)，保持 `MGA-HARDCODED-CONST` 与 `MGA-SCATTERED-UI-FIELD` 0 违规。

## 15. 统一日志系统准则 (Unified Logging System Standard)
- **禁止裸写原生日志**：严禁在任何业务代码中直接调用 `UnityEngine.Debug.Log` / `LogWarning` / `LogError` 或 `Console.WriteLine` / `Console.Write`。全工程代码一律接入统一日志系统 `KSPGMATLog`。
- **全通道自动同步**：`KSPGMATLog` 统一自动分发至三大输出通道：
  1. Unity/KSP 控制台与游戏主日志 `KSP.log`；
  2. 独立导出日志文件 `<GameData>/KSPGMAT/Logs/KSPGMAT.log`（启动自动轮转旧日志为 `KSPGMAT.previous.log`，加锁多线程安全，带启动 Session Banner）；
  3. 控制台 stdout/stderr（确保离线 CLI 与单元测试套件如 `MGADebugRunner` 无头输出）。
- **分级规范**：
  - `Trace` / `Debug`：高频迭代与算法求解中间状态。
  - `Info`：重要生命周期节点与业务收敛输出。
  - `Warn`：非致命异常、自愈保护与安全净空受罚。
  - `Error` / `Fatal`：致命故障，**必须传入 Exception 对象保留完整堆栈**（`KSPGMATLog.Error(tag, msg, ex)`）。
  - `Success` / `Section`：关键收敛与分段横幅标记。
- **上下文记录器推荐**：模块内推荐声明上下文记录器 `private static readonly ILogger Log = KSPGMATLog.ForContext("ModuleName");`，通过 `Log.Info(...)` / `Log.Warn(...)` 结构化输出。
- **UI 交互支持**：UI 设置面板或主界面底部提供一键“打开日志文件夹 (`OpenLogFileOrFolder`)”与“立即刷新刷盘 (`Flush`)”功能。
- **编译器级游离日志门禁**：`tools/ComplianceChecker` (Roslyn AST) 内置 `LogComplianceAnalyzer`，全量扫描拦截任何散落的 `UnityEngine.Debug.*`、`Console.Write*` 或 `MonoBehaviour.print`。违规标记为 `[ORPHAN-LOG]` 阻断性错误 (CRITICAL ERROR)，每次构建部署必须确保 0 违规。
