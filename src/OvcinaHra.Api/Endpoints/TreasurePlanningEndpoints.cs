using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class TreasurePlanningEndpoints
{
    public static RouteGroupBuilder MapTreasurePlanningEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/treasure-planning").WithTags("TreasurePlanning");

        // Pool management
        group.MapGet("/pool/{gameId:int}", GetPool);
        group.MapPost("/pool", AddToPool);
        group.MapDelete("/pool/{id:int}", RemoveFromPool);

        // Unlimited items (always available, derived from GameItem)
        group.MapGet("/unlimited/{gameId:int}", GetUnlimitedItems);

        // Location cards with treasure counts
        group.MapGet("/locations/{gameId:int}", GetLocationCards);

        // Summary
        group.MapGet("/summary/{gameId:int}", GetSummary);

        // Available items for pool (with remaining stock)
        group.MapGet("/available-items/{gameId:int}", GetAvailableItems);

        // Assignment
        group.MapPost("/assign", AssignTreasure);

        return group;
    }

    private static async Task<Ok<List<TreasurePoolItemDto>>> GetPool(int gameId, WorldDbContext db)
    {
        var items = await db.TreasureItems
            .Where(ti => ti.GameId == gameId && ti.TreasureQuestId == null)
            .Include(ti => ti.Item)
            .OrderBy(ti => ti.Item.ItemType).ThenBy(ti => ti.Item.Name)
            .Select(ti => new TreasurePoolItemDto(ti.Id, ti.ItemId, ti.Item.Name, ti.Item.ItemType, ti.Count, ti.GameId))
            .ToListAsync();
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Created<TreasurePoolItemDto>, ValidationProblem>> AddToPool(CreateTreasurePoolItemDto dto, WorldDbContext db)
    {
        var gameItem = await db.GameItems
            .Include(gi => gi.Item)
            .FirstOrDefaultAsync(gi => gi.GameId == dto.GameId && gi.ItemId == dto.ItemId);
        if (gameItem is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ItemId"] = ["Předmět není přiřazen k této hře."]
            });
        }

        // Check stock limit
        if (gameItem.StockCount.HasValue)
        {
            var alreadyInPool = await db.TreasureItems
                .Where(ti => ti.GameId == dto.GameId && ti.ItemId == dto.ItemId)
                .SumAsync(ti => ti.Count);
            if (alreadyInPool + dto.Count > gameItem.StockCount.Value)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Count"] = [$"Překročen limit ({gameItem.StockCount.Value}). Již v pokladech: {alreadyInPool}."]
                });
            }
        }

        var ti = new TreasureItem
        {
            ItemId = dto.ItemId, GameId = dto.GameId, Count = dto.Count, TreasureQuestId = null
        };
        db.TreasureItems.Add(ti);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/treasure-planning/pool/{dto.GameId}",
            new TreasurePoolItemDto(ti.Id, ti.ItemId, gameItem.Item.Name, gameItem.Item.ItemType, ti.Count, ti.GameId));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> RemoveFromPool(int id, WorldDbContext db)
    {
        var ti = await db.TreasureItems.FindAsync(id);
        if (ti is null) return TypedResults.NotFound();
        if (ti.TreasureQuestId is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Id"] = ["Předmět je přiřazen k pokladu. Nejdříve ho odeberte z pokladu."]
            });
        }
        db.TreasureItems.Remove(ti);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<UnlimitedItemDto>>> GetUnlimitedItems(int gameId, WorldDbContext db)
    {
        var items = await db.GameItems
            .Where(gi => gi.GameId == gameId && gi.IsFindable && gi.StockCount == null)
            .Include(gi => gi.Item)
            .OrderBy(gi => gi.Item.ItemType).ThenBy(gi => gi.Item.Name)
            .Select(gi => new UnlimitedItemDto(gi.ItemId, gi.Item.Name, gi.Item.ItemType))
            .ToListAsync();
        return TypedResults.Ok(items);
    }

    private static async Task<Ok<List<AvailablePoolItemDto>>> GetAvailableItems(int gameId, WorldDbContext db)
    {
        // Get limited findable items for this game
        var gameItems = await db.GameItems
            .Where(gi => gi.GameId == gameId && gi.IsFindable && gi.StockCount != null)
            .Include(gi => gi.Item)
            .ToListAsync();

        // Count how many of each item are already in TreasureItems (pool + placed)
        var usedCounts = await db.TreasureItems
            .Where(ti => ti.GameId == gameId)
            .GroupBy(ti => ti.ItemId)
            .Select(g => new { ItemId = g.Key, Used = g.Sum(ti => ti.Count) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Used);

        var result = gameItems
            .Select(gi =>
            {
                usedCounts.TryGetValue(gi.ItemId, out var used);
                var remaining = gi.StockCount!.Value - used;
                return new AvailablePoolItemDto(gi.ItemId, $"{gi.Item.Name} (zbývá: {remaining}/{gi.StockCount})", remaining, gi.StockCount.Value);
            })
            .Where(x => x.Remaining > 0)
            .OrderBy(x => x.DisplayName)
            .ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<List<TreasurePlanningLocationDto>>> GetLocationCards(int gameId, WorldDbContext db)
    {
        var gameLocationIds = await db.GameLocations
            .Where(gl => gl.GameId == gameId)
            .Select(gl => gl.LocationId)
            .ToListAsync();

        var locations = await db.Locations
            .Where(l => gameLocationIds.Contains(l.Id))
            .Include(l => l.GameSecretStashes.Where(gs => gs.GameId == gameId))
            .ThenInclude(gs => gs.SecretStash)
            .OrderBy(l => l.Name)
            .ToListAsync();

        // Get all treasure quests for this game with their items
        var quests = await db.TreasureQuests
            .Where(tq => tq.GameId == gameId)
            .Include(tq => tq.TreasureItems)
            .ToListAsync();

        var result = locations.Select(loc =>
        {
            var locationQuests = quests.Where(q => q.LocationId == loc.Id).ToList();
            var stashIds = loc.GameSecretStashes.Select(gs => gs.SecretStashId).ToHashSet();
            var stashQuests = quests.Where(q => q.SecretStashId.HasValue && stashIds.Contains(q.SecretStashId.Value)).ToList();
            var allQuests = locationQuests.Concat(stashQuests).ToList();

            int CountByDifficulty(TreasureQuestDifficulty d) =>
                allQuests.Where(q => q.Difficulty == d).SelectMany(q => q.TreasureItems).Sum(ti => ti.Count);

            var stashSummaries = loc.GameSecretStashes.Select(gs =>
            {
                var sQuests = quests.Where(q => q.SecretStashId == gs.SecretStashId).ToList();
                return new StashSummaryDto(gs.SecretStashId, gs.SecretStash.Name, sQuests.SelectMany(q => q.TreasureItems).Sum(ti => ti.Count));
            }).ToList();

            return new TreasurePlanningLocationDto(
                loc.Id, loc.Name, loc.LocationKind,
                CountByDifficulty(TreasureQuestDifficulty.Start),
                CountByDifficulty(TreasureQuestDifficulty.Early),
                CountByDifficulty(TreasureQuestDifficulty.Midgame),
                CountByDifficulty(TreasureQuestDifficulty.Lategame),
                allQuests.SelectMany(q => q.TreasureItems).Sum(ti => ti.Count),
                loc.GameSecretStashes.Count, 3,
                stashSummaries);
        }).ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<TreasureSummaryDto>> GetSummary(int gameId, WorldDbContext db)
    {
        var poolRemaining = await db.TreasureItems
            .Where(ti => ti.GameId == gameId && ti.TreasureQuestId == null)
            .SumAsync(ti => ti.Count);

        var quests = await db.TreasureQuests
            .Where(tq => tq.GameId == gameId)
            .Include(tq => tq.TreasureItems)
            .ToListAsync();

        int CountByDifficulty(TreasureQuestDifficulty d) =>
            quests.Where(q => q.Difficulty == d).SelectMany(q => q.TreasureItems).Sum(ti => ti.Count);

        var placed = quests.SelectMany(q => q.TreasureItems).Sum(ti => ti.Count);

        return TypedResults.Ok(new TreasureSummaryDto(
            poolRemaining, placed,
            CountByDifficulty(TreasureQuestDifficulty.Start),
            CountByDifficulty(TreasureQuestDifficulty.Early),
            CountByDifficulty(TreasureQuestDifficulty.Midgame),
            CountByDifficulty(TreasureQuestDifficulty.Lategame)));
    }

    private static async Task<Results<Created<TreasureQuestDetailDto>, ValidationProblem>> AssignTreasure(AssignTreasureDto dto, WorldDbContext db)
    {
        // Validate XOR
        if ((dto.LocationId is null) == (dto.SecretStashId is null))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["LocationId"] = ["Musí být vyplněna buď lokace, nebo tajná skrýš (ne obojí)."]
            });
        }

        // Create the quest
        var quest = new TreasureQuest
        {
            Title = dto.Title, Clue = dto.Clue, Difficulty = dto.Difficulty,
            LocationId = dto.LocationId, SecretStashId = dto.SecretStashId, GameId = dto.GameId
        };
        db.TreasureQuests.Add(quest);
        await db.SaveChangesAsync(); // get the ID

        // Assign pool items
        if (dto.TreasureItemIds is { Count: > 0 })
        {
            var poolItems = await db.TreasureItems
                .Where(ti => dto.TreasureItemIds.Contains(ti.Id) && ti.TreasureQuestId == null && ti.GameId == dto.GameId)
                .ToListAsync();
            foreach (var item in poolItems)
                item.TreasureQuestId = quest.Id;
        }

        // Add unlimited items directly
        if (dto.UnlimitedItems is { Count: > 0 })
        {
            foreach (var ui in dto.UnlimitedItems)
            {
                db.TreasureItems.Add(new TreasureItem
                {
                    ItemId = ui.ItemId, GameId = dto.GameId, Count = ui.Count, TreasureQuestId = quest.Id
                });
            }
        }

        await db.SaveChangesAsync();

        // Reload with includes for response
        var loaded = await db.TreasureQuests
            .Include(t => t.Location)
            .Include(t => t.SecretStash)
            .Include(t => t.TreasureItems).ThenInclude(ti => ti.Item)
            .FirstAsync(t => t.Id == quest.Id);

        return TypedResults.Created($"/api/treasure-quests/{quest.Id}",
            new TreasureQuestDetailDto(
                loaded.Id, loaded.Title, loaded.Clue, loaded.Difficulty,
                loaded.LocationId, loaded.Location?.Name, loaded.SecretStashId, loaded.SecretStash?.Name, loaded.GameId,
                loaded.TreasureItems.Select(ti => new TreasureItemDto(ti.Id, ti.ItemId, ti.Item.Name, ti.Count, ti.TreasureQuestId)).ToList()));
    }
}
