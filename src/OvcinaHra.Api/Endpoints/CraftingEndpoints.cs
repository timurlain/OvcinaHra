using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class CraftingEndpoints
{
    public static RouteGroupBuilder MapCraftingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/crafting").WithTags("Crafting");

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapDelete("/{id:int}", Delete);

        group.MapPost("/{id:int}/ingredients", AddIngredient);
        group.MapDelete("/{id:int}/ingredients/{itemId:int}", RemoveIngredient);

        group.MapPost("/{id:int}/buildings", AddBuildingReq);
        group.MapDelete("/{id:int}/buildings/{buildingId:int}", RemoveBuildingReq);

        return group;
    }

    private static async Task<Ok<List<CraftingRecipeListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var recipes = await db.CraftingRecipes
            .Where(r => r.GameId == gameId)
            .Include(r => r.OutputItem)
            .Include(r => r.Location)
            .OrderBy(r => r.OutputItem.Name)
            .Select(r => new CraftingRecipeListDto(
                r.Id, r.OutputItemId, r.OutputItem.Name,
                r.LocationId, r.Location != null ? r.Location.Name : null, r.GameId))
            .ToListAsync();
        return TypedResults.Ok(recipes);
    }

    private static async Task<Results<Ok<CraftingRecipeDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var r = await db.CraftingRecipes
            .Include(r => r.OutputItem)
            .Include(r => r.Location)
            .Include(r => r.Ingredients).ThenInclude(i => i.Item)
            .Include(r => r.BuildingRequirements).ThenInclude(br => br.Building)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (r is null) return TypedResults.NotFound();

        return TypedResults.Ok(new CraftingRecipeDetailDto(
            r.Id, r.OutputItemId, r.OutputItem.Name,
            r.LocationId, r.Location?.Name, r.GameId,
            r.Ingredients.Select(i => new CraftingIngredientDto(i.ItemId, i.Item.Name, i.Quantity)).ToList(),
            r.BuildingRequirements.Select(br => new CraftingBuildingReqDto(br.BuildingId, br.Building.Name)).ToList()));
    }

    private static async Task<Created<CraftingRecipeListDto>> Create(CreateCraftingRecipeDto dto, WorldDbContext db)
    {
        var r = new CraftingRecipe { GameId = dto.GameId, OutputItemId = dto.OutputItemId, LocationId = dto.LocationId };
        db.CraftingRecipes.Add(r);
        await db.SaveChangesAsync();

        var item = await db.Items.FindAsync(dto.OutputItemId);
        var loc = dto.LocationId.HasValue ? await db.Locations.FindAsync(dto.LocationId) : null;
        return TypedResults.Created($"/api/crafting/{r.Id}",
            new CraftingRecipeListDto(r.Id, r.OutputItemId, item?.Name ?? "", r.LocationId, loc?.Name, r.GameId));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var r = await db.CraftingRecipes.FindAsync(id);
        if (r is null) return TypedResults.NotFound();
        db.CraftingRecipes.Remove(r);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, Conflict>> AddIngredient(int id, AddCraftingIngredientDto dto, WorldDbContext db)
    {
        if (await db.CraftingIngredients.AnyAsync(ci => ci.CraftingRecipeId == id && ci.ItemId == dto.ItemId))
            return TypedResults.Conflict();
        db.CraftingIngredients.Add(new CraftingIngredient { CraftingRecipeId = id, ItemId = dto.ItemId, Quantity = dto.Quantity });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/crafting/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveIngredient(int id, int itemId, WorldDbContext db)
    {
        var ci = await db.CraftingIngredients.FindAsync(id, itemId);
        if (ci is null) return TypedResults.NotFound();
        db.CraftingIngredients.Remove(ci);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, Conflict>> AddBuildingReq(int id, AddCraftingBuildingReqDto dto, WorldDbContext db)
    {
        if (await db.CraftingBuildingRequirements.AnyAsync(cb => cb.CraftingRecipeId == id && cb.BuildingId == dto.BuildingId))
            return TypedResults.Conflict();
        db.CraftingBuildingRequirements.Add(new CraftingBuildingRequirement { CraftingRecipeId = id, BuildingId = dto.BuildingId });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/crafting/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveBuildingReq(int id, int buildingId, WorldDbContext db)
    {
        var cb = await db.CraftingBuildingRequirements.FindAsync(id, buildingId);
        if (cb is null) return TypedResults.NotFound();
        db.CraftingBuildingRequirements.Remove(cb);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
