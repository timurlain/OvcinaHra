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

public class GameSkillEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private async Task<GameDetailDto> CreateGameAsync(string name = "Test Game")
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    /// <summary>
    /// Seeds a Skill template row directly via DbContext.
    /// </summary>
    private async Task<int> CreateTemplateSkillAsync(
        string name,
        SkillCategory category = SkillCategory.Class,
        string? effect = null,
        PlayerClass? classRestriction = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var skill = new Skill
        {
            Name = name,
            Category = category,
            Effect = effect,
            ClassRestriction = classRestriction
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();
        return skill.Id;
    }

    private static CreateGameSkillRequest MakeCreate(
        string name,
        int? templateSkillId = null,
        SkillCategory category = SkillCategory.Class,
        PlayerClass? classRestriction = null,
        string? effect = null,
        string? requirementNotes = null,
        IReadOnlyList<int>? buildingRequirementIds = null,
        int xpCost = 5,
        int? levelRequirement = null) =>
        new(
            TemplateSkillId: templateSkillId,
            Name: name,
            Category: category,
            ClassRestriction: classRestriction,
            Effect: effect,
            RequirementNotes: requirementNotes,
            BuildingRequirementIds: buildingRequirementIds ?? [],
            XpCost: xpCost,
            LevelRequirement: levelRequirement);

    private async Task<GameSkillDto> CreateGameSkillAsync(int gameId, CreateGameSkillRequest req)
    {
        var response = await Client.PostAsJsonAsync($"/api/games/{gameId}/skills", req);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameSkillDto>())!;
    }

    [Fact]
    public async Task Post_AddsFromTemplate_CopiesAllFields()
    {
        var game = await CreateGameAsync("TmplGame");
        var templateId = await CreateTemplateSkillAsync("Tichý úder", SkillCategory.Adventure, effect: "Test");

        var req = MakeCreate(
            name: "Test",
            templateSkillId: templateId,
            category: SkillCategory.Adventure,
            effect: "Test",
            xpCost: 10);

        var response = await Client.PostAsJsonAsync($"/api/games/{game.Id}/skills", req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<GameSkillDto>();
        Assert.NotNull(created);
        Assert.Equal(templateId, created!.TemplateSkillId);
        Assert.Equal(SkillCategory.Adventure, created.Category);
        Assert.Equal("Test", created.Effect);
        Assert.Equal("Test", created.Name);
        Assert.Equal(10, created.XpCost);
    }

    [Fact]
    public async Task Post_CreatesCustom_NullTemplate_Accepted()
    {
        var game = await CreateGameAsync("CustomGame");

        var req = MakeCreate(
            name: "Vlastní",
            templateSkillId: null,
            category: SkillCategory.Quest,
            xpCost: 5);

        var response = await Client.PostAsJsonAsync($"/api/games/{game.Id}/skills", req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<GameSkillDto>();
        Assert.NotNull(created);
        Assert.Null(created!.TemplateSkillId);
        Assert.Equal("Vlastní", created.Name);
        Assert.Equal(SkillCategory.Quest, created.Category);
    }

    [Fact]
    public async Task Post_DuplicateNameInSameGame_Returns409()
    {
        var game = await CreateGameAsync("DupGame");

        var first = await Client.PostAsJsonAsync($"/api/games/{game.Id}/skills", MakeCreate(name: "X"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Client.PostAsJsonAsync($"/api/games/{game.Id}/skills", MakeCreate(name: "X"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_NegativeXpCost_Returns400()
    {
        var game = await CreateGameAsync("NegXpGame");

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/skills",
            MakeCreate(name: "NegXp", xpCost: -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_NegativeLevelRequirement_Returns400()
    {
        var game = await CreateGameAsync("NegLvlGame");

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/skills",
            MakeCreate(name: "NegLvl", xpCost: 1, levelRequirement: -3));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_LevelRequirementNull_Accepted()
    {
        var game = await CreateGameAsync("NullLvlGame");

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/skills",
            MakeCreate(name: "NullLvl", xpCost: 5, levelRequirement: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameSkillDto>();
        Assert.NotNull(created);
        Assert.Null(created!.LevelRequirement);
    }

    [Fact]
    public async Task Post_UnknownTemplateId_Returns400()
    {
        var game = await CreateGameAsync("UnkTmpl");

        var response = await Client.PostAsJsonAsync(
            $"/api/games/{game.Id}/skills",
            MakeCreate(name: "X", templateSkillId: 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesAllFields_PreservesId()
    {
        var game = await CreateGameAsync("PutGame");
        var created = await CreateGameSkillAsync(game.Id, MakeCreate(
            name: "Původní",
            category: SkillCategory.Class,
            effect: "Pův. efekt",
            xpCost: 5,
            levelRequirement: 1));

        var update = new UpdateGameSkillRequest(
            Name: "Nový",
            Category: SkillCategory.Adventure,
            ClassRestriction: PlayerClass.Mage,
            Effect: "Nový efekt",
            RequirementNotes: "Nová poznámka",
            BuildingRequirementIds: [],
            XpCost: 17,
            LevelRequirement: 4);

        var putResp = await Client.PutAsJsonAsync($"/api/games/{game.Id}/skills/{created.Id}", update);
        Assert.Equal(HttpStatusCode.NoContent, putResp.StatusCode);

        var getResp = await Client.GetAsync($"/api/games/{game.Id}/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<GameSkillDto>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("Nový", fetched.Name);
        Assert.Equal(SkillCategory.Adventure, fetched.Category);
        Assert.Equal(PlayerClass.Mage, fetched.ClassRestriction);
        Assert.Equal("Nový efekt", fetched.Effect);
        Assert.Equal("Nová poznámka", fetched.RequirementNotes);
        Assert.Equal(17, fetched.XpCost);
        Assert.Equal(4, fetched.LevelRequirement);
    }

    [Fact]
    public async Task Put_ChangingNameToDuplicate_Returns409()
    {
        var game = await CreateGameAsync("DupPutGame");
        var a = await CreateGameSkillAsync(game.Id, MakeCreate(name: "Ajda"));
        var b = await CreateGameSkillAsync(game.Id, MakeCreate(name: "Brumla"));

        var update = new UpdateGameSkillRequest(
            Name: "Brumla",
            Category: SkillCategory.Class,
            ClassRestriction: null,
            Effect: null,
            RequirementNotes: null,
            BuildingRequirementIds: [],
            XpCost: 5,
            LevelRequirement: null);

        var response = await Client.PutAsJsonAsync($"/api/games/{game.Id}/skills/{a.Id}", update);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownBuildingId_Returns400()
    {
        var game = await CreateGameAsync("UnkBldgGame");
        var created = await CreateGameSkillAsync(game.Id, MakeCreate(name: "BldgGS"));

        var update = new UpdateGameSkillRequest(
            Name: "BldgGS",
            Category: SkillCategory.Class,
            ClassRestriction: null,
            Effect: null,
            RequirementNotes: null,
            BuildingRequirementIds: [999999],
            XpCost: 5,
            LevelRequirement: null);

        var response = await Client.PutAsJsonAsync($"/api/games/{game.Id}/skills/{created.Id}", update);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListsOnlySkillsInGame()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");

        await CreateGameSkillAsync(game1.Id, MakeCreate(name: "Jen v 1", xpCost: 7));
        await CreateGameSkillAsync(game2.Id, MakeCreate(name: "Jen v 2", xpCost: 9));

        var response = await Client.GetAsync($"/api/games/{game1.Id}/skills");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<GameSkillDto>>();
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("Jen v 1", list![0].Name);
        Assert.Equal(game1.Id, list[0].GameId);
    }

    [Fact]
    public async Task Delete_RemovesGameSkill()
    {
        var game = await CreateGameAsync("DelGame");
        var created = await CreateGameSkillAsync(game.Id, MakeCreate(name: "K smazání"));

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        Assert.Null(await db.GameSkills.SingleOrDefaultAsync(gs => gs.Id == created.Id));
    }

    [Fact]
    public async Task Delete_BlockedWhenRecipeReferences_Returns409()
    {
        var game = await CreateGameAsync("BlockDelGame");
        var created = await CreateGameSkillAsync(game.Id, MakeCreate(name: "S receptem"));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var item = new Item
            {
                Name = "Výstupní předmět",
                ItemType = default,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0),
                IsCraftable = true
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();

            var recipe = new CraftingRecipe
            {
                GameId = game.Id,
                OutputItemId = item.Id
            };
            db.CraftingRecipes.Add(recipe);
            await db.SaveChangesAsync();

            db.CraftingSkillRequirements.Add(new CraftingSkillRequirement
            {
                CraftingRecipeId = recipe.Id,
                GameSkillId = created.Id
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/skills/{created.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<WorldDbContext>();
        Assert.NotNull(await db2.GameSkills.SingleOrDefaultAsync(gs => gs.Id == created.Id));
    }

    [Fact]
    public async Task DeletingTemplate_NullsOutTemplateSkillIdOnCopies()
    {
        var game = await CreateGameAsync("CascadeGame");
        var templateId = await CreateTemplateSkillAsync("Šablona k smazání", SkillCategory.Class);
        var gs = await CreateGameSkillAsync(game.Id, MakeCreate(
            name: "Kopie šablony",
            templateSkillId: templateId));

        Assert.Equal(templateId, gs.TemplateSkillId);

        var deleteResp = await Client.DeleteAsync($"/api/skills/{templateId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var persisted = await db.GameSkills.SingleOrDefaultAsync(g => g.Id == gs.Id);
        Assert.NotNull(persisted);
        Assert.Null(persisted!.TemplateSkillId);

        Assert.Null(await db.Skills.SingleOrDefaultAsync(s => s.Id == templateId));
    }
}
