using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class GameEventEndpoints
{
    public static IEndpointRouteBuilder MapGameEventEndpoints(this IEndpointRouteBuilder routes)
    {
        // Game-scoped event routes
        var gameGroup = routes.MapGroup("/api/games/{gameId:int}/events")
            .WithTags("GameEvents")
            .RequireAuthorization();
        gameGroup.MapGet("/", ListByGame);
        gameGroup.MapGet("/current", GetCurrentEvents);
        gameGroup.MapGet("/next", GetNextEvents);
        gameGroup.MapPost("/", CreateForGame);

        // Event-by-id routes
        var eventGroup = routes.MapGroup("/api/events")
            .WithTags("GameEvents")
            .RequireAuthorization();
        eventGroup.MapGet("/{id:int}", GetById);
        eventGroup.MapPut("/{id:int}", Update);
        eventGroup.MapDelete("/{id:int}", Delete);

        // User schedule query
        routes.MapGet("/api/users/{personId:int}/schedule", GetUserSchedule)
            .WithTags("GameEvents")
            .RequireAuthorization();

        return routes;
    }

    private static async Task<Ok<List<GameEventListDto>>> ListByGame(int gameId, WorldDbContext db)
    {
        var events = await db.GameEvents
            .Where(e => e.GameId == gameId && !e.IsDeleted)
            .Select(e => new GameEventListDto(
                e.Id, e.GameId, e.Name, e.Description,
                e.EventTimeSlots.Count, e.EventLocations.Count,
                e.EventQuests.Count, e.EventNpcs.Count))
            .ToListAsync();
        return TypedResults.Ok(events);
    }

    private static async Task<Ok<List<GameEventDetailDto>>> GetCurrentEvents(int gameId, WorldDbContext db)
    {
        var now = DateTime.UtcNow;
        var events = await db.GameEvents
            .Where(e => e.GameId == gameId && !e.IsDeleted)
            .Include(e => e.EventTimeSlots).ThenInclude(ets => ets.TimeSlot)
            .Include(e => e.EventLocations).ThenInclude(el => el.Location)
            .Include(e => e.EventQuests).ThenInclude(eq => eq.Quest)
            .Include(e => e.EventNpcs).ThenInclude(en => en.Npc)
            .ToListAsync();

        var gameNpcs = await db.GameNpcs.Where(gn => gn.GameId == gameId).ToListAsync();

        var current = events
            .Where(e => e.EventTimeSlots.Any(ets =>
                ets.TimeSlot.StartTime <= now &&
                now < ets.TimeSlot.StartTime.Add(ets.TimeSlot.Duration)))
            .Select(e => ToDetailDto(e, gameNpcs))
            .ToList();

        return TypedResults.Ok(current);
    }

    private static async Task<Ok<List<GameEventDetailDto>>> GetNextEvents(
        int gameId, WorldDbContext db, int count = 5)
    {
        var now = DateTime.UtcNow;
        count = Math.Clamp(count <= 0 ? 5 : count, 1, 50);

        var events = await db.GameEvents
            .Where(e => e.GameId == gameId && !e.IsDeleted &&
                        e.EventTimeSlots.Any(ets => ets.TimeSlot.StartTime > now))
            .Include(e => e.EventTimeSlots).ThenInclude(ets => ets.TimeSlot)
            .Include(e => e.EventLocations).ThenInclude(el => el.Location)
            .Include(e => e.EventQuests).ThenInclude(eq => eq.Quest)
            .Include(e => e.EventNpcs).ThenInclude(en => en.Npc)
            .ToListAsync();

        var gameNpcs = await db.GameNpcs.Where(gn => gn.GameId == gameId).ToListAsync();

        var upcoming = events
            .OrderBy(e => e.EventTimeSlots.Min(ets => ets.TimeSlot.StartTime))
            .Take(count)
            .Select(e => ToDetailDto(e, gameNpcs))
            .ToList();

        return TypedResults.Ok(upcoming);
    }

    private static async Task<Results<Ok<GameEventDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var e = await db.GameEvents
            .Where(ev => ev.Id == id && !ev.IsDeleted)
            .Include(ev => ev.EventTimeSlots).ThenInclude(ets => ets.TimeSlot)
            .Include(ev => ev.EventLocations).ThenInclude(el => el.Location)
            .Include(ev => ev.EventQuests).ThenInclude(eq => eq.Quest)
            .Include(ev => ev.EventNpcs).ThenInclude(en => en.Npc)
            .FirstOrDefaultAsync();

        if (e is null) return TypedResults.NotFound();

        var gameNpcs = await db.GameNpcs.Where(gn => gn.GameId == e.GameId).ToListAsync();
        return TypedResults.Ok(ToDetailDto(e, gameNpcs));
    }

    private static async Task<Results<Created<GameEventDetailDto>, BadRequest<string>>> CreateForGame(
        int gameId, CreateGameEventDto dto, WorldDbContext db)
    {
        if (dto.TimeSlotIds.Count == 0)
            return TypedResults.BadRequest("Událost musí mít alespoň jeden časový slot.");

        // Validate time slots belong to this game
        var validSlots = await db.GameTimeSlots
            .Where(ts => ts.GameId == gameId && dto.TimeSlotIds.Contains(ts.Id))
            .Select(ts => ts.Id)
            .ToListAsync();
        if (validSlots.Count != dto.TimeSlotIds.Distinct().Count())
            return TypedResults.BadRequest("Některé časové sloty nepatří k této hře.");

        // Validate quests belong to this game (or are catalog quests with null GameId)
        if (dto.QuestIds.Count > 0)
        {
            var validQuests = await db.Quests
                .Where(q => dto.QuestIds.Contains(q.Id) && (q.GameId == gameId || q.GameId == null))
                .Select(q => q.Id)
                .ToListAsync();
            if (validQuests.Count != dto.QuestIds.Distinct().Count())
                return TypedResults.BadRequest("Některé questy nepatří k této hře ani do katalogu.");
        }

        var ev = new GameEvent
        {
            GameId = gameId,
            Name = dto.Name,
            Description = dto.Description
        };

        foreach (var slotId in dto.TimeSlotIds.Distinct())
            ev.EventTimeSlots.Add(new GameEventTimeSlot { GameTimeSlotId = slotId });
        foreach (var locId in dto.LocationIds.Distinct())
            ev.EventLocations.Add(new GameEventLocation { LocationId = locId });
        foreach (var questId in dto.QuestIds.Distinct())
            ev.EventQuests.Add(new GameEventQuest { QuestId = questId });
        foreach (var n in dto.Npcs.DistinctBy(x => x.NpcId))
            ev.EventNpcs.Add(new GameEventNpc { NpcId = n.NpcId, RoleInEvent = n.RoleInEvent });

        db.GameEvents.Add(ev);
        await db.SaveChangesAsync();

        // Re-fetch with includes for response
        var saved = await db.GameEvents
            .Include(e => e.EventTimeSlots).ThenInclude(ets => ets.TimeSlot)
            .Include(e => e.EventLocations).ThenInclude(el => el.Location)
            .Include(e => e.EventQuests).ThenInclude(eq => eq.Quest)
            .Include(e => e.EventNpcs).ThenInclude(en => en.Npc)
            .FirstAsync(e => e.Id == ev.Id);

        var gameNpcs = await db.GameNpcs.Where(gn => gn.GameId == gameId).ToListAsync();
        return TypedResults.Created($"/api/events/{ev.Id}", ToDetailDto(saved, gameNpcs));
    }

    private static async Task<Results<Ok<GameEventDetailDto>, NotFound, BadRequest<string>>> Update(
        int id, UpdateGameEventDto dto, WorldDbContext db)
    {
        if (dto.TimeSlotIds.Count == 0)
            return TypedResults.BadRequest("Událost musí mít alespoň jeden časový slot.");

        var ev = await db.GameEvents
            .Where(e => e.Id == id && !e.IsDeleted)
            .Include(e => e.EventTimeSlots)
            .Include(e => e.EventLocations)
            .Include(e => e.EventQuests)
            .Include(e => e.EventNpcs)
            .FirstOrDefaultAsync();

        if (ev is null) return TypedResults.NotFound();

        // Validate time slots belong to this game
        var validSlots = await db.GameTimeSlots
            .Where(ts => ts.GameId == ev.GameId && dto.TimeSlotIds.Contains(ts.Id))
            .Select(ts => ts.Id)
            .ToListAsync();
        if (validSlots.Count != dto.TimeSlotIds.Distinct().Count())
            return TypedResults.BadRequest("Některé časové sloty nepatří k této hře.");

        // Validate quests
        if (dto.QuestIds.Count > 0)
        {
            var validQuests = await db.Quests
                .Where(q => dto.QuestIds.Contains(q.Id) && (q.GameId == ev.GameId || q.GameId == null))
                .Select(q => q.Id)
                .ToListAsync();
            if (validQuests.Count != dto.QuestIds.Distinct().Count())
                return TypedResults.BadRequest("Některé questy nepatří k této hře ani do katalogu.");
        }

        ev.Name = dto.Name;
        ev.Description = dto.Description;
        ev.UpdatedAtUtc = DateTime.UtcNow;

        // Replace all junctions
        ev.EventTimeSlots.Clear();
        ev.EventLocations.Clear();
        ev.EventQuests.Clear();
        ev.EventNpcs.Clear();

        foreach (var slotId in dto.TimeSlotIds.Distinct())
            ev.EventTimeSlots.Add(new GameEventTimeSlot { GameTimeSlotId = slotId });
        foreach (var locId in dto.LocationIds.Distinct())
            ev.EventLocations.Add(new GameEventLocation { LocationId = locId });
        foreach (var questId in dto.QuestIds.Distinct())
            ev.EventQuests.Add(new GameEventQuest { QuestId = questId });
        foreach (var n in dto.Npcs.DistinctBy(x => x.NpcId))
            ev.EventNpcs.Add(new GameEventNpc { NpcId = n.NpcId, RoleInEvent = n.RoleInEvent });

        await db.SaveChangesAsync();

        // Re-fetch with navigation props for response
        var saved = await db.GameEvents
            .Include(e => e.EventTimeSlots).ThenInclude(ets => ets.TimeSlot)
            .Include(e => e.EventLocations).ThenInclude(el => el.Location)
            .Include(e => e.EventQuests).ThenInclude(eq => eq.Quest)
            .Include(e => e.EventNpcs).ThenInclude(en => en.Npc)
            .FirstAsync(e => e.Id == id);

        var gameNpcs = await db.GameNpcs.Where(gn => gn.GameId == ev.GameId).ToListAsync();
        return TypedResults.Ok(ToDetailDto(saved, gameNpcs));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var ev = await db.GameEvents.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (ev is null) return TypedResults.NotFound();

        ev.IsDeleted = true;
        ev.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<UserScheduleDto>> GetUserSchedule(
        int personId, int gameId, WorldDbContext db)
    {
        var myNpcIds = await db.GameNpcs
            .Where(gn => gn.GameId == gameId && gn.PlayedByPersonId == personId)
            .Select(gn => gn.NpcId)
            .ToListAsync();

        if (myNpcIds.Count == 0)
            return TypedResults.Ok(new UserScheduleDto(personId, gameId, []));

        var events = await db.GameEvents
            .Where(e => e.GameId == gameId && !e.IsDeleted &&
                        e.EventNpcs.Any(en => myNpcIds.Contains(en.NpcId)))
            .Include(e => e.EventTimeSlots).ThenInclude(ets => ets.TimeSlot)
            .Include(e => e.EventLocations).ThenInclude(el => el.Location)
            .Include(e => e.EventQuests).ThenInclude(eq => eq.Quest)
            .Include(e => e.EventNpcs).ThenInclude(en => en.Npc)
            .ToListAsync();

        var ordered = events
            .OrderBy(e => e.EventTimeSlots.Count > 0
                ? e.EventTimeSlots.Min(ets => ets.TimeSlot.StartTime)
                : DateTime.MaxValue)
            .ToList();

        var result = new List<UserScheduleEventDto>();
        foreach (var e in ordered)
        {
            foreach (var en in e.EventNpcs.Where(en => myNpcIds.Contains(en.NpcId)))
            {
                result.Add(new UserScheduleEventDto(
                    e.Id, e.Name, e.Description,
                    e.EventTimeSlots.Select(ets => new GameEventTimeSlotDto(
                        ets.TimeSlot.Id, ets.TimeSlot.StartTime,
                        (decimal)ets.TimeSlot.Duration.TotalHours,
                        ets.TimeSlot.InGameYear)).ToList(),
                    e.EventLocations.Select(el => el.Location.Name).ToList(),
                    e.EventQuests.Select(eq => eq.Quest.Name).ToList(),
                    en.Npc.Name, en.Npc.Role, en.RoleInEvent));
            }
        }

        return TypedResults.Ok(new UserScheduleDto(personId, gameId, result));
    }

    private static GameEventDetailDto ToDetailDto(GameEvent e, List<GameNpc> gameNpcs)
    {
        return new GameEventDetailDto(
            e.Id, e.GameId, e.Name, e.Description,
            e.EventTimeSlots.Select(ets => new GameEventTimeSlotDto(
                ets.TimeSlot.Id, ets.TimeSlot.StartTime,
                (decimal)ets.TimeSlot.Duration.TotalHours,
                ets.TimeSlot.InGameYear)).ToList(),
            e.EventLocations.Select(el => new GameEventLocationRefDto(
                el.Location.Id, el.Location.Name)).ToList(),
            e.EventQuests.Select(eq => new GameEventQuestRefDto(
                eq.Quest.Id, eq.Quest.Name)).ToList(),
            e.EventNpcs.Select(en =>
            {
                var gn = gameNpcs.FirstOrDefault(g => g.NpcId == en.NpcId);
                return new GameEventNpcRefDto(
                    en.Npc.Id, en.Npc.Name, en.Npc.Role, en.RoleInEvent,
                    gn?.PlayedByName, gn?.PlayedByEmail);
            }).ToList(),
            e.CreatedAtUtc, e.UpdatedAtUtc);
    }
}
