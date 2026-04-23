using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class SecretStashEndpoints
{
    public static RouteGroupBuilder MapSecretStashEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/secret-stashes").WithTags("SecretStashes");

        // Catalog CRUD
        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Per-game assignment
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/game-stash", CreateGameStash);
        group.MapPut("/game-stash/{gameId:int}/{stashId:int}", UpdateGameStash);
        group.MapDelete("/game-stash/{gameId:int}/{stashId:int}", DeleteGameStash);

        return group;
    }

    // --- Catalog ---

    private static async Task<Ok<List<SecretStashListDto>>> GetAll(WorldDbContext db, HttpContext http)
    {
        var rows = await db.SecretStashes
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Description,
                TreasureCount = s.TreasureQuests.Count,
                GameCount = s.GameSecretStashes.Count,
                s.ImagePath
            })
            .ToListAsync();
        var stashes = rows.Select(r => new SecretStashListDto(
            r.Id, r.Name, r.Description,
            r.TreasureCount, r.GameCount,
            ImagePath: r.ImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "secretstashes", r.Id, "medium"))).ToList();
        return TypedResults.Ok(stashes);
    }

    private static async Task<Results<Ok<SecretStashDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var s = await db.SecretStashes.FindAsync(id);
        if (s is null) return TypedResults.NotFound();
        return TypedResults.Ok(new SecretStashDetailDto(s.Id, s.Name, s.Description, s.ImagePath));
    }

    private static async Task<Created<SecretStashDetailDto>> Create(CreateSecretStashDto dto, WorldDbContext db)
    {
        var stash = new SecretStash { Name = dto.Name, Description = dto.Description };
        db.SecretStashes.Add(stash);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/secret-stashes/{stash.Id}",
            new SecretStashDetailDto(stash.Id, stash.Name, stash.Description, stash.ImagePath));
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

    // --- Per-game assignment ---

    private static async Task<Ok<List<GameSecretStashDto>>> GetByGame(int gameId, WorldDbContext db, HttpContext http)
    {
        var rows = await db.GameSecretStashes
            .Where(gs => gs.GameId == gameId)
            .OrderBy(gs => gs.SecretStash.Name)
            .Select(gs => new
            {
                gs.GameId,
                gs.SecretStashId,
                StashName = gs.SecretStash.Name,
                gs.LocationId,
                LocationName = gs.Location.Name,
                TreasureCount = gs.SecretStash.TreasureQuests.Count(tq => tq.GameId == gameId),
                StashImagePath = gs.SecretStash.ImagePath
            })
            .ToListAsync();
        var stashes = rows.Select(r => new GameSecretStashDto(
            r.GameId, r.SecretStashId, r.StashName,
            r.LocationId, r.LocationName,
            r.TreasureCount,
            ImagePath: r.StashImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.StashImagePath) ? null : ImageEndpoints.ThumbUrl(http, "secretstashes", r.SecretStashId, "medium"))).ToList();
        return TypedResults.Ok(stashes);
    }

    private static async Task<Results<Created<GameSecretStashDto>, Conflict, ValidationProblem>> CreateGameStash(
        CreateGameSecretStashDto dto, WorldDbContext db, HttpContext http)
    {
        if (await db.GameSecretStashes.AnyAsync(gs => gs.GameId == dto.GameId && gs.SecretStashId == dto.SecretStashId))
            return TypedResults.Conflict();

        // Max 3 per location per game
        var count = await db.GameSecretStashes.CountAsync(gs => gs.GameId == dto.GameId && gs.LocationId == dto.LocationId);
        if (count >= 3)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["LocationId"] = ["Maximálně 3 tajné skrýše na lokaci za hru."]
            });
        }

        db.GameSecretStashes.Add(new GameSecretStash
        {
            GameId = dto.GameId,
            SecretStashId = dto.SecretStashId,
            LocationId = dto.LocationId
        });
        await db.SaveChangesAsync();

        var stash = await db.SecretStashes.FindAsync(dto.SecretStashId);
        var stashName = stash?.Name ?? "";
        var stashImagePath = stash?.ImagePath;
        var locName = (await db.Locations.FindAsync(dto.LocationId))?.Name ?? "";
        return TypedResults.Created($"/api/secret-stashes/game-stash/{dto.GameId}/{dto.SecretStashId}",
            new GameSecretStashDto(
                dto.GameId, dto.SecretStashId, stashName,
                dto.LocationId, locName,
                TreasureCount: 0,
                ImagePath: stashImagePath,
                ImageUrl: string.IsNullOrWhiteSpace(stashImagePath) ? null : ImageEndpoints.ThumbUrl(http, "secretstashes", dto.SecretStashId, "medium")));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateGameStash(
        int gameId, int stashId, UpdateGameSecretStashDto dto, WorldDbContext db)
    {
        var gs = await db.GameSecretStashes.FindAsync(gameId, stashId);
        if (gs is null) return TypedResults.NotFound();
        gs.LocationId = dto.LocationId;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGameStash(int gameId, int stashId, WorldDbContext db)
    {
        var gs = await db.GameSecretStashes.FindAsync(gameId, stashId);
        if (gs is null) return TypedResults.NotFound();
        db.GameSecretStashes.Remove(gs);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
