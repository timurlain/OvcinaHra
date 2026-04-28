using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class TagEndpoints
{
    // Matches Tag.Name HasMaxLength(100) in TagConfiguration. Centralised here
    // so Create + Update validate the same ceiling and the Czech error message
    // can quote the limit verbatim.
    private const int NameMaxLength = 100;

    public static RouteGroupBuilder MapTagEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tags").WithTags("Tags");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<Ok<List<TagDto>>> GetAll(WorldDbContext db, TagKind? kind = null)
    {
        var query = db.Tags.AsQueryable();
        if (kind.HasValue)
            query = query.Where(t => t.Kind == kind.Value);

        var tags = await query
            .OrderBy(t => t.Kind).ThenBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, t.Kind))
            .ToListAsync();

        return TypedResults.Ok(tags);
    }

    private static async Task<Results<Ok<TagDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var tag = await db.Tags.FindAsync(id);
        if (tag is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new TagDto(tag.Id, tag.Name, tag.Kind));
    }

    // Issue #234/#331 — name flows through Trim + validation that surfaces
    // Czech ProblemDetails verbatim. Create is idempotent by case-insensitive
    // (Kind, Name): existing tag wins and keeps its canonical display casing.
    private static async Task<Results<Created<TagDto>, Ok<TagDto>, ProblemHttpResult>> Create(CreateTagDto dto, WorldDbContext db)
    {
        if (ValidateName(dto.Name, out var name) is { } problem)
            return problem;

        if (!Enum.IsDefined(dto.Kind))
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Neplatný druh tagu.",
                statusCode: StatusCodes.Status400BadRequest);

        var existing = await FindByNameIgnoringCaseAsync(db, dto.Kind, name);
        if (existing is not null)
            return TypedResults.Ok(ToDto(existing));

        var tag = new Tag { Name = name, Kind = dto.Kind };
        db.Tags.Add(tag);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var existingAfterRace = await FindByNameIgnoringCaseAsync(db, dto.Kind, name);
            if (existingAfterRace is not null)
                return TypedResults.Ok(ToDto(existingAfterRace));

            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Tag s názvem „{name}“ už v této kategorii existuje.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Created($"/api/tags/{tag.Id}", new TagDto(tag.Id, tag.Name, tag.Kind));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Update(int id, UpdateTagDto dto, WorldDbContext db)
    {
        // FindAsync first so a 404 on a missing id wins over the 400 input
        // validation below — see _review-instincts §1.
        var tag = await db.Tags.FindAsync(id);
        if (tag is null)
            return TypedResults.NotFound();

        if (ValidateName(dto.Name, out var name) is { } problem)
            return problem;

        var collision = await FindByNameIgnoringCaseAsync(db, tag.Kind, name, exceptId: id);
        if (collision is not null)
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Tag s názvem „{name}“ už v této kategorii existuje.",
                statusCode: StatusCodes.Status400BadRequest);

        tag.Name = name;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Same race as Create — a concurrent rename can still violate the
            // (Kind, Name) UNIQUE index between AnyAsync and SaveChangesAsync.
            // Per Copilot review on PR #282.
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Tag s názvem „{name}“ už v této kategorii existuje.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        return TypedResults.NoContent();
    }

    // Npgsql surfaces unique-index violations as PostgresException with
    // SqlState 23505. Mirrors the helper in GameEndpoints (PR #282 / Copilot).
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static ProblemHttpResult? ValidateName(string? rawName, out string name)
    {
        name = rawName?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Název tagu nesmí být prázdný.",
                statusCode: StatusCodes.Status400BadRequest);

        if (name.Length > NameMaxLength)
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Název tagu nesmí přesáhnout {NameMaxLength} znaků.",
                statusCode: StatusCodes.Status400BadRequest);

        if (name.Any(char.IsControl))
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Název tagu nesmí obsahovat řídicí znaky.",
                statusCode: StatusCodes.Status400BadRequest);

        return null;
    }

    private static async Task<Tag?> FindByNameIgnoringCaseAsync(
        WorldDbContext db,
        TagKind kind,
        string name,
        int? exceptId = null)
    {
        var normalized = name.ToLower();
        return await db.Tags.FirstOrDefaultAsync(t =>
            t.Kind == kind
            && (!exceptId.HasValue || t.Id != exceptId.Value)
            && t.Name.Trim().ToLower() == normalized);
    }

    private static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.Kind);

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var tag = await db.Tags.FindAsync(id);
        if (tag is null)
            return TypedResults.NotFound();

        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
