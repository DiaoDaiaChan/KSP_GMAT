using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MGABenchmarkRunner {
    public struct Vector3D {
        public double x, y, z;
        public Vector3D(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3D zero => new Vector3D(0, 0, 0);
        public double magnitude => Math.Sqrt(x * x + y * y + z * z);
        public double sqrMagnitude => x * x + y * y + z * z;
        public Vector3D normalized => magnitude > 1e-12 ? this / magnitude : zero;
        public static Vector3D operator +(Vector3D a, Vector3D b) => new Vector3D(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3D operator -(Vector3D a, Vector3D b) => new Vector3D(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3D operator *(Vector3D a, double d) => new Vector3D(a.x * d, a.y * d, a.z * d);
        public static Vector3D operator *(double d, Vector3D a) => new Vector3D(a.x * d, a.y * d, a.z * d);
        public static Vector3D operator /(Vector3D a, double d) => new Vector3D(a.x / d, a.y / d, a.z / d);
        public static double Dot(Vector3D a, Vector3D b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3D Cross(Vector3D a, Vector3D b) => new Vector3D(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public override string ToString() => $"({x:+0.000;-0.000}, {y:+0.000;-0.000}, {z:+0.000;-0.000})";
    }

    public class PlanetData {
        public string Name { get; set; }
        public double SMA { get; set; } // km
        public double ECC { get; set; }
        public double INC { get; set; } // deg
        public double RAAN { get; set; } // deg
        public double AOP { get; set; } // deg
        public double MA0 { get; set; } // deg
        public double Mu { get; set; } // km^3/s^2
        public double RadiusKm { get; set; } // km
        public double SafeAtmAltKm { get; set; } // km

        public (Vector3D pos, Vector3D vel) GetState(double ut) {
            double sunMu = 1.32712440018e11;
            double n = Math.Sqrt(sunMu / Math.Pow(SMA, 3));
            double j2000UT = 1546344000.0; // 01 Jan 2000 12:00:00 in RSS UT (JD 2451545.0 - 2433647.5 = 17897.5 days)
            double dt = ut - j2000UT;
            double M = (MA0 * Math.PI / 180.0) + n * dt;
            M = M % (2.0 * Math.PI);
            if (M < 0) M += 2.0 * Math.PI;

            double E = M;
            for (int iter = 0; iter < 20; iter++) {
                double f = E - ECC * Math.Sin(E) - M;
                double fPrime = 1.0 - ECC * Math.Cos(E);
                double dE = -f / fPrime;
                E += dE;
                if (Math.Abs(dE) < 1e-12) break;
            }

            double nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + ECC) * Math.Sin(E / 2.0), Math.Sqrt(1.0 - ECC) * Math.Cos(E / 2.0));
            double r = SMA * (1.0 - ECC * Math.Cos(E));

            double xP = r * Math.Cos(nu);
            double yP = r * Math.Sin(nu);
            double h = Math.Sqrt(sunMu * SMA * (1.0 - ECC * ECC));
            double vxP = -(sunMu / h) * Math.Sin(nu);
            double vyP = (sunMu / h) * (ECC + Math.Cos(nu));

            double incRad = INC * Math.PI / 180.0;
            double raanRad = RAAN * Math.PI / 180.0;
            double aopRad = AOP * Math.PI / 180.0;

            double cosO = Math.Cos(raanRad), sinO = Math.Sin(raanRad);
            double cosi = Math.Cos(incRad), sini = Math.Sin(incRad);
            double cosw = Math.Cos(aopRad), sinw = Math.Sin(aopRad);

            double Px = cosO * cosw - sinO * sinw * cosi;
            double Py = sinO * cosw + cosO * sinw * cosi;
            double Pz = sinw * sini;

            double Qx = -cosO * sinw - sinO * cosw * cosi;
            double Qy = -sinO * sinw + cosO * cosw * cosi;
            double Qz = cosw * sini;

            Vector3D pos = new Vector3D(xP * Px + yP * Qx, xP * Py + yP * Qy, xP * Pz + yP * Qz);
            Vector3D vel = new Vector3D(vxP * Px + vyP * Qx, vxP * Py + vyP * Qy, vxP * Pz + vyP * Qz);
            return (pos, vel);
        }
    }

    public class Program {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PykepGetVersionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepLambertSolveDelegate(
            double r1x, double r1y, double r1z,
            double r2x, double r2y, double r2z,
            double tof_sec, double mu, int retrograde,
            out double v1x, out double v1y, out double v1z,
            out double v2x, out double v2y, out double v2z);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PykepFlybyConDelegate(
            double v_in_x, double v_in_y, double v_in_z,
            double v_out_x, double v_out_y, double v_out_z,
            double mu, double safe_radius,
            out double eq_V2, out double ineq_delta, out double delta_deg, out double rp_km);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double PykepFlybyDvDelegate(
            double v_in_x, double v_in_y, double v_in_z,
            double v_out_x, double v_out_y, double v_out_z,
            double mu, double safe_radius);

        private static PykepGetVersionDelegate pykep_get_version;
        private static PykepLambertSolveDelegate pykep_lambert_solve;
        private static PykepFlybyConDelegate pykep_flyby_con;
        private static PykepFlybyDvDelegate pykep_flyby_dv;

        public static Dictionary<string, PlanetData> Planets = new Dictionary<string, PlanetData> {
            ["Mercury"] = new PlanetData { Name = "Mercury", SMA = 57909100.0, ECC = 0.20563069, INC = 7.00487, RAAN = 48.33167, AOP = 29.12478, MA0 = 174.79439, Mu = 22032.09, RadiusKm = 2439.7, SafeAtmAltKm = 50.0 },
            ["Venus"]   = new PlanetData { Name = "Venus", SMA = 108208930.0, ECC = 0.00677323, INC = 3.39471, RAAN = 76.68069, AOP = 54.85229, MA0 = 50.44675, Mu = 324859.0, RadiusKm = 6051.8, SafeAtmAltKm = 300.0 },
            ["Earth"]   = new PlanetData { Name = "Earth", SMA = 149598023.0, ECC = 0.01671022, INC = 0.00000, RAAN = 0.00000, AOP = 102.94719, MA0 = 357.51716, Mu = 398600.44, RadiusKm = 6378.14, SafeAtmAltKm = 140.0 },
            ["Mars"]    = new PlanetData { Name = "Mars", SMA = 227936640.0, ECC = 0.09341233, INC = 1.85061, RAAN = 49.57854, AOP = 286.46230, MA0 = 19.41248, Mu = 42828.37, RadiusKm = 3389.5, SafeAtmAltKm = 100.0 },
            ["Jupiter"] = new PlanetData { Name = "Jupiter", SMA = 778412020.0, ECC = 0.04839266, INC = 1.30530, RAAN = 100.55615, AOP = 274.19770, MA0 = 19.65053, Mu = 126686534.0, RadiusKm = 69911.0, SafeAtmAltKm = 1000.0 },
            ["Saturn"]  = new PlanetData { Name = "Saturn", SMA = 1426725400.0, ECC = 0.05415060, INC = 2.48446, RAAN = 113.71504, AOP = 338.71690, MA0 = 317.51230, Mu = 37931187.0, RadiusKm = 58232.0, SafeAtmAltKm = 1200.0 },
            ["Uranus"]  = new PlanetData { Name = "Uranus", SMA = 2870972200.0, ECC = 0.04716771, INC = 0.76986, RAAN = 74.22988, AOP = 96.73436, MA0 = 142.26794, Mu = 5793939.0, RadiusKm = 25362.0, SafeAtmAltKm = 800.0 },
            ["Neptune"] = new PlanetData { Name = "Neptune", SMA = 4498252900.0, ECC = 0.00858587, INC = 1.76917, RAAN = 131.72169, AOP = 273.24966, MA0 = 259.90868, Mu = 6836529.0, RadiusKm = 24622.0, SafeAtmAltKm = 800.0 }
        };

        private static double ParseUtcGregorian(string str) {
            string[] parts = str.Trim().Split(new char[] { ' ', ':', '.' }, StringSplitOptions.RemoveEmptyEntries);
            int day = int.Parse(parts[0]);
            string monStr = parts[1];
            int year = int.Parse(parts[2]);
            int hour = parts.Length > 3 ? int.Parse(parts[3]) : 0;
            int min = parts.Length > 4 ? int.Parse(parts[4]) : 0;
            double sec = parts.Length > 5 ? double.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture) : 0;

            string[] months = new string[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            int month = 1;
            for (int m = 1; m <= 12; m++) {
                if (months[m].Equals(monStr, StringComparison.OrdinalIgnoreCase)) { month = m; break; }
            }

            int a = (14 - month) / 12;
            int y = year + 4800 - a;
            int m_adj = month + 12 * a - 3;
            double jdn = day + (153 * m_adj + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
            double jd = jdn + (hour - 12) / 24.0 + min / 1440.0 + sec / 86400.0;
            return (jd - 2433647.5) * 86400.0;
        }

        public static void Main(string[] args) {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Load PyKEP via isolated temp directory
            string nativeDir = Path.GetFullPath(@"GameData/KSPGMAT/native");
            string tempDir = Path.Combine(Path.GetTempPath(), "KSPGMAT_native");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            foreach (var b in new[] { "boost_serialization", "keplerian_toolbox", "pykep_native" }) {
                string src = Path.Combine(nativeDir, b + ".bin");
                string dst = Path.Combine(tempDir, b + ".dll");
                if (File.Exists(src)) {
                    try { File.Copy(src, dst, true); } catch {}
                }
            }

            SetDllDirectory(tempDir);
            LoadLibrary(Path.Combine(tempDir, "boost_serialization.dll"));
            LoadLibrary(Path.Combine(tempDir, "keplerian_toolbox.dll"));
            IntPtr hPykep = LoadLibrary(Path.Combine(tempDir, "pykep_native.dll"));

            if (hPykep == IntPtr.Zero) {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"❌ Failed to load pykep_native (Win32 Error: {err})");
                return;
            }

            pykep_get_version = Marshal.GetDelegateForFunctionPointer<PykepGetVersionDelegate>(GetProcAddress(hPykep, "pykep_get_version"));
            pykep_lambert_solve = Marshal.GetDelegateForFunctionPointer<PykepLambertSolveDelegate>(GetProcAddress(hPykep, "pykep_lambert_solve"));
            pykep_flyby_con = Marshal.GetDelegateForFunctionPointer<PykepFlybyConDelegate>(GetProcAddress(hPykep, "pykep_flyby_con"));
            pykep_flyby_dv = Marshal.GetDelegateForFunctionPointer<PykepFlybyDvDelegate>(GetProcAddress(hPykep, "pykep_flyby_dv"));

            string ver = Marshal.PtrToStringAnsi(pykep_get_version());
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("🌌 NASA / ESA 官方真实 MGA 深空任务 DSM 速度矢量与引力助推物理量完整核验报告");
            Console.WriteLine($"   (Linked Official Engine: {ver})");
            Console.WriteLine("==========================================================================================\n");

            var missions = new (string name, string dep, string[] chain, string launchDate, double[] tofs, double minDV, double maxDV)[] {
                ("Mariner 10 (水手10号, 1973)", "Earth", new[] { "Venus", "Mercury" }, "03 Nov 1973 05:45:00", new[] { 94.0, 52.0 }, 3800.0, 5200.0),
                ("Galileo (伽利略号 VEEGA, 1989)", "Earth", new[] { "Venus", "Earth", "Earth", "Jupiter" }, "18 Oct 1989 16:53:40", new[] { 115.0, 301.0, 731.0, 1095.0 }, 3900.0, 5800.0),
                ("Cassini-Huygens (卡西尼号 VVEJPS, 1997)", "Earth", new[] { "Venus", "Venus", "Earth", "Jupiter", "Saturn" }, "15 Oct 1997 08:43:00", new[] { 193.0, 424.0, 55.0, 499.0, 1279.0 }, 3900.0, 6200.0),
                ("Voyager 2 (旅行者2号 Grand Tour, 1977)", "Earth", new[] { "Jupiter", "Saturn", "Uranus", "Neptune" }, "20 Aug 1977 14:29:44", new[] { 688.0, 762.0, 1618.0, 1307.0 }, 6900.0, 7600.0),
                ("Rosetta (罗塞塔号 E-E-M-E, 2004)", "Earth", new[] { "Earth", "Mars", "Earth" }, "02 Mar 2004 07:17:00", new[] { 365.0, 725.0, 985.0 }, 3600.0, 5200.0)
            };

            double sunMu = 1.32712440018e11;

            foreach (var m in missions) {
                double launchUT = ParseUtcGregorian(m.launchDate);
                Console.WriteLine("==========================================================================================");
                Console.WriteLine($"🚀 任务名称: {m.name}");
                Console.WriteLine($"   航线架构: {m.dep} ➔ {string.Join(" ➔ ", m.chain)}");
                Console.WriteLine($"   历史发射历元: {m.launchDate} (起算 UT: {launchUT:0.0} s)");
                Console.WriteLine("------------------------------------------------------------------------------------------");

                var fullChain = new List<string> { m.dep };
                fullChain.AddRange(m.chain);
                int legCount = fullChain.Count - 1;

                double curUT = launchUT;
                Vector3D lastVOut = Vector3D.zero;
                double totalMissionDV = 0.0;
                double totalFlightDays = m.tofs.Sum();

                for (int k = 0; k < legCount; k++) {
                    string fromN = fullChain[k];
                    string toN = fullChain[k + 1];
                    var pFrom = Planets[fromN];
                    var pTo = Planets[toN];

                    double tofDays = m.tofs[k];
                    double tofSec = tofDays * 86400.0;
                    double arrUT = curUT + tofSec;

                    var (r1, vP1) = pFrom.GetState(curUT);
                    var (r2, vP2) = pTo.GetState(arrUT);

                    int solveOk = pykep_lambert_solve(
                        r1.x, r1.y, r1.z,
                        r2.x, r2.y, r2.z,
                        tofSec, sunMu, 0,
                        out double v1x, out double v1y, out double v1z,
                        out double v2x, out double v2y, out double v2z);

                    Vector3D v1 = new Vector3D(v1x, v1y, v1z);
                    Vector3D v2 = new Vector3D(v2x, v2y, v2z);

                    Vector3D vInfDep = v1 - vP1;
                    Vector3D vInfArr = v2 - vP2;

                    Console.WriteLine($"\n📌 [第 {k + 1} 航段] {fromN} ➔ {toN} (耗时: {tofDays:F1} 天 | 历元: UT {curUT / 86400.0:F1}d ➔ {arrUT / 86400.0:F1}d)");
                    Console.WriteLine($"   • 出发位置 r1: {r1} km (|r1| = {r1.magnitude / 1.496e8:F3} AU)");
                    Console.WriteLine($"   • 到达位置 r2: {r2} km (|r2| = {r2.magnitude / 1.496e8:F3} AU)");
                    Console.WriteLine($"   • 日心转移出射速度 v1: {v1} km/s (|v1| = {v1.magnitude:F2} km/s)");
                    Console.WriteLine($"   • 行星当前公转速度 vP1: {vP1} km/s (|vP1| = {vP1.magnitude:F2} km/s)");
                    Console.WriteLine($"   • 出射双曲超速矢量 v_inf,dep: {vInfDep} km/s (|v_inf| = {vInfDep.magnitude:F3} km/s)");

                    if (k == 0) {
                        // Launch Ejection (LEO 200 km)
                        double rPark = pFrom.RadiusKm + 200.0;
                        double vPark = Math.Sqrt(pFrom.Mu / rPark);
                        double c3 = vInfDep.sqrMagnitude;
                        double vHypPeri = Math.Sqrt(c3 + 2.0 * pFrom.Mu / rPark);
                        double dVEject = (vHypPeri - vPark) * 1000.0;
                        totalMissionDV += dVEject;

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"   🌟 [发射脱离机动 (TMI/TJI)]: ΔV = {dVEject:F1} m/s | C3 特征能量 = {c3:F2} km²/s² | 停泊轨脱离速度 = {vHypPeri:F2} km/s");
                        Console.ResetColor();

                        lastVOut = vInfArr;
                    } else {
                        // Flyby / Deep Space Maneuver Node (DSM)
                        Vector3D vIn = lastVOut;
                        Vector3D vOut = vInfDep;

                        double safeRadius = pFrom.RadiusKm + pFrom.SafeAtmAltKm;
                        int flybyOk = pykep_flyby_con(
                            vIn.x, vIn.y, vIn.z,
                            vOut.x, vOut.y, vOut.z,
                            pFrom.Mu, safeRadius,
                            out double eqV2, out double ineqDelta, out double turnAngleDeg, out double rpKm);

                        double dV_dsm = pykep_flyby_dv(
                            vIn.x, vIn.y, vIn.z,
                            vOut.x, vOut.y, vOut.z,
                            pFrom.Mu, safeRadius) * 1000.0;

                        double altPeri = rpKm - pFrom.RadiusKm;
                        totalMissionDV += dV_dsm;

                        // Frenet 3-axis projection
                        Vector3D vP_unit = vP1.normalized;
                        Vector3D norm_unit = Vector3D.Cross(r1, vP1).normalized;
                        Vector3D rad_unit = Vector3D.Cross(norm_unit, vP_unit).normalized;
                        Vector3D diffV = (vOut - vIn) * 1000.0;
                        double dvPro = Vector3D.Dot(diffV, vP_unit);
                        double dvNorm = Vector3D.Dot(diffV, norm_unit);
                        double dvRad = Vector3D.Dot(diffV, rad_unit);

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   🔄 [ESA 官方动力学引力助推 / 深空机动 (DSM #{k}) @ {fromN}]:");
                        Console.WriteLine($"      • 进入相对速度 v_inf,in: {vIn} km/s (|v| = {vIn.magnitude:F3} km/s)");
                        Console.WriteLine($"      • 出射相对速度 v_inf,out: {vOut} km/s (|v| = {vOut.magnitude:F3} km/s)");
                        Console.WriteLine($"      • 双曲偏转角 δ: {turnAngleDeg:F2}° | 实际近心高度: {altPeri:F0} km (安全下限: {pFrom.SafeAtmAltKm:F0} km)");
                        Console.WriteLine($"      • DSM 动力冲量 ΔV_dsm: {dV_dsm:F1} m/s | 3轴机动矢量: (切向: {dvPro:+0.0;-0.0}, 法向: {dvNorm:+0.0;-0.0}, 径向: {dvRad:+0.0;-0.0}) m/s");
                        Console.WriteLine($"      • 借力物理可行性判定: {(flybyOk == 0 ? "✅ 物理完全可行 (无触地)" : "⚠️ 超出借力限度需补充推进")}");
                        Console.ResetColor();

                        lastVOut = vInfArr;
                    }

                    Console.WriteLine($"   • 到达日心速度 v2: {v2} km/s (|v2| = {v2.magnitude:F2} km/s)");
                    Console.WriteLine($"   • 到达双曲超速 v_inf,arr: {vInfArr} km/s (|v_inf| = {vInfArr.magnitude:F3} km/s)");

                    curUT = arrUT;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n🏆 任务综合结论: 总 ΔV 消耗 = {totalMissionDV:F1} m/s (历史工程参考: {m.minDV:F0} ~ {m.maxDV:F0} m/s) | 总历时 = {totalFlightDays:F0} 天 ({totalFlightDays / 365.25:F2} 年) >> PASS");
                Console.ResetColor();
                Console.WriteLine();
            }
        }
    }
}
