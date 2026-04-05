using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class TagEndpoints
{
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

    private static async Task<Created<TagDto>> Create(CreateTagDto dto, WorldDbContext db)
    {
        var tag = new Tag { Name = dto.Name, Kind = dto.Kind };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/tags/{tag.Id}", new TagDto(tag.Id, tag.Name, tag.Kind));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateTagDto dto, WorldDbContext db)
    {
        var tag = await db.Tags.FindAsync(id);
        if (tag is null)
            return TypedResults.NotFound();

        tag.Name = dto.Name;
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
