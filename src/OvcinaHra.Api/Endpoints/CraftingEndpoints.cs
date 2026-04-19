using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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
        group.MapPut("/{id:int}", Update);
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
            .Include(r => r.SkillRequirements)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (r is null) return TypedResults.NotFound();

        return TypedResults.Ok(ToDetailDto(r));
    }

    private static async Task<Results<Created<CraftingRecipeDetailDto>, BadRequest<ProblemDetails>, NotFound>> Create(
        CreateCraftingRecipeDto dto, WorldDbContext db, CancellationToken ct)
    {
        var skillIds = dto.RequiredSkillIds?.Distinct().ToList() ?? [];
        var problem = await ValidateRequiredSkillsAsync(db, dto.GameId, skillIds, ct);
        if (problem is not null) return TypedResults.BadRequest(problem);

        var r = new CraftingRecipe
        {
            GameId = dto.GameId,
            OutputItemId = dto.OutputItemId,
            LocationId = dto.LocationId,
            SkillRequirements = skillIds
                .Select(sid => new CraftingSkillRequirement { GameSkillId = sid })
                .ToList()
        };
        db.CraftingRecipes.Add(r);
        await db.SaveChangesAsync(ct);

        // Reload with navs for the response.
        var created = await db.CraftingRecipes
            .Include(x => x.OutputItem)
            .Include(x => x.Location)
            .Include(x => x.Ingredients).ThenInclude(i => i.Item)
            .Include(x => x.BuildingRequirements).ThenInclude(br => br.Building)
            .Include(x => x.SkillRequirements)
            .FirstAsync(x => x.Id == r.Id, ct);

        return TypedResults.Created($"/api/crafting/{created.Id}", ToDetailDto(created));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>>> Update(
        int id, UpdateCraftingRecipeDto dto, WorldDbContext db, CancellationToken ct)
    {
        var recipe = await db.CraftingRecipes
            .Include(r => r.SkillRequirements)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (recipe is null) return TypedResults.NotFound();

        var itemExists = await db.Items.AnyAsync(i => i.Id == dto.OutputItemId, ct);
        if (!itemExists)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Vybraný výstupní předmět neexistuje.",
                Detail = $"Předmět s ID {dto.OutputItemId} nebyl nalezen.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (dto.LocationId.HasValue)
        {
            var locationExists = await db.Locations.AnyAsync(l => l.Id == dto.LocationId.Value, ct);
            if (!locationExists)
            {
                return TypedResults.BadRequest(new ProblemDetails
                {
                    Title = "Zvolená lokace neexistuje.",
                    Detail = $"Lokace s ID {dto.LocationId.Value} nebyla nalezena.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        var skillIds = dto.RequiredSkillIds?.Distinct().ToList() ?? [];
        var problem = await ValidateRequiredSkillsAsync(db, recipe.GameId, skillIds, ct);
        if (problem is not null) return TypedResults.BadRequest(problem);

        recipe.OutputItemId = dto.OutputItemId;
        recipe.LocationId = dto.LocationId;

        // Replace SkillRequirements as a set.
        var currentIds = recipe.SkillRequirements.Select(r => r.GameSkillId).ToHashSet();
        var desiredIds = skillIds.ToHashSet();

        foreach (var req in recipe.SkillRequirements.Where(r => !desiredIds.Contains(r.GameSkillId)).ToList())
        {
            recipe.SkillRequirements.Remove(req);
        }
        foreach (var sid in desiredIds.Where(s => !currentIds.Contains(s)))
        {
            recipe.SkillRequirements.Add(new CraftingSkillRequirement { CraftingRecipeId = id, GameSkillId = sid });
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
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

    private static async Task<ProblemDetails?> ValidateRequiredSkillsAsync(
        WorldDbContext db, int gameId, IReadOnlyList<int> gameSkillIds, CancellationToken ct)
    {
        if (gameSkillIds.Count == 0) return null;

        var existing = await db.GameSkills
            .Where(gs => gs.GameId == gameId && gameSkillIds.Contains(gs.Id))
            .Select(gs => gs.Id)
            .ToArrayAsync(ct);
        var missing = gameSkillIds.Except(existing).ToArray();
        if (missing.Length > 0)
        {
            return new ProblemDetails
            {
                Title = "Dovednosti nejsou ve hře dostupné.",
                Detail = $"Dovednosti s ID [{string.Join(", ", missing)}] nejsou v této hře dostupné.",
                Status = StatusCodes.Status400BadRequest
            };
        }

        return null;
    }

    private static CraftingRecipeDetailDto ToDetailDto(CraftingRecipe r) => new(
        r.Id, r.OutputItemId, r.OutputItem.Name,
        r.LocationId, r.Location?.Name, r.GameId,
        r.Ingredients.Select(i => new CraftingIngredientDto(i.ItemId, i.Item.Name, i.Quantity)).ToList(),
        r.BuildingRequirements.Select(br => new CraftingBuildingReqDto(br.BuildingId, br.Building.Name)).ToList())
    {
        RequiredSkillIds = r.SkillRequirements.Select(sr => sr.GameSkillId).ToList()
    };
}
