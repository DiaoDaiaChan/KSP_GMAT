# RunEstimator

> 来源：第 1527-1533 页

RunEstimator
RunEstimator — Ingests navigation measurements and generates an estimated
state vector
Script Syntax
RunEstimator BatchEstimatorInv_InstanceName
Description
The RunEstimator command ingests navigation measurements and generates an
estimated state vector according to the specifications of the input
BatchEstimatorInv resource.
See Also: BatchEstimatorInv
Remarks
How GMAT generates “Computed (C)” DSN data
As part of the estimation process, GMAT must calculate the so-called
observation residual, “O-C,” where “C” is the “Computed” measurement. As
discussed in the RunSimulator help, GMAT calculates the DSN range “C”
measurement as
C ∫ t 1 t 3 f T ( t ) d t , mod M (RU)
where
t 1 , t 3 = Transmission and Reception epoch, respectively
f T = Ground Station transmit frequency
C = transmitter dependent constant (221/1498 for X-band and 1/2 for S-Band)
M = length of the ranging code in RU
and GMAT calculates the DSN Doppler measurement as
C = − M 2 ( t 3 e − t 3 s ) ∫ t 1 s t 1 e f T ( t 1 ) d t 1 = − M 2 ( t 1 e − t 1 s ) D C
I f ¯ T (Hz)
where
t 1 s , t 1 e = start and end of transmission interval, respectively
f T = transmit frequency
M 2 = Transponder turn around ratio (typically, 240/221 for S-
band and 880/749 for X-band)
DCI = ( t 3 e − t 3 s ) = Doppler Count Interval
f ¯ T ≡ ∫ t 1 s t 1 e f T ( t 1 ) d t 1 ( t 1 e − t 1 s ) = average transmit frequency
The value of C and M2 used to calculate the computed range or Doppler
measurement depends upon the data type and whether the data being ingested is
ramped or non-ramped according to the table below. The value of the transmit
frequency used to calculate the computed measurement depends upon whether or
not the data being ingested is ramped or non-ramped.
Value of C (Range) or M2 (Doppler) Value of transmit
Data
used to calculate “Computed” frequency used to
Type
measurement calculate “Computed”
measurement
Estimate
Range Set based upon Uplink Band in Use frequency in
without input Range measurement GMD input range GMD file
ramp file. C=½ for S-band and 221/1498
table for X-band. Ground Station
transmit frequency set
via
Transmitter.Frequency
is not used
Estimate
Range Set based upon Uplink Band in Use frequency in
with input ramp table. C=½ for S-band ramp table
ramp and 221/1498 for X-band.
Frequency in input
table
Value of Uplink Band in input GMD file is not used
Range measurement file has no
effect. Ground Station
transmit frequency set
via
Transmitter.Frequency
is not used
Estimate
Doppler M2=Transponder.TurnAroundRatio Use Ground Station
without transmit frequency set
ramp Value of Uplink Band in input via
table Range measurement file has no Transmitter.Frequency
effect. (Note that for Doppler
data, there is no
frequency data in the
GMD file)
Estimate
Doppler Set based upon Uplink Band in Use frequency in
with input ramp table. M2=240/221 for ramp table. (Note that
ramp S-band and 880/749 for X band. for Doppler data,
table there is no frequency
Value of Uplink Bank in input data in the GMD file)
Doppler GMD measurement file
has no effect. Ground Station
transmit frequency set
via
Transmitter.Frequency
is not used
Earth Nutation Update Interval
If you want the estimator to calculate a Doppler or range rate type of
measurement (e.g., DSN_TCP and RangeRate) residual precisely, you will need
to set the Earth nutation update interval to 0 as shown below.
Earth.NutationUpdateInterval = 0
It is good general practice to set the Earth nutation update interval to zero for all
measurement types.
Examples
Run batch estimator.
Create BatchEstimatorInv myBatchEstimator
BeginMissionSequence
RunEstimator myBatchEstimator
For a comprehensive example of reading in measurements and running the
estimator, see the Chapter 14, Orbit Estimation using DSN Range and Doppler
Data tutorial.
