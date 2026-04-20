namespace OvcinaHra.Client.Auth;

/// <summary>
/// Lightweight structured logging helper for the client auth pipeline.
///
/// Emits a one-line event to the browser console so we can reconstruct why a
/// user was forced back to /login in production. Kept in-process only —
/// Console.WriteLine in Blazor WASM routes to the DevTools console.
///
/// Format: <c>[auth HH:mm:ss.fff] {event} key1=v1 key2="quoted value"</c>
///
/// Values that contain whitespace, quotes, <c>=</c>, or control characters are
/// JSON-escaped and quoted so each <c>key=value</c> pair stays parseable even
/// when a value includes spaces or newlines.
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
            fields.Select(f => $"{f.key}={FormatValue(f.value)}"));
        Console.WriteLine($"[auth {ts}] {ev} {body}");
    }

    private static string FormatValue(object? v)
    {
        if (v is null) return "null";
        var s = v.ToString() ?? "null";
        if (s.Length == 0) return "\"\"";

        var needsQuote = false;
        foreach (var ch in s)
        {
            if (ch <= ' ' || ch == '"' || ch == '\\' || ch == '=')
            {
                needsQuote = true;
                break;
            }
        }
        if (!needsQuote) return s;

        var sb = new System.Text.StringBuilder(s.Length + 4);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
