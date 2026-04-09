using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SecretStashEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var stashes = await Client.GetFromJsonAsync<List<SecretStashListDto>>($"/api/secret-stashes/by-game/{game!.Id}");

        Assert.NotNull(stashes);
        Assert.Empty(stashes);
    }

    [Fact]
    public async Task Create_ValidStash_ReturnsCreated()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var dto = new CreateSecretStashDto("Tajná skrýš u dubu", location!.Id, game!.Id);

        var response = await Client.PostAsJsonAsync("/api/secret-stashes", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SecretStashDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Tajná skrýš u dubu", created.Name);
        Assert.Equal(location.Id, created.LocationId);
        Assert.Equal(game.Id, created.GameId);
    }

    [Fact]
    public async Task GetById_ExistingStash_ReturnsStash()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu", location!.Id, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<SecretStashDetailDto>();

        var response = await Client.GetAsync($"/api/secret-stashes/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stash = await response.Content.ReadFromJsonAsync<SecretStashDetailDto>();
        Assert.NotNull(stash);
        Assert.Equal(created.Id, stash.Id);
        Assert.Equal("Tajná skrýš u dubu", stash.Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/secret-stashes/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingStash_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu", location!.Id, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<SecretStashDetailDto>();

        var updateDto = new UpdateSecretStashDto("Přejmenovaná skrýš", "Nový popis");

        var response = await Client.PutAsJsonAsync($"/api/secret-stashes/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingStash_ReturnsNoContent()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var createResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu", location!.Id, game!.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<SecretStashDetailDto>();

        var response = await Client.DeleteAsync($"/api/secret-stashes/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Create_ThreeStashesAtSameLocation_AllSucceed()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var r1 = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Skrýš 1", location!.Id, game!.Id));
        var r2 = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Skrýš 2", location.Id, game.Id));
        var r3 = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Skrýš 3", location.Id, game.Id));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r3.StatusCode);
    }

    [Fact]
    public async Task Create_FourthAtSameLocation_Returns422()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locationResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = await locationResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        await Client.PostAsJsonAsync("/api/secret-stashes", new CreateSecretStashDto("Skrýš 1", location!.Id, game!.Id));
        await Client.PostAsJsonAsync("/api/secret-stashes", new CreateSecretStashDto("Skrýš 2", location.Id, game.Id));
        await Client.PostAsJsonAsync("/api/secret-stashes", new CreateSecretStashDto("Skrýš 3", location.Id, game.Id));

        var response = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Skrýš 4", location.Id, game.Id));

        Assert.False(response.IsSuccessStatusCode, "Fourth stash at same location should be rejected");
    }
}
