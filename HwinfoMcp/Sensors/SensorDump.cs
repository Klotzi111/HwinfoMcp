using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hwinfo.SharedMemory;

namespace HwinfoMcp.Sensors;

/// <summary>
/// A sensor snapshot as a JSON document: written by <c>HwinfoMcp.exe --dump</c> to record a system's sensor
/// data, and read by the tests to replay it.
/// </summary>
/// <param name="Description">What the data is, e.g. which hardware it was recorded on.</param>
/// <param name="PollTime">When HWiNFO polled the data.</param>
/// <param name="Sensors">The sensor groups with their readings, in HWiNFO's order.</param>
public sealed record SensorDump(string? Description, DateTimeOffset PollTime, IReadOnlyList<SensorDumpGroup> Sensors)
{
	/// <summary>Records a snapshot that carries data.</summary>
	public static SensorDump FromSnapshot(SensorSnapshot snapshot, string? description = null)
	{
		var readings = snapshot.Readings.ToLookup(reading => reading.Sensor);
		return new SensorDump(
			description,
			snapshot.PollTime,
			[
				.. snapshot.Sensors.Select(sensor => new SensorDumpGroup(
					sensor.Id,
					sensor.Instance,
					sensor.NameOrig,
					sensor.NameUser == sensor.NameOrig ? null : sensor.NameUser,
					[
						.. readings[sensor].Select(reading => new SensorDumpReading(
							reading.ReadingId,
							reading.ReadingType,
							reading.LabelOrig,
							reading.LabelUser == reading.LabelOrig ? null : reading.LabelUser,
							reading.Unit,
							reading.Value,
							reading.ValueMin,
							reading.ValueMax,
							reading.ValueAvg
						)),
					]
				)),
			]
		);
	}

	/// <summary>Replays the recorded data as a snapshot, optionally with another poll time.</summary>
	public SensorSnapshot ToSnapshot(DateTimeOffset? pollTime = null)
	{
		var sensors = ImmutableArray.CreateBuilder<Sensor>(Sensors.Count);
		var readings = ImmutableArray.CreateBuilder<SensorReading>();
		foreach (var group in Sensors)
		{
			// One instance per group, shared by its readings, like the shared memory reader does
			var sensor = new Sensor(group.Id, group.Instance, group.NameOrig, group.NameUser ?? group.NameOrig);
			sensors.Add(sensor);
			foreach (var reading in group.Readings)
			{
				readings.Add(new SensorReading(
					reading.Id,
					reading.Type,
					reading.LabelOrig,
					reading.LabelUser ?? reading.LabelOrig,
					reading.Unit,
					reading.Value,
					reading.Min,
					reading.Max,
					reading.Avg,
					sensor
				));
			}
		}

		return new SensorSnapshot(SnapshotState.Available, pollTime ?? PollTime, readings.ToImmutable(), sensors.ToImmutable());
	}

	/// <summary>Writes the dump as indented JSON.</summary>
	public void Save(Stream stream)
	{
		JsonSerializer.Serialize(stream, this, s_json.SensorDump);
	}

	/// <summary>Reads a dump written by <see cref="Save"/>.</summary>
	public static SensorDump Load(Stream stream)
	{
		return JsonSerializer.Deserialize(stream, s_json.SensorDump)
			?? throw new InvalidDataException("The sensor dump is empty.");
	}

	// Non-ASCII characters like "°C" are written as they are rather than escaped
	private static readonly SensorDumpJsonContext s_json = new(
		new JsonSerializerOptions(SensorDumpJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
	);
}

/// <summary>A sensor group of a <see cref="SensorDump"/>.</summary>
/// <param name="NameUser">The name displayed in HWiNFO, if it differs from the original name.</param>
public sealed record SensorDumpGroup(
	uint Id,
	uint Instance,
	string NameOrig,
	string? NameUser,
	IReadOnlyList<SensorDumpReading> Readings);

/// <summary>A reading of a <see cref="SensorDump"/>.</summary>
/// <param name="LabelUser">The label displayed in HWiNFO, if it differs from the original label.</param>
public sealed record SensorDumpReading(
	uint Id,
	SensorType Type,
	string LabelOrig,
	string? LabelUser,
	string Unit,
	double Value,
	double Min,
	double Max,
	double Avg);

/// <summary>The source generated JSON contracts of <see cref="SensorDump"/>.</summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
	UseStringEnumConverter = true,
	WriteIndented = true,
	IndentCharacter = '\t',
	IndentSize = 1)]
[JsonSerializable(typeof(SensorDump))]
public sealed partial class SensorDumpJsonContext : JsonSerializerContext;
