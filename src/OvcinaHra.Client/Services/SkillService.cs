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

    public SkillService(ApiClient api)
    {
        _api = api;
    }

    // ---- /api/skills ----

    public async Task<IReadOnlyList<SkillDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _api.GetListAsync<SkillDto>("/api/skills", ct);
        return list;
    }

    public Task<SkillDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => _api.GetAsync<SkillDto>($"/api/skills/{id}", ct);

    public async Task<SkillDto> CreateAsync(CreateSkillRequest req, CancellationToken ct = default)
    {
        var created = await _api.PostAsync<CreateSkillRequest, SkillDto>("/api/skills", req, ct);
        return created
            ?? throw new InvalidOperationException("Server returned empty body for created skill.");
    }

    public Task UpdateAsync(int id, UpdateSkillRequest req, CancellationToken ct = default)
        => _api.PutAsync($"/api/skills/{id}", req, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => _api.DeleteRequiredAsync($"/api/skills/{id}", ct);

    // ---- /api/games/{gameId}/skills ----

    public async Task<IReadOnlyList<GameSkillDto>> GetGameSkillsAsync(int gameId, CancellationToken ct = default)
    {
        var list = await _api.GetListAsync<GameSkillDto>($"/api/games/{gameId}/skills", ct);
        return list;
    }

    public Task<GameSkillDto?> GetGameSkillAsync(int gameId, int gameSkillId, CancellationToken ct = default)
        => _api.GetAsync<GameSkillDto>($"/api/games/{gameId}/skills/{gameSkillId}", ct);

    public async Task<GameSkillDto> CreateGameSkillAsync(int gameId, CreateGameSkillRequest req, CancellationToken ct = default)
    {
        var created = await _api.PostAsync<CreateGameSkillRequest, GameSkillDto>($"/api/games/{gameId}/skills", req, ct);
        return created
            ?? throw new InvalidOperationException("Server returned empty body for created game skill.");
    }

    public Task UpdateGameSkillAsync(int gameId, int gameSkillId, UpdateGameSkillRequest req, CancellationToken ct = default)
        => _api.PutAsync($"/api/games/{gameId}/skills/{gameSkillId}", req, ct);

    public Task RemoveGameSkillAsync(int gameId, int gameSkillId, CancellationToken ct = default)
        => _api.DeleteRequiredAsync($"/api/games/{gameId}/skills/{gameSkillId}", ct);
}
