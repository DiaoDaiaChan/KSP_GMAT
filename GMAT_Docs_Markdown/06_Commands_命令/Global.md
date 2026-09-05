# Global

> 来源：第 1421-1429 页

Global
Global — Declare Objects as global
Script Syntax
Global ObjectList
ObjectList
ObjectList List all GMAT objects that you want to declare as global.
Description
In GMAT you can use a special command that allows you to declare GMAT
objects as global. By using the Global command, you can declare GMAT's
objects as global either through the GUI or the script mode.
The syntax for declaring objects as global is very simple. After using the Global
command, simply list the name of the objects that needs global declaration. Once
the GmatFunction resource has been declared during initialization, arguments
can be passed to and from the function as input/output by using GMAT's
CallGmatFunction command. Data that is passed into the function as input or
received from the function as output can be declared as global by using the
Global command. See the Remarks section for more details on the Global
command.
See Also: GMATFunction, CallGmatFunction
GUI
Figure below shows default settings of the Global command. By default, only
Spacecraft object is checked and declared as global. As more objects are created
by the user in GMAT's Resources tree, the list of objects that are available to be
declared as global increases.
Notice in the above figure that GMAT by default already considers objects such
as the default coordinate systems, SolarSystemBarycenter, DefaultProp and
SolarSystem as automatic global objects. Furthermore whenever new coordinate
systems or propagators are created in the Resources tree, GMAT automatically
declares the newly created coordinate systems and propagators as global objects.
Since GMAT always declares default or newly created coordinate systems and
propagators as global, hence you do not need to use Global command on
coordinate system and propagator objects.
Remarks
Declaration of Global Objects
GMAT objects can be passed into the GMAT function as input and can also be
returned from the function as output. Refer to both GmatFunction resource and
CallGmatFunction command's Remarks sections to learn more about list of
allowed objects that can be passed as input and output to and from the function.
By default, in GMAT any objects that are created inside the main script are
considered local to the main script. Similarly any objects that may be created
inside the GMAT function are considered local to that function. In GMAT, in
order to declare objects as global, you must declare the objects as global in both
your main script and inside the function. It is a good practice to declare objects
as global right after the BeginMissionSequence line in both the main script and
inside the function.
If a given GMAT object is not declared as global in both the main script and in
the function, then all objects that are passed into the function as input and/or
received as output from the function are considered to be local to that function
and the main script.
Often times, you will propagate a spacecraft, perform differential correction
(DC) or optimization routines interchangeably from both the main script and
inside the function. Whenever you want to plot continuous set of spacecraft
trajectory data and report parameters to same subscribers interchangeably from
both inside the main script and the function, then always declare your
Spacecraft object and subscriber objects (i.e. OrbitView, GroundTrackPlot,
XYPlot, ReportFile, EphemerisFile) as global both in the main script and
inside the function. Abiding by this rule draws plots, reports and ephemeris files
correctly and flow of data will be reported continuously to all the subscribers.
GMAT allows globally declared objects such as Spacecraft, global
variables/arrays/strings to be passed as input/output argument to and from the
function. Globally declared objects such as Spacecraft, variables/arrays/strings
can be plotted or reported interchangeably both from the main script and inside
the function as long as all subscribers are also declared global.
Refer to GmatFunction resource's Examples section that shows three more
examples of how to declare spacecraft, five subscribers, arrays/variables/strings
as global in both the main script and inside the function.
Examples
Declare spacecraft, all subscribers and variables as global. Global variables are
passed as input and received as global output from the function. As you run the
example, notice that data is reported continuously to all 5 subscribers.
Create Spacecraft aSat
Create ForceModel aFM
aFM.CentralBody = Earth
aFM.PointMasses = {Earth}
Create Propagator aProp
aProp.FM = aFM
Create ImpulsiveBurn TOI
Create ImpulsiveBurn GOI
Create DifferentialCorrector DC
Create OrbitView anOrbitView
anOrbitView.Add = {aSat, Earth}
Create GroundTrackPlot GroundTrackPlot1
GroundTrackPlot1.Add = {aSat}
GroundTrackPlot1.CentralBody = Earth
Create XYPlot XYPlot1
XYPlot1.XVariable = aSat.ElapsedDays
XYPlot1.YVariables = {aSat.EarthMJ2000Eq.X}
Create ReportFile rf
rf.Add = {aSat.UTCGregorian, aSat.EarthMJ2000Eq.X, ...
aSat.EarthMJ2000Eq.Y, aSat.EarthMJ2000Eq.Z, ...
aSat.EarthMJ2000Eq.VX, aSat.EarthMJ2000Eq.VY, aSat.EarthMJ2000Eq.VZ}
Create ReportFile rf2
rf2.WriteHeaders = false
Create EphemerisFile anEphemerisFile
GMAT anEphemerisFile.Spacecraft = aSat
Create GmatFunction Global_Objects
Global_Objects.FunctionPath = ...
'C:\Users\rqureshi\Desktop\Global_Objects.gmf'
Create Variable T X Y Z VX VY VZ
BeginMissionSequence
Global aSat
Global aFM TOI GOI DC
Global anOrbitView GroundTrackPlot1 XYPlot1 rf rf2 anEphemerisFile
Global T X Y Z VX VY VZ
% Report initial state to Global 'rf2':
Report rf2 aSat.UTCGregorian aSat.X aSat.Y aSat.Z ...
aSat.VX aSat.VY aSat.VZ
Propagate aProp(aSat) {aSat.ElapsedDays = 1.0}
T = aSat.UTCModJulian
X = aSat.X
Y = aSat.Y
Z = aSat.Z
VX = aSat.VX
VY = aSat.VY
VZ = aSat.VZ
% Call function. Pass Global Variables as input:
% Receive updated global S/C state via global variables:
[T,X,Y,Z,VX,VY,VZ] = Global_Objects(T,X,Y,Z,VX,VY,VZ)
% Report global variables to global 'rf2':
Report rf2 T X Y Z VX VY VZ
% Re-report global S/C state:
Report rf2 aSat.UTCGregorian aSat.X aSat.Y aSat.Z ...
aSat.VX aSat.VY aSat.VZ
%%%%%%%% Function begins below:
function [T,X,Y,Z,VX,VY,VZ] = Global_Objects(T,X,Y,Z,VX,VY,VZ)
BeginMissionSequence
Global aSat
Global aFM TOI GOI DC
Global anOrbitView GroundTrackPlot1 XYPlot1 rf rf2 anEphemerisFile
Global T X Y Z VX VY VZ
% Report global variables to global 'rf2':
Report rf2 T X Y Z VX VY VZ
While aSat.ElapsedDays < 5
Propagate aProp(aSat) {aSat.ElapsedDays = 0.5}
EndWhile
% Send global variables back to main script:
T = aSat.UTCModJulian
X = aSat.X
Y = aSat.Y
Z = aSat.Z
VX = aSat.VX
VY = aSat.VY
VZ = aSat.VZ
