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

// Issue #191 — when a local OvčinaHra game has no ExternalGameId, both
// import surfaces (POST /api/characters/import/{id} and the NPC picker
// GET /api/npcs/available-players/{id}) must return 400 ProblemDetails
// before ever reaching out to registrace. This codifies the contract.
//
// Happy-path import is intentionally not tested here — it would require
// modelling registrace's full payload. The 400 path runs entirely against
// the local DB so it's deterministic; the timeout path uses a slow stubbed
// HttpMessageHandler.
public class RegistraceImportNotLinkedTests(PostgresFixture postgres)
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
    public async Task Import_GameNotLinked_Returns400ProblemDetails()
    {
        var game = await CreateGameAsync("Bez registrace");

        var response = await Client.PostAsJsonAsync(
            $"/api/characters/import/{game.Id}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
        Assert.NotNull(problem.Detail);
        Assert.Contains("propojená s registrací", problem.Detail);
    }

    [Fact]
    public async Task AvailablePlayers_GameNotLinked_Returns400ProblemDetails()
    {
        var game = await CreateGameAsync("NPC bez registrace");

        var response = await Client.GetAsync($"/api/npcs/available-players/{game.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
        Assert.NotNull(problem.Detail);
        Assert.Contains("propojená s registrací", problem.Detail);
    }

    [Fact]
    public async Task Import_GameLinkedButRegistraceUnreachable_DoesNotShortCircuitWith400()
    {
        // When ExternalGameId is set we expect the service to reach the
        // upstream HTTP call. In the test environment registrace is
        // unreachable, so ImportAsync's catch-block returns an empty
        // ImportResultDto with the network error string in `Errors`.
        // Importantly, the response body is NOT a 400 — the "not linked"
        // short-circuit must not fire when ExternalGameId is non-null.
        var game = await CreateGameAsync("Propojená");
        var link = await Client.PostAsJsonAsync($"/api/games/{game.Id}/link",
            new LinkGameDto(424242));
        link.EnsureSuccessStatusCode();

        var response = await Client.PostAsJsonAsync(
            $"/api/characters/import/{game.Id}", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.NotNull(result);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.NotEmpty(result.Errors); // network failure surfaced, not a 400
    }

    [Fact]
    public async Task Import_GameLinkedButRegistraceTimeout_Returns504WithinConfiguredTimeout()
    {
        var game = await CreateGameAsync("Propojená timeout");
        var link = await Client.PostAsJsonAsync($"/api/games/{game.Id}/link",
            new LinkGameDto(123456));
        link.EnsureSuccessStatusCode();

        var (timeoutFactory, timeoutClient) = await Postgres.CreateClientAsync(services =>
        {
            services.RemoveAll<RegistraceImportService>();
            services.AddHttpClient<RegistraceImportService>(client =>
                client.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SlowRegistraceHandler(TimeSpan.FromSeconds(30)));
        });

        await using (timeoutFactory)
        using (timeoutClient)
        {
            var sw = Stopwatch.StartNew();
            var response = await timeoutClient.PostAsJsonAsync(
                $"/api/characters/import/{game.Id}", new { });
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

    private sealed class SlowRegistraceHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<object>())
            };
        }
    }
}
