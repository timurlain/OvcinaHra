using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
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

        group.MapGet("/{gameId:int}/skills", GetGameSkills);
        group.MapGet("/{gameId:int}/skills/{gameSkillId:int}", GetGameSkillById);
        group.MapPost("/{gameId:int}/skills", CreateGameSkill);
        group.MapPut("/{gameId:int}/skills/{gameSkillId:int}", UpdateGameSkill);
        group.MapDelete("/{gameId:int}/skills/{gameSkillId:int}", DeleteGameSkill);

        return group;
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

    private static async Task<Results<NoContent, NotFound>> LinkToRegistrace(int id, LinkGameDto dto, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        game.ExternalGameId = dto.ExternalGameId;
        await db.SaveChangesAsync();
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
