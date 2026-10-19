using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hwinfo.SharedMemory;
using HwinfoMcp.Queries;
using ModelContextProtocol;

namespace HwinfoMcp.Json;

/// <summary>
/// Source generated JSON contracts for the tool answers and the tool parameters, so nothing is serialized
/// via reflection (required for Native AOT).
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true)]
[JsonSerializable(typeof(StatusResult))]
[JsonSerializable(typeof(UnavailableResult))]
[JsonSerializable(typeof(SensorListResult))]
[JsonSerializable(typeof(ReadingsResult))]
[JsonSerializable(typeof(CpuResult))]
[JsonSerializable(typeof(GpuResult))]
[JsonSerializable(typeof(MemoryResult))]
// Tool parameters
[JsonSerializable(typeof(SensorType[]))]
[JsonSerializable(typeof(CpuDetail))]
[JsonSerializable(typeof(GpuSelection))]
[JsonSerializable(typeof(GpuSection[]))]
[JsonSerializable(typeof(CpuSection[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
public sealed partial class HwinfoJsonContext : JsonSerializerContext;

/// <summary>
/// The JSON settings of the server. Kept apart from <see cref="HwinfoJsonContext"/>, whose generated static
/// members must be initialized before these can be built from them.
/// </summary>
public static class HwinfoJson
{
	/// <summary>
	/// The contracts used to write tool answers. Compact, and with non-ASCII characters like "°C" written
	/// as they are rather than escaped.
	/// </summary>
	public static HwinfoJsonContext Output { get; } = new(
		new JsonSerializerOptions(HwinfoJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
	);

	/// <summary>
	/// The options the MCP SDK binds tool parameters and builds their schemas with: the SDK's defaults,
	/// with <see cref="HwinfoJsonContext"/> in front so the parameter types resolve without reflection.
	/// </summary>
	public static JsonSerializerOptions ToolOptions { get; } = CreateToolOptions();

	private static JsonSerializerOptions CreateToolOptions()
	{
		var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
		options.TypeInfoResolverChain.Insert(0, HwinfoJsonContext.Default);
		options.MakeReadOnly();
		return options;
	}
}
