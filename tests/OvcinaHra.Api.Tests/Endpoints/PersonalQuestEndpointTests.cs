using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
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
}
