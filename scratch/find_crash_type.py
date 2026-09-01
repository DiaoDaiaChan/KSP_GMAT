import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

types = [
    "Spacecraft", "Formation", "ChemicalTank", "ElectricTank", "ChemicalThruster", "ElectricThruster",
    "NuclearPowerSystem", "SolarPowerSystem", "Antenna", "Transmitter", "Receiver", "Transponder",
    "Propagator", "ForceModel", "PointMassForce", "DragForce", "SRP", "RelativisticCorrection",
    "ODEModel", "PrinceDormand78", "RungeKutta89", "RungeKutta68", "RungeKutta56", "BulirschStoer",
    "AdamsBashforthMoulton", "SPK",
    "ImpulsiveBurn", "FiniteBurn",
    "DifferentialCorrector",
    "CelestialBody", "Planet", "Moon", "Star", "Barycenter", "LibrationPoint",
    "CoordinateSystem", "SolarSystem",
    "ReportFile", "EphemerisFile", "OrbitView", "GroundTrackPlot", "XYPlot", "DataFile",
    "EclipseLocator", "ContactLocator",
    "Variable", "Array", "String"
]

for t in types:
    sys.stdout.write(f"Testing {t}... ")
    sys.stdout.flush()
    try:
        obj = gmat.Construct(t, f"p_{t}")
        if obj:
            print(f"OK ({obj.GetParameterCount()} params)")
        else:
            print("None")
    except Exception as e:
        print(f"Error: {e}")
