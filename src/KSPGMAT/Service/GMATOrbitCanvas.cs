using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.MGA;

namespace KSPGMAT.Service {
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
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color bg = new Color(0.025f, 0.035f, 0.055f, 1.0f);
            ClearCanvas(tex, bg, width, height);

            if (points == null || points.Count == 0) {
                tex.Apply();
                return tex;
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
            DrawGlowCircle(tex, cx, cy, bodyPix, bodyColor, width, height);

            // Draw anti-aliased trajectory
            Color trajColor = new Color(0.35f, 0.80f, 0.98f, 1.0f);
            for (int i = 0; i < points.Count - 1; i++) {
                int x0 = cx + (int)(points[i].x * scale);
                int y0 = cy + (int)(points[i].y * scale);
                int x1 = cx + (int)(points[i + 1].x * scale);
                int y1 = cy + (int)(points[i + 1].y * scale);
                DrawSmoothLine(tex, x0, y0, x1, y1, trajColor, width, height);
            }

            tex.Apply();
            return tex;
        }

        /// <summary>
        /// 渲染太阳系多重引力助推 3D/2D 日心轨道高精全景画布 (支持 3D 自由旋转俯仰与 2D 锁顶视口)
        /// </summary>
        public static Texture2D RenderSolarSystemMGATexture(
            MGASolution solution, 
            int width = 580, 
            int height = 440, 
            float zoom = 1.0f, 
            Vector2 panOffset = default,
            bool showSoi = true,
            float pitchDeg = 35.0f,
            float yawDeg = 0.0f,
            int selectedEventIndex = -1) 
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color bg = new Color(0.018f, 0.024f, 0.038f, 1.0f);
            ClearCanvas(tex, bg, width, height);

            // 1. Determine strict physical maximum distance in km
            double missionMaxDistKm = 2.2 * AU_KM; // Default to Mars/Asteroid belt view
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                foreach (var leg in solution.Legs) {
                    double r1 = leg.DeparturePosKm.magnitude;
                    double r2 = leg.ArrivalPosKm.magnitude;
                    if (!double.IsNaN(r1) && r1 > missionMaxDistKm) missionMaxDistKm = r1;
                    if (!double.IsNaN(r2) && r2 > missionMaxDistKm) missionMaxDistKm = r2;
                }
            }

            // View framing
            double viewRadiusKm = (missionMaxDistKm * 1.30) / Math.Max(0.05f, zoom);

            // 2. Strict Isometric Scale
            double scale = (Math.Min(width, height) * 0.44) / viewRadiusKm;

            int cx = (int)(width / 2 + panOffset.x);
            int cy = (int)(height / 2 + panOffset.y);

            // 3. Draw 3D Ecliptic Reference Grid & Vernal Axis
            Draw3DEclipticGrid(tex, cx, cy, scale, viewRadiusKm, pitchDeg, yawDeg, width, height);

            // 4. Draw 3D Planetary Orbit Rings & Bodies at Target Epoch
            double missionUt = (solution != null && solution.DepartureUT > 0) ? solution.DepartureUT : (FlightGlobals.ready ? Planetarium.GetUniversalTime() : 0);
            Draw3DPlanetarySystem(tex, cx, cy, scale, viewRadiusKm, missionUt, pitchDeg, yawDeg, width, height, solution);

            // 5. Draw Sun in 3D Center
            Vector2 sunScr = Project3D(Vector3d.zero, scale, cx, cy, pitchDeg, yawDeg, out _);
            DrawSun(tex, (int)sunScr.x, (int)sunScr.y, width, height);

            // 6. Draw 3D Transfer Trajectories & Maneuver Nodes
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                Draw3DTransferLegs(tex, solution, cx, cy, scale, showSoi, pitchDeg, yawDeg, width, height);

                // 6.1 Draw Target Focus Reticle for currently selected Maneuver Event (机动节点目标锁定框)
                if (solution.Events != null && selectedEventIndex >= 0 && selectedEventIndex < solution.Events.Count) {
                    var curEv = solution.Events[selectedEventIndex];
                    Vector3d tgtPos = GetEventPositionKm(solution, curEv);
                    if (!double.IsNaN(tgtPos.x)) {
                        Vector2 tgtScr = Project3D(tgtPos, scale, cx, cy, pitchDeg, yawDeg, out _);
                        Color reticleCol = new Color(0.22f, 0.88f, 0.98f, 0.95f); // High-tech Cyan
                        string tagStr = $"[{curEv.Title}]";
                        DrawTargetReticle(tex, (int)tgtScr.x, (int)tgtScr.y, tagStr, reticleCol, width, height);
                    }
                }
            }

            // 7. Draw HUD Overlay (Scale, Camera Info, Coordinates)
            DrawHudOverlay(tex, viewRadiusKm, scale, pitchDeg, yawDeg, width, height);

            tex.Apply();
            return tex;
        }

        public static Vector3d GetEventPositionKm(MGASolution solution, MGAEventItem ev) {
            if (solution == null || solution.Legs == null || solution.Legs.Count == 0 || ev == null) return Vector3d.zero;
            if (ev.EventType == MGAEventType.Escape) {
                return solution.Legs[0].DeparturePosKm;
            }
            if (ev.EventType == MGAEventType.Circularization) {
                return solution.Legs.Last().ArrivalPosKm;
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

        private static Vector3d ToEcliptic(Vector3d posKm) {
            if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                var refBody = FlightGlobals.Bodies.Find(b => b.name.Equals("Earth", StringComparison.OrdinalIgnoreCase) || b.name.Equals("Kerbin", StringComparison.OrdinalIgnoreCase));
                if (refBody != null && refBody.orbit != null) {
                    double nowUt = FlightGlobals.ready ? Planetarium.GetUniversalTime() : 0.0;
                    Vector3d r = refBody.orbit.getRelativePositionAtUT(nowUt);
                    Vector3d v = refBody.orbit.getOrbitalVelocityAtUT(nowUt);
                    Vector3d uz = Vector3d.Cross(r, v).normalized;
                    if (uz.sqrMagnitude > 0.5) {
                        Vector3d ux = r.normalized;
                        Vector3d uy = Vector3d.Cross(uz, ux).normalized;
                        return new Vector3d(
                            Vector3d.Dot(posKm, ux),
                            Vector3d.Dot(posKm, uy),
                            Vector3d.Dot(posKm, uz)
                        );
                    }
                }
            }
            return posKm;
        }

        public static Vector2 Project3D(Vector3d posKm, double scale, int cx, int cy, float pitchDeg, float yawDeg, out float depth) {
            Vector3d eclPos = ToEcliptic(posKm);

            double yawRad = yawDeg * Mathf.Deg2Rad;
            double pitchRad = pitchDeg * Mathf.Deg2Rad;

            // 1. Yaw rotation around Z axis (in ecliptic plane)
            double cosY = Math.Cos(yawRad), sinY = Math.Sin(yawRad);
            double x1 = eclPos.x * cosY - eclPos.y * sinY;
            double y1 = eclPos.x * sinY + eclPos.y * cosY;
            double z1 = eclPos.z;

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
        // 3D RENDERING PASSES
        // =========================================================================

        private static void ClearCanvas(Texture2D tex, Color bg, int w, int h) {
            Color[] pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
            tex.SetPixels(pixels);
        }

        private static void Draw3DEclipticGrid(Texture2D tex, int cx, int cy, double scale, double viewRadiusKm, float pitch, float yaw, int w, int h) {
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

            DrawSmoothLine(tex, (int)originScr.x, (int)originScr.y, (int)posXScr.x, (int)posXScr.y, vernalColor, w, h);
            DrawSmoothLine(tex, (int)originScr.x, (int)originScr.y, (int)negXScr.x, (int)negXScr.y, axisColor, w, h);
            DrawSmoothLine(tex, (int)originScr.x, (int)originScr.y, (int)posYScr.x, (int)posYScr.y, axisColor, w, h);
            DrawSmoothLine(tex, (int)originScr.x, (int)originScr.y, (int)negYScr.x, (int)negYScr.y, axisColor, w, h);

            // Normal Vector (+Z)
            if (pitch < 80f) {
                Color zCol = new Color(0.35f, 0.25f, 0.55f, 0.6f);
                DrawSmoothLine(tex, (int)originScr.x, (int)originScr.y, (int)posZScr.x, (int)posZScr.y, zCol, w, h);
                DrawBitmapString(tex, (int)posZScr.x + 3, (int)posZScr.y, "+Z", zCol, w, h);
            }

            // 2. Concentric AU Reference Rings in Ecliptic Plane
            double[] auGuides = new double[] { 0.5, 1.0, 1.52, 5.2, 9.58, 19.2, 30.0 };
            foreach (var au in auGuides) {
                double rKm = au * AU_KM;
                if (rKm > viewRadiusKm * 1.8) continue;

                int segs = 100;
                Vector2 prevP = Vector2.zero;
                for (int i = 0; i <= segs; i++) {
                    if ((i / 3) % 2 == 0) { // Dash
                        double ang = (i % segs) * (2.0 * Math.PI / segs);
                        Vector3d pt = new Vector3d(rKm * Math.Cos(ang), rKm * Math.Sin(ang), 0);
                        Vector2 curP = Project3D(pt, scale, cx, cy, pitch, yaw, out _);
                        if (prevP != Vector2.zero) {
                            DrawSmoothLine(tex, (int)prevP.x, (int)prevP.y, (int)curP.x, (int)curP.y, ringColor, w, h);
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
            // 1. Mission active bodies in the flight chain
            if (solution != null && solution.Legs != null && solution.Legs.Count > 0) {
                if (solution.Legs.Any(l => l.FromBody.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                                           l.ToBody.Equals(name, StringComparison.OrdinalIgnoreCase))) {
                    return true;
                }
            }

            // 2. Solar System 8 Major Planets + Pluto + Moon
            //    基线 7.6: 纯净太阳系 Only —— 不再识别原版 Kerbol 虚构天体 (Moho/Eve/Kerbin/Mun/Minmus/Duna/Dres/Jool/Eeloo)
            string n = name.ToLowerInvariant();
            if (n == "mercury" || n == "venus" || n == "earth" || n == "moon" || n == "luna" || n == "mars" || 
                n == "jupiter" || n == "saturn" || n == "uranus" || n == "neptune" || n == "pluto") {
                return true;
            }

            return false;
        }

        private static void Draw3DPlanetarySystem(Texture2D tex, int cx, int cy, double scale, double viewRadiusKm, double ut, float pitch, float yaw, int w, int h, MGASolution solution) {
            var planets = SolarSystemCatalog.GetAvailablePlanets();
            Color defaultOrbitColor = new Color(0.18f, 0.24f, 0.35f, 0.55f);
            Color activeOrbitColor = new Color(0.28f, 0.45f, 0.68f, 0.85f);

            foreach (var p in planets) {
                if (p.SMA <= 0 || p.SMA > viewRadiusKm * 2.2) continue;

                // 智能过滤：仅绘制主要行星或当前航线任务链涉及的目标天体，彻底消除小行星轨道杂乱线条
                if (!IsMajorPlanetOrMissionBody(p.Name, solution)) continue;

                bool isMissionActive = solution != null && solution.Legs != null && 
                    solution.Legs.Any(l => l.FromBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || l.ToBody.Equals(p.Name, StringComparison.OrdinalIgnoreCase));

                Color orbitColor = isMissionActive ? activeOrbitColor : defaultOrbitColor;

                // 1. 严格使用天体真实星历连续采样绘制开普勒轨道环 (保证天体标记与轨道环 100% 绝对物理对齐锁相)
                CelestialBody cb = null;
                if (FlightGlobals.ready && FlightGlobals.Bodies != null) {
                    cb = FlightGlobals.Bodies.Find(b => b.name.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || b.bodyName.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                }

                int segs = 120;
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
                        DrawSmoothLine(tex, (int)prevScr.x, (int)prevScr.y, (int)scrP.x, (int)scrP.y, orbitColor, w, h);
                    }
                    prevScr = scrP;
                }

                // 2. 绘制天体在当前任务历元下的 3D 空间球与标签 (100% 精确落在自己的开普勒轨道线上)
                p.GetHeliocentricState(ut, out Vector3d bPosKm, out _);
                Vector2 bScr = Project3D(bPosKm, scale, cx, cy, pitch, yaw, out float depth);

                if (bScr.x >= -30 && bScr.x < w + 30 && bScr.y >= -30 && bScr.y < h + 30) {
                    Color pCol = PlanetColors.ContainsKey(p.Name) ? PlanetColors[p.Name] : new Color(0.7f, 0.8f, 0.9f, 1f);
                    
                    int pRad = isMissionActive ? 5 : 4;
                    if (p.Name == "Jupiter" || p.Name == "Saturn") pRad = 6;
                    else if (p.Name == "Uranus" || p.Name == "Neptune") pRad = 5;
                    else if (p.Name == "Mercury" || p.Name == "Pluto") pRad = 3;

                    // Planet Glow Core
                    DrawGlowCircle(tex, (int)bScr.x, (int)bScr.y, pRad, pCol, w, h);

                    // Saturn Ring in 3D
                    if (p.Name == "Saturn") {
                        DrawRingEllipse(tex, (int)bScr.x, (int)bScr.y, pRad + 5, pRad + 1, new Color(pCol.r, pCol.g, pCol.b, 0.75f), w, h);
                    }
                    // Earth Atmosphere Halo
                    if (p.Name == "Earth") {
                        DrawAACircle(tex, (int)bScr.x, (int)bScr.y, pRad + 2, new Color(0.4f, 0.8f, 1.0f, 0.5f), w, h);
                    }

                    // Render Planet Name Tag
                    Color textCol = isMissionActive ? new Color(1.0f, 0.90f, 0.25f, 0.95f) : new Color(0.85f, 0.92f, 1.0f, 0.75f);
                    DrawBitmapString(tex, (int)bScr.x + pRad + 3, (int)bScr.y - 3, p.Name, textCol, w, h);
                }
            }
        }

        private static void DrawSun(Texture2D tex, int cx, int cy, int w, int h) {
            DrawSoftGlow(tex, cx, cy, 14, SunGlowOuter, w, h);
            DrawSoftGlow(tex, cx, cy, 9, SunCoronaColor, w, h);
            DrawFilledCircleAA(tex, cx, cy, 5, SunCoreColor, w, h);
            DrawBitmapString(tex, cx + 8, cy - 3, "Sol", new Color(1.0f, 0.9f, 0.5f, 0.9f), w, h);
        }

        private static void Draw3DTransferLegs(Texture2D tex, MGASolution solution, int cx, int cy, double scale, bool showSoi, float pitch, float yaw, int w, int h) {
            double muSun = SolarSystemCatalog.SunMu;

            for (int k = 0; k < solution.Legs.Count; k++) {
                var leg = solution.Legs[k];
                if (double.IsNaN(leg.DeparturePosKm.x) || double.IsNaN(leg.ArrivalPosKm.x)) continue;

                Color legCol = LegColors[k % LegColors.Length];

                Vector3d r0 = leg.DeparturePosKm;
                Vector3d v0 = leg.DepartureVelKms;
                double totalDt = leg.ArrivalUT - leg.DepartureUT;
                if (totalDt <= 0) continue;

                // 经典二体开普勒轨道根数解析转换 (彻底杜绝大步长数值积分的发散与穿屏飞线)
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

                // 初始真近点角与平近点角
                double cosNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, P), -1f, 1f);
                double sinNu0 = Mathf.Clamp((float)Vector3d.Dot(r0.normalized, Q), -1f, 1f);
                double nu0 = Math.Atan2(sinNu0, cosNu0);
                if (nu0 < 0) nu0 += 2.0 * Math.PI;

                int steps = 140;
                Vector2 prevScr = Project3D(r0, scale, cx, cy, pitch, yaw, out _);

                if (ecc < 1.0 && a > 0) {
                    // 椭圆轨道解析采样 (100% 连续且绝对物理闭合稳定)
                    double E0 = 2.0 * Math.Atan2(Math.Sqrt(1.0 - ecc) * Math.Sin(nu0 / 2.0), Math.Sqrt(1.0 + ecc) * Math.Cos(nu0 / 2.0));
                    double M0 = E0 - ecc * Math.Sin(E0);
                    double n = Math.Sqrt(muSun / (a * a * a));

                    for (int s = 1; s <= steps; s++) {
                        double dt = (s / (double)steps) * totalDt;
                        double M = M0 + n * dt;

                        // 求解开普勒方程
                        double E = M;
                        for (int iter = 0; iter < 15; iter++) {
                            double f = E - ecc * Math.Sin(E) - M;
                            double fPrime = 1.0 - ecc * Math.Cos(E);
                            double dE = -f / fPrime;
                            E += dE;
                            if (Math.Abs(dE) < 1e-10) break;
                        }

                        double nu = 2.0 * Math.Atan2(Math.Sqrt(1.0 + ecc) * Math.Sin(E / 2.0), Math.Sqrt(1.0 - ecc) * Math.Cos(E / 2.0));
                        double rMag = a * (1.0 - ecc * Math.Cos(E));
                        Vector3d rPos = (rMag * Math.Cos(nu)) * P + (rMag * Math.Sin(nu)) * Q;

                        Vector2 nextScr = Project3D(rPos, scale, cx, cy, pitch, yaw, out _);
                        DrawThickSmoothLine(tex, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2, w, h);
                        prevScr = nextScr;
                    }
                } else {
                    // 双曲或抛物线平滑拟合
                    for (int s = 1; s <= steps; s++) {
                        double frac = s / (double)steps;
                        Vector3d rPos = Vector3d.Lerp(r0, leg.ArrivalPosKm, (float)frac);
                        Vector2 nextScr = Project3D(rPos, scale, cx, cy, pitch, yaw, out _);
                        DrawThickSmoothLine(tex, (int)prevScr.x, (int)prevScr.y, (int)nextScr.x, (int)nextScr.y, legCol, 2, w, h);
                        prevScr = nextScr;
                    }
                }

                // Departure Node
                Vector2 depScr = Project3D(leg.DeparturePosKm, scale, cx, cy, pitch, yaw, out _);
                DrawGlowCircle(tex, (int)depScr.x, (int)depScr.y, 5, legCol, w, h);

                string badgeLabel = (k == 0) ? $"[1] {leg.FromBody}" : $"[{k + 1}] {leg.FromBody}";
                DrawBitmapString(tex, (int)depScr.x + 7, (int)depScr.y + 2, badgeLabel, legCol, w, h);

                // SOI
                if (showSoi) {
                    var bodyPlanet = SolarSystemCatalog.GetPlanet(leg.FromBody);
                    double approxSoiKm = bodyPlanet.SMA * Math.Pow(bodyPlanet.Mu / muSun, 0.4);
                    int soiPix = Math.Max(4, (int)(approxSoiKm * scale));
                    DrawAACircle(tex, (int)depScr.x, (int)depScr.y, soiPix, new Color(legCol.r, legCol.g, legCol.b, 0.35f), w, h);
                }

                // Final Arrival Node
                if (k == solution.Legs.Count - 1) {
                    Vector2 arrScr = Project3D(leg.ArrivalPosKm, scale, cx, cy, pitch, yaw, out _);
                    Color arrCol = new Color(0.98f, 0.35f, 0.35f, 1.0f);
                    DrawGlowCircle(tex, (int)arrScr.x, (int)arrScr.y, 6, arrCol, w, h);
                    DrawBitmapString(tex, (int)arrScr.x + 8, (int)arrScr.y + 2, $"[End] {leg.ToBody}", arrCol, w, h);
                }
            }
        }

        private static void DrawTargetReticle(Texture2D tex, int tx, int ty, string label, Color col, int w, int h) {
            int S = 20; // Half size (40x40 frame)
            int L = 7;  // Corner segment length

            Color glowCol = new Color(col.r, col.g, col.b, 0.40f);
            Color brightCol = new Color(1f, 1f, 1f, 0.95f);

            // 1. Four L-shaped Corner Brackets (厚实鲜明战术准星)
            // Top-Left
            DrawSmoothLine(tex, tx - S, ty - S, tx - S + L, ty - S, col, w, h);
            DrawSmoothLine(tex, tx - S, ty - S + 1, tx - S + L, ty - S + 1, col, w, h);
            DrawSmoothLine(tex, tx - S, ty - S, tx - S, ty - S + L, col, w, h);
            DrawSmoothLine(tex, tx - S + 1, ty - S, tx - S + 1, ty - S + L, col, w, h);
            // Top-Right
            DrawSmoothLine(tex, tx + S, ty - S, tx + S - L, ty - S, col, w, h);
            DrawSmoothLine(tex, tx + S, ty - S + 1, tx + S - L, ty - S + 1, col, w, h);
            DrawSmoothLine(tex, tx + S, ty - S, tx + S, ty - S + L, col, w, h);
            DrawSmoothLine(tex, tx + S - 1, ty - S, tx + S - 1, ty - S + L, col, w, h);
            // Bottom-Left
            DrawSmoothLine(tex, tx - S, ty + S, tx - S + L, ty + S, col, w, h);
            DrawSmoothLine(tex, tx - S, ty + S - 1, tx - S + L, ty + S - 1, col, w, h);
            DrawSmoothLine(tex, tx - S, ty + S, tx - S, ty + S - L, col, w, h);
            DrawSmoothLine(tex, tx - S + 1, ty + S, tx - S + 1, ty + S - L, col, w, h);
            // Bottom-Right
            DrawSmoothLine(tex, tx + S, ty + S, tx + S - L, ty + S, col, w, h);
            DrawSmoothLine(tex, tx + S, ty + S - 1, tx + S - L, ty + S - 1, col, w, h);
            DrawSmoothLine(tex, tx + S, ty + S, tx + S, ty + S - L, col, w, h);
            DrawSmoothLine(tex, tx + S - 1, ty + S, tx + S - 1, ty + S - L, col, w, h);

            // 2. Crosshair Ticks (十字刻度引线)
            DrawSmoothLine(tex, tx - S - 6, ty, tx - S + 2, ty, col, w, h);
            DrawSmoothLine(tex, tx + S - 2, ty, tx + S + 6, ty, col, w, h);
            DrawSmoothLine(tex, tx, ty - S - 6, tx, ty - S + 2, col, w, h);
            DrawSmoothLine(tex, tx, ty + S - 2, tx, ty + S + 6, col, w, h);

            // 3. Inner Center Ring & Glow Center (中心微光准星)
            DrawAACircle(tex, tx, ty, 8, glowCol, w, h);
            DrawAACircle(tex, tx, ty, 3, brightCol, w, h);

            // 4. Target Label Banner (目标节点动态悬浮标)
            if (!string.IsNullOrEmpty(label)) {
                int labelX = tx + S + 6;
                int labelY = ty - 4;
                DrawBitmapString(tex, labelX, labelY, label, col, w, h);
            }
        }

        private static void DrawHudOverlay(Texture2D tex, double viewRadiusKm, double scale, float pitch, float yaw, int w, int h) {
            // Distance scale legend in bottom-left
            double auInView = viewRadiusKm / AU_KM;
            double legendAu = 1.0;
            if (auInView > 15.0) legendAu = 10.0;
            else if (auInView > 6.0) legendAu = 5.0;
            else if (auInView > 2.5) legendAu = 2.0;
            else if (auInView < 0.8) legendAu = 0.2;

            int barLenPix = (int)(legendAu * AU_KM * scale);
            if (barLenPix > 10 && barLenPix < w - 40) {
                int barX = 20;
                int barY = 20;
                Color hudCol = new Color(0.55f, 0.75f, 0.95f, 0.8f);
                DrawSmoothLine(tex, barX, barY, barX + barLenPix, barY, hudCol, w, h);
                DrawSmoothLine(tex, barX, barY - 3, barX, barY + 3, hudCol, w, h);
                DrawSmoothLine(tex, barX + barLenPix, barY - 3, barX + barLenPix, barY + 3, hudCol, w, h);

                string label = $"{legendAu:G} AU ({legendAu * 1.496e8:0.#E+0} km)";
                DrawBitmapString(tex, barX, barY + 5, label, hudCol, w, h);
            }

            // View Angle Indicator in top-right
            string viewMode = (pitch >= 88f) ? "2D Top-Down" : ((Math.Abs(pitch) < 1.5f) ? $"Ecliptic (0° / {yaw:F0}°)" : $"3D ({pitch:F0}° / {yaw:F0}°)");
            DrawBitmapString(tex, w - 130, h - 18, viewMode, new Color(0.40f, 0.65f, 0.90f, 0.85f), w, h);
        }

        // =========================================================================
        // PRIMITIVE ANTI-ALIASED & GRAPHICS ALGORITHMS
        // =========================================================================

        private static void BlendPixel(Texture2D tex, int x, int y, Color col, int w, int h) {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            Color cur = tex.GetPixel(x, y);
            float alpha = col.a;
            Color blended = new Color(
                cur.r * (1f - alpha) + col.r * alpha,
                cur.g * (1f - alpha) + col.g * alpha,
                cur.b * (1f - alpha) + col.b * alpha,
                1.0f
            );
            tex.SetPixel(x, y, blended);
        }

        private static void DrawSoftGlow(Texture2D tex, int cx, int cy, int rad, Color col, int w, int h) {
            for (int dy = -rad; dy <= rad; dy++) {
                for (int dx = -rad; dx <= rad; dx++) {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= rad) {
                        float factor = (1f - (dist / rad));
                        Color c = new Color(col.r, col.g, col.b, col.a * factor * factor);
                        BlendPixel(tex, cx + dx, cy + dy, c, w, h);
                    }
                }
            }
        }

        private static void DrawGlowCircle(Texture2D tex, int cx, int cy, int rad, Color col, int w, int h) {
            DrawSoftGlow(tex, cx, cy, rad + 4, new Color(col.r, col.g, col.b, 0.35f), w, h);
            DrawFilledCircleAA(tex, cx, cy, rad, col, w, h);
        }

        private static void DrawFilledCircleAA(Texture2D tex, int cx, int cy, int rad, Color col, int w, int h) {
            for (int dy = -rad - 1; dy <= rad + 1; dy++) {
                for (int dx = -rad - 1; dx <= rad + 1; dx++) {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= rad - 0.5f) {
                        BlendPixel(tex, cx + dx, cy + dy, col, w, h);
                    } else if (dist <= rad + 0.5f) {
                        float coverage = (rad + 0.5f) - dist;
                        Color c = new Color(col.r, col.g, col.b, col.a * coverage);
                        BlendPixel(tex, cx + dx, cy + dy, c, w, h);
                    }
                }
            }
        }

        private static void DrawAACircle(Texture2D tex, int cx, int cy, int rad, Color col, int w, int h) {
            int segments = Math.Max(40, rad * 3);
            int prevX = -1, prevY = -1;
            for (int i = 0; i <= segments; i++) {
                double a = (i % segments) * (2.0 * Math.PI / segments);
                int px = cx + (int)(rad * Math.Cos(a));
                int py = cy + (int)(rad * Math.Sin(a));
                if (prevX >= 0) DrawSmoothLine(tex, prevX, prevY, px, py, col, w, h);
                prevX = px; prevY = py;
            }
        }

        private static void DrawRingEllipse(Texture2D tex, int cx, int cy, int rx, int ry, Color col, int w, int h) {
            int segments = 48;
            int prevX = -1, prevY = -1;
            for (int i = 0; i <= segments; i++) {
                double a = (i % segments) * (2.0 * Math.PI / segments);
                int px = cx + (int)(rx * Math.Cos(a));
                int py = cy + (int)(ry * Math.Sin(a));
                if (prevX >= 0) DrawSmoothLine(tex, prevX, prevY, px, py, col, w, h);
                prevX = px; prevY = py;
            }
        }

        /// <summary>
        /// Xiaolin Wu's Anti-Aliased Line Algorithm
        /// </summary>
        private static void DrawSmoothLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col, int w, int h) {
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
                    BlendPixel(tex, ipartY, x, new Color(col.r, col.g, col.b, col.a * (1f - fpartY)), w, h);
                    BlendPixel(tex, ipartY + 1, x, new Color(col.r, col.g, col.b, col.a * fpartY), w, h);
                } else {
                    BlendPixel(tex, x, ipartY, new Color(col.r, col.g, col.b, col.a * (1f - fpartY)), w, h);
                    BlendPixel(tex, x, ipartY + 1, new Color(col.r, col.g, col.b, col.a * fpartY), w, h);
                }
                y += gradient;
            }
        }

        private static void DrawThickSmoothLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col, int thickness, int w, int h) {
            DrawSmoothLine(tex, x0, y0, x1, y1, col, w, h);
            for (int o = 1; o <= thickness; o++) {
                Color fCol = new Color(col.r, col.g, col.b, col.a * 0.65f);
                DrawSmoothLine(tex, x0, y0 + o, x1, y1 + o, fCol, w, h);
                DrawSmoothLine(tex, x0, y0 - o, x1, y1 - o, fCol, w, h);
                DrawSmoothLine(tex, x0 + o, y0, x1 + o, y1, fCol, w, h);
                DrawSmoothLine(tex, x0 - o, y0, x1 - o, y1, fCol, w, h);
            }
        }

        // =========================================================================
        // MINIMAL 5x7 CRISP BITMAP FONT RENDERER ON TEXTURE
        // =========================================================================

        private static void DrawBitmapString(Texture2D tex, int x, int y, string text, Color col, int w, int h) {
            if (string.IsNullOrEmpty(text)) return;
            int curX = x;
            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];
                DrawChar5x7(tex, curX, y, ch, col, w, h);
                curX += 6; // 5 width + 1 spacing
            }
        }

        private static void DrawChar5x7(Texture2D tex, int x, int y, char c, Color col, int w, int h) {
            byte[] glyph = GetGlyph5x7(c);
            if (glyph == null) return;
            for (int row = 0; row < 7; row++) {
                byte b = glyph[row];
                for (int colIdx = 0; colIdx < 5; colIdx++) {
                    if (((b >> (4 - colIdx)) & 1) == 1) {
                        BlendPixel(tex, x + colIdx, y + (6 - row), col, w, h);
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
                case ' ': return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                default:  return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
        }
    }
}
