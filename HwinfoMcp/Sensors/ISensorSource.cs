namespace HwinfoMcp.Sensors;

/// <summary>
/// Provides the current sensor data. Abstracts HWiNFO's shared memory so the query logic can be tested
/// with recorded or hand-made data.
/// </summary>
public interface ISensorSource
{
	/// <summary>
	/// Reads the current sensor data. Never throws for a missing or unreadable HWiNFO; that is reported
	/// through <see cref="SensorSnapshot.State"/> instead.
	/// </summary>
	SensorSnapshot Read();
}
