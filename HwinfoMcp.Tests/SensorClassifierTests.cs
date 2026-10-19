using Hwinfo.SharedMemory;
using HwinfoMcp.Catalog;

namespace HwinfoMcp.Tests;

public sealed class SensorClassifierTests
{
	[Theory]
	[InlineData("CPU [#0]: AMD Ryzen 9 7900X", GroupCategory.Cpu)]
	[InlineData("CPU [#0]: AMD Ryzen 9 7900X: Enhanced", GroupCategory.Cpu)]
	[InlineData("CPU [#1]: Intel Xeon Gold 6430: DTS", GroupCategory.Cpu)]
	[InlineData("dGPU [#0]: AMD Radeon RX 7800 XT: ASUS TUF RX 7800 XT GAMING OC", GroupCategory.Gpu)]
	[InlineData("iGPU [#1]: AMD Radeon 610M", GroupCategory.Gpu)]
	[InlineData("GPU [#0]: NVIDIA GeForce RTX 4070 Ti: MSI RTX 4070 Ti GAMING X TRIO", GroupCategory.Gpu)]
	[InlineData("Memory Timings", GroupCategory.MemoryTimings)]
	[InlineData("DDR5 DIMM [#1]: G.Skill F5-6000J3040G32G (P0 CHANNEL A/DIMM 1)", GroupCategory.Dimm)]
	[InlineData("DDR4 DIMM [#0]: Corsair CMK32GX4M2E3200C16", GroupCategory.Dimm)]
	[InlineData("System: ASUS ", GroupCategory.System)]
	[InlineData("ASUS TUF GAMING X670E-PLUS WIFI (Nuvoton NCT6799D)", GroupCategory.Other)]
	[InlineData("S.M.A.R.T.: Samsung SSD 850 EVO 1TB (S21DNXAG616401V) [E:]", GroupCategory.Other)]
	[InlineData("PresentMon", GroupCategory.Other)]
	[InlineData("Windows Hardware Errors (WHEA)", GroupCategory.Other)]
	public void CategorizeGroup_RecognizesGroupsByNamePattern(string name, GroupCategory expected)
	{
		Assert.Equal(expected, SensorClassifier.CategorizeGroup(new Sensor(1, 0, name, name)));
	}

	[Fact]
	public void CategorizeGroup_UsesOriginalName_SoRenamedGroupsAreStillRecognized()
	{
		var renamed = new Sensor(1, 0, "dGPU [#0]: AMD Radeon RX 7800 XT", "My graphics card");

		Assert.Equal(GroupCategory.Gpu, SensorClassifier.CategorizeGroup(renamed));
		Assert.Equal("dGPU [#0]: AMD Radeon RX 7800 XT", SensorClassifier.Name(renamed, LabelStyle.Original));
		Assert.Equal("My graphics card", SensorClassifier.Name(renamed, LabelStyle.User));
	}

	[Theory]
	[InlineData("GPU [#0]: NVIDIA GeForce RTX 4080: ", "GPU [#0]: NVIDIA GeForce RTX 4080")]
	[InlineData("System: ASUS ", "System: ASUS")]
	[InlineData("Memory Timings", "Memory Timings")]
	public void Name_CutsOffTrailingSeparators(string name, string expected)
	{
		Assert.Equal(expected, SensorClassifier.Name(new Sensor(1, 0, name, name), LabelStyle.Original));
	}

	[Theory]
	// Translated: the original name is reported
	[InlineData("Memory Timings", "Speicher-Timings", "Memory Timings")]
	// Renamed by the user: likewise
	[InlineData("dGPU [#0]: AMD Radeon RX 7800 XT", "My graphics card", "dGPU [#0]: AMD Radeon RX 7800 XT")]
	// Extended by HWiNFO: the details are kept
	[InlineData("PresentMon", "PresentMon [dwm.exe]", "PresentMon [dwm.exe]")]
	[InlineData("Network: RealTek RTL8125", "Network: RealTek RTL8125 - Ethernet", "Network: RealTek RTL8125 - Ethernet")]
	public void Name_Original_KeepsDetailsHwinfoAppends(string original, string user, string expected)
	{
		Assert.Equal(expected, SensorClassifier.Name(new Sensor(1, 0, original, user), LabelStyle.Original));
	}

	[Fact]
	public void Label_FallsBackToTheOtherName()
	{
		var group = new Sensor(1, 0, "Group", "Group");
		var withoutUserLabel = new SensorReading(1, SensorType.Temp, "Temp9", "", "°C", 1, 1, 1, 1, group);
		var withoutOriginalLabel = new SensorReading(1, SensorType.Temp, "", "Water", "°C", 1, 1, 1, 1, group);

		Assert.Equal("Temp9", SensorClassifier.Label(withoutUserLabel, LabelStyle.User));
		Assert.Equal("Water", SensorClassifier.Label(withoutOriginalLabel, LabelStyle.Original));
	}

	[Theory]
	[InlineData("dGPU [#0]: AMD Radeon RX 7800 XT: ASUS TUF RX 7800 XT GAMING OC", GpuKind.Discrete)]
	[InlineData("iGPU [#1]: AMD Radeon 610M", GpuKind.Integrated)]
	// The d/i prefix wins over the name
	[InlineData("dGPU [#0]: Intel UHD Graphics 770", GpuKind.Discrete)]
	// Without the prefix, only known integrated GPU names count as integrated
	[InlineData("GPU [#0]: NVIDIA GeForce RTX 4070 Ti: MSI RTX 4070 Ti GAMING X TRIO", GpuKind.Discrete)]
	[InlineData("GPU [#0]: AMD Radeon RX 7900 XTX", GpuKind.Discrete)]
	[InlineData("GPU [#0]: Intel Arc A770 Graphics", GpuKind.Discrete)]
	[InlineData("GPU [#0]: Intel(R) Arc(TM) B580 Graphics", GpuKind.Discrete)]
	[InlineData("GPU [#1]: Intel UHD Graphics 770", GpuKind.Integrated)]
	[InlineData("GPU [#1]: Intel(R) Iris(R) Xe Graphics", GpuKind.Integrated)]
	[InlineData("GPU [#1]: Intel(R) Arc(TM) Graphics", GpuKind.Integrated)]
	[InlineData("GPU [#0]: Intel Arc 140V GPU", GpuKind.Integrated)]
	[InlineData("GPU [#1]: AMD Radeon(TM) 780M", GpuKind.Integrated)]
	[InlineData("GPU [#1]: AMD Radeon(TM) Graphics", GpuKind.Integrated)]
	[InlineData("GPU [#0]: AMD Radeon 8060S", GpuKind.Integrated)]
	// The Ryzen 7000 iGPU as HWiNFO names it, without a model
	[InlineData("GPU [#1]: AMD Radeon: ", GpuKind.Integrated)]
	[InlineData("GPU [#1]: AMD Radeon", GpuKind.Integrated)]
	[InlineData("GPU [#0]: AMD Radeon VII", GpuKind.Discrete)]
	[InlineData("GPU [#0]: AMD Radeon Pro W7900", GpuKind.Discrete)]
	public void ClassifyGpu_TellsDiscreteFromIntegrated(string name, GpuKind expected)
	{
		Assert.Equal(expected, SensorClassifier.ClassifyGpu(new Sensor(1, 0, name, name)));
	}

	[Theory]
	[InlineData("Core 0 VID", true)]
	[InlineData("Core 11 T1 Usage", true)]
	[InlineData("Core3 (CCD1)", true)]
	[InlineData("Core 0 Clock (perf #2/3)", true)]
	[InlineData("P-core 0 VID", true)]
	[InlineData("E-core 16 T0 Usage", true)]
	[InlineData("Core 0 T0 Effective Clock", true)]
	[InlineData("Core Max", false)]
	[InlineData("Max CPU/Thread Usage", false)]
	[InlineData("Total CPU Usage", false)]
	[InlineData("Average Effective Clock", false)]
	[InlineData("CPU Core Current (SVI3 TFN)", false)]
	[InlineData("IA Cores Power", false)]
	[InlineData("CPU CCD1 (Tdie)", false)]
	[InlineData("Average Active Core Count", false)]
	public void IsPerCore_DetectsPerCoreAndPerThreadLabels(string label, bool expected)
	{
		Assert.Equal(expected, SensorClassifier.IsPerCore(Reading(SensorType.Other, label)));
	}

	[Theory]
	// Memory controller: AMD, NVIDIA and Intel name it differently
	[InlineData(SensorType.Usage, "GPU Memory Controller Utilization", ReadingCategory.MemoryController)]
	[InlineData(SensorType.Usage, "GPU Memory Controller Load", ReadingCategory.MemoryController)]
	[InlineData(SensorType.Clock, "Memory Controller Clock (UCLK)", ReadingCategory.MemoryController)]
	[InlineData(SensorType.Clock, "Infinity Fabric Clock (FCLK)", ReadingCategory.MemoryController)]
	[InlineData(SensorType.Other, "DRAM Read Bandwidth", ReadingCategory.MemoryController)]
	[InlineData(SensorType.Clock, "GPU Memory Clock", ReadingCategory.MemoryController)]
	[InlineData(SensorType.Power, "System Agent Power", ReadingCategory.MemoryController)]
	// Throttling before limits
	[InlineData(SensorType.Other, "Thermal Throttling (HTC)", ReadingCategory.Throttling)]
	[InlineData(SensorType.Other, "Thermal Throttling (PROCHOT CPU)", ReadingCategory.Throttling)]
	[InlineData(SensorType.Other, "Throttle Reason - Power", ReadingCategory.Throttling)]
	[InlineData(SensorType.Other, "Performance Limit - Power", ReadingCategory.Throttling)]
	[InlineData(SensorType.Other, "Package/Ring Power Limit Exceeded", ReadingCategory.Throttling)]
	[InlineData(SensorType.Other, "Core Critical Temperature", ReadingCategory.Throttling)]
	// Limits
	[InlineData(SensorType.Usage, "CPU PPT Limit", ReadingCategory.Limits)]
	[InlineData(SensorType.Usage, "Thermal Limit", ReadingCategory.Limits)]
	[InlineData(SensorType.Power, "PL1 Power Limit (Static)", ReadingCategory.Limits)]
	[InlineData(SensorType.Clock, "Frequency Limit - Global", ReadingCategory.Limits)]
	[InlineData(SensorType.Usage, "Total GPU Power [% of TDP]", ReadingCategory.Limits)]
	[InlineData(SensorType.Temp, "GPU Thermal Limit", ReadingCategory.Limits)]
	// VRAM, whether reported in MB (AMD) or % (NVIDIA)
	[InlineData(SensorType.Other, "GPU Memory Usage", ReadingCategory.Vram)]
	[InlineData(SensorType.Usage, "GPU Memory Usage", ReadingCategory.Vram)]
	[InlineData(SensorType.Other, "GPU D3D Memory Dedicated", ReadingCategory.Vram)]
	[InlineData(SensorType.Other, "GPU Memory Allocated", ReadingCategory.Vram)]
	// Everything else by type
	[InlineData(SensorType.Temp, "GPU Hot Spot Temperature", ReadingCategory.Temperatures)]
	[InlineData(SensorType.Temp, "GPU Memory Junction Temperature", ReadingCategory.Temperatures)]
	[InlineData(SensorType.Power, "Total Board Power (TBP)", ReadingCategory.Power)]
	[InlineData(SensorType.Power, "GPU Memory Power (VDDIO)", ReadingCategory.Power)]
	[InlineData(SensorType.Usage, "GPU Utilization", ReadingCategory.Usage)]
	[InlineData(SensorType.Clock, "GPU Shader Clock", ReadingCategory.Clocks)]
	[InlineData(SensorType.Volt, "GPU Memory Voltage (VDDIO)", ReadingCategory.Voltages)]
	[InlineData(SensorType.Current, "CPU TDC", ReadingCategory.Currents)]
	[InlineData(SensorType.Fan, "GPU Fan", ReadingCategory.Fans)]
	[InlineData(SensorType.Other, "GPU Fan PWM", ReadingCategory.Fans)]
	[InlineData(SensorType.Other, "PCIe Link Speed", ReadingCategory.Other)]
	public void CategorizeReading_SortsVendorSpecificLabelsIntoSections(SensorType type, string label, ReadingCategory expected)
	{
		var group = new Sensor(1, 0, "dGPU [#0]: Some GPU", "dGPU [#0]: Some GPU");

		Assert.Equal(expected, SensorClassifier.CategorizeReading(Reading(type, label, group), group));
	}

	[Fact]
	public void CategorizeReading_TreatsEveryReadingOfALimitReasonsGroupAsThrottling()
	{
		var group = new Sensor(1, 0, "CPU [#0]: Intel Core i7-13700K: Performance Limit Reasons", "x");

		Assert.Equal(ReadingCategory.Throttling, SensorClassifier.CategorizeReading(Reading(SensorType.Other, "IA: Max Turbo Limit", group), group));
	}

	[Fact]
	public void CategorizeReading_MatchesTheUserLabelToo()
	{
		var group = new Sensor(1, 0, "CPU [#0]: X", "CPU [#0]: X");
		var reading = new SensorReading(1, SensorType.Usage, "Some Load", "Memory Controller Load", "%", 1, 1, 1, 1, group);

		Assert.Equal(ReadingCategory.MemoryController, SensorClassifier.CategorizeReading(reading, group));
	}

	[Theory]
	[InlineData("Thermal Throttling (HTC)", true)]
	[InlineData("IMC Clock", true)]
	[InlineData("Dimc", false)]
	[InlineData("", false)]
	public void KeywordSet_MatchesAtTheStartOfWords(string text, bool expected)
	{
		var keywords = new KeywordSet("throttl", "imc");

		Assert.Equal(expected, keywords.IsMatch(text));
	}

	private static SensorReading Reading(SensorType type, string label, Sensor? group = null)
	{
		return new SensorReading(1, type, label, label, "", 1, 1, 1, 1, group ?? new Sensor(1, 0, "Group", "Group"));
	}
}
