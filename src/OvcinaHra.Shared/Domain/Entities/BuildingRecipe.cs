namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Per-game construction recipe for a Building (issue #142). Mirrors the
/// shape of <see cref="CraftingRecipe"/> for Items but is a parallel entity
/// because: (1) the output is a Building, not an Item; (2) Buildings need
/// a money cost which doesn't apply to Item recipes; (3) keeping schemas
/// separate avoids polluting the catalog Item domain with Building-specific
/// nullables.
/// </summary>
public class BuildingRecipe
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int OutputBuildingId { get; set; }

    /// <summary>Optional money cost in groše (per-game currency).</summary>
    public int? MoneyCost { get; set; }

    /// <summary>
    /// Free-text annotation rendered after the ingredient list (mirrors
    /// CraftingRecipe.IngredientNotes from issue #121). Capped at 2000 chars.
    /// </summary>
    public string? IngredientNotes { get; set; }

    public Game Game { get; set; } = null!;
    public Building OutputBuilding { get; set; } = null!;
    public ICollection<BuildingRecipeIngredient> Ingredients { get; set; } = [];
    public ICollection<BuildingRecipePrerequisite> PrerequisiteBuildings { get; set; } = [];
    public ICollection<BuildingRecipeSkillRequirement> SkillRequirements { get; set; } = [];
}
