# Write

> 来源：第 1601-1611 页

Write
Write — Writes data to one or more of the following three destinations: the
message window, the log file, or a ReportFile resource.
Script Syntax
Write ResourceList [{ MessageWindow = true, LogFile = false,
Style = Concise, ReportFile = myReport }]
Description
The Write command allows you to selectively write information to GMAT
output destinations during execution. The Write command can aid in automated
QA by writing data to the GMAT log file or ReportFile resource for an
independent QA systems to process, or to write data to the message window to
aid in troubleshooting and debugging script configurations. This command can
also be used to write information on attached resources in order to see how
paramters change throughout a mission.
Options
Option Description
LogFile
Flag to specify if output should be written to the log file
Accepted Data Types Boolean
Allowed Values {True, False}
Default Value False
Required no
Interfaces GUI, script
MessageWindow
Flag to specify if output should be displayed in the
Message Window
Accepted Data Types Boolean
Allowed Values {True, False}
Default Value True
Required no
Interfaces GUI, script
ReportFile
Name of ReportFile resource where output data will be
written to. If this field is not set, no ReportFile resource
will be written to. The user can set formatting options on a
ReportFile like Precision and ColumnWidth. When
writing data using the Write command, those settings are
not used.
Accepted Data ReportFile resource
Types
Allowed Values Any user-defined ReportFile
resource
Default Value None
Required no
Interfaces GUI, script
ResourceList
A list of one or more GMAT resources and/or resource
fields whose values we wish to output
Accepted Data List of GMAT resources and/or
Types resource fields
Allowed Values Any GMAT resource name or
resource.field name
Default Value None
Required no
Interfaces GUI, script
Style
Parameter to specify format of output. Concise means that,
where appropriate, output will be values only and will not
contain the object name. The exception to this is when you
output an object with fields such as a Spacecraft. In this
case, the object and field will be output. Verbose means
that object names and fields will always be output. Script
means that script-parseable (i.e., the output, when pasted
into an existing GMAT script, will syntax check) output
will be generated
Accepted Data Types String
Allowed Values {Concise, Verbose, Script}
Default Value Concise
Required no
Interfaces GUI, script
GUI
In the example below, the value of myVar would be written to the message
window only.
Examples
Below are some sample scripts using the Write command with the output shown
in bold font.
Create ChemicalTank ChemicalTank1
Create Spacecraft Sat
Create String myString1 myString2
Create Variable myVar
Create Array myArray[2,2]
myVar = 3.1415
myString1 = 'This is my string'
myArray(1,1) = 1
myArray(2,2) = 1
BeginMissionSequence
Write ChemicalTank1 {Style = Script}
Create ChemicalTank ChemicalTank1;
GMAT ChemicalTank1.AllowNegativeFuelMass = false;
GMAT ChemicalTank1.FuelMass = 756;
GMAT ChemicalTank1.Pressure = 1500;
GMAT ChemicalTank1.Temperature = 20;
GMAT ChemicalTank1.RefTemperature = 20;
GMAT ChemicalTank1.Volume = 0.75;
GMAT ChemicalTank1.FuelDensity = 1260;
GMAT ChemicalTank1.PressureModel = PressureRegulated;
Write Sat.X Sat.VZ
7100
1
Write myVar myString1
3.1415
'This is my string'
Write myArray
1 0
0 1
Write myArray(2,2)
1
myString2 = sprintf('%10.7f',Sat.X)
Write myString2 {Style = Script}
Create String myString2;
myString2 = '7100.0000000';
Write myString2
'7100.0000000'
The example below writes out a report that can be read into a GMAT script using
the #Include capability.
Create Spacecraft Sat;
Create ReportFile rf;
rf.Filename = 'GMAT.script';
Create Variable myVar;
GMAT myVar = 11;
BeginMissionSequence;
Write Sat {Style = Script, MessageWindow = false, ReportFile = rf}
The example below writes out parameters for the fuel tank which is an attached
resource to the spacecraft after a manuever is complete. The output is shown
below the script, note the decrease in fuel mass was written using the Write
command this way.
Create Spacecraft Sat;
Create ChemicalTank ChemicalTank1;
GMAT Sat.Tanks = {ChemicalTank1};
BeginMissionSequence;
Maneuver ImpulsiveBurn1(Sat);
Propagate DefaultProp(Sat) {Sat.ElapsedSecs = 12000};
Write Sat.ChemicalTank1
ChemicalTank1.AllowNegativeFuelMass = true;
ChemicalTank1.FuelMass = 386.9462121211856;
ChemicalTank1.Pressure = 1500;
ChemicalTank1.Temperature = 20;
ChemicalTank1.RefTemperature = 20;
ChemicalTank1.Volume = 0.75;
ChemicalTank1.FuelDensity = 1260;
ChemicalTank1.PressureModel = 'PressureRegulated';
