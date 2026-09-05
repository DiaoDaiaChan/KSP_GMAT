# Target

> 来源：第 1554-1567 页

Target
Target — Solve for condition(s) by varying one or more parameters
Script Syntax
Target SolverName [{[SolveMode = value], [ExitMode = value],
[ShowProgressWindow = value]}]
Vary command …
script statement …
Achieve command …
EndTarget
Note
See the section called “Remarks” and the section called
“Description” for this complex command. Multiple Vary and
Achieve commands are permitted. Script statements can appear
anywhere in the Target sequence.
Description
The Target and EndTarget commands are used to define a Target sequence to
determine, for example, the maneuver components required to raise the orbit
apogee to 42164 km. Another common targeting example is to determine the
parking orbit orientation required to align a lunar transfer orbit with the moon.
Target sequences in GMAT are general and these are just examples. Let’s define
the quantities whose values you don’t know precisely, but need to determine, as
the control variables. Define the conditions that must be satisfied as the
constraints. A Target sequence numerically solves a boundary value problem to
determine the value of the control variables required to satisfy the constraints.
You define your control variables by using Vary commands and you define the
problems constraints using Achieve commands. The Target/EndTarget
sequence is an advanced command. The examples later in this section give
additional details.
See also: DifferentialCorrector,Vary,Achieve,Optimize,
Options
Option Description
ApplyCorrections
This GUI button replaces the initial guess values
specified in the Vary commands. If the Target
sequence converged, the converged values are
applied. If the Target sequence did not converge, the
last calculated values are applied. There is one
situation where the action specified above, where the
initial guess values specified in the Vary commands
are replaced, does not occur. This happens when the
initial guess value specified in the Vary command is
given by a variable. See the Remarks section of the
help for additional details.
Accepted Data Types N/A
Allowed Values N/A
Default Value N/A
Required no
Interfaces GUI
ExitMode
Controls the initial guess values for Target sequences
nested in control flow. If ExitMode is set to
SaveAndContinue, the solution of a Target sequence
is saved and used as the initial guess for the next
Target sequence execution. The rest of the mission
sequence is then executed. If ExitMode is set to
DiscardAndContinue, then the solution is discarded
and the initial guess values specified in the Vary
commands are used for each Target sequence
execution. The rest of the mission sequence is then
executed. If ExitMode is set to Stop, the Target
sequence is executed, the solution is discarded, and
the rest of the mission sequence is not executed.
Accepted Data Reference Array
Types
Allowed DiscardAndContinue,
Values SaveAndContinue, Stop
Default Value DiscardAndContinue
Required no
Interfaces GUI, script
ShowProgressWindow
Flag to indicate if solver progress window should be
displayed.
Accepted Data Types Boolean
Allowed Values true,false
Default Value true
Required no
Interfaces GUI, script
SolveMode
Specifies how the Target sequence behaves during
mission execution. When SolveMode is set to Solve,
the Target sequence executes and attempts to solve
the boundary value problem satisfying the targeter
constraints (i.e, goals). When SolveMode is set to
RunInitialGuess, the targeter does not attempt to
solve the boundary value problem and the commands
in the Target sequence execute using the initial guess
values defined in the Vary commands.
Accepted Data Types Reference Array
Allowed Values Solve, RunInitialGuess
Default Value Solve
Required no
Interfaces GUI, script
SolverName
Identifies the DifferentialCorrector used for a
Target sequence.
Accepted Data DifferentialCorrector
Types
Allowed Any user-defined or default
Values DifferentialCorrector
Default Value DefaultDC
Required yes
Interfaces GUI, script
GUI
The Target command allows you to use a differential correction process to solve
problems. To solve a given problem, you need to create a so-called Target
sequence which we now define. When you add a Target command to the
mission sequence, an EndTarget command is automatically added as shown
below.
In the example above, the Target command sequence is defined as all of the
commands between the Target1 and End Target1 commands, inclusive.
Although not shown above, a Target command sequence must contain both a
Vary command and an Achieve command. The Vary command is used to define
the control variables which can be varied in order to achieve a certain goal. The
Achieve command is used to define the desired goal. In order for the Target
aequence to be well formed, there must be at least one Vary command before
any Achieve commands, so that the variable defined in the Vary command can
affect the goal specified in the subsequent Achieve commands. Double click on
Target1 command above to bring up the Target command dialog box, shown
below, which allows you to specify your choice of Solver (i.e., your choice of
DifferentialCorrector), Solver Mode, and Exit Mode. As described in the
Remarks section, the Target command dialog box also allows you to apply
corrections to your Target command sequence.
If you set ShowProgressWindow to true, then a dynamic display is shown
during targeting that contains values of variables and constraints as shown
below.
Remarks
Content of a Target/EndTarget Sequence
A Target/EndTarget sequence must contain at least one Vary command and at
least one Achieve Command. See the Vary and Achieve command sections for
details on the syntax for those commands. The First Vary command must occur
before the first Achieve command. Target commands must be be coupled with
one and only one EndTarget command. Each Target command field in the curly
braces is optional. You can omit the entire list and the curly braces and the
default values will be used for Target configuration fields such as SolveMode
and ExitMode.
Use of a Target/EndTarget Sequence
GMAT Target sequences can solve square problems (the number of Control
Variables equals the number of constraints), over-determined problems (the
number of Control Variables is less than the number of constraints) and under-
determined problems (the number of Control Variables is greater than the
number of constraints). In any of these cases, there may not be a solution and the
type of solution found depends on the selection of the targeter (currently, only
differential correctors are supported). Assuming a solution to the problem exists
and assuming certain mathematical conditions are satisfied, there is often one
solution for a square problem and many solutions to an under-determined
problem. Problems with more goals (i.e., constraints) than variables may not
have a solution. If your problem is under-determined, consider using an
Optimize sequence to find an optimal solution in the space of feasible solutions.
Caution
If you configure a Target sequence and get the error “Rmatrix
error: matrix is singular”, then your control variables defined in
the Vary commands do not affect the constraints defined in the
Achieve commands. A common mistake in this case is that you
forgot to apply a maneuver.
Note on Using Apply Corrections
After the Target sequence has been run, you may choose to apply corrections by
navigating to the Mission tree, right-clicking the Target command to bring up
the Target window, and clicking the Apply Corrections button. The Apply
Corrections button replaces the initial guess values specified in the Vary
commands . If the Target sequence converged, the converged values are applied.
If the Target sequence did not converge, the last calculated values are applied.
Note that the Apply Corrections feature is only currently available through the
GUI interface.
There is one situation where the action specified above, where the initial guess
values specified in the Vary commands are replaced, does not occur. This
happens, as illustrated in the example below, when the initial guess value
specified in the Vary command is given by a variable. In this situation, the
Apply Corrections button has no affect since GMAT does not allow variables to
be overwritten.
Create Variable InitialGuess_BurnDuration BurnDuration
Create DifferentialCorrector aDC
BeginMissionSequence
Target aDC
Vary aDC(BurnDuration = InitialGuess_BurnDuration)
Achieve aDC(BurnDuration = 10) % atypical Achieve command for
% illustrative purposes only
EndTarget
Command Interactions
Vary
command Every Target sequence must contain at least one Vary command.
Vary commands are used to define the control variables associated
with a Target sequence.
Achieve
command Every Target sequence must contain at least one Achieve
command. Achieve commands are used to define the goals
associated with a Target sequence.

Examples
Use a Target sequence to solve for a root of an algebraic equation. Here we
provide an initial guess of 5 for the Control Variable (or independent variable) x,
and solve for the value of x that satisfies the Constraint y = 0, where y :=3*x^3 +
2*x^2 - 4*x + 8. After executing this example you can look in the message
window to see the solution for the variable x. You can easily check that the value
obtained does indeed satisfy the constraint.
Create Variable x y
Create DifferentialCorrector aDC
BeginMissionSequence
Target aDC
Vary aDC(x = 5)
y = 3*x^3 + 2*x^2 - 4*x + 8
Achieve aDC(y = 0,{Tolerance = 0.0000001})
EndTarget
Use a Target sequence to raise orbit apogee. Here the control variable is the
velocity component of an ImpulsiveBurn object. The Constraint is that the
position vector magnitude at orbit apogee is 42164. Report the convergence
status to a file.
Create Spacecraft aSat
Create Propagator aPropagator
Create Variable I
Create ImpulsiveBurn aBurn
Create DifferentialCorrector aDC
Create OrbitView EarthView
EarthView.Add = {Earth,aSat}
EarthView.ViewScaleFactor = 5
Create ReportFile aReport
BeginMissionSequence
Target aDC
Vary aDC(aBurn.Element1 = 1.0, {Upper = 3})
Maneuver aBurn(aSat)
Propagate aPropagator(aSat,{aSat.Apoapsis})
Achieve aDC(aSat.RMAG = 42164)
EndTarget
Report aReport aDC.SolverStatus aDC.SolverState
Similar to the previous example, we use a Target sequence to raise orbit apogee
except that this time we use a finite burn. Here the control variable is the
duration of the Velocity component of a FiniteBurn object. The Constraint is
that the position vector magnitude at orbit apogee is 12000. Additional detail on
the example below can be found in the Target Finite Burn to Raise Apogee
tutorial.
Create Spacecraft DefaultSC
Create Propagator DefaultProp
Create ChemicalThruster Thruster1
GMAT Thruster1.C1 = 1000
GMAT Thruster1.DecrementMass = true
Create ChemicalTank FuelTank1
GMAT Thruster1.Tank = {FuelTank1}
Create FiniteBurn FiniteBurn1
GMAT FiniteBurn1.Thrusters = {Thruster1}
GMAT DefaultSC.Tanks = {FuelTank1}
GMAT DefaultSC.Thrusters = {Thruster1}
Create Variable BurnDuration
Create DifferentialCorrector DC1
BeginMissionSequence
Propagate DefaultProp(DefaultSC) {DefaultSC.Earth.Periapsis}
Target DC1
Vary DC1(BurnDuration = 200, {Upper = 10000})
BeginFiniteBurn FiniteBurn1(DefaultSC)
Propagate DefaultProp(DefaultSC){DefaultSC.ElapsedSecs=BurnDuration}
EndFiniteBurn FiniteBurn1(DefaultSC)
Propagate DefaultProp(DefaultSC) {DefaultSC.Earth.Apoapsis}
Achieve DC1(DefaultSC.Earth.RMAG = 12000)
EndTarget
