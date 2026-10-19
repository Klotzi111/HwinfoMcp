using Hwinfo.SharedMemory;
using HwinfoMcp.Measurements;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using HwinfoMcp.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HwinfoMcp.Tests;

/// <summary>
/// Drives the recorder by hand: each test sets HWiNFO's current poll and the clock, and tells the recorder when
/// to read, as its timer would every 250 ms.
/// </summary>
public sealed class MeasurementRecorderTests
{
	private const string PowerLabel = "CPU Package Power";
	private const string TemperatureLabel = "CPU (Tctl/Tdie)";

	private static readonly DateTimeOffset s_start = TestSnapshots.PollTime;

	private readonly FakeSensorSource _source = new(SensorSnapshot.Unavailable);
	private readonly ManualTimeProvider _clock = new(s_start);

	[Fact]
	public void Stop_ReportsMinMaxMedianAndAverageOfEveryPoll()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("Gaming");
		PollAndSample(recorder, 2, power: 150);
		PollAndSample(recorder, 4, power: 70);
		// The stop takes HWiNFO's latest poll too
		Poll(6, power: 90);

		var result = recorder.Stop("gaming");

		Assert.Equal("Gaming", result.Name);
		Assert.Equal(4, result.Snapshots);
		Assert.Equal(s_start, result.FirstSnapshot);
		Assert.Equal(s_start.AddSeconds(6), result.LastSnapshot);
		Assert.Equal(new ReadingStats(Min: 50, Max: 150, Median: 80, Avg: 90, Samples: 4), StatsOf(result, PowerLabel));
		Assert.Empty(result.Gaps);
	}

	[Fact]
	public void SamePoll_IsRecordedOnce()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");

		Assert.Equal(SampleOutcome.Unchanged, recorder.Sample());
		Assert.Equal(SampleOutcome.Unchanged, recorder.Sample());
		Assert.Equal(1, recorder.GetOverview().Snapshots);
	}

	[Fact]
	public void SamePollTimeWithOtherValues_IsANewPoll()
	{
		// HWiNFO's poll time only has whole seconds, but it may poll more often
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");
		Poll(0, power: 60);

		Assert.Equal(SampleOutcome.Stored, recorder.Sample());
		Assert.Equal(2, recorder.GetOverview().Snapshots);
	}

	[Fact]
	public void ParallelMeasurements_ShareTheirSnapshots()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");
		PollAndSample(recorder, 2, power: 60);
		// Starts with the poll recorded for "a" already
		recorder.Start("b");
		PollAndSample(recorder, 4, power: 70);

		var overview = recorder.GetOverview();
		Assert.Equal(3, overview.Snapshots);
		Assert.Equal([("a", 3), ("b", 2)], overview.Running.Select(running => (running.Name, running.Snapshots)));

		var a = recorder.Stop("a");
		Assert.Equal(3, a.Snapshots);
		// The first poll was only needed by "a"
		Assert.Equal(2, recorder.GetOverview().Snapshots);

		var b = recorder.Stop("b");
		Assert.Equal(2, b.Snapshots);
		Assert.Equal(new ReadingStats(Min: 60, Max: 70, Median: 65, Avg: 65, Samples: 2), StatsOf(b, PowerLabel));

		overview = recorder.GetOverview();
		Assert.Equal(0, overview.Snapshots);
		Assert.Equal(0, overview.UsedBytes);
	}

	[Fact]
	public void MemoryLimit_DropsNewPolls_AndTheStopSaysWhatIsUnmeasured()
	{
		var recorder = CreateRecorder(memoryLimit: RoomFor(2));
		Poll(0, power: 50);
		recorder.Start("a");
		PollAndSample(recorder, 2, power: 60);
		Poll(4, power: 500);
		Assert.Equal(SampleOutcome.Dropped, recorder.Sample());
		Assert.True(recorder.GetOverview().LimitReached);
		PollAndSample(recorder, 6, power: 700);
		_clock.Now = s_start.AddSeconds(10);

		var result = recorder.Stop("a");

		Assert.Equal(2, result.Snapshots);
		Assert.Equal(60, StatsOf(result, PowerLabel).Max);
		var gap = Assert.Single(result.Gaps);
		Assert.Equal(new UnmeasuredPeriod(s_start.AddSeconds(2), s_start.AddSeconds(10), GapReason.MemoryLimit), gap);

		var summary = MeasurementQueries.Summarize(result, QueryContext.From(_clock));
		Assert.Equal(8, summary.UnmeasuredSeconds);
		Assert.Equal("memoryLimit", Assert.Single(summary.Gaps!).Reason);
		Assert.Contains("memory limit", summary.Warning, StringComparison.Ordinal);
		Assert.Contains("between the last snapshot at 2026-10-19T08:00:02+00:00 and the stop at 2026-10-19T08:00:10+00:00", summary.Warning, StringComparison.Ordinal);
		Assert.Contains("8 seconds", summary.Warning, StringComparison.Ordinal);
	}

	[Fact]
	public void MemoryLimit_RecordingResumesOnceStoppingFreesMemory()
	{
		var recorder = CreateRecorder(memoryLimit: RoomFor(2));
		Poll(0, power: 50);
		recorder.Start("a");
		Poll(2, power: 60);
		recorder.Start("b");
		Poll(4, power: 70);
		Assert.Equal(SampleOutcome.Dropped, recorder.Sample());

		var a = recorder.Stop("a");
		PollAndSample(recorder, 6, power: 80);
		var b = recorder.Stop("b");

		Assert.Equal(GapReason.MemoryLimit, Assert.Single(a.Gaps).Reason);
		Assert.Equal(2, b.Snapshots);
		// "b" started after the poll it starts with, so its gap starts with its start
		Assert.Equal(new UnmeasuredPeriod(s_start.AddSeconds(2.5), s_start.AddSeconds(6), GapReason.MemoryLimit), Assert.Single(b.Gaps));
		Assert.False(recorder.GetOverview().LimitReached);
	}

	[Fact]
	public void StartWhileTheLimitIsReached_SaysSoAndCountsTheGapFromTheStart()
	{
		var recorder = CreateRecorder(memoryLimit: RoomFor(1));
		Poll(0, power: 50);
		recorder.Start("a");
		Poll(2, power: 60);

		var start = recorder.Start("b");

		Assert.Equal(SampleOutcome.Dropped, start.Outcome);
		Assert.Contains("memory limit", MeasurementQueries.Started(start, recorder.MemoryLimit, QueryContext.From(_clock)).Warning, StringComparison.Ordinal);
		_clock.Now = s_start.AddSeconds(5);
		var b = recorder.Stop("b");
		Assert.Equal(0, b.Snapshots);
		Assert.Equal(new UnmeasuredPeriod(s_start.AddSeconds(2.5), s_start.AddSeconds(5), GapReason.MemoryLimit), Assert.Single(b.Gaps));
	}

	[Fact]
	public void StaleData_IsNotRecorded_AndReportedAsAGap()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");
		// HWiNFO stops updating, e.g. after the free version's 12 hours
		_clock.Now = s_start.AddSeconds(120);
		Assert.Equal(SampleOutcome.Stale, recorder.Sample());
		PollAndSample(recorder, 130, power: 60);

		var result = recorder.Stop("a");

		Assert.Equal(2, result.Snapshots);
		var gap = Assert.Single(result.Gaps);
		Assert.Equal(GapReason.NoData, gap.Reason);
		Assert.Equal(s_start.AddSeconds(130), gap.To);
		var summary = MeasurementQueries.Summarize(result, QueryContext.From(_clock));
		Assert.Contains("no new data", summary.Warning, StringComparison.Ordinal);
	}

	[Fact]
	public void StartWithoutHwinfo_RecordsOnceItIsAvailable()
	{
		var recorder = CreateRecorder();

		var start = recorder.Start("a");
		PollAndSample(recorder, 2, power: 60);
		var result = recorder.Stop("a");

		Assert.Equal(SampleOutcome.Unavailable, start.Outcome);
		Assert.Contains("Shared Memory Support", MeasurementQueries.Started(start, recorder.MemoryLimit, QueryContext.From(_clock)).Warning, StringComparison.Ordinal);
		Assert.Equal(1, result.Snapshots);
	}

	[Fact]
	public void StopWithoutSnapshots_KeepsNoResult()
	{
		var recorder = CreateRecorder();
		recorder.Start("a");

		var result = recorder.Stop("a");

		Assert.Equal(0, result.Snapshots);
		Assert.Contains(Messages.NoSnapshots, MeasurementQueries.Summarize(result, QueryContext.From(_clock)).Warning, StringComparison.Ordinal);
		Assert.Throws<MeasurementException>(() => recorder.GetCompleted("a"));
	}

	[Fact]
	public void NonFiniteValues_AreLeftOut()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");
		PollAndSample(recorder, 2, power: double.NaN);
		PollAndSample(recorder, 4, power: 70);

		var result = recorder.Stop("a");

		Assert.Equal(new ReadingStats(Min: 50, Max: 70, Median: 60, Avg: 60, Samples: 2), StatsOf(result, PowerLabel));
	}

	[Fact]
	public void ReadingAddedDuringTheMeasurement_HasFewerSamples()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");
		_source.Snapshot = new SnapshotBuilder(s_start.AddSeconds(2))
			.Group("CPU [#0]: Test CPU").Power(PowerLabel, 60).Temp(TemperatureLabel, 70)
			.Build();
		_clock.Now = s_start.AddSeconds(2.5);
		recorder.Sample();

		var result = recorder.Stop("a");

		Assert.Equal(2, result.Readings.Readings.Length);
		Assert.Equal(2, StatsOf(result, PowerLabel).Samples);
		Assert.Equal(new ReadingStats(Min: 70, Max: 70, Median: 70, Avg: 70, Samples: 1), StatsOf(result, TemperatureLabel));
	}

	[Fact]
	public void Result_IsKeptUntilAMeasurementWithTheSameNameStarts()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("a");
		recorder.Stop("a");

		Assert.Equal("a", recorder.GetCompleted("A").Name);

		recorder.Start("A");
		var exception = Assert.Throws<MeasurementException>(() => recorder.GetCompleted("a"));
		Assert.Contains("still running", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void InvalidRequests_SayWhatIsWrong()
	{
		var recorder = CreateRecorder();
		Poll(0, power: 50);
		recorder.Start("Gaming");

		Assert.Contains("already running", Assert.Throws<MeasurementException>(() => recorder.Start(" gaming ")).Message, StringComparison.Ordinal);
		Assert.Contains("Running: 'Gaming'", Assert.Throws<MeasurementException>(() => recorder.Stop("render")).Message, StringComparison.Ordinal);
		Assert.Equal(Messages.MeasurementNameMissing, Assert.Throws<MeasurementException>(() => recorder.Start("  ")).Message);
		Assert.Throws<MeasurementException>(() => recorder.Start(new string('x', MeasurementRecorder.MaxNameLength + 1)));
		Assert.Contains("no stopped measurement", Assert.Throws<MeasurementException>(() => recorder.GetCompleted("render")).Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Timer_RecordsNewPollsWhileAMeasurementRuns()
	{
		var recorder = new MeasurementRecorder(_source, TimeProvider.System, long.MaxValue, NullLogger<MeasurementRecorder>.Instance, TimeSpan.FromMilliseconds(10));
		await using (recorder)
		{
			var now = DateTimeOffset.UtcNow;
			_source.Snapshot = Snapshot(now, power: 50);
			recorder.Start("a");
			_source.Snapshot = Snapshot(now, power: 60);

			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (recorder.GetOverview().Snapshots < 2 && DateTime.UtcNow < deadline)
			{
				await Task.Delay(10, TestContext.Current.CancellationToken);
			}

			Assert.Equal(2, recorder.Stop("a").Snapshots);
		}
	}

	private MeasurementRecorder CreateRecorder(long memoryLimit = HwinfoServerOptions.DefaultMeasurementMemoryLimit)
	{
		return new MeasurementRecorder(_source, _clock, memoryLimit, NullLogger<MeasurementRecorder>.Instance, sampleInterval: null);
	}

	/// <summary>Makes HWiNFO's current poll one from <paramref name="seconds"/> after the start, read half a second later.</summary>
	private void Poll(double seconds, double power)
	{
		_source.Snapshot = Snapshot(s_start.AddSeconds(seconds), power);
		_clock.Now = s_start.AddSeconds(seconds + 0.5);
	}

	private void PollAndSample(MeasurementRecorder recorder, double seconds, double power)
	{
		Poll(seconds, power);
		recorder.Sample();
	}

	private static SensorSnapshot Snapshot(DateTimeOffset pollTime, double power)
	{
		return new SnapshotBuilder(pollTime).Group("CPU [#0]: Test CPU").Power(PowerLabel, power).Build();
	}

	/// <summary>A memory limit that leaves room for that many snapshots of <see cref="Snapshot"/>.</summary>
	private static long RoomFor(int snapshots)
	{
		var snapshot = Snapshot(s_start, 0);
		return new SnapshotLayout(snapshot).EstimatedBytes + (snapshots * StoredSnapshot.Size(snapshot.Readings.Length));
	}

	private static ReadingStats StatsOf(CompletedMeasurement result, string label)
	{
		return result.StatsOf(result.Readings.Readings.Single(reading => reading.LabelOrig == label));
	}
}
