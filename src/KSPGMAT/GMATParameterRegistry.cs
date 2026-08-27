using System;
using System.Collections.Generic;

namespace KSPGMAT {
    public struct GMATParam {
        public string Name;
        public string Category;
        public string Description;
        public string Example;

        public GMATParam(string name, string category, string desc, string example) {
            Name = name;
            Category = category;
            Description = desc;
            Example = example;
        }
    }

    public static class GMATParameterRegistry {
        public static readonly string[] Categories = new string[] {
            "⏱️ 时间/纪元",
            "📐 地月旋转系",
            "🌐 轨道根数",
            "📏 高度与半径",
            "🌍 空间惯性系",
            "⚖️ 质量与硬件"
        };

        public static readonly GMATParam[] AllParameters = new GMATParam[] {
            // 时间与纪元
            new GMATParam("ElapsedSecs", "⏱️ 时间/纪元", "任务经过秒数 (Time in seconds)", "1800.0"),
            new GMATParam("ElapsedDays", "⏱️ 时间/纪元", "任务经过天数 (Time in days)", "2.2"),
            new GMATParam("A1ModJulian", "⏱️ 时间/纪元", "A1修正儒略日纪元", "21545.0"),
            new GMATParam("UTCGregorian", "⏱️ 时间/纪元", "UTC世界时纪元字符", "'27 Aug 2026 12:00:00.000'"),

            // 地月旋转坐标系 (EarthMoonRot) - NRHO 关键坐标
            new GMATParam("EarthMoonRot.X", "📐 地月旋转系", "地月连线方向坐标 (km)", "-173.0"),
            new GMATParam("EarthMoonRot.Y", "📐 地月旋转系", "垂直地月连线切向坐标 (km)", "0.0"),
            new GMATParam("EarthMoonRot.Z", "📐 地月旋转系", "月球北极法向垂直坐标 (km)", "3229.0"),
            new GMATParam("EarthMoonRot.VX", "📐 地月旋转系", "径向相对漂移速度 (km/s)", "0.0"),
            new GMATParam("EarthMoonRot.VY", "📐 地月旋转系", "切向顺/逆行速度 (km/s)", "-1.6674"),
            new GMATParam("EarthMoonRot.VZ", "📐 地月旋转系", "垂直面相对速度分量 (km/s)", "0.0"),

            // 开普勒轨道根数
            new GMATParam("SMA", "🌐 轨道根数", "半长轴 (Semi-Major Axis in km)", "6578.14"),
            new GMATParam("ECC", "🌐 轨道根数", "轨道离心率 (Eccentricity 0~1+)", "0.0012"),
            new GMATParam("INC", "🌐 轨道根数", "轨道倾角 (Inclination in deg)", "28.5"),
            new GMATParam("RAAN", "🌐 轨道根数", "升交点赤经 (RAAN in deg)", "120.0"),
            new GMATParam("AOP", "🌐 轨道根数", "近心点幅角 (Arg of Periapsis in deg)", "45.0"),
            new GMATParam("TA", "🌐 轨道根数", "真近点角 (True Anomaly in deg)", "180.0"),
            new GMATParam("RadPer", "🌐 轨道根数", "近拱点距心半径 (km)", "6578.14"),
            new GMATParam("RadApo", "🌐 轨道根数", "远拱点距心半径 (km)", "42164.0"),

            // 高度与距心半径
            new GMATParam("Altitude", "📏 高度与半径", "海平面高度 (Altitude in km)", "200.0"),
            new GMATParam("RMAG", "📏 高度与半径", "距中心天体总半径模长 (km)", "6578.14"),
            new GMATParam("Earth.Altitude", "📏 高度与半径", "相对地球表面高度 (km)", "200.0"),
            new GMATParam("Luna.Altitude", "📏 高度与半径", "相对月球表面高度 (km)", "1491.6"),
            new GMATParam("Earth.RMAG", "📏 高度与半径", "相对地心距离模长 (km)", "42164.0"),
            new GMATParam("Luna.RMAG", "📏 高度与半径", "相对月心距离模长 (km)", "3229.0"),

            // 空间惯性坐标系 (EarthMJ2000Eq & MoonInertial)
            new GMATParam("EarthMJ2000Eq.X", "🌍 空间惯性系", "地心惯性系X坐标 (km)", "1828.514"),
            new GMATParam("EarthMJ2000Eq.Y", "🌍 空间惯性系", "地心惯性系Y坐标 (km)", "5536.326"),
            new GMATParam("EarthMJ2000Eq.Z", "🌍 空间惯性系", "地心惯性系Z坐标 (km)", "3045.902"),
            new GMATParam("EarthMJ2000Eq.VX", "🌍 空间惯性系", "地心惯性系VX速度 (km/s)", "-7.485"),
            new GMATParam("EarthMJ2000Eq.VY", "🌍 空间惯性系", "地心惯性系VY速度 (km/s)", "1.934"),
            new GMATParam("EarthMJ2000Eq.VZ", "🌍 空间惯性系", "地心惯性系VZ速度 (km/s)", "0.980"),
            new GMATParam("MoonInertial.X", "🌍 空间惯性系", "月心惯性系X坐标 (km)", "0.0"),
            new GMATParam("MoonInertial.Y", "🌍 空间惯性系", "月心惯性系Y坐标 (km)", "0.0"),
            new GMATParam("MoonInertial.Z", "🌍 空间惯性系", "月心惯性系Z坐标 (km)", "3229.0"),
            new GMATParam("MoonInertial.VX", "🌍 空间惯性系", "月心惯性系VX速度 (km/s)", "0.0"),
            new GMATParam("MoonInertial.VY", "🌍 空间惯性系", "月心惯性系VY速度 (km/s)", "-1.6674"),
            new GMATParam("MoonInertial.VZ", "🌍 空间惯性系", "月心惯性系VZ速度 (km/s)", "0.0"),

            // 质量与硬件
            new GMATParam("TotalMass", "⚖️ 质量与硬件", "飞船总质量 (Total Mass in kg)", "7500.0"),
            new GMATParam("DryMass", "⚖️ 质量与硬件", "飞船干重 (Dry Mass in kg)", "1500.0"),
            new GMATParam("MainTank.FuelMass", "⚖️ 质量与硬件", "主储罐剩余燃料质量 (kg)", "6000.0")
        };

        public static List<GMATParam> FindMatches(string query, int maxResults = 4) {
            List<GMATParam> list = new List<GMATParam>();
            if (string.IsNullOrEmpty(query)) return list;
            string lower = query.ToLowerInvariant().Trim();
            foreach (var p in AllParameters) {
                if (p.Name.ToLowerInvariant().Contains(lower) || p.Description.ToLowerInvariant().Contains(lower)) {
                    list.Add(p);
                    if (list.Count >= maxResults) break;
                }
            }
            return list;
        }

        public static List<GMATParam> GetByCategory(string category) {
            List<GMATParam> list = new List<GMATParam>();
            foreach (var p in AllParameters) {
                if (p.Category == category) {
                    list.Add(p);
                }
            }
            return list;
        }
    }
}
