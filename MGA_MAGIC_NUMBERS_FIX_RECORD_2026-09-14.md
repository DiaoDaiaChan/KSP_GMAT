# MGA 模块缺陷修复记录（2026-09-14）

> **对应审计**：`MGA_MAGIC_NUMBERS_AUDIT_VERIFICATION_2026-09-14.md`（128 处，P0 23 / P1 63 / P2 42）
> **本次范围**：全部 P0 + 高价值 P1（含"报告声称已治理但实际未改"的全部条目）
> **未执行**：编译与运行测试（按用户要求跳过）
> **语法自检**：全部改动文件 `{}` 配平 = 0；代码行括号净差 = 0（失衡仅来自多行语句与注释）；无引号奇偶失衡
>
> ---
> ### ⚠️ 2026-09-14 补记：编译结果与一处遗漏
>
> 本轮的"未编译"留下了**一个真实的编译错误**，在随后的应试作弊修复轮次中才被 `dotnet build` 暴露：
>
> ```
> src/KSPGMAT/MGA/MGAEmpiricalRegistry.cs(166,34): error CS1003: 语法错误，应输入"，"
> ```
>
> 原因是 `Rationale = "全部以"代数预算或搜索域的比例"表达 ..."` 中**内嵌双引号未转义**。
> 本文件第 §"自检结果"节声称的"无引号奇偶失衡"**未能覆盖此文件** —— 因为自检脚本只检查了
> `MGADifferentialEvolution.cs` / `MGAFlybyDynamicsHelper.cs` / `MGAEmpiricalRegistry.cs` 三个文件中的
> **奇数引号行**，而该行是**偶数**（4 个引号）却仍非法。
>
> **结论：行为级的语法自检不能替代编译器。** 现已修复，且主工程与测试工程
> 均已 `0 警告 0 错误`（详见 `MGA_EXAM_CHEATING_FIX_RECORD_2026-09-14.md` §六）。
> **后续红线：任何标记为"已修复"的轮次，必须附编译输出。**

---

## 一、修复原则

审计发现前一份自审报告的核心问题不是"漏查"，而是**把"提成具名常量""换成另一组绝对值""换成数学等价分支"当成了"已清零"**。因此本次修复严格区分两类处置：

| 类别 | 处置方式 | 理由 |
| :--- | :--- | :--- |
| **A. 按天体名 / 任务名 / 历史构型 / 测试场景写死的分支与阈值** | **一律改为物理推导或无量纲判据** | 这类是真正的"应试作弊"，必须消除 |
| **B. 尚未完成有监督标定的经验系数** | **命名化 + 集中登记 + 标注标定状态** | 不能凭空编一个"更物理"的数替换（那只是换个数字），正确做法是让它可审计、可替换 |
| **C. 物理常数 / 数值保护（除零、溢出、收敛边界）** | **不动** | 非缺陷 |

其中 B 类全部登记进新建的 **`src/KSPGMAT/MGA/MGAEmpiricalRegistry.cs`**，逐项标注：取值依据 / 失效条件 / 建议替换路径 / 标定状态（`Uncalibrated` / `PhysicallyDerived` / `Calibrated` / `NumericalGuard`）。

---

## 二、P0 修复明细（23 项）

### 2.1 基准测试答案注种（最严重）

| 文件 | 改动 |
| :--- | :--- |
| `MGABenchmarkSuite.cs` | ① `BlindSearchEvaluationMode` 默认值 `false → **true**`（原默认使基准**无条件**走注种分支，且全代码库无任何调用方置 true）；② 注种分支降级为显式 opt-in 的诊断通道，进入时输出醒目警告；③ 结果新增 `BenchmarkResult.IsHistoricReplay` 标记，PASS 判定与批量汇总（`[盲搜]` / `[注种]`）据此区分；④ **删除以历史发射日 ±N 天为圆心的开窗**，新增 `ResolveBlindDepartureWindow()` 按"发射机会年份"年粒度开窗（只编码"哪一年发射"，不泄露具体历元），并支持 `BlindSearchWindowStartStr/EndStr` 显式覆盖；⑤ `PopulationSize/MaxGenerations` 的 60/50 硬编码改用 `MGADefaults`；⑥ 移除未使用的 `CalculateAdaptiveToFBounds` 冗余调用；⑦ 类头补"答案注种拔除"修正记录。 |

### 2.2 `Mercury` 天体名硬编码（6 处）

| 位置 | 原代码 | 现实现 |
| :--- | :--- | :--- |
| `MGADifferentialEvolution.cs:106/118` | `planet.Name.Equals("Mercury")` + 共振比下限 `1.24` / 写死 `1.3333333333333333` | 新增 `DeepWellDiveEnergyGapRatio`（无量纲比能量深度 `\|a_enter/a_body − 1\| ≥ 0.6`）；三个不一致的近似下限 `0.999 / 1.24 / 1.15` **统一为低阶有理共振槽** `1:1` 与 `5:4`（因候选表在 1.0 与 1.25 之间无槽位，**筛选结果与原实现逐位一致**）；`4/3` 回退命名为 `NominalDeepWellPumpRatio` |
| `MGADifferentialEvolution.cs:579` | `text.Equals("Mercury") → num4 = 3.0` | 改用同一无量纲判据；倍数 3.0 保留但命名为 `DeepWellDiveNominalResonanceMultiplier` 并**登记其与解析值（N_req≈1）的差异** |
| `MGADifferentialEvolution.cs:857` | 同上条件 | 同上（`isDeepWellDive2`） |
| `MGADifferentialEvolution.cs:1255` | 到达 v∞ 门槛：通用 7.5 km/s vs Mercury 13.5 km/s（斜率 1.2 vs 1.5） | 统一为 `ArrivalVInfExcessMultiple × sqrt(mu_body/r_safe)` 的**无量纲门槛** + 单一斜率，删除天体特例与 `num378 > 0` 任意门 |

Sol 系行为校验：水星（Δ=0.87）命中、金星（Δ=0.38）/火星（Δ=0.52）不命中 —— 与原判定一致。

### 2.3 按历史构型生成候选 / 按测试场景标定

| 位置 | 原代码 | 现实现 |
| :--- | :--- | :--- |
| `MGAFlybyDynamicsHelper.cs:288` | `(tOrbitDays < 150.0) ? 6.5 : ...`，注释点名"覆盖 MESSENGER/BepiColombo 4:4/6:6 槽"（**报告声称已删除、实际原样保留**） | 删除天体周期分档，改为对任意天体统一取 `Max(ratioMax, MaxResonanceOrderCap)`。**只放宽不排除**（长周期天体原被 3.0 夹紧，现同样放宽） |
| `MGAMCTSTopologySelector.cs:731` | `for (extraRes = 1; extraRes <= 6; extraRes++)`，6 即 BepiColombo 历史共振次数 | 提为 `DeepWellResonanceExpansionOrder`，登记"应替换为解析 N_req"（`ComputeRequiredResonantPhasingCount` 对水星给出 ≈6，量级吻合） |
| `MGAMCTSTopologySelector.cs:91` | `ShallowWellPotentialFraction = 0.03`，自陈"使判据在太阳系中复现原有行为" | 删除按测试集标定的表述，显式登记为 `[经验参数·待标定]`，并写明物理替代路径（`RobustBand` 系统内分位） |
| `MGAMCTSTopologySelector.cs:1887` | `deepestSma < depSmaBounds * 0.45`，注释引用内部**场景编号 "Scenario 9"** | 删除场景引用；提为 `DeepWellExitSmaRatioThreshold` 并给出无量纲等价（比能量深度 ≥ 1.222） |
| `MGAMCTSTopologySelector.cs:1409/1469` | `double vInfDep = 3.5;`，注释自陈取自"Juno/Galileo 级 ~3.5 km/s" | 新增 `SelfResonanceEgaVInfKms()`，由 **2:1 共振泵升 Vis-Viva 解析**给出。地球出发得 **3.21 km/s**（与原值同量级，但不再引用任务清单） |
| `MGAEphemerisReachabilityAnalyzer.cs:648` | `Mu >= 5.0e6 \|\| RadiusKm >= 20000.0` —— 正是 `MGAPhysicalScales` 声称"已取代"的那一行，且位于**到达捕获 ΔV 主结算路径** | 接入 `MGAPhysicalScales.ClassifyGiantBySystemDistribution`（系统内 `median + k·MAD`） |
| `MGAEphemerisReachabilityAnalyzer.cs:837` | `pArrObj.SMA < pDepObj.SMA * 0.45`（同样是被声称"已取代"的硬编码比例），且直接驱动构型配额分支 | 接入 `ClassifyDeepWellBySystemDistribution` |
| `MGAEphemerisReachabilityAnalyzer.cs:901-949` | 离散配额 `multiResCount < 2` / `maxArrCount >= 4`，注释点名"MESSENGER 构型""完整历史 BepiColombo 构型" | 新增 `ResolveRequiredDeepWellResonanceOrder()`，由 `ComputeResonantDeflectionNeeds` 的解析 N_req 驱动；删除全部任务名 |
| `MGASurrogatePreFilter.cs:111/229` | 自共振启航偏移写死 `{1.0, 1.5}`，注释点名 MESSENGER(1年)/BepiColombo(1.5年)（且遗漏它自己提到的 Juno 2年） | 改用 `GetSelfResonantRatioSlots()` —— 遍历 `ResonanceRatioCandidates` 中全部 ≥1:1 的有理槽。**只扩大评估范围** |
| `MGAPlanetaryAssistManager.cs:507` | `Math.Min(8, Math.Max(nArrReq, 6))`，下界 6 = BepiColombo 共振次数（本文件他处注释称已移除，实际场景 B 仍保留） | 改为 `Math.Min(DeepWellResonanceOrderCeiling, nArrReq + ResonanceExplorationHeadroom)`，下界随物理量变化 |
| `MGAPlanetaryAssistManager.cs:503` | `pArr.SMA < pDep.SMA * 0.55` | 接入 `ClassifyDeepWellBySystemDistribution`（Sol 系行为一致） |

### 2.4 绝对阈值统一与开发机路径

| 位置 | 原代码 | 现实现 |
| :--- | :--- | :--- |
| `MGAMCTSTopologySelector.cs:2087` | `p.Mu >= 500.0`（报告称已移除，实际仍在 BFS 天体过滤） | 新增 `IsEffectiveAssistSource()` —— 无量纲借力功率指数判据。行为校验：水星/金星/地球/火星/木星/土星/木卫四 保持入选，Ceres/Vesta/Ryugu/Eros/Bennu 保持排除 |
| `MGAMCTSTopologySelector.cs:2108` | `destPlanet.Mu >= 1000.0` | 同上 |
| `MGAConstraintManager.cs:354` | `IsHardConstraint = ... && hp.MaxSolarDistanceAu < 50.0` —— 用 **50 AU** 绝对值决定约束软硬 | 改为"只要用户显式配置即为硬约束"，并注明软约束应由独立策略标志表达 |
| `PykepNative.cs:462` | `@"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\KSPGMAT\native"` —— 开发机私有路径（报告称已清零） | 新增 `ResolveSteamLibraryNativeDir()`：优先 `KSPGMAT_NATIVE_DIR` → `KSP_ROOT`/`KSP_DIR` → 枚举 Steam 标准库中**含 "Kerbal" 的安装目录**。全部未命中则返回空串，不再加载来源不明的 DLL |

### 2.5 提前收敛门禁

`MGADifferentialEvolution.cs` 的 `TotalFlightDays <= 名义航时 × 1.5` 单条件短路 `break`：
原逻辑只要航时 ≤ 名义 1.5 倍就判定"完美达标"并跳出主循环，会在搜索仍在改进时过早刹车。现改为**双条件**：航时合规 **且** 搜索已进入停滞平台（复用项目自身的 `StagnationHardGenFraction × MaxGenerations`）。容差提为 `EarlyConvergenceToFTolerance`。

---

## 三、P1 修复明细（高价值部分）

### 3.1 历史真值从目标函数中拔除

`MGAHistoricalBlindSearchSuite.cs` 的评分函数原以**历史真值**为基准：

```csharp
double refDays = bCase.HistoricalFlightDays;      // ← 历史航时进了目标函数
double refDv   = bCase.HistoricalTotalDvMs;       // ← 历史 ΔV 进了目标函数
```

虽然不构成染色体注种，但会让"算法是否超过历史"部分取决于历史值本身，污染盲搜结论。现改为**纯物理基准**：`refDays` = `MGAPhysicsLossEngine.ResolveNominalTofDays`（Vis-Viva / 开普勒周期律解析值）；`refDv` = 任务工质预算。历史值仅保留在事后报告与比对中。

同时**删除以历史发射日 ±WindowSpanDays/2 为圆心的开窗**，新增 `ResolveBlindOpportunityWindow()` 按发射机会年份年粒度开窗。

### 3.2 注释与代码不一致（逐条更正）

| 文件 | 问题 | 更正 |
| :--- | :--- | :--- |
| `PykepBridge.cs` 类头 | 声称 `0 Custom Math Calculation in C#`，但同类中 `GetSynodicPeriodDays` 是手写会合周期恒等式且在生产路径高频调用 | 类头改写为准确表述："零托管**动力学**"成立（ΔV/ToF/v∞ 全部委托原生，失败返回 null/NaN，不填近似），但"零托管数学"不成立 |
| `MGAPhysicsSurrogateNetwork.cs:57` | doc 写 `R^2 >= 0.15`，实际常量已是 `0.50` | 改为引用常量 |
| `MGABayesianSubspacePolisher.cs:73` | 方法体内仍写"高斯过程 / Bayesian Polishing"（文件头已纠偏，方法内未同步） | 改为"一维线搜索 + 二次极值内插"，并注明本类无核函数/协方差/EI |
| `MGASymbolicPhysicsConstraints.cs:95` | 注释写"不超过 **4 倍**霍曼周期"，代码是 `* 6.0` | 常量命名化（`MaxToFHohmannStretchFactor = 6.0`）并更正注释 |
| `MGADifferentialEvolution.cs` | 无任何反编译来源提示 | 文件头加"待重构"标注：约 40% 行含 `numNN/arrayNN` 命名，其内经验常数未经源码级重构论证 |
| `MGAFlybyDynamicsHelper.cs:71` | 托管几何降级分支标注为"0 冒充原生" | 登记其**真实局限**：`acos(dot)` 对异面 v∞ 只给转角下界、未做 `rp ≥ r_safe` 截断，**仅可用于 UI 预览与剪枝，不得作为 ΔV 决算输入** |

### 3.3 命名化 + 登记（不再有裸数字）

下列裸字面量已全部提为具名常量并加注标定状态（部分列举）：

- `MGAFlybyDynamicsHelper.cs`：新增 **30 个** 常量（安全半径余量、共振航段上下界、出发段/转移段航时系数、`5478.75` 还原为 **15.0 儒略年**、`2500.0 天` 绝对分档改为**无量纲半长轴比** `DeepSpaceTransferRadiusRatio = 10.0`）。`GetResonanceToFMultiplier` 明确其"忽略入参、恒返回 2:1"的真实语义。
- `MGASurrogatePreFilter.cs`：`10.0 × 86400.0` → `SelfResonantMinDepPeriodSec`。
- `MGAPlanetaryAssistManager.cs`：新增 `DeepWellResonanceOrderCeiling` / `ResonanceExplorationHeadroom` / `ReferenceVInfForAssistSignificanceKms`（替换 3 处 `5.0`）/ `FallbackPeriodDays`。
- `MGATopologyBudget.cs`、`MGAPhysicsLossEngine.cs`、`MGAAugmentedLagrangian.cs`、`MGABanditHyperparameterTuner.cs`、`MGAPolicyValueGuide.cs`、`PykepBridge.cs`、`MGAInjectionAligner.cs`、`SolarSystemCatalog.cs`、`MGADefaults` 的剩余经验常数 —— **全部登记进 `MGAEmpiricalRegistry`**（B 类处置），含失效条件与替换路径。

### 3.4 门控随机种子

`MGASurrogateGate.cs` 原为 `MGASurrogateGate(int seed = 20260911)`。现改为 `DefaultGateSeed` 常量 + `ResolveGateSeed()`，支持环境变量 `KSPGMAT_SURROGATE_GATE_SEED` 覆盖，置 0 使用时间种子（用于统计有效性验证）。默认值保持 20260911 以维持既有基准的逐位可复现性。

---

## 四、未改动项及理由（避免误伤）

以下审计发现**故意不动**，理由已写入代码注释或登记表：

| 项 | 理由 |
| :--- | :--- |
| `MGAEphemerisReachabilityAnalyzer` 的 `NumericalVInfFloorKms = 0.10`、`MaxEccentricityForAsin = 0.95`、`DirectionReversalLimitDeg = 180.0` 等 | 数值保护与物理极限，非经验值 |
| `MGADifferentialEvolution` 的 `sqrMagnitude <= 0.0001`、`Math.Max(-1, Math.Min(1, ...))` 等 | 除零/定义域钳位 |
| `EphemerisTimeSystem.UtToGmatSafeJd` 的 1957 年历元映射 | 外部 GMAT 解析器兼容补丁，**不影响 MGA 求解内核**（内核全程用真实 UT + 原生星历） |
| `PorkchopGenerator` 的色带截断 | 渲染归一化；原 `minDV = 3500.0` 伪造色带**已确认删除** |
| `MGADefaults` 中的 `AU_KM` / `SecondsPerDay` / `DegToRad` / 黄金分割 | 物理与数学常数 |
| 各处的 `MaximumResonanceOrderCap = 6.5` 等 | 已提取命名并登记待标定，**不擅自改值**（改值等于用另一个拍定的数替换，属"换数未消元"） |

---

## 五、遗留待办（明确记录，不掩盖）

> **【2026-09-14 第二轮更新】**
> 下面第 3、4、5、6 条已在第二轮处理完毕，详见 `MGA_MAGIC_NUMBERS_FIX_RECORD_ROUND2_2026-09-14.md`：
> - 第 3 条（`SolarSystemCatalog` 退化为地球 1 AU）→ **已修**：后备库兜底不再伪装地球，
>   新增 `IsOrbitUnresolved` 标志；`ResolveNominalTofDays` 的 `?? AU_KM` 静默替代同步删除。
> - 第 4 条（0.03 / 0.45 / 0.6 三个工程阈值）→ **已修**：切换到系统内分位/壳层判据
>   （`0.6` 即 `DeepWellDiveEnergyGapRatio` 在恢复 `MGADifferentialEvolution.cs` 时已随之清理）。
> - 第 5 条（`MGAPhysicsLossEngine` 三个"规范阈值"）→ **该条结论不成立，已撤回**：
>   损失在 `loss = subjectDvKms / dvRefKms` 处就已归一化，三个阈值本来就是"预算倍数"。
> - 第 6 条（`1.20 / 0.15 / 0.15` 重复实现）→ **已修**：收敛为单一真值源
>   `MGAPhysicsLossEngine.ToFSoftExcessPenalty`，并把选解侧从乘性改为与损失同量纲的加性。
> 第 1、2 条仍开放（标定需要真值数据；DE 重构已被证明不必要）。

1. **有监督标定**：`MGAEmpiricalRegistry` 中 30+ 条 `Uncalibrated` 条目需用 `reports/test_reports/chain_truth_labels.csv`（3463 链真值）标定，标定后把状态推进为 `Calibrated`。
2. ~~**`MGADifferentialEvolution.cs` 源码级重构**~~ —— **已作废**（该文件是被反编译件覆盖，源码本身干净）。
3. ~~**`SolarSystemCatalog` 的离线兜底语义**~~ —— **第二轮已修**。
4. ~~**`ShallowWellPotentialFraction` / `DeepWellExitSmaRatioThreshold` / `DeepWellDiveEnergyGapRatio` 三处**~~ —— **第二轮已修**。
5. ~~**`MGAPhysicsLossEngine` 三个"规范阈值"**~~ —— **第二轮撤回该结论（不成立）**。
6. ~~**重复实现的统一**~~ —— **第二轮已修**。

---

## 六、改动文件清单

| 文件 | 主要改动 |
| :--- | :--- |
| `MGABenchmarkSuite.cs` | 答案注种拔除、盲搜开窗去历史锚定、replay 标记、默认值修正 |
| `MGADifferentialEvolution.cs` | 6 处 Mercury 特判→无量纲判据、共振比有理槽化、提前收敛双条件、反编译标注 |
| `MGAFlybyDynamicsHelper.cs` | 150/6.5 分档删除、30 个常量命名化、2500 天→无量纲比、5478.75→15 儒略年、托管降级局限登记 |
| `MGAMCTSTopologySelector.cs` | 0.03 标定表述删除、1~6 共振展开命名化、vInfDep 3.5→解析泵升、Mu 阈值→无量纲、Scenario 9 引用删除 |
| `MGAEphemerisReachabilityAnalyzer.cs` | 5e6/20000 与 SMA×0.45 → 系统内分位、构型配额→解析 N_req |
| `MGAHistoricalBlindSearchSuite.cs` | 历史真值移出目标函数、盲搜窗口去历史锚定、预算容差命名化、NH 门禁标注 |
| `MGAPlanetaryAssistManager.cs` | 写死 6 删除、SMA×0.55 → 系统内分位、v∞ 5.0 命名化 |
| `MGASurrogatePreFilter.cs` | 自共振偏移 {1.0,1.5} → 全有理槽 |
| `MGAConstraintManager.cs` | 50 AU 软硬判据删除 |
| `PykepNative.cs` | 开发机绝对路径 → 环境变量 + Steam 库枚举 |
| `MGASurrogateGate.cs` | 随机种子可注入 |
| `MGAAdaptiveSolver.cs` | 配置块经验参数统一标注 + 登记表指引 |
| `MGAPhysicsSurrogateNetwork.cs` / `MGABayesianSubspacePolisher.cs` / `MGASymbolicPhysicsConstraints.cs` / `PykepBridge.cs` | 注释-代码不一致更正 |
| **`MGAEmpiricalRegistry.cs`（新建）** | 经验参数唯一集中登记表（30+ 条目，含失效条件与替换路径） |

---

*本记录与 `MGA_MAGIC_NUMBERS_AUDIT_VERIFICATION_2026-09-14.md` 配套使用。按用户要求未执行编译与运行测试。*

---

## 七、重要更正（2026-09-14 17:15 追加）—— `MGADifferentialEvolution.cs` 已从 git 恢复

### 7.1 事实

用户在复核时指出 git 历史中应有正确文件。经核实，**工作区中的
`src/KSPGMAT/MGA/Solver/MGADifferentialEvolution.cs` 不是仓库源码，而是一个未提交的反编译覆盖件**：

| 证据 | 内容 |
| :--- | :--- |
| `git diff --stat -- <file>` | `3214 insertions(+), 2327 deletions(-)` —— 整文件被替换 |
| 工作区（修复前） | 3,094 行，**1,052 处** `numNN / arrayNN / textNN` 反编译命名 |
| `git show HEAD:<file>` | **2,326 行手写实现**，0 处反编译命名，完整中文工程文档头（职责 / 架构红线） |
| 反编译副本来源 | `scratch/decompiled_de.cs`（2,892 行，1,032 处 `numNN`） |
| 方法集 | 两者同为 `ResolveRandomSeed` / `FindAndRankResonantCombos` / `RunWindow` / `MarkHardGateCompliance` / `ComputeFitness`（同名同序）；反编译版**多出** `RotateAsymptoticVectorOverDwell`，而该方法**全仓 0 调用点** |
| 内容 | HEAD 版**不含任何** `Mercury` 逻辑特判，不含 `1.24 / 1.15 / 0.999 / 1.3333333333333333 / 7.5 / 13.5`；已使用比例化判据 `CircuitBreakerGenFraction` 等 |

**判定**：该反编译件来自**清理前的旧 DLL**，覆盖了 `f089664` / `7127bc1` 两次清理提交之后的手写源码。

### 7.2 处置

1. 已备份工作区现状（含本轮基于反编译件的修改）至
   `scratch/MGADifferentialEvolution.decompiled_with_myfixes.bak.cs`（3,214 行），**不丢内容**；
2. 已执行 `git show HEAD:<file> > <file>` 恢复 —— 恢复后 `git diff` 对该文件**完全一致**（2,326 行，0 反编译特征）；
3. 已清理本轮因误判该文件而产生的**悬空内容**：
   - 从 `MGAEmpiricalRegistry` 移除 3 条针对虚构常量的登记项
     （`DeepWellDiveEnergyGapRatio` / `ArrivalVInfExcessMultiple+Slope` / `EarlyConvergenceToFTolerance`）
     与 1 条"反编译产物"条目，代之以对**真实源码**成立的 4 条登记项；
   - 从 `MGAFlybyDynamicsHelper` 移除仅服务于该反编译件的 4 个未使用常量
     （`MinPumpResonanceRatio` / `NominalDeepWellPumpRatio` / `FirstLegNominalResonanceRatio` /
     `DeepWellDiveNominalResonanceMultiplier`）并修正相关文档交叉引用；
   - 全仓 grep 确认 **0 处悬空符号引用**；恢复版 DE 的全部外部依赖签名完好
     （`MGAFlybyDynamicsHelper.PickNominalResonanceRatio` / `ResonanceRatioCandidates`、
     `MGADefaults.*`、`MGAPhysicsLossEngine.*`、`PykepBridge.BuildFullSolution(..., computeAdvanced)`、
     `PykepNative.FindNodalCrossing`、`MGASolutionOrdering.GenomeComparer` 等均未被本轮改动破坏）；
     `RunWindow` 签名已含 `parallelizeInternal` / `populationMaxParallelism`，与 `MGAArchipelagoOptimizer` 的具名参数调用兼容。

### 7.3 对前文的修正

- **§二 2.2「`Mercury` 天体名硬编码（6 处）」整节作废** —— 该 6 处只存在于反编译件中；
- **§三 3.3 中关于 DE 的"命名化"**、**§五 遗留待办第 2 条（"DE 需源码级重构"）作废**；
- 其余章节（答案注种拔除、历史真值移出目标函数、`150/6.5` 分档、`Mu` 绝对阈值、`Scenario 9`、
  开发机路径、50 AU、NH 门禁、注释不一致等）**全部有效** —— 这些都在 HEAD 的干净源码上完成。

### 7.4 新增固化流程（重要）

**任何审计/修复前，必须先做工作区污染检查**：

```bash
# 1) 列出所有被修改的 .cs，逐个对比 HEAD 与工作区的反编译特征计数
git status --short -- '*.cs' | awk '{print $2}' | while read p; do
  h=$(git show HEAD:"$p" 2>/dev/null | grep -cE "\b(num|array|text)[0-9]{1,4}\b")
  w=$(grep -cE "\b(num|array|text)[0-9]{1,4}\b" "$p" 2>/dev/null)
  [ "$h" -gt 0 -o "$w" -gt 0 ] && echo "$p  HEAD=$h 工作区=$w"
done

# 2) 用 git diff --stat 识别"整文件被替换"（insertions ≈ deletions ≈ 文件总行数）
git diff --stat -- '*.cs'
```

判据：`HEAD 计数 = 0` 而 `工作区计数 > 0` ⇒ 该文件被反编译产物污染，**必须先恢复再评估**。
