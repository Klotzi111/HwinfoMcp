using System.Collections.Immutable;
using Hwinfo.SharedMemory;
using HwinfoMcp.Sensors;

namespace HwinfoMcp.Measurements;

/// <summary>
/// What a recorded snapshot holds values for: HWiNFO's sensor groups and readings with their names and units.
/// One instance is shared by all consecutive snapshots with the same readings, so each snapshot only stores its
/// values.
/// </summary>
internal sealed class SnapshotLayout
{
	// Rough sizes of the objects behind a reading and a sensor group, including the headers of their strings
	private const int ReadingOverhead = 160;
	private const int SensorOverhead = 88;

	/// <summary>Creates the layout of a snapshot that carries data.</summary>
	public SnapshotLayout(SensorSnapshot snapshot)
	{
		Readings = snapshot.Readings;
		Sensors = snapshot.Sensors;

		long bytes = 0;
		foreach (var reading in Readings)
		{
			bytes += ReadingOverhead + (2L * (reading.LabelOrig.Length + reading.LabelUser.Length + reading.Unit.Length));
		}

		foreach (var sensor in Sensors)
		{
			bytes += SensorOverhead + (2L * (sensor.NameOrig.Length + sensor.NameUser.Length));
		}

		EstimatedBytes = bytes;
	}

	/// <summary>The readings in HWiNFO's order. A snapshot's values are in the same order.</summary>
	public ImmutableArray<SensorReading> Readings { get; }

	/// <summary>All sensor groups in HWiNFO's order, including those without readings.</summary>
	public ImmutableArray<Sensor> Sensors { get; }

	/// <summary>Roughly how much memory the layout takes.</summary>
	public long EstimatedBytes { get; }

	/// <summary>
	/// Whether a snapshot has the same sensor groups and readings in the same order, with the same names and units.
	/// </summary>
	public bool Matches(SensorSnapshot snapshot)
	{
		if (snapshot.Readings.Length != Readings.Length || snapshot.Sensors.Length != Sensors.Length)
		{
			return false;
		}

		for (var i = 0; i < Sensors.Length; i++)
		{
			if (!SameSensor(Sensors[i], snapshot.Sensors[i]))
			{
				return false;
			}
		}

		for (var i = 0; i < Readings.Length; i++)
		{
			var mine = Readings[i];
			var other = snapshot.Readings[i];
			if (mine.ReadingId != other.ReadingId
				|| mine.ReadingType != other.ReadingType
				|| !SameSensor(mine.Sensor, other.Sensor)
				|| !string.Equals(mine.LabelOrig, other.LabelOrig, StringComparison.Ordinal)
				|| !string.Equals(mine.LabelUser, other.LabelUser, StringComparison.Ordinal)
				|| !string.Equals(mine.Unit, other.Unit, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}

	private static bool SameSensor(Sensor mine, Sensor other)
	{
		// The reader keeps a sensor's instance as long as HWiNFO reports it unchanged
		return ReferenceEquals(mine, other)
			|| (mine.Id == other.Id
				&& mine.Instance == other.Instance
				&& string.Equals(mine.NameOrig, other.NameOrig, StringComparison.Ordinal)
				&& string.Equals(mine.NameUser, other.NameUser, StringComparison.Ordinal));
	}
}
