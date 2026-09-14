# KSP_GMAT 文档索引目录 (Documentation Index)

本项目的所有规范、算法原理、官方参考与基准测试文档统一收纳于 `docs/` 目录下，分类结构如下：

---

## 目录结构导航

```
docs/
├── specs/                          # 核心规范与架构审计
│   ├── GMATStateManager规范.md     # GMAT 状态管理与单一真值源 (SSOT) 规范
│   ├── i18n规范.md                 # 四国语言国际化 (ZH/EN/RU/JP) 格式规范
│   ├── baseline_simplified.md     # baseline.md 压缩版实施条款
│   └── baseline_audit_report.md   # 基线条款全量静态审计与合规分析报告
├── gmat/                           # NASA GMAT 权威参考资料
│   ├── GMAT_User_Guide.pdf         # NASA GMAT 官方用户手册与参考指南 (PDF)
│   └── gmat_docs_distillation.md   # GMAT 官方文档语法、对象模型与参数精要提炼
├── pykep/                          # ESA PyKEP / kep3 动力学算法文档
│   └── notebooks/md/               # Jupyter 教程 Markdown 精选
│       ├── udp_mga/                # 纯弹道多体重力助推 (MGA) 轨迹设计
│       ├── udp_mga_1dsm/           # 包含单次深空机动的 MGA-1DSM 轨迹规划
│       ├── primer_vector/          # Lawden 劳登主矢量转移最优性判定
│       ├── udp_sf_pl2pl/           # Sims-Flanagan 低推力螺旋转移离散解算
│       └── ...                     # 庞特里亚金极大值原理与极小能量轨迹
├── benchmarks/                     # 性能基准测试与数值精度分析
│   ├── benchmark_full_results.txt  # 全量 MGA 航天任务基准求解集输出
│   ├── benchmark_galileo_test.txt  # 经典伽利略 (Galileo VEEGA) 精度比对
│   ├── benchmark_new_native.txt    # kep3 原生 C++ 互操作桥接实测报告
│   ├── benchmark_optimized_output.txt # 多窗口差分进化并发寻优测试
│   └── benchmark_output.txt        # 基础基准运行输出
├── papers/                         # 学术前沿文献、算法原著 PDF 与技术解析
│   ├── 1004.3824_Izzo_PaGMO_Parallel_Optimization.pdf
│   ├── 1104.4731_Vasile_Inflationary_DE.pdf
│   ├── Revisiting_Lambert_Izzo_2015.pdf
│   ├── IJCAI2015_MCTS_Interplanetary.pdf
│   └── ...                         # 轨道转移优化、机器学习航天动力学等研报
└── reports/                        # 工程历史报告与演进记录
    ├── todolist.md                 # 项目功能清单与路线图
    └── walkthrough.md              # 架构演进与最近迭代实施验证报告
```

---

## 根目录关键文件说明

- **[baseline.md](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/baseline.md)**：项目最高架构规范与基线铁律单一真值源。
- **[baseline_agent.md](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/baseline_agent.md)**：编码 Agent 约束守则与审计红线。
- **[README.md](file:///c:/Users/43701/gmat-win-R2026a/bin/KSP_GMAT/README.md)**：项目主介绍与运行指引。
