import os
import sys
import json
import traceback

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
import script_parser

print("========================================")
print("  GMAT PIPELINE ROBUSTNESS AUDIT SUITE  ")
print("========================================")

test_cases = [
    # 1. Standard LEO to GEO with 2 Solvers
    ("Standard 2-Stage Targeter", """
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '09 Mar 1960 00:56:51.031';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 5405.33; Sat.Y = -2646.04; Sat.Z = -2616.99;
Sat.VX = 2.47; Sat.VY = 7.10; Sat.VZ = -2.05;
Sat.DryMass = 1000;
Create ChemicalTank Tank0;
Tank0.FuelMass = 5000;
Create ChemicalThruster Thruster1;
Thruster1.Tank = {Tank0};
Thruster1.C1 = 100000; Thruster1.K1 = 300;
Sat.Tanks = {Tank0};
Sat.Thrusters = {Thruster1};
Create FiniteBurn Burn1;
Burn1.Thrusters = {Thruster1};
Create ForceModel FM;
FM.CentralBody = Earth;
Create Propagator Prop;
Prop.FM = FM;
Create DifferentialCorrector DC1;
Create Variable dur1;
BeginMissionSequence;
Target DC1;
  Vary DC1(dur1 = 100, {Lower = 10, Upper = 300, MaxStep = 5});
  BeginFiniteBurn Burn1(Sat);
  Propagate Prop(Sat) {Sat.ElapsedSecs = dur1};
  EndFiniteBurn Burn1(Sat);
  Propagate Prop(Sat) {Sat.Earth.Apoapsis};
  Achieve DC1(Sat.Earth.Altitude = 35786, {Tolerance = 10});
EndTarget;
"""),

    # 2. Single Propagate Step (Minimal)
    ("Minimal Propagate Only", """
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2000 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 7000; Sat.Y = 0; Sat.Z = 0;
Sat.VX = 0; Sat.VY = 7.5; Sat.VZ = 0;
Create ForceModel FM;
Create Propagator Prop;
Prop.FM = FM;
BeginMissionSequence;
Propagate Prop(Sat) {Sat.ElapsedSecs = 1200};
"""),

    # 3. Target with Non-standard Variable Name
    ("Custom Variable In Targeting", """
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2000 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 6678; Sat.Y = 0; Sat.Z = 0;
Sat.VX = 0; Sat.VY = 7.72; Sat.VZ = 0;
Create ImpulsiveBurn Maneuver1;
Create ForceModel FM;
Create Propagator Prop;
Prop.FM = FM;
Create DifferentialCorrector DC_Custom;
Create Variable my_delta_v;
BeginMissionSequence;
Target DC_Custom;
  Vary DC_Custom(my_delta_v = 1500, {Lower = 500, Upper = 3000, MaxStep = 50});
  Maneuver Maneuver1(Sat);
  Propagate Prop(Sat) {Sat.Earth.Apoapsis};
  Achieve DC_Custom(Sat.Earth.Altitude = 10000, {Tolerance = 5});
EndTarget;
"""),

    # 4. Impulsive Burn Multi-axis Maneuver
    ("Impulsive 3-Axis Burn", """
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '01 Jan 2000 12:00:00.000';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 6800; Sat.Y = 0; Sat.Z = 0;
Sat.VX = 0; Sat.VY = 7.65; Sat.VZ = 0;
Create ImpulsiveBurn IncBurn;
IncBurn.Element1 = 0; IncBurn.Element2 = 0.5; IncBurn.Element3 = 0;
Create ForceModel FM;
Create Propagator Prop;
Prop.FM = FM;
BeginMissionSequence;
Propagate Prop(Sat) {Sat.ElapsedSecs = 300};
Maneuver IncBurn(Sat);
Propagate Prop(Sat) {Sat.ElapsedSecs = 600};
""")
]

passed_count = 0
for idx, (title, script_code) in enumerate(test_cases, 1):
    print(f"\n--- Test #{idx}: {title} ---")
    tmp_path = os.path.abspath(f"scratch/robust_test_{idx}.script")
    with open(tmp_path, "w", encoding="utf-8") as f:
        f.write(script_code.strip())
    try:
        res = gmat_engine.run_raw_script(tmp_path)
        if res.get("success"):
            print(f"  [PASS] Pipeline executed cleanly.")
            print(f"         SMA: {res.get('final_sma')} km, Mass: {res.get('final_mass')} kg, Burns: {len(res.get('solved_burns', []))}")
            passed_count += 1
        else:
            print(f"  [FAIL] Engine returned error: {res.get('error')}")
    except Exception as e:
        print(f"  [EXCEPTION] Pipeline crashed: {e}")
        traceback.print_exc()

print(f"\n========================================")
print(f"  SUMMARY: {passed_count}/{len(test_cases)} Passed ({passed_count/len(test_cases)*100:.0f}%)")
print(f"========================================")
