namespace OvcinaHra.Shared.Domain.Entities;

public class GameSkill
{
    public int GameId { get; set; }
    public int SkillId { get; set; }
    public int XpCost { get; set; }
    public int? LevelRequirement { get; set; }

    public Game Game { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
