using System.Text.Json.Serialization;
using Hwinfo.SharedMemory;

namespace HwinfoMcp.Queries;

// The shapes of the tools' JSON answers. Null members are left out of the JSON, so optional parts cost
// nothing when they are empty.

/// <summary>
/// The measurement types get_readings filters by. Its own enum rather than <see cref="SensorType"/>, so the
/// tool's schema doesn't offer HWiNFO's <see cref="SensorType.None"/> and doesn't change with the reader library.
/// </summary>
public enum MeasurementType
{
	/// <summary>Temperatures.</summary>
	Temp,

	/// <summary>Voltages.</summary>
	Volt,

	/// <summary>Fan and pump speeds.</summary>
	Fan,

	/// <summary>Currents.</summary>
	Current,

	/// <summary>Power draw.</summary>
	Power,

	/// <summary>Clocks.</summary>
	Clock,

	/// <summary>Utilization and load.</summary>
	Usage,

	/// <summary>Everything else, e.g. yes/no flags, timings, framerates.</summary>
	Other,
}

/// <summary>Maps <see cref="MeasurementType"/> to the reader's <see cref="SensorType"/>.</summary>
public static class MeasurementTypes
{
	/// <summary>The <see cref="SensorType"/> of a measurement type.</summary>
	public static SensorType ToSensorType(this MeasurementType type)
	{
		return type switch
		{
			MeasurementType.Temp => SensorType.Temp,
			MeasurementType.Volt => SensorType.Volt,
			MeasurementType.Fan => SensorType.Fan,
			MeasurementType.Current => SensorType.Current,
			MeasurementType.Power => SensorType.Power,
			MeasurementType.Clock => SensorType.Clock,
			MeasurementType.Usage => SensorType.Usage,
			MeasurementType.Other => SensorType.Other,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown measurement type."),
		};
	}
}

/// <summary>How much get_cpu reports.</summary>
public enum CpuDetail
{
	/// <summary>Totals and aggregates only, no per-core or per-thread values.</summary>
	[JsonStringEnumMemberName("summary")]
	Summary,

	/// <summary>Everything, including per-core and per-thread values.</summary>
	[JsonStringEnumMemberName("full")]
	Full,
}

/// <summary>Which GPUs get_gpu reports.</summary>
public enum GpuSelection
{
	/// <summary>Discrete GPUs, falling back to integrated ones if there is none.</summary>
	[JsonStringEnumMemberName("dgpu")]
	Dgpu,

	/// <summary>Integrated GPUs, falling back to discrete ones if there is none.</summary>
	[JsonStringEnumMemberName("igpu")]
	Igpu,

	/// <summary>All GPUs.</summary>
	[JsonStringEnumMemberName("all")]
	All,
}

/// <summary>
/// The sections get_gpu leaves out unless they are asked for, as they are rarely needed. Of the power section
/// it reports the main power reading.
/// </summary>
public enum GpuSection
{
	/// <summary>All power readings rather than only the main one.</summary>
	[JsonStringEnumMemberName("power")]
	Power,

	/// <summary>Voltages.</summary>
	[JsonStringEnumMemberName("voltages")]
	Voltages,

	/// <summary>Currents.</summary>
	[JsonStringEnumMemberName("currents")]
	Currents,

	/// <summary>Limits and how much of them is used.</summary>
	[JsonStringEnumMemberName("limits")]
	Limits,

	/// <summary>Fans.</summary>
	[JsonStringEnumMemberName("fans")]
	Fans,

	/// <summary>Everything else, e.g. the PCIe link speed.</summary>
	[JsonStringEnumMemberName("other")]
	Other,

	/// <summary>All of the above.</summary>
	[JsonStringEnumMemberName("all")]
	All,
}

/// <summary>
/// The sections get_cpu leaves out unless they are asked for, as they are rarely needed. Of the power section
/// it reports the main power reading.
/// </summary>
public enum CpuSection
{
	/// <summary>All power readings rather than only the main one.</summary>
	[JsonStringEnumMemberName("power")]
	Power,

	/// <summary>Voltages.</summary>
	[JsonStringEnumMemberName("voltages")]
	Voltages,

	/// <summary>Currents, including TDC and EDC.</summary>
	[JsonStringEnumMemberName("currents")]
	Currents,

	/// <summary>Limits and how much of them is used, e.g. PPT, TDC, EDC and thermal limit.</summary>
	[JsonStringEnumMemberName("limits")]
	Limits,

	/// <summary>Everything else, e.g. core ratios and the average active core count.</summary>
	[JsonStringEnumMemberName("other")]
	Other,

	/// <summary>CPU readings of other sensor groups: mainboard CPU temperature and fan, Vcore, memory clock.</summary>
	[JsonStringEnumMemberName("related")]
	Related,

	/// <summary>All of the above.</summary>
	[JsonStringEnumMemberName("all")]
	All,
}

/// <summary>One value as the tools report it: the current one, or the statistics of a measurement.</summary>
/// <param name="Label">The label: HWiNFO's original one, or the one it displays with --user-labels.</param>
/// <param name="Value">
/// The current value, rounded to two decimals. Null if HWiNFO reports no finite number; unlike the other members
/// written as null rather than left out, so every current reading has a value. Left out for a measurement.
/// </param>
/// <param name="Unit">The unit, e.g. "°C". Null if there is none.</param>
/// <param name="Min">
/// The minimum since HWiNFO started monitoring, on request only; or the minimum over a measurement.
/// </param>
/// <param name="Max">
/// The maximum since HWiNFO started monitoring, on request only; or the maximum over a measurement.
/// </param>
/// <param name="Median">The median over a measurement.</param>
/// <param name="Avg">
/// The average since HWiNFO started monitoring, on request only; or the average over a measurement.
/// </param>
/// <param name="Samples">
/// How many snapshots of a measurement have a value of the reading, if not all of them do.
/// </param>
/// <param name="Group">
/// The sensor group the value comes from, where it isn't evident: in device overviews it is left out for
/// the main group of the device and shortened to the suffix for its sub groups (e.g. "Enhanced").
/// </param>
public sealed record ReadingValue(
	string Label,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Value,
	string? Unit,
	double? Min = null,
	double? Max = null,
	double? Median = null,
	double? Avg = null,
	int? Samples = null,
	string? Group = null)
{
	/// <summary>Whether the reading reports the statistics of a measurement. Its value is then left out of the JSON.</summary>
	[JsonIgnore]
	public bool Measured { get; init; }
}

/// <summary>The answer of hwinfo_status.</summary>
public sealed record StatusResult(
	bool Available,
	string? PollTime = null,
	double? AgeSeconds = null,
	bool? Stale = null,
	int? SensorGroups = null,
	int? Readings = null,
	string? Warning = null,
	string? Message = null);

/// <summary>The answer of a data tool when there is no data to answer with.</summary>
public sealed record UnavailableResult(bool Available, string Error);

/// <summary>The answer of list_sensors.</summary>
public sealed record SensorListResult(
	string PollTime,
	string? Warning,
	int SensorGroups,
	int Readings,
	IReadOnlyList<SensorGroupInfo> Groups);

/// <summary>One sensor group in the answer of list_sensors.</summary>
/// <param name="Name">The group name: HWiNFO's original one, or the one it displays with --user-labels.</param>
/// <param name="Category">cpu, dgpu, igpu, memory, dimm, system or other.</param>
/// <param name="Readings">The number of readings.</param>
/// <param name="Types">The number of readings per measurement type.</param>
public sealed record SensorGroupInfo(string Name, string Category, int Readings, IReadOnlyDictionary<string, int> Types);

/// <summary>The answer of get_readings.</summary>
/// <param name="Measurement">The measurement the values are of; null for current values.</param>
public sealed record ReadingsResult(
	string PollTime,
	string? Warning,
	int Total,
	int Returned,
	bool Truncated,
	string? Hint,
	IReadOnlyList<ReadingGroup> Groups,
	[property: JsonPropertyOrder(-1)] MeasurementSummary? Measurement = null);

/// <summary>The readings of one sensor group in the answer of get_readings.</summary>
public sealed record ReadingGroup(string Sensor, IReadOnlyList<ReadingValue> Readings);

/// <summary>The answer of get_cpu.</summary>
/// <param name="Related">
/// CPU related readings from other groups, e.g. the mainboard's CPU temperature and fan. On request only.
/// </param>
/// <param name="Omitted">"related" if there are related readings that weren't asked for.</param>
/// <param name="Measurement">The measurement the values are of; null for current values.</param>
public sealed record CpuResult(
	string PollTime,
	string? Warning,
	string Detail,
	string? Note,
	IReadOnlyList<DeviceReadings> Cpus,
	IReadOnlyList<ReadingValue>? Related,
	IReadOnlyList<string>? Omitted = null,
	[property: JsonPropertyOrder(-1)] MeasurementSummary? Measurement = null);

/// <summary>The answer of get_gpu.</summary>
/// <param name="Measurement">The measurement the values are of; null for current values.</param>
public sealed record GpuResult(
	string PollTime,
	string? Warning,
	string Requested,
	string? Note,
	IReadOnlyList<DeviceReadings> Gpus,
	[property: JsonPropertyOrder(-1)] MeasurementSummary? Measurement = null);

/// <summary>The answer of get_memory.</summary>
/// <param name="Timings">The readings of the memory timings group.</param>
/// <param name="Dimms">One entry per memory module.</param>
/// <param name="Usage">RAM, virtual memory and page file usage.</param>
/// <param name="Controller">Memory controller readings of the CPU groups: UCLK, FCLK, DRAM bandwidth.</param>
/// <param name="Measurement">The measurement the values are of; null for current values.</param>
public sealed record MemoryResult(
	string PollTime,
	string? Warning,
	string? Note,
	IReadOnlyList<ReadingValue>? Timings,
	IReadOnlyList<DimmReadings>? Dimms,
	IReadOnlyList<ReadingValue>? Usage,
	IReadOnlyList<ReadingValue>? Controller,
	[property: JsonPropertyOrder(-1)] MeasurementSummary? Measurement = null);

/// <summary>The readings of one memory module.</summary>
public sealed record DimmReadings(string Name, IReadOnlyList<ReadingValue> Readings);

/// <summary>
/// The overview of one CPU or GPU, its readings sorted into sections. Empty sections are left out.
/// </summary>
public sealed record DeviceReadings
{
	/// <summary>The name of the main sensor group, e.g. "CPU [#0]: AMD Ryzen 9 7900X".</summary>
	public required string Name { get; init; }

	/// <summary>dgpu or igpu, for GPUs only.</summary>
	public string? Kind { get; init; }

	/// <summary>All sensor groups the readings come from.</summary>
	public required IReadOnlyList<string> SensorGroups { get; init; }

	/// <summary>Power draw.</summary>
	public IReadOnlyList<ReadingValue>? Power { get; init; }

	/// <summary>Temperatures.</summary>
	public IReadOnlyList<ReadingValue>? Temperatures { get; init; }

	/// <summary>Utilization and load.</summary>
	public IReadOnlyList<ReadingValue>? Usage { get; init; }

	/// <summary>Memory controller load, memory and fabric clocks, DRAM bandwidth.</summary>
	public IReadOnlyList<ReadingValue>? MemoryController { get; init; }

	/// <summary>GPU memory usage.</summary>
	public IReadOnlyList<ReadingValue>? Vram { get; init; }

	/// <summary>Clocks.</summary>
	public IReadOnlyList<ReadingValue>? Clocks { get; init; }

	/// <summary>Limits and how much of them is used.</summary>
	public IReadOnlyList<ReadingValue>? Limits { get; init; }

	/// <summary>Throttling indicators and limit reasons (1 = active).</summary>
	public IReadOnlyList<ReadingValue>? Throttling { get; init; }

	/// <summary>Fans and pumps.</summary>
	public IReadOnlyList<ReadingValue>? Fans { get; init; }

	/// <summary>Voltages.</summary>
	public IReadOnlyList<ReadingValue>? Voltages { get; init; }

	/// <summary>Currents.</summary>
	public IReadOnlyList<ReadingValue>? Currents { get; init; }

	/// <summary>Everything else.</summary>
	public IReadOnlyList<ReadingValue>? Other { get; init; }

	/// <summary>
	/// Sections with readings that were left out or, like "power", shortened because they weren't asked for.
	/// </summary>
	public IReadOnlyList<string>? Omitted { get; init; }
}
