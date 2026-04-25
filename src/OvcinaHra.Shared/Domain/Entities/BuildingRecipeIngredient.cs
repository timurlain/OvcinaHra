namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Item required to construct a Building (issue #142). Composite key on
/// (BuildingRecipeId, ItemId) — same shape as CraftingIngredient.
/// </summary>
public class BuildingRecipeIngredient
{
    public int BuildingRecipeId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }

    public BuildingRecipe BuildingRecipe { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
