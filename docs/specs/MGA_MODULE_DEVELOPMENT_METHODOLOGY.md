# MGA 模块开发方法（Development Methodology）

> 适用对象：`src/KSPGMAT/MGA/**` —— 一个包含混合整数 + 连续变量、多阶段（拓扑挑拣 →
> 星历可达 → DE 进化 → 精修）的复杂求解优化器。本文档是**开发/调试/回归**的操作手册，
> 而非算法说明（算法见 `MGA_MODULE_ARCHITECTURE_AND_IMPLEMENTATION.md`）。

---

## 0. 适用于本模块的三条总则

1. **一切数值只信原生**：ΔV / Lambert / 双曲飞掠 / MGA 转移**必须**由 `PykepBridge → PykepNative`
   （kep3）原生产生。C# 层只组装结构与挑选候选，**严禁自编公式冒充原生结果**（baseline §0.2 / §0.4）。
2. **可复现优先**：求解要能**逐位复现**，否则无法 A/B 回归。随机源必须走可注入种子，固定默认值。
3. **拆一步验一步**：超大方法/超大文件的改动，一律用小步纯搬运 + `dotnet build` + 定向回归，
   避免一次性动到核心求解链而不可逆。

---

## 1. 环境与可复现基线

### 1.1 原生引擎是否就位
离线跑任何求解前先确认：

```
[1] 原生动力学引擎: 已挂载 / 未挂载  (PykepNative.IsAvailable / Version)
[2] SPICE 内核状态:  已载入 / 未载入 (PykepNative.HasSpiceLoaded)
```

> 数值类 bug 排查**第一步**是确认原生 DLL 与 SPICE 已挂载——历史上出现过"原生未加载而静默回退"
> 的假象，在错误的基线上浪费时间。原生不可用应**明确报错**，而非自动回退托管公式（基线红线）。

### 1.2 种子注入约定
| 环境变量 | 默认 | 语义 |
|---|---|---|
| `KSPGMAT_MGA_RANDOM_SEED` | `42` | DE 随机种子；设 `0` = 按时钟随机（探索用） |
| `KSPGMAT_SURROGATE_GATE_SEED` | `20260911` | 代理筛选门种子；默认保持逐位可复现 |

调试时固定种子以复现同一失败态；需要拓宽探索时显式设 `0`。同一配置跑两遍应得到相同结果，
这是回归对比的**前提**。

---

## 2. 分层调试/测试入口（按怀疑半径从小到大）

测试基建在 `tests/MGADebugRunner`、`tests/MGABenchmarkRunner`；均在仓库根目录 `c:\...\KSP_GMAT` 下执行：

| 层 | 命令（示例） | 适用 |
|---|---|---|
| 秒级回归 | `dotnet run --project tests/MGADebugRunner -c Release -- --fast` | 每次改动后的最低门槛 |
| 定向用例 | `... -- --test <name>` | 怀疑某一子系统 |
| 全量模块 | `... -- --all`；列清单 `... -- --list` | 大版本回归 |

### 2.1 常用定向用例（`--test`）
| 用例 key | 对应 | 关注点 |
|---|---|---|
| `topologies` | `HistoricalTopologiesDiagnosisTest` | 拓扑/MCTS 生成的物理合理性与覆盖 |
| `blind_search` | `HistoricalBlindSearchSuiteTest` | 盲搜可持续性、不收敛死锁 |
| `history_bench` | `HistoricalBenchmarkMissionsTest` | 历史任务回放基准 |
| `scenario9` | `Scenario9ChallengeTest` | 4000 m/s 硬门禁验收 |
| `gpu_verify` | `GpuAccelerationValidationTest` | GPU 加速路径与 CPU 结果等价性 |
| `surrogate` | `SurrogateTrainingTest` | 代理网络训练/拟合 |
| `eval-*` | `--eval-cassini / --eval-juno / --eval-galileo / --eval-nh / --eval-messenger` | 对照真实任务 ΔV 同量级 |

### 2.2 最小爆点复现（`scratch/`）
`scratch/` 下约 48 个**独立实验工程**（如 `test_chains.cs`、`test_hyperbola.cs`、`test_mcts.cs`）。
需要验证某个式子/某条链/某个中间量时：
1. 照现有 scratch 工程样式新建独立 csproj（编译快、不污染主工程）；
2. 直接打印 `PykepBridge/PykepNative` 原生返回值，或某个中间层的输出；
3. 定位根因后，**把经验固化为 `tests/MGADebugRunner/TestSuites/` 下的断言用例**，防止回归。

---

## 3. 数值正确性——三把车企

1. **原生对照**：怀疑 ΔV 数值时，直接在 scratch 里调原生入口打印返回，**别信** C# 侧推导。
   对比 GPU/CPU 用 `--test gpu_verify` 保证两条路径一致。
2. **历史对标**：`--eval-*` 把求解结果与真实任务包络（Cassini/Juno/Galileo/NewHorizons/Messenger）
   做数量级对照，是"物理合理"的最快判据。
3. **金标真值**：`reports/test_reports/chain_truth_labels.csv`（3463 链真值）用于训练/命中真值链，
   判断筛选器是否漏掉或捏造可行拓扑。

---

## 4. 可观测性钩子

| 钩子 | 作用 |
|---|---|
| `MGAPipelineTelemetryProbe` | 四阶段管线遥测（PykepBridge/DE/PreFilter/GPU），收敛率、剪枝比 |
| `MGAStepProfiler` | **性能/卡顿/多核饥饿类问题优先看它**，按步骤分耗时 |
| `reports/test_reports/report_*.md` / `log_*.log` | 每次 `--test/--all` 自动导出，diff 两份报告即回归诊断 |

> 规则：能先"测量"再动手，不要凭感觉猜性能瓶颈在哪儿。

---

## 5. 开发工作流（针对巨型优化器）

1. **小步、纯搬运优先**：超大静态类/巨型方法，先做"局部函数→私有静态方法"、"方法→partial 文件"
   的**零逻辑**重构（见 §7）。每次重构后 `dotnet build` 0 错 0 警，并用 `git diff --numstat` 确认
   净改动是"等效置换"而非"悄悄删码"。
2. **回归节奏**：`--fast`（秒级）→ 定向 `--test`（如 `topologies`/`adaptive`/`blind_search`）→
   涉及数值再上 `--eval-*` 与金标对照。**纯结构改动不必跑全量**；数值改动才需要。
3. **限规模起步**：把 `maxRollouts` / `maxChains` / 种群代数压到秒级能跑完的最小值，
   先验证逻辑正确，再放大加压；性能问题回头看 `MGAStepProfiler`。
4. **固定种子复现坏例**：把失败输入与用例固化成 TestSuites 断言，避免回归。
5. **改前查记忆**：优先读 `.workbuddy/memory/*.md` 与 `docs/` 下的 `MGA_REFACTOR_STATUS_AND_ROADMAP.md`
   —— 记载了历次审计结论（如魔法数字治理、水星自减速、V→M DSM 压到 1.2 m/s 的修正），避免重蹈覆辙、
   也避免误删被文档强制保留的治理产物（`MGAEmpiricalRegistry`、`MGASurrogateGate`）。

---

## 6. 约束与红线（必须遵守）

- **禁止** C# 自算 ΔV / 质量流 / 开普勒推导并冒充 GMAT/PyKEP 原生结果。
- **禁止** 绕过 `SanitizeGmatScript` 推送脚本求解。
- **禁止** 绕过 `GMATStateManager` 直读活动飞船（`FlightGlobals.ActiveVessel.*`）。
- **禁止** 硬编码 `"Sat"` / 天体名 / `"LEOProp"` / `"RungeKutta89"` 等名称清单；清单以
  `gmat_capabilities.json` 注册表为真值源。
- **禁止** 删除/禁用 PyKEP 原生路径后静默回退托管公式。
- **数值红线**：一切动力学结论必须原生计算；C# 层仅允许纯粹的解析恒等式并**显式标注**
  （如 `CalcOrbitPeriodSec`、`GetSynodicPeriodDays` 属可保留的数学恒等/启发式）。
- 极值事件（`Apoapsis/Periapsis/Node/...`）**仅用于 Propagate**，严禁用于 Achieve。

---

## 7. 结构约定（降低行数、提升可维护性）

1. **一文件一职责**：一个顶层类型一个文件；一个巨型静态类按阶段拆成多个 `partial` 文件
   （如 `MGADifferentialEvolution` → `Generation / Evolution / Refinement` 三个 partial）。
2. **一方法一意图**：超过 ~200 行的方法优先拆。能"纯局部函数→私有静态方法"的最安全
   （捕获变量转参数，零逻辑改动）；依赖大量共享状态的，用**小步提取 + 逐次 build + 回归**推进。
3. **复用既有公式优先**：出现重复数学段时，先核对该公式是否已存在于本类/`MGAPhysicalScales`，
   **确认等价**（含半长轴/共振阶数定义是否一致）后抽取复用，杜绝逻辑分叉。
4. **常量命名 + 登记**：新增经验常数必须在源文件命 `public const` 并在 `MGAEmpiricalRegistry`
   登记（禁止裸字面量）；每次改动后**同步登记/更新基线文档**。
5. **构建门槛**：`dotnet build` 需 0 错 0 警；新增字段/控件/词条同步更新基线文档与 i18n 词条。

---

## 8. 快速排查手册（cheat sheet）

| 症状 | 先看 | 动作 |
|---|---|---|
| ΔV 数值离谱 | 原生引擎是否挂载、端点物理量 | scratch 调原生打印；对 `--eval-*` 历史量 |
| 拓扑永远缺某类构型 | 拓扑池是否被长链主导 | 拓扑生成侧诊断 + `--test topologies` |
| 收敛极慢 / 卡死 | `MGAStepProfiler` 各阶段耗时 | 检查多核饥饿 / GPU 提交路径；`gpu_verify` |
| 结果不可复现 | 种子注入是否失效（`KSPGMAT_MGA_RANDOM_SEED`） | 固定种子后再跑 |
| 改了大文件 | 净 diff 是否等效置换、构建是否 0/0 | `git diff --stat` + `dotnet build` + 定向回归 |
| 想删"没用"代码 | 是否被文档强制保留 / 是否被求解链引用 | 全库 grep 类型名，确认孤儿再删 |