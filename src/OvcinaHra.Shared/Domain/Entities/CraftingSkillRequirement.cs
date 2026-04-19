namespace OvcinaHra.Shared.Domain.Entities;

public class CraftingSkillRequirement
{
    public int CraftingRecipeId { get; set; }
    public int GameSkillId { get; set; }

    public CraftingRecipe CraftingRecipe { get; set; } = null!;
    public GameSkill GameSkill { get; set; } = null!;
}
