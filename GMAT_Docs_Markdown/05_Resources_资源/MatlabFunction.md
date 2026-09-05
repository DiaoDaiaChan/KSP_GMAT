# MatlabFunction

> 来源：第 747-753 页

MatlabFunction
MatlabFunction — Declaration of an external MATLAB function
Description
The MatlabFunction resource declares to GMAT that the name given refers to
an existing external function in the MATLAB language. This function can be
called in the Mission Sequence like a built-in function, with some limitations.
See the CallMatlabFunction reference for details. Both user-created functions
and built-in functions (like cos or path) are supported.
GMAT supports passing data to and from MATLAB through the function. It
requires that a supported and properly configured version of MATLAB exist on
the system. See the MATLAB Interface documentation for general details on the
interface.
See Also: CallMatlabFunction, MATLAB Interface
Fields
Field Description
FunctionPath
Paths to add to the MATLAB search path when the associated
function is called. Separate multiple paths with semicolons (on
Windows) or colons (on other platforms).
Data Type String
Allowed Valid file path(s)
Values
Access set, get
Default Value MATLAB_FUNCTION_PATH properties in the
startup file
Units N/A
Interfaces GUI, script
GUI
The MatlabFunction GUI window is very simple; it has a single file input box
for the function path, and a Browse button that lets you graphically select the
path.
Remarks
Search Path
When a function declared as a MatlabFunction is called, GMAT starts
MATLAB in the background with a custom, configurable search path. MATLAB
then searches for the named function in this search path. The search is case-
sensitive, so the name of the function name and the MatlabFunction resource
must be identical.
The search path consists of the following components, in order:
1. FunctionPath field of the associated MatlabFunction resource (default:
empty)
2. MATLAB_FUNCTION_PATH entries in the GMAT startup file (default:
GMAT\userfunctions\matlab)
3. MATLAB search path (returned by the MATLAB path() function)
If multiple MATLAB functions are called within a run, the FunctionPath fields
for each are prepended to the search path at the time of the function call.
Multiple paths can be combined in the FunctionPath field by separating the
paths with a semicolon (on Windows) or a colon (on Mac OS X and Linux).
Working Directory
When MATLAB starts in the background, its working directory is set to the
GMAT bin directory.
Examples
Call a simple built-in MATLAB function:
Create MatlabFunction sinh
Create Variable x y
BeginMissionSequence
x = 1
[y] = sinh(x)
Call an external custom MATLAB function:
Create Spacecraft aSat
Create ImpulsiveBurn aBurn
Create Propagator aProp
Create MatlabFunction CalcHohmann
CalcHohmann.FunctionPath = 'C:\path\to\functions'
Create Variable a_target mu dv1 dv2
mu = 398600.4415
BeginMissionSequence
% calculate burns for circular Hohmann transfer (example)
[dv1, dv2] = CalcHohmann(aSat.SMA, a_target, mu)
% perform first maneuver
aBurn.Element1 = dv1
Maneuver aBurn(aSat)
% propagate to apoapsis
Propagate aProp(aSat) {aSat.Apoapsis}
% perform second burn
aBurn.Element1 = dv2
Maneuver aBurn(aSat)
Return the MATLAB search path and working directory:
Create MatlabFunction path pwd
Create String pathStr pwdStr
Create ReportFile aReport
BeginMissionSequence
[pathStr] = path
[pwdStr] = pwd
Report aReport pathStr
Report aReport pwdStr
