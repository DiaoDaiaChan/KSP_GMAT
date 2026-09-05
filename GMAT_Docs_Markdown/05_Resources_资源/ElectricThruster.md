# ElectricThruster

> 来源：第 511-539 页

ElectricThruster
ElectricThruster — An electric thruster model
Description
The ElectricThruster resource is a model of an electric thruster which supports
several models for thrust and mass flow computation. The ElecticThruster
model also allows you to specify properties such as a duty cycle and scale factor
and to connect an ElectricThruster with an ElectricTank. You can flexibly
define the direction of the thrust by specifying the thrust components in
coordinate systems such as (locally defined) SpacecraftBody or LVLH, or by
choosing any configured CoordinateSystem resource.
For a complete descripton of how to configure all Resources required for electric
propulsion modelling, see the Tutorial named Chapter 12, Electric Propulsion
See Also ElectricTank, NuclearPowerSystem, SolarPowerSystem
Fields
Field Description
Axes
Allows the user to define a spacecraft centered set of
axes for the ElectricThruster. This field cannot be
modified in the Mission Sequence
Data Type Reference Array
Allowed VNB, LVLH, MJ2000Eq,
Values SpacecraftBody
Access set
Default VNB
Value
Units N/A
Interfaces GUI, script
ConstantThrust
Thrust value used ThrustModel is set to
ConstantThrustAndIsp.
Data Type Real
Allowed Values Real > 0
Access set, get
Default Value 0.237
Units N
Interfaces GUI, script
CoordinateSystem
Determines what coordinate system the orientation
parameters, ThrustDirection1, ThrustDirection2, and
ThrustDirection3 refer to. This field cannot be
modified in the Mission Sequence.
Data Reference Array
Type
Allowed Local, EarthMJ2000Eq,
Values EarthMJ2000Ec, EarthFixed, or any
user defined system
Access set
Default Local
Value
Units N/A
Interfaces GUI, script
DecrementMass
Flag which determines if the FuelMass is to be
decremented as it used. This field cannot be modified
in the Mission Sequence.
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
FixedEfficiency
Thruster efficiency. Only used when ThrustModel is
FixedEfficiency.
Data Type Real
Allowed Values Real > 0
Access set, get
Default Value 0.7
Units Decimal Percent
Interfaces GUI, script
GravitationalAccel
Value of the gravitational acceleration used for the
FuelTank/Thruster calculations.
Data Type Real Number
Allowed Values Real > 0
Access set, get
Default Value 9.81
Units m/s2
Interfaces GUI, script
Isp
Thruster specific impulse. Only used when
ThrustModel is set to FixedEfficiency or
ConstantThrustAndIsp.
Data Type Real
Allowed Values Real > 0
Access set, get
Default Value 4200
Units seconds
Interfaces GUI, script
MassFlowCoeff1
Mass flow coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value -0.004776
Units See Mathematical Models
Interfaces GUI, script
MassFlowCoeff2
Mass flow coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0.05717
Units See Mathematical Models
Interfaces GUI, script
MassFlowCoeff3
Mass flow coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value -0.09956
Units See Mathematical Models
Interfaces GUI, script
MassFlowCoeff4
Mass flow coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 0.03211
Units See Mathematical Models
Interfaces GUI, script
MassFlowCoeff5
Mass flow coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 2.13781
Units See Mathematical Models
Interfaces GUI, script
MaximumUsablePower
The maximum power the thruster can use to generate
thrust. Power provided above MaximumUsablePower
is not used in the thrust model.
Data Type Real
Allowed Real > 0, Real <
Values MinimumUsablePower
Access set, get
Default Value 7.266
Units kW
Interfaces GUI, script
MinimumUsablePower
The minimum power the thruster can use to generate
thrust. If power provided to thruster is below
MinimumUsablePower, no thrust is generated.
Data Type Real
Allowed Real > 0, Real >
Values MinimumUsablePower
Access set, get
Default Value 0.638
Units kW
Interfaces GUI, script
MixRatio
The mixture ratio employed to draw fuel from multiple
tanks. For example, if there are two tanks and
MixRatio is set to [2 1], then twice as much fuel will
be drawn from tank one as from tank 2 in the Tank list.
Note, if a MixRatio is not supplied, fuel is drawn from
tanks in equal amounts, (the MixRatio is set to a
vector of ones the same length as the Tank list).
Data Array
Type
Allowed Array of real numbers with same length
Values as number of tanks in the Tank array
Access set
Default [1]
Value
Units N/A
Interfaces GUI, script
Origin
This field, used in conjunction with the Axes field,
allows the user to define a spacecraft centered set of
axes for the ElectricThruster. Origin has no affect
when a Local coordinate system is used and the Axes
are set to MJ2000Eq or SpacecraftBody. This field
cannot be modified in the Mission Sequence.
Data Reference Array
Type
Allowed Sun, Mercury, Venus, Earth, Luna,
Values Mars,Jupiter, Saturn, Uranus,
Neptune, Pluto
Access set
Default Earth
Value
Units N/A
Interfaces GUI, script
Tanks
ElectricTank from which the ElectricThruster draws
propellant from. In a script command, an empty list,
e.g., Thruster1.Tank = {}, is NOT allowed. Via the
script, if you wish to indicate that no ElectricTank is
associated with an ElectricThruster, do not include
commands such as Thruster1.Tank = ... in your
script. This field cannot be modified in the Mission
Sequence.
Data Type Reference Array
Allowed Values User defined list of FuelTank(s).
Access set
Default Value N/A
Units N/A
Interfaces GUI, script
ThrustCoeff1
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value -5.19082
Units See Mathematical Models
Interfaces GUI, script
ThrustCoeff2
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 2.96519
Units See Mathematical Models
Interfaces GUI, script
ThrustCoeff3
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value -14.41789
Units See Mathematical Models
Interfaces GUI, script
ThrustCoeff4
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value 54.05382
Units See Mathematical Models
Interfaces GUI, script
ThrustCoeff5
Thrust coefficient.
Data Type Real
Allowed Values Real Number
Access set, get
Default Value -0.00100092
Units See Mathematical Models
Interfaces GUI, script
ThrustDirection1
X component of the spacecraft thrust vector direction.
Data Type Real
Allowed Values Real Number
Access set, get
1
Default Value
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
ThrustModel
The type of thruster model. See Mathematical Models
for a detailed description of the options.
Data String
Type
Allowed ThrustMassPolynomial,
Values ConstantThrustAndIsp,FixedEfficiency
Access set, get
Default ThrustMassPolynomial
Value
Units N/A
Interfaces GUI, script
ThrustScaleFactor
ThrustScaleFactor is a scale factor that is multiplied
by the thrust vector, for a given thruster, before the
thrust vector is added into the total acceleration. Note
that the value of this scale factor does not affect the
mass flow rate.
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
ElectricTank resource
This resource contains the fuel used to
power the ElectricThruster specified by
the FiniteBurn resource.
FiniteBurn resource
When using the
BeginFiniteBurn/EndFiniteBurn
commands, you must specify which
FiniteBurn resource to implement. The
FiniteBurn resource specifies which
ElectricThruster(s) to use for the finite
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
The ElectricThruster dialog box allows you to specify properties of an
ElectricThruster including the Coordinate System of the thrust acceleration
direction vector, the thrust magnitude and Isp coefficients, and choice of
ElectricTank. The layout of the ElectricThruster dialog box is shown below.
When configuring the Coordinate System field, you can choose between
existing coordinate systems or use locally defined coordinate systems. The Axes
field is only active if Coordinate System is set to Local. The Origin field is
only active if Coordinate System is set to Local and Axes is set to either VNB
or LVLH.
Selecting the Configure Polynomials button brings up the following dialog box
where you may input the coefficients for the ElectricThruster polynomial.
Similarly, clicking the Configure Polynomials also allows you to edit mass flow
coefficients as shown below.
Remarks
Mathematical Models
The ElectricThruster model supports several models for computation of thrust
and and mass flow rate and the model used is set by the ThrustModel field.
When ThrustModel is set to ThrustMassPolynomial, the following
polynomials are used to compute thrust and mass flow rate
where P is the power provided to the thruster which is computed using the power
logic defined on the FiniteBurn resource, f_d is duty cycle, f_s is thrust scale
factor, R_iT is the rotation matrix from the thrust coordinate system to the
inertial system, and T_hat is the thrust unit vector. By industry convention, the
mass flow rate and thrust polynomial equations are in mg/s and milli-Newtons
respectively. GMAT internally converts the units to be consistent with the
equations of motion.
When ThrustModel is set to ConstantThrustAndIsp, the following
polynomials are used to compute thrust and mass flow rate
where C_t1 is set using the ConstantThrust field, Isp is set using the Isp field,
f_d is duty cycle, f_s is thrust scale factor, R_iT is the rotation matrix from the
thrust coordinate system to the inertial system, and T_hat is the thrust unit
vector. Note, by industry convention, the mass flow rate and thrust polynomial
equations are in mg/s and milli-Newtons respectively. GMAT internally converts
the units to be consistent with the equations of motion.
When ThrustModel is set to FixedEfficiency, the following polynomials are
used to compute thrust and mass flow rate
where P is the power provided to the thruster which is computed from the power
logic defined on the FiniteBurn Resource. "Eta" is the FixedEfficiency setting,
f_d is duty cycle, f_s is thrust scale factor, R_iT is the rotation matrix from the
thrust coordinate system to the inertial system, and T_hat is the thrust unit
vector.
Use of Thruster Resource in Conjunction With Maneuvers
An ElectricThruster resource is used only in association with finite maneuvers.
To implement a finite maneuver, you must first create both an ElectricTank and
a FiniteBurn resource. You must also associate an ElectricTank with the
ElectricThruster resource and you must associate an ElectricThruster with the
FiniteBurn resource. The actual finite maneuver is implemented using the
BeginFiniteBurn/EndFiniteBurn commands.
For a complete descripton of how to configure all Resources required for electric
propulsion modelling, see the Tutorial named Chapter 12, Electric Propulsion
Local Coordinate Systems
Here, a Local coordinate system is defined as one that we configure "locally"
using the ElectricThruster resource interface as opposed to defining a
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
Caution Regarding Force Model Discontinuties
Note that when modellign shadows on a SolarPowerSystem Resource, it is
possible that there is not enough power available to power an ElectricThruster.
This occurs when the power available from the SolarPowerSystem, or the
power distributed to the thruster, is less than MinimumUsablePower. When this
occurs, the thruster model turns off thrust and this can cause a discontinuity in
the force model. To avoid this, you must propagate to the boundary and switch
propagators, or configure the Propagator to continue propagating if a poor step
occurs.
Examples
Create a default ElectricTank and an ElectricThruster that allows for fuel
depletion, assign the ElectricThruster the default ElectricTank, and attach both
to a Spacecraft.
% Create an ElectricTank Resource
Create ElectricTank anElectricTank
% Create an Electric Thruster Resource
Create ElectricThruster anElectricThruster
anElectricThruster.CoordinateSystem = Local
anElectricThruster.Origin = Earth
anElectricThruster.Axes = VNB
anElectricThruster.ThrustDirection1 = 1
anElectricThruster.ThrustDirection2 = 0
anElectricThruster.ThrustDirection3 = 0
anElectricThruster.DutyCycle = 1
anElectricThruster.ThrustScaleFactor = 1
anElectricThruster.DecrementMass = true
anElectricThruster.Tank = {anElectricTank}
anElectricThruster.GravitationalAccel = 9.810000000000001
anElectricThruster.ThrustModel = ThrustMassPolynomial
anElectricThruster.MaximumUsablePower = 7.266
anElectricThruster.MinimumUsablePower = 0.638
anElectricThruster.ThrustCoeff1 = -5.19082
anElectricThruster.ThrustCoeff2 = 2.96519
anElectricThruster.ThrustCoeff3 = -14.4789
anElectricThruster.ThrustCoeff4 = 54.05382
anElectricThruster.ThrustCoeff5 = -0.00100092
anElectricThruster.MassFlowCoeff1 = -0.004776
anElectricThruster.MassFlowCoeff2 = 0.05717
anElectricThruster.MassFlowCoeff3 = -0.09956
anElectricThruster.MassFlowCoeff4 = 0.03211
anElectricThruster.MassFlowCoeff5 = 2.13781
anElectricThruster.FixedEfficiency = 0.7
anElectricThruster.Isp = 4200
anElectricThruster.ConstantThrust = 0.237
% Create a SolarPowerSystem Resource
Create SolarPowerSystem aSolarPowerSystem
% Create a Spacecraft Resource and attach hardware
Create Spacecraft DefaultSC
DefaultSC.Tanks = {anElectricTank}
DefaultSC.Thrusters = {anElectricThruster}
DefaultSC.PowerSystem = aSolarPowerSystem
BeginMissionSequence
