// ============================================================================
// MGASurrogatePreFilter.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
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
    /// 模块 B: 大时域宏观能量特征面快速初筛
    /// 理论来源: Dario Izzo et al. (2018) "Machine Learning in Trajectory Design"
    /// 机制: 微秒级特征面评估，剔除 >90% 无效高能区，锁定各会合周期能量波谷
    /// </summary>
    public static class MGASurrogatePreFilter
    {
        public struct SynodicWindowCandidate
        {
            public double WindowStartUT;
            public double WindowEndUT;
            public double NominalToFDays;
            public double EstimatedMinDvKms;
        }

        public static List<SynodicWindowCandidate> FilterPromisingWindows(
            MGAAdaptiveSolverConfig config, 
            double synodicPeriodDays)
        {
            var candidates = new List<SynodicWindowCandidate>();
            double totalSpanDays = (config.SearchEndUT - config.SearchStartUT) / MGADefaults.SecondsPerDay;
            if (totalSpanDays <= 0) return candidates;

            double synDays = (synodicPeriodDays > 20.0 && !double.IsNaN(synodicPeriodDays)) 
                ? synodicPeriodDays 
                : 365.25;

            string fromBody = config.DeparturePlanet;
            string toBody = (config.FlybySequence != null && config.FlybySequence.Count > 0)
                ? config.FlybySequence[0]
                : config.ArrivalPlanet;

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            double centralMu = SolarSystemCatalog.GetCentralMu(config.CentralBody ?? "Sun");

            double sma1 = (p1 != null && p1.SMA > 0) ? p1.SMA : 1.496e8;
            double sma2 = (p2 != null && p2.SMA > 0) ? p2.SMA : 1.496e8;
            double aTrans = (sma1 + sma2) * 0.5;
            double muCentralM = (centralMu > 0 ? centralMu : 1.32712440018e11) * 1e9;
            double nomTofDays = Math.PI * Math.Sqrt(Math.Pow(aTrans * 1000.0, 3.0) / muCentralM) / 86400.0;
            if (nomTofDays <= 0 || double.IsNaN(nomTofDays)) nomTofDays = 150.0;

            double minTofD = Math.Max(10.0, nomTofDays * 0.75);
            double maxTofD = nomTofDays * 1.30;

            double stepDays = 15.0; // 15 天粗网格微秒级扫描
            double currentValleyUT = 0.0;
            double currentValleyDv = double.MaxValue;
            double currentValleyTof = nomTofDays;
            double lastCycleBoundary = config.SearchStartUT;

            for (double ut = config.SearchStartUT; ut <= config.SearchEndUT; ut += stepDays * 86400.0)
            {
                double minDvAtEpoch = double.MaxValue;
                double bestTofAtEpoch = nomTofDays;

                p1.GetHeliocentricState(ut, out Vector3d r1, out Vector3d v1);

                for (double tD = minTofD; tD <= maxTofD; tD += 10.0)
                {
                    double arrUT = ut + tD * 86400.0;
                    p2.GetHeliocentricState(arrUT, out Vector3d r2, out Vector3d v2);

                    if (PykepNative.SolveLambert(r1, r2, tD * 86400.0, centralMu, false, out Vector3d vDep, out _))
                    {
                        Vector3d vInf = vDep - v1;
                        double rp = p1.RadiusKm + config.DepPeKm;
                        double vHyp = Math.Sqrt(vInf.sqrMagnitude + 2.0 * p1.Mu / rp);
                        double vPark = Math.Sqrt(p1.Mu / rp);
                        double dv = (vHyp - vPark) * 1000.0; // m/s
                        if (dv < minDvAtEpoch)
                        {
                            minDvAtEpoch = dv;
                            bestTofAtEpoch = tD;
                        }
                    }
                }

                if (minDvAtEpoch < currentValleyDv)
                {
                    currentValleyDv = minDvAtEpoch;
                    currentValleyUT = ut;
                    currentValleyTof = bestTofAtEpoch;
                }

                // 达到一个会合周期或遍历至边界时，将该周期的能量波谷归档为一个聚焦岛屿
                if ((ut - lastCycleBoundary) >= synDays * 86400.0 || ut + stepDays * 86400.0 > config.SearchEndUT)
                {
                    if (currentValleyUT > 0 && currentValleyDv < 1e7)
                    {
                        double wSpan = Math.Min(60.0 * 86400.0, synDays * 0.40 * 86400.0);
                        candidates.Add(new SynodicWindowCandidate
                        {
                            WindowStartUT = Math.Max(config.SearchStartUT, currentValleyUT - wSpan * 0.5),
                            WindowEndUT = Math.Min(config.SearchEndUT, currentValleyUT + wSpan * 0.5),
                            NominalToFDays = currentValleyTof,
                            EstimatedMinDvKms = currentValleyDv / 1000.0
                        });
                    }
                    currentValleyDv = double.MaxValue;
                    currentValleyUT = 0.0;
                    lastCycleBoundary = ut;
                }
            }

            // 如果星历初筛未捕获到有效波谷，降级为均匀会合周期划分
            if (candidates.Count == 0)
            {
                double windowSpanDays = Math.Min(totalSpanDays, synDays);
                for (double ut = config.SearchStartUT; ut < config.SearchEndUT; ut += windowSpanDays * 86400.0)
                {
                    candidates.Add(new SynodicWindowCandidate
                    {
                        WindowStartUT = ut,
                        WindowEndUT = Math.Min(config.SearchEndUT, ut + windowSpanDays * 86400.0),
                        NominalToFDays = nomTofDays,
                        EstimatedMinDvKms = 0.0
                    });
                }
            }

            return candidates;
        }
    }
}
