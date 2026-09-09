using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.MGA {
    /// <summary>
    /// 官方 PyKEP 动力学驱动的高精行星际发射与捕获猪排图 (Porkchop Plot) 生成引擎
    /// (High-Precision Official PyKEP Interplanetary Porkchop Plot Engine)
    /// </summary>
    public class PorkchopGrid {
        public int Width { get; set; } = MGADefaults.PorkchopDefaultWidth;
        public int Height { get; set; } = MGADefaults.PorkchopDefaultHeight;
        public string FromBody { get; set; }
        public string ToBody { get; set; }
        public double DepStartUT { get; set; }
        public double DepSpanDays { get; set; }
        public double ToFMinDays { get; set; }
        public double ToFMaxDays { get; set; }
        public double DepAltKm { get; set; } = MGADefaults.DepartureAltitudeKm;
        public double ArrAltKm { get; set; } = MGADefaults.DestinationAltitudeKm;
        public bool FlybyOnly { get; set; } = false;

        public double[,] DeltaVGrid { get; set; }
        public double[,] EjectDVGrid { get; set; }
        public double[,] InsertDVGrid { get; set; }
        public double[,] C3Grid { get; set; }

        public int BestX { get; set; }
        public int BestY { get; set; }
        public double BestTotalDV { get; set; } = double.MaxValue;
        public double BestDepUT { get; set; }
        public double BestArrUT { get; set; }
        public double BestToFDays { get; set; }

        public Texture2D PlotTexture { get; set; }
        public Texture2D ColorBarTexture { get; set; }
        public double ScaleMinDV { get; set; }
        public double ScaleMaxDV { get; set; }
        public string ComputeBackend { get; set; } = "CPU";

        public struct CellInfo {
            public double DepUT;
            public double ArrUT;
            public double ToFDays;
            public double TotalDV;
            public double EjectDV;
            public double InsertDV;
            public double C3;
            public bool IsValid;
        }

        public CellInfo GetCellInfo(float normX, float normY) {
            normX = Mathf.Clamp01(normX);
            normY = Mathf.Clamp01(normY);
            int gx = Mathf.Clamp((int)(normX * (Width - 1)), 0, Width - 1);
            int gy = Mathf.Clamp((int)(normY * (Height - 1)), 0, Height - 1);

            double depUT = DepStartUT + (gx / (double)(Width - 1)) * (DepSpanDays * MGADefaults.SecondsPerDay);
            double tofDays = ToFMinDays + (gy / (double)(Height - 1)) * (ToFMaxDays - ToFMinDays);
            double arrUT = depUT + tofDays * MGADefaults.SecondsPerDay;

            if (DeltaVGrid == null) return new CellInfo { IsValid = false };

            double totDV = DeltaVGrid[gx, gy];
            double ejDV = EjectDVGrid != null ? EjectDVGrid[gx, gy] : 0;
            double inDV = InsertDVGrid != null ? InsertDVGrid[gx, gy] : 0;
            double c3 = C3Grid != null ? C3Grid[gx, gy] : 0;

            return new CellInfo {
                DepUT = depUT,
                ArrUT = arrUT,
                ToFDays = tofDays,
                TotalDV = totDV,
                EjectDV = ejDV,
                InsertDV = inDV,
                C3 = c3,
                IsValid = !double.IsNaN(totDV) && totDV < MGADefaults.PorkchopMaxValidDv
            };
        }
    }

    public static class PorkchopGenerator {
        public static PorkchopGrid ComputePorkchop(
            string fromBody, string toBody, 
            double depStartUT, double depSpanDays, 
            double tofMinDays, double tofMaxDays,
            double depAltKm = MGADefaults.DepartureAltitudeKm, double arrAltKm = MGADefaults.DestinationAltitudeKm,
            bool flybyOnly = false,
            int gridW = MGADefaults.PorkchopDefaultWidth, int gridH = MGADefaults.PorkchopDefaultHeight,
            Action<float, string> onProgress = null,
            bool forceCpu = false) 
        {
            return ComputeInternal(fromBody, toBody, depStartUT, depSpanDays, tofMinDays, tofMaxDays, depAltKm, arrAltKm, flybyOnly, gridW, gridH, onProgress, forceCpu);
        }

        public static async Task<PorkchopGrid> ComputePorkchopAsync(
            string fromBody, string toBody, 
            double depStartUT, double depSpanDays, 
            double tofMinDays, double tofMaxDays,
            double depAltKm = MGADefaults.DepartureAltitudeKm, double arrAltKm = MGADefaults.DestinationAltitudeKm,
            bool flybyOnly = false,
            int gridW = MGADefaults.PorkchopDefaultWidth, int gridH = MGADefaults.PorkchopDefaultHeight,
            Action<float, string> onProgress = null,
            bool forceCpu = false) 
        {
            var grid = await Task.Run(() => ComputeInternal(
                fromBody, toBody, 
                depStartUT, depSpanDays, 
                tofMinDays, tofMaxDays, 
                depAltKm, arrAltKm,
                flybyOnly, gridW, gridH, onProgress, forceCpu));

            // Safely render texture on the main thread after Task.Run completes
            grid.PlotTexture = RenderPorkchopTexture(grid);
            return grid;
        }

        private static PorkchopGrid ComputeInternal(
            string fromBody, string toBody, 
            double depStartUT, double depSpanDays, 
            double tofMinDays, double tofMaxDays,
            double depAltKm, double arrAltKm,
            bool flybyOnly, int gridW, int gridH,
            Action<float, string> onProgress,
            bool forceCpu = false) 
        {
            var grid = new PorkchopGrid {
                Width = gridW,
                Height = gridH,
                FromBody = fromBody,
                ToBody = toBody,
                DepStartUT = depStartUT,
                DepSpanDays = depSpanDays,
                ToFMinDays = tofMinDays,
                ToFMaxDays = tofMaxDays,
                DepAltKm = depAltKm,
                ArrAltKm = arrAltKm,
                FlybyOnly = flybyOnly,
                DeltaVGrid = new double[gridW, gridH],
                EjectDVGrid = new double[gridW, gridH],
                InsertDVGrid = new double[gridW, gridH],
                C3Grid = new double[gridW, gridH]
            };

            var p1 = SolarSystemCatalog.GetPlanet(fromBody);
            var p2 = SolarSystemCatalog.GetPlanet(toBody);
            string central = SolarSystemCatalog.ResolveTransferCentralBody(fromBody, toBody);
            double centralMu = SolarSystemCatalog.GetCentralMu(central);

            if (string.IsNullOrEmpty(fromBody) || string.IsNullOrEmpty(toBody) ||
                fromBody.Equals(toBody, StringComparison.OrdinalIgnoreCase)) {
                KSPGMATLog.Warn("出发天体与目标天体相同，无法生成猪排图");
                return null;
            }

            if (!PykepNative.IsAvailable) {
                KSPGMATLog.Error("PykepNative 原生库未就绪，无法生成猪排图");
            }

            bool gpuSuccess = false;
            if (!forceCpu && PykepNative.IsGpuComputeAvailable && p1 != null && p2 != null && p1.SMA > 0 && p2.SMA > 0 &&
                !fromBody.Equals(central, StringComparison.OrdinalIgnoreCase) &&
                !toBody.Equals(central, StringComparison.OrdinalIgnoreCase))
            {
                try {
                    var elem1 = new PykepNative.GpuPlanetElements {
                        sma_km = p1.SMA,
                        ecc = p1.ECC,
                        inc_rad = p1.INC * (Math.PI / 180.0),
                        raan_rad = p1.RAAN * (Math.PI / 180.0),
                        aop_rad = p1.AOP * (Math.PI / 180.0),
                        ma0_rad = p1.MA0 * (Math.PI / 180.0),
                        epoch_ut = p1.EpochJ2000,
                        mu_self = p1.Mu,
                        radius_km = p1.RadiusKm,
                        park_alt_km = depAltKm
                    };

                    var elem2 = new PykepNative.GpuPlanetElements {
                        sma_km = p2.SMA,
                        ecc = p2.ECC,
                        inc_rad = p2.INC * (Math.PI / 180.0),
                        raan_rad = p2.RAAN * (Math.PI / 180.0),
                        aop_rad = p2.AOP * (Math.PI / 180.0),
                        ma0_rad = p2.MA0 * (Math.PI / 180.0),
                        epoch_ut = p2.EpochJ2000,
                        mu_self = p2.Mu,
                        radius_km = p2.RadiusKm,
                        park_alt_km = arrAltKm
                    };

                    int totalPts = gridW * gridH;
                    float[] outTotDv = new float[totalPts];
                    float[] outC3 = new float[totalPts];

                    int ret = PykepNative.PorkchopGpuEvaluate(
                        ref elem1, ref elem2,
                        centralMu,
                        depStartUT, depSpanDays * 86400.0,
                        tofMinDays * 86400.0, tofMaxDays * 86400.0,
                        gridW, gridH,
                        flybyOnly,
                        outTotDv, outC3);

                    if (ret == 0) {
                        for (int gx = 0; gx < gridW; gx++) {
                            for (int gy = 0; gy < gridH; gy++) {
                                int idx = gy * gridW + gx;
                                float dv = outTotDv[idx];
                                float c3 = outC3[idx];
                                grid.DeltaVGrid[gx, gy] = (dv < 1e7f) ? (double)dv : double.NaN;
                                grid.C3Grid[gx, gy] = (c3 < 1e7f) ? (double)c3 : double.NaN;
                            }
                        }
                        grid.ComputeBackend = $"DirectCompute HLSL GPU ({PykepNative.GpuDeviceName})";
                        gpuSuccess = true;
                    } else {
                        KSPGMATLog.Warn($"[PorkchopGenerator] GPU 返回降级码 ({ret})，自动平滑回退至多核 CPU (AVX2)");
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Warn($"[PorkchopGenerator] GPU 求解异常，安全降级至 CPU: {ex.Message}");
                    gpuSuccess = false;
                }
            }

            if (!gpuSuccess) {
                grid.ComputeBackend = $"Multi-Thread CPU ({(PykepNative.HasAVX2 ? "AVX2+FMA" : "SSE2")})";
                Parallel.For(0, gridW, gx => {
                    double depUT = depStartUT + (gx / (double)(gridW - 1)) * (depSpanDays * 86400.0);

                    for (int gy = 0; gy < gridH; gy++) {
                        double tofDays = tofMinDays + (gy / (double)(gridH - 1)) * (tofMaxDays - tofMinDays);
                        double tofSec = tofDays * 86400.0;
                        double arrUT = depUT + tofSec;

                        double ejDV = 1e6;
                        double inDV = 0;
                        double totDV = 1e6;
                        double c3 = 1e6;

                        // 官方 PyKEP Izzo 兰伯特求解器高精网格计算 (自适应中心天体历表, 纯正 JPL/VSOP2013 历表，零畸变)
                        p1.GetRelativeState(central, depUT, out Vector3d r1, out Vector3d vP1);
                        p2.GetRelativeState(central, arrUT, out Vector3d r2, out Vector3d vP2);

                        double cRadius = SolarSystemCatalog.GetPlanet(central)?.RadiusKm ?? MGADefaults.EarthRadiusKm;
                        if (r1.sqrMagnitude < 1.0) {
                            double rPark = (p1 != null && p1.RadiusKm > 0 ? p1.RadiusKm : cRadius) + depAltKm;
                            PykepNative.SynthesizeParkingState(centralMu, rPark, false, 0.0, 0.0, r2, vP2, out r1, out vP1);
                        }
                        if (r2.sqrMagnitude < 1.0) {
                            double rPark = (p2 != null && p2.RadiusKm > 0 ? p2.RadiusKm : cRadius) + arrAltKm;
                            PykepNative.SynthesizeParkingState(centralMu, rPark, false, 0.0, 0.0, r1, vP1, out r2, out vP2);
                        }

                        if (PykepNative.SolveLambert(r1, r2, tofSec, centralMu, false, out Vector3d v1, out Vector3d v2)) {
                            Vector3d vInfDepVec = v1 - vP1;
                            Vector3d vInfArrVec = v2 - vP2;
                            double vInfDep = vInfDepVec.magnitude;
                            double vInfArr = vInfArrVec.magnitude;
                            c3 = vInfDep * vInfDep;   // 原生兰伯特输出 + 原生历表速度的精确超速能量 (非近似)

                            // 100% 原生 C++ 动力学计算脱离脉冲与捕获脉冲 (0 托管自编公式)
                            double depDvKm;
                            if (fromBody.Equals(central, StringComparison.OrdinalIgnoreCase)) {
                                depDvKm = vInfDep;
                            } else {
                                depDvKm = PykepBridge.CalculateDepartureDeltaV(vInfDep, p1, depAltKm, depAltKm);
                            }
                            if (!double.IsNaN(depDvKm)) {
                                ejDV = depDvKm * 1000.0;
                                if (!flybyOnly) {
                                    double arrDvKm = PykepBridge.CalculateArrivalInsertionDeltaV(vInfArr, p2, arrAltKm, arrAltKm, false);
                                    if (!double.IsNaN(arrDvKm)) {
                                        inDV = arrDvKm * 1000.0;
                                    }
                                }
                                totDV = ejDV + inDV;
                            } else {
                                ejDV = double.NaN;
                                inDV = double.NaN;
                                totDV = double.NaN;
                            }
                        }

                        grid.DeltaVGrid[gx, gy] = totDV;
                        grid.EjectDVGrid[gx, gy] = ejDV;
                        grid.InsertDVGrid[gx, gy] = inDV;
                        grid.C3Grid[gx, gy] = c3;
                    }
                });
            }

            // Find global minimum
            for (int x = 0; x < gridW; x++) {
                for (int y = 0; y < gridH; y++) {
                    double val = grid.DeltaVGrid[x, y];
                    if (val < grid.BestTotalDV) {
                        grid.BestTotalDV = val;
                        grid.BestX = x;
                        grid.BestY = y;
                    }
                }
            }

            grid.BestDepUT = depStartUT + (grid.BestX / (double)(gridW - 1)) * (depSpanDays * 86400.0);
            grid.BestToFDays = tofMinDays + (grid.BestY / (double)(gridH - 1)) * (tofMaxDays - tofMinDays);
            grid.BestArrUT = grid.BestDepUT + grid.BestToFDays * 86400.0;

            return grid;
        }

        private static List<Color> _twpPalette = null;
        public static List<Color> GetTWPPalette() {
            if (_twpPalette != null) return _twpPalette;
            var list = new List<Color>();
            for (int i = 64; i <= 68; i++) list.Add(new Color32(64, (byte)i, 255, 255));
            for (int i = 133; i <= 255; i++) list.Add(new Color32(128, (byte)i, 255, 255));
            for (int i = 255; i >= 128; i--) list.Add(new Color32(128, 255, (byte)i, 255));
            for (int i = 128; i <= 255; i++) list.Add(new Color32((byte)i, 255, 128, 255));
            for (int i = 255; i >= 128; i--) list.Add(new Color32(255, (byte)i, 128, 255));
            _twpPalette = list;
            return _twpPalette;
        }

        public static Texture2D RenderPorkchopTexture(PorkchopGrid grid) {
            int w = grid.Width;
            int h = grid.Height;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            var pal = GetTWPPalette();
            Color[] pixels = new Color[w * h];

            double minDV = double.MaxValue, maxDV = 0.0;
            double sumLog = 0.0, sumSqLog = 0.0;
            int validCount = 0;

            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    double dv = grid.DeltaVGrid[x, y];
                    if (!double.IsNaN(dv) && dv > 0 && dv < 1e5) {
                        if (dv < minDV) minDV = dv;
                        if (dv > maxDV) maxDV = dv;
                        double log = Math.Log(dv);
                        sumLog += log;
                        sumSqLog += log * log;
                        validCount++;
                    }
                }
            }

            if (validCount == 0 || minDV >= 1e5) minDV = 3500.0;
            double logMin = Math.Log(minDV);
            double mean = validCount > 0 ? sumLog / validCount : logMin;
            double stddev = validCount > 1 ? Math.Sqrt(Math.Max(0.0, sumSqLog / validCount - mean * mean)) : 0.5;
            double logMax = Math.Min(Math.Log(Math.Max(minDV * 1.5, maxDV)), mean + 2.0 * stddev);
            if (logMax <= logMin + 0.01) logMax = logMin + 0.5;

            double displayMaxDV = Math.Exp(logMax);

            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    double dv = grid.DeltaVGrid[x, y];
                    if (double.IsNaN(dv) || dv <= 0 || dv >= 1e5) {
                        pixels[y * w + x] = new Color(0.10f, 0.12f, 0.18f, 1.0f);
                    } else {
                        double logVal = Math.Log(dv);
                        double rel = (logVal - logMin) / (logMax - logMin);
                        int idx = Mathf.Clamp((int)Math.Floor(rel * pal.Count), 0, pal.Count - 1);
                        pixels[y * w + x] = pal[idx];
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            // Generate 16x256 vertical colorbar texture (matching TWP legend)
            Texture2D barTex = new Texture2D(16, 256, TextureFormat.RGBA32, false);
            Color[] barPix = new Color[16 * 256];
            for (int y = 0; y < 256; y++) {
                float rel = y / 255.0f;
                int idx = Mathf.Clamp((int)Math.Floor(rel * pal.Count), 0, pal.Count - 1);
                Color c = pal[idx];
                for (int x = 0; x < 16; x++) {
                    barPix[y * 16 + x] = c;
                }
            }
            barTex.SetPixels(barPix);
            barTex.Apply();

            grid.ScaleMinDV = minDV;
            grid.ScaleMaxDV = displayMaxDV;
            grid.ColorBarTexture = barTex;

            return tex;
        }
    }
}
