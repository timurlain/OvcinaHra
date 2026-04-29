using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class TreasurePlanningEndpoints
{
    private static readonly JsonSerializerOptions TreasureAllocLogJsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapTreasurePlanningEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/treasure-planning").WithTags("TreasurePlanning");

        // Pool management
        group.MapGet("/pool/{gameId:int}", GetPool);
        group.MapPost("/pool", AddToPool);
        group.MapDelete("/pool/{id:int}", RemoveFromPool);

        // Issue #160: bulk refill — append unallocated stock to the pool.
        group.MapPost("/refill-pool/{gameId:int}", RefillPool);

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
        group.MapPost("/treasure-items/{id:int}/adjust-count", AdjustTreasureItemCount);

        return group;
    }

    private static async Task<Ok<List<TreasurePoolItemDto>>> GetPool(int gameId, WorldDbContext db)
    {
        var items = await db.TreasureItems
            .Where(ti => ti.GameId == gameId
                && ti.TreasureQuestId == null
                && ti.Item.ItemType != ItemType.Money)
            .Include(ti => ti.Item)
            .OrderBy(ti => ti.Item.ItemType).ThenBy(ti => ti.Item.Name)
            .Select(ti => new TreasurePoolItemDto(ti.Id, ti.ItemId, ti.Item.Name, ti.Item.ItemType, ti.Count, ti.GameId, ti.Item.IsUnique))
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
        if (gameItem.Item.ItemType == ItemType.Money)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ItemId"] = ["Peníze se do zásobníku nepřidávají. Použijte vytvoření pokladu z grošů na lokaci."]
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
            new TreasurePoolItemDto(ti.Id, ti.ItemId, gameItem.Item.Name, gameItem.Item.ItemType, ti.Count, ti.GameId, gameItem.Item.IsUnique));
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
            .Where(gi => gi.GameId == gameId
                && gi.IsFindable
                && gi.StockCount != null
                && gi.Item.ItemType != ItemType.Money)
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

    private static async Task<Ok<List<TreasurePlanningLocationDto>>> GetLocationCards(
        int gameId,
        WorldDbContext db,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.TreasurePlanningEndpoints");
        var assignedLocationIds = await db.GameLocations
            .Where(gl => gl.GameId == gameId)
            .Select(gl => gl.LocationId)
            .ToListAsync();

        var parentIdsForAssignedChildren = await db.Locations
            .Where(l => assignedLocationIds.Contains(l.Id) && l.ParentLocationId.HasValue)
            .Select(l => l.ParentLocationId!.Value)
            .ToListAsync();
        var gameLocationIds = assignedLocationIds
            .Concat(parentIdsForAssignedChildren)
            .Distinct()
            .ToList();

        var locations = await db.Locations
            .Where(l => gameLocationIds.Contains(l.Id))
            .Include(l => l.GameSecretStashes.Where(gs => gs.GameId == gameId))
            .ThenInclude(gs => gs.SecretStash)
            .OrderBy(l => l.Name)
            .ToListAsync();

        var childLocations = locations.Where(l => l.ParentLocationId.HasValue).ToList();
        foreach (var child in childLocations)
        {
            LogTreasureAlloc(logger, LogLevel.Debug, "child location filtered", new
            {
                locationId = child.Id,
                parentId = child.ParentLocationId
            });
        }

        var childIdsByParent = childLocations
            .GroupBy(l => l.ParentLocationId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Id).ToList());
        var parentLocations = locations
            .Where(l => !l.ParentLocationId.HasValue)
            .ToList();

        // Get all treasure quests for this game with their items
        var quests = await db.TreasureQuests
            .Where(tq => tq.GameId == gameId)
            .Include(tq => tq.TreasureItems)
            .ToListAsync();

        var result = parentLocations.Select(loc =>
        {
            childIdsByParent.TryGetValue(loc.Id, out var childIds);
            var visibleLocationIds = childIds is null
                ? new HashSet<int> { loc.Id }
                : childIds.Append(loc.Id).ToHashSet();
            var locationQuests = quests
                .Where(q => q.LocationId.HasValue && visibleLocationIds.Contains(q.LocationId.Value))
                .ToList();
            var stashIds = loc.GameSecretStashes.Select(gs => gs.SecretStashId).ToHashSet();
            var stashQuests = quests.Where(q => q.SecretStashId.HasValue && stashIds.Contains(q.SecretStashId.Value)).ToList();
            var allQuests = locationQuests.Concat(stashQuests).ToList();

            int CountByDifficulty(GameTimePhase d) =>
                allQuests.Where(q => q.Difficulty == d).SelectMany(q => q.TreasureItems).Sum(ti => ti.Count);

            var stashSummaries = loc.GameSecretStashes.Select(gs =>
            {
                var sQuests = quests.Where(q => q.SecretStashId == gs.SecretStashId).ToList();
                return new StashSummaryDto(gs.SecretStashId, gs.SecretStash.Name, sQuests.SelectMany(q => q.TreasureItems).Sum(ti => ti.Count));
            }).ToList();

            return new TreasurePlanningLocationDto(
                loc.Id, loc.Name, loc.LocationKind,
                CountByDifficulty(GameTimePhase.Start),
                CountByDifficulty(GameTimePhase.Early),
                CountByDifficulty(GameTimePhase.Midgame),
                CountByDifficulty(GameTimePhase.Lategame),
                allQuests.SelectMany(q => q.TreasureItems).Sum(ti => ti.Count),
                loc.GameSecretStashes.Count, 3,
                stashSummaries,
                Region: loc.Region,
                Latitude: loc.Coordinates?.Latitude,
                Longitude: loc.Coordinates?.Longitude,
                EffectiveLatitude: loc.Coordinates?.Latitude,
                EffectiveLongitude: loc.Coordinates?.Longitude);
        }).ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<TreasureSummaryDto>> GetSummary(int gameId, WorldDbContext db)
    {
        var poolRemaining = await db.TreasureItems
            .Where(ti => ti.GameId == gameId
                && ti.TreasureQuestId == null
                && ti.Item.ItemType != ItemType.Money)
            .SumAsync(ti => ti.Count);

        var quests = await db.TreasureQuests
            .Where(tq => tq.GameId == gameId)
            .Include(tq => tq.TreasureItems)
            .ToListAsync();

        int CountByDifficulty(GameTimePhase d) =>
            quests.Where(q => q.Difficulty == d).SelectMany(q => q.TreasureItems).Sum(ti => ti.Count);

        var placed = quests.SelectMany(q => q.TreasureItems).Sum(ti => ti.Count);

        return TypedResults.Ok(new TreasureSummaryDto(
            poolRemaining, placed,
            CountByDifficulty(GameTimePhase.Start),
            CountByDifficulty(GameTimePhase.Early),
            CountByDifficulty(GameTimePhase.Midgame),
            CountByDifficulty(GameTimePhase.Lategame)));
    }

    private static async Task<Results<Created<TreasureQuestDetailDto>, ValidationProblem>> AssignTreasure(
        AssignTreasureDto dto,
        WorldDbContext db,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.TreasurePlanningEndpoints");
        var poolAssignments = dto.PoolItems?
            .GroupBy(p => p.TreasureItemId)
            .Select(g => new PoolItemAssignDto(g.Key, g.Sum(p => p.Count)))
            .ToList() ?? [];
        var legacyPoolItemIds = dto.TreasureItemIds ?? [];
        var poolCount = poolAssignments.Count + legacyPoolItemIds.Count;
        var unlimitedCount = dto.UnlimitedItems?.Count ?? 0;
        var isComposite = poolAssignments.Count > 1 || poolAssignments.Sum(p => p.Count) > 1;
        var targetLocationId = dto.LocationId;
        var targetSecretStashId = dto.SecretStashId;
        LogTreasureAlloc(logger, LogLevel.Information, "assign-entry", new
        {
            dto.GameId,
            dto.LocationId,
            dto.SecretStashId,
            kind = isComposite ? "composite" : "single",
            poolCount,
            poolUnits = poolAssignments.Sum(p => p.Count),
            unlimitedCount
        });

        // Validate XOR
        if ((dto.LocationId is null) == (dto.SecretStashId is null))
        {
            LogTreasureAlloc(logger, LogLevel.Warning, "assign-validation-rejected", new
            {
                reason = "location-stash-xor",
                dto.GameId,
                dto.LocationId,
                dto.SecretStashId
            });
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["LocationId"] = ["Musí být vyplněna buď lokace, nebo tajná skrýš (ne obojí)."]
            });
        }

        if (poolAssignments.Count > 0 && legacyPoolItemIds.Count > 0)
        {
            LogTreasureAlloc(logger, LogLevel.Warning, "assign-validation-rejected", new
            {
                reason = "mixed-pool-contracts",
                dto.GameId,
                poolAssignmentRows = poolAssignments.Count,
                legacyPoolRows = legacyPoolItemIds.Count
            });
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PoolItems"] = ["Použijte buď položky s počtem, nebo starý seznam položek — ne obojí."]
            });
        }

        Dictionary<int, TreasureItem> poolItemsById = [];
        if (poolAssignments.Count > 0)
        {
            var poolIds = poolAssignments.Select(p => p.TreasureItemId).ToHashSet();
            poolItemsById = await db.TreasureItems
                .Include(ti => ti.Item)
                .Where(ti => ti.GameId == dto.GameId && ti.TreasureQuestId == null && poolIds.Contains(ti.Id))
                .ToDictionaryAsync(ti => ti.Id);

            var missing = poolIds.Except(poolItemsById.Keys).ToList();
            if (missing.Count > 0)
            {
                LogTreasureAlloc(logger, LogLevel.Warning, "assign-validation-rejected", new
                {
                    reason = "pool-item-missing",
                    dto.GameId,
                    missing
                });
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["PoolItems"] = ["Některé položky už nejsou v zásobníku."]
                });
            }

            foreach (var assignment in poolAssignments)
            {
                var poolItem = poolItemsById[assignment.TreasureItemId];
                if (assignment.Count <= 0 || assignment.Count > poolItem.Count)
                {
                    LogTreasureAlloc(logger, LogLevel.Warning, "assign-validation-rejected", new
                    {
                        reason = "pool-count-out-of-range",
                        dto.GameId,
                        assignment.TreasureItemId,
                        requested = assignment.Count,
                        available = poolItem.Count
                    });
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["PoolItems"] = [$"Počet pro {poolItem.Item.Name} musí být mezi 1 a {poolItem.Count}."]
                    });
                }

                if (poolItem.Item.IsUnique && assignment.Count != poolItem.Count)
                {
                    LogTreasureAlloc(logger, LogLevel.Warning, "assign-validation-rejected", new
                    {
                        reason = "unique-partial-count",
                        dto.GameId,
                        assignment.TreasureItemId,
                        requested = assignment.Count,
                        available = poolItem.Count
                    });
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["PoolItems"] = [$"{poolItem.Item.Name} je unikátní předmět a musí se přiřadit celý."]
                    });
                }
            }
        }

        if (targetLocationId is int locationId)
        {
            var defaultStash = await PickDefaultStashAsync(db, dto.GameId, locationId);
            if (defaultStash is not null)
            {
                targetLocationId = null;
                targetSecretStashId = defaultStash.SecretStashId;
                LogTreasureAlloc(logger, LogLevel.Information, "default stash picked", new
                {
                    locationId,
                    stashId = defaultStash.SecretStashId,
                    reason = defaultStash.Reason
                });
            }
        }

        await LogGrosTreasureCreateIfNeededAsync(
            db,
            logger,
            dto,
            dto.LocationId,
            targetSecretStashId);

        try
        {
            // Create the quest
            var quest = new TreasureQuest
            {
                Title = dto.Title, Clue = dto.Clue, Difficulty = dto.Difficulty,
                LocationId = targetLocationId, SecretStashId = targetSecretStashId, GameId = dto.GameId
            };
            db.TreasureQuests.Add(quest);
            LogTreasureAlloc(logger, LogLevel.Information, "assign-before-save", new
            {
                phase = "create-quest",
                dto.GameId,
                LocationId = targetLocationId,
                SecretStashId = targetSecretStashId,
                poolUnits = poolAssignments.Sum(p => p.Count)
            });
            await db.SaveChangesAsync(); // get the ID

            // Assign legacy pool items by moving the whole row.
            if (dto.TreasureItemIds is { Count: > 0 })
            {
                var poolItems = await db.TreasureItems
                    .Where(ti => dto.TreasureItemIds.Contains(ti.Id) && ti.TreasureQuestId == null && ti.GameId == dto.GameId)
                    .ToListAsync();
                foreach (var item in poolItems)
                    item.TreasureQuestId = quest.Id;
            }

            foreach (var assignment in poolAssignments)
            {
                var poolItem = poolItemsById[assignment.TreasureItemId];
                LogTreasureAlloc(logger, LogLevel.Information, "assign-item-before", new
                {
                    kind = isComposite ? "composite" : "single",
                    questId = quest.Id,
                    poolItemId = poolItem.Id,
                    poolItem.ItemId,
                    requestedCount = assignment.Count,
                    availableCount = poolItem.Count,
                    LocationId = targetLocationId,
                    SecretStashId = targetSecretStashId
                });
                AssignPoolItemToQuest(poolItem, assignment.Count, quest.Id, db);
                LogTreasureAlloc(logger, LogLevel.Information, "assign-item-after", new
                {
                    kind = isComposite ? "composite" : "single",
                    questId = quest.Id,
                    poolItemId = poolItem.Id,
                    poolItem.ItemId,
                    assignedCount = assignment.Count,
                    remainingPoolCount = poolItem.TreasureQuestId is null ? poolItem.Count : 0,
                    LocationId = targetLocationId,
                    SecretStashId = targetSecretStashId
                });
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

            LogTreasureAlloc(logger, LogLevel.Information, "assign-before-save", new
            {
                phase = "attach-items",
                questId = quest.Id,
                dto.GameId,
                poolCount,
                poolUnits = poolAssignments.Sum(p => p.Count),
                unlimitedCount
            });
            await db.SaveChangesAsync();

            // Reload with includes for response
            var loaded = await db.TreasureQuests
                .Include(t => t.Location)
                .Include(t => t.SecretStash)
                .Include(t => t.TreasureItems).ThenInclude(ti => ti.Item)
                .FirstAsync(t => t.Id == quest.Id);

            LogTreasureAlloc(logger, LogLevel.Information, "assign-success", new
            {
                questId = quest.Id,
                dto.GameId,
                itemCount = loaded.TreasureItems.Sum(ti => ti.Count)
            });

            return TypedResults.Created($"/api/treasure-quests/{quest.Id}",
                new TreasureQuestDetailDto(
                    loaded.Id, loaded.Title, loaded.Clue, loaded.Difficulty,
                    loaded.LocationId, loaded.Location?.Name, loaded.SecretStashId, loaded.SecretStash?.Name, loaded.GameId,
                    loaded.TreasureItems.Select(ti => new TreasureItemDto(ti.Id, ti.ItemId, ti.Item.Name, ti.Count, ti.TreasureQuestId)).ToList()));
        }
        catch (Exception ex)
        {
            LogTreasureAlloc(logger, LogLevel.Error, "assign-catch", new
            {
                dto.GameId,
                dto.LocationId,
                dto.SecretStashId,
                poolCount,
                unlimitedCount
            }, ex);
            throw;
        }
    }

    private sealed record DefaultStashPick(int SecretStashId, string Reason);

    private static async Task<DefaultStashPick?> PickDefaultStashAsync(WorldDbContext db, int gameId, int locationId)
    {
        var candidates = await db.GameSecretStashes
            .Where(gs => gs.GameId == gameId && gs.LocationId == locationId)
            .Select(gs => new
            {
                gs.SecretStashId,
                gs.SecretStash.Name,
                ItemCount = db.TreasureQuests
                    .Where(tq => tq.GameId == gameId && tq.SecretStashId == gs.SecretStashId)
                    .SelectMany(tq => tq.TreasureItems)
                    .Sum(ti => (int?)ti.Count) ?? 0
            })
            .ToListAsync();

        if (candidates.Count == 0)
        {
            return null;
        }

        var minCount = candidates.Min(c => c.ItemCount);
        var tied = candidates
            .Where(c => c.ItemCount == minCount)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.SecretStashId)
            .ToList();

        var selected = tied[0];
        var reason = tied.Count > 1 ? "tiebreak-alpha" : "lowest-count";
        return new DefaultStashPick(selected.SecretStashId, reason);
    }

    private static async Task LogGrosTreasureCreateIfNeededAsync(
        WorldDbContext db,
        ILogger logger,
        AssignTreasureDto dto,
        int? sourceLocationId,
        int? targetSecretStashId)
    {
        if (dto.UnlimitedItems is not { Count: > 0 } unlimitedItems)
        {
            return;
        }

        var itemIds = unlimitedItems.Select(i => i.ItemId).ToHashSet();
        var itemsById = await db.Items
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);
        var gros = unlimitedItems.FirstOrDefault(ui =>
            itemsById.TryGetValue(ui.ItemId, out var item)
            && item.ItemType == ItemType.Money
            && IsGrosItemName(item.Name));

        if (gros is null)
        {
            return;
        }

        LogTreasureAlloc(logger, LogLevel.Information, "gros-treasure-create", new
        {
            locationId = sourceLocationId,
            stashId = targetSecretStashId,
            count = gros.Count
        });
    }

    private static bool IsGrosItemName(string name) =>
        string.Equals(name.Trim(), "Groše", StringComparison.CurrentCultureIgnoreCase)
        || name.Contains("groš", StringComparison.CurrentCultureIgnoreCase);

    private static async Task<Results<Ok<TreasureItemDto>, ValidationProblem, NotFound>> AdjustTreasureItemCount(
        int id,
        AdjustTreasureItemCountDto dto,
        WorldDbContext db,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.TreasurePlanningEndpoints");
        LogTreasureAlloc(logger, LogLevel.Information, "count-adjust-entry", new
        {
            treasureItemId = id,
            dto.Delta,
            dto.Source
        });

        var item = await db.TreasureItems
            .Include(ti => ti.Item)
            .FirstOrDefaultAsync(ti => ti.Id == id);

        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (dto.Delta == 0)
        {
            LogTreasureAlloc(logger, LogLevel.Warning, "count-adjust-validation-rejected", new
            {
                reason = "zero-delta",
                treasureItemId = id
            });
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Delta"] = ["Změna počtu nesmí být nula."]
            });
        }

        if (item.TreasureQuestId is null)
        {
            LogTreasureAlloc(logger, LogLevel.Warning, "count-adjust-validation-rejected", new
            {
                reason = "pool-row-not-target",
                treasureItemId = id
            });
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["TreasureItemId"] = ["Počet lze upravovat jen u již umístěného pokladu."]
            });
        }

        if (item.Item.IsUnique)
        {
            LogTreasureAlloc(logger, LogLevel.Warning, "count-adjust-validation-rejected", new
            {
                reason = "unique-item",
                treasureItemId = id,
                item.ItemId
            });
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["TreasureItemId"] = ["Unikátní předmět nelze upravovat po kusech."]
            });
        }

        if (dto.Delta < 0)
        {
            var removeCount = -dto.Delta;
            if (item.Count - removeCount < 1)
            {
                LogTreasureAlloc(logger, LogLevel.Warning, "count-adjust-validation-rejected", new
                {
                    reason = "below-one",
                    treasureItemId = id,
                    item.Count,
                    dto.Delta
                });
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Delta"] = ["Počet pokladu nesmí klesnout pod 1."]
                });
            }

            item.Count -= removeCount;
            await ReturnToPoolAsync(db, item.GameId, item.ItemId, removeCount);
        }
        else
        {
            var remaining = dto.Delta;
            var poolRows = await db.TreasureItems
                .Where(ti => ti.GameId == item.GameId && ti.ItemId == item.ItemId && ti.TreasureQuestId == null)
                .OrderBy(ti => ti.Id)
                .ToListAsync();
            var available = poolRows.Sum(ti => ti.Count);
            if (available < remaining)
            {
                LogTreasureAlloc(logger, LogLevel.Warning, "count-adjust-validation-rejected", new
                {
                    reason = "not-enough-pool",
                    treasureItemId = id,
                    requested = dto.Delta,
                    available
                });
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Delta"] = [$"V zásobníku je už jen {available} kusů."]
                });
            }

            foreach (var poolRow in poolRows)
            {
                if (remaining == 0) break;
                var take = Math.Min(poolRow.Count, remaining);
                poolRow.Count -= take;
                remaining -= take;
                if (poolRow.Count == 0)
                {
                    db.TreasureItems.Remove(poolRow);
                }
            }
            item.Count += dto.Delta;
        }

        LogTreasureAlloc(logger, LogLevel.Information, "count-adjust-before-save", new
        {
            treasureItemId = id,
            item.ItemId,
            item.GameId,
            dto.Delta,
            item.Count,
            dto.Source
        });
        await db.SaveChangesAsync();

        LogTreasureAlloc(logger, LogLevel.Information, "count-adjust-success", new
        {
            treasureItemId = id,
            item.ItemId,
            item.GameId,
            item.Count
        });

        return TypedResults.Ok(new TreasureItemDto(item.Id, item.ItemId, item.Item.Name, item.Count, item.TreasureQuestId));
    }

    private static void AssignPoolItemToQuest(TreasureItem poolItem, int count, int questId, WorldDbContext db)
    {
        if (count == poolItem.Count)
        {
            poolItem.TreasureQuestId = questId;
            return;
        }

        poolItem.Count -= count;
        db.TreasureItems.Add(new TreasureItem
        {
            ItemId = poolItem.ItemId,
            GameId = poolItem.GameId,
            Count = count,
            TreasureQuestId = questId
        });
    }

    private static async Task ReturnToPoolAsync(WorldDbContext db, int gameId, int itemId, int count)
    {
        var poolRow = await db.TreasureItems
            .FirstOrDefaultAsync(ti => ti.GameId == gameId && ti.ItemId == itemId && ti.TreasureQuestId == null);

        if (poolRow is null)
        {
            db.TreasureItems.Add(new TreasureItem
            {
                GameId = gameId,
                ItemId = itemId,
                Count = count,
                TreasureQuestId = null
            });
            return;
        }

        poolRow.Count += count;
    }

    private static void LogTreasureAlloc(ILogger logger, LogLevel level, string eventName, object payload, Exception? exception = null)
    {
        var json = JsonSerializer.Serialize(new { Event = eventName, Payload = payload }, TreasureAllocLogJsonOptions);
        if (exception is null)
        {
            logger.Log(level, "[treasure-alloc] {Payload}", json);
            return;
        }

        logger.Log(level, exception, "[treasure-alloc] {Payload}", json);
    }

    // ── Issue #160: bulk refill ───────────────────────────────────────────

    /// <summary>
    /// Sweeps every <c>GameItem</c> with <c>IsFindable=true</c> and a non-null
    /// <c>StockCount</c> for the active game, computes the unallocated remainder
    /// against three reward channels (per the user's confirmed inventory):
    /// <list type="bullet">
    ///   <item><c>TreasureItem</c> (pool + treasure-quest, both legs)</item>
    ///   <item><c>QuestReward</c> (regular quest rewards scoped by Quest.GameId)</item>
    ///   <item><c>PersonalQuestItemReward</c> on PersonalQuest templates that
    ///         this game has linked via <c>GamePersonalQuest</c></item>
    /// </list>
    /// MonsterLoot intentionally excluded (situational, encounter-dependent
    /// — confirmed with the user).
    ///
    /// For each item where <c>StockCount &gt; allocated</c>, the remainder is
    /// appended to the existing pool TreasureItem (stack via <c>Count +=</c>)
    /// or a fresh pool row is created. Items where allocations already exceed
    /// stock are skipped and listed in <see cref="RefillPoolResponse.OverAllocated"/>.
    /// </summary>
    private static async Task<Ok<RefillPoolResponse>> RefillPool(int gameId, bool? dryRun, WorldDbContext db)
    {
        // Optional preview mode — compute the same response shape without
        // persisting. UI fetches a preview before showing the confirm popup
        // so the user reviews exactly what will be added before committing.
        var preview = dryRun == true;

        // Wrap the read-then-write in SERIALIZABLE so two concurrent refills
        // can't both compute `remaining` from the same allocation snapshot
        // and double-append. Postgres will retry-fail one of the txns; the
        // caller can press the button again. Cost is negligible — refill is
        // organizer-triggered, not on a hot path.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // 1. Fetch findable items with stock for this game.
        var gameItems = await db.GameItems
            .Where(gi => gi.GameId == gameId
                && gi.IsFindable
                && gi.StockCount != null
                && gi.Item.ItemType != ItemType.Money)
            .Include(gi => gi.Item)
            .ToListAsync();

        if (gameItems.Count == 0)
        {
            await tx.CommitAsync();
            return TypedResults.Ok(new RefillPoolResponse(0,
                Array.Empty<RefillPoolItemDto>(), Array.Empty<RefillPoolOverAllocationDto>()));
        }

        var itemIds = gameItems.Select(gi => gi.ItemId).ToHashSet();

        // 2. Sum allocations across the three reward channels.

        // Channel A: TreasureItem — covers BOTH the pool (TreasureQuestId == null)
        // and items already placed inside a treasure quest. Existing /available-items
        // endpoint sums them the same way; staying consistent.
        var treasureUsed = await db.TreasureItems
            .Where(ti => ti.GameId == gameId && itemIds.Contains(ti.ItemId))
            .GroupBy(ti => ti.ItemId)
            .Select(g => new { ItemId = g.Key, Used = g.Sum(ti => ti.Count) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Used);

        // Channel B: QuestReward — Quest.GameId is nullable on the entity, but
        // for stock allocation we only care about quests pinned to THIS game.
        var questRewardUsed = await db.QuestRewards
            .Where(qr => qr.Quest.GameId == gameId && itemIds.Contains(qr.ItemId))
            .GroupBy(qr => qr.ItemId)
            .Select(g => new { ItemId = g.Key, Used = g.Sum(qr => qr.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Used);

        // Channel C: PersonalQuestItemReward — PersonalQuest is a catalog
        // template; per-game linkage via GamePersonalQuest. Each template's
        // ItemRewards count once per game-link (planning view; the actual
        // per-character payout depends on claims).
        var pqRewardUsed = await db.GamePersonalQuests
            .Where(gpq => gpq.GameId == gameId)
            .SelectMany(gpq => gpq.PersonalQuest.ItemRewards)
            .Where(r => itemIds.Contains(r.ItemId))
            .GroupBy(r => r.ItemId)
            .Select(g => new { ItemId = g.Key, Used = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Used);

        // Pre-load all pool rows for this game in one shot so the per-item
        // append below is dictionary lookup, not a DB roundtrip per item.
        // The /pool POST endpoint can leave multiple unstacked rows per
        // item (legacy behavior), so reduce to a single representative row
        // per ItemId — the refill endpoint always stacks remainder onto one.
        var existingPoolByItem = (await db.TreasureItems
                .Where(ti => ti.GameId == gameId && ti.TreasureQuestId == null
                    && itemIds.Contains(ti.ItemId))
                .ToListAsync())
            .GroupBy(ti => ti.ItemId)
            .ToDictionary(g => g.Key, g => g.First());

        // 3. For each findable game-item: compute remaining; allocate or report.
        var added = new List<RefillPoolItemDto>();
        var overAllocated = new List<RefillPoolOverAllocationDto>();

        foreach (var gi in gameItems)
        {
            var t = treasureUsed.GetValueOrDefault(gi.ItemId, 0);
            var q = questRewardUsed.GetValueOrDefault(gi.ItemId, 0);
            var p = pqRewardUsed.GetValueOrDefault(gi.ItemId, 0);
            var allocated = t + q + p;
            var stock = gi.StockCount!.Value;

            if (allocated > stock)
            {
                overAllocated.Add(new RefillPoolOverAllocationDto(
                    gi.ItemId, gi.Item.Name, stock, allocated, allocated - stock));
                continue;
            }

            var remaining = stock - allocated;
            if (remaining <= 0) continue;

            // Refill always consolidates remainder onto a single per-item
            // pool row (stacks via Count +=). The /pool POST endpoint
            // currently creates fresh rows on every call — refill smooths
            // that over so re-running this action is idempotent.
            if (!preview)
            {
                if (existingPoolByItem.TryGetValue(gi.ItemId, out var existingPool))
                {
                    existingPool.Count += remaining;
                }
                else
                {
                    db.TreasureItems.Add(new TreasureItem
                    {
                        GameId = gameId,
                        ItemId = gi.ItemId,
                        TreasureQuestId = null,
                        Count = remaining
                    });
                }
            }
            added.Add(new RefillPoolItemDto(gi.ItemId, gi.Item.Name, remaining));
        }

        if (preview)
        {
            await tx.RollbackAsync();
        }
        else
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        return TypedResults.Ok(new RefillPoolResponse(
            ItemsAdded: added.Sum(a => a.Added),
            Added: added,
            OverAllocated: overAllocated));
    }
}
