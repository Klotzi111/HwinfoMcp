using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Hwinfo.SharedMemory;

namespace HwinfoMcp.Catalog;

/// <summary>What kind of hardware a sensor group describes.</summary>
public enum GroupCategory
{
	/// <summary>A CPU group, see <see cref="SensorPatterns.CpuGroup"/>.</summary>
	Cpu,

	/// <summary>A GPU group, see <see cref="SensorPatterns.GpuGroup"/>.</summary>
	Gpu,

	/// <summary>The memory timings group, see <see cref="SensorPatterns.MemoryTimingsGroup"/>.</summary>
	MemoryTimings,

	/// <summary>A memory module, see <see cref="SensorPatterns.DimmGroup"/>.</summary>
	Dimm,

	/// <summary>The system group, see <see cref="SensorPatterns.SystemGroup"/>.</summary>
	System,

	/// <summary>Anything else: mainboard, drives, network, PresentMon, ...</summary>
	Other,
}

/// <summary>Whether a GPU is a discrete card or integrated into the CPU.</summary>
public enum GpuKind
{
	/// <summary>A discrete GPU (dGPU).</summary>
	Discrete,

	/// <summary>An integrated GPU (iGPU).</summary>
	Integrated,
}

/// <summary>The section of a CPU or GPU overview a reading is listed under.</summary>
public enum ReadingCategory
{
	/// <summary>Power draw.</summary>
	Power,

	/// <summary>Temperatures, including hotspots and memory junction.</summary>
	Temperatures,

	/// <summary>Utilization and load.</summary>
	Usage,

	/// <summary>Clocks other than memory clocks.</summary>
	Clocks,

	/// <summary>Memory controller load, memory and fabric clocks, DRAM bandwidth.</summary>
	MemoryController,

	/// <summary>GPU memory usage.</summary>
	Vram,

	/// <summary>Limits and how much of them is used.</summary>
	Limits,

	/// <summary>Throttling indicators and limit reasons.</summary>
	Throttling,

	/// <summary>Fans and pumps.</summary>
	Fans,

	/// <summary>Voltages.</summary>
	Voltages,

	/// <summary>Currents.</summary>
	Currents,

	/// <summary>Everything else.</summary>
	Other,
}

/// <summary>
/// A CPU or GPU, made up of all sensor groups that share a prefix like "CPU [#0]" or "dGPU [#0]".
/// </summary>
/// <param name="Category">Either <see cref="GroupCategory.Cpu"/> or <see cref="GroupCategory.Gpu"/>.</param>
/// <param name="Prefix">The shared prefix, e.g. "CPU [#0]".</param>
/// <param name="GpuKind">Discrete or integrated, for GPUs only.</param>
/// <param name="Groups">The sensor groups of the device in HWiNFO's order. The first one is the main group.</param>
public sealed record Device(GroupCategory Category, string Prefix, GpuKind? GpuKind, ImmutableArray<Sensor> Groups)
{
	/// <summary>The main sensor group, the first one HWiNFO lists for the device.</summary>
	public Sensor Primary => Groups[0];
}

/// <summary>Which of HWiNFO's two names for sensor groups and readings the tools report.</summary>
public enum LabelStyle
{
	/// <summary>
	/// The original English names. Stable across HWiNFO's language settings and unambiguous: HWiNFO's
	/// translations sometimes give different readings the same name.
	/// </summary>
	Original,

	/// <summary>The names as displayed in HWiNFO: translated, and renamed by the user where they did.</summary>
	User,
}

/// <summary>
/// Applies <see cref="SensorPatterns"/> to sensor groups and readings.
/// </summary>
public static class SensorClassifier
{
	/// <summary>The name patterns are matched against: the original name, which renaming in HWiNFO doesn't change.</summary>
	public static string MatchName(Sensor sensor)
	{
		return string.IsNullOrWhiteSpace(sensor.NameOrig) ? sensor.NameUser : sensor.NameOrig;
	}

	/// <summary>The name of a sensor group to report, e.g. "CPU [#0]: AMD Ryzen 9 7900X".</summary>
	public static string Name(Sensor sensor, LabelStyle style)
	{
		var name = Choose(sensor.NameOrig, sensor.NameUser, style);
		// HWiNFO ends some names with a separator, e.g. "GPU [#0]: NVIDIA GeForce RTX 4080: "
		return name.TrimEnd(':').TrimEnd();
	}

	/// <summary>The label of a reading to report, e.g. "CPU Package Power".</summary>
	public static string Label(SensorReading reading, LabelStyle style)
	{
		return Choose(reading.LabelOrig, reading.LabelUser, style);
	}

	/// <summary>
	/// Picks the original or the user-defined name. A user-defined name that merely extends the original one
	/// is HWiNFO adding details, e.g. "PresentMon [dwm.exe]", so it is kept for the original style too.
	/// </summary>
	private static string Choose(string original, string user, LabelStyle style)
	{
		original = original.Trim();
		user = user.Trim();
		if (original.Length == 0)
		{
			return user;
		}

		if (user.Length == 0)
		{
			return original;
		}

		return style == LabelStyle.User || user.StartsWith(original, StringComparison.OrdinalIgnoreCase) ? user : original;
	}

	/// <summary>Returns whether a keyword of <paramref name="keywords"/> occurs in the original or the displayed label.</summary>
	public static bool LabelMatches(KeywordSet keywords, SensorReading reading)
	{
		return keywords.IsMatch(reading.LabelOrig) || keywords.IsMatch(reading.LabelUser);
	}

	/// <summary>Returns whether a display name or label contains <paramref name="text"/>, ignoring case.</summary>
	public static bool ContainsText(string? value, string text)
	{
		return value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Determines what kind of hardware a sensor group describes.</summary>
	public static GroupCategory CategorizeGroup(Sensor sensor)
	{
		var name = MatchName(sensor);
		if (SensorPatterns.CpuGroup.IsMatch(name))
		{
			return GroupCategory.Cpu;
		}

		if (SensorPatterns.GpuGroup.IsMatch(name))
		{
			return GroupCategory.Gpu;
		}

		if (SensorPatterns.MemoryTimingsGroup.IsMatch(name))
		{
			return GroupCategory.MemoryTimings;
		}

		if (SensorPatterns.DimmGroup.IsMatch(name))
		{
			return GroupCategory.Dimm;
		}

		return SensorPatterns.SystemGroup.IsMatch(name) ? GroupCategory.System : GroupCategory.Other;
	}

	/// <summary>
	/// Collects the sensor groups of all CPUs or all GPUs into devices, in the order HWiNFO lists them.
	/// </summary>
	/// <param name="sensors">All sensor groups.</param>
	/// <param name="category"><see cref="GroupCategory.Cpu"/> or <see cref="GroupCategory.Gpu"/>.</param>
	public static ImmutableArray<Device> FindDevices(IEnumerable<Sensor> sensors, GroupCategory category)
	{
		var pattern = category switch
		{
			GroupCategory.Cpu => SensorPatterns.CpuGroup,
			GroupCategory.Gpu => SensorPatterns.GpuGroup,
			_ => throw new ArgumentOutOfRangeException(nameof(category), category, "Only CPUs and GPUs are devices."),
		};

		var prefixes = new List<string>();
		var groupsByPrefix = new Dictionary<string, List<Sensor>>(StringComparer.OrdinalIgnoreCase);
		foreach (var sensor in sensors)
		{
			var match = pattern.Match(MatchName(sensor));
			if (!match.Success)
			{
				continue;
			}

			if (!groupsByPrefix.TryGetValue(match.Value, out var groups))
			{
				groups = [];
				groupsByPrefix.Add(match.Value, groups);
				prefixes.Add(match.Value);
			}

			groups.Add(sensor);
		}

		return [
			.. prefixes.Select(prefix =>
			{
				var groups = groupsByPrefix[prefix];
				var gpuKind = category == GroupCategory.Gpu ? ClassifyGpu(groups[0]) : (GpuKind?)null;
				return new Device(category, prefix, gpuKind, [.. groups]);
			}),
		];
	}

	/// <summary>
	/// Tells a discrete from an integrated GPU: by HWiNFO's "dGPU"/"iGPU" prefix where present,
	/// otherwise by the name (see <see cref="SensorPatterns.IntegratedGpuName"/>).
	/// </summary>
	public static GpuKind ClassifyGpu(Sensor sensor)
	{
		var name = MatchName(sensor);
		var match = SensorPatterns.GpuGroup.Match(name);
		var kind = match.Success ? match.Groups["kind"].Value : string.Empty;
		if (kind.Equals("d", StringComparison.OrdinalIgnoreCase))
		{
			return GpuKind.Discrete;
		}

		if (kind.Equals("i", StringComparison.OrdinalIgnoreCase))
		{
			return GpuKind.Integrated;
		}

		return SensorPatterns.IntegratedGpuName.IsMatch(name) || SensorPatterns.IntegratedGpuName.IsMatch(sensor.NameUser)
			? GpuKind.Integrated
			: GpuKind.Discrete;
	}

	/// <summary>Returns whether get_cpu leaves this CPU sub group out, e.g. "...: C-State Residency".</summary>
	public static bool IsExcludedCpuGroup(Sensor sensor)
	{
		return SensorPatterns.CpuExcludedGroups.IsMatch(MatchName(sensor));
	}

	/// <summary>Returns whether a reading is a per-core or per-thread value.</summary>
	public static bool IsPerCore(SensorReading reading)
	{
		var label = string.IsNullOrWhiteSpace(reading.LabelOrig) ? reading.LabelUser : reading.LabelOrig;
		return SensorPatterns.PerCoreLabel.IsMatch(label);
	}

	/// <summary>
	/// Picks the main power reading of a device from its power readings: the first reading matching the
	/// earliest pattern, see <see cref="SensorPatterns.MainGpuPower"/> and <see cref="SensorPatterns.MainCpuPower"/>.
	/// </summary>
	/// <param name="powerReadings">The device's power readings in HWiNFO's order; must not be empty.</param>
	/// <param name="patterns">The patterns of main power labels, most preferred first.</param>
	/// <returns>The index of the main power reading, the first one if no pattern matches.</returns>
	public static int FindMainPower(IReadOnlyList<SensorReading> powerReadings, IReadOnlyList<Regex> patterns)
	{
		foreach (var pattern in patterns)
		{
			for (var i = 0; i < powerReadings.Count; i++)
			{
				var reading = powerReadings[i];
				var label = string.IsNullOrWhiteSpace(reading.LabelOrig) ? reading.LabelUser : reading.LabelOrig;
				if (pattern.IsMatch(label.Trim()))
				{
					return i;
				}
			}
		}

		return 0;
	}

	/// <summary>Determines the section of a CPU or GPU overview a reading belongs to.</summary>
	/// <param name="reading">The reading.</param>
	/// <param name="group">The sensor group of the reading.</param>
	public static ReadingCategory CategorizeReading(SensorReading reading, Sensor group)
	{
		if (SensorPatterns.ThrottlingGroups.IsMatch(MatchName(group)) || LabelMatches(SensorPatterns.Throttling, reading))
		{
			return ReadingCategory.Throttling;
		}

		if (LabelMatches(SensorPatterns.MemoryController, reading))
		{
			return ReadingCategory.MemoryController;
		}

		if (LabelMatches(SensorPatterns.Vram, reading))
		{
			return ReadingCategory.Vram;
		}

		if (LabelMatches(SensorPatterns.Limits, reading))
		{
			return ReadingCategory.Limits;
		}

		if (reading.ReadingType == SensorType.Fan || LabelMatches(SensorPatterns.Fans, reading))
		{
			return ReadingCategory.Fans;
		}

		return reading.ReadingType switch
		{
			SensorType.Power => ReadingCategory.Power,
			SensorType.Temp => ReadingCategory.Temperatures,
			SensorType.Usage => ReadingCategory.Usage,
			SensorType.Clock => ReadingCategory.Clocks,
			SensorType.Volt => ReadingCategory.Voltages,
			SensorType.Current => ReadingCategory.Currents,
			_ => ReadingCategory.Other,
		};
	}
}
