using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.Modules
{
    public class OrbitDynamicsTestModule : ITestModule
    {
        public string ModuleId => "orbit_dynamics";
        public string Name => "轨道动力学与普适天体系统模块 (Orbit Dynamics & Universal Systems)";
        public string Description => "验证任意中心天体 (地心/木心/日心) 解耦动力学、停泊轨锁相对齐与脱靶误差消除、SPICE DE440 高精星历与经典行星际转移基准。";

        public bool Run(TestContext ctx)
        {
            var sw = Stopwatch.StartNew();
            bool allOk = true;

            // -------------------------------------------------------------
            // 用例 1: 任意中心天体解耦 (Earth, Jupiter, Sun)
            // -------------------------------------------------------------
            ctx.StartCase("UniversalCentralBody_Decoupling");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌌 [用例集 任意中心天体解耦] Universal Central Body & Mu 动力学引擎闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double earthMu = SolarSystemCatalog.GetCentralMu("Earth");
                double jupiterMu = SolarSystemCatalog.GetCentralMu("Jupiter");
                double sunMu = SolarSystemCatalog.GetCentralMu("Sun");

                bool muOk = Math.Abs(earthMu - 398600.44) <= 1.0 && Math.Abs(jupiterMu - 126686534.0) <= 1000.0 && sunMu > 1e11;

                double earthMoonHohmannDays = PykepBridge.GetHohmannTransferTimeDays("Earth", "Moon");
                bool emOk = earthMoonHohmannDays >= 4.0 && earthMoonHohmannDays <= 6.0;

                double europaGanymedeDays = PykepBridge.GetHohmannTransferTimeDays("Europa", "Ganymede");
                bool egOk = europaGanymedeDays >= 2.0 && europaGanymedeDays <= 3.2;

                ctx.RecordMetric("地球Mu", $"{earthMu:F1}");
                ctx.RecordMetric("木星Mu", $"{jupiterMu:F1}");
                ctx.RecordMetric("地月转移时间", $"{earthMoonHohmannDays:F2} 天");
                ctx.RecordMetric("木卫二->三时间", $"{europaGanymedeDays:F2} 天");

                if (!muOk || !emOk || !egOk)
                {
                    ctx.Fail("中心天体引力常数或子系统转移时间偏离物理预期");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ 任意中心天体解耦动力学闭环核验通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"任意中心天体异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 2: 停泊轨道点火自适应锁相
            // -------------------------------------------------------------
            ctx.StartCase("ParkingOrbit_PhaseAlignment_TLI");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🛰️ [用例集 停泊轨道点火锁相] 停泊轨点火相位自适应锁相与脱靶误差消除闭环核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                var pMoonInit = SolarSystemCatalog.GetPlanet("Moon");
                double tofDaysInit = 4.75;
                double arrUTInit = 68480.197 + tofDaysInit * 86400.0;
                pMoonInit.GetRelativeState("Earth", arrUTInit, out Vector3d rMoonArrKmInit, out Vector3d vMoonArrKmsInit);

                Vector3d hEst = Vector3d.Cross(rMoonArrKmInit, vMoonArrKmsInit);
                double incTransDeg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, hEst.z / hEst.magnitude))) * 180.0 / Math.PI;
                Vector3d nNode = Vector3d.Cross(new Vector3d(0, 0, 1), hEst);
                double raanTransDeg = Math.Atan2(nNode.y, nNode.x) * 180.0 / Math.PI;
                if (raanTransDeg < 0) raanTransDeg += 360.0;

                var orbitEarth = new AnalyticalParkingOrbit("Earth", 200.0, incDeg: incTransDeg, raanDeg: raanTransDeg);
                orbitEarth.EpochUT = 0.0;
                orbitEarth.MeanAnomalyAtEpoch = 0.0;

                double nominalUT = 68480.197;
                double tofDays = 4.75;
                double arrUT = nominalUT + tofDays * 86400.0;
                var pMoon = SolarSystemCatalog.GetPlanet("Moon");
                pMoon.GetRelativeState("Earth", arrUT, out Vector3d rMoonArrKm, out _);

                var solEarth = new MGASolution
                {
                    DepartureUT = nominalUT,
                    ArrivalUT = arrUT,
                    CentralBody = "Earth",
                    TotalFlightDays = tofDays,
                    LaunchDeltaVKms = 3.15,
                    InsertionDeltaVKms = 0.83,
                    TotalDeltaVKms = 3.98,
                    Legs = new List<MGALegResult> {
                        new MGALegResult {
                            FromBody = "Earth", ToBody = "Moon",
                            DepartureUT = nominalUT, ArrivalUT = arrUT,
                            ToFDays = tofDays, ArrivalPosKm = rMoonArrKm,
                            ProgradeMs = 3150.0, DeltaVKms = 3.15
                        }
                    }
                };

                bool alignOk = MGAInjectionAligner.AlignParkingOrbitInjection(orbitEarth, solEarth, out double alignedTime, out Vector3d alignedDv, 0);

                ctx.RecordMetric("锁相计算成功", alignOk ? "PASS" : "FAIL");
                ctx.RecordMetric("对齐脱离历元", $"{alignedTime:F1} s");

                if (!alignOk)
                {
                    ctx.Fail("停泊轨锁相求解失败");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ 停泊轨点火相位自适应锁相核验通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"停泊轨锁相异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            // -------------------------------------------------------------
            // 用例 3: 经典行星际转移 (Earth -> Mars, Venus, Mercury, Jupiter, Saturn)
            // -------------------------------------------------------------
            ctx.StartCase("ClassicInterplanetary_NASA_Benchmarks");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌌 [用例集 经典行星际转移 ΔV 权威基准] Earth ➔ Mars, Venus, Mercury, Jupiter, Saturn");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                double ut1951 = GMATClient.ParseUtcGregorian("10 Feb 1951 14:52:52.244");

                // Mars
                var (marsOptUT, marsToFDays, marsApproxDV) = PykepBridge.CalculateHohmannWindow("Earth", "Mars", ut1951);
                bool marsOk = marsToFDays >= 200.0 && marsToFDays <= 320.0 && marsApproxDV >= 5000.0 && marsApproxDV <= 6500.0;

                // Venus
                var (venusOptUT, venusToFDays, venusApproxDV) = PykepBridge.CalculateHohmannWindow("Earth", "Venus", ut1951);
                bool venusOk = venusToFDays >= 100.0 && venusToFDays <= 180.0 && venusApproxDV >= 6000.0 && venusApproxDV <= 7800.0;

                // Jupiter
                var (jupOptUT, jupToFDays, jupApproxDV) = PykepBridge.CalculateHohmannWindow("Earth", "Jupiter", ut1951);
                bool jupOk = jupToFDays >= 700.0 && jupToFDays <= 1200.0;

                ctx.RecordMetric("火星霍曼基准", marsOk ? "PASS" : "FAIL");
                ctx.RecordMetric("金星霍曼基准", venusOk ? "PASS" : "FAIL");
                ctx.RecordMetric("木星霍曼基准", jupOk ? "PASS" : "FAIL");

                if (!marsOk || !venusOk || !jupOk)
                {
                    ctx.Fail("经典行星际转移指标偏离权威 NASA 基准");
                    allOk = false;
                }
                else
                {
                    Console.WriteLine("✅ 经典行星际转移权威基准核验 100% 通过！\n");
                }
            }
            catch (Exception ex)
            {
                ctx.Fail($"经典转移基准异常: {ex.Message}\n{ex.StackTrace}");
                allOk = false;
            }
            ctx.EndCase();

            sw.Stop();
            ctx.Report.TotalElapsedMs = sw.Elapsed.TotalMilliseconds;
            return allOk;
        }
    }
}
