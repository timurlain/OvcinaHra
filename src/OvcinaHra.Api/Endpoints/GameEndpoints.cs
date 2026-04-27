using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class GameEndpoints
{
    public static RouteGroupBuilder MapGameEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/games").WithTags("Games");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapPost("/{id:int}/link", LinkToRegistrace);
        group.MapDelete("/{id:int}/link", UnlinkFromRegistrace);

        // Issue #3: server-side proxy for the "Propojit s registrací" picker.
        // Browsers never see the integration API key; this endpoint inherits
        // the parent group's RequireAuthorization gate so only logged-in
        // organizers can trigger the upstream call.
        group.MapGet("/registrace-available", GetRegistraceAvailable);

        // Map overlay (issue #96) — free-draw shapes per game, stored as JSON blob.
        group.MapGet("/{id:int}/overlay", GetOverlay);
        group.MapPut("/{id:int}/overlay", UpdateOverlay);

        group.MapGet("/{gameId:int}/skills", GetGameSkills);
        group.MapGet("/{gameId:int}/skills/{gameSkillId:int}", GetGameSkillById);
        group.MapPost("/{gameId:int}/skills", CreateGameSkill);
        group.MapPut("/{gameId:int}/skills/{gameSkillId:int}", UpdateGameSkill);
        group.MapDelete("/{gameId:int}/skills/{gameSkillId:int}", DeleteGameSkill);
        group.MapPost("/{gameId:int}/quests/bulk", BulkAddQuests);
        group.MapDelete("/{gameId:int}/locations/{locationId:int}", RemoveLocationFromGame);

        return group;
    }

    private static async Task<Results<Ok<BulkAddQuestsResponse>, NotFound>> BulkAddQuests(
        int gameId, BulkAddQuestsRequest request, WorldDbContext db, HttpContext http)
    {
        if (!await db.Games.AnyAsync(g => g.Id == gameId))
            return TypedResults.NotFound();

        var requestedIds = request.QuestCatalogueIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (requestedIds.Count == 0)
            return TypedResults.Ok(new BulkAddQuestsResponse([], []));

        var catalogueNames = await db.Quests
            .AsNoTracking()
            .Where(q => q.GameId == null && requestedIds.Contains(q.Id))
            .Select(q => new { q.Id, q.Name })
            .ToDictionaryAsync(q => q.Id, q => q.Name);

        var existingNames = (await db.Quests
            .AsNoTracking()
            .Where(q => q.GameId == gameId)
            .Select(q => q.Name)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = new List<int>();
        var skipped = new List<int>();

        foreach (var catalogueId in requestedIds)
        {
            if (!catalogueNames.TryGetValue(catalogueId, out var name) || existingNames.Contains(name))
            {
                skipped.Add(catalogueId);
                continue;
            }

            var result = await QuestEndpoints.CopyQuestToGameAsync(catalogueId, gameId, db, http);
            if (result is null)
            {
                skipped.Add(catalogueId);
                continue;
            }

            added.Add(catalogueId);
            existingNames.Add(name);
        }

        return TypedResults.Ok(new BulkAddQuestsResponse(added, skipped));
    }

    private static async Task<Ok<List<GameListDto>>> GetAll(WorldDbContext db)
    {
        var games = await db.Games
            .OrderByDescending(g => g.StartDate)
            .Select(g => new GameListDto(g.Id, g.Name, g.Edition, g.StartDate, g.EndDate, g.Status, g.ExternalGameId))
            .ToListAsync();

        return TypedResults.Ok(games);
    }

    private static async Task<Results<Ok<GameDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new GameDetailDto(
            game.Id, game.Name, game.Edition, game.StartDate, game.EndDate, game.Status, game.ImagePath, game.ExternalGameId,
            game.BoundingBoxSwLat, game.BoundingBoxSwLng, game.BoundingBoxNeLat, game.BoundingBoxNeLng));
    }

    private static async Task<Created<GameDetailDto>> Create(CreateGameDto dto, WorldDbContext db)
    {
        var game = new Game
        {
            Name = dto.Name,
            Edition = dto.Edition,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Status = dto.Status
        };

        db.Games.Add(game);
        await db.SaveChangesAsync();

        var result = new GameDetailDto(
            game.Id, game.Name, game.Edition, game.StartDate, game.EndDate, game.Status, game.ImagePath, game.ExternalGameId,
            game.BoundingBoxSwLat, game.BoundingBoxSwLng, game.BoundingBoxNeLat, game.BoundingBoxNeLng);

        return TypedResults.Created($"/api/games/{game.Id}", result);
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Update(int id, UpdateGameDto dto, WorldDbContext db)
    {
        // Bounding-box validation: all-or-nothing + SW <= NE on both axes.
        // Partial corners or inverted SW/NE would later break fitBounds + the
        // rectangle overlay on the client.
        var bboxFields = new[] { dto.BoundingBoxSwLat, dto.BoundingBoxSwLng, dto.BoundingBoxNeLat, dto.BoundingBoxNeLng };
        var bboxNonNullCount = bboxFields.Count(v => v.HasValue);
        if (bboxNonNullCount is not (0 or 4))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["BoundingBox"] = ["Bounding box musí mít všechny čtyři rohy nastaveny, nebo všechny čtyři prázdné."]
            });
        }
        if (bboxNonNullCount == 4)
        {
            if (dto.BoundingBoxSwLat!.Value > dto.BoundingBoxNeLat!.Value
                || dto.BoundingBoxSwLng!.Value > dto.BoundingBoxNeLng!.Value)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["BoundingBox"] = ["Jihozápadní roh musí mít menší souřadnice než severovýchodní (SW.lat ≤ NE.lat, SW.lng ≤ NE.lng)."]
                });
            }
        }

        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        game.Name = dto.Name;
        game.Edition = dto.Edition;
        game.StartDate = dto.StartDate;
        game.EndDate = dto.EndDate;
        game.Status = dto.Status;
        game.BoundingBoxSwLat = dto.BoundingBoxSwLat;
        game.BoundingBoxSwLng = dto.BoundingBoxSwLng;
        game.BoundingBoxNeLat = dto.BoundingBoxNeLat;
        game.BoundingBoxNeLng = dto.BoundingBoxNeLng;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>>> LinkToRegistrace(
        int id, LinkGameDto dto, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        // Issue #3: pre-check before relying on the UNIQUE INDEX so the
        // organizer gets a friendly Czech message rather than a 500. The
        // unique index (filtered, NULL-safe) is still the authoritative
        // guard against concurrent races.
        var conflict = await db.Games
            .Where(g => g.Id != id && g.ExternalGameId == dto.ExternalGameId)
            .Select(g => new { g.Id, g.Name })
            .FirstOrDefaultAsync();
        if (conflict is not null)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Registrace už je propojená.",
                Detail = $"Hra v registraci #{dto.ExternalGameId} je už propojená s hrou „{conflict.Name}“ (#{conflict.Id}). Nejdřív zruš to staré propojení.",
                Status = StatusCodes.Status409Conflict
            });
        }

        game.ExternalGameId = dto.ExternalGameId;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race against another concurrent link — surface the same 409.
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Registrace už je propojená.",
                Detail = $"Hra v registraci #{dto.ExternalGameId} byla mezitím propojená s jinou hrou.",
                Status = StatusCodes.Status409Conflict
            });
        }
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> UnlinkFromRegistrace(int id, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        game.ExternalGameId = null;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Issue #3 — returns the registrace games available for linking.
    /// Filters out games already linked locally so the picker only shows
    /// candidates the organizer can actually pick. Wraps upstream
    /// failures in a 502 so callers can distinguish "registrace
    /// unreachable/unauthorized" from an internal 500.
    /// </summary>
    private static async Task<Results<Ok<List<RegistraceGameDto>>, ProblemHttpResult>> GetRegistraceAvailable(
        RegistraceGameService registrace, WorldDbContext db, CancellationToken ct)
    {
        IReadOnlyList<RegistraceGameDto> upstream;
        try
        {
            upstream = await registrace.GetAvailableAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                title: "Registrace nedostupná.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var alreadyLinked = await db.Games
            .Where(g => g.ExternalGameId != null)
            .Select(g => g.ExternalGameId!.Value)
            .ToListAsync(ct);
        var linkedSet = alreadyLinked.ToHashSet();
        var filtered = upstream.Where(g => !linkedSet.Contains(g.Id)).ToList();
        return TypedResults.Ok(filtered);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        // Npgsql surfaces unique-violation as PostgresException with SqlState
        // 23505 (PostgresErrorCodes.UniqueViolation). Direct type check is
        // compile-time safe — the migration's filtered UNIQUE INDEX is
        // Postgres-specific anyway, so binding to the Npgsql type here is
        // honest, not a leak.
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    // ----- Map overlay (issue #96) ----------------------------------------

    /// <summary>Max serialized overlay payload — 256 KiB, enough for ~3000 freehand points.</summary>
    private const int OverlayMaxBytes = 256 * 1024;

    /// <summary>
    /// JsonSerializerOptions scoped to the overlay endpoints. Defaults are
    /// fine — the <see cref="MapOverlayShape"/> hierarchy carries its own
    /// JsonPolymorphic attributes so the discriminator is written/read
    /// automatically. Shared instance avoids per-request allocation.
    /// </summary>
    private static readonly JsonSerializerOptions OverlayJsonOptions = new() { WriteIndented = false };

    private static async Task<Results<Ok<MapOverlayDto>, NoContent, NotFound>> GetOverlay(int id, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        if (string.IsNullOrWhiteSpace(game.OverlayJson))
            return TypedResults.NoContent();

        // Best-effort deserialize: if a stored overlay is corrupt (e.g. schema
        // drift from a future shape type, or a hand-edited row), treat as
        // empty rather than 500 — the client can then re-save a valid
        // overlay. System.Text.Json signals an unknown polymorphic
        // discriminator with NotSupportedException (not JsonException), so
        // both must be caught here.
        try
        {
            var dto = JsonSerializer.Deserialize<MapOverlayDto>(game.OverlayJson, OverlayJsonOptions);
            return dto is null ? TypedResults.NoContent() : TypedResults.Ok(dto);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return TypedResults.NoContent();
        }
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UpdateOverlay(
        int id, MapOverlayDto dto, WorldDbContext db)
    {
        // § _review-instincts #1: resource lookup BEFORE validation.
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        var serialized = JsonSerializer.Serialize(dto, OverlayJsonOptions);
        var byteCount = Encoding.UTF8.GetByteCount(serialized);
        if (byteCount > OverlayMaxBytes)
        {
            return TypedResults.Problem(
                title: "Překryv je příliš velký",
                detail: $"Maximální velikost překryvu je {OverlayMaxBytes / 1024} KiB, odeslaná data mají {byteCount / 1024} KiB.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Round-trip guard — make sure the JSON we're about to persist will
        // re-deserialize cleanly. Otherwise a payload with an unknown
        // polymorphic shape (server is on an older build than the client
        // during a rolling deploy) would poison the field and every
        // subsequent GET would crash the editor.
        try
        {
            _ = JsonSerializer.Deserialize<MapOverlayDto>(serialized, OverlayJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return TypedResults.Problem(
                title: "Neznámý tvar v překryvu",
                detail: "Některý z odeslaných tvarů nebyl rozpoznán. Aktualizujte aplikaci a zkuste překryv uložit znovu.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        game.OverlayJson = serialized;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<GameSkillDto>>, NotFound>> GetGameSkills(
        int gameId, WorldDbContext db)
    {
        var gameExists = await db.Games.AnyAsync(g => g.Id == gameId);
        if (!gameExists) return TypedResults.NotFound();

        var skills = await db.GameSkills
            .Where(gs => gs.GameId == gameId)
            .Include(gs => gs.BuildingRequirements)
            .OrderBy(gs => gs.Name)
            .ToListAsync();

        IReadOnlyList<GameSkillDto> dtos = skills.Select(ToDto).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<GameSkillDto>, NotFound>> GetGameSkillById(
        int gameId, int gameSkillId, WorldDbContext db)
    {
        var gameSkill = await db.GameSkills
            .Include(gs => gs.BuildingRequirements)
            .SingleOrDefaultAsync(gs => gs.Id == gameSkillId);
        if (gameSkill is null || gameSkill.GameId != gameId) return TypedResults.NotFound();

        return TypedResults.Ok(ToDto(gameSkill));
    }

    private static async Task<Results<Created<GameSkillDto>, NotFound, BadRequest<ProblemDetails>, Conflict<ProblemDetails>>> CreateGameSkill(
        int gameId, CreateGameSkillRequest dto, WorldDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Název dovednosti je povinný.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (dto.XpCost < 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Cena v XP nemůže být záporná.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (dto.LevelRequirement is < 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Požadavek na úroveň nemůže být záporný.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var gameExists = await db.Games.AnyAsync(g => g.Id == gameId);
        if (!gameExists) return TypedResults.NotFound();

        if (dto.TemplateSkillId is int tid)
        {
            var templateExists = await db.Skills.AnyAsync(s => s.Id == tid);
            if (!templateExists)
            {
                return TypedResults.BadRequest(new ProblemDetails
                {
                    Title = "Šablona dovednosti neexistuje.",
                    Detail = $"Šablona dovednosti s ID {tid} nebyla nalezena.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var templateConflict = await db.GameSkills.AnyAsync(gs => gs.GameId == gameId && gs.TemplateSkillId == tid);
            if (templateConflict)
            {
                return TypedResults.Conflict(new ProblemDetails
                {
                    Title = "Dovednost pro tuto šablonu již v této hře existuje.",
                    Detail = $"Hra s ID {gameId} již obsahuje dovednost vytvořenou ze šablony s ID {tid}.",
                    Status = StatusCodes.Status409Conflict
                });
            }
        }

        var buildingIds = dto.BuildingRequirementIds?.Distinct().ToList() ?? [];
        var buildingError = await ValidateBuildingIdsAsync(buildingIds, db);
        if (buildingError is not null) return TypedResults.BadRequest(buildingError);

        var nameConflict = await db.GameSkills.AnyAsync(gs => gs.GameId == gameId && gs.Name == dto.Name);
        if (nameConflict)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Dovednost s tímto názvem již v této hře existuje.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var gameSkill = new GameSkill
        {
            GameId = gameId,
            TemplateSkillId = dto.TemplateSkillId,
            Name = dto.Name,
            Category = dto.Category,
            ClassRestriction = dto.ClassRestriction,
            Effect = dto.Effect,
            RequirementNotes = dto.RequirementNotes,
            XpCost = dto.XpCost,
            LevelRequirement = dto.LevelRequirement,
            BuildingRequirements = buildingIds
                .Select(bid => new GameSkillBuildingRequirement { BuildingId = bid })
                .ToList()
        };

        db.GameSkills.Add(gameSkill);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/games/{gameId}/skills/{gameSkill.Id}", ToDto(gameSkill));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>, Conflict<ProblemDetails>>> UpdateGameSkill(
        int gameId, int gameSkillId, UpdateGameSkillRequest dto, WorldDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Název dovednosti je povinný.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (dto.XpCost < 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Cena v XP nemůže být záporná.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (dto.LevelRequirement is < 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Požadavek na úroveň nemůže být záporný.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var gameSkill = await db.GameSkills
            .Include(gs => gs.BuildingRequirements)
            .SingleOrDefaultAsync(gs => gs.Id == gameSkillId);
        if (gameSkill is null || gameSkill.GameId != gameId) return TypedResults.NotFound();

        var buildingIds = dto.BuildingRequirementIds?.Distinct().ToList() ?? [];
        var buildingError = await ValidateBuildingIdsAsync(buildingIds, db);
        if (buildingError is not null) return TypedResults.BadRequest(buildingError);

        var nameConflict = await db.GameSkills
            .AnyAsync(gs => gs.GameId == gameId && gs.Id != gameSkillId && gs.Name == dto.Name);
        if (nameConflict)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Dovednost s tímto názvem již v této hře existuje.",
                Status = StatusCodes.Status409Conflict
            });
        }

        gameSkill.Name = dto.Name;
        gameSkill.Category = dto.Category;
        gameSkill.ClassRestriction = dto.ClassRestriction;
        gameSkill.Effect = dto.Effect;
        gameSkill.RequirementNotes = dto.RequirementNotes;
        gameSkill.XpCost = dto.XpCost;
        gameSkill.LevelRequirement = dto.LevelRequirement;

        var currentIds = gameSkill.BuildingRequirements.Select(r => r.BuildingId).ToHashSet();
        var desiredIds = buildingIds.ToHashSet();

        foreach (var req in gameSkill.BuildingRequirements.Where(r => !desiredIds.Contains(r.BuildingId)).ToList())
        {
            gameSkill.BuildingRequirements.Remove(req);
        }
        foreach (var bid in desiredIds.Where(b => !currentIds.Contains(b)))
        {
            gameSkill.BuildingRequirements.Add(new GameSkillBuildingRequirement { GameSkillId = gameSkillId, BuildingId = bid });
        }

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>>> DeleteGameSkill(
        int gameId, int gameSkillId, WorldDbContext db)
    {
        var gameSkill = await db.GameSkills
            .SingleOrDefaultAsync(gs => gs.Id == gameSkillId);
        if (gameSkill is null || gameSkill.GameId != gameId) return TypedResults.NotFound();

        var usedInRecipe = await db.CraftingSkillRequirements
            .AnyAsync(csr => csr.GameSkillId == gameSkillId);
        if (usedInRecipe)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Nelze odebrat dovednost — je vyžadována alespoň jedním receptem v této hře.",
                Status = StatusCodes.Status409Conflict
            });
        }

        db.GameSkills.Remove(gameSkill);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveLocationFromGame(
        int gameId, int locationId, WorldDbContext db)
    {
        var link = await db.GameLocations.FindAsync(gameId, locationId);
        if (link is null) return TypedResults.NotFound();

        db.GameLocations.Remove(link);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<ProblemDetails?> ValidateBuildingIdsAsync(
        IReadOnlyCollection<int> buildingIds, WorldDbContext db)
    {
        if (buildingIds.Count == 0) return null;

        var knownBuildingCount = await db.Buildings
            .CountAsync(b => buildingIds.Contains(b.Id));
        if (knownBuildingCount != buildingIds.Count)
        {
            return new ProblemDetails
            {
                Title = "Některé z požadovaných budov neexistují.",
                Status = StatusCodes.Status400BadRequest
            };
        }
        return null;
    }

    private static GameSkillDto ToDto(GameSkill gs) => new(
        gs.Id,
        gs.GameId,
        gs.TemplateSkillId,
        gs.Name,
        gs.Category,
        gs.ClassRestriction,
        gs.Effect,
        gs.RequirementNotes,
        gs.ImagePath,
        gs.XpCost,
        gs.LevelRequirement,
        gs.BuildingRequirements.Select(r => r.BuildingId).ToList());
}
