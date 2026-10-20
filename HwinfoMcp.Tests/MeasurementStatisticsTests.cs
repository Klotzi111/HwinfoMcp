using Hwinfo.SharedMemory;
using HwinfoMcp.Measurements;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;

namespace HwinfoMcp.Tests;

public sealed class MeasurementStatisticsTests
{
	[Theory]
	[InlineData(new double[] { 3 }, 3)]
	[InlineData(new double[] { 2, 1 }, 1.5)]
	[InlineData(new double[] { 5, 1, 3 }, 3)]
	[InlineData(new double[] { 4, 1, 3, 2 }, 2.5)]
	[InlineData(new double[] { 7, 7, 7, 7, 7 }, 7)]
	[InlineData(new double[] { 0, 0, 0, 100 }, 0)]
	[InlineData(new double[] { -5, 10, -5, 10 }, 2.5)]
	public void Median_IsTheMiddleValueOrTheMeanOfTheTwoMiddleValues(double[] values, double expected)
	{
		Assert.Equal(expected, MeasurementStatistics.Median(values));
	}

	[Fact]
	public void Median_OfNothing_IsNaN()
	{
		Assert.True(double.IsNaN(MeasurementStatistics.Median([])));
	}

	[Fact]
	public void Median_MatchesSorting()
	{
		var random = new Random(42);
		for (var length = 1; length < 300; length++)
		{
			// Few distinct values, like sensors that report the same value again and again
			var values = Enumerable.Range(0, length).Select(_ => (double)random.Next(0, 10)).ToArray();
			var sorted = values.Order().ToArray();
			var expected = length % 2 == 1
				? sorted[length / 2]
				: (sorted[(length / 2) - 1] / 2) + (sorted[length / 2] / 2);

			Assert.Equal(expected, MeasurementStatistics.Median(values));
		}
	}

	[Fact]
	public void Median_OfManyEqualValues_IsFast()
	{
		var values = Enumerable.Repeat(1500.0, 1_000_000).ToArray();

		Assert.Equal(1500, MeasurementStatistics.Median(values));
	}

	// Sensors often rise or fall steadily, e.g. a temperature while a game loads or a battery while it drains and
	// charges. Pivots from fixed positions take quadratic time on these; this test then runs for minutes.
	[Theory]
	[InlineData("rising")]
	[InlineData("falling")]
	[InlineData("rising, then falling")]
	[InlineData("falling, then rising")]
	[InlineData("stepwise falling, then rising")]
	public void Median_OfSteadilyChangingValues_IsFast(string shape)
	{
		const int Length = 400_001;
		var values = Enumerable.Range(0, Length).Select(i => Shape(shape, i, Length)).ToArray();
		var sorted = values.Order().ToArray();
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		var median = MeasurementStatistics.Median(values);

		Assert.Equal(sorted[Length / 2], median);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
	}

	[Fact]
	public void Evaluate_ReadingsAtATime_OverChangingLayouts_GivesTheSameResult()
	{
		var random = new Random(11);
		var full = new SnapshotLayout(TestSnapshots.Amd);
		// HWiNFO dropped every third reading for a while
		var reduced = new SnapshotLayout(TestSnapshots.Amd with { Readings = [.. TestSnapshots.Amd.Readings.Where((_, i) => i % 3 != 0)] });
		var snapshots = Enumerable.Range(0, 60)
			.Select(i =>
			{
				var layout = i is >= 20 and < 40 ? reduced : full;
				return new StoredSnapshot(
					i,
					TestSnapshots.PollTime.AddSeconds(i),
					layout,
					[.. layout.Readings.Select(reading => reading.Value + random.Next(-20, 20))]
				);
			})
			.ToList();

		var atOnce = MeasurementStatistics.Evaluate(snapshots);
		var oneByOne = MeasurementStatistics.Evaluate(snapshots, maxBufferedValues: 1);
		var someAtATime = MeasurementStatistics.Evaluate(snapshots, maxBufferedValues: 1000);

		Assert.Equal(TestSnapshots.Amd.Readings.Length, atOnce.Readings.Length);
		Assert.Equal(atOnce.Stats, oneByOne.Stats);
		Assert.Equal(atOnce.Stats, someAtATime.Stats);
		Assert.Contains(atOnce.Stats, stats => stats.Samples == 40);
		Assert.Contains(atOnce.Stats, stats => stats.Samples == 60);
	}

	[Fact]
	public void Evaluate_ReadingListedTwice_KeepsBothApartAcrossLayouts()
	{
		var sensor = new Sensor(1, 0, "Sensor", "Sensor");
		var before = Snapshot(sensor, (7, "A", 1, "W"), (7, "B", 100, "W"));
		var after = Snapshot(sensor, (7, "A", 2, "W"), (7, "B", 200, "W"), (8, "X", 5, "W"));

		var measured = MeasurementStatistics.Evaluate([Stored(0, before), Stored(1, after)]);

		Assert.Equal(["A", "B", "X"], measured.Readings.Select(reading => reading.LabelOrig));
		Assert.Equal([1.5, 150, 5], measured.Stats.Select(stats => stats.Median));
		Assert.Equal([2, 2, 1], measured.Stats.Select(stats => stats.Samples));
	}

	[Fact]
	public void Evaluate_ReadingInAnotherUnit_IsAReadingOfItsOwn()
	{
		// The user switched HWiNFO from °C to °F during the measurement
		var sensor = new Sensor(1, 0, "Sensor", "Sensor");
		var celsius = Snapshot(sensor, (7, "CPU", 60, "°C"));
		var fahrenheit = Snapshot(sensor, (7, "CPU", 140, "°F"));

		var measured = MeasurementStatistics.Evaluate([Stored(0, celsius), Stored(1, fahrenheit)]);

		Assert.Equal(["°C", "°F"], measured.Readings.Select(reading => reading.Unit));
		Assert.Equal([60.0, 140], measured.Stats.Select(stats => stats.Max));
	}

	[Fact]
	public void Evaluate_ReadingsAtATime_GivesTheSameResult()
	{
		var random = new Random(7);
		var layout = new SnapshotLayout(TestSnapshots.Amd);
		var snapshots = Enumerable.Range(0, 50)
			.Select(i => new StoredSnapshot(
				i,
				TestSnapshots.PollTime.AddSeconds(i),
				layout,
				[.. TestSnapshots.Amd.Readings.Select(reading => reading.Value + random.Next(-20, 20))]
			))
			.ToList();

		var atOnce = MeasurementStatistics.Evaluate(snapshots);
		var oneByOne = MeasurementStatistics.Evaluate(snapshots, maxBufferedValues: 1);

		Assert.Equal(TestSnapshots.Amd.Readings.Length, atOnce.Readings.Length);
		Assert.Equal(atOnce.Stats, oneByOne.Stats);
		Assert.All(atOnce.Stats, stats => Assert.InRange(stats.Median, stats.Min, stats.Max));
	}

	[Fact]
	public void Evaluate_KeepsTheLatestNamesOfReadingsAndGroups()
	{
		var before = new SnapshotBuilder(TestSnapshots.PollTime).Group("CPU [#0]: Test CPU").Power("CPU Package Power", 50).Build();
		var after = new SnapshotBuilder(TestSnapshots.PollTime.AddSeconds(2))
			.Group("CPU [#0]: Test CPU", "Mein Prozessor").Reading(SensorType.Power, "CPU Package Power", 70, "W", "Leistung")
			.Build();

		var measured = MeasurementStatistics.Evaluate([Stored(0, before), Stored(1, after)]);

		var reading = Assert.Single(measured.Readings);
		Assert.Equal("Leistung", reading.LabelUser);
		Assert.Equal("Mein Prozessor", Assert.Single(measured.Sensors).NameUser);
		Assert.Same(measured.Sensors[0], reading.Sensor);
		Assert.Equal(2, measured.Stats[0].Samples);
	}

	private static StoredSnapshot Stored(long sequence, SensorSnapshot snapshot)
	{
		return new StoredSnapshot(sequence, snapshot.PollTime, new SnapshotLayout(snapshot), [.. snapshot.Readings.Select(reading => reading.Value)]);
	}

	/// <summary>A snapshot of one sensor group with readings whose IDs are given, so they can repeat.</summary>
	private static SensorSnapshot Snapshot(Sensor sensor, params (uint Id, string Label, double Value, string Unit)[] readings)
	{
		return new SensorSnapshot(
			SnapshotState.Available,
			TestSnapshots.PollTime,
			[.. readings.Select(reading => new SensorReading(reading.Id, SensorType.Other, reading.Label, reading.Label, reading.Unit, reading.Value, 0, 0, 0, sensor))],
			[sensor]
		);
	}

	private static double Shape(string shape, int i, int length)
	{
		var half = length / 2;
		return shape switch
		{
			"rising" => i,
			"falling" => length - i,
			"rising, then falling" => i < half ? i : length - i,
			"falling, then rising" => i < half ? half - i : i - half,
			// Like a battery's capacity in percent, which stays the same for many polls
			"stepwise falling, then rising" => Math.Floor((i < half ? half - i : i - half) / 13.0),
			_ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
		};
	}
}
