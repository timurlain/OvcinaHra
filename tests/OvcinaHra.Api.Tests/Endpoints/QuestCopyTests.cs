using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class QuestCopyTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync(string name = "Game")
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<LocationDetailDto> CreateLocationAsync(string name = "Hora")
    {
        var response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto(name, LocationKind.Wilderness, 49.5m, 17.1m));
        return (await response.Content.ReadFromJsonAsync<LocationDetailDto>())!;
    }

    private async Task AssignLocationToGameAsync(int gameId, int locationId)
    {
        await Client.PostAsJsonAsync("/api/locations/by-game", new GameLocationDto(gameId, locationId));
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name = "Meč")
    {
        var response = await Client.PostAsJsonAsync("/api/items", new CreateItemDto(name, ItemType.Weapon));
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private async Task<GameItemDto> AssignItemToGameAsync(int gameId, int itemId)
    {
        var response = await Client.PostAsJsonAsync("/api/items/game-item", new CreateGameItemDto(gameId, itemId));
        return (await response.Content.ReadFromJsonAsync<GameItemDto>())!;
    }

    private async Task<MonsterDetailDto> CreateMonsterAsync(string name = "Kostlivec")
    {
        var response = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto(name, MonsterCategory.Tier3, MonsterType.Undead, 5, 3, 10));
        return (await response.Content.ReadFromJsonAsync<MonsterDetailDto>())!;
    }

    private async Task<TagDto> CreateTagAsync(string name = "Undead")
    {
        var response = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto(name, TagKind.Quest));
        return (await response.Content.ReadFromJsonAsync<TagDto>())!;
    }

    private async Task<QuestDetailDto> CreateQuestAsync(string name, int gameId, QuestType type = QuestType.General)
    {
        var response = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto(name, type, gameId));
        return (await response.Content.ReadFromJsonAsync<QuestDetailDto>())!;
    }

    [Fact]
    public async Task CopyQuest_CopiesBasicFields()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");

        var sourceQuest = await CreateQuestAsync("My Quest", game1.Id, QuestType.Timed);

        var updateDto = new UpdateQuestDto("My Quest", QuestType.Timed, "Some description", null, null, null, null, null, null, null);
        await Client.PutAsJsonAsync($"/api/quests/{sourceQuest.Id}", updateDto);

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);
        Assert.Equal("My Quest", result.Quest.Name);
        Assert.Equal(QuestType.Timed, result.Quest.QuestType);
        Assert.Equal(game2.Id, result.Quest.GameId);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.Equal("My Quest", copy.Name);
        Assert.Equal(QuestType.Timed, copy.QuestType);
        Assert.Equal("Some description", copy.Description);
    }

    [Fact]
    public async Task CopyQuest_CopiesTags()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var tag = await CreateTagAsync("Undead");
        var sourceQuest = await CreateQuestAsync("Quest with tag", game1.Id);
        await Client.PostAsync($"/api/quests/{sourceQuest.Id}/tags/{tag.Id}", null);

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.Contains(copy.Tags, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task CopyQuest_CopiesEncounters()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var monster = await CreateMonsterAsync();
        var sourceQuest = await CreateQuestAsync("Quest with encounter", game1.Id);
        await Client.PostAsJsonAsync($"/api/quests/{sourceQuest.Id}/encounters", new AddQuestEncounterDto(monster.Id, 2));

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.Contains(copy.Encounters, e => e.MonsterId == monster.Id && e.Quantity == 2);
    }

    [Fact]
    public async Task CopyQuest_CopiesRewardWhenItemInTargetGame()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var item = await CreateItemAsync();
        await AssignItemToGameAsync(game1.Id, item.Id);
        await AssignItemToGameAsync(game2.Id, item.Id);
        var sourceQuest = await CreateQuestAsync("Quest with reward", game1.Id);
        await Client.PostAsJsonAsync($"/api/quests/{sourceQuest.Id}/rewards", new AddQuestRewardDto(item.Id, 1));

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.Contains(copy.Rewards, r => r.ItemId == item.Id && r.Quantity == 1);
    }

    [Fact]
    public async Task CopyQuest_SkipsRewardWhenItemNotInTargetGame()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var item = await CreateItemAsync();
        await AssignItemToGameAsync(game1.Id, item.Id);
        // item NOT assigned to game2
        var sourceQuest = await CreateQuestAsync("Quest with reward", game1.Id);
        await Client.PostAsJsonAsync($"/api/quests/{sourceQuest.Id}/rewards", new AddQuestRewardDto(item.Id, 1));

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Warnings);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.DoesNotContain(copy.Rewards, r => r.ItemId == item.Id);
    }

    [Fact]
    public async Task CopyQuest_CopiesLocationWhenInTargetGame()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var location = await CreateLocationAsync();
        await AssignLocationToGameAsync(game1.Id, location.Id);
        await AssignLocationToGameAsync(game2.Id, location.Id);
        var sourceQuest = await CreateQuestAsync("Quest with location", game1.Id);
        await Client.PostAsJsonAsync($"/api/quests/{sourceQuest.Id}/locations", new AddQuestLocationDto(location.Id));

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.Contains(copy.Locations, l => l.LocationId == location.Id);
    }

    [Fact]
    public async Task CopyQuest_SkipsLocationWhenNotInTargetGame()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var location = await CreateLocationAsync();
        await AssignLocationToGameAsync(game1.Id, location.Id);
        // location NOT assigned to game2
        var sourceQuest = await CreateQuestAsync("Quest with location", game1.Id);
        await Client.PostAsJsonAsync($"/api/quests/{sourceQuest.Id}/locations", new AddQuestLocationDto(location.Id));

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Warnings);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.DoesNotContain(copy.Locations, l => l.LocationId == location.Id);
    }

    [Fact]
    public async Task CopyQuest_ResetsChainFields()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");

        var parentQuest = await CreateQuestAsync("Parent Quest", game1.Id);
        var sourceQuest = await CreateQuestAsync("Chained Quest", game1.Id);

        var updateDto = new UpdateQuestDto("Chained Quest", QuestType.General, null, null, null, null, null, null, 2, parentQuest.Id);
        await Client.PutAsJsonAsync($"/api/quests/{sourceQuest.Id}", updateDto);

        var response = await Client.PostAsync($"/api/quests/{sourceQuest.Id}/copy-to-game/{game2.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(result);

        var copy = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{result.Quest.Id}");
        Assert.NotNull(copy);
        Assert.Null(copy.ChainOrder);
        Assert.Null(copy.ParentQuestId);
    }
}
