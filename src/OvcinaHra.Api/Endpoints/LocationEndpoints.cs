using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class LocationEndpoints
{
    public static RouteGroupBuilder MapLocationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/locations").WithTags("Locations");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // GameLocation assignment
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/by-game", AssignToGame);
        group.MapDelete("/by-game/{gameId:int}/{locationId:int}", RemoveFromGame);

        return group;
    }

    private static async Task<Ok<List<LocationListDto>>> GetAll(WorldDbContext db)
    {
        var locations = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new LocationListDto(
                l.Id, l.Name, l.LocationKind,
                l.Coordinates.Latitude, l.Coordinates.Longitude))
            .ToListAsync();

        return TypedResults.Ok(locations);
    }

    private static async Task<Results<Ok<LocationDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new LocationDetailDto(
            loc.Id, loc.Name, loc.Description, loc.LocationKind,
            loc.Coordinates.Latitude, loc.Coordinates.Longitude,
            loc.ImagePath, loc.PlacementPhotoPath, loc.NpcInfo, loc.SetupNotes));
    }

    private static async Task<Created<LocationDetailDto>> Create(CreateLocationDto dto, WorldDbContext db)
    {
        var loc = new Location
        {
            Name = dto.Name,
            LocationKind = dto.LocationKind,
            Coordinates = new GpsCoordinates(dto.Latitude, dto.Longitude),
            Description = dto.Description,
            NpcInfo = dto.NpcInfo,
            SetupNotes = dto.SetupNotes
        };

        db.Locations.Add(loc);
        await db.SaveChangesAsync();

        var result = new LocationDetailDto(
            loc.Id, loc.Name, loc.Description, loc.LocationKind,
            loc.Coordinates.Latitude, loc.Coordinates.Longitude,
            loc.ImagePath, loc.PlacementPhotoPath, loc.NpcInfo, loc.SetupNotes);

        return TypedResults.Created($"/api/locations/{loc.Id}", result);
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateLocationDto dto, WorldDbContext db)
    {
        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
            return TypedResults.NotFound();

        loc.Name = dto.Name;
        loc.LocationKind = dto.LocationKind;
        loc.Coordinates = new GpsCoordinates(dto.Latitude, dto.Longitude);
        loc.Description = dto.Description;
        loc.NpcInfo = dto.NpcInfo;
        loc.SetupNotes = dto.SetupNotes;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
            return TypedResults.NotFound();

        db.Locations.Remove(loc);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<LocationListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var locations = await db.Locations
            .Where(l => l.GameLocations.Any(gl => gl.GameId == gameId))
            .OrderBy(l => l.Name)
            .Select(l => new LocationListDto(
                l.Id, l.Name, l.LocationKind,
                l.Coordinates.Latitude, l.Coordinates.Longitude))
            .ToListAsync();

        return TypedResults.Ok(locations);
    }

    private static async Task<Results<Created, Conflict>> AssignToGame(GameLocationDto dto, WorldDbContext db)
    {
        var exists = await db.GameLocations
            .AnyAsync(gl => gl.GameId == dto.GameId && gl.LocationId == dto.LocationId);
        if (exists)
            return TypedResults.Conflict();

        db.GameLocations.Add(new GameLocation { GameId = dto.GameId, LocationId = dto.LocationId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/locations/by-game/{dto.GameId}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveFromGame(int gameId, int locationId, WorldDbContext db)
    {
        var gl = await db.GameLocations.FindAsync(gameId, locationId);
        if (gl is null)
            return TypedResults.NotFound();

        db.GameLocations.Remove(gl);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
