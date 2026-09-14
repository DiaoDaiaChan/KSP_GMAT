// ============================================================================
// MGAConstraintManager.cs
// Tier 1.5: MGA 统一约束管理器 (MGA Constraint & Boundary Management System)
// 职责:
//   1. 承接 MGAMasterOptimizer 顶层高价值工程决策参数 (MGAHighValueParameters)；
//   2. 建立单一真值源 (SSOT) 的全物理/工程约束模型 (MGAConstraintProfile)；
//   3. 自顶向下自适应转译并分发约束至求解器、损失函数与物理自愈引擎；
//   4. 自底向上对任意收敛轨迹进行统一合规审计与打标 (MGAConstraintAuditReport)。
// 遵循 baseline.md: 杜绝任何硬编码与天体特判，100% 开普勒公理推导。
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
    /// MGA 约束类型标识 (全工程统一枚举)
    /// </summary>
    public enum MGAConstraintType
    {
        // 1. 硬物理与轨道动力学边界 (Hard Physics & Orbital Dynamics)
        SafePeriapsis = 0,         // 借力天体近拱点安全净空 (避免穿透地表/大气层)
        StrictUnpoweredFlyby = 1,  // 严格无动力双曲弹弓 (飞掠机动脉冲恒为 0.00 m/s)
        FlybyDeflectionLimit = 2,  // 双曲偏折角物理极限 (满足天体引力最大偏折能级)
        InclinationLock = 3,       // 发射停泊轨道倾角/RAAN 共面锁相
        LegFeasibility = 4,        // 开普勒/Lambert 边值数值几何闭环可行性

        // 2. 推进系统与机动能力预算 (Propulsion Capabilities & Budgets)
        TotalVesselDeltaV = 10,    // 飞船总机动工质预算上限 (m/s)
        MaxDsmDeltaV = 11,         // 单脉冲中途深空机动 (DSM) 预算上限 (m/s)
        LaunchC3Ceiling = 12,      // 始发天体停泊轨出射 C3 / 注入冲量限额 (m/s)
        ArrivalVinfLimit = 13,     // 目标天体到达双曲过剩速度上限 (km/s)

        // 3. 航天器物理走廊与空间环境安全 (Environmental Corridor & Thermal)
        MinSolarDistance = 20,     // 最小近日点安全距离 (AU, 防热罩热控安全底线)
        MaxSolarDistance = 21,     // 最大远日点安全距离 (AU, 太阳能帆板/能源有效性上限)

        // 4. 任务寿命与时序边界 (Mission Lifetime & Temporal Bounds)
        MaxMissionDuration = 30,   // 全任务寿命总航时上限 (年/天)
        MinMissionDuration = 31,   // 全任务允许最短总航时 (年/天)
        MaxResonanceWait = 32,     // 单个同星共振段最大等待时间 (年/天)

        // 5. 拓扑引导与引力辅助策略 (Topology Strategy & Heuristics)
        MaxFlybyCount = 40,        // 全航程允许的最大中间借力次数
        OuterGasGiantAssist = 41   // 外太阳系深空探索巨行星超级引力跳板吸附
    }

    /// <summary>
    /// 约束分类等级
    /// </summary>
    public enum MGAConstraintCategory
    {
        HardPhysics,           // 硬物理守恒与几何边界 (不可违背，违约解不可行)
        PropulsionBudget,      // 推进系统工程预算与能耗限额
        EnvironmentalCorridor, // 热控、光照与空间环境安全走廊
        TemporalLifetime,      // 任务寿命、航时与时序跨度
        TopologyStrategy       // 拓扑爬升链与辅助策略
    }

    /// <summary>
    /// 约束违约判定级别
    /// </summary>
    public enum MGAConstraintSeverity
    {
        Pass,          // 完美合规
        Warning,       // 接近边界 (处于容差缓冲区内)
        Violation,     // 确定性违约 (超出允许公差)
        CriticalFatal  // 致命违规 (如撞击天体表面、极端物理发散)
    }

    /// <summary>
    /// 单个约束项规格契约
    /// </summary>
    public sealed class MGAConstraintSpec
    {
        public MGAConstraintType Type { get; set; }
        public string Name { get; set; } = "";
        public MGAConstraintCategory Category { get; set; }
        public bool IsHardConstraint { get; set; } = false;
        public bool IsEnabled { get; set; } = true;
        public double ThresholdValue { get; set; } = 0.0;
        public double ToleranceValue { get; set; } = 0.0;
        public double NormalizedScale { get; set; } = 1.0;
        public string Unit { get; set; } = "";
        public string Description { get; set; } = "";

        public MGAConstraintSpec Clone()
        {
            return (MGAConstraintSpec)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// 完整任务约束配置文件 (单一真值源 SSOT)
    /// </summary>
    public sealed class MGAConstraintProfile
    {
        private readonly Dictionary<MGAConstraintType, MGAConstraintSpec> _specs =
            new Dictionary<MGAConstraintType, MGAConstraintSpec>();

        public IReadOnlyDictionary<MGAConstraintType, MGAConstraintSpec> Specs => _specs;

        public void SetSpec(MGAConstraintSpec spec)
        {
            if (spec != null) _specs[spec.Type] = spec;
        }

        public MGAConstraintSpec GetSpec(MGAConstraintType type)
        {
            _specs.TryGetValue(type, out var s);
            return s;
        }

        public bool IsEnabled(MGAConstraintType type)
        {
            return _specs.TryGetValue(type, out var s) && s.IsEnabled;
        }

        public double GetThreshold(MGAConstraintType type, double fallback = 0.0)
        {
            return _specs.TryGetValue(type, out var s) ? s.ThresholdValue : fallback;
        }

        public double GetNormalizedScale(MGAConstraintType type, double fallback = 1.0)
        {
            return _specs.TryGetValue(type, out var s) ? s.NormalizedScale : fallback;
        }

        public MGAConstraintProfile Clone()
        {
            var p = new MGAConstraintProfile();
            foreach (var kvp in _specs)
            {
                p._specs[kvp.Key] = kvp.Value.Clone();
            }
            return p;
        }
    }

    /// <summary>
    /// 单项约束审计结果
    /// </summary>
    public sealed class MGAConstraintItemAudit
    {
        public MGAConstraintType Type { get; set; }
        public string Name { get; set; } = "";
        public MGAConstraintCategory Category { get; set; }
        public bool IsHardConstraint { get; set; }
        public MGAConstraintSeverity Severity { get; set; }
        public double ActualValue { get; set; }
        public double ThresholdValue { get; set; }
        public double RawViolation { get; set; }
        public double NormalizedViolation { get; set; }
        public string Unit { get; set; } = "";
        public string Description { get; set; } = "";

        public bool IsSatisfied => Severity == MGAConstraintSeverity.Pass || Severity == MGAConstraintSeverity.Warning;
    }

    /// <summary>
    /// 完整方案合规审计报告
    /// </summary>
    public sealed class MGAConstraintAuditReport
    {
        public List<MGAConstraintItemAudit> Items { get; set; } = new List<MGAConstraintItemAudit>();
        public bool IsFullyFeasible { get; set; } = true;
        public int TotalViolationsCount { get; set; } = 0;
        public int HardViolationsCount { get; set; } = 0;
        public double MaxNormalizedViolation { get; set; } = 0.0;
        public MGAConstraintItemAudit WorstViolationItem { get; set; }
        public string SummaryText { get; set; } = "";
        public string MarkdownReport { get; set; } = "";
    }

    /// <summary>
    /// 核心约束管理器 (MGA Constraint Manager)
    /// 负责在 MGAMasterOptimizer 与中底层各动力学模块间实现无缝转译、参数注入与全景合规审计
    /// </summary>
    public static class MGAConstraintManager
    {
        /// <summary>
        /// 从顶层 MGAHighValueParameters 自适应构建标准约束配置文件
        /// </summary>
        public static MGAConstraintProfile CreateProfile(
            MGAHighValueParameters hp,
            string departureBody = null,
            string arrivalBody = null)
        {
            if (hp == null) hp = new MGAHighValueParameters();
            var profile = new MGAConstraintProfile();

            // 1. 借力近拱点安全净空约束
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.SafePeriapsis,
                Name = "借力天体近心点净空高度",
                Category = MGAConstraintCategory.HardPhysics,
                IsHardConstraint = true,
                IsEnabled = true,
                ThresholdValue = hp.FlybySafeAltitudeMarginKm,
                ToleranceValue = 0.0,
                NormalizedScale = Math.Max(10.0, hp.FlybySafeAltitudeMarginKm),
                Unit = "km",
                Description = "借力近拱点半径必须严格大于天体名义半径与大气/地形安全裕度之和"
            });

            // 2. 严格无动力双曲弹弓约束
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.StrictUnpoweredFlyby,
                Name = "纯自然无动力飞掠脉冲",
                Category = MGAConstraintCategory.HardPhysics,
                IsHardConstraint = true,
                IsEnabled = hp.StrictUnpoweredFlyby,
                ThresholdValue = 0.0,
                ToleranceValue = 1e-4, // 0.1 mm/s 数值容差
                NormalizedScale = 10.0, // 10 m/s 违约基准尺度
                Unit = "m/s",
                Description = "自然双曲引力弹弓中借力脉冲 DeltaV_fb 恒为 0.00 m/s，严禁有动力伪借力"
            });

            // 3. 双曲转弯角极限约束
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.FlybyDeflectionLimit,
                Name = "双曲偏折角几何极限",
                Category = MGAConstraintCategory.HardPhysics,
                IsHardConstraint = true,
                IsEnabled = true,
                ThresholdValue = 0.0,
                ToleranceValue = 1e-4,
                NormalizedScale = 0.1, // 0.1 rad 偏折超出尺度
                Unit = "rad",
                Description = "实际所需转角不得超出借力天体引力势在安全近掠半径下的最大解析双曲偏折角"
            });

            // 4. 发射倾角与升交点赤经锁定约束
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.InclinationLock,
                Name = "始发停泊轨道共面锁定",
                Category = MGAConstraintCategory.HardPhysics,
                IsHardConstraint = true,
                IsEnabled = true,
                ThresholdValue = 0.0,
                ToleranceValue = 1.0, // 1.0° 容差
                NormalizedScale = 1.0,
                Unit = "deg",
                Description = "模式 2 在轨锁定状态下，实际出射点火平面必须与停泊轨道严格对齐"
            });

            // 5. 航段数值开普勒闭环可行性
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.LegFeasibility,
                Name = "航段几何数值闭环",
                Category = MGAConstraintCategory.HardPhysics,
                IsHardConstraint = true,
                IsEnabled = true,
                ThresholdValue = 0.0,
                ToleranceValue = 0.0,
                NormalizedScale = 1.0,
                Unit = "bool",
                Description = "所有航段 Lambert 边值求解必须存在物理真实解"
            });

            // 6. 飞船总工质机动预算
            double vRef = (hp.MaxVesselDeltaVMs > 0) ? hp.MaxVesselDeltaVMs : 4000.0;
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.TotalVesselDeltaV,
                Name = "飞船总机动工质预算",
                Category = MGAConstraintCategory.PropulsionBudget,
                IsHardConstraint = (hp.MaxVesselDeltaVMs > 0),
                IsEnabled = (hp.MaxVesselDeltaVMs > 0),
                ThresholdValue = hp.MaxVesselDeltaVMs,
                ToleranceValue = 10.0, // 10 m/s
                NormalizedScale = vRef,
                Unit = "m/s",
                Description = "全航程总机动 Delta-V (发射额外出射 + 途经 DSM + 终点捕获) 不得超出飞船推进上限"
            });

            // 7. 单脉冲途经深空机动 (DSM) 预算
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MaxDsmDeltaV,
                Name = "单脉冲深空机动上限",
                Category = MGAConstraintCategory.PropulsionBudget,
                IsHardConstraint = (hp.MaxDsmDeltaVMs > 0),
                IsEnabled = (hp.MaxDsmDeltaVMs > 0),
                ThresholdValue = hp.MaxDsmDeltaVMs,
                ToleranceValue = 5.0,
                NormalizedScale = Math.Max(100.0, hp.MaxDsmDeltaVMs),
                Unit = "m/s",
                Description = "中途任一单次深空机动脉冲不得超出推力器单次点火上限"
            });

            // 8. 始发天体停泊轨出射 C3 / 注入限额
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.LaunchC3Ceiling,
                Name = "始发停泊轨出射冲量门禁",
                Category = MGAConstraintCategory.PropulsionBudget,
                IsHardConstraint = (hp.LaunchDeltaVCeilingMs > 0),
                IsEnabled = (hp.LaunchDeltaVCeilingMs > 0),
                ThresholdValue = hp.LaunchDeltaVCeilingMs,
                ToleranceValue = 10.0,
                NormalizedScale = Math.Max(1000.0, hp.LaunchDeltaVCeilingMs),
                Unit = "m/s",
                Description = "运载火箭注入级提供的最大出射速度增量门禁"
            });

            // 9. 到达双曲过剩速度上限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.ArrivalVinfLimit,
                Name = "目标到达双曲超速上限",
                Category = MGAConstraintCategory.PropulsionBudget,
                IsHardConstraint = (hp.MaxArrivalVInfKms > 0),
                IsEnabled = (hp.MaxArrivalVInfKms > 0),
                ThresholdValue = hp.MaxArrivalVInfKms,
                ToleranceValue = 0.05,
                NormalizedScale = Math.Max(1.0, hp.MaxArrivalVInfKms),
                Unit = "km/s",
                Description = "抵达目标引力球时的双曲过剩速度上限"
            });

            // 10. 近日点热控安全下限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MinSolarDistance,
                Name = "航天器最小近日点防热底线",
                Category = MGAConstraintCategory.EnvironmentalCorridor,
                IsHardConstraint = true,
                IsEnabled = (hp.MinSolarDistanceAu > 0),
                ThresholdValue = hp.MinSolarDistanceAu,
                ToleranceValue = 0.005,
                NormalizedScale = 0.1, // 0.1 AU
                Unit = "AU",
                Description = "全轨道近日点距太阳中心距离不得低于热防护罩极限"
            });

            // 11. 远日点能源有效性上限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MaxSolarDistance,
                Name = "航天器最大远日点能源上限",
                Category = MGAConstraintCategory.EnvironmentalCorridor,
                // 【2026-09-14 审计修正】原为 `hp.MaxSolarDistanceAu > 0 && hp.MaxSolarDistanceAu < 50.0`,
                // 即用 **50 AU** 这个绝对值决定该约束是"硬"还是"软"。50 AU 无普适物理意义
                // (约为冥王星轨道外一点), 且造成约束性质在 50 AU 附近突跳。
                // 现改为: 只要用户显式配置了该约束 (阈值 > 0) 即为硬约束 ——
                // 是否"软"应由独立的策略标志表达, 而不是由一个拍定的距离分界决定。
                IsHardConstraint = (hp.MaxSolarDistanceAu > 0),
                IsEnabled = (hp.MaxSolarDistanceAu > 0),
                ThresholdValue = hp.MaxSolarDistanceAu,
                ToleranceValue = 0.1,
                NormalizedScale = 1.0, // 1.0 AU
                Unit = "AU",
                Description = "全轨道远日点不得超出太阳能电池板或深空通讯链路维持极限"
            });

            // 12. 任务总寿命航时上限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MaxMissionDuration,
                Name = "任务全寿命总航时上限",
                Category = MGAConstraintCategory.TemporalLifetime,
                IsHardConstraint = (hp.MaxTotalMissionDurationYears > 0),
                IsEnabled = (hp.MaxTotalMissionDurationYears > 0),
                ThresholdValue = hp.MaxTotalMissionDurationYears * 365.25,
                ToleranceValue = 10.0, // 10 天
                NormalizedScale = Math.Max(100.0, hp.MaxTotalMissionDurationYears * 365.25),
                Unit = "天",
                Description = "自始发离轨至最终抵达目标的总在轨时间上限"
            });

            // 13. 任务总航时下限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MinMissionDuration,
                Name = "任务总航时下限",
                Category = MGAConstraintCategory.TemporalLifetime,
                IsHardConstraint = false,
                IsEnabled = (hp.MinTotalMissionDurationYears > 0),
                ThresholdValue = hp.MinTotalMissionDurationYears * 365.25,
                ToleranceValue = 5.0,
                NormalizedScale = Math.Max(50.0, hp.MinTotalMissionDurationYears * 365.25),
                Unit = "天",
                Description = "保证引力助推有足够几何转移时间展开"
            });

            // 14. 共振等待时间上限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MaxResonanceWait,
                Name = "同星共振段最大等待时间",
                Category = MGAConstraintCategory.TemporalLifetime,
                IsHardConstraint = false,
                IsEnabled = (hp.MaxResonanceWaitYears > 0),
                ThresholdValue = hp.MaxResonanceWaitYears * 365.25,
                ToleranceValue = 10.0,
                NormalizedScale = Math.Max(30.0, hp.MaxResonanceWaitYears * 365.25),
                Unit = "天",
                Description = "限制同星共振等待飞行时间，避免航天器在中间轨道空转耗费任务寿命"
            });

            // 15. 拓扑借力次数上限
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.MaxFlybyCount,
                Name = "全航程中间借力次数上限",
                Category = MGAConstraintCategory.TopologyStrategy,
                IsHardConstraint = true,
                IsEnabled = (hp.MaxIntermediateFlybys > 0),
                ThresholdValue = hp.MaxIntermediateFlybys,
                ToleranceValue = 0.0,
                NormalizedScale = 1.0,
                Unit = "次",
                Description = "避免拓扑搜索盲目组合产生过长且不可工程实现的借力链"
            });

            // 16. 外行星跳板吸附
            profile.SetSpec(new MGAConstraintSpec
            {
                Type = MGAConstraintType.OuterGasGiantAssist,
                Name = "巨行星超级引力跳板吸附",
                Category = MGAConstraintCategory.TopologyStrategy,
                IsHardConstraint = hp.RequireOuterGasGiantAssist,
                IsEnabled = hp.RequireOuterGasGiantAssist,
                ThresholdValue = 1.0,
                ToleranceValue = 0.0,
                NormalizedScale = 1.0,
                Unit = "bool",
                Description = "探测外太阳系深空天体 (>4 AU) 时强制经过木星等巨行星弹弓加速"
            });

            return profile;
        }

        /// <summary>
        /// 将约束配置文件注入自适应求解器配置 MGAAdaptiveSolverConfig
        /// </summary>
        public static void ApplyToSolverConfig(MGAConstraintProfile profile, MGAAdaptiveSolverConfig config)
        {
            if (profile == null || config == null) return;
            config.ConstraintProfile = profile;

            if (profile.IsEnabled(MGAConstraintType.SafePeriapsis))
            {
                config.FlybySafeAtmosphereMarginKm = profile.GetThreshold(MGAConstraintType.SafePeriapsis, 50.0);
                config.EnforceSafeFlybyMargin = true;
            }

            if (profile.IsEnabled(MGAConstraintType.TotalVesselDeltaV))
            {
                double vMax = profile.GetThreshold(MGAConstraintType.TotalVesselDeltaV);
                config.ConstrainByVesselDeltaV = (vMax > 0);
                config.VesselMaxDeltaVMs = vMax;
            }

            if (profile.IsEnabled(MGAConstraintType.MaxDsmDeltaV))
            {
                config.MaxDsmDeltaVMs = profile.GetThreshold(MGAConstraintType.MaxDsmDeltaV);
            }

            if (profile.IsEnabled(MGAConstraintType.LaunchC3Ceiling))
            {
                config.LaunchDeltaVCeilingMs = profile.GetThreshold(MGAConstraintType.LaunchC3Ceiling);
            }

            if (profile.IsEnabled(MGAConstraintType.ArrivalVinfLimit))
            {
                config.VinfFreeKmS = profile.GetThreshold(MGAConstraintType.ArrivalVinfLimit);
            }

            if (profile.IsEnabled(MGAConstraintType.MaxMissionDuration))
            {
                config.TotalToFMaxDays = profile.GetThreshold(MGAConstraintType.MaxMissionDuration);
            }

            if (profile.IsEnabled(MGAConstraintType.MinMissionDuration))
            {
                config.TotalToFMinDays = profile.GetThreshold(MGAConstraintType.MinMissionDuration);
            }

            if (profile.IsEnabled(MGAConstraintType.MaxFlybyCount))
            {
                config.MaxCandidateTopologies = Math.Max(2, (int)profile.GetThreshold(MGAConstraintType.MaxFlybyCount));
            }
        }

        /// <summary>
        /// 将约束配置文件注入底层 MGAConfig
        /// </summary>
        public static void ApplyToMGAConfig(MGAConstraintProfile profile, MGAConfig config)
        {
            if (profile == null || config == null) return;
            config.ConstraintProfile = profile;

            if (profile.IsEnabled(MGAConstraintType.TotalVesselDeltaV))
            {
                double vMax = profile.GetThreshold(MGAConstraintType.TotalVesselDeltaV);
                config.ConstrainByVesselDeltaV = (vMax > 0);
                config.VesselMaxDeltaVMs = vMax;
            }

            if (profile.IsEnabled(MGAConstraintType.MaxDsmDeltaV))
            {
                config.MaxDsmDeltaVMs = profile.GetThreshold(MGAConstraintType.MaxDsmDeltaV);
            }

            if (profile.IsEnabled(MGAConstraintType.LaunchC3Ceiling))
            {
                config.LaunchDeltaVCeilingMs = profile.GetThreshold(MGAConstraintType.LaunchC3Ceiling);
            }
        }

        /// <summary>
        /// 针对单个 MGASolution 开展集中式多维度约束合规审计
        /// </summary>
        public static MGAConstraintAuditReport AuditSolution(MGASolution sol, MGAConstraintProfile profile)
        {
            var report = new MGAConstraintAuditReport();
            if (sol == null)
            {
                report.IsFullyFeasible = false;
                report.SummaryText = "轨迹方案对象为空，无法开展合规审计。";
                return report;
            }

            if (profile == null)
            {
                profile = CreateProfile(new MGAHighValueParameters());
            }

            bool fullyOk = true;
            int totalV = 0;
            int hardV = 0;
            double worstNormV = 0.0;
            MGAConstraintItemAudit worstItem = null;

            // 1. 审计航段开普勒闭环可行性
            if (profile.IsEnabled(MGAConstraintType.LegFeasibility))
            {
                var spec = profile.GetSpec(MGAConstraintType.LegFeasibility);
                bool feasible = sol.AllLegsFeasible;
                double rawV = feasible ? 0.0 : 1.0;
                var severity = feasible ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.CriticalFatal;

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.LegFeasibility,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = severity,
                    ActualValue = feasible ? 1.0 : 0.0,
                    ThresholdValue = 1.0,
                    RawViolation = rawV,
                    NormalizedViolation = rawV,
                    Unit = spec.Unit,
                    Description = feasible ? "全部航段数值几何闭环成立" : "存在 Lambert 无解或数值奇异航段"
                };
                report.Items.Add(item);
                if (!feasible) { fullyOk = false; totalV++; hardV++; }
            }

            // 2. 审计借力天体近心点净空
            if (profile.IsEnabled(MGAConstraintType.SafePeriapsis) && sol.Legs != null)
            {
                var spec = profile.GetSpec(MGAConstraintType.SafePeriapsis);
                double worstMinH = double.MaxValue;
                string worstBody = "";
                double worstHDeficit = 0.0;
                double worstAtmDepth = 0.0;

                for (int i = 0; i < sol.Legs.Count - 1; i++)
                {
                    var leg = sol.Legs[i];
                    var planet = SolarSystemCatalog.GetPlanet(leg.ToBody);
                    double bodyR = planet?.RadiusKm ?? MGADefaults.GetBodyRadiusKm(leg.ToBody);
                    double atmDepth = planet?.AtmosphereDepthKm ?? 0.0;
                    double safeMinAlt = atmDepth + spec.ThresholdValue;
                    double actualH = leg.FlybyPeriapsisAltitudeKm != 0.0 ? leg.FlybyPeriapsisAltitudeKm : (leg.FlybyPeriapsisRadiusKm > 0.0 ? (leg.FlybyPeriapsisRadiusKm - bodyR) : 0.0);

                    if (actualH < worstMinH)
                    {
                        worstMinH = actualH;
                        worstBody = leg.ToBody;
                        worstAtmDepth = atmDepth;
                    }

                    if (actualH < safeMinAlt)
                    {
                        double def = safeMinAlt - actualH;
                        if (def > worstHDeficit) worstHDeficit = def;
                    }
                }

                MGAConstraintSeverity sev = MGAConstraintSeverity.Pass;
                if (worstHDeficit > 0.0)
                {
                    sev = (worstMinH < worstAtmDepth) ? MGAConstraintSeverity.CriticalFatal : MGAConstraintSeverity.Violation;
                    fullyOk = false;
                    totalV++;
                    hardV++;
                }

                double normV = worstHDeficit / Math.Max(10.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.SafePeriapsis,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = worstMinH != double.MaxValue ? worstMinH : 0.0,
                    ThresholdValue = spec.ThresholdValue,
                    RawViolation = worstHDeficit,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"全部借力近掠净空均满足要求 (最低: {worstBody} {worstMinH:F1} km >= 安全基准 {worstAtmDepth + spec.ThresholdValue:F1} km)"
                        : (worstMinH < worstAtmDepth)
                            ? $"【致命】侵入天体大气层/地表 (在 {worstBody} 高度仅 {worstMinH:F1} km < 大气顶层 {worstAtmDepth:F0} km)"
                            : $"借力近心点越界违规 (在 {worstBody} 亏缺 {worstHDeficit:F1} km, 净空仅 {worstMinH:F1} km)"
                };
                report.Items.Add(item);
                if (sev != MGAConstraintSeverity.Pass && worstItem == null) worstItem = item;
            }

            // 3. 审计纯无动力自然飞掠
            if (profile.IsEnabled(MGAConstraintType.StrictUnpoweredFlyby))
            {
                var spec = profile.GetSpec(MGAConstraintType.StrictUnpoweredFlyby);
                double totalFbDvMs = sol.TotalFlybyDeltaVKms * 1000.0;
                double excess = Math.Max(0.0, totalFbDvMs - spec.ToleranceValue);
                MGAConstraintSeverity sev = (excess <= 0.0) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = excess / Math.Max(1.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.StrictUnpoweredFlyby,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = totalFbDvMs,
                    ThresholdValue = spec.ThresholdValue,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"严格保持 0.00 m/s 纯自然无动力飞掠 (实测: {totalFbDvMs:F3} m/s)"
                        : $"检测到非物理有动力飞掠脉冲: {totalFbDvMs:F2} m/s > 0"
                };
                report.Items.Add(item);
                if (sev != MGAConstraintSeverity.Pass && worstItem == null) worstItem = item;
            }

            // 4. 审计总工质机动预算
            if (profile.IsEnabled(MGAConstraintType.TotalVesselDeltaV))
            {
                var spec = profile.GetSpec(MGAConstraintType.TotalVesselDeltaV);
                double totDvMs = sol.TotalDeltaVKms * 1000.0;
                double budgetMs = spec.ThresholdValue;
                double excess = Math.Max(0.0, totDvMs - budgetMs);

                MGAConstraintSeverity sev = MGAConstraintSeverity.Pass;
                if (excess > 0.0)
                {
                    sev = (excess <= spec.ToleranceValue) ? MGAConstraintSeverity.Warning : MGAConstraintSeverity.Violation;
                    if (sev == MGAConstraintSeverity.Violation)
                    {
                        fullyOk = false;
                        totalV++;
                        if (spec.IsHardConstraint) hardV++;
                    }
                }

                double normV = excess / Math.Max(100.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.TotalVesselDeltaV,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = totDvMs,
                    ThresholdValue = budgetMs,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"总机动预算完全达标 ({totDvMs:F1} m/s <= {budgetMs:F0} m/s)"
                        : $"总机动超出预算 {excess:F1} m/s (实际: {totDvMs:F1} m/s, 上限: {budgetMs:F0} m/s)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 5. 审计单脉冲深空机动 (DSM) 上限
            if (profile.IsEnabled(MGAConstraintType.MaxDsmDeltaV) && sol.Legs != null)
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxDsmDeltaV);
                double maxDsmMs = 0.0;
                int worstLegIdx = -1;

                for (int li = 0; li < sol.Legs.Count; li++)
                {
                    var l = sol.Legs[li];
                    double dsmMs = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                    if (dsmMs > maxDsmMs)
                    {
                        maxDsmMs = dsmMs;
                        worstLegIdx = li;
                    }
                }

                double dsmCeilMs = spec.ThresholdValue;
                double excess = Math.Max(0.0, maxDsmMs - dsmCeilMs);
                MGAConstraintSeverity sev = (excess <= 0.0) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = excess / Math.Max(50.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.MaxDsmDeltaV,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = maxDsmMs,
                    ThresholdValue = dsmCeilMs,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"途经 DSM 单脉冲满足限额 (最大在航段 #{worstLegIdx + 1}: {maxDsmMs:F1} m/s <= {dsmCeilMs:F0} m/s)"
                        : $"航段 #{worstLegIdx + 1} DSM 脉冲 ({maxDsmMs:F1} m/s) 超出限额 {dsmCeilMs:F0} m/s"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 6. 审计发射停泊轨出射冲量门禁
            if (profile.IsEnabled(MGAConstraintType.LaunchC3Ceiling))
            {
                var spec = profile.GetSpec(MGAConstraintType.LaunchC3Ceiling);
                double depDvMs = sol.LaunchDeltaVKms * 1000.0;
                double ceilMs = spec.ThresholdValue;
                double excess = Math.Max(0.0, depDvMs - ceilMs);
                MGAConstraintSeverity sev = (excess <= 0.0) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = excess / Math.Max(100.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.LaunchC3Ceiling,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = depDvMs,
                    ThresholdValue = ceilMs,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"发射停泊轨出射冲量达标 ({depDvMs:F1} m/s <= {ceilMs:F0} m/s)"
                        : $"发射出射冲量 ({depDvMs:F1} m/s) 超出运载门禁 ({ceilMs:F0} m/s)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 7. 审计任务总寿命与航时
            if (profile.IsEnabled(MGAConstraintType.MaxMissionDuration))
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxMissionDuration);
                double totDays = sol.TotalFlightDays;
                double maxDays = spec.ThresholdValue;
                double excess = Math.Max(0.0, totDays - maxDays);
                MGAConstraintSeverity sev = (excess <= 0.0) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = excess / Math.Max(10.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.MaxMissionDuration,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = totDays,
                    ThresholdValue = maxDays,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"任务总飞行时间完全达标 ({totDays:F1} 天 <= {maxDays:F0} 天)"
                        : $"任务总航时超期 {excess:F1} 天 (实测: {totDays:F1} 天, 上限: {maxDays:F0} 天)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 8. 审计到达目标天体双曲过剩速度门禁
            if (profile.IsEnabled(MGAConstraintType.ArrivalVinfLimit) && sol.Legs != null && sol.Legs.Count > 0)
            {
                var spec = profile.GetSpec(MGAConstraintType.ArrivalVinfLimit);
                var lastLeg = sol.Legs[sol.Legs.Count - 1];
                double arrVinf = lastLeg.VInfArrivalKms.magnitude;
                double limitVinf = spec.ThresholdValue;
                double excess = Math.Max(0.0, arrVinf - limitVinf);
                MGAConstraintSeverity sev = (excess <= spec.ToleranceValue) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = excess / Math.Max(1.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.ArrivalVinfLimit,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = arrVinf,
                    ThresholdValue = limitVinf,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"到达目标天体双曲过剩速度达标 ({arrVinf:F2} km/s <= {limitVinf:F2} km/s)"
                        : $"到达双曲过剩速度超标 ({arrVinf:F2} km/s > 限额 {limitVinf:F2} km/s)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 9. 审计双曲偏折角物理极限
            if (profile.IsEnabled(MGAConstraintType.FlybyDeflectionLimit) && sol.Legs != null && sol.Legs.Count > 1)
            {
                var spec = profile.GetSpec(MGAConstraintType.FlybyDeflectionLimit);
                double maxExcessTurnDeg = 0.0;
                string worstTurnBody = "";
                double worstActualTurn = 0.0;
                double worstMaxTurn = 0.0;

                for (int k = 0; k < sol.Legs.Count - 1; k++)
                {
                    var lIn = sol.Legs[k];
                    var planet = SolarSystemCatalog.GetPlanet(lIn.ToBody);
                    if (planet != null && planet.Mu > 0)
                    {
                        double vinf = lIn.VInfArrivalKms.magnitude;
                        double maxTurn = MGAFlybyDynamicsHelper.ComputeMaxTurnAngleDeg(planet.Mu, planet.SafeRadiusKm, vinf);
                        double actualTurn = lIn.FlybyTurningAngleDeg;
                        double turnExcess = Math.Max(0.0, actualTurn - maxTurn);
                        if (turnExcess > maxExcessTurnDeg)
                        {
                            maxExcessTurnDeg = turnExcess;
                            worstTurnBody = lIn.ToBody;
                            worstActualTurn = actualTurn;
                            worstMaxTurn = maxTurn;
                        }
                    }
                }

                MGAConstraintSeverity sev = (maxExcessTurnDeg <= 0.05) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;
                double normV = maxExcessTurnDeg / Math.Max(1.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.FlybyDeflectionLimit,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = worstActualTurn,
                    ThresholdValue = worstMaxTurn,
                    RawViolation = maxExcessTurnDeg,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? "所有双曲偏折角均在开普勒引力偏转物理极限内"
                        : $"在天体 {worstTurnBody} 偏折角超限 (实测: {worstActualTurn:F2}°, 最大允许: {worstMaxTurn:F2}°)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 10. 审计任务总航时下限
            if (profile.IsEnabled(MGAConstraintType.MinMissionDuration))
            {
                var spec = profile.GetSpec(MGAConstraintType.MinMissionDuration);
                double totDays = sol.TotalFlightDays;
                double minDays = spec.ThresholdValue;
                double deficit = Math.Max(0.0, minDays - totDays);
                MGAConstraintSeverity sev = (deficit <= spec.ToleranceValue) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Warning;

                double normV = deficit / Math.Max(10.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    totalV++;
                    if (spec.IsHardConstraint) { fullyOk = false; hardV++; }
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.MinMissionDuration,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = totDays,
                    ThresholdValue = minDays,
                    RawViolation = deficit,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"任务总飞行时间满足下限 ({totDays:F1} 天 >= {minDays:F0} 天)"
                        : $"任务总航时低于下限 {deficit:F1} 天 (实测: {totDays:F1} 天, 下限: {minDays:F0} 天)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 11. 审计共面/倾角锁相
            if (profile.IsEnabled(MGAConstraintType.InclinationLock) && sol.Legs != null && sol.Legs.Count > 0)
            {
                var spec = profile.GetSpec(MGAConstraintType.InclinationLock);
                double actualInc = sol.Legs[0].EjectionInclinationDeg;
                double targetInc = spec.ThresholdValue;
                double incError = Math.Abs(actualInc - targetInc);
                MGAConstraintSeverity sev = (incError <= spec.ToleranceValue) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = incError / Math.Max(1.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.InclinationLock,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = actualInc,
                    ThresholdValue = targetInc,
                    RawViolation = incError,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"停泊轨出射倾角与锁相面高度吻合 (偏差 {incError:F3}° <= 容差 {spec.ToleranceValue:F2}°)"
                        : $"出射倾角偏离锁相要求 (实测: {actualInc:F2}°, 目标: {targetInc:F2}°, 偏差: {incError:F2}°)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 12. 审计拓扑跳板深度上限
            if (profile.IsEnabled(MGAConstraintType.MaxFlybyCount) && sol.Legs != null)
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxFlybyCount);
                int fbCount = Math.Max(0, sol.Legs.Count - 1);
                double maxFb = spec.ThresholdValue;
                double excess = Math.Max(0, fbCount - maxFb);
                MGAConstraintSeverity sev = (excess <= 0) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                double normV = excess / Math.Max(1.0, spec.NormalizedScale);
                if (normV > worstNormV) { worstNormV = normV; }

                if (sev != MGAConstraintSeverity.Pass)
                {
                    fullyOk = false;
                    totalV++;
                    if (spec.IsHardConstraint) hardV++;
                }

                var item = new MGAConstraintItemAudit
                {
                    Type = MGAConstraintType.MaxFlybyCount,
                    Name = spec.Name,
                    Category = spec.Category,
                    IsHardConstraint = spec.IsHardConstraint,
                    Severity = sev,
                    ActualValue = fbCount,
                    ThresholdValue = maxFb,
                    RawViolation = excess,
                    NormalizedViolation = normV,
                    Unit = spec.Unit,
                    Description = (sev == MGAConstraintSeverity.Pass)
                        ? $"拓扑借力次数完全受控 ({fbCount} 次 <= 限额 {maxFb:F0} 次)"
                        : $"拓扑借力次数超限 ({fbCount} 次 > 限额 {maxFb:F0} 次)"
                };
                report.Items.Add(item);
                if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
            }

            // 13. 审计热控与光能环境走廊
            if ((profile.IsEnabled(MGAConstraintType.MinSolarDistance) || profile.IsEnabled(MGAConstraintType.MaxSolarDistance)) && sol.Legs != null)
            {
                double KmPerAu = MGADefaults.AU_KM;
                double minRkm = double.MaxValue;
                double maxRkm = 0.0;

                for (int li = 0; li < sol.Legs.Count; li++)
                {
                    var l = sol.Legs[li];
                    double r1 = l.DeparturePosKm.magnitude;
                    double r2 = l.ArrivalPosKm.magnitude;
                    if (r1 > 0 && r1 < minRkm) minRkm = r1;
                    if (r2 > 0 && r2 < minRkm) minRkm = r2;
                    if (r1 > maxRkm) maxRkm = r1;
                    if (r2 > maxRkm) maxRkm = r2;
                }

                if (profile.IsEnabled(MGAConstraintType.MinSolarDistance) && minRkm < double.MaxValue)
                {
                    var spec = profile.GetSpec(MGAConstraintType.MinSolarDistance);
                    double actualMinAu = minRkm / KmPerAu;
                    double limitMinAu = spec.ThresholdValue;
                    double deficit = Math.Max(0.0, limitMinAu - actualMinAu);
                    MGAConstraintSeverity sev = (deficit <= spec.ToleranceValue) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                    double normV = deficit / Math.Max(0.1, spec.NormalizedScale);
                    if (normV > worstNormV) { worstNormV = normV; }

                    if (sev != MGAConstraintSeverity.Pass)
                    {
                        fullyOk = false;
                        totalV++;
                        if (spec.IsHardConstraint) hardV++;
                    }

                    var item = new MGAConstraintItemAudit
                    {
                        Type = MGAConstraintType.MinSolarDistance,
                        Name = spec.Name,
                        Category = spec.Category,
                        IsHardConstraint = spec.IsHardConstraint,
                        Severity = sev,
                        ActualValue = actualMinAu,
                        ThresholdValue = limitMinAu,
                        RawViolation = deficit,
                        NormalizedViolation = normV,
                        Unit = spec.Unit,
                        Description = (sev == MGAConstraintSeverity.Pass)
                            ? $"日心距满足热控下限 (最近: {actualMinAu:F3} AU >= {limitMinAu:F2} AU)"
                            : $"侵入太阳热控危险区 (实测: {actualMinAu:F3} AU < 阈值 {limitMinAu:F2} AU)"
                    };
                    report.Items.Add(item);
                    if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
                }

                if (profile.IsEnabled(MGAConstraintType.MaxSolarDistance) && maxRkm > 0.0)
                {
                    var spec = profile.GetSpec(MGAConstraintType.MaxSolarDistance);
                    double actualMaxAu = maxRkm / KmPerAu;
                    double limitMaxAu = spec.ThresholdValue;
                    double excess = Math.Max(0.0, actualMaxAu - limitMaxAu);
                    MGAConstraintSeverity sev = (excess <= spec.ToleranceValue) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Violation;

                    double normV = excess / Math.Max(1.0, spec.NormalizedScale);
                    if (normV > worstNormV) { worstNormV = normV; }

                    if (sev != MGAConstraintSeverity.Pass)
                    {
                        fullyOk = false;
                        totalV++;
                        if (spec.IsHardConstraint) hardV++;
                    }

                    var item = new MGAConstraintItemAudit
                    {
                        Type = MGAConstraintType.MaxSolarDistance,
                        Name = spec.Name,
                        Category = spec.Category,
                        IsHardConstraint = spec.IsHardConstraint,
                        Severity = sev,
                        ActualValue = actualMaxAu,
                        ThresholdValue = limitMaxAu,
                        RawViolation = excess,
                        NormalizedViolation = normV,
                        Unit = spec.Unit,
                        Description = (sev == MGAConstraintSeverity.Pass)
                            ? $"日心距在能源光照有效范围内 (最远: {actualMaxAu:F2} AU <= {limitMaxAu:F2} AU)"
                            : $"超出太阳能有效工作距离 (实测: {actualMaxAu:F2} AU > 阈值 {limitMaxAu:F2} AU)"
                    };
                    report.Items.Add(item);
                    if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
                }
            }

            // 14. 审计同星共振等待时间上限
            if (profile.IsEnabled(MGAConstraintType.MaxResonanceWait) && sol.Legs != null && sol.Legs.Count > 1)
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxResonanceWait);
                double maxResWaitDays = 0.0;
                string worstResBody = "";

                for (int k = 0; k < sol.Legs.Count; k++)
                {
                    var leg = sol.Legs[k];
                    if (leg.FromBody.Equals(leg.ToBody, StringComparison.OrdinalIgnoreCase))
                    {
                        if (leg.ToFDays > maxResWaitDays)
                        {
                            maxResWaitDays = leg.ToFDays;
                            worstResBody = leg.FromBody;
                        }
                    }
                }

                if (maxResWaitDays > 0.0)
                {
                    double limitResDays = spec.ThresholdValue;
                    double excess = Math.Max(0.0, maxResWaitDays - limitResDays);
                    MGAConstraintSeverity sev = (excess <= spec.ToleranceValue) ? MGAConstraintSeverity.Pass : MGAConstraintSeverity.Warning;

                    double normV = excess / Math.Max(10.0, spec.NormalizedScale);
                    if (normV > worstNormV) { worstNormV = normV; }

                    if (sev != MGAConstraintSeverity.Pass)
                    {
                        totalV++;
                        if (spec.IsHardConstraint) { fullyOk = false; hardV++; }
                    }

                    var item = new MGAConstraintItemAudit
                    {
                        Type = MGAConstraintType.MaxResonanceWait,
                        Name = spec.Name,
                        Category = spec.Category,
                        IsHardConstraint = spec.IsHardConstraint,
                        Severity = sev,
                        ActualValue = maxResWaitDays,
                        ThresholdValue = limitResDays,
                        RawViolation = excess,
                        NormalizedViolation = normV,
                        Unit = spec.Unit,
                        Description = (sev == MGAConstraintSeverity.Pass)
                            ? $"同星共振等待时间合规 (最长在 {worstResBody}: {maxResWaitDays:F1} 天 <= {limitResDays:F0} 天)"
                            : $"同星共振等待时间过长 (在 {worstResBody}: {maxResWaitDays:F1} 天 > 限额 {limitResDays:F0} 天)"
                    };
                    report.Items.Add(item);
                    if (sev == MGAConstraintSeverity.Violation && worstItem == null) worstItem = item;
                }
            }

            report.IsFullyFeasible = fullyOk;
            report.TotalViolationsCount = totalV;
            report.HardViolationsCount = hardV;
            report.MaxNormalizedViolation = worstNormV;
            report.WorstViolationItem = worstItem;

            if (fullyOk)
            {
                report.SummaryText = $"✅ 物理与工程全景约束 100% 满足 (核验项: {report.Items.Count} 项，违约: 0 项)";
            }
            else
            {
                report.SummaryText = $"❌ 存在 {hardV} 项硬约束违背 (总违约: {totalV} 项), 最大归一化违背度: {worstNormV:F2} ({worstItem?.Name ?? "未知项"})";
            }

            report.MarkdownReport = FormatMarkdownReport(report);
            return report;
        }

        /// <summary>
        /// 对多个候选解集合批量执行审计
        /// </summary>
        public static List<MGAConstraintAuditReport> AuditSolutions(
            IEnumerable<MGASolution> solutions,
            MGAConstraintProfile profile)
        {
            var reports = new List<MGAConstraintAuditReport>();
            if (solutions == null) return reports;

            foreach (var s in solutions)
            {
                reports.Add(AuditSolution(s, profile));
            }
            return reports;
        }

        /// <summary>
        /// 格式化导出 Markdown 结构化审计诊断报表
        /// </summary>
        public static string FormatMarkdownReport(MGAConstraintAuditReport report, string missionTitle = "")
        {
            if (report == null) return "";
            var sb = new StringBuilder();

            sb.AppendLine($"# MGA 轨迹动力学与工程约束合规审计报告 {(string.IsNullOrEmpty(missionTitle) ? "" : ("- " + missionTitle))}");
            sb.AppendLine($"*审计结论: {(report.IsFullyFeasible ? "✅ 100% 合规通过 (Feasible)" : "❌ 存在物理/工程约束违背 (Violated)")}*");
            sb.AppendLine();
            sb.AppendLine($"| 统计维度 | 结果值 |");
            sb.AppendLine($"| :--- | :--- |");
            sb.AppendLine($"| 审查约束项总数 | **{report.Items.Count}** 项 |");
            sb.AppendLine($"| 硬物理违约数 (Hard Violations) | **{report.HardViolationsCount}** 项 |");
            sb.AppendLine($"| 总违规项目数 (Total Violations) | **{report.TotalViolationsCount}** 项 |");
            sb.AppendLine($"| 最大归一化违背度 (Worst Normalized) | **{report.MaxNormalizedViolation:F3}** |");
            sb.AppendLine($"| 首要违约致因 | {report.WorstViolationItem?.Name ?? "无"} |");
            sb.AppendLine();

            sb.AppendLine("## 逐项约束核验清单");
            sb.AppendLine("| 约束标识 | 类别 | 硬约束 | 评定状态 | 实际值 | 限额/基准 | 原始违背量 | 归一化违背度 | 诊断说明 |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :--- | :--- |");

            foreach (var it in report.Items)
            {
                string sevIcon = (it.Severity == MGAConstraintSeverity.Pass) ? "✅ PASS"
                    : (it.Severity == MGAConstraintSeverity.Warning) ? "⚠️ WARN"
                    : (it.Severity == MGAConstraintSeverity.Violation) ? "❌ VIOL"
                    : "🛑 FATAL";

                string hardStr = it.IsHardConstraint ? "是" : "否";
                sb.AppendLine($"| {it.Name} | {it.Category} | {hardStr} | {sevIcon} | {it.ActualValue:F2} {it.Unit} | {it.ThresholdValue:F2} {it.Unit} | {it.RawViolation:F2} {it.Unit} | {it.NormalizedViolation:F3} | {it.Description} |");
            }

            return sb.ToString();
        }
    }
}
