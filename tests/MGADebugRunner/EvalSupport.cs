using System;
using System.Collections.Generic;
using KSPGMAT;

namespace MGADebugRunner
{
    /// <summary>
    /// 评测用例的结构化结果契约。
    ///
    /// 【2026-09-14 新增 —— 消除"恒真判定"】
    /// 审计发现以下三种形态使评测**在结构上不可能失败**：
    ///   1. <c>EvalCassini(); ctx.EndCase(true);</c> —— 评测函数返回值被丢弃，判定硬编码 true；
    ///   2. <c>bool ok = true; try { EvalXxx(); } catch { ok = false; }</c> —— 只有抛异常才算失败，
    ///      ΔV / 收敛 / 飞掠可行性从不参与判定；
    ///   3. <c>bool ok = passed &gt; 0;</c> —— N 个任务过 1 个即 PASS。
    ///
    /// 修正原则：**评测函数必须把可判定的物理量回传，由 Run 依显式阈值裁决。**
    /// 评测路径内禁止再出现 <c>EndCase(true)</c>。
    /// </summary>
    public struct EvaluationOutcome
    {
        /// <summary>求解器是否收敛出解。</summary>
        public bool Converged;

        /// <summary>是否全部航段物理闭环 (含飞掠可行、位置矢量连续)。</summary>
        public bool AllLegsFeasible;

        /// <summary>考核总 ΔV (m/s)。未收敛时为 <see cref="double.NaN"/>。</summary>
        public double TotalDvMs;

        /// <summary>工质预算 (m/s)。&lt;= 0 表示未设预算，此时不做超限判定。</summary>
        public double BudgetMs;

        /// <summary>
        /// 本次结果是否来自"历史真值回放"(把历史发射日/航段航时/借力序列写进输入)。
        /// 回放结果**不能**作为求解器寻优能力的证据，只能证明星历/开普勒链路自洽。
        /// </summary>
        public bool IsHistoricReplay;

        /// <summary>判定摘要 (用于 ctx.Fail 的信息)。</summary>
        public string Summary;

        /// <summary>是否在工质预算内 (未设预算时限为通过)。</summary>
        public bool WithinBudget => BudgetMs <= 0.0 || (Converged && TotalDvMs <= BudgetMs);

        /// <summary>综合判定：收敛 + 全航段闭环 + 未超预算。</summary>
        public bool Passed => Converged && AllLegsFeasible && WithinBudget;

        /// <summary>[回放] 与 [盲搜] 标签，供控制台/报表统一标注。</summary>
        public string ModeTag => IsHistoricReplay ? "[回放]" : "[盲搜]";

        public static EvaluationOutcome NotConverged(string why, double budgetMs = 0.0) {
            return new EvaluationOutcome {
                Converged = false,
                AllLegsFeasible = false,
                TotalDvMs = double.NaN,
                BudgetMs = budgetMs,
                IsHistoricReplay = false,
                Summary = why
            };
        }
    }

    /// <summary>
    /// 评测搜索域的物理推导工具。
    ///
    /// 【2026-09-14 新增 —— 消除"搜索域锚定答案邻域"】
    /// 审计发现各评测套件普遍以 <c>launchUT ± 10/20/60 天</c> 开窗，即以**精确历史发射日**为圆心
    /// 的邻域搜索，等于把最优解编码进搜索边界 (最优点恰好落在窗口中心 t=0)。
    ///
    /// 改为"发射机会年份"年粒度开窗：<c>[该年 1 月 1 日, 次年 1 月 1 日)</c>。
    /// 年粒度只表达"任务在哪一年有发射机会"这一**任务级公开先验**（真实任务设计亦以此为约束），
    /// 不泄露具体月/日/时与最优历元。
    /// </summary>
    public static class EvalWindows
    {
        /// <summary>
        /// 由锚点历元推导"发射机会年份"年粒度搜索窗。
        /// 解析失败时退化为零宽窗 (调用方需自行回退到显式区间)。
        /// </summary>
        public static void OpportunityYearWindow(double anchorUT, out double startUT, out double endUT) {
            startUT = 0.0;
            endUT = 0.0;
            if (anchorUT <= 0.0) return;

            int year = 0;
            try {
                // FormatUtcGregorian 输出形如 "15 Oct 1997 08:43:00.000"
                string s = GMATClient.FormatUtcGregorian(anchorUT);
                string[] parts = s.Split(new char[] { ' ', ':', '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3) int.TryParse(parts[2], out year);
            } catch {
                year = 0;
            }

            if (year <= 1900 || year >= 2200) {
                // 无法定年: 不构造窗口 (避免误用锚点邻域)。
                return;
            }

            startUT = GMATClient.ParseUtcGregorian($"01 Jan {year} 00:00:00.000");
            endUT = GMATClient.ParseUtcGregorian($"01 Jan {year + 1} 00:00:00.000");
        }

        /// <summary>
        /// 判断给定拓扑是否**独立地**包含了历史预期借力天体集合 (顺序不敏感)。
        /// 用于"盲搜是否重新发现历史构型"这一类可证伪判据。
        /// </summary>
        public static bool TopologyContainsAll(List<string> actualTopology, IReadOnlyList<string> expectedWaypoints) {
            if (expectedWaypoints == null || expectedWaypoints.Count == 0) return true;
            if (actualTopology == null || actualTopology.Count == 0) return false;
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in actualTopology) if (!string.IsNullOrEmpty(b)) have.Add(b);
            foreach (var w in expectedWaypoints) {
                if (string.IsNullOrEmpty(w)) continue;
                if (!have.Contains(w)) return false;
            }
            return true;
        }
    }
}
