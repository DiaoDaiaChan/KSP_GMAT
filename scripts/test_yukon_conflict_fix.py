import subprocess
import os

def test_script_generation():
    print("Testing GMAT script generation without DifferentialCorrector / Yukon conflict...")
    
    # 模拟刚才冲突脚本的内容，但按照修复后的逻辑：Yukon1 只声明为 Yukon，绝不被声明为 DifferentialCorrector
    script_content = """% Clean GMAT Test Script
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.Epoch = '05 Jan 1961 09:04:33.139';
Sat.CoordinateSystem = EarthMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.X = 5883.722684;
Sat.Y = -1763.640231;
Sat.Z = -2306.346269;
Sat.VX = 3.011086;
Sat.VY = 6.739158;
Sat.VZ = 2.525373;
Sat.DryMass = 1477.95;

Create ForceModel LEOPropForceModel;
LEOPropForceModel.CentralBody = Earth;
LEOPropForceModel.PointMasses = {Earth};

Create Propagator LEOProp;
LEOProp.FM = LEOPropForceModel;
LEOProp.Type = RungeKutta89;

Create Yukon Yukon1;
Yukon1.ShowProgress = true;
Yukon1.ReportStyle = Normal;
Yukon1.ReportFile = 'Yukon1.data';
Yukon1.MaximumIterations = 20;
Yukon1.UseCentralDifferences = false;
Yukon1.FeasibilityTolerance = 0.0001;
Yukon1.OptimalityTolerance = 0.0001;

Create Variable GTO_Duration;
GTO_Duration = 120.0;

BeginMissionSequence;
Optimize Yukon1 {SolveMode = Solve, ExitMode = SaveAndContinue};
   Vary Yukon1(GTO_Duration = 120.0, {Perturbation = 0.001, Lower = 20, Upper = 300, MaxStep = 10});
   Propagate LEOProp(Sat) {Sat.ElapsedSecs = GTO_Duration};
   NonlinearConstraint Yukon1(Sat.Earth.SMA = 42164.14);
EndOptimize;
"""

    test_file = r"C:\Users\43701\gmat-win-R2026a\output\test_yukon_no_conflict.script"
    with open(test_file, "w", encoding="utf-8") as f:
        f.write(script_content)

    gmat_console = r"C:\Users\43701\gmat-win-R2026a\bin\GmatConsole.exe"
    gmat_bin = r"C:\Users\43701\gmat-win-R2026a\bin"

    res = subprocess.run([gmat_console, "--run", test_file, "--exit"], cwd=gmat_bin, capture_output=True, text=True, timeout=30)
    print("Return code:", res.returncode)
    print("STDOUT:", res.stdout[:500])
    if "Interpreter Exception" in res.stdout or "Interpreter Exception" in res.stderr:
        print("ERROR: Interpreter Exception found!")
        sys.exit(1)
    else:
        print("SUCCESS: GMAT parsed and ran the Yukon script with ZERO Interpreter Exceptions!")

if __name__ == "__main__":
    test_script_generation()
