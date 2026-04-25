using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
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

    private static async Task<Ok<List<SpellListDto>>> GetAll(WorldDbContext db, HttpContext http)
    {
        var rows = await db.Spells
            .OrderBy(s => s.Level)
            .ThenBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Level,
                s.School,
                s.IsScroll,
                s.IsReaction,
                s.IsLearnable,
                s.ManaCost,
                s.MinMageLevel,
                s.Price,
                s.Effect,
                s.Description,
                s.ImagePath
            })
            .ToListAsync();
        var spells = rows.Select(r => new SpellListDto(
            r.Id, r.Name, r.Level, r.School,
            r.IsScroll, r.IsReaction, r.IsLearnable,
            r.ManaCost, r.MinMageLevel, r.Price,
            r.Effect, r.Description,
            ImagePath: r.ImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "spells", r.Id, "small"))).ToList();
        return TypedResults.Ok(spells);
    }

    private static async Task<Results<Ok<SpellDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        // Issue #181 — project the ScrollItem join in a single query so the
        // client can render the "Zobrazit jako předmět" link without a second
        // round-trip. Left join (s.ScrollItem != null fan-out) handles the
        // unlinked case naturally.
        var dto = await db.Spells
            .Where(s => s.Id == id)
            .Select(s => new SpellDetailDto(
                s.Id, s.Name, s.Level, s.ManaCost, s.School,
                s.IsScroll, s.IsReaction, s.IsLearnable, s.MinMageLevel, s.Price,
                s.Effect, s.Description, s.ImagePath,
                ScrollItemId: s.ScrollItemId,
                ScrollItemName: s.ScrollItem != null ? s.ScrollItem.Name : null))
            .FirstOrDefaultAsync();
        if (dto is null) return TypedResults.NotFound();
        return TypedResults.Ok(dto);
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

    private static async Task<Results<NoContent, NotFound, Conflict<string>, ProblemHttpResult>> Update(
        int id, UpdateSpellDto dto, WorldDbContext db)
    {
        var s = await db.Spells.FindAsync(id);
        if (s is null) return TypedResults.NotFound();

        if (s.Name != dto.Name && await db.Spells.AnyAsync(x => x.Id != id && x.Name == dto.Name))
            return TypedResults.Conflict($"Kouzlo s názvem '{dto.Name}' už existuje.");

        // Issue #181 — validate ScrollItemId before persisting. EF will throw
        // an FK violation at SaveChangesAsync otherwise, but that bubbles as
        // an unfriendly 500. Cleaner to short-circuit with a Czech
        // ProblemDetails so the client banner shows a useful message.
        if (dto.ScrollItemId is int scrollItemId &&
            !await db.Items.AnyAsync(i => i.Id == scrollItemId))
        {
            return TypedResults.Problem(
                title: "Neplatný předmět",
                detail: $"Položka s ID {scrollItemId} neexistuje.",
                statusCode: StatusCodes.Status400BadRequest);
        }

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
        // Null clears the link; an int sets it. Non-scroll spells should
        // always send null (the client side enforces this via the
        // EffectiveScrollItemId getter).
        s.ScrollItemId = dto.ScrollItemId;

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

    private static async Task<Ok<List<GameSpellDto>>> GetByGame(int gameId, WorldDbContext db, HttpContext http)
    {
        var raw = await db.GameSpells
            .Where(gs => gs.GameId == gameId)
            .OrderBy(gs => gs.Spell.Level)
            .ThenBy(gs => gs.Spell.Name)
            .Select(gs => new
            {
                gs.Id,
                gs.GameId,
                gs.SpellId,
                SpellName = gs.Spell.Name,
                gs.Spell.Level,
                gs.Spell.School,
                gs.Price,
                gs.IsFindable,
                gs.AvailabilityNotes,
                CatalogPrice = gs.Spell.Price,
                SpellImagePath = gs.Spell.ImagePath
            })
            .ToListAsync();
        var rows = raw.Select(r => new GameSpellDto(
            r.Id, r.GameId, r.SpellId, r.SpellName, r.Level, r.School,
            r.Price, r.IsFindable, r.AvailabilityNotes, r.CatalogPrice,
            ImagePath: r.SpellImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.SpellImagePath) ? null : ImageEndpoints.ThumbUrl(http, "spells", r.SpellId, "small"))).ToList();
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Created<GameSpellDto>, Conflict<string>, NotFound<string>>> CreateGameSpell(
        CreateGameSpellDto dto, WorldDbContext db, HttpContext http)
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
                gs.Price, gs.IsFindable, gs.AvailabilityNotes, spell.Price,
                ImagePath: spell.ImagePath,
                ImageUrl: string.IsNullOrWhiteSpace(spell.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "spells", gs.SpellId, "small")));
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
