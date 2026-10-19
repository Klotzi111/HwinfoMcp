using System.Text.RegularExpressions;

namespace HwinfoMcp.Catalog;

/// <summary>
/// Every heuristic that maps HWiNFO's sensor groups and labels to what the tools report, in one place.
/// <para>
/// Names differ between vendors (AMD, Intel, NVIDIA) and hardware generations, so nothing here relies
/// on exact names: sensor groups are recognized by their name pattern, readings by their
/// <see cref="Hwinfo.SharedMemory.SensorType"/> and by keywords. When a value ends up in the wrong
/// place, extend the lists below. When in doubt, a pattern should rather include a value too many than
/// leave out an important one.
/// </para>
/// <para>
/// Group patterns are matched against the original names HWiNFO assigns (<c>NameOrig</c>), label
/// keywords against both the original and the user-defined label, so renaming sensors in HWiNFO
/// doesn't break the recognition.
/// </para>
/// </summary>
public static partial class SensorPatterns
{
	/// <summary>
	/// How old HWiNFO's last poll may be before the data counts as stale. The free HWiNFO version stops
	/// updating its shared memory after 12 hours, which leaves the last values in place.
	/// </summary>
	public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

	#region Sensor groups

	/// <summary>
	/// A CPU sensor group, e.g. "CPU [#0]: AMD Ryzen 9 7900X" and its sub groups
	/// "CPU [#0]: AMD Ryzen 9 7900X: Enhanced", "...: DTS", "...: C-State Residency".
	/// All groups with the same prefix belong to one CPU.
	/// </summary>
	[GeneratedRegex(@"^CPU \[#(?<index>\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	public static partial Regex CpuGroup { get; }

	/// <summary>
	/// A GPU sensor group, e.g. "dGPU [#0]: AMD Radeon RX 7800 XT: ...", "iGPU [#1]: AMD Radeon 610M" or
	/// "GPU [#0]: NVIDIA GeForce RTX 4070". The optional d/i prefix tells discrete from integrated GPUs.
	/// All groups with the same prefix belong to one GPU.
	/// </summary>
	[GeneratedRegex(@"^(?<kind>[di]?)GPU \[#(?<index>\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	public static partial Regex GpuGroup { get; }

	/// <summary>The memory timings group: memory clock, Tcas, Trcd, Trp, Tras, Trc, Trfc, command rate.</summary>
	public static KeywordSet MemoryTimingsGroup { get; } = new("Memory Timings");

	/// <summary>Memory module groups, e.g. "DDR5 DIMM [#1]: G.Skill F5-6000J3040G32G (P0 CHANNEL A/DIMM 1)".</summary>
	public static KeywordSet DimmGroup { get; } = new("DIMM");

	/// <summary>The system group with RAM, virtual memory and page file usage, e.g. "System: ASUS".</summary>
	[GeneratedRegex(@"^System\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	public static partial Regex SystemGroup { get; }

	/// <summary>CPU sub groups get_cpu leaves out because they only add noise.</summary>
	public static KeywordSet CpuExcludedGroups { get; } = new("C-State Residency");

	/// <summary>
	/// Groups whose readings all report throttling or limit reasons, e.g. Intel's
	/// "CPU [#0]: Intel Core i7-13700K: Performance Limit Reasons".
	/// </summary>
	public static KeywordSet ThrottlingGroups { get; } = new("Limit Reasons");

	/// <summary>
	/// Names of integrated GPUs. Only consulted when a GPU group lacks the d/i prefix (plain "GPU [#n]");
	/// any other GPU counts as discrete. Covers Intel UHD/HD/Iris and the Arc iGPUs ("Intel Arc Graphics",
	/// "Intel Arc 140V"), AMD's iGPUs ("AMD Radeon(TM) Graphics", "AMD Radeon 780M", "Radeon Vega 8", and a
	/// bare "AMD Radeon" without a model as HWiNFO names the Ryzen 7000 iGPU), and Qualcomm Adreno.
	/// </summary>
	[GeneratedRegex(
		@"Intel(?:\(R\))?\s+(?:UHD|HD|Iris)"
		+ @"|Intel(?:\(R\))?\s+Arc(?:\(TM\))?\s+(?:Graphics|\d{3}[VT]\b)"
		+ @"|Radeon(?:\(TM\))?(?:\s+(?:Graphics|Vega|\d{3,4}[MS]\b)|\s*(?::|$))"
		+ @"|Adreno|Qualcomm",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	public static partial Regex IntegratedGpuName { get; }

	#endregion

	#region Readings

	/// <summary>
	/// Per-core and per-thread readings, which get_cpu's summary leaves out: "Core 0 VID",
	/// "Core3 (CCD1)", "P-core 2 Clock", "E-core 16 T0 Usage", "Core 5 T1 Effective Clock".
	/// Aggregates like "Core Max" or "Max CPU/Thread Usage" don't match.
	/// </summary>
	[GeneratedRegex(@"\b(?:[PE]-?)?Core\s*#?\d+|\bT\d+\b|\bThread\s*#?\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	public static partial Regex PerCoreLabel { get; }

	/// <summary>
	/// Throttling indicators and limit reasons: "Thermal Throttling (HTC)", "Throttle Reason - Power",
	/// "Performance Limit - Power" (NVIDIA), "Core Power Limit Exceeded" and "Core Critical Temperature" (Intel).
	/// Checked first, so these never end up under limits or temperatures.
	/// </summary>
	public static KeywordSet Throttling { get; } = new(
		"throttl", "prochot", "performance limit", "limit exceeded", "critical temperature", "limit reason"
	);

	/// <summary>
	/// Memory controller and memory interface: "Memory Controller Clock (UCLK)", "DRAM Read Bandwidth",
	/// "Infinity Fabric Clock (FCLK)", "GPU Memory Controller Utilization" (AMD),
	/// "GPU Memory Controller Load" (NVIDIA), "GPU Memory Clock", "System Agent Power" (Intel).
	/// </summary>
	public static KeywordSet MemoryController { get; } = new(
		"memory controller", "memory clock", "uclk", "fclk", "mclk", "infinity fabric", "bandwidth", "dram", "imc",
		"system agent"
	);

	/// <summary>
	/// GPU memory (VRAM) usage: "GPU Memory Usage", "GPU Memory Allocated", "GPU D3D Memory Dedicated".
	/// </summary>
	public static KeywordSet Vram { get; } = new(
		"memory usage", "memory used", "memory allocated", "memory available", "memory dedicated", "memory dynamic",
		"d3d memory", "vram", "frame buffer"
	);

	/// <summary>
	/// Limits and the utilization of limits: "CPU PPT Limit", "Thermal Limit", "PL1 Power Limit (Static)",
	/// "GPU PPT Limit (Sustained)", "Frequency Limit - Global", "Total GPU Power [% of TDP]".
	/// </summary>
	public static KeywordSet Limits { get; } = new("limit", "of tdp");

	/// <summary>Fans and pumps reported with a type other than fan, e.g. "GPU Fan PWM".</summary>
	public static KeywordSet Fans { get; } = new("fan", "pump");

	/// <summary>
	/// Readings of other groups (mainboard, embedded controller, cooler) that belong to the CPU, e.g. the
	/// mainboard's "CPU" temperature and fan, "Vcore" or "TSI0 (CPU)". Only temperatures, fans, voltages,
	/// currents and power are taken from those groups.
	/// </summary>
	public static KeywordSet CpuRelated { get; } = new("cpu", "vcore", "aio", "pump");

	/// <summary>Readings of the system group that describe RAM and page file usage.</summary>
	public static KeywordSet MemoryUsage { get; } = new("physical memory", "virtual memory", "page file");

	#endregion
}
