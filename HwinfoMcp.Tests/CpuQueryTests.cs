using HwinfoMcp.Catalog;
using HwinfoMcp.Queries;
using HwinfoMcp.Tests.Fakes;

namespace HwinfoMcp.Tests;

public sealed class CpuQueryTests
{
	private static readonly QueryContext s_fresh = QueryContext.From(TestSnapshots.FreshClock);

	[Fact]
	public void Amd_Summary_ReportsTotalsWithoutPerCoreValuesAndCStates()
	{
		var result = SensorQueries.GetCpu(TestSnapshots.Amd, s_fresh, CpuDetail.Summary, includeStats: false);

		var cpu = Assert.Single(result.Cpus);
		Assert.Equal("summary", result.Detail);
		Assert.Null(result.Warning);
		Assert.Equal("CPU [#0]: AMD Ryzen 9 7900X", cpu.Name);
		Assert.Equal(["CPU [#0]: AMD Ryzen 9 7900X", "CPU [#0]: AMD Ryzen 9 7900X: Enhanced"], cpu.SensorGroups);

		var all = AllValues(cpu).ToList();
		Assert.DoesNotContain(all, value => value.Label.Contains("Residency", StringComparison.Ordinal));
		Assert.DoesNotContain(all, value => SensorPatterns.PerCoreLabel.IsMatch(value.Label));
		Assert.All(all, value => Assert.Null(value.Min));
	}

	[Fact]
	public void Amd_Summary_SortsPowerTemperaturesUsageAndLimits()
	{
		var cpu = SensorQueries.GetCpu(TestSnapshots.Amd, s_fresh, CpuDetail.Summary, includeStats: false).Cpus[0];

		var packagePower = Find(cpu.Power, "CPU Package Power");
		Assert.Equal(61.65, packagePower.Value);
		Assert.Equal("W", packagePower.Unit);
		Assert.Equal("Enhanced", packagePower.Group);
		Assert.Equal(58.96, Find(cpu.Power, "CPU PPT").Value);

		Assert.Equal(53.13, Find(cpu.Temperatures, "CPU (Tctl/Tdie)").Value);
		Assert.Equal("°C", Find(cpu.Temperatures, "CPU (Tctl/Tdie)").Unit);
		Assert.Equal(52, Find(cpu.Temperatures, "CPU IOD Hotspot").Value);

		var totalUsage = Find(cpu.Usage, "Total CPU Usage");
		Assert.Equal(1.73, totalUsage.Value);
		Assert.Null(totalUsage.Group);

		Assert.Equal(41.52, Find(cpu.Limits, "CPU PPT Limit").Value);
		Assert.Equal(60.28, Find(cpu.Limits, "Thermal Limit").Value);
		Assert.Equal(5354.12, Find(cpu.Limits, "Frequency Limit - Global").Value);

		Assert.Equal(0, Find(cpu.Throttling, "Thermal Throttling (HTC)").Value);
		Assert.Equal("Yes/No", Find(cpu.Throttling, "Thermal Throttling (PROCHOT EXT)").Unit);

		Assert.Equal(13.54, Find(cpu.Currents, "CPU TDC").Value);
	}

	[Fact]
	public void Amd_Summary_ReportsTheMemoryController()
	{
		var result = SensorQueries.GetCpu(TestSnapshots.Amd, s_fresh, CpuDetail.Summary, includeStats: false);
		var cpu = result.Cpus[0];

		Assert.Equal(3100, Find(cpu.MemoryController, "Memory Controller Clock (UCLK)").Value);
		Assert.Equal(2200, Find(cpu.MemoryController, "Infinity Fabric Clock (FCLK)").Value);
		Assert.Equal(0.71, Find(cpu.MemoryController, "DRAM Read Bandwidth").Value);
		Assert.Equal("GB/s", Find(cpu.MemoryController, "DRAM Write Bandwidth").Unit);
		Assert.Null(cpu.Vram);

		// The memory clock comes from the memory timings group, the CPU fan from the mainboard
		var related = Assert.IsAssignableFrom<IReadOnlyList<ReadingValue>>(result.Related);
		var memoryClock = Find(related, "Memory Clock");
		Assert.Equal(3100, memoryClock.Value);
		Assert.Equal("Memory Timings", memoryClock.Group);
		Assert.Contains(related, value => value is { Label: "CPU", Unit: "RPM", Value: 824 });
		Assert.Contains(related, value => value is { Label: "TSI0 (CPU)", Unit: "°C" });
		Assert.DoesNotContain(related, value => value.Label == "Motherboard");
	}

	[Fact]
	public void Amd_Full_AddsPerCoreValuesButStillNoCStates()
	{
		var cpu = SensorQueries.GetCpu(TestSnapshots.Amd, s_fresh, CpuDetail.Full, includeStats: false).Cpus[0];

		Assert.Equal(0.95, Find(cpu.Power, "Core 0 Power").Value);
		Assert.Equal(8.87, Find(cpu.Usage, "Core 0 T0 Usage").Value);
		Assert.Equal(39.3, Find(cpu.Temperatures, "Core0 (CCD1)").Value);
		Assert.Equal(53.5, Find(cpu.Other, "Core 0 Ratio").Value);
		Assert.DoesNotContain(AllValues(cpu), value => value.Label.Contains("Residency", StringComparison.Ordinal));
	}

	[Fact]
	public void IncludeStats_AddsMinMaxAndAverage()
	{
		var cpu = SensorQueries.GetCpu(TestSnapshots.Amd, s_fresh, CpuDetail.Summary, includeStats: true).Cpus[0];

		Assert.All(AllValues(cpu), value =>
		{
			Assert.NotNull(value.Min);
			Assert.NotNull(value.Max);
			Assert.NotNull(value.Avg);
		});
	}

	[Fact]
	public void Intel_SortsDtsEnhancedAndLimitReasonsGroups()
	{
		var result = SensorQueries.GetCpu(TestSnapshots.IntelNvidia, s_fresh, CpuDetail.Summary, includeStats: false);
		var cpu = Assert.Single(result.Cpus);

		Assert.Equal("CPU [#0]: Intel Core i7-13700K", cpu.Name);
		Assert.DoesNotContain(cpu.SensorGroups, group => group.EndsWith("C-State Residency", StringComparison.Ordinal));

		Assert.Equal(225.3, Find(cpu.Power, "CPU Package Power").Value);
		Assert.Equal(93, Find(cpu.Temperatures, "Core Max").Value);
		Assert.Equal("DTS", Find(cpu.Temperatures, "Core Max").Group);
		Assert.Equal(253, Find(cpu.Limits, "PL1 Power Limit (Static)").Value);
		Assert.Equal(7.1, Find(cpu.MemoryController, "System Agent Power").Value);

		Assert.Equal(1, Find(cpu.Throttling, "Package/Ring Power Limit Exceeded").Value);
		var limitReason = Find(cpu.Throttling, "IA: Package-Level RAPL/PBM PL2,PL3");
		Assert.Equal(1, limitReason.Value);
		Assert.Equal("Performance Limit Reasons", limitReason.Group);
		Assert.Contains(cpu.Throttling!, value => value.Label == "IA: Max Turbo Limit");

		// Per-core values of the hybrid architecture are left out of the summary
		Assert.DoesNotContain(AllValues(cpu), value => value.Label.StartsWith("P-core", StringComparison.Ordinal));
		Assert.DoesNotContain(AllValues(cpu), value => value.Label.StartsWith("E-core", StringComparison.Ordinal));

		Assert.Contains(result.Related!, value => value is { Label: "Pump1", Unit: "RPM" });
		Assert.Contains(result.Related!, value => value is { Label: "Vcore", Unit: "V" });
		Assert.Contains(result.Related!, value => value is { Label: "Memory Clock", Value: 2800 });
		Assert.DoesNotContain(result.Related!, value => value.Label == "CPU Busy (avg)");
	}

	[Fact]
	public void Intel_Full_IncludesHybridCores()
	{
		var cpu = SensorQueries.GetCpu(TestSnapshots.IntelNvidia, s_fresh, CpuDetail.Full, includeStats: false).Cpus[0];

		Assert.Equal(1.3, Find(cpu.Voltages, "P-core 0 VID").Value);
		Assert.Equal(100, Find(cpu.Usage, "E-core 16 T0 Usage").Value);
		Assert.Equal(88, Find(cpu.Temperatures, "P-core 0").Value);
	}

	[Fact]
	public void NoCpuGroups_ReportsANote()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime).Group("Memory Timings").Clock("Memory Clock", 3000).Build();

		var result = SensorQueries.GetCpu(snapshot, s_fresh, CpuDetail.Summary, includeStats: false);

		Assert.Empty(result.Cpus);
		Assert.Equal(Messages.NoCpu, result.Note);
	}

	[Fact]
	public void StaleData_IsFlagged()
	{
		var context = new QueryContext(TestSnapshots.PollTime.AddHours(13), TimeZoneInfo.Utc);

		var result = SensorQueries.GetCpu(TestSnapshots.Amd, context, CpuDetail.Summary, includeStats: false);

		Assert.NotNull(result.Warning);
		Assert.Contains("13 hours", result.Warning, StringComparison.Ordinal);
		Assert.Contains("Shared Memory Support", result.Warning, StringComparison.Ordinal);
	}

	internal static ReadingValue Find(IReadOnlyList<ReadingValue>? values, string label)
	{
		Assert.NotNull(values);
		return Assert.Single(values, value => value.Label == label);
	}

	internal static IEnumerable<ReadingValue> AllValues(DeviceReadings device)
	{
		IReadOnlyList<ReadingValue>?[] sections =
		[
			device.Power, device.Temperatures, device.Usage, device.MemoryController, device.Vram, device.Clocks,
			device.Limits, device.Throttling, device.Fans, device.Voltages, device.Currents, device.Other,
		];
		return sections.SelectMany(section => section ?? []);
	}
}
