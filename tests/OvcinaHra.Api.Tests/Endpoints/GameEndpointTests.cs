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
}
