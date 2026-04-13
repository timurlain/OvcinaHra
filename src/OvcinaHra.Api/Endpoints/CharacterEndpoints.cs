using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
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
        group.MapPut("/assignments/{id:int}", UpdateAssignment);
        group.MapPost("/import/{gameId:int}", ImportFromRegistrace);

        return group;
    }

    private static string? FullName(Character c) =>
        string.IsNullOrWhiteSpace(c.PlayerFirstName) && string.IsNullOrWhiteSpace(c.PlayerLastName)
            ? null
            : $"{c.PlayerFirstName} {c.PlayerLastName}".Trim();

    private static async Task<Ok<List<CharacterListDto>>> GetAll(WorldDbContext db, string? search = null)
    {
        var query = db.Characters.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search));

        var characters = await query
            .OrderBy(c => c.Name)
            .ToListAsync();

        return TypedResults.Ok(characters
            .Select(c => new CharacterListDto(
                c.Id, c.Name, FullName(c), c.Race, c.IsPlayedCharacter, c.ExternalPersonId))
            .ToList());
    }

    private static async Task<Results<Ok<CharacterDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var c = await db.Characters
            .Include(c => c.ParentCharacter)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (c is null) return TypedResults.NotFound();

        return TypedResults.Ok(new CharacterDetailDto(
            c.Id, c.Name, c.PlayerFirstName, c.PlayerLastName,
            c.Race, c.BirthYear, c.Notes,
            c.IsPlayedCharacter, c.ExternalPersonId,
            c.ParentCharacterId, c.ParentCharacter?.Name,
            c.CreatedAtUtc, c.UpdatedAtUtc));
    }

    private static async Task<Created<CharacterDetailDto>> Create(CreateCharacterDto dto, WorldDbContext db)
    {
        var now = DateTime.UtcNow;
        var c = new Character
        {
            Name = dto.Name,
            PlayerFirstName = dto.PlayerFirstName,
            PlayerLastName = dto.PlayerLastName,
            Race = dto.Race,
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
                c.Id, c.Name, c.PlayerFirstName, c.PlayerLastName,
                c.Race, c.BirthYear, c.Notes,
                c.IsPlayedCharacter, c.ExternalPersonId,
                c.ParentCharacterId, null,
                c.CreatedAtUtc, c.UpdatedAtUtc));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateCharacterDto dto, WorldDbContext db)
    {
        var c = await db.Characters.FindAsync(id);
        if (c is null) return TypedResults.NotFound();

        c.Name = dto.Name;
        c.PlayerFirstName = dto.PlayerFirstName;
        c.PlayerLastName = dto.PlayerLastName;
        c.Race = dto.Race;
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
            RegistraceCharacterId = dto.RegistraceCharacterId,
            Class = dto.Class,
            Kingdom = dto.Kingdom,
            IsActive = true,
            StartedAtUtc = DateTime.UtcNow
        };
        db.CharacterAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/characters/{id}/assignments",
            new CharacterAssignmentDto(
                assignment.Id, assignment.CharacterId, character.Name,
                assignment.GameId, assignment.ExternalPersonId, assignment.RegistraceCharacterId,
                assignment.Class, assignment.Kingdom,
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
                a.GameId, a.ExternalPersonId, a.RegistraceCharacterId,
                a.Class, a.Kingdom,
                a.IsActive, a.StartedAtUtc, a.EndedAtUtc))
            .ToListAsync();

        return TypedResults.Ok(assignments);
    }

    private static async Task<Results<NoContent, NotFound>> UpdateAssignment(
        int id, UpdateCharacterAssignmentDto dto, WorldDbContext db)
    {
        var assignment = await db.CharacterAssignments.FindAsync(id);
        if (assignment is null) return TypedResults.NotFound();

        assignment.Class = dto.Class;
        assignment.Kingdom = dto.Kingdom;
        assignment.IsActive = dto.IsActive;
        if (!dto.IsActive && assignment.EndedAtUtc is null)
            assignment.EndedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ImportResultDto>> ImportFromRegistrace(
        int gameId, RegistraceImportService importService)
    {
        var result = await importService.ImportAsync(gameId);
        return TypedResults.Ok(result);
    }
}
