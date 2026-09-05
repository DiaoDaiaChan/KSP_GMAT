# ChemicalThruster

> 来源：第 1218-1256 页

ChemicalThruster
ChemicalThruster — A chemical thruster model
Description
The ChemicalThruster resource is a model of a chemical thruster which uses
polynomials to model the thrust and specific impulse as a function of tank
pressure and temperature. The ChemicalThruster model also allows you to
specify properties such as a duty cycle and scale factor and to connect a
ChemicalThruster with a ChemicalTank. You can flexibly define the direction
of the thrust by specifying the thrust components in coordinate systems such as
(locally defined) SpacecraftBody or LVLH, or by choosing any configured
CoordinateSystem resource.
See Also: BeginFiniteBurn,ChemicalTank,FiniteBurn
Fields
The constants Ci below are used in the following equation to calculate thrust (in
Newtons), F , as a function of pressure P (kPa) and temperature T (Celsius).
T
The constants Ki below are used in the following equation to calculate ISP (in
seconds), Isp, as a function of pressure P (kPa) and temperature T (Celsius).
Field Description
Axes
Allows the user to define a spacecraft centered set of axes
for the ChemicalThruster. This field cannot be modified
in the Mission Sequence
Data Type Reference Array
Allowed VNB, LVLH, MJ2000Eq,
Values SpacecraftBody
Access set
Default Value VNB
Units N/A
Interfaces GUI, script
CoordinateSystem
Determines what coordinate system the orientation
parameters, ThrustDirection1, ThrustDirection2, and
ThrustDirection3 refer to. This field cannot be modified
in the Mission Sequence.
Data Reference Array
Type
Allowed Local, EarthMJ2000Eq, EarthMJ2000Ec,
Values EarthFixed, or any user defined system
Access set
Default Local
Value
Units N/A
Interfaces GUI, script
C1
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 10
Units N
Interfaces GUI, script
C2
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/kPa
Interfaces GUI, script
C3 Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N
Interfaces GUI, script
C4
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/kPa
Interfaces GUI, script
C5
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/kPa2
Interfaces GUI, script
C6
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/kPaC7
Interfaces GUI, script
C7
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
C8
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/kPaC9
Interfaces GUI, script
C9
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
C10
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/kPaC11
Interfaces GUI, script
C11
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
C12
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N
Interfaces GUI, script
C13
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
C14
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units 1/kPa
Interfaces GUI, script
C15
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
C16
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units 1/kPa
Interfaces GUI, script
DecrementMass
Flag which determines if the FuelMass is to be
decremented as it used. This field cannot be modified in
the Mission Sequence.
Data Type Boolean
Allowed Values true, false
Access set
Default Value false
Units N/A
Interfaces GUI, script
DutyCycle
Fraction of time that the thrusters are on during a
maneuver. The thrust applied to the spacecraft is scaled
by this amount. Note that this scale factor also affects
mass flow rate.
Data Type Real Number
Allowed Values 0 <= Real <= 1
Access set, get
Default Value 1
Units N/A
Interfaces GUI, script
GravitationalAccel
The gravitational acceleration.
Data Type Real Number
Allowed Values Real > 0
Access set, get
Default Value 9.81
Units m/s2
Interfaces GUI, script
K1
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 300
Units s
Interfaces GUI, script
K2
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s/kPa
Interfaces GUI, script
K3
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s
Interfaces GUI, script
K4
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s/kPa
Interfaces GUI, script
K5
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s/kPa2
Interfaces GUI, script
K6
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s/kPaC7
Interfaces GUI, script
K7
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
K8
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s/kPaC9
Interfaces GUI, script
K9
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
K10
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s/kPaC11
Interfaces GUI, script
K11
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
K12
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units s
Interfaces GUI, script
K13
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
K14
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units 1/kPa
Interfaces GUI, script
K15
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units None
Interfaces GUI, script
K16
ISP coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units 1/kPa
Interfaces GUI, script
MixRatio
The mixture ratio employed to draw fuel from multiple
tanks. For example, if there are two tanks and MixRatio
is set to [2 1], then twice as much fuel will be drawn from
tank one as from tank 2 in the Tank list. Note, if a
MixRatio is not supplied, fuel is drawn from tanks in
equal amounts, (the MixRatio is set to a vector of ones
the same length as the Tank list).
Data Array
Type
Allowed Array of real numbers with same length as
Values number of tanks in the Tank array
Access set
Default [1]
Value
Units N/A
Interfaces GUI, script
Origin
This field, used in conjunction with the Axes field, allows
the user to define a spacecraft centered set of axes for the
ChemicalThruster. Origin has no affect when a Local
coordinate system is used and the Axes are set to
MJ2000Eq or SpacecraftBody. This field cannot be
modified in the Mission Sequence.
Data Reference Array
Type
Allowed Sun, Mercury, Venus, Earth, Luna,
Values Mars,Jupiter, Saturn, Uranus, Neptune,
Pluto
Access set
Default Earth
Value
Units N/A
Interfaces GUI, script
Tanks
A list of ChemicalTank(s) from which the thruster draws
propellant from. In the script, an empty list, e.g.,
Thruster1.Tank = {}, is NOT allowed. Via the script, if
you wish to indicate that no tank is associated with a
thruster, do not include commands such as
Thruster1.Tank = ... in your script. This field cannot
be modified in the Mission Sequence.
Data Type Reference Array
Allowed Values User defined list of tank(s).
Access set
Default Value N/A
Units N/A
Interfaces GUI, script
ThrustDirection1
X component of the spacecraft thrust vector direction.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 1
Units N/A
Interfaces GUI, script
ThrustDirection2
Y component of the spacecraft thrust vector direction.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 1
Units N/A
Interfaces GUI, script
ThrustDirection3
Z component of the spacecraft thrust vector direction.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0
Units N/A
Interfaces GUI, script
ThrustScaleFactor
ThrustScaleFactor is a scale factor that is multiplied by
the thrust vector, for a given thruster, before the thrust
vector is added into the total acceleration. Note that the
value of this scale factor does not affect the mass flow
rate.
Data Type Real Number
Allowed Values Real >= 0
Access set, get
Default Value 1
Units N/A
Interfaces GUI, script
Interactions
Command or Resource Description
BeginFiniteBurn/EndFiniteBurn
command Use these commands, which require a
Spacecraft and a FiniteBurn name as
input, to implement a finite burn.
ChemicalTank resource
This resource contains the fuel used to
power the ChemicalThruster specified by
the FiniteBurn resource.
FiniteBurn resource
When using the
BeginFiniteBurn/EndFiniteBurn
commands, you must specify which
FiniteBurn resource to implement. The
FiniteBurn resource specifies which
ChemicalThruster(s) to use for the finite
burn.
Spacecraft resource
When using the
BeginFiniteBurn/EndFiniteBurn
commands, you must specify which
Spacecraft to apply the finite burn to.
Propagate command
In order to implement a non-zero finite
burn, a Propagate statement must occurr
within the BeginFiniteBurn and
EndFiniteBurn statements.
GUI
The ChemicalThruster dialog box allows you to specify properties of a
ChemicalThruster including the Coordinate System of the thrust acceleration
direction vector, the thrust magnitude and Isp coefficients, and choice of
ChemicalTank. The layout of the ChemicalThruster dialog box is shown
below.
When configuring the Coordinate System field, you can choose between
existing coordinate systems or use locally defined coordinate systems. The Axes
field is only active if Coordinate System is set to Local. The Origin field is
only active if Coordinate System is set to Local and Axes is set to either VNB
or LVLH.
As shown below, if Decrement Mass is checked, then you can input the
gravitational acceleration value used to calculate fuel use. The value of the
gravitational acceleration input here only affects fuel use and does not affect the
force model.
Selecting the Edit Thruster Coef. button brings up the following dialog box
where you may input the coefficients for the ChemicalThruster polynomial.
Similarly, clicking the Edit Impulse Coef. button brings up the following dialog
box where you may input the coefficients for the specific impulse (ISP)
polynomial.

Remarks
Use of ChemicalThruster Resource in Conjunction With
Maneuvers
A ChemicalThruster resource is used only in association with finite maneuvers.
To implement a finite maneuver, you must first create both a ChemicalTank and
a FiniteBurn resource. You must also associate a ChemicalTank with the
ChemicalThruster resource and you must associate a ChemicalThruster with
the FiniteBurn resource. The finite maneuver is implemented using the
BeginFiniteBurn/EndFiniteBurn commands. See the
BeginFiniteBurn/EndFiniteBurn command documentation for worked
examples on how the ChemicalThruster resource is used in conjunction with
finite maneuvers.
Thrust and ISP Calculation
Unscaled thrust, F , and Isp, as a function of Pressure, in kPa, and Temperature,
T
in degrees Celsius, are calculated using the following polynomials.
The thrust, T, output in Newtons, is scaled by the Duty Cycle and Thrust Scale
Factor. The thrust acceleration direction vector (the direction of the actual
acceleration not the thruster nozzle) is given by ThrustDirection1-3 and is
applied in the input Coordinate System. The Isp is output in seconds.
The mass flow rate and the thrust equations are shown below where F and Isp
T
are defined above, f is the duty cycle, f is the thrust scale factor, R is the
d s iT
rotation matrix from the thrust coordinate system to the inertial system, and T is
d
the unitized thrust direction.
Local Coordinate Systems
Here, a Local coordinate system is defined as one that we configure "locally"
using the ChemicalThruster resource interface as opposed to defining a
coordinate system using the Coordinate Systems folder in the Resources Tree.
To configure a local coordinate system, you must specify the coordinate system
of the input thrust acceleration direction vector, ThrustDirection1-3. If you
choose a local coordinate system, the four choices available, as given by the
Axes sub-field, are VNB, LVLH, MJ2000Eq, and SpacecraftBody. VNB or
Velocity-Normal-Binormal is a non-inertial coordinate system based upon the
motion of the spacecraft with respect to the Origin sub-field. For example, if the
Origin is chosen as Earth, then the X-axis of this coordinate system is the along
the velocity of the spacecraft with respect to the Earth, the Y-axis is along the
instantaneous orbit normal (with respect to the Earth) of the spacecraft, and the
Z-axis completes the right-handed set.
Similarly, Local Vertical Local Horizontal or LVLH is also a non-inertial
coordinate system based upon the motion of the spacecraft with respect to the
Origin sub-field. Again, if we choose Earth as the origin, then the X-axis of this
coordinate system is the position of the spacecraft with respect to the Earth, the
Z-axis is the instantaneous orbit normal (with respect to the Earth) of the
spacecraft, and the Y-axis completes the right-handed set.
MJ2000Eq is the J2000-based Earth-centered Earth mean equator inertial
coordinate system. Note that the Origin sub-field is not needed to define this
coordinate system.
SpacecraftBody is the attitude system of the spacecraft. Since the thrust is
applied in this system, GMAT uses the attitude of the spacecraft, a spacecraft
attribute, to determine the inertial thrust direction. Note that the Origin sub-field
is not needed to define this coordinate system.
Caution When Setting the ChemicalTank Temperature and
Reference Temperature
Note that both the thrust and ISP polynomials have terms that involve the ratio,
(Temperature / Reference Temperature). For GMAT, this temperature ratio is
calculated in Celsius units, and thus, there is a discontinuity when the Reference
Temperature is equal to zero. For this reason, GMAT requires that the absolute
value of the input Reference Temperature is greater than 0.01.
Note also that the form of the Thrust and ISP polynomial has some behavior,
when the Reference Temperature is near 0 degrees Centigrade, that you need to
be aware of. Because of the previously mentioned discontinuity, the polynomials
do not vary smoothly when the Reference Temperature is near zero. For
example, consider the two Reference Temperatures, -0.011 and + 0.011 degrees
Centigrade. These two temperatures are close to each other in value and one
might expect that they have roughly similar thrust and ISP values. This may not
be the case, depending upon your choice of thrust/ISP coefficients, since the
temperature ratios associated with the two Reference Temperatures have the
same magnitude but different signs. You may choose to set the input Reference
Temperature equal to the input Temperature, thus eliminating any dependence of
thrust and ISP with temperature when using the currently implemented
ChemicalTank model based upon Boyle’s Law where the fuel Temperature does
not change as fuel is depleted.
Examples
Create a default ChemicalTank and a ChemicalThruster that allows for fuel
depletion, assign the ChemicalThruster the default ChemicalTank, and attach
both the ChemicalThruster and ChemicalTank to a Spacecraft.
% Create the ChemicalTank Resource
Create ChemicalTank FuelTank1
FuelTank1.AllowNegativeFuelMass = false
FuelTank1.FuelMass = 756
FuelTank1.Pressure = 1500
FuelTank1.Temperature = 20
FuelTank1.RefTemperature = 20
FuelTank1.Volume = 0.75
FuelTank1.FuelDensity = 1260
FuelTank1.PressureModel = PressureRegulated
% Create a ChemicalThruster, that allows fuel depletion, and assign it a ChemicalTank
Create ChemicalThruster Thruster1
Thruster1.CoordinateSystem = Local
Thruster1.Origin = Earth
Thruster1.Axes = VNB
Thruster1.ThrustDirection1 = 1
Thruster1.ThrustDirection2 = 0
Thruster1.ThrustDirection3 = 0
Thruster1.DutyCycle = 1
Thruster1.ThrustScaleFactor = 1
Thruster1.DecrementMass = true
Thruster1.Tank = {FuelTank1}
Thruster1.GravitationalAccel = 9.810000000000001
Thruster1.C1 = 10
Thruster1.C2 = 0
Thruster1.C3 = 0
Thruster1.C4 = 0
Thruster1.C5 = 0
Thruster1.C6 = 0
Thruster1.C7 = 0
Thruster1.C8 = 0
Thruster1.C9 = 0
Thruster1.C10 = 0
Thruster1.C11 = 0
Thruster1.C12 = 0
Thruster1.C13 = 0
Thruster1.C14 = 0
Thruster1.C15 = 0
Thruster1.C16 = 0
Thruster1.K1 = 300
Thruster1.K2 = 0
Thruster1.K3 = 0
Thruster1.K4 = 0
Thruster1.K5 = 0
Thruster1.K6 = 0
Thruster1.K7 = 0
Thruster1.K8 = 0
Thruster1.K9 = 0
Thruster1.K10 = 0
Thruster1.K11 = 0
Thruster1.K12 = 0
Thruster1.K13 = 0
Thruster1.K14 = 0
Thruster1.K15 = 0
Thruster1.K16 = 0
% Add the ChemicalThruster and the ChemicalTank to a Spacecraft
Create Spacecraft DefaultSC
DefaultSC.Tanks = {FuelTank1}
DefaultSC.Thrusters = {Thruster1}
BeginMissionSequence
Create two ChemicalTanks (called aTank1 and aTank2) and a
ChemicalThruster, attach both the ChemicalThruster and ChemicalTanks to
a Spacecraft, and configure the thruster to draw four times as much fuel from
aTank1 than aTank2.
% Create the ChemicalTank Resource
Create Spacecraft aSat
aSat.Tanks = {aTank1,aTank2}
aSat.Thrusters = {aThruster}
% Create two tanks
Create ChemicalTank aTank1 aTank2
% Configure thruster to draw four times as much fuel
% from aTank1 than aTank2
Create ChemicalThruster aThruster
aThruster.Tank = {aTank1,aTank2}
aThruster.MixRatio = [4 1]
BeginMissionSequence
