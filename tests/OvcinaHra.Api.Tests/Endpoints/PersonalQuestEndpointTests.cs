using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class PersonalQuestEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var quests = await Client.GetFromJsonAsync<List<PersonalQuestListDto>>("/api/personal-quests");
        Assert.NotNull(quests);
        Assert.Empty(quests);
    }

    [Fact]
    public async Task Create_WithValidDto_ReturnsCreatedWithId()
    {
        var dto = new CreatePersonalQuestDto(
            Name: "Zachránit vesničany",
            Difficulty: TreasureQuestDifficulty.Midgame,
            Description: "Najdi a osvoboď unesené vesničany z lupičského tábora.",
            AllowWarrior: true,
            AllowThief: true,
            QuestCardText: "Lupiči unesli vesničany!",
            RewardCardText: "Vděčnost vesnice",
            RewardNote: "Vděčnost starosty",
            Notes: "Pro úroveň 2+");

        var response = await Client.PostAsJsonAsync("/api/personal-quests", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();
        Assert.NotNull(created);
        Assert.True(created.Id > 0);
        Assert.Equal("Zachránit vesničany", created.Name);
        Assert.Equal(TreasureQuestDifficulty.Midgame, created.Difficulty);
        Assert.True(created.AllowWarrior);
        Assert.False(created.AllowMage);
        Assert.True(created.AllowThief);
        Assert.Equal("Vděčnost starosty", created.RewardNote);
        Assert.Empty(created.SkillRewards);
        Assert.Empty(created.ItemRewards);
    }

    [Fact]
    public async Task GetById_Found_ReturnsWithRewards()
    {
        // Arrange — create a skill + item + quest with both reward types via direct DB insert
        int questId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var skill = new Skill { Name = "Léčivá dlaň" };
            var item = new Item
            {
                Name = "Lektvar léčení",
                ItemType = ItemType.Potion,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Skills.Add(skill);
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var quest = new PersonalQuest
            {
                Name = "Hrdinský čin",
                Difficulty = TreasureQuestDifficulty.Early,
                AllowWarrior = true,
                SkillRewards = [new PersonalQuestSkillReward { SkillId = skill.Id }],
                ItemRewards = [new PersonalQuestItemReward { ItemId = item.Id, Quantity = 2 }]
            };
            db.PersonalQuests.Add(quest);
            await db.SaveChangesAsync();
            questId = quest.Id;
        }

        // Act
        var result = await Client.GetFromJsonAsync<PersonalQuestDetailDto>($"/api/personal-quests/{questId}");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(questId, result.Id);
        Assert.Equal("Hrdinský čin", result.Name);
        var skillReward = Assert.Single(result.SkillRewards);
        Assert.Equal("Léčivá dlaň", skillReward.SkillName);
        var itemReward = Assert.Single(result.ItemRewards);
        Assert.Equal("Lektvar léčení", itemReward.ItemName);
        Assert.Equal(2, itemReward.Quantity);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync("/api/personal-quests/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields_Persists()
    {
        // Arrange — create via POST
        var createDto = new CreatePersonalQuestDto(
            Name: "Původní název",
            Difficulty: TreasureQuestDifficulty.Start,
            Description: "Původní popis",
            AllowWarrior: true);
        var createResponse = await Client.PostAsJsonAsync("/api/personal-quests", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        // Act — PUT with changed fields
        var updateDto = new UpdatePersonalQuestDto(
            Name: "Nový název",
            Difficulty: TreasureQuestDifficulty.Lategame,
            Description: "Nový popis",
            AllowWarrior: false,
            AllowArcher: true,
            AllowMage: true,
            AllowThief: false,
            QuestCardText: "Karta úkolu",
            RewardCardText: "Karta odměny",
            RewardNote: "Poznámka k odměně",
            Notes: "Interní poznámky");
        var response = await Client.PutAsJsonAsync($"/api/personal-quests/{created!.Id}", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var reloaded = await Client.GetFromJsonAsync<PersonalQuestDetailDto>($"/api/personal-quests/{created.Id}");
        Assert.NotNull(reloaded);
        Assert.Equal("Nový název", reloaded.Name);
        Assert.Equal(TreasureQuestDifficulty.Lategame, reloaded.Difficulty);
        Assert.Equal("Nový popis", reloaded.Description);
        Assert.False(reloaded.AllowWarrior);
        Assert.True(reloaded.AllowArcher);
        Assert.True(reloaded.AllowMage);
        Assert.False(reloaded.AllowThief);
        Assert.Equal("Karta úkolu", reloaded.QuestCardText);
        Assert.Equal("Karta odměny", reloaded.RewardCardText);
        Assert.Equal("Poznámka k odměně", reloaded.RewardNote);
        Assert.Equal("Interní poznámky", reloaded.Notes);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var updateDto = new UpdatePersonalQuestDto(
            Name: "Neexistující",
            Difficulty: TreasureQuestDifficulty.Early,
            Description: null,
            AllowWarrior: false, AllowArcher: false, AllowMage: false, AllowThief: false,
            QuestCardText: null, RewardCardText: null, RewardNote: null, Notes: null);

        var response = await Client.PutAsJsonAsync("/api/personal-quests/99999", updateDto);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingId_Removes()
    {
        var createDto = new CreatePersonalQuestDto(
            Name: "K smazání",
            Difficulty: TreasureQuestDifficulty.Early);
        var createResponse = await Client.PostAsJsonAsync("/api/personal-quests", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        var response = await Client.DeleteAsync($"/api/personal-quests/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await Client.GetAsync($"/api/personal-quests/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var response = await Client.DeleteAsync("/api/personal-quests/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Cascades_RemovesRewards()
    {
        // Arrange — quest with 1 skill reward + 1 item reward
        int questId;
        int skillId;
        int itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var skill = new Skill { Name = "Stopovačství" };
            var item = new Item
            {
                Name = "Luk strážce",
                ItemType = ItemType.Weapon,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Skills.Add(skill);
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var quest = new PersonalQuest
            {
                Name = "Kaskáda testu",
                Difficulty = TreasureQuestDifficulty.Midgame,
                SkillRewards = [new PersonalQuestSkillReward { SkillId = skill.Id }],
                ItemRewards = [new PersonalQuestItemReward { ItemId = item.Id, Quantity = 1 }]
            };
            db.PersonalQuests.Add(quest);
            await db.SaveChangesAsync();
            questId = quest.Id;
            skillId = skill.Id;
            itemId = item.Id;
        }

        // Act — delete via API
        var response = await Client.DeleteAsync($"/api/personal-quests/{questId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert — quest gone, rewards gone, but Skill + Item themselves remain
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

            Assert.Null(await db.PersonalQuests.FindAsync(questId));

            var skillRewards = await db.PersonalQuestSkillRewards
                .Where(sr => sr.PersonalQuestId == questId)
                .ToListAsync();
            Assert.Empty(skillRewards);

            var itemRewards = await db.PersonalQuestItemRewards
                .Where(ir => ir.PersonalQuestId == questId)
                .ToListAsync();
            Assert.Empty(itemRewards);

            Assert.NotNull(await db.Skills.FindAsync(skillId));
            Assert.NotNull(await db.Items.FindAsync(itemId));
        }
    }

    // ======================================================================
    // Batch D — Per-game link endpoints
    // ======================================================================

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var result = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
            $"/api/personal-quests/by-game/{game!.Id}");
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByGame_WithRewards_BuildsSummary()
    {
        // Arrange — seed a game, a PQ with 1 skill + 1 item reward (qty 2), and the GPQ link
        int gameId;
        int questId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

            var skill = new Skill { Name = "Druid" };
            var item = new Item
            {
                Name = "Léčivá dlaň",
                ItemType = ItemType.Potion,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Skills.Add(skill);
            db.Items.Add(item);

            var game = new Game
            {
                Name = "Hra s odměnami",
                Edition = 1,
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 6, 3)
            };
            db.Games.Add(game);

            var quest = new PersonalQuest
            {
                Name = "Úkol druida",
                Difficulty = TreasureQuestDifficulty.Early,
                AllowMage = true,
                SkillRewards = [new PersonalQuestSkillReward { Skill = skill }],
                ItemRewards = [new PersonalQuestItemReward { Item = item, Quantity = 2 }]
            };
            db.PersonalQuests.Add(quest);
            await db.SaveChangesAsync();

            db.GamePersonalQuests.Add(new GamePersonalQuest
            {
                GameId = game.Id,
                PersonalQuestId = quest.Id,
                XpCost = 15,
                PerKingdomLimit = 1
            });
            await db.SaveChangesAsync();

            gameId = game.Id;
            questId = quest.Id;
        }

        // Act
        var result = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
            $"/api/personal-quests/by-game/{gameId}");

        // Assert
        Assert.NotNull(result);
        var row = Assert.Single(result);
        Assert.Equal(questId, row.Id);
        Assert.Equal("Úkol druida", row.Name);
        Assert.Equal(gameId, row.GameId);
        Assert.Equal(15, row.XpCost);
        Assert.Equal(1, row.PerKingdomLimit);
        Assert.Equal("Druid │ Léčivá dlaň ×2", row.RewardSummary);
    }

    [Fact]
    public async Task CreateGameLink_Persists_Returns201()
    {
        // Arrange — game + quest
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra link", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Linkovaný úkol", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        // Act
        var response = await Client.PostAsJsonAsync("/api/personal-quests/game-link",
            new CreateGamePersonalQuestDto(game!.Id, quest!.Id, XpCost: 20, PerKingdomLimit: 2));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GamePersonalQuestDto>();
        Assert.NotNull(created);
        Assert.Equal(game.Id, created.GameId);
        Assert.Equal(quest.Id, created.PersonalQuestId);
        Assert.Equal(20, created.XpCost);
        Assert.Equal(2, created.PerKingdomLimit);

        // Verify it shows up in by-game
        var list = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
            $"/api/personal-quests/by-game/{game.Id}");
        Assert.NotNull(list);
        Assert.Single(list);
    }

    [Fact]
    public async Task CreateGameLink_DuplicatePair_ReturnsConflict()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra dup", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Duplikát", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        var dto = new CreateGamePersonalQuestDto(game!.Id, quest!.Id, XpCost: 5, PerKingdomLimit: null);

        var first = await Client.PostAsJsonAsync("/api/personal-quests/game-link", dto);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Client.PostAsJsonAsync("/api/personal-quests/game-link", dto);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task UpdateGameLink_TunesXpCost()
    {
        // Arrange — game + quest + link (XpCost=10)
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra update", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Ladit", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        await Client.PostAsJsonAsync("/api/personal-quests/game-link",
            new CreateGamePersonalQuestDto(game!.Id, quest!.Id, XpCost: 10, PerKingdomLimit: null));

        // Act — update XpCost to 25
        var response = await Client.PutAsJsonAsync(
            $"/api/personal-quests/game-link/{game.Id}/{quest.Id}",
            new UpdateGamePersonalQuestDto(XpCost: 25, PerKingdomLimit: 3));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
            $"/api/personal-quests/by-game/{game.Id}");
        var row = Assert.Single(list!);
        Assert.Equal(25, row.XpCost);
        Assert.Equal(3, row.PerKingdomLimit);
    }

    [Fact]
    public async Task UpdateGameLink_NotFound_Returns404()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/personal-quests/game-link/99999/99999",
            new UpdateGamePersonalQuestDto(XpCost: 1, PerKingdomLimit: null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteGameLink_RemovesConfig_KeepsCatalogEntry()
    {
        // Arrange
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra unlink", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Ponechat v katalogu", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        await Client.PostAsJsonAsync("/api/personal-quests/game-link",
            new CreateGamePersonalQuestDto(game!.Id, quest!.Id));

        // Act
        var response = await Client.DeleteAsync(
            $"/api/personal-quests/game-link/{game.Id}/{quest.Id}");

        // Assert — link gone
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var list = await Client.GetFromJsonAsync<List<GamePersonalQuestListDto>>(
            $"/api/personal-quests/by-game/{game.Id}");
        Assert.NotNull(list);
        Assert.Empty(list);

        // Assert — catalog entry still exists
        var catalogGet = await Client.GetAsync($"/api/personal-quests/{quest.Id}");
        Assert.Equal(HttpStatusCode.OK, catalogGet.StatusCode);
    }

    // ======================================================================
    // Batch D — Reward link endpoints
    // ======================================================================

    [Fact]
    public async Task AddSkillReward_Persists_Returns201()
    {
        // Arrange — skill + quest
        int skillId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var skill = new Skill { Name = "Mrštnost" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();
            skillId = skill.Id;
        }

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Skillová odměna", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        // Act
        var response = await Client.PostAsJsonAsync(
            $"/api/personal-quests/{quest!.Id}/skill-rewards",
            new AddSkillRewardDto(skillId));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reloaded = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
            $"/api/personal-quests/{quest.Id}");
        var reward = Assert.Single(reloaded!.SkillRewards);
        Assert.Equal(skillId, reward.SkillId);
        Assert.Equal("Mrštnost", reward.SkillName);
    }

    [Fact]
    public async Task AddSkillReward_Duplicate_ReturnsConflict()
    {
        int skillId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var skill = new Skill { Name = "Síla" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();
            skillId = skill.Id;
        }

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Duplikátní skill", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        var first = await Client.PostAsJsonAsync(
            $"/api/personal-quests/{quest!.Id}/skill-rewards",
            new AddSkillRewardDto(skillId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Client.PostAsJsonAsync(
            $"/api/personal-quests/{quest.Id}/skill-rewards",
            new AddSkillRewardDto(skillId));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RemoveSkillReward_Existing_Removes()
    {
        int skillId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var skill = new Skill { Name = "Odstranit" };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();
            skillId = skill.Id;
        }

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "K odebrání skillu", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        await Client.PostAsJsonAsync(
            $"/api/personal-quests/{quest!.Id}/skill-rewards",
            new AddSkillRewardDto(skillId));

        // Act
        var response = await Client.DeleteAsync(
            $"/api/personal-quests/{quest.Id}/skill-rewards/{skillId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var reloaded = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
            $"/api/personal-quests/{quest.Id}");
        Assert.Empty(reloaded!.SkillRewards);
    }

    [Fact]
    public async Task RemoveSkillReward_NotLinked_Returns404()
    {
        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Idempotent", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        var response = await Client.DeleteAsync(
            $"/api/personal-quests/{quest!.Id}/skill-rewards/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddItemReward_StoresQuantity()
    {
        int itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var item = new Item
            {
                Name = "Lektvar síly",
                ItemType = ItemType.Potion,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "Item odměna", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        // Act — add with qty 3
        var response = await Client.PostAsJsonAsync(
            $"/api/personal-quests/{quest!.Id}/item-rewards",
            new AddItemRewardDto(itemId, Quantity: 3));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reloaded = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
            $"/api/personal-quests/{quest.Id}");
        var reward = Assert.Single(reloaded!.ItemRewards);
        Assert.Equal(itemId, reward.ItemId);
        Assert.Equal("Lektvar síly", reward.ItemName);
        Assert.Equal(3, reward.Quantity);
    }

    [Fact]
    public async Task RemoveItemReward_Existing_Removes()
    {
        int itemId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var item = new Item
            {
                Name = "K odebrání",
                ItemType = ItemType.Potion,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        var questResponse = await Client.PostAsJsonAsync("/api/personal-quests",
            new CreatePersonalQuestDto(Name: "K odebrání itemu", Difficulty: TreasureQuestDifficulty.Early));
        var quest = await questResponse.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();

        await Client.PostAsJsonAsync(
            $"/api/personal-quests/{quest!.Id}/item-rewards",
            new AddItemRewardDto(itemId, Quantity: 1));

        // Act
        var response = await Client.DeleteAsync(
            $"/api/personal-quests/{quest.Id}/item-rewards/{itemId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var reloaded = await Client.GetFromJsonAsync<PersonalQuestDetailDto>(
            $"/api/personal-quests/{quest.Id}");
        Assert.Empty(reloaded!.ItemRewards);
    }

    [Fact]
    public async Task CreateQuest_WithXpCost_Persisted()
    {
        var create = new CreatePersonalQuestDto(
            Name: "Test XP Quest",
            Difficulty: TreasureQuestDifficulty.Early,
            Description: null,
            AllowWarrior: true, AllowArcher: false, AllowMage: false, AllowThief: false,
            QuestCardText: null, RewardCardText: null, RewardNote: null, Notes: null,
            XpCost: 12);

        var resp = await Client.PostAsJsonAsync("/api/personal-quests", create);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PersonalQuestDetailDto>();
        Assert.NotNull(body);
        Assert.Equal(12, body.XpCost);
    }
}
