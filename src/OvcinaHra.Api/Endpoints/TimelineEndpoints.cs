using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class TimelineEndpoints
{
    public static RouteGroupBuilder MapTimelineEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/timeline").WithTags("Timeline");

        // Time slots
        group.MapGet("/slots/by-game/{gameId:int}", GetSlotsByGame);
        group.MapPost("/slots", CreateSlot);
        group.MapPut("/slots/{id:int}", UpdateSlot);
        group.MapDelete("/slots/{id:int}", DeleteSlot);

        // Battlefield bonuses
        group.MapGet("/bonuses/by-game/{gameId:int}", GetBonusesByGame);
        group.MapPost("/bonuses", CreateBonus);
        group.MapPut("/bonuses/{id:int}", UpdateBonus);
        group.MapDelete("/bonuses/{id:int}", DeleteBonus);

        return group;
    }

    // Time slots
    private static async Task<Ok<List<GameTimeSlotDto>>> GetSlotsByGame(int gameId, WorldDbContext db)
    {
        var rows = await db.GameTimeSlots
            .AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.StartTime)
            .Select(s => new SlotProjection(
                s.Id, s.InGameYear, s.StartTime, s.Duration, s.Stage, s.Rules,
                s.BattlefieldBonusId, s.GameId,
                s.BattlefieldBonus != null ? s.BattlefieldBonus.Name : null,
                s.EventTimeSlots.Select(ets => new EventProjection(
                    ets.GameEvent.Id,
                    ets.GameEvent.Name,
                    ets.GameEvent.EventQuests.Any(),
                    ets.GameEvent.EventNpcs.Any())).ToList()))
            .ToListAsync();
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Created<GameTimeSlotDto>> CreateSlot(CreateGameTimeSlotDto dto, WorldDbContext db)
    {
        var stage = NormalizeStage(dto.Stage);
        var s = new GameTimeSlot
        {
            GameId = dto.GameId,
            StartTime = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Utc),
            Duration = TimeSpan.FromHours((double)dto.DurationHours),
            InGameYear = dto.InGameYear,
            Rules = dto.Rules,
            BattlefieldBonusId = dto.BattlefieldBonusId,
            Stage = stage
        };
        db.GameTimeSlots.Add(s);
        await db.SaveChangesAsync();

        if (dto.LinkedGameEventIds is { Count: > 0 } ids)
        {
            foreach (var eventId in ids.Distinct())
            {
                db.GameEventTimeSlots.Add(new GameEventTimeSlot { GameTimeSlotId = s.Id, GameEventId = eventId });
            }
            await db.SaveChangesAsync();
        }

        var enriched = await LoadSlotDtoAsync(s.Id, db);
        return TypedResults.Created($"/api/timeline/slots/{s.Id}", enriched);
    }

    private static async Task<Results<NoContent, NotFound>> UpdateSlot(int id, UpdateGameTimeSlotDto dto, WorldDbContext db)
    {
        var s = await db.GameTimeSlots.FindAsync(id);
        if (s is null) return TypedResults.NotFound();

        s.StartTime = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Utc);
        s.Duration = TimeSpan.FromHours((double)dto.DurationHours);
        s.InGameYear = dto.InGameYear;
        s.Rules = dto.Rules;
        s.BattlefieldBonusId = dto.BattlefieldBonusId;
        s.Stage = NormalizeStage(dto.Stage);

        if (dto.LinkedGameEventIds is { } incoming)
        {
            var desired = incoming.Distinct().ToHashSet();
            var existing = await db.GameEventTimeSlots
                .Where(ets => ets.GameTimeSlotId == id)
                .ToListAsync();

            foreach (var stale in existing.Where(ets => !desired.Contains(ets.GameEventId)))
                db.GameEventTimeSlots.Remove(stale);

            var existingIds = existing.Select(ets => ets.GameEventId).ToHashSet();
            foreach (var newId in desired.Where(eid => !existingIds.Contains(eid)))
                db.GameEventTimeSlots.Add(new GameEventTimeSlot { GameTimeSlotId = id, GameEventId = newId });
        }

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSlot(int id, WorldDbContext db)
    {
        var s = await db.GameTimeSlots.FindAsync(id);
        if (s is null) return TypedResults.NotFound();
        db.GameTimeSlots.Remove(s);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Battlefield bonuses
    private static async Task<Ok<List<BattlefieldBonusDto>>> GetBonusesByGame(int gameId, WorldDbContext db)
    {
        var bonuses = await db.BattlefieldBonuses
            .AsNoTracking()
            .Where(b => b.GameId == gameId)
            .OrderBy(b => b.Name)
            .Select(b => new BattlefieldBonusDto(b.Id, b.Name, b.AttackBonus, b.DefenseBonus, b.Description, b.ImagePath, b.GameId))
            .ToListAsync();
        return TypedResults.Ok(bonuses);
    }

    private static async Task<Created<BattlefieldBonusDto>> CreateBonus(CreateBattlefieldBonusDto dto, WorldDbContext db)
    {
        var b = new BattlefieldBonus
        {
            GameId = dto.GameId,
            AttackBonus = dto.AttackBonus,
            DefenseBonus = dto.DefenseBonus,
            Name = dto.Name,
            Description = dto.Description
        };
        db.BattlefieldBonuses.Add(b);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/timeline/bonuses/{b.Id}",
            new BattlefieldBonusDto(b.Id, b.Name, b.AttackBonus, b.DefenseBonus, b.Description, b.ImagePath, b.GameId));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateBonus(int id, UpdateBattlefieldBonusDto dto, WorldDbContext db)
    {
        var b = await db.BattlefieldBonuses.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        b.Name = dto.Name;
        b.AttackBonus = dto.AttackBonus;
        b.DefenseBonus = dto.DefenseBonus;
        b.Description = dto.Description;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteBonus(int id, WorldDbContext db)
    {
        var b = await db.BattlefieldBonuses.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        db.BattlefieldBonuses.Remove(b);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // ---- helpers ----

    private static async Task<GameTimeSlotDto> LoadSlotDtoAsync(int id, WorldDbContext db)
    {
        var row = await db.GameTimeSlots
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SlotProjection(
                s.Id, s.InGameYear, s.StartTime, s.Duration, s.Stage, s.Rules,
                s.BattlefieldBonusId, s.GameId,
                s.BattlefieldBonus != null ? s.BattlefieldBonus.Name : null,
                s.EventTimeSlots.Select(ets => new EventProjection(
                    ets.GameEvent.Id,
                    ets.GameEvent.Name,
                    ets.GameEvent.EventQuests.Any(),
                    ets.GameEvent.EventNpcs.Any())).ToList()))
            .SingleAsync();
        return ToDto(row);
    }

    private static GameTimeSlotDto ToDto(SlotProjection r) =>
        new(r.Id, r.InGameYear, r.StartTime, (decimal)r.Duration.TotalHours,
            r.Rules, r.BattlefieldBonusId, r.GameId, r.Stage, r.BonusName)
        {
            LinkedEvents = r.Events
                .Select(e => new LinkedGameEventDto(e.Id, e.Name, ClassifyKind(e.HasQuests, e.HasNpcs)))
                .ToList()
        };

    private static GameEventKind ClassifyKind(bool hasQuests, bool hasNpcs) =>
        hasQuests ? GameEventKind.Quest :
        hasNpcs ? GameEventKind.Encounter :
        GameEventKind.Other;

    // Guard against malformed integer payloads — JsonStringEnumConverter
    // catches strings, but a raw int from a non-conforming client would
    // persist as an undefined enum value (per review-instincts §5.3).
    private static TreasureQuestDifficulty NormalizeStage(TreasureQuestDifficulty stage) =>
        Enum.IsDefined(typeof(TreasureQuestDifficulty), stage) ? stage : TreasureQuestDifficulty.Start;

    private record SlotProjection(
        int Id, int? InGameYear, DateTime StartTime, TimeSpan Duration,
        TreasureQuestDifficulty Stage, string? Rules,
        int? BattlefieldBonusId, int GameId, string? BonusName,
        List<EventProjection> Events);

    private record EventProjection(int Id, string Name, bool HasQuests, bool HasNpcs);
}
