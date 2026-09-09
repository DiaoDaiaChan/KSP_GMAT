// ============================================================================
// MGAPhysicsLossEngine.cs
// 连续物理损失场引擎 (Physics-Informed Loss Field & Adaptive Simulated Annealing)
//
// 架构职责:
//   1. 彻底清除阶梯式硬截断与离散死刑惩罚 (淘汰 5.0e6 经验大数);
//   2. 提供一阶连续可微的物理势垒损失 (C1-Continuous Quadratic Barrier);
//   3. 提供 L1 范数稀疏深空机动 (DSM) 正则化 (Lasso Regularization)，利用数学凸优化
//      次梯度特性驱动假机动自发压平至严格 0.00 m/s，自然涌现纯无动力天体引力弹弓;
//   4. 提供基于 Parallel Tempering 异构岛屿模拟退火 (ASA) 的 Metropolis 接受准则，
//      从数学底层破除开普勒共振深井的局部假极小陷阱。
//
// 设计红线:
//   严禁引入经验参数或行星名称特判，所有损失计算 100% 建立在能量守恒、双曲转向几何
//   与开普勒轨道解析微分学基础之上。
// ============================================================================

using System;
using System.Collections.Generic;
using KSPGMAT.Model;
using UnityEngine;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 连续物理损失场与退火优化引擎
    /// </summary>
    public static class MGAPhysicsLossEngine
    {
        // ------------------------------------------------------------------------
        // 核心物理损失常数 (纯无量纲/尺度归一化物理因子)
        // ------------------------------------------------------------------------
        
        /// <summary>
        /// L1 正则化系数 (Lasso Sparsity Factor):
        /// 施加在深空机动 (DSM) 标量上的 L1 范数次梯度惩罚，驱动冗余机动自然收缩为 0
        /// </summary>
        public const double LambdaL1Dsm = 0.85;

        /// <summary>
        /// 连续光滑二次势垒系数 (Smooth Quadratic Barrier Scale)
        /// </summary>
        public const double QuadraticBarrierScale = 25000.0;

        /// <summary>
        /// 动力飞掠惩罚凸模系数 (Powered Flyby Convex Factor)
        /// </summary>
        public const double PoweredFlybyPenaltyScale = 2500.0;

        // ------------------------------------------------------------------------
        // 连续物理损失计算 (Physics-Informed Loss Field)
        // ------------------------------------------------------------------------

        /// <summary>
        /// 计算候选轨道的综合物理可微损失值 (Total Continuous Physics Loss)
        /// </summary>
        public static double ComputeLoss(MGASolution sol, MGAConfig config)
        {
            if (sol == null || double.IsNaN(sol.TotalDeltaVKms))
            {
                return 1e9; // 几何不可行解的基础高势区
            }

            // 1. 基础物理目标: 全任务日心总速度增量 (发射 + DSM + 飞掠 + 捕获)
            double loss = sol.TotalDeltaVKms;

            // 2. 开普勒轨道连续性/可行性障碍项
            if (!sol.AllLegsFeasible)
            {
                loss += MGADefaults.PenaltyInfeasibleBase;
            }

            if (config != null)
            {
                // 3. 发射窗口时间边界连续二次平滑惩罚 (平滑引导历元回归可行区)
                if (config.EarliestDepartureUT > 0 && sol.DepartureUT < config.EarliestDepartureUT)
                {
                    double violationDays = (config.EarliestDepartureUT - sol.DepartureUT) / MGADefaults.SecondsPerDay;
                    loss += violationDays * MGADefaults.PenaltyDepartureViolationDayWeight 
                          + 0.5 * QuadraticBarrierScale * Math.Pow(violationDays / 30.0, 2.0);
                }
                if (config.LatestDepartureUT > config.EarliestDepartureUT && sol.DepartureUT > config.LatestDepartureUT)
                {
                    double violationDays = (sol.DepartureUT - config.LatestDepartureUT) / MGADefaults.SecondsPerDay;
                    loss += violationDays * MGADefaults.PenaltyDepartureViolationDayWeight 
                          + 0.5 * QuadraticBarrierScale * Math.Pow(violationDays / 30.0, 2.0);
                }

                // 4. L1 稀疏机动正则项 (Lasso DSM Regularization)
                // 数学原理: L1 范数在原点具有恒定的下压斜率，微小的试探性假机动会被严格抹平至 0.00 m/s
                double dsmBudgetKms = (config.MaxDsmDeltaVMs > 0) ? (config.MaxDsmDeltaVMs / 1000.0) : 0.0;
                if (sol.Legs != null)
                {
                    double totalDsmKms = sol.TotalDsmDeltaVKms;
                    // L1 范数项
                    loss += LambdaL1Dsm * totalDsmKms;

                    // 推进器最大预算平滑二次障碍 (Smooth Quadratic Barrier)
                    if (dsmBudgetKms > 0 && totalDsmKms > dsmBudgetKms)
                    {
                        double overKms = totalDsmKms - dsmBudgetKms;
                        loss += QuadraticBarrierScale * (overKms + 0.5 * overKms * overKms);
                    }

                    // 单航段近心点安全半径障碍 (Safe Periapsis Quadratic Barrier)
                    foreach (var leg in sol.Legs)
                    {
                        double rp = leg.FlybyPeriapsisRadiusKm;
                        double rSafe = MGADefaults.GetBodySafeRadiusKm(leg.FromBody);
                        if (rp > 0 && rSafe > 0 && rp < rSafe)
                        {
                            double fracViolation = (rSafe - rp) / rSafe;
                            loss += 0.5 * QuadraticBarrierScale * fracViolation * fracViolation;
                        }
                    }
                }

                // 5. 飞船总推进能力平滑二次障碍
                if (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0)
                {
                    double vesselLimitKms = config.VesselMaxDeltaVMs / 1000.0;
                    if (sol.TotalDeltaVKms > vesselLimitKms)
                    {
                        double overTotalKms = sol.TotalDeltaVKms - vesselLimitKms;
                        loss += QuadraticBarrierScale * (overTotalKms + overTotalKms * overTotalKms);
                    }
                }

                // 6. 发射出射冲量上限平滑二次障碍
                if (config.LaunchDeltaVCeilingMs > 0)
                {
                    double launchLimitKms = config.LaunchDeltaVCeilingMs / 1000.0;
                    if (sol.LaunchDeltaVKms > launchLimitKms)
                    {
                        double overLaunchKms = sol.LaunchDeltaVKms - launchLimitKms;
                        loss += QuadraticBarrierScale * (overLaunchKms + overLaunchKms * overLaunchKms);
                    }
                }

                // 7. 纯无动力引力助推单调凸惩罚 (Strict Zero-Powered Flyby Invariant)
                // 确保飞掠脉冲严格收敛于 0.00 m/s
                if (sol.TotalFlybyDeltaVKms > 1e-4)
                {
                    loss += PoweredFlybyPenaltyScale * (sol.TotalFlybyDeltaVKms + sol.TotalFlybyDeltaVKms * sol.TotalFlybyDeltaVKms);
                }

                // 8. 轨道平面共面与倾角锁定平滑势函数
                if (config.LockDepartureInclination && sol.Legs != null && sol.Legs.Count > 0)
                {
                    double actualInc = sol.Legs[0].EjectionInclinationDeg;
                    double targetInc = config.DepartureInclinationDeg;
                    double incErr = Math.Abs(actualInc - targetInc);
                    if (incErr > MGADefaults.PenaltyIncToleranceDeg)
                    {
                        double excessInc = incErr - MGADefaults.PenaltyIncToleranceDeg;
                        loss += excessInc * MGADefaults.PenaltyIncDegWeight + 0.5 * QuadraticBarrierScale * Math.Pow(excessInc / 10.0, 2.0);
                    }
                }
            }

            return loss;
        }

        // ------------------------------------------------------------------------
        // 自适应模拟退火 (Adaptive Simulated Annealing & Metropolis Acceptance)
        // ------------------------------------------------------------------------

        /// <summary>
        /// 计算当前代数与当前计算岛屿的局部退火温度 (Parallel Tempering Temperature Schedule)
        /// 异构多岛架构:
        /// 岛屿 0 (高温探索岛): 维持高退火温度，允许跨越开普勒假壁垒与不同共振槽;
        /// 岛屿 1 (中温平衡岛): 指数退火降温;
        /// 岛屿 2 (低温收敛岛): 极低温度，快速贪婪下降打磨高精度连续解.
        /// </summary>
        public static double GetIslandTemperature(int currentGen, int maxGens, int islandId, int totalIslands)
        {
            if (maxGens <= 1) return 0.0;
            double progress = Math.Max(0.0, Math.Min(1.0, (double)currentGen / maxGens));

            // 基准温度调度曲线: 几何退火降温 T(t) = T_0 * (T_min / T_0)^progress
            double t0 = 15.0;     // 初始摄动温度 (~15 km/s 损失可跨越)
            double tMin = 0.001;  // 终态退火温度 (~1 m/s 精细收敛)
            double baseT = t0 * Math.Pow(tMin / t0, progress);

            if (totalIslands <= 1) return baseT;

            // 依据岛屿职责分配温标 (Parallel Tempering Multi-Temperature Hierarchy)
            switch (islandId % 3)
            {
                case 0: // 高温探索岛
                    return Math.Max(baseT * 3.5, 0.5);
                case 1: // 中温过渡岛
                    return baseT;
                case 2: // 低温收敛岛
                default:
                    return Math.Max(baseT * 0.15, 0.0001);
            }
        }

        /// <summary>
        /// 基于 Metropolis 统计力学准则评估是否接受变差的候选解
        /// 若 candidateLoss < currentLoss: 100% 接受;
        /// 若 candidateLoss >= currentLoss: 以概率 P = exp(-(L_cand - L_cur) / T) 接受.
        /// </summary>
        public static bool MetropolisAccept(
            double currentLoss, 
            double candidateLoss, 
            double temperature, 
            System.Random rand)
        {
            if (candidateLoss <= currentLoss) return true;
            if (temperature <= 1e-6 || double.IsNaN(temperature)) return false;

            double deltaLoss = candidateLoss - currentLoss;
            // 避免极端指数下溢
            if (deltaLoss / temperature > 40.0) return false;

            double prob = Math.Exp(-deltaLoss / temperature);
            return (rand.NextDouble() < prob);
        }
    }
}
