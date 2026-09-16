using System;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 统一星历与时间系统中枢 (Unified Ephemeris & Time System Hub)
    /// 
    /// 集中管理 KSP 运行时、PyKEP 原生引擎、NASA GMAT DE440 后端三者共用的
    /// 全部天文时间常数与历元转换函数，彻底消灭分散在各处文件中的魔法常数硬编码。
    ///
    /// 时间链路总览:
    ///   KSP UT (seconds since 1951-01-01 00:00:00 UTC, JD 2433647.5)
    ///     ↕ UtToJd / JdToUt
    ///   Julian Date (JD)
    ///     ↕ JdToMjd / MjdToJd
    ///   GMAT Modified Julian Date (MJD = JD - 2430000.0)
    ///     ↕ JdToGregorian / GregorianToJd
    ///   UTC Gregorian String ("01 Jan 2000 12:00:00.000")
    ///
    /// 星历精度层级:
    ///   Layer 1 — MGA 全局搜索: 开普勒平根数 + PyKEP 二体推演 (快速, 亚秒级)
    ///   Layer 2 — KSP 运行时:   RSS 模组轨道根数 + KSP 二体推演 (实时, 游戏内)
    ///   Layer 3 — GMAT 精化:    DE440 SPICE 数值星历 + N体摄动积分 (最高精度)
    /// </summary>
    public static class EphemerisTimeSystem {
        // =====================================================================
        // 基本天文时间常数 (Fundamental Astronomical Time Constants)
        // =====================================================================

        /// <summary>
        /// KSP / RSS 游戏 UT = 0 对应的儒略日
        /// (1951-01-01 00:00:00 UTC = JD 2433647.5)
        /// </summary>
        public const double KSP_EPOCH_JD = 2433647.5;

        /// <summary>
        /// J2000.0 标准天文历元对应的儒略日
        /// (2000-01-01 12:00:00.000 TDB = JD 2451545.0)
        /// </summary>
        public const double J2000_JD = 2451545.0;

        /// <summary>
        /// J2000.0 历元在 KSP UT 时间轴中的秒数
        /// = (J2000_JD - KSP_EPOCH_JD) × 86400
        /// = (2451545.0 - 2433647.5) × 86400
        /// = 17897.5 × 86400
        /// = 1546344000.0 秒
        /// </summary>
        public const double J2000_UT = (J2000_JD - KSP_EPOCH_JD) * 86400.0; // 1546344000.0

        /// <summary>
        /// GMAT 修正儒略日偏移量 (MJD = JD - GMAT_MJD_OFFSET)
        /// </summary>
        public const double GMAT_MJD_OFFSET = 2430000.0;

        /// <summary>
        /// GMAT C++ 引擎硬性最低可用历元 (1957-10-04 Sputnik, JD 2436116.0)
        /// UTCGregorian 格式必须 >= 此日期，否则 GMAT 解析器拒绝执行
        /// </summary>
        public const double GMAT_MIN_EPOCH_JD = 2436116.0;

        /// <summary>
        /// 一天的秒数 (86400.0)
        /// </summary>
        public const double SECONDS_PER_DAY = 86400.0;

        // =====================================================================
        // KSP UT ↔ Julian Date 转换
        // =====================================================================

        /// <summary>
        /// KSP Universal Time (秒) → 儒略日
        /// </summary>
        public static double UtToJd(double ut) {
            return KSP_EPOCH_JD + (ut / SECONDS_PER_DAY);
        }

        /// <summary>
        /// 儒略日 → KSP Universal Time (秒)
        /// </summary>
        public static double JdToUt(double jd) {
            return (jd - KSP_EPOCH_JD) * SECONDS_PER_DAY;
        }

        // =====================================================================
        // KSP UT ↔ GMAT Modified Julian Date 转换
        // =====================================================================

        /// <summary>
        /// KSP UT → GMAT Modified Julian Date
        /// </summary>
        public static double UtToMjd(double ut) {
            return UtToJd(ut) - GMAT_MJD_OFFSET;
        }

        /// <summary>
        /// GMAT Modified Julian Date → KSP UT
        /// </summary>
        public static double MjdToUt(double mjd) {
            return JdToUt(mjd + GMAT_MJD_OFFSET);
        }

        /// <summary>
        /// 儒略日 → GMAT Modified Julian Date
        /// </summary>
        public static double JdToMjd(double jd) {
            return jd - GMAT_MJD_OFFSET;
        }

        /// <summary>
        /// GMAT Modified Julian Date → 儒略日
        /// </summary>
        public static double MjdToJd(double mjd) {
            return mjd + GMAT_MJD_OFFSET;
        }

        // =====================================================================
        // KSP UT → GMAT 安全历元 (1957 前自动映射)
        // =====================================================================

        /// <summary>
        /// KSP UT → GMAT 安全儒略日
        /// 针对 1957 年前的早期历史存档 (如 RP-1 1951 开局)，自动安全映射
        /// </summary>
        public static double UtToGmatSafeJd(double ut) {
            double jd = UtToJd(ut);
            if (jd < GMAT_MIN_EPOCH_JD) {
                double safeBaseJd = 2437000.0; // 07 Mar 1960
                double offset = (jd > GMAT_MJD_OFFSET) ? (jd - KSP_EPOCH_JD) : jd;
                jd = safeBaseJd + offset;
            }
            return jd;
        }

        // =====================================================================
        // 星历源识别与诊断
        // =====================================================================

        /// <summary>
        /// 当前 MGA 求解器使用的行星星历源描述
        /// </summary>
        public static string GetCurrentEphemerisSourceDescription() {
            bool spiceLoaded = false;
            string spiceInfo = "";
            try {
                spiceLoaded = PykepNative.HasSpiceLoaded;
                if (spiceLoaded) {
                    spiceInfo = $"PyKEP SPICE Active ({System.IO.Path.GetFileName(PykepNative.LoadedSpicePath)})";
                }
            } catch { }

            bool kspReady = false;
            string kspInfo = "";
            try {
                kspReady = FlightGlobals.ready && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0;
                if (kspReady) {
                    kspInfo = $"KSP Runtime ({FlightGlobals.Bodies.Count} bodies)";
                }
            } catch { }

            bool nativeReady = PykepNative.IsAvailable;
            string nativeInfo = nativeReady ? "PyKEP C++ Native (Keplerian J2000)" : "PyKEP Unavailable";

            bool gmatSpiceReady = false;
            string gmatSpiceInfo = "";
            try {
                gmatSpiceReady = GMATClient.HasFullSpiceEphemeris;
                if (gmatSpiceReady) {
                    gmatSpiceInfo = $"GMAT SPICE ({GMATClient.DetectedSpiceKernelName})";
                }
            } catch { }

            string primary = spiceLoaded ? spiceInfo : (kspReady ? kspInfo : (nativeReady ? nativeInfo : "Offline Fallback"));
            string gmatLayer = gmatSpiceReady ? gmatSpiceInfo : "GMAT SPICE Not Available";
            return $"MGA Ephemeris: {primary} | Keplerian Fallback: {nativeInfo} | GMAT Backend: {gmatLayer}";
        }
    }
}
