using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Services;

/// <summary>
/// API client wrapper for /api/skills and /api/games/{gameId}/skills endpoints.
/// Follows the same pattern as <see cref="GameContextService"/> — injected
/// <see cref="ApiClient"/>, scoped lifetime.
/// </summary>
public class SkillService
{
    private readonly ApiClient _api;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SkillService(ApiClient api, HttpClient http)
    {
        _api = api;
        _http = http;
    }

    // ---- /api/skills ----

    public async Task<IReadOnlyList<SkillDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _api.GetListAsync<SkillDto>("/api/skills");
        return list;
    }

    public Task<SkillDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => _api.GetAsync<SkillDto>($"/api/skills/{id}");

    public async Task<SkillDto> CreateAsync(CreateSkillRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/skills", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<SkillDto>(JsonOptions, ct);
        return created
            ?? throw new InvalidOperationException("Server returned empty body for created skill.");
    }

    public Task UpdateAsync(int id, UpdateSkillRequest req, CancellationToken ct = default)
        => _api.PutAsync($"/api/skills/{id}", req);

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/skills/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    // ---- /api/games/{gameId}/skills ----

    public async Task<IReadOnlyList<GameSkillDto>> GetGameSkillsAsync(int gameId, CancellationToken ct = default)
    {
        var list = await _api.GetListAsync<GameSkillDto>($"/api/games/{gameId}/skills");
        return list;
    }

    public async Task UpsertGameSkillAsync(int gameId, int skillId, UpsertGameSkillRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/games/{gameId}/skills/{skillId}", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveGameSkillAsync(int gameId, int skillId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/games/{gameId}/skills/{skillId}", ct);
        response.EnsureSuccessStatusCode();
    }
}
