namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Building required to be already constructed before this Building can be
/// built (issue #142). Composite key on (BuildingRecipeId, RequiredBuildingId).
/// Analogous to CraftingBuildingRequirement on the Item side, just with the
/// "this is a prerequisite, not a workshop" naming.
/// </summary>
public class BuildingRecipePrerequisite
{
    public int BuildingRecipeId { get; set; }
    public int RequiredBuildingId { get; set; }

    public BuildingRecipe BuildingRecipe { get; set; } = null!;
    public Building RequiredBuilding { get; set; } = null!;
}
