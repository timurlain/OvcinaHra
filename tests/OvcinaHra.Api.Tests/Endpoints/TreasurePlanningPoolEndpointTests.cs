using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Covers DELETE /api/treasure-planning/pool/{id} per issue #102.
// The endpoint pre-dates the issue; these tests codify the contract so the
// UI-facing Odebrat action can trust the 204 / 400 / 404 split.
public class TreasurePlanningPoolEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Zásobník Test", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<LocationDetailDto> CreateLocationAsync(string name = "Skála", int? parentLocationId = null)
    {
        var response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto(name, LocationKind.Wilderness, 49.5m, 17.1m, ParentLocationId: parentLocationId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationDetailDto>())!;
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name, ItemType itemType = ItemType.Potion)
    {
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, itemType));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private async Task AssignItemToGameAsync(int gameId, int itemId, int? stockCount = null, bool isFindable = false)
    {
        var response = await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(gameId, itemId, StockCount: stockCount, IsFindable: isFindable));
        response.EnsureSuccessStatusCode();
    }

    private async Task AssignLocationToGameAsync(int gameId, int locationId)
    {
        var response = await Client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(gameId, locationId));
        response.EnsureSuccessStatusCode();
    }

    private async Task<SecretStashDetailDto> CreateSecretStashAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SecretStashDetailDto>())!;
    }

    private async Task<GameSecretStashDto> AssignStashToGameAsync(int gameId, int stashId, int locationId)
    {
        var response = await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(gameId, stashId, locationId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameSecretStashDto>())!;
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
                Difficulty: GameTimePhase.Early,
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
    public async Task AssignTreasure_WithPartialPoolCount_SplitsStackAndStepperAdjustsCount()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();
        var item = await CreateItemAsync("Bronz");
        await AssignItemToGameAsync(game.Id, item.Id);
        var pool = await AddPoolItemAsync(game.Id, item.Id, count: 5);

        var assignResponse = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto(
                Title: "Bronz",
                Difficulty: GameTimePhase.Early,
                GameId: game.Id,
                LocationId: location.Id,
                PoolItems: [new PoolItemAssignDto(pool.Id, 1)]));
        assignResponse.EnsureSuccessStatusCode();

        var quest = (await assignResponse.Content.ReadFromJsonAsync<TreasureQuestDetailDto>())!;
        var assigned = Assert.Single(quest.Items);
        Assert.Equal(1, assigned.Count);

        var poolAfterAssign = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{game.Id}");
        var poolRow = Assert.Single(poolAfterAssign!, p => p.ItemId == item.Id);
        Assert.Equal(4, poolRow.Count);

        var plusResponse = await Client.PostAsJsonAsync(
            $"/api/treasure-planning/treasure-items/{assigned.Id}/adjust-count",
            new AdjustTreasureItemCountDto(3, "test-stepper"));
        plusResponse.EnsureSuccessStatusCode();
        var afterPlus = (await plusResponse.Content.ReadFromJsonAsync<TreasureItemDto>())!;
        Assert.Equal(4, afterPlus.Count);

        var poolAfterPlus = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{game.Id}");
        poolRow = Assert.Single(poolAfterPlus!, p => p.ItemId == item.Id);
        Assert.Equal(1, poolRow.Count);

        var minusResponse = await Client.PostAsJsonAsync(
            $"/api/treasure-planning/treasure-items/{assigned.Id}/adjust-count",
            new AdjustTreasureItemCountDto(-1, "test-stepper"));
        minusResponse.EnsureSuccessStatusCode();
        var afterMinus = (await minusResponse.Content.ReadFromJsonAsync<TreasureItemDto>())!;
        Assert.Equal(3, afterMinus.Count);

        var poolAfterMinus = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{game.Id}");
        poolRow = Assert.Single(poolAfterMinus!, p => p.ItemId == item.Id);
        Assert.Equal(2, poolRow.Count);
    }

    [Fact]
    public async Task AssignTreasure_WithCompositePoolItems_CreatesOneQuestAndSplitsRows()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();
        var silver = await CreateItemAsync("Stříbro");
        var bronze = await CreateItemAsync("Bronz");
        await AssignItemToGameAsync(game.Id, silver.Id);
        await AssignItemToGameAsync(game.Id, bronze.Id);
        var silverPool = await AddPoolItemAsync(game.Id, silver.Id, count: 5);
        var bronzePool = await AddPoolItemAsync(game.Id, bronze.Id, count: 3);

        var assignResponse = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto(
                Title: "Balíček: Stříbro × 2 + Bronz × 1",
                Difficulty: GameTimePhase.Early,
                GameId: game.Id,
                LocationId: location.Id,
                PoolItems:
                [
                    new PoolItemAssignDto(silverPool.Id, 2),
                    new PoolItemAssignDto(bronzePool.Id, 1)
                ]));
        assignResponse.EnsureSuccessStatusCode();

        var quest = (await assignResponse.Content.ReadFromJsonAsync<TreasureQuestDetailDto>())!;
        Assert.Equal(2, quest.Items.Count);
        Assert.Equal(location.Id, quest.LocationId);
        Assert.All(quest.Items, item => Assert.Equal(quest.Id, item.TreasureQuestId));
        Assert.Equal(2, Assert.Single(quest.Items, i => i.ItemId == silver.Id).Count);
        Assert.Equal(1, Assert.Single(quest.Items, i => i.ItemId == bronze.Id).Count);

        var poolAfterAssign = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{game.Id}");
        Assert.Equal(3, Assert.Single(poolAfterAssign!, p => p.ItemId == silver.Id).Count);
        Assert.Equal(2, Assert.Single(poolAfterAssign!, p => p.ItemId == bronze.Id).Count);
    }

    [Fact]
    public async Task MoneyItems_CannotBeAddedToPoolOrAvailableItems()
    {
        var game = await CreateGameAsync();
        var bronze = await CreateItemAsync("Bronz", ItemType.Money);
        await AssignItemToGameAsync(game.Id, bronze.Id, stockCount: 50, isFindable: true);

        var addResponse = await Client.PostAsJsonAsync("/api/treasure-planning/pool",
            new CreateTreasurePoolItemDto(bronze.Id, game.Id, 10));
        Assert.Equal(HttpStatusCode.BadRequest, addResponse.StatusCode);

        var available = await Client.GetFromJsonAsync<List<AvailablePoolItemDto>>(
            $"/api/treasure-planning/available-items/{game.Id}");
        Assert.NotNull(available);
        Assert.DoesNotContain(available!, i => i.ItemId == bronze.Id);

        var pool = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{game.Id}");
        Assert.NotNull(pool);
        Assert.DoesNotContain(pool!, i => i.ItemId == bronze.Id);
    }

    [Fact]
    public async Task AssignTreasure_ToLocationWithStashes_DefaultsToLowestCountStashThenAlphabeticalTie()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();
        await AssignLocationToGameAsync(game.Id, location.Id);
        var stashA = await CreateSecretStashAsync("Skrýš A");
        var stashB = await CreateSecretStashAsync("Skrýš B");
        var stashC = await CreateSecretStashAsync("Skrýš C");
        await AssignStashToGameAsync(game.Id, stashA.Id, location.Id);
        await AssignStashToGameAsync(game.Id, stashB.Id, location.Id);
        await AssignStashToGameAsync(game.Id, stashC.Id, location.Id);
        var filler = await CreateItemAsync("Výplň");
        var bronze = await CreateItemAsync("Bronz");
        await AssignItemToGameAsync(game.Id, bronze.Id);
        var pool = await AddPoolItemAsync(game.Id, bronze.Id, count: 2);

        var seedA = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto("A má pět", GameTimePhase.Early, game.Id,
                SecretStashId: stashA.Id,
                UnlimitedItems: [new UnlimitedItemAssignDto(filler.Id, 5)]));
        seedA.EnsureSuccessStatusCode();
        var seedB = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto("B má čtyři", GameTimePhase.Early, game.Id,
                SecretStashId: stashB.Id,
                UnlimitedItems: [new UnlimitedItemAssignDto(filler.Id, 4)]));
        seedB.EnsureSuccessStatusCode();
        var seedC = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto("C má pět", GameTimePhase.Early, game.Id,
                SecretStashId: stashC.Id,
                UnlimitedItems: [new UnlimitedItemAssignDto(filler.Id, 5)]));
        seedC.EnsureSuccessStatusCode();

        var firstAssign = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto("První bronz", GameTimePhase.Early, game.Id,
                LocationId: location.Id,
                PoolItems: [new PoolItemAssignDto(pool.Id, 1)]));
        firstAssign.EnsureSuccessStatusCode();
        var firstQuest = (await firstAssign.Content.ReadFromJsonAsync<TreasureQuestDetailDto>())!;
        Assert.Null(firstQuest.LocationId);
        Assert.Equal(stashB.Id, firstQuest.SecretStashId);

        var secondAssign = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto("Druhý bronz", GameTimePhase.Early, game.Id,
                LocationId: location.Id,
                PoolItems: [new PoolItemAssignDto(pool.Id, 1)]));
        secondAssign.EnsureSuccessStatusCode();
        var secondQuest = (await secondAssign.Content.ReadFromJsonAsync<TreasureQuestDetailDto>())!;
        Assert.Equal(stashA.Id, secondQuest.SecretStashId);
    }

    [Fact]
    public async Task GetLocationCards_ExcludesChildLocationsAndRollsTheirDirectTreasuresIntoParent()
    {
        var game = await CreateGameAsync();
        var parent = await CreateLocationAsync("Starý brod");
        var child = await CreateLocationAsync("Starý brod - břeh", parent.Id);
        await AssignLocationToGameAsync(game.Id, parent.Id);
        await AssignLocationToGameAsync(game.Id, child.Id);
        var gros = await CreateItemAsync("Groše", ItemType.Money);

        var assignResponse = await Client.PostAsJsonAsync("/api/treasure-planning/assign",
            new AssignTreasureDto("Dětský poklad", GameTimePhase.Early, game.Id,
                LocationId: child.Id,
                UnlimitedItems: [new UnlimitedItemAssignDto(gros.Id, 4)]));
        assignResponse.EnsureSuccessStatusCode();

        var cards = await Client.GetFromJsonAsync<List<TreasurePlanningLocationDto>>(
            $"/api/treasure-planning/locations/{game.Id}");
        Assert.NotNull(cards);
        var card = Assert.Single(cards!);
        Assert.Equal(parent.Id, card.LocationId);
        Assert.Equal(4, card.TotalItems);
        Assert.DoesNotContain(cards!, c => c.LocationId == child.Id);
    }

    [Fact]
    public async Task RemoveFromPool_NonExistentRow_ReturnsNotFound()
    {
        var response = await Client.DeleteAsync("/api/treasure-planning/pool/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
