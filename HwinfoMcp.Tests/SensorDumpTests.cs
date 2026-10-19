using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;

namespace HwinfoMcp.Tests;

public sealed class SensorDumpTests
{
	[Fact]
	public void Dump_RoundTripsASnapshot()
	{
		var original = TestSnapshots.Amd;

		using var stream = new MemoryStream();
		SensorDump.FromSnapshot(original, "AMD reference").Save(stream);
		stream.Position = 0;
		var dump = SensorDump.Load(stream);
		var replayed = dump.ToSnapshot();

		Assert.Equal("AMD reference", dump.Description);
		Assert.Equal(original.PollTime, replayed.PollTime);
		Assert.Equal(original.Sensors, replayed.Sensors);
		Assert.Equal(original.Readings.Length, replayed.Readings.Length);
		for (var i = 0; i < original.Readings.Length; i++)
		{
			// Records compare by value, including the sensor each reading belongs to
			Assert.Equal(original.Readings[i], replayed.Readings[i]);
		}
	}

	[Fact]
	public void Dump_OnlyStoresUserNamesThatDiffer()
	{
		var dump = SensorDump.FromSnapshot(TestSnapshots.Amd);

		var renamed = dump.Sensors.Where(group => group.NameUser is not null).ToList();
		Assert.Equal(2, renamed.Count);
		Assert.Equal("PresentMon [dwm.exe]", Assert.Single(renamed, group => group.NameOrig == "PresentMon").NameUser);
		Assert.All(dump.Sensors.SelectMany(group => group.Readings), reading => Assert.Null(reading.LabelUser));
	}

	[Fact]
	public void Dump_WritesUnitsUnescaped()
	{
		using var stream = new MemoryStream();
		SensorDump.FromSnapshot(TestSnapshots.Amd).Save(stream);

		var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
		Assert.Contains("\"unit\": \"°C\"", json, StringComparison.Ordinal);
		Assert.Contains("\"type\": \"Temp\"", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Dump_EndsWithALineBreak()
	{
		using var stream = new MemoryStream();
		SensorDump.FromSnapshot(TestSnapshots.IntelNvidia).Save(stream);

		var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
		Assert.EndsWith("}" + Environment.NewLine, json, StringComparison.Ordinal);
	}
}
