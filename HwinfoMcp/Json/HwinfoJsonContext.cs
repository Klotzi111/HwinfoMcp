using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
[JsonSerializable(typeof(StartMeasurementResult))]
[JsonSerializable(typeof(MeasurementSummary))]
[JsonSerializable(typeof(MeasurementListResult))]
[JsonSerializable(typeof(MeasurementErrorResult))]
// Tool parameters
[JsonSerializable(typeof(MeasurementType[]))]
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
	/// The options tool answers are written with. Compact, with non-ASCII characters like "°C" written as they are
	/// rather than escaped, and without the current value of the readings of a measurement.
	/// </summary>
	public static JsonSerializerOptions Output { get; } = CreateOutputOptions();

	/// <summary>
	/// The options the MCP SDK binds tool parameters and builds their schemas with: the SDK's defaults,
	/// with <see cref="HwinfoJsonContext"/> in front so the parameter types resolve without reflection.
	/// </summary>
	public static JsonSerializerOptions ToolOptions { get; } = CreateToolOptions();

	/// <summary>Writes a tool answer with the <see cref="Output"/> options.</summary>
	/// <typeparam name="T">A type registered with <see cref="HwinfoJsonContext"/>.</typeparam>
	public static string Serialize<T>(T value)
	{
		return JsonSerializer.Serialize(value, (JsonTypeInfo<T>)Output.GetTypeInfo(typeof(T)));
	}

	private static JsonSerializerOptions CreateOutputOptions()
	{
		var options = new JsonSerializerOptions(HwinfoJsonContext.Default.Options)
		{
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			TypeInfoResolver = HwinfoJsonContext.Default.WithAddedModifier(LeaveOutValueOfMeasuredReadings),
		};
		options.MakeReadOnly();
		return options;
	}

	/// <summary>
	/// A current reading's value is written even when it is null, so every current reading has one. The readings
	/// of a measurement have none; their min, max, median and average take its place.
	/// </summary>
	private static void LeaveOutValueOfMeasuredReadings(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Type != typeof(ReadingValue))
		{
			return;
		}

		foreach (var property in typeInfo.Properties)
		{
			if (property.Name == "value")
			{
				property.ShouldSerialize = static (reading, _) => !((ReadingValue)reading).Measured;
			}
		}
	}

	private static JsonSerializerOptions CreateToolOptions()
	{
		var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
		options.TypeInfoResolverChain.Insert(0, HwinfoJsonContext.Default);
		options.MakeReadOnly();
		return options;
	}
}
