using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class GameEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task GetAll_Empty_ReturnsEmptyList()
    {
        var games = await Client.GetFromJsonAsync<List<GameListDto>>("/api/games");
        Assert.NotNull(games);
        Assert.Empty(games);
    }

    [Fact]
    public async Task Create_ValidGame_ReturnsCreated()
    {
        var dto = new CreateGameDto("Balinova pozvánka", 30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));

        var response = await Client.PostAsJsonAsync("/api/games", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GameDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Balinova pozvánka", created.Name);
        Assert.Equal(30, created.Edition);
        Assert.Equal(GameStatus.Draft, created.Status);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task GetById_ExistingGame_ReturnsGame()
    {
        var dto = new CreateGameDto("Test Game", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var createResponse = await Client.PostAsJsonAsync("/api/games", dto);
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var game = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created!.Id}");

        Assert.NotNull(game);
        Assert.Equal("Test Game", game.Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/games/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingGame_ReturnsNoContent()
    {
        var createDto = new CreateGameDto("Original", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var createResponse = await Client.PostAsJsonAsync("/api/games", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var updateDto = new UpdateGameDto("Updated", 2, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), GameStatus.Active);
        var response = await Client.PutAsJsonAsync($"/api/games/{created!.Id}", updateDto);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Equal("Updated", updated!.Name);
        Assert.Equal(GameStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Delete_ExistingGame_IsDisabled()
    {
        var dto = new CreateGameDto("Protected", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var createResponse = await Client.PostAsJsonAsync("/api/games", dto);
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();

        var response = await Client.DeleteAsync($"/api/games/{created!.Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        // Game still exists
        var getResponse = await Client.GetAsync($"/api/games/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    // ----- Bounding box round-trip + validation (issue #2) -----

    [Fact]
    public async Task Update_BoundingBox_RoundTripsThroughGet()
    {
        var created = await CreateGameAsync("Bbox round-trip");

        var updateDto = new UpdateGameDto(
            "Bbox round-trip", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 49.5m, BoundingBoxSwLng: 17.1m,
            BoundingBoxNeLat: 49.7m, BoundingBoxNeLng: 17.4m);
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Equal(49.5m, fetched!.BoundingBoxSwLat);
        Assert.Equal(17.1m, fetched.BoundingBoxSwLng);
        Assert.Equal(49.7m, fetched.BoundingBoxNeLat);
        Assert.Equal(17.4m, fetched.BoundingBoxNeLng);
    }

    [Fact]
    public async Task Update_ClearingBoundingBox_PersistsAllNulls()
    {
        var created = await CreateGameAsync("Bbox clear");
        // Seed a bbox first
        await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Bbox clear", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 49.5m, BoundingBoxSwLng: 17.1m,
            BoundingBoxNeLat: 49.7m, BoundingBoxNeLng: 17.4m));

        // Now clear it (all four null)
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Bbox clear", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Null(fetched!.BoundingBoxSwLat);
        Assert.Null(fetched.BoundingBoxSwLng);
        Assert.Null(fetched.BoundingBoxNeLat);
        Assert.Null(fetched.BoundingBoxNeLng);
    }

    [Fact]
    public async Task Update_PartialBoundingBox_ReturnsValidationProblem()
    {
        var created = await CreateGameAsync("Partial bbox");
        // Only 2 of the 4 corners — must be rejected as 400.
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Partial bbox", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 49.5m, BoundingBoxSwLng: 17.1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_InvertedBoundingBox_ReturnsValidationProblem()
    {
        var created = await CreateGameAsync("Inverted bbox");
        // SW.lat > NE.lat — invalid corners.
        var response = await Client.PutAsJsonAsync($"/api/games/{created.Id}", new UpdateGameDto(
            "Inverted bbox", 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), GameStatus.Draft,
            BoundingBoxSwLat: 50.0m, BoundingBoxSwLng: 17.1m,
            BoundingBoxNeLat: 49.5m, BoundingBoxNeLng: 17.4m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<GameDetailDto> CreateGameAsync(string name)
    {
        var dto = new CreateGameDto(name, 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        var response = await Client.PostAsJsonAsync("/api/games", dto);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }
}
