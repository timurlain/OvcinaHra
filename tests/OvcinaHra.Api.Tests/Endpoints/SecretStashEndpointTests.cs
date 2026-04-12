using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class SecretStashEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    // --- Catalog CRUD ---

    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var stashes = await Client.GetFromJsonAsync<List<SecretStashListDto>>("/api/secret-stashes");
        Assert.NotNull(stashes);
        Assert.Empty(stashes);
    }

    [Fact]
    public async Task Create_ValidStash_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu", "Pod kořenem"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SecretStashDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Tajná skrýš u dubu", created.Name);
        Assert.Equal("Pod kořenem", created.Description);
    }

    [Fact]
    public async Task GetById_ExistingStash_ReturnsStash()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu"));
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
        var createResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<SecretStashDetailDto>();

        var response = await Client.PutAsJsonAsync($"/api/secret-stashes/{created!.Id}",
            new UpdateSecretStashDto("Přejmenovaná skrýš", "Nový popis"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingStash_ReturnsNoContent()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu"));
        var created = await createResponse.Content.ReadFromJsonAsync<SecretStashDetailDto>();

        var response = await Client.DeleteAsync($"/api/secret-stashes/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Per-game assignment ---

    [Fact]
    public async Task GetByGame_Empty_ReturnsEmptyList()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var stashes = await Client.GetFromJsonAsync<List<GameSecretStashDto>>($"/api/secret-stashes/by-game/{game!.Id}");

        Assert.NotNull(stashes);
        Assert.Empty(stashes);
    }

    [Fact]
    public async Task AssignToGame_ValidStash_ReturnsCreated()
    {
        var (game, location, stash) = await CreateGameLocationStash();

        var response = await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var assigned = await response.Content.ReadFromJsonAsync<GameSecretStashDto>();
        Assert.NotNull(assigned);
        Assert.Equal(stash.Id, assigned.SecretStashId);
        Assert.Equal(location.Id, assigned.LocationId);
    }

    [Fact]
    public async Task AssignToGame_Duplicate_ReturnsConflict()
    {
        var (game, location, stash) = await CreateGameLocationStash();

        await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));
        var response = await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AssignThreeToSameLocation_AllSucceed()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = (await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>())!;

        for (int i = 1; i <= 3; i++)
        {
            var s = await Client.PostAsJsonAsync("/api/secret-stashes", new CreateSecretStashDto($"Skrýš {i}"));
            var stash = (await s.Content.ReadFromJsonAsync<SecretStashDetailDto>())!;
            var r = await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
                new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }
    }

    [Fact]
    public async Task AssignFourthToSameLocation_Returns422()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = (await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>())!;

        for (int i = 1; i <= 3; i++)
        {
            var s = await Client.PostAsJsonAsync("/api/secret-stashes", new CreateSecretStashDto($"Skrýš {i}"));
            var stash = (await s.Content.ReadFromJsonAsync<SecretStashDetailDto>())!;
            await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
                new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));
        }

        var s4 = await Client.PostAsJsonAsync("/api/secret-stashes", new CreateSecretStashDto("Skrýš 4"));
        var stash4 = (await s4.Content.ReadFromJsonAsync<SecretStashDetailDto>())!;
        var response = await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash4.Id, location.Id));

        Assert.False(response.IsSuccessStatusCode, "Fourth stash at same location should be rejected");
    }

    [Fact]
    public async Task UnassignFromGame_ReturnsNoContent()
    {
        var (game, location, stash) = await CreateGameLocationStash();

        await Client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(game.Id, stash.Id, location.Id));

        var response = await Client.DeleteAsync($"/api/secret-stashes/game-stash/{game.Id}/{stash.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Helper ---

    private async Task<(GameDetailDto Game, LocationDetailDto Location, SecretStashDetailDto Stash)> CreateGameLocationStash()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Hra", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = (await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>())!;

        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Hora", LocationKind.Wilderness, 49.5m, 17.1m));
        var location = (await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>())!;

        var stashResponse = await Client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Tajná skrýš u dubu"));
        var stash = (await stashResponse.Content.ReadFromJsonAsync<SecretStashDetailDto>())!;

        return (game, location, stash);
    }
}
