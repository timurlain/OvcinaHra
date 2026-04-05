using System.Net.Http.Json;
using OvcinaHra.E2E.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.E2E;

[Collection("E2E")]
public class LocationManagementTests
{
    private readonly E2EFixture _fixture;

    public LocationManagementTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FullLocationLifecycle_CreateEditDelete()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create
        var createDto = new CreateLocationDto("Bílý kámen", LocationKind.Magical, 49.75123m, 17.25456m,
            Description: "Magical stone in the meadow");
        var createResponse = await client.PostAsJsonAsync("/api/locations", createDto);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<LocationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Bílý kámen", created.Name);
        Assert.Equal(49.75123m, created.Latitude);

        // List
        var locations = await client.GetFromJsonAsync<List<LocationListDto>>("/api/locations");
        Assert.Single(locations!);

        // Update
        var updateDto = new UpdateLocationDto("Bílý kámen v2", LocationKind.PointOfInterest,
            49.76m, 17.26m, "Updated description", null, "Připravit svíčky");
        var updateResponse = await client.PutAsJsonAsync($"/api/locations/{created.Id}", updateDto);
        updateResponse.EnsureSuccessStatusCode();

        var updated = await client.GetFromJsonAsync<LocationDetailDto>($"/api/locations/{created.Id}");
        Assert.Equal("Bílý kámen v2", updated!.Name);
        Assert.Equal("Připravit svíčky", updated.SetupNotes);

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/locations/{created.Id}");
        deleteResponse.EnsureSuccessStatusCode();
        locations = await client.GetFromJsonAsync<List<LocationListDto>>("/api/locations");
        Assert.Empty(locations!);
    }

    [Fact]
    public async Task GameLocationAssignment_FullFlow()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create game and locations
        var gameResp = await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Game", 30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResp.Content.ReadFromJsonAsync<GameDetailDto>();

        var loc1Resp = await client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Město A", LocationKind.Town, 49.5m, 17.1m));
        var loc1 = await loc1Resp.Content.ReadFromJsonAsync<LocationDetailDto>();

        var loc2Resp = await client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Město B", LocationKind.Town, 49.6m, 17.2m));
        var loc2 = await loc2Resp.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Assign both to game
        await client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game!.Id, loc1!.Id));
        await client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game.Id, loc2!.Id));

        // Get locations for game
        var gameLocations = await client.GetFromJsonAsync<List<LocationListDto>>(
            $"/api/locations/by-game/{game.Id}");
        Assert.Equal(2, gameLocations!.Count);

        // Remove one
        var removeResp = await client.DeleteAsync(
            $"/api/locations/by-game/{game.Id}/{loc1.Id}");
        removeResp.EnsureSuccessStatusCode();

        gameLocations = await client.GetFromJsonAsync<List<LocationListDto>>(
            $"/api/locations/by-game/{game.Id}");
        Assert.NotNull(gameLocations);
        Assert.Single(gameLocations);
        Assert.Equal("Město B", gameLocations[0].Name);
    }

    [Fact]
    public async Task SecretStash_Max3PerLocationPerGame()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var gameResp = await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Stash Test", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)));
        var game = await gameResp.Content.ReadFromJsonAsync<GameDetailDto>();

        var locResp = await client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Stash Loc", LocationKind.Dungeon, 49.5m, 17.1m));
        var loc = await locResp.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Create 3 — should succeed
        for (int i = 1; i <= 3; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/secret-stashes",
                new CreateSecretStashDto($"Skrýš {i}", loc!.Id, game!.Id));
            resp.EnsureSuccessStatusCode();
        }

        // 4th — should fail with validation error
        var fourthResp = await client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto("Skrýš 4", loc!.Id, game!.Id));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, fourthResp.StatusCode);
    }
}
