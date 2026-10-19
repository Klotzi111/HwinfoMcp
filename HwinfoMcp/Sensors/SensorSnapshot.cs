using System.Collections.Immutable;
using Hwinfo.SharedMemory;

namespace HwinfoMcp.Sensors;

/// <summary>
/// Whether a <see cref="SensorSnapshot"/> carries sensor data.
/// </summary>
public enum SnapshotState
{
	/// <summary>The shared memory was read successfully.</summary>
	Available,

	/// <summary>HWiNFO isn't running or doesn't publish its shared memory.</summary>
	Unavailable,

	/// <summary>The shared memory exists but could not be read.</summary>
	Failed,
}

/// <summary>
/// The result of one read of HWiNFO's sensor data.
/// </summary>
/// <param name="State">Whether the read produced data.</param>
/// <param name="PollTime">When HWiNFO last polled its sensors (whole seconds). Default if there is no data.</param>
/// <param name="Readings">All readings in HWiNFO's order. Empty if there is no data.</param>
/// <param name="Sensors">All sensor groups in HWiNFO's order, including those without readings.</param>
/// <param name="Error">What went wrong, if <paramref name="State"/> is <see cref="SnapshotState.Failed"/>.</param>
public sealed record SensorSnapshot(
	SnapshotState State,
	DateTimeOffset PollTime,
	ImmutableArray<SensorReading> Readings,
	ImmutableArray<Sensor> Sensors,
	string? Error = null)
{
	/// <summary>A snapshot for "HWiNFO isn't publishing anything".</summary>
	public static SensorSnapshot Unavailable { get; } = new(SnapshotState.Unavailable, default, [], []);

	/// <summary>Whether this snapshot carries sensor data.</summary>
	public bool IsAvailable => State == SnapshotState.Available;

	/// <summary>Creates a snapshot from the result of a successful read.</summary>
	public static SensorSnapshot FromReadings(SensorReadings readings)
	{
		return new SensorSnapshot(SnapshotState.Available, readings.PollTime, readings.Readings, readings.Sensors);
	}

	/// <summary>Creates a snapshot for a read that failed.</summary>
	public static SensorSnapshot Failed(string error)
	{
		return new SensorSnapshot(SnapshotState.Failed, default, [], [], error);
	}
}
