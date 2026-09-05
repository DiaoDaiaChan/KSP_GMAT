# FileInterface

> 来源：第 601-607 页

FileInterface
FileInterface — An interface to a data file
Description
The FileInterface resource is an interface to a data file that can be used to load
mission data, like Spacecraft state information and physical properties. Once an
interface is established to a file, the Set command can be used to load the data
and apply it to a destination.
The following file formats are currently supported:
TVHF_ASCII: ASCII format of the TCOPS Vector Hold File (TVHF),
defined by the NASA Goddard Space Flight Center Flight Dynamics
Facility. This file contains spacecraft state and physical information that can
be transferred to a Spacecraft resource.
See Also: Set
Fields
Field Description
Filename
Full path of the file to read. Relative paths are interpreted as relative
to the directory containing the GMAT executable. If the path is
omitted, it is assumed to be “./”.
Data Type String
Allowed Values Valid file path
Access set
Default Value (None)
Units N/A
Interfaces GUI, script
Format
Format of the file to read. Currently, the only allowed format is
“TVHF_ASCII”.
Data Type Enumerated value
Allowed Values TVHF_ASCII
Access set
Default Value TVHF_ASCII
Units N/A
Interfaces GUI, script
GUI
The FileInterface GUI has two fields: a list of accepted options for Format
(currently only TVHF_ASCII), and an input box for Filename. Click Browse to
the right of the Filename box to interactively select a file.
Remarks
Each file format supported by the FileInterface resource exposes a set of
keywords that can be used to extract certain data elements. These keywords can
be used in the Data option of the Set command, as follows:
Set destination source (Data = {keyword[, keyword]})
If the 'All' keyword is used, those fields with a checkmark in the “All” column
are selected.
TVHF_ASCII
Keyword Source field Description 'All'
CartesianState "CARTESIAN Cartesian state elements ✓
COORDINATES" (X, Y, Z, VX, VY, VZ)
Cr "CSUBR" Coefficient of reflectivity ✓
Epoch "EPOCH TIME Epoch of state vector ✓
FOR
ELEMENTS"
Limitations
The following limitations apply to the TVHF_ASCII format:
Only the J2000 coordinate system is supported.
Only the first record in a multiple-record file is loaded.
Examples
Read a TVHF file and use it to configure a spacecraft.
Create Spacecraft aSat
Create FileInterface tvhf
tvhf.Filename = 'statevec.txt'
tvhf.Format = 'TVHF_ASCII'
BeginMissionSequence
Set aSat tvhf
