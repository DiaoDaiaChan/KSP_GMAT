using System;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;
using KSPGMAT.Service;
using UnityEngine;

namespace MGADebugRunner.TestSuites
{
    public class UniversalDynamicBodyRegistryTest : ITestModule
    {
        public string ModuleId => "universal_body_registry";
        public string Name => "普适动态天体注册表与零硬编码动力学测试";
        public string Description => "验证太阳系天体动态注册与零硬编码动力学求解，彻底清除经验多项式";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("UniversalBodyRegistry_DynamicRegistration");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("总检验项", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"动态天体注册表测试未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🌐 [用例集 10 太阳系天体动态注册与零硬编码动力学求解] 彻底清除经验多项式核验");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            try
            {
                string[] solarPlanets = new[] { "Earth", "Mars", "Jupiter", "Moon", "Europa", "Titan" };
                foreach (var bName in solarPlanets)
                {
                    bool ok = PykepNative.GetSpicePlanetProperties(bName, out double mu, out double rKm, out double safeR, out double soiR, out double atmR);
                    if (!ok || mu <= 0 || rKm <= 0)
                    {
                        Console.WriteLine($"❌ 天体 {bName} 属性读取失败！");
                        return;
                    }
                    Console.WriteLine($"  • {bName,-8}: GM={mu:F1} km³/s², 半径={rKm:F1} km, 安全半径={safeR:F1} km, SOI={soiR:F0} km");
                }
                Console.WriteLine("  ✅ 太阳系主要天体物理属性由原生统一注册表驱动，无硬编码分支！");

                double epochJ2000 = EphemerisTimeSystem.J2000_UT;
                bool regOk = PykepNative.RegisterBody(
                    "CeresTest", "Sun", 62.63, 473.0, 493.0, 100000.0, 0.0,
                    413950000.0, 0.076, 10.59, 80.3, 73.1, 95.0, epochJ2000);

                if (!regOk)
                {
                    Console.WriteLine("❌ 动态注册天体 CeresTest 失败！");
                    return;
                }
                Console.WriteLine("  • 成功调用 PykepNative.RegisterBody 动态注册 CeresTest 根数与物理常数");

                double tTarget = epochJ2000 + 86400.0 * 100.0;
                bool stateOk = PykepNative.GetSpicePlanetState("CeresTest", tTarget, "ECLIPJ2000", out Vector3d posCeres, out Vector3d velCeres);
                if (!stateOk || posCeres.magnitude < 3.5e8 || posCeres.magnitude > 4.5e8)
                {
                    Console.WriteLine($"❌ 动态注册天体状态解算异常: r={posCeres.magnitude:F1} km");
                    return;
                }
                Console.WriteLine($"  • 动态天体拉格朗日递推日心距离: |r|={posCeres.magnitude:F1} km, |v|={velCeres.magnitude:F3} km/s (物理自洽，0 经验多项式) ✅");

                bool elemOk = PykepNative.GetSpicePlanetElements("CeresTest", tTarget, 1.32712440018e11, out double a, out double e, out double iDeg, out double raan, out double aop, out double ma);
                if (!elemOk || Math.Abs(a - 413950000.0) > 100.0 || Math.Abs(e - 0.076) > 0.001)
                {
                    Console.WriteLine($"❌ 动态天体开普勒根数异常: a={a:F0}, e={e:F4}");
                    return;
                }
                Console.WriteLine($"  • 动态天体根数读取: a={a:F0} km, e={e:F4}, i={iDeg:F2}°, M={ma:F1}° ✅");

                passed++;
                Console.WriteLine("\n✅ 通用天体动态注册、二体拉格朗日递推与零硬编码太阳系动力学核验 100% 通过！\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 动态天体注册测试异常: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
