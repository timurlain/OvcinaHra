using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #160 sub-task C: POST /api/treasure-planning/refill-pool/{gameId}.
// Sums every IsFindable GameItem.StockCount against TreasureItem (pool +
// treasure-quest), QuestReward, and PersonalQuestItemReward allocations,
// then appends the unallocated remainder to the pool. MonsterLoot is
// intentionally ignored (situational drop, not a guaranteed allocation).
public class TreasurePlanningRefillPoolTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Refill Test", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
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

    private async Task AssignItemToGameAsync(int gameId, int itemId, int? stock, bool findable)
    {
        var response = await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(gameId, itemId, StockCount: stock, IsFindable: findable));
        response.EnsureSuccessStatusCode();
    }

    private async Task<TreasurePoolItemDto> AddPoolItemAsync(int gameId, int itemId, int count = 1)
    {
        var response = await Client.PostAsJsonAsync("/api/treasure-planning/pool",
            new CreateTreasurePoolItemDto(itemId, gameId, count));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TreasurePoolItemDto>())!;
    }

    private async Task<RefillPoolResponse> RefillAsync(int gameId)
    {
        var response = await Client.PostAsync($"/api/treasure-planning/refill-pool/{gameId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RefillPoolResponse>())!;
    }

    private async Task<List<TreasurePoolItemDto>> GetPoolAsync(int gameId)
    {
        var pool = await Client.GetFromJsonAsync<List<TreasurePoolItemDto>>(
            $"/api/treasure-planning/pool/{gameId}");
        return pool!;
    }

    [Fact]
    public async Task Refill_NoAllocations_AppendsFullStockToPool()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Lektvar zdraví");
        await AssignItemToGameAsync(game.Id, item.Id, stock: 10, findable: true);

        var result = await RefillAsync(game.Id);

        Assert.Equal(10, result.ItemsAdded);
        Assert.Single(result.Added);
        Assert.Empty(result.OverAllocated);

        var pool = await GetPoolAsync(game.Id);
        var row = Assert.Single(pool, p => p.ItemId == item.Id);
        Assert.Equal(10, row.Count);
    }

    [Fact]
    public async Task Refill_WithExistingPoolStack_TopsUpRemainder()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Šíp");
        await AssignItemToGameAsync(game.Id, item.Id, stock: 10, findable: true);
        await AddPoolItemAsync(game.Id, item.Id, count: 3);

        var result = await RefillAsync(game.Id);

        // 10 stock − 3 already in pool = 7 added.
        Assert.Equal(7, result.ItemsAdded);
        Assert.Empty(result.OverAllocated);

        // Pool should now hold a single stacked row at Count=10.
        var pool = await GetPoolAsync(game.Id);
        var row = Assert.Single(pool, p => p.ItemId == item.Id);
        Assert.Equal(10, row.Count);
    }

    [Fact]
    public async Task Refill_CalledTwice_SecondCallIsNoop()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Pochodeň");
        await AssignItemToGameAsync(game.Id, item.Id, stock: 5, findable: true);

        var first = await RefillAsync(game.Id);
        var second = await RefillAsync(game.Id);

        Assert.Equal(5, first.ItemsAdded);
        Assert.Equal(0, second.ItemsAdded);
        Assert.Empty(second.Added);
        Assert.Empty(second.OverAllocated);

        var pool = await GetPoolAsync(game.Id);
        var row = Assert.Single(pool, p => p.ItemId == item.Id);
        Assert.Equal(5, row.Count);
    }

    [Fact]
    public async Task Refill_OverAllocated_SkipsItemAndReportsExcess()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Vzácný amulet");
        await AssignItemToGameAsync(game.Id, item.Id, stock: 5, findable: true);
        // Forge an over-allocation directly in the DB. The public
        // /api/treasure-planning/pool POST validates count <= stock so we
        // can't reach this state through the API by design — but production
        // can drift here via QuestReward / PersonalQuestItemReward edits
        // after the pool is populated. Bypass the guard with a direct insert
        // so we exercise the endpoint's "skip + report" branch.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            db.TreasureItems.Add(new TreasureItem
            {
                GameId = game.Id,
                ItemId = item.Id,
                Count = 7,
                TreasureQuestId = null,
            });
            await db.SaveChangesAsync();
        }

        var result = await RefillAsync(game.Id);

        Assert.Equal(0, result.ItemsAdded);
        Assert.Empty(result.Added);
        var over = Assert.Single(result.OverAllocated);
        Assert.Equal(item.Id, over.ItemId);
        Assert.Equal(5, over.StockCount);
        Assert.Equal(7, over.Allocated);
        Assert.Equal(2, over.Excess);

        // Pool unchanged — still the single 7-stack.
        var pool = await GetPoolAsync(game.Id);
        var row = Assert.Single(pool, p => p.ItemId == item.Id);
        Assert.Equal(7, row.Count);
    }

    [Fact]
    public async Task Refill_NonFindableItem_IsIgnored()
    {
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Soukromý relikt");
        await AssignItemToGameAsync(game.Id, item.Id, stock: 4, findable: false);

        var result = await RefillAsync(game.Id);

        Assert.Equal(0, result.ItemsAdded);
        Assert.Empty(result.Added);
        Assert.Empty(result.OverAllocated);

        var pool = await GetPoolAsync(game.Id);
        Assert.DoesNotContain(pool, p => p.ItemId == item.Id);
    }

    [Fact]
    public async Task Refill_NullStock_IsIgnoredAsUnlimited()
    {
        // Null StockCount means "unlimited" elsewhere in the app
        // (GetUnlimitedItems filters on StockCount == null). Refill has
        // nothing concrete to allocate for an unlimited item — no upper
        // bound to top up against — so it skips them entirely.
        var game = await CreateGameAsync();
        var item = await CreateItemAsync("Bez počtu");
        await AssignItemToGameAsync(game.Id, item.Id, stock: null, findable: true);

        var result = await RefillAsync(game.Id);

        Assert.Equal(0, result.ItemsAdded);
        Assert.Empty(result.Added);
        Assert.Empty(result.OverAllocated);
    }
}
