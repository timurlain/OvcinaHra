using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class CharacterEndpoints
{
    public static RouteGroupBuilder MapCharacterEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/characters").WithTags("Characters");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);
        group.MapGet("/{id:int}/assignments", GetAssignments);
        group.MapPost("/{id:int}/assignments", CreateAssignment);

        return group;
    }

    private static async Task<Ok<List<CharacterListDto>>> GetAll(WorldDbContext db, string? search = null)
    {
        var query = db.Characters.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search));

        var characters = await query
            .OrderBy(c => c.Name)
            .Select(c => new CharacterListDto(
                c.Id, c.Name, c.Race, c.Class,
                c.Kingdom, c.IsPlayedCharacter, c.ExternalPersonId))
            .ToListAsync();

        return TypedResults.Ok(characters);
    }

    private static async Task<Results<Ok<CharacterDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var c = await db.Characters
            .Include(c => c.ParentCharacter)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (c is null) return TypedResults.NotFound();

        return TypedResults.Ok(new CharacterDetailDto(
            c.Id, c.Name, c.Race, c.Class,
            c.Kingdom, c.BirthYear, c.Notes,
            c.IsPlayedCharacter, c.ExternalPersonId,
            c.ParentCharacterId, c.ParentCharacter?.Name,
            null, c.CreatedAtUtc, c.UpdatedAtUtc));
    }

    private static async Task<Created<CharacterDetailDto>> Create(CreateCharacterDto dto, WorldDbContext db)
    {
        var now = DateTime.UtcNow;
        var c = new Character
        {
            Name = dto.Name,
            Race = dto.Race,
            Class = dto.Class,
            Kingdom = dto.Kingdom,
            BirthYear = dto.BirthYear,
            Notes = dto.Notes,
            IsPlayedCharacter = dto.IsPlayedCharacter,
            ExternalPersonId = dto.ExternalPersonId,
            ParentCharacterId = dto.ParentCharacterId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Characters.Add(c);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/characters/{c.Id}",
            new CharacterDetailDto(
                c.Id, c.Name, c.Race, c.Class,
                c.Kingdom, c.BirthYear, c.Notes,
                c.IsPlayedCharacter, c.ExternalPersonId,
                c.ParentCharacterId, null,
                null, c.CreatedAtUtc, c.UpdatedAtUtc));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateCharacterDto dto, WorldDbContext db)
    {
        var c = await db.Characters.FindAsync(id);
        if (c is null) return TypedResults.NotFound();

        c.Name = dto.Name;
        c.Race = dto.Race;
        c.Class = dto.Class;
        c.Kingdom = dto.Kingdom;
        c.BirthYear = dto.BirthYear;
        c.Notes = dto.Notes;
        c.IsPlayedCharacter = dto.IsPlayedCharacter;
        c.ExternalPersonId = dto.ExternalPersonId;
        c.ParentCharacterId = dto.ParentCharacterId;
        c.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var c = await db.Characters.FindAsync(id);
        if (c is null) return TypedResults.NotFound();

        c.IsDeleted = true;
        c.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<CharacterAssignmentDto>, NotFound>> CreateAssignment(
        int id, CreateCharacterAssignmentDto dto, WorldDbContext db)
    {
        var character = await db.Characters.FindAsync(id);
        if (character is null) return TypedResults.NotFound();

        var assignment = new CharacterAssignment
        {
            CharacterId = id,
            GameId = dto.GameId,
            ExternalPersonId = dto.ExternalPersonId,
            IsActive = true,
            StartedAtUtc = DateTime.UtcNow
        };
        db.CharacterAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/characters/{id}/assignments",
            new CharacterAssignmentDto(
                assignment.Id, assignment.CharacterId, character.Name,
                assignment.GameId, assignment.ExternalPersonId,
                assignment.IsActive, assignment.StartedAtUtc, assignment.EndedAtUtc));
    }

    private static async Task<Ok<List<CharacterAssignmentDto>>> GetAssignments(int id, WorldDbContext db)
    {
        var assignments = await db.CharacterAssignments
            .Where(a => a.CharacterId == id)
            .Include(a => a.Character)
            .OrderByDescending(a => a.StartedAtUtc)
            .Select(a => new CharacterAssignmentDto(
                a.Id, a.CharacterId, a.Character.Name,
                a.GameId, a.ExternalPersonId,
                a.IsActive, a.StartedAtUtc, a.EndedAtUtc))
            .ToListAsync();

        return TypedResults.Ok(assignments);
    }
}
