# 航天多引力助推 (MGA) 求解优化器核心理论文献库

本目录收录并精炼了支撑本项目（**KSP_GMAT** / 权威自适应 MGA 多引力助推全局优化求解器）底层架构与演化算法的两篇最核心奠基文献。

---

## 核心有效文献速查与代码映射

| 序号 | 论文 Markdown 报告 | 原始文献与作者 | 在 MGA 求解中的核心贡献与关键公式 | 在本项目代码中的具体落地 |
| :--- | :--- | :--- | :--- | :--- |
| **01** | [**1004.3824_Izzo_PaGMO_Parallel_Optimization.md**](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/papers/1004.3824_Izzo_PaGMO_Parallel_Optimization.md) | *A Global Optimisation Toolbox for Massively Parallel Engineering Optimisation*<br>F. Biscani, D. Izzo, C. H. Yam (ESA ACT, 2010) | **广义岛屿模型 (Generalized Island Model)**<br>• 图论建模通信拓扑 $G=(V, E)$（Ring 与 Wheel Rim 轮辋拓扑）<br>• 异步精英迁移算子 $\mathcal{P}_j \leftarrow (\mathcal{P}_j \setminus \mathcal{W}_j) \cup \mathcal{E}_i$<br>• 欧空局 GTOP 经典多重借力基准（Cassini, TandEM, Messenger Full）优化策略 | • [`MGAArchipelagoOptimizer.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/Solver/MGAArchipelagoOptimizer.cs)<br>4 子岛多核有界并行与跨窗口演化<br>• [`MGADifferentialEvolution.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/Solver/MGADifferentialEvolution.cs)<br>环形拓扑每 10 代 Top 2 精英异步迁移 |
| **02** | [**1104.4731_Vasile_Inflationary_DE.md**](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/papers/1104.4731_Vasile_Inflationary_DE.md) | *An Inflationary Differential Evolution Algorithm for Space Trajectory Optimization*<br>M. Vasile, E. Minisci, M. Locatelli (IEEE, 2011) | **智能体动力学 (Agent Dynamics) 与膨胀差分进化 (IDEA)**<br>• 离散动力学方程 $\mathbf{x}_{k+1} = \mathbf{x}_k + \nu \mathcal{S}(\cdot)\mathbf{v}_{k+1}$<br>• $F < 1/2$ 时种群向不动点以收缩因子 $\gamma < 1$ 坍缩的收敛定理<br>• 局部排斥超球体膨胀 (Inflation) 与引导重启 (Guided Restart)，阻断深空轨道欺骗性假局部极小 | • [`MGADifferentialEvolution.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/Solver/MGADifferentialEvolution.cs)<br>连续 35 代停滞早退判定、达标提前收敛与单调盆地跳跃 (MBH)<br>• [`MGASelfHealingEngine.cs`](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/src/KSPGMAT/MGA/Solver/MGASelfHealingEngine.cs)<br>高能区与假极小自动剔除与逆向打靶 |

---

## 为什么其余 3 篇文献被清理？

原目录中曾存在 3 个以航天/优化命名的 PDF 文件（`1810.02640`、`1811.02879`、`2002.04944`），经全文深度解析，确认其下载来源为编号笔误，实际学术内容分别为：
- **`1810.02640`**: 实际为流体力学实验《黏性流体中带电微粒沉降动力学》（与深空引力助推轨迹优化无关）；
- **`1811.02879`**: 实际为半正定规划纯数学理论《SDP 松弛中浮点求解器的极小极大鲁棒性》（非轨道动力学算法）；
- **`2002.04944`**: 实际为量子光学实验《未表征源测量设备无关量子密钥分发实验》（与天体引力助推计算无关）。

因此已果断清除上述与 MGA 轨迹求解无关的文件，确保文献库 100% 聚焦于天体力学引力助推与演化优化核心理论。
