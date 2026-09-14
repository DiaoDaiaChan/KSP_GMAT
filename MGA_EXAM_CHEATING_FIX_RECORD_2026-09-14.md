# MGA 应试作弊行为修复记录 (2026-09-14)

> 对应审计报告：`MGA_EXAM_CHEATING_AUDIT_2026-09-14.md`（23 处，其中 S 级 5 处）
> **实际修复 25 处** —— 修复过程中按"模式扫描"另发现审计遗漏的 2 处同类恒真判定
> （`GalileoEvaluationTest` / `JunoEvaluationTest`）。
> **编译验证：主工程 + 测试工程均 0 警告 0 错误**（`dotnet build ... -c Release --no-restore`）。
> **运行时验证**：`--test galileo` ✅ / `--test juno` ✅ / `--test history_bench` 9·16 /
> `--test history_debug` 1·5 / `--test gui_presets` 5·8（后三者的"不通过"是**修复的目的**，见 §6）。
> 前置：已按 `source-tree-integrity-check` 复核工作区，全部 `.cs` 与 HEAD 相比无反编译特征污染。

---

## 一、修复原则

审计报告的结论是：**考核系统本身的污染比求解器更重，且"求解器侧改得再干净，只要考核恒真，验证通过就没有任何信息量"**。
因此本轮只做一件事：**让每一个判定都具备可证伪性**，并把"答案回放"与"算法能力"在数据流与报表上彻底分开。

三类处置：

| 类别 | 含义 | 处置 |
| :--- | :--- | :--- |
| **假判定** | 判定恒真 / 只有抛异常才算失败 / N 中过 1 即 PASS | **改为结构化结果 + 显式阈值裁决**，判定必须可能为 false |
| **答案泄漏** | 把历史真值写进搜索域、目标函数、控制流或求解器输入 | **移出求解路径**，仅保留"事后比对"用途，并在输出中标注 `[回放]` |
| **预写结论** | 报表/文档中与实测无关的静态成功叙述 | **改为由实测数据聚合生成**，覆盖未收敛/需机动等不利情形 |

**没有做的事**：没有为了让测试"看起来好看"而放宽任何阈值；没有删除任何历史真值数据（它们作为*事后比对基准*是合法的）。

---

## 二、P0 修复（让考核恢复信息量）

### P0-1｜S1/S2/S3：消除三处恒真判定

**新增统一契构** `tests/MGADebugRunner/EvalSupport.cs`：

```csharp
public struct EvaluationOutcome {
    public bool   Converged;        // 是否收敛出解
    public bool   AllLegsFeasible;  // 是否全部航段物理闭环
    public double TotalDvMs;        // 考核总 ΔV
    public double BudgetMs;         // 工质预算 (<=0 表示未设, 不做超限判定)
    public bool   IsHistoricReplay; // 是否来自历史真值回放
    public string Summary;
    public bool WithinBudget => BudgetMs <= 0.0 || (Converged && TotalDvMs <= BudgetMs);
    public bool Passed => Converged && AllLegsFeasible && WithinBudget;
    public string ModeTag => IsHistoricReplay ? "[回放]" : "[盲搜]";
}
```

约定：**评测函数必须返回该结构，由 `Run` 依显式阈值裁决；评测路径内禁止再出现 `EndCase(true)`。**

| 文件 | 修复前 | 修复后 |
| :--- | :--- | :--- |
| `CassiniEvaluationTest.cs` | `EvalCassini(); ctx.EndCase(true); return true;` ← 返回值被丢弃，判定硬编码 true，**结构上不可能失败** | `var outcome = EvalCassini(); ... ctx.EndCase(outcome.Passed);` 判定 = 收敛 ∧ 闭环 ∧ 总ΔV ≤ 6200 m/s（Cassini 任务工质预算） |
| `MessengerEvaluationTest.cs` | `bool ok = true; try { EvalMessenger(); } catch { ok = false; }` ← ΔV/收敛/飞掠可行性**从不参与判定** | 同上；判定 = 收敛 ∧ 闭环 ∧ 总ΔV ≤ 6500 m/s |
| `NewHorizonsEvaluationTest.cs` | 同上 | 同上；判定 = 收敛 ∧ 闭环 ∧ 总ΔV ≤ 11000 m/s |

**附带发现并一并修复（审计遗漏）**：`GalileoEvaluationTest.cs` 与 `JunoEvaluationTest.cs` 有**完全相同**的恒真模式，审计报告未列出。
两者实为纯"历史真值回放"（用历史航段航时构造染色体直接求解，无寻优），已改为
`IsHistoricReplay = true`、判定 = "星历链路能否在历史真值点上给出物理闭环解"（可证伪），
并在报表中明确标注"该结果不作为寻优能力证据"。

### P0-2｜S4：消除预写成功结论

`MGAHistoricalBlindSearchSuite.GenerateMarkdownReport` §3 原为**三条无条件写入的成功断言**，且与紧邻上方的实测表格自相矛盾：

| 原预写断言 | 矛盾点 |
| :--- | :--- |
| "1. …自主推导出了 Voyager 2 的巨行星大巡游链路、Cassini 的内潜弹弓、以及 Mariner 10 的金星减速跳板，**证明了系统的通用天体力学真实性**" | 与 per-mission 循环无关；13 个任务全未收敛也照写 |
| "3. **100% 严苛的物理守恒**: 所有借力点飞掠脉冲**严格 0.00 m/s**" | 上方表格的 `物理审计` 列在 `FlybyDeltaVKms >= 0.001` 时打印 `⚠️ 需机动 {x} m/s` |

现改为**全部由 `results` 聚合生成**，并覆盖不利情形：

```csharp
var converged = results.Where(r => r.Success && r.BestSolution != null).ToList();
...
sb.AppendLine($"1. **收敛情况 (实测)**: {results.Count} 个任务中 **{converged.Count} 个**收敛出解。");
if (notConverged.Count > 0) sb.AppendLine($"   - 未收敛: {string.Join(", ", notConverged.Select(r => r.Case.MissionName))}");
sb.AppendLine($"3. **飞掠脉冲守恒 (实测)**: 共 {flybyLegTotal} 个飞掠航段, 严格 0.00 m/s 者 {flybyLegZeroPulse} 个"
    + (flybyLegNeedsBurn > 0 ? $", **需机动 {flybyLegNeedsBurn} 个** (最大 {worstFlybyPulseMs:F1} m/s, 见 `{worstFlybyLeg}`)。" : "。"));
```

同时 §「底层物理引擎」行的 `"零作弊，飞掠脉冲严格 0.00 m/s"` 也改为只描述引擎本身
（结果性描述交给 §3 的实测聚合）。

### P0-3｜S5：`passed > 0` → `passed == total`

`HistoricalMissionsDebuggerTest.cs`：`bool ok = passed > 0;`（5 个任务过 1 个即 PASS）
→ `bool ok = total > 0 && passed == total;`

### P0-4｜A1/A6：移除答案注入与预算/判据混用

`HistoricalMissionsDebuggerTest.cs` 原实现：

```csharp
hp.MaxVesselDeltaVMs = gt.MaxExpectedDvMs;                       // 预算 = 预期答案
var res = masterOpt.OptimizeMission(..., waypointIntentions: gt.ExpectedWaypoints.ToList(), ...);  // 供应链 = 答案
if (totalDv <= gt.MaxExpectedDvMs * 1.35) passed++;              // 判据 × 同一数值 ⇒ 恒真
```

三处问题同时存在，且横幅自称 **"0 针对特定任务的硬编码特判代码！"**。现改为：

1. 新增显式开关 `public static bool TopologyBlindSearch { get; set; } = true;`
   - `true`（默认）：`waypointIntentions` 传**空列表** ⇒ 拓扑完全由 MCTS + 星历可达性自主推导；
   - `false`：保留历史轨迹复现标定通道，但输出标注 `[回放]`，且明确"不参与能力判定"。
2. 发射窗由"从历史历元起算"改为 `EvalWindows.OpportunityYearWindow` 年粒度机会窗。
3. **判据改为可证伪的三合一**：收敛 ∧ 全航段物理闭环 ∧ **拓扑独立重现历史借力天体集合**。
   不再使用 `× 1.35`（该判据因预算取自同一数值而恒真）。
4. 历史数据（`ExpectedWaypoints` / `MaxExpectedDvMs` / 航时）明确只作**事后比对**用途。

新增判据辅助：

```csharp
// EvalSupport.cs → EvalWindows
public static bool TopologyContainsAll(List<string> actualTopology, IReadOnlyList<string> expectedWaypoints)
```

---

## 三、P1 修复（让搜索域不泄露答案）

### P1-1｜A2：`MGABenchmarkSuite` —— 从"含糊两态"改为"三态显式模式"

**这是本轮最重要的一条，也是最曲折的一条。**

#### 第一层问题：两种模式都把历史拓扑交给求解器

上一轮虽然把 `BlindSearchEvaluationMode` 默认改为 `true`、删除了发射日锚定，
但**两种模式都仍把历史拓扑交给求解器**：

```csharp
FlybySequence = testCase.FlybySequence,   // ← 历史借力天体序列
...
var optSols = MGAAdaptiveSolver.Optimize(config, ...);
```

⇒ 所谓"无先验盲搜"只对**发射时刻**盲、对**拓扑不盲**；而 `MGAHistoricalBlindSearchSuite`
的 `FlybySequence` 是严格为空的。两套件口径不同却都自称"纯盲搜"。

**第一版修复**：盲搜通道改为在**克隆体上先清空拓扑、再做 `FromMGAConfig` 转换**
（顺序很关键 —— 否则转换会用历史拓扑**派生** `LegToFBounds`，属"派生泄漏"），
并删除 `if (sol.TotalDeltaVKms*1000 > testCase.HistoricTotalDeltaVMaxMs)` 这一
"用历史 ΔV 决定要不要真搜"的控制流泄漏。

#### 第二层问题：实测发现原始设计无法同时承载两种诉求

改为纯盲搜后实跑 `--test history_bench`，发现两个事实：

1. **解选择不能取 `[0]`**：`Solve` 返回的候选解按**候选拓扑索引/窗口顺序**展开，不是按质量排序。
   取 `[0]` 会稳定选中"最先展开的那条"，实测 22 个任务**全部退化为单航段直达**
   （Cassini 直达 39688 m/s），基准失去 MGA 意义。已改为**在物理闭环的解中按解自身物理量择优**
   （主键总 ΔV → 次键总航时 → 末键航段数，全部由解自身导出，与线程调度/枚举顺序无关）。
2. **修正后仍只有 2 个候选解**（对日心行星际任务）：`Solve` 在"空 `FlybySequence` + 年粒度窗口"
   配置下对行星际目标只产出直达转移，**不做 MGA 拓扑搜索**（对比：地月目标 Apollo 11 得到 16 个候选、
   含 5 次飞掠）。因此 `MGABenchmarkSuite` 若只保留盲搜，就**丧失"给定历史拓扑下的轨迹优化"这一
   它本来唯一擅长的测量**，而拓扑发现在 `MGAHistoricalBlindSearchSuite` 里已经做得更好。

#### 最终设计：三态显式模式

新增语义互斥的三态枚举，取代含糊的布尔量：

```csharp
public enum MGABenchmarkMode {
    HistoricReplay = 0,             // 注种回放：历史发射日+航段航时+拓扑全注入，仅星历链路诊断
    HistoricTopologyTrajectory = 1, // 默认：拓扑按任务定义给定，但发射窗/航时边界全部物理推导
    TopologyBlind = 2               // 真盲搜：不给任何中间借力天体
}
public MGABenchmarkMode SearchMode { get; set; } = MGABenchmarkMode.HistoricTopologyTrajectory;
```

- **默认 = `HistoricTopologyTrajectory`，并在类注释、报表首行、汇总模式分布三处显式写明"非盲搜"。**
  这才是"历史任务基准"的本义：给出构型、给出物理搜索域，测能否把轨迹优化到历史量级。
  它**不是**作弊 —— 作弊是把历史值注入**搜索域**并谎称盲搜。
- `TopologyBlind` 保留为独立选项，与 `MGAHistoricalBlindSearchSuite` 同口径。
- 三种模式的发射窗一律走 `ResolveBlindDepartureWindow`（年粒度机会窗），
  航时边界一律走 `AutoDeriveResonantLegBounds`（物理推导）。

**顺带修正的一处真实 bug**：`AutoDeriveResonantLegBounds` 的 `flybyChain` 参数语义是
**中间借力天体**（不含出发与终点），而 `BenchmarkCase.FlybySequence` 是**含终点**的。
上一轮的调用 `AutoDeriveResonantLegBounds(dep, FlybySeq.Last(), FlybySeq, ...)` 会导致
终点被当成飞掠体、**多出一条航段**。现改为传入 `FlybySequence` 去掉末元素，并加注释固化语义。


### P1-2｜C 级：评测套件搜索域去历史日锚定

统一改用 `EvalWindows.OpportunityYearWindow(anchorUT, out start, out end)`
（`[该年 1 月 1 日, 次年 1 月 1 日)`，只编码"哪一年发射"这一任务级公开先验）：

| 文件 | 修复前 | 修复后 |
| :--- | :--- | :--- |
| `CassiniEvaluationTest` | `launchUT ± 10 天`（3 处：cfg 窗、精修上下界、DE RunWindow） | 年粒度机会窗 |
| `MessengerEvaluationTest` | `launchUT ± 60 天`（2 处）+ 4.3 敏感度扫描锚点 `launchUT + lOff` | 年粒度机会窗；敏感度扫描锚点改为**机会窗中点** |
| `NewHorizonsEvaluationTest` | `launchUT ± 60 天` | 年粒度机会窗 |
| `GuiPresetOptimizationsTest` | `launchUT ± 20 天` | 年粒度机会窗（回退路径改用与历史无关的 J2000 起点年窗） |
| `HistoricalMissionsDebuggerTest` | 窗口**从历史历元起算**（Galileo/Cassini/BepiColombo 的字符串就是真实发射日），最优解落在窗口起点 t=0 | 年粒度机会窗 |

**保持不动（有物理依据，非锚定）**：`double tVenus1 = launchUT + leg0Tof;` 属"发射历元 + 航段航时"的正常传播，不是搜索域。

### P1-3｜C 级：`GuiPresetOptimizationsTest` 的 ±10% 航时夹逼

```csharp
// 原: 历史真值 ±10%
mgaConfig.MinToFDays = bCase.HistoricToFDays.Select(d => Math.Max(10.0, d * 0.9)).ToList();
mgaConfig.MaxToFDays = bCase.HistoricToFDays.Select(d => d * 1.1).ToList();

// 现: 开普勒/共振物理推导, 与任何历史航时无关
var intermediates = bCase.FlybySequence.Take(Math.Max(0, bCase.FlybySequence.Count - 1)).ToList();
var legBounds = MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(
    bCase.DepartureBody, bCase.FlybySequence.Last(), intermediates, "Sun");
```

同时口径澄清：本套件模拟的是"用户在 GUI 选中预设后点 [优化求解]"的**真实产品路径**，
预设本身含借力链是正常输入，**不是拓扑盲搜测试**，不应被当作盲搜证据引用 —— 已在类注释与
`Description` 中显式声明。

### P1-4｜A3/A4/A5：`nominalGenome` 回放标注与隔离

`CassiniEvaluationTest`、`GpuAccelerationValidationTest` 中的历史真值染色体：

- 输出标签统一加 `[回放·历史真值单点]` / `[回放·Hooke-Jeeves 局部精修]` / `[回放基线]`；
- 增加说明：**"它不是求解器的输出，只用于验证星历 + 开普勒链路在已知真值点上能否复现历史量级的 ΔV"**；
- `GpuAccelerationValidationTest` 中 `pop.Add(nominalGene.Clone())` 的用途澄清为
  **"数值鲁棒性探针的物理已知可行参照点"**（检验 GPU 不误报 100000 奇点罚项），
  表头列名由"标称共振Loss"改为"`[回放基线]Loss`"，避免被误读为"GPU 求解出了这个解"。

---

## 四、P2 修复

### P2-1｜B2：历史 ΔV 判定带宽审计

`MGABenchmarkSuite.BenchmarkCase` 新增三个字段：

```csharp
public double HistoricTotalDeltaVMs { get; set; }              // 单一历史参考值 (>0 时优先)
public double MaxAcceptableDvRelTolerance { get; set; } = 0.35; // 工程口径, 已登记
public string HistoricTotalDeltaVProvenance { get; set; }       // 出处
```

判定逻辑改为：优先 `单值 ± 相对容差` 构造带宽；并**输出带宽审计**：

```
• 判定带宽比 (Hi-Lo)/Lo = {bandWidthRatio*100}% | 出处: {Provenance 或 ⚠ 未登记}
• ⚠ 警告: 判定带宽比 > 60%，该区间过宽，其 PASS 的信息量有限。
  建议为该任务补填 HistoricTotalDeltaVMs (单一历史参考值) 以收紧判定。
```

原判据为纯区间包含 `∈[Min, Max]`，等价于**区间宽度 ≡ 判定强度**；实测宽度跨 900~11800 m/s（13 倍）：
Rosetta 3800–15000（宽 11200）、Juno 1-DSM 4200–16000（宽 11800）、BepiColombo 3500–12000（宽 8500）。
本轮**没有编造更"准"的历史数值**（那只是换一组拍定的数），而是把"区间过宽导致判定无力"这一事实
变成报表里的显式警告，并把收紧路径（补填单值）写明。

### P2-2｜D1：删除 `ApplySolutionToConfig` 死代码

`MGAAdaptiveSolver.cs` 中该方法把**已收敛解**回写为下一轮搜索边界：

```csharp
config.EarliestDepartureUT = solution.DepartureUT;
config.LatestDepartureUT   = solution.DepartureUT + 86400.0;
config.MinToFDays = solution.Legs.Select(l => Math.Max(5.0, l.ToFDays * 0.9)).ToList();
config.MaxToFDays = solution.Legs.Select(l => l.ToFDays * 1.1).ToList();
```

这正是历史审计声称"已清除"的**航时 ±10% 夹逼**的实际形态。全仓（`src/` + `tests/`）**0 个调用点**。
已**删除**，并在原地留下说明块：若后续确有"从已得解热启动"的需求，必须新建**显式命名**的 API
（如 `WarmStartFromSolution(...)`）并登记其使用条件与风险。

---

## 五、顺手修复的编译缺陷

`src/KSPGMAT/MGA/MGAEmpiricalRegistry.cs:166` 存在**未转义的内嵌双引号**，
导致 `CS1003: 语法错误，应输入","` —— 上一轮引入、未编译所以未暴露：

```csharp
// 原 (无法编译):
Rationale = "全部以"代数预算或搜索域的比例"表达 ...",
// 现:
Rationale = "全部以「代数预算或搜索域的比例」表达 ...",
```

已对全文件做引号奇偶自检（非注释行 0 处失衡）。

---

## 六、验证结果

```
dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release --no-restore
  → KSPGMAT -> bin/Release/net48/KSPGMAT.dll    0 个警告  0 个错误

dotnet build tests/MGADebugRunner/MGADebugRunner.csproj -c Release --no-restore
  → MGADebugRunner -> bin/Release/net48/MGADebugRunner.exe    0 个警告  0 个错误
```

**残留模式扫描（全仓）**：

| 检查项 | 结果 |
| :--- | :--- |
| `ctx.EndCase(true)` | 仅存在于说明性注释中，代码内 0 处 |
| `passed > 0` 判定 | 仅存在于注释中，代码内 0 处 |
| `waypointIntentions` 传入历史 waypoints | 仅剩 `Scenario9ChallengeTest.cs:54`（见 §七 遗留） |
| `launchUT ± N 天` 搜索域 | 已全部消除（仅余物理传播项 `launchUT + leg0Tof`） |
| `MaxExpectedDvMs * 1.35` 恒真判据 | 已删除，仅存在于注释中 |
| `BlindSearchEvaluationMode` 布尔开关 | 已删除，被 `MGABenchmarkMode` 三态枚举取代 |
| `ApplySolutionToConfig`（±10% 航时夹逼） | 已删除，仅留说明块 |
| 编译状态 | 主工程 + 测试工程均 **0 警告 0 错误** |

### 6.0 基准套件（`--test history_bench`）：三态模式实跑结果

默认 `HistoricTopologyTrajectory` 模式下，择优解**确实是带引力助推的 MGA 解**，基准恢复 MGA 意义：

| 用例 | 候选解 / 物理闭环 | 择优解 | ΔV (m/s) | 历史带 | 判定 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Mariner 10 | 6 / 6 | 3 航段 (2 次飞掠) | 4206 | 3800–5300 | ✅ |
| Galileo | 16 / 16 | 4 航段 (3 次飞掠) | 6409 | 3900–6200 | ❌ 略超 |
| Cassini-Huygens | 16 / 14 | 7 航段 (6 次飞掠) | 7769 | 3900–6200 | ❌ |
| Voyager 2 | 14 / 8 | 4 航段 (3 次飞掠) | 7735 | 6900–8000 | ✅ |
| Rosetta | 8 / 8 | 2 航段 (1 次飞掠) | 7099 | 3800–15000 | ✅ |
| New Horizons | 15 / 15 | 3 航段 (2 次飞掠) | 8350 | 7500–9200 | ✅ |
| Juno ×2 | 2 / 2 | 3 航段 (2 次飞掠) | 8422 | 3000–8600 / 4200–16000 | ✅ ✅ |
| BepiColombo | 6 / 6 | 4 航段 (3 次飞掠) | 3744 | 3500–12000 | ✅ |
| Apollo 11 | 16 / 16 | 6 航段 (5 次飞掠) | 749 | 3600–4500 | ❌ 退化解 |
| Mars Express / GTO→GEO | 2 / 2 | 1 航段 (无飞掠) | 3616 / 3804 | — | ✅（本质是直达基准） |

（`StandardHistoricalMissions` 共 22 条，此处摘录前 16 条。）

**结论**：✅ 拓扑不再是给定的答案，但也确实按模式定义提供了构型；**ΔV 判定重新具备信息量**
（9/16 通过，Galileo/Cassini 略超历史带这类信号是真的）。`航段=1` 的候选会被显式标注
"未使用引力助推"，不会伪装成 MGA 结果。

---

### 6.1 冒烟结果概览

| 用例 | 结果 | 说明 |
| :--- | :--- | :--- |
| `--test galileo` | ✅ 通过 | 输出已带 `[回放·历史名义]` 前缀 |
| `--test juno` | ✅ 通过 | 输出已带 `[回放·历史名义 796d, 999d]` 前缀 |
| `--test history_bench` | ❌ 9/16 | 见 §6.0 |
| `--test history_debug` | ❌ 1/5 | **这正是修复的目的** —— 见 §6.2 |
| `--test gui_presets` | ❌ 5/8 | 见 §6.3 |

### 6.2 新暴露的真实能力缺口（拓扑盲搜 1/5）

`--test historical_missions_debugger` 在改为**真拓扑盲搜**（不传任何中间借力天体）+ **可证伪判据**
（收敛 ∧ 物理闭环 ∧ 独立重现历史借力天体）后，实测：

| 任务 | 算法自主推导拓扑 | 结果 |
| :--- | :--- | :--- |
| Voyager 2 (E→N) | — | ⚠️ 未收敛（工质预算硬约束，归一化违背度 11.94） |
| Galileo (E→J) | — | ⚠️ 未收敛（违背度 0.77） |
| Cassini (E→S) | — | ⚠️ 未收敛（单脉冲 DSM 上限违背度 1.07） |
| **BepiColombo (E→Mercury)** | `Earth ➔ Venus ➔ Venus ➔ Mercury` | ✅ **通过**（预算占比 83.4%，独立重现 Venus） |
| Scenario 9 (E→N) | — | ⚠️ 未收敛（违背度 4.41） |

**这就是本次修复最重要的产出。** 修复前同一测试会因 `passed > 0` 输出 **PASS**
（1 个任务成功即整体通过），把"拓扑盲搜重现率 1/5"这一事实完全掩盖。
现在它被如实暴露：**在真正无拓扑先验 + 历史工质预算的条件下，算法能独立重现历史 MGA 构型的比例是 1/5。**

> 注意：4 个失败发生在**收敛门**（工质预算硬约束）而非"拓扑不相符"门。
> 即算法没能在历史 ΔV 预算内收敛出可行解 —— 这与上一轮审计"盲搜平均偏差 +932 m/s"的结论方向一致。

### 6.3 新暴露的退化解问题（GUI 预设 3/8 不通过）

`--test gui_presets` 在把 `HistoricToFDays × 0.9/1.1` 换成 `AutoDeriveResonantLegBounds` 之后，
暴露出原来被"历史航时 ±10% 夹逼"掩盖的问题：**求解器会返回世纪尺度的退化解**。

| 预设 | 物理航时包络（各段上限之和） | 实际解航时 | 判定 |
| :--- | :--- | :--- | :--- |
| Mariner 10 | 484 d | 343 d | ✅ |
| **Cassini-Huygens** | 18549 d | **30261 d** | ❌ 退化解 |
| **New Horizons** | 68526 d | **115165 d** | ❌ 退化解 |
| Mars Express | 388 d | 201 d | ✅ |
| Galileo | 6604 d | 6303 d | ✅ |
| **Juno** | 5873 d | **7348 d** | ❌ 退化解 |
| Rosetta | 6564 d | 2150 d | ✅ |
| BepiColombo | 484 d | 362 d | ✅ |

其中 **New Horizons 的 `Jupiter → Pluto` 单段物理上限被推导为 67027 天（183 年）** ——
这是纯弹道霍曼尺度，未计入木星借力的弹弓增益（`GiantSlingshotToFRelaxation` 路径似未生效）。
该包络本身也偏保守。

**处置说明**：判据采用 `总航时 ≤ 各航段物理上限之和`，**没有引入任何倍数系数或新拍定的阈值**
（加个 `×2` 就能让 8/8 全过，但那正是本项目一直在清除的"换个数让它通过"）。
3 个不通过是**真实信号**，不是回归。

**结论：这属于求解质量问题（ToF 包络过宽 → 存在世纪尺度退化解），不属于"应试作弊"**，
建议单独立项，不要通过在测试里放宽判据来掩盖。

---

## 七、明确遗留（未掩盖）

1. **`Scenario9ChallengeTest.cs:54`** 仍以 `waypointIntentions: new List<string> { "Mercury" }` 注入拓扑。
   它是"Scenario 9 挑战场景"的**声明式输入**（场景定义本身就是"经水星去海王星"），与
   `HistoricalMissionsDebuggerTest` 的"自称盲搜却注入拓扑"性质不同。**未改**，但建议在类注释中
   显式声明"本场景为声明式拓扑输入，非盲搜"。
2. **`MGABenchmarkSuite` 的历史 ΔV 缺出处**：`HistoricTotalDeltaVMinMs/MaxMs` 22 组数值均无来源标注。
   本轮新增了 `HistoricTotalDeltaVProvenance` 字段与宽度警告，但**未逐条补出处**（需查文献，不在代码层能解决）。
3. **`DirectDiversityTestModule.cs:41`、`DirectHohmannMissionsTest.cs:64`、`FictionalMissionsTest.cs:78`**
   使用 `tofDays * 1.35` 作为 `MaxToFDays`。这是**单航段直接转移的搜索域**（对虚构/直达任务），
   不涉及历史真值，属正常的工程包络，**未改**。
4. **`GpuAccelerationValidationTest` 单位标注不一致**：Case 2 把 `nominalLoss` 打成 `m/s`、
   Case 3 打成 `km/s`（同一计算量）。本轮只改了列名标注，**未判定哪个正确** —— 需与
   `MGAPhysicsLossEngine.ComputeLoss` 的量纲对照后统一。
5. **Galileo/Juno 回放诊断的总 ΔV 明显偏大**（31493 / 22711 m/s，而真实任务约 4500 / 3000 m/s）。
   本轮只修正了判定与标注，**未追究数值成因**。可能原因：`DsmEta` 固定 0.5、`computeAdvanced: false`、
   或 `DestinationApAKm` 设定导致捕获 ΔV 巨大。建议单独立项排查（这属于求解精度问题，不属于"应试作弊"）。
6. **术语统一**：`MGABenchmarkSuite`（发射时刻盲、现改为拓扑盲）、
   `MGAHistoricalBlindSearchSuite`（完全盲）、`GuiPresetOptimizationsTest`（预设给定拓扑）三者
   现已在类注释中各自写明口径，但 README / 报表层尚未统一"盲搜"术语定义。
7. **【新】拓扑盲搜重现率仅 1/5**（见 §6.1）—— 这是修复后**首次被如实测量**的能力指标。
   4 个失败均卡在"历史工质预算"硬约束上，建议单独立项分析是否需要调整收敛策略，
   而不是放宽预算或判据。
8. **【新】ToF 物理包络过宽导致世纪尺度退化解**（见 §6.2）：
   `New Horizons` 的 `Jupiter → Pluto` 单段上限被推导为 67027 天（183 年），
   纯弹道霍曼尺度、未计木星借力弹弓增益。建议单独核查
   `MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds` 的巨行星弹弓分支。
9. **【新】`HistoricalBenchmarkMissionsTest` 运行时膨胀**：该类遍历全部 **22** 个
   `StandardHistoricalMissions` 并逐个执行 `RunBenchmark`；由于搜索域改为年粒度窗口 + 物理航时边界，
   **单次运行耗时约 3.5 分钟**（原"±N 天窗口 + 历史航时 ±10%"快得多）。
   这是口径修正的必然代价（原窗口把答案邻域当搜索域），但需要用户知悉。
10. **【新】世纪初尺度退化解仍然存在**：`Apollo 11 (1969)` 在 `min(ΔV)` 择优下曾选出
    **航时 209762 天（574 年）/ 749 m/s / Continuity=False** 的退化解。
   已在择优规则中加入"总航时不超物理航时包络"的第一优先级闸门（与 `GuiPresetOptimizationsTest` 同规则），
   但**根因仍是 ToF 物理包络过宽**（见上面第 8 条），未见根治。
11. **【新】`Mars Express` / `GTO ➔ GEO` 在基准表中没有中间借力天体**（`intermediates` 为空），
   因此 `HistoricTopologyTrajectory` 模式下退化为直达转移搜索 —— 这两个用例本质上是
   "直接转移基准"而非 MGA 基准，混在 `StandardHistoricalMissions` 里属分类问题，未改。

---

## 八、本轮工作流教训（自我记录）

1. **批内对同一文件的多次编辑可能只落地一部分** —— 本轮遇到两次（`MessengerEvaluationTest` 的
   4.1 窗口、`GuiPresetOptimizationsTest` 的 `× 2.0`），两次的表现都是"工具报成功但文件未变"。
   **对策：同一文件的多处修改改为逐次串行编辑，并在每次编辑后立即 grep 复核。**
2. **修复完成即编译**，不要等最后一起编 —— 否则一个早期语法错误会掩盖后续所有验证。
3. **修复后必须实跑**：本轮的两个最有价值发现（1/5 重现率、3/8 退化解）都不是编译能发现的，
   而是"把恒真判定换成可证伪判定"之后跑出来的。

---

*本记录对应的全部改动均通过编译（0 警告 0 错误）与至少两项运行时冒烟测试。*
