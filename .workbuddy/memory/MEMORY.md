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
