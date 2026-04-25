using System.Net.Http.Json;
using System.Text.Json;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Services;

/// <summary>
/// Issue #3 — fetches the list of available registrace games for the
/// "Propojit s registrací" picker. Server-to-server only: the API key
/// (<c>IntegrationApi:ApiKey</c>) is held in OvčinaHra config and sent
/// via the <c>X-Api-Key</c> header — the browser never sees it.
/// Mirrors the auth + base-URL pattern used by
/// <see cref="RegistraceImportService"/>.
/// </summary>
public class RegistraceGameService(HttpClient httpClient, IConfiguration configuration)
{
    private readonly string _baseUrl = configuration["IntegrationApi:BaseUrl"] ?? "https://registrace.ovcina.cz";
    private readonly string? _apiKey = configuration["IntegrationApi:ApiKey"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<RegistraceGameDto>> GetAvailableAsync(CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}/api/v1/games");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var games = await response.Content.ReadFromJsonAsync<List<RegistraceGameDto>>(JsonOptions, ct);
        return games ?? [];
    }
}
