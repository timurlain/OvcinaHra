using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Services;

public interface IMapDataService
{
    Task<MapDataDto> GetMapDataAsync(int gameId, CancellationToken ct = default);
}

public sealed class MapDataService(WorldDbContext db, IBlobStorageService blob) : IMapDataService
{
    public async Task<MapDataDto> GetMapDataAsync(int gameId, CancellationToken ct = default)
    {
        // Game-scoped locations. Only parent locations get pinned — variants
        // share their parent's GPS and would stack identical markers.
        var locationRows = await db.GameLocations
            .AsNoTracking()
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
                HobbitChildName = db.Locations
                    .Where(child => child.ParentLocationId == gl.LocationId
                        && child.LocationKind == LocationKind.Hobbit)
                    .OrderBy(child => child.Id)
                    .Select(child => child.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var locations = locationRows.Select(r => new MapLocationDto(
            r.LocationId, r.Name,
            (double)r.Lat, (double)r.Lon,
            r.LocationKind,
            // Kingdom not stored on Location/GameLocation today. Return null
            // so the cockpit's kingdom-tint chip stays inert until a follow-up
            // adds the relationship.
            KingdomId: null, KingdomName: null,
            ThumbnailUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : blob.GetSasUrl(r.ImagePath),
            RenderKind: r.HobbitChildName is null ? null : LocationKind.Hobbit,
            RenderName: r.HobbitChildName)).ToList();

        // Game-scoped stashes. Each pin sits at GameSecretStash.LocationId
        // (which has GPS via Location.Coordinates). Treasure count + highest
        // stage drive the badge color on the client.
        var stashRows = await db.GameSecretStashes
            .AsNoTracking()
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
            .ToListAsync(ct);

        // Pull treasures attached to each stash for this game so the count +
        // highest-stage badge are accurate. One query keeps it cheap.
        var treasureRows = await db.TreasureQuests
            .AsNoTracking()
            .Where(t => t.GameId == gameId && t.SecretStashId != null)
            .Select(t => new { t.SecretStashId, t.Difficulty })
            .ToListAsync(ct);
        var byStash = treasureRows
            .GroupBy(t => t.SecretStashId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var stashes = stashRows.Select(r =>
        {
            var entries = byStash.GetValueOrDefault(r.SecretStashId);
            return new MapStashDto(
                r.SecretStashId, r.LocationId, r.Name,
                (double)r.Lat, (double)r.Lon,
                TreasureCount: entries?.Count ?? 0,
                HighestStage: entries is null || entries.Count == 0
                    ? null
                    : entries.Max(t => t.Difficulty));
        }).ToList();

        return new MapDataDto(locations, stashes);
    }
}
