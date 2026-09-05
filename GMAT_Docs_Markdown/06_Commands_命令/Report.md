# Report

> 来源：第 1518-1526 页

Report
Report — Allows you to write data to a text file
Script Syntax
Report ReportName DataList
ReportName
ReportName option allows you to specify the
ReportFile for data output.
DataList
DataList option allows you to output data to the Filename
specified by the ReportName. Multiple objects can be written
in the DataList when they are separated by spaces.
Description
The Report command allows you to report data at specific points in your
mission sequence. GMAT allows you to insert Report command into the
Mission tree at any location. Report command can be used through GMAT’s
GUI or via the script interface. The parameters reported by Report command are
placed into a report file that can be accessed at the end of the mission run.
See Also: ReportFile
Options
Option Description
ReportName
The ReportName option allows the user to specify the
ReportFile for data output.
Accepted Data Types Resource reference
Allowed Values ReportFile resource
Default Value DefaultReportFile
Required yes
Interfaces GUI, script
DataList
The DataList option allows the user to output data to the
file name that is specified by the ReportName. Multiple
objects can be in the DataList when they are separated by
spaces.
Accepted Reference array
Data
Types
Allowed Spacecraft, ImpulsiveBurn reportable
Values parameters, Array, Array Element, Variable,
or a String.
Default DefaultSC.A1ModJulian
Value
Required yes
Interfaces GUI, script
GUI
Figure below shows default settings for Report command:
Remarks
Report command can be used to report data to a report file at specific points in
your mission. If you want data to be reported at each propagation step of the
entire mission duration, then you should not use Report command. Instead you
should use ReportFile resource. See ReportFile resource section of the User's
Guide to learn about the syntax that allows you to report data at each raw
integrator steps.
Examples
Propagate an orbit for two days and report epoch and selected orbital elements to
a report file using the Report command.
Create Spacecraft aSat
Create ReportFile aReport
Create Propagator aProp
BeginMissionSequence
Report aReport aSat.UTCGregorian aSat.Earth.SMA aSat.Earth.ECC ...
aSat.EarthMJ2000Eq.RAAN
Propagate aProp(aSat) {aSat.ElapsedDays = 2}
Report aReport aSat.UTCGregorian aSat.Earth.SMA aSat.Earth.ECC ...
aSat.EarthMJ2000Eq.RAAN
Report user-defined parameters such as variables, array elements and a string to
a report file using the Report command.
Create ReportFile aReport
Create Variable aVar aVar2
aVar = 100
aVar2 = 2000
Create Array aArray[2,2]
aArray(1, 1) = 2
aArray(1, 2) = 3
aArray(2, 1) = 4
aArray(2, 2) = 5
Create String aString
aString = 'GMAT is awesome'
BeginMissionSequence
Report aReport aVar aVar2 aArray(1,1) aArray(1,2) aArray(2,1) ...
aArray(2,2) aString
While spacecraft propagates for less than a day, report spacecraft's true anomaly,
eccentricity and altitude after every 3600 seconds using the Report command:
Create Spacecraft aSat
Create ReportFile aReport
Create Propagator aProp
BeginMissionSequence
While aSat.ElapsedDays < 1
Propagate aProp(aSat) {aSat.ElapsedSecs = 3600 }
Report aReport aSat.Earth.TA aSat.Earth.ECC aSat.Earth.Altitude
EndWhile
