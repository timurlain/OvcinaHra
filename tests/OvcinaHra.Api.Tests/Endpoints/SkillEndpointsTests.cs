using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SkillEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private async Task<BuildingDetailDto> CreateBuildingAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto(name));
        return (await response.Content.ReadFromJsonAsync<BuildingDetailDto>())!;
    }

    [Fact]
    public async Task Post_CreatesClassSkill_ReturnsCreated()
    {
        var dto = new CreateSkillRequest(
            Name: "Tichý úder",
            ClassRestriction: PlayerClass.Thief,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: []);

        var response = await Client.PostAsJsonAsync("/api/skills", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(created);
        Assert.Equal("Tichý úder", created.Name);
        Assert.Equal(PlayerClass.Thief, created.ClassRestriction);
        Assert.True(created.Id > 0);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var persisted = await db.Skills.SingleOrDefaultAsync(s => s.Id == created.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Tichý úder", persisted.Name);
        Assert.Equal(PlayerClass.Thief, persisted.ClassRestriction);
    }

    [Fact]
    public async Task Post_CreatesAdventurerSkill_NullClassRestriction()
    {
        var dto = new CreateSkillRequest(
            Name: "Základní alchymie",
            ClassRestriction: null,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: []);

        var response = await Client.PostAsJsonAsync("/api/skills", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(created);
        Assert.Equal("Základní alchymie", created.Name);
        Assert.Null(created.ClassRestriction);
    }

    [Fact]
    public async Task Post_WithBuildingRequirements_PersistsAll()
    {
        var kovarna = await CreateBuildingAsync("Kovárna");
        var alchymie = await CreateBuildingAsync("Alchymistická dílna");

        var dto = new CreateSkillRequest(
            Name: "Pokročilé kovářství",
            ClassRestriction: PlayerClass.Warrior,
            Effect: "Umožňuje výrobu mistrovských zbraní",
            RequirementNotes: null,
            RequiredBuildingIds: [kovarna.Id, alchymie.Id]);

        var response = await Client.PostAsJsonAsync("/api/skills", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(created);
        Assert.Equal(2, created.RequiredBuildingIds.Count);
        Assert.Contains(kovarna.Id, created.RequiredBuildingIds);
        Assert.Contains(alchymie.Id, created.RequiredBuildingIds);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var reqs = await db.SkillBuildingRequirements
            .Where(r => r.SkillId == created.Id)
            .ToListAsync();
        Assert.Equal(2, reqs.Count);
        Assert.Contains(reqs, r => r.BuildingId == kovarna.Id);
        Assert.Contains(reqs, r => r.BuildingId == alchymie.Id);
    }

    [Fact]
    public async Task Post_DuplicateName_Returns409()
    {
        var dto = new CreateSkillRequest(
            Name: "Unikátní dovednost",
            ClassRestriction: PlayerClass.Mage,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: []);

        var first = await Client.PostAsJsonAsync("/api/skills", dto);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Client.PostAsJsonAsync("/api/skills", dto);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
