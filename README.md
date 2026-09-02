# KSP_GMAT

<p align="center">
  <b>High-Precision Interplanetary & Cislunar Trajectory Design for Kerbal Space Program</b><br>
  <i>Powered by NASA General Mission Analysis Tool (GMAT) & Principia N-Body Gravitational Physics</i>
</p>

---

## 📖 简介 / Overview

**KSP_GMAT** 是一个深度整合 **Kerbal Space Program (KSP)** 与 **NASA 官方通用任务分析工具 (GMAT, General Mission Analysis Tool)** 的轨道力学仿真与任务规划框架。

本插件专为 **Principia（N 体引力物理模型）** 与 **Realism Overhaul (RO) / Real Solar System (RSS)** 真实太阳系玩家与高精度任务设计者打造。通过全流程标准化的 GMAT 原生脚本编译与运行时对象双向交互，实现：
- 游戏内航天器开普勒状态向量、真实质量流、发动机比冲与推力一键同步；
- **100% GMAT 原生核心求解**（RungeKutta89 积分、高阶重力场、日月点质量摄动）；游戏侧不启用光压和相对论修正。
- 标准化多目标微分校正打靶（Differential Corrector）；
- 求解机动 **一键写入 Principia 飞行规划器**（Flight Planner）。

**计算边界**：轨道传播只由 GMAT `Propagator` 执行；脉冲机动只由 GMAT `ImpulsiveBurn` 执行；有限推力只由 GMAT `FiniteBurn`、`ChemicalThruster` 和 `Propagator` 执行；打靶只由 GMAT `DifferentialCorrector` 的 `Target/Vary/Achieve` 执行。Python 桥接层仅生成 GMAT 脚本、调用 `gmatpy` 并读取 GMAT RuntimeObject/ReportFile，C# 只负责采集游戏状态、显示和写入 Principia。

有限推力结果不会被火箭方程转换成伪造的瞬时脉冲。当前只有 GMAT 明确提供原生脉冲量的节点允许写入 Principia；有限燃烧会保留 GMAT 报告的边界状态和质量，并明确标记为不可等价写入的结果。

传播子配置与 GMAT `PropSetup` 对齐：`Type`、`FM`、`InitialStepSize`、`Accuracy`、`ErrorThreshold`、`SmallestInterval`、`MinStep`、`MaxStep`、`MaxStepAttempts`、`LowerError`、`TargetError`、`StopIfAccuracyIsViolated`、`StepSize`、`MinimumReduction`、`MaximumReduction` 和 `MinimumTolerance` 均可配置。ForceModel 支持中心天体、PrimaryBodies、点质量、误差控制、重力场阶次/次数、势文件、潮汐文件和大气阻力常用参数；游戏侧不发送 SRP 或 RelativisticCorrection。

> **注意 / Note**：本项目已剥离 GMAT 原生二进制库文件（不包含任何官方数百兆的 `.exe`、`.dll`、`osgPlugins` 等闭源或体积冗余文件）。用户可自行配合官方独立版 GMAT（R2022a / R2026a 等）使用。

---

## 🌟 核心特性 / Key Features

### 1. 100% GMAT 原生微分校正多目标求解器 (Standardized DC Targeter)
支持直接通过游戏内 GUI 自由配置并调用 GMAT 原生微分校正器进行单/多参数打靶：
- 🔺 **远拱点高度 (Ap Alt)**：如 GTO 同步转移（$35,786\text{ km}$）
- 🔻 **近拱点高度 (Pe Alt)**：如轨道圆化、精确离轨与再入入轨
- 📏 **轨道半长轴 (SMA)**：精确能量匹配与周期共振
- ⭕ **轨道离心率 (ECC)**：圆轨道（$e=0$）或目标轨道偏心率
- 📐 **轨道倾角 (INC)**：赤道面或特定倾角变换机动
- 🌐 **距心绝对半径 (RMAG)**：GEO 同步轨道与拉格朗日点定距
- 🌙 **月心近心点距离 (Lunar Perilune RMAG)**：**地月转移注入打靶 (TLI)** 与月球捕获（如 $3,229\text{ km}$）

### 2. 行星际深空多重引力助推全局规划 (Interplanetary MGA & Flyby Planner)
集成高性能航天动力学引擎与 NASA/ESA 深空历史数据库，支持太阳系大范围轨道转移与借力巡航设计：
- 🪐 **经典深空探测历史任务库**：内置 Voyager 1/2、Cassini-Huygens、New Horizons、Galileo、Rosetta、BepiColombo 等真实借力飞行基准；
- 🔗 **任意飞行链拓扑自定义 (Flyby Chain)**：如 `Earth -> Venus -> Earth -> Mars -> Jupiter`，自由设定各段发射窗口、飞行历时、近掠半径与机动约束；
- 🥩 **Porkchop 转移能量等高线图**：动态生成双天体转移 $C_3$ 特征能量、出发/到达 $\Delta V$ 及飞行时间等能网格，支持图面点选交互直接提取轨道根数；
- 📊 **MGA 设计报告与全脚本生成**：一键导出高精度动力学参数报告，并自动编译为 NASA GMAT 原生全流程任务脚本。

### 3. 齐奥尔科夫斯基变质量质心提前与高精点火时相轴 (Physical Centroid Timing & Ignition Modes)
针对有限推力变质量飞行过程中推重比持续上升导致的“加速度非线性后退”问题，提供三种工业级点火时相模式：
1. **模式 1 (无补正几何折半基准)**：保留经典几何折半点火时刻（$t - \Delta t/2$），作为误差评估基线；
2. **模式 2 (高精动力学质心补偿)**：结合 Principia C++ 原生变质量一阶冲量矩积分与开普勒实机时钟锁相，精准提前点火时刻，将质心漂移误差压制至 $<0.1\text{ s}$；
3. **模式 3 (闭环自适应二次优化)**：将 Principia 实机质量消耗及轨道残差自动回传至 GMAT 进行闭环二次微调求解，消除非线性漂移；
- ⏱️ **动态时相轴与状态监控**：实时分解 `[点火起始]` -> `[冲量质心]` -> `[关机]` 历元，直观展示质心补偿量与正味点火偏置。

### 4. 交互式任务序列拖动与重排 (Sequence Drag & Drop Reordering)
- 每个任务步骤支持 **鼠标拖拽手柄 (`☰ 拖动`)**，直接换位重排；
- 支持 **一键步进微调 (`▲` / `▼`)**，实时重构执行流；
- 步骤编号、机动关联与累积时间轴自动动态重算。

### 5. 官方三列参数选择器与航天器快照 (ParameterSelectDialog & Craft Details)
- **参数选择器**：完全复刻 NASA GMAT 官方 ParameterSelectDialog 三列式架构（对象类型 -> 挂载硬件 -> 目标参数），支持变量表达式实时高亮预览；
- **航天器全状态快照**：一键展示 Principia ICRF 六维位置速度矢量、开普勒根数、四大约束摄动场对比以及多级火箭质量预算。

### 6. Principia 与 Realism Overhaul 原生深度互通
- 自动提取 RO 真实发动机推力、比冲、质量流率与干质/燃料质量；
- 支持脉冲机动（Impulsive Burn）与真实质量消耗有限推力机动（Finite Burn）；
- 求解完毕后，点击 **【🔥 一键写入所有机动至 Principia】**，瞬间将机动增量 $\Delta V_{\text{VNB}}$ 及精准点火时刻同步写入 Principia 规划器中。

### 7. 完整四语航天工业级本地化 (Full 4-Language Aerospace i18n)
- 完美支持 **简体中文 (zh)**、**English (en)**、**Русский (ru)**、**日本語 (jp)** 四种语言；
- 严格遵循 **Roscosmos（俄罗斯航天国家集团）** 与 **JAXA（日本宇宙航空研究开发机构）** 官方轨道力学专业术语规范；
- **100% 结构对称（各 1,728 个词条）**，支持在游戏内【系统设置】页实时热重载，无需重启；
- 全局移除彩色 Emoji 符号，根绝 Unity IMGUI 文本乱码，由 `GMATMainWindow.Styles.cs` 统一中枢管理航天级高对比度排版与数值单位。

### 8. 深度传播子与摄动力模型配置 (Propagator & Force Model Config)
游戏内可直接对三大内置传播子（`LEOProp`、`CislunarProp`、`LunarNRHOProp`）进行细粒度微调：
- 地球引力模型：球谐阶数（如 JGM-2 8x8）、重力场源；
- 点质量摄动体：自由勾选 Earth, Luna, Sun, Jupiter；
- 表面受力：按需启用地球高空大气阻力 (Atmospheric Drag)；
- 数值积分器：PrinceDormand78、RungeKutta89 等。

### 9. 双重可视化支持 (2D & 3D Visualization)
- 内嵌 GMAT 仿真轨迹二维投影图查看器（Dual View）；
- 独立 Web 3D 交互式 NRHO / Cislunar 轨道空间渲染工具（`nrho-visualizer`）；
- 支持一键调起 NASA GMAT 官方桌面端 3D GUI 渲染视图。

---

## 📁 仓库文件结构 / Repository Structure

```text
KSP_GMAT/
├── GameData/                   # KSP 游戏模组分发目录
│   └── KSPGMAT/
│       ├── KSPGMAT.version     # 模组版本元数据
│       ├── Plugins/            # 编译后插件 DLL (KSPGMAT.dll)
│       ├── Config/             # 外部后端与引擎配置文件 (backend.cfg)
│       ├── loc/                # 四语工业级本地化表 (zh.json, en.json, ru.json, jp.json)
│       ├── native/             # 预编译高性能 C++ 动力学库 (pykep, keplerian_toolbox, boost)
│       └── Textures/           # 工具栏与界面图标
├── src/                        # C# 客户端模组完整源代码 (.NET Framework 4.8)
│   └── KSPGMAT/
│       ├── KSPGMAT.csproj      # 工程文件
│       ├── I18n.cs             # 动态多语言本地化引擎 (热重载 / 自动识别 / 四语解耦)
│       ├── PrincipiaInterop.cs # Principia C++ API P/Invoke 接口
│       ├── ROEngineInterop.cs  # Realism Overhaul 模块接口反射
│       ├── PropagatorConfig.cs # 力学模型与传播子配置模型
│       ├── MGA/                # 行星际深空多重引力助推大域优化器与 Porkchop 网格生成
│       ├── Model/              # 状态管理中枢 (GMATStateManager) 与参数架构 (GMATParamSchema)
│       ├── Service/            # 任务时序提取、脚本解析与高精度星历渲染
│       └── UI/                 # IMGUI 模块化界面 (Styles, TabCraft, TabSequence, TabMGA, TabResults, Modals)
├── gmat_service/               # Python 桥接中间件 (运行于 GMAT 运行时环境中)
│   ├── gmat_bridge_server.py   # 轻量级 RESTful API 服务 (默认端口 8055)
│   ├── gmat_engine.py          # GMAT 原生脚本编译、执行与运行时结果提取
│   ├── orbit_renderer.py       # Matplotlib 轨迹二维投影渲染器
│   ├── script_parser.py        # 脚本解析工具
│   └── requirements.txt        # Python 依赖清单
├── nrho-visualizer/            # Web 3D 轨迹渲染工具 (Three.js / HTML5)
│   ├── index.html
│   ├── package.json
│   └── src/
├── scripts/                    # 参考任务脚本库与回归测试集
│   ├── Mission_EarthToLunarNRHO_1988.script # 经典地月 NRHO 任务基准
│   ├── test_nasa_target.script             # NASA 打靶测试
│   └── test_nrho_14days.script             # 14 天长程 Halo 积分
├── start_gmat_service.bat      # Windows 一键启动桥接服务脚本
├── .gitignore                  # 严谨的过滤规则 (自动剥离 GMAT 官方二进制及临时日志)
└── LICENSE                     # 开源协议 (MIT)
```

### 路径配置

KSP 插件不再依赖固定盘符。编辑 `GameData/KSPGMAT/Config/backend.cfg`，设置 GMAT `bin` 目录。

GMAT 机动写入 Principia 后，结果页提供“校验 GMAT / Principia 机动节点”。它读取 Flight Plan 中的 manoeuvre，按写入时的 Principia 时间基准匹配 GMAT 节点，并报告时间与 ΔV 幅值误差；如果当前 Principia 版本不支持读取接口，会明确显示失败原因。

---

## 🚀 快速上手 / Quickstart Guide

### 1. 环境准备
1. **安装 Kerbal Space Program** (推荐 1.12.x)，并安装以下前置模组：
   - [Principia](https://github.com/mockingbirdnest/Principia)
   - Realism Overhaul / Real Solar System (推荐)
2. **下载并安装 GMAT**：
   - 从 [NASA 官方网站](https://software.nasa.gov/software/GSC-17177-1) 或 GitHub 下载 GMAT (R2022a 或 R2026a)；
   - 确保 Python 3.10+ 环境可用。

### 2. 安装 KSP_GMAT 插件
- 将仓库中的 `GameData/KSPGMAT` 文件夹复制到您的 KSP 安装目录下的 `GameData/` 中。

### 3. 启动 GMAT 桥接后台服务
- 将 `gmat_service` 文件夹放置或软链接到 GMAT 的 `bin/` 目录下；
- 双击运行 `start_gmat_service.bat`（或在控制台执行 `python gmat_service/gmat_bridge_server.py`）；
- 控制台将提示 `GMAT API initialized successfully` 且监听在 `http://127.0.0.1:8055`。

### 4. 在游戏内体验
1. 进入 KSP 飞行视角（Flight Scene），点击右侧工具栏的 **GMAT** 图标；
2. 切换至 **【任务序列与求解】** 标签页：
   - 支持点击预设模板（如 **GTO 地球同步转移** 或 **地月 NRHO 任务**）；
   - 或使用下方按钮自由添加 `➕ 积分`、`➕ 脉冲`、`🎯 目标求解`；
   - 使用卡片顶部的 `▲`、`▼` 或 `☰ 拖动` 自由排列执行顺序；
3. 点击 **`[ ▶ 启动 GMAT 任务序列求解 ]`**：
   - GMAT 原生内核将在几百毫秒内完成收敛；
   - 求解得到的准确速度增量 $\Delta V$ 将实时写回各机动卡片；
4. 点击 **`[ 🔥 一键写入所有机动至 Principia ]`**，打开游戏内 Principia 地图，查看精准变轨结果！

### 5. 运行原生计算边界检查

该检查不执行离线轨道求解，只防止桥接层重新加入二体公式、RK4 或火箭方程替代实现：

```text
python gmat_service/test_native_authority.py
```

### 6. (选装推荐) 支持 1957 年前历史年份星历配置 (RP-1 / 1950~1951 年开局)

> **💡 背景说明**：NASA GMAT 官方安装包默认附带的 `DE405` 行星历表为了精简体积，默认从 **1957 年 10 月 4 日（Sputnik 1 发射）** 起算。如果您在 **RP-1 / 早期航天生涯模组（1950/1951 年开局）** 中进行任务设计，建议下载 NASA JPL 完整版 SPICE 星历内核以获得横跨数个世纪的高精星历支持。

#### 📥 推荐下载文件 (从 NASA JPL / NAIF 镜像库获取)
访问 [NASA JPL BSP 星历镜像库](https://www.mmnt.net/db/0/1/ssd.jpl.nasa.gov/pub/eph/planets/bsp/) 或 [NAIF Generic Kernels](https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/)：

- **🏆 首选推荐（全能高精）: `de430.bsp` 或 `de440.bsp`**
  - **体积**：~115 MB
  - **覆盖年代**：**公元 1550 年 至 2650 年**
  - **特点**：NASA 现代深空导航标准内核，完美覆盖早期航天（1950/1951+）、现代与未来全部任务，精度达到毫米级。
- **⚡ 轻量推荐（极速下载）: `de421.bsp`**
  - **体积**：仅 **~16 MB**
  - **覆盖年代**：**公元 1900 年 至 2050 年**
  - **特点**：体积极为轻巧，完整覆盖整个 20 世纪与 21 世纪，下载即用。

#### 📂 安装与自动识别机制 (Zero-Config Auto Discovery)
1. **自动识别（最推荐）**：直接将下载的 `de440.bsp`（或 `de430.bsp` / `de421.bsp`）放入 GMAT 安装目录的 `data/planetary_ephem/spk/` 文件夹中。
   - 插件会顺着 `gmat_root` 自动寻路并加载，**无需在配置文件中额外填写**，游戏顶栏将直接亮起 SPICE 绿色徽标！
2. **自定义路径指定**：若文件存放在其他目录（如 `Documents`），可在 `GameData/KSPGMAT/Config/backend.cfg` 中添加 `spice_bsp_path=...` 或在游戏内【⚙️ 设置】页中指定。

---

## 📄 开源许可证 / License

本项目采用 [MIT License](LICENSE) 授权许可。
欢迎提交 Issue 和 Pull Request！
