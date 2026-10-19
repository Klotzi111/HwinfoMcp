using Hwinfo.SharedMemory;
using HwinfoMcp.Catalog;
using HwinfoMcp.Sensors;

namespace HwinfoMcp.Measurements;

/// <summary>Why a period of a measurement has no snapshots.</summary>
public enum GapReason
{
	/// <summary>The memory limit was reached, so HWiNFO's polls weren't recorded.</summary>
	MemoryLimit,

	/// <summary>HWiNFO delivered no new data for longer than <see cref="SensorPatterns.StaleAfter"/>.</summary>
	NoData,
}

/// <summary>A period of a measurement without snapshots.</summary>
/// <param name="From">The last snapshot before the gap, or the start of the measurement.</param>
/// <param name="To">The first snapshot after the gap, or the stop of the measurement.</param>
/// <param name="Reason">Why there are no snapshots.</param>
public readonly record struct UnmeasuredPeriod(DateTimeOffset From, DateTimeOffset To, GapReason Reason)
{
	/// <summary>How long the gap is.</summary>
	public TimeSpan Duration => To - From;
}

/// <summary>A measurement that is running: where in the recorded timeline it starts, and where polls were dropped.</summary>
/// <param name="name">The name, as given.</param>
/// <param name="start">When it started.</param>
/// <param name="firstSequence">The sequence number of its first snapshot.</param>
internal sealed class RunningMeasurement(string name, DateTimeOffset start, long firstSequence)
{
	/// <summary>The name, as given.</summary>
	public string Name { get; } = name;

	/// <summary>When it started.</summary>
	public DateTimeOffset Start { get; } = start;

	/// <summary>The sequence number of its first snapshot: every snapshot from there on belongs to it.</summary>
	public long FirstSequence { get; set; } = firstSequence;

	/// <summary>
	/// For every run of polls dropped because of the memory limit: the sequence number of the snapshot recorded
	/// last before it.
	/// </summary>
	public List<long> DroppedAfter { get; } = [];

	/// <summary>Whether the last poll was dropped, i.e. a run of dropped polls is going on.</summary>
	public bool Dropping { get; set; }
}

/// <summary>
/// The result of a stopped measurement: min, max, median and average of every reading, and the periods it has
/// no snapshots for.
/// </summary>
public sealed class CompletedMeasurement
{
	private readonly Dictionary<SensorReading, ReadingStats> _stats;

	private CompletedMeasurement(
		RunningMeasurement measurement,
		DateTimeOffset stop,
		IReadOnlyList<StoredSnapshot> snapshots,
		MeasuredReadings measured,
		IReadOnlyList<UnmeasuredPeriod> gaps,
		long memoryLimit)
	{
		Name = measurement.Name;
		Start = measurement.Start;
		Stop = stop;
		Snapshots = snapshots.Count;
		FirstSnapshot = snapshots.Count > 0 ? snapshots[0].PollTime : null;
		LastSnapshot = snapshots.Count > 0 ? snapshots[^1].PollTime : null;
		Gaps = gaps;
		MemoryLimit = memoryLimit;

		// The readings carry their statistics, so they are told apart by them too
		var readings = new SensorReading[measured.Readings.Length];
		_stats = new Dictionary<SensorReading, ReadingStats>(readings.Length);
		for (var i = 0; i < readings.Length; i++)
		{
			var stats = measured.Stats[i];
			readings[i] = measured.Readings[i] with
			{
				Value = stats.Median,
				ValueMin = stats.Min,
				ValueMax = stats.Max,
				ValueAvg = stats.Avg,
			};
			_stats[readings[i]] = stats;
		}

		Readings = new SensorSnapshot(SnapshotState.Available, LastSnapshot ?? stop, [.. readings], measured.Sensors);
	}

	/// <summary>The name, as given at the start.</summary>
	public string Name { get; }

	/// <summary>When the measurement started.</summary>
	public DateTimeOffset Start { get; }

	/// <summary>When the measurement stopped.</summary>
	public DateTimeOffset Stop { get; }

	/// <summary>How long the measurement ran.</summary>
	public TimeSpan Duration => Stop - Start;

	/// <summary>How many snapshots the measurement has.</summary>
	public int Snapshots { get; }

	/// <summary>When HWiNFO polled the first snapshot. Null without snapshots.</summary>
	public DateTimeOffset? FirstSnapshot { get; }

	/// <summary>When HWiNFO polled the last snapshot. Null without snapshots.</summary>
	public DateTimeOffset? LastSnapshot { get; }

	/// <summary>The periods without snapshots, in order.</summary>
	public IReadOnlyList<UnmeasuredPeriod> Gaps { get; }

	/// <summary>The memory limit the measurement was recorded with.</summary>
	public long MemoryLimit { get; }

	/// <summary>
	/// The readings of the measurement as a snapshot, so the queries can sort them like live data: the median as
	/// value, and min, max and average of the measurement. <see cref="StatsOf"/> has the number of samples too.
	/// </summary>
	public SensorSnapshot Readings { get; }

	/// <summary>The statistics of one of the <see cref="Readings"/>.</summary>
	public ReadingStats StatsOf(SensorReading reading)
	{
		return _stats.GetValueOrDefault(reading, ReadingStats.None);
	}

	/// <summary>Evaluates a measurement that just stopped.</summary>
	/// <param name="measurement">The measurement.</param>
	/// <param name="stop">When it stopped.</param>
	/// <param name="snapshots">Its snapshots, oldest first.</param>
	/// <param name="memoryLimit">The memory limit it was recorded with.</param>
	internal static CompletedMeasurement Evaluate(
		RunningMeasurement measurement,
		DateTimeOffset stop,
		IReadOnlyList<StoredSnapshot> snapshots,
		long memoryLimit)
	{
		var measured = MeasurementStatistics.Evaluate(snapshots);
		return new CompletedMeasurement(measurement, stop, snapshots, measured, FindGaps(measurement, stop, snapshots), memoryLimit);
	}

	/// <summary>
	/// Finds the periods without snapshots: those in which polls were dropped because of the memory limit, and
	/// those in which HWiNFO delivered no new data for longer than <see cref="SensorPatterns.StaleAfter"/>. A
	/// period runs from the last snapshot before it (or the start) to the next one (or the stop).
	/// </summary>
	private static List<UnmeasuredPeriod> FindGaps(RunningMeasurement measurement, DateTimeOffset stop, IReadOnlyList<StoredSnapshot> snapshots)
	{
		// The periods between snapshots, numbered by the snapshot that ends them; the last one ends with the stop
		var droppedBefore = measurement.DroppedAfter
			.Select(sequence => sequence - measurement.FirstSequence + 1)
			.ToHashSet();
		var gaps = new List<UnmeasuredPeriod>();
		var from = measurement.Start;
		for (var i = 0; i <= snapshots.Count; i++)
		{
			// The snapshot current at the start was polled before it, so it opens no gap
			var to = i < snapshots.Count ? snapshots[i].PollTime : stop;
			if (to > from)
			{
				if (droppedBefore.Contains(i))
				{
					gaps.Add(new UnmeasuredPeriod(from, to, GapReason.MemoryLimit));
				}
				else if (to - from > SensorPatterns.StaleAfter)
				{
					gaps.Add(new UnmeasuredPeriod(from, to, GapReason.NoData));
				}

				from = to;
			}
		}

		return gaps;
	}
}
