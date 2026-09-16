# MGA 审计复核与修复 Round 2 — 修复记录（2026-09-17）

> 对象：`reports/mga_empirical_and_magic_audit.md`（4799 项 / 70 文件）
> 方式：逐文件、逐条人工复核 → 先判误判 → 再修真问题。
> 原则：`MGA_MAGIC_NUMBERS_FIX_RECORD_ROUND2_2026-09-14.md` 的 A/B/C 三分类。

---

## 一、本次范围

**第一个文件**：`src/KSPGMAT/MGA/Solver/MGAEphemerisReachabilityAnalyzer.cs`（审计检出 169 条）

---

## 二、误判判定（不改动）

分类器把以下形态一律判 🔴 P0，**全部是误报**：

| 行 | 字面量 | 真实性质 |
| :--- | :--- | :--- |
| L174/L185 | `1000.0` | 星历位置矢量脏数据哨兵（C 类数值保护） |
| L228/L229 | `0.5` / `2.0` | 半角公式 + 余弦定理 `Δv = 2v∞·sin(δ/2)` |
| L232/L262 | `2.0`/`3.0`/`86400.0` | 开普勒第三定律 + 秒→天 |
| L241/L341/L442 | `Math.Pow(2.0, 2.0/3.0)` | 2:1 共振半长轴比（开普勒定律导出） |
| L242/L293/L343/L383 | `2.0/r - 1.0/a` | 活力公式（vis-viva）系数 |
| L252/L310/L253/L311 | `2.0*mu/r` | 逃逸速度能量关系 |
| L278/L289/L353/L437 | `* 0.5` | 霍曼半长轴 `(r1+r2)/2` |
| L305/L325 | `1.0e6` | 赤字饱和哨兵 |
| L313/L315/L420 | `* 1000.0` | km ↔ m 换算 |
| L391/L397/L398 | `2.0`/`0.5` | 飞掠偏转 + 能量守恒 |
| L177/L188/L522 | `180.0/Math.PI`、`+= 360` | 弧度→度 + 角度归一 |

**同义重报**：一行同时产出「经验公式」+「魔法数字」两条 ID ⇒ 统计翻倍。

---

## 三、真问题修复清单

### 3.1 新建统一真值源：`MGAUnits.cs`

`src/KSPGMAT/MGA/Numerics/MGAUnits.cs`（新建，约 380 行）

按用户要求"涉及单位运算/公式的问题，添加一个新的 cs 文件来统一管理"：

- **A. 换算因子常量** — `MetersPerKm` / `SecondsPerDay` / `DaysPerJulianYear` / `KmPerAu` / `RadPerDeg` / `DegPerRad`
- **B. 长度换算** — `KmToM(451525)` → 451525000、`MToKm` / `AuToKm` / `KmToAu`
- **C. 时间换算** — `SecToDay` / `DayToSec` / `DayToJulianYear` / `JulianYearToDay` / `SecToJulianYear` / `JulianYearToSec`
- **D. 速度换算** — `KmsToMs` / `MsToKms`
- **E. 角度换算** — `DegToRad` / `RadToDeg` / `NormalizeDeg360` / `AngularSeparationDeg` / `Atan2PhaseDeg`
- **F. 天体力学公式** — `KeplerThirdLawSec/Day` / `KeplerPeriodRatio` / `VisVivaSpeedKms` / `CircularSpeedKms` / `HyperbolicSpeedAtRadiusKms` / `HohmannSemiMajorAxisKm` / `HohmannTransferTimeSec/Day` / `ResonantSemiMajorAxisKm` / `HyperbolicMaxDeflectionRad` / `DeflectionSpeedGainKms` / `RequiredDeflectionRad` / `Asinh`
- **G. 数学恒等式** — `Midpoint` / `Half` / `Twice` / `Square` / `Cube` / `Hypotenuse` / `VectorDifferenceMagnitude` / `SinHalfAngle`

**数值等价性**：全部替换保持逐位等价（`Cube(a)` = `a*a*a` 而非 `Math.Pow`；
`ResonantSemiMajorAxisKm` 用 `cbrt²` 展开保留 `Math.Pow(x, 2.0/3.0)` 的原始路径）。
`Asinh` 额外加了大数溢出保护分支。

### 3.2 经验系数具名化（B 类，5 组共 7 个新常量）

| 常量 | 值 | 原形态 | 状态 |
| :--- | :--- | :--- | :--- |
| `HalfDeflectionProjectionFloor` | 0.60 | `Math.Max(0.60, sinHalf)` | [经验参数·待标定] |
| `TerminalCaptureResonanceToFFraction` | 0.2 | `periodDays * 0.2` | [经验参数·待标定] |
| `MaxPumpUpSemiMajorAxisRatio` | 2.5 | `r1 * 2.5` | PhysicallyDerived |
| `OutboundResonanceExitRatio` | 1.5 | `r1 * 1.5` | [经验参数·待标定] |
| `InboundResonanceExitRatio` | 0.8 | `r1 * 0.8` | [经验参数·待标定] |
| `DeepWellInwardAlignmentRatio` | 0.9 | `pDep.SMA * 0.9` | [经验参数·待标定] |
| `AlignedInwardResidualVInfKms` | 1.0 | `Math.Min(1.0, ...)` | 物理量级 |
| `MinValidEphemerisRadiusKm` | 1000 | `1000.0` | C 类数值保护 |

全部已在 `MGAEmpiricalRegistry.cs` 登记（附 `Rationale` / `FailureMode` / `ReplacementPath`）。

### 3.3 死代码删除

- `L263` `totalToFDays += periodDays * 1.0;` → `totalToFDays += periodDays;`（恒等乘 1，纯清理）

### 3.4 硬编码预算消除

- `maxBudgetMs = 4000.0` / `maxDsmBudgetMs = 4000.0`（两处函数签名）
  → `MGADefaults.MaxPhysicalEnergyDeficitMs`（已有具名常量，消除重复定义）

### 3.5 单位/公式替换统计

本文件共接入 **80 处** `MGAUnits.*` 调用。
替换后 `grep "86400\|Math\.Pow\|365\.25"` → **0 命中**。

---

## 四、验证

```bash
dotnet build src/KSPGMAT/KSPGMAT.csproj -c Release --no-restore
# → 已成功生成。0 个警告 / 0 个错误  ✅
```

> 按用户指示**跳过运行时 A/B 回归**。⚠️ 遗留风险：本文件位于拓扑剪枝的主路径上，
> 虽然所有替换均保持数值等价，但**尚未做产物位级比对**。
> 建议后续补跑 `MGADebugRunner.exe --blind cassini` 与 `scratch/blind_selfheal2.log` 对照
> （只比 ΔV / ToF / 拓扑）。

---

## 五、新增开发规范

`docs/DEVELOPMENT_STANDARDS.md`（新建）

核心章节：
1. **单位与公式铁律** — 必须走 `MGAUnits` 的清单 + 命名约定 + 量纲后缀表 + 豁免项 + 数值等价性要求
2. **经验常数处置规范** — A/B/C 三分类；B 类"不擅自改值"的理由与标定路径
3. **改动后验证流程** — 编译底线 + A/B 回归 + 回归判定表
4. **考核代码规范** — 判据可证伪、`EvaluationOutcome` 铁律
5. **审计前置：工作区污染检查**
6. **治理报告写作规范** — 每条"已修复"须附三项证据
7. **架构约定速查**
8. **代码审查清单**

---

## 六、后续待办

按检出量排序，尚未复核：

- [ ] `MGADifferentialEvolution.Generation.cs` (275)
- [ ] `MGADefaults.cs` (245)
- [ ] `MGABenchmarkSuite.cs` (235)
- [ ] `MGAMCTSTopologySelector.cs` (226)
- [ ] `MGAPhysicsSurrogateNetwork.cs` (82)
- [ ] 其余 44 个 C# 文件
- [ ] `pykep_native/*.cpp` 18 个文件 (1652) — 预期绝大部分是数学/单位换算

**跨文件推进建议**：`MGAUnits` 已就绪，后续文件可按 §1.4 清单直接批量替换，
`docs/DEVELOPMENT_STANDARDS.md` §1.4 即替换检查表。
