using System.Net.Http.Json;

namespace OvcinaHra.Api.Services;

public record RegistraceUserInfo(bool Exists, string? DisplayName, List<string>? Roles);

public class RegistraceClient
{
    private readonly HttpClient _http;

    public RegistraceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<RegistraceUserInfo> CheckUserAsync(string email, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/users/by-email?email={Uri.EscapeDataString(email)}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegistraceUserInfo>(ct)
            ?? new RegistraceUserInfo(false, null, null);
    }
}
