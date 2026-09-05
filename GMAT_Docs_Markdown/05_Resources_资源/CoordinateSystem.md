# CoordinateSystem

> 来源：第 430-466 页

CoordinateSystem
CoordinateSystem — An axis and origin pair
Description
A CoordinateSystem in GMAT is defined as an origin and an axis system. You
can select the origin of a CoordinateSystem from various points such as a
CelestialBody, Spacecraft, GroundStation, or LibrationPoint to name a few.
GMAT supports numerous axis systems such as J2000 equator, J2000 ecliptic,
ICRF, ITRF, Topocentric, and ObjectReferenced among others.
CoordinateSystems are tightly integrated into GMAT to enable you to define,
report, and visualize data in coordinate systems relevant to your application.
This resource cannot be modified in the Mission Sequence.
See Also: Spacecraft, Calculation Parameters, OrbitView
Fields
Field Description
AlignmentVectorX
The x component of the AlignmentVector
expressed in the local frame (for example,
expressed in the LocalAlignedConstrained
frame). Used for the following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values AlignmentVector >= 1e-9)
Access set
Default 1
Value
Units N/A
Interfaces gui,script
AlignmentVectorY
The y component of the AlignmentVector
expressed in the local frame (for example,
expressed in the LocalAlignedConstrained
frame). Used for the following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values AlignmentVector>= 1e-9)
Access set
Default 0
Value
Units N/A
Interfaces gui, script
AlignmentVectorZ
The z component of the AlignmentVector
expressed in the local frame (for example,
expressed in the LocalAlignedConstrained
frame). Used for the following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values AlignmentVector>= 1e-9)
Access set
Default 0
Value
Units N/A
Interfaces gui,script
Axes
The axes of the CoordinateSystem.
Data String
Type
Allowed MJ2000Eq, MJ2000Ec, ICRF,
Values ITRF, MODEq, MODEc,
TODEq, TODEc, MOEEq,
MOEEc, TOEEq, TOEEc,
ObjectReferenced, Equator,
BodyFixed, BodyInertial, GSE,
GSM, Topocentric,
BodySpinSun
Access set
Default MJ2000Eq
Value
Units N/A
Interfaces GUI, script
ConstraintVectorX
The x component of the ConstraintVector
expressed in the local frame (for example,
expressed in the LocalAlignedConstrained
frame). Used for the following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values ConstraintVector>= 1e-9)
Access set
Default 0
Value
Units N/A
Interfaces gui,script
ConstraintVectorY The y component of the ConstraintVector
expressed in the local frame (for example,
expressed in the LocalAlignedConstrained
frame). Used for the following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values ConstraintVector>= 1e-9)
Access set
Default 0
Value
Units N/A
Interfaces gui,script
ConstraintVectorZ
The z component of the ConstraintVector
expressed in the local frame (for example,
expressed in the LocalAlignedConstrained
frame). Used for the following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values ConstraintVector>= 1e-9)
Access set
Default 1
Value
Units N/A
Interfaces gui,script
ConstraintReferenceVectorX
The x component of the
ConstraintReferenceVector expressed in the
ConstraintCoordinateSystem. Used for the
following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values ConstraintReferenceVector>=
1e-9)
Access set
Default 0
Value
Units N/A
Interfaces gui,script
ConstraintReferenceVectorY
The y component of the
ConstraintReferenceVector expressed in the
ConstraintCoordinateSystem. Used for the
following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values ConstraintReferenceVector>=
1e-9)
Access set
Default 0
Value
Units N/A
Interfaces gui,script
ConstraintReferenceVectorZ
The z component of the
ConstraintReferenceVector expressed in the
ConstraintCoordinateSystem. Used for the
following axis systems:
LocalAlignedConstrained.
Data Real
Type
Allowed -∞ < Real < ∞ (norm of
Values ConstraintReferenceVector>=
1e-9)
Access set
Default 1
Value
Units N/A
Interfaces gui,script
Constraint Coordinate
System The coordinate system for the
ConstraintReferenceVector. Used for the
following axis sytems:
LocalAlignedConstrained.
Data Type Resource
Allowed Values CoordinateSystem
Access set
Default Value EarthMJ2000Eq
Units N/A
Interfaces gui,script
Epoch
The reference epoch for the
CoordinateSystem. This field is only used for
TOE amd MOE axis types.
Data Type String
Allowed Values A1 Modified Julian epoch.
Access set
Default Value 21545
Units Modified Julian Date
Interfaces GUI, script
Origin
The origin of the CoordinateSystem.
Data String
Type
Allowed CelestialBody, Spacecraft,
Values LibrationPoint, Barycenter,
SolarSystemBarycenter,
GroundStation
Access set
Default Earth
Value
Units N/A
Interfaces GUI, script
Primary
The primary body for an ObjectReferenced
axis system. This field is only used if Axes =
ObjectReferenced. See the discussion below
for more information on how Primary and
Secondary are used to compute
ObjectReferenced axes.
Data String
Type
Allowed CelestialBody, Spacecraft,
Values LibrationPoint, Barycenter,
SolarSystemBarycenter,
GroundStation
Access set
Default Earth
Value
Units N/A
Interfaces GUI, script
ReferenceObject
The reference object for a
LocalAlignedConstrained axis system. The
axes are computed such that the
AlignmentVector in the body frame is aligned
with the vector pointing from the Origin to the
ReferenceObject.
Data Resource
Type
Allowed A Resource that has coordinates.
Values For example: CelestialBody,
Spacecraft, LibrationPoint,
Barycenter,
SolarSystemBarycenter,
GroundStation.
Access set
Default Luna
Value
Units N/A
Interfaces gui,script
Secondary
The secondary body for an ObjectReferenced
axis system. This field is only used if Axes =
ObjectReferenced. See the discussion below
for more information on how Primary and
Secondary are used to compute
ObjectReferenced axes.
Data String
Type
Allowed CelestialBody, Spacecraft,
Values LibrationPoint, Barycenter,
SolarSystemBarycenter,
GroundStation
Access set
Default Luna
Value
Units N/A
Interfaces GUI, script
XAxis
The x-axis definition for an ObjectReferenced
axis system. This field is only used if Axes =
ObjectReferenced. See the discussion below
for more information on how the axes are
computed for ObjectReferenced axis systems.
Data Type String
Allowed R,V, N, -R, -V, -N, or
Values empty
Access set
Default Value R
Units N/A
Interfaces GUI, script
YAxis
The y-axis definition for an ObjectReferenced
axis system. This field is only used if Axes =
ObjectReferenced. See the discussion below
for more information on how the axes are
computed for ObjectReferenced axis systems.
Data Type String
Allowed Values R,V, N, -R, -V,-N, or empty
Access set
Default Value No Default
Units N/A
Interfaces GUI, script
Zaxis
The z-axis for an ObjectReferenced axis
system. This field is only used if Axes =
ObjectReferenced. See the discussion below
for more information on how the axes are
computed for ObjectReferenced axis systems.
Data Type String
Allowed Values R,V, N, -R, -V,-N, or empty
Access set
Default Value N
Units N/A
Interfaces GUI, script
GUI
The New Coordinate System dialog box shown above appears when you add a
new coordinate system in the Resource Tree. You provide a name for the new
CoordinateSystem in the Coordinate System Name box and configure the
CoordinateSystem by selecting the Origin and Axes types along with other
settings. Some settings, such as Primary and Secondary, are only active for
particular Axes types and those dependencies are described below.
When editing an existing CoordinateSystem, you use the CoordinateSystem
dialog box. The default configuration is shown above.
If you select ObjectReferenced for the Axes type, then the Primary,
Secondary, X, Y, and Z fields are activated. You can use the ObjectReferenced
axis system to define coordinates based on the motion of two space objects such
as Spacecraft, CelestialBodies, or Barycenters to name a few. See the
discussion below for a detailed definition of the ObjectReferenced axis system.
If you select TOEEq, TOEEc, MOEEq, or MOEEc as the axis type, then the
A1MJd Epoch field is activated. Use the A1MJd Epoch field to define the
reference epoch of the coordinate system.
If you select LocalAlignedConstrained as the axes Type, then
CoordinateSystem dialog displays the fields illustrated above for configuring
the axes.
Remarks
Computation of J2000-Based Axes using IAU76/FK5
Reduction
FK5 reduction is the transformation that rotates a vector expressed in the
MJ2000Eq system to the EarthFixed CoordinateSystem. There are many
coordinate systems that are intermediate rotations in FK5 reduction and this
section describes how the following axes types are computed: MJ2000Eq,
MJ2000Ec, EarthFixed, MODEq, MODEc,TODEq,TODEc, MODEq,
MODEc, TODEq, and TODEc axes systems.
The time varying orientation of the Earth is complex due to interactions between
the Earth and its external environment (the Sun and Moon and Planets) and
internal dynamics. The orientation cannot currently be modelled to the accuracy
required by many space applications and FK5 reduction is a combination of
dynamical modelling along with daily corrections from empirical observations.
The figure below illustrates components of motion of the Earth with respect to
inertial space. The primary components of the motion of the Earth with respect
to inertial space are Precession, Nutation, Sidereal time and, Polar Motion.
The principal moment of inertia is defined as the Celestial Ephemeris Pole. Due
to the fact that Earth’s mass distribution changes with time, the Celestial
Ephemeris Pole is not constant with respect to the Earth’s surface. Precession is
defined as the coning motion that the Celestial Ephemeris Pole makes around the
ecliptic north pole. The other principal component of the motion of the Celestial
Ephemeris Pole is called nutation and is the oscillation in the angle between the
Celestial Ephemeris Pole and the north ecliptic pole. The theory of Precession
and Nutation come from dynamical models of the Earth’s motion. The Sidereal
time is the rotation of the Earth about the Celestial Ephemeris Pole. The sidereal
time model is a combination of theory and observation. The Earth’s spin axis
direction is not constant with respect to the Earth’s crust and its motion is called
Polar Motion. A portion of polar motion is due to complicated dynamics, and a
portion is due to unmodelled errors in nutation. Polar motion is determined from
observation.
The True of Date (TOD) systems and Mean of Date (MOD) systems are
intermediate coordinate systems in FK5 reduction and are commonly used in
analysis. The details of the computations are contained in the GMAT
mathematical specification and the figure below is included here for summary
purposes. The following abbreviations are used in the figure. PM: Polar Motion,
ST: Sideral Time, NUT: Nutation, PREC: Precession, ITRF: International
Terrestrial Reference Frame (Earth Fixed), PEF: Pseudo Earth Fixed, TODEq:
True of Date Equator, TODEc: True of Date Ecliptic, MODEc: Mean of Date
Ecliptic, MODEq: Mean of Date Equator, FK5: J2000 Equatorial Inertial (IAU-
1976/1980).
Computation of ICRF and ITRF Axes using IAU2000
Conventions
The computation for the International Celestial Reference Frame (ICRF) and the
International Terestrial Reference Fame (ITRF) are computed using the IAU
2000A theory with the 2006 update to precession. GMAT uses the Celestial
Intermediate Origin (CIO) method of transformation which avoids issues
associated with precession and nutation. In the CIO model, the Celestial
Intermediate Pole unit vector is modeled using the variables X and S and the
CIO locator, s. For performance reasons, GMAT interpolates X, Y, and s, from
precomputed values stored in the file named ICRF_Table.txt distributed with
GMAT.
GMAT models the rotation from ICRF to MJ200Eq by rotating through the
EarthFixed frame which is identical for both the old (1976) and new (2000)
theories. For performance reasons, the conversion from ICRF to MJ2000Eq is
interplolated from pre-computed values of the Euler axis and angle between
those frames. Note that GMAT does not currenty support the IAU2000 body
fixed frame for Earth and that model will be included in a future release.
Computation of ObjectReference Axis System
An ObjectReferenced axis system is defined by the motion of one object with
respect to another object. The figure below defines the six principal directions of
an Object Referenced axis system. One is the relative position of the secondary
object with respect to the primary object, denoted by r, expressed in the inertial
frame. The second is the relative velocity, denoted here by v, of the secondary
object with respect to the primary, expressed in the inertial frame. The third
direction is the vector normal to the direction of motion which is denoted by n
and is calculated using n = r × v. The remaining three directions are the negative
of the first three yielding the complete set: {R,-R, V,-V, N,-N}.
You define an Object Referenced axis system by defining two axes from the
three available [X, Y, and Z] using the six available options {R,-R, V,-V, N,-N}.
Given two directions, GMAT constructs an orthogonal, right-handed
CoordinateSystem. For example, if you choose the x-axis to be in the direction
of R and the z-axis to be in the direction of N, GMAT completes the right-
handed set by setting the y-axis in the direction of NxR. If you choose
permutations that result in a non-orthogonal or left-handed CoordinateSystem,
GMAT will throw an error message.
Warning
GMAT currently assumes that terms involving the cross and dot
product of acceleration are zero when computing
ObjectReferenced rotation matrices.
Overview of Built-in Coordinate Systems
Name Origin Axes Description
EarthMJ2000Eq Earth MJ2000Eq
An Earth equator inertial system based
on IAU-1976/FK5 theory with 1980
update to nutation.
EarthMJ2000Ec Earth MJ2000Ec
An Earth ecliptic inertial system based
on IAU-1976/FK5 theory with 1980
update to nutation.
EarthFixed Earth BodyFixed
An Earth fixed system based on IAU-
1976/FK5 theory with 1980 update to
nutation.
EarthICRF Earth ICRF
An Earth equator inertial system based
on IAU-2000 theory with 2006 update
to precession.
Description of Axes Types
Origin Base
Axes Name Description
Limitations Type
MJ2000Eq None IAU-
1976 An inertial coordinate system.
FK5 The nominal x-axis points
along the line formed by the
intersection of the Earth’s mean
equatorial plane and the mean
ecliptic plane (at the J2000
epoch), in the direction of
Aries. The z-axis is normal to
the Earth’s mean equator at the
J2000 epoch and the y-axis
completes the right-handed
system. The mean planes of the
ecliptic and equator, at the
J2000 epoch, are computed
using IAU-1976/FK5 theory
with 1980 update for nutation.
MJ2000Ec None IAU-
1976 An inertial coordinate system.
FK5 The x-axis points along the line
formed by the intersection of
the Earth’s mean equator and
the mean ecliptic plane at the
J2000 epoch. The z-axis is
normal to the mean ecliptic
plane at the J2000 Epoch and
the y-axis completes the right-
handed set. This system is
computed using IAU-
1976/FK5 theory with 1980
update for nutation.
ICRF None IAU-
2000 An inertial coordinate system.
The axes are close to the mean
Earth equator and pole at the
J2000 epoch, and at the Earth’s
surface, the RSS difference
between vectors expressed in
MJ2000Eq and ICRF is less
than 1 m. Note that since
MJ2000Eq and ICRF are
imperfect realizations of
inertial systems, the
transformation between them is
time varying. This axis system
is computed using IAU-2000A
theory with 2006 update for
precession.
LocalAlignedConstrained None IAU-
1976 The
FK5 LocalAlignedConstrained
axis system is an aligned
constrained system based on
the position of the
ReferenceObject with respect
to the Origin and is computed
using the well known Triad
algorithm. The axes are
computed such that the
AlignmentVector, defined as
the components of the
alignment vector expressed in
the LocalAlignedConstrained
system, is aligned with the
position of the ReferenceBody
w/r/t the origin. The rotation
about the AlignmentVector is
resolved by minimizing the
angle between the
ContraintVector, defined as
the constraint vector expressed
in the
LocalAlignedConstrained
system, and the
ConstraintReferenceVector,
defined as the constraint
reference vector expressed in
the
ConstraintCoordinateSystem.
The alignment vectors and the
constraint vectors cannot have
zero length. Similarly, the cross
products of the constraint
vector and alignment vector
cannot have zero length.
MODEq None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to Earth’s
mean equator at the current
epoch. The current epoch is
defined by the context of use
and usually comes from the
spacecraft or graphics epoch.
This system is computed using
IAU-1976/FK5 theory with
1980 update for nutation.
MODEc None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to the mean
ecliptic at the current epoch.
The current epoch is defined by
the context of use and usually
comes from the spacecraft or
graphics epoch. This system is
computed using IAU-
1976/FK5 theory with 1980
update for nutation.
TODEq None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to Earth’s
true equator at the current
epoch. The current epoch is
defined by the context of use
and usually comes from the
spacecraft or graphics epoch.
This system is computed using
IAU-1976/FK5 theory with
1980 update for nutation.
TODEc None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to Earth’s
true ecliptic at the current
epoch. The current epoch is
defined by the context of use
and usually comes from the
spacecraft or graphics epoch.
This system is computed using
IAU-1976/FK5 theory with
1980 update for nutation.
MOEEq None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to Earth’s
mean equator at the reference
epoch. The reference epoch is
defined on the
CoordinateSystem object.
This system is computed using
IAU-1976/FK5 theory with
1980 update for nutation.
MOEEc None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to the mean
ecliptic at the reference epoch.
The reference epoch is defined
on the CoordinateSystem
object. This system is
computed using IAU-
1976/FK5 theory with 1980
update for nutation.
TOEEq None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to Earth’s
true equator at the reference
epoch. The reference epoch is
defined on the
CoordinateSystem object.
This system is computed using
IAU-1976/FK5 theory with
1980 update for nutation.
TOEEc None IAU-
1976 A quasi-inertial coordinate
FK5 system referenced to the true
ecliptic at the reference epoch.
The reference epoch is defined
on the CoordinateSystem
object. This system is
computed using IAU-
1976/FK5 theory with 1980
update for nutation.
ObjectReferenced None IAU-
1976 An ObjectReferenced system
FK5 is a CoordinateSystem whose
axes are defined by the motion
of one object with respect to
another object. See the
discussion above for a detailed
description of the
ObjectReferenced axis
system.
Equator Celestial IAU-
Body 1976 A true of date equator axis
FK5 system for the celestial body
selected as the origin. The
Equator system is defined by
the body’s equatorial plane and
its intersection with the ecliptic
plane, at the current epoch. The
current epoch is defined by the
context of use and usually
comes from the spacecraft or
graphics epoch. See the
Remarks for Celestial body
models for axis system
definitions for celestial bodies.
BodyFixed Celestial IAU-
Body or 1976 The BodyFixed axis system is
Spacecraft FK5 referenced to the body equator
and the prime meridian of the
body. See the Remarks for
Celestial body models for axis
system definitions for celestial
bodies.
When Origin is a Spacecraft,
the axes are computed using
the Spacecraft’s attitude
model. Note: not all attitude
models compute body rates. In
the case that body rates are not
available on a spacecraft, a
request for velocity
transformations using a
BodyFixed axis system will
result in an error.
BodyInertial Celestial IAU- An inertial system referenced
Body 1976 to the equator ( at the J2000
FK5 epoch ) of the celestial body
selected as the origin of the
CoordinateSystem. Because
the BodyInertial axis system
uses different theories for
different bodies, the following
definitions describe only the
nominal axis configurations.
The x-axis points along the line
formed by the intersection of
the bodies equator and earth’s
mean equator at J2000. The z-
axis points along the body's
spin axis direction at the J2000
epoch. The y-axis completes
the right-handed set. For Earth,
the BodyInertial axis system is
identical to the MJ2000Eq
system. See the Remarks for
Celestial body models for axis
system definitions for all other
celestial bodies.
GSE None IAU-
1976 The Geocentric Solar Ecliptic
FK5 system. The x-axis points from
Earth to the Sun. The z-axis is
defined as the cross product
RxV where R and V are earth’s
position and velocity with
respect to the sun respectively.
The y-axis completes the right-
handed set. The GSE axes are
computed using the relative
motion of the Earth and Sun
even if the origin is not Earth.
GSM None IAU-
1976 The Geocentric Solar Magnetic
FK5 system. The x-axis points from
Earth to the Sun. The z-axis is
defined to be orthogonal to the
x-axis and lies in the plane of
the x-axis and Earth’s magnetic
dipole vector. The y-axis
completes the right-handed set.
The GSM axes are computed
using the relative motion of the
Earth and Sun even if the
origin is not Earth.
Topocentric Earth IAU-
1976 A GroundStation-based
FK5 coordinate system. The y-axis
points due East and the z-axis
is normal to the local horizon.
The x-axis completes the right
handed set.
BodySpinSun Celestial IAU-
Body 1976 A celestial body spin-axis-
FK5 referenced system. The x-axis
points from the celestial body
to the Sun. The y-axis is
computed as the cross product
of the x-axis and the body's
spin axis. The z-axis completes
the right-handed set.
Examples
Define a Spacecraft’s state in EarthFixed coordinates.
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = EarthFixed
aSpacecraft.X = 7100
aSpacecraft.Y = 0
aSpacecraft.Z = 1300
aSpacecraft.VX = 0
aSpacecraft.VY = 7.35
aSpacecraft.VZ = 1
Report a Spacecraft’s state in GroundStation Topocentric coordinates.
Create Spacecraft aSat
Create Propagator aProp
Create GroundStation aStation
Create CoordinateSystem stationTopo
stationTopo.Origin = aStation
stationTopo.Axes = Topocentric
Create ReportFile aReport
aReport.Filename = 'ReportFile1.txt'
aReport.Add = {aSat.stationTopo.X aSat.stationTopo.Y aSat.stationTopo.Z ...
aSat.stationTopo.VX aSat.stationTopo.VY aSat.stationTopo.VZ}
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 8640.0}
View a trajectory in an ObjectReferenced, rotating-LibrationPoint system.
% Create the Earth-Moon Barycenter and Libration Point
Create Barycenter EarthMoonBary
EarthMoonBary.BodyNames = {Earth,Luna};
Create LibrationPoint SunEarthMoonL1
SunEarthMoonL1.Primary = Sun;
SunEarthMoonL1.Secondary = EarthMoonBary
SunEarthMoonL1.Point = L1;
% Create the coordinate system
Create CoordinateSystem RotatingSEML1Coord
RotatingSEML1Coord.Origin = SunEarthMoonL1
RotatingSEML1Coord.Axes = ObjectReferenced
RotatingSEML1Coord.XAxis = R
RotatingSEML1Coord.ZAxis = N
RotatingSEML1Coord.Primary = Sun
RotatingSEML1Coord.Secondary = EarthMoonBary
% Create the spacecraft and propagator
Create Spacecraft aSpacecraft
aSpacecraft.DateFormat = UTCGregorian
aSpacecraft.Epoch = '09 Dec 2005 13:00:00.000'
aSpacecraft.CoordinateSystem = RotatingSEML1Coord
aSpacecraft.X = -32197.88223741966
aSpacecraft.Y = 211529.1500044117
aSpacecraft.Z = 44708.57017366499
aSpacecraft.VX = 0.03209516489451751
aSpacecraft.VY = 0.06100386504053736
aSpacecraft.VZ = 0.0550442738917212
Create Propagator aPropagator
aPropagator.FM = aForceModel
aPropagator.MaxStep = 86400
Create ForceModel aForceModel
aForceModel.PointMasses = {Earth,Sun,Luna}
% Create a 3-D graphic
Create OrbitView anOrbitView
anOrbitView.Add = {aSpacecraft, Earth, Sun, Luna}
anOrbitView.CoordinateSystem = RotatingSEML1Coord
anOrbitView.ViewPointReference = SunEarthMoonL1
anOrbitView.ViewPointVector = [-1500000 0 0 ]
anOrbitView.ViewDirection = SunEarthMoonL1
anOrbitView.ViewUpCoordinateSystem = RotatingSEML1Coord
anOrbitView.Axes = Off
anOrbitView.XYPlane = Off
BeginMissionSequence
Propagate aPropagator(aSpacecraft, {aSpacecraft.ElapsedDays = 180})
