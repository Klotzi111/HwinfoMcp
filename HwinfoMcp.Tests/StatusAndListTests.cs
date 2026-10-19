using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;

namespace HwinfoMcp.Tests;

public sealed class StatusAndListTests
{
	[Fact]
	public void Status_FreshData()
	{
		var result = SensorQueries.GetStatus(TestSnapshots.Amd, QueryContext.From(TestSnapshots.FreshClock));

		Assert.True(result.Available);
		Assert.Equal("2026-10-19T08:00:00+00:00", result.PollTime);
		Assert.Equal(2, result.AgeSeconds);
		Assert.False(result.Stale);
		Assert.Equal(25, result.SensorGroups);
		Assert.Equal(470, result.Readings);
		Assert.Null(result.Warning);
		Assert.Null(result.Message);
	}

	[Theory]
	[InlineData(61, "61 seconds")]
	[InlineData(600, "10 minutes")]
	[InlineData(12.5 * 3600, "12.5 hours")]
	public void Status_StaleData_WarnsAboutTheFreeVersionLimit(double ageSeconds, string expectedAge)
	{
		var context = new QueryContext(TestSnapshots.PollTime.AddSeconds(ageSeconds), TimeZoneInfo.Utc);

		var result = SensorQueries.GetStatus(TestSnapshots.Amd, context);

		Assert.True(result.Available);
		Assert.True(result.Stale);
		Assert.Contains(expectedAge, result.Warning, StringComparison.Ordinal);
		Assert.Contains("12-hour", result.Warning, StringComparison.Ordinal);
		Assert.Contains(Messages.SharedMemorySetting, result.Warning, StringComparison.Ordinal);
	}

	[Fact]
	public void Status_DataExactlyAtTheThreshold_IsNotStale()
	{
		var context = new QueryContext(TestSnapshots.PollTime.AddSeconds(60), TimeZoneInfo.Utc);

		Assert.False(SensorQueries.GetStatus(TestSnapshots.Amd, context).Stale);
	}

	[Fact]
	public void Status_ClockBehindHwinfo_ReportsAgeZero()
	{
		var context = new QueryContext(TestSnapshots.PollTime.AddSeconds(-3), TimeZoneInfo.Utc);

		Assert.Equal(0, SensorQueries.GetStatus(TestSnapshots.Amd, context).AgeSeconds);
	}

	[Fact]
	public void Status_ShowsThePollTimeInTheLocalTimeZone()
	{
		var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test");
		var context = new QueryContext(TestSnapshots.PollTime, zone);

		Assert.Equal("2026-10-19T10:00:00+02:00", SensorQueries.GetStatus(TestSnapshots.Amd, context).PollTime);
	}

	[Fact]
	public void Status_HwinfoNotRunning_ExplainsHowToEnableSharedMemory()
	{
		var result = SensorQueries.GetStatus(SensorSnapshot.Unavailable, QueryContext.From(TestSnapshots.FreshClock));

		Assert.False(result.Available);
		Assert.Null(result.PollTime);
		Assert.Contains(Messages.SharedMemorySetting, result.Message, StringComparison.Ordinal);
		Assert.Contains("12 hours", result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Status_ReadFailed_ReportsTheError()
	{
		var result = SensorQueries.GetStatus(SensorSnapshot.Failed("Broken section."), QueryContext.From(TestSnapshots.FreshClock));

		Assert.False(result.Available);
		Assert.StartsWith("Broken section.", result.Message, StringComparison.Ordinal);
		Assert.Contains(Messages.SharedMemorySetting, result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ListSensors_CategorizesEveryGroup()
	{
		var result = SensorQueries.ListSensors(TestSnapshots.Amd, QueryContext.From(TestSnapshots.FreshClock));

		Assert.Equal(25, result.SensorGroups);
		Assert.Equal(470, result.Readings);
		Assert.Equal(25, result.Groups.Count);

		var categories = result.Groups.ToDictionary(group => group.Name, group => group.Category);
		Assert.Equal("system", categories["System: ASUS"]);
		Assert.Equal("cpu", categories["CPU [#0]: AMD Ryzen 9 7900X"]);
		Assert.Equal("cpu", categories["CPU [#0]: AMD Ryzen 9 7900X: C-State Residency"]);
		Assert.Equal("memory", categories["Memory Timings"]);
		Assert.Equal("dimm", categories["DDR5 DIMM [#3]: G.Skill F5-6000J3040G32G (P0 CHANNEL B/DIMM 1)"]);
		Assert.Equal("dgpu", categories["dGPU [#0]: AMD Radeon RX 7800 XT: ASUS TUF RX 7800 XT GAMING OC"]);
		Assert.Equal("igpu", categories["iGPU [#1]: AMD Radeon 610M"]);
		Assert.Equal("other", categories["PresentMon [dwm.exe]"]);
		Assert.Equal("other", categories["ASUS TUF GAMING X670E-PLUS WIFI (Nuvoton NCT6799D)"]);
	}

	[Fact]
	public void ListSensors_CountsReadingsPerType()
	{
		var result = SensorQueries.ListSensors(TestSnapshots.Amd, QueryContext.From(TestSnapshots.FreshClock));

		var timings = Assert.Single(result.Groups, group => group.Name == "Memory Timings");
		Assert.Equal(9, timings.Readings);
		Assert.Equal(new Dictionary<string, int> { ["Clock"] = 1, ["Other"] = 8 }, timings.Types);

		// Groups without readings are listed too
		var embeddedController = Assert.Single(result.Groups, group => group.Name == "ASUS EC: ASUS");
		Assert.Equal(0, embeddedController.Readings);
		Assert.Empty(embeddedController.Types);
	}

	[Fact]
	public void ListSensors_StaleData_IsFlagged()
	{
		var context = new QueryContext(TestSnapshots.PollTime.AddMinutes(5), TimeZoneInfo.Utc);

		Assert.NotNull(SensorQueries.ListSensors(TestSnapshots.Amd, context).Warning);
	}
}
