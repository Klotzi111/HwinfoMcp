using Hwinfo.SharedMemory;
using HwinfoMcp.Sensors;

namespace HwinfoMcp.Tests.Fakes;

/// <summary>
/// Builds hand-made snapshots: groups are added in order, readings go to the group added last.
/// Min, max and average are derived from the value, so tests can tell them apart.
/// </summary>
internal sealed class SnapshotBuilder(DateTimeOffset pollTime)
{
	private readonly List<Sensor> _sensors = [];
	private readonly List<SensorReading> _readings = [];
	private Sensor? _group;

	public SnapshotBuilder Group(string name, string? userName = null)
	{
		_group = new Sensor((uint)_sensors.Count, 0, name, userName ?? name);
		_sensors.Add(_group);
		return this;
	}

	/// <summary>Adds the following readings to a group added before, as HWiNFO may list a group's readings apart.</summary>
	public SnapshotBuilder ContinueGroup(string name)
	{
		_group = _sensors.Single(sensor => sensor.NameOrig == name);
		return this;
	}

	public SnapshotBuilder Reading(SensorType type, string label, double value, string unit, string? userLabel = null)
	{
		var group = _group ?? throw new InvalidOperationException("Add a group first.");
		_readings.Add(new SensorReading(
			(uint)_readings.Count, type, label, userLabel ?? label, unit, value, value * 0.5, value * 2, value * 0.75, group
		));
		return this;
	}

	public SnapshotBuilder Temp(string label, double value) => Reading(SensorType.Temp, label, value, "°C");

	public SnapshotBuilder Volt(string label, double value) => Reading(SensorType.Volt, label, value, "V");

	public SnapshotBuilder Fan(string label, double value) => Reading(SensorType.Fan, label, value, "RPM");

	public SnapshotBuilder Current(string label, double value) => Reading(SensorType.Current, label, value, "A");

	public SnapshotBuilder Power(string label, double value) => Reading(SensorType.Power, label, value, "W");

	public SnapshotBuilder Clock(string label, double value) => Reading(SensorType.Clock, label, value, "MHz");

	public SnapshotBuilder Usage(string label, double value) => Reading(SensorType.Usage, label, value, "%");

	public SnapshotBuilder Other(string label, double value, string unit) => Reading(SensorType.Other, label, value, unit);

	public SnapshotBuilder YesNo(string label, bool value) => Reading(SensorType.Other, label, value ? 1 : 0, "Yes/No");

	public SensorSnapshot Build()
	{
		return new SensorSnapshot(SnapshotState.Available, pollTime, [.. _readings], [.. _sensors]);
	}
}
