using System.Collections.Immutable;
using Hwinfo.SharedMemory;

namespace HwinfoMcp.Measurements;

/// <summary>The statistics of one reading over a measurement. NaN where the reading had no finite values.</summary>
/// <param name="Min">The lowest value.</param>
/// <param name="Max">The highest value.</param>
/// <param name="Median">The middle value, or the mean of the two middle values.</param>
/// <param name="Avg">The mean of the values.</param>
/// <param name="Samples">How many snapshots had a finite value of the reading.</param>
public readonly record struct ReadingStats(double Min, double Max, double Median, double Avg, int Samples)
{
	/// <summary>The statistics of a reading without any finite value.</summary>
	public static ReadingStats None { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN, 0);
}

/// <summary>The readings of a measurement and their statistics.</summary>
/// <param name="Readings">
/// Every reading of the snapshots in the order HWiNFO first listed them, with the names they had last.
/// </param>
/// <param name="Sensors">Every sensor group of the snapshots, with the names they had last.</param>
/// <param name="Stats">The statistics of each reading, in the order of <paramref name="Readings"/>.</param>
internal sealed record MeasuredReadings(
	ImmutableArray<SensorReading> Readings,
	ImmutableArray<Sensor> Sensors,
	ImmutableArray<ReadingStats> Stats);

/// <summary>Evaluates the snapshots of a measurement: min, max, median and average of every reading.</summary>
internal static class MeasurementStatistics
{
	/// <summary>
	/// The most values held at once to find the medians, 32 MB. The readings of longer measurements are evaluated
	/// a few at a time.
	/// </summary>
	public const int MaxBufferedValues = 4 * 1024 * 1024;

	/// <summary>Evaluates the snapshots of a measurement.</summary>
	/// <param name="snapshots">The snapshots, oldest first.</param>
	/// <param name="maxBufferedValues">The most values to hold at once to find the medians.</param>
	public static MeasuredReadings Evaluate(IReadOnlyList<StoredSnapshot> snapshots, int maxBufferedValues = MaxBufferedValues)
	{
		var (readings, sensors, indices) = Combine(snapshots);
		var count = readings.Count;

		// Min, max and sum in one pass over the snapshots
		var min = new double[count];
		var max = new double[count];
		var sum = new double[count];
		var samples = new int[count];
		Array.Fill(min, double.PositiveInfinity);
		Array.Fill(max, double.NegativeInfinity);
		SnapshotLayout? layout = null;
		var index = Array.Empty<int>();
		foreach (var snapshot in snapshots)
		{
			if (!ReferenceEquals(snapshot.Layout, layout))
			{
				layout = snapshot.Layout;
				index = indices[layout];
			}

			var values = snapshot.Values;
			for (var i = 0; i < values.Length; i++)
			{
				var value = values[i];
				if (!double.IsFinite(value))
				{
					continue;
				}

				var reading = index[i];
				min[reading] = Math.Min(min[reading], value);
				max[reading] = Math.Max(max[reading], value);
				sum[reading] += value;
				samples[reading]++;
			}
		}

		var medians = FindMedians(snapshots, indices, samples, maxBufferedValues);
		var stats = new ReadingStats[count];
		for (var reading = 0; reading < count; reading++)
		{
			stats[reading] = samples[reading] == 0
				? ReadingStats.None
				: new ReadingStats(min[reading], max[reading], medians[reading], sum[reading] / samples[reading], samples[reading]);
		}

		return new MeasuredReadings([.. readings], [.. sensors], [.. stats]);
	}

	/// <summary>
	/// The median of the values: the middle one, or the mean of the two middle ones. NaN if there are none.
	/// Reorders the values.
	/// </summary>
	public static double Median(Span<double> values)
	{
		if (values.IsEmpty)
		{
			return double.NaN;
		}

		var middle = (values.Length - 1) / 2;
		var lower = Select(values, middle);
		if (values.Length % 2 == 1)
		{
			return lower;
		}

		// Select leaves the values from the middle one on behind it
		var upper = double.PositiveInfinity;
		foreach (var value in values[(middle + 1)..])
		{
			upper = Math.Min(upper, value);
		}

		return (lower / 2) + (upper / 2);
	}

	/// <summary>
	/// Merges the readings of all layouts the snapshots have: a reading is identified by its sensor group and its
	/// ID, so it keeps its statistics when HWiNFO adds or removes other readings in between.
	/// </summary>
	/// <returns>
	/// The readings, the sensor groups, and for each layout the index into the readings of each of its readings.
	/// </returns>
	private static (List<SensorReading> Readings, List<Sensor> Sensors, Dictionary<SnapshotLayout, int[]> Indices) Combine(
		IReadOnlyList<StoredSnapshot> snapshots)
	{
		var readings = new List<SensorReading>();
		var readingIndex = new Dictionary<(uint SensorId, uint Instance, uint ReadingId), int>();
		var sensors = new List<Sensor>();
		var sensorIndex = new Dictionary<(uint Id, uint Instance), int>();
		var indices = new Dictionary<SnapshotLayout, int[]>(ReferenceEqualityComparer.Instance);
		foreach (var snapshot in snapshots)
		{
			var layout = snapshot.Layout;
			if (indices.ContainsKey(layout))
			{
				continue;
			}

			foreach (var sensor in layout.Sensors)
			{
				if (sensorIndex.TryGetValue((sensor.Id, sensor.Instance), out var known))
				{
					sensors[known] = sensor;
				}
				else
				{
					sensorIndex.Add((sensor.Id, sensor.Instance), sensors.Count);
					sensors.Add(sensor);
				}
			}

			var index = new int[layout.Readings.Length];
			var taken = new HashSet<int>();
			for (var i = 0; i < index.Length; i++)
			{
				var reading = layout.Readings[i];
				var key = (reading.Sensor.Id, reading.Sensor.Instance, reading.ReadingId);
				if (readingIndex.TryGetValue(key, out var known) && taken.Add(known))
				{
					// The latest names win
					readings[known] = reading;
				}
				else
				{
					// New, or listed twice in one layout: then the second one is a reading of its own
					known = readings.Count;
					readingIndex[key] = known;
					readings.Add(reading);
					taken.Add(known);
				}

				index[i] = known;
			}

			indices.Add(layout, index);
		}

		// The queries group readings by their sensor group, so each reading has to point to the group reported
		for (var i = 0; i < readings.Count; i++)
		{
			var reading = readings[i];
			if (!sensorIndex.TryGetValue((reading.Sensor.Id, reading.Sensor.Instance), out var known))
			{
				known = sensors.Count;
				sensorIndex.Add((reading.Sensor.Id, reading.Sensor.Instance), known);
				sensors.Add(reading.Sensor);
			}

			if (!ReferenceEquals(sensors[known], reading.Sensor))
			{
				readings[i] = reading with { Sensor = sensors[known] };
			}
		}

		return (readings, sensors, indices);
	}

	/// <summary>
	/// Finds the median of each reading. Collects the values of as many readings at a time as fit into
	/// <paramref name="maxBufferedValues"/>, so a long measurement isn't copied as a whole.
	/// </summary>
	private static double[] FindMedians(
		IReadOnlyList<StoredSnapshot> snapshots,
		Dictionary<SnapshotLayout, int[]> indices,
		int[] samples,
		int maxBufferedValues)
	{
		var count = samples.Length;
		var medians = new double[count];

		// For each layout: where each reading is in its values, -1 if it has none
		var positions = new Dictionary<SnapshotLayout, int[]>(ReferenceEqualityComparer.Instance);
		foreach (var (layout, index) in indices)
		{
			var position = new int[count];
			Array.Fill(position, -1);
			for (var i = 0; i < index.Length; i++)
			{
				position[index[i]] = i;
			}

			positions.Add(layout, position);
		}

		for (var first = 0; first < count;)
		{
			var end = first;
			long buffered = 0;
			while (end < count && (end == first || buffered + samples[end] <= maxBufferedValues))
			{
				buffered += samples[end];
				end++;
			}

			var buffers = new double[end - first][];
			var filled = new int[end - first];
			for (var reading = first; reading < end; reading++)
			{
				buffers[reading - first] = new double[samples[reading]];
			}

			SnapshotLayout? layout = null;
			var position = Array.Empty<int>();
			foreach (var snapshot in snapshots)
			{
				if (!ReferenceEquals(snapshot.Layout, layout))
				{
					layout = snapshot.Layout;
					position = positions[layout];
				}

				for (var reading = first; reading < end; reading++)
				{
					var at = position[reading];
					if (at < 0)
					{
						continue;
					}

					var value = snapshot.Values[at];
					if (double.IsFinite(value))
					{
						buffers[reading - first][filled[reading - first]++] = value;
					}
				}
			}

			for (var reading = first; reading < end; reading++)
			{
				medians[reading] = Median(buffers[reading - first]);
			}

			first = end;
		}

		return medians;
	}

	/// <summary>
	/// Moves the k-th smallest value to index k, the smaller ones before it and the larger ones behind it, and
	/// returns it (quickselect). The three-way partition copes with the long runs of equal values sensors report.
	/// </summary>
	private static double Select(Span<double> values, int k)
	{
		var left = 0;
		var right = values.Length - 1;
		while (left < right)
		{
			var pivot = MedianOfThree(values[left], values[left + ((right - left) / 2)], values[right]);
			var below = left;
			var current = left;
			var above = right;
			while (current <= above)
			{
				var value = values[current];
				if (value < pivot)
				{
					(values[below], values[current]) = (values[current], values[below]);
					below++;
					current++;
				}
				else if (value > pivot)
				{
					(values[above], values[current]) = (values[current], values[above]);
					above--;
				}
				else
				{
					current++;
				}
			}

			// [left, below) is below the pivot, [below, above] equal to it, (above, right] above it
			if (k < below)
			{
				right = below - 1;
			}
			else if (k > above)
			{
				left = above + 1;
			}
			else
			{
				return pivot;
			}
		}

		return values[k];
	}

	private static double MedianOfThree(double a, double b, double c)
	{
		if (a < b)
		{
			return b < c ? b : Math.Max(a, c);
		}

		return a < c ? a : Math.Max(b, c);
	}
}
