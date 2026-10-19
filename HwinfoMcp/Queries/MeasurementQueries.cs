using HwinfoMcp.Measurements;

namespace HwinfoMcp.Queries;

/// <summary>Builds the answers of the measurement tools.</summary>
public static class MeasurementQueries
{
	/// <summary>Builds the answer of start_measurement.</summary>
	/// <param name="start">The measurement that just started.</param>
	/// <param name="memoryLimit">The memory limit for the recorded snapshots.</param>
	/// <param name="context">The time zone to show times in.</param>
	public static StartMeasurementResult Started(MeasurementStart start, long memoryLimit, QueryContext context)
	{
		var warning = start.Outcome switch
		{
			SampleOutcome.Unavailable => Messages.MeasurementWithoutData(start.Current),
			SampleOutcome.Stale => Messages.MeasurementOnStaleData(start.Start - start.Current.PollTime),
			SampleOutcome.Dropped => Messages.MeasurementLimitReached(memoryLimit),
			_ => null,
		};
		return new StartMeasurementResult(
			start.Name,
			SensorQueries.FormatTime(start.Start, context),
			start.Running,
			ByteSize.Format(memoryLimit),
			warning
		);
	}

	/// <summary>
	/// Summarizes a stopped measurement: when it ran, how many snapshots it has, and the periods its values
	/// don't cover.
	/// </summary>
	/// <param name="measurement">The measurement.</param>
	/// <param name="context">The time zone to show times in.</param>
	/// <param name="withHint">Whether to say how to query the values, for the answer of stop_measurement.</param>
	public static MeasurementSummary Summarize(CompletedMeasurement measurement, QueryContext context, bool withHint = false)
	{
		var gaps = measurement.Gaps
			.Select(gap => new MeasurementGap(
				SensorQueries.FormatTime(gap.From, context),
				SensorQueries.FormatTime(gap.To, context),
				Seconds(gap.Duration),
				gap.Reason == GapReason.MemoryLimit ? "memoryLimit" : "noData"
			))
			.ToList();

		var warnings = new List<string>();
		var memoryGaps = measurement.Gaps.Where(gap => gap.Reason == GapReason.MemoryLimit).ToList();
		if (memoryGaps.Count > 0)
		{
			warnings.Add(Messages.MemoryLimitGaps(measurement.MemoryLimit, Total(memoryGaps), memoryGaps.Select(gap => Describe(gap, measurement, context))));
		}

		var noDataGaps = measurement.Gaps.Where(gap => gap.Reason == GapReason.NoData).ToList();
		if (noDataGaps.Count > 0)
		{
			warnings.Add(Messages.NoDataGaps(Total(noDataGaps), noDataGaps.Select(gap => Describe(gap, measurement, context))));
		}

		if (measurement.Snapshots == 0)
		{
			warnings.Add(Messages.NoSnapshots);
		}

		return new MeasurementSummary(
			measurement.Name,
			SensorQueries.FormatTime(measurement.Start, context),
			SensorQueries.FormatTime(measurement.Stop, context),
			Seconds(measurement.Duration),
			measurement.Snapshots,
			measurement.FirstSnapshot is { } first ? SensorQueries.FormatTime(first, context) : null,
			measurement.LastSnapshot is { } last ? SensorQueries.FormatTime(last, context) : null,
			gaps.Count == 0 ? null : Seconds(Total(measurement.Gaps)),
			gaps.Count == 0 ? null : gaps,
			warnings.Count == 0 ? null : string.Join(" ", warnings),
			withHint && measurement.Snapshots > 0 ? Messages.MeasurementHint(measurement.Name) : null
		);
	}

	/// <summary>Builds the answer of list_measurements.</summary>
	/// <param name="overview">The state of all measurements.</param>
	/// <param name="context">The current time, and the time zone to show times in.</param>
	public static MeasurementListResult List(MeasurementOverview overview, QueryContext context)
	{
		var running = overview.Running
			.Select(measurement => new RunningMeasurementEntry(
				measurement.Name,
				SensorQueries.FormatTime(measurement.Start, context),
				Seconds(context.Now - measurement.Start),
				measurement.Snapshots
			))
			.ToList();
		var stopped = overview.Completed.Select(measurement => Summarize(measurement, context)).ToList();
		var memory = new MeasurementMemory(
			ByteSize.Format(overview.UsedBytes),
			ByteSize.Format(overview.MemoryLimit),
			overview.Snapshots,
			overview.LimitReached
		);
		return new MeasurementListResult(
			running,
			stopped,
			memory,
			overview.LimitReached ? Messages.MeasurementLimitReached(overview.MemoryLimit) : null
		);
	}

	/// <summary>Describes a gap, e.g. "between the last snapshot at ... and the stop at ...".</summary>
	private static string Describe(UnmeasuredPeriod gap, CompletedMeasurement measurement, QueryContext context)
	{
		var from = gap.From == measurement.Start ? "the start" : "the last snapshot";
		var to = gap.To == measurement.Stop ? "the stop" : "the next snapshot";
		return $"between {from} at {SensorQueries.FormatTime(gap.From, context)} and {to} at {SensorQueries.FormatTime(gap.To, context)}";
	}

	private static TimeSpan Total(IEnumerable<UnmeasuredPeriod> gaps)
	{
		return gaps.Aggregate(TimeSpan.Zero, (total, gap) => total + gap.Duration);
	}

	private static double Seconds(TimeSpan duration)
	{
		return Math.Round(Math.Max(0, duration.TotalSeconds), 1);
	}
}
