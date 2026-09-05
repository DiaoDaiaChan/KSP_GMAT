# Python Interface

> 来源：第 1770-1775 页

Python Interface
Python Interface — Interface to the Python programming language
Description
The Python interface provides a link to the Python programming language,
allowing GMAT to run Python functions as if they were native functions in the
GMAT script language.
The interface cannot be controlled directly through the script language. Instead,
GMAT starts the Python interface automatically when it calls a Python function.
The Python interface is accessed using GMAT's CallPythonFunction command.
For details on calling a function and passing data, see the CallPythonFunction
reference.
See Also: CallPythonFunction
GUI
The Python interface in GMAT is launched and driven internally. Users do not
have direct access to the interface from the GMAT graphical user interface.
Remarks
Interface Setup
The following conditions must be true for GMAT to successfully initiate
communication with Python. All conditions must be true for the same instance
of Python.
A compatible version of Python must be installed on the same machine on
which GMAT is running. GMAT is built and tested with the latest version
of 64-bit python avialable during final GMAT release preparation unless
Python is released during the last few weeks of a GMAT release. The
interface is linked with the Python binary libraries, so the installed version
of Python on the user's machine must match the architecture and release
used to build GMAT.
The architecture (32-bit or 64-bit) of GMAT and the installed version of
Python must match. For example, the 32-bit version of GMAT is
compatible only with the 32-bit version of Python.
The Python system accesses Python modules on the user's machine. This
functionality is configured, including path information used by Python, by
installing Python as a resource for all users of the machine.
On Windows:
The following path entries (where Python is the path to the installed
version of Python) must be present in the Path environment variable.
Python
Python/Scripts
The following path (where Python is the path to the installed version
of Python) must be present in the PYTHONPATH environment variable.
Python/Lib/site-packages
On Linux:
The Python release used in the GMAT build must be the default
Python package (that is, Python 3.4) accessed from the terminal.
Note
Common troubleshooting tips on Windows:
If you are using the officially-released 32-bit version of
GMAT, make sure you have the 32-bit version of Python
installed.
If the path above exists in your system Path variable, try
placing it at the front of the path specification.
Python Engine Connection
Warning
GMAT does not close the Python interface after a run has
completed. This feature prevents anomalous behavior that can
occur when loading some Python modules repeatedly during a
run, but it can lead to confusing behavior if Python files are
changed and rerun in the same GMAT session.
We recommend restarting GMAT after editing Python functions
in order to guarantee that your edits take effect when you rerun
your script.
When GMAT runs a mission that contains a Python function call, it loads Python
into memory as an embedded system in GMAT before it makes the function call.
It then reuses this system for the rest of the GMAT session.
Examples
See the CallPythonFunction reference for common examples.
