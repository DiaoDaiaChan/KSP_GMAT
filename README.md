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
- **100% GMAT 原生核心求解**（RungeKutta89 积分、高阶地磁球谐引力场、日月点质量摄动、太阳光压 SRP）；
- 标准化多目标微分校正打靶（Differential Corrector）；
- 求解机动 **一键写入 Principia 飞行规划器**（Flight Planner）。

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

### 2. 交互式任务序列拖动与重排 (Sequence Drag & Drop Reordering)
- 每个任务步骤支持 **鼠标拖拽手柄 (`☰ 拖动`)**，直接换位重排；
- 支持 **一键步进微调 (`▲` / `▼`)**，实时重构执行流；
- 步骤编号、机动关联与累积时间轴自动动态重算。

### 3. Principia 与 Realism Overhaul 原生深度互通
- 自动提取 RO 真实发动机推力、比冲、质量流率与干质/燃料质量；
- 支持脉冲机动（Impulsive Burn）与真实质量消耗有限推力机动（Finite Burn）；
- 求解完毕后，点击 **【🔥 一键写入所有机动至 Principia】**，瞬间将机动增量 $\Delta V_{\text{VNB}}$ 及精准点火时刻同步写入 Principia 规划器中。

### 4. 深度传播子与摄动力模型配置 (Propagator & Force Model Config)
游戏内可直接对三大内置传播子（`LEOProp`、`CislunarProp`、`LunarNRHOProp`）进行细粒度微调：
- 地球引力模型：球谐阶数（如 JGM-2 8x8）、重力场源；
- 点质量摄动体：自由勾选 Earth, Luna, Sun, Jupiter；
- 表面受力：太阳光电辐射压力 (SRP, Spherical Model)、地球高空大气阻力 (Atmospheric Drag)；
- 数值积分器：PrinceDormand78、RungeKutta89 等。

### 5. 双重可视化支持 (2D & 3D Visualization)
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
│       ├── Plugins/            # 编译后插件 DLL
│       │   └── KSPGMAT.dll
│       └── Textures/           # 工具栏与界面图标
│           └── icon.png
├── src/                        # C# 客户端模组完整源代码 (.NET Framework 4.8)
│   └── KSPGMAT/
│       ├── KSPGMAT.csproj      # 工程文件
│       ├── GMATClient.cs       # HTTP 异步通信与 JSON 序列化核心
│       ├── GMATParameterRegistry.cs # GMAT 属性与开普勒参数字典
│       ├── PrincipiaInterop.cs # Principia C++ API P/Invoke 接口
│       ├── ROEngineInterop.cs  # Realism Overhaul 模块接口反射
│       ├── PropagatorConfig.cs # 力学模型与传播子配置模型
│       └── UI/
│           ├── GMATMainWindow.cs      # 任务序列与参数交互主窗口
│           └── ToolbarRegistration.cs # 工具栏注册
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

---

## 📄 开源许可证 / License

本项目采用 [MIT License](LICENSE) 授权许可。
欢迎提交 Issue 和 Pull Request！