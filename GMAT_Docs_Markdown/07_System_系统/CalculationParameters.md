# Calculation Parameters

> 来源：第 1613-1711 页

Calculation Parameters
Calculation Parameters — Resource properties available for use by commands
and output
Description
Parameters are named resource properties that can be used to obtain data for use
by Mission Sequence commands or by output resources. Some parameters, such
as the Altitude parameter of Spacecraft, are calculated values that can only be
used to retrieve data. They cannot be set directly. Others, such as the Element1
parameter of ImpulsiveBurn, share the same name as a resource field and can
be used both to set data and retrieve it. Parameters are distinguished from
resource fields by their extra functionality: fields are static resource properties
that are usually set in initialization (or in the GUI Resources tree), while
parameters can be calculated on the fly and used in plots, reports, and
mathematical expressions.
Parameters are classified as one of four types: central-body-dependent
parameters, coordinate-system-dependent parameters, attached-hardware
parameters, and standalone parameters. Standalone parameters are the simplest
type, as they have no dependencies. The ElapsedSecs parameter of Spacecraft
is an example of this; it is simply referenced as Spacecraft.ElapsedSecs.
Central-body-dependent parameters, as the name suggests, have a value that is
dependent on the chosen celestial body. The Altitude parameter of Spacecraft is
an example of this. To reference this parameter, you must specify a central body,
such as Spacecraft.Mars.Altitude. Any built-in central body or user-defined
Asteroid, Comet, Moon, or Planet is valid as a dependency.
Likewise, coordinate-system-dependent parameters have a value that is
dependent on the chosen coordinate system. The DEC parameter of Spacecraft
is an example of this. To reference this parameter, you must specify the name of
a CoordinateSystem resource, such as Spacecraft.EarthFixed.DEC. Any
default or user-defined CoordinateSystem resource is valid as a dependency.
If a dependency is used when retrieving the value of the parameter, as in the
following line, the value of Altitude is calculated at Mars before setting it to the
variable x. If the dependency is omitted, Earth and EarthMJ2000Eq are
assumed unless noted otherwise.
x = DefaultSC.Mars.Altitude
If a dependency is used when setting the value of a parameter, the value of the
parameter is first converted based on the value of the dependency, then the value
is set. For example, in the following line, the value of SMA is first calculated at
Mars, then it is set to the value 10000 in that context. If the dependency is
omitted when setting the value, the default is assumed to be the central body or
coordinate system of the parent resource (in this case, DefaultSC).
DefaultSC.Mars.SMA = 10000
Attached-hardware parameters have no dependencies, but are themselves
dependent on being attached to a Spacecraft. ChemicalTank and
ChemicalThruster parameters are examples of this. The FuelMass parameter
of ChemicalTank cannot be referenced without first attaching the
ChemicalTank to a Spacecraft. Then, the parameter can be referenced as:
Spacecraft.FuelTank.FuelMass.
The individual parameters are resource-specific, and are documented in the
tables below. The GUI has a parameter selection interface that is common to all
parameters. This interface is documented in GUI, below.
See Also: Array, ChemicalTank, ImpulsiveBurn, FiniteBurn, Spacecraft, String,
ChemicalThruster, Variable
GUI
Parameters can be used as input in several places throughout GMAT, such as the
ReportFile and XYPlot resources and the If/Else, Propagate, and Report
commands. In the GUI, all of these use a common interface called the
ParameterSelectDialog that allows for interactive parameter selection. A basic
ParameterSelectDialog window looks like the following:
The ParameterSelectDialog window is used to build a parameter, along with
any dependencies, for use in a command or resource. Some resources and
commands have different requirements for the types of parameters that can be
used, so the ParameterSelectDialog can take slightly different forms, depending
on where it's used. This section will describe the generic interface, then mention
any resource- or command-specific exceptions.
General Usage
The first step in choosing a parameter is to select the object (or resource) type
from the Object Type list in the upper left. Seven types can appear in this list:
Spacecraft, SpacePoint, ImpulsiveBurn, FiniteBurn, Variable, Array, and
String.
Once you've selected a type, The Object List box is populated with all existing
resources of that type. Use this list to choose the specific resource you'd like to
reference.
If the Spacecraft type is selected, the Attached Hardware List appears below
the Object List. This list displays any hardware (such as ChemicalTank or
ChemicalThruster resources) attached to the selected Spacecraft. If the Array
type is selected, Row and Col boxes appear. Use these to specify a row and
column to select an individual array element, or check Select Entire Object to
choose the entire array.
Once a resource is selected, the Object Properties list is populated with all
available parameters provided by that resource. Some resources, such as
instances of Variable or Array, are themselves parameters, so this list remains
empty.
Parameters with different dependency types are commingled in the Object
Properties list. When you select one, the appropriate dependency (if any)
appears below the list. For example, after selecting the Spacecraft AOP
parameter, a CoordinateSystem list appears. After selecting the Spacecraft
Apoapsis parameter, a Central Body list appears. And after selecting the
Spacecraft Cd parameter, no dependency list appears. To select a range of
parameters from the Object Properties list, hold down the Shift key while
selecting the second endpoint of the range. To select multiple individual
parameters, hold down the Ctrl key while making each selection.
To select a parameter, select the appropriate Object Type, the specific resource
from the Object List or Attached Hardware List, the desired parameter from
the Object Properties list, and the required dependency, and add it to the
Selected Value(s) list on the right. There are six buttons available to control this
list:
UP: Move the selected item in the Selected Value(s) list up one position (if
allowed).
DN: Move the selected item in the Selected Value(s) list down one position
(if allowed).
->: Add the selected item in the Object Properties list to the Selected
Value(s) list.
<-: Remove the selected item in the Selected Value(s) list.
=>: Add all items to the Selected Value(s) list.
<=: Remove all items from the Selected Value(s) list.
When finished, the Selected Value(s) list contains the final selected parameters.
Click OK to accept the selection.
The ordering of the Selected Value(s) list is significant in certain circumstances
(such as in the Add field of ReportFile), but not in others. See the
documentation for each resource or command for details.
Special Considerations
Some resources and commands (such as the Propagate command Parameter
argument) only accept a single parameter as input; in this context the
ParameterSelectDialog only allows one parameter in the Selected Value(s) list
and does not allow use of the UP, DN, and => buttons.
In some instances (such as in the Vary command), only parameters that are also
fields (and so can be set in the Mission Sequence) can be used. In this case only
the allowed parameters will be shown in the Object Properties list.
In the Propagate command Parameter argument, only parameters of
Spacecraft can be used. In this case only Spacecraft will be shown in the
Object Type list.
Parameters
Spacecraft
Parameter Settable Plottable Description
A1Gregorian Y N
Spacecraft epoch in the A.1 system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
A1ModJulian Y Y
Spacecraft epoch in the A.1 system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
Acceleration N Y
The total acceleration with respect to
the inertial system computed using
the ForceModel selected for the
dependency.
Data Type Real
Dependency ForceModel
Units km/s^2
AccelerationX N Y
The x-component of acceleration with
respect to the inertial system
computed using the ForceModel
selected for the dependency.
Data Type Real
Dependency ForceModel
Units km/s^2
AccelerationY N Y
The y-component of acceleration with
respect to the inertial system
computed using the ForceModel
selected for the dependency.
Data Type Real
Dependency ForceModel
Units km/s^2
AccelerationZ N Y
The z-component of acceleration with
respect to the inertial system
computed using the ForceModel
selected for the dependency.
Data Type String
Dependency ForceModel
Units km/s^2
AltEquinoctialP Y Y
See Spacecraft.AltEquinoctialP
Data Type Real
Dependency CoordinateSystem
Units (None)
AltEquinoctialQ Y Y
See Spacecraft.AltEquinoctialQ
Data Type Real
Dependency CoordinateSystem
Units (None)
Altitude N Y
Distance to the plane tangent to the
surface of the specified celestial body
at the sub-satellite point. GMAT
assumes the body is an ellipsoid.
Data Type Real
Dependency CelestialBody
Units km
AngularVelocityX Y Y
See Spacecraft.AngularVelocityX
Data Type Real
Dependency (None)
Units deg/s
AngularVelocityY Y Y
See Spacecraft.AngularVelocityY
Data Type Real
Dependency (None)
Units deg/s
AngularVelocityZ Y Y
See Spacecraft.AngularVelocityZ
Data Type Real
Dependency (None)
Units deg/s
AOP Y Y
See Spacecraft.AOP
Data Type Real
Dependency CoordinateSystem
Output Range 0° ≤ AOP < 360°
Units deg
Apoapsis N Y
A parameter that equals zero when
the spacecraft is at orbit apoapsis.
This parameter can only be used as a
stopping condition in the Propagate
command.
Data Type Real
Dependency CelestialBody
Units (None)
AtmosDensity N Y
The atmospheric density at the
current Spacecraft epoch and
location computed using the
ForceModel selected for the
dependency.
Data Type String
Dependency ForceModel
Units kg/km^3
AZI Y Y
See Spacecraft.AZI
Data Type Real
Dependency CoordinateSystem
Output Range -180° ≤ AZI ≤ 180°
Units deg
BdotR N Y
B-plane B·R magnitude.
GMAT computes the B-plane
coordinates in the coordinate system
specified in the dependency. In many
implementations, the B-plane
coordinates are computed in a
pseudo-rotating coordinate system
where the ω×r term is not applied
when transforming velocity vectors.
GMAT does apply the ω×r term in the
velocity transformation. When
computing B-plane coordinates in
inertial systems, this term is
identically zero. For rotating systems
such as the Sun-Earth body-body
rotating system, the effect of
including ω×r is small but noticeable
when comparing results between
systems. When the rotation of the
selected coordinate system is "fast",
the values may differ significantly.
Data Type Real
Dependency CoordinateSystem
Units km
BdotT N Y
B-plane B·T magnitude. See the
BdotR parameter for notes on this
calculation.
Data Type Real
Dependency CoordinateSystem
Units km
BetaAngle N Y
Beta angle (or phase angle) between
the orbit normal vector and the vector
from the celestial body to the sun.
Data Type Real
Dependency CelestialBody
Output -90° ≤ BetaAngle ≤
Range 90°
Units deg
BrouwerLongAOP Y Y
See Spacecraft.BrouwerLongAOP
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerLongAOP ≤
360°
Units deg
BrouwerLongECC Y Y
See Spacecraft.BrouwerLongECC
Data Type Real
Dependency CoordinateSystem
Units (None)
BrouwerLongINC Y Y
See Spacecraft.BrouwerLongINC.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerLongINC ≤
180°
Units deg
BrouwerLongMA Y Y
See Spacecraft.BrouwerLongMA.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerLongMA ≤
360°
Units deg
BrouwerLongRAAN Y Y
See
Spacecraft.BrouwerLongRAAN.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerLongRAAN
≤ 360°
Units deg
BrouwerLongSMA Y Y
See Spacecraft.BrouwerLongSMA
Data Type Real
Dependency CoordinateSystem
Units km
BrouwerShortAOP Y Y
See Spacecraft.BrouwerShortAOP
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerShortAOP
≤ 360°
Units deg
BrouwerShortECC Y Y
See Spacecraft.BrouwerShortECC
Data Type Real
Dependency CoordinateSystem
Units (None)
BrouwerShortINC Y Y
See Spacecraft.BrouwerShortINC
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerShortINC ≤
180°
Units deg
BrouwerShortMA Y Y
See Spacecraft.BrouwerShortMA.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerShortMA ≤
360°
Units deg
BrouwerShortRAAN Y Y
See
Spacecraft.BrouwerShortRAAN.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤
Range BrouwerShortRAAN
≤ 360°
Units deg
BrouwerShortSMA Y Y
See Spacecraft.BrouwerShortSMA
Data Type Real
Dependency CoordinateSystem
Units km
BVectorAngle N Y
B-plane angle between the B vector
and the T unit vector. See the BdotR
parameter for notes on this
calculation.
Data Type Real
Dependency CoordinateSystem
Output -180° ≤
Range BVectorAngle ≤ 180°
Units deg
BVectorMag N Y
B-plane B vector magnitude. See the
BdotR parameter for notes on this
calculation.
Data Type Real
Dependency CoordinateSystem
Units km
C3Energy N Y
C (characteristic) energy.
3
Data Type Real
Dependency CelestialBody
Units MJ/kg (km2/s2)
Cd Y Y
See Spacecraft.Cd
Data Type Real
Dependency (None)
Units (None)
Cr Y Y
See Spacecraft.Cr
Data Type Real
Dependency (None)
Units (None)
CurrA1MJD Y Y
Deprecated. Spacecraft epoch in the
A.1 system and the Modified Julian
format.
Data Type Real
Dependency (None)
Units d
DCM11 Y Y
See Spacecraft.DCM11
Data Type Real
Dependency (None)
Units (None)
DCM12 Y Y
See Spacecraft.DCM12
Data Type Real
Dependency (None)
Units (None)
DCM13 Y Y
See Spacecraft.DCM13
Data Type Real
Dependency (None)
Units (None)
DCM21 Y Y
See Spacecraft.DCM21
Data Type Real
Dependency (None)
Units (None)
DCM22 Y Y
See Spacecraft.DCM22
Data Type Real
Dependency (None)
Units (None)
DCM23 Y Y
See Spacecraft.DCM23
Data Type Real
Dependency (None)
Units (None)
DCM31 Y Y
See Spacecraft.DCM31
Data Type Real
Dependency (None)
Units (None)
DCM32 Y Y
See Spacecraft.DCM32
Data Type Real
Dependency (None)
Units (None)
DCM33 Y Y
See Spacecraft.DCM33
Data Type Real
Dependency (None)
Units (None)
DEC Y Y
See Spacecraft.DEC
Data Type Real
Dependency CoordinateSystem
Output Range -90° ≤ DEC ≤ 90°
Units deg
DECV Y Y
See Spacecraft.DECV
Data Type Real
Dependency CoordinateSystem
Output Range -90° ≤ DECV ≤ 90°
Units deg
Delaunayg Y Y
See Spacecraft.Delaunayg.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ Delaunayg <
Range 360°
Units deg
DelaunayG Y Y
See Spacecraft.DelaunayG.
Data Type Real
Dependency CoordinateSystem
Units km2/s
Delaunayh Y Y
See Spacecraft.Delaunayh.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ Delaunayh <
Range 360°
Units deg
DelaunayH Y Y
See Spacecraft.DelaunayH.
Data Type Real
Dependency CoordinateSystem
Units km2/s
Delaunayl Y Y
See Spacecraft.Delaunayl.
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ Delaunayl <
Range 360°
Units deg
DelaunayL Y Y
See Spacecraft.DelaunayL.
Data Type Real
Dependency CoordinateSystem
Units km2/s
DLA N Y
Declination of the outgoing
hyperbolic asymptote.
Data Type Real
Dependency CoordinateSystem
Output Range -90° ≤ DLA ≤ 90°
Units deg
DragArea Y Y
See Spacecraft.DragArea
Data Type Real
Dependency (None)
Units m2
DryMass Y Y
See Spacecraft.DryMass
Data Type Real
Dependency (None)
Units kg
EA N Y
Eccentric anomaly.
Data Type Real
Dependency CelestialBody
Output Range 0° ≤ EA < 360°
Units deg
ECC Y Y
See Spacecraft.ECC
Data Type Real
Dependency CelestialBody
Output Range
Units (None)
ElapsedDays N Y
See Spacecraft.ElapsedDays
Data Type Real
Dependency (None)
Units d
ElapsedSecs N Y
See Spacecraft.ElapsedSecs
Data Type Real
Dependency (None)
Units s
Energy N Y
Specific orbital energy.
Data Type Real
Dependency CelestialBody
Units MJ/kg (km2/s2)
EquinoctialH Y Y
See Spacecraft.EquinoctialH
Data Type Real
Dependency CoordinateSystem
Units (None)
EquinoctialK Y Y
See Spacecraft.EquinoctialK
Data Type Real
Dependency CoordinateSystem
Units (None)
EquinoctialP Y Y
See Spacecraft.EquinoctialP
Data Type Real
Dependency CoordinateSystem
Units (None)
EquinoctialQ Y Y
See Spacecraft.EquinoctialQ
Data Type Real
Dependency CoordinateSystem
Units (None)
EulerAngle1 Y Y
See Spacecraft.EulerAngle1
Data Type Real
Dependency (None)
Output 0° ≤ EulerAngle1 <
Range 360°
Units deg
EulerAngle2 Y Y See Spacecraft.EulerAngle2
Data Type Real
Dependency (None)
Output 0° ≤ EulerAngle2 <
Range 360°
Units deg
EulerAngle3 Y Y
See Spacecraft.EulerAngle3
Data Type Real
Dependency (None)
Output 0° ≤ EulerAngle3 <
Range 360°
Units deg
EulerAngleRate1 Y Y
See Spacecraft.EulerAngleRate1
Data Type Real
Dependency (None)
Units deg/s
EulerAngleRate2 Y Y
See Spacecraft.EulerAngleRate2
Data Type Real
Dependency (None)
Units deg/s
EulerAngleRate3 Y Y
See Spacecraft.EulerAngleRate3
Data Type Real
Dependency (None)
Units deg/s
FPA Y Y
See Spacecraft.FPA
Data Type Real
Dependency CoordinateSystem
Output Range 0° ≤ FPA ≤ 180°
Units deg
HA N Y
Hyperbolic anomaly.
Data Type Real
Dependency CelestialBody
Output Range -∞ < HA < ∞
Units deg
HMAG N Y
Magnitude of the angular momentum
vector.
Data Type Real
Dependency CelestialBody
Units km2/s
HX N Y
X component of the angular
momentum vector.
Data Type Real
Dependency CoordinateSystem
Units km2/s
HY N Y
Y component of the angular
momentum vector.
Data Type Real
Dependency CoordinateSystem
Units km2/s
HZ N Y
Z component of the angular
momentum vector.
Data Type Real
Dependency CoordinateSystem
Units km2/s
INC Y Y
See Spacecraft.INC
Data Type Real
Dependency CoordinateSystem
Output Range 0° ≤ INC ≤ 180°
Units deg
IncomingBVAZI Y Y
See Spacecraft.IncomingBVAZI
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ IncomingBVAZI
Range < 360°
Units deg
IncomingC3Energy Y Y
See Spacecraft.IncomingC3Energy
Data Type Real
Dependency CelestialBody
Units MJ/kg (km2/s2)
IncomingDHA Y Y
See Spacecraft.IncomingDHA
Data Type Real
Dependency CoordinateSystem
Output -90° ≤
Range IncomingDHA ≤ 90°
Units deg
IncomingRadPer Y Y
See Spacecraft.IncomingRadPer
Data Type Real
Dependency CelestialBody
Units km
IncomingRHA Y Y See Spacecraft.IncomingRHA
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ IncomingRHA <
Range 360°
Units deg
Latitude N Y
Planetodetic latitude.
Data Type Real
Dependency CelestialBody
Output -90° ≤ Latitude ≤
Range 90°
Units deg
Longitude N Y
Planetodetic longitude.
Data Type Real
Dependency CelestialBody
Output -180° ≤ Longitude ≤
Range 180°
Units deg
LST N Y
Local sidereal time of the spacecraft
from the celestial body's inertial x-
axis.
Data Type Real
Dependency CelestialBody
Output Range 0° ≤ LST < 360°
Units deg
MA N Y
Mean anomaly.
Data Type Real
Dependency CelestialBody
Output Range 0° ≤ MA < 360°
Units deg
MHA N Y
Angle between celestial body's body-
fixed and inertial axes. For Earth, this
is the Greenwich Hour Angle.
Data Type Real
Dependency CelestialBody
Output Range 0° ≤ MHA < 360°
Units deg
MLONG Y Y
See Spacecraft.MLONG
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ MLONG <
Range 360°
Units deg
MM N Y
Mean motion.
Data Type Real
Dependency CelestialBody
Output Range
Units rad/s
ModEquinoctialF Y Y
See Spacecraft.ModEquinoctialF
Data Type Real
Dependency CoordinateSystem
Units (None)
ModEquinoctialG Y Y
See Spacecraft.ModEquinoctialG
Data Type Real
Dependency CoordinateSystem
Units (None)
ModEquinoctialH Y Y
See Spacecraft.ModEquinoctialH
Data Type Real
Dependency CoordinateSystem
Units (None)
ModEquinoctialK Y Y
See Spacecraft.ModEquinoctialK
Data Type Real
Dependency CoordinateSystem
Units (None)
MRP1 Y Y
See Spacecraft.MRP1
Data Type Real
Dependency (None)
Units (None)
MRP2 Y Y
See Spacecraft.MRP2
Data Type Real
Dependency (None)
Units (None)
MRP3 Y Y
See Spacecraft.MRP3
Data Type Real
Dependency (None)
Units (None)
OrbitPeriod N Y
Osculating orbit period.
Data Type Real
Dependency CelestialBody
Units s
OrbitSTM N N
State transition matrix with respect to
the origin-independent MJ2000Eq
axes.
Data Type Array (6×6)
Dependency (None)
Units (None)
OrbitSTMA N N
Upper-left quadrant of the state
transition matrix, with respect to the
origin-independent MJ2000Eq axes.
Data Type Array (3×3)
Dependency (None)
Units (None)
OrbitSTMB N N
Upper-right quadrant of the state
transition matrix, with respect to the
origin-independent MJ2000Eq axes.
Data Type Array (3×3)
Dependency (None)
Units (None)
OrbitSTMC N N
Lower-left quadrant of the state
transition matrix, with respect to the
origin-independent MJ2000Eq axes.
Data Type Array (3×3)
Dependency (None)
Units (None)
OrbitSTMD N N
Lower-right quadrant of the state
transition matrix, with respect to the
origin-independent MJ2000Eq axes.
Data Type Array (3×3)
Dependency (None)
Units (None)
OutgoingBVAZI Y Y
See Spacecraft.OutgoingBVAZI
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ OutgoingBVAZI
Range < 360°
Units deg
OutgoingC3Energy Y Y
See Spacecraft.OutgoingC3Energy
Data Type Real
Dependency CelestialBody
Units MJ/kg (km2/s2)
OutgoingDHA Y Y
See Spacecraft.OutgoingDHA
Data Type Real
Dependency CoordinateSystem
Output -90° ≤ OutgoingRHA
Range ≤ 90°
Units deg
OutgoingRadPer Y Y
See Spacecraft.OutgoingRadPer
Data Type Real
Dependency CelestialBody
Units km
OutgoingRHA Y Y
See Spacecraft.OutgoingRHA
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ OutgoingRHA <
Range 360°
Units deg
Periapsis N Y A parameter that equals zero when
the spacecraft is at orbit periapsis.
This parameter can only be used as a
stopping condition in the Propagate
command.
Data Type Real
Dependency CelestialBody
Units (None)
PlanetodeticAZI Y Y
See Spacecraft.PlanetodeticAZI.
This parameter must be used with a
CoordinateSystem with BodyFixed
axes.
Data Type Real
Dependency CoordinateSystem
(with BodyFixed
axes)
Output -180° ≤
Range PlanetodeticAZI ≤
180°
Units deg
PlanetodeticHFPA Y Y
See Spacecraft.PlanetodeticHFPA
This parameter must be used with a
CoordinateSystem with BodyFixed
axes.
Data Type Real
Dependency CoordinateSystem
(with BodyFixed
axes)
Output -90° ≤
Range PlanetodeticHFPA ≤
90°
Units deg
PlanetodeticLAT Y Y
See Spacecraft.PlanetodeticLAT.
This parameter must be used with a
CoordinateSystem with BodyFixed
axes.
Data Type Real
Dependency CoordinateSystem
(with BodyFixed
axes)
Output -180° ≤
Range PlanetodeticLAT ≤
180°
Units deg
PlanetodeticLON Y Y
See Spacecraft.PlanetodeticLON.
This parameter must be used with a
CoordinateSystem with BodyFixed
axes.
Data Type Real
Dependency CoordinateSystem
(with BodyFixed
axes)
Output -180° ≤
Range PlanetodeticLON ≤
180°
Units deg
PlanetodeticRMAG Y Y
See Spacecraft.PlanetodeticRMAG
This parameter must be used with a
CoordinateSystem with BodyFixed
axes.
Data Type Real
Dependency CoordinateSystem
(with BodyFixed
axes)
Units km
PlanetodeticVMAG Y Y
See Spacecraft.PlanetodeticVMAG
This parameter must be used with a
CoordinateSystem with BodyFixed
axes.
Data Type Real
Dependency CoordinateSystem
(with BodyFixed
axes)
Units km/s
Q1 N Y
See Spacecraft.Q1
Data Type Real
Dependency (None)
Units (None)
Q2 N Y
See Spacecraft.Q2
Data Type Real
Dependency (None)
Units (None)
Q3 N Y
See Spacecraft.Q3
Data Type Real
Dependency (None)
Units (None)
Q4 N Y
See Spacecraft.Q4
Data Type Real
Dependency (None)
Units (None)
Quaternion Y N Attitude quaternion.
Data Type Array (1×4)
Dependency (None)
Units (None)
RA Y Y
See Spacecraft.RA
Data Type Real
Dependency CoordinateSystem
Output Range -180° ≤ RA ≤ 180°
Units deg
RAAN Y Y
See Spacecraft.RAAN
Data Type Real
Dependency CoordinateSystem
Output Range 0° ≤ RAAN < 360°
Units deg
RadApo Y Y
See Spacecraft.RadApo
Data Type Real
Dependency CelestialBody
Units km
RadPer Y Y
See Spacecraft.RadPer
Data Type Real
Dependency CelestialBody
Units km
RAV Y Y
See Spacecraft.RAV
Data Type Real
Dependency CoordinateSystem
Output -180° ≤ RAV ≤
Range 180°
Units deg
RLA N Y
Right ascension of the outgoing
hyperbolic asymptote.
Data Type Real
Dependency CoordinateSystem
Output -180° ≤ RLA ≤
Range 180°
Units deg
RMAG Y Y
See Spacecraft.RMAG
Data Type Real
Dependency CelestialBody
Units km
SemilatusRectum Y Y
See Spacecraft.SemilatusRectum
Data Type Real
Dependency CelestialBody
Units km
SemilatusRectum N Y
Semilatus rectum of the osculating
orbit.
Data Type Real
Dependency CelestialBody
Units km
SMA Y Y
See Spacecraft.SMA
Data Type Real
Dependency CelestialBody
Units km
SRPArea Y Y
See Spacecraft.SRPArea
Data Type Real
Dependency (None)
Units m2
TA Y Y
See Spacecraft.TA.
Data Type Real
Dependency CelestialBody
Output Range 0° ≤ TA < 360°
Units deg
TAIGregorian Y N
Spacecraft epoch in the TAI system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
TAIModJulian Y Y
Spacecraft epoch in the TAI system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
TDBGregorian Y N
Spacecraft epoch in the TDB system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
TDBModJulian Y Y
Spacecraft epoch in the TDB system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
TLONG Y Y
See Spacecraft.TLONG
Data Type Real
Dependency CoordinateSystem
Output 0° ≤ TLONG <
Range 360°
Units deg
TotalMass N Y
Total mass, including fuel mass from
attached ChemicalTank resources.
Data Type Real
Dependency (None)
Units kg
TTGregorian Y N Spacecraft epoch in the TT system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
TTModJulian Y Y
Spacecraft epoch in the TT system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
UTCGregorian Y N
Spacecraft epoch in the UTC system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
UTCModJulian Y Y
Spacecraft epoch in the UTC system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
VelApoapsis N Y
Scalar velocity at apoapsis.
Data Type Real
Dependency CelestialBody
Units km/s
VelPeriapsis N Y
Scalar velocity at periapsis.
Data Type Real
Dependency CelestialBody
Units km/s
VMAG Y Y
See Spacecraft.VMAG
Data Type Real
Dependency CoordinateSystem
Output Range
Units km/s
VX Y Y
See Spacecraft.VX
Data Type Real
Dependency CoordinateSystem
Units km/s
VY Y Y
See Spacecraft.VY
Data Type Real
Dependency CoordinateSystem
Units km/s
VZ Y Y
See Spacecraft.VZ
Data Type Real
Dependency CoordinateSystem
Units km/s
X Y Y
See Spacecraft.X
Data Type Real
Dependency CoordinateSystem
Units km
Y Y Y
See Spacecraft.Y
Data Type Real
Dependency CoordinateSystem
Units km
Z Y Y
See Spacecraft.Z
Data Type Real
Dependency CoordinateSystem
Units km
FuelTank
ChemicalTank parameters are accessible only after attaching the
ChemicalTank resource to a Spacecraft, like so:
Create FuelTank aTank
Create Spacecraft aSat
aSat.Tanks = {aTank}
Then, ChemicalTank parameters are accessible by specifying the
ChemicalTank name as the parameter dependency:
Create ReportFile aReport
aReport.Add = {aSat.aTank.FuelMass}
Parameter Settable Plottable Description
FuelDensity Y Y
See ChemicalTank.FuelDensity
Data Type Real
Dependency (None)
Units kg/m3
FuelMass Y Y
See ChemicalTank.FuelMass
Data Type Real
Dependency (None)
Units kg
Pressure Y Y
See ChemicalTank.Pressure
Data Type Real
Dependency (None)
Units kPa
RefTemperature Y Y
See ChemicalTank.RefTemperature
Data Type Real
Dependency (None)
Units °C
Temperature Y Y
See ChemicalTank.Temperature
Data Type Real
Dependency (None)
Units °C
Volume Y Y
See ChemicalTank.Volume
Data Type Real
Dependency (None)
Units m3
Space Point Parameters
All Resources that have coordinates in space have Cartesian position and
velocity parameters, so you can access ephemeris information. This includes all
built-in solar system bodies and other Resources such as CelestialBody,Planet,
Moon, Asteroid, Comet, Barycenter, LibrationPoint, and GroundStation :
CelestialBody.CoordinateSystem.X
CelestialBody.CoordinateSystem.Y
CelestialBody.CoordinateSystem.Z
CelestialBody.CoordinateSystem.VX
CelestialBody.CoordinateSystem.VY
CelestialBody.CoordinateSystem.VZ
Warning
Note that to use these parameters, you must first set the epoch
of the Resource to the desired epoch at which you want the
data. Additionally, the epoch should be set after the
BeginMissionSequence Command. See the following
example.
Create ReportFile rf
BeginMissionSequence
Luna.Epoch.A1ModJulian = 21545
Report rf Luna.EarthMJ2000Eq.X Luna.EarthMJ2000Eq.Y Luna.EarthMJ2000Eq.Z ...
Luna.EarthMJ2000Eq.VX Luna.EarthMJ2000Eq.VY Luna.EarthMJ2000Eq.VZ
Note
Spacecraft parameters are treated slightly different than Space
Point parameters primarly because Spacecraft Cartesian state
parameters are settable, and all other Space Point Cartesian
parameters are only gettable. When requesting state
information for Space Points other than Spacecraft, the
coordinates are computed based on the model configured for
that Resource. Additionally, not all epoch configuration options
supported for Spacecraft are supported for Space Points (i.e.
Epoch and DateFormat).
Parameter Settable Plottable Description
A1Gregorian Y N
Resource epoch in the A.1 system and
the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
A1ModJulian Y Y
Resource epoch in the A.1 system and
the Modified Julian format.
Data Type Real
Dependency (None)
Units d
TAIGregorian Y N
Resource epoch in the TAI system and
the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
TAIModJulian Y Y
Resource epoch in the TAI system and
the Modified Julian format.
Data Type Real
Dependency (None)
Units d
TDBGregorian Y N
Resource epoch in the TDB system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
TDBModJulian Y Y
Resource epoch in the TDB system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
TTGregorian Y N
Resource epoch in the TT system and
the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
TTModJulian Y Y
Resource epoch in the TT system and
the Modified Julian format.
Data Type Real
Dependency (None)
Units d
UTCGregorian Y N
Resource epoch in the UTC system
and the Gregorian format.
Data Type String
Dependency (None)
Units (N/A)
UTCModJulian Y Y
Resource epoch in the UTC system
and the Modified Julian format.
Data Type Real
Dependency (None)
Units d
VX N Y
The x-component of velocity with
respect to the CoordinateSystem
chosen as the dependency. When no
dependency is selected,
EarthMJ2000Eq is used.
Data Type Real
Dependency CoordinateSystem
Units km/s
VY N Y
The y-component of velocity with
respect to the CoordinateSystem
chosen as the dependency. When no
dependency is selected,
EarthMJ2000Eq is used.
Data Type Real
Dependency CoordinateSystem
Units km/s
VZ N Y
The z-component of velocity with
respect to the CoordinateSystem
chosen as the dependency. When no
dependency is selected,
EarthMJ2000Eq is used.
Data Type Real
Dependency CoordinateSystem
Units km/s
X N Y
The x-component of position with
respect to the CoordinateSystem
chosen as the dependency. When no
dependency is selected,
EarthMJ2000Eq is used.
Data Type Real
Dependency CoordinateSystem
Units km
Y N Y
The y-component of position with
respect to the CoordinateSystem
chosen as the dependency. When no
dependency is selected,
EarthMJ2000Eq is used.
Data Type Real
Dependency CoordinateSystem
Units km
Z N Y
The z-component of position with
respect to the CoordinateSystem
chosen as the dependency. When no
dependency is selected,
EarthMJ2000Eq is used.
Data Type Real
Dependency CoordinateSystem
Units km
Thruster
ChemicalThruster parameters are accessible only after attaching the
ChemicalThruster resource to a Spacecraft, like so:
Create Thruster aThruster
Create Spacecraft aSat
aSat.Thrusters = {aThruster}
Then, ChemicalThruster parameters are accessible by specifying the
ChemicalThruster name as the parameter dependency:
Create ReportFile aReport
aReport.Add = {aSat.aThruster.DutyCycle}
The table below shows reportable thruster based parameters:
Parameter Settable Plottable Description
C1 Y Y
See ChemicalThruster.C1
Data Type Real
Dependency (None)
Units N
C2 Y Y
See ChemicalThruster.C2
Data Type Real
Dependency (None)
Units N/kPa
C3 Y Y
See ChemicalThruster.C3
Data Type Real
Dependency (None)
Units N
C4 Y Y
See ChemicalThruster.C4
Data Type Real
Dependency (None)
Units N/kPa
C5 Y Y
See ChemicalThruster.C5
Data Type Real
Dependency (None)
Units N/kPa2
C6 Y Y
See ChemicalThruster.C6
Data Type Real
Dependency (None)
Units N/kPaC7
C7 Y Y
See ChemicalThruster.C7
Data Type Real
Dependency (None)
Units (None)
C8 Y Y
See ChemicalThruster.C8
Data Type Real
Dependency (None)
Units N/kPaC9
C9 Y Y
See ChemicalThruster.C9
Data Type Real
Dependency (None)
Units (None)
C10 Y Y
See ChemicalThruster.C10
Data Type Real
Dependency (None)
Units N/kPaC11
C11 Y Y
See ChemicalThruster.C11
Data Type Real
Dependency (None)
Units (None)
C12 Y Y
See ChemicalThruster.C12
Data Type Real
Dependency (None)
Units N
C13 Y Y
See ChemicalThruster.C13
Data Type Real
Dependency (None)
Units (None)
C14 Y Y
See ChemicalThruster.C14
Data Type Real
Dependency (None)
Units 1/kPa
C15 Y Y
See ChemicalThruster.C15
Data Type Real
Dependency (None)
Units (None)
C16 Y Y
See ChemicalThruster.C16
Data Type Real
Dependency (None)
Units 1/kPa
DutyCycle Y Y
See ChemicalThruster.DutyCycle
Data Type Real
Dependency (None)
Units (None)
GravitationalAccel Y Y
See
ChemicalThruster.GravitationalAccel
Data Type Real
Dependency (None)
Units m/s2
Isp Y Y
Specific impulse of an individual
thruster. When thruster(s) is not turned
on, GMAT will report zeros to a report
file.
Data Type Real
Dependency (None)
Units s
K1 Y Y
See ChemicalThruster.K1
Data Type Real
Dependency (None)
Units s
K2 Y Y
See ChemicalThruster.K2
Data Type Real
Dependency (None)
Units s/kPa
K3 Y Y
See ChemicalThruster.K3
Data Type Real
Dependency (None)
Units s
K4 Y Y
See ChemicalThruster.K4
Data Type Real
Dependency (None)
Units s/kPa
K5 Y Y
See ChemicalThruster.K5
Data Type Real
Dependency (None)
Units s/kPa2
K6 Y Y
See ChemicalThruster.K6
Data Type Real
Dependency (None)
Units s/kPaC7
K7 Y Y
See ChemicalThruster.K7
Data Type Real
Dependency (None)
Units (None)
K8 Y Y
See ChemicalThruster.K8
Data Type Real
Dependency (None)
Units s/kPaC9
K9 Y Y
See ChemicalThruster.K9
Data Type Real
Dependency (None)
Units (None)
K10 Y Y
See ChemicalThruster.K10
Data Type Real
Dependency (None)
Units s/kPaC11
K11 Y Y
See ChemicalThruster.K11
Data Type Real
Dependency (None)
Units (None)
K12 Y Y
See ChemicalThruster.K12
Data Type Real
Dependency (None)
Units s
K13 Y Y
See ChemicalThruster.K13
Data Type Real
Dependency (None)
Units (None)
K14 Y Y
See ChemicalThruster.K14
Data Type Real
Dependency (None)
Units 1/kPa
K15 Y Y
See ChemicalThruster.K15
Data Type Real
Dependency (None)
Units (None)
K16 Y Y
See ChemicalThruster.K16
Data Type Real
Dependency (None)
Units 1/kPa
MassFlowRate N Y
Mass flow rate from an individual
thruster. When thruster(s) is not turned
on, GMAT will report zeros to a report
file.
Data Type Real
Dependency (None)
Units kg/s
ThrustDirection1 Y Y
See
ChemicalThruster.ThrustDirection1
Data Type Real
Dependency (None)
Units (None)
ThrustDirection2 Y Y
See
ChemicalThruster.ThrustDirection2
Data Type Real
Dependency (None)
Units (None)
ThrustDirection3 Y Y
See
ChemicalThruster.ThrustDirection3
Data Type Real
Dependency (None)
Units (None)
ThrustMagnitude Y Y
Magnitude of the thrust from an
individual thruster. When thruster(s) is
not turned on, GMAT will report zeros
to a report file.
Data Type Real
Dependency (None)
Units Newtons
ThrustScaleFactor Y Y
See
ChemicalThruster.ThrustScaleFactor
Data Type Real
Dependency (None)
Units (None)
ImpulsiveBurn
To compute ImpulsiveBurn parameters, GMAT requires that an ImpulsiveBurn
has been executed using a Maneuver command like this:
Maneuver myImpulsiveBurn(mySat)
In the case that an ImpulsiveBurn has not been applied, GMAT will output
zeros for the ImpulsiveBurn components and issue a warning.
We recommended that you evaluate ImpulsiveBurn parameters immediately
after the ImpulsiveBurn is applied using the Maneuver command like this:
Maneuver myImpulsiveBurn(mySat)
myVar = mySat.MyCoordinateSystem.Element1
The above usage avoids issues that may occur if the ImpulsiveBurn coordinate
system is time varying, and the ImpulsiveBurn parameters are requested after
further manipulation of the participants using other commands (such as
Propagate). In that case, it is possible that the participants are no longer at the
epoch of the maneuver, and unexpected results can occur due to epoch
mismatches.
Parameter Settable Plottable Description
B Y Y
See ImpulsiveBurn.B
Data Type Real
Dependency (None)
Units (None)
Element1 Y Y
See ImpulsiveBurn.Element1
Data Type Real
Dependency CoordinateSystem
Units (None)
Element2 Y Y See ImpulsiveBurn.Element2
Data Type Real
Dependency CoordinateSystem
Units (None)
Element3 Y Y
See ImpulsiveBurn.Element3
Data Type Real
Dependency CoordinateSystem
Units (None)
N Y Y
See ImpulsiveBurn.N
Data Type Real
Dependency (None)
Units (None)
V Y Y
See ImpulsiveBurn.V
Data Type Real
Dependency (None)
Units (None)
FiniteBurn
To compute FiniteBurn parameters, GMAT requires that a FiniteBurn has been
executed using a BeginFiniteBurn command like this:
BeginFiniteBurn Maneuver myFiniteBurn(mySat)
In the case that a FiniteBurn has not been applied, GMAT will output zeros for
all reportable FiniteBurn parameters to a report file. All finite burn parameters
will report zeros whenever a finite burn is not turned on. The table below shows
reportable finite burn parameters:
Parameter Settable Plottable Description
TotalAcceleration1 N Y
First component of the total
acceleration from all thrusters in the
three coordinate directions of a
J2000 system. Zero is reported
whenever thruster is not turned on
Data Type Real
Dependency (None)
Units Km/s2
TotalAcceleration2 N Y
Second component of the total
acceleration from all thrusters in the
three coordinate directions of a
J2000 system. Zero is reported
whenever thruster is not turned on
Data Type Real
Dependency (None)
Units Km/s2
TotalAcceleration3 N Y
Third component of the total
acceleration from all thrusters in the
three coordinate directions of a
J2000 system. Zero is reported
whenever thruster is not turned on
Data Type Real
Dependency (None)
Units Km/s2
TotalMassFlowRate N Y
Total mass flow rate from all
thrusters. Zero is reported whenever
thruster is not turned on
Data Type Real
Dependency (None)
Units Kg/s
TotalThrust1 N Y
First component of the total thrust
from all thrusters in the three
coordinate directions of a J2000
system. Zero is reported whenever
thruster is not turned on
Data Type Real
Dependency (None)
Units Newtons
TotalThrust2 N Y
Second component of the total
thrust from all thrusters in the three
coordinate directions of a J2000
system. Zero is reported whenever
thruster is not turned on
Data Type Real
Dependency (None)
Units Newtons
TotalThrust3 N Y
Third component of the total thrust
from all thrusters in the three
coordinate directions of a J2000
system. Zero is reported whenever
thruster is not turned on
Data Type Real
Dependency (None)
Units Newtons
Solver
Solver parameters allow you to query a Solver for its convergence state to
determine if the Solver converged. There are both string and numeric parameters
which are described in further detail in the table below the following usage
example using solver parameters before and after a Target sequence.
Create Spacecraft aSat
Create Propagator aPropagator
Create ImpulsiveBurn aBurn
Create DifferentialCorrector aDC
Create OrbitView EarthView
EarthView.Add = {Earth,aSat}
EarthView.ViewScaleFactor = 5
Create ReportFile aReport
BeginMissionSequence
Report aReport aDC.SolverStatus aDC.SolverState
Target aDC
Vary aDC(aBurn.Element1 = 1.0, {Upper = 3})
Maneuver aBurn(aSat)
Propagate aPropagator(aSat,{aSat.Apoapsis})
Achieve aDC(aSat.RMAG = 42164)
EndTarget
Report aReport aDC.SolverStatus aDC.SolverState
Parameter Settable Plottable Description
SolverStatus N N
The SolverStatus parameter contains
the state of a Solver. If the Solver has
not executed, SolverStatus is
Initialized. If the Solver has executed
and converged, SolverStatus is
Converged. If the Solver is iterating,
SolverStatus is Running. If the
Solver has executed and reached the
maximum number of iterations before
convergence, SolverStatus is
ExceededIterations. If the Solver has
executed and failed to converge, but
did not exceed the maximum
iterations, SolverStatus is
DidNotConverge.
Data Type String
Dependency (None)
Units (None)
SolverState N Y
The SolverState parameter contains
the state of a Solver. If the solver has
not executed, SolverState is 0. If the
Solver has executed and converged,
SolverState is 1. If the Solver is
iterating, SolverState is 0. If the
Solver has executed and reached the
maximum number of iterations before
convergence, SolverState is -1. If the
Solver has executed and failed to
converge, but did not exceed the
maximum iterations, SolverState is -2.
Data Type Integer
Dependency (None)
Units (None)
Array, String, Variable
Array, String, and Variable resources are themselves parameters, and can be
used as any other parameter would. All of these are writable parameters, though
only Variable resources and individual elements of Array resources can be
plotted.
Examples
Using parameters in the Mission Sequence:
Create Spacecraft aSat
Create Propagator aProp
Create ReportFile aReport
Create Variable i
BeginMissionSequence
% propagate for 100 steps
For i=1:100
Propagate aProp(aSat)
% write four parameters (one standalone, three coordinate-system-dependent) to a file
Report aReport aSat.TAIGregorian aSat.EarthFixed.X aSat.EarthFixed.Y aSat.EarthFixed.Z
EndFor
Using parameters as plot data:
Create Spacecraft aSat
Create Propagator aProp
Create XYPlot aPlot
aPlot.XVariable = aSat.TAIModJulian
aPlot.YVariables = {aSat.Earth.Altitude, aSat.Earth.ECC}
Create Variable i
BeginMissionSequence
% propagate for 100 steps
For i=1:100
Propagate aProp(aSat)
EndFor
Using parameters as stopping conditions:
Create Spacecraft aSat
aSat.SMA = 6678
Create ForceModel anFM
anFM.Drag.AtmosphereModel = MSISE90
Create Propagator aProp
aProp.FM = anFM
BeginMissionSequence
Propagate aProp(aSat) {aSat.Earth.Altitude = 100, aSat.ElapsedDays = 365}
