# XYPlot

> 来源：第 1274-1287 页

XYPlot
XYPlot — Plots data onto the X and Y axes of a graph
Description
The XYPlot resource allows you to plot data onto the X and Y axis of the graph.
You can choose to plot any number of parameters as a function of a single
independent variable. GMAT allows you to plot user-defined variables, array
elements, or spacecraft parameters. You can create multiple XYPlots by using
either the GUI or script interface of GMAT. GMAT also provides the option of
when to plot and stop plotting data to a XYPlot through the Toggle On/Off
command. See the Remarks section below for detailed discussion of the
interaction between an XYPlot resource and the Toggle command. GMAT’s
Spacecraft and XYPlot resources also interact with each other throughout the
entire mission duration. Discussion of the interaction between Spacecraft and
XYPlot resources can also be found in the Remarks section.
See Also: Toggle, Spacecraft
Fields
Field Description
Maximized
Allows the user to maximize the XYPlot window. This
field cannot be modified in the Mission Sequence.
Data Type Boolean
Allowed Values true,false
Access set
Default Value false
Units N/A
Interfaces script
UpperLeft
Allows the user to pan the XYPlot display window in any
direction. First value in [0 0] matrix helps to pan the
XYPlot window horizontally and second value helps to
pan the window vertically. This field cannot be modified in
the Mission Sequence.
Data Type Real array
Allowed Values Any Real number
Access set
Default Value [0 0]
Units N/A
Interfaces script
RelativeZOrder
Allows the user to select which XYPlot window to display
first on the screen. The XYPlot with lowest
RelativeZOrder value will be displayed last while XYPlot
with highest RelativeZOrder value will be displayed first.
This field cannot be modified in the Mission Sequence.
Data Type Integer
Allowed Values Integer ≥ 0
Access set
Default Value 0
Units N/A
Interfaces script
ShowGrid
When the ShowGrid field is set to True, then a grid is
drawn on an xy-plot. When the ShowGrid field is set to
False, then a grid is not drawn. This field cannot be
modified in the Mission Sequence.
Data Type Boolean
Allowed Values True,False
Access set
Default Value True
Units N/A
Interfaces GUI, script
ShowPlot
Allows the user to turn off a plot for a particular run,
without deleting the XYPlot resource, or removing it from
the script. If you select True, then the plot will be shown.
If you select False, then the plot will not be shown. This
field cannot be modified in the Mission Sequence.
Data Type Boolean
Allowed Values True,False
Access set
Default Value True
Units N/A
Interfaces GUI, script
Size
Allows the user to control the display size of XYPlot
window. First value in [0 0] matrix controls horizonal size
and second value controls vertical size of XYPlot display
window. This field cannot be modified in the Mission
Sequence.
Data Type Real array
Allowed Values Any Real number
Access set
Default Value [ 0 0 ]
Units N/A
Interfaces script
SolverIterations
This field determines whether or not data associated with
perturbed trajectories during a solver (Targeter, Optimize)
sequence is displayed in the XYPlot. When
SolverIterations is set to All, all perturbations/iterations
are plotted in the XYPlot. When SolverIterations is set to
Current, only the current solution or perturbation is
plotted in XYPlot. When SolverIterations is set to None,
only the final nominal run is plotted on the XYPlot.
Data Type Enumeration
Allowed Values All, Current, None
Access set
Default Value Current
Units N/A
Interfaces GUI, script
XVariable
Allows the user to define the independent variable for an
XYPlot. Only one variable can be defined as an
independent variable. For example, the line
MyXYPlot.XVariable = DefaultSC.A1ModJulian sets the
independent variable to be the epoch of DefaultSC in the
A1 time system and modified Julian format. This field
cannot be modified in the Mission Sequence.
Data Resource reference
Type
Allowed Variable, Array, array element, Spacecraft
Values parameter that evaluates to a real number
Access get, set
Default DefaultSC.A1ModJulian
Value
Units N/A
Interfaces GUI, script
YVariable
Allows the user to add dependent variables to an xy-plot.
All dependent variables are plotted on the y-axis vs the
independent variable defined by XVariable field. The
dependent variable(s) should always be included in curly
braces. For example, MyXYPlot.YVariables =
{DefaultSC.EarthMJ2000Eq.Y,
DefaultSC.EarthMJ2000Eq.Z}. This field cannot be
modified in the Mission Sequence.
Data Reference array
Type
Allowed Any user variable, array element, or
Values spacecraft parameter that evaluates to a real
number
Access get, set
Default DefaultSC.EarthMJ2000Eq.X
Value
Units N/A
Interfaces GUI, script
GUI
The figure below shows the default settings for the XYPlot resource:
Remarks
Behavior when using XYPlot Resource & Toggle Command
The XYPlot resource plots data onto the X and Y axis of the graph at each
propagation step of the entire mission duration. If you want to report data to an
XYPlot at specific points in your mission, then a Toggle On/Off command can
be inserted into the mission sequence to control when the XYPlot is to plot data.
When Toggle Off command is issued for a XYPlot, no data is plotted onto the X
and Y axis of the graph until a Toggle On command is issued. Similarly when a
Toggle On command is used, data is plotted onto the X and Y axis at each
integration step until a Toggle Off command is used.
Below is an example script snippet that shows how to use Toggle Off and
Toggle On commands while using the XYPlot resource. Spacecraft’s position
magnitude and semi-major-axis are plotted as a function of time.
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aXYPlot
aXYPlot.XVariable = aSat.ElapsedDays
aXYPlot.YVariables = {aSat.Earth.RMAG, aSat.Earth.SMA}
BeginMissionSequence
Toggle aXYPlot Off
Propagate aProp(aSat) {aSat.ElapsedDays = 2}
Toggle aXYPlot On
Propagate aProp(aSat) {aSat.ElapsedDays = 4}
Behavior when using XYPlot & Spacecraft resources
Spacecraft resource contains information about spacecraft’s orbit, its attitude,
physical parameters (such as mass and drag coefficient) and any attached
hardware, including thrusters and fuel tanks. Spacecraft resource interacts with
XYPlot throughout the entire mission duration. The data retrieved from the
spacecraft is what gets plotted onto the X and Y axis of the graph at each
propagation step of the entire mission duration.
Behavior When Specifying Empty Brackets in XYPlot's
YVariables Field
When using XYPlot.YVariables field, GMAT does not allow brackets to be left
empty. The brackets must always be populated with values that you wish to plot
against a variable in XVariable field. If brackets are left empty, then GMAT
throws in an exception. Below is a sample script snippet that shows an example
of empty brackets. If you were to run this script, then GMAT throws in an
execption reminding you that brackets for YVariables field cannot be left empty.
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aXYPlot
aXYPlot.XVariable = aSat.ElapsedDays
aXYPlot.YVariables = {}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 2}
Behavior when Reporting Data in Iterative Processes
GMAT allows you to specify how data is plotted onto a plot during iterative
processes such as differential correction or optimization. The SolverIterations
field of an XYPlot resource supports three options which are described in the
table below:
SolverIterations
Description
options
Current
Shows only current iteration/perturbation in an iterative
process and plots current iteration to a plot.
All
Shows all iterations/perturbations in an iterative process and
plots all iterations/perturbations to a plot.
None
Shows only the final solution after the end of an iterative
process and plots only that final solution to the plot.
Examples
Propagate an orbit and plot the spacecraft’s altitude as a function of time at every
integrator step:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aXYPlot
aXYPlot.XVariable = aSat.ElapsedSecs
aXYPlot.YVariables = {aSat.Earth.Altitude}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 4}
Plotting data during an iterative process. Notice SolverIterations field is
selected as All. This means all iterations/perturbations will be plotted.
Create Spacecraft aSat
Create Propagator aProp
Create ImpulsiveBurn TOI
Create DifferentialCorrector aDC
Create XYPlot aXYPlot
aXYPlot.SolverIterations = All
aXYPlot.XVariable = aSat.ElapsedDays
aXYPlot.YVariables = {aSat.Earth.RMAG}
BeginMissionSequence
Propagate aProp(aSat) {aSat.Earth.Periapsis}
Target aDC
Vary aDC(TOI.Element1 = 0.24, {Perturbation = 0.001, Lower = 0.0, ...
Upper = 3.14159, MaxStep = 0.5})
Maneuver TOI(aSat)
Propagate aProp(aSat) {aSat.Earth.Apoapsis}
Achieve aDC(aSat.Earth.RMAG = 42165)
EndTarget
