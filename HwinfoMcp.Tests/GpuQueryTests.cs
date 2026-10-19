using HwinfoMcp.Queries;
using HwinfoMcp.Tests.Fakes;
using static HwinfoMcp.Tests.CpuQueryTests;

namespace HwinfoMcp.Tests;

public sealed class GpuQueryTests
{
	private static readonly QueryContext s_fresh = QueryContext.From(TestSnapshots.FreshClock);

	[Fact]
	public void Amd_Default_ReportsTheDiscreteGpu()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.Amd, s_fresh, GpuSelection.Dgpu, includeStats: false);

		Assert.Equal("dgpu", result.Requested);
		Assert.Null(result.Note);
		var gpu = Assert.Single(result.Gpus);
		Assert.Equal("dGPU [#0]: AMD Radeon RX 7800 XT: ASUS TUF RX 7800 XT GAMING OC", gpu.Name);
		Assert.Equal("dgpu", gpu.Kind);

		// Only the main power, the board power
		var power = Assert.Single(gpu.Power!);
		Assert.Equal("Total Board Power (TBP)", power.Label);
		Assert.Equal(50.87, power.Value);

		// Temperatures including hotspot and memory junction
		Assert.Equal(41, Find(gpu.Temperatures, "GPU Temperature").Value);
		Assert.Equal(51, Find(gpu.Temperatures, "GPU Hot Spot Temperature").Value);
		Assert.Equal(62, Find(gpu.Temperatures, "GPU Memory Junction Temperature").Value);

		// Usage and memory controller
		Assert.Equal(1, Find(gpu.Usage, "GPU Utilization").Value);
		Assert.Equal(0, Find(gpu.MemoryController, "GPU Memory Controller Utilization").Value);
		Assert.Equal(909, Find(gpu.MemoryController, "GPU Memory Clock").Value);

		// VRAM
		Assert.Equal(7624, Find(gpu.Vram, "GPU Memory Usage").Value);
		Assert.Equal("MB", Find(gpu.Vram, "GPU D3D Memory Dedicated").Unit);

		Assert.All(AllValues(gpu), value => Assert.Null(value.Group));

		// The rarely needed sections are left out, but named
		Assert.Null(gpu.Voltages);
		Assert.Null(gpu.Currents);
		Assert.Null(gpu.Limits);
		Assert.Null(gpu.Fans);
		Assert.Null(gpu.Other);
		Assert.Equal(["power", "limits", "fans", "voltages", "currents", "other"], gpu.Omitted);
	}

	[Fact]
	public void IncludePower_ReportsAllPowerReadings()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.Amd, s_fresh, GpuSelection.Dgpu, includeStats: false, [GpuSection.Power]);
		var gpu = Assert.Single(result.Gpus);

		Assert.Equal(9, gpu.Power!.Count);
		Assert.Equal(50.87, Find(gpu.Power, "Total Board Power (TBP)").Value);
		Assert.Equal(29.48, Find(gpu.Power, "Total Graphics Power (TGP)").Value);
		Assert.Equal(0.86, Find(gpu.Power, "GPU Core Power (VDDCR_GFX)").Value);
		Assert.Equal(["limits", "fans", "voltages", "currents", "other"], gpu.Omitted);
	}

	[Theory]
	// NVIDIA and Intel: the card's power, wherever HWiNFO lists it
	[InlineData("GPU Power", "GPU Core (NVVDD) Input Power (sum)", "GPU Power")]
	// AMD: board power before chip power
	[InlineData("Total Graphics Power (TGP)", "Total Board Power (TBP)", "Total Board Power (TBP)")]
	[InlineData("GPU Core Power (VDDCR_GFX)", "Total Graphics Power (TGP)", "Total Graphics Power (TGP)")]
	[InlineData("GPU SoC Power (VDDCR_SOC)", "GPU ASIC Power", "GPU ASIC Power")]
	// No known main power: the first one
	[InlineData("GPU Rail A Power", "GPU Rail B Power", "GPU Rail A Power")]
	public void MainPower_IsPickedByLabel(string first, string second, string expected)
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime)
			.Group("GPU [#0]: Some GPU")
			.Power(first, 10)
			.Power(second, 20)
			.Build();

		var gpu = Assert.Single(SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Dgpu, includeStats: false).Gpus);

		Assert.Equal(expected, Assert.Single(gpu.Power!).Label);
		Assert.Equal(["power"], gpu.Omitted);
	}

	[Fact]
	public void MainPower_IsPickedByTheOriginalLabel()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime)
			.Group("GPU [#0]: NVIDIA GeForce RTX 4080")
			.Reading(Hwinfo.SharedMemory.SensorType.Power, "GPU Core (NVVDD) Input Power (sum)", 30, "W", "GPU Core (NVVDD) Eingang Energieverbrauch (sum)")
			.Reading(Hwinfo.SharedMemory.SensorType.Power, "GPU Power", 55, "W", "GPU Energieverbrauch")
			.Build();
		var context = QueryContext.From(TestSnapshots.FreshClock, Catalog.LabelStyle.User);

		var gpu = Assert.Single(SensorQueries.GetGpu(snapshot, context, GpuSelection.Dgpu, includeStats: false).Gpus);

		Assert.Equal("GPU Energieverbrauch", Assert.Single(gpu.Power!).Label);
	}

	[Fact]
	public void Include_AddsTheRequestedSections()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.Amd, s_fresh, GpuSelection.Dgpu, includeStats: false, [GpuSection.Limits, GpuSection.Fans]);
		var gpu = Assert.Single(result.Gpus);

		// Limits: AMD reports some both as absolute value and as utilization
		Assert.Contains(gpu.Limits!, value => value is { Label: "GPU PPT Limit (Sustained)", Unit: "W", Value: 228 });
		Assert.Contains(gpu.Limits!, value => value is { Label: "GPU PPT Limit (Sustained)", Unit: "%", Value: 12.94 });
		Assert.Contains(gpu.Limits!, value => value is { Label: "GPU Hotspot Thermal Limit", Unit: "%" });
		Assert.Equal(0, Find(gpu.Fans, "GPU Fan").Value);
		Assert.Equal("%", Find(gpu.Fans, "GPU Fan PWM").Unit);

		Assert.Null(gpu.Voltages);
		Assert.Null(gpu.Other);
		Assert.Equal(["power", "voltages", "currents", "other"], gpu.Omitted);
	}

	[Fact]
	public void IncludeAll_LeavesNothingOut()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.Amd, s_fresh, GpuSelection.Dgpu, includeStats: false, [GpuSection.All]);
		var gpu = Assert.Single(result.Gpus);

		Assert.Equal(9, gpu.Power!.Count);
		Assert.Equal(12.61, Find(gpu.Currents, "GPU Memory Current (VDDIO)").Value);
		Assert.Equal(1.35, Find(gpu.Voltages, "GPU Memory Voltage (VDDIO)").Value);
		Assert.Equal(16, Find(gpu.Other, "PCIe Link Speed").Value);
		Assert.NotNull(gpu.Limits);
		Assert.NotNull(gpu.Fans);
		Assert.Null(gpu.Omitted);
	}

	[Fact]
	public void Omitted_OnlyNamesSectionsThatHaveReadings()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime)
			.Group("GPU [#0]: NVIDIA GeForce RTX 4090")
			.Temp("GPU Temperature", 60)
			.Fan("GPU Fan1", 1500)
			.Build();

		var gpu = Assert.Single(SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Dgpu, includeStats: false).Gpus);

		Assert.Equal(["fans"], gpu.Omitted);
	}

	[Fact]
	public void Amd_Igpu_ReportsTheIntegratedGpu()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.Amd, s_fresh, GpuSelection.Igpu, includeStats: false);

		var gpu = Assert.Single(result.Gpus);
		Assert.Equal("iGPU [#1]: AMD Radeon 610M", gpu.Name);
		Assert.Equal("igpu", gpu.Kind);
		Assert.Equal(3100, Find(gpu.MemoryController, "GPU Memory Clock").Value);
		Assert.Equal(0, Find(gpu.Throttling, "Throttle Reason - Power").Value);
	}

	[Fact]
	public void Amd_All_ReportsBothGpus()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.Amd, s_fresh, GpuSelection.All, includeStats: false);

		Assert.Equal(["dgpu", "igpu"], result.Gpus.Select(gpu => gpu.Kind));
	}

	[Fact]
	public void Nvidia_WithoutDiscretePrefix_IsStillTheDefault()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.IntelNvidia, s_fresh, GpuSelection.Dgpu, includeStats: false, [GpuSection.All]);

		var gpu = Assert.Single(result.Gpus);
		Assert.Equal("GPU [#0]: NVIDIA GeForce RTX 4070 Ti: MSI RTX 4070 Ti GAMING X TRIO", gpu.Name);
		Assert.Equal("dgpu", gpu.Kind);

		Assert.Equal(266.1, Find(gpu.Power, "GPU Power").Value);
		Assert.Equal(217.9, Find(gpu.Power, "GPU 16-pin HVPWR Power").Value);
		Assert.Equal(78.4, Find(gpu.Temperatures, "GPU Hot Spot Temperature").Value);
		Assert.Equal(74, Find(gpu.Temperatures, "GPU Memory Junction Temperature").Value);
		Assert.Equal(98, Find(gpu.Usage, "GPU Core Load").Value);
		Assert.Equal(54, Find(gpu.MemoryController, "GPU Memory Controller Load").Value);
		Assert.Equal(1313, Find(gpu.MemoryController, "GPU Memory Clock").Value);
		Assert.Equal(61.8, Find(gpu.Vram, "GPU Memory Usage").Value);
		Assert.Equal(7593, Find(gpu.Vram, "GPU Memory Allocated").Value);
		Assert.Equal(93.4, Find(gpu.Limits, "Total GPU Power [% of TDP]").Value);
		Assert.Equal(83, Find(gpu.Limits, "GPU Thermal Limit").Value);
		Assert.Equal(1, Find(gpu.Throttling, "Performance Limit - Power").Value);
		Assert.Equal(5, gpu.Throttling!.Count);
		Assert.Equal(2, gpu.Fans!.Count);
	}

	[Fact]
	public void Intel_Igpu_IsRecognizedByName()
	{
		var result = SensorQueries.GetGpu(TestSnapshots.IntelNvidia, s_fresh, GpuSelection.Igpu, includeStats: false);

		var gpu = Assert.Single(result.Gpus);
		Assert.Equal("GPU [#1]: Intel UHD Graphics 770", gpu.Name);
		Assert.Equal("igpu", gpu.Kind);
		Assert.Equal(312, Find(gpu.Vram, "GPU D3D Memory Dynamic").Value);
	}

	[Fact]
	public void NoDiscreteGpu_FallsBackToTheIntegratedOne()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime)
			.Group("iGPU [#0]: AMD Radeon 780M")
			.Temp("GPU Temperature", 45)
			.Build();

		var result = SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Dgpu, includeStats: false);

		Assert.Equal("igpu", Assert.Single(result.Gpus).Kind);
		Assert.Equal(Messages.GpuFallback(GpuSelection.Dgpu), result.Note);
	}

	[Fact]
	public void NoGpu_ReportsANote()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime).Group("CPU [#0]: X").Temp("CPU", 40).Build();

		var result = SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Dgpu, includeStats: false);

		Assert.Empty(result.Gpus);
		Assert.Equal(Messages.NoGpu, result.Note);
	}

	[Fact]
	public void TwoDiscreteGpus_AreBothReported()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime)
			.Group("GPU [#0]: NVIDIA GeForce RTX 4090")
			.Power("GPU Power", 400)
			.Power("GPU 16-pin HVPWR Power", 380)
			.Group("GPU [#1]: NVIDIA GeForce RTX 4090")
			.Power("GPU Power", 120)
			.Power("GPU 16-pin HVPWR Power", 110)
			.Group("GPU [#2]: Intel UHD Graphics 770")
			.Power("GPU Power", 1)
			.Build();

		var discrete = SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Dgpu, includeStats: false);
		var integrated = SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Igpu, includeStats: false);

		Assert.Equal(["GPU [#0]: NVIDIA GeForce RTX 4090", "GPU [#1]: NVIDIA GeForce RTX 4090"], discrete.Gpus.Select(gpu => gpu.Name));
		Assert.Equal([400.0, 120.0], discrete.Gpus.Select(gpu => Assert.Single(gpu.Power!).Value!.Value));
		Assert.Null(discrete.Note);
		Assert.Equal("GPU [#2]: Intel UHD Graphics 770", Assert.Single(integrated.Gpus).Name);
	}

	[Fact]
	public void SubGroups_OfAGpu_AreReportedWithTheirSuffix()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime)
			.Group("GPU [#0]: NVIDIA GeForce RTX 4090")
			.Temp("GPU Temperature", 60)
			.Group("GPU [#0]: NVIDIA GeForce RTX 4090: Extra")
			.Power("GPU Power", 400)
			.Build();

		var gpu = Assert.Single(SensorQueries.GetGpu(snapshot, s_fresh, GpuSelection.Dgpu, includeStats: false).Gpus);

		Assert.Equal(2, gpu.SensorGroups.Count);
		Assert.Null(Find(gpu.Temperatures, "GPU Temperature").Group);
		Assert.Equal("Extra", Find(gpu.Power, "GPU Power").Group);
	}
}
