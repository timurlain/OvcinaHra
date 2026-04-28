using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

/// <summary>
/// Phase 1 of the Recipes port (issue #218/#324): nullable GameId +
/// TemplateRecipeId self-FK + Name. Tests cover /api/recipes list/detail,
/// create/update/delete, output item type-derived categories, template fork
/// via /from-template/{id}, and Smazat-blocking when forks exist.
/// </summary>
public class RecipePhase1Tests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<ItemDetailDto> CreateItemAsync(
        string name = "Lektvar",
        ItemType itemType = ItemType.Potion,
        bool isCraftable = true)
    {
        var r = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, itemType, IsCraftable: isCraftable));
        return (await r.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private async Task<GameDetailDto> CreateGameAsync()
    {
        var r = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        return (await r.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    [Fact]
    public async Task Create_CatalogTemplate_DerivesCategory_AndKeepsGameIdNull()
    {
        var item = await CreateItemAsync(itemType: ItemType.Potion);
        var dto = new CreateRecipeDto(
            Name: "Hojivý lektvar",
            OutputItemId: item.Id,
            GameId: null);

        var response = await Client.PostAsJsonAsync("/api/recipes", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<RecipeDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Hojivý lektvar", created.Name);
        Assert.Equal("Hojivý lektvar", created.Title);
        Assert.Equal(ItemType.Potion, created.Category);
        Assert.Null(created.GameId);
        Assert.Null(created.TemplateRecipeId);
        Assert.Empty(created.Ingredients);
        Assert.Empty(created.Buildings);
        Assert.Empty(created.Skills);
        Assert.Equal(0, created.ForksCount);
    }

    [Fact]
    public async Task Create_NullName_FallsBackToOutputItemName_OnTitle()
    {
        // Title is convenience for the formula card heading; computed when
        // Name is unset. This guards against a regression where Title is
        // string.Empty after a "" Name.
        var item = await CreateItemAsync("Klíč");
        var response = await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto(Name: null, OutputItemId: item.Id));
        var created = (await response.Content.ReadFromJsonAsync<RecipeDetailDto>())!;
        Assert.Null(created.Name);
        Assert.Equal("Klíč", created.Title);
    }

    [Fact]
    public async Task GetCatalog_ReturnsAllRecipeRows()
    {
        var item = await CreateItemAsync("Šíp");
        var game = await CreateGameAsync();

        // Catalog template
        await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Lovecký šíp", item.Id));

        // Per-game record (NOT a fork — od nuly with explicit GameId)
        await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Šíp pro hru", item.Id, GameId: game.Id));

        var catalog = await Client.GetFromJsonAsync<List<RecipeListDto>>("/api/recipes");
        Assert.NotNull(catalog);
        Assert.Contains(catalog, r => r.Name == "Lovecký šíp");
        Assert.Contains(catalog, r => r.Name == "Šíp pro hru" && r.GameId == game.Id);
    }

    [Fact]
    public async Task GetByGame_ReturnsOnlyForks_AndOdNuly()
    {
        var item = await CreateItemAsync("Šíp");
        var game = await CreateGameAsync();

        await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Lovecký šíp", item.Id));  // catalog
        await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Šíp od nuly", item.Id, GameId: game.Id));  // od nuly

        var perGame = await Client.GetFromJsonAsync<List<RecipeListDto>>($"/api/games/{game.Id}/recipes");
        Assert.NotNull(perGame);
        Assert.Single(perGame);
        Assert.Equal("Šíp od nuly", perGame[0].Name);
        Assert.Equal(game.Id, perGame[0].GameId);
        Assert.Null(perGame[0].TemplateRecipeId);
    }

    [Fact]
    public async Task Update_DerivesCategoryFromNewOutputItem_AndChangesName()
    {
        var item = await CreateItemAsync();
        var artifact = await CreateItemAsync("Amulet", ItemType.Artifact);
        var create = await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Old Name", item.Id));
        var created = (await create.Content.ReadFromJsonAsync<RecipeDetailDto>())!;

        var updateDto = new UpdateRecipeDto(
            Name: "New Name",
            OutputItemId: artifact.Id);
        var response = await Client.PutAsJsonAsync($"/api/recipes/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await Client.GetFromJsonAsync<RecipeDetailDto>($"/api/recipes/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("New Name", fetched.Name);
        Assert.Equal(ItemType.Artifact, fetched.Category);
    }

    [Fact]
    public async Task CopyFromTemplate_DeepCopiesIngredientsAndBuildings()
    {
        var item = await CreateItemAsync();
        var bricks = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Cihly", ItemType.Miscellaneous));
        var brickItem = (await bricks.Content.ReadFromJsonAsync<ItemDetailDto>())!;
        var building = await Client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Kovárna"));
        var bId = (await building.Content.ReadFromJsonAsync<BuildingDetailDto>())!.Id;

        var template = await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Šablona", item.Id));
        var templateRecipe = (await template.Content.ReadFromJsonAsync<RecipeDetailDto>())!;

        // Wire ingredients + a building requirement onto the template via the
        // legacy /api/crafting child routes (the new /api/recipes surface
        // doesn't duplicate those — issue body says reuse).
        await Client.PostAsJsonAsync($"/api/crafting/{templateRecipe.Id}/ingredients",
            new AddCraftingIngredientDto(brickItem.Id, 5));
        await Client.PostAsJsonAsync($"/api/crafting/{templateRecipe.Id}/buildings",
            new AddCraftingBuildingReqDto(bId));

        var game = await CreateGameAsync();
        var copyResponse = await Client.PostAsync(
            $"/api/games/{game.Id}/recipes/from-template/{templateRecipe.Id}", content: null);
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);

        var fork = (await copyResponse.Content.ReadFromJsonAsync<RecipeDetailDto>())!;
        Assert.Equal(game.Id, fork.GameId);
        Assert.Equal(templateRecipe.Id, fork.TemplateRecipeId);
        Assert.Equal(ItemType.Potion, fork.Category);
        // Children deep-copied:
        Assert.Single(fork.Ingredients);
        Assert.Equal(brickItem.Id, fork.Ingredients[0].ItemId);
        Assert.Equal(5, fork.Ingredients[0].Quantity);
        Assert.Single(fork.Buildings);
        Assert.Equal(bId, fork.Buildings[0].BuildingId);
    }

    [Fact]
    public async Task Delete_CatalogWithForks_ReturnsConflict()
    {
        var item = await CreateItemAsync();
        var template = await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Šablona", item.Id));
        var templateRecipe = (await template.Content.ReadFromJsonAsync<RecipeDetailDto>())!;

        var game = await CreateGameAsync();
        await Client.PostAsync($"/api/games/{game.Id}/recipes/from-template/{templateRecipe.Id}", content: null);

        // Catalog-with-forks DELETE blocked with 409 + ProblemDetails.
        var deleteResponse = await Client.DeleteAsync($"/api/recipes/{templateRecipe.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_CatalogWithoutForks_Succeeds()
    {
        var item = await CreateItemAsync();
        var create = await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("Lone", item.Id));
        var created = (await create.Content.ReadFromJsonAsync<RecipeDetailDto>())!;

        var deleteResponse = await Client.DeleteAsync($"/api/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Create_NonCraftableOutput_ReturnsBadRequest()
    {
        var item = await CreateItemAsync("Dekorace", ItemType.Miscellaneous, isCraftable: false);

        var response = await Client.PostAsJsonAsync("/api/recipes",
            new CreateRecipeDto("R", item.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
