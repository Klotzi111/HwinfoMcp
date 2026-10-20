using Hwinfo.SharedMemory;
using Microsoft.Extensions.Logging;

namespace HwinfoMcp.Sensors;

/// <summary>
/// Reads the sensor data of the local HWiNFO instance from its shared memory. Read-only: it neither
/// writes to the shared memory nor starts or controls HWiNFO.
/// </summary>
public sealed partial class HwinfoSensorSource : ISensorSource
{
	private readonly TryReadReadings _tryRead;
	private readonly ILogger<HwinfoSensorSource> _logger;

	// The error of the last read, if it failed: an error that persists is only warned about once, rather than on
	// every read of a running measurement
	private string? _lastError;

	/// <summary>Creates the source.</summary>
	/// <param name="reader">The shared reader. It caches the mapping, so it must live as long as the server.</param>
	/// <param name="logger">Logs read failures.</param>
	public HwinfoSensorSource(SharedMemoryReader reader, ILogger<HwinfoSensorSource> logger)
		: this(reader.TryReadLocal, logger)
	{
	}

	/// <summary>Creates the source with another way to read, so tests can make the read fail.</summary>
	/// <param name="tryRead">Reads like <see cref="SharedMemoryReader.TryReadLocal"/>.</param>
	/// <param name="logger">Logs read failures.</param>
	internal HwinfoSensorSource(TryReadReadings tryRead, ILogger<HwinfoSensorSource> logger)
	{
		_tryRead = tryRead;
		_logger = logger;
	}

	/// <summary>Reads like <see cref="SharedMemoryReader.TryReadLocal"/>: false if there is nothing to read.</summary>
	internal delegate bool TryReadReadings(out SensorReadings readings);

	/// <inheritdoc />
	public SensorSnapshot Read()
	{
		try
		{
			if (_tryRead(out var readings))
			{
				if (Interlocked.Exchange(ref _lastError, null) is not null)
				{
					LogReadableAgain(_logger);
				}

				return SensorSnapshot.FromReadings(readings);
			}

			Interlocked.Exchange(ref _lastError, null);
			LogUnavailable(_logger);
			return SensorSnapshot.Unavailable;
		}
		catch (UnauthorizedAccessException ex)
		{
			if (IsNewError(ex))
			{
				LogAccessDenied(_logger, ex);
			}

			return SensorSnapshot.Failed(
				"Access to HWiNFO's shared memory was denied. Run HWiNFO and this server under the same user."
			);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// The reader throws for sections it cannot parse, for a busy mutex and once disposed. None of
			// that may take the server down: the tool result reports it instead.
			if (IsNewError(ex))
			{
				LogReadFailed(_logger, ex);
			}

			return SensorSnapshot.Failed($"Reading HWiNFO's shared memory failed: {ex.Message}");
		}
	}

	/// <summary>Whether the error differs from that of the last read. Logs a repeated one at the debug level.</summary>
	private bool IsNewError(Exception exception)
	{
		var error = $"{exception.GetType().FullName}: {exception.Message}";
		if (Interlocked.Exchange(ref _lastError, error) != error)
		{
			return true;
		}

		LogStillFailing(_logger, error);
		return false;
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "HWiNFO shared memory is not available")]
	private static partial void LogUnavailable(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Access to the HWiNFO shared memory was denied")]
	private static partial void LogAccessDenied(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Reading the HWiNFO shared memory failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Reading the HWiNFO shared memory failed again: {Error}")]
	private static partial void LogStillFailing(ILogger logger, string error);

	[LoggerMessage(Level = LogLevel.Information, Message = "The HWiNFO shared memory is readable again")]
	private static partial void LogReadableAgain(ILogger logger);
}
