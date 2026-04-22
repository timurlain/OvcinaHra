using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Services;

/// <summary>
/// API client wrapper for /api/personal-quests endpoints (catalog CRUD, per-game links,
/// and reward links). Mirrors the <see cref="SkillService"/> shape — injected
/// <see cref="ApiClient"/>, scoped lifetime.
/// </summary>
public class PersonalQuestService
{
    private readonly ApiClient _api;

    public PersonalQuestService(ApiClient api)
    {
        _api = api;
    }

    // ---- Catalog CRUD ----

    public Task<List<PersonalQuestListDto>> GetAllAsync()
        => _api.GetListAsync<PersonalQuestListDto>("/api/personal-quests");

    public Task<PersonalQuestDetailDto?> GetByIdAsync(int id)
        => _api.GetAsync<PersonalQuestDetailDto>($"/api/personal-quests/{id}");

    public Task<PersonalQuestDetailDto?> CreateAsync(CreatePersonalQuestDto dto)
        => _api.PostAsync<CreatePersonalQuestDto, PersonalQuestDetailDto>("/api/personal-quests", dto);

    public Task UpdateAsync(int id, UpdatePersonalQuestDto dto)
        => _api.PutAsync($"/api/personal-quests/{id}", dto);

    public Task<bool> DeleteAsync(int id)
        => _api.DeleteAsync($"/api/personal-quests/{id}");

    // ---- Per-game link ----

    public Task<List<GamePersonalQuestListDto>> GetByGameAsync(int gameId)
        => _api.GetListAsync<GamePersonalQuestListDto>($"/api/personal-quests/by-game/{gameId}");

    public Task<GamePersonalQuestDto?> CreateGameLinkAsync(int gameId, int pqId, int? xpCost = null, int? perKingdomLimit = null)
        => _api.PostAsync<CreateGamePersonalQuestDto, GamePersonalQuestDto>(
            "/api/personal-quests/game-link",
            new CreateGamePersonalQuestDto(gameId, pqId, xpCost, perKingdomLimit));

    public Task UpdateGameLinkAsync(int gameId, int pqId, int? xpCost, int? perKingdomLimit)
        => _api.PutAsync(
            $"/api/personal-quests/game-link/{gameId}/{pqId}",
            new UpdateGamePersonalQuestDto(xpCost, perKingdomLimit));

    public Task<bool> DeleteGameLinkAsync(int gameId, int pqId)
        => _api.DeleteAsync($"/api/personal-quests/game-link/{gameId}/{pqId}");

    // ---- Reward links ----

    public Task AddSkillRewardAsync(int pqId, int skillId)
        => _api.PostAsync<AddSkillRewardDto>(
            $"/api/personal-quests/{pqId}/skill-rewards",
            new AddSkillRewardDto(skillId));

    public Task<bool> RemoveSkillRewardAsync(int pqId, int skillId)
        => _api.DeleteAsync($"/api/personal-quests/{pqId}/skill-rewards/{skillId}");

    public Task AddItemRewardAsync(int pqId, int itemId, int quantity)
        => _api.PostAsync<AddItemRewardDto>(
            $"/api/personal-quests/{pqId}/item-rewards",
            new AddItemRewardDto(itemId, quantity));

    public Task<bool> RemoveItemRewardAsync(int pqId, int itemId)
        => _api.DeleteAsync($"/api/personal-quests/{pqId}/item-rewards/{itemId}");
}
