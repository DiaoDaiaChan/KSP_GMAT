# KSP_GMAT 开发规范 (Development Standards)

> **版本**：R2026a.1
> **建立日期**：2026-09-17
> **适用范围**：本仓库全部 C# 与 C++ 源码
> **强制级别**：§1「单位与公式铁律」为 **硬约束**，违反即视为代码缺陷

---

## 1. 单位与公式铁律 (SSOT for Units & Formulas)

### 1.1 唯一真值源

**`src/KSPGMAT/MGA/Numerics/MGAUnits.cs`** 是 MGA 全域单位换算与天体力学基本公式的单一真值源 (SSOT)。

任何源码中出现的单位换算或基本力学公式，**必须**调用 `MGAUnits` 的具名成员，禁止就地展开为字面量表达式。

```csharp
// ❌ 禁止 —— 裸字面量，无法区分"单位换算"与"拍脑袋系数"
double tofDays = (2.0 * Math.PI * Math.Sqrt(Math.Pow(aKm, 3.0) / mu)) / 86400.0;
double dvMs    = dVKms * 1000.0;
double aTrans  = (r1 + r2) * 0.5;
double phase   = Math.Atan2(y, x) * (180.0 / Math.PI);

// ✅ 必须 —— 具名、可审计、量纲自明
double tofDays = MGAUnits.KeplerThirdLawDay(aKm, mu);
double dvMs    = MGAUnits.KmsToMs(dVKms);
double aTrans  = MGAUnits.HohmannSemiMajorAxisKm(r1, r2);
double phase   = MGAUnits.Atan2PhaseDeg(y, x);
```

### 1.2 命名约定

| 类别 | 约定 | 示例 |
| :--- | :--- | :--- |
| 换算函数 | 动词 + 目标单位后缀 | `KmToM(451525)` → 451525000<br>`KmsToMs(3.21)` → 3210<br>`SecToDay(x)` / `DayToSec(x)` |
| 参数化公式 | 纯几何/力学名 + 量纲后缀 | `KeplerThirdLawSec(a, mu)`<br>`VisVivaSpeedKms(r, a, mu)`<br>`HohmannSemiMajorAxisKm(r1, r2)` |
| 换算因子常量 | `<目标单位>Per<源单位>` | `MetersPerKm` / `SecondsPerDay` / `DaysPerJulianYear` / `RadPerDeg` |

用户可读写法示例：`MGAUnits.KmToM(451525)`（把 451525 km 公里化为米）、
`MGAUnits.KeplerThirdLawDay(a, mu)`（开普勒公式求周期）。

### 1.3 量纲后缀表（全仓库统一）

| 后缀 | 含义 | 单位 |
| :--- | :--- | :--- |
| `Km` | 公里 | km |
| `M` | 米 | m |
| `Au` | 天文单位 | AU |
| `Kms` | 公里每秒 | km/s |
| `Ms` | 米每秒 | m/s |
| `Sec` | 秒 | s |
| `Day` | 日 | day |
| `Yr` | 儒略年 | 365.25 day |
| `Deg` | 度 | ° |
| `Rad` | 弧度 | rad |

**变量命名也必须带量纲后缀**。`double r1` 不合格，应为 `double r1Km`；`double dv` 应为 `double dvMs`。

### 1.4 必须走 `MGAUnits` 的清单

| 字面量 / 表达式 | 替代 |
| :--- | :--- |
| `86400.0` | `MGAUnits.SecondsPerDay` / `SecToDay` / `DayToSec` |
| `1000.0`（km↔m） | `MGAUnits.KmToM` / `MToKm` / `KmsToMs` / `MsToKms` |
| `365.25` | `MGAUnits.DaysPerJulianYear` / `DayToJulianYear` |
| `149597870.7` / `MGADefaults.AU_KM` | `MGAUnits.KmPerAu` / `AuToKm` / `KmToAu` |
| `Math.PI / 180.0` | `MGAUnits.RadPerDeg` / `DegToRad` |
| `180.0 / Math.PI` | `MGAUnits.DegPerRad` / `RadToDeg` |
| `(a + b) * 0.5`（半径半和） | `MGAUnits.HohmannSemiMajorAxisKm(a, b)` |
| `2.0 / r - 1.0 / a` | `MGAUnits.VisVivaSpeedKms(r, a, mu)` |
| `Math.Pow(a, 3.0)`（开普勒） | `MGAUnits.KeplerThirdLawSec` / `Cube(a)` |
| `2.0 * v * Math.Sin(θ * 0.5)` | `MGAUnits.DeflectionSpeedGainKms(v, θ)` |
| `if (< 0) += 360.0`（角度归一） | `MGAUnits.NormalizeDeg360(x)` |
| `Math.Abs(a-b); if (>180) 360-x` | `MGAUnits.AngularSeparationDeg(a, b)` |
| `Math.Log(x + Math.Sqrt(x*x+1.0))` | `MGAUnits.Asinh(x)`（含大数溢出保护） |

### 1.5 豁免（不必走 `MGAUnits`）

- **纯数值保护**：`Math.Max(0.0, x)`、`Math.Max(1e-12, x)` 等防零/防负保护。
- **循环与索引**：`for (int i = 0; ...)`、`arr[0]`、`Count - 1`。
- **格式串与 UI 文本**：`$"{x:F1}°"`、`/ 2` 用于排版。
- **物理常数本身的定义处**（如 `MGAUnits` 内、`MGADefaults` 内的 `const double` 初值）——
  这里是常数的**定义源**，不是使用点。
- **原生 C++ 内核**（`src/native/`）：`MGAUnits` 是托管侧 SSOT；
  C++ 侧若需同类换算，应在其同目录建立对应的头文件并遵循同一命名约定，
  **不允许在 C++ 业务代码里裸写**。

### 1.6 数值等价性要求

把字面量替换为 `MGAUnits` 成员时，**必须保证浮点结果与替换前逐位相同**（或差异 ≤ 1 ulp）。
引入 `MGAUnits` 的目的不是"改善数值"，而是**统一语义、消除审计噪音**。

因此：
- 禁止借替换之机改变公式结构（例如把 `a³` 改写成 `Math.Exp(3*Math.Log(a))`）；
- 替换后必须跑 A/B 回归（见 §3）确认产物一致；
- 若某替换**必须**带来数值变化，则它不是"单位化重构"，而是一次**算法变更**，
  须单独提 PR 并在描述中给出物理依据与实测差异。

---

## 2. 经验常数的处置规范

### 2.1 三分类原则

| 类别 | 定义 | 处置 |
| :--- | :--- | :--- |
| **A 类** | 天体名 / 任务名 / 历史构型 / 测试场景写死的分支与阈值 | 一律改为**物理推导**或 `MGAPhysicalScales` 无量纲判据 |
| **B 类** | 未标定的经验系数（如捕获远拱点倍数、投影下限） | **具名化 + 登记 `MGAEmpiricalRegistry`**，附失效条件与替换路径。**不擅自改值** |
| **C 类** | 物理常数 / 数值保护哨兵 | 不动，或归入 `MGAUnits` / `MGADefaults` |

### 2.2 为什么 B 类"不擅自改值"

改值只是把一个拍定的数换成另一个拍定的数 —— 这正是审计报告曾犯的错（把"提成具名常量"记为"已清零"）。
正确的消除路径是 **真值标注 + 有监督标定**：

1. 用 `MGADebugRunner --label` 生成逐链真值（`reports/test_reports/chain_truth_labels.csv`）；
2. 以真值为标签，对候选系数做回归/网格搜索；
3. 用 A/B 回归验证标定后的整体不劣化。

### 2.3 每个 B 类常数的文档要求

```csharp
/// <summary>
/// <一句话说明这个量在物理上是什么。>
/// <para>
/// 物理来源: <推导过程，或明确说明"依赖具体几何，无法由单一不变量确定"。>
/// </para>
/// <para>
/// 标注: **[经验参数·待标定]** —— 标定路径: <指向数据集或实验>。
/// 取代原内联裸字面量 <c>750.0</c>。
/// </para>
/// </summary>
public const double FooMs = 750.0;
```

---

## 3. 改动后的验证流程

### 3.1 底线：编译必须通过

```bash
dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release --no-restore
```

要求 **0 警告 0 错误**（`deploy.ps1` + `tools/ComplianceChecker` 的基线红线）。

> ⚠️ 不带 `--no-restore` 会报 `NuGet.targets(649,5) Value cannot be null (path1)`
> —— 这是**环境性**错误，不是代码问题。

### 3.2 数值重构：A/B 回归

任何触碰求解路径的改动，都必须与最新基线比对：

```bash
tests/MGADebugRunner/bin/Release/net48/MGADebugRunner.exe --blind
```

- 单任务快速验证：`--blind cassini`（对时序最敏感）
- 基线：`scratch/blind_selfheal2.log`（逐链自愈后，两次跑逐位一致 13/13）
- **只比对 ΔV / ToF / 拓扑，不比对耗时**（存在 ±3% 浮点累加顺序浮动）

### 3.3 回归判定

| 情形 | 判定 |
| :--- | :--- |
| 纯单位化重构（§1.6） | 产物**必须位级一致**。不一致即为 bug |
| 有意改变算法 | 需在 PR 描述给出物理依据 + 实测 ΔV/ToF 差异 |
| 改基准/考核代码 | 见 §4 |

---

## 4. 考核代码规范（tests/ 目录）

### 4.1 判据必须可证伪

不只看判据**是什么**，要看它**有没有可能失败**。

反例：`totalDv <= MaxExpectedDvMs * 1.35` 之所以是恒真式，
是因为上游已把求解器预算设成同一个 `MaxExpectedDvMs`。只看 `* 1.35` 会误判为"容差略宽"。

### 4.2 铁律

- 评测函数**必须回传** `EvaluationOutcome`，由 `Run` 依显式阈值裁决；
- **评测路径内禁止 `EndCase(true)`**；
- 禁止 `passed > 0` 之类"N 中过 1 即 PASS"；
- 搜索窗一律用 `EvalWindows.OpportunityYearWindow`（`[该年 1/1, 次年 1/1)`），
  **禁止** `launchUT ± N 天`（最优解恰在窗口中心）；
- 历史真值只能**事后比对**；回放输出必须带 `[回放]` 标签，且不得作为求解器能力证据。

### 4.3 审计必须同时扫 `tests/`

历次审计中，作弊点 **19/23 在 `tests/MGADebugRunner/`**，仅 4 处在 `src/`。
只扫 `src/` 会漏掉绝大部分。

---

## 5. 审计前置：工作区污染检查

审计或重构任何文件前，**先确认工作区未被非源码产物污染**：

```bash
# 反编译特征计数 (numNN / arrayNN / textNN)
grep -c "num[0-9][0-9]" <file>
git show HEAD:<file> | grep -c "num[0-9][0-9]"

# 整文件被替换的特征: insertions ≈ deletions ≈ 文件总行数
git diff --stat <file>
```

判定：`HEAD = 0 但工作区 > 0` ⇒ **先恢复再评估**，不要在污染件上做结论。

> 历史教训：`MGADifferentialEvolution.cs` 曾为未提交的反编译覆盖件（3094 行 / 1052 处 `numNN`），
> 基于它得出的"Mercury 硬编码 6 处""DE 需源码级重构"等结论**全部作废**。
> 已固化为 skill：`~/.workbuddy/skills/source-tree-integrity-check/SKILL.md`。

---

## 6. 治理报告写作规范

**不得**把下列行为记为"已清零"：
- 把裸字面量提成具名常量（§2.2）；
- 换成另一组绝对值；
- 换成数学等价分支。

每条"已修复"必须附三项证据：

| 证据 | 内容 |
| :--- | :--- |
| **(a)** 行为差异 | 在**非太阳系 / 非测试任务**上的实测差异（不能只跑测试用例） |
| **(b)** 常数推导 | 新常数的物理/数学推导出处 |
| **(c)** 残留证明 | `grep` 证明同语义旧常数在全树已无残留 |

反例（历史）：`Mu>=5.0e6`、`Mu>=500.0`、`SMA*0.45`、`MGAFlybyDynamicsHelper.cs:288` 的 `150.0/6.5`
四项皆属"新体系已建、旧实现未删"，却被记为已清除。

---

## 7. 架构约定速查

- `src/KSPGMAT/MGA/` 统一扁平命名空间 `KSPGMAT.MGA`（含 `Solver/`、`Numerics/`、`Telemetry/`）；
  `AI/` 例外，用 `KSPGMAT.MGA.AI`。
- `MGADefaults` 定义在 `Model/MGADefaults.cs`，属性属 `KSPGMAT.Model`，MGA 侧需 `using KSPGMAT.Model;`。
- 两个配置类勿混：
  - `MGAConfig`（`Model/MGAStateManager.cs`，底层，含 `MaxSwingBys/MaxResonances/...`）
  - `MGAAdaptiveSolverConfig`（`MGA/Solver/MGAAdaptiveSolver.cs`，高层，含 `DeparturePlanet/ArrivalPlanet`）
- **零托管数学**：完整轨道传播**只能**委托 `PykepNative` / `PykepBridge`。
  C# 侧可做无量纲化、判据、统计与**解析标量估计**（`MGAUnits` 的定位），
  但**不得**自编开普勒/兰伯特传播冒充原生结果。
- 新增经验常数必须命名 + 登记 `MGAEmpiricalRegistry`，**禁止无命名的裸字面量**。

---

## 8. 代码审查清单 (Checklist)

提交前逐项确认：

- [ ] 编译 0 警告 0 错误
- [ ] 无裸写 `86400.0` / `1000.0`(km↔m) / `365.25` / `Math.PI/180.0` / `Math.Pow(a,3.0)` / `(r1+r2)*0.5`
- [ ] 新增数值均带量纲后缀，或已具名
- [ ] 新增经验系数已登记 `MGAEmpiricalRegistry` 并标注 `[经验参数·待标定]`
- [ ] 触碰求解路径的改动已做 A/B 回归（只比 ΔV/ToF/拓扑）
- [ ] 未引入工作区污染（§5）
- [ ] 改动涉及基准/考核代码时，判据可证伪（§4.1）
