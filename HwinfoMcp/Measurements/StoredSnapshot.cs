namespace HwinfoMcp.Measurements;

/// <summary>
/// One poll of HWiNFO recorded for the running measurements: its time and values. Names and units are in the
/// shared <see cref="SnapshotLayout"/>.
/// </summary>
/// <param name="sequence">The number of the snapshot: one more than that of the snapshot recorded before.</param>
/// <param name="pollTime">When HWiNFO polled the values.</param>
/// <param name="layout">What the values are of.</param>
/// <param name="values">The values, in the order of <see cref="SnapshotLayout.Readings"/>.</param>
internal sealed class StoredSnapshot(long sequence, DateTimeOffset pollTime, SnapshotLayout layout, double[] values)
{
	/// <summary>Roughly how much memory a snapshot takes besides its values: the object and the array header.</summary>
	public const int Overhead = 80;

	/// <summary>The number of the snapshot: one more than that of the snapshot recorded before.</summary>
	public long Sequence { get; } = sequence;

	/// <summary>When HWiNFO polled the values.</summary>
	public DateTimeOffset PollTime { get; } = pollTime;

	/// <summary>What the values are of.</summary>
	public SnapshotLayout Layout { get; } = layout;

	/// <summary>The values, in the order of <see cref="SnapshotLayout.Readings"/>.</summary>
	public double[] Values { get; } = values;

	/// <summary>Roughly how much memory the snapshot takes, without its shared layout.</summary>
	public long Bytes => Size(Values.Length);

	/// <summary>Roughly how much memory a snapshot with that many values takes, without its shared layout.</summary>
	public static long Size(int values)
	{
		return Overhead + (8L * values);
	}
}
