# Transponder

> 来源：第 1210-1217 页

Transponder
Transponder — Defines the electronics hardware, typically attached to a
spacecraft, that receives and automatically re-transmits an incoming signal.
Description
The spacecraft Transponder model is required for modeling DSN two way
range and Doppler data types. The Transponder object includes modeling of a
retransmission delay due to the spacecraft transponder electronics. You can also
specify a turn around ratio which is a multiplicative ratio describing how the
frequency of the retransmitted signal differs from the received frequency. The
incoming and outgoing frequencies are designed to be different so as to avoid RF
interference between the signal transmitted by the ground station to the
spacecraft and the return signal from the spacecraft to the ground station.
See Also: GroundStation, Antenna
Fields
Field Description
HardwareDelay
Transponder electronics delay between receiving time and
transmitting time at the transponder. It is applied for both
simulation and estimation, with or without ramp table use.
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 0
Units seconds
Interfaces script
PrimaryAntenna
Antenna resource used by the Transponder resource
Data Type Antenna Object
Allowed Values Any valid Antenna object
Access set
Default Value None
Units N/A
Interfaces script
TurnAroundRatio
Transponder turn around ratio which is used in both
simulation and estimation. For the DSN Doppler data type
where an input ramp table is not used, changing the
transponder turn around ratio appreciably changes the
measurement. For all DSN data types, changing the turn
around ratio affects the media correction calculations
which will typically result in a small change in the
measurement. See the RunSimulator and RunEstimator
help for additional details.
Data Type STRING_TYPE
Allowed A string in form of 'a/b' where a and b are
Values real numbers
Access set
Default '240/221'
Value
Units N/A
Interfaces script
Remarks
Turn around ratio affects media correction calculations
Suppose you are given a signal with multiple ‘n’ legs. In order to calculate the
media corrections for a given leg, we need to know the associated frequency for
that leg. The turn-around ratio is used to calculate the frequency for legs 2
through n. If media corrections are modeled, then, for both DSN range and
Doppler measurements, the value of the turn-around ratio, as set in the
Transponder resource, will have an effect on the measurements and thus both
simulation and estimation processes will be affected.
Independent of media corrections, how does the turn
around ratio, as set in the Transponder resource, affect
DSN measurements?
Assume that media corrections are turned off so that we can ignore any, typically
small, changes to the DSN measurements caused by media corrections. We make
the following observations.
1. The value of Transponder.TurnAroundRatio has no effect on DSN range
measurements.
2. If a ramp table is provided, then the value of
Transponder.TurnAroundRatio has no effect on DSN Doppler
measurements. In this case, the multiplicative turn around ratio used to
calculate the computed measurement is based upon the Uplink Band given
in the ramp table. (240/221 for S-band and 880/749 for X band)
3. If a ramp table is not provided, then the value of
Transponder.TurnAroundRatio has a proportional effect on DSN Doppler
measurements. For example, if the turn around ratio is doubled, then so is
the DSN Doppler measurement in Hz.
For additional discussion on how the Transponder.TurnAroundRatio field
affects the DSN measurements, see the RunSimulator and RunEstimator help.
Custom turn-around ratios for DSN Doppler data
As mentioned above, the DSN Doppler (TRK-2-34 Type 17) data type
observation value depends upon the transponder turn-around ratio. As shown in
the tables in the RunSimulator and RunEstimator help, for ramped Doppler
data, GMAT only allows for the use of the standard S-band (240/221) and X-
band (880/749) turn-around ratios. For Doppler data where a ramp table is not
used, setting the Transponder turn-around ratio will correctly model the
Doppler data. GMAT cannot currently accommodate custom turn-around ratios
for ramped Doppler data.
Examples
% Create and configure a Transponder object
Create Spacecraft Sat1;
Create Antenna HGA;
Create Transponder Transponder1;
Transponder1.PrimaryAntenna = HGA;
Transponder1.HardwareDelay = 0.0;
Transponder1.TurnAroundRatio = '240/221';
Sat1.AddHardware = {HGA, Transponder1};
BeginMissionSequence;
