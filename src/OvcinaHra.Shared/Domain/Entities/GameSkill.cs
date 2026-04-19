using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class GameSkill
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int? TemplateSkillId { get; set; }

    public required string Name { get; set; }
    public SkillCategory Category { get; set; } = SkillCategory.Class;
    public PlayerClass? ClassRestriction { get; set; }
    public string? Effect { get; set; }
    public string? RequirementNotes { get; set; }
    public string? ImagePath { get; set; }

    public int XpCost { get; set; }
    public int? LevelRequirement { get; set; }

    public Game Game { get; set; } = null!;
    public Skill? Skill { get; set; }

    public ICollection<GameSkillBuildingRequirement> BuildingRequirements { get; set; } = [];
    public ICollection<CraftingSkillRequirement> CraftingRequirements { get; set; } = [];
}
