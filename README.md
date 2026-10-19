# HwinfoMcp

An [MCP](https://modelcontextprotocol.io) server for Windows that gives Claude (or any other MCP client) read
access to the live sensor data of [HWiNFO](https://www.hwinfo.com/): power draw, temperatures, utilization,
clocks, limits and throttling of the CPU and GPU, the memory controller (UCLK, FCLK, DRAM bandwidth, GPU memory
controller load), memory timings and DIMM sensors, plus everything else HWiNFO reports (drives, mainboard, fans,
network, PresentMon framerates, ...).

The server is called `hwinfo-mcp-dotnet`, runs over stdio and ships as a single `HwinfoMcp.exe` that needs no
.NET installation.

> **Unofficial.** This project is not affiliated with, endorsed by or supported by HWiNFO or REALiX.
> HWiNFO is a product of Martin Malík - REALiX.

## How it works

HWiNFO can publish its sensor readings through a shared memory section. HwinfoMcp reads that section with
[Hwinfo.SharedMemory.Net](https://github.com/Seraksab/Hwinfo.SharedMemory.Net) on every tool call and returns
compact JSON.

- **Read-only.** It never writes to the shared memory and never starts or controls HWiNFO.
- **No admin rights, no network access, no telemetry.**
- **Robust.** If HWiNFO isn't running or Shared Memory Support is off, the tools say so and how to fix it
  instead of failing.

## Requirements

- Windows 10 or 11, x64.
- [HWiNFO](https://www.hwinfo.com/) (free or Pro) running with **Shared Memory Support** enabled:
  HWiNFO → Settings → "Shared Memory Support". Its sensor monitoring has to run (sensors window or
  sensors-only mode, minimized to the tray is fine).
- **Free version: Shared Memory Support stops after 12 hours.** HWiNFO then shows a notification and turns
  the option off, so the values freeze or disappear. Turn it back on (which starts another 12 hours) or restart
  HWiNFO. `hwinfo_status` and every other tool warn when the data is older than 60 seconds.

## Installation

1. Download `HwinfoMcp.exe` from the [Releases](../../releases) page (a `.sha256` checksum is attached).
2. Put it in a permanent location, e.g. `C:\Tools\HwinfoMcp\HwinfoMcp.exe`.
3. Register it with your MCP client as shown below.

### Claude Desktop

Open Claude Desktop → Settings → Developer → Edit Config, which opens
`%APPDATA%\Claude\claude_desktop_config.json`, and add the server:

```json
{
	"mcpServers": {
		"hwinfo": {
			"command": "C:\\Tools\\HwinfoMcp\\HwinfoMcp.exe"
		}
	}
}
```

Restart Claude Desktop. The server's log (stderr) ends up in Claude Desktop's MCP log files.

### Claude Code

```powershell
claude mcp add --scope user hwinfo -- "C:\Tools\HwinfoMcp\HwinfoMcp.exe"
```

`--scope user` makes it available in all your projects; leave it out to add it to the current project only.
Check the connection with `claude mcp list` or `/mcp` inside Claude Code.

### Options

| Argument | Effect |
|----------|--------|
| `--user-labels` | Report sensor groups and readings by the names HWiNFO displays (translated, or renamed by you) instead of HWiNFO's original English names. |
| `--dump <file.json> [description]` | Don't start the server: record the current sensor data to a JSON file, see [below](#a-value-ends-up-in-the-wrong-place). |

In Claude Desktop, pass arguments with `"args": ["--user-labels"]`; in Claude Code, append them after the
path.

By default the tools report HWiNFO's original English names, whatever language HWiNFO is set to: they stay
the same across HWiNFO versions and languages, and HWiNFO's translations aren't always unambiguous (the German
one calls both "Total CPU Usage" and "Total CPU Utility" "Gesamte CPU-Auslastung"). Details HWiNFO appends to a
name, such as the process in "PresentMon [game.exe]", are kept. The filters of `get_readings` match both
names.

## Tools

All tools are read-only. Values are rounded to two decimals. Min, max and average (since HWiNFO started
monitoring) are only included with `includeStats: true`.

| Tool | Parameters | What it returns |
|------|------------|-----------------|
| `hwinfo_status` | – | Whether HWiNFO's data is readable, time and age of the last poll, number of sensor groups and readings, a warning if the data is stale. |
| `list_sensors` | – | Every sensor group with its category (`cpu`, `dgpu`, `igpu`, `memory`, `dimm`, `system`, `other`), number of readings and readings per measurement type. |
| `get_readings` | `sensor`, `label`, `types`, `includeStats`, `limit` | Any readings, filtered by part of the sensor group name, part of the label and measurement types (`Temp`, `Volt`, `Fan`, `Current`, `Power`, `Clock`, `Usage`, `Other`). Returns at most `limit` readings (default 100, max 2000) and says when it cut the answer short. |
| `get_cpu` | `detail` (`summary`/`full`), `includeStats` | Curated CPU overview: power, temperatures, usage, memory controller (UCLK, FCLK, DRAM bandwidth), clocks, limits, throttling, voltages, currents, plus CPU readings of other groups (mainboard CPU temperature and fan, memory clock). `summary` leaves out per-core values, C-state residencies are always left out. |
| `get_gpu` | `type` (`dgpu`/`igpu`/`all`), `includeStats` | Curated GPU overview: board and chip power, temperatures incl. hotspot and memory junction, usage, memory controller load and memory clock, VRAM usage, clocks, limits, throttling, fans. Defaults to the discrete GPU and falls back to the integrated one. |
| `get_memory` | `includeStats` | Memory clock and timings (Tcas, Trcd, Trp, Tras, Trc, Trfc, command rate), every DIMM (temperatures, voltages, power), RAM and page file usage, and the CPU's memory controller readings. |

### Example questions

- "Is HWiNFO's sensor data available?"
- "How much power do my CPU and GPU draw right now, and how hot are they?"
- "Is my CPU throttling or running into a power, current or thermal limit?"
- "Run a game for a minute, then tell me the GPU hotspot and memory junction temperatures."
- "How busy is the GPU's memory controller, and how much VRAM is used?"
- "What are my RAM timings, and does the memory controller run 1:1 with the memory clock?"
- "How warm are my DIMMs and SSDs?"
- "Compare the integrated and the discrete GPU."
- "Which fans are spinning, and how fast?"

### Example answer

`get_cpu` on a Ryzen 9 7900X (abridged):

```json
{"pollTime":"2026-10-19T10:00:00+02:00","detail":"summary","cpus":[{"name":"CPU [#0]: AMD Ryzen 9 7900X",
"sensorGroups":["CPU [#0]: AMD Ryzen 9 7900X","CPU [#0]: AMD Ryzen 9 7900X: Enhanced"],
"power":[{"label":"CPU Package Power","value":61.65,"unit":"W","group":"Enhanced"}, ...],
"temperatures":[{"label":"CPU (Tctl/Tdie)","value":53.13,"unit":"°C","group":"Enhanced"}, ...],
"memoryController":[{"label":"Memory Controller Clock (UCLK)","value":3100,"unit":"MHz","group":"Enhanced"},
{"label":"DRAM Read Bandwidth","value":0.71,"unit":"GB/s","group":"Enhanced"}, ...],
"limits":[{"label":"CPU PPT Limit","value":41.52,"unit":"%","group":"Enhanced"}, ...], ...}],
"related":[{"label":"CPU","value":824,"unit":"RPM","group":"ASUS TUF GAMING X670E-PLUS WIFI (Nuvoton NCT6799D)"}, ...]}
```

A reading's `group` names the HWiNFO sensor group it comes from where that isn't obvious: it is left out for a
device's main group and shortened to the suffix (`Enhanced`, `DTS`, ...) for its sub groups.

## Vendor independence

HWiNFO names sensors differently for AMD, Intel and NVIDIA and across hardware generations. HwinfoMcp therefore
doesn't rely on exact names:

- Sensor groups are recognized by name patterns (`CPU [#n]`, `dGPU [#n]`, `iGPU [#n]`, `GPU [#n]`,
  `Memory Timings`, `DIMM`, `System`), matched against HWiNFO's original names, so neither HWiNFO's language
  nor renaming sensors breaks anything. All groups with the same `[#n]` prefix form one device, e.g. a CPU's
  main, `Enhanced` and `DTS` groups.
- Without HWiNFO's `d`/`i` prefix, integrated GPUs are recognized by name (Intel UHD/Iris/Arc Graphics, Radeon
  Graphics/Vega/780M, and a bare "AMD Radeon" as HWiNFO names the Ryzen 7000 iGPU); any other GPU counts as
  discrete.
- Within a device, readings are sorted by their measurement type and by keywords, e.g. `memory controller`,
  `memory clock`, `uclk`, `fclk`, `bandwidth` and `dram` for the memory controller, `throttl`, `prochot` and
  `performance limit` for throttling.

All patterns and keywords live in [`SensorPatterns.cs`](HwinfoMcp/Catalog/SensorPatterns.cs). When in doubt
they include a value too many rather than leave out an important one.

### A value ends up in the wrong place?

Record your sensor data and open an issue with it, or extend the patterns:

```powershell
HwinfoMcp.exe --dump my-system.json "Ryzen 7 9800X3D + RTX 5080"
```

This writes all sensor groups and readings to a JSON file (logs go to stderr). Review it before sharing:
sensor names can contain serial numbers, e.g. of drives. The tests replay such files, see
[`HwinfoMcp.Tests/TestData`](HwinfoMcp.Tests/TestData).

## Building from source

Requires the .NET 10 SDK.

```powershell
dotnet build
dotnet test
```

Publish the single-file Native AOT executable to `artifacts/publish/win-x64/HwinfoMcp.exe`:

```powershell
dotnet publish HwinfoMcp/HwinfoMcp.csproj -p:PublishProfile=win-x64
```

Native AOT needs the MSVC linker, i.e. Visual Studio's "Desktop development with C++" workload or the Visual
Studio Build Tools. Without it, the `win-x64-singlefile` profile builds a self-contained, trimmed single-file
executable instead (larger, starts slower, also needs no .NET installation):

```powershell
dotnet publish HwinfoMcp/HwinfoMcp.csproj -p:PublishProfile=win-x64-singlefile
```

GitHub Actions builds and tests every push. Pushing a tag like `v1.2.3` publishes a release with
`HwinfoMcp.exe`.

## Credits

- [Hwinfo.SharedMemory.Net](https://github.com/Seraksab/Hwinfo.SharedMemory.Net) (MIT) reads HWiNFO's shared
  memory. The test data `hwinfo-sm2-snapshot.bin` is taken from its repository.
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (Apache 2.0/MIT) implements the protocol.

## License

[MIT](LICENSE)
