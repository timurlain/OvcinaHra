namespace OvcinaHra.Shared.Domain.Entities;

public class SkillBuildingRequirement
{
    public int SkillId { get; set; }
    public int BuildingId { get; set; }

    public Skill Skill { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
