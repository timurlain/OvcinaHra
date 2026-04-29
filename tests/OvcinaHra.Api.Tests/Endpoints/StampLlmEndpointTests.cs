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

public class StampLlmEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    private const string ReferenceBlobKey = "locationstamps/lesansky-haj-reference.png";

    [Fact]
    public async Task VerifyLlm_GoodCapture_ReturnsMatchAndWritesAuditLog()
    {
        var goodBytes = ReadTestData("sample-stamp-captured-good.jpg");
        var fake = new FakeStampLlmVerifyService(job =>
            job.CapturedImage.Bytes.SequenceEqual(goodBytes)
                ? new StampLlmVerifyResult(
                    true,
                    0.91,
                    "Silueta stromu se shoduje s referenčním razítkem i přes slabší otisk.",
                    job.ReferenceLocationName,
                    1234,
                    """{"match":true,"confidence":0.91,"reason":"Silueta stromu se shoduje s referenčním razítkem i přes slabší otisk."}""",
                    true)
                : new StampLlmVerifyResult(
                    false,
                    0.22,
                    "Otisk zobrazuje jiný tvar než referenční stromové razítko.",
                    job.ReferenceLocationName,
                    1200,
                    """{"match":false,"confidence":0.22,"reason":"Otisk zobrazuje jiný tvar než referenční stromové razítko."}""",
                    false));

        await using var resources = await CreateClientWithFakeAsync(fake);
        var locationId = await SeedLocationAsync(resources.Factory);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/verify-llm",
            new VerifyStampLlmRequest(
                locationId,
                ToDataUrl(goodBytes, "image/jpeg"),
                ContextStashId: 12,
                ContextQuestId: 34));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyStampLlmResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Match);
        Assert.True(body.Confidence >= 0.7);
        Assert.Equal("Lešanský háj", body.ReferenceLocationName);
        Assert.Equal(1234, body.LatencyMs);

        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.StampLlmVerifications.SingleAsync();
        Assert.Equal(locationId, audit.LocationId);
        Assert.Equal(12, audit.ContextStashId);
        Assert.Equal(34, audit.ContextQuestId);
        Assert.True(audit.Match);
        Assert.Equal(0.91, audit.Confidence);
        Assert.Contains("\"match\":true", audit.RawResponse);
    }

    [Fact]
    public async Task VerifyLlm_DifferentCapture_ReturnsMismatch()
    {
        var differentBytes = ReadTestData("sample-stamp-captured-different.jpg");
        var fake = new FakeStampLlmVerifyService(job => new StampLlmVerifyResult(
            false,
            0.18,
            "Otisk připomíná hvězdu, ale referenční razítko má stromovou siluetu.",
            job.ReferenceLocationName,
            987,
            """{"match":false,"confidence":0.18,"reason":"Otisk připomíná hvězdu, ale referenční razítko má stromovou siluetu."}""",
            false));

        await using var resources = await CreateClientWithFakeAsync(fake);
        var locationId = await SeedLocationAsync(resources.Factory);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/verify-llm",
            new VerifyStampLlmRequest(locationId, ToDataUrl(differentBytes, "image/jpeg")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyStampLlmResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Match);
        Assert.True(body.Confidence < 0.7);
    }

    [Fact]
    public async Task VerifyLlm_BadBase64_ReturnsCzechProblemDetails()
    {
        var fake = new FakeStampLlmVerifyService(_ => throw new InvalidOperationException("Verifier must not run."));
        await using var resources = await CreateClientWithFakeAsync(fake);
        var locationId = await SeedLocationAsync(resources.Factory);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/verify-llm",
            new VerifyStampLlmRequest(locationId, "not-base64"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Neplatný snímek razítka", "base64");
        Assert.Empty(fake.Jobs);
    }

    [Fact]
    public async Task VerifyLlm_LocationWithoutStamp_ReturnsCzechProblemDetails()
    {
        var fake = new FakeStampLlmVerifyService(_ => throw new InvalidOperationException("Verifier must not run."));
        await using var resources = await CreateClientWithFakeAsync(fake);
        var locationId = await SeedLocationAsync(resources.Factory, stampImagePath: null);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/verify-llm",
            new VerifyStampLlmRequest(locationId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response, "Chybí referenční razítko", "nemá nastavený");
        Assert.Empty(fake.Jobs);
    }

    [Fact]
    public async Task VerifyLlm_AnthropicError_Returns502AndWritesFailureAudit()
    {
        var fake = new FakeStampLlmVerifyService(_ => throw new StampLlmProviderException(
            "provider failed",
            432,
            "upstream boom"));
        await using var resources = await CreateClientWithFakeAsync(fake);
        var locationId = await SeedLocationAsync(resources.Factory);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/verify-llm",
            new VerifyStampLlmRequest(locationId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await AssertProblemDetailsAsync(response, "LLM ověření selhalo", "upstream boom");

        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.StampLlmVerifications.SingleAsync();
        Assert.False(audit.Match);
        Assert.Equal(0, audit.Confidence);
        Assert.Equal(432, audit.LatencyMs);
        Assert.Equal("upstream boom", audit.RawResponse);
    }

    [Fact]
    public async Task VerifyLlm_RateLimited_Returns429AndWritesFailureAudit()
    {
        var fake = new FakeStampLlmVerifyService(_ => throw new StampLlmRateLimitedException(
            "rate limited",
            211,
            "too many requests"));
        await using var resources = await CreateClientWithFakeAsync(fake);
        var locationId = await SeedLocationAsync(resources.Factory);

        var response = await resources.Client.PostAsJsonAsync("/api/stamps/verify-llm",
            new VerifyStampLlmRequest(locationId, ToDataUrl(ReadTestData("sample-stamp-captured-good.jpg"), "image/jpeg")));

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        await AssertProblemDetailsAsync(response, "LLM ověření je dočasně omezené", "too many requests");

        using var scope = resources.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var audit = await db.StampLlmVerifications.SingleAsync();
        Assert.False(audit.Match);
        Assert.Equal(211, audit.LatencyMs);
    }

    private async Task<TestResources> CreateClientWithFakeAsync(FakeStampLlmVerifyService fake)
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

    private static async Task<int> SeedLocationAsync(ApiWebApplicationFactory factory, string? stampImagePath = ReferenceBlobKey)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var location = new Location
        {
            Name = "Lešanský háj",
            LocationKind = LocationKind.Wilderness,
            StampImagePath = stampImagePath
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(stampImagePath))
        {
            var blob = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();
            await using var stream = new MemoryStream(ReadTestData("sample-stamp-reference.png"));
            await blob.UploadAsync(stampImagePath, stream, "image/png");
        }

        return location.Id;
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedTitle,
        string expectedDetailFragment)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedTitle, problem!.Title);
        Assert.Contains(expectedDetailFragment, problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadTestData(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));

    private static string ToDataUrl(byte[] bytes, string mediaType)
        => $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

    private sealed class FakeStampLlmVerifyService(Func<StampLlmVerifyJob, StampLlmVerifyResult> verify)
        : IStampLlmVerifyService
    {
        public List<StampLlmVerifyJob> Jobs { get; } = [];

        public Task<StampLlmVerifyResult> VerifyAsync(StampLlmVerifyJob job, CancellationToken ct = default)
        {
            Jobs.Add(job);
            return Task.FromResult(verify(job));
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
