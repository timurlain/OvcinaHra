using System.Net.Http.Json;
using OvcinaHra.E2E.Fixtures;
using OvcinaHra.E2E.PageObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.E2E;

/// <summary>
/// E2E tests for Game Management flow via API.
/// Browser-based E2E tests require a running WASM client — these test the
/// full API flow that the UI would drive, using the shared Testcontainers DB.
/// </summary>
[Collection("E2E")]
public class GameManagementTests
{
    private readonly E2EFixture _fixture;

    public GameManagementTests(E2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FullGameLifecycle_CreateEditDelete()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;

        // Set auth token
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 1. List — empty
        var games = await client.GetFromJsonAsync<List<GameListDto>>("/api/games");
        Assert.NotNull(games);
        Assert.Empty(games);

        // 2. Create
        var createDto = new CreateGameDto("Balinova pozvánka", 30,
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3));
        var createResponse = await client.PostAsJsonAsync("/api/games", createDto);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GameDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Balinova pozvánka", created.Name);

        // 3. List — one game
        games = await client.GetFromJsonAsync<List<GameListDto>>("/api/games");
        Assert.Single(games!);

        // 4. Get by ID
        var detail = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Equal(30, detail!.Edition);

        // 5. Update
        var updateDto = new UpdateGameDto("Balinova pozvánka v2", 30,
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 4),
            OvcinaHra.Shared.Domain.Enums.GameStatus.Active);
        var updateResponse = await client.PutAsJsonAsync($"/api/games/{created.Id}", updateDto);
        updateResponse.EnsureSuccessStatusCode();

        var updated = await client.GetFromJsonAsync<GameDetailDto>($"/api/games/{created.Id}");
        Assert.Equal("Balinova pozvánka v2", updated!.Name);
        Assert.Equal(OvcinaHra.Shared.Domain.Enums.GameStatus.Active, updated.Status);

        // 6. Delete
        var deleteResponse = await client.DeleteAsync($"/api/games/{created.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        games = await client.GetFromJsonAsync<List<GameListDto>>("/api/games");
        Assert.Empty(games!);
    }

    [Fact]
    public async Task CreateMultipleGames_ListOrderedByStartDateDesc()
    {
        await _fixture.CleanDatabaseAsync();
        var client = _fixture.ApiClient;
        var token = await _fixture.GetDevTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Starší hra", 28, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 3)));
        await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Novější hra", 29, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 3)));
        await client.PostAsJsonAsync("/api/games",
            new CreateGameDto("Nejnovější hra", 30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));

        var games = await client.GetFromJsonAsync<List<GameListDto>>("/api/games");
        Assert.Equal(3, games!.Count);
        Assert.Equal("Nejnovější hra", games[0].Name); // Most recent first
        Assert.Equal("Starší hra", games[2].Name);
    }
}
