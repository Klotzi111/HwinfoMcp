using Microsoft.Extensions.Logging;

namespace HwinfoMcp.Tests.Fakes;

/// <summary>A logger that keeps the level of everything logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
	public List<LogLevel> Levels { get; } = [];

	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		Levels.Add(logLevel);
	}
}
