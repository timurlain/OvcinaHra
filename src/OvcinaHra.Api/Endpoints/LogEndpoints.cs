using OvcinaHra.Api.Logging;

namespace OvcinaHra.Api.Endpoints;

public static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/logs").WithTags("Logs");

        group.MapGet("/", (LogRingBuffer buffer, LogLevel? level, int? last, string? search, string? category) =>
        {
            var entries = buffer.Query(level, last ?? 200, search, category);
            return Results.Ok(new
            {
                count = entries.Count,
                total = buffer.Count,
                entries = entries.Select(e => new
                {
                    t = e.Timestamp.ToString("HH:mm:ss.fff"),
                    lvl = e.Level.ToString()[..3].ToUpperInvariant(),
                    cat = e.Category,
                    msg = e.Message,
                    err = e.Exception
                })
            });
        });

        group.MapGet("/text", (LogRingBuffer buffer, LogLevel? level, int? last, string? search, string? category) =>
        {
            var entries = buffer.Query(level, last ?? 200, search, category);
            var lines = entries.Select(e => e.Exception is not null
                ? $"[{e.Timestamp:HH:mm:ss.fff}] {e.Level.ToString()[..3]} {e.Category}: {e.Message}\n  {e.Exception}"
                : $"[{e.Timestamp:HH:mm:ss.fff}] {e.Level.ToString()[..3]} {e.Category}: {e.Message}");
            return Results.Content(string.Join("\n", lines), "text/plain");
        });

        group.MapPost("/client", (ClientLogEntry entry, LogRingBuffer buffer) =>
        {
            buffer.Add(new LogEntry(
                DateTime.UtcNow,
                entry.Level switch { "error" => LogLevel.Error, "warn" => LogLevel.Warning, _ => LogLevel.Information },
                "CLIENT",
                entry.Message,
                entry.Stack,
                0));
            return Results.Ok();
        });

        group.MapDelete("/", (LogRingBuffer buffer) =>
        {
            buffer.Clear();
            return Results.Ok(new { message = "Logs cleared" });
        });

        return group;
    }
}

public record ClientLogEntry(string Level, string Message, string? Stack);
