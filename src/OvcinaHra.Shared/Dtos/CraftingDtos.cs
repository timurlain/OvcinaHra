namespace OvcinaHra.Shared.Dtos;

public record CraftingRecipeListDto(int Id, int OutputItemId, string OutputItemName, int? LocationId, string? LocationName, int GameId);

public record CraftingRecipeDetailDto(
    int Id, int OutputItemId, string OutputItemName,
    int? LocationId, string? LocationName, int GameId,
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
    int GameId,
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
