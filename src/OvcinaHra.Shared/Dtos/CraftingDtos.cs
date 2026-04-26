using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

// Issue #218 — GameId is now nullable on CraftingRecipe (catalog templates
// have no game). Existing /api/crafting consumers see null on catalog rows
// and treat them as "Šablona katalogu". The new /api/recipes surface uses
// the richer RecipeListDto / RecipeDetailDto records below.
public record CraftingRecipeListDto(int Id, int OutputItemId, string OutputItemName, int? LocationId, string? LocationName, int? GameId);

public record CraftingRecipeDetailDto(
    int Id, int OutputItemId, string OutputItemName,
    int? LocationId, string? LocationName, int? GameId,
    List<CraftingIngredientDto> Ingredients,
    List<CraftingBuildingReqDto> BuildingRequirements)
{
    public IReadOnlyList<int> RequiredSkillIds { get; init; } = [];

    /// <summary>
    /// Free-text note rendered after the ingredient list (issue #121).
    /// Example: "Byliny — 3× stejný druh". Capped server-side at 2000 chars.
    /// </summary>
    public string? IngredientNotes { get; init; }
}

public record CreateCraftingRecipeDto(
    // Issue #218 — Catalog templates use GameId = null. Existing per-game
    // /api/crafting POST callers continue to set a value as before.
    int? GameId,
    int OutputItemId,
    int? LocationId = null,
    IReadOnlyList<int>? RequiredSkillIds = null,
    string? IngredientNotes = null);

public record UpdateCraftingRecipeDto(
    int OutputItemId,
    int? LocationId = null,
    IReadOnlyList<int>? RequiredSkillIds = null,
    string? IngredientNotes = null);

public record CraftingIngredientDto(int ItemId, string ItemName, int Quantity);
public record AddCraftingIngredientDto(int ItemId, int Quantity = 1);

public record CraftingBuildingReqDto(int BuildingId, string BuildingName);
public record AddCraftingBuildingReqDto(int BuildingId);

// ────────────────────────────────────────────────────────────────────────
// Issue #218 — Recipes catalog (cookbook) — richer DTOs for the new
// /api/recipes surface. Tile/card/hero rendering powered by these.
// ────────────────────────────────────────────────────────────────────────

public record RecipeIngredientChipDto(int ItemId, string Name, string? ThumbnailUrl, int Quantity);
public record RecipeBuildingChipDto(int BuildingId, string Name);
public record RecipeSkillChipDto(int SkillId, string Name);

public record RecipeListDto(
    int Id,
    string? Name,
    // Title = Name when set, else OutputItem.Name. Convenience field for
    // formula-card headings that always need something to print.
    string Title,
    RecipeCategory Category,
    int? GameId,
    int? TemplateRecipeId,
    int OutputItemId,
    string OutputItemName,
    string? OutputItemThumbnailUrl,
    int OutputQuantity,
    int? LocationId,
    string? LocationName,
    IReadOnlyList<RecipeIngredientChipDto> Ingredients,
    IReadOnlyList<RecipeBuildingChipDto> Buildings,
    IReadOnlyList<RecipeSkillChipDto> Skills,
    string? IngredientNotes,
    // ForksCount: how many per-game forks point at this template. Drives
    // the Smazat-blocked muted pill on catalog templates. Always 0 on
    // per-game rows.
    int ForksCount = 0)
{
    [JsonIgnore] public string CategoryDisplay => Category.GetDisplayName();
}

public record RecipeDetailDto(
    int Id,
    string? Name,
    string Title,
    RecipeCategory Category,
    int? GameId,
    int? TemplateRecipeId,
    int OutputItemId,
    string OutputItemName,
    string? OutputItemThumbnailUrl,
    int OutputQuantity,
    int? LocationId,
    string? LocationName,
    IReadOnlyList<RecipeIngredientChipDto> Ingredients,
    IReadOnlyList<RecipeBuildingChipDto> Buildings,
    IReadOnlyList<RecipeSkillChipDto> Skills,
    string? IngredientNotes,
    int ForksCount = 0)
{
    [JsonIgnore] public string CategoryDisplay => Category.GetDisplayName();
}

public record CreateRecipeDto(
    string? Name,
    int OutputItemId,
    RecipeCategory Category = RecipeCategory.Ostatni,
    int? GameId = null,
    int? LocationId = null,
    int? TemplateRecipeId = null,
    string? IngredientNotes = null);

public record UpdateRecipeDto(
    string? Name,
    int OutputItemId,
    RecipeCategory Category,
    int? LocationId = null,
    // Skills are replaced as a set on update — pass current state to keep
    // unchanged. Null is treated as empty.
    IReadOnlyList<int>? RequiredSkillIds = null,
    string? IngredientNotes = null);

public record AddRecipeIngredientDto(int ItemId, int Quantity = 1);
public record AddRecipeBuildingDto(int BuildingId);
public record AddRecipeSkillDto(int GameSkillId);
