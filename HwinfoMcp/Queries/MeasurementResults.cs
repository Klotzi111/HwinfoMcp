namespace HwinfoMcp.Queries;

// The shapes of the measurement tools' JSON answers. Null members are left out of the JSON.

/// <summary>The answer of start_measurement.</summary>
/// <param name="Name">The name of the measurement.</param>
/// <param name="Start">When it started.</param>
/// <param name="Running">The names of all running measurements, this one included.</param>
/// <param name="MemoryLimit">The memory limit for the recorded snapshots of all measurements, e.g. "1 GB".</param>
/// <param name="Warning">Why the measurement has no data yet, if so.</param>
public sealed record StartMeasurementResult(
	string Name,
	string Start,
	IReadOnlyList<string> Running,
	string MemoryLimit,
	string? Warning);

/// <summary>
/// A stopped measurement: the answer of stop_measurement, and part of every answer that reports its values.
/// </summary>
/// <param name="Name">The name of the measurement.</param>
/// <param name="Start">When it started.</param>
/// <param name="Stop">When it stopped.</param>
/// <param name="DurationSeconds">How long it ran.</param>
/// <param name="Snapshots">How many of HWiNFO's polls it recorded.</param>
/// <param name="FirstSnapshot">When HWiNFO polled the first snapshot.</param>
/// <param name="LastSnapshot">When HWiNFO polled the last snapshot.</param>
/// <param name="UnmeasuredSeconds">How long it had no snapshots in total, if there are gaps.</param>
/// <param name="Gaps">The periods without snapshots.</param>
/// <param name="Warning">What the values don't cover.</param>
/// <param name="Hint">How to query the values; in the answer of stop_measurement only.</param>
public sealed record MeasurementSummary(
	string Name,
	string Start,
	string Stop,
	double DurationSeconds,
	int Snapshots,
	string? FirstSnapshot,
	string? LastSnapshot,
	double? UnmeasuredSeconds,
	IReadOnlyList<MeasurementGap>? Gaps,
	string? Warning,
	string? Hint = null);

/// <summary>A period of a measurement without snapshots.</summary>
/// <param name="From">The last snapshot before the gap, or the start.</param>
/// <param name="To">The next snapshot after the gap, or the stop.</param>
/// <param name="Seconds">How long the gap is.</param>
/// <param name="Reason">memoryLimit: the memory limit was reached. noData: HWiNFO delivered no new data.</param>
public sealed record MeasurementGap(string From, string To, double Seconds, string Reason);

/// <summary>The answer of list_measurements.</summary>
/// <param name="Running">The running measurements, oldest first.</param>
/// <param name="Stopped">The stopped measurements whose values can be queried, oldest first.</param>
/// <param name="Memory">How much memory the recorded snapshots take.</param>
/// <param name="Warning">Says when the memory limit is reached.</param>
public sealed record MeasurementListResult(
	IReadOnlyList<RunningMeasurementEntry> Running,
	IReadOnlyList<MeasurementSummary> Stopped,
	MeasurementMemory Memory,
	string? Warning);

/// <summary>A running measurement in the answer of list_measurements.</summary>
public sealed record RunningMeasurementEntry(string Name, string Start, double DurationSeconds, int Snapshots);

/// <summary>The memory the recorded snapshots take, in the answer of list_measurements.</summary>
/// <param name="Used">How much memory the recorded snapshots of the running measurements take, e.g. "12.5 MB".</param>
/// <param name="Limit">The most memory they may take, e.g. "1 GB".</param>
/// <param name="Snapshots">How many snapshots are recorded.</param>
/// <param name="LimitReached">Whether new polls are dropped because the limit is reached.</param>
public sealed record MeasurementMemory(string Used, string Limit, int Snapshots, bool LimitReached);

/// <summary>The answer when a measurement can't be started, stopped or queried.</summary>
public sealed record MeasurementErrorResult(string Error);
