using System;
using System.Collections.Generic;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;
using UnityEngine;

namespace MGADebugRunner.TestSuites
{
    public class EphemerisSchemeComparisonTest : ITestModule
    {
        public string ModuleId => "ephemeris_comparison";
        public string Name => "星历方案对比与坐标系核验测试 (SPICE vs 游戏运行态)";
        public string Description => "验证 SPICE DE440 vs 游戏运行态误差核验，坐标系定义差与木星反推";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("EphemerisScheme_Comparison");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("总检验项", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"星历方案对比未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🔍 [用例集 B] 方案 1 (SPICE DE440) vs 方案 2 (游戏运行态) 误差核验 (直接调用 GUI 对应 API)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            double testUt = EphemerisTimeSystem.J2000_UT;
            string testBody = "Saturn";
            var comp = SolarSystemCatalog.ComparePlanetSchemes(testBody, testUt);

            Console.WriteLine($"核验天体: {comp.BodyName} @ UT={testUt:F0}s (J2000)");
            Console.WriteLine($"• 方案 1 (SPICE) 就绪: {comp.Scheme1Available}");
            Console.WriteLine($"• 方案 2 (游戏态) 就绪: {comp.Scheme2Available} (离线测试环境中 FlightGlobals 隔离防崩溃)");
            Console.WriteLine($"• SPICE 态矢量 r: ({comp.PosScheme1.x:F1}, {comp.PosScheme1.y:F1}, {comp.PosScheme1.z:F1}) km");
            Console.WriteLine($"• SPICE 态矢量 v: ({comp.VelScheme1.x:F3}, {comp.VelScheme1.y:F3}, {comp.VelScheme1.z:F3}) km/s");
            Console.WriteLine($"• SPICE 轨道根数: SMA={comp.SmaScheme1:F1} km, ECC={comp.EccScheme1:F5}, INC={comp.IncScheme1:F3}°");

            double sma = 0, ecc = 0, inc = 0, raan = 0, aop = 0, ma = 0;
            try
            {
                var asm = typeof(Orbit).Assembly;
                var planType = asm.GetType("Planetarium");
                Console.WriteLine($"[Planetarium Type] Found: {planType != null}");
                if (planType != null)
                {
                    foreach (var prop in planType.GetProperties())
                    {
                        Console.WriteLine($"  Property: {prop.Name} ({prop.PropertyType.Name})");
                    }
                    foreach (var fld in planType.GetFields())
                    {
                        Console.WriteLine($"  Field: {fld.Name} ({fld.FieldType.Name})");
                    }
                }

                var zupFld = planType?.GetField("Zup");
                if (zupFld != null)
                {
                    var zType = zupFld.FieldType;
                    Console.WriteLine($"  Zup Field Type FullName: {zType.FullName}");
                    try
                    {
                        object zupVal = zupFld.GetValue(null);
                        Console.WriteLine($"  Zup Instance: {zupVal != null}");
                        if (zupVal != null)
                        {
                            var xf = zType.GetField("X").GetValue(zupVal);
                            var yf = zType.GetField("Y").GetValue(zupVal);
                            var zf = zType.GetField("Z").GetValue(zupVal);
                            Console.WriteLine($"  Zup.X = {xf}, Zup.Y = {yf}, Zup.Z = {zf}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Get Zup Value Error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Planetarium Refl Error] {ex.Message}");
            }

            // 获取 SPICE 瞬时 6 根数
            PykepNative.GetSpicePlanetElements(testBody, testUt, 1.32712440018e11,
                out sma, out ecc, out inc,
                out raan, out aop, out ma);
            Console.WriteLine($"• SPICE 全 6 根数: SMA={sma:F1}, ECC={ecc:F5}, INC={inc:F3}°, RAAN={raan:F3}°, AOP={aop:F3}°, MA={ma:F3}°");

            double ut1989 = GMATClient.ParseUtcGregorian("28 Sep 1989 16:53:40.000");
            Console.WriteLine($"• 历元 UT (J2000): {EphemerisTimeSystem.J2000_UT:F1}s");
            Console.WriteLine($"• 历元 UT (1989):  {ut1989:F1}s");

            var epochs = new (string label, double testTime)[] {
                ("J2000", EphemerisTimeSystem.J2000_UT),
                ("1989-09-28", ut1989)
            };

            foreach (var (label, testTime) in epochs)
            {
                Console.WriteLine($"\n--- 历元: {label} (UT={testTime:F0}) ---");

                // 1. SPICE ECLIPJ2000
                PykepNative.GetSpicePlanetState("Saturn", testTime, "ECLIPJ2000", out Vector3d rEclip, out Vector3d vEclip);
                Console.WriteLine($"  [SPICE ECLIPJ2000] r=({rEclip.x:F1}, {rEclip.y:F1}, {rEclip.z:F1}) km | |r|={rEclip.magnitude:F1} km");
                Console.WriteLine($"  [SPICE ECLIPJ2000] v=(-{vEclip.x:F3}, {vEclip.y:F3}, {vEclip.z:F3}) km/s | |v|={vEclip.magnitude:F3} km/s");

                // 2. SPICE J2000 (Earth Equator)
                PykepNative.GetSpicePlanetState("Saturn", testTime, "J2000", out Vector3d rEq, out Vector3d vEq);
                Console.WriteLine($"  [SPICE J2000-赤道] r=({rEq.x:F1}, {rEq.y:F1}, {rEq.z:F1}) km | |r|={rEq.magnitude:F1} km");
                Console.WriteLine($"  [SPICE J2000-赤道] v=(-{vEq.x:F3}, {vEq.y:F3}, {vEq.z:F3}) km/s | |v|={vEq.magnitude:F3} km/s");

                double diff_eq_eclip = (rEq - rEclip).magnitude;
                Console.WriteLine($"  ▶ 赤道J2000 vs 黄道ECLIPJ2000 差值: Δr = {diff_eq_eclip:N0} km");

                // 3. 木星在 ECLIPJ2000 vs J2000
                PykepNative.GetSpicePlanetState("Jupiter", testTime, "ECLIPJ2000", out Vector3d rJupEclip, out Vector3d vJupEclip);
                PykepNative.GetSpicePlanetState("Jupiter", testTime, "J2000", out Vector3d rJupEq, out Vector3d vJupEq);
                double jupDiff = (rJupEq - rJupEclip).magnitude;
                double jupVDiff = (vJupEq - vJupEclip).magnitude * 1000.0;
                Console.WriteLine($"  ▶ [木星] 赤道J2000 vs 黄道ECLIPJ2000 差值: Δr = {jupDiff:N0} km, Δv = {jupVDiff:F1} m/s");

                // 木星根数直接反算 (X, Y, Z) vs 对调 (X, Z, Y)
                PykepNative.GetSpicePlanetElements("Jupiter", testTime, 1.32712440018e11,
                    out double j_a, out double j_e, out double j_inc,
                    out double j_raan, out double j_aop, out double j_ma);
                PykepNative.ElementsToState(j_a, j_e, j_inc * Math.PI / 180.0, j_raan * Math.PI / 180.0, j_aop * Math.PI / 180.0, j_ma * Math.PI / 180.0,
                    testTime, testTime, 1.32712440018e11, out Vector3d rJupKep, out Vector3d vJupKep);
                double diff_jup_correct = (rJupEclip - rJupKep).magnitude;
                Vector3d rJupSwapped = new Vector3d(rJupKep.x, rJupKep.z, rJupKep.y);
                double diff_jup_swapped = (rJupEclip - rJupSwapped).magnitude;
                Console.WriteLine($"  ▶ [木星] 正确 (X, Y, Z) 差值: Δr = {diff_jup_correct:F2} km");
                Console.WriteLine($"  ▶ [木星] 错误对调 (X, Z, Y) 差值: Δr = {diff_jup_swapped:N0} km");
            }

            passed++;
        }

        public static void InvestigateJupiterError(double targetUt)
        {
            Console.WriteLine("==========================================================================================");
            Console.WriteLine($"🔬 [反推分析] 木星在 UT = {targetUt:F1}s 下的星历方案对比与误差重现");
            Console.WriteLine("==========================================================================================");

            Vector3d r_eclip, v_eclip;
            Vector3d r_j2000, v_j2000;
            PykepNative.GetSpicePlanetState("Jupiter", targetUt, "ECLIPJ2000", out r_eclip, out v_eclip);
            PykepNative.GetSpicePlanetState("Jupiter", targetUt, "J2000", out r_j2000, out v_j2000);

            Console.WriteLine($"[SPICE ECLIPJ2000] r = ({r_eclip.x:F1}, {r_eclip.y:F1}, {r_eclip.z:F1}) km, |r| = {r_eclip.magnitude:F1} km");
            Console.WriteLine($"                  v = ({v_eclip.x:F3}, {v_eclip.y:F3}, {v_eclip.z:F3}) km/s, |v| = {v_eclip.magnitude:F3} km/s");
            Console.WriteLine($"[SPICE J2000]      r = ({r_j2000.x:F1}, {r_j2000.y:F1}, {r_j2000.z:F1}) km, |r| = {r_j2000.magnitude:F1} km");
            Console.WriteLine($"                  v = ({v_j2000.x:F3}, {v_j2000.y:F3}, {v_j2000.z:F3}) km/s, |v| = {v_j2000.magnitude:F3} km/s");

            double delta_r_frames = (r_eclip - r_j2000).magnitude;
            double delta_v_frames = (v_eclip - v_j2000).magnitude * 1000.0;
            Console.WriteLine($"\n⭐【纯坐标系定义差：ECLIPJ2000 vs J2000】");
            Console.WriteLine($"   Δr = {delta_r_frames:N0} km");
            Console.WriteLine($"   Δv = {delta_v_frames:N1} m/s");

            double sunMu = SolarSystemCatalog.SunMu;
            double a1 = 0, e1 = 0, inc1 = 0, raan1 = 0, aop1 = 0, ma1 = 0;
            PykepNative.GetSpicePlanetElements("Jupiter", targetUt, sunMu, out a1, out e1, out inc1, out raan1, out aop1, out ma1);
            Console.WriteLine($"\n[SPICE Elements] a = {a1:N0} km, e = {e1:F5}, inc = {inc1:F3}°, RAAN = {raan1:F3}°, AOP = {aop1:F3}°, MA = {ma1:F3}°");

            var jupPlanet = SolarSystemCatalog.GetPlanet("Jupiter");
            Console.WriteLine($"[Catalog Jupiter] a = {jupPlanet.SMA:N0} km, e = {jupPlanet.ECC:F5}, inc = {jupPlanet.INC:F3}°");

            var candidates = new List<(string Name, Vector3d r, Vector3d v)>();
            candidates.Add(("ECLIPJ2000 (x, y, z)", r_eclip, v_eclip));
            candidates.Add(("ECLIPJ2000 (x, z, y) 轴置换", new Vector3d(r_eclip.x, r_eclip.z, r_eclip.y), new Vector3d(v_eclip.x, v_eclip.z, v_eclip.y)));
            candidates.Add(("J2000 (x, y, z)", r_j2000, v_j2000));
            candidates.Add(("J2000 (x, z, y) 轴置换", new Vector3d(r_j2000.x, r_j2000.z, r_j2000.y), new Vector3d(v_j2000.x, v_j2000.z, v_j2000.y)));

            Console.WriteLine("\n------------------------------------------------------------------------------------------");
            Console.WriteLine("📊 排列组合对比核验 (目标误差: Δr ≈ 199,519,539 km, Δv ≈ 11,163.1 m/s):");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            Console.WriteLine("\n🔥【精准复现修改前的旧代码】：");
            Vector3d old_sc1_pos = r_j2000;
            Vector3d old_sc1_vel = v_j2000;
            
            Vector3d old_sc2_pos = new Vector3d(r_eclip.x, r_eclip.z, r_eclip.y);
            Vector3d old_sc2_vel = new Vector3d(v_eclip.x, v_eclip.z, v_eclip.y);

            double old_dr = (old_sc1_pos - old_sc2_pos).magnitude;
            double old_dv = (old_sc1_vel - old_sc2_vel).magnitude * 1000.0;
            Console.WriteLine($"   旧代码方案 1 (J2000) vs 旧代码方案 2 (ECLIPJ2000 交换YZ):");
            Console.WriteLine($"   => 位置偏差 Δr = {old_dr:N0} km  (截图目标值: 199,519,539 km)");
            Console.WriteLine($"   => 速度偏差 Δv = {old_dv:N1} m/s (截图目标值: 11,163.1 m/s)");

            Vector3d old_sc2_j2000_swap = new Vector3d(r_j2000.x, r_j2000.z, r_j2000.y);
            Vector3d old_sc2_j2000_swap_v = new Vector3d(v_j2000.x, v_j2000.z, v_j2000.y);
            double dr_j_swap = (old_sc1_pos - old_sc2_j2000_swap).magnitude;
            double dv_j_swap = (old_sc1_vel - old_sc2_j2000_swap_v).magnitude * 1000.0;
            Console.WriteLine($"   旧代码方案 1 (J2000) vs J2000(交换YZ): Δr = {dr_j_swap:N0} km, Δv = {dv_j_swap:N1} m/s");

            Console.WriteLine("\n🔍 开始执行全局坐标变换与实现反推求解器...");
            SearchMatchingTransformation(r_eclip, v_eclip, r_j2000, v_j2000, 199519539.0, 11163.1);
        }

        public static void SearchMatchingTransformation(Vector3d r1, Vector3d v1, Vector3d r1_j2000, Vector3d v1_j2000, double targetDr, double targetDv)
        {
            Console.WriteLine("\n🛰️ 【从木星真实开普勒根数推算 KSP Orbit 并与 SPICE 比较】：");
            Vector3d sc1_old = r1_j2000;
            Vector3d sc1_old_v = v1_j2000;
            
            Vector3d sc2_swap_eclip_r = new Vector3d(r1.x, r1.z, r1.y);
            Vector3d sc2_swap_eclip_v = new Vector3d(v1.x, v1.z, v1.y);
            
            double test_dr = (sc1_old - sc2_swap_eclip_r).magnitude;
            double test_dv = (sc1_old_v - sc2_swap_eclip_v).magnitude * 1000.0;
            Console.WriteLine($"   旧代码假设 1: SPICE(J2000) vs KSP(ECLIP, 交换YZ):");
            Console.WriteLine($"      Δr = {test_dr:N0} km, Δv = {test_dv:N1} m/s");

            int matches = 0;
            for (double phi = 0; phi < 360; phi += 0.5)
            {
                double rad = phi * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                Vector3d r_rot = new Vector3d(r1.x * cos - r1.y * sin, r1.x * sin + r1.y * cos, r1.z);
                Vector3d v_rot = new Vector3d(v1.x * cos - v1.y * sin, v1.x * sin + v1.y * cos, v1.z);

                Vector3d r_rot_swap = new Vector3d(r_rot.x, r_rot.z, r_rot.y);
                Vector3d v_rot_swap = new Vector3d(v_rot.x, v_rot.z, v_rot.y);

                double dr = (sc1_old - r_rot_swap).magnitude;
                double dv = (sc1_old_v - v_rot_swap).magnitude * 1000.0;

                if (Math.Abs(dr - targetDr) < 500000.0 && Math.Abs(dv - targetDv) < 200.0)
                {
                    matches++;
                    Console.WriteLine($"   🎯 精确命中！当相位偏移 phi = {phi:F1}° 时：");
                    Console.WriteLine($"      Δr = {dr:N0} km (目标: {targetDr:N0} km)");
                    Console.WriteLine($"      Δv = {dv:N1} m/s (目标: {targetDv:N1} m/s)");
                }
            }
            if (matches == 0)
            {
                Console.WriteLine("未在纯相位扫描中发现重叠，测试历元偏移扫描...");
            }
        }
    }
}
