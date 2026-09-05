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
            int maxLegsToDraw = 0) 
        {
            CanvasPixelBuffer buf = new CanvasPixelBuffer(width, height, new Color32(5, 6, 10, 255));
            buf.ClearWithCosmicBackground();

            // 1. Determine strict physical maximum distance in km
            double missionMaxDistKm = 2.2 * AU_KM; // Default to Mars/Asteroid belt view
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                int countToCheck = (maxLegsToDraw > 0 && maxLegsToDraw <= solution.Legs.Count) ? maxLegsToDraw : solution.Legs.Count;
                for (int k = 0; k < countToCheck; k++) {
                    var leg = solution.Legs[k];
                    double r1 = leg.DeparturePosKm.magnitude;
                    double r2 = leg.ArrivalPosKm.magnitude;
                    if (!double.IsNaN(r1) && !double.IsInfinity(r1) && r1 > missionMaxDistKm && r1 < 100.0 * AU_KM) missionMaxDistKm = r1;
                    if (!double.IsNaN(r2) && !double.IsInfinity(r2) && r2 > missionMaxDistKm && r2 < 100.0 * AU_KM) missionMaxDistKm = r2;
                }
            }

            if (double.IsNaN(missionMaxDistKm) || double.IsInfinity(missionMaxDistKm) || missionMaxDistKm <= 0) {
                missionMaxDistKm = 2.2 * AU_KM;
            }

            // View framing
            double viewRadiusKm = (missionMaxDistKm * 1.30) / Math.Max(0.05f, zoom);
            if (double.IsNaN(viewRadiusKm) || double.IsInfinity(viewRadiusKm) || viewRadiusKm < 1e5 || viewRadiusKm > 200.0 * AU_KM) {
                viewRadiusKm = 2.86 * AU_KM;
            }

            // 2. Strict Isometric Scale
            double scale = (Math.Min(width, height) * 0.44) / viewRadiusKm;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) {
                scale = (Math.Min(width, height) * 0.44) / (2.86 * AU_KM);
            }

            int cx = (int)(width / 2 + panOffset.x);
            int cy = (int)(height / 2 + panOffset.y);

            // 3. Draw 3D Ecliptic Reference Grid & Vernal Axis
            Draw3DEclipticGrid(buf, cx, cy, scale, viewRadiusKm, pitchDeg, yawDeg);

            // 4. Draw 3D Planetary Orbit Rings & Bodies at Target Epoch (联动当前选中机动节点或滑块推进时刻)
            double missionUt = (solution != null && solution.DepartureUT > 0) ? solution.DepartureUT : (FlightGlobals.ready ? Planetarium.GetUniversalTime() : 0);
            if (solution != null && solution.Events != null && selectedEventIndex >= 0 && selectedEventIndex < solution.Events.Count) {
                var curEv = solution.Events[selectedEventIndex];
                if (curEv.EpochUT > 0) {
                    missionUt = curEv.EpochUT;
                }
            } else if (solution != null && solution.Legs != null && maxLegsToDraw > 0 && maxLegsToDraw <= solution.Legs.Count) {
                // 当用户拖动滑块推进到第 K 航段时，天体行星位置自动前进至该航段的到达历元！
                missionUt = solution.Legs[maxLegsToDraw - 1].ArrivalUT;
            }
            Draw3DPlanetarySystem(buf, cx, cy, scale, viewRadiusKm, missionUt, pitchDeg, yawDeg, solution);

            // 5. Draw Sun in 3D Center
            Vector2 sunScr = Project3D(Vector3d.zero, scale, cx, cy, pitchDeg, yawDeg, out _);
            DrawSun(buf, (int)sunScr.x, (int)sunScr.y);

            // 6. Draw 3D Transfer Trajectories & Maneuver Nodes
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                Draw3DTransferLegs(buf, solution, cx, cy, scale, showSoi, pitchDeg, yawDeg, maxLegsToDraw);

                // 6.1 Draw Target Focus Reticle for currently selected Maneuver Event (机动节点目标锁定框)
                if (solution.Events != null && selectedEventIndex >= 0 && selectedEventIndex < solution.Events.Count) {
                    var curEv = solution.Events[selectedEventIndex];
                    Vector3d tgtPos = GetEventPositionKm(solution, curEv);
                    if (!double.IsNaN(tgtPos.x)) {
                        Vector2 tgtScr = Project3D(tgtPos, scale, cx, cy, pitchDeg, yawDeg, out _);
                        Color reticleCol = new Color(0.22f, 0.88f, 0.98f, 0.95f); // High-tech Cyan
                        string tagStr = $"[{curEv.Title}]";
                        DrawTargetReticle(buf, (int)tgtScr.x, (int)tgtScr.y, tagStr, reticleCol);
                    }
                }
            }

            // 7. Draw HUD Overlay (Scale, Camera Info, Coordinates)
            DrawHudOverlay(buf, viewRadiusKm, scale, pitchDeg, yawDeg);

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
            int maxLegsToDraw = 0) 
        {
            return RenderSolarSystemMGATexture(null, solution, width, height, zoom, panOffset, showSoi, pitchDeg, yawDeg, selectedEventIndex, maxLegsToDraw);
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

        private static void Draw3DEclipticGrid(CanvasPixelBuffer buf, int cx, int cy, double scale, double viewRadiusKm, float pitch, float yaw) {
            Color axisColor = new Color(0.12f, 0.16f, 0.24f, 0.7f);
            Color vernalColor = new Color(0.24f, 0.40f, 0.65f, 0.9f); // +X Vernal Equinox
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

            // 2. Concentric AU Reference Rings in Ecliptic Plane
            double[] auGuides = new double[] { 0.5, 1.0, 1.52, 5.2, 9.58, 19.2, 30.0 };
            foreach (var au in auGuides) {
                double rKm = au * AU_KM;
                if (rKm > viewRadiusKm * 1.8) continue;

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

        private static void Draw3DPlanetarySystem(CanvasPixelBuffer buf, int cx, int cy, double scale, double viewRadiusKm, double ut, float pitch, float yaw, MGASolution solution) {
            var planets = SolarSystemCatalog.GetAvailablePlanets();
            Color defaultOrbitColor = new Color(0.18f, 0.24f, 0.35f, 0.55f);
            Color activeOrbitColor = new Color(0.28f, 0.45f, 0.68f, 0.85f);

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
                    : (2.0 * Math.PI * Math.Sqrt(Math.Pow(p.SMA, 3) / SolarSystemCatalog.SunMu));

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

                    // Render Planet Name Tag with Drop Shadow
                    bool isMissionBody = solution != null && solution.Legs != null && 
                        solution.Legs.Any(l => l.FromBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || l.ToBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                    if (!isMissionBody) {
                        Color textCol = new Color(0.85f, 0.92f, 1.0f, 0.85f);
                        DrawBitmapStringWithShadow(buf, (int)bScr.x + pRad + 3, (int)bScr.y - 3, p.Name, textCol);
                    }
                }
            }
        }

        private static void DrawSun(CanvasPixelBuffer buf, int cx, int cy) {
            DrawSoftGlow(buf, cx, cy, 22, new Color(SunGlowOuter.r, SunGlowOuter.g, SunGlowOuter.b, 0.12f));
            DrawSoftGlow(buf, cx, cy, 14, SunCoronaColor);
            DrawSoftGlow(buf, cx, cy, 8, new Color(1.0f, 0.85f, 0.35f, 0.65f));
            DrawFilledCircleAA(buf, cx, cy, 5, SunCoreColor);
            DrawBitmapStringWithShadow(buf, cx + 8, cy - 3, "Sol", new Color(1.0f, 0.95f, 0.65f, 0.95f));
        }

        private static void Draw3DTransferLegs(CanvasPixelBuffer buf, MGASolution solution, int cx, int cy, double scale, bool showSoi, float pitch, float yaw, int maxLegsToDraw = 0) {
            double muSun = SolarSystemCatalog.SunMu;
            int limit = (maxLegsToDraw > 0 && maxLegsToDraw <= solution.Legs.Count) ? maxLegsToDraw : solution.Legs.Count;

            for (int k = 0; k < limit; k++) {
                var leg = solution.Legs[k];
                if (double.IsNaN(leg.DeparturePosKm.x) || double.IsNaN(leg.ArrivalPosKm.x)) continue;

                Color legCol = LegColors[k % LegColors.Length];

                Vector3d r0 = leg.DeparturePosKm;
                Vector3d v0 = leg.DepartureVelKms;
                double totalDt = leg.ArrivalUT - leg.DepartureUT;
                if (totalDt <= 0) continue;

                Vector3d hVec = Vector3d.Cross(r0, v0);
                double hMag = hVec.magnitude;
                if (hMag < 1e-5) continue;

                double r0Mag = r0.magnitude;
                double v0Mag = v0.magnitude;
                Vector3d eVec = (Vector3d.Cross(v0, hVec) / muSun) - (r0 / r0Mag);
                double ecc = eVec.magnitude;
                double energy = (v0Mag * v0Mag * 0.5) - (muSun / r0Mag);
                double a = -muSun / (2.0 * energy);

                Vector3d P = (ecc > 1e-6) ? (eVec / ecc) : r0.normalized;
                Vector3d W = hVec / hMag;
                Vector3d Q = Vector3d.Cross(W, P).normalized;

                double cosNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, P), -1f, 1f);
                double sinNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, Q), -1f, 1f);
                double nu0 = Math.Atan2(sinNu0, cosNu0);
                if (nu0 < 0) nu0 += 2.0 * Math.PI;

                int steps = 100;
                Vector2 prevScr = Project3D(r0, scale, cx, cy, pitch, yaw, out _);

                if (ecc < 1.0 && a > 0) {
                    double E0 = 2.0 * Math.Atan2(Math.Sqrt(1.0 - ecc) * Math.Sin(nu0 / 2.0), Math.Sqrt(1.0 + ecc) * Math.Cos(nu0 / 2.0));
                    double M0 = E0 - ecc * Math.Sin(E0);
                    double n = Math.Sqrt(muSun / (a * a * a));

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
                        DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                        prevScr = nextScr;
                    }
                } else if (ecc > 1.0001 && energy > 0) {
                    // 双曲逃逸转移弧段 (真实开普勒双曲轨道严密递推采样，彻底消除直线下退化带来的折线假象)
                    double a_h = muSun / (2.0 * energy);
                    double n_h = Math.Sqrt(muSun / (a_h * a_h * a_h));

                    double sinH0 = Math.Sqrt(ecc * ecc - 1.0) * Math.Sin(nu0) / (1.0 + ecc * Math.Cos(nu0));
                    double H0 = Math.Log(sinH0 + Math.Sqrt(sinH0 * sinH0 + 1.0)); // asinh(sinH0)
                    double Mh0 = ecc * Math.Sinh(H0) - H0;

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
                        DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                        prevScr = nextScr;
                    }
                } else {
                    for (int s = 1; s <= steps; s++) {
                        double frac = s / (double)steps;
                        Vector3d rPos = Vector3d.Lerp(r0, leg.ArrivalPosKm, (float)frac);
                        Vector2 nextScr = Project3D(rPos, scale, cx, cy, pitch, yaw, out _);
                        DrawThickSmoothLine(buf, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2);
                        prevScr = nextScr;
                    }
                }

                // Departure Node
                Vector2 depScr = Project3D(leg.DeparturePosKm, scale, cx, cy, pitch, yaw, out _);
                DrawGlowCircle(buf, (int)depScr.x, (int)depScr.y, 5, legCol);
                if (leg.FromBody.Equals("Saturn", StringComparison.OrdinalIgnoreCase)) {
                    DrawRingEllipse(buf, (int)depScr.x, (int)depScr.y, 11, 3, new Color(0.92f, 0.82f, 0.58f, 0.75f));
                }

                string badgeLabel = (k == 0) ? $"[1] {leg.FromBody}" : $"[{k + 1}] {leg.FromBody}";
                DrawBitmapStringWithShadow(buf, (int)depScr.x + 7, (int)depScr.y + 2, badgeLabel, legCol);

                // Feature 1: Draw Deep Space Maneuver (DSM) node in glowing amber
                if (leg.HasDSM && leg.DSM != null && !double.IsNaN(leg.DSMPositionKm.x)) {
                    Vector2 dsmScr = Project3D(leg.DSMPositionKm, scale, cx, cy, pitch, yaw, out _);
                    Color dsmCol = new Color(0.98f, 0.72f, 0.15f, 1.0f);
                    DrawGlowCircle(buf, (int)dsmScr.x, (int)dsmScr.y, 5, dsmCol);
                    string dsmLabel = $"[DSM] {leg.DSM.DeltaVMs:F0}m/s";
                    DrawBitmapStringWithShadow(buf, (int)dsmScr.x + 7, (int)dsmScr.y - 4, dsmLabel, dsmCol);
                }

                // SOI
                if (showSoi) {
                    var bodyPlanet = SolarSystemCatalog.GetPlanet(leg.FromBody);
                    double approxSoiKm = bodyPlanet.SMA * Math.Pow(bodyPlanet.Mu / muSun, 0.4);
                    int soiPix = Math.Max(4, (int)(approxSoiKm * scale));
                    DrawAACircle(buf, (int)depScr.x, (int)depScr.y, soiPix, new Color(legCol.r, legCol.g, legCol.b, 0.35f));
                }

                // Stage or Final Arrival Node
                if (k == limit - 1) {
                    Vector2 arrScr = Project3D(leg.ArrivalPosKm, scale, cx, cy, pitch, yaw, out _);
                    bool isFullEnd = (k == solution.Legs.Count - 1);
                    Color arrCol = isFullEnd ? new Color(0.98f, 0.35f, 0.35f, 1.0f) : new Color(0.25f, 0.92f, 0.65f, 1.0f);
                    DrawGlowCircle(buf, (int)arrScr.x, (int)arrScr.y, 6, arrCol);
                    string arrLabel = isFullEnd ? $"[End] {leg.ToBody}" : $"[Stage #{k + 1}] {leg.ToBody}";
                    DrawBitmapStringWithShadow(buf, (int)arrScr.x + 8, (int)arrScr.y + 2, arrLabel, arrCol);
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

        private static void DrawTargetReticle(CanvasPixelBuffer buf, int tx, int ty, string label, Color col) {
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
                int labelX = tx - (label.Length * 6) / 2;
                int labelY = ty + S + 4;
                DrawBitmapStringWithShadow(buf, labelX, labelY, label, col);
            }
        }

        private static void DrawHudOverlay(CanvasPixelBuffer buf, double viewRadiusKm, double scale, float pitch, float yaw) {
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

            string viewMode = (pitch >= 88f) ? "2D Top-Down (+Z)" : ((pitch <= -88f) ? "2D Bottom-Up (-Z)" : ((Math.Abs(pitch) < 1.5f) ? $"Ecliptic (0° / {yaw:F0}°)" : $"3D ({pitch:F0}° / {yaw:F0}°)"));
            int textW = viewMode.Length * 6;
            DrawBitmapStringWithShadow(buf, buf.Width - textW - 15, buf.Height - 18, viewMode, new Color(0.40f, 0.65f, 0.90f, 0.85f));
            DrawBitmapStringWithShadow(buf, 20, buf.Height - 18, "LMB: Rotate | RMB/Mid: Pan | Scroll: Zoom", new Color(0.30f, 0.42f, 0.58f, 0.75f));
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
                case ' ': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                default:  return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
        }
    }
}
