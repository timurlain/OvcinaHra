using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
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

        return group;
    }

    private static async Task<Ok<List<QuestCatalogDto>>> GetAll(WorldDbContext db)
    {
        // Project to a lightweight shape in EF so we don't pull full Quest + Item entities.
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
                r.Rewards.Select(x => (x.ItemName, x.Quantity))))).ToList();

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

    private static async Task<Results<Created<QuestCopyResultDto>, NotFound>> CopyToGame(int id, int gameId, WorldDbContext db)
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
            ChainOrder = null, ParentQuestId = null, GameId = gameId
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

        return TypedResults.Created($"/api/quests/{copy.Id}",
            new QuestCopyResultDto(
                new QuestListDto(copy.Id, copy.Name, copy.QuestType, copy.ChainOrder, copy.ParentQuestId, copy.GameId),
                warnings));
    }

    private static async Task<Ok<List<QuestListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var quests = await db.Quests
            .Where(q => q.GameId == gameId)
            .OrderBy(q => q.ParentQuestId).ThenBy(q => q.ChainOrder).ThenBy(q => q.Name)
            .Select(q => new QuestListDto(q.Id, q.Name, q.QuestType, q.ChainOrder, q.ParentQuestId, q.GameId))
            .ToListAsync();
        return TypedResults.Ok(quests);
    }

    private static async Task<Results<Ok<QuestDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var q = await db.Quests
            .Include(q => q.QuestTags).ThenInclude(qt => qt.Tag)
            .Include(q => q.QuestLocations).ThenInclude(ql => ql.Location)
            .Include(q => q.QuestEncounters).ThenInclude(qe => qe.Monster)
            .Include(q => q.QuestRewards).ThenInclude(qr => qr.Item)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (q is null) return TypedResults.NotFound();

        return TypedResults.Ok(new QuestDetailDto(
            q.Id, q.Name, q.QuestType, q.Description, q.FullText, q.TimeSlot,
            q.RewardXp, q.RewardMoney, q.RewardNotes, q.ChainOrder, q.ParentQuestId, q.GameId,
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
            ChainOrder = dto.ChainOrder, ParentQuestId = dto.ParentQuestId
        };
        db.Quests.Add(q);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/quests/{q.Id}",
            new QuestListDto(q.Id, q.Name, q.QuestType, q.ChainOrder, q.ParentQuestId, q.GameId));
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
}
