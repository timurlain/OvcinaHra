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
                i.ImagePath
            })
            .ToListAsync();
        var items = rows.Select(r => new ItemListDto(
            r.Id, r.Name, r.ItemType, r.Effect, r.PhysicalForm, r.IsCraftable,
            r.ReqWarrior, r.ReqArcher, r.ReqMage, r.ReqThief,
            r.IsUnique, r.IsLimited, r.ImagePath,
            ImageUrl: string.IsNullOrWhiteSpace(r.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "items", r.Id, "small"))).ToList();
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<ItemDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();

        return TypedResults.Ok(ToDetailDto(item));
    }

    private static async Task<Created<ItemDetailDto>> Create(CreateItemDto dto, WorldDbContext db)
    {
        var item = new Item
        {
            Name = dto.Name,
            ItemType = dto.ItemType,
            Effect = dto.Effect,
            PhysicalForm = dto.PhysicalForm,
            IsCraftable = dto.IsCraftable,
            ClassRequirements = new ClassRequirements(dto.ReqWarrior, dto.ReqArcher, dto.ReqMage, dto.ReqThief),
            IsUnique = dto.IsUnique,
            IsLimited = dto.IsLimited
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/items/{item.Id}", ToDetailDto(item));
    }

    private static async Task<Results<NoContent, NotFound>> Update(int id, UpdateItemDto dto, WorldDbContext db)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return TypedResults.NotFound();

        item.Name = dto.Name;
        item.ItemType = dto.ItemType;
        item.Effect = dto.Effect;
        item.PhysicalForm = dto.PhysicalForm;
        item.IsCraftable = dto.IsCraftable;
        item.ClassRequirements = new ClassRequirements(dto.ReqWarrior, dto.ReqArcher, dto.ReqMage, dto.ReqThief);
        item.IsUnique = dto.IsUnique;
        item.IsLimited = dto.IsLimited;

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

        var result = gameItems
            .Select(gi => new GameItemListDto(
                gi.Item.Id, gi.Item.Name, gi.Item.ItemType, gi.Item.Effect, gi.Item.PhysicalForm,
                gi.Item.IsCraftable,
                gi.Item.ClassRequirements.Warrior, gi.Item.ClassRequirements.Archer,
                gi.Item.ClassRequirements.Mage, gi.Item.ClassRequirements.Thief,
                gi.Item.IsUnique, gi.Item.IsLimited, gi.Item.ImagePath,
                gi.GameId, gi.Price, gi.StockCount, gi.IsSold, gi.SaleCondition, gi.IsFindable,
                summaryByItemId.GetValueOrDefault(gi.ItemId),
                ImageUrl: string.IsNullOrWhiteSpace(gi.Item.ImagePath) ? null : ImageEndpoints.ThumbUrl(http, "items", gi.Item.Id, "small")))
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

    private static async Task<Results<NoContent, NotFound>> UpdateGameItem(int gameId, int itemId, UpdateGameItemDto dto, WorldDbContext db)
    {
        var gi = await db.GameItems.FindAsync(gameId, itemId);
        if (gi is null) return TypedResults.NotFound();

        gi.Price = dto.Price;
        gi.StockCount = dto.StockCount;
        gi.IsSold = dto.IsSold;
        gi.SaleCondition = dto.SaleCondition;
        gi.IsFindable = dto.IsFindable;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteGameItem(int gameId, int itemId, WorldDbContext db)
    {
        var gi = await db.GameItems.FindAsync(gameId, itemId);
        if (gi is null) return TypedResults.NotFound();
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
            i.IsUnique, i.IsLimited, i.ImagePath))
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
        item.IsUnique, item.IsLimited, item.ImagePath);
}
