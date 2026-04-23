using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ImageEndpoints
{
    // "locationstamps" is a logical alias keyed by Location.Id that reads /
    // writes Location.StampImagePath. Exposed as its own entity type so that
    // the thumbnail cache, pre-gen, and backfill pipelines treat the rubber
    // stamp as an independent image (its cache keys don't collide with the
    // main location image). See GetEntityImagePaths and the Upload DbContext
    // switch for the dispatch.
    private static readonly HashSet<string> ValidEntityTypes = ["locations", "locationstamps", "items", "monsters", "secretstashes", "npcs", "buildings", "characters", "kingdoms", "spells"];
    private static readonly HashSet<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/images").WithTags("Images");

        group.MapPost("/{entityType}/{entityId:int}", Upload).DisableAntiforgery();
        group.MapGet("/{entityType}/{entityId:int}", GetUrls);
        group.MapGet("/{entityType}/{entityId:int}/thumb", GetThumbnail).AllowAnonymous();
        group.MapDelete("/{entityType}/{entityId:int}", Delete);
        group.MapPost("/backfill-thumbs", BackfillThumbs);

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

        // Hot-cache path: the common case after the first visitor for a given
        // (entity, preset). Return a 302 redirect to the cached thumb's SAS URL
        // so the browser fetches the bytes straight from Azure Blob and the API
        // is out of the bandwidth path entirely. Prevents the 503 storm when a
        // page like /locations fires 80+ parallel thumbnail requests at once.
        //
        // Cache-Control on the redirect itself tells the browser to reuse the
        // 302 response for an hour — without it the browser re-issues the same
        // GET on every navigation, and the rolling 1 h SAS means the Location
        // header differs each time, so the browser can't reuse the downstream
        // blob bytes either. One hour keeps us safely under the SAS expiry.
        var cachedSasUrl = await thumbnailService.TryGetCachedSasUrlAsync(entityType, entityId, preset, ct);
        if (cachedSasUrl is not null)
        {
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return TypedResults.Redirect(cachedSasUrl, permanent: false);
        }

        // Cold-cache path: resize + cache (throttled inside the service). On
        // first-ever generation we stream the bytes inline — the cache will be
        // populated for subsequent requests which then hit the hot path above.
        var result = await thumbnailService.GetOrCreateAsync(entityType, entityId, imagePath, preset, ct);

        if (result is null)
        {
            // Graceful degrade — redirect to the full-res SAS so the user still
            // sees something when resize fails (missing source blob, corrupt
            // image, ImageSharp exception, etc.).
            var fallback = blobService.GetSasUrl(imagePath);
            if (fallback is null)
                return TypedResults.NotFound();
            return TypedResults.Redirect(fallback, permanent: false);
        }

        // Cold-path: tell the browser to cache for 24 h so we don't churn the
        // resize work on refreshes. Upload path invalidates the server cache;
        // browser caches only refresh after max-age or a hard reload.
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return TypedResults.File(result.Bytes, result.ContentType);
    }

    private static async Task<Results<Ok<ImageUploadResult>, NotFound, BadRequest<string>>> Upload(
        string entityType, int entityId, IFormFile file, IBlobStorageService blobService,
        IThumbnailService thumbnailService, ILogger<ThumbnailService> thumbLogger,
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
        {
            await blobService.DeletePrefixAsync($"{entityType}-thumbs/{entityId}-");
            // Fire-and-forget pre-generation of all presets so the first
            // list-page viewer hits the hot-cache 302-redirect path instead of
            // paying cold resize cost. Upload response returns immediately;
            // generation runs on a background task. Discard (_ =) silences
            // CS4014 without a pragma.
            _ = PreGenerateAllPresetsAsync(thumbnailService, thumbLogger, entityType, entityId, blobKey);
        }

        await UpdateEntityImagePath(db, entityType, entityId, blobKey, isPlacement);

        var url = await blobService.GetSasUrlAsync(blobKey);
        return TypedResults.Ok(new ImageUploadResult(blobKey, url));
    }

    /// <summary>
    /// Kicks off a background task that pre-generates every thumbnail preset
    /// for a freshly-uploaded source image. Fire-and-forget; failures are
    /// logged but don't propagate — the on-demand thumbnail endpoint is still
    /// the fallback. Wraps the whole body in try/catch so an unobserved
    /// exception can't crash the host.
    /// </summary>
    private static async Task PreGenerateAllPresetsAsync(
        IThumbnailService thumbnailService, ILogger logger,
        string entityType, int entityId, string sourceBlobKey)
    {
        // No Task.Run — the work is I/O-bound (blob download/upload around a
        // short CPU resize burst that already runs inside a SemaphoreSlim(4)
        // in ThumbnailService). Wrapping in Task.Run would burn a thread-pool
        // thread per upload and let a burst of uploads spawn unbounded work.
        // Fire-and-forget via `_ =` at the caller means this Task runs on the
        // current sync context, yielding to the pool only on await.
        try
        {
            foreach (var preset in Enum.GetValues<ThumbnailPreset>())
            {
                try
                {
                    await thumbnailService.GetOrCreateAsync(entityType, entityId, sourceBlobKey, preset);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Thumbnail pre-generation failed for {EntityType}/{EntityId} preset {Preset}",
                        entityType, entityId, preset);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Thumbnail pre-generation task failed for {EntityType}/{EntityId}",
                entityType, entityId);
        }
    }

    /// <summary>
    /// Manual trigger to re-run the same backfill sweep the
    /// <c>ThumbnailBackfillHostedService</c> performs on container startup.
    /// Useful after bulk imports or whenever the hosted service didn't cover
    /// an entity.
    ///
    /// Inherits the group's authorization — **any authenticated user** can
    /// call it. This is not gated behind an admin role today; every OvčinaHra
    /// user is an organizer. If a finer gate is needed later, add a policy
    /// here via <c>.RequireAuthorization(policy)</c>.
    ///
    /// Runs **synchronously** (the whole sweep awaits before returning) and
    /// can take minutes for a large catalogue, so call it from a tool that
    /// tolerates long response times. Returns <c>{ processed: N }</c> — the
    /// number of (entity, preset) pairs the service walked (already-cached
    /// thumbs short-circuit inside <c>GetOrCreateAsync</c> and still count
    /// toward the total).
    /// </summary>
    private static async Task<Ok<object>> BackfillThumbs(
        IThumbnailService thumbnailService, WorldDbContext db,
        ILogger<ThumbnailService> logger, CancellationToken ct)
    {
        var processed = await BackfillAllThumbsAsync(db, thumbnailService, logger, ct);
        return TypedResults.Ok<object>(new { processed });
    }

    /// <summary>
    /// Walks every entity table with an image column, and for every row with
    /// a non-empty image path, calls <see cref="IThumbnailService.GetOrCreateAsync"/>
    /// for every <see cref="ThumbnailPreset"/>. The service short-circuits
    /// when the thumb already exists (ExistsAsync HEAD probe), so re-running
    /// this is idempotent and cheap. Returns the number of (entity, preset)
    /// pairs attempted. Shared between the hosted service and the admin
    /// endpoint to avoid duplicate logic.
    /// </summary>
    internal static async Task<int> BackfillAllThumbsAsync(
        WorldDbContext db, IThumbnailService thumbnailService,
        ILogger logger, CancellationToken ct)
    {
        // (entityType, id, blobKey) triples — pulled up front so we're not
        // holding a long-lived DB cursor across network-bound thumbnail work.
        var targets = new List<(string EntityType, int Id, string BlobKey)>();

        foreach (var (entityType, id, blobKey) in await db.Locations
            .Where(l => l.ImagePath != null && l.ImagePath != "")
            .Select(l => ValueTuple.Create("locations", l.Id, l.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        // Rubber-stamp image on Location is surfaced as the "locationstamps"
        // entity-type alias; see the comment on ValidEntityTypes.
        foreach (var (entityType, id, blobKey) in await db.Locations
            .Where(l => l.StampImagePath != null && l.StampImagePath != "")
            .Select(l => ValueTuple.Create("locationstamps", l.Id, l.StampImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.Items
            .Where(i => i.ImagePath != null && i.ImagePath != "")
            .Select(i => ValueTuple.Create("items", i.Id, i.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.Monsters
            .Where(m => m.ImagePath != null && m.ImagePath != "")
            .Select(m => ValueTuple.Create("monsters", m.Id, m.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.SecretStashes
            .Where(s => s.ImagePath != null && s.ImagePath != "")
            .Select(s => ValueTuple.Create("secretstashes", s.Id, s.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.Npcs
            .Where(n => n.ImagePath != null && n.ImagePath != "")
            .Select(n => ValueTuple.Create("npcs", n.Id, n.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.Buildings
            .Where(b => b.ImagePath != null && b.ImagePath != "")
            .Select(b => ValueTuple.Create("buildings", b.Id, b.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.Characters
            .Where(c => c.ImagePath != null && c.ImagePath != "")
            .Select(c => ValueTuple.Create("characters", c.Id, c.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        // Kingdom.BadgeImageUrl is the blob-key field — the column name is
        // legacy tech debt (see MEMORY feedback_ovcinahra_kingdom_badgeimageurl_is_blob_key).
        foreach (var (entityType, id, blobKey) in await db.Kingdoms
            .Where(k => k.BadgeImageUrl != null && k.BadgeImageUrl != "")
            .Select(k => ValueTuple.Create("kingdoms", k.Id, k.BadgeImageUrl!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        foreach (var (entityType, id, blobKey) in await db.Spells
            .Where(s => s.ImagePath != null && s.ImagePath != "")
            .Select(s => ValueTuple.Create("spells", s.Id, s.ImagePath!))
            .ToListAsync(ct))
            targets.Add((entityType, id, blobKey));

        var presets = Enum.GetValues<ThumbnailPreset>();
        var attempted = 0;
        var done = 0;
        // Entity-type count is derived from the actual targets so the log stays
        // accurate if ValidEntityTypes grows or a table happens to be empty.
        var entityTypeCount = targets.Select(t => t.EntityType).Distinct().Count();
        logger.LogInformation(
            "Thumbnail backfill starting: {RowCount} entities across {EntityTypeCount} types, {PresetCount} presets each",
            targets.Count, entityTypeCount, presets.Length);

        foreach (var (entityType, id, blobKey) in targets)
        {
            if (ct.IsCancellationRequested)
            {
                logger.LogInformation("Thumbnail backfill cancelled at {Done}/{Total} rows", done, targets.Count);
                break;
            }

            foreach (var preset in presets)
            {
                try
                {
                    await thumbnailService.GetOrCreateAsync(entityType, id, blobKey, preset, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Thumbnail backfill failed for {EntityType}/{EntityId} preset {Preset}",
                        entityType, id, preset);
                }
                attempted++;
            }
            done++;

            // Log every 50 rows so a fresh-deploy backfill (~200+ rows) shows
            // visible progress in the container logs.
            if (done % 50 == 0)
            {
                logger.LogInformation("Thumbnail backfill progress: {Done}/{Total} rows", done, targets.Count);
            }
        }

        logger.LogInformation(
            "Thumbnail backfill complete: {Attempted} (entity, preset) pairs attempted across {Done}/{Total} rows",
            attempted, done, targets.Count);
        return attempted;
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
            case "locationstamps":
                // Logical alias for Location.StampImagePath; ignores isPlacement
                // because the rubber stamp has no placement variant.
                var locationForStamp = await db.Locations.FindAsync(entityId);
                if (locationForStamp is not null)
                    locationForStamp.StampImagePath = blobKey;
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
            "locationstamps" => await db.Locations.Where(l => l.Id == entityId)
                .Select(l => new ValueTuple<string?, string?>(l.StampImagePath, null))
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
            "locationstamps" => await db.Locations.AnyAsync(l => l.Id == entityId),
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
