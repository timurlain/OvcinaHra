using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class MonsterEndpoints
{
    private const int MaxNotesLength = 1000;

    public static RouteGroupBuilder MapMonsterEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/monsters").WithTags("Monsters");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Per-game assignment
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/game-monster", CreateGameMonster);
        group.MapDelete("/game-monster/{gameId:int}/{monsterId:int}", DeleteGameMonster);

        // Tags
        group.MapPost("/{id:int}/tags/{tagId:int}", AddTag);
        group.MapDelete("/{id:int}/tags/{tagId:int}", RemoveTag);

        // Loot
        group.MapGet("/{id:int}/loot/{gameId:int}", GetLoot);
        group.MapPost("/loot", CreateLoot);
        group.MapDelete("/loot/{monsterId:int}/{itemId:int}/{gameId:int}", DeleteLoot);

        return group;
    }

    private static async Task<Ok<List<MonsterListDto>>> GetAll(WorldDbContext db)
    {
        var monsters = await db.Monsters
            .OrderBy(m => m.Name)
            .Select(m => new MonsterListDto(
                m.Id, m.Name, m.MonsterType, m.Category,
                m.Stats.Attack, m.Stats.Defense, m.Stats.Health,
                m.RewardXp, m.RewardMoney,
                m.Abilities, m.AiBehavior, m.RewardNotes, m.Notes,
                m.MonsterTags.OrderBy(mt => mt.Tag.Name).Select(mt => mt.Tag.Name).ToList()))
            .ToListAsync();
        return TypedResults.Ok(monsters);
    }

    private static async Task<Results<Ok<MonsterDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var m = await db.Monsters
            .Include(m => m.MonsterTags).ThenInclude(mt => mt.Tag)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (m is null) return TypedResults.NotFound();

        var tags = m.MonsterTags.Select(mt => new TagDto(mt.Tag.Id, mt.Tag.Name, mt.Tag.Kind)).ToList();
        return TypedResults.Ok(new MonsterDetailDto(
            m.Id, m.Name, m.Category, m.MonsterType, m.Abilities, m.AiBehavior,
            m.Stats.Attack, m.Stats.Defense, m.Stats.Health,
            m.RewardXp, m.RewardMoney, m.RewardNotes, m.Notes, m.ImagePath, tags));
    }

    private static async Task<Results<Created<MonsterDetailDto>, BadRequest<string>>> Create(CreateMonsterDto dto, WorldDbContext db)
    {
        if (dto.Notes?.Length > MaxNotesLength)
            return TypedResults.BadRequest($"Notes cannot exceed {MaxNotesLength} characters.");

        var m = new Monster
        {
            Name = dto.Name,
            Category = dto.Category,
            MonsterType = dto.MonsterType,
            Stats = new CombatStats(dto.Attack, dto.Defense, dto.Health),
            Abilities = dto.Abilities,
            AiBehavior = dto.AiBehavior,
            RewardXp = dto.RewardXp,
            RewardMoney = dto.RewardMoney,
            RewardNotes = dto.RewardNotes,
            Notes = dto.Notes
        };
        db.Monsters.Add(m);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/monsters/{m.Id}",
            new MonsterDetailDto(m.Id, m.Name, m.Category, m.MonsterType, m.Abilities, m.AiBehavior,
                m.Stats.Attack, m.Stats.Defense, m.Stats.Health,
                m.RewardXp, m.RewardMoney, m.RewardNotes, m.Notes, m.ImagePath, []));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> Update(int id, UpdateMonsterDto dto, WorldDbContext db)
    {
        if (dto.Notes?.Length > MaxNotesLength)
            return TypedResults.BadRequest($"Notes cannot exceed {MaxNotesLength} characters.");

        var m = await db.Monsters.FindAsync(id);
        if (m is null) return TypedResults.NotFound();

        m.Name = dto.Name;
        m.Category = dto.Category;
        m.MonsterType = dto.MonsterType;
        m.Stats = new CombatStats(dto.Attack, dto.Defense, dto.Health);
        m.Abilities = dto.Abilities;
        m.AiBehavior = dto.AiBehavior;
        m.RewardXp = dto.RewardXp;
        m.RewardMoney = dto.RewardMoney;
        m.RewardNotes = dto.RewardNotes;
        m.Notes = dto.Notes;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var m = await db.Monsters.FindAsync(id);
        if (m is null) return TypedResults.NotFound();
        db.Monsters.Remove(m);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<GameMonsterDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var monsters = await db.GameMonsters
            .Where(gm => gm.GameId == gameId)
            .Include(gm => gm.Monster)
            .OrderBy(gm => gm.Monster.Name)
            .Select(gm => new GameMonsterDto(gm.GameId, gm.MonsterId, gm.Monster.Name))
            .ToListAsync();
        return TypedResults.Ok(monsters);
    }

    private static async Task<Results<Created<GameMonsterDto>, Conflict>> CreateGameMonster(CreateGameMonsterDto dto, WorldDbContext db)
    {
        if (await db.GameMonsters.AnyAsync(gm => gm.GameId == dto.GameId && gm.MonsterId == dto.MonsterId))
            return TypedResults.Conflict();

        db.GameMonsters.Add(new GameMonster { GameId = dto.GameId, MonsterId = dto.MonsterId });
        await db.SaveChangesAsync();

        var name = (await db.Monsters.FindAsync(dto.MonsterId))?.Name ?? "";
        return TypedResults.Created($"/api/monsters/game-monster/{dto.GameId}/{dto.MonsterId}",
            new GameMonsterDto(dto.GameId, dto.MonsterId, name));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGameMonster(int gameId, int monsterId, WorldDbContext db)
    {
        var gm = await db.GameMonsters.FindAsync(gameId, monsterId);
        if (gm is null) return TypedResults.NotFound();
        db.GameMonsters.Remove(gm);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, NotFound, Conflict>> AddTag(int id, int tagId, WorldDbContext db)
    {
        if (!await db.Monsters.AnyAsync(m => m.Id == id)) return TypedResults.NotFound();
        if (await db.MonsterTagLinks.AnyAsync(mt => mt.MonsterId == id && mt.TagId == tagId))
            return TypedResults.Conflict();

        db.MonsterTagLinks.Add(new MonsterTagLink { MonsterId = id, TagId = tagId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/monsters/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveTag(int id, int tagId, WorldDbContext db)
    {
        var link = await db.MonsterTagLinks.FindAsync(id, tagId);
        if (link is null) return TypedResults.NotFound();
        db.MonsterTagLinks.Remove(link);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<MonsterLootDto>>> GetLoot(int id, int gameId, WorldDbContext db)
    {
        var loot = await db.MonsterLoots
            .Where(ml => ml.MonsterId == id && ml.GameId == gameId)
            .Include(ml => ml.Item)
            .Select(ml => new MonsterLootDto(ml.MonsterId, ml.ItemId, ml.Item.Name, ml.GameId, ml.Quantity))
            .ToListAsync();
        return TypedResults.Ok(loot);
    }

    private static async Task<Results<Created, Conflict>> CreateLoot(CreateMonsterLootDto dto, WorldDbContext db)
    {
        if (await db.MonsterLoots.AnyAsync(ml => ml.MonsterId == dto.MonsterId && ml.ItemId == dto.ItemId && ml.GameId == dto.GameId))
            return TypedResults.Conflict();

        db.MonsterLoots.Add(new MonsterLoot
        {
            MonsterId = dto.MonsterId, ItemId = dto.ItemId, GameId = dto.GameId, Quantity = dto.Quantity
        });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/monsters/{dto.MonsterId}/loot/{dto.GameId}");
    }

    private static async Task<Results<NoContent, NotFound>> DeleteLoot(int monsterId, int itemId, int gameId, WorldDbContext db)
    {
        var loot = await db.MonsterLoots.FindAsync(monsterId, itemId, gameId);
        if (loot is null) return TypedResults.NotFound();
        db.MonsterLoots.Remove(loot);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
