using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ImageEndpoints
{
    private static readonly HashSet<string> ValidEntityTypes = ["locations", "items", "monsters", "secretstashes", "npcs", "buildings"];
    private static readonly HashSet<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/images").WithTags("Images");

        group.MapPost("/{entityType}/{entityId:int}", Upload).DisableAntiforgery();
        group.MapGet("/{entityType}/{entityId:int}", GetUrls);
        group.MapDelete("/{entityType}/{entityId:int}", Delete);

        return group;
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
            _ => false
        };
    }
}
