using System.IO.Pipelines;
using System.Text.Json;
using HwinfoMcp.Catalog;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HwinfoMcp.Tests;

/// <summary>
/// Runs the server as registered by the program, connected to an MCP client through in-memory pipes.
/// </summary>
public sealed class McpServerTests
{
	private static readonly string[] s_toolNames =
	[
		"get_cpu", "get_gpu", "get_memory", "get_readings", "hwinfo_status", "list_measurements", "list_sensors",
		"start_measurement", "stop_measurement",
	];

	private static readonly string[] s_measurementControls = ["start_measurement", "stop_measurement"];

	private static readonly string[] s_powerAndRelated = ["power", "related"];

	private static readonly string[] s_temperatureAndPower = ["Temp", "Power"];

	private static readonly string[] s_fans = ["fans"];

	private static readonly string[] s_unknownSection = ["bogus"];

	[Fact]
	public async Task Handshake_ReportsServerInfoAndInstructions()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		Assert.Equal(HwinfoMcpServer.Name, server.Client.ServerInfo.Name);
		Assert.Equal(HwinfoMcpServer.Version, server.Client.ServerInfo.Version);
		Assert.Equal(HwinfoMcpServer.Instructions, server.Client.ServerInstructions);
	}

	[Fact]
	public async Task ToolsList_ReturnsAllTools_OnlyTheMeasurementControlsChangeState()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(s_toolNames, tools.Select(tool => tool.Name).Order());
		Assert.All(tools, tool =>
		{
			Assert.False(string.IsNullOrWhiteSpace(tool.Description));
			Assert.Equal(!s_measurementControls.Contains(tool.Name), tool.ProtocolTool.Annotations?.ReadOnlyHint);
			Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
			Assert.False(tool.ProtocolTool.Annotations?.OpenWorldHint);
		});
	}

	[Fact]
	public async Task ToolsList_DescribesEnumParameters()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var tools = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
			.ToDictionary(tool => tool.Name, tool => tool.ProtocolTool.InputSchema);

		var types = tools["get_readings"].GetProperty("properties").GetProperty("types");
		Assert.Equal(["Temp", "Volt", "Fan", "Current", "Power", "Clock", "Usage", "Other"], EnumValues(types.GetProperty("items")));

		var detail = tools["get_cpu"].GetProperty("properties").GetProperty("detail");
		Assert.Equal(["summary", "full"], EnumValues(detail));
		Assert.Equal("summary", detail.GetProperty("default").GetString());
		var cpuInclude = tools["get_cpu"].GetProperty("properties").GetProperty("include").GetProperty("items");
		Assert.Equal(["power", "voltages", "currents", "limits", "other", "related", "all"], EnumValues(cpuInclude));

		var gpuType = tools["get_gpu"].GetProperty("properties").GetProperty("type");
		Assert.Equal(["dgpu", "igpu", "all"], EnumValues(gpuType));
		Assert.Equal("dgpu", gpuType.GetProperty("default").GetString());
		var gpuInclude = tools["get_gpu"].GetProperty("properties").GetProperty("include").GetProperty("items");
		Assert.Equal(["power", "voltages", "currents", "limits", "fans", "other", "all"], EnumValues(gpuInclude));

		Assert.Equal(100, tools["get_readings"].GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32());
	}

	[Fact]
	public async Task GetCpu_BindsTheDetailAndIncludeParameters()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		using var full = await server.CallAsync("get_cpu", new() { ["detail"] = "full", ["include"] = s_powerAndRelated });
		using var summary = await server.CallAsync("get_cpu");

		Assert.Equal("full", full.RootElement.GetProperty("detail").GetString());
		Assert.Contains(Labels(full.RootElement.GetProperty("cpus")[0].GetProperty("power")), label => label == "Core 0 Power");
		Assert.True(full.RootElement.TryGetProperty("related", out _));
		Assert.False(full.RootElement.TryGetProperty("omitted", out _));

		Assert.Equal("summary", summary.RootElement.GetProperty("detail").GetString());
		Assert.Equal(["CPU Package Power"], Labels(summary.RootElement.GetProperty("cpus")[0].GetProperty("power")));
		Assert.False(summary.RootElement.TryGetProperty("related", out _));
		Assert.Equal(["related"], summary.RootElement.GetProperty("omitted").EnumerateArray().Select(value => value.GetString()));
	}

	[Fact]
	public async Task GetReadings_BindsAllParameters()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		using var result = await server.CallAsync("get_readings", new()
		{
			["sensor"] = "dgpu",
			["types"] = s_temperatureAndPower,
			["includeStats"] = true,
			["limit"] = 5,
		});

		var root = result.RootElement;
		Assert.Equal(23, root.GetProperty("total").GetInt32());
		Assert.Equal(5, root.GetProperty("returned").GetInt32());
		Assert.True(root.GetProperty("truncated").GetBoolean());
		var first = root.GetProperty("groups")[0].GetProperty("readings")[0];
		Assert.Equal("GPU Temperature", first.GetProperty("label").GetString());
		Assert.Equal("°C", first.GetProperty("unit").GetString());
		Assert.True(first.TryGetProperty("max", out _));
	}

	[Fact]
	public async Task GetGpu_BindsTheTypeParameter()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		using var result = await server.CallAsync("get_gpu", new() { ["type"] = "igpu" });

		Assert.Equal("iGPU [#1]: AMD Radeon 610M", result.RootElement.GetProperty("gpus")[0].GetProperty("name").GetString());
	}

	[Fact]
	public async Task GetGpu_BindsTheIncludeParameter()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		using var withFans = await server.CallAsync("get_gpu", new() { ["include"] = s_fans });
		using var withDefaults = await server.CallAsync("get_gpu");

		var gpu = withFans.RootElement.GetProperty("gpus")[0];
		Assert.True(gpu.TryGetProperty("fans", out _));
		Assert.False(gpu.TryGetProperty("limits", out _));
		Assert.Equal(["power", "limits", "voltages", "currents", "other"], gpu.GetProperty("omitted").EnumerateArray().Select(value => value.GetString()));
		Assert.False(withDefaults.RootElement.GetProperty("gpus")[0].TryGetProperty("fans", out _));
	}

	[Theory]
	[InlineData("hwinfo_status")]
	[InlineData("list_sensors")]
	[InlineData("get_readings")]
	[InlineData("get_cpu")]
	[InlineData("get_gpu")]
	[InlineData("get_memory")]
	[InlineData("list_measurements")]
	public async Task EveryTool_AnswersWithCompactJson(string tool)
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.IntelNvidia);

		var result = await server.Client.CallToolAsync(tool, cancellationToken: TestContext.Current.CancellationToken);

		Assert.NotEqual(true, result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.DoesNotContain('\n', text);
		Assert.DoesNotContain("\\u00B0", text, StringComparison.Ordinal);
		using var json = JsonDocument.Parse(text);
		Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
	}

	[Theory]
	[InlineData("list_sensors")]
	[InlineData("get_readings")]
	[InlineData("get_cpu")]
	[InlineData("get_gpu")]
	[InlineData("get_memory")]
	public async Task DataTools_WithoutHwinfo_ReturnAnErrorThatSaysHowToFixIt(string tool)
	{
		await using var server = await TestServer.StartAsync(SensorSnapshot.Unavailable);

		var result = await server.Client.CallToolAsync(tool, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.Contains("Shared Memory Support", text, StringComparison.Ordinal);
		Assert.Contains("12 hours", text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("list_sensors")]
	[InlineData("get_readings")]
	[InlineData("get_cpu")]
	[InlineData("get_gpu")]
	[InlineData("get_memory")]
	public async Task DataTools_ReadFailed_ReturnAnErrorWithTheReason(string tool)
	{
		await using var server = await TestServer.StartAsync(SensorSnapshot.Failed("Reading HWiNFO's shared memory failed: Broken section."));

		var result = await server.Client.CallToolAsync(tool, cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		using var json = JsonDocument.Parse(text);
		Assert.False(json.RootElement.GetProperty("available").GetBoolean());
		Assert.Contains("Broken section.", json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task InvalidEnumValue_NamesTheAllowedValues()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var result = await server.Client.CallToolAsync(
			"get_gpu",
			new Dictionary<string, object?> { ["include"] = s_unknownSection },
			cancellationToken: TestContext.Current.CancellationToken
		);

		Assert.True(result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.StartsWith("Invalid arguments for 'get_gpu'", text, StringComparison.Ordinal);
		Assert.Contains("include: array of \"power\", \"voltages\", \"currents\", \"limits\", \"fans\", \"other\", \"all\"", text, StringComparison.Ordinal);
		Assert.Contains("type: \"dgpu\", \"igpu\", \"all\"", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InvalidNumber_NamesTheExpectedType()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var result = await server.Client.CallToolAsync(
			"get_readings",
			new Dictionary<string, object?> { ["limit"] = "many" },
			cancellationToken: TestContext.Current.CancellationToken
		);

		Assert.True(result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.Contains("limit: integer", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ValueWithoutFiniteNumber_IsWrittenAsNull()
	{
		var snapshot = new SnapshotBuilder(TestSnapshots.PollTime).Group("Sensor").Power("Broken Power", double.NaN).Build();
		await using var server = await TestServer.StartAsync(snapshot);

		using var result = await server.CallAsync("get_readings");

		var reading = result.RootElement.GetProperty("groups")[0].GetProperty("readings")[0];
		Assert.Equal(JsonValueKind.Null, reading.GetProperty("value").ValueKind);
		Assert.False(reading.TryGetProperty("min", out _));
	}

	[Fact]
	public async Task Status_WithoutHwinfo_IsNoError()
	{
		await using var server = await TestServer.StartAsync(SensorSnapshot.Unavailable);

		using var result = await server.CallAsync("hwinfo_status");

		Assert.False(result.RootElement.GetProperty("available").GetBoolean());
		Assert.Contains("Shared Memory Support", result.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task EveryCall_ReadsFreshData()
	{
		await using var server = await TestServer.StartAsync(SensorSnapshot.Unavailable);

		using (var before = await server.CallAsync("hwinfo_status"))
		{
			Assert.False(before.RootElement.GetProperty("available").GetBoolean());
		}

		// HWiNFO started in the meantime
		server.Source.Snapshot = TestSnapshots.Amd;
		using var after = await server.CallAsync("hwinfo_status");

		Assert.True(after.RootElement.GetProperty("available").GetBoolean());
		Assert.Equal(2, server.Source.Reads);
	}

	[Fact]
	public async Task LabelStyle_DefaultsToOriginalLabels()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.AmdNvidiaGerman);

		using var result = await server.CallAsync("get_cpu");

		Assert.Contains("CPU Package Power", Labels(result.RootElement.GetProperty("cpus")[0].GetProperty("power")));
	}

	[Fact]
	public async Task LabelStyle_User_ReportsTheLabelsHwinfoDisplays()
	{
		var options = HwinfoServerOptions.Parse([HwinfoServerOptions.UserLabelsSwitch], NoEnvironment, out _, out _);
		await using var server = await TestServer.StartAsync(TestSnapshots.AmdNvidiaGerman, options);

		using var result = await server.CallAsync("get_cpu");

		Assert.Contains("CPU-Gesamt-Leistungsaufnahme", Labels(result.RootElement.GetProperty("cpus")[0].GetProperty("power")));
	}

	[Fact]
	public void Options_KeepUnknownArguments()
	{
		var options = HwinfoServerOptions.Parse(["--verbose", "--USER-LABELS"], NoEnvironment, out var unknown, out var invalid);

		Assert.Equal(LabelStyle.User, options.Labels);
		Assert.Equal(["--verbose"], unknown);
		Assert.Empty(invalid);
		Assert.Equal(LabelStyle.Original, HwinfoServerOptions.Parse([], NoEnvironment, out _, out _).Labels);
	}

	[Fact]
	public async Task Measurement_StartStopAndQueryItsStatistics()
	{
		await using var server = await TestServer.StartAsync(CpuSnapshot(TestSnapshots.PollTime, power: 50));

		using (var started = await server.CallAsync("start_measurement", new() { ["name"] = "Gaming" }))
		{
			Assert.Equal("Gaming", started.RootElement.GetProperty("name").GetString());
			Assert.Equal("1 GB", started.RootElement.GetProperty("memoryLimit").GetString());
			Assert.False(started.RootElement.TryGetProperty("warning", out _));
		}

		server.Source.Snapshot = CpuSnapshot(TestSnapshots.PollTime.AddSeconds(1), power: 150);
		using (var stopped = await server.CallAsync("stop_measurement", new() { ["name"] = "gaming" }))
		{
			Assert.Equal(2, stopped.RootElement.GetProperty("snapshots").GetInt32());
			Assert.False(stopped.RootElement.TryGetProperty("warning", out _));
			Assert.Contains("measurement \"Gaming\"", stopped.RootElement.GetProperty("hint").GetString(), StringComparison.Ordinal);
		}

		using var cpu = await server.CallAsync("get_cpu", new() { ["measurement"] = "GAMING" });

		var root = cpu.RootElement;
		Assert.Equal("measurement", root.EnumerateObject().First().Name);
		Assert.Equal("Gaming", root.GetProperty("measurement").GetProperty("name").GetString());
		Assert.False(root.GetProperty("measurement").TryGetProperty("hint", out _));
		var power = root.GetProperty("cpus")[0].GetProperty("power")[0];
		Assert.Equal("CPU Package Power", power.GetProperty("label").GetString());
		Assert.False(power.TryGetProperty("value", out _));
		Assert.Equal(50, power.GetProperty("min").GetDouble());
		Assert.Equal(150, power.GetProperty("max").GetDouble());
		Assert.Equal(100, power.GetProperty("median").GetDouble());
		Assert.Equal(100, power.GetProperty("avg").GetDouble());
		Assert.False(power.TryGetProperty("samples", out _));
	}

	[Theory]
	[InlineData("get_readings")]
	[InlineData("get_cpu")]
	[InlineData("get_gpu")]
	[InlineData("get_memory")]
	public async Task DataTools_UnknownMeasurement_ReturnAnErrorThatSaysWhy(string tool)
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var result = await server.Client.CallToolAsync(
			tool,
			new Dictionary<string, object?> { ["measurement"] = "nope" },
			cancellationToken: TestContext.Current.CancellationToken
		);

		Assert.True(result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.Contains("no stopped measurement named 'nope'", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task StartMeasurement_NameAlreadyRunning_IsAnError()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);
		using var first = await server.CallAsync("start_measurement", new() { ["name"] = "a" });

		var second = await server.Client.CallToolAsync(
			"start_measurement",
			new Dictionary<string, object?> { ["name"] = "A" },
			cancellationToken: TestContext.Current.CancellationToken
		);

		Assert.True(second.IsError);
		Assert.Contains("already running", Assert.IsType<TextContentBlock>(Assert.Single(second.Content)).Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task StartMeasurement_WithoutName_IsAnErrorThatSaysSo()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var result = await server.Client.CallToolAsync("start_measurement", cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(result.IsError);
		var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.StartsWith("Invalid arguments for 'start_measurement'", text, StringComparison.Ordinal);
		Assert.Contains("'name'", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ListMeasurements_ShowsRunningMeasurementsAndTheirMemory()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);
		using var started = await server.CallAsync("start_measurement", new() { ["name"] = "render" });

		using var list = await server.CallAsync("list_measurements");

		var root = list.RootElement;
		var running = Assert.Single(root.GetProperty("running").EnumerateArray());
		Assert.Equal("render", running.GetProperty("name").GetString());
		Assert.Equal(1, running.GetProperty("snapshots").GetInt32());
		Assert.Empty(root.GetProperty("stopped").EnumerateArray());
		Assert.Equal("1 GB", root.GetProperty("memory").GetProperty("limit").GetString());
		Assert.False(root.GetProperty("memory").GetProperty("limitReached").GetBoolean());
	}

	[Fact]
	public async Task MemoryLimit_ComesFromTheOptions()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd, new HwinfoServerOptions(MeasurementMemoryLimit: 1));

		using var started = await server.CallAsync("start_measurement", new() { ["name"] = "a" });

		Assert.Contains("memory limit of 1 bytes is reached", started.RootElement.GetProperty("warning").GetString(), StringComparison.Ordinal);
	}

	private static SensorSnapshot CpuSnapshot(DateTimeOffset pollTime, double power)
	{
		return new SnapshotBuilder(pollTime).Group("CPU [#0]: AMD Ryzen 9 7900X").Power("CPU Package Power", power).Build();
	}

	private static string? NoEnvironment(string name)
	{
		return null;
	}

	private static string[] EnumValues(JsonElement schema)
	{
		return [.. schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];
	}

	private static IEnumerable<string> Labels(JsonElement values)
	{
		return values.EnumerateArray().Select(value => value.GetProperty("label").GetString()!);
	}

	private sealed class TestServer : IAsyncDisposable
	{
		private readonly ServiceProvider _services;
		private readonly CancellationTokenSource _stop;
		private readonly Task _run;

		private TestServer(ServiceProvider services, FakeSensorSource source, McpClient client, CancellationTokenSource stop, Task run)
		{
			_services = services;
			Source = source;
			Client = client;
			_stop = stop;
			_run = run;
		}

		public FakeSensorSource Source { get; }

		public McpClient Client { get; }

		public static async Task<TestServer> StartAsync(SensorSnapshot snapshot, HwinfoServerOptions? options = null)
		{
			Pipe clientToServer = new(), serverToClient = new();
			var source = new FakeSensorSource(snapshot);

			var services = new ServiceCollection();
			services.AddLogging();
			services.AddSingleton<ISensorSource>(source);
			services.AddSingleton<TimeProvider>(TestSnapshots.FreshClock);
			if (options is not null)
			{
				services.AddSingleton(options);
			}

			services.AddHwinfoMcpServer().WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
			var provider = services.BuildServiceProvider();

			var stop = new CancellationTokenSource();
			var run = provider.GetRequiredService<McpServer>().RunAsync(stop.Token);
			var client = await McpClient.CreateAsync(
				new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
				cancellationToken: TestContext.Current.CancellationToken
			);
			return new TestServer(provider, source, client, stop, run);
		}

		public async Task<JsonDocument> CallAsync(string tool, Dictionary<string, object?>? arguments = null)
		{
			var result = await Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
			var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
			Assert.True(result.IsError is null or false, text);
			return JsonDocument.Parse(text);
		}

		public async ValueTask DisposeAsync()
		{
			await Client.DisposeAsync();
			await _stop.CancelAsync();
			try
			{
				await _run;
			}
			catch (OperationCanceledException)
			{
				// Expected when stopping the server
			}

			_stop.Dispose();
			await _services.DisposeAsync();
		}
	}
}
