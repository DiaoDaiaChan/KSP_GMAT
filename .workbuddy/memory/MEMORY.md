# KSP_GMAT 项目长期记忆

## 构建与验证（重要，每次改代码前先读）

- **可用命令**：`dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release --no-restore`
  - 直接 `dotnet build`（不带 `--no-restore`）会失败：`NuGet.targets(649,5): error : Value cannot be null. (Parameter 'path1')`。
    该错误是环境性的（NuGet 还原阶段），**不是代码问题**，用 `--no-restore` 绕过即可。
  - 加 `-t:Rebuild` 做全量重编译（约 4 秒）。
  - 目标框架 `net48`，引用 KSP 安装目录下的 Assembly-CSharp / UnityEngine / Principia / MechJeb / TWP。
    KSP 路径：`C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod`（存在）。
  - 输出：`src/KSPGMAT/bin/Release/net48/KSPGMAT.dll`，PostBuild 自动拷到 `GameData/KSPGMAT/Plugins/`。
- **基线红线**：`deploy.ps1` + `tools/ComplianceChecker` 要求 **0 警告 0 错误**。改动后必须确认 `warning CS` 计数为 0。

## 运行时验证（2026-09 打通）

```bash
# 历史任务盲搜 (13 个任务; 不带参数=全部)
tests/MGADebugRunner/bin/Release/net48/MGADebugRunner.exe --blind [voyager2|...]
# ML 代理模型训练管线 (产出 GameData/KSPGMAT/Config/mga_surrogate_weights.txt)
tests/MGADebugRunner/bin/Release/net48/MGADebugRunner.exe --train-surrogate
```

- 必须在**仓库根目录**执行（`PykepNative` 按 cwd 与 BaseDirectory 双重查找 `GameData/KSPGMAT/native`）。
- 原生库已齐备：`GameData/KSPGMAT/native/{kep3,pykep_native,openblas,libomp,...}.bin`，Steam 侧同样存在。
- ⚠️ **运行期间 KSPGMAT.dll 被进程占用，无法同时编译**。要先跑完再 build。
- **确定性基线（双套）**：
  - 修复前（仅 Stage 4 全局自愈）：`scratch/blind_p5.log`，13 任务全部收敛，**平均绝对误差 1537 m/s**，
    逐位可复现（P5–P12 共 7 轮验证含三轮重构 A/B，不一致 0/91）。
  - **修复后（逐链自愈，2026-09-12 起）**：`scratch/blind_selfheal.log` 与 `blind_selfheal2.log`，
    两次跑**逐位一致 13/13**（ΔV/ToF/拓扑），平均偏差降至 **+932 m/s**，**−2821 m/s 总改善**，
    **galileo 跨越工质合规线**（7218→6113 m/s）。
    **耗时代价 +157%**（102s→263s），可接受。
  - ⚠️ A/B 比对**优先用 selfheal2**（最新基线），与 selfheal1 13/13 一致证明确定性。
- ⚠️ **A/B 回归铁律**：自愈后求解器仍存在 ±3% 耗时浮动（RunWindow 浮点累加顺序），
  **但产物位级一致**——只比对 ΔV/ToF/拓扑，不比对耗时。
- 单任务快速验证：`--blind cassini`（约 11 s，对时序最敏感，适合复现性检查）。
- ⚠️ 后台跑盲搜时**会话中断会杀掉进程**（日志停滞且无进程残留即为此）——重启任务即可，勿误判为死循环。
- ⚠️ **盲搜 13 任务中 4 个（cassini / messenger / bepicolombo / newhorizons）的考核值来自空池兜底的
  约束超限解**（ToF 或工质超限）——与历史 ΔV 对比时不可直接比较，见审计报告 §20。
  Galileo 工质超限已通过逐链自愈修复（现 6113 < 6300）。
  全量真值数据集：`reports/test_reports/chain_truth_labels.csv`（3463 链，`--label` 生成，~77 min）。

## 拓扑选择链结构（2026-09-11 定稿，改选择逻辑前必读）

```
候选池(MCTS+开普勒图) → SelectDiverseTopologies(去重+深井直达门+Pareto序)
  → FilterAndPrioritizeReachableSequences(赤字门[实测恒不触发] + Deb可行性排序)
  → SelectQualityDiversityTopologies(7维连续相空间 QD, 16席=MaxSupportedChainBodies, λ=0.85)
```
- 离散流派标签（四桶/链长分档/深井门）已于 2026-09-11 全部移除，多样性完全由 QD 连续裁决。
- QD 遍历必须保持**固定列表序 + 布尔标记**（勿改回 HashSet 枚举，class 无值哈希）。
- 真正剩余的预设 = 数值权重（QD 质量分四系数、特征权重 {1,3,2,1.5,1,1,0}、λ），
  消除路径是**真值标注 + 有监督标定**（用 DE 逐链真值做标签），非手调。
- 真值数据集已建成（2026-09-11）：质量分对真值 DV 秩相关仅 −0.36（ΔV 权重 0.001 几乎失明）；
  估计器对直达转移 ToF 高估 2.2×（newhorizons 漏检根因）；QD 查全率 11/13。标定/门禁重建的
  (特征,真值) 对已就绪，见审计报告 §19–20。
- ⚠️ **`TopologyCategory` 是活代码，切勿删除**（2026-09-11 更正）。
  此前"4 处赋值已无消费者（死写入）"的判断**有误**：
  `MGAMCTSTopologySelector.cs:897` `directBucket = ...Where(c => c.TopologyCategory == "Direct")`
  → `:917` `directCand = directBucket.FirstOrDefault()` → `:920` 加入 selected，
  即"直达拓扑保留策略"（非深势阱目标时保留 Direct 首发名额）依赖它。
  删除会让 Direct 链失去首发保底，属功能回归。
  另注：`CandidateTopology` 是 **struct**，`FirstOrDefault()` 空桶返回 `default`
  (FlybyChain=null) 而非 null，故 `directCand.FlybyChain != null` 安全，**无 NRE 风险**。

## ToF 直达偏差修复（2026-09-11 末次提交）

- 位置：`MGAEphemerisReachabilityAnalyzer.cs:494-512`，`isGiantOutward` 分支后**新增** `r2/r1 > 1.5` 分支
- 公式：`legDays *= max(0.45, rRatio^(-0.20))`
- 物理机理：Hohmann (v∞=0) 系统性高估 ToF；DE 端 rRatio 越大越倾向 v∞>0 高能转移；
  拟合 13 任务真值数据集得 rRatio^-0.20，Pluto (rRatio=39.5) 处偏差从 +119% → +9.9%。
- 修复后 13 任务盲搜结果**逐位一致** P12 baseline —— QD 选择不变（Direct estDv 仍高 Pareto）。
- 修复是**特征侧精度提升**，非选择逻辑变更，符合"不引入回归"的保守原则。

## 逐链自愈（2026-09-12 起，当前生产）

- **位置**：`MGAAdaptiveSolver.cs:474`（霍曼基准提前到并行循环外）+
  `:674-682`（`Parallel.For` 内每条拓扑汇总前调 `AuditAndHeal`）。
- **机制**：原 Stage 4 全局自愈仅对最终解集 1 次（13 任务共 3 次成功）；
  现每条拓扑完成 DE 后立即自愈，**总自愈次数 3 → 50+**。
- **收益**：5/13 任务 ΔV 改善（−2821 m/s），galileo 跨越工质合规线（7218→6113 < 6300），
  8/13 任务零回归（位级一致）。
- **代价**：耗时 +157%（102s→263s）。可选优化：自愈内 `Math.Max(60, Pop)` + `Math.Max(120, MaxGen)`
  可下调到 40/60，验证能否保持收益。
- **确定性**：`RunWindow` 内部为确定性逆向打靶，无随机性；
  selfheal1 ↔ selfheal2 = 13/13 逐位一致（ΔV/ToF/拓扑），耗时 ±3% 浮动。
- **触发条件**：自愈在 `MGASelfHealingEngine.cs:95` 由 `needHealing = (isHighEnergyTrap || hasLawdenViolation || hasBPlaneSafetyViolation) && EnableSelfHealing` 决定，
  健康解 `needHealing=false` 直接 `return candidates` 不跑自愈，零成本。

## ⚠️ 两条实证教训（改代码前必读）

1. **先建立可信度量，再优化**。P4 为"ML 前置粗筛"做了 3 轮迭代（误杀判据对齐标签 → 阈值自标定 → 熔断保护），
   最后才发现靶子错了：原生 Lambert 只占"DE注种"阶段的 **1.4%**（155 ms / 11.09 s），
   门自身开销（2.1 µs/次 × 4.9 万次 = 100 ms）与要省下的调用**同量级**。
   ⇒ **不要在原生调用链上做预筛**；真正的 97% 在托管侧（前向步进几何构造、`FindAndRankResonantCombos` 笛卡尔积等）。
2. **改 ML 代码后必须核对"指标是否随改动变化"**。指标字节级不变通常意味着评估路径没走到网络
   （曾因 `Evaluate()` 调带 `IsTrained` 门控的 `Forward()`，验证集实测的是解析判据）。
   另：损失函数归一化**必须在对数域**处理 ΔV（重尾正量），否则 R² 为负。
3. **不要让"排序顺序"决定解**。`ConcurrentBag` 枚举序随线程调度变化 ⇒ 固定 `Random(42)` 也不可复现。
   所有"决定胜出解"的排序必须有**由解自身物理量导出的次级键**；并行归约必须**按索引显式指定顺序**。


## ⚠️ 自审报告可信度（2026-09-14 核验，重要）

`MGA_CODE_AUDIT_EMPIRICAL_AND_BENCHMARK_OVERFITTING.md`（2026-09-13，自称 46 项 100% 清零）
经独立逐行核验**不成立**：重新定位 **128 处**（P0 23 / P1 63 / P2 42）。
详见 `MGA_MAGIC_NUMBERS_AUDIT_VERIFICATION_2026-09-14.md`（仓库根目录）与 `.workbuddy/memory/2026-09-14.md`。

最需记住的三件事实：
1. `MGABenchmarkSuite.cs:44` `BlindSearchEvaluationMode` 默认 `false`，**全树无调用方置 true**
   ⇒ 基准测试永远走 `:486-495` 的答案注种（历史发射日 + `HistoricToFDays` + 借力序列直接进染色体）。
   任何"求解器复现历史任务"的结论在此之前都不可信。
2. **`MGADifferentialEvolution.cs` 曾被发现是反编译覆盖件 —— 已从 git 恢复，源码本身无问题。**
   2026-09-14 核验经历：工作区该文件当时为未提交的**反编译覆盖件**（3,094 行 / 1,052 处 `numNN`，
   `git diff --stat` = `3214+/2327-`，来源 `scratch/decompiled_de.cs`），覆盖了 `f089664`/`7127bc1`
   两次清理提交之后的手写源码。`git show HEAD:<file>` 为 **2,326 行干净手写实现**（0 反编译特征，
   无任何 `Mercury` 逻辑特判，无 `1.24/1.15/0.999/1.3333333333333333/7.5/13.5`，已用比例化判据
   `CircuitBreakerGenFraction` 等）。**已恢复**（`git show HEAD:<file> > <file>`）。
   ⇒ 当时基于它得出的"Mercury 硬编码 6 处""DE 需源码级重构"等结论**全部作废**；
   前一份自审报告针对该文件的 §1.4/§1.5/§1.8 声明**成立**。
3. **无量纲化只改造一半**：`MGAPhysicalScales` 已建成 `AssistPowerIndex` / `RobustBand` 体系，
   但 `MGAEphemerisReachabilityAnalyzer.cs:648` 仍是 `Mu >= 5.0e6 || RadiusKm >= 20000.0`
   （即 `MGAPhysicalScales.cs:301` 声称"已取代"的那行，仍在到达捕获 ΔV 主结算路径）；
   同类残留 `:837` `SMA*0.45`、`MGAMCTSTopologySelector.cs:2087` `Mu>=500.0` / `:2108` `Mu>=1000.0`。

**写治理报告的铁律**：不得把"提成具名常量""换成另一组绝对值""换成数学等价分支"记为"已清零"。
每条须附 (a) 非太阳系/非测试任务上的实测行为差异；(b) 新常数推导出处；
(c) grep 证明同语义旧常数全树无残留。反例：`Mu>=5.0e6`、`Mu>=500.0`、`SMA*0.45`、
`MGAFlybyDynamicsHelper.cs:288` 的 `150.0/6.5`（报告称已删、实际原样保留）四项皆是"新体系已建、旧实现未删"。

### 修复已落地（2026-09-14，未编译未测试）

产出 `MGA_MAGIC_NUMBERS_FIX_RECORD_2026-09-14.md`；新建 `src/KSPGMAT/MGA/MGAEmpiricalRegistry.cs`。
**修复三原则（后续沿用）**：
- **A 类**（天体名/任务名/历史构型/测试场景写死的分支与阈值）→ 一律改为物理推导或 `MGAPhysicalScales` 无量纲判据。
- **B 类**（未标定的经验系数）→ 命名化 + 登记进 `MGAEmpiricalRegistry`（附失效条件与替换路径），
  **不擅自改值** —— 改值只是"换一个拍定的数"，正是前报告的错法。
- **C 类**（物理常数 / 数值保护）→ 不动。

要点速查：
- 基准/盲搜的**答案注种与历史锚定已拔除**：`BlindSearchEvaluationMode` 默认 `true`；历史真值移出盲搜目标函数；
  两者的搜索窗改为"发射机会年份"年粒度（新增 `ResolveBlindDepartureWindow` / `ResolveBlindOpportunityWindow`）。
- 天体名硬编码已清零：新增 `DeepWellDiveEnergyGapRatio`、`IsEffectiveAssistSource`、
  `SelfResonanceEgaVInfKms`（地球出发 2:1 泵升 v∞ = 3.21 km/s）、`ResolveRequiredDeepWellResonanceOrder`。
- 共振比下限统一为低阶有理槽 `1:1 / 5:4 / 4:3`（因 `ResonanceRatioCandidates` 在 1.0 与 1.25 之间无槽，
  **筛选结果与原实现逐位一致**）；`5478.75` 天还原为 15 儒略年；`2500 天` 分档改为无量纲半长轴比。
- **`MGADifferentialEvolution.cs` 已从 git 恢复**（见上文第 2 条），源码本身无需重构；
  之前"它是反编译产物"的判断是工作区污染造成的误判。
- **新增红线（审计前置）**：审计/修复任何代码前，必须先做**工作区污染检查** ——
  对比 `git show HEAD:<file>` 与工作区的反编译特征计数（`numNN/arrayNN/textNN`）与 `git diff --stat`
  （insertions ≈ deletions ≈ 文件总行数 ⇒ 整文件被替换）。`HEAD=0 而工作区>0` ⇒ 先恢复再评估。
  已固化为 skill：`~/.workbuddy/skills/source-tree-integrity-check/SKILL.md`。
- 新增经验常数必须命名 + 登记进 `MGAEmpiricalRegistry`，禁止再做无命名的裸字面量。

### 应试作弊排查（2026-09-14，产出 `MGA_EXAM_CHEATING_AUDIT_2026-09-14.md`）

**四条必须记住的事实**：
1. **作弊主要在考核侧**：23 处中 **19 处在 `tests/MGADebugRunner/`**，仅 4 处在 `src/`。
   以后审计**必须同时扫 tests/**，只扫 src 会漏掉绝大部分。
2. **有 4 个测试是"恒真"的，不可能失败**：
   `CassiniEvaluationTest.cs:21` 直接 `ctx.EndCase(true)`（无条件通过）；
   `MessengerEvaluationTest.cs` / `NewHorizonsEvaluationTest.cs` 的 `Run` 里 `bool ok = true` +
   `try{EvalXxx();}`（返回值被丢弃）⇒ **只有抛异常才算失败**；
   `HistoricalMissionsDebuggerTest.cs:19` `passed > 0` ⇒ 5 任务过 1 即 PASS。
3. **`MGABenchmarkSuite.cs:506` `FlybySequence = testCase.FlybySequence;`** —— 基准套件**两种模式**
   都把历史拓扑交给求解器（"盲搜"只对发射时刻盲、对拓扑不盲）。
   真正拓扑盲的只有 `MGAHistoricalBlindSearchSuite`（`FlybySequence` 严格为空）。
4. **`MGAHistoricalBlindSearchSuite.cs:893-897` 报表生成器无条件写入 3 条成功结论**，
   其中"所有飞掠脉冲严格 0.00 m/s"与其**紧邻上方表格**的 `⚠️ 需机动` 列自相矛盾 —— 预写结论。

**判据可证伪性检查（新增铁律）**：不只看判据是什么，要看它**有没有可能失败**。
`HistoricalMissionsDebuggerTest.cs:80` 的 `totalDv <= MaxExpectedDvMs * 1.35` 之所以是恒真式，
是因为 `:59` 已把求解器预算设成同一个 `MaxExpectedDvMs`（`MGAMasterOptimizer.cs:361` 作为硬约束生效）。
**只看 `* 1.35` 会误判为"容差略宽"，必须结合预算来源才能看出它什么都不检验。**

**其他**：`ApplySolutionToConfig`（`MGAAdaptiveSolver.cs:1166`，±10% 航时夹逼）当前 **0 调用者**（死代码，
即报告声称已清除的"±10% 夹逼"的真实形态）；`MGABenchmarkSuite` 的历史 ΔV 区间宽度 900~11800 m/s（13 倍差），
因 PASS 判据是纯包含关系 ⇒ **区间宽度 ≡ 判定强度**，Rosetta `3800–15000` 等无出处的宽区间近乎无效。
**同一事实多真值源是危险信号**：三张真值表对 Voyager 2 发射日（`20 Aug 1977` vs `01 Aug 1977`）、
Cassini 末段 ToF（`1279` vs `1278`）互不一致。

### 应试作弊修复已完成（2026-09-14，产出 `MGA_EXAM_CHEATING_FIX_RECORD_2026-09-14.md`）

**✅ 已编译验证：主工程 + 测试工程均 0 警告 0 错误；`--test galileo` / `--test juno` 运行时通过。**
上述全部 P0/P1/P2 项已修复（**实际 25 处**，审计报 23 + 遗漏的 Galileo/Juno 2 处同型恒真判定）。

**新增的考核系统契约（改测试前必读）**：
- `tests/MGADebugRunner/EvalSupport.cs` 定义 `EvaluationOutcome`（Converged / AllLegsFeasible / TotalDvMs /
  BudgetMs / IsHistoricReplay / Passed）与 `EvalWindows`（`OpportunityYearWindow` 年粒度机会窗 /
  `TopologyContainsAll` 拓扑重现判据）。
- **铁律**：评测函数必须回传 `EvaluationOutcome`，由 `Run` 依显式阈值裁决；
  **评测路径内禁止出现 `EndCase(true)`**，禁止 `passed > 0` 之类"N 中过 1 即 PASS"。
- **搜索窗一律用 `EvalWindows.OpportunityYearWindow`**（`[该年 1/1, 次年 1/1)`），
  禁止 `launchUT ± N 天`（历史发射日邻域，最优解恰在窗口中心）。
- 历史真值只能用于**事后比对**；用于回放时输出必须带 `[回放]` 标签，且不得作为求解器能力证据。

**三条新教训（血泪）**：
1. **修复必须编译**。上一轮"未编译"留下了 `CS1003`（`MGAEmpiricalRegistry.cs:166` 未转义内嵌双引号）——
   一份看起来完整的修复记录里可以有编译不过的文件。**编译通过是"已完成"的底线。**
2. **把审计清单当"样例"，用模式扫全量**。审计点名了 Cassini/Messenger/NewHorizons 的恒真判定，
   但同族的 Galileo/Juno 完全同型却被漏掉（没做 `grep 'EndCase('` 全量扫描）。
3. **注意"派生泄漏"**。`MGAConfig → MGAAdaptiveSolverConfig` 的 `FromMGAConfig` 会**派生**
   `LegToFBounds`（从 `FlybySequence` 推出）。清源头字段必须在**转换之前**，否则派生字段仍带答案。
   另：`BenchmarkCase.FlybySequence` **含终点**（末元素即目标），误传给
   `AutoDeriveResonantLegBounds` 的第三参数（语义=中间天体）会导致末段重复。

**遗留（未修，见修复记录 §七）**：`Scenario9ChallengeTest:54` 仍注入 `{"Mercury"}`（声明式场景输入）；
`MGABenchmarkSuite` 22 组历史 ΔV 区间仍**无出处**（已加宽度审计警告）；GPU 用例同一 `nominalLoss`
在 Case2 标 `m/s`、Case3 标 `km/s`（量纲矛盾未判定）；Galileo/Juno 回放总 ΔV 偏离真实值 5~7 倍
（属求解精度问题，非作弊，建议单独立项）。

### `MGABenchmarkSuite` 三态模式（2026-09-14，改基准前必读）

`enum MGABenchmarkMode { HistoricReplay, HistoricTopologyTrajectory /*默认*/, TopologyBlind }`
取代已删除的布尔量 `BlindSearchEvaluationMode`。**默认是"给定历史拓扑的轨迹优化基准"（非盲搜）**，
发射窗/航时边界仍全部物理推导。三处（类注释、报表首行、汇总）都显式标注模式。

- **解选择绝不能取 `[0]`**：`MGAAdaptiveSolver.Solve` 的候选解按**候选拓扑索引/窗口顺序**展开，
  非质量排序；取 `[0]` 会让所有任务退化为单航段直达（Cassini 直达 39688 m/s）。
  必须按解自身物理量择优（总 ΔV → 总航时 → 航段数）。
- 实跑 `--test history_bench` = **9/16**，默认模式产出**真 MGA 解**（Mariner 4206 ✅ / Voyager2 7735 ✅ /
  Cassini 7769 ❌ / Galileo 6409 ❌ / BepiColombo 3744 ✅）。单次约 **3.5 分钟**。
- ⚠️ **航时包络 `AutoDeriveResonantLegBounds` 不自洽**：Mariner 10 需 509d 而包络仅 483d；
  NH 的 `Jupiter→Pluto` 单段却被推到 **67027 d（183 年）**（纯弹道霍曼尺度，似未计木星弹弓增益）。
  曾试加"航时不超包络"闸门 → **使结果变差**（Mariner 10 ✅4206→❌15768），已撤回。
  **修 `MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds`，不要再拍阈值。**
- 遗留：Apollo 11 选出 209762 天 / 749 m/s 退化解（Continuity=False）；`Mars Express` / `GTO➔GEO`
  无中间借力天体，本质是直达基准，混在 MGA 列表属分类问题。
- ⚠️ `tests/MGADebugRunner/TestSuites/*.cs` **全部 untracked（从未提交）**，
  无法做 `git show HEAD:` 反编译特征对比。

### 航时边界与"软约束"陷阱（2026-09-14 第二轮，改求解器前必读）

**核心事实：航时包络在代码里是"软惩罚"，不是硬约束。** `TotalToFMaxDays` 只进
`MGAPhysicsLossEngine` 的 `MGAConstraint.MissionDuration` 惩罚项；求解器完全可以在其外取值，
`PykepBridge.Optimize` 返回的 `sols[0]` 可能就是超界解。实测 GUI 预设的 New Horizons 返回 **49950 天**。

**在写任何"边界"代码前必须先确认它是否真的约束**：
- `AutoTuneToFBounds` 在 `MGAAdaptiveSolverConfig.FromMGAConfig` 中**硬编码为 true**，
  且原实现**无条件覆盖** `TotalToFMinDays` ⇒ 经 `MGAConfig` 进入的所有路径
  （GUI 预设 / 基准套件 / 盲搜套件）传入的航时**下界全部被静默丢弃**。已修为 fill-in 语义。
- 判断一个边界是否生效，**不能看代码里有没有赋值**，要看它是否进入目标函数/硬门禁。

**`SolarPlanet.SMA` 是相对该天体自身母体的半长轴，不是相对任意中央天体。**
任何"把 SMA 配上某个 μ"的代码都必须先确认参考系一致。参考实现：
`MGAFlybyDynamicsHelper.ResolveOrbitalRadiusKm`（行星→根恒星；卫星→ParentName；
天体==中央天体→停泊轨道半径；不可解析即**拒绝**）。
反例（实测）：μ_Earth 配 Earth 的日心 1 AU + Moon 的地心 SMA ⇒ 地月航时上界 **53.8 年**。

**航时上界的唯一真值源**：`MGAFlybyDynamicsHelper.MaxTransferToFYears = 15 儒略年`。
异星转移段与**同星共振段**都必须施加它 —— 共振路径原为 `tOrbitDays × 6.5` 且**无上限**
（冥王星 1599 年）。`--test tof_bounds` 是它的可证伪守门（PASS ⟺ 全部航段 ≤ 20 年）。

**未识别天体不再伪装成地球**：`GetStandardPlanetFallback` 兜底原返回地球全套常数（1 AU/μ_E/R_E），
任何自定义天体名都会静默变成地球。现返回全零 + `SolarPlanet.IsOrbitUnresolved`，消费侧必须拒绝。
**禁止再写 `p.SMA > 0 ? p.SMA : MGADefaults.AU_KM` 这类静默替代。**

**两个统计约定取代了绝对比例阈值**：`LowerQuantileLevel = 0.25`（深势阱目标判据）、
`DeepWellExitShellSteps = 2`（反梯级深井），壳层比由 `(SMA_max/SMA_min)^(1/(n-1))` 自导出。
**它们是统计学/工程约定，不是物理常数，但不需要随恒星系更换。**

## 架构约定

- `src/KSPGMAT/MGA/` 下所有文件统一使用 **扁平命名空间 `KSPGMAT.MGA`**（含 `Solver/`、`AI/`、`Telemetry/` 子目录），
  `AI/` 子目录例外，用 `KSPGMAT.MGA.AI`。新增文件请遵循。
- `MGADefaults` 定义在 `Model/MGAStateManager.cs`（不是独立文件），属性位于 `KSPGMAT.Model`，
  `MGA` 命名空间下使用时需 `using KSPGMAT.Model;`。
- 两个不同的配置类，勿混：
  - `MGAConfig`（`Model/MGAStateManager.cs:799`）：底层，含 `MaxSwingBys/MaxResonances/MaxBackLegs/MaxBackSpacing`。
  - `MGAAdaptiveSolverConfig`（`MGA/Solver/MGAAdaptiveSolver.cs:57`）：高层，含 `DeparturePlanet/ArrivalPlanet`。
- 动力学计算**只能**委托 `PykepNative` / `PykepBridge`（基线 §7「零托管数学」）。
  C# 层可以做无量纲化、判据、统计与优化，但不得自编开普勒/兰伯特公式冒充原生结果。

## MGA 求解链真实入口（2026-09 核实）

- UI 走 `MGAAdaptiveSolver.OptimizeAsync` / `AutoOptimizeCandidateSequencesAsync`（`UI/GMATMainWindow.TabMGA.cs`）。
- `MGAMasterOptimizer` + `MGAHighValueParameters` **生产代码零引用**，仅测试 Scenario 9 使用。
- 实时拓扑生成 = `MGAMCTSTopologySelector.GenerateOptimalFlybySequences`
  （MCTS + 开普勒图 + 星历可达性剪枝），**不是** `GenerateCandidateSequences(MGAConfig)`（后者只服务 UI 候选列表）。
- 损失函数唯一调用点：`MGADifferentialEvolution.ComputeFitness`。
- 窗口并发默认 `maxEvalWindows = 2`（`MGAArchipelagoOptimizer.cs`）。
