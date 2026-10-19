using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hwinfo.SharedMemory;
using HwinfoMcp.Json;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HwinfoMcp.Tools;

/// <summary>
/// The MCP tools. Each call reads the current data from HWiNFO's shared memory and answers with compact
/// JSON. Without data, the data tools answer with an error result that says how to fix it.
/// </summary>
/// <param name="source">Where the sensor data comes from.</param>
/// <param name="timeProvider">The clock the age of the data is measured with.</param>
/// <param name="options">The server settings.</param>
[McpServerToolType]
public sealed class HwinfoTools(ISensorSource source, TimeProvider timeProvider, HwinfoServerOptions options)
{
	/// <summary>hwinfo_status</summary>
	[McpServerTool(Name = "hwinfo_status", Title = "HWiNFO status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Checks whether HWiNFO's live sensor data is readable. Returns the time and age of HWiNFO's last poll, "
		+ "the number of sensor groups and readings, and a warning when the data is stale (e.g. the free HWiNFO "
		+ "version stops sharing after 12 hours). Use it when other tools report no data.")]
	public CallToolResult HwinfoStatus()
	{
		var result = SensorQueries.GetStatus(source.Read(), Context());
		return Success(result, HwinfoJson.Output.StatusResult);
	}

	/// <summary>list_sensors</summary>
	[McpServerTool(Name = "list_sensors", Title = "List sensor groups", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Lists all HWiNFO sensor groups with their category (cpu, dgpu, igpu, memory, dimm, system, other), "
		+ "the number of readings and the number of readings per measurement type (Temp, Volt, Fan, Current, "
		+ "Power, Clock, Usage, Other). Use it to find out what get_readings can query.")]
	public CallToolResult ListSensors()
	{
		return Query(SensorQueries.ListSensors, HwinfoJson.Output.SensorListResult);
	}

	/// <summary>get_readings</summary>
	[McpServerTool(Name = "get_readings", Title = "Query readings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Queries any HWiNFO readings, grouped by sensor group: label, value (rounded to 2 decimals) and unit. "
		+ "All filters are optional and combined. The answer is limited and says when it was cut short.")]
	public CallToolResult GetReadings(
		[Description("Part of the sensor group name as listed by list_sensors, case-insensitive, e.g. \"Enhanced\", \"GPU [#0]\", \"DIMM\", \"S.M.A.R.T.\".")]
		string? sensor = null,
		[Description("Part of the reading label, case-insensitive, e.g. \"Power\", \"Hot Spot\", \"Fan\". Matches HWiNFO's original and its displayed (translated) labels.")]
		string? label = null,
		[Description("Measurement types to include, e.g. [\"Temp\", \"Power\", \"Usage\"]. Omit for all types.")]
		SensorType[]? types = null,
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false,
		[Description("The most readings to return (1 to 2000).")]
		int limit = SensorQueries.DefaultReadingLimit)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetReadings(snapshot, context, sensor, label, types, includeStats, limit),
			HwinfoJson.Output.ReadingsResult
		);
	}

	/// <summary>get_cpu</summary>
	[McpServerTool(Name = "get_cpu", Title = "CPU overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Curated CPU overview: power, temperatures, usage, memory controller (UCLK, FCLK, DRAM bandwidth), "
		+ "clocks, limits and their utilization, throttling indicators (1 = active), voltages and currents. "
		+ "C-state residencies are left out. 'related' holds CPU readings of other groups such as the mainboard's "
		+ "CPU temperature, CPU fan and memory clock. A reading's 'group' names its sensor sub group when it isn't "
		+ "the CPU's main group.")]
	public CallToolResult GetCpu(
		[Description("summary: totals and aggregates only (default). full: also per-core and per-thread values.")]
		CpuDetail detail = CpuDetail.Summary,
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetCpu(snapshot, context, detail, includeStats),
			HwinfoJson.Output.CpuResult
		);
	}

	/// <summary>get_gpu</summary>
	[McpServerTool(Name = "get_gpu", Title = "GPU overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Curated GPU overview: board and chip power, temperatures (incl. hotspot and memory junction), usage, "
		+ "memory controller load and memory clock, VRAM usage, clocks, limits, throttling indicators "
		+ "(1 = active), fans, voltages and currents.")]
	public CallToolResult GetGpu(
		[Description("dgpu: discrete GPU(s) (default), igpu: integrated GPU(s), all: every GPU. Falls back to the other kind if there is none of the requested one.")]
		GpuSelection type = GpuSelection.Dgpu,
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetGpu(snapshot, context, type, includeStats),
			HwinfoJson.Output.GpuResult
		);
	}

	/// <summary>get_memory</summary>
	[McpServerTool(Name = "get_memory", Title = "Memory overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Memory overview: memory clock and timings (Tcas, Trcd, Trp, Tras, Trc, Trfc, command rate), every "
		+ "memory module (DIMM temperatures, voltages, power), RAM and page file usage, and the CPU's memory "
		+ "controller readings (UCLK, FCLK, DRAM bandwidth).")]
	public CallToolResult GetMemory(
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetMemory(snapshot, context, includeStats),
			HwinfoJson.Output.MemoryResult
		);
	}

	private CallToolResult Query<T>(Func<SensorSnapshot, QueryContext, T> query, JsonTypeInfo<T> typeInfo)
	{
		var snapshot = source.Read();
		if (!snapshot.IsAvailable)
		{
			var error = new UnavailableResult(Available: false, Error: Messages.NotAvailable(snapshot));
			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(error, HwinfoJson.Output.UnavailableResult) }],
			};
		}

		return Success(query(snapshot, Context()), typeInfo);
	}

	private QueryContext Context()
	{
		return QueryContext.From(timeProvider, options.Labels);
	}

	private static CallToolResult Success<T>(T result, JsonTypeInfo<T> typeInfo)
	{
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, typeInfo) }],
		};
	}
}
