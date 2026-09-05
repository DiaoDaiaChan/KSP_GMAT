# Command-Line Usage

> 来源：第 1739-1745 页

Command-Line Usage
Command-Line Usage — Starting the GMAT application from the command line
Synopsis
GMAT [option...] [script_file]
GMATConsole [option...] [script_file]
Description
The GMAT command starts the GMAT graphical interface. If run with no
arguments, GMAT starts with the default mission loaded. If script_file is
specified, and is a valid path to a GMAT script, GMAT loads the script and
remains open, but does not run it. The GMATConsole command starts the GMAT
console interface. See below for options supported by each interface.
Options
-b, --batch
Runs multiple scripts listed in specified file.
-h, --help
Start GMAT and display command-line usage information in the message
window if using the GUI version, or in the terminal if using the console
interface.
-l <filename>, --logfile <filename>
Specify the log file (ignored in Console interactive mode.
-m, --minimize
Start GMAT with a minimized interface.
-ns, --no_splash
Start GMAT without the splash screen showing.
-r <filename>, --run <filename>
Automatically run the specified script after loading.
--save <filename>
Saves current script (interactive mode only).
--start-server
Starts GMAT Server on start-up (ignored for Console).
-s <filename>, --startup_file <filename>
Specify the startup file (ignored in Console interactive mode).
--summary
Writes command summary (interactive mode only).
--verbose
Dump info messages to screen during run (default is on).
-v, --version
Start GMAT and display version information in the message window.
-x, --exit
Exit GMAT after running the specified script. If specified with only a script
name (i.e. NO –run option), GMAT simply opens and closes.
Precedence Rules
Some file locations, the log file for example, can be set in multiple locations.
The precedence rules are as follows. Command line settings have the highest
precedence, and those values are always used if set. The second precedence is
taken by script level settings, for example, GmatGlobal.LogFile =
C:\myLog.txt. Finally, if no other method is set, the value in the startup file is
used.
There are additional precedence rules that apply when the startup file is
configured to use RUN_MODE = TESTING. In that case, the log file name from the
startup file has precedence, and the output path can be overwritten by settings
avialalble in the GUI Set File Paths option in the File menu, or in the Run
Scripts option avialable in the Scripts menu in the Resource Tree.
Examples
Start GMAT and run the script MyScript.script:
GMAT MyScript.script
Run a script with the interface minimized, and exit afterwards:
GMAT --minimize --exit MyScript.script
