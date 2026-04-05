namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingBuildingRequirement
{
    public int CraftingRecipeId { get; set; }
    public int BuildingId { get; set; }

    public CraftingRecipe CraftingRecipe { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
