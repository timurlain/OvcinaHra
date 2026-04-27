using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;

namespace OvcinaHra.Api.Tests.Fixtures;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiWebApplicationFactory(
        string connectionString,
        Action<IServiceCollection>? configureServices = null)
    {
        _connectionString = connectionString;
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Issue #191 — pin the registrace integration base URL to a known
        // unreachable loopback so any RegistraceImportService HTTP call in
        // tests fails deterministically instead of depending on whether
        // the dev/CI environment can reach https://registrace.ovcina.cz.
        // Tests that need the upstream "linked but unreachable" path
        // assert on the resulting Errors list; tests that gate on the
        // "not linked" 400 short-circuit never reach the network anyway.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IntegrationApi:BaseUrl"] = "http://127.0.0.1:1",
                ["IntegrationApi:ApiKey"] = "test-key-not-used"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<WorldDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<WorldDbContext>(options =>
                options.UseNpgsql(_connectionString));

            // Replace blob storage with in-memory stub for tests
            var blobDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IBlobStorageService));
            if (blobDescriptor != null)
                services.Remove(blobDescriptor);

            services.AddSingleton<IBlobStorageService, InMemoryBlobService>();

            // Remove the startup thumbnail backfill hosted service — tests that
            // exercise the /api/images/backfill-thumbs endpoint seed their own
            // data and assert on the result of an explicit call. Letting the
            // hosted service run in parallel would race against the test's
            // arrange phase and flake assertions.
            var hostedDescriptor = services.SingleOrDefault(
                d => d.ImplementationType == typeof(ThumbnailBackfillHostedService));
            if (hostedDescriptor != null)
                services.Remove(hostedDescriptor);

            _configureServices?.Invoke(services);
        });
    }
}

public class InMemoryBlobService : IBlobStorageService
{
    private readonly Dictionary<string, byte[]> _blobs = new();

    public Task<string> UploadAsync(string blobKey, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        _blobs[blobKey] = ms.ToArray();
        return Task.FromResult(blobKey);
    }

    public Task<string?> GetSasUrlAsync(string blobKey, CancellationToken ct = default)
        => Task.FromResult(_blobs.ContainsKey(blobKey) ? $"https://fake/{blobKey}" : null);

    public string? GetSasUrl(string blobKey) => $"https://fake/{blobKey}";

    public Task DeleteAsync(string blobKey, CancellationToken ct = default)
    {
        _blobs.Remove(blobKey);
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var keys = _blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var k in keys)
            _blobs.Remove(k);
        return Task.CompletedTask;
    }

    public Task<byte[]?> DownloadAsync(string blobKey, CancellationToken ct = default)
        => Task.FromResult(_blobs.TryGetValue(blobKey, out var bytes) ? bytes : null);

    public Task<bool> ExistsAsync(string blobKey, CancellationToken ct = default)
        => Task.FromResult(_blobs.ContainsKey(blobKey));

    /// <summary>
    /// Synchronous cache probe for tests — lets assertions like
    /// <c>blob.Contains("locations-thumbs/1-120x120.webp")</c> verify the
    /// backfill sweep generated the expected cache entries without paying
    /// the async ceremony of <see cref="ExistsAsync"/>.
    /// </summary>
    public bool Contains(string blobKey) => _blobs.ContainsKey(blobKey);
}
