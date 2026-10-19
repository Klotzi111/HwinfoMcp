# HwinfoMcp

An [MCP](https://modelcontextprotocol.io) server for Windows that gives your AI Agent read
access to the live sensor data of [HWiNFO](https://www.hwinfo.com/).

The server is called `hwinfo-mcp-dotnet` and runs over stdio. It ships as a single Native AOT executable for x64
and ARM64 that needs no .NET installation.
For local development and usage combined it's simplest to install it as a .NET global tool with the command `hwinfo-mcp-dotnet`.

> **Disclaimer.** This project is not affiliated with, endorsed by or supported by HWiNFO or REALiX.
> HWiNFO is a product of Martin Malík - REALiX.

## How it works

HWiNFO can publish its sensor readings through a shared memory section. HwinfoMcp reads that section with
[Hwinfo.SharedMemory.Net](https://github.com/Seraksab/Hwinfo.SharedMemory.Net) on every tool call and returns
compact JSON. While a [measurement](https://github.com/Klotzi111/HwinfoMcp#measurements) runs, it also reads it every 250 ms and records each new poll.

- **Read-only.** It never writes to the shared memory and never starts or controls HWiNFO.
- **No admin rights, no network access, no telemetry.**
- **Robust.** If HWiNFO isn't running or Shared Memory Support is off, the tools say so and how to fix it
  instead of failing.

## Requirements

- Windows 10 or 11, x64 or ARM64.
- [HWiNFO](https://www.hwinfo.com/) (free or Pro) running with **Shared Memory Support** enabled:
  HWiNFO → Settings → "Shared Memory Support". Its sensor monitoring has to run (sensors window or
  sensors-only mode, minimized to the tray is fine).
- **Free version: Shared Memory Support stops after 12 hours.** HWiNFO then shows a notification and turns
  the option off, so the values freeze or disappear. Turn it back on (which starts another 12 hours) or restart
  HWiNFO. `hwinfo_status` and every other tool warn when the data is older than 60 seconds.

## Tested with

- **Live:** Windows 10 Pro 22H2, HWiNFO set to German. All tools were used from Claude Code, running the
  server as global tool.
- **Recorded data in the tests:** A real shared memory dump of HWiNFO is used for test data.

## Installation

Downloads are at the [Releases](https://github.com/Klotzi111/HwinfoMcp/releases) page.

<!-- TODO: **As .NET global tool** (needs .NET 10 SDK): -->

**As standalone executable** (no .NET needed): download `HwinfoMcp-win-x64.exe`, or `HwinfoMcp-win-arm64.exe`
for Windows on ARM, and save it in a permanent location, e.g. as `C:\Tools\HwinfoMcp\HwinfoMcp.exe`. Use that
path instead of `hwinfo-mcp-dotnet` below.
*The release's `SHA256SUMS` lists the checksums of all files, and
`gh attestation verify <file> --repo Klotzi111/HwinfoMcp` checks that a file was built by this repository's
GitHub Actions workflow.*

To run your own build, see [Building from source](https://github.com/Klotzi111/HwinfoMcp#building-from-source).

### Claude Desktop

Open Claude Desktop → Settings → Developer → Edit Config, which opens
`%APPDATA%\Claude\claude_desktop_config.json`, and add the server:

```json
{
	"mcpServers": {
		"hwinfo": {
			"command": "hwinfo-mcp-dotnet"
		}
	}
}
```

Restart Claude Desktop. The server's log (stderr) ends up in Claude Desktop's MCP log files.

### Claude Code

```powershell
claude mcp add --scope user hwinfo -- hwinfo-mcp-dotnet
```

`--scope user` makes it available in all your projects; leave it out to add it to the current project only.
Check the connection with `claude mcp list` or `/mcp` inside Claude Code.

### Options

| Argument | Effect |
|----------|--------|
| `--user-labels` | Report sensor groups and readings by the names HWiNFO displays (translated, or renamed by you) instead of HWiNFO's original English names. |
| `--measurement-memory-limit <size>` | The most memory the recorded snapshots of all running [measurements](https://github.com/Klotzi111/HwinfoMcp#measurements) may take, e.g. `512MB` or `2GB` (1 KB = 1024 bytes). Default: `1GB`. Also settable through the environment variable `HWINFO_MCP_MEASUREMENT_MEMORY_LIMIT`; the option wins. |
| `--dump <file.json> [description]` | Don't start the server: record the current sensor data to a JSON file, see [below](https://github.com/Klotzi111/HwinfoMcp#a-value-ends-up-in-the-wrong-place). |
| `--version` | Print the version and exit. |
| `--help` | Print the usage and exit. |

Commands like `--dump` must be the first argument; all switches are case-insensitive. In Claude Desktop, pass
arguments with `"args": ["--user-labels"]`; in Claude Code, append them after the command:
`claude mcp add --scope user hwinfo -- hwinfo-mcp-dotnet --user-labels`.

By default the tools report HWiNFO's original English names, whatever language HWiNFO is set to: they stay
the same across HWiNFO versions and languages, and HWiNFO's translations aren't always unambiguous (the German
one calls both "Total CPU Usage" and "Total CPU Utility" "Gesamte CPU-Auslastung"). Details HWiNFO appends to a
name, such as the process in "PresentMon [game.exe]", are kept. The filters of `get_readings` match both
names.

## Tools

All tools except `start_measurement` and `stop_measurement` are read-only, and those two only change the
server's own measurements. Values are rounded to two decimals. Min, max and average (since HWiNFO started
monitoring) are only included with `includeStats: true`. `get_cpu` and `get_gpu` keep their default answer
short and name the sections they shortened or left out in `omitted`, so Claude knows what it can request with
`include`. Given `measurement`, the data tools report a stopped [measurement](https://github.com/Klotzi111/HwinfoMcp#measurements) instead of the
current values.

| Tool | Parameters | What it returns |
|------|------------|-----------------|
| `hwinfo_status` | – | Whether HWiNFO's data is readable, time and age of the last poll, number of sensor groups and readings, a warning if the data is stale. |
| `list_sensors` | – | Every sensor group with its category (`cpu`, `dgpu`, `igpu`, `memory`, `dimm`, `system`, `other`), number of readings and readings per measurement type. |
| `get_readings` | `sensor`, `label`, `types`, `includeStats`, `limit`, `measurement` | Any readings, filtered by part of the sensor group name, part of the label and measurement types (`Temp`, `Volt`, `Fan`, `Current`, `Power`, `Clock`, `Usage`, `Other`). Returns at most `limit` readings (default 100, max 2000) and says when it cut the answer short. |
| `get_cpu` | `detail` (`summary`/`full`), `include`, `includeStats`, `measurement` | Curated CPU overview: main power ("CPU Package Power"), temperatures, usage, memory controller (UCLK, FCLK, DRAM bandwidth), clocks, throttling. The rarely needed sections come only with `include`: `power` (all power readings), `voltages`, `currents` (incl. TDC/EDC), `limits` (PPT, TDC, EDC, thermal limit), `other`, `related` (CPU readings of other groups: mainboard CPU temperature and fan, Vcore, memory clock), or `all`. `summary` leaves out per-core values, C-state residencies are always left out. |
| `get_gpu` | `type` (`dgpu`/`igpu`/`all`), `include`, `includeStats`, `measurement` | Curated GPU overview: main power (NVIDIA's and Intel's "GPU Power", AMD's board power), temperatures incl. hotspot and memory junction, usage, memory controller load and memory clock, VRAM usage, clocks, throttling. The rarely needed sections come only with `include`: `power` (all power readings: chip, rails, connectors), `voltages`, `currents`, `limits`, `fans`, `other`, or `all`. Defaults to the discrete GPU and falls back to the integrated one. |
| `get_memory` | `includeStats`, `measurement` | Memory clock and timings (Tcas, Trcd, Trp, Tras, Trc, Trfc, command rate), every DIMM (temperatures, voltages, power), RAM and page file usage, and the CPU's memory controller readings. |
| `start_measurement` | `name` | Starts a named measurement: HWiNFO's current poll and every new one until it is stopped. |
| `stop_measurement` | `name` | Stops a measurement and evaluates it. Returns duration, number of snapshots and the periods without snapshots; the values come from the data tools with `measurement`. |
| `list_measurements` | – | The running measurements, the stopped ones that can be queried, and the memory the recorded snapshots take compared to the limit. |

## Measurements

The data tools report the moment of the call. To see how values behave over a period, e.g. while a game or a
benchmark runs, Claude starts a measurement with a name, stops it later, and then asks `get_cpu`, `get_gpu`,
`get_memory` or `get_readings` with `measurement: "<name>"`. Instead of `value` they then report `min`, `max`,
`median` and `avg` of every reading over the measurement, and the answer starts with a summary of the
measurement.

- **Every poll once.** While a measurement runs, the server reads HWiNFO's shared memory every 250 ms and
  records a snapshot whenever HWiNFO polled again (its poll time changed, or any value did, as the poll time only
  has whole seconds). How many snapshots a measurement gets depends on HWiNFO's polling period (Settings →
  "Polling Period", 2000 ms by default). A measurement also includes the poll current at its start.
- **Several at once, one copy.** Measurements running at the same time share their snapshots: each poll is kept
  once, and each measurement only remembers where it started. A snapshot holds only the values; names and units
  are shared as long as HWiNFO's sensors don't change. Snapshots no running measurement needs any more are
  released. Measurements are only evaluated when they stop.
- **Memory limit.** The snapshots of all running measurements together stay below
  `--measurement-memory-limit` (default 1 GB, roughly 8 bytes per reading and poll: with 300 readings and a
  2-second polling period that's days). Once the limit is reached, new polls aren't recorded until stopping
  measurements frees memory. The stop then warns when the last snapshot before the gap was taken and how long
  the measurement went unmeasured; `gaps` lists each such period.
- **No data.** Periods in which HWiNFO delivered no new data for more than 60 seconds (closed, paused, or the
  free version's 12 hours were up) are listed as gaps too. Stale values aren't recorded.
- **Results.** The results of up to 100 stopped measurements are kept until the server stops. Starting a
  measurement with the name of a stopped one replaces its result. Names are case-insensitive.

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

All patterns and keywords live in
[`SensorPatterns.cs`](https://github.com/Klotzi111/HwinfoMcp/blob/main/HwinfoMcp/Catalog/SensorPatterns.cs). When in doubt
they include a value too many rather than leave out an important one.

### A value ends up in the wrong place?

Record your sensor data and open an issue with it, or extend the patterns.  
Example for dumping:

```powershell
HwinfoMcp.exe --dump my-system.json "Ryzen 7 9800X3D + RTX 5080"
```

This writes all sensor groups and readings to a JSON file (logs go to stderr). Review it before sharing:
sensor names can contain serial numbers, e.g. of drives. The tests replay such files, see
[`HwinfoMcp.Tests/TestData`](https://github.com/Klotzi111/HwinfoMcp/tree/main/HwinfoMcp.Tests/TestData).

## Building from source

Requires the .NET 10 SDK.

```powershell
dotnet build
dotnet test
```

The code style lives in `.editorconfig` and is part of the build. For files the build doesn't check, e.g. after a
tool wrote them with other line endings,
[`tools/normalize.py`](https://github.com/Klotzi111/HwinfoMcp/blob/main/tools/normalize.py) applies its encodings,
line endings and final newlines to the files you name (`--check` only reports):

```powershell
python -I tools/normalize.py --check $(git ls-files)
```

### Local install as global tool

[`install-local.ps1`](https://github.com/Klotzi111/HwinfoMcp/blob/main/install-local.ps1) packs the current
build and (re)installs it as global tool, so MCP clients configured with the command `hwinfo-mcp-dotnet` run it.
It needs PowerShell 7.2 or later (`pwsh`), not Windows PowerShell:

```powershell
./install-local.ps1
```

The version gets a `local.<timestamp>` suffix (e.g. `0.1.0-local.20261019143000`), which the server reports
to MCP clients. Close the MCP clients that run the server first, as it locks its files; the script refuses to
run otherwise. Uninstall with `dotnet tool uninstall -g HwinfoMcp`.

### Executables

Publish the single-file Native AOT executable to `artifacts/publish/win-x64/HwinfoMcp.exe` (or, with the
`win-arm64` profile, to `artifacts/publish/win-arm64/HwinfoMcp.exe` for Windows on ARM):

```powershell
dotnet publish HwinfoMcp/HwinfoMcp.csproj -p:PublishProfile=win-x64
```

Native AOT needs the MSVC linker, i.e. Visual Studio's "Desktop development with C++" workload or the Visual
Studio Build Tools (for ARM64 also their "C++ ARM64 build tools"); a MinGW/MSYS2 toolchain doesn't work. Without it, the `win-x64-singlefile` profile builds
a framework-dependent single-file executable instead, which needs the .NET 10 runtime:

```powershell
dotnet publish HwinfoMcp/HwinfoMcp.csproj -p:PublishProfile=win-x64-singlefile
```

The project targets `net10.0` rather than `net10.0-windows` because .NET tools can't target a platform. It
is declared Windows-only through `[SupportedOSPlatform("windows")]`, and the AOT and trim analyzers run on
every build, so it stays Native AOT compatible.

GitHub Actions builds and tests every change to `main`. It also builds the Native AOT
executables for x64 and ARM64, each on a runner of its architecture, and runs the stdio tests against them.

## Credits

Many thanks to [Seraksab](https://github.com/Seraksab) for
[Hwinfo.SharedMemory.Net](https://github.com/Seraksab/Hwinfo.SharedMemory.Net). It does all the heavy lifting of
reading HWiNFO's shared memory reliably, and its real-world snapshot `hwinfo-sm2-snapshot.bin` is the backbone
of this project's tests.

- [Hwinfo.SharedMemory.Net](https://github.com/Seraksab/Hwinfo.SharedMemory.Net) (MIT) reads HWiNFO's shared
  memory. The test data `hwinfo-sm2-snapshot.bin` is taken from its repository.
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (Apache 2.0/MIT) implements the protocol.

## License

[MIT](https://github.com/Klotzi111/HwinfoMcp/blob/main/LICENSE). The executables include third-party software, see
[THIRD-PARTY-NOTICES.md](https://github.com/Klotzi111/HwinfoMcp/blob/main/THIRD-PARTY-NOTICES.md).
