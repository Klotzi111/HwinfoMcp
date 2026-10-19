using HwinfoMcp.Catalog;
using HwinfoMcp.Queries;
using HwinfoMcp.Sensors;
using Microsoft.Extensions.Logging;

namespace HwinfoMcp.Measurements;

/// <summary>What became of a poll read from HWiNFO's shared memory.</summary>
public enum SampleOutcome
{
	/// <summary>The poll was new and is recorded.</summary>
	Stored,

	/// <summary>The poll is recorded already.</summary>
	Unchanged,

	/// <summary>HWiNFO's data isn't available.</summary>
	Unavailable,

	/// <summary>HWiNFO hasn't updated its data for longer than <see cref="SensorPatterns.StaleAfter"/>.</summary>
	Stale,

	/// <summary>The poll was new, but not recorded because the memory limit is reached.</summary>
	Dropped,
}

/// <summary>A measurement that was just started.</summary>
/// <param name="Name">The name, as given.</param>
/// <param name="Start">When it started.</param>
/// <param name="Outcome">What became of the poll current at the start.</param>
/// <param name="Current">The data read at the start.</param>
/// <param name="Running">The names of all running measurements, this one included.</param>
public sealed record MeasurementStart(
	string Name,
	DateTimeOffset Start,
	SampleOutcome Outcome,
	SensorSnapshot Current,
	IReadOnlyList<string> Running);

/// <summary>A running measurement.</summary>
/// <param name="Name">The name, as given.</param>
/// <param name="Start">When it started.</param>
/// <param name="Snapshots">How many snapshots it has so far.</param>
public sealed record RunningMeasurementInfo(string Name, DateTimeOffset Start, int Snapshots);

/// <summary>The state of all measurements.</summary>
/// <param name="Running">The running measurements, oldest first.</param>
/// <param name="Completed">The stopped measurements whose results are kept, oldest first.</param>
/// <param name="Snapshots">How many snapshots are recorded for the running measurements.</param>
/// <param name="UsedBytes">Roughly how much memory the recorded snapshots take.</param>
/// <param name="MemoryLimit">The most memory the recorded snapshots may take.</param>
/// <param name="LimitReached">Whether the last poll was dropped because the memory limit is reached.</param>
public sealed record MeasurementOverview(
	IReadOnlyList<RunningMeasurementInfo> Running,
	IReadOnlyList<CompletedMeasurement> Completed,
	int Snapshots,
	long UsedBytes,
	long MemoryLimit,
	bool LimitReached);

/// <summary>
/// A measurement request that can't be carried out, e.g. stopping a measurement that doesn't run. The message
/// says why, for the tool answer.
/// </summary>
public sealed class MeasurementException(string message) : Exception(message);

/// <summary>
/// Records named measurements. While at least one runs, it reads HWiNFO's shared memory every
/// <see cref="SampleInterval"/> and records each new poll once, in a timeline all running measurements share: a
/// measurement only remembers where in it it starts. Snapshots no running measurement needs any more are
/// released. Measurements are evaluated when they stop, and their results kept for the queries.
/// <para>
/// The recorded snapshots stay below a memory limit. Once it is reached, new polls are dropped until stopping
/// measurements frees memory, and the affected measurements report the periods they have no snapshots for.
/// </para>
/// </summary>
public sealed partial class MeasurementRecorder : IDisposable, IAsyncDisposable
{
	/// <summary>How often HWiNFO's shared memory is read while a measurement runs.</summary>
	public static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

	/// <summary>The longest name a measurement may have.</summary>
	public const int MaxNameLength = 100;

	/// <summary>How many results of stopped measurements are kept. Beyond that, the oldest ones go.</summary>
	public const int MaxKeptResults = 100;

	private readonly ISensorSource _source;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<MeasurementRecorder> _logger;
	private readonly TimeSpan? _sampleInterval;
	private readonly Lock _gate = new();

	// The snapshots of the running measurements, oldest first, numbered consecutively
	private readonly List<StoredSnapshot> _timeline = [];

	// How many recorded snapshots share each layout, so a layout's memory counts once
	private readonly Dictionary<SnapshotLayout, int> _layoutUsers = new(ReferenceEqualityComparer.Instance);
	private readonly Dictionary<string, RunningMeasurement> _running = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CompletedMeasurement> _completed = new(StringComparer.OrdinalIgnoreCase);
	private long _nextSequence;
	private long _usedBytes;
	private bool _dropping;
	private CancellationTokenSource? _samplingStop;
	private Task _sampling = Task.CompletedTask;
	private bool _disposed;

	/// <summary>Creates the recorder of the server.</summary>
	/// <param name="source">Where the sensor data comes from.</param>
	/// <param name="timeProvider">The clock, also for the reads while measurements run.</param>
	/// <param name="options">The server settings, with the memory limit.</param>
	/// <param name="logger">Logs measurements and the memory limit.</param>
	public MeasurementRecorder(ISensorSource source, TimeProvider timeProvider, HwinfoServerOptions options, ILogger<MeasurementRecorder> logger)
		: this(source, timeProvider, options.MeasurementMemoryLimit, logger, SampleInterval)
	{
	}

	/// <summary>Creates a recorder that reads at another interval, or only when told to.</summary>
	/// <param name="source">Where the sensor data comes from.</param>
	/// <param name="timeProvider">The clock, also for the reads while measurements run.</param>
	/// <param name="memoryLimit">The most memory the recorded snapshots may take.</param>
	/// <param name="logger">Logs measurements and the memory limit.</param>
	/// <param name="sampleInterval">How often to read while measurements run. Null to read only on <see cref="Sample"/>.</param>
	internal MeasurementRecorder(
		ISensorSource source,
		TimeProvider timeProvider,
		long memoryLimit,
		ILogger<MeasurementRecorder> logger,
		TimeSpan? sampleInterval)
	{
		_source = source;
		_timeProvider = timeProvider;
		_logger = logger;
		_sampleInterval = sampleInterval;
		MemoryLimit = memoryLimit;
	}

	/// <summary>The most memory the recorded snapshots may take.</summary>
	public long MemoryLimit { get; }

	/// <summary>
	/// Starts a measurement. It includes HWiNFO's current poll and every new one until it is stopped. Replaces the
	/// result of an earlier measurement with the same name.
	/// </summary>
	/// <param name="name">The name. Case-insensitive, unique among running measurements.</param>
	/// <exception cref="MeasurementException">The name is missing, too long or already running.</exception>
	public MeasurementStart Start(string? name)
	{
		var checkedName = CheckName(name);
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_running.ContainsKey(checkedName))
			{
				throw new MeasurementException(Messages.MeasurementAlreadyRunning(checkedName));
			}

			var now = _timeProvider.GetUtcNow();
			// Running before the read, so a dropped poll counts for it too
			var measurement = new RunningMeasurement(checkedName, now, _nextSequence);
			_running.Add(checkedName, measurement);
			_completed.Remove(checkedName);
			var (outcome, current) = SampleLocked(now);
			if (outcome is SampleOutcome.Stored or SampleOutcome.Unchanged)
			{
				measurement.FirstSequence = _timeline[^1].Sequence;
			}

			StartSampling();
			LogStarted(_logger, checkedName, _running.Count);
			return new MeasurementStart(checkedName, now, outcome, current, [.. _running.Values.Select(running => running.Name)]);
		}
	}

	/// <summary>
	/// Stops a measurement, including HWiNFO's latest poll, and evaluates it. The result is kept for
	/// <see cref="GetCompleted"/> if the measurement has snapshots.
	/// </summary>
	/// <param name="name">The name, case-insensitive.</param>
	/// <exception cref="MeasurementException">No measurement with that name runs.</exception>
	public CompletedMeasurement Stop(string? name)
	{
		var checkedName = CheckName(name);
		RunningMeasurement measurement;
		List<StoredSnapshot> snapshots;
		DateTimeOffset now;
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (!_running.TryGetValue(checkedName, out var running))
			{
				throw new MeasurementException(Messages.MeasurementNotRunning(checkedName, RunningNames()));
			}

			measurement = running;
			now = _timeProvider.GetUtcNow();
			SampleLocked(now);
			_running.Remove(checkedName);
			var first = IndexOf(measurement.FirstSequence);
			snapshots = _timeline.GetRange(first, _timeline.Count - first);
			ReleaseUnneeded();
		}

		// Evaluated outside the lock, so the other measurements keep recording meanwhile
		var completed = CompletedMeasurement.Evaluate(measurement, now, snapshots, MemoryLimit);
		LogStopped(_logger, completed.Name, completed.Duration, completed.Snapshots);
		if (completed.Snapshots > 0)
		{
			lock (_gate)
			{
				if (!_disposed)
				{
					Keep(completed);
				}
			}
		}

		return completed;
	}

	/// <summary>Gets the result of a stopped measurement.</summary>
	/// <param name="name">The name, case-insensitive.</param>
	/// <exception cref="MeasurementException">There is no result with that name.</exception>
	public CompletedMeasurement GetCompleted(string? name)
	{
		var checkedName = CheckName(name);
		lock (_gate)
		{
			if (_completed.TryGetValue(checkedName, out var completed))
			{
				return completed;
			}

			throw new MeasurementException(Messages.MeasurementNotFound(
				checkedName,
				_running.ContainsKey(checkedName),
				[.. _completed.Values.Select(result => result.Name)]
			));
		}
	}

	/// <summary>Describes the running and the kept measurements and the memory they take.</summary>
	public MeasurementOverview GetOverview()
	{
		lock (_gate)
		{
			var running = _running.Values
				.OrderBy(measurement => measurement.Start)
				.Select(measurement => new RunningMeasurementInfo(
					measurement.Name,
					measurement.Start,
					_timeline.Count - IndexOf(measurement.FirstSequence)
				))
				.ToList();
			var completed = _completed.Values.OrderBy(result => result.Stop).ToList();
			return new MeasurementOverview(running, completed, _timeline.Count, _usedBytes, MemoryLimit, _dropping);
		}
	}

	/// <summary>Reads HWiNFO's shared memory once and records the poll if it is new and a measurement runs.</summary>
	internal SampleOutcome Sample()
	{
		lock (_gate)
		{
			return _running.Count == 0 ? SampleOutcome.Unchanged : SampleLocked(_timeProvider.GetUtcNow()).Outcome;
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			StopSampling();
			_running.Clear();
			_completed.Clear();
			_timeline.Clear();
			_layoutUsers.Clear();
			_usedBytes = 0;
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		Task sampling;
		lock (_gate)
		{
			sampling = _sampling;
		}

		Dispose();
		await sampling.ConfigureAwait(false);
	}

	private static string CheckName(string? name)
	{
		var trimmed = name?.Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			throw new MeasurementException(Messages.MeasurementNameMissing);
		}

		return trimmed.Length <= MaxNameLength
			? trimmed
			: throw new MeasurementException(Messages.MeasurementNameTooLong(MaxNameLength));
	}

	/// <summary>Reads HWiNFO's shared memory and records the poll if it is new. Call with the lock held.</summary>
	private (SampleOutcome Outcome, SensorSnapshot Read) SampleLocked(DateTimeOffset now)
	{
		var read = _source.Read();
		if (!read.IsAvailable)
		{
			return (SampleOutcome.Unavailable, read);
		}

		// Frozen values, e.g. after the free version's 12 hours, are no measurement
		if (now - read.PollTime > SensorPatterns.StaleAfter)
		{
			return (SampleOutcome.Stale, read);
		}

		// HWiNFO's poll time only has whole seconds, so a poll is new if the time or any value changed
		var last = _timeline.Count > 0 ? _timeline[^1] : null;
		var layout = last is not null && last.Layout.Matches(read) ? last.Layout : null;
		if (layout is not null && last!.PollTime == read.PollTime && HasSameValues(last, read))
		{
			return (SampleOutcome.Unchanged, read);
		}

		layout ??= new SnapshotLayout(read);
		var bytes = StoredSnapshot.Size(read.Readings.Length);
		var layoutBytes = _layoutUsers.ContainsKey(layout) ? 0 : layout.EstimatedBytes;
		if (_usedBytes + bytes + layoutBytes > MemoryLimit)
		{
			Drop();
			return (SampleOutcome.Dropped, read);
		}

		var values = new double[read.Readings.Length];
		for (var i = 0; i < values.Length; i++)
		{
			values[i] = read.Readings[i].Value;
		}

		_timeline.Add(new StoredSnapshot(_nextSequence++, read.PollTime, layout, values));
		_layoutUsers[layout] = _layoutUsers.GetValueOrDefault(layout) + 1;
		_usedBytes += bytes + layoutBytes;
		foreach (var measurement in _running.Values)
		{
			measurement.Dropping = false;
		}

		if (_dropping)
		{
			_dropping = false;
			LogRecordingAgain(_logger);
		}

		return (SampleOutcome.Stored, read);
	}

	private static bool HasSameValues(StoredSnapshot snapshot, SensorSnapshot read)
	{
		for (var i = 0; i < snapshot.Values.Length; i++)
		{
			// Equals rather than ==, so NaN equals NaN
			if (!snapshot.Values[i].Equals(read.Readings[i].Value))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Notes for every running measurement that a poll was dropped.</summary>
	private void Drop()
	{
		if (!_dropping)
		{
			_dropping = true;
			LogLimitReached(_logger, ByteSize.Format(MemoryLimit));
		}

		foreach (var measurement in _running.Values)
		{
			if (!measurement.Dropping)
			{
				measurement.Dropping = true;
				measurement.DroppedAfter.Add(_nextSequence - 1);
			}
		}
	}

	/// <summary>Releases the snapshots before the first one a running measurement needs.</summary>
	private void ReleaseUnneeded()
	{
		if (_running.Count == 0)
		{
			StopSampling();
			_timeline.Clear();
			_layoutUsers.Clear();
			_usedBytes = 0;
			_dropping = false;
			return;
		}

		var count = IndexOf(_running.Values.Min(measurement => measurement.FirstSequence));
		for (var i = 0; i < count; i++)
		{
			var snapshot = _timeline[i];
			_usedBytes -= snapshot.Bytes;
			var users = _layoutUsers[snapshot.Layout] - 1;
			if (users == 0)
			{
				_layoutUsers.Remove(snapshot.Layout);
				_usedBytes -= snapshot.Layout.EstimatedBytes;
			}
			else
			{
				_layoutUsers[snapshot.Layout] = users;
			}
		}

		_timeline.RemoveRange(0, count);
	}

	/// <summary>The index in the timeline of the snapshot with that sequence number, or of where it will be.</summary>
	private int IndexOf(long sequence)
	{
		var first = _timeline.Count > 0 ? _timeline[0].Sequence : _nextSequence;
		return (int)Math.Clamp(sequence - first, 0, _timeline.Count);
	}

	private void Keep(CompletedMeasurement completed)
	{
		_completed[completed.Name] = completed;
		while (_completed.Count > MaxKeptResults)
		{
			_completed.Remove(_completed.Values.MinBy(result => result.Stop)!.Name);
		}
	}

	private List<string> RunningNames()
	{
		return [.. _running.Values.Select(measurement => measurement.Name)];
	}

	private void StartSampling()
	{
		if (_sampleInterval is not { } interval || _samplingStop is not null)
		{
			return;
		}

		_samplingStop = new CancellationTokenSource();
		_sampling = SampleUntilStoppedAsync(interval, _samplingStop.Token);
	}

	private void StopSampling()
	{
		if (_samplingStop is null)
		{
			return;
		}

		_samplingStop.Cancel();
		_samplingStop.Dispose();
		_samplingStop = null;
	}

	private async Task SampleUntilStoppedAsync(TimeSpan interval, CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(interval, _timeProvider);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				lock (_gate)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						return;
					}

					try
					{
						SampleLocked(_timeProvider.GetUtcNow());
					}
					catch (Exception ex) when (ex is not OutOfMemoryException)
					{
						// The next tick tries again; the measurements must keep running
						LogSampleFailed(_logger, ex);
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			// The last measurement stopped
		}
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Measurement {Name} started, {Running} running")]
	private static partial void LogStarted(ILogger logger, string name, int running);

	[LoggerMessage(Level = LogLevel.Information, Message = "Measurement {Name} stopped after {Duration} with {Snapshots} snapshots")]
	private static partial void LogStopped(ILogger logger, string name, TimeSpan duration, int snapshots);

	[LoggerMessage(
		Level = LogLevel.Warning,
		Message = "The measurement memory limit of {Limit} is reached: new polls aren't recorded until measurements are stopped")]
	private static partial void LogLimitReached(ILogger logger, string limit);

	[LoggerMessage(Level = LogLevel.Information, Message = "Below the measurement memory limit again: recording polls again")]
	private static partial void LogRecordingAgain(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Recording a poll for the running measurements failed")]
	private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
