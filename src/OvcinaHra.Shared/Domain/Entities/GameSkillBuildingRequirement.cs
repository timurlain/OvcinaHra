namespace OvcinaHra.Shared.Domain.Entities;

public class GameSkillBuildingRequirement
{
    public int GameSkillId { get; set; }
    public int BuildingId { get; set; }

    public GameSkill GameSkill { get; set; } = null!;
    public Building Building { get; set; } = null!;
}
