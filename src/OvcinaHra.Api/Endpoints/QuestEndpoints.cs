using System.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class QuestEndpoints
{
    public static RouteGroupBuilder MapQuestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/quests").WithTags("Quests");

        group.MapGet("/all", GetAll);
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapPatch("/{id:int}/state", UpdateState);
        group.MapDelete("/{id:int}", Delete);
        group.MapPost("/{id:int}/copy-to-game/{gameId:int}", CopyToGame);
        group.MapPut("/{id:int}/game", MoveToGame);
        group.MapDelete("/{id:int}/game", UnassignFromGame);

        // Tags
        group.MapPost("/{id:int}/tags/{tagId:int}", AddTag);
        group.MapDelete("/{id:int}/tags/{tagId:int}", RemoveTag);

        // Locations
        group.MapPost("/{id:int}/locations", AddLocation);
        group.MapDelete("/{id:int}/locations/{locationId:int}", RemoveLocation);

        // Encounters
        group.MapPost("/{id:int}/encounters", AddEncounter);
        group.MapDelete("/{id:int}/encounters/{monsterId:int}", RemoveEncounter);

        // Rewards
        group.MapPost("/{id:int}/rewards", AddReward);
        group.MapDelete("/{id:int}/rewards/{itemId:int}", RemoveReward);

        // Issue #214 — Waypoints (ordered location stops powering the
        // Map page's animated quest path).
        group.MapGet("/{id:int}/waypoints", GetWaypoints);
        group.MapPost("/{id:int}/waypoints", AddWaypoint);
        group.MapPut("/{id:int}/waypoints/reorder", ReorderWaypoints);
        group.MapPut("/{id:int}/waypoints/{wpId:int}", UpdateWaypoint);
        group.MapDelete("/{id:int}/waypoints/{wpId:int}", RemoveWaypoint);

        return group;
    }

    private static async Task<Ok<List<QuestCatalogDto>>> GetAll(WorldDbContext db, HttpContext http)
    {
        var rows = await db.Quests
            .AsNoTracking()
            .Where(q => q.GameId == null)
            .OrderBy(q => q.Name)
            .Select(q => new
            {
                q.Id,
                q.Name,
                q.QuestType,
                q.Description,
                q.FullText,
                q.RewardXp,
                q.RewardMoney,
                q.RewardNotes,
                q.ImagePath,
                EncountersCount = q.QuestEncounters.Count,
                RewardsCount = q.QuestRewards.Count,
                Rewards = q.QuestRewards
                    .OrderBy(r => r.Item.Name)
                    .Select(r => new { ItemName = r.Item.Name, r.Quantity })
                    .ToList()
            })
            .ToListAsync();

        var dtos = rows.Select(r => new QuestCatalogDto(
            r.Id, r.Name, r.QuestType,
            null, null, null,
            r.Description, r.FullText,
            BuildRewardSummary(r.RewardXp, r.RewardMoney, r.RewardNotes,
                r.Rewards.Select(x => (x.ItemName, x.Quantity))),
            r.ImagePath,
            string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "quests", r.Id, "small"),
            r.EncountersCount, r.RewardsCount)).ToList();

        return TypedResults.Ok(dtos);
    }

    internal static string? BuildRewardSummary(
        int? rewardXp, int? rewardMoney, string? rewardNotes,
        IEnumerable<(string ItemName, int Quantity)> items)
    {
        var parts = new List<string>();

        if (rewardXp is int xp && xp > 0) parts.Add($"{xp} XP");
        if (rewardMoney is int money && money > 0) parts.Add($"{money} gr");

        foreach (var (itemName, quantity) in items)
            parts.Add(quantity > 1 ? $"{itemName} × {quantity}" : itemName);

        if (!string.IsNullOrWhiteSpace(rewardNotes))
            parts.Add($"pozn.: {rewardNotes}");

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static async Task<Results<Created<QuestCopyResultDto>, NotFound>> CopyToGame(int id, int gameId, WorldDbContext db, HttpContext http)
    {
        var source = await db.Quests
            .Include(q => q.QuestTags).ThenInclude(qt => qt.Tag)
            .Include(q => q.QuestLocations)
            .Include(q => q.QuestEncounters)
            .Include(q => q.QuestRewards).ThenInclude(qr => qr.Item)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (source is null) return TypedResults.NotFound();

        var warnings = new List<string>();

        var copy = new Quest
        {
            Name = source.Name, QuestType = source.QuestType, Description = source.Description,
            FullText = source.FullText, TimeSlot = source.TimeSlot,
            RewardXp = source.RewardXp, RewardMoney = source.RewardMoney, RewardNotes = source.RewardNotes,
            ChainOrder = null, ParentQuestId = null, GameId = gameId,
            ImagePath = source.ImagePath, State = QuestState.Inactive
        };
        db.Quests.Add(copy);
        await db.SaveChangesAsync();

        foreach (var tag in source.QuestTags)
            db.QuestTagLinks.Add(new QuestTagLink { QuestId = copy.Id, TagId = tag.TagId });

        foreach (var enc in source.QuestEncounters)
            db.QuestEncounters.Add(new QuestEncounter { QuestId = copy.Id, MonsterId = enc.MonsterId, Quantity = enc.Quantity });

        var targetLocationIds = (await db.GameLocations.Where(gl => gl.GameId == gameId).Select(gl => gl.LocationId).ToListAsync()).ToHashSet();
        foreach (var loc in source.QuestLocations)
        {
            if (targetLocationIds.Contains(loc.LocationId))
                db.QuestLocationLinks.Add(new QuestLocationLink { QuestId = copy.Id, LocationId = loc.LocationId });
            else
                warnings.Add($"Lokace #{loc.LocationId} není v cílové hře — přeskočena.");
        }

        var targetItemIds = (await db.GameItems.Where(gi => gi.GameId == gameId).Select(gi => gi.ItemId).ToListAsync()).ToHashSet();
        foreach (var reward in source.QuestRewards)
        {
            if (targetItemIds.Contains(reward.ItemId))
                db.QuestRewards.Add(new QuestReward { QuestId = copy.Id, ItemId = reward.ItemId, Quantity = reward.Quantity });
            else
                warnings.Add($"Předmět '{reward.Item.Name}' není v cílové hře — odměna přeskočena.");
        }

        await db.SaveChangesAsync();

        var thumb = string.IsNullOrWhiteSpace(copy.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "quests", copy.Id, "small");
        return TypedResults.Created($"/api/quests/{copy.Id}",
            new QuestCopyResultDto(
                new QuestListDto(copy.Id, copy.Name, copy.QuestType, copy.ChainOrder, copy.ParentQuestId, copy.GameId,
                    copy.State, copy.ImagePath, thumb,
                    EncountersCount: source.QuestEncounters.Count,
                    RewardsCount: source.QuestRewards.Count(r => targetItemIds.Contains(r.ItemId)),
                    LocationsCount: source.QuestLocations.Count(l => targetLocationIds.Contains(l.LocationId)),
                    TagsCount: source.QuestTags.Count),
                warnings));
    }

    private static async Task<Ok<List<QuestListDto>>> GetByGame(int gameId, WorldDbContext db, HttpContext http)
    {
        var rows = await db.Quests
            .AsNoTracking()
            .Where(q => q.GameId == gameId)
            .OrderBy(q => q.ParentQuestId).ThenBy(q => q.ChainOrder).ThenBy(q => q.Name)
            .Select(q => new
            {
                q.Id, q.Name, q.QuestType, q.ChainOrder, q.ParentQuestId, q.GameId,
                q.State, q.ImagePath,
                EncountersCount = q.QuestEncounters.Count,
                RewardsCount = q.QuestRewards.Count,
                LocationsCount = q.QuestLocations.Count,
                TagsCount = q.QuestTags.Count
            })
            .ToListAsync();

        var quests = rows.Select(r => new QuestListDto(
            r.Id, r.Name, r.QuestType, r.ChainOrder, r.ParentQuestId, r.GameId,
            r.State, r.ImagePath,
            string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "quests", r.Id, "small"),
            r.EncountersCount, r.RewardsCount, r.LocationsCount, r.TagsCount)).ToList();

        return TypedResults.Ok(quests);
    }

    private static async Task<Results<Ok<QuestDetailDto>, NotFound>> GetById(int id, WorldDbContext db, HttpContext http)
    {
        var q = await db.Quests
            .Include(q => q.QuestTags).ThenInclude(qt => qt.Tag)
            .Include(q => q.QuestLocations).ThenInclude(ql => ql.Location)
            .Include(q => q.QuestEncounters).ThenInclude(qe => qe.Monster)
            .Include(q => q.QuestRewards).ThenInclude(qr => qr.Item)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (q is null) return TypedResults.NotFound();

        var imageUrl = string.IsNullOrWhiteSpace(q.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "quests", q.Id, "small");

        return TypedResults.Ok(new QuestDetailDto(
            q.Id, q.Name, q.QuestType, q.Description, q.FullText, q.TimeSlot,
            q.RewardXp, q.RewardMoney, q.RewardNotes, q.ChainOrder, q.ParentQuestId, q.GameId,
            q.State, q.ImagePath, imageUrl,
            q.QuestTags.Select(qt => new TagDto(qt.Tag.Id, qt.Tag.Name, qt.Tag.Kind)).ToList(),
            q.QuestLocations.Select(ql => new QuestLocationDto(ql.QuestId, ql.LocationId, ql.Location.Name)).ToList(),
            q.QuestEncounters.Select(qe => new QuestEncounterDto(qe.QuestId, qe.MonsterId, qe.Monster.Name, qe.Quantity)).ToList(),
            q.QuestRewards.Select(qr => new QuestRewardDto(qr.QuestId, qr.ItemId, qr.Item.Name, qr.Quantity)).ToList()));
    }

    private static async Task<Created<QuestListDto>> Create(CreateQuestDto dto, WorldDbContext db)
    {
        var q = new Quest
        {
            Name = dto.Name, QuestType = dto.QuestType, GameId = dto.GameId,
            Description = dto.Description, FullText = dto.FullText, TimeSlot = dto.TimeSlot,
            RewardXp = dto.RewardXp, RewardMoney = dto.RewardMoney, RewardNotes = dto.RewardNotes,
            ChainOrder = dto.ChainOrder, ParentQuestId = dto.ParentQuestId,
            State = QuestState.Inactive
        };
        db.Quests.Add(q);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/quests/{q.Id}",
            new QuestListDto(q.Id, q.Name, q.QuestType, q.ChainOrder, q.ParentQuestId, q.GameId,
                q.State, q.ImagePath, ImageUrl: null,
                EncountersCount: 0, RewardsCount: 0, LocationsCount: 0, TagsCount: 0));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateQuestDto dto, WorldDbContext db)
    {
        var q = await db.Quests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();

        q.Name = dto.Name; q.QuestType = dto.QuestType;
        q.Description = dto.Description; q.FullText = dto.FullText; q.TimeSlot = dto.TimeSlot;
        q.RewardXp = dto.RewardXp; q.RewardMoney = dto.RewardMoney; q.RewardNotes = dto.RewardNotes;
        q.ChainOrder = dto.ChainOrder; q.ParentQuestId = dto.ParentQuestId;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> UpdateState(int id, UpdateQuestStateDto dto, WorldDbContext db)
    {
        var q = await db.Quests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();

        if (!Enum.IsDefined(dto.State))
            return TypedResults.BadRequest($"Unknown state '{dto.State}'.");

        q.State = dto.State;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var q = await db.Quests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();
        db.Quests.Remove(q);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Move a quest to a specific game (assigns or re-assigns)
    private static async Task<Results<NoContent, NotFound>> MoveToGame(int id, MoveQuestToGameDto dto, WorldDbContext db)
    {
        var q = await db.Quests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();
        q.GameId = dto.GameId;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Unassign a quest from its game (moves to catalog; GameId = null)
    private static async Task<Results<NoContent, NotFound>> UnassignFromGame(int id, WorldDbContext db)
    {
        var q = await db.Quests.FindAsync(id);
        if (q is null) return TypedResults.NotFound();
        q.GameId = null;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Tags
    private static async Task<Results<Created, Conflict>> AddTag(int id, int tagId, WorldDbContext db)
    {
        if (await db.QuestTagLinks.AnyAsync(qt => qt.QuestId == id && qt.TagId == tagId))
            return TypedResults.Conflict();
        db.QuestTagLinks.Add(new QuestTagLink { QuestId = id, TagId = tagId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/quests/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveTag(int id, int tagId, WorldDbContext db)
    {
        var link = await db.QuestTagLinks.FindAsync(id, tagId);
        if (link is null) return TypedResults.NotFound();
        db.QuestTagLinks.Remove(link);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Locations
    private static async Task<Results<Created, Conflict>> AddLocation(int id, AddQuestLocationDto dto, WorldDbContext db)
    {
        if (await db.QuestLocationLinks.AnyAsync(ql => ql.QuestId == id && ql.LocationId == dto.LocationId))
            return TypedResults.Conflict();
        db.QuestLocationLinks.Add(new QuestLocationLink { QuestId = id, LocationId = dto.LocationId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/quests/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveLocation(int id, int locationId, WorldDbContext db)
    {
        var link = await db.QuestLocationLinks.FindAsync(id, locationId);
        if (link is null) return TypedResults.NotFound();
        db.QuestLocationLinks.Remove(link);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Encounters
    private static async Task<Results<Created, Conflict>> AddEncounter(int id, AddQuestEncounterDto dto, WorldDbContext db)
    {
        if (await db.QuestEncounters.AnyAsync(qe => qe.QuestId == id && qe.MonsterId == dto.MonsterId))
            return TypedResults.Conflict();
        db.QuestEncounters.Add(new QuestEncounter { QuestId = id, MonsterId = dto.MonsterId, Quantity = dto.Quantity });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/quests/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveEncounter(int id, int monsterId, WorldDbContext db)
    {
        var link = await db.QuestEncounters.FindAsync(id, monsterId);
        if (link is null) return TypedResults.NotFound();
        db.QuestEncounters.Remove(link);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Rewards
    private static async Task<Results<Created, Conflict>> AddReward(int id, AddQuestRewardDto dto, WorldDbContext db)
    {
        if (await db.QuestRewards.AnyAsync(qr => qr.QuestId == id && qr.ItemId == dto.ItemId))
            return TypedResults.Conflict();
        db.QuestRewards.Add(new QuestReward { QuestId = id, ItemId = dto.ItemId, Quantity = dto.Quantity });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/quests/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveReward(int id, int itemId, WorldDbContext db)
    {
        var link = await db.QuestRewards.FindAsync(id, itemId);
        if (link is null) return TypedResults.NotFound();
        db.QuestRewards.Remove(link);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // ===== Issue #214 — Waypoint handlers =====

    private static async Task<Results<Ok<List<QuestWaypointDto>>, NotFound>> GetWaypoints(
        int id, WorldDbContext db)
    {
        if (!await db.Quests.AnyAsync(q => q.Id == id)) return TypedResults.NotFound();
        var rows = await db.QuestWaypoints
            .Where(w => w.QuestId == id)
            .OrderBy(w => w.Order)
            .Select(w => new QuestWaypointDto(
                w.Id, w.QuestId, w.LocationId, w.Location.Name, w.Order, w.Label))
            .ToListAsync();
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<QuestWaypointDto>, NotFound, ProblemHttpResult>> AddWaypoint(
        int id, AddQuestWaypointDto dto, WorldDbContext db)
    {
        if (!await db.Quests.AnyAsync(q => q.Id == id)) return TypedResults.NotFound();
        if (!await db.Locations.AnyAsync(l => l.Id == dto.LocationId))
            return TypedResults.Problem(
                title: "Lokace nenalezena.",
                detail: $"Lokace s ID {dto.LocationId} neexistuje.",
                statusCode: StatusCodes.Status400BadRequest);

        // Issue #224 — wrap MAX(Order)+1 read-then-write in a SERIALIZABLE
        // transaction so two concurrent POSTs can't pick the same next
        // Order and trip the (QuestId, Order) unique-index violation.
        // Postgres will retry-fail one of the txns; the editor surfaces
        // the failure as a banner and the user clicks Add again.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var nextOrder = (await db.QuestWaypoints
            .Where(w => w.QuestId == id)
            .Select(w => (int?)w.Order)
            .MaxAsync()) ?? 0;

        var wp = new QuestWaypoint
        {
            QuestId = id,
            LocationId = dto.LocationId,
            Order = nextOrder + 1,
            Label = string.IsNullOrWhiteSpace(dto.Label) ? null : dto.Label.Trim(),
        };
        db.QuestWaypoints.Add(wp);
        await db.SaveChangesAsync();

        var locationName = await db.Locations
            .Where(l => l.Id == dto.LocationId)
            .Select(l => l.Name)
            .FirstAsync();

        await tx.CommitAsync();

        // Issue #224 — return 200 OK with the DTO instead of 201 Created.
        // The 201 contract previously emitted a Location header pointing
        // at /api/quests/{id}/waypoints/{wpId} but no GET-by-id route
        // existed there. The editor reloads the whole list after add, so
        // GET-by-id was never load-bearing; dropping the header avoids
        // shipping a stale link.
        return TypedResults.Ok(
            new QuestWaypointDto(wp.Id, wp.QuestId, wp.LocationId, locationName, wp.Order, wp.Label));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UpdateWaypoint(
        int id, int wpId, UpdateQuestWaypointDto dto, WorldDbContext db)
    {
        var wp = await db.QuestWaypoints.FirstOrDefaultAsync(w => w.Id == wpId && w.QuestId == id);
        if (wp is null) return TypedResults.NotFound();
        if (!await db.Locations.AnyAsync(l => l.Id == dto.LocationId))
            return TypedResults.Problem(
                title: "Lokace nenalezena.",
                detail: $"Lokace s ID {dto.LocationId} neexistuje.",
                statusCode: StatusCodes.Status400BadRequest);
        wp.LocationId = dto.LocationId;
        wp.Label = string.IsNullOrWhiteSpace(dto.Label) ? null : dto.Label.Trim();
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveWaypoint(
        int id, int wpId, WorldDbContext db)
    {
        var wp = await db.QuestWaypoints.FirstOrDefaultAsync(w => w.Id == wpId && w.QuestId == id);
        if (wp is null) return TypedResults.NotFound();

        // Compact remaining waypoints so Order stays gap-free. Drop the
        // target first, then bump every higher Order down by one inside a
        // single transaction so the (QuestId, Order) unique index never
        // sees a duplicate.
        await using var tx = await db.Database.BeginTransactionAsync();
        var deletedOrder = wp.Order;
        db.QuestWaypoints.Remove(wp);
        await db.SaveChangesAsync();

        await db.QuestWaypoints
            .Where(w => w.QuestId == id && w.Order > deletedOrder)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Order, w => w.Order - 1));

        await tx.CommitAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ReorderWaypoints(
        int id, ReorderQuestWaypointsDto dto, WorldDbContext db)
    {
        var existing = await db.QuestWaypoints
            .Where(w => w.QuestId == id)
            .ToListAsync();
        if (existing.Count == 0) return TypedResults.NotFound();

        var existingIds = existing.Select(w => w.Id).ToHashSet();
        var requested = dto.WaypointIdsInOrder ?? [];
        if (requested.Count != existing.Count
            || !requested.All(existingIds.Contains)
            || requested.Distinct().Count() != requested.Count)
        {
            return TypedResults.Problem(
                title: "Neplatné pořadí.",
                detail: "Seznam ID musí přesně odpovídat existujícím waypointům tohoto úkolu.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Two-pass update so the (QuestId, Order) unique index isn't
        // tripped while the new order propagates: stage 1 flips every row
        // to a negative Order (Order = -OldOrder); stage 2 writes the
        // final 1-based positive Order from the requested sequence.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.QuestWaypoints
            .Where(w => w.QuestId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Order, w => -w.Order));

        var byId = existing.ToDictionary(w => w.Id);
        for (var i = 0; i < requested.Count; i++)
        {
            var target = byId[requested[i]];
            // ExecuteUpdate on a single row by Id keeps each step
            // independent of the change tracker — no entity is reloaded
            // here, the SQL writes directly. Stage 1 already flipped
            // Order to negatives; this stage writes the final positive.
            await db.QuestWaypoints
                .Where(w => w.Id == target.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Order, _ => i + 1));
        }

        await tx.CommitAsync();
        return TypedResults.NoContent();
    }
}
