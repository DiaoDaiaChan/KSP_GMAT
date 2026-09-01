import os
import sys

raw_script = """% -------------------------------------------------------------
% GMAT Spacecraft & Hardware Architecture (Mod-wide Sat)
% -------------------------------------------------------------
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '09 Mar 1960 00:56:51.031';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 5405.336241;
Sat.Y = -2646.042322;
Sat.Z = -2616.991087;
Sat.VX = 2.478650;
Sat.VY = 7.104659;
Sat.VZ = -2.050452;
Sat.DryMass = 1477.96;
Sat.DragArea = 15.00;
Sat.Cd = 2.20;
Sat.Cr = 1.80;
Sat.SRPArea = 15.00;

Create ChemicalTank Tank0;
Tank0.AllowNegativeFuelMass = true;
Tank0.FuelMass = 61.32;
Tank0.Volume = 10.00;
Tank0.PressureModel = 'PressureRegulated';
Create ChemicalTank Tank1;
Tank1.AllowNegativeFuelMass = true;
Tank1.FuelMass = 6282.92;
Tank1.Volume = 62.83;
Tank1.PressureModel = 'PressureRegulated';

Create ChemicalThruster Engine11178kNThruster;
Engine11178kNThruster.CoordinateSystem = Local;
Engine11178kNThruster.Origin = Earth;
Engine11178kNThruster.Axes = VNB;
Engine11178kNThruster.ThrustDirection1 = 1;
Engine11178kNThruster.ThrustDirection2 = 0;
Engine11178kNThruster.ThrustDirection3 = 0;
Engine11178kNThruster.DecrementMass = true;
Engine11178kNThruster.Tank = {Tank0};
Engine11178kNThruster.C1 = 2163.94;
Engine11178kNThruster.K1 = 317.79;
Create ChemicalThruster RL10SeriesNonExtendableNozzle;
RL10SeriesNonExtendableNozzle.CoordinateSystem = Local;
RL10SeriesNonExtendableNozzle.Origin = Earth;
RL10SeriesNonExtendableNozzle.Axes = VNB;
RL10SeriesNonExtendableNozzle.ThrustDirection1 = 1;
RL10SeriesNonExtendableNozzle.ThrustDirection2 = 0;
RL10SeriesNonExtendableNozzle.ThrustDirection3 = 0;
RL10SeriesNonExtendableNozzle.DecrementMass = true;
RL10SeriesNonExtendableNozzle.Tank = {Tank1};
RL10SeriesNonExtendableNozzle.C1 = 108100.00;
RL10SeriesNonExtendableNozzle.K1 = 464.80;

Sat.Tanks = {Tank0, Tank1};
Sat.Thrusters = {Engine11178kNThruster, RL10SeriesNonExtendableNozzle};

Create FiniteBurn GTOBurn;
GTOBurn.Thrusters = {RL10SeriesNonExtendableNozzle};
GTOBurn.ThrottleLogicAlgorithm = 'MaxNumberOfThrusters';

Create FiniteBurn GEOBurn;
GEOBurn.Thrusters = {RL10SeriesNonExtendableNozzle};
GEOBurn.ThrottleLogicAlgorithm = 'MaxNumberOfThrusters';

Create ForceModel LEOPropForceModel;
Create Propagator LEOProp;
LEOProp.FM = LEOPropForceModel;
LEOProp.Type = RungeKutta89;
Create ForceModel CislunarPropForceModel;
Create Propagator CislunarProp;
CislunarProp.FM = CislunarPropForceModel;
CislunarProp.Type = RungeKutta89;
Create ForceModel LunarNRHOPropForceModel;
Create Propagator LunarNRHOProp;
LunarNRHOProp.FM = LunarNRHOPropForceModel;
LunarNRHOProp.Type = RungeKutta89;

Create DifferentialCorrector DCGTO;
DCGTO.ShowProgress = true;
DCGTO.DerivativeMethod = ForwardDifference;
DCGTO.Algorithm = NewtonRaphson;
DCGTO.MaximumIterations = 50;

Create DifferentialCorrector DCGEO;
DCGEO.ShowProgress = true;
DCGEO.DerivativeMethod = ForwardDifference;
DCGEO.Algorithm = NewtonRaphson;
DCGEO.MaximumIterations = 50;

Create Variable GTODuration GEODuration GTO_Duration GEO_Duration;

% -------------------------------------------------------------
% GMAT Mission Sequence Execution Control
% -------------------------------------------------------------
BeginMissionSequence;

% Propagate (LEOProp)
Propagate LEOProp(Sat) {Sat.Earth.Latitude = 0.0};
% Target Group (DC_GTO)
Target DC_GTO {SolveMode = Solve, ExitMode = SaveAndContinue};
   % Vary (GTO_Duration)
   Vary DC_GTO(GTO_Duration = 120.0, {Perturbation = 0.0001, Lower = 12, Upper = 650, MaxStep = 0.1});
   % Finite Burn (GTOBurn)
   BeginFiniteBurn GTOBurn(Sat);
   Propagate LEOProp(Sat) {Sat.ElapsedSecs = GTO_Duration};
   EndFiniteBurn GTOBurn(Sat);
% Propagate (LEOProp)
Propagate LEOProp(Sat) {Sat.Earth.Apoapsis};
   % Achieve (Sat.Earth.Altitude)
   Achieve DC_GTO(Sat.Earth.Altitude = 35786.0, {Tolerance = 1});
EndTarget;  % For DefaultDC
% Target Group (DC_GEO)
Target DC_GEO {SolveMode = Solve, ExitMode = SaveAndContinue};
   % Vary (GEO_Duration)
   Vary DC_GEO(GEO_Duration = 70.0, {Perturbation = 0.0001, Lower = 7, Upper = 400, MaxStep = 0.1});
   % Thrust Vector Configuration: RL10SeriesNonExtendableNozzle (VNB Axes)
   Sat.RL10SeriesNonExtendableNozzle.ThrustDirection1 = 1;
   Vary DC_GEO(Sat.RL10SeriesNonExtendableNozzle.ThrustDirection2 = 0.5, {Perturbation = 0.0001, Lower = -3, Upper = 3, MaxStep = 0.1});
   Sat.RL10SeriesNonExtendableNozzle.ThrustDirection3 = 0;
   % Finite Burn (GEOBurn)
   BeginFiniteBurn GEOBurn(Sat);
   Propagate LEOProp(Sat) {Sat.ElapsedSecs = GEO_Duration};
   EndFiniteBurn GEOBurn(Sat);
% Propagate (LEOProp)
Propagate LEOProp(Sat) {Sat.Earth.Periapsis};
   % Achieve (Sat.Earth.SMA)
   Achieve DC_GEO(Sat.Earth.SMA = 42164.14, {Tolerance = 1});
   % Achieve (Sat.EarthMJ2000Eq.VZ)
   Achieve DC_GEO(Sat.EarthMJ2000Eq.VZ = 0.0, {Tolerance = 0.01});
EndTarget;  % For DefaultDC
% Propagate (LEOProp)
Propagate LEOProp(Sat) {Sat.ElapsedSecs = 0.0};
% Trajectory Report
Report TrajReport Sat.UTCGregorian Sat.X Sat.Y Sat.Z;
"""

sys.path.insert(0, os.path.abspath(r"c:\Users\43701\gmat-win-R2026a\bin\KSP_GMAT\gmat_service"))
import gmat_engine
res = gmat_engine.run_raw_script(raw_script)
print("GMAT Run Result:", res.get("success"), res.get("error"))
