# RunSimulator

> 来源：第 1534-1541 页

RunSimulator
RunSimulator — Generates simulated navigation measurements
Script Syntax
RunSimulator Simulator_InstanceName
Description
The RunSimulator command generates the simulated measurements specified
in the user-provided Simulator resource. An output file, with name specified in
the Simulator resource is created.
See Also: Simulator
Remarks
Content of the Output File for DSN data
After the RunSimulator command has finished execution, one or more output
files, as defined in the specified Simulator object, will be created. Each row of
data in an output file contains information about one specific measurement at a
given time. The format for a given row of data is described fully in the
TrackingFileSet resource help.
Currently, GMAT supports two DSN data types, DSN TRK-2-34 type 7
(sequential range) and DSN TRK-2-34 type 17 (Total count phase). As shown in
the TrackingFileSet resource help, for a type 7 measurement, a row of data has
the following GMAT internal file format.
TAIMJD DSN_SeqRange 9004 [Downlink Station ID] [S/C ID] [Range Observable (RU)] [Uplink Band] [Uplink Freq (Hz)] [Range Modulo (RU)]
where [Uplink Band] species the frequency band of the transmitting station as
shown in the table below.
Uplink Band
Description
Value
0 Unknown or not applicable
1 S-band
2 X-band
3 Ka-band
4 Ku-band
5 L-band
and where the [Range Observable (RU)], is calculated according to
C ∫ t 1 t 3 f T ( t ) d t , mod M (RU)
where
t 1 , t 3 = Transmission and Reception epoch, respectively
f T = Ground Station transmit frequency
C = transmitter dependent constant (221/1498 for X-band and 1/2 for S-Band)
M = length of the ranging code in RU
As shown in the TrackingFileSet resource help, for a DSN TRK-2-34 type 17
measurement, a row of data has the following GMAT internal file format.
# TAIMJD_t3e DSN_TCP 9006 [Downlink Station ID] [S/C ID] [Uplink Band] [DopplerCountInterval_seconds] [DopplerMeas_Hz]
where [Uplink Band] has been previously described and where
DopplerMeas_Hz, the Doppler measurement, is calculated according to
C = − M 2 ( t 3 e − t 3 s ) ∫ t 1 s t 1 e f T ( t 1 ) d t 1 = − M 2 ( t 1 e − t 1 s ) D C
I f ¯ T (Hz)
where
t 1 s , t 1 e = start and end of transmission interval
f T = transmit frequency
M 2 = Transponder turn around ratio (typically, 240/221 for S-
band and 880/749 for X-band)
DCI = ( t 3 e − t 3 s ) = Doppler Count Interval
f ¯ T ≡ ∫ t 1 s t 1 e f T ( t 1 ) d t 1 ( t 1 e − t 1 s ) = average transmit frequency
Note that (t3e - t3s) is known as the Doppler Count Interval and is an input field,
SimDopplerCountInterval, for the TrackingFileSet resource.
When you simulate DSN range or Doppler, you can choose whether or not the
frequency from the transmitting Ground Station is Non-ramped or Ramped. If
you wish to model ramped data, you must supply an input ramp table. The
format of the input ramp table is discussed in the TrackingFileSet resource help.
The table below shows how the values of Uplink Band, C, M2, and transmit
frequency are calculated. The second column shows how the Uplink Band,
which is included in the output file for both range and Doppler measurements, is
calculated. For S-band, a “1” is output and for X-band, a “2” is output.
The output GMAT Measurement Data (GMD) file contains the observable value
which is calculated using the equations shown above. The third column shows
how the value of C or M2, which is used to calculate the observation value
shown in the GMD file, is calculated.
Finally, the fourth column shows how the transmit frequency, which shows up
directly in the GMD file (for DSN range but not DSN Doppler) and is also used
to calculate the observation value given in the GMD file, is calculated.
Measurement Value of C (Range) or M2 (Doppler)
Uplink Band
Type used to calculate Observation
Simulate
Range Set based upon Set based upon Uplink Band result
without ramp transmitter frequency shown in previous column. C=½
table set by user on the for S-band and 221/1498 for X-
Transmitter.Frequency band.
field. If freq is in
[2000-4000] MHz, Value of
then Uplink Band is Transponder.TurnAroundRatio has
S-band. If freq is in no effect on C
[7000-8000] MHz,
then Uplink Band is
X-band.
Simulate
Range with Uplink Band in ramp Set based upon Uplink Band result
ramp table table takes precedence shown in previous column. C=½
over both transmitter for S-band and 221/1498 for X-
frequency set by user band.
and transmit
frequency in ramp Value of
table. Transponder.TurnAroundRatio has
no effect on C
Simulate
Doppler Set based upon M2=Transponder.TurnAroundRatio
without ramp transmitter frequency
table set by user on the
Transmitter.Frequency
field. If freq is in
[2000-4000] MHz,
then Uplink Band is
S-band. If freq is in
[7000-8000] MHz,
then Uplink Band is
X-band.
Simulate
Doppler with Uplink Band in ramp Set based upon Uplink Band result
ramp table table takes precedence shown in previous column.
over both transmitter M2=240/221 for S-band and
frequency set by user 880/749 for X band.
and transmit
frequency in ramp Value of
table. Transponder.TurnAroundRatio has
no effect on M2
As discussed in the Transponder Help, for both ramped and non-ramped data,
the turn around ratio set on the Transponder object,
Transponder.TurnAroundRatio, will be used to calculate the media corrections
needed to determine the value of the simulated range and Doppler
measurements.
Earth Nutation Update Interval
If you want to simulate a Doppler or range rate type of measurement (e.g.,
DSN_TCP and RangeRate) precisely, you will need to set the Earth nutation
update interval to 0 as shown below.
Earth.NutationUpdateInterval = 0
It is good general practice to set the Earth nutation update interval to zero for all
measurement types.
Examples
Run simulation.
%Perform a simulation
Create Simulator mySim
BeginMissionSequence
RunSimulator mySim
For a comprehensive example of running a simulation, see the Chapter 13,
Simulate DSN Range and Doppler Data tutorial.
