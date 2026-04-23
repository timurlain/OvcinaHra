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

    public ThumbnailService(IBlobStorageService blob, ILogger<ThumbnailService> logger)
    {
        _blob = blob;
        _logger = logger;
    }

    public async Task<ThumbnailResult?> GetOrCreateAsync(
        string entityType, int entityId, string sourceBlobKey, ThumbnailPreset preset, CancellationToken ct = default)
    {
        var (width, height) = DimensionsFor(preset);
        var cacheKey = $"{entityType}-thumbs/{entityId}-{width}x{height}.webp";

        // Cache hit? Return cached bytes directly.
        var cached = await _blob.DownloadAsync(cacheKey, ct);
        if (cached is not null)
            return new ThumbnailResult(cached, "image/webp");

        // Cache miss — fetch source, resize, encode, cache.
        var source = await _blob.DownloadAsync(sourceBlobKey, ct);
        if (source is null)
        {
            _logger.LogWarning("Thumbnail source blob missing: {SourceKey}", sourceBlobKey);
            return null;
        }

        try
        {
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

            // Write to cache (fire-and-forget style would be nicer but we want the
            // write to complete so a concurrent second request picks it up; the
            // extra ~20 ms is negligible).
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
