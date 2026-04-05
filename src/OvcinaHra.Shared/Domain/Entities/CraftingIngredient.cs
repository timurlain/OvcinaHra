namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingIngredient
{
    public int CraftingRecipeId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }

    public CraftingRecipe CraftingRecipe { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
