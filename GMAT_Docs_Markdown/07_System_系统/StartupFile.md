# Startup File

> 来源：第 1797-1809 页

Startup File
Startup File — The gmat_startup_file.txt configuration file
Description
The GMAT startup file (gmat_startup_file.txt) contains basic configuration
settings for the GMAT application. This includes the locations of data files and
plugins, search paths for user-defined functions, and various options that control
execution.
The startup file must be located in the same location as the GMAT executable,
and must be named gmat_startup_file.txt. GMAT loads the startup file once
during program initialization.
File Format
Basic Syntax
The startup file is a text file containing characters from the 7-bit US-ASCII
character set. The startup file is case-sensitive.
Lines are terminated by any of the following ASCII character sequences:
line feed (hex: 0A)
carriage return (hex: 0D)
carriage return followed by line feed (hex: 0D0A)
White space can appear above or below any line and before or after any key or
value. The following characters are recognized as white space:
space (hex: 20)
horizontal tab (hex: 09)
Comments begin with the number sign (“#”) and must appear on their own line.
Inline comments are not allowed.
Setting Properties
Properties are specified via key-value pairs, with the following syntax:
PROPERTY = VALUE
Properties are one word, with no spaces. Values extend from the first non-
whitespace character after the equal sign to the end of the line. At least one
whitespace character is required on both sides of the equal sign.
Properties are named according to the following conventions:
Properties that accept directory paths end with “_PATH”.
Properties that accept file paths end with “_FILE”.
The behavior of duplicate property entries is dependent on the individual
property. In general:
Multiple PLUGIN entries cause GMAT to load each named plugin.
Multiple identical *_FUNCTION_PATH entries add each path to the search
path, starting with the first.
Multiple identical *_FILE entries are ignored; the last value is used.
Accessing Property Values
The value of any property ending in “_PATH” (including custom ones) can be
referenced by other values. To reference a value, include the property name as
part of the value. Repeated slash characters are collapsed. For example:
ROOT_PATH = ../
OUTPUT_PATH = ROOT_PATH/output/
sets OUTPUT_PATH to a value of "../output/".
File Paths
Forward slashes and backslashes can be used interchangeably, and can be mixed
in a single path. The following three paths are considered identical:
data/planetary_ephem/spk/de421.bsp
data\planetary_ephem\spk\de421.bsp
data\planetary_ephem/spk\de421.bsp
Absolute paths are passed to the underlying operating system as-is, aside from
normalizing the slashes.
Relative paths are relative to the location of the GMAT executable.
Properties
The available properties are shown here, with default values where appropriate.
System
ROOT_PATH=../
Path to GMAT root directory.
Plugins
PLUGIN
Path to plugin library, without extension. Multiple PLUGIN properties are
allowed, one per plugin.
User Functions
GMAT_FUNCTION_PATH
Search path for GMAT function files (.gmf files). May occur multiple times
to add multiple paths.
MATLAB_FUNCTION_PATH
Search path for MATLAB function files (.m files). May occur multiple
times to add multiple paths.
PYTHON_MODULE_PATH
Search path for Python modules. May occur multiple times to add multiple
paths.
Output
LOG_FILE=OUTPUT_PATH/GmatLog.txt
Path of application log file
MEASUREMENT_PATH=OUTPUT_PATH/
Path of simulated measurement data files. Only used with the
libGmatEstimation plugin.
OUTPUT_PATH=../output/
Output directory path for ReportFile resources.
SCREENSHOT_FILE=OUTPUT_PATH/OUTPUT_PATH
Output path and base filename for screenshots. The base filename is
appended with “_###.png”, where “###” is a number sequence starting from
001. If the base filename is missing, it defaults to “SCREEN_SHOT”.
VEHICLE_EPHEM_PATH=OUTPUT_PATH/
Default output directory path for EphemerisFile resources.
Data Files
Note this section only discusses the paths that can be set via the startup file. See
Configuring Data Files or a discussion of file contents of data files that are
regularly updated and how to maintain those files.
CELESTIALBODY_POT_PATH=DATA_PATH/gravity/celestialbody/
Search path for gravity potential files for CELESTIALBODY. CELESTIALBODY is
the name of any celestial body defined in a given GMAT mission. This
property has no default for user-defined celestial bodies.
ATMOSPHERE_PATH
Path to directory containing atmosphere model data.
BODY_3D_MODEL_PATH
Path to directory containing CelestialBody 3D model files.
CSSI_FLUX_FILE
Path to default CSSI solar flux file.
DATA_PATH=ROOT_PATH/data/
Path to directory containing data files.
DE405_FILE=DE_PATH/leDE1941.405
Path to DE405 DE-file ephemeris file.
DE421_FILE
Path to DE421 DE-file ephemeris file.
DE424_FILE
Path to DE424 DE-file ephemeris file.
EGM96_FILE=EARTH_POT_PATH/EGM96.cof
Path to EGM-96 Earth gravity potential file.
EOP_FILE
Path to IERS “EOP 08 C04 (IAU1980)” Earth orientation parameters file.
ICRF_FILE
Path to data required for computing rotation matrix from FK5 to ICRF
(ICRF_Table.txt).
JGM2_FILE=EARTH_POT_PATH/JGM2.cof
Path to JGM-2 Earth gravity potential file.
JGM3_FILE=EARTH_POT_PATH/JGM3.cof
Path to JGM-3 Earth gravity potential file.
LEAP_SECS_FILE=TIME_PATH/tai-utc.dat
Path to cumulative leap seconds file from http://maia.usno.navy.mil.
LP165P_FILE=LUNA_POT_PATH/LP165P.cof
Path to LP165P Moon gravity potential file.
LSK_FILE
Path to SPICE leap second kernel.
MARINI_TROPO_FILE
Path to file containing location specific atmospheric data needed for the
Marini tropospheric model.
MARS50C_FILE=MARS_POT_PATH/Mars50c.cof
Path to Mars50c Mars gravity potential file.
MGNP180U_FILE=VENUS_POT_PATH/MGNP180U.cof
Path to MGNP180U Venus gravity potential file.
NUTATION_COEFF_FILE=PLANETARY_COEFF_PATH/NUTATION.DAT
Path to nutation series data for FK5 reduction (NUTATION.DAT).
PLANETARY_COEFF_PATH=DATA_PATH/planetary_coeff/
Path to directory containing planetary coefficient files.
PLANETARY_EPHEM_DE_PATH
Path to directory containing DE ephemeris files.
PLANETARY_EPHEM_SPK_PATH
Path to directory containing SPICE planetary ephemeris files.
PLANETARY_PCK_FILE
Path to SPICE planetary constants kernel for default celestial bodies.
PLANETARY_SPK_FILE
Path to SPICE ephemeris kernel for default celestial bodies.
SCHATTEN_FILE
Path to default Schatten solar flux predict file.
SPACECRAFT_MODEL_FILE
Default spacecraft 3D model file.
SPAD_PATH
Path to directory containing SPAD data files.
SPAD_SRP_FILE
Path to default SPAD SRP model.
TIME_PATH=DATA_PATH/time/
Path to directory containing leap-second files.
VEHICLE_EPHEM_CCSDS_PATH
Path to directory containing spacecraft CCSDS-OEM ephemeris files.
VEHICLE_EPHEM_SPK_PATH
Path to directory containing spacecraft SPK ephemeris files.
VEHICLE_MODEL_PATH
Path to directory containing 3D spacecraft models.
Application Files
CELESTIALBODY_TEXTURE_FILE=TEXTURE_PATH/DefaultTextureFile.jpg
Path to texture file for CELESTIALBODY. CELESTIALBODY is the
name of any of the built-in celestial bodies in GMAT. DefaultTextureFile is
the default texture file defined for that celestial body.
BORDER_FILE
Path to constellation border catalog.
CONSTELLATION_FILE=STAR_PATH/inp_Constellation.txt
Path to constellation catalog.
GUI_CONFIG_PATH=DATA_PATH/gui_config/
Path to directory containing GUI configuration files.
HELP_FILE
Path to help file.
ICON_PATH=DATA_PATH/graphics/icons/
Path to directory containing application icons.
MAIN_ICON_FILE
Path to GUI icon.
PERSONALIZATION_FILE=DATA_PATH/gui_config/MyGmat.ini
Path to GUI configuration and history file.
SPACECRAFT_MODEL_FILE=MODEL_PATH/aura.3ds
Path to default Spacecraft 3D model file.
SPLASH_FILE=SPLASH_PATH/GMATSplashScreen.tif
Path to GUI splash image.
SPLASH_PATH=DATA_PATH/graphics/splash/
Path to directory containing splash file.
STAR_FILE=STAR_PATH/inp_StarCatalog.txt
Path to star catalog.
STAR_PATH=DATA_PATH/graphics/stars/
Path to directory containing star and constellation catalogs.
TEXTURE_PATH=DATA_PATH/graphics/texture/
Path to directory containing celestial body texture files.
Program Settings
MATLAB_APP_PATH
[OS X only] Path to MATLAB app (.app).
MATLAB_MODE=SHARED
MATLAB interface connection mode. The available options are:
NO_MATLAB
Disables the MATLAB interface.
SHARED
Each GMAT instance shares a single MATLAB connection. Default.
SINGLE
Each GMAT instance uses its own MATLAB connection.
WRITE_GMAT_KEYWORD=ON
Write “GMAT “ prefix before assignment lines when saving a GMAT script
file. Accepted values are ON and OFF.
WRITE_PERSONALIZATION_FILE=ON
Write data on window locations and other local configuration settings to the
GMAT.ini file. Setting to OFF avoids issues encountered when
simultaneous instances of GMAT try to write to the user config file at the
same time, resulting in a system error. Accepted values are ON and OFF.
Debug Settings
DEBUG_FILE_PATH=OFF
Debug file path handling. Accepted values are ON and OFF.
DEBUG_MATLAB=OFF
Debug MATLAB Interface connection. Accepted values are ON and OFF.
DEBUG_PARAMETERS=OFF
Write table of available parameters to log file on startup. Accepted values
are ON and OFF.
HIDE_SAVEMISSION=TRUE
Hide the SaveMission command from the GUI. Accepted values are TRUE
and FALSE.
PLOT_MODE
XYPlot window placement mode. The only accepted value is TILE, which
will cause GMAT to ignore plot window placement fields and tile the
windows.
RUN_MODE
GMAT execution mode. The available options are:
EXIT_AFTER_RUN
When GMAT is called with the -r or --run command-line argument,
automatically exit after the run is finished.
TESTING
Shows testing options in the GUI.
TESTING_NO_PLOTS
Same as TESTING, but also disables all graphical output in the GUI.
ECHO_COMMANDS
Write commands to log file as they are executed. Accepted values are
TRUE and FALSE.
NO_SPLASH
Skip showing the splash screen on GMAT startup. Accepted values are
TRUE and FALSE.
