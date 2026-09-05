# GUI 对 GMAT Script 操作全功能优化落地与验证总结 (Walkthrough)

## 1. 概述与达成目标
根据官方文档全集（`GMAT_Docs_Markdown` 01~07 系列）、`gmat_docs_distillation.md` 及 [`baseline_agent.md`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/baseline_agent.md) 的工程要求，对游戏内 GUI 对 GMAT Script 脚本的操作进行了全方位的深度重构与功能补全。

实现了以下 5 大核心模块：
1. **前置静态语法分析与自愈门禁 (Pre-flight Linter & Auto-Fix Gate)**
2. **官方 7 大标准脚本范式库原生注入器 (Canonical Blueprint Injector)**
3. **双栏视觉差异对比与合并视图 (Side-by-side Visual Diff & Merge Viewer)**
4. **无损往返 AST 同步与单步代码透视 (Lossless Round-Trip AST & Code Peek)**
5. **高级命令扩展与优化器支持 (Advanced Optimization & Flow Control Support)**

---

## 2. 变更详情与架构

### 2.1 模块 1：前置静态语法分析器与自愈门禁
- **新增文件**: [`src/KSPGMAT/Service/GMATScriptLinter.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptLinter.cs)
- **核心逻辑**:
  - `AnalyzeScript`: 静态词法语法扫描，检查非 ASCII 乱码、非纯净前缀（如 `Sat1.`）、大括号/分号闭合。
  - 结构块配对验证：`Target` / `EndTarget`、`BeginFiniteBurn` / `EndFiniteBurn`、`Optimize` / `EndOptimize`。
  - 基线逻辑校验：`Vary` 变量上界下界安全性检查、`Achieve` 容差检查与极值事件（`Apoapsis/Periapsis/Node`）非法打靶拦截、`Node` 赤道升交点语法告警（建议改用 `Latitude = 0.0`）。
  - `AutoFixScript`: 一键自愈修复已知语法缺陷。
- **UI 联动**:
  - 在 [`src/KSPGMAT/UI/GMATMainWindow.IDE.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.IDE.cs) 的 `TriggerPropagationSolve` 处建立强门禁，遇到阻塞错误拦截求解并弹出警告。
  - 在 [`src/KSPGMAT/UI/GMATMainWindow.TabResources.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabResources.cs) 顶部建立诊断徽章（红/黄/绿指示灯）、可折叠诊断列表、点击行号高亮定位与一键自愈按钮。

### 2.2 模块 2：官方 7 大标准脚本范式库注入器
- **新增文件**: [`src/KSPGMAT/Service/GMATBlueprintLibrary.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATBlueprintLibrary.cs)
- **集成范式**:
  1. `Simulate Orbit`: 基础开普勒轨道与积分器配置。
  2. `Hohmann Transfer`: 经典双冲转移打靶与远地点圆化。
  3. `Finite Burn`: 连续推力变轨与推力器/储罐动力学。
  4. `Mars B-Plane Flyby`: 深空飞掠火星 B 平面打靶 (`BdotT`, `BdotR`)。
  5. `Lunar Multiple Shooting`: 地月多打靶轨道拼接。
  6. `SEP Electric Propulsion`: 低推力电力推进螺旋变轨。
  7. `Report Pipeline`: 轨道根数与动力学状态报告输出管道。
- **SSOT 绑定**: 所有范式动态获取 `GMATStateManager.Current` 中的航天器名、中心天体与历元，完全避免硬编码。
- **UI 抽屉**: 在脚本工具栏增设 `[📚 范式蓝本 (Blueprints)]` 抽屉，支持“整本替换”与“在光标处插入”。

### 2.3 模块 3：双栏视觉差异对比与合并视图
- **新增文件**: [`src/KSPGMAT/Service/GMATDiffEngine.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATDiffEngine.cs)
- **核心算法**: 基于 Myers / LCS 逐行差异比对算法，将生成的基准脚本与用户在 IDE 中修改的缓冲区进行差异配对，并将相邻的增删合并为修改块。
- **UI 升级**: 彻底重写 `ideViewMode == 2`，实现锁相滚动、红/绿/黄增删改高亮底色与前缀符号（`+`、`-`、`~`）、差异统计与 `[应用变更至缓冲区]` / `[放弃修改还原基准]`。

### 2.4 模块 4：无损往返 AST 同步与单步代码透视
- **模型拓展**: [`src/KSPGMAT/GMATClient.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/GMATClient.cs) 的 `MissionStep` 添加 `custom_verbatim_script`、`show_code_peek`、`is_backwards` 字段。
- **单步代码透视**:
  - [`src/KSPGMAT/Service/GMATScriptGenerator.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/Service/GMATScriptGenerator.cs) 提供 `GenerateStepScript`。
  - [`src/KSPGMAT/UI/GMATMainWindow.TabSequence.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/UI/GMATMainWindow.TabSequence.cs) 的卡片右上角添加 `</>` 按钮，点击即可就地展开该步骤由 GMAT 生成器生成的原生语法代码，并附带语法高亮。
- **原生脚本块**: 序列末尾或卡片内部未被结构化解析的代码，自动收纳至 `CustomScript` 块并无损输出至求解脚本；GUI 提供卡片式文本编辑与快捷添加按钮。
- **反向传播**: 传播卡片提供 `[逆行反向传播 (Backwards)]` 开关，对应 GMAT 原生 `Propagate Backwards`。

### 2.5 模块 5：高级命令扩展与优化器规范支持
- **Yukon 官方优化器与按需声明修复**:
  - 彻底修正了非优化任务过度声明优化器的缺陷：仅当任务序列中包含 `Optimize`、`Minimize` 或 `NonlinearConstraint` 时才按需声明优化器。
  - 将非法的 `Create SQP` 修正为 GMAT 官方开箱即用支持的 SQP 优化器对象类型：**`Create Yukon Yukon1;`**（由 GMAT 内置的 `libYukonOptimizer` 插件提供，避免 Harwell 商业闭源 `VF13ad` 或未配置的 `fmincon` 缺失异常）。
  - 为 `Yukon` 配置合规的官方收敛容差属性：`FeasibilityTolerance`、`OptimalityTolerance`（替换不支持的通用 `Tolerance`）。
  - 在 `SanitizeGmatScript` 中加入自愈门禁：自动将历史遗留或手写的 `Create SQP <name>` 自愈替换为 `Create Yukon <name>`，并将 `<name>.Tolerance` 属性自愈转换为 `FeasibilityTolerance` 和 `OptimalityTolerance`。
  - `GMATScriptParser.cs` 增设对 `Create Yukon`、`Create FminconOptimizer` 及各官方优化器的完整双向解析支持。
  - `TabSequence.cs` 为 `Optimize`、`Minimize`、`NonlinearConstraint` 提供了专属参数调整卡片，并适配胶囊徽章与指示条主题色。

---

## 3. 验证与测试结果

### 3.1 编译验证
运行编译命令：
```powershell
dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release
```
**结果**:
```
已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:00.51
```

### 3.2 四语 i18n 完整性审计
运行检查脚本：
```powershell
python scripts/verify_i18n_health.py
```
**结果**:
```
Total keys in zh.json: 1855
ALL 4 LANGUAGES HAVE 100% PERFECT SYMMETRIC KEY COUNT (1728/1728)!
Tag nesting errors in zh.json: 0
Tag nesting errors in en.json: 0
Tag nesting errors in ru.json: 0
Tag nesting errors in jp.json: 0
Total placeholder mismatches across languages: 0
Total C# call site placeholder errors: 0

>>> ALL FORMATTING, TAG NESTING, AND I18N HEALTH CHECKS PASSED 100%! <<<
```
（包含中、英、俄、日四语 100% 对称补全）。

---

## 4. 遵守 baseline_agent 声明
我已遵守baseline：
- 严格践行 SSOT，所有状态通过 `GMATStateManager` 读写；
- 零托管计算，所有轨迹与优化交由原生引擎；
- 语法与脚本通过纯 ASCII、合法命名、参数能力位及安全容差闸门净化；
- 暗黑 UI 规范，四语 i18n 全量对称且 0 警告 0 错误编译通过。
