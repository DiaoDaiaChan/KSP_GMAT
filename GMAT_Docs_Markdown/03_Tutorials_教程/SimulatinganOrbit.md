# Simulating an Orbit

> 来源：第 79-89 页

Chapter 5. Simulating an Orbit
Audience Beginner
Length 30 minutes
Prerequisites None
Script File Tut_SimulatingAnOrbit.script
Objective and Overview
Note
The most fundamental capability of GMAT is to propagate, or
simulate the orbital motion of, spacecraft. The ability to
propagate spacecraft is used in nearly every practical aspect of
space mission analysis, from simple orbital predictions (e.g.
When will the International Space Station be over my house?)
to complex analyses that determine the thruster firing sequence
required to send a spacecraft to the Moon or Mars.
This tutorial will teach you how to use GMAT to propagate a spacecraft. You
will learn how to configure Spacecraft and Propagator resources, and how to
use the Propagate command to propagate the spacecraft to orbit periapsis, which
is the point of minimum distance between the spacecraft and Earth. The basic
steps in this tutorial are:
1. Configure a Spacecraft and define its epoch and orbital elements.
2. Configure a Propagator.
3. Modify the default OrbitView plot to visualize the spacecraft trajectory.
4. Modify the Propagate command to propagate the spacecraft to periapsis.
5. Run the mission and analyze the results.
Configure the Spacecraft
In this section, you will rename the default Spacecraft and set the Spacecraft’s
initial epoch and classical orbital elements. You’ll need GMAT open, with the
default mission loaded. To load the default mission, click New Mission ( ) or
start a new GMAT session.
Rename the Spacecraft
1. In the Resources tree, right-click DefaultSC and click Rename.
2. Type Sat.
3. Click OK.
Set the Spacecraft Epoch
1. In the Resources tree, double-click Sat. Click the Orbit tab if it is not
already selected.
2. In the Epoch Format list, select UTCGregorian. You’ll see the value in
the Epoch field change to the UTC Gregorian epoch format.
3. In in the Epoch box, type 22 Jul 2014 11:29:10.811. This field is case-
sensitive, and must be entered in the exact format shown.
4. Click Apply or press the ENTER key to save these changes.
Set the Keplerian Orbital Elements
1. In the StateType list, select Keplerian. In the Elements list, you will see
the GUI reconfigure to display the Keplerian state representation.
2. In the SMA box, type 83474.318.
3. Set the remaining orbital elements as shown in the table below.
Table 5.1. Sat Orbit State Settings
Field Value
ECC 0.89652
INC 12.4606
RAAN 292.8362
AOP 218.9805
TA 180
4. Click OK.
5. Click Save ( ). If this is the first time you have saved the mission, you’ll be
prompted to provide a name and location for the file.
Figure 5.1. Spacecraft State Setup
Configure the Propagator
In this section you’ll rename the default Propagator and configure the force
model.
Rename the Propagator
1. In the Resources tree, right-click DefaultProp and click Rename.
2. Type LowEarthProp.
3. Click OK.
Configure the Force Model
For this tutorial you will use an Earth 10×10 spherical harmonic model, the
Jacchia-Roberts atmospheric model, solar radiation pressure, and point mass
perturbations from the Sun and Moon.
1. In the Resources tree, double-click LowEarthProp.
2. Under Gravity, in the Degree box, type 10.
3. In the Order box, type 10.
4. In Atmosphere Model list, click JacchiaRoberts.
5. Click the Select button next to the Point Masses box. This opens the
CelesBodySelectDialog window.
6. In the Available Bodies list, click Sun, then click -> to add Sun to the
Selected Bodies list.
7. Add the moon (named Luna in GMAT) in the same way.
8. Click OK to close the CelesBodySelectDialog.
9. Select Use Solar Radiation Pressure to toggle it on. Your screen should
now match Figure 5.2, “Force Model Configuration”.
10. Click OK.
Figure 5.2. Force Model Configuration
Configuring the Orbit View Plot
Now you will configure an OrbitView plot so you can visualize Sat and its
trajectory. The orbit of Sat is highly eccentric. To view the entire orbit at once,
we need to adjust the settings of DefaultOrbitView.
1. In the Resources tree, double-click DefaultOrbitView.
2. In the three boxes to the right of View Point Vector, type the values
-60000, 30000, and 20000 respectively.
3. Under Drawing Option to the left, clear Draw XY Plane. Your screen
should now match Figure 5.3, “DefaultOrbitView Configuration”.
4. Click OK.
Figure 5.3. DefaultOrbitView Configuration

Configure the Propagate Command
This is the last step before running the mission. Below you will configure a
Propagate command to propagate (or simulate the motion of) Sat to orbit
periapsis.
1. Click the Mission tab to display the Mission tree.
2. Double-click Propagate1.
3. Under Stopping Conditions, click the (...) button to the left of
Sat.ElapsedSecs. This will display the ParameterSelectDialog window.
4. In the Object List box, click Sat if it is not already selected. This directs
GMAT to associate the stopping condition with the spacecraft Sat.
5. In the Object Properties list, double-click Periapsis to add it to the
Selected Values list. This is shown in Figure 5.4, “Propagate Command
ParameterSelectDialog Configuration”.
Figure 5.4. Propagate Command ParameterSelectDialog Configuration
6. Click OK. Your screen should now match Figure 5.5, “Propagate
Command Configuration”.
7. Click OK.
Figure 5.5. Propagate Command Configuration
Run and Analyze the Results
Congratulations, you have now configured your first GMAT mission and are
ready to run the mission and analyze the results.
1. Click Save ( ) to save your mission.
2. Click the Run ( ).
You will see GMAT propagate the orbit and stop at orbit periapsis. Figure 5.6,
“Orbit View Plot after Mission Run” illustrates what you should see after
correctly completing this tutorial. Here are a few things you can try to explore
the results of this tutorial:
1. Manipulate the DefaultOrbitView plot using your mouse to orient the
trajectory so that you can to verify that at the final location the spacecraft is
at periapsis. See the OrbitView reference for details.
2. Display the command summary:
1. Click the Mission tab to display the Mission tree.
2. Right-click Propagate1 and select Command Summary to see data
on the final state of Sat.
3. Use the Coordinate System list to change the coordinate system in
which the data is displayed.
3. Click Start Animation ( ) to animate the mission and watch the orbit
propagate from the initial state to periapsis.
Figure 5.6. Orbit View Plot after Mission Run
