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

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

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
