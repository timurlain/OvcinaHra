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
    public static RouteGroupBuilder MapScanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/scan").WithTags("Scan").RequireAuthorization();

        group.MapGet("/{personId:int}", GetCharacterProfile);
        group.MapPost("/{personId:int}/events", PostEvent);
        group.MapDelete("/{personId:int}/events/last-levelup", DeleteLastLevelUp);
        group.MapGet("/{personId:int}/events", GetRecentEvents);

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

    private static async Task<Results<Created<CharacterEventDto>, NotFound, BadRequest<string>>> PostEvent(
        int personId, CreateCharacterEventDto dto, WorldDbContext db, HttpContext httpContext)
    {
        var assignment = await db.CharacterAssignments
            .Include(a => a.Character)
            .Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

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

        db.CharacterEvents.Add(ev);
        await db.SaveChangesAsync();

        var result = new CharacterEventDto(ev.Id, ev.EventType, ev.Data, ev.Location, ev.OrganizerName, ev.Timestamp);
        return TypedResults.Created($"/api/scan/{personId}/events/{ev.Id}", result);
    }

    private static async Task<Results<Ok<CharacterEventDto>, NotFound>> DeleteLastLevelUp(
        int personId, WorldDbContext db, HttpContext httpContext)
    {
        var assignment = await db.CharacterAssignments
            .Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.ExternalPersonId == personId && a.IsActive);

        if (assignment is null) return TypedResults.NotFound();

        var levelUp = assignment.Events
            .Where(e => e.EventType == CharacterEventType.LevelUp)
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .FirstOrDefault();

        if (levelUp is null) return TypedResults.NotFound();

        var organizer = GetOrganizer(httpContext);
        var audit = new CharacterEvent
        {
            CharacterAssignmentId = assignment.Id,
            Timestamp = DateTime.UtcNow,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            EventType = CharacterEventType.LevelUpReverted,
            Data = JsonSerializer.Serialize(new
            {
                revertedEventId = levelUp.Id,
                revertedLevel = ExtractLevel(levelUp.Data)
            }),
            Location = levelUp.Location
        };

        db.CharacterEvents.Remove(levelUp);
        db.CharacterEvents.Add(audit);
        await db.SaveChangesAsync();

        return TypedResults.Ok(ToDto(audit));
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

    private static CharacterEventDto ToDto(CharacterEvent ev) =>
        new(ev.Id, ev.EventType, ev.Data, ev.Location, ev.OrganizerName, ev.Timestamp);

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
}
