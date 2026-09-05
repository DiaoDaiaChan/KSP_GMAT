# SNOPT

> 来源：第 937-949 页

SNOPT
SNOPT — The Sequential Quadratic Programming (SQP) optimizer, SNOPT
Description
The SNOPT optimizer is a SQP-based Nonlinear Programming solver
developed by Stanford Business Software, Inc. It is a proprietary component that
is not distritbuted with GMAT and must be obtained from the vendor. SNOPT
performs nonlinear constrained optimization and supports both linear and
nonlinear constraints. To use this solver, you must configure the solver options
including convergence criteria, maximum iterations, among other options. In the
mission sequence, you implement an optimizer such as SNOPT by using an
Optimize/EndOptimize sequence. Within this sequence, you define
optimization variables by using the Vary command, and define cost and
constraints by using the Minimize and NonlinearConstraint commands
respectively.
This resource cannot be modified in the Mission Sequence.
See Also: FminconOptimizer,Optimize,Vary, NonlinearConstraint, Minimize
Fields
Field Description
MajorFeasibilityTolerance
Specifies how accurately the nonlinear
constraints should be satisfied.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e-5
Units None
Interfaces GUI, script
MajorIterationsLimit
The maximum number of major iterations
allowed. It is intended to guard against an
excessive number of linearizations of the
constraints
Data Type Integer
Allowed Values Integer > 0
Access set
Default Value 1e-5
Units None
Interfaces GUI, script
MajorOptimalityTolerance
Specifies the final accuracy of the dual variables.
See the SNOPT user guide for further details.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e-5
Units None
Interfaces GUI, script
OutputFileName Contains the path and file name of the report file.
This report contains data written by SNOPT
regarding optimization progress and information.
Data Type String
Allowed Values Any user-defined file name
Access set
Default Value SNOPT.out
Units N/A
Interfaces GUI, script
OverrideSpecsFileValues
Flag to indicate if options settable in the GMAT
script/GUI should override values set in the
SNOPT Specs file. Note that if the specs file is
not found during initialization, GMAT
configurations are applied even if the
OverrideSpecsFileValues field is set to false.
Data Type Boolean
Allowed Values true, false
Access set
Default Value true
Units None
Interfaces GUI, script
ReportFile
Contains the path and file name of the report file.
This report contains data written by GMAT
regarding optimization progress and information.
data Type String
Allowed Values Any user-defined file name
Access set
Default Value SNOPTSNOPT1.data
Units N/A
Interfaces GUI,script
ReportStyle
Determines the amount and type of data written
to the message window and to the report
specified by field ReportFile for each iteration
of the solver (When ShowProgress is true).
Currently, the Normal, Debug, and Concise
options contain the same information: the values
for the control variables, the constraints, and the
objective function. In addition to this
information, the Verbose option also contains
values of the optimizer-scaled control variables.
Data Type String
Allowed Normal, Concise, Verbose,
Values Debug
Access set
Default Value Normal
Units None
Interfaces GUI, script
ShowProgress
Determines whether data pertaining to iterations
of the solver is both displayed in the message
window and written to the report specified by the
ReportFile field. When ShowProgress is true,
the amount of information contained in the
message window and written in the report is
controlled by the ReportStyle field.
Data Type Boolean
Allowed Values true, false
Access set
Default Value true
Units None
Interfaces GUI, script
SpecsFileName
File read by SNOPT to configure all settings of
the optimizer. The GMAT script/gui interface
only supportsa small subset of the SNOPT
configuration options. This file allows you to set
any options supported by SNOPT. This file is
only loaded if it is found during initialization and
selected values set on the file can be overwritten
by the GMAT configuration by
OverrideSpecsFileValues = true. See the
Remarks section for more information.
Data Type String
Allowed Values Any user-defined file name
Access set
Default Value SNOPT.spec
Units N/A
Interfaces GUI, script
TotalIterationsLimit
The maximum number of minor iterations
allowed.
Data Type Integer
Allowed Values Integer > 0
Access set
Default Value 100000
Units None
Interfaces GUI, script
GUI
The SNOPT dialog box allows you to specify properties of a SNOPT such as as
maximum iterations, cost function tolerance, feasibility tolerance, choice of
reporting options, and choice of whether or not to use the central difference
derivative method.
To create a SNOPT resource, navigate to the Resources tree, expand the Solvers
folder, highlight and then right-click on the Optimizers sub-folder, point to Add
and then select SNOPT. This will create a new SNOPT resource, SNOPT1.
Double-click on SNOPT1 to bring up the SNOPT dialog box shown below.
Remarks
SNOPT Optimizer Version and Availability
GMAT currently uses SNOPT 7.2-12.2. This optimizer is not included as part of
the nominal GMAT installation and is only available if you have created and
installed the SNOPT plug-in or obtained SNOPT from the vendor.
SPECS File Configuration
The Specs file contains a list of options and values in the following general
form:.
Begin options
Iterations limit 500
Minor feasibility tolerance 1.0e-7
Solution Yes
End options
The file starts with the keyword Begin and ends with End. The file is in free
format. Each line specifies a single option, using one or more items as follows:
1. A keyword (required for all options).
2. A phrase (one or more words) that qualifies the keyword (only for some
options).
3. A number that specifies an integer or real value (only for some options).
Such numbers may be up to 16 contiguous characters in Fortran 77’s I, F, E
or D formats, terminated by a space or new line.
The items may be entered in upper or lower case or a mixture of both. Some of
the keywords have synonyms, and certain abbreviations are allowed, as long as
there is no ambiguity. Blank lines and comments may be used to improve
readability. A comment begins with an asterisk (*) anywhere on a line. All
subsequent characters on the line are ignored. The Begin line is echoed to the
Summary file.
For a complete list of SNOPT options, see the SNOPT user guide.
Configuring SNOPT for Effective Optimization
When using SNOPT, the Upper and Lower bounds in the Vary commands are
required fields. By setting these values appropriately for your problem, you
reduce the likelihood that SNOPT will try values that are unphysical or that can
result in numerical singularities in the physical models. It is important to set
bounds carefully when using SNOPT.
Aditionally, SNOPT is quite senstive to scaling and care must be taken to
provide acceptable values of AdditiveScaleFactor and
MultiplicativeScaleFactor in the Vary commands. When using SNOPT,
derivatives are computed by SNOPT via the optimizer's built-in finite
differencing. If an optimization problem is not appropriately scaled, optimization
may fail, or take an un-nesessarily long time. Note that SNOPT has built-in
scaling options that can be set via the Specs file and are described in further
detail in the SNOPT user guide.
Resource and Command Interactions
Warning
GMAT's Vary command is a generic interface designed to
support many optimizers and not all settings supported by the
Vary command are supported by SNOPT. See the Vary
command documentation for details on the which Vary
command settings are supported by SNOPT.
The SNOPT resource can only be used in the context of optimization-type
commands. Please see the documentation for Optimize, Vary,
NonlinearConstraint, and Minimize for more information and worked
examples.
Examples
A simple mathematical optimization problem using SNOPT.
Create SNOPT NLP
GMAT NLP.ShowProgress = true
GMAT NLP.ReportStyle = Normal
GMAT NLP.ReportFile = output.report
GMAT NLP.MajorOptimalityTolerance = 0.001
GMAT NLP.MajorFeasibilityTolerance = 0.0001
GMAT NLP.MajorIterationsLimit = 456
GMAT NLP.TotalIterationsLimit = 789012
GMAT NLP.OutputFileName = 'SNOPTName123.out'
GMAT NLP.SpecsFileName = 'SNOPT.spec'
GMAT NLP.OverrideSpecsFileValues = true
Create Variable X1 X2 J G
BeginMissionSequence
Optimize NLP {SolveMode = Solve, ExitMode = DiscardAndContinue}
% Vary the independent variables
Vary 'Vary X1' NLP(X1 = 0, {Perturbation = 0.0000001, Upper = 10, ...
Lower = -10, AdditiveScaleFactor = 0.0, ...
MultiplicativeScaleFactor = 1.0})
Vary 'Vary X2' NLP(X2 = 0, {Perturbation = 0.0000001, Upper = 10, ...
Lower = -10, AdditiveScaleFactor = 0.0, ...
MultiplicativeScaleFactor = 1.0})
% The cost function and Minimize command
J = ( X1 - 2 )^2 + ( X2 - 2 )^2
Minimize 'Minimize Cost (J)' NLP(J)
% Calculate constraint and use NonLinearConstraint command
GMAT G = X2 + X1
NonlinearConstraint NLP(G<=8)
EndOptimize
