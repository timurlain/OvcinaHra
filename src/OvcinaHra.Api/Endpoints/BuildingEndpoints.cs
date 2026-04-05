using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class BuildingEndpoints
{
    public static RouteGroupBuilder MapBuildingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/buildings").WithTags("Buildings");

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<Ok<List<BuildingListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var buildings = await db.Buildings
            .Where(b => b.GameId == gameId)
            .Include(b => b.Location)
            .OrderBy(b => b.Name)
            .Select(b => new BuildingListDto(b.Id, b.Name, b.LocationId, b.Location != null ? b.Location.Name : null, b.GameId, b.IsPrebuilt))
            .ToListAsync();
        return TypedResults.Ok(buildings);
    }

    private static async Task<Results<Ok<BuildingDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var b = await db.Buildings.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        return TypedResults.Ok(new BuildingDetailDto(b.Id, b.Name, b.Description, b.ImagePath, b.LocationId, b.GameId, b.IsPrebuilt));
    }

    private static async Task<Created<BuildingDetailDto>> Create(CreateBuildingDto dto, WorldDbContext db)
    {
        var b = new Building { Name = dto.Name, GameId = dto.GameId, Description = dto.Description, LocationId = dto.LocationId, IsPrebuilt = dto.IsPrebuilt };
        db.Buildings.Add(b);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/buildings/{b.Id}",
            new BuildingDetailDto(b.Id, b.Name, b.Description, b.ImagePath, b.LocationId, b.GameId, b.IsPrebuilt));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateBuildingDto dto, WorldDbContext db)
    {
        var b = await db.Buildings.FindAsync(id);
        if (b is null) return TypedResults.NotFound();
        b.Name = dto.Name; b.Description = dto.Description; b.LocationId = dto.LocationId; b.IsPrebuilt = dto.IsPrebuilt;
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
}
