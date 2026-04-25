namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Per-game skill required to construct this Building (issue #142). Composite
/// key on (BuildingRecipeId, GameSkillId) — same shape as CraftingSkillRequirement.
/// </summary>
public class BuildingRecipeSkillRequirement
{
    public int BuildingRecipeId { get; set; }
    public int GameSkillId { get; set; }

    public BuildingRecipe BuildingRecipe { get; set; } = null!;
    public GameSkill GameSkill { get; set; } = null!;
}
