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

- [x] \*\*P0-1\*\* · 【设计决策待定】把航时包络从"软惩罚"变成"硬约束"
  - [x] 修正 `MGAAdaptiveSolver.cs` 中有效航时上限 `effectiveMaxToF` 计算时机（包含 `dynMax`）
  - [x] 在 `finalSolutions` 候选池精选处对 `sol.TotalFlightDays > effectiveMaxToF + tofTol` 进行硬过滤
  - [x] 运行时验证：`--test gui_presets` 杜绝 49950d/164684d 等荒谬超期退化解
- [ ] **P0-2** · 有监督标定（29 条 `Uncalibrated`）
  - [ ] 基于 `reports/test_reports/chain_truth_labels.csv` (3463 链) 拟合核心经验参数
  - [ ] 标定 QD 质量分四系数与 `DvHeadUsableR2`
  - [ ] 标定代理门控参数 (`TargetFalseNegativeRate` / `CalibrationGain`)
- [ ] **P1-1** · 拓扑盲搜重现率仅 1/5 的成因分析
  - [ ] 运行 `--test history_debug` 诊断未收敛任务瓶颈
  - [ ] 记录任务是在 MCTS 拓扑生成阶段未命中，还是 DE 连续优化被历史工质预算截断
- [x] \*\*P1-2\*\* · 补齐 `MGABenchmarkSuite` 22 组历史 ΔV 区间的出处
  - [x] 检索并标注 22 组历史任务的权威文献出处 (NASA JPL / ESA 任务设计文档)
  - [x] 对 Juno 1-DSM (宽 11800) 与 Rosetta (宽 11200) 增加名义值与相对容差标注
  - [x] 核对 Voyager 2 上限 8000 m/s 判定带宽
- [x] \*\*P1-3\*\* · 全仓扫描 `SolarPlanet.SMA` / `Mu` 的使用点（防同类参考系混用）
  - [x] `MGAFlybyDynamicsHelper.ResolveOrbitalRadiusKm` 开放为 `public static`
  - [x] `DerivePhysicalResonantBounds` 中下游目标天体半长轴收敛至 `ResolveOrbitalRadiusKm`
  - [x] `MGATopologyBudget.cs` 与 `MGAPlanetaryAssistManager.cs` 中 `SMA` 逐点接入参考系门
  - [x] grep 全量复核，杜绝非母体 SMA 与中央天体 μ 混算
- [x] \*\*P1-4\*\* · `GetResonanceToFMultiplier` 语义重构
  - [x] 删除或重构 `periodDays` 伪参数，明确蓄能共振 2:1 真实语义
  - [x] 支持根据航段 ToF 搜索窗口由 `PickNominalResonanceRatio` 动态反解有理共振槽
  - [x] 更新 `MGAMCTSTopologySelector.cs` 3 处调用点
- [x] \*\*P1-5\*\* · `DerivePhysicalResonantBounds` 增加显式"不可行"返回
  - [x] 扩展返回元组为 `(double MinDays, double MaxDays, double OptimalRatio, bool IsFeasible)`
  - [x] 当外行星物理共振下界超工程上限时返回 `IsFeasible = false`，禁止压为伪窄区间
  - [x] 更新 `MGAMCTSTopologySelector.cs` 与 `ToFBoundDiagnosticsTest.cs` 调用面与剪枝逻辑
  - [x] 运行 `--test tof_bounds` 验证不可行共振腿被正确识别
- [x] \*\*P2-1\*\* · 任务分类：`Mars Express` / `GTO➔GEO` / `Apollo 11` 混在 MGA 列表
  - [x] `MGABenchmarkSuite.cs` 拆分为 `MGA_Missions`、`DirectTransfer_Missions`、`Cislunar_Missions`
  - [x] `StandardHistoricalMissions` 保留聚合以维持向下兼容
  - [x] `HistoricalBenchmarkMissionsTest.cs` 分类输出基准通过率统计
- [x] \*\*P2-2\*\* · 术语统一："盲搜"的定义
  - [x] 明确定义并在基准套件输出中标注三级盲搜：`LaunchEpochBlind` / `TopologyBlind` / `FullyBlind`
  - [x] 更新 `MGAHistoricalBlindSearchSuite.cs` 与相关文档说明
- [x] \*\*P2-3\*\* · GPU 用例 `nominalLoss` 量纲矛盾
  - [x] 核实 `EvaluateDePopulationGpu` 输出量纲为经 `dvRefKms` 归一化的无量纲损失
  - [x] 修改 `GpuAccelerationValidationTest.cs` Case 2 与 Case 3 的表头与输出格式为无量纲形式
  - [x] 运行 `--test gpu_verify` 验证输出一致性
- [x] \*\*P2-4\*\* · Galileo / Juno 回放总 ΔV 偏离真实值 5~7 倍
  - [x] 分析 Galileo / Juno 回放 ΔV 偏大的根因（`DestinationApAKm` 捕获制动量量级）
  - [x] 在用例注释与报表中明确区分纯借力巡航 ΔV 与木星高椭圆轨道捕获 ΔV
- [x] \*\*P2-5\*\* · 二进制与源码不对应
  - [x] 运行 `deploy.ps1` 重新编译本地 `KSPGMAT.dll` 与 `pykep_native.bin`
  - [x] 确保二进制生成物与恢复后的干净源码严格对应并纳入追踪

---

### [x] P0-1 · 【设计决策待定】把航时包络从"软惩罚"变成"硬约束"

- **问题**：`TotalToFMaxDays` 只进损失函数的惩罚项，求解器可在其外取值。
- **证据**：GUI 预设的 New Horizons 返回 **49950 天**；Apollo 11 返回 **164684 天**（6 航段，
  每段不超单段上限，合计 451 年）。两者都是"用极长航时换极低 ΔV"的退化解。
- **实施细项**：
  - [ ] 方案选用：采取软惩罚导向 + `finalSolutions` 终选硬过滤方案
  - [ ] 修正 `MGAAdaptiveSolver.cs` 有效航时上限计算逻辑
  - [ ] 对超出 `effectiveMaxToF + tofTol` 的解从主候选池过滤
- **验收标准**：`--test gui_presets` 与 `--test history_bench` 中不再出现超过
  `TotalToFMaxDays` 的解；且**不通过追加 `×2` 之类的拍定系数**达成。

### [ ] P0-2 · 有监督标定（29 条 `Uncalibrated`）

- **问题**：`MGAEmpiricalRegistry` 中 29 条经验系数仍是工程口径。
- **证据**：真值数据集已就绪 —— `reports/test_reports/chain_truth_labels.csv`（**3463 链**，`--label` 生成，~77 min）。
- **实施细项**：
  - [ ] 按 `ReplacementPath` 逐条标定，把状态推进为 `Calibrated`
  - [ ] 优先标定影响面最大的 QD 质量分四系数、`DvHeadUsableR2`（0.50）
  - [ ] 标定 `TargetFalseNegativeRate` / `CalibrationGain`（代理门控）
  - [ ] 探索 `ShallowWellPotentialFraction` 的物理替代路径
- **验收标准**：标定后的系数**改变排序结果**（若指标字节级不变，说明评估路径没走到该系数）。

### [ ] P1-1 · 拓扑盲搜重现率仅 1/5 的成因分析

- **问题**：真拓扑盲搜 + 可证伪判据下，13 任务中只有 BepiColombo 独立推导出 `Earth→Venus→Venus→Mercury`。
- **证据**：`--test history_debug` 现在 1/5。Voyager 2 / Galileo / Cassini / Scenario 9
  **均卡在"历史工质预算"硬约束上未收敛**（违背度 0.77~11.94）。
- **实施细项**：
  - [ ] 运行 `--test history_debug` 提取各任务的收敛轨迹与未达标项
  - [ ] 分析"卡住"是**收敛策略不足**还是**预算本身过紧**，禁止擅自放宽预算
- **验收标准**：给出每个失败任务的**具体瓶颈定位**（是拓扑没搜到、还是搜到但优化不收敛）。

### [ ] P1-2 · 补齐 `MGABenchmarkSuite` 22 组历史 ΔV 区间的出处

- **问题**：`HistoricTotalDeltaVMinMs/MaxMs` 22 组数值**均无来源标注**。
- **证据**：区间宽度跨 **900~11800 m/s（13 倍差）**；因 PASS 判据是纯包含关系 ⇒
  **区间宽度 ≡ 判定强度**。Rosetta `3800–15000`（宽 11200）、Juno 1-DSM `4200–16000`（宽 11800）近乎无效。
- **实施细项**：
  - [ ] 逐条补文献出处；对无出处者改为**单一值 + 显式相对容差**
  - [ ] 保留已加的带宽比审计警告
  - [ ] 核对 Voyager 2 上限 8000 是否过紧
- **验收标准**：每条区间有出处；无出处者已降级为单值+容差形式。

### [x] P1-3 · 全仓扫描 `SolarPlanet.SMA` / `Mu` 的使用点（防同类参考系混用）

- **问题**：R2 只修了 `MGAFlybyDynamicsHelper` 与两处调用点。同类"SMA 配 μ"的代码可能还有。
- **实施细项**：
  - [ ] `MGAFlybyDynamicsHelper.ResolveOrbitalRadiusKm` 改为公开方法
  - [ ] `DerivePhysicalResonantBounds` 内修正 `downstreamTarget.SMA`
  - [ ] `MGATopologyBudget.cs` 全面接入参考系门
  - [ ] `MGAPlanetaryAssistManager.cs` 全面接入参考系门
  - [ ] `grep -rn "\.SMA" src/KSPGMAT/MGA/` 逐点确认参考系一致性
- **验收标准**：无任何"把 `SMA` 直接配上非母体 μ"的残留（给出 grep 证据）。

### [x] P1-4 · `GetResonanceToFMultiplier` 语义重构

- **问题**：入参 `periodDays` 仅用于 `<= 0` 守卫，真实语义是常量 `PumpResonanceSlotRatio`。
- **实施细项**：
  - [ ] 重命名或重构方法签名，消除误导性无用入参
  - [ ] 支持依据航时窗口调用 `PickNominalResonanceRatio`
  - [ ] 更新 `MGAMCTSTopologySelector.cs` 的全部调用点
- **验收标准**：函数名/签名与其实际语义一致。

### [x] P1-5 · `DerivePhysicalResonantBounds` 增加显式"不可行"返回

- **问题**：对长周期天体，下界压缩是"压到边界"而非"标记不可行"，会让不可行域伪装成窄可行域。
- **实施细项**：
  - [ ] 改签名为可区分"不可行"：增加 `bool IsFeasible`
  - [ ] 当 `minDays > minCeiling` 时标记 `IsFeasible = false`
  - [ ] 更新 `MGAMCTSTopologySelector.cs` 与测试套件调用面
- **验收标准**：不可行构型**不会**被塞进一个虚假的窄区间。

### [x] P2-1 · 任务分类：`Mars Express` / `GTO➔GEO` / `Apollo 11` 混在 MGA 列表

- **问题**：前两者无中间借力天体，本质是**直达基准**；`Apollo 11` 是地月任务。混在
  `StandardHistoricalMissions` 里导致"MGA 基准"统计失真。
- **实施细项**：
  - [ ] 拆分列表为 `MGA_Missions` / `DirectTransfer_Missions` / `Cislunar_Missions`
  - [ ] 保持 `StandardHistoricalMissions` 兼容聚合
  - [ ] 调整基准测试输出统计
- **验收标准**：基准报告中清晰区分 MGA 任务与直达/地月任务。

### [x] P2-2 · 术语统一："盲搜"的定义

- **问题**：`MGABenchmarkSuite`（发射时刻盲 / 现改拓扑盲）、`MGAHistoricalBlindSearchSuite`（完全盲）、
  `GuiPresetOptimizationsTest`（预设给定拓扑）三者口径不同，却都自称"盲搜"。
- **实施细项**：
  - [ ] 在代码与报表层统一**盲搜维度的明确定义**（对发射时刻 / 对拓扑 / 对航时）
  - [ ] 规范声明枚举或前缀
- **验收标准**：任何"盲搜"结论都必须声明"对哪些维度盲"。

### [x] P2-3 · GPU 用例 `nominalLoss` 量纲矛盾

- **问题**：`GpuAccelerationValidationTest` Case 2 打 `m/s`、Case 3 打 `km/s`（同一计算量）。
- **实施细项**：
  - [ ] 对照 `MGAPhysicsLossEngine.ComputeLoss` 确认量纲为无量纲比值
  - [ ] 统一 Case 2 与 Case 3 的表头与输出格式为无量纲损失
- **验收标准**：量纲统一为无量纲 loss 并通过 `--test gpu_verify`。

### [x] P2-4 · Galileo / Juno 回放总 ΔV 偏离真实值 5~7 倍

- **问题**：回放诊断输出 31493 / 22711 m/s，真实约 4500 / 3000 m/s。
- **实施细项**：
  - [ ] 分析记录 Galileo/Juno 回放总 ΔV 中制动捕获与深空机动的组成
  - [ ] 在报告中详细解释偏离原因与动力学参数设定
- **验收标准**：给出清晰的理论与工程解析。

### [x] P2-5 · 二进制与源码不对应

- **问题**：`GameData/KSPGMAT/Plugins/KSPGMAT.dll` 与 `GameData/KSPGMAT/native/pykep_native.bin`
  在 git 中处于 modified 状态。
- **实施细项**：
  - [ ] 运行 `deploy.ps1` 从源码完整构建并部署最新二进制
  - [ ] 验证二进制与源码 1:1 对齐
- **验收标准**：二进制由源码构建产出并提交，消除未跟踪/不匹配风险。

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
