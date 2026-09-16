# KSP_GMAT 项目长期记忆

> 精简于 2026-09-17。历史细节见 `.workbuddy/memory/YYYY-MM-DD.md`。

---

## 一、构建与验证（改代码前必读）

- **编译**：`dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release --no-restore`
  - 不带 `--no-restore` 会报 `NuGet.targets(649,5) Value cannot be null (path1)` —— 环境性，非代码问题。
  - 加 `-t:Rebuild` 全量（约 4s）。目标 `net48`，引 KSP 安装目录程序集。
  - 输出 `src/KSPGMAT/bin/Release/net48/KSPGMAT.dll`，PostBuild 自动拷 `GameData/KSPGMAT/Plugins/`。
- **红线**：`deploy.ps1` + `tools/ComplianceChecker` 要求 **0 警告 0 错误**。
- **运行时验证**（必须仓库根目录执行；`PykepNative` 按 cwd + BaseDirectory 查 `GameData/KSPGMAT/native`）：
  ```bash
  tests/MGADebugRunner/bin/Release/net48/MGADebugRunner.exe --blind [cassini|...]   # 13 任务，~263s
  tests/MGADebugRunner/bin/Release/net48/MGADebugRunner.exe --train-surrogate
  ```
- ⚠️ 运行期间 DLL 被占用，**不能同时编译**。⚠️ 后台跑盲搜时会话中断会杀进程（日志停滞+无进程残留即为此）。
- **确定性基线**：`scratch/blind_p5.log`（自愈前，平均绝对误差 1537 m/s）；
  `scratch/blind_selfheal.log` / `blind_selfheal2.log`（逐链自愈后，**两次逐位一致 13/13**，平均偏差 +932 m/s，
  较前改善 −2821 m/s，galileo 跨过工质线 7218→6113）。**A/B 比对优先 selfheal2，只比 ΔV/ToF/拓扑，不比耗时**（±3% 浮点浮动）。
- 13 任务中 **cassini / messenger / bepicolombo / newhorizons 的考核值来自空池兜底的约束超限解**，不可与历史 ΔV 直接比。
- 全量真值数据集：`reports/test_reports/chain_truth_labels.csv`（3463 链，`--label` 生成，~77 min）。

---

## 二、拓扑选择链（2026-09-11 定稿）

```
候选池(MCTS+开普勒图) → SelectDiverseTopologies(去重+深井直达门+Pareto序)
  → FilterAndPrioritizeReachableSequences(赤字门[实测恒不触发] + Deb 排序)
  → SelectQualityDiversityTopologies(7 维连续相空间 QD, 16 席, λ=0.85)
```
- 离散流派标签已全部移除，多样性由 QD 连续裁决。**QD 遍历须保持固定列表序 + 布尔标记**（勿改 HashSet 枚举）。
- 剩余预设 = 数值权重（QD 质量分四系数、特征权重 `{1,3,2,1.5,1,1,0}`、λ），消除路径是**真值标注 + 有监督标定**，非手调。
- 标定现状：质量分对真值 DV 秩相关仅 **−0.36**（ΔV 权重 0.001 几乎失明）；估计器对直达 ToF **高估 2.2×**（NH 漏检根因）；QD 查全 11/13。
- ⚠️ **`TopologyCategory` 是活代码，切勿删**：`MGAMCTSTopologySelector.cs:897` → `:917` → `:920` 的"直达拓扑保留策略"依赖它。
  （`CandidateTopology` 是 struct，空桶 `FirstOrDefault()` 返回 `default` 而非 null，无 NRE 风险。）

---

## 三、已落地的特征/求解器修复

### ToF 直达偏差（2026-09-11）
- `MGAEphemerisReachabilityAnalyzer.cs:494-512`：`isGiantOutward` 分支后新增 `r2/r1 > 1.5` 分支，
  `legDays *= max(0.45, rRatio^(-0.20))`，拟合 13 任务真值。Pluto 偏差 +119% → +9.9%。
- 属**特征侧精度提升**，修复后 13 任务盲搜逐位一致（QD 选择未变）。

### 逐链自愈（2026-09-12 起，当前生产）
- 位置：`MGAAdaptiveSolver.cs:474`（霍曼基准移出并行循环）+ `:674-682`（`Parallel.For` 内每条拓扑汇总前 `AuditAndHeal`）。
- 机制：原 Stage 4 全局自愈仅 1 次（13 任务成功 3 次）→ **现每条拓扑后立即自愈，总次数 3 → 50+**。
- 收益 5/13 改善、8/13 零回归；代价耗时 +157%（102s→263s）。
- 触发：`MGASelfHealingEngine.cs:95` `needHealing = (isHighEnergyTrap || hasLawdenViolation || hasBPlaneSafetyViolation) && EnableSelfHealing`；
  健康解直接 `return candidates`，零成本。

### 搜索窗与边界（2026-09-14 第二轮）
- **航时包络是软惩罚不是硬约束**：`TotalToFMaxDays` 只进 `MGAPhysicsLossEngine` 的 `MissionDuration` 惩罚项。
  **判断边界是否生效，不能看有没有赋值，要看它是否进入目标函数/硬门禁。**
- `AutoTuneToFBounds` 在 `MGAAdaptiveSolverConfig.FromMGAConfig` **硬编码 true 且无条件覆盖 `TotalToFMinDays`**
  ⇒ 所有经 `MGAConfig` 的路径（GUI 预设/基准/盲搜）下界被静默丢弃。已改为 fill-in 语义。
- **`SolarPlanet.SMA` 是相对自身母体的半长轴**，配 μ 前必须确认参考系一致。
  参考实现 `MGAFlybyDynamicsHelper.ResolveOrbitalRadiusKm`（行星→根恒星 / 卫星→ParentName / 自中心→停泊轨道；不可解析即拒绝）。
  反例实测：μ_Earth + Earth 日心 1 AU + Moon 地心 SMA ⇒ 地月航时上界 53.8 年。
- **航时上界唯一真值源**：`MGAFlybyDynamicsHelper.MaxTransferToFYears = 15 儒略年`，异星段与同星共振段都须施加。
  `--test tof_bounds` 是可证伪守门（PASS ⟺ 全部航段 ≤ 20 年）。
- **未识别天体不再伪装成地球**：`GetStandardPlanetFallback` 改为全零 + `IsOrbitUnresolved`，消费侧必须拒绝。
  **禁止 `p.SMA > 0 ? p.SMA : MGADefaults.AU_KM` 这类静默替代。**
- 两个统计约定：`LowerQuantileLevel = 0.25`（深势阱判据）、`DeepWellExitShellSteps = 2`（反梯级深井），
  壳层比由 `(SMA_max/SMA_min)^(1/(n-1))` 自导出。

---

## 四、⚠️ 实证教训（反复踩过）

1. **先建立可信度量，再优化**。P4「ML 前置粗筛」迭代 3 轮后才发现靶子错了：
   原生 Lambert 只占 DE 注种阶段 **1.4%**（155ms/11.09s），门自身开销 100ms 同量级。
   ⇒ **不要在原生调用链上做预筛**，97% 耗时在托管侧（前向步进几何构造、`FindAndRankResonantCombos` 笛卡尔积）。
2. **改 ML 代码后必须核对"指标是否随改动变化"**。指标字节级不变 ⇒ 评估路径没走到网络
   （曾因 `Evaluate()` 调带 `IsTrained` 门控的 `Forward()`）。损失函数**必须在对数域**处理 ΔV（重尾正量）。
3. **不要让"排序顺序"决定解**。`ConcurrentBag` 枚举序随线程调度变化 ⇒ 固定 `Random(42)` 也不可复现。
   决定胜出解的排序必须有**由解自身物理量导出的次级键**；并行归约须**按索引显式指定顺序**。
4. **解选择绝不能取 `[0]`**：`MGAAdaptiveSolver.Solve` 候选解按候选拓扑索引/窗口顺序展开，非质量排序。
   取 `[0]` 会让所有任务退化为单航段直达（Cassini 39688 m/s）。须按总 ΔV → 总 ToF → 航段数择优。

---

## 五、⚠️ 审计与报告的不可信史（写治理报告前必读）

### 5.1 三类已证实的造假/失真

| 报告 | 声称 | 实际 |
| :--- | :--- | :--- |
| `MGA_CODE_AUDIT_...OVERFITTING.md`（09-13） | 46 项 100% 清零 | 独立核验重新定位 **128 处**（P0 23 / P1 63 / P2 42） |
| `MGADifferentialEvolution.cs` 反编译件 | "DE 需源码级重构" | 工作区污染（3094 行 / 1052 处 `numNN`，`git diff --stat` = 3214+/2327-）。`HEAD` 为 2326 行干净手写实现，**已从 git 恢复** |
| `MGABenchmarkSuite` 基准测试 | "求解器复现历史任务" | `BlindSearchEvaluationMode` 默认 false 且全树无调用方置 true ⇒ 永远走答案注种。**已改为三态枚举** |

### 5.2 写治理报告的铁律
不得把"提成具名常量""换成另一组绝对值""换成数学等价分支"记为"已清零"。每条须附
**(a)** 非太阳系/非测试任务上的实测行为差异；**(b)** 新常数推导出处；**(c)** grep 证明同语义旧常数全树无残留。
反例：`Mu>=5.0e6`、`Mu>=500.0`、`SMA*0.45`、`MGAFlybyDynamicsHelper.cs:288` 的 `150.0/6.5` 四项皆是"新体系已建、旧实现未删"。

### 5.3 修复三原则（A/B/C 分类）
- **A 类**（天体名/任务名/历史构型/测试场景写死的分支与阈值）→ 改物理推导或 `MGAPhysicalScales` 无量纲判据。
- **B 类**（未标定的经验系数）→ 命名化 + 登记 `MGAEmpiricalRegistry`（附失效条件与替换路径），**不擅自改值**
  （改值只是"换一个拍定的数"，正是前报告的错法）。
- **C 类**（物理常数 / 数值保护）→ 不动。

### 5.4 自动审计工具本身也会系统性误判（2026-09-17）
`reports/mga_empirical_and_magic_audit.md`（4799 项 / 774 P0）的 P0 判定**不可采信**。
被判 P0 的绝大多数是 **代数系数**（`(r1+r2)*0.5` 霍曼半长轴、`2v·sin(θ/2)` 余弦定理）、
**物理定律指数**（`Math.Pow(a,3.0)` 开普勒、`2/r - 1/a` vis-viva）、**单位换算/数学常数**（`86400.0`、`Math.PI`、`180.0`）。
另有**同义重报**（一行同时产出"经验公式"+"魔法数字"两条 ID，统计翻倍）。
⇒ **引述该类报告条数一律打折，不可当 KPI。** 去重去误后真问题约 100–200 条。
复核台账：`reports/mga_audit_refix_round2_2026-09-17.md`。

### 5.5 新增红线：工作区污染检查（审计前置）
审计/修复任何代码前先做污染检查——对比 `git show HEAD:<file>` 与工作区的反编译特征计数
（`numNN/arrayNN/textNN`）与 `git diff --stat`（insertions ≈ deletions ≈ 文件总行数 ⇒ 整文件被替换）。
`HEAD=0 而工作区>0` ⇒ 先恢复再评估。已固化为 skill：`~/.workbuddy/skills/source-tree-integrity-check/SKILL.md`。

---

## 六、应试作弊排查（2026-09-14，已修复并编译验证）

### 6.1 四条必须记住的事实
1. **作弊主要在考核侧**：23 处中 **19 处在 `tests/MGADebugRunner/`**，仅 4 处在 `src/`。
   ⇒ **审计必须同时扫 tests/**，只扫 src 会漏掉绝大部分。
2. **4 个测试是"恒真"的**（不可能失败）：`CassiniEvaluationTest.cs:21` 无条件 `EndCase(true)`；
   `Messenger` / `NewHorizons` 的 `bool ok = true` + `try{}`（返回值被丢弃，只有抛异常才算失败）；
   `HistoricalMissionsDebuggerTest.cs:19` `passed > 0`。
3. `MGABenchmarkSuite.cs:506` 两种模式都把历史拓扑交给求解器（"盲搜"只对发射时刻盲）。
   真正拓扑盲的只有 `MGAHistoricalBlindSearchSuite`。
4. `MGAHistoricalBlindSearchSuite.cs:893-897` 报表生成器**无条件写入 3 条成功结论**，与紧邻表格自相矛盾。

### 6.2 判据可证伪性检查（铁律）
不只看判据是什么，要看它**有没有可能失败**。`HistoricalMissionsDebuggerTest.cs:80` 的
`totalDv <= MaxExpectedDvMs * 1.35` 是恒真式，因为 `:59` 已把预算设成同一个 `MaxExpectedDvMs`
（`MGAMasterOptimizer.cs:361` 作硬约束生效）。**只看 `* 1.35` 会误判为"容差略宽"。**

### 6.3 已修复（25 处，主工程+测试工程 0 警告 0 错误，`--test galileo` / `--test juno` 通过）
- `EvalSupport.cs` 定义 `EvaluationOutcome`（Converged / AllLegsFeasible / TotalDvMs / BudgetMs / IsHistoricReplay / Passed）
  与 `EvalWindows`（`OpportunityYearWindow` 年粒度机会窗 / `TopologyContainsAll`）。
- **铁律**：评测函数必须回传 `EvaluationOutcome`，由 `Run` 依显式阈值裁决；
  **评测路径内禁止 `EndCase(true)`**，禁止 `passed > 0` 之类"N 中过 1 即 PASS"。
- **搜索窗一律用 `EvalWindows.OpportunityYearWindow`**（`[该年 1/1, 次年 1/1)`），禁止 `launchUT ± N 天`
  （历史发射日邻域，最优解恰在窗口中心）。
- 历史真值只能**事后比对**；回放输出必须带 `[回放]` 标签，且不得作为求解器能力证据。

### 6.4 三条血泪教训
1. **修复必须编译**。上轮"未编译"留下 `CS1003`（`MGAEmpiricalRegistry.cs:166` 未转义内嵌双引号）。
   **编译通过是"已完成"的底线。**
2. **把审计清单当"样例"，用模式扫全量**。审计点名 Cassini/Messenger/NH，同型 Galileo/Juno 被漏掉（没 grep 全量）。
3. **注意"派生泄漏"**。`MGAConfig → MGAAdaptiveSolverConfig.FromMGAConfig` 会**派生** `LegToFBounds`
   （从 `FlybySequence` 推出）。清源头字段须在**转换之前**。
   另：`BenchmarkCase.FlybySequence` **含终点**（末元素即目标），误传给
   `AutoDeriveResonantLegBounds` 第三参数（语义=中间天体）会导致末段重复。

### 6.5 遗留（未修）
`Scenario9ChallengeTest:54` 仍注入 `{"Mercury"}`；`MGABenchmarkSuite` 22 组历史 ΔV 区间**无出处**（已加宽度审计警告）；
GPU 用例同一 `nominalLoss` 在 Case2 标 m/s、Case3 标 km/s；Galileo/Juno 回放总 ΔV 偏离真实值 5~7 倍（求解精度，非作弊）。
`ApplySolutionToConfig`（`MGAAdaptiveSolver.cs:1166`，±10% 航时夹逼）当前 **0 调用者**（死代码）。
**同一事实多真值源是危险信号**：Voyager 2 发射日（`20 Aug 1977` vs `01 Aug 1977`）、Cassini 末段 ToF（`1279` vs `1278`）不一致。

---

## 七、`MGABenchmarkSuite` 三态模式（改基准前必读）

`enum MGABenchmarkMode { HistoricReplay, HistoricTopologyTrajectory /*默认*/, TopologyBlind }`
取代已删的布尔量 `BlindSearchEvaluationMode`。默认是"给定历史拓扑的轨迹优化基准"（非盲搜），
发射窗/航时边界仍全部物理推导。三处（类注释、报表首行、汇总）都显式标注模式。

- 实跑 `--test history_bench` = **9/16**，默认模式产出真 MGA 解（Mariner 4206 ✅ / Voyager2 7735 ✅ /
  Cassini 7769 ❌ / Galileo 6409 ❌ / BepiColombo 3744 ✅）。单次约 **3.5 分钟**。
- ⚠️ **航时包络 `AutoDeriveResonantLegBounds` 不自洽**：Mariner 10 需 509d 而包络仅 483d；
  NH 的 `Jupiter→Pluto` 单段被推到 **67027 d（183 年）**（纯弹道霍曼尺度，似未计木星弹弓增益）。
  曾试加"航时不超包络"闸门 → **结果变差**（Mariner 10 ✅4206→❌15768），已撤回。
  **修 `MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds`，不要再拍阈值。**
- 遗留：Apollo 11 选出 209762 天 / 749 m/s 退化解（Continuity=False）；`Mars Express` / `GTO➔GEO`
  无中间借力天体，本质是直达基准，混在 MGA 列表属分类问题。
- ⚠️ `tests/MGADebugRunner/TestSuites/*.cs` **全部 untracked（从未提交）**，无法做 `git show HEAD:` 对比。

---

## 八、架构约定

- `src/KSPGMAT/MGA/` 统一扁平命名空间 `KSPGMAT.MGA`（含 `Solver/`、`Telemetry/`），`AI/` 例外用 `KSPGMAT.MGA.AI`。
- `MGADefaults` 定义在 `Model/MGAStateManager.cs`（非独立文件），属性在 `KSPGMAT.Model`，MGA 侧需 `using KSPGMAT.Model;`。
- 两个配置类别混：
  - `MGAConfig`（`Model/MGAStateManager.cs:799`，底层，含 `MaxSwingBys/MaxResonances/MaxBackLegs/MaxBackSpacing`）
  - `MGAAdaptiveSolverConfig`（`MGA/Solver/MGAAdaptiveSolver.cs:57`，高层，含 `DeparturePlanet/ArrivalPlanet`）
- 动力学计算**只能**委托 `PykepNative` / `PykepBridge`（基线 §7「零托管数学」）。
  C# 层可做无量纲化、判据、统计与优化，**不得自编开普勒/兰伯特公式冒充原生结果**。
- 新增经验常数必须命名 + 登记 `MGAEmpiricalRegistry`，**禁止无命名的裸字面量**。

## 九、MGA 求解链真实入口（2026-09 核实）

- UI 走 `MGAAdaptiveSolver.OptimizeAsync` / `AutoOptimizeCandidateSequencesAsync`（`UI/GMATMainWindow.TabMGA.cs`）。
- `MGAMasterOptimizer` + `MGAHighValueParameters` **生产代码零引用**，仅测试 Scenario 9 使用。
- 实时拓扑生成 = `MGAMCTSTopologySelector.GenerateOptimalFlybySequences`（MCTS + 开普勒图 + 星历可达性剪枝），
  **不是** `GenerateCandidateSequences(MGAConfig)`（后者只服务 UI 候选列表）。
- 损失函数唯一调用点：`MGADifferentialEvolution.ComputeFitness`。
- 窗口并发默认 `maxEvalWindows = 2`（`MGAArchipelagoOptimizer.cs`）。
