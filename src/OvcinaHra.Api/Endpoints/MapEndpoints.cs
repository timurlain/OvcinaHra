using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

/// <summary>
/// Issue #207 — Map page (cartographer's workshop) data endpoints.
/// Two routes: aggregated map data for the page-level render and a
/// per-location peek for the right slide-in panel. Both are read-only,
/// game-scoped, and intentionally avoid the encounter / quest-path
/// surfaces (no schema for ordered location waypoints today — see
/// MapDtos.cs scope note).
/// </summary>
public static class MapEndpoints
{
    public static RouteGroupBuilder MapMapEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/map").WithTags("Map");

        group.MapGet("/data", GetMapData);
        group.MapGet("/locations/{id:int}/peek", GetLocationPeek);

        return group;
    }

    private static async Task<Ok<MapDataDto>> GetMapData(
        int gameId, WorldDbContext db, IBlobStorageService blob)
    {
        // Game-scoped locations. Only parent locations get pinned —
        // variants share their parent's GPS and would stack identical
        // markers on top of each other.
        var locationRows = await db.GameLocations
            .Where(gl => gl.GameId == gameId
                && gl.Location.ParentLocationId == null
                && gl.Location.Coordinates != null)
            .Select(gl => new
            {
                gl.LocationId,
                gl.Location.Name,
                gl.Location.LocationKind,
                Lat = gl.Location.Coordinates!.Latitude,
                Lon = gl.Location.Coordinates!.Longitude,
                gl.Location.ImagePath,
            })
            .ToListAsync();

        var locations = locationRows.Select(r => new MapLocationDto(
            r.LocationId, r.Name,
            (double)r.Lat, (double)r.Lon,
            r.LocationKind,
            // Kingdom not stored on Location/GameLocation today. Return
            // null so the cockpit's kingdom-tint chip stays inert until
            // a follow-up adds the relationship.
            KingdomId: null, KingdomName: null,
            ThumbnailUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : blob.GetSasUrl(r.ImagePath))).ToList();

        // Game-scoped stashes. Each pin sits at GameSecretStash.LocationId
        // (which has GPS via Location.Coordinates). Treasure count + highest
        // stage drive the badge color on the client.
        var stashRows = await db.GameSecretStashes
            .Where(gss => gss.GameId == gameId
                && gss.Location.Coordinates != null)
            .Select(gss => new
            {
                gss.SecretStashId,
                gss.LocationId,
                gss.SecretStash.Name,
                Lat = gss.Location.Coordinates!.Latitude,
                Lon = gss.Location.Coordinates!.Longitude,
            })
            .ToListAsync();

        // Pull treasures attached to each stash for this game so the count
        // + highest-stage badge are accurate. One query keeps it cheap.
        var treasureRows = await db.TreasureQuests
            .Where(t => t.GameId == gameId && t.SecretStashId != null)
            .Select(t => new { t.SecretStashId, t.Difficulty })
            .ToListAsync();
        var byStash = treasureRows
            .GroupBy(t => t.SecretStashId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var stashes = stashRows.Select(r =>
        {
            var entries = byStash.GetValueOrDefault(r.SecretStashId);
            var count = entries?.Count ?? 0;
            GameTimePhase? highest = entries is { Count: > 0 }
                ? entries.Max(e => e.Difficulty)
                : null;
            return new MapStashDto(
                r.SecretStashId, r.LocationId, r.Name,
                (double)r.Lat, (double)r.Lon, count, highest);
        }).ToList();

        return TypedResults.Ok(new MapDataDto(locations, stashes));
    }

    private static async Task<Results<Ok<LocationPeekDto>, NotFound>> GetLocationPeek(
        int id, int gameId, WorldDbContext db, IBlobStorageService blob)
    {
        // Project Coordinates components directly — projecting the owned
        // GpsCoordinates type into an anonymous wrapper trips EF's tracking
        // query rule (owned entity without owner). Pulling Latitude /
        // Longitude into nullable decimals sidesteps it cleanly.
        var loc = await db.Locations
            .Where(l => l.Id == id)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.LocationKind,
                l.Description,
                l.ImagePath,
                Lat = l.Coordinates != null ? (decimal?)l.Coordinates.Latitude : null,
                Lon = l.Coordinates != null ? (decimal?)l.Coordinates.Longitude : null,
            })
            .FirstOrDefaultAsync();
        if (loc is null) return TypedResults.NotFound();

        // Stashes for this location in this game.
        var stashes = await db.GameSecretStashes
            .Where(gss => gss.GameId == gameId && gss.LocationId == id)
            .Select(gss => new
            {
                gss.SecretStashId,
                gss.SecretStash.Name,
                TreasureCount = db.TreasureQuests
                    .Count(t => t.GameId == gameId && t.SecretStashId == gss.SecretStashId),
            })
            .ToListAsync();
        var stashDtos = stashes
            .Select(s => new LocationPeekStashDto(s.SecretStashId, s.Name, s.TreasureCount))
            .ToList();

        // Treasures attached to this location (directly via LocationId or
        // via one of its stashes). Group by stage.
        var treasureRows = await db.TreasureQuests
            .Where(t => t.GameId == gameId
                && (t.LocationId == id
                    || (t.SecretStashId != null
                        && db.GameSecretStashes.Any(gss =>
                            gss.GameId == gameId
                            && gss.SecretStashId == t.SecretStashId
                            && gss.LocationId == id))))
            .Select(t => new { t.Id, t.Title, t.Difficulty })
            .ToListAsync();

        // Render all four stages so the peek's layout stays stable even
        // when some buckets are empty (the cockpit shows "Žádné poklady").
        // The peek's "treasures by stage" explorer renders the four
        // gameplay phases in canonical order. EndGame (post-climax wrap)
        // is intentionally excluded — treasures don't get planted that
        // late and the explorer would always show an empty fifth row.
        var stagesInOrder = new[]
        {
            GameTimePhase.Start, GameTimePhase.Early,
            GameTimePhase.Midgame, GameTimePhase.Lategame,
        };
        var byStage = treasureRows.GroupBy(t => t.Difficulty)
            .ToDictionary(g => g.Key, g => g.ToList());
        var stageRows = stagesInOrder.Select(stage =>
        {
            var entries = byStage.GetValueOrDefault(stage) ?? [];
            return new TreasuresByStageRowDto(
                stage,
                entries.Count,
                entries.Select(e => new TreasuresByStageItemDto(e.Id, e.Title)).ToList());
        }).ToList();

        var lorePreview = MakeLorePreview(loc.Description);

        return TypedResults.Ok(new LocationPeekDto(
            loc.Id, loc.Name,
            string.IsNullOrWhiteSpace(loc.ImagePath) ? null : blob.GetSasUrl(loc.ImagePath),
            (double)(loc.Lat ?? 0m), (double)(loc.Lon ?? 0m),
            KingdomId: null, KingdomName: null,
            loc.LocationKind, loc.LocationKind.ToString(),
            stashDtos,
            stageRows,
            lorePreview));
    }

    /// <summary>
    /// First two sentences of <see cref="Location.Description"/>, capped
    /// at ~240 chars so the peek panel doesn't have to scroll for the
    /// "lore preview" surface.
    /// </summary>
    private static string? MakeLorePreview(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var trimmed = description.Trim();
        var firstTwoSentences = string.Concat(
            trimmed.Split(['.', '!', '?'], 3, StringSplitOptions.None)
                .Take(2)
                .Select(s => s.Trim() + "."));
        return firstTwoSentences.Length > 240
            ? firstTwoSentences[..240].TrimEnd() + "…"
            : firstTwoSentences;
    }
}
