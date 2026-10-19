using HwinfoMcp.Sensors;

namespace HwinfoMcp.Tests.Fakes;

/// <summary>A sensor source that returns a given snapshot.</summary>
internal sealed class FakeSensorSource(SensorSnapshot snapshot) : ISensorSource
{
	public SensorSnapshot Snapshot { get; set; } = snapshot;

	public int Reads { get; private set; }

	public SensorSnapshot Read()
	{
		Reads++;
		return Snapshot;
	}
}

/// <summary>A clock that stands still, in UTC.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
	public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

	public override DateTimeOffset GetUtcNow()
	{
		return now;
	}
}

/// <summary>A clock that only moves when it is set, in UTC.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
	public DateTimeOffset Now { get; set; } = now;

	public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

	public override DateTimeOffset GetUtcNow()
	{
		return Now;
	}
}
