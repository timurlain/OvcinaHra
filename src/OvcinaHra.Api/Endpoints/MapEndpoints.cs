using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

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
        int gameId, IMapDataService mapData, CancellationToken ct)
        => TypedResults.Ok(await mapData.GetMapDataAsync(gameId, ct));

    private static async Task<Results<Ok<LocationPeekDto>, NotFound>> GetLocationPeek(
        int id, int gameId, WorldDbContext db, IBlobStorageService blob)
    {
        // Game-scoped lookup. Joining through GameLocations enforces that
        // the location is actually part of the requested game — pre-fixup
        // any caller knowing a valid (gameId, locationId) pair could peek
        // an arbitrary location. Coordinates components are projected as
        // nullable decimals so the owned GpsCoordinates value object
        // doesn't trip EF's tracking-without-owner rule.
        var loc = await db.GameLocations
            .Where(gl => gl.GameId == gameId
                && gl.LocationId == id
                && gl.Location.ParentLocationId == null)
            .Select(gl => new
            {
                gl.Location.Id,
                gl.Location.Name,
                gl.Location.LocationKind,
                gl.Location.Description,
                gl.Location.ImagePath,
                Lat = gl.Location.Coordinates != null ? (decimal?)gl.Location.Coordinates.Latitude : null,
                Lon = gl.Location.Coordinates != null ? (decimal?)gl.Location.Coordinates.Longitude : null,
            })
            .FirstOrDefaultAsync();
        if (loc is null) return TypedResults.NotFound();
        // GPS-less location → 404 rather than (0,0) which would render
        // the pin / centering at the Gulf of Guinea (Copilot C6).
        if (loc.Lat is null || loc.Lon is null) return TypedResults.NotFound();

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

        // Children / variants — sub-locations of this parent. They never
        // get pins on the map (parent-only rule) but the peek lists them
        // so the organizer can navigate to a variant via /locations/{id}.
        var children = await db.Locations
            .Where(l => l.ParentLocationId == id)
            .OrderBy(l => l.Name)
            .Select(l => new LocationPeekChildDto(l.Id, l.Name, l.LocationKind))
            .ToListAsync();

        return TypedResults.Ok(new LocationPeekDto(
            loc.Id, loc.Name,
            string.IsNullOrWhiteSpace(loc.ImagePath) ? null : blob.GetSasUrl(loc.ImagePath),
            (double)loc.Lat!.Value, (double)loc.Lon!.Value,
            KingdomId: null, KingdomName: null,
            // Use the localized [Display] label so the peek chip reads
            // "Pustina" not "Wilderness" (Copilot C5).
            loc.LocationKind, loc.LocationKind.GetDisplayName(),
            stashDtos,
            stageRows,
            lorePreview,
            children));
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
