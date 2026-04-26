namespace OvcinaHra.Shared.Domain.Entities;

public class Building
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? ImagePath { get; set; }
    public int? LocationId { get; set; }
    public bool IsPrebuilt { get; set; }

    // Issue #208 — every building carries a "build recipe" pair: cost in
    // groše + sacred effect prose. CostMoney null/0 → "Zdarma" pill; Effect
    // null → muted "Bez efektu" placeholder. NOT to be conflated with
    // BuildingRecipe (construction-recipe entity from issue #142).
    public int? CostMoney { get; set; }
    public string? Effect { get; set; }

    public Location? Location { get; set; }
    public ICollection<GameBuilding> GameBuildings { get; set; } = [];
    public ICollection<CraftingBuildingRequirement> CraftingRequirements { get; set; } = [];

    // Issue #142 — building crafting recipes. "AsOutput" lists BuildingRecipes
    // whose OutputBuilding is this Building. "AsPrerequisite" covers the
    // inverse: other buildings whose recipe requires this one as a prerequisite.
    public ICollection<BuildingRecipe> BuildingRecipesAsOutput { get; set; } = [];
    public ICollection<BuildingRecipePrerequisite> BuildingRecipesAsPrerequisite { get; set; } = [];
}
