namespace OvcinaHra.Client.Auth;

/// <summary>
/// Lightweight structured logging helper for the client auth pipeline.
///
/// Emits a one-line event to the browser console so we can reconstruct why a
/// user was forced back to /login in production. Kept in-process only —
/// Console.WriteLine in Blazor WASM routes to the DevTools console.
///
/// Format: <c>[auth HH:mm:ss.fff] {event} key1=v1 key2=v2 ...</c>
/// </summary>
internal static class AuthLog
{
    public static void Event(string ev, params (string key, object? value)[] fields)
    {
        var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        if (fields.Length == 0)
        {
            Console.WriteLine($"[auth {ts}] {ev}");
            return;
        }

        var body = string.Join(
            " ",
            fields.Select(f => $"{f.key}={(f.value is null ? "null" : f.value)}"));
        Console.WriteLine($"[auth {ts}] {ev} {body}");
    }
}
