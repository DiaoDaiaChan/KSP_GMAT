import os
import sys

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import gmatpy as gmat
gmat_engine.init_gmat()

confirmed_resources = [
    "Spacecraft", "ChemicalTank", "ElectricTank", "ChemicalThruster", "ElectricThruster",
    "NuclearPowerSystem", "SolarPowerSystem",
    "Propagator", "ForceModel", "PointMassForce", "DragForce", "RelativisticCorrection",
    "ODEModel", "PrinceDormand78", "RungeKutta89", "RungeKutta68", "RungeKutta56", "AdamsBashforthMoulton",
    "ImpulsiveBurn", "FiniteBurn",
    "DifferentialCorrector",
    "Planet", "Moon", "Barycenter", "LibrationPoint", "CoordinateSystem",
    "ReportFile", "EphemerisFile", "OrbitView", "GroundTrackPlot", "XYPlot",
    "Variable", "Array", "String"
]

for r in confirmed_resources:
    print(f"Constructing resource: {r} ... ", end="")
    obj = gmat.Construct(r, f"Probe_{r}")
    print(f"Created. Count: {obj.GetParameterCount()}")
    for i in range(obj.GetParameterCount()):
        try:
            name = obj.GetParameterText(i)
        except Exception as e:
            print(f"   Param {i} error: {e}")
            break
    print("Done.")

print("All resources done.")
