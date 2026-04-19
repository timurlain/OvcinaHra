using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Skill
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public PlayerClass? ClassRestriction { get; set; }
    public string? Effect { get; set; }
    public string? RequirementNotes { get; set; }
    public string? ImagePath { get; set; }

    public ICollection<SkillBuildingRequirement> BuildingRequirements { get; set; } = [];
    public ICollection<GameSkill> GameSkills { get; set; } = [];
}
