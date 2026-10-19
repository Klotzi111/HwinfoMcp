using HwinfoMcp.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace HwinfoMcp;

/// <summary>
/// The entry point: runs the MCP server over stdio, or the command the first argument selects. While the server
/// runs, stdout belongs to the MCP protocol: everything else, logs included, goes to stderr.
/// </summary>
internal static partial class Program
{
	/// <summary>The command line switch that prints the version.</summary>
	public const string VersionSwitch = "--version";

	/// <summary>The command line switches that print the usage.</summary>
	public static IReadOnlyList<string> HelpSwitches { get; } = ["--help", "-h", "-?", "/?"];

	/// <summary>What --help prints.</summary>
	public const string Usage =
		"""
		MCP server (stdio) that gives MCP clients like Claude read access to HWiNFO's live sensor data.
		MCP clients start it and talk JSON-RPC over stdin and stdout; it isn't meant to be used interactively.

		Usage:
		  HwinfoMcp [--user-labels]                   Run the MCP server.
		  HwinfoMcp --dump <file.json> [description]  Record the current sensor data to a JSON file.
		  HwinfoMcp --version                         Print the version.
		  HwinfoMcp --help                            Print this help.

		Options:
		  --user-labels  Report the names HWiNFO displays (translated, or renamed by you) instead of
		                 HWiNFO's original English names.

		Commands like --dump must be the first argument. Switches are case-insensitive.
		""";

	private static async Task<int> Main(string[] args)
	{
		if (args is [var command, .. var commandArgs])
		{
			if (command.Equals(DumpCommand.Switch, StringComparison.OrdinalIgnoreCase))
			{
				using var loggerFactory = LoggerFactory.Create(ConfigureLogging);
				return DumpCommand.Run(commandArgs, loggerFactory);
			}

			if (command.Equals(VersionSwitch, StringComparison.OrdinalIgnoreCase))
			{
				// Not serving MCP, so stdout is free for the answer
				Console.Out.WriteLine(HwinfoMcpServer.Version);
				return 0;
			}

			if (HelpSwitches.Contains(command, StringComparer.OrdinalIgnoreCase))
			{
				Console.Out.WriteLine($"{HwinfoMcpServer.Name} {HwinfoMcpServer.Version}");
				Console.Out.WriteLine(Usage);
				return 0;
			}
		}

		var serverOptions = HwinfoServerOptions.Parse(args, out var unknownArgs);

		// The empty builder skips the defaults that don't fit a stdio server launched from any working directory:
		// appsettings.json files watched in the current directory, the Windows event log, and logs on stdout.
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		ConfigureLogging(builder.Logging);
		builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
		builder.Services.AddSingleton(serverOptions);
		builder.Services.AddHwinfoMcpServer().WithStdioServerTransport();

		using var host = builder.Build();
		var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HwinfoMcp");
		LogStarted(logger, HwinfoMcpServer.Name, HwinfoMcpServer.Version, serverOptions.Labels);
		foreach (var arg in unknownArgs)
		{
			LogUnknownArgument(logger, arg);
		}

		if (!Console.IsInputRedirected)
		{
			LogInteractiveConsole(logger);
		}

		await host.RunAsync();
		return 0;
	}

	private static void ConfigureLogging(ILoggingBuilder logging)
	{
		logging.SetMinimumLevel(LogLevel.Information);
		// The SDK logs every request at the information level, which only fills the MCP clients' log files
		logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
		logging.AddSimpleConsole(options =>
		{
			options.SingleLine = true;
			options.TimestampFormat = "HH:mm:ss ";
			// MCP clients write stderr to log files, where color codes are just noise
			options.ColorBehavior = LoggerColorBehavior.Disabled;
		});
		logging.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "{Name} {Version} started, reporting {Labels} labels")]
	private static partial void LogStarted(ILogger logger, string name, string version, LabelStyle labels);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring unknown argument {Argument}")]
	private static partial void LogUnknownArgument(ILogger logger, string argument);

	[LoggerMessage(
		Level = LogLevel.Warning,
		Message = "stdin is a console: this is an MCP server that MCP clients start and talk JSON-RPC to over stdio. "
			+ "Configure it in your MCP client instead, see --help.")]
	private static partial void LogInteractiveConsole(ILogger logger);
}
