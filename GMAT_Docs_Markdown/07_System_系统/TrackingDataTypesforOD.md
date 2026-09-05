# Tracking Data Types for OD

> 来源：第 1810-1819 页

Tracking Data Types for OD
Tracking Data Types for Orbit Determination — This section describes tracking
data types and file formats for orbit determination.
Measurement Types Supported
GMAT supports the following measurement types for orbit determination.
GMAT
Measurement
Measurement Measurement Description
Units
Type Name
DSN_SeqRange DSN Sequential Ranging (TRK-2-34 data Range Units
Type 7), ramped and un-ramped
DSN_TCP DSN Total Count Phase (TRK-2-34 data Hertz
Type 17) measurements, implemented as a
derived "Doppler" type measurement using
successive phase measurements
GPS_PosVec Earth-fixed position vectors from a spacecraft Kilometers
on-board GPS receiver
Range Two-way transponder range. A round-trip Kilometers
range measurement which includes the
Spacecraft Transponder.HardwareDelay
RangeRate Two-way coherent transponder range-rate. Kilometers/sec
This is modeled as the difference between
range measurements at the end and start of
the Doppler count interval, divided by the
length of the count interval. The
measurement is time-tagged at the end of the
interval.
The GMAT measurement type names listed are the string names to be used in
instances of ErrorModel, AcceptFilter, RejectFilter, and TrackingFileSet,
and in the GMAT GMD-format tracking data file to identify each measurement
type to GMAT.
Deprecated Measurement Type Names
This version of GMAT deprecates the DSNRange/Range_RU and
Doppler/Doppler_HZ measurement type names. They are replaced by the new
consistent naming convention introduced in the previous section. The old names
will still work in the current version of GMAT, but users are encouraged to
transition their scripts to use of the new type names.
The new data type names employ the same name in the GMD file, error model,
and tracking file set tracking configuration, eliminating the need for a mapping
between the names employed in each resource. For those still using the
deprecated data type names, the following table provides a guide.
GMD File and ErrorModel and
TrackingFileSet.AddTrackingConfig StatisticsAccept/RejectFilter
Measurement Type Name Measurement Type Name
DSNRange Range_RU
Doppler Doppler_HZ
GMAT Tracking Data File Formats
GMAT uses a native ASCII tracking data file format called a “GMAT
Measurement Data File”, or GMD file. This file format currently implements the
following observation measurement types:
DSN Sequential Ranging, TRK-2-34 data Type 7
Derived Doppler using successive DSN Total Count Phase Doppler tracking
measurements, TRK-2-34 data Type 17
DSN transmit frequency ramp records, TRK-2-34 data Type 9
Earth-fixed position vectors from a spacecraft on-board GPS receiver
Two-way coherent transponder range measurements
Two-way coherent transponder range-rate measurements
Each GMD file consists of a series of space-delimited ASCII records. Details of
the GMD file format for each observation type are provided in the following
sections. A single GMD file may contain one or more of the record types
described below, but ramp records must be in a separate file. For further details
on the TRK-2-34 data formats, please consult the TRK-2-34 DSN Tracking
System Data Archival Format, 820-013 Deep Space Network External Interface
Specification.
DSN Sequential Range
DSN TRK-2-34 Sequential Ranging employs the DSN_SeqRange measurement
type. DSN_SeqRange is a round-trip range observation measured in range units.
The GMD record format for DSN_SeqRange tracking data is shown in the table
below.
Field Description
1 Observation receive time in TAIModJulian
2 Observation type name - DSN TRK-2-34 Type 7 Sequential Range
= DSN_SeqRange
3
Observation type index number - 9004 = DSN_SeqRange (TRK-2-
34)
4 Downlink Ground station pad ID
5 Spacecraft ID
6 Range observable (meas_rng or rng_obs from TRK-2-34 Sequential
Range CHDO)
7 Uplink frequency band indicator - 0 = unknown, 1 = S-band, 2 = X-
band, 3 = Ka-band, 4 = Ku-band, 5 = L-band
8 Uplink frequency in Hz
9 Range modulo value (rng_modulo from TRK-2-34 Sequential
Range CHDO)
The transmit frequency specified in the TRK-2-34 range data GMD file is only
used if a frequency ramp table is not available. If a transmit frequency ramp
record file is provided on the TrackingFileSet.RampTable field, the transmit
frequency will be determined from the ramp table and the frequency specified in
the range data GMD file will be ignored. A sample of GMD data records for
TRK-2-34 Sequential Range data is shown below.
% - 1 - - 2 - 3 4 5 - 6 - 7 - 8 - - 9 -
27236.157789352 DSN_SeqRange 9004 45 59 +9.810325186004e+005 1 +2.091414432000e+009 +1.048576000000e+006
27236.158240741 DSN_SeqRange 9004 45 59 +5.813243487947e+005 1 +2.091414432000e+009 +1.048576000000e+006
27236.158692130 DSN_SeqRange 9004 45 59 +1.863046908683e+005 1 +2.091414432000e+009 +1.048576000000e+006
27236.159143519 DSN_SeqRange 9004 45 59 +8.450116485521e+005 1 +2.091414432000e+009 +1.048576000000e+006
DSN Total Count Phase
DSN TRK-2-34 Total Count Phase employs the DSN_TCP measurement type.
As shown below, the GMAT Doppler measurement type, measured in Hz, is
derived from successive Total Phase Count (TCP) observations.
Derived "Doppler" Observation= − [ ϕ ( t 3 e ) − ϕ ( t 3 s ) ] t 3 e − t 3 s =
− [ ϕ ( t 3 e ) − ϕ ( t 3 s ) ] DCI (Hz)
where
t 3 s , t 3 e = start and end of reception interval DCI = Doppler
Count Interval in seconds ϕ = Total Count Phase (from type 17 TRK-
2-34 record)
The GMD record format for DSN_TCP tracking data is shown in the table
below.
Field Description
1 Observation receive time in TAIModJulian
2 Observation type name - DSN TRK-2-34 Type 17 Total Count
Phase = DSN_TCP
3 Observation type index number - 9006 = DSN_TCP (TRK-2-34)
4 Downlink ground station pad ID
5 Spacecraft ID
6 Uplink frequency band indicator - 0 = unknown, 1 = S-band, 2 = X-
band, 3 = Ka-band, 4 = Ku-band, 5 = L-band
7 Doppler count interval in seconds
8 Observation value - Doppler observable derived from Total Count
Phase (TCP) TRK-2-34 Type 17 measurements
A sample of GMD data records for TRK-2-34 Total Count Phase derived
Doppler data is shown below.
% - 1 - - 2 - 3 4 5 6 7 - 8 -
27226.011944444 DSN_TCP 9006 15 6241 1 10 -2.2445668331979342e+09
27226.012060185 DSN_TCP 9006 15 6241 1 10 -2.2445668330920730e+09
27226.012175926 DSN_TCP 9006 15 6241 1 10 -2.2445668329843016e+09
27226.012291667 DSN_TCP 9006 15 6241 1 10 -2.2445668328729177e+09
Transmit Frequency Ramp Records
GMAT supports DSN tracking utilizing both constant and ramped transmit
frequencies. If the transmit frequency is constant, GMAT will use the transmit
frequency specified on the DSN_SeqRange measurement records for the
computation of the range observation and a ramp table file is not required. If the
transmit frequency is ramped, the user must generate a GMD file of ramp
records from TRK-2-34 Type 9 raw data, and provide the GMD ramp table on
the TrackingFileSet.RampTable object field. If a ramp table is provided,
GMAT ignores the frequency specified on the DSN_SeqRange records and
instead computes the transmit frequency from the ramp records.
The record format for ground-based range-rate tracking data is shown in the
table below.
Field Description
1 Observation receive time in TAIModJulian
2 Ground station pad ID
3 Spacecraft ID
4 Uplink frequency band indicator - 0 = unknown, 1 = S-band, 2 = X-
band, 3 = Ka-band, 4 = Ku-band, 5 = L-band
5 Ramp type - 0 = snap, 1 = start of new ramp, 2 = medial report, 3 =
periodic report, 4 = end of ramps, 5 = ramping terminated by
operator, 6 = invalid/unknown
6 Ramp frequency in Hz
7 Ramp rate in Hz/sec
A sample GMD ramp file is shown below.
% - 1 - 2 3 4 5 - 6 - - 7 -
27238.640625000 34 234 2 1 +7.186571173393e+09 +6.010599999990e-01
27238.654513889 34 234 2 1 +7.186571894665e+09 +5.822699999990e-01
27238.659664352 34 234 2 3 +7.186572153775e+09 +5.822699999990e-01
27238.668402778 34 234 2 1 +7.186572593389e+09 +5.590199999990e-01
27238.682291667 34 234 2 1 +7.186573264213e+09 +5.315100000000e-01
Earth-fixed Position Vectors from a Spacecraft On-board
GPS Receiver
GPS-derived Earth-fixed position vectors employ the GPS_PosVec
measurement type. The fixed frame assumed for the vector components is
GMAT's EarthFixed reference frame (see CoordinateSystem). The record format
for GPS_PosVec tracking data is shown in the table below.
Field Description
1 Observation receive time in TAIModJulian
2 Observation type name - GPS_PosVec
3 Observation type index number - 9014 =
GPS_PosVec
4 GPS receiver ID
5 Earth-fixed position X component (km)
6 Earth-fixed position Y component (km)
7 Earth-fixed position Z component (km)
The GMAT user should be aware that the GPS_PosVec measurement is currently
treated as a vector quantity. The vector components are not treated as
independent observations. If any component of a vector observation (X, Y, or Z)
is edited from the solution by the user or by autonomous sigma editing, the other
components associated with that observation will also be edited out, regardless
of their quality.
A sample GMD GPS_PosVec file is shown below.
% - 1 - - 2 - 3 4 - 5 - - 6 - - 7 -
26112.586516203704 GPS_PosVec 9014 800 -3575.594419 -5758.828897 1440.891615
26112.587210648147 GPS_PosVec 9014 800 -3257.134099 -5984.420574 1265.579859
26112.587905092594 GPS_PosVec 9014 800 -2926.558570 -6187.149174 1084.793371
26112.588599537037 GPS_PosVec 9014 800 -2585.076391 -6366.230816 899.311591
26112.589293981480 GPS_PosVec 9014 800 -2233.950454 -6520.997704 709.941434
Two-Way Transponder Range
Two-way range measurements that pass through a Spacecraft transponder use the
Range measurement type. Range is a round-trip range observation measured in
kilometers. The measurement model in GMAT will include the Spacecraft
Transponder.HardwareDelay, but the HardwareDelay may be set to zero. The
GMD record format for Range data is shown in the table below.
Field Description
1 Observation receive time in TAIModJulian
2 Observation type name - Range
3
Observation type index number - 9002 = Range
4 Downlink ground station pad ID
5 Spacecraft ID
6 Two-way (round-trip) range observation in
kilometers
A sample of GMD data records for Range data is shown below.
% - 1 - - 2 - 3 4 5 - 6 -
27182.022395833334 Range 9002 117 322 +7.447171160686e+04
27182.022511574076 Range 9002 117 322 +7.447456623065e+04
27182.022627314815 Range 9002 117 322 +7.447742325277e+04
27182.022743055557 Range 9002 117 322 +7.448028087448e+04
Two-Way Range-rate
Two-way coherent range-rate tracking uses the RangeRate measurement type.
RangeRate is the difference of the range observation at the end of the averaging
interval and the start of the averaging interval, divided by the averaging interval
duration. The time tag is at the end of the averaging interval. The GMD record
format for RangeRate data is shown in the table below.
Field Description
1 Observation receive time in TAIModJulian
2 Observation type name - RangeRate
3
Observation type index number - 9012 = RangeRate
4 Downlink ground station pad ID
5 Spacecraft ID
6 Uplink frequency band indicator - 0 = unknown, 1 = S-band, 2 = X-
band, 3 = Ka-band, 4 = Ku-band, 5 = L-band
7 Doppler averaging interval in seconds
8 Range-rate observation in kilometers/second
A sample of GMD data records for RangeRate data is shown below.
% - 1 - - 2 - 3 4 5 6 7 - 8 -
23430.503148148146 RangeRate 9012 GDS LEOSat 1 10 -11.61467029
23430.503842592592 RangeRate 9012 GDS LEOSat 1 10 -11.45104085
23430.504537037035 RangeRate 9012 GDS LEOSat 1 10 -11.18499007
23430.505231481478 RangeRate 9012 GDS LEOSat 1 10 -10.76465017
