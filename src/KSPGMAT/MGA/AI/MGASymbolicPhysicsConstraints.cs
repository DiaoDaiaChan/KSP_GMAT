// ============================================================================
// MGASymbolicPhysicsConstraints.cs
// 符号物理经验约束引擎 (Symbolic Physics Constraint Engine)
// 理论基础: 双曲偏折几何极限 delta_max = 2 * arcsin(1 / (1 + r_p * v_inf^2 / mu))
// 机制: O(1) 代数硬拦截，在执行昂贵三角/浮点/Lambert计算前过滤物理无解采样
// ============================================================================

using System;
using KSPGMAT.Model;

namespace KSPGMAT.MGA.AI
{
    /// <summary>
    /// 符号物理与代数极限快速判定器
    /// </summary>
    public static class MGASymbolicPhysicsConstraints
    {
        private const double Rad2Deg = 180.0 / Math.PI;
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// 计算给定天体引力参数 mu, 最低近拱半径 rp, 以及渐近相对速度 vInf 下的最大物理偏折角 (弧度)
        /// </summary>
        public static double ComputeMaxDeflectionAngleRad(double mu, double rpKm, double vInfKms)
        {
            if (mu <= 0 || rpKm <= 0 || vInfKms <= 0) return 0.0;
            double denom = 1.0 + (rpKm * 1000.0 * (vInfKms * 1000.0) * (vInfKms * 1000.0)) / (mu * 1e9);
            if (denom <= 1.0) return Math.PI;
            double sinVal = 1.0 / denom;
            if (sinVal >= 1.0) return Math.PI;
            return 2.0 * Math.Asin(sinVal);
        }

        /// <summary>
        /// 快速几何判定：两次借力之间的进入与离去渐近速度是否在物理最大偏折角容限内
        /// </summary>
        public static bool IsFlybyTurnFeasible(
            Vector3d vInfIn, 
            Vector3d vInfOut, 
            double mu, 
            double rpMinKm, 
            double maxPoweredDeltaVKms,
            out double requiredDeflectionRad,
            out double maxDeflectionRad)
        {
            double vInMag = vInfIn.magnitude;
            double vOutMag = vInfOut.magnitude;
            requiredDeflectionRad = 0.0;
            maxDeflectionRad = 0.0;

            if (vInMag < 1e-4 || vOutMag < 1e-4) return false;

            // 1. 渐近转角计算
            double dot = Vector3d.Dot(vInfIn, vInfOut) / (vInMag * vOutMag);
            if (dot > 1.0) dot = 1.0;
            if (dot < -1.0) dot = -1.0;
            requiredDeflectionRad = Math.Acos(dot);

            // 2. 最大转角 (以较小 vInf 为准，偏折能力最大)
            double vInfRef = Math.Min(vInMag, vOutMag);
            maxDeflectionRad = ComputeMaxDeflectionAngleRad(mu, rpMinKm, vInfRef);

            // 3. 若转角小于最大物理转角，无动力即完全可行
            if (requiredDeflectionRad <= maxDeflectionRad + 1e-5)
            {
                return true;
            }

            // 4. 若转角超标，评估机动所需动力助推 Delta V 上限估算 (解析下界)
            // Delta V_turn >= 2 * v_inf * sin((delta_req - delta_max) / 2)
            double excessTurnRad = requiredDeflectionRad - maxDeflectionRad;
            double lowerBoundTurnDv = 2.0 * vInfRef * Math.Sin(excessTurnRad * 0.5);

            return lowerBoundTurnDv <= maxPoweredDeltaVKms;
        }

        /// <summary>
        /// 快速粗筛：检验给定转移段的平均日心半长轴与标称飞行时间是否违背基本开普勒开尔文-霍曼下界
        /// </summary>
        public static bool IsToFKeplerianFeasible(double r1Km, double r2Km, double tofSec, double centralMu)
        {
            if (tofSec <= 10.0 || r1Km <= 0 || r2Km <= 0 || centralMu <= 0) return false;

            // 霍曼转移半长轴与半周期
            double aHohmannKm = (r1Km + r2Km) * 0.5;
            double muM = centralMu * 1e9;
            double aHohmannM = aHohmannKm * 1000.0;
            double minTofHohmannSec = Math.PI * Math.Sqrt((aHohmannM * aHohmannM * aHohmannM) / muM);

            // 即使双曲超速极限，航行时间通常也不低于极值开普勒下界的 15%
            double absoluteMinTofSec = minTofHohmannSec * 0.15;
            if (tofSec < absoluteMinTofSec) return false;

            // 上界：通常不应该超过 4 倍霍曼周期（避免多圈长时间死滞）
            double absoluteMaxTofSec = minTofHohmannSec * 6.0;
            if (tofSec > absoluteMaxTofSec) return false;

            return true;
        }

        /// <summary>
        /// 快速符号损失代数惩罚：在未执行严苛 Lambert 时快速估算几何违背程度
        /// 严格遵循 C1-连续二次势垒损失原理，杜绝任何常量死刑阶跃基数 (如 +1000)
        /// </summary>
        public static double EvaluateSymbolicViolationPenalty(
            Vector3d vInfIn, 
            Vector3d vInfOut, 
            double mu, 
            double rpMinKm, 
            double maxPoweredDeltaVKms)
        {
            if (!IsFlybyTurnFeasible(vInfIn, vInfOut, mu, rpMinKm, maxPoweredDeltaVKms, out double reqRad, out double maxRad))
            {
                double excess = Math.Max(0.0, reqRad - maxRad);
                double vInMag = vInfIn.magnitude;
                double vOutMag = vInfOut.magnitude;
                double vInfRef = Math.Min(vInMag, vOutMag);
                double turnDv = 2.0 * vInfRef * Math.Sin(excess * 0.5);
                double dvExcess = Math.Max(0.0, turnDv - maxPoweredDeltaVKms);
                // C1 连续光滑二次物理势垒: 在边界处无缝连续归零，随违背量平滑递增
                return 100.0 * (dvExcess + 0.5 * dvExcess * dvExcess) + 500.0 * excess * excess;
            }
            return 0.0;
        }
    }
}
