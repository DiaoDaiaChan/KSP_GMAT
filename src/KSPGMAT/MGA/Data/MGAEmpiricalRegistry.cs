// ============================================================================
// MGAEmpiricalRegistry.cs
// 经验参数与未标定常数的**唯一集中登记表** (Single Registry of Empirical Parameters)
//
// 【存在理由 / 2026-09-14 审计修正】
//   在 2026-09-14 的独立核验中, 发现 MGA 模块存在 128 处魔法数字 / 经验值 / 应试硬编码
//   (P0 23 / P1 63 / P2 42)。治理采取两条原则:
//     (a) **P0 (按天体名、任务名、历史构型或测试场景写死的分支与阈值) —— 一律改为物理推导**:
//         例如用 AssistPowerIndex / RobustBand / SpecificEnergyGapRatio / 开普勒解析式取代
//         绝对的 Mu 阈值、SMA 比例、天体名判断与固定共振阶数。这些修正已在各源文件内就地完成。
//     (b) **P1/P2 (尚未完成有监督标定的经验系数) —— 一律命名化 + 集中登记**,
//         使它们从"裸数字"变为"可审计、可替换、可追踪的具名参数"。
//
//   本文件即 (b) 的登记表。**每一项都标注了标定状态与失效条件**,
//   避免再出现"注释声称已零硬编码、实现里仍是裸数字"的情况。
//
// 【使用约定】
//   1. 新增经验常数时, 必须在对应源文件内定义 `public const` 并写明物理/工程依据,
//      同时在本登记表的对应条目中登记; 禁止再出现无命名的裸字面量。
//   2. 完成有监督标定后, 把 `CalibrationStatus` 从 `Uncalibrated` 改为 `Calibrated` 并附证据来源。
//   3. 本表仅是**登记与文档**, 不重复定义数值 —— 数值以各源文件的常量为唯一真值源 (SSOT)。
// ============================================================================

using System.Collections.Generic;

namespace KSPGMAT.MGA
{
    /// <summary>经验参数的标定状态。</summary>
    public enum MGAEmpiricalStatus
    {
        /// <summary>尚未标定 —— 取值来自工程口径或历史经验, 换恒星系/换任务可能失效。</summary>
        Uncalibrated = 0,

        /// <summary>已有解析推导支撑, 但推导本身引入了一阶近似。</summary>
        PhysicallyDerived = 1,

        /// <summary>已由真值数据集 (reports/test_reports/chain_truth_labels.csv) 标定并留存证据。</summary>
        Calibrated = 2,

        /// <summary>纯数值保护 (除零 / 溢出 / 收敛边界), 不属于经验参数。</summary>
        NumericalGuard = 3,

        /// <summary>
        /// 已注销 —— 该项已被**物理推导或系统内分位判据**取代, 源码中的常量/分支已删除。
        /// 保留登记项本身是为了留存"曾经存在过什么、被什么取代"的审计痕迹。
        /// </summary>
        Resolved = 4
    }

    /// <summary>单条经验参数登记项。</summary>
    public sealed class MGAEmpiricalEntry
    {
        /// <summary>参数所属源文件。</summary>
        public string File;

        /// <summary>常量名 (应能在源文件中 grep 到)。</summary>
        public string Symbol;

        /// <summary>标定状态。</summary>
        public MGAEmpiricalStatus Status;

        /// <summary>取值依据 / 物理含义。</summary>
        public string Rationale;

        /// <summary>失效条件 (换恒星系 / 换任务 / 换量级时会怎样)。</summary>
        public string FailureMode;

        /// <summary>建议的替换路径 (物理量或标定方法)。</summary>
        public string ReplacementPath;
    }

    /// <summary>
    /// MGA 模块经验参数集中登记表。
    /// </summary>
    public static class MGAEmpiricalRegistry
    {
        /// <summary>
        /// 全部已登记的经验参数 (只读)。顺序按类别分组, 便于审计逐个核对。
        /// </summary>
        public static readonly IReadOnlyList<MGAEmpiricalEntry> Entries = new List<MGAEmpiricalEntry>
        {
            // ---------------------------------------------------------------
            // A. 拓扑选择与容差 (MGAMCTSTopologySelector / MGATopologyBudget)
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "DeficitAcceptanceMultiple",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "能量赤字超过 2 × 预算者仍进入候选池供优化器实测淘汰。",
                FailureMode = "预算量级差异极大的任务 (深空 vs 内行星) 用同一倍数, 可能过松或过紧。",
                ReplacementPath = "以候选池淘汰率 (实测 Pareto 前沿命中率) 标定。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "TradeoffKmsPerYear",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "帕累托折算率: 航时每多 1 年折合 0.3 km/s。",
                FailureMode = "纯工程偏好, 不同任务类型 (载人/无人) 应有不同汇率。",
                ReplacementPath = "由任务方给定; 或由历史任务的 (ΔV, ToF) 散点回归得出。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "InwardMissionOutwardEnvelope / OutwardMissionOutwardEnvelope",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "深潜 / 外向任务的可达包络倍率 (1.6 / 1.25)。",
                FailureMode = "包络过大 → 候选池爆炸; 过小 → 漏掉真实高能解。",
                ReplacementPath = "以借力功率谱的鲁棒分位 (RobustBand) 替代固定倍率。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "MaxFlightDaysToHohmannMultiple",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "航时上限 = 8 × 霍曼航时; 依据 T ~ a^(3/2), 超此倍数者半长轴已远超两端轨道。",
                FailureMode = "8 的具体取值仍为工程口径。",
                ReplacementPath = "由可达 a_max 反解周期上界 (现有 DerivePhysicalResonantBounds 已部分实现)。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "GiantSlingshotToFRelaxation / GiantSlingshotToFMinFactor",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "巨行星弹弓后的航时收缩/下限系数 (1.35 / 0.35)。",
                FailureMode = "换恒星系后「巨行星」的量级改变, 系数不适用。",
                ReplacementPath = "已用日心速度球定理给出等效巡航速度; 系数本身待标定。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "ShallowWellPotentialFraction (0.03)",
                Status = MGAEmpiricalStatus.Resolved,
                Rationale = "【2026-09-14 第二轮已注销并删除】浅势阱判定比例 0.03 (mu/r_safe 相对 mu_c/a) 已由 MGAPhysicalScales.ClassifyDeepWellTargetBySystemDistribution 取代 —— 判据改为该系统全部天体势阱深度比的**下四分位**, 统计学约定而非绝对比例。Sol 系行为逐一致 (水星/火星命中, 金星/地球/巨行星不命中)。",
                FailureMode = "已消除: 分位自动跟随任意恒星系的天体分布。",
                ReplacementPath = "已完成 (系统内分位)。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "DeepWellResonanceExpansionOrder",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "深势阱目标本星共振候选的展开阶数上限 (6)。",
                FailureMode = "原为该上界直接点名 BepiColombo (6 次共振), 属按历史答案生成候选。",
                ReplacementPath = "接入 MGAFlybyDynamicsHelper.ComputeRequiredResonantPhasingCount 的 N_req。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAMCTSTopologySelector.cs", Symbol = "DeepWellExitSmaRatioThreshold (0.45)",
                Status = MGAEmpiricalStatus.Resolved,
                Rationale = "【2026-09-14 第二轮已注销并删除】已由 MGAPhysicalScales.ClassifyDeepWellExitByShellSteps 取代 —— 判据改为「最内天体比出发天体深入至少 2 个**系统内轨道壳层**」, 壳层比由 (SMA_max/SMA_min)^(1/(n-1)) 从系统自身导出 (Sol 系 ≈ 1.588, 阈值 ≈ 0.397·depSMA, 与原 0.45 同量级)。仅保留一个离散壳层数 DeepWellExitShellSteps = 2。",
                FailureMode = "已消除: 壳层比随星系自动缩放。样本不足时显式回退 InsufficientSystemSampleSmaRatioFallback = 0.45 并标注为回退值。",
                ReplacementPath = "已完成 (系统内壳层计数)。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGATopologyBudget.cs", Symbol = "MaxSupportedChainBodies / MinimumResonanceHeadroom",
                Status = MGAEmpiricalStatus.NumericalGuard,
                Rationale = "链长上限 16 为底层 BuildFullSolution 的输入约束; 共振探索余量 1 为最小保底。",
                FailureMode = "实现约束的变化需同步。",
                ReplacementPath = "无需替换 (实现约束)。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGATopologyBudget.cs", Symbol = "inner*1.05 / outer*0.95 / inner*1.2 / SMA 比 0.55、2.0 / 3000.0 天 / extraResonances/4",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "梯级筛选比例、深井/远外天体判定比例、巡航时间预算与共振额度除数。",
                FailureMode = "换恒星系后行星间距分布改变, 固定比例不再对应同一物理情形。",
                ReplacementPath = "梯级筛选改用语义化的相对分位; 3000 天改由任务寿命输入推导。"
            },

            // ---------------------------------------------------------------
            // B. 差分进化内核 (MGADifferentialEvolution)
            //
            // 【重要更正 · 2026-09-14】本节原先登记了三项"取代天体名硬编码 Mercury / 绝对阈值"的
            // 常量 (DeepWellDiveEnergyGapRatio / ArrivalVInfExcessMultiple+Slope /
            // EarlyConvergenceToFTolerance) 与一项"反编译产物"条目。**经核实这些均不成立**:
            //   · 工作区中的 MGADifferentialEvolution.cs 当时是一个**未提交的反编译覆盖件**
            //     (3,094 行, 1,052 处 numNN 命名, 来源为清理前的旧 DLL);
            //   · git HEAD 中的真正源码为 2,326 行手写实现, **不含任何 Mercury 天体名特判**,
            //     也不含 1.24 / 1.15 / 0.999 / 1.3333333333333333 / 7.5 / 13.5 等魔数;
            //   · 该反编译件已从 git 恢复 (见 MGA_MAGIC_NUMBERS_FIX_RECORD §七)。
            // 因此本节只保留对**真实源码**成立的登记项。
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "Solver/MGADifferentialEvolution.cs", Symbol = "CircuitBreaker*/Stagnation*/Inflation* 等比例化判据 (0.25 / 0.15 / 0.04 / 0.12 / 0.05 / 0.10 / 2.0 / 0.03 / 0.15)",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "全部以「代数预算或搜索域的比例」表达 (无量纲、自缩放), 已取代原绝对阈值 gen>=25 / stagGens>=15,6,10,14 / dispersion<0.05,0.04。因 maxGens 已按维度自适应, 比例形式在任何问题规模下语义等效。",
                FailureMode = "比例值本身仍是工程口径 (但已无量纲, 可移植)。",
                ReplacementPath = "以收敛率统计标定各比例; 属低优先级。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGADifferentialEvolution.cs", Symbol = "局部 ToF 邻域扰动因子 (0.95 / 0.98 / 1.02 / 1.05 等)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "在物理共振出口点附近的吸引盆内做小范围 ToF 微调采样。",
                FailureMode = "扰动幅度 ±2%~±5% 为工程口径, 但作用域明确 (仅围绕已解出的物理出口点)。",
                ReplacementPath = "以出口 ToF 的兰伯特敏感度 (转向角残差梯度) 自适应确定幅度。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGADifferentialEvolution.cs", Symbol = "模式搜索加速 (stepMultiplier = 1.2 + min(0.6, relImprovement*2.0); 外推 *= 1.1 + min(0.5, ...))",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "加速因子已由相对改善量自适应给出, **不再是固定 1.5 倍** (前审计报告 §5 称该项为固定 1.5, 对当前源码不成立)。",
                FailureMode = "上下限 0.6 / 0.5 仍为工程口径。",
                ReplacementPath = "低优先级。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGADifferentialEvolution.cs", Symbol = "种群/代数规模 (按维度自适应) 与组合枚举上限",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "按决策向量维度线性/比例缩放种群与代数, 并设枚举上限防组合爆炸。",
                FailureMode = "缩放的系数为工程口径; 大规模链长时枚举上限可能成为瓶颈。",
                ReplacementPath = "以收敛代数实测分布标定缩放系数。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGASelfHealingEngine.cs", Symbol = "自愈窗口/算力分配 (tOrbD*0.85、*3.25、ToFDays*0.60、*1.50、Pop/3、Gen/2 等)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "自愈搜索窗口与快速层算力分配。",
                FailureMode = "窗口过窄 → 自愈无效; 过宽 → 耗时失控 (实测自愈已使总耗时 +157%)。",
                ReplacementPath = "由拓扑全域物理航时区间 (AutoDeriveResonantLegBounds) 完全接管。"
            },

            // ---------------------------------------------------------------
            // C. 损失函数与约束 (MGAPhysicsLossEngine / MGAConstraintManager)
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "Solver/MGAPhysicsLossEngine.cs", Symbol = "FeasibleLossCeiling / DeepInfeasibilityLoss / SelectionLossCeiling",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "三个「规范阈值」 (1.0+1e-9 / 100.0 / 4.0)。注释称其为「预算倍数」, 但代码中并无任何输入去乘预算。",
                FailureMode = "对预算量级差异极大的任务使用同一熔断阈值, 外太阳系真·高能可行解可能被误熔断。",
                ReplacementPath = "让阈值真正随 dvRef 缩放, 或改由解集自身的鲁棒分位给出。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAPhysicsLossEngine.cs", Symbol = "退火温标 (Init 1.0 / Final 1e-4 / Hot 3.5 / Cold 0.15)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "注释称「自标定」, 但默认调用传入 lossScale = 1.0, 自标定路径从不触发。",
                FailureMode = "温标退化为四个固定常数。",
                ReplacementPath = "把实测损失离散度 (MAD) 真正接入 lossScale。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAConstraintManager.cs", Symbol = "各约束的 NormalizedScale 地板 (10/100/1000/0.1) 与 ToleranceValue",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "归一化分母地板与容差口径。约束阈值本身来自任务配置 (hp), 这部分是合规的。",
                FailureMode = "地板值在极端小/大预算任务上会主导归一化结果。",
                ReplacementPath = "由约束自身的物理基准尺度 (而非固定地板) 给出。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAPhysicsLossEngine.cs", Symbol = "1.20 倍航时软界 + 0.15/0.15 系数; tH>2500 天分支; 1.5 周期共振基准",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "标称航时的塑形系数与分档。",
                FailureMode = "2500 天为绝对天数分档, 以太阳系尺度标定。",
                ReplacementPath = "已把 2500 天在 MGAFlybyDynamicsHelper 中改为无量纲半长轴比; 本处待同步。"
            },

            // ---------------------------------------------------------------
            // D. AI / 门控 / 预筛 (AI/*, MGASurrogatePreFilter)
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "AI/MGASurrogateGate.cs", Symbol = "TargetFalseNegativeRate / CalibrationGain / HardAbortMultiple / MinControlSamples / FeasibilityEnvelopeFactor / 固定种子 20260911",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "门控目标误杀率与校准增益; 阈值本身在线自适应, 但上下界与目标全写死。",
                FailureMode = "固定种子使「随机」对照组确定性可复现, 存在被选择性调参的风险。",
                ReplacementPath = "目标误杀率改由下游漏检代价推导; 种子改为可注入。"
            },
            new MGAEmpiricalEntry {
                File = "AI/MGABanditHyperparameterTuner.cs", Symbol = "F/CR 初值 0.7/0.5、UCB 探索系数 1.414、ε=0.10、奖励常数 0.5/1.5/0.05、muF/muCR 0.65/0.50、停滞扩展 0.30/0.20/0.02、夹紧 [0.20,0.95]",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "注释称「彻底消除硬编码经验退火与魔法数字」, 实际为 12 个手定常数。",
                FailureMode = "换问题维度/规模后需重新调参。",
                ReplacementPath = "保留 JADE Lehmer 均值更新律 (这部分是文献标准), 把初值与夹紧区间外置为配置。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGASurrogatePreFilter.cs", Symbol = "网格 64/256/3.0、步长 15/35/18、采样 0.70~1.30、邻域 1.2、±30/5、150.0 天兜底、park_alt 100 km",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "预筛网格分辨率与扫描步长; park_alt 100 km 为任意天体统一的停泊高度。",
                FailureMode = "park_alt 对半径 < 100 km 的小天体无物理意义。",
                ReplacementPath = "网格由 GPU 实测吞吐标定; park_alt 改由天体半径比例给出。"
            },
            new MGAEmpiricalEntry {
                File = "AI/MGAPhysicsSurrogateNetwork.cs", Symbol = "DvHeadUsableR2 = 0.50、L2 衰减 1e-4",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "ΔV 回归头的可用性门禁 (已由 0.15 收紧到 0.50)。",
                FailureMode = "R² 门禁与训练集规模耦合。",
                ReplacementPath = "以「粗筛误杀率」的端到端指标取代 R² 门禁。"
            },
            new MGAEmpiricalEntry {
                File = "AI/MGAAugmentedLagrangian.cs", Symbol = "RhoInit 1.0 / RhoMax 1e6 / RhoGrowth 2.0 / LambdaMax 1e6 / Contraction 0.10",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "更新律 (ρ 随违背收缩/增长, λ 对偶上升) 是自适应的; 种子与边界全写死。",
                FailureMode = "罚参数封顶值在病态问题上不可解释。",
                ReplacementPath = "以违背量的量级自适应设定 RhoMax。"
            },
            new MGAEmpiricalEntry {
                File = "AI/MGABayesianSubspacePolisher.cs", Symbol = "步长 0.5% span、trialAlphas 8 项、×5.0、h=0.3α、2|h|",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "局部一阶差分与二次极值内插的步长参数。",
                FailureMode = "步长与问题尺度无关, 缩放敏感。",
                ReplacementPath = "步长改由参数上下界与实际梯度量级自适应。"
            },
            new MGAEmpiricalEntry {
                File = "AI/MGAPolicyValueGuide.cs", Symbol = "muFactor*0.4、±1.5 踏板奖励、+2.0 终点诱引、0.7/0.2/0.1 价值权重、0.3/0.2 消耗惩罚、+0.15 巨行星奖励、0.5 近邻折现",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "MCTS 先验与价值评分的启发式权重。",
                FailureMode = "权重在换恒星系后失配 (尤其「巨行星奖励」)。",
                ReplacementPath = "由自对弈/回溯统计标定; 或改为纯无量纲借力功率排序。"
            },
            new MGAEmpiricalEntry {
                File = "AI/MGASymbolicPhysicsConstraints.cs", Symbol = "MinToFHohmannShrinkFactor 0.15 / MaxToFHohmannStretchFactor 6.0",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "航时上下界的霍曼比例; 原注释误写为「4 倍」, 已更正。",
                FailureMode = "对多圈共振任务上界过紧或过松。",
                ReplacementPath = "上界由可达 a_max 反解。"
            },

            // ---------------------------------------------------------------
            // E. 桥接层与注入对齐 (PykepBridge / PykepNative / MGAInjectionAligner)
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "PykepBridge.cs", Symbol = "安全半径 +100 km、SOI 哨兵 1e18、DSM eta 夹紧 [0.05,0.95]、默认 eta 0.5、停泊高度 200 km、段数 [4,32]、满油门初值 1.0、终质量初猜 ×0.85、帕累托点数 9",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "源自语/约束参数与数值初值。",
                FailureMode = "终质量 ×0.85 初猜在真实燃料消耗 > 15% 时影响 Sims-Flanagan 收敛。",
                ReplacementPath = "初猜改由火箭方程解析给出 (给定 Isp / 目标 ΔV)。"
            },
            new MGAEmpiricalEntry {
                File = "PykepBridge.cs", Symbol = "GetSynodicPeriodDays 手写 T_syn 恒等式",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "T_syn = 1/|1/T1 − 1/T2| 为精确代数恒等式, 输入周期来自原生或 KSP 天体。",
                FailureMode = "无 (代数正确); 仅与类头「0 Custom Math」的旧表述不符。",
                ReplacementPath = "无需替换; 类头声明已更正。"
            },
            new MGAEmpiricalEntry {
                File = "MGAPlanetaryAssistManager.cs", Symbol = "ResonanceExplorationHeadroom 2 / DeepWellResonanceOrderCeiling 8 / ReferenceVInfForAssistSignificanceKms 5.0 / 发射兜底 3800 m/s / 安全半径 +200、+100 / 365.25 天兜底 / SMA 比 1.3",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "深势阱共振链的探索余量与若干兜底值; 原 Math.Max(nArrReq, 6) 的写死下界 6 已移除。",
                FailureMode = "兜底 3800 m/s 与 +200/+100 净空为地球尺度标定。",
                ReplacementPath = "兜底值改由天体星历; 净空改由大气层顶 + 参数化缓冲给出。"
            },
            new MGAEmpiricalEntry {
                File = "MGAInjectionAligner.cs", Symbol = "停泊轨解析兜底 / 采样 72 点 / 黄金分割 15 次 / 最小提前量 60 s / 兜底周期 5400 s",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "点火锁相的采样与收敛参数。",
                FailureMode = "兜底 90 min 周期对非地球天体不适用。",
                ReplacementPath = "兜底周期改由停泊轨根数解析给出。"
            },

            // ---------------------------------------------------------------
            // F. 行星际目录与时间系统 (SolarSystemCatalog / MGADefaults)
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "SolarSystemCatalog.cs", Symbol = "地球 μ 398600.4418 / 半径 6378.14 / 安全半径 6518.14 兜底; ResolveParentBody 硬编码母星字典; 离线样本 {Earth,Mars,Venus,Jupiter}; 半径 > 10.0 过滤; isSun 仅识别 Sun",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "SPICE / FlightGlobals 不可用时的离线兜底与名称归一化表。",
                FailureMode = "**换到 Kerbol 缩尺星系或自定义行星系时, 未识别天体一律退化为地球 1 AU**, 飞掠几何与 SOI 全错; Kerbol 被误判为非恒星。",
                ReplacementPath = "(1) 兜底改为「拒绝并提供显式的未知天体占位」, 而非伪装成地球; (2) 母星归属改为按星历层级树递归查找; (3) 恒星识别改为按中心性判定而非名称匹配。"
            },
            new MGAEmpiricalEntry {
                File = "Model/MGAStateManager.cs (MGADefaults)", Symbol = "轨道/质量/搜索窗/DE/帕累托/惩罚 等 16 组共约 90 个常量",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "全局默认值。物理常数 (AU_KM、SecondsPerDay、DegToRad) 与数学常数 (黄金分割) 属可接受; 其余为经验默认。",
                FailureMode = "默认搜索窗 (3 年 / 1095.75 天)、默认 ToF (50~300 天)、惩罚系数等以太阳系内行星任务标定。",
                ReplacementPath = "按任务类型提供 profile 预设, 并把惩罚系数改为无量纲化 (g_hat) 后由约束自身尺度驱动。"
            },
            new MGAEmpiricalEntry {
                File = "EphemerisTimeSystem.cs", Symbol = "UtToGmatSafeJd 的 1957 年前历元映射 (safeBaseJd 2437000.0)",
                Status = MGAEmpiricalStatus.NumericalGuard,
                Rationale = "NASA GMAT 官方 C++ 解析器硬性要求 UTCGregorian >= 1957-10-04。",
                FailureMode = "仅影响导出的 .script 文本, **不影响 MGA 求解内核** (内核全程使用真实 UT 与原生星历)。",
                ReplacementPath = "无需替换 (外部工具兼容补丁); 已在原注释中明确隔离定性。"
            },

            // ---------------------------------------------------------------
            // G. 输出与排序
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "MGASolutionOrdering.cs", Symbol = "航时软界 1.20 × nominalDays + 0.15/0.15",
                Status = MGAEmpiricalStatus.Resolved,
                Rationale = "【2026-09-14 第二轮已解决】帕累托膝部选解器的航时惩罚原与 MGAPhysicsLossEngine 各写一遍且形式不同 (一处乘在解自身 ΔV 上, 一处加在预算单位上), 导致优化目标与选解排序不一致。现两处共用 MGAPhysicsLossEngine.ToFSoftExcessPenalty, 统一为以参考预算为单位的加性惩罚。",
                FailureMode = "已消除 (单一真值源)。",
                ReplacementPath = "已完成。"
            },
            new MGAEmpiricalEntry {
                File = "PorkchopGenerator.cs", Symbol = "色带截断 dv < 1e5、mean + 2.0σ",
                Status = MGAEmpiricalStatus.NumericalGuard,
                Rationale = "渲染归一化的截断; 无可行解时填充空解深色背景 (原 3500 m/s 伪造色带**已删除**)。",
                FailureMode = "无。",
                ReplacementPath = "无需替换。"
            },

            // ---------------------------------------------------------------
            // F. 【第二轮审计 · 2026-09-14】航时包络 / 参考系 / 损失阈值
            // ---------------------------------------------------------------
            new MGAEmpiricalEntry {
                File = "MGAFlybyDynamicsHelper.cs", Symbol = "MaxTransferToFYears (15 儒略年)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "单航段航时上界的**工程硬上限**, 对全部航段统一施加。取代原先的 `霍曼时间 × 3.5` (无绝对上限) + 条件性 `a2/a1 > 10` 门 + 用 Math.Max 实现的伪上限。实测修复前: 316 个受检天体中段有 86 个 > 20 年, 最长 357.5 年 (海王星→冥王星); 修复后 0 个 > 20 年, 最长 15.0 年。取值依据: 现实最长单航段是 New Horizons 木星→冥王星 ≈ 8.4 年, 15 年给出 1.8 倍余量。",
                FailureMode = "该 15 年仍是工程口径, 未由有监督标定确定; 但对任意恒星系是**显式年数**而非隐藏在倍数里。",
                ReplacementPath = "以真值数据集 (chain_truth_labels.csv) 的实际航段分布收紧该上限。"
            },
            new MGAEmpiricalEntry {
                File = "MGAFlybyDynamicsHelper.cs", Symbol = "ResolveOrbitalRadiusKm (参考系一致性门)",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "【第二轮新增】原实现直接把 SolarPlanet.SMA (相对**该天体自身母体**的半长轴) 与传入的中央天体 μ 混用。实测: 以 μ_Earth 配 Earth 的**日心** SMA (1 AU) 与 Moon 的**地心** SMA, 给出阿波罗 11 号地月转移航时上界 **19649 天 = 53.8 年**。现按目录约定解析 (行星→根恒星; 卫星→ParentName; 天体==中央天体→停泊轨道半径), 不可解析即拒绝并回退。修复后地月上界 = 15 天, 且 162 个跨参考系配对转为显式拒绝。",
                FailureMode = "无 (纯几何/目录约定)。系统样本不足时回退值已命名标注。",
                ReplacementPath = "无需替换。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAPhysicsLossEngine.cs", Symbol = "ToFSoftBoundMultiple / ToFSoftPenaltyLinear / ToFSoftPenaltyQuadratic (1.20 / 0.15 / 0.15)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "航时软界阻尼的阈值与系数。原在 MGAPhysicsLossEngine 与 MGASolutionOrdering **各写一遍且形式不同** (一处加在预算单位上, 一处乘在解自身 ΔV 上) ⇒ 优化目标与选解排序不是同一函数。现收敛为单一 helper ToFSoftExcessPenalty, 两处共用, 统一为以参考预算为单位的加性惩罚。",
                FailureMode = "1.20 与 0.15/0.15 仍是工程口径 (形状: 超界后线性+平方)。",
                ReplacementPath = "以收敛行为 (不同超界幅度下的解质量) 标定系数形状。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAPhysicsLossEngine.cs", Symbol = "NominalToFDeepSpaceThresholdDays (2500 天)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "【诚实标注: 无法无量纲化】判定「霍曼时间已不再是有效单弧标称航时」的**绝对天数**阈值。同样的 2500 天对地球出发 (≈ 到土星) 与对木星出发 (木星周期已 11.9 年) 含义不同。审计尝试过改为比值判据, 但该分支的语义本身就是工程时间量; 换成比值会改变行为 (实测会把地球→金星标称航时压到 13 天, 触发全量误罚)。故选择显式命名 + 登记, 而非换一个同样拍定的比值。",
                FailureMode = "对远离地球周期量级的出发天体, 该阈值不再对应同一物理情形。",
                ReplacementPath = "由出发天体的公转周期与目标跨度共同参数化 (需先建立标定数据)。"
            },
            new MGAEmpiricalEntry {
                File = "MGAPhysicalScales.cs", Symbol = "LowerQuantileLevel (0.25) / DeepWellExitShellSteps (2)",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "【第二轮新增】把两处绝对比例 (0.03 / 0.45) 改为系统内相对判据所需的两个**统计学约定量**: 下四分位水平 0.25, 与向内壳层数 2 (离散计数)。二者均与恒星系无关; 壳层比由 (SMA_max/SMA_min)^(1/(n-1)) 从系统自身导出 (Sol ≈ 1.588)。Sol 系行为与原绝对阈值逐一致。",
                FailureMode = "分位水平与壳层数是统计/工程约定, 非物理常数。",
                ReplacementPath = "待标定后可调, 但**不需要**随恒星系更换。"
            },
            new MGAEmpiricalEntry {
                File = "SolarSystemCatalog.cs", Symbol = "GetStandardPlanetFallback 未识别天体分支",
                Status = MGAEmpiricalStatus.Resolved,
                Rationale = "【第二轮已注销并删除】原兜底为 `return (149598023.0, 0.0167, 0.0, 398600.4418, 6378.14)` —— 即**任何未识别天体名静默变成地球** (1 AU / μ_Earth / R_Earth)。在 Kerbol 缩尺星系 / JNSQ / 自建星球包下, 所有自定义天体与地球物理等价, 由此产生的航时/ΔV/共振阶数全部是伪值且无断言可发现。现改为返回全零 + 置 IsOrbitUnresolved, 由消费侧显式拒绝 (ResolveNominalTofDays 亦同步删除 `?? AU_KM` 的静默替代)。",
                FailureMode = "已消除。",
                ReplacementPath = "已完成 (显式未解析标志)。"
            },

            // ============================================================
            // 【第三轮新增 2026-09-17】MGAEphemerisReachabilityAnalyzer.cs
            // 复核 reports/mga_empirical_and_magic_audit.md 时, 把该文件中 4 处未命名的
            // 内联经验系数具名化, 并接入 MGAUnits 单位/公式真值源。
            // ============================================================
            new MGAEmpiricalEntry {
                File = "Solver/MGAEphemerisReachabilityAnalyzer.cs", Symbol = "HalfDeflectionProjectionFloor (0.60)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "【第三轮新增】终点共振自减速的**单次速率消减量下限比例**。物理式为 Δv = 2·v∞·sin(δ/2); v∞ 低时 sin(δ/2) 退化为小量, 会使消减量被高估、共振圈数被低估。此处对 sin(δ/2) 施加下限以表征偏折角的有效几何投影比例。取代原内联裸字面量 `Math.Max(0.60, sinHalf)`。",
                FailureMode = "该下限依赖具体交会几何的统计分布, 对势阱深度与 v∞ 量级差异大的目标不会同样适用。",
                ReplacementPath = "用 chain_truth_labels.csv 的真值共振圈数反标定: 以 (v∞, δ, 实际圈数) 为样本拟合有效投影比例。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAEphemerisReachabilityAnalyzer.cs", Symbol = "TerminalCaptureResonanceToFFraction (0.2)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "【第三轮新增】终点捕获共振环的**航时计权比例**。终点自减速环是近目标天体的低速捕获机动 (周期由目标势阱主导), 不属行星际深空巡航漂移, 故按此比例计入残余调相时间。取代原内联裸字面量 `periodDays * 0.2`。",
                FailureMode = "比例本身无解析依据; 对捕获圈数多的任务 (如水星类深井) 会系统性低估航时。",
                ReplacementPath = "以真值航时 (chain_truth_labels.csv 的 ToF 分量) 回归终点段实际占比。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAEphemerisReachabilityAnalyzer.cs", Symbol = "MaxPumpUpSemiMajorAxisRatio (2.5)",
                Status = MGAEmpiricalStatus.PhysicallyDerived,
                Rationale = "【第三轮新增】引力泵升轨道半长轴的**合理性上界** (以当前天体日心半径为 1 单位)。由能量守恒反解的 a_pump 必须仍处于本段转移范围。2:1 共振半长轴为 2^(2/3) ≈ 1.587 倍, 上界取 2.5 为其留出高能转移裕度。取代原内联裸字面量 `r1 * 2.5`。",
                FailureMode = "2.5 与 1.587 之间的裕度是工程口径, 非解析推导。",
                ReplacementPath = "由可达 a_max (MGAFlybyDynamicsHelper.DerivePhysicalInterplanetaryBounds) 反解, 替换为系统导出量。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAEphemerisReachabilityAnalyzer.cs", Symbol = "OutboundResonanceExitRatio (1.5) / InboundResonanceExitRatio (0.8)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "【第三轮新增】共振进场轨道方向判据的两个**半径比门限**。r2 > 1.5·r1 判为冲向外侧的高能升轨 (进场取 2:1 泵升共振); r2 < 0.8·r1 判为潜向内侧的降轨 (进场取 1:2 共振降轨); 之间按 1:1 处理。取代原内联裸字面量 `r1 * 1.5` / `r1 * 0.8`。",
                FailureMode = "1.5 与 0.8 是「显著外向/内向」的经验分界, 对两端轨道半径接近的任务 (如地-金) 敏感性高。",
                ReplacementPath = "以 2:1 与 1:2 共振半长轴 (即 2^(2/3) ≈ 1.587 与 2^(-2/3) ≈ 0.630) 作为解析分界重新标定。"
            },
            new MGAEmpiricalEntry {
                File = "Solver/MGAEphemerisReachabilityAnalyzer.cs", Symbol = "DeepWellInwardAlignmentRatio (0.9) / AlignedInwardResidualVInfKms (1.0)",
                Status = MGAEmpiricalStatus.Uncalibrated,
                Rationale = "【第三轮新增】内侧深井进场的**对齐判定门限**与**对齐后离面残差上限**。前者: 目标半径 < 0.9×出发半径时视为深井目标 (如地→水的 0.39), 配合 clusterCount≥2 判定轨道面已对齐。后者: 共面近圆交会的真实残余离面分量量级 ~1 km/s。取代原内联 `pDep.SMA * 0.9` 与 `Math.Min(1.0, orthoVinfRaw)`。",
                FailureMode = "0.9 过宽 (未真正深井的任务也会被判定为可对齐); 1 km/s 是量级估计而非逐任务真值。",
                ReplacementPath = "0.9 改为由 MGAPhysicalScales 的系统内分位判据给出 (与 LowerQuantileLevel 同源); 1.0 由实际倾角差的 sin 分量解析给出。"
            }
        };

        /// <summary>按标定状态统计条目数 (供审计与遥测展示)。</summary>
        public static Dictionary<MGAEmpiricalStatus, int> CountByStatus() {
            var dict = new Dictionary<MGAEmpiricalStatus, int>();
            foreach (var e in Entries) {
                dict.TryGetValue(e.Status, out int cur);
                dict[e.Status] = cur + 1;
            }
            return dict;
        }
    }
}
