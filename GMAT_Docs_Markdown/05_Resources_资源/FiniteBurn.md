# FiniteBurn

> 来源：第 608-617 页

FiniteBurn
FiniteBurn — A finite burn
Description
The FiniteBurn resource is used when continuous propulsion is desired.
Impulsive burns happen instantaneously through the use of the Maneuver
command, while finite burns occur continuously starting at the BeginFiniteBurn
command and lasting until the EndFiniteBurn command is reached in the
mission sequence. In order to apply a non-zero Finite Burn, there must be a
Propagate command between the BeginFiniteBurn and EndFiniteBurn
commands.
See Also: ChemicalTank, ChemicalThruster, Spacecraft, BeginFiniteBurn,
EndFiniteBurn, Calculation Parameters
Fields
Field Description
Thrusters
The Thruster field allows the selection of which
Thruster, from a list of previously created thrusters, to use
when applying a finite burn. Currently, using the GUI, you
can only select one Thruster to attach to a FiniteBurn
resource. Using the scripting interface, you may attach
multiple thrusters to a FiniteBurn resource. Using the
scripting interface, you may attach multiple thrusters to a
FiniteBurn resource. In a script command, an empty list,
e.g., FiniteBurn1.Thruster={}, is allowed but is of
limited utility since the GUI will automatically associate a
ChemicalThruster, if one has been created, with the
FiniteBurn. This field cannot be modified in the Mission
Sequence.
Data Reference Array
Type
Allowed A list of Thrusters created by user. Can be a
Values list of ChemicalThrusters or
ElectricThrusters but you cannot mix
chemical and electric thrusters.
Access set
Default No Default
Value
Units N/A
Interfaces GUI, script, or only one
VectorFormat
Deprecated. Allows you to define the format of the finite
burn thrust direction. This field has no affect. The finite
burn thrust direction, as specified in the Thruster resource,
is always given in Cartesian format. Note: You can use
GMAT scripting to covert from other representations to
Cartesian and then set the Cartesian format.
Data Type Enumeration
Allowed Values Cartesian, Spherical
Access set
Default Value Cartesian
Units N/A
Interfaces script
GUI
The FiniteBurn dialog box allows you to specify which thruster to use for the
finite burn. The layout of the FiniteBurn dialog box is shown below.
Remarks
Configuring a FiniteBurn
To perform a finite burn, the FiniteBurn resource itself and a number of related
resources and commands must be properly configured. You must associate a
specific ChemicalThruster hardware resource with a created FiniteBurn. You
must associate a specific ChemicalTank hardware resource with the chosen
ChemicalThruster. Finally, you must attach both the chosen Thrusters and
Tanks to the desired Spacecraft. See the example below for additional details.
FiniteBurn Using Multiple Thrusters
Using the GUI, a FiniteBurn resource must be associated with exactly one
Thruster.
Using the scripting interface, one can assign multiple thrusters to a single
FiniteBurn resource.
Interactions
Field Description
Spacecraft
resource Must be created in order to apply any burn.
Thruster
resource As discussed in the Remarks, every FiniteBurn resource
must be associated with at least one ChemicalThruster or
ElectricThruster. Any thruster created in the resource tree
can be incorporated into a FiniteBurn but thruster types
cannot be mixed.
ChemicalTank
resource To perform a finite burn, a Tank must be attached to the
Spacecraft. (A ChemicalTank is needed to provide
pressure and temperature data used when modeling the
thrust and specific impulse. A Tank is also needed if you
want to model mass depletion.)
BeginFiniteBurn
and After a FiniteBurn is created, to apply it in the mission
EndFiniteBurn sequence, a BeginFiniteBurn and EndFiniteBurn
command command must be appended to the mission tree.
Propagate
command In order to apply a non-zero finite burn, there must be a
Propagate command between the BeginFiniteBurn and
EndFiniteBurn commands.
Reporting FiniteBurn Parameters
GMAT now supports finite burn parameters that report the thrust component data
for a finite burn. The parameters include total thrust from all thrusters in the
three coordinate directions, the total acceleration from all thrusters in the three
coordinate directions, and the total mass flow rate from all thrusters. Currently,
by default the total thrust and total acceleration parameters in the three
coordinate directions are reported only in the J2000 system and do not support
any other coordinate system dependency. Furthermore, you can now also report
out any thruster's individual parameters such as thrust magnitude, Isp and mass
flow rate. See the Calculation Parameters reference for definitions of these finite
burn and thruster specific parameters. Also see the Examples section for an
example that shows how to report the finite burn and individual thruster specific
parameters to a report file.
Examples
Configure a chemical finite burn. Create a default Spacecraft and
ChemicalTank Resource; Create a default ChemicalThruster that allows for
fuel depletion from the default ChemicalTank; Attach ChemicalTank and
ChemicalThruster to the Spacecraft; Create default ForceModel and
Propagator; Create a Finite Burn that uses the default thruster and apply a 30
minute finite burn to the spacecraft.
% Create a default Spacecraft and ChemicalTank Resource
Create Spacecraft DefaultSC
Create ChemicalTank FuelTank1
% Create a default ChemicalThruster. Allow for fuel depletion from
% the default ChemicalTank.
Create ChemicalThruster Thruster1
Thruster1.DecrementMass = true
Thruster1.Tank = {FuelTank1}
% Attach ChemicalTank and ChemicalThruster to the spacecraft
DefaultSC.Thrusters = {Thruster1}
DefaultSC.Tanks = {FuelTank1}
% Create default ForceModel and Propagator
Create ForceModel DefaultProp_ForceModel
Create Propagator DefaultProp
DefaultProp.FM = DefaultProp_ForceModel
% Create a Finite Burn that uses the default thruster
Create FiniteBurn FiniteBurn1
FiniteBurn1.Thrusters = {Thruster1}
BeginMissionSequence
% Implement 30 minute finite burn
BeginFiniteBurn FiniteBurn1(DefaultSC)
Propagate DefaultProp(DefaultSC) {DefaultSC.ElapsedSecs = 1800}
EndFiniteBurn FiniteBurn1(DefaultSC)
This example shows how to report finite burn parameters such as total
acceleration (from all thrusters), total thrust (from all thrusters) in the three
coordinate directions. We also report total mass flow rate from all thrusters.
Additionally, individual thruster specific parameters such as thruster mass flow
rate, thrust magnitude and thruster Isp are also reported. Note that in the
generated report, all finite burn and thruster parameters are reported as zeros
when thrusters are not turned on.
Create Spacecraft aSat
Create ChemicalTank aFuelTank
Create ChemicalThruster aThruster
aThruster.DecrementMass = true
aThruster.Tank = {aFuelTank}
aThruster.C1 = 1000 % Constant Thrust
aThruster.K1 = 300 % Constant Isp
aSat.Thrusters = {aThruster}
aSat.Tanks = {aFuelTank}
Create ForceModel aFM
aFM.CentralBody = Earth
aFM.PointMasses = {Earth}
Create Propagator aProp
aProp.FM = aFM
Create FiniteBurn aFB
aFB.Thrusters = {aThruster}
Create ReportFile rf
rf.Add = {aSat.UTCGregorian, aFB.TotalAcceleration1, aFB.TotalAcceleration2, ...
aFB.TotalAcceleration3, aFB.TotalMassFlowRate, aFB.TotalThrust1, ...
aFB.TotalThrust2, aFB.TotalThrust3, aSat.aThruster.MassFlowRate, ...
aSat.aThruster.ThrustMagnitude, aSat.aThruster.Isp}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 1000}
% Do a Finite-Burn for 1800 Secs
BeginFiniteBurn aFB(aSat)
Propagate aProp(aSat) {aSat.ElapsedSecs = 1800}
EndFiniteBurn aFB(aSat)
Propagate aProp(aSat) {aSat.ElapsedSecs = 1000}
