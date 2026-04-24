using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
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
        group.MapGet("/{id:int}/loot/all-games", GetLootAllGames);
        group.MapPost("/loot", CreateLoot);
        group.MapDelete("/loot/{monsterId:int}/{itemId:int}/{gameId:int}", DeleteLoot);

        // Detail-page aggregates
        group.MapGet("/{id:int}/occurrences", GetOccurrences);

        return group;
    }

    private static async Task<Ok<List<MonsterListDto>>> GetAll(WorldDbContext db, HttpContext http)
    {
        var rows = await db.Monsters
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.MonsterType,
                m.Category,
                m.Stats.Attack,
                m.Stats.Defense,
                m.Stats.Health,
                m.RewardXp,
                m.RewardMoney,
                m.Abilities,
                m.AiBehavior,
                m.RewardNotes,
                m.Notes,
                TagNames = m.MonsterTags.OrderBy(mt => mt.Tag.Name).Select(mt => mt.Tag.Name).ToList(),
                m.ImagePath
            })
            .ToListAsync();
        var monsters = rows.Select(r => new MonsterListDto(
            r.Id, r.Name, r.MonsterType, r.Category,
            r.Attack, r.Defense, r.Health,
            r.RewardXp, r.RewardMoney,
            r.Abilities, r.AiBehavior, r.RewardNotes, r.Notes,
            r.TagNames,
            ImagePath: r.ImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "monsters", r.Id, "small"))).ToList();
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

    // Issue #111 — validation surface. Previously a duplicate Name crashed
    // with a bare Postgres unique-constraint 500; empty Name / negative
    // stats / over-long Notes were enforced only at the DB layer if at all.
    // Unified Czech ProblemDetails (title "Uložení selhalo") covers them all.
    private static async Task<IResult> Create(CreateMonsterDto dto, WorldDbContext db)
    {
        var error = await ValidateMonsterAsync(db, dto.Name, dto.Attack, dto.Defense, dto.Health, dto.Notes, selfId: null);
        if (error is not null) return error;

        var m = new Monster
        {
            Name = dto.Name.Trim(),
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

    private static async Task<IResult> Update(int id, UpdateMonsterDto dto, WorldDbContext db)
    {
        var m = await db.Monsters.FindAsync(id);
        if (m is null) return TypedResults.NotFound();

        var error = await ValidateMonsterAsync(db, dto.Name, dto.Attack, dto.Defense, dto.Health, dto.Notes, selfId: id);
        if (error is not null) return error;

        m.Name = dto.Name.Trim();
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

    // Centralised Czech-facing validation for Create + Update. Returns null
    // when the payload is clean; otherwise a ProblemDetails(400). Name
    // uniqueness check ignores selfId so Update of a monster to its own
    // current name doesn't trip the duplicate check.
    private static async Task<IResult?> ValidateMonsterAsync(
        WorldDbContext db,
        string? name, int attack, int defense, int health, string? notes,
        int? selfId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Název nesmí být prázdný.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var trimmed = name.Trim();

        if (attack < 0 || defense < 0)
        {
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Útok a obrana nesmí být záporné.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (health <= 0)
        {
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Životy musí být alespoň 1.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (notes?.Length > MaxNotesLength)
        {
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Poznámka nesmí přesáhnout {MaxNotesLength} znaků.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var collision = await db.Monsters
            .Where(x => x.Name == trimmed && (selfId == null || x.Id != selfId.Value))
            .AnyAsync();
        if (collision)
        {
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Příšera s názvem „{trimmed}\" už existuje. Vyber jiný název.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
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

    private static async Task<Ok<List<MonsterLootByGameDto>>> GetLootAllGames(int id, WorldDbContext db)
    {
        var assigned = await db.GameMonsters
            .Where(gm => gm.MonsterId == id)
            .Select(gm => new { gm.GameId, GameName = gm.Game.Name, gm.Game.Edition, gm.Game.StartDate })
            .ToListAsync();

        var lootRows = await db.MonsterLoots
            .Where(ml => ml.MonsterId == id)
            .Select(ml => new
            {
                ml.GameId,
                GameName = ml.Game.Name,
                ml.Game.Edition,
                ml.Game.StartDate,
                Loot = new MonsterLootDto(ml.MonsterId, ml.ItemId, ml.Item.Name, ml.GameId, ml.Quantity)
            })
            .ToListAsync();

        var info = new Dictionary<int, (string Name, int Edition, DateOnly StartDate)>();
        foreach (var a in assigned) info[a.GameId] = (a.GameName, a.Edition, a.StartDate);
        foreach (var l in lootRows) info.TryAdd(l.GameId, (l.GameName, l.Edition, l.StartDate));

        // Index loot once by GameId so the per-game projection below is O(games + lootRows)
        // instead of the O(games × lootRows) re-scan a naive .Where would do.
        var lootByGame = lootRows.ToLookup(l => l.GameId, l => l.Loot);

        var result = info
            .OrderByDescending(kv => kv.Value.StartDate)
            .ThenBy(kv => kv.Value.Name)
            .Select(kv => new MonsterLootByGameDto(
                kv.Key,
                kv.Value.Name,
                kv.Value.Edition,
                lootByGame[kv.Key].OrderBy(d => d.ItemName).ToList()))
            .ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<List<MonsterOccurrenceDto>>> GetOccurrences(int id, WorldDbContext db)
    {
        var rows = await db.QuestEncounters
            .Where(qe => qe.MonsterId == id && qe.Quest.GameId != null)
            .Select(qe => new
            {
                GameId = qe.Quest.GameId!.Value,
                GameName = qe.Quest.Game!.Name,
                qe.Quest.Game.Edition,
                qe.QuestId,
                qe.Quantity
            })
            .ToListAsync();

        var grouped = rows
            .GroupBy(r => new { r.GameId, r.GameName, r.Edition })
            .Select(g => new MonsterOccurrenceDto(
                g.Key.GameId,
                g.Key.GameName,
                g.Key.Edition,
                g.Select(x => x.QuestId).Distinct().Count(),
                g.Sum(x => x.Quantity)))
            .OrderBy(o => o.GameName)
            .ToList();

        return TypedResults.Ok(grouped);
    }
}
