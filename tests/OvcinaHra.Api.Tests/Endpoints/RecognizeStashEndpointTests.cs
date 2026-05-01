using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

/// <summary>
/// Sister tests for /api/stamps/recognize. Mirrors <see cref="StampLlmEndpointTests"/> shape:
/// per-class fake LLM service (registered via <see cref="ApiWebApplicationFactory"/> overrides),
/// real Postgres, real seed data. The recognize endpoint is many-to-1 so the seed has multiple
/// stamped locations under one game, and the fake's Func decides which one wins.
/// </summary>
public class RecognizeStashEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private const string ReferenceBlobKeyPrefix = "locationstamps/recognize-test-";

    [Fact]
    public async Task Recognize_HappyPath_RanksTopCandidateAndAttachesStashes()
    {
        var capturedBytes = ReadTestData("sample-stamp-captured-good.jpg");

        // Fake ranks the location whose name starts with "Stará chýše" highest, second
        // location next, third lowest. Tests aggregate + projection + audit row writing.
        var fake = new FakeStampLlmRecognizeService(job =>
        {
            var ranked = job.References
                .Select((r, i) => new StampLlmRankedCandidate(
                    r.LocationId,
                    r.LocationName,
                    r.LocationName.StartsWith("Stará chýše") ? 0.92
                        : r.LocationName.StartsWith("Mlýn") ? 0.41
                        : 0.18))
                .OrderByDescending(c => c.Confidence)
                .ToList();

            return new StampLlmRecognizeResult(
                ranked,
                job.References.Count,
                7240,
                """{"candidates":[{"index":1,"confidence":0.92}]}""");
        });

        await using var resources = await CreateClientWithFakeAsync(fake);
        var (gameId, _) = await SeedGameWithStampedLocationsAsync(resources.Factory, 3);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(gameId, ToDataUrl(capturedBytes, "image/jpeg")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RecognizeStashResponse>();
        Assert.NotNull(body);
        Assert.False(body!.NoReferences);
        Assert.Equal(3, body.TotalReferencesScanned);
        Assert.Equal(7240, body.LatencyMs);
        Assert.Equal(3, body.Candidates.Count);
        Assert.StartsWith("Stará chýše", body.Candidates[0].LocationName);
        Assert.True(body.Candidates[0].Confidence > body.Candidates[1].Confidence);

        // Top-1 has its stashes inlined.
        Assert.NotEmpty(body.Candidates[0].Stashes);

        // Audit row written.
        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.StampLlmVerifications.SingleAsync();
        Assert.Equal(StampLlmVerification.ModeRecognize, audit.Mode);
        Assert.Equal(gameId, audit.GameId);
        Assert.Equal(3, audit.ReferencesScanned);
        Assert.True(audit.Match);
        Assert.Equal(0.92, audit.Confidence);
    }

    [Fact]
    public async Task Recognize_NoStampedLocations_ReturnsNoReferencesFlag()
    {
        var fake = new FakeStampLlmRecognizeService(_ =>
            throw new InvalidOperationException("Verifier must not run when there are no references."));

        await using var resources = await CreateClientWithFakeAsync(fake);
        var gameId = await SeedGameWithoutStampsAsync(resources.Factory);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(gameId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RecognizeStashResponse>();
        Assert.NotNull(body);
        Assert.True(body!.NoReferences);
        Assert.Empty(body.Candidates);
        Assert.Equal(0, body.TotalReferencesScanned);
        Assert.Empty(fake.Jobs);

        // No audit row when no LLM call.
        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        Assert.Equal(0, await db.StampLlmVerifications.CountAsync());
    }

    [Fact]
    public async Task Recognize_GameNotFound_Returns404()
    {
        var fake = new FakeStampLlmRecognizeService(_ =>
            throw new InvalidOperationException("Verifier must not run."));

        await using var resources = await CreateClientWithFakeAsync(fake);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(GameId: 999_999, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Hra neexistuje", "999999");
        Assert.Empty(fake.Jobs);
    }

    [Fact]
    public async Task Recognize_BadBase64_ReturnsCzechProblemDetails()
    {
        var fake = new FakeStampLlmRecognizeService(_ =>
            throw new InvalidOperationException("Verifier must not run."));

        await using var resources = await CreateClientWithFakeAsync(fake);
        var (gameId, _) = await SeedGameWithStampedLocationsAsync(resources.Factory, 1);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(gameId, "not-base64"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Neplatný snímek razítka", "base64");
        Assert.Empty(fake.Jobs);
    }

    [Fact]
    public async Task Recognize_AnthropicError_Returns502_AndWritesAuditFailure()
    {
        var fake = new FakeStampLlmRecognizeService(_ =>
            throw new StampLlmProviderException("upstream boom", 211, "raw upstream body"));

        await using var resources = await CreateClientWithFakeAsync(fake);
        var (gameId, _) = await SeedGameWithStampedLocationsAsync(resources.Factory, 2);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(gameId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await AssertProblemDetailsAsync(response, "LLM rozpoznání selhalo", "ruční výběr");
        Assert.DoesNotContain("upstream boom", problem.Detail, StringComparison.OrdinalIgnoreCase);

        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.StampLlmVerifications.SingleAsync();
        Assert.Equal(StampLlmVerification.ModeRecognize, audit.Mode);
        Assert.False(audit.Match);
        Assert.Equal(211, audit.LatencyMs);
        Assert.Equal(gameId, audit.GameId);
    }

    [Fact]
    public async Task Recognize_RateLimited_Returns429_AndWritesAuditFailure()
    {
        var fake = new FakeStampLlmRecognizeService(_ =>
            throw new StampLlmRateLimitedException("rate limited", 150, "too many requests"));

        await using var resources = await CreateClientWithFakeAsync(fake);
        var (gameId, _) = await SeedGameWithStampedLocationsAsync(resources.Factory, 1);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(gameId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        var problem = await AssertProblemDetailsAsync(response, "LLM rozpoznání je dočasně omezené", "zkus");
        Assert.DoesNotContain("too many requests", problem.Detail, StringComparison.OrdinalIgnoreCase);

        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.StampLlmVerifications.SingleAsync();
        Assert.Equal(StampLlmVerification.ModeRecognize, audit.Mode);
        Assert.Equal(150, audit.LatencyMs);
    }

    [Fact]
    public async Task Recognize_MissingAnthropicConfiguration_Returns503()
    {
        var fake = new FakeStampLlmRecognizeService(
            _ => throw new InvalidOperationException("Verifier must not run."),
            isConfigured: false);

        await using var resources = await CreateClientWithFakeAsync(fake);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(GameId: 999, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        // The 503 helper is shared with /verify-llm — title intentionally not branded "rozpoznání"
        // to keep the operator-facing wording consistent across both LLM endpoints.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertProblemDetailsAsync(
            response,
            "LLM ověření není v tomto prostředí nakonfigurované",
            "Anthropic__ApiKey");
        Assert.Empty(fake.Jobs);
    }

    [Fact]
    public async Task Recognize_TwentyFiveStampedLocations_BatchesAndReturnsTop3()
    {
        // 25 stamped locations forces 2 batches (19 + 6) under MaxReferencesPerBatch=19.
        // Fake echoes whatever's in the request — in this test we configure the fake to
        // mark location index 12 (in the seed order) as the winner across batches.
        var fake = new FakeStampLlmRecognizeService(job =>
        {
            var ranked = job.References
                .Select((r, i) => new StampLlmRankedCandidate(
                    r.LocationId,
                    r.LocationName,
                    r.LocationName == "Lokace-12" ? 0.95
                        : r.LocationName == "Lokace-3" ? 0.55
                        : r.LocationName == "Lokace-20" ? 0.40
                        : 0.10))
                .OrderByDescending(c => c.Confidence)
                .ToList();

            return new StampLlmRecognizeResult(
                ranked,
                job.References.Count,
                12_500,
                """{"candidates":[]}""");
        });

        await using var resources = await CreateClientWithFakeAsync(fake);
        var (gameId, _) = await SeedGameWithStampedLocationsAsync(resources.Factory, 25);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/recognize",
            new RecognizeStashRequest(gameId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RecognizeStashResponse>();
        Assert.NotNull(body);
        Assert.Equal(25, body!.TotalReferencesScanned);
        Assert.Equal(3, body.Candidates.Count);
        Assert.Equal("Lokace-12", body.Candidates[0].LocationName);
        Assert.Equal(0.95, body.Candidates[0].Confidence);
        Assert.True(body.Candidates[0].Confidence > body.Candidates[1].Confidence);
        Assert.True(body.Candidates[1].Confidence > body.Candidates[2].Confidence);
    }

    private async Task<TestResources> CreateClientWithFakeAsync(FakeStampLlmRecognizeService fake)
    {
        var (factory, client) = await Postgres.CreateClientAsync(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStampLlmVerifyService));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IStampLlmVerifyService>(fake);
        });

        return new TestResources(factory, client);
    }

    /// <summary>
    /// Seeds <paramref name="locationCount"/> Locations under a fresh Game, every one with a
    /// distinct StampImagePath and uploaded reference blob. The first three locations also
    /// get a SecretStash + GameSecretStash so the candidate-stash projection has data to find.
    /// </summary>
    private static async Task<(int GameId, IReadOnlyList<int> LocationIds)> SeedGameWithStampedLocationsAsync(
        ApiWebApplicationFactory factory,
        int locationCount)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var blob = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        var game = new Game
        {
            Name = "Recognize Test",
            Edition = 30,
            Status = GameStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2))
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        var locationIds = new List<int>(locationCount);
        var refBytes = ReadTestData("sample-stamp-reference.png");

        for (var i = 0; i < locationCount; i++)
        {
            // First three named for the happy-path assertions; rest are "Lokace-N" for the
            // batch test. Distinct StampImagePath per location so the resolve loop has work.
            var name = i switch
            {
                0 => "Stará chýše",
                1 => "Mlýn",
                2 => "Stará studna",
                _ => $"Lokace-{i}"
            };

            var blobKey = $"{ReferenceBlobKeyPrefix}{Guid.NewGuid():N}.png";
            var location = new Location
            {
                Name = name,
                LocationKind = LocationKind.Wilderness,
                StampImagePath = blobKey
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();

            db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = location.Id });

            await using (var stream = new MemoryStream(refBytes))
            {
                await blob.UploadAsync(blobKey, stream, "image/png");
            }

            locationIds.Add(location.Id);
        }

        // Seed two stashes on each of the first three locations so the candidate UI has
        // something to render. Beyond that the batch test doesn't need stashes.
        for (var i = 0; i < Math.Min(3, locationCount); i++)
        {
            for (var s = 0; s < 2; s++)
            {
                var stash = new SecretStash
                {
                    Name = $"Skrýš-{i}-{s}",
                    Description = $"Popis skrýše {i}-{s}"
                };
                db.SecretStashes.Add(stash);
                await db.SaveChangesAsync();
                db.GameSecretStashes.Add(new GameSecretStash
                {
                    GameId = game.Id,
                    LocationId = locationIds[i],
                    SecretStashId = stash.Id
                });
            }
        }
        await db.SaveChangesAsync();

        return (game.Id, locationIds);
    }

    private static async Task<int> SeedGameWithoutStampsAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var game = new Game
        {
            Name = "Empty Recognize Game",
            Edition = 31,
            Status = GameStatus.Draft,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2))
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        // Add a location WITHOUT a stamp — confirms the StampImagePath filter actually filters.
        var location = new Location
        {
            Name = "Bez razítka",
            LocationKind = LocationKind.Wilderness,
            StampImagePath = null
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        db.GameLocations.Add(new GameLocation { GameId = game.Id, LocationId = location.Id });
        await db.SaveChangesAsync();

        return game.Id;
    }

    private static async Task<ProblemDetails> AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedTitle,
        string expectedDetailFragment)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedTitle, problem!.Title);
        Assert.Contains(expectedDetailFragment, problem.Detail, StringComparison.OrdinalIgnoreCase);
        return problem;
    }

    private static byte[] ReadTestData(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));

    private static string ToDataUrl(byte[] bytes, string mediaType)
        => $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

    /// <summary>
    /// In-memory IStampLlmVerifyService that records every recognize call and returns the
    /// caller's pre-canned result. <see cref="VerifyAsync"/> is left throwing because these
    /// tests don't exercise the verify endpoint.
    /// </summary>
    private sealed class FakeStampLlmRecognizeService(
        Func<StampLlmRecognizeJob, StampLlmRecognizeResult> recognize,
        bool isConfigured = true)
        : IStampLlmVerifyService
    {
        public bool IsConfigured { get; } = isConfigured;
        public List<StampLlmRecognizeJob> Jobs { get; } = [];

        public Task<StampLlmVerifyResult> VerifyAsync(StampLlmVerifyJob job, CancellationToken ct = default)
            => throw new InvalidOperationException("Recognize tests must not call verify.");

        public Task<StampLlmRecognizeResult> RecognizeAsync(StampLlmRecognizeJob job, CancellationToken ct = default)
        {
            Jobs.Add(job);
            return Task.FromResult(recognize(job));
        }
    }

    private sealed record TestResources(ApiWebApplicationFactory Factory, HttpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
