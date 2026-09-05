# BatchEstimatorInv

> 来源：第 373-394 页

BatchEstimatorInv
BatchEstimatorInv — A batch least squares estimator
Description
A batch least squares estimator is a method for obtaining an estimate for a
parameter vector, x0, such that a performance index, which is a function of that
parameter, J = J(x0), is minimized. For our application, x0 typically includes the
spacecraft position and velocity at a specific epoch and the performance index is
a weighted sum of the squares of the measurement residuals.
See Also: TrackingFileSet, RunEstimator
Fields
Field Description
AbsoluteTol
Absolute Weighted RMS convergence criteria
tolerance
Data Type Real
Allowed Values Real > 0
Access set
Default Value 0.001
Units dimensionless
Interfaces script
DataFilters
Defines filters to be applied to the data. One or
more filters of either type (AcceptFilter,
RejectFilter) may be specified. Rules
specified by data filters on a
BatchEstimatorInv are applied to determine
what data is accepted or rejected from the
computation of the state update.
Data Resource array
Type
Allowed User defined instances of
Values AcceptFilter and RejectFilter
resources
Access set
Default None
Value
Units N/A
Interfaces script
EstimationEpoch
Estimation Epoch. This is the epoch associated
with the "solve-fors." As of R2016A, this
epoch comes from the participants defined in
the Measurements field. In later releases,
additional options will be allowed.
Data Type String
Allowed Values 'FromParticipants'
Access set
Default Value 'FromParticipants'
Units N/A
Interfaces script
EstimationEpochFormat
Estimation Epoch format. This is the desired
input format for the EstimationEpoch field.
For release R2016A, the only allowed value is
'FromParticipants' which means that the
EstimationEpoch comes from the participants
defined in the Measurements field. In later
releases, additional options will be allowed.
Data Type String
Allowed Values 'FromParticipants'
Access set
Default Value 'FromParticipants'
Units N/A
Interfaces script
FreezeIteration
Specifies which iteration to freeze the selection
of measurements that are edited out
Data Type integer
Allowed Values any positive integer
Access set
Default Value 4
Units N/A
Interfaces script
FreezeMeasurementEditing
Allows the selection of measurements that are
edited out to be frozen
Data Type true/false
Allowed Values true or false
Access set
Default Value false
Units N/A
Interfaces script
InversionAlgorithm
Algorithm used to invert the normal equations
Data Type String
Allowed Values Internal, Cholesky, Schur
Access set
Default Value Internal
Units N/A
Interfaces script
MatlabFile
File name for the output MATLAB file.
Leaving this parameter unset means that no
MATLAB file will be output.
Data Type String
Allowed Values Any valid file name.
Access set
Default Value (unset)
Units N/A
Interfaces script
MaxConsecutiveDivergences
Specifies maximum number of consecutive
diverging iterations allowed before batch
estimation processing is stopped
Data Type integer
Allowed Values any positive integer
Access set
Default Value 3
Units N/A
Interfaces script
MaximumIterations Specifies maximum number of iterations
allowed for batch estimation
Data Type integer
Allowed Values any positive integer
Access set
Default Value 15
Units N/A
Interfaces script
Measurements
Specifies a list of measurements used for batch
estimation
Data Type ObjectArray
Allowed one or more valid
Values TrackingFileSet objects
Access set
Default empty list
Value
Units N/A
Interfaces script
OLSEAdditiveConstant
Additive constant used for outer loop sigma
editing (OLSE)
Data Type Real
Allowed Values any real number
Access set
Default Value 0.0
Units N/A
Interfaces script
OLSEInitialRMSSigma
Initial predicted root-mean-square value used
for outer loop sigma editing (OLSE)
Data Type Real
Allowed Values Real > 0.0
Access set
Default Value 3000.0
Units dimensionless
Interfaces script
OLSEMultiplicativeConstant
Multiplicative constant used for outer loop
sigma editing (OLSE)
Data Type Real
Allowed Values Real > 0.0
Access set
Default Value 3.0
Units dimensionless
Interfaces script
OLSEUseRMSP
Flag used to specify editing algorithm used for
outer loop sigma editing (OLSE) for iterations
greater than 1. See Behavior of Outer Loop
Sigma Editing (OLSE) in the Remarks
section for details.
Data Type true/false
Allowed Values true or false
Access set
Default Value true
Units dimensionless
Interfaces script
Propagator
Propagator object used for batch estimation
Data Type Object
Allowed Values valid Propagator object
Access set
Default Value None
Units N/A
Interfaces script
RelativeTol
Relative Weighted RMS convergence criteria
tolerance
Data Type Real
Allowed Values Real > 0
Access set
Default Value 0.0001
Units dimensionless
Interfaces script
ReportFile
Specifies the name of estimation report file
Data Type String
Allowed string containing a valid file
Values name
Access set
Default 'BatchEstimatorInv' +
Value instancename + '.data'
Units N/A
Interfaces script
ReportStyle
Specifies the type of estimation report. The
Normal style excludes reporting of observation
TAI, partials, and frequency information. For
this current GMAT version, for normal GMAT
operation, only the Normal style is an allowed
choice.
Data Type String
Allowed Values Normal
Access set
Default Value Normal
Units N/A
Interfaces script
ResetBestRMSIfDiverging
If set true and the estimation process has
diverged, then the Best RMS is reset to the
current RMS.
Data Type true/false
Allowed Values true or false
Access set
Default Value false
Units N/A
Interfaces script
ShowAllResiduals
Allows residuals plots to be shown
Data Type On/Off
Allowed Values On or Off
Access set
Default Value On
Units N/A
Interfaces script
ShowProgress
Allows detailed output of the batch estimator
to be shown in the message window
Data Type true/false
Allowed Values true or false
Access set
Default Value true
Units N/A
Interfaces script
UseInitialCovariance
If set true, a priori error covariance term is
added to the estimation cost function. This
option should be set to true when estimating
with an applied
Spacecraft.OrbitErrorCovariance,
Spacecraft.CdSigma, Spacecraft.CrSigma,
or ErrorModel.BiasSigma. See the Remarks
section below for some restrictions on the use
of this field.
Data Type true/false
Allowed Values true or false
Access set
Default Value false
Units N/A
Interfaces script
Remarks
Navigation Requires Use of Fixed Step Numerical
Integration
GMAT navigation requires use of fixed stepped propagation. The
BatchEstimatorInv resource has a Propagator field containing the name of the
Propagator resource that will be used during the estimation process. As shown
in the Note below, there are some hard restrictions on the choice of error control
specified for the ForceModel resource associated with your propagator.
Note
For batch estimation, the ErrorControl parameter specified for
the ForceModel resource associated with the
BatchEstimatorInv Propagator must be set to 'None.' Of
course, when using fixed step control, the user must choose a
step size, as given by the Propagator InitialStepSize field,
for the chosen orbit regime and force profile, that yields the
desired accuracy.
Behavior of Convergence Criteria
GMAT has four input fields, RelativeTol, AbsoluteTol, MaximumIterations,
and MaxConsecutiveDivergences that are used to determine if the estimator has
converged after each new iteration. Associated with these input fields are the two
convergence tests shown below:
Absolute Weighted RMS convergence criteria
Weighted RMScurrent <= AbsoluteTol
Relative Weighted Root Mean Square (RMS) convergence criteria
|RMSP – RMSB|/ RMSB <= RelativeTol
where
RMSB = smallest Weighted RMS achieved during the current and previous
iterations
RMSP = predicted Weighted RMS of next iteration
Batch estimation is considered to have converged when either or both of the
above criteria is met within MaximumIterations iterations or less.
Batch estimation is considered to have diverged when number of consecutive
diverging iterations is equal to or greater than MaxConsecutiveDivergences or
the number of iterations exceeds MaximumIterations.
Behavior of Outer Loop Sigma Editing (OLSE)
GMAT has four input fields, OLSEMultiplicativeConstant,
OLSEAdditiveConstant, OLSEUseRMSP, and OLSEInitialRMSSigma, that
are used to 'edit' (i.e., reject or throw away) bad measurement data. There are
plans to have both an inner loop and and outer loop iteration editing procedure.
Currently, only the outer loop iteration editing procedure is implemented. This
editing procedure is done on a per iteration basis. Data that is edited is not used
to calculate the state vector estimate for the current iteration but the data is
available as a candidate measurement for subsequent iterations. On the first outer
loop iteration, data is edited if
|Weighted Measurement Residual| > OLSEInitialRMSSigma
where the Weighted Measurement Residual for a single given measurement is
given by
(O-C)/NoiseSigma
and where NoiseSigma is the input noise (one sigma) for the measurement type
associated with the given measurement. On subsequent outer loop iterations, the
data is edited if
|Weighted Measurement Residual| > OLSEMultiplicativeConstant * RMS
+ OLSEAdditiveConstant
The editing algorithm above depends upon the user input value of
OLSEUseRMSP. If OLSEUseRMSP = True, then RMS = WRMSP where
WRMSP is the predicted weighted RMS calculated at the end of the previous
iteration. Otherwise, If OLSEUseRMSP = False, then RMS = WRMS where
WRMS is the actual weighted RMS calculated at the end of the previous
iteration.
Behavior of Freezing Measurement Editing
GMAT has two input fields, FreezeMeasurementEditing and FreezeIteration,
that are used to determine if and when to 'freeze' (i.e., no longer change) the
selection of measurements which are edited out by the Outer Loop Sigma Editor.
Freezing the measurement editing only takes place when
FreezeMeasurementEditing is true.
If freezing is enabled, the selection of measurements to edit is locked after the
iteration specified by FreezeIteration. If the value of FreezeIteration is 1, the
estimator uses the value of OLSEInitialRMSSigma, as defined above, to
determine which measurements are used to calculate the first iteration of the
state vector deviation vector. Afterwards, the same measurements edited out by
the initial RMS sigma filter are edited out for the remainder of the iterations. If
the value of FreezeIteration is 2 or greater, the estimator uses the above defined
outer loop sigma editing to determine the state vector deviation vector up to the
iteration specified by FreezeIteration, at which point whichever measurements
are edited out by the outer loop sigma editor stay edited out for the remainder of
the iterations. Frozen measurements that are edited out will retain the edit flag
the outer loop sigma editor used the iteration they were edited out.
Freezing measurement editing can be useful in situations where a solution takes
an excessive number of iterations to converge and latter iterations are only
editing a small amount of data. If this is the case, enabling the editing freeze on
an appropriate iteration will generally force the solution to converge quickly
after reaching the frozen iteration.
Propagator Settings
The BatchEstimatorInv resource has a Propagator field containing the name
of the Propagator resource that will be used during the estimation process. The
minimum step size, MinStep, of your propagator should always be set to 0.
UseInitialCovariance Restrictions
As mentioned in the Field spec above, if this field is set to true, then the a priori
error covariance term is added to the estimation cost function. For the current
GMAT release, there are some restrictions on the use of this field as given below.
1. The user must input the a priori orbit state covariance in the EarthMJ200Eq
coordinate system.
2. If the user is solving for the Cartesian orbit state, e.g., Sat.SolveFors =
{CartesianState}, then the input a priori orbit state covariance must be in
terms of Cartesian elements. Likewise, if the user is solving for the
Keplerian orbit state, e.g., Sat.SolveFors = {KeplerianState}, then the input
a priori orbit state covariance must be in terms of Keplerian elements.
3. If the user is solving for the Keplerian orbit state, e.g., Sat.SolveFors =
{KeplerianState}, then the input a priori orbit state covariance must be
expressed in terms in terms of spacecraft Mean Anomaly (MA) and not
True Anomaly (TA). To be more specific, in this situation, the diagonal
elements of the 6x6 orbit state error covariance are the variance of the SMA
(km^2), eccentricity (dimensionless), INC (deg^2), RAAN (deg^2), AOP
(deg^2), and MA (deg^2). Note that, in this case, we require the a priori
covariance to be input in terms of MA even though, for the current release
of GMAT, the associated orbit state can not be set using MA.
Interactions
Resource Description
TrackingFileSet
resource Must be created in order to tell the BatchEstimatorInv
resource which data will be processed
Propagator Used by GMAT to generate the predicted orbit
resource
RunEstimator
command Must use the RunEstimator command to actually process
the data defined by the BatchEstimatorInv resource
Examples
Below is an example of a configured batch estimator instance. In this example,
estData is an instance of a TrackingFileSet and ODProp is an instance of
Propagator.
Create BatchEstimatorInv bat;
bat.ShowProgress = true;
bat.Measurements = {estData}
bat.AbsoluteTol = 0.000001;
bat.RelativeTol = 0.001;
bat.MaximumIterations = 10;
bat.MaxConsecutiveDivergences = 3;
bat.Propagator = ODProp;
bat.ShowAllResiduals = On;
bat.OLSEInitialRMSSigma = 3000;
bat.OLSEMultiplicativeConstant = 3;
bat.OLSEAdditiveConstant = 0;
bat.InversionAlgorithm = 'Internal';
bat.EstimationEpochFormat = 'FromParticipants';
bat.EstimationEpoch = 'FromParticipants';
bat.ReportStyle = 'Normal';
bat.ReportFile = 'BatchEstimator_Report.txt';
BeginMissionSequence;
For a comprehensive example of reading in measurements and running the
estimator, see the Chapter 14, Orbit Estimation using DSN Range and Doppler
Data tutorial.
