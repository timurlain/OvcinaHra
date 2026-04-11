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
                l.Region,
                l.Coordinates != null ? l.Coordinates.Latitude : (decimal?)null,
                l.Coordinates != null ? l.Coordinates.Longitude : (decimal?)null,
                l.ParentLocationId))
            .ToListAsync();

        return TypedResults.Ok(locations);
    }

    private static async Task<Results<Ok<LocationDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var loc = await db.Locations
            .Include(l => l.Variants)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (loc is null)
            return TypedResults.NotFound();

        var variants = loc.Variants.Select(v => new LocationVariantDto(v.Id, v.Name, v.LocationKind)).ToList();
        return TypedResults.Ok(new LocationDetailDto(
            loc.Id, loc.Name, loc.Description, loc.Details, loc.GamePotential, loc.Prompt, loc.Region, loc.LocationKind,
            loc.Coordinates?.Latitude, loc.Coordinates?.Longitude,
            loc.ImagePath, loc.PlacementPhotoPath, loc.NpcInfo, loc.SetupNotes,
            loc.ParentLocationId, variants));
    }

    private static async Task<Created<LocationDetailDto>> Create(CreateLocationDto dto, WorldDbContext db)
    {
        var loc = new Location
        {
            Name = dto.Name,
            LocationKind = dto.LocationKind,
            Coordinates = dto.Latitude.HasValue && dto.Longitude.HasValue
                ? new GpsCoordinates(dto.Latitude.Value, dto.Longitude.Value) : null,
            Description = dto.Description,
            Details = dto.Details,
            GamePotential = dto.GamePotential,
            Prompt = dto.Prompt,
            Region = dto.Region,
            NpcInfo = dto.NpcInfo,
            SetupNotes = dto.SetupNotes,
            ParentLocationId = dto.ParentLocationId
        };

        db.Locations.Add(loc);
        await db.SaveChangesAsync();

        var result = new LocationDetailDto(
            loc.Id, loc.Name, loc.Description, loc.Details, loc.GamePotential, loc.Prompt, loc.Region, loc.LocationKind,
            loc.Coordinates?.Latitude, loc.Coordinates?.Longitude,
            loc.ImagePath, loc.PlacementPhotoPath, loc.NpcInfo, loc.SetupNotes,
            loc.ParentLocationId, []);

        return TypedResults.Created($"/api/locations/{loc.Id}", result);
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateLocationDto dto, WorldDbContext db)
    {
        var loc = await db.Locations.FindAsync(id);
        if (loc is null)
            return TypedResults.NotFound();

        loc.Name = dto.Name;
        loc.LocationKind = dto.LocationKind;
        loc.Coordinates = dto.Latitude.HasValue && dto.Longitude.HasValue
            ? new GpsCoordinates(dto.Latitude.Value, dto.Longitude.Value) : null;
        loc.Description = dto.Description;
        loc.Details = dto.Details;
        loc.GamePotential = dto.GamePotential;
        loc.Prompt = dto.Prompt;
        loc.Region = dto.Region;
        loc.NpcInfo = dto.NpcInfo;
        loc.SetupNotes = dto.SetupNotes;
        loc.ParentLocationId = dto.ParentLocationId;

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
                l.Region,
                l.Coordinates != null ? l.Coordinates.Latitude : (decimal?)null,
                l.Coordinates != null ? l.Coordinates.Longitude : (decimal?)null,
                l.ParentLocationId))
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
