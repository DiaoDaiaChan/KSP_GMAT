# SolarSystem

> 来源：第 965-975 页

SolarSystem
SolarSystem — High level solar system configuration options
Description
The SolarSystem resource allows you to define global properties for the model
of the solar system including the ephemeris source for built-in celestial bodies
and selected settings to improve performance when medium fidelity modelling is
acceptable for your application. This resource cannot be modified in the mission
sequence.
Note
As of release R2015a, GMAT uses two separate solar system
configurations for core parts of the system. For propagation,
GMAT uses the source specified by
SolarSystem.EphemerisSource and the CelestialBody
properties configured on each resource. For event location with
the new ContactLocator and EclipseLocator resources,
GMAT always uses SPICE data for SolarSystem and
CelestialBody properties. See ContactLocator, EclipseLocator,
and CelestialBody for details.
See Also: CelestialBody, LibrationPoint, Barycenter, CoordinateSystem
Fields
Field Description
DEFilename
The path and name of the DE file.
Data Type String
Allowed Values Valid DE file
Access set
Default Value ../data/planetary_ephem/de/leDE1941.405
Units N/A
Interfaces GUI, script
EphemerisSource
The ephemeris model for built-in celestial bodies.
Data Type String
Allowed Values DE405, DE421, DE424, or SPICE
Access set
Default Value DE405
Units N/A
Interfaces GUI, script
EphemerisUpdateInterval
The time between time updates for celetial body ephemeris. For
example, if EphemerisUpdateInterval = 60, if an ephemeris
made at time t = 1200, and a subsequent call is made at
1210, the same ephemeris will be returned for the second
option is for high speed, low fidelity modelling or for
modelling orbits far from third body perturbation sources.
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 0
Units N/A
Interfaces GUI, script
LSKFilename
The path and name of the SPK leap second kernel.
Data Type String
Allowed Values Valid SPK leapsecond kernel
Access set
Default Value ../data/time/naif0011.tls
Units N/A
Interfaces GUI, script
PCKFilename
The path and name of the PCK planetary constants
Data Type String
Allowed Path to valid PCK planetary constants kernel
Values (.tpc)
Access set
Default Value ../data/planetary_coeff/pck00010.tpc
Units N/A
Interfaces GUI, script
SPKFilename
The path and name of the SPK orbit ephemeris kernel.
Data String
Type
Allowed Valid SPK ephemeris kernel (.bsp)
Values
Access set
Default ../data/planetary_ephem/spk/DE405AllPlanets.bsp
Value
Units N/A
Interfaces GUI, script
UseTTForEphemeris
Flag to use Terrestrial Time (TT) as input to the orbital ephemeris
routines. When set to false, TDB is used.
Data Type String
Allowed Values true,false
Access set
Default Value false
Units N/A
Interfaces GUI, script
GUI
The SolarSystem dialog box allows you to configure global properties for solar
system modelling. The default configuration is illustrated above. Use
Ephemeris Source to choose the ephemeris model for built-in celestial bodies.
If you select either DE405, DE421, or DE424 the dialog box above illustrates
available options.
Warning
GMAT allows you to provide user-created DE or SPK kernel
files but we recommend using the files distributed with GMAT.
The files provided with GMAT have been extensively tested for
consistency and accuracy with the original data provided by
JPL and other models in GMAT. Using inconsistent ephemeris
files or user-generated files can result in instability or numerical
issues if the files are not generated correctly.
Changing the ephemeris source for an application is equivalent
to making a fundamental change to the model of the solar
system. We recommend selecting the EphemerisSource early
in the analysis process and using that model consistently. In the
event that an ephemeris model change is necessary, we
recommend that you change the model in the script file and not
via the GUI. We allow you to change EphemerisSource via the
GUI for convenience in early design phases when rigorous
consistency in modelling is less important.
Additionally, when using DE as the EphemerisSource,
modelling is with respect to planetary system barcyenter. When
using SPICE as the EphemerisSource, modelling is with
respect to the planet center.
If you select SPICE for Ephemeris Source, the SolarSystem dialog box
reconfigures to disable the Ephemeris Filename option, indicating that this is
no longer used in this mission..
Remarks
GMAT uses the ephemeris file selected in the EphemerisSource field for all
built-in celestial bodies. For user-defined bodies, the ephemeris model is
specified on the CelestialBody object.
For more information on the DE files provided by JPL see here.
For general information on SPICE ephemeris files see the JPL NAIF site.
For information on the SPK kernel named DE???AllPlanets.bsp
distributed with GMAT, see the Readme-DE???AllPlanets.txt files located
in \data\planetary_ephem\spk in the GMAT distribution.
Note: The SolarSystem and built-in CelestialBody resources require several
hundred fields for full configuration. GMAT only saves non-default values for
SolarSystem and CelestialBody to the script so that scripts are not populated
with hundreds of default settings.
Examples
Use DE421 for ephemeris.
GMAT SolarSystem.EphemerisSource = 'DE421'
Create Spacecraft aSpacecraft
Create Propagator aPropagator
aPropagator.FM = aForceModel
Create ForceModel aForceModel
aForceModel.PointMasses = {Luna, Sun}
BeginMissionSequence
Propagate aPropagator(aSpacecraft) {aSpacecraft.ElapsedSecs = 12000.0}
Use SPICE for ephemeris.
GMAT SolarSystem.EphemerisSource = 'SPICE'
Create Spacecraft aSpacecraft
Create Propagator aPropagator
aPropagator.FM = aForceModel
Create ForceModel aForceModel
aForceModel.PointMasses = {Luna, Sun}
BeginMissionSequence
Propagate aPropagator(aSpacecraft) {aSpacecraft.ElapsedSecs = 12000.0}
