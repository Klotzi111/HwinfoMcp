using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HwinfoMcp.Tools;

/// <summary>
/// The answer to a tool call whose arguments can't be bound, e.g. an unknown section in get_gpu's include or a
/// missing required argument. It names the allowed values from the tool's input schema, so the client can correct
/// the call.
/// </summary>
public static class ToolArgumentErrors
{
	/// <summary>Creates the error result for a tool call whose arguments failed to bind.</summary>
	/// <param name="context">The tool call.</param>
	/// <param name="exception">The error the binding reported.</param>
	public static CallToolResult Create(RequestContext<CallToolRequestParams> context, Exception exception)
	{
		var tool = context.MatchedPrimitive as McpServerTool;
		return Create(context.Params?.Name, tool?.ProtocolTool.InputSchema, exception);
	}

	/// <summary>Creates the error result for a tool call whose arguments failed to bind.</summary>
	/// <param name="toolName">The name of the tool.</param>
	/// <param name="inputSchema">The tool's input schema, if known.</param>
	/// <param name="exception">The error the binding reported.</param>
	public static CallToolResult Create(string? toolName, JsonElement? inputSchema, Exception exception)
	{
		var text = new StringBuilder($"Invalid arguments for '{toolName}': {exception.Message}");
		if (inputSchema is { } schema
			&& schema.ValueKind == JsonValueKind.Object
			&& schema.TryGetProperty("properties", out var properties)
			&& properties.ValueKind == JsonValueKind.Object)
		{
			text.Append(" Allowed values: ");
			text.AppendJoin("; ", properties.EnumerateObject().Select(property => $"{property.Name}: {Describe(property.Value)}"));
			text.Append('.');
		}

		return new CallToolResult
		{
			IsError = true,
			Content = [new TextContentBlock { Text = text.ToString() }],
		};
	}

	/// <summary>Describes the values a schema allows, e.g. <c>"dgpu", "igpu", "all"</c> or <c>array of "Temp", ...</c>.</summary>
	private static string Describe(JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object)
		{
			return "any";
		}

		if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
		{
			return string.Join(", ", values.EnumerateArray().Select(value => value.GetRawText()));
		}

		if (schema.TryGetProperty("items", out var items))
		{
			return $"array of {Describe(items)}";
		}

		if (schema.TryGetProperty("type", out var type))
		{
			return type.ValueKind == JsonValueKind.Array
				? string.Join(" or ", type.EnumerateArray().Select(name => name.GetString()))
				: type.GetString() ?? "any";
		}

		return "any";
	}
}
