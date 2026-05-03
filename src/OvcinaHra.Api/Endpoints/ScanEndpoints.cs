using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ScanEndpoints
{
    private const string IdempotencyHeader = "X-Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 200;

    public static RouteGroupBuilder MapScanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/scan").WithTags("Scan").RequireAuthorization();

        group.MapGet("/{personId:int}", GetCharacterProfile);
        group.MapPost("/{personId:int}/events", PostEvent);
        group.MapDelete("/{personId:int}/events/last-levelup", DeleteLastLevelUp);
        group.MapDelete("/{personId:int}/events/last-note", DeleteLastNote);
        group.MapDelete("/{personId:int}/events/last-combat", DeleteLastCombat);
        group.MapGet("/{personId:int}/events", GetRecentEvents);
        group.MapGet("/{personId:int}/treasure-quests/pending", GetPendingTreasureQuests);
        group.MapPost("/{personId:int}/treasure-quests/{questId:int}/verify", VerifyTreasureQuest);

        return group;
    }

    private static async Task<Results<Ok<ScanCharacterDto>, NotFound>> GetCharacterProfile(
        int personId, WorldDbContext db)
    {
        var assignment = await db.CharacterAssignments
            .Include(a => a.Character)
            .Include(a => a.Events)
            .Include(a => a.Kingdom)
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

        var events = assignment.Events.ToList();

        var currentLevel = events.Count(e => e.EventType == CharacterEventType.LevelUp);
        var totalXp = currentLevel;

        var skills = events
            .Where(e => e.EventType == CharacterEventType.SkillGained)
            .Select(e =>
            {
                try
                {
                    var doc = JsonDocument.Parse(e.Data);
                    return doc.RootElement.TryGetProperty("skill", out var skill)
                        ? skill.GetString() ?? string.Empty
                        : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            })
            .Where(s => s.Length > 0)
            .ToList();

        var recentEvents = events
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .Select(e => new CharacterEventDto(e.Id, e.EventType, e.Data, e.Location, e.OrganizerName, e.Timestamp))
            .ToList();

        var ch = assignment.Character;
        var playerFullName =
            string.IsNullOrWhiteSpace(ch.PlayerFirstName) && string.IsNullOrWhiteSpace(ch.PlayerLastName)
                ? null
                : $"{ch.PlayerFirstName} {ch.PlayerLastName}".Trim();

        return TypedResults.Ok(new ScanCharacterDto(
            ch.Id, assignment.Id, personId,
            ch.Name, playerFullName,
            ch.Race, assignment.Class,
            assignment.Kingdom?.Name, ch.BirthYear,
            currentLevel, totalXp, skills, recentEvents));
    }

    private static async Task<IResult> PostEvent(
        int personId, CreateCharacterEventDto dto, WorldDbContext db, HttpContext httpContext)
    {
        var assignment = await db.CharacterAssignments
            .Include(a => a.Character)
            .Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

        // Glejt monster-combat (#518, loosened in #523) — MonsterVictory and
        // MonsterDefeat originally required an active OrganizerRoleAssignment
        // for the caller's email in a slot covering now ± 15 min. That gate
        // blocked organizers using the Force Monster override (Glejt v0.1.23+),
        // which is meant to be a real bypass when login/role plumbing is
        // unreliable. We now keep only the cheap sanity check: payload must
        // carry a numeric monsterNpcId pointing at an NPC flagged Monster in
        // this game. monsterName is still trusted from the client (audit log,
        // not a security boundary). Runs BEFORE idempotency replay to keep
        // ordering symmetric with the previous gate.
        if (dto.EventType is CharacterEventType.MonsterVictory or CharacterEventType.MonsterDefeat)
        {
            var monsterAuth = await AuthorizeMonsterEventAsync(dto, assignment.GameId, httpContext, db);
            if (monsterAuth is { } denied) return denied;
        }

        var idempotencyKey = GetIdempotencyKey(httpContext);
        if (await TryGetIdempotentEventAsync(db, assignment.Id, idempotencyKey) is { } replay)
            return TypedResults.Ok(replay);
        if (!IsValidIdempotencyKey(idempotencyKey))
            return SaveFailed("Hlavička X-Idempotency-Key je příliš dlouhá.");

        var organizer = GetOrganizer(httpContext);

        string eventData = dto.Data;

        if (dto.EventType == CharacterEventType.LevelUp)
        {
            var existingLevels = assignment.Events.Count(e => e.EventType == CharacterEventType.LevelUp);
            eventData = JsonSerializer.Serialize(new { level = existingLevels + 1 });
        }
        else if (dto.EventType == CharacterEventType.ClassChosen)
        {
            if (assignment.Class is not null)
                return TypedResults.BadRequest("Character already has a class assigned.");

            try
            {
                var doc = JsonDocument.Parse(dto.Data);
                if (!doc.RootElement.TryGetProperty("class", out var classProp))
                    return TypedResults.BadRequest("Missing 'class' property in Data.");

                var className = classProp.GetString();
                if (!Enum.TryParse<PlayerClass>(className, ignoreCase: true, out var playerClass))
                    return TypedResults.BadRequest($"Unknown class: '{className}'.");

                assignment.Class = playerClass;
            }
            catch (JsonException)
            {
                return TypedResults.BadRequest("Data must be valid JSON for ClassChosen event.");
            }
        }

        var ev = new CharacterEvent
        {
            CharacterAssignmentId = assignment.Id,
            Timestamp = DateTime.UtcNow,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            EventType = dto.EventType,
            Data = eventData,
            Location = dto.Location
        };

        // Issue #478 — capture level for audit before any save. assignment.Events
        // is the pre-add count from .Include(a => a.Events), so +1 = the new
        // level (matches eventData seeded on line 109).
        int? newLevelForAudit = dto.EventType == CharacterEventType.LevelUp
            ? assignment.Events.Count(e => e.EventType == CharacterEventType.LevelUp) + 1
            : null;

        db.CharacterEvents.Add(ev);
        TrackIdempotency(db, assignment.Id, idempotencyKey, ev);
        await db.SaveChangesAsync();

        // Issue #478 — best-effort mirror into the WorldActivity audit log so
        // the Aktivity světa table on the cockpit surfaces level-ups alongside
        // placements. CharacterEvent stays the system-of-record for player
        // progression (RecentActivityPanel polls it for the live takeover);
        // WorldActivity is the organizer-action audit overlay. Audit failures
        // must never roll back the primary event — pre-check the Game FK
        // (cheap query) so we don't throw on assignments tied to fake gameIds
        // (test fixtures), and wrap the audit save in try/catch as a belt
        // for any other transient constraint hit.
        if (newLevelForAudit is { } newLevel
            && await db.Games.AnyAsync(g => g.Id == assignment.GameId))
        {
            try
            {
                db.WorldActivities.Add(new WorldActivity
                {
                    GameId = assignment.GameId,
                    TimestampUtc = ev.Timestamp,
                    OrganizerUserId = organizer.UserId,
                    OrganizerName = organizer.Name,
                    ActivityType = WorldActivityType.CharacterLevelUp,
                    Description = $"{assignment.Character.Name} dosáhl {newLevel}. úrovně",
                    CharacterAssignmentId = assignment.Id,
                    DataJson = eventData
                });
                await db.SaveChangesAsync();
            }
            catch (Exception)
            {
                // Audit overlay only — primary level-up is already persisted
                // and returned to the caller; the missed audit row will simply
                // not appear in the Aktivity světa table.
            }
        }

        // Monster combat audit overlay — mirror MonsterVictory/MonsterDefeat
        // into Aktivity světa so organizers see fights in the cockpit feed
        // alongside placements + level-ups. Same best-effort pattern as
        // CharacterLevelUp above; failures must not roll back the primary
        // CharacterEvent.
        if ((dto.EventType == CharacterEventType.MonsterVictory
             || dto.EventType == CharacterEventType.MonsterDefeat)
            && await db.Games.AnyAsync(g => g.Id == assignment.GameId))
        {
            try
            {
                string monsterName = "nestvůrou";
                try
                {
                    using var doc = JsonDocument.Parse(eventData);
                    if (doc.RootElement.TryGetProperty("monsterName", out var mn)
                        && mn.GetString() is { Length: > 0 } parsed)
                    {
                        monsterName = parsed;
                    }
                }
                catch (JsonException) { }

                var (activityType, description) = dto.EventType switch
                {
                    CharacterEventType.MonsterVictory =>
                        (WorldActivityType.HeroFell,
                         $"{assignment.Character.Name} padl pod nestvůrou: {monsterName}"),
                    _ =>
                        (WorldActivityType.MonsterDefeated,
                         $"{assignment.Character.Name} přemohl nestvůru: {monsterName}"),
                };

                db.WorldActivities.Add(new WorldActivity
                {
                    GameId = assignment.GameId,
                    TimestampUtc = ev.Timestamp,
                    OrganizerUserId = organizer.UserId,
                    OrganizerName = organizer.Name,
                    ActivityType = activityType,
                    Description = description,
                    CharacterAssignmentId = assignment.Id,
                    DataJson = eventData
                });
                await db.SaveChangesAsync();
            }
            catch (Exception)
            {
                // Audit overlay only — primary combat event is persisted.
            }
        }

        return TypedResults.Created($"/api/scan/{personId}/events/{ev.Id}", ToDto(ev));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLastLevelUp(
        int personId, WorldDbContext db, HttpContext httpContext, ILoggerFactory loggerFactory)
    {
        return await DeleteLastMatchingEventAsync(
            personId,
            db,
            httpContext,
            loggerFactory,
            "last-levelup",
            [CharacterEventType.LevelUp],
            CharacterEventType.LevelUpReverted,
            WorldActivityType.CharacterLevelReverted,
            assignment => $"{assignment.Character.Name} – vrácena úroveň",
            (original, key, revertedAtUtc) => JsonSerializer.Serialize(new
            {
                revertedEventId = original.Id,
                revertedLevel = ExtractLevel(original.Data),
                originalTimestampUtc = original.Timestamp,
                revertedAtUtc,
                idempotencyKey = key
            }));
    }

    /// <summary>
    /// Glejt monster-combat (#518) — undo the most recent Note event on a
    /// hero. Mirrors the regular-mode "Vrátit poznámku" affordance. No
    /// monster-role gating: any signed-in organizer who can hit
    /// <c>/api/scan/*</c> may undo, matching the existing
    /// <see cref="DeleteLastLevelUp"/> permission model.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLastNote(
        int personId, WorldDbContext db, HttpContext httpContext, ILoggerFactory loggerFactory)
    {
        return await DeleteLastMatchingEventAsync(
            personId,
            db,
            httpContext,
            loggerFactory,
            "last-note",
            [CharacterEventType.Note],
            CharacterEventType.NoteReverted,
            WorldActivityType.NoteReverted,
            assignment => $"{assignment.Character.Name} – vrácena poznámka",
            CreateGenericRevertData);
    }

    /// <summary>
    /// Glejt monster-combat (#518) — undo the most recent monster-combat
    /// event (MonsterVictory or MonsterDefeat) on a hero. An intervening
    /// LevelUp recorded after the combat is intentionally ignored — the
    /// scope is "newest combat row, even if other event types are newer".
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLastCombat(
        int personId, WorldDbContext db, HttpContext httpContext, ILoggerFactory loggerFactory)
    {
        return await DeleteLastMatchingEventAsync(
            personId,
            db,
            httpContext,
            loggerFactory,
            "last-combat",
            [CharacterEventType.MonsterVictory, CharacterEventType.MonsterDefeat],
            CharacterEventType.MonsterCombatReverted,
            WorldActivityType.MonsterCombatReverted,
            assignment => $"{assignment.Character.Name} – vrácen souboj s nestvůrou",
            CreateGenericRevertData);
    }

    private static async Task<Results<Ok<List<CharacterEventDto>>, NotFound>> GetRecentEvents(
        int personId, WorldDbContext db)
    {
        var assignment = await db.CharacterAssignments
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

        var events = await db.CharacterEvents
            .Where(e => e.CharacterAssignmentId == assignment.Id)
            .OrderByDescending(e => e.Timestamp)
            .Take(20)
            .Select(e => new CharacterEventDto(e.Id, e.EventType, e.Data, e.Location, e.OrganizerName, e.Timestamp))
            .ToListAsync();

        return TypedResults.Ok(events);
    }

    private static async Task<Results<Ok<List<PendingTreasureQuestDto>>, NotFound>> GetPendingTreasureQuests(
        int personId, WorldDbContext db)
    {
        var assignment = await db.CharacterAssignments
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

        var quests = await db.TreasureQuests
            .Where(t => t.GameId == assignment.GameId && t.SecretStashId != null)
            .Where(t => !db.TreasureQuestVerifications.Any(v =>
                v.TreasureQuestId == t.Id && v.CharacterAssignmentId == assignment.Id))
            .OrderBy(t => t.Title)
            .Select(t => new PendingTreasureQuestDto(
                t.Id,
                t.Title,
                t.SecretStashId!.Value,
                t.SecretStash!.Name,
                db.GameSecretStashes
                    .Where(gs => gs.GameId == assignment.GameId && gs.SecretStashId == t.SecretStashId)
                    .Select(gs => (int?)gs.LocationId)
                    .FirstOrDefault(),
                db.GameSecretStashes
                    .Where(gs => gs.GameId == assignment.GameId && gs.SecretStashId == t.SecretStashId)
                    .Select(gs => gs.Location.Name)
                    .FirstOrDefault(),
                null,
                null))
            .ToListAsync();

        return TypedResults.Ok(quests);
    }

    private static async Task<IResult> VerifyTreasureQuest(
        int personId, int questId, VerifyTreasureQuestDto dto, WorldDbContext db, HttpContext httpContext)
    {
        var assignment = await db.CharacterAssignments
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

        var idempotencyKey = GetIdempotencyKey(httpContext);
        if (await TryGetIdempotentEventAsync(db, assignment.Id, idempotencyKey) is { } replay)
            return TypedResults.Ok(replay);

        var quest = await db.TreasureQuests
            .Include(t => t.SecretStash)
            .Include(t => t.TreasureItems)
            .ThenInclude(ti => ti.Item)
            .FirstOrDefaultAsync(t => t.Id == questId
                && t.GameId == assignment.GameId
                && t.SecretStashId != null);

        if (quest is null) return TypedResults.NotFound();

        var alreadyVerified = await db.TreasureQuestVerifications.AnyAsync(v =>
            v.TreasureQuestId == quest.Id && v.CharacterAssignmentId == assignment.Id);
        if (alreadyVerified) return TypedResults.NotFound();
        if (!IsValidIdempotencyKey(idempotencyKey))
            return SaveFailed("Hlavička X-Idempotency-Key je příliš dlouhá.");

        var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
        if (dto.Override && reason is null)
        {
            return SaveFailed("Při ručním přepsání musíš uvést důvod.");
        }

        if (dto.StashId != quest.SecretStashId!.Value && !dto.Override)
        {
            return SaveFailed("Razítko neodpovídá očekávané skrýši.");
        }

        var organizer = GetOrganizer(httpContext);
        var rewards = quest.TreasureItems
            .OrderBy(ti => ti.Item.Name)
            .Select(ti => new { itemId = ti.ItemId, itemName = ti.Item.Name, count = ti.Count })
            .ToList();
        var now = DateTime.UtcNow;
        var ev = new CharacterEvent
        {
            CharacterAssignmentId = assignment.Id,
            Timestamp = now,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            EventType = CharacterEventType.TreasureQuestStampVerified,
            Data = JsonSerializer.Serialize(new
            {
                questId = quest.Id,
                questName = quest.Title,
                expectedStashId = quest.SecretStashId.Value,
                expectedStashName = quest.SecretStash!.Name,
                stashId = dto.StashId,
                matchConfidence = dto.MatchConfidence,
                @override = dto.Override,
                reason,
                rewards
            }),
            Location = quest.SecretStash.Name
        };

        db.CharacterEvents.Add(ev);
        db.TreasureQuestVerifications.Add(new TreasureQuestVerification
        {
            TreasureQuestId = quest.Id,
            CharacterAssignmentId = assignment.Id,
            CharacterEventId = ev.Id,
            Event = ev,
            VerifiedStashId = dto.StashId,
            MatchConfidence = dto.MatchConfidence,
            Override = dto.Override,
            Reason = reason,
            VerifiedAtUtc = now,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name
        });
        TrackIdempotency(db, assignment.Id, idempotencyKey, ev);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/scan/{personId}/events/{ev.Id}", ToDto(ev));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteLastMatchingEventAsync(
        int personId,
        WorldDbContext db,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        string endpoint,
        IReadOnlyCollection<CharacterEventType> eventTypes,
        CharacterEventType auditEventType,
        WorldActivityType worldActivityType,
        Func<CharacterAssignment, string> description,
        Func<CharacterEvent, string?, DateTime, string> data)
    {
        var rawIdempotencyKey = GetIdempotencyKey(httpContext);
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.ScanIdempotency");
        logger.LogInformation(
            "[scan-idem] entry endpoint={Endpoint} personId={PersonId} idempotencyKey={IdempotencyKey}",
            endpoint,
            personId,
            rawIdempotencyKey ?? "<missing>");

        var assignment = await db.CharacterAssignments
            .Include(a => a.Character)
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);
        if (assignment is null)
        {
            logger.LogInformation(
                "[scan-idem] assignment-miss endpoint={Endpoint} personId={PersonId}",
                endpoint,
                personId);
            return TypedResults.NotFound();
        }

        if (!TryScopeRevertIdempotencyKey(
            endpoint,
            rawIdempotencyKey,
            logger,
            personId,
            out var scopedIdempotencyKey,
            out var problem))
        {
            return problem!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            if (scopedIdempotencyKey is not null)
            {
                logger.LogInformation(
                    "[scan-idem] lookup-start endpoint={Endpoint} assignmentId={AssignmentId} idempotencyKey={IdempotencyKey}",
                    endpoint,
                    assignment.Id,
                    rawIdempotencyKey);

                var alreadyReverted = await db.EventIdempotencies.AnyAsync(e =>
                    e.CharacterAssignmentId == assignment.Id && e.IdempotencyKey == scopedIdempotencyKey);
                logger.LogInformation(
                    "[scan-idem] lookup-complete endpoint={Endpoint} assignmentId={AssignmentId} idempotencyHit={IdempotencyHit}",
                    endpoint,
                    assignment.Id,
                    alreadyReverted);

                if (alreadyReverted)
                {
                    logger.LogInformation(
                        "[scan-idem] idempotency-hit endpoint={Endpoint} assignmentId={AssignmentId} idempotencyKey={IdempotencyKey}",
                        endpoint,
                        assignment.Id,
                        rawIdempotencyKey);
                    await transaction.CommitAsync();
                    return TypedResults.NoContent();
                }
            }

            var original = await db.CharacterEvents
                .Where(e => e.CharacterAssignmentId == assignment.Id && eventTypes.Contains(e.EventType))
                .OrderByDescending(e => e.Timestamp)
                .ThenByDescending(e => e.Id)
                .FirstOrDefaultAsync();
            if (original is null)
            {
                logger.LogInformation(
                    "[scan-idem] no-matching-event endpoint={Endpoint} assignmentId={AssignmentId}",
                    endpoint,
                    assignment.Id);
                await transaction.CommitAsync();
                return TypedResults.NotFound();
            }

            var organizer = GetOrganizer(httpContext);
            var revertedAtUtc = DateTime.UtcNow;
            var audit = new CharacterEvent
            {
                CharacterAssignmentId = assignment.Id,
                Timestamp = revertedAtUtc,
                OrganizerUserId = organizer.UserId,
                OrganizerName = organizer.Name,
                EventType = auditEventType,
                Data = data(original, rawIdempotencyKey, revertedAtUtc),
                Location = original.Location
            };

            logger.LogInformation(
                "[scan-idem] fresh-revert-start endpoint={Endpoint} assignmentId={AssignmentId} originalEventId={OriginalEventId} originalEventType={OriginalEventType}",
                endpoint,
                assignment.Id,
                original.Id,
                original.EventType);

            db.CharacterEvents.Remove(original);
            db.CharacterEvents.Add(audit);
            TrackIdempotency(db, assignment.Id, scopedIdempotencyKey, audit);

            if (await db.Games.AnyAsync(g => g.Id == assignment.GameId))
            {
                db.WorldActivities.Add(new WorldActivity
                {
                    GameId = assignment.GameId,
                    TimestampUtc = audit.Timestamp,
                    OrganizerUserId = organizer.UserId,
                    OrganizerName = organizer.Name,
                    ActivityType = worldActivityType,
                    Description = description(assignment),
                    CharacterAssignmentId = assignment.Id,
                    DataJson = audit.Data
                });
                logger.LogInformation(
                    "[scan-idem] world-activity-added endpoint={Endpoint} assignmentId={AssignmentId} gameId={GameId}",
                    endpoint,
                    assignment.Id,
                    assignment.GameId);
            }
            else
            {
                logger.LogWarning(
                    "[scan-idem] world-activity-skipped-no-game endpoint={Endpoint} assignmentId={AssignmentId} gameId={GameId}",
                    endpoint,
                    assignment.Id,
                    assignment.GameId);
            }

            logger.LogInformation(
                "[scan-idem] transaction-save-start endpoint={Endpoint} assignmentId={AssignmentId}",
                endpoint,
                assignment.Id);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            logger.LogInformation(
                "[scan-idem] transaction-committed endpoint={Endpoint} assignmentId={AssignmentId} auditEventId={AuditEventId}",
                endpoint,
                assignment.Id,
                audit.Id);
            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[scan-idem] transaction-failed endpoint={Endpoint} assignmentId={AssignmentId} idempotencyKey={IdempotencyKey}",
                endpoint,
                assignment.Id,
                rawIdempotencyKey ?? "<missing>");
            throw;
        }
    }

    private static CharacterEventDto ToDto(CharacterEvent ev) =>
        new(ev.Id, ev.EventType, ev.Data, ev.Location, ev.OrganizerName, ev.Timestamp);

    private static string? GetIdempotencyKey(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(IdempotencyHeader, out var values))
            return null;

        var key = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    private static bool IsValidIdempotencyKey(string? key) =>
        key is null || key.Length <= MaxIdempotencyKeyLength;

    private static bool TryScopeRevertIdempotencyKey(
        string endpoint,
        string? key,
        ILogger logger,
        int personId,
        out string? scopedKey,
        out ProblemHttpResult? problem)
    {
        scopedKey = null;
        problem = null;
        if (key is null)
        {
            logger.LogWarning(
                "[scan-idem] missing-idempotency-key endpoint={Endpoint} personId={PersonId}",
                endpoint,
                personId);
            return true;
        }

        scopedKey = $"revert:{endpoint}:{key}";
        if (IsValidIdempotencyKey(scopedKey)) return true;

        logger.LogWarning(
            "[scan-idem] idempotency-key-too-long endpoint={Endpoint} personId={PersonId} length={Length}",
            endpoint,
            personId,
            key.Length);
        problem = SaveFailed("Hlavička X-Idempotency-Key je příliš dlouhá.");
        return false;
    }

    private static async Task<CharacterEventDto?> TryGetIdempotentEventAsync(
        WorldDbContext db,
        int assignmentId,
        string? key)
    {
        if (key is null) return null;

        return await db.EventIdempotencies
            .Where(e => e.CharacterAssignmentId == assignmentId && e.IdempotencyKey == key)
            .Select(e => new CharacterEventDto(
                e.Event.Id,
                e.Event.EventType,
                e.Event.Data,
                e.Event.Location,
                e.Event.OrganizerName,
                e.Event.Timestamp))
            .FirstOrDefaultAsync();
    }

    private static void TrackIdempotency(
        WorldDbContext db,
        int assignmentId,
        string? key,
        CharacterEvent ev)
    {
        if (key is null) return;

        db.EventIdempotencies.Add(new EventIdempotency
        {
            CharacterAssignmentId = assignmentId,
            IdempotencyKey = key,
            EventId = ev.Id,
            Event = ev,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static ProblemHttpResult SaveFailed(string detail) =>
        TypedResults.Problem(
            title: "Uložení selhalo",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static string CreateGenericRevertData(CharacterEvent original, string? key, DateTime revertedAtUtc) =>
        JsonSerializer.Serialize(new
        {
            revertedEventId = original.Id,
            originalEventType = original.EventType.ToString(),
            originalTimestampUtc = original.Timestamp,
            originalData = original.Data,
            originalLocation = original.Location,
            revertedAtUtc,
            idempotencyKey = key
        });

    private static int? ExtractLevel(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty("level", out var level) && level.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string UserId, string Name) GetOrganizer(HttpContext httpContext)
    {
        var user = httpContext.User;
        return (
            user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
            user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? "Unknown");
    }

    /// <summary>
    /// Sanity-checks a monster combat event. The Glejt "Force Monster" toggle
    /// is meant to be a true bypass when login/role plumbing is unreliable, so
    /// the original ±15 min OrganizerRoleAssignment gate was dropped (#523).
    /// What survives is the cheap correctness guard: payload must carry a
    /// numeric <c>monsterNpcId</c>, and that NPC must actually be flagged
    /// <see cref="NpcRole.Monster"/> in the assignment's game.
    /// </summary>
    private static async Task<IResult?> AuthorizeMonsterEventAsync(
        CreateCharacterEventDto dto,
        int gameId,
        HttpContext httpContext,
        WorldDbContext db)
    {
        int? monsterNpcId = TryReadMonsterNpcId(dto.Data);
        if (monsterNpcId is null)
        {
            return TypedResults.Problem(
                title: "Souboj s nestvůrou nelze zapsat",
                detail: "Payload musí obsahovat číselné monsterNpcId.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var npcIsMonsterInGame = await db.GameNpcs
            .AsNoTracking()
            .AnyAsync(gn => gn.GameId == gameId
                && gn.NpcId == monsterNpcId.Value
                && gn.Npc.Role == NpcRole.Monster);

        if (!npcIsMonsterInGame)
        {
            return TypedResults.Problem(
                title: "Souboj s nestvůrou nelze zapsat",
                detail: "Vybraná NPC není v této hře označená jako nestvůra.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static int? TryReadMonsterNpcId(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty("monsterNpcId", out var v)
                && v.ValueKind == JsonValueKind.Number
                && v.TryGetInt32(out var id)
                    ? id
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
