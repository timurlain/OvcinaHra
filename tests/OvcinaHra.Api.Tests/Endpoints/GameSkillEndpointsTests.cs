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

    private async Task<int> CreateSkillAsync(string name, PlayerClass? classRestriction)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var skill = new Skill
        {
            Name = name,
            ClassRestriction = classRestriction
        };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();
        return skill.Id;
    }

    private async Task CreateGameSkillAsync(int gameId, int skillId, int xpCost, int? levelRequirement)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        db.GameSkills.Add(new GameSkill
        {
            GameId = gameId,
            SkillId = skillId,
            XpCost = xpCost,
            LevelRequirement = levelRequirement
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Put_AddsSkillToGame()
    {
        var game = await CreateGameAsync("G1");
        var skillId = await CreateSkillAsync("Tichý úder", PlayerClass.Thief);

        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/skills/{skillId}",
            new UpsertGameSkillRequest(XpCost: 10, LevelRequirement: 2));

        Assert.True(
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 201 or 204, got {(int)response.StatusCode}");

        var listResp = await Client.GetAsync($"/api/games/{game.Id}/skills");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<List<GameSkillDto>>();
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(skillId, list[0].SkillId);
        Assert.Equal(10, list[0].XpCost);
        Assert.Equal(2, list[0].LevelRequirement);
    }

    [Fact]
    public async Task Put_SecondCall_UpdatesXpAndLevel()
    {
        var game = await CreateGameAsync("G2");
        var skillId = await CreateSkillAsync("Léčitelství", null);
        await CreateGameSkillAsync(game.Id, skillId, xpCost: 10, levelRequirement: 1);

        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/skills/{skillId}",
            new UpsertGameSkillRequest(XpCost: 20, LevelRequirement: 5));

        Assert.True(
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 201 or 204, got {(int)response.StatusCode}");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var gs = await db.GameSkills.SingleAsync(g => g.GameId == game.Id && g.SkillId == skillId);
        Assert.Equal(20, gs.XpCost);
        Assert.Equal(5, gs.LevelRequirement);
    }

    [Fact]
    public async Task Put_NegativeXpCost_Returns400()
    {
        var game = await CreateGameAsync("G3");
        var skillId = await CreateSkillAsync("Neg XP", null);

        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/skills/{skillId}",
            new UpsertGameSkillRequest(XpCost: -1, LevelRequirement: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_NegativeLevelRequirement_Returns400()
    {
        var game = await CreateGameAsync("G4");
        var skillId = await CreateSkillAsync("Neg Level", null);

        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/skills/{skillId}",
            new UpsertGameSkillRequest(XpCost: 0, LevelRequirement: -3));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_LevelRequirementNull_Accepted()
    {
        var game = await CreateGameAsync("G5");
        var skillId = await CreateSkillAsync("Null Level", null);

        var response = await Client.PutAsJsonAsync(
            $"/api/games/{game.Id}/skills/{skillId}",
            new UpsertGameSkillRequest(XpCost: 5, LevelRequirement: null));

        Assert.True(
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 201 or 204, got {(int)response.StatusCode}");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var gs = await db.GameSkills.SingleAsync(g => g.GameId == game.Id && g.SkillId == skillId);
        Assert.Equal(5, gs.XpCost);
        Assert.Null(gs.LevelRequirement);
    }

    [Fact]
    public async Task Get_ListsOnlySkillsInGame()
    {
        var game1 = await CreateGameAsync("Game1");
        var game2 = await CreateGameAsync("Game2");
        var skillA = await CreateSkillAsync("Skill A", PlayerClass.Warrior);
        var skillB = await CreateSkillAsync("Skill B", PlayerClass.Mage);

        await CreateGameSkillAsync(game1.Id, skillA, xpCost: 7, levelRequirement: null);
        await CreateGameSkillAsync(game2.Id, skillB, xpCost: 9, levelRequirement: 3);

        var response = await Client.GetAsync($"/api/games/{game1.Id}/skills");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<GameSkillDto>>();
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(skillA, list[0].SkillId);
        Assert.Equal("Skill A", list[0].SkillName);
        Assert.Equal(PlayerClass.Warrior, list[0].ClassRestriction);
        Assert.Equal(7, list[0].XpCost);
        Assert.Null(list[0].LevelRequirement);
    }

    [Fact]
    public async Task Delete_RemovesSkillFromGame()
    {
        var game = await CreateGameAsync("G7");
        var skillId = await CreateSkillAsync("K smazání", null);
        await CreateGameSkillAsync(game.Id, skillId, xpCost: 3, levelRequirement: null);

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/skills/{skillId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var gone = await db.GameSkills
            .SingleOrDefaultAsync(g => g.GameId == game.Id && g.SkillId == skillId);
        Assert.Null(gone);
    }

    [Fact]
    public async Task Delete_BlockedWhenRecipeRequiresSkill_Returns409()
    {
        var game = await CreateGameAsync("G8");
        var skillId = await CreateSkillAsync("S receptem v hře", null);
        await CreateGameSkillAsync(game.Id, skillId, xpCost: 5, levelRequirement: null);

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
                SkillId = skillId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/skills/{skillId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<WorldDbContext>();
        var stillThere = await db2.GameSkills
            .SingleOrDefaultAsync(g => g.GameId == game.Id && g.SkillId == skillId);
        Assert.NotNull(stillThere);
    }
}
