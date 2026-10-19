using Hwinfo.SharedMemory;
using Microsoft.Extensions.Logging;

namespace HwinfoMcp.Sensors;

/// <summary>
/// Reads the sensor data of the local HWiNFO instance from its shared memory. Read-only: it neither
/// writes to the shared memory nor starts or controls HWiNFO.
/// </summary>
/// <param name="reader">The shared reader. It caches the mapping, so it must live as long as the server.</param>
/// <param name="logger">Logs read failures.</param>
public sealed class HwinfoSensorSource(SharedMemoryReader reader, ILogger<HwinfoSensorSource> logger) : ISensorSource
{
	/// <inheritdoc />
	public SensorSnapshot Read()
	{
		try
		{
			if (reader.TryReadLocal(out var readings))
			{
				return SensorSnapshot.FromReadings(readings);
			}

			logger.LogDebug("HWiNFO shared memory is not available");
			return SensorSnapshot.Unavailable;
		}
		catch (UnauthorizedAccessException ex)
		{
			logger.LogWarning(ex, "Access to the HWiNFO shared memory was denied");
			return SensorSnapshot.Failed(
				"Access to HWiNFO's shared memory was denied. Run HWiNFO and this server under the same user."
			);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			// The reader throws for sections it cannot parse, for a busy mutex and once disposed. None of
			// that may take the server down: the tool result reports it instead.
			logger.LogWarning(ex, "Reading the HWiNFO shared memory failed");
			return SensorSnapshot.Failed($"Reading HWiNFO's shared memory failed: {ex.Message}");
		}
	}
}
