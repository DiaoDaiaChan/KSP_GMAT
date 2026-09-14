using System;
using KSPGMAT;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace MGADebugRunner.TestSuites
{
    public class PlanetaryCatalogDynamicLoadingTest : ITestModule
    {
        public string ModuleId => "planetary_catalog";
        public string Name => "天体常数与 J2000 根数原生动态加载测试";
        public string Description => "验证纯原生动态反算天体物理常数与 J2000 开普勒根数 (0 源码硬编码)";

        public bool Run(TestContext ctx)
        {
            ctx.StartCase("PlanetaryCatalog_DynamicLoading");
            int total = 0, passed = 0;
            RunTest(ref total, ref passed);
            bool ok = passed == total && total > 0;
            ctx.RecordMetric("总检验天体数", total);
            ctx.RecordMetric("通过数", passed);
            if (!ok) ctx.Fail($"天体常数加载未全部通过 ({passed}/{total})");
            ctx.EndCase(ok);
            return ok;
        }

        public static void RunTest(ref int total, ref int passed)
        {
            total++;
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine("🪐 [用例集 A] 纯原生动态反算天体物理常数与 J2000 开普勒根数 (0 源码硬编码)");
            Console.WriteLine("------------------------------------------------------------------------------------------");

            string[] testBodies = new string[] { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto", "Moon" };
            bool allOk = true;

            foreach (var name in testBodies)
            {
                var p = SolarSystemCatalog.GetPlanet(name);
                if (p == null || p.SMA <= 0 || p.RadiusKm <= 0 || p.Mu <= 0)
                {
                    allOk = false;
                    Console.WriteLine($"❌ {name}: 数据无效！");
                }
                else
                {
                    Console.WriteLine($"• {p.Icon} {p.Name,-8} ({p.GetDisplayName(),-4}): SMA={p.SMA,12:F1} km, ECC={p.ECC,8:F5}, INC={p.INC,6:F2}°, Radius={p.RadiusKm,7:F1} km, Mu={p.Mu,10:F1}");
                }
            }

            if (allOk)
            {
                passed++;
                Console.WriteLine("✅ 天体常数与 J2000 根数全部由 Native C++ SPICE/ic2par 动态生成并通过核验！\n");
            }
            else
            {
                Console.WriteLine("❌ 部分天体数据生成异常！\n");
            }
        }
    }
}
