// ============================================================================
// MGASelfHealingEngine.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
// 权威自适应 MGA 求解器子模块。底层 100% 官方 ESA kep3 Native C++ 闭环动力学。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 模块 D: 物理基准门禁与智能数值自愈引擎
    /// 理论来源: Dario Izzo et al. (JGCD 2020) "Real-Time Guidance via DNN (G&CNets)"
    /// 机制: 物理健康度门禁 (ΔV ≤ 1.35 × ΔV_Hohmann)，超标时自动触发 Native 局部高精网格打靶与伴随重构
    /// </summary>
    public static class MGASelfHealingEngine
    {
        public static List<MGASolution> AuditAndHeal(
            MGAAdaptiveSolverConfig config,
            List<MGASolution> candidates,
            double nominalHohmannDepKms,
            double nominalHohmannTotKms,
            CancellationToken ct)
        {
            if (candidates == null || candidates.Count == 0) return new List<MGASolution>();

            var healedList = new List<MGASolution>();

            // 物理门禁警戒线
            double depCeilingKms = (nominalHohmannDepKms > 0) 
                ? nominalHohmannDepKms * config.HohmannEnergyCeilingFactor 
                : 6.0;
            double totCeilingKms = (nominalHohmannTotKms > 0) 
                ? nominalHohmannTotKms * config.HohmannEnergyCeilingFactor 
                : 10.0;

            var bestSol = candidates[0];
            bool isHighEnergyTrap = (bestSol.LaunchDeltaVKms > depCeilingKms || bestSol.TotalDeltaVKms > totCeilingKms);
            bool hasLawdenViolation = (bestSol.PrimerVector != null && !bestSol.PrimerVector.IsOptimal && bestSol.PrimerVector.MaxPMag > 1.25);
            
            // 检查 B 平面 3-sigma 安全防撞风险
            bool hasBPlaneSafetyViolation = false;
            if (config.EnableBPlaneCovarianceAnalysis && config.EnforceSafeFlybyMargin && bestSol.Legs != null && bestSol.Legs.Count > 1)
            {
                for (int k = 0; k < bestSol.Legs.Count - 1; k++)
                {
                    var leg = bestSol.Legs[k];
                    var fbP = SolarSystemCatalog.GetPlanet(leg.ToBody);
                    if (fbP != null && fbP.Mu > 0)
                    {
                        double dt = leg.ToFDays * 86400.0;
                        var bpCov = PykepBridge.CalculateBPlaneCovariance(
                            leg.DeparturePosKm, leg.DepartureVelKms, dt, SolarSystemCatalog.SunMu,
                            config.InitialPosSigmaKm, config.InitialVelSigmaMs / 1000.0,
                            leg.ArrivalPosKm, leg.ArrivalVelKms,
                            fbP.Mu, fbP.RadiusKm, config.FlybySafeAtmosphereMarginKm);
                        if (bpCov != null)
                        {
                            bestSol.BPlane = bpCov;
                            if (!bpCov.IsSafeFlyby)
                            {
                                hasBPlaneSafetyViolation = true;
                                break;
                            }
                        }
                    }
                }
            }

            bool needHealing = (isHighEnergyTrap || hasLawdenViolation || hasBPlaneSafetyViolation) && config.EnableSelfHealing;

            // 如果健康度合格且无劳登极值异常或B平面安全告警，直接保留
            if (!needHealing)
            {
                return candidates;
            }

            // 触发数值自愈: 围绕最佳时间节点展开精细网格打靶重构 (0.5天步长)
            if (hasLawdenViolation && config.EngineMode == MGAEngineMode.BallisticImpulsive)
            {
                KSPGMATLog.Info($"[MGASelfHealing] 检测到劳登引向量极大值超标 (|p|={bestSol.PrimerVector.MaxPMag:F2} > 1.0)，自动闭环激活深空机动 (1DSM) 并插入最优脉冲...");
            }
            else if (hasBPlaneSafetyViolation)
            {
                KSPGMATLog.Info($"[MGASelfHealing] 检测到 B 平面 3-sigma 弥散穿透行星安全边界 (裕度 < 0)，自动启动安全规避微调自愈...");
            }
            else
            {
                KSPGMATLog.Info($"[MGASelfHealing] 检测到候选解 ΔV 偏高 ({bestSol.LaunchDeltaVKms:F1} km/s > 门禁 {depCeilingKms:F1} km/s)，自动启动逆向打靶自愈...");
            }

            double healCenterUT = bestSol.DepartureUT;
            double spanSec = config.HealingWindowSpanDays * MGADefaults.SecondsPerDay;
            double healStartUT = Math.Max(config.SearchStartUT, healCenterUT - spanSec);
            double healEndUT = Math.Min(config.SearchEndUT, healCenterUT + spanSec);

            var bestFlybys = (bestSol.Legs != null && bestSol.Legs.Count > 1)
                ? bestSol.Legs.Take(bestSol.Legs.Count - 1).Select(l => l.ToBody).ToList()
                : (config.FlybySequence ?? new List<string>());

            var healBounds = (bestSol.Legs != null && bestSol.Legs.Count > 0)
                ? bestSol.Legs.Select((l, idx) => {
                    bool isRes = !string.IsNullOrEmpty(l.FromBody) && l.FromBody.Equals(l.ToBody, StringComparison.OrdinalIgnoreCase);
                    if (isRes)
                    {
                        var pB = SolarSystemCatalog.GetPlanet(l.FromBody);
                        double smaB = PykepBridge.GetRelativeSMA(pB, config.CentralBody, 0.0);
                        double tOrbD = PykepBridge.CalcOrbitPeriodSec(smaB, SolarSystemCatalog.GetCentralMu(config.CentralBody ?? "Sun")) / 86400.0;
                        if (tOrbD > 0)
                        {
                            return (MinDays: Math.Max(0.5, tOrbD * 0.85), MaxDays: tOrbD * 3.25);
                        }
                    }
                    return (MinDays: Math.Max(0.5, l.ToFDays * 0.70), MaxDays: l.ToFDays * 1.30);
                }).ToList()
                : ((config.LegToFBounds != null && config.LegToFBounds.Count > 0)
                    ? config.LegToFBounds
                    : MGAMCTSTopologySelector.AutoDeriveResonantLegBounds(config.DeparturePlanet, config.ArrivalPlanet, bestFlybys, config.CentralBody));

            var fineCfg = new MGAConfig
            {
                DepartureBody = config.DeparturePlanet,
                DestinationBody = config.ArrivalPlanet,
                FlybySequence = bestFlybys,
                EarliestDepartureUT = healStartUT,
                LatestDepartureUT = healEndUT,
                DeparturePeAKm = config.DepPeKm,
                DepartureApAKm = config.DepApKm,
                DepartureInclinationDeg = -1.0, // 强制纯切向
                DepartureMode = MGADepartureMode.PreLaunchFreeOrbit,
                DestinationPeAKm = config.ArrPeKm,
                DestinationApAKm = config.ArrApKm,
                NoInsertionBurn = config.NoInsertionBurn,
                EngineMode = (hasLawdenViolation && config.EngineMode == MGAEngineMode.BallisticImpulsive)
                    ? MGAEngineMode.DeepSpaceManeuver1DSM
                    : config.EngineMode,
                LegStrategies = config.LegStrategies != null ? new List<MGALegStrategy>(config.LegStrategies) : null,
                FlybyStrategies = config.FlybyStrategies != null ? new List<MGAFlybyStrategy>(config.FlybyStrategies) : null,
                DsmEtas = config.DsmEtas != null ? new List<double>(config.DsmEtas) : null,
                DsmEtaDefault = config.DsmEtaDefault,
                ConstrainByVesselDeltaV = config.ConstrainByVesselDeltaV,
                VesselMaxDeltaVMs = config.VesselMaxDeltaVMs,
                MaxDsmDeltaVMs = config.MaxDsmDeltaVMs,
                LaunchDeltaVCeilingMs = config.LaunchDeltaVCeilingMs,
                PopulationSize = Math.Max(60, config.PopulationSize),
                MaxGenerations = Math.Max(120, config.MaxGenerations),
                MinToFDays = healBounds.Select(b => b.MinDays).ToList(),
                MaxToFDays = healBounds.Select(b => b.MaxDays).ToList()
            };
            if (hasLawdenViolation && bestSol.PrimerVector.MaxPTimeSec > 0 && bestSol.Legs != null && bestSol.Legs.Count > 0)
            {
                double nomEta = Math.Max(0.15, Math.Min(0.85, bestSol.PrimerVector.MaxPTimeSec / Math.Max(1.0, bestSol.Legs[0].ToFDays * 86400.0)));
                fineCfg.DsmEtas = new List<double> { nomEta };
                fineCfg.DsmEtaDefault = nomEta;
            }

            double auditBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
            double auditDsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : auditBudgetMs;

            var healedSols = MGADifferentialEvolution.RunWindow(fineCfg, healStartUT, healEndUT, null, ct);
            if (healedSols != null && healedSols.Count > 0)
            {
                foreach (var hs in healedSols.OrderBy(s => s.TotalDeltaVKms))
                {
                    if (hs.AllLegsFeasible)
                    {
                        bool isStrictlyBetter = (hs.TotalDeltaVKms < bestSol.TotalDeltaVKms);
                        bool withinBudget = (auditBudgetMs >= 1e8 || hs.TotalDeltaVKms * 1000.0 <= auditBudgetMs * 1.05)
                            && (auditDsmBudgetMs >= 1e8 || (hs.TotalDsmDeltaVKms * 1000.0 <= auditDsmBudgetMs * 1.05 && (hs.Legs == null || !hs.Legs.Any(l => l.DSM != null && l.DSM.DeltaVMs > auditDsmBudgetMs * 1.05))));
                        if (withinBudget || isStrictlyBetter)
                        {
                            healedList.Add(hs);
                        }
                    }
                }
            }

            // 若自愈成功找到更优解，置于最前
            if (healedList.Count > 0 && healedList[0].TotalDeltaVKms < bestSol.TotalDeltaVKms)
            {
                KSPGMATLog.Info($"[MGASelfHealing] ✅ 自愈成功！总机动从 {bestSol.TotalDeltaVKms*1000:F0} m/s 降至 {healedList[0].TotalDeltaVKms*1000:F0} m/s");
                foreach (var old in candidates)
                {
                    if (!healedList.Any(h => Math.Abs(h.DepartureUT - old.DepartureUT) < 86400.0))
                    {
                        healedList.Add(old);
                    }
                }
                return healedList;
            }

            return candidates;
        }
    }
}
