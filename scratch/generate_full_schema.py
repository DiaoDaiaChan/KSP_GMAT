import sys
sys.path.insert(0, r'C:\Users\43701\gmat-win-R2026a\bin')
import gmatpy

gmatpy.Setup('C:/Users/43701/gmat-win-R2026a/bin/gmat_startup_file.txt')
mod = gmatpy.Moderator.Instance()
all_param_types = mod.GetListOfFactoryItems(gmatpy.PARAMETER)

print(f"Total GMAT official parameter types: {len(all_param_types)}")

# Category mapping and docstrings
categories = {
    "Keplerian": ["SMA", "ECC", "INC", "RAAN", "RADN", "AOP", "TA", "MA", "EA", "HA", "MM", "RadApo", "RadPer", "SemilatusRectum", "SMADot", "OrbitPeriod", "VelApoapsis", "VelPeriapsis", "Energy", "C3Energy"],
    "Equinoctial": ["ModEquinoctialF", "ModEquinoctialG", "ModEquinoctialH", "ModEquinoctialK", "TLONG", "TLONGDot", "EquinoctialH", "EquinoctialK", "EquinoctialP", "EquinoctialQ", "MLONG", "EquinoctialHDot", "EquinoctialKDot", "EquinoctialPDot", "EquinoctialQDot", "AltEquinoctialP", "AltEquinoctialQ"],
    "Delaunay": ["Delaunayl", "Delaunayg", "Delaunayh", "DelaunayL", "DelaunayG", "DelaunayH"],
    "BrouwerMean": ["BrouwerShortSMA", "BrouwerShortECC", "BrouwerShortINC", "BrouwerShortRAAN", "BrouwerShortAOP", "BrouwerShortMA", "BrouwerLongSMA", "BrouwerLongECC", "BrouwerLongINC", "BrouwerLongRAAN", "BrouwerLongAOP", "BrouwerLongMA"],
    "Spherical": ["Altitude", "RMAG", "VMAG", "AMAG", "RA", "DEC", "RAV", "DECV", "AZI", "FPA", "Latitude", "Longitude", "LST", "MHA", "DLA", "RLA", "PlanetodeticRMAG", "PlanetodeticLON", "PlanetodeticLAT", "PlanetodeticVMAG", "PlanetodeticAZI", "PlanetodeticHFPA"],
    "HyperbolicBPlane": ["IncomingRadPer", "IncomingC3Energy", "IncomingRHA", "IncomingDHA", "IncomingBVAZI", "OutgoingRadPer", "OutgoingC3Energy", "OutgoingRHA", "OutgoingDHA", "OutgoingBVAZI", "BdotT", "BdotR", "BVectorMag", "BVectorAngle"],
    "Cartesian": ["X", "Y", "Z", "VX", "VY", "VZ", "AX", "AY", "AZ", "HMAG", "HX", "HY", "HZ", "BetaAngle"],
    "Hardware": ["TotalMass", "DryMass", "FuelMass", "DragArea", "SRPArea", "Cd", "Cr", "CdSigma", "CrSigma", "Pressure", "Temperature", "RefTemperature", "Volume", "FuelDensity"],
    "Thruster": ["ThrustDirection1", "ThrustDirection2", "ThrustDirection3", "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "C12", "C13", "C14", "C15", "C16", "K1", "K2", "K3", "K4", "K5", "K6", "K7", "K8", "K9", "K10", "K11", "K12", "K13", "K14", "K15", "K16", "ThrustMagnitude", "Isp", "MassFlowRate", "TotalThrust1", "TotalThrust2", "TotalThrust3", "TotalAcceleration1", "TotalAcceleration2", "TotalAcceleration3", "TotalMassFlowRate", "DutyCycle", "ThrustScaleFactor", "GravitationalAccel"],
    "Attitude": ["Q1", "Q2", "Q3", "Q4", "EulerAngle1", "EulerAngle2", "EulerAngle3", "EulerAngleRate1", "EulerAngleRate2", "EulerAngleRate3", "MRP1", "MRP2", "MRP3", "AngularVelocityX", "AngularVelocityY", "AngularVelocityZ", "DCM11", "DCM12", "DCM13", "DCM21", "DCM22", "DCM23", "DCM31", "DCM32", "DCM33"],
    "Time": ["ElapsedDays", "ElapsedSecs", "CurrA1MJD", "A1ModJulian", "A1Gregorian", "TAIModJulian", "TAIGregorian", "TTModJulian", "TTGregorian", "TDBModJulian", "TDBGregorian", "UTCModJulian", "UTCGregorian"]
}

print("Categories configured successfully.")
