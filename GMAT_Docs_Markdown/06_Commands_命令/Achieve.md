# Achieve

> 来源：第 1303-1311 页

Achieve
Achieve — Specify a goal for a Target sequence
Script Syntax
Achieve SolverName (Goal = Arg1, [{Tolerance = Arg2}])
Description
The Achieve command is used in conjunction with the Target command as part
of the Target sequence. The purpose of the Achieve command is to define a goal
for the targeter (currently, the differential corrector is the only targeter available
within a Target sequence) to achieve. To configure the Achieve command, you
specify the goal object, its corresponding desired value, and an optional
tolerance so the differential corrector can find a solution. The Achieve command
must be accompanied and preceded by a Vary command in order to assist in the
targeting process.
See Also: DifferentialCorrector, Target, Vary
Options
Option Description
Arg1
Specifies the desired value for the Goal after the
DifferentialCorrector has converged.
Accepted Data Array, ArrayElement, Variable, String
Types
Allowed Values Real Number, Array element, or
Variable
Default Value 42165
Required yes
Interfaces GUI, script
Arg2
Convergence tolerance for how close Goal equals Arg1
Accepted Real Number, Array element, Variable, or any
Data Types user-defined parameter > 0
Allowed Real Number, Array element, Variable, or any
Values user-defined parameter > 0
Default 0.1
Value
Required no
Interfaces GUI, script
Goal
Allows you to select any single element user defined parameter,
except a number, as a targeter goal.
Accepted Object parameter, ArrayElement, Variable
Data
Types
Allowed Spacecraft parameter, Array element, Variable, or
Values any other single element user defined parameter,
excluding numbers
Default DefaultSC.Earth.RMAG
Value
Required yes
Interfaces GUI, script
SolverName
Specifies the DifferentialCorrector being used in the Target
sequence
Accepted Data String
Types
Allowed Values Any user defined
DifferentialCorrector
Default Value DefaultDC
Required yes
Interfaces GUI, script
GUI
You use an Achieve command, which is only valid within a Target sequence, to
define your desired goal. More than one Achieve command may be used within
a Target command sequence. The Achieve command dialog box, which allows
you to specify the targeter, goal object, goal value, and convergence tolerance, is
shown below.
Remarks
Command Interactions
A Target sequence must contain at least one Vary and one Achieve command.
Target
command An Achieve command only occurs within a Target sequence
Vary
command Associated with any Achieve command is at least one Vary
command. The Vary command identifies the control variable used
by the targeter. The goal specified by the Achieve command is
obtained by varying the control variables.
Examples
As mentioned above, an Achieve command only occurs within a Target
sequence. See the Target command help for examples showing the use of the
Achieve command.
