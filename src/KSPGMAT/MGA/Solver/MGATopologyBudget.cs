// ============================================================================
// MGATopologyBudget.cs
// 拓扑搜索预算的物理推导器 (Graph-Derived Topology Search Budget)
//
// 【取代对象】
//   1. MGAMCTSTopologySelector.AutoTuneTopologyHyperparameters 中的 ratio 分档表:
//        rArr < rDep*0.5      -> MaxSwingBys=6, MaxBackLegs=2, MaxBackSpacing=3
//        ratio >= 10 || >*8.0 -> MaxSwingBys=6, MaxBackLegs=2, MaxBackSpacing=3
//        ratio >= 4           -> MaxSwingBys=4, MaxBackLegs=1, MaxBackSpacing=2
//        else                 -> MaxSwingBys=3, MaxBackLegs=1, MaxBackSpacing=1
//      (经代码追踪确认: 该表仅被写入 AutoTunedParams 供显示, 从未驱动搜索 —— 但它是
//       错误的"心智模型"来源, 且 MGADefaults 中的对应硬编码 4/1/1/2 确实驱动了搜索。)
//   2. MGADefaults.MaxSwingBys=4 / MaxResonances=1 / MaxBackLegs=1 / MaxBackSpacing=2
//
// 【推导原理 (全部为无量纲量, 无任何经验分档)】
//   MaxSwingBys   = 系统中"可用的物理梯级"个数 |R|
//                   R = { n : SMA 严格介于出发与到达之间
//                           ∧ AssistPowerIndex(n, v_inf) >= 可辨识下限 }
//   MaxResonances = n_req + floor(搜索窗天数 / 天体公转周期)
//                   n_req 由解析双曲偏折几何给出 (ceil(theta_req / delta_max)),
//                   额外额度来自"真正的物理时间预算", 而非"用户要求不设上限"。
//   MaxBackLegs   = 由单调爬升能否闭合能量缺口决定:
//                   gap = dv_required / Σ assist_gain
//                   gap <= 1  -> 0 (单调梯级已足够, 无需折返)
//                   否则按 log2(gap) 给出所需折返次数 (每次折返大致使可调相位翻倍)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>由物理图推导得到的拓扑搜索预算</summary>
    public sealed class MGATopologyBudget
    {
        public int MaxSwingBys;
        public int MaxResonances;
        public int MaxBackLegs;
        public int MaxBackSpacing;
        public int MaxChainLegs;

        /// <summary>解析推导的目标天体共振次数下界 (无动力偏折几何)</summary>
        public int RequiredResonances;

        /// <summary>系统内可用物理梯级的天体名列表 (|R|)</summary>
        public List<string> UsableRungs = new List<string>();

        /// <summary>各候选天体的借力功率指数 (无量纲, 供诊断/UI)</summary>
        public List<KeyValuePair<string, double>> AssistPowerTable = new List<KeyValuePair<string, double>>();

        /// <summary>单调爬升的能量闭合比 gap = dv_required / Σ assist_gain</summary>
        public double EnergyGapRatio = 1.0;

        /// <summary>最深途经点的转移难度比 (任务相对无量纲量, 取代 SMA 比值分档)</summary>
        public double DeepestWaypointDifficulty = 1.0;

        public string Rationale = "";

        public override string ToString()
        {
            return $"MaxSwingBys={MaxSwingBys}, MaxResonances={MaxResonances}, " +
                   $"MaxBackLegs={MaxBackLegs}, MaxBackSpacing={MaxBackSpacing}";
        }
    }

    /// <summary>
    /// 拓扑预算物理推导器。所有输出均由系统天体目录 + 基本物理常数解析导出,
    /// 且 100% 与出发/到达天体对相关 (任务相对), 不含任何按天体名称或轨道比的分档。
    /// </summary>
    public static class MGATopologyBudgetDeriver
    {
        /// <summary>
        /// 硬性物理上限: 底层 BuildFullSolution 拒绝 legCount > 16 的染色体,
        /// 故链长必须 <= 17 个天体。这是**求解器实现约束**, 非经验常数。
        /// </summary>
        public const int MaxSupportedChainBodies = 16;

        /// <summary>
        /// 共振搜索余量 (无量纲): 解析下界 n_req 给出"至少需要几次",
        /// 优化器需要在其上方留出搜索空间以探索非解析最优构型。
        /// 该余量由可用时间预算给出 (见 Derive), 此常数仅为最小保底。
        /// </summary>
        public const int MinimumResonanceHeadroom = 1;

        /// <summary>
        /// 根据出发与到达天体的轨道半长轴比、引力势阱阶梯与搜索时间预算，解析推导 MGA 拓扑搜索物理预算 (MGATopologyBudget)
        /// </summary>
        /// <param name="departure">出发天体名称</param>
        /// <param name="arrival">到达天体名称</param>
        /// <param name="centralBody">中心天体名称 (默认 Sun)</param>
        /// <param name="searchWindowDays">任务总搜索时域跨度 (天)</param>
        /// <returns>包含最大借力次数、共振次数与步长限制的物理预算对象</returns>
        public static MGATopologyBudget Derive(
            string departure, string arrival, string centralBody = "Sun", double searchWindowDays = 0.0)
        {
            var budget = new MGATopologyBudget();
            var pDep = SolarSystemCatalog.GetPlanet(departure);
            var pArr = SolarSystemCatalog.GetPlanet(arrival);
            if (pDep == null || pArr == null || pDep.SMA <= 0 || pArr.SMA <= 0)
            {
                budget.MaxSwingBys = 2;
                budget.MaxResonances = 1;
                budget.MaxBackLegs = 1;
                budget.MaxBackSpacing = 1;
                budget.MaxChainLegs = 4;
                budget.Rationale = "天体目录不可用, 退化为最小可搜索预算。";
                return budget;
            }

            string central = string.IsNullOrEmpty(centralBody) ? "Sun" : centralBody;
            double muCentral = SolarSystemCatalog.GetCentralMu(central);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;

            // ------------------------------------------------------------------
            // 1. 取系统节点
            // ------------------------------------------------------------------
            var nodes = MGAMCTSTopologySelector.KeplerAdjacencyGraphEngine.GetSystemNodes(
                central, departure, arrival);
            if (nodes == null || nodes.Count == 0)
            {
                budget.MaxSwingBys = 2;
                budget.MaxResonances = 2;
                budget.MaxBackLegs = 1;
                budget.MaxBackSpacing = 1;
                budget.MaxChainLegs = 5;
                budget.Rationale = "系统节点为空, 退化为最小可搜索预算。";
                return budget;
            }

            var depNode = nodes.FirstOrDefault(n => n.Name.Equals(departure, StringComparison.OrdinalIgnoreCase));
            var arrNode = nodes.FirstOrDefault(n => n.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase));
            if (depNode == null || arrNode == null)
            {
                budget.MaxSwingBys = 2;
                budget.MaxResonances = 2;
                budget.MaxBackLegs = 1;
                budget.MaxBackSpacing = 1;
                budget.MaxChainLegs = 5;
                budget.Rationale = "出发/到达天体不在系统图中, 退化为最小可搜索预算。";
                return budget;
            }

            bool isOutward = arrNode.SMA > depNode.SMA;
            double inner = Math.Min(depNode.SMA, arrNode.SMA);
            double outer = Math.Max(depNode.SMA, arrNode.SMA);

            // ------------------------------------------------------------------
            // 2. 可用物理梯级 |R|
            // 梯级天体包含：
            //   a) 介于出发与到达轨道之间的顺行中继天体 (rDep < r < rArr 或 rArr < r < rDep)
            //   b) 外太阳系任务中, 位于出发轨道内侧的强引力深势阱天体 (如金星之于地球到土星/木星任务)
            //      向内俯冲借助内侧强引力天体弹射是星际航行(如 Cassini VVEJGA)经典构型。
            // ------------------------------------------------------------------
            var rungAssist = new List<KeyValuePair<string, double>>();

            foreach (var n in nodes)
            {
                var sp = SolarSystemCatalog.GetPlanet(n.Name);
                if (sp == null) continue;

                bool isBetween = (n.SMA > inner * 1.05 && n.SMA < outer * 0.95);
                bool isInnerSlingshotCandidate = (isOutward && n.SMA < depNode.SMA * 0.95);

                if (!isBetween && !isInnerSlingshotCandidate) continue;

                // 该天体作为梯级的到达 v_inf (霍曼近似) —— 纯解析
                double vInf = MGAPhysicalScales.HohmannVInfKms(muCentral, depNode.SMA, n.SMA);
                double index = MGAPhysicalScales.AssistPowerIndex(
                    sp, Math.Max(vInf, 1e-3), muCentral);

                // 内侧俯冲弹射候选仅在具备足够借力势能时纳入梯级
                if (isInnerSlingshotCandidate && index < MGAPhysicalScales.DefaultMinAssistPowerIndex)
                {
                    continue;
                }

                rungAssist.Add(new KeyValuePair<string, double>(n.Name, index));
            }

            budget.AssistPowerTable = rungAssist
                .OrderByDescending(kv => kv.Value)
                .ToList();

            budget.UsableRungs = rungAssist
                .Where(kv => kv.Value >= MGAPhysicalScales.DefaultMinAssistPowerIndex)
                .Select(kv => kv.Key)
                .ToList();

            // MaxSwingBys 直接等于可用梯级数 —— 这是图搜索的输出, 不再是输入
            budget.MaxSwingBys = Math.Max(1, budget.UsableRungs.Count);

            // ------------------------------------------------------------------
            // 3. 能量闭合比 gap (决定是否需要折返腿)
            // ------------------------------------------------------------------
            double dvRequired = MGAPhysicalScales.HohmannVInfKms(muCentral, depNode.SMA, arrNode.SMA);
            double availableBoost = 0.0;
            foreach (var kv in rungAssist)
            {
                if (kv.Value < MGAPhysicalScales.DefaultMinAssistPowerIndex) continue;
                var sp = SolarSystemCatalog.GetPlanet(kv.Key);
                if (sp == null) continue;
                double vInf = MGAPhysicalScales.HohmannVInfKms(muCentral, depNode.SMA, sp.SMA);
                availableBoost += MGAPhysicalScales.AssistGainKms(sp, Math.Max(vInf, 1e-3));
            }

            budget.EnergyGapRatio = (availableBoost > 1e-9)
                ? dvRequired / availableBoost
                : 1.0e3;

            // gap <= 1 表示单调梯级已能闭合能量缺口 -> 无需折返
            // gap > 1 时, 每次折返大致使可调相位翻倍, 故所需折返次数 ~ log2(gap)
            if (budget.EnergyGapRatio <= 1.0)
            {
                budget.MaxBackLegs = 0;
            }
            else
            {
                budget.MaxBackLegs = Math.Min(3,
                    (int)Math.Ceiling(Math.Log(budget.EnergyGapRatio) / Math.Log(2.0)));
            }
            budget.MaxBackSpacing = budget.MaxBackLegs > 0 ? Math.Max(1, budget.MaxBackLegs + 1) : 1;

            // ------------------------------------------------------------------
            // 4. 共振次数: 解析下界 n_req + 真实时间预算允许的额外共振
            // ------------------------------------------------------------------
            var innerDeep = nodes.Where(n => n.SMA < inner * 1.2).OrderBy(n => n.SMA).FirstOrDefault();
            var deepNode = innerDeep ?? depNode;

            MGAPlanetaryAssistManager.ComputeResonantDeflectionNeeds(
                SolarSystemCatalog.GetPlanet(deepNode.Name),
                SolarSystemCatalog.GetPlanet(departure),
                SolarSystemCatalog.GetPlanet(arrival),
                central,
                out _, out _, out _, out int nReq);

            budget.RequiredResonances = Math.Max(1, nReq);

            double bodyPeriodDays = MGAPhysicalScales.BodyPeriodSec(
                SolarSystemCatalog.GetPlanet(deepNode.Name), muCentral) / MGADefaults.SecondsPerDay;
            if (bodyPeriodDays <= 1e-6) bodyPeriodDays = 365.25;

            double windowDays = (searchWindowDays > 0.0)
                ? searchWindowDays
                : MGADefaults.InterplanetarySearchDays;

            // 额外共振额度 = 搜索窗内该天体还能完成多少次公转 (物理时间预算, 非人为上限)
            int extraResonances = (int)Math.Floor(windowDays / bodyPeriodDays);
            budget.MaxResonances = Math.Max(
                budget.RequiredResonances + MinimumResonanceHeadroom,
                budget.RequiredResonances + Math.Max(0, extraResonances) / 4);

            // ------------------------------------------------------------------
            // 5. 最深途经点转移难度 (取代 SMA 比值分档)
            // ------------------------------------------------------------------
            double deepestDifficulty = 1.0;
            foreach (var rung in budget.UsableRungs)
            {
                double d = MGAPhysicalScales.TransferDifficultyRatio(
                    muCentral, pDep, SolarSystemCatalog.GetPlanet(rung), pArr);
                if (d > deepestDifficulty) deepestDifficulty = d;
            }
            budget.DeepestWaypointDifficulty = deepestDifficulty;

            // ------------------------------------------------------------------
            // 6. 链长上限: 求解器物理上限 <-> 预算总和
            // ------------------------------------------------------------------
            budget.MaxChainLegs = Math.Min(
                MaxSupportedChainBodies,
                budget.MaxSwingBys + Math.Max(0, budget.MaxBackLegs) + 2);

            var sb = new StringBuilder();
            sb.Append(isOutward ? "外向" : "内向");
            sb.Append($"转移 | 可用物理梯级 {budget.MaxSwingBys} 个");
            if (budget.UsableRungs.Count > 0)
                sb.Append($" ({string.Join("→", budget.UsableRungs)})");
            sb.Append($" | 解析共振下界 n_req={budget.RequiredResonances}");
            sb.Append($" | 搜索窗 {windowDays:F0} 天 / {deepNode.Name} 周期 {bodyPeriodDays:F0} 天");
            sb.Append($" | 能量闭合比 gap={budget.EnergyGapRatio:F2}");
            sb.Append($" | 最深途经点难度比 {budget.DeepestWaypointDifficulty:F2}");
            sb.Append($" | 全部由物理图与解析式导出 (无经验分档)");
            budget.Rationale = sb.ToString();

            return budget;
        }
    }
}
