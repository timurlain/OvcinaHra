using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SkillEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
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

    // Note (issue #153): Delete is now blocked at the API when GameSkill copies
    // reference the template — see `Delete_BlockedWith409_WhenCopiesExist` near
    // the bottom of this file. The previous "cascade SetNull" contract that
    // lived here was replaced because the designer brief is explicit:
    // "Smazat zablokováno když existují kopie v hrách". The 409 path keeps
    // the copies authoritatively rooted in their templates.

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

    // ──────────────────────────────────────────────────────────────────
    //  Issue #153 — usage rollup + delete-blocked
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsZeroUsage_WhenNoCopiesExist()
    {
        var skill = await CreateSkillAsync("Solo Skill", PlayerClass.Mage, "Effect", null, []);

        var resp = await Client.GetAsync($"/api/skills/{skill.Id}");
        resp.EnsureSuccessStatusCode();
        var fetched = await resp.Content.ReadFromJsonAsync<SkillDto>();

        Assert.NotNull(fetched);
        Assert.Equal(0, fetched!.UsageCount);
    }

    [Fact]
    public async Task GetById_PopulatesUsageCount_WhenCopiesExist()
    {
        var skill = await CreateSkillAsync("Forked Skill", PlayerClass.Warrior, "Effect", null, []);
        await SeedGameAndCopyAsync(skill.Id, "Hra A");
        await SeedGameAndCopyAsync(skill.Id, "Hra B");

        var resp = await Client.GetAsync($"/api/skills/{skill.Id}");
        resp.EnsureSuccessStatusCode();
        var fetched = await resp.Content.ReadFromJsonAsync<SkillDto>();

        Assert.NotNull(fetched);
        Assert.Equal(2, fetched!.UsageCount);
    }

    [Fact]
    public async Task GetAll_PopulatesUsageCount_PerTemplate()
    {
        var s1 = await CreateSkillAsync("Tpl-1", null, null, null, []);
        var s2 = await CreateSkillAsync("Tpl-2", null, null, null, []);
        await SeedGameAndCopyAsync(s1.Id, "Hra X");
        await SeedGameAndCopyAsync(s2.Id, "Hra Y");
        await SeedGameAndCopyAsync(s2.Id, "Hra Z");

        var list = await Client.GetFromJsonAsync<List<SkillDto>>("/api/skills");

        Assert.NotNull(list);
        Assert.Equal(1, list!.Single(x => x.Id == s1.Id).UsageCount);
        Assert.Equal(2, list!.Single(x => x.Id == s2.Id).UsageCount);
    }

    [Fact]
    public async Task GetUsage_ReturnsGames_WhenCopiesExist()
    {
        var skill = await CreateSkillAsync("Usage Skill", null, "Effect", null, []);
        await SeedGameAndCopyAsync(skill.Id, "První hra");
        await SeedGameAndCopyAsync(skill.Id, "Druhá hra");

        var resp = await Client.GetAsync($"/api/skills/{skill.Id}/usage");
        resp.EnsureSuccessStatusCode();
        var usage = await resp.Content.ReadFromJsonAsync<SkillUsageDto>();

        Assert.NotNull(usage);
        Assert.Equal(skill.Id, usage!.SkillId);
        Assert.Equal(2, usage.CopiesCount);
        Assert.Equal(2, usage.Games.Count);
        Assert.Contains(usage.Games, g => g.GameName == "První hra");
        Assert.Contains(usage.Games, g => g.GameName == "Druhá hra");
    }

    [Fact]
    public async Task GetUsage_ReturnsEmpty_WhenNoCopies()
    {
        var skill = await CreateSkillAsync("Empty Usage", null, null, null, []);

        var resp = await Client.GetAsync($"/api/skills/{skill.Id}/usage");
        resp.EnsureSuccessStatusCode();
        var usage = await resp.Content.ReadFromJsonAsync<SkillUsageDto>();

        Assert.NotNull(usage);
        Assert.Equal(0, usage!.CopiesCount);
        Assert.Empty(usage.Games);
    }

    [Fact]
    public async Task GetUsage_ReturnsNotFound_WhenSkillMissing()
    {
        var resp = await Client.GetAsync("/api/skills/999999/usage");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_BlockedWith409_WhenCopiesExist()
    {
        var skill = await CreateSkillAsync("Locked", null, "Effect", null, []);
        await SeedGameAndCopyAsync(skill.Id, "Aktivní hra");

        var resp = await Client.DeleteAsync($"/api/skills/{skill.Id}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("nelze smazat", problem!.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hrách", problem.Detail);

        // The skill must still exist after the blocked delete.
        var getResp = await Client.GetAsync($"/api/skills/{skill.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    [Fact]
    public async Task Delete_AllowedWhenNoCopies()
    {
        var skill = await CreateSkillAsync("Free To Delete", null, null, null, []);

        var resp = await Client.DeleteAsync($"/api/skills/{skill.Id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var getResp = await Client.GetAsync($"/api/skills/{skill.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    /// <summary>
    /// Seeds a Game + a GameSkill that copies the given template directly via
    /// DbContext. Avoids the public POST /api/games/{id}/skills route so the
    /// usage-count tests stay focused on read behavior.
    /// </summary>
    private async Task<int> SeedGameAndCopyAsync(int templateSkillId, string gameName)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var template = await db.Skills.SingleAsync(s => s.Id == templateSkillId);
        var game = new Game
        {
            Name = gameName,
            Edition = 1,
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 3),
            Status = GameStatus.Draft
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var copy = new GameSkill
        {
            GameId = game.Id,
            TemplateSkillId = template.Id,
            Name = template.Name,
            Category = template.Category,
            ClassRestriction = template.ClassRestriction,
            Effect = template.Effect,
            RequirementNotes = template.RequirementNotes,
            XpCost = 5,
            LevelRequirement = null
        };
        db.GameSkills.Add(copy);
        await db.SaveChangesAsync();
        return copy.Id;
    }
}
