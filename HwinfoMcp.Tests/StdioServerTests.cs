using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HwinfoMcp.Tests;

/// <summary>
/// Starts the real server executable and talks to it over stdio, the way Claude Desktop and Claude Code do.
/// It reads the real shared memory, so it passes whether HWiNFO is running or not.
/// <para>
/// By default it runs the framework-dependent HwinfoMcp.exe of the build. CI points <see cref="ExecutableVariable"/>
/// at the Native AOT executable, so these tests also check that the trimmed native build works.
/// </para>
/// </summary>
public sealed class StdioServerTests
{
	/// <summary>The environment variable that selects another executable to test, e.g. the Native AOT one.</summary>
	public const string ExecutableVariable = "HWINFOMCP_EXE";

	private static readonly string[] s_powerAndFans = ["power", "fans"];

	private static readonly string[] s_unknownSection = ["bogus"];

	private static readonly string[] s_temperatures = ["Temp"];

	private static readonly string[] s_limitsAndRelated = ["limits", "related"];

	// On dispose the SDK's client waits this long for the server to exit before it closes stdin and kills the
	// server, which would otherwise cost every test the default 5 seconds. The server itself exits on stdin EOF.
	private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromMilliseconds(200);

	private static string Executable => Environment.GetEnvironmentVariable(ExecutableVariable) is { Length: > 0 } path
		? path
		: Path.Combine(AppContext.BaseDirectory, "HwinfoMcp.exe");

	[Fact]
	public async Task Server_CompletesTheHandshakeAndListsAllTools()
	{
		var stderr = new ConcurrentQueue<string>();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var startedLine = $"{HwinfoMcpServer.Name} {HwinfoMcpServer.Version} started";
		var transport = new StdioClientTransport(new StdioClientTransportOptions
		{
			Name = "HwinfoMcp",
			Command = Executable,
			ShutdownTimeout = s_shutdownTimeout,
			StandardErrorLines = line =>
			{
				stderr.Enqueue(line);
				if (line.Contains(startedLine, StringComparison.Ordinal))
				{
					started.TrySetResult();
				}
			},
		});

		await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal(HwinfoMcpServer.Name, client.ServerInfo.Name);
		Assert.Equal(HwinfoMcpServer.Version, client.ServerInfo.Version);
		var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
		Assert.Equal(9, tools.Count);

		// Never an error: without HWiNFO the status says so
		var status = await client.CallToolAsync("hwinfo_status", cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotEqual(true, status.IsError);
		using var json = JsonDocument.Parse(Text(status));
		Assert.True(json.RootElement.TryGetProperty("available", out _));

		// Logs go to stderr, which stdout stayed clean of, or the client would have failed above. The console
		// logger writes in the background, so the line may arrive after the answers.
		await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		Assert.DoesNotContain(stderr, line => line.Contains("request handler", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("list_sensors")]
	[InlineData("get_readings")]
	[InlineData("get_cpu")]
	[InlineData("get_gpu")]
	[InlineData("get_memory")]
	public async Task EveryTool_BindsItsParametersAndAnswersWithJson(string tool)
	{
		Dictionary<string, object?> arguments = tool switch
		{
			"get_readings" => new() { ["sensor"] = "CPU", ["types"] = s_temperatures, ["includeStats"] = true, ["limit"] = 5 },
			"get_cpu" => new() { ["detail"] = "full", ["include"] = s_limitsAndRelated, ["includeStats"] = true },
			"get_gpu" => new() { ["type"] = "all", ["include"] = s_powerAndFans, ["includeStats"] = true },
			"get_memory" => new() { ["includeStats"] = true },
			_ => [],
		};
		await using var client = await StartAsync();

		var result = await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

		// With HWiNFO the answer, without it an error object that says how to fix it
		using var json = JsonDocument.Parse(Text(result));
		if (result.IsError is true)
		{
			Assert.False(json.RootElement.GetProperty("available").GetBoolean());
		}
		else
		{
			Assert.True(json.RootElement.TryGetProperty("pollTime", out _));
		}
	}

	[Fact]
	public async Task InvalidArguments_AreAnsweredWithTheAllowedValues()
	{
		await using var client = await StartAsync();

		var result = await client.CallToolAsync(
			"get_gpu",
			new Dictionary<string, object?> { ["include"] = s_unknownSection },
			cancellationToken: TestContext.Current.CancellationToken
		);

		Assert.True(result.IsError);
		Assert.Contains("\"fans\"", Text(result), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Version_PrintsTheVersion()
	{
		var (exitCode, stdout) = await RunAsync("--version");

		Assert.Equal(0, exitCode);
		Assert.Equal(HwinfoMcpServer.Version, stdout.Trim());
	}

	[Fact]
	public async Task Help_PrintsTheUsage()
	{
		var (exitCode, stdout) = await RunAsync("--HELP");

		Assert.Equal(0, exitCode);
		Assert.Contains("--dump <file.json>", stdout, StringComparison.Ordinal);
		Assert.Contains(HwinfoServerOptions.UserLabelsSwitch, stdout, StringComparison.Ordinal);
		Assert.Contains(HwinfoServerOptions.MeasurementMemoryLimitSwitch, stdout, StringComparison.Ordinal);
		Assert.Contains(HwinfoServerOptions.MeasurementMemoryLimitVariable, stdout, StringComparison.Ordinal);
	}

	private static async Task<McpClient> StartAsync()
	{
		var transport = new StdioClientTransport(new StdioClientTransportOptions
		{
			Name = "HwinfoMcp",
			Command = Executable,
			ShutdownTimeout = s_shutdownTimeout,
		});
		return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
	}

	private static async Task<(int ExitCode, string Stdout)> RunAsync(params string[] args)
	{
		var startInfo = new ProcessStartInfo(Executable) { RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var arg in args)
		{
			startInfo.ArgumentList.Add(arg);
		}

		using var process = Process.Start(startInfo)!;
		var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		await process.WaitForExitAsync(TestContext.Current.CancellationToken);
		await stderr;
		return (process.ExitCode, await stdout);
	}

	private static string Text(CallToolResult result)
	{
		return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
	}
}
