# While

> 来源：第 1593-1600 页

While
While — Execute a series of commands repeatedly while a condition is met
Script Syntax
While logical expression
[script statement]
…
EndWhile
Description
The While command is a control logic statement that executes a series of
commands repeatedly as long as the value of the provided logical expression is
true. The logical expression is evaluated before every iteration of the loop. If the
expression is initially false, the loop is never executed. The syntax of the
expression is described in the script language reference.
See Also: Script Language, For, If
GUI
The While command GUI panel features a table in which you can build a
complex logical expression. The rows of the table correspond to individual
relational expressions in a compound logical expression, and the columns
correspond to individual elements of those expressions. The first line
automatically contains a default statement:
While DefaultSC.ElapsedDays < 1.0
The first column of the first row contains a placeholder for the While command
name. This cannot be changed. The first column of each additional row contains
the logical operator (&, |) that joins the expression in that row with the one
above it. To select a logical operator, double-click or right-click in the
appropriate box in the table, and a selection window will appear. Click the
correct operator and click OK to select it.
The Left Hand Side column contains the left-hand side of each individual
relational expression. Double-click the cell to type a parameter name. To set this
value from a parameter selection list instead, either click “…” to the left of the
cell you want to set, or right-click the cell itself. A ParameterSelectDialog
window will appear that allows you to choose a parameter.
The Condition column contains the conditional operator (==, ~=, <, etc.) that
joins the left-hand and right-hand sides of the expression. To select a relational
operator, double-click or right-click in the appropriate box in the table, and a
selection window will appear. Click the correct operator and click OK to select
it.
Finally, the Right Hand Side column contains the right-hand side of the
expression. This value can be modified the same way as the Left Hand Side
column.
When you are finished, click Apply to save your changes, or click OK to save
your changes and close the window. The command will be validated when either
button is clicked.
Examples
Propagate a spacecraft until it reaches a predefined altitude, reporting data at
each periapsis crossing:
Create Spacecraft aSat
aSat.SMA = 6800
aSat.ECC = 0
Create ForceModel aForceModel
aForceModel.Drag.AtmosphereModel = MSISE90
Create Propagator aProp
aProp.FM = aForceModel
Create ReportFile aReport
BeginMissionSequence
While aSat.Altitude > 300
Propagate aProp(aSat) {aSat.Periapsis}
Report aReport aSat.TAIGregorian aSat.Altitude
EndWhile
