# CelestialBody

> 来源：第 395-429 页

CelestialBody
CelestialBody — A celestial body model
Description
The CelestialBody resource is a model of a celestial body containing settings for
the physical properties, as well as the models for the orbital motion and
orientation. GMAT contains built-in models for the Sun, the 8 planets, Earth's
moon, and Pluto. You can create a custom CelestialBody resource to model a
planet, asteroid, comet, or moon. This resource cannot be modified in the
Mission Sequence.
See Also: SolarSystem, Barycenter, LibrationPoint, CoordinateSystem, Color
Fields
Field Description
3DModelFile
Allows you to load 3D models for your celestial body.
.3ds model formats.
Data Type String
Allowed Values . 3ds model formats only
Access set
Default Value empty
Units N/A
Interfaces GUI, script
3DModelOffsetX
This field lets you translate a celestial body in +X
body's coordinate system.
Data Type Real
Allowed Values -3.5 <= Real <= 3.5
Access set
Default Value 0.000000
Units N/A
Interfaces GUI, script
3DModelOffsetY
This field lets you translate a celestial body in +Y
body's coordinate system.
Data Type Real
Allowed Values -3.5 <= Real <= 3.5
Access set
Default Value 0.000000
Units N/A
Interfaces GUI, script
3DModelOffsetZ
This field lets you translate a celestial body in +Z
body's coordinate system.
Data Type Real
Allowed Values -3.5 <= Real <= 3.5
Access set
Default Value 0.000000
Units N/A
Interfaces GUI, script
3DModelRotationX
Allows you to perform a fixed rotation of a celestial
X-axis of central body's coordinate system.
Data Type Real
Allowed Values -180 <= Real <= 180
Access set
Default Value 0.000000
Units Deg.
Interfaces GUI, script
3DModelRotationY
Allows you to perform a fixed rotation of a celestial
Y-axis of central body's coordinate system.
Data Type Real
Allowed Values -180 <= Real <= 180
Access set
Default Value 0.000000
Units Deg.
Interfaces GUI, script
3DModelRotationZ
Allows you to perform a fixed rotation of a celestial
Z-axis of central body's coordinate system.
Data Type Real
Allowed Values -180 <= Real <= 180
Access set
Default Value 0.000000
Units Deg.
Interfaces GUI, script
3DModelScale
Allows you to apply a scale factor to the celestial
Data Type Real
Allowed Values 0.001 <= Real <= 1000
Access set
Default Value 10
Units N/A
Interfaces GUI, script
CentralBody
The central body of the celestial body. The central
primarily by the GUI.
Data Type String
Allowed Comet, Planet, Asteroid, or Moon
Values
Access set
Default For Comet, Planet, Asteroid, the default is
Value Moon, the default is Earth.
Units N/A
Interfaces GUI, script
EquatorialRadius
The body's equatorial radius.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 6378.1363
Units km
Interfaces GUI, script
EopFileName
Optional Earth EOP file to use instead of the EOP
startup file. Note that an emtpy string is the default, and when set to an
empty string, the EOP file defined in the GMAT startup file is used. This
field is only valid for Earth .
Data Type Filename
Allowed Values Valid file name
Access set
Default Value ''
Units N/A
Interfaces script
FileName
Path and/or name of texture map file used in OrbitView
Data String
Type
Allowed A file of the following format:
Values
.jpeg, .bmp, .png, .gif, .tif, .pcx, .pnm, .tga, or
Access set
Default '../data/graphics/texture/GenericCelestialBody.jpg'
Value
Units N/A
Interfaces GUI, script
Flattening
The body's polar flattening.
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 0.0033527
Units N/A
Interfaces GUI, script
FrameSpiceKernelName List of SPICE FK files to load for this body. Used to
properties for use with ContactLocator and EclipseLocator
Remarks.
Data Type String array
Allowed Paths to valid SPICE FK files
Values
Access set
Default Value Varies for built-in bodies. Empty for user-defined
bodies.
Units N/A
Interfaces GUI, script
Mu
The body's gravitational parameter.
Data Type Real
Allowed Values Real > 0
Access set
Default Value 398600.4415
Units km^3/s^2
Interfaces GUI, script
NAIFId
NAIF Integer ID for body.
Data Type Integer
Allowed Values Integer
Access set
Default Value -123456789
Units N/A
Interfaces GUI, script
NutationUpdateInterval
The time interval between updates for Earth nutation
NutationUpdateInterval = 3600, then GMAT only updates
hourly basis.
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 60
Units sec.
Interfaces GUI, script
OrbitColor
Allows you to set available colors on built-in or user-defined
CelestialBody objects that are drawn on the 3D OrbitView
displays. Colors on a CelestialBody object can be set
an integer array. For example: Setting a celestial body's orbit color to red
can be done in the following two ways: CelestialBody.OrbitColor =
Red or Celestialbody.OrbitColor = [255 0 0]
modified in the Mission Sequence as well.
Data Integer Array or String
Type
Allowed Any color available from the Orbit Color Picker in
Values Valid predefined color name or RGB triplet value
and 255.
Access set
Default Orchid for user-defined Planet, Pink for user-defined
Value Comet, Salmon for user-defined Asteroid
defined Moon
Units N/A
Interfaces GUI, script
OrbitSpiceKernelName
List of SPK kernels. Providing emtpy brackets unloads previously loaded
kernels.
Data Type Reference array
Allowed Values valid array of SPK kernels
Access set
Default Value N/A
Units N/A
Interfaces GUI, script
OrientationEpoch
The reference epoch for orientation data.
Data Type String
Allowed Values 6116.0 <= Epoch <= 58127.5
Access set
Default Value 21545.0
Units A1 Modified Julian Epoch
Interfaces GUI, script
PlanetarySpiceKernelName
List of SPICE PCK files to load for this body. Used
body properties for use with ContactLocator and
Remarks.
Data Type String array
Allowed Paths to valid SPICE PCK files
Values
Access set
Default Value Varies for built-in bodies. Empty for user-defined
bodies.
Units N/A
Interfaces GUI, script
PosVelSource
The model for user-defined body orbit ephemeredes.
only supports a single ephemeris model for custom
this is set using PosVelSource field. The default for
SPICE and it is not necessary to configure this field in the current version
of GMAT. This field has no effect for built-in bodies.
Data Type String
Allowed SPICE
Values
Access set
Default Value DE405 for built-in bodies. SPICE
bodies.
Units N/A
Interfaces GUI, script
RotationConstant
The body's spin angle at the orientation epoch.
Data Type Real
Allowed Values Real
Access set
Default Value 190.147
Units deg
Interfaces GUI, script
RotationDataSource
Deprecated.
Data String
Type
Allowed IAUSimplified, DEFile, FK5IAU1980
Values Remarks for more details as not all options are allowed for
all bodies.
Access none
Default See the Remarks for how the default model is chosen based
Value on the celestial body
Units N/A
Interfaces GUI
RotationRate
The body's spin rate.
Data Type Real
Allowed Values Real
Access set
Default Value 360.9856235
Units deg/day
Interfaces GUI, script
SpiceFrameId
SPICE ID of body-fixed frame. Used to define celestial body properties
for use with ContactLocator and EclipseLocator
Data Type String
Allowed Valid SPICE frame ID (text or numeric)
Values
Access set
Default Value Varies for built-in bodies. Empty for user-defined
bodies.
Units N/A
Interfaces GUI, script
SpinAxisDECConstant
The declination of the body's spin axis at the orientation epoch.
Data Type Real
Allowed Values Real
Access set
Default Value 90
Units deg
Interfaces GUI, script
SpinAxisDECRate
The rate of change of the body's spin axis declination.
Data Type Real
Allowed Values Real
Access set
Default Value -0.5570
Units deg/century
Interfaces GUI, script
SpinAxisRAConstant
The right ascension of the body's spin axis at the orientation epoch.
Data Type Real
Allowed Values Real
Access set
Default Value -0.641
Units deg
Interfaces GUI, script
SpinAxisRARate The rate of change of the body's right ascension.
Data Type Real
Allowed Values Real
Access set
Default Value -0.641
Units deg/century
Interfaces GUI, script
TargetColor
Allows you to set available colors on CelestialBody
orbital trajectories that are drawn during iterative processes such as
Differential Correction or Optimization. The target color can be
through a string or an integer array. For example:
perturbing trajectory color to yellow can be done in following two ways:
Celestialbody.TargetColor = Yellow or Celestialbody.TargetColor
= [255 255 0] . This field can be modified in the Mission Sequence as
well.
Data Integer Array or String
Type
Allowed Any color available from the Orbit Color Picker in
Values Valid predefined color name or RGB triplet value
and 255.
Access set
Default Dark Gray for built-in or user-defined
Value Asteroid and Moon
Units N/A
Interfaces GUI, script
TextureMapFileName
Allows you to load a texture map file for your celestial body.
Data Type String
Allowed Values texture map files in jpeg format
Access set
Default Value 'GenericCelestialBody.jpg'
Units N/A
Interfaces GUI, script

GUI
The CelestialBody GUI has three tabs that allow you to set the physical
properties, orbital properties, and the orientation model. CelestialBody
resources can be used in ForceModels, CoordinateSystems, LibrationPoints,
and Barycenters, among others. For a built-in CelestialBody, the Orbit and
Orientation tabs are largely inactive and the behavior is discussed below. To
create a custom Asteroid - as an example of how to create a custom
CelestialBody - perform the following steps.
1. In the Resource Tree, expand the SolarSystem folder.
2. Right-click Sun and select Add -> Asteroid.
3. In the New Asteroid dialog box, type the desired name.
The CelestialBody Properties tab is shown below. GMAT models all bodies as
spherical ellipsoids and you can set the Equatorial Radius, Flattening, and Mu
(gravitational parameter) on this dialog box, as well as the texture map used in
OrbitView graphics displays.
The CelestialBody Orbit tab is shown below for creating a custom
CelestialBody. Settings on this panel are inactive for built-in celestial bodies
and the ephemeris for built-in bodies is configured on the SolarSystem dialog.
The CentralBody field is populated automatically when the object is created
and is always inactive. To configure SPICE ephemerides for a custom body,
provide a list of SPK files and the NAIF ID. See the discussion below for more
information on configuring SPICE files.
The CelestialBody Orientation tab is shown below. Most settings on this panel
are inactive for built-in celestial bodies and exceptions for the Earth and Earth's
moon are described further below. To define the orientation for a celestial body
you provide a reference epoch, the initial orientation at the reference epoch, and
angular rates. See the discussion below for a more detailed description of the
orientation model.
The Earth and Earth's moon have unique fields to configure their orientation
models. The Earth has an extra field called NutationUpdateInterval that can be
used when lower fidelity, higher performance simulations are required.
The CelestialBody Visualization tab is shown below for creating a custom
CelestialBody. On the visualization tab, you can set data such as 3d model of a
celestial body, texture file, translation and rotation of a celestial body on all three
axes, scale of the 3D model as well as assign orbit and target colors to the orbit
of the body.

Remarks
Celestial body orientation model
The orientation of built-in celestial bodies is modeled using high fidelity theories
on a per-body basis. The orientation of Earth is modeled using IAU-1976/FK5.
The orientation of the Moon is modeled using lunar librations from the DE file.
The orientation of Neptune is modeled using IAU-2002. The remaining built-in
celestial body orientations are modeled using data published by the IAU/IAG in
"Report of the IAU/IAG Working Group on Cartographic Coordinates and
Rotational Elements of the Planets and Satellites: 2000".
The orientation of a custom CelestialBody is modeled by providing three angles
and their rates based on IAU/IAG conventions. The figure below illustrates the
angles. The angles αo, δo, and W, are respectively the SpinAxisRAConstant,
SpinAxisDECConstant, and RotationConstant. The angular rates are
respectively SpinAxisRARate, SpinAxisDECRate, and RotationRate. All
angles are referenced to the X-Y plane of the ICRF axis system. The constant
values SpinAxisRAConstant, SpinAxisDECConstant, and RotationConstant
are defined to be the values at the epoch defined in OrientationEpoch.
Below is an example illustrating how to configure a CelestialBody according to
the IAU 2006 recommended values for Vesta. Note the orientation epoch
typically used by the IAU is 01 Jan 2000 12:00:00.00.000 TDB and this must be
converted to A1ModJulian which can easily be performed using the Spacecraft
Orbit dialog box.
Create Asteroid Vesta
Vesta.CentralBody = Sun
% Note that currently the only available
% format for OrientationEpoch is A1ModJulian
Vesta.OrientationEpoch = 21544.99962789878
Vesta.SpinAxisRAConstant = 301.9
Vesta.SpinAxisRARate = 0.9
Vesta.SpinAxisDECConstant = 90.9
Vesta.SpinAxisDECRate = 0.0
Vesta.RotationConstant = 292.9
Vesta.RotationRate = 1617.332776
Note: The orientation models available for Earth and Luna have additional fields
for configuration. Earth has an additional field called NutationUpdateInterval
that controls the update frequency for the Nutation matrix. For high fidelity
applications, NutationUpdateInterval should be set to zero. The
RotationDataSource field for Earth and Luna defines the theory used for the
rotation of those bodies. Currently, only FK5IAU1980 and DE are available for
Earth and Luna respectively and the field is displayed for information purposes
only.
Setting colors on orbits of celestial bodies
GMAT allows you to assign colors to orbits of celestial bodies that are drawn in
the OrbitView graphics display windows. GMAT also allows you to assign
colors to perturbing celestial body orbital trajectories drawn during iterative
processes such as differential correction or optimization. The CelestialBody
object's OrbitColor and TargetColor fields are used to assign colors to both
orbital and perturbing trajectories. See the Fields section for description of these
two fields. Also see Color documentation for discussion and examples on how to
set colors on a celestial body.
Configuring orbit ephemerides
The ephemerides for built-in celestial bodies is specified by the
SolarSystem.EphemerisSource field and the same source is used for all built-in
bodies. Ephemerides for a custom CelestialBody are provided by SPICE files.
Archives of available SPICE files can be found at the JPL NAIF site and the
Solar System Dynamics site . JPL provides utilities to create custom SPICE files
in the event existing kernels don't satisfy requirements for your application. To
create custom SPICE kernels, see the documentation provided by JPL. The list
of NAIF Ids for celestial bodies is located here.
Note that the DE files model the barycenter of planetary systems. So for Jupiter,
when using DE405 for example, you are modeling Jupiter's location as the
barycenter of the Jovian system. SPICE kernels differentiate the barycenter of a
planetary system from the location of the individual bodies. So when using
SPICE to model Jupiter, you are modeling the location of Jupiter using Jupiter's
center of mass.
To specify the SPICE kernels for a custom CelestialBody, use the NAIFId,
CentralBody, and SourceFileName fields. GMAT is distributed with an SPK
file for CERES which has NAIF ID 2000001. Here is how to configure a
CelestialBody to use the CERES SPICE ephemeris data.
Create CelestialBody Ceres
Ceres.CentralBody = Sun
Ceres.SourceFilename = '../data/planetary_ephem/spk/ceres_1900_2100.bsp'
Note: GMAT currently only supports a single ephemeris model for custom
bodies (SPICE) and this is set using PosVelSource field. The default for
PosVelSource is SPICE and it is not necessary to configure this field in the
current version of GMAT.
Warning
NAIF distributes SPICE kernels for many celestial bodies and
each kernel is consistent with a particular primary ephemeris
release such as DE421. For high precision analysis, it is
important to ensure that the ephemerides used for a custom
celestial body are consistent with the ephemeris source
selection in the SolarSystem.EphemerisSource field. SPICE
kernels are typically distributed with a ".cmt" file and in that
file the line that contains the ephemeris model looks like this:
Planetary Ephemeris Number: DE-0421/LE-0421
Configuring physical properties
GMAT models all celestial bodies as spherical ellipsoids. To define the physical
properties use the Flattening, EquatorialRadius, and Mu fields.
Configuring for event location
GMAT's event location subsystem (consisting of ContactLocator and
EclipseLocator) uses celestial body definitions from the SPICE toolkit.
Properites such as radius, flattening, ephemeris, and orientation must be
configured separately for use with the event locators.
CelestialBody shape and orientation are configured via SPICE PCK files,
loaded from two sources in the following order:
1. SolarSystem.PCKFilename
2. Sun.PlanetarySpiceKernelName (in list order), followed by Mercury,
Venus, Earth, Mars, Jupiter, Saturn, Uranus, Neptune, Pluto, Luna
3. User-defined bodies
Data loaded last takes precedence over data loaded first, if there is a conflict.
Note that because the SPICE kernel pool is shared for the entire run, a PCK file
loaded for Pluto may override data loaded by Sun, if the file contains conflicting
data. Note that this order isn't absolute—coordinate systems that with an SPK-
defined origin load differently, for example. To determine the exact load order,
see the GmatLog.txt file.
Note
GMAT's SPICE kernel load order is based on many factors, and
can be unpredictable. Therefore, it is important that the kernels
referenced by a mission be consistent. For example, NAIF's
de421.bsp and mar085.bsp are consistent, because they are
both based on the DE421 model. Inconsistent kernels can cause
unpredictable behavior based on the order in which they are
loaded.
The body-fixed frame for a CelestialBody is defined on the Orientation tab by
the SpiceFrameId and SpiceFrameKernelFile fields. The SpiceFrameId
contains the SPICE ID for the body-fixed frame, which may be built-in or
defined via external FK files. External FK files can be loaded by adding them to
the SpiceFrameKernelFile list for each body. These files are loaded just after
PlanetarySpiceKernelName for each body. The list of built-in frames is
available as an appendix in the SPICE documentation. GMAT's default frames
are:
Earth: ITRF93
Luna: MOON_PA
Other default bodies: IAU_CelestialBody
The Earth ITRF93 frame is defined by three high-fidelity orientation PCK files,
shown below. More information on these files can be found in the NAIF
aareadme.txt file.
earth_start_end_predict.bpc: long-term low-fidelity EOP predictions
earth_start_end.bpc: long-term low-fidelity historical EOP
earth_start_end_filedate.bpc: near-term high-fidelity EOP history and
predictions
The Luna MOON_PA frame is defined by an orientation PCK file and a frame-
defining FK file, shown below. More information can be found in the NAIF
PCK aareadme.txt file and the FK aareadme.txt file. Other versions of the
MOON_PA frame are available from NAIF.
moon_pa_de421_1900-2050.bpc: Moon orientation consistent with DE421
PA frame
moon_080317.tf: MOON_PA frame definition
Examples
Configure a CelestialBody to model Saturn's moon Titan. Note you must obtain
the SPICE kernel named "sat288.bsp" from here and place it in the directory
identified in the script snippet below
Create Moon Titan
Titan.NAIFId = 606
Titan.OrbitSpiceKernelName = { ...
'../data/planetary_ephem/spk/sat288.bsp' ...
}
Titan.SpiceFrameId = 'IAU_TITAN'
Titan.EquatorialRadius = 2575
Titan.Flattening = 0
Titan.Mu = 8978.5215
Titan.PosVelSource = 'SPICE'
Titan.CentralBody = 'Saturn'
Titan.RotationDataSource = 'IAUSimplified'
Titan.OrientationEpoch = 21545
Titan.SpinAxisRAConstant = 36.41
Titan.SpinAxisRARate = -0.036
Titan.SpinAxisDECConstant = 83.94
Titan.SpinAxisDECRate = -0.004
Titan.RotationConstant = 189.64
Titan.RotationRate = 22.5769768
