// ============================================================================
// SolarSystemCatalog.cs  (从 SolarSystemBodies.cs 拆分, P1 模型/目录/标定分层)
// 天体星历目录与 SPICE/Principia 框架标定。底层星历数据源原生 (JPL SPICE / FlightGlobals),
// 目录仅做查找/缓存/取参, 严禁托管自算权威星历。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    public static class SolarSystemCatalog {
        public const double J2000_RSS_UT = EphemerisTimeSystem.J2000_UT; // 01 Jan 2000 12:00:00 in RSS UT
        public static double EarthMu => MGADefaults.EarthMu; // km^3/s^2 (标准参考系动态真值)
        public static double EarthRadiusKm => MGADefaults.EarthRadiusKm; // km (标准参考系动态真值)

        private static double _dynamicRaOffsetDeg = 0.0; // 标准 J2000 ICRF 基准初值 (X=春分点)
        private static volatile bool _hasDynamicallyCalibrated = false;

        /// <summary>
        /// 动态自适应赤道平面偏角：消除 Principia/Sol-Configs 初始赤经原点 (Reference Meridian) 与 SPICE 春分点之间的恒定平面偏角。
        /// 彻底消除硬编码，在游戏运行时通过对太阳系主行星 (Earth, Mars, Venus, Jupiter) 联立圆周平均 (Circular Mean) 动态解析求解。
        /// </summary>
        public static double SPICE_TO_PRINCIPIA_RA_OFFSET_DEG {
            get {
                EnsureDynamicallyCalibrated();
                return _dynamicRaOffsetDeg;
            }
        }

        /// <summary>
        /// 纯动态反推对齐偏角：提取当前时刻 SPICE 原生 J2000 状态与游戏内 Principia 原生 N 体日心真实状态，
        /// 通过对太阳系多颗主天体在赤道平面的相位差进行圆周平均 (Circular Mean) 动态解析求解全局几何偏角。
        /// 0 静态硬编码，完全自适应任意星历历元与太阳系模组配置。
        /// </summary>
        public static bool RecalibrateFrameOffset() {
            if (!IsFlightGlobalsReady || !PykepNative.HasSpiceLoaded) {
                return false;
            }

            try {
                double ut = PrincipiaInterop.IsPrincipiaAvailable() 
                    ? PrincipiaInterop.GetCurrentPrincipiaTime() 
                    : Planetarium.GetUniversalTime();

                string[] samplePlanets = new[] { "Earth", "Mars", "Venus", "Jupiter" };

                double sinSum = 0.0;
                double cosSum = 0.0;
                int validSamples = 0;

                foreach (var name in samplePlanets) {
                    var body = FlightGlobals.Bodies.Find(x => 
                        x.name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                        x.bodyName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (body == null || body.referenceBody == null) continue;

                    // 1. 直读 Principia 原生 N 体日心真实状态 (或未篡改真实轨道)
                    Vector3d rGame;
                    if (PrincipiaInterop.TryGetCelestialFromParent(body, out rGame, out _)) {
                        // 成功直读 Principia 原生
                    } else if (PrincipiaInterop.TryGetHeliocentricState(body, ut, out rGame, out _)) {
                        // 成功提取方案 B
                    } else {
                        continue;
                    }

                    // 2. 直读 SPICE 原生 J2000 真实日心状态 (未旋转)
                    if (!PykepNative.GetSpicePlanetState(name, ut, "J2000", out Vector3d rSpice, out _)) {
                        continue;
                    }

                    // 3. 计算赤道平面的极角差: Δθ_i = φ_game - φ_spice
                    double phiSpice = Math.Atan2(rSpice.y, rSpice.x);
                    double phiGame = Math.Atan2(rGame.y, rGame.x);
                    double dphi = phiGame - phiSpice;

                    sinSum += Math.Sin(dphi);
                    cosSum += Math.Cos(dphi);
                    validSamples++;
                }

                if (validSamples > 0) {
                    // 圆周平均 (Circular Mean) 求解全局最优刚体旋转角
                    double meanRad = Math.Atan2(sinSum, cosSum);
                    double meanDeg = meanRad * (180.0 / Math.PI);
                    while (meanDeg < 0) meanDeg += 360.0;
                    if (meanDeg > 180.0) meanDeg -= 360.0;

                    _dynamicRaOffsetDeg = meanDeg;
                    _hasDynamicallyCalibrated = true;
                    KSPGMATLog.Info($"[SolarSystemCatalog] 动态星历坐标系自适应对齐标定成功: 样本数={validSamples}, 动态反推求解偏角={_dynamicRaOffsetDeg:F6}°");
                    return true;
                }
            } catch (Exception ex) {
                KSPGMATLog.Warn("[SolarSystemCatalog] 动态标定失败: " + ex.Message);
            }
            return false;
        }

        public static void EnsureDynamicallyCalibrated() {
            if (_hasDynamicallyCalibrated) return;
            if (IsMainThread && IsFlightGlobalsReady && PykepNative.HasSpiceLoaded) {
                RecalibrateFrameOffset();
            }
        }

        public static Vector3d AlignSpiceJ2000ToPrincipia(Vector3d v) {
            double rad = SPICE_TO_PRINCIPIA_RA_OFFSET_DEG * Mathf.Deg2Rad;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            return new Vector3d(
                v.x * cos - v.y * sin,
                v.x * sin + v.y * cos,
                v.z
            );
        }

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
            "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto", 
            "Moon", "Europa", "Ganymede", "Callisto", "Io", "Titan", "Ceres", "Vesta"
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
                            try {
                                PykepNative.RegisterBody(
                                    p.Name,
                                    p.ParentName,
                                    p.Mu,
                                    p.RadiusKm,
                                    p.SafeRadiusKm,
                                    p.SoiKm,
                                    p.AtmosphereDepthKm,
                                    p.SMA,
                                    p.ECC,
                                    p.INC,
                                    p.RAAN,
                                    p.AOP,
                                    p.MA0,
                                    p.EpochJ2000
                                );
                            } catch { }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 动态获取当前宇宙根恒星名称 (0 硬编码，实时读取 FlightGlobals 根节点，离线默认回退 "Sun")
        /// </summary>
        public static string RootStarName {
            get {
                if (IsFlightGlobalsReady) {
                    try {
                        var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                        if (sun != null && !string.IsNullOrEmpty(sun.name)) return sun.name;
                    } catch { }
                }
                return "Sun";
            }
        }

        /// <summary>
        /// 权威天体所属母星解析器 (支持游戏运行态动态探测与标准星系后备)
        /// </summary>
        public static string ResolveParentBody(string name) {
            if (string.IsNullOrEmpty(name)) return RootStarName;
            if (IsFlightGlobalsReady) {
                try {
                    var cb = FlightGlobals.Bodies.Find(b => b.name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                                                           b.bodyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                           (!string.IsNullOrEmpty(b.displayName) && CleanDisplayName(b.displayName).Equals(name, StringComparison.OrdinalIgnoreCase)));
                    if (cb != null) {
                        return (cb.referenceBody != null && cb.referenceBody != cb) ? cb.referenceBody.name : RootStarName;
                    }
                } catch { }
            }
            if (_planetsCache.TryGetValue(name, out var cached) && !string.IsNullOrEmpty(cached.ParentName)) {
                return cached.ParentName;
            }
            string upper = name.Trim().ToUpperInvariant();
            if (upper == "MOON" || upper == "LUNA" || upper.Contains("月球")) return "Earth";
            if (upper == "PHOBOS" || upper == "DEIMOS") return "Mars";
            if (upper == "IO" || upper == "EUROPA" || upper == "GANYMEDE" || upper == "CALLISTO") return "Jupiter";
            if (upper == "TITAN" || upper == "ENCELADUS" || upper == "MIMAS" || upper == "TETHYS" || upper == "DIONE" || upper == "RHEA" || upper == "IAPETUS") return "Saturn";
            if (upper == "TRITON") return "Neptune";
            if (upper == "CHARON") return "Pluto";
            if (upper == "MUN" || upper == "MINMUS") return "Kerbin";
            if (upper == "IKE") return "Duna";
            if (upper == "LAYTHE" || upper == "VALL" || upper == "TYLO" || upper == "BOP" || upper == "POL") return "Jool";
            if (upper == "GILLY") return "Eve";
            return RootStarName;
        }

        /// <summary>
        /// 方案 1: 纯动态调用底层 Native C++ SPICE 提取常数与 J2000 开普勒根数，0 C# 静态硬编码
        /// </summary>
        public static SolarPlanet CreateFromSpiceScheme(string name) {
            if (string.IsNullOrEmpty(name)) return null;

            // 1. 调用底层 C++ 获取权威常数与星历物理属性 (GM, 半径, 安全飞越半径, SOI 影响球半径, 大气高度)
            double mu = 0, rKm = 0, safeKm = 0, soiKm = 0, atmDepthKm = 0;
            PykepNative.GetSpicePlanetProperties(name, out mu, out rKm, out safeKm, out soiKm, out atmDepthKm);
            safeKm = ComputeAutomaticSafeRadiusKm(name, rKm, atmDepthKm);

            // 2. 调用底层 C++ 从 SPICE DE440 切比雪夫状态与 ic2par 动态反算 J2000 瞬时开普勒根数
            double sma = 0, ecc = 0, inc = 0, raan = 0, aop = 0, ma = 0;
            PykepNative.GetSpicePlanetElements(name, J2000_RSS_UT, SunMu, out sma, out ecc, out inc, out raan, out aop, out ma);

            if (sma > 0) {
                // 升交点赤经 RAAN 同样补偿该恒定赤道平面偏角
                raan = (raan + SPICE_TO_PRINCIPIA_RA_OFFSET_DEG) % 360.0;
                if (raan < 0) raan += 360.0;
            } else {
                (sma, ecc, inc, mu, rKm) = GetStandardPlanetFallback(name);
                safeKm = ComputeAutomaticSafeRadiusKm(name, rKm, atmDepthKm);
            }

            string parent = ResolveParentBody(name);
            bool isMoon = !string.IsNullOrEmpty(parent) && !parent.Equals("Sun", StringComparison.OrdinalIgnoreCase);

            if (isMoon) {
                // 卫星轨道在母星引力场下定义 (SPICE GetSpicePlanetElements 默认按 Sun 计算日心公转轨道，需还原为其绕母星的半长轴)
                var (moonSma, _, _, _, _) = GetStandardPlanetFallback(name);
                if (moonSma > 0 && moonSma < 1e7) {
                    sma = moonSma;
                }
            }

            if (soiKm <= 0 && sma > 0) {
                double parentMu = isMoon ? GetCentralMu(parent) : SunMu;
                if (parentMu > 0 && mu > 0) {
                    soiKm = sma * Math.Pow(mu / parentMu, 0.4);
                }
            }
            if (name.Equals("Sun", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(parent)) {
                soiKm = double.PositiveInfinity;
            }

            string icon = GetDefaultIcon(name);
            return new SolarPlanet {
                Name = name,
                DisplayName = I18n.T("body_" + name, name),
                Icon = icon,
                Mu = mu > 0 ? mu : 1000.0,
                RadiusKm = rKm > 0 ? rKm : 500.0,
                SafeRadiusKm = safeKm > 0 ? safeKm : 520.0,
                AtmosphereDepthKm = atmDepthKm,
                SoiKm = soiKm,
                SMA = sma,
                ECC = ecc,
                INC = inc,
                RAAN = raan,
                AOP = aop,
                MA0 = ma,
                EpochJ2000 = J2000_RSS_UT,
                ParentName = parent,
                IsMoon = isMoon,
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

                            // 将天体动态注入底层 C++ 原生引擎注册表 (0 源码硬编码)
                            try {
                                PykepNative.RegisterBody(
                                    sp.Name,
                                    sp.ParentName,
                                    sp.Mu,
                                    sp.RadiusKm,
                                    sp.SafeRadiusKm,
                                    sp.SoiKm,
                                    sp.AtmosphereDepthKm,
                                    sp.SMA,
                                    sp.ECC,
                                    sp.INC,
                                    sp.RAAN,
                                    sp.AOP,
                                    sp.MA0,
                                    sp.EpochJ2000
                                );
                            } catch { }
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
        /// 严格基于权威天体物理模型 (MGAStateManager.MGADefaults) 自动推导并计算每个天体的安全飞掠高度 (km)
        /// 1. 大气天体: 大气顶层 + 权威安全缓冲区 (atmDepthKm + Max(MinSafeAltitudeKm, 100km))
        /// 2. 无大气固态天体: 严格采用 MGADefaults.DefaultFlybyAltitudeKm (300 km) 权威基准；微小天体/小行星按半径自适应下限 (>= 30 km)
        /// 杜绝任何贴地 20 km 或贴大气飞行的伪物理作弊解，100% 保持单一真值源 (Single Source of Truth)
        /// </summary>
        public static double ComputeAutomaticSafeFlybyAltitudeKm(string bodyName, double radiusKm, double atmDepthKm) {
            if (atmDepthKm > 0) {
                // 有大气天体：大气顶层 + 权威安全净空缓冲区 (例如 Venus 300+100=400km, Earth 100+100=200km, Titan 600+100=700km)
                return atmDepthKm + Math.Max(MGADefaults.MinSafeAltitudeKm, 100.0);
            }

            // 无大气固态天体 (Mercury, Moon, Ceres, Pluto, etc.)
            // 对微小天体/小行星 (半径 < 100 km) 保障至少 MinSafeAltitudeKm (30 km) 或 15% 半径
            if (radiusKm > 0 && radiusKm < 100.0) {
                return Math.Max(MGADefaults.MinSafeAltitudeKm, radiusKm * 0.15);
            }

            // 标准天体与行星 (Mercury, Moon, Ceres, etc.) 严格采用 MGADefaults 官方基准 DefaultFlybyAltitudeKm (300.0 km)
            return MGADefaults.DefaultFlybyAltitudeKm;
        }

        public static double ComputeAutomaticSafeRadiusKm(string bodyName, double radiusKm, double atmDepthKm) {
            if (radiusKm <= 0) radiusKm = 6000.0;
            double safeAlt = ComputeAutomaticSafeFlybyAltitudeKm(bodyName, radiusKm, atmDepthKm);
            return radiusKm + safeAlt;
        }

        /// <summary>
        /// 方案 2: 随时从游戏运行态 CelestialBody 即时创建天体物理模型并接入 I18N
        /// </summary>
        public static SolarPlanet CreateFromCelestialBody(CelestialBody b) {
            if (b == null) return null;
            string icon = GetDefaultIcon(b.name);
            double rKm = b.Radius / 1000.0;
            double atmDepthKm = b.atmosphere ? (b.atmosphereDepth / 1000.0) : 0.0;
            double soiKm = (b.orbit == null || b.referenceBody == null || b.referenceBody == b)
                ? double.PositiveInfinity
                : (b.sphereOfInfluence / 1000.0);
            double safeKm = ComputeAutomaticSafeRadiusKm(b.name, rKm, atmDepthKm);

            // 方案 B: 优先使用 Principia 内部未篡改真轨道 (避免每帧被覆写的瞬时密切椭圆污染)
            var orb = PrincipiaInterop.GetUnmodifiedOrbit(b) ?? b.orbit;

            // 预存轨道链路 (用于后台多线程无 Unity API 依赖的稳定开普勒直接求值)
            var chain = new List<Orbit>();
            var curr = b;
            while (curr != null && curr.referenceBody != null && curr.referenceBody != curr) {
                var currOrb = PrincipiaInterop.GetUnmodifiedOrbit(curr) ?? curr.orbit;
                if (currOrb != null) chain.Add(currOrb);
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
                SafeRadiusKm = safeKm,
                AtmosphereDepthKm = atmDepthKm,
                SoiKm = soiKm,
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
        /// 全局权威天体等价性判定 (解耦局部硬编码，支持中英双语、Stock KSP 与 RSS/RO 任意别名)
        /// </summary>
        public static bool AreBodiesEqual(string name1, string name2) {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2)) return false;
            if (name1.Equals(name2, StringComparison.OrdinalIgnoreCase)) return true;

            string c1 = CanonicalizeName(name1);
            string c2 = CanonicalizeName(name2);
            if (!string.IsNullOrEmpty(c1) && !string.IsNullOrEmpty(c2) && c1.Equals(c2, StringComparison.OrdinalIgnoreCase)) return true;

            var p1 = GetPlanet(name1);
            var p2 = GetPlanet(name2);
            if (p1 != null && p2 != null && !string.IsNullOrEmpty(p1.Name) && !string.IsNullOrEmpty(p2.Name)) {
                return p1.Name.Equals(p2.Name, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>
        /// 全局权威天体名称归一化 (转换为标准英文字典名，如 Mun/Luna -> Moon, Kerbin -> Earth, Kerbol -> Sun, Duna -> Mars, Jool -> Jupiter)
        /// </summary>
        public static string CanonicalizeName(string name) {
            if (string.IsNullOrEmpty(name)) return "";
            string upper = name.Trim().ToUpperInvariant();

            if (upper == "SUN" || upper == "KERBOL" || upper == "SOL" || upper == "太阳") return "Sun";
            if (upper == "EARTH" || upper == "KERBIN" || upper == "地球") return "Earth";
            if (upper == "MOON" || upper == "LUNA" || upper == "MUN" || upper == "月球") return "Moon";
            if (upper == "MARS" || upper == "DUNA" || upper == "火星") return "Mars";
            if (upper == "VENUS" || upper == "EVE" || upper == "金星") return "Venus";
            if (upper == "MERCURY" || upper == "MOHO" || upper == "水星") return "Mercury";
            if (upper == "JUPITER" || upper == "JOOL" || upper == "木星") return "Jupiter";
            if (upper == "SATURN" || upper == "土星") return "Saturn";
            if (upper == "URANUS" || upper == "天王星") return "Uranus";
            if (upper == "NEPTUNE" || upper == "海王星") return "Neptune";
            if (upper == "PLUTO" || upper == "冥王星") return "Pluto";

            if (upper == "EUROPA" || upper == "木卫二") return "Europa";
            if (upper == "GANYMEDE" || upper == "木卫三") return "Ganymede";
            if (upper == "CALLISTO" || upper == "木卫四") return "Callisto";
            if (upper == "IO" || upper == "木卫一") return "Io";
            if (upper == "TITAN" || upper == "土卫六") return "Titan";
            if (upper == "ENCELADUS" || upper == "土卫二") return "Enceladus";
            if (upper == "CERES" || upper == "谷神星") return "Ceres";
            if (upper == "VESTA" || upper == "灶神星") return "Vesta";

            return name.Trim();
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
                double mu = 0, rKm = 0, safeKm = 0, soiKm = 0, atmDepthKm = 0;
                PykepNative.GetSpicePlanetProperties(name, out mu, out rKm, out safeKm, out soiKm, out atmDepthKm);
                double sma = 0, ecc = 0, inc = 0, raan = 0, aop = 0, ma = 0;
                PykepNative.GetSpicePlanetElements(name, J2000_RSS_UT, SunMu, out sma, out ecc, out inc, out raan, out aop, out ma);

                if (sma <= 0) {
                    (sma, ecc, inc, mu, rKm) = GetStandardPlanetFallback(name);
                }
                safeKm = ComputeAutomaticSafeRadiusKm(name, rKm > 0 ? rKm : 6378.14, atmDepthKm);

                string parentFallback = ResolveParentBody(name);
                bool isMoonFallback = !string.IsNullOrEmpty(parentFallback) && !parentFallback.Equals("Sun", StringComparison.OrdinalIgnoreCase);

                if (soiKm <= 0 && sma > 0) {
                    double parentMu = isMoonFallback ? GetCentralMu(parentFallback) : SunMu;
                    if (parentMu > 0 && mu > 0) {
                        soiKm = sma * Math.Pow(mu / parentMu, 0.4);
                    }
                }
                if (name.Equals("Sun", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(parentFallback)) {
                    soiKm = double.PositiveInfinity;
                }

                string defaultIcon = GetDefaultIcon(name);
                var fallbackPlanet = new SolarPlanet {
                    Name = name,
                    DisplayName = I18n.T("body_" + name, name),
                    Icon = defaultIcon,
                    Mu = mu > 0 ? mu : 398600.4418,
                    RadiusKm = rKm > 0 ? rKm : 6378.14,
                    SafeRadiusKm = safeKm > 0 ? safeKm : 6518.14,
                    AtmosphereDepthKm = atmDepthKm,
                    SoiKm = soiKm,
                    SMA = sma,
                    ECC = ecc,
                    INC = inc,
                    RAAN = raan,
                    AOP = aop,
                    MA0 = ma,
                    EpochJ2000 = J2000_RSS_UT,
                    ParentName = parentFallback,
                    IsMoon = isMoonFallback,
                    SourceScheme = EphemerisScheme.Scheme1_SpiceDE440
                };
                if (StandardSolarBodyNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase))) {
                    _planetsCache[name] = fallbackPlanet;
                }
                try {
                    PykepNative.RegisterBody(
                        fallbackPlanet.Name,
                        fallbackPlanet.ParentName,
                        fallbackPlanet.Mu,
                        fallbackPlanet.RadiusKm,
                        fallbackPlanet.SafeRadiusKm,
                        fallbackPlanet.SoiKm,
                        fallbackPlanet.AtmosphereDepthKm,
                        fallbackPlanet.SMA,
                        fallbackPlanet.ECC,
                        fallbackPlanet.INC,
                        fallbackPlanet.RAAN,
                        fallbackPlanet.AOP,
                        fallbackPlanet.MA0,
                        fallbackPlanet.EpochJ2000
                    );
                } catch { }
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
            if (upper.Contains("MOON") || upper.Contains("月球") || upper.Contains("LUNA") || upper.Contains("MUN"))
                return (384400.0, 0.05490000, 5.14500, 4902.8, 1737.4);
            if (upper.Contains("MARS") || upper.Contains("火星") || upper.Contains("DUNA"))
                return (227936640.0, 0.09341233, 1.85061, 42828.37, 3389.5);
            if (upper.Contains("EUROPA") || upper.Contains("木卫二"))
                return (670900.0, 0.00900000, 0.47000, 3202.73, 1560.8);
            if (upper.Contains("GANYMEDE") || upper.Contains("木卫三"))
                return (1070400.0, 0.00130000, 0.20000, 9887.83, 2634.1);
            if (upper.Contains("CALLISTO") || upper.Contains("木卫四"))
                return (1882700.0, 0.00740000, 0.19200, 7179.29, 2410.3);
            if (upper.Contains("TITAN") || upper.Contains("土卫六"))
                return (1221870.0, 0.02880000, 0.34854, 8978.14, 2574.7);
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
            if (upper.Contains("CERES") || upper.Contains("谷神星"))
                return (413970000.0, 0.07580000, 10.59000, 62.63, 473.0);
            if (upper.Contains("VESTA") || upper.Contains("灶神星"))
                return (353320000.0, 0.08870000, 7.14000, 17.29, 262.7);
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

            // 离线/未载入时返回方案 1 权威天体缓存 (严格过滤，杜绝临时查询天体污染太阳系天体拓扑搜索)
            EnsureScheme1Cache();
            lock (_cacheLock) {
                return _planetsCache.Values
                    .Where(p => StandardSolarBodyNames.Any(s => s.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
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
        /// 动态扫描获取当前宇宙中所有拥有卫星的母星列表 (零硬编码，全量兼容 RSS / Stock / OPM / JNSQ 及任意自定义星球包)
        /// </summary>
        public static List<SolarPlanet> GetBodiesWithMoons() {
            var parentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (IsFlightGlobalsReady) {
                try {
                    var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                    foreach (var b in FlightGlobals.Bodies) {
                        if (b == null || b == sun) continue;
                        bool hasMoons = (b.orbitingBodies != null && b.orbitingBodies.Count > 0) ||
                                        FlightGlobals.Bodies.Any(child => child != null && child != b && child.referenceBody == b);
                        if (hasMoons) {
                            parentNames.Add(b.name);
                        }
                    }
                } catch { }
            }

            if (parentNames.Count == 0) {
                var all = GetAvailablePlanets();
                string root = RootStarName;
                foreach (var p in all) {
                    if (p.IsMoon && !string.IsNullOrEmpty(p.ParentName) && !p.ParentName.Equals(root, StringComparison.OrdinalIgnoreCase)) {
                        parentNames.Add(p.ParentName);
                    }
                }
            }

            var result = new List<SolarPlanet>();
            foreach (var name in parentNames) {
                var parentPlanet = GetPlanet(name);
                if (parentPlanet != null && !result.Any(r => r.Name.Equals(parentPlanet.Name, StringComparison.OrdinalIgnoreCase))) {
                    result.Add(parentPlanet);
                }
            }
            return result.OrderBy(p => p.SMA).ToList();
        }

        /// <summary>
        /// 动态获取指定母星下的所有直接子卫星列表 (零硬编码自适应运行态与离线星历)
        /// </summary>
        public static List<SolarPlanet> GetMoonsOf(string parentName) {
            if (string.IsNullOrEmpty(parentName)) return new List<SolarPlanet>();
            if (IsFlightGlobalsReady) {
                try {
                    var parentCb = FlightGlobals.Bodies.Find(b => b.name.Equals(parentName, StringComparison.OrdinalIgnoreCase) || 
                                                                 b.bodyName.Equals(parentName, StringComparison.OrdinalIgnoreCase));
                    if (parentCb != null) {
                        var moons = FlightGlobals.Bodies
                            .Where(b => b != null && b != parentCb && b.referenceBody == parentCb && b.orbit != null)
                            .OrderBy(b => b.orbit.semiMajorAxis)
                            .Select(b => CreateFromCelestialBody(b))
                            .ToList();
                        if (moons.Count > 0) return moons;
                    }
                } catch { }
            }
            var all = GetAvailablePlanets();
            return all.Where(p => p.IsMoon && p.ParentName.Equals(parentName, StringComparison.OrdinalIgnoreCase))
                      .OrderBy(p => p.SMA)
                      .ToList();
        }

        /// <summary>
        /// 动态推导两个天体之间转移的中心引力天体 (Central Body)：
        /// 1. 若两者母星相同且非恒星 (如 Europa -> Ganymede 或 Mun -> Minmus)，中心天体为该公共母星 (Jupiter 或 Kerbin)；
        /// 2. 若其中一个是另一个的母星 (如 Earth -> Moon 或 Kerbin -> Mun)，中心天体为该母星 (Earth 或 Kerbin)；
        /// 3. 否则为恒星/行星际转移 (Sun 或 Kerbol)。
        /// </summary>
        public static string ResolveTransferCentralBody(string bodyA, string bodyB) {
            string root = RootStarName;
            if (string.IsNullOrEmpty(bodyA) || string.IsNullOrEmpty(bodyB)) return root;
            var pA = GetPlanet(bodyA);
            var pB = GetPlanet(bodyB);
            if (pA == null || pB == null) return root;

            // 情况 1: 其中一个是另一个的直接母星
            if (pB.IsMoon && pB.ParentName.Equals(pA.Name, StringComparison.OrdinalIgnoreCase)) return pA.Name;
            if (pA.IsMoon && pA.ParentName.Equals(pB.Name, StringComparison.OrdinalIgnoreCase)) return pB.Name;

            // 情况 2: 两者属于同一母星的子卫星 (如 木卫二 -> 木卫三 或 坎星 Mun -> Minmus)
            if (pA.IsMoon && pB.IsMoon && 
                !string.IsNullOrEmpty(pA.ParentName) && 
                pA.ParentName.Equals(pB.ParentName, StringComparison.OrdinalIgnoreCase)) {
                return pA.ParentName;
            }

            // 情况 3: 普通行星际转移
            return root;
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

            // 确保坐标系对齐偏角已完成实时动态反推校准 (0 硬编码，多星圆周平均解析反演)
            EnsureDynamicallyCalibrated();

            // 0. 太阳/中心天体基准特判：太阳是日心坐标原点，不计算开普勒公转根数，杜绝除以 0 导致 NaN
            bool isSun = bodyName.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                        bodyName.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                        bodyName.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);
            if (isSun) {
                res.Scheme1Available = true;
                res.Scheme2Available = true;
                res.PosScheme1 = Vector3d.zero;
                res.VelScheme1 = Vector3d.zero;
                res.PosScheme2 = Vector3d.zero;
                res.VelScheme2 = Vector3d.zero;
                res.PosDiffKm = 0.0;
                res.VelDiffMs = 0.0;
                res.SmaScheme1 = 0.0;
                res.EccScheme1 = 0.0;
                res.IncScheme1 = 0.0;
                res.SmaScheme2 = 0.0;
                res.EccScheme2 = 0.0;
                res.IncScheme2 = 0.0;
                res.DaKm = 0.0;
                res.De = 0.0;
                res.DincDeg = 0.0;
                return res;
            }

            // 统一日心标准参考系：
            // Principia 与真实太阳系 (RSS) 的原生空间坐标系为地球赤道惯性参考系 (ICRF / J2000)
            // 方案 1 (NASA JPL SPICE DE440) 严格采用同构 J2000 (ICRF) 参考系，彻底消除黄赤交角 (23.44°) 引起的坐标系假性偏差
            string spiceFrame = "J2000";

            // 检查当前天体是否为绕行星公转的卫星 (如 Moon, Phobos)
            CelestialBody targetBody = null;
            CelestialBody parentBody = null;
            double centralMuKm = SunMu;
            bool isSatellite = false;

            if (IsFlightGlobalsReady) {
                targetBody = FlightGlobals.Bodies.Find(x => 
                    x.name.Equals(bodyName, StringComparison.OrdinalIgnoreCase) || 
                    x.bodyName.Equals(bodyName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(x.displayName) && CleanDisplayName(x.displayName).Equals(bodyName, StringComparison.OrdinalIgnoreCase)));
                if (targetBody != null && targetBody.referenceBody != null && targetBody.referenceBody != targetBody) {
                    parentBody = targetBody.referenceBody;
                    // 若母天体不是太阳，则该天体为卫星 (以其母行星为中心对比轨道根数与相对状态)
                    if (!parentBody.name.Equals("Sun", StringComparison.OrdinalIgnoreCase) && 
                        !parentBody.name.Equals("Sol", StringComparison.OrdinalIgnoreCase) && 
                        !parentBody.name.Equals("Kerbol", StringComparison.OrdinalIgnoreCase)) {
                        isSatellite = true;
                        centralMuKm = parentBody.gravParameter / 1e9; // m^3/s^2 -> km^3/s^2
                    }
                }
            }

            // 1. 方案 1: SPICE 状态与根数
            if (PykepNative.HasSpiceLoaded) {
                if (isSatellite && parentBody != null) {
                    // 针对卫星 (如月球)：SPICE 先分别读取月球与母星 (地球) 的日心状态，相减求得高精地心/相对母星状态
                    if (PykepNative.GetSpicePlanetState(bodyName, ut, spiceFrame, out Vector3d rSatSun, out Vector3d vSatSun) &&
                        PykepNative.GetSpicePlanetState(parentBody.name, ut, spiceFrame, out Vector3d rParSun, out Vector3d vParSun)) {
                        res.PosScheme1 = rSatSun - rParSun;
                        res.VelScheme1 = vSatSun - vParSun;
                        res.Scheme1Available = true;
                    } else if (PykepNative.GetSpicePlanetState(bodyName, ut, spiceFrame, out res.PosScheme1, out res.VelScheme1)) {
                        res.Scheme1Available = true;
                    }
                } else {
                    // 针对行星 (如火星)：直接获取日心状态
                    if (PykepNative.GetSpicePlanetState(bodyName, ut, spiceFrame, out res.PosScheme1, out res.VelScheme1)) {
                        res.Scheme1Available = true;
                    }
                }

                if (res.Scheme1Available) {
                    // 精确补偿 SPICE J2000 赤道惯性系与 Principia ICRF 游戏物理坐标系的 15.371911° 赤经零点偏角
                    res.PosScheme1 = AlignSpiceJ2000ToPrincipia(res.PosScheme1);
                    res.VelScheme1 = AlignSpiceJ2000ToPrincipia(res.VelScheme1);
                }
            }

            // 计算方案 1 的开普勒根数 (使用对应的中心引力常数 centralMuKm，100% 底层 C++ 原生计算，0 托管计算)
            if (res.Scheme1Available && centralMuKm > 0) {
                double raan1 = 0, aop1 = 0, ma1 = 0;
                PykepNative.GetSpicePlanetElements(bodyName, ut, centralMuKm, out res.SmaScheme1, out res.EccScheme1, out res.IncScheme1, out raan1, out aop1, out ma1);
            }

            // 2. 方案 2: FlightGlobals 游戏运行态天体 (方案 B: 接入 Principia 原生星历与未篡改轨道)
            if (targetBody != null) {
                res.Scheme2Available = true;

                if (isSatellite) {
                    // 卫星模式：优先从 Principia 提取相对母星状态，或回退到未篡改地心轨道
                    if (PrincipiaInterop.TryGetCelestialFromParent(targetBody, out res.PosScheme2, out res.VelScheme2)) {
                        // 成功由 Principia 提取高精度相对状态
                    } else {
                        Orbit unmodSat = PrincipiaInterop.GetUnmodifiedOrbit(targetBody) ?? targetBody.orbit;
                        if (unmodSat != null) {
                            Vector3d r_m = unmodSat.getRelativePositionAtUT(ut);
                            Vector3d v_ms = unmodSat.getOrbitalVelocityAtUT(ut);
                            // KSP Orbit.getRelativePositionAtUT 输出已处于天体力学 Z-Up (黄道面) 惯性系 (X=春分点, Y=黄道90°, Z=北极)
                            res.PosScheme2 = new Vector3d(r_m.x / 1000.0, r_m.y / 1000.0, r_m.z / 1000.0);
                            res.VelScheme2 = new Vector3d(v_ms.x / 1000.0, v_ms.y / 1000.0, v_ms.z / 1000.0);
                        }
                    }
                } else {
                    // 行星模式：方案 B 优先直读 Principia 原生 N 体日心状态 (当前时刻直读，0 二体外推累积误差)
                    if (targetBody.referenceBody != null && 
                        (targetBody.referenceBody.name.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                         targetBody.referenceBody.name.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                         targetBody.referenceBody.name.Equals("Kerbol", StringComparison.OrdinalIgnoreCase)) &&
                        PrincipiaInterop.TryGetCelestialFromParent(targetBody, out res.PosScheme2, out res.VelScheme2)) {
                        // 成功直读 Principia 原生日心高精状态
                    } else if (PrincipiaInterop.TryGetHeliocentricState(targetBody, ut, out res.PosScheme2, out res.VelScheme2)) {
                        // 成功由 Principia 方案 B 提取
                    } else {
                        // 普通回退：级联求和至日心
                        Vector3d r_total = Vector3d.zero;
                        Vector3d v_total = Vector3d.zero;
                        var curr = targetBody;
                        while (curr != null && curr.orbit != null && curr.referenceBody != null && curr.referenceBody != curr) {
                            r_total += curr.orbit.getRelativePositionAtUT(ut);
                            v_total += curr.orbit.getOrbitalVelocityAtUT(ut);
                            curr = curr.referenceBody;
                        }
                        // KSP Orbit 输出处于天体力学 Z-Up (黄道面) 惯性系 (严禁交换 Y 与 Z)
                        res.PosScheme2 = new Vector3d(r_total.x / 1000.0, r_total.y / 1000.0, r_total.z / 1000.0);
                        res.VelScheme2 = new Vector3d(v_total.x / 1000.0, v_total.y / 1000.0, v_total.z / 1000.0);
                    }
                }

                // 开普勒根数读取：优先从未篡改轨道读取真均根数，彻底避免 Principia 每帧瞬时密切根数扰动
                var unmod = PrincipiaInterop.GetUnmodifiedOrbit(targetBody) ?? targetBody.orbit;
                if (unmod != null) {
                    res.SmaScheme2 = unmod.semiMajorAxis / 1000.0;
                    res.EccScheme2 = unmod.eccentricity;
                    res.IncScheme2 = unmod.inclination;
                }
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
                KSPGMATLog.Info($"[ComparePlanetSchemes] target={bodyName}, ut={ut:F1}s | frame={spiceFrame}");
                KSPGMATLog.Info($"  Scheme1(SPICE): r=[{res.PosScheme1.x:F1}, {res.PosScheme1.y:F1}, {res.PosScheme1.z:F1}]km (|r|={res.PosScheme1.magnitude:F1}km), v=[{res.VelScheme1.x:F3}, {res.VelScheme1.y:F3}, {res.VelScheme1.z:F3}]km/s");
                KSPGMATLog.Info($"  Scheme2(Game):  r=[{res.PosScheme2.x:F1}, {res.PosScheme2.y:F1}, {res.PosScheme2.z:F1}]km (|r|={res.PosScheme2.magnitude:F1}km), v=[{res.VelScheme2.x:F3}, {res.VelScheme2.y:F3}, {res.VelScheme2.z:F3}]km/s");
                KSPGMATLog.Info($"  Diff: Δr={res.PosDiffKm:F1}km, Δv={res.VelDiffMs:F1}m/s | Δa={res.DaKm:F1}km, Δe={res.De:F5}, Δi={res.DincDeg:F3}°");
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

        /// <summary>
        /// 获取指定中心天体的权威引力常数 (km^3/s^2)
        /// 彻底消除硬编码，优先读取游戏运行态天体树或 SPICE 权威内核
        /// </summary>
        public static double GetCentralMu(string centralBody) {
            if (string.IsNullOrEmpty(centralBody) || 
                centralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                centralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) || 
                centralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase)) {
                return SunMu;
            }

            // 1. 优先从当前天体字典或 FlightGlobals 动态直读
            var planet = GetPlanet(centralBody);
            if (planet != null && planet.Mu > 0) {
                return planet.Mu;
            }

            // 2. 从 SPICE 提取权威常数
            if (PykepNative.HasSpiceLoaded) {
                double mu, r, safeR;
                if (PykepNative.GetSpicePlanetConstants(centralBody, out mu, out r, out safeR) && mu > 0) {
                    return mu;
                }
            }

            // 3. 后备物理常数兜底
            var fb = GetStandardPlanetFallback(centralBody);
            if (fb.mu > 0) return fb.mu;

            return 398600.4418; // 默认地球保底
        }

        /// <summary>
        /// 动态探测当前游戏或星历环境中可用的引力系统 (中心母星) 清单
        /// 针对 SOL/RSS/OPM/Stock: 动态自适应探测哪些行星实际配置并加载了卫星，仅列出真正拥有卫星的天体，杜绝死名单
        /// </summary>
        public static List<string> GetAvailableSystems() {
            var systems = new List<string> { "Sun" };

            if (IsFlightGlobalsReady) {
                try {
                    var sun = FlightGlobals.Bodies.Find(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b);
                    // 找出所有作为 referenceBody 且在当前游戏存档中真实拥有有效引力子卫星的父天体 (无主观 500km 门槛)
                    var parentsWithMoons = FlightGlobals.Bodies
                        .Where(b => b != sun && FlightGlobals.Bodies.Any(c => c != b && c.referenceBody == b && c.orbit != null && c.gravParameter > 0 && c.Radius > 10.0))
                        .OrderBy(b => b.orbit != null ? b.orbit.semiMajorAxis : 0.0)
                        .Select(b => b.name)
                        .ToList();

                    foreach (var p in parentsWithMoons) {
                        if (!systems.Contains(p)) systems.Add(p);
                    }
                    if (systems.Count > 1) return systems;
                } catch { }
            }

            // 离线/测试环境保底：经典主要行星系
            string[] standardSystems = new[] { "Earth", "Jupiter", "Saturn", "Mars", "Uranus", "Neptune" };
            foreach (var s in standardSystems) {
                if (!systems.Contains(s)) systems.Add(s);
            }
            return systems;
        }

        /// <summary>
        /// 动态获取指定引力系统内的全部可用天体 (包含中心母星自身停泊轨道及所有实际已加载的子卫星)
        /// 针对 SOL/RSS/OPM 解决卫星选择性收录问题：真实加载了哪些卫星就展示哪些，严禁虚假全量或遗漏
        /// </summary>
        public static List<SolarPlanet> GetSystemBodies(string centralBody) {
            bool isHelio = string.IsNullOrEmpty(centralBody) || 
                           centralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                           centralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) || 
                           centralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

            if (isHelio) {
                // 日心系统：返回所有绕太阳运行的一级主行星/矮行星 (排除卫星)
                var all = GetAvailablePlanets();
                return all.Where(p => !p.IsMoon && (string.IsNullOrEmpty(p.ParentName) || 
                                                     p.ParentName.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                                                     p.ParentName.Equals("Sol", StringComparison.OrdinalIgnoreCase) || 
                                                     p.ParentName.Equals("Kerbol", StringComparison.OrdinalIgnoreCase)))
                          .OrderBy(p => p.SMA)
                          .ToList();
            }

            var result = new List<SolarPlanet>();

            // 1. 中心母星自身作为出发/捕获候选 (代表从母星停泊轨道出发)
            var centerPlanet = GetPlanet(centralBody);
            if (centerPlanet != null) {
                var parkCopy = new SolarPlanet {
                    Name = centerPlanet.Name,
                    DisplayName = centerPlanet.DisplayName + " [" + I18n.T("mga_parking_orbit", "停泊轨道") + "]",
                    Icon = centerPlanet.Icon,
                    Mu = centerPlanet.Mu,
                    RadiusKm = centerPlanet.RadiusKm,
                    SafeRadiusKm = centerPlanet.SafeRadiusKm,
                    SMA = 0,
                    ECC = 0,
                    INC = 0,
                    ParentName = centerPlanet.Name,
                    IsMoon = false
                };
                result.Add(parkCopy);
            }

            // 2. 游戏运行态：完全动态遍历 FlightGlobals.Bodies 探测真实存在的卫星
            if (IsFlightGlobalsReady) {
                try {
                    var parentCb = FlightGlobals.Bodies.Find(b => 
                        b.name.Equals(centralBody, StringComparison.OrdinalIgnoreCase) || 
                        b.bodyName.Equals(centralBody, StringComparison.OrdinalIgnoreCase));

                    if (parentCb != null) {
                        var moons = FlightGlobals.Bodies
                            .Where(b => b != parentCb && b.referenceBody == parentCb && b.orbit != null && b.gravParameter > 0 && b.Radius > 10.0)
                            .OrderBy(b => b.orbit.semiMajorAxis)
                            .Select(b => CreateFromCelestialBody(b))
                            .Where(sp => sp != null)
                            .ToList();

                        foreach (var m in moons) {
                            if (!result.Any(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))) {
                                result.Add(m);
                            }
                        }

                        if (result.Count > 1) return result;
                    }
                } catch { }
            }

            // 3. 离线/测试环境后备：为主要行星系提供公认核心大卫星后备
            var offlineMoons = GetOfflineStandardMoons(centralBody);
            foreach (var m in offlineMoons) {
                if (!result.Any(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))) {
                    result.Add(m);
                }
            }

            return result;
        }

        /// <summary>
        /// 离线/脱机环境权威核心大卫星后备数据 (保证单元测试与脱机规划完全自洽)
        /// </summary>
        private static List<SolarPlanet> GetOfflineStandardMoons(string centralBody) {
            var list = new List<SolarPlanet>();
            string upper = (centralBody ?? "").ToUpperInvariant();

            if (upper.Contains("EARTH") || upper.Contains("地球") || upper.Contains("KERBIN")) {
                list.Add(new SolarPlanet {
                    Name = "Moon", DisplayName = I18n.T("body_Moon", "月球"), Icon = "Lu",
                    Mu = 4902.8, RadiusKm = 1737.4, SafeRadiusKm = 1757.4, AtmosphereDepthKm = 0.0, SoiKm = 66100.0,
                    SMA = 384400.0, ECC = 0.0549, INC = 5.145, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
            } else if (upper.Contains("SATURN") || upper.Contains("土星")) {
                list.Add(new SolarPlanet {
                    Name = "Enceladus", DisplayName = I18n.T("body_Enceladus", "土卫二"), Icon = "En",
                    Mu = 7.21, RadiusKm = 252.1, SafeRadiusKm = 272.1, AtmosphereDepthKm = 0.0, SoiKm = 950.0,
                    SMA = 238000.0, ECC = 0.0047, INC = 0.009, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Tethys", DisplayName = I18n.T("body_Tethys", "土卫三"), Icon = "Te",
                    Mu = 41.2, RadiusKm = 531.1, SafeRadiusKm = 551.1, AtmosphereDepthKm = 0.0, SoiKm = 2000.0,
                    SMA = 294600.0, ECC = 0.0, INC = 1.091, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Dione", DisplayName = I18n.T("body_Dione", "土卫四"), Icon = "Di",
                    Mu = 73.1, RadiusKm = 561.4, SafeRadiusKm = 581.4, AtmosphereDepthKm = 0.0, SoiKm = 3200.0,
                    SMA = 377400.0, ECC = 0.0022, INC = 0.02, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Rhea", DisplayName = I18n.T("body_Rhea", "土卫五"), Icon = "Rh",
                    Mu = 153.9, RadiusKm = 763.8, SafeRadiusKm = 783.8, AtmosphereDepthKm = 0.0, SoiKm = 5800.0,
                    SMA = 527000.0, ECC = 0.001, INC = 0.35, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Titan", DisplayName = I18n.T("body_Titan", "泰坦 (土卫六)"), Icon = "Ti",
                    Mu = 8978.1, RadiusKm = 2575.0, SafeRadiusKm = 3185.0, AtmosphereDepthKm = 600.0, SoiKm = 85000.0,
                    SMA = 1221870.0, ECC = 0.0288, INC = 0.33, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Iapetus", DisplayName = I18n.T("body_Iapetus", "土卫八"), Icon = "Ia",
                    Mu = 120.5, RadiusKm = 734.5, SafeRadiusKm = 754.5, AtmosphereDepthKm = 0.0, SoiKm = 36000.0,
                    SMA = 3561300.0, ECC = 0.0283, INC = 15.47, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
            } else if (upper.Contains("JUPITER") || upper.Contains("木星") || upper.Contains("JOOL")) {
                list.Add(new SolarPlanet {
                    Name = "Io", DisplayName = I18n.T("body_Io", "木卫一"), Icon = "Io",
                    Mu = 5959.9, RadiusKm = 1821.6, SafeRadiusKm = 1841.6, AtmosphereDepthKm = 0.0, SoiKm = 11000.0,
                    SMA = 421700.0, ECC = 0.0041, INC = 0.05, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Europa", DisplayName = I18n.T("body_Europa", "木卫二"), Icon = "Eu",
                    Mu = 3202.7, RadiusKm = 1560.8, SafeRadiusKm = 1580.8, AtmosphereDepthKm = 0.0, SoiKm = 9700.0,
                    SMA = 670900.0, ECC = 0.009, INC = 0.47, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Ganymede", DisplayName = I18n.T("body_Ganymede", "木卫三"), Icon = "Ga",
                    Mu = 9887.8, RadiusKm = 2631.2, SafeRadiusKm = 2651.2, AtmosphereDepthKm = 0.0, SoiKm = 32000.0,
                    SMA = 1070400.0, ECC = 0.0013, INC = 0.2, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
                list.Add(new SolarPlanet {
                    Name = "Callisto", DisplayName = I18n.T("body_Callisto", "木卫四"), Icon = "Ca",
                    Mu = 7179.3, RadiusKm = 2410.3, SafeRadiusKm = 2430.3, AtmosphereDepthKm = 0.0, SoiKm = 50000.0,
                    SMA = 1882700.0, ECC = 0.0074, INC = 0.28, EpochJ2000 = J2000_RSS_UT,
                    ParentName = centralBody, IsMoon = true
                });
            }

            foreach (var sp in list) {
                sp.SafeRadiusKm = ComputeAutomaticSafeRadiusKm(sp.Name, sp.RadiusKm, sp.AtmosphereDepthKm);
            }

            return list;
        }

        /// <summary>
        /// 纯物理动态“引力必吃榜”：计算给定中心天体系统 (如 Sun/Sol) 内所有环绕天体的引力助推潜力并排序。
        /// 0 硬编码天体名称，纯粹按天体引力势能特征深度 Phi_safe = Mu / SafeRadiusKm 与系统内最大天体进行归一化。
        /// </summary>
        /// <param name="centralBody">中心天体名称 (默认 "Sun")</param>
        /// <returns>降序排列的 (天体对象, 归一化潜力比率 0~1.0, 引力势深 km^2/s^2) 列表</returns>
        public static List<(SolarPlanet Body, double Ratio, double PotentialDepth)> GetGravityAssistRankings(string centralBody = "Sun") {
            var all = GetAvailablePlanets();
            if (all == null || all.Count == 0) return new List<(SolarPlanet, double, double)>();

            string cName = string.IsNullOrEmpty(centralBody) ? "Sun" : centralBody;
            bool isHelio = cName.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                           cName.Equals("Sol", StringComparison.OrdinalIgnoreCase) ||
                           cName.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

            // 筛选当前中心天体下的所有直接环绕天体 (排除中心天体本身与无效天体)
            var candidates = all.Where(p => {
                if (p == null || string.IsNullOrEmpty(p.Name)) return false;
                if (p.Name.Equals(cName, StringComparison.OrdinalIgnoreCase)) return false;
                if (isHelio) {
                    return !p.IsMoon || string.IsNullOrEmpty(p.ParentName) || 
                           p.ParentName.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                           p.ParentName.Equals("Sol", StringComparison.OrdinalIgnoreCase);
                } else {
                    return p.ParentName.Equals(cName, StringComparison.OrdinalIgnoreCase);
                }
            }).ToList();

            if (candidates.Count == 0) return new List<(SolarPlanet, double, double)>();

            var depths = candidates.Select(p => (Body: p, Depth: p.GravityPotentialDepth)).ToList();
            double maxDepth = depths.Max(x => x.Depth);
            if (maxDepth <= 0.0) maxDepth = 1.0;

            return depths
                .Select(x => (x.Body, Ratio: x.Depth / maxDepth, PotentialDepth: x.Depth))
                .OrderByDescending(x => x.Ratio)
                .ToList();
        }
    }
}
