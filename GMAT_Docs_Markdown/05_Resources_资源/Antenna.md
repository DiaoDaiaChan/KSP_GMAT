# Antenna

> 来源：第 349-352 页

Antenna
Antenna — Transmits or receives an RF signal.
Description
A number of GMAT resources, GroundStation, Transponder, Receiver, and
Transmitter, use an Antenna resource to transmit and/or receive RF signals.
See Also: GroundStation, Transponder, Receiver, Transmitter
Fields
There are no fields for the Antenna resource.
Examples
This example shows how the Antenna resource is used.
Create Antenna SatTranponderAntenna DSNReceiverAntenna DSNTransmitterAntenna;
Create Transponder SatTransponder;
SatTransponder.PrimaryAntenna = SatTranponderAntenna
Create Spacecraft Sat
Sat.AddHardware = {SatTransponder, SatTranponderAntenna};
Create Transmitter DSNTransmitter
DSNTransmitter.PrimaryAntenna = DSNTransmitterAntenna
Create Receiver DSNReceiver
DSNReceiver.PrimaryAntenna = DSNReceiverAntenna;
Create GroundStation DSN;
DSN.AddHardware = ...
{DSNTransmitter, DSNReceiver, DSNTransmitterAntenna, DSNReceiverAntenna};
BeginMissionSequence;
Since the Antenna resource currently has no fields and thus has no function, for
this GMAT release, we only need to create one Antenna resource that can be
used multiple times. Thus, the example above simplifies as shown below.
Create Antenna GenericAntenna;
Create Transponder SatTransponder;
SatTransponder.PrimaryAntenna = GenericAntenna
Create Spacecraft Sat
Sat.AddHardware = {SatTransponder, GenericAntenna};
Create Transmitter DSNTransmitter
DSNTransmitter.PrimaryAntenna = GenericAntenna
Create Receiver DSNReceiver
DSNReceiver.PrimaryAntenna = GenericAntenna;
Create GroundStation DSN;
DSN.AddHardware = ...
{DSNTransmitter, DSNReceiver, GenericAntenna};
BeginMissionSequence;
