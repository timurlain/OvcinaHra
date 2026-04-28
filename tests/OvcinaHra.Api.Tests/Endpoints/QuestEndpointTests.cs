using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Tests.Endpoints;

public class QuestEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var quests = await Client.GetFromJsonAsync<List<QuestListDto>>($"/api/quests/by-game/{game!.Id}");

        Assert.NotNull(quests);
        Assert.Empty(quests);
    }

    [Fact]
    public async Task Create_ValidQuest_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var dto = new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id);

        var response = await Client.PostAsJsonAsync("/api/quests", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<QuestDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Záchrana vesnice", created.Name);
        Assert.Equal(QuestType.General, created.QuestType);
        Assert.Equal(game.Id, created.GameId);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task GetById_ReturnsQuestWithCollections()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var quest = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created!.Id}");

        Assert.NotNull(quest);
        Assert.Equal("Záchrana vesnice", quest.Name);
        Assert.Equal(QuestType.General, quest.QuestType);
        Assert.NotNull(quest.Tags);
        Assert.NotNull(quest.Locations);
        Assert.NotNull(quest.Encounters);
        Assert.NotNull(quest.Rewards);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/quests/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_CatalogQuest_ReturnsRewardSummaryWithAllParts()
    {
        // Create two items — alphabetical order matters for the summary.
        var lektvarResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Lektvar", ItemType.Potion));
        var lektvar = (await lektvarResponse.Content.ReadFromJsonAsync<ItemDetailDto>())!;

        var mecResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon));
        var mec = (await mecResponse.Content.ReadFromJsonAsync<ItemDetailDto>())!;

        // Catalog quest (GameId = null) with XP, money, notes, and two items.
        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto(
                "Test Quest", QuestType.General, GameId: null,
                Description: "Pro organizátora", FullText: "Pro hráče",
                RewardXp: 50, RewardMoney: 20, RewardNotes: "tajemství"));
        var created = (await createResponse.Content.ReadFromJsonAsync<QuestListDto>())!;

        await Client.PostAsJsonAsync($"/api/quests/{created.Id}/rewards",
            new AddQuestRewardDto(lektvar.Id, Quantity: 5));
        await Client.PostAsJsonAsync($"/api/quests/{created.Id}/rewards",
            new AddQuestRewardDto(mec.Id, Quantity: 1));

        // Catalog endpoint should return a single combined string.
        var catalog = await Client.GetFromJsonAsync<List<QuestCatalogDto>>("/api/quests/all");
        Assert.NotNull(catalog);
        var quest = catalog.Single(q => q.Id == created.Id);

        Assert.Equal("Pro organizátora", quest.Description);
        Assert.Equal("Pro hráče", quest.FullText);
        Assert.Equal("50 XP · 20 gr · Lektvar × 5 · Meč · pozn.: tajemství", quest.RewardSummary);
    }

    [Fact]
    public async Task GetAll_CatalogQuestWithNoRewards_ReturnsNullSummary()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Bez odměn", QuestType.General, GameId: null));
        var created = (await createResponse.Content.ReadFromJsonAsync<QuestListDto>())!;

        var catalog = await Client.GetFromJsonAsync<List<QuestCatalogDto>>("/api/quests/all");
        Assert.NotNull(catalog);
        var quest = catalog.Single(q => q.Id == created.Id);

        Assert.Null(quest.RewardSummary);
    }

    [Fact]
    public async Task Update_ExistingQuest_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Starý název", QuestType.General, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var updateDto = new UpdateQuestDto("Nový název", QuestType.Timed, "Popis úkolu", null, null, 100, 50, "Odměna za splnění", null, null);
        var response = await Client.PutAsJsonAsync($"/api/quests/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created.Id}");
        Assert.Equal("Nový název", updated!.Name);
        Assert.Equal(QuestType.Timed, updated.QuestType);
        Assert.Equal("Popis úkolu", updated.Description);
        Assert.Equal(100, updated.RewardXp);
    }

    [Fact]
    public async Task Create_WithValidTimeSlot_PersistsProjection()
    {
        var game = await CreateGameAsync();
        var startTime = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var slot = await CreateTimeSlotAsync(game.Id, startTime, GameTimePhase.Midgame);
        var expectedName = TimeSlotDisplayExtensions.FormatTimeSlotDisplay(
            GameTimePhase.Midgame, 1247, startTime, 2);

        var response = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Časovaný úkol", QuestType.Timed, game.Id, TimeSlotId: slot.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<QuestListDto>();
        Assert.Equal(slot.Id, created!.TimeSlotId);
        Assert.Equal(expectedName, created.TimeSlotName);

        var detail = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created.Id}");
        Assert.Equal(slot.Id, detail!.TimeSlotId);
        Assert.Equal(expectedName, detail.TimeSlotName);
    }

    [Fact]
    public async Task Update_WithValidTimeSlot_PersistsProjection()
    {
        var game = await CreateGameAsync();
        var slot = await CreateTimeSlotAsync(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
            GameTimePhase.Early);
        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Starý název", QuestType.General, game.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var updateDto = new UpdateQuestDto(
            "Nový název", QuestType.Timed, "Popis úkolu", null, slot.Id, 100, 50, "Odměna za splnění", null, null);
        var response = await Client.PutAsJsonAsync($"/api/quests/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created.Id}");
        Assert.Equal(slot.Id, updated!.TimeSlotId);
        Assert.Contains("Rozvoj hry", updated.TimeSlotName);
    }

    [Fact]
    public async Task Create_WithCatalogTimeSlot_ReturnsBadRequestProblemDetails()
    {
        var game = await CreateGameAsync();
        var slot = await CreateTimeSlotAsync(game.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
            GameTimePhase.Start);

        var response = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Katalogový úkol", QuestType.General, TimeSlotId: slot.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Neplatný časový slot", problem!.Title);
        Assert.Contains("přiřazenému ke hře", problem.Detail);
    }

    [Fact]
    public async Task Create_WithCrossGameTimeSlot_ReturnsBadRequestProblemDetails()
    {
        var questGame = await CreateGameAsync(1);
        var slotGame = await CreateGameAsync(2);
        var slot = await CreateTimeSlotAsync(slotGame.Id, new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
            GameTimePhase.Start);

        var response = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Cizí slot", QuestType.Timed, questGame.Id, TimeSlotId: slot.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Neplatný časový slot", problem!.Title);
        Assert.Contains("jiné hře", problem.Detail);
    }

    [Fact]
    public async Task Delete_ExistingQuest_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Smazaný úkol", QuestType.Penance, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var response = await Client.DeleteAsync($"/api/quests/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await Client.GetAsync($"/api/quests/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task AddTag_ReturnsOk()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var tagResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Undead", TagKind.Quest));
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagDto>();

        var response = await Client.PostAsync($"/api/quests/{quest!.Id}/tags/{tag!.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.Contains(updated!.Tags, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task RemoveTag_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var tagResponse = await Client.PostAsJsonAsync("/api/tags", new CreateTagDto("Undead", TagKind.Quest));
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagDto>();

        await Client.PostAsync($"/api/quests/{quest!.Id}/tags/{tag!.Id}", null);

        var response = await Client.DeleteAsync($"/api/quests/{quest.Id}/tags/{tag.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.DoesNotContain(updated!.Tags, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task AddLocation_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var response = await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/locations",
            new AddQuestLocationDto(location!.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.Contains(updated!.Locations, l => l.LocationId == location.Id);
    }

    [Fact]
    public async Task RemoveLocation_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/locations", new AddQuestLocationDto(location!.Id));

        var response = await Client.DeleteAsync($"/api/quests/{quest.Id}/locations/{location.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.DoesNotContain(updated!.Locations, l => l.LocationId == location.Id);
    }

    [Fact]
    public async Task AddEncounter_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", MonsterCategory.Tier3, MonsterType.Undead, 5, 3, 10));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var response = await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/encounters",
            new AddQuestEncounterDto(monster!.Id, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.Contains(updated!.Encounters, e => e.MonsterId == monster.Id && e.Quantity == 2);
    }

    [Fact]
    public async Task RemoveEncounter_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", MonsterCategory.Tier3, MonsterType.Undead, 5, 3, 10));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/encounters", new AddQuestEncounterDto(monster!.Id));

        var response = await Client.DeleteAsync($"/api/quests/{quest.Id}/encounters/{monster.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.DoesNotContain(updated!.Encounters, e => e.MonsterId == monster.Id);
    }

    [Fact]
    public async Task AddReward_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var response = await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/rewards",
            new AddQuestRewardDto(item!.Id, 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.Contains(updated!.Rewards, r => r.ItemId == item.Id && r.Quantity == 1);
    }

    [Fact]
    public async Task RemoveReward_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Záchrana vesnice", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/rewards", new AddQuestRewardDto(item!.Id));

        var response = await Client.DeleteAsync($"/api/quests/{quest.Id}/rewards/{item.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{quest.Id}");
        Assert.DoesNotContain(updated!.Rewards, r => r.ItemId == item.Id);
    }

    private async Task<GameDetailDto> CreateGameAsync(int edition = 1)
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto($"Test Hra {edition}", edition, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        gameResponse.EnsureSuccessStatusCode();
        return (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<GameTimeSlotDto> CreateTimeSlotAsync(int gameId, DateTime startTime, GameTimePhase stage)
    {
        var slotResponse = await Client.PostAsJsonAsync("/api/timeline/slots",
            new CreateGameTimeSlotDto(gameId, startTime, 2, InGameYear: 1247, Stage: stage));
        slotResponse.EnsureSuccessStatusCode();
        return (await slotResponse.Content.ReadFromJsonAsync<GameTimeSlotDto>())!;
    }
}
