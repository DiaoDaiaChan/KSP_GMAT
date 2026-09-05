# AcceptFilter

> 来源：第 335-348 页

AcceptFilter
AcceptFilter — Allows selection of data subsets for processing by the batch least
squares estimator.
Description
Starting with the R2017A release of GMAT, the AcceptFilter resource replaces
the StatisticsAcceptFilter resource. The StatisticsAcceptFilter resource is still
available in this release but it is deprecated and will be removed in a future
release.
The AcceptFilter object is used to create criteria for the inclusion of subsets of
the available data in the estimation process based on observation frequency,
tracker, measurement type, record number, or time. Instances of AcceptFilter
are specified for use on the DataFilters field of a TrackingFileSet or
BatchEstimatorInv object.
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
A single AcceptFilter may employ multiple selection criteria (for example
simultaneously thinning different stations or data types by differing intervals).
Multiple criteria on a single filter are considered in an AND sense. When
multiple criteria are specified on a single filter, an observation must meet all
specified criteria to be accepted.
Multiple AcceptFilters with different selection criteria may be specified on a
single TrackingFileSet or BatchEstimatorInv. When multiple filters are
specified, these act in an OR sense. Data meeting criteria for any of the specified
filters will be accepted.
See Also RejectFilter, TrackingFileSet, BatchEstimatorInv
Fields
Field Description
DataTypes
List of data types
Data Type String Array
Allowed A set of any supported GMAT
Values measurement types, or 'All'
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
Values TAIGregorian, TAIModJulian,
TTGregorian, TTModJulian A1Gregorian,
A1ModJulian, TDBGregorian,
TDBModJulian
Access set
Default TAIModJulian
Value
Units N/A
Interfaces script
FileNames
List of file names (a subset of the relevant
TrackingFileSet's FileName field) containing the
tracking data. If this field equals
From_AddTrackingConfig, then two things happen; (1)
All of the files in the relevant TrackingFileSet are used
as a starting point, and (2) Of the data in all of the files,
only the data defined by the AddTrackingConfig field
of the relevant TrackingFileSet are used. This field is
only applicable when the AcceptFilter is used on a
TrackingFileSet.
Data Type StringArray
Allowed valid file name, 'All', or
Values 'From_AddTrackingConfig'
Access set
Default {All}
Value
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
record numbers to accept. Observation record numbers
are reported in the GMAT estimator output file. This
field is only applicable when the AcceptFilter is used on
the estimator level.
Data Type String array
Allowed Integers or spans of integers (see
Values examples)
Access set
Default Value {All}
Units N/A
Interfaces script
ThinMode
'Frequency' for record count frequency mode and 'Time'
for time interval mode. This field is only applicable
when the AcceptFilter is used on a TrackingFileSet.
Data Type String
Allowed Values 'Frequency' or 'Time'
Access set
Default Value Frequency
Units N/A
Interfaces script
ThinningFrequency
If ThinMode is Frequency, the integer 'n' is used to
specify that every nth data point should be accepted. For
example, 3 specifies that every third data point, meeting
all the accept criteria, should be accepted and 1 specifies
that every data point, meeting all the accept criteria,
should be accepted. If ThinMode is Time, the integer 'n'
is a number of seconds between accepted observations,
using the first available observation as the anchor epoch.
For example, a value of 300 means that observations will
be accepted every 300 seconds, starting from the first
available observation. This field is only applicable when
the AcceptFilter is used on a TrackingFileSet.
Data Type Integer
Allowed Values Positive Integer
Access set
Default Value 1
Units Depends on ThinMode value
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
Some fields of AcceptFilter are not applicable at either the first-level (tracking
file set) or second-level (estimator) editing stages. The RecordNumbers field
has no functionality when applied to an accept filter at the tracking file set level.
The FileNames, ThinningFrequency, and ThinMode fields have no
functionality when applied to an accept filter at the estimator level.
Use of combinations of instances of AcceptFilter and RejectFilter at both
levels is permitted.
Examples
First-level (TrackingFileSet) Data Editing
The following examples illustrate use of an AcceptFilter for first-level data
editing. At this level, the AcceptFilter instance should be assigned to the
DataFilters field of a TrackingFileSet. In these examples, only data meeting
the criteria specified by the accept filter will be admitted through. All other data
is immediately discarded.
This example shows how to create an AcceptFilter to sample the data at a
frequency of 1:10 (thinning the data to one tenth of its volume).
Create AcceptFilter af;
af.ThinningFrequency = 10;
Create TrackingFileSet estData;
estData.DataFilters = {af};
BeginMissionSequence;
The next example will accept all data from station GDS and accept every 5th
observation from station CAN. Only data from stations GDS and CAN will be
accepted.
Create AcceptFilter af1;
Create AcceptFilter af2;
Create GroundStation GDS CAN;
af1.Trackers = {'GDS'};
af2.Trackers = {'CAN'};
af2.ThinningFrequency = 5;
Create TrackingFileSet estData;
estData.DataFilters = {af1, af2};
BeginMissionSequence;
The last example illustrates thinning data by time interval, using a 300-second
thinning interval.
Create AcceptFilter saf;
af.ThinMode = 'Time';
af.ThinningFrequency = 300;
Create TrackingFileSet estData;
estData.DataFilters = {af};
BeginMissionSequence;
Second-level (estimator) Data Editing
The following examples illustrate use of an AcceptFilter for second-level data
editing. At this level, the AcceptFilter instance should be assigned to the
DataFilters field of a BatchEstimatorInv. In these examples, only data meeting
the criteria specified by the accept filter will be used in the estimation state
update. Residuals will be computed for all available data (all data admitted at the
first level), but data not accepted at the estimator level will be flagged as user
edited.
This example shows how to create an AcceptFilter to accept specific data
records by record number.
Create AcceptFilter af;
af.RecordNumbers = {10, 11, 20-150, 155-300};
Create BatchEstimatorInv bls;
bls.DataFilters = {af};
BeginMissionSequence;
The next example will accept only range data from station MAD over the time
span 10 Jun 2012 02:56 to 13:59.
Create AcceptFilter af;
Create GroundStation MAD;
af.Trackers = {'MAD'};
af.DataTypes = {'Range'};
af.EpochFormat = UTCGregorian;
af.InitialEpoch = '10 Jun 2012 02:56:00.000';
af.FinalEpoch = '10 Jun 2012 13:59:00.000';
Create BatchEstimatorInv bls;
bls.DataFilters = {af};
BeginMissionSequence;
The last example illustrates accepting all data from station MAD and only range
data from station CAN.
Create AcceptFilter af1 af2;
Create GroundStation MAD CAN;
af1.Trackers = {'MAD'};
af2.Trackers = {'CAN'};
af2.DataTypes = {'Range'};
Create BatchEstimatorInv bls;
bls.DataFilters = {af1, af2};
BeginMissionSequence;
