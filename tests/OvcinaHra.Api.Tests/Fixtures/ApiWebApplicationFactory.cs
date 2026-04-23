using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;

namespace OvcinaHra.Api.Tests.Fixtures;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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

    public string GetSasUrl(string blobKey) => $"https://fake/{blobKey}";

    public Task DeleteAsync(string blobKey, CancellationToken ct = default)
    {
        _blobs.Remove(blobKey);
        return Task.CompletedTask;
    }
}
