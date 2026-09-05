# ErrorModel

> 来源：第 591-600 页

ErrorModel
ErrorModel — Used to specify measurement noise for simulation and
estimation, and to apply or estimate measurement biases.
Description
An ErrorModel is assigned on the ErrorModels field of an instance of
GroundStation or a spacecraft-attached Receiver to model biases and noise,
and optionally to estimate biases on each measurement type provided by the
ground station or receiver. An error model must be specified for each data type
employed by each tracking station or receiver, but a single instance of
ErrorModel may be used by multiple ground stations or spacecraft receivers.
An error model is only assigned to a receiver if GPS_PosVec data is employed.
The GPS_PosVec observation type models position estimates provided by an
on-board GPS receiver. Since this type of data is not derived from ground station
measurement modeling, the error model for GPS_PosVec data is specified on
the ErrorModels field of a Receiver resource instead. The receiver must be
attached to the corresponding Spacecraft object. Error models for all other
observation types should be specified on the ErrorModels field of the relevant
ground station resources. Error models cannot be assigned on receivers attached
to ground stations.
The ErrorModel is used by both the simulator and the estimator. For a data
simulation run, the ErrorModel specifies the measurement type and noise
employed when generating the simulated measurement. A bias may optionally
be applied to the simulated observations.
For an estimation run, the ErrorModel specifies the observation type, presumed
observation noise, and an optional bias to be applied to the observation. An
observation bias may also be estimated by adding the keyword Bias to the
ErrorModel.SolveFors list. If the SolveFors list is empty, no bias will be
estimated. The SolveFors list is ignored by the simulator.
The ErrorModel resource does not currently support application or estimation
of biases for the GPS_PosVec data type.
See Also GroundStation, Receiver
Fields
Field Description
Bias
The constant bias associated with the measurement. For
simulations, this bias is added to the measurement. As
shown below, the units used depend upon measurement
type, ErrorModel.Type.
Data Type Real
Allowed Values Any Real number
Access set
Default Value 0.0
Units See Remarks section
Interfaces script
BiasSigma
Standard deviation of Bias. This field, which only has a
function if both (1)
BatchEstimatorInv.UseInitialCovariance = true and (2)
Bias is a solve-for parameter, is used to constrain the
estimated value of Bias. As shown below, the units used
depend upon measurement type, ErrorModel.Type. This
parameter is not implemented for GPS_PosVec data.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e+70
Units See Remarks section
Interfaces script
NoiseSigma
One sigma value of Gaussian noise. For simulations, if
Sim.AddNoise = true, this noise is added to the
measurements. For estimation, this value is used to as part
of the batch processing algorithms to calculate the
measurement type weighting. As shown below, the units
used depend upon measurement type, ErrorModel.Type.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 103
Units See Remarks section
Interfaces script
SolveFors
List of parameters to estimate. This parameter is not
implemented for GPS_PosVec data.
Data Type StringArray
Allowed Values {} or {Bias}
Access set
Default Value {}
Units N/A
Interfaces script
Type
Measurement data type.
Data Type Enumeration
Allowed DSN_SeqRange, DSN_TCP, GPS_PosVec,
Values Range, RangeRate
Access set
Default DSN_SeqRange
Value
Units N/A
Interfaces script
Remarks
Units for Bias, BiasSigma, and NoiseSigma
The following table shows the units to be used for Bias, BiasSigma, and
NoiseSigma for each measurement data type that GMAT supports.
GMAT Measurement Type Units
DSN_SeqRange Range Units
DSN_TCP Hertz
GPS_PosVec Kilometers
Range Kilometers
RangeRate Kilometers/sec
Deprecated Measurement Type Names
This version of GMAT deprecates the DSNRange/Range_RU and
Doppler/Doppler_HZ measurement type names. These have been replaced by
the DSN_SeqRange and DSN_TCP types. These new names are employed
identically in the GMAT Measurement Data (GMD) data file, the
ErrorModel.Type parameter, and the TrackingFileSet.AddTrackingConfig
parameter. Scripts employing the deprecated measurement type names will still
work in this version of GMAT, but future versions will remove this support.
Users are encouraged to update their scripts to use the new names.
The new data type names employ the same name in the GMD file, error model,
and tracking file set tracking configuration, eliminating the need for a mapping
between the names employed in each resource. For those still using the
deprecated data type names, the following table provides a guide.
Deprecated GMD File and
Deprecated ErrorModel
TrackingFileSet.AddTrackingConfig
Measurement Type Name
Measurement Type Name
DSNRange Range_RU
Doppler Doppler_HZ
Examples
This example shows how to create an error model for DSN Sequential Range
observations and illustrates estimation of a range bias parameter.
% Create an ErrorModel
% Measurement noise is in Range Units
Create ErrorModel RangeModel;
RangeModel.Type = 'DSN_SeqRange';
RangeModel.NoiseSigma = 11.;
RangeModel.Bias = 0.;
RangeModel.SolveFors = {Bias};
% Assign it to a ground station
Create GroundStation DSN;
DSN.ErrorModels = {RangeModel};
BeginMissionSequence;
This example shows how to create an error model for on-board GPS
observations.
% Create an ErrorModel
% Measurement noise is in kilometers. Bias estimation is not permitted.
Create ErrorModel PosVecModel;
PosVecModel.Type = 'GPS_PosVec';
PosVecModel.NoiseSigma = 0.010;
% Assign the error model to a receiver and add that receiver to a spacecraft.
Create Antenna GpsAntenna;
Create Receiver GpsReceiver;
GpsReceiver.Id = 800;
GpsReceiver.PrimaryAntenna = GpsAntenna;
GpsReceiver.ErrorModels = {PosVecModel};
Create Spacecraft Sat;
Sat.AddHardware = {GpsReceiver, GpsAntenna};
BeginMissionSequence;
