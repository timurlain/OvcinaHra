using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class TreasureQuestEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<LocationDetailDto> CreateLocationAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        return (await response.Content.ReadFromJsonAsync<LocationDetailDto>())!;
    }

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var game = await CreateGameAsync();

        var treasureQuests = await Client.GetFromJsonAsync<List<TreasureQuestListDto>>($"/api/treasure-quests/by-game/{game.Id}");

        Assert.NotNull(treasureQuests);
        Assert.Empty(treasureQuests);
    }

    [Fact]
    public async Task Create_ValidTreasureQuest_ReturnsCreated()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();

        var dto = new CreateTreasureQuestDto("Ztracený poklad", TreasureQuestDifficulty.Early, game.Id, LocationId: location.Id);

        var response = await Client.PostAsJsonAsync("/api/treasure-quests", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TreasureQuestListDto>();
        Assert.NotNull(created);
        Assert.Equal("Ztracený poklad", created.Title);
        Assert.Equal(TreasureQuestDifficulty.Early, created.Difficulty);
        Assert.Equal(game.Id, created.GameId);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task Create_WithoutLocationOrStash_ReturnsValidationProblem()
    {
        var game = await CreateGameAsync();

        var dto = new CreateTreasureQuestDto("Špatný poklad", TreasureQuestDifficulty.Start, game.Id);

        var response = await Client.PostAsJsonAsync("/api/treasure-quests", dto);

        Assert.False(response.IsSuccessStatusCode, "Should reject when neither LocationId nor SecretStashId is set");
    }

    [Fact]
    public async Task GetById_ReturnsTreasureQuestWithItems()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();

        var createResponse = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Ztracený poklad", TreasureQuestDifficulty.Early, game.Id, LocationId: location.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var treasureQuest = await Client.GetFromJsonAsync<TreasureQuestDetailDto>($"/api/treasure-quests/{created!.Id}");

        Assert.NotNull(treasureQuest);
        Assert.Equal("Ztracený poklad", treasureQuest.Title);
        Assert.Equal(TreasureQuestDifficulty.Early, treasureQuest.Difficulty);
        Assert.NotNull(treasureQuest.Items);
        Assert.Empty(treasureQuest.Items);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/treasure-quests/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNoContent()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();

        var createResponse = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Starý název", TreasureQuestDifficulty.Start, game.Id, LocationId: location.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var updateDto = new UpdateTreasureQuestDto("Nový název", "Hledej u staré studny", TreasureQuestDifficulty.Midgame, location.Id, null);
        var response = await Client.PutAsJsonAsync($"/api/treasure-quests/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<TreasureQuestDetailDto>($"/api/treasure-quests/{created.Id}");
        Assert.Equal("Nový název", updated!.Title);
        Assert.Equal(TreasureQuestDifficulty.Midgame, updated.Difficulty);
        Assert.Equal("Hledej u staré studny", updated.Clue);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();

        var createResponse = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Smazaný poklad", TreasureQuestDifficulty.Lategame, game.Id, LocationId: location.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var response = await Client.DeleteAsync($"/api/treasure-quests/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await Client.GetAsync($"/api/treasure-quests/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task AddItem_ReturnsCreated()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();

        var tqResponse = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Ztracený poklad", TreasureQuestDifficulty.Early, game.Id, LocationId: location.Id));
        var tq = await tqResponse.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var response = await Client.PostAsJsonAsync($"/api/treasure-quests/{tq!.Id}/items",
            new AddTreasureItemDto(item!.Id, 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<TreasureQuestDetailDto>($"/api/treasure-quests/{tq.Id}");
        Assert.Contains(updated!.Items, i => i.ItemId == item.Id && i.Count == 3);
    }

    [Fact]
    public async Task RemoveItem_ReturnsNoContent()
    {
        var game = await CreateGameAsync();
        var location = await CreateLocationAsync();

        var tqResponse = await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Ztracený poklad", TreasureQuestDifficulty.Early, game.Id, LocationId: location.Id));
        var tq = await tqResponse.Content.ReadFromJsonAsync<TreasureQuestListDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        await Client.PostAsJsonAsync($"/api/treasure-quests/{tq!.Id}/items", new AddTreasureItemDto(item!.Id));

        // Get the TreasureItem Id from the detail
        var detail = await Client.GetFromJsonAsync<TreasureQuestDetailDto>($"/api/treasure-quests/{tq.Id}");
        var treasureItemId = detail!.Items.First(i => i.ItemId == item.Id).Id;

        var response = await Client.DeleteAsync($"/api/treasure-quests/{tq.Id}/items/{treasureItemId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<TreasureQuestDetailDto>($"/api/treasure-quests/{tq.Id}");
        Assert.DoesNotContain(updated!.Items, i => i.ItemId == item.Id);
    }
}
