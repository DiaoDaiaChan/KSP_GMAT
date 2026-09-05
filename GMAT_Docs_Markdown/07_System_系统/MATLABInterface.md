# MATLAB Interface

> 来源：第 1763-1769 页

MATLAB Interface
MATLAB Interface — Interface to MATLAB system
Description
The MATLAB interface provides a link to the Mathworks MATLAB
environment, allowing GMAT to run MATLAB functions as if they were native
functions in the GMAT script language.
The interface cannot be controlled directly through the script language, though it
can be in the GMAT GUI. Instead, GMAT starts the interface automatically
when it calls a MATLAB function.
There are two GMAT components that provide user access to the interface. For
details on declaring a MATLAB function, see the MatlabFunction reference.
For details on calling a function and passing data, see the CallMatlabFunction
reference.
See Also: CallMatlabFunction, MatlabFunction
GUI
The MATLAB interface provides an icon in the Interfaces folder in the
Resources tree that can be used to control the interface. Right-clicking the icon
shows two options: Open and Close.
The Open menu item causes GMAT to open a connection to the MATLAB
Engine, which in turns displays a MATLAB command window in the
background. This connection is then used for all communication between GMAT
and MATLAB until the connection is closed. Only one connection can be open
at a time.
The Close menu item causes GMAT to close any open connection to the
MATLAB Engine. If no connection is open, it has no effect.
Remarks
Interface Setup
The following conditions must be true for GMAT to successfully initiate
communication with MATLAB. All conditions must be true for the same
instance of MATLAB.
Install a compatible, licensed version of MATLAB on the same machine on
which GMAT is running. GMAT is tested with the latest version of
MATLAB at the time of release, though versions R2006b and newer have
been known to work.
The architecture (32-bit or 64-bit) of GMAT and the installed version of
MATLAB must match. For example, the 32-bit version of GMAT is
compatible only with the 32-bit version of MATLAB.
On Windows:
1. Add the following path (where MATLAB is the path to the installed
version of MATLAB) to your Path environment variable (either your
user variable, or the system variable). If you continue to have trouble,
try putting this path at the very beginning of your system path.
MATLAB\bin\win32 (or win64 for use with 64-bit versions of GMAT)
2. Register MATLAB for use as a COM server by running:
matlab -regserver
This is done automatically by the MATLAB installer. To do it
manually, open an elevated command window and run the command
above. Make sure to run the command in the folder containing the
executable you wish to use (i.e. MATLAB\bin\win32 or
MATLAB\bin\win64.)
On Mac OS X:
The MATLABFORGMAT environment variable must exist and
contain the full path to the MATLAB application bundle (e.g.
/Applications/MATLAB_R2010a/MATLAB_R2010a.app).
Note that 64-bit GMAT must be used to interface with MATLAB after
version R2010a.
Note
Common troubleshooting tips on Windows:
If you are using the officially-released 32-bit version of
GMAT, make sure you have the 32-bit version of
MATLAB installed.
If the path above exists in your system Path variable, try
place it at the front.
Make sure the same instance of MATLAB is referenced
both in the Path variable and when running matlab -
regserver.
MATLAB Engine Connection
Warning
Caution: GMAT does not close the MATLAB Command
Window it creates after a run has completed. This allows
manual inspection of the MATLAB workspace, but it can lead
to confusing behavior if MATLAB functions or paths are
changed and rerun in the same window.
We recommend closing the MATLAB Command Window by
right-clicking Matlab in the Resources tree and clicking Close
between each run if you are actively editing the script.
When GMAT runs a mission that contains a MATLAB function call, it opens a
connection to the MATLAB engine before it makes the function call. It then
reuses this connection for the rest of the GMAT session.
The MATLAB Engine can be controlled manually through the Open and Close
options available by right-clicking the Matlab item in the Resources tree.
Examples
See the MatlabFunction reference for common examples.
