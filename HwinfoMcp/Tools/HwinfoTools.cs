using System.ComponentModel;
using Hwinfo.SharedMemory;
using HwinfoMcp.Measurements;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HwinfoMcp.Tools;

/// <summary>
/// The MCP tools that report sensor data. Each call reads the current data from HWiNFO's shared memory and
/// answers with compact JSON. Without data, the data tools answer with an error result that says how to fix it.
/// Given a measurement, the data tools report its statistics instead.
/// </summary>
/// <param name="source">Where the sensor data comes from.</param>
/// <param name="recorder">Holds the results of stopped measurements.</param>
/// <param name="timeProvider">The clock the age of the data is measured with.</param>
/// <param name="options">The server settings.</param>
[McpServerToolType]
public sealed class HwinfoTools(ISensorSource source, MeasurementRecorder recorder, TimeProvider timeProvider, HwinfoServerOptions options)
{
	private const string MeasurementDescription =
		"The name of a stopped measurement (see start_measurement): report its min, max, median and average instead "
		+ "of the current values. includeStats is then not needed.";

	/// <summary>hwinfo_status</summary>
	[McpServerTool(Name = "hwinfo_status", Title = "HWiNFO status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Checks whether HWiNFO's live sensor data is readable. Returns the time and age of HWiNFO's last poll, "
		+ "the number of sensor groups and readings, and a warning when the data is stale (e.g. the free HWiNFO "
		+ "version stops sharing after 12 hours). Use it when other tools report no data.")]
	public CallToolResult HwinfoStatus()
	{
		return ToolResults.Success(SensorQueries.GetStatus(source.Read(), Context()));
	}

	/// <summary>list_sensors</summary>
	[McpServerTool(Name = "list_sensors", Title = "List sensor groups", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Lists all HWiNFO sensor groups with their category (cpu, dgpu, igpu, memory, dimm, system, other), "
		+ "the number of readings and the number of readings per measurement type (Temp, Volt, Fan, Current, "
		+ "Power, Clock, Usage, Other). Use it to find out what get_readings can query.")]
	public CallToolResult ListSensors()
	{
		return Query(SensorQueries.ListSensors);
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
		MeasurementType[]? types = null,
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false,
		[Description("The most readings to return (1 to 2000).")]
		int limit = SensorQueries.DefaultReadingLimit,
		[Description(MeasurementDescription)]
		string? measurement = null)
	{
		SensorType[]? sensorTypes = types is null ? null : [.. types.Select(MeasurementTypes.ToSensorType)];
		return Query(
			(snapshot, context) => SensorQueries.GetReadings(snapshot, context, sensor, label, sensorTypes, includeStats, limit),
			measurement
		);
	}

	/// <summary>get_cpu</summary>
	[McpServerTool(Name = "get_cpu", Title = "CPU overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Curated CPU overview: main power ('CPU Package Power'), temperatures, usage, memory controller (UCLK, "
		+ "FCLK, DRAM bandwidth), clocks and throttling indicators (1 = active). The other power readings, voltages, "
		+ "currents (incl. TDC/EDC), limits and their utilization (PPT, TDC, EDC, thermal), other readings and "
		+ "'related' CPU readings of other groups (mainboard CPU temperature and fan, Vcore, memory clock) are only "
		+ "included on request (include); 'omitted' lists the sections that were shortened or left out. C-state "
		+ "residencies are never included. A reading's 'group' names its sensor sub group when it isn't the CPU's "
		+ "main group.")]
	public CallToolResult GetCpu(
		[Description("summary: totals and aggregates only (default). full: also per-core and per-thread values.")]
		CpuDetail detail = CpuDetail.Summary,
		[Description("Rarely needed sections to include too, e.g. [\"limits\", \"related\"], or [\"all\"]. \"power\" returns all power readings instead of only the main one. Omit for none.")]
		CpuSection[]? include = null,
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false,
		[Description(MeasurementDescription)]
		string? measurement = null)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetCpu(snapshot, context, detail, includeStats, include),
			measurement
		);
	}

	/// <summary>get_gpu</summary>
	[McpServerTool(Name = "get_gpu", Title = "GPU overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Curated GPU overview: main power (e.g. 'GPU Power' or AMD's board power), temperatures (incl. hotspot "
		+ "and memory junction), usage, memory controller load and memory clock, VRAM usage, clocks and throttling "
		+ "indicators (1 = active). The other power readings (chip, rails, connectors), voltages, currents, limits, "
		+ "fans and other readings are only included on request (include); 'omitted' lists the sections that were "
		+ "shortened or left out.")]
	public CallToolResult GetGpu(
		[Description("dgpu: discrete GPU(s) (default), igpu: integrated GPU(s), all: every GPU. Falls back to the other kind if there is none of the requested one.")]
		GpuSelection type = GpuSelection.Dgpu,
		[Description("Rarely needed sections to include too, e.g. [\"power\", \"limits\"], or [\"all\"]. \"power\" returns all power readings instead of only the main one. Omit for none.")]
		GpuSection[]? include = null,
		[Description("Also return min, max and average since HWiNFO started monitoring.")]
		bool includeStats = false,
		[Description(MeasurementDescription)]
		string? measurement = null)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetGpu(snapshot, context, type, includeStats, include),
			measurement
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
		bool includeStats = false,
		[Description(MeasurementDescription)]
		string? measurement = null)
	{
		return Query(
			(snapshot, context) => SensorQueries.GetMemory(snapshot, context, includeStats),
			measurement
		);
	}

	/// <summary>Runs a query on the current data, or on the readings of a stopped measurement if one is named.</summary>
	private CallToolResult Query<T>(Func<SensorSnapshot, QueryContext, T> query, string? measurement = null)
	{
		if (!string.IsNullOrWhiteSpace(measurement))
		{
			CompletedMeasurement completed;
			try
			{
				completed = recorder.GetCompleted(measurement);
			}
			catch (MeasurementException ex)
			{
				return ToolResults.Error(new MeasurementErrorResult(ex.Message));
			}

			return ToolResults.Success(query(completed.Readings, Context() with { Measurement = completed }));
		}

		var snapshot = source.Read();
		if (!snapshot.IsAvailable)
		{
			return ToolResults.Error(new UnavailableResult(Available: false, Error: Messages.NotAvailable(snapshot)));
		}

		return ToolResults.Success(query(snapshot, Context()));
	}

	private QueryContext Context()
	{
		return QueryContext.From(timeProvider, options.Labels);
	}
}
