using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

/// <summary>
/// Issue #158 — server-side fan-out for the Home cockpit. Each route
/// returns one of the Dashboard*Dto records and powers a single section
/// of the cockpit. The cockpit fires them in parallel client-side.
/// </summary>
public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/dashboard").WithTags("Dashboard");

        group.MapGet("/stats", GetStats);
        group.MapGet("/issues", GetIssues);
        group.MapGet("/activity", GetActivity);
        group.MapGet("/world-activity", GetWorldActivity);
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
    /// Powers RecentActivityFeed from the best-effort WorldChange audit log.
    /// </summary>
    private static async Task<Ok<List<DashboardActivityDto>>> GetActivity(
        int gameId, WorldDbContext db, int? limit = 10)
    {
        var take = Math.Clamp(limit ?? 10, 1, 50);

        var changes = await db.WorldChanges
            .AsNoTracking()
            .Where(c => c.GameId == gameId || c.GameId == null)
            .OrderByDescending(c => c.ChangedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(take)
            .Select(c => new
            {
                c.EntityType,
                c.EntityId,
                c.EntityName,
                c.Operation,
                c.ActorDisplayName,
                c.ChangedAtUtc
            })
            .ToListAsync();

        var rows = changes.Select(c => new DashboardActivityDto(
            c.EntityType.ToLowerInvariant(),
            c.EntityId,
            c.EntityName,
            null,
            ChangeVerb(c.Operation),
            c.ActorDisplayName,
            c.ChangedAtUtc)).ToList();

        return TypedResults.Ok(rows);
    }

    private static string ChangeVerb(WorldChangeOperation operation) => operation switch
    {
        WorldChangeOperation.Created => "vytvořil",
        WorldChangeOperation.Updated => "upravil",
        WorldChangeOperation.Deleted => "smazal",
        _ => "upravil"
    };

    /// <summary>
    /// Issue #478 — raw WorldActivity rows for the Aktivity světa table on
    /// the Home cockpit. Returns the audit log 1:1 (no merge with legacy
    /// entity-timestamp rows) so organizers can verify what's flowing in
    /// from the Glejt PWA + in-app workflows pre-weekend. Default 100 rows,
    /// max 500. 404 when the game doesn't exist (per §1 — REST 404 before
    /// 200-with-empty masking orchestrator bugs).
    /// </summary>
    private static async Task<Results<Ok<List<WorldActivityRowDto>>, NotFound>> GetWorldActivity(
        int gameId, WorldDbContext db, int? take = 100)
    {
        var n = Math.Clamp(take ?? 100, 1, 500);

        if (!await db.Games.AnyAsync(g => g.Id == gameId))
            return TypedResults.NotFound();

        var rows = await db.WorldActivities
            .Where(a => a.GameId == gameId)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(n)
            .Select(a => new WorldActivityRowDto(
                a.Id,
                a.TimestampUtc,
                a.OrganizerName,
                a.ActivityType,
                a.Description,
                a.LocationId,
                a.Location != null ? a.Location.Name : null,
                a.CharacterAssignmentId,
                a.QuestId))
            .ToListAsync();

        return TypedResults.Ok(rows);
    }

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
