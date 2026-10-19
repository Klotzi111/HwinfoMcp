using HwinfoMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

// stdout belongs to the MCP protocol: everything else, logs included, goes to stderr.

if (args is [DumpCommand.Switch, .. var dumpArgs])
{
	using var loggerFactory = LoggerFactory.Create(ConfigureLogging);
	return DumpCommand.Run(dumpArgs, loggerFactory);
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
logger.LogInformation(
	"{Name} {Version} started, reporting {Labels} labels",
	HwinfoMcpServer.Name,
	HwinfoMcpServer.Version,
	serverOptions.Labels
);
foreach (var arg in unknownArgs)
{
	logger.LogWarning("Ignoring unknown argument {Argument}", arg);
}

await host.RunAsync();
return 0;

static void ConfigureLogging(ILoggingBuilder logging)
{
	logging.SetMinimumLevel(LogLevel.Information);
	logging.AddSimpleConsole(options =>
	{
		options.SingleLine = true;
		options.TimestampFormat = "HH:mm:ss ";
		// MCP clients write stderr to log files, where color codes are just noise
		options.ColorBehavior = LoggerColorBehavior.Disabled;
	});
	logging.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
}
