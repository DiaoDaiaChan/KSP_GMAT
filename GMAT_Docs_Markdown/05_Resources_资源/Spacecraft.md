# Spacecraft

> 来源：第 976-977 页

Spacecraft
Spacecraft — A spacecraft model
Description
A Spacecraft resource is GMAT's spacecraft model and includes data and
models for the spacecraft's orbit, epoch, attitude, and physical parameters (such
as mass and drag coefficient), as well as attached hardware, including tanks and
thrusters. The Spacecraft model also contains the data that configures how the
Spacecraft 3-D CAD model is used in an OrbitView. Spacecraft has certain
fields that can be set in the Mission Sequence and some that cannot. See the field
tables on the pages below for more information.
GMAT's documentation for Spacecraft is extensive and is broken down into the
following sections:
Spacecraft Attitude
Spacecraft Ballistic/Mass Properties
Spacecraft Epoch
Spacecraft Hardware
Spacecraft Navigation
Spacecraft Orbit State
Spacecraft Visualization Properties

## Spacecraft Attitude

Spacecraft Attitude
Spacecraft Attitude — The spacecraft attitude model
Description
GMAT models the orientation and rate of rotation of a spacecraft using several
different mathematical models. Currently, GMAT assumes that a Spacecraft is a
rigid body. The currently supported attitude models are Spinner,
CoordinateSystemFixed, and SpiceAttitude. The Spinner model is a simple,
inertially fixed spin axis model. The CoordinateSystemFixed model allows you
to use any CoordinateSystem supported by GMAT as the attitude of a
Spacecraft. The SpiceAttitude model allows you to define the Spacecraft
attitude based on SPICE attitude kernels.
See Also: Spacecraft
Fields
Field Description
AngularVelocityX
The x-component of Spacecraft body angular
velocity expressed in the inertial frame.
AngularVelocityX is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real< ∞
Access set,get
Default Value 0
Units deg/sec
Interfaces GUI, script
AngularVelocityY
The y-component of Spacecraft body angular
velocity expressed in the inertial frame.
AngularVelocityY is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real< ∞
Access set,get
Default Value 0
Units deg/sec
Interfaces GUI, script
AngularVelocityZ
The z-component of Spacecraft body angular
velocity expressed in the inertial frame.
AngularVelocityZ is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real< ∞
Access set,get
Default Value 0
Units deg/sec
Interfaces GUI, script
Attitude
The attitude mode for the Spacecraft.
Data String
Type
Allowed CoordinateSystemFixed, Spinner,
Values SpiceAttitude, NadirPointing,
CCSDS-AEM, PrecessingSpinner
Access set
Default CoordinateSystemFixed
Value
Units N/A
Interfaces GUI, script
AttitudeConstraintType
The constraint type for resolving attitude
ambiguity. The attitude is computed such that the
angle between the BodyConstraintVector and
the constraint defined by
AttitudeConstraintType is minimized. A
Velocity constraint uses the inertial velocity
vector expressed with respect to the
AttitudeReferenceBody. An OrbitNormal
constraint uses the orbit normal vector expressed
with respect to the AttitudeReferenceBody.
AttitudeConstraintType is used for the
following attitude models: NadirPointing.
Data Type Enumeration
Allowed Values Velocity, OrbitNormal
Access set
Default Value OrbitNormal
Units N/A
Interfaces GUI, script
AttitudeCoordinateSystem
The CoordinateSystem used in attitude
computations. The AttitudeCoordinateSystem
field is only used for the following attitude
models: CoordinateSystemFixed.
Data Type String
Allowed Values CoordinateSystem resource.
Access set
Default Value EarthMJ2000Eq
Units N/A
Interfaces GUI, script
AttitudeFileName
Path (optional) and name of CCSDS attitude
ephemeris message file. If a path is not provided,
and GMAT does not find the file in the current
directory, then an error occurs and execution is
halted.
Data Type String
Allowed Values AEM File
Access set
Default Value N/A
Units N/A
Interfaces GUI, script
AttitudeRate-
DisplayStateType The attitude rate representation to display in the
GUI and script file. AttitudeRateDisplayType is
used for the following attitude models: Spinner.
Data Type String
Allowed AngularVelocity,
Values EulerAngleRates
Access set
Default AngularVelocity
Value
Units N/A
Interfaces GUI, script
AttitudeReferenceBody
The celestial body used to define nadir.
AttitudeReferenceBody is used for the
following attitude models: NadirPointing.
Data Type Resource
Allowed Values Celestial Body
Access set
Default Value Earth
Units N/A
Interfaces GUI, script
AttitudeSpiceKernelName
SPK Kernels for Spacecraft attitude. SPK
atttitude kernels have extension ".BC". This field
cannot be set in the Mission Sequence. An empty
list unloads all kernels of this type on the
Spacecraft.
Data Type String array
Allowed Values Array of attitude kernel files
Access set
Default Value empty array
Units N/A
Interfaces GUI, script
BodyAlignmentVectorX
The x-component of the alignment vector,
expressed in the body frame, to align with the
opposite of the radial vector.
BodyAlignmentVectorX is used for the
following attitude models: NadirPointing.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 1
Units N/A
Interfaces GUI, script
BodyAlignmentVectorY
The y-component of the alignment vector,
expressed in the body frame, to align with the
opposite of the radial vector.
BodyAlignmentVectorY is used for the
following attitude models: NadirPointing.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
BodyAlignmentVectorZ
The z-component of the alignment vector,
expressed in the body frame, to align with the
opposite of the radial vector.
BodyAlignmentVectorZ is used for the
following attitude models: NadirPointing.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
BodyConstraintVectorX
The x-component of the constraint vector,
expressed in the body frame. See NadirPointing
description for further details.
BodyConstraintVectorX is used for the
following attitude models: NadirPointing.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
BodyConstraintVectorY
The y-component of the constraint vector,
expressed in the body frame. See NadirPointing
description for further details.
BodyConstraintVectorY is used for the
following attitude models: NadirPointing.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
BodyConstraintVectorZ
The z-component of the constraint vector,
expressed in the body frame. See NadirPointing
description for further details.
BodyConstraintVectorZ is used for the
following attitude models: NadirPointing.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 1
Units N/A
Interfaces GUI, script
BodySpinAxisX The x-component of the spin axis, expressed in
the body frame. BodySpinAxisX is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
BodySpinAxisY
The y-component of the spin axis, expressed in
the body frame. BodySpinAxisY is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
BodySpinAxisZ
The z-component of the spin axis, expressed in
the body frame. BodySpinAxisZ is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 1
Units N/A
Interfaces GUI, script
DCM11
Component (1,1) of the Direction Cosine Matrix.
DCM11 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 1
Units N/A
Interfaces GUI, script
DCM12
Component (1,2) of the Direction Cosine Matrix.
DCM12 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 0
Units N/A
Interfaces GUI, script
DCM13
Component (1,3) of the Direction Cosine Matrix.
DCM13 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 0
Units N/A
Interfaces GUI, script
DCM21
Component (2,1) of the Direction Cosine Matrix.
DCM21 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 0
Units N/A
Interfaces GUI, script
DCM22
Component (2,2) of the Direction Cosine Matrix.
DCM22 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 1
Units N/A
Interfaces GUI, script
DCM23
Component (2,3) of the Direction Cosine Matrix.
DCM23 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 0
Units N/A
Interfaces GUI, script
DCM31
Component (3,1) of the Direction Cosine Matrix.
DCM31 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 0
Units N/A
Interfaces GUI, script
DCM32
Component (3,2) of the Direction Cosine Matrix.
DCM32 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 1
Units N/A
Interfaces GUI, script
DCM33
Component (3,3) of the Direction Cosine Matrix.
DCM33 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -1 <= Real <=1
Access set,get
Default Value 1
Units N/A
Interfaces GUI, script
EulerAngle1
The value of the first Euler angle. EulerAngle1
is used for the following Attitude models:
Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units deg.
Interfaces GUI, script
EulerAngle2
The value of the second Euler angle.
EulerAngle2 is used for the following Attitude
models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units deg.
Interfaces GUI, script
EulerAngle3
The value of the third Euler angle. EulerAngle3
is used for the following Attitude models:
Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units deg.
Interfaces GUI, script
EulerAngleRate1
The value of the first Euler angle rate.
EulerAngleRate1 is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units deg./sec.
Interfaces GUI, script
EulerAngleRate2
The value of the second Euler angle rate.
EulerAngleRate2 is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 1
Units deg./sec.
Interfaces GUI, script
EulerAngleRate3
The value of the third Euler angle rate.
EulerAngleRate3 is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 1
Units deg./sec.
Interfaces GUI, script
FrameSpiceKernelName
SPK Kernels for Spacecraft body frame. SPK
Frame kernels have extension ".TF". This field
cannot be set in the Mission Sequence. An empty
list unloads all kernels of this type on the
Spacecraft.
Data Type String array
Allowed Values Array of .tf files.
Access set
Default Value emtpy array
Units N/A
Interfaces GUI, script
EulerAngleSequence
The Euler angle sequence used for Euler angle
input and output..
Data Type String
Allowed 123,231,312,132,321,213,121,
Values 232,313,131,323,212
Access set
Default 321
Value
Units N/A
Interfaces GUI, script
InitialPrecessionAngle
The initial precession angle.
InitialPrecessionAngle is used for the following
attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units deg.
Interfaces GUI, script
InitialSpinAngle
The initial attitude spin angle. InitialSpinAngle
is used for the following attitude models:
PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units deg.
Interfaces GUI, script
NAIFIdReferenceFrame
The Id of the spacecraft body frame used in
SPICE kernels.
Data Type Integer
Allowed Values -∞ < Integer < ∞
Access set
Default Value -9000001
Units N/A
Interfaces GUI, script
NutationAngle
The attitude nutation angle. NutationAngle is
used for the following attitude models:
PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 15
Units deg.
Interfaces GUI, script
NutationReferenceVectorX
The x-component of the nutation reference
vector, expressed in the inertial frame.
NutationReferenceVectorX is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
NutationReferenceVectorY
The y-component of the nutation reference
vector, expressed in the inertial frame.
NutationReferenceVectorY is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units N/A
Interfaces GUI, script
NutationReferenceVectorZ
The z-component of the nutation reference
vector, expressed in the inertial frame.
NutationReferenceVectorZ is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 1
Units N/A
Interfaces GUI, script
MRP1 The value of the first modified Rodrigues
parameter. MRP1 is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units dimensionless
Interfaces GUI, script
MRP2
The value of the second modified Rodrigues
parameter. MRP2 is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units dimensionless
Interfaces GUI, script
MRP3
The value of the second modified Rodrigues
parameter. MRP2 is used for the following
Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units dimensionless
Interfaces GUI, script
PrecessionRate
The rate of attitude precession.
InitialPrecessionAngle is used for the following
attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units deg./s
Interfaces GUI, script
Q1
First component of quaternion. GMAT’s
quaternion representation includes the three
“vector” components as the first three elements
in the quaternion and the “rotation” component
as the last element in the quaternion. Q1 is used
for the following Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units dimensionless
Interfaces GUI, script
Q2
Second component of quaternion. GMAT’s
quaternion representation includes the three
“vector” components as the first three elements
in the quaternion and the “rotation” component
as the last element in the quaternion. Q2 is used
for the following Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units dimensionless
Interfaces GUI, script
Q3
Third component of quaternion. GMAT’s
quaternion representation includes the three
“vector” components as the first three elements
in the quaternion and the “rotation” component
as the last element in the quaternion. Q3 is used
for the following Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 0
Units dimensionless
Interfaces GUI, script
Q4
Fourth component of quaternion. GMAT’s
quaternion representation includes the three
“vector” components as the first three elements
in the quaternion and the “rotation” component
as the last element in the quaternion. Q4 is used
for the following Attitude models: Spinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set,get
Default Value 1
Units dimensionless
Interfaces GUI, script
Quaternion
The quaternion vector. GMAT’s quaternion
representation includes the three “vector”
components as the first three elements in the
quaternion and the “rotation” component as the
last element in the quaternion. Quaternion is
used for the following Attitude models: Spinner.
Data Type Real array
Allowed Values Real array (length four)
Access set,get
Default Value [0 0 0 1];
Units dimensionless
Interfaces GUI, script
SCClockSpiceKernelName
SPK Kernels for spacecraft clock. SPK clock
kernels have extension ".TSC". This field cannot
be set in the Mission Sequence. An empty list
unloads all kernels of this type on the
Spacecraft. An empty list unloads all kernels of
this type on the Spacecraft.
Data Type String array
Allowed Values Array of .tsc file names
Access set,get
Default Value empty array
Units N/A
Interfaces GUI, script
SpinRate
The attitude spin rate. SpinRate is used for the
following attitude models: PrecessingSpinner.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 10
Units deg./s
Interfaces GUI, script
Remarks
Overview of Availble Attitude Models
GMAT supports many attitude models including the following:
CoordinateSystemFixed, SpiceAttitude, NadirPointing, CCSDS-AEM,
PrecessingSpinner, and Spinner (we recommend using thew new
PrecessingSpinner model instead of Spinner). Different attitude models require
different information to fully configure the model. For example, when you select
the CoordinateSystemFixed model, the attitude and body rates are entirely
determined by the CoordinateSystem model and defining Euler angles or
angular velocity components are not required and have no effect. The reference
tables above, and the detailed examples for each model type below, describe
which fields are used for each model.
Note
GMAT attitude parameterizations such as the DCM rotate from
inertial to body.
Overview of State Representations
Quaternion
The quaternion is a four element, non-singular attitude representation. GMAT’s
quaternion representation includes the three “vector” components as the first
three elements in the quaternion and the “rotation” component as the last
element in the quaternion. In assignment mode, you can set the quaternions
element by element like this
aSpacecraft.Q1 = 0.5
aSpacecraft.Q2 = 0.5
aSpacecraft.Q3 = 0.5
aSpacecraft.Q4 = 0.5
or simultaneously set the entire quaternion like this
aSpacecraft.Quaternion = [0.5 0.5 0.5 0.5]
GMAT normalizes the quaternion before use. In command mode, you must enter
the entire quaternion as a single vector to avoid scaling components of the
quaternion before the entire quaternion is set.
DirectionCosineMatrix (DCM)
The Direction Cosine Matrix is a 3x3 array that contains cosines of the angles
that rotate from the x, y, and z inertial axes to the x, y, and z body axes. The
direction cosine matrix must be ortho-normal and you define the DCM element
by element. Here is an example that shows how to define the attitude using the
DCM.
aSpacecraft.DCM11 = 1
aSpacecraft.DCM12 = 0
aSpacecraft.DCM13 = 0
aSpacecraft.DCM21 = 0
aSpacecraft.DCM22 = 1
aSpacecraft.DCM23 = 0
aSpacecraft.DCM31 = 0
aSpacecraft.DCM32 = 0
aSpacecraft.DCM33 = 1
Euler Angles
Euler angles are a sequence of three rotations about coordinate axes to transform
from one system to another system. GMAT supports all 12 Euler angle
sequences. Here is an example setting attitude using a “321” sequence.
aSpacecraft.EulerAngleSequence = '321'
aSpacecraft.EulerAngle1 = 45
aSpacecraft.EulerAngle2 = 45
aSpacecraft.EulerAngle3 = 90
Warning
Caution: The Euler angles have singularities that can cause
issues during modeling. We recommend using other
representations for this reason.
Modified Rogriques parameters
The modified Rodgriques parameters are a modification of the Euer Axis/Angle
representation. Specifically, the MRP vector is equal to nhat* tan(Euler Angle/4)
where nhat is the unitized Euler Axis.
aSpacecraft.MRP1 = 0.2928932188134525
aSpacecraft.MRP2 = 0.2928932188134524
aSpacecraft.MRP3 = 1.149673585146546e-017
Euler Angles Rates
The Euler angle rates are the first time derivative of the Euler angles and can be
used to define the body rates. Euler angle rates use the same sequence as the
EulerAngles. The example below shows how to define the Euler angle rates for a
spacecraft.
aSpacecraft.EulerAngleSequence = '321'
aSpacecraft.EulerAngleRate1 = -5
aSpacecraft.EulerAngleRate2 = 20
aSpacecraft.EulerAngleRate3 = 30
Angular Velocity
The angular velocity is the angular velocity of the spacecraft body with respect
to the inertial frame, expressed in the inertial frame. The example below shows
how to define the angular velocity for a spacecraft.
aSpacecraft.AngularVelocityX = 5;
aSpacecraft.AngularVelocityY = 10;
aSpacecraft.AngularVelocityZ = 5;
Coordinate System Fixed Attitude Model
The CoordinateSystemFixed model allows you to use any existing
CoordinateSystem to define the attitude of a Spacecraft. The attitude uses the
axes defined on the CoordinateSystem to compute the body fixed to inertial
matrix and attitude rate parameters such as the angular velocity. To configure
this attitude mode, select CoordinateSystemFixed, for Attitude. You can define
the EulerAngleSequence used when outputting EulerAngles and EulerAngle
rates.
Warning
For the CoordinateSystemFixed attitude model, the attitude is
completely described by the selected coordinate system. If you
are working in the script, setting attitude parameters (Euler
Angles, Quaternion etc.) or setting attitude rate parameters such
as (Euler Angle Rates etc.) has no effect.
The script example below shows how to configure a Spacecraft to use a
spacecraft VNB attitude system.
Create Spacecraft aSat
aSat.Attitude = CoordinateSystemFixed
aSat.ModelRotationZ = -90
aSat.AttitudeCoordinateSystem = 'attCoordSys'
Create ForceModel Propagator1_ForceModel
Create Propagator Propagator1
Propagator1.FM = Propagator1_ForceModel
Propagator1.MaxStep = 10
Create CoordinateSystem attCoordSys
attCoordSys.Origin = Earth
attCoordSys.Axes = ObjectReferenced
attCoordSys.XAxis = V
attCoordSys.YAxis = N
attCoordSys.Primary = Earth
attCoordSys.Secondary = aSat
Create OrbitView OrbitView1;
OrbitView1.Add = {aSat, Earth}
OrbitView1.ViewPointReference = Earth
OrbitView1.ViewPointVector = [ 30000 0 0 ]
BeginMissionSequence
Propagate Propagator1(aSat) {aSat.ElapsedSecs = 12000.0}
Spinner Attitude Model
The Spinner attitude model propagates the attitude assuming the spin axis
direction is fixed in inertial space. We recommend using the newer
PrecessingSpinner model instead of Spinner, and this model is maintained
primarily for backwards compatibility. You define the attitude by providing
initial body orientation and rates. GMAT propagates the attitude by computing
the angular velocity and then rotates the Spacecraft about that angular velocity
vector at a constant rate defined by the magnitude of the angular velocity. You
can define the initial attitude using quaternions, Euler angles, the DCM, or the
modified Rodriques parameters. You can define the attitude rates using Euler
angles rates or angular velocity. When working with Euler angles, the rotation
sequence is determined by the EulerAngleSequence field.
Warning
Caution: If you are working in the script, setting the
CoordinateSystem for the Spinner attitude model has no
effect.
The example below configures a spacecraft to spin about the inertial z axis.
Create Spacecraft aSat;
aSat.Attitude = Spinner
aSat.ModelRotationZ = -90
aSat.AngularVelocityZ = 5
Create ForceModel Propagator1_ForceModel
Create Propagator Propagator1
GMAT Propagator1.FM = Propagator1_ForceModel
GMAT Propagator1.MaxStep = 10
Create CoordinateSystem attCoordSys
attCoordSys.Origin = Earth
attCoordSys.Axes = ObjectReferenced
attCoordSys.XAxis = V
attCoordSys.YAxis = N
attCoordSys.Primary = Earth
attCoordSys.Secondary = aSat
Create OrbitView OrbitView1;
OrbitView1.Add = {aSat, Earth}
OrbitView1.ViewPointReference = Earth
OrbitView1.ViewPointVector = [ 30000 0 0 ]
BeginMissionSequence
Propagate Propagator1(aSat) {aSat.ElapsedSecs = 12000.0}
SPK Attitude Model
The SpiceAttitude model propagates the attitude using attitude SPICE kernels.
To configure a Spacecraft to use SPICE kernels select SpiceAttitude for the
Attitude field as shown below.
Warning
Caution: For the SpiceAttitude model, the attitude is
completely described by the spice kernels. When working in
the script, setting the CoordinateSystem, attitude parameters
(EulerAngles, Quaternion etc.) or attitude rate parameters
such as (EulerAngleRates etc.) has no effect.
You must provide three SPICE kernel types for the SpiceAttitude model: the
attitude kernel (.bc file), the frame kernel (.tf file) and the spacecraft clock kernel
(.tsc file). These files are defined on the Spacecraft SPICE tab as shown below.
In addition to the kernels, you must also provide the Spacecraft NAIFId and the
NAIFIdReferenceFrame. Below is an illustration of the SPICE tab configured
for MarsExpress script found later in this section.
The example below configures a Spacecraft to use SPK kernels to propagator
the attitude for Mars Express. The SPK kernels are distributed with GMAT.
Create Spacecraft MarsExpress
MarsExpress.NAIFId = -41
MarsExpress.NAIFIdReferenceFrame = -41001
MarsExpress.Attitude = 'SpiceAttitude'
MarsExpress.OrbitSpiceKernelName = ...
{'../data/vehicle/ephem/spk/MarsExpress_Short.BSP'}
MarsExpress.AttitudeSpiceKernelName = ...
{'../data/vehicle/ephem/spk/MarsExpress_ATNM_PTR00012_100531_002.BC'}
MarsExpress.SCClockSpiceKernelName = ...
{'../data/vehicle/ephem/spk/MarsExpress_MEX_100921_STEP.TSC'}
MarsExpress.FrameSpiceKernelName = ...
{'../data/vehicle/ephem/spk/MarsExpress_MEX_V10.TF'}
Create Propagator spkProp
spkProp.Type = SPK
spkProp.StepSize = 60
spkProp.CentralBody = Mars
spkProp.EpochFormat = 'UTCGregorian'
spkProp.StartEpoch = '01 Jun 2010 16:59:09.815'
Create CoordinateSystem MarsMJ2000Eq
MarsMJ2000Eq.Origin = Mars
MarsMJ2000Eq.Axes = MJ2000Eq
Create OrbitView Enhanced3DView1
Enhanced3DView1.Add = {MarsExpress, Mars}
Enhanced3DView1.CoordinateSystem = MarsMJ2000Eq
Enhanced3DView1.ViewPointReference = Mars
Enhanced3DView1.ViewPointVector = [ 10000 10000 10000 ]
Enhanced3DView1.ViewDirection = Mars
BeginMissionSequence
Propagate spkProp(MarsExpress) {MarsExpress.ElapsedDays = 0.2}
Nadir Pointing Model
The NadirPointing attitude mode configures the attitude of a spacecraft to point
a specified vector in the spacecraft body system in the nadir direction. The
ambiguity in angle about the nadir vector is resolved by minimizing the angle
between two constraint vectors. Note: the nadir pointing mode points the attitude
in the negative radial direction (not opposite the planetodetic normal).
To configure which axis points to nadir, set the AttitudeReferenceBody field to
the desired celestial body and define the body components of the alignment
vector using the BodyAlignmentVector fields. To configure the constraint, set
the AttitudeConstraintType field to the desired constraint type, and define the
body components of the constraint using the BodyConstraintVector fields.
GMAT supports two constraint types, OrbitNormal and Velocity, and in both
cases the vectors are constructed using the inertial spacecraft state with respect
to the AttitudeReferenceBody.
Warning
Attitude rates are not computed for the NadirPointing model.
If you perform a computation that requires attitude rate
information when using the NadirPointing mode, GMAT will
throw an error message and execution will stop. Similarly, if
the definitions of the BodyAlignmentVector and
BodyConstraintVector fields result in an undefined attitude,
an error message is thrown and execution will stop.
The script example below shows how to configure a Spacecraft to use an Earth
NadirPointing attitude system where the body y-axis points nadir and the angle
between the body x-axis and the orbit normal vector is a minimum.
Create Spacecraft aSat;
GMAT aSat.Attitude = NadirPointing;
GMAT aSat.AttitudeReferenceBody = Earth
GMAT aSat.AttitudeConstraintType = OrbitNormal
GMAT aSat.BodyAlignmentVectorX = 0
GMAT aSat.BodyAlignmentVectorY = 1
GMAT aSat.BodyAlignmentVectorZ = 0
GMAT aSat.BodyConstraintVectorX = 1
GMAT aSat.BodyConstraintVectorX = 0
GMAT aSat.BodyConstraintVectorX = 0
Create ForceModel Propagator1_ForceModel
Create Propagator Propagator1
Propagator1.FM = Propagator1_ForceModel
Propagator1.MaxStep = 10
Create OrbitView OrbitView1;
OrbitView1.Add = {aSat, Earth}
OrbitView1.ViewPointReference = Earth
OrbitView1.ViewPointVector = [ 30000 0 0 ]
BeginMissionSequence
Propagate Propagator1(aSat) {aSat.ElapsedSecs = 12000.0}
CCSDS Attitude Ephemeris Message
The CCSDS Attitude Ephemeris Message (AEM) is an ASCII standard for
attitude ephemerides documented in “ATTITUDE DATA MESSAGES”
RECOMMENDED STANDARD CCSDS 504.0-B-1. GMAT supports some, but
not all, of the attitude messages defined in the standard. According to the
CCSDS AEM specification, “The set of attitude data messages described in this
Recommended Standard is the baseline concept for attitude representation in
data interchange applications that are cross-supported between Agencies of the
CCSDS.” Additionally, the forward of the standard states “Derived Agency
standards may implement only a subset of the optional features allowed by the
Recommended Standard and may incorporate features not addressed by this
Recommended Standard. See the details below for supported keyword types and
details for creating AEM files that GMAT can use for attitude modelling.
An AEM file must have the format illustrated below described in Table 4-1 of
the standard. The header section contains high level information on the version,
originator, and date. The body of the file is composed of paired blocks of
Metadata and data. The Metadata sections contain information on the data such
as the first and last epoch of the block, the time system employed, the reference
frames, the attitude type (quaternion, Euler Angle, etc.) and many other items
documented in later sections. The data sections contain lines of epoch and
attitude data.
An example CCSDS AEM file is shown below
CCSDS_AEM_VERS = 1.0
CREATION_DATE = 2002-11-04T17:22:31
ORIGINATOR = NASA/JPL
META_START
COMMENT This file was produced by M.R. Somebody, MSOO NAV/JPL, 2002 OCT 04.
COMMENT It is to be used for attitude reconstruction only.
COMMENT The relative accuracy of these attitudes is 0.1 degrees per axis.
OBJECT_NAME = MARS GLOBAL SURVEYOR
OBJECT_ID = 1996-062A
CENTER_NAME = mars barycenter
REF_FRAME_A = EME2000
REF_FRAME_B = SC_BODY_1
ATTITUDE_DIR = A2B
TIME_SYSTEM = UTC
START_TIME = 1996-11-28T21:29:07.2555
USEABLE_START_TIME = 1996-11-28T22:08:02.5555
USEABLE_STOP_TIME = 1996-11-30T01:18:02.5555
STOP_TIME = 1996-11-30T01:28:02.5555
ATTITUDE_TYPE = QUATERNION
QUATERNION_TYPE = LAST
INTERPOLATION_METHOD = hermite
INTERPOLATION_DEGREE = 7
META_STOP
DATA_START
1996-11-28T21:29:07.2555 0.56748 0.03146 0.45689 0.68427
1996-11-28T22:08:03.5555 0.42319 -0.45697 0.23784 0.74533
1996-11-28T22:08:04.5555 -0.84532 0.26974 -0.06532 0.45652
< intervening data records omitted here >
1996-11-30T01:28:02.5555 0.74563 -0.45375 0.36875 0.31964
DATA_STOP
META_START
COMMENT This block begins after trajectory correction maneuver TCM-3.
OBJECT_NAME = mars global surveyor
OBJECT_ID = 1996-062A
CENTER_NAME = MARS BARYCENTER
REF_FRAME_A = EME2000
REF_FRAME_B = SC_BODY_1
ATTITUDE_DIR = A2B
TIME_SYSTEM = UTC
START_TIME = 1996-12-18T12:05:00.5555
USEABLE_START_TIME = 1996-12-18T12:10:00.5555
USEABLE_STOP_TIME = 1996-12-28T21:23:00.5555
STOP_TIME = 1996-12-28T21:28:00.5555
ATTITUDE_TYPE = QUATERNION
QUATERNION_TYPE = LAST
META_STOP
DATA_START
1996-12-18T12:05:00.5555 -0.64585 0.018542 -0.23854 0.72501
1996-12-18T12:10:05.5555 0.87451 -0.43475 0.13458 -0.16767
1996-12-18T12:10:10.5555 0.03125 -0.65874 0.23458 -0.71418
< intervening records omitted here >
1996-12-28T21:28:00.5555 -0.25485 0.58745 -0.36845 0.67394
DATA_STOP
CCSDS files require many keywords and fields, some are required for all file
types, others are Situationally Required (SR) depending upon the type of file
(i.e. If ATTITUDE_TYPE = QUATERNION, then QUATERNION_TYPE must
be included). The tables below describe GMAT’s implementation starting with
header keywords
Keyword Required Description and Supported Values
CCSDS_AEM_VERS
Y Format version in the form of ‘x.y’, where
‘y’ is incremented for corrections and minor
changes, and ‘x’ is incremented for major
changes. This particular line must be the
first non-blank line in the file. In GMAT the
version must be set to 1.0. If the version is
not set to a supported version, then GMAT
throws an exception.
Example:
CCSDS_AEM_VERS =1.0
COMMENT
N Comments (allowed after AEM version
number and META_START and before a
data block of ephemeris lines). Each
comment line shall begin with this
keyword. GMAT does not use this field.
CREATION_DATE
Y File creation date/time in one of the
following formats: YYYY-MM-
DDThh:mm:ss[.d?d] or YYYY-
DDDThh:mm:ss[.d?d] where ‘YYYY’ is
the year, ‘MM’ is the two-digit month, ‘DD’
is the two-digit day, ‘DDD’ is the threedigit
day of year, ‘T’ is constant, ‘hh:mm:ss[.d?
d]’ is the UTC time in hours, minutes,
seconds, and optional fractional seconds.
As many ‘d’ characters to the right of the
period as required may be used to obtain
the required precision. All fields require
leading zeros. GMAT does not use this
field.
ORIGINATOR
Y Creating agency (value should be specified
in an ICD). GMAT does not use this field.
MetaData Keywords are described in the table below.
Keyword Required Description and Supported Values
META_START
Y The AEM message contains both
metadata and attitude ephemeris data; this
keyword is used to delineate the start of a
metadata block within the message
(metadata are provided in a block,
surrounded by ‘META_START’ and
‘META_STOP’ markers to facilitate file
parsing). This keyword must appear on a
line by itself.
COMMENT
N Comments allowed only at the beginning
of the Metadata section. Each comment
line shall begin with this keyword. GMAT
does not use this.
Example:
COMMENT This is a comment
OBJECT_NAME
Y Spacecraft name of the object
corresponding to the attitude data to be
given. There is no CCSDS-based
restriction on the value for this keyword,
but it is recommended to use names from
the SPACEWARN Bulletin, which
include the Object name and international
designator of the participant.
Example:
OBJECT_NAME = EUTELSAT
Note: GMAT does not use this field. In
GMAT, you associate a file with a
particular spacecraft by configuring a
particular spacecraft to use the file as
shown below.
Create Spacecraft aSat
aSat.Attitude = CCSDS-AEM
aSat.AttitudeFileName = myFile.aem
OBJECT_ID
Y Spacecraft identifier of the object
corresponding to the attitude data to be
given. See the AEM specification for
recommendations for spacecraft Ids.
GMAT does not use this field.
CENTER_NAME
N Origin of reference frame, which may be
a natural solar system body (planets,
asteroids, comets, and natural satellites),
including any planet barycenter or the
solar system barycenter, or another
spacecraft (in this the value for
‘CENTER_NAME’ is subject to the same
rules as for ‘OBJECT_NAME’). There is
no CCSDS-based restriction on the value
for this keyword, but for natural bodies it
is recommended to use names from the
NASA/JPL Solar System Dynamics
Group . GMAT does not use this field.
REF_FRAME_A
Y The name of the reference frame
specifying one frame of the
transformation, whose direction is
specified using the keyword
ATTITUDE_DIR. The full set of values is
enumerated in annex A of the AEM
standard, with an excerpt provided in the
‘Values / Examples’ column.
In GMAT, REF_FRAME_A can be any of
the following and must be different than
REF_FRAME_B: EME2000,
SC_BODY_1
Example:
REF_FRAME_A = EME2000
REF_FRAME_A = SC_Body_1
REF_FRAME_B
Y The name of the reference frame
specifying one frame of the
transformation, whose direction is
specified using the keyword
ATTITUDE_DIR. The full set of values is
enumerated in annex A of the AEM
standard, with an excerpt provided in the
‘Values / Examples’ column.
In GMAT, REF_FRAME_B can be any of
the following and must be different than
REF_FRAME_A: EME2000,
SC_BODY_1
Example:
REF_FRAME_A = EME2000
REF_FRAME_A = SC_Body_1
ATTITUDE_DIR
Y Rotation direction of the attitude
specifying from which frame the
transformation is to: A2B specifies a
transformation from the REF_FRAME_A
to the REF_FRAME_B; B2A specifies a
transformation from the REF_FRAME_B
to the REF_FRAME_A.
Examples:
ATTITUDE_DIR = A2B
ATTITUDE_DIR = B2A
TIME_SYSTEM
Y Time system used for both attitude
ephemeris data and metadata. GMAT
supports the following options: UTC
Example:
TIME_SYSTEM = UTC
START_TIME
Y Start of TOTAL time span covered by
attitude ephemeris data immediately
following this metadata block. The
START_TIME time tag at a new block of
attitude ephemeris data must be equal to
or greater than the STOP_TIME time tag
of the previous block. See the
CREATION_DATE specification for
detailed information on time formats.
Note: precision in the seconds place is
only preserved to a few microseconds.
Example:
START_TIME = 1996-12-
18T14:28:15.117
USEABLE_
START_TIME, N Optional start and end of USEABLE time
USEABLE_ span covered by attitude ephemeris data
STOP_TIME immediately following this metadata
block. To allow for proper interpolation
near the ends of the attitude ephemeris
data block, it may be necessary,
depending upon the interpolation method
to be used, to utilize these keywords with
values within the time span covered by
the attitude ephemeris data records as
denoted by the START/STOP_TIME time
tags. If this is provided, GMAT only uses
data in the USEABLE timespan for
interpolation. If it is not provided, GMAT
uses the data in the
START_TIME/STOP_TIME segment for
interpolation. See the CREATION_DATE
specification for detailed information on
time formats.
Example:
USEABLE_ START_TIME = 1996-12-
18T14:28:15.117
USEABLE_ STOP_TIME = 1996-12-
18T14:28:15.117
STOP_TIME
Y End of TOTAL time span covered by the
attitude ephemeris data immediately
following this metadata block. The
STOP_TIME time tag for the block of
attitude ephemeris data must be equal to
or less than the START_TIME time tag of
the next block. See the
CREATION_DATE specification for
detailed information on time formats.
Note: precision in the seconds place is
only preserved to a few microseconds.
Example:
STOP_TIME = 1996-12-18T14:28:15.117
ATTITUDE_TYPE
Y The format of the data lines in the
message. GMAT supports the following
types
ATTITUDE_TYPE = QUATERNION
ATTITUDE_TYPE = EULER_ANGLE
QUATERNION_TYPE
SR The placement of the scalar portion of the
quaternion (QC) in the attitude data. This
keyword is only used if
ATTITUDE_TYPE denotes quaternion
and in that case the field is required.
Example:
QUATERNION_TYPE = FIRST
QUATERNION_TYPE = LAST
EULER_ROT_SEQ
SR The rotation sequence of the Euler angles
that rotate from REF_FRAME_A to
REF_FRAME_B, or vice versa, as
specified using the ATTITUDE_DIR
keyword. This keyword is only used if
ATTITUDE_TYPE denotes EulerAngles
and in that case the field is required.
Example:
EULER_ROT_SEQ = 321
RATE_FRAME
N GMAT does not use this field.
INTERPOLATION
_METHOD N Recommended interpolation method for
attitude ephemeris data in the block
immediately following this metadata
block. Note. GMAT uses spherical linear
interpolation when ATTITUDE_TYPE =
QUATERNION. GMAT uses lagrange
interpolation for ATTITUDE_TYPE =
EULER_ANGLE.
Examples:
INTERPOLATION _METHOD =
LINEAR
INTERPOLATION _METHOD =
LAGRANGE
INTERPOLATION
_DEGREE SR Recommended interpolation degree for
attitude ephemeris data in the block
immediately following this metadata
block. It must be an integer value. This
keyword must be used if the
‘INTERPOLATION_METHOD’ keyword
is used. The field is only used for
Lagrange Interpolation and in that case
the value must be between 0 and 9. In the
case order is zero for Lagrange
interpolation, no interpolation is
performed, and the attitude returned is the
value immediately before the requested
epoch.
Example:
INTERPOLATION _DEGREE = 7
META_STOP
Y The end of a metadata block within the
message. The AEM message contains
both metadata and attitude ephemeris
data; this keyword is used to delineate the
end of a metadata block within the
message (metadata are provided in a
block, surrounded by ‘META_START’
and ‘META_STOP’ markers to facilitate
file parsing). This keyword must appear
on a line by itself.
Data Keywords are described in the table below.
Keyword Required Description and Supported Values
DATA_START The start of an attitude data block within the
Y message. The AEM message contains both
metadata and attitude ephemeris data; this
keyword is used to delineate the start of a data
block within the message (data are provided in
a block, surrounded by ‘DATA_START’ and
‘DATA_STOP’ markers to facilitate file
parsing). This keyword must appear on a line
by itself.
DATA_STOP
Y The end of an attitude data block within the
message. The AEM message contains both
metadata and attitude ephemeris data; this
keyword is used to delineate the end of a data
block within the message (data are provided in
a block, surrounded by ‘DATA_START’ and
‘DATA_STOP’ markers to facilitate file
parsing). This keyword must appear on a line
by itself.
QUATERNION
SR Required when ATTITUDE_TYPE =
QUATERNION. The general format of a
quaternion data line is: Epoch, QC, Q1, Q2, Q3
or Epoch, Q1, Q2, Q3, QC
Example:
2000-01-01T11:59:28.000 0.195286 -0.079460
0.3188764 0.92404936
EULER ANGLE
SR Required when ATTITUDE_TYPE =
EULER_ANGLE. The general format of an
Euler angle data line is: Epoch, X_Angle,
Y_Angle, Z_Angle.
Example:
2000-001T11:59:28.000 35.45409 -15.74726
18.803877
Propagate a spacecraft's attitude using a CCSDS AEM file
Create Spacecraft aSat ;
GMAT aSat.Attitude = CCSDS-AEM;
GMAT aSat.AttitudeFileName = ...
'../data/vehicle/ephem/ccsds/CCSDS_BasicEulerFile.aem'
Create Propagator aProp;
Create OrbitView a3DView
a3DView.Add = {aSat,Earth}
BeginMissionSequence;
Propagate aProp(aSat) {aSat.ElapsedSecs = 3600};
Precessing Spinner Model
The PrecessingSpinner attitude mode configures the attitude of a spacecraft to
have steady-state precession motion with respect to a specified vector defined in
the inertial frame. The spin axis must be provided in the spacecraft body frame.
To configure the spin axis of the spacecraft body, set the BodySpinAxis, which
is expressed in the body frame, and define the reference vector of the steady-
state precession motion using the NutationReferenceVector, which is expressed
in the inertial frame. To configure the initial attitude of the spacecraft, set
InitialPrecessionAngle to define the initial angle of the precession, set
InitialSpinAngle to define the initial angle of the spin, and set NutationAngle
to define the nutation angle which is constant. To configure the rate of
precession and spin rate, set PrecessingRate and SpinRate which are constant.
Note
The PrecessingSpinner model uses the cross product of the
BodySpinAxis axis and the inertial x-axis as a reference for the
initial attitude. To avoid an undefined attitude when the spin
axis is aligned, or nearly aligned, with the inertial x-axis, a
different reference vector is used in that case. In the event that
the cross product of BodySpinAxis and the inertial x-axis is
less than 1e-5, the inertial y-axis is used as the reference vector.
For further details see the engineering/mathematical
specifications.
The script example below shows how to configure a Spacecraft to have
PrecessingSpinner attitude mode where the body z-axis spins with respect to
the inertial z-axis. PrecessionRate is set to 1 deg./sec., InitialPrecessionAngle
is set to 0 deg./sec., SpinRate is set to 2 deg./sec., InitialSpinAngle is set to 0
deg./sec., and NutationAngle is set to 30 deg.
Create Spacecraft aSat;
GMAT aSat.Attitude = PrecessingSpinner;
GMAT aSat.NutationReferenceVectorX = 0;
GMAT aSat.NutationReferenceVectorY = 0;
GMAT aSat.NutationReferenceVectorZ = 1;
GMAT aSat.BodySpinAxisX = 0;
GMAT aSat.BodySpinAxisY = 0;
GMAT aSat.BodySpinAxisZ = 1;
GMAT aSat.InitialPrecessionAngle = 0;
GMAT aSat.PrecessionRate = 1;
GMAT aSat.NutationAngle = 30;
GMAT aSat.InitialSpinAngle = 0;
GMAT aSat.SpinRate = 2;
Create OrbitView OrbitView1;
OrbitView1.Add = {aSat, Earth}
OrbitView1.ViewPointReference = Earth
OrbitView1.ViewPointVector = [ 30000 0 0 ]
Create Propagator aProp
aProp.MaxStep = 10
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedSecs = 12000.0}

## Spacecraft Ballistic/Mass Properties

Spacecraft Ballistic/Mass Properties
Spacecraft Ballistic/Mass Properties — The physical properties of the spacecraft
Description
The Spacecraft ballistic and mass properties include the drag and SRP areas and
coefficients as well as the spacecraft dry mass. These quantities are used
primarily in orbital dynamics modelling. GMAT supports a spherical SRP model,
and higher fidelity SRP file option.
See Also: Propagate, Propagator,Spacecraft
Fields
Field Description
Cd
The coefficent of drag used to compute the
acceleration due to drag.
Data Type Real
Allowed Values Real >= 0
Access set, get
Default Value 2.2
Units dimensionless
Interfaces GUI, script
Cr
The coefficent of reflectivity used to compute the
acceleration due to SRP. A value of zero means the
spacecraft is translucent to incoming radiation. A
value of 1.0 indicates all radiaion is absorbed and all
the force is transmitted to the spacecraft. A value of
2.0 indicates all radiation is reflected and twice the
force is transmitted to the spacecraft.
Data Type Real
Allowed Values 0 <= Cr <= 2.0
Access set, get
Default Value 1.8
Units dimensionless
Interfaces GUI, script
Drag Area
The area used to compute acceleration due to
atmospheric drag.
Data Type Real
Allowed Values Real > = 0
Access set, get
Default Value 15
Units m^2
Interfaces GUI, script
DryMass
The dry mass of the Spacecraft (does not include fuel
mass).
Data Type Real
Allowed Values Real >=0
Access set, get
Default Value 850
Units kg
Interfaces GUI, script
SPADSRPFile
Name ( and optionally path information) of SPAD file.
Data Type String
Allowed Values valid path and SPAD file
Access set
Default Value N/A
Units N/A
Interfaces GUI, script
SPADSRPScaleFactor
Scale factor applied to SRP force when using a
SPADModel in the propagation.
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 1
Units dimensionless
Interfaces GUI, script
SRPArea
The area used to compute acceleration due to solar
radiation pressure.
Data Type Real
Allowed Values Real > 0
Access set, get
Default Value 1
Units m^2
Interfaces GUI, script
GUI
The GUI interface for ballistic and mass properties is contained on the
Ballistic/Mass tab of the Spacecraft resource. You can enter physical properties
such as the drag and SRP areas and coefficients and the Spacecraft dry mass
which are used in orbital dynamics modelling. GMAT supports a spherical SRP
model and a SPAD (Solar Pressure and Aerodynamic Drag) file.
Remarks
Configuring Ballistic and Mass Properties for the Spherical
Model
GMAT supports a cannonball model for drag and SRP modeling. In the
cannonball model, the area is assumed to be independent of the spacecraft’s
orientation with respect to the local velocity vector and the sun vector. For more
details on the computation and configuration of drag and SRP models see the
Force Model documentation.
Configuring Ballistic and Mass Properties for the SRP File
The (SPAD) SRP file can be used for high fidelity SRP modelling taking into
account the physical properties of the spacecraft (shape and reflectivity) and the
spacecraft attitude. SPAD stands for Solar Pressure and Aerodynamic Drag.
SPAD files are tabulated data that contain the spacecraft area scaled by physical
properties like Cr including specular, diffuse, and reflective properties. Data is
expressed as a function of azimuth and elevation in the spacecraft body frame.
Note: the azimuth and elevation tabulated on the file are the azimuth and
elevation of the vector from the Sun, to the Spacecraft, expressed in the body
frame. To compute the SRP acceleration, GMAT computes the sun vector’s
azimuth and elevation in the spacecraft body frame, and then interpolates the
SPAD data using bi-linear interpolation. Note that this formulation results in an
attitude dependent SRP acceleration. For more details on the computation and
configuration of drag and SRP models see the Force Model documentation.
Caution
When using a SPAD SRP file, GMAT uses the attitude defined
on the Spacecraft resource to compute the Sun's positon in the
body frame. If the attitude uses a coordinate system with Axes
set to ObjectReferenced, and those axes refer back to the
Spacecraft orbit state (i.e. VNB or LVLH systems), GMAT
holds the attitude constant over a given integration step. In
those cases, we recommend carefully choosing a maximum
step size small enough to ensure the resulting approximation is
acceptable for your application.
A valid SPAD file header, and the first three lines of data are shown below for
illustrative purposes. Note, GMAT does not use all values provide on the file and
GMAT's usage of SPAD files is described in detail in the table below the
example.
Version : 4.21
System : sphericalSat
Analysis Type : Area
Pixel Size : 5
Spacecraft Size : 436.2
Pressure : 1
Center of Mass : (50.9, 184.9, -49)
Current time : May 7, 2009 15:53:38.00
Motion : 1
Name : Azimuth
Method : Step
Minimum : -180
Maximum : 180
Step : 5
Motion : 2
Name : Elevation
Method : Step
Minimum : -90
Maximum : 90
Step : 5
: END
Record count : 2701
AzimuthElevatio Force(X) Force(Y) Force(Z)
degrees degrees m^2 m^2 m^2
------- ------- --------- --------- --------- ---------
-180.00 -90.00 -0.00000000000000 -0.00000000000000 -8.94500000000000
-180.00 -85.00 -0.77960811887780 -0.00000000000000 -8.91096157443066
-180.00 -80.00 -1.55328294923069 -0.00000000000000 -8.80910535069420
A SPAD file contains three sections as illustrated below. Data specifications for
items in each section are described in the tables below
A SPAD file header may contain many fields but only a few are used by GMAT
as described below. Other fields are ignored.
Keyword Required Description and Supported Values
Analysis Type
Y The SPAD software can creates files with
Analysis Types of Solar Pressure, Area, and
Drag. GMAT only supports the Area option.
Example:
Analysis Type : Area
Pressure
N SPAD supports the ability to apply a pressure
scale factor for SRP files. GMAT does not read
this value, but the SRP properties on the file
have been scaled by the Pressure factor. The
value is usually “1”. However, when not 1, it is
possible to apply an SRP scale factor twice, once
from the value applied in SPAD, and once from
SPADSRPScaleFactor. Care should be taken to
ensure that if the desired scale factor was
applied during file creation that it is not
reapplied in GMAT.
The SPAD file Motion Data section describes the data contained in the body of
the file. The Motion Data fields used by GMAT are described below. Others are
ingored.
Keyword Required Description and Supported Values
Motion
Y Together, the Motion and Name fields specify
the type of data in the first two columns of the
body of the file. GMAT currently supports
Azimuth and Elevation Motion only (no
articulating appendages) and requires that the
first Motion is Azimuth and the second Motion
is Elevation as shown below.
Examples:
Motion : 1
Name : Azimuth
and
Motion : 2
Name : Elevation
Name
Y Together, the Motion and Name fields specify
the type of data in the first two columns of the
body of the file. GMAT currently supports
Azimuth and Elevation Motion only (no
articulating appendages) and requires that the
first Motion is Azimuth and the second Motion
is Elevation as shown below.
Examples:
Motion : 1
Name : Azimuth
and
Motion : 2
Name : Elevation
Method
Y The step size in the independent variable. The
only supported value is Step.
Example:
Motion : 1
Method : Step
Maximum
Y The maximum value for an independent variable
(Motion Type). For Azimuth, Maximum must
be 180, and for Elevation Maximum must be 90.
Example:
Motion : 1
Name : Azimuth
Maximum : 180
Motion : 2
Name : Elevation
Maximum : 90
Minimum
Y The minimum value for an independent
variable. (Motion Type). For Azimuth,
minimum must be -180, and for Elevation
minimum must be -90.
Example:
Motion : 1
Name : Azimuth
Minimum : -180
Motion : 2
Name : Elevation
Minimum : -90
Step
Y The step size for the independent variable
(Motion Type). If Step does not divide evenly
into the variable range, then errors may occur
because the maximum and/or minimum values
may not be on the file.
Example:
Motion : 1
Step : 15
Record count
Y Record count is the number of rows of data in
the data segment. Record count =
(360/(Azimuth Step) +1)*(180/(Elevation Step)
+1).
Example:
Record count : 325
The SPAD file data block contains tabulated acceleration data as described
below.
Keyword Required Description and Supported Values
Azimuth
Y Azimuth data column. Must be first column in
the data. Units must be degrees. Azimuth is the
azimuth of the vector from spacecraft to sun,
expressed in the body frame: atan2(ySun,xSun)).
Example:
AzimuthElevatio
degrees degrees
------- -------
-180.00 -90.00
-180.00 -75.00
-180.00 -60.00
Elevation
N Elevation data column. Must be second column
in the data. Units must be degrees. Elevation is
the elevation of the vector from spacecraft to
sun, expressed in the body frame:
atan2(zSun,sqrt(xSun^2 + ySun^2)).
Example:
AzimuthElevatio
degrees degrees
------- -------
-180.00 -90.00
-180.00 -75.00
-180.00 -60.00
Force(*)
N Area vector columns. Must be columns 3-5 in
the data. Quantities must be in base units of
m^2,mm^2,cm^2,in^2,ft^2. If another unit is
provided in the header lines, an exception is
thrown. The area vector is the direction of the
resulting SRP force, scaled by area and Cr
properties.
Example: See code listing above.
Total Mass Computation
The TotalMass property of a Spacecraft is a read-only property that is the sum
of the DryMass value and the sum of the fuel mass in all attached fuel tanks.
GMAT’s propagators will not allow the total mass of a spacecraft to be negative.
However, GMAT will allow the mass of a ChemicalTank to be negative. See the
ChemicalTank documentation for details.
Examples
Configure physical properties for a spherical SRP model.
Create Spacecraft aSpacecraft
aSpacecraft.Cd = 2.2
aSpacecraft.Cr = 1.8
aSpacecraft.DragArea = 40
aSpacecraft.SRPArea = 35
aSpacecraft.DryMass = 2000
Create Propagator aPropagator
BeginMissionSequence
Propagate aPropagator(aSpacecraft, {aSpacecraft.ElapsedSecs = 600})
Configure a SPAD SRP model.
Create Spacecraft aSpacecraft;
aSpacecraft.DryMass = 2000
aSpacecraft.SPADSRPFile = '..\data\vehicle\spad\SphericalModel.spo'
aSpacecraft.SPADSRPScaleFactor = 1;
Create ForceModel aFM;
aFM.SRP = On;
aFM.SRP.SRPModel = SPADFile
Create Propagator aProp;
aProp.FM = aFM;
BeginMissionSequence
Propagate aProp(aSpacecraft) {aSpacecraft.ElapsedDays = 0.2}

## Spacecraft Epoch

Spacecraft Epoch
Spacecraft Epoch — The spacecraft epoch
Description
The epoch of a Spacecraft is the time and date corresponding to the specified
orbit state. See the Spacecraft Orbit State section for interactions between the
epoch, coordinate system, and spacecraft state fields.
See Also: Spacecraft
Caution
GMAT’s Modified Julian Date (MJD) format differs from that
of other software. The Modified Julian format is a constant
offset from the full Julian date (JD):
MJD = JD - offset
GMAT uses a non-standard offset, as shown in the following
table.
Epoch Type GMAT common
reference epoch 05 Jan 1941 17 Nov 1858
12:00:00.000 00:00:00.000
Modified Julian 2430000.0 2400000.5
offset
Fields
Field Description
DateFormat
The time system and format of the Epoch field. In
the GUI, this field is called EpochFormat.
Data Enumeration
Type
Allowed A1ModJulian, TAIModJulian,
Values UTCModJulian, TTModJulian,
TDBModJulian, A1Gregorian,
TAIGregorian, TTGregorian,
UTCGregorian, TDBGregorian
Access set only
Default TAIModJulian
Value
Interfaces GUI, script
Epoch
The time and date corresponding to the specified
orbit state.
Data Time
Type
Allowed Gregorian: 04 Oct 1957
Values 12:00:00.000 <= Epoch <= 28 Feb
2100 00:00:00.000
Modified Julian: 6116.0 <= Epoch <=
58127.5
Access set only
Default 21545
Value
Interfaces GUI, script
A1ModJulian
The Spacecraft orbit epoch in the A.1 system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 21545.00000039794
Units Days
Interfaces script
Epoch.A1ModJulian
The spacecraft orbit epoch in the A.1 system and the
Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 21545.00000039794
Units Days
Interfaces none
CurrA1MJD
This field has been deprecated and should no longer
be used.
The current epoch in the A1ModJulian format. This
field can only be used within the mission sequence.
Data Type Time
Allowed 6116.0 <= CurrA1MJD <= 58127.5
Values
Access get, set (mission sequence only)
Default converted equivalent of 21545
Value Modified Julian (TAI)
Interfaces script only
A1Gregorian
The Spacecraft orbit epoch in the A.1 system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 01 Jan 2000 12:00:00.034
Units N/A
Interfaces GUI, script
TAIGregorian
The Spacecraft orbit epoch in the TAI system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 01 Jan 2000 12:00:00.000
Units Gregorian date
Interfaces GUI, script
TAIModJulian
The Spacecraft orbit epoch in the TAI system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 21545
Units See A1ModJulian
Interfaces GUI, script
TDBGregorian
The Spacecraft orbit epoch in the TDB system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 01 Jan 2000 12:00:32.184
Units See A1Gregorian
Interfaces GUI, script
TDBModJulian
The Spacecraft orbit epoch in the TDB system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 21545.00037249916
Units See A1ModJulian
Interfaces GUI, script
TTGregorian
The Spacecraft orbit epoch in the TT system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 01 Jan 2000 12:00:32.184
Units See A1Gregorian
Interfaces GUI, script
TTModJulian
The Spacecraft orbit epoch in the TT system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 21545.0003725
Units See A1ModJulian
Interfaces GUI, script
UTCGregorian
The Spacecraft orbit epoch in the UTC system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 01 Jan 2000 11:59:28.000
Units See A1Gregorian
Interfaces GUI, script
UTCModJulian
The Spacecraft orbit epoch in the UTC system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get (mission sequence only)
Default Value 21544.99962962963
Units See A1ModJulian
Interfaces GUI, script
Epoch.A1Gregorian
The Spacecraft orbit epoch in the A.1 system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 01 Jan 2000 12:00:00.034
Units N/A
Interfaces GUI, script
Epoch.TAIGregorian
The Spacecraft orbit epoch in the TAI system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value DefaultValue
Units 01 Jan 2000 12:00:00.000
Interfaces GUI, script
Epoch.TAIModJulian
The Spacecraft orbit epoch in the TAI system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch.A1ModJulian
Access set, get
Default Value 21545
Units See Epoch.A1ModJulian
Interfaces GUI, script
Epoch.TDBGregorian
The Spacecraft orbit epoch in the TDB system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 01 Jan 2000 12:00:32.184
Units See Epoch.A1Gregorian
Interfaces GUI, script
Epoch.TDBModJulian
The Spacecraftorbit epoch in the TDB system and
the Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 21545.00037249916
Units See Epoch.A1ModJulian
Interfaces GUI, script
Epoch.TTGregorian
The Spacecraft orbit epoch in the TT system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 01 Jan 2000 12:00:32.184
Units See Epoch.A1Gregorian
Interfaces GUI, script
Epoch.TTModJulian
The Spacecraftorbit epoch in the TT system and the
Modified Julian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 21545.0003725
Units See Epoch.A1ModJulian
Interfaces GUI, script
Epoch.UTCGregorian
The Spacecraftorbit epoch in the UTC system and
the Gregorian format.
Data Type String
Allowed Values See Epoch
Access set, get
Default Value 01 Jan 2000 11:59:28.000
Units See Epoch.A1Gregorian
Interfaces GUI, script
Epoch.UTCModJulian
The Spacecraft orbit epoch in the UTC system and
the Modified Julian format.
Data Type String
Allowed Values Range
Access See Epoch
Default Value 21544.99962962963
Units See Epoch.A1ModJulian
Interfaces GUI, script
GUI
A change in EpochFormat causes an immediate update to Epoch to reflect the
chosen time system and format.
Remarks
GMAT supports five time systems or scales and two formats:
A.1 USNO atomic time; GMAT’s internal
time system
TAI International Atomic Time
TDB Barycentric Dynamical Time
TT Terrestrial Time
UTC Coordinated Universal Time
Gregorian
Text with the following format: dd mmm
yyyy HH:MM:SS.FFF
dd two-digit day of month
mmm first three letters of month
yyyy four-digit year
HH two-digit hour
MM two-digit minute
SS two-digit second
FFF three-digit fraction of second
Modified Julian Floating-point number of days from a
reference epoch. In GMAT, the
reference epoch is 05 Jan 1941
12:00:00.000 (JD 2430000.0).
The epoch can be set in multiple ways. The default method is to set the
DateFormat field to the desired time system and format, then set the Epoch
field to the desired epoch. This method cannot be used to get the epoch value,
such as on the right-hand side of an assignment statement.
aSat.DateFormat = UTCGregorian
aSat.Epoch = '18 May 2012 12:00:00.000'
An alternate method is to specify the DateFormat in the parameter name. This
method works in both “get” and “set” modes.
aSat.Epoch.UTCGregorian = '18 May 2012 12:00:00.000'
Report aReport aSat.Epoch.UTCGregorian
A third method can be used in “get” mode everywhere, but in “set” mode only in
the mission sequence (i.e. after the BeginMissionSequence command).
aSat.UTCGregorian = '18 May 2012 12:00:00.000'
Report aReport aSat.UTCGregorian
GMAT uses the A.1 time system in the Modified Julian format for its internal
calculations. The system converts all other systems and formats on input and
again at output.
Leap Seconds
When converting to and from the UTC time system, GMAT includes leap
seconds as appropriate, according to the tai-utc.dat data file from the IERS.
This file contains the conversion between TAI and UTC, including all leap
seconds that have been added or announced.
GMAT applies the leap second as the last second before the date listed in the
tai-utc.dat file, which historically has been either January 1 or July 1. In the
Gregorian date format, the leap second appears as a “60th second”: for example,
“31 Dec 2008 23:59:60.000”. From the International Astronomical Union's
Standards of Fundamental Astronomy "SOFA Time Scale and Calendar Tools"
documentation: "Note that UTC has to be expressed as hours, minutes and
seconds (or at least in seconds in a given day) if leap seconds are to be taken
into account in the correct manner. In particular, it is inappropriate to express
UTC as a Julian Date, because there will be an ambiguity during a leap second
so that for example 1994 June 30 23:59:60:0 and 1994 July 1 00:00:00:0 would
both come out as MJD 49534.00000 and because subtracting two such JDs
would not yield the correct interval in cases that contain leap seconds." For this
reason, we discourage use of the UTC modified Julian system, and recommend
using UTC Gregorian when a UTC time system is required.
For epochs prior to the first entry in the leap-second file, the UTC and TAI time
systems are considered identical (i.e. zero leap seconds are added). For epochs
after the last entry, the leap second count from the last entry is used.
The tai-utc.dat file is periodically updated by the IERS when new leap
seconds are announced. The latest version of this file can always be found at
http://maia.usno.navy.mil/ser7/tai-utc.dat. To replace it, download the
latest version and replace GMAT’s file in the location <GMAT>/data/time/tai-
utc.dat, where <GMAT> is the install directory of GMAT on your system.
Examples
Setting the epoch for propagation
Create Spacecraft aSat
aSat.DateFormat = TAIModJulian
aSat.Epoch = 25562.5
Create ForceModel aFM
Create Propagator aProp
aProp.FM = aFM
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 1}
Plotting and reporting the epoch (syntax #1)
Create Spacecraft aSat
aSat.DateFormat = A1Gregorian
aSat.Epoch = '12 Jul 2015 08:21:45.921'
Create XYPlot aPlot
aPlot.XVariable = aSat.UTCModJulian
aPlot.YVariables = aSat.Earth.Altitude
Create Report aReport
aReport.Add = {aSat.UTCGregorian, aSat.EarthMJ2000Eq.ECC}
Plotting and reporting the epoch (syntax #2)
Create Spacecraft aSat
aSat.DateFormat = TTGregorian
aSat.Epoch = '01 Dec 1978 00:00:00.000'
Create XYPlot aPlot
aPlot.XVariable = aSat.Epoch.TTModJulian
aPlot.YVariables = aSat.Earth.RMAG
Create Report aReport
aReport.Add = {aSat.Epoch.A1Gregorian, aSat.Earth.RMAG}

## Spacecraft Hardware

Spacecraft Hardware
Spacecraft Hardware — Add hardware to a spacecraft
Description
The hardware fields allow you to attach pre-configured hardware models to a
spacecraft. Current models include ChemicalTank, ChemicalThruster
,ElectricTank, and ElectricThruster. Before you attach a hardware model to a
Spacecraft, you must first create the model.
See Also: ChemicalTank, ChemicalThruster,ElectricTank, ElectricThruster
Fields
Field Description
Tanks
This field is used to attach FuelTank(s) to a Spacecraft. In a script
command, an empty list, e.g., DefaultSC.Tanks={}, is allowed and
is used to indicate that no FuelTank(s) is attached to the spacecraft.
Data Type Reference Array
Allowed A list of ChemicalTanks and Chemical
Values Thrusters.
Access set
Default Value N/A
Units N/A
Interfaces GUI, script.
Thrusters
This field is used to attach Thruster(s) to a Spacecraft. In a script
command, an empty list, e.g., DefaultSC.Thrusters={}, is allowed
and is used to indicate that no Thrusters are attached to the
spacecraft.
Data Type Reference Array
Allowed A list of ChemicalThrusters and
Values ElectricThrusters.
Access set
Default Value N/A
Units N/A
Interfaces GUI, script
GUI
There are two spacecraft hardware items, the FuelTank and the Thruster, that
can be attached to a Spacecraft. Here, we describe the method used to create and
then attach these items to a Spacecraft. For details on how to configure the
FuelTank and Thruster resources, see the help for the individual hardware
item. Note the discussion below uses a chemical system as an example but
applies equally to electric systems.
As shown below, to add a ChemicalTank to your script, highlight the
Hardware resource and then right click to add a ChemicalTank.
To add a Thruster to your script, highlight the Hardware resource and then
right click to add a Thruster.
Thus far, we have created both a ChemicalTank and a ChemicalThruster.
Next, we attach both the ChemicalTank and the ChemicalThruster to a
particular Spacecraft. To do this, double click on the desired Spacecraft under
the Spacecraft resource to bring up the associated GUI panel. Then click on the
Tanks tab to bring up the following GUI display.
Next, select the desired ChemicalTank and use the right arrow button to attach
the ChemicalTank to the Spacecraft as shown below. Then click the Apply
button.
Similarly, to attach a ChemicalThruster to a Spacecraft, double click on the
desired Spacecraft under the Spacecraft resource and then select the Actuators
tab. Then select the desired ChemicalThruster and use the right arrow to attach
the ChemicalThruster to the Spacecraft as shown below. Finally, click the
Apply button.

Remarks
To use a Thruster to apply a finite burn to a Spacecraft, additional steps are
required. For example, when you create the ChemicalThruster resource, you
have to associate a ChemicalTank with the ChemicalThruster. For details on
this and related matters, see the help for the ChemicalTank,
ChemicalThruster, and FiniteBurn resources.
Examples
Create a default Spacecraft. Create ChemicalTank and ChemicalThruster
resources and attach them to the Spacecraft.
% Create default Spacecraft, ChemicalTank, and Thruster Resources
Create Spacecraft DefaultSC
Create ChemicalTank FuelTank1
Create ChemicalThruster Thruster1
% Attach ChemicalTank and Thruster to the spacecraft
DefaultSC.Thrusters = {Thruster1}
DefaultSC.Tanks = {FuelTank1}
BeginMissionSequence

## Spacecraft Navigation

Spacecraft Navigation
Spacecraft Navigation — There are a number of Spacecraft fields that are used
exclusively to support GMAT's navigation capability.
Description
When using GMAT's navigation capabilities, certain Spacecraft parameters can
be "solved-for." As discussed in the Spacecraft Ballistic/Mass Properties section,
the Spacecraft ballistic and mass properties include the coefficient of
reflectivity, Cr, and the coefficient of drag, Cd. As discussed in the Spacecraft
Orbit State section, you can specify the CartesianState, i.e., the X, Y, Z
position (km), and the Vx, Vy, Vz velocity (km/s) of a Spacecraft. As part of
GMAT's navigation capability, GMAT can ingest measurements and estimate
("solve-for") values for Cr, Cd, and either the CartesianState, or
KeplerianState.
See Also: BatchEstimatorInv
Fields
Field Description
AddHardware
List of Antenna, Transmitter, Receiver, and
Transponder objects attached to a Spacecraft
Data Antenna, Transmitter, Receiver, or
Type Transponder object
Allowed Any user defined Antenna,
Values Transmitter, Receiver, or
Transponder object
Access set
Default None
Value
Units N/A
Interfaces script
CdSigma
Standard deviation of the coefficient of reflectivity,
Cd. This field is only used if the
UseInitialCovariance field of the
BatchEstimatorInv resource is set to True and Cd is
being solved for.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e70
Units dimensionless
Interfaces script
CrSigma
Standard deviation of the coefficient of reflectivity,
Cr. This field is only used if the
UseInitialCovariance field of the
BatchEstimatorInv resource is set to True and Cr is
being solved for.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 1e70
Units dimensionless
Interfaces script
OrbitErrorCovariance
State 6x6 error covariance matrix. If CartesianState
is estimated, this must be a Cartesian covariance. If
KeplerianState is estimated, this must be a Keplerian
covariance. Regardless of choice of spacecraft
coordinate system, the covariance must be specified
in the EarthMJ2000Eq coordinate system.
This field is only used if the UseInitialCovariance of
the BatchEstimatorInv resource is set to True.
Data Real Matrix
Type
Allowed 6x6 positive definite symmetric Array
Values
Access set
Default 6x6 diagonal matrix with 1e70 in all
Value diagonal entries.
Units For Cartesian elements: covariance
matrix where position is specified in km
and velocity in km/s. (Thus, first three
diagonal elements have units km^2 and
last three diagonal elements have units
(km/s)^2)
For Keplerian elements: covariance
matrix in km and degrees (For example,
the SMA element of the matrix has units
km^2 and the INC element has units
deg^2). The order of Keplerian elements
is (SMA, ECC, INC, RAAN, AOP,
MA). See the Remarks section for
additional notes.
Interfaces script
SolveFors
List of fields to be solved for. This list must at least
include either CartesianState or KeplerianState (but
not both). For example, Cr cannot be the only
parameter solved for.
Data Type StringArray
Allowed CartesianState, KeplerianState, Cr,
Values Cd
Access set
Default Value None
Units N/A
Interfaces script
Remarks
When estimating CartesianState, the input OrbitErrorCovariance matrix must
represent a Cartesian covariance, and when estimating KeplerianState the
OrbitErrorCovariance must represent a Keplerian covariance. Note that
Keplerian covariance input employs Mean Anomaly (MA) instead of True
Anomaly. The current release of GMAT only supports input of Keplerian orbit
elements using TA and does not permit explicitly setting an initial MA.
For more details, see the section called “UseInitialCovariance Restrictions” in
the Batch Estimator resource.
Examples
Solve for Cr and the spacecraft Cartesian state.
Create Spacecraft Sat
Create BatchEstimatorInv bat
Sat.SolveFors = {CartesianState, Cr}
%User must create a TrackingFileSet
%and set up bat appropriately
BeginMissionSequence
RunEstimator bat
Solve for Cd and the spacecraft Cartesian state assuming that the a priori
information is included in the estimation state vector.
Create Spacecraft Sat
Sat.SolveFors = {CartesianState, Cd}
Create BatchEstimatorInv bat
bat.UseInitialCovariance= True
%User must create a TrackingFileSet
%and set up bat appropriately
Create Array Initial_6x6_covariance[6,6]
BeginMissionSequence
Initial_6x6_covariance = ...
diag([1e-6 1e70 1e70 1e70 1e70 1e70]) %X pos known very well
Sat.OrbitErrorCovariance = Initial_6x6_covariance
Sat.CrSigma = 1e-6 %Cr known very well
RunEstimator bat

## Spacecraft Orbit State

Spacecraft Orbit State
Spacecraft Orbit State — The orbital initial conditions
Description
GMAT supports a suite of state types for defining the orbital state, including
Cartesian and Keplerian, among others. In addtion, you can define the orbital
state in different coordinate systems, for example EarthMJ2000Eq and
EarthFixed. GMAT provides three general state types that can be used with any
coordinate system: Cartesian, SphericalAZFPA, and SphericalRADEC. There
are three additional state types that can be used with coordinate systems centered
at a celestial body: Keplerian, ModifiedKeplerian, and Equinoctial.
In the section called “Remarks” below, we describe each state type in detail
including state-type definitions, singularities, and how the state fields interact
with the CoordinateSystem and Epoch fields. There are some limitations when
setting the orbital state during initialization, which are discussed in the section
called “Remarks”. We also include examples for setting each state type in
commonly used coordinate systems.
See Also: Spacecraft, Propagator, and Spacecraft Epoch
Fields
Field Description
AltEquinoctialP
A measure of the orientation of the orbit. AltEquinoctialP
and AltEquinoctialQ together govern how an orbit is
oriented. AltEquinotialP = sin(INC/2)*sin(RAAN).
Data Type Real
Allowed Values -1 ≤ AltEquinoctialP ≤ 1
Access set, get
Default Value 0.08982062789020774
Units (None)
Interfaces GUI, script
AltEquinoctialQ
A measure of the orientation of the orbit. AltEquinoctialP
and AltEquinoctialQ together govern how an orbit is
oriented. AltEquinotialP = sin(INC/2)*cos(RAAN).
Data Type Real
Allowed Values -1 ≤ AltEquinoctialQ ≤ 1
Access set, get
Default Value 0.06674269576352432
Units (None)
Interfaces GUI, script
AOP
The orbital argument of periapsis expressed in the
coordinate system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < AOP < ∞
Access set, get
Default Value 314.1905515359921
Units deg.
Interfaces GUI, script
AZI The orbital velocity azimuth expressed in the coordinate
system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < AZI < ∞
Access set, get
Default Value 82.37742168155043
Units deg.
Interfaces GUI, script
BrouwerLongAOP Brouwer-Lyddane long-term averaged (short-term
averaged) mean argument of periapsis.
BrouwerShortAOP
Data Type Real
Allowed -∞ <
Values BrouwerLongAOP/BrouwerShortAOP
< ∞
Access set, get
Default Conversion from default Cartesian state
Value
Units deg
Interfaces GUI, script
BrouwerLongECC Brouwer-Lyddane long-term averaged (short-term
averaged) mean eccentricity.
BrouwerShortECC
Data Type Real
Allowed 0 ≤
Values BrouwerLongECC/BrouwerShortECC
≤ 0.99
Access set, get
Default Conversion from default Cartesian state
Value
Units N/A
Interfaces GUI, script
BrouwerLongINC Brouwer-Lyddane long-term averaged (short-term
BrouwerShortINC averaged) mean inclination.
Data Type Real
Allowed 0 ≤
Values BrouwerLongINC/BrouwerShortINC
180
Access set, get
Default Conversion from default Cartesian state
Value
Units deg
Interfaces GUI, script
BrouwerLongMA Brouwer-Lyddane long-term averaged (short-term
averaged) mean MA (mean anomaly).
BrouwerShortMA
Data Type Real
Allowed -∞ <
Values BrouwerLongMA/BrouwerShortMA
∞
Access set, get
Default Conversion from default Cartesian state
Value
Units deg
Interfaces GUI, script
BrouwerLongRAAN Brouwer-Lyddane long-term averaged (short-term
averaged) mean RAAN (right ascension of the ascending
BrouwerShortRAAN node).
Data Real
Type
Allowed -∞ <
Values BrouwerLongRAAN/BrouwerShortRAAN
< ∞
Access set, get
Default Conversion from default Cartesian state
Value
Units deg
Interfaces GUI, script
BrouwerLongSMA Long-term averaged (short-term averaged) mean semi-
major axis.
BrouwerShortSMA
Data Type Real
Allowed Brouwer*SMA > 3000/(1-
Values Brouwer*ECC)
Access set, get
Default Value Conversion from default Cartesian state
Units km
Interfaces GUI, script
CoordinateSystem
The coordinate system with respect to which the orbital
state is defined. The CoordinateSystem field is
dependent upon the DisplayStateType field. If the
coordinate system chosen by the user does not have a
gravitational body at the origin, then the state types
Keplerian, ModifiedKeplerian, and Equinoctial are not
permitted.
Data Type String
Allowed Values CoordinateSystem resource
Access set
Default Value EarthMJ2000Eq
Units N/A
Interfaces GUI, script
DEC
The declination of the orbital position expressed in the
coordinate system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -90 ≤ DEC ≤ 90
Access set, get
Default Value 10.37584492005105
Units deg
Interfaces GUI, script
DECV
The declination of orbital velocity expressed in the
coordinate system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -90 ≤ DECV ≤ 90
Access set, get
Default Value 7.747772036108118
Units deg
Interfaces GUI, script
Delaunayg
Delaunay "g" element, identical to AOP, expressed in the
coordinate system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < Delaunayg < ∞
Access set, get
Default Value 314.1905515359921
Units deg
Interfaces GUI, script
DelaunayG
Delaunay "G" element, the magnitude of the orbital
angular momentum, expressed in the coordinate system
chosen in the CoordinateSystem field.
Data Type Real
Allowed Values 0 ≤ DelaunayG < ∞
Access set, get
Default Value 53525.52895581695
Units km2/s
Interfaces GUI, script
Delaunayh
Delaunay "h" element, identical to RAAN, expressed in
the coordinate system chosen in the CoordinateSystem
field.
Data Type Real
Allowed Values -∞ < Delaunayh < ∞
Access set, get
Default Value 306.6148021947984
Units deg
Interfaces GUI, script
DelaunayH
Delaunay "H" element, the z-component of the orbital
angular momentum vector, expressed in the coordinate
system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < Delaunayl < ∞
Access set, get
Default Value 52184.99999999999
Units km2/s
Interfaces GUI, script
Delaunayl
Delaunay "ℓ" element, identical to the mean anomaly,
expressed in the coordinate system chosen in the
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < Delaunayl < ∞
Access set, get
Default Value 97.10782663991999
Units deg
Interfaces GUI, script
DelaunayL
Delaunay "L" element, related to the two-body orbital
energy, expressed in the coordinate system chosen in the
CoordinateSystem field.
Data Type Real
Allowed Values 0 ≤ DelaunayL < ∞
Access set, get
Default Value 53541.66590560955
Units km2/s
Interfaces GUI, script
DisplayStateType
The orbital state type displayed in the GUI. Allowed state
types are dependent upon the selection of
CoordinateSystem. For example, if the coordinate system
does not have a celestial body at the origin, Keplerian
ModifiedKeplerian, and Equinoctial are not allowed
options for DisplayStateType.
Data String
Type
Allowed Cartesian, Keplerian, ModifiedKeplerian
Values SphericalAZFPA, SphericalRADEC, or
Equinoctial
Access set
Default Cartesian
Value
Units N/A
Interfaces GUI, script
ECC
The orbital eccentricity expressed in the coordinate
system chosen in the CoordinateSystem field.
Data Real
Type
Allowed ECC < 0.9999999 or ECC > 1.0000001. If
Values ECC > 1, SMA must be < 0
Access set, get
Default 0.02454974900598137
Value
Units N/A
Interfaces GUI, script
EquinoctialH
A measure of the orbital eccentricity and argument of
periapsis. EquinoctialH and EquinoctialK together
govern how elliptic an orbit is and where the periapsis is
located. EquinotialH = ECC * sin(AOP + RAAN) .
Data Real
Type
Allowed -0.99999 < EquinoctialH < 0.99999, AND
Values sqrt(EquinoctialH^2 + EquinoctialK^2) <
0.99999
Access set, get
Default -0.02423431419337062
Value
Units dimless
Interfaces GUI, script
EquinoctialK
A measure of the orbital eccentricity and argument of
periapsis. EquinoctialH and EquinoctialK together
govern how elliptic an orbit is and where the periapsis is
located. EquinotialK = ECC * cos(AOP + RAAN) .
Data Real
Type
Allowed -0.99999 < EquinoctialK < 0.99999, AND
Values sqrt(EquinoctialH^2 + EquinoctialK^2) <
0.99999
Access set, get
Default -0.003922778585859663
Value
Units dimless
Interfaces GUI, script
EquinoctialP
A measure of the orientation of the orbit. EquinoctialP
and EquinoctialQ together govern how an orbit is
oriented. EquinotialP = tan(INC/2)*sin(RAAN).
Data Type Real
Allowed Values -∞ < EquinoctialP < ∞
Access set, get
Default Value -0.09038834725719359
Units dimless
Interfaces GUI, script
EquinoctialQ
A measure of the orientation of the orbit. EquinoctialP
and EquinoctialQ together govern how an orbit is
oriented. EquinotialQ = tan(INC/2)*cos(RAAN).
Data Type Real
Allowed Values -∞ < EquinoctialQ < ∞
Access set, get
Default Value 0.06716454898232072
Units dimless
Interfaces GUI, script
FPA
The orbital flight path angle expressed in the coordinate
system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values 0 ≤ FPA ≤ 180
Access set, get
Default Value 88.60870365370448
Units Deg.
Interfaces GUI, script
Id
The spacecraft Id used in tracking data files. This field is
only used for EstimationPlugin protype functionality.
Data Type String
Allowed Values String
Access set
Default Value SatId
Units N/A
Interfaces script
INC
The orbital inclination expressed in the coordinate system
chosen in the CoordinateSystem field.
Data Type Real
Allowed Values 0 ≤ INC ≤ 180
Access set, get
Default Value 12.85008005658097
Units deg
Interfaces GUI, script
IncomingBVAZI IncomingBVAZI/OutgoingBVAZI is the B-vector
azimuth at infinity of the incoming/outgoing asymptote
OutgoingBVAZI measured counter-clockwise from south. If C3Energy
the apsides vector is substituted for the outgoing/incoming
asymptote.
Data Type Real
Allowed -∞ < IncomingBVAZI/OutgoingBVAZI
Values < ∞
Access set, get
Default Value Conversion from default Cartesian state
Units deg
Interfaces GUI, script
IncomingC3Energy C3 energy. C3Energy = -mu/SMA.
IncomingC3Energy/OutgoingC3Energy differ only in
OutgoingC3Energy that they are associated with the IncomingAsymptote
OutgoingAsymptote state representations, respectively.
Data Type Real
Allowed IncomingC3Energy ≤ -1e-7 or
Values IncomingC3Energy ≥ 1e-7
OutgoingC3Energy ≤ -1e-7 or
OutgoingC3Energy ≥ 1e-7
Access set, get
Default Conversion from default Cartesian state
Value
Units km2/s2
Interfaces GUI, script
IncomingDHA IncomingDHA/OutgoingDHA is the declination of the
incoming/outgoing asymptote. If C3Energy < 0 the
OutgoingDHA apsides vector is substituted for the incoming/outgoing
asymptote..
Data Type Real
Allowed -90° ≤ IncomingDHA/OutgoingDHA
Values 90°
Access set, get
Default Value Conversion from default Cartesian state
Units deg
Interfaces GUI, script
IncomingRadPer The orbital radius of periapsis. The radius of periapsis is
the minimum distance (osculating) between the spacecraft
OutgoingRadPer and celestial body at the origin of coordinate system.
IncomingRadPer/OutgoingRadPer differ from RadPer
only in that they are associated with the
IncomingAsymptote and OutgoingAsymptote state
representations, respectively.
Data Type Real
Allowed Values abs(IncomingRadPer) ≥ 1 meter.
abs(OutgoingRadPer) ≥ 1 meter.
Access set, get
Default Value Conversion from default Cartesian state
Units km
Interfaces GUI, script
IncomingRHA IncomingRHA/OutgoingRHA is the right ascension of
the incoming/outgoing asymptote. If C3Energy < 0 the
OutgoingRHA apsides vector is substituted for the incoming/outgoing
asymptote.
Data Type Real
Allowed -∞ < IncomingRHA/OutgoingRHA
Values ∞
Access set, get
Default Value Conversion from default Cartesian state
Units deg
Interfaces GUI, script
MLONG
A measure of the location of the spacecraft in it's orbit.
MLONG = AOP + RAAN + MA.
Data Type Real
Allowed Values -360 ≤ MLONG ≤ 360
Access set, get
Default Value 357.9131803707105
Units deg.
Interfaces GUI, script
ModEquinoctialF
Components of the eccentricity vector (with
ModEquinoctialG). The eccentricity vector has a
magnitude equal to the eccentricity and it points from the
central body to perigee. ModEquinoctialF = ECC *
cos(AOP+RAAN)
Data Type Real
Allowed Values -∞ < ModEquinoctialF < ∞
Access set, get
Default Value -0.003922778585859663
Units (None)
Interfaces GUI, script
ModEquinoctialG
Components of eccentricity vector (with
ModEquinoctialF). ModEquinoctialG = ECC *
sin(AOP+RAAN)
Data Type Real
Allowed Values -∞ < ModEquinoctialG < ∞
Access set, get
Default Value -0.02423431419337062
Units (None)
Interfaces GUI, script
ModEquinoctialH
Identical to EquinoctialQ.
Data Type Real
Allowed Values -∞ < ModEquinoctialH < ∞
Access set, get
Default Value 0.06716454898232072
Units (None)
Interfaces GUI, script
ModEquinoctialK
Idential to EquinoctialP.
Data Type Real
Allowed Values -∞ < ModEquinoctialK < ∞
Access set, get
Default Value -0.09038834725719359
Units (None)
Interfaces GUI, script
NAIFId
The spacecraft Id used in SPICE kernels.
Data Type String
Allowed Values String
Access set
Default Value -123456789
Units N/A
Interfaces GUI, script
OrbitSpiceKernelName
SPK Kernels for spacecraft orbit. SPK orbit kernels have
extension ".BSP". This field cannot be set in the Mission
Sequence.
Data Type String array
Allowed Values List of path and filenames.
Access set
Default Value No Default. The field is empty.
Units N/A
Interfaces GUI, script
PlanetodeticAZI
The orbital velocity azimuth expressed in the coordinate
system chosen in the CoordinateSystem field. Unlike the
AZI field, PlanetodeticAZI is associated with the
Planetodetic state representation, which is only valid for
coordinate systems with BodyFixed axes.
Data Type Real
Allowed Values -∞ < PlanetodeticAZI < ∞
Access set, get
Default Value 81.80908019114962
Units deg
Interfaces GUI, script
PlanetodeticHFPA
The orbital horizontal flight path angle expressed in the
coordinate system chosen in the CoordinateSystem field.
PlanetodeticHFPA is only valid for coordinate systems
with BodyFixed axes.
Data Type Real
Allowed Values -90 ≤ PlanetodeticHFPA ≤ 90
Access set, get
Default Value 1.494615814842774
Units deg
Interfaces GUI, script
PlanetodeticLAT
The planetodetic latitude expressed in the coordinate
system chosen in the CoordinateSystem field. This field
is only valid for coordinate systems with BodyFixed axes.
Data Type Real
Allowed Values -90 ≤ PlanetodeticLAT ≤ 90
Access set, get
Default Value 10.43478253114861
Units deg
Interfaces GUI, script
PlanetodeticLON
The planetodetic longitude expressed in the coordinate
system chosen in the CoordinateSystem field. This field
is only valid for coordinate systems with BodyFixed axes.
Data Type Real
Allowed Values -∞ < PlanetodeticLON < ∞
Access set, get
Default Value 79.67188405807977
Units deg
Interfaces GUI, script
PlanetodeticRMAG
The magnitude of the orbital position vector expressed in
the coordinate system chosen in the CoordinateSystem
field. Unlike the RMAG field, PlanetodeticRMAG is
associated with the Planetodetic state representation,
which is only valid for coordinate systems with
BodyFixed axes.
Data Type Real
Allowed Values PlanetodeticRMAG ≥ 1e-10
Access set, get
Default Value 7218.032973047435
Units km
Interfaces GUI, script
PlanetodeticVMAG
The magnitude of the orbital velocity vector expressed in
the coordinate system chosen in the CoordinateSystem
field. Unlike the VMAG field, PlanetodeticVMAG is
associated with the Planetodetic state representation,
which is only valid for coordinate systems with
BodyFixed axes.
Data Type Real
Allowed Values PlanetodeticVMAG ≥ 1e-10
Access set, get
Default Value 6.905049647173787
Units km/s
Interfaces GUI, script
RA
The right ascension of the orbital position expressed in the
coordinate system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < RA < ∞
Access set,get
Default Value 0
Units deg
Interfaces GUI, script
RAAN
The orbital right ascension of the ascending node
expressed in the coordinate system chosen in the
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < RAAN < ∞
Access set, get
Default Value 306.6148021947984
Units deg
Interfaces GUI, script
RadApo
The orbital radius of apoapsis expressed in the coordinate
system chosen in the CoordinateSystem field. The radius
of apoapsis is the maximum distance (osculating) between
the Spacecraft and celestial body at the origin of
CoordinateSystem.
Data Type Real
Allowed Values abs(RadApo) ≥ 1 meter.
Access set, get
Default Value 7368.49911046818
Units km
Interfaces GUI, script
RadPer
The orbital radius of periapsis expressed in the coordinate
system chosen in the CoordinateSystem field. The radius
of periapsis is the minimum distance (osculating) between
the Spacecraft and celestial body at the origin of
CoordinateSystem.
Data Type Real
Allowed Values abs(RadPer) ≥ 1 meter.
Access set, get
Default Value 7015.378524789846
Units km
Interfaces GUI, script
RAV
The right ascension of orbital velocity expressed in the
coordinate system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < RAV < ∞
Access set,get
Default Value 90
Units deg
Interfaces GUI, script
RMAG
The magnitude of the orbital position vector expressed in
the coordinate system chosen in the CoordinateSystem
field.
Data Type Real
Allowed Values RMAG ≥ 1e-10
Access set, get
Default Value 7218.032973047435
Units km
Interfaces GUI, script
SemilatusRectum
Magnitude of the position vector when at true anomaly
90 deg.
Data Type Real
Allowed Values SemilatusRectum > 1e-7
Access set, get
Default Value 7187.60430675539
Units km
Interfaces GUI, script
SMA
The orbital semi-major axis expressed in the coordinate
system chosen in the CoordinateSystem field.
Data Real
Type
Allowed SMA < -0.001 m or SMA > 0.001 meter. If
Values SMA < 0, then ECC must be > 1
Access set, get
Default 7191.938817629013
Value
Units km
Interfaces GUI, script
TA
The orbital true anomaly expressed in the coordinate
system chosen in the CoordinateSystem field.
Data Type Real
Allowed Values -∞ < TA < ∞
Access set, get
Default Value 99.8877493320488
Units deg.
Interfaces GUI, script
TLONG
True longitude of the osculating orbit. TLONG = RAAN
+ AOP + TA
Data Type Real
Allowed Values -∞ < TLONG < ∞
Access set, get
Default Value 0.6931030628392251
Units deg
Interfaces GUI, script
VMAG
The magnitude of the orbital velocity vector expressed in
the coordinate system chosen in the CoordinateSystem
field.
Data Type Real
Allowed Values VMAG ≥ 1e-10
Access set, get
Default Value 7.417715281675348
Units km/s
Interfaces GUI, script
VX
The x-component of the Spacecraft velocity with respect
to the coordinate system chosen in the spacecraft's
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < VX < ∞
Access set, get
Default Value 0
Units km/s
Interfaces GUI, script
VY
The y-component of the Spacecraft velocity with respect
to the coordinate system chosen in the spacecraft's
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < VY < ∞
Access set, get
Default Value 7.35
Units km/s
Interfaces GUI, script
VZ
The z-component of the Spacecraft velocity with respect
to the coordinate system chosen in the spacecraft's
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < VZ < ∞
Access set, get
Default Value 1
Units km/s
Interfaces GUI, script
X
The x-component of the Spacecraft position with respect
to the coordinate system chosen in the spacecraft's
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < X < ∞
Access set,get
Default Value 7100
Units km
Interfaces GUI, script
Y
The y-component of the Spacecraft position with respect
to the coordinate system chosen in the spacecraft's
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < Y < ∞
Access set, get
Default Value 0
Units km
Interfaces GUI, script
Z
The z-component of the Spacecraft position with respect
to the coordinate system chosen in the spacecraft's
CoordinateSystem field.
Data Type Real
Allowed Values -∞ < Z < ∞
Access set, get
Default Value 1300
Units km
Interfaces GUI, script
GUI
The Spacecraft orbit state dialog box allows you to set the epoch, coordinate
system, and state type values for the Spacecraft orbital state. When you specify
an orbital state, you define the state in the representation selected in the
StateType menu, with respect to the coordinate system specified in the
CoordinateSystem menu, at the epoch defined in the Epoch menu. If the
selected CoordinateSystem is time varying, the epoch of the coordinate system
is defined by the Epoch field, and changing the epoch changes the inertial
representation of the orbital state.
A change in Epoch Format causes an immediate update to Epoch to reflect the
chosen time system and format.
The Keplerian, ModifiedKeplerian, and Equinoctial state types cannot be
computed if the CoordinateSystem does not have a central body at the origin, or
if the CoordinateSystem references the current spacecraft (resulting in a
circular reference). For example, if you have selected the Keplerian state type,
coordinate systems for which the Keplerian elements cannot be computed do not
appear in the CoordinateSystem menu. Similarly, if you have selected a
CoordinateSystem that does not have a celestial body at the origin, Keplerian-
based state types will not appear as options in the StateType menu. The
Planetodetic state type cannot be selected untill the CoordinateSystem has
BodyFixed axes.
Remarks
Cartesian State
The Cartesian state is composed of the position and velocity components
expressed with respect to the selected CoordinateSystem.
Keplerian and Modified Keplerian State Types
The Keplerian and ModifiedKeplerian state types use the osculating Keplerian
orbital elements with respect to the selected CoordinateSystem. To use either
the Keplerian or ModifiedKeplerian state type, the Spacecraft’s coordinate
system must have a central body at the origin. The two representations differ in
how the orbit size and shape are defined. The Keplerian state type is composed
of the following elements: SMA, ECC, INC, RAAN, AOP, and TA. The
ModifiedKeplerian state type is composed of the following elements: RadApo,
RadPer, INC, RAAN, AOP, and TA. The tables and figures below describe
each Keplerian state element in detail including singularities.
Geometry of the Keplerian Elements
Name Description
SMA
SMA contains information on the type and size of an orbit. If SMA
> 0 the orbit is elliptic. If SMA <0 the orbit is hyperbolic. SMA is
infinite for parabolic orbits.
ECC
ECC contains information on the shape of an orbit. If ECC = 0,
then the orbit is circular. If 0 < ECC < 1, the orbit is elliptical. If ,
ECC = 1 the orbit is parabolic. If ECC > 1 then the orbit is
hyperbolic.
INC
INC is the angle between the orbit angular momentum vector and
the z-axis. If INC < 90 deg., then the orbit is prograde. If INC > 90
deg, then the orbit is retrograde
RAAN
RAAN is defined as the angle between x-axis and the node vector
measured counterclockwise. The node vector is defined as the cross
product of the z-axis and orbit angular momentum vector. RAAN is
undefined for equatorial orbits.
AOP
AOP is the angle between a vector pointing at periapsis and a vector
pointing in the direction of the line of nodes. AOP is undefined for
circular orbits.
TA
TA is defined as the angle between a vector pointing at periapsis
and a vector pointing at the spacecraft. TA is undefined for circular
orbits.
The Keplerian and ModifiedKeplerian state types have several singularities.
The table below describes the different singularities and how each is handled in
the state conversion algorithms.
Singularity Comments and Behavior
ECC = 1
SMA is infinite and cannot be used to define the size of the
orbit. GMAT requires ECC < 0.9999999 or ECC >
1.0000001 when setting ECC or when performing
conversions. For transformations performed near these
limits, loss of precision may occur.
ECC = 0
AOP is undefined. If ECC <= 1e-11, GMAT sets AOP to
zero in the conversion from Cartesian to
Keplerian/ModKeplerian and includes all orbital-plane
angular displacement in the true anomaly.
SMA = 0
Results in a singular conic section. GMAT requires |SMA|
> 1 meter when inputting SMA.
SMA = INF
SMA is infinite and another parameter is required to
capture the size of the orbit. Keplerian elements are not
supported.
INC = 0
RAAN is undefined. If INC < 6e-10, GMAT sets RAAN
to 0 in the conversion from Cartesian to
Keplerian/ModKeplerian. Then, if ECC < 1e-11, AOP is
set to 0 and GMAT includes all angular displacement
between the x-axis and the spacecraft in the true anomaly.
If ECC ≥ 1e-11, then AOP is computed as the angle
between the eccentricity vector and the x-axis.
INC = 180
RAAN is undefined. If INC > (180 - 6e-10), GMAT sets
RAAN to 0 in the conversion from Cartesian to
Keplerian/ModKeplerian. Then, if ECC < 1e-11, AOP is
set to 0 and GMAT includes all angular displacement
between the x-axis and the spacecraft in the true anomaly.
If ECC ≥ 1e-11, then AOP is computed as the angle
between the eccentricity vector and the x-axis.
RadPer = 0
Singular conic section. GMAT requires RadPer > 1 meter
in state conversions.
RadApo = 0
Singular conic section. GMAT requires abs(RadApo) > 1
meter in state conversions.
Delaunay State Type
The conversion between Delaunay and Cartesian is performed passing through
classical Keplerian state. Therefore, Delaunay state cannot represent parabolic
orbits. Also, the Delaunay state cannot represent hyperbolic orbits because of
the definition of DelaunayL, which is not a real value when SMA is negative.
The table below describes the elements of the Delaunay state.
Element Description
Delaunayl
The mean anomaly. It is related to uniform angular
motion on a circle of radius SMA.
Delaunayg
See “Keplerian State” section, AOP
Delaunayh
See “Keplerian State” section, RAAN
DelaunayL
Related to the two-body orbital energy. DelaunayL
= sqrt(mu*SMA)
DelaunayG
Magnitude of the orbital angular momentum vector.
DelaunayG = DelaunayL*sqrt(1-ECC^2)
DelaunayH
The K component of the orbital angular momentum.
DelaunayH = DelaunayG * cos(INC)
Singularities in the Delaunay Elements
Singularities in the Delaunay elements is the same as the Keplerian elements,
because it uses the Keplerian elements during conversion. See “Keplerian
State” section. The table below shows the additional singularities regarding the
Delaunay state type.
Element Description
ECC > 1
DelaunayL is not real for hyperbolic orbits by its
definition.
Brouwer-Lyddane Mean State Type
The BrouwerMeanShort state represents short-term averaged mean motion
under low-order zonal harmonics (i.e. J2-J5). Likewise, BrouwerMeanLong
state represents long-term averaged mean motion under low-order zonal
harmonics (i.e. J2-J5). GMAT uses JGM-2 zonal coefficients in Brouwer Mean
states algorithms. Both are singular for near parabolic or hyperbolic orbits. To
use BrouwerMeanShort/BrouwerMeanLong state type in GMAT, the central
body must be the Earth. If the central body is the Earth, GMAT can calculate
BrouwerMeanShort/BrouwerMeanLong state from the osculating state
(Cartesian, Keplerian, etc.) and vice-versa.
Element Description
BrouwerLongAOP Brouwer-Lyddane long-term averaged (short-term
averaged) mean argument of periapsis.
BrouwerShortAOP
BrouwerLongMA Brouwer-Lyddane long-term averaged (short-term
averaged) mean MA (mean anomaly).
BrouwerShortMA
BrouwerLongECC Brouwer-Lyddane long-term averaged (short-term
averaged) mean eccentricity.
BrouwerShortECC
BrouwerLongINC Brouwer-Lyddane long-term averaged (short-term
averaged) mean inclination.
BrouwerShortINC
BrouwerLongRAAN Brouwer-Lyddane long-term averaged (short-term
averaged) mean RAAN (right ascension of the
BrouwerShortRAAN ascending node).
BrouwerLongSMA Long-term averaged (short-term averaged) mean
semi-major axis.
BrouwerShortSMA
Singularities in the Brouwer-Lyddane Mean Elements
The table below shows the characteristics of singularities regarding
BrouwerMeanShort/BrouwerMeanLong state and the implemented method to
handle the singularities in GMAT state conversion algorithms. Note that because
Brouwer-Lyddane mean elements involve an iterative solution, loss of precision
may occur near singularities.
Element Description
BrouwerSMA <
3000/(1-BrouwerECC) Because Brouwer’s formulation based on Earth’s
zonal harmonics, BrouwerMeanShort and
BrouwerMeanLong cannot address orbits with
mean perigee distance is smaller than Earth’s radius,
3000 km because of numerical instability.
BrouwerLongINC= 63,
BrouwerLongINC = If given BrouwerLongINC (long-term averaged
117 INC only) is close to i = 63 deg. or 117 deg., the
c
algorithm is unstable because of singular terms (non-
zero imaginary components). Thus, GMAT cannot
calculate osculating elements.
BrouwerLongECC = 0,
BrouwerLongECC ≥ 1 If BrouwerECC is larger than 0.9, or BrouwerECC
is smaller than 1E-7, it has been reported that
Cartesian to BrouwerMeanLong state does not
converge statistically. For these cases, GMAT gives
a warning message with the current conversion error.
Spherical State Types
The SphericalAZFPA and SphericalRADEC state types are composed of the
polar coordinates of the spacecraft state expressed with respect to the selected
CoordinateSystem. The two spherical representations differ in how the velocity
is defined. The SphericalRADEC state type is composed of the following
elements: RMAG, RA, DEC, VMAG, RAV, and DECV. The
SphericalAZFPA state type is composed of the following elements: RMAG,
RA, DEC, VMAG, AZI and FPA. The tables and figures below describe each
spherical state element in detail including singularities.
Geometry of the Spherical Elements
Name Description
RMAG
The magnitude of the position vector.
RA
The right ascension which is the angle between the projection of the
position vector into the xy-plane and the x-axis measured
counterclockwise.
DEC
The declination which is the angle between tjhe position vector and
the xy-plane.
VMAG
The magnitude of the velocity vector.
FPA
The vertical flight path angle. The angle measured from a plane
normal to the postion vector to the velocity vector , measured in the
plane formed by position vector and velocity vector.
AZI
The flight path azimuth. The angle measured from the vector
perpendicular to the position vector and pointing north, to the
projection of the velocity vector, into a plane normal to the position
vector.
RAV
The right ascension of velocity. The angle between the projection of
the velocity vector into the xy-plane and the x-axis measured
counterclockwise.
DECV
The flight path azimuth. The angle between the velocity vector and
the xy-plane.
Singularities in the Spherical Elements
Singularity Comments and Behavior
RMAG = 0
Results in a singular conic section: declination and flight
path angle are undefined. GMAT will not allow
transformations if RMAG < 1e-10. For RMAG values
greater than, but near 1e-10, loss of precision may occur in
transformations.
VMAG = 0
Results in a singular conic section: velocity declination and
flight path angle are undefined. GMAT will not allow
transformations if VMAG < 1e-10.For VMAG values
greater than, but near 1e-10, loss of precision may occur in
transformations.
Planetodetic State Type
The Planetodetic state type is useful for specifying states relative to the surface
of a central body. It is very similar to the spherical state types, but uses the
central body's flattening in its definition. To use the Planetodetic state type, the
spacecraft’s coordinate system must have a celestial body at the origin, and must
have BodyFixed axes.
Element Description
PlanetodeticRMAG
Magnitude of the orbital radius vector.
PlanetodeticLON
Planetodetic longitude.
PlanetodeticLAT
Planetodetic latitude, using the Flattening of the
central body.
PlanetodeticVMAG
Magnitude of the orbital velocity vector in the fixed
frame.
PlanetodeticAZI
Orbital velocity azimuth in the fixed frame.
PlanetodeticHFPA
Horizontal flight path angle. HFPA = 90 - VFPA
Singularities in the Planetodetic Elements
Singularity Comments and Behavior
PlanetodeticRMAG
= 0 Results in a singular conic section: declination and flight
path angle are undefined. GMAT will not allow
transformations if PlanetodeticRMAG < 1e-10. For
PlanetodeticRMAG values greater than, but near 1e-10,
loss of precision may occur in transformations.
PlanetodeticVMAG
= 0 Results in a singular conic section: velocity declination
and flight path angle are undefined. GMAT will not
allow transformations if PlanetodeticVMAG < 1e-10.
For PlanetodeticVMAG values greater than, but near
1e-10, loss of precision may occur in transformations.
Equinoctial State Type
GMAT supports the Equinoctial state representation which is non-singular for
elliptic orbits with inclinations less than 180 degrees. To use the Equinoctial
state type, the spacecraft’s coordinate system must have a central body at the
origin.
Element Description
SMA
See Keplerian section.
EquinoctialH
A measure of the orbital eccentricity and argument
of periapsis. EquinoctialH and EquinoctialK
together govern how elliptical an orbit is and where
the periapsis is located. EquinotialH = ECC *
sin(AOP).
EquinoctialK
A measure of the orbital eccentricity and argument
of periapsis. EquinoctialH and EquinoctialK
together govern how eliptical an orbit is and where
the periapsis is located. EquinotialK = ECC *
cos(AOP)
EquinoctialP
A measure of the orientation of the orbit.
EquinoctialP and EquinoctialQ together govern
how an orbit is oriented. EquinotialP =
tan(INC/2)*sin(RAAN).
EquinoctialQ
A measure of the orientation of the orbit.
EquinoctialP and EquinoctialQ together govern
how an orbit is oriented. EquinotialQ =
tan(INC/2)*cos(RAAN).
MLONG
A measure of the mean location of the spacecraft in
its orbit. MLONG = AOP + RAAN + MA.
Singularities in the Equinoctial Elements
Element Description
INC = 180
RAAN is undefined. If INC > 180 - 1.0e-11, GMAT
sets RAAN to 0 degrees. GMAT does not support
Equinoctial elements for true retrograde orbits.
ECC > 0.9999999
Equinoctial elements are not defined for parabolic
or hyperbolic orbits.
Alternate Equinoctial State Type
The AlternateEquinoctial state type is a slight variation on the Equinoctial
elements that uses sin(INC/2) instead of tan(INC/2) in the "P" and "Q"
elements. Both representations have the same singularties.
Element Description
SMA
See Keplerian section.
EquinoctialH
See Equinoctial section.
EquinoctialK
See Equinoctial section.
AltEquinoctialP
A measure of the orientation of the orbit.
AltEquinoctialP and AltEquinoctialQ together
govern how an orbit is oriented. AltEquinotialP =
sin(INC/2)*sin(RAAN).
AltEquinoctialQ
A measure of the orientation of the orbit.
AltEquinoctialP and AltEquinoctialQ together
govern how an orbit is oriented. AltEquinotialP =
sin(INC/2)*cos(RAAN).
MLONG
See Equinoctial section.
Modified Equinoctial State Type
The ModifiedEquinoctial state representation is non-singular for circular,
elliptic, parabolic, and hyperbolic orbits. The only singularity is for retrograde
equatorial orbits, because, like Equinoctial and ModifiedEquinoctial, GMAT
does not support the retrograde factor.
Element Description
SemilatusRectum
Magnitude of the position vector when at true
anomaly of 90 deg SemilatusRectum = SMA*(1-
ECC^2)
ModEquinoctialF
Components of eccentricity vector (with
ModEquinoctialG). Projection of eccentricity
vector onto x. ModEquinoctialF = ECC * cos
(AOP+RAAN)
ModEquinoctialG
Components of eccentricity vector (with
ModEquinoctialF). Projection of eccentricity vector
onto y. ModEquinoctialG = ECC * sin
(AOP+RAAN)
ModEquinoctialH
Identical to EquinoctialQ.
ModEquinoctialK
Idential to EquinoctialP.
TLONG
A measure of the true location of the spacecraft in its
orbit. TLONG = AOP + RAAN + TA.
Singularities in the Modified Equinoctial Elements
Element Description
INC = 180
Similar to Equinoctial elements, there is singularity
at INC = 180 deg. GMAT does not support
ModifiedEquinoctial elements for retrograde
equatorial orbits.
Hyperbolic Asymptote State Type
GMAT supports two related hyperbolic asymptote state types:
IncomingAsymptote for defining the incoming hyperbolic asymptote, and
OutgoingAsymptote, for defining the outgoing hyperbolic asymptote. Both
representations are useful for defining flybys.
Element Description
IncomingRadPer The orbital radius of periapsis. The radius of
periapsis is the minimum distance (osculating)
OutgoingRadPer between the spacecraft and celestial body at the
origin of coordinate system.
IncomingRadPer/OutgoingRadPer differ from
RadPer only in that they are associated with the
IncomingAsymptote and OutgoingAsymptote
state representations, respectively.
IncomingC3Energy C3 energy. C3Energy = -mu/SMA.
IncomingC3Energy/OutgoingC3Energy differ
OutgoingC3Energy only in that they are associated with the
IncomingAsymptote and OutgoingAsymptote
state representations, respectively.
IncomingRHA IncomingRHA/OutgoingRHA is the right
ascension of the incoming/outgoing asymptote. If
OutgoingRHA C3Energy < 0 the apsides vector is substituted for
the incoming/outgoing asymptote.
IncomingDHA IncomingDHA/OutgoingDHA is the declination of
the incoming/outgoing asymptote. If C3Energy < 0
OutgoingDHA the apsides vector is substituted for the
incoming/outgoing asymptote..
IncomingBVAZI IncomingBVAZI/OutgoingBVAZI is the B-vector
azimuth at infinity of the incoming/outgoing
OutgoingBVAZI asymptote measured counter-clockwise from south.
If C3Energy < 0 the apsides vector is substituted for
the outgoing/incoming asymptote.
TA
See Keplerian.
Singularities in the Hyperbolic Asymptote Elements
Element Description
IncomingC3Energy/OutgoingC3Energy
= 0 If
IncomingC3Energy/OutgoingC3Energy
= 0 the spacecraft has a parabolic orbit.
Hyperbolic asymptote states do not
support parabolic orbits. It must be
avoided that -1E-7 ≤
IncomingC3Energy/OutgoingC3Energy
≤ 1E-7 by choosing a proper set of
elements.
ECC = 0
For the case of circular orbits, TA is
undefined. It must be avoided that ECC
1E-7 by choosing a proper set of
elements. GMAT does not support
hyperbolic asymptote representation for
true circular orbits.
Asymptote vector parallel to z-axis
If the asymptote vector is parallel or
antiparallel to coordinate system’s z-
direction, then the B-plane is undefined. It
must be avoided by choosing either a
proper coordinate system or set of
elements.
State Component Interactions with the Spacecraft
Coordinate System Field
When you define Spacecraft state elements such as SMA, X, or DEC for
example, these values are set in coordinates defined by the Spacecraft’s
CoordinateSystem field. For example, the following lines result in the X-
component of the Cartesian state of MySat to be 1000, in the EarthFixed
system.
aSpacecraft.CoordinateSystem = EarthFixed
aSpacecraft.X = 1000
When the script lines above are executed in a script, GMAT converts the state to
the specified coordinate system, in this case EarthFixed, sets the X component
to 1000, and then converts the state back to the internal inertial representation.
The following example sets SMA to 8000 in the EarthMJ2000Eq system, then
sets X to 6000 in the Earth fixed system. (Note this is NOT allowed in
initialization mode; see later remarks for more information).
aSpacecraft.CoordinateSystem = EarthMJ2000Eq
aSpacecraft.SMA = 8000
aSpacecraft.CoordinateSystem = EarthFixed
aSpacecraft.X = 6000
State Component Interactions with the Spacecraft Epoch
Field
When you specify the Spacecraft’s epoch, you define the initial epoch of the
spacecraft in the specified coordinate system. If your choice for the Spacecraft's
coordinate system is a time varying system such as the EarthFixed system, then
you define the state in the EarthFixed system at that epoch. For example, the
following lines would result in the cartesian state of MySat to be set to [7000 0
1300 0 7.35 1] in the EarthFixed system at 01 Dec 2000 12:00:00.000
UTC.
Create Spacecraft MySat
MySat.Epoch.UTCGregorian = '01 Dec 2000 12:00:00.000'
MySat.CoordinateSystem = EarthFixed
MySat.X = 7000
MySat.Y = 0
MySat.Z = 1300
MySat.VX = 0
MySat.VY = 7.35
MySat.VZ = 1
The corresponding EarthMJ2000Eq representation is
X = -2320.30266
Y = -6604.25075
Z = 1300.02599
VX = 7.41609
VY = -2.60562
VZ = 0.99953
You can change the epoch of a Spacecraft in the mission sequence using a script
line like this:
MySat.Epoch.TAIGregorian = '02 Dec 2000 12:00:00.000'
When the above line is executed in the mission sequence, GMAT converts the
state to the specified coordinate system and then to the specified state type — in
this case EarthFixed and Cartesian respectively — sets the epoch to the value
of 02 Dec 2000 12:00:00.000, and then converts the state back to the internal
representation. This behavior is identical to that of the spacecraft orbit dialog
box in the GUI. Because the coordinate system in this case is time varying,
changing the spacecraft epoch has resulted in a change in the spacecraft's inertial
state representation. After the epoch is changed to 02 Dec 2000 12:00:00.000,
the EarthMJ2000Eq state representation is now:
X = -2206.35771
Y = -6643.18687
Z = 1300.02073
VX = 7.45981
VY = -2.47767
VZ = 0.99953
Scripting Limitations during Initialization
When setting the Spacecraft orbit state in a script, there are a few limitations to
be aware of. In the initialization portion of the script (before the
BeginMissionSequence command), you should set the epoch and coordinate
system only once; multiple definitions of these parameters will result in either
errors or warning messages and may lead to unexpected results.
Also when setting a state during initialization, you must set the orbit state in a set
of fields corresponding to a single state type. For example, set the orbit state
using the X, Y, Z, VX, VY, VZ fields (for the Cartesian state type) or the
SMA, ECC, INC, RAAN, AOP, TA fields (for the Keplerian state type), but
not a mixture of the two. If you need to mix state types, coordinate systems, or
epochs to define the state of a spacecraft, you must set the state using scripting in
the mission sequence (after the BeginMissionSequence command).
Shared State Components
Some state components, such as SMA, are shared among multiple state
representations. In the mission sequence, GMAT does not require you to specify
the state representation that you are setting; rather, you may specify a
combination of elements from different representations.
For these shared components, GMAT defines a default representation for each,
and uses that representation when setting or retrieving the value for the shared
component. This is normally transparent, though it can have side effects if the
default representation has singularities or numerical precision losses caused by
the value being set or retrieved. The following table lists each shared state
component and its default representation.
Field Shared Between Default Representation
AOP Keplerian, ModifiedKeplerian Keplerian
DEC SphericalAZFPA, SphericalAZFPA
SphericalRADEC
EquinoctialH AlternateEquinoctial, Equinoctial
Equinoctial
EquinoctialK AlternateEquinoctial, Equinoctial
Equinoctial
INC Keplerian, ModifiedKeplerian Keplerian
RA SphericalAZFPA, SphericalAZFPA
SphericalRADEC
RAAN Keplerian, ModifiedKeplerian Keplerian
RMAG SphericalAZFPA, SphericalAZFPA
SphericalRADEC
SMA AlternateEquinoctial, Keplerian
Equinoctial, Keplerian
TA IncomingAsymptote, Keplerian
OutgoingAsymptote,
Keplerian, ModifiedKeplerian
VMAG SphericalAZFPA, SphericalAZFPA
SphericalRADEC
As an example, consider the following mission sequence. Because GMAT
executes each command sequentially, it uses the assigned state representation to
calculation each component. For shared components, it uses the default
representation for reach.
BeginMissionSequence
aSpacecraft.SMA = 20000 % conversion goes through Keplerian
aSpacecraft.RA = 30 % conversion goes through SphericalAZFPA
aSpacecraft.OutgoingDHA = 90 % conversion goes through OutgoingAsymptote
aSpacecraft.TA = 45 % conversion goes through Keplerian
Warning
When setting state parameters (especially in Keplerian-based
representations) using non-default dependencies, be careful of
the loss of precision caused by large translations in the
intermediate orbit.
Examples
Define a Spacecraft’s Earth MJ2000Eq coordinates in the Keplerian
representation:
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = EarthMJ2000Eq
aSpacecraft.SMA = 7100
aSpacecraft.ECC = 0.01
aSpacecraft.INC = 30
aSpacecraft.RAAN = 45
aSpacecraft.AOP = 90
aSpacecraft.TA = 270
Define a Spacecraft’s Earth fixed coordinates in the Cartesian representation:
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = EarthFixed
aSpacecraft.X = 7100
aSpacecraft.Y = 0
aSpacecraft.Z = 1300
aSpacecraft.VX = 0
aSpacecraft.VY = 7.35
aSpacecraft.VZ = 1
Define a Spacecraft’s Moon centered coordinates in ModifiedKeplerian
representation.
Create CoordinateSystem MoonInertial
MoonInertial.Origin = Luna
MoonInertial.Axes = BodyInertial
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = MoonInertial
aSpacecraft.RadPer = 2100
aSpacecraft.RadApo = 2200
aSpacecraft.INC = 90
aSpacecraft.RAAN = 45
aSpacecraft.AOP = 45
aSpacecraft.TA = 180
Define a Spacecraft’s Rotating Libration Point coordinates in the
SphericalAZFPA representation:
Create LibrationPoint ESL1
ESL1.Primary = Sun
ESL1.Secondary = Earth
ESL1.Point = L1
Create CoordinateSystem EarthSunL1CS
EarthSunL1CS.Origin = ESL1
EarthSunL1CS.Axes = ObjectReferenced
EarthSunL1CS.XAxis = R
EarthSunL1CS.ZAxis = N
EarthSunL1CS.Primary = Sun
EarthSunL1CS.Secondary = Earth
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = EarthSunL1CS
aSpacecraft.DateFormat = UTCGregorian
aSpacecraft.Epoch = '09 Dec 2005 13:00:00.000'
aSpacecraft.RMAG = 1520834.130720907
aSpacecraft.RA = -111.7450242065574
aSpacecraft.DEC = -20.23326432189756
aSpacecraft.VMAG = 0.2519453702907011
aSpacecraft.AZI = 85.22478175803107
aSpacecraft.FPA = 97.97050698644287
Define a Spacecraft’s Earth-fixed coordinates in the Planetodetic
representation:
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = EarthFixed
aSpacecraft.PlanetodeticRMAG = 7218.032973047435
aSpacecraft.PlanetodeticLON = 79.67188405817301
aSpacecraft.PlanetodeticLAT = 10.43478253417053
aSpacecraft.PlanetodeticVMAG = 6.905049647178043
aSpacecraft.PlanetodeticAZI = 81.80908019170981
aSpacecraft.PlanetodeticHFPA = 1.494615714741736
Set a Spacecraft’s Earth MJ2000 ecliptic coordinates in the Equinoctial
representation:
Create Spacecraft aSpacecraft
aSpacecraft.CoordinateSystem = EarthMJ2000Ec
aSpacecraft.SMA = 9100
aSpacecraft.EquinoctialH = 0.00905
aSpacecraft.EquinoctialK = 0.00424
aSpacecraft.EquinoctialP = -0.1059
aSpacecraft.EquinoctialQ = 0.14949
aSpacecraft.MLONG = 247.4528

## Spacecraft Visualization Properties
