using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class LocationEndpoints
{
    private const int LocationInheritanceMaxDepth = 3;

    internal sealed record LocationCoordinateState(
        int Id,
        decimal? Latitude,
        decimal? Longitude,
        int? ParentLocationId);

    public static RouteGroupBuilder MapLocationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/locations").WithTags("Locations");

        group.MapGet("/", GetAll);
        group.MapGet("/nearby", GetNearby);
        group.MapGet("/{id:int}", GetById);
        group.MapGet("/{id:int}/quests", GetQuests);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);
        group.MapPost("/{id:int}/placement-record", RecordPlacement);

        // GameLocation assignment
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/by-game", AssignToGame);
        group.MapDelete("/by-game/{gameId:int}/{locationId:int}", RemoveFromGame);

        // Issue #252 — drag-drop coordinate patch. Mounted last so the
        // route table reads top-to-bottom in lifecycle order (CRUD →
        // queries → game-link → narrow patches).
        group.MapPatch("/{id:int}/coordinates", PatchCoordinates);

        return group;
    }

    private static async Task<Ok<List<LocationListDto>>> GetAll(WorldDbContext db, HttpContext http)
    {
        var rows = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.LocationKind,
                l.Region,
                Latitude = l.Coordinates != null ? l.Coordinates.Latitude : (decimal?)null,
                Longitude = l.Coordinates != null ? l.Coordinates.Longitude : (decimal?)null,
                l.ParentLocationId,
                l.Description,
                l.Details,
                l.GamePotential,
                l.NpcInfo,
                l.SetupNotes,
                l.ImagePath,
                l.PlacementPhotoPath,
                l.StampImagePath
            })
            .ToListAsync();

        var lookup = await BuildLocationStateLookupAsync(
            db,
            rows.Select(r => new LocationCoordinateState(r.Id, r.Latitude, r.Longitude, r.ParentLocationId)));

        var locations = rows.Select(r =>
        {
            var effective = GetEffectiveCoordinates(lookup, r.Id);
            return new LocationListDto(
                r.Id, r.Name, r.LocationKind,
                r.Region,
                r.Latitude,
                r.Longitude,
                r.ParentLocationId,
                r.Description, r.Details, r.GamePotential, r.NpcInfo, r.SetupNotes,
                Array.Empty<LocationStashDto>(),
                Array.Empty<LocationQuestDto>(),
                Array.Empty<LocationTreasureQuestDto>(),
                ImagePath: r.ImagePath,
                ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "locations", r.Id, "small"),
                PlacementPhotoPath: r.PlacementPhotoPath,
                PlacementPhotoUrl: string.IsNullOrWhiteSpace(r.PlacementPhotoPath) ? null : ImageEndpoints.ThumbUrl(http, "locationplacements", r.Id, "small"),
                StampImagePath: r.StampImagePath,
                StampImageUrl: string.IsNullOrWhiteSpace(r.StampImagePath) ? null : ImageEndpoints.ThumbUrl(http, "locationstamps", r.Id, "small"),
                IsLocated: effective.HasValue,
                EffectiveLatitude: effective?.Latitude,
                EffectiveLongitude: effective?.Longitude);
        }).ToList();

        return TypedResults.Ok(locations);
    }

    /// <summary>
    /// Locations within haversine radius of [lat, lng]. Used by the
    /// LocationDetail orientation map (issue #110) to render nearby name
    /// labels. Results cap at 50, ordered by distance. Optional excludeId
    /// drops the subject location itself so callers don't see their own
    /// pin twice.
    /// </summary>
    private static async Task<Results<Ok<List<NearbyLocationDto>>, BadRequest<string>>> GetNearby(
        double lat, double lng, WorldDbContext db,
        double radiusKm = 5.0, int? excludeId = null)
    {
        if (radiusKm <= 0 || radiusKm > 200)
            return TypedResults.BadRequest("radiusKm must be between 0 (exclusive) and 200.");
        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
            return TypedResults.BadRequest("lat/lng out of range.");

        // Rough pre-filter via a lat/lng bounding window to keep the
        // in-memory haversine pass small. 1° latitude ≈ 111 km everywhere;
        // 1° longitude ≈ 111*cos(lat) km — narrower near the poles.
        var latDelta = (decimal)(radiusKm / 111.0);
        var cosLat = Math.Max(0.01, Math.Cos(lat * Math.PI / 180.0));
        var lngDelta = (decimal)(radiusKm / (111.0 * cosLat));

        var latM = (decimal)lat;
        var lngM = (decimal)lng;
        var swLat = latM - latDelta;
        var neLat = latM + latDelta;
        var swLng = lngM - lngDelta;
        var neLng = lngM + lngDelta;

        var candidates = await db.Locations
            .Where(l => l.Coordinates != null
                && l.Coordinates.Latitude >= swLat && l.Coordinates.Latitude <= neLat
                && l.Coordinates.Longitude >= swLng && l.Coordinates.Longitude <= neLng
                && (excludeId == null || l.Id != excludeId.Value))
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.LocationKind,
                Latitude = l.Coordinates!.Latitude,
                Longitude = l.Coordinates!.Longitude
            })
            .ToListAsync();

        var results = candidates
            .Select(c => new NearbyLocationDto(c.Id, c.Name, c.LocationKind, c.Latitude, c.Longitude,
                HaversineKm(lat, lng, (double)c.Latitude, (double)c.Longitude)))
            .Where(d => d.DistanceKm <= radiusKm)
            .OrderBy(d => d.DistanceKm)
            .Take(50)
            .ToList();

        return TypedResults.Ok(results);
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthKm = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthKm * c;
    }

    private static async Task<Results<Ok<LocationDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var loc = await db.Locations
            .Include(l => l.Variants)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (loc is null)
            return TypedResults.NotFound();

        var variants = loc.Variants.Select(v => new LocationVariantDto(v.Id, v.Name, v.LocationKind)).ToList();
        decimal? latitude = loc.ParentLocationId.HasValue ? null : loc.Coordinates?.Latitude;
        decimal? longitude = loc.ParentLocationId.HasValue ? null : loc.Coordinates?.Longitude;

        return TypedResults.Ok(new LocationDetailDto(
            loc.Id, loc.Name, loc.Description, loc.Details, loc.GamePotential, loc.Prompt, loc.Region, loc.LocationKind,
            latitude, longitude,
            loc.ImagePath, loc.PlacementPhotoPath, loc.StampImagePath, loc.NpcInfo, loc.SetupNotes,
            loc.ParentLocationId, variants));
    }

    private static async Task<Results<Ok<List<LocationQuestSummaryDto>>, NotFound>> GetQuests(
        int id, WorldDbContext db, int? gameId = null)
    {
        var locationExists = await db.Locations.AnyAsync(l => l.Id == id);
        if (!locationExists)
            return TypedResults.NotFound();

        var quests = await db.QuestLocationLinks
            .Where(ql => ql.LocationId == id
                && (gameId == null || ql.Quest.GameId == null || ql.Quest.GameId == gameId))
            .OrderBy(ql => ql.Quest.Name)
            .Select(ql => new LocationQuestSummaryDto(
                ql.QuestId,
                ql.Quest.Name,
                ql.Quest.QuestType))
            .ToListAsync();

        return TypedResults.Ok(quests);
    }

    private static async Task<Created<LocationDetailDto>> Create(CreateLocationDto dto, WorldDbContext db)
    {
        var loc = new Location
        {
            Name = dto.Name,
            LocationKind = dto.LocationKind,
            Coordinates = !dto.ParentLocationId.HasValue && dto.Latitude.HasValue && dto.Longitude.HasValue
                ? new GpsCoordinates(dto.Latitude.Value, dto.Longitude.Value) : null,
            Description = dto.Description,
            Details = dto.Details,
            GamePotential = dto.GamePotential,
            Prompt = dto.Prompt,
            Region = dto.Region,
            NpcInfo = dto.NpcInfo,
            SetupNotes = dto.SetupNotes,
            ParentLocationId = dto.ParentLocationId
        };

        db.Locations.Add(loc);
        await db.SaveChangesAsync();

        var result = new LocationDetailDto(
            loc.Id, loc.Name, loc.Description, loc.Details, loc.GamePotential, loc.Prompt, loc.Region, loc.LocationKind,
            loc.Coordinates?.Latitude, loc.Coordinates?.Longitude,
            loc.ImagePath, loc.PlacementPhotoPath, loc.StampImagePath, loc.NpcInfo, loc.SetupNotes,
            loc.ParentLocationId, []);

        return TypedResults.Created($"/api/locations/{loc.Id}", result);
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateLocationDto dto, WorldDbContext db)
    {
        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
            return TypedResults.NotFound();

        loc.Name = dto.Name;
        loc.LocationKind = dto.LocationKind;
        loc.Coordinates = !dto.ParentLocationId.HasValue && dto.Latitude.HasValue && dto.Longitude.HasValue
            ? new GpsCoordinates(dto.Latitude.Value, dto.Longitude.Value) : null;
        loc.Description = dto.Description;
        loc.Details = dto.Details;
        loc.GamePotential = dto.GamePotential;
        loc.Prompt = dto.Prompt;
        loc.Region = dto.Region;
        loc.NpcInfo = dto.NpcInfo;
        loc.SetupNotes = dto.SetupNotes;
        loc.ParentLocationId = dto.ParentLocationId;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
            return TypedResults.NotFound();

        db.Locations.Remove(loc);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<LocationPlacementStatusDto>, NotFound, ProblemHttpResult>> RecordPlacement(
        int id,
        LocationPlacementRecordRequest dto,
        WorldDbContext db,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var organizer = GetOrganizer(http.User);
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationEndpoints");
        logger.LogInformation(
            "[loc-placement] placement.record.entry gameId={GameId} locationId={LocationId} userId={UserId} imageBlobUrl={ImageBlobUrl}",
            dto.GameId,
            id,
            organizer.UserId,
            null);

        try
        {
            var location = await db.Locations
                .Where(l => l.Id == id && l.GameLocations.Any(gl => gl.GameId == dto.GameId))
                .FirstOrDefaultAsync(ct);
            if (location is null)
            {
                logger.LogInformation(
                    "[loc-placement] placement.record.not-found gameId={GameId} locationId={LocationId} userId={UserId} imageBlobUrl={ImageBlobUrl}",
                    dto.GameId,
                    id,
                    organizer.UserId,
                    null);
                return TypedResults.NotFound();
            }

            if (string.IsNullOrWhiteSpace(location.PlacementPhotoPath))
            {
                logger.LogInformation(
                    "[loc-placement] placement.record.missing-photo gameId={GameId} locationId={LocationId} userId={UserId} imageBlobUrl={ImageBlobUrl}",
                    dto.GameId,
                    id,
                    organizer.UserId,
                    null);
                return PlacementProblem("Nejprve nahrajte fotografii umístění.");
            }

            var nowUtc = DateTime.UtcNow;
            var localTimestamp = NormalizeLocalTimestamp(dto.LocalTimestampText, nowUtc);
            var notes = dto.Notes?.Trim();
            if (!string.IsNullOrWhiteSpace(notes))
            {
                var noteBlock = $"[{localTimestamp} {organizer.Name}] {notes}";
                location.SetupNotes = string.IsNullOrWhiteSpace(location.SetupNotes)
                    ? noteBlock
                    : $"{noteBlock}{Environment.NewLine}----{Environment.NewLine}{location.SetupNotes}";
            }

            var activity = new WorldActivity
            {
                GameId = dto.GameId,
                TimestampUtc = nowUtc,
                OrganizerUserId = organizer.UserId,
                OrganizerName = organizer.Name,
                ActivityType = WorldActivityType.LocationPlaced,
                Description = $"Umístěna lokace: {location.Name}",
                LocationId = location.Id,
                DataJson = JsonSerializer.Serialize(new
                {
                    notes,
                    photoBlobKey = location.PlacementPhotoPath,
                    localTimestamp,
                    dto.ClientUtcOffsetMinutes
                })
            };
            db.WorldActivities.Add(activity);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "[loc-placement] placement.record.activity-inserted gameId={GameId} locationId={LocationId} userId={UserId} imageBlobUrl={ImageBlobUrl} activityId={ActivityId}",
                dto.GameId,
                id,
                organizer.UserId,
                location.PlacementPhotoPath,
                activity.Id);

            return TypedResults.Ok(new LocationPlacementStatusDto(
                location.Id,
                location.Name,
                true,
                location.PlacementPhotoPath,
                ImageEndpoints.ThumbUrl(http, "locationplacements", location.Id, "small"),
                location.SetupNotes,
                activity.TimestampUtc,
                activity.OrganizerName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "[loc-placement] placement.record.failed gameId={GameId} locationId={LocationId} userId={UserId} imageBlobUrl={ImageBlobUrl}",
                dto.GameId,
                id,
                organizer.UserId,
                null);
            throw;
        }
    }

    private static async Task<Ok<List<LocationListDto>>> GetByGame(int gameId, WorldDbContext db, HttpContext http)
    {
        var rows = await db.Locations
            .Where(l => l.GameLocations.Any(gl => gl.GameId == gameId))
            .OrderBy(l => l.Name)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.LocationKind,
                l.Region,
                Latitude = l.Coordinates != null ? l.Coordinates.Latitude : (decimal?)null,
                Longitude = l.Coordinates != null ? l.Coordinates.Longitude : (decimal?)null,
                l.ParentLocationId,
                l.Description,
                l.Details,
                l.GamePotential,
                l.NpcInfo,
                l.SetupNotes,
                l.ImagePath,
                l.PlacementPhotoPath,
                l.StampImagePath,
                Stashes = l.GameSecretStashes
                    .Where(gss => gss.GameId == gameId)
                    .OrderBy(gss => gss.SecretStash.Name)
                    .Select(gss => new
                    {
                        SecretStashId = gss.SecretStashId,
                        StashName = gss.SecretStash.Name,
                        StashImagePath = gss.SecretStash.ImagePath,
                        TreasureQuests = gss.SecretStash.TreasureQuests
                            .Where(tq => tq.GameId == gameId)
                            .OrderBy(tq => tq.Title)
                            .ThenBy(tq => tq.Id)
                            .Select(tq => new LocationTreasureQuestDto(tq.Id, tq.Title, tq.Clue, tq.Difficulty))
                            .ToList()
                    })
                    .ToList(),
                Quests = l.QuestLocations
                    .Where(ql => ql.Quest.GameId == null || ql.Quest.GameId == gameId)
                    .OrderBy(ql => ql.Quest.Name)
                    .Select(ql => new LocationQuestDto(
                        ql.QuestId,
                        ql.Quest.Name,
                        ql.Quest.QuestType,
                        ql.Quest.Description,
                        ql.Quest.FullText,
                        ql.Quest.RewardXp,
                        ql.Quest.RewardMoney,
                        ql.Quest.RewardNotes,
                        ql.Quest.QuestRewards
                            .OrderBy(qr => qr.Item.Name)
                            .Select(qr => new LocationQuestRewardItemDto(qr.ItemId, qr.Item.Name, qr.Quantity))
                            .ToList()))
                    .ToList(),
                LocationTreasureQuests = l.TreasureQuests
                    .Where(tq => tq.GameId == gameId)
                    .OrderBy(tq => tq.Title)
                    .ThenBy(tq => tq.Id)
                    .Select(tq => new LocationTreasureQuestDto(tq.Id, tq.Title, tq.Clue, tq.Difficulty))
                    .ToList()
            })
            .ToListAsync();

        var lookup = await BuildLocationStateLookupAsync(
            db,
            rows.Select(r => new LocationCoordinateState(r.Id, r.Latitude, r.Longitude, r.ParentLocationId)));

        var dtos = rows.Select(r =>
        {
            var effective = GetEffectiveCoordinates(lookup, r.Id);
            return new LocationListDto(
                r.Id, r.Name, r.LocationKind,
                r.Region,
                r.Latitude,
                r.Longitude,
                r.ParentLocationId,
                r.Description, r.Details, r.GamePotential, r.NpcInfo, r.SetupNotes,
                Stashes: r.Stashes.Select(s => new LocationStashDto(
                    s.SecretStashId,
                    s.StashName,
                    s.TreasureQuests,
                    ImagePath: s.StashImagePath,
                    ImageUrl: string.IsNullOrWhiteSpace(s.StashImagePath) ? null : ImageEndpoints.ThumbUrl(http, "secretstashes", s.SecretStashId, "medium"))).ToList(),
                Quests: r.Quests,
                LocationTreasureQuests: r.LocationTreasureQuests,
                ImagePath: r.ImagePath,
                ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "locations", r.Id, "small"),
                PlacementPhotoPath: r.PlacementPhotoPath,
                PlacementPhotoUrl: string.IsNullOrWhiteSpace(r.PlacementPhotoPath) ? null : ImageEndpoints.ThumbUrl(http, "locationplacements", r.Id, "small"),
                StampImagePath: r.StampImagePath,
                StampImageUrl: string.IsNullOrWhiteSpace(r.StampImagePath) ? null : ImageEndpoints.ThumbUrl(http, "locationstamps", r.Id, "small"),
                IsLocated: effective.HasValue,
                EffectiveLatitude: effective?.Latitude,
                EffectiveLongitude: effective?.Longitude);
        }).ToList();

        return TypedResults.Ok(dtos);
    }

    internal static async Task<Dictionary<int, LocationCoordinateState>> BuildLocationStateLookupAsync(
        WorldDbContext db,
        IEnumerable<LocationCoordinateState> seed)
    {
        var lookup = seed.ToDictionary(s => s.Id);
        var missingParentIds = MissingParentIds(lookup.Values, lookup);

        for (var depth = 0; depth < LocationInheritanceMaxDepth && missingParentIds.Count > 0; depth++)
        {
            var parents = await db.Locations
                .Where(l => missingParentIds.Contains(l.Id))
                .Select(l => new LocationCoordinateState(
                    l.Id,
                    l.Coordinates != null ? l.Coordinates.Latitude : (decimal?)null,
                    l.Coordinates != null ? l.Coordinates.Longitude : (decimal?)null,
                    l.ParentLocationId))
                .ToListAsync();

            foreach (var parent in parents)
            {
                lookup[parent.Id] = parent;
            }

            missingParentIds = MissingParentIds(parents, lookup);
        }

        return lookup;
    }

    private static HashSet<int> MissingParentIds(
        IEnumerable<LocationCoordinateState> locations,
        IReadOnlyDictionary<int, LocationCoordinateState> lookup) =>
        locations
            .Select(l => l.ParentLocationId)
            .Where(id => id.HasValue && !lookup.ContainsKey(id.Value))
            .Select(id => id!.Value)
            .ToHashSet();

    internal static bool IsLocated(
        IReadOnlyDictionary<int, LocationCoordinateState> lookup,
        int? locationId,
        int depth = 0) =>
        GetEffectiveCoordinates(lookup, locationId, depth).HasValue;

    internal static (decimal Latitude, decimal Longitude)? GetEffectiveCoordinates(
        IReadOnlyDictionary<int, LocationCoordinateState> lookup,
        int? locationId,
        int depth = 0)
    {
        if (locationId is null
            || depth > LocationInheritanceMaxDepth
            || !lookup.TryGetValue(locationId.Value, out var row))
        {
            return null;
        }

        if (!row.ParentLocationId.HasValue && row.Latitude.HasValue && row.Longitude.HasValue)
            return (row.Latitude.Value, row.Longitude.Value);

        return GetEffectiveCoordinates(lookup, row.ParentLocationId, depth + 1);
    }

    private static async Task<Results<Created, Conflict>> AssignToGame(GameLocationDto dto, WorldDbContext db)
    {
        var exists = await db.GameLocations
            .AnyAsync(gl => gl.GameId == dto.GameId && gl.LocationId == dto.LocationId);
        if (exists)
            return TypedResults.Conflict();

        db.GameLocations.Add(new GameLocation { GameId = dto.GameId, LocationId = dto.LocationId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/locations/by-game/{dto.GameId}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveFromGame(int gameId, int locationId, WorldDbContext db)
    {
        var gl = await db.GameLocations.FindAsync(gameId, locationId);
        if (gl is null)
            return TypedResults.NotFound();

        db.GameLocations.Remove(gl);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // ──────────────────────────────────────────────────────────────────
    // Issue #252 — PATCH /api/locations/{id}/coordinates
    //
    // Drag-drop relocate path. Replaces the prior GET LocationDetailDto →
    // PUT UpdateLocationDto round-trip in MapPage.ConfirmDragAsync, which
    // could clobber concurrent organizer edits on Description / Details /
    // NpcInfo / etc. since UpdateLocationDto's nullable string fields would
    // overwrite anything the GET picked up before another organizer's
    // SaveChangesAsync landed.
    //
    // Validation:
    //   1. 404 first if the location id is missing (per _review-instincts §1).
    //   2. Then 400 ProblemDetails(czech) on out-of-range lat / lng.
    //
    // No audit log added — there's no LocationAudit infrastructure today
    // (verified via grep of LocationEndpoints + Domain/Entities). Adding
    // one solely for the coordinate patch would be the first audit on the
    // Location surface; defer until a broader audit need surfaces. See
    // Phase-0 Q&A on PR #284.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> PatchCoordinates(
        int id, LocationCoordinatesPatchDto dto, WorldDbContext db, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.LocationEndpoints");
        logger.LogInformation(
            "[map-diag] location.coordinates.patch.entry locationId={LocationId} lat={Latitude} lng={Longitude}",
            id,
            dto.Latitude,
            dto.Longitude);

        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
        {
            logger.LogInformation("[map-diag] location.coordinates.patch.not-found locationId={LocationId}", id);
            return TypedResults.NotFound();
        }

        if (dto.Latitude < -90m || dto.Latitude > 90m)
        {
            logger.LogInformation(
                "[map-diag] location.coordinates.patch.invalid-lat locationId={LocationId} lat={Latitude}",
                id,
                dto.Latitude);
            return TypedResults.Problem(
                title: "Neplatná souřadnice",
                detail: "Zeměpisná šířka musí být v rozsahu -90 až 90 stupňů.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (dto.Longitude < -180m || dto.Longitude > 180m)
        {
            logger.LogInformation(
                "[map-diag] location.coordinates.patch.invalid-lng locationId={LocationId} lng={Longitude}",
                id,
                dto.Longitude);
            return TypedResults.Problem(
                title: "Neplatná souřadnice",
                detail: "Zeměpisná délka musí být v rozsahu -180 až 180 stupňů.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        loc.Coordinates = new GpsCoordinates(dto.Latitude, dto.Longitude);
        logger.LogInformation("[map-diag] location.coordinates.patch.before-commit locationId={LocationId}", id);
        await db.SaveChangesAsync();
        logger.LogInformation("[map-diag] location.coordinates.patch.committed locationId={LocationId}", id);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult PlacementProblem(string detail) =>
        TypedResults.Problem(
            title: "Umístění lokace se nepodařilo uložit",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static (string UserId, string Name) GetOrganizer(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "unknown";
        var name = user.FindFirstValue("name")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? "Unknown";
        return (userId, name);
    }

    private static string NormalizeLocalTimestamp(string? value, DateTime fallbackUtc)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? fallbackUtc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
            : trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }
}
