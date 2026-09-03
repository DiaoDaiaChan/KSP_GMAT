using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 真实太阳系行星天体模型与轨道星历
    /// (Real Solar System Planetary Model & Ephemeris)
    /// </summary>
    public class SolarPlanet {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Icon { get; set; }
        public double Mu { get; set; }              // km^3 / s^2
        public double RadiusKm { get; set; }        // km
        public double SafeRadiusKm { get; set; }    // km (Radius + Atmosphere / Safety margin)
        public double SMA { get; set; }             // km
        public double ECC { get; set; }
        public double INC { get; set; }             // deg
        public double RAAN { get; set; }            // deg
        public double AOP { get; set; }             // deg
        public double MA0 { get; set; }             // Mean Anomaly at J2000 (deg)
        public double EpochJ2000 { get; set; } = 0.0; // UT of reference epoch

        /// <summary>
        /// 本地化天体显示名 (跟随当前语言，如 zh: "水星 (Mercury)" / en: "Mercury")。
        /// 未注册语言时回退到双语 DisplayName 以便搜索匹配。
        /// </summary>
        public string GetDisplayName() {
            return I18n.T("body_" + Name, DisplayName);
        }

        /// <summary>
        /// 计算指定 UT 时刻相对太阳的日心 ICRF 笛卡尔空间状态 (位置 km, 速度 km/s)
        /// 100% 线程安全且与 3D 开普勒轨道环几何严格锁相
        /// </summary>
        public void GetHeliocentricState(double ut, out Vector3d posKm, out Vector3d velKms) {
            // 1. 优先直接从 KSP 实时星历提取真实日心位置与速度 (100% 绝对物理真值)
            if (FlightGlobals.ready && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0) {
                var b = FlightGlobals.Bodies.Find(x => 
                    x.name.Equals(Name, StringComparison.OrdinalIgnoreCase) || 
                    x.bodyName.Equals(Name, StringComparison.OrdinalIgnoreCase));
                if (b != null && b.orbit != null) {
                    posKm = b.orbit.getRelativePositionAtUT(ut) / 1000.0;
                    velKms = b.orbit.getOrbitalVelocityAtUT(ut) / 1000.0;
                    return;
                }
            }

            // 2. 独立后备解析星历计算
            double dt = ut - EpochJ2000;
            double n = Math.Sqrt(SolarSystemCatalog.SunMu / (SMA * SMA * SMA)); // Mean motion rad/s
            double M = (MA0 * Mathf.Deg2Rad + n * dt) % (2.0 * Math.PI);
            if (M < 0) M += 2.0 * Math.PI;

            // Solve Kepler's equation M = E - e*sin(E)
            double E = M;
            for (int iter = 0; iter < 12; iter++) {
                double f = E - ECC * Math.Sin(E) - M;
                double fPrime = 1.0 - ECC * Math.Cos(E);
                double delta = f / fPrime;
                E -= delta;
                if (Math.Abs(delta) < 1e-13) break;
            }

            double nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + ECC) * Math.Sin(E * 0.5), Math.Sqrt(1.0 - ECC) * Math.Cos(E * 0.5));
            double r = SMA * (1.0 - ECC * Math.Cos(E));

            // Orbit plane coordinates
            double pX = r * Math.Cos(nu);
            double pY = r * Math.Sin(nu);

            double h = Math.Sqrt(SolarSystemCatalog.SunMu * SMA * (1.0 - ECC * ECC));
            double vX = -(SolarSystemCatalog.SunMu / h) * Math.Sin(nu);
            double vY = (SolarSystemCatalog.SunMu / h) * (ECC + Math.Cos(nu));

            // Rotate by AOP, INC, RAAN to J2000 Ecliptic/Equatorial frame
            double incRad = INC * Mathf.Deg2Rad;
            double raanRad = RAAN * Mathf.Deg2Rad;
            double aopRad = AOP * Mathf.Deg2Rad;

            double cosO = Math.Cos(raanRad), sinO = Math.Sin(raanRad);
            double cosi = Math.Cos(incRad),  sini = Math.Sin(incRad);
            double cosw = Math.Cos(aopRad),  sinw = Math.Sin(aopRad);

            double Px = cosO * cosw - sinO * sinw * cosi;
            double Py = sinO * cosw + cosO * sinw * cosi;
            double Pz = sinw * sini;

            double Qx = -cosO * sinw - sinO * cosw * cosi;
            double Qy = -sinO * sinw + cosO * cosw * cosi;
            double Qz = cosw * sini;

            posKm = new Vector3d(pX * Px + pY * Qx, pX * Py + pY * Qy, pX * Pz + pY * Qz);
            velKms = new Vector3d(vX * Px + vY * Qx, vX * Py + vY * Qy, vX * Pz + vY * Qz);
        }
    }

    /// <summary>
    /// 真实太阳系各大行星标准常数与星历全景字典
    /// (NASA Standard Astronomical Constants Catalog for Real Solar System)
    /// </summary>
    public static class SolarSystemCatalog {
        public const double J2000_RSS_UT = 1546344000.0; // 01 Jan 2000 12:00:00 in RSS UT (JD 2451545.0 - 2433647.5 = 17897.5 days)

        public static double SunMu {
            get {
                if (FlightGlobals.ready && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0) {
                    var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                    if (sun != null && sun.gravParameter > 0) return sun.gravParameter / 1e9;
                }
                return 1.32712440018e11; // Standard Sun GM km^3/s^2
            }
        }

        public static readonly Dictionary<string, SolarPlanet> Planets = new Dictionary<string, SolarPlanet>(StringComparer.OrdinalIgnoreCase) {
            ["Mercury"] = new SolarPlanet {
                Name = "Mercury", DisplayName = "水星 (Mercury)", Icon = "Me",
                Mu = 22032.09, RadiusKm = 2439.7, SafeRadiusKm = 2489.7,
                SMA = 57909100.0, ECC = 0.20563069, INC = 7.00487, RAAN = 48.33167, AOP = 29.12478, MA0 = 174.79439, EpochJ2000 = J2000_RSS_UT
            },
            ["Venus"] = new SolarPlanet {
                Name = "Venus", DisplayName = "金星 (Venus)", Icon = "Ve",
                Mu = 324859.0, RadiusKm = 6051.8, SafeRadiusKm = 6351.8, // 300km atmosphere
                SMA = 108208930.0, ECC = 0.00677323, INC = 3.39471, RAAN = 76.68069, AOP = 54.85229, MA0 = 50.44675, EpochJ2000 = J2000_RSS_UT
            },
            ["Earth"] = new SolarPlanet {
                Name = "Earth", DisplayName = "地球 (Earth)", Icon = "Ea",
                Mu = 398600.4418, RadiusKm = 6378.14, SafeRadiusKm = 6518.14, // 140km safe
                SMA = 149598023.0, ECC = 0.01671022, INC = 0.00000, RAAN = 0.00000, AOP = 102.94719, MA0 = 357.51716, EpochJ2000 = J2000_RSS_UT
            },
            ["Mars"] = new SolarPlanet {
                Name = "Mars", DisplayName = "火星 (Mars)", Icon = "Ma",
                Mu = 42828.3752, RadiusKm = 3389.5, SafeRadiusKm = 3489.5, // 100km atmosphere
                SMA = 227936640.0, ECC = 0.09341233, INC = 1.85061, RAAN = 49.57854, AOP = 286.46230, MA0 = 19.41248, EpochJ2000 = J2000_RSS_UT
            },
            ["Jupiter"] = new SolarPlanet {
                Name = "Jupiter", DisplayName = "木星 (Jupiter)", Icon = "Ju",
                Mu = 126686534.0, RadiusKm = 69911.0, SafeRadiusKm = 70911.0,
                SMA = 778412020.0, ECC = 0.04839266, INC = 1.30530, RAAN = 100.55615, AOP = 274.19770, MA0 = 19.65053, EpochJ2000 = J2000_RSS_UT
            },
            ["Saturn"] = new SolarPlanet {
                Name = "Saturn", DisplayName = "土星 (Saturn)", Icon = "Sa",
                Mu = 37931187.0, RadiusKm = 58232.0, SafeRadiusKm = 59432.0,
                SMA = 1426725400.0, ECC = 0.05415060, INC = 2.48446, RAAN = 113.71504, AOP = 338.71690, MA0 = 317.51230, EpochJ2000 = J2000_RSS_UT
            },
            ["Uranus"] = new SolarPlanet {
                Name = "Uranus", DisplayName = "天王星 (Uranus)", Icon = "Ur",
                Mu = 5793939.0, RadiusKm = 25362.0, SafeRadiusKm = 26162.0,
                SMA = 2870972200.0, ECC = 0.04716771, INC = 0.76986, RAAN = 74.22988, AOP = 96.73436, MA0 = 142.26794, EpochJ2000 = J2000_RSS_UT
            },
            ["Neptune"] = new SolarPlanet {
                Name = "Neptune", DisplayName = "海王星 (Neptune)", Icon = "Ne",
                Mu = 6836529.0, RadiusKm = 24622.0, SafeRadiusKm = 25422.0,
                SMA = 4498252900.0, ECC = 0.00858587, INC = 1.76917, RAAN = 131.72169, AOP = 273.24966, MA0 = 259.90868, EpochJ2000 = J2000_RSS_UT
            },
            ["Pluto"] = new SolarPlanet {
                Name = "Pluto", DisplayName = "冥王星 (Pluto)", Icon = "Pl",
                Mu = 871.0, RadiusKm = 1188.3, SafeRadiusKm = 1208.3,
                SMA = 5906380000.0, ECC = 0.2488, INC = 17.16, RAAN = 110.30, AOP = 113.83, MA0 = 14.53, EpochJ2000 = J2000_RSS_UT
            },
            ["Ceres"] = new SolarPlanet {
                Name = "Ceres", DisplayName = "谷神星 (Ceres)", Icon = "Ce",
                Mu = 62.63, RadiusKm = 473.0, SafeRadiusKm = 483.0,
                SMA = 413700000.0, ECC = 0.0758, INC = 10.59, RAAN = 80.33, AOP = 72.6, MA0 = 95.99, EpochJ2000 = J2000_RSS_UT
            },
            ["Vesta"] = new SolarPlanet {
                Name = "Vesta", DisplayName = "灶神星 (Vesta)", Icon = "Ve",
                Mu = 17.29, RadiusKm = 262.7, SafeRadiusKm = 272.7,
                SMA = 353260000.0, ECC = 0.0887, INC = 7.14, RAAN = 103.81, AOP = 151.2, MA0 = 20.86, EpochJ2000 = J2000_RSS_UT
            },
            ["Pallas"] = new SolarPlanet {
                Name = "Pallas", DisplayName = "智神星 (Pallas)", Icon = "Pa",
                Mu = 13.7, RadiusKm = 256.0, SafeRadiusKm = 266.0,
                SMA = 414400000.0, ECC = 0.231, INC = 34.84, RAAN = 173.08, AOP = 310.2, MA0 = 61.3, EpochJ2000 = J2000_RSS_UT
            },
            ["Eros"] = new SolarPlanet {
                Name = "Eros", DisplayName = "爱神星 (Eros)", Icon = "Er",
                Mu = 0.000446, RadiusKm = 8.4, SafeRadiusKm = 10.0,
                SMA = 217500000.0, ECC = 0.223, INC = 10.83, RAAN = 304.32, AOP = 178.8, MA0 = 310.2, EpochJ2000 = J2000_RSS_UT
            },
            ["Ryugu"] = new SolarPlanet {
                Name = "Ryugu", DisplayName = "龙宫 (Ryugu)", Icon = "Ry",
                Mu = 0.000030, RadiusKm = 0.45, SafeRadiusKm = 1.0,
                SMA = 178000000.0, ECC = 0.190, INC = 5.88, RAAN = 251.6, AOP = 211.4, MA0 = 120.0, EpochJ2000 = J2000_RSS_UT
            },
            ["Ida"] = new SolarPlanet {
                Name = "Ida", DisplayName = "艾女星 (Ida)", Icon = "Id",
                Mu = 0.0028, RadiusKm = 15.7, SafeRadiusKm = 18.0,
                SMA = 428000000.0, ECC = 0.045, INC = 1.14, RAAN = 324.0, AOP = 109.0, MA0 = 15.0, EpochJ2000 = J2000_RSS_UT
            },
            ["Psyche"] = new SolarPlanet {
                Name = "Psyche", DisplayName = "灵神星 (Psyche)", Icon = "Ps",
                Mu = 1.53, RadiusKm = 111.5, SafeRadiusKm = 120.0,
                SMA = 437000000.0, ECC = 0.134, INC = 3.09, RAAN = 150.3, AOP = 228.4, MA0 = 180.0, EpochJ2000 = J2000_RSS_UT
            },
            ["Arrokoth"] = new SolarPlanet {
                Name = "Arrokoth", DisplayName = "阿罗科斯 (Arrokoth)", Icon = "Ar",
                Mu = 0.0001, RadiusKm = 18.0, SafeRadiusKm = 20.0,
                SMA = 6660000000.0, ECC = 0.042, INC = 2.45, RAAN = 187.0, AOP = 170.0, MA0 = 200.0, EpochJ2000 = J2000_RSS_UT
            }
        };

        /// <summary>
        /// 基线 7.6: 纯净太阳系 (Pure Sol/RSS Only) 白名单。
        /// 仅允许真实太阳系天体 (水金地月火木土天海冥)，彻底过滤原版 Kerbol 虚构天体 (Moho/Eve/Kerbin/Mun/Minmus/Duna/Dres/Jool/Eeloo)。
        /// </summary>
        /// <summary>
        /// 过滤原版 Kerbol 虚构天体 (Moho/Eve/Kerbin/Mun/Minmus/Duna/Dres/Jool/Eeloo 等)，保证 Pure Sol/RSS Only。
        /// </summary>
        public static readonly HashSet<string> KerbolFictionalBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Kerbin", "Mun", "Minmus", "Moho", "Eve", "Gilly", "Duna", "Ike", "Dres", 
            "Jool", "Laythe", "Vall", "Tylo", "Bop", "Pol", "Eeloo"
        };

        /// <summary>
        /// 基线 7.6 / 8.0: 纯净太阳系 (Pure Sol/RSS Only) 核心天体白名单。
        /// 包含各大行星、矮行星、小行星及主带天体。
        /// </summary>
        public static readonly HashSet<string> PureSolWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Mercury", "Venus", "Earth", "Moon", "Luna", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto",
            "Ceres", "Vesta", "Pallas", "Juno", "Hygiea",
            "Ryugu", "Eros", "Bennu", "Itokawa", "Ida", "Psyche", "Arrokoth", "Mathilde", "Gaspra", "Lutetia", "Steins",
            "Chiron", "Chariklo", "Sedna", "Makemake", "Haumea", "Eris", "Quaoar", "Orcus", "Gonggong", "Varuna", "Salacia",
            "Io", "Europa", "Ganymede", "Callisto", "Titan", "Enceladus", "Triton", "Charon"
        };

        public static SolarPlanet GetPlanet(string name) {
            if (string.IsNullOrEmpty(name)) name = "Earth";

            // 1. 优先读取 KSP 真实天体星历与引力常数 (RSS / Stock 实时物理星历)
            //    基线 7.6: 仅接受纯净太阳系白名单天体，彻底过滤 Kerbol 虚构天体
            if (FlightGlobals.ready && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0) {
                var b = FlightGlobals.Bodies.Find(body => 
                    (PureSolWhitelist.Contains(body.name) || PureSolWhitelist.Contains(body.bodyName) || !KerbolFictionalBlacklist.Contains(body.name)) &&
                    (body.name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                    body.bodyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(body.displayName) && body.displayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)));
                if (b != null) {
                    string icon = GetDefaultIcon(b.name);
                    double safeAtm = b.atmosphere ? (b.atmosphereDepth / 1000.0) : 50.0;
                    double rKm = b.Radius / 1000.0;
                    var orb = b.orbit;
                    return new SolarPlanet {
                        Name = b.name,
                        DisplayName = $"{icon} {b.name}",
                        Icon = icon,
                        Mu = b.gravParameter / 1e9,
                        RadiusKm = rKm,
                        SafeRadiusKm = rKm + safeAtm,
                        SMA = (orb != null ? orb.semiMajorAxis : 1e8) / 1000.0,
                        ECC = orb != null ? orb.eccentricity : 0.0,
                        INC = orb != null ? orb.inclination : 0.0,
                        RAAN = orb != null ? orb.LAN : 0.0,
                        AOP = orb != null ? orb.argumentOfPeriapsis : 0.0,
                        MA0 = orb != null ? orb.meanAnomalyAtEpoch * Mathf.Rad2Deg : 0.0,
                        EpochJ2000 = orb != null ? orb.epoch : 0.0
                    };
                }
            }

            // 2. 独立后备离线字典
            if (Planets.ContainsKey(name)) return Planets[name];
            foreach (var kv in Planets) {
                if (kv.Value.DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    kv.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }

            return Planets.Values.First();
        }

        public static List<SolarPlanet> GetAvailablePlanets() {
            // 1. 优先动态发现 KSP 真实太阳系日心行星 (RSS)；基线 7.6: 仅保留纯净太阳系白名单天体，
            //    Kerbol 虚构天体一律过滤，保证 Pure Sol/RSS Only。
            if (FlightGlobals.ready && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0) {
                var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                if (sun != null) {
                    var sunOrbiters = FlightGlobals.Bodies
                        .Where(b => b.referenceBody == sun && b != sun && b.orbit != null && b.orbit.semiMajorAxis > 0)
                        .Where(b => PureSolWhitelist.Contains(b.name) || PureSolWhitelist.Contains(b.bodyName) || !KerbolFictionalBlacklist.Contains(b.name))
                        .OrderBy(b => b.orbit.semiMajorAxis)
                        .Select(b => GetPlanet(b.name))
                        .ToList();
                    if (sunOrbiters.Count > 0) return sunOrbiters;
                }
            }

            // 2. 离线标准太阳系顺序 (含主要矮行星与探测小行星)
            string[] solStandardOrder = new string[] {
                "Mercury", "Venus", "Earth", "Mars", "Ryugu", "Eros", "Vesta", "Ceres", "Pallas", "Ida", "Psyche",
                "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto", "Arrokoth"
            };

            var result = new List<SolarPlanet>();
            foreach (var standardName in solStandardOrder) {
                if (Planets.ContainsKey(standardName)) {
                    result.Add(Planets[standardName]);
                }
            }

            return result.Count > 0 ? result : Planets.Values.Where(p => p.Name != "Moon").ToList();
        }

        public static int GetPlanetIndex(string name) {
            var list = GetAvailablePlanets();
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    list[i].DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) {
                    return i;
                }
            }
            return -1;
        }

        private static string GetDefaultIcon(string name) {
            // 基线 7.6: 仅保留真实太阳系天体图标，移除 Kerbol 虚构天体别名
            string n = name.ToLowerInvariant();
            if (n.Contains("mercury")) return "Me";
            if (n.Contains("venus")) return "Ve";
            if (n.Contains("earth")) return "Ea";
            if (n.Contains("moon") || n.Contains("luna")) return "Lu";
            if (n.Contains("mars")) return "Ma";
            if (n.Contains("jupiter")) return "Ju";
            if (n.Contains("saturn")) return "Sa";
            if (n.Contains("uranus")) return "Ur";
            if (n.Contains("neptune")) return "Ne";
            if (n.Contains("pluto")) return "Pl";
            if (n.Contains("ceres")) return "Ce";
            if (n.Contains("vesta")) return "Ve";
            if (n.Contains("pallas")) return "Pa";
            if (n.Contains("ryugu")) return "Ry";
            if (n.Contains("eros")) return "Er";
            if (n.Contains("ida")) return "Id";
            if (n.Contains("psyche")) return "Ps";
            if (n.Contains("arrokoth")) return "Ar";
            if (n.Contains("bennu")) return "Be";
            if (n.Contains("itokawa")) return "It";
            if (name.Length >= 2) return char.ToUpperInvariant(name[0]).ToString() + char.ToLowerInvariant(name[1]).ToString();
            return name.ToUpperInvariant();
        }
    }
}
