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

    /// <summary>
    /// Deletes every blob whose key starts with <paramref name="prefix"/>.
    /// Used by the thumbnail cache invalidation path: when a new source image is
    /// uploaded for an entity, all previously cached resized variants under
    /// <c>{entity}-thumbs/{id}-</c> must be wiped so the next thumb request
    /// regenerates from the fresh source. No-op if no blobs match.
    /// </summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Downloads a blob's content as a byte array. Returns <c>null</c> if the blob
    /// does not exist. Used by the thumbnail service to read source images for
    /// resizing.
    /// </summary>
    Task<byte[]?> DownloadAsync(string blobKey, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> iff a blob exists at the given key. Used by the
    /// thumbnail service for fast cache-hit checks.
    /// </summary>
    Task<bool> ExistsAsync(string blobKey, CancellationToken ct = default);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly int _sasExpiryHours;

    public BlobStorageService(IConfiguration config, ILogger<BlobStorageService> logger)
    {
        var connectionString = config["BlobStorage:ConnectionString"]!;
        var containerName = config["BlobStorage:ContainerName"] ?? "ovcinahra-images";
        _container = new BlobContainerClient(connectionString, containerName);
        _logger = logger;
        _sasExpiryHours = Math.Max(24, config.GetValue<int?>("BlobStorage:SasExpiryHours") ?? 24);
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
        catch (Exception ex)
        {
            // Never let a single row's SAS generation failure 500 a whole list page.
            // The tile components render a glyph fallback when ImageUrl is null.
            // Logged so credential/container misconfiguration is still diagnosable.
            _logger.LogWarning(ex, "Failed to generate SAS URL for blob key {BlobKey}", blobKey);
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
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(_sasExpiryHours)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sasBuilder).ToString();
    }

    public async Task DeleteAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        // Iterate pages of blob items matching the prefix and delete each. Under
        // normal use this hits a handful of blobs (one per preset per entity),
        // so a simple sequential delete is fine. We swallow per-blob delete
        // failures with a warning — a stale cached thumbnail is harmless
        // compared to failing the user's upload.
        await foreach (var item in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            try
            {
                await _container.GetBlobClient(item.Name).DeleteIfExistsAsync(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete cached thumbnail {BlobName} during prefix invalidation", item.Name);
            }
        }
    }

    public async Task<byte[]?> DownloadAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        if (!await blob.ExistsAsync(ct))
            return null;

        await using var ms = new MemoryStream();
        await blob.DownloadToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task<bool> ExistsAsync(string blobKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobKey);
        return await blob.ExistsAsync(ct);
    }
}
