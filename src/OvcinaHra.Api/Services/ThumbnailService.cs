using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace OvcinaHra.Api.Services;

/// <summary>
/// On-demand server-side thumbnail generator. Resizes source images fetched from
/// <see cref="IBlobStorageService"/>, encodes them as WebP, and caches the result
/// in the same blob container under the <c>{entity}-thumbs/</c> prefix. Subsequent
/// requests for the same entity/preset serve straight from the cache.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Produces a resized WebP thumbnail for the given source blob key at the given
    /// preset dimensions. Returns the encoded bytes on success, or <c>null</c> if
    /// the source blob does not exist or resize failed. Cache layout:
    /// <c>{entityType}-thumbs/{entityId}-{w}x{h}.webp</c>.
    /// </summary>
    Task<ThumbnailResult?> GetOrCreateAsync(
        string entityType, int entityId, string sourceBlobKey, ThumbnailPreset preset, CancellationToken ct = default);

    /// <summary>
    /// Fast cache probe — returns a SAS URL to the cached thumbnail blob if it exists,
    /// or <c>null</c> if the thumbnail has not been generated yet. Used by the endpoint
    /// to short-circuit hot-cache lookups into a 302 redirect (no bytes through the API).
    /// </summary>
    Task<string?> TryGetCachedSasUrlAsync(
        string entityType, int entityId, ThumbnailPreset preset, CancellationToken ct = default);
}

/// <summary>
/// Preset thumbnail dimensions. Values are pixel sizes after resize. Presets are a
/// closed set so we don't explode the cache with arbitrary <c>?w=&amp;h=</c> combos.
/// </summary>
public enum ThumbnailPreset
{
    /// <summary>120×120 — smallest list tiles (items, spells, etc.).</summary>
    Small,
    /// <summary>240×336 (5:7) — default for SecretStash / LocationStash cards.</summary>
    Medium,
    /// <summary>240×300 (4:5) — portrait tiles (characters).</summary>
    Portrait,
    /// <summary>480×672 — retina-friendly large tiles.</summary>
    Large
}

public record ThumbnailResult(byte[] Bytes, string ContentType);

public class ThumbnailService : IThumbnailService
{
    private readonly IBlobStorageService _blob;
    private readonly ILogger<ThumbnailService> _logger;

    // Cap concurrent resizes across the whole process. A list page fires ~50
    // thumbnail requests in parallel; 50 simultaneous ImageSharp decode+resize+
    // encode cycles tank a small Container App (503s from the platform). The
    // limit lets early requests finish while later ones queue briefly. Static
    // because ThumbnailService is DI-registered as singleton.
    private static readonly SemaphoreSlim _resizeGate = new(4, 4);

    public ThumbnailService(IBlobStorageService blob, ILogger<ThumbnailService> logger)
    {
        _blob = blob;
        _logger = logger;
    }

    public async Task<string?> TryGetCachedSasUrlAsync(
        string entityType, int entityId, ThumbnailPreset preset, CancellationToken ct = default)
    {
        var (width, height) = DimensionsFor(preset);
        var cacheKey = $"{entityType}-thumbs/{entityId}-{width}x{height}.webp";
        // ExistsAsync is a single HEAD request — cheap compared to DownloadAsync
        // or a full resize. When the thumb is cached (the common case after the
        // first visitor), the caller redirects to this SAS URL and the browser
        // fetches the bytes straight from Azure Blob. Zero bytes through the API.
        if (!await _blob.ExistsAsync(cacheKey, ct))
            return null;
        return _blob.GetSasUrl(cacheKey);
    }

    public async Task<ThumbnailResult?> GetOrCreateAsync(
        string entityType, int entityId, string sourceBlobKey, ThumbnailPreset preset, CancellationToken ct = default)
    {
        var (width, height) = DimensionsFor(preset);
        var cacheKey = $"{entityType}-thumbs/{entityId}-{width}x{height}.webp";

        // Cache hit? Return cached bytes directly. (Endpoint should have short-
        // circuited via TryGetCachedSasUrlAsync, but double-check in case the
        // cache populated after that probe.)
        var cached = await _blob.DownloadAsync(cacheKey, ct);
        if (cached is not null)
            return new ThumbnailResult(cached, "image/webp");

        // Cache miss — fetch source, resize, encode, cache. The resize block runs
        // under a concurrency gate so a thumbnail stampede on a cold page doesn't
        // overwhelm the container.
        var source = await _blob.DownloadAsync(sourceBlobKey, ct);
        if (source is null)
        {
            _logger.LogWarning("Thumbnail source blob missing: {SourceKey}", sourceBlobKey);
            return null;
        }

        await _resizeGate.WaitAsync(ct);
        try
        {
            // Re-check after acquiring the gate — another request may have populated
            // the cache while we were waiting. Avoids duplicate work.
            cached = await _blob.DownloadAsync(cacheKey, ct);
            if (cached is not null)
                return new ThumbnailResult(cached, "image/webp");

            byte[] resized;
            using (var image = Image.Load(source))
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
                    Mode = ResizeMode.Crop
                }));

                using var ms = new MemoryStream();
                await image.SaveAsync(ms, new WebpEncoder { Quality = 82 }, ct);
                resized = ms.ToArray();
            }

            using (var uploadStream = new MemoryStream(resized))
            {
                await _blob.UploadAsync(cacheKey, uploadStream, "image/webp", ct);
            }

            return new ThumbnailResult(resized, "image/webp");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail resize failed for {SourceKey} at preset {Preset}", sourceBlobKey, preset);
            return null;
        }
        finally
        {
            _resizeGate.Release();
        }
    }

    private static (int Width, int Height) DimensionsFor(ThumbnailPreset preset) => preset switch
    {
        ThumbnailPreset.Small => (120, 120),
        ThumbnailPreset.Medium => (240, 336),
        ThumbnailPreset.Portrait => (240, 300),
        ThumbnailPreset.Large => (480, 672),
        _ => (240, 336)
    };

    public static ThumbnailPreset ParsePreset(string? size) => size?.ToLowerInvariant() switch
    {
        "small" => ThumbnailPreset.Small,
        "medium" => ThumbnailPreset.Medium,
        "portrait" => ThumbnailPreset.Portrait,
        "large" => ThumbnailPreset.Large,
        _ => ThumbnailPreset.Medium
    };
}
