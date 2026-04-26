using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;
using OvcinaHra.Shared.Dtos;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Endpoints;

public static class ItemEndpoints
{
    public static RouteGroupBuilder MapItemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/items").WithTags("Items");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Class/level query — "can this class at this level use this item?"
        group.MapGet("/usable", GetUsableItems);
        group.MapGet("/{id:int}/can-use", CanUseItem);

        // Per-game item config
        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapPost("/game-item", CreateGameItem);
        group.MapPut("/game-item/{gameId:int}/{itemId:int}", UpdateGameItem);
        group.MapDelete("/game-item/{gameId:int}/{itemId:int}", DeleteGameItem);

        // Detail-page aggregate: powers Tvorba + Výskyt + Obchod tabs on ItemDetail.
        group.MapGet("/{id:int}/usage", GetUsage);

        return group;
    }

    private static async Task<Ok<List<ItemListDto>>> GetAll(WorldDbContext db, HttpContext http)
    {
        var rows = await db.Items
            .OrderBy(i => i.Name)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.ItemType,
                i.Effect,
                i.PhysicalForm,
                i.IsCraftable,
                ReqWarrior = i.ClassRequirements.Warrior,
                ReqArcher = i.ClassRequirements.Archer,
                ReqMage = i.ClassRequirements.Mage,
                ReqThief = i.ClassRequirements.Thief,
                i.IsUnique,
                i.IsLimited,
                i.ImagePath,
                i.Note
            })
            .ToListAsync();

        // Issue #154 — collect ItemIds that have at least one CraftingRecipe
        // (in any game) so the catalog tile only shows the IsCraftable badge
        // when there's actually a recipe somewhere; one query, not N.
        var craftedItemIds = await db.CraftingRecipes
            .Select(r => r.OutputItemId)
            .Distinct()
            .ToListAsync();
        var craftedSet = craftedItemIds.ToHashSet();

        var items = rows.Select(r => new ItemListDto(
            r.Id, r.Name, r.ItemType, r.Effect, r.PhysicalForm, r.IsCraftable,
            r.ReqWarrior, r.ReqArcher, r.ReqMage, r.ReqThief,
            r.IsUnique, r.IsLimited, r.ImagePath,
            Note: r.Note,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "items", r.Id, "small"),
            HasRecipe: craftedSet.Contains(r.Id))).ToList();
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<ItemDetailDto>, NotFound>> GetById(int id, WorldDbContext db, HttpContext http)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();

        // Populate ImageUrl on the detail DTO so the new ItemDetail page (and any
        // future caller) doesn't have to make a second /api/images/items/{id}
        // round-trip just to render the hero (per Copilot review on PR #90).
        var imageUrl = string.IsNullOrWhiteSpace(item.ImagePath)
            ? null
            : ImageEndpoints.ThumbUrl(http, "items", item.Id, "small");

        // Issue #154 — same flag as the catalog list, populated here so the
        // quick-peek popup can gate the IsCraftable badge consistently.
        var hasRecipe = await db.CraftingRecipes.AnyAsync(r => r.OutputItemId == id);

        return TypedResults.Ok(ToDetailDto(item) with { ImageUrl = imageUrl, HasRecipe = hasRecipe });
    }

    // Note cap used on both Create and Update (issue #120). 2000 chars is a
    // generous ceiling for organizer commentary; anything longer probably
    // belongs in the wiki. DevExpress DxMemo MaxLength isn't reliable in
    // 25.2.5 so the cap is enforced server-side.
    private const int NoteMaxLength = 2000;

    private static async Task<Results<Created<ItemDetailDto>, ProblemHttpResult>> Create(CreateItemDto dto, WorldDbContext db)
    {
        var note = dto.Note?.Trim();
        if (!string.IsNullOrEmpty(note) && note.Length > NoteMaxLength)
            return TypedResults.Problem(
                title: "Poznámka je příliš dlouhá",
                detail: $"Poznámka nesmí být delší než {NoteMaxLength} znaků.",
                statusCode: StatusCodes.Status400BadRequest);

        var item = new Item
        {
            Name = dto.Name,
            ItemType = dto.ItemType,
            Effect = dto.Effect,
            PhysicalForm = dto.PhysicalForm,
            IsCraftable = dto.IsCraftable,
            ClassRequirements = new ClassRequirements(dto.ReqWarrior, dto.ReqArcher, dto.ReqMage, dto.ReqThief),
            IsUnique = dto.IsUnique,
            IsLimited = dto.IsLimited,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/items/{item.Id}", ToDetailDto(item));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Update(int id, UpdateItemDto dto, WorldDbContext db)
    {
        // Check existence first so a request for a missing id returns 404
        // (REST contract), not 400 from any downstream length/validation.
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();

        // Effect now reachable via inline edit (issue #119) — cap the length
        // server-side since DevExpress DxTextBox MaxLength isn't reliable in
        // 25.2.5. 500 chars is a generous ceiling for an in-game item effect
        // blurb; anything longer probably belongs in the catalog detail.
        var effect = dto.Effect?.Trim();
        if (!string.IsNullOrEmpty(effect) && effect.Length > 500)
            return TypedResults.Problem(
                title: "Efekt je příliš dlouhý",
                detail: "Popis efektu nesmí být delší než 500 znaků.",
                statusCode: StatusCodes.Status400BadRequest);

        // Poznámka (issue #120) — same trim + cap pattern as Effect.
        var note = dto.Note?.Trim();
        if (!string.IsNullOrEmpty(note) && note.Length > NoteMaxLength)
            return TypedResults.Problem(
                title: "Poznámka je příliš dlouhá",
                detail: $"Poznámka nesmí být delší než {NoteMaxLength} znaků.",
                statusCode: StatusCodes.Status400BadRequest);

        item.Name = dto.Name;
        item.ItemType = dto.ItemType;
        item.Effect = string.IsNullOrWhiteSpace(effect) ? null : effect;
        item.PhysicalForm = dto.PhysicalForm;
        item.IsCraftable = dto.IsCraftable;
        item.ClassRequirements = new ClassRequirements(dto.ReqWarrior, dto.ReqArcher, dto.ReqMage, dto.ReqThief);
        item.IsUnique = dto.IsUnique;
        item.IsLimited = dto.IsLimited;
        item.Note = string.IsNullOrWhiteSpace(note) ? null : note;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();
        db.Items.Remove(item);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<GameItemListDto>>> GetByGame(int gameId, WorldDbContext db, HttpContext http)
    {
        var gameItems = await db.GameItems
            .Where(gi => gi.GameId == gameId)
            .Include(gi => gi.Item)
            .OrderBy(gi => gi.Item.Name)
            .ToListAsync();

        var recipes = await db.CraftingRecipes
            .Where(r => r.GameId == gameId)
            .Include(r => r.Ingredients).ThenInclude(i => i.Item)
            .Include(r => r.BuildingRequirements).ThenInclude(b => b.Building)
            .Include(r => r.SkillRequirements).ThenInclude(s => s.GameSkill)
            .ToListAsync();

        // GroupBy is defensive — schema doesn't prevent multiple recipes for the same OutputItemId
        // in a single game, and ToDictionary would throw on duplicates.
        var summaryByItemId = recipes
            .GroupBy(r => r.OutputItemId)
            .ToDictionary(g => g.Key, g => BuildRecipeSummary(g.First()));

        // Issue #154 — keep the recipe-existence flag separate from the
        // RecipeSummary string. BuildRecipeSummary returns null when a
        // recipe row exists but has no ingredients/buildings/skills, so
        // deriving HasRecipe from the summary would hide the IsCraftable
        // badge incorrectly. Source the flag from the dictionary key set.
        var hasRecipeByItemId = recipes.Select(r => r.OutputItemId).ToHashSet();

        var result = gameItems
            .Select(gi => new GameItemListDto(
                gi.Item.Id, gi.Item.Name, gi.Item.ItemType, gi.Item.Effect, gi.Item.PhysicalForm,
                gi.Item.IsCraftable,
                gi.Item.ClassRequirements.Warrior, gi.Item.ClassRequirements.Archer,
                gi.Item.ClassRequirements.Mage, gi.Item.ClassRequirements.Thief,
                gi.Item.IsUnique, gi.Item.IsLimited, gi.Item.ImagePath,
                gi.GameId, gi.Price, gi.StockCount, gi.IsSold, gi.SaleCondition, gi.IsFindable,
                summaryByItemId.GetValueOrDefault(gi.ItemId),
                Note: gi.Item.Note,
                ImageUrl: string.IsNullOrWhiteSpace(gi.Item.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "items", gi.Item.Id, "small"),
                HasRecipe: hasRecipeByItemId.Contains(gi.ItemId)))
            .ToList();

        return TypedResults.Ok(result);
    }

    private static string? BuildRecipeSummary(CraftingRecipe r)
    {
        var parts = new List<string>();
        if (r.Ingredients.Count > 0)
        {
            parts.Add(string.Join(", ",
                r.Ingredients.OrderBy(i => i.Item.Name).Select(i => $"{i.Quantity}× {i.Item.Name}")));
        }
        if (r.BuildingRequirements.Count > 0)
        {
            parts.Add(string.Join(", ",
                r.BuildingRequirements.OrderBy(b => b.Building.Name).Select(b => b.Building.Name)));
        }
        if (r.SkillRequirements.Count > 0)
        {
            parts.Add(string.Join(", ",
                r.SkillRequirements.OrderBy(s => s.GameSkill.Name).Select(s => s.GameSkill.Name)));
        }
        return parts.Count > 0 ? string.Join(" │ ", parts) : null;
    }

    private static async Task<Results<Created<GameItemDto>, Conflict>> CreateGameItem(CreateGameItemDto dto, WorldDbContext db)
    {
        var exists = await db.GameItems.AnyAsync(gi => gi.GameId == dto.GameId && gi.ItemId == dto.ItemId);
        if (exists) return TypedResults.Conflict();

        var gi = new GameItem
        {
            GameId = dto.GameId,
            ItemId = dto.ItemId,
            Price = dto.Price,
            StockCount = dto.StockCount,
            IsSold = dto.IsSold,
            SaleCondition = dto.SaleCondition,
            IsFindable = dto.IsFindable
        };
        db.GameItems.Add(gi);
        await db.SaveChangesAsync();

        var itemName = (await db.Items.FindAsync(dto.ItemId))?.Name ?? "";
        return TypedResults.Created($"/api/items/game-item/{gi.GameId}/{gi.ItemId}",
            new GameItemDto(gi.GameId, gi.ItemId, itemName, gi.Price, gi.StockCount, gi.IsSold, gi.SaleCondition, gi.IsFindable));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UpdateGameItem(int gameId, int itemId, UpdateGameItemDto dto, WorldDbContext db)
    {
        // Check existence first — a request for a missing (gameId,itemId) link
        // must return 404, not 400 from downstream validation. Matches REST
        // expectations for clients and keeps semantics consistent with Update.
        var gi = await db.GameItems.FindAsync(gameId, itemId);
        if (gi is null) return TypedResults.NotFound();

        // Inline-edit (issue #119) hits this endpoint directly from the grid,
        // so validation guards bad inputs with Czech ProblemDetails that the
        // grid can surface verbatim.
        if (dto.Price is < 0)
            return TypedResults.Problem(
                title: "Neplatná cena",
                detail: "Cena nesmí být záporná.",
                statusCode: StatusCodes.Status400BadRequest);

        if (dto.StockCount is < 0)
            return TypedResults.Problem(
                title: "Neplatný sklad",
                detail: "Počet na skladě nesmí být záporný.",
                statusCode: StatusCodes.Status400BadRequest);

        var saleCondition = dto.SaleCondition?.Trim();
        if (!string.IsNullOrEmpty(saleCondition) && saleCondition.Length > 200)
            return TypedResults.Problem(
                title: "Podmínka prodeje je příliš dlouhá",
                detail: "Podmínka prodeje nesmí být delší než 200 znaků.",
                statusCode: StatusCodes.Status400BadRequest);

        gi.Price = dto.Price;
        gi.StockCount = dto.StockCount;
        gi.IsSold = dto.IsSold;
        gi.SaleCondition = string.IsNullOrWhiteSpace(saleCondition) ? null : saleCondition;
        gi.IsFindable = dto.IsFindable;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Removing the per-game link must not strand ghost TreasureItem /
    // MonsterLoot rows that still reference this (GameId, ItemId) pair —
    // see issue #99. Block the delete with a Czech ProblemDetails(400)
    // listing the offending TreasureQuest titles / pool count / Monster
    // names so the organizer knows what to detach first. QuestReward /
    // PersonalQuestItemReward / CraftingIngredient are catalog-level (not
    // per-game) and are left alone here; follow-ups can tighten those.
    private static async Task<IResult> DeleteGameItem(int gameId, int itemId, WorldDbContext db)
    {
        var gi = await db.GameItems.FindAsync(gameId, itemId);
        if (gi is null) return TypedResults.NotFound();

        var treasureQuestTitles = await db.TreasureItems
            .Where(ti => ti.GameId == gameId && ti.ItemId == itemId && ti.TreasureQuestId != null)
            .Select(ti => ti.TreasureQuest!.Title)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        var poolCount = await db.TreasureItems
            .Where(ti => ti.GameId == gameId && ti.ItemId == itemId && ti.TreasureQuestId == null)
            .SumAsync(ti => (int?)ti.Count) ?? 0;

        var monsterNames = await db.MonsterLoots
            .Where(ml => ml.GameId == gameId && ml.ItemId == itemId)
            .Select(ml => ml.Monster.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        if (treasureQuestTitles.Count > 0 || poolCount > 0 || monsterNames.Count > 0)
        {
            var parts = new List<string>();
            if (treasureQuestTitles.Count > 0)
                parts.Add($"je součástí pokladu: {string.Join(", ", treasureQuestTitles)}");
            if (poolCount > 0)
                parts.Add($"v zásobníku: {poolCount} ks");
            if (monsterNames.Count > 0)
                parts.Add($"kořist příšer: {string.Join(", ", monsterNames)}");

            return TypedResults.Problem(
                title: "Položku nelze odebrat",
                detail: $"Tuto položku nelze odebrat — {string.Join("; ", parts)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        db.GameItems.Remove(gi);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // Returns all items usable by that class at that level.
    // An item is usable if: unrestricted (all requirements 0),
    // OR the specified class requirement is > 0 and <= the given level.
    private static async Task<Ok<List<ItemListDto>>> GetUsableItems(
        PlayerClass playerClass, int level, WorldDbContext db, int? gameId = null)
    {
        var query = db.Items.AsQueryable();
        if (gameId.HasValue)
            query = query.Where(i => i.GameItems.Any(gi => gi.GameId == gameId.Value));

        var items = await query.ToListAsync();

        var usable = items.Where(i =>
        {
            var cr = i.ClassRequirements;
            if (cr.IsUnrestricted) return true;

            var required = cr.GetRequirement(playerClass);
            return required > 0 && level >= required;
        })
        .Select(i => new ItemListDto(
            i.Id, i.Name, i.ItemType, i.Effect, i.PhysicalForm, i.IsCraftable,
            i.ClassRequirements.Warrior, i.ClassRequirements.Archer,
            i.ClassRequirements.Mage, i.ClassRequirements.Thief,
            i.IsUnique, i.IsLimited, i.ImagePath,
            Note: i.Note))
        .OrderBy(i => i.Name)
        .ToList();

        return TypedResults.Ok(usable);
    }

    // Check if a specific item can be used by a given class at a given level.
    private static async Task<Results<Ok<ItemUsabilityDto>, NotFound>> CanUseItem(
        int id, PlayerClass playerClass, int level, WorldDbContext db)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();

        var cr = item.ClassRequirements;
        var className = playerClass.GetDisplayName();

        bool canUse;
        int requiredLevel;
        string reason;

        if (cr.IsUnrestricted)
        {
            canUse = true;
            requiredLevel = 0;
            reason = "Bez omezení — může použít kdokoli";
        }
        else
        {
            requiredLevel = cr.GetRequirement(playerClass);

            if (requiredLevel == 0)
            {
                canUse = false;
                reason = $"{className} nemůže tento předmět použít";
            }
            else if (level >= requiredLevel)
            {
                canUse = true;
                reason = $"Ano — vyžaduje {className} úroveň {requiredLevel}+";
            }
            else
            {
                canUse = false;
                reason = $"Ne — vyžaduje {className} úroveň {requiredLevel}, máš {level}";
            }
        }

        return TypedResults.Ok(new ItemUsabilityDto(item.Id, item.Name, canUse, requiredLevel, reason,
            cr.Warrior, cr.Archer, cr.Mage, cr.Thief));
    }

    private static ItemDetailDto ToDetailDto(Item item) => new(
        item.Id, item.Name, item.ItemType, item.Effect, item.PhysicalForm, item.IsCraftable,
        item.ClassRequirements.Warrior, item.ClassRequirements.Archer,
        item.ClassRequirements.Mage, item.ClassRequirements.Thief,
        item.IsUnique, item.IsLimited, item.ImagePath,
        Note: item.Note);

    // Detail-page aggregate. One round-trip returns:
    //  - CraftedBy:    recipes whose OutputItemId == this item
    //  - UsedIn:       recipes that include this item as a CraftingIngredient
    //  - MonsterLoot:  per-game MonsterLoot rows (with monster name + thumb URL)
    //  - QuestRewards: per-game QuestReward rows (only quests bound to a game)
    //  - Treasures:    per-game TreasureItem rows
    //  - Shops:        per-game GameItem rows
    private static async Task<Results<Ok<ItemUsageDto>, NotFound>> GetUsage(int id, WorldDbContext db, HttpContext http)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();

        var craftedBy = await db.CraftingRecipes
            .Where(r => r.OutputItemId == id)
            .Select(r => new ItemUsageRecipeDto(
                r.Id,
                // Issue #218 — Recipe.GameId is nullable; catalog templates
                // emit a null Game ref. Tvorba tab groups them under
                // a "Šablona katalogu" header.
                r.Game == null ? null : new ItemUsageGameRefDto(r.Game.Id, r.Game.Name, r.Game.Edition),
                r.OutputItemId, r.OutputItem.Name,
                r.LocationId, r.Location != null ? r.Location.Name : null,
                r.Ingredients.Select(i => new CraftingIngredientDto(i.ItemId, i.Item.Name, i.Quantity)).ToList(),
                r.BuildingRequirements.Select(b => new CraftingBuildingReqDto(b.BuildingId, b.Building.Name)).ToList(),
                r.IngredientNotes))
            .ToListAsync();

        var usedIn = await db.CraftingRecipes
            .Where(r => r.Ingredients.Any(i => i.ItemId == id))
            .Select(r => new ItemUsageRecipeDto(
                r.Id,
                // Issue #218 — Recipe.GameId is nullable; catalog templates
                // emit a null Game ref. Tvorba tab groups them under
                // a "Šablona katalogu" header.
                r.Game == null ? null : new ItemUsageGameRefDto(r.Game.Id, r.Game.Name, r.Game.Edition),
                r.OutputItemId, r.OutputItem.Name,
                r.LocationId, r.Location != null ? r.Location.Name : null,
                r.Ingredients.Select(i => new CraftingIngredientDto(i.ItemId, i.Item.Name, i.Quantity)).ToList(),
                r.BuildingRequirements.Select(b => new CraftingBuildingReqDto(b.BuildingId, b.Building.Name)).ToList(),
                r.IngredientNotes))
            .ToListAsync();

        // Project bare path first; resolve thumb URL host-side after materialization.
        var monsterLootRows = await db.MonsterLoots
            .Where(ml => ml.ItemId == id)
            .Select(ml => new
            {
                ml.MonsterId,
                MonsterName = ml.Monster.Name,
                MonsterImagePath = ml.Monster.ImagePath,
                ml.GameId,
                GameName = ml.Game.Name,
                ml.Game.Edition,
                ml.Quantity
            })
            .ToListAsync();

        var monsterLoot = monsterLootRows.Select(ml => new ItemUsageMonsterLootDto(
            ml.MonsterId, ml.MonsterName,
            string.IsNullOrWhiteSpace(ml.MonsterImagePath) ? null : ImageEndpoints.ThumbUrl(http, "monsters", ml.MonsterId, "small"),
            new ItemUsageGameRefDto(ml.GameId, ml.GameName, ml.Edition),
            ml.Quantity)).ToList();

        var questRewards = await db.QuestRewards
            .Where(qr => qr.ItemId == id && qr.Quest.GameId != null)
            .Select(qr => new ItemUsageQuestRewardDto(
                qr.QuestId,
                qr.Quest.Name,
                new ItemUsageGameRefDto(qr.Quest.GameId!.Value, qr.Quest.Game!.Name, qr.Quest.Game.Edition),
                qr.Quantity))
            .ToListAsync();

        var treasures = await db.TreasureItems
            .Where(ti => ti.ItemId == id && ti.TreasureQuestId != null)
            .Select(ti => new ItemUsageTreasureDto(
                ti.TreasureQuestId!.Value,
                ti.TreasureQuest!.Title,
                new ItemUsageGameRefDto(ti.GameId, ti.Game.Name, ti.Game.Edition),
                ti.Count))
            .ToListAsync();

        var shops = await db.GameItems
            .Where(gi => gi.ItemId == id)
            .Select(gi => new ItemUsageShopDto(
                new ItemUsageGameRefDto(gi.GameId, gi.Game.Name, gi.Game.Edition),
                gi.Price, gi.StockCount, gi.IsSold, gi.SaleCondition, gi.IsFindable))
            .ToListAsync();

        return TypedResults.Ok(new ItemUsageDto(
            item.Id, item.Name,
            craftedBy, usedIn,
            monsterLoot, questRewards, treasures, shops));
    }
}
