using System.Globalization;
using HwinfoMcp.Measurements;
using HwinfoMcp.Sensors;

namespace HwinfoMcp.Queries;

/// <summary>
/// The texts the tools answer with when something is missing or off.
/// </summary>
public static class Messages
{
	/// <summary>Where the setting is that makes HWiNFO publish its sensor data.</summary>
	public const string SharedMemorySetting = "HWiNFO → Settings → 'Shared Memory Support'";

	/// <summary>The free version of HWiNFO only shares its sensor data for 12 hours at a time.</summary>
	public const string FreeVersionLimit =
		"The free version of HWiNFO stops Shared Memory Support after 12 hours; re-enable the option or restart HWiNFO then.";

	/// <summary>The answer when there are no CPU groups.</summary>
	public const string NoCpu = "HWiNFO reports no CPU sensor groups. Call list_sensors to see what it reports.";

	/// <summary>The answer when there are no GPU groups.</summary>
	public const string NoGpu = "HWiNFO reports no GPU sensor groups. Call list_sensors to see what it reports.";

	/// <summary>The answer when there are no memory groups at all.</summary>
	public const string NoMemory =
		"HWiNFO reports no memory timings, memory modules or memory usage. Call list_sensors to see what it reports.";

	/// <summary>The answer when no reading matches the filters of get_readings.</summary>
	public const string NoReadingMatched =
		"No reading matched. Call list_sensors for the available sensor groups and measurement types.";

	/// <summary>Explains why a snapshot carries no data and what to do about it.</summary>
	public static string NotAvailable(SensorSnapshot snapshot)
	{
		return snapshot.State switch
		{
			SnapshotState.Failed => $"{snapshot.Error} Make sure HWiNFO is running with {SharedMemorySetting} enabled.",
			_ => "HWiNFO sensor data is not available: HWiNFO is not running or its shared memory is turned off. "
				+ $"Start HWiNFO and enable {SharedMemorySetting}. {FreeVersionLimit}",
		};
	}

	/// <summary>Warns that HWiNFO hasn't updated the data for a while.</summary>
	public static string Stale(TimeSpan age, string pollTime)
	{
		return $"The data is stale: HWiNFO last updated it {FormatDuration(age)} ago ({pollTime}). HWiNFO may be "
			+ "paused or closed, or the free version stopped sharing after its 12-hour limit. "
			+ $"Re-enable {SharedMemorySetting} or restart HWiNFO.";
	}

	/// <summary>Explains that get_readings cut its answer short.</summary>
	public static string Truncated(int returned, int total, int maxLimit)
	{
		return $"Showing the first {returned} of {total} matching readings. Narrow the query with sensor, label or "
			+ $"types, or raise limit (up to {maxLimit}).";
	}

	/// <summary>Explains that get_gpu shows other GPUs than requested because there are none of that kind.</summary>
	public static string GpuFallback(GpuSelection requested)
	{
		return requested == GpuSelection.Igpu
			? "There is no integrated GPU; showing the discrete GPU(s) instead."
			: "There is no discrete GPU; showing the integrated GPU(s) instead.";
	}

	/// <summary>The answer when a measurement is started, stopped or queried without a name.</summary>
	public const string MeasurementNameMissing = "Give the measurement a name, e.g. \"gaming\".";

	/// <summary>Says that the measurement has no snapshots to evaluate.</summary>
	public const string NoSnapshots =
		"No snapshots were recorded: HWiNFO's data wasn't available or wasn't updated during the measurement, so "
		+ "there is nothing to evaluate.";

	/// <summary>The answer when the name of a measurement is too long.</summary>
	public static string MeasurementNameTooLong(int maxLength)
	{
		return $"The name of a measurement may be at most {maxLength} characters long.";
	}

	/// <summary>The answer when a measurement is started under the name of a running one.</summary>
	public static string MeasurementAlreadyRunning(string name)
	{
		return $"A measurement named '{name}' is already running. Stop it with stop_measurement first, or choose another name.";
	}

	/// <summary>The answer when a measurement is stopped that doesn't run.</summary>
	public static string MeasurementNotRunning(string name, IReadOnlyCollection<string> running)
	{
		return running.Count == 0
			? $"No measurement named '{name}' is running; no measurement is running at all."
			: $"No measurement named '{name}' is running. Running: {NameList(running)}.";
	}

	/// <summary>The answer when the values of a measurement are asked for that has no result.</summary>
	/// <param name="name">The name asked for.</param>
	/// <param name="running">Whether a measurement with that name is running.</param>
	/// <param name="stopped">The names of the stopped measurements with a result.</param>
	public static string MeasurementNotFound(string name, bool running, IReadOnlyCollection<string> stopped)
	{
		var text = running
			? $"The measurement '{name}' is still running; stop it with stop_measurement to evaluate it."
			: $"There is no stopped measurement named '{name}'.";
		return stopped.Count == 0
			? $"{text} There are no stopped measurements to query."
			: $"{text} Stopped measurements: {NameList(stopped)}.";
	}

	/// <summary>The answer when evaluating a stopped measurement failed.</summary>
	public static string MeasurementEvaluationFailed(string name, string error)
	{
		return $"The measurement '{name}' is stopped, but evaluating it failed: {error}";
	}

	/// <summary>Says how to query the values of a stopped measurement.</summary>
	public static string MeasurementHint(string name)
	{
		return $"Query the values with get_cpu, get_gpu, get_memory or get_readings and measurement \"{name}\".";
	}

	/// <summary>Warns that the measurement memory limit is reached, so new polls aren't recorded.</summary>
	public static string MeasurementLimitReached(long limit)
	{
		return $"The measurement memory limit of {ByteSize.Format(limit)} is reached: new polls aren't recorded until "
			+ $"running measurements are stopped. {RaiseLimit}";
	}

	/// <summary>Warns that a measurement started without data from HWiNFO.</summary>
	public static string MeasurementWithoutData(SensorSnapshot current)
	{
		return $"{NotAvailable(current)} The measurement runs anyway and records HWiNFO's data once it is available.";
	}

	/// <summary>Warns that a measurement started while HWiNFO's data is stale.</summary>
	public static string MeasurementOnStaleData(TimeSpan age)
	{
		return $"HWiNFO hasn't updated its data for {FormatDuration(age)}. The measurement runs anyway and records "
			+ $"HWiNFO's data once it updates again. {FreeVersionLimit}";
	}

	/// <summary>Warns that the memory limit kept polls of a measurement from being recorded.</summary>
	/// <param name="limit">The memory limit.</param>
	/// <param name="unmeasured">How long the measurement went without snapshots because of the limit.</param>
	/// <param name="periods">Each period without snapshots, e.g. "between the last snapshot at ... and the stop at ...".</param>
	public static string MemoryLimitGaps(long limit, TimeSpan unmeasured, IEnumerable<string> periods)
	{
		return $"The measurement memory limit of {ByteSize.Format(limit)} was reached, so {FormatDuration(unmeasured)} "
			+ $"of the measurement are unmeasured: no snapshots were recorded {string.Join("; ", periods)}. The values "
			+ $"only cover the rest. {RaiseLimit}";
	}

	/// <summary>Warns that HWiNFO delivered no new data during parts of a measurement.</summary>
	/// <param name="unmeasured">How long HWiNFO delivered no new data.</param>
	/// <param name="periods">Each period without new data, e.g. "between the last snapshot at ... and the stop at ...".</param>
	public static string NoDataGaps(TimeSpan unmeasured, IEnumerable<string> periods)
	{
		return $"HWiNFO delivered no new data for {FormatDuration(unmeasured)} of the measurement, "
			+ $"{string.Join("; ", periods)}: it was closed or paused, or stopped sharing its data. {FreeVersionLimit}";
	}

	/// <summary>Formats a duration for humans, e.g. "45 seconds", "12 minutes", "13.5 hours".</summary>
	public static string FormatDuration(TimeSpan duration)
	{
		if (duration < TimeSpan.FromMinutes(2))
		{
			return string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(duration.TotalSeconds):0} seconds");
		}

		return duration < TimeSpan.FromHours(2)
			? string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(duration.TotalMinutes):0} minutes")
			: string.Create(CultureInfo.InvariantCulture, $"{duration.TotalHours:0.#} hours");
	}

	private static string RaiseLimit =>
		$"Raise the limit with {HwinfoServerOptions.MeasurementMemoryLimitSwitch} or the environment variable "
		+ $"{HwinfoServerOptions.MeasurementMemoryLimitVariable}, or run fewer measurements at a time.";

	private static string NameList(IEnumerable<string> names)
	{
		return string.Join(", ", names.Select(name => $"'{name}'"));
	}
}
