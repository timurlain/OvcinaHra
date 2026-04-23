using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class SpellEndpoints
{
    public static RouteGroupBuilder MapSpellEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/spells").WithTags("Spells");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Per-game spell configuration
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/game-spell", CreateGameSpell);
        group.MapPut("/game-spell/{gameId:int}/{spellId:int}", UpdateGameSpell);
        group.MapDelete("/game-spell/{gameId:int}/{spellId:int}", DeleteGameSpell);

        return group;
    }

    // ── Catalog ────────────────────────────────────────────────────────

    private static async Task<Ok<List<SpellListDto>>> GetAll(WorldDbContext db)
    {
        var spells = await db.Spells
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .Select(s => new SpellListDto(
                s.Id, s.Name, s.Level, s.School,
                s.IsScroll, s.IsReaction, s.IsLearnable,
                s.ManaCost, s.MinMageLevel, s.Price,
                s.Effect, s.Description))
            .ToListAsync();
        return TypedResults.Ok(spells);
    }

    private static async Task<Results<Ok<SpellDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var s = await db.Spells.FindAsync(id);
        if (s is null) return TypedResults.NotFound();

        return TypedResults.Ok(new SpellDetailDto(
            s.Id, s.Name, s.Level, s.ManaCost, s.School,
            s.IsScroll, s.IsReaction, s.IsLearnable, s.MinMageLevel, s.Price,
            s.Effect, s.Description, s.ImagePath));
    }

    private static async Task<Results<Created<SpellDetailDto>, Conflict<string>>> Create(
        CreateSpellDto dto, WorldDbContext db)
    {
        if (await db.Spells.AnyAsync(x => x.Name == dto.Name))
            return TypedResults.Conflict($"Kouzlo s názvem '{dto.Name}' už existuje.");

        var s = new Spell
        {
            Name = dto.Name,
            Level = dto.Level,
            ManaCost = dto.ManaCost,
            School = dto.School,
            IsScroll = dto.IsScroll,
            IsReaction = dto.IsReaction,
            IsLearnable = dto.IsLearnable,
            MinMageLevel = dto.MinMageLevel,
            Price = dto.Price,
            Effect = dto.Effect,
            Description = dto.Description
        };
        db.Spells.Add(s);
        await db.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/spells/{s.Id}",
            new SpellDetailDto(s.Id, s.Name, s.Level, s.ManaCost, s.School,
                s.IsScroll, s.IsReaction, s.IsLearnable, s.MinMageLevel, s.Price,
                s.Effect, s.Description, s.ImagePath));
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> Update(
        int id, UpdateSpellDto dto, WorldDbContext db)
    {
        var s = await db.Spells.FindAsync(id);
        if (s is null) return TypedResults.NotFound();

        if (s.Name != dto.Name && await db.Spells.AnyAsync(x => x.Id != id && x.Name == dto.Name))
            return TypedResults.Conflict($"Kouzlo s názvem '{dto.Name}' už existuje.");

        s.Name = dto.Name;
        s.Level = dto.Level;
        s.ManaCost = dto.ManaCost;
        s.School = dto.School;
        s.IsScroll = dto.IsScroll;
        s.IsReaction = dto.IsReaction;
        s.IsLearnable = dto.IsLearnable;
        s.MinMageLevel = dto.MinMageLevel;
        s.Price = dto.Price;
        s.Effect = dto.Effect;
        s.Description = dto.Description;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var s = await db.Spells.FindAsync(id);
        if (s is null) return TypedResults.NotFound();

        db.Spells.Remove(s);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // ── Per-game ──────────────────────────────────────────────────────

    private static async Task<Ok<List<GameSpellDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var rows = await db.GameSpells
            .Where(gs => gs.GameId == gameId)
            .Include(gs => gs.Spell)
            .OrderBy(gs => gs.Spell.Level)
            .ThenBy(gs => gs.Spell.Name)
            .Select(gs => new GameSpellDto(
                gs.Id, gs.GameId, gs.SpellId, gs.Spell.Name, gs.Spell.Level, gs.Spell.School,
                gs.Price, gs.IsFindable, gs.AvailabilityNotes, gs.Spell.Price))
            .ToListAsync();
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Created<GameSpellDto>, Conflict<string>, NotFound<string>>> CreateGameSpell(
        CreateGameSpellDto dto, WorldDbContext db)
    {
        // Validate FKs up front — otherwise EF surfaces FK violations as 500.
        var spell = await db.Spells.FindAsync(dto.SpellId);
        if (spell is null) return TypedResults.NotFound($"Kouzlo #{dto.SpellId} neexistuje.");

        if (!await db.Games.AnyAsync(g => g.Id == dto.GameId))
            return TypedResults.NotFound($"Hra #{dto.GameId} neexistuje.");

        if (await db.GameSpells.AnyAsync(gs => gs.GameId == dto.GameId && gs.SpellId == dto.SpellId))
            return TypedResults.Conflict("Kouzlo už je ve hře přiřazené.");

        var gs = new GameSpell
        {
            GameId = dto.GameId,
            SpellId = dto.SpellId,
            Price = dto.Price,
            IsFindable = dto.IsFindable,
            AvailabilityNotes = dto.AvailabilityNotes
        };
        db.GameSpells.Add(gs);
        await db.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/spells/by-game/{gs.GameId}",
            new GameSpellDto(gs.Id, gs.GameId, gs.SpellId, spell.Name, spell.Level, spell.School,
                gs.Price, gs.IsFindable, gs.AvailabilityNotes, spell.Price));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateGameSpell(
        int gameId, int spellId, UpdateGameSpellDto dto, WorldDbContext db)
    {
        var gs = await db.GameSpells.FirstOrDefaultAsync(x => x.GameId == gameId && x.SpellId == spellId);
        if (gs is null) return TypedResults.NotFound();

        gs.Price = dto.Price;
        gs.IsFindable = dto.IsFindable;
        gs.AvailabilityNotes = dto.AvailabilityNotes;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGameSpell(
        int gameId, int spellId, WorldDbContext db)
    {
        var gs = await db.GameSpells.FirstOrDefaultAsync(x => x.GameId == gameId && x.SpellId == spellId);
        if (gs is null) return TypedResults.NotFound();

        db.GameSpells.Remove(gs);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
