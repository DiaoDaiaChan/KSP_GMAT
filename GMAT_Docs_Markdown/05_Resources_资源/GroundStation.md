# GroundStation

> 来源：第 677-700 页

GroundStation
GroundStation — A ground station model.
Description
A GroundStation models a facility fixed to the surface of a CelestialBody.
There are several state representations available for defining the location of a
ground station including Cartesian and spherical. This resource cannot be
modified in the mission sequence.
See Also: ContactLocator, CoordinateSystem, Color
Fields
Field Description
AddHardware
List of all Transmitter, Receiver, and Antenna
hardware used by ground station
Data Object Array
Type
Allowed Each element in the list has to be a
Values valid Transmitter, Receiver, or
Antenna
Access set
Default None
Value
Units N/A
Interfaces script
Altitude
The altitude of the station with respect to the
HorizonReference.
Data Type Real
Allowed Values -∞ < Real < ∞
Access set
Default Value 0
Units km
Interfaces GUI, script
CentralBody
The central body of the GroundStation.
Data String
Type
Allowed Earth. (Ground stations are currenly
Values only supported with respect to Earth)
Access set
Default Earth
Value
Units N/A
Interfaces GUI, script
DataSource
Source of where to get Temperature, Pressure,
Humidity, and MinimumElevationAngle. If the
value is Constant, then the values of these
parameters, as set in the GroundStation resource,
remain constant for all relevant measurements.
Currently, the value of Constant is the only allowed
value.
Data Type Enumeration
Allowed Values Constant
Access set
Default Value Constant
Units N/A
Interfaces script
ErrorModels
User-defined list of ErrorModel objects that
describe the measurement error models used for
this GroundStation.
Data Type StringList
Allowed Any valid user-defined
Values ErrorModel resource
Access set
Default None
Value
Units N/A
Interfaces script
HorizonReference
The system used for the horizon. Sphere is
equivalent to Geocentric, Ellipsoid is equivalent to
Geodetic.
Data Type String
Allowed Values Sphere, Ellipsoid
Access set
Default Value Sphere
Units N/A
Interfaces GUI, script
Humidity
Humidity at ground station used to calculate
tropospheric correction for the
HopfieldSaastamoinen model. GMAT only uses
this value if DataSource is set to Constant.
Data Type Real
Allowed Values 0.0 <= Real <=100.0
Access set, get
Default Value 55
Units percentage
Interfaces script
Id
Id of the GroundStation used in simulation and
estimation
Data Type String
Allowed May contain letters, integers, dashes,
Values underscores
Access set,
Default StationId
Value
Units N/A
Interfaces GUI, script
IonosphereModel
Specification of ionospheric model used in the
light time calculations.
Data Type Enumeration
Allowed Values 'None', 'IRI2007'
Access set
Default Value 'None'
Units N/A
Interfaces script
Latitude The latitude of the station with respect to
HorizonReference.
Data Type Real
Allowed Values -90 < Real < 90
Access set
Default Value 0
Units deg.
Interfaces GUI, script
Location1
The first component of the GroundStation
location. When StateType is Cartesian,
Location1 is the x-component of station location
in the body-fixed system. When StateType is
Spherical or Elliposoid, Location1 is the
Longitude (deg.) of the GroundStation.
Data Real
Type
Allowed -∞ < Real < ∞ for Cartesian, See
Values Longitude, Latitude, Altitude for
others.
Access set
Default 6378.1363
Value
Units see description
Interfaces GUI, script
Location2
The second component of the GroundStation
location. When StateType is Cartesian,
Location2 is the y-component of station location
in the body-fixed system. When StateType is
Spherical or Ellipsoid, Location2 is the Latitude
(deg.) of the GroundStation.
Data Real
Type
Allowed -∞ < Real < ∞ for Cartesian, See
Values Longitude, Latitude, Altitude for
others.
Access set
Default 0
Value
Units see description
Interfaces GUI, script
Location3
The third component of the GroundStation
location. When StateType is Cartesian,
Location3 is the z-component of station location in
the body-fixed system. When StateType is
Spherical or Elliposoid, Location3 is the height
(km) of the GroundStation above the reference
shape.
Data Reals
Type
Allowed -∞ < Real < ∞ for Cartesian, See
Values Longitude, Latitude, Altitude for
others.
Access set,
Default 0
Value
Units see description
Interfaces GUI, script
Longitude The longitude of the station.
Data Type Real
Allowed Values value >=0
Access set
Default Value 0
Units deg.
Interfaces GUI, script
MinimumElevationAngle
Minimum elevation angle constraint for use with
ContactLocator. For navigation related
processing, this is minimum elevation angle for
signal transmitted from spacecraft to ground
station. During simulation, this is the minimum
elevation angle required in order for data to be
output. During estimation, this is the minimum
elevation angle required for data to be used to
calculate an estimate. GMAT only uses this value if
DataSource is set to Constant.
Data Type Real
Allowed -90 ≤ MinimumElevationAngle
Values ≤ 90
Access set
Default Value 7
Units deg
Interfaces GUI, script
OrbitColor
Allows you to select available colors for a user-
defined GroundStation. The GroundStation
object is drawn on a spacecraft's ground track plot
created by GroundTrackPlot 2D graphics display
resource. The colors can be identified through a
string or an integer array. For example: Setting
groundstation's color to red can be done in
following two ways: GroundStation.OrbitColor
= Red or GroundStation.OrbitColor = [255 0
0]. This field can be modified in the Mission
Sequence as well.
Data Integer Array or String
Type
Allowed Any color available from the Orbit
Values Color Picker in GUI. Valid predefined
color name or RGB triplet value
between 0 and 255.
Access set
Default Thistle
Value
Units N/A
Interfaces GUI, script
Pressure
Air pressure at ground station used to calculate
tropospheric correction for the
HopfieldSaastamoinen model. GMAT only uses
this value if DataSource is set to Constant.
Data Type Real
Allowed Values Real >0.0
Access set, get
Default Value 1013.5
Units hPa
Interfaces script
StateType
The type of state used to define the location of the
ground station. For example, Cartesian or
Ellipsoid.
Data Type String
Allowed Values Cartesian, Spherical, Ellipsoid
Access set
Default Value Cartesian
Units N/A
Interfaces GUI, script
SpiceFrameId
The station's SPICE frame ID. Note this field does
not have a default, and is not saved to script, unless
it is set to a specific allowed value.
Data String or Integer
Type
Allowed Valid SPICE frame ID (text or
Values numeric). The convention for stations
is '399xyz', where 'xyz' are integers
mapped to the station. For example,
DSN station 'DSS-66' has Id '399066'.
Access set
Default No default.
Value
Units N/A
Interfaces script
TargetColor
Allows you to select available colors for a user-
defined GroundStation object during iterative
processes such as Differential Correction or
Optimization. The target color can be identified
through a string or an integer array. For example:
Setting groundstation's target color to yellow color
can be done in following two ways:
GroundStation.TargetColor = Yellow or
GroundStation.TargetColor = [255 255 0].
This field can be modified in the Mission Sequence
as well.
Data Integer Array or String
Type
Allowed Any color available from the Orbit
Values Color Picker in GUI. Valid predefined
color name or RGB triplet value
between 0 and 255.
Access set
Default DarkGray
Value
Units N/A
Interfaces GUI, script
Temperature
Air temperature at ground station used to calculate
tropospheric correction for the
HopfieldSaastamoinen model. GMAT only uses
this value if DataSource is set to Constant.
Data Type Real
Allowed Values Real >0.0
Access set, get
Default Value 295.1
Units Kelvin
Interfaces script
TroposphereModel
Specification of tropospheric model used in the
light time calculations.
Data Type Enumeration
Allowed 'None', 'HopfieldSaastamoinen',
Values 'Marini'
Access set
Default 'None'
Value
Units N/A
Interfaces script
GUI
To create a GroundSation, starting from the Resource Tree:
1. Right-click the GroundStation folder and select Add Ground Station.
2. Double-click GroundStation1.
You can set the ground station location in several state representations. The
Cartesian representation is illustrated above. To set the Longitude, Latitude,
and Altitude to 45 deg., 270 deg., and 0.1 km respectively, with respect to the
reference ellipsoid:
1. In the StateType menu, select Spherical.
2. In the HorizonReference menu, select Ellipsoid.
3. In the Latitude text box, type 45.
4. In the Longitude text box, type 270.
5. In the Altitude text box, type 0.1.

Remarks
The GroundStation model allows you to configure a facility by defining the
location in body-fixed coordinates using one of several state representations.
GMAT supports Cartesian, Sphere, and Ellipsoid representations and examples
below show how to configure a GroundStation in each representation. When
using the Ellipsoid model or Sphere representations, GMAT uses the physical
properties - flattening and radius for example - defined on the CelestialBody
resource.
Setting Colors On a Ground Station Facility
GMAT allows you to set colors on a ground station facility that you create. The
GroundStations are drawn on the GroundTrackPlot 2D graphics display. The
GroundStation object's OrbitColor and TargetColor fields are used to set
colors on a ground station facility. See the Fields section to read more about
these two fields. Also See Color documentation for discussion and examples on
how to set colors on a ground station facility.
Marini Troposphere Model Data File
The Marini troposphere model utilizes a data file which contains monthly mean
values for the model calculation for different locations on the Earth's surface.
This data file's location is specified by the MARINI_TROPO_FILE property in the
startup file. Each line in the data file contains a latitude longitude pair, followed
by 12 values, one for each month of the year. Each value in the data file
combines both the refractivity and a scale height factor into a single integer,
which are both used in the Marini model. The two rightmost digits are used to
obtain the scale height, while the remaining digits to the left represent the
refractivity. The digits used for the scale height have the decimal point placed
between the two digits, while the refractivity values have the decimal point
placed at the right of its rightmost digit. For example, a value in the data file of
37068 would correspond to a refractivity of 370, and a scale height of 6.8.
The line in the data file is selected for use if it is within one degree of latitude
and one degree of longitude of the ground station location. The column is then
selected based on the month of the year. If the location of the ground station is
within one degree of latitude and longitude of multiple locations in the data file,
the first line is the one selected. If the location of the ground station is not within
one degree of latitude and longitude of a location in the data file, a default value
of 37068 is used instead, regardless of month. The latitude ranges from -90 to 90
degrees, while the longitude spans from 0 to 360 degrees.
Examples
Configure a GroundStation in Geodetic coordinates.
Create GroundStation aGroundStation
aGroundStation.CentralBody = Earth
aGroundStation.StateType = Spherical
aGroundStation.HorizonReference = Ellipsoid
aGroundStation.Location1 = 60
aGroundStation.Location2 = 45
aGroundStation.Location3 = 0.01
% or alternatively
aGroundStation.Latitude = 60
aGroundStation.Longitude = 45
aGroundStation.Altitude = 0.01
Configure a GroundStation in Geocentric coordinates.
Create GroundStation aGroundStation
aGroundStation.CentralBody = Earth
aGroundStation.StateType = Spherical
aGroundStation.HorizonReference = Sphere
aGroundStation.Location1 = 59.83308194090783
aGroundStation.Location2 = 45
aGroundStation.Location3 = -15.99424674414058
% or alternatively
aGroundStation.Latitude = 59.83308194090783
aGroundStation.Longitude = 45
aGroundStation.Altitude = -15.99424674414058
Configure a GroundStation in Geocentric coordinates.
Create GroundStation aGroundStation
aGroundStation.CentralBody = Earth
aGroundStation.StateType = Cartesian
aGroundStation.Location1 = 2260.697433050543
aGroundStation.Location2 = 2260.697433050542
aGroundStation.Location3 = 5500.485954732006
Configure a GroundStation that, when used for navigation, will model how the
RF signal is refracted in the atmosphere.
Create GroundStation aGroundStation
aGroundStation.IonosphereModel = 'IRI2007';
aGroundStation.TroposphereModel = 'HopfieldSaastamoinen';
BeginMissionSequence;
Attach a Transmitter and Receiver resource to a GroundStation.
Create Transmitter Transmitter1
Create Receiver Receiver1
Create GroundStation aGroundStation;
aGroundStation.AddHardware = {Transmitter1, Receiver1};
BeginMissionSequence;
