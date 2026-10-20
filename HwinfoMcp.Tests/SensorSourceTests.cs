using Hwinfo.SharedMemory;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HwinfoMcp.Tests;

/// <summary>
/// The real sensor source, with the shared memory read replaced: whatever the read does, the source never throws
/// but reports why there is no data.
/// </summary>
public sealed class SensorSourceTests
{
	[Fact]
	public void Read_ReturnsTheReadings()
	{
		var expected = TestSnapshots.IntelNvidia;
		var source = Source((out readings) =>
		{
			readings = new SensorReadings(expected.PollTime, expected.Readings, expected.Sensors);
			return true;
		});

		var snapshot = source.Read();

		Assert.Equal(SnapshotState.Available, snapshot.State);
		Assert.Equal(expected.PollTime, snapshot.PollTime);
		Assert.Equal(expected.Readings, snapshot.Readings);
		Assert.Equal(expected.Sensors, snapshot.Sensors);
	}

	[Fact]
	public void Read_NothingToRead_IsUnavailable()
	{
		var source = Source((out readings) =>
		{
			readings = new SensorReadings(default, [], []);
			return false;
		});

		Assert.Same(SensorSnapshot.Unavailable, source.Read());
	}

	[Fact]
	public void Read_AccessDenied_FailsWithAHint()
	{
		var source = Source((out _) => throw new UnauthorizedAccessException("Access is denied."));

		var snapshot = source.Read();

		Assert.Equal(SnapshotState.Failed, snapshot.State);
		Assert.Contains("same user", snapshot.Error, StringComparison.Ordinal);
		Assert.Contains(Messages.SharedMemorySetting, Messages.NotAvailable(snapshot), StringComparison.Ordinal);
	}

	// What SharedMemoryReader.TryReadLocal throws, according to its documentation
	[Theory]
	[InlineData(typeof(InvalidDataException))]
	[InlineData(typeof(TimeoutException))]
	[InlineData(typeof(InvalidOperationException))]
	[InlineData(typeof(ObjectDisposedException))]
	public void Read_Throws_FailsWithTheReason(Type errorType)
	{
		var error = (Exception)Activator.CreateInstance(errorType, "Simulated read error")!;
		var source = Source((out _) => throw error);

		var snapshot = source.Read();

		Assert.Equal(SnapshotState.Failed, snapshot.State);
		Assert.Empty(snapshot.Readings);
		Assert.Contains(error.Message, snapshot.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_FailingAgain_WarnsOnce_UntilItIsReadableAgain()
	{
		// While a measurement runs, the source is read four times a second
		var fail = true;
		var expected = TestSnapshots.IntelNvidia;
		var logger = new ListLogger<HwinfoSensorSource>();
		var source = new HwinfoSensorSource(
			(out readings) =>
			{
				if (fail)
				{
					throw new TimeoutException("HWiNFO's mutex is busy.");
				}

				readings = new SensorReadings(expected.PollTime, expected.Readings, expected.Sensors);
				return true;
			},
			logger
		);

		source.Read();
		source.Read();
		source.Read();
		Assert.Equal([LogLevel.Warning, LogLevel.Debug, LogLevel.Debug], logger.Levels);

		fail = false;
		source.Read();
		fail = true;
		source.Read();
		Assert.Equal([LogLevel.Warning, LogLevel.Debug, LogLevel.Debug, LogLevel.Information, LogLevel.Warning], logger.Levels);
	}

	[Fact]
	public void Read_OutOfMemory_IsNotSwallowed()
	{
		var source = Source((out _) => throw new OutOfMemoryException());

		Assert.Throws<OutOfMemoryException>(source.Read);
	}

	private static HwinfoSensorSource Source(HwinfoSensorSource.TryReadReadings tryRead)
	{
		return new HwinfoSensorSource(tryRead, NullLogger<HwinfoSensorSource>.Instance);
	}
}
