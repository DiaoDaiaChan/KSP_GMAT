# RejectFilter

> 来源：第 894-905 页

RejectFilter
RejectFilter — Allows selection of data subsets for processing by the batch least
squares estimator.
Description
Starting with release R2017A of GMAT, the RejectFilter resource replaces the
StatisticsRejectFilter resource. The StatisticsRejectFilter resource is still
available in this release but it is deprecated and will be removed in a future
release.
The RejectFilter object is used to create criteria for the exclusion of subsets of
the available data in the estimation process based on tracker, observed object,
measurement type, or time. Instances of RejectFilter are specified for use on the
DataFilters field of a TrackingFileSet or BatchEstimatorInv object.
GMAT implements two levels of data editing for estimation. First-level editing
criteria are specified on the DataFilters field of the TrackingFileSet instance.
At this level, the user may choose what data is admitted into the overall pool of
observations provided to the estimator. Any data excluded at the tracking file set
level will be immediately discarded and not available to the estimation process.
Second-level data editing is specified on the DataFilters field of the
BatchEstimatorInv instance. At this level, the user may choose what data is
used in the estimation state update. Residuals will be computed for any
observations admitted through first-level editing, but any data excluded at the
estimator level will be flagged as user edited, and will not affect the computation
of the state correction. This allows the user to evaluate the quality of untrusted
data against a solution computed using a trusted set of measurements.
A single reject filter may employ multiple selection criteria (for example
simultaneous thinning by time and tracker). Multiple criteria on a single filter are
considered in an AND sense. When multiple criteria are specified in a single
filter, an observation must meet all specified criteria to be rejected. Multiple
filters with different selection criteria may be specified on a single
TrackingFileSet or BatchEstimatorInv. When multiple filters are specified,
these act in an OR sense. Data meeting criteria for any of the specified filters
will be rejected.
See Also AcceptFilter, TrackingFileSet, BatchEstimatorInv
Fields
Field Description
DataTypes
List of data types
Data Type String Array
Allowed A set of any supported GMAT measurement
Values types, or 'All'
Access set
Default {All}
Value
Units N/A
Interfaces script
EpochFormat
Allows user to select format of the epoch
Data String
Type
Allowed UTCGregorian, UTCModJulian,
Values TAIGregorian, TAIModJulian, TTGregorian,
TTModJulian A1Gregorian, A1ModJulian,
TDBGregorian, TDBModJulian
Access set
Default TAIModJulian
Value
Units N/A
Interfaces script
FileNames
List of file names (a subset of the relevant
TrackingFileSet's FileName field) containing the tracking
data, to be excluded from processing. This field is only
applicable when the RejectFilter is used on a
TrackingFileSet.
Data Type StringArray
Allowed Values valid file name or 'All'
Access set
Default Value {All}
Units N/A
Interfaces script
FinalEpoch
Final epoch of desired data to process
Data Type String
Allowed Values any valid epoch
Access set
Default Value latest day defined in GMAT
Units N/A
Interfaces script
InitialEpoch
Initial epoch of desired data to process
Data Type String
Allowed Values any valid epoch
Access set
Default Value earliest day defined in GMAT
Units N/A
Interfaces script
ObservedObjects
List of user-created tracked objects (e.g., name of the
Spacecraft resource being tracked)
Data Type Object Array
Allowed Values User defined observed object or 'All'
Access set
Default Value {All}
Units N/A
Interfaces script
RecordNumbers
A list of one or more single record numbers or spans of
record numbers to reject. Observation record numbers are
reported in the GMAT estimator output file. This field is
only applicable when the RejectFilter is used on the
estimator level.
Data Type String array
Allowed Integers or spans of integers (see
Values examples)
Access set
Default Value {}
Units N/A
Interfaces script
Trackers
List of user-created trackers (e.g., name of the
GroundStation resource being used)
Data Object Array
Type
Allowed any valid user-created Tracker object (e.g.,
Values GroundStation) or 'All'
Access set
Default {All}
Value
Units N/A
Interfaces script
Remarks
Some fields of RejectFilter are not applicable at either the first-level (tracking
file set) or second-level (estimator) editing stages. The RecordNumbers field
has no functionality when applied to a reject filter at the tracking file set level.
The FileNames field has no functionality when applied to a reject filter at the
estimator level.
Use of combinations of instances AcceptFilter and RejectFilter at both levels is
permitted.
Examples
First-level (TrackingFileSet) Data Editing
The following examples illustrate use of a RejectFilter for first-level data
editing. At this level, the RejectFilter instance should be assigned to the
DataFilters field of a TrackingFileSet. In these examples, data meeting the
criteria specified by the reject filter will be immediately discarded. All other data
is admitted.
This example shows how to create a RejectFilter to reject all observations from
station GDS.
Create GroundStation GDS;
Create RejectFilter rf;
rf.Trackers = {'GDS'};
Create TrackingFileSet estData;
estData.DataFilters = {rf};
BeginMissionSequence;
The next example will reject all DSN Doppler (i.e., DSN_TCP) tracking
measurements from station GDS, and all tracking of any type from station CAN.
All other tracking measurements will be accepted.
Create GroundStation GDS CAN;
Create RejectFilter rf1;
Create RejectFilter rf2;
rf1.Trackers = {'GDS'};
rf1.DataTypes = {'DSN_TCP'};
rf2.Trackers = {'CAN'};
Create TrackingFileSet estData;
estData.DataFilters = {rf1, rf2};
BeginMissionSequence;
Second-level (estimator) Data Editing
The following examples illustrate use of a RejectFilter for second-level data
editing. At this level, the RejectFilter instance should be assigned to the
DataFilters field of a BatchEstimatorInv. In these examples, data meeting the
criteria specified by the reject filter will excluded from the estimation state
update. Residuals will be computed for all available data (all data admitted at the
first level), but data rejected at the estimator level will be flagged as user edited.
This example shows how to create a RejectFilter to reject specific observations
by record number.
Create RejectFilter rf;
rf.RecordNumbers = {13, 25, 75-87};
Create BatchEstimatorInv bls;
bls.DataFilters = {rf};
BeginMissionSequence;
The next example shows how to simultaneously employ multiple reject filters. In
this example:
MAD range data over the span 10 Jun 2012 02:56 to 13:59 is rejected
All CAN DSN_TCP data is rejected
All RangeRate data (from any station) is rejected
Create RejectFilter rf1 rf2 rf3;
Create GroundStation MAD CAN;
rf1.Trackers = {'MAD'};
rf1.DataTypes = {'Range'};
rf1.EpochFormat = UTCGregorian;
rf1.InitialEpoch = '10 Jun 2012 02:56:00.000';
rf1.FinalEpoch = '10 Jun 2012 13:59:00.000';
rf2.Trackers = {'CAN'};
rf2.DataTypes = {'DSN_TCP'};
rf3.DataTypes = {'RangeRate'};
Create BatchEstimatorInv bls;
bls.DataFilters = {rf1, rf2, rf3};
BeginMissionSequence;
