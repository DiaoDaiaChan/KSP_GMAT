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

        public void AddSpec(MGAConstraintType type, string name, MGAConstraintCategory cat, bool hard, bool enabled, double thresh, double tol, double scale, string unit, string desc)
        {
            SetSpec(new MGAConstraintSpec
            {
                Type = type, Name = name, Category = cat, IsHardConstraint = hard, IsEnabled = enabled,
                ThresholdValue = thresh, ToleranceValue = tol, NormalizedScale = scale, Unit = unit, Description = desc
            });
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
            profile.AddSpec(MGAConstraintType.SafePeriapsis, "借力天体近心点净空高度", MGAConstraintCategory.HardPhysics, true, true, hp.FlybySafeAltitudeMarginKm, 0.0, Math.Max(10.0, hp.FlybySafeAltitudeMarginKm), "km", "借力近拱点半径必须严格大于天体名义半径与大气/地形安全裕度之和");

            // 2. 严格无动力双曲弹弓约束
            profile.AddSpec(MGAConstraintType.StrictUnpoweredFlyby, "纯自然无动力飞掠脉冲", MGAConstraintCategory.HardPhysics, true, hp.StrictUnpoweredFlyby, 0.0, 1e-4, 10.0, "m/s", "自然双曲引力弹弓中借力脉冲 DeltaV_fb 恒为 0.00 m/s，严禁有动力伪借力");

            // 3. 双曲转弯角极限约束
            profile.AddSpec(MGAConstraintType.FlybyDeflectionLimit, "双曲偏折角几何极限", MGAConstraintCategory.HardPhysics, true, true, 0.0, 1e-4, 0.1, "rad", "实际所需转角不得超出借力天体引力势在安全近掠半径下的最大解析双曲偏折角");

            // 4. 发射倾角与升交点赤经锁定约束
            profile.AddSpec(MGAConstraintType.InclinationLock, "始发停泊轨道共面锁定", MGAConstraintCategory.HardPhysics, true, true, 0.0, 1.0, 1.0, "deg", "模式 2 在轨锁定状态下，实际出射点火平面必须与停泊轨道严格对齐");

            // 5. 航段数值开普勒闭环可行性
            profile.AddSpec(MGAConstraintType.LegFeasibility, "航段开普勒闭环可行性", MGAConstraintCategory.HardPhysics, true, true, 1.0, 0.0, 1.0, "bool", "开普勒 Lambert 双两点边值问题求解必须有解且避免数值发散");

            // 6. 飞船总工质机动预算
            double vRef = (hp.MaxVesselDeltaVMs > 0) ? hp.MaxVesselDeltaVMs : 4000.0;
            profile.AddSpec(MGAConstraintType.TotalVesselDeltaV, "飞船总工质机动预算", MGAConstraintCategory.PropulsionBudget, (hp.MaxVesselDeltaVMs > 0), (hp.MaxVesselDeltaVMs > 0), hp.MaxVesselDeltaVMs, 10.0, vRef, "m/s", "全航程总机动 Delta-V (发射额外出射 + 途经 DSM + 终点捕获) 不得超出飞船推进上限");

            // 7. 单脉冲途经深空机动 (DSM) 预算
            profile.AddSpec(MGAConstraintType.MaxDsmDeltaV, "单次深空机动预算", MGAConstraintCategory.PropulsionBudget, (hp.MaxDsmDeltaVMs > 0), (hp.MaxDsmDeltaVMs > 0), hp.MaxDsmDeltaVMs, 5.0, Math.Max(100.0, hp.MaxDsmDeltaVMs), "m/s", "中途任一单次深空机动脉冲不得超出推力器单次点火上限");

            // 8. 始发天体停泊轨出射 C3 / 注入限额
            profile.AddSpec(MGAConstraintType.LaunchC3Ceiling, "始发停泊轨出射 C3 限额", MGAConstraintCategory.PropulsionBudget, (hp.LaunchDeltaVCeilingMs > 0), (hp.LaunchDeltaVCeilingMs > 0), hp.LaunchDeltaVCeilingMs, 10.0, Math.Max(1000.0, hp.LaunchDeltaVCeilingMs), "m/s", "运载火箭注入级提供的最大出射速度增量门禁");

            // 9. 到达双曲过剩速度上限
            profile.AddSpec(MGAConstraintType.ArrivalVinfLimit, "目标到达双曲超速上限", MGAConstraintCategory.PropulsionBudget, (hp.MaxArrivalVInfKms > 0), (hp.MaxArrivalVInfKms > 0), hp.MaxArrivalVInfKms, 0.05, Math.Max(1.0, hp.MaxArrivalVInfKms), "km/s", "抵达目标引力球时的双曲过剩速度上限");

            // 10. 近日点热控安全下限
            profile.AddSpec(MGAConstraintType.MinSolarDistance, "航天器最小近日点防热底线", MGAConstraintCategory.EnvironmentalCorridor, true, (hp.MinSolarDistanceAu > 0), hp.MinSolarDistanceAu, 0.005, 0.1, "AU", "全轨道近日点距太阳中心距离不得低于热防护罩极限");

            // 11. 远日点能源有效性上限
            profile.AddSpec(MGAConstraintType.MaxSolarDistance, "航天器最大远日点能源上限", MGAConstraintCategory.EnvironmentalCorridor, (hp.MaxSolarDistanceAu > 0), (hp.MaxSolarDistanceAu > 0), hp.MaxSolarDistanceAu, 0.1, 1.0, "AU", "全轨道远日点不得超出太阳能电池板或深空通讯链路维持极限");

            // 12. 任务总寿命航时上限
            profile.AddSpec(MGAConstraintType.MaxMissionDuration, "任务全寿命总航时上限", MGAConstraintCategory.TemporalLifetime, (hp.MaxTotalMissionDurationYears > 0), (hp.MaxTotalMissionDurationYears > 0), hp.MaxTotalMissionDurationYears * 365.25, 10.0, Math.Max(100.0, hp.MaxTotalMissionDurationYears * 365.25), "天", "自始发离轨至最终抵达目标的总在轨时间上限");

            // 13. 任务总航时下限
            profile.AddSpec(MGAConstraintType.MinMissionDuration, "任务总航时下限", MGAConstraintCategory.TemporalLifetime, false, (hp.MinTotalMissionDurationYears > 0), hp.MinTotalMissionDurationYears * 365.25, 5.0, Math.Max(50.0, hp.MinTotalMissionDurationYears * 365.25), "天", "保证引力助推有足够几何转移时间展开");

            // 14. 共振等待时间上限
            profile.AddSpec(MGAConstraintType.MaxResonanceWait, "同星共振段最大等待时间", MGAConstraintCategory.TemporalLifetime, false, (hp.MaxResonanceWaitYears > 0), hp.MaxResonanceWaitYears * 365.25, 10.0, Math.Max(30.0, hp.MaxResonanceWaitYears * 365.25), "天", "限制同星共振等待飞行时间，避免航天器在中间轨道空转耗费任务寿命");

            // 15. 拓扑借力次数上限
            profile.AddSpec(MGAConstraintType.MaxFlybyCount, "全航程中间借力次数上限", MGAConstraintCategory.TopologyStrategy, true, (hp.MaxIntermediateFlybys > 0), hp.MaxIntermediateFlybys, 0.0, 1.0, "次", "避免拓扑搜索盲目组合产生过长且不可工程实现的借力链");

            // 16. 外行星跳板吸附
            profile.AddSpec(MGAConstraintType.OuterGasGiantAssist, "巨行星超级引力跳板吸附", MGAConstraintCategory.TopologyStrategy, hp.RequireOuterGasGiantAssist, hp.RequireOuterGasGiantAssist, 1.0, 0.0, 1.0, "bool", "探测外太阳系深空天体 (>4 AU) 时强制经过木星等巨行星弹弓加速");

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
        /// <summary>
        /// 评估并登记单个约束项的合规审计记录 (统一 DRY 模式)
        /// </summary>
        private static void EvaluateAndRecordConstraint(
            MGAConstraintAuditReport report,
            MGAConstraintProfile profile,
            MGAConstraintType type,
            double actualValue,
            double thresholdValue,
            double rawViolation,
            bool isViolated,
            string passDescription,
            string failDescription,
            ref bool fullyOk,
            ref int totalV,
            ref int hardV,
            ref double worstNormV,
            ref MGAConstraintItemAudit worstItem,
            bool isFatal = false)
        {
            var spec = profile.GetSpec(type);
            if (spec == null) return;

            MGAConstraintSeverity severity;
            if (!isViolated)
            {
                severity = MGAConstraintSeverity.Pass;
            }
            else if (isFatal)
            {
                severity = MGAConstraintSeverity.CriticalFatal;
            }
            else
            {
                severity = spec.IsHardConstraint ? MGAConstraintSeverity.Violation : MGAConstraintSeverity.Warning;
            }

            double normV = isViolated ? Math.Min(100.0, rawViolation / Math.Max(1e-6, spec.NormalizedScale)) : 0.0;
            var item = new MGAConstraintItemAudit
            {
                Type = type,
                Name = spec.Name,
                Category = spec.Category,
                IsHardConstraint = spec.IsHardConstraint,
                Severity = severity,
                ActualValue = actualValue,
                ThresholdValue = thresholdValue,
                RawViolation = Math.Max(0.0, rawViolation),
                NormalizedViolation = normV,
                Unit = spec.Unit,
                Description = isViolated ? failDescription : passDescription
            };

            report.Items.Add(item);
            if (isViolated)
            {
                fullyOk = false;
                totalV++;
                if (spec.IsHardConstraint) hardV++;
                if (normV > worstNormV)
                {
                    worstNormV = normV;
                    worstItem = item;
                }
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

            if (profile == null) profile = CreateProfile(new MGAHighValueParameters());

            bool fullyOk = true;
            int totalV = 0;
            int hardV = 0;
            double worstNormV = 0.0;
            MGAConstraintItemAudit worstItem = null;

            // 1. 航段数值开普勒闭环可行性
            if (profile.IsEnabled(MGAConstraintType.LegFeasibility))
            {
                bool feasible = sol.AllLegsFeasible;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.LegFeasibility,
                    actualValue: feasible ? 1.0 : 0.0,
                    thresholdValue: 1.0,
                    rawViolation: feasible ? 0.0 : 1.0,
                    isViolated: !feasible,
                    passDescription: "全部航段数值几何闭环成立",
                    failDescription: "存在 Lambert 无解或数值奇异航段",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem,
                    isFatal: true);
            }

            // 2. 借力天体近心点安全净空
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

                bool hViolated = worstHDeficit > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.SafePeriapsis,
                    actualValue: (worstMinH < double.MaxValue) ? worstMinH : spec.ThresholdValue,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: worstHDeficit,
                    isViolated: hViolated,
                    passDescription: "全部中间天体借力近拱点均在安全净空走廊之上",
                    failDescription: $"天体 {worstBody} 近拱点净空不足: 实际 {worstMinH:F1} km < 阈值 {worstAtmDepth + spec.ThresholdValue:F1} km",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem,
                    isFatal: (worstMinH < worstAtmDepth));
            }

            // 3. 严格无动力双曲弹弓约束
            if (profile.IsEnabled(MGAConstraintType.StrictUnpoweredFlyby))
            {
                var spec = profile.GetSpec(MGAConstraintType.StrictUnpoweredFlyby);
                double totalFbDvMs = sol.TotalFlybyDeltaVKms * 1000.0;
                double excess = Math.Max(0.0, totalFbDvMs - spec.ToleranceValue);
                bool fbViolated = excess > 0.0;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.StrictUnpoweredFlyby,
                    actualValue: totalFbDvMs,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess,
                    isViolated: fbViolated,
                    passDescription: "严格保持 0.00 m/s 自然无动力飞掠",
                    failDescription: $"检测到有动力飞掠机动: {totalFbDvMs:F2} m/s > 0",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 4. 飞船总工质机动预算
            if (profile.IsEnabled(MGAConstraintType.TotalVesselDeltaV))
            {
                var spec = profile.GetSpec(MGAConstraintType.TotalVesselDeltaV);
                double totDvMs = sol.TotalDeltaVKms * 1000.0;
                double budgetMs = spec.ThresholdValue;
                double excess = Math.Max(0.0, totDvMs - budgetMs);
                bool dvViolated = excess > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.TotalVesselDeltaV,
                    actualValue: totDvMs,
                    thresholdValue: budgetMs,
                    rawViolation: excess,
                    isViolated: dvViolated,
                    passDescription: $"总机动预算安全 ({totDvMs:F1} m/s <= {budgetMs:F0} m/s)",
                    failDescription: $"总机动预算超支 {excess:F1} m/s (实际: {totDvMs:F1} m/s, 预算: {budgetMs:F0} m/s)",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 5. 最大中途深空机动 (DSM) 预算
            if (profile.IsEnabled(MGAConstraintType.MaxDsmDeltaV) && sol.Legs != null)
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxDsmDeltaV);
                double maxDsmMs = 0.0;
                foreach (var l in sol.Legs)
                {
                    double dsmMs = (l.DSM != null) ? l.DSM.DeltaVMs : 0.0;
                    if (dsmMs > maxDsmMs) maxDsmMs = dsmMs;
                }
                double excess = Math.Max(0.0, maxDsmMs - spec.ThresholdValue);
                bool dsmViolated = excess > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.MaxDsmDeltaV,
                    actualValue: maxDsmMs,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess,
                    isViolated: dsmViolated,
                    passDescription: $"单次 DSM 冲量 ({maxDsmMs:F1} m/s) 满足限额 ({spec.ThresholdValue:F0} m/s)",
                    failDescription: $"单次 DSM 脉冲超额: 最大 {maxDsmMs:F1} m/s > 限额 {spec.ThresholdValue:F0} m/s",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 6. 始发停泊轨出射 C3 / 注入冲量限额
            if (profile.IsEnabled(MGAConstraintType.LaunchC3Ceiling))
            {
                var spec = profile.GetSpec(MGAConstraintType.LaunchC3Ceiling);
                double launchDv = sol.LaunchDeltaVKms * 1000.0;
                double excess = Math.Max(0.0, launchDv - spec.ThresholdValue);
                bool launchViolated = excess > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.LaunchC3Ceiling,
                    actualValue: launchDv,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess,
                    isViolated: launchViolated,
                    passDescription: $"始发出射脉冲合规 ({launchDv:F1} m/s <= {spec.ThresholdValue:F0} m/s)",
                    failDescription: $"始发出射脉冲超支 {excess:F1} m/s (实际: {launchDv:F1} m/s, 限额: {spec.ThresholdValue:F0} m/s)",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 7. 全任务寿命总航时上限
            if (profile.IsEnabled(MGAConstraintType.MaxMissionDuration))
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxMissionDuration);
                double totDays = sol.TotalFlightDays;
                double maxDays = spec.ThresholdValue * 365.25;
                double excess = Math.Max(0.0, totDays - maxDays);
                bool tofViolated = excess > spec.ToleranceValue * 365.25;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.MaxMissionDuration,
                    actualValue: totDays / 365.25,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess / 365.25,
                    isViolated: tofViolated,
                    passDescription: $"总航时满足任务寿命要求 ({totDays / 365.25:F2} 年 <= {spec.ThresholdValue:F1} 年)",
                    failDescription: $"总航时超出最大寿命走廊 (实际: {totDays / 365.25:F2} 年 > 限额: {spec.ThresholdValue:F1} 年)",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 8. 目标天体到达双曲过剩速度上限
            if (profile.IsEnabled(MGAConstraintType.ArrivalVinfLimit) && sol.Legs != null && sol.Legs.Count > 0)
            {
                var spec = profile.GetSpec(MGAConstraintType.ArrivalVinfLimit);
                double arrVinf = sol.Legs.Last().VInfArrivalKms.magnitude;
                double excess = (spec.ThresholdValue > 0) ? Math.Max(0.0, arrVinf - spec.ThresholdValue) : 0.0;
                bool arrViolated = spec.ThresholdValue > 0 && excess > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.ArrivalVinfLimit,
                    actualValue: arrVinf,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess,
                    isViolated: arrViolated,
                    passDescription: $"终点到达超速正常 ({arrVinf:F2} km/s <= {spec.ThresholdValue:F1} km/s)",
                    failDescription: $"终点到达双曲速度过大: {arrVinf:F2} km/s > 捕获上限 {spec.ThresholdValue:F1} km/s",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 9. 双曲偏折角几何极限
            if (profile.IsEnabled(MGAConstraintType.FlybyDeflectionLimit) && sol.Legs != null && sol.Legs.Count > 1)
            {
                var spec = profile.GetSpec(MGAConstraintType.FlybyDeflectionLimit);
                double maxExcessDeg = 0.0;
                string worstBody = "";
                for (int k = 0; k < sol.Legs.Count - 1; k++)
                {
                    var lIn = sol.Legs[k];
                    var planet = SolarSystemCatalog.GetPlanet(lIn.ToBody);
                    if (planet != null && planet.Mu > 0)
                    {
                        double vinf = lIn.VInfArrivalKms.magnitude;
                        double bodyR = planet.RadiusKm > 0 ? planet.RadiusKm : MGADefaults.GetBodyRadiusKm(lIn.ToBody);
                        double safeR = planet.SafeRadiusKm > 0 ? planet.SafeRadiusKm : (bodyR + 50.0);
                        double maxTurnRad = MGAPhysicalScales.MaxTurnAngleRad(planet, vinf);
                        double maxTurnDeg = maxTurnRad * (180.0 / Math.PI);
                        double actualTurnDeg = lIn.FlybyTurningAngleDeg;
                        if (actualTurnDeg > maxTurnDeg)
                        {
                            double def = actualTurnDeg - maxTurnDeg;
                            if (def > maxExcessDeg) { maxExcessDeg = def; worstBody = lIn.ToBody; }
                        }
                    }
                }
                bool defViolated = maxExcessDeg > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.FlybyDeflectionLimit,
                    actualValue: maxExcessDeg,
                    thresholdValue: 0.0,
                    rawViolation: maxExcessDeg,
                    isViolated: defViolated,
                    passDescription: "全部飞掠转向角均在天体引力极限以内",
                    failDescription: $"天体 {worstBody} 偏折角超限 {maxExcessDeg:F2}° (超出引力物理极限)",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 10. 任务允许最短飞行时间
            if (profile.IsEnabled(MGAConstraintType.MinMissionDuration))
            {
                var spec = profile.GetSpec(MGAConstraintType.MinMissionDuration);
                double totDays = sol.TotalFlightDays;
                double minDays = spec.ThresholdValue * 365.25;
                double deficit = Math.Max(0.0, minDays - totDays);
                bool minViolated = deficit > spec.ToleranceValue * 365.25;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.MinMissionDuration,
                    actualValue: totDays / 365.25,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: deficit / 365.25,
                    isViolated: minViolated,
                    passDescription: $"总航时满足最短历时门槛 ({totDays / 365.25:F2} 年 >= {spec.ThresholdValue:F2} 年)",
                    failDescription: $"总航时过短: {totDays / 365.25:F2} 年 < 最短要求 {spec.ThresholdValue:F2} 年",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 11. 始发停泊轨道共面锁定
            if (profile.IsEnabled(MGAConstraintType.InclinationLock) && sol.Legs != null && sol.Legs.Count > 0)
            {
                var spec = profile.GetSpec(MGAConstraintType.InclinationLock);
                double actualInc = sol.Legs[0].EjectionInclinationDeg;
                double targetInc = spec.ThresholdValue;
                double incError = Math.Abs(actualInc - targetInc);
                bool incViolated = incError > spec.ToleranceValue;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.InclinationLock,
                    actualValue: actualInc,
                    thresholdValue: targetInc,
                    rawViolation: incError,
                    isViolated: incViolated,
                    passDescription: $"停泊轨道共面锁相对齐正常 (误差 {incError:F2}° <= {spec.ToleranceValue:F1}°)",
                    failDescription: $"出射平面倾角偏差 {incError:F2}° > 容差 {spec.ToleranceValue:F1}°",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 12. 全航程允许的最大中间借力次数
            if (profile.IsEnabled(MGAConstraintType.MaxFlybyCount) && sol.Legs != null)
            {
                var spec = profile.GetSpec(MGAConstraintType.MaxFlybyCount);
                int fbCount = Math.Max(0, sol.Legs.Count - 1);
                double excess = Math.Max(0.0, fbCount - spec.ThresholdValue);
                bool fbViolated = excess > 0;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.MaxFlybyCount,
                    actualValue: fbCount,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess,
                    isViolated: fbViolated,
                    passDescription: $"借力总次数符合任务构型上限 ({fbCount} 次 <= {spec.ThresholdValue:F0} 次)",
                    failDescription: $"借力次数过多: 实际 {fbCount} 次 > 上限 {spec.ThresholdValue:F0} 次",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
            }

            // 13. 最小与最大近日/远日点安全距离
            if ((profile.IsEnabled(MGAConstraintType.MinSolarDistance) || profile.IsEnabled(MGAConstraintType.MaxSolarDistance)) && sol.Legs != null)
            {
                double KmPerAu = MGADefaults.AU_KM;
                double minRkm = double.MaxValue;
                double maxRkm = 0.0;
                foreach (var l in sol.Legs)
                {
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
                    double deficit = Math.Max(0.0, spec.ThresholdValue - actualMinAu);
                    bool minViolated = deficit > spec.ToleranceValue;
                    EvaluateAndRecordConstraint(
                        report, profile, MGAConstraintType.MinSolarDistance,
                        actualValue: actualMinAu,
                        thresholdValue: spec.ThresholdValue,
                        rawViolation: deficit,
                        isViolated: minViolated,
                        passDescription: $"航程最小近日距正常 ({actualMinAu:F3} AU >= {spec.ThresholdValue:F2} AU)",
                        failDescription: $"航程过度贴近恒星热区: 最小近日距 {actualMinAu:F3} AU < 安全线 {spec.ThresholdValue:F2} AU",
                        ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
                }

                if (profile.IsEnabled(MGAConstraintType.MaxSolarDistance) && maxRkm > 0.0)
                {
                    var spec = profile.GetSpec(MGAConstraintType.MaxSolarDistance);
                    double actualMaxAu = maxRkm / KmPerAu;
                    double excess = Math.Max(0.0, actualMaxAu - spec.ThresholdValue);
                    bool maxViolated = excess > spec.ToleranceValue;
                    EvaluateAndRecordConstraint(
                        report, profile, MGAConstraintType.MaxSolarDistance,
                        actualValue: actualMaxAu,
                        thresholdValue: spec.ThresholdValue,
                        rawViolation: excess,
                        isViolated: maxViolated,
                        passDescription: $"航程最大远日距正常 ({actualMaxAu:F2} AU <= {spec.ThresholdValue:F1} AU)",
                        failDescription: $"航程超出太阳能覆盖范围: 最大远日距 {actualMaxAu:F2} AU > 上限 {spec.ThresholdValue:F1} AU",
                        ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
                }
            }

            // 14. 单个同星共振段最大等待时间
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
                double excess = (maxResWaitDays > 0) ? Math.Max(0.0, maxResWaitDays - spec.ThresholdValue * 365.25) : 0.0;
                bool resViolated = maxResWaitDays > 0 && excess > spec.ToleranceValue * 365.25;
                EvaluateAndRecordConstraint(
                    report, profile, MGAConstraintType.MaxResonanceWait,
                    actualValue: maxResWaitDays / 365.25,
                    thresholdValue: spec.ThresholdValue,
                    rawViolation: excess / 365.25,
                    isViolated: resViolated,
                    passDescription: (maxResWaitDays > 0) ? $"共振等待耗时正常 ({maxResWaitDays / 365.25:F2} 年 <= {spec.ThresholdValue:F1} 年)" : "无同星共振腿",
                    failDescription: $"天体 {worstResBody} 共振等待时间过长: 实际 {maxResWaitDays / 365.25:F2} 年 > 上限 {spec.ThresholdValue:F1} 年",
                    ref fullyOk, ref totalV, ref hardV, ref worstNormV, ref worstItem);
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
