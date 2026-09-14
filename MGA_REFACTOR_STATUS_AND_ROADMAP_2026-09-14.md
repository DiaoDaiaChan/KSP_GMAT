# MGA 模块 · 结论 / 重构进度 / 后续方向（汇总）

> **日期**：2026-09-14
> **性质**：本文是三轮审计与修复的**汇总索引 + 状态快照 + 路线图**，不重复细节。
> 细节见文末「附录 · 文档索引」中的四份原始记录。
> **当前编译状态**：`src/KSPGMAT/KSPGMAT.csproj` 与 `tests/MGADebugRunner/MGADebugRunner.csproj`
> 均 **0 警告 0 错误**（`dotnet build -c Release --no-restore`）。

---

## 一、结论

### 1.1 一句话结论

**原始 MGA 代码的问题不是"漏了几个魔法数字"，而是三个系统性缺陷：**

1. **考核系统本身被污染** —— 至少 4 个测试在结构上不可能失败；作弊 23 处中 **19 处在考核侧**。
2. **判据"看起来在检查，实际什么都检验不了"** —— 例如 `totalDv <= MaxExpectedDvMs * 1.35`
   因预算已被设成同一个 `MaxExpectedDvMs`，成为**恒真式**。
3. **物理边界推导结构错误** —— 航时上界用 `Math.Max(cap, …)` 组合（**用 Max 实现的上限不可能起约束**）、
   共振路径完全无上限、`SolarPlanet.SMA` 被跨参考系误用。

前一份自审报告（`MGA_CODE_AUDIT_EMPIRICAL_AND_BENCHMARK_OVERFITTING.md`，自称"46 项 100% 清零"）
**不成立**：独立核验重新定位 **128 处**（P0 23 / P1 63 / P2 42）。
其错法不是漏查，而是**把"提成具名常量""换成另一组绝对值""换成数学等价分支"记成了"已清零"**。

### 1.2 七条必须记住的事实

| # | 事实 | 证据 |
| :--- | :--- | :--- |
| 1 | **工作区不等于真值源** | `MGADifferentialEvolution.cs` 曾被发现是**未提交的反编译覆盖件**（3,094 行 / 1,052 处 `numNN`，`git diff --stat` = `3214+/2327-`，来源 `scratch/decompiled_de.cs`）。`git show HEAD:` 是 **2,326 行干净手写实现**。已恢复。基于它的全部结论作废。 |
| 2 | **作弊主要在考核侧** | 23 处中 19 处在 `tests/MGADebugRunner/`，仅 4 处在 `src/`。**只扫 `src/` 是重大遗漏。** |
| 3 | **4 个恒真测试** | `CassiniEvaluationTest.cs:21` `ctx.EndCase(true)`（无条件通过）；`Messenger`/`NewHorizons` 的 `Run` 里 `bool ok = true` + 丢弃返回值（**只有抛异常才算失败**）；`HistoricalMissionsDebuggerTest.cs:19` `passed > 0`（5 任务过 1 即 PASS）。 |
| 4 | **基准套件的"盲搜"是假的** | `MGABenchmarkSuite.cs:506` `FlybySequence = testCase.FlybySequence;` —— **两种模式**都把历史拓扑交给求解器（只对发射时刻盲、对拓扑不盲）。 |
| 5 | **预写成功结论** | `MGAHistoricalBlindSearchSuite.cs:893-897` 报表生成器**无条件**写入 3 条成功结论，其中"所有飞掠脉冲严格 0.00 m/s"与**紧邻上方表格**的 `⚠️ 需机动` 列自相矛盾。 |
| 6 | **航时包络是"软惩罚"而非硬约束** | `TotalToFMaxDays` 只进 `MGAPhysicsLossEngine` 的惩罚项。实测 GUI 的 New Horizons 返回 **49950 天**；Apollo 11 返回 **164684 天**。 |
| 7 | **参考系混用**（最深的物理缺陷） | `SolarPlanet.SMA` 是相对**该天体自身母体**的半长轴。实测 μ_Earth 配 Earth 的日心 1 AU + Moon 的地心 SMA ⇒ 地月航时上界 **53.8 年**。 |

### 1.3 原始代码的缺陷分类（本轮修复的靶子）

| 类别 | 含义 | 处置原则 |
| :--- | :--- | :--- |
| **A 类（作弊/应试）** | 按天体名 / 任务名 / 历史构型 / 测试场景写死的分支与阈值 | **一律改为物理推导或无量纲判据**（必须消除） |
| **B 类（经验值）** | 尚未标定的经验系数 | **命名化 + 登记进 `MGAEmpiricalRegistry`**，附失效条件与替换路径。**不擅自改值** —— 改值只是"换一个拍定的数"，正是前报告的错法 |
| **C 类（物理常数 / 数值保护）** | 除零、溢出、收敛边界、真实物理常数 | **不动** |
| **D 类（结构错误）** | 用 `Math.Max` 实现上限、参考系混用、派生字段泄漏答案 | **改结构，不加阈值** |
| **E 类（机制缺陷）** | 边界不进目标函数/硬门禁 ⇒ 实际不约束 | 需**产品决策**（是否允许超长航时换低 ΔV） |

### 1.4 修复三原则（后续沿用）

```
A 类 → 物理推导 / 无量纲判据（消元，不是换数）
B 类 → 登记 DTO，保留原值，标注标定状态与替换路径
C 类 → 不动
新增经验常数 → 必须命名 + 登记进 MGAEmpiricalRegistry，禁止裸字面量
```

---

## 二、当前重构进度

### 2.1 三轮工作概览

| 轮次 | 报告 | 范围 | 关键产出 |
| :--- | :--- | :--- | :--- |
| **R1** | `MGA_MAGIC_NUMBERS_FIX_RECORD_2026-09-14.md` | 魔法数字/经验值 P0 + 高价值 P1 | 拔除答案注种、清除天体名硬编码、统一无量纲判据、清除开发机绝对路径；新建 `MGAEmpiricalRegistry.cs` |
| **R1'** | `MGA_EXAM_CHEATING_FIX_RECORD_2026-09-14.md` | 应试作弊 25 处（审计报 23 + 新发现 2） | 消除全部恒真判定与预写结论、三态基准模式、新建 `EvalSupport.cs` 考核契约 |
| **R2** | `MGA_MAGIC_NUMBERS_FIX_RECORD_ROUND2_2026-09-14.md` | 上轮遗留 + 深挖结构缺陷 | 航时包络修复、参考系门、未识别天体不再伪装地球、软界单一真值源、绝对比例→系统内分位 |

### 2.2 已落地修复（按主题）

**① 考核系统（R1'，25 处）**
- 新建 `tests/MGADebugRunner/EvalSupport.cs`：`EvaluationOutcome`（Converged / AllLegsFeasible / TotalDvMs /
  BudgetMs / IsHistoricReplay / Passed）+ `EvalWindows`（`OpportunityYearWindow` 年粒度机会窗 /
  `TopologyContainsAll` 拓扑重现判据）。
- **铁律**：评测函数必须回传 `EvaluationOutcome`，由 `Run` 依显式阈值裁决；
  **评测路径内禁止 `EndCase(true)`**，禁止 `passed > 0` 之类"N 中过 1 即 PASS"。
- 5 个套件的 `launchUT ± N 天` 搜索域 → 年粒度机会窗。
- `MGAHistoricalBlindSearchSuite` 的 3 条预写成功结论 → 由实测数据生成。

**② 求解器（R1 + R2）**
- **答案注种与历史锚定拔除**：`MGABenchmarkSuite` 改为**三态显式模式**
  `enum MGABenchmarkMode { HistoricReplay, HistoricTopologyTrajectory /*默认*/, TopologyBlind }`，
  取代含糊的布尔量；盲搜模式 `FlybySequence` **严格为空**、先清源头字段再转换（防派生泄漏）。
- **天体名硬编码清零**：新增 `IsEffectiveAssistSource()`（取代 `Mu>=500` / `Mu>=1000`）、
  `SelfResonanceEgaVInfKms()`（2:1 泵升 Vis-Viva，地球出发 3.21 km/s，取代写死的 3.5）、
  `ResolveRequiredDeepWellResonanceOrder()`；共振比下限统一为低阶有理槽 `1:1 / 5:4 / 4:3`。
- **航时包络（R2 最高价值）**：统一为 `min(霍曼×放大, MaxTransferToFYears = 15 儒略年)`，
  共振路径补同一上限；**实测 316 段中 86 段 >20 年、最长 357.5 年 → 0 段超限、最长 15.0 年**。
- **参考系一致性门** `ResolveOrbitalRadiusKm`（行星→根恒星；卫星→`ParentName`；
  天体==中央天体→停泊轨道半径；不可解析即**拒绝**）。地月 53.8 年 → **15 天**；162 个跨参考系配对转为显式拒绝。
- **未识别天体不再伪装成地球**：`GetStandardPlanetFallback` 兜底返回全零 + `SolarPlanet.IsOrbitUnresolved`，
  删除 `p.SMA > 0 ? p.SMA : MGADefaults.AU_KM` 这类静默替代。
- **软界单一真值源**：`MGAPhysicsLossEngine.ToFSoftExcessPenalty`（`1.20 / 0.15 / 0.15`），
  选解侧由乘性改为与损失同量纲的加性；`LossReferenceCache` 增加自适应配置重载。
- **绝对比例 → 系统内分位**：`LowerQuantileLevel = 0.25`（深势阱目标）、
  `DeepWellExitShellSteps = 2`（反梯级深井，壳层比 `(SMA_max/SMA_min)^(1/(n-1))` 自导出）。
- 删除死代码 `MGAAdaptiveSolver.ApplySolutionToConfig`（0 调用点，"±10% 航时夹逼"的真实形态）。

**③ 原生桥接（R1）**
- `PykepNative.cs` 开发机私有 Steam 路径 → `ResolveSteamLibraryNativeDir()`
  （环境变量 → `KSP_ROOT`/`KSP_DIR` → 枚举含 "Kerbal" 的 Steam 库）。

### 2.3 验证状态

| 项 | 状态 |
| :--- | :--- |
| 主工程编译 | ✅ 0 警告 0 错误 |
| 测试工程编译 | ✅ 0 警告 0 错误 |
| **`--test tof_bounds`（R2 新增可证伪探针）** | ✅ **❌ → ✅**（世纪尺度航段 86 → 0，最长 357.5 年 → 15.0 年） |
| `--test gui_presets` | ❌ 5/8（失败形态改善：Cassini 30261d→17983d；NH 由纯时长退化改为"存在不可行航段"这一更诚实的失败理由） |
| `--test history_bench` | **9/16 → 11/16**（改善）；单次约 3.5 分钟 |
| `--test history_debug`（真拓扑盲搜） | ❌ **1/5** —— **修复后首次被如实测量**的能力指标 |
| `--test galileo` / `--test juno` | ✅ 通过（输出已带 `[回放·历史名义]` 前缀） |

**基准回归的关键变化（必须如实记录）：**

- **改善**：Cassini(1-DSM) ❌→✅（ΔV 7769→7050）；New Horizons ΔV 8350→7889、航时 20381→10408 d；Juno 航时 −28%。
- **边际回归**：**Voyager 2 ✅→❌**（7735 → 8188 m/s，+5.9%，超历史带上限 8000 约 2.4%）。
  原解是 **10242 天（28 年）**的"超长航时换低 ΔV"形态，收紧后不再可选；新解 6114 天更接近真实
  （Voyager 2 实际 ≈4380 天）。**视为把搜索域从非物理拉回物理的代价，不是缺陷。**

### 2.4 经验参数登记表（`MGAEmpiricalRegistry.cs`）

**共 42 条**，是"尚未消元"项的**唯一权威清单**：

| 状态 | 条数 | 含义 |
| :--- | :--- | :--- |
| `Uncalibrated` | **29** | 未标定的经验系数，附失效条件与替换路径 —— **下一步标定的主要对象** |
| `PhysicallyDerived` | 6 | 有物理推导出处（如 `MaxTransferToFYears`、`ResolveOrbitalRadiusKm`） |
| `Resolved` | 4 | 本轮已消元（如未识别天体兜底、软界重复实现） |
| `NumericalGuard` | 3 | 纯数值保护，非经验参数 |

### 2.5 改动范围

累计改动 **50+ 个 `.cs` 文件**（跨三轮，含重叠）。核心涉及：

```
src/KSPGMAT/MGA/Solver/   MGAAdaptiveSolver, MGAMCTSTopologySelector, MGAEphemerisReachabilityAnalyzer,
                          MGAPhysicsLossEngine, MGASurrogatePreFilter, MGAPlanetaryAssistManager,
                          MGAConstraintManager, MGAHistoricalBlindSearchSuite
src/KSPGMAT/MGA/          MGAFlybyDynamicsHelper, MGAPhysicalScales, MGABenchmarkSuite, MGASolutionOrdering,
                          SolarSystemCatalog, SolarSystemBodies, PykepBridge, PykepNative, MGAEmpiricalRegistry(新)
src/KSPGMAT/MGA/AI/       MGASurrogateGate, MGAPhysicsSurrogateNetwork, MGABayesianSubspacePolisher,
                          MGASymbolicPhysicsConstraints
tests/MGADebugRunner/     EvalSupport(新), ToFBoundDiagnosticsTest(新), Program,
                          Cassini/Messenger/NewHorizons/Galileo/Juno/HistoricalMissionsDebugger/
                          GuiPresetOptimizations/GpuAcceleration 各评测套件
```

---

## 三、接下来的修改方向

按优先级排列。**每条给出：问题 / 证据 / 建议动作 / 验收标准。**

### 任务跟踪清单 (Roadmap Checklist)

- [ ] **P0-1** · 【设计决策待定】把航时包络从"软惩罚"变成"硬约束"
- [ ] **P0-2** · 有监督标定（29 条 `Uncalibrated`）
- [ ] **P1-1** · 拓扑盲搜重现率仅 1/5 的成因分析
- [ ] **P1-2** · 补齐 `MGABenchmarkSuite` 22 组历史 ΔV 区间的出处
- [ ] **P1-3** · 全仓扫描 `SolarPlanet.SMA` / `Mu` 的使用点（防同类参考系混用）
- [ ] **P1-4** · `GetResonanceToFMultiplier` 语义重构
- [ ] **P1-5** · `DerivePhysicalResonantBounds` 增加显式"不可行"返回
- [ ] **P2-1** · 任务分类：`Mars Express` / `GTO➔GEO` / `Apollo 11` 混在 MGA 列表
- [ ] **P2-2** · 术语统一："盲搜"的定义
- [ ] **P2-3** · GPU 用例 `nominalLoss` 量纲矛盾
- [ ] **P2-4** · Galileo / Juno 回放总 ΔV 偏离真实值 5~7 倍
- [ ] **P2-5** · 二进制与源码不对应

---

### [ ] P0-1 · 【设计决策待定】把航时包络从"软惩罚"变成"硬约束"

- **问题**：`TotalToFMaxDays` 只进损失函数的惩罚项，求解器可在其外取值。
- **证据**：GUI 预设的 New Horizons 返回 **49950 天**；Apollo 11 返回 **164684 天**（6 航段，
  每段不超单段上限，合计 451 年）。两者都是"用极长航时换极低 ΔV"的退化解。
- **建议动作**（三条备选，需选一条）：
  1. `MGAConstraintManager` 中把 `MissionDuration` 提升为**硬门禁**（违反即判不可行）；
  2. 保留软惩罚，在 `MGASolutionOrdering` **最终选解处**过滤超界解；
  3. 保持现状，但把"超长航时解"显式标记并单列，不参与主排名。
- **验收标准**：`--test gui_presets` 与 `--test history_bench` 中不再出现超过
  `TotalToFMaxDays` 的解；且**不通过追加 `×2` 之类的拍定系数**达成。
- ⚠️ **这是产品取向问题**（是否允许为极低 ΔV 接受超长航时），**不要擅自替用户决定**。

### [ ] P0-2 · 有监督标定（29 条 `Uncalibrated`）

- **问题**：`MGAEmpiricalRegistry` 中 29 条经验系数仍是工程口径。
- **证据**：真值数据集已就绪 —— `reports/test_reports/chain_truth_labels.csv`（**3463 链**，`--label` 生成，~77 min）。
- **建议动作**：按 `ReplacementPath` 逐条标定，把状态推进为 `Calibrated`。
  优先标定**影响面最大**的几条：QD 质量分四系数、`DvHeadUsableR2`（0.50）、
  `TargetFalseNegativeRate` / `CalibrationGain`（代理门控）、`ShallowWellPotentialFraction` 的物理替代路径。
- **验收标准**：标定后的系数**改变排序结果**（若指标字节级不变，说明评估路径没走到该系数 —— 参见项目记忆的 ML 教训）。

### [ ] P1-1 · 拓扑盲搜重现率仅 1/5 的成因分析

- **问题**：真拓扑盲搜 + 可证伪判据下，13 任务中只有 BepiColombo 独立推导出 `Earth→Venus→Venus→Mercury`。
- **证据**：`--test history_debug` 现在 1/5。Voyager 2 / Galileo / Cassini / Scenario 9
  **均卡在"历史工质预算"硬约束上未收敛**（违背度 0.77~11.94）。
- **建议动作**：分析"卡住"是**收敛策略不足**还是**预算本身过紧**。
  **不要**通过放宽预算或判据来"解决"。
- **验收标准**：给出每个失败任务的**具体瓶颈定位**（是拓扑没搜到、还是搜到但优化不收敛）。

### [ ] P1-2 · 补齐 `MGABenchmarkSuite` 22 组历史 ΔV 区间的出处

- **问题**：`HistoricTotalDeltaVMinMs/MaxMs` 22 组数值**均无来源标注**。
- **证据**：区间宽度跨 **900~11800 m/s（13 倍差）**；因 PASS 判据是纯包含关系 ⇒
  **区间宽度 ≡ 判定强度**。Rosetta `3800–15000`（宽 11200）、Juno 1-DSM `4200–16000`（宽 11800）近乎无效。
- **建议动作**：逐条补文献出处；对无出处者改为**单一值 + 显式相对容差**，
  并保留已加的带宽比审计警告。**顺带核对 Voyager 2 上限 8000 是否过紧**（见 §2.3 边际回归）。
- **验收标准**：每条区间有出处；无出处者已降级为单值+容差形式。

### [ ] P1-3 · 全仓扫描 `SolarPlanet.SMA` / `Mu` 的使用点（防同类参考系混用）

- **问题**：R2 只修了 `MGAFlybyDynamicsHelper` 与两处调用点。同类"SMA 配 μ"的代码可能还有。
- **建议动作**：`grep -rn "\.SMA" src/KSPGMAT/MGA/` 逐点确认参考系一致性，
  全部收敛到 `ResolveOrbitalRadiusKm`。
- **验收标准**：无任何"把 `SMA` 直接配上非母体 μ"的残留（给出 grep 证据）。

### [ ] P1-4 · `GetResonanceToFMultiplier` 语义重构

- **问题**：入参 `periodDays` 仅用于 `<= 0` 守卫，真实语义是常量 `PumpResonanceSlotRatio`。
- **建议动作**：删除误导性入参，或改为真正按周期给出共振槽位（3 处调用面）。
- **验收标准**：函数名/签名与其实际语义一致。

### [ ] P1-5 · `DerivePhysicalResonantBounds` 增加显式"不可行"返回

- **问题**：对长周期天体，下界压缩是"压到边界"而非"标记不可行"，会让不可行域伪装成窄可行域。
- **建议动作**：改签名为可区分"不可行"，更新调用面。
- **验收标准**：不可行构型**不会**被塞进一个虚假的窄区间。

### [ ] P2-1 · 任务分类：`Mars Express` / `GTO➔GEO` / `Apollo 11` 混在 MGA 列表

- **问题**：前两者无中间借力天体，本质是**直达基准**；`Apollo 11` 是地月任务。混在
  `StandardHistoricalMissions` 里导致"MGA 基准"统计失真。
- **建议动作**：拆分列表为 `MGA_Missions` / `DirectTransfer_Missions` / `Cislunar_Missions`。

### [ ] P2-2 · 术语统一："盲搜"的定义

- **问题**：`MGABenchmarkSuite`（发射时刻盲 / 现改拓扑盲）、`MGAHistoricalBlindSearchSuite`（完全盲）、
  `GuiPresetOptimizationsTest`（预设给定拓扑）三者口径不同，却都自称"盲搜"。
- **建议动作**：在 README / 报表层统一**盲搜维度的明确定义**（对发射时刻 / 对拓扑 / 对航时）。
- **验收标准**：任何"盲搜"结论都必须声明"对哪些维度盲"。

### [ ] P2-3 · GPU 用例 `nominalLoss` 量纲矛盾

- **问题**：`GpuAccelerationValidationTest` Case 2 打 `m/s`、Case 3 打 `km/s`（同一计算量）。
- **建议动作**：对照 `MGAPhysicsLossEngine.ComputeLoss` 的真实量纲后统一。

### [ ] P2-4 · Galileo / Juno 回放总 ΔV 偏离真实值 5~7 倍

- **问题**：回放诊断输出 31493 / 22711 m/s，真实约 4500 / 3000 m/s。
- **可能原因**：`DsmEta` 固定 0.5、`computeAdvanced: false`、`DestinationApAKm` 设定导致捕获 ΔV 巨大。
- **性质**：**求解精度问题，不属于作弊**。建议单独立项。
- ⚠️ **`tests/MGADebugRunner/TestSuites/*.cs` 全部 untracked（从未提交）**，无法做反编译污染对比 ——
  建议尽早纳入版本控制。

### [ ] P2-5 · 二进制与源码不对应

- **问题**：`GameData/KSPGMAT/Plugins/KSPGMAT.dll` 与 `GameData/KSPGMAT/native/pykep_native.bin`
  在 git 中处于 modified 状态。**若反编译件是从该 DLL 反出来的，则当前 DLL 与恢复后的源码并不对应。**
- **建议动作**：确认这两个二进制的来源与构建时间，决定是否重新构建对齐。

---

## 四、红线（每次改代码前必读）

### 4.1 审计前置：工作区污染检查

```bash
git status --short -- '*.cs' | awk '{print $2}' | while read p; do
  h=$(git show HEAD:"$p" 2>/dev/null | grep -cE "\b(num|array|text|flag)[0-9]{1,4}\b")
  w=$(grep -cE "\b(num|array|text|flag)[0-9]{1,4}\b" "$p" 2>/dev/null)
  [ "$h" -eq 0 ] && [ "$w" -gt 0 ] && echo "⚠ 污染: $p HEAD=$h 工作区=$w"
done
git diff --stat -- '*.cs'   # insertions ≈ deletions ≈ 文件总行数 ⇒ 整文件被替换
```

判据：**`HEAD = 0` 而 `工作区 > 0` ⇒ 被反编译/生成物污染，必须先恢复再评估。**
已固化为 skill：`~/.workbuddy/skills/source-tree-integrity-check/SKILL.md`。

### 4.2 修复铁律

1. **必须编译**。R1 的"未编译"留下了 `CS1003`（未转义内嵌双引号）—— 语法自检查不出。
   **编译通过是"已完成"的底线。**
2. **把审计清单当"样例"，用模式扫全量**。审计点名 Cassini/Messenger/NewHorizons 的恒真判定，
   同族的 Galileo/Juno 完全同型却被漏掉（没做 `grep 'EndCase('` 全量扫描）。
3. **判据要做"可证伪性检查"**。不只看判据是什么，要看它**有没有可能失败**。
4. **注意"派生泄漏"**。`MGAConfig → MGAAdaptiveSolverConfig` 的 `FromMGAConfig` 会**派生**
   `LegToFBounds`。清源头字段必须在**转换之前**。
5. **判断边界是否生效，不能看有没有赋值，要看它是否进入目标函数/硬门禁**。
6. **报告铁律**：不得把"提成具名常量""换成另一组绝对值""换成数学等价分支"记为"已清零"。
   每条须附 (a) 非太阳系/非测试任务上的实测行为差异；(b) 新常数推导出处；
   (c) grep 证明同语义旧常数全树无残留。
7. **同一文件的多处修改逐次串行编辑**，每次编辑后立即 grep 复核（曾两次遇到"工具报成功但文件未变"）。
8. **写治理结论前先确认参考系与量纲**。R2 撤回了一条 R1 的错误结论
   （"三个规范阈值没有输入去乘 dvRef"不成立 —— `loss = subjectDvKms / dvRefKms` 早已归一化）。

---

## 附录 · 文档索引

| 文档 | 内容 |
| :--- | :--- |
| `MGA_MAGIC_NUMBERS_AUDIT_VERIFICATION_2026-09-14.md` | 128 处魔法数字/经验值的独立核验（含报告-代码不一致对照表） |
| `MGA_MAGIC_NUMBERS_FIX_RECORD_2026-09-14.md` | R1 修复记录（含 §七 关于 `MGADifferentialEvolution.cs` 反编译覆盖件的更正） |
| `MGA_MAGIC_NUMBERS_FIX_RECORD_ROUND2_2026-09-14.md` | R2 修复记录（航时包络、参考系门、软界单一真值源、撤回一条错误结论） |
| `MGA_EXAM_CHEATING_AUDIT_2026-09-14.md` | 应试作弊专项排查（23 处，S/A/B/C/D 分级） |
| `MGA_EXAM_CHEATING_FIX_RECORD_2026-09-14.md` | R1' 修复记录（25 处恒真判定与预注种消除、三态基准模式） |
| `MGA_CODE_AUDIT_EMPIRICAL_AND_BENCHMARK_OVERFITTING.md` | **前一份自审报告（已被核验否定，保留供对照）** |
| `src/KSPGMAT/MGA/MGAEmpiricalRegistry.cs` | 42 条经验参数/判据的**唯一权威登记表**（含状态、失效条件、替换路径） |
| `tests/MGADebugRunner/EvalSupport.cs` | 考核系统契约（`EvaluationOutcome` / `EvalWindows`） |
| `tests/MGADebugRunner/TestSuites/ToFBoundDiagnosticsTest.cs` | 航时边界可证伪探针（`--test tof_bounds`） |
| `.workbuddy/memory/MEMORY.md` | 项目长期记忆（构建/验证/架构红线） |

---

*本文档为状态快照，随修复推进更新。所有"已修复"条目均通过编译验证与至少一项运行时验证。*
