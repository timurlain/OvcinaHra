using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/search").WithTags("Search");
        group.MapGet("/", Search);
        return group;
    }

    private static async Task<Ok<SearchResponseDto>> Search(string q, WorldDbContext db, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return TypedResults.Ok(new SearchResponseDto(q ?? "", 0, []));

        // Sanitize: split into words, join with & for tsquery AND matching
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tsQuery = string.Join(" & ", terms.Select(t => t.Replace("'", "''") + ":*"));

        var results = new List<SearchResultDto>();

        // Search Locations
        var locations = await db.Locations
            .FromSqlRaw(
                """
                SELECT * FROM "Locations"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                LIMIT {1}
                """, tsQuery, limit)
            .Select(l => new SearchResultDto("Location", l.Id, l.Name, l.Description))
            .ToListAsync();
        results.AddRange(locations);

        // Search Items
        var items = await db.Items
            .FromSqlRaw(
                """
                SELECT * FROM "Items"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                LIMIT {1}
                """, tsQuery, limit)
            .Select(i => new SearchResultDto("Item", i.Id, i.Name, i.Effect))
            .ToListAsync();
        results.AddRange(items);

        // Search Monsters
        var monsters = await db.Monsters
            .FromSqlRaw(
                """
                SELECT * FROM "Monsters"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                LIMIT {1}
                """, tsQuery, limit)
            .Select(m => new SearchResultDto("Monster", m.Id, m.Name, m.Abilities))
            .ToListAsync();
        results.AddRange(monsters);

        // Search Quests
        var quests = await db.Quests
            .FromSqlRaw(
                """
                SELECT * FROM "Quests"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                LIMIT {1}
                """, tsQuery, limit)
            .Select(q => new SearchResultDto("Quest", q.Id, q.Name, q.Description))
            .ToListAsync();
        results.AddRange(quests);

        return TypedResults.Ok(new SearchResponseDto(q, results.Count, results));
    }
}
