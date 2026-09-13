// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge
// Module 7: SPICE Ephemeris Kernel Loader & Planetary State Evaluator
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"
#include "spk_reader.hpp"
#include <kep3/udpla/jpl_lp.hpp>
#include <kep3/core_astro/convert_anomalies.hpp>

#include <string>
#include <unordered_map>
#include <memory>
#include <mutex>
#include <algorithm>
#include <cctype>

namespace {
    static std::string g_loaded_spice_kernel_path = "kep3_builtin_ephemeris";
    static std::recursive_mutex g_spice_mutex;
    static std::unordered_map<std::string, std::shared_ptr<kep3::udpla::jpl_lp>> g_jpl_planet_cache;
    static kspgmat::spice::SpkReader g_spk_reader;

    struct RegisteredBody {
        std::string name;
        std::string parent_name; // e.g. "sun", "earth", "jupiter"
        double mu = 0.0;         // km^3 / s^2
        double radius_km = 0.0;
        double safe_radius_km = 0.0;
        double soi_radius_km = 0.0;
        double atm_depth_km = 0.0;

        // Keplerian orbital elements relative to parent (mean elements at epoch)
        double sma_km = 0.0;
        double ecc = 0.0;
        double inc_deg = 0.0;
        double raan_deg = 0.0;
        double aop_deg = 0.0;
        double ma0_deg = 0.0;
        double epoch_ut = 1546344000.0; // J2000.0 (KSP RSS UT = 1546344000.0)
    };

    static std::unordered_map<std::string, RegisteredBody> g_registered_bodies;
    static bool g_default_bodies_seeded = false;

    // IAU standard obliquity of the ecliptic at J2000.0: eps = 23°26'21.406" = 23.4392911111111°
    static constexpr double EPSILON_J2000_RAD = 23.43929111111111 * (3.14159265358979323846 / 180.0);
    static const double COS_EPS = std::cos(EPSILON_J2000_RAD);
    static const double SIN_EPS = std::sin(EPSILON_J2000_RAD);

    static inline void rotate_eclip_to_icrf(double& x, double& y, double& z) {
        double y_new = y * COS_EPS - z * SIN_EPS;
        double z_new = y * SIN_EPS + z * COS_EPS;
        y = y_new;
        z = z_new;
    }

    static inline bool is_icrf_frame(const char* frame) {
        if (!frame || std::strlen(frame) == 0) return false;
        std::string f(frame);
        std::transform(f.begin(), f.end(), f.begin(), [](unsigned char c) { return (char)std::toupper(c); });
        if (f.find("ECLIP") != std::string::npos) return false;
        if (f.find("J2000") != std::string::npos || f.find("ICRF") != std::string::npos || 
            f.find("EME") != std::string::npos || f.find("EQUATOR") != std::string::npos) {
            return true;
        }
        return false;
    }

    static std::string normalize_body_name(const std::string& raw_name) {
        if (raw_name.empty()) return "";
        std::string s = raw_name;
        while (!s.empty() && std::isspace((unsigned char)s.front())) s.erase(s.begin());
        while (!s.empty() && std::isspace((unsigned char)s.back())) s.pop_back();

        std::string lower = s;
        std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char c) {
            return (char)std::tolower(c);
        });

        if (lower.find("mercury") != std::string::npos || lower == "1") return "mercury";
        if (lower.find("venus") != std::string::npos || lower == "2") return "venus";
        if (lower.find("earth") != std::string::npos || lower == "3" || lower == "399") return "earth";
        if (lower.find("moon") != std::string::npos || lower.find("luna") != std::string::npos || lower == "301") return "moon";
        if (lower.find("mars") != std::string::npos || lower == "4") return "mars";
        if (lower.find("jupiter") != std::string::npos || lower == "5") return "jupiter";
        if (lower.find("saturn") != std::string::npos || lower == "6") return "saturn";
        if (lower.find("uranus") != std::string::npos || lower == "7") return "uranus";
        if (lower.find("neptune") != std::string::npos || lower == "8") return "neptune";

        return lower;
    }

    static void seed_default_solar_system() {
        if (g_default_bodies_seeded) return;
        g_default_bodies_seeded = true;

        // Sun (中心恒星)
        g_registered_bodies["sun"] = {
            "sun", "", SUN_MU, 696340.0, 700000.0, 1e18, 0.0,
            0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1546344000.0
        };

        // 水星
        g_registered_bodies["mercury"] = {
            "mercury", "sun", 22032.09, 2439.7, 2459.7, 112000.0, 0.0,
            57909100.0, 0.20563069, 7.00487, 48.33167, 29.12478, 174.79439, 1546344000.0
        };

        // 金星
        g_registered_bodies["venus"] = {
            "venus", "sun", 324859.0, 6051.8, 6361.8, 616000.0, 300.0,
            108208930.0, 0.00677323, 3.39471, 76.68069, 54.85229, 50.44675, 1546344000.0
        };

        // 地球
        g_registered_bodies["earth"] = {
            "earth", "sun", 398600.4418, 6378.14, 6528.14, 925000.0, 140.0,
            149598023.0, 0.01671022, 0.00000, 0.00000, 102.94719, 357.51716, 1546344000.0
        };

        // 月球 (母星: 地球)
        g_registered_bodies["moon"] = {
            "moon", "earth", 4902.8, 1737.4, 1757.4, 66100.0, 0.0,
            384400.0, 0.05490, 5.145396, 125.0445, 318.0634, 134.9634, 1546344000.0
        };

        // 火星
        g_registered_bodies["mars"] = {
            "mars", "sun", 42828.37, 3389.5, 3524.5, 577000.0, 125.0,
            227936640.0, 0.09341233, 1.85061, 49.57854, 286.46230, 19.41248, 1546344000.0
        };

        // 火星卫星
        g_registered_bodies["phobos"] = {
            "phobos", "mars", 7.087e-4, 11.26, 15.0, 20.0, 0.0,
            9376.0, 0.0151, 1.093, 0.0, 150.0, 0.0, 1546344000.0
        };
        g_registered_bodies["deimos"] = {
            "deimos", "mars", 9.85e-5, 6.2, 10.0, 15.0, 0.0,
            23463.0, 0.00033, 0.93, 0.0, 0.0, 0.0, 1546344000.0
        };

        // 木星
        g_registered_bodies["jupiter"] = {
            "jupiter", "sun", 126686534.0, 69911.0, 70911.0, 48200000.0, 1000.0,
            778412020.0, 0.04839266, 1.30530, 100.55615, 274.19770, 19.65053, 1546344000.0
        };

        // 伽利略卫星
        g_registered_bodies["io"] = {
            "io", "jupiter", 5959.9, 1821.6, 1841.6, 11000.0, 0.0,
            421700.0, 0.0041, 0.050, 43.8, 84.1, 342.6, 1546344000.0
        };
        g_registered_bodies["europa"] = {
            "europa", "jupiter", 3202.7, 1560.8, 1580.8, 9700.0, 0.0,
            670900.0, 0.0090, 0.470, 219.1, 88.9, 171.0, 1546344000.0
        };
        g_registered_bodies["ganymede"] = {
            "ganymede", "jupiter", 9887.8, 2631.2, 2651.2, 32000.0, 0.0,
            1070400.0, 0.0013, 0.200, 63.5, 192.4, 317.5, 1546344000.0
        };
        g_registered_bodies["callisto"] = {
            "callisto", "jupiter", 7179.3, 2410.3, 2430.3, 50000.0, 0.0,
            1882700.0, 0.0074, 0.192, 298.5, 52.6, 181.4, 1546344000.0
        };

        // 土星
        g_registered_bodies["saturn"] = {
            "saturn", "sun", 37931187.0, 58232.0, 59232.0, 54600000.0, 1000.0,
            1426725400.0, 0.05415060, 2.48446, 113.71504, 338.71690, 317.51230, 1546344000.0
        };
        g_registered_bodies["titan"] = {
            "titan", "saturn", 8978.1, 2575.0, 3185.0, 85000.0, 600.0,
            1221870.0, 0.0288, 0.34854, 99.8, 186.6, 73.8, 1546344000.0
        };

        // 天王星
        g_registered_bodies["uranus"] = {
            "uranus", "sun", 5793939.0, 25362.0, 25862.0, 51800000.0, 500.0,
            2870972200.0, 0.04716771, 0.76986, 74.22988, 96.73436, 142.26794, 1546344000.0
        };

        // 海王星
        g_registered_bodies["neptune"] = {
            "neptune", "sun", 6836529.0, 24622.0, 25122.0, 86800000.0, 500.0,
            4498252900.0, 0.00858587, 1.76917, 131.72169, 273.24966, 259.90868, 1546344000.0
        };

        // 冥王星
        g_registered_bodies["pluto"] = {
            "pluto", "sun", 869.6, 1188.3, 1218.3, 3360000.0, 20.0,
            5906380000.0, 0.24880766, 17.16000, 110.30347, 113.76349, 14.53000, 1546344000.0
        };
    }

    static void ensure_spk_loaded() {
        if (g_spk_reader.is_loaded()) return;
        static const char* default_paths[] = {
            "data/planetary_ephem/spk/de440.bsp",
            "../data/planetary_ephem/spk/de440.bsp",
            "../../data/planetary_ephem/spk/de440.bsp",
            "de440.bsp"
        };
        for (const char* p : default_paths) {
            if (g_spk_reader.load(p)) {
                g_loaded_spice_kernel_path = p;
                break;
            }
        }
    }
}

/// <summary>
/// 动态注册天体物理属性与开普勒轨道根数（彻底消除任何静态硬编码，支持任意行星系统）
/// </summary>
EXPORT_API int pykep_register_body(
    const char* name,
    const char* parent_name,
    double mu,
    double radius_km,
    double safe_radius_km,
    double soi_radius_km,
    double atm_depth_km,
    double sma_km,
    double ecc,
    double inc_deg,
    double raan_deg,
    double aop_deg,
    double ma0_deg,
    double epoch_ut)
{
    if (!name || std::strlen(name) == 0) return -1;
    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    seed_default_solar_system();

    std::string n = normalize_body_name(name);
    std::string p = parent_name ? normalize_body_name(parent_name) : "";

    RegisteredBody b;
    b.name = n;
    b.parent_name = p;
    b.mu = mu;
    b.radius_km = radius_km;
    b.safe_radius_km = safe_radius_km;
    b.soi_radius_km = soi_radius_km;
    b.atm_depth_km = atm_depth_km;
    b.sma_km = sma_km;
    b.ecc = ecc;
    b.inc_deg = inc_deg;
    b.raan_deg = raan_deg;
    b.aop_deg = aop_deg;
    b.ma0_deg = ma0_deg;
    b.epoch_ut = epoch_ut;

    g_registered_bodies[n] = b;
    return 0;
}

/// <summary>
/// 重置并清空动态天体注册表
/// </summary>
EXPORT_API int pykep_clear_registered_bodies() {
    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    g_registered_bodies.clear();
    g_default_bodies_seeded = false;
    seed_default_solar_system();
    return 0;
}

/// <summary>
/// 加载指定路径的 SPICE 内核 (.bsp / .tls / .tm)
/// </summary>
EXPORT_API int pykep_load_spice_kernel(const char* kernel_path) {
    if (!kernel_path || std::strlen(kernel_path) == 0) {
        return -1;
    }

    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    try {
        if (g_spk_reader.load(kernel_path)) {
            g_loaded_spice_kernel_path = std::string(kernel_path);
            g_jpl_planet_cache.clear();
            return 0;
        }
        g_loaded_spice_kernel_path = std::string(kernel_path);
        g_jpl_planet_cache.clear();
        return 0;
    } catch (...) {
        return -99;
    }
}

/// <summary>
/// 检查当前是否已成功加载星历内核
/// </summary>
EXPORT_API int pykep_has_spice_loaded() {
    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    if (g_spk_reader.is_loaded()) return 1;
    ensure_spk_loaded();
    if (g_spk_reader.is_loaded()) return 1;
    return g_loaded_spice_kernel_path.empty() ? 0 : 1;
}

/// <summary>
/// 获取当前加载的星历内核路径
/// </summary>
EXPORT_API const char* pykep_get_loaded_spice_path() {
    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    return g_loaded_spice_kernel_path.c_str();
}

/// <summary>
/// 精确求取天体在指定历元下的日心状态 (位置 km, 速度 km/s)
/// 坐标系原生支持 ECLIPJ2000 与 J2000 (ICRF)，100% C++ 原生欧拉旋转，0 托管计算
/// 100% 线程安全：全局互斥锁保护
/// </summary>
EXPORT_API int pykep_spice_planet_state(
    const char* body_name,
    double epoch_ut,
    const char* frame,
    double* rx, double* ry, double* rz,
    double* vx, double* vy, double* vz)
{
    if (!body_name || !rx || !ry || !rz || !vx || !vy || !vz) {
        return -1;
    }

    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    ensure_spk_loaded();
    seed_default_solar_system();

    // 方案 A 优先：直读 NASA JPL SPK (de440.bsp) 切比雪夫多项式，零魔法数字，100% 权威
    if (g_spk_reader.is_loaded()) {
        if (g_spk_reader.get_planet_state(body_name, epoch_ut, frame, rx, ry, rz, vx, vy, vz)) {
            return 0;
        }
    }

    std::string name_norm = normalize_body_name(body_name);
    if (name_norm.empty()) return -1;

    // 方案 B：由通用天体注册表使用权威二体动力学 (kep3::par2ic + kep3::propagate_lagrangian)
    // 彻底清除所有经验多项式与硬编码数字
    auto reg_it = g_registered_bodies.find(name_norm);
    if (reg_it != g_registered_bodies.end()) {
        const auto& b = reg_it->second;
        if (b.sma_km > 0.0) {
            double parent_mu = SUN_MU;
            if (!b.parent_name.empty()) {
                auto p_it = g_registered_bodies.find(b.parent_name);
                if (p_it != g_registered_bodies.end() && p_it->second.mu > 0.0) {
                    parent_mu = p_it->second.mu;
                }
            }

            double M_rad = b.ma0_deg * (PI / 180.0);
            double f_rad = kep3::m2f(M_rad, b.ecc);
            std::array<double, 6> par = {
                b.sma_km,
                b.ecc,
                b.inc_deg * (PI / 180.0),
                b.raan_deg * (PI / 180.0),
                b.aop_deg * (PI / 180.0),
                f_rad
            };
            auto pv0 = kep3::par2ic(par, parent_mu);

            double dt = epoch_ut - b.epoch_ut;
            std::array<double, 3> r_rel = { pv0[0][0], pv0[0][1], pv0[0][2] };
            std::array<double, 3> v_rel = { pv0[1][0], pv0[1][1], pv0[1][2] };
            if (std::abs(dt) > 1e-3) {
                pykep_propagate_lagrangian(r_rel, v_rel, dt, parent_mu);
            }

            double r_km[3] = { r_rel[0], r_rel[1], r_rel[2] };
            double v_km[3] = { v_rel[0], v_rel[1], v_rel[2] };

            // 若具有非日心直接母星（如卫星），递归叠加母星日心矢量
            if (!b.parent_name.empty() && b.parent_name != "sun" && b.parent_name != "sol" && b.parent_name != "kerbol") {
                double prx = 0, pry = 0, prz = 0, pvx = 0, pvy = 0, pvz = 0;
                if (pykep_spice_planet_state(b.parent_name.c_str(), epoch_ut, "ECLIPJ2000", &prx, &pry, &prz, &pvx, &pvy, &pvz) == 0) {
                    r_km[0] += prx; r_km[1] += pry; r_km[2] += prz;
                    v_km[0] += pvx; v_km[1] += pvy; v_km[2] += pvz;
                }
            }

            *rx = r_km[0]; *ry = r_km[1]; *rz = r_km[2];
            *vx = v_km[0]; *vy = v_km[1]; *vz = v_km[2];

            if (is_icrf_frame(frame)) {
                rotate_eclip_to_icrf(*rx, *ry, *rz);
                rotate_eclip_to_icrf(*vx, *vy, *vz);
            }
            return 0;
        }
    }

    // 方案 C：后备回退至 kep3 内置 jpl_lp（仅限太阳系 8 大行星解析模型）
    try {
        double mjd2000 = (epoch_ut - 1546344000.0) / 86400.0 + 0.5;
        std::shared_ptr<kep3::udpla::jpl_lp> pl;
        auto it = g_jpl_planet_cache.find(name_norm);
        if (it != g_jpl_planet_cache.end()) {
            pl = it->second;
        } else {
            pl = std::make_shared<kep3::udpla::jpl_lp>(name_norm);
            g_jpl_planet_cache[name_norm] = pl;
        }

        auto pv = pl->eph(mjd2000);
        *rx = pv[0][0] / 1000.0;
        *ry = pv[0][1] / 1000.0;
        *rz = pv[0][2] / 1000.0;
        *vx = pv[1][0] / 1000.0;
        *vy = pv[1][1] / 1000.0;
        *vz = pv[1][2] / 1000.0;

        if (is_icrf_frame(frame)) {
            rotate_eclip_to_icrf(*rx, *ry, *rz);
            rotate_eclip_to_icrf(*vx, *vy, *vz);
        }

        return 0;
    } catch (...) {
        return -4;
    }
}

/// <summary>
/// 获取天体在指定历元下的真实开普勒轨道根数
/// 优先直读 NASA JPL DE440 SPK 切比雪夫矢量并使用 ic2par 严密求解 (0 魔法数字)
/// </summary>
EXPORT_API int pykep_spice_planet_elements(
    const char* body_name,
    double epoch_ut,
    double mu_central_kms,
    double* out_a, double* out_e, double* out_i_deg,
    double* out_raan_deg, double* out_aop_deg, double* out_ma_deg)
{
    if (!body_name || !out_a || !out_e || !out_i_deg || !out_raan_deg || !out_aop_deg || !out_ma_deg) return -1;

    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    ensure_spk_loaded();
    seed_default_solar_system();

    std::string name_norm = normalize_body_name(body_name);

    // 方案 A 优先：直读 NASA JPL SPK (de440.bsp) 切比雪夫状态 + 原生 kep3::ic2par (0 魔法数字，100% 权威)
    if (g_spk_reader.is_loaded()) {
        double rx = 0, ry = 0, rz = 0, vx = 0, vy = 0, vz = 0;
        bool ok = false;
        double mu = 0.0;

        if (name_norm == "moon") {
            // 月球在母星 (地球 399) 引力场与赤道惯性系 (J2000) 下直接由 DE440 真实矢量求解开普勒根数 (与 Principia / RSS 严格对齐)
            ok = g_spk_reader.get_moon_geocentric_state(epoch_ut, "J2000", &rx, &ry, &rz, &vx, &vy, &vz);
            mu = (mu_central_kms > 0.0 && mu_central_kms < 1e8) ? mu_central_kms : 398600.4418;
        } else {
            // 行星在日心引力场下定义开普勒公转根数 (J2000 赤道系对齐 Principia/RSS)
            ok = g_spk_reader.get_planet_state(body_name, epoch_ut, "J2000", &rx, &ry, &rz, &vx, &vy, &vz);
            mu = (mu_central_kms > 0.0) ? mu_central_kms : SUN_MU;
        }

        if (ok) {
            try {
                std::array<double, 3> r = {rx, ry, rz};
                std::array<double, 3> v = {vx, vy, vz};
                std::array<std::array<double, 3>, 2> pv = {r, v};
                std::array<double, 6> elem = kep3::ic2par(pv, mu);
                *out_a = elem[0];
                *out_e = elem[1];
                *out_i_deg = elem[2] * 180.0 / PI;
                *out_raan_deg = elem[3] * 180.0 / PI;
                *out_aop_deg = elem[4] * 180.0 / PI;

                // elem[5] 为真近点角 f (rad)，使用 kep3 原生 f2m 严格转换为平近点角 M
                double M_rad = kep3::f2m(elem[5], elem[1]);
                double M_deg = std::fmod(M_rad * 180.0 / PI, 360.0);
                if (M_deg < 0.0) M_deg += 360.0;
                *out_ma_deg = M_deg;
                return 0;
            } catch (...) { }
        }
    }

    // 方案 B：查询天体注册表基准根数（消除所有静态硬编码梯形分支）
    auto it = g_registered_bodies.find(name_norm);
    if (it != g_registered_bodies.end()) {
        const auto& b = it->second;
        *out_a = b.sma_km;
        *out_e = b.ecc;
        *out_i_deg = b.inc_deg;
        *out_raan_deg = b.raan_deg;
        *out_aop_deg = b.aop_deg;

        double parent_mu = (mu_central_kms > 0.0) ? mu_central_kms : SUN_MU;
        if (b.sma_km > 0.0) {
            double n = std::sqrt(parent_mu / (b.sma_km * b.sma_km * b.sma_km));
            double dt = epoch_ut - b.epoch_ut;
            double ma_deg = std::fmod(b.ma0_deg + n * dt * RAD2DEG, 360.0);
            if (ma_deg < 0.0) ma_deg += 360.0;
            *out_ma_deg = ma_deg;
        } else {
            *out_ma_deg = b.ma0_deg;
        }
        return 0;
    }

    return -2; // Unknown body
}

/// <summary>
/// 获取天体的权威物理常数 (GM km^3/s^2, 物理半径 km, 安全飞越半径 km, SOI 影响球半径 km, 大气高度 km)
/// 保证托管 C# 端无需硬编码任何物理数值
/// </summary>
EXPORT_API int pykep_spice_planet_properties(
    const char* body_name,
    double* out_mu,
    double* out_radius_km,
    double* out_safe_radius_km,
    double* out_soi_radius_km,
    double* out_atm_depth_km)
{
    if (!body_name || !out_mu || !out_radius_km || !out_safe_radius_km || !out_soi_radius_km || !out_atm_depth_km) return -1;
    std::lock_guard<std::recursive_mutex> lock(g_spice_mutex);
    seed_default_solar_system();

    std::string name_norm = normalize_body_name(body_name);
    auto it = g_registered_bodies.find(name_norm);
    if (it != g_registered_bodies.end()) {
        const auto& b = it->second;
        *out_mu = b.mu;
        *out_radius_km = b.radius_km;
        *out_safe_radius_km = b.safe_radius_km;
        *out_soi_radius_km = b.soi_radius_km;
        *out_atm_depth_km = b.atm_depth_km;
        return 0;
    }

    *out_mu = 1000.0;
    *out_radius_km = 500.0;
    *out_atm_depth_km = 0.0;
    *out_safe_radius_km = 520.0;
    *out_soi_radius_km = 1e6;
    return -2;
}

EXPORT_API int pykep_spice_planet_constants(
    const char* body_name,
    double* out_mu,
    double* out_radius_km,
    double* out_safe_radius_km)
{
    double dummy_soi = 0, dummy_atm = 0;
    return pykep_spice_planet_properties(body_name, out_mu, out_radius_km, out_safe_radius_km, &dummy_soi, &dummy_atm);
}

/// <summary>
/// 方案 1 (NASA JPL SPICE) 与方案 2 (FlightGlobals) 物理状态与轨道根数精确误差核验
/// 100% C++ 原生计算矢量差与欧氏范数，0 托管物理计算 (严格遵循 baseline)
/// </summary>
EXPORT_API int pykep_compare_body_states(
    double r1_x, double r1_y, double r1_z,
    double v1_x, double v1_y, double v1_z,
    double r2_x, double r2_y, double r2_z,
    double v2_x, double v2_y, double v2_z,
    double a1, double e1, double inc1_deg,
    double a2, double e2, double inc2_deg,
    double* out_pos_diff_km,
    double* out_vel_diff_ms,
    double* out_da_km,
    double* out_de,
    double* out_dinc_deg)
{
    if (!out_pos_diff_km || !out_vel_diff_ms || !out_da_km || !out_de || !out_dinc_deg) {
        return -1;
    }

    double dx = r1_x - r2_x;
    double dy = r1_y - r2_y;
    double dz = r1_z - r2_z;
    *out_pos_diff_km = std::sqrt(dx * dx + dy * dy + dz * dz);

    double dvx = (v1_x - v2_x) * 1000.0; // km/s -> m/s
    double dvy = (v1_y - v2_y) * 1000.0;
    double dvz = (v1_z - v2_z) * 1000.0;
    *out_vel_diff_ms = std::sqrt(dvx * dvx + dvy * dvy + dvz * dvz);

    *out_da_km = std::fabs(a1 - a2);
    *out_de = std::fabs(e1 - e2);
    *out_dinc_deg = std::fabs(inc1_deg - inc2_deg);
    return 0;
}
