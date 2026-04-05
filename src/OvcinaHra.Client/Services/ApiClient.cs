using System.Net;
using System.Net.Http.Json;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Typed HTTP client wrapper for all API calls.
/// Handles JSON serialization and common error patterns.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<T>> GetListAsync<T>(string url)
    {
        return await _http.GetFromJsonAsync<List<T>>(url) ?? [];
    }

    public async Task<T?> GetAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task PostAsync<TRequest>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostAsync(string url)
    {
        var response = await _http.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutAsync<TRequest>(string url, TRequest data)
    {
        var response = await _http.PutAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> DeleteAsync(string url)
    {
        var response = await _http.DeleteAsync(url);
        return response.IsSuccessStatusCode;
    }
}
