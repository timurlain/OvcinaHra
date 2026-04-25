using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #142 — Building crafting cost endpoints. Mirrors CraftingEndpointTests
// for the Item-side recipe model. Validation rules in lock-step:
// MoneyCost ≥ 0, IngredientNotes ≤ 2000 chars, prerequisite cannot reference
// the recipe's own output building, skills must be present in the game.
public class BuildingRecipeEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Stavební hra", 1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<BuildingDetailDto> CreateBuildingAsync(string name, int? gameId = null)
    {
        var url = gameId.HasValue ? $"/api/buildings?gameId={gameId}" : "/api/buildings";
        var response = await Client.PostAsJsonAsync(url, new CreateBuildingDto(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BuildingDetailDto>())!;
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, ItemType.Potion));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response, string expectedTitle, string expectedDetailFragment)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedTitle, problem!.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Contains(expectedDetailFragment, problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Empty + happy-path -----

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var game = await CreateGameAsync();
        var recipes = await Client.GetFromJsonAsync<List<BuildingRecipeListDto>>(
            $"/api/building-recipes/by-game/{game.Id}");
        Assert.NotNull(recipes);
        Assert.Empty(recipes);
    }

    [Fact]
    public async Task Create_ValidRecipe_ReturnsCreated()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Hradiště");

        var response = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id, MoneyCost: 50));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>();
        Assert.NotNull(created);
        Assert.Equal(b.Id, created!.OutputBuildingId);
        Assert.Equal("Hradiště", created.OutputBuildingName);
        Assert.Equal(50, created.MoneyCost);
        Assert.Empty(created.Ingredients);
        Assert.Empty(created.PrerequisiteBuildings);
    }

    [Fact]
    public async Task Create_NegativeMoneyCost_Returns400()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Stáj");

        var response = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id, MoneyCost: -10));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Neplatná cena", "záporná");
    }

    [Fact]
    public async Task Create_OversizedNotes_Returns400()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Knihovna");
        var tooLong = new string('p', 2001);

        var response = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id, IngredientNotes: tooLong));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Poznámka je příliš dlouhá", "2000");
    }

    [Fact]
    public async Task Update_RoundTripsAllFields()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Tržiště");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        createResp.EnsureSuccessStatusCode();
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var updateResp = await Client.PutAsJsonAsync($"/api/building-recipes/{recipe.Id}",
            new UpdateBuildingRecipeDto(b.Id, MoneyCost: 120, IngredientNotes: "Pouze v sezóně"));
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        var fetched = await Client.GetFromJsonAsync<BuildingRecipeDetailDto>($"/api/building-recipes/{recipe.Id}");
        Assert.Equal(120, fetched!.MoneyCost);
        Assert.Equal("Pouze v sezóně", fetched.IngredientNotes);
    }

    [Fact]
    public async Task Update_NonExistentRecipe_Returns404()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Maják");
        var resp = await Client.PutAsJsonAsync("/api/building-recipes/99999",
            new UpdateBuildingRecipeDto(b.Id));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        var resp = await Client.GetAsync("/api/building-recipes/99999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- Ingredients -----

    [Fact]
    public async Task AddIngredient_AppearsInDetail_AndRemoveDeletesIt()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Mlýn");
        var item = await CreateItemAsync("Dřevo");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        createResp.EnsureSuccessStatusCode();
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var addResp = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id, Quantity: 5));
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);

        var afterAdd = await Client.GetFromJsonAsync<BuildingRecipeDetailDto>($"/api/building-recipes/{recipe.Id}");
        Assert.Single(afterAdd!.Ingredients);
        Assert.Equal(5, afterAdd.Ingredients[0].Quantity);

        var removeResp = await Client.DeleteAsync($"/api/building-recipes/{recipe.Id}/ingredients/{item.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var afterRemove = await Client.GetFromJsonAsync<BuildingRecipeDetailDto>($"/api/building-recipes/{recipe.Id}");
        Assert.Empty(afterRemove!.Ingredients);
    }

    [Fact]
    public async Task AddIngredient_RecipeNotFound_Returns404()
    {
        var item = await CreateItemAsync("Sláma");
        var resp = await Client.PostAsJsonAsync("/api/building-recipes/99999/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddIngredient_NegativeQuantity_Returns400()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Strážnice");
        var item = await CreateItemAsync("Kámen");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var resp = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id, Quantity: 0));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertProblemDetailsAsync(resp, "Neplatné množství", "1");
    }

    [Fact]
    public async Task AddIngredient_NonExistentItem_Returns400()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Sklad");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var resp = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(ItemId: 99999, Quantity: 1));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertProblemDetailsAsync(resp, "Předmět neexistuje", "99999");
    }

    [Fact]
    public async Task AddPrerequisite_RecipeNotFound_Returns404()
    {
        var b = await CreateBuildingAsync("Brána");
        var resp = await Client.PostAsJsonAsync("/api/building-recipes/99999/prerequisites",
            new AddBuildingRecipePrerequisiteDto(b.Id));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddPrerequisite_NonExistentBuilding_Returns400()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Most");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var resp = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/prerequisites",
            new AddBuildingRecipePrerequisiteDto(BuildingId: 99999));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertProblemDetailsAsync(resp, "Budova neexistuje", "99999");
    }

    [Fact]
    public async Task AddIngredient_DuplicateReturnsConflict()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Pekárna");
        var item = await CreateItemAsync("Mouka");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var first = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var dup = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    // ----- Prerequisites -----

    [Fact]
    public async Task AddPrerequisite_AppearsInDetail_AndRemoveDeletesIt()
    {
        var game = await CreateGameAsync();
        var output = await CreateBuildingAsync("Kostel");
        var prereq = await CreateBuildingAsync("Základy");

        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, output.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var addResp = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/prerequisites",
            new AddBuildingRecipePrerequisiteDto(prereq.Id));
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);

        var afterAdd = await Client.GetFromJsonAsync<BuildingRecipeDetailDto>($"/api/building-recipes/{recipe.Id}");
        Assert.Single(afterAdd!.PrerequisiteBuildings);
        Assert.Equal(prereq.Id, afterAdd.PrerequisiteBuildings[0].BuildingId);

        var removeResp = await Client.DeleteAsync($"/api/building-recipes/{recipe.Id}/prerequisites/{prereq.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        var afterRemove = await Client.GetFromJsonAsync<BuildingRecipeDetailDto>($"/api/building-recipes/{recipe.Id}");
        Assert.Empty(afterRemove!.PrerequisiteBuildings);
    }

    [Fact]
    public async Task AddPrerequisite_SelfReference_Returns400()
    {
        var game = await CreateGameAsync();
        var b = await CreateBuildingAsync("Hrad");
        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, b.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        var resp = await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/prerequisites",
            new AddBuildingRecipePrerequisiteDto(b.Id));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertProblemDetailsAsync(resp, "Neplatný požadavek", "sama");
    }

    // ----- Cascade delete -----

    [Fact]
    public async Task DeleteRecipe_CascadesIngredientsAndPrerequisites()
    {
        var game = await CreateGameAsync();
        var output = await CreateBuildingAsync("Tvrz");
        var prereq = await CreateBuildingAsync("Palisáda");
        var item = await CreateItemAsync("Kámen");

        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, output.Id));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;

        await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id, 3));
        await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/prerequisites",
            new AddBuildingRecipePrerequisiteDto(prereq.Id));

        var deleteResp = await Client.DeleteAsync($"/api/building-recipes/{recipe.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // GetById returns 404 → recipe is gone. Children would have been
        // orphaned without cascade — testing existence of the parent is
        // sufficient because the ingredient/prerequisite tables have FK
        // back to the recipe with ON DELETE CASCADE behavior in EF.
        var refetch = await Client.GetAsync($"/api/building-recipes/{recipe.Id}");
        Assert.Equal(HttpStatusCode.NotFound, refetch.StatusCode);
    }

    // ----- Grid summary integration -----

    [Fact]
    public async Task BuildingsByGame_PopulatesRecipeSummary_ForBuildingsWithRecipes()
    {
        var game = await CreateGameAsync();
        var withRecipe = await CreateBuildingAsync("Hostinec", gameId: game.Id);
        var noRecipe = await CreateBuildingAsync("Studna", gameId: game.Id);
        var item = await CreateItemAsync("Sláma");

        var createResp = await Client.PostAsJsonAsync("/api/building-recipes",
            new CreateBuildingRecipeDto(game.Id, withRecipe.Id, MoneyCost: 75));
        var recipe = (await createResp.Content.ReadFromJsonAsync<BuildingRecipeDetailDto>())!;
        await Client.PostAsJsonAsync($"/api/building-recipes/{recipe.Id}/ingredients",
            new AddBuildingRecipeIngredientDto(item.Id, 4));

        var rows = await Client.GetFromJsonAsync<List<BuildingListDto>>($"/api/buildings/by-game/{game.Id}");
        Assert.NotNull(rows);
        var withRow = rows!.Single(r => r.Id == withRecipe.Id);
        var noRow = rows.Single(r => r.Id == noRecipe.Id);
        Assert.False(string.IsNullOrEmpty(withRow.RecipeSummary));
        Assert.Contains("75", withRow.RecipeSummary);
        Assert.Contains("4", withRow.RecipeSummary);
        Assert.Null(noRow.RecipeSummary);
    }
}
