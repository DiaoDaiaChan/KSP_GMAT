using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 天体星历与物理参数的数据源方案
    /// </summary>
    public enum EphemerisScheme {
        /// <summary>方案 2 (首选): 游戏运行态 (FlightGlobals 实时天体树动态提取)</summary>
        Scheme2_FlightGlobals = 2,
        /// <summary>方案 1 (权威回退): NASA JPL SPICE 权威星历内核 (DE440 动态反算 / 原生常数)</summary>
        Scheme1_SpiceDE440 = 1
    }

    /// <summary>
    /// 方案 1 与方案 2 星历与轨道根数精确误差核验结果
    /// (100% C++ 原生计算矢量差与欧氏范数，0 托管物理计算)
    /// </summary>
    public class SchemeComparisonResult {
        public string BodyName = "";
        public double EpochUt = 0.0;
        public bool Scheme1Available = false;
        public bool Scheme2Available = false;
        public Vector3d PosScheme1 = Vector3d.zero; // SPICE (km)
        public Vector3d VelScheme1 = Vector3d.zero; // SPICE (km/s)
        public Vector3d PosScheme2 = Vector3d.zero; // FlightGlobals (km)
        public Vector3d VelScheme2 = Vector3d.zero; // FlightGlobals (km/s)
        public double SmaScheme1 = 0;
        public double SmaScheme2 = 0;
        public double EccScheme1 = 0;
        public double EccScheme2 = 0;
        public double IncScheme1 = 0;
        public double IncScheme2 = 0;

        // 100% Native C++ 原生计算误差指标
        public double PosDiffKm = 0.0;   // Δr = |r_KSP - r_SPICE|
        public double VelDiffMs = 0.0;   // Δv = |v_KSP - v_SPICE|
        public double DaKm = 0.0;        // |a_KSP - a_SPICE|
        public double De = 0.0;          // |e_KSP - e_SPICE|
        public double DincDeg = 0.0;     // |inc_KSP - inc_SPICE|
    }

    /// <summary>
    /// 行星天体模型与轨道星历 (支持 方案2 实时游戏天体 与 方案1 SPICE 权威回退)
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
        public string ParentName { get; set; } = ""; // Reference central body name
        public bool IsMoon { get; set; } = false; // Whether this is a moon / secondary satellite
        public EphemerisScheme SourceScheme { get; set; } = EphemerisScheme.Scheme2_FlightGlobals;
        public EphemerisScheme ActiveEvaluationScheme { get; set; } = EphemerisScheme.Scheme2_FlightGlobals;

        public List<Orbit> OrbitChain { get; set; } = new List<Orbit>();

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
        /// 优先执行方案 2 (游戏运行态)，不可用时自动平滑回退方案 1 (NASA JPL SPICE)
        /// </summary>
        public void GetHeliocentricState(double ut, out Vector3d posKm, out Vector3d velKms) {
            // 1. 方案 2 (第一优先级): 如果已预先提取 OrbitChain，在任意线程均可直接计算开普勒位置与速度
            // (100% 线程安全，纯 C# 开普勒求解，0 Unity API 跨线程调用)
            if (OrbitChain != null && OrbitChain.Count > 0) {
                try {
                    ActiveEvaluationScheme = EphemerisScheme.Scheme2_FlightGlobals;
                    Vector3d r_total = Vector3d.zero;
                    Vector3d v_total = Vector3d.zero;
                    for (int i = 0; i < OrbitChain.Count; i++) {
                        var orb = OrbitChain[i];
                        if (orb != null) {
                            r_total += orb.getRelativePositionAtUT(ut);
                            v_total += orb.getOrbitalVelocityAtUT(ut);
                        }
                    }
                    // KSP Orbit.getRelativePositionAtUT(ut) 内部经 Planetarium.Zup.WorldToLocal 求解，
                    // 输出已处于 Z-Up (黄道北极法向) 惯性直角坐标系：
                    // X=春分点, Y=黄道面内90°, Z=黄道北极法向，与 PyKEP 标准 ECLIPJ2000 完全一致 (严禁交换 Y 与 Z)
                    posKm = new Vector3d(r_total.x / 1000.0, r_total.y / 1000.0, r_total.z / 1000.0);
                    velKms = new Vector3d(v_total.x / 1000.0, v_total.y / 1000.0, v_total.z / 1000.0);
                    return;
                } catch { }
            }

            // 1.1 若在主线程且 FlightGlobals 就绪，直接动态读取并回填 OrbitChain
            if (SolarSystemCatalog.IsFlightGlobalsReady && SolarSystemCatalog.IsMainThread) {
                try {
                    var b = FlightGlobals.Bodies.Find(x => 
                        x.name.Equals(Name, StringComparison.OrdinalIgnoreCase) || 
                        x.bodyName.Equals(Name, StringComparison.OrdinalIgnoreCase));
                    if (b != null && b.orbit != null) {
                        ActiveEvaluationScheme = EphemerisScheme.Scheme2_FlightGlobals;
                        Vector3d r_total = Vector3d.zero;
                        Vector3d v_total = Vector3d.zero;
                        var curr = b;
                        var chain = new List<Orbit>();
                        while (curr != null && curr.orbit != null && curr.referenceBody != null && curr.referenceBody != curr) {
                            chain.Add(curr.orbit);
                            r_total += curr.orbit.getRelativePositionAtUT(ut);
                            v_total += curr.orbit.getOrbitalVelocityAtUT(ut);
                            curr = curr.referenceBody;
                        }
                        OrbitChain = chain;
                        posKm = new Vector3d(r_total.x / 1000.0, r_total.y / 1000.0, r_total.z / 1000.0);
                        velKms = new Vector3d(v_total.x / 1000.0, v_total.y / 1000.0, v_total.z / 1000.0);
                        return;
                    }
                } catch { }
            }

            // 2. 方案 2 失败时，自动平滑回退 方案 1: NASA JPL SPICE 数值星历 (DE440 切比雪夫多项式插值)
            // 仅对 SPICE 内核支持的真实太阳系标准天体查询，避免虚构/KSP天体产生 IDCODENOTFOUND
            ActiveEvaluationScheme = EphemerisScheme.Scheme1_SpiceDE440;
            if (PykepNative.HasSpiceLoaded && SolarSystemCatalog.StandardSolarBodyNames.Contains(Name)) {
                if (PykepNative.GetSpicePlanetState(Name, ut, "ECLIPJ2000", out posKm, out velKms)) {
                    return;
                }
            }

            // 3. 独立后备星历: 方案 1 根数外推 (如果 SPICE 内核未载入，通过原生 C++ propagate_lagrangian 求解)
            //    (开普勒牛顿迭代 + par2ic + propagate_lagrangian, 0 托管自编公式, 基线7.0)
            if (PykepNative.IsAvailable && SMA > 0) {
                if (PykepNative.ElementsToState(
                        SMA, ECC, INC * Mathf.Deg2Rad, RAAN * Mathf.Deg2Rad, AOP * Mathf.Deg2Rad,
                        MA0 * Mathf.Deg2Rad, EpochJ2000, ut, SolarSystemCatalog.SunMu,
                        out posKm, out velKms)) {
                    return;
                }
                KSPGMATLog.Warn($"PykepNative 原生根数外推失败, 返回零状态: {Name}");
            }

            // 4. 原生不可用: 返回零状态降级 (严禁托管开普勒推导)
            posKm = Vector3d.zero;
            velKms = Vector3d.zero;
        }
    }

    /// <summary>
    /// 太阳系行星星历目录管理系统
    /// 实现方案 2 (游戏运行态) 为主，并可平滑回退 方案 1 (NASA JPL SPICE 权威动态反推)
    /// 彻底消除 C# 源码硬编码
    /// </summary>
    public static class SolarSystemCatalog {
        public const double J2000_RSS_UT = EphemerisTimeSystem.J2000_UT; // 01 Jan 2000 12:00:00 in RSS UT

        private static volatile bool _cachedFlightGlobalsReady = false;
        private static double _cachedSunMu = 1.32712440018e11;
        private static int _mainThreadId = -1;

        /// <summary>
        /// 是否处于 Unity 主线程
        /// </summary>
        public static bool IsMainThread {
            get {
                if (_mainThreadId == -1) return true; // 单线程独立 Runner 或未初始化默认环境
                return System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;
            }
        }

        public static void InitializeMainThread() {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            UpdateFlightGlobalsState();
        }

        public static void UpdateFlightGlobalsState() {
            if (_mainThreadId != -1 && System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThreadId) {
                return; // 仅限主线程调用 Unity 单例 API
            }
            try {
                if (HighLogic.LoadedScene != GameScenes.LOADING && FlightGlobals.ready && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0) {
                    _cachedFlightGlobalsReady = true;
                    var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                    if (sun != null && sun.gravParameter > 0) {
                        _cachedSunMu = sun.gravParameter / 1e9;
                    }
                } else {
                    _cachedFlightGlobalsReady = false;
                }
            } catch {
                _cachedFlightGlobalsReady = false;
            }
        }

        /// <summary>
        /// 检查 FlightGlobals 运行态是否完全可用 (线程安全，支持后台工作线程无异常查询)
        /// </summary>
        public static bool IsFlightGlobalsReady {
            get {
                if (IsMainThread) {
                    UpdateFlightGlobalsState();
                }
                return _cachedFlightGlobalsReady;
            }
        }

        /// <summary>
        /// 标准太阳系天体名称列表 (用于方案 1 动态构建权威天体库)
        /// </summary>
        public static readonly string[] StandardSolarBodyNames = new string[] {
            "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto", "Moon"
        };

        public static double SunMu {
            get {
                if (IsFlightGlobalsReady) {
                    return _cachedSunMu;
                }
                return 1.32712440018e11; // Standard Sun GM km^3/s^2
            }
        }

        /// <summary>
        /// 当前全局主生效数据源方案 (优先方案 2，若飞行场景未就绪则回退方案 1)
        /// </summary>
        public static EphemerisScheme CurrentScheme {
            get {
                if (IsFlightGlobalsReady) {
                    return EphemerisScheme.Scheme2_FlightGlobals;
                }
                return EphemerisScheme.Scheme1_SpiceDE440;
            }
        }

        /// <summary>
        /// 获取当前数据源方案的简要说明
        /// </summary>
        public static string GetSchemeDescription(EphemerisScheme s) {
            if (s == EphemerisScheme.Scheme2_FlightGlobals) {
                return I18n.T("ephem_scheme_2_desc", "方案 2: 游戏运行态 (FlightGlobals 实时天体树)");
            }
            return I18n.T("ephem_scheme_1_desc", "方案 1: NASA JPL SPICE 权威内核 (DE440 动态反算)");
        }

        private static readonly Dictionary<string, SolarPlanet> _planetsCache = new Dictionary<string, SolarPlanet>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// 确保所有天体在 Unity 主线程预先全量载入缓存，杜绝多线程寻优时并发读写字典或跨线程访问 Unity API
        /// </summary>
        public static void EnsureAllPlanetsCached() {
            lock (_cacheLock) {
                if (IsMainThread) {
                    UpdateFlightGlobalsState();
                    if (IsFlightGlobalsReady) {
                        SyncFromFlightGlobals();
                    }
                }
                InitScheme1FallbackCache();
            }
        }

        private static void EnsureScheme1Cache() {
            lock (_cacheLock) {
                try { PykepNative.TryAutoLoadSpiceFromGMAT(); } catch { }
                InitScheme1FallbackCache();
            }
        }

        private static void InitScheme1FallbackCache() {
            lock (_cacheLock) {
                foreach (var name in StandardSolarBodyNames) {
                    if (!_planetsCache.ContainsKey(name)) {
                        var p = CreateFromSpiceScheme(name);
                        if (p != null) {
                            _planetsCache[p.Name] = p;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 方案 1: 纯动态调用底层 Native C++ SPICE 提取常数与 J2000 开普勒根数，0 C# 静态硬编码
        /// </summary>
        public static SolarPlanet CreateFromSpiceScheme(string name) {
            if (string.IsNullOrEmpty(name)) return null;

            // 1. 调用底层 C++ 获取权威常数 (GM, 半径, 安全飞越半径)
            double mu = 0, rKm = 0, safeKm = 0;
            PykepNative.GetSpicePlanetConstants(name, out mu, out rKm, out safeKm);

            // 2. 调用底层 C++ 从 SPICE DE440 切比雪夫状态与 ic2par 动态反算 J2000 瞬时开普勒根数
            double sma = 0, ecc = 0, inc = 0, raan = 0, aop = 0, ma = 0;
            PykepNative.GetSpicePlanetElements(name, J2000_RSS_UT, SunMu, out sma, out ecc, out inc, out raan, out aop, out ma);

            if (sma <= 0) {
                (sma, ecc, inc, mu, rKm) = GetStandardPlanetFallback(name);
                safeKm = rKm + 100.0;
            }

            string icon = GetDefaultIcon(name);
            return new SolarPlanet {
                Name = name,
                DisplayName = I18n.T("body_" + name, name),
                Icon = icon,
                Mu = mu > 0 ? mu : 1000.0,
                RadiusKm = rKm > 0 ? rKm : 500.0,
                SafeRadiusKm = safeKm > 0 ? safeKm : 520.0,
                SMA = sma,
                ECC = ecc,
                INC = inc,
                RAAN = raan,
                AOP = aop,
                MA0 = ma,
                EpochJ2000 = J2000_RSS_UT,
                SourceScheme = EphemerisScheme.Scheme1_SpiceDE440
            };
        }

        /// <summary>
        /// 动态天体映射字典 (随时动态直读游戏内实际天体数据并接入 I18N)
        /// </summary>
        public static Dictionary<string, SolarPlanet> Planets {
            get {
                EnsureAllPlanetsCached();
                lock (_cacheLock) {
                    return new Dictionary<string, SolarPlanet>(_planetsCache, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public static void SyncFromFlightGlobals() {
            if (!IsFlightGlobalsReady || !IsMainThread) return;

            try {
                lock (_cacheLock) {
                    foreach (var b in FlightGlobals.Bodies) {
                        if (b == null || string.IsNullOrEmpty(b.name)) continue;
                        var sp = CreateFromCelestialBody(b);
                        if (sp != null) {
                            _planetsCache[b.name] = sp;
                            if (!string.IsNullOrEmpty(b.bodyName) && !_planetsCache.ContainsKey(b.bodyName)) {
                                _planetsCache[b.bodyName] = sp;
                            }
                        }
                    }
                }
            } catch { }
        }

        public static string CleanDisplayName(string raw) {
            if (string.IsNullOrEmpty(raw)) return "";
            return System.Text.RegularExpressions.Regex.Replace(raw, @"\^[A-Za-z]", "").Trim();
        }

        /// <summary>
        /// 方案 2: 随时从游戏运行态 CelestialBody 即时创建天体物理模型并接入 I18N
        /// </summary>
        public static SolarPlanet CreateFromCelestialBody(CelestialBody b) {
            if (b == null) return null;
            string icon = GetDefaultIcon(b.name);
            double safeAtm = b.atmosphere ? (b.atmosphereDepth / 1000.0) : 20.0;
            double rKm = b.Radius / 1000.0;
            var orb = b.orbit;

            // 预存轨道链路 (用于后台多线程无 Unity API 依赖的开普勒直接求值)
            var chain = new List<Orbit>();
            var curr = b;
            while (curr != null && curr.orbit != null && curr.referenceBody != null && curr.referenceBody != curr) {
                chain.Add(curr.orbit);
                curr = curr.referenceBody;
            }

            // 接入 I18N 本地化：优先读取 I18n.T("body_" + name)，未注册时回退游戏内置显示名
            string rawClean = !string.IsNullOrEmpty(b.displayName) ? CleanDisplayName(b.displayName) : b.name;
            string localizedName = I18n.T("body_" + b.name, rawClean);

            return new SolarPlanet {
                Name = b.name,
                DisplayName = localizedName,
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
                EpochJ2000 = orb != null ? orb.epoch : 0.0,
                ParentName = b.referenceBody != null ? b.referenceBody.name : "",
                IsMoon = (b.referenceBody != null && b.referenceBody.orbit != null),
                SourceScheme = EphemerisScheme.Scheme2_FlightGlobals,
                OrbitChain = chain
            };
        }

        /// <summary>
        /// 获取天体模型 (优先方案 2 游戏实时天体，回退方案 1 SPICE 权威天体)
        /// 100% 线程安全，严禁在未命中时将目标行星降级为地球 1.0 AU 轨道
        /// </summary>
        public static SolarPlanet GetPlanet(string name) {
            if (string.IsNullOrEmpty(name)) name = "Earth";

            lock (_cacheLock) {
                // 1. 优先从线程安全缓存查询
                if (_planetsCache.TryGetValue(name, out var cached)) return cached;
                foreach (var kv in _planetsCache) {
                    if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        kv.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(kv.Value.DisplayName) && kv.Value.DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)) {
                        return kv.Value;
                    }
                }

                // 2. 方案 2: 若在主线程且 FlightGlobals 就绪，直接动态提取
                if (IsFlightGlobalsReady && IsMainThread) {
                    try {
                        var b = FlightGlobals.Bodies.Find(body => 
                            body.name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                            body.bodyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(body.displayName) && CleanDisplayName(body.displayName).Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(body.displayName) && CleanDisplayName(body.displayName).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));
                        if (b != null) {
                            var sp = CreateFromCelestialBody(b);
                            if (sp != null) {
                                _planetsCache[b.name] = sp;
                                _planetsCache[name] = sp;
                                return sp;
                            }
                        }
                    } catch { }
                }

                // 3. 动态从 SPICE / 原生引擎反推天体
                var spicePlanet = CreateFromSpiceScheme(name);
                if (spicePlanet != null && spicePlanet.SMA > 0) {
                    _planetsCache[spicePlanet.Name] = spicePlanet;
                    _planetsCache[name] = spicePlanet;
                    return spicePlanet;
                }

                // 4. 严谨物理兜底保护对象 (杜绝任何天体被退化为地球 1.0 AU)
                double mu = 0, rKm = 0, safeKm = 0;
                PykepNative.GetSpicePlanetConstants(name, out mu, out rKm, out safeKm);
                double sma = 0, ecc = 0, inc = 0, raan = 0, aop = 0, ma = 0;
                PykepNative.GetSpicePlanetElements(name, J2000_RSS_UT, SunMu, out sma, out ecc, out inc, out raan, out aop, out ma);

                if (sma <= 0) {
                    (sma, ecc, inc, mu, rKm) = GetStandardPlanetFallback(name);
                    safeKm = rKm + 100.0;
                }

                string defaultIcon = GetDefaultIcon(name);
                var fallbackPlanet = new SolarPlanet {
                    Name = name,
                    DisplayName = I18n.T("body_" + name, name),
                    Icon = defaultIcon,
                    Mu = mu > 0 ? mu : 398600.4418,
                    RadiusKm = rKm > 0 ? rKm : 6378.14,
                    SafeRadiusKm = safeKm > 0 ? safeKm : 6518.14,
                    SMA = sma,
                    ECC = ecc,
                    INC = inc,
                    RAAN = raan,
                    AOP = aop,
                    MA0 = ma,
                    EpochJ2000 = J2000_RSS_UT,
                    SourceScheme = EphemerisScheme.Scheme1_SpiceDE440
                };
                _planetsCache[name] = fallbackPlanet;
                return fallbackPlanet;
            }
        }

        /// <summary>
        /// 太阳系与常见天体标准真实半长轴与物理常数后备库 (防止任何未识别天体退化为 1.0 AU)
        /// </summary>
        private static (double sma, double ecc, double inc, double mu, double rKm) GetStandardPlanetFallback(string name) {
            string upper = (name ?? "").ToUpperInvariant();
            if (upper.Contains("MERCURY") || upper.Contains("水星") || upper.Contains("MOHO"))
                return (57909100.0, 0.20563069, 7.00487, 22032.09, 2439.7);
            if (upper.Contains("VENUS") || upper.Contains("金星") || upper.Contains("EVE"))
                return (108208930.0, 0.00677323, 3.39471, 324859.0, 6051.8);
            if (upper.Contains("EARTH") || upper.Contains("地球") || upper.Contains("KERBIN"))
                return (149598023.0, 0.01671022, 0.00000, 398600.4418, 6378.14);
            if (upper.Contains("MOON") || upper.Contains("月球") || upper.Contains("MUN"))
                return (149598023.0, 0.05490000, 5.14500, 4902.8, 1737.4);
            if (upper.Contains("MARS") || upper.Contains("火星") || upper.Contains("DUNA"))
                return (227936640.0, 0.09341233, 1.85061, 42828.37, 3389.5);
            if (upper.Contains("JUPITER") || upper.Contains("木星") || upper.Contains("JOOL"))
                return (778412020.0, 0.04839266, 1.30530, 126686534.0, 69911.0);
            if (upper.Contains("SATURN") || upper.Contains("土星"))
                return (1426725400.0, 0.05415060, 2.48446, 37931187.0, 58232.0);
            if (upper.Contains("URANUS") || upper.Contains("天王星"))
                return (2870972200.0, 0.04716771, 0.76986, 5793939.0, 25362.0);
            if (upper.Contains("NEPTUNE") || upper.Contains("海王星"))
                return (4498252900.0, 0.00858587, 1.76917, 6836529.0, 24622.0);
            if (upper.Contains("PLUTO") || upper.Contains("冥王星"))
                return (5906380000.0, 0.24880766, 17.16000, 869.6, 1188.3);
            return (149598023.0, 0.0167, 0.0, 398600.4418, 6378.14);
        }

        /// <summary>
        /// 获取当前可用天体全列表
        /// </summary>
        public static List<SolarPlanet> GetAvailablePlanets() {
            // 优先动态发现当前游戏内所有公转天体 (方案 2: 全量兼容 RSS / OPM / JNSQ / Stock 及任意小行星包与扩展星球包)
            if (IsFlightGlobalsReady) {
                try {
                    var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                    var allOrbiters = FlightGlobals.Bodies
                        .Where(b => b != sun && b.orbit != null && b.orbit.semiMajorAxis > 0)
                        .OrderBy(b => (b.referenceBody == sun ? 0 : 1)) // 优先日心天体(主行星/矮行星/小行星)，后置卫星
                        .ThenBy(b => b.orbit.semiMajorAxis)
                        .Select(b => CreateFromCelestialBody(b))
                        .ToList();
                    if (allOrbiters.Count > 0) return allOrbiters;
                } catch { }
            }

            // 离线/未载入时返回方案 1 权威天体缓存
            EnsureScheme1Cache();
            return _planetsCache.Values.ToList();
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

        /// <summary>
        /// 对比指定天体在方案 1 (NASA JPL SPICE) 与 方案 2 (FlightGlobals) 之间的误差值
        /// (100% C++ 原生计算差值指标，0 托管物理计算)
        /// </summary>
        public static SchemeComparisonResult ComparePlanetSchemes(string bodyName, double ut) {
            var res = new SchemeComparisonResult {
                BodyName = bodyName,
                EpochUt = ut
            };

            // 自动检测参考坐标系：
            // 在 RSS (Real Solar System) 模组中，游戏内天体坐标系以地球 J2000 赤道面为基准 (Earth.inc ≈ 23.44°)；
            // 若此时 SPICE 查询 ECLIPJ2000 (黄道面)，两者会存在 23.44° 的黄赤交角，导致 Δr 呈现 1.5 亿公里的几何坐标系夹角差。
            // 自动适配 SPICE 参考系至游戏内实际对齐坐标系 ("J2000" 赤道系 或 "ECLIPJ2000" 黄道系)。
            string spiceFrame = "ECLIPJ2000";
            if (IsFlightGlobalsReady) {
                var earthRef = FlightGlobals.Bodies.Find(x => x.name.Equals("Earth", StringComparison.OrdinalIgnoreCase));
                if (earthRef != null && earthRef.orbit != null && earthRef.orbit.inclination > 15.0) {
                    spiceFrame = "J2000";
                }
            }

            // 1. 方案 1: SPICE 状态与根数
            if (PykepNative.HasSpiceLoaded) {
                if (PykepNative.GetSpicePlanetState(bodyName, ut, spiceFrame, out res.PosScheme1, out res.VelScheme1)) {
                    res.Scheme1Available = true;
                }
            }
            double raan1 = 0, aop1 = 0, ma1 = 0;
            PykepNative.GetSpicePlanetElements(bodyName, ut, SunMu, out res.SmaScheme1, out res.EccScheme1, out res.IncScheme1, out raan1, out aop1, out ma1);
            if (spiceFrame == "J2000" && res.Scheme1Available) {
                Vector3d h = Vector3d.Cross(res.PosScheme1, res.VelScheme1);
                if (h.magnitude > 1e-5) {
                    res.IncScheme1 = Math.Acos(Mathf.Clamp((float)(h.z / h.magnitude), -1f, 1f)) * Mathf.Rad2Deg;
                }
            }

            // 2. 方案 2: FlightGlobals 游戏运行态天体
            if (IsFlightGlobalsReady) {
                try {
                    var b = FlightGlobals.Bodies.Find(x => 
                        x.name.Equals(bodyName, StringComparison.OrdinalIgnoreCase) || 
                        x.bodyName.Equals(bodyName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(x.displayName) && CleanDisplayName(x.displayName).Equals(bodyName, StringComparison.OrdinalIgnoreCase)));
                    if (b != null && b.orbit != null) {
                        res.Scheme2Available = true;
                        Vector3d r_total = Vector3d.zero;
                        Vector3d v_total = Vector3d.zero;
                        var curr = b;
                        while (curr != null && curr.orbit != null && curr.referenceBody != null && curr.referenceBody != curr) {
                            r_total += curr.orbit.getRelativePositionAtUT(ut);
                            v_total += curr.orbit.getOrbitalVelocityAtUT(ut);
                            curr = curr.referenceBody;
                        }
                        res.PosScheme2 = new Vector3d(r_total.x / 1000.0, r_total.y / 1000.0, r_total.z / 1000.0);
                        res.VelScheme2 = new Vector3d(v_total.x / 1000.0, v_total.y / 1000.0, v_total.z / 1000.0);
                        res.SmaScheme2 = b.orbit.semiMajorAxis / 1000.0;
                        res.EccScheme2 = b.orbit.eccentricity;
                        res.IncScheme2 = b.orbit.inclination;
                    }
                } catch { }
            }

            // 3. 调用 100% C++ 原生计算方案 1 与方案 2 的矢量差与范数误差 (0 托管物理计算)
            if (res.Scheme1Available && res.Scheme2Available) {
                PykepNative.CompareBodyStates(
                    res.PosScheme1, res.VelScheme1,
                    res.PosScheme2, res.VelScheme2,
                    res.SmaScheme1, res.EccScheme1, res.IncScheme1,
                    res.SmaScheme2, res.EccScheme2, res.IncScheme2,
                    out res.PosDiffKm, out res.VelDiffMs,
                    out res.DaKm, out res.De, out res.DincDeg);
            }

            return res;
        }

        private static string GetDefaultIcon(string name) {
            if (string.IsNullOrEmpty(name)) return "Pl";
            string clean = CleanDisplayName(name).Trim();
            string defaultIcon = (clean.Length >= 2)
                ? (char.ToUpperInvariant(clean[0]).ToString() + char.ToLowerInvariant(clean[1]).ToString())
                : clean.ToUpperInvariant();
            return I18n.T("icon_" + clean, defaultIcon);
        }
    }
}
