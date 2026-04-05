using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class SecretStashEndpoints
{
    public static RouteGroupBuilder MapSecretStashEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/secret-stashes").WithTags("SecretStashes");

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<Ok<List<SecretStashListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var stashes = await db.SecretStashes
            .Where(s => s.GameId == gameId)
            .Include(s => s.Location)
            .OrderBy(s => s.Name)
            .Select(s => new SecretStashListDto(s.Id, s.Name, s.LocationId, s.Location.Name, s.GameId))
            .ToListAsync();
        return TypedResults.Ok(stashes);
    }

    private static async Task<Results<Ok<SecretStashDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var s = await db.SecretStashes.FindAsync(id);
        if (s is null) return TypedResults.NotFound();
        return TypedResults.Ok(new SecretStashDetailDto(s.Id, s.Name, s.Description, s.ImagePath, s.LocationId, s.GameId));
    }

    private static async Task<Results<Created<SecretStashDetailDto>, ValidationProblem>> Create(CreateSecretStashDto dto, WorldDbContext db)
    {
        // Max 3 per location per game
        var count = await db.SecretStashes.CountAsync(s => s.GameId == dto.GameId && s.LocationId == dto.LocationId);
        if (count >= 3)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["LocationId"] = ["Maximálně 3 tajné skrýše na lokaci za hru."]
            });
        }

        var stash = new SecretStash
        {
            Name = dto.Name,
            LocationId = dto.LocationId,
            GameId = dto.GameId,
            Description = dto.Description
        };
        db.SecretStashes.Add(stash);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/secret-stashes/{stash.Id}",
            new SecretStashDetailDto(stash.Id, stash.Name, stash.Description, stash.ImagePath, stash.LocationId, stash.GameId));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateSecretStashDto dto, WorldDbContext db)
    {
        var s = await db.SecretStashes.FindAsync(id);
        if (s is null) return TypedResults.NotFound();

        s.Name = dto.Name;
        s.Description = dto.Description;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var s = await db.SecretStashes.FindAsync(id);
        if (s is null) return TypedResults.NotFound();
        db.SecretStashes.Remove(s);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
