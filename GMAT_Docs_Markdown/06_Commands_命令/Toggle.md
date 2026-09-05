# Toggle

> 来源：第 1568-1576 页

Toggle
Toggle — Allows you to turn data output off or on
Script Syntax
Toggle OutputNames Arg
OutputNames
OutputNames is the list of subscribers that are to be toggled.
When multiple subscribers are being toggled in the OutputNames,
then they need to be separated by a space.
Arg
Arg option allows you to turn off or on the data output to
the selected subscribers listed in the OutputNames.
Description
The Toggle command allows you to turn data output off or on for the subscribers
that you select such as ReportFile, XYPlot, OrbitView, GroundTrackPlot and
EphemerisFile. GMAT allows you to insert Toggle command into the Mission
tree at any location and data output can be turned off or on at any point in your
mission. Toggle command can be used through GMAT’s GUI or the script
interface.
Options
Option Description
OutputNames
The Toggle option allows the user to assign subscribers
such as ReportFile, XYPlot, OrbitView,
GrounTrackPlot or EphemerisFile to be toggled. When
more than one subscriber is being toggled, they need to be
separated by a space.
Accepted Resource reference
Data
Types
Allowed ReportFile, XYPlot, OrbitView,
Values GroundTrackPlot or EphemerisFile
resources
Default DefaultOrbitView
Value
Required yes
Interfaces GUI, script
Arg
The Arg option allows the user to turn off or on the data
output to the selected subscriber.
Accepted Data Types Boolean
Allowed Values On, Off
Default Value On
Required yes
Interfaces GUI, script
GUI
Figure below shows default settings for Toggle command:
Remarks
The subscribers such as ReportFile, XYPlot, OrbitView, GroundTrackPlot
and EphemerisFile report or plot data at each propagation step of the entire
mission duration. If you want to report data to any of these subscribers at
specific points in your mission, then a Toggle On/Off command can be inserted
into the mission sequence to control when a subscriber reports or plots data. For
example, when a Toggle Off command is issued for a XYPlot, no data is plotted
onto the X and Y axis of the graph until a Toggle On command is issued.
Similarly when a Toggle On command is used, data is plotted onto the X and Y
axis at each integration step until a Toggle Off command is used.
Examples
This example shows how to use Toggle Off and Toggle On commands while
using the XYPlot resource. Spacecraft’s position magnitude and semi-major-axis
are plotted as a function of time. XYPlot is turned off for the first 2 days of the
propagation:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aPlot
aPlot.XVariable = aSat.ElapsedDays
aPlot.YVariables = {aSat.Earth.RMAG, aSat.Earth.SMA}
BeginMissionSequence
Toggle aPlot Off
Propagate aProp(aSat) {aSat.ElapsedDays = 2}
Toggle aPlot On
Propagate aProp(aSat) {aSat.ElapsedDays = 4}
This example shows how to use Toggle Off and Toggle On commands while
using the ReportFile resource. Spacecraft’s cartesian position vector is reported
to the report file. Report file is turned off for the first day of the propagation:
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
aReport.Filename = 'ReportFile1.txt'
aReport.Add = {aSat.ElapsedDays aSat.EarthMJ2000Eq.X ...
aSat.EarthMJ2000Eq.Y aSat.EarthMJ2000Eq.Z}
BeginMissionSequence
Toggle aReport Off
Propagate aProp(aSat) {aSat.ElapsedDays = 1}
Toggle aReport On
Propagate aProp(aSat) {aSat.ElapsedDays = 4}
This example shows how to toggle multiple subscribers. Toggle Off and Toggle
On commands are used on multiple subscribers like ReportFile, XYPlot and
EphemerisFile. Subscribers are turned off for first 3 days of the propagation:
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
aReport.Filename = 'ReportFile1.txt'
aReport.Add = {aSat.ElapsedDays aSat.EarthMJ2000Eq.X ...
aSat.EarthMJ2000Eq.Y aSat.EarthMJ2000Eq.Z}
Create XYPlot aPlot
aPlot.XVariable = aSat.ElapsedDays
aPlot.YVariables = {aSat.Earth.RMAG, aSat.Earth.SMA}
Create EphemerisFile aEphemerisFile
aEphemerisFile.Spacecraft = aSat
BeginMissionSequence
Toggle aReport aPlot aEphemerisFile Off
Propagate aProp(aSat) {aSat.ElapsedDays = 3}
Toggle aReport aPlot aEphemerisFile On
Propagate aProp(aSat) {aSat.ElapsedDays = 1}
