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
        group.MapPut("/{gameId:int}/skills/{skillId:int}", UpsertGameSkill);
        group.MapDelete("/{gameId:int}/skills/{skillId:int}", DeleteGameSkill);

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
            game.Id, game.Name, game.Edition, game.StartDate, game.EndDate, game.Status, game.ImagePath, game.ExternalGameId));
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
            game.Id, game.Name, game.Edition, game.StartDate, game.EndDate, game.Status, game.ImagePath, game.ExternalGameId);

        return TypedResults.Created($"/api/games/{game.Id}", result);
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateGameDto dto, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        game.Name = dto.Name;
        game.Edition = dto.Edition;
        game.StartDate = dto.StartDate;
        game.EndDate = dto.EndDate;
        game.Status = dto.Status;

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

        var dtos = await db.GameSkills
            .Where(gs => gs.GameId == gameId)
            .OrderBy(gs => gs.Skill.Name)
            .Select(gs => new GameSkillDto(
                gs.GameId,
                gs.SkillId,
                gs.Skill.Name,
                gs.Skill.ClassRestriction,
                gs.XpCost,
                gs.LevelRequirement))
            .ToListAsync();

        return TypedResults.Ok((IReadOnlyList<GameSkillDto>)dtos);
    }

    private static async Task<Results<Created<GameSkillDto>, NoContent, NotFound, BadRequest<ProblemDetails>>> UpsertGameSkill(
        int gameId, int skillId, UpsertGameSkillRequest dto, WorldDbContext db)
    {
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

        var skill = await db.Skills.SingleOrDefaultAsync(s => s.Id == skillId);
        if (skill is null) return TypedResults.NotFound();

        var existing = await db.GameSkills
            .SingleOrDefaultAsync(gs => gs.GameId == gameId && gs.SkillId == skillId);

        if (existing is not null)
        {
            existing.XpCost = dto.XpCost;
            existing.LevelRequirement = dto.LevelRequirement;
            await db.SaveChangesAsync();
            return TypedResults.NoContent();
        }

        var gameSkill = new GameSkill
        {
            GameId = gameId,
            SkillId = skillId,
            XpCost = dto.XpCost,
            LevelRequirement = dto.LevelRequirement
        };
        db.GameSkills.Add(gameSkill);
        await db.SaveChangesAsync();

        var result = new GameSkillDto(
            gameId,
            skillId,
            skill.Name,
            skill.ClassRestriction,
            gameSkill.XpCost,
            gameSkill.LevelRequirement);

        return TypedResults.Created($"/api/games/{gameId}/skills/{skillId}", result);
    }

    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>>> DeleteGameSkill(
        int gameId, int skillId, WorldDbContext db)
    {
        var gameSkill = await db.GameSkills
            .SingleOrDefaultAsync(gs => gs.GameId == gameId && gs.SkillId == skillId);
        if (gameSkill is null) return TypedResults.NotFound();

        var usedInRecipe = await db.CraftingSkillRequirements
            .AnyAsync(csr => csr.SkillId == skillId && csr.CraftingRecipe.GameId == gameId);
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
}
