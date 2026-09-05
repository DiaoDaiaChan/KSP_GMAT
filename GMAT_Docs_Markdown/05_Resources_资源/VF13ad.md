# VF13ad

> 来源：第 1264-1273 页

VF13ad
VF13ad — The Sequential Quadratic Programming (SQP) optimizer, VF13ad
Description
The VF13ad optimizer is a SQP-based Nonlinear Programming solver available
in the Harwell Subroutine Library. VF13ad performs nonlinear constrained
optimization and supports both linear and nonlinear constraints. To use this
solver, you must configure the solver options including convergence criteria,
maximum iterations, and gradient computation method. In the mission sequence,
you implement an optimizer such as VF13ad by using an
Optimize/EndOptimize sequence. Within this sequence, you define
optimization variables by using the Vary command, and define cost and
constraints by using the Minimize and NonlinearConstraint commands
respectively.
This resource cannot be modified in the Mission Sequence.
See Also: FminconOptimizer,Optimize,Vary, NonlinearConstraint, Minimize
Fields
Field Description
FeasibilityTolerance
Specifies the accuracy to which you want constraints
to be satisfied.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e-3
Units None
Interfaces GUI, script
MaximumIterations
Specifies the maximum allowable number of nominal
passes through the Solver Control Sequence.
Data Type Integer
Allowed Values Integer > 0
Access set
Default Value 200
Units None
Interfaces GUI, script
ReportFile
Contains the path and file name of the report file.
Data Type String
Allowed Values Any user-defined file name
Access set
Default Value VF13adVF13ad1.data
Units None
Interfaces GUI, script
ReportStyle
Determines the amount and type of data written to the
message window and to the report specified by field
ReportFile for each iteration of the solver (When
ShowProgress is true). Currently, the Normal,
Debug, and Concise options contain the same
information: the values for the control variables, the
constraints, and the objective function. In addition to
this information, the Verbose option also contains
values of the optimizer-scaled control variables.
Data Type String
Allowed Values Normal, Concise, Verbose, Debug
Access set
Default Value Normal
Units None
Interfaces GUI, script
ShowProgress
Determines whether data pertaining to iterations of the
solver is both displayed in the message window and
written to the report specified by the ReportFile field.
When ShowProgress is true, the amount of
information contained in the message window and
written in the report is controlled by the ReportStyle
field.
Data Type Boolean
Allowed Values true, false
Access set
Default Value true
Units None
Interfaces GUI, script
Tolerance
Specifies the measure the optimizer will use to
determine when an optimal solution has been found
based on the value of the goal set in a Minimize
command.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e-5
Units None
Interfaces GUI, script
UseCentralDifferences
Allows you to choose whether or not to use central
differencing for numerically determining the
derivative. For the default, 'false' value of this field,
forward differencing is used to calculate the
derivative.
Data Type Boolean
Allowed Values true, false
Access set
Default Value false
Units None
Interfaces GUI, script
GUI
The VF13ad dialog box allows you to specify properties of a VF13ad such as
maximum iterations, cost function tolerance, feasibility tolerance, choice of
reporting options, and choice of whether or not to use the central difference
derivative method.
To create a VF13ad resource, navigate to the Resources tree, expand the
Solvers folder, highlight and then right-click on the Optimizers sub-folder,
point to Add and then select VF13ad. This will create a new VF13ad resource,
VF13ad1. Double-click on VF13ad1 to bring up the VF13ad dialog box shown
below.
Remarks
VF13ad Optimizer Availability
This optimizer is not included as part of the nominal GMAT installation and is
only available if you have created and installed the VF13ad plug-in.
Resource and Command Interactions
The VF13ad resource can only be used in the context of optimization-type
commands. Please see the documentation for Optimize, Vary,
NonlinearConstraint, and Minimize for more information and worked
examples.
Examples
Create a VF13ad resource named VF13ad1.
Create VF13ad VF13ad1
VF13ad1.ShowProgress = true
VF13ad1.ReportStyle = Normal
VF13ad1.ReportFile = 'VF13adVF13ad1.data'
VF13ad1.MaximumIterations = 200
VF13ad1.Tolerance = 1e-005
VF13ad1.UseCentralDifferences = false
VF13ad1.FeasibilityTolerance = 1e-003
For an example of how a VF13ad resource can be used within an Optimization
sequence, see the Optimize command examples.
