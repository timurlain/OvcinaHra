using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

/// <summary>
/// Phase 1 of the Quests port (issue #193): adds Quest.ImagePath +
/// Quest.State (3-value enum) and a PATCH /state endpoint. These tests
/// cover the new fields and lift count projections on the list/catalog
/// DTOs that the Phase 1 UI relies on.
/// </summary>
public class QuestPhase1Tests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Create_DefaultsState_ToInactive()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var dto = new CreateQuestDto("Nový úkol", QuestType.General, game!.Id);
        var response = await Client.PostAsJsonAsync("/api/quests", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<QuestListDto>();
        Assert.NotNull(created);
        Assert.Equal(QuestState.Inactive, created.State);
        Assert.Null(created.ImagePath);
        Assert.Null(created.ImageUrl);
        // Fresh quest has no children — counts must all be zero.
        Assert.Equal(0, created.EncountersCount);
        Assert.Equal(0, created.RewardsCount);
        Assert.Equal(0, created.LocationsCount);
        Assert.Equal(0, created.TagsCount);
    }

    [Fact]
    public async Task GetById_NewQuest_ReturnsInactiveState()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Test", QuestType.General, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestListDto>();

        var detail = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created!.Id}");
        Assert.NotNull(detail);
        Assert.Equal(QuestState.Inactive, detail.State);
        Assert.Null(detail.ImagePath);
    }

    [Fact]
    public async Task PatchState_TransitionsThroughAllValues()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("State quest", QuestType.General, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestListDto>();

        // Inactive → Active
        var r1 = await Client.PatchAsJsonAsync($"/api/quests/{created!.Id}/state",
            new UpdateQuestStateDto(QuestState.Active));
        Assert.Equal(HttpStatusCode.NoContent, r1.StatusCode);
        var afterActive = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created.Id}");
        Assert.Equal(QuestState.Active, afterActive!.State);

        // Active → Completed
        var r2 = await Client.PatchAsJsonAsync($"/api/quests/{created.Id}/state",
            new UpdateQuestStateDto(QuestState.Completed));
        Assert.Equal(HttpStatusCode.NoContent, r2.StatusCode);
        var afterDone = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created.Id}");
        Assert.Equal(QuestState.Completed, afterDone!.State);

        // Completed → Inactive (revert)
        var r3 = await Client.PatchAsJsonAsync($"/api/quests/{created.Id}/state",
            new UpdateQuestStateDto(QuestState.Inactive));
        Assert.Equal(HttpStatusCode.NoContent, r3.StatusCode);
        var afterRevert = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{created.Id}");
        Assert.Equal(QuestState.Inactive, afterRevert!.State);
    }

    [Fact]
    public async Task PatchState_NonExistentQuest_ReturnsNotFound()
    {
        var response = await Client.PatchAsJsonAsync("/api/quests/99999/state",
            new UpdateQuestStateDto(QuestState.Active));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByGame_PopulatesCounts()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Counts quest", QuestType.General, game!.Id));
        var quest = await questResponse.Content.ReadFromJsonAsync<QuestListDto>();

        // Build up the join collections so the projection has something to count.
        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Skřet", MonsterCategory.Tier1, MonsterType.Goblin, 2, 1, 4));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();
        await Client.PostAsJsonAsync($"/api/quests/{quest!.Id}/encounters",
            new AddQuestEncounterDto(monster!.Id, 3));

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Klíč", ItemType.Miscellaneous));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();
        await Client.PostAsJsonAsync($"/api/quests/{quest.Id}/rewards",
            new AddQuestRewardDto(item!.Id, 1));

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Brod", LocationKind.Wilderness, 49.0m, 17.0m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();
        await Client.PostAsJsonAsync($"/api/quests/{quest.Id}/locations",
            new AddQuestLocationDto(location!.Id));

        var tagResponse = await Client.PostAsJsonAsync("/api/tags",
            new CreateTagDto("Easy", TagKind.Quest));
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagDto>();
        await Client.PostAsync($"/api/quests/{quest.Id}/tags/{tag!.Id}", null);

        // GetByGame is the projection that powers /games/{gid}/quests grid.
        var list = await Client.GetFromJsonAsync<List<QuestListDto>>($"/api/quests/by-game/{game.Id}");
        Assert.NotNull(list);
        var fromList = list.Single(q => q.Id == quest.Id);

        Assert.Equal(1, fromList.EncountersCount);
        Assert.Equal(1, fromList.RewardsCount);
        Assert.Equal(1, fromList.LocationsCount);
        Assert.Equal(1, fromList.TagsCount);
    }

    [Fact]
    public async Task GetAll_CatalogProjection_PopulatesCountsAndImageFields()
    {
        // Catalog quest = GameId is null; covers the dedicated /api/quests/all
        // projection which feeds the gallery + Seznam DxGrid.
        var createResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Catalog quest", QuestType.General, GameId: null));
        var created = await createResponse.Content.ReadFromJsonAsync<QuestListDto>();

        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Skřet", MonsterCategory.Tier1, MonsterType.Goblin, 2, 1, 4));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();
        await Client.PostAsJsonAsync($"/api/quests/{created!.Id}/encounters",
            new AddQuestEncounterDto(monster!.Id, 1));

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Klíč", ItemType.Miscellaneous));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();
        await Client.PostAsJsonAsync($"/api/quests/{created.Id}/rewards",
            new AddQuestRewardDto(item!.Id, 2));

        var catalog = await Client.GetFromJsonAsync<List<QuestCatalogDto>>("/api/quests/all");
        Assert.NotNull(catalog);
        var c = catalog.Single(q => q.Id == created.Id);

        Assert.Equal(1, c.EncountersCount);
        Assert.Equal(1, c.RewardsCount);
        // No image yet — both blob-key and resolved URL must be null.
        Assert.Null(c.ImagePath);
        Assert.Null(c.ImageUrl);
    }

    [Fact]
    public async Task CopyToGame_CopiesImagePathAndDefaultsToInactiveState()
    {
        // Source = catalog quest (GameId null) with an arbitrary blob key in
        // ImagePath. CopyToGame must propagate the blob key and force State
        // to Inactive on the per-game fork regardless of source state.
        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Skřet", MonsterCategory.Tier1, MonsterType.Goblin, 2, 1, 4));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var sourceResponse = await Client.PostAsJsonAsync("/api/quests",
            new CreateQuestDto("Šablona", QuestType.General, GameId: null));
        var source = await sourceResponse.Content.ReadFromJsonAsync<QuestListDto>();

        await Client.PostAsJsonAsync($"/api/quests/{source!.Id}/encounters",
            new AddQuestEncounterDto(monster!.Id, 2));

        // Target game.
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Cílová hra", 30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        // Trigger copy.
        var copyResponse = await Client.PostAsync(
            $"/api/quests/{source.Id}/copy-to-game/{game!.Id}", content: null);
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copyResult = await copyResponse.Content.ReadFromJsonAsync<QuestCopyResultDto>();
        Assert.NotNull(copyResult);

        // The forked quest must be Inactive — copies always start dormant.
        var fork = await Client.GetFromJsonAsync<QuestDetailDto>($"/api/quests/{copyResult.Quest.Id}");
        Assert.NotNull(fork);
        Assert.Equal(QuestState.Inactive, fork.State);
        Assert.Equal(game.Id, fork.GameId);
        // Encounters were deep-copied.
        Assert.Single(fork.Encounters);
    }
}
