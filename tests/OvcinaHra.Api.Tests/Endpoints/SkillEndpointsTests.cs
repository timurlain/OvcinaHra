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

    [Fact]
    public async Task Get_ReturnsAllSkills()
    {
        var s1 = new CreateSkillRequest("Stopování", PlayerClass.Archer, null, null, []);
        var s2 = new CreateSkillRequest("Léčitelství", null, "Léčí 1 zranění", null, []);
        var s3 = new CreateSkillRequest("Runy ochrany", PlayerClass.Mage, null, "Ve svatyni", []);

        foreach (var s in new[] { s1, s2, s3 })
        {
            var r = await Client.PostAsJsonAsync("/api/skills", s);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }

        var response = await Client.GetAsync("/api/skills");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<SkillDto>>();
        Assert.NotNull(list);
        Assert.Equal(3, list.Count);
        Assert.Contains(list, s => s.Name == "Stopování" && s.ClassRestriction == PlayerClass.Archer);
        Assert.Contains(list, s => s.Name == "Léčitelství" && s.ClassRestriction == null && s.Effect == "Léčí 1 zranění");
        Assert.Contains(list, s => s.Name == "Runy ochrany" && s.RequirementNotes == "Ve svatyni");
    }

    [Fact]
    public async Task GetById_ReturnsSkillWithBuildingIds()
    {
        var kovarna = await CreateBuildingAsync("Kovárna G");
        var alchymie = await CreateBuildingAsync("Dílna G");

        var createDto = new CreateSkillRequest(
            Name: "Dvojitý úder",
            ClassRestriction: PlayerClass.Warrior,
            Effect: "Dvakrát za kolo",
            RequirementNotes: null,
            RequiredBuildingIds: [kovarna.Id, alchymie.Id]);

        var createResp = await Client.PostAsJsonAsync("/api/skills", createDto);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(created);

        var response = await Client.GetAsync($"/api/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Dvojitý úder", fetched.Name);
        Assert.Equal(2, fetched.RequiredBuildingIds.Count);
        Assert.Contains(kovarna.Id, fetched.RequiredBuildingIds);
        Assert.Contains(alchymie.Id, fetched.RequiredBuildingIds);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync("/api/skills/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesScalarFields()
    {
        var created = await CreateSkillAsync("Původní název", PlayerClass.Thief, "Pův. efekt", "Pův. poznámka", []);

        var update = new UpdateSkillRequest(
            Name: "Nový název",
            ClassRestriction: PlayerClass.Mage,
            Effect: "Nový efekt",
            RequirementNotes: "Nová poznámka",
            RequiredBuildingIds: []);

        var putResp = await Client.PutAsJsonAsync($"/api/skills/{created.Id}", update);
        Assert.Equal(HttpStatusCode.NoContent, putResp.StatusCode);

        var getResp = await Client.GetAsync($"/api/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(fetched);
        Assert.Equal("Nový název", fetched.Name);
        Assert.Equal(PlayerClass.Mage, fetched.ClassRestriction);
        Assert.Equal("Nový efekt", fetched.Effect);
        Assert.Equal("Nová poznámka", fetched.RequirementNotes);
    }

    [Fact]
    public async Task Put_ReplacesBuildingRequirementsAsSet()
    {
        var bA = await CreateBuildingAsync("Budova A");
        var bB = await CreateBuildingAsync("Budova B");
        var bC = await CreateBuildingAsync("Budova C");

        var created = await CreateSkillAsync("Sada budov", PlayerClass.Warrior, null, null, [bA.Id, bB.Id]);

        var update = new UpdateSkillRequest(
            Name: "Sada budov",
            ClassRestriction: PlayerClass.Warrior,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: [bB.Id, bC.Id]);

        var putResp = await Client.PutAsJsonAsync($"/api/skills/{created.Id}", update);
        Assert.Equal(HttpStatusCode.NoContent, putResp.StatusCode);

        var getResp = await Client.GetAsync($"/api/skills/{created.Id}");
        var fetched = await getResp.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(fetched);
        Assert.Equal(2, fetched.RequiredBuildingIds.Count);
        Assert.Contains(bB.Id, fetched.RequiredBuildingIds);
        Assert.Contains(bC.Id, fetched.RequiredBuildingIds);
        Assert.DoesNotContain(bA.Id, fetched.RequiredBuildingIds);
    }

    [Fact]
    public async Task Put_NonExistentId_Returns404()
    {
        var update = new UpdateSkillRequest(
            Name: "Cokoli",
            ClassRestriction: null,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: []);

        var response = await Client.PutAsJsonAsync("/api/skills/999999", update);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_DuplicateName_Returns409()
    {
        var first = await CreateSkillAsync("První dovednost", null, null, null, []);
        var second = await CreateSkillAsync("Druhá dovednost", null, null, null, []);

        var update = new UpdateSkillRequest(
            Name: "Druhá dovednost",
            ClassRestriction: null,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: []);

        var response = await Client.PutAsJsonAsync($"/api/skills/{first.Id}", update);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_SameName_Succeeds()
    {
        var created = await CreateSkillAsync("Stejné jméno", PlayerClass.Archer, null, null, []);

        var update = new UpdateSkillRequest(
            Name: "Stejné jméno",
            ClassRestriction: PlayerClass.Archer,
            Effect: "Přidaný efekt",
            RequirementNotes: null,
            RequiredBuildingIds: []);

        var response = await Client.PutAsJsonAsync($"/api/skills/{created.Id}", update);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesSkill()
    {
        var created = await CreateSkillAsync("Ke smazání", PlayerClass.Thief, null, null, []);

        var response = await Client.DeleteAsync($"/api/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var gone = await db.Skills.SingleOrDefaultAsync(s => s.Id == created.Id);
        Assert.Null(gone);
    }

    [Fact]
    public async Task Delete_WithGameSkillReference_NullsOutTemplateSkillId()
    {
        // Template delete no longer blocks when per-game copies reference it;
        // the FK cascades SetNull so per-game GameSkill rows survive without a template.
        var skill = await CreateSkillAsync("Šablona s kopiemi", null, null, null, []);
        int skillId = skill.Id;

        int gameSkillId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var game = new Game
            {
                Name = "Testovací hra",
                Edition = 1,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 1, 3),
                Status = default
            };
            db.Games.Add(game);
            await db.SaveChangesAsync();

            var gs = new GameSkill
            {
                GameId = game.Id,
                TemplateSkillId = skillId,
                Name = "Kopie šablony",
                Category = SkillCategory.Class,
                XpCost = 10,
                LevelRequirement = null
            };
            db.GameSkills.Add(gs);
            await db.SaveChangesAsync();
            gameSkillId = gs.Id;
        }

        var response = await Client.DeleteAsync($"/api/skills/{skillId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<WorldDbContext>();

        // Template is gone.
        Assert.Null(await db2.Skills.SingleOrDefaultAsync(s => s.Id == skillId));

        // GameSkill row survives with TemplateSkillId nulled.
        var persistedGs = await db2.GameSkills.SingleOrDefaultAsync(g => g.Id == gameSkillId);
        Assert.NotNull(persistedGs);
        Assert.Null(persistedGs!.TemplateSkillId);
    }

    [Fact]
    public async Task Delete_NonExistentId_Returns404()
    {
        var response = await Client.DeleteAsync("/api/skills/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_PersistsCategory()
    {
        var dto = new CreateSkillRequest(
            Name: "Questová šablona",
            ClassRestriction: null,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: [],
            Category: SkillCategory.Quest);

        var response = await Client.PostAsJsonAsync("/api/skills", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(created);
        Assert.Equal(SkillCategory.Quest, created!.Category);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var persisted = await db.Skills.SingleOrDefaultAsync(s => s.Id == created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(SkillCategory.Quest, persisted!.Category);
    }

    [Fact]
    public async Task Put_UpdatesCategory()
    {
        // Seed as default Class category.
        var created = await CreateSkillAsync("Povolánková", PlayerClass.Warrior, null, null, []);
        Assert.Equal(SkillCategory.Class, created.Category);

        var update = new UpdateSkillRequest(
            Name: "Povolánková",
            ClassRestriction: PlayerClass.Warrior,
            Effect: null,
            RequirementNotes: null,
            RequiredBuildingIds: [],
            Category: SkillCategory.Adventure);

        var putResp = await Client.PutAsJsonAsync($"/api/skills/{created.Id}", update);
        Assert.Equal(HttpStatusCode.NoContent, putResp.StatusCode);

        var getResp = await Client.GetAsync($"/api/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<SkillDto>();
        Assert.NotNull(fetched);
        Assert.Equal(SkillCategory.Adventure, fetched!.Category);
    }

    private async Task<SkillDto> CreateSkillAsync(
        string name,
        PlayerClass? classRestriction,
        string? effect,
        string? requirementNotes,
        IReadOnlyList<int> requiredBuildingIds)
    {
        var dto = new CreateSkillRequest(name, classRestriction, effect, requirementNotes, requiredBuildingIds);
        var response = await Client.PostAsJsonAsync("/api/skills", dto);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SkillDto>())!;
    }
}
