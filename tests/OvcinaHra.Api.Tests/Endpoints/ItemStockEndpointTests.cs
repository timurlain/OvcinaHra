using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ItemStockEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<ItemDetailDto> CreateItemAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/items", new CreateItemDto(name, ItemType.Potion));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>())!;
    }

    [Fact]
    public async Task UpdateStock_ValidPayload_PersistsCatalogStockAndAudit()
    {
        var item = await CreateItemAsync("Inventurní lektvar");
        var beforeUpdate = DateTime.UtcNow.AddSeconds(-5);

        var response = await Client.PutAsJsonAsync(
            $"/api/items/{item.Id}/stock",
            new UpdateItemStockDto(StockCount: 7, StockNote: "  Po finále zůstalo v krabici.  "));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.NotNull(detail);
        Assert.Equal(7, detail!.StockCount);
        Assert.Equal("Po finále zůstalo v krabici.", detail.StockNote);
        Assert.NotNull(detail.StockUpdatedUtc);
        Assert.True(detail.StockUpdatedUtc >= beforeUpdate);
        Assert.Equal("Test Organizátor", detail.StockUpdatedBy);

        var list = await Client.GetFromJsonAsync<List<ItemListDto>>("/api/items");
        var row = list!.Single(i => i.Id == item.Id);
        Assert.Equal(7, row.StockCount.GetValueOrDefault());
        Assert.Equal("Po finále zůstalo v krabici.", row.StockNote);
        Assert.Equal(detail.StockUpdatedUtc, row.StockUpdatedUtc);
        Assert.Equal("Test Organizátor", row.StockUpdatedBy);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.WorldChanges.SingleOrDefaultAsync(c =>
            c.EntityType == nameof(Item)
            && c.EntityId == item.Id
            && c.Operation == WorldChangeOperation.Updated);
        Assert.NotNull(audit);
        Assert.Equal("Test Organizátor", audit!.ActorDisplayName);
        Assert.Equal("test-user", audit.ActorUserId);
    }

    [Fact]
    public async Task UpdateStock_LongOrganizerName_TruncatesAuditName()
    {
        var item = await CreateItemAsync("Dlouhé jméno organizátora");
        var longName = new string('A', 250);

        using var client = Factory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync(
            "/api/auth/dev-token",
            new DevTokenRequest("long-user", "long@ovcina.cz", longName));
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);

        var response = await client.PutAsJsonAsync(
            $"/api/items/{item.Id}/stock",
            new UpdateItemStockDto(StockCount: 4));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.Equal(longName[..200], detail!.StockUpdatedBy);
    }

    [Fact]
    public async Task UpdateStock_MissingItem_ReturnsNotFoundBeforeValidation()
    {
        var response = await Client.PutAsJsonAsync(
            "/api/items/999999/stock",
            new UpdateItemStockDto(StockCount: -1, StockNote: new string('x', 501)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStock_NegativeCount_ReturnsProblemDetails()
    {
        var item = await CreateItemAsync("Záporná inventura");

        var response = await Client.PutAsJsonAsync(
            $"/api/items/{item.Id}/stock",
            new UpdateItemStockDto(StockCount: -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Neplatný počet kusů", "záporný");
    }

    [Fact]
    public async Task UpdateStock_TooLongNote_ReturnsProblemDetails()
    {
        var item = await CreateItemAsync("Dlouhá inventura");

        var response = await Client.PutAsJsonAsync(
            $"/api/items/{item.Id}/stock",
            new UpdateItemStockDto(StockCount: 1, StockNote: new string('x', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Poznámka inventury je příliš dlouhá", "500");
    }

    [Fact]
    public async Task UpdateStock_WhitespaceNote_PersistsAsNull()
    {
        var item = await CreateItemAsync("Prázdná poznámka");

        var response = await Client.PutAsJsonAsync(
            $"/api/items/{item.Id}/stock",
            new UpdateItemStockDto(StockCount: 2, StockNote: "   "));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}");
        Assert.Equal(2, detail!.StockCount);
        Assert.Null(detail.StockNote);
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedTitle,
        string expectedDetailFragment)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedTitle, problem!.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Contains(expectedDetailFragment, problem.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
