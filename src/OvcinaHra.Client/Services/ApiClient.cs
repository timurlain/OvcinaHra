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
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
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
}
