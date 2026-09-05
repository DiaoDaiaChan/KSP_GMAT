# Assignment (=)

> 来源：第 1312-1329 页

Assignment (=)
Assignment (=) — Set a variable or resource field to a value, possibly using
mathematical expressions
Script Syntax
settable_item = expression
Description
The assignment command (in the GUI, the Equation command) allows you to
set a resource field or parameter to a value, possibly using mathematical
expressions. GMAT uses the assignment operator ('=') to indicate an assignment
command. The assignment operator uses the following syntax, where LHS
denotes the left-hand side of the operator, and RHS denotes the right-hand side
of the operator:
LHS = RHS
In this expression, the left-hand side (LHS) is being set to the value of the right-
hand side (RHS). The syntax of the LHS and RHS expressions vary, but both must
evaluate to compatible data types for the command to succeed.
Left-hand side
The left-hand side of the assignment command must be a single item of any of
the following types:
allowed resource (e.g. Spacecraft, Variable, Array)
resource field for allowed resources (e.g. Spacecraft.Epoch,
Spacecraft.DateFormat)
settable resource parameter (e.g. Spacecraft.X, ReportFile.Precision)
Array or Array element
See the documentation for a particular resource to determine which fields and
parameters can be set.
Right-hand side
The right-hand side of the assignment command can consist of any of the
following:
literal value
resource (e.g. Spacecraft, Variable, Array)
resource field (e.g. Spacecraft.Epoch, Spacecraft.DateFormat)
resource parameter (e.g. Spacecraft.X, ChemicalThruster.K1)
Array or Array element
mathematical expression (see below)
MATLAB function calls are considered distinct from the assignment command.
See the reference pages for more information.
GUI
The assignment command in the script language corresponds to the Equation
command in the GUI. The Equation properties box allows you to input both
sides of the expression into free-form text boxes. The default values on each side
are “Not_Set”; these are placeholders only, and are not valid during the mission
run. You can type into each box the same syntax described above for the script
language. When you click OK or Apply, GMAT validates each side of the
expression and provides feedback for any warnings or errors.
Remarks
Data type compatibility
In general, the data types of the left-hand side and the right-hand side must
match after all expressions are evaluated. This means that a Spacecraft resource
can only be set to another Spacecraft resource, numeric parameters can only be
set to numeric values, and String resources can only be set to string values.
Additionally, the dimension of Array instances must match for the command to
succeed. For numeric quantities, the assignment command does not distinguish
between integers and floating-point values.
Parameters
Parameters can be used on either side of an assignment command, but there may
be certain restrictions.
On the right-hand side of the command, any parameter can be used. If a
parameter accepts a dependency (such as Spacecraft.CoordinateSystem.X) and
the dependency is omitted, a default dependency value will be used. For
coordinate-system-dependent parameters, the default is EarthMJ2000Eq. For
central-body-dependent parameters, the default is Earth.
On the left-hand side, only settable (writable) parameters can be used.
Furthermore, no dependency can be specified, except in the special case that the
dependencies on both sides of the assignment command are equivalent. On the
left-hand side, the default values of omitted dependencies are automatically
taken to be the current values of the CoordinateSystem field of the referenced
Spacecraft and its origin.
These examples show valid and invalid usage of parameters:
Create Spacecraft aSat1 aSat2
aSat2.CoordinateSystem = 'EarthFixed'
Create Variable x
BeginMissionSequence
x = aSat1.EarthFixed.X % Valid: Parameter with dependency on RHS
x = aSat1.EarthMJ2000Eq.X % Valid: This and next statement are equiv.
x = aSat1.X % Valid: Default dep. value is EarthMJ2000Eq.
x = aSat1.Mars.Altitude % Valid: Parameter with dependency on RHS
x = aSat1.Earth.Altitude % Valid: This and next statement are equiv.
x = aSat1.Altitude % Valid: Default dependency value is Earth.
aSat2.X = 1e5 % Valid: Default parameter value is EarthFixed.
aSat2.EarthMJ2000Eq.X = 1e5 % INVALID: Dependencies not allowed on LHS.
aSat2.EarthFixed.X = 1e5 % Valid: Special case because value = default.
aSat2.EarthMJ2000Eq.X = aSat1.EarthFixed.X % INVALID: Dependency on LHS
aSat2.EarthMJ2000Eq.X = aSat1.EarthMJ2000Eq.X % INVALID: Dependency on LHS
aSat2.EarthFixed.X = aSat1.EarthFixed.X % Valid: Special case
% DANGEROUS! Valid, but sets EarthMJ2000Eq RHS values to EarthFixed LHS param.
aSat2.X = aSat1.EarthMJ2000Eq.X
% DANGEROUS! RHS default is EarthMJ2000Eq, LHS default is current setting on
% aSat2 (EarthFixed in this case).
aSat2.X = aSat1.X
Mathematical Expressions
The assignment command supports the use of inline mathematical expressions
on the right-hand side of the command. These expressions follow the general
syntax rules of MATLAB expressions, and can use a variety of operators and
built-in functions.
Parsing
Mathematical expressions are recognized by the presence of any of the operators
or built-in functions described below. Before execution, all white space (e.g.
spaces and tabs) is removed from the expression.
Data Types
Mathematical expressions operate on numeric values (integers or floating-point
numbers). This includes the following:
literal values
numeric resources (Variable, Array)
gettable resource parameters (e.g. Spacecraft.X, ChemicalThruster.K1)
Array elements
calculation parameters (e.g. Spacecraft.OrbitPeriod)
nested mathematical expressions
Several of GMAT’s operators and functions are vectorized, so they operate on
full Array resources as well as scalar numeric values.
Operators
Vectorized
operators + Addition or unary plus. X+Y adds X and Y. X and Y must have
the same dimensions unless either is a scalar.
- Subtraction or unary minus. -X is the negative of X, where X
can be any size. X-Y subtracts Y from X. X and Y must have the
same dimensions unless either is a scalar.
* Multiplication. X*Y is the product of X and Y. If both X and Y are
scalars, this is the simple algebraic product. If X is a matrix or
vector and Y is a scalar, all elements of X are multiplied by Y
(and vice versa). If both X and Y are non-scalar, X*Y performs
matrix multiplication and the number of columns in X must
equal the number of rows in Y.
' Transpose. X' is the transpose of X. If X is a scalar, X' is equal
to X.
Scalar
operators / Division. X/Y divides X by Y. If both X and Y are scalars, this is
the simple algebraic quotient. If X is a matrix or vector, each
element is divided by Y. Y must be a non-zero scalar quantity.
^ Power. X^Y raises X to the Y power. X and Y must be scalar
quantities. A special case is X^(-1), which when applied to a
square matrix X, returns the inverse of X.
When multiple expressions are combined, GMAT uses the following order of
operations. Operations begin with those operators at the top of the list and and
continue downwards. Within each level, operations proceed left-to-right.
1. parentheses ()
2. transpose ('), power (^)
3. unary plus (+), unary minus (-)
4. multiplication (*), division (/)
5. addition (+), subtraction (-)
Built-in Functions
GMAT supports the following built-in functions in mathematical expressions.
Supported functions include common scalar functions, meaning they accept a
single value only, such as sin and cos, matrix functions that operate on an entire
matrix or vector, and string functions.
Scalar
Math sin Sine. In Y = sin(X), Y is the sine of the angle X. X must
Functions be in radians. Y will be in the range [-1, 1].
cos Cosine. In Y = cos(X), Y is the cosine of the angle X. X
must be in radians. Y will be in the range [-1, 1].
tan Tangent. In Y = tan(X), Y is the tangent of the angle X.
X must be in radians. The tangent function is undefined
at angles that normalize to p/2 or -p/2.
asin Arcsine. In Y = asin(X), Y is the arcsine of X. X must
be in the range [-1, 1], and Y will be in the range [-p/2,
p/2].
acos Arccosine. In Y = acos(X), Y is the arccosine of X. X
must be in the range [-1, 1], and Y will be in the range
[0, p].
atan Arctangent. In Y = atan(X), Y is the arctangent of X. Y
will be in the range (-p/2, p/2).
atan2 Four-quadrant arctangent. In A = atan2(Y, X), A is the
arctangent of Y/X. A will be in the range (-p, p].
atan2(Y, X) is equivalent to atan(Y/X) except for the
expanded range.
log Natural logarithm. In Y = log(X), Y is the natural
logarithm of X. X must be non-zero positive.
log10 Common logarithm. In Y = log10(X), Y is the common
(base-10) logarithm of X. X must be non-zero positive.
exp Exponential. In Y = exp(X), Y is exponential of X (eX).
DegToRad Radian conversion. In Y = DegToRad(X), Y is the angle
X in units of radians. X must be an angle in degrees.
RadToDeg Degree conversion. In Y = RadToDeg(X), Y is the angle
X in units of degrees. X must be an angle in radians.
abs Absolute value. In Y = abs(X), Y is the absolute value
of X.
sqrt Square root. In Y = sqrt(X), Y is the square root of X. X
must be non-negative.
Numeric
Manipulation mod Modulus after division. mod(x,y) returns x - n*y,
Functions where n = floor(x/y) if y ~= 0. By convention,
mod(x,x) is x.
ceil Round towards plus infinity. ceil(X) rounds X to
the nearest integer towards plus infinity.
floor Round towards minus infinity. floor(X) rounds X
to the nearest integer towards minus infinity.
fix Round towards zero. fix(X) rounds X to the
nearest integer towards zero.
Random
Number randn Normally distributed pseudorandom numbers. R =
Functions randn(N) returns an N-by-N matrix containing
pseudorandom values drawn from the standard normal
distribution. R = randn() returns a single random
number.
rand Uniformly distributed pseudorandom numbers. R =
rand(N) returns an N-by-N matrix containing
pseudorandom values drawn from the standard uniform
distribution on the open interval (0,1). R = rand()
returns a single random number.
SetSeed Set seed for random number generation. SetSeed(X)sets
the seed for the random number generator where X must
b a postive real number. Note: SetSeed calls through to
the C++ 11 random number generator seed algorithm
that requires an unsigned integer. Since the GMAT script
language only supports real numbers, casting is
performed by the compiler which rounds the real
number down to the nearest integer. We recommend
passing in real numbers with zero mantissa (i.e "1.0" or
"198.0").
Matrix
Functions norm 2-norm. In Y = norm(X), Y is the 2-norm of X, where X
must be a vector (i.e. one dimension must be 1). If X is a
scalar, Y is equal to X.
det Determinant. In Y = det(X), Y is tthe cross product of the
vectors A and B. If X is a matrix, the number of rows must
equal the number of columns. If X is a scalar, Y is equal to
X. For efficiency, GMAT’s implementation of the
determinant is currently limited to matrices 9×9 or smaller.
cross Vector cross product. In C = cross(A,B), C is the vector
cross product of A and B . A and B must be 3 element arrays.
inv Inverse. In Y = inv(X), Y is the inverse of X. X must be a
matrix or a scalar. If X is a matrix, the number of rows must
equal the number of columns. X^(-1) is an alternate
syntax.
String
Manipulation strcat String concatenation. STROUT = strcat(S1,
Functions S2, ..., SN)concatenates strings. Inputs can
be combinations of string variables and string
literals.
strfind String find. INDEX = strfind(TEXT,PATTERN)
returns the starting index of the first instance of
PATTERN in TEXT. If PATTERN is not found, INDEX
= -1.
strrep String replace. NEWSTR =
strrep(OLDSTR,OLDSUBSTR,NEWSUBSTR)
replaces all occurrences of the string OLDSUBSTR
within string OLDSTR with the string NEWSUBSTR.
strcmp String compare. FLAG = strcmp(S1,S2)
compares the strings S1 and S2 and returns
logical 1 (true) if they are identical, and returns
logical 0 (false) otherwise.
sprintf Write formatted data to a string. STRING =
sprintf(FORMATSPEC, A, ...) formats data in
A,... according to FORMATSPEC which is a C-
style format spec.
Note: The GMAT sprintf function calls
through to the sprintf function in the c-library
iostream. Additionally, the GMAT script
language does not support an integer data type,
only doubles.
A format spec follows this prototype:
%[flags][width][.precision]
[length]specifier
Specifiers
a Hexadecimal floating point, lowercase.
A Hexadecimal floating point, uppercase
e Scientific notation (mantissa/exponent), lowercase
E Scientific notation (mantissa/exponent), uppercase
f Decimal floating point, lowercase
F Decimal floating point, uppercase
g Use the shortest representation: %e or %f
G Use the shortest representation: %E or %F
o Unsigned octal
x Unsigned hexadecimal integer, lowercase
X Unsigned hexadecimal integer, uppercase
Flags
+ Forces to preceed the result with a plus or minus sign (+
or -) even for positive numbers. By default, only negative
numbers are preceded with a - sign.
- Left-justify within the given field width; Right
justification is the default (see width sub-specifier).
# Used with o, x or X specifiers the value is preceeded
with 0, 0x or 0X respectively for values different than
zero. Used with a, A, e, E, f, F, g or G it forces the
written output to contain a decimal point even if no more
digits follow. By default, if no digits follow, no decimal
point is written.
0 Left-pads the number with zeroes (0) instead of spaces
when padding is specified (see width sub-specifier).
(space) If no sign is going to be written, a blank space is inserted
before the value.
Width
Minimum number of characters to be printed. If the value to be
printed is shorter than this number, the result is padded with blank
spaces. The value is not truncated even if the result is larger.
Precision
For a, A, e, E, f and F specifiers: this is the number of digits to be
printed after the decimal point (by default, this is 6).
For g and G specifiers: This is the maximum number of
significant digits to be printed.
For s: this is the maximum number of characters to be printed. By
default all characters are printed until the ending null character is
encountered. If the period is specified without an explicit value
for precision, 0 is assumed.
Integer specifiers are not supported as GMAT does not have an
integer data type in the script language.
Examples
Evaluate a basic algebraic equation:
Create Variable A B C x y
x = 1
Create ReportFile aReport
BeginMissionSequence
A = 10
B = 20
C = 2
y = A*x^2 + B*x + C
Report aReport y
Matrix manipulation:
Create Array A[2,2] B[2,2] C[2,2] x[2,1] y[2,1]
Create ReportFile aReport
A(1,1) = 10
A(2,1) = 5
A(1,2) = .10
A(2,2) = 1
x(1,1) = 2
x(2,1) = 3
BeginMissionSequence
B = inv(A)
C = B'
y = C*x
Report aReport A B C x y
Cloning a resource:
Create Spacecraft Sat1 Sat2
Sat1.Cd = 1.87
Sat1.DryMass = 123.456
Create ReportFile aReport
BeginMissionSequence
Sat2 = Sat1
Report aReport Sat2.Cd Sat2.DryMass
Using built-in functions:
Create Variable pi x y1 y2 y3
Create Array A[3,3]
Create Spacecraft aSat
Create ReportFile aReport
BeginMissionSequence
pi = acos(-1)
aSat.TA = pi/4
x = pi/4
A(1,1) = pi/4
y1 = sin(x)
y2 = sin(aSat.TA)
y3 = sin(A(1,1))
Report aReport y1 y2 y3
