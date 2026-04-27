using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OvcinaHra.Client.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<T>> GetListAsync<T>(string url)
    {
        return await _http.GetFromJsonAsync<List<T>>(url, JsonOptions) ?? [];
    }

    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data, JsonOptions);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0 || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return default;
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content))
            return default;
        return JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
    }

    public async Task PostAsync<TRequest>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data, JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostAsync(string url)
    {
        var response = await _http.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutAsync<TRequest>(string url, TRequest data)
    {
        var response = await _http.PutAsJsonAsync(url, data, JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    public async Task PatchAsync<TRequest>(string url, TRequest data)
    {
        using var content = JsonContent.Create(data, options: JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<T?> PostMultipartAsync<T>(string url, MultipartFormDataContent content)
    {
        var response = await _http.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task<bool> DeleteAsync(string url)
    {
        var response = await _http.DeleteAsync(url);
        return response.IsSuccessStatusCode;
    }

    // Variant of DeleteAsync that reads the server's ProblemDetails on
    // non-success so the caller can surface the server's Czech `detail`
    // verbatim in the UI instead of a generic fallback. On success returns
    // (true, null); on failure returns (false, problemDetailOrNull) where
    // problemDetailOrNull is the ProblemDetails.detail field or the raw body
    // if the server did not return an application/problem+json response.
    public async Task<(bool Ok, string? ProblemDetail)> DeleteWithProblemAsync(string url)
    {
        var response = await _http.DeleteAsync(url);
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    // Put + Post variants of DeleteWithProblemAsync (see #118 + #124). Same
    // read-body-once-then-parse shape so a 400 ProblemDetails surface round-
    // trips verbatim into the caller's error banner. PostWithProblemAsync
    // also hands back the deserialized response body on success so callers
    // don't need a second round-trip to read what the server just wrote.
    public async Task<(bool Ok, TResponse? Value, string? ProblemDetail)> PostWithProblemAsync<TRequest, TResponse>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data, JsonOptions);
        if (!response.IsSuccessStatusCode)
            return (false, default, await ReadProblemDetailAsync(response));

        if (response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent)
            return (true, default, null);

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content))
            return (true, default, null);

        return (true, JsonSerializer.Deserialize<TResponse>(content, JsonOptions), null);
    }

    public async Task<(bool Ok, string? ProblemDetail)> PutWithProblemAsync<TRequest>(string url, TRequest data)
    {
        var response = await _http.PutAsJsonAsync(url, data, JsonOptions);
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    // Issue #252 — PATCH variant of *WithProblemAsync. Used by the map's
    // drag-drop relocate (LocationCoordinatesPatchDto) so the server's
    // Czech ProblemDetails on out-of-range lat/lng round-trips into the
    // confirm popup banner. Same read-body-once shape as the others.
    public async Task<(bool Ok, string? ProblemDetail)> PatchWithProblemAsync<TRequest>(string url, TRequest data)
    {
        using var content = JsonContent.Create(data, options: JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
        var response = await _http.SendAsync(request);
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadProblemDetailAsync(response));
    }

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var problem = JsonSerializer.Deserialize<ProblemDetailsLite>(raw, JsonOptions);
            if (!string.IsNullOrWhiteSpace(problem?.Detail)) return problem.Detail;
            if (!string.IsNullOrWhiteSpace(problem?.Title)) return problem.Title;
        }
        catch (JsonException)
        {
            // Non-JSON body — fall through to the raw-text return below.
        }
        return raw;
    }

    private sealed record ProblemDetailsLite(string? Title, string? Detail, int? Status);
}
