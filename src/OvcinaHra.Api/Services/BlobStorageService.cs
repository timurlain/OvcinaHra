using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace OvcinaHra.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobKey, Stream content, string contentType, CancellationToken ct = default);
    Task<string?> GetSasUrlAsync(string blobKey, CancellationToken ct = default);
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

        // For Azurite (dev), SAS doesn't work easily -- return direct URL
        // For production, generate SAS token
        if (_container.Uri.Host.Contains("127.0.0.1") || _container.Uri.Host.Contains("localhost"))
        {
            return blob.Uri.ToString();
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobKey,
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
