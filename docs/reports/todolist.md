# KSP-GMAT 项目待办清单 (Todo List)

## 📌 专题：Principia 运行态星历导出至 GMAT (Principia-to-GMAT Ephemeris Bridge)

> **目标**：将游戏内 Principia N 体数值积分推演的天体与飞船状态，自动化采样并打包为标准 SPICE SPK (`.bsp`) 或 CCSDS OEM (`.oem`) 星历文件，直接注入 NASA GMAT 求解内核，实现全星系（包括原版 Kerbol 星系、第三方星系模组、以及高精度飞船轨道）的无缝 N 体动力学推演。

### 阶段一：底层 C++ SPICE 写入与打包引擎 (`pykep_native`)
- [ ] **1.1 封装 NAIF CSPICE SPK 写入 API**
  - 在 `src/native/pykep_native/pykep_spice.cpp` 中引入 `spkw08_c`（Type 8: 等时间步长拉格朗日多项式插值）或 `spkw13_c`（Type 13: 变步长埃尔米特插值）。
  - 实现非托管导出函数 `pykep_export_spk_kernel(...)`。
- [ ] **1.2 C# P/Invoke 接口声明与桥接**
  - 在 `PykepNative.cs` 中添加 `ExportSpkKernel(...)` 的委托声明与动态方法绑定。
  - 处理大容量状态矢量连续内存传递（`double[]` 数组映射）。
- [ ] **1.3 离线基准测试与合法性校验**
  - 编写 C# / PowerShell 单元测试，将一段模拟的开普勒/数值轨迹写入 `.bsp`。
  - 使用 NASA SPICE 工具（`spkmerge`, `spkdiff`, `brief`）或 GMAT 运行验证生成的 `.bsp` 结构合法性与时间边界连续性。

---

### 阶段二：Principia 空间状态时间轴采样器 (`PrincipiaSampler`)
- [ ] **2.1 天体多层级采样逻辑**
  - 针对主行星（日心）：按 1~3 天时间步长采样未来 5~20 年的状态。
  - 针对卫星（地心/火心）：按 1~2 小时时间步长采样。
  - 支持调用 Principia 的 `ephemeris_` 外推接口或通过未篡改初始根数高精度采样。
- [ ] **2.2 坐标系与物理量严格转换**
  - 严格应用已验证的基矢映射：
    $$X_{icrf} = X_{unity},\quad Y_{icrf} = Z_{unity},\quad Z_{icrf} = Y_{unity}$$
    $$V_{x, icrf} = V_{x, unity},\quad V_{y, icrf} = V_{z, unity},\quad V_{z, icrf} = V_{y, unity}$$
  - 历元单位统一为 J2000 TDB 秒数。
- [ ] **2.3 飞船机动轨迹（Flight Plan）采样**
  - 支持将当前活跃飞船的 Principia 飞行计划节点及轨迹采样导出为单独的 `vessel_plan.bsp` 或 `vessel_plan.oem`。

---

### 阶段三：GMAT 脚本自动生成与星历无缝挂载
- [ ] **3.1 `GMATScriptGenerator` 自动配置星历源**
  - 当检测到使用 Principia 导出的星历时，在 GMAT 脚本顶部自动配置：
    ```gmat
    GMAT SolarSystem.EphemerisSource = 'SPICE';
    GMAT SolarSystem.SPKFilename = '../GameData/KSPGMAT/data/principia_ephem.bsp';
    ```
- [ ] **3.2 原版 Kerbol 星系 / 自定义天体声明**
  - 针对非现实天体（如 Kerbin, Mun, Duna, Jool）：
    自动生成 GMAT `Create CelestialBody` 脚本定义块并绑定对应的 NAIF ID 与引力常数。
- [ ] **3.3 飞船参考星历挂载 (`EphemerisName`)**
  - 在飞船定义中支持注入 `Spacecraft.EphemerisName = 'vessel_plan.bsp'`，用于在 GMAT GUI 中直观对比 Principia 轨迹与 GMAT 数值积分轨迹的偏差。

---

### 阶段四：UI 控制面板与一键导出交互
- [ ] **4.1 独立导出管理卡片 / 弹窗**
  - 在“设置/诊断”或 TabCraft 下添加“ Principia 星历导出中心”：
    - 采样时间跨度设置（例如 1 年、5 年、20 年）。
    - 包含天体勾选列表（日、地、月、火、全系统）。
    - 当前星历状态展示（未生成 / 已就绪 / 上次生成时间与大小）。
- [ ] **4.2 导出进度条与异步后台工作流**
  - 采用后台协程/任务执行大规模轨迹计算与写入，避免采样期间游戏主线程卡顿。
  - 提供一键导出并直接刷新 GMAT 脚本引用的联动按钮。

---

## 🛠️ 项目常规优化与近期跟进事项

- [x] **已完成**：方案 1 (SPICE DE440) 与方案 2 (Principia 游戏真值) 的坐标系基矢与母星引力对齐（误差降至 0.0035%）。
- [x] **已完成**：消除太阳中心天体除以零导致的 NaN，完善原点友好卡片。
- [ ] **测试跟进**：在游戏不同任务阶段（开局阶段、跨行星转移阶段、近地停泊阶段）持续监控 TabCraft 比对卡片的性能与稳定性。
- [ ] **国际化同步**：确保所有新增的星历导出与校核提示词条在 `GMATI18n.cs` 中拥有完整的中文与英文映射。
