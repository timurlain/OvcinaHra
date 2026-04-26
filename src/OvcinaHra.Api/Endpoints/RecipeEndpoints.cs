using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

/// <summary>
/// Issue #218 — Recipes cookbook surface. Companion to the legacy
/// <see cref="CraftingEndpoints"/> /api/crafting routes (kept for backward
/// compatibility with existing Item Tvorba consumption). The /api/recipes
/// surface speaks the richer <see cref="RecipeListDto"/> + template-fork
/// vocabulary used by the new cookbook UI, /games/{gid}/recipes per-game
/// grid, and the eventual Tvorba-tab refresh.
/// </summary>
public static class RecipeEndpoints
{
    public static RouteGroupBuilder MapRecipeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/recipes").WithTags("Recipes");

        // Catalog (templates: GameId == null)
        group.MapGet("/", GetCatalog);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        // Per-game scope
        var gameGroup = routes.MapGroup("/api/games/{gameId:int}/recipes").WithTags("Recipes");
        gameGroup.MapGet("/", GetByGame);
        gameGroup.MapPost("/from-template/{templateId:int}", CopyFromTemplate);

        return group;
    }

    private static async Task<Ok<List<RecipeListDto>>> GetCatalog(WorldDbContext db, HttpContext http)
    {
        var rows = await db.CraftingRecipes
            .AsNoTracking()
            .Where(r => r.GameId == null)
            .OrderBy(r => r.Name ?? r.OutputItem.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Category,
                r.GameId,
                r.TemplateRecipeId,
                r.OutputItemId,
                OutputItemName = r.OutputItem.Name,
                OutputItemImagePath = r.OutputItem.ImagePath,
                r.LocationId,
                LocationName = r.Location != null ? r.Location.Name : null,
                Ingredients = r.Ingredients
                    .OrderBy(i => i.Item.Name)
                    .Select(i => new { i.ItemId, ItemName = i.Item.Name, ItemImagePath = i.Item.ImagePath, i.Quantity })
                    .ToList(),
                Buildings = r.BuildingRequirements
                    .OrderBy(br => br.Building.Name)
                    .Select(br => new { br.BuildingId, BuildingName = br.Building.Name })
                    .ToList(),
                Skills = r.SkillRequirements
                    .OrderBy(sr => sr.GameSkill.Name)
                    .Select(sr => new { sr.GameSkillId, SkillName = sr.GameSkill.Name })
                    .ToList(),
                r.IngredientNotes,
                ForksCount = db.CraftingRecipes.Count(f => f.TemplateRecipeId == r.Id)
            })
            .ToListAsync();

        return TypedResults.Ok(rows.Select(r => MapToListDto(http, r)).ToList());
    }

    private static async Task<Ok<List<RecipeListDto>>> GetByGame(int gameId, WorldDbContext db, HttpContext http)
    {
        var rows = await db.CraftingRecipes
            .AsNoTracking()
            .Where(r => r.GameId == gameId)
            .OrderBy(r => r.Name ?? r.OutputItem.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Category,
                r.GameId,
                r.TemplateRecipeId,
                r.OutputItemId,
                OutputItemName = r.OutputItem.Name,
                OutputItemImagePath = r.OutputItem.ImagePath,
                r.LocationId,
                LocationName = r.Location != null ? r.Location.Name : null,
                Ingredients = r.Ingredients
                    .OrderBy(i => i.Item.Name)
                    .Select(i => new { i.ItemId, ItemName = i.Item.Name, ItemImagePath = i.Item.ImagePath, i.Quantity })
                    .ToList(),
                Buildings = r.BuildingRequirements
                    .OrderBy(br => br.Building.Name)
                    .Select(br => new { br.BuildingId, BuildingName = br.Building.Name })
                    .ToList(),
                Skills = r.SkillRequirements
                    .OrderBy(sr => sr.GameSkill.Name)
                    .Select(sr => new { sr.GameSkillId, SkillName = sr.GameSkill.Name })
                    .ToList(),
                r.IngredientNotes,
                ForksCount = 0  // Per-game rows are forks themselves; ForksCount is only meaningful on catalog templates.
            })
            .ToListAsync();

        return TypedResults.Ok(rows.Select(r => MapToListDto(http, r)).ToList());
    }

    private static async Task<Results<Ok<RecipeDetailDto>, NotFound>> GetById(int id, WorldDbContext db, HttpContext http)
    {
        var r = await db.CraftingRecipes
            .Include(r => r.OutputItem)
            .Include(r => r.Location)
            .Include(r => r.Ingredients).ThenInclude(i => i.Item)
            .Include(r => r.BuildingRequirements).ThenInclude(br => br.Building)
            .Include(r => r.SkillRequirements).ThenInclude(sr => sr.GameSkill)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (r is null) return TypedResults.NotFound();

        // For catalog templates, count their per-game forks (drives Smazat-
        // blocked check + UI badge). For per-game rows, ForksCount stays 0.
        int forksCount = 0;
        if (r.GameId is null)
            forksCount = await db.CraftingRecipes.CountAsync(f => f.TemplateRecipeId == r.Id);

        return TypedResults.Ok(new RecipeDetailDto(
            r.Id, r.Name,
            Title: r.Name ?? r.OutputItem.Name,
            r.Category, r.GameId, r.TemplateRecipeId,
            r.OutputItemId, r.OutputItem.Name,
            OutputItemThumbnailUrl: ThumbForItem(http, r.OutputItem),
            OutputQuantity: 1,
            r.LocationId, r.Location?.Name,
            r.Ingredients.OrderBy(i => i.Item.Name).Select(i =>
                new RecipeIngredientChipDto(i.ItemId, i.Item.Name, ThumbForItem(http, i.Item), i.Quantity)).ToList(),
            r.BuildingRequirements.OrderBy(br => br.Building.Name).Select(br =>
                new RecipeBuildingChipDto(br.BuildingId, br.Building.Name)).ToList(),
            r.SkillRequirements.OrderBy(sr => sr.GameSkill.Name).Select(sr =>
                new RecipeSkillChipDto(sr.GameSkillId, sr.GameSkill.Name)).ToList(),
            r.IngredientNotes,
            forksCount));
    }

    private static async Task<Results<Created<RecipeDetailDto>, BadRequest<ProblemDetails>, NotFound>> Create(
        CreateRecipeDto dto, WorldDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!Enum.IsDefined(dto.Category))
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neznámá kategorie",
                Detail = $"Hodnota '{dto.Category}' není povolena.",
                Status = StatusCodes.Status400BadRequest
            });

        var itemExists = await db.Items.AnyAsync(i => i.Id == dto.OutputItemId, ct);
        if (!itemExists)
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Vybraný výstupní předmět neexistuje.",
                Detail = $"Předmět s ID {dto.OutputItemId} nebyl nalezen.",
                Status = StatusCodes.Status400BadRequest
            });

        var recipe = new CraftingRecipe
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim(),
            OutputItemId = dto.OutputItemId,
            Category = dto.Category,
            GameId = dto.GameId,
            LocationId = dto.LocationId,
            TemplateRecipeId = dto.TemplateRecipeId,
            IngredientNotes = string.IsNullOrWhiteSpace(dto.IngredientNotes) ? null : dto.IngredientNotes.Trim()
        };
        db.CraftingRecipes.Add(recipe);
        await db.SaveChangesAsync(ct);

        var detail = await GetById(recipe.Id, db, http);
        return detail.Result switch
        {
            Ok<RecipeDetailDto> ok when ok.Value is not null => TypedResults.Created($"/api/recipes/{recipe.Id}", ok.Value),
            _ => TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Internal projection failed",
                Status = StatusCodes.Status500InternalServerError
            })
        };
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<ProblemDetails>>> Update(
        int id, UpdateRecipeDto dto, WorldDbContext db, CancellationToken ct)
    {
        // Include SkillRequirements because we replace the set — same
        // pattern as CraftingEndpoints.Update.
        var recipe = await db.CraftingRecipes
            .Include(r => r.SkillRequirements)
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (recipe is null) return TypedResults.NotFound();

        if (!Enum.IsDefined(dto.Category))
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neznámá kategorie",
                Detail = $"Hodnota '{dto.Category}' není povolena.",
                Status = StatusCodes.Status400BadRequest
            });

        var itemExists = await db.Items.AnyAsync(i => i.Id == dto.OutputItemId, ct);
        if (!itemExists)
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Vybraný výstupní předmět neexistuje.",
                Detail = $"Předmět s ID {dto.OutputItemId} nebyl nalezen.",
                Status = StatusCodes.Status400BadRequest
            });

        var skillIds = dto.RequiredSkillIds?.Distinct().ToList() ?? [];
        // Skills only validated against the per-game GameSkill set when the
        // recipe IS per-game. Catalog templates (GameId == null) skip this
        // check; the from-template fork endpoint re-validates when copying.
        if (recipe.GameId is int gid && skillIds.Count > 0)
        {
            var existingSkills = await db.GameSkills
                .Where(gs => gs.GameId == gid && skillIds.Contains(gs.Id))
                .Select(gs => gs.Id)
                .ToArrayAsync(ct);
            var missing = skillIds.Except(existingSkills).ToArray();
            if (missing.Length > 0)
                return TypedResults.BadRequest(new ProblemDetails
                {
                    Title = "Dovednosti nejsou ve hře dostupné.",
                    Detail = $"Dovednosti s ID [{string.Join(", ", missing)}] nejsou v této hře.",
                    Status = StatusCodes.Status400BadRequest
                });
        }

        recipe.Name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim();
        recipe.OutputItemId = dto.OutputItemId;
        recipe.Category = dto.Category;
        recipe.LocationId = dto.LocationId;
        recipe.IngredientNotes = string.IsNullOrWhiteSpace(dto.IngredientNotes) ? null : dto.IngredientNotes.Trim();

        // Replace SkillRequirements as a set.
        var currentIds = recipe.SkillRequirements.Select(r => r.GameSkillId).ToHashSet();
        var desiredIds = skillIds.ToHashSet();
        foreach (var req in recipe.SkillRequirements.Where(r => !desiredIds.Contains(r.GameSkillId)).ToList())
            recipe.SkillRequirements.Remove(req);
        foreach (var sid in desiredIds.Where(s => !currentIds.Contains(s)))
            recipe.SkillRequirements.Add(new CraftingSkillRequirement { CraftingRecipeId = id, GameSkillId = sid });

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>>> Delete(int id, WorldDbContext db, CancellationToken ct)
    {
        var recipe = await db.CraftingRecipes.FindAsync([id], ct);
        if (recipe is null) return TypedResults.NotFound();

        // Block deletion of catalog templates that have per-game forks. Same
        // pattern as Skills + Quests catalog deletes.
        if (recipe.GameId is null)
        {
            var forksCount = await db.CraftingRecipes.CountAsync(f => f.TemplateRecipeId == id, ct);
            if (forksCount > 0)
                return TypedResults.Conflict(new ProblemDetails
                {
                    Title = "Šablonu nelze smazat — používá se v hrách.",
                    Detail = $"Tato šablona je zkopírována do {forksCount} her. Smažte kopie nejdříve.",
                    Status = StatusCodes.Status409Conflict
                });
        }

        db.CraftingRecipes.Remove(recipe);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<RecipeDetailDto>, NotFound, BadRequest<ProblemDetails>>> CopyFromTemplate(
        int gameId, int templateId, WorldDbContext db, HttpContext http, CancellationToken ct)
    {
        var template = await db.CraftingRecipes
            .Include(r => r.Ingredients)
            .Include(r => r.BuildingRequirements)
            .Include(r => r.SkillRequirements)
            .FirstOrDefaultAsync(r => r.Id == templateId, ct);
        if (template is null) return TypedResults.NotFound();

        if (template.GameId is not null)
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Zdrojový recept není šablona",
                Detail = "Forkovat lze pouze recepty z katalogu (GameId == null).",
                Status = StatusCodes.Status400BadRequest
            });

        var gameExists = await db.Games.AnyAsync(g => g.Id == gameId, ct);
        if (!gameExists) return TypedResults.NotFound();

        var fork = new CraftingRecipe
        {
            Name = template.Name,
            OutputItemId = template.OutputItemId,
            Category = template.Category,
            GameId = gameId,
            LocationId = template.LocationId,  // organizer can re-pin per game on edit
            TemplateRecipeId = template.Id,
            IngredientNotes = template.IngredientNotes
        };
        db.CraftingRecipes.Add(fork);
        await db.SaveChangesAsync(ct);

        // Deep-copy children. Skills are filtered to ones available in the
        // target game's GameSkill set — a template skill that doesn't exist
        // in this game gets dropped silently (organizer can re-add per game).
        foreach (var ing in template.Ingredients)
            db.CraftingIngredients.Add(new CraftingIngredient
            {
                CraftingRecipeId = fork.Id,
                ItemId = ing.ItemId,
                Quantity = ing.Quantity
            });

        foreach (var br in template.BuildingRequirements)
            db.CraftingBuildingRequirements.Add(new CraftingBuildingRequirement
            {
                CraftingRecipeId = fork.Id,
                BuildingId = br.BuildingId
            });

        var validSkillIds = await db.GameSkills
            .Where(gs => gs.GameId == gameId
                && template.SkillRequirements.Select(sr => sr.GameSkillId).Contains(gs.Id))
            .Select(gs => gs.Id)
            .ToListAsync(ct);
        foreach (var sid in validSkillIds)
            db.CraftingSkillRequirements.Add(new CraftingSkillRequirement
            {
                CraftingRecipeId = fork.Id,
                GameSkillId = sid
            });

        await db.SaveChangesAsync(ct);

        var detail = await GetById(fork.Id, db, http);
        return detail.Result switch
        {
            Ok<RecipeDetailDto> ok when ok.Value is not null => TypedResults.Created($"/api/recipes/{fork.Id}", ok.Value),
            _ => TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Internal projection failed",
                Status = StatusCodes.Status500InternalServerError
            })
        };
    }

    private static string? ThumbForItem(HttpContext http, Item item)
        => string.IsNullOrWhiteSpace(item.ImagePath)
            ? null
            : ImageEndpoints.ThumbUrl(http, "items", item.Id, "small");

    // Static helper because anonymous-type projections from EF land here as
    // an `object` in the C# compiler's view; dynamic dispatch on properties
    // would erase IntelliSense support. The shape is small enough to keep
    // the local-record verbosity in line.
    private static RecipeListDto MapToListDto(HttpContext http, dynamic r)
    {
        string outputItemName = (string)r.OutputItemName;
        string? outputItemImagePath = (string?)r.OutputItemImagePath;
        int outputItemId = (int)r.OutputItemId;
        string? thumb = string.IsNullOrWhiteSpace(outputItemImagePath)
            ? null
            : ImageEndpoints.ThumbUrl(http, "items", outputItemId, "small");

        var ingredients = new List<RecipeIngredientChipDto>();
        foreach (var i in r.Ingredients)
        {
            string? ingThumb = string.IsNullOrWhiteSpace((string?)i.ItemImagePath)
                ? null
                : ImageEndpoints.ThumbUrl(http, "items", (int)i.ItemId, "small");
            ingredients.Add(new RecipeIngredientChipDto((int)i.ItemId, (string)i.ItemName, ingThumb, (int)i.Quantity));
        }

        var buildings = new List<RecipeBuildingChipDto>();
        foreach (var b in r.Buildings)
            buildings.Add(new RecipeBuildingChipDto((int)b.BuildingId, (string)b.BuildingName));

        var skills = new List<RecipeSkillChipDto>();
        foreach (var s in r.Skills)
            skills.Add(new RecipeSkillChipDto((int)s.GameSkillId, (string)s.SkillName));

        return new RecipeListDto(
            (int)r.Id,
            (string?)r.Name,
            Title: (string?)r.Name ?? outputItemName,
            (RecipeCategory)r.Category,
            (int?)r.GameId,
            (int?)r.TemplateRecipeId,
            outputItemId,
            outputItemName,
            thumb,
            OutputQuantity: 1,
            (int?)r.LocationId,
            (string?)r.LocationName,
            ingredients,
            buildings,
            skills,
            (string?)r.IngredientNotes,
            (int)r.ForksCount);
    }
}
