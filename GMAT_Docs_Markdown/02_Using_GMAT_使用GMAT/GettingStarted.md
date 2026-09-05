# Getting Started

> 来源：第 18-23 页

Chapter 2. Getting Started
Installation
Installers and application bundles are available on the GMAT SourceForge
project page, located at https://sourceforge.net/projects/gmat.
The following packages are available for the major platforms:
Installer Binary bundle Source code
Windows (7,10) ✔ ✔ ✔
Mac OS X ✔ ✔
Linux ✔ ✔
Installer
To use the Windows installer, download the appropriate gmat-winInstaller-
*.exe file from the SourceForge download page and run it. You'll be asked a
series of questions, and GMAT will be installed to your local user account.
By default, GMAT installs to the %LOCALAPPDATA% folder in your user directory,
and does not require elevated privileges to install. On Windows Vista and
Windows 7, this generally corresponds to the
C:\Users\username\AppData\Local folder. You are free to choose another
install location during the installation process, but elevated privileges may be
required to do so.
Binary Bundle
A binary bundle is available on Windows as a .zip archive. To use it, unzip it
anywhere in your file system, making sure to keep the folder structure intact. To
run GMAT, run the GMAT\bin\GMAT.exe executable in the extracted folder.
Source Code
GMAT is available as a platform-independent source code bundle. Note that all
testing is performed on Windows, so on other platforms it is considered a beta
release. See the GMAT Wiki for compiling instructions.
Rather than compiling from the source bundle, however, we generally
recommend checking out a snapshot from the Subversion repository:
svn://svn.code.sf.net/p/gmat/code
There are tags available for reach release.
Running GMAT
Starting GMAT
On Microsoft Windows platforms there are several ways to start a GMAT
session. If you used the GMAT installer, you can click the GMAT R2018a item
in the Start menu. If you installed GMAT from a .zip file or by compiling the
system, locate the GMAT bin directory double-click GMAT.exe.
To start GMAT from the command line, run GMAT.exe. Various command-line
parameters are available; see Command-Line Usage for details.
Exiting GMAT
To end a GMAT session on Windows or Linux, in the menu bar, click File, then
click Exit. On Mac OS X, in the menu bar, click GMAT, then click Quit
GMAT, or type Command+Q.
Sample Missions
The GMAT distribution includes more than 30 sample missions. These samples
show how to apply GMAT to problems ranging from the Hohmann transfer to
libration point station-keeping to trajectory optimization. To locate and run a
sample mission:
1. Open GMAT.
2. On the toolbar click Open.
3. Navigate to the samples folder located in the GMAT root directory.
4. Double-click a script file of your choice.
5. Click Run ( ).
To run optimization missions, you will need MATLAB and the MATLAB
Optimization Toolbox or the internal libVF13Optimizer plugin. These are
proprietary libraries and are not distributed with GMAT. MATLAB connectivity
is not yet fully supported in the Mac and Linux, and therefore you cannot run
optimization missions that use MATLAB’s fmincon optimizer on those
platforms. See MATLAB Interface for details on configuring the MATLAB
optimizer.
Getting Help
This User Guide provides documentation and tutorials for all of GMAT's feature.
But if you have further questions, or want to provide feedback, here are some
additional resources:
Homepage: http://gmat.gsfc.nasa.gov
Wiki: http://gmatcentral.org
User forums: http://forums.gmatcentral.org
Downloads and source code: http://sourceforge.net/projects/gmat
Submit bug reports and feature requests: http://bugs.gmatcentral.org
Official contact: <gmat@gsfc.nasa.gov>
