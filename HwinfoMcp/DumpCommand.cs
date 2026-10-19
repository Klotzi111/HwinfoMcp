using Hwinfo.SharedMemory;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using Microsoft.Extensions.Logging;

namespace HwinfoMcp;

/// <summary>
/// <c>HwinfoMcp.exe --dump &lt;file.json&gt; [description]</c>: records the current sensor data to a JSON
/// file, e.g. to add a system to the tests or to report readings that end up in the wrong place.
/// </summary>
public static partial class DumpCommand
{
	/// <summary>The command line switch that selects this command.</summary>
	public const string Switch = "--dump";

	/// <summary>Runs the command against HWiNFO's shared memory and returns the exit code.</summary>
	/// <param name="args">The arguments after <see cref="Switch"/>.</param>
	/// <param name="loggerFactory">Creates the loggers, which write to stderr.</param>
	public static int Run(string[] args, ILoggerFactory loggerFactory)
	{
		using var reader = new SharedMemoryReader();
		var source = new HwinfoSensorSource(reader, loggerFactory.CreateLogger<HwinfoSensorSource>());
		return Run(args, source, loggerFactory.CreateLogger(typeof(DumpCommand)));
	}

	/// <summary>Runs the command and returns the exit code: 0 on success, 1 without data or file, 2 for wrong arguments.</summary>
	/// <param name="args">The arguments after <see cref="Switch"/>.</param>
	/// <param name="source">Where the sensor data comes from.</param>
	/// <param name="logger">Reports the outcome, on stderr.</param>
	internal static int Run(string[] args, ISensorSource source, ILogger logger)
	{
		if (args.Length is < 1 or > 2)
		{
			LogUsage(logger);
			return 2;
		}

		var snapshot = source.Read();
		if (!snapshot.IsAvailable)
		{
			LogNotAvailable(logger, Messages.NotAvailable(snapshot));
			return 1;
		}

		string path;
		try
		{
			path = Path.GetFullPath(args[0]);
			using var stream = File.Create(path);
			SensorDump.FromSnapshot(snapshot, args.Length > 1 ? args[1] : null).Save(stream);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			// A missing folder, a locked file or a bad path is a usage error, not a crash
			LogWriteFailed(logger, args[0], ex.Message);
			return 1;
		}

		LogWritten(logger, snapshot.Readings.Length, snapshot.Sensors.Length, path);
		return 0;
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Usage: HwinfoMcp " + Switch + " <file.json> [description]")]
	private static partial void LogUsage(ILogger logger);

	[LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
	private static partial void LogNotAvailable(ILogger logger, string message);

	[LoggerMessage(Level = LogLevel.Error, Message = "Could not write {Path}: {Reason}")]
	private static partial void LogWriteFailed(ILogger logger, string path, string reason);

	[LoggerMessage(
		Level = LogLevel.Information,
		Message = "Wrote {Readings} readings of {Sensors} sensor groups to {Path}. Review it before sharing: sensor names "
			+ "can contain serial numbers.")]
	private static partial void LogWritten(ILogger logger, int readings, int sensors, string path);
}
