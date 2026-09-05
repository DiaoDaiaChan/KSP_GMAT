# Stop

> 来源：第 1549-1553 页

Stop
Stop — Stop mission execution
Description
The Stop command stops execution of the current mission at the point that the
command is encountered and returns control to the GMAT interface. The effect
is similar to that of the Stop button on the GUI toolbar.
GUI
The Stop command can be inserted into and deleted from Mission tree, but the
command has no GUI panel of its own.
Remarks
The Stop command stops execution of the current mission, not the GMAT
application. All data displayed to the point, at which the script was stopped (e.g.
OrbitView windows, GroundTrackPlot windows), remain available for
manipulation. Using the Stop command within a loop or solver structure will
stop execution at the first iteration during which the command is encountered.
Examples
Stopping the execution of a script between commands:
Create Spacecraft aSat
Create ForceModel aForceModel
Create Propagator aProp
aProp.FM = aForceModel
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 30};
Stop
Propagate aProp(aSat) {aSat.ElapsedDays = 30};
Stopping the execution of a solver structure for further investigation:
Create ChemicalTank aTank
Create ForceModel aForceModel
Create DifferentialCorrector aDC
Create Spacecraft aSat
aSat.Tanks = {aTank}
Create Propagator aProp
aProp.FM = aForceModel
Create ImpulsiveBurn anIB
anIB.DecrementMass = true
anIB.Tanks = {aTank}
BeginMissionSequence
Target aDC
Vary aDC(anIB.Element1 = 0.5)
Maneuver anIB(aSat)
Propagate aProp(aSat) {aSat.Periapsis}
If aSat.aTank.FuelMass < 10
Stop
EndIf
Achieve aDC(aSat.Altitude = 1000)
