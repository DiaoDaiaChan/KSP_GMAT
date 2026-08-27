import os

script_content = '''
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

Create ImpulsiveBurn TLI;
TLI.CoordinateSystem   = Local;
TLI.Origin             = Earth;
TLI.Axes               = VNB;
TLI.Element1           = 3.13;
TLI.Element2           = 0.0;
TLI.Element3           = 0.0;

Create ImpulsiveBurn LOI;
LOI.CoordinateSystem   = Local;
LOI.Origin             = Luna;
LOI.Axes               = VNB;
LOI.Element1           = -0.800;

Create CoordinateSystem EarthMoonRot;
EarthMoonRot.Origin    = Luna;
EarthMoonRot.Axes      = ObjectReferenced;
EarthMoonRot.XAxis     = R;
EarthMoonRot.ZAxis     = N;
EarthMoonRot.Primary   = Earth;
EarthMoonRot.Secondary = Luna;

Create Variable CoastSecs TOF_Days;
CoastSecs = 880.0;
TOF_Days  = 4.6;

Create DifferentialCorrector DC1;

Create ReportFile TargetingReport;
TargetingReport.Filename = 'd:/nocomp/gmat-win-R2026a/output/nasa_lunar_target_report.txt';
TargetingReport.Add = {Sat.ElapsedDays, Sat.Earth.RMAG, Sat.Luna.RMAG, Sat.EarthMoonRot.RA, Sat.EarthMoonRot.DEC};

BeginMissionSequence;

Target 'Coarse Lunar Target' DC1 {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary 'Vary Coast' DC1(CoastSecs = 880.0, {Perturbation = 2.0, MaxStep = 50.0, Lower = 200.0, Upper = 3000.0});
   Vary 'Vary TLI_V' DC1(TLI.Element1 = 3.13, {Perturbation = 0.0001, MaxStep = 0.02, Lower = 3.05, Upper = 3.25});
   Vary 'Vary TLI_N' DC1(TLI.Element2 = 0.0, {Perturbation = 0.0001, MaxStep = 0.05, Lower = -1.0, Upper = 1.0});
   Vary 'Vary TOF'   DC1(TOF_Days = 4.6, {Perturbation = 0.01, MaxStep = 0.1, Lower = 3.5, Upper = 5.5});
   
   Propagate 'Coast' LEOProp(Sat) {Sat.ElapsedSecs = CoastSecs};
   Maneuver 'Apply TLI' TLI(Sat);
   Propagate 'Prop To Moon' CislunarProp(Sat) {Sat.ElapsedDays = TOF_Days};
   
   Achieve 'RA = 0'  DC1(Sat.EarthMoonRot.RA = 0.0, {Tolerance = 2.0});
   Achieve 'DEC = 0' DC1(Sat.EarthMoonRot.DEC = 0.0, {Tolerance = 2.0});
EndTarget;

% Propagate to Luna Perilune
Propagate 'To Perilune' CislunarProp(Sat) {Sat.Luna.Periapsis};
Maneuver 'Lunar Orbit Insertion' LOI(Sat);
Propagate 'In Lunar Orbit' CislunarProp(Sat) {Sat.ElapsedDays = 7.0};
'''

path = 'd:/nocomp/gmat-win-R2026a/bin/test_nasa_target.script'
with open(path, 'w', encoding='utf-8') as f:
    f.write(script_content)

print(f"Written NASA-style target script to {path}")
