# Receiver

> 来源：第 888-893 页

Receiver
Receiver — Hardware that receives an RF signal.
Description
A GroundStation or Spacecraft resource needs a Receiver. A GroundStation
resource, for example, needs to receive the RF signal from ground station user
spacecraft. A Receiver is assigned on the AddHardware list of an instance of a
GroundStation or Spacecraft.
The receiver resource is also used as the host object for the GPS_PosVec
measurement error model. When using GPS_PosVec data for estimation or
simulation, an ErrorModel instance specifying the GPS_PosVec measurement
type should be assigned on a Receiver object, and that receiver should be
assigned to the associated Spacecraft object.
See Also: GroundStation, Antenna
Fields
Field Description
ErrorModels
User-defined list of ErrorModel objects that describe the
measurement error models used for this receiver. The only
error model type currently supported is GPS_PosVec. This
parameter is only needed when simulating or estimating
using GPS_PosVec data.
Data StringList
Type
Allowed An instance of ErrorModel using the
Values GPS_PosVec observation type
Access set
Default None
Value
Units N/A
Interfaces script
Id
Integer identification number for this receiver. This should
match the receiver ID specified for the GPS_PosVec data
in the GMD file. This parameter is only needed when
simulating or estimating using GPS_PosVec data.
Data Type Integer
Allowed Values Integer >= 0
Access set
Default Value 800
Units N/A
Interfaces script
PrimaryAntenna
Antenna resource used by Receiver or Spacecraft
resource to receive a signal
Data Type Antenna Object
Allowed Values Any valid Antenna object
Access set
Default Value None
Units N/A
Interfaces script
Examples
Create and configure a Receiver object and attach it to a GroundStation.
Create Antenna DSNReceiverAntenna;
Create Receiver Receiver1;
Receiver1.PrimaryAntenna = DSNReceiverAntenna;
Create GroundStation DSN
DSN.AddHardware = {Receiver1};
BeginMissionSequence;
