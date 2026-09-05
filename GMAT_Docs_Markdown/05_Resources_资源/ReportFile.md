# ReportFile

> 来源：第 906-925 页

ReportFile
ReportFile — Report data to a text file
Description
The ReportFile resource allows you to write data to a text file that can be
viewed after a mission run has been completed. GMAT allows you to report
user-defined Variables, Arrays, Strings and Object Parameters. GMAT gives
you control over setting formatting properties of the output report file that is
generated at the end of a mission run. You can create ReportFile resource in
either the GUI or script interface. GMAT also provides the option of when to
write and stop writing data to a text file through the Toggle On/Off command.
See the Remarks section below for detailed discussion of the interaction between
ReportFile resource and Toggle command.
See Also: Report, Toggle
Fields
Field Description
Add
Allows a user to add any number of user-defined
Variables, Arrays, Strings or Object Parameters to a
report file. To add multiple user-defined variables or
parameters, enclose the reported values with curly
brackets. Ex. MyReportName.Add ={Sat.X, Sat.Y, Var1,
Array(1,1)}; The GUI's Selected Value(s) field is the
equivalent of the script's Add field. This field cannot be
modified in the Mission Sequence.
Data Reference array
Type
Allowed Any user-defined parameter. Ex. Variables,
Values Arrays, Strings, or Object parameters
Access set
Default {DefaultSC.A1ModJulian,
DefaultSC.EarthMJ2000Eq.X}
Value
Units N/A
Interfaces GUI, script
ColumnWidth
This field defines the width of the data columns in a report
file. The value for ColumnWidth is applied to all columns
of data. For example, if ColumnWidth is set to 20, then
each data column will be 20 white-spaces wide.
Data Type Integer
Allowed Values Integer > 1
Access set
Default Value 23
Units Characters
Interfaces GUI, script
Delimiter
When FixedWidth field is turned off, this field become
active. The Delimiter field allows you to report data to a
report file in Comma, Semicolon, Space and Tab delimited
format.
Data Type Enumeration
Allowed Comma, SemiColon, Space, Tab
Values
Access set
Default Value When this field is active, then default is
Space
Units N/A
Interfaces GUI, script
Filename
Allows the user to define the file path and file name for a
report file.
Data Type String
Allowed Values Valid File Path and Name
Access set
Default Value ReportFile1.txt
Units N/A
Interfaces GUI, script
FixedWidth
Allows you to enable or disable Delimiter and
ColumnWidth fields. When this field is turned on, the
Delimiter field is inactive and ColumnWidth field is
active and can be used to vary the width of the data
columns. When FixedWidth field is turned off, the
ColumnWidth field becomes inactive and Delimiter field
is active for use.
Data Type Boolean
Allowed Values On, Off
Access set
Default Value On
Units N/A
Interfaces GUI, script
LeftJustify
When the LeftJustify field is set to On, then the data is left
justified and appears at the left most side of the column. If
the LeftJustify field is set to Off, then the data is centered
in the column.
Data Type Boolean
Allowed Values On, Off
Access set
Default Value On
Units N/A
Interfaces GUI, script
Maximized
Allows the user to maximize the ReportFile window. This
field cannot be modified in the Mission Sequence.
Data Type Boolean
Allowed Values true,false
Access set
Default Value false
Units N/A
Interfaces script
Precision
Allows the user to set the number of significant digits of
the data written to a report.
Data Type Integer
Allowed Values Integer > 1
Access set
Default Value 16
Units Same as variable being reported
Interfaces GUI, script
RelativeZOrder
Allows the user to select which ReportFile to display first
on the screen. The ReportFile with lowest
RelativeZOrder value will be displayed last while
ReportFile with highest RelativeZOrder value will be
displayed first. This field cannot be modified in the
Mission Sequence.
Data Type Integer
Allowed Values Integer ≥ 0
Access set
Default Value 0
Units N/A
Interfaces script
Size
Allows the user to control the display size of generated
report file. First value in [0 0] matrix controls horizonal
size and second value controls vertical size of report file
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
sequence is written to a report file. When SolverIterations
is set to All, all perturbations/iterations are written to a
report file. When SolverIterations is set to Current, only
current solution is written to a report file. When
SolverIterations is set to None, this shows only final
solution after the end of an iterative process and reports
only final solution to a report file.
Data Type Enumeration
Allowed Values All, Current, None
Access set
Default Value Current
Units N/A
Interfaces GUI, script
Upperleft
Allows the user to pan the generated report file display
window in any direction. First value in [0 0] matrix helps
to pan the report file window horizontally and second value
helps to pan the window vertically. This field cannot be
modified in the Mission Sequence.
Data Type Real array
Allowed Values Any Real number
Access set
Default Value [ 0 0 ]
Units N/A
Interfaces script
WriteHeaders
This field specifies whether to include headers that
describe the variables in a report file.
Data Type Boolean
Allowed Values True, False
Access set
Default Value True
Units N/A
Interfaces GUI, script
WriteReport
This field specifies whether to write data to the report
FileName.
Data Type Boolean
Allowed Values True, False
Access set
Default Value True
Units N/A
Interfaces GUI, script
ZeroFill
Allows zeros to be placed in data written to a report to
match set precision.
Data Type Boolean
Allowed Values On, Off
Access set
Default Value Off
Units N/A
Interfaces GUI, script
GUI
Figure below shows default name and settings for the ReportFile resource:
Remarks
Behavior When using Filename field
GMAT allows you to specify the name of the report file in two ways. The default
naming convention for a report file when using FileName field is shown below:
Create ReportFile aReport
aReport.Filename = 'ReportFile1.txt'
aReport.WriteReport = true
An alternate method for naming a report file is to name the file without using
any single quotes around the report file’s name.
Create ReportFile aReport
aReport.Filename = ReportFile1.txt
aReport.WriteReport = true
How data is reported to a report file
GMAT allows you to report data to a report file in two ways: You can use
ReportFile.Add field or a Report command.
You can add data using the .Add field of ReportFile resource and this method
reports data to the report file at each propagation step. Below is an example
script snippet that shows how to report epoch and selected orbital elements using
the .Add field:
Create Spacecraft aSat
Create ReportFile aReport
aReport.Add = {aSat.UTCGregorian aSat.Earth.SMA, aSat.Earth.ECC, ...
aSat.Earth.TA, aSat.EarthMJ2000Eq.RAAN}
Create Propagator aProp
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 8640.0}
GMAT’s ReportFile.Add field will not report selected data to the report file at
each propagation step if Propagate command is not included under the
BeginMissionSequence.
An alternative method of reporting data to the report file is via the Report
command. Using the Report command allows you to report data to the report
file at specific points in your mission. Below is an example script snippet that
shows how to report epoch and selected orbital elements using the Report
command:
Create Spacecraft aSat
Create ReportFile aReport
Create Propagator aProp
BeginMissionSequence
Report aReport aSat.UTCGregorian aSat.Earth.SMA aSat.Earth.ECC ...
aSat.Earth.TA aSat.EarthMJ2000Eq.RAAN
Propagate aProp(aSat) {aSat.ElapsedSecs = 8640.0}
Report aReport aSat.UTCGregorian aSat.Earth.SMA aSat.Earth.ECC ...
aSat.Earth.TA aSat.EarthMJ2000Eq.RAAN
Behavior and Interactions when using ReportFile Resource
& Report Command
Suppose you utilize a ReportFile resource and opt not to write a report and
select false for the field name WriteReport, as shown in the example below:
Create ReportFile aReport
aReport.Filename = ReportFile1.txt
aReport.Add = {aSat.A1ModJulian, aSat.Earth.SMA}
aReport.WriteReport = false
Now assume that at the same time, you decide to utilize Report command in the
Mission tree, as shown in the example script snippet below:
BeginMissionSequence;
Report aReport aSat.A1ModJulian aSat.Earth.SMA aSat.Earth.ECC
Propagate aProp(aSat) {aSat.Earth.Periapsis}
Report aReport aSat.A1ModJulian aSat.Earth.SMA aSat.Earth.ECC
At this point, you may think that since false option is selected under the field
name WriteReport in ReportFile resource, hence GMAT will not generate the
report file called ReportFile1.txt. On the Contrary, GMAT will generate a
report called ReportFile1.txt, but this report will only contain data that was
requested using the Report command. ReportFile1.txt text file will contain
epoch, semi-major-axis and eccentricity only at specific points of the mission.
Behavior when reporting data in Iterative Processes
GMAT allows you to specify how data is written to reports during iterative
processes such as differential correction or optimization. SolverIterations field
of ReportFile resource supports 3 options which are described in the table
below:
SolverIterations
Description
options
All
Shows only current iteration/perturbation after the end of an
iterative process and reports current solution to a report file.
Current
Shows all iterations/perturbations in an iterative process and
reports all iterations/perturbations to a report file.
None
Shows only final solution after the end of an iterative process
and reports only final solution to a report file.
Where Reports are written
GMAT allows you to write reports to any desired path or location. You can do
this by going to GMAT’s startup file called gmat_startup_file.txt and define
an absolute path under OUTPUT_PATH. This allows you to save report files in the
directory of your choice as oppose to saving report files in GMAT's default
Output folder. In ReportFile.FileName field, If no path is provided and only
name of the report file is defined, then report files are written to GMAT's default
Output folder. The default path where reports are written to is the Output folder
located in the main directory where GMAT is installed.
Below is an example script snippet that shows where generated reports are
written when only report file’s name is provided under the FileName field. In
this example, 'ReportFile1.txt'report is written to the Output folder located in
the main directory where GMAT is installed:
Create ReportFile aReport
aReport.Filename = 'ReportFile1.txt'
aReport.Add = {aSat.A1ModJulian, aSat.Earth.ECC}
An alternate method where report files can be written is by defining a relative
path. You can define the relative path in GMAT’s startup file
gmat_startup_file.txt under OUTPUT_PATH. For example, you can set a
relative path by setting OUTPUT_PATH =
C:/Users/rqureshi/Desktop/GMAT/mytestfolder/../output2/. In this path,
the syntax ".." means to “go up one level”. After saving the startup file, when the
script is executed, the generated report file named under FileName field will be
written to a path C:\Users\rqureshi\Desktop\GMAT\output2.
Another method where report files can be written to is by defining an absolute
path in GMAT’s startup file gmat_startup_file.txt under OUTPUT_PATH. For
example, you can set an absolute path by setting OUTPUT_PATH =
C:/Users/rqureshi/Desktop/GMAT/mytestfolder/. When the script is
executed, report file named under FileName field will be written to an absolute
path C:\Users\rqureshi\Desktop\GMAT\mytestfolder.
Instead of defining a relative or an absolute path in GMAT's startup file, you can
choose to define an absolute path under FileName field too. For example, if you
set ReportFile.FileName =
C:\Users\rqureshi\Desktop\GMAT\mytestfolder\ReportFile.txt, then
report file will be saved in mytestfolder.
Behavior when using ReportFile Resource & Toggle
Command
GMAT allows you to use Toggle command while using the Add field of
ReportFile resource. When Toggle Off command is issued for a ReportFile, not
data is sent to a report file until a Toggle On command is issued. Similarly, when
a Toggle On command is used, data is sent to a report file at each integration
step until a Toggle Off command is used.
Below is an example script snippet that shows how to use Toggle Off and
Toggle On command while using the ReportFile resource. Spacecraft’s
cartesian position vector is reported to the report file.
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
aReport.Filename = 'ReportFile1.txt'
aReport.Add = {aSat.UTCGregorian, aSat.EarthMJ2000Eq.X ...
aSat.EarthMJ2000Eq.Y aSat.EarthMJ2000Eq.Z}
BeginMissionSequence
Toggle aReport Off
Propagate aProp(aSat) {aSat.ElapsedDays = 2}
Toggle aReport On
Propagate aProp(aSat) {aSat.ElapsedDays = 4}
Behavior When Specifying Empty Brackets in ReportFile's
Add Field
When using ReportFile.Add field, GMAT does not allow brackets to be left
empty. The brackets must always be populated with values that you wish to
report. If brackets are left empty, then GMAT throws in an exception. Below is a
sample script snippet that shows an example of empty brackets. If you were to
run this script, then GMAT throws in an execption reminding you that brackets
cannot be left empty.
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
aReport.Add = {}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 8640.0}
Examples
Propagate an orbit and write cartesian state to a report file at every integrator
step
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
GMAT aReport.Filename = 'ReportFile1.txt'
aReport.Add = {aSat.EarthMJ2000Eq.X aSat.EarthMJ2000Eq.Y ...
aSat.EarthMJ2000Eq.Z aSat.EarthMJ2000Eq.VX ...
aSat.EarthMJ2000Eq.VY aSat.EarthMJ2000Eq.VZ}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 8640.0}
Propagate an orbit for 1 day and write cartesian state to a report file at specific
points in your mission
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
GMAT aReport.Filename = 'ReportFile1.txt'
BeginMissionSequence
Report aReport aSat.EarthMJ2000Eq.X aSat.EarthMJ2000Eq.Y ...
aSat.EarthMJ2000Eq.Z aSat.EarthMJ2000Eq.VX ...
aSat.EarthMJ2000Eq.VY aSat.EarthMJ2000Eq.VZ
Propagate aProp(aSat) {aSat.ElapsedDays = 1}
Report aReport aSat.EarthMJ2000Eq.X aSat.EarthMJ2000Eq.Y ...
aSat.EarthMJ2000Eq.Z aSat.EarthMJ2000Eq.VX ...
aSat.EarthMJ2000Eq.VY aSat.EarthMJ2000Eq.VZ
