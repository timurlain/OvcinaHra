using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Covers the free-text Note / IngredientNotes fields added in #120 / #121.
// Both are capped at 2000 chars server-side (DxMemo MaxLength is unreliable
// in DevExpress 25.2.5 — server enforces). Whitespace-only input is trimmed
// to null on persist so nullable semantics hold downstream.
public class NotesFieldTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Notes Hra", 1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name, string? note = null)
    {
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, ItemType.Potion, Note: note));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, string expectedTitle, string expectedDetailFragment)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedTitle, problem!.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Contains(expectedDetailFragment, problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Item.Note (issue #120) -----

    [Fact]
    public async Task CreateItem_WithNote_Persists()
    {
        var item = await CreateItemAsync("Lektvar života", note: "Křehký, vejde se 1 do batohu.");

        // Read back through the detail endpoint to confirm the note round-tripped.
        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.Equal("Křehký, vejde se 1 do batohu.", detail!.Note);
    }

    [Fact]
    public async Task UpdateItem_ClearNote_PersistsNull()
    {
        var item = await CreateItemAsync("Náramek", note: "Magicky aktivní za úplňku.");

        // PUT with a null Note should clear the field.
        var response = await Client.PutAsJsonAsync($"/api/items/{item.Id}",
            new UpdateItemDto(item.Name, item.ItemType, item.Effect, item.PhysicalForm, item.IsCraftable,
                ReqWarrior: 0, ReqArcher: 0, ReqMage: 0, ReqThief: 0,
                IsUnique: false, IsLimited: false,
                Note: null));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.Null(detail!.Note);
    }

    [Fact]
    public async Task UpdateItem_WhitespaceOnlyNote_PersistsNull()
    {
        // Trimming happens server-side — whitespace-only input lands as null,
        // matching the semantics for SaleCondition / Effect.
        var item = await CreateItemAsync("Klíč", note: "Původní poznámka.");

        var response = await Client.PutAsJsonAsync($"/api/items/{item.Id}",
            new UpdateItemDto(item.Name, item.ItemType, item.Effect, item.PhysicalForm, item.IsCraftable,
                ReqWarrior: 0, ReqArcher: 0, ReqMage: 0, ReqThief: 0,
                IsUnique: false, IsLimited: false,
                Note: "   "));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.Null(detail!.Note);
    }

    [Fact]
    public async Task UpdateItem_OversizedNote_Returns400()
    {
        var item = await CreateItemAsync("Drahokam");

        var tooLong = new string('n', 2001);
        var response = await Client.PutAsJsonAsync($"/api/items/{item.Id}",
            new UpdateItemDto(item.Name, item.ItemType, item.Effect, item.PhysicalForm, item.IsCraftable,
                ReqWarrior: 0, ReqArcher: 0, ReqMage: 0, ReqThief: 0,
                IsUnique: false, IsLimited: false,
                Note: tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Poznámka je příliš dlouhá", "2000");
    }

    [Fact]
    public async Task CreateItem_OversizedNote_Returns400()
    {
        var tooLong = new string('n', 2001);
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Příliš upovídaný předmět", ItemType.Potion, Note: tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Poznámka je příliš dlouhá", "2000");
    }

    // ----- CraftingRecipe.IngredientNotes (issue #121) -----

    [Fact]
    public async Task CreateRecipe_WithIngredientNotes_Persists()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Bylinný odvar");

        var notes = "Byliny — 3× stejný druh.";
        var response = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item.Id, IngredientNotes: notes));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var recipe = await response.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>();
        Assert.Equal(notes, recipe!.IngredientNotes);

        // Re-read via GET to confirm it survived the round-trip.
        var fetched = await Client.GetFromJsonAsync<CraftingRecipeDetailDto>($"/api/crafting/{recipe.Id}");
        Assert.Equal(notes, fetched!.IngredientNotes);
    }

    [Fact]
    public async Task UpdateRecipe_WithIngredientNotes_Persists()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Lektvar štěstí");

        var createResp = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item.Id));
        createResp.EnsureSuccessStatusCode();
        var recipe = (await createResp.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>())!;
        Assert.Null(recipe.IngredientNotes);

        var updateResp = await Client.PutAsJsonAsync($"/api/crafting/{recipe.Id}",
            new UpdateCraftingRecipeDto(item.Id, IngredientNotes: "Vyžaduje 1× speciální složku."));
        Assert.Equal(HttpStatusCode.NoContent, updateResp.StatusCode);

        var updated = await Client.GetFromJsonAsync<CraftingRecipeDetailDto>($"/api/crafting/{recipe.Id}");
        Assert.Equal("Vyžaduje 1× speciální složku.", updated!.IngredientNotes);
    }

    [Fact]
    public async Task UpdateRecipe_OversizedNotes_Returns400()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Megalektvar");

        var createResp = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item.Id));
        createResp.EnsureSuccessStatusCode();
        var recipe = (await createResp.Content.ReadFromJsonAsync<CraftingRecipeDetailDto>())!;

        var tooLong = new string('p', 2001);
        var response = await Client.PutAsJsonAsync($"/api/crafting/{recipe.Id}",
            new UpdateCraftingRecipeDto(item.Id, IngredientNotes: tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Poznámka je příliš dlouhá", "2000");
    }

    [Fact]
    public async Task CreateRecipe_OversizedNotes_Returns400()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Magická nádoba");

        var tooLong = new string('p', 2001);
        var response = await Client.PostAsJsonAsync("/api/crafting",
            new CreateCraftingRecipeDto(game.Id, item.Id, IngredientNotes: tooLong));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Poznámka je příliš dlouhá", "2000");
    }
}
