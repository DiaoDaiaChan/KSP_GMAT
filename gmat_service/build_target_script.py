import os
import gmatpy as gmat

script_content = '''
%-------------------------------------------------------------------------------
% True Lunar NRHO Targeting from In-Game Vessel State (08 Mar 1960)
%-------------------------------------------------------------------------------

Create Spacecraft Sat;
Sat.DateFormat       = UTCGregorian;
Sat.Epoch            = '08 Mar 1960 00:59:47.536';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X                = -1244.658658;
Sat.Y                = -6255.014130;
Sat.Z                =  1578.466612;
Sat.VX               =  6.759596;
Sat.VY               = -2.154297;
Sat.VZ               = -3.220852;
Sat.DryMass          = 1652.69;

Create ForceModel EarthMoonFM;
EarthMoonFM.CentralBody            = Earth;
EarthMoonFM.PointMasses            = {Earth, Luna, Sun};
EarthMoonFM.Drag                   = None;
EarthMoonFM.SRP                    = On;
EarthMoonFM.RelativisticCorrection = Off;
EarthMoonFM.ErrorControl           = RSSStep;

Create ForceModel MoonFM;
MoonFM.CentralBody            = Luna;
MoonFM.PointMasses            = {Luna, Earth, Sun};
MoonFM.Drag                   = None;
MoonFM.SRP                    = On;
MoonFM.RelativisticCorrection = Off;
MoonFM.ErrorControl           = RSSStep;

Create Propagator LEOProp;
LEOProp.FM              = EarthMoonFM;
LEOProp.Type            = RungeKutta89;
LEOProp.InitialStepSize = 30;
LEOProp.Accuracy        = 1e-11;

Create Propagator CislunarProp;
CislunarProp.FM              = EarthMoonFM;
CislunarProp.Type            = RungeKutta89;
CislunarProp.InitialStepSize = 60;
CislunarProp.Accuracy        = 1e-11;
CislunarProp.MaxStep         = 7200;

Create Propagator MoonProp;
MoonProp.FM              = MoonFM;
MoonProp.Type            = RungeKutta89;
MoonProp.InitialStepSize = 60;
MoonProp.Accuracy        = 1e-11;
MoonProp.MaxStep         = 3600;

Create ImpulsiveBurn TLI;
TLI.CoordinateSystem   = Local;
TLI.Origin             = Earth;
TLI.Axes               = VNB;
TLI.Element1           = 3.135;
TLI.Element2           = 0.0;
TLI.Element3           = 0.0;

Create ImpulsiveBurn NRHOI;
NRHOI.CoordinateSystem = Local;
NRHOI.Origin           = Luna;
NRHOI.Axes             = VNB;
NRHOI.Element1         = -0.780;
NRHOI.Element2         = 0.0;
NRHOI.Element3         = 0.0;

Create CoordinateSystem MoonInertial;
MoonInertial.Origin = Luna;
MoonInertial.Axes   = BodyInertial;

Create CoordinateSystem EarthMoonRot;
EarthMoonRot.Origin    = Luna;
EarthMoonRot.Axes      = ObjectReferenced;
EarthMoonRot.XAxis     = R;
EarthMoonRot.ZAxis     = N;
EarthMoonRot.Primary   = Earth;
EarthMoonRot.Secondary = Luna;

Create Variable CoastSecs TOF_Days MinPerilune;
CoastSecs   = 880.0;
TOF_Days    = 4.5;
MinPerilune = 3229.0;

Create DifferentialCorrector DC;
DC.ShowProgress = true;
DC.MaxIterations = 50;

Create ReportFile TargetingReport;
TargetingReport.Filename = 'd:/nocomp/gmat-win-R2026a/output/true_lunar_target_report.txt';
TargetingReport.Add = {Sat.ElapsedDays, Sat.Earth.RMAG, Sat.Luna.RMAG, Sat.EarthMoonRot.X, Sat.EarthMoonRot.Y, Sat.EarthMoonRot.Z};

BeginMissionSequence;

Target 'Target Moon Perilune' DC {SolveMode = Solve, ExitMode = SaveAndContinue};
    Vary 'Vary Coast' DC(CoastSecs = 880.0, {Perturbation = 1.0, MaxStep = 50.0, Lower = 500.0, Upper = 2000.0});
    Vary 'Vary TLI_V' DC(TLI.Element1 = 3.135, {Perturbation = 0.0001, MaxStep = 0.01, Lower = 3.10, Upper = 3.20});
    Vary 'Vary TLI_N' DC(TLI.Element2 = 0.0,   {Perturbation = 0.0001, MaxStep = 0.02, Lower = -0.3, Upper = 0.3});

    Propagate 'Coast to Nodal Injection' LEOProp(Sat) {Sat.ElapsedSecs = CoastSecs};
    Maneuver 'Execute TLI' TLI(Sat);
    Propagate 'Fly to Moon' CislunarProp(Sat) {Sat.Luna.Periapsis, Sat.ElapsedDays = 6.0};

    Achieve 'Achieve Perilune Distance' DC(Sat.Luna.RMAG = 3229.0, {Tolerance = 200.0});
    Achieve 'Achieve Equatorial Cross'  DC(Sat.EarthMoonRot.Y = 0.0, {Tolerance = 500.0});
EndTarget;

Maneuver 'Capture into NRHO' NRHOI(Sat);
Propagate 'Stay in Lunar Orbit' MoonProp(Sat) {Sat.ElapsedDays = 14.0};
'''

script_path = 'd:/nocomp/gmat-win-R2026a/bin/true_lunar_target.script'
with open(script_path, 'w', encoding='utf-8') as f:
    f.write(script_content)

print(f"Written target script to {script_path}")
