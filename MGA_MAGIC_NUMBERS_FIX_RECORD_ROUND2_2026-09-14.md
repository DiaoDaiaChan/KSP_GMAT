# MGA 硬编码 / 魔法数字修复记录 · 第二轮 (2026-09-14)

> **对应审计**：`MGA_MAGIC_NUMBERS_AUDIT_VERIFICATION_2026-09-14.md`（128 处）
> **上一轮修复记录**：`MGA_MAGIC_NUMBERS_FIX_RECORD_2026-09-14.md`（P0 全量 + 高价值 P1，遗留 6 条）
> **本轮范围**：上一轮遗留的 4 条待办 + 本轮新发现的 3 类机制缺陷
> **编译验证**：主工程 + 测试工程均 **0 警告 0 错误**
> **新增可证伪探针**：`--test tof_bounds`（修复前 ❌ / 修复后 ✅）

---

## 零、本轮的核心产出：把"原始代码的不足"变成可量化的证据

上一轮的遗留项都是"知道不对，但说不清有多不对"。本轮先造了一个**可证伪的探针**
（`tests/MGADebugRunner/TestSuites/ToFBoundDiagnosticsTest.cs`，`--test tof_bounds`），
把航时边界推导的荒谬程度**量化**出来，再动手改：

判定契约：**PASS ⟺ 全部受检航段的 `minDays > 0` ∧ `maxDays ≥ minDays` ∧ `maxDays ≤ 20 儒略年`**。
20 年是**显式声明的工程理智上界**（不是从任何倍数里隐式推出来的），且探针同时打印"任务级包络合计"
作为**不掩盖的指标**（该指标不由单段包络负责，应由任务级 `TotalToFMaxDays` 约束）。

**修复前实测（❌）**：

| 指标 | 修复前 |
| :--- | :--- |
| 受检航段总数 | 316 |
| **世纪尺度航段 (> 20 年)** | **86** |
| **最长单段航时上界** | **357.5 年** — Neptune➔Pluto (130570 天) |
| 跨参考系被静默混算的配对 | 162（产出伪边界，无任何标记） |
| `maxDays / 霍曼时间` | 恒为 **3.50**（根因指纹） |

超限清单（节选）：`Jupiter➔Pluto 183.5 年`、`Saturn➔Neptune 154.7 年`、`Uranus➔Pluto 276.8 年`、
`Neptune➔Uranus 214.4 年`；任务级：`Voyager 2 包络合计 349.4 年`、`New Horizons 187.6 年`、`Cassini 50.8 年`。

**修复后实测（✅）**：

| 指标 | 修复后 |
| :--- | :--- |
| **世纪尺度航段** | **0** |
| **最长单段航时上界** | **15.0 年**（= 显式工程上限 `MaxTransferToFYears`） |
| 跨参考系配对 | 162 个转为**显式拒绝**（返回带标注的保守回退，而非伪边界） |
| Voyager 2 包络合计 | 349.4 年 → **49.1 年** |
| New Horizons 包络合计 | 187.6 年 → **19.1 年** |
| Cassini 包络合计 | 50.8 年 → **30.6 年** |
| Apollo 11 (地月) 单段 | **53.8 年 → 0.04 年 (15 天)** |

---

## 一、A 类：航时包络推导（本轮最高价值）

### 1.1 `DerivePhysicalInterplanetaryBounds` —— 上界结构本身是错的

**原实现**（`MGAFlybyDynamicsHelper.cs`）：

```csharp
double maxFactor = Math.Max(depMaxFactor, OuterTransferToFMaxFactor);      // 恒 = 3.5
bool isDeepSpaceTransfer = (a2 / a1) > DeepSpaceTransferRadiusRatio;      // 门: a2/a1 > 10
double deepSpaceCapDays  = DeepSpaceCruiseToFCapYears * DaysPerJulianYear; // 15 年
double maxDays = isDeepSpaceTransfer
    ? Math.Min(tHohmannDays * maxFactor, Math.Max(deepSpaceCapDays, tParabolicDays * 1.25))
    : Math.Max(minDays + MinLegToFSpanDays, tHohmannDays * maxFactor);
```

三处独立缺陷：

1. **非深空分支没有任何绝对上限** —— `maxDays = 霍曼时间 × 3.5`。而霍曼时间本身 ∝ (a1+a2)^1.5，
   于是木星→冥王星（霍曼 ≈ 53 年）得到 **183.5 年**。
2. **`Math.Max` 实现的"上限"不是上限** —— 深空分支写成 `Math.Max(cap, tParabolic×1.25)`，
   于是 `tParabolic×1.25` 可以把 15 年的 cap **抬高**（月球→冥王星被抬到 23.1 年）。
   一个用 `Max` 组合出来的 "cap" 在数学上不可能起约束作用。
3. **`a2/a1 > 10` 的比值门让恰好漏网者免于上限** —— 地球→土星比值 9.58，落在门外，
   因此拿到无上限的 3.5 倍霍曼 = **21.3 年**。

**修复**：对**出发段与中段一视同仁**地施加"两项取小"：

```csharp
double maxTransferDays = MaxTransferToFYears * DaysPerJulianYear;   // 显式年数, 非隐藏倍数
...
double maxDays = Math.Min(tHohmannDays * Math.Max(depMaxFactor, OuterTransferToFMaxFactor), maxTransferDays);
```

并删除 `DeepSpaceTransferRadiusRatio` 与其分支（其意图已被统一的年限上限覆盖，且原实现是坏的）。

**取值依据**（写入代码注释）：现实中最长的**单航段** MGA 转移是 New Horizons 的木星→冥王星 ≈ 8.4 年；
取 15 年给出 ≈1.8 倍余量。标注为 **[工程口径·待标定]** —— 但它是一个**显式声明的年数**，
而不是藏在"霍曼 × 常数"里隐式产生。

### 1.2 `DerivePhysicalResonantBounds` —— 共振路径完全没被约束（新发现）

同星共振腿的上界原为 `tOrbitDays × max(ratioMax, 6.5)`，**没有任何绝对上限**。
探针 A3 段实测：

| 天体 | 公转周期 | 航时上界 | 折合 |
| :--- | :--- | :--- | :--- |
| 土星 | 10839 d | 70453 d | **192.9 年** |
| 天王星 | 30802 d | 200211 d | **548.1 年** |
| 海王星 | 60334 d | 392170 d | **1073.7 年** |
| **冥王星** | 89866 d | **584130 d** | **1599.3 年** |

**这是 GUI 预设路径产出 49950 天退化解的直接来源** —— 该路径允许求解器在共振腿内自由取点。

**修复**：施加与异星转移段**同一条**上限；并处理长周期天体的下界压缩
（冥王星的物理下界 0.65×周期 = 58413 天本身就超上限，故把下界压到 `上限 / ToFMinSpanFactor`
以保持 `min < max` 且区间不塌缩为一点 —— 塌缩会使下游 ToF 采样出现 0/0）：

```csharp
double capDays = MaxTransferToFYears * DaysPerJulianYear;
maxDays = Math.Min(maxDays, capDays);
double minCeiling = capDays / ToFMinSpanFactor;
if (minDays > minCeiling) minDays = minCeiling;
if (maxDays < minDays * ToFMinSpanGuard) maxDays = Math.Min(capDays, minDays * ToFMinSpanFactor);
```

### 1.3 **参考系混用**（本轮最深的缺陷，超出"魔法数字"范畴）

`DerivePhysicalInterplanetaryBounds` 直接用 `fromPlanet.SMA` / `toPlanet.SMA`。
但 `SolarPlanet.SMA` 是**相对该天体自身母体**的轨道半长轴，而函数拿到的是**中央天体**的 μ。
两者不同参考系时结果在物理上无意义，且**不会报错**，只是给出巨大数字。

**实测**（用正确的中央天体跑探针后暴露）：

```
Apollo 11 (Earth ➔ Moon), 中央天体 = Earth (由 ResolveTransferCentralBody 给出):
    a1 = Earth.SMA = 1.496e8 km   ← 这是**日心**半长轴
    a2 = Moon.SMA  = 3.844e5 km   ← 这是**地心**半长轴
    μ  = μ_Earth = 398600.44
  ⇒ 航时上界 = 19649 天 = 53.8 年
```

（注：上一轮探针写死 `central = "Sun"` 所以没发现；本轮改用
`SolarSystemCatalog.ResolveTransferCentralBody(...)` 后立刻暴露。）

**修复**：新增 `ResolveOrbitalRadiusKm(SolarPlanet, centralName)`，按目录约定解析——
行星以根恒星为参考系、卫星以 `ParentName` 为参考系、天体即中央天体时用**停泊轨道半径**
（"绕自身的半长轴"无意义）；**任一不可解析即拒绝**并返回带标注的保守回退值。
`DerivePhysicalResonantBounds` 同步加同一道门。

**效果**：地月航时上界 **53.8 年 → 0.04 年 (15 天)**；162 个跨参考系配对由"静默伪边界"转为"显式拒绝"。

### 1.4 死代码

`DerivePhysicalResonantBounds` 中的
`if (maxDays < minDays * ResonantToFMinSpanGuard) maxDays = minDays * ResonantToFMinSpanFactor;`
—— 上一行的 `Math.Max(minDays * ToFMinSpanFactor, …)` 已保证 `maxDays ≥ 1.5 × minDays`，
故该守卫**永不可能为真**。已删除。同时把 `ResonantToFMinSpanGuard/Factor` 改名为中性的
`ToFMinSpanGuard/Factor`（异星转移段也需要同一守卫）。

---

## 二、P0：未识别天体"伪装成地球"

**原实现**（`SolarSystemCatalog.GetStandardPlanetFallback` 的最后一行）：

```csharp
return (149598023.0, 0.0167, 0.0, 398600.4418, 6378.14);   // ← 地球
```

即：**任何不在后备表里的天体名，静默变成地球**（1 AU、μ_Earth、R_Earth）。

后果（这正是上一轮遗留第 3 条所指）：在 Kerbol 缩尺星系 / JNSQ / 自建星球包下，
所有自定义天体的轨道半径、引力常数、半径与地球**完全一致** ——
由此算出的航时、ΔV、共振阶数全部是伪值，**而且没有任何断言能发现它**
（因为"地球值"本身自洽，误差不会表现为异常）。

**消费者侧还有第二处**（`MGAPhysicsLossEngine.ResolveNominalTofDays`）：

```csharp
double rF = (pF != null && pF.SMA > 0) ? pF.SMA : MGADefaults.AU_KM;   // ← 又静默代入 1 AU
```

**修复**：
1. 后备库兜底改为返回全零（`SMA ≤ 0` ⇒ 可被识别）；
2. `SolarPlanet` 新增 `IsOrbitUnresolved` 标志，并在置位时输出 `KSPGMATLog.Warn`；
3. `ResolveNominalTofDays` 删除 `?? AU_KM` 的静默替代，改为**不做判定**（返回 0，由调用方跳过航时项）。

已登记进 `MGAEmpiricalRegistry` 并标记为 `Resolved`。

---

## 三、C 类：优化目标 ≠ 选解排序（同一软界两处不同实现）

`1.20 / 0.15 / 0.15` 这个航时软界原在**两个地方各写一遍，且形式不同**：

| 位置 | 原实现 | 单位 |
| :--- | :--- | :--- |
| `MGAPhysicsLossEngine.ComputeLoss` | `loss  += 0.15·e + 0.15·e²` | **预算单位**（加性） |
| `MGASolutionOrdering.SelectParetoKneeSolution` | `dvKms += (0.15·e + 0.15·e²) · dvKms` | **解自身 ΔV**（乘性） |

⇒ 优化器最小化的目标与最终选解所用的排序**不是同一个函数**，同一个解在两个阶段的相对优劣可能相反；
且乘性形式**对高 ΔV 解施加更大的绝对惩罚**，与"压制低 ΔV 长航时退化解"的初衷方向相反。

**修复**：收敛为单一真值源 + 统一为加性（预算单位）：

```csharp
// MGAPhysicsLossEngine
public const double ToFSoftBoundMultiple = 1.20;
public const double ToFSoftPenaltyLinear = 0.15;
public const double ToFSoftPenaltyQuadratic = 0.15;
public static double ToFSoftExcessPenalty(double actualDays, double nominalDays) { ... }
```

- 损失侧：`loss += ToFSoftExcessPenalty(sol.TotalFlightDays, nomTofDays);`
- 选解侧：`s.TotalDeltaVKms + ToFSoftExcessPenalty(...) * penaltyScaleKms`
- 新增 `LossReferenceCache.Get(MGAAdaptiveSolverConfig)` 重载，使 `MGAAdaptiveSolver` 路径
  也能取到与损失函数**同一个** `dvRef`（否则该路径只能退化为"用解集中位 ΔV 当尺度"）。

### 3.1 `ResolveNominalTofDays` 的命名化（含一处**无法无量纲化**的诚实标注）

`2500.0 / 1.15 / 0.55 / 1.5 / 10.0` → `NominalToFDeepSpaceThresholdDays` /
`NominalToFFastCruiseFactor` / `NominalToFParabolicFactor` / `NominalResonantLegPeriodMultiple` /
`MinNominalToFDays`。

其中 **`2500` 无法无量纲化，且本轮明确不做"换成另一个拍定的比值"**：
审计尝试过把它改成比值判据，但该分支的语义是"当霍曼时间已长到不再是有意义的单弧标称值时改用抛物线基准",
而"长到不再有意义"本身就是一个**工程口径的时间量**；实测改成比值会把地球→金星的标称航时压到 13 天，
导致全量误罚。故选择**显式命名 + 记录出处 + 登记待标定**。

---

## 四、D 类：绝对比例 → 系统内相对判据

上一轮遗留第 4 条的两处：

| 原常量 | 值 | 问题 | 新判据 |
| :--- | :--- | :--- | :--- |
| `ShallowWellPotentialFraction` | 0.03 | 天体势阱深度比 `(μ/r_safe)/(μ_c/a)` 的**绝对**阈值 | `ClassifyDeepWellTargetBySystemDistribution` — 落在系统内全部天体该比值的**下四分位**以内 |
| `DeepWellExitSmaRatioThreshold` | 0.45 | 最内天体/出发天体 SMA 的**绝对**比例 | `ClassifyDeepWellExitByShellSteps` — 深入至少 **2 个系统内轨道壳层**（壳层比 = `(SMA_max/SMA_min)^(1/(n-1))`，Sol ≈ 1.588） |

新增基础设施（`MGAPhysicalScales`）：`Quantile`、`WellDepthRatio`、`ShellStepRatio`、
`LowerQuantileLevel = 0.25`、`DeepWellExitShellSteps = 2.0`、`InsufficientSystemSampleSmaRatioFallback = 0.45`。
后三者中前两个是**统计学/工程约定**（与已有的 `OutlierRobustK = 2.0` 同类），不是物理常数；
最后一个只在系统样本不足时使用并显式标注为回退值。

**行为校验（Sol 系，与原绝对阈值逐一致）**：

- 势阱深度比：水星 0.0038、火星 0.021 → 落于下四分位 (≈0.032) 以内 ⇒ 深势阱 ✓；
  金星 0.043、地球 0.068、巨行星 4.8~10.5 ⇒ 非深势阱 ✓
- 壳层：阈值 ≈ 0.397·depSMA（原 0.45）；水星(0.387 AU)对地球出发仍为真、地球自身仍为假 ✓

两个原常量已**删除**，登记项状态改为新增的 `Resolved`（保留审计痕迹）。

> 注：上一轮提及的 `DeepWellDiveEnergyGapRatio(0.6)` **在反编译件恢复清理时已一并移除**，
> 代码中已不存在（只剩一条陈旧注释，见 §六 已修）。

---

## 五、E 类：**撤回上一轮审计的一条错误结论**

上一轮审计第 5 条称：

> `MGAPhysicsLossEngine` 三个"规范阈值"（`FeasibleLossCeiling` / `DeepInfeasibilityLoss` /
> `SelectionLossCeiling`）：注释称其为"预算倍数"，但代码中无任何输入去乘预算 —— 应让阈值真正随 `dvRef` 缩放。

**该结论不成立，本轮撤回。** 证据：`ComputeLoss` 内主目标在归一化阶段就已除以参考预算——

```csharp
double dvRefKms = LossReferenceCache.Get(config);
double loss = subjectDvKms / dvRefKms;      // ← 损失本身已是"预算倍数"
```

因此 `1.0 / 100.0 / 4.0` 这三个阈值**本来就是以参考预算为单位**的，注释与代码一致。
"没有代码去乘预算"看的是阈值表达式，漏看了它的定义域已被归一化。
**这是一个"只看常量、不看量纲"的误判**，与上一轮报告的错法（把等价变换当成已清零）互为镜像。

---

## 六、F 类：本轮新发现（超出魔法数字范畴的机制缺陷）

### 6.1 `AutoTuneToFBounds` 无条件覆盖调用方的航时下界

```csharp
// MGAMCTSTopologySelector.CalculateAdaptiveToFBounds 的返回值
config.TotalToFMinDays = dynMin;                                          // ← 一律覆盖
config.TotalToFMaxDays = (userSpecifiedMaxToF > 0) ? userSpecifiedMaxToF : dynMax;
```

而 `MGAAdaptiveSolverConfig.FromMGAConfig` 把 `AutoTuneToFBounds` **硬编码为 `true`**，
于是所有经 `MGAConfig` 进入的路径（GUI 预设、基准套件、盲搜套件）传入的
`MinToFDays` 派生的总航时**下界全部被静默丢弃**。

**已修**为 fill-in 语义（仅在调用方未指定时补默认值）：

```csharp
if (userSpecifiedMinToF <= 0) config.TotalToFMinDays = dynMin;
if (userSpecifiedMaxToF <= 0) config.TotalToFMaxDays = dynMax;
```

**但必须说清：这一处修复"必要但不充分"** —— 实测 GUI 预设的 New Horizons 仍返回 49950 天，
因为 `TotalToFMaxDays` 在损失函数里只是**软惩罚**（`MGAConstraint.MissionDuration`），
并不是硬过滤；而 `PykepBridge.Optimize` 返回的 `sols[0]` 仍可能是该惩罚较大的解。
**把航时包络变成硬约束（或在最终选解处过滤）是设计决策，本轮未擅自更改**，见 §八 遗留。

### 6.2 陈旧注释

`MGAEmpiricalRegistry` 中引用 `DeepWellDiveEnergyGapRatio` 的段落已更新（该常量在上一轮恢复
`MGADifferentialEvolution.cs` 时随悬空常量一并清理）。

---

## 七、验证结果

```
dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release --no-restore      → 0 个警告  0 个错误
dotnet build tests/MGADebugRunner/MGADebugRunner.csproj -c Release --no-restore → 0 个警告  0 个错误
```

| 用例 | 结果 | 说明 |
| :--- | :--- | :--- |
| **`--test tof_bounds`（新增）** | **❌ → ✅** | 86 个世纪尺度航段 → **0**；最长 357.5 年 → **15.0 年** |
| `--test gui_presets` | ❌ 5/8 | 通过数不变，但失败形态改善：Cassini 30261d→**17983d**；NH 由 115165d 的纯时长退化改为"存在不可行航段"（更诚实的失败理由） |
| `--test history_bench` | 见 §七.1 | 22 条基准，检查拓扑判据变更是否引入回归 |

### 7.1 基准回归（`--test history_bench`，22 条摘录 16 条）

**结论：通过数 9/16 → 11/16（改善），但有一处边际回归必须如实记录。**

| 用例 | 修复前 ΔV / 航时 | 修复后 ΔV / 航时 | 判定变化 |
| :--- | :--- | :--- | :--- |
| Mariner 10 | 4206 / 509d | 4206 / 509d | ✅ → ✅（位级一致） |
| Galileo | 6409 / 2102d | 6409 / 2102d | ❌ → ❌（一致） |
| Cassini-Huygens | 7769 / 8076d | **7050 / 8747d** | ❌ → ❌（ΔV 降 719，仍超上限 6200） |
| **Voyager 2** | **7735 / 10242d** | **8188 / 6114d** | **✅ → ❌**（见下） |
| Rosetta | 7099 / 564d | 7099 / 564d | ✅ → ✅ |
| **New Horizons** | 8350 / 20381d | **7889 / 10408d** | ✅ → ✅（ΔV 降 461，航时腰斩） |
| Juno ×2 | 8422 / 9476d | 8471 / 6842d | ✅ → ✅（ΔV +49，航时降 28%） |
| Cassini (1-DSM) | 7769 / 8076d | **7050 / 8747d** | ❌ → **✅**（历史带 4000–7500） |
| BepiColombo | 3744 / 884d | 3744 / 884d | ✅ → ✅ |
| Mars Express / GTO→GEO | 3616 / 3804 | 3616 / 3804 | ✅ → ✅（直达，未用引力助推） |
| Galileo (劳登) | 6409 / 2102d | 6409 / 2102d | ❌ → ❌ |
| **Apollo 11** | 749 / **209762d** | 853 / **164684d** | ❌ → ❌（见下） |

**两处必须说清的变化：**

1. **Voyager 2 的边际回归（✅ → ❌）**：ΔV 7735 → 8188 m/s（+5.9%），超过历史带上限 8000 约 2.4%。
   原因是包络收紧后，原先那条 **10242 天（28 年）**的解不再可选 —— 它正是"用超长航时换低 ΔV"的形态。
   收紧后选到的 6114 天解更接近真实构型（Voyager 2 实际总航时 ≈ 12 年 = 4380 天）。
   **这是把搜索域从"非物理"拉回"物理"的代价，不是缺陷**；但若认为 8000 的上限本身过紧，
   应去核对该历史区间的出处（上一轮已登记"22 组历史 ΔV 区间均无出处"）。

2. **Apollo 11 的退化解未被消除（749→853 m/s，航时 209762→164684 天）**：
   这正是 §6.1 所述"航时包络只是软惩罚、不是硬约束"的直接后果 ——
   地月任务被求解器展开成 **6 航段**拓扑，每段都不超单段上限，但合计仍达 451 年。
   本轮**没有**通过追加阈值去掩盖它（已列入 §九 遗留第 1 条，属设计决策）。

---

## 八、本轮修改文件清单

| 文件 | 改动 |
| :--- | :--- |
| `src/KSPGMAT/MGA/MGAFlybyDynamicsHelper.cs` | 上限统一（`MaxTransferToFYears`）、共振路径补上限、参考系门 `ResolveOrbitalRadiusKm`、删除 2 个常量与 1 处死代码、常量改名 |
| `src/KSPGMAT/MGA/Solver/MGAPhysicsLossEngine.cs` | 航时软界单一真值源 `ToFSoftExcessPenalty` + 3 常量；`ResolveNominalTofDays` 命名化 + 删除 1 AU 静默替代；`LossReferenceCache` 增加自适应配置重载 |
| `src/KSPGMAT/MGA/MGASolutionOrdering.cs` | 选解改用同一 helper，统一为预算单位加性惩罚；新增 `dvRefKms` 参数 |
| `src/KSPGMAT/MGA/Solver/MGAAdaptiveSolver.cs` | `AutoTuneToFBounds` 改为 fill-in；选解传入 `dvRef` |
| `src/KSPGMAT/MGA/Solver/MGAMCTSTopologySelector.cs` | 两处绝对比例判据切换到系统内分位/壳层；调用点传入中央天体名；删除 2 个常量 |
| `src/KSPGMAT/MGA/MGAPhysicalScales.cs` | 新增 `Quantile` / `WellDepthRatio` / `ShellStepRatio` / `ClassifyDeepWellTargetBySystemDistribution` / `ClassifyDeepWellExitByShellSteps` + 3 常量 |
| `src/KSPGMAT/MGA/SolarSystemCatalog.cs` | 后备库兜底不再伪装地球；置 `IsOrbitUnresolved` + 警告日志 |
| `src/KSPGMAT/MGA/SolarSystemBodies.cs` | 新增 `IsOrbitUnresolved` |
| `src/KSPGMAT/MGA/MGAEmpiricalRegistry.cs` | 新增 `Resolved` 状态；注销 2 项、解决 2 项、新增 6 项 |
| `tests/MGADebugRunner/TestSuites/ToFBoundDiagnosticsTest.cs` | **新增**可证伪探针（A 天体中段 / A2 系统内 / A3 同星共振 / B 任务包络） |
| `tests/MGADebugRunner/Program.cs` | 注册 `--test tof_bounds` |

---

## 九、明确遗留（未掩盖）

1. **航时包络目前是"软惩罚"，不是硬约束**（§6.1）。GUI 预设路径的 New Horizons 仍返回 49950 天的解。
   把 `TotalToFMaxDays` 变成硬过滤，或在 `MGASolutionOrdering` 最终选解处过滤，是**设计决策**，
   涉及"是否允许为极低 ΔV 接受超长航时"的产品取向，本轮未擅自更改。
2. **`NominalToFDeepSpaceThresholdDays = 2500` 仍是绝对天数**（§3.1）。已诚实标注"无法在不改变
   行为的前提下无量纲化"，并登记待标定。**没有**换成另一个拍定的比值。
3. **正向 `DerivePhysicalResonantBounds` 对长周期天体的下界压缩是"压到边界"而非"标记不可行"**。
   更彻底的做法是给该函数增加显式的"不可行"返回（需改签名，影响调用面），本轮未做。
4. **`GetResonanceToFMultiplier(double periodDays)` 仍然基本忽略入参**（只用于 `<= 0` 守卫），
   真实语义是常量 `PumpResonanceSlotRatio`。已命名化但未重构调用面（3 处）。
5. **系统样本不足时的回退仍是绝对比例** `InsufficientSystemSampleSmaRatioFallback = 0.45`
   （仅在系统天体 < 2 个时触发）。
6. **`MGAPhysicsLossEngine` 三个"规范阈值"的审计结论已撤回**（§五），
   若后续要真正改为"相对于任务寿命而非预算"，那是一个**新需求**，不是修 bug。
7. **`--test tof_bounds` 的 20 年判据本身是工程口径**。它是探针的**显式**阈值，
   目的是让"荒谬"可判定；生产代码用的是 `MaxTransferToFYears = 15`。

---

*本轮全部改动均通过编译（0 警告 0 错误）与至少一项运行时验证（`--test tof_bounds` 由 ❌ 转 ✅）。*
