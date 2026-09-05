# Script Language

> 来源：第 1776-1796 页

Script Language
Script Language — The GMAT script language
Script Structure
A GMAT script is a text file consisting of valid script syntax elements, such as
initialization statements, Mission Sequence commands, and comments. These
syntax elements are described later in this specification.
At the highest level, a GMAT script is made up of two sections: Initialization and
the Mission Sequence. These sections each contain statements, but they have
different rules about which sorts of statements are valid. The
BeginMissionSequence command defines the beginning of the Mission
Sequence section.
Initialization
The first section in a script file, referred to as Initialization, is responsible for
creating resources and setting their initial state. The Initialization section can
contain the following types of statements:
resource creation statements (the Create statement)
initialization statements
Only literal assignments are allowed in this section; no execution of commands
or evaluation of parameters is done. In the GUI, the Initialization section maps
directly to the Resources tree. All resources created, and all fields set, in this
section appear as resources in the GUI when the script is loaded.
Mission Sequence
The Mission Sequence section contains the Mission Sequence, or the list of
GMAT commands that are executed sequentially when the mission is run. The
Mission Sequence section can contain the following types of statements:
command statements
The Mission Sequence begins at the first instance of the BeginMissionSequence
command; therefore, this must be the first command statement in the script file.
For backwards compatibility, if the BeginMissionSequence command is
missing, the Mission Sequence begins with the first command encountered.
In the GUI, the Mission Sequence section maps directly to the Mission tree.
Each statement in the script (with the exception of the BeginScript/EndScript
compound command) is displayed as a single element in the tree.
Basic Syntax
Source Text
A GMAT script consists of a single file containing characters from the 7-bit US-
ASCII character set. The script language is case-sensitive, so this line creates
four different Variable resources:
Create Variable x X y Y
The script language is made up of lines. A line can be:
empty
a comment (see Comments, below)
a statement (see Statements)
Statement lines can be split over multiple physical lines with the continuation
marker (“...”).
Line Termination
Script lines are terminated by any of the following ASCII character sequences:
line feed (hex: 0A)
carriage return (hex: 0D)
carriage return followed by line feed (hex: 0D0A)
White Space
White space can appear above or below any line, before or after any statement
within a line, and many other places in a script. The following characters are
recognized as white space:
space (hex: 20)
horizontal tab (hex: 09)
Horizontal tab characters are preserved in string literals, but are replaced by
spaces in some other contexts (e.g. equations, comments).
Comments
Comments begin with the percent symbol (“%”, hex: 25) and extend to the end of
the line. There is no multi-line or embedded comment in the script language.
File Paths
Several resource types have fields that accept file paths as input. The general
syntax of such paths is common to the language, but some specific behavior is
specified by each resource.
Forward slashes and backslashes can be used interchangeably within GMAT, and
can be mixed in a single path. The following three paths are considered identical:
data/planetary_ephem/spk/de421.bsp
data\planetary_ephem\spk\de421.bsp
data\planetary_ephem/spk\de421.bsp
Absolute paths are passed to the underlying operating system as-is, aside from
normalizing the slashes.
For input files, relative paths are first considered relative to the script file, then to
a location defined by each resource type separately, and usually defined in the
GMAT startup file. For details, see the reference documentation for each
resource type.
For output files, relative paths are considered relative to the script file. If only a
filename is specified, the file is placed into the output location defined in the
GMAT startup file (usually GMAT's output folder).
File paths are written as string literals (see Strings under Data Types). Quotes are
mandatory if the path contains spaces, but are optional otherwise.
Data Types
Literals
Integers
Integers are written as a sequence of literal digits, with no decimal. Preceding
zeros and prepended signs (+ or -) are allowed. Scientific notation is not
permitted.
Real Numbers
Real numbers can be written in any of the following formats:
12 (whole number)
12.5 (decimal)
1.25e1 or 1.25e-1 (scientific notation)
In all formats, the base can contain preceding or trailing zeros. In scientific
notation, the exponent can be prepended by a sign (+ or -) and can contain
preceding zeros, but cannot contain a decimal. The exponent delimiter is case-
insensitive (e.g. "e" or "E").
Strings
String literals are delimited by single-quote characters (“'”, hex: 27).
All language-supported characters are allowed in strings, with the exceptions
below. There are no escape characters or character substitute sequences (such as
“\n” for line feed).
In Initialization, the following characters are not allowed in string literals:
some non-printable characters (NUL, SUB) (hex: 00, 1A)
line termination characters (LF, CR) (hex: 0A, 0D)
percent character (“%”) (hex: 25)
In the Mission Sequence, the following characters are not allowed in string
literals:
some non-printable characters (NUL, SUB) (hex: 00, 1A)
line termination characters (LF, CR) (hex: 0A, 0D)
percent character (“%”) (hex: 25)
Quotes are generally optional, but are mandatory in Initialization if the string
contains whitespace, any script language symbols, or any GMAT-recognized
elements (e.g. keywords, resource names). They are mandatory in the Mission
Sequence in the same instances, and additionally if the string contains
mathematical operators and certain non-printable characters. We recommend
quoting all string literals.
Booleans
The following boolean values are supported:
true (alias: on)
false (alias: off)
Boolean literals are case-insensitive.
Enumerated Values
Many resource fields accept enumerated values. For example,
Spacecraft.DateFormat accepts one of 10 values (A1ModJulian,
A1Gregorian, etc.). Enumerated values are written as string literals. Quotes are
always optional, as none contain spaces or special characters.
References
References to resources and resource parameters are indicated by the name of
the resource or resource parameter. References are written as string literals.
Quotes are always optional, as resource names and parameters cannot contain
spaces or special characters.
Resources
Resource Types
Resources in GMAT are instances of a base resource type that are given user-
defined names and store data independently of other resources of the same type.
Resource types include Spacecraft, GroundStation, and Variable. They cannot
be used directly; they must first be instantiated with the Create statement. For
example:
Create Spacecraft aSat
In the example, Spacecraft is the resource type and aSat is the resource. This is
similar to the concept of classes and objects in object-oriented programming,
where GMAT’s resource types are analogous to classes and its resources are
analogous to objects.
Naming Rules
Resources must be named according to these rules:
Name must be made up of ASCII letters, numbers, or the underscore
character (“_”). This corresponds to hex values 30–39, 41–5A, 5F, and 61–
7A.
Name must begin with a letter (A–Z or a–z, hex: 41–5A or 61–7A)
Name cannot be a reserved keyword or command name
Shadowing
When the same name is used for multiple purposes in a script, the shadowing
rules apply to determine how a reference to the name is interpreted.
Resource names must be unique within a script. If a script attempts to create
multiple resources that have the same case-sensitive name, the first Create
statement in the script with that name is executed and all subsequent ones are
ignored. The conflict is noted in a warning message.
Caution
GMAT does not test to ensure that Resource names and
function names are unique. Care should be taken to use unique
names for user-defined GMAT, MATLAB, and Python
functions to avoid name clashes.
Command names and keywords are reserved. They cannot be used as resource
names. See the Keywords section for a list of keywords.
Built-in function names (like sin or cos) can be used as resource names with
one exception: a reference to, for example, “sin(1)” on the right-hand side of an
equal sign will be interpreted as a call to the sin built-in function, not element 1
of an Array resource named sin. The same is true for the other built-in
functions.
Resource type names (like “Spacecraft”) can be used as resource names. In such
an instance, the conflict is resolved by the context. For example:
Create Spacecraft Spacecraft
Create Spacecraft aSat
In the example, GMAT knows by context that in the second Create statement,
the argument “Spacecraft” refers to the resource type, not the resource instance
created in the first statement.
Compound Types
Array of Literals
Arrays of literals are accepted as input by some resources. Arrays of booleans,
integers, and real numbers are surrounded by square brackets (“[“ and “]”, hex:
5B and 5D). Arrays of strings are surrounded by curly brackets (“{“ and “}”,
hex: 7B and 7D). In all cases, the values are separated by whitespace or commas.
Only one-dimensional arrays of literals are supported. See the following
examples.
anOrbitView.DrawObject = [true true] % boolean array
aSat.OrbitColor = [255 0 0] % integer array
anOrbitView.ViewPointVector = [3e4, 1.2, -14] % real array
aSpacecraft.OrbitSpiceKernelName = ...
{'file1.bsp', 'file2.bsp'} % string array
Arrays of References
Some resources accept arrays of references to other resources or resource fields.
These reference arrays are surrounded by curly brackets (“{“ and “}”, hex: 7B
and 7D) and the values are separated by whitespace or commas. Only one-
dimensional arrays of references are supported. The values can optionally be
surrounded by single quotes. See the following example.
aForceModel.PointMasses = {'Luna', Mars} % array of resource references
aReport.Add = {Sat1.X, 'Sat1.Y', Sat1.Z} % array of parameter references
Conversion
In contexts that accept a real number, integer literals (those with no fractional
value) are automatically converted to the equivalent floating-point value upon
execution.
There is no built-in conversion between string values and numeric values,
though such a conversion may be implemented by individual commands.
Keywords
The script language recognized these reserved keywords:
Create
GMAT
function
In addition, all command names are reserved, including commands created by
active plugins.
Expressions
The only types of expressions common to multiple commands are logical
expressions, which are used by the If/Else and While commands. They are
documented here instead of in both command references.
Relational Operators
The following relational operators are supported in logical expressions:
<
less than
<=
less than or equal to
>
greater than
>
greater than or equal
to
==
equal to
~=
not equal to
The relational operators are scalar operators; they do not operate on Array
resources (only individual elements).
Each relational operator operates on the values of its arguments, not on their
identity. Consider the example:
Create Variable x y
x = 5
y = 5
BeginMissionSequence
If x == y
% body
EndIf
Logical Operators
The following logical operators are supported in logical expressions:
&
logical AND (short-circuit operator)
|
logical OR
The logical AND operator exhibits short-circuit behavior. That is, if the left-hand
side of the operator evaluates to false, the right-hand side is not evaluated,
though it is still parsed for syntactic validity.
Logical Expressions
Logical expressions are composed of relational expressions combined with
logical operators.
Relational expressions must contain one relational operator and two valid
arguments. Literal boolean values are not supported, and numeric values are not
interpreted as truth or falsehood. See the following examples:
1 == 5 % false
1 ~= 5 % true
true % error
1 % error
A % where "A" is an Array resource; error
1 == 5 <= 3 % error
Logical expressions must contain at least one relational expression. Multiple
relational expressions are combined using logical operators. All relational
expressions are evaluated first, from left to right, then the full logical expression
is evaluated from left to right, though the short-circuit AND operator (“&”) may
terminate the full evaluation. Parentheses are not allowed. See the following
examples:
1 == 1 % true
2 ~= 4 | 3 == 3 % true
8 >= 3 & 3 < 4 % true
2 < 4 & 1 > 3 | 5 == 5 % true
2 < 4 & (1 > 3 | 5 == 5) % error
1 & 1 % error
true | false % error
Statements
Statement Structure
Script statements consist of (in order):
1. Optional "GMAT " prefix
2. Valid statement syntax (with optional line continuation)
3. Optional semicolon
4. Line termination sequence
Any statement in the script may be prefixed by the characters “GMAT “. This
prefix is optional and has no effect, but is supported for backward compatibility.
A statement can be split over multiple physical lines by using the line
continuation marker, three sequential period characters (“...”, hex: 2E2E2E),
before each line break within the statement.
Any statement may be terminated with a semicolon character (“;”, hex: 3B). The
semicolon is optional and has no effect, but is supported for backward
compatibility. Multiple statements cannot be combined on a line.
White space may occur before or after a statement, or between any of the
components listed above. It is also generally allowed anywhere inside of a
statement, and any exceptions are noted in the documentation specific to that
statement.
The Create Statement
The Create statement is a special statement that creates resources and assigns
them names. It is only valid in the Initialization section of the script. It has the
following components:
1. Create keyword
2. Resource type
3. Resource name(s)
The Create keyword indicates the start of the statement. It is followed by the
resource type, which indicates the type of resource to create. This is followed by
a resource name, a user-defined name that is then used to refer to that particular
resource. This name must follow the resource naming rules, listed previously.
The only exception to this syntax is when creating an Array resource, in which
case the dimension of the resource must also be specified
Multiple resource names are allowed, in which case multiple resources of the
same type will be created. Multiple names are separated by white space or by
commas (“,”, hex: 2C).
See the following examples:
Create Spacecraft aSat % creates a resource "aSat" of type Spacecraft
Create ForceModel aFM
Create Propagator aProp
Create Variable x y % creates two Variable resources: "x" and "y"
Create String s1, s2 % creates two String resources: "s1" and "s2"
Create Array A[2,2] % creates a 2x2 Array resource named "A"
Initialization Statements
Initialization statements are special statements that assign initial values to
resource fields. They are only valid in the Initialization section of the script, and
generally take the following form:
resource.field = value
Some fields, like those on ForceModel resources, have a multiple-dotted form:
ForceModel.GravityField.PrimaryBody.Degree = value
All initialization statements are composed of the following elements:
1. Resource name
2. Period character (“.”, hex: 2E)
3. Field name, potentially in multiple-dotted form
4. Equal character (“=”, hex: 3D)
5. Initial field value
The resource name must refer to a resource created previously in same script.
The field name must refer to a valid field that exists for the associated resource
type. Parameters cannot be set with an initialization statement, though it is valid
to set a dual-mode field (one that can also be a parameter). Fields and parameters
are listed in the documentation for each resource type.
All values are taken literally; no evaluation is performed. Therefore, numeric
and string values must be specified as literals, and resource names and
parameters are stored as references. See the following example:
Create Spacecraft aSat
Create XYPlot aPlot
Create Variable x y z
x = 7100 % valid
aSat.X = 7100 % valid
aSat.X = 7100 + 2 % error (mathematical expression)
aSat.X = x % error (field accepts literal, and variable
% evaluation does not occur)
aPlot.XVariable = x % valid (field accepts reference to Variable x)
aPlot.YVariables = {y, z} % valid (field accepts array of references to
% Variables y and z)
For backwards compatibility, there is one exception to the literal-value rule:
Spacecraft resources can copied with an initialization statement like:
Create Spacecraft aSat1 aSat2
aSat2 = aSat1 % Valid only for Spacecraft resources
Fields that have no assigned value in the Initialization section of the script
remain at their default values, as specified in the documentation for each
resource type.
Command Statements
Command statements invoke GMAT commands. They must appear in the
Mission Sequence section of the script. One special command,
BeginMissionSequence, initiates the Mission Sequence.
Command statements are displayed by the GUI as individual line items in the
Mission tree. The only exception is the BeginScript/EndScript compound
command; this is displayed as a single ScriptEvent item by the GUI.
Command statements are composed of the following elements:
1. Command name (except assignment commands)
2. Optional label
3. Command arguments
The command name is the name of the command being invoked (e.g. Propagate
or BeginFiniteBurn). The command name is mandatory with one exception: the
assignment command is indicated by its structure (“LHS = RHS”) instead of its
name.
A command label is an optional string literal that can be added immediately after
the command name. This label is used by the GUI to “name” the statement in the
Mission tree, and is intended for a short text description to aid the user. It must
be single-quoted, whether or not it contains spaces. The command label may
contain any ASCII character except certain non-printable characters (NUL,
SUB), line termination characters (LF, CR), the percent sign (“%”), and the single
quote (“'“). If the command label is omitted, the Mission tree statement is given
a default label made up of the command name and an ID number. For example,
if the third Propagate command in the script is unlabeled, it will be given the
default label “Propagate3”.
The command arguments control the behavior of the command. The syntax of
the arguments is specified by each command individually, and is documented
separately. Some commands, such as Stop, have no arguments.
See the following example:
Propagate 'Prop to periapsis' aProp(aSat) {aSat.Periapsis}
In the example, “Propagate” is the command name, “'Prop to periapsis'” is
the command label, and “aProp(aSat) {aSat.Periapsis}” is the argument
string.
Compound Statements
Compound statements are command statements that control the execution of
other command statements. Compound statements are composed of three
elements:
1. Begin statement
2. Body
3. End statement
The begin statement carries the name of the command itself, while the end
statement begins with the string “End”. For example, the While command is a
compound command composed of two statements:
While ['label'] arguments
[body]
EndWhile
The If/Else compound command is composed of three statements:
If ['label'] arguments
[body]
Else
[body]
EndIf
The body of a compound command may consist of independent command
statements, possibly including other compound statements. Certain compound
commands may limit the commands that can be present in the body, while other
commands may only be contained within certain compound commands. These
limitations are documented separately for each command.
Processing
GMAT processes a script in two phases: interpretation and execution. This
section gives an overview of the processing sequence; low-level details are
documented in Chapter 17 of the GMAT Architectural Specification.
Interpretation
GMAT interprets a script in two stages: a parsing stage and a validation stage. In
the parsing stage, GMAT reads and interprets each line of the script sequentially.
As it interprets a line, it checks it for syntactic correctness and performs any
initialization needed by the line. For example, if the line being interpreted is a
Create statement, the related resource is created. If GMAT encounters an
initialization line, it assigns the appropriate value to the indicated resource field.
And if it encounters a command statement, it creates the command structure and
interprets its arguments. All language, resource initialization, and command
syntax errors are caught during this parsing stage.
In the validation stage, GMAT checks that all references between resources are
valid. For example, if the script indicates that a Spacecraft resource should be
defined in relation to a specific CoordinateSystem resource, the reference is
validated during this stage. The validation checks that all referenced resources
exist and are of the correct type.
The two-stage interpretation method affects the order of statements in the script.
For example, Create statements must appear in the script above any
initialization statements that reference the resource being created. But because
validation is performed separately, the Create statement for a
CoordinateSystem resource can appear in the script below an initialization line
that references this resource. See the following examples:
Create Spacecraft aSat
% This is valid; the aSat resource has been created by the line above.
aSat.DateFormat = TAIGregorian
% This is invalid; the aReport resource has not yet been created.
aReport.Filename = 'report.txt'
Create ReportFile aReport
Create XYPlot aPlot
% This is valid; the reference to aSat is validated
% after all resources are created.
aPlot.XVariable = aSat.A1ModJulian
Create Spacecraft aSat
Once both stages have completed, the script has been loaded into GMAT. In the
GUI, if any, the Resources tree is populated with the resources created in the
Initialization section of the script, and the Mission tree is populated with the
command statements in the Mission Sequence.
The interpretation phase is also sometimes called the “build” phase or the “load”
phase.
Execution
When a mission is run, GMAT first builds interconnections between resources,
then performs command execution. In this phase, all commands in the Mission
Sequence are executed sequentially, in the order of definition in the script. When
a command statement is executed, its arguments are fully processed by the
command, and any remaining errors are reported. Examples of execution-phase
errors include mismatched data types, out-of-bounds array references, and
divide-by-zero errors.
Processing Errors
If GMAT encounters an error during the interpretation stage (parsing or
validation), the mission is not loaded. Instead, GMAT reverts to a minimum
mission consisting of:
SolarSystem
Default CoordinateSystem resources: EarthMJ2000Eq,
EarthMJ2000Ec, EarthFixed, EarthICRF
If an error is encountered during the execution stage (linking or command
execution), execution of the mission stops at the point of the error.
