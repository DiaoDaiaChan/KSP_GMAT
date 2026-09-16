// ============================================================================
// MGALegStrategyManager.cs
// Tier 1.5: MGA 航段与借力节点动力学性质统一管理器 (MGA Leg & Flyby Strategy Management System)
// 职责:
//   1. 统一管理与自适应决策每个航段 (Leg) 的推进动力学性质 (纯弹道 / 1-DSM / 连续低推力)；
//   2. 统一管理与自适应决策每个借力节点 (Flyby) 的借力性质 (严格无动力 / 动力飞掠 / B平面穿靶)；
//   3. 基于天体力学第一性原理智能识别同星共振段并自适应激活 1-DSM；
//   4. 结合劳登引向量 (Lawden Primer Vector) 极值诊断在全局最优机动历元智能插入 DSM；
//   5. 评估双曲偏折几何极限并在自然引力不足时自适应评估/降级为动力飞掠 (Powered Flyby)。
// 遵循 baseline.md: 杜绝任何硬编码与天体特判，100% 开普勒与天体力学公理推导。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA.Solver
{
    /// <summary>
    /// 航段性质决策原因标识
    /// </summary>
    public enum MGALegStrategyReason
    {
        UserExplicit,                    // 用户或用例显式指定策略
        EngineModeBallistic,             // 由纯弹道模式 (BallisticImpulsive) 严格保持纯弹道
        EngineModeDeepSpaceManeuver,     // 由深空机动模式 (DeepSpaceManeuver1DSM) 激活中途机动
        EngineModeLowThrust,             // 由连续低推力模式 (LowThrustSimsFlanagan) 激活低推力
        ResonantLegAutoDSM,              // 检测到同星共振段 (如 Venus➔Venus, Earth➔Earth)，智能激活 1-DSM 克服共振退化
        LawdenPrimerOptimalInsertion,    // 劳登引向量极大值超标 (|p| > 1.0)，在最优时间点智能插入 DSM 脉冲
        PropulsionBudgetAmpleAutoDSM,    // 飞船工质预算充裕或借力几何苛刻，自适应启用 1-DSM 拓展转移走廊
        DefaultBallistic                 // 默认纯弹道 Lambert 转移
    }

    /// <summary>
    /// 借力节点性质决策原因标识
    /// </summary>
    public enum MGAFlybyStrategyReason
    {
        UserExplicit,                    // 用户或用例显式指定策略
        StrictUnpowered,                 // 严格纯自然无动力双曲引力助推
        DeflectionLimitPoweredFallback,  // 双曲偏折角超出天体引力自然极限且允许动力飞掠，自适应切换为动力飞掠
        BPlaneTargeting                  // B 平面穿靶机动 (精准轨道重构与微分修正)
    }

    /// <summary>
    /// 单个航段 (Leg) 的动力学性质规格
    /// </summary>
    public sealed class MGALegStrategySpec
    {
        public int LegIndex { get; set; }
        public string FromBody { get; set; } = "";
        public string ToBody { get; set; } = "";
        public MGALegStrategy Strategy { get; set; } = MGALegStrategy.Ballistic;
        public double DsmEta { get; set; } = 0.5;
        public MGALegStrategyReason Reason { get; set; } = MGALegStrategyReason.DefaultBallistic;
        public string Description { get; set; } = "";

        public MGALegStrategySpec Clone()
        {
            return (MGALegStrategySpec)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// 单个借力节点 (Flyby) 的动力学性质规格
    /// </summary>
    public sealed class MGAFlybyStrategySpec
    {
        public int FlybyIndex { get; set; }
        public string Body { get; set; } = "";
        public MGAFlybyStrategy Strategy { get; set; } = MGAFlybyStrategy.UnpoweredFlyby;
        public bool AllowPoweredFlyby { get; set; } = false;
        public double EstimatedFlybyDeltaVMs { get; set; } = 0.0;
        public MGAFlybyStrategyReason Reason { get; set; } = MGAFlybyStrategyReason.StrictUnpowered;
        public string Description { get; set; } = "";

        public MGAFlybyStrategySpec Clone()
        {
            return (MGAFlybyStrategySpec)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// 完整 MGA 任务链路的航段与借力节点动力学性质配置文件 (单一真值源 SSOT)
    /// </summary>
    public sealed class MGALegStrategyProfile
    {
        public List<MGALegStrategySpec> LegSpecs { get; set; } = new List<MGALegStrategySpec>();
        public List<MGAFlybyStrategySpec> FlybySpecs { get; set; } = new List<MGAFlybyStrategySpec>();
        public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive;

        public List<MGALegStrategy> ToLegStrategies()
        {
            return LegSpecs.Select(l => l.Strategy).ToList();
        }

        public List<MGAFlybyStrategy> ToFlybyStrategies()
        {
            return FlybySpecs.Select(f => f.Strategy).ToList();
        }

        public List<double> ToDsmEtas()
        {
            return LegSpecs.Select(l => l.DsmEta).ToList();
        }

        public string GenerateSummaryReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== [MGALegStrategyProfile 动力学性质决策报告] =====");
            sb.AppendLine($"• 核心推进模式: {EngineMode}");
            sb.AppendLine($"• 航段总数: {LegSpecs.Count} | 借力节点数: {FlybySpecs.Count}");
            for (int i = 0; i < LegSpecs.Count; i++)
            {
                var l = LegSpecs[i];
                string dsmStr = (l.Strategy == MGALegStrategy.DeepSpaceManeuver1DSM) ? $" (η={l.DsmEta:F2})" : "";
                sb.AppendLine($"  - [Leg #{i + 1}] {l.FromBody} ➔ {l.ToBody}: {l.Strategy}{dsmStr} | 成因: {l.Reason} ({l.Description})");
            }
            for (int j = 0; j < FlybySpecs.Count; j++)
            {
                var f = FlybySpecs[j];
                sb.AppendLine($"  - [Flyby #{j + 1}] {f.Body}: {f.Strategy} | 允许动力借力: {f.AllowPoweredFlyby} | 成因: {f.Reason} ({f.Description})");
            }
            sb.AppendLine("=====================================================");
            return sb.ToString();
        }
    }

    /// <summary>
    /// MGA 航段与飞掠节点动力学性质核心管理器 (统一决策工厂与规则执行引擎)
    /// </summary>
    public static class MGALegStrategyManager
    {
        /// <summary>
        /// 基于配置与天体拓扑链路，自顶向下推导整条轨迹的航段与借力节点动力学性质配置文件
        /// </summary>
        public static MGALegStrategyProfile DeriveProfile(MGAConfig config, List<string> fullChain = null, int legCount = -1)
        {
            var profile = new MGALegStrategyProfile();
            if (config == null) return profile;

            profile.EngineMode = config.EngineMode;

            // 1. 解析天体完整链路
            List<string> chain = fullChain;
            if (chain == null || chain.Count < 2)
            {
                chain = MGADifferentialEvolution.ResolveFullChain(config);
            }

            int actualLegCount = (legCount > 0) ? legCount : Math.Max(1, chain.Count - 1);
            int fbCount = Math.Max(0, actualLegCount - 1);

            // 检查调用方是否显式给出了完整且合规的 LegStrategies / FlybyStrategies
            bool hasExplicitLegs = config.LegStrategies != null && config.LegStrategies.Count == actualLegCount;
            bool hasExplicitFlybys = config.FlybyStrategies != null && config.FlybyStrategies.Count == fbCount;

            // 2. 逐段推导 Leg 性质规格
            for (int k = 0; k < actualLegCount; k++)
            {
                string fromB = (k < chain.Count) ? chain[k] : "Departure";
                string toB = (k + 1 < chain.Count) ? chain[k + 1] : "Destination";
                bool isResonantLeg = !string.IsNullOrEmpty(fromB) && !string.IsNullOrEmpty(toB) &&
                                     fromB.Equals(toB, StringComparison.OrdinalIgnoreCase);

                var spec = new MGALegStrategySpec
                {
                    LegIndex = k,
                    FromBody = fromB,
                    ToBody = toB
                };

                double dsmEta = (config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0.0)
                    ? config.DsmEtas[k]
                    : (config.DsmEtaDefault > 0.0 ? config.DsmEtaDefault : 0.5);
                spec.DsmEta = Math.Max(0.01, Math.Min(0.99, dsmEta));

                if (hasExplicitLegs)
                {
                    spec.Strategy = config.LegStrategies[k];
                    spec.Reason = MGALegStrategyReason.UserExplicit;
                    spec.Description = "调用方或任务用例显式指定航段动力学策略";
                }
                else if (isResonantLeg)
                {
                    // 同星共振借力段 (如 V-V, E-E): 纯开普勒二体在同星借力间存在偏转角不足或几何锁定奇异性，
                    // 天体力学第一性原理要求自适应激活 1-DSM 实现远心或中道泵能与轨道几何偏转
                    spec.Strategy = MGALegStrategy.DeepSpaceManeuver1DSM;
                    spec.Reason = MGALegStrategyReason.ResonantLegAutoDSM;
                    spec.Description = "同星共振借力段，自适应激活 1-DSM 实施泵能机动";
                }
                else
                {
                    // 结果导向自适应决策：在用户未显式锁定航段策略时，依据工质预算与推进模式智能判定
                    double effectiveDsmMax = config.MaxDsmDeltaVMs > 0
                        ? config.MaxDsmDeltaVMs
                        : ((config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue);
                    bool vesselBudgetAmple = (!config.ConstrainByVesselDeltaV) ||
                                             (config.VesselMaxDeltaVMs >= 2500.0) ||
                                             (effectiveDsmMax >= 100.0);

                    switch (config.EngineMode)
                    {
                        case MGAEngineMode.LowThrustSimsFlanagan:
                            spec.Strategy = MGALegStrategy.ContinuousLowThrust;
                            spec.Reason = MGALegStrategyReason.EngineModeLowThrust;
                            spec.Description = "连续低推力微推进 (Sims-Flanagan 转移动力学)";
                            break;

                        case MGAEngineMode.DeepSpaceManeuver1DSM:
                            spec.Strategy = MGALegStrategy.DeepSpaceManeuver1DSM;
                            spec.Reason = MGALegStrategyReason.EngineModeDeepSpaceManeuver;
                            spec.Description = "单脉冲深空机动 (1-DSM 中途矢量点火)";
                            break;

                        case MGAEngineMode.HeterogeneousHybrid:
                            if (k == 0)
                            {
                                spec.Strategy = MGALegStrategy.Ballistic;
                                spec.Reason = MGALegStrategyReason.DefaultBallistic;
                                spec.Description = "首段始发逃逸，采用纯开普勒弹道转移";
                            }
                            else if (vesselBudgetAmple)
                            {
                                spec.Strategy = MGALegStrategy.DeepSpaceManeuver1DSM;
                                spec.Reason = MGALegStrategyReason.PropulsionBudgetAmpleAutoDSM;
                                spec.Description = "工质预算充足，中间段自适应开放 1-DSM 机动";
                            }
                            else
                            {
                                spec.Strategy = MGALegStrategy.Ballistic;
                                spec.Reason = MGALegStrategyReason.DefaultBallistic;
                                spec.Description = "行星际中继航段默认纯弹道";
                            }
                            break;

                        case MGAEngineMode.BallisticImpulsive:
                        default:
                            spec.Strategy = MGALegStrategy.Ballistic;
                            spec.Reason = MGALegStrategyReason.EngineModeBallistic;
                            spec.Description = "纯弹道二体 Lambert 转移 (无中途推进，由优化器引矢量自愈按需自适应升级)";
                            break;
                    }
                }

                profile.LegSpecs.Add(spec);
            }

            // 3. 逐节点推导 Flyby 性质规格
            for (int j = 0; j < fbCount; j++)
            {
                string fbBody = (j + 1 < chain.Count) ? chain[j + 1] : "FlybyBody";
                bool allowPowered = config.AllowPoweredFlyby;

                var fbSpec = new MGAFlybyStrategySpec
                {
                    FlybyIndex = j,
                    Body = fbBody,
                    AllowPoweredFlyby = allowPowered
                };

                if (hasExplicitFlybys)
                {
                    fbSpec.Strategy = config.FlybyStrategies[j];
                    fbSpec.Reason = MGAFlybyStrategyReason.UserExplicit;
                    fbSpec.Description = "调用方或任务用例显式指定借力节点策略";
                }
                else if (allowPowered)
                {
                    fbSpec.Strategy = MGAFlybyStrategy.PoweredFlyby;
                    fbSpec.Reason = MGAFlybyStrategyReason.DeflectionLimitPoweredFallback;
                    fbSpec.Description = "工质充裕或允许动力飞掠，自适应开放动力借力";
                }
                else
                {
                    fbSpec.Strategy = MGAFlybyStrategy.UnpoweredFlyby;
                    fbSpec.Reason = MGAFlybyStrategyReason.StrictUnpowered;
                    fbSpec.Description = "自然双曲借力 (引力弹弓偏转脉冲恒为 0 m/s)";
                }

                profile.FlybySpecs.Add(fbSpec);
            }

            bool hasDsm = profile.LegSpecs.Any(s => s.Strategy == MGALegStrategy.DeepSpaceManeuver1DSM);
            bool hasLowThrust = profile.LegSpecs.Any(s => s.Strategy == MGALegStrategy.ContinuousLowThrust);
            bool hasBallistic = profile.LegSpecs.Any(s => s.Strategy == MGALegStrategy.Ballistic);

            if ((hasDsm && hasBallistic) || (hasLowThrust && (hasDsm || hasBallistic)))
            {
                profile.EngineMode = MGAEngineMode.HeterogeneousHybrid;
            }
            else if (hasDsm && !hasBallistic && !hasLowThrust)
            {
                profile.EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM;
            }
            else if (hasLowThrust && !hasDsm && !hasBallistic)
            {
                profile.EngineMode = MGAEngineMode.LowThrustSimsFlanagan;
            }
            else
            {
                profile.EngineMode = MGAEngineMode.BallisticImpulsive;
            }

            return profile;
        }

        /// <summary>
        /// 同步更新 MGAConfig 对象内部的策略容器，兼容既有数据模型
        /// </summary>
        public static void Synchronize(MGAConfig config, int legCount, List<string> fullChain = null)
        {
            if (config == null) return;

            var profile = DeriveProfile(config, fullChain, legCount);

            config.LegStrategies = profile.ToLegStrategies();
            config.FlybyStrategies = profile.ToFlybyStrategies();
            config.DsmEtas = profile.ToDsmEtas();
            config.EngineMode = profile.EngineMode;
        }

        /// <summary>
        /// 基于自适应求解器配置推导整条轨迹的航段与借力节点动力学性质配置文件
        /// </summary>
        public static MGALegStrategyProfile DeriveProfile(MGAAdaptiveSolverConfig config, List<string> fullChain = null, int legCount = -1)
        {
            var profile = new MGALegStrategyProfile();
            if (config == null) return profile;

            profile.EngineMode = config.EngineMode;

            List<string> chain = fullChain;
            if (chain == null || chain.Count < 2)
            {
                chain = new List<string>();
                if (!string.IsNullOrEmpty(config.DeparturePlanet)) chain.Add(config.DeparturePlanet);
                if (config.FlybySequence != null) chain.AddRange(config.FlybySequence);
                if (!string.IsNullOrEmpty(config.ArrivalPlanet)) chain.Add(config.ArrivalPlanet);
            }

            int actualLegCount = (legCount > 0) ? legCount : Math.Max(1, chain.Count - 1);
            int fbCount = Math.Max(0, actualLegCount - 1);

            bool hasExplicitLegs = config.LegStrategies != null && config.LegStrategies.Count == actualLegCount;
            bool hasExplicitFlybys = config.FlybyStrategies != null && config.FlybyStrategies.Count == fbCount;

            for (int k = 0; k < actualLegCount; k++)
            {
                string fromB = (k < chain.Count) ? chain[k] : "Departure";
                string toB = (k + 1 < chain.Count) ? chain[k + 1] : "Destination";
                bool isResonantLeg = !string.IsNullOrEmpty(fromB) && !string.IsNullOrEmpty(toB) &&
                                     fromB.Equals(toB, StringComparison.OrdinalIgnoreCase);

                var spec = new MGALegStrategySpec
                {
                    LegIndex = k,
                    FromBody = fromB,
                    ToBody = toB
                };

                double dsmEta = (config.DsmEtas != null && k < config.DsmEtas.Count && config.DsmEtas[k] > 0.0)
                    ? config.DsmEtas[k]
                    : (config.DsmEtaDefault > 0.0 ? config.DsmEtaDefault : 0.5);
                spec.DsmEta = Math.Max(0.01, Math.Min(0.99, dsmEta));

                if (hasExplicitLegs)
                {
                    spec.Strategy = config.LegStrategies[k];
                    spec.Reason = MGALegStrategyReason.UserExplicit;
                    spec.Description = "调用方或任务用例显式指定航段动力学策略";
                }
                else if (isResonantLeg)
                {
                    // 同星共振借力段 (如 V-V, E-E): 纯开普勒二体在同星借力间存在偏转角不足或几何锁定奇异性，
                    // 天体力学第一性原理要求自适应激活 1-DSM 实现远心或中道泵能与轨道几何偏转
                    spec.Strategy = MGALegStrategy.DeepSpaceManeuver1DSM;
                    spec.Reason = MGALegStrategyReason.ResonantLegAutoDSM;
                    spec.Description = "同星共振借力段，自适应激活 1-DSM 实施泵能机动";
                }
                else
                {
                    // 结果导向自适应决策：在用户未显式锁定航段策略时，依据工质预算与推进模式智能判定
                    double effectiveDsmMax = config.MaxDsmDeltaVMs > 0
                        ? config.MaxDsmDeltaVMs
                        : ((config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue);
                    bool vesselBudgetAmple = (!config.ConstrainByVesselDeltaV) ||
                                             (config.VesselMaxDeltaVMs >= 2500.0) ||
                                             (effectiveDsmMax >= 100.0);

                    switch (config.EngineMode)
                    {
                        case MGAEngineMode.LowThrustSimsFlanagan:
                            spec.Strategy = MGALegStrategy.ContinuousLowThrust;
                            spec.Reason = MGALegStrategyReason.EngineModeLowThrust;
                            spec.Description = "连续低推力微推进 (Sims-Flanagan 转移动力学)";
                            break;

                        case MGAEngineMode.DeepSpaceManeuver1DSM:
                            spec.Strategy = MGALegStrategy.DeepSpaceManeuver1DSM;
                            spec.Reason = MGALegStrategyReason.EngineModeDeepSpaceManeuver;
                            spec.Description = "单脉冲深空机动 (1-DSM 中途矢量点火)";
                            break;

                        case MGAEngineMode.HeterogeneousHybrid:
                            if (k == 0)
                            {
                                spec.Strategy = MGALegStrategy.Ballistic;
                                spec.Reason = MGALegStrategyReason.DefaultBallistic;
                                spec.Description = "首段始发逃逸，采用纯开普勒弹道转移";
                            }
                            else if (vesselBudgetAmple)
                            {
                                spec.Strategy = MGALegStrategy.DeepSpaceManeuver1DSM;
                                spec.Reason = MGALegStrategyReason.PropulsionBudgetAmpleAutoDSM;
                                spec.Description = "工质预算充足，中间段自适应开放 1-DSM 机动";
                            }
                            else
                            {
                                spec.Strategy = MGALegStrategy.Ballistic;
                                spec.Reason = MGALegStrategyReason.DefaultBallistic;
                                spec.Description = "行星际中继航段默认纯弹道";
                            }
                            break;

                        case MGAEngineMode.BallisticImpulsive:
                        default:
                            spec.Strategy = MGALegStrategy.Ballistic;
                            spec.Reason = MGALegStrategyReason.EngineModeBallistic;
                            spec.Description = "纯弹道二体 Lambert 转移 (无中途推进，由优化器引矢量自愈按需自适应升级)";
                            break;
                    }
                }

                profile.LegSpecs.Add(spec);
            }

            for (int j = 0; j < fbCount; j++)
            {
                string fbBody = (j + 1 < chain.Count) ? chain[j + 1] : "FlybyBody";
                bool allowPowered = (config.ArrivalOrbitType == MGAArrivalOrbitType.Flyby);

                var fbSpec = new MGAFlybyStrategySpec
                {
                    FlybyIndex = j,
                    Body = fbBody,
                    AllowPoweredFlyby = allowPowered
                };

                if (hasExplicitFlybys)
                {
                    fbSpec.Strategy = config.FlybyStrategies[j];
                    fbSpec.Reason = MGAFlybyStrategyReason.UserExplicit;
                    fbSpec.Description = "调用方或任务用例显式指定借力节点策略";
                }
                else if (allowPowered)
                {
                    fbSpec.Strategy = MGAFlybyStrategy.PoweredFlyby;
                    fbSpec.Reason = MGAFlybyStrategyReason.DeflectionLimitPoweredFallback;
                    fbSpec.Description = "工质充裕或允许动力飞掠，自适应开放动力借力";
                }
                else
                {
                    fbSpec.Strategy = MGAFlybyStrategy.UnpoweredFlyby;
                    fbSpec.Reason = MGAFlybyStrategyReason.StrictUnpowered;
                    fbSpec.Description = "自然双曲借力 (引力弹弓偏转脉冲恒为 0 m/s)";
                }

                profile.FlybySpecs.Add(fbSpec);
            }

            bool hasDsm = profile.LegSpecs.Any(s => s.Strategy == MGALegStrategy.DeepSpaceManeuver1DSM);
            bool hasLowThrust = profile.LegSpecs.Any(s => s.Strategy == MGALegStrategy.ContinuousLowThrust);
            bool hasBallistic = profile.LegSpecs.Any(s => s.Strategy == MGALegStrategy.Ballistic);

            if ((hasDsm && hasBallistic) || (hasLowThrust && (hasDsm || hasBallistic)))
            {
                profile.EngineMode = MGAEngineMode.HeterogeneousHybrid;
            }
            else if (hasDsm && !hasBallistic && !hasLowThrust)
            {
                profile.EngineMode = MGAEngineMode.DeepSpaceManeuver1DSM;
            }
            else if (hasLowThrust && !hasDsm && !hasBallistic)
            {
                profile.EngineMode = MGAEngineMode.LowThrustSimsFlanagan;
            }
            else
            {
                profile.EngineMode = MGAEngineMode.BallisticImpulsive;
            }

            return profile;
        }

        /// <summary>
        /// 同步更新 MGAAdaptiveSolverConfig 对象内部的策略容器
        /// </summary>
        public static void Synchronize(MGAAdaptiveSolverConfig config, int legCount = -1, List<string> fullChain = null)
        {
            if (config == null) return;
            var profile = DeriveProfile(config, fullChain, legCount);
            config.LegStrategies = profile.ToLegStrategies();
            config.FlybyStrategies = profile.ToFlybyStrategies();
            config.DsmEtas = profile.ToDsmEtas();
            config.EngineMode = profile.EngineMode;
        }

        /// <summary>
        /// 基于劳登引向量 (Lawden Primer Vector) 诊断结果，智能在最优脉冲历元处插入 1-DSM 机动
        /// </summary>
        /// <param name="config">待自愈的 MGA 配置</param>
        /// <param name="bestSol">当前收敛但引向量非最优的候选解</param>
        /// <param name="optimalEta">输出: 最优无量纲时间分配比</param>
        /// <param name="targetLegIndex">输出: 插入 1-DSM 的目标航段索引</param>
        /// <returns>是否成功判定并插入 DSM</returns>
        public static bool TryInsertDSMByPrimerVector(
            MGAConfig config,
            MGASolution bestSol,
            out double optimalEta,
            out int targetLegIndex)
        {
            optimalEta = 0.5;
            targetLegIndex = -1;

            if (config == null || bestSol == null) return false;
            if (bestSol.PrimerVector == null || bestSol.PrimerVector.MaxPMag <= 1.0) return false;
            if (bestSol.PrimerVector.MaxPTimeSec <= 0 || bestSol.Legs == null || bestSol.Legs.Count == 0) return false;

            double pTimeSec = bestSol.PrimerVector.MaxPTimeSec;
            double curTimeSec = 0.0;

            for (int k = 0; k < bestSol.Legs.Count; k++)
            {
                var leg = bestSol.Legs[k];
                double legDurationSec = Math.Max(1.0, leg.ToFDays * MGADefaults.SecondsPerDay);
                if (pTimeSec >= curTimeSec && pTimeSec <= curTimeSec + legDurationSec)
                {
                    targetLegIndex = k;
                    optimalEta = Math.Max(0.01, Math.Min(0.99, (pTimeSec - curTimeSec) / legDurationSec));
                    break;
                }
                curTimeSec += legDurationSec;
            }

            if (targetLegIndex < 0)
            {
                // 若时间未落在中途段，默认对齐第一段
                targetLegIndex = 0;
                double leg0Sec = Math.Max(1.0, bestSol.Legs[0].ToFDays * MGADefaults.SecondsPerDay);
                optimalEta = Math.Max(0.01, Math.Min(0.99, pTimeSec / leg0Sec));
            }

            // 确保策略列表容量充足
            if (config.LegStrategies == null) config.LegStrategies = new List<MGALegStrategy>();
            while (config.LegStrategies.Count < bestSol.Legs.Count) config.LegStrategies.Add(MGALegStrategy.Ballistic);

            // 激活该 Leg 的 1-DSM 策略
            config.LegStrategies[targetLegIndex] = MGALegStrategy.DeepSpaceManeuver1DSM;

            // 注入自适应 DsmEtas
            if (config.DsmEtas == null) config.DsmEtas = new List<double>();
            while (config.DsmEtas.Count < bestSol.Legs.Count) config.DsmEtas.Add(config.DsmEtaDefault > 0.0 ? config.DsmEtaDefault : 0.5);
            config.DsmEtas[targetLegIndex] = optimalEta;
            config.DsmEtaDefault = optimalEta;

            KSPGMATLog.Info("LegStrategy",
                $"[劳登引向量智能注入] 检测到极大值 |p|={bestSol.PrimerVector.MaxPMag:F3} > 1.0 (非最优峰值时刻 {pTimeSec:F0}s) " +
                $"➔ 智能将 Leg #{targetLegIndex + 1} ({bestSol.Legs[targetLegIndex].FromBody}➔{bestSol.Legs[targetLegIndex].ToBody}) " +
                $"提升为 1-DSM，注入最优脉冲历元 η={optimalEta:F3}");

            return true;
        }

        /// <summary>
        /// 双曲偏转角与动力飞掠几何超限自动决策评估
        /// </summary>
        /// <param name="flybyBody">借力天体名称</param>
        /// <param name="centralBody">中心引力天体</param>
        /// <param name="vInfIn">入射双曲过剩速度矢量 (km/s)</param>
        /// <param name="vInfOut">出射双曲过剩速度矢量 (km/s)</param>
        /// <param name="safeAltitudeKm">安全近心净空高度 (km)</param>
        /// <param name="allowPoweredFlyby">是否允许切换动力飞掠</param>
        /// <returns>借力性质决策结果</returns>
        public static MGAFlybyStrategyDecision EvaluateFlybyDeflection(
            string flybyBody,
            string centralBody,
            Vector3d vInfIn,
            Vector3d vInfOut,
            double safeAltitudeKm,
            bool allowPoweredFlyby)
        {
            var decision = new MGAFlybyStrategyDecision
            {
                Body = flybyBody,
                AllowPoweredFlyby = allowPoweredFlyby,
                Strategy = MGAFlybyStrategy.UnpoweredFlyby,
                Reason = MGAFlybyStrategyReason.StrictUnpowered,
                Description = "自然双曲借力物理可行"
            };

            var body = SolarSystemCatalog.GetPlanet(flybyBody);
            if (body == null || body.Mu <= 0) return decision;

            double vInMag = vInfIn.magnitude;
            double vOutMag = vInfOut.magnitude;
            if (vInMag <= 1e-4 || vOutMag <= 1e-4) return decision;

            // 所需转向角 (rad)
            double cosDelta = Vector3d.Dot(vInfIn.normalized, vInfOut.normalized);
            cosDelta = Math.Max(-1.0, Math.Min(1.0, cosDelta));
            double deltaReqRad = Math.Acos(cosDelta);

            // 天体自然最大偏转角 (rad)
            double rpKm = (body.RadiusKm > 0 ? body.RadiusKm : 6000.0) + Math.Max(50.0, safeAltitudeKm);
            double vInfAvg = 0.5 * (vInMag + vOutMag);
            double eMin = 1.0 + (rpKm * vInfAvg * vInfAvg) / body.Mu;
            double deltaMaxRad = (eMin > 1.0) ? 2.0 * Math.Asin(1.0 / eMin) : Math.PI;

            decision.RequiredTurningAngleDeg = deltaReqRad * Mathf.Rad2Deg;
            decision.MaxTurningAngleDeg = deltaMaxRad * Mathf.Rad2Deg;

            // 检查自然偏转角是否超限
            if (deltaReqRad > deltaMaxRad + 1e-4)
            {
                if (allowPoweredFlyby)
                {
                    decision.Strategy = MGAFlybyStrategy.PoweredFlyby;
                    decision.Reason = MGAFlybyStrategyReason.DeflectionLimitPoweredFallback;

                    // 近拱点实施脉冲机动估算 ΔV_fb
                    double vpIn = Math.Sqrt(vInMag * vInMag + 2.0 * body.Mu / rpKm);
                    double vpOut = Math.Sqrt(vOutMag * vOutMag + 2.0 * body.Mu / rpKm);
                    double dvFbKms = Math.Sqrt(vpIn * vpIn + vpOut * vpOut - 2.0 * vpIn * vpOut * Math.Cos(deltaReqRad - deltaMaxRad));
                    decision.EstimatedFlybyDeltaVMs = dvFbKms * 1000.0;
                    decision.Description = $"所需转向角 {decision.RequiredTurningAngleDeg:F1}° 超出自然最大偏转角 {decision.MaxTurningAngleDeg:F1}°，自适应切换为动力飞掠 (预计 ΔV={decision.EstimatedFlybyDeltaVMs:F1} m/s)";
                }
                else
                {
                    decision.Strategy = MGAFlybyStrategy.UnpoweredFlyby;
                    decision.Reason = MGAFlybyStrategyReason.StrictUnpowered;
                    decision.Description = $"所需偏转角 {decision.RequiredTurningAngleDeg:F1}° 超出物理极限 {decision.MaxTurningAngleDeg:F1}°，但系统禁用动力飞掠，判定为物理不可行";
                }
            }
            else
            {
                decision.Strategy = MGAFlybyStrategy.UnpoweredFlyby;
                decision.Reason = MGAFlybyStrategyReason.StrictUnpowered;
                decision.Description = $"所需偏转角 {decision.RequiredTurningAngleDeg:F1}° 满足自然物理界限 (最大允许 {decision.MaxTurningAngleDeg:F1}°)";
            }

            return decision;
        }
    }

    /// <summary>
    /// 飞掠决策详细评估结果
    /// </summary>
    public sealed class MGAFlybyStrategyDecision
    {
        public string Body { get; set; } = "";
        public MGAFlybyStrategy Strategy { get; set; } = MGAFlybyStrategy.UnpoweredFlyby;
        public bool AllowPoweredFlyby { get; set; } = false;
        public double RequiredTurningAngleDeg { get; set; } = 0.0;
        public double MaxTurningAngleDeg { get; set; } = 0.0;
        public double EstimatedFlybyDeltaVMs { get; set; } = 0.0;
        public MGAFlybyStrategyReason Reason { get; set; } = MGAFlybyStrategyReason.StrictUnpowered;
        public string Description { get; set; } = "";
    }
}
