namespace OvcinaHra.Api.Logging;

public class RingBufferLoggerProvider(LogRingBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RingBufferLogger(buffer, categoryName);
    public void Dispose() { }
}

public class RingBufferLogger(LogRingBuffer buffer, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // Skip noisy framework categories but KEEP auth-related ones for debugging
        if (category.StartsWith("Microsoft.EntityFrameworkCore.Database.Command") ||
            category.StartsWith("Microsoft.Hosting.") ||
            category.StartsWith("System.Net.Http."))
            return;

        buffer.Add(new LogEntry(
            DateTime.UtcNow, logLevel,
            SimplifyCategory(category),
            formatter(state, null),
            exception?.ToString(),
            eventId.Id));
    }

    private static string SimplifyCategory(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }
}
