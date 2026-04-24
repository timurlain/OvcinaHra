using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Covers the server-side validation added for inline-edit (issue #119).
// PUT /api/items/{id} and PUT /api/items/game-item/{gameId}/{itemId} are now
// hit directly from the grid on every blur, so bad inputs must surface as
// Czech ProblemDetails 400 rather than getting persisted as junk.
public class ItemInlineEditTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra #119", 1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    private async Task<ItemDetailDto> CreateItemAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto(name, ItemType.Potion));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    private async Task AssignItemToGameAsync(int gameId, int itemId)
    {
        var response = await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(gameId, itemId));
        response.EnsureSuccessStatusCode();
    }

    // ----- UpdateGameItem validation -----

    [Fact]
    public async Task UpdateGameItem_NegativePrice_ReturnsBadRequest()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Lektvar zdraví");
        await AssignItemToGameAsync(game.Id, item.Id);

        var response = await Client.PutAsJsonAsync(
            $"/api/items/game-item/{game.Id}/{item.Id}",
            new UpdateGameItemDto(Price: -1, StockCount: null, IsSold: true, SaleCondition: null, IsFindable: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGameItem_NegativeStock_ReturnsBadRequest()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Sušenka");
        await AssignItemToGameAsync(game.Id, item.Id);

        var response = await Client.PutAsJsonAsync(
            $"/api/items/game-item/{game.Id}/{item.Id}",
            new UpdateGameItemDto(Price: 5, StockCount: -3, IsSold: true, SaleCondition: null, IsFindable: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGameItem_SaleConditionTooLong_ReturnsBadRequest()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Svitek");
        await AssignItemToGameAsync(game.Id, item.Id);

        var tooLong = new string('x', 201);
        var response = await Client.PutAsJsonAsync(
            $"/api/items/game-item/{game.Id}/{item.Id}",
            new UpdateGameItemDto(Price: 5, StockCount: 1, IsSold: true, SaleCondition: tooLong, IsFindable: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGameItem_ValidInputs_Returns204AndPersists()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Prsten");
        await AssignItemToGameAsync(game.Id, item.Id);

        var response = await Client.PutAsJsonAsync(
            $"/api/items/game-item/{game.Id}/{item.Id}",
            new UpdateGameItemDto(Price: 42, StockCount: 3, IsSold: true, SaleCondition: "Jen pro členy cechu", IsFindable: false));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Read back via the by-game list and confirm the fields round-tripped.
        var byGame = await Client.GetFromJsonAsync<List<GameItemListDto>>($"/api/items/by-game/{game.Id}");
        var gi = byGame!.Single(x => x.Id == item.Id);
        Assert.Equal(42, gi.Price);
        Assert.Equal(3, gi.StockCount);
        Assert.True(gi.IsSold);
        Assert.Equal("Jen pro členy cechu", gi.SaleCondition);
    }

    [Fact]
    public async Task UpdateGameItem_WhitespaceOnlySaleCondition_PersistsAsNull()
    {
        // Trimming happens server-side — whitespace-only input should land as null,
        // not as a stray " " that breaks the nullable semantics downstream.
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Luk");
        await AssignItemToGameAsync(game.Id, item.Id);

        var response = await Client.PutAsJsonAsync(
            $"/api/items/game-item/{game.Id}/{item.Id}",
            new UpdateGameItemDto(Price: 10, StockCount: null, IsSold: true, SaleCondition: "   ", IsFindable: false));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var byGame = await Client.GetFromJsonAsync<List<GameItemListDto>>($"/api/items/by-game/{game.Id}");
        var gi = byGame!.Single(x => x.Id == item.Id);
        Assert.Null(gi.SaleCondition);
    }

    // ----- UpdateItem (catalog) validation -----

    [Fact]
    public async Task UpdateItem_EffectTooLong_ReturnsBadRequest()
    {
        var item = await CreateItemAsync("Magický kámen");

        var tooLong = new string('e', 501);
        var response = await Client.PutAsJsonAsync(
            $"/api/items/{item.Id}",
            new UpdateItemDto(item.Name, item.ItemType, tooLong, item.PhysicalForm, item.IsCraftable,
                ReqWarrior: 0, ReqArcher: 0, ReqMage: 0, ReqThief: 0, IsUnique: false, IsLimited: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_EffectWithinLimit_Persists()
    {
        var item = await CreateItemAsync("Magický kámen 2");

        var effect = "Léčí 3 HP. Lze použít jen v bitvě.";
        var response = await Client.PutAsJsonAsync(
            $"/api/items/{item.Id}",
            new UpdateItemDto(item.Name, item.ItemType, effect, item.PhysicalForm, item.IsCraftable,
                ReqWarrior: 0, ReqArcher: 0, ReqMage: 0, ReqThief: 0, IsUnique: false, IsLimited: false));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.Equal(effect, detail!.Effect);
    }
}
