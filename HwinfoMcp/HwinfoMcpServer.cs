using System.Reflection;
using System.Text.Json;
using Hwinfo.SharedMemory;
using HwinfoMcp.Json;
using HwinfoMcp.Measurements;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;

namespace HwinfoMcp;

/// <summary>
/// Registers the MCP server and its services. Shared by the program and the tests, which pick the transport
/// and may register their own <see cref="ISensorSource"/> first.
/// </summary>
public static class HwinfoMcpServer
{
	/// <summary>The name the server reports to MCP clients.</summary>
	public const string Name = "hwinfo-mcp-dotnet";

	/// <summary>The instructions the server gives MCP clients on how to use the tools.</summary>
	public const string Instructions =
		"Read-only access to the live sensor data of HWiNFO on this Windows PC. "
		+ "Use get_cpu, get_gpu and get_memory for curated overviews, and list_sensors with get_readings for "
		+ "anything else (drives, mainboard, fans, network, PresentMon/RTSS framerates, ...). "
		+ "Every call returns current values; call again to see how they change. "
		+ "To measure over a period, call start_measurement with a name and later stop_measurement, then pass the "
		+ "name as measurement to get_cpu, get_gpu, get_memory or get_readings for min, max, median and average. "
		+ "If a tool reports no data or stale data, check hwinfo_status.";

	/// <summary>The version of the server, e.g. "1.2.0".</summary>
	public static string Version { get; } = GetVersion();

	/// <summary>
	/// Adds the MCP server with the HWiNFO tools. Registers the HWiNFO shared memory reader unless an
	/// <see cref="ISensorSource"/> is registered already, and the default settings unless
	/// <see cref="HwinfoServerOptions"/> are.
	/// </summary>
	public static IMcpServerBuilder AddHwinfoMcpServer(this IServiceCollection services)
	{
		services.TryAddSingleton(new HwinfoServerOptions());
		services.TryAddSingleton(TimeProvider.System);
		// One reader for the lifetime of the server: it caches the mapping and is thread-safe
		services.TryAddSingleton(_ => new SharedMemoryReader());
		services.TryAddSingleton<ISensorSource, HwinfoSensorSource>();
		// One recorder for the lifetime of the server: it holds the measurements and their snapshots
		services.TryAddSingleton<MeasurementRecorder>();

		return services
			.AddMcpServer(options =>
			{
				options.ServerInfo = new Implementation { Name = Name, Version = Version };
				options.ServerInstructions = Instructions;
			})
			.WithTools<HwinfoTools>(HwinfoJson.ToolOptions)
			.WithTools<MeasurementTools>(HwinfoJson.ToolOptions)
			.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
			{
				try
				{
					return await next(context, cancellationToken);
				}
				catch (Exception ex) when (ex is JsonException or ArgumentException)
				{
					// The SDK binds the arguments with System.Text.Json before the tool runs, throws an
					// ArgumentException for a missing required one, and would only answer "An error occurred
					// invoking '...'." Name the allowed values instead.
					return ToolArgumentErrors.Create(context, ex);
				}
			}));
	}

	private static string GetVersion()
	{
		var version = typeof(HwinfoMcpServer).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
		// Drop the source revision the SDK appends, e.g. "1.2.0+3f2c1a..."
		var plus = version.IndexOf('+');
		return plus < 0 ? version : version[..plus];
	}
}
