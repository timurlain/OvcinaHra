using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SearchEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        var result = await Client.GetFromJsonAsync<SearchResponseDto>("/api/search?q=");
        Assert.NotNull(result);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task Search_FindsLocationByName()
    {
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Dračí jeskyně", LocationKind.Dungeon, 49.5m, 17.1m));
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Elfí háj", LocationKind.Magical, 49.6m, 17.2m));

        var result = await Client.GetFromJsonAsync<SearchResponseDto>("/api/search?q=Dračí");
        Assert.NotNull(result);
        Assert.Contains(result.Results, r => r.EntityType == "Location" && r.Name == "Dračí jeskyně");
        Assert.DoesNotContain(result.Results, r => r.Name == "Elfí háj");
    }

    [Fact]
    public async Task Search_FindsAcrossEntityTypes()
    {
        // Create a location and a monster both containing "temný"
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Temný hvozd", LocationKind.Wilderness, 49.5m, 17.1m));
        await Client.PostAsJsonAsync("/api/monsters",
            new CreateMonsterDto("Temný strážce", MonsterCategory.Tier3, MonsterType.Undead, 5, 3, 10));

        var result = await Client.GetFromJsonAsync<SearchResponseDto>("/api/search?q=temný");
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 2);
        Assert.Contains(result.Results, r => r.EntityType == "Location");
        Assert.Contains(result.Results, r => r.EntityType == "Monster");
    }

    [Fact]
    public async Task Search_PrefixMatching_Works()
    {
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Kamenný portál", LocationKind.PointOfInterest, 49.5m, 17.1m));

        // "kamen" should match "Kamenný" via prefix :*
        var result = await Client.GetFromJsonAsync<SearchResponseDto>("/api/search?q=kamen");
        Assert.NotNull(result);
        Assert.Contains(result.Results, r => r.Name == "Kamenný portál");
    }

    [Fact]
    public async Task Search_SingleCharacterManualQuery_Works()
    {
        await Client.PostAsJsonAsync("/api/items",
            new CreateItemDto("Meč", ItemType.Weapon, "Ostrá čepel"));

        var result = await Client.GetFromJsonAsync<SearchResponseDto>("/api/search?q=M");

        Assert.NotNull(result);
        Assert.Contains(result.Results, r => r.EntityType == "Item" && r.Name == "Meč");
    }
}
