using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

// Issue #3 — POST/DELETE /api/games/{id}/link contract.
// Codifies the round-trip, UNIQUE INDEX 409 path, and the timeout guard for
// the matching /api/games/registrace-available proxy.
public class GameLinkEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private async Task<GameDetailDto> CreateGameAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/games",
            new CreateGameDto(name, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 3)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>())!;
    }

    [Fact]
    public async Task RegistraceAvailable_UpstreamTimeout_ReturnsGatewayTimeoutWithinConfiguredTimeout()
    {
        var (timeoutFactory, timeoutClient) = await Postgres.CreateClientAsync(services =>
        {
            services.RemoveAll<RegistraceGameService>();
            services.AddHttpClient<RegistraceGameService>(client =>
                client.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SlowHttpMessageHandler(TimeSpan.FromSeconds(30)));
        });

        await using (timeoutFactory)
        using (timeoutClient)
        {
            var sw = Stopwatch.StartNew();
            var response = await timeoutClient.GetAsync("/api/games/registrace-available");
            sw.Stop();

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.True(
                sw.Elapsed < TimeSpan.FromSeconds(20),
                $"Expected the 15s registrace timeout, got {sw.Elapsed.TotalSeconds:F1}s.");

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal((int)HttpStatusCode.GatewayTimeout, problem.Status);
            Assert.Equal(RegistraceImportProblems.TimeoutTitle, problem.Title);
            Assert.Equal(RegistraceImportProblems.TimeoutDetail, problem.Detail);
        }
    }

    [Fact]
    public async Task Link_FreshExternalId_SetsAndReturnsNoContent()
    {
        var game = await CreateGameAsync("Štafeta II");

        var response = await Client.PostAsJsonAsync($"/api/games/{game.Id}/link",
            new LinkGameDto(42));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{game.Id}");
        Assert.NotNull(detail);
        Assert.Equal(42, detail.ExternalGameId);
    }

    [Fact]
    public async Task Link_NonExistentGame_ReturnsNotFound()
    {
        var response = await Client.PostAsJsonAsync("/api/games/999999/link",
            new LinkGameDto(7));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Link_RelinkSameGameToDifferentExternalId_Updates()
    {
        var game = await CreateGameAsync("Hra na opravu");
        await Client.PostAsJsonAsync($"/api/games/{game.Id}/link", new LinkGameDto(11));

        var response = await Client.PostAsJsonAsync($"/api/games/{game.Id}/link",
            new LinkGameDto(22));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{game.Id}");
        Assert.Equal(22, detail!.ExternalGameId);
    }

    [Fact]
    public async Task Link_ExternalIdAlreadyTaken_Returns409WithProblemDetails()
    {
        var first = await CreateGameAsync("První");
        var second = await CreateGameAsync("Druhá");

        var ok = await Client.PostAsJsonAsync($"/api/games/{first.Id}/link",
            new LinkGameDto(99));
        ok.EnsureSuccessStatusCode();

        var conflict = await Client.PostAsJsonAsync($"/api/games/{second.Id}/link",
            new LinkGameDto(99));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var problem = await conflict.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.Conflict, problem.Status);
        Assert.NotNull(problem.Detail);
        Assert.Contains("První", problem.Detail);

        // Second game must still be unlinked — no partial mutation.
        var detail = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{second.Id}");
        Assert.Null(detail!.ExternalGameId);
    }

    [Fact]
    public async Task Unlink_LinkedGame_ClearsExternalId()
    {
        var game = await CreateGameAsync("Pro odpojení");
        await Client.PostAsJsonAsync($"/api/games/{game.Id}/link", new LinkGameDto(55));

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/link");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await Client.GetFromJsonAsync<GameDetailDto>($"/api/games/{game.Id}");
        Assert.Null(detail!.ExternalGameId);
    }

    [Fact]
    public async Task Unlink_AlreadyUnlinked_ReturnsNoContent()
    {
        // Idempotent — unlinking an unlinked game is not an error; the
        // operation just clears an already-null field. Mirrors the
        // existing handler contract before issue #3.
        var game = await CreateGameAsync("Nikdy nebyla propojená");

        var response = await Client.DeleteAsync($"/api/games/{game.Id}/link");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unlink_NonExistentGame_ReturnsNotFound()
    {
        var response = await Client.DeleteAsync("/api/games/999999/link");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Link_ReleasesExternalIdAfterUnlink_AllowingAnotherGameToTakeIt()
    {
        // Verifies the filtered UNIQUE INDEX gating on NULL — once we
        // unlink, the external id frees up for a different local game.
        var first = await CreateGameAsync("Drží 77");
        var second = await CreateGameAsync("Bere 77");

        await Client.PostAsJsonAsync($"/api/games/{first.Id}/link", new LinkGameDto(77));
        await Client.DeleteAsync($"/api/games/{first.Id}/link");

        var response = await Client.PostAsJsonAsync($"/api/games/{second.Id}/link",
            new LinkGameDto(77));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

}
