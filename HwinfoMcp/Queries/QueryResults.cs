using System.Text.Json.Serialization;

namespace HwinfoMcp.Queries;

// The shapes of the tools' JSON answers. Null members are left out of the JSON, so optional parts cost
// nothing when they are empty.

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

/// <summary>One value as the tools report it.</summary>
/// <param name="Label">The label as displayed in HWiNFO.</param>
/// <param name="Value">The current value, rounded to two decimals. Null if HWiNFO reports no finite number.</param>
/// <param name="Unit">The unit, e.g. "°C". Null if there is none.</param>
/// <param name="Min">The minimum since HWiNFO started monitoring, on request only.</param>
/// <param name="Max">The maximum since HWiNFO started monitoring, on request only.</param>
/// <param name="Avg">The average since HWiNFO started monitoring, on request only.</param>
/// <param name="Group">
/// The sensor group the value comes from, where it isn't evident: in device overviews it is left out for
/// the main group of the device and shortened to the suffix for its sub groups (e.g. "Enhanced").
/// </param>
public sealed record ReadingValue(
	string Label,
	double? Value,
	string? Unit,
	double? Min = null,
	double? Max = null,
	double? Avg = null,
	string? Group = null);

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
/// <param name="Name">The group name as displayed in HWiNFO.</param>
/// <param name="Category">cpu, dgpu, igpu, memory, dimm, system or other.</param>
/// <param name="Readings">The number of readings.</param>
/// <param name="Types">The number of readings per measurement type.</param>
public sealed record SensorGroupInfo(string Name, string Category, int Readings, IReadOnlyDictionary<string, int> Types);

/// <summary>The answer of get_readings.</summary>
public sealed record ReadingsResult(
	string PollTime,
	string? Warning,
	int Total,
	int Returned,
	bool Truncated,
	string? Hint,
	IReadOnlyList<ReadingGroup> Groups);

/// <summary>The readings of one sensor group in the answer of get_readings.</summary>
public sealed record ReadingGroup(string Sensor, IReadOnlyList<ReadingValue> Readings);

/// <summary>The answer of get_cpu.</summary>
/// <param name="Related">CPU related readings from other groups, e.g. the mainboard's CPU temperature and fan.</param>
public sealed record CpuResult(
	string PollTime,
	string? Warning,
	string Detail,
	string? Note,
	IReadOnlyList<DeviceReadings> Cpus,
	IReadOnlyList<ReadingValue>? Related);

/// <summary>The answer of get_gpu.</summary>
public sealed record GpuResult(
	string PollTime,
	string? Warning,
	string Requested,
	string? Note,
	IReadOnlyList<DeviceReadings> Gpus);

/// <summary>The answer of get_memory.</summary>
/// <param name="Timings">The readings of the memory timings group.</param>
/// <param name="Dimms">One entry per memory module.</param>
/// <param name="Usage">RAM, virtual memory and page file usage.</param>
/// <param name="Controller">Memory controller readings of the CPU groups: UCLK, FCLK, DRAM bandwidth.</param>
public sealed record MemoryResult(
	string PollTime,
	string? Warning,
	string? Note,
	IReadOnlyList<ReadingValue>? Timings,
	IReadOnlyList<DimmReadings>? Dimms,
	IReadOnlyList<ReadingValue>? Usage,
	IReadOnlyList<ReadingValue>? Controller);

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
}
