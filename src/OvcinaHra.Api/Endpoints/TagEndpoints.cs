using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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

    // Issue #234 — name now flows through Trim + non-empty + max-length +
    // dup-check guards that surface Czech ProblemDetails verbatim instead of
    // hiding behind EnsureSuccessStatusCode. The client passes the user's
    // typed name through unchanged (PostAsJsonAsync, JSON body) so any char
    // — `+`, `&`, `:`, `'`, space — round-trips end-to-end.
    private static async Task<Results<Created<TagDto>, ProblemHttpResult>> Create(CreateTagDto dto, WorldDbContext db)
    {
        var name = dto.Name?.Trim() ?? "";
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

        if (!Enum.IsDefined(dto.Kind))
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: "Neplatný druh tagu.",
                statusCode: StatusCodes.Status400BadRequest);

        // EF translates Trim() to Postgres TRIM() so legacy rows that already
        // have whitespace padding still collide on the dup-check.
        var collision = await db.Tags
            .AnyAsync(t => t.Kind == dto.Kind && t.Name.Trim() == name);
        if (collision)
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Tag s názvem „{name}“ už v této kategorii existuje.",
                statusCode: StatusCodes.Status400BadRequest);

        var tag = new Tag { Name = name, Kind = dto.Kind };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/tags/{tag.Id}", new TagDto(tag.Id, tag.Name, tag.Kind));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Update(int id, UpdateTagDto dto, WorldDbContext db)
    {
        // FindAsync first so a 404 on a missing id wins over the 400 input
        // validation below — see _review-instincts §1.
        var tag = await db.Tags.FindAsync(id);
        if (tag is null)
            return TypedResults.NotFound();

        var name = dto.Name?.Trim() ?? "";
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

        var collision = await db.Tags
            .AnyAsync(t => t.Id != id && t.Kind == tag.Kind && t.Name.Trim() == name);
        if (collision)
            return TypedResults.Problem(
                title: "Uložení selhalo",
                detail: $"Tag s názvem „{name}“ už v této kategorii existuje.",
                statusCode: StatusCodes.Status400BadRequest);

        tag.Name = name;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

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
