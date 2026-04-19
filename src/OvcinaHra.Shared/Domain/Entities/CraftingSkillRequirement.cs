namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingSkillRequirement
{
    public int CraftingRecipeId { get; set; }
    public int SkillId { get; set; }

    public CraftingRecipe CraftingRecipe { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
