using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class LocationEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task Create_ValidLocation_ReturnsCreated()
    {
        var dto = new CreateLocationDto("Bílý kámen", LocationKind.Magical, 49.75m, 17.25m,
            Description: "Magical stone in the meadow");

        var response = await Client.PostAsJsonAsync("/api/locations", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<LocationDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Bílý kámen", created.Name);
        Assert.Equal(49.75m, created.Latitude);
        Assert.Equal(17.25m, created.Longitude);
        Assert.Equal(LocationKind.Magical, created.LocationKind);
    }

    [Fact]
    public async Task GetAll_ReturnsOrderedByName()
    {
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Zelená hora", LocationKind.Wilderness, 49.5m, 17.1m));
        await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Arnor", LocationKind.Town, 49.6m, 17.2m));

        var locations = await Client.GetFromJsonAsync<List<LocationListDto>>("/api/locations");
        Assert.NotNull(locations);
        Assert.Equal(2, locations.Count);
        Assert.Equal("Arnor", locations[0].Name);
        Assert.Equal("Zelená hora", locations[1].Name);
    }

    [Fact]
    public async Task AssignToGame_AndGetByGame_Works()
    {
        // Create a game
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Test Edition", 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        // Create locations
        var loc1Response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Loc A", LocationKind.Village, 49.5m, 17.1m));
        var loc1 = await loc1Response.Content.ReadFromJsonAsync<LocationDetailDto>();

        var loc2Response = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Loc B", LocationKind.Town, 49.6m, 17.2m));
        var loc2 = await loc2Response.Content.ReadFromJsonAsync<LocationDetailDto>();

        // Assign loc1 to game
        var assignResponse = await Client.PostAsJsonAsync("/api/locations/by-game",
            new GameLocationDto(game!.Id, loc1!.Id));
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        // Get locations for game — only loc1
        var gameLocations = await Client.GetFromJsonAsync<List<LocationListDto>>(
            $"/api/locations/by-game/{game.Id}");
        Assert.NotNull(gameLocations);
        Assert.Single(gameLocations);
        Assert.Equal("Loc A", gameLocations[0].Name);
    }

    [Fact]
    public async Task AssignToGame_Duplicate_ReturnsConflict()
    {
        var gameResponse = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Dup Test", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)));
        var game = await gameResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var locResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Dup Loc", LocationKind.Village, 49.5m, 17.1m));
        var loc = await locResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        await Client.PostAsJsonAsync("/api/locations/by-game", new GameLocationDto(game!.Id, loc!.Id));
        var dupResponse = await Client.PostAsJsonAsync("/api/locations/by-game", new GameLocationDto(game.Id, loc.Id));

        Assert.Equal(HttpStatusCode.Conflict, dupResponse.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesCoordinates()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/locations",
            new CreateLocationDto("Movable", LocationKind.PointOfInterest, 49.0m, 17.0m));
        var created = await createResponse.Content.ReadFromJsonAsync<LocationDetailDto>();

        var updateDto = new UpdateLocationDto("Movable", LocationKind.PointOfInterest, 50.0m, 18.0m, null, null, null);
        var response = await Client.PutAsJsonAsync($"/api/locations/{created!.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<LocationDetailDto>($"/api/locations/{created.Id}");
        Assert.Equal(50.0m, updated!.Latitude);
        Assert.Equal(18.0m, updated.Longitude);
    }
}
