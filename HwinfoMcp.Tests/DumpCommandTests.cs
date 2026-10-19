using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HwinfoMcp.Tests;

public sealed class DumpCommandTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), $"HwinfoMcp.Tests_{Guid.NewGuid():N}");

	public DumpCommandTests()
	{
		Directory.CreateDirectory(_folder);
	}

	public void Dispose()
	{
		Directory.Delete(_folder, recursive: true);
	}

	[Fact]
	public void Dump_WritesTheSnapshotWithItsDescription()
	{
		var path = Path.Combine(_folder, "system.json");

		var exitCode = Run([path, "Intel + NVIDIA"], TestSnapshots.IntelNvidia);

		Assert.Equal(0, exitCode);
		using var stream = File.OpenRead(path);
		var dump = SensorDump.Load(stream);
		Assert.Equal("Intel + NVIDIA", dump.Description);
		Assert.Equal(TestSnapshots.IntelNvidia.Readings.Length, dump.Sensors.Sum(group => group.Readings.Count));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(3)]
	public void WrongNumberOfArguments_IsAUsageError(int count)
	{
		string[] args = [.. Enumerable.Range(0, count).Select(index => Path.Combine(_folder, $"{index}.json"))];

		Assert.Equal(2, Run(args, TestSnapshots.IntelNvidia));
		Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
	}

	[Fact]
	public void WithoutHwinfo_NothingIsWritten()
	{
		var path = Path.Combine(_folder, "system.json");

		Assert.Equal(1, Run([path], SensorSnapshot.Unavailable));
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void MissingFolder_IsReportedWithoutCrashing()
	{
		var path = Path.Combine(_folder, "missing", "system.json");

		Assert.Equal(1, Run([path], TestSnapshots.IntelNvidia));
	}

	private static int Run(string[] args, SensorSnapshot snapshot)
	{
		return DumpCommand.Run(args, new FakeSensorSource(snapshot), NullLogger.Instance);
	}
}
