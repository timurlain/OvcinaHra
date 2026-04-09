using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class MonsterEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var monsters = await Client.GetFromJsonAsync<List<MonsterListDto>>("/api/monsters");

        Assert.NotNull(monsters);
        Assert.Empty(monsters);
    }

    [Fact]
    public async Task Create_ValidMonster_ReturnsCreated()
    {
        var dto = new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10);

        var response = await Client.PostAsJsonAsync("/api/monsters", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MonsterDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Kostlivec", created.Name);
        Assert.Equal(3, created.Category);
        Assert.Equal(MonsterType.Undead, created.MonsterType);
        Assert.Equal(5, created.Attack);
        Assert.Equal(3, created.Defense);
        Assert.Equal(10, created.Health);
    }

    [Fact]
    public async Task GetById_ExistingMonster_ReturnsMonster()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var created = await createResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var result = await Client.GetFromJsonAsync<MonsterDetailDto>($"/api/monsters/{created!.Id}");

        Assert.NotNull(result);
        Assert.Equal(created.Id, result.Id);
        Assert.Equal("Kostlivec", result.Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/monsters/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingMonster_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var created = await createResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var updateDto = new UpdateMonsterDto("Kostlivec Veteran", 4, MonsterType.Undead, 8, 5, 20,
            Abilities: "Regenerace", AiBehavior: null, RewardXp: 50, RewardMoney: null, RewardNotes: null);
        var response = await Client.PutAsJsonAsync($"/api/monsters/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<MonsterDetailDto>($"/api/monsters/{created.Id}");
        Assert.Equal("Kostlivec Veteran", updated!.Name);
        Assert.Equal(4, updated.Category);
        Assert.Equal(8, updated.Attack);
        Assert.Equal(50, updated.RewardXp);
    }

    [Fact]
    public async Task Delete_ExistingMonster_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var created = await createResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var response = await Client.DeleteAsync($"/api/monsters/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AddTag_ReturnsOk()
    {
        var createMonsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var monster = await createMonsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var createTagResponse = await Client.PostAsJsonAsync("/api/tags",
            new CreateTagDto("Undead", TagKind.Monster));
        var tag = await createTagResponse.Content.ReadFromJsonAsync<TagDto>();

        var response = await Client.PostAsync($"/api/monsters/{monster!.Id}/tags/{tag!.Id}", null);

        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.Created,
            $"Expected NoContent or Created, got {response.StatusCode}");
    }

    [Fact]
    public async Task RemoveTag_ReturnsNoContent()
    {
        var createMonsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var monster = await createMonsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var createTagResponse = await Client.PostAsJsonAsync("/api/tags",
            new CreateTagDto("Undead", TagKind.Monster));
        var tag = await createTagResponse.Content.ReadFromJsonAsync<TagDto>();

        await Client.PostAsync($"/api/monsters/{monster!.Id}/tags/{tag!.Id}", null);

        var response = await Client.DeleteAsync($"/api/monsters/{monster.Id}/tags/{tag.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AddLoot_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        var dto = new CreateMonsterLootDto(monster!.Id, item!.Id, game!.Id, 1);
        var response = await Client.PostAsJsonAsync("/api/monsters/loot", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RemoveLoot_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var itemResponse = await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon));
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDetailDto>();

        var monsterResponse = await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Kostlivec", 3, MonsterType.Undead, 5, 3, 10));
        var monster = await monsterResponse.Content.ReadFromJsonAsync<MonsterDetailDto>();

        await Client.PostAsJsonAsync("/api/monsters/loot",
            new CreateMonsterLootDto(monster!.Id, item!.Id, game!.Id, 1));

        var response = await Client.DeleteAsync($"/api/monsters/loot/{monster.Id}/{item.Id}/{game.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
