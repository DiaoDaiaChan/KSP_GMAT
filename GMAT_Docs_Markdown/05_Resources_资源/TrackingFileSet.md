# TrackingFileSet

> 来源：第 1191-1203 页

TrackingFileSet
TrackingFileSet — Manages the observation data contained in one or more
external tracking data files.
Description
A TrackingFileSet is required for both simulator and estimator runs. For a data
simulation run, the user must specify the desired tracking strings for the
simulated data (via AddTrackingConfig) and provide an output file name for
the simulated tracking observations (via FileName). In simulation mode, the
user may specify a range modulo constant, Doppler count interval, and other
parameters, depending on the type of tracking data being simulated. See the
remarks below for more details.
When running the estimator, the FileName parameter specifies the path to a
pregenerated external tracking data file. It is not necessary to explicitly specify
tracking configurations when running the estimator; GMAT will examine the
specified external tracking data file and determine the tracking configurations
automatically. GMAT will throw an error message if it is unable to uniquely
identify all objects found in the tracking data file.
When running the estimator, one or more AcceptFilters and/or RejectFilters
may be employed to select from all available observations a smaller subset for
use in the estimation process.
The SimRangeModuloConstant and SimDopplerCountInterval fields apply
only to the simulator and are ignored by the estimator. When running the
estimator, these values are provided in the tracking data file. For both the
simulator and estimator, relativity, light time, and ET-TAI corrections may
optionally be applied.
See Also: Simulator, BatchEstimatorInv, AcceptFilter, RejectFilter, Tracking
Data Types for OD
Fields
Field Description
AddTrackingConfig
One or more signal paths and measurement types
for simulation or estimation. See the Remarks
section below for details on the Tracking Strand
specification.
Data String
Type
Allowed {{Tracking Strand},
Values MeasurementType1[,
MeasurementType2, ...]}
Access set
Default None
Value
Units N/A
Interfaces script
DataFilters
Defines filters to be applied to the data. One or
more filters of either type (AcceptFilter,
RejectFilter) may be specified. Rules specified
by data filters on a TrackingFileSet are applied
to determine what data is admitted or rejected as
input to the estimation process.
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
FileName
For simulation, specifies an output file for the
simulated measurement data. For estimation,
specifies one or more preexisting tracking data
input files in GMD-format.
Data Type String
Allowed Values Valid file path
Access set
Default Value None
Units N/A
Interfaces script
RampTable
Specifies a transmit frequency ramp table to be
used when computing measurements for both
simulation and estimation.
Data Type String
Allowed Values Valid file path
Access set
Default Value None
Units N/A
Interfaces script
SimDopplerCountInterval
Specifies the Doppler count interval used for
Doppler and range-rate measurements. This
value is only used in simulation mode.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1.0
Units Seconds
Interfaces script
SimRangeModuloConstant
Specifies the value of the DSN range ambiguity
interval. This value is only used in simulation
mode.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1.00E+18
Units Range Units (RU)
Interfaces script
UseETminusTAI
Flag specifying if General Relativistic time
corrections should be made to the measurements.
If this flag is set, GMAT will apply the
adjustment from TAI to Ephemeris Time when
solving the light time equations for the computed
measurement. See Remarks below for more
details.
Data Type Boolean
Allowed Values True, False
Access set
Default Value False
Units N/A
Interfaces script
UseLightTime
Flag specifying whether light time corrections
should be applied to computed measurements.
Data Type Boolean
Allowed Values True, False
Access set
Default Value True
Units N/A
Interfaces script
UseRelativityCorrection
Flag specifying if General Relativistic
corrections should be made to the computed
measurements. If this flag is set, GMAT will
adjust the computed light time to include the
effects due to the coordinate velocity of light and
bending of the signal path. See Remarks below
for more details.
Data Type Boolean
Allowed Values True, False
Access set
Default Value False
Units N/A
Interfaces script
Remarks
See Tracking Data Types for OD for a detailed listing of all tracking data types
and tracking data file formats supported by GMAT for orbit determination.
Setting UseETminusTAI to True corresponds to inclusion in the computed
round-trip light time of the ET-TAI uplink and downlink terms in Eq. 11-7 of
Moyer, Formulation of Observed and Computed Values of Deep Space Network
Data Types for Navigation, JPL Publication 00-7, October 2000. Setting
UseRelativityCorrection to True corresponds to inclusion of the RLT uplink
and downlink terms in Eq. 11-7 of Moyer.
The SimRangeModuloConstant field is used only in the simulation of DSN
range tracking data. The user may specify a value to be used for this field or may
omit it, in which case the default value is used. This field is not applicable to
estimation. In estimation, this value is provided in the input tracking data file.
The SimDopplerCountInterval is used in the simulation of DSN_TCP and
RangeRate tracking data. The user may specify a value to be used for this field
or may omit it, in which case default value of 1 second is used. This field is not
applicable to estimation. In estimation, this value is provided in the input
tracking data file.
When displaying or saving a TrackingFileSet object using the Write command,
GMAT will display a number of items relevant to simulating TDRS data formats.
These options are not implemented in the current release and should be ignored
or manually removed from the output file.
Tracking Strand Specification
When simulating tracking data, at least one tracking strand must be specified
using the AddTrackingConfig parameter. The tracking strand must be enclosed
within curly braces. The format of the tracking strand depends on the
measurement data type being generated. Use the following table as a guide.
Measurement Type Name Tracking Strand Specification Format
DSN_SeqRange, {XMIT_GroundStation, Spacecraft,
DSN_TCP, Range, RECV_GroundStation}
RangeRate
GPS_PosVec {Spacecraft.Receiver}
Examples
This example illustrates use of the TrackingFileSet object for simulation of
DSN tracking data. Specification of the tracking configurations
(AddTrackingConfig) is optional when running the estimator. If omitted,
GMAT will attempt to automatically detect the tracking configurations present in
the tracking data file.
In this example, the frequency ramp table file dsn.ramp must be a preexisting
ramp table. GMAT will not simulate ramp table records. Alternatively, the user
may omit specification of a ramp table when simulating data. If the ramp table is
omitted, the simulator will use the frequency specified on the Transmitter
object attached to each GroundStation.
Create TrackingFileSet dsnObs;
%Create objects referenced by dnsObs
Create GroundStation GDS CAN MAD;
Create Spacecraft EstSat;
Create AcceptFilter af;
dsnObs.AddTrackingConfig = {{GDS, EstSat, GDS}, 'DSN_TCP'};
dsnObs.AddTrackingConfig = {{CAN, EstSat, CAN}, 'DSN_TCP'};
dsnObs.AddTrackingConfig = {{MAD, EstSat, MAD}, 'DSN_TCP', 'DSN_SeqRange'};
dsnObs.FileName = {'dsn.gmd'};
dsnObs.RampTable = {'dsn.ramp'};
dsnObs.UseLightTime = True;
dsnObs.UseRelativityCorrection = False;
dsnObs.UseETminusTAI = False;
dsnObs.SimRangeModuloConstant = 67108864;
dsnObs.SimDopplerCountInterval = 10.;
dsnObs.DataFilters = {af};
BeginMissionSequence;
This example illustrates use of the TrackingFileSet object for simulation of
GPS_PosVec tracking data. This example assumes that GpsReceiver is a
previously created instance of Receiver and has been attached to SimSat using
the AddHardware method.
Create TrackingFileSet PosVecObs;
PosVecObs.FileName = {'posvec_obs.gmd'};
PosVecObs.AddTrackingConfig = {{SimSat.GpsReceiver}, 'GPS_PosVec'};
SimMeas.DataFilters = {};
BeginMissionSequence;
