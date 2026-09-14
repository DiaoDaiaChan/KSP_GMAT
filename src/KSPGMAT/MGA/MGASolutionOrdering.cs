// ============================================================================
// MGASolutionOrdering.cs
// 求解结果的确定性排序 (Deterministic Solution Ordering)
//
// 【修复的问题】
//   求解链多处使用 ConcurrentBag 聚合多线程结果 (窗口级 Parallel.For), 其枚举顺序
//   随线程调度变化。这些集合随后仅按 TotalDeltaVKms 排序, 而**近似并列的解**会以
//   不确定的顺序出现 —— 于是"固定随机种子 = 可复现"这一前提被破坏。
//
//   实测证据 (13 任务全量盲搜, 同一构建连跑两次):
//     cassini  11031.1 m/s   vs   11109.8 m/s   (相差 78.7 m/s)
//   其余 12 个任务逐位一致, 说明差异只在存在近并列解的拓扑上显现,
//   即线程时序决定了哪个并列解胜出。
//
// 【修法】
//   为所有"决定胜出解"的排序补充**确定性次级键**:
//     主键: TotalDeltaVKms (解质量, 必须保持)
//     次键: 发射历元 (四舍五入到 ms) -> 总航时 -> 拓扑字符串 (序数比较)
//   次级键全部由解自身的物理量导出, 与线程时序无关, 因此
//   在固定种子下结果完全可复现, 同时不改变"更优解优先"的语义。
//
// 用法: 将 `xs.OrderBy(s => s.TotalDeltaVKms)` 替换为 `xs.Deterministic()`。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>求解结果的确定性排序扩展</summary>
    public static class MGASolutionOrdering
    {
        /// <summary>发射历元次级键的量化精度 (秒): 1 ms，足以稳定区分不同解而不引入数值噪声。</summary>
        private const double EpochQuantizationSec = 1.0e-3;

        /// <summary>航时次级键的量化精度 (天)。</summary>
        private const double TofQuantizationDays = 1.0e-4;

        /// <summary>
        /// 按 (总ΔV -> 发射历元 -> 总航时 -> 拓扑) 做**确定性**升序排序。
        /// 主键语义不变 (仍以解质量为准), 次级键仅用于消除并列解的顺序不确定性。
        /// </summary>
        public static IEnumerable<MGASolution> Deterministic(this IEnumerable<MGASolution> source)
        {
            if (source == null) return Enumerable.Empty<MGASolution>();

            return source
                .Where(s => s != null)
                .OrderBy(s => s.TotalDeltaVKms)
                .ThenBy(s => Math.Round(s.DepartureUT / EpochQuantizationSec))
                .ThenBy(s => Math.Round(s.TotalFlightDays / TofQuantizationDays))
                .ThenBy(TopologyKey, StringComparer.Ordinal);
        }

        /// <summary>
        /// 按 (总航时 -> 总ΔV -> 发射历元 -> 拓扑) 做**确定性**升序排序。
        /// 与 <see cref="Deterministic"/> 同构, 仅主键换成航时, 供"最快到达"轨使用。
        /// </summary>
        public static IEnumerable<MGASolution> DeterministicByFlightDays(this IEnumerable<MGASolution> source)
        {
            if (source == null) return Enumerable.Empty<MGASolution>();

            return source
                .Where(s => s != null)
                .OrderBy(s => s.TotalFlightDays)
                .ThenBy(s => s.TotalDeltaVKms)
                .ThenBy(s => Math.Round(s.DepartureUT / EpochQuantizationSec))
                .ThenBy(TopologyKey, StringComparer.Ordinal);
        }

        /// <summary>
        /// 基因组 (决策向量) 字典序比较器。用于为"适应度完全并列"的候选提供确定性次级键 ——
        /// 并行容器 (ConcurrentBag) 的枚举顺序随线程调度变化, 稳定排序本身无法消除该不确定性。
        /// </summary>
        public sealed class GenomeComparer : IComparer<double[]>
        {
            public static readonly GenomeComparer Instance = new GenomeComparer();

            public int Compare(double[] a, double[] b)
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return -1;
                if (b == null) return 1;

                int n = (a.Length < b.Length) ? a.Length : b.Length;
                for (int i = 0; i < n; i++)
                {
                    int c = a[i].CompareTo(b[i]);
                    if (c != 0) return c;
                }
                return a.Length.CompareTo(b.Length);
            }
        }

        /// <summary>
        /// 拓扑签名: 由各航段的 From/To 天体与航时构成, 用于在完全并列时给出稳定顺序。
        /// </summary>
        private static string TopologyKey(MGASolution s)
        {
            if (s == null || s.Legs == null || s.Legs.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var leg in s.Legs)
            {
                if (leg == null) continue;
                sb.Append(leg.FromBody ?? "?").Append('>').Append(leg.ToBody ?? "?");
                sb.Append('@').Append(Math.Round(leg.ToFDays / TofQuantizationDays));
                sb.Append('|');
            }
            return sb.ToString();
        }
        /// <summary>
        /// 综合考虑工质最优与航时惩罚的帕累托平衡选解器 (Pareto-Knee Balanced Selector)。
        /// 在 Pareto 前沿解集中，识别边际替换率合理的膝部解，抑制航时无限拖延换取微小工质降低的伪最优陷阱。
        /// </summary>
        public static MGASolution SelectParetoKneeSolution(IEnumerable<MGASolution> paretoFront, MGAConfig config)
        {
            double dvRefKms = MGAPhysicsLossEngine.LossReferenceCache.Get(config);
            return SelectParetoKneeSolution(paretoFront, config?.CentralBody ?? "Sun", dvRefKms);
        }

        /// <summary>
        /// 帕累托膝部选解。
        ///
        /// 【2026-09-14 第二轮审计修正 —— 消除"目标函数不一致"】
        /// 原实现在此处**就地重写**了一份与 <c>MGAPhysicsLossEngine</c> 形式不同的航时软界:
        ///     此处   : `dvKms += (0.15*e + 0.15*e²) * dvKms`   ← 乘在**解自身 ΔV** 上
        ///     损失侧 : `loss  += (0.15*e + 0.15*e²)`           ← 加在**预算单位**上
        /// 后果: 优化器最小化的目标与最终选解所用的排序**不是同一个函数** —— 同一个解在两个阶段的
        /// 相对优劣可能相反; 且乘性形式会**对高 ΔV 解施加更大的绝对惩罚**, 与"压制低 ΔV 长航时退化解"
        /// 的初衷方向相反。
        /// 现改为: 两处共用 <see cref="MGAPhysicsLossEngine.ToFSoftExcessPenalty"/>, 并统一为
        /// **以参考预算 <paramref name="dvRefKms"/> 为单位的加性惩罚**。
        /// </summary>
        public static MGASolution SelectParetoKneeSolution(IEnumerable<MGASolution> paretoFront, string centralBody = "Sun", double dvRefKms = 0.0)
        {
            if (paretoFront == null) return null;
            var list = paretoFront.Where(s => s != null && s.AllLegsFeasible).ToList();
            if (list.Count == 0) return paretoFront.FirstOrDefault();
            if (list.Count == 1) return list[0];

            double nominalDays = MGAPhysicsLossEngine.ResolveNominalTofDays(list[0], centralBody);
            if (nominalDays <= 0) return list.OrderBy(s => s.TotalDeltaVKms).First();

            // 参考预算未知时退化为"以本前沿中位 ΔV 为尺度" —— 仍在同一量纲上加性,
            // 只是尺度来自当前解集而非任务预算; 该退化路径已在报告中登记。
            double penaltyScaleKms = dvRefKms;
            if (penaltyScaleKms <= 1e-9)
            {
                var ordered = list.OrderBy(s => s.TotalDeltaVKms).ToList();
                penaltyScaleKms = ordered[ordered.Count / 2].TotalDeltaVKms;
            }
            if (penaltyScaleKms <= 1e-9) penaltyScaleKms = 1.0;

            return list
                .OrderBy(s => s.TotalDeltaVKms
                              + MGAPhysicsLossEngine.ToFSoftExcessPenalty(s.TotalFlightDays, nominalDays) * penaltyScaleKms)
                .ThenBy(s => s.TotalDeltaVKms)
                .ThenBy(s => s.TotalFlightDays)
                .First();
        }
    }
}
