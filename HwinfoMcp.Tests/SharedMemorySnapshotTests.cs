using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using Hwinfo.SharedMemory;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;

namespace HwinfoMcp.Tests;

/// <summary>
/// Checks the JSON recording the other tests replay against what the real <see cref="SharedMemoryReader"/> makes of
/// the raw shared memory dump it was converted from.
/// </summary>
public sealed class SharedMemorySnapshotTests
{
	[Fact]
	public void JsonRecording_MatchesWhatTheReaderReadsFromTheRawDump()
	{
		var fromDump = ReadRawDump("hwinfo-sm2-snapshot.bin");

		var fromRecording = TestSnapshots.LoadRecording(TestSnapshots.AmdRecording).ToSnapshot();

		Assert.Equal(fromDump.PollTime, fromRecording.PollTime);
		Assert.Equal(fromDump.Sensors, fromRecording.Sensors);
		Assert.Equal(fromDump.Readings, fromRecording.Readings);
	}

	/// <summary>
	/// Reads a raw dump of HWiNFO's shared memory section through the real reader. HWiNFO's section lives in the
	/// Global namespace, which needs a privilege to create, so a session local copy is read through the reader's
	/// internal entry point, which runs the same parsing.
	/// </summary>
	private static SensorSnapshot ReadRawDump(string fileName)
	{
		var data = File.ReadAllBytes(TestSnapshots.TestDataPath(fileName));
		var name = $"Local\\HwinfoMcp.Tests_{Guid.NewGuid():N}";
		using var file = MemoryMappedFile.CreateNew(name, data.Length);
		using (var accessor = file.CreateViewAccessor())
		{
			accessor.WriteArray(0, data, 0, data.Length);
		}

		using var reader = new SharedMemoryReader(new SharedMemoryReaderOptions { StalenessTimeout = TimeSpan.Zero });
		Assert.True(TryReadMemoryMappedFile(reader, name, out var readings), "The dump could not be read.");
		return SensorSnapshot.FromReadings(readings);
	}

	// Hwinfo.SharedMemory.Net 4.0.0 only reads its fixed section names publicly. If an update renames this
	// internal method, only this test fails, with a MissingMethodException; the others replay the JSON recording.
	[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryReadMemoryMappedFile")]
	private static extern bool TryReadMemoryMappedFile(SharedMemoryReader reader, string fileName, out SensorReadings readings);
}
