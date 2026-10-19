using System.Globalization;
using System.Text.RegularExpressions;
using Hwinfo.SharedMemory;
using HwinfoMcp.Catalog;
using HwinfoMcp.Measurements;
using HwinfoMcp.Sensors;

namespace HwinfoMcp.Queries;

/// <summary>
/// When and how a query runs: the current time, which the age of the data is measured against, the time
/// zone poll times are shown in, which names to report, and whether to report a measurement.
/// </summary>
/// <param name="Now">The current time.</param>
/// <param name="TimeZone">The time zone poll times are shown in.</param>
/// <param name="Labels">Whether to report HWiNFO's original names or the ones it displays.</param>
/// <param name="Measurement">
/// The measurement to report the statistics of, instead of the current values. The query then runs on its
/// <see cref="CompletedMeasurement.Readings"/>.
/// </param>
public readonly record struct QueryContext(
	DateTimeOffset Now,
	TimeZoneInfo TimeZone,
	LabelStyle Labels = LabelStyle.Original,
	CompletedMeasurement? Measurement = null)
{
	/// <summary>Creates the context for a query that runs now.</summary>
	public static QueryContext From(TimeProvider timeProvider, LabelStyle labels = LabelStyle.Original)
	{
		return new QueryContext(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone, labels);
	}

	/// <summary>The name of a sensor group to report.</summary>
	public string Name(Sensor sensor)
	{
		return SensorClassifier.Name(sensor, Labels);
	}
}

/// <summary>
/// Builds the answers of the tools from a <see cref="SensorSnapshot"/> that carries data. Pure functions,
/// so they can be tested with recorded snapshots.
/// </summary>
public static class SensorQueries
{
	/// <summary>How many readings get_readings returns unless asked for more.</summary>
	public const int DefaultReadingLimit = 100;

	/// <summary>The most readings get_readings returns.</summary>
	public const int MaxReadingLimit = 2000;

	// The sections get_gpu and get_cpu only report on request. Of the power section, they report the main
	// power reading unless the whole section is requested.
	private static readonly (GpuSection Section, ReadingCategory Category)[] s_optionalGpuSections =
	[
		(GpuSection.Voltages, ReadingCategory.Voltages),
		(GpuSection.Currents, ReadingCategory.Currents),
		(GpuSection.Limits, ReadingCategory.Limits),
		(GpuSection.Fans, ReadingCategory.Fans),
		(GpuSection.Other, ReadingCategory.Other),
	];

	private static readonly (CpuSection Section, ReadingCategory Category)[] s_optionalCpuSections =
	[
		(CpuSection.Voltages, ReadingCategory.Voltages),
		(CpuSection.Currents, ReadingCategory.Currents),
		(CpuSection.Limits, ReadingCategory.Limits),
		(CpuSection.Other, ReadingCategory.Other),
	];

	/// <summary>Builds the answer of hwinfo_status. Unlike the other queries it also handles snapshots without data.</summary>
	public static StatusResult GetStatus(SensorSnapshot snapshot, QueryContext context)
	{
		if (!snapshot.IsAvailable)
		{
			return new StatusResult(Available: false, Message: Messages.NotAvailable(snapshot));
		}

		var age = GetAge(snapshot, context);
		var pollTime = FormatTime(snapshot.PollTime, context);
		var stale = age > SensorPatterns.StaleAfter;
		return new StatusResult(
			Available: true,
			PollTime: pollTime,
			AgeSeconds: Math.Round(age.TotalSeconds, 1),
			Stale: stale,
			SensorGroups: snapshot.Sensors.Length,
			Readings: snapshot.Readings.Length,
			Warning: stale ? Messages.Stale(age, pollTime) : null
		);
	}

	/// <summary>Builds the answer of list_sensors: every sensor group with its number of readings per type.</summary>
	public static SensorListResult ListSensors(SensorSnapshot snapshot, QueryContext context)
	{
		var readings = snapshot.Readings.ToLookup(reading => reading.Sensor);
		var groups = snapshot.Sensors
			.Select(sensor =>
			{
				var sensorReadings = readings[sensor].ToList();
				var types = sensorReadings
					.GroupBy(reading => reading.ReadingType)
					.OrderBy(group => group.Key)
					.ToDictionary(group => group.Key.ToString(), group => group.Count());
				return new SensorGroupInfo(context.Name(sensor), CategoryName(sensor), sensorReadings.Count, types);
			})
			.ToList();

		var (pollTime, warning) = GetFreshness(snapshot, context);
		return new SensorListResult(pollTime, warning, groups.Count, snapshot.Readings.Length, groups);
	}

	/// <summary>Builds the answer of get_readings: the readings matching all given filters, grouped by sensor group.</summary>
	/// <param name="snapshot">The data.</param>
	/// <param name="context">When and how the query runs.</param>
	/// <param name="sensor">Part of the sensor group name, ignoring case. Null or empty for all groups.</param>
	/// <param name="label">Part of the label, ignoring case. Null or empty for all labels.</param>
	/// <param name="types">The measurement types to include. Null or empty for all types.</param>
	/// <param name="includeStats">Whether to include min, max and average.</param>
	/// <param name="limit">The most readings to return, clamped to 1 to <see cref="MaxReadingLimit"/>.</param>
	public static ReadingsResult GetReadings(
		SensorSnapshot snapshot,
		QueryContext context,
		string? sensor,
		string? label,
		IReadOnlyCollection<SensorType>? types,
		bool includeStats,
		int limit)
	{
		sensor = sensor?.Trim();
		label = label?.Trim();
		limit = Math.Clamp(limit, 1, MaxReadingLimit);

		// Both the original and the displayed names match, whichever of them the answer reports
		var matches = snapshot.Readings
			.Where(reading =>
				(string.IsNullOrEmpty(sensor)
					|| SensorClassifier.ContainsText(reading.Sensor.NameUser, sensor)
					|| SensorClassifier.ContainsText(reading.Sensor.NameOrig, sensor))
				&& (string.IsNullOrEmpty(label)
					|| SensorClassifier.ContainsText(reading.LabelUser, label)
					|| SensorClassifier.ContainsText(reading.LabelOrig, label))
				&& (types is null || types.Count == 0 || types.Contains(reading.ReadingType)))
			.ToList();

		// One entry per group, in the order HWiNFO lists them, even if a group's readings aren't listed together
		var groups = matches
			.Take(limit)
			.GroupBy(reading => reading.Sensor)
			.Select(sensorReadings => new ReadingGroup(
				context.Name(sensorReadings.Key),
				[.. sensorReadings.Select(reading => ToValue(reading, context, includeStats))]
			))
			.ToList();

		var returned = Math.Min(limit, matches.Count);
		var truncated = returned < matches.Count;
		var hint = matches.Count == 0
			? Messages.NoReadingMatched
			: truncated ? Messages.Truncated(returned, matches.Count, MaxReadingLimit) : null;

		var (pollTime, warning) = GetFreshness(snapshot, context);
		return new ReadingsResult(pollTime, warning, matches.Count, returned, truncated, hint, groups, Summarize(context));
	}

	/// <summary>
	/// Builds the answer of get_cpu: the readings of every CPU sorted into sections. Of the power readings only
	/// the main one is reported, and voltages, currents, limits, other readings and the CPU readings of other
	/// groups not at all, unless they are requested; the answer names the sections it shortened or left out.
	/// </summary>
	/// <param name="snapshot">The data.</param>
	/// <param name="context">When and how the query runs.</param>
	/// <param name="detail">Whether to include per-core and per-thread values.</param>
	/// <param name="includeStats">Whether to include min, max and average.</param>
	/// <param name="include">The rarely needed sections to include too. Null or empty for none.</param>
	public static CpuResult GetCpu(
		SensorSnapshot snapshot,
		QueryContext context,
		CpuDetail detail,
		bool includeStats,
		IReadOnlyCollection<CpuSection>? include = null)
	{
		include ??= [];
		var (excluded, mainPower) = ResolveSections(include, CpuSection.All, CpuSection.Power, s_optionalCpuSections, SensorPatterns.MainCpuPower);
		var includeRelated = include.Contains(CpuSection.All) || include.Contains(CpuSection.Related);

		var readings = snapshot.Readings.ToLookup(reading => reading.Sensor);
		var cpus = SensorClassifier.FindDevices(snapshot.Sensors, GroupCategory.Cpu)
			.Select(device => BuildDevice(
				device,
				readings,
				context,
				includeStats,
				includeGroup: group => !SensorClassifier.IsExcludedCpuGroup(group),
				includeReading: reading => detail == CpuDetail.Full || !SensorClassifier.IsPerCore(reading),
				excluded,
				mainPower
			))
			.ToList();

		// Only turned into values when asked for; otherwise it only matters whether there are any
		var related = FindCpuRelatedReadings(snapshot.Sensors, readings);
		List<ReadingValue>? relatedValues = null;
		string[]? omitted = null;
		if (includeRelated)
		{
			relatedValues = NullIfEmpty([.. related.Select(item => ToValue(item.Reading, context, includeStats, context.Name(item.Group)))]);
		}
		else if (related.Any())
		{
			omitted = ["related"];
		}

		var (pollTime, warning) = GetFreshness(snapshot, context);
		return new CpuResult(
			pollTime,
			warning,
			detail == CpuDetail.Full ? "full" : "summary",
			cpus.Count == 0 ? Messages.NoCpu : null,
			cpus,
			relatedValues,
			omitted,
			Summarize(context)
		);
	}

	/// <summary>
	/// Builds the answer of get_gpu: the readings of the selected GPUs sorted into sections. Of the power
	/// readings only the main one is reported, and voltages, currents, limits, fans and other readings not at
	/// all, unless they are requested; the answer names the sections it shortened or left out.
	/// </summary>
	/// <param name="snapshot">The data.</param>
	/// <param name="context">When and how the query runs.</param>
	/// <param name="selection">Which GPUs to report.</param>
	/// <param name="includeStats">Whether to include min, max and average.</param>
	/// <param name="include">The rarely needed sections to include too. Null or empty for none.</param>
	public static GpuResult GetGpu(
		SensorSnapshot snapshot,
		QueryContext context,
		GpuSelection selection,
		bool includeStats,
		IReadOnlyCollection<GpuSection>? include = null)
	{
		var (excluded, mainPower) = ResolveSections(include ?? [], GpuSection.All, GpuSection.Power, s_optionalGpuSections, SensorPatterns.MainGpuPower);

		var readings = snapshot.Readings.ToLookup(reading => reading.Sensor);
		var devices = SensorClassifier.FindDevices(snapshot.Sensors, GroupCategory.Gpu);

		IReadOnlyList<Device> selected = selection switch
		{
			GpuSelection.Dgpu => [.. devices.Where(device => device.GpuKind == GpuKind.Discrete)],
			GpuSelection.Igpu => [.. devices.Where(device => device.GpuKind == GpuKind.Integrated)],
			_ => devices,
		};

		string? note = null;
		if (devices.IsEmpty)
		{
			note = Messages.NoGpu;
		}
		else if (selected.Count == 0)
		{
			// Rather report the GPU there is than nothing at all
			selected = devices;
			note = Messages.GpuFallback(selection);
		}

		var gpus = selected
			.Select(device => BuildDevice(device, readings, context, includeStats, includeGroup: _ => true, includeReading: _ => true, excluded, mainPower))
			.ToList();

		var (pollTime, warning) = GetFreshness(snapshot, context);
		return new GpuResult(pollTime, warning, SelectionName(selection), note, gpus, Summarize(context));
	}

	/// <summary>
	/// Builds the answer of get_memory: memory timings, the memory modules, RAM usage and the memory
	/// controller readings of the CPU.
	/// </summary>
	public static MemoryResult GetMemory(SensorSnapshot snapshot, QueryContext context, bool includeStats)
	{
		var readings = snapshot.Readings.ToLookup(reading => reading.Sensor);
		var groups = snapshot.Sensors.Select(sensor => (Sensor: sensor, Category: SensorClassifier.CategorizeGroup(sensor))).ToList();

		var timingGroups = groups.Where(group => group.Category == GroupCategory.MemoryTimings).ToList();
		var timings = timingGroups
			.SelectMany(group => readings[group.Sensor].Select(reading =>
				ToValue(reading, context, includeStats, timingGroups.Count > 1 ? context.Name(group.Sensor) : null)))
			.ToList();

		var dimms = groups
			.Where(group => group.Category == GroupCategory.Dimm)
			.Select(group => new DimmReadings(
				context.Name(group.Sensor),
				[.. readings[group.Sensor].Select(reading => ToValue(reading, context, includeStats))]
			))
			.ToList();

		var usage = groups
			.Where(group => group.Category == GroupCategory.System)
			.SelectMany(group => readings[group.Sensor])
			.Where(reading => SensorClassifier.LabelMatches(SensorPatterns.MemoryUsage, reading))
			.Select(reading => ToValue(reading, context, includeStats))
			.ToList();

		var controller = SensorClassifier.FindDevices(snapshot.Sensors, GroupCategory.Cpu)
			.SelectMany(device => device.Groups)
			.Where(group => !SensorClassifier.IsExcludedCpuGroup(group))
			.SelectMany(group => readings[group]
				.Where(reading => SensorClassifier.CategorizeReading(reading, group) == ReadingCategory.MemoryController)
				.Select(reading => ToValue(reading, context, includeStats, context.Name(group))))
			.ToList();

		var empty = timings.Count == 0 && dimms.Count == 0 && usage.Count == 0 && controller.Count == 0;
		var (pollTime, warning) = GetFreshness(snapshot, context);
		return new MemoryResult(
			pollTime,
			warning,
			empty ? Messages.NoMemory : null,
			NullIfEmpty(timings),
			dimms.Count == 0 ? null : dimms,
			NullIfEmpty(usage),
			NullIfEmpty(controller),
			Summarize(context)
		);
	}

	/// <summary>
	/// Converts a reading into what the tools report, values rounded to two decimals. For a measurement, its min,
	/// max, median and average instead of the current value.
	/// </summary>
	/// <param name="reading">The reading.</param>
	/// <param name="context">Decides which label to report, and whether to report a measurement.</param>
	/// <param name="includeStats">Whether to include min, max and average of the current values.</param>
	/// <param name="group">The group to report the reading under, if any.</param>
	public static ReadingValue ToValue(SensorReading reading, QueryContext context, bool includeStats, string? group = null)
	{
		var label = SensorClassifier.Label(reading, context.Labels);
		var unit = reading.Unit.Trim();
		if (context.Measurement is { } measurement)
		{
			var stats = measurement.StatsOf(reading);
			return new ReadingValue(
				label,
				Value: null,
				unit.Length == 0 ? null : unit,
				Min: Round(stats.Min),
				Max: Round(stats.Max),
				Median: Round(stats.Median),
				Avg: Round(stats.Avg),
				Samples: stats.Samples == measurement.Snapshots ? null : stats.Samples,
				Group: group
			)
			{
				Measured = true,
			};
		}

		return new ReadingValue(
			label,
			Round(reading.Value),
			unit.Length == 0 ? null : unit,
			Min: includeStats ? Round(reading.ValueMin) : null,
			Max: includeStats ? Round(reading.ValueMax) : null,
			Avg: includeStats ? Round(reading.ValueAvg) : null,
			Group: group
		);
	}

	/// <summary>Rounds to two decimals. Null for values JSON can't represent.</summary>
	public static double? Round(double value)
	{
		if (!double.IsFinite(value))
		{
			return null;
		}

		var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
		// Avoids reporting "-0" for tiny negative values
		return rounded == 0 ? 0 : rounded;
	}

	/// <summary>Formats a point in time in the given time zone, e.g. "2026-10-19T10:36:33+02:00".</summary>
	public static string FormatTime(DateTimeOffset time, QueryContext context)
	{
		return TimeZoneInfo.ConvertTime(time, context.TimeZone)
			.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
	}

	/// <summary>The category list_sensors reports for a sensor group.</summary>
	public static string CategoryName(Sensor sensor)
	{
		return SensorClassifier.CategorizeGroup(sensor) switch
		{
			GroupCategory.Cpu => "cpu",
			GroupCategory.Gpu => KindName(SensorClassifier.ClassifyGpu(sensor)),
			GroupCategory.MemoryTimings => "memory",
			GroupCategory.Dimm => "dimm",
			GroupCategory.System => "system",
			_ => "other",
		};
	}

	private static string KindName(GpuKind kind)
	{
		return kind == GpuKind.Integrated ? "igpu" : "dgpu";
	}

	private static string SelectionName(GpuSelection selection)
	{
		return selection switch
		{
			GpuSelection.Igpu => "igpu",
			GpuSelection.All => "all",
			_ => "dgpu",
		};
	}

	private static TimeSpan GetAge(SensorSnapshot snapshot, QueryContext context)
	{
		// A clock running slightly behind HWiNFO's would otherwise report a negative age
		var age = context.Now - snapshot.PollTime;
		return age < TimeSpan.Zero ? TimeSpan.Zero : age;
	}

	private static (string PollTime, string? Warning) GetFreshness(SensorSnapshot snapshot, QueryContext context)
	{
		var pollTime = FormatTime(snapshot.PollTime, context);
		// A measurement is old by nature; what its values don't cover is in its summary
		if (context.Measurement is not null)
		{
			return (pollTime, null);
		}

		var age = GetAge(snapshot, context);
		return (pollTime, age > SensorPatterns.StaleAfter ? Messages.Stale(age, pollTime) : null);
	}

	private static MeasurementSummary? Summarize(QueryContext context)
	{
		return context.Measurement is { } measurement ? MeasurementQueries.Summarize(measurement, context) : null;
	}

	/// <summary>
	/// The CPU readings of groups other than the CPU's own: the memory controller readings of the memory timings
	/// group, and the CPU temperatures, fans, voltages, currents and power of mainboard, cooler and the like.
	/// </summary>
	private static IEnumerable<(SensorReading Reading, Sensor Group)> FindCpuRelatedReadings(
		IEnumerable<Sensor> sensors,
		ILookup<Sensor, SensorReading> readings)
	{
		foreach (var sensor in sensors)
		{
			var category = SensorClassifier.CategorizeGroup(sensor);
			if (category is not (GroupCategory.MemoryTimings or GroupCategory.Other))
			{
				continue;
			}

			foreach (var reading in readings[sensor])
			{
				var isRelated = category == GroupCategory.MemoryTimings
					? SensorClassifier.LabelMatches(SensorPatterns.MemoryController, reading)
					: IsCpuRelated(reading);
				if (isRelated)
				{
					yield return (reading, sensor);
				}
			}
		}
	}

	private static bool IsCpuRelated(SensorReading reading)
	{
		return reading.ReadingType is SensorType.Temp or SensorType.Fan or SensorType.Volt or SensorType.Current or SensorType.Power
			&& SensorClassifier.LabelMatches(SensorPatterns.CpuRelated, reading);
	}

	/// <summary>
	/// Works out from the include parameter of get_gpu or get_cpu which sections to leave out, and whether to
	/// shorten the power section to its main reading.
	/// </summary>
	/// <returns>
	/// The sections to leave out, and the patterns of the main power reading, or null to report all power readings.
	/// </returns>
	private static (HashSet<ReadingCategory> Excluded, IReadOnlyList<Regex>? MainPower) ResolveSections<TSection>(
		IReadOnlyCollection<TSection> include,
		TSection all,
		TSection power,
		IEnumerable<(TSection Section, ReadingCategory Category)> optionalSections,
		IReadOnlyList<Regex> mainPowerPatterns)
		where TSection : struct, Enum
	{
		if (include.Contains(all))
		{
			return ([], null);
		}

		var excluded = optionalSections
			.Where(section => !include.Contains(section.Section))
			.Select(section => section.Category)
			.ToHashSet();
		return (excluded, include.Contains(power) ? null : mainPowerPatterns);
	}

	private static DeviceReadings BuildDevice(
		Device device,
		ILookup<Sensor, SensorReading> readings,
		QueryContext context,
		bool includeStats,
		Func<Sensor, bool> includeGroup,
		Func<SensorReading, bool> includeReading,
		HashSet<ReadingCategory>? excludedSections = null,
		IReadOnlyList<Regex>? mainPowerPatterns = null)
	{
		var groups = device.Groups.Where(includeGroup).ToList();
		var sections = new Dictionary<ReadingCategory, List<ReadingValue>>();
		var omitted = new SortedSet<ReadingCategory>();
		// The readings behind the power section, in the same order, to pick the main one by its original label
		var powerReadings = new List<SensorReading>();
		foreach (var group in groups)
		{
			var groupName = ShortGroupName(device, group, context);
			foreach (var reading in readings[group].Where(includeReading))
			{
				var category = SensorClassifier.CategorizeReading(reading, group);
				if (excludedSections?.Contains(category) == true)
				{
					omitted.Add(category);
					continue;
				}

				if (!sections.TryGetValue(category, out var section))
				{
					section = [];
					sections.Add(category, section);
				}

				section.Add(ToValue(reading, context, includeStats, groupName));
				if (category == ReadingCategory.Power)
				{
					powerReadings.Add(reading);
				}
			}
		}

		if (mainPowerPatterns is not null && powerReadings.Count > 1)
		{
			var main = SensorClassifier.FindMainPower(powerReadings, mainPowerPatterns);
			sections[ReadingCategory.Power] = [sections[ReadingCategory.Power][main]];
			omitted.Add(ReadingCategory.Power);
		}

		return new DeviceReadings
		{
			Name = context.Name(device.Primary),
			Kind = device.GpuKind is { } kind ? KindName(kind) : null,
			SensorGroups = [.. groups.Select(context.Name)],
			Power = sections.GetValueOrDefault(ReadingCategory.Power),
			Temperatures = sections.GetValueOrDefault(ReadingCategory.Temperatures),
			Usage = sections.GetValueOrDefault(ReadingCategory.Usage),
			MemoryController = sections.GetValueOrDefault(ReadingCategory.MemoryController),
			Vram = sections.GetValueOrDefault(ReadingCategory.Vram),
			Clocks = sections.GetValueOrDefault(ReadingCategory.Clocks),
			Limits = sections.GetValueOrDefault(ReadingCategory.Limits),
			Throttling = sections.GetValueOrDefault(ReadingCategory.Throttling),
			Fans = sections.GetValueOrDefault(ReadingCategory.Fans),
			Voltages = sections.GetValueOrDefault(ReadingCategory.Voltages),
			Currents = sections.GetValueOrDefault(ReadingCategory.Currents),
			Other = sections.GetValueOrDefault(ReadingCategory.Other),
			Omitted = omitted.Count == 0 ? null : [.. omitted.Select(SectionName)],
		};
	}

	/// <summary>The name of a section as the answer and the include parameter of get_gpu spell it.</summary>
	private static string SectionName(ReadingCategory category)
	{
		return category switch
		{
			ReadingCategory.MemoryController => "memoryController",
			_ => category.ToString().ToLowerInvariant(),
		};
	}

	/// <summary>
	/// The group name a device overview reports a reading under: none for the main group, the suffix for
	/// a sub group ("Enhanced" for "CPU [#0]: AMD Ryzen 9 7900X: Enhanced"), the full name otherwise.
	/// </summary>
	private static string? ShortGroupName(Device device, Sensor group, QueryContext context)
	{
		if (group.Equals(device.Primary))
		{
			return null;
		}

		var primaryName = context.Name(device.Primary);
		var name = context.Name(group);
		return name.Length > primaryName.Length + 1
			&& name.StartsWith(primaryName, StringComparison.OrdinalIgnoreCase)
			&& name[primaryName.Length] == ':'
				? name[(primaryName.Length + 1)..].Trim()
				: name;
	}

	private static List<ReadingValue>? NullIfEmpty(List<ReadingValue> values)
	{
		return values.Count == 0 ? null : values;
	}
}
