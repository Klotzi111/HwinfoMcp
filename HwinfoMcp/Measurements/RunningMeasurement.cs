namespace HwinfoMcp.Measurements;

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
