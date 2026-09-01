import os
import sys

raw_script = """Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '09 Mar 1960 02:17:59.931';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 1814.870912;
Sat.Y = -6133.502988;
Sat.Z = -1480.062968;
Sat.VX = 6.522736;
Sat.VY = 2.730007;
Sat.VZ = -3.285493;

Create ForceModel FM;
FM.CentralBody = Earth;
FM.PointMasses = {Earth};
Create Propagator Prop;
Prop.FM = FM;

BeginMissionSequence;
Propagate Prop(Sat) {Sat.ElapsedSecs = 0.0};
"""

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
gmat_engine.init_gmat()
global gmat
import gmatpy as gmat
gmat.LoadScript(os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\inspect_user_orbit.script"))
with open(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\inspect_user_orbit.script", "w") as f:
    f.write(raw_script)
gmat.LoadScript(os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\inspect_user_orbit.script"))
gmat.RunScript()
sat = gmat.GetRuntimeObject("Sat")
for p in ["SMA", "ECC", "INC", "RAAN", "AOP", "TA", "Altitude", "Apoapsis", "Periapsis"]:
    print(f"{p}: {sat.GetRealParameter(p)}")
