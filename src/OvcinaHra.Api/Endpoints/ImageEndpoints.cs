using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ImageEndpoints
{
    private static readonly HashSet<string> ValidEntityTypes = ["locations", "items", "monsters", "secretstashes", "npcs", "buildings", "characters", "kingdoms", "spells"];
    private static readonly HashSet<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/images").WithTags("Images");

        group.MapPost("/{entityType}/{entityId:int}", Upload).DisableAntiforgery();
        group.MapGet("/{entityType}/{entityId:int}", GetUrls);
        group.MapGet("/{entityType}/{entityId:int}/thumb", GetThumbnail).AllowAnonymous();
        group.MapDelete("/{entityType}/{entityId:int}", Delete);

        return group;
    }

    /// <summary>
    /// Builds an absolute URL to the thumbnail endpoint for a given entity.
    /// Needed because the frontend (hra.ovcina.cz) and API (api.hra.ovcina.cz)
    /// live on different origins — a relative "/api/images/..." URL would be
    /// resolved by the browser against the frontend host and 404.
    ///
    /// Prefers a configured <c>Api:PublicBaseUrl</c> (set in prod via the
    /// Azure Container App env var <c>Api__PublicBaseUrl=https://api.hra.ovcina.cz</c>)
    /// to sidestep host-header injection — <c>AllowedHosts</c> is permissive
    /// ("*") and <c>Request.Host</c> reflects the inbound header. Falls back to
    /// <c>Request.Scheme+Host</c> for local dev where no config is set.
    /// </summary>
    public static string ThumbUrl(HttpContext http, string entityType, int entityId, string size)
    {
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        var baseUrl = config["Api:PublicBaseUrl"]?.TrimEnd('/')
            ?? $"{http.Request.Scheme}://{http.Request.Host}";
        return $"{baseUrl}/api/images/{entityType}/{entityId}/thumb?size={size}";
    }

    private static async Task<IResult> GetThumbnail(
        string entityType, int entityId,
        IBlobStorageService blobService, IThumbnailService thumbnailService, WorldDbContext db,
        HttpContext http, CancellationToken ct, string? size = null)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.BadRequest($"Invalid entity type '{entityType}'.");

        if (!await EntityExists(db, entityType, entityId))
            return TypedResults.NotFound();

        var (imagePath, _) = await GetEntityImagePaths(db, entityType, entityId);
        if (string.IsNullOrWhiteSpace(imagePath))
            return TypedResults.NotFound();

        var preset = ThumbnailService.ParsePreset(size);
        var result = await thumbnailService.GetOrCreateAsync(entityType, entityId, imagePath, preset, ct);

        if (result is null)
        {
            // Graceful degrade — return a redirect to the full-res SAS so the
            // user still sees something when resize fails (missing source
            // blob, corrupt image, ImageSharp exception, etc.).
            var fallback = blobService.GetSasUrl(imagePath);
            if (fallback is null)
                return TypedResults.NotFound();
            return TypedResults.Redirect(fallback, permanent: false);
        }

        // Let browsers cache aggressively — list pages revisit the same tile
        // URLs constantly, and our cache-key includes the (entity, size) pair.
        // Upload path invalidates the server cache, but user browsers will
        // only refresh after 24 h or a hard reload. Acceptable for this UX.
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return TypedResults.File(result.Bytes, result.ContentType);
    }

    private static async Task<Results<Ok<ImageUploadResult>, NotFound, BadRequest<string>>> Upload(
        string entityType, int entityId, IFormFile file, IBlobStorageService blobService,
        WorldDbContext db, string? field = null)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.BadRequest($"Invalid entity type '{entityType}'.");

        if (file.Length > MaxFileSize)
            return TypedResults.BadRequest($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)} MB.");

        if (!AllowedContentTypes.Contains(file.ContentType))
            return TypedResults.BadRequest($"Invalid content type '{file.ContentType}'. Allowed: {string.Join(", ", AllowedContentTypes)}.");

        if (!await EntityExists(db, entityType, entityId))
            return TypedResults.NotFound();

        var isPlacement = string.Equals(field, "placement", StringComparison.OrdinalIgnoreCase);
        var suffix = isPlacement ? "placement" : "image";
        var ext = file.ContentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "bin"
        };
        var blobKey = $"{entityType}/{entityId}/{suffix}.{ext}";

        await using var stream = file.OpenReadStream();
        await blobService.UploadAsync(blobKey, stream, file.ContentType);

        // Invalidate any cached thumbnails for this entity — they were resized
        // from the previous source image and are stale now. Only apply to the
        // main image path; placement photos are detail-only and not cached.
        if (!isPlacement)
            await blobService.DeletePrefixAsync($"{entityType}-thumbs/{entityId}-");

        await UpdateEntityImagePath(db, entityType, entityId, blobKey, isPlacement);

        var url = await blobService.GetSasUrlAsync(blobKey);
        return TypedResults.Ok(new ImageUploadResult(blobKey, url));
    }

    private static async Task<Results<Ok<ImageUrlsDto>, NotFound, BadRequest<string>>> GetUrls(
        string entityType, int entityId, IBlobStorageService blobService, WorldDbContext db)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.BadRequest($"Invalid entity type '{entityType}'.");

        if (!await EntityExists(db, entityType, entityId))
            return TypedResults.NotFound();

        var (imagePath, placementPath) = await GetEntityImagePaths(db, entityType, entityId);

        string? imageUrl = imagePath is not null ? await blobService.GetSasUrlAsync(imagePath) : null;
        string? placementUrl = placementPath is not null ? await blobService.GetSasUrlAsync(placementPath) : null;

        return TypedResults.Ok(new ImageUrlsDto(imageUrl, placementUrl));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> Delete(
        string entityType, int entityId, IBlobStorageService blobService,
        WorldDbContext db, string? field = null)
    {
        if (!ValidEntityTypes.Contains(entityType))
            return TypedResults.BadRequest($"Invalid entity type '{entityType}'.");

        if (!await EntityExists(db, entityType, entityId))
            return TypedResults.NotFound();

        var isPlacement = string.Equals(field, "placement", StringComparison.OrdinalIgnoreCase);
        var (imagePath, placementPath) = await GetEntityImagePaths(db, entityType, entityId);
        var pathToDelete = isPlacement ? placementPath : imagePath;

        if (pathToDelete is not null)
        {
            await blobService.DeleteAsync(pathToDelete);
            await UpdateEntityImagePath(db, entityType, entityId, null, isPlacement);
        }

        return TypedResults.NoContent();
    }

    private static async Task UpdateEntityImagePath(WorldDbContext db, string entityType, int entityId, string? blobKey, bool isPlacement)
    {
        switch (entityType)
        {
            case "locations":
                var location = await db.Locations.FindAsync(entityId);
                if (location is not null)
                {
                    if (isPlacement)
                        location.PlacementPhotoPath = blobKey;
                    else
                        location.ImagePath = blobKey;
                }
                break;
            case "items":
                var item = await db.Items.FindAsync(entityId);
                if (item is not null) item.ImagePath = blobKey;
                break;
            case "monsters":
                var monster = await db.Monsters.FindAsync(entityId);
                if (monster is not null) monster.ImagePath = blobKey;
                break;
            case "secretstashes":
                var stash = await db.SecretStashes.FindAsync(entityId);
                if (stash is not null) stash.ImagePath = blobKey;
                break;
            case "npcs":
                var npc = await db.Npcs.FindAsync(entityId);
                if (npc is not null) npc.ImagePath = blobKey;
                break;
            case "buildings":
                var building = await db.Buildings.FindAsync(entityId);
                if (building is not null) building.ImagePath = blobKey;
                break;
            case "characters":
                var character = await db.Characters.FindAsync(entityId);
                if (character is not null) character.ImagePath = blobKey;
                break;
            case "kingdoms":
                var kingdom = await db.Kingdoms.FindAsync(entityId);
                if (kingdom is not null) kingdom.BadgeImageUrl = blobKey;
                break;
            case "spells":
                var spell = await db.Spells.FindAsync(entityId);
                if (spell is not null) spell.ImagePath = blobKey;
                break;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<(string? ImagePath, string? PlacementPath)> GetEntityImagePaths(
        WorldDbContext db, string entityType, int entityId)
    {
        return entityType switch
        {
            "locations" => await db.Locations.Where(l => l.Id == entityId)
                .Select(l => new ValueTuple<string?, string?>(l.ImagePath, l.PlacementPhotoPath))
                .FirstOrDefaultAsync(),
            "items" => await db.Items.Where(i => i.Id == entityId)
                .Select(i => new ValueTuple<string?, string?>(i.ImagePath, null))
                .FirstOrDefaultAsync(),
            "monsters" => await db.Monsters.Where(m => m.Id == entityId)
                .Select(m => new ValueTuple<string?, string?>(m.ImagePath, null))
                .FirstOrDefaultAsync(),
            "secretstashes" => await db.SecretStashes.Where(s => s.Id == entityId)
                .Select(s => new ValueTuple<string?, string?>(s.ImagePath, null))
                .FirstOrDefaultAsync(),
            "npcs" => await db.Npcs.Where(n => n.Id == entityId)
                .Select(n => new ValueTuple<string?, string?>(n.ImagePath, null))
                .FirstOrDefaultAsync(),
            "buildings" => await db.Buildings.Where(b => b.Id == entityId)
                .Select(b => new ValueTuple<string?, string?>(b.ImagePath, null))
                .FirstOrDefaultAsync(),
            "characters" => await db.Characters.Where(c => c.Id == entityId)
                .Select(c => new ValueTuple<string?, string?>(c.ImagePath, null))
                .FirstOrDefaultAsync(),
            "kingdoms" => await db.Kingdoms.Where(k => k.Id == entityId)
                .Select(k => new ValueTuple<string?, string?>(k.BadgeImageUrl, null))
                .FirstOrDefaultAsync(),
            "spells" => await db.Spells.Where(s => s.Id == entityId)
                .Select(s => new ValueTuple<string?, string?>(s.ImagePath, null))
                .FirstOrDefaultAsync(),
            _ => (null, null)
        };
    }

    private static async Task<bool> EntityExists(WorldDbContext db, string entityType, int entityId)
    {
        return entityType switch
        {
            "locations" => await db.Locations.AnyAsync(l => l.Id == entityId),
            "items" => await db.Items.AnyAsync(i => i.Id == entityId),
            "monsters" => await db.Monsters.AnyAsync(m => m.Id == entityId),
            "secretstashes" => await db.SecretStashes.AnyAsync(s => s.Id == entityId),
            "npcs" => await db.Npcs.AnyAsync(n => n.Id == entityId),
            "buildings" => await db.Buildings.AnyAsync(b => b.Id == entityId),
            "characters" => await db.Characters.AnyAsync(c => c.Id == entityId),
            "kingdoms" => await db.Kingdoms.AnyAsync(k => k.Id == entityId),
            "spells" => await db.Spells.AnyAsync(s => s.Id == entityId),
            _ => false
        };
    }
}
