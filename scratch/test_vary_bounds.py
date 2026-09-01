import sys
import os

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
from script_parser import parse_gmat_script

script = """
BeginMissionSequence;
Target DC_GTO {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary DC_GTO(GTO_Duration = 120.0, {Perturbation = 0.0001, Lower = 12, Upper = 650, MaxStep = 0.1});
   BeginFiniteBurn GTO_Burn(Sat);
   Propagate LEOProp(Sat) {Sat.ElapsedSecs = GTO_Duration};
   EndFiniteBurn GTO_Burn(Sat);
   Propagate LEOProp(Sat) {Sat.Earth.Apoapsis};
   Achieve DC_GTO(Sat.Earth.Altitude = 35786.0, {Tolerance = 1});
EndTarget;
Target DC_GEO {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary DC_GEO(GEO_Duration = 50, {Perturbation = 0.0001, Lower = 5, Upper = 300, MaxStep = 0.1});
   Sat.RL10SeriesNonExtendableNozzle.ThrustDirection1 = 1;
   Vary DC_GEO(Sat.RL10SeriesNonExtendableNozzle.ThrustDirection2 = 0.5, {Perturbation = 0.0001, Lower = -3, Upper = 3, MaxStep = 0.1});
   Sat.RL10SeriesNonExtendableNozzle.ThrustDirection3 = 0;
   BeginFiniteBurn GEO_Burn(Sat);
   Propagate LEOProp(Sat) {Sat.ElapsedSecs = GEO_Duration};
   EndFiniteBurn GEO_Burn(Sat);
   Propagate LEOProp(Sat) {Sat.Earth.Periapsis};
   Achieve DC_GEO(Sat.Earth.SMA = 42164.14, {Tolerance = 1});
   Achieve DC_GEO(Sat.EarthMJ2000Eq.VZ = 0.0, {Tolerance = 0.01});
EndTarget;
"""

fn = r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\scratch\test_vary_bounds.script"
with open(fn, "w") as f: f.write(script)
res = parse_gmat_script(fn)
for s in res["sequence"]:
    print(s.get("type"), s.get("label"), "low:", s.get("vary_lower"), "up:", s.get("vary_upper"), "step:", s.get("vary_max_step"))
