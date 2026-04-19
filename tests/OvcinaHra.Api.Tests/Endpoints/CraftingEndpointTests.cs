using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class CraftingEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var recipes = await Client.GetFromJsonAsync<List<CraftingRecipeListDto>>($"/api/crafting/by-game/{game!.Id}");

        Assert.NotNull(recipes);
        Assert.Empty(recipes);
    }

    [Fact]
    public async Task Create_ValidRecipe_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var dto = new CreateCraftingRecipeDto(game!.Id, item!.Id);
        var response = await Client.PostAsJsonAsync("/api/crafting", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CraftingRecipeListDto>();
        Assert.NotNull(created);
        Assert.Equal(game.Id, created.GameId);
        Assert.Equal(item.Id, created.OutputItemId);
    }

    [Fact]
    public async Task GetById_ReturnsRecipeWithDetails()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Luk", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game!.Id, item!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();

        var result = await Client.GetFromJsonAsync<CraftingRecipeDetailDto>($"/api/crafting/{created!.Id}");

        Assert.NotNull(result);
        Assert.Equal(created.Id, result.Id);
        Assert.Equal(item.Id, result.OutputItemId);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/crafting/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingRecipe_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Štít", ItemType.Armor));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game!.Id, item!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();

        var response = await Client.DeleteAsync($"/api/crafting/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AddIngredient_Valid_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var outputItemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Lektvar", ItemType.Potion));
        var outputItem = await outputItemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var ingredientItemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Bylina", ItemType.Ingredient));
        var ingredientItem = await ingredientItemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var recipeResponse = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game!.Id, outputItem!.Id));
        var recipe = await recipeResponse.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();

        var dto = new AddCraftingIngredientDto(ingredientItem!.Id, 3);
        var response = await Client.PostAsJsonAsync($"/api/crafting/{recipe!.Id}/ingredients", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RemoveIngredient_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var outputItemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Lektvar", ItemType.Potion));
        var outputItem = await outputItemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var ingredientItemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Bylina", ItemType.Ingredient));
        var ingredientItem = await ingredientItemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var recipeResponse = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game!.Id, outputItem!.Id));
        var recipe = await recipeResponse.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();

        await Client.PostAsJsonAsync($"/api/crafting/{recipe!.Id}/ingredients",
            new AddCraftingIngredientDto(ingredientItem!.Id, 2));

        var response = await Client.DeleteAsync($"/api/crafting/{recipe.Id}/ingredients/{ingredientItem.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AddBuildingRequirement_Valid_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var buildingResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Kovárna"));
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var recipeResponse = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game!.Id, item!.Id));
        var recipe = await recipeResponse.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();

        var dto = new AddCraftingBuildingReqDto(building!.Id);
        var response = await Client.PostAsJsonAsync($"/api/crafting/{recipe!.Id}/buildings", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RemoveBuildingRequirement_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var buildingResponse = await Client.PostAsJsonAsync("/api/buildings",
            new CreateBuildingDto("Kovárna"));
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDetailDto>();

        var recipeResponse = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game!.Id, item!.Id));
        var recipe = await recipeResponse.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();

        await Client.PostAsJsonAsync($"/api/crafting/{recipe!.Id}/buildings",
            new AddCraftingBuildingReqDto(building!.Id));

        var response = await Client.DeleteAsync($"/api/crafting/{recipe.Id}/buildings/{building.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    #region Skill requirements (Task 4.2 — failing until handler wires RequiredSkillIds)

    private async Task<int> SeedSkillAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var skill = new Skill { Name = name };
        db.Skills.Add(skill);
        await db.SaveChangesAsync();
        return skill.Id;
    }

    private async Task AddGameSkillAsync(int gameId, int skillId, int xpCost = 5)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        db.GameSkills.Add(new GameSkill
        {
            GameId = gameId,
            SkillId = skillId,
            XpCost = xpCost,
            LevelRequirement = null
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateRecipe_WithRequiredSkillIds_PersistsLinks()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč kováře", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var s1 = await SeedSkillAsync("Kovářství");
        var s2 = await SeedSkillAsync("Tavba rudy");
        await AddGameSkillAsync(game!.Id, s1);
        await AddGameSkillAsync(game.Id, s2);

        var dto = new CreateCraftingRecipeDto(game.Id, item!.Id, LocationId: null,
            RequiredSkillIds: new[] { s1, s2 });
        var response = await Client.PostAsJsonAsync("/api/crafting", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CraftingRecipeListDto>();
        Assert.NotNull(created);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var linkedSkillIds = await db.CraftingSkillRequirements
            .Where(r => r.CraftingRecipeId == created!.Id)
            .Select(r => r.SkillId)
            .ToListAsync();

        Assert.Equal(2, linkedSkillIds.Count);
        Assert.Contains(s1, linkedSkillIds);
        Assert.Contains(s2, linkedSkillIds);
    }

    [Fact]
    public async Task UpdateRecipe_ReplacesSkillSet()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Amulet", ItemType.Armor));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var s1 = await SeedSkillAsync("Dov1");
        var s2 = await SeedSkillAsync("Dov2");
        var s3 = await SeedSkillAsync("Dov3");
        await AddGameSkillAsync(game!.Id, s1);
        await AddGameSkillAsync(game.Id, s2);
        await AddGameSkillAsync(game.Id, s3);

        var createResp = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item!.Id, LocationId: null,
                RequiredSkillIds: new[] { s1, s2 }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<CraftingRecipeListDto>();

        var updateDto = new UpdateCraftingRecipeDto(item.Id, LocationId: null,
            RequiredSkillIds: new[] { s2, s3 });
        var putResp = await Client.PutAsJsonAsync($"/api/crafting/{created!.Id}", updateDto);

        Assert.True(
            putResp.StatusCode == HttpStatusCode.NoContent ||
            putResp.StatusCode == HttpStatusCode.OK,
            $"Expected 200 or 204, got {(int)putResp.StatusCode}");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var finalIds = await db.CraftingSkillRequirements
            .Where(r => r.CraftingRecipeId == created.Id)
            .Select(r => r.SkillId)
            .ToListAsync();

        Assert.Equal(2, finalIds.Count);
        Assert.Contains(s2, finalIds);
        Assert.Contains(s3, finalIds);
        Assert.DoesNotContain(s1, finalIds);
    }

    [Fact]
    public async Task CreateRecipe_SkillNotInGame_Returns400()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Štít", ItemType.Armor));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        // Skill exists in catalog but is NOT added to this game.
        var orphanSkillId = await SeedSkillAsync("Neregistrovaná dovednost");

        var dto = new CreateCraftingRecipeDto(game!.Id, item!.Id, LocationId: null,
            RequiredSkillIds: new[] { orphanSkillId });
        var response = await Client.PostAsJsonAsync("/api/crafting", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task CreateRecipe_UnknownSkillId_Returns400()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Luk", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var dto = new CreateCraftingRecipeDto(game!.Id, item!.Id, LocationId: null,
            RequiredSkillIds: new[] { 999999 });
        var response = await Client.PostAsJsonAsync("/api/crafting", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion
}
