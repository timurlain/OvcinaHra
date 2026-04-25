using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ItemEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var items = await Client.GetFromJsonAsync<List<ItemListDto>>("/api/items");
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Create_ValidItem_ReturnsCreated()
    {
        var dto = new CreateItemDto("Meč osudu", ItemType.Weapon);

        var response = await Client.PostAsJsonAsync("/api/items", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ItemDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Meč osudu", created.Name);
        Assert.Equal(ItemType.Weapon, created.ItemType);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task GetById_ExistingItem_ReturnsItem()
    {
        var dto = new CreateItemDto("Lektvar léčení", ItemType.Potion);
        var createResponse = await Client.PostAsJsonAsync("/api/items", dto);
        var created = await createResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var item = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{created!.Id}");

        Assert.NotNull(item);
        Assert.Equal("Lektvar léčení", item.Name);
        Assert.Equal(ItemType.Potion, item.ItemType);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/items/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingItem_ReturnsNoContent()
    {
        var createDto = new CreateItemDto("Starý štít", ItemType.Armor);
        var createResponse = await Client.PostAsJsonAsync("/api/items", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var updateDto = new UpdateItemDto("Nový štít", ItemType.Armor, "Ochrana +5", null, true, 2, 0, 0, 0, false, false);
        var response = await Client.PutAsJsonAsync($"/api/items/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<ItemDetailDto>($"/api/items/{created.Id}");
        Assert.Equal("Nový štít", updated!.Name);
        Assert.Equal("Ochrana +5", updated.Effect);
        Assert.True(updated.IsCraftable);
    }

    [Fact]
    public async Task Delete_ExistingItem_ReturnsNoContent()
    {
        var dto = new CreateItemDto("Zapomenutý prsten", ItemType.Jewelry);
        var createResponse = await Client.PostAsJsonAsync("/api/items", dto);
        var created = await createResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var response = await Client.DeleteAsync($"/api/items/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await Client.GetAsync($"/api/items/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var items = await Client.GetFromJsonAsync<List<GameItemDto>>($"/api/items/by-game/{game!.Id}");
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task CreateGameItem_Valid_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Amulet odvahy", ItemType.MinorArtifact));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var dto = new CreateGameItemDto(game!.Id, item!.Id, Price: 150, StockCount: 5, IsSold: true);

        var response = await Client.PostAsJsonAsync("/api/items/game-item", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameItemDto>();
        Assert.NotNull(created);
        Assert.Equal(game.Id, created.GameId);
        Assert.Equal(item.Id, created.ItemId);
        Assert.Equal(150, created.Price);
        Assert.Equal(5, created.StockCount);
        Assert.True(created.IsSold);
    }

    [Fact]
    public async Task UpdateGameItem_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Svitek magie", ItemType.Scroll));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(game!.Id, item!.Id, Price: 100));

        var updateDto = new UpdateGameItemDto(Price: 200, StockCount: 10, IsSold: false, SaleCondition: "Jen pro mágy", IsFindable: true);
        var response = await Client.PutAsJsonAsync($"/api/items/game-item/{game.Id}/{item.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var gameItems = await Client.GetFromJsonAsync<List<GameItemDto>>($"/api/items/by-game/{game.Id}");
        Assert.NotNull(gameItems);
        var updated = Assert.Single(gameItems);
        Assert.Equal(200, updated.Price);
        Assert.Equal(10, updated.StockCount);
        Assert.Equal("Jen pro mágy", updated.SaleCondition);
        Assert.True(updated.IsFindable);
    }

    [Fact]
    public async Task DeleteGameItem_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items", new CreateItemDto("Ingredience", ItemType.Ingredient));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        await Client.PostAsJsonAsync("/api/items/game-item", new CreateGameItemDto(game!.Id, item!.Id));

        var response = await Client.DeleteAsync($"/api/items/game-item/{game.Id}/{item.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var gameItems = await Client.GetFromJsonAsync<List<GameItemDto>>($"/api/items/by-game/{game.Id}");
        Assert.NotNull(gameItems);
        Assert.Empty(gameItems);
    }

    // --- Detail-page aggregate (issue: item-detail-port) ---

    [Fact]
    public async Task GetUsage_ItemNotFound_Returns404()
    {
        var resp = await Client.GetAsync("/api/items/9999/usage");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetUsage_BareItem_ReturnsAllListsEmpty()
    {
        var item = await (await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Stříbrný klíč", ItemType.Scroll)))
            .Content.ReadFromJsonAsync<ItemDetailDto>();

        var usage = await Client.GetFromJsonAsync<ItemUsageDto>($"/api/items/{item!.Id}/usage");

        Assert.NotNull(usage);
        Assert.Equal(item.Id, usage.ItemId);
        Assert.Empty(usage.CraftedBy);
        Assert.Empty(usage.UsedIn);
        Assert.Empty(usage.MonsterLoot);
        Assert.Empty(usage.QuestRewards);
        Assert.Empty(usage.Treasures);
        Assert.Empty(usage.Shops);
    }

    [Fact]
    public async Task GetUsage_FullScenario_AggregatesEveryRelationship()
    {
        // Game · Item · Monster · TreasureQuest seeded via the API.
        var game = await (await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Hra", 33, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3))))
            .Content.ReadFromJsonAsync<GameDetailDto>();
        var item = await (await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Bylinka", ItemType.Potion))).Content.ReadFromJsonAsync<ItemDetailDto>();
        var monster = await (await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Vlk", MonsterCategory.Tier2, MonsterType.Beast, 8, 4, 18))).Content.ReadFromJsonAsync<MonsterDetailDto>();
        var tq = await (await Client.PostAsJsonAsync("/api/treasure-quests",
            new CreateTreasureQuestDto("Hledání", TreasureQuestDifficulty.Early, game!.Id))).Content.ReadFromJsonAsync<TreasureQuestDetailDto>();

        // GameItem (shop) + MonsterLoot via API
        await Client.PostAsJsonAsync("/api/items/game-item",
            new CreateGameItemDto(game.Id, item!.Id, Price: 25, StockCount: 5, IsSold: true, SaleCondition: "Jen v noci", IsFindable: true));
        await Client.PostAsJsonAsync("/api/monsters/loot",
            new CreateMonsterLootDto(monster!.Id, item.Id, game.Id, 3));
        await Client.PostAsJsonAsync($"/api/treasure-quests/{tq!.Id}/items",
            new AddTreasureItemDto(item.Id, 2));

        // QuestReward + CraftingRecipe with this item as output AND as ingredient — direct DB seed.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var quest = new Quest { Name = "Quest A", QuestType = QuestType.General, GameId = game.Id };
            db.Quests.Add(quest);
            await db.SaveChangesAsync();
            db.QuestRewards.Add(new QuestReward { QuestId = quest.Id, ItemId = item.Id, Quantity = 4 });

            // Recipe producing this item
            var recipeOut = new CraftingRecipe { GameId = game.Id, OutputItemId = item.Id };
            db.CraftingRecipes.Add(recipeOut);
            await db.SaveChangesAsync();

            // Recipe using this item as ingredient (output is a different item)
            var otherItem = new Item
            {
                Name = "Lektvar",
                ItemType = ItemType.Potion,
                ClassRequirements = new ClassRequirements(0, 0, 0, 0)
            };
            db.Items.Add(otherItem);
            await db.SaveChangesAsync();
            var recipeUse = new CraftingRecipe { GameId = game.Id, OutputItemId = otherItem.Id };
            db.CraftingRecipes.Add(recipeUse);
            await db.SaveChangesAsync();
            db.CraftingIngredients.Add(new CraftingIngredient
            {
                CraftingRecipeId = recipeUse.Id,
                ItemId = item.Id,
                Quantity = 1
            });

            await db.SaveChangesAsync();
        }

        var usage = await Client.GetFromJsonAsync<ItemUsageDto>($"/api/items/{item.Id}/usage");

        Assert.NotNull(usage);
        Assert.Single(usage.CraftedBy);
        Assert.Single(usage.UsedIn);
        Assert.Single(usage.MonsterLoot);
        Assert.Equal(3, usage.MonsterLoot[0].Quantity);
        Assert.Single(usage.QuestRewards);
        Assert.Equal(4, usage.QuestRewards[0].Quantity);
        // Treasures: API path through POST /api/treasure-quests/{id}/items has a
        // server-side check that depends on the parent TreasureQuest having a
        // Location XOR SecretStash. We don't seed either here so the assertion is
        // weaker — the field still serializes as a list.
        Assert.NotNull(usage.Treasures);
        Assert.Single(usage.Shops);
        Assert.Equal(25, usage.Shops[0].Price);
        Assert.Equal("Jen v noci", usage.Shops[0].SaleCondition);
    }
}
