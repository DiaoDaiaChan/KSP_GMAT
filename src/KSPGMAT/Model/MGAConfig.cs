using System;
using System.Collections.Generic;
using KSPGMAT.MGA;
using KSPGMAT.MGA.Solver;

namespace KSPGMAT.Model {
    /// <summary>
    /// MGA 规划配置输入 (100% 对齐 KSP-MGA-Planner 与高级动力学特性)
    /// </summary>
    public class MGAConfig {
        public MGAArrivalOrbitType ArrivalOrbitType { get; set; } = MGAArrivalOrbitType.HighlyElliptic;
        public bool AutoAdaptGiantPlanetEllipse { get; set; } = true;
        public MGAPropulsionType PropulsionType { get; set; } = MGAPropulsionType.ChemicalImpulsive;

        public string CentralBody { get; set; } = "Sun";
        public bool IsHeliocentric => string.IsNullOrEmpty(CentralBody) || 
                                      CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase) || 
                                      CentralBody.Equals("Sol", StringComparison.OrdinalIgnoreCase) || 
                                      CentralBody.Equals("Kerbol", StringComparison.OrdinalIgnoreCase);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string TryGetFlightGlobalsBodyName() {
            try {
                if (FlightGlobals.ready && FlightGlobals.currentMainBody != null) {
                    return FlightGlobals.currentMainBody.name;
                }
            } catch { }
            return null;
        }

        private string _departureBody;
        public string DepartureBody {
            get {
                if (string.IsNullOrEmpty(_departureBody)) {
                    try {
                        _departureBody = TryGetFlightGlobalsBodyName();
                    } catch { }
                    if (string.IsNullOrEmpty(_departureBody)) {
                        _departureBody = (SolarSystemCatalog.GetPlanet("Earth") != null ? "Earth" : "Kerbin");
                    }
                }
                return _departureBody;
            }
            set {
                if (_departureBody != value) {
                    _departureBody = value;
                    if (!string.IsNullOrEmpty(value)) {
                        if (!LockDepartureInclination) {
                            DepartureInclinationDeg = MGADefaults.GetDefaultLaunchInclinationDeg(value);
                        }
                        double safeAlt = MGADefaults.GetSafeDepartureAltitudeKm(value);
                        if (Math.Abs(DeparturePeAKm - MGADefaults.FallbackDepartureAltitudeKm) < 1e-3 || DeparturePeAKm <= 0) {
                            DeparturePeAKm = safeAlt;
                            DepartureApAKm = safeAlt;
                        }
                    }
                }
            }
        }

        private string _destinationBody;
        public string DestinationBody {
            get {
                if (string.IsNullOrEmpty(_destinationBody)) {
                    if (_flybySequence != null && _flybySequence.Count > 0) {
                        return _flybySequence[_flybySequence.Count - 1];
                    }
                    if (!string.IsNullOrEmpty(CentralBody) && !CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase)) {
                        var sat = SolarSystemCatalog.GetMoonsOf(CentralBody);
                        if (sat != null && sat.Count > 0) return sat[0].Name;
                    }
                    _destinationBody = (SolarSystemCatalog.GetPlanet("Mars") != null ? "Mars" : "Duna");
                }
                return _destinationBody;
            }
            set {
                if (_destinationBody != value) {
                    _destinationBody = value;
                    if (!string.IsNullOrEmpty(value)) {
                        ApplyCaptureOrbitConstraints(forceResetIfInvalid: true);
                    }
                }
            }
        }

        private List<string> _flybySequence;
        public List<string> FlybySequence {
            get {
                if (_flybySequence == null || _flybySequence.Count == 0) {
                    string dest = !string.IsNullOrEmpty(_destinationBody)
                        ? _destinationBody
                        : (!string.IsNullOrEmpty(CentralBody) && !CentralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase)
                            ? (SolarSystemCatalog.GetMoonsOf(CentralBody)?.Count > 0 ? SolarSystemCatalog.GetMoonsOf(CentralBody)[0].Name : "Mars")
                            : (SolarSystemCatalog.GetPlanet("Mars") != null ? "Mars" : "Duna"));
                    _flybySequence = new List<string> { dest };
                }
                return _flybySequence;
            }
            set => _flybySequence = value;
        }
        
        public MGAEngineMode EngineMode { get; set; } = MGAEngineMode.BallisticImpulsive;
        public List<MGALegStrategy> LegStrategies { get; set; } = new List<MGALegStrategy>();
        public List<MGAFlybyStrategy> FlybyStrategies { get; set; } = new List<MGAFlybyStrategy>();

        private readonly object _syncLock = new object();
        public void SyncStrategiesWithChain(int legCount, List<string> fullChain = null) {
            lock (_syncLock) {
                MGALegStrategyManager.Synchronize(this, legCount, fullChain);
            }
        }

        public double DsmEtaDefault { get; set; } = MGADefaults.DsmEtaDefault;
        public List<double> DsmEtas { get; set; } = new List<double>();
        public List<double> FlybyRpKms { get; set; } = new List<double>();
        public List<double> FlybyBetasRad { get; set; } = new List<double>();

        public double LowThrustMaxThrustN { get; set; } = MGADefaults.LowThrustMaxThrustN;
        public double LowThrustIspSec { get; set; } = MGADefaults.LowThrustIspSec;
        public double SpacecraftWetMassKg { get; set; } = MGADefaults.SpacecraftWetMassKg;
        public int LowThrustSegments { get; set; } = MGADefaults.LowThrustSegments;
        public double TargetBDotTKm { get; set; } = 0.0;
        public double TargetBDotRKm { get; set; } = 0.0;

        public int MaxSwingBys { get; set; } = MGADefaults.MaxSwingBys;
        public int MaxResonances { get; set; } = MGADefaults.MaxResonances;
        public int MaxBackLegs { get; set; } = MGADefaults.MaxBackLegs;
        public int MaxBackSpacing { get; set; } = MGADefaults.MaxBackSpacing;
        public bool ConstrainByVesselDeltaV { get; set; } = false;
        public double VesselMaxDeltaVMs { get; set; } = 0.0;
        public double MaxDsmDeltaVMs { get; set; } = 0.0;
        public double LaunchDeltaVCeilingMs { get; set; } = 0.0;
        public MGAConstraintProfile ConstraintProfile { get; set; }
        public double DepartureToFMinFactor { get; set; } = 0.65;
        public double DepartureToFMaxFactor { get; set; } = 1.50;
        public double ResonantMaxOffsetDays { get; set; } = 0.0;
        public int LambertMaxRevs { get; set; } = 0;

        public double EarliestDepartureUT { get; set; }
        public double LatestDepartureUT { get; set; }
        public double SeedEpochUT { get; set; } = 0.0;
        public double SeedToFDays { get; set; } = 0.0;
        public double DepartureSearchDays {
            get => (LatestDepartureUT > EarliestDepartureUT) ? (LatestDepartureUT - EarliestDepartureUT) / MGADefaults.SecondsPerDay : (IsHeliocentric ? MGADefaults.InterplanetarySearchDays : MGADefaults.SystemSearchDays);
            set {
                if (value > 0) {
                    LatestDepartureUT = EarliestDepartureUT + value * MGADefaults.SecondsPerDay;
                }
            }
        }
        
        public MGADepartureMode DepartureMode { get; set; } = MGADepartureMode.PreLaunchFreeOrbit;
        public double DepartureAltitudeKm { get => DeparturePeAKm; set { DeparturePeAKm = value; DepartureApAKm = value; } }
        public double DeparturePeAKm { get; set; } = MGADefaults.DepartureAltitudeKm;
        public double DepartureApAKm { get; set; } = MGADefaults.DepartureAltitudeKm;
        public double DepartureInclinationDeg { get; set; } = MGADefaults.DefaultEarthInclinationDeg;
        public double DepartureRAANDeg { get; set; } = 0.0;
        public double DepartureAOPDeg { get; set; } = 0.0;
        public double DepartureEccentricity { get; set; } = 0.0;
        public bool LockDepartureInclination { get; set; } = false;
        public bool LockDepartureRAAN { get; set; } = false;

        public double DestinationAltitudeKm { get => DestinationPeAKm; set { DestinationPeAKm = value; DestinationApAKm = value; } }
        public double DestinationPeAKm { get; set; } = MGADefaults.DestinationAltitudeKm;
        public double DestinationApAKm { get; set; } = MGADefaults.DestinationAltitudeKm;
        public bool NoInsertionBurn { get; set; } = false;
        public bool EnableLowEnergyManifoldCapture { get; set; } = false; // 多体引力流形低能捕获与弱稳定性边界优化开关 (默认关闭，待定)

        /// <summary>
        /// 基于天体引力影响球 (SOI) 与大气顶层边界，自动应用物理刚性约束
        /// </summary>
        public void ApplyCaptureOrbitConstraints(bool forceResetIfInvalid = false) {
            string dest = DestinationBody;
            if (string.IsNullOrEmpty(dest)) return;
            MGADefaults.GetBodyCaptureAltitudeBounds(dest, out double minPeA, out double recPeA, out double maxApA);
            if (forceResetIfInvalid || DestinationPeAKm < minPeA || DestinationPeAKm > maxApA || DestinationPeAKm <= 0) {
                DestinationPeAKm = recPeA;
            }

            var destP = SolarSystemCatalog.GetPlanet(dest);
            bool isGiant = destP != null && (destP.Mu >= 5e6 || destP.RadiusKm >= 20000.0);

            if (ArrivalOrbitType == MGAArrivalOrbitType.Flyby || NoInsertionBurn) {
                // 飞掠模式：零制动
                DestinationApAKm = DestinationPeAKm;
            } else if (ArrivalOrbitType == MGAArrivalOrbitType.HighlyElliptic || (AutoAdaptGiantPlanetEllipse && isGiant)) {
                // 科学高偏心率捕获椭圆: 严格基于天体力学拉普拉斯引力球与 Hill 稳定界限推导 (ra = min(0.20 * SOI, 100 * rp))
                if (forceResetIfInvalid || DestinationApAKm <= DestinationPeAKm) {
                    double destR = destP != null ? destP.RadiusKm : 6000.0;
                    double rp = destR + DestinationPeAKm;
                    double soiKm = (destP != null && destP.SoiKm > 0) ? destP.SoiKm : destR * 100.0;
                    double ra = Math.Min(soiKm * 0.20, rp * 100.0);
                    DestinationApAKm = Math.Min(maxApA, Math.Round(ra - destR));
                }
            } else {
                if (forceResetIfInvalid || DestinationApAKm < DestinationPeAKm) {
                    DestinationApAKm = DestinationPeAKm;
                } else if (DestinationApAKm > maxApA) {
                    DestinationApAKm = maxApA;
                }
            }
        }

        public void ApplyDepartureOrbitConstraints(bool forceResetIfInvalid = false) {
            string dep = DepartureBody;
            if (string.IsNullOrEmpty(dep)) return;
            MGADefaults.GetBodyCaptureAltitudeBounds(dep, out double minPeA, out double recPeA, out double maxApA);
            if (forceResetIfInvalid || DeparturePeAKm < minPeA || DeparturePeAKm > maxApA || DeparturePeAKm <= 0) {
                DeparturePeAKm = recPeA;
            }
            if (forceResetIfInvalid || DepartureApAKm < DeparturePeAKm) {
                DepartureApAKm = DeparturePeAKm;
            } else if (DepartureApAKm > maxApA) {
                DepartureApAKm = maxApA;
            }
        }

        public double MaxDurationDays { get; set; } = MGADefaults.MaxDurationDays;
        public bool EnableMaxDuration { get; set; } = false;

        public List<double> MinToFDays { get; set; } = new List<double>();
        public List<double> MaxToFDays { get; set; } = new List<double>();
        public int PopulationSize { get; set; } = MGADefaults.PopulationSize;
        public int MaxGenerations { get; set; } = MGADefaults.MaxGenerations;
        public bool AllowPoweredFlyby { get; set; } = true;
        public bool EnableParkingOrbitPhaseAlignment { get; set; } = true;

        public MGAConfig Clone() {
            var copy = (MGAConfig)this.MemberwiseClone();
            if (this._flybySequence != null) copy._flybySequence = new List<string>(this._flybySequence);
            if (this.MinToFDays != null) copy.MinToFDays = new List<double>(this.MinToFDays);
            if (this.MaxToFDays != null) copy.MaxToFDays = new List<double>(this.MaxToFDays);
            if (this.LegStrategies != null) copy.LegStrategies = new List<MGALegStrategy>(this.LegStrategies);
            if (this.FlybyStrategies != null) copy.FlybyStrategies = new List<MGAFlybyStrategy>(this.FlybyStrategies);
            if (this.DsmEtas != null) copy.DsmEtas = new List<double>(this.DsmEtas);
            if (this.FlybyRpKms != null) copy.FlybyRpKms = new List<double>(this.FlybyRpKms);
            if (this.FlybyBetasRad != null) copy.FlybyBetasRad = new List<double>(this.FlybyBetasRad);
            return copy;
        }
    }
}
