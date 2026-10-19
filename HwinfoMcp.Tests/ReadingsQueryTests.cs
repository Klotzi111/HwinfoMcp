using Hwinfo.SharedMemory;
using HwinfoMcp.Queries;
using HwinfoMcp.Tests.Fakes;

namespace HwinfoMcp.Tests;

public sealed class ReadingsQueryTests
{
	private static readonly QueryContext s_fresh = QueryContext.From(TestSnapshots.FreshClock);

	[Fact]
	public void NoFilter_IsLimitedAndSaysSo()
	{
		var result = Query();

		Assert.Equal(470, result.Total);
		Assert.Equal(SensorQueries.DefaultReadingLimit, result.Returned);
		Assert.True(result.Truncated);
		Assert.Equal(SensorQueries.DefaultReadingLimit, result.Groups.Sum(group => group.Readings.Count));
		Assert.Contains("Showing the first 100 of 470", result.Hint, StringComparison.Ordinal);
	}

	[Fact]
	public void SensorFilter_IsACaseInsensitiveSubstring()
	{
		var result = Query(sensor: "enhanced");

		var group = Assert.Single(result.Groups);
		Assert.Equal("CPU [#0]: AMD Ryzen 9 7900X: Enhanced", group.Sensor);
		Assert.False(result.Truncated);
		Assert.Null(result.Hint);
		Assert.Equal(64, result.Total);
	}

	[Fact]
	public void SensorFilter_MatchesTheUserDefinedNameToo()
	{
		var result = Query(sensor: "dwm.exe");

		Assert.Equal("PresentMon [dwm.exe]", Assert.Single(result.Groups).Sensor);
	}

	[Fact]
	public void LabelFilter_SpansGroups()
	{
		var result = Query(label: "HOT SPOT");

		Assert.Equal(["dGPU [#0]: AMD Radeon RX 7800 XT: ASUS TUF RX 7800 XT GAMING OC"], result.Groups.Select(group => group.Sensor));
		Assert.Equal(51, Assert.Single(result.Groups[0].Readings).Value);
	}

	[Fact]
	public void Filters_AreCombined()
	{
		var result = Query(sensor: "dGPU", types: [SensorType.Temp, SensorType.Power]);

		var readings = Assert.Single(result.Groups).Readings;
		Assert.Equal(23, readings.Count);
		Assert.All(readings, reading => Assert.Contains(reading.Unit, new[] { "°C", "W" }));
	}

	[Fact]
	public void IncludeStats_AddsMinMaxAndAverage()
	{
		var result = Query(sensor: "Enhanced", label: "CPU Package Power", includeStats: true);

		var value = Assert.Single(Assert.Single(result.Groups).Readings);
		Assert.Equal(61.65, value.Value);
		Assert.NotNull(value.Min);
		Assert.NotNull(value.Max);
		Assert.NotNull(value.Avg);
		Assert.Null(value.Group);
	}

	[Fact]
	public void WithoutStats_MinMaxAndAverageAreLeftOut()
	{
		var result = Query(sensor: "Memory Timings");

		Assert.All(result.Groups.SelectMany(group => group.Readings), value =>
		{
			Assert.Null(value.Min);
			Assert.Null(value.Max);
			Assert.Null(value.Avg);
		});
	}

	[Fact]
	public void NoMatch_ExplainsWhatToDo()
	{
		var result = Query(sensor: "does not exist");

		Assert.Equal(0, result.Total);
		Assert.Empty(result.Groups);
		Assert.Equal(Messages.NoReadingMatched, result.Hint);
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(-5, 1)]
	[InlineData(5000, 470)]
	[InlineData(470, 470)]
	public void Limit_IsClamped(int limit, int expectedReturned)
	{
		Assert.Equal(expectedReturned, Query(limit: limit).Returned);
	}

	[Fact]
	public void EmptyUnits_AreLeftOut()
	{
		var result = Query(label: "Average Active Core Count");

		Assert.Null(Assert.Single(Assert.Single(result.Groups).Readings).Unit);
	}

	[Theory]
	[InlineData(1.125, 1.13)]
	[InlineData(3566.6666666666665, 3566.67)]
	[InlineData(-0.001, 0.0)]
	[InlineData(double.NaN, null)]
	[InlineData(double.PositiveInfinity, null)]
	public void Values_AreRoundedToTwoDecimals(double value, double? expected)
	{
		Assert.Equal(expected, SensorQueries.Round(value));
	}

	private static ReadingsResult Query(
		string? sensor = null,
		string? label = null,
		SensorType[]? types = null,
		bool includeStats = false,
		int limit = SensorQueries.DefaultReadingLimit)
	{
		return SensorQueries.GetReadings(TestSnapshots.Amd, s_fresh, sensor, label, types, includeStats, limit);
	}
}
