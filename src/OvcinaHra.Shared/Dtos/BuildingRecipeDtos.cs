namespace OvcinaHra.Shared.Dtos;

// Issue #142 — Building crafting cost. Mirrors CraftingDtos exactly except
// for `MoneyCost` (Building-only) and the prerequisite-vs-required-buildings
// renaming: on the Item side `BuildingRequirement` means "this Item is
// crafted at this Building"; on the Building side `Prerequisite` means
// "this Building requires that Building to already be constructed".

public record BuildingRecipeListDto(
    int Id, int OutputBuildingId, string OutputBuildingName, int GameId, int? MoneyCost);

public record BuildingRecipeDetailDto(
    int Id, int OutputBuildingId, string OutputBuildingName, int GameId,
    int? MoneyCost,
    List<BuildingRecipeIngredientDto> Ingredients,
    List<BuildingRecipePrerequisiteDto> PrerequisiteBuildings)
{
    public IReadOnlyList<int> RequiredSkillIds { get; init; } = [];

    /// <summary>
    /// Free-text annotation rendered after the ingredient list; mirrors
    /// CraftingRecipeDetailDto.IngredientNotes (issue #121). Capped at 2000
    /// chars server-side.
    /// </summary>
    public string? IngredientNotes { get; init; }
}

public record CreateBuildingRecipeDto(
    int GameId,
    int OutputBuildingId,
    int? MoneyCost = null,
    IReadOnlyList<int>? RequiredSkillIds = null,
    string? IngredientNotes = null);

public record UpdateBuildingRecipeDto(
    int OutputBuildingId,
    int? MoneyCost = null,
    IReadOnlyList<int>? RequiredSkillIds = null,
    string? IngredientNotes = null);

public record BuildingRecipeIngredientDto(int ItemId, string ItemName, int Quantity);
public record AddBuildingRecipeIngredientDto(int ItemId, int Quantity = 1);

public record BuildingRecipePrerequisiteDto(int BuildingId, string BuildingName);
public record AddBuildingRecipePrerequisiteDto(int BuildingId);
