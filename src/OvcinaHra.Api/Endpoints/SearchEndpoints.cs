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

    private static async Task<Ok<SearchResponseDto>> Search(string q, WorldDbContext db, int? gameId = null, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return TypedResults.Ok(new SearchResponseDto(q ?? "", 0, []));

        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tsQuery = string.Join(" & ", terms.Select(t => t.Replace("'", "''") + ":*"));

        var results = new List<SearchResultDto>();

        // Search Locations
        var locQuery = db.Locations
            .FromSqlRaw(
                """
                SELECT * FROM "Locations"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                LIMIT {1}
                """, tsQuery, limit)
            .AsQueryable();
        if (gameId.HasValue)
            locQuery = locQuery.Where(l => l.GameLocations.Any(gl => gl.GameId == gameId.Value));
        results.AddRange(await locQuery
            .Select(l => new SearchResultDto("Location", l.Id, l.Name, l.Description))
            .ToListAsync());

        // Search Items
        var itemQuery = db.Items
            .FromSqlRaw(
                """
                SELECT * FROM "Items"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                LIMIT {1}
                """, tsQuery, limit)
            .AsQueryable();
        if (gameId.HasValue)
            itemQuery = itemQuery.Where(i => i.GameItems.Any(gi => gi.GameId == gameId.Value));
        results.AddRange(await itemQuery
            .Select(i => new SearchResultDto("Item", i.Id, i.Name, i.Effect))
            .ToListAsync());

        // Search Monsters (no game join table — include all)
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

        // Search Quests (no game join table — include all)
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

        // Search Spells — catalog is cross-game. When gameId is provided, filter to
        // spells actually assigned to that game via GameSpell.
        // NOTE: no LIMIT in the raw SQL; Take(limit) runs AFTER the optional game
        // filter so we don't chop off valid matches before they get filtered.
        var spellQuery = db.Spells
            .FromSqlRaw(
                """
                SELECT * FROM "Spells"
                WHERE "SearchVector" @@ to_tsquery('simple', {0})
                """, tsQuery)
            .AsQueryable();
        if (gameId.HasValue)
            spellQuery = spellQuery.Where(s => s.GameSpells.Any(gs => gs.GameId == gameId.Value));
        results.AddRange(await spellQuery
            .Take(limit)
            .Select(s => new SearchResultDto("Spell", s.Id, s.Name, s.Effect))
            .ToListAsync());

        return TypedResults.Ok(new SearchResponseDto(q, results.Count, results));
    }
}
