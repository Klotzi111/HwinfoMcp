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
	private static readonly string[] s_toolNames = ["get_cpu", "get_gpu", "get_memory", "get_readings", "hwinfo_status", "list_sensors"];

	[Fact]
	public async Task Handshake_ReportsServerInfoAndInstructions()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		Assert.Equal(HwinfoMcpServer.Name, server.Client.ServerInfo.Name);
		Assert.Equal(HwinfoMcpServer.Version, server.Client.ServerInfo.Version);
		Assert.Equal(HwinfoMcpServer.Instructions, server.Client.ServerInstructions);
	}

	[Fact]
	public async Task ToolsList_ReturnsAllToolsAsReadOnly()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(s_toolNames, tools.Select(tool => tool.Name).Order());
		Assert.All(tools, tool =>
		{
			Assert.False(string.IsNullOrWhiteSpace(tool.Description));
			Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
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
		Assert.Contains("Temp", EnumValues(types.GetProperty("items")));
		Assert.Contains("Power", EnumValues(types.GetProperty("items")));

		var detail = tools["get_cpu"].GetProperty("properties").GetProperty("detail");
		Assert.Equal(["summary", "full"], EnumValues(detail));
		Assert.Equal("summary", detail.GetProperty("default").GetString());

		var gpuType = tools["get_gpu"].GetProperty("properties").GetProperty("type");
		Assert.Equal(["dgpu", "igpu", "all"], EnumValues(gpuType));
		Assert.Equal("dgpu", gpuType.GetProperty("default").GetString());

		Assert.Equal(100, tools["get_readings"].GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32());
	}

	[Fact]
	public async Task GetCpu_BindsTheDetailParameter()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		using var full = await server.CallAsync("get_cpu", new() { ["detail"] = "full" });
		using var summary = await server.CallAsync("get_cpu");

		Assert.Equal("full", full.RootElement.GetProperty("detail").GetString());
		Assert.Contains(Labels(full.RootElement.GetProperty("cpus")[0].GetProperty("power")), label => label == "Core 0 Power");
		Assert.Equal("summary", summary.RootElement.GetProperty("detail").GetString());
		Assert.DoesNotContain(Labels(summary.RootElement.GetProperty("cpus")[0].GetProperty("power")), label => label == "Core 0 Power");
	}

	[Fact]
	public async Task GetReadings_BindsAllParameters()
	{
		await using var server = await TestServer.StartAsync(TestSnapshots.Amd);

		using var result = await server.CallAsync("get_readings", new()
		{
			["sensor"] = "dgpu",
			["types"] = new[] { "Temp", "Power" },
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

	[Theory]
	[InlineData("hwinfo_status")]
	[InlineData("list_sensors")]
	[InlineData("get_readings")]
	[InlineData("get_cpu")]
	[InlineData("get_gpu")]
	[InlineData("get_memory")]
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
		var options = HwinfoServerOptions.Parse([HwinfoServerOptions.UserLabelsSwitch], out _);
		await using var server = await TestServer.StartAsync(TestSnapshots.AmdNvidiaGerman, options);

		using var result = await server.CallAsync("get_cpu");

		Assert.Contains("CPU-Gesamt-Leistungsaufnahme", Labels(result.RootElement.GetProperty("cpus")[0].GetProperty("power")));
	}

	[Fact]
	public void Options_KeepUnknownArguments()
	{
		var options = HwinfoServerOptions.Parse(["--verbose", "--USER-LABELS"], out var unknown);

		Assert.Equal(LabelStyle.User, options.Labels);
		Assert.Equal(["--verbose"], unknown);
		Assert.Equal(LabelStyle.Original, HwinfoServerOptions.Parse([], out _).Labels);
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
