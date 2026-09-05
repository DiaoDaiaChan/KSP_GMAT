# NuclearPowerSystem

> 来源：第 754-764 页

NuclearPowerSystem
NuclearPowerSystem — A nuclear power system
Description
The NuclearPowerSystem models a nuclear power system including power
generated as function of time and distance from the sun.
For a complete descripton of how to configure all Resources required for electric
propulsion modelling, see the Tutorial named Chapter 12, Electric Propulsion
See Also ElectricTank, ElectricThruster, SolarPowerSystem
Fields
Field Description
AnnualDecayRate
The annual decay rate of the power system.
Data Type Real
Allowed Values 0 <=Real <= 100
Access set
Default Value 5
Units Percent/Year
Interfaces GUI, script
BusCoeff1
Coefficient of power required by spacecraft bus.
Data Type Real
Allowed Values Real
Access set
Default Value 0.3
Units kW
Interfaces GUI, script
BusCoeff2
Coefficient of power required by spacecraft bus.
Data Type Real
Allowed Values Real
Access set
Default Value 0
Units kW*AU
Interfaces GUI, script
BusCoeff3
Coefficient of power required by spacecraft bus.
Data Type Real
Allowed Values Real
Access set
Default Value 0
Units kw*AU2
Interfaces GUI, script
EpochFormat
The epoch format for the PowerInitialEpoch field.
Data Type String
Allowed Values Valid Epoch format.
Access set
Default Value UTCGregorian
Units N/A
Interfaces GUI, script
InitialEpoch The initial epoch of the system used to define power
system elapsed lifetime.
Data String
Type
Allowed Valid GMAT Epoch consistent with
Values PowerInitialEpochFormat
Access set
Default 01 Jan 2000 11:59:27.966
Value
Units N/A
Interfaces GUI, script
InitialMaxPower
The maximum power generated at the
PowerInitialEpoch.
Data Type Real
Allowed Values Real >= 0
Access set
Default Value 1.2
Units kW
Interfaces GUI, script
Margin
The required margin between power left after power
bus, and power used by the propulsion system.
Data Type Real
Allowed Values 0 <=Real <= 100
Access set
Default Value 5
Units Percent
Interfaces GUI, script
GUI
The GUI for the NuclearPowerSystem is shown below.
Remarks
Computation of Base Power
The NuclearPowerSystem models power degradation as a function of time. You
must provide a power system initial epoch, the power generated at that epoch,
and an annual power decay rate. Additionally, the AnnualDecayRate field
models the power degredation on a per year basis. The base power is computed
using
where "tau" is the power AnnualDecayRate, P_0 is InitialMaxPower, and
"delta t" is the elapsed time between the simulation epoch and InitialEpoch.
Computation of Bus Power
The power required by the spacecraft bus for all subsystems other than the
propulsion system is computed using
where A_Bus, B_Bus, and C_Bus are BusCoeff1, BusCoeff2, and BusCoeff3
respectively and r is the distance from the Sun in Au.
Computation of Power Available for Propulsion
Total power is compute using
Thrust power available for electric propulsion is finaly computed using
Where "delta M" is power Margin.
Examples
Create a NuclearPowerSystem and attach it to a Spacecraft.
Create Spacecraft DefaultSC
DefaultSC.PowerSystem = NuclearPowerSystem1
Create NuclearPowerSystem NuclearPowerSystem1
BeginMissionSequence
For a complete descripton of how to configure all Resources required for electric
propulsion modeling, see the Tutorial named Chapter 12, Electric Propulsion.
