namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingRecipe
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int OutputItemId { get; set; }
    public int? LocationId { get; set; }

    public Game Game { get; set; } = null!;
    public Item OutputItem { get; set; } = null!;
    public Location? Location { get; set; }
    public ICollection<CraftingIngredient> Ingredients { get; set; } = [];
    public ICollection<CraftingBuildingRequirement> BuildingRequirements { get; set; } = [];
}
