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

    public async Task<T?> GetAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
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

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsLite>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
                return (false, problem.Detail);
            if (!string.IsNullOrWhiteSpace(problem?.Title))
                return (false, problem.Title);
        }
        catch (JsonException)
        {
            // Non-JSON body — fall through to the raw-text path below.
        }

        var raw = await response.Content.ReadAsStringAsync();
        return (false, string.IsNullOrWhiteSpace(raw) ? null : raw);
    }

    private sealed record ProblemDetailsLite(string? Title, string? Detail, int? Status);
}
