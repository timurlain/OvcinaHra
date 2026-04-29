using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Services;

public interface IMapDataService
{
    Task<MapDataDto> GetMapDataAsync(int gameId, CancellationToken ct = default);
}

public sealed class MapDataService(
    WorldDbContext db,
    IBlobStorageService blob,
    ILogger<MapDataService> logger) : IMapDataService
{
    public async Task<MapDataDto> GetMapDataAsync(int gameId, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

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

        var parentLocationIds = locationRows.Select(r => r.LocationId).ToList();
        var childParentRows = await db.Locations
            .AsNoTracking()
            .Where(l => l.ParentLocationId.HasValue
                && parentLocationIds.Contains(l.ParentLocationId.Value))
            .Select(l => new
            {
                l.Id,
                ParentId = l.ParentLocationId!.Value,
            })
            .ToListAsync(ct);

        var parentIdByLocationId = parentLocationIds.ToDictionary(id => id, id => id);
        foreach (var child in childParentRows)
        {
            parentIdByLocationId[child.Id] = child.ParentId;
        }
        var visibleLocationIds = parentIdByLocationId.Keys.ToList();

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

        var treasureCountsByLocation = parentLocationIds
            .ToDictionary(id => id, _ => new TreasureCountAccumulator());

        if (visibleLocationIds.Count > 0)
        {
            var locationTreasureRows = await db.TreasureQuests
                .AsNoTracking()
                .Where(t => t.GameId == gameId
                    && t.LocationId != null
                    && visibleLocationIds.Contains(t.LocationId.Value))
                .Select(t => new
                {
                    t.LocationId,
                    t.Difficulty,
                    ItemCount = t.TreasureItems.Sum(ti => (int?)ti.Count) ?? 0,
                })
                .ToListAsync(ct);

            foreach (var row in locationTreasureRows)
            {
                if (row.LocationId is int locationId
                    && parentIdByLocationId.TryGetValue(locationId, out var parentId))
                {
                    treasureCountsByLocation[parentId].Add(row.Difficulty, row.ItemCount);
                }
            }
        }

        // Pull treasures attached to each stash for this game so the count +
        // highest-stage badge are accurate. One query keeps it cheap.
        var treasureRows = await db.TreasureQuests
            .AsNoTracking()
            .Where(t => t.GameId == gameId && t.SecretStashId != null)
            .Select(t => new
            {
                t.SecretStashId,
                t.Difficulty,
                ItemCount = t.TreasureItems.Sum(ti => (int?)ti.Count) ?? 0,
            })
            .ToListAsync(ct);

        var parentIdByStashId = stashRows
            .Where(r => parentIdByLocationId.ContainsKey(r.LocationId))
            .ToDictionary(r => r.SecretStashId, r => parentIdByLocationId[r.LocationId]);

        foreach (var row in treasureRows)
        {
            if (row.SecretStashId is int stashId
                && parentIdByStashId.TryGetValue(stashId, out var parentId))
            {
                treasureCountsByLocation[parentId].Add(row.Difficulty, row.ItemCount);
            }
        }

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
            RenderName: r.HobbitChildName is null
                ? null
                : $"{r.HobbitChildName} ({r.Name})",
            TreasureCounts: treasureCountsByLocation.GetValueOrDefault(r.LocationId)?.ToDto()
                ?? TreasureCountAccumulator.Empty.ToDto())).ToList();

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

        logger.LogInformation(
            "[treasure-map] count-data loaded gameId={GameId} locationCount={LocationCount} elapsedMs={ElapsedMs}",
            gameId,
            locations.Count,
            stopwatch.ElapsedMilliseconds);

        return new MapDataDto(locations, stashes);
    }

    private sealed class TreasureCountAccumulator
    {
        public static TreasureCountAccumulator Empty { get; } = new();

        private int Total { get; set; }
        private int Start { get; set; }
        private int Early { get; set; }
        private int Midgame { get; set; }
        private int Lategame { get; set; }

        public void Add(GameTimePhase phase, int count)
        {
            if (count <= 0) return;

            Total += count;
            switch (phase)
            {
                case GameTimePhase.Start:
                    Start += count;
                    break;
                case GameTimePhase.Early:
                    Early += count;
                    break;
                case GameTimePhase.Midgame:
                    Midgame += count;
                    break;
                case GameTimePhase.Lategame:
                case GameTimePhase.EndGame:
                    Lategame += count;
                    break;
            }
        }

        public TreasureCountByPhaseDto ToDto() => new(Total, Start, Early, Midgame, Lategame);
    }
}
