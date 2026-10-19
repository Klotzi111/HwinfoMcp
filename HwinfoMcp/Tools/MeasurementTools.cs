using System.ComponentModel;
using HwinfoMcp.Measurements;
using HwinfoMcp.Queries;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HwinfoMcp.Tools;

/// <summary>
/// The MCP tools that measure over a period: start and stop named measurements, and list them. The data tools
/// report the values of a stopped measurement.
/// </summary>
/// <param name="recorder">Records the measurements.</param>
/// <param name="timeProvider">The clock, for the time zone times are shown in.</param>
[McpServerToolType]
public sealed class MeasurementTools(MeasurementRecorder recorder, TimeProvider timeProvider)
{
	/// <summary>start_measurement</summary>
	[McpServerTool(Name = "start_measurement", Title = "Start a measurement", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Starts a named measurement over a period, e.g. while a game or benchmark runs: from now on, every new "
		+ "HWiNFO poll is recorded until stop_measurement is called with the same name. Several measurements can run "
		+ "at once; they share the recorded data. Starting a name again replaces the result of the earlier one.")]
	public CallToolResult StartMeasurement(
		[Description("A name for the measurement, e.g. \"gaming\" or \"render-test\". Case-insensitive; unique among the running measurements.")]
		string name)
	{
		try
		{
			var start = recorder.Start(name);
			return ToolResults.Success(MeasurementQueries.Started(start, recorder.MemoryLimit, Context()));
		}
		catch (MeasurementException ex)
		{
			return ToolResults.Error(new MeasurementErrorResult(ex.Message));
		}
	}

	/// <summary>stop_measurement</summary>
	[McpServerTool(Name = "stop_measurement", Title = "Stop a measurement", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Stops a running measurement and evaluates min, max, median and average of every reading over its duration. "
		+ "Returns a summary: duration, number of snapshots, and the periods without snapshots (memory limit reached, "
		+ "or no new data from HWiNFO). Query the values with get_cpu, get_gpu, get_memory or get_readings and "
		+ "measurement set to the name.")]
	public CallToolResult StopMeasurement(
		[Description("The name the measurement was started with, case-insensitive.")]
		string name)
	{
		try
		{
			var completed = recorder.Stop(name);
			return ToolResults.Success(MeasurementQueries.Summarize(completed, Context(), withHint: true));
		}
		catch (MeasurementException ex)
		{
			return ToolResults.Error(new MeasurementErrorResult(ex.Message));
		}
	}

	/// <summary>list_measurements</summary>
	[McpServerTool(Name = "list_measurements", Title = "List measurements", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Lists the running measurements, the stopped ones whose values can be queried, and how much memory the "
		+ "recorded snapshots take compared to the limit.")]
	public CallToolResult ListMeasurements()
	{
		return ToolResults.Success(MeasurementQueries.List(recorder.GetOverview(), Context()));
	}

	private QueryContext Context()
	{
		return QueryContext.From(timeProvider);
	}
}
