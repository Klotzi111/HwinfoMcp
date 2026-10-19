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
			.Group("CPU [#0]: Test CPU", "Mein Prozessor").Reading(Hwinfo.SharedMemory.SensorType.Power, "CPU Package Power", 70, "W", "Leistung")
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
}
