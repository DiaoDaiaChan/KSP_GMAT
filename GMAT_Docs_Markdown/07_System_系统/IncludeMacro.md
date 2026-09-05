# #Include Macro

> 来源：第 1746-1754 页

#Include Macro
#Include Macro — Load or import a script snippet
Script Syntax
#Include './Define_Path_to_Script_Snippet_File_In_SingleQuotes.txt'
Description
Using the #Include macro, GMAT now allows you to load GMAT resources and
script snippets from external files during the script initialization and mission
execution. This is a powerful feature that allows you to reuse configurations
across multiple users and/or scripts. This feature can be used to simplify
automation for operations and Monte-Carlo and parametric scanning that have
use cases with a lot of common data but some data that changes from one
execution to the next.
The script snippet external files that you can now load using the #Include macro
can be defined with any file extensions, although most common file extensions
are (*.script) or (*.txt). The #Include macro can be used to load snippets from
external files either before or after the BeginMissionSequence script command.
The #Include macro can only be used through the script mode and its usage is
not allowed via the GUI.
GUI
There are two rules in regards to how GMAT's GUI behaves whenever we use
the #Include macro:
1. If any #Include macro is used before BeginMissionSequence, then
GMAT’s GUI is editable, runnable but you cannot save GMAT scripts from
the GUI's Save button. You can of course make changes to your script in the
Script mode and save your changes from the script mode.
2. If there are no #Include macros before BeginMissionSequence and there
are any number of #Include macros after BeginMissionSequence, then
GMAT’s GUI is editable, runnable and savable (i.e. you can make changes
to objects in the GUI and then save those changes to the script from the
GUI's Save button).
Whenever you load and run GMAT scripts that may use an #Include macro
before BeginMissionSequence command, (i.e. Rule # 1 defined above), then
GMAT’s Resources, Mission and Output trees will change color to a light olive
green and a Non-Savable GUI Mode message will show up in red color at the top
center of the main GMAT screen. This light olive green color change and Non-
Savable GUI Mode message is simply telling you that GMAT's GUI is editable,
runnable but you cannot save changes to your GMAT script via GMAT GUI's
Save button.
If your GMAT script only contains #Include macro(s) after
BeginMissionSequence (i.e. Rule # 2 defined above), then no color changes
occur in GMAT's Resources, Mission and Output trees and you can save
changes to your scripts either from GUI or script mode.
Remarks
In GMAT, the default method of defining the file path of the external file(s) that
you want to load using the #Include macro is: './My_Script_Snippet.txt'.
This is the easiest and most convenient method of defining the path of your
script snippet files as it simply requires that both your main script and script
snippet file be in the same directory. You can also define both relative
('..\My_Script_Snippet.txt') and absolute paths to your external script
snippet files.
The Examples section shows you simple yet powerful examples of how to use
the #Include macro in simplifying your main GMAT scripts.
Examples
Initialize S/C from an external script snippet file called 'Initialize_Spacecraft.txt'.
Run this example by creating a .txt file and paste contents of
'Initialize_Spacecraft.txt' and put this snippet script in same directory as the main
GMAT script.
Create Spacecraft aSat
%Initialize aSat from external file:
#Include './Initialize_Spacecraft.txt'
Create Propagator aProp
Create OrbitView anOrbitView
anOrbitView.Add = {aSat, Earth}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 0.5}
%%%%%% Contents of 'Initialize_Spacecraft.txt' snippet file begins below:
aSat.DateFormat = UTCGregorian
aSat.Epoch = '02 Jan 2000 11:59:28.000'
aSat.CoordinateSystem = EarthMJ2000Eq
aSat.DisplayStateType = Cartesian
aSat.X = 8000
aSat.Y = 2000
aSat.Z = 4000
aSat.VX = 0.5
aSat.VY = 7.5
aSat.VZ = 1.5
aSat.DryMass = 1000
aSat.Cd = 2.2
aSat.Cr = 1.8
aSat.DragArea = 20
aSat.SRPArea = 1
aSat.NAIFId = -10009001
aSat.NAIFIdReferenceFrame = -9009001
aSat.OrbitColor = Yellow
aSat.TargetColor = Teal
aSat.Id = 'SatId'
aSat.Attitude = CoordinateSystemFixed
aSat.SPADSRPScaleFactor = 1
aSat.ModelFile = 'aura.3ds'
aSat.ModelOffsetX = 0
aSat.ModelOffsetY = 0
aSat.ModelOffsetZ = 0
aSat.ModelRotationX = 0
aSat.ModelRotationY = 0
aSat.ModelRotationZ = 0
aSat.ModelScale = 1
aSat.AttitudeDisplayStateType = 'Quaternion'
aSat.AttitudeRateDisplayStateType = 'AngularVelocity'
aSat.AttitudeCoordinateSystem = EarthMJ2000Eq
aSat.EulerAngleSequence = '321'
In this example, we call an external file through #Include macro which is used
only after the BeginMissionSequence command. Perform a finite burn from an
external script snippet file called 'Perform_FiniteBurn.txt'. Run this example by
creating a .txt file and paste contents of 'Perform_FiniteBurn.txt' and put this
snippet script in same directory as the main GMAT script.
Create Spacecraft aSat
Create ChemicalTank aFuelTank
Create ChemicalThruster aThruster
aThruster.DecrementMass = true
aThruster.Tank = {aFuelTank}
aThruster.C1 = 1000 % Constant Thrust
aThruster.K1 = 300 % Constant Isp
aSat.Thrusters = {aThruster}
aSat.Tanks = {aFuelTank}
Create ForceModel aFM
aFM.CentralBody = Earth
aFM.PointMasses = {Earth}
Create Propagator aProp
aProp.FM = aFM
Create FiniteBurn aFB
aFB.Thrusters = {aThruster}
Create ReportFile rf
rf.Add = {aSat.UTCGregorian, aFB.TotalAcceleration1, ...
aFB.TotalAcceleration2, aFB.TotalAcceleration3, ...
aFB.TotalMassFlowRate, aFB.TotalThrust1, ...
aFB.TotalThrust2, aFB.TotalThrust3, ...
aSat.aThruster.MassFlowRate, ...
aSat.aThruster.ThrustMagnitude, aSat.aThruster.Isp}
Create OrbitView anOrbitView
anOrbitView.Add = {aSat, Earth}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 1000}
%Perform a FiniteBurn from an external file:
#Include './Perform_FiniteBurn.txt'
Propagate aProp(aSat) {aSat.ElapsedSecs = 1000}
%%%%%% Contents of 'Perform_FiniteBurn.txt' snippet file begins below:
% Do a Finite-Burn for 1800 Secs
BeginFiniteBurn aFB(aSat)
Propagate aProp(aSat) {aSat.ElapsedSecs = 1800, OrbitColor = Yellow}
EndFiniteBurn aFB(aSat)
In this example, we call external files through #Include macros which are used
both before and after the BeginMissionSequence. Note that all objects in the
Resources tree are imported and initialized from an external script snippet file
called 'Entire_Resources_Tree.txt'. Similarly, all commands in the Mission tree
are loaded from an external snippet file called 'Entire_Mission_Tree.txt'. Run
this example by creating a .txt file and paste contents of
'Entire_Resources_Tree.txt'. Next create another .txt file and paste contents of
'Entire_Mission_Tree.txt'. Put both of these snippet scripts in same directory as
the main GMAT script and then run the main GMAT script.
% Initialize all Resources tree objects
% from an external file:
#Include './Entire_Resources_Tree.txt'
BeginMissionSequence
% Execute all Mission tree commands
% from an external file:
#Include './Entire_Mission_Tree.txt'
%%%%%% Contents of 'Entire_Resources_Tree.txt' snippet file begins below:
Create Spacecraft aSat
Create Propagator aProp
Create ImpulsiveBurn TOI
Create DifferentialCorrector aDC
Create OrbitView anOrbitView
anOrbitView.Add = {aSat, Earth}
anOrbitView.SolverIterations = All
%%%%%% Contents of 'Entire_Mission_Tree.txt' snippet file begins below:
Propagate aProp(aSat) {aSat.Earth.Periapsis}
Target aDC
Vary aDC(TOI.Element1 = 0.24, {Perturbation = 0.001, ...
Lower = 0.0, Upper = 3.14159, MaxStep = 0.5})
Maneuver TOI(aSat)
Propagate aProp(aSat) {aSat.Earth.Apoapsis}
Achieve aDC(aSat.Earth.RMAG = 42165)
EndTarget
