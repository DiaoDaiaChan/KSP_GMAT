using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.MGA;

namespace KSPGMAT.Service {
    /// <summary>
    /// 高性能内存像素缓冲画布 (彻底消除 Texture2D.GetPixel/SetPixel 跨非托管边界的巨量 P/Invoke 开销)
    /// </summary>
    public class CanvasPixelBuffer {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public CanvasPixelBuffer(int w, int h, Color32 bg) {
            Width = w;
            Height = h;
            Pixels = new Color32[w * h];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = bg;
        }

        public void ClearWithCosmicBackground() {
            int cx = Width / 2;
            int cy = Height / 2;
            float maxR2 = (float)(cx * cx + cy * cy);
            if (maxR2 < 1f) maxR2 = 1f;

            for (int y = 0; y < Height; y++) {
                int dy = y - cy;
                int dy2 = dy * dy;
                int rowIdx = y * Width;
                for (int x = 0; x < Width; x++) {
                    int dx = x - cx;
                    float r2 = dx * dx + dy2;
                    float t = Mathf.Clamp01(r2 / maxR2);
                    // 中心深邃靛青/石板蓝暗调 (11, 16, 26)，向四周边缘平滑过渡至深空黑 (4, 5, 8)
                    byte r = (byte)(11 - t * 7);
                    byte g = (byte)(16 - t * 11);
                    byte b = (byte)(26 - t * 18);
                    Pixels[rowIdx + x] = new Color32(r, g, b, 255);
                }
            }

            // 确定性微弱深空恒星背景粒子 (固定种子 19770820，摄像机平移/旋转时不发生闪烁噪动)
            uint seed = 19770820;
            int numStars = Math.Min(110, (Width * Height) / 2400);
            for (int i = 0; i < numStars; i++) {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                int sx = (int)(seed % (uint)Width);
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                int sy = (int)(seed % (uint)Height);
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                byte br = (byte)(25 + (seed % 60));
                int idx = sy * Width + sx;
                Color32 cur = Pixels[idx];
                Pixels[idx] = new Color32(
                    (byte)Math.Min(255, cur.r + br),
                    (byte)Math.Min(255, cur.g + br),
                    (byte)Math.Min(255, cur.b + (byte)(br * 1.3f)),
                    255
                );
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void BlendPixelFast(int x, int y, byte r, byte g, byte b, float alpha) {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            int idx = y * Width + x;
            if (alpha <= 0.001f) return;
            if (alpha >= 0.999f) {
                Pixels[idx] = new Color32(r, g, b, 255);
                return;
            }
            Color32 cur = Pixels[idx];
            float invA = 1.0f - alpha;
            Pixels[idx] = new Color32(
                (byte)(cur.r * invA + r * alpha),
                (byte)(cur.g * invA + g * alpha),
                (byte)(cur.b * invA + b * alpha),
                255
            );
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void BlendPixel(int x, int y, Color col) {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            int idx = y * Width + x;
            float alpha = col.a;
            if (alpha <= 0.001f) return;
            if (alpha >= 0.999f) {
                Pixels[idx] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), 255);
                return;
            }
            Color32 cur = Pixels[idx];
            float invA = 1.0f - alpha;
            Pixels[idx] = new Color32(
                (byte)(cur.r * invA + col.r * 255f * alpha),
                (byte)(cur.g * invA + col.g * 255f * alpha),
                (byte)(cur.b * invA + col.b * 255f * alpha),
                255
            );
        }
    }

    /// <summary>
    /// 高精工业级三维/二维日心轨道与深空引力助推全景视口渲染引擎
    /// (High-Precision Anti-Aliased 3D/2D Astronomical Heliocentric Orbit Engine)
    /// </summary>
    public static class GMATOrbitCanvas {
        public const double AU_KM = 149597870.7; // 1 AU in km

        // Celestial Theme Palettes
        private static readonly Color SunCoreColor = new Color(1.0f, 0.96f, 0.60f, 1.0f);
        private static readonly Color SunCoronaColor = new Color(1.0f, 0.65f, 0.15f, 0.45f);
        private static readonly Color SunGlowOuter = new Color(1.0f, 0.45f, 0.05f, 0.15f);

        private static readonly Dictionary<string, Color> PlanetColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase) {
            ["Mercury"] = new Color(0.72f, 0.75f, 0.78f, 1.0f), // Slate Silver
            ["Venus"]   = new Color(0.92f, 0.78f, 0.48f, 1.0f), // Golden Pearl
            ["Earth"]   = new Color(0.28f, 0.65f, 1.00f, 1.0f), // Azure Blue
            ["Moon"]    = new Color(0.80f, 0.82f, 0.85f, 1.0f), // Luna Pale
            ["Mars"]    = new Color(0.92f, 0.38f, 0.24f, 1.0f), // Rust Red
            ["Jupiter"] = new Color(0.86f, 0.68f, 0.48f, 1.0f), // Jovian Ochre
            ["Saturn"]  = new Color(0.92f, 0.82f, 0.58f, 1.0f), // Pale Gold
            ["Uranus"]  = new Color(0.48f, 0.88f, 0.95f, 1.0f), // Cyan Ice
            ["Neptune"] = new Color(0.32f, 0.48f, 0.92f, 1.0f), // Deep Azure
            ["Pluto"]   = new Color(0.75f, 0.68f, 0.62f, 1.0f)  // Brownish Gray
        };

        private static readonly Color[] LegColors = new Color[] {
            new Color(0.22f, 0.74f, 0.97f, 1.0f), // 1: Cyber Cyan
            new Color(0.20f, 0.86f, 0.58f, 1.0f), // 2: Emerald Green
            new Color(0.98f, 0.78f, 0.15f, 1.0f), // 3: Amber Gold
            new Color(0.72f, 0.48f, 0.98f, 1.0f), // 4: Royal Violet
            new Color(0.98f, 0.42f, 0.55f, 1.0f), // 5: Coral Rose
            new Color(0.38f, 0.88f, 0.98f, 1.0f)  // 6: Sky Blue
        };

        /// <summary>
        /// 渲染单飞船局部轨道图 (TabSequence)
        /// </summary>
        public static Texture2D RenderTrajectoryTexture(List<TrajectoryPoint> points, string centralBody, int width = 512, int height = 512) {
            CanvasPixelBuffer buf = new CanvasPixelBuffer(width, height, new Color32(6, 9, 14, 255));
            buf.ClearWithCosmicBackground();

            if (points == null || points.Count == 0) {
                Texture2D emptyTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                emptyTex.SetPixels32(buf.Pixels);
                emptyTex.Apply(false);
                return emptyTex;
            }

            double maxR = 1000.0;
            for (int i = 0; i < points.Count; i++) {
                double r = Math.Sqrt(points[i].x * points[i].x + points[i].y * points[i].y + points[i].z * points[i].z);
                if (r > maxR) maxR = r;
            }
            double viewRadius = maxR * 1.25;

            int cx = width / 2;
            int cy = height / 2;
            double scale = (Math.Min(width, height) * 0.44) / viewRadius;

            // Draw central body
            double bodyRadKm = 6378.14;
            Color bodyColor = new Color(0.25f, 0.60f, 0.95f, 1f);
            if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                var b = FlightGlobals.Bodies.Find(cb => cb != null && cb.bodyName.Equals(centralBody, StringComparison.OrdinalIgnoreCase));
                if (b != null && b.Radius > 0) {
                    bodyRadKm = b.Radius / 1000.0;
                    if (b.bodyName == "Moon" || b.bodyName == "Luna") bodyColor = new Color(0.7f, 0.7f, 0.75f, 1f);
                    else if (b.bodyName == "Mars" || b.bodyName == "Duna") bodyColor = new Color(0.9f, 0.35f, 0.2f, 1f);
                    else if (b.bodyName == "Sun" || b.bodyName == "Kerbol") bodyColor = new Color(1.0f, 0.85f, 0.2f, 1f);
                }
            }

            int bodyPix = Math.Max(5, (int)(bodyRadKm * scale));
            DrawGlowCircle(buf, cx, cy, bodyPix, bodyColor);

            // Draw anti-aliased trajectory
            Color trajColor = new Color(0.35f, 0.80f, 0.98f, 1.0f);
            for (int i = 0; i < points.Count - 1; i++) {
                int x0 = cx + (int)(points[i].x * scale);
                int y0 = cy + (int)(points[i].y * scale);
                int x1 = cx + (int)(points[i + 1].x * scale);
                int y1 = cy + (int)(points[i + 1].y * scale);
                DrawSmoothLine(buf, x0, y0, x1, y1, trajColor);
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(buf.Pixels);
            tex.Apply(false);
            return tex;
        }

        /// <summary>
        /// 渲染太阳系多重引力助推 3D/2D 日心轨道高精全景画布 (支持 3D 自由旋转俯仰与 2D 锁顶视口)
        /// 支持 Texture2D 重用，彻底杜绝 GPU 显存泄漏与 GC 停顿
        /// </summary>
        public static Texture2D RenderSolarSystemMGATexture(
            Texture2D existingTex,
            MGASolution solution, 
            int width = 580, 
            int height = 440, 
            float zoom = 1.0f, 
            Vector2 panOffset = default,
            bool showSoi = true,
            float pitchDeg = 35.0f,
            float yawDeg = 0.0f,
            int selectedEventIndex = -1,
            float flowProgress = 0f) 
        {
            CanvasPixelBuffer buf = new CanvasPixelBuffer(width, height, new Color32(5, 6, 10, 255));
            buf.ClearWithCosmicBackground();

            // 1. 计算当前任务历元与航天器高精遥测数据 (SSOT: 全面打通 MGAStateManager)
            CraftTelemetry telem = ComputeCraftTelemetry(solution, flowProgress, selectedEventIndex);
            double missionUt = telem.MissionUt;
            bool isInterplanetary = telem.IsInterplanetary;
            string centralBody = telem.CentralBody ?? "Sun";
            double muCentral = isInterplanetary ? SolarSystemCatalog.SunMu : SolarSystemCatalog.GetCentralMu(centralBody);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;

            // 2. 动态依据引力域确定视口物理跨度 (km)
            double missionMaxDistKm = 0;
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                int countToCheck = (flowProgress > 0.0001f && flowProgress <= solution.Legs.Count) ? Mathf.CeilToInt(flowProgress) : solution.Legs.Count;
                for (int k = 0; k < countToCheck; k++) {
                    var leg = solution.Legs[k];
                    double r1 = leg.DeparturePosKm.magnitude;
                    double r2 = leg.ArrivalPosKm.magnitude;
                    if (!double.IsNaN(r1) && !double.IsInfinity(r1) && r1 > missionMaxDistKm && r1 < 100.0 * AU_KM) missionMaxDistKm = r1;
                    if (!double.IsNaN(r2) && !double.IsInfinity(r2) && r2 > missionMaxDistKm && r2 < 100.0 * AU_KM) missionMaxDistKm = r2;
                }
            }

            if (missionMaxDistKm <= 0) {
                if (isInterplanetary) {
                    missionMaxDistKm = 2.2 * AU_KM;
                } else {
                    var moons = SolarSystemCatalog.GetMoonsOf(centralBody);
                    if (moons != null && moons.Count > 0) {
                        missionMaxDistKm = moons.Max(m => m.SMA > 0 ? m.SMA : 400000.0) * 1.25;
                    } else {
                        var pC = SolarSystemCatalog.GetPlanet(centralBody);
                        missionMaxDistKm = (pC != null && pC.RadiusKm > 0) ? pC.RadiusKm * 15.0 : 400000.0;
                    }
                }
            }

            // View framing
            double viewRadiusKm = (missionMaxDistKm * 1.30) / Math.Max(0.05f, zoom);
            if (isInterplanetary) {
                if (double.IsNaN(viewRadiusKm) || double.IsInfinity(viewRadiusKm) || viewRadiusKm < 0.1 * AU_KM || viewRadiusKm > 200.0 * AU_KM) {
                    viewRadiusKm = 2.86 * AU_KM;
                }
            } else {
                if (double.IsNaN(viewRadiusKm) || double.IsInfinity(viewRadiusKm) || viewRadiusKm < 500.0) {
                    viewRadiusKm = Math.Max(500.0, missionMaxDistKm * 1.30);
                }
            }

            // 3. Strict Isometric Scale
            double scale = (Math.Min(width, height) * 0.44) / viewRadiusKm;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) {
                scale = (Math.Min(width, height) * 0.44) / (isInterplanetary ? (2.86 * AU_KM) : 400000.0);
            }

            int cx = (int)(width / 2 + panOffset.x);
            int cy = (int)(height / 2 + panOffset.y);

            // 4. Draw 3D Ecliptic Reference Grid & Vernal/Orbital Axis
            Draw3DEclipticGrid(buf, cx, cy, scale, viewRadiusKm, pitchDeg, yawDeg, isInterplanetary);

            // 5. 2D 屏幕空间标签避让排版与去重管理器
            ScreenLabelManager labelMgr = new ScreenLabelManager(width, height);

            // 注册 HUD 标尺与四周操作区域禁区 (严禁任何天体或节点标签遮挡仪表板)
            labelMgr.RegisterExclusionZone(20 + 40, 20 + 8, 45, 12, priority: 0); // 左下角比例尺
            int panelW = 236;
            int panelH = 56;
            labelMgr.RegisterExclusionZone(14 + panelW / 2, height - 12 - panelH / 2, panelW / 2 + 4, panelH / 2 + 4, priority: 0); // 左上角遥测 HUD
            labelMgr.RegisterExclusionZone(width - 120, 20, 115, 12, priority: 0); // 右下角操作提示
            labelMgr.RegisterExclusionZone(width - 60, height - 18, 55, 10, priority: 0); // 右上角 3D 视角

            // 预先计算选中的机动目标框位置
            Vector2 tgtScr = new Vector2(-1000, -1000);
            if (solution != null && solution.Events != null && selectedEventIndex >= 0 && selectedEventIndex < solution.Events.Count) {
                var curEv = solution.Events[selectedEventIndex];
                Vector3d tgtPos = GetEventPositionKm(solution, curEv);
                if (!double.IsNaN(tgtPos.x)) {
                    tgtScr = Project3D(tgtPos, scale, cx, cy, pitchDeg, yawDeg, out _);
                    // 登记目标框禁区 (40x40 瞄准框)
                    labelMgr.RegisterExclusionZone((int)tgtScr.x, (int)tgtScr.y, 22, 22, priority: 0);
                }
            }

            // 6. 绘制行星系统或卫星系统 (自动按引力域无缝切换)
            Draw3DPlanetarySystem(buf, cx, cy, scale, viewRadiusKm, missionUt, pitchDeg, yawDeg, solution, labelMgr, isInterplanetary, centralBody, muCentral);

            // 7. 绘制引力中心天体 (恒星或母行星)
            Vector2 centerScr = Project3D(Vector3d.zero, scale, cx, cy, pitchDeg, yawDeg, out _);
            if (isInterplanetary) {
                DrawSun(buf, (int)centerScr.x, (int)centerScr.y);
                labelMgr.RegisterExclusionZone((int)centerScr.x, (int)centerScr.y, 14, 14, priority: 0);
            } else {
                DrawCentralPlanet(buf, (int)centerScr.x, (int)centerScr.y, centralBody, labelMgr);
                labelMgr.RegisterExclusionZone((int)centerScr.x, (int)centerScr.y, 16, 16, priority: 0);
            }

            // 6. Draw 3D Transfer Trajectories & Maneuver Nodes
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                Draw3DTransferLegs(buf, solution, cx, cy, scale, viewRadiusKm, showSoi, pitchDeg, yawDeg, flowProgress, labelMgr, tgtScr, telem);

                // 6.1 Draw Target Focus Reticle for currently selected Maneuver Event (机动节点目标锁定框)
                if (solution.Events != null && selectedEventIndex >= 0 && selectedEventIndex < solution.Events.Count) {
                    var curEv = solution.Events[selectedEventIndex];
                    if (tgtScr.x >= 0 && tgtScr.y >= 0) {
                        Color reticleCol = new Color(0.22f, 0.88f, 0.98f, 0.95f); // High-tech Cyan
                        string tagStr = $"[{curEv.Title}]";
                        DrawTargetReticle(buf, (int)tgtScr.x, (int)tgtScr.y, tagStr, reticleCol, labelMgr);
                    }
                }
            }

            // 7. Draw HUD Overlay (Scale, Camera Info, Top-Left Flight Telemetry)
            DrawHudOverlay(buf, viewRadiusKm, scale, pitchDeg, yawDeg, telem, solution);

            // 8. 详细几何诊断日志输出 (当解更新或切换任务时记录)
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                int solHash = solution.GetHashCode() ^ solution.Legs.Count;
                if (solHash != _lastLoggedSolutionHash || Math.Abs(solution.DepartureUT - _lastLoggedDepUT) > 1.0) {
                    _lastLoggedSolutionHash = solHash;
                    _lastLoggedDepUT = solution.DepartureUT;
                    LogCanvasDiagnostics(solution, width, height, zoom, scale, pitchDeg, yawDeg, cx, cy);
                }
            }

            Texture2D tex = existingTex;
            if (tex == null || tex.width != width || tex.height != height) {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
            tex.SetPixels32(buf.Pixels);
            tex.Apply(false);
            return tex;
        }

        private static int _lastLoggedSolutionHash = 0;
        private static double _lastLoggedDepUT = -1.0;

        private static void LogCanvasDiagnostics(MGASolution solution, int width, int height, float zoom, double scale, float pitch, float yaw, int cx, int cy) {
            try {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[KSPGMAT-Canvas] ==================== 3D 视口与星历几何诊断报告 ====================");
                sb.AppendLine($"[KSPGMAT-Canvas] 画布尺寸: {width}x{height} | 缩放(Zoom): {zoom:F2} | 标度(Scale): {scale:0.000E+0} px/km | 俯仰(Pitch): {pitch:F1}° | 偏航(Yaw): {yaw:F1}° | 投影中心: ({cx}, {cy})");
                sb.AppendLine($"[KSPGMAT-Canvas] 任务总链: {solution.FlybyChainSummary} | 出发UT: {solution.DepartureUT:F1} ({GMATClient.FormatUtcGregorian(solution.DepartureUT)}) | 到达UT: {solution.ArrivalUT:F1} ({GMATClient.FormatUtcGregorian(solution.ArrivalUT)}) | 飞行: {solution.TotalFlightDays:F1}d | 总ΔV: {solution.TotalDeltaVKms * 1000.0:F1} m/s");

                for (int k = 0; k < solution.Legs.Count; k++) {
                    var l = solution.Legs[k];
                    Vector2 dScr = Project3D(l.DeparturePosKm, scale, cx, cy, pitch, yaw, out _);
                    Vector2 aScr = Project3D(l.ArrivalPosKm, scale, cx, cy, pitch, yaw, out _);
                    double r1AU = l.DeparturePosKm.magnitude / AU_KM;
                    double r2AU = l.ArrivalPosKm.magnitude / AU_KM;
                    sb.AppendLine($"[KSPGMAT-Canvas]   航段 #{k + 1}: {l.FromBody} -> {l.ToBody} (飞行 {l.ToFDays:F1}天, 相角 {l.PhaseAngleDeg:F1}°)");
                    sb.AppendLine($"[KSPGMAT-Canvas]     出发点: UT={l.DepartureUT:F1} ({GMATClient.FormatUtcGregorian(l.DepartureUT)}) | 日心坐标=({l.DeparturePosKm.x:F0}, {l.DeparturePosKm.y:F0}, {l.DeparturePosKm.z:F0}) km ({r1AU:F3} AU) | 屏幕像素=({dScr.x:F0}, {dScr.y:F0})");
                    sb.AppendLine($"[KSPGMAT-Canvas]     到达点: UT={l.ArrivalUT:F1} ({GMATClient.FormatUtcGregorian(l.ArrivalUT)}) | 日心坐标=({l.ArrivalPosKm.x:F0}, {l.ArrivalPosKm.y:F0}, {l.ArrivalPosKm.z:F0}) km ({r2AU:F3} AU) | 屏幕像素=({aScr.x:F0}, {aScr.y:F0})");
                }

                var planets = SolarSystemCatalog.GetAvailablePlanets();
                sb.AppendLine("[KSPGMAT-Canvas]   关键天体当前绘制位置核查:");
                foreach (var p in planets) {
                    if (!IsMajorPlanetOrMissionBody(p.Name, solution)) continue;
                    double evalUt = solution.DepartureUT;
                    string role = "非任务天体";
                    bool isArrival = false;
                    if (solution.Legs.Any(l => l.ToBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) {
                        var leg = solution.Legs.First(l => l.ToBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                        evalUt = leg.ArrivalUT;
                        role = $"目标到达天体(交会于Leg#{leg.LegIndex + 1})";
                        isArrival = true;
                    } else if (solution.Legs.Any(l => l.FromBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) {
                        var leg = solution.Legs.First(l => l.FromBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                        evalUt = leg.DepartureUT;
                        role = $"任务始发天体(发射于Leg#{leg.LegIndex + 1})";
                    }
                    p.GetHeliocentricState(evalUt, out Vector3d pos, out _);
                    Vector2 scr = Project3D(pos, scale, cx, cy, pitch, yaw, out _);
                    double distAU = pos.magnitude / AU_KM;

                    string ghostInfo = "";
                    if (isArrival && solution.DepartureUT > 0 && Math.Abs(evalUt - solution.DepartureUT) > 3600.0) {
                        p.GetHeliocentricState(solution.DepartureUT, out Vector3d gPos, out _);
                        Vector2 gScr = Project3D(gPos, scale, cx, cy, pitch, yaw, out _);
                        ghostInfo = $" | 发射初位幽灵: 日心=({gPos.x:F0}, {gPos.y:F0}, {gPos.z:F0}) km, 像素=({gScr.x:F0}, {gScr.y:F0})";
                    }

                    sb.AppendLine($"[KSPGMAT-Canvas]     {p.Name,-8} ({p.Icon}) [{role}]: 评估历元={evalUt:F1} ({GMATClient.FormatUtcGregorian(evalUt)}) | 日心=({pos.x:F0}, {pos.y:F0}, {pos.z:F0}) km ({distAU:F3} AU) | 屏幕像素=({scr.x:F0}, {scr.y:F0}){ghostInfo}");
                }
                sb.AppendLine("[KSPGMAT-Canvas] ===================================================================");
                UnityEngine.Debug.Log(sb.ToString());
            } catch (Exception ex) {
                UnityEngine.Debug.LogWarning("[KSPGMAT-Canvas] LogCanvasDiagnostics error: " + ex.Message);
            }
        }

        public static Texture2D RenderSolarSystemMGATexture(
            MGASolution solution, 
            int width = 580, 
            int height = 440, 
            float zoom = 1.0f, 
            Vector2 panOffset = default,
            bool showSoi = true,
            float pitchDeg = 35.0f,
            float yawDeg = 0.0f,
            int selectedEventIndex = -1,
            float flowProgress = 0f) 
        {
            return RenderSolarSystemMGATexture(null, solution, width, height, zoom, panOffset, showSoi, pitchDeg, yawDeg, selectedEventIndex, flowProgress);
        }

        public static Texture2D RenderSolarSystemMGATexture(
            Texture2D existingTex,
            MGASolution solution, 
            int width, int height, float zoom, Vector2 panOffset, bool showSoi, float pitchDeg, float yawDeg, int selectedEventIndex, int maxLegsToDraw) 
        {
            return RenderSolarSystemMGATexture(existingTex, solution, width, height, zoom, panOffset, showSoi, pitchDeg, yawDeg, selectedEventIndex, (float)maxLegsToDraw);
        }

        public static Vector3d GetEventPositionKm(MGASolution solution, MGAEventItem ev) {
            if (solution == null || solution.Legs == null || solution.Legs.Count == 0 || ev == null) return Vector3d.zero;
            if (ev.EventType == MGAEventType.Escape) {
                return solution.Legs[0].DeparturePosKm;
            }
            if (ev.EventType == MGAEventType.Circularization) {
                return solution.Legs.Last().ArrivalPosKm;
            }
            if (ev.EventType == MGAEventType.DSM || ev.EventType == MGAEventType.TCM) {
                if (!double.IsNaN(ev.PositionKm.x) && ev.PositionKm.magnitude > 100.0) {
                    return ev.PositionKm;
                }
            }
            foreach (var leg in solution.Legs) {
                if (Math.Abs(leg.DepartureUT - ev.EpochUT) < 10.0 || leg.FromBody.Equals(ev.BodyName, StringComparison.OrdinalIgnoreCase)) {
                    return leg.DeparturePosKm;
                }
            }
            if (ev.SequenceNumber > 1 && ev.SequenceNumber - 2 < solution.Legs.Count) {
                return solution.Legs[ev.SequenceNumber - 2].ArrivalPosKm;
            }
            return solution.Legs.Last().ArrivalPosKm;
        }

        // =========================================================================
        // 3D PROJECTION PIPELINE
        // =========================================================================

        public static Vector2 Project3D(Vector3d posKm, double scale, int cx, int cy, float pitchDeg, float yawDeg, out float depth) {
            if (double.IsNaN(posKm.x) || double.IsNaN(posKm.y) || double.IsNaN(posKm.z) ||
                double.IsInfinity(posKm.x) || double.IsInfinity(posKm.y) || double.IsInfinity(posKm.z) ||
                double.IsNaN(scale) || double.IsInfinity(scale)) {
                depth = 0;
                return new Vector2(cx, cy);
            }

            double yawRad = yawDeg * Mathf.Deg2Rad;
            double pitchRad = pitchDeg * Mathf.Deg2Rad;

            // 1. Yaw rotation around Z axis (in ecliptic plane)
            double cosY = Math.Cos(yawRad), sinY = Math.Sin(yawRad);
            double x1 = posKm.x * cosY - posKm.y * sinY;
            double y1 = posKm.x * sinY + posKm.y * cosY;
            double z1 = posKm.z;

            // 2. Pitch tilt around X axis (elevation from ecliptic)
            double cosP = Math.Cos(pitchRad), sinP = Math.Sin(pitchRad);
            double x2 = x1;
            double y2 = y1 * sinP + z1 * cosP;
            double z2 = y1 * cosP - z1 * sinP;

            depth = (float)z2;

            int scrX = cx + (int)(x2 * scale);
            int scrY = cy + (int)(y2 * scale);
            return new Vector2(scrX, scrY);
        }

        // =========================================================================
        // 3D RENDERING PASSES (Buffer Based)
        // =========================================================================

        private static void Draw3DEclipticGrid(CanvasPixelBuffer buf, int cx, int cy, double scale, double viewRadiusKm, float pitch, float yaw, bool isInterplanetary = true) {
            Color axisColor = new Color(0.12f, 0.16f, 0.24f, 0.7f);
            Color vernalColor = new Color(0.24f, 0.40f, 0.65f, 0.9f); // +X Vernal Equinox / Primary Reference Axis
            Color ringColor = new Color(0.10f, 0.15f, 0.22f, 0.55f);

            // 1. Primary Coordinate Axes (+X Vernal Equinox, +Y 90° Ecliptic, +Z North Pole)
            double axisLenKm = viewRadiusKm * 0.9;
            Vector2 originScr = Project3D(Vector3d.zero, scale, cx, cy, pitch, yaw, out _);
            Vector2 posXScr = Project3D(new Vector3d(axisLenKm, 0, 0), scale, cx, cy, pitch, yaw, out _);
            Vector2 negXScr = Project3D(new Vector3d(-axisLenKm, 0, 0), scale, cx, cy, pitch, yaw, out _);
            Vector2 posYScr = Project3D(new Vector3d(0, axisLenKm, 0), scale, cx, cy, pitch, yaw, out _);
            Vector2 negYScr = Project3D(new Vector3d(0, -axisLenKm, 0), scale, cx, cy, pitch, yaw, out _);
            Vector2 posZScr = Project3D(new Vector3d(0, 0, axisLenKm * 0.35), scale, cx, cy, pitch, yaw, out _);

            DrawSmoothLine(buf, (int)originScr.x, (int)originScr.y, (int)posXScr.x, (int)posXScr.y, vernalColor);
            DrawSmoothLine(buf, (int)originScr.x, (int)originScr.y, (int)negXScr.x, (int)negXScr.y, axisColor);
            DrawSmoothLine(buf, (int)originScr.x, (int)originScr.y, (int)posYScr.x, (int)posYScr.y, axisColor);
            DrawSmoothLine(buf, (int)originScr.x, (int)originScr.y, (int)negYScr.x, (int)negYScr.y, axisColor);

            // Normal Vector (+Z)
            if (Math.Abs(pitch) < 82f) {
                Color zCol = new Color(0.35f, 0.25f, 0.55f, 0.6f);
                DrawSmoothLine(buf, (int)originScr.x, (int)originScr.y, (int)posZScr.x, (int)posZScr.y, zCol);
                DrawBitmapString(buf, (int)posZScr.x + 3, (int)posZScr.y, "+Z", zCol);
            }

            // 2. Concentric Reference Rings (AU in Interplanetary mode, KM in Planetary system mode)
            List<double> ringsKm = new List<double>();
            if (isInterplanetary) {
                double[] auGuides = new double[] { 0.5, 1.0, 1.52, 5.2, 9.58, 19.2, 30.0 };
                for (int a = 0; a < auGuides.Length; a++) ringsKm.Add(auGuides[a] * AU_KM);
            } else {
                double[] kmGuides = new double[] { 10000.0, 20000.0, 50000.0, 100000.0, 200000.0, 400000.0, 800000.0, 1500000.0, 3000000.0 };
                for (int k = 0; k < kmGuides.Length; k++) ringsKm.Add(kmGuides[k]);
            }

            foreach (var rKm in ringsKm) {
                if (rKm > viewRadiusKm * 1.8 || rKm < viewRadiusKm * 0.04) continue;

                int segs = 140;
                Vector2 prevP = Vector2.zero;
                for (int i = 0; i <= segs; i++) {
                    if ((i / 3) % 2 == 0) { // Dash
                        double ang = (i % segs) * (2.0 * Math.PI / segs);
                        Vector3d pt = new Vector3d(rKm * Math.Cos(ang), rKm * Math.Sin(ang), 0);
                        Vector2 curP = Project3D(pt, scale, cx, cy, pitch, yaw, out _);
                        if (prevP != Vector2.zero) {
                            DrawSmoothLine(buf, (int)prevP.x, (int)prevP.y, (int)curP.x, (int)curP.y, ringColor);
                        }
                        prevP = curP;
                    } else {
                        prevP = Vector2.zero;
                    }
                }
            }
        }

        private static bool IsMajorPlanetOrMissionBody(string name, MGASolution solution) {
            if (string.IsNullOrEmpty(name)) return false;
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                if (solution.Legs.Any(l => l.FromBody.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                                           l.ToBody.Equals(name, StringComparison.OrdinalIgnoreCase))) {
                    return true;
                }
            }
            string n = name.ToLowerInvariant();
            if (n == "mercury" || n == "venus" || n == "earth" || n == "moon" || n == "luna" || n == "mars" || 
                n == "jupiter" || n == "saturn" || n == "uranus" || n == "neptune" || n == "pluto") {
                return true;
            }
            return false;
        }

        private static void Draw3DPlanetarySystem(CanvasPixelBuffer buf, int cx, int cy, double scale, double viewRadiusKm, double ut, float pitch, float yaw, MGASolution solution, ScreenLabelManager labelMgr = null, bool isInterplanetary = true, string centralBody = "Sun", double muCentral = 0) {
            Color defaultOrbitColor = new Color(0.18f, 0.24f, 0.35f, 0.55f);
            Color activeOrbitColor = new Color(0.28f, 0.45f, 0.68f, 0.85f);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;

            if (isInterplanetary) {
                var planets = SolarSystemCatalog.GetAvailablePlanets();
                foreach (var p in planets) {
                    if (p.SMA <= 0 || p.SMA > viewRadiusKm * 2.2) continue;
                    if (!IsMajorPlanetOrMissionBody(p.Name, solution)) continue;

                    bool isMissionActive = solution != null && solution.Legs != null && 
                        solution.Legs.Any(l => l.FromBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || l.ToBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase));

                    Color orbitColor = isMissionActive ? activeOrbitColor : defaultOrbitColor;

                    CelestialBody cb = null;
                    if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                        cb = FlightGlobals.Bodies.Find(b => b.name.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                    }

                    // 1. 严格使用天体真实星历连续采样绘制开普勒轨道环 (提升至 160 采样点，消除多边形折线感)
                    int segs = 160;
                    Vector2 prevScr = Vector2.zero;
                    double period = (cb != null && cb.orbit != null && cb.orbit.period > 0) 
                        ? cb.orbit.period 
                        : PykepBridge.CalcOrbitPeriodSec(p.SMA, SolarSystemCatalog.SunMu);

                    for (int i = 0; i <= segs; i++) {
                        double sampleUT = ut + (i / (double)segs) * period;
                        Vector3d pos3D;
                        if (cb != null && cb.orbit != null) {
                            pos3D = cb.orbit.getRelativePositionAtUT(sampleUT) / 1000.0;
                        } else {
                            p.GetHeliocentricState(sampleUT, out pos3D, out _);
                        }

                        Vector2 scrP = Project3D(pos3D, scale, cx, cy, pitch, yaw, out _);
                        if (prevScr != Vector2.zero) {
                            DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)scrP.x, (int)scrP.y, orbitColor);
                        }
                        prevScr = scrP;
                    }

                    // 2. 绘制天体在当前任务历元下的 3D 空间球与图标标签 (包含微向日侧高光球体质感)
                    p.GetHeliocentricState(ut, out Vector3d bPosKm, out _);
                    Vector2 bScr = Project3D(bPosKm, scale, cx, cy, pitch, yaw, out _);

                    if (bScr.x >= -30 && bScr.x < buf.Width + 30 && bScr.y >= -30 && bScr.y < buf.Height + 30) {
                        Color pCol = PlanetColors.ContainsKey(p.Name) ? PlanetColors[p.Name] : new Color(0.7f, 0.8f, 0.9f, 1f);
                        
                        int pRad = isMissionActive ? 5 : 4;
                        if (p.Name == "Jupiter" || p.Name == "Saturn") pRad = 6;
                        else if (p.Name == "Uranus" || p.Name == "Neptune") pRad = 5;
                        else if (p.Name == "Mercury" || p.Name == "Pluto") pRad = 3;

                        // Planet Atmosphere Halo & Core
                        Color haloCol = new Color(pCol.r, pCol.g, pCol.b, 0.32f);
                        DrawAACircle(buf, (int)bScr.x, (int)bScr.y, pRad + 2, haloCol);
                        DrawFilledCircleAA(buf, (int)bScr.x, (int)bScr.y, pRad, pCol);

                        // 3D Directional Specular Highlight (向日面微高光，赋予真实行星球体光影)
                        Vector2 toSun = new Vector2(cx - bScr.x, cy - bScr.y).normalized;
                        if (pRad >= 4) {
                            int hx = (int)(bScr.x + toSun.x * (pRad * 0.35f));
                            int hy = (int)(bScr.y + toSun.y * (pRad * 0.35f));
                            DrawFilledCircleAA(buf, hx, hy, Math.Max(1, (int)(pRad * 0.42f)), new Color(1f, 1f, 1f, 0.28f));
                        }

                        // Saturn Ring in 3D
                        if (p.Name == "Saturn") {
                            DrawRingEllipse(buf, (int)bScr.x, (int)bScr.y, pRad + 5, pRad + 1, new Color(pCol.r, pCol.g, pCol.b, 0.75f));
                        }
                        // Earth Atmosphere Halo
                        if (p.Name == "Earth") {
                            DrawAACircle(buf, (int)bScr.x, (int)bScr.y, pRad + 3, new Color(0.35f, 0.75f, 1.0f, 0.45f));
                        }

                        // Render Planet Name Tag with Drop Shadow & Collision Avoidance
                        bool isMissionBody = solution != null && solution.Legs != null && 
                            solution.Legs.Any(l => l.FromBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || l.ToBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                        if (!isMissionBody) {
                            Color textCol = new Color(0.85f, 0.92f, 1.0f, 0.85f);
                            if (labelMgr != null) {
                                labelMgr.TryPlaceLabel(buf, (int)bScr.x, (int)bScr.y, p.Name, textCol, priority: 4, force: false);
                            } else {
                                DrawBitmapStringWithShadow(buf, (int)bScr.x + pRad + 3, (int)bScr.y - 3, p.Name, textCol);
                            }
                        }
                    }
                }
            } else {
                // =========================================================================
                // 行星系模式：中心为母行星 (如 Earth / Jupiter)，周围环绕绘制其所有子卫星 (Moons)
                // =========================================================================
                var moons = SolarSystemCatalog.GetMoonsOf(centralBody);
                foreach (var m in moons) {
                    if (m.SMA <= 0) continue;

                    bool isMissionActive = solution != null && solution.Legs != null && 
                        solution.Legs.Any(l => l.FromBody.Equals(m.Name, StringComparison.OrdinalIgnoreCase) || l.ToBody.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
                    Color orbitColor = isMissionActive ? activeOrbitColor : defaultOrbitColor;

                    // 1. 绘制卫星绕母星相对开普勒轨道环
                    int segs = 160;
                    Vector2 prevScr = Vector2.zero;
                    double period = PykepBridge.CalcOrbitPeriodSec(m.SMA, muCentral);

                    for (int i = 0; i <= segs; i++) {
                        double sampleUT = ut + (i / (double)segs) * period;
                        m.GetParentCentricState(sampleUT, out Vector3d pos3D, out _);

                        Vector2 scrP = Project3D(pos3D, scale, cx, cy, pitch, yaw, out _);
                        if (prevScr != Vector2.zero) {
                            DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)scrP.x, (int)scrP.y, orbitColor);
                        }
                        prevScr = scrP;
                    }

                    // 2. 绘制卫星当前历元下的空间位置与本体
                    m.GetParentCentricState(ut, out Vector3d mPosKm, out _);
                    Vector2 mScr = Project3D(mPosKm, scale, cx, cy, pitch, yaw, out _);

                    if (mScr.x >= -30 && mScr.x < buf.Width + 30 && mScr.y >= -30 && mScr.y < buf.Height + 30) {
                        Color mCol = PlanetColors.ContainsKey(m.Name) ? PlanetColors[m.Name] : new Color(0.75f, 0.82f, 0.90f, 1f);
                        int mRad = isMissionActive ? 5 : 4;

                        Color haloCol = new Color(mCol.r, mCol.g, mCol.b, 0.35f);
                        DrawAACircle(buf, (int)mScr.x, (int)mScr.y, mRad + 2, haloCol);
                        DrawFilledCircleAA(buf, (int)mScr.x, (int)mScr.y, mRad, mCol);

                        // 向心微高光
                        Vector2 toCenter = new Vector2(cx - mScr.x, cy - mScr.y).normalized;
                        if (mRad >= 4) {
                            int hx = (int)(mScr.x + toCenter.x * (mRad * 0.3f));
                            int hy = (int)(mScr.y + toCenter.y * (mRad * 0.3f));
                            DrawFilledCircleAA(buf, hx, hy, Math.Max(1, (int)(mRad * 0.4f)), new Color(1f, 1f, 1f, 0.3f));
                        }

                        // 卫星标签 (防遮挡)
                        bool isMissionBody = solution != null && solution.Legs != null && 
                            solution.Legs.Any(l => l.FromBody.Equals(m.Name, StringComparison.OrdinalIgnoreCase) || l.ToBody.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
                        if (!isMissionBody) {
                            Color textCol = new Color(0.85f, 0.92f, 1.0f, 0.85f);
                            if (labelMgr != null) {
                                labelMgr.TryPlaceLabel(buf, (int)mScr.x, (int)mScr.y, m.Name, textCol, priority: 3, force: false);
                            } else {
                                DrawBitmapStringWithShadow(buf, (int)mScr.x + mRad + 3, (int)mScr.y - 3, m.Name, textCol);
                            }
                        }
                    }
                }
            }
        }

        private static void DrawCentralPlanet(CanvasPixelBuffer buf, int cx, int cy, string bodyName, ScreenLabelManager labelMgr = null) {
            Color pCol = PlanetColors.ContainsKey(bodyName) ? PlanetColors[bodyName] : new Color(0.35f, 0.65f, 1.0f, 1.0f);
            int pRad = 8;
            if (bodyName.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) || bodyName.Equals("Saturn", StringComparison.OrdinalIgnoreCase)) pRad = 11;
            else if (bodyName.Equals("Uranus", StringComparison.OrdinalIgnoreCase) || bodyName.Equals("Neptune", StringComparison.OrdinalIgnoreCase)) pRad = 9;

            // Halo & Atmospheric glow
            Color haloCol = new Color(pCol.r, pCol.g, pCol.b, 0.28f);
            DrawSoftGlow(buf, cx, cy, pRad + 6, haloCol);
            DrawAACircle(buf, cx, cy, pRad + 3, new Color(pCol.r, pCol.g, pCol.b, 0.60f));
            DrawFilledCircleAA(buf, cx, cy, pRad, pCol);

            // Specular Highlight
            DrawFilledCircleAA(buf, cx + (int)(pRad * 0.35f), cy + (int)(pRad * 0.35f), Math.Max(1, (int)(pRad * 0.4f)), new Color(1f, 1f, 1f, 0.35f));

            // Saturn Ring
            if (bodyName.Equals("Saturn", StringComparison.OrdinalIgnoreCase)) {
                DrawRingEllipse(buf, cx, cy, pRad + 8, pRad + 2, new Color(pCol.r, pCol.g, pCol.b, 0.75f));
            }

            // Name Label
            Color textCol = new Color(0.90f, 0.95f, 1.0f, 0.95f);
            string dName = bodyName.ToUpperInvariant();
            if (labelMgr != null) {
                labelMgr.TryPlaceLabel(buf, cx, cy, dName, textCol, priority: 1, force: true);
            } else {
                DrawBitmapStringWithShadow(buf, cx + pRad + 4, cy - 3, dName, textCol);
            }
        }

        private static void DrawSun(CanvasPixelBuffer buf, int cx, int cy) {
            DrawSoftGlow(buf, cx, cy, 22, new Color(SunGlowOuter.r, SunGlowOuter.g, SunGlowOuter.b, 0.12f));
            DrawSoftGlow(buf, cx, cy, 14, SunCoronaColor);
            DrawSoftGlow(buf, cx, cy, 8, new Color(1.0f, 0.85f, 0.35f, 0.65f));
            DrawFilledCircleAA(buf, cx, cy, 5, SunCoreColor);
            DrawBitmapStringWithShadow(buf, cx + 8, cy - 3, "Sol", new Color(1.0f, 0.95f, 0.65f, 0.95f));
        }

        private static void DrawSpacecraftProbe(CanvasPixelBuffer buf, int cx, int cy, Color col, Vector2 velDir) {
            // 1. Soft glowing aura around probe
            DrawSoftGlow(buf, cx, cy, 9, new Color(col.r, col.g, col.b, 0.45f));

            // 2. Velocity heading vector line
            if (velDir.sqrMagnitude > 0.01f) {
                Vector2 n = velDir.normalized;
                int hx = cx + (int)(n.x * 16);
                int hy = cy + (int)(n.y * 16);
                DrawSmoothLine(buf, cx, cy, hx, hy, new Color(1.0f, 0.90f, 0.25f, 0.95f)); // Bright Amber Velocity Vector
                DrawFilledCircleAA(buf, hx, hy, 2, new Color(1.0f, 1.0f, 1.0f, 1.0f));
            }

            // 3. Diamond satellite body
            int rad = 5;
            DrawSmoothLine(buf, cx - rad, cy, cx, cy + rad, col);
            DrawSmoothLine(buf, cx, cy + rad, cx + rad, cy, col);
            DrawSmoothLine(buf, cx + rad, cy, cx, cy - rad, col);
            DrawSmoothLine(buf, cx, cy - rad, cx - rad, cy, col);

            // 4. White probe core
            DrawFilledCircleAA(buf, cx, cy, 2, new Color(1f, 1f, 1f, 1f));
        }

        private static void Draw3DTransferLegs(
            CanvasPixelBuffer buf, 
            MGASolution solution, 
            int cx, int cy, 
            double scale, 
            double viewRadiusKm, 
            bool showSoi, 
            float pitch, float yaw, 
            float flowProgress, 
            ScreenLabelManager labelMgr = null, 
            Vector2 targetReticleScr = default,
            CraftTelemetry telem = default) {
            double muCentral = (telem.IsInterplanetary || string.IsNullOrEmpty(telem.CentralBody)) 
                ? SolarSystemCatalog.SunMu 
                : SolarSystemCatalog.GetCentralMu(telem.CentralBody);
            if (muCentral <= 0) muCentral = SolarSystemCatalog.SunMu;
            int totalLegs = solution.Legs.Count;
            bool isAll = (flowProgress <= 0.0001f || flowProgress > totalLegs);
            
            int activeLegIdx = isAll ? totalLegs - 1 : Mathf.Clamp((int)Math.Floor(flowProgress), 0, totalLegs - 1);
            float activeFrac = isAll ? 1.0f : (flowProgress - activeLegIdx);
            if (!isAll && flowProgress >= totalLegs) {
                activeLegIdx = totalLegs - 1;
                activeFrac = 1.0f;
            }

            Vector3d craftPos = telem.HasCraft ? telem.PositionKm : Vector3d.zero;
            Vector3d craftVel = telem.HasCraft ? telem.VelocityKmS : Vector3d.zero;
            bool hasCraft = !isAll && telem.HasCraft;

            for (int k = 0; k < totalLegs; k++) {
                var leg = solution.Legs[k];
                if (double.IsNaN(leg.DeparturePosKm.x) || double.IsNaN(leg.ArrivalPosKm.x)) continue;

                Color legCol = LegColors[k % LegColors.Length];

                bool isPast = !isAll && (k < activeLegIdx);
                bool isActive = !isAll && (k == activeLegIdx);
                bool isFuture = !isAll && (k > activeLegIdx);

                Vector3d r0 = leg.DeparturePosKm;
                Vector3d v0 = leg.DepartureVelKms;
                double totalDt = leg.ArrivalUT - leg.DepartureUT;
                if (totalDt <= 0) continue;

                Vector3d hVec = Vector3d.Cross(r0, v0);
                double hMag = hVec.magnitude;
                if (hMag < 1e-5) continue;

                double r0Mag = r0.magnitude;
                double v0Mag = v0.magnitude;
                Vector3d eVec = (Vector3d.Cross(v0, hVec) / muCentral) - (r0 / r0Mag);
                double ecc = eVec.magnitude;
                double energy = (v0Mag * v0Mag * 0.5) - (muCentral / r0Mag);
                double a = -muCentral / (2.0 * energy);

                Vector3d P = (ecc > 1e-6) ? (eVec / ecc) : r0.normalized;
                Vector3d W = hVec / hMag;
                Vector3d Q = Vector3d.Cross(W, P).normalized;

                double cosNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, P), -1f, 1f);
                double sinNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, Q), -1f, 1f);
                double nu0 = Math.Atan2(sinNu0, cosNu0);
                if (nu0 < 0) nu0 += 2.0 * Math.PI;

                int steps = 120;
                Vector2 prevScr = Project3D(r0, scale, cx, cy, pitch, yaw, out _);

                // Compute craft position and velocity if this is the active leg
                if (isActive) {
                    double dtActive = activeFrac * totalDt;
                    if (ecc < 1.0 && a > 0) {
                        double E0 = 2.0 * Math.Atan2(Math.Sqrt(1.0 - ecc) * Math.Sin(nu0 / 2.0), Math.Sqrt(1.0 + ecc) * Math.Cos(nu0 / 2.0));
                        double M0 = E0 - ecc * Math.Sin(E0);
                        double n = Math.Sqrt(muCentral / (a * a * a));
                        double Mc = M0 + n * dtActive;
                        double Ec = Mc;
                        for (int it = 0; it < 14; it++) {
                            double f = Ec - ecc * Math.Sin(Ec) - Mc;
                            double fp = 1.0 - ecc * Math.Cos(Ec);
                            double dE = -f / fp;
                            Ec += dE;
                            if (Math.Abs(dE) < 1e-9) break;
                        }
                        double nuc = 2.0 * Math.Atan2(Math.Sqrt(1.0 + ecc) * Math.Sin(Ec / 2.0), Math.Sqrt(1.0 - ecc) * Math.Cos(Ec / 2.0));
                        double rMagC = a * (1.0 - ecc * Math.Cos(Ec));
                        craftPos = (activeFrac >= 0.999f) ? leg.ArrivalPosKm : ((rMagC * Math.Cos(nuc)) * P + (rMagC * Math.Sin(nuc)) * Q);
                        double vMagFactor = Math.Sqrt(muCentral * a) / Math.Max(1.0, rMagC);
                        craftVel = vMagFactor * (-Math.Sin(Ec) * P + Math.Sqrt(Math.Max(0, 1.0 - ecc * ecc)) * Math.Cos(Ec) * Q);
                    } else if (ecc > 1.0001 && energy > 0) {
                        double a_h = muCentral / (2.0 * energy);
                        double n_h = Math.Sqrt(muCentral / (a_h * a_h * a_h));
                        double sinH0 = Math.Sqrt(ecc * ecc - 1.0) * Math.Sin(nu0) / (1.0 + ecc * Math.Cos(nu0));
                        double H0 = Math.Log(sinH0 + Math.Sqrt(sinH0 * sinH0 + 1.0));
                        double Mh0 = ecc * Math.Sinh(H0) - H0;
                        double Mhc = Mh0 + n_h * dtActive;
                        double Hc = H0;
                        for (int it = 0; it < 16; it++) {
                            double sH = Math.Sinh(Hc);
                            double cH = Math.Cosh(Hc);
                            double f = ecc * sH - Hc - Mhc;
                            double fp = ecc * cH - 1.0;
                            double dH = -f / fp;
                            Hc += dH;
                            if (Math.Abs(dH) < 1e-9) break;
                        }
                        double xP = a_h * (ecc - Math.Cosh(Hc));
                        double yQ = a_h * Math.Sqrt(ecc * ecc - 1.0) * Math.Sinh(Hc);
                        craftPos = (activeFrac >= 0.999f) ? leg.ArrivalPosKm : (xP * P + yQ * Q);
                        double rMagC = craftPos.magnitude;
                        double vMagSq = muCentral * (2.0 / Math.Max(1.0, rMagC) + 1.0 / a_h);
                        craftVel = (P * (-Math.Sinh(Hc)) + Q * Math.Cosh(Hc)).normalized * Math.Sqrt(Math.Max(0, vMagSq));
                    } else {
                        craftPos = Vector3d.Lerp(r0, leg.ArrivalPosKm, activeFrac);
                        craftVel = (leg.ArrivalPosKm - r0).normalized * v0.magnitude;
                    }
                }

                // Render the Keplerian arc points
                if (ecc < 1.0 && a > 0) {
                    double E0 = 2.0 * Math.Atan2(Math.Sqrt(1.0 - ecc) * Math.Sin(nu0 / 2.0), Math.Sqrt(1.0 + ecc) * Math.Cos(nu0 / 2.0));
                    double M0 = E0 - ecc * Math.Sin(E0);
                    double n = Math.Sqrt(muCentral / (a * a * a));

                    int activeStepLimit = isActive ? Mathf.Clamp((int)Math.Round(activeFrac * steps), 0, steps) : steps;

                    for (int s = 1; s <= steps; s++) {
                        double dt = (s / (double)steps) * totalDt;
                        double M = M0 + n * dt;
                        double E = M;
                        for (int iter = 0; iter < 12; iter++) {
                            double f = E - ecc * Math.Sin(E) - M;
                            double fPrime = 1.0 - ecc * Math.Cos(E);
                            double dE = -f / fPrime;
                            E += dE;
                            if (Math.Abs(dE) < 1e-8) break;
                        }

                        double nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + ecc) * Math.Sin(E / 2.0), Math.Sqrt(1.0 - ecc) * Math.Cos(E / 2.0));
                        double rMag = a * (1.0 - ecc * Math.Cos(E));
                        Vector3d rPos = (s == steps) ? leg.ArrivalPosKm : ((rMag * Math.Cos(nu)) * P + (rMag * Math.Sin(nu)) * Q);
                        Vector2 nextScr = Project3D(rPos, scale, cx, cy, pitch, yaw, out _);

                        if (isAll || isPast) {
                            DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                        } else if (isActive) {
                            if (s <= activeStepLimit) {
                                DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                            } else if (s % 2 == 0) {
                                // Faint dashed preview of remaining path in current leg
                                DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, new Color(legCol.r, legCol.g, legCol.b, 0.22f));
                            }
                        } else if (isFuture) {
                            // Faint dashed preview of future legs
                            if (s % 3 == 0) {
                                DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, new Color(legCol.r, legCol.g, legCol.b, 0.18f));
                            }
                        }
                        prevScr = nextScr;
                    }
                } else if (ecc > 1.0001 && energy > 0) {
                    double a_h = muCentral / (2.0 * energy);
                    double n_h = Math.Sqrt(muCentral / (a_h * a_h * a_h));
                    double sinH0 = Math.Sqrt(ecc * ecc - 1.0) * Math.Sin(nu0) / (1.0 + ecc * Math.Cos(nu0));
                    double H0 = Math.Log(sinH0 + Math.Sqrt(sinH0 * sinH0 + 1.0));
                    double Mh0 = ecc * Math.Sinh(H0) - H0;

                    int activeStepLimit = isActive ? Mathf.Clamp((int)Math.Round(activeFrac * steps), 0, steps) : steps;
                    double prevH = H0;
                    for (int s = 1; s <= steps; s++) {
                        double dt = (s / (double)steps) * totalDt;
                        double Mh = Mh0 + n_h * dt;
                        double H = prevH;
                        for (int iter = 0; iter < 16; iter++) {
                            double sH = Math.Sinh(H);
                            double cH = Math.Cosh(H);
                            double f = ecc * sH - H - Mh;
                            double fp = ecc * cH - 1.0;
                            double dH = -f / fp;
                            H += dH;
                            if (Math.Abs(dH) < 1e-9) break;
                        }
                        prevH = H;

                        double xP = a_h * (ecc - Math.Cosh(H));
                        double yQ = a_h * Math.Sqrt(ecc * ecc - 1.0) * Math.Sinh(H);
                        Vector3d rPos = (s == steps) ? leg.ArrivalPosKm : (xP * P + yQ * Q);
                        Vector2 nextScr = Project3D(rPos, scale, cx, cy, pitch, yaw, out _);

                        if (isAll || isPast) {
                            DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                        } else if (isActive) {
                            if (s <= activeStepLimit) {
                                DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                            } else if (s % 2 == 0) {
                                DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, new Color(legCol.r, legCol.g, legCol.b, 0.22f));
                            }
                        } else if (isFuture) {
                            if (s % 3 == 0) {
                                DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, new Color(legCol.r, legCol.g, legCol.b, 0.18f));
                            }
                        }
                        prevScr = nextScr;
                    }
                } else {
                    int activeStepLimit = isActive ? Mathf.Clamp((int)Math.Round(activeFrac * steps), 0, steps) : steps;
                    for (int s = 1; s <= steps; s++) {
                        double frac = s / (double)steps;
                        Vector3d rPos = Vector3d.Lerp(r0, leg.ArrivalPosKm, (float)frac);
                        Vector2 nextScr = Project3D(rPos, scale, cx, cy, pitch, yaw, out _);
                        if (isAll || isPast || (isActive && s <= activeStepLimit)) {
                            DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                        } else if (s % 2 == 0) {
                            DrawSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, new Color(legCol.r, legCol.g, legCol.b, 0.20f));
                        }
                        prevScr = nextScr;
                    }
                }

                // Departure Node: draw if isAll or if leg has begun (k <= activeLegIdx)
                if (isAll || k <= activeLegIdx) {
                    Vector2 depScr = Project3D(leg.DeparturePosKm, scale, cx, cy, pitch, yaw, out _);
                    DrawGlowCircle(buf, (int)depScr.x, (int)depScr.y, 5, legCol);
                    if (leg.FromBody.Equals("Saturn", StringComparison.OrdinalIgnoreCase)) {
                        DrawRingEllipse(buf, (int)depScr.x, (int)depScr.y, 11, 3, new Color(0.92f, 0.82f, 0.58f, 0.75f));
                    }

                    // 1. 目标框互斥去重：若该节点正处于 Target Reticle 准星范围，由 Reticle 统一高亮展示标题，绝不重复印字
                    bool nearReticle = labelMgr != null && labelMgr.IsNearReticle((int)depScr.x, (int)depScr.y, targetReticleScr, 16f);

                    // 2. 时间维度自适应隐藏 (根据时间自动隐藏不需要的标签)：
                    // - 播放动画/拖动进度条 (!isAll):
                    //   仅高亮当前活动机动 (k == activeLegIdx) 以及紧邻的下一个借力点 (k == activeLegIdx + 1)；
                    //   过去已完成的陈年旧借力点 (k < activeLegIdx) 与未到达的远期借力点 (k > activeLegIdx + 1)：自动隐藏冗余文字！
                    // - 全局概览 (isAll):
                    //   所有节点参与智能排版避让；若缩放到内行星极度密集区域，避让器自动隐藏次要文字，只留光晕小点！
                    bool showNodeText = !nearReticle;
                    if (!isAll) {
                        bool isCurrentLeg = (k == activeLegIdx);
                        bool isNextLeg = (k == activeLegIdx + 1);
                        bool isFreshLaunch = (k == 0 && activeLegIdx <= 1);
                        showNodeText = showNodeText && (isCurrentLeg || isNextLeg || isFreshLaunch);
                    }

                    if (showNodeText) {
                        string badgeLabel = (k == 0) ? $"[1] {leg.FromBody}" : $"[{k + 1}] {leg.FromBody}";
                        if (labelMgr != null) {
                            labelMgr.TryPlaceLabel(buf, (int)depScr.x, (int)depScr.y, badgeLabel, legCol, priority: (k == activeLegIdx ? 2 : 3), force: false);
                        } else {
                            DrawBitmapStringWithShadow(buf, (int)depScr.x + 7, (int)depScr.y + 2, badgeLabel, legCol);
                        }
                    }

                    if (showSoi) {
                        var bodyPlanet = SolarSystemCatalog.GetPlanet(leg.FromBody);
                        double approxSoiKm = (bodyPlanet != null && bodyPlanet.SoiKm > 0 && !double.IsInfinity(bodyPlanet.SoiKm))
                            ? bodyPlanet.SoiKm
                            : (bodyPlanet != null ? bodyPlanet.SMA * Math.Pow(bodyPlanet.Mu / muCentral, 0.4) : 1e6);
                        int soiPix = Math.Max(4, (int)(approxSoiKm * scale));
                        DrawAACircle(buf, (int)depScr.x, (int)depScr.y, soiPix, new Color(legCol.r, legCol.g, legCol.b, 0.35f));
                    }
                }

                // DSM node
                if (leg.HasDSM && leg.DSM != null && !double.IsNaN(leg.DSMPositionKm.x)) {
                    // In scrubber mode: show DSM if past or if on active leg and passed DSM epoch
                    bool showDsm = isAll || isPast || (isActive && activeFrac >= (leg.DSM.EpochOffsetSec / totalDt));
                    if (showDsm) {
                        Vector2 dsmScr = Project3D(leg.DSMPositionKm, scale, cx, cy, pitch, yaw, out _);
                        Color dsmCol = new Color(0.98f, 0.72f, 0.15f, 1.0f);
                        DrawGlowCircle(buf, (int)dsmScr.x, (int)dsmScr.y, 5, dsmCol);
                        
                        bool nearReticle = labelMgr != null && labelMgr.IsNearReticle((int)dsmScr.x, (int)dsmScr.y, targetReticleScr, 16f);
                        bool showDsmText = !nearReticle && (isAll || isActive);
                        if (showDsmText) {
                            string dsmLabel = $"[DSM] {leg.DSM.DeltaVMs:F0}M/S";
                            if (labelMgr != null) {
                                labelMgr.TryPlaceLabel(buf, (int)dsmScr.x, (int)dsmScr.y, dsmLabel, dsmCol, priority: 2, force: false);
                            } else {
                                DrawBitmapStringWithShadow(buf, (int)dsmScr.x + 7, (int)dsmScr.y - 4, dsmLabel, dsmCol);
                            }
                        }
                    }
                }

                // Arrival Node:
                // 【彻底消除重复】：仅在整个飞行链的最终 Destination (k == totalLegs - 1) 绘制到达标记与 [END] 标签！
                // 中间所有交接天体的借力标签已在 Departure 中唯一绘制，杜绝到达与出发在同一像素重复打印！
                if (k == totalLegs - 1 && (isAll || isPast || (isActive && activeFrac >= 0.999f))) {
                    Vector2 arrScr = Project3D(leg.ArrivalPosKm, scale, cx, cy, pitch, yaw, out _);
                    Color arrCol = new Color(0.98f, 0.35f, 0.35f, 1.0f);
                    DrawGlowCircle(buf, (int)arrScr.x, (int)arrScr.y, 6, arrCol);
                    
                    bool nearReticle = labelMgr != null && labelMgr.IsNearReticle((int)arrScr.x, (int)arrScr.y, targetReticleScr, 16f);
                    if (!nearReticle) {
                        string arrLabel = $"[END] {leg.ToBody}";
                        if (labelMgr != null) {
                            labelMgr.TryPlaceLabel(buf, (int)arrScr.x, (int)arrScr.y, arrLabel, arrCol, priority: 2, force: true);
                        } else {
                            DrawBitmapStringWithShadow(buf, (int)arrScr.x + 8, (int)arrScr.y + 2, arrLabel, arrCol);
                        }
                    }
                }
            }

            // Spacecraft Probe Marker (Rendered on top when scrubbing or animating)
            if (hasCraft && !double.IsNaN(craftPos.x) && craftPos.magnitude > 100.0) {
                Vector2 craftScr = Project3D(craftPos, scale, cx, cy, pitch, yaw, out _);
                Vector2 craftAheadScr = Project3D(craftPos + craftVel.normalized * (viewRadiusKm * 0.08), scale, cx, cy, pitch, yaw, out _);
                Vector2 velDir = craftAheadScr - craftScr;

                DrawSpacecraftProbe(buf, (int)craftScr.x, (int)craftScr.y, new Color(0.25f, 0.92f, 1.0f, 1.0f), velDir);

                // Probe Telemetry HUD Overlay (通过避让器智能排版，绝不与借力点或天体图标叠在一起)
                string speedStr = $"V: {craftVel.magnitude:F1} KM/S";
                string distStr = telem.IsInterplanetary ? $"R: {(craftPos.magnitude / AU_KM):F2} AU" : $"R: {craftPos.magnitude:N0} KM";
                Color hudCol = new Color(0.35f, 0.95f, 1.0f, 0.95f);
                Color distCol = new Color(0.85f, 0.90f, 1.0f, 0.85f);
                if (labelMgr != null) {
                    labelMgr.TryPlaceLabel(buf, (int)craftScr.x, (int)craftScr.y - 12, speedStr, hudCol, priority: 1, force: false);
                    labelMgr.TryPlaceLabel(buf, (int)craftScr.x, (int)craftScr.y + 2, distStr, distCol, priority: 1, force: false);
                } else {
                    DrawBitmapStringWithShadow(buf, (int)craftScr.x + 10, (int)craftScr.y - 12, speedStr, hudCol);
                    DrawBitmapStringWithShadow(buf, (int)craftScr.x + 10, (int)craftScr.y - 2, distStr, distCol);
                }
            }

            // Feature 6: Draw Continuous Low-Thrust Segments
            if (solution.LowThrustSolution != null && solution.LowThrustSolution.SegmentPositionsKm.Count > 1) {
                var pts = solution.LowThrustSolution.SegmentPositionsKm;
                for (int i = 0; i < pts.Count - 1; i++) {
                    Vector2 pA = Project3D(pts[i], scale, cx, cy, pitch, yaw, out _);
                    Vector2 pB = Project3D(pts[i + 1], scale, cx, cy, pitch, yaw, out _);
                    Color thCol = new Color(0.35f, 0.85f, 0.98f, 0.85f);
                    DrawThickSmoothLine(buf, (int)pA.x, (int)pA.y, (int)pB.x, (int)pB.y, thCol, 2);
                }
            }
        }

        private static void DrawTargetReticle(CanvasPixelBuffer buf, int tx, int ty, string label, Color col, ScreenLabelManager labelMgr = null) {
            int S = 20; // Half size (40x40 frame)
            int L = 7;  // Corner segment length

            Color glowCol = new Color(col.r, col.g, col.b, 0.40f);
            Color brightCol = new Color(1f, 1f, 1f, 0.95f);

            // Top-Left
            DrawSmoothLine(buf, tx - S, ty - S, tx - S + L, ty - S, col);
            DrawSmoothLine(buf, tx - S, ty - S + 1, tx - S + L, ty - S + 1, col);
            DrawSmoothLine(buf, tx - S, ty - S, tx - S, ty - S + L, col);
            DrawSmoothLine(buf, tx - S + 1, ty - S, tx - S + 1, ty - S + L, col);
            // Top-Right
            DrawSmoothLine(buf, tx + S, ty - S, tx + S - L, ty - S, col);
            DrawSmoothLine(buf, tx + S, ty - S + 1, tx + S - L, ty - S + 1, col);
            DrawSmoothLine(buf, tx + S, ty - S, tx + S, ty - S + L, col);
            DrawSmoothLine(buf, tx + S - 1, ty - S, tx + S - 1, ty - S + L, col);
            // Bottom-Left
            DrawSmoothLine(buf, tx - S, ty + S, tx - S + L, ty + S, col);
            DrawSmoothLine(buf, tx - S, ty + S - 1, tx - S + L, ty + S - 1, col);
            DrawSmoothLine(buf, tx - S, ty + S, tx - S, ty + S - L, col);
            DrawSmoothLine(buf, tx - S + 1, ty + S, tx - S + 1, ty + S - L, col);
            // Bottom-Right
            DrawSmoothLine(buf, tx + S, ty + S, tx + S - L, ty + S, col);
            DrawSmoothLine(buf, tx + S, ty + S - 1, tx + S - L, ty + S - 1, col);
            DrawSmoothLine(buf, tx + S, ty + S, tx + S, ty + S - L, col);
            DrawSmoothLine(buf, tx + S - 1, ty + S, tx + S - 1, ty + S - L, col);

            // Crosshairs
            DrawSmoothLine(buf, tx - S - 6, ty, tx - S + 2, ty, col);
            DrawSmoothLine(buf, tx + S - 2, ty, tx + S + 6, ty, col);
            DrawSmoothLine(buf, tx, ty - S - 6, tx, ty - S + 2, col);
            DrawSmoothLine(buf, tx, ty + S - 2, tx, ty + S + 6, col);

            DrawAACircle(buf, tx, ty, 8, glowCol);
            DrawAACircle(buf, tx, ty, 3, brightCol);

            if (!string.IsNullOrEmpty(label)) {
                if (labelMgr != null) {
                    labelMgr.TryPlaceLabel(buf, tx, ty + S + 2, label, col, priority: 0, force: true);
                } else {
                    int labelX = tx - (label.Length * 6) / 2;
                    int labelY = ty + S + 4;
                    DrawBitmapStringWithShadow(buf, labelX, labelY, label, col);
                }
            }
        }

        public static CraftTelemetry ComputeCraftTelemetry(MGASolution solution, float flowProgress, int selectedEventIndex) {
            return MGAStateManager.Current.UpdateTelemetry(flowProgress, selectedEventIndex);
        }

        private static void DrawFilledRect(CanvasPixelBuffer buf, int rx, int ry, int rw, int rh, Color col) {
            int x0 = Math.Max(0, rx);
            int x1 = Math.Min(buf.Width - 1, rx + rw);
            int y0 = Math.Max(0, ry);
            int y1 = Math.Min(buf.Height - 1, ry + rh);
            byte r = (byte)(col.r * 255f);
            byte g = (byte)(col.g * 255f);
            byte b = (byte)(col.b * 255f);
            float a = col.a;
            for (int y = y0; y <= y1; y++) {
                for (int x = x0; x <= x1; x++) {
                    buf.BlendPixelFast(x, y, r, g, b, a);
                }
            }
        }

        private static void DrawRectOutline(CanvasPixelBuffer buf, int rx, int ry, int rw, int rh, Color col) {
            DrawSmoothLine(buf, rx, ry, rx + rw, ry, col);
            DrawSmoothLine(buf, rx, ry + rh, rx + rw, ry + rh, col);
            DrawSmoothLine(buf, rx, ry, rx, ry + rh, col);
            DrawSmoothLine(buf, rx + rw, ry, rx + rw, ry + rh, col);
        }

        private static string FormatUtcEpochDisplay(double ut) {
            if (ut <= 0) return "N/A";
            string s = GMATClient.FormatUtcGregorian(ut);
            if (string.IsNullOrEmpty(s)) return "N/A";
            int dotIdx = s.LastIndexOf('.');
            if (dotIdx > 0 && dotIdx >= s.Length - 4) {
                s = s.Substring(0, dotIdx);
            }
            return s.ToUpperInvariant();
        }

        private static void DrawFlightTelemetryPanel(CanvasPixelBuffer buf, CraftTelemetry telem, MGASolution solution) {
            int panelX = 14;
            int panelW = 236;
            int panelH = 56;
            int panelY = buf.Height - panelH - 12;

            // 1. 半透明暗色科技背景卡片 (Dark Glassmorphism Card)
            DrawFilledRect(buf, panelX, panelY, panelW, panelH, new Color(0.04f, 0.08f, 0.13f, 0.82f));

            // 2. 细线发光边框与高亮饰条 (Border & Cyberpunk Accent)
            Color borderCol = new Color(0.18f, 0.35f, 0.52f, 0.60f);
            DrawRectOutline(buf, panelX, panelY, panelW, panelH, borderCol);

            // 左侧高亮青色能量饰条 (Vertical Cyan Status Bar)
            Color cyanAccent = new Color(0.20f, 0.85f, 1.0f, 0.95f);
            DrawSmoothLine(buf, panelX, panelY, panelX, panelY + panelH, cyanAccent);
            DrawSmoothLine(buf, panelX + 1, panelY, panelX + 1, panelY + panelH, cyanAccent);

            // 科技切角微修饰 (Corner Accents)
            DrawSmoothLine(buf, panelX + panelW - 8, panelY + panelH, panelX + panelW, panelY + panelH, cyanAccent);
            DrawSmoothLine(buf, panelX + panelW, panelY + panelH - 8, panelX + panelW, panelY + panelH, cyanAccent);

            // 3. 文字内容排版
            int line1Y = panelY + 42;
            int line2Y = panelY + 29;
            int line3Y = panelY + 16;
            int line4Y = panelY + 3;

            if (telem.HasCraft) {
                // 实时绿色动态呼吸指示灯 (Active Green LED)
                DrawGlowCircle(buf, panelX + 9, line1Y + 3, 2, new Color(0.22f, 0.96f, 0.55f, 0.95f));

                string stageStr = (telem.ActiveLegIndex >= 0 && telem.TotalLegs > 0)
                    ? $"STAGE [{telem.ActiveLegIndex + 1}/{telem.TotalLegs}] {telem.FromBody.ToUpperInvariant()} > {telem.ToBody.ToUpperInvariant()}"
                    : "STAGE [EN-ROUTE]";
                DrawBitmapStringWithShadow(buf, panelX + 16, line1Y, stageStr, new Color(0.35f, 0.80f, 1.0f, 0.95f));

                // 行 2: 当前时刻历元 (跟随动画/滑块实时刷新)
                string epochStr = $"EPOCH: {FormatUtcEpochDisplay(telem.MissionUt)}";
                DrawBitmapStringWithShadow(buf, panelX + 8, line2Y, epochStr, new Color(0.92f, 0.96f, 1.0f, 0.95f));

                // 行 3: 航天器瞬时速度与任务飞行天数 (VEL & MET)
                string velStr = $"VEL: {telem.SpeedKmS:F2} KM/S";
                string metStr = $"MET: T+{telem.MetDays:F1}D";
                DrawBitmapStringWithShadow(buf, panelX + 8, line3Y, velStr, new Color(0.98f, 0.82f, 0.22f, 0.95f));
                DrawBitmapStringWithShadow(buf, panelX + 130, line3Y, metStr, new Color(0.70f, 0.85f, 0.98f, 0.88f));

                // 行 4: 引力中心距离与到达目标距离 (DIST: X.XX AU 或 KM)
                string distStr = telem.IsInterplanetary 
                    ? $"DIST: {telem.DistanceAu:F2} AU ({telem.DistanceKm:0.#E+0} KM)"
                    : $"DIST: {telem.DistanceKm:N0} KM ({telem.CentralBody?.ToUpperInvariant()})";
                DrawBitmapStringWithShadow(buf, panelX + 8, line4Y, distStr, new Color(0.40f, 0.90f, 0.72f, 0.92f));
            } else {
                // 概览总览模式 (Overview Mode / Solar System Frame)
                DrawGlowCircle(buf, panelX + 9, line1Y + 3, 2, new Color(0.40f, 0.70f, 1.0f, 0.95f));
                string overviewTitle = telem.IsInterplanetary ? "MISSION [SOLAR OVERVIEW]" : $"MISSION [{telem.CentralBody?.ToUpperInvariant()} SYSTEM]";
                DrawBitmapStringWithShadow(buf, panelX + 16, line1Y, overviewTitle, new Color(0.35f, 0.80f, 1.0f, 0.95f));

                string epochStr = $"EPOCH: {FormatUtcEpochDisplay(telem.MissionUt)}";
                DrawBitmapStringWithShadow(buf, panelX + 8, line2Y, epochStr, new Color(0.92f, 0.96f, 1.0f, 0.95f));

                string sysStr = (solution != null && solution.Legs != null && solution.Legs.Count > 0)
                    ? $"LEGS: {solution.Legs.Count} STEPS | DV: {solution.TotalDeltaVKms * 1000.0:F0} M/S"
                    : (telem.IsInterplanetary ? "FRAME: ECLIPJ2000 HELIOCENTRIC" : $"FRAME: {telem.CentralBody?.ToUpperInvariant()}-CENTRIC");
                DrawBitmapStringWithShadow(buf, panelX + 8, line3Y, sysStr, new Color(0.85f, 0.88f, 0.95f, 0.85f));

                string destStr = (solution != null && solution.Legs != null && solution.Legs.Count > 0)
                    ? $"SPAN: {solution.TotalFlightDays:F0}D -> {solution.Legs[solution.Legs.Count - 1].ToBody.ToUpperInvariant()}"
                    : "STATUS: READY";
                DrawBitmapStringWithShadow(buf, panelX + 8, line4Y, destStr, new Color(0.40f, 0.85f, 0.75f, 0.85f));
            }
        }

        private static void DrawHudOverlay(
            CanvasPixelBuffer buf, 
            double viewRadiusKm, 
            double scale, 
            float pitch, 
            float yaw,
            CraftTelemetry telem,
            MGASolution solution) 
        {
            // 1. 左下角比例尺 (Distance Scale Bar)
            if (telem.IsInterplanetary) {
                double auInView = viewRadiusKm / AU_KM;
                double legendAu = 1.0;
                if (auInView > 15.0) legendAu = 10.0;
                else if (auInView > 6.0) legendAu = 5.0;
                else if (auInView > 2.5) legendAu = 2.0;
                else if (auInView < 0.8) legendAu = 0.2;

                int barLenPix = (int)(legendAu * AU_KM * scale);
                if (barLenPix > 10 && barLenPix < buf.Width - 40) {
                    int barX = 20;
                    int barY = 20;
                    Color hudCol = new Color(0.55f, 0.75f, 0.95f, 0.8f);
                    DrawSmoothLine(buf, barX, barY, barX + barLenPix, barY, hudCol);
                    DrawSmoothLine(buf, barX, barY - 3, barX, barY + 3, hudCol);
                    DrawSmoothLine(buf, barX + barLenPix, barY - 3, barX + barLenPix, barY + 3, hudCol);

                    string label = $"{legendAu:G} AU ({legendAu * 1.496e8:0.#E+0} km)";
                    DrawBitmapStringWithShadow(buf, barX, barY + 5, label, hudCol);
                }
            } else {
                double legendKm = 10000.0;
                if (viewRadiusKm > 1000000.0) legendKm = 500000.0;
                else if (viewRadiusKm > 400000.0) legendKm = 200000.0;
                else if (viewRadiusKm > 150000.0) legendKm = 100000.0;
                else if (viewRadiusKm > 50000.0) legendKm = 20000.0;
                else if (viewRadiusKm < 15000.0) legendKm = 5000.0;

                int barLenPix = (int)(legendKm * scale);
                if (barLenPix > 10 && barLenPix < buf.Width - 40) {
                    int barX = 20;
                    int barY = 20;
                    Color hudCol = new Color(0.55f, 0.75f, 0.95f, 0.8f);
                    DrawSmoothLine(buf, barX, barY, barX + barLenPix, barY, hudCol);
                    DrawSmoothLine(buf, barX, barY - 3, barX, barY + 3, hudCol);
                    DrawSmoothLine(buf, barX + barLenPix, barY - 3, barX + barLenPix, barY + 3, hudCol);

                    string label = $"{legendKm:N0} KM ({telem.CentralBody?.ToUpperInvariant()})";
                    DrawBitmapStringWithShadow(buf, barX, barY + 5, label, hudCol);
                }
            }

            // 2. 右上角 3D 视角模式 (Camera View Mode)
            string viewMode = (pitch >= 88f) ? "2D Top-Down (+Z)" : ((pitch <= -88f) ? "2D Bottom-Up (-Z)" : ((Math.Abs(pitch) < 1.5f) ? $"Ecliptic (0° / {yaw:F0}°)" : $"3D ({pitch:F0}° / {yaw:F0}°)"));
            int textW = viewMode.Length * 6;
            DrawBitmapStringWithShadow(buf, buf.Width - textW - 15, buf.Height - 18, viewMode, new Color(0.40f, 0.65f, 0.90f, 0.85f));

            // 3. 右下角操作提示 (Help Controls Hints) - 移至右下角避免与左上角遥测面板冲突
            string helpText = "LMB: Rotate | RMB/Mid: Pan | Scroll: Zoom";
            int helpW = helpText.Length * 6;
            DrawBitmapStringWithShadow(buf, buf.Width - helpW - 15, 20, helpText, new Color(0.30f, 0.42f, 0.58f, 0.75f));

            // 4. 左上角实时航天遥测面板 (Top-Left Dynamic Flight Telemetry HUD)
            DrawFlightTelemetryPanel(buf, telem, solution);
        }

        // =========================================================================
        // HIGH PERFORMANCE BUFFER DRAWING PRIMITIVES
        // =========================================================================

        private static void DrawSoftGlow(CanvasPixelBuffer buf, int cx, int cy, int rad, Color col) {
            for (int dy = -rad; dy <= rad; dy++) {
                for (int dx = -rad; dx <= rad; dx++) {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= rad) {
                        float factor = (1f - (dist / rad));
                        Color c = new Color(col.r, col.g, col.b, col.a * factor * factor);
                        buf.BlendPixel(cx + dx, cy + dy, c);
                    }
                }
            }
        }

        private static void DrawGlowCircle(CanvasPixelBuffer buf, int cx, int cy, int rad, Color col) {
            DrawSoftGlow(buf, cx, cy, rad + 4, new Color(col.r, col.g, col.b, 0.35f));
            DrawFilledCircleAA(buf, cx, cy, rad, col);
        }

        private static void DrawFilledCircleAA(CanvasPixelBuffer buf, int cx, int cy, int rad, Color col) {
            for (int dy = -rad - 1; dy <= rad + 1; dy++) {
                for (int dx = -rad - 1; dx <= rad + 1; dx++) {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= rad - 0.5f) {
                        buf.BlendPixel(cx + dx, cy + dy, col);
                    } else if (dist <= rad + 0.5f) {
                        float coverage = (rad + 0.5f) - dist;
                        Color c = new Color(col.r, col.g, col.b, col.a * coverage);
                        buf.BlendPixel(cx + dx, cy + dy, c);
                    }
                }
            }
        }

        private static void DrawAACircle(CanvasPixelBuffer buf, int cx, int cy, int rad, Color col) {
            int segments = Math.Max(32, Math.Min(120, rad * 3));
            int prevX = -1, prevY = -1;
            for (int i = 0; i <= segments; i++) {
                double a = (i % segments) * (2.0 * Math.PI / segments);
                int px = cx + (int)(rad * Math.Cos(a));
                int py = cy + (int)(rad * Math.Sin(a));
                if (prevX >= 0) DrawSmoothLine(buf, prevX, prevY, px, py, col);
                prevX = px; prevY = py;
            }
        }

        private static void DrawRingEllipse(CanvasPixelBuffer buf, int cx, int cy, int rx, int ry, Color col) {
            int segments = 40;
            int prevX = -1, prevY = -1;
            for (int i = 0; i <= segments; i++) {
                double a = (i % segments) * (2.0 * Math.PI / segments);
                int px = cx + (int)(rx * Math.Cos(a));
                int py = cy + (int)(ry * Math.Sin(a));
                if (prevX >= 0) DrawSmoothLine(buf, prevX, prevY, px, py, col);
                prevX = px; prevY = py;
            }
        }

        /// <summary>
        /// Xiaolin Wu's Anti-Aliased Line Algorithm with Trivial Reject Clipping
        /// </summary>
        private static void DrawSmoothLine(CanvasPixelBuffer buf, int x0, int y0, int x1, int y1, Color col) {
            int w = buf.Width;
            int h = buf.Height;

            // Bounding box trivial rejection
            if ((x0 < 0 && x1 < 0) || (x0 >= w && x1 >= w) || (y0 < 0 && y1 < 0) || (y0 >= h && y1 >= h)) return;

            // Clamp crazy spikes
            if (Math.Abs(x1 - x0) > 4000 || Math.Abs(y1 - y0) > 4000) return;

            bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
            if (steep) {
                int t = x0; x0 = y0; y0 = t;
                t = x1; x1 = y1; y1 = t;
            }
            if (x0 > x1) {
                int t = x0; x0 = x1; x1 = t;
                t = y0; y0 = y1; y1 = t;
            }

            int dx = x1 - x0;
            int dy = y1 - y0;
            float gradient = (dx == 0) ? 1f : (float)dy / dx;

            float y = y0;
            for (int x = x0; x <= x1; x++) {
                int ipartY = (int)y;
                float fpartY = y - ipartY;

                if (steep) {
                    buf.BlendPixel(ipartY, x, new Color(col.r, col.g, col.b, col.a * (1f - fpartY)));
                    buf.BlendPixel(ipartY + 1, x, new Color(col.r, col.g, col.b, col.a * fpartY));
                } else {
                    buf.BlendPixel(x, ipartY, new Color(col.r, col.g, col.b, col.a * (1f - fpartY)));
                    buf.BlendPixel(x, ipartY + 1, new Color(col.r, col.g, col.b, col.a * fpartY));
                }
                y += gradient;
            }
        }

        private static void DrawBitmapStringWithShadow(CanvasPixelBuffer buf, int x, int y, string text, Color col) {
            if (string.IsNullOrEmpty(text)) return;
            Color shadowCol = new Color(0.01f, 0.02f, 0.04f, 0.90f);
            DrawBitmapString(buf, x + 1, y - 1, text, shadowCol);
            DrawBitmapString(buf, x, y, text, col);
        }

        /// <summary>
        /// 连续垂直距离场反走样粗线光栅化 (True Distance-Field Anti-Aliased Line)
        /// 彻底消除卡顿与走样毛刺，呈现圆润发光的现代航天轨迹矢量质感
        /// </summary>
        private static void DrawThickSmoothLine(CanvasPixelBuffer buf, int x0, int y0, int x1, int y1, Color col, float radius) {
            int pad = (int)Math.Ceiling(radius) + 2;
            int minX = Math.Max(0, Math.Min(x0, x1) - pad);
            int maxX = Math.Min(buf.Width - 1, Math.Max(x0, x1) + pad);
            int minY = Math.Max(0, Math.Min(y0, y1) - pad);
            int maxY = Math.Min(buf.Height - 1, Math.Max(y0, y1) + pad);

            if (minX > maxX || minY > maxY) return;

            float vx = x1 - x0;
            float vy = y1 - y0;
            float lenSq = vx * vx + vy * vy;
            if (lenSq < 0.0001f) {
                DrawFilledCircleAA(buf, x0, y0, (int)radius, col);
                return;
            }

            byte rB = (byte)(col.r * 255f);
            byte gB = (byte)(col.g * 255f);
            byte bB = (byte)(col.b * 255f);
            float baseA = col.a;
            float glowR = radius + 1.8f;

            for (int y = minY; y <= maxY; y++) {
                float wy = y - y0;
                for (int x = minX; x <= maxX; x++) {
                    float wx = x - x0;
                    float c1 = wx * vx + wy * vy;
                    float px, py;
                    if (c1 <= 0f) {
                        px = x0; py = y0;
                    } else if (c1 >= lenSq) {
                        px = x1; py = y1;
                    } else {
                        float t = c1 / lenSq;
                        px = x0 + t * vx;
                        py = y0 + t * vy;
                    }
                    float dx = x - px;
                    float dy = y - py;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist <= radius + 0.5f) {
                        float coverage = Mathf.Clamp01((radius + 0.5f) - dist);
                        buf.BlendPixelFast(x, y, rB, gB, bB, baseA * coverage);
                    } else if (dist <= glowR) {
                        float glowCov = (1.0f - (dist - radius) / 1.8f) * 0.28f * baseA;
                        buf.BlendPixelFast(x, y, rB, gB, bB, glowCov);
                    }
                }
            }
        }

        // =========================================================================
        // MINIMAL 5x7 CRISP BITMAP FONT RENDERER ON BUFFER
        // =========================================================================

        private static void DrawBitmapString(CanvasPixelBuffer buf, int x, int y, string text, Color col) {
            if (string.IsNullOrEmpty(text)) return;
            int curX = x;
            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];
                DrawChar5x7(buf, curX, y, ch, col);
                curX += 6; // 5 width + 1 spacing
            }
        }

        private static void DrawChar5x7(CanvasPixelBuffer buf, int x, int y, char c, Color col) {
            byte[] glyph = GetGlyph5x7(c);
            if (glyph == null) return;
            for (int row = 0; row < 7; row++) {
                byte b = glyph[row];
                for (int colIdx = 0; colIdx < 5; colIdx++) {
                    if (((b >> (4 - colIdx)) & 1) == 1) {
                        buf.BlendPixel(x + colIdx, y + (6 - row), col);
                    }
                }
            }
        }

        private static byte[] GetGlyph5x7(char c) {
            switch (char.ToUpperInvariant(c)) {
                case 'A': return new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
                case 'B': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E };
                case 'C': return new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E };
                case 'D': return new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C };
                case 'E': return new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F };
                case 'F': return new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 };
                case 'G': return new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F };
                case 'H': return new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 };
                case 'I': return new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E };
                case 'J': return new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C };
                case 'K': return new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 };
                case 'L': return new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F };
                case 'M': return new byte[] { 0x11, 0x1B, 0x15, 0x11, 0x11, 0x11, 0x11 };
                case 'N': return new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 };
                case 'O': return new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
                case 'P': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 };
                case 'Q': return new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D };
                case 'R': return new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 };
                case 'S': return new byte[] { 0x0E, 0x11, 0x10, 0x0E, 0x01, 0x11, 0x0E };
                case 'T': return new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 };
                case 'U': return new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E };
                case 'V': return new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 };
                case 'W': return new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 };
                case 'X': return new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 };
                case 'Y': return new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 };
                case 'Z': return new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F };
                case '0': return new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E };
                case '1': return new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E };
                case '2': return new byte[] { 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F };
                case '3': return new byte[] { 0x1F, 0x01, 0x02, 0x0E, 0x01, 0x11, 0x0E };
                case '4': return new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 };
                case '5': return new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E };
                case '6': return new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E };
                case '7': return new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 };
                case '8': return new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E };
                case '9': return new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C };
                case '+': return new byte[] { 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00 };
                case '-': return new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 };
                case '.': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C };
                case '/': return new byte[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x00, 0x00 };
                case '[': return new byte[] { 0x0E, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0E };
                case ']': return new byte[] { 0x0E, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0E };
                case '(': return new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 };
                case ')': return new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 };
                case '°': return new byte[] { 0x0C, 0x12, 0x12, 0x0C, 0x00, 0x00, 0x00 };
                case ':': return new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 };
                case '|': return new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 };
                case '>': return new byte[] { 0x10, 0x08, 0x04, 0x02, 0x04, 0x08, 0x10 };
                case '<': return new byte[] { 0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02 };
                case ',': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x08 };
                case '=': return new byte[] { 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00, 0x00 };
                case '#': return new byte[] { 0x0A, 0x1F, 0x0A, 0x0A, 0x1F, 0x0A, 0x00 };
                case '%': return new byte[] { 0x19, 0x19, 0x02, 0x04, 0x08, 0x13, 0x13 };
                case ' ': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                default:  return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
        }

        /// <summary>
        /// 2D 屏幕空间标签智能避让、排版与去重管理器 (Screen Space Anti-Cluttering & Adaptive Layout)
        /// 解决文字叠加、多重借力重叠、飞船HUD遮挡，并支持时间驱动的自适应可见性
        /// </summary>
        private class ScreenLabelManager {
            private struct LabelBox {
                public int x;
                public int y;
                public int w;
                public int h;
                public int priority; // 0=Reticle(最高), 1=Probe HUD, 2=Active Leg, 3=Normal Leg, 4=Planet Name
            }

            private readonly List<LabelBox> _placed = new List<LabelBox>(32);
            private readonly int _bufW;
            private readonly int _bufH;

            public ScreenLabelManager(int w, int h) {
                _bufW = w;
                _bufH = h;
            }

            public void RegisterExclusionZone(int cx, int cy, int halfW, int halfH, int priority = 0) {
                _placed.Add(new LabelBox {
                    x = cx - halfW,
                    y = cy - halfH,
                    w = halfW * 2,
                    h = halfH * 2,
                    priority = priority
                });
            }

            public bool IsNearReticle(int anchorX, int anchorY, Vector2 reticleScr, float thresholdPix = 16f) {
                if (reticleScr.x < 0 || reticleScr.y < 0) return false;
                float dx = anchorX - reticleScr.x;
                float dy = anchorY - reticleScr.y;
                return (dx * dx + dy * dy) < (thresholdPix * thresholdPix);
            }

            /// <summary>
            /// 尝试在指定屏幕坐标锚点附近绘制标签，自动避让重合，返回是否成功绘制
            /// </summary>
            public bool TryPlaceLabel(CanvasPixelBuffer buf, int anchorX, int anchorY, string text, Color col, int priority = 3, bool force = false) {
                if (string.IsNullOrEmpty(text)) return false;

                int textW = text.Length * 6;
                int textH = 8;
                int pad = 2;

                // 优先测试的 6 个候选避让槽位 (偏右上, 偏右下, 偏左上, 偏左下, 正上方, 正下方)
                var candidateOffsets = new (int ox, int oy)[] {
                    (7, -3),                    // 默认偏右上
                    (7, 4),                     // 偏右下
                    (-textW - 7, -3),           // 偏左上
                    (-textW - 7, 4),            // 偏左下
                    (-textW / 2, -14),          // 正上方
                    (-textW / 2, 9)             // 正下方
                };

                foreach (var offset in candidateOffsets) {
                    int candX = anchorX + offset.ox;
                    int candY = anchorY + offset.oy;

                    // 边界保护 (不画出画布边缘)
                    if (candX < 4 || candX + textW > _bufW - 4 || candY < 4 || candY + textH > _bufH - 4) {
                        continue;
                    }

                    // AABB 碰撞检测
                    bool collision = false;
                    for (int i = 0; i < _placed.Count; i++) {
                        var p = _placed[i];
                        if (candX < p.x + p.w + pad && candX + textW + pad > p.x &&
                            candY < p.y + p.h + pad && candY + textH + pad > p.y) {
                            collision = true;
                            break;
                        }
                    }

                    if (!collision) {
                        DrawBitmapStringWithShadow(buf, candX, candY, text, col);
                        _placed.Add(new LabelBox { x = candX, y = candY, w = textW, h = textH, priority = priority });
                        return true;
                    }
                }

                if (force) {
                    // 强制必须显示的最高优先级文本
                    int forceX = Mathf.Clamp(anchorX - textW / 2, 4, _bufW - textW - 4);
                    int forceY = Mathf.Clamp(anchorY + 22, 4, _bufH - textH - 4);
                    DrawBitmapStringWithShadow(buf, forceX, forceY, text, col);
                    _placed.Add(new LabelBox { x = forceX, y = forceY, w = textW, h = textH, priority = priority });
                    return true;
                }

                // 空间极度拥挤且为低优先级标签时：自动优雅隐藏文字，只留发光节点，绝不叠加成乱码！
                return false;
            }
        }
    }
}
