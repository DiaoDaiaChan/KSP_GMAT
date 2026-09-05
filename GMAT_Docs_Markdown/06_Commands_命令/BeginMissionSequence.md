# BeginMissionSequence

> 来源：第 1342-1347 页

BeginMissionSequence
BeginMissionSequence — Begin the mission sequence portion of a script
Script Syntax
BeginMissionSequence
Description
The BeginMissionSequence command indicates the end of resource
initialization and the beginning of the mission sequence portion of a GMAT
script. It must appear once as the first command in the script, and must follow all
resource creation lines.
See Also: Script Language
GUI
The BeginMissionSequence command is managed automatically when building
mission sequences using the GUI mission tree. However, when editing the
GMAT script directly, either with the GMAT script editor or with an external
editor, you must insert the BeginMissionSequence command manually.
Remarks
The BeginMissionSequence is a script-only command that is not needed when
working from the GUI. It indicates to GMAT that the portion of the script above
the command consists of static resource initialization that can be performed in
any order, and that the portion below the command consists of mission sequence
commands that must be executed sequentially. This and other rules of the
scripting language are discussed in detail in the script language reference.
Examples
A minimal GMAT script that propagates a spacecraft:
Create Spacecraft aSat
Create Propagator aProp
BeginMissionSequence
Propagate aProp(aSat) {aSat.ElapsedDays = 1}
