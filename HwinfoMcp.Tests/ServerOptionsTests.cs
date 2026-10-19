using HwinfoMcp.Measurements;

namespace HwinfoMcp.Tests;

public sealed class ServerOptionsTests
{
	private const long Gigabyte = 1L << 30;

	[Fact]
	public void MemoryLimit_DefaultsTo1GB()
	{
		var options = Parse([]);

		Assert.Equal(Gigabyte, options.MeasurementMemoryLimit);
	}

	[Theory]
	[InlineData("--measurement-memory-limit", "2GB")]
	[InlineData("--MEASUREMENT-MEMORY-LIMIT", "2 gb")]
	[InlineData("--measurement-memory-limit=2GiB", null)]
	public void MemoryLimit_FromTheCommandLine(string first, string? second)
	{
		var options = Parse(second is null ? [first] : [first, second], out var unknown, out var invalid);

		Assert.Equal(2 * Gigabyte, options.MeasurementMemoryLimit);
		Assert.Empty(unknown);
		Assert.Empty(invalid);
	}

	[Fact]
	public void MemoryLimit_FromTheEnvironment()
	{
		var options = Parse([], environment: "512MB");

		Assert.Equal(512L << 20, options.MeasurementMemoryLimit);
	}

	[Fact]
	public void MemoryLimit_TheCommandLineWinsOverTheEnvironment()
	{
		var options = Parse([HwinfoServerOptions.MeasurementMemoryLimitSwitch, "3GB"], environment: "512MB");

		Assert.Equal(3 * Gigabyte, options.MeasurementMemoryLimit);
	}

	[Fact]
	public void MemoryLimit_Invalid_IsReportedAndIgnored()
	{
		var options = Parse([HwinfoServerOptions.MeasurementMemoryLimitSwitch, "lots"], out _, out var invalid, environment: "512MB");

		Assert.Equal(512L << 20, options.MeasurementMemoryLimit);
		var problem = Assert.Single(invalid);
		Assert.Contains("'lots'", problem, StringComparison.Ordinal);
		Assert.Contains(HwinfoServerOptions.MeasurementMemoryLimitSwitch, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void MemoryLimit_InvalidEnvironment_IsReportedAndIgnored()
	{
		var options = Parse([], out _, out var invalid, environment: "-1GB");

		Assert.Equal(Gigabyte, options.MeasurementMemoryLimit);
		Assert.Contains(HwinfoServerOptions.MeasurementMemoryLimitVariable, Assert.Single(invalid), StringComparison.Ordinal);
	}

	[Fact]
	public void MemoryLimit_WithoutValue_IsReported()
	{
		var options = Parse(["--user-labels", HwinfoServerOptions.MeasurementMemoryLimitSwitch], out var unknown, out var invalid);

		Assert.Equal(Gigabyte, options.MeasurementMemoryLimit);
		Assert.Empty(unknown);
		Assert.Contains("needs a size", Assert.Single(invalid), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1GB", 1L << 30)]
	[InlineData("1 gib", 1L << 30)]
	[InlineData("1.5G", 3L << 29)]
	[InlineData("512MB", 512L << 20)]
	[InlineData("64k", 64L << 10)]
	[InlineData("1TB", 1L << 40)]
	[InlineData("1048576", 1L << 20)]
	[InlineData(" 100 B ", 100)]
	public void ByteSize_ParsesNumbersWithBinaryUnits(string text, long expected)
	{
		Assert.True(ByteSize.TryParse(text, out var bytes));
		Assert.Equal(expected, bytes);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("GB")]
	[InlineData("lots")]
	[InlineData("-1GB")]
	[InlineData("0")]
	[InlineData("1XB")]
	[InlineData("1,5GB")]
	[InlineData("99999999TB")]
	public void ByteSize_RejectsWhatIsNoSize(string? text)
	{
		Assert.False(ByteSize.TryParse(text, out _));
	}

	[Theory]
	[InlineData(800, "800 bytes")]
	[InlineData(1L << 30, "1 GB")]
	[InlineData(3L << 29, "1.5 GB")]
	[InlineData(512L << 20, "512 MB")]
	public void ByteSize_FormatsForHumans(long bytes, string expected)
	{
		Assert.Equal(expected, ByteSize.Format(bytes));
	}

	private static HwinfoServerOptions Parse(string[] args, string? environment = null)
	{
		return Parse(args, out _, out _, environment);
	}

	private static HwinfoServerOptions Parse(
		string[] args,
		out IReadOnlyList<string> unknown,
		out IReadOnlyList<string> invalid,
		string? environment = null)
	{
		return HwinfoServerOptions.Parse(
			args,
			name => name == HwinfoServerOptions.MeasurementMemoryLimitVariable ? environment : null,
			out unknown,
			out invalid
		);
	}
}
