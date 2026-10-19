using HwinfoMcp.Queries;
using HwinfoMcp.Tests.Fakes;
using static HwinfoMcp.Tests.CpuQueryTests;

namespace HwinfoMcp.Tests;

public sealed class MemoryQueryTests
{
	private static readonly QueryContext s_fresh = QueryContext.From(TestSnapshots.FreshClock);

	[Fact]
	public void Amd_ReportsTimingsDimmsUsageAndController()
	{
		var result = SensorQueries.GetMemory(TestSnapshots.Amd, s_fresh, includeStats: false);

		Assert.Null(result.Note);
		Assert.Equal(
			["Memory Clock", "Memory Clock Ratio", "Tcas", "Trcd", "Trp", "Tras", "Trc", "Trfc", "Command Rate"],
			result.Timings!.Select(value => value.Label)
		);
		Assert.Equal(3100, Find(result.Timings, "Memory Clock").Value);
		Assert.Equal(30, Find(result.Timings, "Tcas").Value);
		Assert.Equal("T", Find(result.Timings, "Trfc").Unit);
		Assert.All(result.Timings!, value => Assert.Null(value.Group));

		Assert.Equal(2, result.Dimms!.Count);
		var dimm = result.Dimms[0];
		Assert.Equal("DDR5 DIMM [#1]: G.Skill F5-6000J3040G32G (P0 CHANNEL A/DIMM 1)", dimm.Name);
		Assert.Equal(48.5, Find(dimm.Readings, "SPD Hub Temperature").Value);
		Assert.Equal(1.37, Find(dimm.Readings, "VDD (SWA) Voltage").Value);

		Assert.Equal(24882, Find(result.Usage, "Physical Memory Used").Value);
		Assert.Equal(38.4, Find(result.Usage, "Physical Memory Load").Value);
		Assert.Equal(0, Find(result.Usage, "Page File Usage").Value);

		var uclk = Find(result.Controller, "Memory Controller Clock (UCLK)");
		Assert.Equal(3100, uclk.Value);
		Assert.Equal("CPU [#0]: AMD Ryzen 9 7900X: Enhanced", uclk.Group);
		Assert.Equal(0.14, Find(result.Controller, "DRAM Write Bandwidth").Value);
	}

	[Fact]
	public void Intel_ReportsDdr5Modules()
	{
		var result = SensorQueries.GetMemory(TestSnapshots.IntelNvidia, s_fresh, includeStats: true);

		Assert.Equal(884, Find(result.Timings, "Trfc").Value);
		var dimms = Assert.IsType<IReadOnlyList<DimmReadings>>(result.Dimms, exactMatch: false);
		Assert.Equal(
			["DDR5 DIMM [#0]: Kingston KF556C36-16 (P0 CHANNEL A/DIMM 1)", "DDR5 DIMM [#2]: Kingston KF556C36-16 (P0 CHANNEL B/DIMM 1)"],
			dimms.Select(dimm => dimm.Name)
		);
		var temperature = Find(dimms[1].Readings, "SPD Hub Temperature");
		Assert.Equal(46, temperature.Value);
		Assert.Equal(23, temperature.Min);
		Assert.Equal(92, temperature.Max);
		Assert.Equal(34.5, temperature.Avg);
	}

	[Fact]
	public void NothingMemoryRelated_ReportsANote()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime).Group("PresentMon").Other("Framerate", 60, "FPS").Build();

		var result = SensorQueries.GetMemory(snapshot, s_fresh, includeStats: false);

		Assert.Equal(Messages.NoMemory, result.Note);
		Assert.Null(result.Timings);
		Assert.Null(result.Dimms);
		Assert.Null(result.Usage);
		Assert.Null(result.Controller);
	}
}
