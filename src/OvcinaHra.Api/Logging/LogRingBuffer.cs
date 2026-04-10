using System.Collections.Concurrent;

namespace OvcinaHra.Api.Logging;

public record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message, string? Exception, int EventId);

public class LogRingBuffer(int maxSize = 2000)
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > maxSize)
            _entries.TryDequeue(out _);
    }

    public IReadOnlyList<LogEntry> Query(LogLevel? minLevel = null, int? last = null, string? search = null, string? category = null)
    {
        IEnumerable<LogEntry> result = _entries;
        if (minLevel.HasValue) result = result.Where(e => e.Level >= minLevel.Value);
        if (!string.IsNullOrEmpty(category)) result = result.Where(e => e.Category.Contains(category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(search)) result = result.Where(e => e.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (e.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        var list = result.ToList();
        if (last.HasValue && last.Value < list.Count) list = list.Skip(list.Count - last.Value).ToList();
        return list;
    }

    public int Count => _entries.Count;
    public void Clear() => _entries.Clear();
}
