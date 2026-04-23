using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class KingdomEndpoints
{
    public static RouteGroupBuilder MapKingdomEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/kingdoms").WithTags("Kingdoms");

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<Ok<List<KingdomDto>>> GetAll(WorldDbContext db, IBlobStorageService blob)
    {
        var rows = await db.Kingdoms
            .OrderBy(k => k.SortOrder)
            .ThenBy(k => k.Name)
            .Select(k => new
            {
                k.Id,
                k.Name,
                k.HexColor,
                k.BadgeImageUrl,
                k.Description,
                k.SortOrder,
                AssignmentCount = k.Assignments.Count
            })
            .ToListAsync();

        var kingdoms = rows.Select(r => new KingdomDto(
            r.Id, r.Name, r.HexColor, r.BadgeImageUrl, r.Description, r.SortOrder,
            r.AssignmentCount,
            ImageUrl: string.IsNullOrWhiteSpace(r.BadgeImageUrl) ? null : blob.GetSasUrl(r.BadgeImageUrl))).ToList();

        return TypedResults.Ok(kingdoms);
    }

    private static async Task<Results<Created<KingdomDto>, Conflict<string>, BadRequest<string>>> Create(
        CreateKingdomDto dto, WorldDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return TypedResults.BadRequest("Název království je povinný.");

        var normalizedName = dto.Name.Trim();

        var exists = await db.Kingdoms.AnyAsync(k => k.Name == normalizedName);
        if (exists)
            return TypedResults.Conflict($"Království s názvem '{normalizedName}' už existuje.");

        var k = new Kingdom
        {
            Name = normalizedName,
            HexColor = dto.HexColor,
            BadgeImageUrl = dto.BadgeImageUrl,
            Description = dto.Description,
            SortOrder = dto.SortOrder
        };
        db.Kingdoms.Add(k);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/kingdoms/{k.Id}",
            new KingdomDto(k.Id, k.Name, k.HexColor, k.BadgeImageUrl, k.Description, k.SortOrder, 0));
    }

    // Name gating: if any CharacterAssignment references this kingdom, the Name
    // cannot be changed (rename = canon change, must be done via code/migration).
    // Other metadata (hex, badge, description, sort order) is freely editable.
    private static async Task<Results<NoContent, NotFound, Conflict<string>, BadRequest<string>>> Update(
        int id, UpdateKingdomDto dto, WorldDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return TypedResults.BadRequest("Název království je povinný.");

        var normalizedName = dto.Name.Trim();

        var k = await db.Kingdoms.FindAsync(id);
        if (k is null) return TypedResults.NotFound();

        if (!string.Equals(k.Name, normalizedName, StringComparison.Ordinal))
        {
            var inUse = await db.CharacterAssignments.AnyAsync(a => a.KingdomId == id);
            if (inUse)
                return TypedResults.Conflict(
                    "Toto království je přiřazeno postavám — název nelze přejmenovat. Ostatní údaje můžete upravit.");

            var nameClash = await db.Kingdoms.AnyAsync(x => x.Id != id && x.Name == normalizedName);
            if (nameClash)
                return TypedResults.Conflict($"Království s názvem '{normalizedName}' už existuje.");

            k.Name = normalizedName;
        }

        k.HexColor = dto.HexColor;
        k.BadgeImageUrl = dto.BadgeImageUrl;
        k.Description = dto.Description;
        k.SortOrder = dto.SortOrder;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Blocked when any CharacterAssignment references the kingdom. Returning
    // 409 keeps historic assignments intact.
    private static async Task<Results<NoContent, NotFound, Conflict<string>>> Delete(
        int id, WorldDbContext db)
    {
        var k = await db.Kingdoms.FindAsync(id);
        if (k is null) return TypedResults.NotFound();

        var inUse = await db.CharacterAssignments.AnyAsync(a => a.KingdomId == id);
        if (inUse)
            return TypedResults.Conflict(
                "Toto království je přiřazeno postavám — nejde smazat. Nejprve odstraňte přiřazení.");

        db.Kingdoms.Remove(k);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
