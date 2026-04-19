namespace OvcinaHra.Shared.Domain.Entities;

public class PersonalQuestSkillReward
{
    public int PersonalQuestId { get; set; }
    public int SkillId { get; set; }

    public PersonalQuest PersonalQuest { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
