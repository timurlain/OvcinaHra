using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class TreasureQuestEndpoints
{
    public static RouteGroupBuilder MapTreasureQuestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/treasure-quests").WithTags("TreasureQuests");

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        group.MapPost("/{id:int}/items", AddItem);
        group.MapDelete("/{id:int}/items/{itemId:int}", RemoveItem);

        return group;
    }

    private static async Task<Ok<List<TreasureQuestListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var tqs = await db.TreasureQuests
            .Where(t => t.GameId == gameId)
            .OrderBy(t => t.Difficulty).ThenBy(t => t.Title)
            .Select(t => new TreasureQuestListDto(t.Id, t.Title, t.Difficulty, t.LocationId, t.SecretStashId, t.GameId))
            .ToListAsync();
        return TypedResults.Ok(tqs);
    }

    private static async Task<Results<Ok<TreasureQuestDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var t = await db.TreasureQuests
            .Include(t => t.TreasureItems).ThenInclude(ti => ti.Item)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (t is null) return TypedResults.NotFound();

        return TypedResults.Ok(new TreasureQuestDetailDto(
            t.Id, t.Title, t.Clue, t.Difficulty, t.LocationId, t.SecretStashId, t.GameId,
            t.TreasureItems.Select(ti => new TreasureItemDto(ti.TreasureQuestId, ti.ItemId, ti.Item.Name, ti.Count)).ToList()));
    }

    private static async Task<Results<Created<TreasureQuestListDto>, ValidationProblem>> Create(CreateTreasureQuestDto dto, WorldDbContext db)
    {
        // Exactly one of LocationId or SecretStashId
        if ((dto.LocationId is null) == (dto.SecretStashId is null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["LocationId"] = ["Musí být vyplněna buď lokace, nebo tajná skrýš (ne obojí)."]
            });
        }

        var t = new TreasureQuest
        {
            Title = dto.Title, Clue = dto.Clue, Difficulty = dto.Difficulty,
            LocationId = dto.LocationId, SecretStashId = dto.SecretStashId, GameId = dto.GameId
        };
        db.TreasureQuests.Add(t);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/treasure-quests/{t.Id}",
            new TreasureQuestListDto(t.Id, t.Title, t.Difficulty, t.LocationId, t.SecretStashId, t.GameId));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Update(int id, UpdateTreasureQuestDto dto, WorldDbContext db)
    {
        var t = await db.TreasureQuests.FindAsync(id);
        if (t is null) return TypedResults.NotFound();

        if ((dto.LocationId is null) == (dto.SecretStashId is null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["LocationId"] = ["Musí být vyplněna buď lokace, nebo tajná skrýš (ne obojí)."]
            });
        }

        t.Title = dto.Title; t.Clue = dto.Clue; t.Difficulty = dto.Difficulty;
        t.LocationId = dto.LocationId; t.SecretStashId = dto.SecretStashId;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var t = await db.TreasureQuests.FindAsync(id);
        if (t is null) return TypedResults.NotFound();
        db.TreasureQuests.Remove(t);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, Conflict>> AddItem(int id, AddTreasureItemDto dto, WorldDbContext db)
    {
        if (await db.TreasureItems.AnyAsync(ti => ti.TreasureQuestId == id && ti.ItemId == dto.ItemId))
            return TypedResults.Conflict();
        db.TreasureItems.Add(new TreasureItem { TreasureQuestId = id, ItemId = dto.ItemId, Count = dto.Count });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/treasure-quests/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveItem(int id, int itemId, WorldDbContext db)
    {
        var ti = await db.TreasureItems.FindAsync(id, itemId);
        if (ti is null) return TypedResults.NotFound();
        db.TreasureItems.Remove(ti);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
