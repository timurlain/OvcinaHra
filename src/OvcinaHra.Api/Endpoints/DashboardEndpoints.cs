using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

/// <summary>
/// Issue #158 — server-side fan-out for the Home cockpit. Four routes,
/// each returns one of the Dashboard*Dto records and powers a single
/// section of the cockpit. The cockpit fires them in parallel client-side.
/// </summary>
public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/dashboard").WithTags("Dashboard");

        group.MapGet("/stats", GetStats);
        group.MapGet("/issues", GetIssues);
        group.MapGet("/activity", GetActivity);
        group.MapGet("/timeline", GetTimeline);
        group.MapGet("/events/recent", GetRecentEvents);

        return group;
    }

    /// <summary>
    /// Powers StavSvetaStrip — 7 game-scoped catalog counts. Sequential
    /// CountAsync (not Task.WhenAll on a shared DbContext, which EF Core
    /// does not support — second-operation-started exception). The brief's
    /// "single round-trip" goal is satisfied by one HTTP call from the
    /// cockpit, not by parallel DB queries.
    /// </summary>
    private static async Task<Ok<DashboardStatsDto>> GetStats(int gameId, WorldDbContext db)
    {
        var locations = await db.GameLocations.CountAsync(gl => gl.GameId == gameId);
        var characters = await db.CharacterAssignments.CountAsync(a => a.GameId == gameId);
        var items = await db.GameItems.CountAsync(gi => gi.GameId == gameId);
        var spells = await db.GameSpells.CountAsync(gs => gs.GameId == gameId);
        var skills = await db.GameSkills.CountAsync(gs => gs.GameId == gameId);
        var treasures = await db.TreasureQuests.CountAsync(t => t.GameId == gameId);
        var quests = await db.Quests.CountAsync(q => q.GameId == gameId);

        return TypedResults.Ok(new DashboardStatsDto(
            locations, characters, items, spells, skills, treasures, quests));
    }

    /// <summary>
    /// Powers PozorPanel — needs-attention list for Příprava state. Each
    /// row has a Czech label, a count, a severity bucket, and a target
    /// route the click-through navigates to.
    /// </summary>
    private static async Task<Ok<DashboardIssuesDto>> GetIssues(int gameId, WorldDbContext db)
    {
        // Sequential awaits — same DbContext concurrency rule as Stats above.
        var locationRows = await db.GameLocations
            .Where(gl => gl.GameId == gameId)
            .Select(gl => new LocationEndpoints.LocationCoordinateState(
                gl.LocationId,
                gl.Location.Coordinates != null ? gl.Location.Coordinates.Latitude : (decimal?)null,
                gl.Location.Coordinates != null ? gl.Location.Coordinates.Longitude : (decimal?)null,
                gl.Location.ParentLocationId))
            .ToListAsync();
        var locationLookup = await LocationEndpoints.BuildLocationStateLookupAsync(db, locationRows);
        var locationsNoGps = locationRows.Count(row => !LocationEndpoints.IsLocated(locationLookup, row.Id));
        var itemsNoPrice = await db.GameItems
            .Where(gi => gi.GameId == gameId && gi.IsSold && gi.Price == null)
            .CountAsync();
        var treasuresUnplaced = await db.TreasureQuests
            .Where(t => t.GameId == gameId && t.LocationId == null && t.SecretStashId == null)
            .CountAsync();
        var emptyStashes = await db.GameSecretStashes
            .Where(gss => gss.GameId == gameId
                && !db.TreasureQuests.Any(t => t.GameId == gameId && t.SecretStashId == gss.SecretStashId))
            .CountAsync();
        // Game-scoped — Copilot caught these counting catalog-wide and
        // misreporting issues from other games. The skill/spell text lives
        // on the catalog Skill/Spell, joined via Game{Skill|Spell}.
        var skillsNoEffect = await db.GameSkills
            .Where(gs => gs.GameId == gameId)
            .CountAsync(gs => string.IsNullOrWhiteSpace(gs.Effect));
        var spellsNoEffect = await db.GameSpells
            .Where(gs => gs.GameId == gameId)
            .CountAsync(gs => string.IsNullOrWhiteSpace(gs.Spell.Effect));

        var issues = new List<DashboardIssueDto>
        {
            new("locations-no-gps", "Lokace bez GPS",
                locationsNoGps, Severity(locationsNoGps), "/locations"),
            new("items-no-price", "Předměty bez ceny",
                itemsNoPrice, Severity(itemsNoPrice), "/items"),
            new("treasures-unplaced", "Poklady neumístěné",
                treasuresUnplaced, Severity(treasuresUnplaced), "/treasures"),
            new("empty-stashes", "Skrýše bez pokladu",
                emptyStashes, Severity(emptyStashes), "/secret-stashes"),
            new("skills-no-effect", "Dovednosti bez Efektu",
                skillsNoEffect, Severity(skillsNoEffect), "/skills"),
            new("spells-no-effect", "Kouzla bez Efektu",
                spellsNoEffect, Severity(spellsNoEffect), "/spells"),
        };

        return TypedResults.Ok(new DashboardIssuesDto(issues));
    }

    private static string Severity(int count) =>
        count == 0 ? "low" : count < 5 ? "low" : count < 15 ? "mid" : "high";

    /// <summary>
    /// Powers RecentActivityFeed — real WorldActivity recordings first,
    /// followed by legacy per-entity timestamp rows until every workflow
    /// records through WorldActivity.
    /// </summary>
    private static async Task<Ok<List<DashboardActivityDto>>> GetActivity(
        int gameId, WorldDbContext db, HttpContext http, int? limit = 10)
    {
        var take = Math.Clamp(limit ?? 10, 1, 50);

        var activityRows = await db.WorldActivities
            .Where(a => a.GameId == gameId)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.LocationId,
                a.Description,
                a.OrganizerName,
                a.TimestampUtc,
                a.ActivityType,
                LocationName = a.Location != null ? a.Location.Name : null,
                PlacementPhotoPath = a.Location != null ? a.Location.PlacementPhotoPath : null
            })
            .ToListAsync();

        var activities = activityRows.Select(a => new DashboardActivityDto(
            a.LocationId.HasValue ? "location" : "activity",
            a.LocationId ?? a.Id,
            a.LocationName ?? a.Description,
            a.LocationId.HasValue && !string.IsNullOrWhiteSpace(a.PlacementPhotoPath)
                ? ImageEndpoints.ThumbUrl(http, "locationplacements", a.LocationId.Value, "small")
                : null,
            ActivityVerb(a.ActivityType),
            a.OrganizerName,
            a.TimestampUtc)).ToList();

        // Per-entity slices, each ordered DESC and capped at `take` so the
        // merged result has enough material to slice the global top-N from.
        // Sequential awaits — single DbContext can't serve concurrent queries.
        var characters = await db.CharacterAssignments
            .Where(a => a.GameId == gameId)
            .OrderByDescending(a => a.Character.UpdatedAtUtc)
            .Take(take)
            .Select(a => new DashboardActivityDto(
                "character", a.CharacterId, a.Character.Name,
                null, "upravil", "—", a.Character.UpdatedAtUtc))
            .ToListAsync();

        var events = await db.GameEvents
            .Where(e => e.GameId == gameId)
            .OrderByDescending(e => e.UpdatedAtUtc)
            .Take(take)
            .Select(e => new DashboardActivityDto(
                "event", e.Id, e.Name,
                null, "upravil", "—", e.UpdatedAtUtc))
            .ToListAsync();

        var npcs = await db.GameNpcs
            .Where(gn => gn.GameId == gameId)
            .OrderByDescending(gn => gn.Npc.UpdatedAtUtc)
            .Take(take)
            .Select(gn => new DashboardActivityDto(
                "npc", gn.NpcId, gn.Npc.Name,
                null, "upravil", "—", gn.Npc.UpdatedAtUtc))
            .ToListAsync();

        var merged = characters
            .Concat(activities)
            .Concat(events)
            .Concat(npcs)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(take)
            .ToList();

        return TypedResults.Ok(merged);
    }

    private static string ActivityVerb(WorldActivityType type) => type switch
    {
        WorldActivityType.LocationPlaced => "umístil",
        WorldActivityType.CharacterLevelUp => "zapsal level",
        WorldActivityType.QuestCompleted => "dokončil quest",
        _ => "zapsal"
    };

    /// <summary>
    /// Powers TimelinePreview — upcoming and currently-running GameTimeSlot
    /// rows. Filter and limit run inside the DB query (Copilot perf fix).
    /// Title is picked deterministically when a slot has multiple events:
    /// alphabetic by Name, then GameEventId. Server only reports
    /// <c>IsRunning</c> (UTC-safe); the "Brzy/Zítra/Později" buckets are
    /// computed client-side from local time so they don't drift across
    /// time zones near midnight.
    /// </summary>
    private static async Task<Ok<List<TimelineRowDto>>> GetTimeline(
        int gameId, WorldDbContext db, int? take = 6)
    {
        var n = Math.Clamp(take ?? 6, 1, 20);
        var now = DateTime.UtcNow;

        var rows = await db.GameTimeSlots
            .Where(s => s.GameId == gameId && s.StartTime + s.Duration > now)
            .OrderBy(s => s.StartTime)
            .Take(n)
            .Select(s => new
            {
                SlotId = s.Id,
                s.StartTime,
                s.Duration,
                // Multi-event slot: deterministic pick — alpha by Name then
                // GameEventId — so the preview doesn't flicker between
                // events on subsequent loads (Copilot finding).
                Title = db.GameEventTimeSlots
                    .Where(ets => ets.GameTimeSlotId == s.Id)
                    .OrderBy(ets => ets.GameEvent.Name)
                    .ThenBy(ets => ets.GameEventId)
                    .Select(ets => (string?)ets.GameEvent.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var timeline = rows
            .Select(r => new TimelineRowDto(
                r.SlotId, r.StartTime, r.Duration, r.Title,
                LocationName: null, LocationId: null,
                IsRunning: r.StartTime <= now && r.StartTime + r.Duration > now))
            .ToList();

        return TypedResults.Ok(timeline);
    }

    private static async Task<Ok<List<DashboardRecentEventDto>>> GetRecentEvents(
        int gameId, WorldDbContext db, DateTime? since = null)
    {
        var query = db.CharacterEvents
            .Where(e => e.Assignment.GameId == gameId);

        if (since is not null)
        {
            var sinceUtc = since.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(since.Value, DateTimeKind.Utc)
                : since.Value.ToUniversalTime();
            query = query.Where(e => e.Timestamp > sinceUtc);
        }

        var rows = await query
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .Take(50)
            .Select(e => new DashboardRecentEventDto(
                e.Id,
                e.CharacterAssignmentId,
                e.Assignment.CharacterId,
                e.Assignment.Character.Name,
                e.EventType,
                e.Data,
                e.Location,
                e.OrganizerName,
                e.Timestamp))
            .ToListAsync();

        return TypedResults.Ok(rows);
    }
}
