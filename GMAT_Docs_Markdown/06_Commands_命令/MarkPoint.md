# MarkPoint

> 来源：第 1446-1453 页

MarkPoint
MarkPoint — Allows you to add a special mark point character on an XYPlot
Script Syntax
MarkPoint OutputNames
OutputNames
OutputNames is the list of subscribers and a special mark point
will be added to each subscriber’s XYPlot. When mark points need
to be added to multiple subscribers, then the subscribers need
to be separated by a space.
Description
The MarkPoint command allows you to add a special mark point character to
highlight a single data point on an XYPlot. MarkPoint command works only
for XYPlot subscriber. This command also allows you to add special mark
points on multiple XYPlot resources. MarkPoint command can be used through
GMAT’s GUI or the script interface.
Options
Option Description
OutputNames
The MarkPoint command allows the user to add a special
mark point character to highlight an individual data point
on an XYPlot.
Accepted Data Types Resource reference
Allowed Values XYPlot resource
Default Value DefaultXYPlot
Required yes
Interfaces GUI, script
GUI
Figure below shows default settings for MarkPoint command:
Remarks
GMAT allows you to insert MarkPoint command into the Mission tree at any
location. This allows you to add special mark points on an XYPlot at any point
in your mission. The XYPlot subscriber plots data at each propagation step of
the entire mission duration. If you to want to place mark points on an XYPlot at
specific points, then a MarkPoint command can be inserted into the mission
sequence to control when mark points are placed onto an XYPlot. Refer to the
Examples section below to see how MarkPoint command can be used in the
Mission tree.
Examples
This example shows how to use MarkPoint command on multiple subscribers.
Mark points are added on two XYPlots after every 0.2 days through an iterative
loop:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aPlot1 aPlot2
aPlot1.XVariable = aSat.A1ModJulian
aPlot1.YVariables = {aSat.EarthMJ2000Eq.X}
aPlot2.XVariable = aSat.A1ModJulian
aPlot2.YVariables = {aSat.EarthMJ2000Eq.VX}
BeginMissionSequence;
While aSat.ElapsedDays < 1.0
MarkPoint aPlot1 aPlot2
Propagate aProp(aSat) {aSat.ElapsedDays = 0.2}
EndWhile
This example shows how to use MarkPoint on a single subscriber. In this
example, mark points are placed on the XYPlot the moment spacecraft’s altitude
goes below 750 Km. Note that mark points are placed on the XYPlot at every
integration step:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aPlot1
aPlot1.XVariable = aSat.A1ModJulian
aPlot1.YVariables = {aSat.Earth.Altitude}
BeginMissionSequence
While aSat.ElapsedDays < 2
Propagate aProp(aSat)
If aSat.Earth.Altitude < 750
MarkPoint aPlot1
EndIf
EndWhile
