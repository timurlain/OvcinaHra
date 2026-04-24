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
        var createDto = new CreateLocationDto(
            Name: "Bílý kámen",
            LocationKind: LocationKind.Magical,
            Latitude: 49.75123m,
            Longitude: 17.25456m,
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

        // Update — named args so "Připravit svíčky" lands in SetupNotes, not
        // GamePotential. Issue #129: the positional form had it falling into
        // slot 7 (GamePotential) because Details/null pushed it past SetupNotes.
        var updateDto = new UpdateLocationDto(
            Name: "Bílý kámen v2",
            LocationKind: LocationKind.PointOfInterest,
            Latitude: 49.76m,
            Longitude: 17.26m,
            Description: "Updated description",
            SetupNotes: "Připravit svíčky");
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
            new CreateGameDto(
                Name: "Test Game",
                Edition: 30,
                StartDate: new DateOnly(2026, 5, 1),
                EndDate: new DateOnly(2026, 5, 3)));
        var game = await gameResp.Content.ReadFromJsonAsync<GameDetailDto>();

        var loc1Resp = await client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto(
                Name: "Město A",
                LocationKind: LocationKind.Town,
                Latitude: 49.5m,
                Longitude: 17.1m));
        var loc1 = await loc1Resp.Content.ReadFromJsonAsync<LocationDetailDto>();

        var loc2Resp = await client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto(
                Name: "Město B",
                LocationKind: LocationKind.Town,
                Latitude: 49.6m,
                Longitude: 17.2m));
        var loc2 = await loc2Resp.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Assign both to game
        await client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(GameId: game!.Id, LocationId: loc1!.Id));
        await client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(GameId: game.Id, LocationId: loc2!.Id));

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
            new CreateGameDto(
                Name: "Stash Test",
                Edition: 1,
                StartDate: new DateOnly(2026, 1, 1),
                EndDate: new DateOnly(2026, 1, 2)));
        var game = await gameResp.Content.ReadFromJsonAsync<GameDetailDto>();

        var locResp = await client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto(
                Name: "Stash Loc",
                LocationKind: LocationKind.Dungeon,
                Latitude: 49.5m,
                Longitude: 17.1m));
        var loc = await locResp.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Create 3 catalog stashes and assign — should succeed
        for (int i = 1; i <= 3; i++)
        {
            var stashResp = await client.PostAsJsonAsync("/api/secret-stashes",
                new CreateSecretStashDto(Name: $"Skrýš {i}"));
            stashResp.EnsureSuccessStatusCode();
            var stash = await stashResp.Content.ReadFromJsonAsync<SecretStashDetailDto>();
            var assignResp = await client.PostAsJsonAsync("/api/secret-stashes/game-stash",
                new CreateGameSecretStashDto(GameId: game!.Id, SecretStashId: stash!.Id, LocationId: loc!.Id));
            assignResp.EnsureSuccessStatusCode();
        }

        // 4th — should fail with validation error
        var s4Resp = await client.PostAsJsonAsync("/api/secret-stashes",
            new CreateSecretStashDto(Name: "Skrýš 4"));
        var s4 = await s4Resp.Content.ReadFromJsonAsync<SecretStashDetailDto>();
        var fourthResp = await client.PostAsJsonAsync("/api/secret-stashes/game-stash",
            new CreateGameSecretStashDto(GameId: game!.Id, SecretStashId: s4!.Id, LocationId: loc!.Id));
        Assert.False(fourthResp.IsSuccessStatusCode);
    }
}
