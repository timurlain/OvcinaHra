using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace OvcinaHra.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobKey, Stream content, string contentType, CancellationToken ct = default);
    Task<string?> GetSasUrlAsync(string blobKey, CancellationToken ct = default);

    /// <summary>
    /// Synchronous SAS URL builder for list endpoints — skips the remote existence
    /// check performed by <see cref="GetSasUrlAsync"/>. A list page with 50 rows would
    /// otherwise do 50 blob existence round trips to Azure. Callers pass a blob key
    /// stored on the entity and trust it; a stale key just produces a broken image
    /// at the client (handled by an <c>onerror</c> fallback in the tile components).
    /// Returns <c>null</c> if SAS generation throws (e.g., misconfigured credentials) so
    /// a single bad key or transient SDK error cannot 500 a whole list page.
    /// </summary>
    string? GetSasUrl(string blobKey);

    Task DeleteAsync(string blobKey, CancellationToken ct = default);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;

    public BlobStorageService(IConfiguration config)
    {
        var connectionString = config["BlobStorage:ConnectionString"]!;
        var containerName = config["BlobStorage:ContainerName"] ?? "ovcinahra-images";
        _container = new BlobContainerClient(connectionString, containerName);
    }

    public async Task<string> UploadAsync(string blobKey, Stream content, string contentType, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = _container.GetBlobClient(blobKey);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blobKey;
    }

    public async Task<string?> GetSasUrlAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        if (!await blob.ExistsAsync(ct))
            return null;

        return BuildSasUrl(blob);
    }

    public string? GetSasUrl(string blobKey)
    {
        try
        {
            var blob = _container.GetBlobClient(blobKey);
            return BuildSasUrl(blob);
        }
        catch
        {
            // Never let a single row's SAS generation failure 500 a whole list page.
            // The tile components render a glyph fallback when ImageUrl is null.
            return null;
        }
    }

    private string BuildSasUrl(BlobClient blob)
    {
        // For Azurite (dev), SAS doesn't work easily -- return direct URL
        // For production, generate SAS token
        if (_container.Uri.Host.Contains("127.0.0.1") || _container.Uri.Host.Contains("localhost"))
        {
            return blob.Uri.ToString();
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blob.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sasBuilder).ToString();
    }

    public async Task DeleteAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }
}
