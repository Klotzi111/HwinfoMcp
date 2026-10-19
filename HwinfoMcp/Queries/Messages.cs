using System.Globalization;
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
}
