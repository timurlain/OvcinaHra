using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Covers DELETE /api/items/game-item/{gameId}/{itemId} per issue #99.
// The endpoint must refuse to delete a GameItem while a per-game entity
// still references the same (GameId, ItemId) — otherwise TreasureItem /
// MonsterLoot rows survive as silent ghosts. Tests span the three blocking
// sources (assigned treasure, pool treasure, monster loot) plus a regression
// happy-path that confirms an unreferenced GameItem still deletes cleanly.
public class ItemGameLinkTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    // ----- helpers -----

    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra #99", 1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<LocationDetailDto> CreateLocationAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Skála", LocationKind.Wilderness, 49.5m, 17.1m));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationDetailDto>())!;
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, ItemType.Potion));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private async Task AssignItemToGameAsync(int gameId, int itemId)
    {
        var response = await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(gameId, itemId));
        response.EnsureSuccessStatusCode();
    }

    private async Task<int> CreateMonsterAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto(name, MonsterCategory.Tier1, MonsterType.Beast, 5, 5, 10));
        response.EnsureSuccessStatusCode();
        var monster = await response.Content.ReadFromJsonAsync<MonsterDetailDto>();
        return monster!.Id;
    }

    // ----- 204 regression -----

    [Fact]
    public async Task DeleteGameItem_NoReferences_ReturnsNoContent()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Neškodný meč");
        await AssignItemToGameAsync(game.Id, item.Id);

        var response = await Client.DeleteAsync($"/api/items/game-item/{game.Id}/{item.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ----- TreasureItem: assigned -----

    [Fact]
    public async Task DeleteGameItem_WhenReferencedByAssignedTreasureItem_ReturnsBadRequestWithTitle()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();
        var item = await CreateItemAsync("Prsten síly");
        await AssignItemToGameAsync(game.Id, item.Id);

        // Build a pool row, then attach it to a quest — this flips
        // TreasureQuestId from null to the quest id, turning the item into
        // an assigned reference rather than a pool one.
        var poolResponse = await Client.PostAsJsonAsync("/api/treasure-planning/pool",
            new CreateTreasurePoolItemDto(item.Id, game.Id, 1));
        poolResponse.EnsureSuccessStatusCode();
        var pool = await poolResponse.Content.ReadFromJsonAsync<TreasurePoolItemDto>();

        var assignResponse = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto(
                Title: "Drakův poklad",
                Difficulty: GameTimePhase.Lategame,
                GameId: game.Id,
                LocationId: location.Id,
                TreasureItemIds: new List<int> { pool!.Id }));
        assignResponse.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/items/game-item/{game.Id}/{item.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Drakův poklad", body);
        Assert.Contains("Položku nelze odebrat", body);

        // Row must still exist — confirm via the by-game item list. Direct
        // (GameId, ItemId) check, not dependent on class/level rules.
        var byGame = await Client.GetFromJsonAsync<List<ItemListDto>>(
            $"/api/items/by-game/{game.Id}");
        Assert.NotNull(byGame);
        Assert.Contains(byGame, i => i.Id == item.Id);
    }

    // ----- TreasureItem: pool only -----

    [Fact]
    public async Task DeleteGameItem_WhenReferencedByPoolTreasureItem_ReturnsBadRequest()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Lektvar magie");
        await AssignItemToGameAsync(game.Id, item.Id);

        var poolResponse = await Client.PostAsJsonAsync("/api/treasure-planning/pool",
            new CreateTreasurePoolItemDto(item.Id, game.Id, 2));
        poolResponse.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/items/game-item/{game.Id}/{item.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("zásobník", body, StringComparison.OrdinalIgnoreCase);
    }

    // ----- MonsterLoot -----

    [Fact]
    public async Task DeleteGameItem_WhenReferencedByMonsterLoot_ReturnsBadRequestWithMonsterName()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Dračí šupina");
        await AssignItemToGameAsync(game.Id, item.Id);
        var monsterId = await CreateMonsterAsync("Bahenní červ");

        var lootResponse = await Client.PostAsJsonAsync("/api/monsters/loot",
            new CreateMonsterLootDto(monsterId, item.Id, game.Id, 1));
        lootResponse.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/items/game-item/{game.Id}/{item.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bahenní červ", body);
        Assert.Contains("kořist", body, StringComparison.OrdinalIgnoreCase);
    }
}
