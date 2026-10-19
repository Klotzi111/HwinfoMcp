using HwinfoMcp.Catalog;

namespace HwinfoMcp;

/// <summary>The settings of the server, taken from the command line.</summary>
/// <param name="Labels">Whether the tools report HWiNFO's original names or the ones it displays.</param>
public sealed record HwinfoServerOptions(LabelStyle Labels = LabelStyle.Original)
{
	/// <summary>The command line switch that makes the tools report the names HWiNFO displays.</summary>
	public const string UserLabelsSwitch = "--user-labels";

	/// <summary>Reads the settings from the command line and returns the arguments it doesn't know.</summary>
	public static HwinfoServerOptions Parse(IEnumerable<string> args, out IReadOnlyList<string> unknown)
	{
		var options = new HwinfoServerOptions();
		var rest = new List<string>();
		foreach (var arg in args)
		{
			if (arg.Equals(UserLabelsSwitch, StringComparison.OrdinalIgnoreCase))
			{
				options = options with { Labels = LabelStyle.User };
			}
			else
			{
				rest.Add(arg);
			}
		}

		unknown = rest;
		return options;
	}
}
