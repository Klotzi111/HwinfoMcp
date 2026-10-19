using Hwinfo.SharedMemory;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using Microsoft.Extensions.Logging;

namespace HwinfoMcp;

/// <summary>
/// <c>HwinfoMcp.exe --dump &lt;file.json&gt; [description]</c>: records the current sensor data to a JSON
/// file, e.g. to add a system to the tests or to report readings that end up in the wrong place.
/// </summary>
public static class DumpCommand
{
	/// <summary>The command line switch that selects this command.</summary>
	public const string Switch = "--dump";

	/// <summary>Runs the command and returns the exit code.</summary>
	/// <param name="args">The arguments after <see cref="Switch"/>.</param>
	/// <param name="loggerFactory">Creates the loggers, which write to stderr.</param>
	public static int Run(string[] args, ILoggerFactory loggerFactory)
	{
		var logger = loggerFactory.CreateLogger(typeof(DumpCommand));
		if (args.Length is < 1 or > 2)
		{
			logger.LogError("Usage: HwinfoMcp {Switch} <file.json> [description]", Switch);
			return 2;
		}

		using var reader = new SharedMemoryReader();
		var source = new HwinfoSensorSource(reader, loggerFactory.CreateLogger<HwinfoSensorSource>());
		var snapshot = source.Read();
		if (!snapshot.IsAvailable)
		{
			logger.LogError("{Message}", Messages.NotAvailable(snapshot));
			return 1;
		}

		var path = Path.GetFullPath(args[0]);
		using (var stream = File.Create(path))
		{
			SensorDump.FromSnapshot(snapshot, args.Length > 1 ? args[1] : null).Save(stream);
		}

		logger.LogInformation(
			"Wrote {Readings} readings of {Sensors} sensor groups to {Path}. Review it before sharing: sensor names "
			+ "can contain serial numbers.",
			snapshot.Readings.Length,
			snapshot.Sensors.Length,
			path
		);
		return 0;
	}
}
