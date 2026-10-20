using System.Globalization;

namespace HwinfoMcp.Measurements;

/// <summary>
/// Parses and formats sizes in bytes like "512MB" or "2 GB". The units are binary: 1 KB is 1024 bytes, so "KB"
/// and "KiB" mean the same.
/// </summary>
public static class ByteSize
{
	// Longer units first, so "GB" isn't taken for "B"
	private static readonly (string Unit, long Factor)[] s_units =
	[
		("TiB", 1L << 40), ("TB", 1L << 40), ("T", 1L << 40),
		("GiB", 1L << 30), ("GB", 1L << 30), ("G", 1L << 30),
		("MiB", 1L << 20), ("MB", 1L << 20), ("M", 1L << 20),
		("KiB", 1L << 10), ("KB", 1L << 10), ("K", 1L << 10),
		("B", 1),
	];

	private static readonly string[] s_formatUnits = ["bytes", "KB", "MB", "GB", "TB"];

	/// <summary>
	/// Parses a size: a number with an optional unit, e.g. "1GB", "1.5 gb", "512MiB" or "1048576" (bytes).
	/// The size must be at least one byte.
	/// </summary>
	public static bool TryParse(string? text, out long bytes)
	{
		bytes = 0;
		var span = text.AsSpan().Trim();
		long factor = 1;
		foreach (var (unit, unitFactor) in s_units)
		{
			if (span.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
			{
				span = span[..^unit.Length].TrimEnd();
				factor = unitFactor;
				break;
			}
		}

		// TryParse takes "NaN" and "Infinity" whatever the styles say
		if (!double.TryParse(span, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
			|| !double.IsFinite(number))
		{
			return false;
		}

		var value = Math.Floor(number * factor);
		if (value < 1 || value >= long.MaxValue)
		{
			return false;
		}

		bytes = (long)value;
		return true;
	}

	/// <summary>Formats a size for humans, e.g. "1 GB", "1.5 GB", "512 MB" or "800 bytes".</summary>
	public static string Format(long bytes)
	{
		double value = bytes;
		var unit = 0;
		// Rounded as written, so just below 1 GB reads "1 GB" rather than "1024 MB"
		while (Math.Round(Math.Abs(value), 1) >= 1024 && unit < s_formatUnits.Length - 1)
		{
			value /= 1024;
			unit++;
		}

		if (unit == 0)
		{
			return bytes == 1 ? "1 byte" : string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes");
		}

		return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {s_formatUnits[unit]}");
	}
}
