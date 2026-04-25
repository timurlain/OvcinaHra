using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

// Issue #142 — Building crafting cost. Mirrors CraftingEndpoints (the Item
// side) endpoint-for-endpoint. Kept as a sibling file so the two domains
// stay independent — see PR body for the duplication-as-follow-up note
// about extracting a shared editor/endpoint base.
public static class BuildingRecipeEndpoints
{
    public static RouteGroupBuilder MapBuildingRecipeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/building-recipes").WithTags("BuildingRecipes");

        group.MapGet("/by-game/{gameId:int}", GetByGame);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        group.MapPost("/{id:int}/ingredients", AddIngredient);
        group.MapDelete("/{id:int}/ingredients/{itemId:int}", RemoveIngredient);

        group.MapPost("/{id:int}/prerequisites", AddPrerequisite);
        group.MapDelete("/{id:int}/prerequisites/{buildingId:int}", RemovePrerequisite);

        return group;
    }

    private static async Task<Ok<List<BuildingRecipeListDto>>> GetByGame(int gameId, WorldDbContext db)
    {
        var recipes = await db.BuildingRecipes
            .Where(r => r.GameId == gameId)
            .Include(r => r.OutputBuilding)
            .OrderBy(r => r.OutputBuilding.Name)
            .Select(r => new BuildingRecipeListDto(
                r.Id, r.OutputBuildingId, r.OutputBuilding.Name, r.GameId, r.MoneyCost))
            .ToListAsync();
        return TypedResults.Ok(recipes);
    }

    private static async Task<Results<Ok<BuildingRecipeDetailDto>, NotFound>> GetById(int id, WorldDbContext db)
    {
        var r = await db.BuildingRecipes
            .Include(r => r.OutputBuilding)
            .Include(r => r.Ingredients).ThenInclude(i => i.Item)
            .Include(r => r.PrerequisiteBuildings).ThenInclude(p => p.RequiredBuilding)
            .Include(r => r.SkillRequirements)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (r is null) return TypedResults.NotFound();

        return TypedResults.Ok(ToDetailDto(r));
    }

    // Same 2000-char ceiling as CraftingRecipe.IngredientNotes (issue #121).
    private const int IngredientNotesMaxLength = 2000;

    private static async Task<Results<Created<BuildingRecipeDetailDto>, BadRequest<ProblemDetails>>> Create(
        CreateBuildingRecipeDto dto, WorldDbContext db, CancellationToken ct)
    {
        var skillIds = dto.RequiredSkillIds?.Distinct().ToList() ?? [];

        if (dto.MoneyCost is < 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neplatná cena",
                Detail = "Cena nesmí být záporná.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var problem = await ValidateRequiredSkillsAsync(db, dto.GameId, skillIds, ct);
        if (problem is not null) return TypedResults.BadRequest(problem);

        var ingredientNotes = dto.IngredientNotes?.Trim();
        if (!string.IsNullOrEmpty(ingredientNotes) && ingredientNotes.Length > IngredientNotesMaxLength)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Poznámka je příliš dlouhá",
                Detail = $"Poznámka k surovinám nesmí být delší než {IngredientNotesMaxLength} znaků.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var r = new BuildingRecipe
        {
            GameId = dto.GameId,
            OutputBuildingId = dto.OutputBuildingId,
            MoneyCost = dto.MoneyCost,
            IngredientNotes = string.IsNullOrWhiteSpace(ingredientNotes) ? null : ingredientNotes,
            SkillRequirements = skillIds
                .Select(sid => new BuildingRecipeSkillRequirement { GameSkillId = sid })
                .ToList()
        };
        db.BuildingRecipes.Add(r);
        await db.SaveChangesAsync(ct);

        // Reload with navs for the response.
        var created = await db.BuildingRecipes
            .Include(x => x.OutputBuilding)
            .Include(x => x.Ingredients).ThenInclude(i => i.Item)
            .Include(x => x.PrerequisiteBuildings).ThenInclude(p => p.RequiredBuilding)
            .Include(x => x.SkillRequirements)
            .FirstAsync(x => x.Id == r.Id, ct);

        return TypedResults.Created($"/api/building-recipes/{created.Id}", ToDetailDto(created));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>>> Update(
        int id, UpdateBuildingRecipeDto dto, WorldDbContext db, CancellationToken ct)
    {
        var recipe = await db.BuildingRecipes
            .Include(r => r.SkillRequirements)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (recipe is null) return TypedResults.NotFound();

        var buildingExists = await db.Buildings.AnyAsync(b => b.Id == dto.OutputBuildingId, ct);
        if (!buildingExists)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Vybraná budova neexistuje.",
                Detail = $"Budova s ID {dto.OutputBuildingId} nebyla nalezena.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (dto.MoneyCost is < 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neplatná cena",
                Detail = "Cena nesmí být záporná.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var skillIds = dto.RequiredSkillIds?.Distinct().ToList() ?? [];
        var problem = await ValidateRequiredSkillsAsync(db, recipe.GameId, skillIds, ct);
        if (problem is not null) return TypedResults.BadRequest(problem);

        var ingredientNotes = dto.IngredientNotes?.Trim();
        if (!string.IsNullOrEmpty(ingredientNotes) && ingredientNotes.Length > IngredientNotesMaxLength)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Poznámka je příliš dlouhá",
                Detail = $"Poznámka k surovinám nesmí být delší než {IngredientNotesMaxLength} znaků.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        recipe.OutputBuildingId = dto.OutputBuildingId;
        recipe.MoneyCost = dto.MoneyCost;
        recipe.IngredientNotes = string.IsNullOrWhiteSpace(ingredientNotes) ? null : ingredientNotes;

        // Replace SkillRequirements as a set.
        var currentIds = recipe.SkillRequirements.Select(r => r.GameSkillId).ToHashSet();
        var desiredIds = skillIds.ToHashSet();

        foreach (var req in recipe.SkillRequirements.Where(r => !desiredIds.Contains(r.GameSkillId)).ToList())
        {
            recipe.SkillRequirements.Remove(req);
        }
        foreach (var sid in desiredIds.Where(s => !currentIds.Contains(s)))
        {
            recipe.SkillRequirements.Add(new BuildingRecipeSkillRequirement { BuildingRecipeId = id, GameSkillId = sid });
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, WorldDbContext db)
    {
        var r = await db.BuildingRecipes.FindAsync(id);
        if (r is null) return TypedResults.NotFound();
        db.BuildingRecipes.Remove(r);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, Conflict>> AddIngredient(int id, AddBuildingRecipeIngredientDto dto, WorldDbContext db)
    {
        if (await db.BuildingRecipeIngredients.AnyAsync(bi => bi.BuildingRecipeId == id && bi.ItemId == dto.ItemId))
            return TypedResults.Conflict();
        db.BuildingRecipeIngredients.Add(new BuildingRecipeIngredient
        {
            BuildingRecipeId = id,
            ItemId = dto.ItemId,
            Quantity = dto.Quantity
        });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/building-recipes/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemoveIngredient(int id, int itemId, WorldDbContext db)
    {
        var bi = await db.BuildingRecipeIngredients.FindAsync(id, itemId);
        if (bi is null) return TypedResults.NotFound();
        db.BuildingRecipeIngredients.Remove(bi);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created, Conflict, BadRequest<ProblemDetails>>> AddPrerequisite(int id, AddBuildingRecipePrerequisiteDto dto, WorldDbContext db)
    {
        // Guard against a recipe declaring its OWN output building as a
        // prerequisite — the schema doesn't enforce it but it's nonsense.
        var recipe = await db.BuildingRecipes.FindAsync(id);
        if (recipe is null) return TypedResults.Conflict();
        if (recipe.OutputBuildingId == dto.BuildingId)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neplatný požadavek",
                Detail = "Budova nemůže být požadavkem sama na sebe.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (await db.BuildingRecipePrerequisites.AnyAsync(p => p.BuildingRecipeId == id && p.RequiredBuildingId == dto.BuildingId))
            return TypedResults.Conflict();
        db.BuildingRecipePrerequisites.Add(new BuildingRecipePrerequisite
        {
            BuildingRecipeId = id,
            RequiredBuildingId = dto.BuildingId
        });
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/building-recipes/{id}");
    }

    private static async Task<Results<NoContent, NotFound>> RemovePrerequisite(int id, int buildingId, WorldDbContext db)
    {
        var p = await db.BuildingRecipePrerequisites.FindAsync(id, buildingId);
        if (p is null) return TypedResults.NotFound();
        db.BuildingRecipePrerequisites.Remove(p);
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

    /// <summary>
    /// Compact recipe summary for the BuildingList by-game grid column.
    /// Format: "3× ingrediencí · 50 zlaťáků · 1 dovednost · 2 budov".
    /// Returns null when the recipe is empty (no ingredients, no money,
    /// no skills, no prereqs) so the column shows "—" instead of noise.
    /// </summary>
    internal static string? BuildRecipeSummary(BuildingRecipe r)
    {
        var parts = new List<string>();
        if (r.Ingredients.Count > 0)
            parts.Add($"{r.Ingredients.Sum(i => i.Quantity)}× ingrediencí");
        if (r.MoneyCost is > 0)
            parts.Add($"{r.MoneyCost} zlaťáků");
        if (r.SkillRequirements.Count > 0)
            parts.Add($"{r.SkillRequirements.Count} dovednost");
        if (r.PrerequisiteBuildings.Count > 0)
            parts.Add($"{r.PrerequisiteBuildings.Count} budov");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static BuildingRecipeDetailDto ToDetailDto(BuildingRecipe r) => new(
        r.Id, r.OutputBuildingId, r.OutputBuilding.Name, r.GameId,
        r.MoneyCost,
        r.Ingredients.Select(i => new BuildingRecipeIngredientDto(i.ItemId, i.Item.Name, i.Quantity)).ToList(),
        r.PrerequisiteBuildings.Select(p => new BuildingRecipePrerequisiteDto(p.RequiredBuildingId, p.RequiredBuilding.Name)).ToList())
    {
        RequiredSkillIds = r.SkillRequirements.Select(sr => sr.GameSkillId).ToList(),
        IngredientNotes = r.IngredientNotes
    };
}
