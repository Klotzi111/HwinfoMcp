using HwinfoMcp.Catalog;
using HwinfoMcp.Measurements;

namespace HwinfoMcp;

/// <summary>The settings of the server, taken from the command line and the environment.</summary>
/// <param name="Labels">Whether the tools report HWiNFO's original names or the ones it displays.</param>
/// <param name="MeasurementMemoryLimit">The most memory the snapshots of all measurements may take, in bytes.</param>
public sealed record HwinfoServerOptions(
	LabelStyle Labels = LabelStyle.Original,
	long MeasurementMemoryLimit = HwinfoServerOptions.DefaultMeasurementMemoryLimit)
{
	/// <summary>The command line switch that makes the tools report the names HWiNFO displays.</summary>
	public const string UserLabelsSwitch = "--user-labels";

	/// <summary>The command line option that sets <see cref="MeasurementMemoryLimit"/>, e.g. "--measurement-memory-limit 2GB".</summary>
	public const string MeasurementMemoryLimitSwitch = "--measurement-memory-limit";

	/// <summary>The environment variable that sets <see cref="MeasurementMemoryLimit"/>. The command line option wins.</summary>
	public const string MeasurementMemoryLimitVariable = "HWINFO_MCP_MEASUREMENT_MEMORY_LIMIT";

	/// <summary>The default of <see cref="MeasurementMemoryLimit"/>: 1 GB.</summary>
	public const long DefaultMeasurementMemoryLimit = 1L << 30;

	/// <summary>
	/// Reads the settings from the command line and the environment, and returns the arguments it doesn't know
	/// and what it ignored because it is invalid. An invalid value leaves the setting as it would be without it.
	/// </summary>
	/// <param name="args">The command line arguments.</param>
	/// <param name="getEnvironmentVariable">Reads an environment variable, e.g. <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
	/// <param name="unknown">The arguments that aren't settings.</param>
	/// <param name="invalid">Why settings were ignored, one message each.</param>
	public static HwinfoServerOptions Parse(
		IReadOnlyList<string> args,
		Func<string, string?> getEnvironmentVariable,
		out IReadOnlyList<string> unknown,
		out IReadOnlyList<string> invalid)
	{
		var options = new HwinfoServerOptions();
		var rest = new List<string>();
		var problems = new List<string>();
		string? limitArgument = null;
		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			if (arg.Equals(UserLabelsSwitch, StringComparison.OrdinalIgnoreCase))
			{
				options = options with { Labels = LabelStyle.User };
			}
			else if (arg.Equals(MeasurementMemoryLimitSwitch, StringComparison.OrdinalIgnoreCase))
			{
				// Another switch right after it means the size is missing
				if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
				{
					limitArgument = args[++i];
				}
				else
				{
					problems.Add($"{MeasurementMemoryLimitSwitch} needs a size, e.g. {MeasurementMemoryLimitSwitch} 2GB.");
				}
			}
			else if (arg.StartsWith(MeasurementMemoryLimitSwitch + "=", StringComparison.OrdinalIgnoreCase))
			{
				limitArgument = arg[(MeasurementMemoryLimitSwitch.Length + 1)..];
			}
			else
			{
				rest.Add(arg);
			}
		}

		// The environment first, so the command line overrides it
		var limitVariable = getEnvironmentVariable(MeasurementMemoryLimitVariable);
		if (!string.IsNullOrWhiteSpace(limitVariable))
		{
			options = WithLimit(options, limitVariable, $"the environment variable {MeasurementMemoryLimitVariable}", problems);
		}

		if (limitArgument is not null)
		{
			options = WithLimit(options, limitArgument, MeasurementMemoryLimitSwitch, problems);
		}

		unknown = rest;
		invalid = problems;
		return options;
	}

	private static HwinfoServerOptions WithLimit(HwinfoServerOptions options, string value, string source, List<string> problems)
	{
		if (ByteSize.TryParse(value, out var bytes))
		{
			return options with { MeasurementMemoryLimit = bytes };
		}

		problems.Add($"Ignoring the measurement memory limit '{value}' of {source}: expected a size like 512MB or 2GB.");
		return options;
	}
}
