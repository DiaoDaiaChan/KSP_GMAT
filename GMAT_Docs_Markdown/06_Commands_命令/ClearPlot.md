# ClearPlot

> 来源：第 1376-1382 页

ClearPlot
ClearPlot — Allows you to clear all data from an XYPlot
Script Syntax
ClearPlot OutputNames
OutputNames
OutputNames is the list of subscribers whose data is to be
cleared. When data of multiple subscribers is to be cleared,
then they need to be separated by a space.
Description
The ClearPlot command allows you to clear all data from an XYPlot after it has
been plotted. The ClearPlot command works only for the XYPlot resource and
data from multiple XYPlot resources can be cleared. ClearPlot command can be
used through GMAT’s GUI or the script interface.
Options
Option Description
OutputNames
The ClearPlot command allows the user to clear data from
an XYPlot subscriber. When more than one subscriber is
being used, the subscribers need to be separated by a space.
Accepted Data Types Resource reference
Allowed Values XYPlot resource
Default Value DefaultXYPlot
Required yes
Interfaces GUI, script
GUI
Figure below shows default settings for ClearPlot command.
Remarks
GMAT allows you to insert ClearPlot command into the Mission tree at any
location. This allows you to clear data output from an XYPlot at any point in
your mission. The XYPlot subscriber plots data at each propagation step of the
entire mission duration. If you want to report data to an XYPlot at specific
points in your mission, then a ClearPlot command can be inserted into the
mission sequence to control when a subscriber plots data. Refer to the Examples
section below to see how ClearPlot command can be used in the Mission tree.
Examples
This example shows how to use ClearPlot command on multiple subscribers.
Data from XYPlot subscribers is cleared after 2 days of the propagation:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aPlot1 aPlot2 aPlot3
aPlot1.XVariable = aSat.ElapsedSecs
aPlot1.YVariables = {aSat.EarthMJ2000Eq.X}
aPlot2.XVariable = aSat.ElapsedSecs
aPlot2.YVariables = {aSat.EarthMJ2000Eq.Y}
aPlot3.XVariable = aSat.ElapsedSecs
aPlot3.YVariables = {aSat.EarthMJ2000Eq.VX, aSat.EarthMJ2000Eq.VY, ...
aSat.EarthMJ2000Eq.VZ}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 2}
ClearPlot aPlot1 aPlot2 aPlot3
This example shows how to use ClearPlot command on a single subscriber.
Data from XYPlot is cleared for the first 3 days of the propagation and only the
data retrieved from last day of propagation is plotted:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aPlot1
aPlot1.XVariable = aSat.ElapsedDays
aPlot1.YVariables = {aSat.EarthMJ2000Eq.X, aSat.EarthMJ2000Eq.Y}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 3}
ClearPlot aPlot1
Propagate aProp(aSat) {aSat.ElapsedDays = 1}
