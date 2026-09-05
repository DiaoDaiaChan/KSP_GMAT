# Simulate DSN Range and Doppler Data

> 来源：第 272-296 页

Chapter 13. Simulate DSN Range and Doppler
Data
Audience Intermediate level
Length 40 minutes
Prerequisites Basic Mission Design Tutorials
Tut_Simulate_DSN_Range_and_Doppler_Data.script
Script Files
Tut_Simulate_DSN_Range_and_Doppler_Data_3_weeks.script
Objective and Overview
Note
GMAT currently implements a number of different data types
for orbit determination. Please refer to Tracking Data Types for
OD for details on all the measurement types currently
supported by GMAT. The measurements being considered here
are DSN two way range and DSN two way Doppler.
In this tutorial, we will use GMAT to generate simulated DSN range and
Doppler measurement data for a sample spacecraft in orbit about the Sun. The
spacecraft in this tutorial is in an Earth “drift away” type orbit about 1 AU away
from the Sun and almost 300 million km away from the Earth.
The basic steps of this tutorial are:
1. Create and configure the spacecraft, spacecraft transponder, and related
parameters
2. Create and configure the Ground Station and related parameters
3. Define the types of measurements to be simulated
4. Create and configure Force model and propagator
5. Create and configure Simulator object
6. Run the mission and analyze the results
7. Create a realistic GMAT Measurement Data (GMD) file
Note that this tutorial, unlike most of the mission design tutorials, will be
entirely script based. This is because most of the resources and commands
related to navigation are not implemented in the GUI and are only available via
the script interface.
As you go through the tutorial below, it is recommended that you paste the script
segments into GMAT as you go along. After each paste into GMAT, you should
perform a syntax check by hitting the Save, Sync button ( ). To avoid syntax
errors, where needed, don’t forget to add the following command to the last line
of the script segment you are checking.
BeginMissionSequence
We note that in addition to the material presented here, you should also look at
the individual Help resources for all the objects and commands we create and
use here. For example, Spacecraft, Transponder, Transmitter,
GroundStation, ErrorModel, TrackingFileSet, RunSimulator, etc all have
their own Help pages.
Create and configure the spacecraft, spacecraft
transponder, and related parameters
For this tutorial, you’ll need GMAT open, with a new empty script open. To
create a new script, click New Script, ( )
Create a satellite and set its epoch and Cartesian
coordinates
Since this is a Sun-orbiting spacecraft, we choose to represent the orbit in a Sun-
centered coordinate frame which we define using the scripting below.
% Create the Sun-centered J2000 frame.
Create CoordinateSystem SunMJ2000Eq;
SunMJ2000Eq.Origin = Sun;
SunMJ2000Eq.Axes = MJ2000Eq; %Earth mean equator axes
Next, we create a new spacecraft, Sat, and set its epoch and Cartesian
coordinates.
Create Spacecraft Sat;
Sat.DateFormat = UTCGregorian;
Sat.CoordinateSystem = SunMJ2000Eq;
Sat.DisplayStateType = Cartesian;
Sat.Epoch = 19 Aug 2015 00:00:00.000;
Sat.X = -126544968
Sat.Y = 61978514
Sat.Z = 24133221
Sat.VX = -13.789
Sat.VY = -24.673
Sat.VZ = -10.662
Sat.Id = 11111;
Note that, in addition to setting Sat’s coordinates, we also assigned it an ID
number. This is the number that will be written to the GMAT Measurement Data
(GMD) file that we will discuss later.
Create a Transponder object and attach it to our spacecraft
To simulate navigation measurements for a given spacecraft, GMAT requires
that a Transponder object, which receives the ground station uplink signal and
re-transmits it, typically, to a ground station, be attached to the spacecraft.
Below, we create the Transponder object and attach it to our spacecraft.
Create Antenna HGA;
Create Transponder SatTransponder;
SatTransponder.PrimaryAntenna = HGA;
SatTransponder.HardwareDelay = 1e-06; %seconds
SatTransponder.TurnAroundRatio = '880/749';
Sat.AddHardware = {SatTransponder, HGA};
After we create the Transponder object, there are three fields,
PrimaryAntenna, HardwareDelay, and TurnAroundRatio that must be set.
The PrimaryAntenna is the antenna that the spacecraft transponder,
SatTransponder, uses to receive and retransmit RF signals. In the example
above, we set this field to HGA which is an Antenna object we have created.
Currently the Antenna resource has no function but in a future release, it may
have a function. HardwareDelay, the transponder signal delay in seconds, is set
to one micro-second. We set TurnAroundRatio, which is the ratio of the
retransmitted to the input signal, to '880/749.' See the FRC-21_RunSimulator
Help and Appendix A – Determination of Measurement Noise Values for a
discussion on how GMAT uses this input field. As described in the Help, if our
DSN data does not use a ramp table, this turn around ratio is used directly to
calculate the Doppler measurements.
Note that in the last script command above, we attach our newly created
Transponder and its related Antenna object to our spacecraft, Sat.
Create and configure the Ground Station and
related parameters
Create Ground Station Transmitter, Receiver, and Antenna
objects
Before we create the GroundStation object itself, as shown below, we first
create the Transmitter, Receiver, and Antenna objects that must be associated
with any GroundStation.
% Ground Station electronics.
Create Transmitter DSNTransmitter;
Create Receiver DSNReceiver;
Create Antenna DSNAntenna;
DSNTransmitter.PrimaryAntenna = DSNAntenna;
DSNReceiver.PrimaryAntenna = DSNAntenna;
DSNTransmitter.Frequency = 7200; %MHz
In the script segment above, we first created Transmitter, Receiver, and
Antenna objects. The GMAT script line DSNTransmitter.PrimaryAntenna =
DSNAntenna, sets the main antenna that the Transmitter object will be using.
Likewise, the DSNReceiver.PrimaryAntenna = DSNAntenna script line sets the
main antenna that the Receiver object will be using. As previously mentioned,
the Antenna object currently has no function, but we include it here both
because GMAT requires it and for completeness since the Antenna resource
may have a function in a future GMAT release. Finally, we set the transmitter
frequency in the last GMAT script line above. See the RunSimulator Help for a
complete description of how this input frequency is used. As described in the
Help, since in this example we will not be using a ramp table, this input
frequency will be used to calculate the simulated value of the range and Doppler
observations. In addition, this input frequency will also be output to the range
data file created by the RunSimulator command.
Create Ground Station
Below, we create and configure a GroundStation object.
% Create ground station and associated error models
Create GroundStation CAN;
CAN.CentralBody = Earth;
CAN.StateType = Cartesian;
CAN.HorizonReference = Ellipsoid;
CAN.Location1 = -4461.083514
CAN.Location2 = 2682.281745
CAN.Location3 = -3674.570392
CAN.Id = 22222;
CAN.MinimumElevationAngle = 7.0;
CAN.IonosphereModel = 'IRI2007';
CAN.TroposphereModel = 'HopfieldSaastamoinen';
CAN.AddHardware = {DSNTransmitter, DSNAntenna, ...
DSNReceiver};
The script segment above is broken into five sections. In the first section, we
create our GroundStation object and we set our Earth-Centered Fixed Cartesian
coordinates. In the second section, we set the ID of the ground station that will
output to the GMD file created by the RunSimulator command. In the third
section, we set the minimum elevation angle to 7 degrees. Below this ground
station to spacecraft elevation angle, no simulated data will be created. In the
fourth section, we specify which troposphere and ionosphere model we wish to
use to model RF signal atmospheric refraction effects. Finally, in the fifth
section, we attached three pieces of previously created required hardware to our
ground station, a transmitter, a receiver, and an antenna.
Create Ground Station Error Models
It is well known that all measurement types have random noise and/or biases
associated with them. For GMAT, these affects are modelled using ground
station error models. Since we have already created the GroundStation object
and its related hardware, we now create the ground station error models. Since
we wish to simulate both range and Doppler data, we need to create two error
models as shown below, one for range measurements and one for Doppler
measurements.
% Create Ground station error models
Create ErrorModel DSNrange;
DSNrange.Type = 'DSN_SeqRange';
DSNrange.NoiseSigma = 10.63;
DSNrange.Bias = 0.0;
Create ErrorModel DSNdoppler;
DSNdoppler.Type = 'DSN_TCP';
DSNdoppler.NoiseSigma = 0.0282;
DSNdoppler.Bias = 0.0;
CAN.ErrorModels = {DSNrange, DSNdoppler};
The script segment above is broken into three sections. The first section defines
an ErrorModel named DSNrange. The error model Type is DSN_SeqRange
which indicates that it is an error model for DSN sequential range
measurements. The 1 sigma standard deviation of the Gaussian white noise is set
to 10.63 Range Units (RU) and the measurement bias is set to 0 RU.
The second section above defines an ErrorModel named DSNdoppler. The
error model Type is DSN_TCP which indicates that it is an error model for DSN
total count phase-derived Doppler measurements. The 1 sigma standard
deviation of the Gaussian white noise is set to 0.0282 Hz and the measurement
bias is set to 0 Hz.
The third section above attaches the two ErrorModel resources we have just
created to the CAN GroundStation. Note that in GMAT, the measurement noise
or bias is defined on a per ground station basis. Thus, any range measurement
error involving the CAN GroundStation is defined by the DSNRange
ErrorModel and any Doppler measurement error involving the CAN
GroundStation is defined by the DSNdoppler ErrorModel. Note that since
GMAT currently only models two way measurements where the transmitting and
receiving ground stations are the same, we do not have to consider the case
where the transmitting and receiving ground stations are different. Suppose we
were to add an additional GroundStation to this simulation. The measurement
error for observations involving this new GroundStation would be defined by
the ErrorModel resources attached to it.
See Appendix A – Determination of Measurement Noise Values for a discussion
of how we determined the values for NoiseSigma for the two ErrorModel
resources we created.
Define the types of measurements to be
simulated
Now we will create and configure a TrackingFileSet resource. This resource
defines the type of data to be simulated, the ground stations that will be used,
and the file name of the output GMD file which will contain the simulated data.
In addition, the TrackingFileSet resource will define needed simulation
parameters for the various data types.
Create TrackingFileSet DSNsimData;
DSNsimData.AddTrackingConfig = {{CAN, Sat, CAN}, 'DSN_SeqRange'};
DSNsimData.AddTrackingConfig = {{CAN, Sat, CAN}, 'DSN_TCP'};
DSNsimData.FileName = ...
{'Sat_dsn_range_and_doppler_measurements.gmd'};
DSNsimData.UseLightTi = true;
DSNsimData.UseRelativityCorrection = true;
DSNsimData.UseETminusTAI = true;
DSNsimData.SimDopplerCountInterval = 10.0;
DSNsimData.SimRangeModuloConstant = 3.3554432e+07;
The script lines above are broken into three sections. In the first section, the
resource name, DSNsimData, is declared, the data types are defined, and the
output file name is specified. AddTrackingConfig is the field that is used to
define the data types. The first AddTrackingConfig line tells GMAT to simulate
DSN range two way measurements for the CAN to Sat to CAN measurement
strand. The second AddTrackingConfig line tells GMAT to simulate DSN
Doppler two way measurements for the CAN to Sat to CAN measurement
strand.
The second section above sets some simulation parameters that apply to both the
range and Doppler measurements. We set UseLightTime to True in order to
generate realistic measurements where GMAT takes into account the finite speed
of light. The last two parameters in this section, UseRelativityCorrection and
UseETminusTAI, are set to True so that general relativistic corrections, as
described in Moyer [2000], are applied to the light time equations.
The third section above sets simulation parameters that apply to a specific
measurement type. SimDopplerCountInterval applies only to Doppler
measurements and SimRangeModuloConstant applies only to range
measurements. We note that the “Sim” in the field names is used to indicate that
these fields only are applicable when GMAT is in simulation mode (i.e., when
using the RunSimulator command) data and not when GMAT is in estimation
mode (i.e., when using the RunEstimator command).
SimDopplerCountInterval, the Doppler Count Interval, is set to 10 seconds and
SimRangeModuloConstant, the maximum possible range value, is set to
33554432. See the RunSimulator Help and Appendix A – Determination of
Measurement Noise Values for a description of how these parameters are used to
calculate the measurement values.
Create and configure Force model and
propagator
We now create and configure the force model and propagator that will be used
for the simulation. For this deep space drift away orbit, we naturally choose the
Sun as our central body. Since we are far away from all the planets, we use point
mass gravity models and we include the effects of the Sun, Earth, Moon, and
most of the other planets. In addition, we model Solar Radiation Pressure (SRP)
affects and we include the affect of general relativity on the dynamics. The script
segment accomplishing this is shown below.
Create ForceModel Fm;
Create Propagator Prop;
Fm.CentralBody = Sun;
Fm.PointMasses = {Sun, Earth, Luna, Mars, Saturn, ...
Uranus, Mercury, Venus, Jupiter};
Fm.SRP = On;
Fm.RelativisticCorrection = On;
Fm.ErrorControl = None;
Prop.FM = Fm;
Prop.MinStep = 0;
Create and configure Simulator object
As shown below, we create and configure the Simulator object used to define
our simulation.
Create Simulator Sim;
Sim.AddData = {DSNsimData};
Sim.EpochFormat = UTCGregorian;
Sim.InitialEpoch = '19 Aug 2015 00:00:00.000';
Sim.FinalEpoch = '19 Aug 2015 00:12:00.000';
Sim.MeasurementTimeStep = 600;
Sim.Propagator = Prop;
Sim.AddNoise = Off;
In the first script line above, we create a Simulator object, Sim. The next field
set is AddData which is used to specify which TrackingFileSet should be used.
Recall that the TrackingFileSet specifies the type of data to be simulated and
the file name specifying where to store the data. The TrackingFileSet,
DSNsimData, that we created in the Define the types of measurements to be
simulated section, specified that we wanted to simulate two way DSN range and
Doppler data that involved the CAN GroundStation.
The next three script lines, which set the EpochFormat, InitialEpoch, and
FinalEpoch fields, specify the time period of the simulation. Here, we choose a
short 12 minute duration.
The next script line sets the MeasurementTimeStep field which specifies the
requested time between measurements. We choose a value of 10 minutes. This
means that our data file will contain a maximum of two range measurements and
two Doppler measurements.
The next script line sets the Propagator field which specifies which Propagator
object should be used. We set this field to the Prop Propagator object which we
created in the Create and configure Force model and propagator section.
Finally, in the last line of the script segment, we set the AddNoise field which
specifies whether or not we want to add noise to our simulated measurements.
The noise that can be added is defined by the ErrorModel objects that we
created in the Create and configure the Ground Station and related parameters
section. As discussed in the Create and configure the Ground Station and related
parameters section and Appendix A – Determination of Measurement Noise
Values, the noise added to the range measurements would be Gaussian with a
one sigma value of 10.63 Range Units and the noise added to the Doppler
measurements would be Gaussian with a one sigma value of 0.0282 Hz. For this
simulation, we choose not to add noise.
Run the mission and analyze the results
The script segment used to run the mission is shown below.
BeginMissionSequence
RunSimulator Sim
The first script line, BeginMissionSequence, is a required command which
indicates that the “Command” section of the GMAT script has begun. The
second line of the script issues the RunSimulator command with the Sim
Simulator resource, defined in the Create and configure Simulator object section,
as an argument. This tells GMAT to perform the simulation specified by the Sim
resource.
We have now completed all of our script segments. See the file, Simulate DSN
Range and Doppler Data.script, for a listing of the entire script. We are now
ready to run the script. Hit the Save,Sync,Run button, ( ). Because we are
only simulating a small amount of data, the script should finish execution in
about one second.
Let’s take a look at the output created. The file created,
Sat_dsn_range_and_doppler_measurements.gmd, was specified in the
TrackingFileSet resource, DSNsimData, that we created in the Define the types
of measurements to be simulated section. The default directory, if none is
specified, is the GMAT ‘output’ directory. Let’s analyze the contents of this
“GMAT Measurement Data” or GMD file as shown below.
% GMAT Internal Measurement Data File
27253.500405092593 DSN_SeqRange 9004 22222 11111 26016945.24902344 2 7.2e+009 3.3554432e+007
27253.500405092593 DSN_TCP 9006 22222 11111 2 10 -8459336323.89349840
27253.507349537038 DSN_SeqRange 9004 22222 11111 21728172.10375977 2 7.2e+009 3.3554432e+007
27253.507349537038 DSN_TCP 9006 22222 11111 2 10 -8459335611.28409770
The first line of the file is a comment line indicating that this is a file containing
measurement data stored in GMAT’s internal format. There are 4 lines of data
representing range data at two successive times and Doppler data at two
successive times. As we expected, we have no more than 4 total measurements.
Refer to the TrackingFileSet Help for a description of the range and Doppler
GMD file format.
We now analyze the first line of data which represents a DSN two way range
measurement at the start of the simulation at '19 Aug 2015 00:00:00.000 UTCG’
which corresponds to the output TAI modified Julian Day of
27253.500405092593 TAIMJD.
The second and third fields, DSN_SeqRange and 9004, are just internal GMAT
codes indicating the use of DSN range (Trk 2-34 type 7) data.
The 4th field, 22222, is the Downlink station ID. This is the ID we gave the
CAN GroundStation object that we created in the Create and configure the
Ground Station and related parameters section. The 5th field, 11111, is the
spacecraft ID. This is the ID we gave the Sat Spacecraft object that we created
in the Create and configure the spacecraft, spacecraft transponder, and related
parameters section.
The 6th field, 26016945.24902344, is the actual DSN range observation value in
RU.
The 7th field, 2, is an integer which represents the Uplink Band of the uplink
GroundStation, CAN. The designation, 2, represents X-band. See the
RunSimulator Help for a detailed discussion of how GMAT determines what
value should be written here. As described in the Help, since we are not using a
ramp table, GMAT determines the Uplink Band by looking at the transmit
frequency of the Transmitter object attached to the CAN ground station. GMAT
knows that the 7200 MHz value that we assigned to CAN’s Transmitter
resource, DSNTransmitter, corresponds to an X-band frequency.
The 8th field, 7.2e+009, is the transmit frequency of CAN at the time of the
measurement. Since we are not using a ramp table, this value will be constant for
all measurements and it is given by the value of the frequency of the
Transmitter object, DSNTransmitter, that we attached to the CAN ground
station. Recall the following script segment, DSNTransmitter.Frequency =
7200; %MHz, from the Create and configure the Ground Station and related
parameters section.
The 9th field, 3.3554432e+007, represents the integer range modulo number that
helps define the DSN range measurement. This is the value that we set when we
created and configured the TrackingFileSet DSNsimData object in the Define
the types of measurements to be simulated section. Recall the following script
command,
DSNsimData.SimRangeModuloConstant = 3.3554432e+07;
This range modulo number is discussed in Appendix A – Determination of
Measurement Noise Values and is defined as M, the length of the ranging code in
RU.
We now analyze the second line of data which represents a DSN two way
Doppler measurement at the start of the simulation at '19 Aug 2015 00:00:00.000
UTCG’ which corresponds to the output TAI modified Julian Day of
27253.500405092593 TAIMJD.
The second and third fields, Doppler and 9006, are just internal GMAT codes
indicating the use of DSN Doppler (derived from two successive Trk 2-34 type
17 Total Count Phase measurements) data.
The 4th field, 22222, is the Downlink station ID. This is the ID we gave the
CAN GroundStation object that we created in the Create and configure the
Ground Station and related parameters section. The 5th field, 11111, is the
spacecraft ID. This is the ID we gave the Sat Spacecraft object that we created
in the Create and configure the spacecraft, spacecraft transponder, and related
parameters section.
The 6th field, 2, is an integer which represents the Uplink Band of the uplink
GroundStation, CAN. As we mentioned when discussing the range
measurement, the designation, 2, represents X-band.
The 7th field, 10, is the Doppler Count Interval (DCI) used to help define the
Doppler measurement. This is the value that we set when we created and
configured the TrackingFileSet DSNsimData object in the Define the types of
measurements to be simulated section. Recall the following script command,
DSNsimData.SimDopplerCountInterval = 10.0;
The DCI is also discussed in Appendix A – Determination of Measurement Noise
Values.
The 8th field, -7819057474.22393610, is the actual DSN Doppler observation
value in Hz.
The third line of data represents the second DSN two way range measurement at
'19 Aug 2015 00:10:00.000 UTCG’ which corresponds to the output TAI
modified Julian Day time of 27253.507349537038 TAIMJD. The fourth line of
data represents the second DSN two way Doppler measurement at '19 Aug 2015
00:10:00.000 UTCG.’
Create a more realistic GMAT Measurement Data
(GMD)
We have run a short simple simulation and generated a sample GMD file. Our
next goal is to generate a realistic GMD file that a different script can read in and
generate an orbit determination solution. To add more realism, we will do the
following:
Generate data from additional ground stations
Add the use of a ramp table
Perform a longer simulation
Add measurement noise
In order to generate measurement data from additional ground stations, we must
first create and configure additional GroundStation objects. Below, we create
and configure two new ground stations, GDS and MAD.
Create GroundStation GDS;
GDS.CentralBody = Earth;
GDS.StateType = Cartesian;
GDS.HorizonReference = Ellipsoid;
GDS.Location1 = -2353.621251;
GDS.Location2 = -4641.341542;
GDS.Location3 = 3677.052370;
GDS.Id = '33333';
GDS.AddHardware = {DSNTransmitter, DSNAntenna, DSNReceiver};
GDS.MinimumElevationAngle = 7.0;
GDS.IonosphereModel = 'IRI2007';
GDS.TroposphereModel = 'HopfieldSaastamoinen';
Create GroundStation MAD;
MAD.CentralBody = Earth;
MAD.StateType = Cartesian;
MAD.HorizonReference = Ellipsoid;
MAD.Location1 = 4849.519988;
MAD.Location2 = -0360.641653;
MAD.Location3 = 4114.504590;
MAD.Id = '44444';
MAD.AddHardware = {DSNTransmitter, DSNAntenna, DSNReceiver};
MAD.MinimumElevationAngle = 7.0;
MAD.IonosphereModel = 'IRI2007';
MAD.TroposphereModel = 'HopfieldSaastamoinen';
Now that we have defined two additional ground stations, we must specify the
measurement noise associated with these new ground stations. This can be done
using the previously created ErrorModel resources as shown below.
GDS.ErrorModels = {DSNrange, DSNdoppler};
MAD.ErrorModels = {DSNrange, DSNdoppler};
Next, we must add the corresponding two way range and Doppler measurements
associated with our new ground stations to our TrackingFileSet object,
DSNsimData, as shown below.
DSNsimData.AddTrackingConfig = {{GDS, Sat, GDS}, 'DSN_SeqRange'};
DSNsimData.AddTrackingConfig = {{GDS, Sat, GDS}, 'DSN_TCP'};
DSNsimData.AddTrackingConfig = {{MAD, Sat, MAD}, 'DSN_SeqRange'};
DSNsimData.AddTrackingConfig = {{MAD, Sat, MAD}, 'DSN_TCP'};
We now create our ramp table that many but not all missions use. A ramp table is
a table that allows GMAT to calculate the transmit frequency of all the ground
stations involved in our simulation. Recall that GMAT needs to know the
transmit frequency, as a function of time, in order to calculate the value of the
observations. The term “ramp” is used because the transmit frequency increases
linearly with time and a graph of transmit frequency vs. time would typically
show a ramp. A mission that does not use a ramp table simply uses a constant
transmit frequency for a given ground station.
To modify our script to accommodate the use of a ramp table, we modify our
TrackingFileSet object, DSNsimData, as shown below.
DSNsimData.RampTable = ...
{'../output/Simulate DSN Range and Doppler Data 3 weeks.rmp'};
We must now create a file with the name shown above in the GMAT ‘output’
directory. Refer to the TrackingFileSet Help for a description of the ramp table
file format. In order for GMAT to determine the transmit frequencies of all the
ground stations, the ramp table must have at least one row of data for every
ground station providing measurement data. The contents of our ramp table is
shown below.
27252 22222 11111 2 1 7.2e09 0.2
27252 33333 11111 2 1 7.3e09 0.3
27252 44444 11111 2 1 7.4e09 0.4
Each row of data above is called a ramp record. Let’s analyze the first ramp
record. The first field, 27252, is the TAIMJD date of the ramp record.
The second field, 22222, is the ground station ID of the GroundStation object
whose frequency is being specified. We note that the ID 22222 corresponds to
the CAN ground station. The third field, 11111, is the ID of the spacecraft that
the CAN ground station is transmitting to. We recognize 11111 as the ID of the
Sat spacecraft.
The 4th field, 2, is an integer representing the uplink band of the transmission.
The integer 2 represents X-band. The 5th field, 1, is an integer describing the
ramp type. The integer 1 represents the start of a new ramp.
The 6th field, 7.2e9, is the transmission frequency in Hz, from CAN to Sat at the
time given by the first field. The 7th input is the ramp rate in Hz/s.
We now describe how GMAT uses the ramp record to determine the transmit
frequency of CAN to Sat at a given time. We let TAIMJD be the time associated
with the ramp record. Then GMAT will calculate the value of the transmit
frequency at t = 27252.5 TAIMJD as shown below.
f ( t ) = f ( t o ) + R a m p R a t e * 86400 * ( t − t o )
where
f ( t o ) = Transmit Frequency at the start of the ramp record
f ( t ) = Transmit Frequency at a later time, t > t o
Note that, in the typical case where there are numerous ramp records, it is
assumed that t o < t is chosen as close to time t as possible. For our case above,
the transmit frequency from CAN to Sat at time t is
f ( t ) = 7.2 e 9 + 0.2 * 86400 * ( 27252.5 − 27252 ) = 7200008640 Hz
The second and third rows of the ramp table allow GMAT to calculate the
transmit frequency from GDS to Sat and MAD to Sat, respectively. We now
create a file, Simulate DSN Range and Doppler Data Realistic GMD.rmp,
with the contents shown above and place it in GMAT’s ‘output’ folder.
We make one final comment about the use of a ramp table. We note that when a
ramp table is used, GMAT uses the various script inputs (e.g.,
SatTransponder.TurnAroundRatio and DSNTransmitter.Frequency)
differently. See the RunSimulator Help for details.
We only have two steps remaining in order to create a script that generates more
realistic measurement data. The first step is to increase the simulation time from
10 minutes to the more realistic 3 weeks worth of data that is typically needed to
generate an orbit determination solution for a spacecraft in this type of deep
space orbit. The second step is to turn on the measurement noise. These two
steps are accomplished by making the following changes to our TrackingFileSet
object, DSNsimData.
Sim.FinalEpoch = '09 Sep 2015 00:00:00.000';
Sim.AddNoise = On;
Sim.MeasurementTimeStep = 3600;
Note that above, in addition to implementing the two needed steps, we also
changed the measurement time step from 600 seconds to 3600 seconds. This is
not a realistic time step as many missions would use a time step that might even
be less than 600 seconds. We used this larger time step for tutorial purposes only
so that the script would not take too long to run.
A complete script, containing all the changes we have made in the Create a more
realistic GMAT Measurement Data (GMD) section, is contained in the file,
Tut_Simulate_DSN_Range_and_Doppler_Data_3_weeks.script. Note that in
this file, in addition to the changes above, we have also changed the GMD
output file name to Simulate DSN Range and Doppler Data 3 weeks.gmd.
Now run the script which should take approximately 1-2 minutes since we are
generating much more data than previously. We will use the GMD file we have
created here as input to an estimation script we will build in the next tutorial,
Orbit Estimation using DSN Range and Doppler Data.
References
Mesarch M. Mesarch, M. Robertson, N. Ottenstein, A. Nicholson, M.
[2007] Nicholson, D. Ward, J. Cosgrove, D. German, S. Hendry, J.
Shaw, “Orbit Determination and Navigation of the SOlar
TErrestrial Relations Observatory (STEREO)”, 20th
International Symposium on Space Flight Dynamics, Annapolis,
MD, September 24-28, 2007.
Moyer Moyer, Theodore D., Formulation for Observed and Computed
[2000] Values of Deep Space Network Data Types for Navigation (JPL
Publication 00-7), Jet Propulsion Laboratory, California Institute
of Technology, October 2000.
Schanzle Schanzle, A., Orbit Determination Error Analysis System
[1995] (ODEAS) Report on Error Sources and Nominal 3-Sigma
Uncertainties for Covariance Analysis Studies Using ODEAS
(Update No. 2), Computer Sciences Corporation (CSC) memo
delivered as part of NASA contract NAS-5-31500, May 31,
1995.
Appendix A – Determination of Measurement
Noise Values
We now say a few words on how we determined the values for NoiseSigma for
the two ErrorModel resources we created. The computed value of the DSN
range measurement is given by (Moyer [2000]):
C ∫ t 1 t 3 f T ( t ) d t , mod M (RU)
where
t 1 , t 3 = Transmission and Reception epoch, respectively
f T = Ground Station transmit frequency
C = transmitter dependent constant (221/1498 for X-band and 1/2 for S-Band)
M = length of the ranging code in RU
We note that M as defined above is equal to SimRangeModuloConstant which
was discussed in the Define the types of measurements to be simulated section.
By manipulation of the equation above, we can find a relationship between RU
and meters, as shown below.
C d ( in meters ) c f ¯ T = d(in RU)
where
f ¯ T = ∫ t 1 t 3 f T ( t ) d t ( t 3 − t 1 ) =
average transmit frequency (between transmit and receive),
c=speed of light in m/s
d= round trip distance
If we assume the round trip distance is 1 meter, we have
d(in RU) = C f ¯ T c
Recall that in the Create and configure the Ground Station and related
parameters section, we set DSNTransmitter.Frequency = 7200; This
corresponds to an X-band frequency (so, C=221/1498) of 7200e6 Hz. For the
case where a ramp table is not used, we have a constant frequency, f ¯ T = f T ,
and thus
d(in RU)= 221 1498 7200 e 6 299792458 = 3 .543172 RU
For this example, for DSN range measurements, we want to use a 1 sigma noise
bias of 3 meters (Schanzle [1995]). From the calculations above, we determine
that this corresponds to 3*3.543172 ≈ 10.63 RU.
We now turn our attention to the DSN Doppler measurement. The DSN Doppler
measurement that GMAT uses is actually a derived observation, O, calculated
using two successive Total Count Phase, ϕ , (type 17 Trk 2-34 record)
measurements as shown below.
O ≡ − [ ϕ ( t 3 e ) − ϕ ( t 3 s ) ] t 3 e − t 3 s (Hz)
where
t 1 s , t 1 e = start and end of transmission interval
t 3 s , t 3 e = start and end of reception interval
ϕ = Total Count Phase (type 17 Trk 2-34 record)
In the absence of measurement noise, one can show (Moyer [2000]), that the
Observed value (O) above equals the Computed (C) value below.
C = − M 2 ( t 3 e − t 3 s ) ∫ t 1 s t 1 e f T ( t 1 ) d t 1 = − M 2 ( t 1 e − t 1 s ) D
C I f ¯ T (Hz)
where
t 1 s , t 1 e = start and end of transmission interval
f T = transmit frequency
M 2 = Transponder turn around ratio (typically, 240/221 for S-
band and 880/749 for X-band)
DCI = ( t 3 e − t 3 s ) = Doppler Count Interval
f ¯ T ≡ ∫ t 1 s t 1 e f T ( t 1 ) d t 1 ( t 1 e − t 1 s ) = average transmit frequency
Neglecting ionospheric media corrections, further calculation (Mesarch [2007])
shows that the values of O and C can be related to an average range rate value, ρ
˙ ¯ , as shown below.
ρ ˙ ¯ O b s e r v e d = c ( 1 + O M 2 f ¯ T ) , ρ ˙ ¯ C o m p u t e d = c ( 1 + C M
2 f ¯ T )
where
ρ ˙ ¯ ≡ ( Round Trip distance at t 3 e ) − ( Round Trip distance at t 3 s ) t 3 e − t
3 s
Thus, we determine that
ρ ˙ ¯ O b s e r v e d − ρ ˙ ¯ C o m p u t e d = c M 2 f ¯ T ( O − C )
The quantity, ( O − C ) , above represents the measurement noise and thus the
equation gives us a way to convert measurement noise in Hz to measurement
noise in mm/s. To convert from mm/s to Hz, simply multiply by M 2 f ¯ T c = M
2 f ¯ T 299792458000 . In our case, where we use a constant X-band frequency
of 7.2e9, the conversion factor is given by 880 749 7.2 e 9 299792458000 ≈ 0
.0282 . For this tutorial, we use a 1 sigma noise value of 1 mm/s (Schanzle
[1995]) which corresponds to this value of 0.0282 Hz.
