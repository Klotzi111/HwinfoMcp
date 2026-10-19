using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HwinfoMcp.Tests;

/// <summary>
/// Starts the real server executable and talks to it over stdio, the way Claude Desktop and Claude Code do.
/// It reads the real shared memory, so it passes whether HWiNFO is running or not.
/// </summary>
public sealed class StdioServerTests
{
	[Fact]
	public async Task Server_CompletesTheHandshakeAndListsAllTools()
	{
		var stderr = new ConcurrentQueue<string>();
		var transport = new StdioClientTransport(new StdioClientTransportOptions
		{
			Name = "HwinfoMcp",
			Command = Path.Combine(AppContext.BaseDirectory, "HwinfoMcp.exe"),
			StandardErrorLines = stderr.Enqueue,
		});

		await using (var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken))
		{
			Assert.Equal(HwinfoMcpServer.Name, client.ServerInfo.Name);

			var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
			Assert.Equal(6, tools.Count);

			// Never an error: without HWiNFO the status says so
			var status = await client.CallToolAsync("hwinfo_status", cancellationToken: TestContext.Current.CancellationToken);
			Assert.NotEqual(true, status.IsError);
			using var json = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(status.Content)).Text);
			Assert.True(json.RootElement.TryGetProperty("available", out _));
		}

		// Logs go to stderr, which stdout stayed clean of, or the client would have failed above
		Assert.Contains(stderr, line => line.Contains($"{HwinfoMcpServer.Name} {HwinfoMcpServer.Version} started", StringComparison.Ordinal));
	}
}
