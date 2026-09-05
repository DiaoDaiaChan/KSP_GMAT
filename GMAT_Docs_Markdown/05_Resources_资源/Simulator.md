# Simulator

> 来源：第 926-936 页

Simulator
Simulator — Configures the generation of simulated tracking data
measurements.
Description
A Simulator configures the generation of simulated tracking data measurements.
These measurements can then be used by a BatchEstimatorInv resource as part
of an estimation run.
The Simulator object requires specification of one or more instances of a
TrackingFileSet resource which identify the specific tracking data observation
strands, data types, desired measurement corrections, and the output tracking
data file name. Simulated data will be written in the GMAT Measurement Data
(GMD) ASCII tracking data format. You must additionally specify a time span
for the simulation run and a time interval between simulated observations.
Simulated observations are only generated when a tracking strand meets the
visibility constraints of all objects in the strand (for example, the observation
must be above the ground station minimum elevation mask). Additionally, you
must configure and specify an instance of a Propagator for the simulator.
Finally, you can choose to add random Gaussian white noise to the generated
measurements or to generate measurements without noise. If the
Simulator.AddNoise option is set to On, noise with the standard deviation
specified on each measurement strand’s GroundStation.ErrorModel, is added
to the measurements.
See Also: TrackingFileSet, RunEstimator
Fields
Field Description
AddData
A list of one or more TrackingFileSets
Data Type TrackingFileSet object
Allowed Values Any valid TrackingFileSet object
Access set
Default Value None
Units N/A
Interfaces script
AddNoise
If true, adds noise to simulated observations
Data Type Boolean
Allowed Values true, false, on, off
Access set
Default Value false
Units N/A
Interfaces script
EpochFormat
Epoch format of both the initial and final epoch
Data STRING_TYPE
Type
Allowed A1ModJulian, TAIModJulian,
Values UTCModJulian, TTModJulian,
TDBModJulian, A1Gregorian,
TAIGregorian, TTGregorian,
UTCGregorian, TDBGregorian
Access set
Default TAIModJulian
Value
Units N/A
Interfaces script
InitialEpoch
The initial (start) epoch of the data simulation span.
In the GMAT script, the EpochFormat field needs to
be set before this field is set.
Data STRING_TYPE
Type
Allowed Gregorian: 04 Oct 1957 12:00:00.000
Values <= Epoch <= 28 Feb 2100 00:00:00.000
Modified Julian: 6116.0 <= Epoch <=
58127.5
Access set
Default '21545'
Value
Units N/A
Interfaces script
FinalEpoch
The final (ending) epoch of the data simulation span.
In the GMAT script, the EpochFormat field needs to
be set before this field is set.
Data STRING_TYPE
Type
Allowed Gregorian: 04 Oct 1957 12:00:00.000
Values <= Epoch <= 28 Feb 2100 00:00:00.000
Modified Julian: 6116.0 <= Epoch <=
58127.5
Access set
Default '21545'
Value
Units N/A
Interfaces script
MeasurementTimeStep
Specifies time step in seconds between two
consecutive simulated observations
Data Type Real
Allowed Values Real > 0
Access set
Default Value 60
Units seconds
Interfaces script
Propagator
Name of Propagator object used for calculation
Data Type Propagator Object
Allowed Values Any valid Propagator object
Access set
Default Value None
Units N/A
Interfaces script
Remarks
Navigation Requires Use of Fixed Step Numerical
Integration
GMAT navigation requires use of fixed stepped propagation. The Simulator
resource has a Propagator field containing the name of the Propagator
resource that will be used during the simulation. As shown in the Note below,
there are some hard restrictions on the choice of error control specified for the
ForceModel resource associated with your propagator.
Note
For simulation, the ErrorControl parameter specified for the
ForceModel resource associated with the Simulator
Propagator must be set to 'None.' Of course, when using fixed
step control, the user must choose a step size, as given by the
Propagator InitialStepSize field, for the chosen orbit
regime and force profile, that yields the desired accuracy.
Propagator Settings
The Simulator resource has a Propagator field containing the name of the
Propagator resource that will be used during the estimation process. The
minimum step size, MinStep, of your propagator should always be set to 0.
Interactions
Resource Description
TrackingFileSet
resource Must be created in order to tell the Simulator resource, via
the AddData field, which data types will be simulated and to
specify the name of the output tracking data file (via
FileName)
Propagator Used by GMAT to generate the simulated orbit
resource
RunSimulator
command Must use the RunSimulator command to actually create the
data defined by the Simulator resource
Examples
The example below illustrates using the simulator to generate DSN range
measurements. This example is more detailed than usual as it can actually be run
to produce a file, simData.gmd, that contains a single range measurement for a
fictional DSN ground station. For a more comprehensive example of simulating
measurements, see the Chapter 13, Simulate DSN Range and Doppler Data
tutorial.
%Create and Configure Spacecraft
Create Spacecraft SimSat;
GMAT SimSat.DateFormat = UTCGregorian;
GMAT SimSat.Epoch = '19 Aug 2015 00:00:00.000';
GMAT SimSat.X = -126544963;
GMAT SimSat.Y = 61978518;
GMAT SimSat.Z = 24133225;
GMAT SimSat.VX = -13.789;
GMAT SimSat.VY = -24.673;
GMAT SimSat.VZ = -10.662;
GMAT SimSat.AddHardware = {SatTransponder, SatTranponderAntenna};
%Create and configure RF hardware
Create Antenna SatTranponderAntenna DSNReceiverAntenna DSNTransmitterAntenna;
Create Transponder SatTransponder;
GMAT SatTransponder.PrimaryAntenna = SatTranponderAntenna;
Create Transmitter DSNTransmitter;
GMAT DSNTransmitter.PrimaryAntenna = DSNTransmitterAntenna;
GMAT DSNTransmitter.Frequency = 7200;
Create Receiver DSNReceiver;
GMAT DSNReceiver.PrimaryAntenna = DSNReceiverAntenna;
%Create and configure ground station and related error model
Create GroundStation DSN;
GMAT DSN.AddHardware = ...
{DSNTransmitter, DSNReceiver, DSNTransmitterAntenna, DSNReceiverAntenna};
GMAT DSN.ErrorModels = {DSNrange};
Create ErrorModel DSNrange;
GMAT DSNrange.Type = 'DSN_SeqRange';
GMAT DSNrange.NoiseSigma = 10;
%Define data types
Create TrackingFileSet simData;
GMAT simData.AddTrackingConfig = {{DSN,SimSat,DSN}, DSN_SeqRange};
GMAT simData.FileName = {'simData.gmd'};
% Create and configure the Simulator object
Create ForceModel FM1
FM1.ErrorControl = None %Fixed step integration required for Navigation
Create Propagator prop;
prop.FM = FM1
prop.MinStep = 0 %For Navigation, allow propagator to take arbitrarily small steps
Create Simulator sim;
GMAT sim.AddData = {simData};
GMAT sim.Propagator = prop;
GMAT sim.EpochFormat = UTCGregorian;
GMAT sim.InitialEpoch = '19 Aug 2015 08:00:00.000';
GMAT sim.FinalEpoch = '19 Aug 2015 08:00:01.000';
GMAT sim.MeasurementTimeStep = 60;
GMAT sim.AddNoise = On;
% Mission Sequence - run the simulator.
BeginMissionSequence;
RunSimulator sim;
