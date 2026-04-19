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
}
