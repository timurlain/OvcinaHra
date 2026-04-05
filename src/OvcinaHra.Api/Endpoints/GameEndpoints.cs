using Microsoft.AspNetCore.Http.HttpResults;
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
        group.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<Ok<List<GameListDto>>> GetAll(WorldDbContext db)
    {
        var games = await db.Games
            .OrderByDescending(g => g.StartDate)
            .Select(g => new GameListDto(g.Id, g.Name, g.Edition, g.StartDate, g.EndDate, g.Status))
            .ToListAsync();

        return TypedResults.Ok(games);
    }

    private static async Task<Results<Ok<GameDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new GameDetailDto(
            game.Id, game.Name, game.Edition, game.StartDate, game.EndDate, game.Status, game.ImagePath));
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
            game.Id, game.Name, game.Edition, game.StartDate, game.EndDate, game.Status, game.ImagePath);

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

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var game = await db.Games.FindAsync(id);
        if (game is null)
            return TypedResults.NotFound();

        db.Games.Remove(game);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
