using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
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
        var slots = await db.GameTimeSlots
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.StartTime)
            .Select(s => new GameTimeSlotDto(s.Id, s.InGameYear, s.StartTime, (int)s.Duration.TotalHours, s.Rules, s.BattlefieldBonusId, s.GameId))
            .ToListAsync();
        return TypedResults.Ok(slots);
    }

    private static async Task<Created<GameTimeSlotDto>> CreateSlot(CreateGameTimeSlotDto dto, WorldDbContext db)
    {
        var s = new GameTimeSlot
        {
            GameId = dto.GameId, StartTime = dto.StartTime, Duration = TimeSpan.FromHours(dto.DurationHours),
            InGameYear = dto.InGameYear, Rules = dto.Rules, BattlefieldBonusId = dto.BattlefieldBonusId
        };
        db.GameTimeSlots.Add(s);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/timeline/slots/{s.Id}",
            new GameTimeSlotDto(s.Id, s.InGameYear, s.StartTime, (int)s.Duration.TotalHours, s.Rules, s.BattlefieldBonusId, s.GameId));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateSlot(int id, UpdateGameTimeSlotDto dto, WorldDbContext db)
    {
        var s = await db.GameTimeSlots.FindAsync(id);
        if (s is null) return TypedResults.NotFound();
        s.StartTime = dto.StartTime; s.Duration = TimeSpan.FromHours(dto.DurationHours); s.InGameYear = dto.InGameYear;
        s.Rules = dto.Rules; s.BattlefieldBonusId = dto.BattlefieldBonusId;
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
            GameId = dto.GameId, AttackBonus = dto.AttackBonus, DefenseBonus = dto.DefenseBonus,
            Name = dto.Name, Description = dto.Description
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
        b.Name = dto.Name; b.AttackBonus = dto.AttackBonus; b.DefenseBonus = dto.DefenseBonus; b.Description = dto.Description;
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
}
