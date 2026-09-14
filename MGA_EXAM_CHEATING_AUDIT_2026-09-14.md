# MGA 模块 · 应试作弊行为专项排查（2026-09-14）

> **⚠️ 本报告的全部 P0/P1/P2 项已修复** —— 见 `MGA_EXAM_CHEATING_FIX_RECORD_2026-09-14.md`。
> 修复后主工程与测试工程均 **0 警告 0 错误**，并已通过 `--test galileo` / `--test juno` 运行时冒烟测试。
> 修复过程中另发现审计遗漏的 2 处同类恒真判定（`GalileoEvaluationTest` / `JunoEvaluationTest`），已一并修复。
>
> **触发**：在确认 `MGADifferentialEvolution.cs` 曾被反编译件覆盖并已恢复之后，对**剩余的真实源码 + 考核系统**做一次针对性排查。
> **范围**：`src/KSPGMAT/MGA/` **与** `tests/MGADebugRunner/`（上一轮只扫了 src，这是重要遗漏）。
> **前置**：已按 `source-tree-integrity-check` 流程复核工作区完整性 —— 全部 `.cs` 文件的 HEAD 与工作区均无反编译特征，**本轮结论基于干净源码**。
> **性质说明**：本报告只记录"应试作弊"（为让考核通过而扭曲代码/判据/报告）类行为，不重复上一轮的"魔法数字"清单。
> **后续更正**：正文中的计数"23 处"应读作 **25 处**（追加 Galileo / Juno 两处恒真判定）。

---

## 0. 核心结论

**考核系统本身已被污染，且污染程度比求解器更重。**

- 本轮定位 **23 处**应试作弊行为（S 级 5 · A 级 6 · B 级 3 · C 级 8 · D 级 1），其中 **5 处 S 级**（"无条件通过"或"预写成功结论"）——这类问题的危害是釜底抽薪：**求解器侧改得再干净，只要考核恒真，"验证通过"就没有任何信息量**。
- 分布上：**19 处在 `tests/MGADebugRunner/`，仅 4 处在 `src/`**。上一轮审计的遗漏原因就是只扫了 `src/`。
- 最严重的三条：
  1. `CassiniEvaluationTest` 直接 `ctx.EndCase(true)` —— **无条件通过**；
  2. `MessengerEvaluationTest` / `NewHorizonsEvaluationTest` —— **只有抛异常才算失败**，评估数值从不参与判定；
  3. `MGAHistoricalBlindSearchSuite` 报表生成器**无条件**写入三条成功结论，其中一条与紧邻上方的实测表格**自相矛盾**。

---

## 1. S 级 —— 无条件"通过"与预写成功结论

### S1 · Cassini 评测测试无条件通过 ★最严重

`tests/MGADebugRunner/TestSuites/CassiniEvaluationTest.cs:17-23`

```csharp
ctx.StartCase("Cassini_VvejgaEvaluation");
EvalCassini();            // ← 返回值被丢弃
ctx.EndCase(true);        // ← 硬编码 true
return true;
```

`EvalCassini()` 内部做了大量计算与打印，但**其结果从未参与判定**。该测试在结构上不可能失败。

### S2 / S3 · MESSENGER 与 New Horizons 评测"只有异常才算失败"

`MessengerEvaluationTest.cs:18-31` 与 `NewHorizonsEvaluationTest.cs:18-31`（结构完全相同）

```csharp
bool ok = true;
try { EvalMessenger(); }                       // ← 返回值同样被丢弃
catch (Exception ex) { ctx.Fail(...); ok = false; }
ctx.EndCase(ok);
return ok;
```

判定仅取决于"是否抛异常"。**ΔV 是否达标、飞掠是否可行、是否收敛，一概不参与判定。** 这两个套件名为 "评估测试"，实为诊断打印。

### S4 · 历史任务调试器"过 1 个即算通过"

`tests/MGADebugRunner/TestSuites/HistoricalMissionsDebuggerTest.cs:17-22`

```csharp
int passed = RunHistoricalMissionsDebugger(out int total);
bool ok = passed > 0;                                   // ← 5 个任务过 1 个就 OK
if (!ok) ctx.Fail($"历史任务调试器未通过任何任务 ({passed}/{total})");
```

5 个任务（Voyager 2 / Galileo / Cassini / BepiColombo / Scenario 9）中只要 1 个"通过"，整个套件即 PASS。

### S5 · 报表生成器无条件写入三条成功结论（含自相矛盾）★

`src/KSPGMAT/MGA/Solver/MGAHistoricalBlindSearchSuite.cs:893-897`

```csharp
sb.AppendLine("## 3. 核心结论与工程启示");
sb.AppendLine("1. **算法自主涌现的惊人保真度**: 即使完全不给任何借力天体提示，MCTS 势能梯度与开普勒能量赤字剪枝系统
   依然自主推导出了 Voyager 2 的巨行星大巡游链路、Cassini 的内潜弹弓、以及 Mariner 10 的金星减速跳板，
   证明了系统的通用天体力学真实性；");
sb.AppendLine("2. **超历史解的科学必然性**: 在部分任务中，得益于现代计算机每秒数亿次兰伯特迭代…实现了超越
   20 世纪手工/图解工程规划的更优解；");
sb.AppendLine("3. **100% 严苛的物理守恒**: 所有借力点飞掠脉冲严格 0.00 m/s，双曲转向角严格受制于双曲离心率与
   近拱点安全高度，彻底杜绝了任何穿心或伪机动假象。");
```

问题有三层：

1. **无条件输出** —— 这段文字在 per-mission 循环之后，与实测结果无关。哪怕 13 个任务全部失败（`未收敛`），这三条结论照样写进报表。
2. **点名了具体的、未经验证的成就** —— "推导出了 Voyager 2 的巨行星大巡游链路、Cassini 的内潜弹弓、Mariner 10 的金星减速跳板"。这是把期望结果写成了结论。
3. **第 3 条与紧邻上方的实测表格直接冲突** —— 表格中每行有 `audit` 列，当 `FlybyDeltaVKms >= 0.001` 时打印 `⚠️ 需机动 {x} m/s`（`:878`）。报表本身就会产出带该警告的行，随后立刻断言"所有借力点飞掠脉冲严格 0.00 m/s"。

**这是最典型的学术不端形态：预写结论 + 忽略实测。** 它同时污染 `reports/test_reports/report_historical_blind.md`（22.9 KB）。

---

## 2. A 级 —— 标准答案注入（把历史真值喂给求解器）

### A1 · 历史借力序列当"航点意图"传入求解器

`tests/MGADebugRunner/TestSuites/HistoricalMissionsDebuggerTest.cs:64-71`

```csharp
var res = masterOpt.OptimizeMission(
    departureBody: gt.Departure,
    arrivalBody: gt.Arrival,
    waypointIntentions: gt.ExpectedWaypoints.ToList(),   // ← 历史借力序列
    ...);
```

而 `gt.ExpectedWaypoints` 就是标准答案（`:30-34`）：

| 任务 | ExpectedWaypoints |
| :--- | :--- |
| Voyager 2 | `Jupiter → Saturn → Uranus` |
| Galileo | `Venus → Earth → Earth` |
| Cassini | `Venus → Venus → Earth → Jupiter` |
| BepiColombo | `Venus` |
| Scenario 9 | `Mercury` |

**下游确认**：`MGAMasterOptimizer.cs:348` → `FlybySequence = waypointIntentions != null ? new List<string>(waypointIntentions) : new List<string>()` —— 直接成为求解器的拓扑输入。

**而该测试的横幅自称**（`:42`）：`"原则: 100% 纯通用算法自适应推导，0 针对特定任务的硬编码特判代码！"` —— 与代码直接矛盾。

### A2 · 基准套件在"盲搜模式"下仍然交出历史拓扑 ★重要遗漏

`src/KSPGMAT/MGA/MGABenchmarkSuite.cs:506`

```csharp
FlybySequence = testCase.FlybySequence,   // ← 两种模式都会执行到这里
```

上一轮我修掉了注入型答案注种（历史发射日 + 历史航段航时）并改了开窗方式，但**拓扑仍是从 `testCase.FlybySequence` 直接给出的**——即所谓"无先验盲搜模式"只对**发射时刻**盲，对**借力拓扑不盲**。

全仓比对后确认：**真正对拓扑盲的只有 `MGAHistoricalBlindSearchSuite`**（其 `FlybySequence = new List<string>()` 严格为空）。两份套件的"盲搜"含义不同，却在各自的报表里都写作"纯盲搜/无先验"，容易造成误读。

### A3 · 手写"标称基因组"= 标准答案

`tests/MGADebugRunner/TestSuites/CassiniEvaluationTest.cs:175-184`

```csharp
double[] nominalGenome = new double[] {
    launchUT,                  // 历史发射时刻（精确值）
    193.0 * 86400.0,           // 历史第 1 段航时
    424.0 * 86400.0,           // 历史第 2 段航时
    55.0  * 86400.0,           // 历史第 3 段航时
    499.0 * 86400.0,           // 历史第 4 段航时
    1278.0 * 86400.0,          // 历史第 5 段航时（基准表作 1279）
    0.5, 0.5, 0.5, 0.5, 0.5
};
```

对照 `MGABenchmarkSuite.cs:158` 的 `HistoricToFDays = { 193.0, 424.0, 55.0, 499.0, 1279.0 }` —— **逐个数字对上（末段差 1 天）**。该数组随后被 `PykepBridge.BuildFullSolution(nominalGenome, fullChain, cfg, false)` 直接求解，并打印为 `[Nominal Cassini Test]`。

### A4 · "优化"以答案点为起点

`CassiniEvaluationTest.cs:213-225`

```csharp
double[] polGene = (double[])nominalGenome.Clone();     // ← 从标准答案克隆
for (int iter = 0; iter < 10; iter++) { ... }           // 步长 ±0.1~±1.0 天
```

所谓 `[Polishing Nominal Cassini with Hooke-Jeeves Pattern Search]` 是在**已知历史解**附近做 ±0.1~1.0 天的局部精修 —— 这不是搜索，是复现。

### A5 · GPU 验证套件同样预置答案并放进初始种群

`tests/MGADebugRunner/TestSuites/GpuAccelerationValidationTest.cs:126-133`（Cassini 构型）

```csharp
nominalGene[0] = launchUT;
nominalGene[1] = 193.0 * 86400.0;
nominalGene[2] = 424.0 * 86400.0;
nominalGene[3] = 53.0  * 86400.0;
nominalGene[4] = 600.0 * 86400.0;
nominalGene[5] = 1000.0 * 86400.0;
```

`:147` → `pop.Add((double[])nominalGene.Clone());  // #0 为标称共振个体` —— **把答案放进 DE 初始种群**。
`:240-246` 对 MESSENGER 构型重复同样做法（`155 / 224.7 / 130 / 88` 天）。

### A6 · 用"预期 ΔV"当求解器的工质预算

`HistoricalMissionsDebuggerTest.cs:58-59`

```csharp
var hp = MGAHighValueParameters.CreatePreset(gt.Preset);
hp.MaxVesselDeltaVMs = gt.MaxExpectedDvMs;     // ← 预算 = 预期答案的 ΔV
```

**下游确认**：`MGAMasterOptimizer.cs:361-362` → `ConstrainByVesselDeltaV = (p.MaxVesselDeltaVMs > 0); VesselMaxDeltaVMs = p.MaxVesselDeltaVMs;` —— 该预算被作为**硬约束**生效。

⇒ 这把 B1 的判定变成了恒真式（见下）。

---

## 3. B 级 —— 判据放宽与自证（先给答案再判答案）

### B1 · `+35%` 容差，且该判据实质恒真 ★

`HistoricalMissionsDebuggerTest.cs:80-84`

```csharp
if (totalDv <= gt.MaxExpectedDvMs * 1.35) {
    passed++;
    Console.WriteLine($"    ✅ 力学形态与能量预算匹配成功！");
}
```

三层问题：

1. **35% 的容差本身就很宽**（上一份自审报告曾声称已清除 DE 内核里的 `* 1.02 / * 1.05` 放行，这里在考核侧却留着 `* 1.35`）。
2. **判据恒真**：由 A6，求解器预算被设为 `MaxExpectedDvMs` 且为硬约束，因此任何 `res.Success == true` 的解必然满足 `totalDv ≤ MaxExpectedDvMs < MaxExpectedDvMs × 1.35`。**该判据不检验任何东西**，只是一个装饰性的恒真式。
3. 结合 S4，5 个任务过 1 个即 PASS ⇒ **整个套件的失败概率几乎为零**。

### B2 · 基准套件的"历史 ΔV 参考区间"宽度可疑

`MGABenchmarkSuite.cs` 的 PASS 判据是纯**包含**关系（`:582-584`）：

```csharp
bool dvWithinTolerance = res.ComputedTotalDV >= testCase.HistoricTotalDeltaVMinMs
                      && res.ComputedTotalDV <= testCase.HistoricTotalDeltaVMaxMs;
```

因此**区间宽度 ≡ 判定强度**。实测各任务区间宽度：

| 任务 | 区间 (m/s) | 宽度 |
| :--- | :--- | ---: |
| Rosetta EEMEB | 3800 – 15000 | **11200** |
| Juno EEJ 1-DSM | 4200 – 16000 | **11800** |
| BepiColombo SEP | 3500 – 12000 | **8500** |
| Mariner 10 | 3800 – 5300 | 1500 |
| New Horizons | 7500 – 9200 | 1700 |

宽度跨度为 **900 ~ 11800 m/s（13 倍）**。`3800–15000 m/s` 这样的区间无法作为一个"历史工程参考值"——它宽到足以吞掉几乎任何求解器输出，等价于把该任务的 ΔV 判据放宽到近乎无效。

**待核**：这些区间缺少出处引用。建议改为"单一历史值 + 显式相对容差（如 ±10%）"，并注明数据来源。

### B3 · 通过率判据本身

见 S4：`passed > 0`。

---

## 4. C 级 —— 搜索域锚定在答案邻域

虽然上一轮修掉了 `MGABenchmarkSuite` / `MGAHistoricalBlindSearchSuite` 的"历史发射日 ±N 天"开窗，**但评测测试套件里的同类锚定全部保留**：

| 位置 | 搜索域 | 真实机会空间量级 |
| :--- | :--- | :--- |
| `CassiniEvaluationTest.cs:163-164` | `launchUT ± 10 天` | 会合周期 ~1 年 |
| `CassiniEvaluationTest.cs:205-206` | `launchUT ± 10 天`（Hooke-Jeeves 界） | 同上 |
| `CassiniEvaluationTest.cs:243` | `RunWindow(launchUT ± 10 天)` | 同上 |
| `MessengerEvaluationTest.cs:101-102` | `launchUT ± 60 天` | — |
| `MessengerEvaluationTest.cs:253-254` | `RunWindow(launchUT ± 60 天)` | — |
| `NewHorizonsEvaluationTest.cs:239-240` | `RunWindow(launchUT ± 60 天)` | — |
| `GuiPresetOptimizationsTest.cs:54-55` | `launchUT ± 20 天` | — |
| `HistoricalMissionsDebuggerTest.cs:55-56` | 窗口**从历史历元起算**`+ span` | Galileo/Cassini/BepiColombo 的字符串**就是真实发射日** ⇒ 最优解位于窗口起点 `t=0` |

在 `±10~60 天` 的窗口里"搜出"历史解，等价于已经知道答案在哪。

---

## 5. D 级 —— 潜在作弊机制（当前死代码，但一旦接线即成作弊）

### D1 · `ApplySolutionToConfig`：把已得解当搜索域

`src/KSPGMAT/MGA/Solver/MGAAdaptiveSolver.cs:1166-1181`

```csharp
config.EarliestDepartureUT = solution.DepartureUT;
config.LatestDepartureUT   = solution.DepartureUT + 86400.0;                    // 发射窗 1 天
config.FlybySequence       = solution.Legs.Select(l => l.ToBody).ToList();      // 拓扑写回
config.MinToFDays          = solution.Legs.Select(l => Math.Max(5.0, l.ToFDays * 0.9)).ToList();  // −10%
config.MaxToFDays          = solution.Legs.Select(l => l.ToFDays * 1.1).ToList();                 // +10%
```

这就是上一份自审报告声称已清除的 **"航时 ±10% 夹逼"** 的实际形态。

**当前状态**：全仓 `grep ApplySolutionToConfig` **只有定义、0 个调用者** ⇒ 死代码。**危害可控但必须处理**：任何人接线它，就等于把"答案 ±10%"当搜索域；且若上层传入的 `solution` 来自注种路径，则中心就是历史真值。

建议：要么删除，要么在 XML 文档中显式标注"仅用于**解自身的局部精化**，严禁以历史真值作为输入"，并在函数内加断言拒绝 `IsHistoricReplay` 来源的解。

---

## 6. 与上一份自审报告的关系（哪些"已清零"声明不成立）

| 报告声明 | 实况 |
| :--- | :--- |
| §1.3「历史盲搜套件向求解器泄露总航时与定制 C3 门禁 — 已完成」 | `TotalToFMaxDays = -1.0` 属实；**但 New Horizons 的 `LaunchCeilingMs = 10000`（默认 3800）仍在**，属逐任务放宽门槛 |
| §1.5「差分进化内核私自放宽硬门禁 (+2%~+5%) — 已拔出」 | DE 内核里确实没有；**但考核侧的 `* 1.35` 放行仍在**（B1） |
| §5「`PykepNative.cs` / `Mgadefaults` 天体名硬编码 — 已完成」 | 与本轮无关，维持上一轮结论 |
| "基准测试与历史盲搜完全解耦（标定模式与无先验盲搜模式物理分离）" | **不成立**：`MGABenchmarkSuite` 在两种模式下都把历史拓扑交给求解器（A2）；而真正纯盲搜的 `MGAHistoricalBlindSearchSuite` 报表又写了预写结论（S5） |
| 上轮审计结论"128 处魔法数字" | 有效（非本轮范围）；**但本轮新增了"考核系统 23 处作弊"，上一轮完全遗漏，因为只扫了 `src/`** |

---

## 7. 修复优先级建议

**P0（立即 —— 让考核恢复信息量）**

1. **删掉 S1/S2/S3 的"恒真判定"**：`EvalCassini/EvalMessenger/EvalNewHorizons` 应返回结构化结果（是否收敛、ΔV、飞掠可行性、是否超预算），由 `Run` 依**明确阈值**判定；`ctx.EndCase(true)` 必须消除。
2. **S5**：把三条结论改为**由实测数据生成**。若 `passed == 0` 或存在 `⚠️ 需机动` 行，结论段必须如实说明；删去点名的任务成就，或改为只在该任务确实匹配历史拓扑时才输出。
3. **A1/A6**：`HistoricalMissionsDebuggerTest` 要么移除 `waypointIntentions`（真正做拓扑搜索），要么把测试改名为"历史轨迹复现标定（非盲搜）"，并**停止**把 `MaxExpectedDvMs` 同时用作预算与判据。

**P1（高 —— 让搜索域不泄露答案）**

4. **A2**：给 `MGABenchmarkSuite` 增加独立的"拓扑盲搜"标志；或让"盲搜模式"下 `FlybySequence` 只保留终点（与 `MGAHistoricalBlindSearchSuite` 口径统一）。
5. **C 级**：把各评测套件的搜索域改为物理推导（会合周期 / 发射机会年份），不再锚定 `launchUT ± N 天`。
6. **A3/A4/A5**：`nominalGenome` 不得作为"求解器输出"呈现；如需作为"标定回放"的输入，必须与寻优路径严格分离，并在输出中标注 `[回放]`。

**P2（中）**

7. **B2**：历史 ΔV 区间改为单一值 + 显式相对容差，并补出处。
8. **D1**：`ApplySolutionToConfig` 加护栏或删除。
9. 在报表与 README 中统一"盲搜"术语的定义（是"拓扑盲"还是"发射日盲"），避免同名不同义。

---

## 8. 方法论记录（本轮为什么能查出来）

1. **先做完整性检查**（`source-tree-integrity-check`）—— 否则又会把反编译件当源码审一遍。
2. **扫 `tests/` 而不只扫 `src/`** —— 上一轮只扫 src 是重大遗漏；本轮 23 处作弊里，**19 处在 `tests/`**。作弊往往发生在**考核侧**而不是被测侧。
3. **看"判据能不能失败"**，而不只看"判据是什么" —— `+35%` 本身不致命，致命的是它与"预算=预期值"组合后成为恒真式。**判定逻辑要做可证伪性检查。**
4. **看报表/README 里的结论是生成的还是写死的** —— 静态成功叙述是学术不端最直白的形态，也是最好查的一种。
5. **对比两份"真值表"** —— `MGABenchmarkSuite.StandardHistoricalMissions`、`MGAHistoricalBlindSearchSuite.HistoricalCatalog`、`HistoryMissionGroundTruth.GroundTruths` 三张表对同一任务的数值互不一致（如 Voyager 2 发射日 `20 Aug 1977` vs `01 Aug 1977`；Cassini 末段 ToF `1279` vs `1278`）。**同一事实多真值源本身就是危险信号。**

---

*本报告全部结论基于干净工作区的直接读取与 grep 交叉验证，未修改任何文件。*

---

## 9. 修复后续（2026-09-14 追加）

本报告全部 P0/P1/P2 项**已修复**，详见 `MGA_EXAM_CHEATING_FIX_RECORD_2026-09-14.md`。补充三点：

1. **实际修复 25 处，比本报告多 2 处** —— 修复时改用"按模式扫全量"而非"按清单逐条查"，
   于是发现 `GalileoEvaluationTest` / `JunoEvaluationTest` 有与 S2/S3 **完全同型**的
   "只有抛异常才算失败"恒真判定，本报告漏列。
   ⇒ **教训：审计清单是"样例"，不是"全集"；修复时必须用模式 grep 复核全量。**
2. **修复后暴露了两个此前被恒真判定掩盖的真实问题**（不是回归）：
   - 拓扑盲搜的**历史构型重现率只有 1/5**（修复前该项测试因 `passed > 0` 报 PASS）；
   - GUI 预设路径存在**世纪尺度退化解**（Cassini 30261 天、New Horizons 115165 天），
     此前被"历史航时 ±10% 夹逼"掩盖。
3. 本报告 §7 未列出的一项也被一并修复：`MGAEmpiricalRegistry.cs:166` 存在**未转义内嵌双引号**，
   文件无法通过编译（`CS1003`）。该文件是**上一轮"未编译"的修复轮次**引入的。
   ⇒ **教训：任何标记为"已修复"的轮次必须附编译输出。**
