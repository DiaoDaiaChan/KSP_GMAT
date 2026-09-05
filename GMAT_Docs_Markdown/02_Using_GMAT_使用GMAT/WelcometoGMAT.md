# Welcome to GMAT

> 来源：第 6-17 页

Chapter 1. Welcome to GMAT
The General Mission Analysis Tool (GMAT) is the world’s only enterprise,
multi-mission, open source software system for space mission design,
optimization, and navigation. The system supports missions in flight regimes
ranging from low Earth orbit to lunar, libration point, and deep space missions.
GMAT is developed by a team of NASA, private industry, public, and private
contributors and is used for real-world mission support, engineering studies, as a
tool for education, and public engagement. See the R2018a Release Notes for a
complete list of changes in R2018a.
Milestones and Accomplishments
We're excited that GMAT has recently seen signficant adoption for operational
misssion support..
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
Features Overview
GMAT is a feature rich system containing high fidelity space system models,
optimization and targeting, built in scripting and programming infrastructure,
and customizable plots, reports and data products, to enable flexible analysis and
solutions for custom and unique applications. GMAT can be driven from a fully
featured, interactive GUI or from a custom script language. Here are some of
GMAT’s key features broken down by feature group.
Dynamics and Environment Modelling
High fidelity dynamics models including harmonic gravity, drag, tides, and
relativistic corrections
High fidelity spacecraft modeling
Formations and constellations
Impulsive and finite maneuver modeling and optimization
Propulsion system modeling including chemical and electric system
Solar System modeling including high fidelity ephemerides, custom
celestial bodies, libration points, and barycenters
Rich set of coordinate systems including J2000, ICRF, fixed, rotating,
topocentric, and many others
Propagation using CCSDS, SPICE, STK, and Code 500 ephemeris files
Propagators that naturally synchronize epochs of multiple vehicles and
avoid fixed step integration and interpolation
Plotting, Reporting and Product Generation
Interactive 3-D graphics
Customizable data plots and reports
Post computation animation
CCSDS, SPK, and Code-500 ephemeris generation
Eclipse and station contact location
Optimization and Targeting
Boundary value targeters
Nonlinear, constrained optimization
Custom, scriptable cost functions
Custom, scriptable nonlinear equality and inequality constraint functions
Custom targeter controls and constraints
Programming Infrastructure
User defined variables, arrays, and strings
User defined equations using MATLAB syntax. (i.e. overloaded array
operation)
Control flow such as If, For, and While loops for custom applications
Matlab interface
Python interface
User-defined functions (sub-routines)
Built in parameters and calculations in multiple coordinate systems
Orbit Determination Infrastructure
Batch estimator
Extensive statistical results reporting
DSN data types
GN data types
Measurement data editing
Media corrections
Error modeling
Interfaces
Fully featured, interactive GUI that makes simple analysis quick and easy
Custom scripting language that makes complex, custom analysis possible
Matlab interface for custom external simulations and calculations
Python interface for custom external simulations and calculations
File interface for the TCOPS Vector Hold File format, for loading of initial
spacecraft data
Command line interface for batch analysis
Heritage
GMAT has enabled and enhanced missions in nearly every NASA flight regime
including enabling new mission types, extending the life of existing missions,
and enabling new science observations. GMAT has supported 8 NASA missions
and 10+ NASA proposal efforts. The system has experienced broad application
and adoption around the world. To date, GMAT has been used by over 30
organizations, with 15 universities and 12 commercial firms publishing results in
the open literature.
Licensing
GMAT is licensed under the Apache License 2.0.
Platform Support
GMAT has been rigorously tested on the Windows 7 platform and we perform
nightly regression tests running almost 14,000 test cases for the system core and
over 4000 test cases for the GUI interface. The system core has been rigorously
tested on Windows 10, but the GUI has only undergone preliminary testing on
that platform. Note that R2018a is the last version that will be tested on
Windows 10. The Mac and Linux console versions are rigorously tested, but the
GUI is provided in Beta form on those platforms. On Mac, the minimum OS
version is OSX 10.10 (Yosemite) and testing was performed on OSX 10.12
(Sierra).
The following plugin modules do not run under this release of GMAT on Mac
and Linux platforms:
Optimizer libFmincon
libMarsGRAM
and the Mac release does not support the following plugin:
libMsise86
Component Status
GMAT is distributed with production and Alpha/Beta components. Components
that are in Alpha/Beta status are turned off by default. The status of plugin
components is shown below.
Production quality plugin components:
libDataInterface
libEphemPropagator
libEventLocator
libFormation
libGmatFunction
libNewParameters
libPythonInterface
libStation
libGmatEstimation
libMatlabInterface
libFminconOptimizer
libProductionPropagators
libScriptTools
libYukonOptimizer
Alpha quality plugin components:
libCInterface
libGeometricMeasurements
libExtraPropagators
libPolyhedronGravity
libSaveCommand
libThrustFile
libEKF
Internal-only plugins (not included in public releases):
proprietary/libMarsGRAM
proprietary/libMsise86
proprietary/libNRLMsise00
proprietary/libSNOptimizer
proprietary/libVF13Optimizer
Contributors
The Navigation and Mission Design Branch at NASA’s Goddard Space Flight
Center performs project management activities and is involved in most phases of
the development process including requirements, algorithms, design, and testing.
The Ground Software Systems Branch performs design, implementation, and
integration testing. External particpants contribute to design, implementation,
testing and documentation. We use a collaborative development model that
enables innovation and actively involves the public and private sector having
seen contributions from 12 commercial firms. External participants for R2018a
include:
Thinking Systems, Inc. (system architecture and all aspects of
development)
Omitron, Inc (testing, requirements, specifications)
Emergent Space Technologies, Inc.
Past commercial and external contributors to GMAT include:
Air Force Research Lab (all aspects of development)
Boeing (algorithms and testing)
The Schafer Corporation (all aspects of development)
Honeywell Technology Solutions (testing)
Computer Sciences Corporation (requirements)
Korea Aerospace Research Institute
Chonbuk National University, South Korea
Korea Advanced Institute of Science and Technology
Yonsei University, South Korea
The NASA Jet Propulsion Laboratory (JPL) has provided funding for integration
of the SPICE toolkit into GMAT. Additionally, the European Space Agency’s
(ESA) Advanced Concepts team has developed optimizer plug-ins for the Non-
Linear Programming (NLP) solvers SNOPT (Sparse Nonlinear OPTimizer) and
IPOPT (Interior Point OPTimizer).
