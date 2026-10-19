using HwinfoMcp.Json;
using ModelContextProtocol.Protocol;

namespace HwinfoMcp.Tools;

/// <summary>Wraps tool answers as compact JSON text.</summary>
internal static class ToolResults
{
	/// <summary>An answer.</summary>
	public static CallToolResult Success<T>(T result)
	{
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = HwinfoJson.Serialize(result) }],
		};
	}

	/// <summary>An answer that tells the client the call failed.</summary>
	public static CallToolResult Error<T>(T result)
	{
		return new CallToolResult
		{
			IsError = true,
			Content = [new TextContentBlock { Text = HwinfoJson.Serialize(result) }],
		};
	}
}
