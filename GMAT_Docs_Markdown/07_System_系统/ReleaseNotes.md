# Release Notes

> 来源：第 1820-1894 页

Release Notes
GMAT R2018a Release Notes
The General Mission Analysis Tool (GMAT) version R2018a was released
March 2018. This is the first public release since June, 2017, and is the 12th
release for the project.
Below is a summary of key changes in this release. Please see the full R2018a
Release Notes on JIRA for a complete list.
Milestones and Accomplishments
We're excited that GMAT has recently seen signficant adoption for operational
misssion support.
GMAT is now used as the primary system for maneuver planning and
product generation for the Solar Dynamics Observatory (SDO).
GMAT is now used as the primary operational tool for orbit determination
for the Solar and Heliospheric Observatory (SOHO) mission.
GMAT is now used as the primary operational tool for maneuver planning,
orbit determination, and product generation for the Advanced Composition
Explorer (ACE) mission.
GMAT is now used as the primary operational tool for maneuver planning,
orbit determination, and product generation for the Wind mission.
In April 2018, the Transiting Exoplanet Survey Satellite (TESS) mission is
planned to launch. TESS has used GMAT as its primary tool for mission
design and maneuver planning from proposal development through
operations.
In April 2018, the LRO project will hold an operational readiness review to
perform final evaluation of GMAT to replace GTDS as the primary
operational orbit determination (OD) tool for the Lunar Reconnaissance
Orbiter (LRO).
New Features
Orbit Determination Enhancements
The following new features and capabilities have been added to GMAT's
estimation system.
The batch estimator now supports a capability that freezes the
measurements used for estimation after a user-specified number of
iterations. This functionality avoids estimator chatter that can occur near
solutions when some measurements are near the sigma edit boundary and
are repeatedly removed during one iteration and then added back in the next
iteration.
Numerics are improved when calculating Doppler and DSN_TCP
measurement residuals, improving noise behavior in the residuals.
The GroundStation object supports a new troposphere model, the Marini
model, matchig the implementation used in GTDS. One operational
advantage of the Marini model is that it doesn’t require input of weather
data at the Ground station. (Models that do accept weather data may have
more accuracy.)
Time is now modeled using three data members, a day number,
seconds of day, and fraction of second. High precision time is
surgically implemented in appropriate models such as Earth rotation,
planetary ephemerides and others.
Range differences are computed using a Taylor series and differenced
Chebyshev polynomials.
Measurement simulation now accounts for central body occultation when
orbiting bodies other than the Earth.
Estimation now supports solving for the Keplerian state estimation with a
priori constraints.
For BLS estimation, the user may choose to perform measurement editing
using either the weighted root-mean-square (WRMS) of residuals, or the
predicted weighted root-mean-square (WRMSP) of residuals. Residuals of
elevation edited data are now reported.
The batch estimator report now shows the name of input files used in the
configuration and the observation time span. Additionally, spacecraft
hardware configurations and new measurement statistics information are
included.
GMD file improvements
As shown by the new features above, GMAT’s orbit determination (OD)
capability has been significantly enhanced. As with all new releases, missions
that use GMAT’s OD capability should perform a baseline set of
regression/performance tests prior to using the new version of GMAT OD for
operational purposes.
Example scripts:
See Ex_R2018a_CompareEphemeris.script for a new example on
performing ephemeris compares at non-Earth bodies.
See Ex_R2018a_MergeEphemeris.script for an example demonstrating
merging ephemerides.
Built-in Optimizer
GMAT now contains a built-in optimizer called Yukon, developed by the GMAT
team. The algorithm uses an SQP line search algorithm with an active set QP-
subproblem algorithm. Yukon is designed for small scale problems and is not
applicable to large, sparse optimization problems. See the Yukon reference for
more information.
Improvements
Tide modeling is improved, and GMAT now supports lunar tides.
STM propagation now includes variational terms from drag models.
The degree and order of STM contributions from harmonic gravity is now
settable by the user and defaults to the maximum order on the gravity file or
100, whichever is lower.
The buffer size that determines the number of plot points stored by the
OrbitView Resource is now exposed to the user.
Significant performance improvements have been made in the IRI2007
ionosphere model.
The script editor highlights errors and warnings found on the first pass of
parsing.
GMAT now supports body fixed and TOD coordinate systems for Code 500
Ephemerides and supports all central bodies in the Code 500 Ephemeris
format.
The CommandEcho command has been added to GMAT to support printing
commands to the message window and log file as they are executed in a
mission sequence. This command is particularly useful when debugging
user scripts. See the CommandEcho reference for more information.
The Code500 propagator type now automatically detects the endianness
when reading Code500 files.
The STK ephemeris propagator now uses Hermite interpolation, and
includes velocity information in the position interpolation for segments that
contain fewer than 7 rows of data. Velocity interpolation for segments with
fewer than 7 rows of data is performed by forming the hermite interpolating
polynomial for position, and then differentiating the position interpolating
polynomial to obtain the velocity.
You can now set the step size of an ephemeris propagator during mission
execution (i.e. after the BeginMissionSequence command).
The startup file now allows optional updating of the user configuration file.
This avoids issues encountered when simultaneous instances of GMAT try
to write to the user config file at the same time, resulting in a system error.
The Python data file utility now updates data files used by the IRI2007
model.
The GMAT CMake based build system now supports plugin components
developed by external groups.
GMAT now supports GUI plugin components.
Compatibility Changes
Batch estimation now requires the use of fixed step integration.
The RotationDataSource on CelestialBody Resources is deprecated and no
longer has an effect.
The Spacecraft EstimationStateType parameter is deprecated.
The EphemerisFile OutputFormat options ‘UNIX’ and ‘PC’ are deprecated.
‘BigEndian’ and ‘LittleEndian’ should be used instead.
The EarthTideModel on the ForceModel Resource has been renamed to
TideModel
GMAT now returns error codes via the command line interface to indicate if
issues were encountered during system execution.
When using the Write command to write Resource properties to a
ReportFile, only scalar, real quantities are written. Properties that are either
not real or are arrays are ignored and a warning is issued.
Upcoming Changes in R2019a
This is the last version of GMAT tested on Windows 7.
Known & Fixed Issues
Fixed Issues
Over 112 bugs were closed in this release. See the "Critical Issues Fixed in
R2018a" report for a list of critical bugs and resolutions in R2018a. See the
"Minor Issues Fixed for R2018a" report for minor issues addressed in R2018a.
The STK ephemeris propagator now correctly handles segments with fewer
than 5 rows of data.
STK ephemeris files that contain event boundaries now correctly count the
number of ephemeris rows represented in the NumberOfEphemerisPoints
keyword value pair.
Comments describing the source of ephemeris discontinuities in CCSDS
ephemeris files are now written inside of meta data blocks.
Known Issues
See the "All Known Issues for R2018a" report for a list of all known issues in
R2018a.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-5417 Adaptive step size control behaves inconsistently when used in
GMAT's navigation system. Fixed step integration is currently
required for simulation and estimation.
GMT-6202 Spikes of up to 1 mm/sec may be observed in some cases in
DSN_TCP and Doppler ionospheric corrections. The IRI2007
model has some jumps in the electron density when moving
through time. Spikes are caused when the start and end signal
paths are located on different sides of these jumps.
GMT-6367 For Macs with a Touch Bar (GUI issue only): there appears to be
an issue with WxWidgets, the third party GUI library used by
GMAT, and the Mac Touch Bar. Crashes occur frequently and the
traceback indicates that the issue lies in Apple code, related to the
Touch bar specifically, possibly caused by a NULL string pointer.
Our analysis suggests this issue cannot be addressed by the GMAT
team or by WxWidgets; however, we will continue to investigate.
In the meantime, the GMAT Console version will continue to
work, and the GUI version (Beta) will work on Macs without a
Touch Bar.
GMAT R2017a Release Notes
The General Mission Analysis Tool (GMAT) version R2017a was released June
2017. This is the first public release since Oct. 2016, and is the 11th release for
the project. This is the first 64 bit version of GMAT on Windows (Mac and
Linux are 64 bit only).
Below is a summary of key changes in this release. Please see the full R2017a
Release Notes on JIRA for a complete list.
New Features
Orbit Determination Enhancements
The following new features and capabilities have been added to GMAT.
Three new data types can now be processed in GMAT; GPS point solution
(GPS_PosVec), range data (Range), and range rate (RangeRate) data. Note
that all of these data types have been through regression testing but only the
DSN range data type has been through substantial operational testing. Thus,
the DSN range data type is the most validated data type available in GMAT.
A minimally tested and documented alpha version of an extended Kalman
filter algorithm is now available for experimental use. This plugin is
available but turned off by default. To use, enable the "libEKF" plugin in
the startup file.
A second-level data editing capability has been added. This feature allows
you to choose observations that are computed and reported but not used in
the estimation state update.
STK .e Ephemeris Propagator
GMAT now supports a propagator that uses AGI's .e ephemeris file format. See
the Propagator reference for more information.
File Manager Utility
You can now manage empirical data updates using a Python file manager. The
utility allows you to easily update leap second, EOP, space weather, and other
files and optionally archive old versions. See the Configuring GMAT Data Files
section for more information. When you run the the utility, you will see output
like that shown below (the data below is only a partial summary of the output).
--------UPDATING GMAT LEAP SECOND FILE ------------------------------
Process Began At 2017-06-01-11:23:55
--------Downloading tai-utc.dat
tai-utc.dat downloaded successfully
tai-utc.dat archived successfully to 2017-06-01-11h23m55s_tai-utc.dat
tai-utc.dat updated successfully
Process Finished At 2017-06-01-11:23:55
--------UPDATING GMAT EOP FILE --------------------------------
Process Began At 2017-06-01-11:23:55
--------Downloading eopc04_08.62-now
eopc04_08.62-now downloaded successfully
eopc04_08.62-now archived successfully to
2017-06-01-11h23m57s_eopc04_08.62-now
eopc04_08.62-now updated successfully
---------UPDATING SPICE LEAP SECOND FILE -----------------------
Process Began At 2017-06-01-11:23:57
--------Downloading naif0011.tls
SPICELeapSecondKernel.tls downloaded successfully
--------Downloading naif0012.tls
SPICELeapSecondKernel.tls downloaded successfully
SPICELeapSecondKernel.tls archived successfully to
2017-06-01-11h24m00s_SPICELeapSecondKernel.tls
SPICELeapSecondKernel.tls updated successfully
Process Finished At 2017-06-01-11:24:00
Collocation Stand Alone Library and Toolkit (CSALT)
GMAT now has a stand alone C++ library for solving optimal control problems
via collocation (CSALT). The library is well tested and available for
applications, and is currently undergoing integration into GMAT. The CSALT
library is not exposed via GMAT interfaces, but users who are familiar with C++
programming can solve optimal control problems with CSALT now. The source
code will be made available via SourceForge. CSALT integration into GMAT is
underway and planned for completion in the next GMAT release. For more
information on the CSALT Library see the paper entitled
CSALT_CollocationBenchmarkingResults.pdf in the docs folder of the GMAT
distribution.
Preliminary API Interface
A preliminary API is under development. The API is not available in the
production release and is distributed separately on SourceForge in packages with
the name "Alpha" in the title. The API employs SWIG to expose GMAT to
several languages. Preliminary testing has been performed on the JAVA interface
called from MATLAB. The code snippet below illustrates how to call through
the JAVA interface from MATLAB to compute orbital accelerations on a
spacecraft. Some testing of the Python binding as also been performed.
% Load GMAT
scriptFileName = fullfile(pwd, 'gmat.script');
[myMod, gmatBinPath, result] = load_gmat(scriptFileName);
% Get the SolarSystem object from GMAT
ss = myMod.GetDefaultSolarSystem();
% Prepare the force model to be used for dynamics
fm = myMod.GetODEModel('DefaultProp_ForceModel');
state = gmat.GmatState(6+6^2);
fm.SetSolarSystem(ss); % Set solar system pointer in force model
fm.SetState(state); % Provide force model with the state placeholder
% Create new Spacecraft
sat = gmat.Spacecraft('Sat');
% Create PropagationStateManager to manage calculation of derivatives
propManager = gmat.PropagationStateManager();
propManager.SetObject(sat); % Add sat PropagationStateManager
propManager.SetProperty('AMatrix', sat); % Want to calculate Jacobian
propManager.BuildState();
% Tell force model to use propmanager
fm.SetPropStateManager(propManager);
fm.UpdateInitialData(); % Update model with changes
fm.BuildModelFromMap(); % Sets up the models in the force model
state = gmat.gmat.convertJavaDoubleArray(x(:,tIndex));
% Compute the orbital accelerations including variational terms
fm.GetDerivatives(state, t(tIndex), 1); % Calculate derivatives
deriv = fm.GetDerivativeArray(); % Get calculated derivatives
derivArray = gmat.gmat.convertDoubleArray(deriv, 42);
Improvements
You can now define the name and location of the gmat startup and log file
via the command line interface. This is useful when running multiple
GMAT sessions simultaneously or when you have complex, custom file
configurations.
You can now write STK ephem files with units in meters (previously, only
km was supported).
You can now write STK ephem files without discrete event boundaries.
Compatibility Changes
GMAT now requires Python version 3.6.x.
Schatten files no longer require the "PREDICTED SOLAR DATA"
keyword at the top of the file.
The names and locations of several data files used by GMAT are no longer
hard coded and their names and locations are set in the file
gmat_startup_file.txt located in the bin directory. If you use custom
startup files, you MUST add the lines below to your startup file before
GMAT will start. Note that the startup files distributed with GMAT have
these lines added. This backwards compatiblity issue only affects users who
customize their startup file.
EARTH_LATEST_PCK_FILE = PLANETARY_COEFF_PATH/earth_latest_high_prec.bpc
EARTH_PCK_PREDICTED_FILE = PLANETARY_COEFF_PATH/SPICEEarthPredictedKernel.bpc
EARTH_PCK_CURRENT_FILE = PLANETARY_COEFF_PATH/SPICEEarthCurrentKernel.bpc
LUNA_PCK_CURRENT_FILE = PLANETARY_COEFF_PATH/SPICELunaCurrentKernel.bpc
LUNA_FRAME_KERNEL_FILE = PLANETARY_COEFF_PATH/SPICELunaFrameKernel.tf
The syntax for navigation functionality has been significantly changed for
consistency throughout the system. See the Deprecated Measurement
Type Names section of the Tracking Data Types for OD Help for more
details.
GMAT Stuff
Don't forget you can purchase clothing and other items with the GMAT logo via
©Land's End, Inc at the GSFC Store . Once, you've chosen an item, make sure to
select the GMAT logo!
Known & Fixed Issues
Over 70 bugs were closed in this release. See the "Critical Issues Fixed in
R2017a" report for a list of critical bugs and resolutions in R2017a. See the
"Minor Issues Fixed for R2017a" report for minor issues addressed in R2017a.
Known Issues
All known issues that affect this version of GMAT can be seen in the "Known
Issues in R2017a" report in JIRA.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-5269 Atmosphere model affects propagation at GEO.
GMT-2561 UTC Epoch Entry and Reporting During Leap Second is incorrect.
GMT-3043 Inconsistent validation when creating variables that shadow built-
in math functions
GMT-3289 First step algorithm fails for backwards propagation using SPK
propagator
GMT-3350 Single-quote requirements are not consistent across objects and
modes
GMT-3669 Planets not drawn during optimization in OrbitView
GMT-3738 Cannot set standalone FuelTank, Thruster fields in
CallMatlabFunction
GMT-4520 Unrelated script line in Optimize changes results (causes crash)
GMT-4398 Coordinate System Fixed attitudes are held constant in SPAD SRP
model during a propagation step
GMT-5600 Numerical Issues when calculating the Observation Residuals
GMT-6040 Correct the code for the RunSimulator and RunEstimator
commands so that they respect the scripted propagator settings
GMT-5881 Error in Ionosphere modeling
GMAT R2016a Release Notes
The General Mission Analysis Tool (GMAT) version R2016a was released Oct.
2016. This is the first public release since Nov. 2015, and is the 10th release for
the project. Note this will be the last 32 bit version of GMAT on Windows
(Mac and Linux are 64 bit only).
Below is a summary of key changes in this release. Please see the full R2016a
Release Notes on JIRA for a complete list.
New Features
Orbit Determination
GMAT now supports orbit determination with a focus on batch estimation of
DSN data types including range and Doppler. We’ve been working on navigation
functionality for several releases, but this is the first production release
containing navigation functionality. Orbit determination functionality has
undergone a rigorous QA process including shadow testing in GSFC’s Flight
Dynamics Facility and is extensively documented in tutorials and reference
material. Navigation components include BatchEstimator, Simulator,
ErrorModel, StatisticsAcceptFilter, StatisticsRejectFilter, TrackingDataSet, and
the RunEstimator and RunSimulator Commands. We recommend taking the
tutorials first then reviewing the reference material for orbit determination
components to get started.
See the Simulation and Estimation tutorials for more information.
Code 500 Ephemeris Propagator
GMAT now supports a propagator that uses GSFC’s Code 500 ephemeris file
format. The Code 500 file format is legacy format still used by some systems at
GSFC. This functionality allows users of GSFC legacy systems to simulate and
analyze trajectories computed in systems such as GTDS.
See the Propagator reference for more information.
Write Command
You can now export GMAT resources to files during the mission sequence
execution. This is a powerful feature that allows you to save configurations at
any point in a session for use by in later sessions or by other users.
See the Write Command reference for more information.
#Include Macro
You can now load GMAT resources and script snippets from external files during
the script initialization and mission execution. This is a powerful feature that
allows you to reuse configurations across multiple users and/or scripts. This
feature can also greatly simplify automation for operations and Monte-Carlo and
parametric scanning that have use cases with a lot of common data but some data
that changes from one execution to the next.
See the #Include reference for more information.
GetEphemStates Built-in Function
Using the built-in GetEphemStates function, you can now query SPICE, Code-
500 and STK .e ephemeris types and for a spacecraft’s initial epoch, initial state,
final epoch and final state in any GMAT supported epoch formats and coordinate
systems. This allows you to perform numerical propagation using states off of
ephemiris files for comparison and other analysis.
See the GetEphemStates referece for more information.
Improvements
You can now define the EOP file location in a script.
The system now supports finite burn parameters that report the thrust
component data for a finite burn. The parameters include total thrust from
all thrusters in the three coordinate directions, the total acceleration from all
thrusters in the three coordinate directions, and the total mass flow rate.
Furthermore, you can now also report individual thruster parameters such
as thrust magnitude, Isp and mass flow rate.
GMAT now contains built-in string manipulations functions sprintf, strcmp,
strcat, strfind, strrep.
Several new built in math functions are implemented including a built-in
cross product function. For manipulation of numeric data we've
implemented mod, ceil, floor, fix. For random number generation we've
implemented rand, randn, and SetSeed.
You can now model finite burns that employ multiple tanks. Previous
versions were limited to a single tank.
GMAT now supports generation of STK's “.e” ephemeris format in addition
those supported previously such as CCSDS-OEM, SPK and Code-500
formats.
We've written over 130 pages of new, high-quality user documentation!
The behavior of the GUI when using large fonts has been improved.
Compatibility Changes
You can now override the default NAIFId on a CelestialBody to allow
using body centers or barycenters as the reference for built-in celestial
bodies. Previously this field was read-only.
Development and Tools
Developer Tools and Dependencies
We updated the CMake-based build system that is used on all platforms. The
CMake configuration is maintained by the GMAT team and distributed with the
source code. Thanks to CMake, it is much easier to compile GMAT. See the wiki
documentation for details. Note that old build files are no longer supported and
are considered obsolete.
GMAT Stuff
Don't forget you can purchase clothing and other items with the GMAT logo via
©Land's End, Inc at the GSFC Store . Once, you've chosen an item, make sure to
select the GMAT logo!
Known & Fixed Issues
Over 100 bugs were closed in this release. See the "Critical Issues Fixed in
R2016a" report for a list of critical bugs and resolutions in R2016a. See the
"Minor Issues Fixed for R2016a" report for minor issues addressed in R2016a.
Known Issues
All known issues that affect this version of GMAT can be seen in the "Known
Issues in R2016a" report in JIRA.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-5269 Atmosphere model affects propagation at GEO.
GMT-2561 UTC Epoch Entry and Reporting During Leap Second is incorrect.
GMT-3043 Inconsistent validation when creating variables that shadow built-
in math functions
GMT-3289 First step algorithm fails for backwards propagation using SPK
propagator
GMT-3350 Single-quote requirements are not consistent across objects and
modes
GMT-3669 Planets not drawn during optimization in OrbitView
GMT-3738 Cannot set standalone FuelTank, Thruster fields in
CallMatlabFunction
GMT-4520 Unrelated script line in Optimize changes results (causes crash)
GMT-4520 Coordinate System Fixed attitudes are held constant in SPAD SRP
model during a propagation step
GMAT R2015a Release Notes
The General Mission Analysis Tool (GMAT) version R2015a was released Nov
2015. This is the first public release since July 2014, and is the 9th release for
the project.
Below is a summary of key changes in this release. Please see the full R2015a
Release Notes on JIRA for a complete list.
New Features
GMAT Functions
You can now write functions (sub-routines) in the GMAT script language. This
powerful feature greatly expands the practical capability of the system and
makes maintaining complex configurations simpler. This feature also enables
sharing GMAT script utilities among among projects. If you need a new math
computation, want to isolate a complex section of code, or re-use code, GMAT
functions are a great solution.
See the Using GMAT Functions tutorial for more information.
Eclipse Location
GMAT now supports eclipse location. Under the hood GMAT calls NAIF SPICE
routines. Thanks to the NAIF for making this great functionality available.
See the Eclipse Locator reference for more information.
Station Contact Location
GMAT now supports station contact location. Under the hood GMAT calls NAIF
SPICE routines. Thanks to the NAIF for making this great functionality
available.
See the Contact Locator reference for more information.
Python Interface
GMAT now supports an interface with Python. The power of the Python
ecosystem can now be used with GMAT.
See the Python reference for more information.
Electric Propulsion
GMAT now supports modelling of electric propulsion systems. Below is an
examle showing GMAT modelling a cube-sat with electric propulsion in a lunar
weak-stablity orbit. You can model electric tanks, thrusters, and power systems
(both Solar and nuclear).
See the Electric Propulsion tutorial for more information.
SNOPT Optimizer
GMAT now interfaces to Stanford Business Software, Inc. SNOPT Optimizer
See the SNOPT reference for more information.
Space Weather Modelling
You can now provide flux files for drag modelling including Schatten historical
files and Center for Space Standards and Innovation (CSSI) Space Weather
Files. This greatly improves long term orbital predictions and reconstructions in
the Earth's atmosphere.
See the Propagator reference for more information.
Celestial Body 3-D Graphics Models
You can now use a 3D model for celestial bodies in 3-D graphics.
See the Celestial Body reference for more information.
Solver Status Window
GMAT now displays a window showing solver variables and constraint values
during execution. This helps track the progress of targeters and optimizers and is
an important aid in troubleshooting convergence issues.
Improvements
Documentation
We've written over 70 pages of new, high-quality user documentation! We've
also written two conference papers documenting our verification and validation
process and results, and the flight qualification program and results for the
Advanced Composition Explorer (ACE). Conference papers are located in the
"docs" folder of the distribution.
Training Videos
We've posted training videos on YouTube . You can now take GMAT training
even if you are unable to attend the live classes!
Other Improvements
You can now optionally apply an ImpulsiveBurn in the backwards
direction which is convenient when targeting backwards in time.
GMAT is distributed with beta plugin Polyhedral gravity model.
The system now looks in the working directory for scripts run from the
command line
You can now reference supporting files relative to the script file location for
ease in sharing complex configurations.
You can now define an minimum elevation angle for a groundstation used
in event location and estimation.
The appearance of constellations in 3-D graphics has been improved.
The 3-D model scaling sensitivity in the GUI has been improved.
The behavior of the GUI when using large fonts has been improved.
Compatibility Changes
The ChemicalTank Resource has been renamed to ChemicalTank to
distinguish between chemical and electric systems.
The ChemicalThruster Resource has been renamed to ChemicalThruster
to distinguish between chemical and electric systems.
The sensitivity of Spacecraft Resource settings such as ModelOffsetX,
ModelRotationY, and ModelScale has changed in 3-D graphics.
When applying an ImpulsiveBurn during backwards targeting, GMAT now
attempts to compute maneuver values that are consistent with a forward
targeting approach. The maneuver values reference the pre-manevuer
velocity components instead of the post-maneuver components.
Development and Tools
Developer Documenation
We've added extensive documentation describing how to add new Resources and
Commands to GMAT. Resources and Commands are key to GMAT development
and application. This documentation is essential reading for making fundamental
extensions to GMAT. See the wiki documentation for details.
Developer Tools and Dependencies
We developed a new CMake-based build system that is used on all platforms.
The CMake configuration is maintained by the GMAT team and distributed with
the source code. Thanks to CMake, it is much easier to compile GMAT. See the
wiki documentation for details.
We updated SPICE to version N0065 and updated WxWidgets to version 3.0.2.
GMAT Stuff
You can now purchase clothing and other items with the GMAT logo via
©Land's End, Inc at the GSFC Store . Once, you've chosen an item, make sure to
select the GMAT logo!
Known & Fixed Issues
Over 215 bugs were closed in this release. See the "Critical Issues Fixed in
R2015a" report for a list of critical bugs and resolutions in R2015a. See the
"Minor Issues Fixed for R2015a" report for minor issues addressed in R2015a.
Known Issues
All known issues that affect this version of GMAT can be seen in the "Known
Issues in R2015a" report in JIRA.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-5253 GMAT stuck in script state after bad script load.
GMT-5269 Atmosphere model affects propagation at GEO.
GMT-2561 UTC Epoch Entry and Reporting During Leap Second is incorrect.
GMT-3043 Inconsistent validation when creating variables that shadow built-
in math functions
GMT-3289 First step algorithm fails for backwards propagation using SPK
propagator
GMT-3350 Single-quote requirements are not consistent across objects and
modes
GMT-3669 Planets not drawn during optimization in OrbitView
GMT-3738 Cannot set standalone FuelTank, Thruster fields in
CallMatlabFunction
GMT-4520 Unrelated script line in Optimize changes results (causes crash)
GMT-4408 Failed to load icon file and to open DE file
GMT-4520 Coordinate System Fixed attitudes are held constant in SPAD SRP
model during a propagation step
GMAT R2014a Release Notes
The General Mission Analysis Tool (GMAT) version R2014a was released May
2014. This is the first public release since April 2013, and is the 8th release for
the project.
Below is a summary of key changes in this release. Please see the full R2014a
Release Notes on JIRA for a complete list.
New Features
Trajectory Colors and Labels
In GMAT R2014a, you can now specify colors for each segment of your
trajectory independently, so you can clearly see where a segment begins and
ends. This can help define portions of a trajectory, such as before or after
maneuvers. All color handling has also been moved from the graphics resources
(OrbitView and GroundTrackPlot) to the resources and commands controlling
the trajectory (e.g. Spacecraft, Planet, Propagate).
On Spacecraft, the color specification has moved to the Visualization tab. See
the circled area in the screenshot below. Colors for celestial bodies (Planet,
Moon, Asteroid, etc.) are specified similarly.
The trajectory color associated with a particular trajectory segment can be
changed by changing the color for that particular Propagate command. It will
override the color for the Spacecraft being propagated for that segment only, and
it will return to the default color afterwards.
Additionally, colors can now be specified either by name ('Blue') or by RGB
value ([0 0 255]).
This release also adds participant labels in the graphics as well. As long as
OrbitView.ShowLabels is enabled, each celestial body or Spacecraft in the plot
will show its name next to it.
See the following example:
Create Spacecraft aSat
aSat.OrbitColor = 'Blue'
Create Propagator aProp
Create OrbitView aView
aView.Add = {aSat, Earth}
aView.XYPlane = off
aView.Axes = off
aView.EnableConstellations = off
aView.ShowLabels = on
BeginMissionSequence
% plots in blue
Propagate aProp(aSat) {aSat.ElapsedSecs = 900}
aSat.OrbitColor = 'Green'
% plots in green
Propagate aProp(aSat) {aSat.ElapsedSecs = 900}
% plots in red
Propagate aProp(aSat) {aSat.ElapsedSecs = 900, OrbitColor = Red}
This example results in the following image:
See the Color reference, as well as the individual Spacecraft, CelestialBody,
Propagate, and OrbitView references, for more information.
New Orbit State Representations
GMAT now supports six new common orbit state representations, developed
with support by the Korea Aerospace Research Institute (KARI). The new
representations are:
Long- and short-period Brouwer-Lyddane mean elements
(BrouwerMeanLong and BrouwerMeanShort)
Incoming and outgoing hyperbolic asymptote elements
(IncomingAsymptote and OutgoingAsymptote)
Modified equinoctial elements (ModifiedEquinoctial)
Alternate equinoctial elements (AlternateEquinoctial)
Delaunay elements (Delaunay)
Planetodetic elements, when using a body-fixed coordinate system
(Planetodetic)
The new representations are available as options in the Spacecraft "State Type"
list, and as options to the Spacecraft.DisplayStateType field.
See the Spacecraft Orbit State reference for more information.
New Attitude Models
GMAT now supports three new kinematic attitude models, developed with
support by the Korea Aerospace Research Institute (KARI). The new
representations are:
Precessing spinner
Nadir pointing
CCSDS Attitude Ephemeris Message (AEM)
The new representations are available as options in the Spacecraft "Attitude"
list, and as options to the Spacecraft.DisplayStateType field.
See the Spacecraft Attitude reference for more information.
Dynamics and Model Improvements
GMAT now supports several new dynamics models and a new numerical
integrator.
Prince Dormand 853 integrator. See the Propagator reference for more
information.
Mars-GRAM density model. See the Propagator reference for more
information.
High-fidelity, attitude dependent SRP dynamics model. See the Propagator
reference, and the Spacecraft Ballistic and Mass Properties reference for
more information.
Targeting and Optimization Improvements
There are new boundary value solver options on DifferentialCorrector
(Broyden, and ModifiedBroyden). Brodyen’s method and modified
Broyden's method usually take more iterations but fewer function
evaluations than NewtonRaphson and so are often faster. See the
Differential Corrector reference for more information.
There are new parameters that check for convergence of solvers. See the
Calculation Parameters reference for more information.
Below is a script example that illustrates the new algorithm and parameter
options.
Create Spacecraft aSat
Create Propagator aPropagator
Create ImpulsiveBurn aBurn
Create DifferentialCorrector aDC
% This algorithm is often faster, as is ModifiedBroyden
aDC.Algorithm = Broyden
Create OrbitView EarthView
EarthView.Add = {Earth,aSat}
EarthView.ViewScaleFactor = 5
Create ReportFile aReport
BeginMissionSequence
% Report targeter status here
Report aReport aDC.SolverStatus aDC.SolverState
Target aDC
Vary aDC(aBurn.Element1 = 1.0, {Upper = 3, MaxStep = 0.4})
Maneuver aBurn(aSat)
Propagate aPropagator(aSat,{aSat.Apoapsis})
Achieve aDC(aSat.RMAG = 42164)
EndTarget
% Report targeter status here
Report aReport aDC.SolverStatus aDC.SolverState
Improvements
Dependencies in Assignment Command
You can now define settable parameters by using a dependency on the LHS of an
assignment command:
Create Spacecraft aSat
BeginMissionSequence
aSat.EarthFixed.X = 7000
aSat.EarthMJ2000Eq.VZ = 1
Other Improvements
You can now set true retrograde orbits when using the Keplerian
representation.
You can now use the quaternion Rvector parameter on the right hand side of
an assignment command.
You can now use a Spacecraft body fixed coordinate system as the
coordinate system for an OrbitView.
The number of Spacecraft that that can be displayed in OrbitView is no
longer limited to 30.
The documentation for OrbitView has been significantly expanded. See the
Orbit View reference for details.
You can now save an XY plot graphics window to an image file.
The supported set of keyboard shortcuts has been greatly expanded. See the
Keyboard Shortcuts reference for more information.
You can now use many more common ASCII characters in GMAT strings.
You can now generate orbit state command summary reports using
coordinate systems that have any point type as the origin of the selected
coordinate system. Previously the origin had to be a Celestial Body.
Compatibility Changes
Color settings for Resources displayed in graphics are now configured on
the Resource and via the Propagate command. OrbitColor and
TargetColor fields on graphics resources are no longer used.. See the
Spacecraft Visualization reference, and Propagate command reference for
details.
AtmosDensity is now reported in units of kg/km^3. See the Calculation
Parameter reference for details.
Known & Fixed Issues
Over 123 bugs were closed in this release. See the "Critical Issues Fixed in
R2014a" report for a list of critical bugs and resolutions in R2014a. See the
"Minor Issues Fixed for R2014a" report for minor issues addressed in R2014a.
Known Issues
All known issues that affect this version of GMAT can be seen in the "Known
Issues in R2014a" report in JIRA.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-2561 UTC Epoch Entry and Reporting During Leap Second is incorrect.
GMT-3043 Inconsistent validation when creating variables that shadow built-
in math functions
GMT-3108 OrbitView with STM and Propagate Synchronized does not show
spacecraft in correct locations
GMT-3289 First step algorithm fails for backwards propagation using SPK
propagator
GMT-3350 Single-quote requirements are not consistent across objects and
modes
GMT-3556 Unable to associate tank with thruster in command mode
GMT-3629 GUI starts in bad state when started with --minimize
GMT-3669 Planets not drawn during optimization in OrbitView
GMT-3738 Cannot set standalone FuelTank, Thruster fields in
CallMatlabFunction
GMT-4520 Unrelated script line in Optimize changes results (causes crash)
GMT-4408 Failed to load icon file and to open DE file
GMT-4520 Coordinate System Fixed attitudes are held constant in SPAD SRP
model during a propagation step
GMAT R2013b Release Notes
The General Mission Analysis Tool (GMAT) version R2013b was released in
August 2013. This is the first public release since April, and is the 7th release for
the project. This is an internal-only release, intended to support the ACE
mission.
Below is a summary of key changes in this release. Please see the full R2013b
Release Notes on JIRA for a complete list.
New Features
Data File Interface
GMAT now can load Spacecraft state and physical properties data directly from
a data file. A new resource, FileInterface, controls the interface to the data file,
and the new Set command lets you apply the data as a part of the Mission
Sequence.
See the following example:
Create Spacecraft aSat
Create FileInterface tvhf
tvhf.Filename = 'statevec.txt'
tvhf.Format = 'TVHF_ASCII'
BeginMissionSequence
Set aSat tvhf
See the FileInterface and Set references for more information.
Code-500 Ephemeris Format
GMAT's EphemerisFile resource can now write a Code-500 format ephemeris
file. The Code-500 format is a binary ephemeris format defined by the NASA
Goddard Space Flight Center Flight Dynamics Facility.
Create Spacecraft sc
Create Propagator prop
Create EphemerisFile ephem
ephem.Spacecraft = sc
ephem.Filename = 'ephem.eph'
ephem.FileFormat = 'Code-500'
ephem.StepSize = 60
ephem.OutputFormat = 'PC'
BeginMissionSequence
Propagate prop(sc) {sc.ElapsedDays = 1}
See the EphemerisFile reference for more information on this format.
New Local Aligned-Constrained Coordinate System
A local aligned-constrainted coordinate system is one defined by an alignment
vector (defined based on the position of a reference object with respect to the
origin) and two constraint vectors. This is a highly flexible coordinate system
that can be defined in many ways, depending on mission needs. To use it, select
the LocalAlignedConstraned axes type when creating a new
CoordinateSystem.
Create CoordinateSystem ACECoordSys
ACECoordSys.Origin = Earth
ACECoordSys.Axes = LocalAlignedConstrained
ACECoordSys.ReferenceObject = ACE
ACECoordSys.AlignmentVectorX = 0
ACECoordSys.AlignmentVectorY = 0
ACECoordSys.AlignmentVectorZ = 1
ACECoordSys.ConstraintVectorX = 1
ACECoordSys.ConstraintVectorY = 0
ACECoordSys.ConstraintVectorY = 0
ACECoordSys.ConstraintCoordinateSystem = EarthMJ2000Ec
ACECoordSys.ConstraintReferenceVectorX = 0
ACECoordSys.ConstraintReferenceVectorY = 0
ACECoordSys.ConstraintReferenceVectorZ = 1
See the CoordinateSystem reference for more information.
Improvements
Force Model Parameters
You can now access ForceModel-dependent parameters, such as Spacecraft
acceleration and atmospheric density. The new parameters are:
Spacecraft.ForceModel.Acceleration
Spacecraft.ForceModel.AccelerationX
Spacecraft.ForceModel.AccelerationY
Spacecraft.ForceModel.AccelerationZ
Spacecraft.ForceModel.AtmosDensity
Space Point Parameters
All Resources that have coordinates in space now have Cartesian position and
velocity parameters, so you can access ephemeris information. This includes all
built-in solar system bodies and other Resources such as CelestialBody,Planet,
Moon, Asteroid, Comet, Barycenter, LibrationPoint, and GroundStation :
CelestialBody.CoordinateSystem.X
CelestialBody.CoordinateSystem.Y
CelestialBody.CoordinateSystem.Z
CelestialBody.CoordinateSystem.VX
CelestialBody.CoordinateSystem.VY
CelestialBody.CoordinateSystem.VZ
Note that to use these parameters, you must first set the epoch of the Resource to
the desired epoch at which you want the data. See the following example:
Create ReportFile rf
BeginMissionSequence
Luna.Epoch.A1ModJulian = 21545
Report rf Luna.EarthMJ2000Eq.X Luna.EarthMJ2000Eq.Y Luna.EarthMJ2000Eq.Z ...
Luna.EarthMJ2000Eq.VX Luna.EarthMJ2000Eq.VY Luna.EarthMJ2000Eq.VZ
Compatibility Changes
EphemerisFile.InitialEpoch now cannot be later than
EphemerisFile.FinalEpoch. See the EphemerisFile reference for details.
When EphemerisFile.FileFormat is set to 'SPK',
EphemerisFile.CoordinateSystem must have MJ2000Eq as the axis system.
Other axis systems are no longer allowed with this ephemeris format. See
the EphemerisFile reference for details.
The deprecated fields Thruster.Element{1–3} have been removed. Use
Thruster.ThrustDirection{1–3} instead. See the Thruster reference for
details.
Tab characters in strings are now treated literally, instead of being changed
to spaces. See GMT-3336 for details.
Known & Fixed Issues
Over 50 bugs were closed in this release. See the "Critical Issues Fixed in
R2013b" report for a list of critical bugs and resolutions in R2013b. See the
"Minor Issues Fixed for R2013b" report for minor issues addressed in R2013b.
Known Issues
All known issues that affect this version of GMAT can be seen in the "Known
Issues in R2013b" report in JIRA.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-2561 UTC Epoch Entry and Reporting During Leap Second is incorrect.
GMT-3043 Inconsistent validation when creating variables that shadow built-
in math functions
GMT-3108 OrbitView with STM and Propagate Synchronized does not show
spacecraft in correct locations
GMT-3289 First step algorithm fails for backwards propagation using SPK
propagator
GMT-4097 Ephemeris File is Not Chunking File At Some Discontinuty Types
GMT-3350 Single-quote requirements are not consistent across objects and
modes
GMT-3556 Unable to associate tank with thruster in command mode
GMT-3629 GUI starts in bad state when started with --minimize
GMT-3669 Planets not drawn during optimization in OrbitView
GMT-3738 Cannot set standalone FuelTank, Thruster fields in
CallMatlabFunction
GMT-3745 SPICE ephemeris stress tests are not writing out ephemeris for the
entire mission sequence
GMAT R2013a Release Notes
The General Mission Analysis Tool (GMAT) version R2013a was released in
April, 2013. This is the first public release since May 23, 2012, and is the 6th
public release for the project. R2013a is a major release transitioning GMAT
from beta to production status. In this release:
End-user documentation was rewritten and greatly expanded.
11,000 script-based regression tests run nightly.
5,000 GUI-based regression tests run weekly.
Code and documentation was contributed by 11 developers from 3
organizations.
Licensing
GMAT is now licensed under Apache License, Version 2.0. According to the
Open Source Proliferation Report, the Apache License 2.0 is one of the most
widely-used open source licenses, thereby making GMAT compatible with more
existing software and projects.
Major Improvements
Production Status
Release R2013a is a major release of GMAT that transitions from beta to
production status. Most of our efforts have been devoted to improving the
quality of the software and its documentation. This year we made a complete
sweep through the system, starting by updating engineering specifications for all
features, identifying test gaps, writing new tests, addressing known and newly
found bugs, and completing user documentation.
Tutorials
The GMAT User Guide now contains 5 in-depth tutorials that show how to use
GMAT for end-to-end analysis. The tutorials are designed to teach you how to
use GMAT in the context of performing real-world analysis and are intended to
take between 30 minutes and several hours to complete. Each tutorial has a
difficulty level and an approximate duration listed with any prerequisites in its
introduction, and is arranged in a general order of difficulty. The simplest
tutorial shows you how to enter orbital initial conditions and propagate to orbit
perigee, while more advanced tutorials show how to perform finite-maneuver
targeting, Mars B-plane targeting, and lunar flyby optimization.
Reference Guide
We have written a complete reference manual for GMAT for R2013a. The
reference manual contains detailed information on all GMAT components.
Whether you need detailed information on syntax or application-specific
examples, go here. For each GMAT resource (e.g. Spacecraft,
ChemicalThruster, XYPlot) and command (e.g. Optimize, Propagate), the
following information is documented:
Brief description of the feature
List of related or coupled features
Complete syntactical specification of the interface
Tables with detailed options, variable ranges and data types, defaults, and
expected behavior
Copy-and-paste-ready examples
The guide also contains general reference material about the system, such as:
Script language syntax
External interfaces
Parameter listings
Configuration files
Command line interface
Testing
We have spent much of our time preparing for R2013a on testing. Our script and
GUI-based regression test systems doubled in size in the last year. They now
contain:
Over 6,000 new system, validation, and end-to-end script-based tests
30 new end-to-end GUI tests
3,000 new GUI system tests
GUI test are performed using SmartBear’s TestComplete software. Script tests
are performed using a custom MATLAB-based automated test system. A
complete execution of the regression test system now takes almost four days of
computer time.
Minor Enhancements
While most of our effort has been focused on quality for this release, we have
included some new features.
ICRF is now supported for input and output of orbit state data:
The Earth texture map is improved:
CCSDS ephemeris files are now accessible in the output tab:
Improved mouse controls for interactive 3-D graphics. See the OrbitView
reference for details.
Improved 3ds model support
Improved error messages system-wide
New BodySpinSun axis system for asteroid survey missions
Improved system modularization by moving more features to plugins
Compatibility Changes
Our last release, R2012a, was beta software. R2013a is mature, production
software. We made some changes that may cause backwards compatibility issues
with scripts written in previous beta versions. Examples of changes in R2013a
that affect backwards compatibility with previous beta versions include:
Fixed many poorly-named fields and/or parameters (i.e.
OrbitView.CelestialPlane → OrbitView.EclipticPlane)
Corrected missed or invalid data validation checking
Removed partially-implemented functionality from previous releases
Removed improperly-exposed internal fields and functions
Disabled configuration of some resources in the mission sequence
In all cases, we modified GMAT to work correctly as specified in the
documentation, but did not always maintain backwards compatibility with
previous versions. This was a one-time, “pull-of-the-Band-Aid” approach, and
future releases will maintain backwards compatibility with R2013a or provide
deprecation notifications of features that are no longer supported.
In addition, there were some features that did not meet quality expectations for
this release and have been turned off in the release package. Most of these
features can be turned on for analysis purposes, but they are not fully tested and
should be used with caution.
Orbit Designer (disabled)
GMAT functions (libGmatFunctions)
Save command (libSaveCommand)
Bulirsh-Stoer integrator (libExtraPropagators)
To turn on these features, see the Startup File reference.
Known & Fixed Issues
Over 720 bugs and issues were closed in this release. See the "Critical Issues
Fixed for R2013a" report for a list of critical bugs and resolutions for R2013a.
See the "Minor Issues Fixed for R2013a" report" for minor issues addressed in
R2013a.
Known Issues
All known issues that affect this version of GMAT can be seen in the "Known
issues in R2013a" report in JIRA.
There are several known issues in this release that we consider to be significant:
ID Description
GMT-2561 UTC Epoch Entry and Reporting During Leap Second is incorrect.
GMT-3043 Inconsistent validation when creating variables that shadow built-
in math functions
GMT-3108 OrbitView with STM and Propagate Synchronized does not show
spacecraft in correct locations
GMT-3289 First step algorithm fails for backwards propagation using SPK
propagator
GMT-3321 MATLAB uses stale version of function if command window isn't
restarted between runs
GMT-3350 Single-quote requirements are not consistent across objects and
modes
GMT-3556 Unable to associate tank with thruster in command mode
GMT-3629 GUI starts in bad state when started with --minimize
GMT-3669 Planets not drawn during optimization in OrbitView
GMT-3738 Cannot set standalone FuelTank, Thruster fields in
CallMatlabFunction
GMT-3745 SPICE ephemeris stress tests are not writing out ephemeris for the
entire mission sequence
GMAT R2012a Release Notes
The General Mission Analysis Tool (GMAT) version R2012a was released May
23, 2012. This is the first public release in over a year, and is the 5th public
release for the project. In this release:
52,000 lines of code were added
Code and documentation was contributed by 9 developers from 2
organizations
6847 system tests were run every weeknight
This is a beta release. It has undergone extensive testing in many areas, but is not
considered ready for production use.
New Features
Ground Track Plot
GMAT can now show the ground track of a spacecraft using the new
GroundTrackPlot resource. This view shows the orbital path of one or more
spacecraft projected onto a two-dimensional map of a celestial body, and can use
any celestial body that you have configured. Here's an example of the plot
created as part of the default mission:
Orbit Designer
Sometimes you need to create a spacecraft in a particular orbit but don't exactly
know the proper orbital element values. Before, you had to make a rough
estimate, or go back to the math to figure it out. Now, GMAT R2012a comes
with a new Orbit Designer that does this math for you.
The Orbit Designer helps you create one of six different Earth-centered orbit
types, each with a flexible array of input options:
sun-synchronous
repeat sun-synchronous
repeat ground track
geostationary
molniya
frozen
Once you've created your desired orbit, it is automatically imported into the
Spacecraft resource for later use. Here's an example of a sun-synchronous orbit
using the Designer. To open the Orbit Designer, click the button on the
Spacecraft properties window.
Eclipse Locator [alpha]
We've done significant work toward having a robust eclipse location tool in
GMAT, but this work is not complete. This release comes with an alpha-stage
plugin (disabled by default) called libEventLocator. When enabled, this plugin
adds a new EclipseLocator resource that can be configured to calculate eclipse
entry and exit times and durations with respect to any configured Spacecraft and
celestial bodies. The eclipse data can be reported to a text file or plotted
graphically. Some known limitations include an assumption of spherical celestial
bodies and a lack of light-time correction. This feature has not been rigorously
tested, and may be brittle. We've included it here as a preview of what's coming
in future releases.
C Interface [alpha]
Likewise, we've included an experimental library and plugin that exposes a
plain-C interface to GMAT's internal dynamics model functionality. This
interface is intended to fill a very specific need: to expose force model derivates
from GMAT to external software, especially MATLAB, for use with an external
integrator (though GMAT can do the propagation also, if desired). The interface
is documented by an API reference for now.
Improvements
Dynamics Models
We've made lots of improvements to GMAT's already capable force model suite.
Here's some highlights:
GMAT now models Earth ocean and pole tides. This is a script-only option
that can be turned on alongside an Earth harmonic gravity model; turn it on
with a line like this:
ForceModel.GravityField.Earth.EarthTideModel = 'SolidAndPole'
You can now apply relativistic corrections using the checkbox on the
properties for Propagator.
Solar System
GMAT can now use the DE421 and DE424 ephemerides for the solar system.
These files are included in the installer, but are not activated by default. To use
either of these ephemerides, double-click the SolarSystem folder and select it
from the Ephemeris Source list. Or include the following script line:
SolarSystem.EphemerisSource = 'DE421'
There's also a new SolarSystem resource called SolarSystemBarycenter that
represents the barycenter as given by the chosen ephemeris source (DE405,
DE421, SPICE, etc.). This resource can be used directly in reports or as the
origin of a user-defined coordinate system.
TDB Input
You can now input the epoch of a Spacecraft orbit in the TDB time system (in
both Modified Julian and Gregorian formats).
Mission Tree
We've made significant improvements to the mission tree to make it more user-
friendly to heavy users. The biggest improvement is that you can now filter the
mission sequence in different ways to make complex missions easier to
understand, for example by hiding non-physical events or collapsing the tree to
only its top-level elements.
GMAT also now lets you name your mission sequence commands. Thus, instead
of a sequence made up of commands like "Optimize1" and "Propagate3", you
can label them "Optimize LOI" and "Prop to Periapsis". This example shows the
Ex_HohmannTransfer.script sample with labeled commands.
Finally, we added the ability to undock the mission tree so you can place it and
the resources tree side by side and see both at the same time. To undock the tree,
right-click the Mission tab and drag it from its docked position. To dock it again,
just close the new Mission window.
Mission Summary
You can now change the coordinate system shown in the Mission Summary on
the fly: just change the Coordinate System list at the top of the window and the
numbers will update. This feature can use any coordinate system currently
defined in GMAT, including user-defined ones.
There's also a new Mission Summary - Physics-Based Commands that shows
only physical events (Propagate commands, burns, etc.), and further data was
added to both Mission Summary types.
Window Persistency
The locations of output windows are now saved with the mission in the script
file. This means that when running a mission, all the output windows that were
open when the mission was last saved will reappear in their old positions.
In addition, the locations of certain GMAT windows, like the mission tree, the
script editor, and the application window itself are saved to the user preferences
file (MyGMAT.ini).
Switch to Visual Studio on Windows
With this release, the official GMAT binaries for Windows are now compiled
with Microsoft Visual Studio 2010 instead of GCC. The biggest benefit of this is
in performance; we've seen up to a 50% performance improvement in certain
cases in unofficial testing. It also leads to more a industry-standard development
process on Windows, as the MinGW suite is no longer needed.
New Icons
The last release saw a major overhaul of GMAT's GUI icons. This time we've
revised some and added more, especially in the mission tree.
Training Manual
The non-reference material in the GMAT User Guide has been overhauled,
partially rewritten, and reformatted to form a new GMAT Training Manual. This
includes the "Getting Started" material, some short how-to articles, and some
longer tutorials. All of this information is included in the GMAT User Guide as
well, in addition to reference material that is undergoing a similar rewrite later
this year.
Infrastructure
The GMAT project has implemented several infrastructure improvements in the
last year. The biggest of these was switching from our old Bugzilla system to
JIRA for issue tracking.
This year also saw the creation of the GMAT Blog and the GMAT Plugins and
Extensions Blog with a fair number of posts each, plus reorganizations for the
wiki and the forums. We reactivated our two mailing lists, gmat-developers and
gmat-users, but haven't seen much usage of each yet. And finally, we created a
new mailing list, gmat-buildtest, for automated daily build and test updates.
Compatibility Changes
Application Control Changes
The command-line arguments for the GMAT executable have changed. See the
following table for replacements.
Old New Description
-help --help, -h Shows available options
-date --version, -v Shows GMAT build date
-ms --start-server Starts GMAT server on
startup
-br filename --run, -r scriptname Builds and runs the script
-minimize --minimize, -m Minimizes GMAT
window
-exit --exit, -x Exits GMAT after a script
is run
Script Syntax Changes
Resource Field Replacement
ForceModel Drag Drag.AtmosphereModel
Propagator MinimumTolerance (none)
(BulirschStoer)
Known & Fixed Issues
Many bugs were closed in this release, but a comprehensive list is difficult to
create because of the move from Bugzilla to JIRA. See the "Bugs closed in
R2012a" report in for a partial list.
All known issues that affect this version of GMAT can be seen in the "Known
issues in R2012a" report in JIRA.
GMAT R2011a Release Notes
The General Mission Analysis Tool (GMAT) version R2011a was released April
29, 2011 on the following platforms:
Windows (XP, Vista, 7) Beta
Mac OS X (10.6) Alpha
Linux Alpha
This is the first release since September 2008, and is the 4th public release for
the project. In this release:
100,000 lines of code were added
798 bugs were opened and 733 were closed
Code was contributed by 9 developers from 4 organizations
6216 system tests were written and run nightly
New Features
OrbitView
GMAT's old OpenGLPlot 3D graphics view was completely revamped and
renamed OrbitView. The new OrbitView plot supports all of the features of
OpenGLPlot, but adds several new ones:
Perspective view instead of orthogonal
Stars and constellations (with names)
A new default Earth texture
Accurate lighting
Support for user-supplied spacecraft models in 3ds and POV formats.
All existing scripts will use the new OrbitView object automatically, with no
script changes needed. Here's a sample of what can be done with the new
graphics:
User-Defined Celestial Bodies
Users can now define their own celestial bodies (Planets, Moons, Asteroids, and
Comets) through the GMAT interface, by right-clicking on the Sun resource (for
Planets, Asteroids, and Comets) or any other Solar System resource (for Moons).
User-defined celestial bodies can be customized in many ways:
Mu (for propagation), radius and flattening (for calculating altitude)
User-supplied texture file, for use with OrbitView
Ephemeris from two-body propagation of an initial Keplerian state or from
a SPICE kernel
Orientation and spin state
Ephemeris Output
GMAT can now output spacecraft ephemeris files in CCSDS-OEM and SPK
formats by using the EphemerisFile resource. For each ephemeris, you can
customize:
Coordinate system
Interpolation order
Step size
Epoch range
SPICE Integration for Spacecraft
Spacecraft in GMAT can now be propagated using data from a SPICE kernel
rather than by numerical integration. This can be activated on the SPICE tab of
the Spacecraft resource, or through the script. The following SPICE kernels are
supported:
SPK/BSP (orbit)
CK (attitude)
FK (frame)
SCLK (spacecraft clock)
Plugins
New features can now be added to GMAT through plugins, rather than being
compiled into the GMAT executable itself. The following plugins are included in
this release, with their release status indicated:
libMatlabPlugin Beta
libFminconOptimizer (Windows only) Beta
libGmatEstimation Alpha (preview)
Plugins can be enabled or disabled through the startup file
(gmat_startup_file.txt), located in the GMAT bin directory. All plugins are
disabled by default.
GUI/Script Synchronization
For those that work with both the script and the graphical interface, GMAT now
makes it explicitly clear if the two are synchronized, and which script is active
(if you have several loaded). The possible states are:
Synchronized (the interface and the script have the same data)
GUI or Script Modified (one of them has been modified with respect to the
other)
Unsynchronized (different changes exist in each place)
The only state in which manual intervention is necessary is Unsynchronized,
which must be merged manually (or one set of changes must be discarded). The
following status indicators are available on Windows and Linux (on Mac, they
appear as single characters on the GMAT toolbar).
Estimation [Alpha]
GMAT R2011a includes significant new state estimation capabilities in the
libGmatEstimation plugin. The included features are:
Measurement models
Geometric
TDRSS range
USN two-way range
Estimators
Batch
Extended Kalman
Resources
GroundStation
Antenna
Transmitter
Receiver
Transponder
Note
This functionality is alpha status, and is included with this
release as a preview only. It has not been rigorously tested.
User Documentation
GMAT’s user documentation has been completely revamped. In place of the old
wiki, our formal documentation is now implemented in DocBook, with HTML,
PDF, and Windows Help formats shipped with GMAT. Our documentation
resources for this release are:
Help (shipped with GMAT, accessed through the Help > Contents menu
item)
Online Help (updated frequently, http://gmat.sourceforge.net/docs/)
Video Tutorials (http://gmat.sourceforge.net/docs/videos.html)
Help Forum (http://gmat.ed-pages.com/forum/)
Wiki (for informal and user-contributed documentation, samples, and tips:
http://gmat.ed-pages.com/wiki/tiki-index.php)
Screenshot ( )
GMAT can now export a screenshot of the OrbitView panel to the output folder
in PNG format.
Improvements
Automatic MATLAB Detection
MATLAB connectivity is now automatically established through the
libMatlabInterface plugin, if enabled in your gmat_startup_file.txt. We are no
longer shipping separate executables with and without MATLAB integration.
Most recent MATLAB versions are supported, though configuration is
necessary.
Dynamics Model Numerics
All included dynamics models have been thoroughly tested against truth
software (AGI STK, and A.I. Solutions FreeFlyer, primarily), and all known
numeric issues have been corrected.
Script Editor [Windows]
GMAT’s integrated script editor on Windows is much improved in this release,
and now features:
Syntax highlighting for GMAT keywords
Line numbering
Find & Replace
Active script indicator and GUI synchronization buttons
Regression Testing
The GMAT project developed a completely new testing system that allows us to
do nightly, automated tests across the entire system, and on multiple platforms.
The new system has the following features:
Focused on GMAT script testing
Written in MATLAB language
Includes 6216 tests with coverage of most of GMAT’s functional
requirements
Allows automatic regression testing on nightly builds
Compatible with all supported platforms
The project is also regularly testing the GMAT graphical interface on Windows
using the SmartBear TestComplete tool. This testing occurs approximately twice
a week, and is focused on entering and running complete missions through the
interface and checking that the results match those generated in script mode.
Visual Improvements
This release features numerous visual improvements, including:
A new application icon and splash screen (shown below)
Many new, professionally-created icons
A welcome page for new users
Compatibility Changes
Platform Support
GMAT supports the following platforms:
Windows XP
Windows Vista
Windows 7
Mac OS X Snow Leopard (10.6)
Linux (Intel 64-bit)
With the exception of the Linux version, GMAT is a 32-bit application, but will
run on 64-bit platforms in 32-bit mode. The MATLAB interface was tested with
32-bit MATLAB 2010b on Windows, and is expected to support 32-bit
MATLAB versions from R2006b through R2011a.
Mac: MATLAB 2010a was tested, but version coverage is expected to be
identical to Windows.
Linux: MATLAB 2009b 64-bit was tested, and 64-bit MATLAB is required.
Otherwise, version coverage is expected to be identical to Windows.
Script Syntax Changes
The BeginMissionSequence command will soon be required for all scripts. In
this release a warning is generated if this statement is missing.
The following syntax elements are deprecated, and will be removed in a future
release:
Resource Field Replacement
DifferentialCorrector TargeterTextFile ReportFile
DifferentialCorrector UseCentralDifferences DerivativeMethod =
"CentralDifference"
EphemerisFile FileName Filename
FiniteBurn Axes
FiniteBurn BurnScaleFactor
FiniteBurn CoordinateSystem
FiniteBurn Origin
FiniteBurn Tanks
CoordinateSystem = CoordinateSystem =
FiniteBurn "Inertial" "MJ2000Eq"
ImpulsiveBurn
VectorFormat
FiniteBurn
ImpulsiveBurn
FiniteBurn V Element1
ImpulsiveBurn N Element2
B Element3
FuelTank PressureRegulated PressureModel =
PressureRegulated
OpenGLPlot OrbitView
OrbitView EarthSunLines SunLine
OrbitView ViewDirection = [0 0
ViewDirection = 1]
Vector
ViewDirection = [0 0
1]
OrbitView ViewPointRef ViewPointReference
OrbitView ViewPointReference =
ViewPointRef = Vector [0 0 1]
ViewPointRefVector =
[0 0 1]
OrbitView ViewPointVector = [0
ViewPointVector = 0 1]
Vector
ViewPointVectorVector
= [0 0 1]
SolarSystem Ephemeris EphemerisSource
Spacecraft StateType DisplayStateType
Thruster
X_Direction ThrustDirection1
Y_Direction ThrustDirection2
Z_Direction ThrustDirection3
Element1
Element2
Element3
XYPlot Add YVariable
XYPlot Grid ShowGrid
XYPlot IndVar XVariable
Command Old Syntax New Syntax
Propagate Propagate - Propagate BackProp
DefaultProp(sc) DefaultProp(sc)
Fixed Issues
733 bugs were closed in this release, including 368 marked “major” or “critical”.
See the full report for details.
Known Issues
There remain 268 open bugs in the project’s Bugzilla database, 42 of which are
marked “major” or “critical”. These are tabulated below.
Table 24. Multiple platforms
407 Multi-Matlab run bug
636 MATLAB Callbacks on Linux and
Mac
648 DOCUMENT BEHAVIOR - Final
orbital state does not match for the two
report methods
776 Batch vs Individual Runs different
1604 Keplerian Conversion Errors for
Hyperbolic Orbits
1668 Decimal marker not flexible enough
for international builds
1684 MMS script in GMAT takes 300 times
longer than similar run in FreeFlyer
1731 Major Performance issue in GMAT
Functions
1734 Spacecraft allows conversion for
singular conic section.
1992 Determinant of "large" disallowed due
to poor algorithm performance
2058 Can't set SRP Flux and Nominal Sun
via GUI
2088 EOP file reader uses Julian Day
2147 Empty parentheses "( )" are not caught
in math validation
2313 Finite Burn/Thruster Tests Have errors
> 1000 km but may be due to script
differences
2322 DOCUMENT: MATLAB interface
requires manual configuration by user
2344 when a propagator object is deleted, its
associated force model is not deleted
2349 Performance Issue in Force Modelling
2410 Ephemeris propagator has large
numeric error
2416 STM Parameters are wrong when
using Coordinate System other than
EarthMJ2000Eq
Table 25. Windows
970 Matlab connection issue
1012 Quirky Numerical Issues 2 in Batch
mode
1128 GMAT incompatible with MATLAB
R14 and earlier
1417 Some lines prefixed by "function" are
ingored
1436 Potential performance issue using
many propagate commands
1528 GMAT Function scripts unusable
depending on file
ownership/permissions
1580 Spacecraft Attitude Coordinate System
Conversion not implemented
1592 Atmosphere Model Setup File Features
Not Implemented
2056 Reproducibility of script run not
guaranteed
2065 Difficult to read low number in
Spacecraft Attitude GUI
2066 SC Attitude GUI won't accept
0.0:90.0:0.0 as a 3-2-1 Euler Angle
input
2067 Apply Button Sometimes Not
Functional in SC Attitude GUI
2374 Crash when GMAT tries to write to a
folder without write permissions
2381 TestComplete does not match user
inputs to DefaultSC
2382 Point Mass Issue when using Script vs.
User Input
Table 26. Mac OS X
1216 MATLAB->GMAT not working
2081 Texture Maps not showing on Mac for
OrbitView
2092 GMAT crashes when MATLAB engine
does not open
2291 LSK file text ctrl remains visible when
source set to DE405 or 2Body
2311 Resource Tree - text messed up for
objects in folders
2383 Crash running RoutineTests with plots
ON
Table 27. Linux
1851 On Linux, STC Editor crashes GMAT
on Close
1877 On Linux, Ctrl-C crashes GMAT if no
MDIChildren are open
