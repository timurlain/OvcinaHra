namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingRecipe
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int OutputItemId { get; set; }
    public int? LocationId { get; set; }

    // Free-text note rendered after the ingredient list (issue #121).
    // Example use: "Byliny — 3× stejný druh" — constraints on ingredient
    // combinations that can't be encoded in the structured ingredient rows.
    // Not tied to any one ingredient; belongs at the recipe level.
    public string? IngredientNotes { get; set; }

    public Game Game { get; set; } = null!;
    public Item OutputItem { get; set; } = null!;
    public Location? Location { get; set; }
    public ICollection<CraftingIngredient> Ingredients { get; set; } = [];
    public ICollection<CraftingBuildingRequirement> BuildingRequirements { get; set; } = [];
    public ICollection<CraftingSkillRequirement> SkillRequirements { get; set; } = [];
}
