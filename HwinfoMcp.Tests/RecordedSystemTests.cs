using HwinfoMcp.Catalog;
using HwinfoMcp.Queries;
using HwinfoMcp.Tests.Fakes;
using static HwinfoMcp.Tests.CpuQueryTests;

namespace HwinfoMcp.Tests;

/// <summary>
/// Runs the queries against the recording of a Ryzen 7 7800X3D with an RTX 4080 and HWiNFO set to German.
/// </summary>
public sealed class RecordedSystemTests
{
	private static readonly QueryContext s_original = QueryContext.From(TestSnapshots.FreshClock);
	private static readonly QueryContext s_user = QueryContext.From(TestSnapshots.FreshClock, LabelStyle.User);

	[Fact]
	public void Gpu_Default_IsTheNvidiaCard()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.AmdNvidiaGerman, s_original, GpuSelection.Dgpu, includeStats: false);

		Assert.Null(result.Note);
		var gpu = Assert.Single(result.Gpus);
		// HWiNFO ends the group name with ": ", which is cut off
		Assert.Equal("GPU [#0]: NVIDIA GeForce RTX 4080", gpu.Name);
		Assert.Equal("dgpu", gpu.Kind);

		Assert.Equal(55.26, Find(gpu.Power, "GPU Power").Value);
		Assert.Equal(48.83, Find(gpu.Power, "GPU 16-pin HVPWR Power").Value);
		Assert.Equal(62.97, Find(gpu.Temperatures, "GPU Hot Spot Temperature").Value);
		Assert.Equal(58, Find(gpu.Temperatures, "GPU Memory Junction Temperature").Value);
		Assert.Equal(0, Find(gpu.MemoryController, "GPU Memory Controller Load").Value);
		Assert.Equal(2800.49, Find(gpu.MemoryController, "GPU Memory Clock").Value);
		Assert.Equal(24.14, Find(gpu.Vram, "GPU Memory Usage").Value);
		Assert.Equal(3952.77, Find(gpu.Vram, "GPU Memory Allocated").Value);
		Assert.Equal(17.25, Find(gpu.Limits, "Total GPU Power [% of TDP]").Value);
		Assert.Equal(84, Find(gpu.Limits, "GPU Thermal Limit").Value);
		Assert.Equal(1, Find(gpu.Throttling, "Performance Limit - Utilization").Value);
		Assert.Equal(6, gpu.Throttling!.Count);
		// The fans are reported in RPM and in %
		Assert.Equal(4, gpu.Fans!.Count);
	}

	[Fact]
	public void Gpu_BareAmdRadeon_IsTheIntegratedGpu()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.AmdNvidiaGerman, s_original, GpuSelection.Igpu, includeStats: false);

		var gpu = Assert.Single(result.Gpus);
		Assert.Equal("GPU [#1]: AMD Radeon", gpu.Name);
		Assert.Equal("igpu", gpu.Kind);
		Assert.Equal(50.46, Find(gpu.Temperatures, "GPU Temperature").Value);
	}

	[Fact]
	public void Cpu_Summary_ReportsTheX3dChip()
	{
		var result = SensorQueries.GetCpu(TestSnapshots.AmdNvidiaGerman, s_original, CpuDetail.Summary, includeStats: false);

		var cpu = Assert.Single(result.Cpus);
		Assert.Equal("CPU [#0]: AMD Ryzen 7 7800X3D", cpu.Name);
		Assert.Equal(["CPU [#0]: AMD Ryzen 7 7800X3D", "CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced"], cpu.SensorGroups);
		Assert.Equal(35.9, Find(cpu.Power, "CPU Package Power").Value);
		Assert.Equal(57.63, Find(cpu.Temperatures, "CPU (Tctl/Tdie)").Value);
		Assert.Equal(3000, Find(cpu.MemoryController, "Memory Controller Clock (UCLK)").Value);
		Assert.Equal("Gbps", Find(cpu.MemoryController, "DRAM Read Bandwidth").Unit);
		Assert.Equal(60.69, Find(cpu.Limits, "Thermal Limit").Value);
		Assert.Equal(3, cpu.Throttling!.Count);
		Assert.DoesNotContain(AllValues(cpu), value => SensorPatterns.PerCoreLabel.IsMatch(value.Label));

		Assert.Contains(result.Related!, value => value is { Label: "CPU", Unit: "RPM", Value: 972 });
		Assert.Contains(result.Related!, value => value is { Label: "CPU VDDIO", Unit: "V" });
		Assert.Contains(result.Related!, value => value is { Label: "Memory Clock", Value: 3000, Group: "Memory Timings" });
		Assert.DoesNotContain(result.Related!, value => value.Label == "Framerate");
	}

	[Fact]
	public void Memory_ReportsTimingsDimmsAndUsage()
	{
		var result = SensorQueries.GetMemory(TestSnapshots.AmdNvidiaGerman, s_original, includeStats: false);

		Assert.Equal(884, Find(result.Timings, "Trfc").Value);
		Assert.Equal(2, result.Dimms!.Count);
		Assert.Equal("DDR5 DIMM [#1] (P0 CHANNEL A/DIMMA2)", result.Dimms[0].Name);
		Assert.Equal(42.25, Find(result.Dimms[0].Readings, "SPD Hub Temperature").Value);
		Assert.Equal(12791, Find(result.Usage, "Physical Memory Used").Value);
		Assert.Equal(2000, Find(result.Controller, "Infinity Fabric Clock (FCLK)").Value);
	}

	[Fact]
	public void OriginalLabels_AreEnglishAndUnambiguous()
	{
		var cpu = SensorQueries.GetCpu(TestSnapshots.AmdNvidiaGerman, s_original, CpuDetail.Summary, includeStats: false).Cpus[0];

		// HWiNFO's German translation calls both of these "Gesamte CPU-Auslastung"
		Assert.Equal(5.59, Find(cpu.Usage, "Total CPU Usage").Value);
		Assert.Equal(7.92, Find(cpu.Usage, "Total CPU Utility").Value);
	}

	[Fact]
	public void UserLabels_AreTheOnesHwinfoDisplays()
	{
		var cpu = SensorQueries.GetCpu(TestSnapshots.AmdNvidiaGerman, s_user, CpuDetail.Summary, includeStats: false).Cpus[0];
		var list = SensorQueries.ListSensors(TestSnapshots.AmdNvidiaGerman, s_user);

		Assert.Equal(35.9, Find(cpu.Power, "CPU-Gesamt-Leistungsaufnahme").Value);
		Assert.Equal(3000, Find(cpu.MemoryController, "Takt des Speicher-Controllers (UCLK)").Value);
		Assert.Contains(list.Groups, group => group is { Name: "Speicher-Timings", Category: "memory" });
		Assert.Contains(list.Groups, group => group is { Name: "CPU [#0]: AMD Ryzen 7 7800X3D: C-State Belegung", Category: "cpu" });
	}

	[Fact]
	public void Readings_MatchBothLanguages()
	{
		var english = SensorQueries.GetReadings(TestSnapshots.AmdNvidiaGerman, s_original, "Memory Timings", "Clock", null, false, 100);
		var german = SensorQueries.GetReadings(TestSnapshots.AmdNvidiaGerman, s_original, "Speicher-Timings", "Takt", null, false, 100);

		Assert.Equal("Memory Clock", english.Groups[0].Readings[0].Label);
		Assert.Equal(english.Total, german.Total);
		Assert.Equal("Memory Timings", german.Groups[0].Sensor);
	}

	[Fact]
	public void ListSensors_CategorizesTheGroups()
	{
		var categories = SensorQueries.ListSensors(TestSnapshots.AmdNvidiaGerman, s_original).Groups
			.ToDictionary(group => group.Name, group => group.Category);

		Assert.Equal("dgpu", categories["GPU [#0]: NVIDIA GeForce RTX 4080"]);
		Assert.Equal("igpu", categories["GPU [#1]: AMD Radeon"]);
		Assert.Equal("dimm", categories["DDR5 DIMM [#3] (P0 CHANNEL B/DIMMB2)"]);
		Assert.Equal("system", categories["System: MSI MS-7D75"]);
		Assert.Equal("other", categories["RTSS"]);
	}
}
