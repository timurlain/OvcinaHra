using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class BuildingEndpoints
{
    public static RouteGroupBuilder MapBuildingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/buildings").WithTags("Buildings");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // GameBuilding assignment
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/by-game", AssignToGame);
        group.MapDelete("/by-game/{gameId:int}/{buildingId:int}", RemoveFromGame);

        return group;
    }

    private static async Task<Ok<List<BuildingListDto>>> GetAll(WorldDbContext db)
    {
        var rows = await db.Buildings
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                b.Id,
                b.Name,
                b.Description,
                b.Notes,
                b.LocationId,
                LocationName = b.Location != null ? b.Location.Name : null,
                b.IsPrebuilt,
                b.ImagePath
            })
            .ToListAsync();
        var buildings = rows.Select(r => new BuildingListDto(
            r.Id, r.Name, r.Description, r.Notes,
            r.LocationId, r.LocationName, r.IsPrebuilt,
            ImagePath: r.ImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : $"/api/images/buildings/{r.Id}/thumb?size=small")).ToList();
        return TypedResults.Ok(buildings);
    }

    private static async Task<Ok<List<BuildingListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var rows = await db.GameBuildings
            .Where(gb => gb.GameId == gameId)
            .OrderBy(gb => gb.Building.Name)
            .Select(gb => new
            {
                gb.Building.Id,
                gb.Building.Name,
                gb.Building.Description,
                gb.Building.Notes,
                gb.Building.LocationId,
                LocationName = gb.Building.Location != null ? gb.Building.Location.Name : null,
                gb.Building.IsPrebuilt,
                gb.Building.ImagePath
            })
            .ToListAsync();
        var buildings = rows.Select(r => new BuildingListDto(
            r.Id, r.Name, r.Description, r.Notes,
            r.LocationId, r.LocationName, r.IsPrebuilt,
            ImagePath: r.ImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : $"/api/images/buildings/{r.Id}/thumb?size=small")).ToList();
        return TypedResults.Ok(buildings);
    }

    private static async Task<Results<Ok<BuildingDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var b = await db.Buildings.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        return TypedResults.Ok(new BuildingDetailDto(b.Id, b.Name, b.Description, b.Notes, b.ImagePath, b.LocationId, b.IsPrebuilt));
    }

    private static async Task<Created<BuildingDetailDto>> Create(CreateBuildingDto dto, HttpContext http, WorldDbContext db)
    {
        var b = new Building
        {
            Name = dto.Name,
            Description = dto.Description,
            Notes = dto.Notes,
            LocationId = dto.LocationId,
            IsPrebuilt = dto.IsPrebuilt
        };
        db.Buildings.Add(b);
        await db.SaveChangesAsync();

        // Optionally auto-assign to a game via ?gameId=N query param
        if (http.Request.Query.TryGetValue("gameId", out var gameIdStr) &&
            int.TryParse(gameIdStr, out var gameId))
        {
            db.GameBuildings.Add(new GameBuilding { GameId = gameId, BuildingId = b.Id });
            await db.SaveChangesAsync();
        }

        return TypedResults.Created($"/api/buildings/{b.Id}",
            new BuildingDetailDto(b.Id, b.Name, b.Description, b.Notes, b.ImagePath, b.LocationId, b.IsPrebuilt));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateBuildingDto dto, WorldDbContext db)
    {
        var b = await db.Buildings.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        b.Name = dto.Name; b.Description = dto.Description; b.Notes = dto.Notes; b.LocationId = dto.LocationId; b.IsPrebuilt = dto.IsPrebuilt;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var b = await db.Buildings.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        db.Buildings.Remove(b);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, Conflict>> AssignToGame(GameBuildingDto dto, WorldDbContext db)
    {
        var exists = await db.GameBuildings
            .AnyAsync(gb => gb.GameId == dto.GameId && gb.BuildingId == dto.BuildingId);
        if (exists)
            return TypedResults.Conflict();

        db.GameBuildings.Add(new GameBuilding { GameId = dto.GameId, BuildingId = dto.BuildingId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/buildings/by-game/{dto.GameId}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveFromGame(int gameId, int buildingId, WorldDbContext db)
    {
        var gb = await db.GameBuildings.FindAsync(gameId, buildingId);
        if (gb is null) return TypedResults.NotFound();

        db.GameBuildings.Remove(gb);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
