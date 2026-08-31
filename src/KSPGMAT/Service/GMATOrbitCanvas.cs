using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    public static class GMATOrbitCanvas {
        public static Texture2D RenderTrajectoryTexture(List<TrajectoryPoint> points, string centralBody, int width = 512, int height = 512) {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // Fill Background (Deep space dark navy)
            Color bg = new Color(0.04f, 0.06f, 0.10f, 1.0f);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
            tex.SetPixels(pixels);

            if (points == null || points.Count == 0) {
                tex.Apply();
                return tex;
            }

            // Find Cartesian Bounding Box in XY Plane
            double maxR = 1000.0;
            for (int i = 0; i < points.Count; i++) {
                double r = Math.Sqrt(points[i].x * points[i].x + points[i].y * points[i].y + points[i].z * points[i].z);
                if (r > maxR) maxR = r;
            }
            double viewRadius = maxR * 1.15;

            int cx = width / 2;
            int cy = height / 2;

            // Draw Central Body Sphere
            double bodyRadKm = 6378.1363;
            Color bodyColor = new Color(0.2f, 0.5f, 0.9f, 1f);

            if (FlightGlobals.Bodies != null) {
                var b = FlightGlobals.Bodies.Find(cb => cb != null && (cb.bodyName.Equals(centralBody, StringComparison.OrdinalIgnoreCase) ||
                                                        (centralBody == "Luna" && cb.bodyName == "Moon") ||
                                                        (centralBody == "Moon" && cb.bodyName == "Luna")));
                if (b != null && b.Radius > 0) {
                    bodyRadKm = b.Radius / 1000.0;
                    if (b.bodyName == "Moon" || b.bodyName == "Luna" || b.bodyName == "Mun") bodyColor = new Color(0.7f, 0.7f, 0.75f, 1f);
                    else if (b.bodyName == "Mars" || b.bodyName == "Duna") bodyColor = new Color(0.9f, 0.35f, 0.2f, 1f);
                    else if (b.bodyName == "Sun" || b.bodyName == "Kerbol") bodyColor = new Color(1.0f, 0.85f, 0.2f, 1f);
                }
            } else if (centralBody == "Luna" || centralBody == "Moon") {
                bodyRadKm = 1737.4;
                bodyColor = new Color(0.7f, 0.7f, 0.75f, 1f);
            }

            int bodyPixelRad = Math.Max(4, (int)((bodyRadKm / viewRadius) * (width / 2)));

            for (int dy = -bodyPixelRad; dy <= bodyPixelRad; dy++) {
                for (int dx = -bodyPixelRad; dx <= bodyPixelRad; dx++) {
                    if (dx * dx + dy * dy <= bodyPixelRad * bodyPixelRad) {
                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height) {
                            tex.SetPixel(px, py, bodyColor);
                        }
                    }
                }
            }

            // Draw Trajectory Path Lines
            Color trajColor = new Color(0.22f, 0.74f, 0.97f, 1.0f); // Vibrant cyan
            for (int i = 0; i < points.Count - 1; i++) {
                int x0 = cx + (int)((points[i].x / viewRadius) * (width / 2));
                int y0 = cy + (int)((points[i].y / viewRadius) * (height / 2));
                int x1 = cx + (int)((points[i + 1].x / viewRadius) * (width / 2));
                int y1 = cy + (int)((points[i + 1].y / viewRadius) * (height / 2));

                DrawLine(tex, x0, y0, x1, y1, trajColor, width, height);
            }

            tex.Apply();
            return tex;
        }

        private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col, int w, int h) {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true) {
                if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h) {
                    tex.SetPixel(x0, y0, col);
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}
