using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Covers DELETE /api/treasure-planning/pool/{id} per issue #102.
// The endpoint pre-dates the issue; these tests codify the contract so the
// UI-facing Odebrat action can trust the 204 / 400 / 404 split.
public class TreasurePlanningPoolEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Zásobník Test", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<LocationDetailDto> CreateLocationAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Skála", LocationKind.Wilderness, 49.5m, 17.1m));
        return (await response.Content.ReadFromJsonAsync<LocationDetailDto>())!;
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, ItemType.Potion));
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private async Task AssignItemToGameAsync(int gameId, int itemId)
    {
        var response = await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(gameId, itemId));
        response.EnsureSuccessStatusCode();
    }

    private async Task<TreasurePoolItemDto> AddPoolItemAsync(int gameId, int itemId, int count = 1)
    {
        var response = await Client.PostAsJsonAsync("/api/treasure-planning/pool",
            new CreateTreasurePoolItemDto(itemId, gameId, count));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TreasurePoolItemDto>())!;
    }

    [Fact]
    public async Task RemoveFromPool_UnassignedItem_ReturnsNoContentAndDeletes()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Lektvar zdraví");
        await AssignItemToGameAsync(game.Id, item.Id);
        var pool = await AddPoolItemAsync(game.Id, item.Id);

        var response = await Client.DeleteAsync($"/api/treasure-planning/pool/{pool.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var poolAfter = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{game.Id}");
        Assert.NotNull(poolAfter);
        Assert.DoesNotContain(poolAfter, p => p.Id == pool.Id);
    }

    [Fact]
    public async Task RemoveFromPool_AssignedItem_RejectsWith400AndLeavesRow()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();
        var item = await CreateItemAsync("Meč");
        await AssignItemToGameAsync(game.Id, item.Id);
        var pool = await AddPoolItemAsync(game.Id, item.Id);

        // Attach the pool row to a quest — the planning flow re-uses the
        // existing TreasureItem by setting its TreasureQuestId. After this the
        // row is no longer in the pool; attempting to delete it via the pool
        // endpoint must be rejected so the caller knows to detach it first.
        var assignResponse = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto(
                Title: "Přiřazený poklad",
                Difficulty: TreasureQuestDifficulty.Early,
                GameId: game.Id,
                LocationId: location.Id,
                TreasureItemIds: new List<int> { pool.Id }));
        assignResponse.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/treasure-planning/pool/{pool.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Row must still exist — on the owning quest, with Id preserved.
        var quest = await assignResponse.Content.ReadFromJsonAsync<TreasureQuestDetailDto>();
        Assert.NotNull(quest);
        var detail = await Client.GetFromJsonAsync<TreasureQuestDetailDto>(
            $"/api/treasure-quests/{quest!.Id}");
        Assert.NotNull(detail);
        Assert.Contains(detail!.Items, ti => ti.Id == pool.Id);
    }

    [Fact]
    public async Task RemoveFromPool_NonExistentRow_ReturnsNotFound()
    {
        var response = await Client.DeleteAsync("/api/treasure-planning/pool/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
