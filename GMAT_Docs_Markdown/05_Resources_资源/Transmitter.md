# Transmitter

> 来源：第 1204-1209 页

Transmitter
Transmitter — Defines the electronics hardware, attached to a GroundStation
resource, that transmits an RF signal.
Description
A ground station needs a Transmitter to transmit the RF signal to both user
spacecraft and to navigation spacecraft such as TDRS. A Transmitter is
assigned on the AddHardware list of an instance of a GroundStation.
See Also GroundStation, Antenna
Fields
Field Description
Frequency
Transmit frequency
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 0
Units MHz
Interfaces script
PrimaryAntenna
Antenna resource used by GroundStation resource to
transmit a signal
Data Type Antenna Object
Allowed Values Any Antenna object
Access set
Default Value None
Units N/A
Interfaces script
Remarks
Discussion of how Transmitter frequency is used
A transmitter will be attached to a GroundStation resource. As discussed in the
RunSimulator Help, for the case where a ramp table is not used, the transmit
frequency is used directly to calculate the DSN range and Doppler
measurements. If a ramp table is specified on the relevant TrackingFileSet, the
frequency profile specified in the ramp table is used and the transmitter
frequency is ignored.
Examples
Create and configure a Transmitter object
Create Antenna DSNAntenna;
Create Transmitter Transmitter1;
Transmitter1.PrimaryAntenna = DSNAntenna;
Transmitter1.Frequency = 7186.3;
Create GroundStation DSN
DSN.AddHardware = {Transmitter1};
BeginMissionSequence;
