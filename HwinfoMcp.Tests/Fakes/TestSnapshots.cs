using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using Hwinfo.SharedMemory;
using HwinfoMcp.Sensors;

namespace HwinfoMcp.Tests.Fakes;

/// <summary>
/// The data sets the tests run against.
/// </summary>
internal static class TestSnapshots
{
	/// <summary>The poll time of every data set.</summary>
	public static readonly DateTimeOffset PollTime = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

	/// <summary>A clock two seconds after <see cref="PollTime"/>, i.e. fresh data.</summary>
	public static FixedTimeProvider FreshClock => new(PollTime.AddSeconds(2));

	private static readonly Lazy<SensorSnapshot> s_amd = new(LoadAmdSnapshot);

	/// <summary>
	/// The real shared memory dump of an AMD Ryzen 9 7900X with a Radeon RX 7800 XT (dGPU) and the CPU's
	/// Radeon 610M (iGPU), read through the real <see cref="SharedMemoryReader"/>.
	/// </summary>
	public static SensorSnapshot Amd => s_amd.Value;

	/// <summary>
	/// A hand-made Intel Core i7-13700K with an NVIDIA GeForce RTX 4070 Ti and Intel UHD Graphics 770, labeled
	/// the way HWiNFO labels these vendors. Its GPU groups lack the d/i prefix, so the name heuristic has to
	/// tell them apart.
	/// </summary>
	public static SensorSnapshot IntelNvidia { get; } = BuildIntelNvidia();

	private static readonly Lazy<SensorSnapshot> s_amdNvidiaGerman = new(() => LoadDump("amd-7800x3d-rtx4080-de.json"));

	/// <summary>
	/// A real recording of an AMD Ryzen 7 7800X3D with its Radeon iGPU and an NVIDIA GeForce RTX 4080, with
	/// HWiNFO set to German. Neither GPU group carries the d/i prefix.
	/// </summary>
	public static SensorSnapshot AmdNvidiaGerman => s_amdNvidiaGerman.Value;

	/// <summary>Loads a JSON dump from the test data folder.</summary>
	public static SensorSnapshot LoadDump(string fileName)
	{
		using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
		return SensorDump.Load(stream).ToSnapshot(PollTime);
	}

	private static SensorSnapshot LoadAmdSnapshot()
	{
		var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "hwinfo-sm2-snapshot.bin"));
		// The poll time is the int64 at offset 12 of the header
		BitConverter.GetBytes(PollTime.ToUnixTimeSeconds()).CopyTo(data, 12);

		// HWiNFO's section lives in the Global namespace, which needs a privilege to create. A session local
		// copy read through the reader's internal entry point exercises the same parsing.
		var name = $"Local\\HwinfoMcp.Tests_{Guid.NewGuid():N}";
		using var file = MemoryMappedFile.CreateNew(name, data.Length);
		using (var accessor = file.CreateViewAccessor())
		{
			accessor.WriteArray(0, data, 0, data.Length);
		}

		using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { StalenessTimeout = TimeSpan.Zero });
		if (!TryReadMemoryMappedFile(reader, name, out var readings))
		{
			throw new InvalidOperationException("The snapshot could not be read.");
		}

		return SensorSnapshot.FromReadings(readings);
	}

	// Hwinfo.SharedMemory.Net 4.0.0 only reads the fixed section names publicly. If an update renames
	// this internal method, the tests fail here with a MissingMethodException.
	[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryReadMemoryMappedFile")]
	private static extern bool TryReadMemoryMappedFile(SharedMemoryReader reader, string fileName, out SensorReadings readings);

	private static SensorSnapshot BuildIntelNvidia()
	{
		return new SnapshotBuilder(PollTime)
			.Group("System: MSI MS-7D91")
			.Other("Virtual Memory Committed", 21034, "MB")
			.Other("Physical Memory Used", 17890, "MB")
			.Other("Physical Memory Available", 14678, "MB")
			.Other("Physical Memory Load", 54.9, "%")
			.Other("Page File Usage", 0.4, "%")
			.Group("CPU [#0]: Intel Core i7-13700K")
			.Volt("P-core 0 VID", 1.296)
			.Volt("P-core 1 VID", 1.291)
			.Volt("E-core 16 VID", 1.188)
			.Clock("P-core 0 Clock (perf #1/3)", 5290.6)
			.Clock("P-core 1 Clock (perf #2/4)", 5290.6)
			.Clock("E-core 16 Clock (perf #9)", 4190.5)
			.Clock("Bus Clock", 99.8)
			.Clock("Ring/LLC Clock", 4589.3)
			.Clock("P-core 0 T0 Effective Clock", 5201.2)
			.Clock("Average Effective Clock", 4812.7)
			.Usage("P-core 0 T0 Usage", 99.1)
			.Usage("P-core 0 T1 Usage", 98.7)
			.Usage("E-core 16 T0 Usage", 100)
			.Usage("Max CPU/Thread Usage", 100)
			.Usage("Total CPU Usage", 97.4)
			.Other("P-core 0 Ratio", 53, "x")
			.Other("Uncore Ratio", 46, "x")
			.Group("CPU [#0]: Intel Core i7-13700K: C-State Residency")
			.Usage("Package C2 Residency", 0.2)
			.Usage("Core 0 C0 Residency", 99.5)
			.Group("CPU [#0]: Intel Core i7-13700K: DTS")
			.Temp("P-core 0", 88)
			.Temp("P-core 1", 91)
			.Temp("E-core 16", 79)
			.Temp("Core Max", 93)
			.Other("Core 0 Distance to TjMAX", 12, "°C")
			.YesNo("Core Thermal Throttling", false)
			.YesNo("Core Critical Temperature", false)
			.YesNo("Core Power Limit Exceeded", true)
			.YesNo("Package/Ring Thermal Throttling", false)
			.YesNo("Package/Ring Critical Temperature", false)
			.YesNo("Package/Ring Power Limit Exceeded", true)
			.Group("CPU [#0]: Intel Core i7-13700K: Enhanced")
			.Temp("CPU Package", 94)
			.Temp("CPU IA Cores", 93)
			.Temp("CPU GT Cores (Graphics)", 52)
			.Temp("VR VCC Temperature (SVID)", 61)
			.Volt("VR VCC Voltage (SVID OUT)", 1.231)
			.Current("VR VCC Current (SVID IOUT)", 168.4)
			.Power("CPU Package Power", 225.3)
			.Power("IA Cores Power", 209.8)
			.Power("System Agent Power", 7.1)
			.Power("Rest-of-Chip Power", 3.4)
			.Power("GT Cores Power", 0.1)
			.Power("PL1 Power Limit (Static)", 253)
			.Power("PL2 Power Limit (Static)", 253)
			.Clock("GPU Clock", 300)
			.Usage("GPU D3D Usage", 1.2)
			.Other("Current cTDP Level", 0, "")
			.Group("CPU [#0]: Intel Core i7-13700K: Performance Limit Reasons")
			.YesNo("IA: PROCHOT", false)
			.YesNo("IA: Thermal Event", false)
			.YesNo("IA: Package-Level RAPL/PBM PL1", false)
			.YesNo("IA: Package-Level RAPL/PBM PL2,PL3", true)
			.YesNo("IA: Max Turbo Limit", false)
			.YesNo("IA: Electrical Design Point/Other (ICCmax,PL4,SVID,DDR RAPL)", false)
			.Group("Memory Timings")
			.Clock("Memory Clock", 2800)
			.Other("Memory Clock Ratio", 28, "x")
			.Other("Gear Mode", 2, "")
			.Other("Tcas", 36, "T")
			.Other("Trcd", 36, "T")
			.Other("Trp", 36, "T")
			.Other("Tras", 76, "T")
			.Other("Trc", 112, "T")
			.Other("Trfc", 884, "T")
			.Other("Command Rate", 2, "T")
			.Group("DDR5 DIMM [#0]: Kingston KF556C36-16 (P0 CHANNEL A/DIMM 1)")
			.Temp("SPD Hub Temperature", 44.5)
			.Volt("VDD (SWA) Voltage", 1.25)
			.Volt("VDDQ (SWB) Voltage", 1.25)
			.Power("Total Power", 2.25)
			.YesNo("PMIC High Temperature", false)
			.Group("DDR5 DIMM [#2]: Kingston KF556C36-16 (P0 CHANNEL B/DIMM 1)")
			.Temp("SPD Hub Temperature", 46)
			.Volt("VDD (SWA) Voltage", 1.25)
			.Volt("VDDQ (SWB) Voltage", 1.25)
			.Power("Total Power", 2.5)
			.YesNo("PMIC High Temperature", false)
			.Group("MSI MAG Z790 TOMAHAWK WIFI (MS-7D91) (Nuvoton NCT6687D)")
			.Temp("CPU", 92)
			.Temp("System", 38)
			.Temp("MOS", 57)
			.Volt("+12V", 12.096)
			.Volt("Vcore", 1.244)
			.Volt("CPU SA", 1.098)
			.Volt("DRAM", 1.25)
			.Fan("CPU", 1825)
			.Fan("Pump1", 2410)
			.Fan("System 1", 905)
			.Group("GPU [#0]: NVIDIA GeForce RTX 4070 Ti: MSI RTX 4070 Ti GAMING X TRIO")
			.Temp("GPU Temperature", 66.2)
			.Temp("GPU Memory Junction Temperature", 74)
			.Temp("GPU Hot Spot Temperature", 78.4)
			.Temp("GPU Thermal Limit", 83)
			.Volt("GPU Core Voltage", 1.045)
			.Volt("GPU PCIe +12V Input Voltage", 12.11)
			.Volt("GPU 16-pin HVPWR Voltage", 12.02)
			.Fan("GPU Fan1", 1544)
			.Fan("GPU Fan2", 1561)
			.Current("GPU PCIe +12V Input Current", 3.98)
			.Current("GPU 16-pin HVPWR Current", 18.12)
			.Power("GPU Power", 266.1)
			.Power("GPU Core (NVVDD) Input Power (sum)", 151.6)
			.Power("GPU FBVDD Input Power", 27.3)
			.Power("GPU PCIe +12V Input Power", 48.2)
			.Power("GPU 16-pin HVPWR Power", 217.9)
			.Clock("GPU Clock", 2790)
			.Clock("GPU Memory Clock", 1313)
			.Clock("GPU Video Clock", 1965)
			.Clock("GPU Effective Clock", 2786.4)
			.Usage("GPU Core Load", 98)
			.Usage("GPU Memory Controller Load", 54)
			.Usage("GPU Video Engine Load", 0)
			.Usage("GPU Bus Load", 6)
			.Usage("GPU Memory Usage", 61.8)
			.Usage("GPU D3D Usage", 97.6)
			.Usage("Total GPU Power (normalized) [% of TDP]", 92.1)
			.Usage("Total GPU Power [% of TDP]", 93.4)
			.YesNo("Performance Limit - Power", true)
			.YesNo("Performance Limit - Thermal", false)
			.YesNo("Performance Limit - Reliability Voltage", false)
			.YesNo("Performance Limit - Max Operating Voltage", false)
			.YesNo("Performance Limit - Utilization", false)
			.Other("GPU Memory Allocated", 7593, "MB")
			.Other("GPU Memory Available", 4695, "MB")
			.Other("GPU D3D Memory Dedicated", 7402, "MB")
			.Other("GPU D3D Memory Dynamic", 121, "MB")
			.Other("PCIe Link Speed", 16, "GT/s")
			.Group("GPU [#1]: Intel UHD Graphics 770")
			.Power("GPU Power", 0.4)
			.Clock("GPU Clock", 300)
			.Usage("GPU D3D Usage", 1.2)
			.Other("GPU D3D Memory Dynamic", 312, "MB")
			.Group("S.M.A.R.T.: Samsung SSD 990 PRO 2TB")
			.Temp("Drive Temperature", 47)
			.Temp("Drive Temperature 2", 55)
			.Usage("Drive Remaining Life", 99)
			.YesNo("Drive Failure", false)
			.Group("Drive: Samsung SSD 990 PRO 2TB")
			.Usage("Read Activity", 3.2)
			.Usage("Total Activity", 4.1)
			.Other("Read Rate", 12.4, "MB/s")
			.Group("PresentMon", "PresentMon [Game.exe]")
			.Other("Framerate Presented (avg)", 143.9, "FPS")
			.Other("CPU Busy (avg)", 5.1, "ms")
			.Build();
	}
}
